// iteration 11 - owner quests (built from proposal-quests.md 7.3; SelfResurrect lives in OnlineSession.Death.cs, MoveSyncNow in motion's Movement.cs)
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 11 (designer quests): sharing, party accept, the server clock, timed / daily quests, quest items on targets, reward spells.
    public sealed partial class OnlineSession
    {
        const uint QFlagSharable = 0x8, QFlagDaily = 0x1000;            // QuestDef.h:135 / :144
        const uint QStateFailBit = 0x2;                                  // QUEST_STATE_FAIL (Player.h:621)
        const int QDailySlots = 25;                                      // PLAYER_MAX_DAILY_QUESTS (Player.h:72); PLAYER_FIELD_DAILY_QUESTS_1 x 25 (UpdateFields.cs:359)
        const int QEffectLearnSpell = 36;                                // SPELL_EFFECT_LEARN_SPELL (SharedDefines.h)
        const uint QTargetUnit = 0x2, QTargetGo = 0x800, QTargetGoItem = 0x4000, QTargetDest = 0x40;   // SpellCastTargetFlags (SpellInfo.h)
        const double QClockRefreshSeconds = 600.0;                       // ours: CMSG_QUERY_TIME again every 10 minutes
        const float QItemDefaultRange = 5f;                              // ours: a gameobject item spell without a range row

        double _qServerTime = -1, _qServerTimeAt;                       // SMSG_QUERY_TIME_RESPONSE ServerTime and our _now when it came
        double _qClockAskAt = 0;
        int _qConfirmSerial;
        DbcTable _qSpellDbc; bool _qSpellDbcTried;

        void InitQuestExtras()
        {
            QOn(Op.MSG_QUEST_PUSH_RESULT, OnQPushResult);
            QOn(Op.SMSG_QUEST_CONFIRM_ACCEPT, OnQConfirmAccept);
            QOn(Op.SMSG_QUERY_TIME_RESPONSE, OnQQueryTime);
            WowLog.Info("Online quests: ours - the server clock is asked every " + QClockRefreshSeconds + " s, timed quests refresh every " + QTimerRefreshSeconds +
                        " s, a gameobject quest item without a range uses " + QItemDefaultRange + " yd");
        }

        void TickQuestExtras(float dt)
        {
            if (_now >= _qClockAskAt) { _qClockAskAt = _now + QClockRefreshSeconds; _world.Send(Op.CMSG_QUERY_TIME); }   // empty; QueryHandler.cpp:72-85
            if (_player.DailyResetSeconds > 0f) _player.DailyResetSeconds = Math.Max(0f, _player.DailyResetSeconds - dt);
            if (_self != null)
            {
                int n = 0;
                for (int i = 0; i < QDailySlots; i++) if (_self.U32(UF.PLAYER_FIELD_DAILY_QUESTS_1 + i) != 0) n++;
                _player.DailyQuestsDone = n;
            }
            var c = _player.PendingQuestConfirm;                              // a party member's popup goes when we got that quest another way
            if (c != null && QLogSlotOf(c.QuestId) >= 0) _player.PendingQuestConfirm = null;
        }

        /// <summary>The server's unix time now (SMSG_QUERY_TIME_RESPONSE + elapsed), -1 = not known yet.</summary>
        double QServerNow() { return _qServerTime < 0 ? -1 : _qServerTime + (_now - _qServerTimeAt); }

        /// <summary>SMSG_QUERY_TIME_RESPONSE (QueryPackets.h:64-73): uint32 ServerTime, uint32 TimeResponse (seconds to the daily reset).</summary>
        void OnQQueryTime(WowPacket p)
        {
            uint t = p.U32();
            uint reset = p.U32();
            bool first = _qServerTime < 0;
            _qServerTime = t; _qServerTimeAt = _now;
            _player.DailyResetSeconds = reset;
            _qLogDirty = true;
            if (first) WowLog.Info("Online quests: server clock " + t + " (unix), daily reset in " + reset + " s (SMSG_QUERY_TIME_RESPONSE)");
        }

        public void ShareQuest(int questId)
        {
            if (_world == null) return;
            if (QLogSlotOf(questId) < 0) return;
            if (_player.Group.Count == 0) { Error("You are not in a party"); return; }                          // ERR_QUEST_PUSH_NOT_IN_PARTY_S (GS:3438)
            _world.Send(Op.CMSG_PUSHQUESTTOPARTY, new PacketWriter().U32((uint)questId));                    // QuestPackets.cpp:123-126
            WowLog.Info("Online quests: share quest " + questId + " '" + QTitle(questId) + "' (CMSG_PUSHQUESTTOPARTY)");
        }

        /// <summary>MSG_QUEST_PUSH_RESULT to the sharer (QuestPackets.cpp:70-76): uint64 member, uint8 QuestShareMessages (QuestDef.h:64-77).</summary>
        void OnQPushResult(WowPacket p)
        {
            ulong who = p.U64();
            int msg = p.U8();
            string n = ChatSender(who) ?? "Someone";                                                       // "Someone": ours
            string text;
            switch (msg)                                                                                     // by meaning (section 5; GS:3430-3441)
            {
                case 0: text = "Sharing quest with " + n + "..."; break;
                case 1: text = n + " is not eligible for that quest"; break;
                case 2: text = n + " has accepted your quest"; break;
                case 3: text = n + " has declined your quest"; break;
                case 4: text = n + " is busy"; break;
                case 5: text = n + "'s quest log is full"; break;
                case 6: text = n + " is already on that quest"; break;
                case 7: text = n + " has completed that quest"; break;
                case 8: text = "That quest cannot be shared today"; break;
                case 9: text = "Quest sharing timer has expired"; break;
                case 10: text = "You are not in a party"; break;
                default: text = null; break;
            }
            if (text != null) _events.Enqueue(new GameEvent { Type = GameEventType.Info, SourceGuid = who, Text = text });
            WowLog.Info("Online quests: MSG_QUEST_PUSH_RESULT " + msg + " from " + who.ToString("X16") + " - " + (text ?? "unknown value"));
        }

        /// <summary>SMSG_QUEST_CONFIRM_ACCEPT (QuestPackets.cpp:61-68): uint32 quest, string title, uint64 the member who accepted it.</summary>
        void OnQConfirmAccept(WowPacket p)
        {
            int id = (int)p.U32();
            string title = p.CString();
            ulong from = p.U64();
            int used = 0;
            for (int s = 0; s < QLogSlots; s++) if (_qSlots[s * QSlotFields] != 0) used++;
            _player.PendingQuestConfirm = new QuestConfirmView { QuestId = id, Title = title, FromGuid = from, FromName = ChatSender(from) ?? "Someone",
                                                                 LogFull = used >= QLogSlots, Serial = ++_qConfirmSerial };
            WowLog.Info("Online quests: " + (ChatSender(from) ?? from.ToString("X16")) + " is starting quest " + id + " '" + title + "' - QUEST_ACCEPT asked");
        }

        public void AnswerQuestConfirm(bool accept)
        {
            var c = _player.PendingQuestConfirm;
            _player.PendingQuestConfirm = null;
            if (c == null || _world == null || !accept) return;                                          // WoW's QUEST_ACCEPT has no OnCancel (StaticPopup.lua:1647-1657)
            _world.Send(Op.CMSG_QUEST_CONFIRM_ACCEPT, new PacketWriter().U32((uint)c.QuestId));             // QuestPackets.cpp:118-121; QuestHandler.cpp:446-476
            WowLog.Info("Online quests: joined quest " + c.QuestId + " of " + c.FromName + " (CMSG_QUEST_CONFIRM_ACCEPT)");
        }

        /// <summary>QuestView extras (contract 6.1) for a log entry (slot >= 0) or a dialog quest.</summary>
        void QFillExtras(QuestView v, QTemplate t, QTalk k, int slot, QMenuQuest mq)
        {
            uint flags = k != null && k.FullFlags != 0 ? k.FullFlags : t != null ? t.Flags : 0u;
            v.Sharable = (flags & QFlagSharable) != 0;
            v.Daily = (flags & QFlagDaily) != 0;
            v.Repeatable = mq != null && mq.Repeatable;
            v.SuggestedPlayers = k != null ? k.DetailsSuggested : 0;
            v.LogCompletionText = t != null ? QPersonal(t.CompletedText) : null;
            if (slot >= 0)
            {
                uint state = _qSlots[slot * QSlotFields + 1], expiry = _qSlots[slot * QSlotFields + 4];   // QUEST_STATE_OFFSET 1, QUEST_TIME_OFFSET 4 (Player.h:609-612)
                v.Failed = (state & QStateFailBit) != 0;
                if (expiry != 0)
                {
                    _qAnyTimed = true;
                    double now = QServerNow();
                    if (v.Failed || expiry == 1) v.TimeLeft = 0f;                  // FailQuest sets the timer to 1 (PlayerQuest.cpp:927)
                    else if (now >= 0) v.TimeLeft = (float)Math.Max(0.0, expiry - now);
                    float total;
                    if (v.TimeLeft > 0f && !_qTimerTotal.TryGetValue(v.Id, out total)) _qTimerTotal[v.Id] = v.TimeLeft;
                    if (_qTimerTotal.TryGetValue(v.Id, out total)) v.TimeTotal = total;
                }
                QFindUseItem(v, t);
            }
            int rs = 0, rsc = 0;
            if (k != null) { if (k.RewardAt >= 0 && (k.RewSpell != 0 || k.RewSpellCast != 0)) { rs = k.RewSpell; rsc = k.RewSpellCast; } else { rs = k.DetailsRewSpell; rsc = k.DetailsRewSpellCast; } }
            int sp = rs != 0 ? rs : Math.Max(0, rsc);
            if (sp > 0) QFillRewardSpell(v, sp);
        }

        /// <summary>The tracker's item: the quest's source item, else a required item, that is in the bags and has a "Use:" spell (ours - the
        /// client's GetQuestLogSpecialItemInfo rule is not readable data; QUEST_FLAGS_DISPLAY_ITEM_IN_TRACKER 0x20000 marks 1 quest only).</summary>
        void QFindUseItem(QuestView v, QTemplate t)
        {
            if (t == null) return;
            for (int c = -1; c < 6; c++)
            {
                int entry = c < 0 ? t.SrcItem : t.ReqItem[c];
                if (entry <= 0) continue;
                ItemProto d;
                if (!_itemProtos.TryGetValue(entry, out d) || d.Missing || d.UseSpell <= 0) continue;
                var bags = _player.Bags;
                for (int i = 0; i < bags.Count; i++)
                    if (bags[i] != null && bags[i].Entry == entry) { v.UseItemEntry = entry; v.UseItemBagIndex = i; return; }
            }
        }

        void QFillRewardSpell(QuestView v, int spellId)
        {
            var db = SpellDb.Get(_files);
            var si = db != null ? db.Find(spellId) : null;
            if (si == null) return;
            v.RewardSpellId = spellId;
            v.RewardSpellName = si.Name;
            v.RewardSpellIcon = si.IconPath;
            for (int k = 0; k < si.Effects.Length; k++) if (si.Effects[k] != null && si.Effects[k].Effect == QEffectLearnSpell) v.RewardSpellLearned = true;
        }

        /// <summary>What a quest item's spell wants (the census of section 4): 1 unit, 2 gameobject, 3 a place, 0 nothing explicit (self / area / spell focus).</summary>
        int QItemTargetKind(int spellId)
        {
            var db = SpellDb.Get(_files);
            var si = db != null ? db.Find(spellId) : null;
            if (si == null) return 0;
            if (!_qSpellDbcTried) { _qSpellDbcTried = true; _qSpellDbc = Dbc.Load(_files, "Spell"); if (_qSpellDbc != null && _qSpellDbc.FieldCount != SpellFields.Spell.FieldCount) _qSpellDbc = null; }
            uint mask = 0;
            if (_qSpellDbc != null) { int r = _qSpellDbc.FindRow(spellId); if (r >= 0) mask = _qSpellDbc.GetUInt(r, SpellFields.Spell.Targets); }
            bool go = (mask & (QTargetGo | QTargetGoItem)) != 0, unit = (mask & QTargetUnit) != 0, dest = (mask & QTargetDest) != 0;
            for (int k = 0; k < si.Effects.Length; k++)
            {
                var e = si.Effects[k];
                if (e == null || e.Effect == 0) continue;
                foreach (int tg in new[] { e.TargetA, e.TargetB })
                {
                    if (tg == 23 || tg == 26) go = true;                          // TARGET_GAMEOBJECT_TARGET, TARGET_GAMEOBJECT_ITEM_TARGET (SharedDefines.h)
                    else if (tg == 6 || tg == 21 || tg == 25 || tg == 35 || tg == 53 || tg == 57) unit = true;   // TARGET_UNIT_TARGET_* / TARGET_DEST_TARGET_ENEMY
                    else if (tg == 87 || tg == 89) dest = true;                   // TARGET_DEST_DEST, TARGET_DEST_TRAJ
                }
            }
            return go ? 2 : unit ? 1 : dest ? 3 : 0;
        }

        public void UseQuestItem(int questId)
        {
            QuestView v = null;
            foreach (var q in _player.QuestLog) if (q.Id == questId) { v = q; break; }
            if (v == null || v.UseItemBagIndex < 0 || v.UseItemBagIndex >= _bagPos.Count) return;
            ItemProto d;
            if (!_itemProtos.TryGetValue(v.UseItemEntry, out d) || d.UseSpell <= 0) { UseItem(v.UseItemBagIndex); return; }
            int kind = QItemTargetKind(d.UseSpell);
            if (kind == 1 && (_target == 0 || _target == _player.Guid)) { Error("You have no target."); return; }   // ERR_GENERIC_NO_TARGET (GS:3064)
            if (kind == 2) { QUseItemOnObject(_bagPos[v.UseItemBagIndex], d.UseSpell, v.UseItemEntry + " '" + d.Name + "'"); return; }
            UseItem(v.UseItemBagIndex);                                             // unit (the target), ground (spellfx's circle), self / area / spell focus
        }

        /// <summary>A gameobject-target item spell: the nearest gameobject within the spell's range that sparkles for us (GAMEOBJECT_DYNAMIC 0x08 =
        /// ActivateToQuest, GameObject.cpp:2810-2828), else the nearest gameobject in range (ours). CMSG_USE_ITEM with TARGET_FLAG_GAMEOBJECT 0x800 +
        /// its packed guid (SpellCastTargets::Read, Spell.cpp:170-171). True = handled (sent, or refused with WoW's text).</summary>
        bool QUseItemOnObject(BagPos bp, int spell, string label)
        {
            var db = SpellDb.Get(_files);
            var si = db != null ? db.Find(spell) : null;
            float range = si != null && si.RangeMax > 0f ? si.RangeMax : QItemDefaultRange;
            ulong best = 0; float bestD = float.MaxValue; bool bestSparkle = false;
            foreach (var e in _entities)
            {
                if (e.Kind != EntityKind.GameObject) continue;
                var o = FindObject(e.Guid);
                if (o == null || !QInReach(o, e, range)) continue;
                float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z, dd = dx * dx + dy * dy + dz * dz;
                bool sp = e.Lootable;                                                 // Objects.cs:239: the quest sparkle for us
                if ((sp && !bestSparkle) || (sp == bestSparkle && dd < bestD)) { best = e.Guid; bestD = dd; bestSparkle = sp; }
            }
            if (best == 0) { Error("Invalid target"); WowLog.Info("Online quests: " + label + " wants a gameobject - none within " + range + " yd"); return true; }   // SPELL_FAILED_BAD_TARGETS (GS:6949)
            if (++_itemCastCount == 0) _itemCastCount = 1;                             // Items.cs's counter (0 marks triggered casts in AC)
            var w = new PacketWriter().U8(bp.Bag).U8(bp.Slot).U8(_itemCastCount).U32((uint)spell).U64(bp.Guid).U32(0).U8(0).U32(QTargetGo).PackedGuid(best);
            _world.Send(Op.CMSG_USE_ITEM, w);
            var be = Find(best);
            WowLog.Info("Online quests: use " + label + " -> spell " + spell + " on gameobject " + (be != null ? be.Name : "?") + " " + best.ToString("X16") +
                        " (" + Math.Sqrt(bestD).ToString("0.0") + " yd" + (bestSparkle ? ", sparkling" : "") + ", CMSG_USE_ITEM TARGET_FLAG_GAMEOBJECT)");
            return true;
        }
    }
}
