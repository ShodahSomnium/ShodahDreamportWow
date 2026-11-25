// iteration 11 - owner motion (proposal-motion.md 6.2.4): transports online - boats and zeppelins (GO type 15 MO_TRANSPORT), elevators
// and the Deeprun Tram (GO type 11 TRANSPORT). The server never sends a transport's position after its create block (BuildUpdate sends
// fields only, Transport.cpp:204-214): the client animates it from the create block's path progress (Object.cpp:467-475) and its own
// clock (MotionTransport::Update `timer = progress % period`, Transport.cpp:238; StaticTransport::Update :968-1019). Passengers carry
// MOVEMENTFLAG_ONTRANSPORT + offsets in the transport's frame (VehicleDefines.h:143-165); we board by sending ONTRANSPORT ourselves
// (MovementHandler.cpp:437-486). Riding a ship across a map change: SMSG_NEW_WORLD carries our OFFSETS (Player.cpp:1629-1634).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Motion;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const int GoTypeTransport = 11, GoTypeMoTransport = 15;     // GAMEOBJECT_TYPE_TRANSPORT / _MO_TRANSPORT (SharedDefines.h)
        // ours (logged once in InitTransport)
        const float TransportDisagreeYards = 50f;         // our path's pose vs the create block's current pose: beyond this the port is off
        const double TransportArrivalFallback = 10.0;     // s: after a map change on a ship, our create block with ONTRANSPORT is awaited this long
        const double PassengerExtrapolateMax = 1.0;       // s: a passenger walking on deck is extrapolated at most this long

        sealed class TransportInfo
        {
            public ulong Guid; public int Entry, GoType, GoState;
            public WowPos Spawn; public float ParentRotZ, ParentRotW;
            public uint Progress0; public double Clock0;
            public uint ServerPeriod;
            public TransportPath Path; public bool PathTried, Disagrees, Away, PoseKnown, Logged, NoAnimLogged;
            public uint StaticProgress; public double StaticClock;
            public WowPos Pose;
        }

        sealed class GoTemplateInfo { public int Type; public readonly uint[] Data = new uint[24]; public float Size; public string Name; }

        sealed class PassengerInfo
        {
            public ulong Transport; public float LX, LY, LZ, LO; public uint Flags; public double At; public SplinePath LocalSpline;
        }

        struct CreateRecord { public uint Progress; public double At; public WowPos Stationary; }

        readonly Dictionary<ulong, TransportInfo> _transports = new Dictionary<ulong, TransportInfo>();
        readonly Dictionary<int, GoTemplateInfo> _goTemplates = new Dictionary<int, GoTemplateInfo>();
        readonly Dictionary<ulong, PassengerInfo> _passengers = new Dictionary<ulong, PassengerInfo>();
        readonly Dictionary<ulong, CreateRecord> _createProgress = new Dictionary<ulong, CreateRecord>();
        readonly List<ulong> _transportGone = new List<ulong>();
        ulong _transportGuid;
        StaticTransportAnim _staticAnim;
        bool _awaitTransportArrival;
        double _arrivalAt;
        uint _arrivalEntry;
        WowPos _arrivalOffset, _arrivalGuess;

        /// <summary>proposal-motion.md 3.8: GameObject 0xF110, Transport (type 11, Transport.cpp:865) 0xF120, Mo_Transport (Transport.cpp:79)
        /// 0x1FC0 (P/acsrc/ObjectGuid.h:66, 72) - the high 16 bits of the guid.</summary>
        static bool IsGoTransportGuid(ulong g)
        {
            uint h = (uint)(g >> 48);
            return g != 0 && (h == 0xF110 || h == 0xF120 || h == 0x1FC0);
        }

        partial void InitTransport()
        {
            _world.On(Op.SMSG_GAMEOBJECT_QUERY_RESPONSE, OnTransportGoQuery, this);   // Objects.cs keeps the name; this keeps the transport data
            WowLog.Info("Online transport: ours - our path's pose may differ " + TransportDisagreeYards + " yd from the server's before it is drawn static, " +
                        "half the measured round trip is added to the path clock, a ship's arrival after a map change is awaited " + TransportArrivalFallback +
                        " s, passengers on deck extrapolated at most " + PassengerExtrapolateMax + " s.");
        }

        // ------------------------------------------------------------------ IWowSession

        /// <summary>The floor under the avatar belongs to this transport (TransportCarry), 0 = off it. The next movement packets carry
        /// MOVEMENTFLAG_ONTRANSPORT and the offsets (OwnMovementInfo): the server makes us its passenger.</summary>
        public void SetPlayerTransport(ulong transportGuid)
        {
            if (transportGuid != 0 && !_transports.ContainsKey(transportGuid)) return;
            SetPlayerTransportInternal(transportGuid, transportGuid != 0 ? "the avatar stands on it" : "the avatar stepped off");
        }

        void SetPlayerTransportInternal(ulong g, string why)
        {
            if (g == _transportGuid) return;
            ulong old = _transportGuid;
            _transportGuid = g;
            _player.TransportGuid = g;
            _haveLocal = false;
            _haveSentLocal = false;
            _localSpeed = 0f;
            string name = TransportName(g != 0 ? g : old);
            _events.Enqueue(new GameEvent { Type = GameEventType.TransportChanged, SourceGuid = _player.Guid, TargetGuid = g, Text = name });
            WowLog.Info("Online transport: " + (g != 0 ? "on board " + name + " (" + g.ToString("X16") + ")" : "off " + name) + " - " + why);
            // one heartbeat at once so the server boards / unboards us (MovementHandler.cpp:437-486)
            if (_haveAvatar && _world != null && !_world.IsClosed && _holdUntil < 0 && _player.Guid != 0 && _player.ForcedMove == null && !_player.OnTaxi &&
                !_player.ControlLost && !_rooted && !_motion.Driven && _vehMover == 0 && _vehSeat == null)
            {
                uint flags = _sentFlags & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight);
                SendMove(Op.MSG_MOVE_HEARTBEAT, flags, _avatarPos, flags != 0 ? _sentPos.O : _avatarPos.O, 0, 0, 0, 0, 0);
            }
        }

        string TransportName(ulong g)
        {
            var e = g != 0 ? Find(g) : null;
            return e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : g != 0 ? "transport " + g.ToString("X") : "the transport";
        }

        /// <summary>The transport's pose now (WoW world coordinates, O = its heading), false when unknown or away on another map.</summary>
        bool TryTransportPose(ulong guid, out WowPos pose)
        {
            TransportInfo t;
            if (guid != 0 && _transports.TryGetValue(guid, out t) && t.PoseKnown && !t.Away) { pose = t.Pose; return true; }
            pose = default(WowPos);
            return false;
        }

        // ------------------------------------------------------------------ the transports

        /// <summary>Movement.cs ApplyMovementBlock: a game object's create block with UPDATEFLAG_TRANSPORT: its path progress (and the
        /// stationary pose: for MO transports the CURRENT pose, GameObject.h:334-337) at our clock.</summary>
        void NoteTransportProgress(OnlineObject o, MovementBlock m)
        {
            var rec = new CreateRecord { Progress = m.TransportPathProgress, At = _now, Stationary = new WowPos(m.X, m.Y, m.Z, m.O) };
            _createProgress[o.Guid] = rec;
            TransportInfo t;
            if (_transports.TryGetValue(o.Guid, out t))
            {
                t.Progress0 = rec.Progress + (t.GoType == GoTypeMoTransport ? HalfRoundTrip() : 0u);
                t.Clock0 = _now;
                t.Spawn = rec.Stationary;
                t.StaticProgress = rec.Progress;
                t.StaticClock = _now;
            }
        }

        uint HalfRoundTrip() { return _world != null && _world.LatencyMs > 0 ? (uint)(_world.LatencyMs / 2) : 0u; }

        /// <summary>OnlineSession.Objects.cs RefreshGameObject hook (also reached from RefreshAllReactions: idempotent).</summary>
        partial void NoteTransport(OnlineObject o)
        {
            var s = o.State;
            if (s == null) return;
            int type = s.GoType;
            if (type != GoTypeTransport && type != GoTypeMoTransport)
            {
                uint high = (uint)(o.Guid >> 48);
                if (type != 0) return;
                if (high == 0x1FC0) type = GoTypeMoTransport;
                else if (high == 0xF120) type = GoTypeTransport;
                else return;
            }
            TransportInfo t;
            if (!_transports.TryGetValue(o.Guid, out t))
            {
                t = new TransportInfo { Guid = o.Guid, GoType = type, Entry = o.Entry, Clock0 = _now, StaticClock = _now, Spawn = s.Pos };
                CreateRecord cr;
                if (_createProgress.TryGetValue(o.Guid, out cr))
                {
                    t.Progress0 = cr.Progress + (type == GoTypeMoTransport ? HalfRoundTrip() : 0u);
                    t.Clock0 = cr.At;
                    t.StaticProgress = cr.Progress;
                    t.StaticClock = cr.At;
                    t.Spawn = cr.Stationary;
                }
                _transports[o.Guid] = t;
                WowLog.Info("Online transport: " + (s.Name ?? "?") + " (entry " + o.Entry + ", type " + type + ", " + o.Guid.ToString("X16") + ") at " + t.Spawn + ", path progress " + t.Progress0 + " ms");
            }
            t.Entry = o.Entry;
            t.GoType = type;
            t.GoState = s.GoState;
            t.ParentRotZ = o.F32(UF.GAMEOBJECT_PARENTROTATION + 2);
            t.ParentRotW = o.F32(UF.GAMEOBJECT_PARENTROTATION + 3);
            if (type == GoTypeMoTransport) t.ServerPeriod = o.U32(UF.GAMEOBJECT_LEVEL);
            s.IsTransport = true;
            EnsureTransportPath(t, s);
            UpdateTransportPose(t, s);
        }

        /// <summary>SMSG_GAMEOBJECT_QUERY_RESPONSE (P/acsrc/QueryHandler.cpp:193-211): uint32 entry, type, displayId, cstring name, 3 x
        /// empty name, cstring icon, castBarCaption, unk1, uint32 data[24] (MAX_GAMEOBJECT_DATA), float size, uint32 questItems[6].</summary>
        void OnTransportGoQuery(WowPacket p)
        {
            uint raw = p.U32();
            if ((raw & 0x80000000u) != 0 || p.Remaining < 8) return;
            int entry = (int)raw;
            int type = (int)p.U32();
            if (type != GoTypeTransport && type != GoTypeMoTransport) return;
            p.U32();                                         // displayId
            var tpl = new GoTemplateInfo { Type = type, Name = p.CString() };
            for (int i = 0; i < 3; i++) p.CString();
            p.CString(); p.CString(); p.CString();           // icon, castBarCaption, unk1
            for (int i = 0; i < 24 && p.Remaining >= 4; i++) tpl.Data[i] = p.U32();
            if (p.Remaining >= 4) tpl.Size = p.F32();
            _goTemplates[entry] = tpl;
            foreach (var t in _transports.Values)
            {
                if (t.Entry != entry) continue;
                OnlineObject o;
                if (_objects.TryGetValue(t.Guid, out o) && o.State != null) { EnsureTransportPath(t, o.State); UpdateTransportPose(t, o.State); }
            }
        }

        /// <summary>Type 15: TransportPath.Build from the template's data0 taxiPathId, data1 moveSpeed, data2 accelRate
        /// (GameObjectData.h:214-226) once the template is known; logs ours vs the server's period and the pose cross-check.</summary>
        void EnsureTransportPath(TransportInfo t, EntityState s)
        {
            if (t.GoType != GoTypeMoTransport || t.PathTried) return;
            GoTemplateInfo tpl;
            if (!_goTemplates.TryGetValue(t.Entry, out tpl)) return;
            t.PathTried = true;
            var db = TaxiDbOnline();
            int pathId = (int)tpl.Data[0];
            try { t.Path = db != null ? TransportPath.Build(db.PathPoints(pathId), tpl.Data[1], tpl.Data[2]) : null; }
            catch (Exception e) { WowLog.Warn("Online transport: building path " + pathId + " threw " + e.Message); t.Path = null; }
            if (t.Path == null)
            {
                WowLog.Warn("Online transport: " + (s.Name ?? tpl.Name) + " path " + pathId + " could not be built - drawn where it appeared");
                return;
            }
            uint ours = t.Path.PeriodMs;
            long diff = t.ServerPeriod > 0 ? Math.Abs((long)ours - t.ServerPeriod) : 0;
            int map;
            WowPos at = t.Path.PoseAt(t.Progress0 % (double)ours, out map);
            float dx = at.X - t.Spawn.X, dy = at.Y - t.Spawn.Y, dz = at.Z - t.Spawn.Z;
            float off = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            t.Disagrees = map == _mapId && off > TransportDisagreeYards;
            WowLog.Info("Online transport: " + (s.Name ?? tpl.Name) + " path " + pathId + " (" + tpl.Data[1] + " yd/s, accel " + tpl.Data[2] + "), period ours " + ours + " ms vs server " +
                        (t.ServerPeriod > 0 ? t.ServerPeriod + " ms" : "unknown") + ", " + t.Path.Summary + "; our pose at its progress is " + off.ToString("0.0") +
                        " yd from the server's" + (t.Disagrees ? " - WARNING: the path port disagrees, drawn static" : ""));
            if (diff > 50) WowLog.Warn("Online transport: " + (s.Name ?? tpl.Name) + " period differs by " + diff + " ms from the server's - a port error (our period kept)");
        }

        /// <summary>2026-10-03 (base reality's elevator method as Euro Truck Simulator runs it): TransportCarry calls this in Update at order -100,
        /// BEFORE WowGame ticks this session (order 0), with this frame's Time.deltaTime - the value WowGame passes to Tick (WowGame.cs:2022,
        /// 2106), which adds it to _now first (OnlineSession.cs Tick). Every transport's pose is computed at that clock now, so the deck the
        /// avatar stands on is drawn, the avatar carried with it and the physics synced before the client steps its CharacterController and
        /// places the desktop avatar (order 0) - the order ETS keeps (EtsNet.cs:42 order -100 draws the vehicles, then EtsRiders.EarlyPass
        /// carries: EtsRiders.cs:33-36). Tick then reaches the same clock, so TickTransport gives the same poses (a static transport has no
        /// step left), and the offsets SetPlayerPosition / OwnMovementInfo measure against t.Pose use the pose the avatar was carried to.</summary>
        public void AdvanceTransports(float deltaSeconds)
        {
            if (_transports.Count == 0) return;
            double at = _now + (deltaSeconds > 0f && deltaSeconds < 5f ? deltaSeconds : 0f);
            foreach (var t in _transports.Values)
            {
                OnlineObject o;
                if (_objects.TryGetValue(t.Guid, out o) && o.State != null) UpdateTransportPose(t, o.State, at);
            }
        }

        void UpdateTransportPose(TransportInfo t, EntityState s)
        {
            UpdateTransportPose(t, s, _now);
        }

        /// <summary>The pose of one transport at the clock now (normally _now) -> EntityState.Pos (WowEntityView draws transports unsmoothed:
        /// motion hook 8).</summary>
        void UpdateTransportPose(TransportInfo t, EntityState s, double now)
        {
            if (t.GoType == GoTypeMoTransport)
            {
                if (t.Path == null || t.Disagrees)
                {
                    t.Pose = t.Spawn;
                    t.PoseKnown = true;
                    t.Away = false;
                    s.Pos = t.Spawn;
                    return;
                }
                int map;
                WowPos pose = t.Path.PoseAt(t.Progress0 + (now - t.Clock0) * 1000.0, out map);
                bool away = map != _mapId;
                if (away != t.Away) WowLog.Info("Online transport: " + (s.Name ?? "?") + (away ? " left this map (sails on map " + map + ")" : " is on this map again"));
                t.Away = away;
                if (away) return;
                t.Pose = pose;
                t.PoseKnown = true;
                s.Pos = pose;
                return;
            }
            // type 11 (StaticTransport::Update / RelocateToProgress)
            if (_staticAnim == null) _staticAnim = StaticTransportAnim.Get(_files);
            if (!_staticAnim.Has(t.Entry))
            {
                if (!t.NoAnimLogged) { t.NoAnimLogged = true; WowLog.Info("Online transport: " + (s.Name ?? "?") + " (entry " + t.Entry + ") has no TransportAnimation rows - drawn where it is"); }
                t.Pose = s.Pos;
                t.PoseKnown = true;
                return;
            }
            uint period = _staticAnim.Period(t.Entry);
            GoTemplateInfo tpl;
            uint pause = _goTemplates.TryGetValue(t.Entry, out tpl) ? tpl.Data[0] : 0u;   // transport.pauseAtTime (GameObjectData.h:184-192)
            double elapsed = (now - t.StaticClock) * 1000.0;
            if (elapsed >= 1.0)
            {
                uint step = (uint)Math.Min(elapsed, 60000.0);
                t.StaticProgress = StaticTransportAnim.Advance(t.StaticProgress, step, period, pause, t.GoState);
                t.StaticClock += step / 1000.0;
            }
            WowPos p2;
            if (_staticAnim.PoseAt(t.Entry, t.StaticProgress, t.Spawn, t.ParentRotZ, t.ParentRotW, out p2))
            {
                t.Pose = p2;
                t.PoseKnown = true;
                s.Pos = p2;
                if (!t.Logged)
                {
                    t.Logged = true;
                    WowLog.Info("Online transport: " + (s.Name ?? "?") + " (entry " + t.Entry + ") animates over " + period + " ms" + (pause != 0 ? ", pauses at " + pause + " ms" : "") +
                                " from progress " + t.StaticProgress + ", path rotation " + PathRotDeg(t).ToString("0") + " deg (GAMEOBJECT_PARENTROTATION z " + t.ParentRotZ.ToString("0.###") + ", w " + t.ParentRotW.ToString("0.###") + "; Transport.cpp:1036-1042)");   // duelsnd: the tram cars need 180 deg to stay in the tunnel (harness TramFacts)
                }
            }
        }

        /// <summary>duelsnd: StaticTransport::RelocateToProgress's path angle in degrees: sign(PARENTROTATION z) * 2 acos(w) (Transport.cpp:1036-1037).</summary>
        static double PathRotDeg(TransportInfo t) { double w = Math.Max(-1.0, Math.Min(1.0, t.ParentRotW)); return (t.ParentRotZ >= 0f ? 1.0 : -1.0) * 2.0 * Math.Acos(w) * 180.0 / Math.PI; }

        partial void TickTransport(float dt)
        {
            if (_transports.Count > 0)
            {
                _transportGone.Clear();
                foreach (var t in _transports.Values)
                {
                    OnlineObject o;
                    if (!_objects.TryGetValue(t.Guid, out o) || o.State == null) { _transportGone.Add(t.Guid); continue; }
                    UpdateTransportPose(t, o.State);
                }
                foreach (ulong g in _transportGone)
                {
                    _transports.Remove(g);
                    _createProgress.Remove(g);
                    if (g == _transportGuid) SetPlayerTransportInternal(0, "the transport is gone");
                }
            }
            if (_passengers.Count > 0) TickPassengers();
            if (_awaitTransportArrival && _now - _arrivalAt > TransportArrivalFallback)
            {
                _awaitTransportArrival = false;
                WowLog.Warn("Online transport: no create block of ours on transport " + _arrivalEntry + " within " + TransportArrivalFallback + " s of the map change - arriving at the path's estimate " + _arrivalGuess);
                HoldAt(_arrivalGuess, 20.0);
                TeleportEvent(_arrivalGuess, _mapId);
            }
        }

        // ------------------------------------------------------------------ passengers

        bool IsTransportPassenger(OnlineObject o)
        {
            PassengerInfo p;
            WowPos tp;
            return o != null && _passengers.TryGetValue(o.Guid, out p) && TryTransportPose(p.Transport, out tp);
        }

        /// <summary>Movement.cs (create blocks, MSG_MOVE_*): a MovementInfo with ONTRANSPORT on a game-object transport keeps the local
        /// offsets (TX, TY, TZ, TO); without it the unit is off any transport. Our own create block after a map change on a ship
        /// places us on the deck.</summary>
        void NoteTransportPassenger(OnlineObject o, MovementInfo mi)
        {
            if (o == null || mi == null) return;
            if (o == _self) { NoteSelfOnTransport(mi); return; }
            bool on = (mi.Flags & MoveFlags.OnTransport) != 0 && IsGoTransportGuid(mi.TransportGuid);
            if (on)
            {
                PassengerInfo p;
                if (!_passengers.TryGetValue(o.Guid, out p)) _passengers[o.Guid] = p = new PassengerInfo();
                p.Transport = mi.TransportGuid;
                p.LX = mi.TX; p.LY = mi.TY; p.LZ = mi.TZ; p.LO = mi.TO;
                p.Flags = mi.Flags;
                p.At = _now;
                p.LocalSpline = null;
                if (o.State != null) o.State.TransportGuid = mi.TransportGuid;
                WowPos tp;
                if (TryTransportPose(p.Transport, out tp)) { WowPos w = ForcedMoveMath.PassengerPosition(p.LX, p.LY, p.LZ, p.LO, tp); SetEntityPos(o, w.X, w.Y, w.Z, w.O); }
            }
            else if (_passengers.Remove(o.Guid) && o.State != null) o.State.TransportGuid = 0;
        }

        /// <summary>Movement.cs OnMonsterMove: SMSG_MONSTER_MOVE_TRANSPORT of a unit on a game-object transport - the spline in the
        /// transport's frame (TransportPathTransform, MoveSplineInit.cpp:240-247), evaluated each Tick and composed with the pose.</summary>
        void TransportMonsterMove(OnlineObject o, ulong tGuid, SplinePath s, float sx, float sy, float sz)
        {
            PassengerInfo p;
            if (!_passengers.TryGetValue(o.Guid, out p)) _passengers[o.Guid] = p = new PassengerInfo();
            p.Transport = tGuid;
            p.Flags = 0;
            p.At = _now;
            p.LX = sx; p.LY = sy; p.LZ = sz;
            if (o.State != null) o.State.TransportGuid = tGuid;
            o.Spline = null;
            if (s == null) { p.LocalSpline = null; if (o.State != null) o.State.Moving = false; return; }
            s.StartTime = _now;
            p.LocalSpline = s;
            if (o.State != null) o.State.Moving = true;
        }

        /// <summary>A plain SMSG_MONSTER_MOVE: the unit is no longer moved in a transport's frame.</summary>
        void ClearPassengerSpline(OnlineObject o)
        {
            PassengerInfo p;
            if (o != null && _passengers.TryGetValue(o.Guid, out p) && p.LocalSpline != null) { _passengers.Remove(o.Guid); if (o.State != null) o.State.TransportGuid = 0; }
        }

        void TickPassengers()
        {
            _transportGone.Clear();
            foreach (var kv in _passengers)
            {
                OnlineObject o;
                if (!_objects.TryGetValue(kv.Key, out o) || o.State == null) { _transportGone.Add(kv.Key); continue; }
                var p = kv.Value;
                WowPos tp;
                if (!TryTransportPose(p.Transport, out tp)) continue;
                float lx = p.LX, ly = p.LY, lz = p.LZ, lo = p.LO;
                if (p.LocalSpline != null)
                {
                    float dir;
                    p.LocalSpline.Evaluate(_now, out lx, out ly, out lz, out dir);
                    if (p.LocalSpline.Finished(_now))
                    {
                        if (p.LocalSpline.FacingKind == 1) lo = p.LocalSpline.FacingAngle;
                        else if (p.LocalSpline.Count > 1) lo = dir;
                        p.LX = lx; p.LY = ly; p.LZ = lz; p.LO = lo;
                        p.LocalSpline = null;
                        o.State.Moving = false;
                    }
                    else lo = dir;
                }
                else if ((p.Flags & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight)) != 0)
                {
                    // a player walking on deck: dead-reckon in the transport's frame like Extrapolate does in the world
                    float t = (float)Math.Min(_now - p.At, PassengerExtrapolateMax);
                    float fx = 0f, fy = 0f;
                    if ((p.Flags & MoveFlags.Forward) != 0) fx += 1f;
                    if ((p.Flags & MoveFlags.Backward) != 0) fx -= 1f;
                    if ((p.Flags & MoveFlags.StrafeLeft) != 0) fy += 1f;
                    if ((p.Flags & MoveFlags.StrafeRight) != 0) fy -= 1f;
                    float len = (float)Math.Sqrt(fx * fx + fy * fy);
                    if (len > 0f)
                    {
                        bool back = (p.Flags & MoveFlags.Backward) != 0;
                        int type = (p.Flags & MoveFlags.Walking) != 0 ? MoveType.Walk : back ? MoveType.RunBack : MoveType.Run;
                        float d = o.Speeds[type] * t / len;
                        float c = (float)Math.Cos(lo), sn = (float)Math.Sin(lo);
                        lx += (fx * c - fy * sn) * d;
                        ly += (fx * sn + fy * c) * d;
                    }
                }
                WowPos w = ForcedMoveMath.PassengerPosition(lx, ly, lz, lo, tp);
                SetEntityPos(o, w.X, w.Y, w.Z, w.O);
            }
            foreach (ulong g in _transportGone) _passengers.Remove(g);
        }

        // ------------------------------------------------------------------ riding a ship across a map change

        /// <summary>Movement.cs OnNewWorld with a transport entry from SMSG_TRANSFER_PENDING: the packet's x y z o are our offsets on the
        /// ship. The world position follows our create block (Map.cpp:1653-1658: the transport's block first, then ours with
        /// ONTRANSPORT); meanwhile the loading screen aims at the path's arrival keyframe on the new map + the offsets. True = handled.</summary>
        bool TransportArrival(uint entry, int map, WowPos offsets)
        {
            _awaitTransportArrival = true;
            _arrivalAt = _now;
            _arrivalEntry = entry;
            _arrivalOffset = offsets;
            WowPos guess = _player.Pos;
            GoTemplateInfo tpl;
            var db = TaxiDbOnline();
            if (_goTemplates.TryGetValue((int)entry, out tpl) && tpl.Type == GoTypeMoTransport && db != null)
            {
                TransportPath path = null;
                try { path = TransportPath.Build(db.PathPoints((int)tpl.Data[0]), tpl.Data[1], tpl.Data[2]); } catch (Exception) { path = null; }
                WowPos arr;
                if (path != null && path.TryArrivalOnMap(map, out arr)) guess = ForcedMoveMath.PassengerPosition(offsets.X, offsets.Y, offsets.Z, offsets.O, arr);
            }
            _arrivalGuess = guess;
            HoldAt(guess, 30.0);
            SetPlayerTransportInternal(0, "the map changes on board");   // after HoldAt: nothing is sent before our WORLDPORT_ACK
            return true;
        }

        void NoteSelfOnTransport(MovementInfo mi)
        {
            if (!_awaitTransportArrival) return;
            if ((mi.Flags & MoveFlags.OnTransport) == 0 || !IsGoTransportGuid(mi.TransportGuid)) return;
            _awaitTransportArrival = false;
            WowPos tp, world;
            if (TryTransportPose(mi.TransportGuid, out tp)) world = ForcedMoveMath.PassengerPosition(mi.TX, mi.TY, mi.TZ, mi.TO, tp);
            else world = new WowPos(mi.X, mi.Y, mi.Z, mi.O);
            HoldAt(world, 20.0);
            _transportGuid = 0;
            SetPlayerTransportInternal(mi.TransportGuid, "arrived on board after the map change");
            TeleportEvent(world, _mapId);
            WowLog.Info("Online transport: arrived on board " + TransportName(mi.TransportGuid) + " on map " + _mapId + " at " + world + " (offsets " + mi.TX.ToString("0.0") + ", " +
                        mi.TY.ToString("0.0") + ", " + mi.TZ.ToString("0.0") + ")");
        }

        /// <summary>OnNewWorld: the transports and passengers of the old map are gone (Map::SendRemoveTransports, Map.cpp:1711-1728).</summary>
        void TransportClearWorld()
        {
            _transports.Clear();
            _passengers.Clear();
            _createProgress.Clear();
            _taxiStatusAsked.Clear();
        }
    }
}
