using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Stage S1 (lead contract stub, PLAN10 s2/s3): owner party fills this file (bodies only; the member signatures are frozen).
    //
    // WoW's party on the AzerothCore realm, the way the 3.3.5a client does it (the server is the truth). Protocol truth = AzerothCore
    // master (copies in P/acsrc10): src/server/game/Handlers/GroupHandler.cpp, src/server/game/Groups/Group.cpp + Group.h,
    // SharedDefines.h (PartyResult), WorldSession.h (PartyOperation).
    // - InviteToGroup(name)      -> CMSG_GROUP_INVITE: "recvData >> membername; recvData.read_skip<uint32>();" (GroupHandler.cpp:66-68).
    // - SMSG_GROUP_INVITE        -> PlayerState.PendingInviteFrom + GroupInvite: uint8 flag (1 invited / 0 "already in group"), name,
    //                               uint32 0, uint8 0 (count), uint32 0 (GroupHandler.cpp:229-235, :169-175).
    // - AnswerGroupInvite(true)  -> CMSG_GROUP_ACCEPT "recvData.read_skip<uint32>();" (GroupHandler.cpp:242); false -> CMSG_GROUP_DECLINE (empty).
    // - SMSG_GROUP_LIST          -> PlayerState.Group / GroupLeaderGuid + GroupChanged (Group::SendUpdateToPlayer, Group.cpp:1883-1951).
    // - SMSG_PARTY_MEMBER_STATS(_FULL) -> each member's health / power / level / zone / position / online / dead / ghost, parsed by its
    //                               GroupUpdateFlags (BuildPartyMemberStatsChangedPacket GroupHandler.cpp:817-1000, _FULL :1003-1135).
    //                               CMSG_REQUEST_PARTY_MEMBER_STATS (uint64 guid, :1005-1006) once per new member, like the client.
    // - SMSG_GROUP_DESTROYED / SMSG_GROUP_UNINVITE / SMSG_GROUP_DECLINE / SMSG_GROUP_SET_LEADER / SMSG_PARTY_COMMAND_RESULT -> WoW's texts.
    // - LeaveGroup()             -> CMSG_GROUP_DISBAND (empty; HandleGroupDisbandOpcode :490-514).
    // Every text is WoW's (GlobalStrings.lua key + line); each is emitted once as a ChatMessage (ChatKind.System, SourceGuid 0) for the
    // chat log and once as an Info event for the HUD's yellow line (GrpText). Members' race / class come from the visible object's
    // UNIT_FIELD_BYTES_0 or SMSG_NAME_QUERY_RESPONSE (QueryPackets.cpp:25-45, a read-only peek next to Objects.cs' handler).
    public sealed partial class OnlineSession
    {
        // ---- AzerothCore constants (Group.h:58-125, SharedDefines.h:3959-3990, WorldSession.h:254-260) ----------------------------
        const uint GrpStatusOnline = 0x0001, GrpStatusDead = 0x0004, GrpStatusGhost = 0x0008;   // GroupMemberOnlineStatus
        const byte GrpTypeLfg = 0x08;                                                            // GROUPTYPE_LFG
        const uint GufStatus = 0x1, GufCurHp = 0x2, GufMaxHp = 0x4, GufPowerType = 0x8, GufCurPower = 0x10, GufMaxPower = 0x20,
                   GufLevel = 0x40, GufZone = 0x80, GufPosition = 0x100, GufAuras = 0x200, GufPetGuid = 0x400, GufPetName = 0x800,
                   GufPetModel = 0x1000, GufPetCurHp = 0x2000, GufPetMaxHp = 0x4000, GufPetPowerType = 0x8000, GufPetCurPower = 0x10000,
                   GufPetMaxPower = 0x20000, GufPetAuras = 0x40000, GufVehicleSeat = 0x80000;   // GroupUpdateFlags
        const uint GufKnown = 0x000FFFFF;                                                         // GROUP_UPDATE_FULL | VEHICLE_SEAT
        const int PartyOpInvite = 0, PartyOpUninvite = 1, PartyOpLeave = 2, PartyOpSwap = 4;     // PartyOperation
        const int MaxGroupListCount = 39;                                                         // MAXRAIDSIZE 40 - the receiver
        const int GrpStatsLogEvery = 50;                                                          // ours: log the 1st and every 50th stats packet per member

        readonly Dictionary<ulong, GroupMemberView> _grpKnown = new Dictionary<ulong, GroupMemberView>();   // every member seen this session (stats may come first)
        readonly Dictionary<ulong, int> _grpStatsCount = new Dictionary<ulong, int>();
        readonly HashSet<ulong> _grpStatsAsked = new HashSet<ulong>();
        readonly HashSet<ulong> _grpNameAsked = new HashSet<ulong>();
        readonly Dictionary<ulong, int> _grpNameRace = new Dictionary<ulong, int>(), _grpNameClass = new Dictionary<ulong, int>();
        readonly HashSet<string> _grpOddLogged = new HashSet<string>();
        readonly List<GroupMemberView> _grpNew = new List<GroupMemberView>();
        ulong _grpGuid;                   // the group's own guid (SMSG_GROUP_LIST), 0 = none (log only)
        bool _grpInviteSentOk;            // our CMSG_GROUP_INVITE got ERR_PARTY_RESULT_OK: a pending group we lead (LeaveGroup cancels it)
        int _grpLists;
        DbcTable _grpAreas;
        bool _grpAreasTried;

        void InitGroup()
        {
            _world.On(Op.SMSG_GROUP_INVITE, GrpOnInvite, this);
            _world.On(Op.SMSG_GROUP_LIST, GrpOnList, this);
            _world.On(Op.SMSG_PARTY_MEMBER_STATS, p => GrpOnStats(p, false), this);
            _world.On(Op.SMSG_PARTY_MEMBER_STATS_FULL, p => GrpOnStats(p, true), this);
            _world.On(Op.SMSG_PARTY_COMMAND_RESULT, GrpOnCommandResult, this);
            _world.On(Op.SMSG_GROUP_DECLINE, GrpOnDecline, this);
            _world.On(Op.SMSG_GROUP_DESTROYED, GrpOnDestroyed, this);
            _world.On(Op.SMSG_GROUP_UNINVITE, GrpOnUninvite, this);
            _world.On(Op.SMSG_GROUP_SET_LEADER, GrpOnSetLeader, this);
            _world.On(Op.SMSG_GROUPACTION_THROTTLED, p => GrpText("You have attempted too many group actions in a short period of time.  Please wait momentarily before attempting further group actions."), this);   // ERR_GROUP_ACTION_THROTTLED (GlobalStrings.lua:3068)
            _world.On(Op.SMSG_NAME_QUERY_RESPONSE, GrpOnNameQuery, this);   // read-only peek: race / class of group members
            _world.On(Op.SMSG_LOOT_START_ROLL, GrpOnStartRoll, this);
            _world.On(Op.SMSG_LOOT_ROLL, GrpOnRoll, this);
            _world.On(Op.SMSG_LOOT_ROLL_WON, GrpOnRollWon, this);
            _world.On(Op.SMSG_LOOT_ALL_PASSED, GrpOnAllPassed, this);
        }

        /// <summary>Every frame: the members' live values from the objects we can see (the client reads UnitHealth from the object when it
        /// is in range, from the party stats otherwise), their map, Somnium marking, and the name queries for race / class.</summary>
        void TickGroup(float dt)
        {
            List<GroupMemberView> g = _player.Group;
            for (int i = 0; i < g.Count; i++) GrpRefresh(g[i]);
            List<LootRollView> rolls = _player.LootRolls;
            for (int i = rolls.Count - 1; i >= 0; i--)
            {
                LootRollView r = rolls[i];
                r.SecondsLeft -= dt;
                GrpFillRollItem(r);
                if (r.SecondsLeft > 0f) continue;
                rolls.RemoveAt(i);   // GroupLootFrame hides itself at the end of its timer; the server passes for us
                WowLog.Info("Online group: the roll for " + GrpItemName(r.Item.Entry) + " timed out unanswered (the server counts it as a pass)");
            }
        }

        // ------------------------------------------------------------------ actions (IWowSession)

        public void InviteToGroup(string characterName)
        {
            string name = characterName != null ? characterName.Trim() : "";
            if (name.Length == 0) return;
            if (string.Equals(name, _player.Name, StringComparison.OrdinalIgnoreCase))
            {
                GrpText("You can't invite yourself to a group.");   // ERR_INVITE_SELF (GlobalStrings.lua:3154)
                return;
            }
            if (_world == null) { Error(NotYet); return; }
            // HandleGroupInviteOpcode: "recvData >> membername; recvData.read_skip<uint32>();" - the name as typed (the server normalizes it).
            _world.Send(Op.CMSG_GROUP_INVITE, new PacketWriter().CString(name).U32(0));
            WowLog.Info("Online group: CMSG_GROUP_INVITE '" + name + "'");
        }

        public void AnswerGroupInvite(bool accept)
        {
            string from = _player.PendingInviteFrom;
            if (from == null) { WowLog.Info("Online group: answer (" + (accept ? "accept" : "decline") + ") without a pending invite - ignored"); return; }
            _player.PendingInviteFrom = null;
            if (_world == null) return;
            if (accept) _world.Send(Op.CMSG_GROUP_ACCEPT, new PacketWriter().U32(0));   // HandleGroupAcceptOpcode: read_skip<uint32>
            else _world.Send(Op.CMSG_GROUP_DECLINE);                                    // HandleGroupDeclineOpcode: no body
            WowLog.Info("Online group: " + (accept ? "CMSG_GROUP_ACCEPT" : "CMSG_GROUP_DECLINE") + " (invite from " + from + ")");
        }

        public void LeaveGroup()
        {
            if (_player.Group.Count == 0 && !_grpInviteSentOk)
            {
                GrpText("You aren't in a party.");   // ERR_NOT_IN_GROUP (GlobalStrings.lua:3311)
                return;
            }
            if (_world == null) return;
            // HandleGroupDisbandOpcode: leaves the group (SendPartyResult(PARTY_OP_LEAVE, name, OK) + RemoveFromGroup) or cancels the
            // pending group we lead (grpInvite->Disband()).
            _world.Send(Op.CMSG_GROUP_DISBAND);
            _grpInviteSentOk = false;
            WowLog.Info("Online group: CMSG_GROUP_DISBAND (" + _player.Group.Count + " other member(s))");
        }

        // Lead (2026-09-27): the leader's commands from UnitPopup (UNINVITE, PROMOTE, LOOT_METHOD, LOOT_THRESHOLD) and GroupLootFrame's
        // Need / Greed / Disenchant / Pass. The server checks leadership itself and answers errors with SMSG_PARTY_COMMAND_RESULT.

        bool GrpLeadCheck()
        {
            if (_player.Group.Count == 0) { GrpText("You aren't in a party."); return false; }                 // ERR_NOT_IN_GROUP (GlobalStrings.lua:3311)
            if (_player.GroupLeaderGuid != _player.Guid) { GrpText("You are not the party leader."); return false; }   // ERR_NOT_LEADER (:3313)
            return _world != null;
        }

        public void UninviteFromGroup(ulong guid)
        {
            if (guid == 0 || guid == _player.Guid || !GrpLeadCheck()) return;
            // HandleGroupUninviteGuidOpcode: "recvData >> guid; recvData >> reason;" - an empty reason outside LFG (GroupHandler.cpp:353-423).
            _world.Send(Op.CMSG_GROUP_UNINVITE_GUID, new PacketWriter().U64(guid).CString(""));
            WowLog.Info("Online group: CMSG_GROUP_UNINVITE_GUID " + GrpNameOf(guid));
        }

        public void PromoteToLeader(ulong guid)
        {
            if (guid == 0 || guid == _player.Guid || !GrpLeadCheck()) return;
            // HandleGroupSetLeaderOpcode: "recvData >> guid;" - the new leader must be connected (FindConnectedPlayer) (:471-488).
            _world.Send(Op.CMSG_GROUP_SET_LEADER, new PacketWriter().U64(guid));
            WowLog.Info("Online group: CMSG_GROUP_SET_LEADER " + GrpNameOf(guid));
        }

        public void SetLootMethod(int method, int threshold)
        {
            if (method < 0 || method > 4 || threshold < 2 || threshold > 6 || !GrpLeadCheck()) return;   // FREE_FOR_ALL..NEED_BEFORE_GREED, UNCOMMON..ARTIFACT
            // HandleLootMethodOpcode: "recvData >> lootMethod >> lootMaster >> lootThreshold;" (uint32, uint64, uint32; :516-547). The master
            // looter is you, as UnitPopup's MASTER_LOOTER on your own frame does: SetLootMethod("master", fullname) (UnitPopup.lua:1293).
            _world.Send(Op.CMSG_LOOT_METHOD, new PacketWriter().U32((uint)method).U64(method == 2 ? _player.Guid : 0).U32((uint)threshold));
            WowLog.Info("Online group: CMSG_LOOT_METHOD " + method + ", threshold " + threshold);
        }

        public void RollOnLoot(LootRollView roll, int rollType)
        {
            if (roll == null || !_player.LootRolls.Remove(roll)) return;
            if ((rollType == 1 && !roll.CanNeed) || (rollType == 2 && !roll.CanGreed) || (rollType == 3 && !roll.CanDisenchant) || rollType < 0 || rollType > 3) rollType = 0;
            if (_world == null) return;
            // HandleLootRoll: "recvData >> guid; recvData >> itemSlot; recvData >> rollType;" (uint64, uint32, uint8; :549-575).
            _world.Send(Op.CMSG_LOOT_ROLL, new PacketWriter().U64(roll.RollGuid).U32((uint)roll.Slot).U8((byte)rollType));
            WowLog.Info("Online group: CMSG_LOOT_ROLL " + GrpItemName(roll.Item.Entry) + " - " + GrpRollWord(rollType));
        }

        // ------------------------------------------------------------------ loot rolls (Group.cpp SendLootStartRoll / SendLootRoll / SendLootRollWon / SendLootAllPassed)

        const byte RollFlagNeed = 0x02, RollFlagGreed = 0x04, RollFlagDisenchant = 0x08;   // RollMask (LootMgr.h:40-48)

        /// <summary>SMSG_LOOT_START_ROLL: uint64 item guid, uint32 map, uint32 slot, uint32 item entry, uint32 random suffix, uint32 random
        /// property, uint32 count, uint32 countdown (ms, 60000), uint8 vote mask (Group.cpp:927-946).</summary>
        void GrpOnStartRoll(WowPacket p)
        {
            ulong guid = p.U64();
            p.U32();
            int slot = (int)p.U32();
            int entry = (int)p.U32();
            int suffixFactor = (int)p.U32(), randomProp = (int)p.U32();          // "data << uint32(r.itemRandomSuffix); data << uint32(r.itemRandomPropId)" (Group.cpp:934-935)
            int count = (int)p.U32();
            uint ms = p.U32();
            byte mask = p.U8();
            var r = new LootRollView
            {
                RollGuid = guid, Slot = slot, Item = new ItemView { Entry = entry, Count = Math.Max(1, count) },
                Duration = Math.Max(1f, ms / 1000f), SecondsLeft = Math.Max(1f, ms / 1000f),
                CanNeed = (mask & RollFlagNeed) != 0, CanGreed = (mask & RollFlagGreed) != 0, CanDisenchant = (mask & RollFlagDisenchant) != 0,
            };
            var rollInst = RollOf(suffixFactor, randomProp);
            if (rollInst != null) _grpRollInst[guid] = rollInst; else _grpRollInst.Remove(guid);   // the item tooltip fix: "of the Bear" + its stats
            GrpFillRollItem(r);
            _player.LootRolls.Add(r);
            WowLog.Info("Online group: SMSG_LOOT_START_ROLL item " + entry + " x" + r.Item.Count + ", " + r.Duration.ToString("0") + " s, mask 0x" + mask.ToString("X2"));
        }

        /// <summary>SMSG_LOOT_ROLL: uint64 item guid (0 for a vote), uint32 slot, uint64 player, uint32 entry, uint32 suffix, uint32 property,
        /// uint8 roll number (0 + type 0 = chose Need, 128 = chose Pass / Greed / Disenchant, 1..100 = the roll), uint8 roll type, uint8 auto
        /// pass (Group.cpp:1017-1039; the votes :1515-1533, the rolls :1635 / :1715).</summary>
        void GrpOnRoll(WowPacket p)
        {
            p.U64(); p.U32();
            ulong who = p.U64();
            int entry = (int)p.U32();
            p.U32(); int rollProp = (int)p.U32();                                // suffix factor, random property
            int number = p.U8();
            int type = p.U8();
            bool autoPass = p.U8() != 0;
            bool me = who == _player.Guid;
            string item = GrpItemName(entry, rollProp), name = GrpNameOf(who);
            string text;
            if (autoPass && me) text = "You automatically passed on: " + item + " because you cannot loot that item.";   // LOOT_ROLL_PASSED_SELF_AUTO (GlobalStrings.lua:4831)
            else if (number == 0 && type == 0) text = me ? "You have selected Need for: " + item : name + " has selected Need for: " + item;   // LOOT_ROLL_NEED(_SELF) (:4825-4826)
            else if (number == 128 && type == 2) text = me ? "You have selected Greed for: " + item : name + " has selected Greed for: " + item;   // LOOT_ROLL_GREED(_SELF) (:4818-4819)
            else if (number == 128 && type == 3) text = me ? "You have selected Disenchant for: " + item : name + " has selected Disenchant for: " + item;   // LOOT_ROLL_DISENCHANT(_SELF) (:4816-4817)
            else if (number >= 128) text = me ? "You passed on: " + item : name + " passed on: " + item;   // LOOT_ROLL_PASSED(_SELF) (:4827, :4830); auto-passes of others too (no gendered text)
            else text = (type == 1 ? "Need" : type == 3 ? "Disenchant" : "Greed") + " Roll - " + number + " for " + item + " by " + (me ? _player.Name : name);   // LOOT_ROLL_ROLLED_NEED / _GREED / _DE (:4832-4834)
            GrpText(text, false);
        }

        /// <summary>SMSG_LOOT_ROLL_WON: uint64 item guid, uint32 slot, uint32 entry, uint32 suffix, uint32 property, uint64 winner, uint8 roll
        /// number, uint8 roll type (Group.cpp:1041-1062).</summary>
        void GrpOnRollWon(WowPacket p)
        {
            ulong guid = p.U64();
            p.U32();
            int entry = (int)p.U32();
            p.U32(); int wonProp = (int)p.U32();                                 // suffix factor, random property
            ulong winner = p.U64();
            string item = GrpItemName(entry, wonProp);
            GrpText(winner == _player.Guid ? "You won: " + item : GrpNameOf(winner) + " won: " + item, false);   // LOOT_ROLL_YOU_WON / LOOT_ROLL_WON (:4835, :4839)
            GrpDropRoll(guid);
        }

        /// <summary>SMSG_LOOT_ALL_PASSED: uint64 item guid, uint32 slot, uint32 entry, uint32 property, uint32 suffix (Group.cpp:1064-1080).</summary>
        void GrpOnAllPassed(WowPacket p)
        {
            ulong guid = p.U64();
            p.U32();
            int entry = (int)p.U32();
            int passedProp = (int)p.U32();                                       // "data << uint32(roll.itemRandomPropId)" (Group.cpp:1070)
            GrpText("Everyone passed on: " + GrpItemName(entry, passedProp), false);   // LOOT_ROLL_ALL_PASSED (:4815)
            GrpDropRoll(guid);
        }

        void GrpDropRoll(ulong guid)
        {
            List<LootRollView> rolls = _player.LootRolls;
            for (int i = rolls.Count - 1; i >= 0; i--) if (rolls[i].RollGuid == guid) rolls.RemoveAt(i);
            _grpRollInst.Remove(guid);
        }

        void GrpFillRollItem(LootRollView r)
        {
            if (r.Item.Name != null) return;
            ItemProto proto;
            if (!_itemProtos.TryGetValue(r.Item.Entry, out proto)) { AskItem(r.Item.Entry); return; }
            ItemTipInstance inst;
            _grpRollInst.TryGetValue(r.RollGuid, out inst);
            if (!proto.Missing) FillItemView(r.Item, r.Item.Entry, r.Item.Count, proto, inst);
        }

        readonly Dictionary<ulong, ItemTipInstance> _grpRollInst = new Dictionary<ulong, ItemTipInstance>();   // open rolls: the item's random property / suffix

        string GrpItemName(int entry, int randomProp = 0)
        {
            string n = ItemInfoNameOf(entry, randomProp);
            return "[" + (n ?? "item " + entry) + "]";   // the client's item link, as text
        }

        static string GrpRollWord(int type) { return type == 1 ? "Need" : type == 2 ? "Greed" : type == 3 ? "Disenchant" : "Pass"; }   // NEED / GREED / ROLL_DISENCHANT / PASS

        static readonly string[] LootMethodText = { "Looting changed to Free for All.", "Looting changed to Round Robin.", "Looting changed to Master Looter.",
                                                    "Looting changed to Group Loot.", "Looting set to Need Before Greed." };   // ERR_SET_LOOT_* (GlobalStrings.lua:3479-3483)
        static readonly string[] QualityText = { "Poor", "Common", "Uncommon", "Rare", "Epic", "Legendary", "Artifact" };   // ITEM_QUALITYn_DESC (:4490-4496)

        // ------------------------------------------------------------------ handlers

        /// <summary>SMSG_GROUP_INVITE (GroupHandler.cpp:229-235): uint8 1 = "you are invited", 0 = "invited, but you are already in a
        /// group" (:169-175); inviter name; uint32 0, uint8 0 (count), uint32 0 follow (not needed).</summary>
        void GrpOnInvite(WowPacket p)
        {
            bool invited = p.U8() != 0;
            string name = p.CString();
            if (string.IsNullOrEmpty(name)) { GrpOdd("SMSG_GROUP_INVITE", "no inviter name (" + p.Length + " bytes)"); return; }
            if (!invited)
            {
                // ERR_INVITED_ALREADY_IN_GROUP_SS "|Hplayer:%s|h[%s]|h invited you to a group, but ..." (GlobalStrings.lua:3144) - the link shown as [name]
                GrpText("[" + name + "] invited you to a group, but you could not accept because you are already in a group.");
                WowLog.Info("Online group: SMSG_GROUP_INVITE from " + name + " while already in a group (flag 0)");
                return;
            }
            _player.PendingInviteFrom = name;
            // ERR_INVITED_TO_GROUP_SS "|Hplayer:%s|h[%s]|h has invited you to join a group." (GlobalStrings.lua:3146): the chat line; the popup is
            // StaticPopup "PARTY_INVITE" INVITATION (the UI, UiGroup).
            GrpText("[" + name + "] has invited you to join a group.", false);
            _events.Enqueue(new GameEvent { Type = GameEventType.GroupInvite, Text = name });
            WowLog.Info("Online group: SMSG_GROUP_INVITE from " + name + " - PendingInviteFrom set (Accept / Decline)");
        }

        /// <summary>
        /// SMSG_GROUP_LIST (Group::SendUpdateToPlayer, Group.cpp:1901-1949): uint8 groupType, uint8 subgroup, uint8 flags, uint8 roles,
        /// [GROUPTYPE_LFG: uint8 dungeon status, uint32 dungeon], uint64 group guid, uint32 counter, uint32 count, per OTHER member
        /// (name, uint64 guid, uint8 online state, uint8 group, uint8 flags, uint8 roles), uint64 leader guid, [count &gt; 0: uint8 loot
        /// method, uint64 looter, uint8 threshold, uint8 dungeon difficulty, uint8 raid difficulty, uint8 dynamic]. After leaving / a
        /// disband the list is empty (0x10, 0, 0, 0, guid, counter, 0, uint64 0; Group.cpp:680-682, :878-880).
        /// </summary>
        void GrpOnList(WowPacket p)
        {
            byte type = p.U8();
            p.U8(); p.U8(); p.U8();                          // our subgroup, flags, roles
            if ((type & GrpTypeLfg) != 0) { p.U8(); p.U32(); }
            ulong groupGuid = p.U64();
            uint counter = p.U32();
            uint count = p.U32();
            if (count > MaxGroupListCount) { GrpOdd("SMSG_GROUP_LIST", "count " + count + " (> " + MaxGroupListCount + ") - ignored"); return; }
            _grpNew.Clear();
            for (int i = 0; i < count; i++)
            {
                string name = p.CString();
                ulong guid = p.U64();
                byte online = p.U8();
                p.U8(); p.U8(); p.U8();                      // group (subgroup), flags (GroupMemberFlags), roles
                if (guid == 0) continue;
                GroupMemberView v = GrpView(guid);
                if (!string.IsNullOrEmpty(name)) v.Name = name;
                v.Online = (online & GrpStatusOnline) != 0;
                if (!v.Online) { v.Dead = false; v.Ghost = false; v.MapId = -1; }
                _grpNew.Add(v);
            }
            ulong leader = p.Remaining >= 8 ? p.U64() : 0;
            if (count == 0) leader = 0;
            // count > 0: uint8 loot method, uint64 master looter (0 unless MASTER_LOOT), uint8 threshold, then the difficulties (Group.cpp:1935-1947)
            int lootMethod = -1, threshold = 2;
            ulong master = 0;
            if (count > 0 && p.Remaining >= 10) { lootMethod = p.U8(); master = p.U64(); threshold = p.U8(); }
            // The client's system lines when the leader changes them (ERR_SET_LOOT_* / ERR_SET_LOOT_THRESHOLD_S, GlobalStrings.lua:3479-3484);
            // ours: not on our first list of a group.
            bool sameGroup = count > 0 && groupGuid == _grpGuid && _player.GroupLootMethod >= 0;
            if (sameGroup && lootMethod != _player.GroupLootMethod && lootMethod >= 0 && lootMethod < LootMethodText.Length) GrpText(LootMethodText[lootMethod]);
            if (sameGroup && threshold != _player.GroupLootThreshold && threshold >= 0 && threshold < QualityText.Length) GrpText("Loot threshold set to " + QualityText[threshold] + ".");
            _player.GroupLootMethod = lootMethod;
            _player.GroupLootThreshold = threshold;
            _player.GroupMasterLooterGuid = master;
            if (count == 0) _player.LootRolls.Clear();   // leaving the group ends our rolls server-side (RemoveMember -> CountRollVote MAX_ROLL_TYPE)

            List<GroupMemberView> g = _player.Group;
            bool wasIn = g.Count > 0;
            ulong oldLeader = _player.GroupLeaderGuid;
            bool changed = g.Count != _grpNew.Count || oldLeader != leader;
            for (int i = 0; !changed && i < g.Count; i++) if (g[i] != _grpNew[i]) changed = true;

            // WoW's texts for members added / removed (the client prints them when the list changes): ERR_JOINED_GROUP_S
            // "%s joins the party." (GlobalStrings.lua:3173) / ERR_LEFT_GROUP_S "%s leaves the party." (:3179). Ours: our own first list
            // prints the joins only when we lead (the invitee accepted our invite); when we are the one joining, the popup was the feedback.
            bool raidList = (type & 0x02) != 0;   // iteration 11 (interact H5): a raid prints ERR_RAID_MEMBER_ADDED_S / _REMOVED_S instead (OnlineSession.Raid.cs)
            bool printJoins = (wasIn || leader == _player.Guid) && !raidList;
            for (int i = 0; i < _grpNew.Count; i++)
                if (!g.Contains(_grpNew[i]) && printJoins) GrpText((_grpNew[i].Name ?? "?") + " joins the party.");
            if (_grpNew.Count > 0 && !raidList)
                for (int i = 0; i < g.Count; i++)
                    if (!_grpNew.Contains(g[i])) GrpText((g[i].Name ?? "?") + " leaves the party.");

            g.Clear();
            g.AddRange(_grpNew);
            _player.GroupLeaderGuid = leader;
            for (int i = 0; i < g.Count; i++)
            {
                g[i].Leader = g[i].Guid == leader;
                GrpRefresh(g[i]);
                if (g[i].Online && _grpStatsAsked.Add(g[i].Guid))
                    _world.Send(Op.CMSG_REQUEST_PARTY_MEMBER_STATS, new PacketWriter().U64(g[i].Guid));   // "recvData >> Guid" (GroupHandler.cpp:1005-1006)
            }
            if (g.Count > 0) _grpInviteSentOk = false;
            bool newGroup = count > 0 && groupGuid != _grpGuid;
            _grpGuid = count > 0 ? groupGuid : 0;
            _grpLists++;
            if (changed) _events.Enqueue(new GameEvent { Type = GameEventType.GroupChanged, Amount = g.Count });
            if (changed || _grpLists <= 3)
                WowLog.Info("Online group: SMSG_GROUP_LIST #" + counter + " type 0x" + type.ToString("X2") + " - " + g.Count + " other member(s)" + GrpNames(g) +
                            ", leader " + (leader == 0 ? "none" : leader == _player.Guid ? "me" : GrpNameOf(leader)) + (changed ? " (changed)" : "") + (newGroup ? ", group 0x" + groupGuid.ToString("X") : ""));
        }

        /// <summary>
        /// SMSG_PARTY_MEMBER_STATS (BuildPartyMemberStatsChangedPacket): packed guid, uint32 mask, then per set GroupUpdateFlag in order:
        /// STATUS uint16, CUR_HP uint32, MAX_HP uint32, POWER_TYPE uint8, CUR_POWER uint16, MAX_POWER uint16, LEVEL uint16, ZONE uint16,
        /// POSITION uint16 x + uint16 y, AURAS uint64 mask + per bit (uint32 spell, uint8), PET_GUID uint64, PET_NAME string, PET_MODEL_ID uint16,
        /// PET_CUR_HP uint32, PET_MAX_HP uint32, PET_POWER_TYPE uint8, PET_CUR_POWER uint16, PET_MAX_POWER uint16, PET_AURAS uint64 mask + per
        /// bit (uint32, uint8), VEHICLE_SEAT uint32. "if (mask &amp; GROUP_UPDATE_FLAG_POWER_TYPE) mask |= (CUR_POWER | MAX_POWER)" is applied
        /// before the mask is written. _FULL (HandleRequestPartyMemberStatsOpcode) starts with uint8 0; it only carries POWER_TYPE when the
        /// power is not mana ("if (powerType != POWER_MANA) updateFlags |= GROUP_UPDATE_FLAG_POWER_TYPE"); an unknown / not-grouped player
        /// answers STATUS = MEMBER_STATUS_OFFLINE only.
        /// </summary>
        void GrpOnStats(WowPacket p, bool full)
        {
            if (full) p.U8();
            ulong guid = p.PackedGuid();
            uint mask = p.U32();
            if (guid == 0) return;
            if ((mask & ~GufKnown) != 0) GrpOdd(full ? "SMSG_PARTY_MEMBER_STATS_FULL" : "SMSG_PARTY_MEMBER_STATS", "unknown update bits 0x" + (mask & ~GufKnown).ToString("X") + " (after the known fields - ignored)");
            GroupMemberView v = GrpView(guid);
            if ((mask & GufStatus) != 0)
            {
                uint st = p.U16();
                v.Online = (st & GrpStatusOnline) != 0;
                v.Ghost = (st & GrpStatusGhost) != 0;
                v.Dead = v.Ghost || (st & GrpStatusDead) != 0;   // Dead = dead or a released spirit (UnitIsDeadOrGhost); Ghost = released
            }
            if ((mask & GufCurHp) != 0) v.Health = (int)p.U32();
            if ((mask & GufMaxHp) != 0) v.MaxHealth = (int)p.U32();
            if ((mask & GufPowerType) != 0) { int pt = p.U8(); v.Power = pt < 7 && pt != 5 ? (PowerType)pt : PowerType.None; }
            else if (full && (mask & GufCurPower) != 0) v.Power = PowerType.Mana;   // _FULL leaves POWER_TYPE out for mana
            if ((mask & GufCurPower) != 0) v.PowerValue = p.U16();
            if ((mask & GufMaxPower) != 0) v.PowerMax = p.U16();
            if ((mask & GufLevel) != 0) v.Level = p.U16();
            if ((mask & GufZone) != 0) { v.ZoneId = p.U16(); v.MapId = GrpMapOfZone(v.ZoneId, v.MapId); }
            if ((mask & GufPosition) != 0)
            {
                // "*data << uint16(player->GetPositionX())": the float's integer part truncated to 16 bits - read as signed (a coordinate
                // below 0 arrives as its two's complement on the x86 builds AC ships).
                short x = (short)p.U16(), y = (short)p.U16();
                v.Pos = new WowPos(x, y, 0f, 0f);
            }
            if ((mask & GufAuras) != 0) GrpSkipAuras(p);
            if ((mask & GufPetGuid) != 0) p.U64();
            if ((mask & GufPetName) != 0) p.CString();
            if ((mask & GufPetModel) != 0) p.U16();
            if ((mask & GufPetCurHp) != 0) p.U32();
            if ((mask & GufPetMaxHp) != 0) p.U32();
            if ((mask & GufPetPowerType) != 0) p.U8();
            if ((mask & GufPetCurPower) != 0) p.U16();
            if ((mask & GufPetMaxPower) != 0) p.U16();
            if ((mask & GufPetAuras) != 0) GrpSkipAuras(p);
            if ((mask & GufVehicleSeat) != 0) p.U32();
            if (!v.Online) { v.Dead = false; v.Ghost = false; v.MapId = -1; }
            GrpRefresh(v);
            int n;
            _grpStatsCount.TryGetValue(guid, out n);
            _grpStatsCount[guid] = ++n;
            if (n == 1 || n % GrpStatsLogEvery == 0)
                WowLog.Info("Online group: " + (full ? "SMSG_PARTY_MEMBER_STATS_FULL" : "SMSG_PARTY_MEMBER_STATS") + " #" + n + " for " + (v.Name ?? GrpNameOf(guid)) +
                            " (mask 0x" + mask.ToString("X") + "): " + (v.Online ? "online" : "offline") + (v.Ghost ? ", ghost" : v.Dead ? ", dead" : "") +
                            ", health " + v.Health + "/" + v.MaxHealth + ", " + v.Power + " " + v.PowerValue + "/" + v.PowerMax + ", level " + v.Level +
                            ", zone " + v.ZoneId + " (map " + v.MapId + "), at " + v.Pos.X.ToString("0") + ", " + v.Pos.Y.ToString("0") + (_player.Group.Contains(v) ? "" : " (not listed yet)"));
        }

        /// <summary>The aura block of the stats packets: uint64 mask, per set bit uint32 spell id + uint8 (flags / 1).</summary>
        static void GrpSkipAuras(WowPacket p)
        {
            ulong m = p.U64();
            for (int i = 0; i < 64; i++) if ((m & (1UL << i)) != 0) { p.U32(); p.U8(); }
        }

        /// <summary>
        /// SMSG_PARTY_COMMAND_RESULT (WorldSession::SendPartyResult, GroupHandler.cpp:53-62): uint32 PartyOperation, member name, uint32
        /// PartyResult, uint32 val (LFD kick timer seconds). The client's text for each result is the GlobalStrings key of the same name.
        /// </summary>
        void GrpOnCommandResult(WowPacket p)
        {
            int op = (int)p.U32();
            string name = p.CString() ?? "";
            int res = (int)p.U32();
            uint val = p.Remaining >= 4 ? p.U32() : 0u;
            if (op == PartyOpInvite && res == 0) _grpInviteSentOk = true;
            string text = GrpResultText(op, res, name, val);
            WowLog.Info("Online group: SMSG_PARTY_COMMAND_RESULT op " + GrpOpName(op) + " '" + name + "' result " + res + (val != 0 ? " val " + val : "") + " -> " + (text ?? "(no text)"));
            if (text != null) GrpText(text);
        }

        /// <summary>WoW's text for (operation, result); null = none shown. Strings: GlobalStrings.lua (enUS 3.3.5a), line in the comment.</summary>
        static string GrpResultText(int op, int res, string name, uint val)
        {
            switch (res)
            {
                case 0:   // ERR_PARTY_RESULT_OK
                    if (op == PartyOpInvite) return "You have invited " + name + " to join your group.";   // ERR_INVITE_PLAYER_S (GlobalStrings.lua:3152)
                    if (op == PartyOpLeave) return "You leave the group.";                                 // ERR_LEFT_GROUP_YOU (:3180)
                    return null;
                case 1: return "Cannot find player '" + name + "'.";                                      // ERR_BAD_PLAYER_NAME_S (:2948)
                case 2: return name + " is not in your party.";                                          // ERR_TARGET_NOT_IN_GROUP_S (:3512)
                case 3: return name + " is not in your instance.";                                       // ERR_TARGET_NOT_IN_INSTANCE_S (:3513)
                case 4: return "Your party is full.";                                                     // ERR_GROUP_FULL (:3070)
                case 5: return name + " is already in a group.";                                         // ERR_ALREADY_IN_GROUP_S (:2869)
                case 6: return "You aren't in a party.";                                                  // ERR_NOT_IN_GROUP (:3311)
                case 7: return "You are not the party leader.";                                           // ERR_NOT_LEADER (:3313)
                case 8: return "Target is unfriendly.";                                                   // ERR_PLAYER_WRONG_FACTION (:3396)
                case 9: return name + " is ignoring you.";                                                // ERR_IGNORING_YOU_S (:3132)
                case 12: return "You already have a pending match.";                                      // ERR_LFG_PENDING (:3201)
                case 13: return "Trial accounts cannot invite characters into groups.";                   // ERR_INVITE_RESTRICTED (:3153)
                case 14: return op == PartyOpSwap ? "Players in raid combat cannot change raid subgroups"   // ERR_GROUP_SWAP_FAILED (:3075)
                                                  : "You cannot invite a player in raid combat";            // ERR_INVITE_IN_COMBAT (:3149) - SharedDefines.h:3973
                case 15: return "You cannot invite players from that realm";                              // ERR_INVITE_UNKNOWN_REALM (:3155)
                case 16: return "Could not create a party";                                               // ERR_INVITE_NO_PARTY_SERVER (:3150)
                case 17: return "Cannot invite additional players until the party is formed";            // ERR_INVITE_PARTY_BUSY (:3151)
                case 18: return "There were multiple players in the party with that name, please include realm name";   // ERR_PARTY_TARGET_AMBIGUOUS (:3356)
                case 19: return name + " is locked to another instance.";                                // ERR_PARTY_LFG_INVITE_RAID_LOCKED (:3354)
                case 20: return "You can not initiate any more party kicks.";                             // ERR_PARTY_LFG_BOOT_LIMIT (:3348)
                case 21: return "You must wait " + GrpSeconds(val) + " before initiating another player kick.";   // ERR_PARTY_LFG_BOOT_COOLDOWN_S (:3344); the time format is ours
                case 22: return "Another kick vote is already in progress.";                              // ERR_PARTY_LFG_BOOT_IN_PROGRESS (:3347)
                case 23: return "There are not enough players in the group to initate a vote.";          // ERR_PARTY_LFG_BOOT_TOO_FEW_PLAYERS (:3351)
                case 24: return "That player can not be kicked for another " + GrpSeconds(val) + ".";       // ERR_PARTY_LFG_BOOT_NOT_ELIGIBLE_S (:3350); the time format is ours
                case 25: return "Character too low level for raid.";                                      // ERR_RAID_DISALLOWED_BY_LEVEL (:3449)
                case 26: return "Players cannot be kicked while in combat, or shortly after combat.";     // ERR_PARTY_LFG_BOOT_IN_COMBAT (:3346)
                case 27: return "A reason is needed to vote to kick this player.";                         // ERR_VOTE_KICK_REASON_NEEDED: no GlobalStrings entry in 3.3.5a - ours
                case 28: return "Players cannot be kicked after the instance is complete.";               // ERR_PARTY_LFG_BOOT_DUNGEON_COMPLETE (:3345)
                case 29: return "Players cannot be kicked during loot rolls.";                            // ERR_PARTY_LFG_BOOT_LOOT_ROLLS (:3349)
                case 30: return "You cannot teleport out of the dungeon while in combat.";                // ERR_PARTY_LFG_TELEPORT_IN_COMBAT (:3355)
                default: return "The group command failed (result " + res + ").";                         // ours: a PartyResult 3.3.5a does not list
            }
        }

        static string GrpSeconds(uint s) { return s + (s == 1 ? " second" : " seconds"); }   // ours

        static string GrpOpName(int op)
        {
            switch (op) { case PartyOpInvite: return "INVITE"; case PartyOpUninvite: return "UNINVITE"; case PartyOpLeave: return "LEAVE"; case PartyOpSwap: return "SWAP"; default: return op.ToString(); }
        }

        /// <summary>SMSG_GROUP_DECLINE (HandleGroupDeclineOpcode, GroupHandler.cpp:348-350): the invitee's name, sent to the leader.</summary>
        void GrpOnDecline(WowPacket p)
        {
            string name = p.CString();
            _grpInviteSentOk = false;
            WowLog.Info("Online group: SMSG_GROUP_DECLINE from " + name);
            GrpText(name + " declines your group invitation.");   // ERR_DECLINE_GROUP_S (GlobalStrings.lua:3022)
        }

        /// <summary>SMSG_GROUP_DESTROYED (Group::ForcedDisband, Group.cpp:865-869): empty; the empty SMSG_GROUP_LIST follows.</summary>
        void GrpOnDestroyed(WowPacket p)
        {
            WowLog.Info("Online group: SMSG_GROUP_DESTROYED");
            _grpInviteSentOk = false;
            GrpText("Your group has been disbanded.");   // ERR_GROUP_DISBANDED (GlobalStrings.lua:3069)
        }

        /// <summary>SMSG_GROUP_UNINVITE (Group::RemoveMember with GROUP_REMOVEMETHOD_KICK, Group.cpp:673-677): empty; the empty list follows.</summary>
        void GrpOnUninvite(WowPacket p)
        {
            WowLog.Info("Online group: SMSG_GROUP_UNINVITE (removed from the group)");
            GrpText("You have been removed from the group.");   // ERR_UNINVITE_YOU (GlobalStrings.lua:3564)
        }

        /// <summary>SMSG_GROUP_SET_LEADER (Group::ChangeLeader, Group.cpp:812-814): the new leader's name; SendUpdate's list carries the guid.</summary>
        void GrpOnSetLeader(WowPacket p)
        {
            string name = p.CString();
            bool me = string.Equals(name, _player.Name, StringComparison.OrdinalIgnoreCase);
            WowLog.Info("Online group: SMSG_GROUP_SET_LEADER " + name + (me ? " (me)" : ""));
            GrpText(me ? "You are now the group leader." : name + " is now the group leader.");   // ERR_NEW_LEADER_YOU / ERR_NEW_LEADER_S (GlobalStrings.lua:3296 / :3295)
            GroupMemberView v = null;
            for (int i = 0; i < _player.Group.Count; i++) if (string.Equals(_player.Group[i].Name, name, StringComparison.OrdinalIgnoreCase)) v = _player.Group[i];
            ulong leader = me ? _player.Guid : v != null ? v.Guid : _player.GroupLeaderGuid;
            if (leader == _player.GroupLeaderGuid) return;
            _player.GroupLeaderGuid = leader;
            for (int i = 0; i < _player.Group.Count; i++) _player.Group[i].Leader = _player.Group[i].Guid == leader;
            _events.Enqueue(new GameEvent { Type = GameEventType.GroupChanged, Amount = _player.Group.Count });
        }

        /// <summary>SMSG_NAME_QUERY_RESPONSE (NameQueryResponse::Write, QueryPackets.cpp:25-45): packed guid, uint8 NameUnknown, [name,
        /// realm name, uint8 race, uint8 sex, uint8 class, ...]. Read only for the race / class of group members (Objects.cs owns names).</summary>
        void GrpOnNameQuery(WowPacket p)
        {
            ulong guid = p.PackedGuid();
            if (p.U8() != 0) return;
            if (!_grpKnown.ContainsKey(guid)) return;
            p.CString(); p.CString();
            if (p.Remaining < 3) return;
            int race = p.U8(); p.U8(); int cls = p.U8();
            _grpNameRace[guid] = race;
            _grpNameClass[guid] = cls;
            GroupMemberView v;
            if (_grpKnown.TryGetValue(guid, out v)) GrpRefresh(v);
        }

        // ------------------------------------------------------------------ helpers

        GroupMemberView GrpView(ulong guid)
        {
            GroupMemberView v;
            if (!_grpKnown.TryGetValue(guid, out v))
            {
                v = new GroupMemberView { Guid = guid };
                _grpKnown[guid] = v;
            }
            return v;
        }

        /// <summary>A member's live values: from the object while we can see it (the client's rule: in range = the unit's own fields),
        /// else what the stats said; race / class from the object or the name query; Somnium marking from MarkSomniumPlayer.</summary>
        void GrpRefresh(GroupMemberView v)
        {
            OnlineObject o;
            EntityState e = _objects.TryGetValue(v.Guid, out o) ? o.State : null;
            if (e != null && e.Kind == EntityKind.Player)
            {
                v.Online = true;
                v.Health = e.Health; v.MaxHealth = e.MaxHealth;
                v.Power = e.Power; v.PowerValue = e.PowerValue; v.PowerMax = e.PowerMax;
                if (e.Level > 0) v.Level = e.Level;
                v.Pos = e.Pos;
                v.MapId = _mapId;
                v.Ghost = (o.U32(UF.PLAYER_FLAGS) & PlayerFlagGhost) != 0;   // PlayerFlagGhost 0x10 (Objects.cs, PLAYER_FLAGS_GHOST)
                v.Dead = v.Ghost || e.Dead;
                if (e.Race > 0) v.Race = e.Race;
                int cls = o.Byte(UF.UNIT_FIELD_BYTES_0, 1);
                if (cls > 0) v.Class = (WowClass)cls;
                if (string.IsNullOrEmpty(v.Name) && !string.IsNullOrEmpty(e.Name)) v.Name = e.Name;
            }
            int r, c;
            if (v.Race <= 0 && _grpNameRace.TryGetValue(v.Guid, out r)) v.Race = r;
            if ((int)v.Class <= 0 && _grpNameClass.TryGetValue(v.Guid, out c)) v.Class = (WowClass)c;
            if ((v.Race <= 0 || (int)v.Class <= 0) && _world != null && _grpNameAsked.Add(v.Guid))
                _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(v.Guid));   // NameQuery::Read: ObjectGuid (uint64)
            v.Leader = v.Guid != 0 && v.Guid == _player.GroupLeaderGuid;
            v.IsSomniumPlayer = _somniumPlayers.Contains(v.Guid) || (e != null && e.IsSomniumPlayer);
        }

        /// <summary>AreaTable.dbc ContinentID (DbcFields.AreaTable.ContinentID, = AC AreaTableEntry mapid) of the member's zone; the
        /// fallback when the file is not there is the last known map.</summary>
        int GrpMapOfZone(int zoneId, int fallback)
        {
            if (zoneId <= 0) return fallback;
            if (_grpAreas == null && !_grpAreasTried)
            {
                _grpAreasTried = true;
                try { _grpAreas = _files != null ? Dbc.Load(_files, "AreaTable") : null; }
                catch (Exception ex) { WowLog.Warn("Online group: AreaTable.dbc not read (" + ex.Message + ") - members' maps unknown outside visual range"); }
            }
            if (_grpAreas == null) return fallback;
            int row = _grpAreas.FindRow(zoneId);
            return row >= 0 ? _grpAreas.GetInt(row, DbcFields.AreaTable.ContinentID) : fallback;
        }

        /// <summary>A WoW group text: a system line for the chat log (ChatMessage, ChatKind.System) and, unless hud is false, the HUD's yellow line (Info).</summary>
        void GrpText(string text, bool hud = true)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)ChatKind.System, Text = text });
            if (hud) _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text });
        }

        void GrpOdd(string opcode, string what)
        {
            if (_grpOddLogged.Add(opcode)) WowLog.Warn("Online group: odd " + opcode + ": " + what + " (logged once)");
        }

        string GrpNameOf(ulong guid)
        {
            GroupMemberView v;
            if (_grpKnown.TryGetValue(guid, out v) && !string.IsNullOrEmpty(v.Name)) return v.Name;
            return "0x" + guid.ToString("X");
        }

        static string GrpNames(List<GroupMemberView> g)
        {
            if (g.Count == 0) return "";
            var sb = new System.Text.StringBuilder(" (");
            for (int i = 0; i < g.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(g[i].Name ?? "?").Append(g[i].Online ? "" : " offline");
            }
            return sb.Append(")").ToString();
        }
    }
}
