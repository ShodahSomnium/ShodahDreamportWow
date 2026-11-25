// iteration 11 - owner trainer (proposal-trainer.md 4.2, 5.5): WoW's TradeSkillFrame (Blizzard_TradeSkillUI.xml / .lua of the user's install) -
// the profession window: rank bar, "Have Materials", the recipes coloured by difficulty with "[n]", the detail (icon, made count, tools,
// cooldown, description, reagents have / need), Create / Create All / - count + / Exit. A session window (the game opens it when a profession
// spell is used or a Skills line is tapped): desktop the left panel, VR the world dialog surface in front of the player.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiTradeSkill : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            // UIPanelWindows["TradeSkillFrame"] = { area = "left", pushable = 3 } (Blizzard_TradeSkillUI.lua:22)
            return new UiWindowSpec
            {
                Kind = UiWindowKind.TradeSkill, Frame = "TradeSkillFrame", TitleKey = "TRADE_SKILLS", Title = "Professions",   // GlobalStrings.lua:7649
                Area = UiPanelArea.Left, Pushable = 3, SessionWindow = true, Wanted = s => s.CurrentTradeSkill != null, NearGuid = s => 0UL,
                Closed = s => s.CloseTradeSkill(), Palm = false,
                OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",   // Blizzard_TradeSkillUI.xml:978-985
                Owner = "trainer", Hidden = false,
                Build = (parent, ctx, close) => new UiTradeSkill(parent, ctx, close)
            };
        }

        private const string Ct = @"Interface\ClassTrainerFrame\UI-ClassTrainer-", Ts = @"Interface\TradeSkillFrame\UI-TradeSkill-";
        private const int Rows = 8;                                  // TRADE_SKILLS_DISPLAYED (lua :2)
        private const float RowH = 16f, ListX = 22f, ListY = 96f, RowW = 293f;
        private const float DetailX = 20f, DetailY = 234f, DetailW = 297f, DetailH = 172f;   // TradeSkillDetailScrollFrame 297x176 at (20, -234)
        private const int MaxReagents = 8;
        // TradeSkillTypeColor (Blizzard_TradeSkillUI.lua:15-20)
        private static readonly Color[] Diff = { new Color(1f, 0.5f, 0.25f), new Color(1f, 1f, 0f), new Color(0.25f, 0.75f, 0.25f), new Color(0.5f, 0.5f, 0.5f) };
        private static readonly Color HeaderC = new Color(1f, 0.82f, 0f);

        private sealed class Line { public bool Header; public string Group; public RecipeView Recipe; }

        private readonly Action _close;
        private readonly TextMeshProUGUI _title, _rankText;
        private readonly UiBar _rank;
        private readonly RawImage _botLeft, _botRight;
        private readonly RectTransform _list;
        private readonly UiButton[] _rows = new UiButton[Rows];
        private readonly TextMeshProUGUI[] _rowName = new TextMeshProUGUI[Rows], _rowCount = new TextMeshProUGUI[Rows];
        private readonly RawImage[] _rowSel = new RawImage[Rows], _rowPlus = new RawImage[Rows];
        private readonly UiButton _up, _down, _dUp, _dDown;
        private readonly RectTransform _detail, _scroll;
        private readonly UiButton _icon;
        private readonly TextMeshProUGUI _iconCount, _name, _reqLabel, _reqText, _cooldown, _desc, _reagentLabel;
        private readonly UiButton[] _reagents = new UiButton[MaxReagents];
        private readonly TextMeshProUGUI[] _reagentName = new TextMeshProUGUI[MaxReagents];
        private readonly string[] _reagentPath = new string[MaxReagents];
        private readonly bool[] _reagentLoaded = new bool[MaxReagents];
        private readonly UiButton _create, _createAll, _dec, _inc, _exit;
        private readonly RectTransform _countBox;
        private readonly TextMeshProUGUI _count;
        private readonly bool[] _haveMaterials = { false };

        private readonly List<Line> _lines = new List<Line>();
        private readonly HashSet<string> _collapsed = new HashSet<string>();
        private TradeSkillView _shown;
        private int _shownVersion = -1, _offset, _selSpell, _countValue = 1, _verbLayout = -1;
        private float _detailOffset, _detailContentH;
        private bool _dirty = true, _iconLoaded;
        private string _iconPath;
        private static bool _logged;

        public UiTradeSkill(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "TradeSkillFrame", 0f, 0f, W, H);
            UiKit.Pic(Root, "TopLeft", UiArt.Tex(Ct + "TopLeft.blp"), 0f, 0f, 256f, 256f);
            UiKit.Pic(Root, "TopRight", UiArt.Tex(Ct + "TopRight.blp"), 256f, 0f, 128f, 256f);
            _botLeft = UiKit.Pic(Root, "BotLeft", UiArt.Tex(Ts + "BotLeft.blp"), 0f, 256f, 256f, 256f);
            _botRight = UiKit.Pic(Root, "BotRight", UiArt.Tex(Ct + "BotRight.blp"), 256f, 256f, 128f, 256f);
            Portrait(UiArt.Tex(UiArt.PortraitNpc));
            _title = Title("", 17f);                                                                                   // TRADE_SKILL_TITLE "%s" (GlobalStrings.lua:7650)
            UiKit.CloseButton(Root, 323f, 8f, Close);
            // TradeSkillRankFrame 265x14 at (75, -36): (0, 0, 1, 0.5) on (0, 0, 0.75, 0.5), "rank/maxRank" (xml :228-250, lua :277-281)
            UiKit.Solid(Root, "RankBackground", new Color(0f, 0f, 0.75f, 0.5f), 75f, 36f, 265f, 14f);
            _rank = UiBar.Make(Root, "TradeSkillRankFrame", 75f, 36f, 265f, 14f, new Color(0f, 0f, 1f, 0.5f), false);
            _rankText = UiKit.Text(Root, "RankText", "", 10f, Color.white, TextAlignmentOptions.Center, 75f, 36f, 265f, 14f);
            TrUi.CheckBox(Root, TrUi.G("CRAFT_IS_MAKEABLE", "Have Materials"), 70f, 52f, 150f, () => _haveMaterials[0], v => { _haveMaterials[0] = v; _dirty = true; _offset = 0; });   // (70, -46), GlobalStrings.lua:1869
            UiKit.Pic(Root, "HorizontalBarLeft", UiArt.Tex(Ct + "HorizontalBar.blp"), 0f, 1f, 0f, 0.25f, 15f, 221f, 256f, 16f);                       // (15, -221)
            UiKit.Pic(Root, "HorizontalBarRight", UiArt.Tex(Ct + "HorizontalBar.blp"), 0f, 0.29296875f, 0.25f, 0.5f, 271f, 221f, 75f, 16f);

            _list = UiKit.Rect(Root, "List", ListX - 1f, ListY, 296f, Rows * RowH);
            for (int i = 0; i < Rows; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(_list, "TradeSkillSkill" + (i + 1), 1f, i * RowH, RowW, RowH);
                _rowSel[i] = UiKit.Pic(b.transform, "Selected", UiArt.Tex(TrUi.Highlight2), 0f, 0f, RowW, RowH);
                _rowSel[i].enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(TrUi.Highlight2), 0f, 0f, RowW, RowH);
                b.Hilite.enabled = false;
                _rowPlus[i] = UiKit.Pic(b.transform, "Plus", UiArt.Tex(TrUi.MinusUp), 0f, 0f, 16f, 16f);
                _rowName[i] = UiKit.Text(b.transform, "Text", "", 12f, Diff[0], TextAlignmentOptions.Left, 18f, 0f, 230f, RowH);   // text width 275 (lua :4)
                _rowName[i].textWrappingMode = TextWrappingModes.NoWrap;
                _rowName[i].overflowMode = TextOverflowModes.Ellipsis;
                _rowCount[i] = UiKit.Text(b.transform, "Count", "", 12f, Diff[0], TextAlignmentOptions.Right, 230f, 0f, RowW - 234f, RowH);   // "[n]" (lua :185-199)
                _rowCount[i].textWrappingMode = TextWrappingModes.NoWrap;
                b.Click = () => Press(k);
                b.Tip = () => RowTip(k);
                b.gameObject.SetActive(false);
                _rows[i] = b;
            }
            _up = UiKit.ArtButton(Root, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 320f, 94f, 20f, () => Scroll(-1));
            _down = UiKit.ArtButton(Root, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 320f, 206f, 20f, () => Scroll(1));

            // the detail, clipped and scrolled by its own arrows (TradeSkillDetailScrollFrame)
            _detail = UiKit.Rect(Root, "Detail", DetailX, DetailY, DetailW, DetailH);
            _detail.gameObject.AddComponent<RectMask2D>();
            _scroll = UiKit.Rect(_detail, "Scroll", 0f, 0f, DetailW, DetailH);
            _icon = UiKit.Slot(_scroll, "TradeSkillSkillIcon", 8f, 3f, 37f, false);                                                         // 37x37 at (8, -3)
            _icon.Tip = () => { var r = Selected(); return r == null ? null : r.MadeTooltip ?? r.Tooltip; };   // lead fix 2026-10-11: the made item's tooltip
            _iconCount = UiKit.Text(_icon.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 20f, 35f, 15f, UiArt.Numbers);
            _name = UiKit.Text(_scroll, "TradeSkillSkillName", "", 12f, UiArt.Gold, TextAlignmentOptions.TopLeft, 50f, 5f, 244f, 15f);       // (50, -5) width 244
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _reqLabel = UiKit.Text(_scroll, "RequirementLabel", TrUi.G("REQUIRES_LABEL", "Requires:"), 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 50f, 20f, 60f, 13f);   // GlobalStrings.lua:6084
            _reqText = UiKit.Text(_scroll, "RequirementText", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 104f, 20f, 180f, 26f);       // 180 wide, 4 right of the label
            _cooldown = UiKit.Text(_scroll, "TradeSkillSkillCooldown", "", 10f, UiArt.ErrorRed, TextAlignmentOptions.TopLeft, 50f, 34f, 244f, 13f);   // GameFontRedSmall
            _desc = UiKit.Text(_scroll, "TradeSkillDescription", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 5f, 50f, 290f, 20f);      // (5, -50) width 290
            _reagentLabel = UiKit.Text(_scroll, "TradeSkillReagentLabel", TrUi.G("SPELL_REAGENTS", "Reagents: |n").Trim(), 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 5f, 70f, 200f, 13f);   // GlobalStrings.lua:7238
            for (int i = 0; i < MaxReagents; i++)
            {
                int k = i;
                // QuestItemTemplate 147x41: icon 37, UI-QuestItemNameFrame, name, the count "have /need" (lua :313-341)
                UiButton b = UiKit.Button(_scroll, "TradeSkillReagent" + (i + 1), 0f, 0f, 147f, 41f);
                UiKit.Pic(b.transform, "Name frame", UiArt.Tex(UiArt.QuestItemNameFrame), 30f, -12f, 128f, 64f);
                b.Icon = UiKit.Pic(b.transform, "Icon", null, 0f, 2f, 37f, 37f);
                b.Icon.enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 2f, 37f, 37f);
                b.Hilite.enabled = false;
                b.Count = UiKit.Text(b.transform, "Count", "", 10f, Color.white, TextAlignmentOptions.BottomRight, -10f, 24f, 46f, 14f, UiArt.Numbers);
                _reagentName[i] = UiKit.Text(b.transform, "Name", "", 10f, Color.white, TextAlignmentOptions.Left, 43f, 2f, 100f, 37f);
                b.Tip = () => ReagentTip(k);
                b.gameObject.SetActive(false);
                _reagents[i] = b;
            }
            _dUp = UiKit.ArtButton(Root, "Detail up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 320f, 232f, 20f, () => ScrollDetail(-40f));
            _dDown = UiKit.ArtButton(Root, "Detail down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 320f, 386f, 20f, () => ScrollDetail(40f));

            // the bottom buttons (xml :775-964): Create All 86 left of Create, - 23x22, the count box 30x20, + 23x22, Create (224, -422), Exit (305, -422)
            _createAll = UiKit.PanelButton(Root, TrUi.G("CREATE_ALL", "Create All"), 18f, 411f, 80f, 22f, CreateAllPressed);   // GlobalStrings.lua:1874
            _dec = TextButton("Decrement", "-", 101f, 411f, () => SetCount(_countValue - 1));
            _countBox = UiKit.Rect(Root, "TradeSkillInputBox", 128f, 412f, 30f, 20f);
            UiKit.Solid(_countBox, "Back", new Color(0f, 0f, 0f, 0.6f), 0f, 0f, 30f, 20f);
            _count = UiKit.Text(_countBox, "Text", "1", 11f, Color.white, TextAlignmentOptions.Center, 0f, 0f, 30f, 20f);
            _inc = TextButton("Increment", "+", 158f, 411f, () => SetCount(_countValue + 1));
            _create = UiKit.PanelButton(Root, TrUi.G("CREATE", "Create"), 184f, 411f, 80f, 22f, CreatePressed);                 // GlobalStrings.lua:1871
            _exit = UiKit.PanelButton(Root, TrUi.G("EXIT", "Exit"), 265f, 411f, 80f, 22f, Close);
            Root.gameObject.SetActive(false);
        }

        private UiButton TextButton(string name, string label, float x, float y, Action click)
        {
            UiButton b = UiKit.PanelButton(Root, label, x, y, 23f, 22f, click);   // TradeSkillDecrementButton / IncrementButton 23x22 (ours: panel-button art)
            b.name = name;
            return b;
        }

        protected override void OnOpen()
        {
            _dirty = true;
            _offset = 0;
            _selSpell = 0;
            _countValue = 1;   // the count box starts at 1 on show (xml :978-981)
            _detailOffset = 0f;
            _shown = null;
        }

        private void Close()
        {
            if (_close != null) _close();
            else if (Ctx != null && Ctx.S != null) Ctx.S.CloseTradeSkill();
        }

        private RecipeView Selected()
        {
            var v = _shown;
            if (v == null) return null;
            foreach (var r in v.Recipes) if (r.SpellId == _selSpell) return r;
            return null;
        }

        private string RowTip(int row)
        {
            int i = _offset + row;
            // lead fix 2026-10-11: the line shows the item it makes (its tooltip), else the spell's
            return i >= 0 && i < _lines.Count && !_lines[i].Header ? _lines[i].Recipe.MadeTooltip ?? _lines[i].Recipe.Tooltip : null;
        }

        private string ReagentTip(int k)
        {
            var r = Selected();
            if (r == null || k >= r.Reagents.Count) return null;
            var g = r.Reagents[k];
            return (g.Name ?? "Retrieving item information") + "\n" + g.Have + " / " + g.Need;
        }

        private void Press(int row)
        {
            int i = _offset + row;
            if (i < 0 || i >= _lines.Count) return;
            var l = _lines[i];
            if (l.Header)
            {
                if (!_collapsed.Remove(l.Group)) _collapsed.Add(l.Group);
                _dirty = true;
                return;
            }
            if (_selSpell != l.Recipe.SpellId) { _selSpell = l.Recipe.SpellId; _detailOffset = 0f; }
            UiSound.Named("igMainMenuOptionCheckBoxOn");
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

        private void ScrollDetail(float by)
        {
            float max = Mathf.Max(0f, _detailContentH - DetailH);
            _detailOffset = Mathf.Clamp(_detailOffset + by, 0f, max);
            UiKit.Move(_scroll, 0f, -_detailOffset);
        }

        private void SetCount(int n)
        {
            _countValue = Mathf.Clamp(n, 1, 999);
            _count.text = _countValue.ToString();
        }

        private void CreatePressed()
        {
            var r = Selected();
            if (r == null || Ctx.S == null) return;
            Ctx.S.CraftRecipe(r.SpellId, r.Verb != null ? 1 : _countValue);   // DoTradeSkill(selected, count box) (xml :802)
        }

        private void CreateAllPressed()
        {
            var r = Selected();
            if (r == null || Ctx.S == null || r.Craftable <= 0) return;
            SetCount(r.Craftable);                                              // count = numAvailable (xml :850-851)
            Ctx.S.CraftRecipe(r.SpellId, r.Craftable);
        }

        public override void Refresh(float now)
        {
            var s = Ctx != null ? Ctx.S : null;
            var v = s != null ? s.CurrentTradeSkill : null;
            if (v == null) return;
            if (!ReferenceEquals(v, _shown) || v.Version != _shownVersion)
            {
                if (_shown != null && v.SkillId != _shown.SkillId) { _offset = 0; _selSpell = 0; }
                _shown = v;
                _dirty = true;
            }
            int wheel = TrUi.Wheel(_list, Ctx.Vr);
            if (wheel != 0) Scroll(wheel);
            int dw = TrUi.Wheel(_detail, Ctx.Vr);
            if (dw != 0) ScrollDetail(dw * 40f);
            TypeCount();
            if (_dirty) Rebuild(v);
            var sel = Selected();
            bool queue = v.QueueLeft > 0;
            bool short_ = sel == null || sel.Craftable <= 0;
            _create.Interactable = sel != null && !short_ && !queue;                                // Create disabled when a reagent is short (lua :324-363)
            _createAll.Interactable = sel != null && !short_ && !queue;
            _count.text = queue && sel != null && v.QueueSpellId == sel.SpellId ? v.QueueLeft.ToString() : _countValue.ToString();   // ours: counts down
            if (sel != null) SetIcon(_icon, sel.MadeIconPath ?? sel.IconPath, ref _iconPath, ref _iconLoaded);   // lead fix 2026-10-11: the made item's icon
            if (sel != null)
                for (int i = 0; i < sel.Reagents.Count && i < MaxReagents; i++)
                {
                    var g = sel.Reagents[i];
                    if (string.IsNullOrEmpty(g.IconPath)) continue;
                    if (SetIcon(_reagents[i], g.IconPath, ref _reagentPath[i], ref _reagentLoaded[i]))
                        _reagents[i].Icon.color = g.Have >= g.Need ? Color.white : new Color(0.5f, 0.5f, 0.5f);   // SetItemButtonTextureVertexColor 0.5 when short
                }
        }

        /// <summary>Desktop: digits typed while the mouse is over the count box set it (ours - the count box is not a text field here).</summary>
        private void TypeCount()
        {
            if (Ctx.Vr || _countBox == null) return;
            try
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_countBox, Input.mousePosition, null)) return;
                string typed = Input.inputString;
                if (string.IsNullOrEmpty(typed)) return;
                foreach (char c in typed)
                {
                    if (c == '\b') SetCount(_countValue / 10 > 0 ? _countValue / 10 : 1);
                    else if (c >= '0' && c <= '9') SetCount(_countValue >= 100 ? (c - '0') : _countValue * 10 + (c - '0'));
                }
            }
            catch (Exception) { }
        }

        private void Rebuild(TradeSkillView v)
        {
            _dirty = false;
            _shownVersion = v.Version;
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI profession: TradeSkillFrame built (" + v.Name + ", " + v.Recipes.Count + " recipes; grouped by the made item's subclass, " +
                            "the count box is set with - / + or by typing over it - ours; no search box / subclass dropdowns - known gap)");
            }
            _title.text = TrUi.Fmt(TrUi.G("TRADE_SKILL_TITLE", "%s"), v.Name ?? "");
            _rank.Set(v.MaxRank > 0 ? (float)v.Rank / v.MaxRank : 0f);
            _rankText.text = v.Rank + "/" + v.MaxRank;
            _lines.Clear();
            string group = null;
            foreach (var r in v.Recipes)
            {
                if (_haveMaterials[0] && r.Craftable <= 0) continue;
                string g = r.Group ?? "";
                if (!string.Equals(g, group, StringComparison.Ordinal))
                {
                    group = g;
                    if (g.Length > 0) _lines.Add(new Line { Header = true, Group = g });
                }
                if (g.Length > 0 && _collapsed.Contains(g)) continue;
                _lines.Add(new Line { Group = g, Recipe = r });
            }
            if (Selected() == null || (_haveMaterials[0] && Selected().Craftable <= 0))
            {
                _selSpell = 0;
                foreach (var l in _lines) if (!l.Header) { _selSpell = l.Recipe.SpellId; break; }   // the first recipe is selected (TradeSkillFrame_OnShow)
                _detailOffset = 0f;
            }
            _offset = Mathf.Clamp(_offset, 0, Math.Max(0, _lines.Count - Rows));
            for (int r = 0; r < Rows; r++)
            {
                int i = _offset + r;
                bool on = i < _lines.Count;
                _rows[r].gameObject.SetActive(on);
                if (!on) continue;
                var l = _lines[i];
                _rowPlus[r].enabled = l.Header;
                if (l.Header)
                {
                    _rowPlus[r].texture = UiArt.Tex(_collapsed.Contains(l.Group) ? TrUi.PlusUp : TrUi.MinusUp);   // lua :128-133
                    _rowName[r].text = l.Group;
                    _rowName[r].color = HeaderC;
                    _rowCount[r].text = "";
                    _rowSel[r].enabled = false;
                    continue;
                }
                var rc = l.Recipe;
                Color c = Diff[Mathf.Clamp((int)rc.Difficulty, 0, 3)];
                _rowName[r].text = rc.Name ?? "";
                _rowName[r].color = c;
                _rowCount[r].text = rc.Craftable > 0 ? "[" + rc.Craftable + "]" : "";
                _rowCount[r].color = c;
                bool selected = rc.SpellId == _selSpell;
                _rowSel[r].enabled = selected;
                if (selected) _rowSel[r].color = c;                                                    // the highlight tinted like the line (lua :268-271)
            }
            _up.Interactable = _offset > 0;
            _down.Interactable = _offset + Rows < _lines.Count;
            var sel = Selected();
            _detail.gameObject.SetActive(sel != null);
            LayoutVerb(sel != null ? sel.Verb : null);
            if (sel == null) return;
            _name.text = sel.Name ?? "";
            _iconCount.text = sel.CreatesItem == 0 || sel.MaxMade <= 1 ? "" : sel.MinMade == sel.MaxMade ? sel.MinMade.ToString() : sel.MinMade + "-" + sel.MaxMade;   // lua :289-301
            float y = 20f;
            bool tools = !string.IsNullOrEmpty(sel.Tools);
            _reqLabel.gameObject.SetActive(tools);
            _reqText.gameObject.SetActive(tools);
            if (tools) { _reqText.text = sel.Tools; y += Mathf.Max(14f, _reqText.preferredHeight); }
            bool cd = sel.CooldownRemaining > 0f;
            _cooldown.gameObject.SetActive(cd);
            if (cd)
            {
                UiKit.Move(_cooldown.rectTransform, 50f, y);
                _cooldown.text = TrUi.G("COOLDOWN_REMAINING", "Cooldown remaining:") + " " + CooldownText(sel.CooldownRemaining);   // GlobalStrings.lua:1859
                y += 14f;
            }
            y = Mathf.Max(50f, y + 4f);
            _desc.text = TrUi.Body(sel.Tooltip);
            UiKit.Move(_desc.rectTransform, 5f, y);
            float dh = Mathf.Max(13f, _desc.preferredHeight);
            y += dh + 10f;
            _reagentLabel.gameObject.SetActive(sel.Reagents.Count > 0);
            UiKit.Move(_reagentLabel.rectTransform, 5f, y);
            y += 16f;
            for (int i = 0; i < MaxReagents; i++)
            {
                bool on = i < sel.Reagents.Count;
                _reagents[i].gameObject.SetActive(on);
                if (!on) continue;
                var g = sel.Reagents[i];
                UiKit.Move(_reagents[i].Rt, 3f + (i % 2) * 147f, y + (i / 2) * 43f);
                bool enough = g.Have >= g.Need;
                _reagentName[i].text = g.Name ?? "Retrieving item information";
                _reagentName[i].color = enough ? Color.white : UiArt.Grey;                             // GRAY_FONT_COLOR when short
                _reagents[i].Count.text = (g.Have >= 100 ? "*" : g.Have.ToString()) + " /" + g.Need;   // ">= 100 shows *" (lua :313-341)
                _reagents[i].Count.color = enough ? Color.white : UiArt.Grey;
                if (string.IsNullOrEmpty(g.IconPath)) _reagents[i].Icon.enabled = false;
            }
            _detailContentH = y + ((sel.Reagents.Count + 1) / 2) * 43f + 4f;
            UiKit.Size(_scroll, DetailW, Mathf.Max(DetailH, _detailContentH));
            _detailOffset = Mathf.Clamp(_detailOffset, 0f, Mathf.Max(0f, _detailContentH - DetailH));
            UiKit.Move(_scroll, 0f, -_detailOffset);
            _dUp.Interactable = _detailOffset > 0f;
            _dDown.Interactable = _detailOffset + DetailH < _detailContentH;
        }

        /// <summary>Blizzard_TradeSkillUI.lua:386-411: a recipe that makes no item shows the skill's verb on Create and hides Create All / - / count / +;
        /// the bottom art is then UI-ClassTrainer-BotLeft / -BotRight.</summary>
        private void LayoutVerb(string verb)
        {
            int layout = verb != null ? 1 : 0;
            _create.SetLabel(verb ?? TrUi.G("CREATE", "Create"));
            if (layout == _verbLayout) return;
            _verbLayout = layout;
            bool counts = verb == null;
            _createAll.gameObject.SetActive(counts);
            _dec.gameObject.SetActive(counts);
            _inc.gameObject.SetActive(counts);
            _countBox.gameObject.SetActive(counts);
            _botLeft.texture = UiArt.Tex(counts ? Ts + "BotLeft.blp" : Ct + "BotLeft.blp");
            _botRight.texture = UiArt.Tex(Ct + "BotRight.blp");
        }

        private static string CooldownText(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            if (s >= 86400) return (s / 86400) + " day" + (s / 86400 > 1 ? "s" : "");
            if (s >= 3600) return (s / 3600) + " hr";
            if (s >= 60) return (s / 60) + " min";
            return s + " sec";
        }
    }
}
