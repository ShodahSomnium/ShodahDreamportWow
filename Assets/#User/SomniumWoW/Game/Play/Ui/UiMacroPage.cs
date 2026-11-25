// Lead 2026-10-12 (the user: "Please add macros in the settings, so players can use them in game."): the Settings window's Macros page -
// ours, after WoW's MacroFrame (MacroFrame.xml: 18 buttons in rows of 6, the name, the 255-letter text, New / Delete, the button picked up
// onto the cursor to drop on a bar). VR: a poke on a text box opens the headset keyboard (UiTrade's path); "New line" adds a line break.
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiMacroPage
    {
        private const float X0 = 30f, Y0 = 90f, Cell = 34f, Gap = 8f, W = 324f;
        private readonly UiContext _ctx;
        private readonly Transform _keyboardParent;
        private readonly UiButton[] _slot = new UiButton[UiMacros.MaxMacros];
        private readonly RawImage[] _sel = new RawImage[UiMacros.MaxMacros];
        private readonly string[] _iconShown = new string[UiMacros.MaxMacros];
        private readonly TMP_InputField _name, _body;
        private readonly UiButton _nameVr, _bodyVr;
        private WowKeyboard _keyboard;
        private int _id = 1, _shownId = -1;
        private bool _dirty;
        private float _dirtyAt;
        private bool _filling;

        public UiMacroPage(RectTransform page, UiContext ctx, Transform keyboardParent)
        {
            _ctx = ctx;
            _keyboardParent = keyboardParent;
            for (int i = 0; i < UiMacros.MaxMacros; i++)
            {
                int id = i + 1;
                float x = X0 + (i % 6) * (Cell + Gap) + 10f, y = Y0 + (i / 6) * (Cell + Gap);
                UiButton b = UiKit.Slot(page, "Macro " + id, x, y, Cell, true);
                _sel[i] = UiKit.Pic(b.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, Cell, Cell);
                _sel[i].enabled = false;
                b.Click = () => Select(id);
                b.Tip = () => UiMacros.Tooltip(UiMacros.Get(id));
                _slot[i] = b;
            }
            float y1 = Y0 + 3 * (Cell + Gap) + 4f;
            UiKit.Text(page, "Name label", "Name", 12f, UiArt.Gold, TextAlignmentOptions.Left, X0, y1 + 2f, 50f, 16f);
            _name = WowTextField.Create(page, "Macro name", X0 + 50f, y1, W - 50f, 20f, 11f, "Macro name", false, UiMacros.MaxName);
            _name.onValueChanged.AddListener(_ => Edited());
            _nameVr = UiKit.Button(page, "Macro name (VR)", X0 + 50f, y1, W - 50f, 20f);
            _nameVr.Click = () => OpenKeyboard(_name, "Macro name");
            float y2 = y1 + 28f;
            _body = WowTextField.Create(page, "Macro text", X0, y2, W, 140f, 11f, "/cast Fireball", false, UiMacros.MaxBody);
            _body.lineType = TMP_InputField.LineType.MultiLineNewline;
            if (_body.textComponent != null)
            {
                _body.textComponent.textWrappingMode = TextWrappingModes.Normal;
                _body.textComponent.alignment = TextAlignmentOptions.TopLeft;
            }
            if (_body.placeholder is TextMeshProUGUI ph) ph.alignment = TextAlignmentOptions.TopLeft;
            _body.onValueChanged.AddListener(_ => Edited());
            _bodyVr = UiKit.Button(page, "Macro text (VR)", X0, y2, W, 140f);
            _bodyVr.Click = () => OpenKeyboard(_body, "Macro text");
            float y3 = y2 + 148f, bw = (W - 18f) / 4f;
            UiButton nw = UiKit.PanelButton(page, "New", X0, y3, bw, 22f, NewMacro);
            nw.Tip = () => "<size=14>New</size>\nA new macro in the first free button.";
            UiButton del = UiKit.PanelButton(page, "Delete", X0 + (bw + 6f), y3, bw, 22f, Delete);
            del.Tip = () => "<size=14>Delete</size>\nDeletes the selected macro (its bar buttons stay, empty).";
            UiButton nl = UiKit.PanelButton(page, "New line", X0 + 2f * (bw + 6f), y3, bw, 22f, NewLine);
            nl.Tip = () => "<size=14>New line</size>\nStarts a new line in the macro text (the headset keyboard has no line break).";
            UiButton pick = UiKit.PanelButton(page, "Put on bar", X0 + 3f * (bw + 6f), y3, bw, 22f, PickUp);
            pick.Tip = () => "<size=14>Put on bar</size>\nPicks the macro up: click an action button (VR: grip a spell menu and release on a button) to place it.\n"
                + "Lines: /cast Name (Rank n), /use Item, /target Name, /cleartarget, /startattack, /stopattack, /stopcasting, any chat command; # lines are skipped.";
            UiMacros.Hook();
        }

        private bool Vr { get { return _ctx != null && _ctx.Vr; } }

        private void Select(int id)
        {
            Flush();
            _id = id;
            _shownId = -1;
            UiSound.Play(WowUiSound.ButtonClick);
        }

        private void NewMacro()
        {
            Flush();
            int id = UiMacros.FreeId();
            if (id == 0) { Say("All 18 macro buttons are used."); return; }   // ours
            UiMacros.Set(id, new UiMacros.Macro { Name = "Macro " + id, Body = "" });
            _id = id;
            _shownId = -1;
            UiSound.Play(WowUiSound.ButtonClick);
        }

        private void Delete()
        {
            if (UiMacros.Get(_id) == null) return;
            UiMacros.Set(_id, null);
            _dirty = false;
            _shownId = -1;
            UiSound.Play(WowUiSound.ButtonClick);
        }

        private void NewLine()
        {
            if (_body == null) return;
            _body.text = (_body.text ?? "") + "\n";
            Edited();
        }

        private void PickUp()
        {
            Flush();
            UiMacros.Macro m = UiMacros.Get(_id);
            if (m == null) { Say("Write a macro first."); return; }   // ours
            PlayerState p = _ctx != null && _ctx.S != null ? _ctx.S.Player : null;
            UiActionCursor.Pick(UiMacros.Packed(_id), m.Name, UiMacros.IconOf(p, m), -1, false);
            UiSound.Play(WowUiSound.ButtonClick);
        }

        private void OpenKeyboard(TMP_InputField f, string title)
        {
            if (!Vr || f == null) return;
            if (_keyboard == null) _keyboard = WowKeyboard.Create(_keyboardParent);
            _keyboard.Open(f, title, false, Flush);
        }

        private void Edited()
        {
            if (_filling) return;
            _dirty = true;
            _dirtyAt = Time.unscaledTime;
        }

        /// <summary>The fields into the selected macro (a macro is made when an empty button gets text).</summary>
        private void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            string name = (_name.text ?? "").Trim(), body = (_body.text ?? "").Replace("\r", "");
            if (name.Length == 0 && body.Trim().Length == 0 && UiMacros.Get(_id) == null) return;
            UiMacros.Set(_id, new UiMacros.Macro { Name = name.Length > 0 ? name : "Macro " + _id, Body = body });
        }

        public void Refresh(float now)
        {
            if (_dirty && now - _dirtyAt > 0.75f) Flush();
            if (_nameVr.gameObject.activeSelf != Vr) _nameVr.gameObject.SetActive(Vr);
            if (_bodyVr.gameObject.activeSelf != Vr) _bodyVr.gameObject.SetActive(Vr);
            if (_shownId != _id)
            {
                _shownId = _id;
                UiMacros.Macro m = UiMacros.Get(_id);
                _filling = true;
                _name.text = m != null ? m.Name : "";
                _body.text = m != null ? m.Body : "";
                _filling = false;
            }
            PlayerState p = _ctx != null && _ctx.S != null ? _ctx.S.Player : null;
            for (int i = 0; i < UiMacros.MaxMacros; i++)
            {
                UiMacros.Macro m = UiMacros.Get(i + 1);
                bool sel = i + 1 == _id;
                if (_sel[i].enabled != sel) _sel[i].enabled = sel;
                string path = m != null ? UiMacros.IconOf(p, m) : null;
                if (path == _iconShown[i] && (path == null || _slot[i].Icon.enabled)) continue;
                if (path == null) { _iconShown[i] = null; _slot[i].Icon.enabled = false; continue; }
                Texture2D t = UiArt.Icon(path);
                if (t == null) continue;
                _iconShown[i] = path;
                _slot[i].Icon.texture = t;
                _slot[i].Icon.color = Color.white;   // UiKit.Slot makes the icon clear (lead fix 2026-10-11)
                _slot[i].Icon.enabled = true;
            }
        }

        public void Closed() { Flush(); }

        private void Say(string text)
        {
            if (_ctx != null) _ctx.Say(text, UiArt.InfoYellow);
        }
    }
}
