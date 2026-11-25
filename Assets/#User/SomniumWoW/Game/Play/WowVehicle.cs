// iteration 11 - owner vehicle (proposal-pets.md s6.3). Seats the Somnium avatar on a vehicle (Passenger / Turret seats, a driven vehicle or a
// possessed unit while the server moves it), puts the avatar on a vehicle / possessed unit before WowMount rides it (Drives(p), hook H7b),
// hides the unit WowMount draws as the "mount" (DrawnVehicle, hook H7c), turret aim input + the aimed spell's landing ring, enter / exit
// sounds, VR blink. VR comfort: the seat follows the vehicle's YAW only (never its pitch / roll), as the taxi. Added by WowGame after WowMount
// (hook H7a). Execution order -450: after WowMount (-500), so WowMount has let go before this takes the avatar and vice versa.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Flight;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Pets;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    [DefaultExecutionOrder(-450)]
    public sealed class WowVehicle : MonoBehaviour
    {
        /// <summary>VR blink at enter / exit = WowMount's (black 0.15 s, fade 0.08 s).</summary>
        public const float BlinkSeconds = WowMount.BlinkSeconds, BlinkFade = WowMount.BlinkFade;
        /// <summary>Ours: a stick counts as centred below this (= MountInput.StickCentred, ETS EtsVr.cs:85).</summary>
        public const float StickDead = MountInput.StickCentred;
        /// <summary>Ours: the turret aim wish runs this far ahead of the current aim while a key / stick is held (the session turns at
        /// Vehicle.dbc TurnSpeed / PitchSpeed toward it, so the turret moves at the vehicle's own speed).</summary>
        public const float AimLeadSeconds = 0.25f;
        /// <summary>Ours: the landing ring of an aimed vehicle shot when its triggered spell has no radius (yards).</summary>
        public const float MarkerRadiusFallback = 2f;
        /// <summary>Ours: a steered unit without a drawn view is handed to WowMount after this long anyway (seconds).</summary>
        public const float PlaceTimeout = 2f;

        /// <summary>A vehicle seat holds the avatar (Passenger / Turret, a drive / possession the server moves, or the moment before WowMount rides).</summary>
        public static bool OwnsAvatar { get; private set; }
        /// <summary>The seat's feet (Unity, current origin) while OwnsAvatar - the session's position.</summary>
        public static Vector3 Feet { get; private set; }
        /// <summary>Where the world should stream while OwnsAvatar.</summary>
        public static Vector3 StreamFocus { get; private set; }

        /// <summary>The unit WowMount draws as the "mount" while we drive it (its entity view hides), 0 = none.</summary>
        public static ulong DrawnVehicle
        {
            get
            {
                IWowSession s = WowPlay.Session;
                PlayerState p = s != null ? s.Player : null;
                return Drives(p) ? p.Vehicle.Guid : 0UL;
            }
        }

        /// <summary>We steer the vehicle / possessed unit ourselves: WowMount rides it (hook H7b). Only after this component has put the
        /// avatar on it (so the ride starts where the unit is, not where the avatar stood) and let go.</summary>
        public static bool Drives(PlayerState p)
        {
            VehicleView v = p != null ? p.Vehicle : null;
            if (v == null || (v.Mode != VehicleMode.Drive && v.Mode != VehicleMode.Possess) || !v.CanSteer || v.ServerMoving) return false;
            return _placedFor == v.Guid && !_holding;
        }

        /// <summary>Lets go of the avatar now (WowGame's teleports / back to the room - as WowMount.LetGo). placeAvatar false: the caller teleports.</summary>
        public static void LetGo(string why, bool placeAvatar)
        {
            WowVehicle me = _instance;
            if (me == null || !_holding) return;
            try { me.Release(why, me.CurrentSpot(), placeAvatar, false); }
            catch (Exception e) { WowLog.Error("Vehicle: LetGo(" + why + ") threw " + e); }
        }

        private static WowVehicle _instance;
        private static bool _holding;
        private static ulong _placedFor;

        private MountSeat _seat;
        private MountCamera _camera;
        private FlightFade _blink;
        private float _blinkT;
        private bool _subscribed;
        private ulong _heldFor;
        private VehicleMode _heldMode;
        private int _heldCounter = -1;
        private float _heldSince, _retryAt, _noViewSince = -1f;
        private Vector3 _seatPoint;
        private float _seatYaw;
        private bool _seatValid;
        private string _seatSource = "";
        private readonly HashSet<ulong> _loggedVehicles = new HashSet<ulong>();
        private int _lastExitSound;
        private bool _aimHeld;
        private InputDevice _left, _right;
        private float _nextScan;
        private bool _keysBroken, _xrBroken, _markerBroken, _loggedConstants;
        private ulong _drivesLoggedFor;

        // ------------------------------------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            _instance = this;
            _camera = GetComponent<MountCamera>();
            if (_camera == null) _camera = gameObject.AddComponent<MountCamera>();   // shared with WowMount (never both at once)
        }

        private void OnEnable()
        {
            _instance = this;
            if (_subscribed) return;
            _subscribed = true;
            WowPlay.Event += OnEvent;
            WowPlay.Ended += OnEnded;
            WowSpace.Rebased += OnRebased;
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
            if (_holding)
            {
                try { Release("the vehicle component was disabled", CurrentSpot(), false, false); }
                catch (Exception e) { WowLog.Error("Vehicle: release on disable threw " + e); }
            }
            if (_blink != null) { _blink.Destroy(); _blink = null; }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            OwnsAvatar = false;
            _holding = false;
            _placedFor = 0;
        }

        private void OnEnded()
        {
            if (_holding) Release("the game ended", CurrentSpot(), false, false);
            _placedFor = 0;
            _loggedVehicles.Clear();
        }

        private void OnEvent(GameEvent e)
        {
            try
            {
                switch (e.Type)
                {
                    case GameEventType.Teleport:
                        // WowGame teleports the avatar itself: the root goes to the target first (no drop), then the controller is back
                        if (_holding) Release("a game teleport", WowSpace.ToUnity(0, e.Pos.X, e.Pos.Y, e.Pos.Z), false, false);
                        break;
                    case GameEventType.VehicleEntered:
                        {
                            var si = VehicleDb.Seat(e.SpellId);
                            _lastExitSound = si != null ? si.ExitSound : 0;
                            if (si != null && si.EnterSound > 0) PlaySound(si.EnterSound, "enter");
                            break;
                        }
                    case GameEventType.VehicleLeft:
                        if (_lastExitSound > 0) PlaySound(_lastExitSound, "exit");
                        _lastExitSound = 0;
                        break;
                }
            }
            catch (Exception ex) { WowLog.Error("Vehicle: event " + e.Type + " threw " + ex); }
        }

        private void OnRebased(Vector3 d)
        {
            if (!_holding) return;
            _seatPoint -= d;
            StreamFocus -= d;
            Feet -= d;
            try
            {
                if (_seat != null && _seatValid) _seat.WriteRoot(_seatPoint, _seatYaw, WowPlayer.XrPresent);
            }
            catch (Exception e) { WowLog.Warn("Vehicle: rebase: " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------ Update (-450)

        private void Update()
        {
            TickBlink(Time.unscaledDeltaTime);
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            VehicleView v = p != null ? p.Vehicle : null;
            if (v == null)
            {
                if (_holding) Release("left the vehicle", SessionSpot(s, p), false, true);
                _placedFor = 0;
                _drivesLoggedFor = 0;
                OwnsAvatar = false;
                return;
            }
            LogConstantsOnce();
            bool steer = (v.Mode == VehicleMode.Drive || v.Mode == VehicleMode.Possess) && v.CanSteer && !v.ServerMoving;
            if (steer && _placedFor == v.Guid)
            {
                // WowMount rides it from here (no blink: the avatar is already on the unit)
                if (_holding) Release("WowMount drives " + Describe(v), CurrentFeet(v), false, false);
                OwnsAvatar = false;
                if (_drivesLoggedFor != v.Guid)
                {
                    _drivesLoggedFor = v.Guid;
                    WowLog.Info("Vehicle: WowMount drives " + Describe(v) + " (display " + v.DisplayId + ")");
                }
                return;
            }
            if (_placedFor != 0 && _placedFor != v.Guid) _placedFor = 0;
            if (p.Dead) { if (_holding) Release("death", CurrentSpot(), false, true); OwnsAvatar = false; return; }
            if (!_holding)
            {
                // another system still holds the avatar (the ride lands first, a flight, a forced move): wait
                if (WowMount.OwnsAvatar || WowFlight.Flying || WowForcedMove.OwnsAvatar || Time.unscaledTime < _retryAt) { OwnsAvatar = false; return; }
                if (!TakeOver(v)) { OwnsAvatar = false; return; }
            }
            else if (_heldFor != v.Guid || _heldMode != v.Mode || _heldCounter != v.Counter)
            {
                if (_heldFor != v.Guid || _heldMode != v.Mode) WowLog.Info("Vehicle: now " + Describe(v) + " (mode " + v.Mode + ")");
                _heldFor = v.Guid; _heldMode = v.Mode; _heldCounter = v.Counter;
            }
            OwnsAvatar = true;
            bool vr = WowPlayer.XrPresent;
            if (ComputeSeat(v, out _seatPoint, out _seatYaw)) _seatValid = true;
            Feet = CurrentFeet(v);
            StreamFocus = Feet;
            if (_seatValid) _seat.WriteRoot(_seatPoint, _seatYaw, vr);
            if (v.Mode == VehicleMode.Turret && v.CanSteer) TickTurretInput(s, v, vr);
            TickAimMarker(s, p, v);
        }

        // ------------------------------------------------------------------------------------------------ LateUpdate: the seat

        private void LateUpdate()
        {
            if (!_holding || _seat == null) return;
            IWowSession s = WowPlay.Session;
            VehicleView v = s != null && s.Player != null ? s.Player.Vehicle : null;
            if (v == null) return;
            bool vr = WowPlayer.XrPresent;
            if (ComputeSeat(v, out _seatPoint, out _seatYaw)) _seatValid = true;
            if (!_seatValid) return;
            _seat.Seat(_seatPoint, _seatYaw, vr);
            if (!vr && _camera != null && _camera.On) _camera.SetTarget(_seatPoint, _seatYaw, 0f);
            // a steered unit: the avatar is on it now - WowMount takes over next frame at its feet
            bool steer = (v.Mode == VehicleMode.Drive || v.Mode == VehicleMode.Possess) && v.CanSteer && !v.ServerMoving;
            if (steer && _placedFor != v.Guid)
            {
                bool haveView = WowEntityViews.GetView(v.Guid) != null;
                if (haveView || (_noViewSince >= 0f && Time.unscaledTime - _noViewSince > PlaceTimeout))
                {
                    _placedFor = v.Guid;
                    WowLog.Info("Vehicle: the avatar is on " + Describe(v) + (haveView ? "" : " (no drawn view after " + PlaceTimeout + " s - its server position)") + " - WowMount steers it next");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ take / release

        private bool TakeOver(VehicleView v)
        {
            Transform root = FindAvatar();
            if (root == null) { _retryAt = Time.unscaledTime + 3f; WowLog.Warn("Vehicle: no avatar to seat on " + Describe(v) + " - retrying in 3 s"); return false; }
            bool vr = WowPlayer.XrPresent;
            if (vr) Blink();
            _seat = new MountSeat();
            _seat.Tag = "Vehicle";
            string why;
            if (!_seat.TakeOver(root, out why))
            {
                _retryAt = Time.unscaledTime + 3f;
                WowLog.Warn("Vehicle: the seat could not take the avatar (" + why + ") - retrying in 3 s");
                _seat = null;
                return false;
            }
            _holding = true;
            _heldFor = v.Guid; _heldMode = v.Mode; _heldCounter = v.Counter;
            _heldSince = Time.unscaledTime;
            _seatValid = false;
            _noViewSince = WowEntityViews.GetView(v.Guid) == null ? Time.unscaledTime : -1f;
            WowOrigin.AvatarDriven = true;   // a rebase mid-seat commits at once; OnRebased rewrites the root (the FlightRide pattern)
            if (!vr && _camera != null) _camera.Begin();
            if (ComputeSeat(v, out _seatPoint, out _seatYaw)) { _seatValid = true; _seat.WriteRoot(_seatPoint, _seatYaw, vr); }
            _loggedVehicles.Add(v.Guid);
            WowLog.Info("Vehicle: seated on " + Describe(v) + " (mode " + v.Mode + ", " + _seatSource + "), offset " + SeatOffsetUnity(v).ToString("F2") + " m; controller " +
                        (_seat.CcFound ? "disabled" : "not found") + (vr ? "; VR blink" : "; desktop chase camera"));
            return true;
        }

        private void Release(string why, Vector3 spot, bool placeAvatar, bool blink)
        {
            if (!_holding) return;
            _holding = false;
            OwnsAvatar = false;
            bool vr = WowPlayer.XrPresent;
            if (vr && blink) Blink();
            string how = "none";
            float yaw = _seatYaw;
            if (_seat != null) _seat.Release(spot, yaw, placeAvatar, out how);
            if (!WowFlight.Flying && !WowMount.OwnsAvatar) WowOrigin.AvatarDriven = false;
            if (_camera != null && !WowMount.OwnsAvatar) _camera.End(why);
            WowLog.Info("Vehicle: released (" + why + ") at " + spot.ToString("F1") + ", teleport " + how + "; controller " + (_seat != null && _seat.CcFound ? "restored" : "none") +
                        ", pose and motion given back; " + (Time.unscaledTime - _heldSince).ToString("0") + " s seated");
            _seat = null;
            _seatValid = false;
            _aimHeld = false;
        }

        // ------------------------------------------------------------------------------------------------ the seat point

        /// <summary>Where the avatar sits (Unity) and its yaw: the vehicle model's attachment 0 ("mount main", as WowMount's mounts) else its view
        /// root, + the VehicleSeat offset turned by the view's yaw (s4.6: m_attachmentID cannot be decoded - ours, logged once per vehicle). A
        /// possessed / steered unit: its view root + its height (ours). No view: the unit's server position. Yaw only (VR comfort).</summary>
        private bool ComputeSeat(VehicleView v, out Vector3 point, out float yaw)
        {
            point = Vector3.zero;
            yaw = _seatYaw;
            Transform view = WowEntityViews.GetView(v.Guid);
            bool seat = v.Mode != VehicleMode.Possess && v.SeatIndex >= 0;   // a vehicle seat (passenger, turret, a drive before / between the ride)
            float viewYaw;
            Vector3 basePos;
            string src;
            if (view != null)
            {
                viewYaw = view.eulerAngles.y;
                basePos = view.position;
                src = "view root";
                if (_noViewSince >= 0f) _noViewSince = -1f;
            }
            else
            {
                IWowSession s = WowPlay.Session;
                EntityState e = s != null ? s.Find(v.Guid) : null;
                if (e == null) return false;   // keep the last seat point until the session clears the vehicle (s6.3)
                basePos = WowSpace.ToUnity(0, e.Pos.X, e.Pos.Y, e.Pos.Z);
                viewYaw = WowSpace.OrientationToYaw(e.Pos.O);
                src = "server position (no drawn view)";
                if (_noViewSince < 0f) _noViewSince = Time.unscaledTime;
            }
            if (seat)
            {
                Transform att = view != null ? WowEntityViews.GetAttachment(v.Guid, 0) : null;
                Vector3 at = att != null ? att.position : basePos;
                if (att != null) src = "attachment 0";
                point = at + Quaternion.Euler(0f, viewYaw, 0f) * SeatOffsetUnity(v);
                yaw = viewYaw + WowSpace.OrientationToYaw(v.SeatOffset.O);
            }
            else
            {
                float h, fp;
                if (view != null && WowEntityViews.TryGetSize(v.Guid, out h, out fp)) { point = basePos + Vector3.up * h; src += " + its height"; }
                else point = basePos;
                yaw = viewYaw;
            }
            if (_seatSource != src)
            {
                _seatSource = src;
                if (_holding) WowLog.Info("Vehicle: seat point on " + Describe(v) + " = " + src);
            }
            return true;
        }

        /// <summary>The seat offset (WoW yards, the vehicle's frame) as Unity metres in the vehicle's frame.</summary>
        private static Vector3 SeatOffsetUnity(VehicleView v)
        {
            return WowSpace.PointToUnity(v.SeatOffset.X, v.SeatOffset.Y, v.SeatOffset.Z);
        }

        /// <summary>The character's feet for the session: the vehicle's position + the seat offset (AC's passenger position, Vehicle.cpp:412-419),
        /// a steered unit's feet.</summary>
        private Vector3 CurrentFeet(VehicleView v)
        {
            Transform view = WowEntityViews.GetView(v.Guid);
            IWowSession s = WowPlay.Session;
            EntityState e = s != null ? s.Find(v.Guid) : null;
            Vector3 basePos;
            float yaw;
            if (view != null) { basePos = view.position; yaw = view.eulerAngles.y; }
            else if (e != null) { basePos = WowSpace.ToUnity(0, e.Pos.X, e.Pos.Y, e.Pos.Z); yaw = WowSpace.OrientationToYaw(e.Pos.O); }
            else return Feet;
            if (v.SeatIndex >= 0 && (v.Mode == VehicleMode.Passenger || v.Mode == VehicleMode.Turret)) basePos += Quaternion.Euler(0f, yaw, 0f) * SeatOffsetUnity(v);
            return basePos;
        }

        private Vector3 CurrentSpot()
        {
            if (_seatValid) return _seatPoint;
            return _seat != null && _seat.Root != null ? _seat.Root.position : Feet;
        }

        private static Vector3 SessionSpot(IWowSession s, PlayerState p)
        {
            if (p == null) return Feet;
            return WowSpace.ToUnity(0, p.Pos.X, p.Pos.Y, p.Pos.Z);
        }

        // ------------------------------------------------------------------------------------------------ turret input (s6.3)

        /// <summary>Desktop: A / D or Left / Right turn, W / S or Up / Down aim (legacy Input, as MountInput). VR: the left stick (x turn,
        /// y aim, as the rides) or the right stick's y (aim); the client's own snap / smooth turn (right stick) turns the turret too (ours).
        /// -> IWowSession.SetVehicleAim every frame the wish changes; the session turns at the vehicle's speeds.</summary>
        private void TickTurretInput(IWowSession s, VehicleView v, bool vr)
        {
            float turn = 0f, aim = 0f, clientTurnDeg = 0f;
            if (!vr)
            {
                if (!_keysBroken)
                {
                    try
                    {
                        if (!WowArmUi.TypingSomewhere())
                        {
                            bool l = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow), r = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                            bool u = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow), d = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
                            turn = l == r ? 0f : r ? 1f : -1f;
                            aim = u == d ? 0f : u ? 1f : -1f;
                        }
                    }
                    catch (Exception e) { _keysBroken = true; WowLog.Warn("Vehicle: desktop keys unavailable (legacy Input): " + e.Message); }
                }
            }
            else if (!_xrBroken)
            {
                try
                {
                    if ((!_left.isValid || !_right.isValid) && Time.unscaledTime >= _nextScan)
                    {
                        _nextScan = Time.unscaledTime + 1f;
                        _left = FirstDevice(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
                        _right = FirstDevice(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
                    }
                    Vector2 ls = Vector2.zero, rs = Vector2.zero;
                    if (_left.isValid && _left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 a)) ls = a;
                    if (_right.isValid && _right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 b)) rs = b;
                    turn = Mathf.Abs(ls.x) > StickDead ? ls.x : 0f;
                    float ay = Mathf.Abs(ls.y) >= Mathf.Abs(rs.y) ? ls.y : rs.y;
                    aim = Mathf.Abs(ay) > StickDead ? ay : 0f;
                }
                catch (Exception e) { _xrBroken = true; WowLog.Warn("Vehicle: VR sticks unavailable: " + e.Message); }
                float d;
                bool poseEntry;
                if (_seat != null && _seat.TryReadClientTurn(out d, out poseEntry)) clientTurnDeg = d;
            }
            EntityState e2 = s.Find(v.Guid);
            float curO = e2 != null ? e2.Pos.O : 0f;
            bool any = turn != 0f || aim != 0f || clientTurnDeg != 0f;
            if (any)
            {
                // WoW orientation grows counter-clockwise: turning right lowers it; a Unity yaw step of +d degrees = -d in orientation
                float wantO = curO - turn * Mathf.Max(0.05f, v.TurnSpeed) * AimLeadSeconds - clientTurnDeg * Mathf.Deg2Rad;
                float wantP = v.AimPitch + aim * Mathf.Max(0.05f, v.PitchSpeed) * AimLeadSeconds;
                s.SetVehicleAim(wantO, wantP);
                _aimHeld = true;
            }
            else if (_aimHeld)
            {
                _aimHeld = false;
                s.SetVehicleAim(curO, v.AimPitch);   // stop where it is
            }
        }

        private static InputDevice FirstDevice(InputDeviceCharacteristics c)
        {
            var list = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(c, list);
            return list.Count > 0 ? list[0] : default(InputDevice);
        }

        // ------------------------------------------------------------------------------------------------ aimed shots: the landing ring

        /// <summary>While the vehicle bar holds an Aimed slot (TARGET_DEST_TRAJ 89): spellfx's ring where the shot would land (VehicleDb.BallisticDest,
        /// the session's own formula), drawn this frame only.</summary>
        private void TickAimMarker(IWowSession s, PlayerState p, VehicleView v)
        {
            if (_markerBroken) return;
            ControlBarView bar = p.ControlBar;
            if (bar == null || bar.Kind != ControlBarKind.Vehicle || bar.UnitGuid != v.Guid) return;
            ControlSlotView aimed = null;
            for (int i = 0; i < bar.Slots.Length; i++)
                if (bar.Slots[i] != null && bar.Slots[i].Kind == ControlSlotKind.Spell && bar.Slots[i].Aimed) { aimed = bar.Slots[i]; break; }
            if (aimed == null) return;
            EntityState e = s.Find(v.Guid);
            if (e == null) return;
            try
            {
                SpellDb db = WowPlay.Files != null ? SpellDb.Get(WowPlay.Files) : null;
                SpellInfo sp = db != null ? db.Find(aimed.Action) : null;
                if (sp == null || sp.Speed <= 0f) return;
                var src = new WowPos(e.Pos.X, e.Pos.Y, e.Pos.Z + VehicleDb.ShotHeight, e.Pos.O);
                WowPos dest = VehicleDb.BallisticDest(src, e.Pos.O, v.AimPitch, sp.Speed, sp.RangeMax, GroundZ);
                UiGroundTarget.Marker(this, dest, ImpactRadius(db, sp), true);
            }
            catch (Exception ex) { _markerBroken = true; WowLog.Warn("Vehicle: the aim ring failed (" + ex.Message + ") - not drawn from now on"); }
        }

        /// <summary>The triggered spell's EffectRadius (TRIGGER_MISSILE 32 -> e.g. 52436), else the spell's own, else 2 yd (ours).</summary>
        private static float ImpactRadius(SpellDb db, SpellInfo sp)
        {
            float r = 0f;
            for (int i = 0; i < sp.Effects.Length; i++)
            {
                var ef = sp.Effects[i];
                if (ef == null) continue;
                r = Mathf.Max(r, ef.Radius);
                SpellInfo t = ef.TriggerSpell > 0 ? db.Find(ef.TriggerSpell) : null;
                if (t != null) for (int j = 0; j < t.Effects.Length; j++) if (t.Effects[j] != null) r = Mathf.Max(r, t.Effects[j].Radius);
            }
            return r > 0f ? r : MarkerRadiusFallback;
        }

        /// <summary>The loaded map's floor / terrain under a WoW point (yards), NaN when unknown.</summary>
        private static float GroundZ(float x, float y, float zHint)
        {
            WowGame g = WowGame.Current;
            WowMapView map = g != null ? g.Map : null;
            if (map == null) return float.NaN;
            Vector3 u = WowSpace.ToUnity(0, x, y, zHint);
            float gy;
            if (!map.TryFloorHeight(u, 4f, out gy) && !map.TryGroundHeight(u, out gy)) return float.NaN;
            float wx, wy, wz;
            WowSpace.ToWow(0, new Vector3(u.x, gy, u.z), out wx, out wy, out wz);
            return wz;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        private static Transform FindAvatar()
        {
            Transform root = WowPlayer.Root;
            if (root != null) return root;
            if (Application.isEditor && !WowPlayer.HasBridgePlayer)
            {
                foreach (CharacterController cc in UnityEngine.Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                    if (cc != null && cc.gameObject.name != "WoW Mount Proxy") return cc.transform;
            }
            return null;
        }

        private static string Describe(VehicleView v)
        {
            return (string.IsNullOrEmpty(v.Name) ? "unit" : v.Name) + " (0x" + v.Guid.ToString("X") + (v.VehicleId != 0 ? ", Vehicle " + v.VehicleId : "") +
                   (v.SeatIndex >= 0 ? ", seat " + v.SeatIndex : "") + ")";
        }

        private void PlaySound(int soundEntry, string what)
        {
            try { WowAudio.PlaySoundEntryUi(soundEntry); }
            catch (Exception e) { WowLog.Warn("Vehicle: the " + what + " sound " + soundEntry + " failed: " + e.Message); }
        }

        private void LogConstantsOnce()
        {
            if (_loggedConstants) return;
            _loggedConstants = true;
            WowLog.Info("Vehicle: ours - blink " + BlinkSeconds + " s + " + BlinkFade + " s (WowMount's), stick dead zone " + StickDead + ", turret aim lead " + AimLeadSeconds +
                        " s, aim ring fallback " + MarkerRadiusFallback + " yd, a steered unit without a view is handed to the ride after " + PlaceTimeout +
                        " s; the seat = the vehicle model's attachment 0 (else its root) + the VehicleSeat offset, yaw only");
        }

        private void Blink()
        {
            if (_blink == null) _blink = new FlightFade();
            _blinkT = 0f;
            try { _blink.Set(1f); }
            catch (Exception e) { WowLog.Warn("Vehicle: blink: " + e.Message); _blink = null; }
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
    }
}
