// iteration 11 - owner quests (proposal-quests.md 7.7.4): WoW's WatchFrame (the quest tracker) + the client-side quest watch list.
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Game.MapApp;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// WoW's quest watch list, client-side as in 3.3.5 (no opcode): in memory for the play session, never written to disk (REQUEST.md C
    /// privacy). Auto-watch on QuestAccepted (AUTO_QUEST_WATCH, QuestLogFrame.lua:260-264) and on QuestProgress (AUTO_QUEST_PROGRESS :265-271 -
    /// ours: kept, not the 300 s of MAX_QUEST_WATCH_TIME), MAX_WATCHABLE_QUESTS 25 (:5); at the session start every quest of the log is
    /// watched (ours: nothing persists). A quest the player stopped tracking is not auto-watched again this session (ours).
    /// </summary>
    public static class QuestWatch
    {
        public const int Max = 25;
        private static readonly List<int> Watched = new List<int>();
        private static readonly HashSet<int> Stopped = new HashSet<int>();
        private static bool _seeded;
        public static int Serial { get; private set; }
        /// <summary>The quest the quest log should select and show (the tracker's title click); 0 = none. UiQuestLog consumes it (hook H-U2).</summary>
        public static int ShowQuestId;
        public static int ShowSerial { get; private set; }

        public static IReadOnlyList<int> List { get { return Watched; } }
        public static bool IsWatched(int questId) { return Watched.Contains(questId); }

        /// <summary>Track / Untrack (the quest log's Track button). False + QUEST_WATCH_TOO_MANY when 25 are watched.</summary>
        public static bool Toggle(int questId, UiContext ctx)
        {
            if (Watched.Remove(questId)) { Stopped.Add(questId); Serial++; return true; }
            if (Watched.Count >= Max) { if (ctx != null) ctx.Say(UiGlobalStrings.FormatOr("QUEST_WATCH_TOO_MANY", "You may only watch %d quests at a time.", Max), UiArt.ErrorRed); return false; }   // GS:5912
            Stopped.Remove(questId);
            Watched.Add(questId);
            Serial++;
            return true;
        }

        public static void Unwatch(int questId) { if (Watched.Remove(questId)) { Stopped.Add(questId); Serial++; } }

        public static void RequestShow(int questId) { ShowQuestId = questId; ShowSerial++; }

        public static void Reset() { Watched.Clear(); Stopped.Clear(); _seeded = false; ShowQuestId = 0; Serial++; }

        private static ulong _character;

        /// <summary>Every frame: the first log seen is watched whole; quests that left the log are dropped. Another character = a fresh list.</summary>
        public static void Sync(PlayerState p)
        {
            if (p == null) return;
            if (p.Guid != 0 && p.Guid != _character) { if (_character != 0) Reset(); _character = p.Guid; }
            if (!_seeded && p.QuestLog.Count > 0)
            {
                _seeded = true;
                for (int i = 0; i < p.QuestLog.Count && Watched.Count < Max; i++) if (!Watched.Contains(p.QuestLog[i].Id)) Watched.Add(p.QuestLog[i].Id);
                Serial++;
                WowLog.Info("UI: quest tracker - watching the " + Watched.Count + " quests of the log (in memory only, ours).");
            }
            for (int i = Watched.Count - 1; i >= 0; i--)
            {
                bool inLog = false;
                for (int k = 0; k < p.QuestLog.Count; k++) if (p.QuestLog[k].Id == Watched[i]) { inLog = true; break; }
                if (!inLog && _seeded) { Watched.RemoveAt(i); Serial++; }
            }
        }

        public static void OnEvent(GameEvent e)
        {
            if (e.QuestId <= 0) return;
            if ((e.Type == GameEventType.QuestAccepted || e.Type == GameEventType.QuestProgress) && !Watched.Contains(e.QuestId) && !Stopped.Contains(e.QuestId) && Watched.Count < Max)
            {
                Watched.Add(e.QuestId);
                Serial++;
            }
        }
    }

    /// <summary>
    /// WoW's WatchFrame (proposal-quests.md section 5): "Objectives (n)", the collapse button, "Quest Timers", per watched quest the title (gold
    /// 0.75, 0.61, 0) and its unfinished objectives reversed ("3/10 Kobold Vermin slain"), or the completion text; the quest item button (26 x 26,
    /// UI-Quickslot2); title click = the quest log at that quest, right-click (desktop) = Open Quest Details / Stop Tracking / Share Quest.
    /// Desktop: inside the "Objectives" anchor (TOPRIGHT (0, -172), 204 wide). VR: its own world surface right of the floating HUD (ours).
    /// Also shows the party-accept QUEST_ACCEPT question (Player.PendingQuestConfirm) through uicore's UiPopup.
    /// </summary>
    public sealed class UiQuestTracker
    {
        public const float Width = 204f;                                     // WATCHFRAME_EXPANDEDWIDTH (WatchFrame.lua:4)
        public const float LineH = 16f, WrapH = 29f, QuestGap = 10f, TypeGap = 10f, ItemW = 33f;   // WatchFrame.lua:5-18
        public const float LinesTop = 30f, LinesRight = 24f, ItemSize = 26f;                       // WatchFrame.xml:197-205, :21-22
        public const int MaxLines = 48, MaxItems = 10;                       // ours: pools
        public const float VrMetresPerUnit = 0.0007f, VrDistance = 1f, VrYaw = 24f, VrPitch = 10f, VrFollow = 5f, VrHeight = 360f;   // ours
        private static readonly Color TitleColor = new Color(0.75f, 0.61f, 0f);   // WatchFrame.lua:585

        public UiSurface Surface;
        private readonly UiContext _ctx;
        private readonly bool _vr;
        private readonly RectTransform _root, _lines;
        private readonly TextMeshProUGUI _title;
        private readonly UiButton _collapse;
        private readonly UiButton[] _line = new UiButton[MaxLines];
        private readonly TextMeshProUGUI[] _lineText = new TextMeshProUGUI[MaxLines];
        private readonly int[] _lineQuest = new int[MaxLines];
        private readonly UiButton[] _item = new UiButton[MaxItems];
        private readonly int[] _itemQuest = new int[MaxItems];
        private readonly string[] _itemPath = new string[MaxItems];
        // iteration 11 quest map: the quest POI buttons left of the titles (WatchFrame.lua:901-918; QuestPOI_DisplayButton "WatchFrameLines":
        // "poiButton:SetScale(0.9)", "SetPoint("TOPRIGHT", questTitle, "TOPLEFT", 0, 5)", OnClick WatchFrameQuestPOI_OnClick)
        public const int MaxPoi = 25;                                        // ours: pool (MAX_WATCHABLE_QUESTS)
        public const float PoiScale = 0.9f, PoiSize = 32f * PoiScale;
        private readonly UiButton[] _poi = new UiButton[MaxPoi];
        private readonly QuestPoiIcon[] _poiIcon = new QuestPoiIcon[MaxPoi];
        private readonly int[] _poiQuest = new int[MaxPoi];
        private static bool _poiLogged;
        private readonly RectTransform _menu;
        private readonly TextMeshProUGUI _menuTitle;
        private readonly UiButton _menuShare;
        private int _menuQuest;
        private float _menuUntil;
        private int _askSerial;
        private static int LastAskSerial;                                    // the QUEST_ACCEPT request already shown (desktop + VR trackers share it)
        private bool _collapsed;
        private int _sig = int.MinValue, _count = -1;
        private Quaternion _smooth;
        private bool _hasSmooth;
        private static readonly StringBuilder Sb = new StringBuilder(256);

        public UiQuestTracker(Transform anchor, UiContext ctx, bool vr)
        {
            _ctx = ctx;
            _vr = vr;
            UiSurface old = UiKit.Surface;
            bool oldOver = UiKit.Over, oldPointable = UiKit.Pointable;
            try
            {
                if (vr)
                {
                    UiKit.Over = true;
                    UiKit.Pointable = false;
                    Surface = UiSurface.MakeWorld("WoW Objectives (VR)", anchor, Width, VrHeight, VrMetresPerUnit, true, 205);
                    UiKit.Surface = Surface;
                    _root = UiKit.Rect(Surface.Content, "Objectives", 0f, 0f, Width, VrHeight);
                }
                else
                {
                    UiKit.Pointable = true;
                    _root = UiKit.Rect(anchor, "Objectives", 0f, 0f, Width, 768f);
                }
                // Header (WatchFrame.xml:153-174) and the collapse button (:176-196)
                _title = UiKit.Text(_root, "Title", "Objectives", 12f, UiArt.Gold, TextAlignmentOptions.TopLeft, 0f, 6f, 160f, 16f);   // OBJECTIVES_TRACKER_LABEL (GS:5223)
                _collapse = UiKit.Button(_root, "Collapse", Width - 12f - 16f, 5f, 16f, 16f);
                _collapse.Art = UiKit.Pic(_collapse.transform, "Art", UiArt.Tex(@"Interface\Buttons\UI-Panel-QuestHideButton.blp"), 0f, 0.5f, 0.5f, 1f, 0f, 0f, 16f, 16f);
                _collapse.Hilite = UiKit.Pic(_collapse.transform, "Highlight", UiArt.Add(UiArt.CloseHighlight), 0f, 0f, 16f, 16f);
                _collapse.Hilite.enabled = false;
                _collapse.Click = ToggleCollapse;
                _lines = UiKit.Rect(_root, "Lines", 0f, LinesTop, Width - LinesRight, 700f);
                for (int i = 0; i < MaxLines; i++)
                {
                    int k = i;
                    UiButton b = UiKit.Button(_lines, "Line " + (i + 1), 0f, 0f, Width - LinesRight, LineH);
                    _lineText[i] = UiKit.Text(b.transform, "Text", "", 11f, Color.white, TextAlignmentOptions.TopLeft, 0f, 0f, Width - LinesRight, LineH);
                    b.Click = () => TitleClicked(k);
                    b.RightClick = () => TitleMenu(k);
                    b.gameObject.SetActive(false);
                    _line[i] = b;
                }
                for (int i = 0; i < MaxItems; i++)
                {
                    int k = i;
                    UiButton b = UiKit.Button(_lines, "Quest item " + (i + 1), 0f, 0f, ItemSize, ItemSize);
                    b.Icon = UiKit.Pic(b.transform, "Icon", null, 0f, 0f, ItemSize, ItemSize);
                    UiKit.Pic(b.transform, "Frame", UiArt.Tex(UiArt.Quickslot), -8f, -8f, 42f, 42f);   // NormalTexture UI-Quickslot2 42x42 centred (WatchFrame.xml:89-93)
                    b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, ItemSize, ItemSize);
                    b.Hilite.enabled = false;
                    b.Count = UiKit.Text(b.transform, "Count", "", 10f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, ItemSize - 3f, ItemSize - 2f, UiArt.Numbers);
                    b.Click = () => { if (_ctx.S != null && _itemQuest[k] > 0) _ctx.S.UseQuestItem(_itemQuest[k]); };   // UseQuestLogSpecialItem (WatchFrame.lua:1258-1268)
                    b.Tip = () => ItemTip(k);
                    b.gameObject.SetActive(false);
                    _item[i] = b;
                }
                for (int i = 0; i < MaxPoi; i++)
                {
                    int k = i;
                    UiButton b = UiKit.Button(_lines, "Quest POI " + (i + 1), -PoiSize, 0f, PoiSize, PoiSize);
                    _poiIcon[i] = QuestPoiIcon.Make(b.transform, "POI", PoiSize, false);
                    b.Click = () => PoiClicked(k);
                    b.Tip = () => _poiQuest[k] > 0 ? (Find(_ctx.P, _poiQuest[k]) ?? new QuestView()).Title : null;
                    b.gameObject.SetActive(false);
                    _poi[i] = b;
                }
                // The title menu (desktop right-click; WatchFrameDropDown_Initialize :1063-1135) - ours: three panel buttons in a dark box
                _menu = UiKit.Rect(_root, "Menu", -150f, 0f, 150f, 92f);
                UiKit.Solid(_menu, "Back", new Color(0f, 0f, 0f, 0.85f), 0f, 0f, 150f, 92f);
                _menuTitle = UiKit.Text(_menu, "Title", "", 11f, UiArt.Gold, TextAlignmentOptions.Left, 6f, 2f, 140f, 18f);
                UiKit.PanelButton(_menu, UiGlobalStrings.G("OBJECTIVES_VIEW_IN_QUESTLOG", "Open Quest Details"), 4f, 22f, 142f, 21f, () => { RequestShow(_menuQuest); HideMenu(); });   // OBJECTIVES_VIEW_IN_QUESTLOG (GS:5226)
                UiKit.PanelButton(_menu, UiGlobalStrings.G("OBJECTIVES_STOP_TRACKING", "Stop Tracking"), 4f, 45f, 142f, 21f, () => { QuestWatch.Unwatch(_menuQuest); HideMenu(); });   // OBJECTIVES_STOP_TRACKING (GS:5221)
                _menuShare = UiKit.PanelButton(_menu, UiGlobalStrings.G("SHARE_QUEST", "Share Quest"), 4f, 68f, 142f, 21f, () => { if (_ctx.S != null) _ctx.S.ShareQuest(_menuQuest); HideMenu(); });   // SHARE_QUEST (GS:6238)
                _menu.gameObject.SetActive(false);
                if (Surface != null) { Surface.Finish(); Surface.SetShown(false); }
            }
            finally
            {
                UiKit.Surface = old;
                UiKit.Over = oldOver;
                UiKit.Pointable = oldPointable;
            }
        }

        public void OnEvent(GameEvent e) { QuestWatch.OnEvent(e); }

        private void ToggleCollapse() { _collapsed = !_collapsed; _sig = int.MinValue; UiKit.Coords(_collapse.Art, 0f, 0.5f, _collapsed ? 0f : 0.5f, _collapsed ? 0.5f : 1f); }   // WatchFrame.lua:301-321

        private void RequestShow(int questId)
        {
            if (questId <= 0) return;
            QuestWatch.RequestShow(questId);
            if (_ctx.IsOpen != null && !_ctx.IsOpen(UiWindowKind.QuestLog) && _ctx.Toggle != null) _ctx.Toggle(UiWindowKind.QuestLog);
        }

        private void TitleClicked(int k) { if (_lineQuest[k] > 0) RequestShow(_lineQuest[k]); }   // WatchFrameLinkButtonTemplate_OnLeftClick (:117-127)

        /// <summary>WatchFrameQuestPOI_OnClick (WatchFrame.lua:1313-1322): the map already showing this quest closes, else WorldMap_OpenToQuest.</summary>
        private void PoiClicked(int k)
        {
            int id = _poiQuest[k];
            if (id <= 0) return;
            if (WowTabletMap.AnyShown)
            {
                if (WowQuestMap.SelectedQuestId == id) { WowTabletMap.CloseWindows("the tracker's POI of the shown quest"); return; }
                UiSound.Named("igMainMenuOptionCheckBoxOn");
            }
            WowQuestMap.OpenToQuest(_ctx.P, id);
        }

        private void TitleMenu(int k)
        {
            if (_vr || _lineQuest[k] <= 0) return;
            _menuQuest = _lineQuest[k];
            QuestView q = Find(_ctx.P, _menuQuest);
            _menuTitle.text = q != null ? q.Title : "";
            _menuShare.gameObject.SetActive(q != null && q.Sharable && _ctx.P.Group.Count > 0);   // GetQuestLogPushable and in a party (WatchFrame.lua:1089-1095)
            UiKit.Move(_menu, -154f, LinesTop + ((RectTransform)_line[k].transform).anchoredPosition.y * -1f);
            _menu.gameObject.SetActive(true);
            _menuUntil = Time.unscaledTime + 6f;                                  // ours: the menu closes itself after 6 s
        }

        private void HideMenu() { _menu.gameObject.SetActive(false); }

        /// <summary>QUEST_ACCEPT / QUEST_ACCEPT_LOG_FULL (StaticPopup.lua:1647-1670) through uicore's UiPopup: YES = ConfirmAcceptQuest, NO sends
        /// nothing (no OnCancel in WoW; the session only forgets the question); exclusive, hideOnEscape; the log-full one has Yes disabled.
        /// Both trackers (desktop + VR) may exist: the popup is shown once per request (UiPopup.IsShown).</summary>
        private void AskQuestConfirm(QuestConfirmView c)
        {
            if (UiPopup.IsShown("QUEST_ACCEPT")) UiPopup.HideWhich("QUEST_ACCEPT");
            string text = c.LogFull
                ? UiGlobalStrings.FormatOr("QUEST_ACCEPT_LOG_FULL", "%s is starting %s\nYour quest log is full. You can join this\nquest if you make room in your quest log.", c.FromName, c.Title)   // GS:5880
                : UiGlobalStrings.FormatOr("QUEST_ACCEPT", "%s is starting %s\nWould you like to as well?", c.FromName, c.Title);                                                         // GS:5879
            bool full = c.LogFull;
            UiContext ctx = _ctx;
            UiPopup.Show(new UiPopupSpec
            {
                Which = "QUEST_ACCEPT", Text = text, Data = c.Serial,
                Button1 = UiGlobalStrings.G("YES", "Yes"), Button2 = UiGlobalStrings.G("NO", "No"),
                Exclusive = true, HideOnEscape = true,
                Button1Enabled = full ? (Func<string, bool>)(_ => false) : null,
                OnAccept = () => { if (ctx.S != null) ctx.S.AnswerQuestConfirm(true); },
                OnCancel = () => { if (ctx.S != null) ctx.S.AnswerQuestConfirm(false); }
            });
            WowLog.Info("UI: QUEST_ACCEPT - " + c.FromName + " is starting quest " + c.QuestId + (full ? " (log full)" : ""));
        }

        private string ItemTip(int k)
        {
            PlayerState p = _ctx.P;
            QuestView q = Find(p, _itemQuest[k]);
            if (q == null || q.UseItemBagIndex < 0 || q.UseItemBagIndex >= p.Bags.Count) return null;
            return UiTooltip.Item(p.Bags[q.UseItemBagIndex], null);
        }

        private static QuestView Find(PlayerState p, int id)
        {
            if (p == null) return null;
            for (int i = 0; i < p.QuestLog.Count; i++) if (p.QuestLog[i].Id == id) return p.QuestLog[i];
            return null;
        }

        /// <summary>SecondsToTime (UIParent.lua:2271-2325): abbreviated, at most two units ("29 Min 59 Sec").</summary>
        public static string SecondsToTime(float seconds)
        {
            int s = Mathf.FloorToInt(Mathf.Max(0f, seconds)), count = 0;
            var b = new StringBuilder();
            if (s >= 86400) { int n = s / 86400; b.Append(n).Append(n == 1 ? " Day" : " Days"); s %= 86400; count++; }   // DAYS_ABBR (GS:1925)
            if (s >= 3600) { if (b.Length > 0) b.Append(' '); b.Append(s / 3600).Append(" Hr"); s %= 3600; count++; }    // HOURS_ABBR (GS:4196)
            if (count < 2 && s >= 60) { if (b.Length > 0) b.Append(' '); b.Append(s / 60).Append(" Min"); s %= 60; count++; }   // MINUTES_ABBR (GS:4953)
            if (count < 2 && s > 0) { if (b.Length > 0) b.Append(' '); b.Append(s).Append(" Sec"); }                    // SECONDS_ABBR (GS:6201)
            return b.ToString();
        }

        /// <summary>GetTimerTextColor (WatchFrame.lua:323-337).</summary>
        public static Color TimerColor(float duration, float elapsed)
        {
            float left = duration > 0f ? 1f - elapsed / duration : 1f;
            if (left > 0.66f) return Color.white;
            if (left > 0.33f) return new Color(1f, 1f, (left - 0.33f) / (0.66f - 0.33f));
            return new Color(1f, left / 0.33f, 0f);
        }

        /// <summary>WatchFrame_ReverseQuestObjective (WatchFrame.lua:1270-1277) on GetQuestLogLeaderBoard's "%s: %d/%d": "0/1 The Deed to Brill"
        /// (WowQuestMap.ObjectiveLine; the tracker only shows unfinished objectives, :838-845).</summary>
        private static string Objective(QuestObjectiveView o)
        {
            return o.Need > 1 || !o.Done ? WowQuestMap.ObjectiveLine(o) : o.Text;
        }

        public void Refresh(float now)
        {
            PlayerState p = _ctx.P;
            QuestWatch.Sync(p);
            if (_menu.gameObject.activeSelf && now > _menuUntil) HideMenu();
            QuestConfirmView c = p != null ? p.PendingQuestConfirm : null;
            if (c == null) { if (_askSerial != 0) { _askSerial = 0; LastAskSerial = 0; if (UiPopup.IsShown("QUEST_ACCEPT")) UiPopup.HideWhich("QUEST_ACCEPT"); } }
            else if (c.Serial != _askSerial)
            {
                _askSerial = c.Serial;
                if (c.Serial != LastAskSerial) { LastAskSerial = c.Serial; AskQuestConfirm(c); }   // once for both trackers
            }
            if (p == null) return;
            // Quest map fix (the "- 0/1 Item 13471" lines): the views are rebuilt when the server's item / creature / object names arrive
            // (SMSG_ITEM_QUERY_SINGLE_RESPONSE -> OnQItemQuery -> _qLogDirty), but this signature only had counts, so the first texts stayed.
            // It now hashes the titles and objective texts too (WatchFrame redraws on QUEST_LOG_UPDATE, WatchFrame.lua:238-242).
            int sig = QuestWatch.Serial * 7919 + (_collapsed ? 1 : 0) + p.QuestLog.Count * 31;
            for (int i = 0; i < p.QuestLog.Count; i++)
            {
                QuestView q = p.QuestLog[i];
                sig = sig * 31 + q.Id + (int)q.State * 3 + (q.Failed ? 5 : 0) + q.UseItemBagIndex * 11 + (q.TimeLeft >= 0f ? Mathf.CeilToInt(q.TimeLeft) * 13 : 0)
                      + (q.Title != null ? q.Title.GetHashCode() : 0) + (q.LogCompletionText != null ? q.LogCompletionText.GetHashCode() * 3 : 0) + (q.Pois != null ? 17 + q.Pois.Count : 0);
                for (int o = 0; o < q.Objectives.Count; o++) sig = sig * 7 + q.Objectives[o].Have + (q.Objectives[o].Text != null ? q.Objectives[o].Text.GetHashCode() : 0);
            }
            // iteration 11 quest map: the POI numbers follow the player's zone, Show Quest Objectives and the map's selection
            sig = sig * 31 + WowQuestMap.Serial * 13 + WowQuestMap.PlayerZone * 7 + (WowQuestMap.ShowObjectives ? 1 : 0) + (WowTabletMap.AnyShown ? WowQuestMap.SelectedQuestId * 5 : 0);
            if (sig != _sig) { _sig = sig; Layout(p); }
            for (int i = 0; i < MaxItems; i++)
            {
                if (!_item[i].gameObject.activeSelf) continue;
                QuestView q = Find(p, _itemQuest[i]);
                if (q == null || q.UseItemBagIndex < 0 || q.UseItemBagIndex >= p.Bags.Count) continue;
                string path = p.Bags[q.UseItemBagIndex].IconPath;
                if (ReferenceEquals(path, _itemPath[i]) && _item[i].Icon.enabled) continue;   // quest map fix: a button reused by another quest got its icon again
                Texture2D t = UiArt.Icon(path);
                if (t == null) { _item[i].Icon.enabled = false; continue; }
                _itemPath[i] = path;
                _item[i].Icon.texture = t; _item[i].Icon.color = Color.white; _item[i].Icon.enabled = true;
            }
        }

        private void Layout(PlayerState p)
        {
            int n = 0, items = 0, shown = 0;
            float y = 0f;
            // the timers block (WatchFrame_DisplayQuestTimers :471-524): every timed quest of the log
            bool header = false;
            for (int i = 0; i < p.QuestLog.Count && !_collapsed; i++)
            {
                QuestView q = p.QuestLog[i];
                if (q.TimeLeft < 0f || q.Failed) continue;
                if (!header) { y = PlaceLine(n++, UiGlobalStrings.G("QUEST_TIMERS", "Quest Timers"), UiArt.Gold, 0, y, 0f); header = true; shown++; }   // QUEST_TIMERS (GS:5907)
                y = PlaceLine(n++, " - " + SecondsToTime(q.TimeLeft), TimerColor(q.TimeTotal, q.TimeTotal - q.TimeLeft), q.Id, y, 0f);
            }
            if (header) y += TypeGap;
            var list = QuestWatch.List;
            // iteration 11 quest map: the POI buttons (WatchFrame.lua:901-918): a watched quest on the current map ("CURRENT_MAP_QUESTS[questID]",
            // the player's zone: WatchFrame_GetCurrentMapQuests after SetMapToCurrentZone) gets QUEST_POI_NUMERIC (its own running count) or,
            // complete, QUEST_POI_COMPLETE_IN; a complete quest elsewhere QUEST_POI_COMPLETE_OUT. Ours: only for quests with POI data (online,
            // answered) - offline has none. The map's selected quest is selected while the map shows (:783-785, :940-942).
            List<QuestMapEntry> onMap = WowQuestMap.ShowObjectives ? WowQuestMap.PlayerZoneQuests(p) : null;
            int selectedQuest = WowTabletMap.AnyShown ? WowQuestMap.SelectedQuestId : 0;
            int pois = 0, poiNumeric = 0, poiIn = 0, poiOut = 0;
            for (int w = 0; w < list.Count && !_collapsed; w++)
            {
                QuestView q = Find(p, list[w]);
                if (q == null) continue;
                if (n + 1 + q.Objectives.Count >= MaxLines) break;                   // a quest that does not fit any more stops the list (:855-862)
                bool complete = WowQuestMap.IsComplete(q);                        // the log's complete state, failed is not complete (:806-811; WowQuestMap.IsComplete)
                bool item = q.UseItemBagIndex >= 0 && !complete && items < MaxItems;
                float top = y;
                // WatchFrame_SetLine (:575-616, :600): "if ( hasItem and WATCHFRAME_SETLINES_NUMLINES < 2 ) usedWidth = usedWidth + WATCHFRAME_ITEM_WIDTH" -
                // the item narrows the title and the line under it (two line units; the header resets the count); a wrapped line counts 2.
                int setLines = 0;
                float y0 = y;
                y = PlaceLine(n++, q.Failed ? q.Title + " " + UiGlobalStrings.G("QUEST_FAILED_TAG", "- Failed") : q.Title, TitleColor, q.Id, y, item ? ItemW : 0f);   // QUEST_FAILED_TAG (GS:5888)
                setLines += y - y0 > LineH + 0.5f ? 2 : 1;
                if (!q.Failed)
                {
                    if (complete) y = PlaceLine(n++, "- " + (string.IsNullOrEmpty(q.LogCompletionText) ? "Ready to hand in." : q.LogCompletionText), Color.white, 0, y, 0f);   // ours fallback
                    else
                        for (int o = 0; o < q.Objectives.Count; o++)
                        {
                            if (q.Objectives[o].Done) continue;
                            y0 = y;
                            y = PlaceLine(n++, "- " + Objective(q.Objectives[o]), Color.white, 0, y, item && setLines < 2 ? ItemW : 0f);
                            setLines += y - y0 > LineH + 0.5f ? 2 : 1;
                        }
                }
                if (item)
                {
                    UiButton b = _item[items];
                    UiKit.Move(b.Rt, (Width - LinesRight) + 10f - ItemSize, top + 2f);      // TOPRIGHT of the title + (10, -2) (WatchFrame.lua:883)
                    if (_itemQuest[items] != q.Id) _itemPath[items] = null;         // quest map fix: the button now shows another quest's item
                    _itemQuest[items] = q.Id;
                    ItemView iv = q.UseItemBagIndex < p.Bags.Count ? p.Bags[q.UseItemBagIndex] : null;
                    b.Count.SetText(iv != null && iv.Count > 1 ? iv.Count.ToString() : "");
                    b.gameObject.SetActive(true);
                    items++;
                }
                if (onMap != null && q.Pois != null && pois < MaxPoi)
                {
                    QuestMapEntry e = null;
                    for (int k = 0; k < onMap.Count && e == null; k++) if (onMap[k].QuestId == q.Id) e = onMap[k];
                    int type = 0, index = 0;
                    if (e != null) { if (complete) { type = QuestPoiIcon.CompleteIn; index = ++poiIn; } else { type = QuestPoiIcon.Numeric; index = ++poiNumeric; } }
                    else if (complete) { type = QuestPoiIcon.CompleteOut; index = ++poiOut; }
                    if (type != 0)
                    {
                        UiButton b = _poi[pois];
                        UiKit.Move(b.Rt, -PoiSize, top - 5f * PoiScale);                // "SetPoint("TOPRIGHT", questTitle, "TOPLEFT", 0, 5)" at SetScale(0.9)
                        _poiIcon[pois].Set(type, index, q.Id == selectedQuest && type != QuestPoiIcon.CompleteOut);
                        _poiQuest[pois] = q.Id;
                        b.gameObject.SetActive(true);
                        pois++;
                    }
                }
                y += QuestGap;
                shown++;
            }
            for (int i = n; i < MaxLines; i++) if (_line[i].gameObject.activeSelf) _line[i].gameObject.SetActive(false);
            for (int i = items; i < MaxItems; i++) { if (_item[i].gameObject.activeSelf) _item[i].gameObject.SetActive(false); _itemQuest[i] = 0; _itemPath[i] = null; }
            for (int i = pois; i < MaxPoi; i++) { if (_poi[i].gameObject.activeSelf) _poi[i].gameObject.SetActive(false); _poiQuest[i] = 0; }
            if (pois > 0 && !_poiLogged)
            {
                _poiLogged = true;
                WowLog.Info("UI quest map: tracker POI buttons - " + pois + " shown (" + poiNumeric + " numbered on zone " + WowQuestMap.PlayerZone + ", " + poiIn + " complete here, " + poiOut + " complete elsewhere).");
            }
            int total = 0;
            for (int i = 0; i < list.Count; i++) if (Find(p, list[i]) != null) total++;
            for (int i = 0; i < p.QuestLog.Count; i++) if (p.QuestLog[i].TimeLeft >= 0f && !p.QuestLog[i].Failed) { total++; break; }
            bool any = total > 0;
            _title.text = UiGlobalStrings.G("OBJECTIVES_TRACKER_LABEL", "Objectives") + " (" + total + ")";                                // WatchFrame.lua:390
            _title.enabled = any;
            _collapse.gameObject.SetActive(any);
            if (_vr && Surface != null && Surface.Shown != (any && WowSettings.VrHud)) Surface.SetShown(any && WowSettings.VrHud);
            if (total != _count) { _count = total; WowLog.Info("UI: quest tracker - " + list.Count + " watched, " + total + " shown" + (_vr ? " (VR panel)" : "") + "."); }
        }

        private float PlaceLine(int i, string text, Color color, int questId, float y, float narrower)
        {
            UiButton b = _line[i];
            TextMeshProUGUI t = _lineText[i];
            float w = Width - LinesRight - narrower;
            t.text = text;
            t.color = color;
            float h = t.GetPreferredValues(text, w, 0f).y > LineH + 1f ? WrapH : LineH;
            UiKit.Size(t.rectTransform, w, h);
            UiKit.Size(b.Rt, w, h);
            UiKit.Move(b.Rt, 0f, y);
            _lineQuest[i] = questId;
            b.gameObject.SetActive(true);
            return y + h;
        }

        /// <summary>VR: lazy-follow beside the floating HUD (UiVrHud.Place's way, mirrored to the right; values ours).</summary>
        public void PlaceVr(Transform head, float dt)
        {
            if (Surface == null || !Surface.Shown || head == null) { _hasSmooth = false; return; }
            float size = VrMetresPerUnit * Mathf.Clamp(WowSettings.VrQuestSize, 0.75f, 2f);   // lead 2026-10-10: Settings "Quest text size (VR)"
            if (!Mathf.Approximately(Surface.Root.localScale.x, size)) Surface.Root.localScale = Vector3.one * size;
            Vector3 f = head.forward;
            Vector3 flat = new Vector3(f.x, 0f, f.z);
            Quaternion look = Mathf.Abs(f.y) < 0.97f ? Quaternion.LookRotation(f, Vector3.up) : Quaternion.LookRotation(flat.sqrMagnitude > 1e-6f ? flat : Vector3.forward, Vector3.up);
            if (!_hasSmooth) { _smooth = look; _hasSmooth = true; }
            else _smooth = Quaternion.Slerp(_smooth, look, 1f - Mathf.Exp(-VrFollow * Mathf.Max(0f, dt)));
            Quaternion rot = _smooth * Quaternion.Euler(-VrPitch, VrYaw, 0f);
            Surface.Root.SetPositionAndRotation(head.position + rot * new Vector3(0f, 0f, VrDistance), rot);
        }
    }
}
