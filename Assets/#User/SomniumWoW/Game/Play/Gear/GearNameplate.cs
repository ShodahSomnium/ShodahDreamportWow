using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>The viewer's camera, re-found twice a second (Camera.main is not reliable in the client: EtsVr/AomView lesson).</summary>
    public static class GearEye
    {
        private static Camera _eye;
        private static float _next;

        public static Camera Get()
        {
            if (_eye != null && _eye.isActiveAndEnabled && Time.unscaledTime < _next) return _eye;
            _next = Time.unscaledTime + 0.5f;
            bool xr = XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName);
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled && main.targetTexture == null && (!xr || main.stereoTargetEye != StereoTargetEyeMask.None)) return _eye = main;
            Camera best = null;
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || !c.isActiveAndEnabled || c.targetTexture != null) continue;
                if (xr && c.stereoTargetEye == StereoTargetEyeMask.None) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return _eye = best != null ? best : main;
        }
    }

    /// <summary>
    /// "Name - Level N Class" over another player's avatar, in WoW's class colour (RAID_CLASS_COLORS, Interface\FrameXML\
    /// Constants.lua lines 54-65), with their health percent under it. Built as the ETS nameplate is: a world-space Canvas with
    /// TextMeshProUGUI on the UI layer (5), no raycaster and raycastTarget off (it never takes the client's pointer), not
    /// parented to the avatar, turned to the viewer's camera each LateUpdate and scaled with distance.
    /// </summary>
    public sealed class GearNameplate
    {
        private const float UnitsToMetres = 0.0025f, NameSize = 40f, HealthSize = 28f;
        private const float NearDistance = 8f, MaxScale = 3f, HideBeyond = 60f, HideNearer = 0.6f;

        private readonly GameObject _go;
        private readonly Transform _t;
        private readonly Canvas _canvas;
        private readonly TextMeshProUGUI _name, _health;
        private string _nameSaid, _healthSaid;
        private float _scale = -1f;

        public GearNameplate(Transform parent)
        {
            _go = new GameObject("Gear Nameplate", typeof(RectTransform));
            _go.layer = Ui.UiKit.Layer;
            Ui.UiSharp.Register(_go);   // lead 2026-10-10
            _t = _go.transform;
            _t.SetParent(parent, false);
            _canvas = _go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)_t;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(900f, 90f);
            _name = Label("name", NameSize, 50f);
            _health = Label("health", HealthSize, 14f);
            _canvas.enabled = false;
        }

        private TextMeshProUGUI Label(string name, float size, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = Ui.UiKit.Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(_t, false);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(900f, size * 1.3f);
            var text = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = WowUi.Font;
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false; // a player's name is text, never markup
            text.raycastTarget = false;
            return text;
        }

        public static Color ClassColour(int cls)
        {
            switch ((WowClass)cls)
            {
                case WowClass.Hunter: return new Color(0.67f, 0.83f, 0.45f);
                case WowClass.Warlock: return new Color(0.58f, 0.51f, 0.79f);
                case WowClass.Priest: return new Color(1f, 1f, 1f);
                case WowClass.Paladin: return new Color(0.96f, 0.55f, 0.73f);
                case WowClass.Mage: return new Color(0.41f, 0.8f, 0.94f);
                case WowClass.Rogue: return new Color(1f, 0.96f, 0.41f);
                case WowClass.Druid: return new Color(1f, 0.49f, 0.04f);
                case WowClass.Shaman: return new Color(0f, 0.44f, 0.87f);
                case WowClass.Warrior: return new Color(0.78f, 0.61f, 0.43f);
                case WowClass.DeathKnight: return new Color(0.77f, 0.12f, 0.23f);
                default: return new Color(1f, 0.82f, 0f);
            }
        }

        public void Set(string line, Color colour, int healthPercent, bool dead)
        {
            if (line != _nameSaid) { _nameSaid = line; _name.text = line; }
            _name.color = colour;
            string h = dead ? "dead" : healthPercent + "%";
            if (h != _healthSaid)
            {
                _healthSaid = h;
                _health.text = h;
                _health.color = dead ? new Color(0.6f, 0.6f, 0.6f) : Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 1f, 0.3f), healthPercent / 100f);
            }
        }

        /// <summary>Over the head, square to the viewer, scaled with distance; hidden when too near or too far.</summary>
        public void Place(Vector3 at, bool visible)
        {
            Camera eye = visible ? GearEye.Get() : null;
            if (eye == null) { if (_canvas.enabled) _canvas.enabled = false; return; }
            _t.position = at;
            Vector3 d = at - eye.transform.position;
            float distance = d.magnitude;
            bool show = distance >= HideNearer && distance < HideBeyond;
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;
            if (_canvas.worldCamera != eye) _canvas.worldCamera = eye;
            if (d.x * d.x + d.z * d.z > 1e-4f * distance * distance) _t.rotation = Quaternion.LookRotation(d / distance, Vector3.up);
            float scale = UnitsToMetres * (distance <= NearDistance ? 1f : Mathf.Min(MaxScale, distance / NearDistance));
            if (scale != _scale) { _scale = scale; _t.localScale = new Vector3(scale, scale, scale); }
        }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }
    }
}
