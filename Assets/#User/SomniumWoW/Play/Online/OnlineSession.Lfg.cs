// iteration 11 - owner queues (proposal-interact.md P3, section 6.2.8): the Dungeon Finder online (wave-0 stub filled).
// Protocol = AzerothCore master: LFGHandler.cpp (P/acsrc11/interact) :30-48 (lock blocks), :152-263 (player / party info), :300-381 (updates),
// :383-454 (role chosen / role check / join result), :456-611 (queue status, reward, boot, proposal), :613-640; LFGPackets.cpp:20-34 (join);
// LFG.h:37-102, LFGMgr.h:49-132 (enums, timers); LFGMgr.cpp:1955-1990 (a found group: PROPOSAL_UPDATE 2 -> GROUP_FOUND -> REMOVED_FROM_QUEUE).
// Texts = GlobalStrings.lua of the 3.3.5a install (GS line in comments). InitLfg / TickLfg are partial methods (OnlineSession.Hooks11.cs)
// called by interact's InitInteract / TickInteract.
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const float LfgRoleCheckSeconds = 45f;      // LFG_TIME_ROLECHECK (LFGMgr.h:49)
        const float LfgProposalSeconds = 40f;       // LFG_TIME_PROPOSAL (LFGMgr.h:51)
        const int LfgExpansion = 2;                 // ours: the realm's 3.3.5 accounts are Wrath of the Lich King (AuthClient knows no expansion value)
        const int LfgMaxShownLevelDiff = 15;        // LFD_MAX_SHOWN_LEVEL_DIFF (LFDFrame.lua:18)

        // LFGDungeons.dbc (measured 2026-10-03 with InteractDbcFacts: 49 fields): 1 Name_lang, 18 MinLevel, 19 MaxLevel, 20 TargetLevel (recLevel),
        // 23 MapID, 24 Difficulty, 26 TypeID (1 dungeon, 2 raid, 6 random), 29 ExpansionLevel, 30 OrderIndex, 31 GroupID.
        // LFGDungeonGroup.dbc (measured: 21 fields): 1 Name_lang (1 "Classic Dungeons" ... 5 "Wrath of the Lich King Heroic"), 18 OrderIndex.
        const int LfDbcName = 1, LfDbcMin = 18, LfDbcMax = 19, LfDbcRec = 20, LfDbcMap = 23, LfDbcDiff = 24, LfDbcType = 26, LfDbcExp = 29,
                  LfDbcOrder = 30, LfDbcGroup = 31, LfGroupName = 1, LfGroupOrder = 18;

        readonly LfgView _lfg = new LfgView();
        readonly Dictionary<int, int> _lfLocks = new Dictionary<int, int>();   // dungeon id -> LfgLockStatusType (LFG.h:79-93)
        DbcTable _lfDbc, _lfGroupDbc;
        bool _lfDbcTried, _lfAskedAtLogin, _lfJoinSaid, _lfLeftSaid, _lfBootWasActive;
        int _lfOfferEntry, _lfOfferVersion;
        ulong _lfDeclinedBy;

        /// <summary>Never null.</summary>
        public LfgView Lfg { get { return _lfg; } }

        /// <summary>SMSG_LFG_OFFER_CONTINUE's dungeon entry (StaticPopup LFG_OFFER_CONTINUE), 0 = none. Not in the frozen contract (LfgView has no
        /// field for it) - the UI reads it from OnlineSession; LfgOfferVersion goes +1 on every offer.</summary>
        public int LfgOfferContinueEntry { get { return _lfOfferEntry; } }
        public int LfgOfferVersion { get { return _lfOfferVersion; } }
        public void LfgClearOffer() { _lfOfferEntry = 0; }

        partial void InitLfg()
        {
            QuOn(Op.SMSG_LFG_PLAYER_INFO, LfOnPlayerInfo);
            QuOn(Op.SMSG_LFG_PARTY_INFO, LfOnPartyInfo);
            QuOn(Op.SMSG_LFG_JOIN_RESULT, LfOnJoinResult);
            QuOn(Op.SMSG_LFG_UPDATE_PLAYER, p => LfOnUpdate(p, false));
            QuOn(Op.SMSG_LFG_UPDATE_PARTY, p => LfOnUpdate(p, true));
            QuOn(Op.SMSG_LFG_QUEUE_STATUS, LfOnQueueStatus);
            QuOn(Op.SMSG_LFG_ROLE_CHECK_UPDATE, LfOnRoleCheck);
            QuOn(Op.SMSG_LFG_ROLE_CHOSEN, LfOnRoleChosen);
            QuOn(Op.SMSG_LFG_PROPOSAL_UPDATE, LfOnProposal);
            QuOn(Op.SMSG_LFG_BOOT_PROPOSAL_UPDATE, LfOnBoot);
            QuOn(Op.SMSG_LFG_TELEPORT_DENIED, LfOnTeleportDenied);
            QuOn(Op.SMSG_LFG_PLAYER_REWARD, LfOnReward);
            QuOn(Op.SMSG_LFG_OFFER_CONTINUE, LfOnOfferContinue);
            QuOn(Op.SMSG_LFG_DISABLED, LfOnDisabled);
            WowLog.Info("Online dungeon finder: lock info, queue, role check, proposal, teleport in / out, kick votes and rewards are handled (iteration 11).");
        }

        // ------------------------------------------------------------------ requests (IWowInteractions)

        /// <summary>Opening the Dungeon Finder: CMSG_LFD_PLAYER_LOCK_INFO_REQUEST (empty) + CMSG_LFD_PARTY_LOCK_INFO_REQUEST in a group (LFGHandler.cpp:152-263).</summary>
        public void RequestLfgInfo()
        {
            if (_world == null) return;
            QuSend(Op.CMSG_LFD_PLAYER_LOCK_INFO_REQUEST, null);
            if (_player.Group.Count > 0) QuSend(Op.CMSG_LFD_PARTY_LOCK_INFO_REQUEST, null);
        }

        /// <summary>Find Group: CMSG_LFG_JOIN (LFGPackets.cpp:20-34: uint32 roles, uint8 noPartialClear, uint8 achievements, uint8 n, n x uint32 entry,
        /// uint8 3, 3 x uint8 needs, string comment). Solo or the group leader (LFGHandler.cpp:52-55).</summary>
        public void JoinLfg(int roles, IList<int> dungeonEntries)
        {
            if (_world == null) return;
            if ((roles & (LfgView.RoleTank | LfgView.RoleHealer | LfgView.RoleDamage)) == 0) { Error("You must select at least one role."); return; }   // ERR_LFG_NO_ROLES_SELECTED (:3196)
            if (dungeonEntries == null || dungeonEntries.Count == 0) { Error("You did not select any valid slots."); return; }                     // ERR_LFG_NO_SLOTS_SELECTED (:3199)
            if (_player.Group.Count > 0 && _player.GroupLeaderGuid != _player.Guid && !Raid.IsLfgGroup) { Error("You are not the party leader."); return; }   // ERR_NOT_LEADER (:3313)
            int n = Math.Min(dungeonEntries.Count, 255);
            var w = new PacketWriter().U32((uint)roles).U8(0).U8(0).U8(n);
            for (int i = 0; i < n; i++) w.U32((uint)dungeonEntries[i]);
            w.U8(3).U8(0).U8(0).U8(0).CString("");
            QuSend(Op.CMSG_LFG_JOIN, w);
            _lfg.MyRoles = roles;
            _lfg.Version++;
            WowLog.Info("Online dungeon finder: join sent (roles 0x" + roles.ToString("X") + ", " + n + " dungeon(s)).");
        }

        /// <summary>Leave Queue: CMSG_LFG_LEAVE (empty; leader only, LFGHandler.cpp:78-93).</summary>
        public void LeaveLfg() { QuSend(Op.CMSG_LFG_LEAVE, null); }

        /// <summary>The role-check popup: CMSG_LFG_SET_ROLES (uint8 roles, 0 = decline; LFGHandler.cpp:106-120).</summary>
        public void SetLfgRoles(int roles)
        {
            if (_world == null) return;
            QuSend(Op.CMSG_LFG_SET_ROLES, new PacketWriter().U8(roles & 0xFF));
            _lfg.RoleCheckAsked = false;
            if (roles != 0) _lfg.MyRoles = roles;
            _lfg.Version++;
        }

        /// <summary>Enter Dungeon / Leave Queue: CMSG_LFG_PROPOSAL_RESULT (uint32 proposal id, uint8 accept; LFGHandler.cpp:95-104).</summary>
        public void AnswerLfgProposal(bool accept)
        {
            if (_world == null || _lfg.ProposalId == 0) return;
            QuSend(Op.CMSG_LFG_PROPOSAL_RESULT, new PacketWriter().U32((uint)_lfg.ProposalId).U8(accept ? 1 : 0));
            _lfg.ProposalAsked = false;
            _lfg.Version++;
        }

        /// <summary>Teleport Out Of / To Dungeon: CMSG_LFG_TELEPORT (uint8 out; LFGHandler.cpp:143-150).</summary>
        public void LfgTeleport(bool outOfDungeon) { QuSend(Op.CMSG_LFG_TELEPORT, new PacketWriter().U8(outOfDungeon ? 1 : 0)); }

        /// <summary>StaticPopup VOTE_BOOT_PLAYER: CMSG_LFG_SET_BOOT_VOTE (uint8 agree; LFGHandler.cpp:133-141).</summary>
        public void LfgBootVote(bool agree)
        {
            if (_world == null) return;
            QuSend(Op.CMSG_LFG_SET_BOOT_VOTE, new PacketWriter().U8(agree ? 1 : 0));
            _lfg.BootVoted = true;
            _lfg.Version++;
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>SMSG_LFG_PLAYER_INFO (LFGHandler.cpp:152-228): uint8 n random dungeons, n x {uint32 entry, uint8 done, uint32 money, uint32 xp,
        /// uint32 0, uint32 0, uint8 k, k x (uint32 item, uint32 display, uint32 count)}, then the lock block (uint32 m, m x (uint32 entry, uint32 status)).</summary>
        void LfOnPlayerInfo(WowPacket p)
        {
            int n = p.U8();
            _lfg.RandomDungeons.Clear();
            for (int i = 0; i < n; i++)
            {
                int entry = (int)p.U32();
                LfgDungeonView d = LfView(entry);
                d.Done = p.U8() != 0;
                d.RewardMoney = (int)p.U32();
                d.RewardXp = (int)p.U32();
                p.U32(); p.U32();
                int k = p.U8();
                for (int j = 0; j < k; j++)
                {
                    int item = (int)p.U32();
                    p.U32();
                    int count = (int)p.U32();
                    d.RewardItems.Add(ItemInfoView(item, Math.Max(1, count)));
                }
                _lfg.RandomDungeons.Add(d);
            }
            _lfLocks.Clear();
            int m = p.Remaining >= 4 ? (int)p.U32() : 0;
            for (int i = 0; i < m && p.Remaining >= 8; i++)
            {
                int entry = (int)p.U32();
                int status = (int)p.U32();
                _lfLocks[entry & 0xFFFFFF] = status;
            }
            for (int i = 0; i < _lfg.RandomDungeons.Count; i++) LfApplyLock(_lfg.RandomDungeons[i]);
            LfBuildSpecific();
            _lfg.InfoLoaded = true;
            _lfg.Version++;
            WowLog.Info("Online dungeon finder: lock info (" + _lfg.RandomDungeons.Count + " random, " + _lfg.Dungeons.Count + " specific dungeon(s), " + m + " lock(s)).");
        }

        /// <summary>SMSG_LFG_PARTY_INFO (LFGHandler.cpp:230-263, :40-48): uint8 n, n x (uint64 guid, uint32 m, m x (uint32 entry, uint32 status)).
        /// The specific list greys a dungeon a member cannot enter (ours: any member's lock counts).</summary>
        void LfOnPartyInfo(WowPacket p)
        {
            int n = p.U8();
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                p.U64();
                int m = (int)p.U32();
                for (int j = 0; j < m; j++)
                {
                    int id = (int)(p.U32() & 0xFFFFFF);
                    int status = (int)p.U32();
                    if (!_lfLocks.ContainsKey(id)) { _lfLocks[id] = status; changed = true; }
                }
            }
            if (!changed) return;
            for (int i = 0; i < _lfg.Dungeons.Count; i++) LfApplyLock(_lfg.Dungeons[i]);
            for (int i = 0; i < _lfg.RandomDungeons.Count; i++) LfApplyLock(_lfg.RandomDungeons[i]);
            _lfg.Version++;
        }

        /// <summary>SMSG_LFG_JOIN_RESULT (LFGHandler.cpp:441-454): uint32 result (LfgJoinResult LFGMgr.h:100-121), uint32 state, [party lock block].</summary>
        void LfOnJoinResult(WowPacket p)
        {
            int result = (int)p.U32();
            p.U32();
            WowLog.Info("Online dungeon finder: join result " + result + ".");
            string text = LfJoinText(result);
            if (text != null) Error(text);
        }

        internal static string LfJoinText(int result)
        {
            switch (result)
            {
                case 0: return null;                                                                              // LFG_JOIN_OK: no text
                case 1: return "The Role Check has failed.";                                                       // ERR_LFG_ROLE_CHECK_FAILED (:3209)
                case 2: return "Your group is already full.";                                                      // ERR_LFG_GROUP_FULL (:3186)
                case 4: return "Internal LFG Error.";                                                              // ERR_LFG_NO_LFG_OBJECT (:3195)
                case 5: return "You do not meet the requirements for the chosen dungeons.";                        // ERR_LFG_NO_SLOTS_PLAYER (:3198)
                case 6: return "One or more party members do not meet the requirements for the chosen dungeons.";  // ERR_LFG_NO_SLOTS_PARTY (:3197)
                case 7: return "You cannot mix dungeons, raids, and random when picking dungeons.";                 // ERR_LFG_MISMATCHED_SLOTS (:3194)
                case 8: return "The dungeon you chose does not support players from multiple realms.";             // ERR_LFG_PARTY_PLAYERS_FROM_DIFFERENT_REALMS (:3200)
                case 9: return "One or more party members are pending invites or disconnected.";                   // ERR_LFG_MEMBERS_NOT_PRESENT (:3193)
                case 10: return "Could not retrieve information about some party members.";                       // ERR_LFG_GET_INFO_TIMEOUT (:3185)
                case 11: return "One or more dungeons was not valid.";                                             // ERR_LFG_INVALID_SLOT (:3187)
                case 12: return "You can not queue for dungeons until your deserter debuff wears off.";            // ERR_LFG_DESERTER_PLAYER (:3184)
                case 13: return "One or more party members has a deserter debuff.";                                // ERR_LFG_DESERTER_PARTY (:3183)
                case 14: return "You can not queue for random dungeons while on random dungeon cooldown.";         // ERR_LFG_RANDOM_COOLDOWN_PLAYER (:3207)
                case 15: return "One or more party members are on random dungeon cooldown.";                       // ERR_LFG_RANDOM_COOLDOWN_PARTY (:3206)
                case 16: return "You can not enter dungeons with more than 5 party members.";                      // ERR_LFG_TOO_MANY_MEMBERS (:3213)
                case 17: return "You cannot queue for a dungeon while using battlegrounds or arenas.";             // ERR_LFG_CANT_USE_DUNGEONS (:3182)
                default: return null;                                                                             // 3 / 18: no client reaction (LFGMgr.h:102)
            }
        }

        /// <summary>SMSG_LFG_UPDATE_PLAYER (LFGHandler.cpp:302-337): uint8 type, uint8 extra, [uint8 queued, uint8 0, uint8 0, uint8 n, n x uint32, string];
        /// SMSG_LFG_UPDATE_PARTY (:339-381): uint8 type, uint8 extra, [uint8 join, uint8 queued, uint8 0, uint8 0, 3 x uint8 0, uint8 n, n x uint32, string].
        /// Types LfgUpdateType (LFG.h:46-64).</summary>
        void LfOnUpdate(WowPacket p, bool party)
        {
            int type = p.U8();
            bool extra = p.U8() != 0;
            bool join = false, queued = false;
            var entries = new List<int>();
            if (extra)
            {
                if (party) join = p.U8() != 0;
                queued = p.U8() != 0;
                p.U8(); p.U8();
                if (party) { p.U8(); p.U8(); p.U8(); }
                int n = p.U8();
                for (int i = 0; i < n && p.Remaining >= 4; i++) entries.Add((int)p.U32());
            }
            LfgStatus was = _lfg.Status;
            switch (type)
            {
                case 5:    // JOIN_QUEUE
                case 12:   // ADDED_TO_QUEUE
                    _lfg.Status = LfgStatus.Queued;
                    if (entries.Count > 0) { _lfg.Queued.Clear(); _lfg.Queued.AddRange(entries); }
                    if (!_lfJoinSaid) { _lfJoinSaid = true; _lfLeftSaid = false; GrpText("You are now queued in the Dungeon Finder."); }   // ERR_LFG_JOINED_QUEUE (:3189)
                    _lfg.RoleCheckAsked = false;
                    break;
                case 13:   // PROPOSAL_BEGIN
                    _lfg.Status = LfgStatus.Proposal;
                    break;
                case 10:   // GROUP_FOUND
                    _lfg.Status = LfgStatus.InDungeon;
                    _lfg.Queued.Clear();
                    break;
                case 7:    // REMOVED_FROM_QUEUE (also right after GROUP_FOUND - LFGMgr.cpp:1982)
                    if (_lfg.Status != LfgStatus.InDungeon && _lfg.Status != LfgStatus.Finished)
                    {
                        _lfg.Status = LfgStatus.None;
                        if (!_lfLeftSaid && _lfJoinSaid) { _lfLeftSaid = true; GrpText("You are no longer queued in the Dungeon Finder."); }   // ERR_LFG_LEFT_QUEUE (:3192)
                    }
                    _lfg.Queued.Clear();
                    _lfJoinSaid = false;
                    break;
                case 8:    // PROPOSAL_FAILED: back to the front of the queue
                    Error("Someone has declined the dungeon invite. You have been returned to the front of the dungeon queue.");   // ERR_LFG_PROPOSAL_FAILED (:3205)
                    _lfg.Status = LfgStatus.Queued;
                    _lfg.ProposalAsked = false;
                    break;
                case 9:    // PROPOSAL_DECLINED
                    Error(party ? "You have been removed from the Dungeon queue because someone in your party did not accept the invitation."   // _PARTY (:3203)
                                : "You have been removed from the Dungeon queue because you did not accept the invitation.");                      // _SELF (:3204)
                    _lfg.Status = LfgStatus.None;
                    _lfg.ProposalAsked = false;
                    _lfJoinSaid = false;
                    break;
                case 4:    // ROLECHECK_ABORTED
                    Error("Your group leader has cancelled the Role Check.");   // ERR_LFG_ROLE_CHECK_ABORTED (:3208)
                    LfEndRoleCheck();
                    break;
                case 6:    // ROLECHECK_FAILED
                    Error("The Role Check has failed.");                        // ERR_LFG_ROLE_CHECK_FAILED (:3209)
                    LfEndRoleCheck();
                    break;
                case 1:    // LEADER_UNK1 (at group leave)
                case 16:   // GROUP_DISBAND_UNK16
                    if (_lfg.Status == LfgStatus.InDungeon || _lfg.Status == LfgStatus.Finished) _lfg.Status = LfgStatus.None;
                    break;
                case 14:   // UPDATE_STATUS (CMSG_LFG_GET_STATUS / login): only the packet with the extra block says anything
                    if (!extra) break;
                    if (queued) { _lfg.Status = LfgStatus.Queued; _lfJoinSaid = true; _lfg.Queued.Clear(); _lfg.Queued.AddRange(entries); }
                    else if (party && join && _lfg.Status == LfgStatus.None) _lfg.Status = LfgStatus.InDungeon;
                    break;
            }
            if (_lfg.Status != LfgStatus.Queued) _lfg.QueuedSeconds = was == LfgStatus.Queued ? 0f : _lfg.QueuedSeconds;
            _lfg.Version++;
            if (_lfg.Status != was) WowLog.Info("Online dungeon finder: " + (party ? "party" : "player") + " update " + type + ": " + was + " -> " + _lfg.Status + ".");
        }

        void LfEndRoleCheck()
        {
            _lfg.RoleCheckAsked = false;
            _lfg.RoleCheck.Clear();
            if (_lfg.Status == LfgStatus.RoleCheck) _lfg.Status = LfgStatus.None;
            _lfJoinSaid = false;
        }

        /// <summary>SMSG_LFG_QUEUE_STATUS (LFGHandler.cpp:456-473): uint32 dungeon, int32 avg wait, int32 wait, int32 tank, int32 healer, int32 dps (s),
        /// uint8 tanks / healers / dps needed, uint32 queued s.</summary>
        void LfOnQueueStatus(WowPacket p)
        {
            p.U32();
            _lfg.AvgWait = p.I32();
            _lfg.MyWait = p.I32();
            _lfg.TankWait = p.I32();
            _lfg.HealerWait = p.I32();
            _lfg.DamageWait = p.I32();
            _lfg.TanksNeeded = p.U8();
            _lfg.HealersNeeded = p.U8();
            _lfg.DamageNeeded = p.U8();
            _lfg.QueuedSeconds = p.U32();
            if (_lfg.Status == LfgStatus.None) _lfg.Status = LfgStatus.Queued;
            _lfg.Version++;
        }

        /// <summary>SMSG_LFG_ROLE_CHECK_UPDATE (LFGHandler.cpp:394-439): uint32 state (LfgRoleCheckState LFGMgr.h:123-132), uint8 begins, uint8 n,
        /// n x uint32 entry, uint8 m, m x (uint64 guid, uint8 ready, uint32 roles, uint8 level) - the leader first.</summary>
        void LfOnRoleCheck(WowPacket p)
        {
            int state = (int)p.U32();
            bool begins = p.U8() != 0;
            int n = p.U8();
            for (int i = 0; i < n; i++) p.U32();
            int m = p.U8();
            _lfg.RoleCheck.Clear();
            LfgMemberView me = null;
            for (int i = 0; i < m; i++)
            {
                var v = new LfgMemberView();
                v.Guid = p.U64();
                v.Chosen = p.U8() != 0;
                v.Roles = (int)p.U32();
                v.Level = p.U8();
                v.IsMe = v.Guid == _player.Guid;
                v.Name = v.IsMe ? _player.Name : QuName(v.Guid);
                if (v.IsMe) me = v;
                _lfg.RoleCheck.Add(v);
            }
            switch (state)
            {
                case 2:    // INITIALITING
                    if (begins) { GrpText("A role check has been initiated. Your group will be queued when all members have selected a role."); _lfg.RoleCheckSecondsLeft = LfgRoleCheckSeconds; }   // ERR_LFG_ROLE_CHECK_INITIATED (:3212)
                    _lfg.Status = LfgStatus.RoleCheck;
                    _lfg.RoleCheckAsked = me != null && !me.Chosen;
                    break;
                case 1:    // FINISHED: the queue follows (ADDED_TO_QUEUE)
                    _lfg.RoleCheckAsked = false;
                    break;
                case 3: Error("Role Check failed because group member did not respond."); LfEndRoleCheck(); break;   // ERR_LFG_ROLE_CHECK_FAILED_TIMEOUT (:3211)
                case 4: Error("Role Check failed because your group is not viable."); LfEndRoleCheck(); break;       // _FAILED_NOT_VIABLE (:3210)
                case 5: Error("Your group leader has cancelled the Role Check."); LfEndRoleCheck(); break;           // ERR_LFG_ROLE_CHECK_ABORTED (:3208)
                case 6:
                    Error((_lfDeclinedBy != 0 ? (ChatSender(_lfDeclinedBy) ?? "Unknown") : "Unknown") + " has not selected any roles.");   // ERR_LFG_PLAYER_DECLINED_ROLE_CHECK (:3202), UNKNOWN (:7899)
                    LfEndRoleCheck();
                    break;
            }
            _lfg.Version++;
        }

        /// <summary>SMSG_LFG_ROLE_CHOSEN (LFGHandler.cpp:383-392): uint64 guid, uint8 ready, uint32 roles.</summary>
        void LfOnRoleChosen(WowPacket p)
        {
            ulong guid = p.U64();
            bool ready = p.U8() != 0;
            int roles = (int)p.U32();
            for (int i = 0; i < _lfg.RoleCheck.Count; i++)
            {
                LfgMemberView v = _lfg.RoleCheck[i];
                if (v.Guid != guid) continue;
                v.Chosen = ready;
                v.Roles = roles;
            }
            if (guid == _player.Guid) _lfg.RoleCheckAsked = false;
            if (!ready) { _lfDeclinedBy = guid; _lfg.Version++; return; }
            string who = guid == _player.Guid ? _player.Name : (ChatSender(guid) ?? "Unknown");
            GrpText(who + " has chosen: " + LfRoleNames(roles), false);   // LFG_ROLE_CHECK_ROLE_CHOSEN "%s has chosen: %s" (:4739)
            _lfg.Version++;
        }

        /// <summary>The roles as TANK / HEALER / DAMAGER (GlobalStrings.lua:7468 / :4006 / :1906) joined with ", " (ours).</summary>
        internal static string LfRoleNames(int roles)
        {
            var sb = new StringBuilder();
            if ((roles & LfgView.RoleTank) != 0) sb.Append("Tank");
            if ((roles & LfgView.RoleHealer) != 0) { if (sb.Length > 0) sb.Append(", "); sb.Append("Healer"); }
            if ((roles & LfgView.RoleDamage) != 0) { if (sb.Length > 0) sb.Append(", "); sb.Append("Damage"); }
            return sb.ToString();
        }

        /// <summary>SMSG_LFG_PROPOSAL_UPDATE (LFGHandler.cpp:545-611): uint32 entry, uint8 state (0 initiating, 1 failed, 2 success), uint32 id,
        /// uint32 encounters, uint8 silent, uint8 n, n x (uint32 role, uint8 me, uint8 inDungeon, uint8 sameGroup, uint8 answered, uint8 accepted).</summary>
        void LfOnProposal(WowPacket p)
        {
            int entry = (int)p.U32();
            int state = p.U8();
            int id = (int)p.U32();
            p.U32();
            bool silent = p.U8() != 0;
            int n = p.U8();
            bool fresh = id != _lfg.ProposalId;
            _lfg.ProposalMembers.Clear();
            LfgMemberView me = null;
            for (int i = 0; i < n; i++)
            {
                var v = new LfgMemberView();
                v.Roles = (int)p.U32();
                v.IsMe = p.U8() != 0;
                p.U8(); p.U8();
                v.Answered = p.U8() != 0;
                v.Accepted = p.U8() != 0;
                if (v.IsMe) { me = v; v.Guid = _player.Guid; v.Name = _player.Name; }
                _lfg.ProposalMembers.Add(v);
            }
            _lfg.ProposalId = id;
            _lfg.ProposalEntry = entry;
            _lfg.ProposalState = state;
            _lfg.ProposalDungeonName = LfName(entry & 0xFFFFFF);
            if (me != null) _lfg.MyProposalRole = me.Roles;
            if (fresh) _lfg.ProposalSecondsLeft = LfgProposalSeconds;
            _lfg.ProposalAsked = state == 0 && !silent && me != null && !me.Answered;
            if (state == 0 && _lfg.Status != LfgStatus.InDungeon) _lfg.Status = LfgStatus.Proposal;
            if (state == 2)
            {
                _lfg.Status = LfgStatus.InDungeon;
                _lfg.InLfgDungeon = true;
                _lfg.ProposalAsked = false;
            }
            else if (state == 1) _lfg.ProposalAsked = false;
            _lfg.Version++;
            WowLog.Info("Online dungeon finder: proposal " + id + " for " + _lfg.ProposalDungeonName + " state " + state + (silent ? " (silent)" : "") + ", " + n + " member(s).");
        }

        /// <summary>SMSG_LFG_BOOT_PROPOSAL_UPDATE (LFGHandler.cpp:513-543): uint8 inProgress, uint8 didVote, uint8 agree, uint64 victim, uint32 votes,
        /// uint32 agrees, uint32 seconds left, uint32 needed (LFG_GROUP_KICK_VOTES_NEEDED 3), string reason.</summary>
        void LfOnBoot(WowPacket p)
        {
            bool active = p.U8() != 0;
            _lfg.BootVoted = p.U8() != 0;
            p.U8();
            _lfg.BootVictim = p.U64();
            _lfg.BootVotes = (int)p.U32();
            _lfg.BootAgree = (int)p.U32();
            _lfg.BootSecondsLeft = p.U32();
            _lfg.BootNeeded = (int)p.U32();
            _lfg.BootReason = p.Remaining > 0 ? p.CString() : "";
            _lfg.BootVictimName = _lfg.BootVictim == _player.Guid ? _player.Name : (QuName(_lfg.BootVictim) ?? "Unknown");
            _lfg.BootActive = active;
            if (active && _lfg.Status != LfgStatus.None) _lfg.Status = LfgStatus.Boot;
            if (!active && _lfBootWasActive)
            {
                bool passed = _lfg.BootAgree >= _lfg.BootNeeded;
                GrpText((passed ? "The vote to kick %s has passed." : "The vote to kick %s has failed.").Replace("%s", _lfg.BootVictimName));   // ERR_PARTY_LFG_BOOT_VOTE_SUCCEEDED (:3353) / _FAILED (:3352)
                if (_lfg.Status == LfgStatus.Boot) _lfg.Status = LfgStatus.InDungeon;
            }
            _lfBootWasActive = active;
            _lfg.Version++;
        }

        /// <summary>SMSG_LFG_TELEPORT_DENIED (LFGHandler.cpp:635-640): uint32 LfgTeleportError (LFGMgr.h:87-97).</summary>
        void LfOnTeleportDenied(WowPacket p)
        {
            int e = (int)p.U32();
            string text;
            switch (e)
            {
                case 1: text = "You can't do that when you're dead."; break;                       // ERR_PLAYER_DEAD (:3387)
                case 2: text = "You can't do that while jumping or falling"; break;                // ERR_NOT_WHILE_FALLING (:3317)
                case 4: text = "You can't do that while fatigued"; break;                          // ERR_NOT_WHILE_FATIGUED (:3318)
                case 6: text = "You can't use that here."; break;                                  // SPELL_FAILED_NOT_HERE (:7094) - ours for INVALID_LOCATION
                case 8: text = "You cannot teleport out of the dungeon while in combat."; break;   // ERR_PARTY_LFG_TELEPORT_IN_COMBAT (:3355)
                case 5: text = null; break;                                                         // "5 = No client reaction" (LFGMgr.h:89)
                default: text = "You can't do that right now."; break;                             // ERR_CLIENT_LOCKED_OUT (:3008): 7 per LFGMgr.h:89, ours for 3 (vehicle)
            }
            if (text != null) Error(text);
            WowLog.Info("Online dungeon finder: teleport denied (" + e + ").");
        }

        /// <summary>SMSG_LFG_PLAYER_REWARD (LFGHandler.cpp:475-511): uint32 random entry, uint32 done entry, uint8 done, uint32 1, uint32 money,
        /// uint32 xp, uint32 0, uint32 0, uint8 k, k x (item, display, count). The money / items arrive as the usual events.</summary>
        void LfOnReward(WowPacket p)
        {
            int rentry = (int)p.U32();
            int sentry = (int)p.U32();
            _lfg.Status = LfgStatus.Finished;
            _lfg.Version++;
            string name = LfName(sentry & 0xFFFFFF);
            GrpText("Dungeon completed: " + name, false);   // ours (WoW shows its reward frame)
            WowLog.Info("Online dungeon finder: dungeon completed (" + name + ", random entry " + (rentry & 0xFFFFFF) + ").");
        }

        /// <summary>SMSG_LFG_OFFER_CONTINUE (LFGHandler.cpp:625-633): uint32 dungeon entry -> StaticPopup LFG_OFFER_CONTINUE (GlobalStrings.lua:4735).</summary>
        void LfOnOfferContinue(WowPacket p)
        {
            _lfOfferEntry = (int)p.U32();
            _lfOfferVersion++;
            _lfg.Version++;
        }

        /// <summary>SMSG_LFG_DISABLED (LFGHandler.cpp:619-623, empty).</summary>
        void LfOnDisabled(WowPacket p)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = "The Dungeon Finder is disabled." });   // ours
            _lfg.Status = LfgStatus.None;
            _lfg.RoleCheckAsked = _lfg.ProposalAsked = false;
            _lfg.Version++;
        }

        partial void TickLfg(float dt)
        {
            if (!_lfAskedAtLogin && _world != null)
            {
                _lfAskedAtLogin = true;
                QuSend(Op.CMSG_LFG_GET_STATUS, null);   // ours: the queue state after a relog (LFGHandler.cpp:281-300 answers with UPDATE_STATUS)
            }
            if (_lfg.RoleCheckSecondsLeft > 0f) _lfg.RoleCheckSecondsLeft = Math.Max(0f, _lfg.RoleCheckSecondsLeft - dt);
            if (_lfg.ProposalSecondsLeft > 0f) _lfg.ProposalSecondsLeft = Math.Max(0f, _lfg.ProposalSecondsLeft - dt);
            if (_lfg.BootSecondsLeft > 0f) _lfg.BootSecondsLeft = Math.Max(0f, _lfg.BootSecondsLeft - dt);
            if (_lfg.Status == LfgStatus.Queued) _lfg.QueuedSeconds += dt;
            bool grouped = _player.Group.Count > 0;
            if ((_lfg.Status == LfgStatus.InDungeon || _lfg.Status == LfgStatus.Finished) && !grouped) { _lfg.Status = LfgStatus.None; _lfg.Version++; }
            bool inside = Raid.IsLfgGroup || (grouped && (_lfg.Status == LfgStatus.InDungeon || _lfg.Status == LfgStatus.Finished || _lfg.Status == LfgStatus.Boot));
            if (inside != _lfg.InLfgDungeon) { _lfg.InLfgDungeon = inside; _lfg.Version++; }
        }

        // ------------------------------------------------------------------ LFGDungeons.dbc

        void LfTables()
        {
            if (_lfDbcTried) return;
            _lfDbcTried = true;
            try
            {
                _lfDbc = _files != null ? Dbc.Load(_files, "LFGDungeons") : null;
                _lfGroupDbc = _files != null ? Dbc.Load(_files, "LFGDungeonGroup") : null;
            }
            catch (Exception ex) { WowLog.Warn("Online dungeon finder: LFGDungeons.dbc not read (" + ex.Message + ") - dungeons are shown by id."); }
        }

        /// <summary>LFGDungeons.dbc Name_lang; "Dungeon &lt;id&gt;" (ours) when unknown.</summary>
        internal string LfName(int id)
        {
            LfTables();
            int row = _lfDbc != null ? _lfDbc.FindRow(id) : -1;
            string n = row >= 0 ? _lfDbc.GetLocalizedString(row, LfDbcName) : null;
            return string.IsNullOrEmpty(n) ? "Dungeon " + id : n;
        }

        /// <summary>A dungeon view of an entry (id | type << 24) from LFGDungeons.dbc.</summary>
        LfgDungeonView LfView(int entry)
        {
            LfTables();
            int id = entry & 0xFFFFFF;
            var d = new LfgDungeonView { Id = id, Entry = entry, Name = "Dungeon " + id };
            int row = _lfDbc != null ? _lfDbc.FindRow(id) : -1;
            if (row < 0) return d;
            d.Name = LfName(id);
            d.MinLevel = _lfDbc.GetInt(row, LfDbcMin);
            d.MaxLevel = _lfDbc.GetInt(row, LfDbcMax);
            d.MapId = _lfDbc.GetInt(row, LfDbcMap);
            d.Difficulty = _lfDbc.GetInt(row, LfDbcDiff);
            d.TypeId = _lfDbc.GetInt(row, LfDbcType);
            d.Expansion = _lfDbc.GetInt(row, LfDbcExp);
            if (entry == id) d.Entry = id | d.TypeId << 24;   // what CMSG_LFG_JOIN sends (LFGHandler.cpp:66 strips the type again)
            return d;
        }

        void LfApplyLock(LfgDungeonView d)
        {
            int s;
            d.LockStatus = _lfLocks.TryGetValue(d.Id, out s) ? s : 0;
        }

        /// <summary>LFDList_DefaultFilterFunction (LFDFrame.lua:1203-1211) over the TypeID 1 rows: a header (GroupID != 0), the expansion, our level
        /// in MinLevel..MaxLevel, and (level - 15 &lt;= the recommended level or not locked); ordered by GroupID then OrderIndex (ours: the C code's
        /// GetLFDChoiceOrder is not readable).</summary>
        void LfBuildSpecific()
        {
            LfTables();
            _lfg.Dungeons.Clear();
            if (_lfDbc == null) return;
            int level = _player.Level;
            var rows = new List<int>();
            for (int r = 0; r < _lfDbc.RecordCount; r++)
            {
                if (_lfDbc.GetInt(r, LfDbcType) != 1) continue;
                if (_lfDbc.GetInt(r, LfDbcGroup) == 0) continue;
                if (_lfDbc.GetInt(r, LfDbcExp) > LfgExpansion) continue;
                if (level < _lfDbc.GetInt(r, LfDbcMin) || level > _lfDbc.GetInt(r, LfDbcMax)) continue;
                int id = _lfDbc.GetId(r);
                if (!(level - LfgMaxShownLevelDiff <= _lfDbc.GetInt(r, LfDbcRec) || !_lfLocks.ContainsKey(id))) continue;
                rows.Add(r);
            }
            rows.Sort((a, b) =>
            {
                int ga = LfGroupSort(_lfDbc.GetInt(a, LfDbcGroup)), gb = LfGroupSort(_lfDbc.GetInt(b, LfDbcGroup));
                if (ga != gb) return ga.CompareTo(gb);
                int oa = _lfDbc.GetInt(a, LfDbcOrder), ob = _lfDbc.GetInt(b, LfDbcOrder);
                if (oa != ob) return oa.CompareTo(ob);
                return string.CompareOrdinal(_lfDbc.GetLocalizedString(a, LfDbcName), _lfDbc.GetLocalizedString(b, LfDbcName));
            });
            for (int i = 0; i < rows.Count; i++)
            {
                LfgDungeonView d = LfView(_lfDbc.GetId(rows[i]));
                LfApplyLock(d);
                _lfg.Dungeons.Add(d);
            }
        }

        int LfGroupSort(int groupId)
        {
            int row = _lfGroupDbc != null ? _lfGroupDbc.FindRow(groupId) : -1;
            return row >= 0 ? _lfGroupDbc.GetInt(row, LfGroupOrder) : groupId;
        }

        /// <summary>The LFGDungeonGroup.dbc header of a dungeon ("Classic Dungeons", "Burning Crusade Heroic" ...), null when it has none (the UI's
        /// group headers; not in the frozen contract).</summary>
        public string LfgGroupNameOf(int dungeonId)
        {
            LfTables();
            int row = _lfDbc != null ? _lfDbc.FindRow(dungeonId) : -1;
            if (row < 0 || _lfGroupDbc == null) return null;
            int g = _lfDbc.GetInt(row, LfDbcGroup);
            int grow = _lfGroupDbc.FindRow(g);
            string n = grow >= 0 ? _lfGroupDbc.GetLocalizedString(grow, LfGroupName) : null;
            return string.IsNullOrEmpty(n) ? null : n;
        }
    }
}
