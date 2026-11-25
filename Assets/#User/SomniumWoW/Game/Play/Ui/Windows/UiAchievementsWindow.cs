// iteration 11 - owner character (proposal-talents.md 4.2 / 6.1): WoW's AchievementFrame (key Y, micro button "Achievement").
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// AchievementFrame (Blizzard_AchievementUI.xml:2039-3300 / .lua, dumps in P/harness/out_d11talents): 768x500, the tutorial-parchment
    /// background, metal borders and wood corners, the header above the frame (UI-Achievement-Header, "Achievement Points" + the total with the
    /// tiny shield), the categories column (175 wide at (21, -19): "Summary", the top categories, the selected one's children) and the
    /// achievements list (504x440 next to it, 496-wide buttons, the selected one expanded with its criteria) or the Summary (recent
    /// achievements + progress overview bars). Data: Achievement / Achievement_Category / Achievement_Criteria from the install (AchievementDb),
    /// progress from the session (PlayerState.Achievements, online). Ours: the filter is a cycling button inside the frame (WoW: a dropdown in
    /// the header), series show their next step only, earned ones first; the Statistics tab is a known gap.
    /// </summary>
    public sealed class UiAchievementsWindow : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Achievements, Frame = "AchievementFrame", TitleKey = "ACHIEVEMENT_BUTTON", Title = "Achievements",   // GlobalStrings.lua:23
                Area = UiPanelArea.Doublewide, Pushable = 0, Width = 840f, Height = 500f, XOffset = 80f, WhileDead = true, Key = KeyCode.Y,
                Binding = "TOGGLEACHIEVEMENT", Micro = "Achievement", MicroOrder = 3, OpenSound = "AchievementMenuOpen", CloseSound = "AchievementMenuClose",
                Owner = "character", Hidden = false,   // built (iteration 11, character)
                Build = (parent, ctx, close) => new UiAchievementsWindow(parent, ctx, close)
            };
        }

        const string A = @"Interface\AchievementFrame\";
        const float CatX = 21f, CatY = 19f, CatW = 175f, CatButtonH = 24f;   // AchievementFrameCategories (21, -19) 175 wide; buttons 24 tall
        const int CatButtons = 18;
        const float ListX = 218f, ListY = 19f, ListW = 504f, ListH = 440f;   // AchievementFrameAchievements 504x440 22 right of the categories
        const int ListButtons = 6;
        const int FilterAll = 1, FilterEarned = 2, FilterIncomplete = 3;      // ACHIEVEMENT_FILTER_* (.lua:43-45)

        readonly UiAchCategoryButton[] _cats = new UiAchCategoryButton[CatButtons];
        readonly UiAchButton[] _buttons = new UiAchButton[ListButtons];
        readonly RectTransform _listFrame, _summary, _catFrame;
        readonly UiButton _listUp, _listDown, _catUp, _catDown, _filter;
        readonly TextMeshProUGUI _points, _empty;
        TextMeshProUGUI _summaryEmpty, _totalTitle;   // built in BuildSummary
        UiAchBar _total;
        readonly UiAchBar[] _catBars = new UiAchBar[8];
        readonly TextMeshProUGUI[] _catBarNames = new TextMeshProUGUI[8];
        readonly RectTransform[] _recent = new RectTransform[4];
        readonly RawImage[] _recentIcon = new RawImage[4];
        readonly TextMeshProUGUI[] _recentName = new TextMeshProUGUI[4], _recentDate = new TextMeshProUGUI[4], _recentPts = new TextMeshProUGUI[4];
        readonly int[] _recentId = new int[4];
        readonly List<KeyValuePair<int, bool>> _catLines = new List<KeyValuePair<int, bool>>();   // category id, is child; 0 = Summary
        readonly List<AchievementInfo> _list = new List<AchievementInfo>();
        int _category, _expandedTop = -1, _catTop, _listTop, _filterMode = FilterAll, _expandedAch;
        int _shownCounter = -1, _shownCategory = -1, _shownFilter = -1, _shownListTop = -1, _shownCatTop = -1, _shownExpanded = -1;
        bool _dirty = true;
        static bool _orderLogged;

        public UiAchievementsWindow(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            W = 768f; H = 500f;
            Root = UiKit.Rect(parent, "AchievementFrame", 0f, 0f, W, H);
            // background + borders (Blizzard_AchievementUI.xml:2039-2240)
            UiKit.Pic(Root, "Background", UiArt.Tex(@"Interface\TutorialFrame\TutorialFrameBackground.blp"), 16f, 16f, W - 32f, H - 32f);
            UiKit.Pic(Root, "MetalBorderLeft", UiArt.Tex(A + "UI-Achievement-MetalBorder-Left.blp"), 0f, 1f, 0f, 0.87f, 14f, (H - 436f) * 0.5f, 16f, 436f);
            UiKit.Pic(Root, "MetalBorderRight", UiArt.Tex(A + "UI-Achievement-MetalBorder-Left.blp"), 1f, 0f, 0.87f, 0f, W - 13f - 16f, (H - 436f) * 0.5f, 16f, 436f);
            UiKit.Pic(Root, "MetalBorderTop", UiArt.Tex(A + "UI-Achievement-MetalBorder-Top.blp"), 0.87f, 0f, 0f, 1f, 28f, 12f, W - 56f, 16f);
            UiKit.Pic(Root, "MetalBorderBottom", UiArt.Tex(A + "UI-Achievement-MetalBorder-Top.blp"), 0f, 0.87f, 1f, 0f, 28f, H - 13f - 16f, W - 56f, 16f);
            UiKit.Pic(Root, "CategoriesBG", UiArt.Tex(A + "UI-Achievement-Parchment.blp"), 25f, 23f, 195f, H - 46f);
            UiKit.Pic(Root, "WaterMark", UiArt.Tex(A + "UI-Achievement-AchievementWatermark.blp"), 25f, H - 23f - 256f, 195f, 256f);
            Texture2D joint = UiArt.Tex(A + "UI-Achievement-MetalBorder-Joint.blp");
            UiKit.Pic(Root, "JointTL", joint, 1f, 0f, 1f, 0f, 9f, 7f, 32f, 32f);
            UiKit.Pic(Root, "JointTR", joint, 0f, 1f, 1f, 0f, W - 8f - 32f, 7f, 32f, 32f);
            UiKit.Pic(Root, "JointBL", joint, 1f, 0f, 0f, 1f, 9f, H - 8f - 32f, 32f, 32f);
            UiKit.Pic(Root, "JointBR", joint, 0f, 1f, 0f, 1f, W - 8f - 32f, H - 8f - 32f, 32f, 32f);
            Texture2D wood = UiArt.Tex(A + "UI-Achievement-WoodBorder-Corner.blp");
            UiKit.Pic(Root, "WoodTL", wood, 0f, 1f, 0f, 1f, 4f, 2f, 64f, 64f);
            UiKit.Pic(Root, "WoodTR", wood, 1f, 0f, 0f, 1f, W - 4f - 64f, 2f, 64f, 64f);
            UiKit.Pic(Root, "WoodBL", wood, 0f, 1f, 1f, 0f, 4f, H - 3f - 64f, 64f, 64f);
            UiKit.Pic(Root, "WoodBR", wood, 1f, 0f, 1f, 0f, W - 4f - 64f, H - 3f - 64f, 64f, 64f);
            // the header: BOTTOMLEFT on the frame's TOPLEFT (26, -39), 726x106 - it stands above the frame
            Texture2D header = UiArt.Tex(A + "UI-Achievement-Header.blp");
            UiKit.Pic(Root, "HeaderLeft", header, 0f, 1f, 0f, 0.4140625f, 26f, 39f - 106f, 512f, 106f);
            UiKit.Pic(Root, "HeaderRight", header, 0f, 0.419921875f, 0.4140625f, 0.8046875f, 26f + 512f, 45f - 100f, 215f, 100f);
            float pointCx = 26f + 726f * 0.5f + 20f;                            // PointBorder: BOTTOM of the header (20, 16), 133x39
            UiKit.Pic(Root, "PointBorder", header, 0.419921875f, 0.6796875f, 0.4140625f, 0.56640625f, pointCx - 66.5f, 39f - 16f - 39f, 133f, 39f);
            TextMeshProUGUI title = UiKit.Text(Root, "HeaderTitle", UiGlobalStrings.G("ACHIEVEMENT_TITLE", "Achievement Points"), 12f, UiArt.Gold, TextAlignmentOptions.Center, pointCx - 100f, 39f - 16f - 39f - 7f, 200f, 12f);   // GlobalStrings.lua:27
            title.textWrappingMode = TextWrappingModes.NoWrap;
            _points = UiKit.Text(Root, "HeaderPoints", "0", 12f, Color.white, TextAlignmentOptions.Center, pointCx - 60f, 39f - 16f - 39f + 13f, 120f, 14f);
            _points.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Pic(Root, "HeaderShield", UiArt.Tex(A + "UI-Achievement-TinyShield.blp"), 0f, 0.625f, 0f, 0.625f, pointCx + 22f, 39f - 16f - 39f + 10f, 20f, 20f);
            UiKit.CloseButton(Root, W + 3f - 32f, -4f, close);                 // TOPRIGHT (3, 4)
            // categories
            _catFrame = UiKit.Rect(Root, "AchievementFrameCategories", CatX, CatY, CatW, H - CatY - 20f);
            for (int i = 0; i < CatButtons; i++)
            {
                var b = new UiAchCategoryButton(Root, i);
                int k = i;
                b.B.Click = () => CategoryClick(k);
                b.Hide();
                _cats[i] = b;
            }
            _catUp = UiKit.ArtButton(Root, "CategoriesUp", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, CatX + CatW - 18f, CatY + 2f, 16f, () => ScrollCats(-3));
            _catDown = UiKit.ArtButton(Root, "CategoriesDown", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, CatX + CatW - 18f, H - 20f - 18f, 16f, () => ScrollCats(3));
            // the achievements list
            _listFrame = UiKit.Rect(Root, "AchievementFrameAchievements", ListX, ListY, ListW, ListH);
            UiKit.Pic(_listFrame, "Background", UiArt.Tex(A + "UI-Achievement-AchievementBackground.blp"), 3f, 3f, ListW - 6f, ListH - 6f);
            for (int i = 0; i < ListButtons; i++)
            {
                var b = new UiAchButton(_listFrame, i);
                int k = i;
                b.B.Click = () => AchievementClick(k);
                b.B.Tip = () => AchievementTip(k);
                b.Hide();
                _buttons[i] = b;
            }
            _listUp = UiKit.ArtButton(Root, "AchievementsUp", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, ListX + ListW + 2f, ListY + 4f, 20f, () => ScrollList(-1));
            _listDown = UiKit.ArtButton(Root, "AchievementsDown", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, ListX + ListW + 2f, ListY + ListH - 24f, 20f, () => ScrollList(1));
            _empty = UiKit.Text(_listFrame, "Empty", "", 12f, Color.white, TextAlignmentOptions.Center, 30f, 160f, ListW - 60f, 120f);
            // filter (ours: inside the frame, cycles All / Earned / Incomplete - ACHIEVEMENTFRAME_FILTER_* GlobalStrings.lua:15-17)
            _filter = UiKit.PanelButton(Root, FilterText(), ListX + ListW - 150f, 0f, 150f, 20f, CycleFilter);
            // the summary (529x461 on the categories' TOPRIGHT)
            _summary = UiKit.Rect(Root, "AchievementFrameSummary", CatX + CatW, CatY, 529f, 461f);
            UiKit.Pic(_summary, "Background", UiArt.Tex(A + "UI-Achievement-AchievementBackground.blp"), 3f, 3f, 523f, 455f);
            BuildSummary();
            Root.gameObject.SetActive(false);
        }

        void BuildSummary()
        {
            // AchievementFrameSummaryAchievements (5, -20) 210 tall with its header (UI-Achievement-RecentHeader, LATEST_UNLOCKED_ACHIEVEMENTS)
            UiKit.Pic(_summary, "RecentHeader", UiArt.Tex(A + "UI-Achievement-RecentHeader.blp"), 5f - 20f, 20f, 519f + 40f, 20f);
            UiKit.Text(_summary, "RecentTitle", UiGlobalStrings.G("LATEST_UNLOCKED_ACHIEVEMENTS", "Recent Achievements"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 5f, 20f, 519f, 20f);   // GlobalStrings.lua:4680
            for (int i = 0; i < 4; i++)
            {
                RectTransform r = UiKit.Rect(_summary, "Recent" + (i + 1), 15f, 46f + i * 46f, 499f, 42f);
                UiKit.Pic(r, "Background", UiArt.Tex(A + "UI-Achievement-Parchment-Horizontal.blp"), 0f, 0f, 499f, 42f);
                _recentIcon[i] = UiKit.Pic(r, "Icon", null, 6f, 4f, 34f, 34f);
                _recentName[i] = UiKit.Text(r, "Name", "", 13f, Color.black, TextAlignmentOptions.MidlineLeft, 50f, 0f, 300f, 42f, null, false);
                _recentName[i].textWrappingMode = TextWrappingModes.NoWrap;
                _recentDate[i] = UiKit.Text(r, "Date", "", 11f, Color.black, TextAlignmentOptions.MidlineRight, 499f - 160f, 0f, 90f, 42f, null, false);
                _recentPts[i] = UiKit.Text(r, "Points", "", 13f, Color.black, TextAlignmentOptions.MidlineRight, 499f - 60f, 0f, 50f, 42f, null, false);
                r.gameObject.SetActive(false);
                _recent[i] = r;
            }
            _summaryEmpty = UiKit.Text(_summary, "EmptyText", "", 12f, Color.white, TextAlignmentOptions.Top, 5f, 50f, 519f, 40f);
            // AchievementFrameSummaryCategories: 8 below the achievements, header ACHIEVEMENT_CATEGORY_PROGRESS + "Achievements Earned" + the categories
            float y = 20f + 210f + 8f;
            UiKit.Pic(_summary, "ProgressHeader", UiArt.Tex(A + "UI-Achievement-RecentHeader.blp"), 5f - 20f, y, 519f + 40f, 20f);
            UiKit.Text(_summary, "ProgressTitle", UiGlobalStrings.G("ACHIEVEMENT_CATEGORY_PROGRESS", "Progress Overview"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 5f, y, 519f, 20f);   // GlobalStrings.lua:24
            _totalTitle = UiKit.Text(_summary, "TotalTitle", UiGlobalStrings.G("ACHIEVEMENTS_COMPLETED", "Achievements Earned"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 5f, y + 26f, 519f, 14f);   // :19
            _total = new UiAchBar(_summary, "Total", 40f, y + 44f, 449f);
            for (int i = 0; i < 8; i++)
            {
                float cx = i % 2 == 0 ? 30f : 280f, cy = y + 72f + (i / 2) * 36f;
                _catBarNames[i] = UiKit.Text(_summary, "CategoryName" + i, "", 11f, UiArt.Gold, TextAlignmentOptions.BottomLeft, cx, cy, 220f, 14f);
                _catBarNames[i].textWrappingMode = TextWrappingModes.NoWrap;
                _catBars[i] = new UiAchBar(_summary, "Category" + i, cx, cy + 16f, 220f);
            }
        }

        // ------------------------------------------------------------------ data

        AchievementDb Db { get { return UiArt.Files != null ? AchievementDb.Get(UiArt.Files) : null; } }

        static bool Done(AchievementsView v, int id) { return v != null && v.Completed.ContainsKey(id); }

        /// <summary>Is this achievement listed for the player at all (faction, hidden, Feats of Strength only when earned)?</summary>
        static bool Listed(AchievementInfo a, AchievementsView v, WowFaction f, int categoryId)
        {
            if (!AchievementDb.ForFaction(a, f)) return false;
            bool done = Done(v, a.Id);
            if (a.Hidden && !done) return false;
            if (categoryId == AchievementDb.FeatsOfStrengthCategory && !done) return false;
            return true;
        }

        void BuildList(PlayerState p)
        {
            _list.Clear();
            AchievementDb db = Db;
            if (db == null || !db.Available || _category <= 0) return;
            AchievementCategoryInfo cat = db.Category(_category);
            if (cat == null) return;
            AchievementsView v = p.Achievements;
            var done = new List<AchievementInfo>();
            var todo = new List<AchievementInfo>();
            for (int i = 0; i < cat.Achievements.Count; i++)
            {
                AchievementInfo a = cat.Achievements[i];
                if (!Listed(a, v, p.Faction, cat.Id)) continue;
                bool d = Done(v, a.Id);
                // a series shows one step (ours, WoW shows the next step with the earlier ones as its progressive icons)
                if (a.Previous > 0 && db.Find(a.Previous) != null && !Done(v, a.Previous)) continue;
                AchievementInfo next = a.Next != 0 ? db.Find(a.Next) : null;
                if (d && next != null && next.Category == a.Category && AchievementDb.ForFaction(next, p.Faction) && (_filterMode != FilterEarned || Done(v, next.Id))) continue;
                if (_filterMode == FilterEarned && !d) continue;
                if (_filterMode == FilterIncomplete && d) continue;
                (d ? done : todo).Add(a);
            }
            _list.AddRange(done);
            _list.AddRange(todo);
            if (!_orderLogged)
            {
                _orderLogged = true;
                WowLog.Info("UI: achievement list order is ours (earned first, then Achievement.dbc order; a series shows its next step)");
            }
        }

        void BuildCategoryLines()
        {
            _catLines.Clear();
            _catLines.Add(new KeyValuePair<int, bool>(0, false));
            AchievementDb db = Db;
            if (db == null || !db.Available) return;
            for (int i = 0; i < db.TopCategories.Count; i++)
            {
                AchievementCategoryInfo c = db.TopCategories[i];
                _catLines.Add(new KeyValuePair<int, bool>(c.Id, false));
                if (c.Id != _expandedTop) continue;
                for (int k = 0; k < c.Children.Count; k++) _catLines.Add(new KeyValuePair<int, bool>(c.Children[k].Id, true));
            }
        }

        /// <summary>(earned, total) over a category and its children (Feats of Strength and hidden-unearned left out).</summary>
        void Count(AchievementCategoryInfo c, AchievementsView v, WowFaction f, ref int earned, ref int total)
        {
            if (c == null || c.Id == AchievementDb.FeatsOfStrengthCategory) return;
            for (int i = 0; i < c.Achievements.Count; i++)
            {
                AchievementInfo a = c.Achievements[i];
                if (!Listed(a, v, f, c.Id)) continue;
                total++;
                if (Done(v, a.Id)) earned++;
            }
            for (int i = 0; i < c.Children.Count; i++) Count(c.Children[i], v, f, ref earned, ref total);
        }

        // ------------------------------------------------------------------ input

        void CategoryClick(int k)
        {
            int id = _cats[k].CategoryId;
            int idx = _catTop + k;
            bool child = idx < _catLines.Count && _catLines[idx].Value;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            if (id == 0) { _category = 0; _expandedTop = -1; }
            else if (!child)
            {
                // AchievementFrameCategories_SelectButton: a top category opens its children (and closes the open one when clicked again)
                _expandedTop = _expandedTop == id && _category == id ? -1 : id;
                _category = id;
            }
            else _category = id;
            _listTop = 0;
            _expandedAch = 0;
            _dirty = true;
        }

        void AchievementClick(int k)
        {
            AchievementInfo a = _buttons[k].A;
            if (a == null) return;
            _expandedAch = _expandedAch == a.Id ? 0 : a.Id;   // AchievementButton_OnClick: select / collapse
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            _dirty = true;
        }

        string AchievementTip(int k)
        {
            AchievementInfo a = _buttons[k].A;
            if (a == null) return null;
            PlayerState p = Ctx.P;
            long when = 0;
            bool done = p != null && p.Achievements.Completed.TryGetValue(a.Id, out when);
            if (!done) when = 0;
            // ACHIEVEMENT_TOOLTIP_COMPLETE / _IN_PROGRESS (GlobalStrings.lua:28-29)
            return "<size=14>" + a.Name + "</size>\n" + a.Description + "\n" + (done ? "<color=#00ff00>Achievement earned by " + (p.Name ?? "") + " on " + AchievementDb.ShortDate(when) + "</color>"
                                                                            : "<color=#a0a0a0>Achievement in progress by " + (p != null ? p.Name : "") + "</color>");
        }

        void CycleFilter()
        {
            _filterMode = _filterMode == FilterAll ? FilterEarned : _filterMode == FilterEarned ? FilterIncomplete : FilterAll;
            _filter.SetLabel(FilterText());
            _listTop = 0;
            _dirty = true;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
        }

        string FilterText()
        {
            return _filterMode == FilterEarned ? UiGlobalStrings.G("ACHIEVEMENTFRAME_FILTER_COMPLETED", "Earned")
                 : _filterMode == FilterIncomplete ? UiGlobalStrings.G("ACHIEVEMENTFRAME_FILTER_INCOMPLETE", "Incomplete")
                 : UiGlobalStrings.G("ACHIEVEMENTFRAME_FILTER_ALL", "All");
        }

        void ScrollCats(int by)
        {
            int t = Mathf.Clamp(_catTop + by, 0, Mathf.Max(0, _catLines.Count - CatButtons));
            if (t != _catTop) { _catTop = t; _dirty = true; }
        }

        void ScrollList(int by)
        {
            int t = Mathf.Clamp(_listTop + by, 0, Mathf.Max(0, _list.Count - 1));
            if (t != _listTop) { _listTop = t; _dirty = true; }
        }

        // ------------------------------------------------------------------ refresh

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            float wl = UiCharacterText.Wheel(_listFrame, Ctx.Vr);
            if (wl > 0.01f) ScrollList(-1); else if (wl < -0.01f) ScrollList(1);
            float wc = UiCharacterText.Wheel(_catFrame, Ctx.Vr);
            if (wc > 0.01f) ScrollCats(-1); else if (wc < -0.01f) ScrollCats(1);
            AchievementsView v = p.Achievements;
            if (v.Counter != _shownCounter) { _shownCounter = v.Counter; _dirty = true; }
            if (!_dirty && _shownCategory == _category && _shownFilter == _filterMode && _shownListTop == _listTop && _shownCatTop == _catTop && _shownExpanded == _expandedAch)
            {
                if (_category == 0) RefreshRecentIcons(p);
                else for (int i = 0; i < ListButtons; i++) if (_buttons[i].IconPending && _buttons[i].B.gameObject.activeSelf) RefreshIconOnly(i);
                return;
            }
            _dirty = false;
            _shownCategory = _category; _shownFilter = _filterMode; _shownListTop = _listTop; _shownCatTop = _catTop; _shownExpanded = _expandedAch;
            _points.text = v.Points.ToString();
            DrawCategories();
            bool summary = _category == 0;
            _summary.gameObject.SetActive(summary);
            _listFrame.gameObject.SetActive(!summary);
            _filter.gameObject.SetActive(!summary);
            bool online = Ctx.S is OnlineSession;
            AchievementDb db = Db;
            if (summary) { DrawSummary(p, db, online); _listUp.gameObject.SetActive(false); _listDown.gameObject.SetActive(false); return; }
            BuildList(p);
            _listTop = Mathf.Clamp(_listTop, 0, Mathf.Max(0, _list.Count - 1));
            float y = 3f, bottom = ListH - 5f;
            int used = 0;
            for (int i = _listTop; i < _list.Count && used < ListButtons && y < bottom - 20f; i++)
            {
                float h = _buttons[used].Show(_list[i], v, db, _list[i].Id == _expandedAch, y);
                y += h + 2f;
                used++;
            }
            for (int i = used; i < ListButtons; i++) _buttons[i].Hide();
            bool scrolls = _list.Count > 1 && (_listTop > 0 || used < _list.Count - _listTop);
            _listUp.gameObject.SetActive(scrolls); _listDown.gameObject.SetActive(scrolls);
            _listUp.Interactable = _listTop > 0; _listDown.Interactable = _listTop + used < _list.Count;
            if (!online) _empty.text = UiCharacterText.OnlineOnly;
            else if (_list.Count == 0 && _category == AchievementDb.FeatsOfStrengthCategory)
                _empty.text = UiGlobalStrings.G("FEAT_OF_STRENGTH_DESCRIPTION", "Feats of Strength are accomplishments that players will find very difficult if not impossible to earn.  Worth no points, Feats of Strength are a collection of past glories on Azeroth.");   // GlobalStrings.lua:3674
            else _empty.text = "";
        }

        void RefreshIconOnly(int i)
        {
            // icons load on a pool thread: re-ask until each is in (UiArt.Icon returns null while loading, nothing is allocated meanwhile)
            UiAchButton b = _buttons[i];
            if (b.A == null) return;
            b.Show(b.A, Ctx.P.Achievements, Db, b.A.Id == _expandedAch, -b.B.Rt.anchoredPosition.y);
        }

        void DrawCategories()
        {
            BuildCategoryLines();
            _catTop = Mathf.Clamp(_catTop, 0, Mathf.Max(0, _catLines.Count - CatButtons));
            AchievementDb db = Db;
            for (int i = 0; i < CatButtons; i++)
            {
                int idx = _catTop + i;
                if (idx >= _catLines.Count) { _cats[i].Hide(); continue; }
                int id = _catLines[idx].Key;
                bool child = _catLines[idx].Value;
                string name = id == 0 ? UiGlobalStrings.G("ACHIEVEMENT_SUMMARY_CATEGORY", "Summary") : (db != null && db.Category(id) != null ? db.Category(id).Name : "?");   // GlobalStrings.lua:26
                _cats[i].Show(id, name, child, id == _category, CatX + 5f, CatY + 5f + i * CatButtonH);
            }
            bool scrolls = _catLines.Count > CatButtons;
            _catUp.gameObject.SetActive(scrolls); _catDown.gameObject.SetActive(scrolls);
            _catUp.Interactable = _catTop > 0; _catDown.Interactable = _catTop < _catLines.Count - CatButtons;
        }

        void DrawSummary(PlayerState p, AchievementDb db, bool online)
        {
            AchievementsView v = p.Achievements;
            // the last 4 earned (ACHIEVEMENTUI_MAX_SUMMARY_ACHIEVEMENTS), newest first
            var recent = new List<KeyValuePair<int, long>>(v.Completed);
            recent.Sort((x, y) => y.Value != x.Value ? y.Value.CompareTo(x.Value) : y.Key.CompareTo(x.Key));
            int shown = 0;
            for (int i = 0; i < recent.Count && shown < 4; i++)
            {
                AchievementInfo a = db != null ? db.Find(recent[i].Key) : null;
                if (a == null || a.Counter) continue;
                _recentId[shown] = a.Id;
                _recentName[shown].text = a.Name;
                _recentDate[shown].text = AchievementDb.ShortDate(recent[i].Value);
                _recentPts[shown].text = a.Points > 0 ? a.Points.ToString() : "";
                _recentIcon[shown].texture = UiArt.Icon(a.IconPath);
                _recent[shown].gameObject.SetActive(true);
                shown++;
            }
            for (int i = shown; i < 4; i++) { _recentId[i] = 0; _recent[i].gameObject.SetActive(false); }
            _summaryEmpty.text = !online ? UiCharacterText.OnlineOnly : shown == 0 ? UiGlobalStrings.G("NO_COMPLETED_ACHIEVEMENTS", "You have not earned any achievements recently") : "";   // GlobalStrings.lua:5199
            int earned = 0, total = 0;
            if (db != null)
                for (int i = 0; i < db.TopCategories.Count; i++) Count(db.TopCategories[i], v, p.Faction, ref earned, ref total);
            _total.Set(earned, Math.Max(1, total));
            int bar = 0;
            if (db != null)
                for (int i = 0; i < db.TopCategories.Count && bar < 8; i++)
                {
                    AchievementCategoryInfo c = db.TopCategories[i];
                    if (c.Id == AchievementDb.FeatsOfStrengthCategory) continue;
                    int e = 0, t = 0;
                    Count(c, v, p.Faction, ref e, ref t);
                    if (t == 0) continue;
                    _catBarNames[bar].text = c.Name;
                    _catBars[bar].Set(e, t);
                    _catBars[bar].Root.gameObject.SetActive(true);
                    bar++;
                }
            for (int i = bar; i < 8; i++) { _catBarNames[i].text = ""; _catBars[i].Root.gameObject.SetActive(false); }
        }

        void RefreshRecentIcons(PlayerState p)
        {
            AchievementDb db = Db;
            if (db == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (_recentId[i] == 0 || _recentIcon[i].texture != null) continue;
                AchievementInfo a = db.Find(_recentId[i]);
                if (a != null) _recentIcon[i].texture = UiArt.Icon(a.IconPath);
            }
        }

        protected override void OnOpen()
        {
            _dirty = true;
        }
    }
}
