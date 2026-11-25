// iteration 11 (w0game, wave 0): moved verbatim from UiWindows.cs (PLAN11 s3) - owner quests from wave 1 on.
// iteration 11 (quests H-U2): all 25 quests (scroll arrows + wheel), Share / Track, Abandon through WoW's ABANDON_QUEST popup, Failed / Daily /
// timer tags, "Daily: n/25", the reward spell line, and the tracker's "show this quest" request.
// 13.7 (the user): quests grouped under zone headers, the selected quest in a scrolling pane with its full text and rewards.
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.MapApp;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    // =====================================================================================================================
    /// <summary>
    /// The quest log on WoW's single-pane UI-QuestLog art: the quest list on top, grouped under zone / category headers like WoW's log
    /// (QuestLogFrame.lua: a header row with UI-PlusButton-Up / UI-MinusButton-Up folds its quests); each quest row shows the level in
    /// difficulty colour, "(Complete)", "- Failed", "(Daily)", the time left of a timed quest; scrolled with the arrows or the mouse wheel.
    /// Below, the selected quest in a scrolling pane (13.7, the user: "there is no scroll bar for text ... display them exactly as we saw
    /// when we get them, so we see full text, reward and xp"): title, objectives with progress, the description, and the rewards laid out
    /// as the quest giver showed them (item icons with tooltips, the reward spell, experience, money), with the scroll arrows and
    /// UIPanelScrollBarTemplate's knob (UI-ScrollBar-Knob 18 x 24, TexCoords 0.20-0.80 / 0.125-0.875, read from UIPanelTemplates.xml).
    /// Share / Track above the art's bottom button boxes (QuestLogControlPanel, QuestLogFrame.xml:125-200 - ours: placed above the 384
    /// single pane's two boxes), Abandon (WoW's ABANDON_QUEST popup) and Close in the boxes.
    /// </summary>
    public sealed class UiQuestLog : UiWindow
    {
        public const int Rows = 8;                                           // 13.7: 8 (was 10) - the detail pane got the room
        public const int MaxQuests = 25;                                     // the 25 quest log slots (PLAYER_QUEST_LOG_1_1 .. 25)
        private const float RowW = 280f;                                     // ours: 300 minus the scroll arrows' column
        private const float ListY = 76f;
        private const float DetailX = 22f, DetailY = 226f, DetailW = 280f, DetailH = 158f, TextW = 272f;   // ours (13.7)
        private const int MaxItems = 6;
        private readonly UiButton[] _row = new UiButton[Rows];
        private readonly TextMeshProUGUI[] _rowText = new TextMeshProUGUI[Rows];
        private readonly RawImage[] _rowSel = new RawImage[Rows];
        private readonly RawImage[] _rowFold = new RawImage[Rows];
        private readonly TextMeshProUGUI _empty, _count;
        private readonly UiButton _abandon, _share, _track, _up, _down, _showMap, _dUp, _dDown;
        private readonly TextMeshProUGUI _showMapText;
        private readonly RectTransform _list, _detailClip, _detail;
        private readonly RawImage _knob;
        // the detail pane's pieces, stacked top to bottom by LayoutDetail
        private readonly TextMeshProUGUI _dTitle, _dObjectives, _dLines, _dDescHead, _dDesc, _dRewHead, _dSpell, _dChoose, _dReceive, _dXp;
        private readonly UiButton[] _items = new UiButton[MaxItems];
        private readonly TextMeshProUGUI[] _itemName = new TextMeshProUGUI[MaxItems];
        private readonly ItemView[] _itemView = new ItemView[MaxItems];
        private readonly string[] _itemPath = new string[MaxItems];
        private readonly bool[] _itemLoaded = new bool[MaxItems];
        private float _y, _detailH, _offset;
        private int _detailFor;
        private int _selectedId, _sig = int.MinValue, _top, _showSeen, _foldSerial;
        // the list as shown: a header (quest -1, its key) or a quest (its log index)
        private readonly List<int> _entryQuest = new List<int>();
        private readonly List<string> _entryHeader = new List<string>();
        private readonly HashSet<string> _folded = new HashSet<string>();
        private static readonly Dictionary<int, string> ZoneNames = new Dictionary<int, string>();
        private static bool _logged;

        public UiQuestLog(Transform parent, UiContext ctx, float x, float y, System.Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "Quest Log", x, y, W, H);
            UiKit.Frame4(Root, "Art", UiArt.QuestLog + "TopLeft.blp", UiArt.QuestLog + "TopRight.blp", UiArt.QuestLog + "BotLeft.blp", UiArt.QuestLog + "BotRight.blp");
            Portrait(UiArt.Tex(UiArt.QuestLogBookIcon));
            Title("Quest Log", 16f);
            UiKit.CloseButton(Root, 322f, 7f, close);
            _count = UiKit.Text(Root, "Count", "", 10f, Color.white, TextAlignmentOptions.Right, 70f, 44f, 170f, 14f);   // ours (iteration 11 quest map: moved left of Show Map)
            // QuestLogFrameShowMapButton (QuestLogFrame.xml): 48 x 32 at TOPRIGHT (-25, -38) (QuestLogShowMapPOI_UpdatePosition), the texture
            // UI-QuestMap_Button (TexCoords 0.125-0.875, 0-0.5) anchored RIGHT, SHOW_MAP "Show Map" (GameFontNormal) left of it, the width
            // "self.text:GetWidth() + self.texture:GetWidth()", ButtonHilight-Square 36 x 25 (ADD) at RIGHT (-7, 0) on hover.
            _showMap = UiKit.Button(Root, "QuestLogFrameShowMapButton", W - 25f - 112f, 38f, 112f, 32f);
            UiKit.Pic(_showMap.transform, "Texture", UiArt.Tex(@"Interface\QuestFrame\UI-QuestMap_Button.blp"), 0.125f, 0.875f, 0f, 0.5f, 112f - 48f, 0f, 48f, 32f);
            _showMapText = UiKit.Text(_showMap.transform, "Text", UiGlobalStrings.G("SHOW_MAP", "Show Map"), 12f, UiArt.Gold, TextAlignmentOptions.Right, 0f, 0f, 112f - 48f, 32f);   // GS:6281
            _showMapText.textWrappingMode = TextWrappingModes.NoWrap;
            _showMap.Hilite = UiKit.Pic(_showMap.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 112f - 7f - 36f, (32f - 25f) * 0.5f, 36f, 25f);
            _showMap.Hilite.enabled = false;
            _showMap.Click = ShowMap;
            _showMap.Tip = () => "Show this quest's areas on the WoW Map.";   // ours (the button has no tooltip in WoW)
            _showMap.gameObject.SetActive(false);
            _list = UiKit.Rect(Root, "Quest list", 22f, ListY, 300f, Rows * 16f);
            for (int i = 0; i < Rows; i++)
            {
                int k = i;
                _row[i] = UiKit.Button(Root, "Quest row " + (i + 1), 22f, ListY + i * 16f, RowW, 16f);
                _rowSel[i] = UiKit.Pic(_row[i].transform, "Selected", UiArt.Add(UiArt.QuestLogTitleHighlight), 0f, 0f, RowW, 16f);
                _rowSel[i].color = new Color(0.9f, 0.8f, 0.3f, 1f);
                _rowSel[i].enabled = false;
                _row[i].Hilite = UiKit.Pic(_row[i].transform, "Highlight", UiArt.Add(UiArt.QuestLogTitleHighlight), 0f, 0f, RowW, 16f);
                _row[i].Hilite.enabled = false;
                _rowFold[i] = UiKit.Pic(_row[i].transform, "Fold", UiArt.Tex(@"Interface\Buttons\UI-MinusButton-Up.blp"), 2f, 0f, 16f, 16f);   // QuestLogFrame.lua
                _rowFold[i].enabled = false;
                _rowText[i] = UiKit.Text(_row[i].transform, "Text", "", 11f, Color.white, TextAlignmentOptions.Left, 20f, 0f, RowW - 24f, 16f);   // 13.7: after the header fold icon
                _rowText[i].textWrappingMode = TextWrappingModes.NoWrap;
                _rowText[i].overflowMode = TextOverflowModes.Ellipsis;
                _row[i].Click = () => Select(k);
                _row[i].gameObject.SetActive(false);
            }
            // the scroll arrows at the list's right edge (UiQuestDialog's art: UI-ScrollBar-Scroll*Button, 20 x 20)
            _up = UiKit.ArtButton(Root, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 304f, ListY, 20f, () => ScrollRows(-1));
            _down = UiKit.ArtButton(Root, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 304f, ListY + Rows * 16f - 20f, 20f, () => ScrollRows(1));
            _up.gameObject.SetActive(false);
            _down.gameObject.SetActive(false);
            _empty = UiKit.Text(Root, "Empty", "Your quest log is empty.", 12f, UiArt.Gold, TextAlignmentOptions.Center, 22f, 120f, 300f, 20f);
            UiKit.Pic(Root, "Break", UiArt.Tex(UiArt.HorizontalBreak), 36f, ListY + Rows * 16f, 272f, 24f);

            // 13.7: the detail pane - a clipped scroll like UiQuestDialog's (RectMask2D; UiSurface.Clipped keeps hidden buttons unpressable)
            _detailClip = UiKit.Rect(Root, "Quest detail", DetailX, DetailY, DetailW, DetailH);
            _detailClip.gameObject.AddComponent<RectMask2D>();
            _detail = UiKit.Rect(_detailClip, "Scroll", 0f, 0f, DetailW, DetailH);
            _dTitle = DetailText("Quest title", 15f, UiArt.Gold, UiArt.Morpheus);
            _dObjectives = DetailText("Objectives", 11f, UiArt.Gold, null);
            _dLines = DetailText("Objective lines", 11f, Color.white, null);
            _dDescHead = DetailText("Description heading", 13f, UiArt.Gold, UiArt.Morpheus);
            _dDesc = DetailText("Description", 11f, new Color(0.88f, 0.82f, 0.69f), null);
            _dRewHead = DetailText("Rewards heading", 13f, UiArt.Gold, UiArt.Morpheus);
            _dSpell = DetailText("Reward spell", 11f, Color.white, null);
            _dChoose = DetailText("Choose", 11f, Color.white, null);
            for (int i = 0; i < MaxItems; i++)
            {
                int k = i;
                // LargeItemButtonTemplate as the quest dialog draws it, smaller: icon + name, two per row
                UiButton b = UiKit.Button(_detail, "Reward " + (i + 1), 0f, 0f, 134f, 34f);
                UiKit.Pic(b.transform, "Name frame", UiArt.Tex(UiArt.QuestItemNameFrame), 24f, -10f, 112f, 54f);
                b.Icon = UiKit.Pic(b.transform, "Icon", null, 0f, 2f, 30f, 30f);
                b.Icon.enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 2f, 30f, 30f);
                b.Hilite.enabled = false;
                b.Count = UiKit.Text(b.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 18f, 29f, 13f, UiArt.Numbers);
                _itemName[i] = UiKit.Text(b.transform, "Name", "", 10f, Color.white, TextAlignmentOptions.Left, 35f, 2f, 96f, 30f);
                b.Tip = () => UiTooltip.Item(_itemView[k], null);
                b.gameObject.SetActive(false);
                _items[i] = b;
            }
            _dReceive = DetailText("Receive", 11f, Color.white, null);
            _dXp = DetailText("Experience and money", 11f, Color.white, null);
            _dUp = UiKit.ArtButton(Root, "Detail scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 304f, DetailY, 20f, () => ScrollDetail(-48f));
            _dDown = UiKit.ArtButton(Root, "Detail scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 304f, DetailY + DetailH - 20f, 20f, () => ScrollDetail(48f));
            _knob = UiKit.Pic(Root, "Detail scroll knob", UiArt.Tex(@"Interface\Buttons\UI-ScrollBar-Knob.blp"), 0.20f, 0.80f, 0.125f, 0.875f, 305f, DetailY + 20f, 18f, 24f);
            _dUp.gameObject.SetActive(false);
            _dDown.gameObject.SetActive(false);
            _knob.enabled = false;

            // Share / Track (QuestLogControlPanel: SHARE_QUEST_ABBREV GS:6239, TRACK_QUEST_ABBREV GS:7638) - ours at (18, 388) / (236, 388)
            _share = UiKit.PanelButton(Root, UiGlobalStrings.G("SHARE_QUEST_ABBREV", "Share"), 18f, 388f, 104f, 21f, Share);
            _track = UiKit.PanelButton(Root, UiGlobalStrings.G("TRACK_QUEST_ABBREV", "Track"), 236f, 388f, 104f, 21f, Track);
            _abandon = UiKit.PanelButton(Root, UiGlobalStrings.G("ABANDON_QUEST_ABBREV", "Abandon"), 18f, 411f, 104f, 21f, Abandon);   // GS:5
            UiKit.PanelButton(Root, "Close", 236f, 411f, 104f, 21f, close);
            Root.gameObject.SetActive(false);
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI: quest log - ours: " + Rows + " rows with zone headers through " + MaxQuests + " quests (arrows at x 304, rows " + RowW + " wide), scrolling detail "
                    + DetailW + " x " + DetailH + " at y " + DetailY + ", Share / Track at (18, 388) / (236, 388).");
            }
        }

        private TextMeshProUGUI DetailText(string name, float size, Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI t = UiKit.Text(_detail, name, "", size, color, TextAlignmentOptions.TopLeft, 4f, 0f, TextW, 16f, font);
            t.overflowMode = TextOverflowModes.Overflow;
            t.enabled = false;
            return t;
        }

        private List<QuestView> Log { get { return Ctx.P != null ? Ctx.P.QuestLog : null; } }

        // ------------------------------------------------------------------ the list: zone headers + quests

        /// <summary>The header a quest goes under: the session's zone name, else ZoneOrSort named from AreaTable (> 0) or QuestSort (< 0).</summary>
        private static string ZoneOf(QuestView q)
        {
            if (!string.IsNullOrEmpty(q.ZoneName)) return q.ZoneName;
            int z = q.ZoneOrSort;
            if (z == 0) return "Other";                                       // ours: a quest without zone or sort
            string name;
            if (ZoneNames.TryGetValue(z, out name)) return name;
            name = null;
            try
            {
                WowFiles files = UiArt.Files;
                if (files != null)
                {
                    if (z > 0)
                    {
                        DbcTable area = Dbc.Load(files, "AreaTable");
                        int r = area != null ? area.FindRow(z) : -1;
                        if (r >= 0) name = area.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang);
                    }
                    else
                    {
                        DbcTable sort = Dbc.Load(files, "QuestSort");
                        int r = sort != null ? sort.FindRow(-z) : -1;
                        if (r >= 0) name = sort.GetLocalizedString(r, DbcFields.QuestSort.SortName_lang);
                    }
                }
            }
            catch (Exception e) { WowLog.Warn("UI: quest log zone " + z + ": " + e.Message); }
            if (string.IsNullOrEmpty(name)) name = "Other";
            ZoneNames[z] = name;
            return name;
        }

        /// <summary>Headers in the order their first quest has in the log; each header's quests in log order; folded headers hide theirs.</summary>
        private void BuildEntries(List<QuestView> log)
        {
            _entryQuest.Clear();
            _entryHeader.Clear();
            var order = new List<string>();
            var zone = new string[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                zone[i] = ZoneOf(log[i]);
                if (!order.Contains(zone[i])) order.Add(zone[i]);
            }
            for (int h = 0; h < order.Count; h++)
            {
                _entryQuest.Add(-1);
                _entryHeader.Add(order[h]);
                if (_folded.Contains(order[h])) continue;
                for (int i = 0; i < log.Count; i++)
                {
                    if (zone[i] != order[h]) continue;
                    _entryQuest.Add(i);
                    _entryHeader.Add(order[h]);
                }
            }
        }

        private void Select(int row)
        {
            List<QuestView> log = Log;
            int e = _top + row;
            if (log == null || e >= _entryQuest.Count) return;
            int qi = _entryQuest[e];
            if (qi < 0)
            {
                string key = _entryHeader[e];                                // a header row: fold / unfold (QuestLogTitleButton_OnClick)
                if (!_folded.Remove(key)) _folded.Add(key);
                _foldSerial++;
                _sig = int.MinValue;
                return;
            }
            if (qi >= log.Count) return;
            _selectedId = log[qi].Id;
            _sig = int.MinValue;
        }

        private QuestView Selected()
        {
            List<QuestView> log = Log;
            if (log == null) return null;
            for (int i = 0; i < log.Count; i++) if (log[i].Id == _selectedId) return log[i];
            return log.Count > 0 ? log[0] : null;
        }

        private void ScrollRows(int d)
        {
            int max = Mathf.Max(0, _entryQuest.Count - Rows);
            int top = Mathf.Clamp(_top + d, 0, max);
            if (top == _top) return;
            _top = top;
            _sig = int.MinValue;
        }

        /// <summary>Unfolds the quest's header and scrolls the list so the quest at this log index is visible.</summary>
        private void ShowRow(int index)
        {
            List<QuestView> log = Log;
            if (log == null || index < 0 || index >= log.Count) return;
            if (_folded.Remove(ZoneOf(log[index]))) _foldSerial++;
            BuildEntries(log);
            int e = _entryQuest.IndexOf(index);
            if (e < 0) return;
            if (e < _top) _top = e;
            else if (e >= _top + Rows) _top = e - Rows + 1;
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _entryQuest.Count - Rows));
        }

        // ------------------------------------------------------------------ buttons

        /// <summary>QuestLogFrameAbandonButton -> StaticPopup ABANDON_QUEST "Abandon \"%s\"?" (GS:6; StaticPopup.lua:1687-1702: YES / NO, whileDead).</summary>
        private void Abandon()
        {
            QuestView q = Selected();
            if (q == null || Ctx.S == null) return;
            int id = q.Id;
            UiContext ctx = Ctx;
            UiPopup.Show(new UiPopupSpec
            {
                Which = "ABANDON_QUEST", Text = UiGlobalStrings.FormatOr("ABANDON_QUEST", "Abandon \"%s\"?", q.Title ?? ""), Data = id,
                Button1 = UiGlobalStrings.G("YES", "Yes"), Button2 = UiGlobalStrings.G("NO", "No"),
                WhileDead = true, Exclusive = true, HideOnEscape = true,
                OnAccept = () => { if (ctx.S != null) { WowLog.Info("UI: quest " + id + " abandoned (ABANDON_QUEST)."); ctx.S.AbandonQuest(id); } }
            });
        }

        /// <summary>QuestLogFrameShareButton: QuestLogPushQuest (enabled when GetQuestLogPushable and in a party, QuestLogFrame.lua:859-874).</summary>
        private void Share()
        {
            QuestView q = Selected();
            if (q == null || Ctx.S == null || !q.Sharable || Ctx.P.Group.Count == 0) return;
            Ctx.S.ShareQuest(q.Id);
        }

        /// <summary>QuestLogFrameShowMapButton OnClick: "WorldMap_OpenToQuest(questID, self:GetParent())" with the selected quest's id.</summary>
        private void ShowMap()
        {
            QuestView q = Selected();
            if (q == null) return;
            WowLog.Info("UI quest map: Show Map - quest " + q.Id + " '" + q.Title + "' (" + (q.Pois == null ? "no POI data" : q.Pois.Count + " POI areas") + ").");
            WowQuestMap.OpenToQuest(Ctx.P, q.Id);
        }

        /// <summary>QuestLogFrameTrackButton: watch / unwatch (client-side, QuestWatch; the label "Untrack" while watched is ours).</summary>
        private void Track()
        {
            QuestView q = Selected();
            if (q == null) return;
            QuestWatch.Toggle(q.Id, Ctx);
            _sig = int.MinValue;
        }

        private static int Wheel(RectTransform r)
        {
            if (r == null || WowPlayer.XrPresent) return 0;
            try
            {
                float y = Input.mouseScrollDelta.y;
                if (Mathf.Abs(y) < 0.01f) return 0;
                if (!RectTransformUtility.RectangleContainsScreenPoint(r, Input.mousePosition, null)) return 0;
                return y > 0f ? -1 : 1;
            }
            catch (Exception) { return 0; }   // the project may run the new Input System only
        }

        // ------------------------------------------------------------------ the detail pane

        private void ScrollDetail(float d)
        {
            _offset = Mathf.Clamp(_offset + d, 0f, Mathf.Max(0f, _detailH - DetailH));
            UiKit.Move(_detail, 0f, -_offset);
            UpdateDetailBar();
        }

        private void UpdateDetailBar()
        {
            bool scrolls = _detailH > DetailH + 1f;
            if (_dUp.gameObject.activeSelf != scrolls) { _dUp.gameObject.SetActive(scrolls); _dDown.gameObject.SetActive(scrolls); }
            _knob.enabled = scrolls;
            if (!scrolls) return;
            _dUp.Interactable = _offset > 0.5f;
            _dDown.Interactable = _offset < _detailH - DetailH - 0.5f;
            float travel = DetailH - 40f - 24f;                               // between the arrows, minus the knob
            float k = Mathf.Clamp01(_offset / Mathf.Max(1f, _detailH - DetailH));
            UiKit.Move(_knob.rectTransform, 305f, DetailY + 20f + travel * k);
        }

        private void Place(TextMeshProUGUI t, string text, float gapAfter)
        {
            if (string.IsNullOrEmpty(text)) return;
            t.text = text;
            Vector2 pref = t.GetPreferredValues(text, TextW, 0f);
            float h = Mathf.Ceil(pref.y) + 2f;
            UiKit.Move(t.rectTransform, 4f, _y);
            UiKit.Size(t.rectTransform, TextW, h);
            t.enabled = true;
            _y += h + gapAfter;
        }

        private int PlaceItems(List<ItemView> list, int n)
        {
            int placed = 0;
            for (int i = 0; i < list.Count && n < MaxItems; i++)
            {
                ItemView it = list[i];
                UiButton b = _items[n];
                int col = placed % 2;
                if (col == 0 && placed > 0) _y += 37f;
                UiKit.Move(b.Rt, 2f + col * 138f, _y);
                b.gameObject.SetActive(true);
                if (!ReferenceEquals(_itemView[n], it)) { _itemLoaded[n] = false; b.Icon.enabled = false; }
                _itemView[n] = it;
                _itemName[n].text = it.Name ?? "";
                _itemName[n].color = it.Quality >= 2 ? UiArt.QualityColor(it.Quality) : Color.white;
                b.Count.SetText(it.Count > 1 ? it.Count.ToString() : "");
                placed++;
                n++;
            }
            if (placed > 0) _y += 40f;
            return n;
        }

        /// <summary>The selected quest top to bottom: what the quest giver's details page showed, with the log's progress.</summary>
        private void LayoutDetail(QuestView q)
        {
            _dTitle.enabled = _dObjectives.enabled = _dLines.enabled = _dDescHead.enabled = _dDesc.enabled = false;
            _dRewHead.enabled = _dSpell.enabled = _dChoose.enabled = _dReceive.enabled = _dXp.enabled = false;
            for (int i = 0; i < MaxItems; i++) { _items[i].gameObject.SetActive(false); _itemView[i] = null; }
            if (q.Id != _detailFor) { _detailFor = q.Id; _offset = 0f; }
            _y = 2f;
            Place(_dTitle, q.Title, 4f);
            Sb.Length = 0;
            if (q.TimeLeft > 0f && !q.Failed) Sb.Append("<color=#ffd100>").Append("Time remaining:").Append("</color> ").Append(UiQuestTracker.SecondsToTime(q.TimeLeft));   // ours (no GlobalStrings key)
            if (Sb.Length > 0 && !string.IsNullOrEmpty(q.ObjectivesText)) Sb.Append('\n');
            if (!string.IsNullOrEmpty(q.ObjectivesText)) Sb.Append(q.ObjectivesText);
            Place(_dObjectives, Sb.ToString(), 4f);
            Sb.Length = 0;
            for (int o = 0; o < q.Objectives.Count; o++)
            {
                QuestObjectiveView ob = q.Objectives[o];
                if (Sb.Length > 0) Sb.Append('\n');
                Sb.Append(ob.Done ? "<color=#9d9d9d>- " : "<color=#ffffff>- ").Append(ob.Text);
                if (ob.Need > 1 || !ob.Done) Sb.Append(": ").Append(Mathf.Min(ob.Have, ob.Need)).Append('/').Append(ob.Need);
                Sb.Append("</color>");
            }
            if (q.State == QuestState.Complete && !q.Failed) { if (Sb.Length > 0) Sb.Append('\n'); Sb.Append("<color=#00ff00>Ready to hand in.</color>"); }
            if (q.SuggestedPlayers > 1) { if (Sb.Length > 0) Sb.Append('\n'); Sb.Append("<color=#ffd100>").Append(UiGlobalStrings.G("GROUP", "Group")).Append(" (").Append(q.SuggestedPlayers).Append(")</color>"); }   // GS:3853
            Place(_dLines, Sb.ToString(), 8f);
            if (!string.IsNullOrEmpty(q.Description))
            {
                Place(_dDescHead, UiGlobalStrings.G("QUEST_DESCRIPTION", "Description"), 2f);
                Place(_dDesc, q.Description, 8f);
            }
            bool any = q.RewardXp > 0 || q.RewardMoney > 0 || q.RewardItems.Count > 0 || q.RewardChoices.Count > 0 || q.RewardSpellId != 0;
            if (any)
            {
                Place(_dRewHead, "Rewards", 2f);
                int n = 0;
                if (q.RewardChoices.Count > 0)
                {
                    Place(_dChoose, q.RewardChoices.Count > 1 ? "You will be able to choose one of these rewards:" : "You will receive:", 3f);
                    n = PlaceItems(q.RewardChoices, n);
                }
                if (q.RewardItems.Count > 0)
                {
                    Place(_dReceive, q.RewardChoices.Count > 0 ? "You will also receive:" : "You will receive:", 3f);
                    n = PlaceItems(q.RewardItems, n);
                }
                // iteration 11: REWARD_SPELL / REWARD_AURA (QuestInfo.lua:382-392; GS:6141 / 6134)
                if (q.RewardSpellId != 0)
                    Place(_dSpell, (q.RewardSpellLearned ? UiGlobalStrings.G("REWARD_SPELL", "You will learn:") : UiGlobalStrings.G("REWARD_AURA", "The following spell will be cast on you:"))
                        + " <color=#ffd100>" + (q.RewardSpellName ?? ("spell " + q.RewardSpellId)) + "</color>", 3f);
                Sb.Length = 0;
                if (q.RewardXp > 0) Sb.Append("Experience: <color=#ffd100>").Append(q.RewardXp).Append("</color>");
                if (q.RewardMoney > 0) { if (Sb.Length > 0) Sb.Append('\n'); Sb.Append("Money: ").Append(UiArt.MoneyText(q.RewardMoney)); }
                Place(_dXp, Sb.ToString(), 3f);
            }
            _detailH = _y + 6f;
            UiKit.Size(_detail, DetailW, Mathf.Max(DetailH, _detailH));
            _offset = Mathf.Clamp(_offset, 0f, Mathf.Max(0f, _detailH - DetailH));
            UiKit.Move(_detail, 0f, -_offset);
            UpdateDetailBar();
        }

        private void HideDetail()
        {
            _dTitle.enabled = _dObjectives.enabled = _dLines.enabled = _dDescHead.enabled = _dDesc.enabled = false;
            _dRewHead.enabled = _dSpell.enabled = _dChoose.enabled = _dReceive.enabled = _dXp.enabled = false;
            for (int i = 0; i < MaxItems; i++) { _items[i].gameObject.SetActive(false); _itemView[i] = null; }
            _detailH = 0f;
            _offset = 0f;
            UpdateDetailBar();
        }

        // ------------------------------------------------------------------ refresh

        public override void Refresh(float now)
        {
            List<QuestView> log = Log;
            if (log == null) return;
            PlayerState p = Ctx.P;
            int wheel = Wheel(_list);
            if (wheel != 0) ScrollRows(wheel);
            int dWheel = Wheel(_detailClip);
            if (dWheel != 0) ScrollDetail(dWheel * 32f);
            for (int i = 0; i < MaxItems; i++)
            {
                if (_itemView[i] == null || _itemLoaded[i]) continue;
                SetIcon(_items[i], _itemView[i].IconPath, ref _itemPath[i], ref _itemLoaded[i]);
            }
            if (QuestWatch.ShowSerial != _showSeen)
            {
                _showSeen = QuestWatch.ShowSerial;                                // the tracker's title click / "Open Quest Details" (H-U2 e)
                for (int i = 0; i < log.Count; i++) if (log[i].Id == QuestWatch.ShowQuestId) { _selectedId = log[i].Id; ShowRow(i); _sig = int.MinValue; break; }
            }
            int lvl = p.Level;
            int sig = log.Count * 131 + _selectedId * 7 + lvl + _top * 977 + QuestWatch.Serial * 7919 + p.Group.Count * 3 + p.DailyQuestsDone * 13 + _foldSerial * 104729;
            for (int i = 0; i < log.Count; i++)
            {
                QuestView q = log[i];
                sig = sig * 31 + q.Id * 5 + (int)q.State + (q.Failed ? 11 : 0) + (q.TimeLeft >= 0f ? Mathf.CeilToInt(q.TimeLeft) * 17 : 0)
                      + (q.Title != null ? q.Title.GetHashCode() : 0) + q.ZoneOrSort * 3 + (q.ZoneName != null ? q.ZoneName.GetHashCode() : 0);   // quest map fix: names that arrive later redraw too
                sig = sig * 7 + (q.Description != null ? q.Description.Length : 0) + q.RewardXp + q.RewardMoney + q.RewardItems.Count * 3 + q.RewardChoices.Count * 5;   // 13.7: the detail pane
                for (int r = 0; r < q.RewardItems.Count; r++) sig = sig * 3 + (q.RewardItems[r].Name != null ? q.RewardItems[r].Name.GetHashCode() : 0);
                for (int r = 0; r < q.RewardChoices.Count; r++) sig = sig * 3 + (q.RewardChoices[r].Name != null ? q.RewardChoices[r].Name.GetHashCode() : 0);
                for (int o = 0; o < q.Objectives.Count; o++) sig = sig * 17 + q.Objectives[o].Have + (q.Objectives[o].Text != null ? q.Objectives[o].Text.GetHashCode() : 0);
            }
            // QuestLog_UpdateMapButton (QuestLogFrame.lua:319-325): "if ( WatchFrame.showObjectives and GetNumQuestLogEntries() ~= 0 ) then Show()"
            bool map = WowQuestMap.ShowObjectives && log.Count > 0;
            if (_showMap.gameObject.activeSelf != map) _showMap.gameObject.SetActive(map);
            if (sig == _sig) return;
            _sig = sig;
            QuestView sel = Selected();
            if (sel != null) _selectedId = sel.Id;
            BuildEntries(log);
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _entryQuest.Count - Rows));
            Sb.Length = 0;
            if (p.DailyQuestsDone > 0)                                            // QUEST_LOG_DAILY_COUNT_TEMPLATE "Daily: |cffffffff%d/%d|r" (GS:5895)
                Sb.Append(UiGlobalStrings.FormatOr("QUEST_LOG_DAILY_COUNT_TEMPLATE", "Daily: |cffffffff%d/%d|r", p.DailyQuestsDone, 25).Replace("|cffffffff", "<color=#ffffff>").Replace("|r", "</color>")).Append("   ");
            Sb.Append(log.Count).Append(" / ").Append(MaxQuests);
            _count.text = Sb.ToString();
            _empty.enabled = log.Count == 0;
            for (int i = 0; i < Rows; i++)
            {
                int e = _top + i;
                bool on = e < _entryQuest.Count;
                _row[i].gameObject.SetActive(on);
                if (!on) continue;
                int qi = _entryQuest[e];
                if (qi < 0)
                {
                    string key = _entryHeader[e];                                 // a header (QuestLogTitleButton isHeader): fold icon + the zone's name
                    _rowFold[i].enabled = true;
                    _rowFold[i].texture = UiArt.Tex(_folded.Contains(key) ? @"Interface\Buttons\UI-PlusButton-Up.blp" : @"Interface\Buttons\UI-MinusButton-Up.blp");
                    _rowFold[i].color = Color.white;
                    _rowText[i].text = key;
                    _rowText[i].color = new Color(0.75f, 0.75f, 0.75f);           // ours: the header grey
                    _rowSel[i].enabled = false;
                    continue;
                }
                QuestView q = log[qi];
                _rowFold[i].enabled = false;
                Sb.Length = 0;
                Sb.Append('[').Append(q.Level).Append("] ").Append(q.Title);
                if (q.Failed) Sb.Append(' ').Append(UiGlobalStrings.G("QUEST_FAILED_TAG", "- Failed"));             // GS:5888
                else if (q.State == QuestState.Complete) Sb.Append(" (Complete)");
                if (q.Daily) Sb.Append(" (").Append(UiGlobalStrings.G("DAILY", "Daily")).Append(')');            // the daily tag (DAILY_QUEST_TAG_TEMPLATE GS:1904)
                if (q.TimeLeft > 0f && !q.Failed) Sb.Append("  ").Append(UiQuestTracker.SecondsToTime(q.TimeLeft));
                _rowText[i].text = Sb.ToString();
                _rowText[i].color = q.Failed ? new Color(1f, 0.1f, 0.1f) : UiArt.DifficultyColor(q.Level, lvl);
                _rowSel[i].enabled = q == sel;
            }
            bool scrolls = _entryQuest.Count > Rows;
            _up.gameObject.SetActive(scrolls);
            _down.gameObject.SetActive(scrolls);
            _up.Interactable = _top > 0;
            _down.Interactable = _top < _entryQuest.Count - Rows;
            _abandon.Interactable = sel != null;
            _share.Interactable = sel != null && sel.Sharable && p.Group.Count > 0;
            _track.Interactable = sel != null;
            _track.SetLabel(sel != null && QuestWatch.IsWatched(sel.Id) ? "Untrack" : UiGlobalStrings.G("TRACK_QUEST_ABBREV", "Track"));   // "Untrack": ours
            if (sel == null) { HideDetail(); return; }
            LayoutDetail(sel);
        }
    }

    /// <summary>Shared quest reward text.</summary>
    public static class UiQuestText
    {
        public static void Rewards(StringBuilder sb, QuestView q, bool light)
        {
            bool any = q.RewardXp > 0 || q.RewardMoney > 0 || q.RewardItems.Count > 0 || q.RewardChoices.Count > 0 || q.RewardSpellId != 0;
            if (!any) return;
            sb.Append(light ? "\n<color=#ffd100>Rewards:</color> " : "\nRewards: ");
            bool first = true;
            if (q.RewardXp > 0) { sb.Append(q.RewardXp).Append(" experience"); first = false; }
            if (q.RewardMoney > 0) { if (!first) sb.Append(", "); sb.Append(UiArt.MoneyText(q.RewardMoney)); first = false; }
            for (int i = 0; i < q.RewardItems.Count; i++)
            {
                if (!first) sb.Append(", ");
                ItemView it = q.RewardItems[i];
                sb.Append("<color=").Append(UiArt.QualityHex(it.Quality)).Append('>').Append(it.Name).Append("</color>");
                if (it.Count > 1) sb.Append(" x").Append(it.Count);
                first = false;
            }
            if (q.RewardChoices.Count > 0)
            {
                sb.Append(light ? "\n<color=#ffd100>Choose one:</color> " : "\nChoose one: ");
                for (int i = 0; i < q.RewardChoices.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    ItemView it = q.RewardChoices[i];
                    sb.Append("<color=").Append(UiArt.QualityHex(it.Quality)).Append('>').Append(it.Name).Append("</color>");
                }
            }
            if (q.RewardSpellId != 0)                                         // iteration 11: REWARD_SPELL / REWARD_AURA (QuestInfo.lua:382-392; GS:6141 / 6134)
            {
                string label = q.RewardSpellLearned ? UiGlobalStrings.G("REWARD_SPELL", "You will learn:") : UiGlobalStrings.G("REWARD_AURA", "The following spell will be cast on you:");
                sb.Append(light ? "\n<color=#ffd100>" + label + "</color> " : "\n" + label + " ").Append(q.RewardSpellName ?? ("spell " + q.RewardSpellId));
            }
        }
    }
}
