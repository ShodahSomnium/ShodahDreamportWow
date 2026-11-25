// iteration 11 - owner trainer (proposal-trainer.md 4.3, 5.5): WoW's SkillFrame (the CharacterFrame's "Skills" tab, SkillFrame.xml / .lua of the
// user's install): category headers (collapsible), a bar per skill ("value/max"), the detail bar with WoW's Unlearn button for primary
// professions, and (ours) one tap on a profession line opens its profession window. Toggled window (K, the palm, the CharacterFrame tabs).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiSkills : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            // a CharacterFrame tab: UIPanelWindows["CharacterFrame"] = { area = "left", pushable = 3, whileDead = 1 } (UIParent.lua:20)
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Skills, Frame = "SkillFrame", TitleKey = "SKILLS", Title = "Skills",   // GlobalStrings.lua:6331
                Area = UiPanelArea.Left, Pushable = 3, WhileDead = true, Key = KeyCode.K, Binding = "TOGGLECHARACTER1",   // PLAN11 s2: K = Skills in the world (DefaultBindings.wtf:111)
                Palm = true, OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",
                Owner = "trainer", Hidden = false,
                Build = (parent, ctx, close) => new UiSkills(parent, ctx, close)
            };
        }

        private const string Doll = @"Interface\PaperDollInfoFrame\";
        private const string BarArt = Doll + "UI-Character-Skills-Bar.blp", BarBorder = Doll + "UI-Character-Skills-BarBorder.blp";
        private const string CancelUp = @"Interface\Buttons\CancelButton-Up.blp", CancelDown = @"Interface\Buttons\CancelButton-Down.blp", CancelHighlight = @"Interface\Buttons\CancelButton-Highlight.blp";
        private const int Rows = 12;                 // SKILLS_TO_DISPLAY (SkillFrame.lua:1)
        private const float RowStep = 18f, ListY = 79f, BarX = 38f, BarW = 271f, BarH = 15f;   // SkillRankFrame1 (38, -79), 271x15, the next 3 below
        private static readonly Color Blue = new Color(0f, 0f, 1f, 0.5f), BlueBack = new Color(0f, 0f, 0.75f, 0.5f), GreyBar = new Color(0.5f, 0.5f, 0.5f, 0.5f);   // SkillFrame.lua:95-170

        private sealed class Line { public bool Header; public int Category; public string Text; public SkillView Skill; }

        private readonly Action _close;
        private readonly TextMeshProUGUI _title;
        private readonly RectTransform _list;
        private readonly UiButton[] _rows = new UiButton[Rows];
        private readonly RawImage[] _rowPlus = new RawImage[Rows], _rowSel = new RawImage[Rows];
        private readonly TextMeshProUGUI[] _rowHeader = new TextMeshProUGUI[Rows], _rowName = new TextMeshProUGUI[Rows], _rowRank = new TextMeshProUGUI[Rows];
        private readonly UiBar[] _rowBar = new UiBar[Rows];
        private readonly RawImage[] _rowBack = new RawImage[Rows], _rowBorder = new RawImage[Rows];
        private readonly UiButton _up, _down, _all, _unlearn;
        private readonly RawImage _allPlus;
        private readonly RectTransform _detail;
        private readonly UiBar _detailBar;
        private readonly TextMeshProUGUI _detailName, _detailRank, _detailDesc, _detailHint;
        private readonly TrPopupBox _popup;

        private readonly List<Line> _lines = new List<Line>();
        private readonly HashSet<int> _collapsed = new HashSet<int>();
        private int _shownVersion = -1, _offset, _selSkill;
        private bool _dirty = true;
        private static bool _logged;

        public UiSkills(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "SkillFrame", 0f, 0f, W, H);
            RectTransform art = UiKit.Frame4(Root, "Art", Doll + "UI-Character-General-TopLeft.blp", Doll + "UI-Character-General-TopRight.blp",
                                             Doll + "SkillFrame-BotLeft.blp", Doll + "SkillFrame-BotRight.blp");
            UiKit.Move(art, 2f, 1f);                                                                     // the art at TOPLEFT (2, -1) (SkillFrame.xml:136-179)
            Portrait(UiArt.Tex(UiArt.PortraitNpc));
            _title = Title(TrUi.G("SKILLS", "Skills"), 17f);
            UiKit.CloseButton(Root, 323f, 8f, Close);
            // "All" collapse / expand (SkillFrameCollapseAllButton in SkillFrameExpandButtonFrame 54x32 at (70, -49))
            _all = UiKit.Button(Root, "SkillFrameCollapseAllButton", 74f, 56f, 60f, 16f);
            _allPlus = UiKit.Pic(_all.transform, "Plus", UiArt.Tex(TrUi.MinusUp), 0f, 0f, 16f, 16f);
            _all.Hilite = UiKit.Pic(_all.transform, "Highlight", UiArt.Add(TrUi.PlusMinusHighlight), 0f, 0f, 16f, 16f);
            _all.Hilite.enabled = false;
            _all.Label = UiKit.Text(_all.transform, "Label", TrUi.G("ALL", "All"), 10f, UiArt.Gold, TextAlignmentOptions.Left, 19f, 0f, 40f, 16f);   // GlobalStrings.lua:526
            _all.LabelColor = UiArt.Gold;
            _all.Click = ToggleAll;

            _list = UiKit.Rect(Root, "List", 21f, ListY - 4f, 296f, Rows * RowStep + 4f);
            for (int i = 0; i < Rows; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(Root, "SkillRow" + (i + 1), 20f, ListY + i * RowStep - 1f, BarX - 20f + BarW + 4f, 17f);
                _rowSel[i] = UiKit.Pic(b.transform, "Selected", UiArt.Add(TrUi.Highlight2), BarX - 20f - 4f, -1f, BarW + 8f, 19f);
                _rowSel[i].enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(TrUi.Highlight2), 0f, 0f, BarX - 20f + BarW + 4f, 17f);
                b.Hilite.enabled = false;
                // header row: plus / minus + the category name (SkillLabelTemplate)
                _rowPlus[i] = UiKit.Pic(b.transform, "Plus", UiArt.Tex(TrUi.MinusUp), 2f, 0f, 16f, 16f);
                _rowHeader[i] = UiKit.Text(b.transform, "Header", "", 12f, UiArt.Gold, TextAlignmentOptions.Left, 21f, 0f, 260f, 17f);
                _rowHeader[i].textWrappingMode = TextWrappingModes.NoWrap;
                // skill row: the status bar in its border (SkillStatusBarTemplate)
                _rowBack[i] = UiKit.Solid(b.transform, "Background", BlueBack, BarX - 20f, 1f, BarW, BarH);
                _rowBar[i] = UiBar.Make(b.transform, "Bar", BarX - 20f, 1f, BarW, BarH, Blue, false);
                _rowBar[i].Fill.texture = UiArt.Tex(BarArt);
                _rowBorder[i] = UiKit.Pic(b.transform, "Border", UiArt.Tex(BarBorder), BarX - 20f - 5f, 1f + BarH * 0.5f - 16f, 281f, 32f);   // 281x32 at LEFT (-5, 0)
                _rowName[i] = UiKit.Text(b.transform, "SkillName", "", 10f, UiArt.Gold, TextAlignmentOptions.Left, BarX - 20f + 6f, 1f, 160f, BarH);   // GameFontNormalSmall LEFT (6, 1)
                _rowName[i].textWrappingMode = TextWrappingModes.NoWrap;
                _rowRank[i] = UiKit.Text(b.transform, "SkillRank", "", 10f, Color.white, TextAlignmentOptions.Right, BarX - 20f + BarW - 134f, 1f, 128f, BarH);   // GameFontHighlightSmall, 128 wide
                _rowRank[i].textWrappingMode = TextWrappingModes.NoWrap;
                b.Click = () => Press(k);
                b.Tip = () => RowTip(k);
                b.gameObject.SetActive(false);
                _rows[i] = b;
            }
            _up = UiKit.ArtButton(Root, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 320f, 74f, 20f, () => Scroll(-1));
            _down = UiKit.ArtButton(Root, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 320f, 276f, 20f, () => Scroll(1));
            UiKit.Pic(Root, "HorizontalBarLeft", UiArt.Tex(@"Interface\ClassTrainerFrame\UI-ClassTrainer-HorizontalBar.blp"), 0f, 1f, 0f, 0.25f, 15f, 290f, 256f, 16f);   // (15, -290)
            UiKit.Pic(Root, "HorizontalBarRight", UiArt.Tex(@"Interface\ClassTrainerFrame\UI-ClassTrainer-HorizontalBar.blp"), 0f, 0.29296875f, 0.25f, 0.5f, 271f, 290f, 75f, 16f);

            // the detail (SkillDetailScrollFrame 296x107, 8 below the list): SkillDetailStatusBar 211x15 at the top's CENTER (-10, -20)
            _detail = UiKit.Rect(Root, "Detail", 21f, 303f, 296f, 104f);
            UiKit.Solid(_detail, "Background", BlueBack, 32f, 13f, 211f, 15f);
            _detailBar = UiBar.Make(_detail, "SkillDetailStatusBar", 32f, 13f, 211f, 15f, Blue, false);
            _detailBar.Fill.texture = UiArt.Tex(BarArt);
            UiKit.Pic(_detail, "Border", UiArt.Tex(BarBorder), 27f, 13f + 7.5f - 16f, 221f, 32f);
            _detailName = UiKit.Text(_detail, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Left, 38f, 13f, 130f, 15f);
            _detailName.textWrappingMode = TextWrappingModes.NoWrap;
            _detailRank = UiKit.Text(_detail, "Rank", "", 10f, Color.white, TextAlignmentOptions.Right, 112f, 13f, 126f, 15f);
            _detailRank.textWrappingMode = TextWrappingModes.NoWrap;
            // SkillDetailStatusBarUnlearnButton 32x32 (CancelButton art), shown for an abandonable skill (SkillFrame.lua:219-227)
            _unlearn = UiKit.ArtButton(_detail, "SkillDetailStatusBarUnlearnButton", CancelUp, CancelDown, null, CancelHighlight, 245f, 4f, 32f, UnlearnPressed);
            _unlearn.Tip = () => TrUi.G("UNLEARN_SKILL_TOOLTIP", "Unlearn this profession");   // GlobalStrings.lua:7903
            _detailDesc = UiKit.Text(_detail, "Description", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 5f, 36f, 286f, 52f);
            _detailHint = UiKit.Text(_detail, "Hint", "", 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 5f, 88f, 286f, 14f);
            _detail.gameObject.SetActive(false);

            UiKit.PanelButton(Root, TrUi.G("CLOSE", "Close"), 265f, 411f, 80f, 22f, Close);   // SkillFrameCancelButton CENTER (305, -422)
            _popup = new TrPopupBox(Root, 32f, 150f);
            UiCharacterTabs.Build(Root, ctx, UiWindowKind.Skills);                           // talents' CharacterFrame tab strip (hook: Skills window, area trainer)
            Root.gameObject.SetActive(false);
        }

        protected override void OnOpen()
        {
            _dirty = true;
            _offset = 0;
        }

        protected override void OnClose()
        {
            TrUi.HidePopup("UNLEARN_SKILL", _popup);
        }

        private void Close()
        {
            TrUi.HidePopup("UNLEARN_SKILL", _popup);
            if (_close != null) _close();
            else if (Ctx != null && Ctx.Toggle != null) Ctx.Toggle(UiWindowKind.Skills);
        }

        private SkillView Find(int skill)
        {
            var p = Ctx != null ? Ctx.P : null;
            if (p == null || p.Skills == null) return null;
            foreach (var s in p.Skills) if (s != null && s.SkillId == skill) return s;
            return null;
        }

        private string RowTip(int row)
        {
            int i = _offset + row;
            if (i < 0 || i >= _lines.Count || _lines[i].Header) return null;
            var s = _lines[i].Skill;
            if (s.HasTradeWindow) return s.Name + "\n<color=#00ff00>Tap to open the profession window.</color>";   // ours
            return string.IsNullOrEmpty(s.Description) ? s.Name : s.Name + "\n" + s.Description;
        }

        private void Press(int row)
        {
            int i = _offset + row;
            if (i < 0 || i >= _lines.Count) return;
            var l = _lines[i];
            if (l.Header)
            {
                if (!_collapsed.Remove(l.Category)) _collapsed.Add(l.Category);
                _dirty = true;
                return;
            }
            _selSkill = l.Skill.SkillId;
            _dirty = true;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            if (l.Skill.HasTradeWindow && Ctx.S != null) Ctx.S.OpenTradeSkill(l.Skill.SkillId);   // ours: one tap opens the profession window
        }

        private void ToggleAll()
        {
            var p = Ctx != null ? Ctx.P : null;
            if (p == null) return;
            bool anyOpen = false;
            foreach (var s in p.Skills) if (s != null && !_collapsed.Contains(s.Category)) { anyOpen = true; break; }
            _collapsed.Clear();
            if (anyOpen) foreach (var s in p.Skills) if (s != null) _collapsed.Add(s.Category);
            _offset = 0;
            _dirty = true;
        }

        private void Scroll(int by)
        {
            int max = Math.Max(0, _lines.Count - Rows);
            int o = Mathf.Clamp(_offset + by, 0, max);
            if (o == _offset) return;
            _offset = o;
            _dirty = true;
        }

        private void UnlearnPressed()
        {
            var s = Find(_selSkill);
            if (s == null || !s.Abandonable || Ctx.S == null) return;
            int id = s.SkillId;
            // StaticPopupDialogs["UNLEARN_SKILL"] (StaticPopup.lua:2174-2186): UNLEARN_SKILL, UNLEARN / CANCEL, timeout 60, exclusive, whileDead
            TrUi.Confirm(_popup, "UNLEARN_SKILL", TrUi.Fmt(TrUi.G("UNLEARN_SKILL", "Do you want to unlearn %s?"), s.Name),
                         TrUi.G("UNLEARN", "Unlearn"), TrUi.G("CANCEL", "Cancel"), () => { if (Ctx.S != null) Ctx.S.UnlearnSkill(id); }, 60f, true, true);
        }

        public override void Refresh(float now)
        {
            var p = Ctx != null ? Ctx.P : null;
            if (p == null) return;
            if (p.SkillsVersion != _shownVersion) _dirty = true;
            int wheel = TrUi.Wheel(_list, Ctx.Vr);
            if (wheel != 0) Scroll(wheel);
            if (_dirty) Rebuild(p);
            _popup.Tick();
        }

        private void Rebuild(PlayerState p)
        {
            _dirty = false;
            _shownVersion = p.SkillsVersion;
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI skills: SkillFrame built (" + (p.Skills != null ? p.Skills.Count : 0) + " skills; a profession line opens its window with one tap - ours)");
            }
            _title.text = string.IsNullOrEmpty(p.Name) ? TrUi.G("SKILLS", "Skills") : p.Name;   // the CharacterFrame's title is the character's name
            _lines.Clear();
            int cat = int.MinValue;
            if (p.Skills != null)
                foreach (var s in p.Skills)
                {
                    if (s == null) continue;
                    if (s.Category != cat)
                    {
                        cat = s.Category;
                        _lines.Add(new Line { Header = true, Category = cat, Text = string.IsNullOrEmpty(s.CategoryName) ? "?" : s.CategoryName });
                    }
                    if (!_collapsed.Contains(cat)) _lines.Add(new Line { Category = cat, Skill = s, Text = s.Name });
                }
            _offset = Mathf.Clamp(_offset, 0, Math.Max(0, _lines.Count - Rows));
            bool anyOpen = false;
            foreach (var l in _lines) if (!l.Header) { anyOpen = true; break; }
            _allPlus.texture = UiArt.Tex(anyOpen ? TrUi.MinusUp : TrUi.PlusUp);
            for (int r = 0; r < Rows; r++)
            {
                int i = _offset + r;
                bool on = i < _lines.Count;
                _rows[r].gameObject.SetActive(on);
                if (!on) continue;
                var l = _lines[i];
                bool h = l.Header;
                _rowPlus[r].enabled = h;
                _rowHeader[r].enabled = h;
                _rowBack[r].enabled = !h;
                _rowBar[r].Root.gameObject.SetActive(!h);
                _rowBorder[r].enabled = !h;
                _rowName[r].enabled = !h;
                _rowRank[r].enabled = !h;
                _rowSel[r].enabled = !h && l.Skill.SkillId == _selSkill;
                if (h)
                {
                    _rowPlus[r].texture = UiArt.Tex(_collapsed.Contains(l.Category) ? TrUi.PlusUp : TrUi.MinusUp);   // SkillFrame.lua:55-71
                    _rowHeader[r].text = l.Text;
                    continue;
                }
                var s = l.Skill;
                _rowName[r].text = s.Name ?? "";
                bool mono = s.Max <= 1;
                _rowBar[r].SetColor(mono ? GreyBar : Blue);
                _rowBar[r].Set(mono ? 1f : (s.Max > 0 ? (float)s.Value / s.Max : 0f));
                _rowRank[r].text = mono ? "" : RankText(s);
            }
            _up.Interactable = _offset > 0;
            _down.Interactable = _offset + Rows < _lines.Count;
            var sel = Find(_selSkill);
            _detail.gameObject.SetActive(sel != null);
            if (sel == null) return;
            bool m = sel.Max <= 1;
            _detailName.text = sel.Name ?? "";
            _detailRank.text = m ? "" : RankText(sel);
            _detailBar.SetColor(m ? GreyBar : Blue);
            _detailBar.Set(m ? 1f : (sel.Max > 0 ? (float)sel.Value / sel.Max : 0f));
            _unlearn.gameObject.SetActive(sel.Abandonable);
            _detailDesc.text = sel.Description ?? "";
            _detailHint.text = sel.HasTradeWindow ? "Tap the line to open the profession window." : "";   // ours
        }

        /// <summary>"rank/max" or "rank (+mod)/max" with the modifier green / red (SkillFrame.lua:172-185).</summary>
        private static string RankText(SkillView s)
        {
            int mod = s.TempBonus + s.PermBonus;
            if (mod == 0) return s.Value + "/" + s.Max;
            return s.Value + " (" + (mod > 0 ? "<color=#20ff20>+" : "<color=#ff2020>") + mod + "</color>)/" + s.Max;
        }
    }
}
