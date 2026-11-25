using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Motion;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Movement both ways: others (create blocks, SMSG_MONSTER_MOVE splines, MSG_MOVE_*), ours (MSG_MOVE_* from SetPlayerPosition as
    // AzerothCore MovementHandler reads them), forced speed/root acks, near and far teleports.
    // Iteration 11 (owner motion, proposal-motion.md 6.2.2, with proposal-pets.md H3 a-g and proposal-world.md hook 2 merged): self splines
    // go to OnlineSession.Forced.cs / Taxi.cs, SMSG_MONSTER_MOVE_TRANSPORT to Transport.cs (game-object transports) or Vehicle.cs (units),
    // the sender stops while the server moves us, carries MOVEMENTFLAG_ONTRANSPORT on a transport, and keeps the facing true (6.2.9).
    public sealed partial class OnlineSession
    {
        const float HeartbeatSeconds = 0.5f;           // the real client heartbeats every 500 ms while moving
        const float ExtrapolateMaxSeconds = 1.0f;       // others: keep extrapolating a moving player at most this long past its last packet
        const double Gravity = 19.29110527038574;       // MovementUtil.cpp
        // iteration 11 (motion 6.2.9), ours: the facing sector keeps its value while the travel stays within 22.5 deg + this of its centre
        const float SectorHysteresis = 8f * (float)Math.PI / 180f;
        const float TransportMovingSpeed = 0.5f;        // yd/s relative to the deck: a passenger moves (ours)

        readonly Dictionary<ushort, Action<WowPacket>> _moveHandlers = new Dictionary<ushort, Action<WowPacket>>();

        static readonly ushort[] RelayedMoves =
        {
            Op.MSG_MOVE_START_FORWARD, Op.MSG_MOVE_START_BACKWARD, Op.MSG_MOVE_STOP, Op.MSG_MOVE_START_STRAFE_LEFT, Op.MSG_MOVE_START_STRAFE_RIGHT,
            Op.MSG_MOVE_STOP_STRAFE, Op.MSG_MOVE_JUMP, Op.MSG_MOVE_START_TURN_LEFT, Op.MSG_MOVE_START_TURN_RIGHT, Op.MSG_MOVE_STOP_TURN,
            Op.MSG_MOVE_START_PITCH_UP, Op.MSG_MOVE_START_PITCH_DOWN, Op.MSG_MOVE_STOP_PITCH, Op.MSG_MOVE_SET_RUN_MODE, Op.MSG_MOVE_SET_WALK_MODE,
            Op.MSG_MOVE_TELEPORT, Op.MSG_MOVE_FALL_LAND, Op.MSG_MOVE_START_SWIM, Op.MSG_MOVE_STOP_SWIM, Op.MSG_MOVE_SET_FACING, Op.MSG_MOVE_SET_PITCH,
            Op.MSG_MOVE_ROOT, Op.MSG_MOVE_UNROOT, Op.MSG_MOVE_HEARTBEAT, Op.MSG_MOVE_KNOCK_BACK, Op.MSG_MOVE_HOVER, Op.MSG_MOVE_FEATHER_FALL,
            Op.MSG_MOVE_WATER_WALK, Op.MSG_MOVE_START_ASCEND, Op.MSG_MOVE_STOP_ASCEND, Op.MSG_MOVE_START_DESCEND, Op.MSG_MOVE_GRAVITY_CHNG,
            Op.MSG_MOVE_UPDATE_CAN_FLY, Op.MSG_MOVE_UPDATE_CAN_TRANSITION_BETWEEN_SWIM_AND_FLY, Op.MSG_MOVE_SET_COLLISION_HGT
        };

        // MSG_MOVE_SET_*_SPEED (relayed acks: MovementInfo + float) and SMSG_SPLINE_SET_*_SPEED (packed guid + float), by UnitMoveType
        static readonly ushort[] MsgSetSpeed = { Op.MSG_MOVE_SET_WALK_SPEED, Op.MSG_MOVE_SET_RUN_SPEED, Op.MSG_MOVE_SET_RUN_BACK_SPEED, Op.MSG_MOVE_SET_SWIM_SPEED,
            Op.MSG_MOVE_SET_SWIM_BACK_SPEED, Op.MSG_MOVE_SET_TURN_RATE, Op.MSG_MOVE_SET_FLIGHT_SPEED, Op.MSG_MOVE_SET_FLIGHT_BACK_SPEED, Op.MSG_MOVE_SET_PITCH_RATE };
        static readonly ushort[] SplineSetSpeed = { Op.SMSG_SPLINE_SET_WALK_SPEED, Op.SMSG_SPLINE_SET_RUN_SPEED, Op.SMSG_SPLINE_SET_RUN_BACK_SPEED, Op.SMSG_SPLINE_SET_SWIM_SPEED,
            Op.SMSG_SPLINE_SET_SWIM_BACK_SPEED, Op.SMSG_SPLINE_SET_TURN_RATE, Op.SMSG_SPLINE_SET_FLIGHT_SPEED, Op.SMSG_SPLINE_SET_FLIGHT_BACK_SPEED, Op.SMSG_SPLINE_SET_PITCH_RATE };
        // SMSG_FORCE_*_SPEED_CHANGE -> CMSG_FORCE_*_SPEED_CHANGE_ACK (HandleForceSpeedChangeAck's switch), by UnitMoveType
        static readonly ushort[] ForceSpeed = { Op.SMSG_FORCE_WALK_SPEED_CHANGE, Op.SMSG_FORCE_RUN_SPEED_CHANGE, Op.SMSG_FORCE_RUN_BACK_SPEED_CHANGE, Op.SMSG_FORCE_SWIM_SPEED_CHANGE,
            Op.SMSG_FORCE_SWIM_BACK_SPEED_CHANGE, Op.SMSG_FORCE_TURN_RATE_CHANGE, Op.SMSG_FORCE_FLIGHT_SPEED_CHANGE, Op.SMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE, Op.SMSG_FORCE_PITCH_RATE_CHANGE };
        static readonly ushort[] ForceSpeedAck = { Op.CMSG_FORCE_WALK_SPEED_CHANGE_ACK, Op.CMSG_FORCE_RUN_SPEED_CHANGE_ACK, Op.CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK, Op.CMSG_FORCE_SWIM_SPEED_CHANGE_ACK,
            Op.CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK, Op.CMSG_FORCE_TURN_RATE_CHANGE_ACK, Op.CMSG_FORCE_FLIGHT_SPEED_CHANGE_ACK, Op.CMSG_FORCE_FLIGHT_BACK_SPEED_CHANGE_ACK, Op.CMSG_FORCE_PITCH_RATE_CHANGE_ACK };

        // our own movement state
        readonly float[] _mySpeeds = (float[])MoveType.Base.Clone();
        WowPos _avatarPos, _sentPos;
        bool _haveAvatar, _moving, _airborne, _rooted;
        double _lastSendAt = -10, _airStart, _lastAvatarAt;
        float _vz, _moveDir;
        double _holdUntil = -1;                        // after a teleport: ignore the avatar until it reaches _holdPos (or time runs out)
        WowPos _holdPos;
        int _movesSent, _teleports;
        // iteration 11 (motion): the facing-true walker (6.2.9) and the passenger frame on a transport (6.2.4)
        int _walkSector = int.MinValue;                // the quantized travel sector of the last moving packet, MinValue = none
        float _walkO;                                  // the orientation the walker sends while moving (quantized facing)
        WowPos _lastLocal, _sentLocal;                 // our position in the transport's frame (now / at the last packet)
        bool _haveLocal, _haveSentLocal;
        float _localSpeed;

        void RegisterMovementHandlers()
        {
            foreach (ushort op in RelayedMoves) AddMove(op, OnOtherMove);
            for (int t = 0; t < MoveType.Count; t++)
            {
                int type = t;
                AddMove(MsgSetSpeed[t], p => OnMsgSetSpeed(p, type));
                AddMove(SplineSetSpeed[t], p => OnSplineSetSpeed(p, type));
                AddMove(ForceSpeed[t], p => OnForceSpeed(p, type));
            }
            AddMove(Op.SMSG_MONSTER_MOVE, p => OnMonsterMove(p, false));
            AddMove(Op.SMSG_MONSTER_MOVE_TRANSPORT, p => OnMonsterMove(p, true));
            AddMove(Op.SMSG_SPLINE_MOVE_SET_WALK_MODE, p => OnSplineWalkMode(p, true));
            AddMove(Op.SMSG_SPLINE_MOVE_SET_RUN_MODE, p => OnSplineWalkMode(p, false));
            AddMove(Op.SMSG_FORCE_MOVE_ROOT, p => OnForceRoot(p, true));
            AddMove(Op.SMSG_FORCE_MOVE_UNROOT, p => OnForceRoot(p, false));
            AddMove(Op.MSG_MOVE_TELEPORT_ACK, OnTeleportAck);
            AddMove(Op.SMSG_TRANSFER_PENDING, OnTransferPending);
            AddMove(Op.SMSG_NEW_WORLD, OnNewWorld);
            AddMove(Op.SMSG_TRANSFER_ABORTED, OnTransferAborted);
            _world.On(Op.SMSG_MULTIPLE_MOVES, OnMultipleMoves, this);
        }

        void AddMove(ushort op, Action<WowPacket> h)
        {
            _moveHandlers[op] = h;
            _world.On(op, h, this);
        }

        // ------------------------------------------------------------------ others: state from the server

        /// <summary>Lead fix (2026-09-27): after a server path (start, stop or end) the unit's MovementInfo stands at that spot and is
        /// not moving, so the dead reckoning in UpdateMovers never replays an old create-block or MSG_MOVE position. The next MSG_MOVE_*
        /// of a player replaces it as before.</summary>
        void SettleMove(OnlineObject o, float x, float y, float z, float ori)
        {
            var mi = o.Move;
            if (mi == null) return;
            mi.X = x; mi.Y = y; mi.Z = z; mi.O = ori;
            mi.Flags &= ~(MoveFlags.MaskMoving | MoveFlags.MaskTurning);
            o.MoveTime = _now;
        }

        void SetEntityPos(OnlineObject o, float x, float y, float z, float ori)
        {
            o.X = x; o.Y = y; o.Z = z; o.O = ori; o.HasPos = true;
            if (o.State != null) o.State.Pos = new WowPos(x, y, z, ori);
        }

        void ApplyMovementBlock(OnlineObject o, MovementBlock m)
        {
            if (m == null) return;
            if (m.Info != null)
            {
                o.Move = m.Info;
                o.MoveTime = _now;
                o.TransportGuid = m.Info.TransportGuid;
                // iteration 11 (motion 6.2.4 / pets H3f): passengers of a game-object transport (incl. our arrival on a ship) / of a vehicle
                if (IsGoTransportGuid(m.Info.TransportGuid) || o.TransportGuid == 0) NoteTransportPassenger(o, m.Info);
                if (!IsGoTransportGuid(m.Info.TransportGuid)) VehOnPassengerInfo(o, m.Info);
                Array.Copy(m.Speeds, o.Speeds, MoveType.Count);
                if (o == _self) { Array.Copy(m.Speeds, _mySpeeds, MoveType.Count); CopySpeedsToPlayer(); }
                else ApplyOtherMoveState(o, m.Info, 0);
            }
            if (m.HasPosition && o != _self) SetEntityPos(o, m.X, m.Y, m.Z, m.O);
            if (m.VehicleId != 0 && o.State != null) o.State.VehicleId = (int)m.VehicleId;   // iteration 11 (pets H3g)
            if ((m.Flags & UpdateType.FlagTransport) != 0 && o.TypeId == TypeId.GameObject) NoteTransportProgress(o, m);   // iteration 11 (motion 6.2.4)
            if (m.Spline != null && o != _self)
            {
                m.Spline.StartTime = _now - m.SplineTimePassedMs / 1000.0;
                o.Spline = m.Spline.Count > 0 ? m.Spline : null;
            }
            // Lead fix (2026-09-27): only players are dead-reckoned from their MovementInfo (their clients send MSG_MOVE_*);
            // a creature's movement comes as server paths, so a create block without a usable path leaves it standing where it is.
            if (o != _self && o.Spline == null && m.HasPosition && o.State != null && o.State.Kind != EntityKind.Player)
                SettleMove(o, m.X, m.Y, m.Z, m.O);
            if (m.HasRotation && o.State != null)
            {
                float qx, qy, qz, qw;
                MovementBlock.UnpackRotation(m.PackedRotation, out qx, out qy, out qz, out qw);
                o.State.RotX = qx; o.State.RotY = qy; o.State.RotZ = qz; o.State.RotW = qw;
            }
            if (o.State != null && m.Info != null)
            {
                o.State.Moving = o.Spline != null || (m.Info.Flags & MoveFlags.MaskMoving) != 0;
                o.State.Running = (m.Info.Flags & MoveFlags.Walking) == 0;
            }
        }

        OnlineObject Mover(ulong guid)
        {
            OnlineObject o;
            if (!_objects.TryGetValue(guid, out o) || o == _self || guid == _player.Guid) return null;
            return o;
        }

        /// <summary>Another unit's MSG_MOVE_* (WorldSession::WriteMovementInfo: packed guid + MovementInfo, relayed by HandleMovementOpcodes).</summary>
        void OnOtherMove(WowPacket p)
        {
            ulong g = p.PackedGuid();
            if (g != 0 && g == _player.Guid) { OnRelayedSelfMove(p); return; }   // iteration 11 (motion 6.2.3): a player charmer moves us
            var o = Mover(g);
            if (o == null) return;
            var mi = new MovementInfo();
            mi.Read(p);
            o.Move = mi;
            o.MoveTime = _now;
            o.Spline = null;
            o.TransportGuid = mi.TransportGuid;
            if (IsGoTransportGuid(mi.TransportGuid) || o.TransportGuid == 0) NoteTransportPassenger(o, mi);   // iteration 11 (motion)
            if (!IsGoTransportGuid(mi.TransportGuid)) VehOnPassengerInfo(o, mi);                              // iteration 11 (pets H3f)
            if (!IsTransportPassenger(o)) SetEntityPos(o, mi.X, mi.Y, mi.Z, mi.O);
            if (o.State != null)
            {
                o.State.Moving = (mi.Flags & MoveFlags.MaskMoving) != 0;
                o.State.Running = (mi.Flags & MoveFlags.Walking) == 0;
                // lead 2026-10-03: jumps of other players (WoW clients send MSG_MOVE_JUMP with FALLING + the jump speeds, then
                // MSG_MOVE_FALL_LAND) - the view plays JumpStart / Jump / JumpEnd (or JumpLandRun) from these
                if (p.Opcode == Op.MSG_MOVE_JUMP) o.State.JumpCounter++;
                o.State.Airborne = (mi.Flags & MoveFlags.Falling) != 0;
                o.Landed = false;
            }
            ApplyOtherMoveState(o, mi, p.Opcode);
        }

        void OnMsgSetSpeed(WowPacket p, int type)
        {
            ulong g = p.PackedGuid();
            var mi = new MovementInfo();
            mi.Read(p);
            float speed = p.F32();
            var o = Mover(g);
            if (o == null) return;
            o.Speeds[type] = speed;
            o.Move = mi;
            o.MoveTime = _now;
            if (!IsTransportPassenger(o)) SetEntityPos(o, mi.X, mi.Y, mi.Z, mi.O);
            ApplyOtherMoveState(o, mi, p.Opcode);
        }

        void OnSplineSetSpeed(WowPacket p, int type)
        {
            ulong g = p.PackedGuid();
            float speed = p.F32();
            var o = Mover(g);
            if (o != null) o.Speeds[type] = speed;
        }

        void OnSplineWalkMode(WowPacket p, bool walk)
        {
            var o = Mover(p.PackedGuid());
            if (o == null || o.Move == null) return;
            if (walk) o.Move.Flags |= MoveFlags.Walking; else o.Move.Flags &= ~MoveFlags.Walking;
        }

        /// <summary>
        /// SMSG_MONSTER_MOVE (MoveSplineInit::Launch): packed guid, [TRANSPORT variant: packed transport guid, int8 seat], then
        /// PacketBuilder::WriteMonsterMove (see SplinePath.ReadMonsterMove). Iteration 11: our own guid -> the forced move / taxi
        /// (motion) or a vehicle seat (pets); a game-object transport -> its passenger (motion); a unit transport -> a vehicle (pets).
        /// </summary>
        void OnMonsterMove(WowPacket p, bool transport)
        {
            ulong g = p.PackedGuid();
            ulong tGuid = 0; sbyte tSeat = -1;
            if (transport) { tGuid = p.PackedGuid(); tSeat = (sbyte)p.U8(); }
            float sx, sy, sz; uint id;
            SplinePath s = SplinePath.ReadMonsterMove(p, out sx, out sy, out sz, out id);
            bool vehicleSpline = s != null && (s.Flags & (SplineFlags.TransportEnter | SplineFlags.TransportExit)) != 0;   // pets H3e
            if (g != 0 && g == _player.Guid)
            {
                if (vehicleSpline || (transport && !IsGoTransportGuid(tGuid))) { VehOnSelfSpline(tGuid, tSeat, s); return; }   // (pets)
                OnSelfMonsterMove(s, sx, sy, sz, id, transport ? tGuid : 0UL);                                              // motion
                return;
            }
            var o = Mover(g);
            if (o == null) return;
            if (transport)
            {
                if (IsGoTransportGuid(tGuid)) TransportMonsterMove(o, tGuid, s, sx, sy, sz);   // iteration 11 (motion): a passenger on a ship
                else VehOnPassengerSpline(o, tGuid, tSeat, s);                                 // iteration 11 (pets H3f): a vehicle passenger
                return;
            }
            ClearPassengerSpline(o);
            // Lead fix (2026-09-27, the user's first real login: "the ones walking around are teleporting to different places"):
            // a server path supersedes the unit's last MovementInfo. A unit first seen while moving carries FORWARD (+ SPLINE_ENABLED)
            // in its create block (AC MoveSplineInit::Launch sets MOVEMENTFLAG_FORWARD); left as it was, UpdateMovers dead-reckoned
            // from that OLD spot every time a path ended - the unit snapped back toward where it was first seen, then jumped to the
            // next path's start. So: the MovementInfo follows the path (start here, the end in UpdateMovers) and is never "moving".
            SettleMove(o, sx, sy, sz, o.O);
            if (s == null)                                   // MonsterMoveStop
            {
                o.Spline = null;
                SetEntityPos(o, sx, sy, sz, o.O);
                if (o.State != null) o.State.Moving = false;
                return;
            }
            s.StartTime = _now;
            o.Spline = s;
            if (o.State != null)
            {
                o.State.Moving = true;
                float v = s.DurationMs > 0 ? s.Length / (s.DurationMs / 1000f) : 0f;
                o.State.Running = v > o.Speeds[MoveType.Walk] * 1.25f;   // AC picks run/walk speed server-side; the client only sees the duration
            }
            SetEntityPos(o, sx, sy, sz, o.O);
        }

        /// <summary>Per frame: splines advance, moving players are extrapolated between their packets.</summary>
        void UpdateMovers()
        {
            foreach (var o in _objects.Values)
            {
                if (o.State == null || o == _self) continue;
                if (IsTransportPassenger(o)) continue;       // iteration 11 (motion): Transport.cs places them with the transport each Tick
                var s = o.Spline;
                if (s != null)
                {
                    float x, y, z, dir;
                    s.Evaluate(_now, out x, out y, out z, out dir);
                    float ori = o.O;
                    bool done = s.Finished(_now);
                    if (done)
                    {
                        if (s.FacingKind == 1) ori = s.FacingAngle;
                        else if (s.FacingKind == 3) ori = (float)Math.Atan2(s.FacingY - y, s.FacingX - x);
                        else if (s.FacingKind == 2) { var t = Find(s.FacingTarget); if (t != null) ori = (float)Math.Atan2(t.Pos.Y - y, t.Pos.X - x); }
                        else if ((s.Flags & SplineFlags.OrientationFixed) == 0 && s.Count > 1) ori = dir;
                        o.Spline = null;
                        o.State.Moving = false;
                        SettleMove(o, x, y, z, ori);            // lead fix: the unit stays at the path's end (no dead reckoning from an old spot)
                    }
                    else if ((s.Flags & (SplineFlags.OrientationFixed | SplineFlags.Falling)) == 0) ori = (s.Flags & SplineFlags.OrientationInversed) != 0 ? -dir : dir;
                    SetEntityPos(o, x, y, z, ori);
                    continue;
                }
                var mi = o.Move;
                if (mi == null || (mi.Flags & (MoveFlags.MaskMoving | MoveFlags.MaskTurning)) == 0 || (mi.Flags & MoveFlags.Root) != 0) continue;
                float dt = (float)Math.Min(_now - o.MoveTime, ExtrapolateMaxSeconds);
                if (dt <= 0f) continue;
                Extrapolate(o, mi, dt);
            }
        }

        /// <summary>Dead reckoning from the last MovementInfo like the real client: direction flags x the unit's speed, turning, jumps.</summary>
        void Extrapolate(OnlineObject o, MovementInfo mi, float dt)
        {
            float ori = mi.O;
            if ((mi.Flags & MoveFlags.Left) != 0) ori += o.Speeds[MoveType.TurnRate] * dt;
            if ((mi.Flags & MoveFlags.Right) != 0) ori -= o.Speeds[MoveType.TurnRate] * dt;
            float x = mi.X, y = mi.Y, z = mi.Z;
            if ((mi.Flags & MoveFlags.Falling) != 0)
            {
                // jump: horizontal along (cos, sin) at xyspeed; vertical from zspeed (negative = up) under gravity
                x += mi.JumpCos * mi.JumpXYSpeed * dt;
                y += mi.JumpSin * mi.JumpXYSpeed * dt;
                z -= (float)(mi.JumpZSpeed * dt + 0.5 * Gravity * dt * dt);
                // the ground under the arc (the hint at the jump's start height + 2 yd, or the arc's own height when it rose higher)
                float g = _groundZ != null ? _groundZ(x, y, Math.Max(mi.Z, z) + 2f) : float.NaN;
                bool down = mi.JumpZSpeed * dt + 0.5 * Gravity * dt * dt > 0;   // past the top of the arc
                if (!float.IsNaN(g) && z < g)
                {
                    z = g;
                    // lead 2026-10-03: landed on our side before its MSG_MOVE_FALL_LAND arrives - the view lands it now (no hanging in the air)
                    if (down && o.State != null && !o.Landed) { o.State.Airborne = false; o.Landed = true; }
                }
            }
            else
            {
                float fx = 0f, fy = 0f;
                if ((mi.Flags & MoveFlags.Forward) != 0) fx += 1f;
                if ((mi.Flags & MoveFlags.Backward) != 0) fx -= 1f;
                if ((mi.Flags & MoveFlags.StrafeLeft) != 0) fy += 1f;
                if ((mi.Flags & MoveFlags.StrafeRight) != 0) fy -= 1f;
                float len = (float)Math.Sqrt(fx * fx + fy * fy);
                if (len > 0f)
                {
                    bool swim = (mi.Flags & MoveFlags.Swimming) != 0, fly = (mi.Flags & MoveFlags.Flying) != 0;
                    bool back = (mi.Flags & MoveFlags.Backward) != 0;
                    int type = fly ? (back ? MoveType.FlightBack : MoveType.Flight) : swim ? (back ? MoveType.SwimBack : MoveType.Swim)
                             : (mi.Flags & MoveFlags.Walking) != 0 ? MoveType.Walk : back ? MoveType.RunBack : MoveType.Run;
                    float d = o.Speeds[type] * dt / len;
                    float c = (float)Math.Cos(mi.O), sn = (float)Math.Sin(mi.O);
                    x += (fx * c - fy * sn) * d;                   // WoW: X north, Y west, orientation counter-clockwise from +X
                    y += (fx * sn + fy * c) * d;
                    if (fly || swim)
                    {
                        z += (float)Math.Sin(mi.Pitch) * fx * o.Speeds[type] * dt / len;
                    }
                    else if (_groundZ != null)
                    {
                        float g = _groundZ(x, y, mi.Z + 2f);
                        if (!float.IsNaN(g) && Math.Abs(g - mi.Z) < 4f) z = g;
                    }
                }
                else if ((mi.Flags & (MoveFlags.Flying | MoveFlags.Swimming)) != 0 && (mi.Flags & (MoveFlags.Ascending | MoveFlags.Descending)) != 0)
                {
                    // iteration 7 (P2): a pure climb / descent (no direction key) - the real client's rate is not in AC: ours, 0.6 x flight speed
                    float rate = o.Speeds[(mi.Flags & MoveFlags.Flying) != 0 ? MoveType.Flight : MoveType.Swim] * OthersAscendFactor;
                    z += ((mi.Flags & MoveFlags.Ascending) != 0 ? rate : -rate) * dt;
                }
            }
            SetEntityPos(o, x, y, z, ori);
        }

        // ------------------------------------------------------------------ ours: to the server

        public void SetPlayerPosition(WowPos pos, bool moving)
        {
            double now = _now;
            if (_holdUntil >= 0)
            {
                float hx = pos.X - _holdPos.X, hy = pos.Y - _holdPos.Y;
                if (hx * hx + hy * hy > 15f * 15f && now < _holdUntil) return;   // the avatar has not been moved to the teleport target yet
                if (hx * hx + hy * hy > 15f * 15f)
                    WowLog.Warn("Online world: the avatar is " + Math.Sqrt(hx * hx + hy * hy).ToString("0") + " yd from where the server put the character " + _holdPos + " - sending the avatar's position from now on.");
                _holdUntil = -1;
                _haveAvatar = false;
            }
            // iteration 11 (motion 6.2.4): on a transport the avatar's world displacement includes the ship's: walking and direction are
            // measured in the transport's frame (a passenger standing still on a moving ship does not move)
            WowPos tp = default(WowPos);
            bool onTransport = _transportGuid != 0 && TryTransportPose(_transportGuid, out tp);
            if (onTransport)
            {
                WowPos local = ForcedMoveMath.PassengerOffset(pos.X, pos.Y, pos.Z, pos.O, tp);
                float dtl = (float)(now - _lastAvatarAt);
                if (_haveLocal && dtl > 1e-4f)
                {
                    float lx = local.X - _lastLocal.X, ly = local.Y - _lastLocal.Y;
                    float inst = (float)Math.Sqrt(lx * lx + ly * ly) / dtl;
                    if (inst > 50f) inst = 0f;
                    _localSpeed = _localSpeed * 0.6f + inst * 0.4f;
                    if (lx * lx + ly * ly > 0.0004f) _moveDir = ForcedMoveMath.Normalize((float)Math.Atan2(ly, lx) + tp.O);
                }
                _lastLocal = local;
                _haveLocal = true;
                moving = _localSpeed > TransportMovingSpeed;
            }
            else _haveLocal = false;
            if (_haveAvatar)
            {
                float dt = (float)(now - _lastAvatarAt);
                if (dt > 1e-4f)
                {
                    float vz = (pos.Z - _avatarPos.Z) / dt;
                    _vz = _vz * 0.6f + vz * 0.4f;
                    float dx = pos.X - _avatarPos.X, dy = pos.Y - _avatarPos.Y;
                    if (!onTransport && dx * dx + dy * dy > 0.0004f) _moveDir = (float)Math.Atan2(dy, dx);
                }
            }
            else _moveDir = pos.O;
            _avatarPos = pos;
            _lastAvatarAt = now;
            _haveAvatar = true;
            _player.Pos = pos;
            _moving = moving;
            if (_motion.Driven) MeasureRider(pos);            // iteration 7: pos = the mount's feet, pos.O = its heading
        }

        /// <summary>
        /// MSG_MOVE_START_* when the avatar starts walking, MSG_MOVE_HEARTBEAT every 0.5 s while it walks, MSG_MOVE_SET_FACING when its
        /// heading turns, MSG_MOVE_STOP when it stops, MSG_MOVE_SET_FACING when it turns standing, MSG_MOVE_JUMP / MSG_MOVE_FALL_LAND
        /// around a detected jump. Iteration 11 (motion 6.2.9, decision D-M6): while walking the sent orientation is the avatar's FACING
        /// quantized to the 8 WoW key directions (Forward, Forward+Strafe, Strafe, Backward+Strafe, Backward) so that WoW's direction for
        /// those flags is exactly the avatar's travel (other clients dead-reckon it) and the facing stays within 22.5 deg of where the
        /// player looks (casts and swings at a target in front work while strafing: Spell.cpp:7390-7391, PlayerUpdates.cpp:183).
        /// Standing it is the avatar's facing. Nothing is sent while the server moves us (forced moves, taxi, lost control).
        /// </summary>
        void TickOwnMovement()
        {
            if (!_haveAvatar || _world == null || _holdUntil >= 0 || _player.Guid == 0) return;
            // iteration 11 (motion 6.2.2 item 4, merged with pets H3c): the server / a vehicle moves us
            var fm = _player.ForcedMove;
            if (fm != null && fm.Kind == ForcedMoveKind.Knockback && !_motion.Driven && _vehMover == 0) { TickKnockbackSender(); return; }   // motion 6.2.3
            if (_vehMover == 0 && (fm != null || _player.OnTaxi || _player.ControlLost)) return;   // MovementHandler.cpp:543-547 drops ours meanwhile
            if (_rooted && _vehMover == 0) return;           // VerifyMovementInfo drops moving packets of a rooted mover (MovementHandler.cpp:606-609)
            if (_vehSeat != null && _vehMover == 0) return;  // pets H3c: a vehicle passenger sends nothing
            if (_vehMover != 0 && !VehMaySend()) return;     // pets: may the controlled unit's movement go out now
            // iteration 7: a ride (or the fall after an air dismount) drives the avatar - the rider's sender (OnlineSession.Rider.cs)
            if (_motion.Driven != _wasDriven) { _wasDriven = _motion.Driven; RiderTransition(_motion.Driven); }
            if (_motion.Driven) { TickRider(); return; }
            double now = _now;
            var pos = _avatarPos;
            // Lead (2026-10-03, desktop walk-by-default): the walk mode of a Dreamport desktop player on foot. The real client sends
            // MSG_MOVE_SET_WALK_MODE / MSG_MOVE_SET_RUN_MODE when it toggles (MovementHandler.cpp HandleMovementOpcodes, the same reader as
            // every MSG_MOVE_*) and keeps MOVEMENTFLAG_WALKING (0x100) in every movement packet while walking - other clients animate a walk.
            bool footWalk = _motion.Walking && _vehMover == 0;
            if (footWalk != _footWalk)
            {
                _footWalk = footWalk;
                uint keep = _sentFlags & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight | MoveFlags.Falling);
                SendMove(footWalk ? Op.MSG_MOVE_SET_WALK_MODE : Op.MSG_MOVE_SET_RUN_MODE, keep, pos, keep != 0 ? _sentPos.O : pos.O, 0, 0, 0, 0, 0);
                if (_walkModeLogs++ < 6) WowLog.Info("Online: " + (footWalk ? "walk" : "run") + " mode (" + (footWalk ? "MSG_MOVE_SET_WALK_MODE" : "MSG_MOVE_SET_RUN_MODE") + ")");
                return;
            }
            uint sentDir = _sentFlags & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight);
            bool wasMoving = sentDir != 0;
            float face = pos.O;
            uint dirFlags = MoveFlags.Forward;
            float ori = face;
            if (_moving)
            {
                int k = WalkSector(_moveDir, face);
                dirFlags = SectorFlags(k);
                ori = NormalizeO(_moveDir - k * (float)(Math.PI / 4));
            }

            // jump detection: rising clearly above the ground; landing when back on it
            float ground = _groundZ != null ? _groundZ(pos.X, pos.Y, pos.Z + 0.5f) : float.NaN;
            if (!_airborne && !float.IsNaN(ground) && pos.Z - ground > 0.6f && _vz > 2.5f && _transportGuid == 0)
            {
                _airborne = true;
                _airStart = now;
                uint f = MoveFlags.Falling | (_moving ? dirFlags : 0u);
                float hs = _moving ? _mySpeeds[_footWalk ? MoveType.Walk : MoveType.Run] : 0f;
                SendMove(Op.MSG_MOVE_JUMP, f, pos, ori, 0, -_vz, (float)Math.Sin(_moveDir), (float)Math.Cos(_moveDir), hs);
                return;
            }
            if (_airborne)
            {
                uint fallMs = (uint)((now - _airStart) * 1000.0);
                bool landed = float.IsNaN(ground) ? now - _airStart > 3.0 : (pos.Z - ground < 0.25f && _vz < 1f) || now - _airStart > 10.0;
                if (landed)
                {
                    _airborne = false;
                    SendMove(Op.MSG_MOVE_FALL_LAND, _moving ? dirFlags : 0u, pos, ori, fallMs, 0, 0, 0, 0);
                    return;
                }
                if (now - _lastSendAt >= HeartbeatSeconds)
                {
                    float hs = _moving ? _mySpeeds[_footWalk ? MoveType.Walk : MoveType.Run] : 0f;
                    SendMove(Op.MSG_MOVE_HEARTBEAT, MoveFlags.Falling | (_moving ? dirFlags : 0u), pos, ori, fallMs, _sentJumpZ, (float)Math.Sin(_moveDir), (float)Math.Cos(_moveDir), hs);
                }
                return;
            }

            if (SeatedHold(pos, _moving && !wasMoving)) return;   // lead fix 2026-10-04: VR sway while seated (OnlineSession.States.cs)
            if (_moving && !wasMoving && _castId != 0)
            {
                // iteration 7: a mount cast is running - AC cancels it on any moving packet (Spell::update, isMoving). Hold the START while
                // the avatar only sways; a real walk (> CastWalkTolerance from where the cast began) goes out and the server interrupts.
                float cdx = pos.X - _castPos.X, cdy = pos.Y - _castPos.Y;
                if (cdx * cdx + cdy * cdy < CastWalkTolerance * CastWalkTolerance) return;
            }
            if (_moving && !wasMoving)
            {
                // the start's travel = the way the avatar has actually gone since the last sent position (wait up to 0.25 s for it)
                float sdx = pos.X - _sentPos.X, sdy = pos.Y - _sentPos.Y;
                bool onT = _haveLocal && _haveSentLocal;
                if (onT) { sdx = _lastLocal.X - _sentLocal.X; sdy = _lastLocal.Y - _sentLocal.Y; }
                if (sdx * sdx + sdy * sdy < 0.05f * 0.05f)
                {
                    if (_startWaitSince < 0) _startWaitSince = now;
                    if (now - _startWaitSince < 0.25) return;
                }
                else
                {
                    float travel = (float)Math.Atan2(sdy, sdx);
                    WowPos tp;
                    if (onT && TryTransportPose(_transportGuid, out tp)) travel += tp.O;
                    _walkSector = int.MinValue;
                    int k = WalkSector(travel, face);
                    dirFlags = SectorFlags(k);
                    ori = NormalizeO(travel - k * (float)(Math.PI / 4));
                }
                _startWaitSince = -1;
                SendMove(StartOpcode(dirFlags, 0u), dirFlags, pos, ori, 0, 0, 0, 0, 0);
                _walkO = ori;
                return;
            }
            _startWaitSince = -1;
            if (!_moving && wasMoving) { _walkSector = int.MinValue; SendMove(Op.MSG_MOVE_STOP, 0u, pos, face, 0, 0, 0, 0, 0); return; }
            if (_moving)
            {
                _walkO = ori;
                if (dirFlags != sentDir) { SendMove(StartOpcode(dirFlags, sentDir), dirFlags, pos, ori, 0, 0, 0, 0, 0); return; }
                float dO = Math.Abs(AngleDiff(ori, _sentPos.O));
                if (dO > 0.35f && now - _lastSendAt > 0.15) SendMove(Op.MSG_MOVE_SET_FACING, dirFlags, pos, ori, 0, 0, 0, 0, 0);
                else if (now - _lastSendAt >= HeartbeatSeconds) SendMove(Op.MSG_MOVE_HEARTBEAT, dirFlags, pos, ori, 0, 0, 0, 0, 0);
            }
            else
            {
                float dO = Math.Abs(AngleDiff(face, _sentPos.O));
                float dx = pos.X - _sentPos.X, dy = pos.Y - _sentPos.Y, dz = pos.Z - _sentPos.Z;
                if (_haveLocal && _haveSentLocal) { dx = _lastLocal.X - _sentLocal.X; dy = _lastLocal.Y - _sentLocal.Y; dz = _lastLocal.Z - _sentLocal.Z; }
                bool drifted = dx * dx + dy * dy + dz * dz > 0.5f * 0.5f;     // small steps below the walking threshold still reach the server
                if ((dO > 0.2f || drifted) && now - _lastSendAt > 0.25) SendMove(drifted ? Op.MSG_MOVE_HEARTBEAT : Op.MSG_MOVE_SET_FACING, 0u, pos, face, 0, 0, 0, 0, 0);
            }
        }

        /// <summary>6.2.9: the travel direction relative to the facing in 45 deg sectors, -4..4 (+ = left, WoW orientations grow
        /// counter-clockwise; 4 and -4 are both "backward"), with hysteresis around the previous sector (ours).</summary>
        int WalkSector(float travel, float face)
        {
            float rel = AngleDiff(travel, face);
            const float Q = (float)(Math.PI / 4);
            if (_walkSector != int.MinValue && Math.Abs(AngleDiff(rel, _walkSector * Q)) < Q / 2f + SectorHysteresis) return _walkSector;
            int k = (int)Math.Round(rel / Q);
            if (k <= -4) k = 4;
            if (k > 4) k = 4;
            _walkSector = k;
            return k;
        }

        /// <summary>The real client's key flags of a sector (OnlineMovement.cs MoveFlags).</summary>
        static uint SectorFlags(int k)
        {
            switch (k)
            {
                case 0: return MoveFlags.Forward;
                case 1: return MoveFlags.Forward | MoveFlags.StrafeLeft;
                case 2: return MoveFlags.StrafeLeft;
                case 3: return MoveFlags.Backward | MoveFlags.StrafeLeft;
                case -1: return MoveFlags.Forward | MoveFlags.StrafeRight;
                case -2: return MoveFlags.StrafeRight;
                case -3: return MoveFlags.Backward | MoveFlags.StrafeRight;
                default: return MoveFlags.Backward;
            }
        }

        /// <summary>The real client's opcode for a change of the moving keys (Opcodes.cs 0x0B5-0x0BA).</summary>
        static ushort StartOpcode(uint now, uint before)
        {
            const uint Strafe = MoveFlags.StrafeLeft | MoveFlags.StrafeRight, FB = MoveFlags.Forward | MoveFlags.Backward;
            if ((now & Strafe) != (before & Strafe))
            {
                if ((now & MoveFlags.StrafeLeft) != 0) return Op.MSG_MOVE_START_STRAFE_LEFT;
                if ((now & MoveFlags.StrafeRight) != 0) return Op.MSG_MOVE_START_STRAFE_RIGHT;
                if ((now & FB) == (before & FB)) return Op.MSG_MOVE_STOP_STRAFE;
            }
            return (now & MoveFlags.Backward) != 0 ? Op.MSG_MOVE_START_BACKWARD : Op.MSG_MOVE_START_FORWARD;
        }

        /// <summary>quests H-M1 (Death.cs before CMSG_RECLAIM_CORPSE): one MSG_MOVE_HEARTBEAT when the avatar is >= 0.05 yd from the last
        /// sent position, so the server's distance check uses where the avatar really stands. True = sent.</summary>
        internal bool MoveSyncNow(string why)
        {
            if (!_haveAvatar || _world == null || _world.IsClosed || _player.Guid == 0) return false;
            if (_holdUntil >= 0 || _rooted || _motion.Driven || _airborne || _vehMover != 0 || _vehSeat != null) return false;
            if (_player.ForcedMove != null || _player.OnTaxi || _player.ControlLost) return false;
            var pos = _avatarPos;
            float dx = pos.X - _sentPos.X, dy = pos.Y - _sentPos.Y, dz = pos.Z - _sentPos.Z;
            if (dx * dx + dy * dy + dz * dz < 0.05f * 0.05f) return false;
            uint flags = _sentFlags & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight);
            SendMove(Op.MSG_MOVE_HEARTBEAT, flags, pos, flags != 0 ? _sentPos.O : pos.O, 0, 0, 0, 0, 0);
            WowLog.Info("Online: position synced before " + (why ?? "?") + " (" + Math.Sqrt(dx * dx + dy * dy + dz * dz).ToString("0.00") + " yd from the last packet)");
            return true;
        }

        uint _sentFlags;
        float _sentJumpZ;
        bool _footWalk;          // lead 2026-10-03: on foot in WoW's walk mode (desktop walk-by-default)
        int _walkModeLogs;
        double _startWaitSince = -1;

        static float AngleDiff(float a, float b)
        {
            float d = a - b;
            while (d > Math.PI) d -= (float)(2 * Math.PI);
            while (d < -Math.PI) d += (float)(2 * Math.PI);
            return d;
        }

        static float NormalizeO(float o)
        {
            const float TwoPi = (float)(2 * Math.PI);
            o %= TwoPi;
            if (o < 0f) o += TwoPi;
            return o;
        }

        /// <summary>HandleMovementOpcodes reads: packed guid (must be our mover), MovementInfo (ReadMovementInfo).</summary>
        void SendMove(ushort opcode, uint flags, WowPos pos, float ori, uint fallTime, float jumpZ, float jumpSin, float jumpCos, float jumpXY)
        {
            SendMove(opcode, flags, pos, ori, fallTime, jumpZ, jumpSin, jumpCos, jumpXY, 0f);
        }

        /// <summary>The same with the pitch float (written by MovementInfo.Write when SWIMMING or FLYING is set).</summary>
        void SendMove(ushort opcode, uint flags, WowPos pos, float ori, uint fallTime, float jumpZ, float jumpSin, float jumpCos, float jumpXY, float pitch)
        {
            StStandUpOnMove(flags);   // states fix 2026-10-04: walking ends a kneel / sleep (OnlineSession.States.cs)
            var mi = OwnMovementInfo(flags, pos, ori);
            mi.Pitch = pitch;
            mi.FallTime = fallTime;
            mi.JumpZSpeed = jumpZ; mi.JumpSin = jumpSin; mi.JumpCos = jumpCos; mi.JumpXYSpeed = jumpXY;
            var w = new PacketWriter().PackedGuid(_vehMover != 0 ? _vehMover : _player.Guid);   // iteration 11 (pets H3a)
            mi.Write(w);
            _world.Send(opcode, w);
            if (opcode == Op.MSG_MOVE_JUMP) _sentJumpZ = jumpZ;
            _sentFlags = flags;
            _sentPos = new WowPos(pos.X, pos.Y, pos.Z, mi.O);
            if ((mi.Flags & MoveFlags.OnTransport) != 0) { _sentLocal = new WowPos(mi.TX, mi.TY, mi.TZ, mi.TO); _haveSentLocal = true; }
            else _haveSentLocal = false;
            _lastSendAt = _now;
            _movesSent++;
        }

        /// <summary>
        /// Iteration 7 flag rules on EVERY packet and ack (AC WorldSession::ReadMovementInfo): CAN_FLY while the server lets us fly, never
        /// CAN_FLY / FLYING without it; FALLING never together with CAN_FLY or DISABLE_GRAVITY ("Cannot fly and fall at the same time");
        /// the hover / water walk / feather fall / gravity state the server set and we acked. Iteration 11: a controlled vehicle's info
        /// (pets H3a), a vehicle seat (pets H3b), or MOVEMENTFLAG_ONTRANSPORT with the offsets in the transport's frame (motion:
        /// TransportBase::CalculatePassengerOffset, VehicleDefines.h:154-165).
        /// </summary>
        MovementInfo OwnMovementInfo(uint flags, WowPos pos, float ori)
        {
            if (_vehMover != 0)
            {
                var vm = VehMoverInfo(flags, pos, ori);
                if (vm != null) return vm;
            }
            flags |= _stateFlags;
            if (_footWalk && !_motion.Driven) flags |= MoveFlags.Walking;   // lead 2026-10-03: desktop walk mode on foot
            if (_player.CanFly) flags |= MoveFlags.CanFly;
            else flags &= ~(MoveFlags.CanFly | MoveFlags.Flying);
            if ((flags & (MoveFlags.CanFly | MoveFlags.DisableGravity)) != 0) flags &= ~MoveFlags.Falling;
            var mi = new MovementInfo
            {
                Flags = flags | (_rooted ? MoveFlags.Root : 0u),
                Time = _clientTicks(),
                X = pos.X, Y = pos.Y, Z = pos.Z, O = NormalizeO(ori)
            };
            if (_vehSeat != null) VehAddSeat(mi);
            else
            {
                WowPos tp;
                if (_transportGuid != 0 && TryTransportPose(_transportGuid, out tp))
                {
                    WowPos l = ForcedMoveMath.PassengerOffset(pos.X, pos.Y, pos.Z, mi.O, tp);
                    mi.Flags |= MoveFlags.OnTransport;
                    mi.TransportGuid = _transportGuid;
                    mi.TX = l.X; mi.TY = l.Y; mi.TZ = l.Z; mi.TO = l.O;
                    mi.TransportTime = _clientTicks();
                    mi.TransportSeat = -1;
                }
            }
            return mi;
        }

        /// <summary>
        /// SMSG_FORCE_*_SPEED_CHANGE (Unit::SendSpeedToController): packed guid, uint32 counter, [MOVE_RUN: uint8 0], float speed.
        /// Answer CMSG_FORCE_*_SPEED_CHANGE_ACK: packed guid, counter, MovementInfo, the same float (a different value kicks).
        /// </summary>
        void OnForceSpeed(WowPacket p, int type)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (type == MoveType.Run) p.U8();
            float speed = p.F32();
            if (g != 0 && g == _vehMover) { VehMoverSpeed(type, speed); return; }   // iteration 11 (pets H3d)
            if (g != _player.Guid) return;
            _mySpeeds[type] = speed;
            SetPlayerSpeed(type, speed);                    // iteration 7: the ride moves at exactly the server's speed
            if (type == MoveType.Run) _runAckAt = _now;
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            OwnMovementInfo(AckFlags(), _haveAvatar ? _avatarPos : _player.Pos, _sentPos.O).Write(w);
            w.F32(speed);
            _world.Send(ForceSpeedAck[type], w);
            if (type == MoveType.Run || type == MoveType.Flight || type == MoveType.Swim)
                WowLog.Info("Online: " + SpeedNames[type] + " speed " + speed.ToString("0.00") + " yd/s acked (counter " + counter + ")");
        }

        /// <summary>SMSG_FORCE_MOVE_(UN)ROOT (Unit::SetRooted): packed guid, uint32 counter -> CMSG_FORCE_MOVE_(UN)ROOT_ACK: packed guid, counter, MovementInfo.</summary>
        void OnForceRoot(WowPacket p, bool root)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (g != _player.Guid) return;
            _rooted = root;
            RefreshRooted();                                // iteration 11 (motion): PlayerState.Rooted for the avatar (WowForcedMove)
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            OwnMovementInfo(0u, _haveAvatar ? _avatarPos : _player.Pos, _sentPos.O).Write(w);
            _world.Send(root ? Op.CMSG_FORCE_MOVE_ROOT_ACK : Op.CMSG_FORCE_MOVE_UNROOT_ACK, w);
            _sentFlags = 0;
        }

        // ------------------------------------------------------------------ teleports

        void HoldAt(WowPos pos, double seconds)
        {
            _holdPos = pos;
            _holdUntil = _now + seconds;
            _airborne = false;
            _sentFlags = 0;
            _sentPos = pos;
            _player.Pos = pos;
            _walkSector = int.MinValue;
        }

        void TeleportEvent(WowPos pos, int map)
        {
            _teleports++;
            _events.Enqueue(new GameEvent { Type = GameEventType.Teleport, Pos = pos, Amount = map, SourceGuid = _player.Guid });
        }

        /// <summary>
        /// Near teleport (Player::SendTeleportAckPacket): MSG_MOVE_TELEPORT_ACK = packed guid, uint32 counter, MovementInfo at the
        /// destination. Answer (HandleMoveTeleportAck): packed guid, uint32 flags (the counter), uint32 time.
        /// </summary>
        void OnTeleportAck(WowPacket p)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            var mi = new MovementInfo();
            mi.Read(p);
            if (g != _player.Guid) return;
            var pos = new WowPos(mi.X, mi.Y, mi.Z, mi.O);
            // iteration 11 (motion 6.2.2 item 9): a same-map teleport frame of a ship keeps us on board (Transport.cpp:612-639)
            WowPos tp;
            if ((mi.Flags & MoveFlags.OnTransport) != 0 && IsGoTransportGuid(mi.TransportGuid) && TryTransportPose(mi.TransportGuid, out tp))
                pos = ForcedMoveMath.PassengerPosition(mi.TX, mi.TY, mi.TZ, mi.TO, tp);
            HoldAt(pos, 10.0);
            _world.Send(Op.MSG_MOVE_TELEPORT_ACK, new PacketWriter().PackedGuid(g).U32(counter).U32(_clientTicks()));
            EndForcedMove("teleport");                      // iteration 11 (motion): a teleport ends any forced move / flight
            TaxiCutShort("teleport");
            TaxiNoteTeleport();
            if ((mi.Flags & MoveFlags.OnTransport) != 0 && IsGoTransportGuid(mi.TransportGuid)) SetPlayerTransportInternal(mi.TransportGuid, "a teleport frame of the transport");
            else SetPlayerTransportInternal(0, "teleported");
            TeleportEvent(pos, _mapId);
            WowLog.Info("Online world: teleported to " + pos + " on map " + _mapId);
        }

        uint _transferTransportEntry, _transferOldMap;

        /// <summary>SMSG_TRANSFER_PENDING (Player::TeleportTo, Player.cpp:1609-1614): uint32 map [, uint32 transport entry, uint32 old map].</summary>
        void OnTransferPending(WowPacket p)
        {
            uint map = p.U32();
            _transferTransportEntry = 0;
            _transferOldMap = 0;
            if (p.Remaining >= 8) { _transferTransportEntry = p.U32(); _transferOldMap = p.U32(); }
            WowLog.Info("Online world: transfer to map " + map + " pending" + (_transferTransportEntry != 0 ? " on transport " + _transferTransportEntry + " from map " + _transferOldMap : ""));
        }

        /// <summary>
        /// SMSG_NEW_WORLD: uint32 map, x, y, z, o. Everything of the old map is gone (the server sends the new map's objects after our
        /// MSG_MOVE_WORLDPORT_ACK, empty body, HandleMoveWorldportAck). MapId changes before the Teleport event (Amount = map).
        /// Iteration 11 (motion): a taxi crossing to the next map keeps flying (TaxiHandler.cpp:216-241, MovementHandler.cpp:217-226);
        /// riding a ship the x y z o are the transport OFFSETS (Player.cpp:1629-1634) - the world position follows our create block.
        /// </summary>
        void OnNewWorld(WowPacket p)
        {
            int map = (int)p.U32();
            var pos = new WowPos(p.F32(), p.F32(), p.F32(), p.F32());
            int old = _mapId;
            EndForcedMove("teleport");
            if (_player.OnTaxi)
            {
                if (_taxiHop || _selfTaxiFlight)
                {
                    _taxiHop = false;
                    _taxiHopArrivedAt = _now;
                    WowLog.Info("Online taxi: the two-map flight crossed to map " + map + " - the server flies on from " + pos);
                }
                else TaxiCutShort("new world");
            }
            uint transportEntry = _transferTransportEntry;
            _transferTransportEntry = 0;
            ClearWorld();
            TransportClearWorld();
            _mapId = map;
            if (transportEntry != 0 && TransportArrival(transportEntry, map, pos))
            {
                _world.Send(Op.MSG_MOVE_WORLDPORT_ACK);
                WowLog.Info("Online world: new world map " + old + " -> " + map + " on transport " + transportEntry + " (offsets " + pos + ") - the world position follows our create block");
                return;
            }
            HoldAt(pos, 10.0);
            SetPlayerTransportInternal(0, "a new map");      // after HoldAt: no packet goes out before our WORLDPORT_ACK
            _world.Send(Op.MSG_MOVE_WORLDPORT_ACK);
            TeleportEvent(pos, map);
            WowLog.Info("Online world: new world map " + old + " -> " + map + " at " + pos);
        }

        /// <summary>SMSG_TRANSFER_ABORTED (Player::SendTransferAborted): uint32 map, uint8 reason [, uint8 arg] (world hook 2: the instance
        /// owner's texts).</summary>
        void OnTransferAborted(WowPacket p)
        {
            uint map = p.U32();
            int reason = p.Remaining > 0 ? p.U8() : 0;
            int arg = p.Remaining > 0 ? p.U8() : 0;
            InstTransferAborted((int)map, reason, arg);
            if (_taxiHop) TaxiCutShort("transfer aborted");
        }

        /// <summary>
        /// SMSG_MULTIPLE_MOVES (Player.cpp setCompoundState): uint32 size, then per packet uint8 size (2 + body), uint16 opcode, body.
        /// (An inflated SMSG_COMPRESSED_MOVES carries the same entries without the leading size.)
        /// </summary>
        void OnMultipleMoves(WowPacket p)
        {
            if (p.Length >= 4 && BitConverterLE.U32(p.Data, 0) == (uint)(p.Length - 4)) p.Pos = 4;
            while (p.Remaining >= 3)
            {
                int size = p.U8();
                if (size < 2 || size > p.Remaining) break;
                ushort op = p.U16();
                var sub = new WowPacket(op, p.Bytes(size - 2));
                Action<WowPacket> h;
                if (_moveHandlers.TryGetValue(op, out h))
                {
                    try { h(sub); }
                    catch (Exception e) { WowLog.Warn("Online world: " + Op.Name(op) + " inside SMSG_MULTIPLE_MOVES failed: " + e.Message); }
                }
            }
        }

        /// <summary>Movement packets sent / teleports handled (diagnostics, probes).</summary>
        public int MovesSent { get { return _movesSent; } }
    }
}
