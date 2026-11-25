// iteration 11 - owner trainer (proposal-trainer.md 4.1, 5.5): WoW's ClassTrainerFrame (Blizzard_TrainerUI.xml / .lua of the user's install) -
// class, riding and profession trainers, online and offline. Built by uicore's registry (Plan A): desktop = the left panel, VR = the world dialog
// surface towards the trainer. Also the small helpers the three trainer-area windows share (TrUi, TrPopupBox).
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiTrainer : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            // UIPanelWindows["ClassTrainerFrame"] = { area = "left", pushable = 0 } (Blizzard_TrainerUI.lua:14)
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Trainer, Frame = "ClassTrainerFrame", Title = "Trainer", Area = UiPanelArea.Left, Pushable = 0,
                SessionWindow = true, Wanted = s => s.CurrentTrainer != null,
                NearGuid = s => s.CurrentTrainer != null ? s.CurrentTrainer.NpcGuid : 0UL,
                Closed = s => s.CloseTrainer(), Palm = false,
                OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",   // Blizzard_TrainerUI.xml:512-522
                Owner = "trainer", Hidden = false,
                Build = (parent, ctx, close) => new UiTrainer(parent, ctx, close)
            };
        }

        private const string Art = @"Interface\ClassTrainerFrame\UI-ClassTrainer-";
        private const float ListX = 21f, ListY = 96f, ListW = 296f, RowW = 293f, RowH = 16f;   // ClassTrainerListScrollFrame 296x184 TOPRIGHT (-67, -96); rows 293x16
        private const int MaxRows = 11;                                                          // CLASS_TRAINER_SKILLS_DISPLAYED (10 for a tradeskill trainer)
        private static readonly Color Green = new Color(0f, 1f, 0f), Red = new Color(0.9f, 0f, 0f), GreyC = new Color(0.5f, 0.5f, 0.5f);   // GameFontNormalLeftGreen / Red / Grey (FontStyles.xml)
        private static readonly Color SubGreen = new Color(0f, 0.6f, 0f), SubRed = new Color(0.6f, 0f, 0f);                                 // Blizzard_TrainerUI.lua:160-171

        private readonly Action _close;
        private readonly TextMeshProUGUI _name, _greeting;
        private readonly RawImage _barLeft, _barRight;
        private readonly UiButton[] _rows = new UiButton[MaxRows];
        private readonly TextMeshProUGUI[] _rowName = new TextMeshProUGUI[MaxRows], _rowSub = new TextMeshProUGUI[MaxRows];
        private readonly RawImage[] _rowSel = new RawImage[MaxRows];
        private readonly UiButton _up, _down, _train, _exit;
        private readonly UiButton[] _filters = new UiButton[3];
        private readonly bool[] _filterOn = { true, true, false };   // TRAINER_FILTER_AVAILABLE 1, _UNAVAILABLE 1, _USED 0 (Blizzard_TrainerUI.lua:9-11)
        private readonly RectTransform _detail, _costOk, _costRed;
        private readonly UiButton _icon;
        private readonly TextMeshProUGUI _skillName, _req, _costLabel, _desc;
        private readonly UiMoney _moneyOk, _moneyRed, _money;
        private readonly TrPopupBox _popup;
        private readonly RectTransform _list;

        private readonly List<TrainerServiceView> _visible = new List<TrainerServiceView>();
        private TrainerView _shown;
        private int _shownVersion = -1, _offset, _selSpell, _rowsShown = MaxRows;
        private bool _dirty = true, _iconLoaded, _tradeLayout;
        private string _iconPath;
        private static bool _logged;

        public UiTrainer(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "ClassTrainerFrame", 0f, 0f, W, H);
            UiKit.Frame4(Root, "Art", Art + "TopLeft.blp", Art + "TopRight.blp", Art + "BotLeft.blp", Art + "BotRight.blp");
            Portrait(UiArt.Tex(UiArt.PortraitNpc));
            _name = Title("", 17f);                                                                                             // UnitName("npc") TOP (0, -17)
            _greeting = UiKit.Text(Root, "Greeting", "", 11f, Color.white, TextAlignmentOptions.TopLeft, 76f, 38f, 260f, 30f);  // GameFontHighlight 260x30 (76, -38)
            UiKit.CloseButton(Root, 323f, 8f, Close);                                                                          // TOPRIGHT (-29, -8)
            string[] labels = { TrUi.G("AVAILABLE", "Available"), TrUi.G("UNAVAILABLE", "Unavailable"), TrUi.G("USED", "Already Known") };   // GlobalStrings.lua:663 / 7828 / 7916
            float[] fx = { 72f, 158f, 250f };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _filters[i] = TrUi.CheckBox(Root, labels[i], fx[i], 66f, 84f, () => _filterOn[k], v => { _filterOn[k] = v; _dirty = true; });
            }
            _barLeft = UiKit.Pic(Root, "HorizontalBarLeft", UiArt.Tex(Art + "HorizontalBar.blp"), 0f, 1f, 0f, 0.25f, 15f, 275f, 256f, 16f);           // (15, -275)
            _barRight = UiKit.Pic(Root, "HorizontalBarRight", UiArt.Tex(Art + "HorizontalBar.blp"), 0f, 0.29296875f, 0.25f, 0.5f, 271f, 275f, 75f, 16f);

            _list = UiKit.Rect(Root, "List", ListX, ListY, ListW, MaxRows * RowH);
            for (int i = 0; i < MaxRows; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(_list, "ClassTrainerSkill" + (i + 1), 1f, i * RowH, RowW, RowH);
                _rowSel[i] = UiKit.Pic(b.transform, "Selected", UiArt.Tex(TrUi.Highlight2), 0f, 0f, RowW, RowH);
                _rowSel[i].enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(TrUi.Highlight2), 0f, 0f, RowW, RowH);
                b.Hilite.enabled = false;
                _rowName[i] = UiKit.Text(b.transform, "Name", "", 12f, Green, TextAlignmentOptions.Left, 4f, 0f, 200f, RowH);
                _rowName[i].textWrappingMode = TextWrappingModes.NoWrap;
                _rowName[i].overflowMode = TextOverflowModes.Ellipsis;
                _rowSub[i] = UiKit.Text(b.transform, "Sub", "", 12f, SubGreen, TextAlignmentOptions.Right, 200f, 0f, RowW - 204f, RowH);
                _rowSub[i].textWrappingMode = TextWrappingModes.NoWrap;
                b.Click = () => Select(k);
                b.Tip = () => RowTip(k);
                b.gameObject.SetActive(false);
                _rows[i] = b;
            }
            _up = UiKit.ArtButton(Root, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 320f, 94f, 20f, () => Scroll(-1));
            _down = UiKit.ArtButton(Root, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 320f, 262f, 20f, () => Scroll(1));

            // the detail (ClassTrainerDetailScrollFrame 296x135, 8 below the list)
            _detail = UiKit.Rect(Root, "Detail", ListX, 288f, ListW, 118f);
            _detail.gameObject.AddComponent<RectMask2D>();
            UiKit.Pic(_detail, "EmptySlot", UiArt.Tex(UiArt.EmptySlot), -8f, -9f, 64f, 64f);                                     // UI-EmptySlot 64x64 at (-13, 13) of the icon
            _icon = UiKit.Slot(_detail, "ClassTrainerSkillIcon", 5f, 4f, 37f, false);                                                // 37x37 at (5, -4)
            _icon.Tip = () => { var sv = Selected(); return sv != null ? sv.Tooltip : null; };
            _skillName = UiKit.Text(_detail, "SkillName", "", 12f, UiArt.Gold, TextAlignmentOptions.TopLeft, 46f, 2f, 244f, 15f);    // GameFontNormal (46, -2) width 244
            _skillName.textWrappingMode = TextWrappingModes.NoWrap;
            _req = UiKit.Text(_detail, "Requirements", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 46f, 17f, 244f, 30f);     // GameFontHighlightSmall under the name
            _costLabel = UiKit.Text(_detail, "CostLabel", TrUi.G("COSTS_LABEL", "Cost:"), 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 5f, 50f, 60f, 13f);   // (5, -50)
            _costOk = UiKit.Rect(_detail, "Cost", 0f, 0f, 200f, 20f);
            _moneyOk = new UiMoney(_costOk, 170f, 49f, Color.white);
            _costRed = UiKit.Rect(_detail, "CostRed", 0f, 0f, 200f, 20f);
            _moneyRed = new UiMoney(_costRed, 170f, 49f, UiArt.ErrorRed);                                                          // red when GetMoney() < cost (lua :341-357)
            _desc = UiKit.Text(_detail, "Description", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 5f, 72f, 290f, 46f);     // 290 wide, 10 below the cost

            _train = UiKit.PanelButton(Root, TrUi.G("TRAIN", "Train"), 184f, 409f, 80f, 22f, TrainPressed);                          // CENTER (224, -420)
            _exit = UiKit.PanelButton(Root, TrUi.G("EXIT", "Exit"), 265f, 409f, 80f, 22f, Close);                                    // CENTER (305, -420)
            RectTransform money = UiKit.Rect(Root, "ClassTrainerMoneyFrame", 0f, 0f, 200f, 20f);
            _money = new UiMoney(money, 180f, 412f, Color.white);                                                                   // BOTTOMRIGHT -> BOTTOMLEFT (180, 86)
            _popup = new TrPopupBox(Root, 32f, 150f);
            Root.gameObject.SetActive(false);
        }

        protected override void OnOpen()
        {
            _dirty = true;
            _offset = 0;
            _selSpell = 0;
            _shown = null;
        }

        protected override void OnClose()
        {
            TrUi.HidePopup("CONFIRM_PROFESSION", _popup);   // the popup hides with the frame (Blizzard_TrainerUI.xml:515-522)
        }

        private void Close()
        {
            TrUi.HidePopup("CONFIRM_PROFESSION", _popup);
            if (_close != null) _close();
            else if (Ctx != null && Ctx.S != null) Ctx.S.CloseTrainer();
        }

        private TrainerServiceView Selected()
        {
            foreach (var sv in _visible) if (sv.SpellId == _selSpell) return sv;
            return null;
        }

        private string RowTip(int row)
        {
            int i = _offset + row;
            return i >= 0 && i < _visible.Count ? _visible[i].Tooltip : null;
        }

        private void Select(int row)
        {
            int i = _offset + row;
            if (i < 0 || i >= _visible.Count) return;
            _selSpell = _visible[i].SpellId;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            _dirty = true;
        }

        private void Scroll(int by)
        {
            int max = Math.Max(0, _visible.Count - _rowsShown);
            int o = Mathf.Clamp(_offset + by, 0, max);
            if (o == _offset) return;
            _offset = o;
            _dirty = true;
        }

        private void TrainPressed()
        {
            var s = Ctx != null ? Ctx.S : null;
            var sv = Selected();
            if (s == null || sv == null || !sv.CanTrain) return;
            if (!sv.NeedsConfirm) { s.BuyTrainerSpell(sv.SpellId); return; }
            // StaticPopupDialogs["CONFIRM_PROFESSION"] (Blizzard_TrainerUI.lua:15-37): PROFESSION_CONFIRMATION2 when a profession is already known
            var p = s.Player;
            bool second = p != null && p.FreeProfessions < 2;   // MAX_LEARNABLE_PROFESSIONS = 2 (lua :6)
            string skill = sv.Name;
            string text = second
                ? TrUi.Fmt(TrUi.G("PROFESSION_CONFIRMATION2", "You may only know two professions at any one time.  Would you like to learn |cffffd200%s|r as your second one?"), skill)
                : TrUi.Fmt(TrUi.G("PROFESSION_CONFIRMATION1", "You may only know two professions at any one time.  Would you like to learn |cffffd200%s|r as your first one?"), skill);
            int id = sv.SpellId;
            TrUi.Confirm(_popup, "CONFIRM_PROFESSION", text, TrUi.G("ACCEPT", "Accept"), TrUi.G("CANCEL", "Cancel"), () => { if (Ctx.S != null) Ctx.S.BuyTrainerSpell(id); }, 0f, false, false);
        }

        public override void Refresh(float now)
        {
            var s = Ctx != null ? Ctx.S : null;
            var v = s != null ? s.CurrentTrainer : null;
            if (v == null) return;
            if (!ReferenceEquals(v, _shown) || v.Version != _shownVersion) { _dirty = true; if (!ReferenceEquals(v, _shown)) { _offset = 0; _selSpell = 0; } }
            int wheel = TrUi.Wheel(_list, Ctx.Vr);
            if (wheel != 0) Scroll(wheel);
            if (_dirty) Rebuild(v);
            var p = s.Player;
            if (p != null) _money.Set(p.Money);
            var sel = Selected();
            _train.Interactable = sel != null && sel.CanTrain;
            if (sel != null && sel.MoneyCost > 0)
            {
                bool poor = p == null || p.Money < sel.MoneyCost;
                _costOk.gameObject.SetActive(!poor);
                _costRed.gameObject.SetActive(poor);
            }
            if (sel != null) SetIcon(_icon, sel.IconPath, ref _iconPath, ref _iconLoaded);
            _popup.Tick();
        }

        private void Rebuild(TrainerView v)
        {
            _dirty = false;
            if (!ReferenceEquals(v, _shown) && !_logged)
            {
                _logged = true;
                WowLog.Info("UI trainer: ClassTrainerFrame built (filter = three check boxes, ours - WoW uses a dropdown; list order by level / name / rank, ours)");
            }
            _shown = v;
            _shownVersion = v.Version;
            bool trade = v.Kind == TrainerKind.Tradeskill;
            if (trade != _tradeLayout)
            {
                // a tradeskill trainer: 10 rows, the bar at -259 and the list 168 high (Blizzard_TrainerUI.lua:445-459)
                _tradeLayout = trade;
                _rowsShown = trade ? MaxRows - 1 : MaxRows;
                float bar = trade ? 259f : 275f;
                UiKit.Move(_barLeft.rectTransform, 15f, bar);
                UiKit.Move(_barRight.rectTransform, 271f, bar);
                UiKit.Move(_detail, ListX, trade ? 272f : 288f);
                UiKit.Move(_down.Rt, 320f, trade ? 246f : 262f);
            }
            _name.text = v.NpcName ?? "";
            _greeting.text = v.Greeting ?? "";
            _visible.Clear();
            foreach (var sv in v.Services)
            {
                int f = sv.State == TrainerSpellState.Available ? 0 : sv.State == TrainerSpellState.Unavailable ? 1 : 2;
                if (_filterOn[f]) _visible.Add(sv);
            }
            if (Selected() == null)
            {
                _selSpell = 0;
                foreach (var sv in _visible) if (sv.State == TrainerSpellState.Available) { _selSpell = sv.SpellId; break; }   // ClassTrainer_SelectFirstLearnableSkill
                if (_selSpell == 0 && _visible.Count > 0) _selSpell = _visible[0].SpellId;
            }
            _offset = Mathf.Clamp(_offset, 0, Math.Max(0, _visible.Count - _rowsShown));
            for (int r = 0; r < MaxRows; r++)
            {
                int i = _offset + r;
                bool on = r < _rowsShown && i < _visible.Count;
                _rows[r].gameObject.SetActive(on);
                if (!on) continue;
                var sv = _visible[i];
                Color c = sv.State == TrainerSpellState.Available ? Green : sv.State == TrainerSpellState.Unavailable ? Red : GreyC;
                Color sc = sv.State == TrainerSpellState.Available ? SubGreen : sv.State == TrainerSpellState.Unavailable ? SubRed : GreyC;
                _rowName[r].text = sv.Name ?? "";
                _rowName[r].color = c;
                _rows[r].LabelColor = c;
                _rowSub[r].text = string.IsNullOrEmpty(sv.Rank) ? "" : "(" + sv.Rank + ")";   // PARENS_TEMPLATE (GlobalStrings.lua:5567)
                _rowSub[r].color = sc;
                bool selected = sv.SpellId == _selSpell;
                _rowSel[r].enabled = selected;
                if (selected) _rowSel[r].color = c;                                            // the highlight tinted like the line (lua :252-257)
            }
            _up.Interactable = _offset > 0;
            _down.Interactable = _offset + _rowsShown < _visible.Count;
            var sel = Selected();
            _detail.gameObject.SetActive(sel != null);
            if (sel == null) return;
            _skillName.text = string.IsNullOrEmpty(sel.Rank) ? sel.Name : sel.Name + " (" + sel.Rank + ")";
            _req.text = sel.Requirements != null ? TrUi.G("REQUIRES_LABEL", "Requires:") + sel.Requirements.Substring(Math.Min(sel.Requirements.Length, 9)) : "";
            bool cost = sel.MoneyCost > 0;
            _costLabel.gameObject.SetActive(cost);
            _costOk.gameObject.SetActive(cost);
            _costRed.gameObject.SetActive(false);
            if (cost) { _moneyOk.Set(sel.MoneyCost); _moneyRed.Set(sel.MoneyCost); }
            _desc.text = TrUi.Body(sel.Tooltip);
            UiKit.Move(_desc.rectTransform, 5f, cost ? 72f : 50f);
        }
    }

    /// <summary>The small pieces the trainer-area windows (UiTrainer, UiTradeSkill, UiSkills) share. Texts through UiGlobalStrings with the enUS
    /// fallbacks of GlobalStrings.lua; WoW's |c / |r / |n codes as TMP rich text.</summary>
    internal static class TrUi
    {
        public const string Highlight2 = @"Interface\Buttons\UI-Listbox-Highlight2.blp";   // ClassTrainerSkillHighlight / TradeSkillHighlight (293x16)
        public const string CheckUp = @"Interface\Buttons\UI-CheckBox-Up.blp", CheckDown = @"Interface\Buttons\UI-CheckBox-Down.blp";
        public const string CheckHighlight = @"Interface\Buttons\UI-CheckBox-Highlight.blp", CheckCheck = @"Interface\Buttons\UI-CheckBox-Check.blp";
        public const string PlusUp = @"Interface\Buttons\UI-PlusButton-Up.blp", MinusUp = @"Interface\Buttons\UI-MinusButton-Up.blp";
        public const string PlusMinusHighlight = @"Interface\Buttons\UI-PlusButton-Hilight.blp";

        public static string G(string key, string fallback)
        {
            string s = null;
            try { s = UiGlobalStrings.G(key, fallback); } catch (Exception) { s = null; }
            return Wow(string.IsNullOrEmpty(s) ? fallback : s);
        }

        /// <summary>WoW's colour / newline escapes as TMP rich text: |cffRRGGBB -> &lt;color=#RRGGBB&gt;, |r -> &lt;/color&gt;, |n -> new line.</summary>
        public static string Wow(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('|') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '|' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if ((n == 'c' || n == 'C') && i + 9 < s.Length) { sb.Append("<color=#").Append(s, i + 4, 6).Append('>'); i += 9; continue; }
                    if (n == 'r' || n == 'R') { sb.Append("</color>"); i += 1; continue; }
                    if (n == 'n' || n == 'N') { sb.Append('\n'); i += 1; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Lua-style %s / %d filled in order.</summary>
        public static string Fmt(string f, params object[] args)
        {
            if (string.IsNullOrEmpty(f)) return "";
            var sb = new StringBuilder(f.Length + 32);
            int a = 0;
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i] == '%' && i + 1 < f.Length && (f[i + 1] == 's' || f[i + 1] == 'd'))
                {
                    sb.Append(a < args.Length ? Convert.ToString(args[a], System.Globalization.CultureInfo.InvariantCulture) : "");
                    a++;
                    i++;
                    continue;
                }
                sb.Append(f[i]);
            }
            return sb.ToString();
        }

        /// <summary>A spell tooltip without its first line (the name the window already shows).</summary>
        public static string Body(string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip)) return "";
            int nl = tooltip.IndexOf('\n');
            return nl >= 0 && nl + 1 < tooltip.Length ? tooltip.Substring(nl + 1) : "";
        }

        /// <summary>OptionsCheckButtonTemplate-like: a 16-unit UI-CheckBox with its label (GameFontNormalSmall) to the right; one click toggles.</summary>
        public static UiButton CheckBox(Transform parent, string label, float x, float y, float w, Func<bool> get, Action<bool> set)
        {
            UiButton b = UiKit.Button(parent, "Check " + label, x, y, w, 18f);
            b.Art = UiKit.Pic(b.transform, "Box", UiArt.Tex(CheckUp), 0f, 0f, 18f, 18f);
            b.ArtUp = UiArt.Tex(CheckUp);
            b.Pushed = UiKit.Pic(b.transform, "Down", UiArt.Tex(CheckDown), 0f, 0f, 18f, 18f);
            b.Pushed.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(CheckHighlight), 0f, 0f, 18f, 18f);
            b.Hilite.enabled = false;
            RawImage check = UiKit.Pic(b.transform, "Check", UiArt.Tex(CheckCheck), 0f, 0f, 18f, 18f);
            check.enabled = get();
            b.Label = UiKit.Text(b.transform, "Label", label, 10f, UiArt.Gold, TextAlignmentOptions.Left, 19f, 0f, w - 19f, 18f);
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            b.LabelColor = UiArt.Gold;
            b.Click = () =>
            {
                bool v = !get();
                set(v);
                check.enabled = v;
                UiSound.Named(v ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            };
            return b;
        }

        /// <summary>The mouse wheel over a list (desktop only: the overlay canvas, camera null): -1 up, +1 down, 0 none.</summary>
        public static int Wheel(RectTransform list, bool vr)
        {
            if (vr || list == null || !list.gameObject.activeInHierarchy) return 0;
            try
            {
                float d = Input.mouseScrollDelta.y;
                if (Mathf.Abs(d) < 0.01f) return 0;
                if (!RectTransformUtility.RectangleContainsScreenPoint(list, Input.mousePosition, null)) return 0;
                return d > 0f ? -1 : 1;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>uicore's UiPopup when it is in the build (Show returns a handle &gt; 0), else the window's own popup.</summary>
        public static void Confirm(TrPopupBox fallback, string which, string text, string b1, string b2, Action accept, float timeout, bool whileDead, bool exclusive)
        {
            int h = 0;
            try
            {
                h = UiPopup.Show(new UiPopupSpec
                {
                    Which = which, Text = text, Button1 = b1, Button2 = b2, OnAccept = accept, Timeout = timeout, WhileDead = whileDead,
                    Exclusive = exclusive, ShowAlert = true, HideOnEscape = true
                });
            }
            catch (Exception e) { WowLog.Warn("UI trainer: UiPopup.Show threw (" + e.Message + ") - the window's own popup is used"); h = 0; }
            if (h > 0) return;
            if (fallback != null) fallback.Show(which, text, b1, b2, accept, timeout);
        }

        public static void HidePopup(string which, TrPopupBox fallback)
        {
            try { UiPopup.HideWhich(which); } catch (Exception) { }
            if (fallback != null) fallback.Hide(which);
        }
    }

    /// <summary>The window's own StaticPopup-like box (only when uicore's UiPopup is not in the build): UI-DialogBox backdrop (edge 32, insets 11),
    /// the text, two StaticPopupButtons 128x21 (StaticPopup.xml), above the window's content.</summary>
    internal sealed class TrPopupBox
    {
        private const float PopupW = 320f, ButtonW = 128f, ButtonH = 21f;
        private readonly RectTransform _root;
        private readonly TextMeshProUGUI _text;
        private readonly UiButton _one, _two;
        private Action _accept;
        private string _which;
        private float _until;

        public TrPopupBox(RectTransform parent, float x, float y)
        {
            _root = UiKit.Rect(parent, "Popup", x, y, PopupW, 100f);
            var back = UiBackdrop.Make(_root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            back.Place(0f, 0f, PopupW, 100f);
            _text = UiKit.Text(_root, "Text", "", 12f, Color.white, TextAlignmentOptions.Center, 20f, 14f, PopupW - 40f, 40f);
            _one = Button(PopupW * 0.5f - 6f - ButtonW, 64f, () => Press(true));
            _two = Button(PopupW * 0.5f + 6f, 64f, () => Press(false));
            _root.gameObject.SetActive(false);
        }

        private UiButton Button(float x, float y, Action click)
        {
            UiButton b = UiKit.Button(_root, "Button", x, y, ButtonW, ButtonH);
            b.Art = UiKit.Pic(b.transform, "Up", UiArt.Tex(UiArt.DialogButtonUp), 0f, 1f, 0f, 0.71875f, 0f, 0f, ButtonW, ButtonH);
            b.ArtUp = UiArt.Tex(UiArt.DialogButtonUp);
            b.Pushed = UiKit.Pic(b.transform, "Down", UiArt.Tex(UiArt.DialogButtonDown), 0f, 1f, 0f, 0.71875f, 0f, 0f, ButtonW, ButtonH);
            b.Pushed.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.DialogButtonHighlight), 0f, 1f, 0f, 0.71875f, 0f, 0f, ButtonW, ButtonH);
            b.Hilite.enabled = false;
            b.Label = UiKit.Text(b.transform, "Label", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, ButtonW, ButtonH);
            b.LabelColor = UiArt.Gold;
            b.Click = click;
            return b;
        }

        public bool IsShown { get { return _root.gameObject.activeSelf; } }

        public void Show(string which, string text, string b1, string b2, Action accept, float timeout)
        {
            _which = which;
            _accept = accept;
            _text.text = text ?? "";
            _one.SetLabel(b1 ?? "OK");
            _two.SetLabel(b2 ?? "");
            _two.gameObject.SetActive(!string.IsNullOrEmpty(b2));
            _until = timeout > 0f ? Time.unscaledTime + timeout : -1f;
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            UiSound.Named("igMainMenuOpen");
        }

        public void Hide(string which)
        {
            if (which != null && _which != null && which != _which) return;
            _root.gameObject.SetActive(false);
            _accept = null;
        }

        public void Tick()
        {
            if (_until > 0f && IsShown && Time.unscaledTime > _until) Hide(null);
        }

        private void Press(bool accept)
        {
            Action a = _accept;
            Hide(null);
            if (accept && a != null) a();
        }
    }
}
