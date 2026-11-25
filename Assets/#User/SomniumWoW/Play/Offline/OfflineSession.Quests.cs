using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        /// <summary>WoW's quest log holds 25 quests.</summary>
        public const int MaxQuestLog = 25;

        private float _exploreTimer;

        // ------------------------------------------------------------------ IWowSession: NPCs, quests

        public void Interact(ulong guid)
        {
            var c = CreatureOf(guid);
            if (c == null) return;
            if (_player.Dead) { EmitError("You are dead"); return; }
            if (_player.OnTaxi) { EmitError("You are in flight"); return; }
            float d = Dist(_me.Pos, c.Pos);
            if (c.Dead)
            {
                if (!c.State.Lootable) { EmitError("There is nothing to loot"); return; }
                if (d > InteractRange + c.CombatReach) { EmitError("You are too far away"); return; }
                OpenLoot(c);
                return;
            }
            if (!c.Tpl.IsNpc && c.State.Reaction != Reaction.Friendly) { StartAttack(guid); return; }
            if (d > InteractRange + c.CombatReach) { EmitError("You are too far away"); return; }
            SetTarget(guid);
            if (IsFlightMaster(c)) { OpenTaxi(c); return; }   // iteration 4: discovers the node, opens the flight map (CurrentTaxi)
            // Talking to an NPC counts for "speak to" objectives before the dialog opens.
            OnTalkCredit(c.Tpl.Entry);
            OpenDialog(c);
            TrOfflineAfterDialog(c);                                                // iteration 11: trainer F1 (an own-class trainer without quests opens its window)
        }

        public void CloseDialog()
        {
            _dialog = null;
            _dialogNpc = null;
        }

        private void OpenDialog(OfflineCreature npc)
        {
            var d = new NpcDialog();
            d.NpcGuid = npc.Guid;
            d.NpcName = npc.State.Name;
            d.Greeting = string.IsNullOrEmpty(npc.Tpl.Greeting) ? "Greetings, " + _player.Name + "." : npc.Tpl.Greeting.Replace("$N", _player.Name).Replace("$C", _player.Class.ToString());
            // Quests this NPC takes back first (complete, then in progress), then the ones it offers.
            foreach (var q in _zone.Quests)
            {
                if (q == null || q.EnderEntry != npc.Tpl.Entry) continue;
                var rec = _rec.FindQuest(q.Id);
                if (rec != null && rec.State == QuestState.Complete) d.Quests.Add(BuildQuestView(q));
            }
            foreach (var q in _zone.Quests)
            {
                if (q == null || q.EnderEntry != npc.Tpl.Entry) continue;
                var rec = _rec.FindQuest(q.Id);
                if (rec != null && rec.State == QuestState.Active) d.Quests.Add(BuildQuestView(q));
            }
            foreach (var q in _zone.Quests)
            {
                if (q == null || q.GiverEntry != npc.Tpl.Entry) continue;
                if (QuestStateOf(q) == QuestState.Available) d.Quests.Add(BuildQuestView(q));
            }
            _dialog = d;
            _dialogNpc = npc;
            if (!string.IsNullOrEmpty(npc.Tpl.Greeting)) Emit(GameEventType.Say, npc.Guid, PlayerGuid, 0, 0, false, d.Greeting);
        }

        private void RefreshDialog()
        {
            if (_dialogNpc == null) return;
            var npc = _dialogNpc;
            OpenDialog(npc);
        }

        public void AcceptQuest(int questId)
        {
            QuestDef q;
            if (!_quests.TryGetValue(questId, out q)) { EmitError("Unknown quest"); return; }
            if (QuestStateOf(q) != QuestState.Available) { EmitError(_rec.FindQuest(questId) != null ? "You are already on that quest" : "You can't take that quest yet"); return; }
            if (_rec.Quests.Count >= MaxQuestLog) { EmitError("Your quest log is full"); return; }
            var rec = new QuestRecord { Id = q.Id, State = QuestState.Active };
            _rec.Quests.Add(rec);
            for (int i = 0; i < q.Objectives.Count && i < 4; i++)
            {
                var o = q.Objectives[i];
                if (o.Kind == ObjectiveKind.Collect) rec.Counts[i] = Math.Min(o.Count, _rec.CountInBags(o.TargetEntry));
            }
            EmitQuest(GameEventType.QuestAccepted, q.Id, "Quest accepted: " + q.Title);
            WowLog.Info("OfflineSession: quest " + q.Id + " '" + q.Title + "' accepted");
            CheckQuestComplete(q, rec);
            QuestChanged();
        }

        public void CompleteQuest(int questId, int rewardChoiceIndex)
        {
            QuestDef q;
            if (!_quests.TryGetValue(questId, out q)) { EmitError("Unknown quest"); return; }
            var rec = _rec.FindQuest(questId);
            if (rec == null || rec.State != QuestState.Complete) { EmitError("That quest is not complete yet"); return; }
            if (_dialogNpc != null && _dialogNpc.Tpl.Entry != q.EnderEntry) { EmitError("Turn that quest in to someone else"); return; }
            // Room in the bags for the rewards?
            int need = q.RewardItems.Count + (q.RewardChoiceItems.Count > 0 ? 1 : 0);
            int freeAfter = CharacterRecord.BagSlots - _rec.Bags.Count;
            foreach (var o in q.Objectives) if (o.Kind == ObjectiveKind.Collect && _rec.CountInBags(o.TargetEntry) <= o.Count) freeAfter++;
            if (need > freeAfter) { EmitError("Inventory is full"); return; }
            if (q.RewardChoiceItems.Count > 0 && (rewardChoiceIndex < 0 || rewardChoiceIndex >= q.RewardChoiceItems.Count)) rewardChoiceIndex = 0;

            // Take the collected quest items.
            foreach (var o in q.Objectives) if (o.Kind == ObjectiveKind.Collect) _rec.RemoveFromBags(o.TargetEntry, o.Count);
            _rec.Quests.Remove(rec);
            if (!_rec.Rewarded.Contains(q.Id)) _rec.Rewarded.Add(q.Id);
            EmitQuest(GameEventType.QuestRewarded, q.Id, q.Title + " completed.");
            WowLog.Info("OfflineSession: quest " + q.Id + " '" + q.Title + "' rewarded (level " + _player.Level + ")");

            if (q.RewardMoney > 0) { _player.Money += q.RewardMoney; Emit(GameEventType.MoneyReceived, 0, PlayerGuid, q.RewardMoney, 0, false, MoneyText(q.RewardMoney)); }
            foreach (int item in q.RewardItems) GiveItem(item, 1);
            if (q.RewardChoiceItems.Count > 0) GiveItem(q.RewardChoiceItems[rewardChoiceIndex], 1);
            int xp = WowFormulas.QuestXp(q.RewardXp, q.Level, _player.Level);
            if (xp > 0) GiveXp(xp, 0, q.Id);
            QuestChanged();
            RefreshBagsAndEquipment();
        }

        public void AbandonQuest(int questId)
        {
            var rec = _rec.FindQuest(questId);
            if (rec == null) return;
            _rec.Quests.Remove(rec);
            QuestDef q;
            if (_quests.TryGetValue(questId, out q))
            {
                foreach (var o in q.Objectives) if (o.Kind == ObjectiveKind.Collect) { var d = FindItemDef(o.TargetEntry); if (d != null && d.QuestId == questId) _rec.RemoveFromBags(o.TargetEntry, 999); }
                EmitQuest(GameEventType.QuestAbandoned, questId, "Quest abandoned: " + q.Title);
            }
            QuestChanged();
            RefreshBagsAndEquipment();
        }

        // ------------------------------------------------------------------ quest logic

        private QuestState QuestStateOf(QuestDef q)
        {
            if (_rec.Rewarded.Contains(q.Id)) return QuestState.Rewarded;
            var rec = _rec.FindQuest(q.Id);
            if (rec != null) return rec.State;
            if (q.ClassOnly != WowClass.None && q.ClassOnly != _rec.Class) return QuestState.Unavailable;
            if (_player.Level < q.MinLevel) return QuestState.Unavailable;
            if (q.PrevQuestId != 0 && !_rec.Rewarded.Contains(q.PrevQuestId)) return QuestState.Unavailable;
            return QuestState.Available;
        }

        private void OnKillCredit(int entry)
        {
            foreach (var rec in _rec.Quests)
            {
                if (rec.State != QuestState.Active) continue;
                QuestDef q;
                if (!_quests.TryGetValue(rec.Id, out q)) continue;
                for (int i = 0; i < q.Objectives.Count && i < 4; i++)
                {
                    var o = q.Objectives[i];
                    if (o.Kind != ObjectiveKind.Kill || o.TargetEntry != entry || rec.Counts[i] >= o.Count) continue;
                    rec.Counts[i]++;
                    EmitQuest(GameEventType.QuestProgress, q.Id, ObjectiveLine(o, rec.Counts[i]));
                    CheckQuestComplete(q, rec);
                    _questDirty = true;
                }
            }
        }

        private void OnTalkCredit(int npcEntry)
        {
            foreach (var rec in _rec.Quests)
            {
                if (rec.State != QuestState.Active) continue;
                QuestDef q;
                if (!_quests.TryGetValue(rec.Id, out q)) continue;
                for (int i = 0; i < q.Objectives.Count && i < 4; i++)
                {
                    var o = q.Objectives[i];
                    if (o.Kind != ObjectiveKind.TalkTo || o.TargetEntry != npcEntry || rec.Counts[i] >= o.Count) continue;
                    rec.Counts[i] = o.Count;
                    EmitQuest(GameEventType.QuestProgress, q.Id, ObjectiveLine(o, rec.Counts[i]));
                    CheckQuestComplete(q, rec);
                    _questDirty = true;
                }
            }
        }

        /// <summary>Collect objectives follow the bag contents.</summary>
        private void OnItemsChanged(int entry)
        {
            foreach (var rec in _rec.Quests)
            {
                QuestDef q;
                if (!_quests.TryGetValue(rec.Id, out q)) continue;
                for (int i = 0; i < q.Objectives.Count && i < 4; i++)
                {
                    var o = q.Objectives[i];
                    if (o.Kind != ObjectiveKind.Collect || o.TargetEntry != entry) continue;
                    int have = Math.Min(o.Count, _rec.CountInBags(entry));
                    if (have == rec.Counts[i]) continue;
                    bool up = have > rec.Counts[i];
                    rec.Counts[i] = have;
                    if (up) EmitQuest(GameEventType.QuestProgress, q.Id, ObjectiveLine(o, have));
                    CheckQuestComplete(q, rec);
                    _questDirty = true;
                }
            }
        }

        private void StepQuestsExplore(float dt)
        {
            _exploreTimer -= dt;
            if (_exploreTimer > 0f) return;
            _exploreTimer = 0.5f;
            if (_player.Dead) return;
            foreach (var rec in _rec.Quests)
            {
                if (rec.State != QuestState.Active) continue;
                QuestDef q;
                if (!_quests.TryGetValue(rec.Id, out q)) continue;
                for (int i = 0; i < q.Objectives.Count && i < 4; i++)
                {
                    var o = q.Objectives[i];
                    if (o.Kind != ObjectiveKind.Explore || rec.Counts[i] >= o.Count) continue;
                    if (Dist2D(_me.Pos, o.ExplorePos) > (o.ExploreRadius > 0 ? o.ExploreRadius : 10f)) continue;
                    rec.Counts[i] = o.Count;
                    EmitQuest(GameEventType.QuestProgress, q.Id, (string.IsNullOrEmpty(o.Text) ? "Explored" : o.Text) + " (Complete)");
                    CheckQuestComplete(q, rec);
                    _questDirty = true;
                }
            }
            if (_questDirty) QuestChanged();
        }

        private void CheckQuestComplete(QuestDef q, QuestRecord rec)
        {
            bool done = true;
            for (int i = 0; i < q.Objectives.Count && i < 4; i++) if (rec.Counts[i] < q.Objectives[i].Count) { done = false; break; }
            if (done && rec.State == QuestState.Active)
            {
                rec.State = QuestState.Complete;
                EmitQuest(GameEventType.QuestCompleted, q.Id, q.Title + " (Complete)");
            }
            else if (!done && rec.State == QuestState.Complete) rec.State = QuestState.Active;
            _questDirty = true;
        }

        /// <summary>True while an active quest still needs this item (quest drops only drop then).</summary>
        private bool QuestNeedsItem(int questId, int itemEntry)
        {
            var rec = _rec.FindQuest(questId);
            if (rec == null || rec.State == QuestState.Rewarded) return false;
            QuestDef q;
            if (!_quests.TryGetValue(questId, out q)) return false;
            for (int i = 0; i < q.Objectives.Count && i < 4; i++)
            {
                var o = q.Objectives[i];
                if (o.Kind == ObjectiveKind.Collect && o.TargetEntry == itemEntry && _rec.CountInBags(itemEntry) < o.Count) return true;
            }
            return false;
        }

        private static string ObjectiveLine(QuestObjectiveDef o, int have)
        {
            return (string.IsNullOrEmpty(o.Text) ? "Objective" : o.Text) + ": " + Math.Min(have, o.Count) + "/" + o.Count;
        }

        // ------------------------------------------------------------------ views

        private bool _questDirty = true;

        private void QuestChanged()
        {
            _questDirty = false;
            RefreshQuestLog();
            RefreshMarkers();
            if (_dialog != null) RefreshDialog();
        }

        private void RefreshQuestLog()
        {
            _player.QuestLog.Clear();
            foreach (var rec in _rec.Quests)
            {
                QuestDef q;
                if (_quests.TryGetValue(rec.Id, out q)) _player.QuestLog.Add(BuildQuestView(q));
            }
        }

        private QuestView BuildQuestView(QuestDef q)
        {
            var v = new QuestView();
            v.Id = q.Id;
            v.Title = q.Title ?? "";
            v.Level = q.Level;
            v.ZoneName = _zone != null ? _zone.Name : null;                      // 13.7: the quest log's header
            v.State = QuestStateOf(q);
            v.Description = Personal(q.Description);
            v.ObjectivesText = Personal(q.ObjectivesText);
            v.ProgressText = Personal(q.ProgressText);
            v.CompletionText = Personal(q.CompletionText);
            var rec = _rec.FindQuest(q.Id);
            for (int i = 0; i < q.Objectives.Count && i < 4; i++)
            {
                var o = q.Objectives[i];
                v.Objectives.Add(new QuestObjectiveView { Text = o.Text ?? "", Have = rec != null ? Math.Min(rec.Counts[i], o.Count) : 0, Need = o.Count });
            }
            v.RewardXp = WowFormulas.QuestXp(q.RewardXp, q.Level, _player.Level);
            v.RewardMoney = q.RewardMoney;
            foreach (int e in q.RewardItems) v.RewardItems.Add(ItemViewOf(e, 1));
            foreach (int e in q.RewardChoiceItems) v.RewardChoices.Add(ItemViewOf(e, 1));
            v.GiverGuid = FirstGuidOf(q.GiverEntry);
            v.EnderGuid = FirstGuidOf(q.EnderEntry);
            CreatureTemplate et;                                                   // iteration 11: the tracker's line of a complete quest - ours, our own writing
            v.LogCompletionText = _templates.TryGetValue(q.EnderEntry, out et) && !string.IsNullOrEmpty(et.Name) ? "Return to " + et.Name + "." : null;
            return v;
        }

        private string Personal(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("$N", _player.Name).Replace("$n", _player.Name).Replace("$C", _player.Class.ToString()).Replace("$c", _player.Class.ToString().ToLowerInvariant());
        }

        private ulong FirstGuidOf(int entry)
        {
            for (int i = 0; i < _creatures.Count; i++) if (_creatures[i].Tpl.Entry == entry) return _creatures[i].Guid;
            return 0;
        }

        /// <summary>"!" yellow when an NPC offers a quest, "?" yellow when one can be handed in, "?" grey while in progress.</summary>
        private void RefreshMarkers()
        {
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!c.Tpl.IsNpc) { c.State.Marker = QuestMarker.None; continue; }
                var m = QuestMarker.None;
                foreach (var q in _zone.Quests)
                {
                    if (q == null) continue;
                    var st = QuestStateOf(q);
                    if (q.EnderEntry == c.Tpl.Entry && st == QuestState.Complete) { m = QuestMarker.Complete; break; }
                    if (q.GiverEntry == c.Tpl.Entry && st == QuestState.Available) m = QuestMarker.Available;
                    else if (q.EnderEntry == c.Tpl.Entry && st == QuestState.Active && m == QuestMarker.None) m = QuestMarker.Incomplete;
                }
                c.State.Marker = m;
            }
        }

        private static string MoneyText(int copper)
        {
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var parts = new List<string>();
            if (g > 0) parts.Add(g + " Gold");
            if (s > 0) parts.Add(s + " Silver");
            if (c > 0 || parts.Count == 0) parts.Add(c + " Copper");
            return string.Join(", ", parts.ToArray());
        }
    }
}

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    // ---- iteration 11 - owner quests: the 4 new quest members of IWowSession offline (proposal-quests.md 7.6).
    public sealed partial class OfflineSession
    {
        public void ShareQuest(int questId) { Emit(GameEventType.Info, PlayerGuid, PlayerGuid, 0, 0, false, "Sharing quests needs a party on your realm (online)."); }   // ours
        public void AnswerQuestConfirm(bool accept) { _player.PendingQuestConfirm = null; }
        public void UseQuestItem(int questId)
        {
            foreach (var q in _player.QuestLog) if (q.Id == questId && q.UseItemBagIndex >= 0) { UseItem(q.UseItemBagIndex); return; }
        }
        public void SelfResurrect() { }                                            // no soulstones offline (README gap)
    }
}
