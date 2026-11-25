// iteration 11 - owner character (proposal-talents.md 4.2 / 6.1): WoW's TokenFrame (CharacterFrame's Currency tab).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// TokenFrame (Blizzard_TokenUI.xml / .lua): UI-Character-General-TopLeft / TopRight + UI-TalentFrame-BotLeft / BotRight, the list
    /// container 299x330 at (26, -77) with 295x17 rows 3 apart (TOKEN_BUTTON_OFFSET), category rows on UI-TokenFrame-CategoryButton with the
    /// UI-PlusMinus-Buttons expand icon, currency rows with the name (11 in), the count right-aligned 5 left of the 15x15 icon (20 from the
    /// right); Honor Points / Arena Points use WoW's PvP icons (TokenFrame_Update :99-109); the player's money (SmallMoneyFrame at
    /// BOTTOMRIGHT (-115, 84)) and the Close button. Categories in CurrencyCategory id order, names sorted (ours - GetCurrencyListInfo is C code).
    /// </summary>
    public sealed class UiCurrencyWindow : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Currency, Frame = "TokenFrame", TitleKey = "CURRENCY", Title = "Currency",   // GlobalStrings.lua:1894
                Area = UiPanelArea.Left, Pushable = 3, WhileDead = true, Binding = "TOGGLECURRENCY", Palm = false,
                OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",
                Owner = "character", Hidden = false,   // built (iteration 11, character); reached from the CharacterFrame tabs (no default key, as WoW)
                Build = (parent, ctx, close) => new UiCurrencyWindow(parent, ctx, close)
            };
        }

        const int Rows = 16;
        const float ListX = 26f, ListY = 77f, RowW = 295f, RowH = 17f, RowStep = 20f;   // container (26, -77); button (0, -2) then 3 apart

        sealed class Line
        {
            public bool Header;
            public int CategoryId;
            public string Text;
            public CurrencyView C;
        }

        sealed class Row
        {
            public UiButton B;
            public RawImage CatLeft, CatRight, Expand, Icon;
            public TextMeshProUGUI Name, Count, HeaderText;
            public Line L;
        }

        readonly UiCharacterShell _shell;
        readonly Row[] _rows = new Row[Rows];
        readonly List<Line> _lines = new List<Line>();
        readonly HashSet<int> _collapsed = new HashSet<int>();
        readonly RectTransform _list;
        readonly UiButton _up, _down;
        readonly TextMeshProUGUI _empty;
        readonly UiMoney _money;
        List<CurrencyView> _shownList;
        int _top, _shownTop = -1, _shownFaction = -1;
        bool _dirty = true;

        public UiCurrencyWindow(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            W = 384f; H = 512f;
            Root = UiKit.Rect(parent, "TokenFrame", 0f, 0f, W, H);
            _shell = new UiCharacterShell(Root, @"Interface\PaperDollInfoFrame\UI-Character-General-TopLeft.blp", @"Interface\PaperDollInfoFrame\UI-Character-General-TopRight.blp",
                                          @"Interface\TalentFrame\UI-TalentFrame-BotLeft.blp", @"Interface\TalentFrame\UI-TalentFrame-BotRight.blp", close);
            _list = UiKit.Rect(Root, "TokenFrameContainer", ListX, ListY, 299f, 330f);
            for (int i = 0; i < Rows; i++)
            {
                var r = new Row();
                r.B = UiKit.Button(Root, "TokenButton" + (i + 1), ListX, ListY + 2f + i * RowStep, RowW, RowH);
                // header: CategoryLeft 256x18 (0, 1, 0, 0.28125) up to CategoryRight 37x18 (0, 0.14453125, 0.296875, 0.578125) at the right
                r.CatLeft = UiKit.Pic(r.B.transform, "CategoryLeft", UiArt.Tex(@"Interface\TokenFrame\UI-TokenFrame-CategoryButton.blp"), 0f, 1f, 0f, 0.28125f, 0f, -0.5f, RowW - 37f, 18f);
                r.CatRight = UiKit.Pic(r.B.transform, "CategoryRight", UiArt.Tex(@"Interface\TokenFrame\UI-TokenFrame-CategoryButton.blp"), 0f, 0.14453125f, 0.296875f, 0.578125f, RowW - 37f, -0.5f, 37f, 18f);
                r.Expand = UiKit.Pic(r.B.transform, "ExpandIcon", UiArt.Tex(@"Interface\Buttons\UI-PlusMinus-Buttons.blp"), 0.5625f, 1f, 0f, 0.4375f, RowW - 8f - 7f, 5f, 7f, 7f);
                r.HeaderText = UiKit.Text(r.B.transform, "Text", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, RowW, RowH);   // NormalFont GameFontNormal, CENTER
                r.HeaderText.textWrappingMode = TextWrappingModes.NoWrap;
                r.Name = UiKit.Text(r.B.transform, "Name", "", 11f, Color.white, TextAlignmentOptions.MidlineLeft, 11f, 0f, 210f, RowH);   // GameFontHighlightLeft 210x11 at LEFT (11, 0)
                r.Name.textWrappingMode = TextWrappingModes.NoWrap;
                r.Name.overflowMode = TextOverflowModes.Ellipsis;
                r.Count = UiKit.Text(r.B.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.MidlineRight, RowW - 20f - 15f - 5f - 60f, 0f, 60f, RowH);
                r.Count.textWrappingMode = TextWrappingModes.NoWrap;
                r.Icon = UiKit.Pic(r.B.transform, "Icon", null, RowW - 20f - 15f, 1f, 15f, 15f);
                r.B.Hilite = UiKit.Pic(r.B.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, RowW, RowH);
                r.B.Hilite.enabled = false;
                int k = i;
                r.B.Click = () => RowClick(k);
                r.B.Tip = () => RowTip(k);
                r.B.gameObject.SetActive(false);
                _rows[i] = r;
            }
            _up = UiKit.ArtButton(Root, "ScrollUp", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 326f, 80f, 20f, () => Scroll(-3));
            _down = UiKit.ArtButton(Root, "ScrollDown", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 326f, 386f, 20f, () => Scroll(3));
            _empty = UiKit.Text(Root, "Empty", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 40f, 200f, 270f, 60f);
            _money = new UiMoney(Root, 384f - 115f, 512f - 84f - 13f, Color.white);
            UiKit.PanelButton(Root, UiGlobalStrings.G("CLOSE", "Close"), 305f - 40f, 421f - 11f, 80f, 22f, close);   // TokenFrameCancelButton CENTER (305, -421)
            UiCharacterTabs.Build(Root, ctx, UiWindowKind.Currency);
            Root.gameObject.SetActive(false);
        }

        void BuildLines(PlayerState p)
        {
            _lines.Clear();
            List<CurrencyView> all = p != null ? p.Currencies : null;
            if (all == null || all.Count == 0) return;
            var cats = new List<int>();
            for (int i = 0; i < all.Count; i++) if (all[i] != null && !cats.Contains(all[i].CategoryId)) cats.Add(all[i].CategoryId);
            cats.Sort();
            var items = new List<CurrencyView>();
            for (int c = 0; c < cats.Count; c++)
            {
                items.Clear();
                string catName = "";
                for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].CategoryId == cats[c]) { items.Add(all[i]); catName = all[i].CategoryName; }
                items.Sort((a, b) => string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase));
                _lines.Add(new Line { Header = true, CategoryId = cats[c], Text = catName });
                if (_collapsed.Contains(cats[c])) continue;
                for (int i = 0; i < items.Count; i++) _lines.Add(new Line { C = items[i], CategoryId = cats[c] });
            }
        }

        static string NameOf(CurrencyView c)
        {
            if (c == null) return "";
            if (c.Item != null && !string.IsNullOrEmpty(c.Item.Name)) return c.Item.Name;
            return c.ItemEntry == 43308 ? "Honor Points" : c.ItemEntry == 43307 ? "Arena Points" : "";   // HONOR_POINTS / ARENA_POINTS (GlobalStrings.lua:4189 / 563) until the item answer is in
        }

        void RowClick(int k)
        {
            Line l = _rows[k].L;
            if (l == null || !l.Header) return;
            if (!_collapsed.Remove(l.CategoryId)) _collapsed.Add(l.CategoryId);   // TokenButton_OnClick: ExpandCurrencyList
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            _dirty = true;
        }

        string RowTip(int k)
        {
            Line l = _rows[k].L;
            if (l == null || l.Header || l.C == null) return null;
            string extra = null;
            PlayerState p = Ctx.P;
            if (l.C.ItemEntry == 43308 && p != null)   // ours: the lifetime honorable kills under Honor Points (HONORABLE_KILLS, GlobalStrings.lua:4180)
                extra = "<color=#ffd100>" + UiGlobalStrings.G("HONORABLE_KILLS", "Honorable Kills") + ":</color> " + p.LifetimeHonorableKills;
            if (l.C.Item != null && !string.IsNullOrEmpty(l.C.Item.Name)) return UiTooltip.Item(l.C.Item, extra);
            return "<size=14>" + NameOf(l.C) + "</size>" + (extra != null ? "\n" + extra : "");
        }

        void Scroll(int by)
        {
            int t = Mathf.Clamp(_top + by, 0, Mathf.Max(0, _lines.Count - Rows));
            if (t == _top) return;
            _top = t;
            _dirty = true;
        }

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            _shell.Refresh(p);
            _money.Set(p.Money);
            float wheel = UiCharacterText.Wheel(_list, Ctx.Vr);
            if (wheel > 0.01f) Scroll(-1); else if (wheel < -0.01f) Scroll(1);
            if (!ReferenceEquals(p.Currencies, _shownList) || (int)p.Faction != _shownFaction) { _shownList = p.Currencies; _shownFaction = (int)p.Faction; _dirty = true; }
            // names / icons arrive with the server's item answers: redraw the shown rows (cheap) every frame
            if (_dirty || _shownTop != _top)
            {
                _dirty = false;
                BuildLines(p);
                _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _lines.Count - Rows));
                _shownTop = _top;
                bool scrolls = _lines.Count > Rows;
                _up.gameObject.SetActive(scrolls); _down.gameObject.SetActive(scrolls);
                _up.Interactable = _top > 0; _down.Interactable = _top < _lines.Count - Rows;
                _empty.text = !(Ctx.S is OnlineSession) ? UiCharacterText.OnlineOnly : "";
            }
            for (int i = 0; i < Rows; i++) Draw(_rows[i], _top + i < _lines.Count ? _lines[_top + i] : null, p);
        }

        void Draw(Row r, Line l, PlayerState p)
        {
            r.L = l;
            bool on = l != null;
            if (r.B.gameObject.activeSelf != on) r.B.gameObject.SetActive(on);
            if (!on) return;
            r.CatLeft.enabled = l.Header && r.CatLeft.texture != null;
            r.CatRight.enabled = l.Header && r.CatRight.texture != null;
            r.Expand.enabled = l.Header && r.Expand.texture != null;
            r.HeaderText.enabled = l.Header;
            r.Name.enabled = r.Count.enabled = !l.Header;
            if (l.Header)
            {
                bool collapsed = _collapsed.Contains(l.CategoryId);
                // expanded (0.5625, 1, 0, 0.4375) / collapsed (0, 0.4375, 0, 0.4375) (TokenFrame_Update :81-85)
                if (collapsed) UiKit.Coords(r.Expand, 0f, 0.4375f, 0f, 0.4375f); else UiKit.Coords(r.Expand, 0.5625f, 1f, 0f, 0.4375f);
                if (r.HeaderText.text != l.Text) r.HeaderText.text = l.Text ?? "";
                r.Icon.enabled = false;
                return;
            }
            string name = NameOf(l.C);
            if (r.Name.text != name) r.Name.text = name;
            string count = l.C.Count.ToString();
            if (r.Count.text != count) r.Count.text = count;
            if (l.C.ItemEntry == 43307)
            {
                r.Icon.texture = UiArt.Tex(@"Interface\PVPFrame\PVP-ArenaPoints-Icon.blp");
                UiKit.Coords(r.Icon, 0f, 1f, 0f, 1f);
            }
            else if (l.C.ItemEntry == 43308)
            {
                r.Icon.texture = UiArt.Tex(p.Faction == WowFaction.Horde ? @"Interface\TargetingFrame\UI-PVP-Horde.blp" : @"Interface\TargetingFrame\UI-PVP-Alliance.blp");
                UiKit.Coords(r.Icon, 0.03125f, 0.59375f, 0.03125f, 0.59375f);
            }
            else
            {
                Texture2D t = l.C.Item != null && !string.IsNullOrEmpty(l.C.Item.IconPath) ? UiArt.Icon(l.C.Item.IconPath) : null;   // null while it loads
                if (r.Icon.texture != t) { r.Icon.texture = t; UiKit.Coords(r.Icon, 0f, 1f, 0f, 1f); }
            }
            r.Icon.color = Color.white;
            r.Icon.enabled = r.Icon.texture != null;
        }

        protected override void OnOpen()
        {
            _dirty = true;
        }
    }
}
