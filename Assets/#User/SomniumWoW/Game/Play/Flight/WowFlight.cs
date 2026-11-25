using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Flight;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Flights (iteration 4). The flight map while the session has a flight master's taxi view open (IWowSession.CurrentTaxi), and
    /// the flight itself on TaxiStarted: the avatar seated on the taxi mount (Player.MountDisplayId), carried along the stitched
    /// TaxiPathNode route at 32 yd/s (Catmull-Rom, as the server's flying spline), the world streamed ahead, landed on the ground at
    /// the end (IWowSession.FinishTaxi). WowSettings.FastTravel: fade out, jump to the landing point once it has loaded, fade in.
    /// The front end adds this component (WowGame.Awake).
    /// Other modules: while <see cref="Flying"/> the flight owns the avatar's transform (no teleports, no fall rescue) and the
    /// streaming focus is <see cref="StreamFocus"/>; <see cref="PointerOnMap"/> = the VR ray is on the flight map (treat as UI).
    /// </summary>
    // Lead fix 2026-10-04 (the user: "On the griffons, I am vibrating in Non-XR"): the ride moved the avatar only in LateUpdate (order 0),
    // while the client places a desktop avatar in Update (NonXRRootsMovementHandler, see TransportCarry.cs) - two writers a frame apart.
    // Now as the ground mounts do (WowMount, -500): Update advances the ride and writes the root at the saddle BEFORE the client's
    // order-0 scripts (MountSeat.WriteRoot), LateUpdate holds the pose and the seat (MountSeat.Seat) before the chase camera (10000).
    [DefaultExecutionOrder(-500)]
    public sealed class WowFlight : MonoBehaviour
    {
        public static WowFlight Instance { get; private set; }
        /// <summary>A flight is carrying the avatar (from TaxiStarted until the landing).</summary>
        public static bool Flying { get; private set; }
        /// <summary>The flight map is on screen / in the world.</summary>
        public static bool MapOpen { get; private set; }
        /// <summary>VR: the right-hand ray (or a poke) is on the flight map this frame.</summary>
        public static bool PointerOnMap { get; private set; }
        /// <summary>While flying: where the world should stream (Unity, current origin frame) - a few seconds ahead on the route.</summary>
        public static Vector3 StreamFocus { get; private set; }

        private FlightData _data;
        private bool _dataTried;
        private FlightMap _map;
        private FlightRide _ride;
        private TaxiView _shownTaxi, _closedTaxi;
        private int _shownNodes = -1;
        private List<int> _pendingRoute;
        private int _pendingNode;
        private string _pendingName;
        private FlightFade _fadeIn;
        private float _fadeInTime;
        private bool _subscribed;

        // Iteration 11 (motion 6.2.8): online flights ride the SERVER's spline (PlayerState.ForcedMove Kind Taxi) with its timing.
        // ours (logged once): the deferral limit, the hop's black fade and the wait for the server's map change at a map edge.
        private const float DeferLimitSeconds = 60f;   // TaxiStarted while the world loads (login mid-flight, the two-map hop)
        private const float HopWaitSeconds = 30f;      // after the local end at a map edge: the server's far teleport is awaited this long
        private static bool _valuesLogged, _fastLogged;
        private GameEvent _taxiEvent;                  // a deferred online TaxiStarted (GameEvent is a struct: _taxiDeferred says it is set)
        private bool _taxiDeferred;
        private float _taxiEventAt;
        private bool _hopWaiting;                      // the spline ended at a map edge: waiting for the server's far teleport
        private float _hopAt;
        private FlightFade _hopFade;                   // black over the map change, faded in once the far teleport starts

        private void OnEnable()
        {
            Instance = this;
            if (!_subscribed)
            {
                _subscribed = true;
                WowPlay.Event += OnEvent;
                WowPlay.Ended += OnEnded;
                WowSpace.Rebased += OnRebased;
            }
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                _subscribed = false;
                WowPlay.Event -= OnEvent;
                WowPlay.Ended -= OnEnded;
                WowSpace.Rebased -= OnRebased;
            }
            if (_ride != null) EndRide(false, "the flight component was disabled");
            if (_hopWaiting) EndHop("the flight component was disabled");
            if (_fadeIn != null) { _fadeIn.Destroy(); _fadeIn = null; }
            if (_map != null) { _map.Destroy(); _map = null; }
            MapOpen = false;
            PointerOnMap = false;
            if (Instance == this) Instance = null;
        }

        private FlightData Data()
        {
            if (_data != null || _dataTried) return _data;
            _dataTried = true;
            if (WowPlay.Files == null) { _dataTried = false; return null; }
            try
            {
                _data = FlightData.Load(WowPlay.Files);
                WowLog.Info("Flight: " + _data.Summary + ".");
            }
            catch (Exception e)
            {
                WowLog.Error("Flight: reading the taxi tables failed: " + e);
                _data = null;
            }
            return _data;
        }

        // ------------------------------------------------------------------------------------------------ the map

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            TaxiView taxi = null;
            if (s != null && _ride == null)
            {
                try { taxi = s.CurrentTaxi; }
                catch (Exception e) { WowLog.Warn("Flight: CurrentTaxi threw " + e.Message); }
            }

            if (taxi != null && taxi == _closedTaxi) taxi = null; // closed with the button; the session may clear it a frame later
            else _closedTaxi = null;
            if (taxi != null && Data() != null)
            {
                if (_map == null)
                {
                    _map = new FlightMap();
                    _map.Fly = OnPickNode;
                    _map.CloseClicked = OnCloseButton;
                }
                if (!_map.Shown || taxi != _shownTaxi || taxi.Nodes.Count != _shownNodes)
                {
                    _shownTaxi = taxi;
                    _shownNodes = taxi.Nodes.Count;
                    string master = null;
                    Vector3? at = null;
                    EntityState e = taxi.MasterGuid != 0 ? s.Find(taxi.MasterGuid) : null;
                    if (e != null) { master = e.Name; at = WowSpace.ToUnity(s.MapId, e.Pos.X, e.Pos.Y, e.Pos.Z); }
                    try { _map.Show(taxi, _data, WowPlayer.XrPresent, master, at, s.Player != null ? s.Player.Faction : WowFaction.Alliance); }
                    catch (Exception ex) { WowLog.Error("Flight map: Show failed: " + ex); }
                }
                try { _map.Tick(); }
                catch (Exception ex) { WowLog.Warn("Flight map: Tick: " + ex.Message); }
            }
            else if (_map != null && _map.Shown)
            {
                _map.Hide();
                _shownTaxi = null;
                _shownNodes = -1;
            }
            MapOpen = _map != null && _map.Shown;
            PointerOnMap = MapOpen && _map.PointerOn;

            // The session ended the flight on its own (death, logout, a GM teleport): give the avatar back where it is.
            if (_ride != null && s != null && s.Player != null && !s.Player.OnTaxi && _ride.State == FlightRide.Phase.Flying && _ride.Travelled > 1.0)
                EndRide(false, "the session ended the flight (Player.OnTaxi is false)");

            // Iteration 11 (motion 6.2.8): an online TaxiStarted waits until the world around the avatar is ready (login mid-flight, the
            // continuation after a two-map hop), at most DeferLimitSeconds.
            if (_taxiDeferred && _ride == null && s != null)
            {
                if (s.Player == null || !s.Player.OnTaxi) { WowLog.Info("Flight: the deferred flight was ended by the session before it could start."); _taxiDeferred = false; }
                else if (WorldReady()) { _taxiDeferred = false; StartRide(_taxiEvent); }
                else if (Time.time - _taxiEventAt > DeferLimitSeconds)
                {
                    WowLog.Warn("Flight: the world was not ready " + DeferLimitSeconds + " s after TaxiStarted - finishing the flight without a ride.");
                    _taxiDeferred = false;
                    SafeFinish(s);
                }
            }
            // The map edge of a two-map route: black until the server's far teleport (Teleport event, OnEvent) or the session gives up.
            if (_hopWaiting && (s == null || s.Player == null || !s.Player.OnTaxi || Time.time - _hopAt > HopWaitSeconds))
                EndHop(s == null || s.Player == null || !s.Player.OnTaxi ? "the session ended the flight at the map edge" : "no map change after " + HopWaitSeconds + " s");
            // lead fix 2026-10-04: the ride steps here (Update, -500); LateUpdate only holds the seat
            if (_ride != null)
            {
                try { _rideDone = _ride.Tick(Time.deltaTime, MapView()); }
                catch (Exception e) { WowLog.Error("Flight: Tick threw " + e); _rideDone = true; }
                StreamFocus = _ride.StreamFocus;
            }
        }

        private bool _rideDone;

        /// <summary>FlightMap.Close (the registry's Taxi External spec): as the map's own close button.</summary>
        internal void CloseMap(string why)
        {
            WowLog.Info("Flight map: closed - " + why + ".");
            OnCloseButton();
        }

        /// <summary>The world around the avatar is ready for a ride (the same gate as WowMount's: the origin settled and the avatar out
        /// of the login room on loaded ground). Also used by TransportCarry for the arrival on a ship.</summary>
        internal static bool WorldReady()
        {
            WowGame g = WowGame.Current;
            Func<bool> gate = WowOrigin.MayRebaseOverride;
            bool settled;
            if (gate != null)
            {
                try { settled = gate(); } catch (Exception) { settled = false; }
            }
            else settled = g != null && g.OriginMayRebase;
            if (!settled) return false;
            Vector3 feet;
            if (!WowPlayer.TryFeet(out feet) || (feet - WowSpace.LoginRoomOrigin).magnitude < 60f) return false;
            WowMapView map = MapView();
            if (map == null) return gate != null;   // a lab without WowGame drives the world itself
            return map.IsReadyAround(feet, 10f);
        }

        private void EndHop(string why)
        {
            _hopWaiting = false;
            Flying = false;
            if (_hopFade != null) { if (_fadeIn != null) _fadeIn.Destroy(); _fadeIn = _hopFade; _fadeInTime = 0f; _hopFade = null; }
            WowLog.Info("Flight: map edge - " + why + "; the avatar is free again.");
        }

        private void OnPickNode(TaxiNodeView node)
        {
            IWowSession s = WowPlay.Session;
            if (s == null || node == null) return;
            _pendingRoute = node.Route != null ? new List<int>(node.Route) : new List<int>();
            if (_pendingRoute.Count == 0 && node.PathId != 0) _pendingRoute.Add(node.PathId);
            _pendingNode = node.Id;
            _pendingName = node.Name;
            bool ok = false;
            try { ok = s.TakeTaxi(node.Id); }
            catch (Exception e) { WowLog.Error("Flight: TakeTaxi(" + node.Id + ") threw " + e); }
            WowLog.Info("Flight map: " + (node.Name ?? node.Id.ToString()) + " picked (" + _pendingRoute.Count + " hop(s), " + node.Cost + " copper) - TakeTaxi " + (ok ? "accepted" : "refused") + ".");
            if (!ok) _pendingRoute = null;
        }

        private void OnCloseButton()
        {
            IWowSession s = WowPlay.Session;
            try { if (s != null) s.CloseTaxi(); }
            catch (Exception e) { WowLog.Warn("Flight: CloseTaxi threw " + e.Message); }
            if (_map != null) _map.Hide();
            _shownTaxi = null;
            _shownNodes = -1;
        }

        // ------------------------------------------------------------------------------------------------ the flight

        private void OnEvent(GameEvent e)
        {
            if (e.Type == GameEventType.TaxiStarted)
            {
                IWowSession s = WowPlay.Session;
                if (ServerTaxi(s) != null)
                {
                    // Online (6.2.8): ride the server's spline; deferred to Update until the world is ready (often at once).
                    if (!_valuesLogged)
                    {
                        _valuesLogged = true;
                        WowLog.Info("Flight: online rides follow the server's spline and timing (ours: start deferred up to " + DeferLimitSeconds + " s while the world loads, map-edge wait " + HopWaitSeconds + " s, 0.4 s black at a map edge).");
                    }
                    _taxiEvent = e;
                    _taxiDeferred = true;
                    _taxiEventAt = Time.time;
                    if (_hopWaiting) EndHop("the flight continues");
                    if (_map != null) _map.Hide();
                    return;
                }
                StartRide(e);
            }
            else if (e.Type == GameEventType.Teleport && _hopWaiting)
                EndHop("the server's far teleport started (the loading screen takes over)");
        }

        /// <summary>The session's taxi spline (online), or null (offline: the DBC route).</summary>
        private static ForcedMoveView ServerTaxi(IWowSession s)
        {
            ForcedMoveView m = s != null && s.Player != null ? s.Player.ForcedMove : null;
            return m != null && m.Kind == ForcedMoveKind.Taxi && m.Points != null && m.Points.Length >= 6 ? m : null;
        }

        private void StartRide(GameEvent e)
        {
            IWowSession s = WowPlay.Session;
            if (s == null) return;
            WowMount.LetGo("flight", false);   // iteration 7 (Ride, defensive): a ride never shares the avatar with a flight
            if (_ride != null) EndRide(false, "a new flight started");
            if (_map != null) _map.Hide();
            FlightData data = Data();
            if (data == null) { WowLog.Error("Flight: TaxiStarted but the taxi tables are missing - finishing at once."); SafeFinish(s); return; }

            var ids = new List<int>();
            if (s.Player != null && s.Player.TaxiRoute != null && s.Player.TaxiRoute.Count > 0) ids.AddRange(s.Player.TaxiRoute);
            else if (_pendingRoute != null && _pendingRoute.Count > 0 && (e.Amount == 0 || _pendingRoute.Contains(e.Amount))) ids.AddRange(_pendingRoute);
            else if (e.Amount != 0) ids.Add(e.Amount);
            if (ids.Count == 0 && s.Player != null && s.Player.TaxiPathId != 0) ids.Add(s.Player.TaxiPathId);
            string name = !string.IsNullOrEmpty(e.Text) ? e.Text : _pendingName;
            _pendingRoute = null;

            string note;
            List<FlightPoint> route;
            ForcedMoveView server = ServerTaxi(s);
            if (server != null)
            {
                // Online: the server's own Flying spline (its first point is our server position, MoveSplineInit.cpp:65), not the DBC route.
                route = new List<FlightPoint>(server.Points.Length / 3);
                for (int i = 0; i + 2 < server.Points.Length; i += 3)
                    route.Add(new FlightPoint(s.MapId, server.Points[i], server.Points[i + 1], server.Points[i + 2], 0, 0));
                note = "the server's spline, " + (server.DurationMs / 1000.0).ToString("0.0") + " s" + (server.ContinuesOnMap >= 0 ? ", continues on map " + server.ContinuesOnMap : "");
                if (WowSettings.FastTravel && !_fastLogged) { _fastLogged = true; WowLog.Info("Flight: fast travel is offline only - riding the server's flight."); }
            }
            else route = data.BuildRoute(ids, out note);
            int display = s.Player != null ? s.Player.MountDisplayId : 0;
            if (display <= 0)
            {
                // The taxi mount's creature (TaxiNodes MountCreatureID) needs the server's creature table for its display; until the
                // session provides one: the Alliance taxi gryphon / Horde wind rider models (CreatureDisplayInfo 1149 Gryphon.m2
                // scale 1, 295 Wyvern.m2 scale 1 - read from the client's tables).
                display = s.Player != null && s.Player.Faction == WowFaction.Horde ? 295 : 1149;
                WowLog.Warn("Flight: Player.MountDisplayId is 0 - using display " + display + ".");
            }
            WowPos from = s.Player != null ? s.Player.Pos : e.Pos;
            var ride = new FlightRide { Destination = string.IsNullOrEmpty(name) ? "node " + _pendingNode : name };
            ride.Camera = GetComponent<MountCamera>();   // iteration 7 (Ride P2): the desktop chase camera WowMount added to this object (null = the client camera)
            if (server != null && route.Count > 0)
            {
                from = new WowPos(route[0].X, route[0].Y, route[0].Z, from.O);   // nothing is prepended: the spline starts where the server has us
                ride.ServerSeconds = server.DurationMs / 1000.0;
                ride.ServerAge = server.AgeAtPublish + Math.Max(0f, Time.time - _taxiEventAt);
                ride.HopAtEnd = server.ContinuesOnMap >= 0;
            }
            string why;
            bool ok = false;
            try { ok = ride.Begin(route, from, display, WowPlay.Files, WowSettings.FastTravel && server == null, out why); }
            catch (Exception ex) { why = ex.GetType().Name + ": " + ex.Message; WowLog.Error("Flight: Begin threw " + ex); }
            WowLog.Info("Flight: TaxiStarted path " + e.Amount + ", route of " + ids.Count + " path(s) [" + string.Join(",", ids) + "] -> " + route.Count + " nodes" + (note.Length > 0 ? " (" + note + ")" : "") + ".");
            if (!ok)
            {
                WowLog.Error("Flight: cannot fly (" + why + ") - landing at the destination at once.");
                if (route.Count > 0) { string how; FlightPoint end = route[route.Count - 1]; WowPlayer.Teleport(WowSpace.ToUnity(end.Map, end.X, end.Y, end.Z), 0f, out how); }
                SafeFinish(s);
                return;
            }
            _ride = ride;
            _rideDone = false;   // lead fix 2026-10-04
            Flying = true;
            WowOrigin.AvatarDriven = true;   // integrator: a rebase mid-flight must not teleport the avatar (OnRebased rewrites it)
            StreamFocus = ride.StreamFocus;
        }

        private void LateUpdate()
        {
            if (_fadeIn != null)
            {
                _fadeInTime += Time.deltaTime;
                _fadeIn.Set(1f - Mathf.Clamp01(_fadeInTime / 0.6f));
                if (_fadeInTime >= 0.6f) { _fadeIn.Destroy(); _fadeIn = null; }
            }
            if (_hopFade != null) _hopFade.Set(1f);
            if (_ride == null) return;
            bool done = _rideDone;
            try { _ride.LateSeat(); }   // lead fix 2026-10-04: the pose and seat held after the client's Update (the ride stepped in Update)
            catch (Exception e) { WowLog.Error("Flight: LateSeat threw " + e); done = true; }
            IWowSession s = WowPlay.Session;
            if (s != null && _ride != null && _ride.State == FlightRide.Phase.Flying)
            {
                // The session hears where the player is along the route (the avatar's own position follows the same frame).
                Vector3 feet;
                if (WowPlayer.TryFeet(out feet))
                {
                    float x, y, z;
                    WowSpace.ToWow(s.MapId, feet, out x, out y, out z);
                    try { s.SetPlayerPosition(new WowPos(x, y, z, _ride.End.O), true); } catch (Exception) { }
                }
            }
            if (done) EndRide(true, null);
        }

        private void EndRide(bool atEnd, string why)
        {
            _rideDone = false;   // lead fix 2026-10-04: the next ride starts clean
            FlightRide ride = _ride;
            _ride = null;
            Flying = false;
            WowOrigin.AvatarDriven = false;
            if (ride == null) return;
            Vector3 landed = Vector3.zero;
            try { landed = ride.Release(MapView(), atEnd); }
            catch (Exception e) { WowLog.Error("Flight: Release threw " + e); }
            FlightFade fade = ride.DetachFade();
            bool hop = atEnd && ride.HopAtEnd;
            if (hop)
            {
                // Iteration 11 (motion 6.2.8): the map edge of a two-map route. The avatar was released where it is (no ground snap);
                // Flying stays true (no fall rescue) and the view black until the server's far teleport (EndHop).
                if (fade == null) fade = new FlightFade();
                fade.Set(1f);
                if (_hopFade != null) _hopFade.Destroy();
                _hopFade = fade;
                _hopWaiting = true;
                _hopAt = Time.time;
                Flying = true;
                WowLog.Info("Flight: map edge reached - waiting for the server to carry the flight to the next map.");
            }
            else if (fade != null) { if (_fadeIn != null) _fadeIn.Destroy(); _fadeIn = fade; _fadeInTime = 0f; }
            if (why != null) WowLog.Warn("Flight: ended early - " + why + ".");
            IWowSession s = WowPlay.Session;
            if (s != null)
            {
                float x, y, z;
                WowSpace.ToWow(s.MapId, landed, out x, out y, out z);
                try { s.SetPlayerPosition(new WowPos(x, y, z, ride.End.O), false); } catch (Exception) { }
                if (s.Player == null || s.Player.OnTaxi) SafeFinish(s);
            }
        }

        private static void SafeFinish(IWowSession s)
        {
            try { s.FinishTaxi(); }
            catch (Exception e) { WowLog.Error("Flight: FinishTaxi threw " + e); }
        }

        private void OnRebased(Vector3 delta)
        {
            if (_ride == null) return;
            try { _ride.OnRebased(delta); }
            catch (Exception e) { WowLog.Warn("Flight: rebase: " + e.Message); }
            StreamFocus = _ride.StreamFocus;
        }

        private void OnEnded()
        {
            if (_ride != null) EndRide(false, "the game ended");
            if (_map != null) _map.Hide();
            _shownTaxi = null;
            _pendingRoute = null;
            _taxiDeferred = false;
            if (_hopWaiting) EndHop("the game ended");
        }

        private static WowMapView MapView()
        {
            WowGame g = WowGame.Current;
            return g != null ? g.Map : null;
        }
    }
}
