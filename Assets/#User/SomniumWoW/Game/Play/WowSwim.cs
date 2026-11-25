using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.World;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Lead 2026-10-26 (user: "implement swimming logic just like in wow but for Somnium player, at the moment we just walk on the floor").
    /// On foot in water too deep to stand in, the Somnium avatar swims:
    /// - When: the water above the floor under the feet is deeper than 0.75 x the avatar's controller height (AC Unit.cpp "exact mirror of
    ///   client logic": liquid level - z > collision height x 0.75, the rule OfflineSession.Mounts / AudioMountSteps already use) and the feet
    ///   are under the surface. Ends when the floor comes within standing depth (0.10 m hysteresis) - you walk out at the shore.
    /// - The water: terrain liquid from the ground data (MountPlace.TryLiquid, the mounts' source) and WMO liquid (canals, inside buildings)
    ///   from WowLiquidSurface; the highest wins. The floor: one ray on layer 0 (terrain tiles + WMO groups), the terrain height as fallback.
    /// - How: the client's own controller keeps the sideways motion (its keys / stick, at WoW's swim speed through WowRunSpeed); gravity and
    ///   jump are 0 (Bridge SetGravity / SetJump, as AvatarHold) and the falling animation is off; the height is ours: every frame the
    ///   controller's transform is put at "surface - depth" + Physics.SyncTransforms (TransportCarry's proven write; position only).
    ///   Entering, you rise to the surface (feet 0.75 x height under it: head and shoulders out, as WoW floats you); there is no other
    ///   buoyancy - as in WoW you stay at the depth you swim to. Never above the surface float height, never below the floor.
    /// - Up / down: desktop Space up, Left Ctrl down (X is Somnium's mute - MountInput's binding); W with the right mouse held swims where the
    ///   camera looks (WoW's mouselook). VR: right A up; the left stick forward swims where the head looks beyond +-12 degrees (MountInput's
    ///   climb dead zone). Rates = the swim speed.
    /// - Server: PlayerMotion Driven + Swimming (+ Forward / Strafe / Vertical / Pitch from the measured motion) -> OnlineSession.Rider sends
    ///   MSG_MOVE_START_SWIM / STOP_SWIM, ascend / descend, pitch, heartbeats - normal WoW clients see us swim; breath (the mirror timer
    ///   bar) and drowning are the server's.
    /// A mount, flight, vehicle, transport, forced move or the ground hold (WowOrigin.AvatarDriven) owns the avatar: swimming steps aside
    /// without touching the motion settings (each owner restores the client's defaults when it lets go).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class WowSwim : MonoBehaviour
    {
        public const float SwimDepthFactor = 0.75f;   // AC Unit.cpp 4217-4219 (OfflineSession.Mounts.SwimDepthFactor)
        public const float ExitHysteresis = 0.10f;    // ours, m: the floor must come this much shallower than the swim depth to stand
        public const float EnterMargin = 0.05f;       // ours, m
        public const float UnderSurface = 0.05f;      // ours, m: the feet must be this far under the surface to start swimming
        public const float RiseSpeed = 1.2f;          // ours, m/s: rising to the surface after entering
        public const float SettleSpeed = 2.5f;        // ours, m/s: easing into the allowed depth range (a fall into the water)
        public const float FloorGap = 0.02f;          // ours, m: the lowest the feet go above the floor
        public const float VrPitchDeadDeg = 12f;      // MountInput.VrClimbDeadDeg
        public const float LostSeconds = 1f;          // ours: no surface / floor for this long ends swimming
        private const float YardsToMetres = 0.9144f;
        private const int WorldMask = 1 << 0;         // terrain tiles + WMO groups (MountMotor.WorldMask)

        /// <summary>The avatar swims (we hold its height; the session gets Motion).</summary>
        public static bool Active { get; private set; }
        /// <summary>This frame's motion for IWowSession.SetPlayerMotion while Active (WowGame).</summary>
        public static PlayerMotion Motion { get; private set; }

        private float _depth;              // m of the feet under the surface while Active
        private bool _rising, _loggedRules, _keysBroken, _xrBroken;
        private float _lostFor;
        private Vector3 _lastFeet;
        private float _lastAt = -1f;
        private int _swims;
        private Transform _ccFor;
        private CharacterController _cc;
        private InputDevice _left, _right;
        private float _nextScan;
        private static readonly List<InputDevice> Found = new List<InputDevice>();

        private void Update()
        {
            try { Step(); }
            catch (Exception e)
            {
                WowLog.Warn("Swim: " + e.GetType().Name + ": " + e.Message + " - stopped swimming");
                Stop("an error", true);
            }
        }

        private void OnDisable() { Stop("the game component stopped", true); }

        private void Step()
        {
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            Transform root = WowPlayer.Root;
            if (s == null || p == null || root == null) { Stop("no game", true); return; }
            if (WowMount.OwnsAvatar || WowFlight.Flying || WowVehicle.OwnsAvatar || WowForcedMove.OwnsAvatar || WowOrigin.AvatarDriven || p.OnTaxi)
            {
                Stop("something else moves the avatar", false);
                return;
            }
            if (p.Dead && !p.Ghost) { Stop("dead", true); return; }
            if (!_loggedRules)
            {
                _loggedRules = true;
                WowLog.Info("Swim: rules - swim when the water over the floor is deeper than " + SwimDepthFactor + " x the avatar's height (AC), out at "
                    + ExitHysteresis + " m shallower; float with the feet " + SwimDepthFactor + " x height under the surface; desktop Space up / Left Ctrl down / "
                    + "right mouse + W follows the camera; VR A up, stick forward follows the head beyond " + VrPitchDeadDeg + " deg; terrain + WMO water.");
            }

            CharacterController cc = Controller(root);
            Transform body = cc != null && root.IsChildOf(cc.transform) ? cc.transform : root;
            float height = cc != null && cc.height > 0.5f ? cc.height : 1.8f;
            float swimDepth = SwimDepthFactor * height;
            Vector3 feet = root.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            float surface, floor;
            bool haveSurface = TrySurface(s, feet, height, out surface);
            bool haveFloor = TryFloor(feet, out floor);
            if (!Active)
            {
                if (!haveSurface || !haveFloor) return;
                if (surface - floor > swimDepth + EnterMargin && feet.y < surface - UnderSurface) Start(surface, floor, feet, swimDepth);
                return;
            }
            if (!haveSurface || !haveFloor)
            {
                _lostFor += dt;
                if (_lostFor > LostSeconds) Stop("the water or the floor under it is not known for " + LostSeconds + " s", true);
                return;
            }
            _lostFor = 0f;
            if (surface - floor < swimDepth - ExitHysteresis) { Stop("the floor is within standing depth", true); return; }
            float maxDepth = surface - floor - FloorGap;      // the feet on the floor

            float swimMps = (p.SwimSpeed > 0.1f ? p.SwimSpeed : 4.722222f) * YardsToMetres;
            float up = VerticalInput(swimMps);
            if (_rising)
            {
                if (up != 0f || _depth <= swimDepth + 0.01f) _rising = false;
                else up = RiseSpeed;
            }
            _depth -= up * dt;
            if (_depth < swimDepth) _depth = Mathf.Min(swimDepth, _depth + SettleSpeed * dt);   // above the float height (a jump in): ease down
            if (_depth > maxDepth) _depth = maxDepth;                                          // never into the floor
            if (_depth < 0f) _depth = 0f;
            float targetY = surface - _depth;
            if (Mathf.Abs(targetY - feet.y) > 1e-4f)
            {
                body.position += Vector3.up * (targetY - feet.y);
                Physics.SyncTransforms();
            }
            feet.y = targetY;
            BuildMotion(feet, up);
        }

        private void Start(float surface, float floor, Vector3 feet, float swimDepth)
        {
            ISomniumPlayer player = Local();
            try
            {
                ISomniumPlayerMotion m = player?.Features?.Motion;
                m?.SetGravity(0f, 0f);
                m?.SetJump(0f, 0f);
            }
            catch (Exception e) { WowLog.Warn("Swim: SetGravity / SetJump threw " + e.Message); }
            try { player?.Features?.Animation?.SetFallingAnimationDisabled(true); } catch (Exception) { }
            Active = true;
            _depth = surface - feet.y;
            _rising = true;
            _lostFor = 0f;
            _lastAt = -1f;
            _swims++;
            if (_swims <= 30 || _swims % 50 == 0)
                WowLog.Info("Swim: swimming (water " + (surface - floor).ToString("0.00") + " m deep, feet " + _depth.ToString("0.00") + " m under the surface, float at "
                    + swimDepth.ToString("0.00") + " m) - gravity and jump 0, rising to the surface (" + _swims + ")");
        }

        /// <summary>Ends swimming. restore = give the client its gravity / jump / falling animation back (false when another owner took the
        /// avatar: it set its own values and restores the defaults itself).</summary>
        private void Stop(string why, bool restore)
        {
            if (!Active) return;
            Active = false;
            Motion = default(PlayerMotion);
            if (restore)
            {
                ISomniumPlayer player = Local();
                try
                {
                    ISomniumPlayerMotion m = player?.Features?.Motion;
                    m?.SetGravity(1f, 1f);
                    m?.SetJump(1f, 1f);
                }
                catch (Exception e) { WowLog.Warn("Swim: restoring gravity / jump threw " + e.Message); }
                try { player?.Features?.Animation?.SetFallingAnimationDisabled(false); } catch (Exception) { }
            }
            if (_swims <= 30 || _swims % 50 == 0) WowLog.Info("Swim: out of the water (" + why + ")");
        }

        /// <summary>The water surface (Unity y) at the feet: terrain liquid and WMO liquid, the higher one.</summary>
        private static bool TrySurface(IWowSession s, Vector3 feet, float height, out float surface)
        {
            surface = float.NegativeInfinity;
            bool found = false;
            MountPlace place = WowMount.PlaceSampler;
            float y;
            if (place != null)
            {
                try { if (place.TryLiquid(s.MapId, feet, out y)) { surface = y; found = true; } }
                catch (Exception) { }
            }
            // WMO water above the feet or up to a body height under them (a dive from a quay)
            if (WowLiquidSurface.TrySurface(feet + Vector3.up * (height * 2f), height * 3f, out y) && y > surface) { surface = y; found = true; }
            return found;
        }

        /// <summary>The floor under the feet (Unity y): layer 0 ray from just above them, else the terrain height.</summary>
        private static bool TryFloor(Vector3 feet, out float floor)
        {
            floor = 0f;
            RaycastHit hit;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out hit, 400f, WorldMask, QueryTriggerInteraction.Ignore)) { floor = hit.point.y; return true; }
            WowGame game = WowGame.Current;
            WowMapView map = game != null ? game.Map : null;
            return map != null && map.TryGroundHeight(feet, out floor);
        }

        /// <summary>m/s, + up: the swim keys, and the view's pitch while swimming forward (desktop with the right mouse, VR beyond the dead zone).</summary>
        private float VerticalInput(float swimMps)
        {
            if (WowPlayer.XrPresent) return VrVertical(swimMps);
            if (_keysBroken) return 0f;
            try
            {
                if (Typing()) return 0f;
                float v = 0f;
                if (Input.GetKey(KeyCode.Space)) v += swimMps;
                if (Input.GetKey(KeyCode.LeftControl)) v -= swimMps;
                if (v == 0f && Input.GetMouseButton(1))
                {
                    int fwd = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                    Camera cam = Camera.main;
                    if (fwd != 0 && cam != null) v = swimMps * fwd * Mathf.Clamp(cam.transform.forward.y, -1f, 1f);
                }
                return v;
            }
            catch (Exception e)
            {
                _keysBroken = true;
                WowLog.Warn("Swim: desktop keys unavailable (legacy Input): " + e.Message);
                return 0f;
            }
        }

        private float VrVertical(float swimMps)
        {
            if (_xrBroken) return 0f;
            try
            {
                if ((!_left.isValid || !_right.isValid) && Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 1f;
                    _left = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
                    _right = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
                }
                bool a = _right.isValid && _right.TryGetFeatureValue(CommonUsages.primaryButton, out bool down) && down;
                if (a) return swimMps;
                Vector2 stick = Vector2.zero;
                if (_left.isValid && _left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 st)) stick = st;
                Camera head = WowPlayer.Head();
                if (stick.y <= 0.2f || head == null) return 0f;
                float pitch = Mathf.Asin(Mathf.Clamp(head.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;   // + looking up
                if (Mathf.Abs(pitch) <= VrPitchDeadDeg) return 0f;
                float shaped = Mathf.Sign(pitch) * Mathf.Min(90f, Mathf.Abs(pitch) - VrPitchDeadDeg) * Mathf.Deg2Rad;
                return swimMps * stick.y * Mathf.Sin(shaped);
            }
            catch (Exception e)
            {
                _xrBroken = true;
                WowLog.Warn("Swim: VR controller input unavailable: " + e.Message);
                return 0f;
            }
        }

        /// <summary>PlayerMotion for the session from the measured motion: forward / strafe relative to the facing, vertical, pitch.</summary>
        private void BuildMotion(Vector3 feet, float up)
        {
            float now = Time.time;
            var m = new PlayerMotion { Driven = true, Swimming = true };
            if (_lastAt >= 0f && now - _lastAt > 1e-4f)
            {
                float dt = now - _lastAt;
                Vector3 v = (feet - _lastFeet) / dt;
                float yaw;
                string src;
                if (!WowPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * new Vector3(v.x, 0f, v.z);
                const float Moving = 0.3f;   // m/s, ours
                m.Forward = local.z > Moving ? 1 : local.z < -Moving ? -1 : 0;
                m.Strafe = local.x > Moving ? 1 : local.x < -Moving ? -1 : 0;
                m.Vertical = v.y > Moving ? 1 : v.y < -Moving ? -1 : 0;
                float planar = new Vector2(v.x, v.z).magnitude;
                m.Pitch = planar > Moving || Mathf.Abs(v.y) > Moving ? Mathf.Atan2(v.y, Mathf.Max(planar, 1e-3f)) : 0f;
            }
            else if (up != 0f) m.Vertical = up > 0f ? 1 : -1;
            _lastFeet = feet;
            _lastAt = now;
            Motion = m;
        }

        private CharacterController Controller(Transform root)
        {
            if (_ccFor != root)
            {
                _ccFor = root;
                _cc = root.GetComponentInParent<CharacterController>();
                if (_cc == null) _cc = root.GetComponentInChildren<CharacterController>();
            }
            return _cc;
        }

        private static ISomniumPlayer Local()
        {
            try { return SomniumBridge.PlayersContainer?.LocalPlayer; }
            catch (Exception) { return null; }
        }

        private static InputDevice First(InputDeviceCharacteristics wanted)
        {
            InputDevices.GetDevicesWithCharacteristics(wanted, Found);
            return Found.Count > 0 ? Found[0] : default(InputDevice);
        }

        /// <summary>A text field has the keyboard (MountInput.TypingSomewhere's rule) or the WoW chat box is open.</summary>
        private static bool Typing()
        {
            if (SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiChat.Typing) return true;
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
        }
    }
}
