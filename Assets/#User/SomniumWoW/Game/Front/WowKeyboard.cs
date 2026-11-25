using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// A WoW-styled single-line TMP input field for the room panels (dark box, gold caret, WoW's font). Desktop players type
    /// into it; in a headset selecting it opens WowKeyboard. Password fields mask with '*'. Nothing here ever logs the text.
    /// </summary>
    public static class WowTextField
    {
        public static TMP_InputField Create(Transform parent, string name, float x, float y, float w, float h, float size, string placeholder, bool password, int limit)
        {
            RectTransform r = WowUi.Place(parent, "Field " + name, x, y, w, h);
            GameObject go = r.gameObject;
            go.SetActive(false);   // configure before TMP_InputField's OnEnable runs
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.72f);
            bg.raycastTarget = true;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.45f, 0.2f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            // lead fix 2026-10-10 (the user: "Auction House ... you can only see 1 digit in the input field"): the side padding was 10 on each
            // side whatever the width - a 24-wide money box kept 4 units of text room. Now 12 % of the width, 2 .. 10 (ours).
            float pad = Mathf.Clamp(w * 0.12f, 2f, 10f);
            RectTransform area = WowUi.Place(r, "Text Area", pad, 2f, w - 2f * pad, h - 4f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = WowUi.Text(area, "Text", "", size, Color.white, TextAlignmentOptions.Left, 0f, 0f, w - 2f * pad, h - 4f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            TextMeshProUGUI ph = WowUi.Text(area, "Placeholder", placeholder ?? "", size * 0.85f, new Color(0.5f, 0.5f, 0.5f, 1f), TextAlignmentOptions.Left, 0f, 0f, w - 2f * pad, h - 4f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.textWrappingMode = TextWrappingModes.NoWrap;
            ph.fontStyle = FontStyles.Italic;

            var f = go.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.placeholder = ph;
            f.targetGraphic = bg;
            f.fontAsset = WowUi.Font;
            f.pointSize = size;
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.characterLimit = limit;
            f.richText = false;
            f.customCaretColor = true;
            f.caretColor = WowUi.GlueGold;
            f.caretWidth = 3;
            f.selectionColor = new Color(0.9f, 0.7f, 0.1f, 0.35f);
            f.onFocusSelectAll = false;
            f.shouldHideSoftKeyboard = true;
            if (password)
            {
                f.contentType = TMP_InputField.ContentType.Password;
                f.asteriskChar = '*';
            }
            else
            {
                f.contentType = TMP_InputField.ContentType.Standard;
            }

            go.SetActive(true);
            return f;
        }

        /// <summary>True while the player types into one of our fields on desktop (focused TMP/uGUI input field).</summary>
        public static bool Typing()
        {
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return false;
            var tmp = sel.GetComponent<TMP_InputField>();
            return tmp != null && tmp.isFocused;
        }
    }

    /// <summary>
    /// The headset's on-screen keyboard: a WoW backdrop with glue-button keys (digits, letters, a symbols page, shift, space,
    /// backspace, enter, hide) that the client's ray or a poke presses (standard uGUI pointer events through the canvas's
    /// GraphicRaycaster + TrackedDeviceGraphicRaycaster, like every room panel). It edits the selected TMP input field's text
    /// directly, so it keeps working when the field loses the EventSystem selection. Opened 0.45 m in front of the head and
    /// 0.38 m below it, tilted 35 degrees like a desk keyboard; a preview line shows what is typed (masked for passwords).
    /// Keys are never logged (only "opened for Account" / "closed").
    /// </summary>
    public sealed class WowKeyboard : MonoBehaviour
    {
        public const float Width = 820f, Height = 430f, MetresPerUnit = 0.001f;
        private const float Key = 60f, Gap = 4f, Left = 32f, Top = 84f;

        private static readonly string[] Letters = { "1234567890", "qwertyuiop-", "asdfghjkl.", "zxcvbnm@_" };
        private static readonly string[] Symbols = { "1234567890", "!#$%^&*()+=", "[]{};'\",<>", "?/\\|~`:@_" };

        private Canvas _canvas;
        private TextMeshProUGUI _preview;
        private readonly List<WowKeyButton> _charKeys = new List<WowKeyButton>();
        private WowKeyButton _shiftKey, _symbolKey;
        private TMP_InputField _target;
        private string _title = "";
        private bool _masked, _shift, _symbols;
        private Action _onEnter;
        private Vector3 _openedAt;

        public bool IsOpen { get { return _canvas != null && _canvas.gameObject.activeSelf && _target != null; } }

        public TMP_InputField Target { get { return _target; } }

        public static WowKeyboard Create(Transform parent)
        {
            var go = new GameObject("Wow Keyboard");
            go.transform.SetParent(parent, false);
            return go.AddComponent<WowKeyboard>();
        }

        /// <summary>Opens the keyboard for a field (title = the field's label; masked = password); onEnter runs on the Enter key.</summary>
        public void Open(TMP_InputField field, string title, bool masked, Action onEnter)
        {
            if (field == null) return;
            if (_canvas == null) Build();
            bool wasOpen = IsOpen;
            _target = field;
            _title = title ?? "";
            _masked = masked;
            _onEnter = onEnter;
            _canvas.gameObject.SetActive(true);
            if (!wasOpen) Place();
            Refresh();
            WowLog.Info("Keyboard: opened for " + _title + (wasOpen ? " (switched field)" : "") + ".");
        }

        public void Close()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) { _target = null; return; }
            _canvas.gameObject.SetActive(false);
            _target = null;
            _onEnter = null;
            WowLog.Info("Keyboard: closed.");
        }

        private void Place()
        {
            Camera head = WowPlayer.Head();
            Transform t = _canvas.transform;

            if (head != null)
            {
                Vector3 f = head.transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                t.position = head.transform.position + f * 0.45f + Vector3.down * 0.38f;
                t.rotation = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(35f, 0f, 0f);
            }
            else
            {
                t.localPosition = new Vector3(0f, 1.0f, 1.2f);
                t.localRotation = Quaternion.Euler(35f, 0f, 0f);
            }

            _openedAt = t.position;
        }

        private void Build()
        {
            _canvas = WowUi.MakeCanvas("Wow Keyboard Canvas", transform, Width, Height, MetresPerUnit);
            Transform root = _canvas.transform;
            // Integrator (iteration 6 lab): WoW's dialog background tile is see-through, and the keyboard floats in front of the room
            // panel, whose status line then ran through the preview line - an opaque plate behind the backdrop.
            RawImage plate = WowUi.Picture(root, "Plate", null, new Rect(0f, 0f, 1f, 1f), 6f, 6f, Width - 12f, Height - 12f);
            plate.color = new Color(0.02f, 0.02f, 0.025f, 1f);
            WowUi.Backdrop(root, 0f, 0f, Width, Height, 28f);
            _preview = WowUi.Text(root, "Preview", "", 26f, Color.white, TextAlignmentOptions.Left, 30f, 22f, Width - 60f, 50f);
            _preview.alignment = TextAlignmentOptions.MidlineLeft;
            _preview.textWrappingMode = TextWrappingModes.NoWrap;
            _preview.overflowMode = TextOverflowModes.Ellipsis;

            // Rows 0-3: character keys (+ Backspace on row 0, Enter on row 2, Shift before row 3).
            for (int row = 0; row < 4; row++)
            {
                float x = Left + (row == 2 ? 10f : 0f);
                float y = Top + row * (Key + Gap + 2f);

                if (row == 3)
                {
                    _shiftKey = WowKeyButton.Create(root, "Shift", x, y, 110f, Key, 20f, ToggleShift);
                    x += 110f + Gap;
                }

                for (int i = 0; i < Letters[row].Length; i++)
                {
                    int r = row, k = i;
                    WowKeyButton b = WowKeyButton.Create(root, Letters[row][i].ToString(), x, y, Key, Key, 26f, () => TypeKey(r, k));
                    b.Row = row;
                    b.Index = i;
                    _charKeys.Add(b);
                    x += Key + Gap;
                }

                if (row == 0) WowKeyButton.Create(root, "Back", x, y, 110f, Key, 20f, Backspace);
                if (row == 2) WowKeyButton.Create(root, "Enter", x, y, 100f, Key, 20f, Enter);
            }

            float y4 = Top + 4 * (Key + Gap + 2f);
            _symbolKey = WowKeyButton.Create(root, "#+=", Left, y4, 110f, Key, 20f, ToggleSymbols);
            WowKeyButton.Create(root, "Space", Left + 114f, y4, 420f, Key, 20f, () => Type(" "));
            WowKeyButton.Create(root, "Hide", Left + 538f, y4, 200f, Key, 20f, Close);
            _canvas.gameObject.SetActive(false);
        }

        private void TypeKey(int row, int index)
        {
            string set = (_symbols ? Symbols : Letters)[row];
            if (index < 0 || index >= set.Length) return;
            char c = set[index];
            if (_shift && c >= 'a' && c <= 'z') c = char.ToUpperInvariant(c);
            Type(c.ToString());

            if (_shift)
            {
                _shift = false;   // one-shot, like a phone keyboard
                RefreshKeys();
            }
        }

        private void Type(string s)
        {
            if (_target == null) return;
            string t = _target.text ?? "";
            if (_target.characterLimit > 0 && t.Length + s.Length > _target.characterLimit) return;
            _target.text = t + s;
            Refresh();
        }

        private void Backspace()
        {
            if (_target == null) return;
            string t = _target.text ?? "";
            if (t.Length > 0) _target.text = t.Substring(0, t.Length - 1);
            Refresh();
        }

        private void Enter()
        {
            Action a = _onEnter;
            Close();

            try
            {
                if (a != null) a();
            }
            catch (Exception e)
            {
                WowLog.Error("Keyboard: the Enter action failed: " + e);
            }
        }

        private void ToggleShift()
        {
            _shift = !_shift;
            RefreshKeys();
        }

        private void ToggleSymbols()
        {
            _symbols = !_symbols;
            _shift = false;
            RefreshKeys();
        }

        private void RefreshKeys()
        {
            foreach (WowKeyButton b in _charKeys)
            {
                string set = (_symbols ? Symbols : Letters)[b.Row];
                char c = b.Index < set.Length ? set[b.Index] : ' ';
                if (_shift && c >= 'a' && c <= 'z') c = char.ToUpperInvariant(c);
                b.SetLabel(c.ToString());
            }

            if (_shiftKey != null) _shiftKey.Selected = _shift;
            if (_symbolKey != null) { _symbolKey.Selected = _symbols; _symbolKey.SetLabel(_symbols ? "abc" : "#+="); }
        }

        private void Refresh()
        {
            if (_preview == null) return;
            string t = _target != null ? (_target.text ?? "") : "";
            _preview.text = _title + ":  " + (_masked ? new string('*', t.Length) : t) + "_";
            RefreshKeys();
        }

        private void LateUpdate()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;

            if (_target == null || !_target.gameObject.activeInHierarchy)
            {
                Close();
                return;
            }

            _canvas.worldCamera = WowPlayer.Head();
            Camera head = WowPlayer.Head();
            if (head != null && (head.transform.position - _openedAt).magnitude > 1.6f) Place();   // walked away: bring it back
        }
    }

    /// <summary>One key: glue-button art, hover glow, pressed art; a short repeat guard; never logs its label.</summary>
    public sealed class WowKeyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public int Row, Index;
        private RawImage _art, _glow;
        private TextMeshProUGUI _label;
        private Action _onClick;
        private bool _hover, _down, _selected;
        private float _lastClick;

        public static WowKeyButton Create(Transform parent, string label, float x, float y, float w, float h, float textSize, Action onClick)
        {
            RectTransform r = WowUi.Place(parent, "Key", x, y, w, h);
            var k = r.gameObject.AddComponent<WowKeyButton>();
            k._onClick = onClick;
            var hit = r.gameObject.AddComponent<Image>();
            hit.color = WowUi.HasGameArt ? new Color(0f, 0f, 0f, 0f) : new Color(0.25f, 0.05f, 0.03f, 0.9f);
            hit.raycastTarget = true;
            k._art = WowUi.Picture(r, "Art", WowUi.Tex(WowUi.ButtonUp), WowUi.ButtonCoords, 0f, 0f, w, h);
            k._glow = WowUi.Picture(r, "Highlight", WowUi.Highlight(), WowUi.HighlightCoords, 0f, 0f, w, h);
            k._glow.gameObject.SetActive(false);
            k._label = WowUi.Text(r, "Label", label, textSize, WowUi.GlueGold, TextAlignmentOptions.Center, 2f, 0f, w - 4f, h);
            k._label.textWrappingMode = TextWrappingModes.NoWrap;
            k._label.alignment = TextAlignmentOptions.Center;
            k.Refresh();
            return k;
        }

        public bool Selected { get { return _selected; } set { _selected = value; Refresh(); } }

        public void SetLabel(string text)
        {
            if (_label != null && _label.text != text) _label.text = text;
        }

        private void Refresh()
        {
            if (_art == null) return;
            string path = _selected ? (_down ? WowUi.ButtonDownBlue : WowUi.ButtonUpBlue) : (_down ? WowUi.ButtonDown : WowUi.ButtonUp);
            Texture2D t = WowUi.Tex(path);
            _art.texture = t;
            _art.color = t == null ? new Color(0f, 0f, 0f, 0f) : Color.white;
            _glow.gameObject.SetActive(_hover && _glow.texture != null);
            _label.color = _hover || _selected ? Color.white : WowUi.GlueGold;
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (Time.unscaledTime - _lastClick < 0.08f) return;
            _lastClick = Time.unscaledTime;

            try
            {
                if (_onClick != null) _onClick();
            }
            catch (Exception ex)
            {
                WowLog.Error("Keyboard: a key failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
