using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Mounts
{
    /// <summary>
    /// The rider's input -> RideIntent (iteration 7, owner Ride; PLAN7 s5.3 "Inputs", s4.9 bindings).
    /// Desktop (legacy Input, free while the avatar's CharacterController is off - NonXRMovementProvider.MovingProcess returns
    /// early): W / S and the arrows forward / back, A / D turn at WoW's turn rate, Q / E strafe, Space jump / take off / climb,
    /// Left Ctrl descend (decision D5: X is Somnium's Mute/Hide), right mouse held = mouse steering (mouse X turns the mount 3
    /// degrees per unit, ETS MouseSensitivity 3; mouse Y is the chase camera's pitch = the climb angle while flying). Nothing while
    /// a text field has the keyboard (the WowArmUi.TypingSomewhere rule, copied: it is private there).
    /// VR (UnityEngine.XR InputDevices / CommonUsages as ETS EtsVr and Gear/GearXr read them): left stick Y -> stop / walk / run
    /// steps with hysteresis (back = RunBack), left stick X -> smooth steering (ETS dead zone 0.15 rescaled, quadratic), right A =
    /// jump / take off / climb (held), climb angle = head pitch (dead zone +-12 deg, x 1.2, capped +-60). The travel direction is
    /// always the mount's heading, never the head's yaw. Nothing acts until the stick is centred and A released after mount-up
    /// (ETS _vrPedalsArmed). The client's own right-stick snap / smooth turn is read back by WowMount, not here.
    /// </summary>
    public sealed class MountInput
    {
        /// <summary>ETS EtsTruckCamera.MouseSensitivity = 3f (line 28): degrees of mount turn per unit of mouse X while the right mouse is held.</summary>
        public const float MouseSteerDegPerUnit = 3f;
        /// <summary>Ours (PLAN7 s4.8): VR climb angle = (head pitch - 12) x 1.2 outside a +-12 degree dead zone, capped +-60.</summary>
        public const float VrClimbDeadDeg = 12f, VrClimbGain = 1.2f, MaxClimbDeg = 60f;
        /// <summary>ETS dead zone 0.15 (EtsVr.cs:85): the stick counts as centred below it (arming).</summary>
        public const float StickCentred = 0.15f;

        private RideSpeedStep _step;
        private bool _armed;
        private InputDevice _left, _right;
        private float _nextScan;
        private bool _aWas;
        private bool _keysBroken, _xrBroken;
        private static readonly List<InputDevice> Found = new List<InputDevice>();

        /// <summary>The right mouse is held this frame (desktop mouse steering / the camera's pitch).</summary>
        public bool MouseSteering { get; private set; }

        /// <summary>VR: inputs are armed (the stick was centred and A released after mount-up).</summary>
        public bool Armed { get { return _armed; } }

        /// <summary>At mount-up: VR inputs disarmed until released, the speed step back to stop.</summary>
        public void Reset()
        {
            _armed = false;
            _step = RideSpeedStep.Stop;
            _aWas = false;
            MouseSteering = false;
        }

        /// <summary>This frame's intent. cameraPitchDeg = the desktop chase camera's pitch (+ = above, looking down).</summary>
        public RideIntent Read(bool vr, float cameraPitchDeg)
        {
            return vr ? ReadVr() : ReadDesktop(cameraPitchDeg);
        }

        private RideIntent ReadDesktop(float cameraPitchDeg)
        {
            RideIntent i = default(RideIntent);
            MouseSteering = false;
            if (_keysBroken) return i;
            try
            {
                if (TypingSomewhere()) return i;
                bool f = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                bool b = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
                bool tl = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
                bool tr = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                bool sl = Input.GetKey(KeyCode.Q), sr = Input.GetKey(KeyCode.E);
                i.Forward = f == b ? 0 : f ? 1 : -1;
                i.Strafe = sl == sr ? 0 : sr ? 1 : -1;
                i.Step = RideSpeedStep.Run;
                i.TurnAxis = tl == tr ? 0f : tr ? 1f : -1f;
                i.JumpPressed = Input.GetKeyDown(KeyCode.Space);
                i.AscendHeld = Input.GetKey(KeyCode.Space);
                i.DescendHeld = Input.GetKey(KeyCode.LeftControl);
                MouseSteering = Input.GetMouseButton(1);
                if (MouseSteering)
                {
                    float mx = Input.GetAxisRaw("Mouse X");
                    if (!float.IsNaN(mx)) i.TurnDeltaDeg = mx * MouseSteerDegPerUnit;
                    i.ClimbValid = true;
                    i.ClimbDeg = Mathf.Clamp(-cameraPitchDeg, -MaxClimbDeg, MaxClimbDeg);   // WoW's mouse flight: where the camera looks
                }
            }
            catch (Exception e)
            {
                _keysBroken = true;
                WowLog.Warn("Ride: desktop keys unavailable (legacy Input): " + e.Message);
                i = default(RideIntent);
            }
            return i;
        }

        private RideIntent ReadVr()
        {
            RideIntent i = default(RideIntent);
            if (_xrBroken) return i;
            try
            {
                if ((!_left.isValid || !_right.isValid) && Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 1f;
                    _left = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
                    _right = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
                }
                Vector2 stick = Vector2.zero;
                if (_left.isValid && _left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 s)) stick = s;
                bool a = _right.isValid && _right.TryGetFeatureValue(CommonUsages.primaryButton, out bool down) && down;
                if (!_armed)
                {
                    if (Mathf.Abs(stick.x) < StickCentred && Mathf.Abs(stick.y) < StickCentred && !a)
                    {
                        _armed = true;
                        WowLog.Info("Ride: VR inputs armed (left stick centred, A released).");
                    }
                    _aWas = a;
                    return i;
                }
                _step = RideMotion.StickStep(stick.y, _step);
                i.Step = _step;
                i.Forward = _step == RideSpeedStep.Stop ? 0 : stick.y > 0f ? 1 : -1;
                i.TurnAxis = RideMotion.ShapeAxis(stick.x);
                i.JumpPressed = a && !_aWas;
                i.AscendHeld = a;
                _aWas = a;
                Camera head = WowPlayer.Head();
                if (head != null)
                {
                    Vector3 fwd = head.transform.forward;
                    float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;   // + = looking up
                    if (Mathf.Abs(pitch) > VrClimbDeadDeg)
                    {
                        i.ClimbValid = true;
                        i.ClimbDeg = Mathf.Clamp(Mathf.Sign(pitch) * (Mathf.Abs(pitch) - VrClimbDeadDeg) * VrClimbGain, -MaxClimbDeg, MaxClimbDeg);
                    }
                }
            }
            catch (Exception e)
            {
                _xrBroken = true;
                WowLog.Warn("Ride: VR controller input unavailable: " + e.Message);
                i = default(RideIntent);
            }
            return i;
        }

        private static InputDevice First(InputDeviceCharacteristics wanted)
        {
            InputDevices.GetDevicesWithCharacteristics(wanted, Found);
            return Found.Count > 0 ? Found[0] : default(InputDevice);
        }

        /// <summary>A text field (the client's chat, any input field) has the keyboard (WowArmUi.TypingSomewhere's rule).</summary>
        private static bool TypingSomewhere()
        {
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
        }
    }
}
