using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Flight;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    // Iteration 7 (lead contract PLAN7 s3.7; owner Ride fills the bodies - the signatures are the lead's).

    /// <summary>What Remote writes into the record for the local rider, and what UI+Audio reads.</summary>
    public struct MountSnapshot
    {
        public bool Riding;              // a ridden mount (not a taxi) owns the avatar
        public byte Kind;                // 1 ground, 2 flying mount, 3 taxi (only with Ride's priority-2 taxi adoption)
        public bool Airborne, Swimming, Flying;
        public int GaitAnim; public float GaitRate; public int MountUpCounter, Epoch;
        public bool RelativeSaid;        // the two below are valid
        public Vector3 MountInRoot;      // the drawn mount root in the SYNCED root's yaw frame: Body.Root + the flat CharacterController-centre
                                         // offset (exactly what the client's PlayerTransformNetworkDriver sends), metres
        public float MountYawInRoot;     // degrees
        public int PoseStampMs;          // writer clock (ms of the unscaled frame time, Time.unscaledTimeAsDouble) at the LateUpdate that placed the root
        public Vector3 RootPos, RootVel; public float RootYaw;   // stage B: the synced root, this client's CURRENT origin frame (m, m/s, deg)
    }

    /// <summary>
    /// The local rider (iteration 7, owner Ride; PLAN7 s5.3 - where "smooth like ETS" is won or lost). Added by WowGame.Awake next to
    /// WowFlight. While the session says Player.Mounted (not on a taxi, not dead, the world settled and built round the player) the
    /// avatar is taken over like the taxi and the Euro Truck Sim seat (MountSeat) and driven by OUR controller: a hidden proxy
    /// CharacterController (MountMotor) stepped once per rendered frame through the pure RideMotion; the root written in Update (order
    /// -500, so everything later in the frame reads this frame) and again in LateUpdate after the pose (the Quake / ETS order); the
    /// mount model (MountModel, owner Body) placed heading only on the step-filtered feet and animated through RideGait; a desktop chase
    /// camera (MountCamera); VR: the client's own right-stick turn read back as steering, ETS's pose-entry undo, PlaceHead with a 0.45 m
    /// lean clamp, a 0.15 s blink at mount-up / dismount.
    /// Frame order (PLAN7 s5.3):
    ///   Update -500     MountPlace.Tick (on foot too); MountInput -> RideIntent; RideLimits.From(Player, vr); RideMotion.Step -> proxy.Move;
    ///                   the visible y (FilterY); the root written; Feet / HeadingYaw / Moving / Motion / StreamFocus / SeatHeight published.
    ///   Update -205     the client's XRBodyTransformer applies its snap / smooth turn to Body.Root (VR).
    ///   Update 0        WowGame: SetPlayerMotion(Motion), SetPlayerPosition(the mount's feet, heading, Moving), Tick.
    ///   LateUpdate -500 (a) VR turn read-back (b) SittingStraight FIRST + VR undo (c) root again (d) VR PlaceHead / desktop hips on the
    ///                   saddle (e) MountModel.Place + Animate(RideGait.Pick) (f) Snapshot.
    ///   LateUpdate 10000 MountCamera (desktop).
    ///   next Update     MeasureDrift: root vs last written ("Ride: root contested N of M frames" every 10 s when N > 0).
    /// Every value that is not WoW's is a named constant (here, RideMotion, RideLimits, MountSeat, MountMotor, MountCamera, MountInput),
    /// logged once at the first ride.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class WowMount : MonoBehaviour
    {
        public static WowMount Instance { get; private set; }
        public static bool OwnsAvatar { get; private set; }       // a ride (or the fall after a dismount in the air) writes the avatar root
        public static Vector3 Feet { get; private set; }          // the MOUNT's feet (proxy bottom), Unity, current origin frame = the session's position
        public static float HeadingYaw { get; private set; }      // Unity degrees = the session's facing while OwnsAvatar
        public static bool Moving { get; private set; }           // WowGame's moving flag while OwnsAvatar
        public static PlayerMotion Motion { get; private set; }   // this frame; default (Driven false) on foot
        public static Vector3 StreamFocus { get; private set; }   // where the world should stream while OwnsAvatar
        public static float SeatHeight { get; private set; }      // m above Feet (nameplates, click boxes)
        public static Transform MountRoot { get; private set; }   // the drawn local mount (it carries the M2Animator), null when none
        public static MountSnapshot Snapshot { get; private set; }

        // ---- iteration 11 (w0game, wave 0): the merged hooks of motion (16), pets (H7b) and avatar (6)

        /// <summary>The ride system carries the player: a mount, a vehicle / possessed unit we steer (WowVehicle.Drives, pets H7b) or a
        /// flying Flight Form (FormRide.Flies, motion hook 16). Both extras answer false until their owners build them.</summary>
        private static bool Rides(PlayerState p)
        {
            return p.Mounted || WowVehicle.Drives(p) || Play.Motion.FormRide.Flies(p);
        }

        private bool _form;   // the current ride is a Flight Form (no mount model; the saddle is the visible feet)

        /// <summary>motion hook 10: a forced move (knockback / path) while riding - the ride's own jump or path, then ForcedMoveDone.
        /// Returns false when it cannot (WowForcedMove logs the known gap). Wave 0: false (not implemented - the owner Ride fills it).</summary>
        public static bool TryApplyForcedMove(ForcedMoveView m) { return false; }

        /// <summary>avatar hook 6: the local spot as MountPlace samples it (5 Hz, on foot too) - liquid depth for the form's swim animations.</summary>
        /// <summary>Lead 2026-10-26: the sampler itself (WowSwim asks it for the terrain water surface every frame, as the motor does).</summary>
        public static MountPlace PlaceSampler { get { var m = Instance; return m != null ? m._place : null; } }
        public static PlayerPlace Place { get { var m = Instance; return m != null && m._place != null ? m._place.Current : default(PlayerPlace); } }

        /// <summary>Gives the avatar back now (before WowGame's own teleports / the room). placeAvatar false = do not teleport it.</summary>
        public static void LetGo(string why, bool placeAvatar)
        {
            WowMount m = Instance;
            if (m == null || m._phase == Phase.None) return;
            try { m.Release(why, placeAvatar, false); }
            catch (Exception e) { WowLog.Error("Ride: LetGo(" + why + ") threw " + e); }
        }

        // ---- ours (PLAN7 s4.8; logged once at the first ride) ------------------------------------------------------------------------
        /// <summary>Fallback switch (decision D3): false = the ETS / taxi behaviour (the client's turn is erased each frame; steer with the
        /// left stick only).</summary>
        public const bool ReadClientTurn = true;
        /// <summary>Ours: stream ahead velocity x 6 s (capped 200 m); the area 30 m round that point checked every 0.25 s; not ready ->
        /// the speed cap eases to 60 % (0.8 per second), logged once per stall. (The taxi streams 8 s ahead: FlightRide.StreamAheadSeconds.)</summary>
        public const float StreamAheadSeconds = 6f, StreamAheadMax = 200f, StreamCheckEvery = 0.25f, StreamCheckRadius = 30f, StreamCap = 0.6f, CapEaseRate = 0.8f;
        /// <summary>Ours (decision D4): the VR blink at mount-up / dismount: 0.15 s black, then a 0.08 s fade back.</summary>
        public const float BlinkSeconds = 0.15f, BlinkFade = 0.08f;
        /// <summary>Ours: seconds between the ride's summary logs / the contested-root logs; the longest fall after an air dismount.</summary>
        public const float LogEvery = 30f, ContestLogEvery = 10f, FallTimeout = 15f;
        /// <summary>The rider's collision height in AzerothCore's mounted formula: 2.031 yd = the CollisionHeight measured on the player
        /// models (PLAN7 s4.2); the Somnium avatar has no WoW model - ours, logged. MountHeight + 2.031 x 0.5 = 2.88 yd on the horse.</summary>
        public const float RiderCollisionYards = 2.031f;
        /// <summary>Ours: a VR read-back turn of at least this many degrees in one frame is counted as a snap (the client's snap is 45).</summary>
        public const float SnapCountDeg = 15f;
        /// <summary>Ours: the mounted log waits for the hips measurement at most this long.</summary>
        public const float MountedLogWait = 1.5f;
        /// <summary>Ours: a root that moved more than this between our writes counts as contested (Quake Q3Player.MeasureDrift: 1 mm).</summary>
        public const float ContestTolerance = 0.001f;

        private const float K = 0.9144f;

        private enum Phase { None, Riding, Falling }

        private Phase _phase;
        private readonly MountSeat _seat = new MountSeat();
        private readonly MountMotor _motor = new MountMotor();
        private readonly MountInput _input = new MountInput();
        private MountPlace _place = new MountPlace();
        private MountModel _model = new MountModel();
        private MountModel _dying;
        private float _dyingUntil;
        private int _preparedDisplay, _shownDisplay;
        private MountCamera _camera;
        private bool _subscribed;
        private RideLimits _limits;
        private RideGaitPick _gait;
        private float _lastMouseTurn;
        private float _cap = 1f;
        private bool _stalled;
        private float _nextStreamCheck, _stallSince;
        private int _stalls;
        private int _epoch;
        private float _retryAt;
        private float _rideStart, _nextLog, _nextContestLog, _fallStart;
        private bool _releaseNext;
        private string _fallWhy;
        private bool _mountedLogged, _constantsLogged;
        private int _contested, _contestFrames, _contestedWindow, _contestFramesWindow;
        private float _contestMax, _contestMaxWindow;
        private int _rebases, _snaps, _poseSkips, _jumps, _snapsTotal;
        private float _smoothTurn;
        private RideMode _lastMode;
        private float _lastVisY;
        private bool _placeBroken, _modelBroken;
        private string _chSource;
        private FlightFade _blink;
        private float _blinkT;
        private bool _vrAtStart;

        // ------------------------------------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
            _camera = GetComponent<MountCamera>();
            if (_camera == null) _camera = gameObject.AddComponent<MountCamera>();
        }

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
            if (_phase != Phase.None)
            {
                try { Release("the ride component was disabled", true, true); }
                catch (Exception e) { WowLog.Error("Ride: release on disable threw " + e); }
            }
            DestroyModels();
            if (_blink != null) { _blink.Destroy(); _blink = null; }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            OwnsAvatar = false;
            Motion = default(PlayerMotion);
            Snapshot = default(MountSnapshot);
            MountRoot = null;
        }

        private void OnEnded()
        {
            if (_phase != Phase.None) Release("the game ended", false, false);
            DestroyModels();
            _place = new MountPlace();
            _preparedDisplay = 0;
        }

        private void OnEvent(GameEvent e)
        {
            try
            {
                IWowSession s = WowPlay.Session;
                PlayerState p = s != null ? s.Player : null;
                switch (e.Type)
                {
                    case GameEventType.CastStarted:
                        if (_phase == Phase.None && p != null && (e.SourceGuid == 0 || e.SourceGuid == p.Guid) && MountCatalog.IsMount(e.SpellId)) Prepare(e.SpellId, p);
                        break;
                    case GameEventType.Dismounted:
                        if (_phase == Phase.Riding) EndRide("dismounted (" + (DismountReason)e.Amount + (string.IsNullOrEmpty(e.Text) ? "" : ": " + e.Text) + ")", true);
                        break;
                    case GameEventType.PlayerDied:
                        if (_phase == Phase.Riding) EndRide("death", true);
                        break;
                    case GameEventType.Teleport:
                        _epoch++;
                        if (_phase != Phase.None) Release("a game teleport", false, false);   // WowGame teleports the avatar itself (LetGo hook)
                        break;
                    case GameEventType.TaxiStarted:
                        if (_phase != Phase.None) Release("a flight path started", false, false);   // the flight takes the avatar over
                        break;
                }
            }
            catch (Exception ex) { WowLog.Error("Ride: event " + e.Type + " threw " + ex); }
        }

        // ------------------------------------------------------------------------------------------------ Update (-500)

        private void Update()
        {
            float dt = Time.deltaTime, now = Time.unscaledTime;
            TickBlink(Time.unscaledDeltaTime);
            TickDying(now);
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (_phase != Phase.None) MeasureDrift(now);
            if (s == null || p == null)
            {
                if (_phase != Phase.None) Release("no game session", false, false);
                PublishOnFoot();
                return;
            }
            TickPlace(s, now);
            if (_phase == Phase.None)
            {
                if (Rides(p) && now >= _retryAt && !p.OnTaxi && !p.Dead && !p.Ghost && !WowFlight.Flying && WorldReady()) StartRide(s, p, now);
            }
            else if (_phase == Phase.Riding)
            {
                string why = !Rides(p) ? "the session says not mounted (or the flight form ended)" : p.Dead ? "death" : p.OnTaxi ? "a flight path" : WowFlight.Flying ? "a flight" : null;
                if (why != null) EndRide(why, p.Vehicle == null);   // integrator (vehicle request): no fall when a vehicle / possession seat takes the avatar next
            }
            if (_phase == Phase.Riding) TickRide(s, p, dt, now);
            else if (_phase == Phase.Falling) TickFall(s, p, dt, now);
            if (_phase == Phase.None) PublishOnFoot();
        }

        private void StartRide(IWowSession s, PlayerState p, float now)
        {
            Transform root = FindAvatar();
            if (root == null) { Fail(s, "no avatar to seat", now); return; }
            bool vr = WowPlayer.XrPresent;
            float heading = root.eulerAngles.y;   // desktop: the avatar root's yaw (the avatar turns to where it walks)
            string src;
            float headYaw;
            if (vr && WowPlayer.TryFacingYaw(out headYaw, out src)) heading = headYaw;   // VR: the headset's yaw
            if (vr) Blink();   // black before the lift (the takeover and the first seat write happen this frame)
            string why;
            if (!_seat.TakeOver(root, out why)) { Fail(s, why, now); return; }
            LogConstantsOnce();
            float ch = CollisionHeightM(p, true);
            _motor.Create(transform, _seat.SyncedRoot, heading, _seat.CcRadius, ch, _seat.CcStep, _seat.CcSlope);
            StreamFocus = _motor.Feet;
            // Iteration 11 (motion hook 16): a Flight Form ride has no mount model (the avatar area draws the bird on the hidden avatar);
            // the saddle is the visible feet and the record's mount display stays 0 (Player.MountDisplayId is 0 in a form).
            _form = !p.Mounted && Play.Motion.FormRide.Flies(p);
            if (_form)
            {
                WowLog.Info("Ride: Flight Form - flying without a mount model (the avatar area draws the form).");
            }
            else
            {
                int display = p.MountDisplayId;
                if (display <= 0)
                {
                    MountInfo mi = MountCatalog.Find(p.MountSpellId);
                    display = MountDisplays.StandIn(mi != null ? mi.Kind : p.MountKind, p.Faction);
                    WowLog.Warn("Ride: Player.MountDisplayId is 0 - drawing the labelled stand-in display " + display + ".");
                }
                ShowModel(display);
            }
            _phase = Phase.Riding;
            OwnsAvatar = true;
            WowOrigin.AvatarDriven = true;   // a rebase mid-ride commits at once; OnRebased rewrites the root (the FlightRide pattern)
            _input.Reset();
            _vrAtStart = vr;
            if (!vr) _camera.Begin();
            _rideStart = now;
            _nextLog = now + LogEvery;
            _nextContestLog = now + ContestLogEvery;
            _mountedLogged = false;
            _cap = 1f;
            _stalled = false;
            _nextStreamCheck = now;
            _contested = _contestFrames = _contestedWindow = _contestFramesWindow = 0;
            _contestMax = _contestMaxWindow = 0f;
            _rebases = _snaps = _poseSkips = _jumps = _snapsTotal = 0;
            _smoothTurn = 0f;
            _lastMode = RideMode.Ground;
            _lastVisY = _motor.VisibleY;
            _releaseNext = false;
            _gait = default(RideGaitPick);
            _epoch++;
        }

        private void Fail(IWowSession s, string why, float now)
        {
            _retryAt = now + 3f;
            WowLog.Warn("Ride: cannot ride (" + why + ") - dismounting so the game and the view agree.");
            try { s.Dismount(); }
            catch (Exception e) { WowLog.Warn("Ride: Dismount threw " + e.Message); }
        }

        private void TickRide(IWowSession s, PlayerState p, float dt, float now)
        {
            bool vr = WowPlayer.XrPresent;
            if (_seat.Root == null) { Release("the avatar disappeared", false, true); return; }
            _motor.SetHeight(CollisionHeightM(p, false));
            if (p.MountDisplayId > 0 && p.MountDisplayId != _shownDisplay)
            {
                WowLog.Info("Ride: the session's mount display changed " + _shownDisplay + " -> " + p.MountDisplayId + " mid-ride - showing it.");
                ShowModel(p.MountDisplayId);
            }
            RideIntent i = _input.Read(vr, _camera.Pitch);
            if (p.Rooted || p.ControlLost) i = default(RideIntent);   // iteration 11 (motion hook 16): rooted / feared - the motor ignores input
            _lastMouseTurn = vr ? 0f : i.TurnDeltaDeg;
            RideLimits l = RideLimits.From(p, vr);
            _limits = l;
            TickStreamCap(now, dt);
            if (_cap < 0.9999f) l = l.Scaled(_cap);
            _motor.Step(i, l, dt, _place, s.MapId, MapView());
            ModeChanged(p);
            if (_motor.State.Jumped) _jumps++;
            float h = _motor.State.Heading;
            Vector3 vis = _motor.VisibleFeet;
            Vector3 saddle = Saddle(vis, h);
            _seat.WriteRoot(saddle, h, vr);
            Publish(p, l, vr, saddle, false);
        }

        // ------------------------------------------------------------------------------------------------ LateUpdate (-500)

        private void LateUpdate()
        {
            try
            {
                if (_phase == Phase.Riding) LateRide();
                else if (_phase == Phase.Falling) LateFall();
            }
            catch (Exception e)
            {
                WowLog.Error("Ride: LateUpdate threw " + e + " - giving the avatar back.");
                try { Release("error", true, true); } catch (Exception) { }
            }
        }

        private void LateRide()
        {
            if (_seat.Root == null) return;
            bool vr = WowPlayer.XrPresent;
            float h = _motor.State.Heading;
            // (a) the client's own snap / smooth turn since our Update write, as steering
            if (vr && ReadClientTurn)
            {
                float d;
                bool poseEntry;
                if (_seat.TryReadClientTurn(out d, out poseEntry))
                {
                    h = Wrap(h + d);
                    _motor.State.Heading = h;
                    if (Mathf.Abs(d) >= SnapCountDeg) { _snaps++; _snapsTotal++; } else _smoothTurn += Mathf.Abs(d);
                }
                else if (poseEntry) _poseSkips++;
            }
            // (b)-(d) the pose, the root again, VR head / desktop hips
            Vector3 vis = _motor.VisibleFeet;
            Vector3 saddle = Saddle(vis, h);
            _seat.Seat(saddle, h, vr);
            // (e) the mount's gait
            RideState st = _motor.State;
            float planar = st.Velocity.PlanarLength;
            float fwd = st.Forward < 0 ? -planar : planar;
            try
            {
                // playing = what the model's animator plays NOW (Body's contract), so a one-shot (JumpStart, JumpEnd, JumpLandRun)
                // is left once it has ended; the last pick only while no model is built (fixer, review finding 1).
                var an = _model.Animator;
                int playing = an != null && an.PlayingAnimationId >= 0 ? an.PlayingAnimationId : _gait.AnimId;
                _gait = RideGait.Pick(st.Mode, fwd, st.TurnRate, st.Velocity.Y, _limits.Run, st.Jumped, st.Landed, _model.Gait, playing);
                _model.Animate(_gait);
            }
            catch (Exception e) { ModelError("Animate", e); }
            // (f) what others see
            WriteSnapshot(vis, h);
            if (!vr)
            {
                // lead fix 2026-10-05: the chase camera on the steady seat (the rider now sits on the live, bobbing saddle)
                Vector3 steady = _form ? saddle : vis + Quaternion.Euler(0f, h, 0f) * (_model != null ? _model.SeatLocal : new Vector3(0f, 1.7f, 0f));
                _camera.SetTarget(steady, h, _lastMouseTurn);
            }
            _lastMouseTurn = 0f;
            HeadingYaw = h;
            SeatHeight = saddle.y - _motor.Feet.y;
            MountRoot = _model.Root;
            float now = Time.unscaledTime;
            LogMountedOnce(now, vr);
            LogPeriodic(now, vr);
        }

        private void LateFall()
        {
            if (_seat.Root == null) return;
            Vector3 feet = _motor.Feet;
            _seat.WriteStanding(feet, _motor.State.Heading);
            if (!WowPlayer.XrPresent) _camera.SetTarget(feet + Vector3.up * 0.9f, _motor.State.Heading, 0f);
        }

        // ------------------------------------------------------------------------------------------------ the end of a ride

        /// <summary>A chosen or forced end: in the air first a fall (the proxy under WoW's gravity, Motion Airborne so online sends
        /// FALLING and FALL_LAND - WoW's fall damage applies online), then the release on landing.</summary>
        private void EndRide(string why, bool allowFall)
        {
            if (_phase != Phase.Riding) return;
            RideMode m = _motor.State.Mode;
            if (allowFall && (m == RideMode.Airborne || m == RideMode.Flying) && _motor.Ground.HaveCollider)
            {
                _phase = Phase.Falling;
                _fallStart = Time.unscaledTime;
                _fallWhy = why;
                _releaseNext = false;
                _motor.State.Mode = RideMode.Airborne;
                _motor.State.JumpVelocity = new RideVec(_motor.State.Velocity.X, 0f, _motor.State.Velocity.Z);
                _motor.State.FallTime = 0f;
                // VR: black before the drop from the saddle to the proxy's feet (1-2.7 m in one frame; rule 2 / decision D4). The
                // release on landing blinks again for its teleport (fixer, review finding 3).
                if (WowPlayer.XrPresent) Blink();
                _seat.LeavePose();
                HideModel();
                MountRoot = null;
                Snapshot = default(MountSnapshot);
                WowLog.Info("Ride: " + why + " in the air (" + m + ", " + (_motor.Feet.y - GroundBelow(_motor.Feet)).ToString("0.0") + " m up) - falling first, released on landing.");
                return;
            }
            Release(why, true, false);
        }

        private void TickFall(IWowSession s, PlayerState p, float dt, float now)
        {
            if (_releaseNext || _seat.Root == null) { Release(_fallWhy + ", landed", true, false); return; }
            bool vr = WowPlayer.XrPresent;
            RideLimits l = RideLimits.From(p, vr);
            l.CanFly = false;
            _motor.Step(default(RideIntent), l, dt, _place, s.MapId, MapView());
            Vector3 feet = _motor.Feet;
            _seat.WriteStanding(feet, _motor.State.Heading);
            Publish(p, l, vr, feet, true);
            RideState st = _motor.State;
            // The Landed frame is published first (online: MSG_MOVE_FALL_LAND), the release follows next frame.
            if (st.Landed || st.Mode != RideMode.Airborne || now - _fallStart > FallTimeout)
            {
                if (now - _fallStart > FallTimeout) WowLog.Warn("Ride: the fall after the dismount lasted " + FallTimeout + " s - releasing anyway.");
                _releaseNext = true;
            }
        }

        /// <summary>Gives the avatar back now (no fall phase). dismountSession: the ride ended for its own reason while the session may
        /// still say Mounted - tell the session so the state never diverges.</summary>
        private void Release(string why, bool placeAvatar, bool dismountSession)
        {
            if (_phase == Phase.None) return;
            Phase was = _phase;
            _phase = Phase.None;
            bool vr = WowPlayer.XrPresent;
            float h = _motor.State.Heading;
            Vector3 feet = _motor.Proxy != null ? _motor.Feet : (_seat.Root != null ? _seat.Root.position : Vector3.zero);
            Vector3 at = feet;
            if (was == Phase.Riding && _motor.Proxy != null)
            {
                // Under the saddle when that is inside the proxy's own footprint, else the proxy's feet (never inside a wall).
                Vector3 saddle = Saddle(_motor.VisibleFeet, h);
                Vector2 off = new Vector2(saddle.x - feet.x, saddle.z - feet.z);
                if (off.magnitude <= _motor.Proxy.radius) { at.x = saddle.x; at.z = saddle.z; }
            }
            string ground = "proxy";
            float gy;
            WowMapView map = MapView();
            if (map != null && map.TryFloorHeight(at, 1.5f, out gy) && gy > at.y - 3f) { at.y = gy + 0.02f; ground = "floor"; }
            else if (map != null && map.TryGroundHeight(at, out gy) && gy > at.y - 3f) { at.y = gy + 0.02f; ground = "terrain"; }
            if (vr) Blink();
            string how;
            _seat.Release(at, h, placeAvatar, out how);
            if (!WowFlight.Flying) WowOrigin.AvatarDriven = false;
            _camera.End(why);
            HideModel();
            _motor.Destroy();
            OwnsAvatar = false;
            PublishOnFoot();
            _stalled = false;
            _cap = 1f;
            WowLog.Info("Ride: released (" + why + ") at " + at.ToString("F1") + " (" + ground + "), no-drop write, teleport " + how + "; controller " +
                        (_seat.CcFound ? "restored" : "none") + ", pose and motion given back; " + (Time.unscaledTime - _rideStart).ToString("0") + " s ridden, " + _jumps + " jumps.");
            if (dismountSession)
            {
                IWowSession s = WowPlay.Session;
                if (s != null && s.Player != null && Rides(s.Player))   // iteration 11 (pets H7b + motion hook 16)
                {
                    WowLog.Warn("Ride: the ride ended on its own (" + why + ") while the session still says mounted - dismounting.");
                    _retryAt = Time.unscaledTime + 3f;
                    try { s.Dismount(); }
                    catch (Exception e) { WowLog.Warn("Ride: Dismount threw " + e.Message); }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ floating origin

        private void OnRebased(Vector3 delta)
        {
            if (_phase == Phase.None) return;
            try
            {
                _rebases++;
                _epoch++;
                // WowOrigin shifted the proxy and the mount model (children of the WoW Game object) and synced physics; only our own
                // Unity-space values move here, then the root is rewritten from the (already shifted) proxy - the FlightRide pattern.
                _motor.OnRebased(delta);
                _lastVisY -= delta.y;
                StreamFocus -= delta;
                Feet = _motor.Feet;
                float h = _motor.State.Heading;
                bool vr = WowPlayer.XrPresent;
                if (_phase == Phase.Riding)
                {
                    Vector3 saddle = Saddle(_motor.VisibleFeet, h, false);   // the model was shifted by WowOrigin: not placed again here
                    _seat.WriteRoot(saddle, h, vr);
                }
                else _seat.WriteStanding(_motor.Feet, h);
            }
            catch (Exception e) { WowLog.Warn("Ride: rebase: " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------ helpers

        private void Prepare(int spellId, PlayerState p)
        {
            MountInfo mi = MountCatalog.Find(spellId);
            int entry = mi != null ? mi.CreatureEntry : 0;
            MountDisplaySource src;
            int display = MountDisplays.Resolve(entry, 0, out src);
            if (display <= 0) display = MountDisplays.StandIn(mi != null ? mi.Kind : MountKind.Ground, p.Faction);
            if (display == _preparedDisplay) return;
            _preparedDisplay = display;
            try { _model.Prepare(display); }
            catch (Exception e) { ModelError("Prepare", e); }
        }

        private void ShowModel(int display)
        {
            _shownDisplay = display;
            try
            {
                _model.Show(display);
                MountRoot = _model.Root;
            }
            catch (Exception e) { ModelError("Show", e); }
            _preparedDisplay = 0;
        }

        private void HideModel()
        {
            if (_dying != null) { try { _dying.Destroy(); } catch (Exception) { } }
            _dying = _model;
            _dyingUntil = Time.unscaledTime + 0.25f;
            try { _dying.Hide(0.15f); }
            catch (Exception e) { ModelError("Hide", e); }
            _model = new MountModel();
            _shownDisplay = 0;
            MountRoot = null;
        }

        private void TickDying(float now)
        {
            if (_dying == null || now < _dyingUntil) return;
            try { _dying.Destroy(); } catch (Exception e) { ModelError("Destroy", e); }
            _dying = null;
        }

        private void DestroyModels()
        {
            try { if (_dying != null) _dying.Destroy(); } catch (Exception) { }
            _dying = null;
            try { _model.Destroy(); } catch (Exception) { }
            _model = new MountModel();
            _shownDisplay = 0;
            MountRoot = null;
        }

        private void ModelError(string what, Exception e)
        {
            if (_modelBroken) return;
            _modelBroken = true;
            WowLog.Error("Ride: MountModel." + what + " threw " + e + " (logged once).");
        }

        /// <summary>The saddle (world): the model placed heading only on the visible feet, then its fixed seat (mount space).</summary>
        private Vector3 Saddle(Vector3 visibleFeet, float heading, bool place = true)
        {
            if (_form) return visibleFeet;   // iteration 11 (motion hook 16): a Flight Form ride - no mount, the avatar stands on the motor's feet
            Vector3 seat = new Vector3(0f, 1.7f, 0f);
            try
            {
                if (place) _model.Place(visibleFeet, heading);
                seat = _model.SeatLocal;
            }
            catch (Exception e) { ModelError("Place", e); }
            // lead fix 2026-10-05: desktop riders sit on the live saddle (MountModel.TryLiveSeat); VR keeps the fixed seat (comfort)
            Vector3 live;
            if (!WowPlayer.XrPresent && _model != null && _model.TryLiveSeat(out live)) return live;
            // MountModel's root has scale 1 and is placed heading only at the feet, so Root.TransformPoint(SeatLocal) is exactly this
            // (Body's contract note) - computed without the transform so it holds in a Rebased handler and before the model is built.
            return visibleFeet + Quaternion.Euler(0f, heading, 0f) * seat;
        }

        /// <summary>The proxy's height (m): the session's collision height (online: the server's), else the model's MountHeight + the
        /// rider's 2.031 x 0.5 yd (AzerothCore Unit.cpp 16783), else the horse's 2.88 yd.</summary>
        private float CollisionHeightM(PlayerState p, bool log)
        {
            string src;
            float yd;
            float mh = 0f;
            try { mh = _model.MountHeightYards; } catch (Exception) { mh = 0f; }
            if (p != null && p.CollisionHeight > 0.1f) { yd = p.CollisionHeight; src = "the session"; }
            else if (mh > 0.01f) { yd = mh + RiderCollisionYards * 0.5f; src = "the model's MountHeight " + mh.ToString("0.000") + " + " + RiderCollisionYards + " x 0.5"; }
            else { yd = RideMotion.DefaultCollisionHeight / K; src = "the horse's (nothing known yet)"; }
            if (log || src != _chSource)
            {
                if (_chSource != null || log) WowLog.Info("Ride: proxy height " + (yd * K).ToString("0.00") + " m = collision height " + yd.ToString("0.00") + " yd from " + src + ".");
                _chSource = src;
            }
            return yd * K;
        }

        private void Publish(PlayerState p, in RideLimits l, bool vr, Vector3 saddle, bool falling)
        {
            RideState st = _motor.State;
            Vector3 feet = _motor.Feet;
            Feet = feet;
            HeadingYaw = st.Heading;
            float planar = st.Velocity.PlanarLength;
            Moving = planar > 0.05f || Mathf.Abs(st.Velocity.Y) > 0.05f || st.Mode == RideMode.Airborne;
            PlayerMotion m = default(PlayerMotion);
            m.Driven = true;
            m.Forward = falling ? 0 : st.Forward;
            m.Strafe = falling ? 0 : st.Strafe;
            m.Turn = vr || falling ? 0 : st.Turn;   // key turning at Player.TurnRate only (the VR stick steers at its own rate: SET_FACING)
            m.Walking = !falling && st.Walking;
            m.Airborne = st.Mode == RideMode.Airborne;
            m.Flying = !falling && st.Mode == RideMode.Flying;
            m.Swimming = st.Mode == RideMode.Swimming;
            m.Vertical = m.Flying ? st.Vertical : 0;
            m.Pitch = m.Flying || m.Swimming ? st.Pitch : 0f;
            m.FallTime = m.Airborne ? st.FallTime : 0f;
            m.Jumped = st.Jumped;
            if (st.Jumped)
            {
                float jp = st.JumpVelocity.PlanarLength;
                m.JumpUpSpeed = l.JumpSpeed / K;
                m.JumpPlanarSpeed = jp / K;
                m.JumpDirection = WowSpace.YawToOrientation(jp > 0.01f ? Mathf.Atan2(st.JumpVelocity.X, st.JumpVelocity.Z) * Mathf.Rad2Deg : st.Heading);
            }
            m.Landed = st.Landed;
            m.AtSpeed = st.AtSpeed;
            Motion = m;
            Vector3 ahead = new Vector3(st.Velocity.X, 0f, st.Velocity.Z) * StreamAheadSeconds;
            if (ahead.magnitude > StreamAheadMax) ahead = ahead.normalized * StreamAheadMax;
            StreamFocus = feet + ahead;
            SeatHeight = saddle.y - feet.y;
        }

        private void PublishOnFoot()
        {
            OwnsAvatar = false;
            Motion = default(PlayerMotion);
            Moving = false;
            MountRoot = null;
            Snapshot = default(MountSnapshot);
            SeatHeight = 0f;
        }

        private void WriteSnapshot(Vector3 vis, float h)
        {
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            Transform root = _seat.Root;
            if (root == null) return;
            RideState st = _motor.State;
            MountSnapshot sn = default(MountSnapshot);
            sn.Riding = true;
            sn.Kind = (byte)(p != null && p.MountKind == MountKind.Flying ? 2 : 1);
            sn.Airborne = st.Mode == RideMode.Airborne;
            sn.Swimming = st.Mode == RideMode.Swimming;
            sn.Flying = st.Mode == RideMode.Flying;
            sn.GaitAnim = _gait.AnimId;
            sn.GaitRate = _gait.Rate;
            sn.MountUpCounter = p != null ? (p.MountCounter & 15) : 0;
            sn.Epoch = _epoch & 31;
            float rootYaw = root.eulerAngles.y;
            Quaternion ry = Quaternion.Euler(0f, rootYaw, 0f);
            Vector3 synced = _seat.SyncedRoot;
            Transform mr = _model.Root;
            Vector3 mountPos = mr != null ? mr.position : vis;
            sn.RelativeSaid = true;
            sn.MountInRoot = Quaternion.Inverse(ry) * (mountPos - synced);
            sn.MountYawInRoot = Mathf.DeltaAngle(rootYaw, h);
            // The unscaled frame time in ms: the positions above advance by Time.deltaTime, which equals the unscaled frame step on
            // every normal frame, so this stamp matches them; unscaled, a long hitch (deltaTime clamped) cannot shift the clock
            // against real time (fixer, review finding 2: Environment.TickCount steps in 15-16 ms on Windows and made stage B
            // jitter 5-11 cm RMS). Never 0 while riding: 0 = "no stage-B pose" on the wire (integrator, Remote request).
            int stampMs = unchecked((int)(long)(Time.unscaledTimeAsDouble * 1000.0));
            sn.PoseStampMs = stampMs != 0 ? stampMs : 1;
            float dt = Time.deltaTime;
            float vy = dt > 1e-5f ? (vis.y - _lastVisY) / dt : 0f;
            _lastVisY = vis.y;
            sn.RootPos = synced;
            sn.RootVel = new Vector3(st.Velocity.X, vy, st.Velocity.Z);
            sn.RootYaw = rootYaw;
            Snapshot = sn;
        }

        private void TickPlace(IWowSession s, float now)
        {
            if (_placeBroken) return;
            WowMapView map = MapView();
            if (map == null || map.MapId < 0 || s.MapId != map.MapId) return;
            Vector3 feet;
            if (_phase != Phase.None && _motor.Proxy != null) feet = _motor.Feet;
            else if (!WowPlayer.TryFeet(out feet)) return;
            if ((feet - WowSpace.LoginRoomOrigin).magnitude < 60f) return;
            try { _place.Tick(s, s.MapId, feet, now); }
            catch (Exception e) { _placeBroken = true; WowLog.Error("Ride: MountPlace.Tick threw " + e + " (off for this game)."); }
        }

        private void TickStreamCap(float now, float dt)
        {
            WowMapView map = MapView();
            if (now >= _nextStreamCheck)
            {
                _nextStreamCheck = now + StreamCheckEvery;
                bool ready = true;
                if (map != null)
                {
                    try { ready = map.IsReadyAround(StreamFocus, StreamCheckRadius); }
                    catch (Exception) { ready = true; }
                }
                if (!ready && !_stalled)
                {
                    _stalled = true;
                    _stalls++;
                    _stallSince = now;
                    WowLog.Info("Ride: world ahead not ready (speed cap " + (StreamCap * 100f).ToString("0") + " %) at " + StreamFocus.ToString("F0") + ".");
                }
                else if (ready && _stalled)
                {
                    _stalled = false;
                    WowLog.Info("Ride: world ahead ready again after " + (now - _stallSince).ToString("0.0") + " s - speed cap off.");
                }
            }
            _cap = Mathf.MoveTowards(_cap, _stalled ? StreamCap : 1f, CapEaseRate * dt);
        }

        private void ModeChanged(PlayerState p)
        {
            RideState st = _motor.State;
            if (st.Mode == _lastMode) return;
            RideMode was = _lastMode;
            _lastMode = st.Mode;
            float chYd = _motor.Ground.CollisionHeight / K;
            switch (st.Mode)
            {
                case RideMode.Flying:
                    WowLog.Info("Ride: take-off (flight " + (p.FlightSpeed).ToString("0.0") + " yd/s, " + (p.CanFly ? "CanFly" : "no CanFly?") + ").");
                    break;
                case RideMode.Swimming:
                    WowLog.Info("Ride: swimming (liquid " + (_motor.Ground.LiquidDepth / K).ToString("0.00") + " yd > " + (RideMotion.SwimEnter * chYd).ToString("0.00") + " yd = 0.75 x collision height), swim " + p.SwimSpeed.ToString("0.00") + " yd/s.");
                    break;
                case RideMode.Ground:
                    if (was == RideMode.Flying) WowLog.Info("Ride: landed (from flight).");
                    else if (was == RideMode.Swimming) WowLog.Info("Ride: out of the water.");
                    else if (was == RideMode.Airborne && st.FallTime > 1f) WowLog.Info("Ride: landed after a " + st.FallTime.ToString("0.0") + " s fall.");
                    break;
            }
        }

        private void MeasureDrift(float now)
        {
            Transform root = _seat.Root;
            if (root == null) return;
            float d = (root.position - _seat.LastWritten).magnitude;
            _contestFrames++;
            _contestFramesWindow++;
            if (d > ContestTolerance)
            {
                _contested++;
                _contestedWindow++;
                if (d > _contestMax) _contestMax = d;
                if (d > _contestMaxWindow) _contestMaxWindow = d;
            }
            if (now >= _nextContestLog)
            {
                _nextContestLog = now + ContestLogEvery;
                if (_contestedWindow > 0)
                    WowLog.Warn("Ride: root contested " + _contestedWindow + " of " + _contestFramesWindow + " frames (max " + _contestMaxWindow.ToString("0.000") + " m) - something else moved the avatar between our writes.");
                _contestedWindow = 0;
                _contestFramesWindow = 0;
                _contestMaxWindow = 0f;
            }
        }

        private void LogMountedOnce(float now, bool vr)
        {
            if (_mountedLogged) return;
            if (!_seat.HipsMeasured && now - _rideStart < MountedLogWait) return;
            _mountedLogged = true;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (p == null) return;
            MountInfo mi = MountCatalog.Find(p.MountSpellId);
            string name = mi != null && !string.IsNullOrEmpty(mi.Name) ? mi.Name : "a mount";
            bool flyer = p.MountKind == MountKind.Flying;
            Vector3 seat = new Vector3(0f, 1.7f, 0f);
            try { seat = _model.SeatLocal; } catch (Exception) { }
            WowLog.Info("Ride: mounted " + name + " (spell " + p.MountSpellId + ", display " + p.MountDisplayId + " from " + p.MountDisplaySource + ") - " + (flyer ? "flying" : "ground") + " " +
                        p.RunSpeed.ToString("0.0") + " yd/s" + (flyer ? " (flight " + p.FlightSpeed.ToString("0.0") + ")" : "") + ", seat (" + seat.z.ToString("0.00") + ", " + seat.y.ToString("0.00") + ") m, hips " +
                        _seat.Hips.ToString("0.00") + " m " + (_seat.HipsMeasured ? "measured" : "fallback") + ", proxy " + _motor.Proxy.radius.ToString("0.00") + "/" + _motor.Proxy.height.ToString("0.00") + "/" +
                        _motor.Proxy.stepOffset.ToString("0.00") + "/" + _motor.Proxy.slopeLimit.ToString("0") + " (" + _seat.ControllerLine + "), camera " + (vr ? "VR head placed" : "chase") + ".");
            if (vr)
            {
                WowLog.Info("Ride: VR seat - pose entry turn " + (_seat.PoseTurnUndone >= 0f ? _seat.PoseTurnUndone.ToString("0") + " deg undone" : "not seen") + ", " +
                            (_seat.HandsFound ? "hands reset" : "hand offsets NOT found") + "; eye " + _seat.EyeTarget.ToString("0.00") + " m above the saddle (" + (_seat.EyeMeasured ? "measured" : "fallback") +
                            "); lean clamp " + MountSeat.LeanLimit.ToString("0.00") + " m (re-rest past " + MountSeat.LeanRerest.ToString("0.0") + " m); client turn read-back " + (ReadClientTurn ? "on" : "off (ETS behaviour)") + ".");
            }
        }

        private void LogPeriodic(float now, bool vr)
        {
            if (now < _nextLog) return;
            float span = now - (_nextLog - LogEvery);
            _nextLog = now + LogEvery;
            float dist, gap;
            _motor.TakeStats(out dist, out gap);
            RideMode m = _motor.State.Mode;
            float session = m == RideMode.Flying ? _limits.Flight : m == RideMode.Swimming ? _limits.Swim : _limits.Run;
            WowLog.Info("Ride: " + span.ToString("0") + " s - " + dist.ToString("0") + " m, avg " + (span > 0f ? dist / span : 0f).ToString("0.00") + " m/s (session " + session.ToString("0.00") +
                        " m/s), max step-filter gap " + gap.ToString("0.00") + " m, contested " + _contested + " of " + _contestFrames + " frames (max " + _contestMax.ToString("0.000") + " m), rebases " + _rebases +
                        " (driven), " + _jumps + " jumps, " + _stalls + " stream stalls, " + _motor.Rescues + " rescues, " + _motor.Reseats + " re-seats.");
            _contested = 0;
            _contestFrames = 0;
            _contestMax = 0f;
            if (vr)
            {
                WowLog.Info("Ride: client turn read back: " + _snaps + " snaps, smooth " + _smoothTurn.ToString("0") + " deg (" + _poseSkips + " pose-entry frames skipped; " + _snapsTotal + " snaps this ride).");
                _snaps = 0;
                _smoothTurn = 0f;
                _poseSkips = 0;
            }
        }

        private void LogConstantsOnce()
        {
            if (_constantsLogged) return;
            _constantsLogged = true;
            WowLog.Info("Ride: ours (PLAN7 s4.8) - ramps ground " + RideLimits.GroundAccel + " / " + RideLimits.GroundDecel + " s, flying " + RideLimits.FlightAccel + " / " + RideLimits.FlightDecel +
                        " s (smoothstep); key turn to full in " + RideLimits.KeyTurnRamp + " s; VR steering " + RideLimits.VrSteerDeg + " deg/s (dead zone " + RideMotion.StickDeadZone + ", quadratic, " + RideLimits.VrTurnRamp +
                        " s); VR steps walk > " + RideMotion.StickWalkOn + ", run > " + RideMotion.StickRunOn + ", run -> walk < " + RideMotion.StickRunOff + ", stop < " + RideMotion.StickStopBelow +
                        "; VR climb (pitch - " + MountInput.VrClimbDeadDeg + ") x " + MountInput.VrClimbGain + " capped " + MountInput.MaxClimbDeg + "; ascend " + RideLimits.AscendShare + " x flight; jump " + RideLimits.JumpLaunch +
                        " m/s up (WoW's value is a client constant); step filter max(" + RideMotion.StepFilterMinRate + " m/s, v x tan 55) snap " + RideMotion.StepFilterSnap + " m; grounded pull max(" + RideMotion.GroundSnapMin +
                        " m, travel x tan 45), " + RideMotion.UngroundedGrace + " s grace; swim enter " + RideMotion.SwimEnter + " (AC) hold " + RideMotion.SwimHold + " leave " + RideMotion.SwimLeave +
                        " x collision height; stream ahead " + StreamAheadSeconds + " s (max " + StreamAheadMax + " m), cap " + (StreamCap * 100f) + " %; unloaded ground: collider search " + MountMotor.ColliderSearch +
                        " m, rescue " + MountMotor.RescueBelow + " m under the terrain; hips " + MountSeat.HipsMin + "-" + MountSeat.HipsMax + " m (fallback " + MountSeat.HipsFallback + "), sampled " + MountSeat.MeasureDelayFrames +
                        " frames after the pose over " + MountSeat.MeasureFrames + "; VR lean " + MountSeat.LeanLimit + " m re-rest " + MountSeat.LeanRerest + " m, eye fallback " + MountSeat.EyeFallback + " m, a measured eye eased in over " + MountSeat.EyeEaseSeconds +
                        " s; blink " + BlinkSeconds + " s + " + BlinkFade + " s; chase camera saddle + " + MountCamera.TargetAboveSaddle + " m, yaw omega " + MountCamera.YawOmega + "/s, mouse " + MountCamera.MouseDegPerUnit +
                        " deg/unit, pitch " + MountCamera.PitchMin + ".." + MountCamera.PitchMax + ", distance " + MountCamera.DistMin + "-" + MountCamera.DistMax + " m (exp " + MountCamera.DistSmooth + "/s), wall cast r " +
                        MountCamera.CastRadius + " (let out exp " + MountCamera.LetOut + "/s); rider collision " + RiderCollisionYards + " yd; ReadClientTurn " + ReadClientTurn + ".");
        }

        private void Blink()
        {
            if (_blink == null) _blink = new FlightFade();
            _blinkT = 0f;
            try { _blink.Set(1f); }
            catch (Exception e) { WowLog.Warn("Ride: blink: " + e.Message); _blink = null; }
        }

        private void TickBlink(float dt)
        {
            if (_blink == null) return;
            _blinkT += dt;
            float a = _blinkT < BlinkSeconds ? 1f : 1f - (_blinkT - BlinkSeconds) / BlinkFade;
            if (a <= 0f) { _blink.Destroy(); _blink = null; return; }
            try { _blink.Set(a); }
            catch (Exception) { _blink.Destroy(); _blink = null; }
        }

        private static bool WorldReady()
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

        private static Transform FindAvatar()
        {
            Transform root = WowPlayer.Root;
            if (root != null) return root;
            // Editor / lab without the client: the SDK test harness's (or the lab's) CharacterController - never our own proxy.
            if (Application.isEditor && !WowPlayer.HasBridgePlayer)
            {
                foreach (CharacterController cc in UnityEngine.Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                {
                    if (cc != null && cc.gameObject.name != "WoW Mount Proxy") return cc.transform;
                }
            }
            return null;
        }

        private static float GroundBelow(Vector3 p)
        {
            RaycastHit hit;
            return Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out hit, 500f, 1, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y;
        }

        private static WowMapView _editorMap;
        private static float _nextEditorMapSearch;

        private static WowMapView MapView()
        {
            WowGame g = WowGame.Current;
            if (g != null) return g.Map;
            // Editor / lab without WowGame (as WowPlayer's editor controller fallback): the scene's map view, searched once a second.
            if (!Application.isEditor) return null;
            if (_editorMap == null && Time.unscaledTime >= _nextEditorMapSearch)
            {
                _nextEditorMapSearch = Time.unscaledTime + 1f;
                _editorMap = UnityEngine.Object.FindFirstObjectByType<WowMapView>();
            }
            return _editorMap;
        }

        private static float Wrap(float a)
        {
            a %= 360f;
            if (a < 0f) a += 360f;
            return a;
        }
    }
}
