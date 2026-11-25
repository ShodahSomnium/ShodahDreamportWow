using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// A WoW options check box on a world-space panel: FrameXML's check button art (Interface\Buttons\UI-CheckBox-Up / -Down /
    /// -Check, the -Highlight drawn additive through WowUi.AdditiveAsAlpha) with its label to the right. The whole row
    /// (box + label) takes the pointer, so a VR ray does not have to hit the small box.
    /// </summary>
    public sealed class WowCheckBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public const string Up = @"Interface\Buttons\UI-CheckBox-Up.blp";
        public const string Down = @"Interface\Buttons\UI-CheckBox-Down.blp";
        public const string Check = @"Interface\Buttons\UI-CheckBox-Check.blp";
        public const string HighlightPath = @"Interface\Buttons\UI-CheckBox-Highlight.blp";

        private RawImage _box, _check, _hover;
        private Texture2D _up, _down;
        private TextMeshProUGUI _label;
        private Action<bool> _changed;
        private bool _on, _hoverOn, _pressed, _interactable = true;
        private float _lastClick;

        public static WowCheckBox Create(Transform parent, string label, float x, float y, float w, float h, bool on, Action<bool> changed)
        {
            RectTransform r = WowUi.Place(parent, "Check " + label, x, y, w, h);
            var c = r.gameObject.AddComponent<WowCheckBox>();
            c.Build(label, w, h, on, changed);
            return c;
        }

        private void Build(string label, float w, float h, bool on, Action<bool> changed)
        {
            _changed = changed;
            _on = on;
            var catcher = gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            _up = WowUi.Tex(Up);
            _down = WowUi.Tex(Down);
            float s = h;
            _box = WowUi.Picture(transform, "Box", _up, new Rect(0f, 0f, 1f, 1f), 0f, 0f, s, s);
            if (_up == null) _box.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            Texture2D check = WowUi.Tex(Check);
            _check = WowUi.Picture(transform, "Check", check, new Rect(0f, 0f, 1f, 1f), 0f, 0f, s, s);
            if (check == null) _check.color = new Color(1f, 0.82f, 0f, 1f);
            Texture2D glow = WowUi.AdditiveAsAlpha(HighlightPath);
            _hover = WowUi.Picture(transform, "Highlight", glow, new Rect(0f, 0f, 1f, 1f), 0f, 0f, s, s);
            if (glow == null) _hover.color = new Color(0.3f, 0.5f, 1f, 0.35f);
            _label = WowUi.Text(transform, "Label", label, Mathf.Min(21f, h * 0.55f), Color.white, TextAlignmentOptions.Left, s + 8f, 0f, w - s - 8f, h);
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 12f;
            _label.fontSizeMax = Mathf.Min(21f, h * 0.55f);
            Refresh();
        }

        public bool Value
        {
            get { return _on; }
            set { _on = value; Refresh(); }
        }

        public bool Interactable
        {
            get { return _interactable; }
            set { _interactable = value; Refresh(); }
        }

        private void Refresh()
        {
            if (_box == null) return;
            _check.gameObject.SetActive(_on);
            _hover.gameObject.SetActive(_hoverOn && _interactable);
            if (_down != null && _up != null) _box.texture = _pressed && _interactable ? _down : _up;
            _label.color = _interactable ? Color.white : WowUi.Grey;
            _check.color = _interactable ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
        }

        public void OnPointerEnter(PointerEventData e) { _hoverOn = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hoverOn = false; _pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _pressed = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _pressed = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (!_interactable || Time.unscaledTime - _lastClick < 0.3f) return;
            _lastClick = Time.unscaledTime;
            _on = !_on;
            Refresh();

            try
            {
                if (_changed != null) _changed(_on);
            }
            catch (Exception ex)
            {
                WowLog.Error("UI: check box '" + (_label != null ? _label.text : "?") + "' failed: " + ex);
            }
        }
    }

    /// <summary>
    /// A WoW options slider on a world-space panel: the track is FrameXML's Interface\Buttons\UI-SliderBar-Background, the
    /// thumb Interface\Buttons\UI-SliderBar-Button-Horizontal (32x32, as OptionsSliderTemplate draws it); the label above
    /// left, the value above right. Pointer down / drag on the track sets the value (the VR ray's hit point on the canvas
    /// plane, else the screen point); "-" and "+" glue buttons step it, for a steady hand in a headset.
    /// </summary>
    public sealed class WowSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public const string Background = @"Interface\Buttons\UI-SliderBar-Background.blp";
        public const string Thumb = @"Interface\Buttons\UI-SliderBar-Button-Horizontal.blp";

        private RectTransform _track;
        private RawImage _thumb;
        private Image _fill;
        private TextMeshProUGUI _label, _valueText;
        private WowGlueButton _minus, _plus;
        private float _min, _max, _step, _value;
        private Func<float, string> _format;
        private Action<float> _changed;
        private bool _interactable = true;
        private float _trackWidth;

        public static WowSlider Create(Transform parent, string label, float x, float y, float w, float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
        {
            RectTransform r = WowUi.Place(parent, "Slider " + label, x, y, w, 50f);
            var s = r.gameObject.AddComponent<WowSlider>();
            s.Build(label, w, min, max, step, value, format, changed);
            return s;
        }

        private void Build(string label, float w, float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
        {
            _min = min;
            _max = max;
            _step = step;
            _format = format ?? (v => Mathf.RoundToInt(v * 100f) + "%");
            _changed = changed;
            _label = WowUi.Text(transform, "Label", label, 18f, Color.white, TextAlignmentOptions.Left, 0f, 0f, w - 90f, 22f);
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 12f;
            _label.fontSizeMax = 18f;
            _valueText = WowUi.Text(transform, "Value", "", 18f, WowUi.GlueGold, TextAlignmentOptions.Right, w - 90f, 0f, 90f, 22f);

            const float Btn = 40f;
            _minus = WowUi.Button(transform, "-", 0f, 20f, Btn, 30f, 22f, () => Step(-1));
            _plus = WowUi.Button(transform, "+", w - Btn, 20f, Btn, 30f, 22f, () => Step(1));
            _trackWidth = w - 2f * Btn - 28f;
            _track = WowUi.Place(transform, "Track", Btn + 14f, 25f, _trackWidth, 20f);
            // A bronze edge, a dark groove with the FrameXML background, a gold fill up to the value (the lab showed the plain
            // background alone is invisible on the black panel).
            var frame = _track.gameObject.AddComponent<Image>();
            frame.color = new Color(0.5f, 0.45f, 0.33f, 1f);
            frame.raycastTarget = true;
            RectTransform groove = WowUi.Place(_track, "Groove", 2f, 2f, _trackWidth - 4f, 16f);
            var g = groove.gameObject.AddComponent<Image>();
            g.color = new Color(0.06f, 0.06f, 0.06f, 1f);
            g.raycastTarget = false;
            Texture2D bg = WowUi.Tex(Background);
            if (bg != null) WowUi.Picture(_track, "Background", bg, new Rect(0f, 0f, 1f, 1f), 2f, 2f, _trackWidth - 4f, 16f);
            RectTransform fill = WowUi.Place(_track, "Fill", 2f, 2f, 0f, 16f);
            _fill = fill.gameObject.AddComponent<Image>();
            _fill.color = new Color(0.8f, 0.62f, 0.1f, 0.85f);
            _fill.raycastTarget = false;
            // The events land on the track (its Image); this component sits on the row, so the track forwards to it.
            var fwd = _track.gameObject.AddComponent<WowSliderTrack>();
            fwd.Owner = this;
            Texture2D thumb = WowUi.Tex(Thumb);
            _thumb = WowUi.Picture(_track, "Thumb", thumb, new Rect(0f, 0f, 1f, 1f), 0f, -12f, 44f, 44f);
            if (thumb == null) { _thumb.color = new Color(0.8f, 0.8f, 0.8f, 1f); _thumb.rectTransform.sizeDelta = new Vector2(10f, 24f); }
            SetValue(value, false);
        }

        public float Value { get { return _value; } }

        public bool Interactable
        {
            get { return _interactable; }
            set
            {
                _interactable = value;
                if (_minus != null) _minus.Interactable = value;
                if (_plus != null) _plus.Interactable = value;
                if (_label != null) _label.color = value ? Color.white : WowUi.Grey;
            }
        }

        /// <summary>Sets the shown value (clamped and snapped to the step); notify = call the changed callback.</summary>
        public void SetValue(float v, bool notify)
        {
            if (float.IsNaN(v)) return;
            float s = _step > 0f ? _min + Mathf.Round((v - _min) / _step) * _step : v;
            s = Mathf.Clamp(s, _min, _max);
            bool changed = Mathf.Abs(s - _value) > 1e-5f;
            _value = s;
            float t = _max > _min ? (s - _min) / (_max - _min) : 0f;
            if (_thumb != null) _thumb.rectTransform.anchoredPosition = new Vector2(t * _trackWidth - _thumb.rectTransform.sizeDelta.x / 2f, _thumb.rectTransform.anchoredPosition.y);
            if (_fill != null) _fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, t * (_trackWidth - 4f)), 16f);
            if (_valueText != null) _valueText.text = _format(s);

            if (notify && changed && _changed != null)
            {
                try { _changed(s); }
                catch (Exception ex) { WowLog.Error("UI: slider '" + (_label != null ? _label.text : "?") + "' failed: " + ex); }
            }
        }

        private void Step(int dir)
        {
            if (!_interactable) return;
            SetValue(_value + dir * (_step > 0f ? _step : (_max - _min) / 20f), true);
        }

        internal void Pointer(PointerEventData e)
        {
            if (!_interactable || e == null || _track == null) return;
            Vector2 local;
            bool ok = false;
            local = Vector2.zero;

            // A VR ray (and uGUI's GraphicRaycaster on a world-space canvas) reports where it hit the canvas plane.
            if (e.pointerCurrentRaycast.gameObject != null && e.pointerCurrentRaycast.worldPosition != Vector3.zero)
            {
                local = _track.InverseTransformPoint(e.pointerCurrentRaycast.worldPosition);
                ok = true;
            }
            else
            {
                Camera cam = e.pressEventCamera != null ? e.pressEventCamera : e.enterEventCamera;
                ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, e.position, cam, out local);
            }

            if (!ok) return;
            Rect r = _track.rect;
            float t = r.width > 0f ? Mathf.Clamp01((local.x - r.xMin) / r.width) : 0f;
            SetValue(_min + t * (_max - _min), true);
        }

        public void OnPointerDown(PointerEventData e) { Pointer(e); }
        public void OnDrag(PointerEventData e) { Pointer(e); }
        public void OnPointerUp(PointerEventData e) { }
    }

    /// <summary>Forwards pointer down / drag on a slider's track to its WowSlider.</summary>
    public sealed class WowSliderTrack : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public WowSlider Owner;

        public void OnPointerDown(PointerEventData e) { if (Owner != null) Owner.Pointer(e); }
        public void OnDrag(PointerEventData e) { if (Owner != null) Owner.Pointer(e); }
    }

    /// <summary>A row of glue buttons of which one is selected (difficulty, effects quality, voice).</summary>
    public sealed class WowChoice
    {
        private readonly WowGlueButton[] _buttons;
        private readonly Action<int> _changed;
        private int _value;

        public WowChoice(Transform parent, string label, string[] options, float x, float y, float w, int value, Action<int> changed)
        {
            _changed = changed;
            _value = value;
            TextMeshProUGUI l = WowUi.Text(parent, "Choice " + label, label, 18f, Color.white, TextAlignmentOptions.Left, x, y, w, 22f);
            l.enableAutoSizing = true;
            l.fontSizeMin = 12f;
            l.fontSizeMax = 18f;
            _buttons = new WowGlueButton[options.Length];
            float bw = (w - (options.Length - 1) * 6f) / options.Length;

            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                _buttons[i] = WowUi.Button(parent, options[i], x + i * (bw + 6f), y + 22f, bw, 32f, 17f, () => Pick(k));
            }

            Refresh();
        }

        public int Value
        {
            get { return _value; }
            set { _value = value; Refresh(); }
        }

        private void Pick(int k)
        {
            if (k == _value) return;
            _value = k;
            Refresh();

            try { if (_changed != null) _changed(k); }
            catch (Exception ex) { WowLog.Error("UI: choice failed: " + ex); }
        }

        private void Refresh()
        {
            for (int i = 0; i < _buttons.Length; i++) if (_buttons[i] != null) _buttons[i].Selected = i == _value;
        }
    }
}
