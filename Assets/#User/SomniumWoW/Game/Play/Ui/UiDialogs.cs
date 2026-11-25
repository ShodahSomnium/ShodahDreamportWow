using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// The NPC quest frame (QuestFrame.xml): the parchment UI-QuestGreeting panel, the NPC's name, and four pages - greeting with
    /// the NPC's quests (ActiveQuestIcon / IncompleteQuestIcon / AvailableQuestIcon), quest details with Accept / Decline, progress
    /// with Continue / Goodbye, and completion with the reward choice and Complete Quest. Content 300x334 at (23, 81), clipped,
    /// scrolled with the art's scroll arrows when longer; buttons at BOTTOMLEFT (22, 72) and BOTTOMRIGHT (-39, 73).
    /// All quest text shown here is the zone's own writing (Play/Content), never Blizzard's.
    /// </summary>
    public sealed class UiQuestDialog : UiWindow
    {
        private enum Page { Greeting, Detail, Progress, Reward }

        private const float ContentW = 300f, ContentH = 334f;
        private const int MaxTitles = 32, MaxItems = 6;                      // iteration 11 (npc H8): quests + gossip options share the pool (NUMGOSSIPBUTTONS 32)

        private readonly TextMeshProUGUI _npcName;
        private readonly RectTransform _scroll;
        private readonly TextMeshProUGUI _head, _body, _head2, _body2, _head3, _moneyLine, _choose;
        private readonly UiButton[] _titles = new UiButton[MaxTitles];
        private readonly TextMeshProUGUI[] _titleText = new TextMeshProUGUI[MaxTitles];
        private readonly RawImage[] _titleIcon = new RawImage[MaxTitles];
        private readonly int[] _titleQuest = new int[MaxTitles];
        private readonly int[] _titleOption = new int[MaxTitles];              // iteration 11 (npc H8): the NpcDialog.Options index of an option row, -1 = a quest row
        // GossipIcon art order (GossipFrame.lua:146-163; proposal-npc.md H8)
        private static readonly string[] GossipIconNames = { "Gossip", "Vendor", "Taxi", "Trainer", "Healer", "Binder", "Banker", "Petition", "Tabard", "BattleMaster", "Unlearn" };
        private readonly UiButton[] _items = new UiButton[MaxItems];
        private readonly TextMeshProUGUI[] _itemName = new TextMeshProUGUI[MaxItems];
        private readonly RawImage[] _itemSel = new RawImage[MaxItems];
        private readonly ItemView[] _itemView = new ItemView[MaxItems];
        private readonly bool[] _itemIsChoice = new bool[MaxItems];
        // out11_questfix: the RewardChoices index each item button shows (set at layout), -1 = not a choice. The choice used to be looked up
        // by object reference (RewardChoices.IndexOf(_itemView[k])), but the sessions rebuild the dialog's views (OnlineSession QBuildDialog
        // on every _qDialogDirty, OfflineSession RefreshDialog) without a relayout here: the tap found nothing, _choice stayed -1 and
        // "Complete Quest" stayed off ("Choose a reward first.") for every quest with 2+ reward choices.
        private readonly int[] _itemChoiceIdx = new int[MaxItems];
        private readonly string[] _itemPath = new string[MaxItems];
        private readonly bool[] _itemLoaded = new bool[MaxItems];
        private readonly UiButton _left, _right, _up, _down;

        private Page _page;
        private int _questId, _choice = -1, _sig = int.MinValue;
        private int _openSeen;                                                  // iteration 11: NpcDialog.OpenSerial already honoured
        private TextMeshProUGUI _spellLine;                                     // iteration 11: the reward spell line
        private ulong _npc;
        private float _contentH, _offset;
        private NpcDialog _dialog;

        public UiQuestDialog(Transform parent, UiContext ctx, float x, float y)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "Quest Frame", x, y, W, H);
            // Lead fix (2026-10-03, the user: "The bottom left of the quest box, it is a see through place where button normally is"):
            // UI-QuestGreeting-BotLeft has a TRANSPARENT button socket (texture px ~21..96 x 163..182, measured with the harness UiPng
            // probe) that WoW fills with the left button; on pages without one (greeting, goodbye only) the world showed through.
            // A dark plate behind the art fills it (colour ours, the shade of the art's bottom bar); the art's socket rim stays on top.
            UiKit.Solid(Root, "Left button socket", new Color(0.13f, 0.12f, 0.11f, 1f), 20f, 418f, 78f, 22f);
            UiKit.Frame4(Root, "Art", UiArt.QuestGreeting + "TopLeft.blp", UiArt.QuestGreeting + "TopRight.blp", UiArt.QuestGreeting + "BotLeft.blp", UiArt.QuestGreeting + "BotRight.blp");
            Portrait(UiArt.Tex(UiArt.PortraitNpc));
            _npcName = UiKit.Text(Root, "NPC name", "", 12f, Color.white, TextAlignmentOptions.Center, 78f, 21f, 236f, 18f);
            _npcName.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.CloseButton(Root, 326f, 15f, Goodbye);

            RectTransform clip = UiKit.Rect(Root, "Content", 23f, 81f, ContentW, ContentH);
            clip.gameObject.AddComponent<RectMask2D>();
            _scroll = UiKit.Rect(clip, "Scroll", 0f, 0f, ContentW, ContentH);
            _head = QuestTitle("Heading");
            _body = QuestBody("Text");
            for (int i = 0; i < MaxTitles; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(_scroll, "Quest " + (i + 1), 0f, 0f, ContentW, 16f);
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 0f, ContentW, 16f);
                b.Hilite.enabled = false;
                _titleIcon[i] = UiKit.Pic(b.transform, "Icon", UiArt.Tex(UiArt.AvailableQuestIcon), 4f, 0f, 16f, 16f);
                _titleText[i] = UiKit.Text(b.transform, "Title", "", 12f, UiArt.QuestText, TextAlignmentOptions.Left, 22f, 0f, 275f, 16f, null, false);
                _titleText[i].textWrappingMode = TextWrappingModes.NoWrap;
                b.Click = () => { if (_titleOption[k] >= 0) { ChooseOption(_titleOption[k]); return; } UiSound.Named("igQuestListSelect"); OpenQuest(_titleQuest[k]); };   // QuestFrame.lua:321
                b.gameObject.SetActive(false);
                _titles[i] = b;
            }
            _head2 = QuestTitle("Heading 2");
            _body2 = QuestBody("Text 2");
            _head3 = QuestTitle("Heading 3");
            for (int i = 0; i < MaxItems; i++)
            {
                int k = i;
                // LargeItemButtonTemplate: icon + UI-QuestItemNameFrame + name, 147 wide, two per row.
                UiButton b = UiKit.Button(_scroll, "Reward " + (i + 1), 0f, 0f, 147f, 41f);
                _itemSel[i] = UiKit.Pic(b.transform, "Chosen", UiArt.Tex(UiArt.QuestItemHighlight), -8f, -10f, 256f * 0.66f, 64f * 0.9f);
                _itemSel[i].enabled = false;
                UiKit.Pic(b.transform, "Name frame", UiArt.Tex(UiArt.QuestItemNameFrame), 30f, -12f, 128f, 64f);
                b.Icon = UiKit.Pic(b.transform, "Icon", null, 0f, 2f, 37f, 37f);
                b.Icon.enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 2f, 37f, 37f);
                b.Hilite.enabled = false;
                b.Count = UiKit.Text(b.transform, "Count", "", 12f, Color.white, TextAlignmentOptions.BottomRight, 0f, 24f, 35f, 14f, UiArt.Numbers);
                _itemName[i] = UiKit.Text(b.transform, "Name", "", 11f, UiArt.QuestText, TextAlignmentOptions.Left, 43f, 2f, 100f, 37f, null, false);
                b.Click = () => ChooseItem(k);
                b.Tip = () => UiTooltip.Item(_itemView[k], _itemIsChoice[k] && _page == Page.Reward ? "<color=#00ff00>Tap to choose this reward.</color>" : null);
                b.gameObject.SetActive(false);
                _items[i] = b;
            }
            _choose = QuestBody("Choose");
            _moneyLine = QuestBody("Money");
            _spellLine = QuestBody("Spell");                                    // iteration 11

            _left = UiKit.PanelButton(Root, "Accept", 22f, 418f, 120f, 22f, LeftPressed);
            _right = UiKit.PanelButton(Root, "Decline", 267f, 417f, 78f, 22f, RightPressed);
            _up = UiKit.ArtButton(Root, "Scroll up", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 326f, 80f, 20f, () => ScrollBy(-80f));
            _down = UiKit.ArtButton(Root, "Scroll down", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 326f, 396f, 20f, () => ScrollBy(80f));
            Root.gameObject.SetActive(false);
        }

        private TextMeshProUGUI QuestTitle(string name)
        {
            // QuestTitleFont: MORPHEUS 18, black (FontStyles.xml:173).
            TextMeshProUGUI t = UiKit.Text(_scroll, name, "", 17f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 5f, 0f, 285f, 22f, UiArt.Morpheus, false);
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private TextMeshProUGUI QuestBody(string name)
        {
            // QuestFont: FRIZQT 13, black (FontStyles.xml:185).
            TextMeshProUGUI t = UiKit.Text(_scroll, name, "", 12f, UiArt.QuestText, TextAlignmentOptions.TopLeft, 5f, 0f, 285f, 20f, null, false);
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        protected override void OnOpen()
        {
            _page = Page.Greeting;
            _sig = int.MinValue;
            _offset = 0f;
        }

        private QuestView Quest(int id)
        {
            if (_dialog == null) return null;
            for (int i = 0; i < _dialog.Quests.Count; i++) if (_dialog.Quests[i].Id == id) return _dialog.Quests[i];
            return null;
        }

        private void OpenQuest(int id)
        {
            QuestView q = Quest(id);
            if (q == null) return;
            _questId = id;
            _choice = -1;
            _page = q.State == QuestState.Available ? Page.Detail : Page.Progress;
            _sig = int.MinValue;
            _offset = 0f;
        }

        private void ChooseItem(int k)
        {
            if (_page != Page.Reward || !_itemIsChoice[k]) return;
            _choice = ChoiceIndexOf(k);
            _sig = int.MinValue;
        }

        private int ChoiceIndexOf(int k)
        {
            QuestView q = Quest(_questId);
            if (q == null || _itemView[k] == null || !_itemIsChoice[k]) return -1;
            int i = _itemChoiceIdx[k];                                          // out11_questfix: the laid-out index, not the object
            return i >= 0 && i < q.RewardChoices.Count ? i : -1;
        }

        private void LeftPressed()
        {
            IWowSession s = Ctx.S;
            QuestView q = Quest(_questId);
            if (s == null) return;
            switch (_page)
            {
                case Page.Detail:
                    if (q != null) s.AcceptQuest(q.Id);
                    ToGreeting();
                    break;
                case Page.Progress:
                    if (q != null && q.State == QuestState.Complete) { _page = Page.Reward; _choice = q.RewardChoices.Count == 1 ? 0 : -1; _sig = int.MinValue; _offset = 0f; }
                    break;
                case Page.Reward:
                    if (q == null) { ToGreeting(); break; }
                    if (q.RewardChoices.Count > 1 && _choice < 0) { Ctx.Say("Choose a reward first.", UiArt.ErrorRed); break; }
                    s.CompleteQuest(q.Id, Mathf.Max(0, _choice));
                    ToGreeting();
                    break;
            }
        }

        private void RightPressed()
        {
            // iteration 11: a conversation the server opened on one quest closes on Decline / Goodbye (WoW's QuestFrame; a shared quest is declined by it)
            if (_page == Page.Greeting || (_dialog != null && _dialog.Quests.Count == 1 && _dialog.OpenSerial != 0)) Goodbye();
            else ToGreeting();
        }

        private void Goodbye()
        {
            if (Ctx.S != null) Ctx.S.CloseDialog();
        }

        /// <summary>Iteration 11 (npc H8): a gossip option row. A coded option asks GOSSIP_ENTER_CODE, a paid / confirmed one GOSSIP_CONFIRM
        /// (UIParent.lua:913-934), else CMSG_GOSSIP_SELECT_OPTION through the session.</summary>
        private void ChooseOption(int k)
        {
            var d = _dialog;
            if (d == null || d.Options == null || k < 0 || k >= d.Options.Count || Ctx.S == null) return;
            GossipOptionView o = d.Options[k];
            UiSound.Named("igQuestListSelect");
            WowLog.Info("UI: gossip option " + o.Index + " '" + (o.Text ?? "") + "' chosen" + (o.Coded ? " (code asked)" : o.BoxMoney > 0 || !string.IsNullOrEmpty(o.BoxText) ? " (confirm asked)" : ""));
            if (o.Coded) UiPopup.Show(UiNpcPopups.EnterCode(Ctx, o.Index));                 // GOSSIP_ENTER_CODE -> SelectGossipOption(o.Index, code)
            else if (!string.IsNullOrEmpty(o.BoxText) || o.BoxMoney > 0) UiPopup.Show(UiNpcPopups.GossipConfirm(Ctx, o.Index, o.BoxText, o.BoxMoney));
            else Ctx.S.SelectGossipOption(o.Index, null);
        }

        private void ToGreeting()
        {
            _page = Page.Greeting;
            _sig = int.MinValue;
            _offset = 0f;
        }

        private void ScrollBy(float d)
        {
            _offset = Mathf.Clamp(_offset + d, 0f, Mathf.Max(0f, _contentH - ContentH));
            UiKit.Move(_scroll, 0f, -_offset);
            UpdateArrows();
        }

        private void UpdateArrows()
        {
            bool scrolls = _contentH > ContentH + 1f;
            _up.gameObject.SetActive(scrolls);
            _down.gameObject.SetActive(scrolls);
            _up.Interactable = _offset > 0.5f;
            _down.Interactable = _offset < _contentH - ContentH - 0.5f;
        }

        public override void Refresh(float now)
        {
            IWowSession s = Ctx.S;
            _dialog = s != null ? s.CurrentDialog : null;
            if (_dialog == null) return;
            if (_dialog.NpcGuid != _npc) { _npc = _dialog.NpcGuid; _page = Page.Greeting; _sig = int.MinValue; _offset = 0f; }
            if (_dialog.OpenSerial != 0 && _dialog.OpenSerial != _openSeen)
            {
                _openSeen = _dialog.OpenSerial;                                  // iteration 11: the page the server sent (QuestFrame opens it directly)
                QuestView oq = Quest(_dialog.OpenQuestId);
                if (oq != null)
                {
                    _questId = oq.Id; _choice = oq.RewardChoices.Count == 1 ? 0 : -1; _offset = 0f; _sig = int.MinValue;
                    _page = _dialog.OpenPage == 1 ? Page.Detail : _dialog.OpenPage == 2 ? Page.Progress : Page.Reward;
                    if (_page == Page.Reward && oq.State != QuestState.Complete) _page = Page.Progress;
                }
            }
            int sig = (int)_page * 1000003 + _questId * 31 + _choice + _dialog.Quests.Count * 7 + (_dialog.Greeting != null ? _dialog.Greeting.Length : 0)
                    + (_dialog.Options != null ? _dialog.Options.Count * 13 : 0);   // iteration 11 (npc H8)
            for (int i = 0; i < _dialog.Quests.Count; i++)
            {
                QuestView q = _dialog.Quests[i];
                sig = sig * 31 + q.Id * 3 + (int)q.State;
                for (int o = 0; o < q.Objectives.Count; o++) sig = sig * 7 + q.Objectives[o].Have + (q.Objectives[o].Text != null ? q.Objectives[o].Text.GetHashCode() : 0);
                // lead fix 2026-11-03 (user: rewards shown as "Item 15458"): the item names arrive after the window opens (the session's
                // CMSG_ITEM_QUERY_SINGLE answers rebuild the views) - a new name changes the signature, so the window lays out again
                // (UiQuestLog's 13.7 rule)
                for (int r = 0; r < q.RewardItems.Count; r++) sig = sig * 3 + (q.RewardItems[r].Name != null ? q.RewardItems[r].Name.GetHashCode() : 0) + q.RewardItems[r].Quality;
                for (int r = 0; r < q.RewardChoices.Count; r++) sig = sig * 3 + (q.RewardChoices[r].Name != null ? q.RewardChoices[r].Name.GetHashCode() : 0) + q.RewardChoices[r].Quality;
            }
            if (_page != Page.Greeting && Quest(_questId) == null) { _page = Page.Greeting; sig ^= 0x55; }
            if (sig != _sig)
            {
                _sig = sig;
                Layout();
            }
            for (int i = 0; i < MaxItems; i++)
            {
                if (_itemView[i] == null || _itemLoaded[i]) continue;
                SetIcon(_items[i], _itemView[i].IconPath, ref _itemPath[i], ref _itemLoaded[i]);
            }
        }

        // ------------------------------------------------------------------ layout

        private float _y;

        private void Layout()
        {
            _npcName.text = _dialog.NpcName ?? "";
            _y = 8f;
            HideAll();
            QuestView q = Quest(_questId);
            switch (_page)
            {
                case Page.Greeting: LayoutGreeting(); break;
                case Page.Detail: LayoutDetail(q); break;
                case Page.Progress: LayoutProgress(q); break;
                case Page.Reward: LayoutReward(q); break;
            }
            _contentH = _y + 8f;
            UiKit.Size(_scroll, ContentW, Mathf.Max(ContentH, _contentH));
            _offset = Mathf.Clamp(_offset, 0f, Mathf.Max(0f, _contentH - ContentH));
            UiKit.Move(_scroll, 0f, -_offset);
            UpdateArrows();
        }

        private void HideAll()
        {
            _head.enabled = _body.enabled = _head2.enabled = _body2.enabled = _head3.enabled = _moneyLine.enabled = _choose.enabled = _spellLine.enabled = false;
            for (int i = 0; i < MaxTitles; i++) _titles[i].gameObject.SetActive(false);
            for (int i = 0; i < MaxItems; i++) { _items[i].gameObject.SetActive(false); _itemView[i] = null; }
        }

        private void PlaceText(TextMeshProUGUI t, string text, float gapAfter)
        {
            if (string.IsNullOrEmpty(text)) return;
            t.text = text;
            Vector2 pref = t.GetPreferredValues(text, 285f, 0f);
            float h = Mathf.Ceil(pref.y) + 2f;
            UiKit.Move(t.rectTransform, 5f, _y);
            UiKit.Size(t.rectTransform, 285f, h);
            t.enabled = true;
            _y += h + gapAfter;
        }

        private void LayoutGreeting()
        {
            PlaceText(_body, _dialog.Greeting, 12f);
            int n = 0;
            // Quests to hand in or in progress first ("Current Quests"), then the ones on offer ("Available Quests").
            for (int pass = 0; pass < 2; pass++)
            {
                bool header = false;
                for (int i = 0; i < _dialog.Quests.Count && n < MaxTitles; i++)
                {
                    QuestView q = _dialog.Quests[i];
                    bool available = q.State == QuestState.Available;
                    if ((pass == 0) == available) continue;
                    if (!header) { PlaceText(pass == 0 ? _head : _head2, pass == 0 ? "Current Quests" : "Available Quests", 4f); header = true; }
                    UiButton b = _titles[n];
                    _titleQuest[n] = q.Id;
                    _titleOption[n] = -1;
                    _titleText[n].text = q.Title ?? "";
                    _titleIcon[n].texture = UiArt.Tex(available ? UiArt.AvailableQuestIcon : q.State == QuestState.Complete ? UiArt.ActiveQuestIcon : UiArt.IncompleteQuestIcon);
                    UiKit.Move(b.Rt, 0f, _y);
                    b.gameObject.SetActive(true);
                    _y += 18f;
                    n++;
                }
                if (header) _y += 8f;
            }
            // iteration 11 (npc H8): one button per gossip option after the quests, its GossipIcon left of the text (GossipFrame.lua:146-163)
            var opts = _dialog.Options;
            for (int i = 0; opts != null && i < opts.Count && n < MaxTitles; i++)
            {
                GossipOptionView o = opts[i];
                if (o == null) continue;
                UiButton b = _titles[n];
                _titleQuest[n] = 0;
                _titleOption[n] = i;
                _titleText[n].text = o.Text ?? "";
                string icon = o.Icon >= 0 && o.Icon < GossipIconNames.Length ? GossipIconNames[o.Icon] : "Gossip";
                _titleIcon[n].texture = UiArt.Tex(@"Interface\GossipFrame\" + icon + "GossipIcon.blp");
                UiKit.Move(b.Rt, 0f, _y);
                b.gameObject.SetActive(true);
                _y += 18f;
                n++;
            }
            if (n == 0 && string.IsNullOrEmpty(_dialog.Greeting)) PlaceText(_body, "Greetings.", 4f);
            _left.gameObject.SetActive(false);
            _right.SetLabel("Goodbye");
            _right.gameObject.SetActive(true);
        }

        private void LayoutDetail(QuestView q)
        {
            if (q == null) { LayoutGreeting(); return; }
            PlaceText(_head, q.Title, 6f);
            PlaceText(_body, q.Description, 12f);
            PlaceText(_head2, "Quest Objectives", 4f);
            PlaceText(_body2, string.IsNullOrEmpty(q.ObjectivesText) ? ObjectiveLines(q) : q.ObjectivesText, 12f);
            LayoutRewards(q, false);
            _left.SetLabel("Accept");
            _left.gameObject.SetActive(true);
            _left.Interactable = true;
            _right.SetLabel("Decline");
            _right.gameObject.SetActive(true);
        }

        private void LayoutProgress(QuestView q)
        {
            if (q == null) { LayoutGreeting(); return; }
            PlaceText(_head, q.Title, 6f);
            string text = string.IsNullOrEmpty(q.ProgressText) ? (q.State == QuestState.Complete ? "You have done what was asked." : "You have not finished yet.") : q.ProgressText;
            PlaceText(_body, text, 12f);
            PlaceText(_body2, ObjectiveLines(q), 8f);
            _left.SetLabel("Continue");
            _left.gameObject.SetActive(true);
            _left.Interactable = q.State == QuestState.Complete;
            _right.SetLabel("Goodbye");
            _right.gameObject.SetActive(true);
        }

        private void LayoutReward(QuestView q)
        {
            if (q == null) { LayoutGreeting(); return; }
            PlaceText(_head, q.Title, 6f);
            PlaceText(_body, string.IsNullOrEmpty(q.CompletionText) ? "Well done." : q.CompletionText, 12f);
            LayoutRewards(q, true);
            _left.SetLabel("Complete Quest");
            _left.gameObject.SetActive(true);
            _left.Interactable = q.RewardChoices.Count <= 1 || _choice >= 0;
            _right.SetLabel("Cancel");
            _right.gameObject.SetActive(true);
        }

        private static string ObjectiveLines(QuestView q)
        {
            Sb.Length = 0;
            for (int o = 0; o < q.Objectives.Count; o++)
            {
                QuestObjectiveView ob = q.Objectives[o];
                if (o > 0) Sb.Append('\n');
                Sb.Append(ob.Text);
                if (ob.Need > 1 || !ob.Done) Sb.Append(": ").Append(Mathf.Min(ob.Have, ob.Need)).Append('/').Append(ob.Need);
                if (ob.Done) Sb.Append(" (Complete)");
            }
            return Sb.ToString();
        }

        private void LayoutRewards(QuestView q, bool choosing)
        {
            bool any = q.RewardXp > 0 || q.RewardMoney > 0 || q.RewardItems.Count > 0 || q.RewardChoices.Count > 0 || q.RewardSpellId != 0;
            if (!any) return;
            PlaceText(_head3, "Rewards", 4f);
            // iteration 11: REWARD_SPELL "You will learn:" (GS:6141) / REWARD_AURA "The following spell will be cast on you:" (GS:6134), QuestInfo.lua:382-392
            if (q.RewardSpellId != 0) PlaceText(_spellLine, (q.RewardSpellLearned ? "You will learn:" : "The following spell will be cast on you:") + " " + q.RewardSpellName, 4f);
            int n = 0;
            if (q.RewardChoices.Count > 0)
            {
                PlaceText(_choose, q.RewardChoices.Count > 1 ? "You will be able to choose one of these rewards:" : "You will receive:", 4f);
                n = PlaceItems(q.RewardChoices, true, n, choosing);
            }
            if (q.RewardItems.Count > 0)
            {
                if (q.RewardChoices.Count > 0) _y += 2f;
                n = PlaceItems(q.RewardItems, false, n, choosing);
            }
            Sb.Length = 0;
            if (q.RewardXp > 0) Sb.Append("Experience: ").Append(q.RewardXp);
            if (q.RewardMoney > 0) { if (Sb.Length > 0) Sb.Append('\n'); Sb.Append("Money: ").Append(UiArt.MoneyText(q.RewardMoney)); }
            if (Sb.Length > 0)
            {
                PlaceText(_moneyLine, Sb.ToString(), 4f);
            }
        }

        private int PlaceItems(List<ItemView> list, bool choices, int n, bool choosing)
        {
            for (int i = 0; i < list.Count && n < MaxItems; i++)
            {
                ItemView it = list[i];
                UiButton b = _items[n];
                int col = i % 2;
                if (col == 0 && i > 0) _y += 44f;
                UiKit.Move(b.Rt, col * 149f, _y);
                b.gameObject.SetActive(true);
                if (!ReferenceEquals(_itemView[n], it)) { _itemLoaded[n] = false; b.Icon.enabled = false; }
                _itemView[n] = it;
                _itemIsChoice[n] = choices;
                _itemChoiceIdx[n] = choices ? i : -1;                           // out11_questfix
                _itemName[n].text = it.Name ?? "";
                _itemName[n].color = it.Quality >= 2 ? UiArt.QualityColor(it.Quality) * 0.75f + new Color(0f, 0f, 0f, 0.25f) : UiArt.QuestText;
                b.Count.SetText(it.Count > 1 ? it.Count.ToString() : "");
                _itemSel[n].enabled = choices && choosing && _choice == i;       // out11_questfix: by index (the views may be rebuilt)
                n++;
            }
            _y += 46f;
            return n;
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// LootFrame (LootFrame.xml: 256x256 UI-LootPanel, buttons 37x37 at (24, 80) step 41, four per page). The coins are the first
    /// entry (the offline session hands money out with Loot All). Tap an item to take it; Loot All takes everything.
    /// </summary>
    public sealed class UiLoot : UiWindow
    {
        private const int PerPage = 4;
        private readonly UiButton[] _slot = new UiButton[PerPage];
        private readonly TextMeshProUGUI[] _name = new TextMeshProUGUI[PerPage];
        private readonly string[] _path = new string[PerPage];
        private readonly bool[] _loaded = new bool[PerPage];
        private readonly int[] _index = new int[PerPage];   // -1 = the coins, else LootView.Items index
        private readonly TextMeshProUGUI _title;
        private readonly UiButton _prev, _next;
        private int _page, _sig = int.MinValue;

        public UiLoot(Transform parent, UiContext ctx, float x, float y)
        {
            Ctx = ctx;
            W = 256f;
            H = 256f;
            Root = UiKit.Rect(parent, "Loot", x, y, W, H);
            UiKit.Pic(Root, "Art", UiArt.Tex(UiArt.LootPanel), 0f, 0f, 256f, 256f);
            UiKit.Pic(Root, "Portrait", UiArt.Tex(UiArt.TargetDead), 10f, 8f, 58f, 58f);
            _title = UiKit.Text(Root, "Title", "Loot", 12f, UiArt.Gold, TextAlignmentOptions.Left, 74f, 17f, 82f, 17f);
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.CloseButton(Root, 159f, 7f, () => { if (Ctx.S != null) Ctx.S.CloseLoot(); });
            for (int i = 0; i < PerPage; i++)
            {
                int k = i;
                UiButton b = UiKit.Slot(Root, "Loot " + (i + 1), 24f, 80f + i * 41f, 37f, true);
                b.Count = UiKit.Text(b.transform, "Count", "", 12f, Color.white, TextAlignmentOptions.BottomRight, 0f, 22f, 35f, 14f, UiArt.Numbers);
                b.Click = () => Take(k);
                b.RightClick = () => Take(k);
                b.Tip = () => Tip(k);
                _name[i] = UiKit.Text(Root, "Loot name " + (i + 1), "", 11f, Color.white, TextAlignmentOptions.Left, 66f, 80f + i * 41f, 110f, 37f);
                b.gameObject.SetActive(false);
                _slot[i] = b;
            }
            _prev = UiKit.ArtButton(Root, "Loot up", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.ButtonHilightSquare, 25f, 243f - 32f - 4f, 32f, () => { _page--; _sig = int.MinValue; });
            _next = UiKit.ArtButton(Root, "Loot down", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.ButtonHilightSquare, 142f, 243f - 32f - 4f, 32f, () => { _page++; _sig = int.MinValue; });
            UiKit.PanelButton(Root, "Loot All", 62f, 245f, 80f, 22f, () => { if (Ctx.S != null) Ctx.S.LootAll(); });
            Root.gameObject.SetActive(false);
        }

        protected override void OnOpen() { _page = 0; _sig = int.MinValue; }

        private void Take(int k)
        {
            IWowSession s = Ctx.S;
            if (s == null) return;
            if (_index[k] < 0) s.LootAll();
            else s.LootItem(_index[k]);
        }

        private string Tip(int k)
        {
            LootView l = Ctx.S != null ? Ctx.S.CurrentLoot : null;
            if (l == null) return null;
            if (_index[k] < 0) return "<size=14>" + UiArt.MoneyText(l.Money) + "</size>\n<color=#00ff00>Tap to take everything.</color>";
            return _index[k] < l.Items.Count ? UiTooltip.Item(l.Items[_index[k]], null) : null;
        }

        public override void Refresh(float now)
        {
            LootView l = Ctx.S != null ? Ctx.S.CurrentLoot : null;
            if (l == null) return;
            int entries = l.Items.Count + (l.Money > 0 ? 1 : 0);
            int pages = Mathf.Max(1, (entries + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            int sig = l.Money * 31 + _page * 7 + l.Items.Count + (int)l.CorpseGuid;
            for (int i = 0; i < l.Items.Count; i++)
            {
                ItemView si = l.Items[i];
                sig = sig * 31 + si.Entry * 3 + si.Count;
                // Stage O2 (integrator, lootdeath request 4): online names / quality / icons can arrive after the window opened
                sig = sig * 17 + (si.Name != null ? si.Name.GetHashCode() : 0) + si.Quality * 5 + si.DisplayId;
            }
            if (sig != _sig)
            {
                _sig = sig;
                EntityState corpse = Ctx.S.Find(l.CorpseGuid);
                _title.text = corpse != null && !string.IsNullOrEmpty(corpse.Name) ? corpse.Name : "Loot";
                _prev.gameObject.SetActive(pages > 1);
                _next.gameObject.SetActive(pages > 1);
                _prev.Interactable = _page > 0;
                _next.Interactable = _page < pages - 1;
                for (int k = 0; k < PerPage; k++)
                {
                    int e = _page * PerPage + k;
                    UiButton b = _slot[k];
                    bool on = e < entries;
                    b.gameObject.SetActive(on);
                    _name[k].enabled = on;
                    if (!on) continue;
                    int idx = l.Money > 0 ? e - 1 : e;
                    _index[k] = idx;
                    _loaded[k] = false;
                    _path[k] = null;
                    b.Icon.enabled = false;
                    if (idx < 0)
                    {
                        _name[k].text = UiArt.MoneyText(l.Money);
                        _name[k].color = Color.white;
                        b.Count.SetText("");
                    }
                    else
                    {
                        ItemView it = l.Items[idx];
                        _name[k].text = it.Name ?? "";
                        _name[k].color = UiArt.QualityColor(it.Quality);
                        b.Count.SetText(it.Count > 1 ? it.Count.ToString() : "");
                    }
                }
            }
            for (int k = 0; k < PerPage; k++)
            {
                if (!_slot[k].gameObject.activeSelf || _loaded[k]) continue;
                string path = _index[k] < 0 ? UiArt.CoinIcon : (_index[k] < l.Items.Count ? l.Items[_index[k]].IconPath : null);
                SetIcon(_slot[k], path, ref _path[k], ref _loaded[k]);
            }
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// Stage O2 (owner ghost): WoW's StaticPopups of the death flow in one popup (StaticPopup.lua, UIParent.lua), for both sessions,
    /// reading only PlayerState:
    /// - "DEATH" while lying dead (Dead and not Ghost): DEATH_RELEASE_TIMER "%d %s until release" + Release Spirit. The 6 minutes are the
    ///   client's own countdown (GetReleaseTimeRemaining from PLAYER_DEAD; AzerothCore auto-releases after the same m_deathTimer).
    /// - "RECOVER_CORPSE" for a ghost in reach of its corpse with the delay over (PlayerState.CanReclaim): "Resurrect now?" + Accept.
    ///   The delay countdown (RECOVER_CORPSE_TIMER) is shown by the corpse marker (UiGhost) while it runs.
    /// - "XP_LOSS" family next to the spirit healer (PlayerState.NearSpiritHealer): CONFIRM_XP_LOSS* + Accept / Cancel; with a penalty the
    ///   first Accept asks again (CONFIRM_XP_LOSS_AGAIN*), exactly as StaticPopup.lua's OnAccept "if ( data ) ... return 1".
    /// The dialog backdrop (GlueDialog insets 11/12/12/11, edge 32), StaticPopupButtons 128x21 (UI-DialogBox-Button TexCoords 0, 1, 0, 0.71875),
    /// two buttons 6 units either side of the centre, 16 from the bottom (StaticPopup.xml). The layout opens it with WantsOpen (integrator
    /// request 2); UiGhost (its MonoBehaviour on the layout) refreshes the text every frame.
    /// </summary>
    public sealed class UiDeath : UiWindow
    {
        private enum Mode { None, Death, Reclaim, Healer, HealerAgain, Resurrect, Request }   // rescorpse 2026-10-04: Request = StaticPopup "RESURRECT"

        /// <summary>Player.cpp:4654 m_deathTimer = 6 * MINUTE: the release countdown the client shows (GetReleaseTimeRemaining).</summary>
        public const float ReleaseSeconds = 360f;

        private const float PopupW = 320f, ButtonW = 128f, ButtonH = 21f;
        private readonly UiBackdrop _back;
        private readonly TextMeshProUGUI _text;
        private readonly UiButton _one, _two;
        private readonly UiSurface _surface;   // the popup's own surface in VR (grown to fit the text), null on the desktop HUD
        private Mode _mode = Mode.None;
        private float _deathShownAt = -1f;
        private int _shownSecond = int.MinValue;
        private string _shownText;
        private static bool _healerDeclined, _healerAsked;
        // Iteration 11 (designer quests): R1 VR follow (ours - uicore's popup rule 1.3 m; the angle and its delay are ours), R4 self-resurrection
        public const float FollowWalkMetres = 1.3f, FollowAngle = 50f, FollowAngleSeconds = 0.8f;
        private Vector3 _placedHead;
        private bool _placedHeadKnown, _selfResShown;
        private float _offViewSince = -1f;

        public UiDeath(Transform parent, UiContext ctx, float x, float y)
        {
            Ctx = ctx;
            W = PopupW;
            H = 100f;
            UiSurface surface = UiKit.Surface;
            _surface = surface != null && surface.IsWorld ? surface : null;
            Root = UiKit.Rect(parent, "Death Popup", x, y, W, H);
            _back = UiBackdrop.Make(Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            _back.Place(0f, 0f, W, H);
            _text = UiKit.Text(Root, "Text", "You have died.", 13f, Color.white, TextAlignmentOptions.Center, 20f, 16f, W - 40f, 30f);
            _one = PopupButton("Release Spirit", (W - ButtonW) * 0.5f, 56f, () => Press(true));
            _two = PopupButton("Cancel", W * 0.5f + 6f, 56f, () => Press(false));
            _two.gameObject.SetActive(false);
            Root.gameObject.SetActive(false);
            // The ghost HUD (corpse marker, ghost-world look, the per-frame refresh of this popup) lives on the layout's root object.
            UiGhost.Attach(this, ctx, surface);
        }

        private UiButton PopupButton(string label, float x, float y, System.Action click)
        {
            UiButton b = UiKit.Button(Root, label, x, y, ButtonW, ButtonH);
            b.Art = UiKit.Pic(b.transform, "Up", UiArt.Tex(UiArt.DialogButtonUp), 0f, 1f, 0f, 0.71875f, 0f, 0f, ButtonW, ButtonH);
            b.ArtUp = UiArt.Tex(UiArt.DialogButtonUp);
            b.Pushed = UiKit.Pic(b.transform, "Down", UiArt.Tex(UiArt.DialogButtonDown), 0f, 1f, 0f, 0.71875f, 0f, 0f, ButtonW, ButtonH);
            b.Pushed.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.DialogButtonHighlight), 0f, 1f, 0f, 0.71875f, 0f, 0f, ButtonW, ButtonH);
            b.Hilite.enabled = false;
            b.Label = UiKit.Text(b.transform, "Label", label, 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, ButtonW, ButtonH);
            b.LabelColor = UiArt.Gold;
            b.Click = click;
            return b;
        }

        /// <summary>
        /// For the layout (integrator request 2: <c>Death.SetOpen(UiDeath.WantsOpen(s))</c> instead of <c>Death.SetOpen(p.Dead)</c>): open while
        /// lying dead, or for a ghost when WoW would show a popup (Resurrect at the corpse, the spirit healer's offer not cancelled).
        /// Works with either reading of PlayerState.Dead for a ghost.
        /// </summary>
        public static bool WantsOpen(IWowSession s)
        {
            PlayerState p = s != null ? s.Player : null;
            if (p == null) return false;
            if (!p.NearSpiritHealer) { _healerDeclined = false; _healerAsked = false; }
            if (p.Ghost) return p.ResurrectRequestBy != null || p.CanReclaim || (p.NearSpiritHealer && !_healerDeclined);   // rescorpse: a pending resurrection shows anywhere
            return p.Dead;
        }

        /// <summary>
        /// A click (or VR trigger) on the already targeted unit (WowEntityViews.OnEntityClicked): a ghost next to the spirit healer who
        /// clicks him again gets his offer back after a Cancel, as a right-click on him does in WoW. NearSpiritHealer means "targeted and
        /// in reach" offline and "in reach" online (where the click also reaches the session's TrySpiritHealer); only living,
        /// non-hostile units count, so a click on a monster does not bring the offer back.
        /// </summary>
        public static void ReopenHealer(IWowSession s, EntityState e)
        {
            PlayerState p = s != null ? s.Player : null;
            if (p == null || e == null || !p.Ghost || !p.NearSpiritHealer || !_healerDeclined) return;
            if (e.Dead || e.Reaction == Reaction.Hostile || (e.Kind != EntityKind.Npc && e.Kind != EntityKind.Creature)) return;
            _healerDeclined = false;
            _healerAsked = false;
            WowLog.Info("UI: the spirit healer's offer shown again (" + (e.Name ?? "?") + " clicked).");
        }

        private static Mode Choose(PlayerState p)
        {
            if (p == null) return Mode.None;
            // rescorpse 2026-10-04: a resurrection by another player comes first ("RESURRECT" cancels = "DEATH", StaticPopup.lua:1211), lying dead or a ghost
            if (p.Dead && p.ResurrectRequestBy != null) return Mode.Request;
            if (!p.NearSpiritHealer) { _healerDeclined = false; _healerAsked = false; }
            if (p.Ghost)
            {
                // DEATH "cancels = RECOVER_CORPSE"; at the corpse the free resurrection comes first.
                if (p.CanReclaim) return Mode.Reclaim;
                if (p.NearSpiritHealer && !_healerDeclined) return _healerAsked ? Mode.HealerAgain : Mode.Healer;
                return Mode.None;
            }
            // Owner lootdeath (online): CanReclaim while lying dead = a pending SMSG_RESURRECT_REQUEST; "Accept" = ReclaimCorpse answers it.
            if (p.Dead) return p.CanReclaim ? Mode.Resurrect : Mode.Death;
            return Mode.None;
        }

        private void Press(bool first)
        {
            IWowSession s = Ctx.S;
            if (s == null) return;
            PlayerState p = s.Player;
            switch (_mode)
            {
                case Mode.Death:
                    if (first) { WowLog.Info("UI: Release Spirit pressed."); s.ReleaseSpirit(); }
                    else if (SelfRes(p)) { WowLog.Info("UI: " + p.SelfResName + " pressed."); s.SelfResurrect(); }   // iteration 11 (R4): DEATH OnCancel "clicked" -> UseSoulstone (StaticPopup.lua:1158-1162)
                    break;
                case Mode.Reclaim:
                    if (first) { WowLog.Info("UI: Resurrect (at the corpse) accepted."); s.ReclaimCorpse(); }
                    break;
                case Mode.Resurrect:
                    // StaticPopup "RESURRECT": Accept; there is no decline in the session contract, so the second button is the DEATH
                    // popup's Release Spirit that WoW shows beside it.
                    if (first) { WowLog.Info("UI: resurrection request accepted."); s.ReclaimCorpse(); }
                    else { WowLog.Info("UI: Release Spirit pressed (a resurrection request was pending)."); s.ReleaseSpirit(); }
                    break;
                case Mode.Request:
                    // rescorpse 2026-10-04: StaticPopup "RESURRECT" - button1 ACCEPT -> AcceptResurrect() (disabled until the corpse recovery
                    // delay is over, StartDelay = GetCorpseRecoveryDelay), button2 DECLINE -> DeclineResurrect() (StaticPopup.lua:1191-1216)
                    if (first)
                    {
                        if (p != null && p.ResurrectRequestDelay > 0f) break;
                        WowLog.Info("UI: the resurrection by " + (p != null ? p.ResurrectRequestBy : "?") + " accepted.");
                        s.ReclaimCorpse();
                    }
                    else
                    {
                        WowLog.Info("UI: the resurrection by " + (p != null ? p.ResurrectRequestBy : "?") + " declined.");
                        var online = s as SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession;
                        if (online != null) online.DeclineResurrect("Decline pressed");
                    }
                    break;
                case Mode.Healer:
                case Mode.HealerAgain:
                    if (!first) { _healerDeclined = true; _healerAsked = false; WowLog.Info("UI: the spirit healer's offer cancelled."); break; }
                    int sick = SicknessSeconds(p.Level);
                    bool again = _mode == Mode.Healer && (sick > 0 || p.Level > 10);   // XP_LOSS / _NO_DURABILITY / _NO_SICKNESS ask twice; _NO_SICKNESS_NO_DURABILITY does not
                    if (again) { _healerAsked = true; break; }
                    WowLog.Info("UI: the spirit healer's resurrection accepted (level " + p.Level + ", sickness " + sick + " s).");
                    _healerAsked = false;
                    s.AcceptSpiritHealer();
                    break;
            }
            _shownText = null;            // re-layout next frame
            _shownSecond = int.MinValue;
        }

        /// <summary>Player.cpp:4613-4634 (the client's GetResSicknessDuration shows the same): level 1-10 none, 11-19 one minute per level above 10, 20+ ten minutes.</summary>
        public static int SicknessSeconds(int level)
        {
            const int start = 11;   // worldserver.conf.dist Death.SicknessLevel = 11
            if (level < start) return 0;
            return level < start + 9 ? (level - start + 1) * 60 : 600;
        }

        /// <summary>StaticPopup_OnUpdate's timer text: under 60 s in seconds (SECONDS), else ceil(minutes) (MINUTES); "|4Second:Seconds;" picks by the number.</summary>
        public static string TimeText(string format, float seconds)
        {
            int t = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            int n = t < 60 ? t : Mathf.CeilToInt(t / 60f);
            string unit = t < 60 ? (n == 1 ? "Second" : "Seconds") : (n == 1 ? "Minute" : "Minutes");   // SECONDS / MINUTES (GlobalStrings.lua:6200 / 4952)
            return format.Replace("%d", n.ToString()).Replace("%s", unit);
        }

        private static string Minutes(int seconds)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds / 60f));
            return n + (n == 1 ? " Minute" : " Minutes");   // D_MINUTES "%d |4Minute:Minutes;" (GlobalStrings.lua:2102); the client's own formatting of GetResSicknessDuration is not known - ours
        }

        public override void Refresh(float now) { Tick(now); }

        /// <summary>Every frame while open (UiGhost calls it): which popup, its text and buttons; the release countdown.</summary>
        internal void Tick(float now)
        {
            if (!IsOpen) { _deathShownAt = -1f; _placedHeadKnown = false; _offViewSince = -1f; UiPopup.Reserve(this, 0f); return; }   // uicore H6
            IWowSession s = Ctx.S;
            PlayerState p = s != null ? s.Player : null;
            Mode want = Choose(p);
            // iteration 11 (R1): VR - the popup comes along when the player walked on or looked away (every frame, before the early returns)
            if (_surface != null && _surface.Shown && want != Mode.None) FollowVr(now); else { _placedHeadKnown = false; _offViewSince = -1f; }
            bool selfRes = want == Mode.Death && SelfRes(p);
            if (selfRes != _selfResShown) { _selfResShown = selfRes; _shownText = null; _shownSecond = int.MinValue; }   // R4: the button appears with the field
            if (want == Mode.Death && _deathShownAt < 0f) _deathShownAt = now;
            if (want != Mode.Death) _deathShownAt = -1f;
            string text;
            switch (want)
            {
                case Mode.Death:
                    {
                        float left = ReleaseSeconds - (now - _deathShownAt);
                        int second = Mathf.CeilToInt(left);
                        if (want == _mode && second == _shownSecond) return;
                        _shownSecond = second;
                        text = !p.ReleaseTimer ? "You have died. Release to the nearest graveyard?"   // iteration 11 (R5): no auto-release in instances (Player.cpp:4648; StaticPopup.lua:1138-1139)
                            : left > 0f ? TimeText("%d %s until release", left)            // DEATH_RELEASE_TIMER (GlobalStrings.lua:1938)
                            : "You have died. Release to the nearest graveyard?";            // DEATH_RELEASE_NOTIMER (GlobalStrings.lua:1936)
                        break;
                    }
                case Mode.Reclaim:
                    // RECOVER_CORPSE (GlobalStrings.lua:6034); a pending resurrection request (online) shows its own text
                    text = UiGhost.ResurrectRequestText ?? "Resurrect now?";
                    break;
                case Mode.Resurrect:
                    // RESURRECT_REQUEST / _NO_SICKNESS "%s wants to resurrect you" as the session's Info line said it (owner lootdeath)
                    text = UiGhost.ResurrectRequestText ?? "Resurrect now?";
                    break;
                case Mode.Request:
                    {
                        // RESURRECT_REQUEST_TIMER / _NO_SICKNESS_TIMER "%s wants to resurrect you and will be able to in %d %s[.  You will be
                        // afflicted with resurrection sickness.]" while the delay runs, then RESURRECT_REQUEST / _NO_SICKNESS (GlobalStrings.lua:6127-6130)
                        float d = p.ResurrectRequestDelay;
                        int second = d > 0f ? Mathf.CeilToInt(d) : 0;
                        if (want == _mode && second == _shownSecond && _shownText != null) return;
                        _shownSecond = second;
                        string who = p.ResurrectRequestBy ?? "Someone";
                        string sick = p.ResurrectRequestSickness ? ".  You will be afflicted with resurrection sickness." : "";
                        text = d > 0f ? TimeText(who + " wants to resurrect you and will be able to in %d %s", d) + sick
                                      : who + " wants to resurrect you" + sick;
                        break;
                    }
                case Mode.Healer:
                    text = HealerText(p.Level, false);
                    break;
                case Mode.HealerAgain:
                    text = HealerText(p.Level, true);
                    break;
                default:
                    text = null;
                    break;
            }
            if (want == _mode && text == _shownText) return;
            bool changed = want != _mode;
            // duelsnd: "RESURRECT" is a StaticPopup - its OnShow / OnHide play igMainMenuOpen / igMainMenuClose (StaticPopup.lua:3428 / :3445)
            if (changed && _mode == Mode.Request) UiSound.Added(UiPopup.CloseSound, "StaticPopup_OnHide, StaticPopup.lua:3445 (RESURRECT)");
            if (changed && want == Mode.Request) UiSound.Added(UiPopup.OpenSound, "StaticPopup_OnShow, StaticPopup.lua:3428 (RESURRECT)");
            _mode = want;
            _shownText = text;
            Layout(text);
            if (changed && want != Mode.None)
            {
                WowLog.Info("UI: death popup '" + (want == Mode.Death ? "DEATH" : want == Mode.Reclaim ? "RECOVER_CORPSE" : want == Mode.Resurrect ? "RESURRECT" : want == Mode.Request ? "RESURRECT (by " + (p.ResurrectRequestBy ?? "?") + ")" : want == Mode.Healer ? "XP_LOSS" : "XP_LOSS (again)") + "' shown.");
                PlaceVr();
            }
        }

        /// <summary>UIParent.lua:701-722 CONFIRM_XP_LOSS: the dialog by level and sickness; the texts of GlobalStrings.lua:1837-1843 ("%%" = "%").</summary>
        private static string HealerText(int level, bool again)
        {
            int sick = SicknessSeconds(level);
            string d = Minutes(sick);
            if (sick > 0 && level <= 10)
                return again ? "Remember, if you find your corpse there is no penalty.  Are you sure you want to incur " + d + " of Resurrection Sickness?"                // CONFIRM_XP_LOSS_AGAIN_NO_DURABILITY
                             : "If you find your corpse, you can resurrect for no penalty.  Otherwise you will be afflicted by " + d + " of Resurrection Sickness.";     // CONFIRM_XP_LOSS_NO_DURABILITY
            if (sick > 0)
                return again ? "Remember, if you find your corpse there is no penalty.  Are you sure you want to take up to 25% durability damage (equipped and inventory) and incur " + d + " of Resurrection Sickness?"   // CONFIRM_XP_LOSS_AGAIN
                             : "If you find your corpse, you can resurrect for no penalty.  If I resurrect you all of your items will take up to 25% durability damage (equipped and inventory) and you will be afflicted by " + d + " of Resurrection Sickness.";   // CONFIRM_XP_LOSS
            if (level > 10)
                return again ? "Remember, if you find your corpse there is no penalty.  Are you sure you want to have all your items take up to 25% durability damage?"   // CONFIRM_XP_LOSS_AGAIN_NO_SICKNESS
                             : "If you find your corpse, you can resurrect for no penalty.  If I resurrect you all of your items will take up to 25% durability damage (equipped and inventory).";   // CONFIRM_XP_LOSS_NO_SICKNESS
            return "You can find your corpse and resurrect at that location. Players that are level 10 or below can resurrect here with no penalty.";   // CONFIRM_XP_LOSS_NO_SICKNESS_NO_DURABILITY
        }

        private void Layout(string text)
        {
            bool on = text != null;
            _back.Root.gameObject.SetActive(on);
            _text.enabled = on;
            bool two = _mode == Mode.Healer || _mode == Mode.HealerAgain || _mode == Mode.Resurrect || _mode == Mode.Request || (_mode == Mode.Death && SelfRes(Ctx.P));   // iteration 11 (R4)
            _one.gameObject.SetActive(on);
            _two.gameObject.SetActive(on && two);
            if (!on) { UiPopup.Reserve(this, 0f); return; }                      // uicore H6: the desktop popup stack moves below us
            _text.fontSize = text.Length > 60 ? 12f : 13f;
            _text.text = text;
            float tw = W - 40f;
            float th = Mathf.Ceil(_text.GetPreferredValues(text, tw, 0f).y) + 2f;
            UiKit.Size(_text.rectTransform, tw, th);
            float h = 16f + th + 12f + ButtonH + 16f;
            H = Mathf.Max(100f, h);
            UiKit.Size(Root, W, H);
            _back.Place(0f, 0f, W, H);
            float by = H - 16f - ButtonH;
            _one.SetLabel(_mode == Mode.Death ? "Release Spirit" : "Accept");     // DEATH_RELEASE (GlobalStrings.lua:1935) / ACCEPT (:11)
            _one.Interactable = !(_mode == Mode.Request && Ctx.P != null && Ctx.P.ResurrectRequestDelay > 0f);   // rescorpse: StartDelay greys ACCEPT (StaticPopup.lua:3346-3355)
            _two.SetLabel(_mode == Mode.Request ? "Decline" : _mode == Mode.Resurrect ? "Release Spirit" : _mode == Mode.Death ? (Ctx.P != null && Ctx.P.SelfResName != null ? Ctx.P.SelfResName : "Reincarnate") : "Cancel");   // DEATH_RELEASE (GlobalStrings.lua:1935) / DEATH button2 = HasSoulstone()'s text, ours the spell name, USE_SOULSTONE (:7926) / CANCEL (:1338)
            UiKit.Move(_one.Rt, two ? W * 0.5f - 6f - ButtonW : (W - ButtonW) * 0.5f, by);
            UiKit.Move(_two.Rt, W * 0.5f + 6f, by);
            if (_surface != null && _surface.Root != null && Mathf.Abs(_surface.Root.sizeDelta.y - H) > 0.5f)
            {
                // VR: the popup canvas is pointed at inside its own rect (UiSurface.RayHit), so it grows with the text.
                _surface.Root.sizeDelta = new Vector2(W, H);
                _surface.Height = H;
            }
            UiPopup.Reserve(this, _surface == null ? H : 0f);                    // uicore H6: on the desktop the popup stack starts below the death popup
        }

        /// <summary>VR: a new popup while the popup canvas stays shown comes in front of the eyes again (as UiVrLayout does when it opens).</summary>
        private void PlaceVr()
        {
            if (_surface == null || !_surface.Shown) return;
            Camera head = WowPlayer.Head();
            if (head == null) return;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _surface.Root.SetPositionAndRotation(head.transform.position + fwd * 1.1f - Vector3.up * 0.1f, Quaternion.LookRotation(fwd, Vector3.up));
            _placedHead = head.transform.position;                              // iteration 11 (R1)
            _placedHeadKnown = true;
        }

        private static bool SelfRes(PlayerState p) { return p != null && p.SelfResSpellId != 0; }   // iteration 11 (R4)

        /// <summary>Iteration 11 (R1): VR - the popup comes back in front when the player walked on (1.3 m) or looked away (50 degrees for 0.8 s);
        /// the corpse popup opens 39 yd from the body while the ghost runs towards it.</summary>
        private void FollowVr(float now)
        {
            Camera cam = WowPlayer.Head();
            if (cam == null) return;
            Transform h = cam.transform;
            if (!_placedHeadKnown) { _placedHead = h.position; _placedHeadKnown = true; }
            Vector2 walked = new Vector2(h.position.x - _placedHead.x, h.position.z - _placedHead.z);
            Vector3 to = _surface.Root.position - h.position, fwd = h.forward;
            float angle = Vector3.Angle(new Vector3(fwd.x, 0f, fwd.z), new Vector3(to.x, 0f, to.z));
            if (angle > FollowAngle) { if (_offViewSince < 0f) _offViewSince = now; } else _offViewSince = -1f;
            if (walked.magnitude > FollowWalkMetres || (_offViewSince >= 0f && now - _offViewSince > FollowAngleSeconds)) { PlaceVr(); _offViewSince = -1f; }
        }
    }
}
