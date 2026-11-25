// iteration 11 - owner motion (proposal-motion.md 6.2.1): flight masters and flights online.
// Flight master (npc's H4 registry, flag 0x2000) -> CMSG_TAXIQUERYAVAILABLENODES -> SMSG_SHOWTAXINODES -> TaxiView (WoW's flight map,
// Game/Play/Flight/FlightMap.cs) -> TakeTaxi -> CMSG_ACTIVATETAXI / EXPRESS -> SMSG_ACTIVATETAXIREPLY -> the server's self Flying spline
// (SMSG_MONSTER_MOVE for our own guid, MoveSplineInit.cpp:124) -> TaxiStarted + ForcedMove Taxi (WowFlight rides it in the server's
// time) -> the server ends the flight (SMSG_DISMOUNT, TAXI_FLIGHT cleared) and WowFlight lands -> FinishTaxi (CMSG_MOVE_SPLINE_DONE).
// Two-map routes: at the map edge FinishTaxi sends CMSG_MOVE_SPLINE_DONE, the server far-teleports (TaxiHandler.cpp:216-241) and flies
// on from the arrival node (MovementHandler.cpp:217-226).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        /// <summary>WowSettings.TaxiMapAfterDiscovery, copied by WowGame (motion D-M3): open the flight map right after a new flight path.</summary>
        public static bool TaxiMapAfterDiscovery = true;
        /// <summary>WowSettings.VrFacingAssist &amp;&amp; VR, copied by WowGame (motion D-M5).</summary>
        public static bool FacingAssist;

        const uint NpcFlagFlightMaster = 0x2000;          // UNIT_NPC_FLAG_FLIGHTMASTER (UnitDefines.h:335)
        const uint UnitFlagTaxiFlight = 0x00100000;       // UNIT_FLAG_TAXI_FLIGHT (UnitDefines.h:277)
        const uint UnitFlagDisableMove = 0x00000004;      // UNIT_FLAG_DISABLE_MOVE (UnitDefines.h:259)
        // ours (logged once in InitTaxi)
        const double TaxiOkWindow = 10.0;                 // s: a Flying self spline this soon after ACTIVATETAXIREPLY(0) is the flight
        const double TaxiPendingHold = 1.0;               // s: a Flying self spline waits this long for TAXI_FLIGHT in our values
        const double TaxiServerEndGrace = 3.0;            // s: after the server ended a flight normally, we end it ourselves if WowFlight has not
        const double TaxiHopResend = 10.0, TaxiHopGiveUp = 20.0, TaxiContinueWait = 30.0;   // s: the two-map hop
        const double TaxiInstantWindow = 10.0;            // s: a teleport this soon after our ACTIVATETAXI without a reply = an instant flight
        const float TaxiDestinationRadius = 60f;          // yd: the TaxiNodes row nearest the spline's end names an unasked flight's destination
        const int TaxiStatusPerSecond = 5;                // flight-master status queries per second at most

        // the reply codes of SMSG_ACTIVATETAXIREPLY (SharedDefines.h:3849-3864) -> GlobalStrings.lua (lines 3515-3528)
        static readonly string[] TaxiReplyText =
        {
            null,                                                   // 0 ERR_TAXIOK
            "UNSPECIFIED TAXI SERVER ERROR",                        // 1 ERR_TAXIUNSPECIFIEDSERVERERROR (GS 3528)
            "There is no direct path to that destination!",         // 2 ERR_TAXINOSUCHPATH (GS 3517)
            "You don't have enough money!",                         // 3 ERR_TAXINOTENOUGHMONEY (GS 3518)
            "You are too far away from the taxi stand!",            // 4 ERR_TAXITOOFARAWAY (GS 3527)
            "There is no taxi vendor nearby!",                      // 5 ERR_TAXINOVENDORNEARBY (GS 3521)
            "You haven't reached that taxi node on foot yet!",      // 6 ERR_TAXINOTVISITED (GS 3520)
            "You are busy and can't use the taxi service now.",     // 7 ERR_TAXIPLAYERBUSY (GS 3523)
            "You are already mounted! Dismount first.",             // 8 ERR_TAXIPLAYERALREADYMOUNTED (GS 3522)
            "You can't take a taxi while shapeshifted!",            // 9 ERR_TAXIPLAYERSHAPESHIFTED (GS 3525)
            "You are moving.",                                      // 10 ERR_TAXIPLAYERMOVING (GS 3524)
            "You are already there!",                               // 11 ERR_TAXISAMENODE (GS 3526)
            "You need to be standing to go anywhere."               // 12 ERR_TAXINOTSTANDING (GS 3519)
        };
        const string TaxiNewPathText = "New flight path discovered!";                                   // ERR_NEWTAXIPATH (GS 3292)
        const string TaxiNoPathsText = "You don’t know any flight locations connected to this one.";  // ERR_TAXINOPATHS (GS 3516, the file's apostrophe)

        WowTaxiDb _taxiDbOnline;
        TaxiView _taxi;
        ulong _taxiMaster;
        readonly uint[] _taxiMask = new uint[14];         // TaxiMaskSize (DBCStructure.h:2284)
        int _taxiCurNode;
        List<int> _taxiPendingRoute;
        int _taxiPendingDest;
        double _taxiAskedAt = -100, _taxiOkAt = -100, _taxiHelloAt = -100, _taxiNewAt = -100;
        ulong _taxiHelloGuid;
        bool _taxiServerEnded, _selfTaxiFlight, _selfDisableMove, _selfMountFlag;
        double _taxiServerEndedAt, _taxiStartAt;
        uint _taxiSplineId;
        int _taxiDurationMs;
        WowPos _taxiEnd;
        SplinePath _taxiPendingSpline;                    // a Flying self spline waiting for TAXI_FLIGHT (login mid-flight: the pending rule)
        double _taxiPendingAt;
        bool _taxiHop, _taxiHopResent;
        double _taxiHopAt, _taxiHopArrivedAt = -100;
        int _taxiHopMap = -1;
        double _taxiHeartbeatAt = -1;
        readonly HashSet<ulong> _taxiStatusAsked = new HashSet<ulong>();
        double _taxiStatusTick;
        int _taxiStatusThisSecond;
        bool _taxiUnlockLogged;

        WowTaxiDb TaxiDbOnline()
        {
            if (_taxiDbOnline == null && _files != null)
            {
                try { _taxiDbOnline = WowTaxiDb.Get(_files); }
                catch (Exception e) { WowLog.Warn("Online taxi: reading the taxi tables failed: " + e.Message); }
            }
            return _taxiDbOnline;
        }

        partial void InitTaxi()
        {
            RegisterNpcService(NpcFlagFlightMaster, TaxiOpen, "flight master");
            _world.On(Op.SMSG_SHOWTAXINODES, OnShowTaxiNodes, this);
            _world.On(Op.SMSG_TAXINODE_STATUS, OnTaxiNodeStatus, this);
            _world.On(Op.SMSG_NEW_TAXI_PATH, OnNewTaxiPath, this);
            _world.On(Op.SMSG_ACTIVATETAXIREPLY, OnActivateTaxiReply, this);
            WowLog.Info("Online taxi: ours - a Flying self spline within " + TaxiOkWindow + " s of ACTIVATETAXIREPLY(0) is the flight, held " + TaxiPendingHold +
                        " s for TAXI_FLIGHT otherwise; server-end grace " + TaxiServerEndGrace + " s; two-map hop: SPLINE_DONE again after " + TaxiHopResend +
                        " s, given up after " + TaxiHopGiveUp + " s, continuation awaited " + TaxiContinueWait + " s; instant-flight window " + TaxiInstantWindow +
                        " s; unasked destination = the node within " + TaxiDestinationRadius + " yd of the spline's end; " + TaxiStatusPerSecond +
                        " flight-master status queries per second; flight map after a discovery " + (TaxiMapAfterDiscovery ? "on" : "off") + ".");
        }

        // ------------------------------------------------------------------ IWowSession

        public TaxiView CurrentTaxi { get { return _taxi; } }

        /// <summary>The 3.3.5 client sends nothing when the TaxiFrame closes.</summary>
        public void CloseTaxi() { _taxi = null; }

        /// <summary>CMSG_ACTIVATETAXI (uint64 guid, uint32 from, uint32 to: TaxiHandler.cpp:278) for one hop, CMSG_ACTIVATETAXIEXPRESS
        /// (uint64 guid, uint32 count, uint32 node[count], node[0] = the current node: TaxiHandler.cpp:170-193) for a multi-hop route.
        /// True = asked (WowFlight waits for TaxiStarted); the server's reply code is the truth (no client-side money / mount check).</summary>
        public bool TakeTaxi(int destinationNodeId)
        {
            if (_taxi == null || _world == null || _taxiMaster == 0) { Error(TaxiReplyText[5]); return false; }
            TaxiNodeView node = null;
            foreach (var v in _taxi.Nodes) if (v.Id == destinationNodeId) { node = v; break; }
            if (node == null || !node.Reachable || node.Route == null || node.Route.Count == 0) { Error(TaxiReplyText[2]); return false; }
            var db = TaxiDbOnline();
            var nodes = new List<int> { _taxiCurNode };
            foreach (int pathId in node.Route)
            {
                var pi = db != null ? db.Path(pathId) : null;
                if (pi == null) { Error(TaxiReplyText[2]); WowLog.Warn("Online taxi: TaxiPath " + pathId + " unknown - not asked"); return false; }
                nodes.Add(pi.To);
            }
            if (node.Route.Count == 1)
                _world.Send(Op.CMSG_ACTIVATETAXI, new PacketWriter().U64(_taxiMaster).U32((uint)_taxiCurNode).U32((uint)destinationNodeId));
            else
            {
                var w = new PacketWriter().U64(_taxiMaster).U32((uint)nodes.Count);
                foreach (int n in nodes) w.U32((uint)n);
                _world.Send(Op.CMSG_ACTIVATETAXIEXPRESS, w);
            }
            _taxiPendingRoute = new List<int>(node.Route);
            _taxiPendingDest = destinationNodeId;
            _taxiAskedAt = _now;
            WowLog.Info("Online taxi: " + (node.Route.Count == 1 ? "CMSG_ACTIVATETAXI" : "CMSG_ACTIVATETAXIEXPRESS") + " " + string.Join(" -> ", nodes) +
                        " '" + (node.Name ?? "?") + "', paths [" + string.Join(",", node.Route) + "], " + node.Cost + " copper on the map (the server charges)");
            return true;
        }

        /// <summary>WowFlight at the local end of the server's spline (or when it gave the avatar back early).</summary>
        public void FinishTaxi()
        {
            if (!_player.OnTaxi) return;
            WowPos landing = _haveAvatar ? _avatarPos : _player.Pos;
            if (!_taxiServerEnded && _world != null && !_world.IsClosed && _player.Guid != 0)
            {
                // CMSG_MOVE_SPLINE_DONE: packed guid, MovementInfo, uint32 spline id (TaxiHandler.cpp:212-214)
                var w = new PacketWriter().PackedGuid(_player.Guid);
                OwnMovementInfo(0u, landing, landing.O).Write(w);
                w.U32(_taxiSplineId);
                _world.Send(Op.CMSG_MOVE_SPLINE_DONE, w);
            }
            var fm = _player.ForcedMove;
            if (fm != null && fm.Kind == ForcedMoveKind.Taxi && fm.ContinuesOnMap >= 0 && _selfTaxiFlight)
            {
                _taxiHop = true;
                _taxiHopAt = _now;
                _taxiHopResent = false;
                _taxiHopMap = fm.ContinuesOnMap;
                EndForcedMove("hop");
                WowLog.Info("Online taxi: map edge reached - CMSG_MOVE_SPLINE_DONE sent (spline " + _taxiSplineId + "), waiting for the server's teleport to map " + _taxiHopMap);
                return;
            }
            EndTaxi(landing, null);
        }

        /// <summary>motion 6.3.2 fallback (only when npc's NpcRoute is not built): a flight master without the gossip / questgiver flags
        /// opens the taxi map; true = handled.</summary>
        public bool TryTaxiInteract(EntityState e, OnlineObject o)
        {
            if (e == null || o == null) return false;
            uint npc = o.U32(UF.UNIT_NPC_FLAGS);
            if ((npc & NpcFlagFlightMaster) == 0 || (npc & 0x3u) != 0) return false;
            return TaxiOpen(e.Guid);
        }

        // ------------------------------------------------------------------ the flight master and the map

        /// <summary>npc's registry opener for 0x2000: CMSG_TAXIQUERYAVAILABLENODES (uint64 guid, TaxiHandler.cpp:62-63).</summary>
        bool TaxiOpen(ulong guid)
        {
            if (_world == null || guid == 0) return false;
            _world.Send(Op.CMSG_TAXIQUERYAVAILABLENODES, new PacketWriter().U64(guid));
            _taxiHelloGuid = guid;
            _taxiHelloAt = _now;
            var e = Find(guid);
            WowLog.Info("Online taxi: CMSG_TAXIQUERYAVAILABLENODES to " + (e != null ? e.Name : "?") + " (" + guid.ToString("X16") + ")");
            return true;
        }

        /// <summary>SMSG_SHOWTAXINODES (TaxiHandler.cpp:98-103): uint32 1, uint64 flight master, uint32 current node, uint32 taximask[14].</summary>
        void OnShowTaxiNodes(WowPacket p)
        {
            uint unk = p.U32();
            ulong guid = p.U64();
            int cur = (int)p.U32();
            for (int i = 0; i < _taxiMask.Length; i++) _taxiMask[i] = p.Remaining >= 4 ? p.U32() : 0u;
            if (unk != 1) WowLog.Warn("Online taxi: SMSG_SHOWTAXINODES leading uint32 " + unk + " (expected 1)");
            var known = WowTaxiDb.KnownFromMask(_taxiMask);
            _player.KnownTaxiNodes.Clear();
            _player.KnownTaxiNodes.AddRange(known);
            if (Offline.OfflineSession.UnlockAllFlightPaths && !_taxiUnlockLogged)
            {
                _taxiUnlockLogged = true;
                WowLog.Info("Online taxi: 'unlock all flight paths' is an offline setting - online the server's taximask is the only truth (logged once)");
            }
            _taxiMaster = guid;
            _taxiCurNode = cur;
            _taxi = BuildOnlineTaxiView(guid, cur, known);
            NpcServiceOpened(guid, "taxi");                 // npc H4: the gossip dialog that led here closes (the client hides its GossipFrame)
            int reachable = 0;
            foreach (var v in _taxi.Nodes) if (v.Reachable) reachable++;
            var db = TaxiDbOnline();
            var ni = db != null ? db.Node(cur) : null;
            var master = Find(guid);
            if (reachable == 0) Error(TaxiNoPathsText);
            WowLog.Info("Online taxi: map of node " + cur + " '" + (ni != null ? ni.Name : "?") + "' from " + (master != null ? master.Name : guid.ToString("X")) + " - " +
                        known.Count + " known, " + reachable + " reachable, " + _taxi.Nodes.Count + " listed");
        }

        TaxiView BuildOnlineTaxiView(ulong master, int cur, List<int> knownList)
        {
            var db = TaxiDbOnline();
            var known = new HashSet<int>(knownList);
            known.Add(cur);
            var curNode = db != null ? db.Node(cur) : null;
            int map = curNode != null ? curNode.MapId : _mapId;
            var view = new TaxiView { MasterGuid = master, CurrentNodeId = cur, MapId = map };
            if (db == null || !db.Available) return view;
            var f = _player.Faction;
            bool haveCur = false;
            foreach (var n in db.ViewNodes(map, f))
            {
                var v = new TaxiNodeView { Id = n.Id, Name = n.Name, MapId = n.MapId, X = n.X, Y = n.Y, Z = n.Z };
                v.Known = known.Contains(n.Id);
                v.Current = n.Id == cur;
                if (v.Current) haveCur = true;
                else
                {
                    var route = v.Known ? db.FindRoute(cur, n.Id, f, known, true) : null;
                    if (route != null)
                    {
                        v.Reachable = true;
                        v.Route.AddRange(route);
                        v.Cost = db.RouteCost(route);          // without the server's reputation discount (known gap: the server charges right)
                    }
                    var direct = db.PathBetween(cur, n.Id);
                    v.PathId = direct != null && db.IsFlightMasterNode(n.Id, f) ? direct.Id : 0;
                }
                view.Nodes.Add(v);
            }
            if (!haveCur && curNode != null)
                view.Nodes.Add(new TaxiNodeView { Id = curNode.Id, Name = curNode.Name, MapId = curNode.MapId, X = curNode.X, Y = curNode.Y, Z = curNode.Z, Known = true, Current = true });
            return view;
        }

        /// <summary>SMSG_TAXINODE_STATUS (TaxiHandler.cpp:53-55): uint64 guid, uint8 known.</summary>
        void OnTaxiNodeStatus(WowPacket p)
        {
            ulong guid = p.U64();
            int known = p.U8();
            var e = Find(guid);
            if (e != null) e.TaxiStatus = (sbyte)(known != 0 ? 1 : 0);
            // D-M3 (ours, default on): SendLearnNewTaxiNode sends NEW_TAXI_PATH then STATUS(master, 1) (TaxiHandler.cpp:136-146) and
            // returns without the map; ask again at once so the map opens like offline (the server now knows the node: :78-82).
            if (known == 1 && _now - _taxiNewAt < 1.0 && TaxiMapAfterDiscovery && _world != null)
            {
                _taxiNewAt = -100;
                WowLog.Info("Online taxi: new flight path at " + (e != null ? e.Name : guid.ToString("X")) + " - asking for the flight map at once (setting 'Flight map after a discovery')");
                TaxiOpen(guid);
            }
        }

        /// <summary>SMSG_NEW_TAXI_PATH (empty: TaxiHandler.cpp:138-139, 154-162).</summary>
        void OnNewTaxiPath(WowPacket p)
        {
            _taxiNewAt = _now;
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = TaxiNewPathText });
            WowLog.Info("Online taxi: SMSG_NEW_TAXI_PATH - " + TaxiNewPathText);
        }

        /// <summary>SMSG_ACTIVATETAXIREPLY (TaxiHandler.cpp:303-304): uint32 reply.</summary>
        void OnActivateTaxiReply(WowPacket p)
        {
            uint code = p.U32();
            if (code == 0)
            {
                _taxiOkAt = _now;
                _taxi = null;                                // the map closes
                WowLog.Info("Online taxi: SMSG_ACTIVATETAXIREPLY ERR_TAXIOK - the flight starts");
                return;
            }
            string text = code < TaxiReplyText.Length ? TaxiReplyText[code] : null;
            Error(text ?? TaxiReplyText[1]);
            WowLog.Info("Online taxi: SMSG_ACTIVATETAXIREPLY " + code + " '" + (text ?? "?") + "'");
            _taxiPendingRoute = null;
            _taxiPendingDest = 0;
        }

        // ------------------------------------------------------------------ the flight

        /// <summary>The taxi start rule for a Flying self spline (Forced.cs).</summary>
        bool TaxiStartRule()
        {
            return _now - _taxiOkAt < TaxiOkWindow || _selfTaxiFlight || (_selfDisableMove && _selfMountFlag) || (_taxiHop && _player.OnTaxi) || (_player.OnTaxi && _now - _taxiHopArrivedAt < TaxiContinueWait);
        }

        /// <summary>A Flying self spline: the flight (or its continuation after a hop), or held for up to TaxiPendingHold s.</summary>
        void TaxiOnFlyingSpline(SplinePath s, float sx, float sy, float sz, double age)
        {
            if (TaxiStartRule()) { StartTaxi(s, age); return; }
            _taxiPendingSpline = s;
            _taxiPendingAt = _now;
            WowLog.Info("Online taxi: a Flying self spline " + s.Id + " without TAXI_FLIGHT yet - held up to " + TaxiPendingHold + " s (login mid-flight / quest flight)");
        }

        void StartTaxi(SplinePath s, double age)
        {
            _taxiPendingSpline = null;
            var db = TaxiDbOnline();
            bool continuing = _player.OnTaxi && (_taxiHop || _now - _taxiHopArrivedAt < TaxiContinueWait);
            int n = s.Count;
            float lx = s.Pts[n * 3 - 3], ly = s.Pts[n * 3 - 2], lz = s.Pts[n * 3 - 1];
            if (!continuing)
            {
                _player.TaxiRoute.Clear();
                if (_taxiPendingRoute != null) _player.TaxiRoute.AddRange(_taxiPendingRoute);
                _player.TaxiPathId = _player.TaxiRoute.Count > 0 ? _player.TaxiRoute[0] : 0;
                _player.TaxiDestinationNode = _taxiPendingDest != 0 ? _taxiPendingDest : (db != null ? db.NearestNode(_mapId, lx, ly, lz, TaxiDestinationRadius) : 0);
            }
            _taxiHop = false;
            _taxiHopArrivedAt = -100;
            _player.OnTaxi = true;
            RefreshControl();
            int display = _self != null ? _self.I32(UF.UNIT_FIELD_MOUNTDISPLAYID) : 0;
            if (display <= 0 && db != null) display = db.MountDisplayId(_taxiCurNode, _player.Faction);
            if (display > 0) _player.MountDisplayId = display;
            int contMap = db != null ? db.ContinuesOnMap(_player.TaxiRoute, _mapId, lx, ly, lz) : -1;
            var pts = s.Pts.ToArray();
            var m = new ForcedMoveView
            {
                Kind = ForcedMoveKind.Taxi, Points = pts, DurationMs = s.DurationMs, Smooth = true, ContinuesOnMap = contMap,
                Source = "SMSG_MONSTER_MOVE spline " + s.Id + " (taxi)", AgeAtPublish = (float)Math.Max(0, age)
            };
            _taxiSplineId = s.Id;
            _taxiStartAt = _now - Math.Max(0, age);
            _taxiDurationMs = s.DurationMs;
            _taxiEnd = new WowPos(lx, ly, lz, 0f);
            _taxiServerEnded = false;
            _sentFlags = 0;
            PublishForcedMove(m, 0);
            var dest = db != null ? db.Node(_player.TaxiDestinationNode) : null;
            string destName = dest != null ? dest.Name : "";
            _events.Enqueue(new GameEvent
            {
                Type = GameEventType.TaxiStarted, SourceGuid = _player.Guid, TargetGuid = _taxiMaster, Amount = _player.TaxiPathId,
                Pos = new WowPos(s.Pts[0], s.Pts[1], s.Pts[2], 0f), Text = continuing ? "continuing to " + destName : destName
            });
            float len = s.Length;
            double secs = s.DurationMs / 1000.0;
            WowLog.Info("Online taxi: flight " + (continuing ? "continues" : "started") + " - spline " + s.Id + ", " + n + " points, " + len.ToString("0") + " yd, " +
                        secs.ToString("0.0") + " s (server 32 yd/s => " + (secs > 0 ? (len / secs).ToString("0.0") : "?") + "), mount display " + _player.MountDisplayId +
                        ", route [" + string.Join(",", _player.TaxiRoute) + "] -> node " + _player.TaxiDestinationNode + " '" + destName + "'" +
                        (contMap >= 0 ? ", continues on map " + contMap + " after the spline" : "") + (age > 0.01 ? ", " + age.ToString("0.00") + " s old" : ""));
            _taxiPendingRoute = null;
            _taxiPendingDest = 0;
        }

        /// <summary>The flight is over (landed, or cut short): every taxi field back, TaxiFinished at pos.</summary>
        void EndTaxi(WowPos pos, string cutShort)
        {
            if (!_player.OnTaxi) return;
            _player.OnTaxi = false;
            _player.TaxiRoute.Clear();
            _player.TaxiPathId = 0;
            _player.TaxiDestinationNode = 0;
            if (!_player.Mounted) _player.MountDisplayId = 0;
            _taxiHop = false;
            _taxiHopArrivedAt = -100;
            _taxiPendingSpline = null;
            RefreshControl();
            var fm = _player.ForcedMove;
            if (fm != null && fm.Kind == ForcedMoveKind.Taxi) EndForcedMove(cutShort ?? null);
            _events.Enqueue(new GameEvent { Type = GameEventType.TaxiFinished, SourceGuid = _player.Guid, Pos = pos });
            _taxiHeartbeatAt = _now + 1.0;                   // ours: one heartbeat a second later, so the server holds the avatar's ground spot
            WowLog.Info("Online taxi: flight " + (cutShort != null ? "cut short (" + cutShort + ")" : "ended") + " at " + pos + (_taxiServerEnded ? " (the server ended it)" : ""));
        }

        /// <summary>Teleport / new world while on a taxi that is not a hop: end at once.</summary>
        void TaxiCutShort(string why)
        {
            if (!_player.OnTaxi) return;
            EndTaxi(_haveAvatar ? _avatarPos : _player.Pos, why);
        }

        /// <summary>OnlineSession.Mounts.cs UpdateSelfMount hook: the taxi's mount is not a ridden mount (Unit::Mount at
        /// TaxiHandler.cpp:119-120). True = handled (no Mounted / Dismounted event).</summary>
        bool TaxiOwnsMount(OnlineObject o)
        {
            uint flags = o.U32(UF.UNIT_FIELD_FLAGS);
            bool taxiFlag = (flags & UnitFlagTaxiFlight) != 0;
            if (!_player.OnTaxi && !taxiFlag && _taxiPendingSpline == null) return false;
            if (_player.Mounted) return false;               // a real mount was up before: the normal rules
            int display = o.I32(UF.UNIT_FIELD_MOUNTDISPLAYID);
            if (display > 0) _player.MountDisplayId = display;
            return true;
        }

        /// <summary>OnlineSession.Mounts.cs OnDismount hook (self): SMSG_DISMOUNT while on a taxi = the server ended the flight (DoFinalize).</summary>
        bool TaxiDismount()
        {
            if (!_player.OnTaxi) return false;
            TaxiServerEnd("SMSG_DISMOUNT");
            return true;
        }

        void TaxiServerEnd(string why)
        {
            if (_taxiServerEnded || !_player.OnTaxi || _taxiHop) return;
            _taxiServerEnded = true;
            _taxiServerEndedAt = _now;
            double flown = _now - _taxiStartAt;
            bool normal = _taxiDurationMs <= 0 || flown >= _taxiDurationMs / 1000.0 - 2.0;
            WowLog.Info("Online taxi: the server ended the flight (" + why + ") after " + flown.ToString("0.0") + " of " + (_taxiDurationMs / 1000.0).ToString("0.0") + " s - " +
                        (normal ? "landing" : "cut short"));
            if (!normal) EndTaxi(_haveAvatar ? _avatarPos : _player.Pos, "stopped by the server");
        }

        /// <summary>Forced.cs RefreshSelfMotion: our UNIT_FIELD_FLAGS taxi bits.</summary>
        void TaxiSelfFlags(uint flags)
        {
            bool was = _selfTaxiFlight;
            _selfTaxiFlight = (flags & UnitFlagTaxiFlight) != 0;
            _selfDisableMove = (flags & UnitFlagDisableMove) != 0;
            _selfMountFlag = (flags & UnitFlagMount) != 0;
            if (_selfTaxiFlight && !was && _taxiPendingSpline != null) StartTaxi(_taxiPendingSpline, _now - _taxiPendingAt);
            else if (was && !_selfTaxiFlight && _player.OnTaxi) TaxiServerEnd("UNIT_FLAG_TAXI_FLIGHT cleared");
        }

        partial void TickTaxi(float dt)
        {
            if (_world == null) return;
            // a held Flying spline without TAXI_FLIGHT: after TaxiPendingHold s it is an ordinary server path
            if (_taxiPendingSpline != null && _now - _taxiPendingAt > TaxiPendingHold)
            {
                var s = _taxiPendingSpline;
                _taxiPendingSpline = null;
                WowLog.Info("Online taxi: the held Flying spline " + s.Id + " got no TAXI_FLIGHT within " + TaxiPendingHold + " s - followed as a server path (logged once per spline)");
                PublishSelfPath(s, _now - _taxiPendingAt, ForcedMoveKind.Path);
            }
            if (_player.OnTaxi)
            {
                if (_taxiServerEnded && !_taxiHop && _now - _taxiServerEndedAt > TaxiServerEndGrace)
                {
                    WowLog.Info("Online taxi: WowFlight did not finish " + TaxiServerEndGrace + " s after the server's end - finishing at the spline's end");
                    EndTaxi(_taxiEnd, null);
                }
                if (_taxiHop)
                {
                    if (!_taxiHopResent && _now - _taxiHopAt > TaxiHopResend && _player.Guid != 0)
                    {
                        _taxiHopResent = true;
                        var w = new PacketWriter().PackedGuid(_player.Guid);
                        OwnMovementInfo(0u, _haveAvatar ? _avatarPos : _player.Pos, _player.Pos.O).Write(w);
                        w.U32(_taxiSplineId);
                        _world.Send(Op.CMSG_MOVE_SPLINE_DONE, w);
                        WowLog.Warn("Online taxi: no teleport " + TaxiHopResend + " s after CMSG_MOVE_SPLINE_DONE at the map edge - sent it once more");
                    }
                    else if (_now - _taxiHopAt > TaxiHopGiveUp)
                    {
                        WowLog.Warn("Online taxi: the server did not continue the two-map flight within " + TaxiHopGiveUp + " s - ending it here");
                        EndTaxi(_haveAvatar ? _avatarPos : _player.Pos, "the two-map hop timed out");
                    }
                }
                else if (_taxiHopArrivedAt > 0 && _now - _taxiHopArrivedAt > TaxiContinueWait && _player.ForcedMove == null)
                {
                    WowLog.Warn("Online taxi: no continuation spline " + TaxiContinueWait + " s after the map change - ending the flight");
                    EndTaxi(_haveAvatar ? _avatarPos : _player.Pos, "no continuation after the map change");
                }
            }
            else if (_taxiHeartbeatAt > 0 && _now >= _taxiHeartbeatAt)
            {
                _taxiHeartbeatAt = -1;
                if (_haveAvatar && _holdUntil < 0 && _player.ForcedMove == null && !_player.ControlLost && !_rooted && !_motion.Driven && _player.Guid != 0)
                    SendMove(Op.MSG_MOVE_HEARTBEAT, 0u, _avatarPos, _avatarPos.O, 0, 0, 0, 0, 0);
            }
            // instant flights (worldserver InstantFlightPaths): a teleport soon after our ACTIVATETAXI and no reply
            if (_taxiPendingRoute != null && _taxiOkAt < _taxiAskedAt && _now - _taxiAskedAt > TaxiInstantWindow)
            {
                WowLog.Info("Online taxi: no SMSG_ACTIVATETAXIREPLY " + TaxiInstantWindow + " s after the request - forgetting the pending route");
                _taxiPendingRoute = null;
                _taxiPendingDest = 0;
            }
            TickTaxiStatus();
        }

        /// <summary>A teleport arrived (Movement.cs): after our request with no reply it was an instant flight.</summary>
        void TaxiNoteTeleport()
        {
            if (_taxiPendingRoute != null && _taxiOkAt < _taxiAskedAt && _now - _taxiAskedAt < TaxiInstantWindow)
            {
                WowLog.Info("Online taxi: instant flight - the server teleported us (InstantFlightPaths)");
                _taxiPendingRoute = null;
                _taxiPendingDest = 0;
                _taxi = null;
            }
        }

        /// <summary>CMSG_TAXINODE_STATUS_QUERY (uint64 guid, TaxiHandler.cpp:31) for every flight master in sight, once each.</summary>
        void TickTaxiStatus()
        {
            if (_now - _taxiStatusTick >= 1.0) { _taxiStatusTick = _now; _taxiStatusThisSecond = 0; }
            if (_taxiStatusThisSecond >= TaxiStatusPerSecond) return;
            foreach (var o in _objects.Values)
            {
                var e = o.State;
                if (e == null || e.Kind != EntityKind.Npc || e.TaxiStatus >= 0 || e.Dead) continue;
                if ((o.U32(UF.UNIT_NPC_FLAGS) & NpcFlagFlightMaster) == 0 || !_taxiStatusAsked.Add(o.Guid)) continue;
                _world.Send(Op.CMSG_TAXINODE_STATUS_QUERY, new PacketWriter().U64(o.Guid));
                if (++_taxiStatusThisSecond >= TaxiStatusPerSecond) return;
            }
        }
    }
}
