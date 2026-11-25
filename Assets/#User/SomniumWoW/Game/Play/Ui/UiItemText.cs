// iteration 11 - owner npcui (proposal-npc.md 4 / 6.1 / H7; the wave-0 stub by w0game, filled by npcui)
// ItemTextFrame (ItemTextFrame.xml / .lua of the user's install, harness UiDump into P/harness/out_d11npc; file:line per value): a
// readable item, a book / plaque object or a letter - the title, the page text on the material's art in the material's text colour
// (MATERIAL_TEXT_COLOR_TABLE, Constants.lua:37-44), the page number and the Prev / Next page buttons. The text scrolls with the arrows.
using System;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiItemText : UiWindow
    {
        private const float ClipX = 38f, ClipY = 76f, ClipW = 280f, ClipH = 355f;   // ItemTextScrollFrame 280x355 TOPRIGHT (-66,-76)
        private const float TextY = 15f, TextW = 270f;                             // ItemTextPageText 270x304 at (0,-15)
        private const float ScrollStep = 80f;                                      // ours (as UiQuestDialog)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.ItemText, Frame = "ItemTextFrame", Area = UiPanelArea.Left, Pushable = 0,   // UIPanelWindows (UIParent.lua:21)
                Width = 384f, Height = 512f, WhileDead = true,                     // ours: reading needs no life (proposal-npc.md H7)
                SessionWindow = true, Palm = false,
                Wanted = s => s.CurrentItemText != null,
                NearGuid = s => 0UL,                                                // VR: in front of the player (a book is read where you stand)
                Closed = s => s.CloseItemText(),
                OpenSound = "igMainMenuOpen", CloseSound = "igMainMenuClose",       // ItemTextFrame.xml:336 / :340
                Owner = "npcui", Hidden = false,
                Build = (parent, ctx, close) => new UiItemText(parent, ctx, close)
            };
        }

        private static bool _logged;
        private readonly RawImage[] _material = new RawImage[4];
        private readonly TextMeshProUGUI _title, _page, _text, _prevText, _nextText;
        private readonly RectTransform _scroll;
        private readonly UiButton _prev, _next, _up, _down;
        private readonly Action _close;
        private int _version = -1;
        private string _materialShown;
        private float _offset, _contentH;
        private bool _layoutPending;

        public UiItemText(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            W = 384f;
            H = 512f;
            Root = UiKit.Rect(parent, "ItemTextFrame", 0f, 0f, W, H);
            UiKit.Blocker(Root);                                                                                      // enableMouse
            UiKit.Pic(Root, "Icon", UiArt.Tex(UiArt.SpellbookIcon), 10f, 8f, 58f, 58f);                              // Spellbook-Icon 58x58 (10,-8)
            UiKit.Pic(Root, "TopLeft", UiArt.Tex(@"Interface\ItemTextFrame\UI-ItemText-TopLeft.blp"), 0f, 0f, 256f, 256f);
            UiKit.Pic(Root, "TopRight", UiArt.Tex(UiArt.SpellbookPanel + "TopRight.blp"), 256f, 0f, 128f, 256f);
            UiKit.Pic(Root, "BotLeft", UiArt.Tex(@"Interface\ItemTextFrame\UI-ItemText-BotLeft.blp"), 0f, 256f, 256f, 256f);
            UiKit.Pic(Root, "BotRight", UiArt.Tex(UiArt.SpellbookPanel + "BotRight.blp"), 256f, 256f, 128f, 256f);
            // ItemTextMaterialTopLeft 256x256 at (21,-75), + TopRight 64x256, BotLeft 256x128, BotRight 64x128
            _material[0] = UiKit.Pic(Root, "ItemTextMaterialTopLeft", null, 21f, 75f, 256f, 256f);
            _material[1] = UiKit.Pic(Root, "ItemTextMaterialTopRight", null, 277f, 75f, 64f, 256f);
            _material[2] = UiKit.Pic(Root, "ItemTextMaterialBotLeft", null, 21f, 331f, 256f, 128f);
            _material[3] = UiKit.Pic(Root, "ItemTextMaterialBotRight", null, 277f, 331f, 64f, 128f);
            for (int i = 0; i < 4; i++) _material[i].enabled = false;

            _title = UiKit.Text(Root, "ItemTextTitleText", "", 13f, UiArt.Gold, TextAlignmentOptions.Center, 86f, 19f, 224f, 14f);   // CENTER (6,230) 224x14
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _page = UiKit.Text(Root, "ItemTextCurrentPage", "", 13f, UiArt.Gold, TextAlignmentOptions.Top, 106f, 50f, 192f, 16f);    // TOP (10,-50) 192 wide

            RectTransform clip = UiKit.Rect(Root, "ItemTextScrollFrame", ClipX, ClipY, ClipW, ClipH);
            clip.gameObject.AddComponent<RectMask2D>();
            _scroll = UiKit.Rect(clip, "ItemTextPageScrollChild", 0f, 0f, ClipW, ClipH);
            _text = UiKit.Text(_scroll, "ItemTextPageText", "", 15f, UiArt.QuestTitle, TextAlignmentOptions.TopLeft, 0f, TextY, TextW, 304f, UiArt.Morpheus, false);   // ItemTextFontNormal
            _up = UiKit.ArtButton(Root, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 322f, 78f, 20f, () => ScrollBy(-ScrollStep));      // ours: the scroll bar's arrows
            _down = UiKit.ArtButton(Root, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 322f, 409f, 20f, () => ScrollBy(ScrollStep));

            // ItemTextPrevPageButton CENTER at TOPLEFT (90,-56), Next CENTER at TOPRIGHT (-55,-56), Close CENTER at TOPRIGHT (-45,-26)
            _prev = UiKit.ArtButton(Root, "ItemTextPrevPageButton", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.MouseHilight, 74f, 40f, 32f, () => Turn(-1));
            _next = UiKit.ArtButton(Root, "ItemTextNextPageButton", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.MouseHilight, 313f, 40f, 32f, () => Turn(1));
            _prevText = UiKit.Text(Root, "Prev", UiGlobalStrings.G("PREV", "Prev"), 13f, UiArt.Gold, TextAlignmentOptions.Left, 106f, 48f, 50f, 16f);    // GlobalStrings.lua:5717
            _nextText = UiKit.Text(Root, "Next", UiGlobalStrings.G("NEXT", "Next"), 13f, UiArt.Gold, TextAlignmentOptions.Right, 263f, 48f, 50f, 16f);   // :5178
            UiKit.CloseButton(Root, 323f, 10f, () => { if (_close != null) _close(); });

            Root.gameObject.SetActive(false);
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI item text (npcui): WoW's ItemTextFrame (ItemTextFrame.xml/.lua); ours: the scroll arrows (80 units a step), the page's HTML " +
                            "tags reduced to plain lines (H1/H2 larger), shown while dead.");
            }
        }

        protected override void OnOpen()
        {
            _version = -1;
            _offset = 0f;
        }

        public override void Refresh(float now)
        {
            IWowSession s = Ctx.S;
            ItemTextView v = s != null ? s.CurrentItemText : null;
            if (v == null) return;
            if (v.Version != _version)
            {
                _version = v.Version;
                Show(v);
            }
            else if (_layoutPending) Layout();
        }

        /// <summary>ITEM_TEXT_BEGIN / ITEM_TEXT_READY (ItemTextFrame.lua:10-88).</summary>
        private void Show(ItemTextView v)
        {
            _title.SetText(v.Title ?? "");
            string material = string.IsNullOrEmpty(v.Material) ? "Parchment" : v.Material;
            _text.color = TextColor(material);                                         // GetMaterialTextColors (UIParent.lua:2810-2818)
            if (!string.Equals(material, _materialShown))
            {
                _materialShown = material;
                bool art = material != "Parchment";
                string[] part = { "TopLeft", "TopRight", "BotLeft", "BotRight" };
                for (int i = 0; i < 4; i++)
                {
                    Texture2D t = art ? UiArt.Tex(@"Interface\ItemTextFrame\ItemText-" + material + "-" + part[i] + ".blp") : null;   // :52-62
                    _material[i].texture = t;
                    _material[i].enabled = t != null;
                }
            }
            bool loading = v.Text == null;
            _text.SetText(loading ? "" : "\n" + Clean(v.Text) + "\n\n");               // "\n"..text.."\n\n" (:41-45)
            bool paged = !loading && (v.Page > 1 || v.HasNext);
            _page.SetText(paged ? v.Page.ToString() : "");
            SetActive(_prev, paged && v.Page > 1);
            SetActive(_next, paged && v.HasNext);
            _prevText.enabled = _prev.gameObject.activeSelf;
            _nextText.enabled = _next.gameObject.activeSelf;
            _offset = 0f;                                                                // ItemTextScrollFrameScrollBar:SetValue(0)
            Layout();
        }

        private void Layout()
        {
            _layoutPending = false;
            float h = _text.preferredHeight;
            if (h <= 0f && _text.text.Length > 0) { _layoutPending = true; return; }   // the font asset may still be loading
            _contentH = TextY + h;
            UiKit.Size(_text.rectTransform, TextW, Mathf.Max(304f, h));
            UiKit.Size(_scroll, ClipW, Mathf.Max(ClipH, _contentH));
            ScrollBy(0f);
        }

        private void ScrollBy(float d)
        {
            float max = Mathf.Max(0f, _contentH - ClipH);
            _offset = Mathf.Clamp(_offset + d, 0f, max);
            UiKit.Move(_scroll, 0f, -_offset);
            bool scrolls = max > 1f;
            SetActive(_up, scrolls);
            SetActive(_down, scrolls);
            _up.Interactable = _offset > 0f;
            _down.Interactable = _offset < max;
        }

        private void Turn(int d)
        {
            IWowSession s = Ctx.S;
            if (s == null || s.CurrentItemText == null) return;
            UiSound.Named("igMainMenuOptionCheckBoxOn");                                 // ItemTextPrevPage / NextPage OnClick
            s.ItemTextPage(d);
        }

        private static void SetActive(UiButton b, bool on)
        {
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
        }

        /// <summary>MATERIAL_TEXT_COLOR_TABLE (Constants.lua:37-44); an unknown material = "Default".</summary>
        private static Color TextColor(string material)
        {
            switch (material)
            {
                case "Stone": return Color.white;
                case "Marble": return Color.black;
                case "Silver": return new Color(0.12f, 0.12f, 0.12f);
                default: return new Color(0.18f, 0.12f, 0.06f);   // Default / Parchment / Bronze
            }
        }

        private static readonly Regex Br = new Regex(@"<\s*br\s*/?\s*>|</\s*p\s*>", RegexOptions.IgnoreCase);
        private static readonly Regex H1 = new Regex(@"<\s*h1[^>]*>", RegexOptions.IgnoreCase);
        private static readonly Regex H2 = new Regex(@"<\s*h[23][^>]*>", RegexOptions.IgnoreCase);
        private static readonly Regex HEnd = new Regex(@"</\s*h[123]\s*>", RegexOptions.IgnoreCase);
        private static readonly Regex Tag = new Regex(@"<\s*/?\s*(html|body|p|img|a|font)\b[^>]*>", RegexOptions.IgnoreCase);

        /// <summary>The SimpleHTML of a page (some books are "&lt;HTML&gt;&lt;BODY&gt;&lt;H1&gt;..") as TMP text: line breaks kept, headings
        /// larger, the other tags removed (ours); plain text stays as it is.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text ?? "";
            string t = Br.Replace(text, "\n");
            t = H1.Replace(t, "<size=130%>");
            t = H2.Replace(t, "<size=115%>");
            t = HEnd.Replace(t, "</size>\n");
            t = Tag.Replace(t, "");
            return t.Trim('\n', ' ');
        }
    }
}
