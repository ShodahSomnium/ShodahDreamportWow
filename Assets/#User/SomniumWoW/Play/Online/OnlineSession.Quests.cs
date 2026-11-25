using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>
    /// Stage O2, owner quests (PLAN9 s2): talking to NPCs and gameobjects, the NPC dialog and the quest log online, exactly as the
    /// offline game fills NpcDialog / PlayerState.QuestLog and emits QuestAccepted / QuestProgress / QuestCompleted / QuestRewarded /
    /// QuestAbandoned. Protocol truth = AzerothCore (copies in P/acsrc/quests/): GossipDef.cpp (PlayerMenu::SendGossipMenu,
    /// SendQuestGiverQuestList, SendQuestGiverQuestDetails, SendQuestQueryResponse, SendQuestGiverOfferReward,
    /// SendQuestGiverRequestItems), QuestHandler.cpp (every CMSG_QUESTGIVER_* reader), PlayerQuest.cpp (PrepareQuestMenu icons,
    /// SendPreparedQuest, the SMSG_QUESTUPDATE_* senders), QuestPackets.cpp (the small writers), QueryHandler.cpp (NPC text,
    /// gameobject query), NPCHandler.cpp (gossip hello), SpellHandler.cpp + SpellEffects.cpp (gameobject use / Opening).
    ///
    /// The UI (Game/Play/Ui/UiDialogs.cs UiQuestDialog) turns its pages (greeting, details, progress, reward) from CurrentDialog
    /// alone, without asking the session, and lays a page out once. So the dialog is PUBLISHED only when the server's texts for it
    /// are in: the NPC text (CMSG_NPC_TEXT_QUERY), every quest template (CMSG_QUEST_QUERY), what the giver says about each quest
    /// (CMSG_QUESTGIVER_QUERY_QUEST -> SMSG_QUESTGIVER_QUEST_DETAILS for quests on offer, CMSG_QUESTGIVER_COMPLETE_QUEST /
    /// CMSG_QUESTGIVER_REQUEST_REWARD -> SMSG_QUESTGIVER_REQUEST_ITEMS / SMSG_QUESTGIVER_OFFER_REWARD for quests to hand in - the
    /// packets the real client sends when a quest title / "Continue" is clicked, sent up front) and the names of reward items and
    /// objective targets - or after QDialogWaitSeconds. Every one of those requests has no effect on the server beyond its answer
    /// (the handlers are quoted where they are sent); quests flagged auto-accept (QUEST_FLAGS_AUTO_ACCEPT) are never asked ahead.
    /// </summary>
    public sealed partial class OnlineSession
    {
        NpcDialog _dialog;

        public NpcDialog CurrentDialog { get { return _dialog; } }

        // ------------------------------------------------------------------ ours (PLAN7 rule 7: named, logged once at start)
        const double QDialogWaitSeconds = 1.5;   // ours: the longest an NPC dialog waits for the server's texts before it opens anyway
        const double QHelloTimeout = 5.0;        // ours: an NPC answer later than this after Interact no longer opens a dialog
        const float QDialogCloseYards = 10f;     // ours (the offline game's value, OfflineSession.Stats): walking further away closes it
        const float QBagCheckSeconds = 0.25f;    // ours: how often item objectives re-count the bags
        const double QEventWaitSeconds = 1.5;    // ours: a quest event waits this long at most for the quest's title

        // ------------------------------------------------------------------ AzerothCore / 3.3.5a facts
        const int QLogSlots = 25, QSlotFields = 5;                     // MAX_QUEST_LOG_SIZE (QuestDef.h:33), MAX_QUEST_OFFSET (Player.h:615)
        const uint QStateComplete = 0x1, QStateFail = 0x2;             // QuestSlotStateMask QUEST_STATE_COMPLETE / _FAIL (Player.h:617)
        const float QInteractionDistance = 5.5f;                       // INTERACTION_DISTANCE (ObjectDefines.h:24)
        const float QDefaultObjectSize = 0.388999998569489f;           // DEFAULT_WORLD_OBJECT_SIZE (ObjectDefines.h:44)
        const float QDefaultCombatReach = 1.5f;                        // DEFAULT_COMBAT_REACH (ObjectDefines.h:45)
        const uint QNpcGossip = 0x1, QNpcQuestGiver = 0x2;             // UNIT_NPC_FLAG_GOSSIP / _QUESTGIVER (UnitDefines.h:322-323)
        const ulong QHighPlayer = 0x0000;                              // ObjectGuid.h HighGuid::Player: (guid >> 48) == 0 - iteration 11 (shared quests)
        const double QTimerRefreshSeconds = 1.0;                       // ours: the log views of a timed quest are rebuilt once a second
        const uint QFlagHiddenRewards = 0x200, QFlagAutoComplete = 0x10000, QFlagAutoAccept = 0x80000;   // QuestFlags (QuestDef.h:141-151)
        const int QIconAvailable = 2, QIconTurnIn = 4, QIconAutoComplete = 0;   // PlayerQuest.cpp PrepareQuestMenu AddMenuItem icons
        const int QEffectOpenLock = 33;                                 // SPELL_EFFECT_OPEN_LOCK (SharedDefines.h:799)
        const int QLockKeyItem = 1, QLockKeySkill = 2, QLockKeySpell = 3;       // LockKeyType (SharedDefines.h:2588-2591)
        const int QLockTypeOpen = 5;                                    // LOCKTYPE_OPEN (SharedDefines.h:2600)
        const uint QTargetGameObject = 0x800;                           // TARGET_FLAG_GAMEOBJECT (SpellInfo.h)
        const int QItemClassQuest = 12;                                 // ITEM_CLASS_QUEST (ItemTemplate.h)
        // Lock.dbc (AC DBCStructure.h LockEntry: 0 ID, 1-8 Type, 9-16 Index, 17-24 Skill, 25-32 Action; 33 fields - the probe measures it)
        const int QLockFieldCount = 33, QLockType = 1, QLockIndex = 9;
        // LockType.dbc 1 = Name_lang (the probe measures "Herbalism" for id 2); ChrClasses.dbc 4 / ChrRaces.dbc 14 = Name_lang (DbcFields)
        const int QLockTypeName = 1;
        // QuestXP.dbc (AC DBCStructure.h QuestXPEntry: 0 ID = quest level, 1-10 Exp[10]; 11 fields - the probe measures it)
        const int QQuestXpFieldCount = 11, QQuestXpExp = 1;
        const int QMaxLevel = 80;                                       // DEFAULT_MAX_LEVEL (SharedDefines.h): no quest XP at the level cap
        // GameobjectTypes (SharedDefines.h) and where each keeps its lock (GameObjectData.h GetLockId)
        const int QGoDoor = 0, QGoButton = 1, QGoQuestGiver = 2, QGoChest = 3, QGoGoober = 10, QGoMailbox = 19, QGoSpellCaster = 22, QGoMeetingStone = 23, QGoRitual = 18;
        const int QGoText = 9, QGoFishingNode = 17, QGoGuildBank = 34;  // GameobjectTypes (SharedDefines.h) - iteration 11 (npc H3 / trainer Q2 / social H1)

        // ------------------------------------------------------------------ state
        sealed class QTemplate      // SMSG_QUEST_QUERY_RESPONSE
        {
            public int Id, Method, Level, MinLevel, Type, NextInChain, XpId, RewardMoney, SrcItem, PlayersSlain, ZoneOrSort;
            public uint Flags;
            public readonly int[] RewItem = new int[4], RewItemCount = new int[4], RewChoice = new int[6], RewChoiceCount = new int[6];
            public readonly int[] ReqNpcOrGo = new int[4], ReqNpcOrGoCount = new int[4], ReqItem = new int[6], ReqItemCount = new int[6];
            public readonly string[] ObjectiveText = new string[4];
            public string Title = "", Objectives = "", Details = "", AreaDescription = "", CompletedText = "";
        }

        sealed class QTalk          // what a quest giver said about one quest, kept for the session (the newest wins)
        {
            public string Title, Details, Objectives, Progress, Completion;
            public double DetailsAt = -100, ProgressAt = -100, RewardAt = -100;
            public bool Completable;
            public int Xp = -1, Money = -1, DetailsXp = -1, DetailsMoney = -1;
            public List<int[]> Choices, Items, DetailsChoices, DetailsItems;   // {entry, count, display}
            public int DetailsSuggested, RewSpell, RewSpellCast, DetailsRewSpell, DetailsRewSpellCast;   // iteration 11
            public uint FullFlags;                                                                    // iteration 11: the details' full flags
            // out11_questfix: the giver's last word on an ACTIVE quest - REQUEST_ITEMS' completable flag (0x03) or an OFFER_REWARD (= completable),
            // from which NPC, when. The 3.3.5 client enables "Continue" from it (QuestFrame.lua:135 IsQuestCompletable()), not from the log.
            public bool SrvCompletable;
            public ulong SrvNpc;
            public double SrvAt = -100;
        }

        sealed class QMenuQuest { public int Id, Icon, Level; public uint Flags; public string Title; public bool Repeatable; }

        sealed class QMenu          // one NPC conversation, built from the server's packets
        {
            public ulong Npc;
            public string Source;
            public int TextId = -1;
            public string Greeting;                  // null while the NPC text is awaited
            public readonly List<QMenuQuest> Quests = new List<QMenuQuest>();
            public readonly List<string> Options = new List<string>();
            public readonly Dictionary<int, int> Waits = new Dictionary<int, int>();   // quest -> WaitDetails | WaitProgress | WaitReward
            public double Started, Deadline;
            public bool Published;
            public ulong Sharer;                     // iteration 11: a pushed quest's sharer (accept goes to them, closing declines)
            public int OpenQuest, OpenPage, OpenSerial;   // iteration 11: the page the server sent (NpcDialog.OpenQuestId / OpenPage / OpenSerial)
            public int MenuId;                                                    // iteration 11 (npc H3): echoed by CMSG_GOSSIP_SELECT_OPTION
            public readonly List<GossipOptionView> GossipOptions = new List<GossipOptionView>();
            // out11_questfix: giver answers still owed to OUR requests, per quest (each CMSG_QUESTGIVER_QUERY_QUEST / COMPLETE_QUEST / REQUEST_REWARD
            // owes one). Only an answer nobody owes is the server opening a page itself - the old bit test took the 2nd OFFER_REWARD of a hand-in
            // (COMPLETE_QUEST and REQUEST_REWARD both answer one) for that and jumped the open dialog to the reward page.
            public readonly Dictionary<int, int> Owed = new Dictionary<int, int>();
            public readonly HashSet<int> RewardAsked = new HashSet<int>();         // out11_questfix: REQUEST_REWARD sent in this conversation
        }
        const int QWaitDetails = 1, QWaitProgress = 2, QWaitReward = 4;

        sealed class QItemInfo { public string Name = ""; public int Quality = 1, Display, InventoryType, Class; public bool Missing; }
        sealed class QPending { public GameEvent E; public int QuestId; public Func<string> Text; public double Until; }

        QMenu _qMenu;
        ulong _qHelloGuid;
        double _qHelloAt = -100;
        int _qCancelsPending;
        double _qCancelAt = -100;
        bool _qDialogDirty, _qLogDirty, _qSlotsKnown;
        float _qBagTimer;
        byte _qCastCount = 0x80;                       // our own CMSG_CAST_SPELL counter (never 0 = triggered in AC; the spells owner counts from 1)
        readonly Dictionary<int, QTemplate> _qTemplates = new Dictionary<int, QTemplate>();
        readonly HashSet<int> _qAsked = new HashSet<int>();
        readonly Dictionary<int, QTalk> _qTalk = new Dictionary<int, QTalk>();
        readonly Dictionary<int, string> _qNpcTexts = new Dictionary<int, string>();
        readonly HashSet<int> _qTextAsked = new HashSet<int>();
        readonly Dictionary<int, QItemInfo> _qItems = new Dictionary<int, QItemInfo>();
        readonly HashSet<int> _qItemAsked = new HashSet<int>();
        readonly Dictionary<int, uint[]> _qGoData = new Dictionary<int, uint[]>();      // gameobject entry -> {type, data0..23}
        readonly uint[] _qSlots = new uint[QLogSlots * QSlotFields], _qScratch = new uint[QLogSlots * QSlotFields];
        readonly HashSet<int> _qAbandoning = new HashSet<int>(), _qRewarded = new HashSet<int>();
        readonly Dictionary<long, int> _qItemSeen = new Dictionary<long, int>();
        readonly Dictionary<int, int[]> _qChoiceSlots = new Dictionary<int, int[]>();   // quest -> RewardChoiceItemId index of each RewardChoices entry
        readonly List<QPending> _qEvents = new List<QPending>();
        readonly HashSet<ushort> _qOddLogged = new HashSet<ushort>();
        DbcTable _qItemDisplay, _qLock, _qLockType, _qClasses, _qRaces, _qQuestXp;
        bool _qDbcTried;
        int _qDialogs, _qAccepted, _qRewardsSent, _qTemplatesIn;
        int _qOpenSerial;                                                                  // iteration 11
        float _qTimerTick;
        bool _qAnyTimed;
        readonly Dictionary<int, float> _qTimerTotal = new Dictionary<int, float>();

        void InitQuests()
        {
            if (_world == null) return;
            QOn(Op.SMSG_GOSSIP_MESSAGE, OnQGossipMessage);
            QOn(Op.SMSG_GOSSIP_COMPLETE, OnQGossipComplete);
            QOn(Op.SMSG_NPC_TEXT_UPDATE, OnQNpcText);
            QOn(Op.SMSG_QUESTGIVER_QUEST_LIST, OnQQuestList);
            QOn(Op.SMSG_QUESTGIVER_QUEST_DETAILS, OnQDetails);
            QOn(Op.SMSG_QUESTGIVER_REQUEST_ITEMS, OnQRequestItems);
            QOn(Op.SMSG_QUESTGIVER_OFFER_REWARD, OnQOfferReward);
            QOn(Op.SMSG_QUESTGIVER_QUEST_COMPLETE, OnQQuestComplete);
            QOn(Op.SMSG_QUESTGIVER_QUEST_INVALID, OnQInvalid);
            QOn(Op.SMSG_QUESTGIVER_QUEST_FAILED, OnQGiverFailed);
            QOn(Op.SMSG_QUESTUPDATE_FAILEDTIMER, OnQFailedTimer);
            QOn(Op.SMSG_QUESTLOG_FULL, OnQLogFull);
            QOn(Op.SMSG_QUEST_QUERY_RESPONSE, OnQQueryResponse);
            QOn(Op.SMSG_QUESTUPDATE_ADD_KILL, OnQAddKill);
            QOn(Op.SMSG_QUESTUPDATE_ADD_PVP_KILL, OnQAddPvpKill);
            QOn(Op.SMSG_QUESTUPDATE_ADD_ITEM, OnQAddItem);
            QOn(Op.SMSG_QUESTUPDATE_COMPLETE, OnQUpdateComplete);
            // passive readers (several handlers per opcode run in order): names of reward / required items (the items owner keeps the
            // bag cache) and the type data (lock ids) of gameobjects (Objects.cs keeps their names)
            QOn(Op.SMSG_ITEM_QUERY_SINGLE_RESPONSE, OnQItemQuery);
            QOn(Op.SMSG_GAMEOBJECT_QUERY_RESPONSE, OnQGameObjectQuery);
            WowLog.Info("Online quests: ours - the dialog waits up to " + QDialogWaitSeconds + " s for the server's texts, an NPC answer opens it up to "
                        + QHelloTimeout + " s after the hello, it closes beyond " + QDialogCloseYards + " yd, item objectives re-count every "
                        + QBagCheckSeconds + " s, quest events wait up to " + QEventWaitSeconds + " s for a title");
            InitQuestExtras();                                                   // iteration 11 (OnlineSession.QuestExtras.cs)
            InitQuestPoi();                                                      // iteration 11 quest map (OnlineSession.QuestPoi.cs)
        }

        /// <summary>PLAN9 rule 2: a packet shorter than AzerothCore writes it (or otherwise odd) is dropped with ONE warning per opcode.</summary>
        void QOn(ushort op, Action<WowPacket> fn)
        {
            _world.On(op, p =>
            {
                try { fn(p); }
                catch (PacketTooShortException ex)
                {
                    if (_qOddLogged.Add(op)) WowLog.Warn("Online quests: " + p.Name + " (" + p.Length + " bytes) is not what AzerothCore writes (" + ex.Message + ") - dropped (logged once)");
                }
            }, this);
        }

        void TickQuests(float dt)
        {
            if (_world == null) return;
            QReadLogFields();
            TickQuestExtras(dt);                                                 // iteration 11
            if (_qAnyTimed && (_qTimerTick -= dt) <= 0f) { _qTimerTick = (float)QTimerRefreshSeconds; _qLogDirty = true; }
            _qBagTimer -= dt;
            if (_qBagTimer <= 0f) { _qBagTimer = QBagCheckSeconds; QCheckBagCounts(); }
            if (_qLogDirty)
            {
                _qLogDirty = false;
                QRefreshLog();
                _qDialogDirty = true;
                QPoiFlush();                                                     // iteration 11 quest map: the log's quests not asked yet
            }
            QFlushEvents();
            var m = _qMenu;
            if (m == null) return;
            if (!m.Published)
            {
                if (QMenuReady(m) || _now >= m.Deadline) QPublish(m);
            }
            else if (_qDialogDirty)
            {
                _qDialogDirty = false;
                _dialog = QBuildDialog(m);
            }
            QAutoClose(m);
        }

        // ================================================================== IWowSession: talking to NPCs and objects

        /// <summary>
        /// Interact routing (PLAN9 s2): a dead unit -> lootdeath's TryLootInteract (it answers every dead unit); a spirit healer ->
        /// TrySpiritHealer; a hostile unit or a non-friendly creature -> StartAttack (the offline rule); an NPC -> CMSG_GOSSIP_HELLO
        /// (UNIT_NPC_FLAG_GOSSIP) or CMSG_QUESTGIVER_HELLO; a gameobject -> CMSG_GAMEOBJ_USE or the Opening spell (QUseGameObject).
        /// </summary>
        public void Interact(ulong guid)
        {
            var e = Find(guid);
            var o = FindObject(guid);
            if (e == null || o == null || _world == null) return;
            if (e.Kind == EntityKind.GameObject) { QUseGameObject(e, o); return; }
            uint npcFlags = o.TypeId == TypeId.Unit ? o.U32(UF.UNIT_NPC_FLAGS) : 0u;
            // iteration 11 (quests): npc flags = alive for AzerothCore - a dead creature has none (Creature.cpp:1988 ReplaceAllNpcFlags(NONE));
            // a corpse-looking NPC (feign death 29266 / UNIT_DYNFLAG_DEAD with health, 28 templates) talks (Player.cpp:2134).
            bool serverAlive = npcFlags != 0;
            if (o.TypeId == TypeId.Unit && !serverAlive && TryLootInteract(guid)) return;
            if (e.Dead && !serverAlive) return;
            if (TrySpiritHealer(guid)) return;
            if (e.Kind == EntityKind.Player && e.PlayerVehicle && !_player.Dead) { PetsEnterPlayerVehicle(guid); return; }   // iteration 11: pets H4b (a player's vehicle mount)
            if (e.Kind == EntityKind.Player || e.Kind == EntityKind.Pet || e.Kind == EntityKind.Totem) { SetTarget(guid); return; }   // trade, duel, inspect: the interact area's menus
            if (e.Reaction == Reaction.Hostile || (e.Kind == EntityKind.Creature && e.Reaction != Reaction.Friendly)) { StartAttack(guid); return; }
            SetTarget(guid);
            if (e.Kind != EntityKind.Npc) return;
            if (_player.Dead) { Error("You can't do that when you're dead."); return; }                         // ERR_PLAYER_DEAD (GlobalStrings.lua:3387)
            if (!QInReach(o, e, QInteractionDistance)) { Error("You need to be closer to interact with that target."); return; }   // ERR_TOO_FAR_TO_INTERACT (GlobalStrings.lua:3537)
            uint npc = o.U32(UF.UNIT_NPC_FLAGS);
            // iteration 11 (npc H3, OnlineSession.Npc.cs): spell click, vendor, banker, the registered services (trainer, flight master, mailbox,
            // petitioner, stable, auctioneer, battlemaster...), the server's default menu - false only for GOSSIP / QUESTGIVER npcs (the hello below)
            if (NpcRoute(e, o, npc)) return;
            // NPCHandler.cpp HandleGossipHelloOpcode / QuestHandler.cpp HandleQuestgiverHelloOpcode: "recvData >> guid" (uint64)
            ushort op = (npc & QNpcGossip) != 0 ? Op.CMSG_GOSSIP_HELLO : Op.CMSG_QUESTGIVER_HELLO;
            _world.Send(op, new PacketWriter().U64(guid));
            _qHelloGuid = guid;
            _qHelloAt = _now;
            WowLog.Info("Online quests: " + Op.Name(op) + " to " + (e.Name ?? "?") + " (" + guid.ToString("X16") + ", npc flags 0x" + npc.ToString("X") +
                        (e.Dead ? ", looks dead - alive for the server" : "") + ")");
        }

        /// <summary>CMSG_QUESTGIVER_CANCEL (QuestHandler.cpp HandleQuestgiverCancel: SendCloseGossip - the one SMSG_GOSSIP_COMPLETE it causes is swallowed).</summary>
        public void CloseDialog()
        {
            bool had = _dialog != null || _qMenu != null;
            var m = _qMenu;                                                      // iteration 11: a shared quest closed unanswered = declined
            if (m != null && m.Sharer != 0 && _world != null && !_world.IsClosed)
            {
                int q = m.OpenQuest != 0 ? m.OpenQuest : (m.Quests.Count > 0 ? m.Quests[0].Id : 0);
                // MSG_QUEST_PUSH_RESULT (uint64 sharer, uint32 quest, uint8 QUEST_PARTY_MSG_DECLINE_QUEST 3) - QuestPackets.cpp:98-105; the server forwards
                // it and clears our divider (QuestHandler.cpp:605-618) - without it later shares answer BUSY (:584-587)
                _world.Send(Op.MSG_QUEST_PUSH_RESULT, new PacketWriter().U64(m.Sharer).U32((uint)q).U8(3));
                WowLog.Info("Online quests: shared quest " + q + " declined (MSG_QUEST_PUSH_RESULT 3 to " + m.Sharer.ToString("X16") + ")");
            }
            _dialog = null;
            _qMenu = null;
            _qHelloGuid = 0;
            if (!had || _world == null || _world.IsClosed) return;
            _world.Send(Op.CMSG_QUESTGIVER_CANCEL);
            _qCancelsPending++;
            _qCancelAt = _now;
        }

        /// <summary>
        /// CMSG_QUESTGIVER_ACCEPT_QUEST (QuestHandler.cpp HandleQuestgiverAcceptQuestOpcode: "recvData >> guid >> questId >> unk1",
        /// uint64 + uint32 + uint32). The server adds the quest (PLAYER_QUEST_LOG_* -> QuestAccepted) and answers SendCloseGossip:
        /// the dialog closes like the real client's quest frame.
        /// </summary>
        public void AcceptQuest(int questId)
        {
            if (_world == null) { Error(NotYet); return; }
            var m = _qMenu;
            if (m == null || QMenuQuestOf(m, questId) == null) { Error("Unknown quest"); return; }                     // ours, as OfflineSession
            if (QLogSlotOf(questId) >= 0)
            {
                // iteration 11: an auto-accept quest is added when its details are queried (QuestHandler.cpp:222-223): already in the log = accepted
                if ((QMenuQuestOf(m, questId).Flags & QFlagAutoAccept) != 0) { CloseDialog(); return; }
                Error("You are already on that quest"); return;                                                        // ERR_QUEST_ALREADY_ON (GlobalStrings.lua:3412)
            }
            ulong giver = m.Sharer != 0 ? m.Sharer : m.Npc;                     // iteration 11: a shared quest is accepted FROM the sharer (QuestHandler.cpp:124-130 / :150-158)
            _world.Send(Op.CMSG_QUESTGIVER_ACCEPT_QUEST, new PacketWriter().U64(giver).U32((uint)questId).U32(0));
            if (m.Sharer != 0) m.Sharer = 0;                                     // the server answered the sharer ACCEPT and cleared our divider
            _qAccepted++;
            ScheduleQuestQuery(1.0);                                         // the giver's marker changes (no status packet is sent for it)
            WowLog.Info("Online quests: accept quest " + questId + " '" + QTitle(questId) + "' at " + giver.ToString("X16") + (giver != m.Npc ? " (the sharer)" : ""));
        }

        /// <summary>
        /// CMSG_QUESTGIVER_CHOOSE_REWARD (QuestHandler.cpp HandleQuestgiverChooseRewardOpcode: "recvData >> guid >> questId >> reward",
        /// reward = the RewardChoiceItemId index, &lt; QUEST_REWARD_CHOICES_COUNT). The server rewards (SMSG_QUESTGIVER_QUEST_COMPLETE ->
        /// QuestRewarded + MoneyReceived; items via SMSG_ITEM_PUSH_RESULT; XP via SMSG_LOG_XPGAIN) and may offer the next quest.
        /// </summary>
        public void CompleteQuest(int questId, int rewardChoiceIndex)
        {
            if (_world == null) { Error(NotYet); return; }
            var m = _qMenu;
            if (m == null || QMenuQuestOf(m, questId) == null) { Error("Turn that quest in to someone else"); return; }  // ours, as OfflineSession
            string basis;
            if (QStateInMenu(QMenuQuestOf(m, questId), m, out basis) != QuestState.Complete)
            {
                WowLog.Info("Online quests: hand-in of quest " + questId + " '" + QTitle(questId) + "' refused here - " + basis);
                Error("That quest is not complete yet"); return;                                                     // ours, as OfflineSession
            }
            int[] slots;
            int reward = 0;
            if (_qChoiceSlots.TryGetValue(questId, out slots) && slots.Length > 0)
                reward = slots[rewardChoiceIndex >= 0 && rewardChoiceIndex < slots.Length ? rewardChoiceIndex : 0];
            _world.Send(Op.CMSG_QUESTGIVER_CHOOSE_REWARD, new PacketWriter().U64(m.Npc).U32((uint)questId).U32((uint)reward));
            m.Owed.Remove(questId);                                              // out11_questfix: a refused reward comes back as an OFFER_REWARD page (QuestHandler.cpp:350-351)
            _qRewardsSent++;
            ScheduleQuestQuery(1.0);
            WowLog.Info("Online quests: hand in quest " + questId + " '" + QTitle(questId) + "' at " + m.Npc.ToString("X16") + " reward index " + reward + " (choice " + rewardChoiceIndex + ") - hand-in path: " + basis);
        }

        /// <summary>CMSG_QUESTLOG_REMOVE_QUEST (QuestPackets.cpp QuestLogRemoveQuest::Read: uint8 Slot). QuestAbandoned follows when the slot empties.</summary>
        public void AbandonQuest(int questId)
        {
            if (_world == null) return;
            int slot = QLogSlotOf(questId);
            if (slot < 0) return;
            _world.Send(Op.CMSG_QUESTLOG_REMOVE_QUEST, new PacketWriter().U8(slot));
            _qAbandoning.Add(questId);
            ScheduleQuestQuery(1.0);
            WowLog.Info("Online quests: abandon quest " + questId + " '" + QTitle(questId) + "' (log slot " + slot + ")");
        }

        // ================================================================== gameobjects

        /// <summary>
        /// A click on a gameobject. AC GameObject::Use (GameObject.cpp:1479) handles doors, buttons, quest givers, goobers, spell casters,
        /// meeting stones... but has NO chest case: a chest (and any object with a lock) is opened by casting a known spell with
        /// SPELL_EFFECT_OPEN_LOCK whose MiscValue is the lock's type (Spell::EffectOpenLock -> CanOpenLock -> SendLoot, SpellEffects.cpp
        /// 2136-2290): the server then sends SMSG_LOOT_RESPONSE (lootdeath's loot window).
        /// </summary>
        void QUseGameObject(EntityState e, OnlineObject o)
        {
            if (_player.Dead) { Error("You can't do that when you're dead."); return; }                         // ERR_PLAYER_DEAD (GlobalStrings.lua:3387)
            int type = e.GoType;
            if (!QInReach(o, e, QGoInteractionDistance(type))) { Error("You are too far away."); return; }        // ERR_USE_TOO_FAR (GlobalStrings.lua:3579)
            int lockId = QGoLockId(e.Entry, type);
            // iteration 11 (quests): a QUEST GIVER opens its menu with CMSG_GAMEOBJ_USE whatever its lock - HandleGameObjectUseOpcode checks only the
            // distance (SpellHandler.cpp:329-348) and GameObject::Use case QUESTGIVER has no lock check (GameObject.cpp:1526-1535). The 200 locked
            // quest objects (lock 43 'Open Kneeling' bodies / corpses, crates, graves...) need no 1-s Opening and work while the avatar moves.
            if (type != QGoQuestGiver && (type == QGoChest || lockId != 0)) { QOpenLock(e, lockId); return; }
            if (type == QGoMeetingStone && !QMeetingStoneOk(e)) return;
            switch (type)
            {
                case QGoDoor: case QGoButton: case QGoQuestGiver: case QGoGoober: case QGoSpellCaster: case QGoMeetingStone: case QGoRitual:
                    // lead fix 2026-10-26 (user: "make meeting stones work"): the stone casts 23598 -> triggers 59782 'Summoning Stone Effect'
                    // (Spell.dbc: effect 50 TRANS_DOOR, gameobject 179944) = a SUMMONING_RITUAL portal (type 18) the other party members click
                    // (AC GameObject::Use case 18); the summoned player then gets SMSG_SUMMON_REQUEST (OnlineSession.Interact.cs). Type 18 sent nothing.
                    // SpellHandler.cpp HandleGameObjectUseOpcode: "recvData >> guid" (uint64) -> IsWithinDistInMap(GetInteractionDistance) -> Use
                    _world.Send(Op.CMSG_GAMEOBJ_USE, new PacketWriter().U64(e.Guid));
                    _qHelloGuid = e.Guid;                                        // a quest giver / goober answers with its gossip or quests
                    _qHelloAt = _now;
                    WowLog.Info("Online quests: CMSG_GAMEOBJ_USE on " + (e.Name ?? "?") + " (type " + type + ", entry " + e.Entry +
                                (type == QGoQuestGiver && lockId != 0 ? ", lock " + lockId + " - a quest giver opens without the Opening spell" : "") + ")");
                    break;
                case QGoMailbox:
                    OpenMailbox(e.Guid);                                         // iteration 11 (social H1): CMSG_GET_MAIL_LIST, the mail window opens
                    break;
                case QGoText:                                                    // 9: books, plaques - opened locally (AC GameObject::Use has no TEXT case) - npc H3
                    NpcReadObjectText(e);
                    break;
                case QGoFishingNode:                                             // 17: the fishing bobber - trainer Q2
                    PfUseBobber(e);
                    break;
                case QGoGuildBank:                                               // GAMEOBJECT_TYPE_GUILD_BANK (SharedDefines.h) - iteration 11 (social), gap G3
                    Error("That window is not in this version yet.");            // ours: the npc area's text for a service window we do not have
                    WowLog.Info("Online social: guild vault " + (e.Name ?? "?") + " (gameobject type 34) - the guild bank is not in this version (gap G3)");
                    break;
                default:
                    WowLog.Info("Online quests: gameobject " + (e.Name ?? "?") + " of type " + type + " has no use online (nothing sent)");
                    break;
            }
        }

        /// <summary>
        /// Lead fix 2026-10-26: AC GameObject::Use case MEETINGSTONE (GameObject.cpp:2029-2054) returns SILENTLY without a party member target
        /// or below the stone's minimum level (data0 minLevel, data1 maxLevel, data2 area) - the client checks first and says why, with its own
        /// GlobalStrings.lua texts (ERR_MEETING_STONE_*).
        /// </summary>
        bool QMeetingStoneOk(EntityState stone)
        {
            ulong tg = TargetGuid;
            if (tg == 0 || tg == _player.Guid) { Error("No target selected"); return false; }                               // ERR_MEETING_STONE_INVALID_TARGET
            if (_player.Group == null || _player.Group.Count == 0) { Error("You need to be in a party to use a meeting stone"); return false; }   // ERR_MEETING_STONE_NEED_PARTY
            GroupMemberView member = null;                                   // the target is often far away (picked from the party frame)
            for (int i = 0; i < _player.Group.Count; i++) if (_player.Group[i].Guid == tg) { member = _player.Group[i]; break; }
            if (member == null) { Error("Your target is not in the party"); return false; }                                         // ERR_MEETING_STONE_TARGET_NOT_IN_PARTY
            uint[] d;
            int minLevel = _qGoData.TryGetValue(stone.Entry, out d) ? (int)d[1 + 0] : 0;
            if (minLevel > 0 && _player.Level < minLevel) { Error("You do not meet the level requirements of this meeting stone."); return false; }   // ERR_MEETING_STONE_INVALID_LEVEL
            EntityState t = Find(tg);
            int theirLevel = t != null && t.Level > 0 ? t.Level : member.Level;
            if (minLevel > 0 && theirLevel > 0 && theirLevel < minLevel) { Error("Your target does not meet the level requirements of this meeting stone."); return false; }   // ERR_MEETING_STONE_TARGET_INVALID_LEVEL
            WowLog.Info("Online quests: meeting stone " + (stone.Name ?? "?") + " -> summon " + (member.Name ?? "0x" + tg.ToString("X")) + " (min level " + minLevel + "); one more party member clicks the portal - gameobject 179944 reqParticipants 2 incl. the summoner, AC base SQL");
            return true;
        }

        /// <summary>GameObject::GetInteractionDistance (GameObject.cpp:2914).</summary>
        static float QGoInteractionDistance(int type)
        {
            switch (type)
            {
                case 12: return 0f;                                              // AREADAMAGE
                case 2: case 9: case 24: case 26: case 27: return 5.5555553f;    // QUESTGIVER, TEXT, FLAGSTAND, FLAGDROP, MINI_GAME
                case 4: return 10f;                                              // BINDER
                case 7: case 32: return 3f;                                      // CHAIR, BARBER_CHAIR
                case 17: return 100f;                                            // FISHINGNODE
                case 25: return 20f + 0.5f;                                      // FISHINGHOLE: 20 + CONTACT_DISTANCE (ObjectDefines.h)
                case 13: case 14: case 31: case 33: case 0: return 5f;           // CAMERA, MAP_OBJECT, DUNGEON_DIFFICULTY, DESTRUCTIBLE_BUILDING, DOOR
                case 34: case 19: return 10f;                                    // GUILD_BANK, MAILBOX ("not blizzlike" 10)
                default: return QInteractionDistance;
            }
        }

        /// <summary>GameObjectTemplate::GetLockId (GameObjectData.h:428): door / button data1, quest giver / chest / trap / goober ... data0.</summary>
        int QGoLockId(int entry, int type)
        {
            uint[] d;
            if (!_qGoData.TryGetValue(entry, out d)) return 0;
            switch (type)
            {
                case 0: case 1: return (int)d[1 + 1];
                case 2: case 3: case 6: case 10: case 12: case 13: case 24: case 25: case 26: return (int)d[1 + 0];
                default: return 0;
            }
        }

        /// <summary>
        /// Casts the known Opening spell for a lock (Spell::CanOpenLock: a LOCK_KEY_SKILL entry needs a spell whose OPEN_LOCK MiscValue
        /// is the entry's Index = LockType; lock id 0 opens with any). CMSG_CAST_SPELL: uint8 castCount, uint32 spell, uint8 castFlags,
        /// SpellCastTargets::Read (ac_Spell.cpp:163): uint32 targetMask, TARGET_FLAG_GAMEOBJECT -> packed guid.
        /// </summary>
        void QOpenLock(EntityState e, int lockId)
        {
            QLoadDbcs();
            var db = SpellDb.Get(_files);
            int spell = 0;
            string need = null;
            if (lockId == 0) spell = QFindOpenSpell(db, QLockTypeOpen, true);
            else if (_qLock != null)
            {
                int r = _qLock.FindRow(lockId);
                for (int i = 0; r >= 0 && i < 8 && spell == 0; i++)
                {
                    int kind = _qLock.GetInt(r, QLockType + i), index = _qLock.GetInt(r, QLockIndex + i);
                    if (kind == QLockKeySkill) { spell = QFindOpenSpell(db, index, false); if (spell == 0 && need == null) need = QLockTypeNameOf(index); }
                    else if (kind == QLockKeySpell && index > 0 && _known.Contains(index)) spell = index;
                    else if (kind == QLockKeyItem && index > 0 && need == null) need = QItemName(index);
                }
            }
            if (spell == 0)
            {
                if (!string.IsNullOrEmpty(need)) Error("Requires " + need);          // ERR_USE_LOCKED_WITH_SPELL_S / _WITH_ITEM_S "Requires %s" (GlobalStrings.lua:3573-3575)
                else Error("You can't open that.");                                  // ERR_USE_CANT_OPEN (GlobalStrings.lua:3570)
                WowLog.Info("Online quests: " + (e.Name ?? "?") + " (lock " + lockId + ") - no known Opening spell" + (need != null ? ", requires " + need : ""));
                return;
            }
            // a cast-time Opening / Herb Gathering / Mining behaves like the spells owner's SpCast: refused while moving (AC Spell::prepare
            // would interrupt it), a pending moving-flag packet is closed with MSG_MOVE_STOP first
            var si = db != null ? db.Find(spell) : null;
            bool timed = si != null && (si.CastTimeMs > 0 || si.IsChanneled);
            if (timed && SpMovingNow()) { FailLocal(spell, "Can't do that while moving", "opening " + (e.Name ?? "?") + " while the avatar moves"); return; }   // SPELL_FAILED_MOVING (GlobalStrings.lua:7083)
            if (timed) SpStopBeforeCast(_haveAvatar ? _avatarPos : _player.Pos);
            if (++_qCastCount == 0) _qCastCount = 0x80;
            _world.Send(Op.CMSG_CAST_SPELL, new PacketWriter().U8(_qCastCount).U32((uint)spell).U8(0).U32(QTargetGameObject).PackedGuid(e.Guid));
            // iteration 11: an opened object may answer with its menu (Spell::SendLoot SpellEffects.cpp:2156-2183: a goober's Use, scripts' GossipHello) -
            // the answer window (QHelloTimeout) starts when the cast ends
            _qHelloGuid = e.Guid;
            _qHelloAt = _now + (si != null ? Math.Max(0, si.CastTimeMs) / 1000.0 : 0.0);
            WowLog.Info("Online quests: open " + (e.Name ?? "?") + " (type " + e.GoType + ", lock " + lockId + ") with spell " + spell + " (castCount " + _qCastCount + ")");
        }

        /// <summary>A spell the server says we know (Spells.cs _known) with SPELL_EFFECT_OPEN_LOCK for this lock type (any: prefer LOCKTYPE_OPEN).</summary>
        int QFindOpenSpell(SpellDb db, int lockType, bool any)
        {
            if (db == null) return 0;
            int fallback = 0;
            foreach (int id in _known)
            {
                var si = db.Find(id);
                if (si == null) continue;
                for (int k = 0; k < si.Effects.Length; k++)
                {
                    var ef = si.Effects[k];
                    if (ef == null || ef.Effect != QEffectOpenLock) continue;
                    if (ef.MiscValue == lockType) return id;
                    if (any && fallback == 0) fallback = id;
                }
            }
            return fallback;
        }

        string QLockTypeNameOf(int lockType)
        {
            if (_qLockType == null) return null;
            int r = _qLockType.FindRow(lockType);
            string n = r >= 0 ? _qLockType.GetString(r, QLockTypeName) : null;
            return string.IsNullOrEmpty(n) ? null : n;
        }

        // ================================================================== server packets: menus

        /// <summary>
        /// SMSG_GOSSIP_MESSAGE (PlayerMenu::SendGossipMenu, GossipDef.cpp:192-238): uint64 guid, uint32 menuId, uint32 titleTextId,
        /// uint32 count, count x (uint32 index, uint8 icon, uint8 coded, uint32 boxMoney, string message, string boxMessage), uint32
        /// quests, quests x (uint32 id, uint32 icon, int32 level, uint32 flags, uint8 repeatable, string title).
        /// </summary>
        void OnQGossipMessage(WowPacket p)
        {
            ulong npc = p.U64();
            int menuId = (int)p.U32();                                            // iteration 11 (npc H3): echoed by CMSG_GOSSIP_SELECT_OPTION
            int textId = (int)p.U32();
            uint nOpt = p.U32();
            var options = NpcReadGossipOptions(p, nOpt);                          // iteration 11 (OnlineSession.Npc.cs): index, icon, coded, money, text, box text
            uint nQ = p.Remaining >= 4 ? p.U32() : 0;
            var quests = QReadMenuQuests(p, nQ);
            if (!QWantsMenu(npc)) { WowLog.Info("Online quests: SMSG_GOSSIP_MESSAGE of " + npc.ToString("X16") + " not asked for by us - ignored"); return; }
            if (NpcAutoSelect(npc, menuId, options, quests.Count)) return;       // GossipFrame.lua:10-17: a lone non-gossip option is chosen at once
            var m = new QMenu { Npc = npc, TextId = textId, Source = "gossip", MenuId = menuId };
            foreach (var o in options) m.Options.Add(o.Text);                     // kept for the existing log lines
            m.GossipOptions.AddRange(options);
            m.Quests.AddRange(quests);
            string t;
            if (_qNpcTexts.TryGetValue(textId, out t)) m.Greeting = t;
            else QAskNpcText(textId, npc);
            QStartMenu(m);
        }

        /// <summary>
        /// SMSG_QUESTGIVER_QUEST_LIST (PlayerMenu::SendQuestGiverQuestList, GossipDef.cpp:289-345): uint64 guid, string greeting,
        /// uint32 emote delay, uint32 emote, uint8 count, count x (uint32 id, uint32 icon, int32 level, uint32 flags, uint8 repeatable, string title).
        /// </summary>
        void OnQQuestList(WowPacket p)
        {
            ulong npc = p.U64();
            string greeting = p.CString();
            p.U32(); p.U32();
            int n = p.U8();
            var quests = QReadMenuQuests(p, (uint)n);
            if (!QWantsMenu(npc)) { WowLog.Info("Online quests: SMSG_QUESTGIVER_QUEST_LIST of " + npc.ToString("X16") + " not asked for by us - ignored"); return; }
            var m = new QMenu { Npc = npc, Greeting = greeting ?? "", Source = "quest list" };
            m.Quests.AddRange(quests);
            QStartMenu(m);
        }

        List<QMenuQuest> QReadMenuQuests(WowPacket p, uint n)
        {
            var list = new List<QMenuQuest>();
            for (uint i = 0; i < n && p.Remaining > 0; i++)
            {
                var q = new QMenuQuest { Id = (int)p.U32(), Icon = (int)p.U32(), Level = p.I32(), Flags = p.U32() };
                q.Repeatable = p.U8() != 0;                                      // repeatable (blue "?" icon) - GossipDef.cpp:226 / :368, iteration 11
                q.Title = p.CString();
                list.Add(q);
            }
            return list;
        }

        /// <summary>SMSG_GOSSIP_COMPLETE (PlayerMenu::SendCloseGossip, empty): the server closed the conversation (after an accept, a failure...).</summary>
        void OnQGossipComplete(WowPacket p)
        {
            if (_qCancelsPending > 0 && _now - _qCancelAt > 3.0) _qCancelsPending = 0;   // an answer that never came
            if (_qCancelsPending > 0) { _qCancelsPending--; return; }
            if (_qMenu == null && _dialog == null) return;
            WowLog.Info("Online quests: the server closed the dialog with " + (_qMenu != null ? _qMenu.Npc.ToString("X16") : "?") + " (SMSG_GOSSIP_COMPLETE)");
            _qMenu = null;
            _dialog = null;
        }

        /// <summary>
        /// SMSG_NPC_TEXT_UPDATE (QueryHandler.cpp HandleNpcTextQueryOpcode:274-357): uint32 textId, 8 x (float probability, string text0,
        /// string text1, uint32 language, 3 x (uint32 delay, uint32 emote)). The greeting = option 0's Text_0, else its Text_1 (the choice
        /// AC itself makes for quest-list greetings, PlayerQuest.cpp SendPreparedQuest), else the first non-empty option.
        /// </summary>
        void OnQNpcText(WowPacket p)
        {
            int textId = (int)p.U32();
            string chosen = null, first = null;
            for (int i = 0; i < 8 && p.Remaining > 0; i++)
            {
                p.F32();
                string t0 = p.CString(), t1 = p.CString();
                p.U32();
                for (int k = 0; k < 3; k++) { p.U32(); p.U32(); }
                string t = !string.IsNullOrEmpty(t0) ? t0 : t1;
                if (i == 0 && !string.IsNullOrEmpty(t)) chosen = t;
                if (first == null && !string.IsNullOrEmpty(t)) first = t;
            }
            string text = chosen ?? first ?? "";
            _qNpcTexts[textId] = text;
            var m = _qMenu;
            if (m != null && m.TextId == textId) { m.Greeting = text; _qDialogDirty = true; }
        }

        /// <summary>
        /// SMSG_QUESTGIVER_QUEST_DETAILS (PlayerMenu::SendQuestGiverQuestDetails, GossipDef.cpp:388-503): uint64 npc, uint64 divider,
        /// uint32 quest, string title, details, objectives, uint8 activateAccept, uint32 flags, uint32 suggested players, uint8 0,
        /// uint32 choices + n x (id, count, display), uint32 items + n x (id, count, display), uint32 money, uint32 xp (all 0 with
        /// QUEST_FLAGS_HIDDEN_REWARDS), honor, float, spell, cast spell, title, talents, arena, 0, 5 factions, 5 values, 5 overrides,
        /// uint32 emote count + n x (emote, delay).
        /// </summary>
        void OnQDetails(WowPacket p)
        {
            ulong npc = p.U64();
            ulong divider = p.U64();                                             // iteration 11: GossipDef.cpp:406 GetDivider() - the sharer of a pushed quest
            int id = (int)p.U32();
            var talk = QTalkOf(id);
            talk.Title = p.CString();
            talk.Details = p.CString();
            talk.Objectives = p.CString();
            p.U8();                                                              // activateAccept
            uint flags = p.U32();
            talk.FullFlags = flags;                                              // the full flags (the query sends Flags & 0xFFFF only, GossipDef.cpp:574)
            talk.DetailsSuggested = (int)p.U32(); p.U8();
            talk.DetailsChoices = QReadItemTriples(p);
            talk.DetailsItems = QReadItemTriples(p);
            talk.DetailsMoney = Math.Max(0, p.I32());
            talk.DetailsXp = (int)p.U32();
            if (p.Remaining >= 16) { p.U32(); p.F32(); talk.DetailsRewSpell = (int)p.U32(); talk.DetailsRewSpellCast = p.I32(); }   // GossipDef.cpp:478-481
            talk.DetailsAt = _now;
            ulong sharer = npc == _player.Guid && divider != 0 ? divider : 0;  // HandlePushQuestToParty: our own guid + divider = the sharer (QuestHandler.cpp:596-598)
            QAfterTalk(npc, id, QWaitDetails, QIconAvailable, flags, talk.Title, "details", sharer);
        }

        /// <summary>
        /// SMSG_QUESTGIVER_REQUEST_ITEMS (PlayerMenu::SendQuestGiverRequestItems, GossipDef.cpp:764-841): uint64 npc, uint32 quest,
        /// string title, string text, uint32 0, uint32 emote, uint32 closeOnCancel, uint32 flags, uint32 suggested, uint32 required money,
        /// uint32 n + n x (id, count, display), uint32 0x00 (cannot complete) / 0x03 (can), uint32 0x04, 0x08, 0x10.
        /// </summary>
        void OnQRequestItems(WowPacket p)
        {
            ulong npc = p.U64();
            int id = (int)p.U32();
            var talk = QTalkOf(id);
            talk.Title = p.CString();
            talk.Progress = p.CString();
            p.U32(); p.U32(); p.U32();
            uint flags = p.U32();
            p.U32(); p.U32();
            QReadItemTriples(p);                                                 // the required items (the template has them too)
            talk.Completable = p.Remaining >= 4 && (p.U32() & 0x3) == 0x3;
            talk.ProgressAt = _now;
            talk.SrvCompletable = talk.Completable; talk.SrvNpc = npc; talk.SrvAt = _now;   // out11_questfix
            // also the answer to CMSG_QUESTGIVER_QUERY_QUEST for "IsAutoComplete() || !GetQuestMethod()" quests (HandleQuestgiverQueryQuestOpcode)
            ulong sharer = (npc >> 48) == QHighPlayer && npc != _player.Guid ? npc : 0;   // iteration 11: an auto-complete push carries the SHARER's guid (QuestHandler.cpp:593-594)
            QAfterTalk(npc, id, QWaitProgress | QWaitDetails, QIconTurnIn, flags, talk.Title, "request items", sharer);
            // out11_questfix: completable (0x03) and no OFFER_REWARD from this NPC yet -> CMSG_QUESTGIVER_REQUEST_REWARD, what WoW's "Continue" sends
            // (QuestFrame.lua:107-110 QuestProgressCompleteButton_OnClick -> CompleteQuest()); AzerothCore completes the quest there when it can
            // (QuestHandler.cpp:371-372 "if (_player->CanCompleteQuest(questId)) _player->CompleteQuest(questId);") and answers OFFER_REWARD (:374-378).
            // Only for a quest of our log: for one outside it CompleteQuest would create a status the real client causes only by clicking.
            var m = _qMenu;
            if (talk.Completable && m != null && m.Npc == npc && QCanAskGiver(m) && QLogSlotOf(id) >= 0 && !m.RewardAsked.Contains(id) && talk.RewardAt < m.Started)
            {
                _world.Send(Op.CMSG_QUESTGIVER_REQUEST_REWARD, new PacketWriter().U64(m.Npc).U32((uint)id));
                m.RewardAsked.Add(id);
                QOwe(m, id);
                if (!m.Published) { int w; m.Waits.TryGetValue(id, out w); m.Waits[id] = w | QWaitReward; }
                bool bit = (_qSlots[QLogSlotOf(id) * QSlotFields + 1] & QStateComplete) != 0;
                WowLog.Info("Online quests: quest " + id + " '" + QTitle(id) + "' - SMSG_QUESTGIVER_REQUEST_ITEMS says completable (0x03)" + (bit ? "" : " while the log does not read complete")
                            + " -> CMSG_QUESTGIVER_REQUEST_REWARD (WoW's Continue)");
            }
        }

        /// <summary>out11_questfix: a giver we may ask ahead - not an item / a player / a shared quest (QPrefetch's rule).</summary>
        bool QCanAskGiver(QMenu m)
        {
            return m.Npc != 0 && (m.Npc >> 48) != 0x4000 && (m.Npc >> 48) != QHighPlayer && m.Sharer == 0;
        }

        /// <summary>out11_questfix: one more giver answer owed to our requests for this quest.</summary>
        static void QOwe(QMenu m, int questId)
        {
            int n;
            m.Owed.TryGetValue(questId, out n);
            m.Owed[questId] = n + 1;
        }

        /// <summary>
        /// SMSG_QUESTGIVER_OFFER_REWARD (PlayerMenu::SendQuestGiverOfferReward, GossipDef.cpp:667-762): uint64 npc, uint32 quest,
        /// string title, string reward text, uint8 enableNext, uint32 flags, uint32 suggested, uint32 emotes + n x (delay, emote),
        /// uint32 choices + n x (id, count, display) (RewardChoiceItemId[0..n-1]), uint32 items + n x (id, count, display), uint32 money,
        /// uint32 xp, honor, float, 0x08, spell, cast spell, title, talents, arena, 0, 5 factions, 5 values, 5 overrides.
        /// </summary>
        void OnQOfferReward(WowPacket p)
        {
            ulong npc = p.U64();
            int id = (int)p.U32();
            var talk = QTalkOf(id);
            talk.Title = p.CString();
            talk.Completion = p.CString();
            p.U8();
            uint flags = p.U32();
            p.U32();
            uint emotes = p.U32();
            for (uint i = 0; i < emotes && p.Remaining >= 8; i++) { p.U32(); p.U32(); }
            talk.Choices = QReadItemTriples(p);
            talk.Items = QReadItemTriples(p);
            talk.Money = Math.Max(0, p.I32());
            talk.Xp = (int)p.U32();
            if (p.Remaining >= 20) { p.U32(); p.F32(); p.U32(); talk.RewSpell = (int)p.U32(); talk.RewSpellCast = p.I32(); }   // GossipDef.cpp:729-733 (iteration 11)
            talk.Completable = true;
            talk.RewardAt = _now;
            talk.SrvCompletable = true; talk.SrvNpc = npc; talk.SrvAt = _now;   // out11_questfix: AzerothCore offers the reward only for a completable quest
            QAfterTalk(npc, id, QWaitProgress | QWaitReward | QWaitDetails, QIconTurnIn, flags, talk.Title, "offer reward", 0);
        }

        List<int[]> QReadItemTriples(WowPacket p)
        {
            var list = new List<int[]>();
            uint n = p.U32();
            for (uint i = 0; i < n && p.Remaining >= 12; i++) list.Add(new[] { (int)p.U32(), (int)p.U32(), (int)p.U32() });
            return list;
        }

        QTalk QTalkOf(int id)
        {
            QTalk t;
            if (!_qTalk.TryGetValue(id, out t)) { t = new QTalk(); _qTalk[id] = t; }
            return t;
        }

        /// <summary>A giver's text arrived: clear the wait, or open a conversation the server started for us (a single-quest NPC, the next quest in a chain, a quest item).</summary>
        void QAfterTalk(ulong npc, int questId, int clears, int icon, uint flags, string title, string what, ulong sharer)
        {
            QAskTemplate(questId);
            int page = what == "details" ? 1 : what == "request items" ? 2 : 3;
            var m = _qMenu;
            if (m != null && m.Npc == npc)
            {
                int w, owed;
                bool asked = m.Owed.TryGetValue(questId, out owed) && owed > 0;  // out11_questfix: an answer to one of OUR requests (counted, not a bit)
                if (asked) { if (owed == 1) m.Owed.Remove(questId); else m.Owed[questId] = owed - 1; }
                if (m.Waits.TryGetValue(questId, out w)) { w &= ~clears; if (w == 0) m.Waits.Remove(questId); else m.Waits[questId] = w; }
                if (QMenuQuestOf(m, questId) == null) m.Quests.Add(new QMenuQuest { Id = questId, Icon = icon, Level = 0, Flags = flags, Title = title });
                if (sharer != 0) m.Sharer = sharer;
                // iteration 11: a page we did not ask for is the server opening it (the next quest of a chain after CHOOSE_REWARD,
                // HandleQuestgiverChooseRewardOpcode -> SendQuestGiverQuestDetails; another share) - the real client shows it at once
                if (!asked && m.Published) QOpenPage(m, questId, page);
                _qRewarded.Remove(questId);
                _qDialogDirty = true;
                return;
            }
            bool itemGuid = (npc >> 48) == 0x4000;                                // HighGuid::Item: a quest started from an item
            if (!QWantsMenu(npc) && !itemGuid && sharer == 0) { WowLog.Info("Online quests: " + what + " for quest " + questId + " from " + npc.ToString("X16") + " kept (no dialog with it)"); return; }
            var nm = new QMenu { Npc = npc, Greeting = "", Source = sharer != 0 ? "shared by " + (ChatSender(sharer) ?? sharer.ToString("X16")) : what, Sharer = sharer };
            nm.Quests.Add(new QMenuQuest { Id = questId, Icon = icon, Level = 0, Flags = flags, Title = title });
            QStartMenu(nm);
            QOpenPage(nm, questId, page);                                         // iteration 11: the conversation opens on the page the server sent
            int w2;
            if (nm.Waits.TryGetValue(questId, out w2)) { w2 &= ~clears; if (w2 == 0) nm.Waits.Remove(questId); else nm.Waits[questId] = w2; }
        }

        /// <summary>Iteration 11: npc flags = alive for AzerothCore (Creature.cpp:1988 clears a dead creature's) - a corpse-looking NPC.</summary>
        bool QNpcFlagsSet(ulong guid)
        {
            var o = FindObject(guid);
            return o != null && o.TypeId == TypeId.Unit && o.U32(UF.UNIT_NPC_FLAGS) != 0;
        }

        void QOpenPage(QMenu m, int questId, int page)
        {
            m.OpenQuest = questId;
            m.OpenPage = page;
            m.OpenSerial = ++_qOpenSerial;
            _qDialogDirty = true;
        }

        bool QWantsMenu(ulong npc)
        {
            if (npc == 0) return false;
            if (_qMenu != null && _qMenu.Npc == npc) return true;
            return npc == _qHelloGuid && _now - _qHelloAt <= QHelloTimeout;
        }

        /// <summary>A new conversation: ask for what it still lacks (templates, the giver's texts), publish when complete or at the deadline.</summary>
        void QStartMenu(QMenu m)
        {
            bool reopen = _qMenu != null && _qMenu.Npc == m.Npc && _qMenu.Published;
            if (_dialog != null && _dialog.NpcGuid != m.Npc) _dialog = null;       // another NPC answered: the old conversation ends now
            _qMenu = m;
            _qHelloGuid = 0;
            m.Started = _now;
            m.Deadline = _now + QDialogWaitSeconds;
            foreach (var q in m.Quests)
            {
                QAskTemplate(q.Id);
                QPrefetch(m, q);
            }
            if (reopen) QPublish(m);
        }

        /// <summary>
        /// Asks the giver what it says about a quest, the packets the real client sends when the quest (or "Continue") is clicked:
        /// on offer -> CMSG_QUESTGIVER_QUERY_QUEST (guid, uint32 quest, uint8 unk; HandleQuestgiverQueryQuestOpcode only answers -
        /// except for auto-accept quests, which are never asked here); to hand in -> CMSG_QUESTGIVER_COMPLETE_QUEST (guid, uint32 quest;
        /// HandleQuestgiverCompleteQuest answers REQUEST_ITEMS or OFFER_REWARD) and, when complete in the log,
        /// CMSG_QUESTGIVER_REQUEST_REWARD (guid, uint32 quest; HandleQuestgiverRequestRewardOpcode answers OFFER_REWARD).
        /// </summary>
        void QPrefetch(QMenu m, QMenuQuest q)
        {
            // an item / a player cannot be asked (TYPEMASK_UNIT | GAMEOBJECT): a CMSG_QUESTGIVER_QUERY_QUEST to a player answers SendCloseGossip
            // (QuestHandler.cpp:211-215) and would close a shared quest (iteration 11)
            if (!QCanAskGiver(m)) return;
            QTalk t;
            _qTalk.TryGetValue(q.Id, out t);
            var st = QStateInMenu(q, m);
            int wait = 0;
            if (st == QuestState.Available)
            {
                if ((q.Flags & QFlagAutoAccept) != 0) return;
                if (t != null && _now - t.DetailsAt < 2.0) return;
                _world.Send(Op.CMSG_QUESTGIVER_QUERY_QUEST, new PacketWriter().U64(m.Npc).U32((uint)q.Id).U8(0));
                QOwe(m, q.Id);                                                    // out11_questfix
                wait = QWaitDetails;
            }
            else
            {
                bool inLog = QLogSlotOf(q.Id) >= 0;
                if (t == null || _now - t.ProgressAt >= 2.0 && _now - t.RewardAt >= 2.0)
                {
                    _world.Send(Op.CMSG_QUESTGIVER_COMPLETE_QUEST, new PacketWriter().U64(m.Npc).U32((uint)q.Id));
                    QOwe(m, q.Id);
                    wait |= QWaitProgress;
                }
                if (st == QuestState.Complete && inLog && (t == null || _now - t.RewardAt >= 2.0))
                {
                    _world.Send(Op.CMSG_QUESTGIVER_REQUEST_REWARD, new PacketWriter().U64(m.Npc).U32((uint)q.Id));
                    QOwe(m, q.Id);
                    m.RewardAsked.Add(q.Id);
                    wait |= QWaitReward;
                }
            }
            if (wait != 0) m.Waits[q.Id] = wait;
        }

        bool QMenuReady(QMenu m)
        {
            if (m.Greeting == null) return false;
            if (m.Waits.Count > 0) return false;
            bool ready = true;
            foreach (var q in m.Quests)
            {
                QTemplate t;
                if (!_qTemplates.TryGetValue(q.Id, out t)) { ready = false; continue; }
                if (!QNamesKnown(q.Id, t)) ready = false;
            }
            return ready;
        }

        void QPublish(QMenu m)
        {
            m.Published = true;
            _dialog = QBuildDialog(m);
            _qDialogDirty = false;
            _qDialogs++;
            var sb = new StringBuilder();
            sb.Append("Online quests: dialog with ").Append(_dialog.NpcName).Append(" (").Append(m.Source).Append(", ").Append(m.Npc.ToString("X16")).Append(") - ");
            sb.Append(_dialog.Quests.Count).Append(" quests");
            foreach (var q in _dialog.Quests) sb.Append(" [").Append(q.Id).Append(' ').Append(q.State).Append(" '").Append(q.Title).Append("']");
            sb.Append(", greeting ").Append(_dialog.Greeting != null ? _dialog.Greeting.Length : 0).Append(" chars, ").Append(m.Options.Count).Append(" gossip options, after ")
              .Append(((int)((_now - m.Started) * 1000)).ToString()).Append(" ms");
            if (!QMenuReady(m))
            {
                sb.Append(" (timed out waiting for");
                if (m.Greeting == null) sb.Append(" the NPC text");
                foreach (var kv in m.Waits) sb.Append(" quest ").Append(kv.Key).Append(" giver text");
                foreach (var q in m.Quests) if (!_qTemplates.ContainsKey(q.Id)) sb.Append(" template ").Append(q.Id);
                sb.Append(')');
            }
            WowLog.Info(sb.ToString());
        }

        /// <summary>NpcDialog as the offline game fills it: the quests the NPC takes back (complete, then in progress), then the ones it offers.</summary>
        NpcDialog QBuildDialog(QMenu m)
        {
            var d = new NpcDialog();
            d.NpcGuid = m.Npc;
            var e = Find(m.Npc);
            d.NpcName = m.Sharer != 0 ? (ChatSender(m.Sharer) ?? "Someone") : (e != null && e.Name != null ? e.Name : QItemNameOrEmpty(m.Npc));   // "Someone": ours (Death.cs:414)
            d.OpenQuestId = m.OpenQuest; d.OpenPage = m.OpenPage; d.OpenSerial = m.OpenSerial; d.SharedByGuid = m.Sharer;   // iteration 11
            d.Greeting = QPersonal(m.Greeting ?? "");
            // iteration 11 (npc H3): the gossip options are buttons now (UiQuestDialog; CMSG_GOSSIP_SELECT_OPTION through SelectGossipOption)
            d.MenuId = m.MenuId;
            d.FromObject = (m.Npc >> 48) == 0xF110 || (m.Npc >> 48) == 0xF120;   // HighGuid GameObject / Transport (ObjectGuid.h:65-66)
            foreach (var o in m.GossipOptions)
                d.Options.Add(new GossipOptionView { Index = o.Index, Icon = o.Icon, Text = QPersonal(o.Text ?? ""), Coded = o.Coded, BoxMoney = o.BoxMoney, BoxText = QPersonal(o.BoxText ?? "") });
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var q in m.Quests)
                {
                    if (_qRewarded.Contains(q.Id) && QLogSlotOf(q.Id) < 0) continue;
                    var st = QStateInMenu(q, m);
                    int want = st == QuestState.Complete ? 0 : st == QuestState.Active ? 1 : 2;
                    if (want != pass) continue;
                    var v = QBuildView(q.Id, st, QLogSlotOf(q.Id), q, true);
                    if (st == QuestState.Available) v.GiverGuid = m.Npc; else v.EnderGuid = m.Npc;
                    d.Quests.Add(v);
                }
            }
            return d;
        }

        QMenuQuest QMenuQuestOf(QMenu m, int id)
        {
            for (int i = 0; i < m.Quests.Count; i++) if (m.Quests[i].Id == id) return m.Quests[i];
            return null;
        }

        /// <summary>
        /// In the log: its state bit - or (out11_questfix) what THIS giver said in THIS conversation: an OFFER_REWARD or a REQUEST_ITEMS with the
        /// completable flag 0x03 make it "to hand in" even while the log does not read complete. WoW's quest frame never looks at the log here:
        /// GossipFrame.lua:113-139 only picks the icon from it (ActiveQuestIcon / IncompleteQuestIcon) and every active title calls
        /// SelectGossipActiveQuest; the progress panel's Continue is IsQuestCompletable() (QuestFrame.lua:135-139) = the server's flag.
        /// Without a log entry: icon 4 / 0 = an auto-complete quest (PrepareQuestMenu); else on offer.
        /// </summary>
        QuestState QStateInMenu(QMenuQuest q, QMenu m)
        {
            string basis;
            return QStateInMenu(q, m, out basis);
        }

        QuestState QStateInMenu(QMenuQuest q, QMenu m, out string basis)
        {
            int slot = QLogSlotOf(q.Id);
            QTalk t;
            _qTalk.TryGetValue(q.Id, out t);
            if (slot >= 0)
            {
                if ((_qSlots[slot * QSlotFields + 1] & QStateComplete) != 0) { basis = "the log reads complete (QUEST_STATE_COMPLETE)"; return QuestState.Complete; }
                bool heard = t != null && m != null && t.SrvNpc == m.Npc && t.SrvAt >= m.Started;
                if (heard && t.SrvCompletable)
                {
                    basis = "the server said completable (" + (t.RewardAt >= t.ProgressAt ? "SMSG_QUESTGIVER_OFFER_REWARD" : "SMSG_QUESTGIVER_REQUEST_ITEMS 0x03") + "), the log does not read complete";
                    return QuestState.Complete;
                }
                basis = heard ? "the log and the server's SMSG_QUESTGIVER_REQUEST_ITEMS both say not complete" : "the log does not read complete and this giver has not said otherwise";
                return QuestState.Active;
            }
            if (q.Icon == QIconTurnIn || q.Icon == QIconAutoComplete || (q.Flags & QFlagAutoComplete) != 0)
            {
                if (t != null && t.ProgressAt > t.RewardAt && !t.Completable) { basis = "not in the log, the giver's REQUEST_ITEMS says not completable"; return QuestState.Active; }
                basis = "not in the log, auto-complete (menu icon " + q.Icon + ")";
                return QuestState.Complete;
            }
            basis = "on offer";
            return QuestState.Available;
        }

        void QAutoClose(QMenu m)
        {
            if ((m.Npc >> 48) == 0x4000 || (m.Npc >> 48) == 0) return;           // an item / a player sharing a quest: nothing to walk away from
            var e = Find(m.Npc);
            string why = null;
            if (e == null) why = "is gone";
            else if (e.Dead && e.Kind != EntityKind.GameObject && !QNpcFlagsSet(m.Npc)) why = "died";   // iteration 11: a corpse-looking NPC (npc flags) stays
            else if (_player.Dead) why = "- the player died";
            else
            {
                float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z;
                if (dx * dx + dy * dy + dz * dz > QDialogCloseYards * QDialogCloseYards) why = "is more than " + QDialogCloseYards + " yd away";
            }
            if (why == null) return;
            WowLog.Info("Online quests: dialog closed - " + (e != null ? e.Name : m.Npc.ToString("X16")) + " " + why);
            CloseDialog();
        }

        // ================================================================== server packets: results

        /// <summary>SMSG_QUESTGIVER_QUEST_COMPLETE (QuestPackets.cpp:27-37 QuestGiverQuestComplete): uint32 quest, xp, money, honor, talents, arena.</summary>
        void OnQQuestComplete(WowPacket p)
        {
            int id = (int)p.U32();
            int xp = (int)p.U32(), money = (int)p.U32();
            _qRewarded.Add(id);
            QEmit(GameEventType.QuestRewarded, id, () => QTitle(id) + " completed.");                         // ERR_QUEST_COMPLETE_S (GlobalStrings.lua:3413)
            if (money > 0)
            {
                var e = new GameEvent { Type = GameEventType.MoneyReceived, TargetGuid = _player.Guid, Amount = money };
                _qEvents.Add(new QPending { E = e, Text = () => QMoneyText(money), Until = _now });
            }
            _qDialogDirty = true;
            ScheduleQuestQuery(0.5);
            WowLog.Info("Online quests: quest " + id + " '" + QTitle(id) + "' rewarded: " + xp + " xp, " + money + " copper (SMSG_QUESTGIVER_QUEST_COMPLETE)");
        }

        /// <summary>SMSG_QUESTGIVER_QUEST_INVALID (QuestPackets.cpp QuestGiverQuestInvalid: uint32 QuestFailedReason) - WoW's text per reason (QuestDef.h:46-61).</summary>
        void OnQInvalid(WowPacket p)
        {
            uint reason = p.U32();
            string text;
            switch (reason)
            {
                case 1: text = "You are not high enough level for that quest."; break;             // ERR_QUEST_FAILED_LOW_LEVEL (GlobalStrings.lua:3417)
                case 6: text = "That quest is not available to your race."; break;                  // ERR_QUEST_FAILED_WRONG_RACE (3423)
                case 7: text = "You have completed that quest."; break;                             // ERR_QUEST_ALREADY_DONE (3410)
                case 12: text = "You can only be on one timed quest at a time"; break;              // ERR_QUEST_ONLY_ONE_TIMED (3429)
                case 13: case 18: text = "You are already on that quest"; break;                    // ERR_QUEST_ALREADY_ON (3412)
                case 16: text = "This quest requires an expansion enabled account."; break;         // ERR_QUEST_FAILED_EXPANSION (3416)
                case 21: text = "You don't have the required items with you.  Check storage."; break;   // ERR_QUEST_FAILED_MISSING_ITEMS (3419)
                case 23: text = "You don't have enough money for that quest"; break;                // ERR_QUEST_FAILED_NOT_ENOUGH_MONEY (3420)
                case 26: text = "You have already completed 25 daily quests today"; break;          // ERR_QUEST_FAILED_TOO_MANY_DAILY_QUESTS_I (3422), 25 = QuestDef.h:58
                case 27: text = "You cannot complete quests once you have reached tired time"; break;   // ERR_QUEST_FAILED_CAIS (3415)
                case 29: text = "You have completed that daily quest today."; break;                // ERR_QUEST_ALREADY_DONE_DAILY (3411)
                default: text = "You don't meet the requirements for that quest"; break;            // ERR_QUEST_NEED_PREREQS (3427): INVALIDREASON_DONT_HAVE_REQ
            }
            Error(text);
            WowLog.Info("Online quests: SMSG_QUESTGIVER_QUEST_INVALID reason " + reason + " - " + text);
        }

        /// <summary>SMSG_QUESTGIVER_QUEST_FAILED (QuestPackets.cpp QuestGiverQuestFailed: uint32 quest, uint32 InventoryResult; Player::FailQuest sends 0).</summary>
        void OnQGiverFailed(WowPacket p)
        {
            int id = (int)p.U32();
            uint reason = p.Remaining >= 4 ? p.U32() : 0;
            if (reason == 50 || reason == 4) QEmit(GameEventType.Info, id, () => QTitle(id) + " failed: Inventory is full.");        // EQUIP_ERR_INVENTORY_FULL / BAG_FULL (Item.h) -> ERR_QUEST_FAILED_BAG_FULL_S (3414)
            else if (reason == 17) QEmit(GameEventType.Info, id, () => QTitle(id) + " failed: Duplicate item found.");           // EQUIP_ERR_CANT_CARRY_MORE_OF_THIS -> ERR_QUEST_FAILED_MAX_COUNT_S (3418)
            else QEmit(GameEventType.Info, id, () => QTitle(id) + " failed.");                                                    // ERR_QUEST_FAILED_S (3421)
            _qLogDirty = true;
            WowLog.Info("Online quests: quest " + id + " failed (reason " + reason + ")");
        }

        /// <summary>SMSG_QUESTUPDATE_FAILEDTIMER (QuestPackets.cpp QuestUpdateFailedTimer: uint32 quest).</summary>
        void OnQFailedTimer(WowPacket p)
        {
            int id = (int)p.U32();
            QEmit(GameEventType.Info, id, () => QTitle(id) + " failed.");                                                          // ERR_QUEST_FAILED_S (3421)
            _qLogDirty = true;
        }

        /// <summary>SMSG_QUESTLOG_FULL (QuestPackets.h QuestLogFull, empty; Player::SatisfyQuestLog).</summary>
        void OnQLogFull(WowPacket p) { Error("Your quest log is full."); }                                                      // ERR_QUEST_LOG_FULL (GlobalStrings.lua:3425)

        /// <summary>SMSG_QUESTUPDATE_ADD_KILL (QuestPackets.cpp QuestUpdateAddKill: uint32 quest, uint32 entry (gameobject | 0x80000000), uint32 count, uint32 required, uint64 guid).</summary>
        void OnQAddKill(WowPacket p)
        {
            int id = (int)p.U32();
            uint entry = p.U32();
            int count = (int)p.U32(), need = (int)p.U32();
            QEmit(GameEventType.QuestProgress, id, () => QKillLine(id, entry, count, need));
            _qLogDirty = true;
        }

        /// <summary>SMSG_QUESTUPDATE_ADD_PVP_KILL (QuestPackets.cpp QuestUpdateAddPvPKill: uint32 quest, uint32 count, uint32 required).</summary>
        void OnQAddPvpKill(WowPacket p)
        {
            int id = (int)p.U32();
            int count = (int)p.U32(), need = (int)p.U32();
            QEmit(GameEventType.QuestProgress, id, () => "Players slain: " + count + "/" + need);                                // ERR_QUEST_ADD_PLAYER_KILL_SII (3409)
            _qLogDirty = true;
        }

        /// <summary>SMSG_QUESTUPDATE_ADD_ITEM: AC sends it EMPTY (PlayerQuest.cpp:2517 "not required by the 3.3.5 client") - item objectives count the bags.</summary>
        void OnQAddItem(WowPacket p) { _qBagTimer = 0f; }

        /// <summary>SMSG_QUESTUPDATE_COMPLETE (QuestUpdateComplete: uint32 quest) - only for an explored area / scripted event (AreaExploredOrEventHappens).</summary>
        void OnQUpdateComplete(WowPacket p)
        {
            int id = (int)p.U32();
            QEmit(GameEventType.QuestProgress, id, () => "Objective Complete.");                                                  // ERR_QUEST_UNKNOWN_COMPLETE (3446)
            _qLogDirty = true;
        }

        string QKillLine(int questId, uint entry, int count, int need)
        {
            QTemplate t;
            if (_qTemplates.TryGetValue(questId, out t))
                for (int i = 0; i < 4; i++)
                    if ((uint)t.ReqNpcOrGo[i] == entry && !string.IsNullOrEmpty(t.ObjectiveText[i])) return t.ObjectiveText[i] + ": " + count + "/" + need;   // ERR_QUEST_ADD_FOUND_SII "%s: %d/%d"
            if ((entry & 0x80000000u) != 0) return QGoName((int)(entry & 0x7FFFFFFF)) + ": " + count + "/" + need;                  // ERR_QUEST_ADD_FOUND_SII (3406)
            return QCreatureName((int)entry) + " slain: " + count + "/" + need;                                                    // ERR_QUEST_ADD_KILL_SII (3408)
        }

        // ================================================================== templates and names

        void QAskTemplate(int id)
        {
            if (id <= 0 || _qTemplates.ContainsKey(id) || !_qAsked.Add(id)) return;
            _world.Send(Op.CMSG_QUEST_QUERY, new PacketWriter().U32((uint)id));  // HandleQuestQueryOpcode: "recvData >> questId"
        }

        void QAskNpcText(int textId, ulong npc)
        {
            if (!_qTextAsked.Add(textId)) return;
            _world.Send(Op.CMSG_NPC_TEXT_QUERY, new PacketWriter().U32((uint)textId).U64(npc));   // HandleNpcTextQueryOpcode: uint32 textID, ObjectGuid
        }

        /// <summary>
        /// SMSG_QUEST_QUERY_RESPONSE (PlayerMenu::SendQuestQueryResponse, GossipDef.cpp:505-639): uint32 id, method, level, min level,
        /// zone or sort, type, suggested, rep objective faction, value, faction 2, value 2, next in chain, xp id, money, money at max
        /// level, spell, cast spell, honor, float honor multiplier, source item, flags (&amp; 0xFFFF), title id, players slain, talents,
        /// arena, 0; 4 x (reward item, count); 6 x (choice item, count); 5 factions, 5 values, 5 overrides; uint32 poi map, float x,
        /// float y, uint32 poi opt; title, objectives, details, area description, completed text; 4 x (npc or go (go | 0x80000000),
        /// count, item drop, 0); 6 x (item, count); 4 x objective text.
        /// </summary>
        void OnQQueryResponse(WowPacket p)
        {
            var t = new QTemplate();
            t.Id = (int)p.U32();
            t.Method = (int)p.U32();
            t.Level = p.I32();
            t.MinLevel = (int)p.U32();
            t.ZoneOrSort = p.I32();                                              // zone or sort (13.7: the quest log's header)
            t.Type = (int)p.U32();
            p.U32(); p.U32(); p.U32(); p.U32(); p.U32();                         // suggested, rep objective faction/value x2
            t.NextInChain = (int)p.U32();
            t.XpId = (int)p.U32();
            t.RewardMoney = p.I32();                                             // negative = money the quest takes (GetRewOrReqMoney)
            p.U32(); p.U32(); p.U32(); p.U32(); p.F32();                         // money at max level, spell, cast spell, honor, multiplier
            t.SrcItem = (int)p.U32();
            t.Flags = p.U32();
            p.U32();
            t.PlayersSlain = (int)p.U32();
            p.U32(); p.U32(); p.U32();                                           // talents, arena, rep show mask
            for (int i = 0; i < 4; i++) { t.RewItem[i] = (int)p.U32(); t.RewItemCount[i] = (int)p.U32(); }
            for (int i = 0; i < 6; i++) { t.RewChoice[i] = (int)p.U32(); t.RewChoiceCount[i] = (int)p.U32(); }
            for (int i = 0; i < 15; i++) p.U32();                                // 5 factions, 5 values, 5 overrides
            p.U32(); p.F32(); p.F32(); p.U32();                                  // poi
            t.Title = p.CString();
            t.Objectives = p.CString();
            t.Details = p.CString();
            t.AreaDescription = p.CString();
            t.CompletedText = p.CString();
            for (int i = 0; i < 4; i++) { t.ReqNpcOrGo[i] = (int)p.U32(); t.ReqNpcOrGoCount[i] = (int)p.U32(); p.U32(); p.U32(); }
            for (int i = 0; i < 6; i++) { t.ReqItem[i] = (int)p.U32(); t.ReqItemCount[i] = (int)p.U32(); }
            for (int i = 0; i < 4; i++) t.ObjectiveText[i] = p.Remaining > 0 ? p.CString() : "";
            _qTemplates[t.Id] = t;
            _qTemplatesIn++;
            QNamesKnown(t.Id, t);                                                // ask for the objective / reward names now
            _qLogDirty = true;
            _qDialogDirty = true;
        }

        /// <summary>SMSG_ITEM_QUERY_SINGLE_RESPONSE (ItemHandler.cpp HandleItemQuerySingleOpcode:413-431): uint32 entry (| 0x80000000 unknown), class, subclass, int32 sound, name, 3 x uint8 0, display, quality, flags, flags2, int32 buy, sell, inventory type ...</summary>
        void OnQItemQuery(WowPacket p)
        {
            uint raw = p.U32();
            int entry = (int)(raw & 0x7FFFFFFF);
            var ii = new QItemInfo();
            if ((raw & 0x80000000u) != 0 || p.Remaining < 12) ii.Missing = true;
            else
            {
                ii.Class = (int)p.U32();
                p.U32(); p.U32();
                ii.Name = p.CString();
                p.U8(); p.U8(); p.U8();
                ii.Display = (int)p.U32();
                ii.Quality = (int)p.U32();
                p.U32(); p.U32(); p.U32(); p.U32();
                ii.InventoryType = (int)p.U32();
            }
            _qItems[entry] = ii;
            if (_qItemAsked.Contains(entry) || QItemMatters(entry)) { _qLogDirty = true; _qDialogDirty = true; }
        }

        /// <summary>SMSG_GAMEOBJECT_QUERY_RESPONSE (QueryHandler.cpp:193-213): uint32 entry, type, display, name, 3 x uint8, icon, cast bar caption, unk1, 24 x uint32 data, float size, 6 quest items.</summary>
        void OnQGameObjectQuery(WowPacket p)
        {
            uint raw = p.U32();
            if ((raw & 0x80000000u) != 0 || p.Remaining < 8) return;
            var d = new uint[25];
            d[0] = p.U32();
            p.U32();
            p.CString(); p.U8(); p.U8(); p.U8(); p.CString(); p.CString(); p.CString();
            for (int i = 0; i < 24 && p.Remaining >= 4; i++) d[1 + i] = p.U32();
            _qGoData[(int)raw] = d;
            CurGoDataArrived((int)raw);                                          // lead fix 2026-10-03: the cursor of these objects (OnlineSession.Cursor.cs)
            // objective names - out11_questfix: only for a gameobject some quest names (RequiredNpcOrGo = entry | 0x80000000); every other answer
            // (each new chair, door, mailbox in sight) rebuilt the log and the open dialog's views
            if (QGoMatters((int)raw)) _qLogDirty = true;
        }

        bool QGoMatters(int entry)
        {
            int raw = entry | unchecked((int)0x80000000);
            foreach (var t in _qTemplates.Values) for (int i = 0; i < 4; i++) if (t.ReqNpcOrGo[i] == raw) return true;
            return false;
        }

        bool QItemMatters(int entry)
        {
            foreach (var t in _qTemplates.Values)
            {
                if (t.SrcItem == entry) return true;                              // quest map fix: the tracker's item button (QFindUseItem) needs its proto
                for (int i = 0; i < 6; i++) if (t.ReqItem[i] == entry || t.RewChoice[i] == entry) return true;
                for (int i = 0; i < 4; i++) if (t.RewItem[i] == entry) return true;
            }
            return false;
        }

        /// <summary>True when every name a quest's views show is known; asks for the missing ones (items, creatures, gameobjects).</summary>
        bool QNamesKnown(int questId, QTemplate t)
        {
            bool ok = true;
            for (int i = 0; i < 4; i++)
            {
                int raw = t.ReqNpcOrGo[i];
                if (raw == 0 || !string.IsNullOrEmpty(t.ObjectiveText[i])) continue;
                if ((raw & unchecked((int)0x80000000)) != 0) { if (!QAskGo(raw & 0x7FFFFFFF)) ok = false; }
                else if (!QAskCreature(raw)) ok = false;
            }
            for (int i = 0; i < 6; i++) if (t.ReqItem[i] > 0 && !QAskItem(t.ReqItem[i])) ok = false;
            for (int i = 0; i < 6; i++) if (t.RewChoice[i] > 0 && !QAskItem(t.RewChoice[i])) ok = false;
            for (int i = 0; i < 4; i++) if (t.RewItem[i] > 0 && !QAskItem(t.RewItem[i])) ok = false;
            QTalk k;
            if (_qTalk.TryGetValue(questId, out k))
            {
                foreach (var l in new[] { k.Choices, k.Items, k.DetailsChoices, k.DetailsItems })
                    if (l != null) foreach (var it in l) if (it[0] > 0 && !QAskItem(it[0])) ok = false;
            }
            return ok;
        }

        /// <summary>CMSG_ITEM_QUERY_SINGLE (ItemHandler.cpp:389: "uint32 item; recvData >> item"). True = known.</summary>
        bool QAskItem(int entry)
        {
            if (_qItems.ContainsKey(entry)) return true;
            if (_qItemAsked.Add(entry)) _world.Send(Op.CMSG_ITEM_QUERY_SINGLE, new PacketWriter().U32((uint)entry));
            return false;
        }

        /// <summary>CMSG_CREATURE_QUERY (QueryHandler: uint32 entry, ObjectGuid) through the object manager's cache (Objects.cs). True = known.</summary>
        bool QAskCreature(int entry)
        {
            if (_creatureInfo.ContainsKey(entry)) return true;
            if (_creatureAsked.Add(entry)) _world.Send(Op.CMSG_CREATURE_QUERY, new PacketWriter().U32((uint)entry).U64(0));
            return false;
        }

        /// <summary>CMSG_GAMEOBJECT_QUERY (uint32 entry, ObjectGuid) through Objects.cs's name cache. True = known.</summary>
        bool QAskGo(int entry)
        {
            if (_goNames.ContainsKey(entry)) return true;
            if (_goAsked.Add(entry)) _world.Send(Op.CMSG_GAMEOBJECT_QUERY, new PacketWriter().U32((uint)entry).U64(0));
            return false;
        }

        string QCreatureName(int entry)
        {
            CreatureInfo ci;
            if (_creatureInfo.TryGetValue(entry, out ci) && ci != null && !ci.Missing && !string.IsNullOrEmpty(ci.Name)) return ci.Name;
            QAskCreature(entry);
            return "Creature " + entry;                                          // ours, until the server's name arrives
        }

        string QGoName(int entry)
        {
            string n;
            if (_goNames.TryGetValue(entry, out n) && !string.IsNullOrEmpty(n)) return n;
            QAskGo(entry);
            return "Object " + entry;                                            // ours, until the server's name arrives
        }

        string QItemName(int entry)
        {
            QItemInfo ii;
            if (_qItems.TryGetValue(entry, out ii) && !ii.Missing && !string.IsNullOrEmpty(ii.Name)) return ii.Name;
            QAskItem(entry);
            return "Item " + entry;                                              // ours, until the server's name arrives
        }

        string QItemNameOrEmpty(ulong itemGuid)
        {
            var o = FindObject(itemGuid);
            if (o == null) return "";
            QItemInfo ii;
            return _qItems.TryGetValue(o.Entry, out ii) && !ii.Missing ? ii.Name : "";
        }

        string QTitle(int id)
        {
            QTalk k;
            if (_qTalk.TryGetValue(id, out k) && !string.IsNullOrEmpty(k.Title)) return k.Title;
            QTemplate t;
            if (_qTemplates.TryGetValue(id, out t) && !string.IsNullOrEmpty(t.Title)) return t.Title;
            if (_qMenu != null) { var mq = QMenuQuestOf(_qMenu, id); if (mq != null && !string.IsNullOrEmpty(mq.Title)) return mq.Title; }
            return "Quest " + id;                                                // ours, until the template arrives
        }

        // ================================================================== views

        /// <summary>
        /// A QuestView the way OfflineSession.BuildQuestView fills it. Texts: what the giver said (details / request items / offer
        /// reward) over the template; rewards: OFFER_REWARD (to hand in) or QUEST_DETAILS (on offer) over the template; objectives:
        /// the template's creature / gameobject / item / player-kill objectives with the log's counters and the bag counts.
        /// </summary>
        QuestView QBuildView(int id, QuestState state, int slot, QMenuQuest mq, bool forDialog)
        {
            QTemplate t;
            _qTemplates.TryGetValue(id, out t);
            QTalk k;
            _qTalk.TryGetValue(id, out k);
            var v = new QuestView();
            v.Id = id;
            v.Title = QTitle(id);
            int level = mq != null && mq.Level > 0 ? mq.Level : t != null ? t.Level : 0;
            v.Level = level > 0 ? level : _player.Level;                         // "may be -1 ... Player::GetQuestLevel" (GossipDef.cpp:512)
            v.State = state;
            v.ZoneOrSort = t != null ? t.ZoneOrSort : 0;                         // 13.7
            v.Description = QPersonal(k != null && !string.IsNullOrEmpty(k.Details) ? k.Details : t != null ? t.Details : "");
            v.ObjectivesText = QPersonal(k != null && !string.IsNullOrEmpty(k.Objectives) ? k.Objectives : t != null ? t.Objectives : "");
            v.ProgressText = QPersonal(k != null ? k.Progress : "");
            v.CompletionText = QPersonal(k != null ? k.Completion : "");
            if (t != null) QObjectives(v, t, slot, state);
            QFillExtras(v, t, k, slot, mq);                                       // iteration 11 (OnlineSession.QuestExtras.cs)
            // rewards
            List<int[]> choices = null, items = null;
            bool fromOffer = false;
            if (k != null && k.RewardAt >= 0 && k.Choices != null && (state != QuestState.Available || k.DetailsChoices == null))
            { choices = k.Choices; items = k.Items; v.RewardXp = Math.Max(0, k.Xp); v.RewardMoney = Math.Max(0, k.Money); fromOffer = true; }
            else if (k != null && k.DetailsChoices != null)
            { choices = k.DetailsChoices; items = k.DetailsItems; v.RewardXp = Math.Max(0, k.DetailsXp); v.RewardMoney = Math.Max(0, k.DetailsMoney); }
            var slots = new List<int>();
            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    v.RewardChoices.Add(QItemView(choices[i][0], choices[i][1], choices[i][2]));
                    slots.Add(fromOffer ? i : QChoiceSlotOf(t, choices[i][0], i));   // OFFER_REWARD writes RewardChoiceItemId[0..n-1]; DETAILS skips the empty ones
                }
                if (items != null) foreach (var it in items) v.RewardItems.Add(QItemView(it[0], it[1], it[2]));
            }
            else if (t != null && (t.Flags & QFlagHiddenRewards) == 0)
            {
                for (int i = 0; i < 6; i++) if (t.RewChoice[i] > 0) { v.RewardChoices.Add(QItemView(t.RewChoice[i], t.RewChoiceCount[i], 0)); slots.Add(i); }
                for (int i = 0; i < 4; i++) if (t.RewItem[i] > 0) v.RewardItems.Add(QItemView(t.RewItem[i], t.RewItemCount[i], 0));
                v.RewardMoney = Math.Max(0, t.RewardMoney);
                v.RewardXp = QXpValue(t);                                   // no giver's figure yet: the client's own (QuestXP.dbc)
            }
            if (forDialog || !_qChoiceSlots.ContainsKey(id)) _qChoiceSlots[id] = slots.ToArray();
            return v;
        }

        static int QChoiceSlotOf(QTemplate t, int entry, int fallback)
        {
            if (t != null) for (int i = 0; i < 6; i++) if (t.RewChoice[i] == entry) return i;
            return fallback;
        }

        void QObjectives(QuestView v, QTemplate t, int slot, QuestState state)
        {
            ulong counters = slot >= 0 ? (_qSlots[slot * QSlotFields + 2] | (ulong)_qSlots[slot * QSlotFields + 3] << 32) : 0;   // GetQuestSlotCounter: uint64 >> (counter * 16)
            for (int i = 0; i < 4; i++)
            {
                int raw = t.ReqNpcOrGo[i], need = t.ReqNpcOrGoCount[i];
                if (raw == 0 || need <= 0) continue;
                string text = !string.IsNullOrEmpty(t.ObjectiveText[i]) ? t.ObjectiveText[i]
                    : (raw & unchecked((int)0x80000000)) != 0 ? QGoName(raw & 0x7FFFFFFF) : QCreatureName(raw) + " slain";   // QUEST_MONSTERS_KILLED "%s slain: %d/%d"
                int have = (int)((counters >> (i * 16)) & 0xFFFF);
                if (state == QuestState.Complete && slot >= 0) have = Math.Max(have, need);
                v.Objectives.Add(new QuestObjectiveView { Text = text, Have = Math.Min(have, need), Need = need });
            }
            for (int i = 0; i < 6; i++)
            {
                int item = t.ReqItem[i], need = t.ReqItemCount[i];
                if (item <= 0 || need <= 0) continue;
                v.Objectives.Add(new QuestObjectiveView { Text = QItemName(item), Have = Math.Min(QBagCount(item), need), Need = need });   // QUEST_ITEMS_NEEDED "%s: %d/%d"
            }
            if (t.PlayersSlain > 0)
            {
                int have = (int)(counters & 0xFFFF);                              // QUEST_PVP_KILL_SLOT 0 (QuestDef.h:43)
                v.Objectives.Add(new QuestObjectiveView { Text = "Players slain", Have = Math.Min(have, t.PlayersSlain), Need = t.PlayersSlain });   // QUEST_PLAYERS_KILLED
            }
        }

        ItemView QItemView(int entry, int count, int display)
        {
            QItemInfo ii;
            if (!_qItems.TryGetValue(entry, out ii)) { QAskItem(entry); ii = null; }
            int disp = display > 0 ? display : ii != null && ii.Display > 0 ? ii.Display : _db.ItemDisplayId(entry);
            var v = new ItemView();
            v.Entry = entry;
            v.Count = Math.Max(1, count);
            v.DisplayId = disp;
            v.Name = ii != null && !ii.Missing && !string.IsNullOrEmpty(ii.Name) ? ii.Name : "Item " + entry;   // ours until the name arrives
            v.Quality = ii != null && !ii.Missing ? ii.Quality : 1;
            v.InventoryType = ii != null ? ii.InventoryType : 0;
            v.IsQuestItem = ii != null && ii.Class == QItemClassQuest;
            v.IconPath = QIconOf(disp);
            v.Tooltip = ItemInfoTooltip(entry, v.Count)                         // the bags' full tooltip once Items.cs has the answer (integrator)
                        ?? v.Name + (v.IsQuestItem ? "\nQuest Item" : "");       // ITEM_BIND_QUEST (GlobalStrings) "Quest Item"
            return v;
        }

        string QIconOf(int displayId)
        {
            QLoadDbcs();
            if (_qItemDisplay != null && displayId > 0)
            {
                int r = _qItemDisplay.FindRow(displayId);
                if (r >= 0)
                {
                    string icon = _qItemDisplay.GetString(r, DbcFields.ItemDisplayInfo.InventoryIcon);
                    if (!string.IsNullOrEmpty(icon)) return @"Interface\Icons\" + icon;
                }
            }
            return @"Interface\Icons\INV_Misc_QuestionMark";
        }

        void QLoadDbcs()
        {
            if (_qDbcTried || _files == null) return;
            _qDbcTried = true;
            _qItemDisplay = Dbc.Load(_files, "ItemDisplayInfo");
            if (_qItemDisplay != null && _qItemDisplay.FieldCount != DbcFields.ItemDisplayInfo.FieldCount) _qItemDisplay = null;
            _qLock = Dbc.Load(_files, "Lock");
            if (_qLock != null && _qLock.FieldCount != QLockFieldCount) { WowLog.Warn("Online quests: Lock.dbc has " + _qLock.FieldCount + " fields, expected " + QLockFieldCount + " - locked objects cannot be opened"); _qLock = null; }
            _qLockType = Dbc.Load(_files, "LockType");
            _qClasses = Dbc.Load(_files, "ChrClasses");
            if (_qClasses != null && _qClasses.FieldCount != DbcFields.ChrClasses.FieldCount) _qClasses = null;
            _qRaces = Dbc.Load(_files, "ChrRaces");
            if (_qRaces != null && _qRaces.FieldCount != DbcFields.ChrRaces.FieldCount) _qRaces = null;
            _qQuestXp = Dbc.Load(_files, "QuestXP");
            if (_qQuestXp != null && _qQuestXp.FieldCount != QQuestXpFieldCount) { WowLog.Warn("Online quests: QuestXP.dbc has " + _qQuestXp.FieldCount + " fields, expected " + QQuestXpFieldCount + " - the quest log shows no XP until a giver names it"); _qQuestXp = null; }
        }

        /// <summary>
        /// The XP a quest log entry shows before any giver named a figure - what the 3.3.5 client computes itself from the template's XpId:
        /// Quest::XPValue (QuestDef.cpp:199-236) over QuestXP.dbc [quest level].Exp[XpId]; without the realm's XP rate (the server's
        /// QUEST_DETAILS / OFFER_REWARD figure replaces it as soon as a giver is spoken to); none at the level cap.
        /// </summary>
        int QXpValue(QTemplate t)
        {
            if (_player.Level >= QMaxLevel || t.XpId < 0 || t.XpId >= 10) return 0;
            QLoadDbcs();
            if (_qQuestXp == null) return 0;
            int questLevel = t.Level == -1 ? _player.Level : t.Level;
            int r = _qQuestXp.FindRow(questLevel);
            if (r < 0) return 0;
            int diffFactor = 2 * (questLevel - _player.Level) + 20;
            if (diffFactor < 1) diffFactor = 1;
            else if (diffFactor > 10) diffFactor = 10;
            int xp = diffFactor * _qQuestXp.GetInt(r, QQuestXpExp + t.XpId) / 10;
            if (xp <= 100) xp = 5 * ((xp + 2) / 5);
            else if (xp <= 500) xp = 10 * ((xp + 5) / 10);
            else if (xp <= 1000) xp = 25 * ((xp + 12) / 25);
            else xp = 50 * ((xp + 25) / 50);
            return Math.Max(0, xp);
        }

        int QBagCount(int entry)
        {
            int n = 0;
            var bags = _player.Bags;
            if (bags != null) for (int i = 0; i < bags.Count; i++) { var b = bags[i]; if (b != null && b.Entry == entry) n += Math.Max(1, b.Count); }
            var eq = _player.Equipment;
            if (eq != null) for (int i = 0; i < eq.Length; i++) if (eq[i] != null && eq[i].Entry == entry) n++;
            return n;
        }

        // ================================================================== the quest log (PLAYER_QUEST_LOG_1_1 + slot * 5: id, state, 2 x counters, time)

        int QLogSlotOf(int questId)
        {
            if (!_qSlotsKnown || questId <= 0) return -1;
            for (int s = 0; s < QLogSlots; s++) if ((int)_qSlots[s * QSlotFields] == questId) return s;
            return -1;
        }

        /// <summary>Our own update fields -> the log; new quests -> QuestAccepted, the complete bit -> QuestCompleted, a slot we abandoned emptying -> QuestAbandoned.</summary>
        void QReadLogFields()
        {
            var self = _self;
            if (self == null) return;
            bool changed = false;
            for (int k = 0; k < _qScratch.Length; k++)
            {
                uint v = self.U32(UF.PLAYER_QUEST_LOG_1_1 + k);
                _qScratch[k] = v;
                if (v != _qSlots[k]) changed = true;
            }
            if (!_qSlotsKnown)
            {
                Array.Copy(_qScratch, _qSlots, _qSlots.Length);
                _qSlotsKnown = true;
                int n = 0;
                for (int s = 0; s < QLogSlots; s++) { int id = (int)_qSlots[s * QSlotFields]; if (id > 0) { n++; QAskTemplate(id); } }
                _qLogDirty = true;
                WowLog.Info("Online quests: quest log from the server - " + n + " quests");
                return;
            }
            if (!changed) return;
            for (int s = 0; s < QLogSlots; s++)
            {
                int id = (int)_qScratch[s * QSlotFields];
                if (id <= 0) continue;
                int prev = -1;
                for (int ps = 0; ps < QLogSlots; ps++) if ((int)_qSlots[ps * QSlotFields] == id) { prev = ps; break; }
                uint state = _qScratch[s * QSlotFields + 1];
                if (prev < 0)
                {
                    QAskTemplate(id);
                    _qRewarded.Remove(id);
                    _qAbandoning.Remove(id);
                    int qid = id;
                    QEmit(GameEventType.QuestAccepted, qid, () => "Quest accepted: " + QTitle(qid));                          // ERR_QUEST_ACCEPTED_S (GlobalStrings.lua:3405)
                    WowLog.Info("Online quests: quest " + id + " '" + QTitle(id) + "' in the log (slot " + s + ")");
                    if ((state & QStateComplete) != 0) QEmit(GameEventType.QuestCompleted, qid, () => QTitle(qid) + " (Complete)");   // ERR_QUEST_OBJECTIVE_COMPLETE_S (3428)
                }
                else
                {
                    uint was = _qSlots[prev * QSlotFields + 1];
                    int qid = id;
                    if ((state & QStateComplete) != 0 && (was & QStateComplete) == 0)
                    {
                        QEmit(GameEventType.QuestCompleted, qid, () => QTitle(qid) + " (Complete)");                           // ERR_QUEST_OBJECTIVE_COMPLETE_S (3428)
                        WowLog.Info("Online quests: quest " + id + " '" + QTitle(id) + "' complete");
                    }
                }
            }
            for (int ps = 0; ps < QLogSlots; ps++)
            {
                int id = (int)_qSlots[ps * QSlotFields];
                if (id <= 0) continue;
                bool still = false;
                for (int s = 0; s < QLogSlots; s++) if ((int)_qScratch[s * QSlotFields] == id) { still = true; break; }
                if (still) continue;
                int qid = id;
                if (_qAbandoning.Remove(id)) QEmit(GameEventType.QuestAbandoned, qid, () => "Quest abandoned: " + QTitle(qid));   // ours, as OfflineSession
                WowLog.Info("Online quests: quest " + id + " '" + QTitle(id) + "' left the log");
                for (int j = 0; j < 6; j++) _qItemSeen.Remove(((long)id << 8) | (long)j);
                _qTimerTotal.Remove(id);                                          // iteration 11
            }
            Array.Copy(_qScratch, _qSlots, _qSlots.Length);
            _qLogDirty = true;
        }

        /// <summary>Item objectives follow the bags (AC sends SMSG_QUESTUPDATE_ADD_ITEM empty): a count going up -> QuestProgress "name: n/m" (ERR_QUEST_ADD_ITEM_SII).</summary>
        void QCheckBagCounts()
        {
            if (!_qSlotsKnown) return;
            for (int s = 0; s < QLogSlots; s++)
            {
                int id = (int)_qSlots[s * QSlotFields];
                QTemplate t;
                if (id <= 0 || !_qTemplates.TryGetValue(id, out t)) continue;
                for (int j = 0; j < 6; j++)
                {
                    int item = t.ReqItem[j], need = t.ReqItemCount[j];
                    if (item <= 0 || need <= 0) continue;
                    int have = Math.Min(QBagCount(item), need);
                    long key = ((long)id << 8) | (long)j;
                    int old;
                    if (!_qItemSeen.TryGetValue(key, out old)) { _qItemSeen[key] = have; _qLogDirty = true; continue; }
                    if (have == old) continue;
                    _qItemSeen[key] = have;
                    _qLogDirty = true;
                    if (have > old)
                    {
                        int qid = id, it = item, h = have, nd = need;
                        QEmit(GameEventType.QuestProgress, qid, () => QItemName(it) + ": " + h + "/" + nd);                    // ERR_QUEST_ADD_ITEM_SII (GlobalStrings.lua:3407)
                    }
                }
            }
        }

        void QRefreshLog()
        {
            var log = _player.QuestLog;
            log.Clear();
            _qAnyTimed = false;                                                   // iteration 11: QFillExtras sets it again for a timed quest
            if (!_qSlotsKnown) return;
            for (int s = 0; s < QLogSlots; s++)
            {
                int id = (int)_qSlots[s * QSlotFields];
                if (id <= 0) continue;
                var st = (_qSlots[s * QSlotFields + 1] & QStateComplete) != 0 ? QuestState.Complete : QuestState.Active;
                var v = QBuildView(id, st, s, null, false);
                QPoiAttach(v);                                                   // iteration 11 quest map (OnlineSession.QuestPoi.cs)
                log.Add(v);
            }
        }

        // ================================================================== events and texts

        /// <summary>Quest events go out in order, each once its quest's title is known (or after QEventWaitSeconds).</summary>
        void QEmit(GameEventType type, int questId, Func<string> text)
        {
            QAskTemplate(questId);
            _qEvents.Add(new QPending { E = new GameEvent { Type = type, QuestId = questId }, QuestId = questId, Text = text, Until = _now + QEventWaitSeconds });
        }

        void QFlushEvents()
        {
            while (_qEvents.Count > 0)
            {
                var q = _qEvents[0];
                if (q.QuestId > 0 && _now < q.Until && !_qTemplates.ContainsKey(q.QuestId))
                {
                    QTalk k;
                    if (!_qTalk.TryGetValue(q.QuestId, out k) || string.IsNullOrEmpty(k.Title)) break;
                }
                _qEvents.RemoveAt(0);
                var e = q.E;
                e.Text = q.Text != null ? q.Text() : null;
                _events.Enqueue(e);
            }
        }

        /// <summary>WoW's text tokens: $N name, $C class, $R race, $G male:female; by the player's gender, $B line break (lower case too).</summary>
        string QPersonal(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('$') < 0) return s;
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '$' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char t = s[i + 1];
                switch (t)
                {
                    case 'N': case 'n': sb.Append(_player.Name ?? ""); i++; break;
                    case 'C': sb.Append(QClassName()); i++; break;
                    case 'c': sb.Append(QClassName().ToLowerInvariant()); i++; break;
                    case 'R': sb.Append(QRaceName()); i++; break;
                    case 'r': sb.Append(QRaceName().ToLowerInvariant()); i++; break;
                    case 'B': case 'b': sb.Append('\n'); i++; break;
                    case 'G': case 'g':
                    {
                        int colon = s.IndexOf(':', i + 2), semi = colon >= 0 ? s.IndexOf(';', colon + 1) : -1;
                        if (colon < 0 || semi < 0) { sb.Append(c); break; }
                        sb.Append(QFemale() ? s.Substring(colon + 1, semi - colon - 1) : s.Substring(i + 2, colon - i - 2).TrimStart());
                        i = semi;
                        break;
                    }
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        bool QFemale() { return _self != null && _self.Byte(UF.UNIT_FIELD_BYTES_0, 2) == 1; }

        string QClassName()
        {
            QLoadDbcs();
            int r = _qClasses != null ? _qClasses.FindRow((int)_player.Class) : -1;
            string n = r >= 0 ? _qClasses.GetString(r, DbcFields.ChrClasses.Name_lang) : null;
            return string.IsNullOrEmpty(n) ? _player.Class.ToString() : n;
        }

        string QRaceName()
        {
            QLoadDbcs();
            int r = _qRaces != null ? _qRaces.FindRow(_player.Race) : -1;
            string n = r >= 0 ? _qRaces.GetString(r, DbcFields.ChrRaces.Name_lang) : null;
            return string.IsNullOrEmpty(n) ? "" : n;
        }

        /// <summary>The offline game's money text (OfflineSession.MoneyText): "1 Gold, 2 Silver, 3 Copper".</summary>
        static string QMoneyText(int copper)
        {
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var parts = new List<string>();
            if (g > 0) parts.Add(g + " Gold");
            if (s > 0) parts.Add(s + " Silver");
            if (c > 0 || parts.Count == 0) parts.Add(c + " Copper");
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// WorldObject::IsWithinDistInMap(obj, dist) with both radii (Object.cpp:1261 _IsWithinDist; Object.h:556 defaults 3D, own + target
        /// radius; GetObjectSize = UNIT_FIELD_COMBATREACH for units, DEFAULT_WORLD_OBJECT_SIZE * scale otherwise, Object.cpp:2897).
        /// </summary>
        bool QInReach(OnlineObject o, EntityState e, float dist)
        {
            float mine = _self != null ? _self.F32(UF.UNIT_FIELD_COMBATREACH) : 0f;
            if (!(mine > 0f && mine < 50f)) mine = QDefaultCombatReach;
            float theirs = o.IsUnit ? o.F32(UF.UNIT_FIELD_COMBATREACH) : 0f;
            if (!(theirs > 0f && theirs < 50f)) theirs = QDefaultObjectSize * (e.Scale > 0f ? e.Scale : 1f);
            float max = dist + mine + theirs;
            float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z;
            return dx * dx + dy * dy + dz * dz < max * max;
        }

        /// <summary>Counters for the probes and the session summary.</summary>
        public string QuestSummary()
        {
            return "dialogs " + _qDialogs + ", accepts " + _qAccepted + ", hand-ins " + _qRewardsSent + ", templates " + _qTemplatesIn + ", log " + _player.QuestLog.Count
                   + ", items named " + _qItems.Count + ", events waiting " + _qEvents.Count + ", " + QPoiSummary;
        }
    }
}
