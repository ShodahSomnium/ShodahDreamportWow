using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.MapApp;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// Stage S1 (owner hud), a Dreamport addition: the focus button (WowFocus).
    /// VR: the LEFT controller's primary button (X on Touch controllers; UnityEngine.XR InputDevices + CommonUsages.primaryButton,
    /// the members GearXr already uses). Only SHORT presses are ours (released within WowFocus.ShortPressMax = 0.35 s): the client
    /// binds the same button to its "Inspector" action as a Hold with InputSystem's default hold time of 0.4 s
    /// (E:/Somnium Space/Main Build/Assets/_SomniumSpace/Inputs/VR/XRI Player Interactions.inputactions:70-78 and :392-398,
    /// Assets/InputSystem.inputsettings.asset:28 m_DefaultHoldTime: 0.4), so a long press stays the client's inspector mode and
    /// does nothing here. Nothing while a hand holds the Somnium tablet.
    /// Desktop: the H key (ours: F is the client's Interact and Shift its Sprint; no binding of H in any of the client's
    /// .inputactions; our UI uses 1-0 - = P L C B T V O J N K Z), never while a text field has the keyboard.
    /// A short press: no focus = the target becomes the focus; a focus = the focus becomes the target; three short presses within
    /// 0.8 s = the focus is cleared. A short haptic tick on the left controller when it did something (Settings > Controller vibration).
    /// Lives on the "WoW Game" object: WowGame adds it, or the HUD's party frames do on their first build (Ensure).
    /// </summary>
    public sealed class VrFocusButton : MonoBehaviour
    {
        public const KeyCode DesktopKey = KeyCode.H;
        public const string DesktopKeyName = "H";
        public const string VrButtonName = "X";

        private bool _down, _broken, _keyBroken, _loggedStart, _loggedDevice;
        private bool _downInspector; private int _inspectorPresses;   // stage S1 integrator (review finding 1)
        private float _downAt;
        private int _long, _short;
        private IWowSession _session;

        /// <summary>Adds the button once to the object that carries WowArmUi (the "WoW Game" object) above this transform.</summary>
        public static void Ensure(Transform near)
        {
            try
            {
                if (near == null) return;
                WowArmUi ui = near.GetComponentInParent<WowArmUi>();
                GameObject go = ui != null ? ui.gameObject : null;
                if (go == null || go.GetComponent<VrFocusButton>() != null) return;
                go.AddComponent<VrFocusButton>();
            }
            catch (Exception e) { WowLog.Warn("Focus: adding the focus button failed: " + e.Message); }
        }

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            if (s != _session)
            {
                _session = s;
                _down = false;
                WowFocus.ResetPresses();
                if (s != null && !_loggedStart)
                {
                    _loggedStart = true;
                    WowLog.Info("Focus: button ready - VR: a short press of the left " + VrButtonName + " (under " + WowFocus.ShortPressMax.ToString("0.00")
                        + " s; longer presses stay the client's inspector mode), desktop: " + DesktopKeyName + ". No focus = the target becomes the focus, a focus = it is targeted, three presses within "
                        + WowFocus.TripleWindow.ToString("0.0") + " s = cleared.");
                }
            }
            if (s == null) return;
            float now = Time.unscaledTime;
            if (WowPlayer.XrPresent) Vr(s, now);
            else { _down = false; Desk(s, now); }
        }

        private void Vr(IWowSession s, float now)
        {
            if (_broken) return;
            bool pressed;
            try
            {
                InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                bool b;
                pressed = left.isValid && left.TryGetFeatureValue(CommonUsages.primaryButton, out b) && b;
                if (left.isValid && !_loggedDevice)
                {
                    _loggedDevice = true;
                    bool probe;
                    WowLog.Info("Focus: left controller '" + left.name + "' reports primaryButton " + (left.TryGetFeatureValue(CommonUsages.primaryButton, out probe) ? "yes" : "NO") + ".");
                }
            }
            catch (Exception e)
            {
                _broken = true;
                WowLog.Warn("Focus: the left controller's button is unavailable: " + e.Message);
                return;
            }
            if (pressed && !_down)
            {
                _down = true;
                _downAt = now;
                _downInspector = VrClientLaser.Inspector;
                return;
            }
            if (pressed || !_down) return;
            _down = false;
            float held = now - _downAt;
            if (held >= WowFocus.ShortPressMax)
            {
                _long++;
                if (_long <= 3) WowLog.Info("Focus: long " + VrButtonName + " press (" + held.ToString("0.00") + " s) left to the client (inspector mode).");
                return;
            }
            if (WowMapTabletLink.TabletHeld) return;   // the tablet in hand: the buttons are the tablet's business
            // Stage S1 (integrator, review finding 1): with the client's inspector mode on, the same X press / multi-tap is ITS Mute / Hide
            // of the inspected object (CS Inputs/VR/XRI Player Interactions.inputactions:410 / :432, InspectorBase.cs:234-252) - left to it.
            if (_downInspector || VrClientLaser.Inspector)
            {
                if (_inspectorPresses++ < 3) WowLog.Info("Focus: short " + VrButtonName + " press while the client's inspector mode is on - left to the client (its Mute / Hide).");
                return;
            }
            Press(s, now, "the " + VrButtonName + " button (VR)", true);
        }

        private void Desk(IWowSession s, float now)
        {
            if (_keyBroken) return;
            try
            {
                if (WowArmUi.TypingSomewhere() || SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiChat.Typing) return;   // a text field / the chat edit box has the keyboard
                if (!Input.GetKeyDown(DesktopKey)) return;
            }
            catch (Exception e)
            {
                _keyBroken = true;
                WowLog.Warn("Focus: the " + DesktopKeyName + " key is unavailable (legacy Input): " + e.Message);
                return;
            }
            Press(s, now, "the " + DesktopKeyName + " key", false);
        }

        private void Press(IWowSession s, float now, string how, bool vr)
        {
            string did;
            try { did = WowFocus.ShortPress(s, now, how); }
            catch (Exception e) { WowLog.Error("Focus: " + how + " failed: " + e); return; }
            _short++;
            if (_short <= 10 || _short % 50 == 0) WowLog.Info("Focus: " + how + " short press " + _short + " -> " + did + ".");
            if (vr && did != "none") GearXr.Pulse(false, did == "clear" ? 0.5f : 0.3f, did == "clear" ? 0.08f : 0.04f);
        }
    }
}
