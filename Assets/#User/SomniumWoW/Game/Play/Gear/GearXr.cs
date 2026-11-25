using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// The controllers through UnityEngine.XR (InputDevices / CommonUsages), as the Euro Truck Sim Dreamport reads them
    /// (EtsVr: grip analog first, the button as a floor; haptic impulses where the device supports them). Unity.InputSystem
    /// is not on the upload server's list. The grip is not taken from the client: its XRI "Grab" interactor only selects
    /// interactables (EtsSteeringGrab's finding), and nothing of ours is one, so a grip near a sheath answers only here.
    /// </summary>
    public static class GearXr
    {
        public const float GripOn = 0.55f, GripOff = 0.35f;

        private static InputDevice _left, _right;
        private static readonly List<InputDevice> Found = new List<InputDevice>();
        private static float _nextScan;
        private static int _frame = -1;
        private static bool _present;
        private static readonly bool[] _down = new bool[2];
        private static readonly bool[] _pressed = new bool[2];
        private static readonly int[] _impulse = new int[2];   // 0 unknown, 1 yes, 2 no
        private static bool _described;

        /// <summary>A headset is running (XRSettings, as EtsVr.Present).</summary>
        public static bool Present
        {
            get
            {
                Step();
                return _present;
            }
        }

        /// <summary>The grip of a hand is held (with hysteresis).</summary>
        public static bool GripHeld(bool right) { Step(); return _down[right ? 1 : 0]; }

        /// <summary>The grip of a hand went down this frame.</summary>
        public static bool GripPressed(bool right) { Step(); return _pressed[right ? 1 : 0]; }

        private static void Step()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _present = XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName);
            if (!_present)
            {
                _down[0] = _down[1] = _pressed[0] = _pressed[1] = false;
                return;
            }
            if (!_left.isValid || !_right.isValid)
            {
                if (Time.unscaledTime >= _nextScan)
                {
                    _nextScan = Time.unscaledTime + 1f;
                    _left = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
                    _right = First(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
                    _impulse[0] = _impulse[1] = 0;
                    _kind[0] = _kind[1] = 0;   // lead 2026-10-05: classify the new devices again
                    if (_left.isValid && _right.isValid && !_described)
                    {
                        _described = true;
                        WowLog.Info("Gear: VR controllers " + Describe(_left) + " | " + Describe(_right));
                    }
                }
            }
            for (int h = 0; h < 2; h++)
            {
                InputDevice d = h == 1 ? _right : _left;
                float g = Grip(d);
                bool was = _down[h];
                bool now = g >= (was ? GripOff : GripOn);
                _pressed[h] = now && !was;
                _down[h] = now;
            }
        }

        /// <summary>
        /// Lead 2026-10-05 (the user: "for index controllers, please use FORCE, as at the moment, the slightest touch of the index grip area"
        /// opens the menu): on a Valve Index ("Knuckles") the grip value and button follow the capacitive grip, so a touch reads as held. A
        /// float feature whose name says "force" (Unity OpenXR's Index profile: gripForce / "GripForce") is used instead when the device
        /// has one - held at IndexForceOn of the squeeze force; an Index without it ignores the touch button and needs IndexGripOn of the
        /// grip value. Every Index logs its feature names once (the next Player.log says which it had). Values ours.
        /// </summary>
        public const float IndexForceOn = 0.25f, IndexGripOn = 0.9f;
        private static readonly int[] _kind = new int[2];             // 0 not looked, 1 normal, 2 Index with force, 3 Index without force
        private static readonly InputFeatureUsage<float>[] _force = new InputFeatureUsage<float>[2];
        private static readonly List<InputFeatureUsage> Usages = new List<InputFeatureUsage>();

        private static float Grip(InputDevice device)
        {
            if (!device.isValid) return 0f;
            int h = device == _right ? 1 : 0;
            if (_kind[h] == 0) Classify(device, h);
            float value = device.TryGetFeatureValue(CommonUsages.grip, out float squeezed) ? squeezed : 0f;
            if (_kind[h] == 2)
            {
                // the squeeze force (0..1) mapped so IndexForceOn reads as GripOn
                float f = device.TryGetFeatureValue(_force[h], out float force) ? force : 0f;
                return f >= IndexForceOn ? 1f : f / IndexForceOn * GripOff;
            }
            if (_kind[h] == 3) return value >= IndexGripOn ? 1f : value * GripOff;   // no touch button, a near-full grip
            if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool down) && down) value = Mathf.Max(value, 1f);
            return value;
        }

        private static void Classify(InputDevice device, int h)
        {
            string n = device.name ?? "";
            bool index = n.IndexOf("Index", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Knuckles", System.StringComparison.OrdinalIgnoreCase) >= 0
                         || n.IndexOf("Valve", System.StringComparison.OrdinalIgnoreCase) >= 0;
            _kind[h] = 1;
            if (!index) return;
            _kind[h] = 3;
            var names = new System.Text.StringBuilder();
            Usages.Clear();
            if (device.TryGetFeatureUsages(Usages))
                foreach (InputFeatureUsage u in Usages)
                {
                    names.Append(names.Length > 0 ? ", " : "").Append(u.name).Append(':').Append(u.type.Name);
                    if (_kind[h] == 3 && u.type == typeof(float) && u.name.IndexOf("force", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _force[h] = new InputFeatureUsage<float>(u.name);
                        _kind[h] = 2;
                    }
                }
            WowLog.Info("Gear: '" + n + "' (" + (h == 1 ? "right" : "left") + ") is a Valve Index - grip from " + (_kind[h] == 2 ? "the squeeze force '" + _force[h].name + "' (held at " + IndexForceOn + ")"
                        : "the grip value only (held at " + IndexGripOn + ", no force feature found)") + "; features: " + names);
        }

        /// <summary>A short vibration on a hand (nothing when the device has no impulse haptics).</summary>
        /// <summary>WowSettings.Haptics (WowGear copies it on WowSettings.Changed): false = no controller pulses at all.</summary>
        public static bool HapticsOn = true;

        public static void Pulse(bool right, float amplitude, float seconds)
        {
            if (!HapticsOn || !Present) return;
            int h = right ? 1 : 0;
            InputDevice device = right ? _right : _left;
            if (!device.isValid) return;
            if (_impulse[h] == 0) _impulse[h] = device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse ? 1 : 2;
            if (_impulse[h] != 1) return;
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), seconds);
        }

        private static InputDevice First(InputDeviceCharacteristics wanted)
        {
            InputDevices.GetDevicesWithCharacteristics(wanted, Found);
            return Found.Count > 0 ? Found[0] : default(InputDevice);
        }

        private static string Describe(InputDevice device)
        {
            bool analog = device.TryGetFeatureValue(CommonUsages.grip, out float _);
            bool button = device.TryGetFeatureValue(CommonUsages.gripButton, out bool _);
            bool haptics = device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse;
            return "'" + device.name + "' grip(float) " + (analog ? "yes" : "NO") + ", gripButton " + (button ? "yes" : "NO") + ", haptic impulse " + (haptics ? "yes" : "NO");
        }
    }
}
