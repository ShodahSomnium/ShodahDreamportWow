// iteration 11 - owner character (proposal-talents.md 6.1): UiCharacterTabs (the CharacterFrame tab strip the five character-frame windows
// share), the ReputationFrame row widget, a small check box and the CharacterFrame panel shell used by Reputation and Currency.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// CharacterFrame's tabs (CharacterFrame.xml:77-150, CharacterFrameTabButtonTemplate in CharacterFrameTemplates.xml): Character / Pets (our
    /// Mounts pane) / Reputation / Skills (shown when area trainer's Skills window is registered and not hidden) / Currency (shown when its
    /// window is built and the player knows a currency, like TokenFrame's tab). First at BOTTOMLEFT (11, 46), each next 15 units into the
    /// previous; the shown window's tab is the "active" art 5 units higher. A click closes this window and opens the other one (the panel
    /// manager puts it on the same spot) with igCharacterInfoTab (CharacterFrame.lua:47).
    /// </summary>
    public static class UiCharacterTabs
    {
        public const string ActiveTab = @"Interface\PaperDollInfoFrame\UI-Character-ActiveTab.blp";
        public const string InactiveTab = @"Interface\PaperDollInfoFrame\UI-Character-InActiveTab.blp";
        public const string TabHighlight = @"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp";
        static bool _logged;

        static readonly UiWindowKind[] Kinds = { UiWindowKind.Character, UiWindowKind.Mounts, UiWindowKind.Reputation, UiWindowKind.Skills, UiWindowKind.Currency };
        // CHARACTER (GlobalStrings.lua:1372), PETS (:5600), REPUTATION_ABBR (:6071), SKILLS_ABBR (:6332), CURRENCY (:1894)
        static readonly string[] Keys = { "CHARACTER", "PETS", "REPUTATION_ABBR", "SKILLS_ABBR", "CURRENCY" };
        static readonly string[] Fallback = { "Character", "Pets", "Reputation", "Skills", "Currency" };

        /// <summary>Builds the five CharacterFrame tabs at the bottom of root (BOTTOMLEFT (11, 46), each next -15 to the right); current = the
        /// window's own kind (Character, Mounts, Reputation, Skills or Currency).</summary>
        public static void Build(RectTransform root, UiContext ctx, UiWindowKind current)
        {
            if (root == null || ctx == null) return;
            RectTransform strip = UiKit.Rect(root, "CharacterFrameTabs", 0f, 0f, 384f, 512f);
            var tabs = new List<Tab>();
            for (int i = 0; i < Kinds.Length; i++)
            {
                UiWindowKind k = Kinds[i];
                bool active = k == current;
                string label = UiGlobalStrings.G(Keys[i], Fallback[i]);
                var t = new Tab { Kind = k };
                t.B = UiKit.Button(strip, "Tab " + Fallback[i], 0f, 0f, 100f, 32f);
                t.Left = UiKit.Pic(t.B.transform, "Left", UiArt.Tex(active ? ActiveTab : InactiveTab), 0f, 0.15625f, 0f, 1f, 0f, active ? -5f : 0f, 20f, 32f);
                t.Middle = UiKit.Pic(t.B.transform, "Middle", UiArt.Tex(active ? ActiveTab : InactiveTab), 0.15625f, 0.84375f, 0f, 1f, 20f, active ? -5f : 0f, 60f, 32f);
                t.Right = UiKit.Pic(t.B.transform, "Right", UiArt.Tex(active ? ActiveTab : InactiveTab), 0.84375f, 1f, 0f, 1f, 80f, active ? -5f : 0f, 20f, 32f);
                if (!active)
                {
                    t.B.Hilite = UiKit.Pic(t.B.transform, "Highlight", UiArt.Add(TabHighlight), 10f, 2f, 80f, 32f);
                    t.B.Hilite.enabled = false;
                }
                // GameFontNormalSmall (gold) / the selected tab is disabled = GameFontHighlightSmall (white), text CENTER (0, 2) = 2 units up
                t.B.Label = UiKit.Text(t.B.transform, "Text", label, 10f, active ? Color.white : UiArt.Gold, TextAlignmentOptions.Center, 0f, active ? -5f : -2f, 100f, 32f);
                t.B.Label.textWrappingMode = TextWrappingModes.NoWrap;
                t.B.LabelColor = active ? Color.white : UiArt.Gold;
                // PanelTemplates_TabResize(tab, 0): text width + the two 20-unit sides
                t.W = Mathf.Ceil(t.B.Label.GetPreferredValues(label).x) + 40f;
                UiWindowKind target = k;
                if (!active) t.B.Click = () => Switch(ctx, current, target);
                t.B.Tip = () => "<size=14>" + label + "</size>";
                tabs.Add(t);
            }
            var s = strip.gameObject.AddComponent<UiCharacterTabStrip>();
            s.Tabs = tabs;
            s.Ctx = ctx;
            s.Layout();
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI: CharacterFrame tabs (Character / Pets / Reputation / Skills / Currency) - Skills shows when its window is registered, Currency when a currency is known");
            }
        }

        static void Switch(UiContext ctx, UiWindowKind from, UiWindowKind to)
        {
            if (ctx.Toggle == null) return;
            UiSound.Named("igCharacterInfoTab");
            if (ctx.IsOpen == null || ctx.IsOpen(from)) ctx.Toggle(from);
            if (ctx.IsOpen == null || !ctx.IsOpen(to)) ctx.Toggle(to);
        }

        /// <summary>Is this tab offered now? (the registry spec exists, has a window and is not hidden; Currency also needs a known currency).</summary>
        internal static bool Offered(UiWindowKind k, UiContext ctx)
        {
            if (k == UiWindowKind.Character || k == UiWindowKind.Mounts || k == UiWindowKind.Reputation) return true;
            UiWindowSpec spec = UiRegistry.Of(k);
            if (spec == null || spec.Hidden || spec.Build == null) return false;
            if (k == UiWindowKind.Currency)
            {
                PlayerState p = ctx != null ? ctx.P : null;
                return p != null && p.Currencies != null && p.Currencies.Count > 0;
            }
            return true;
        }

        internal sealed class Tab
        {
            public UiWindowKind Kind;
            public UiButton B;
            public RawImage Left, Middle, Right;
            public float W;
        }
    }

    /// <summary>Re-lays the tab strip each time its window opens (a registry spec or the known currencies may have changed).</summary>
    public sealed class UiCharacterTabStrip : MonoBehaviour
    {
        internal List<UiCharacterTabs.Tab> Tabs;
        internal UiContext Ctx;

        void OnEnable() { Layout(); }

        internal void Layout()
        {
            if (Tabs == null) return;
            float x = 11f;
            for (int i = 0; i < Tabs.Count; i++)
            {
                UiCharacterTabs.Tab t = Tabs[i];
                bool on = UiCharacterTabs.Offered(t.Kind, Ctx);
                if (t.B.gameObject.activeSelf != on) t.B.gameObject.SetActive(on);
                if (!on) continue;
                UiKit.Move(t.B.Rt, x, 512f - 46f - 32f);
                UiKit.Size(t.B.Rt, t.W, 32f);
                UiKit.Size(t.Middle.rectTransform, t.W - 40f, 32f);
                UiKit.Move(t.Right.rectTransform, t.W - 20f, -t.Right.rectTransform.anchoredPosition.y);
                UiKit.Size(t.B.Label.rectTransform, t.W, 32f);
                if (t.B.Hilite != null) UiKit.Size(t.B.Hilite.rectTransform, t.W - 20f, 32f);
                x += t.W - 15f;
            }
        }
    }

    /// <summary>
    /// The CharacterFrame panel shell the Reputation and Currency windows share: their FrameXML art pieces, the class portrait (60x60 at
    /// (7, 6) like UiCharacter), the character's name as CharacterFrame's title, the close button.
    /// </summary>
    internal sealed class UiCharacterShell
    {
        public RawImage Portrait;
        public TextMeshProUGUI Name;
        WowClass _cls = (WowClass)255;
        string _name;

        public UiCharacterShell(RectTransform root, string tl, string tr, string bl, string br, Action close)
        {
            // ReputationFrame.xml:238-283 / Blizzard_TokenUI.xml:160-205: TopLeft (2,-1) 256x256, TopRight (258,-1) 128x256, bottom pieces 256 lower
            UiKit.Pic(root, "TopLeft", UiArt.Tex(tl), 2f, 1f, 256f, 256f);
            UiKit.Pic(root, "TopRight", UiArt.Tex(tr), 258f, 1f, 128f, 256f);
            UiKit.Pic(root, "BottomLeft", UiArt.Tex(bl), 2f, 257f, 256f, 256f);
            UiKit.Pic(root, "BottomRight", UiArt.Tex(br), 258f, 257f, 128f, 256f);
            Portrait = UiKit.Pic(root, "Portrait", UiArt.Tex(UiArt.ClassCircles), 7f, 6f, 60f, 60f);
            Name = UiKit.Text(root, "Name", "", 13f, Color.white, TextAlignmentOptions.Center, 78f, 16f, 240f, 17f);
            Name.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.CloseButton(root, 323f, 7f, close);
        }

        public void Refresh(PlayerState p)
        {
            if (p == null) return;
            if (p.Class != _cls)
            {
                _cls = p.Class;
                Vector4 c = UiArt.ClassCoords(p.Class);
                UiKit.Coords(Portrait, c.x, c.y, c.z, c.w);
            }
            if (!string.Equals(p.Name, _name)) { _name = p.Name; Name.text = p.Name ?? ""; }
        }
    }

    /// <summary>A check box (OptionsSmallCheckButtonTemplate / ReputationDetailAtWarCheckBox: 26x26 UI-CheckBox-Up / -Down / -Highlight /
    /// the check texture) with its label to the right (2 units into the box, ReputationFrame.xml:655-660).</summary>
    internal sealed class UiRepCheck
    {
        public readonly UiButton B;
        public readonly TextMeshProUGUI Label;
        readonly RawImage _check;
        bool _on, _enabled = true;
        public float LabelRight { get { return 24f + Label.GetPreferredValues(Label.text).x; } }

        public UiRepCheck(Transform parent, string name, string text, string checkTex, float checkSize, float checkX, float checkY, float x, float y, Color labelColor, Action click)
        {
            B = UiKit.Button(parent, name, x, y, 26f, 26f);
            B.Art = UiKit.Pic(B.transform, "Up", UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Up.blp"), 0f, 0f, 26f, 26f);
            B.ArtUp = UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Up.blp");
            B.Pushed = UiKit.Pic(B.transform, "Down", UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Down.blp"), 0f, 0f, 26f, 26f);
            B.Pushed.enabled = false;
            B.Hilite = UiKit.Pic(B.transform, "Highlight", UiArt.Add(@"Interface\Buttons\UI-CheckBox-Highlight.blp"), 0f, 0f, 26f, 26f);
            B.Hilite.enabled = false;
            _check = UiKit.Pic(B.transform, "Check", UiArt.Tex(checkTex), checkX, checkY, checkSize, checkSize);
            _check.enabled = false;
            Label = UiKit.Text(B.transform, "Text", text, 10f, labelColor, TextAlignmentOptions.MidlineLeft, 24f, 0f, 160f, 26f);
            Label.textWrappingMode = TextWrappingModes.NoWrap;
            B.Click = click;
        }

        public void Set(bool on, bool enabled, Color labelColor)
        {
            if (_on != on) { _on = on; _check.enabled = on && _check.texture != null; }
            if (_enabled != enabled) { _enabled = enabled; B.Interactable = enabled; }
            if (Label.color != labelColor) Label.color = labelColor;
            if (_check.enabled) _check.color = enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        }
    }

    /// <summary>
    /// One ReputationBarTemplate row (ReputationFrame.xml:4-230, ReputationFrame_SetRowType ReputationFrame.lua:44-121): the row stays 20 tall
    /// with its right edge at x 316 (ReputationBar1 TOPRIGHT (-68, -83)); its left edge moves by type: faction 44, child 62, header 20,
    /// header + child 39. Factions: title (GameFontHighlightSmall) 10 in, the UI-Character-ReputationBar background up to the bar, the bar
    /// 101 wide with its 62 + 42 frame; headers: the +/- button 3 in, the title (GameFontNormalLeft) 10 right of it, the bar 99 wide (60 + 39
    /// frame) only when the header has its own reputation. Bar colours FACTION_BAR_COLORS (ReputationFrame.lua:3-12).
    /// </summary>
    internal sealed class UiRepRow
    {
        public const float Right = 316f, Height = 20f;
        const string RepBar = @"Interface\PaperDollInfoFrame\UI-Character-ReputationBar.blp";
        const string RepBarHighlight = @"Interface\PaperDollInfoFrame\UI-Character-ReputationBar-Highlight.blp";
        const string SkillsBar = @"Interface\PaperDollInfoFrame\UI-Character-Skills-Bar.blp";
        static readonly Color[] BarColors =
        {
            new Color(0.8f, 0.3f, 0.22f), new Color(0.8f, 0.3f, 0.22f), new Color(0.75f, 0.27f, 0f), new Color(0.9f, 0.7f, 0f),
            new Color(0f, 0.6f, 0.1f), new Color(0f, 0.6f, 0.1f), new Color(0f, 0.6f, 0.1f), new Color(0f, 0.6f, 0.1f)
        };

        public readonly UiButton B;          // the whole row (click = select / collapse)
        public readonly UiButton Toggle;     // the +/- button of a header
        readonly RawImage _plusMinus, _background, _barLeft, _barRight, _fill, _hi1, _hi2, _war;
        readonly TextMeshProUGUI _title, _standing;
        readonly RectTransform _bar;
        public FactionView View;
        public int Index;
        int _type = -1;
        string _standingText, _valueText;

        public UiRepRow(Transform parent, int i, float y)
        {
            B = UiKit.Button(parent, "ReputationBar" + (i + 1), 44f, y, Right - 44f, Height);
            _background = UiKit.Pic(B.transform, "Background", UiArt.Tex(RepBar), 0f, 0.7578125f, 0f, 0.328125f, 0f, -0.5f, 10f, 21f);
            Toggle = UiKit.Button(B.transform, "ExpandOrCollapseButton", 3f, 2f, 16f, 16f);
            _plusMinus = UiKit.Pic(Toggle.transform, "Normal", UiArt.Tex(@"Interface\Buttons\UI-MinusButton-Up.blp"), 0f, 0f, 16f, 16f);
            Toggle.Hilite = UiKit.Pic(Toggle.transform, "Highlight", UiArt.Add(@"Interface\Buttons\UI-PlusButton-Hilight.blp"), 0f, 0f, 16f, 16f);
            Toggle.Hilite.enabled = false;
            _title = UiKit.Text(B.transform, "FactionName", "", 10f, Color.white, TextAlignmentOptions.MidlineLeft, 10f, 0f, 160f, Height);
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _bar = UiKit.Rect(B.transform, "ReputationBar", 0f, 3.5f, 101f, 13f);
            _fill = UiKit.Pic(_bar, "Fill", UiArt.Tex(SkillsBar), 0f, 0f, 101f, 13f);
            _war = UiKit.Solid(_bar, "AtWarHighlight", new Color(0.8f, 0.1f, 0.1f, 0.2f), -1f, -2f, 103f, 17f);   // AtWarHighlight1/2 (ADD, alpha 0.2) - ours: a red wash
            _barLeft = UiKit.Pic(_bar, "LeftTexture", UiArt.Tex(RepBar), 0.691f, 1f, 0.047f, 0.281f, 0f, -4f, 62f, 21f);
            _barRight = UiKit.Pic(_bar, "RightTexture", UiArt.Tex(RepBar), 0f, 0.164f, 0.3906f, 0.625f, 62f, -4f, 42f, 21f);
            _standing = UiKit.Text(_bar, "FactionStanding", "", 10f, Color.white, TextAlignmentOptions.Center, 0f, 0f, 101f, 13f);
            _standing.textWrappingMode = TextWrappingModes.NoWrap;
            _hi1 = UiKit.Pic(B.transform, "Highlight1", UiArt.Add(RepBarHighlight), 0f, 1f, 0f, 0.4375f, -2f, -4f, 10f, 28f);
            _hi2 = UiKit.Pic(B.transform, "Highlight2", UiArt.Add(RepBarHighlight), 0f, 0.06640625f, 0.4375f, 0.875f, 0f, -4f, 17f, 28f);
            _hi1.enabled = _hi2.enabled = false;
            Index = i;
        }

        /// <summary>rowType = isChild + 2 * isHeader (ReputationFrame.lua:44).</summary>
        public void Show(FactionView v, bool collapsed, bool selected, bool hovered)
        {
            View = v;
            if (!B.gameObject.activeSelf) B.gameObject.SetActive(true);
            int type = (v.IsChild ? 1 : 0) + (v.IsHeader ? 2 : 0);
            if (type != _type)
            {
                _type = type;
                float left = type == 0 ? 44f : type == 1 ? 62f : type == 2 ? 20f : 39f;
                float w = Right - left;
                UiKit.Move(B.Rt, left, -B.Rt.anchoredPosition.y);
                UiKit.Size(B.Rt, w, Height);
                bool header = type >= 2;
                float barW = header ? 99f : 101f;
                UiKit.Move(_bar, w - barW, 3.5f);
                UiKit.Size(_bar, barW, 13f);
                UiKit.Size(_fill.rectTransform, barW, 13f);
                UiKit.Size(_standing.rectTransform, barW, 13f);
                Toggle.gameObject.SetActive(header);
                _background.enabled = !header && _background.texture != null;
                UiKit.Size(_background.rectTransform, Mathf.Max(1f, w - barW), 21f);
                // header frame: 60 x 15 + 39 x 15 (0.765625, 1.0, 0.046875, 0.28125) / (0.0, 0.15234375, 0.390625, 0.625)
                if (header)
                {
                    UiKit.Coords(_barLeft, 0.765625f, 1f, 0.046875f, 0.28125f); UiKit.Move(_barLeft.rectTransform, 0f, -1f); UiKit.Size(_barLeft.rectTransform, 60f, 15f);
                    UiKit.Coords(_barRight, 0f, 0.15234375f, 0.390625f, 0.625f); UiKit.Move(_barRight.rectTransform, 60f, -1f); UiKit.Size(_barRight.rectTransform, 39f, 15f);
                }
                else
                {
                    UiKit.Coords(_barLeft, 0.7578125f, 1f, 0f, 0.328125f); UiKit.Move(_barLeft.rectTransform, 0f, -4f); UiKit.Size(_barLeft.rectTransform, 62f, 21f);
                    UiKit.Coords(_barRight, 0f, 0.1640625f, 0.34375f, 0.671875f); UiKit.Move(_barRight.rectTransform, 62f, -4f); UiKit.Size(_barRight.rectTransform, 42f, 21f);
                }
                float titleX = header ? 3f + 13f + 10f : 10f;
                UiKit.Move(_title.rectTransform, titleX, 0f);
                UiKit.Size(_title.rectTransform, type == 0 ? 160f : type == 1 ? 150f : type == 2 ? 145f : 135f, Height);
                _title.fontSize = header ? 12f : 10f;
                UiKit.Size(_hi1.rectTransform, Mathf.Max(1f, w - 8f), 28f);
                UiKit.Move(_hi2.rectTransform, w - 17f, -4f);
            }
            bool headerRow = type >= 2;
            _title.color = headerRow ? UiArt.Gold : Color.white;   // GameFontNormalLeft / GameFontHighlightSmall
            if (_title.text != v.Name) _title.text = v.Name ?? "";
            _plusMinus.texture = UiArt.Tex(collapsed ? @"Interface\Buttons\UI-PlusButton-Up.blp" : @"Interface\Buttons\UI-MinusButton-Up.blp");
            bool bar = (v.HasRep || !headerRow) && v.RepListId >= 0;
            if (_bar.gameObject.activeSelf != bar) _bar.gameObject.SetActive(bar);
            if (bar)
            {
                int max = Mathf.Max(1, v.RankMax - v.RankMin), value = Mathf.Clamp(v.Standing - v.RankMin, 0, max);
                float frac = value / (float)max;
                float bw = headerRow ? 99f : 101f;
                _fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.01f, bw * frac), 13f);
                UiKit.Coords(_fill, 0f, frac, 0f, 1f);
                _fill.enabled = frac > 0.001f;
                Color c = BarColors[Mathf.Clamp(v.Rank, 0, 7)];
                _fill.color = c;
                _standingText = UiCharacterText.Standing(v.Rank);
                _valueText = value + " / " + max;   // HIGHLIGHT_FONT_COLOR " %d / %d" (ReputationFrame.lua:156)
                string shown = hovered ? _valueText : _standingText;
                if (_standing.text != shown) _standing.text = shown;
                _war.enabled = v.AtWar;
            }
            _hi1.enabled = _hi2.enabled = selected || hovered;
        }

        public void Hide()
        {
            View = null;
            if (B.gameObject.activeSelf) B.gameObject.SetActive(false);
        }
    }

    /// <summary>WoW texts of the character panes, through the install's GlobalStrings (uicore's UiGlobalStrings) with the enUS fallback quoted.</summary>
    internal static class UiCharacterText
    {
        static readonly string[] StandingKeys = { "FACTION_STANDING_LABEL1", "FACTION_STANDING_LABEL2", "FACTION_STANDING_LABEL3", "FACTION_STANDING_LABEL4",
                                                  "FACTION_STANDING_LABEL5", "FACTION_STANDING_LABEL6", "FACTION_STANDING_LABEL7", "FACTION_STANDING_LABEL8" };
        static readonly string[] StandingEn = { "Hated", "Hostile", "Unfriendly", "Neutral", "Friendly", "Honored", "Revered", "Exalted" };   // GlobalStrings.lua:3651-3665

        public static string Standing(int rank)
        {
            rank = Mathf.Clamp(rank, 0, 7);
            return UiGlobalStrings.G(StandingKeys[rank], StandingEn[rank]);
        }

        /// <summary>Ours (offline the realm's panes are empty): one line in the window.</summary>
        public const string OnlineOnly = "Online only - play on your realm.";

        /// <summary>Desktop: the mouse wheel over r (Input.mouseScrollDelta like UiChat), 0 in VR or elsewhere.</summary>
        public static float Wheel(RectTransform r, bool vr)
        {
            if (vr || r == null) return 0f;
            try
            {
                if (!r.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(r, Input.mousePosition, null)) return 0f;
                return Input.mouseScrollDelta.y;
            }
            catch (Exception) { return 0f; }
        }
    }
}
