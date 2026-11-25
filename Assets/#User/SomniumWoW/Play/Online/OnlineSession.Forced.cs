// iteration 11 - owner motion (proposal-motion.md 6.2.3): every movement the server forces on the player, online.
// Self splines (SMSG_MONSTER_MOVE for our own guid: MoveSplineInit.cpp:124 sends it to the mover too) -> ForcedMoveView Path / Fleeing /
// Confused (Charge, Intercept, Intervene, Feral Charge, Death Grip, pulls, fear, polymorph wander, a creature's charm); knockbacks and
// Disengage (SMSG_MOVE_KNOCK_BACK + ack + the falling sender + MSG_MOVE_FALL_LAND); lost control (SMSG_CLIENT_CONTROL_UPDATE, our guid
// only - other guids are the vehicle owner's); a player charmer's relayed MSG_MOVE_* with our guid; spline roots and the SPLINE_ flag
// variants; PlayerState Rooted / Stunned / ControlLost / SlowFall / WaterWalking / Hovering; the facing flush before casts and swings.
using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Motion;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const uint UnitFlagStunned = 0x00040000, UnitFlagConfused = 0x00400000, UnitFlagFleeing = 0x00800000;   // UnitDefines.h:275, 279, 280
        // ours (logged once in InitForced)
        const double ForcedTimeoutSlack = 2.0;            // s: a path not reported done by the view ends this long after its duration
        const double KnockbackMaxSeconds = 10.0;          // s: a knockback that never lands is landed anyway
        const double ForcedSpellWindow = 2.0;             // s: our own cast this recent names a forced move (Charge ...)
        const float FacingFlushMin = 0.05f;               // rad: the facing is flushed before a cast / swing when it differs more than this
        const float SettleMinMs = 150f;                   // ms: the shortest settle path after a server stop
        const int CharmPathMinMs = 100, CharmPathMaxMs = 1000;   // ms: a charmer's relayed position is reached over this long
        const float FacingAssistArc = 100f * (float)Math.PI / 180f;   // D-M5: the target must be this close to the headset's yaw

        int _forcedId;
        double _forcedStartAt;
        bool _controlUpdateLost, _splineRooted, _selfFleeing, _selfConfused;
        int _lastOwnCast;
        double _lastOwnCastAt = -100;
        double _kbStart;
        float _kbVz, _kbSin, _kbCos, _kbXY;
        int _facingFlushes;
        bool _relayIgnoredLogged, _assistArmed, _transportSplineLogged;
        int _controlBits = -1;                            // Rooted 1 | Stunned 2 | ControlLost 4 | SlowFall 8 | WaterWalking 16 | Hovering 32

        partial void InitForced()
        {
            AddMove(Op.SMSG_MOVE_KNOCK_BACK, OnKnockBack);
            _world.On(Op.SMSG_CLIENT_CONTROL_UPDATE, OnClientControlUpdate, this);   // pets register their own handler for other guids
            AddMove(Op.SMSG_SPLINE_MOVE_ROOT, p => OnSplineRoot(p, true));
            AddMove(Op.SMSG_SPLINE_MOVE_UNROOT, p => OnSplineRoot(p, false));
            AddMove(Op.SMSG_SPLINE_MOVE_FEATHER_FALL, p => OnSplineFlag(p, MoveFlags.FallingSlow, true));
            AddMove(Op.SMSG_SPLINE_MOVE_NORMAL_FALL, p => OnSplineFlag(p, MoveFlags.FallingSlow, false));
            AddMove(Op.SMSG_SPLINE_MOVE_SET_HOVER, p => OnSplineFlag(p, MoveFlags.Hover, true));
            AddMove(Op.SMSG_SPLINE_MOVE_UNSET_HOVER, p => OnSplineFlag(p, MoveFlags.Hover, false));
            AddMove(Op.SMSG_SPLINE_MOVE_WATER_WALK, p => OnSplineFlag(p, MoveFlags.WaterWalking, true));
            AddMove(Op.SMSG_SPLINE_MOVE_LAND_WALK, p => OnSplineFlag(p, MoveFlags.WaterWalking, false));
            // AC packs these into SMSG_MULTIPLE_MOVES at every map entry (Player.cpp:11859-11900); OnlineSession.Mounts.cs registered them
            // with the world only, so the dispatch table gets them too (the exact arguments of Mounts.cs:32-37)
            _moveHandlers[Op.SMSG_MOVE_SET_HOVER] = p => OnFlagChange(p, Op.CMSG_MOVE_HOVER_ACK, MoveFlags.Hover, true, true, "hover");
            _moveHandlers[Op.SMSG_MOVE_UNSET_HOVER] = p => OnFlagChange(p, Op.CMSG_MOVE_HOVER_ACK, MoveFlags.Hover, false, true, "hover");
            _moveHandlers[Op.SMSG_MOVE_FEATHER_FALL] = p => OnFlagChange(p, Op.CMSG_MOVE_FEATHER_FALL_ACK, MoveFlags.FallingSlow, true, true, "feather fall");
            _moveHandlers[Op.SMSG_MOVE_NORMAL_FALL] = p => OnFlagChange(p, Op.CMSG_MOVE_FEATHER_FALL_ACK, MoveFlags.FallingSlow, false, true, "feather fall");
            _moveHandlers[Op.SMSG_MOVE_WATER_WALK] = p => OnFlagChange(p, Op.CMSG_MOVE_WATER_WALK_ACK, MoveFlags.WaterWalking, true, true, "water walk");
            _moveHandlers[Op.SMSG_MOVE_LAND_WALK] = p => OnFlagChange(p, Op.CMSG_MOVE_WATER_WALK_ACK, MoveFlags.WaterWalking, false, true, "water walk");
            WowLog.Info("Online: forced movement - ours: a path not finished " + ForcedTimeoutSlack + " s after its duration ends, a knockback lands after at most " +
                        KnockbackMaxSeconds + " s, our cast within " + ForcedSpellWindow + " s names a move, facing flushed before casts / swings past " + FacingFlushMin +
                        " rad, settle paths >= " + SettleMinMs + " ms, a charmer's positions reached in " + CharmPathMinMs + "-" + CharmPathMaxMs + " ms, sector hysteresis " +
                        (SectorHysteresis * 180f / Math.PI).ToString("0") + " deg, VR facing assist " + (FacingAssist ? "on" : "off") + ".");
        }

        // ------------------------------------------------------------------ IWowSession

        /// <summary>The view finished the forced move id at pos (WoW coordinates). Knockback -> MSG_MOVE_FALL_LAND (fall time since the
        /// launch: the real client's landing, AC applies fall damage there, MovementHandler.cpp:633-636); Path / Fleeing / Confused ->
        /// nothing to send (the walker resumes from pos). Taxi ends with FinishTaxi. Ignored when id is not the current move.</summary>
        public void ForcedMoveDone(int id, WowPos pos)
        {
            var m = _player.ForcedMove;
            if (m == null || m.Id != id || m.Kind == ForcedMoveKind.Taxi) return;
            if (m.Kind == ForcedMoveKind.Knockback && _world != null && !_world.IsClosed && _player.Guid != 0)
            {
                uint fallMs = (uint)Math.Max(0.0, (_now - _kbStart) * 1000.0);
                SendMove(Op.MSG_MOVE_FALL_LAND, 0u, pos, pos.O, fallMs, 0, 0, 0, 0);
                WowLog.Info("Online: knockback landed at " + pos + " after " + fallMs + " ms (MSG_MOVE_FALL_LAND)");
            }
            else
            {
                _sentPos = pos;
                _sentFlags = 0;
            }
            EndForcedMove(null);
        }

        // ------------------------------------------------------------------ publishing

        /// <summary>Publishes m as PlayerState.ForcedMove (a running one is replaced: Ended "replaced").</summary>
        void PublishForcedMove(ForcedMoveView m, int unused)
        {
            var old = _player.ForcedMove;
            if (old != null) RaiseForcedEnded(old, "replaced");
            m.Id = ++_forcedId;
            _player.ForcedMove = m;
            _player.ForcedMoveCounter++;
            _forcedStartAt = _now - m.AgeAtPublish;
            WowPos at = m.Kind == ForcedMoveKind.Knockback ? m.Start : EndOf(m);
            _events.Enqueue(new GameEvent { Type = GameEventType.ForcedMoveStarted, SourceGuid = _player.Guid, Amount = (int)m.Kind, SpellId = m.SpellId, Pos = at, Text = m.Source });
        }

        /// <summary>Ends the current forced move (why null = it ran its course).</summary>
        void EndForcedMove(string why)
        {
            var m = _player.ForcedMove;
            if (m == null) return;
            _player.ForcedMove = null;
            RaiseForcedEnded(m, why);
        }

        void RaiseForcedEnded(ForcedMoveView m, string why)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.ForcedMoveEnded, SourceGuid = _player.Guid, Amount = (int)m.Kind, SpellId = m.SpellId, Text = why });
            if (why != null && why != "replaced") WowLog.Info("Online: forced " + m.Kind + " " + m.Id + " ended (" + why + ")");
        }

        static WowPos EndOf(ForcedMoveView m)
        {
            if (m.Points == null || m.Points.Length < 3) return m.Start;
            int n = m.Points.Length;
            return new WowPos(m.Points[n - 3], m.Points[n - 2], m.Points[n - 1], float.IsNaN(m.EndFacing) ? 0f : m.EndFacing);
        }

        /// <summary>A self spline (MoveSplineInit::Launch for the player) published as Path / Fleeing / Confused.</summary>
        void PublishSelfPath(SplinePath s, double age, ForcedMoveKind kind)
        {
            int n = s.Count;
            if (n == 0) return;
            var m = new ForcedMoveView
            {
                Kind = kind, Points = s.Pts.ToArray(), DurationMs = Math.Max(0, s.DurationMs), Smooth = s.Smooth,
                Source = "SMSG_MONSTER_MOVE spline " + s.Id, AgeAtPublish = (float)Math.Max(0, age), SpellId = NameForcedSpell()
            };
            if ((s.Flags & SplineFlags.Parabolic) != 0) { m.ParabolicAccel = s.VerticalAccel; m.ParabolicStartMs = s.EffectStartMs; }
            float lx = s.Pts[n * 3 - 3], ly = s.Pts[n * 3 - 2];
            if (s.FacingKind == 1) m.EndFacing = ForcedMoveMath.Normalize(s.FacingAngle);
            else if (s.FacingKind == 3) m.EndFacing = ForcedMoveMath.Normalize((float)Math.Atan2(s.FacingY - ly, s.FacingX - lx));
            else if (s.FacingKind == 2) m.FacingGuid = s.FacingTarget;
            PublishForcedMove(m, 0);
            double len = ForcedMoveMath.PathLength(m);
            WowLog.Info("Online: forced " + kind + " - spline " + s.Id + ", " + n + " points, " + len.ToString("0.0") + " yd in " + m.DurationMs + " ms (" +
                        (m.DurationMs > 0 ? (len / (m.DurationMs / 1000.0)).ToString("0.0") : "-") + " yd/s), spell " + m.SpellId +
                        ((s.Flags & SplineFlags.Parabolic) != 0 ? ", arc " + s.VerticalAccel.ToString("0.0") + " yd/s2 from " + s.EffectStartMs + " ms" : "") +
                        ", from (" + s.Pts[0].ToString("0.0") + ", " + s.Pts[1].ToString("0.0") + ", " + s.Pts[2].ToString("0.0") + ") to (" + lx.ToString("0.0") + ", " + ly.ToString("0.0") + ", " +
                        s.Pts[n * 3 - 1].ToString("0.0") + ")");
        }

        /// <summary>The spell behind a forced move: our own cast within 2 s whose Spell.dbc has a movement effect (96 CHARGE, 41 / 42
        /// JUMP / JUMP_DEST, 124 / 145 PULL_TOWARDS, 149 CHARGE_DEST), else 0.</summary>
        int NameForcedSpell()
        {
            if (_lastOwnCast == 0 || _now - _lastOwnCastAt > ForcedSpellWindow) return 0;
            var si = Db != null ? Db.Find(_lastOwnCast) : null;
            if (si == null) return 0;
            foreach (var e in si.Effects)
            {
                if (e == null) continue;
                int t = e.Effect;
                if (t == 96 || t == 41 || t == 42 || t == 124 || t == 145 || t == 149) return _lastOwnCast;
            }
            return 0;
        }

        // ------------------------------------------------------------------ self splines (Movement.cs OnMonsterMove)

        void OnSelfMonsterMove(SplinePath s, float sx, float sy, float sz, uint id, ulong transportGuid)
        {
            if (transportGuid != 0)
            {
                // SMSG_MONSTER_MOVE_TRANSPORT for us on a ship: the points are in the transport's frame (TransportPathTransform,
                // MoveSplineInit.cpp:240-247) -> world with the transport's pose now
                WowPos tp;
                if (!TryTransportPose(transportGuid, out tp))
                {
                    if (!_transportSplineLogged) { _transportSplineLogged = true; WowLog.Warn("Online: a self spline on transport " + transportGuid.ToString("X16") + " whose pose is unknown - ignored (logged once)"); }
                    return;
                }
                WowPos w0 = ForcedMoveMath.PassengerPosition(sx, sy, sz, 0f, tp);
                sx = w0.X; sy = w0.Y; sz = w0.Z;
                if (s != null)
                    for (int i = 0; i + 2 < s.Pts.Count; i += 3)
                    {
                        WowPos w = ForcedMoveMath.PassengerPosition(s.Pts[i], s.Pts[i + 1], s.Pts[i + 2], 0f, tp);
                        s.Pts[i] = w.X; s.Pts[i + 1] = w.Y; s.Pts[i + 2] = w.Z;
                    }
                if (s != null && s.FacingKind == 1) s.FacingAngle = ForcedMoveMath.Normalize(s.FacingAngle + tp.O);
            }
            if (s == null)
            {
                // MonsterMoveStop: a running path settles on the server's stop point
                var cur = _player.ForcedMove;
                if (cur == null || cur.Kind == ForcedMoveKind.Taxi || cur.Kind == ForcedMoveKind.Knockback) return;
                WowPos a = _haveAvatar ? _avatarPos : _player.Pos;
                float dx = sx - a.X, dy = sy - a.Y, dz = sz - a.Z;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                float speed = _player.RunSpeed > 0.1f ? _player.RunSpeed : 7f;
                int ms = (int)Math.Max(SettleMinMs, dist / speed * 1000f);
                PublishForcedMove(new ForcedMoveView { Kind = cur.Kind, Points = new[] { a.X, a.Y, a.Z, sx, sy, sz }, DurationMs = ms, Source = "SMSG_MONSTER_MOVE stop (settle)" }, 0);
                WowLog.Info("Online: forced move stopped by the server - settling " + dist.ToString("0.0") + " yd in " + ms + " ms");
                return;
            }
            // a Flying self spline is the taxi (FlightPathMovementGenerator: SetFly, WaypointMovementGenerator.cpp:689-718) unless a fear /
            // charm holds us; Taxi.cs starts it, or holds it until TAXI_FLIGHT arrives (login mid-flight)
            if ((s.Flags & SplineFlags.Flying) != 0 && (!_controlUpdateLost || _player.OnTaxi))
            {
                TaxiOnFlyingSpline(s, sx, sy, sz, 0);
                return;
            }
            var kind = _selfFleeing ? ForcedMoveKind.Fleeing : _selfConfused ? ForcedMoveKind.Confused : ForcedMoveKind.Path;
            PublishSelfPath(s, 0, kind);
        }

        // ------------------------------------------------------------------ knockback (Unit::KnockbackFrom, Unit.cpp:14999-15036)

        /// <summary>SMSG_MOVE_KNOCK_BACK: packed guid, uint32 counter, float vcos, vsin, speedXY, -speedZ (Unit.cpp:15023-15029).
        /// Ack CMSG_MOVE_KNOCK_BACK_ACK: packed guid, uint32 counter, MovementInfo with FALLING + the jump (MovementHandler.cpp:830-844).</summary>
        void OnKnockBack(WowPacket p)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            float vcos = p.F32(), vsin = p.F32(), sxy = p.F32(), vz = p.F32();
            if (g != _player.Guid || _world == null) return;
            WowPos pos = _haveAvatar ? _avatarPos : _player.Pos;
            var mi = OwnMovementInfo((AckFlags() & ~MoveFlags.MaskMoving) | MoveFlags.Falling, pos, pos.O);
            mi.FallTime = 0;
            mi.JumpZSpeed = vz; mi.JumpSin = vsin; mi.JumpCos = vcos; mi.JumpXYSpeed = sxy;
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            mi.Write(w);
            _world.Send(Op.CMSG_MOVE_KNOCK_BACK_ACK, w);
            _sentPos = new WowPos(pos.X, pos.Y, pos.Z, mi.O);
            _sentFlags = MoveFlags.Falling;
            _lastSendAt = _now;
            _airborne = false;
            _kbStart = _now;
            _kbVz = vz; _kbSin = vsin; _kbCos = vcos; _kbXY = sxy;
            var m = new ForcedMoveView
            {
                Kind = ForcedMoveKind.Knockback, Start = pos, DirCos = vcos, DirSin = vsin, SpeedXY = sxy, SpeedUp = -vz,
                SlowFall = (_stateFlags & MoveFlags.FallingSlow) != 0, Source = "SMSG_MOVE_KNOCK_BACK counter " + counter, SpellId = NameForcedSpell()
            };
            PublishForcedMove(m, 0);
            WowLog.Info("Online: knockback - counter " + counter + ", direction (" + vcos.ToString("0.00") + ", " + vsin.ToString("0.00") + "), " + sxy.ToString("0.0") +
                        " yd/s out, " + (-vz).ToString("0.0") + " yd/s up" + (m.SlowFall ? ", slow fall" : "") + " - acked");
        }

        /// <summary>The falling sender during a knockback (Movement.cs TickOwnMovement): heartbeats with FALLING and the launch's jump
        /// data every 0.5 s; a knockback that never lands is landed after KnockbackMaxSeconds.</summary>
        void TickKnockbackSender()
        {
            var m = _player.ForcedMove;
            if (m == null) return;
            double t = _now - _kbStart;
            if (t > KnockbackMaxSeconds)
            {
                WowLog.Warn("Online: the knockback did not land within " + KnockbackMaxSeconds + " s - landing it where the avatar is");
                ForcedMoveDone(m.Id, _avatarPos);
                return;
            }
            if (_now - _lastSendAt >= HeartbeatSeconds)
                SendMove(Op.MSG_MOVE_HEARTBEAT, MoveFlags.Falling, _avatarPos, _avatarPos.O, (uint)(t * 1000.0), _kbVz, _kbSin, _kbCos, _kbXY);
        }

        // ------------------------------------------------------------------ control

        /// <summary>SMSG_CLIENT_CONTROL_UPDATE (Player::SetClientControl, Player.cpp:13178-13181): packed guid, uint8 allowMove. Our guid
        /// only; any other guid is the vehicle owner's (possess / vehicles, their own handler on the same opcode).</summary>
        void OnClientControlUpdate(WowPacket p)
        {
            ulong g = p.PackedGuid();
            byte allow = p.U8();
            if (g != _player.Guid) return;
            bool lost = allow == 0;
            if (lost != _controlUpdateLost) WowLog.Info("Online: control of the character " + (lost ? "LOST (fear, confuse, charm)" : "given back") + " (SMSG_CLIENT_CONTROL_UPDATE)");
            _controlUpdateLost = lost;
            RefreshControl();
        }

        /// <summary>Movement.cs OnOtherMove: a MSG_MOVE_* with OUR guid = a player charmer moves us (HandleMovementOpcodes relays the
        /// charmer's packets to the mover itself: MovementHandler.cpp:413, Player.cpp:5852-5855).</summary>
        void OnRelayedSelfMove(WowPacket p)
        {
            var mi = new MovementInfo();
            try { mi.Read(p); }
            catch (Exception) { return; }
            if (!_player.ControlLost)
            {
                if (!_relayIgnoredLogged) { _relayIgnoredLogged = true; WowLog.Info("Online: a relayed move of our own guid while in control - ignored (logged once)"); }
                return;
            }
            WowPos a = _haveAvatar ? _avatarPos : _player.Pos;
            float dx = mi.X - a.X, dy = mi.Y - a.Y, dz = mi.Z - a.Z;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float speed = _player.RunSpeed > 0.1f ? _player.RunSpeed : 7f;
            int ms = (int)Math.Min(CharmPathMaxMs, Math.Max(CharmPathMinMs, dist / speed * 1000f));
            PublishForcedMove(new ForcedMoveView
            {
                Kind = ForcedMoveKind.Path, Points = new[] { a.X, a.Y, a.Z, mi.X, mi.Y, mi.Z }, DurationMs = ms,
                EndFacing = ForcedMoveMath.Normalize(mi.O), Source = "relayed " + Op.Name(p.Opcode) + " (charmed)"
            }, 0);
        }

        /// <summary>SMSG_SPLINE_MOVE_ROOT / UNROOT (Unit::SetRooted for a unit that is not client controlled, Unit.cpp:14130-14135): packed guid.</summary>
        void OnSplineRoot(WowPacket p, bool root)
        {
            ulong g = p.PackedGuid();
            if (g == _player.Guid)
            {
                _splineRooted = root;
                RefreshRooted();
                return;
            }
            var o = Mover(g);
            if (o == null || o.Move == null) return;
            if (root) o.Move.Flags |= MoveFlags.Root; else o.Move.Flags &= ~MoveFlags.Root;   // their extrapolation stops (UpdateMovers)
        }

        /// <summary>SMSG_SPLINE_MOVE_FEATHER_FALL / NORMAL_FALL / SET_HOVER / UNSET_HOVER / WATER_WALK / LAND_WALK: packed guid, no ack.</summary>
        void OnSplineFlag(WowPacket p, uint flag, bool set)
        {
            ulong g = p.PackedGuid();
            if (g == _player.Guid)
            {
                if (set) _stateFlags |= flag; else _stateFlags &= ~flag;
                RefreshMoveAuras();
                return;
            }
            var o = Mover(g);
            if (o == null || o.Move == null) return;
            if (set) o.Move.Flags |= flag; else o.Move.Flags &= ~flag;
        }

        /// <summary>OnlineSession.Objects.cs RefreshSelf hook: UNIT_FIELD_FLAGS stunned / fleeing / confused / taxi.</summary>
        partial void RefreshSelfMotion(OnlineObject o)
        {
            uint flags = o.U32(UF.UNIT_FIELD_FLAGS);
            _player.Stunned = (flags & UnitFlagStunned) != 0;
            _player.StandState = o.Byte(UF.UNIT_FIELD_BYTES_1, 0);   // states fix 2026-10-04: our stand state (Unit::SetStandState writes UNIT_FIELD_BYTES_1 byte 0) for the avatar poses
            _selfFleeing = (flags & UnitFlagFleeing) != 0;
            _selfConfused = (flags & UnitFlagConfused) != 0;
            TaxiSelfFlags(flags);
            RefreshRooted();
        }

        void RefreshRooted()
        {
            _player.Rooted = _rooted || _splineRooted || _player.Stunned;
            RaiseControlChanged();
        }

        void RefreshControl()
        {
            _player.ControlLost = _controlUpdateLost || _player.OnTaxi;
            RaiseControlChanged();
        }

        /// <summary>Mounts.cs OnFlagChange and the SPLINE_ variants: PlayerState SlowFall / WaterWalking / Hovering from our flags.</summary>
        void RefreshMoveAuras()
        {
            _player.SlowFall = (_stateFlags & MoveFlags.FallingSlow) != 0;
            _player.WaterWalking = (_stateFlags & MoveFlags.WaterWalking) != 0;
            _player.Hovering = (_stateFlags & MoveFlags.Hover) != 0;
            RaiseControlChanged();
        }

        void RaiseControlChanged()
        {
            var p = _player;
            int bits = (p.Rooted ? 1 : 0) | (p.Stunned ? 2 : 0) | (p.ControlLost ? 4 : 0) | (p.SlowFall ? 8 : 0) | (p.WaterWalking ? 16 : 0) | (p.Hovering ? 32 : 0);
            if (bits == _controlBits) return;
            bool first = _controlBits < 0;
            _controlBits = bits;
            if (first && bits == 0) return;
            _events.Enqueue(new GameEvent { Type = GameEventType.ControlChanged, SourceGuid = p.Guid, Amount = bits & 7 });
            WowLog.Info("Online: movement state - " + (p.Rooted ? "rooted " : "") + (p.Stunned ? "stunned " : "") + (p.ControlLost ? "control-lost " : "") + (p.SlowFall ? "slow-fall " : "") +
                        (p.WaterWalking ? "water-walking " : "") + (p.Hovering ? "hovering " : "") + (bits == 0 ? "free" : ""));
        }

        // ------------------------------------------------------------------ facing (6.2.9) and the casts

        /// <summary>spellfx's Spells.cs SpCast / Combat.cs StartAttack: right before CMSG_CAST_SPELL / CMSG_ATTACKSWING the server gets
        /// the facing the avatar has now (AC checks HasInArc against our last accepted orientation: Spell.cpp:7390-7391,
        /// PlayerUpdates.cpp:183). Nothing while the server moves us or a root holds us (AC drops those packets: MovementHandler.cpp:606-609).</summary>
        partial void FlushFacing(ulong targetGuid)
        {
            if (!_haveAvatar || _world == null || _world.IsClosed || _holdUntil >= 0 || _player.Guid == 0) return;
            if (_player.ForcedMove != null || _player.OnTaxi || _player.ControlLost || _rooted || _player.Rooted || _player.Stunned) return;
            if (_motion.Driven || _vehMover != 0 || _vehSeat != null || _airborne) return;
            float o = _moving ? _walkO : _avatarPos.O;
            if (FacingAssist && _assistArmed && targetGuid != 0)
            {
                _assistArmed = false;
                var t = Find(targetGuid);
                if (t != null)
                {
                    float to = (float)Math.Atan2(t.Pos.Y - _avatarPos.Y, t.Pos.X - _avatarPos.X);
                    if (Math.Abs(AngleDiff(to, _avatarPos.O)) < FacingAssistArc)
                    {
                        o = NormalizeO(to);
                        WowLog.Info("Online: VR facing assist - the cast faces " + t.Name + " (" + (AngleDiff(to, _avatarPos.O) * 180f / Math.PI).ToString("0") + " deg from the headset)");
                    }
                }
            }
            float turned = AngleDiff(o, _sentPos.O);
            if (Math.Abs(turned) <= FacingFlushMin) return;
            uint flags = _moving ? _sentFlags & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight) : 0u;
            SendMove(Op.MSG_MOVE_SET_FACING, flags, _avatarPos, o, 0, 0, 0, 0, 0);
            if (++_facingFlushes <= 3) WowLog.Info("Online: facing flushed before a " + (targetGuid != 0 ? "cast / swing at " + targetGuid.ToString("X") : "cast") + " - turned " + (turned * 180f / Math.PI).ToString("0") + " deg");
        }

        /// <summary>spellfx's Spells.cs SpCast: the last own cast (names a following forced move: our Charge).</summary>
        partial void NoteOwnCast(int spellId)
        {
            _lastOwnCast = spellId;
            _lastOwnCastAt = _now;
        }

        partial void TickForced(float dt)
        {
            var m = _player.ForcedMove;
            if (m != null && (m.Kind == ForcedMoveKind.Path || m.Kind == ForcedMoveKind.Fleeing || m.Kind == ForcedMoveKind.Confused) &&
                _now - _forcedStartAt > m.DurationMs / 1000.0 + ForcedTimeoutSlack)
            {
                WowLog.Info("Online: forced " + m.Kind + " " + m.Id + " not reported done " + ForcedTimeoutSlack + " s after its " + m.DurationMs + " ms - ended (timeout)");
                if (_haveAvatar) { _sentPos = _avatarPos; }
                _sentFlags = 0;
                EndForcedMove("timeout");
            }
            if (FacingAssist && !_assistArmed)
            {
                // D-M5: a "Target needs to be in front of you." / "You are facing the wrong way!" this frame arms the next flush
                foreach (var e in _events)
                {
                    if (e.Type != GameEventType.Error || e.Text == null) continue;
                    if (e.Text == "Target needs to be in front of you." || e.Text == "You are facing the wrong way!") { _assistArmed = true; break; }
                }
            }
        }
    }
}
