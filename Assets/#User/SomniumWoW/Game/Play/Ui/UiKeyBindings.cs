// iteration 11 - owner uicore (proposal-uicore.md 5.4.7): the Key Bindings window - a READ-ONLY list of every desktop key of the Dreamport
// (Pure/UiKeyMap: ours, the other areas' keys, with WoW's binding name from the install's GlobalStrings BINDING_NAME_* when it exists)
// and the Somnium client's own keys marked "(Somnium)" (they cannot be changed here). Rebinding is gap G6. Ours: 384 x 512, centred
// (WoW's KeyBindingFrame was not dumped), pages of 20 rows with the spellbook's page arrows.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiKeyBindings : UiWindow
    {
        private const int Rows = 20;
        private readonly TextMeshProUGUI[] _action = new TextMeshProUGUI[Rows], _key = new TextMeshProUGUI[Rows];
        private readonly TextMeshProUGUI _page;
        private readonly UiButton _prev, _next;
        private readonly List<string[]> _lines = new List<string[]>();
        private int _at, _builtFor = -1;

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                // proposal-uicore.md 5.4.7: the read-only list of the Dreamport desktop keys (Game Menu > Key Bindings).
                Kind = UiWindowKind.KeyBindings, Frame = "KeyBindingFrame", TitleKey = "KEY_BINDINGS", Title = "Key Bindings",   // GlobalStrings.lua:4571
                Area = UiPanelArea.Center, WhileDead = true, Palm = false, OpenSound = "igMainMenuOpen", CloseSound = "igMainMenuClose",
                Owner = "uicore", Hidden = false,
                Build = (parent, ctx, close) => new UiKeyBindings(parent, ctx, close)
            };
        }

        public UiKeyBindings(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "KeyBindingFrame", 0f, 0f, W, H);
            UiBackdrop back = UiBackdrop.Make(Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            back.Place(0f, 0f, W, H);
            UiKit.Pic(Root, "Header", UiArt.Tex(@"Interface\DialogFrame\UI-DialogBox-Header.blp"), (W - 256f) * 0.5f, -12f, 256f, 64f);
            TextMeshProUGUI title = UiKit.Text(Root, "Title", UiGlobalStrings.G("KEY_BINDINGS", "Key Bindings"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, -2f, W, 18f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.CloseButton(Root, W - 36f, 6f, close);
            UiKit.Text(Root, "Note", "Read-only: the keys of this world (Somnium's own keys are marked).", 10f, UiArt.Grey, TextAlignmentOptions.Center, 20f, 30f, W - 40f, 14f);   // ours
            for (int i = 0; i < Rows; i++)
            {
                float y = 52f + i * 20f;
                _action[i] = UiKit.Text(Root, "Action " + i, "", 11f, Color.white, TextAlignmentOptions.MidlineLeft, 22f, y, 240f, 18f);
                _action[i].textWrappingMode = TextWrappingModes.NoWrap;
                _action[i].overflowMode = TextOverflowModes.Ellipsis;
                _key[i] = UiKit.Text(Root, "Key " + i, "", 11f, UiArt.Gold, TextAlignmentOptions.MidlineRight, 262f, y, 100f, 18f);
                _key[i].textWrappingMode = TextWrappingModes.NoWrap;
            }
            _prev = UiKit.ArtButton(Root, "Prev", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, null, 22f, H - 50f, 32f, () => Turn(-1));
            _next = UiKit.ArtButton(Root, "Next", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, null, W - 54f, H - 50f, 32f, () => Turn(1));
            _page = UiKit.Text(Root, "Page", "", 11f, UiArt.Gold, TextAlignmentOptions.Center, 60f, H - 44f, W - 120f, 18f);
            Root.gameObject.SetActive(false);
        }

        private void Turn(int d)
        {
            int pages = Math.Max(1, (_lines.Count + Rows - 1) / Rows);
            _at = Mathf.Clamp(_at + d, 0, pages - 1);
            Show();
        }

        protected override void OnOpen()
        {
            if (_builtFor != (Pure.UiGlobalStrings.Loaded ? 1 : 0)) BuildLines();
            Show();
        }

        private void BuildLines()
        {
            _builtFor = UiGlobalStrings.Loaded ? 1 : 0;
            _lines.Clear();
            for (int i = 0; i < UiKeyMap.Entries.Count; i++)
            {
                UiKeyMap.Entry e = UiKeyMap.Entries[i];
                string name = e.Does;
                if (!string.IsNullOrEmpty(e.Binding) && e.Binding.IndexOf(' ') < 0 && e.Binding != "ours")
                    name = UiGlobalStrings.G("BINDING_NAME_" + e.Binding, e.Does);
                _lines.Add(new[] { name, e.Chord });
            }
            foreach (KeyValuePair<string, string> c in UiKeyMap.Client)
                _lines.Add(new[] { "<color=#9d9d9d>" + c.Value + " (Somnium)</color>", c.Key });
        }

        private void Show()
        {
            int pages = Math.Max(1, (_lines.Count + Rows - 1) / Rows);
            _at = Mathf.Clamp(_at, 0, pages - 1);
            for (int i = 0; i < Rows; i++)
            {
                int k = _at * Rows + i;
                bool has = k < _lines.Count;
                _action[i].text = has ? _lines[k][0] : "";
                _key[i].text = has ? _lines[k][1] : "";
            }
            _page.SetText("{0} / {1}", _at + 1, pages);
            _prev.Interactable = _at > 0;
            _next.Interactable = _at < pages - 1;
        }

        public override void Refresh(float now) { }
    }

    /// <summary>The Help micro button (MicroOrder 9) - HelpFrame is not offered (proposal-uicore.md gap G1): a spec without a window, Hidden
    /// (PLAN11 s1.8: no button that does nothing).</summary>
    public static class UiHelpStub
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Help, Frame = "HelpFrame", TitleKey = "HELP_BUTTON", Title = "Help Request",   // GlobalStrings.lua:4136
                Area = UiPanelArea.Center, WhileDead = true, Micro = "Help", MicroOrder = 9, Palm = false, Owner = "uicore", Hidden = true
            };
        }
    }
}
