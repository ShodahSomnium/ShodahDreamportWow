// iteration 11 - owner interact (proposal-interact.md 3.4 / 5.5 / 6.2.4): what raids add to the party (OnlineSession.Group.cs): convert,
// assistants, main tank / main assist, sub groups, ready checks, raid target icons, Dungeon Finder vote kicks.
// AzerothCore master: src/server/game/Handlers/GroupHandler.cpp:610-815 (target icons, convert, sub groups, assistant, assignment, ready
// check), :1154- (swap), :353-414 (uninvite by guid with a reason); Groups/Group.cpp:1830-1866 (SetTargetIcon / SendTargetIconList),
// :1901-1949 (SendUpdateToPlayer), :1982-2005 (BroadcastReadyCheck / OfflineReadyCheck); Group.h (GroupType, GroupMemberFlags).
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const byte IxGroupBg = 0x01, IxGroupRaid = 0x02, IxGroupBgRaid = 0x04, IxGroupLfg = 0x08;    // GroupType (Group.h)
        const byte IxMemberAssistant = 0x01, IxMemberMainTank = 0x02, IxMemberMainAssist = 0x04;    // GroupMemberFlags (Group.h)
        const int IxSubGroups = 8;                     // MAX_RAID_SUBGROUPS
        const float IxReadyCheckSeconds = 30f;         // ours: the server keeps no time - the initiator's client ends it (decision D-I4)
        const float IxReadyMarksKeep = 10f;            // ours: the marks stay 10 s after the end (RaidGroupFrame_ReadyCheckFinished fades them)
        const float IxReadyCheckGuard = 60f;           // ours: a check whose initiator never sends FINISHED ends here

        readonly RaidView _ixRaid = new RaidView();
        readonly Dictionary<ulong, RaidMemberView> _ixRaidKnown = new Dictionary<ulong, RaidMemberView>();
        readonly List<RaidMemberView> _ixRaidNew = new List<RaidMemberView>();
        ulong _ixRaidGroup;              // the group guid of the last list, 0 = none
        bool _ixRaidInGroup;
        float _ixReadyAge, _ixReadyMarks;

        public RaidView Raid { get { return _ixRaid; } }

        void IxRaidInit()
        {
            IxHandle(Op.SMSG_GROUP_LIST, IxRaidOnList);   // after InitGroup's handler (WorldClient.On keeps the order; p.Pos is reset per handler)
            IxHandle(Op.MSG_RAID_TARGET_UPDATE, IxRaidOnTargetUpdate);
            IxHandle(Op.MSG_RAID_READY_CHECK, IxRaidOnReadyCheck);
            IxHandle(Op.MSG_RAID_READY_CHECK_CONFIRM, IxRaidOnReadyConfirm);
            IxHandle(Op.MSG_RAID_READY_CHECK_FINISHED, IxRaidOnReadyFinished);
        }

        // ------------------------------------------------------------------ SMSG_GROUP_LIST (the raid parts Group.cs skips)

        /// <summary>
        /// SMSG_GROUP_LIST (Group::SendUpdateToPlayer, Group.cpp:1901-1949): uint8 groupType, uint8 our subgroup, uint8 our flags, uint8 our
        /// roles, [LFG: uint8 status, uint32 dungeon], uint64 group guid, uint32 counter, uint32 count, per other member { string name, uint64
        /// guid, uint8 online, uint8 subgroup, uint8 flags, uint8 roles }, uint64 leader, ... (the loot / difficulty tail is Group.cs').
        /// </summary>
        void IxRaidOnList(WowPacket p)
        {
            byte type = p.U8();
            int mySub = p.U8();
            byte myFlags = p.U8();
            int myRoles = p.U8();
            if ((type & IxGroupLfg) != 0) { p.U8(); p.U32(); }
            ulong groupGuid = p.U64();
            p.U32();                                     // counter
            uint count = p.U32();
            if (count > MaxGroupListCount) return;       // Group.cs logged it
            _ixRaidNew.Clear();
            for (int i = 0; i < count; i++)
            {
                string name = p.CString();
                ulong guid = p.U64();
                p.U8();                                  // online state (Group.cs keeps it)
                int sub = p.U8();
                byte flags = p.U8();
                int roles = p.U8();
                if (guid == 0) continue;
                RaidMemberView m;
                if (!_ixRaidKnown.TryGetValue(guid, out m)) { m = new RaidMemberView { Guid = guid }; _ixRaidKnown[guid] = m; }
                m.SubGroup = sub;
                m.Assistant = (flags & IxMemberAssistant) != 0;
                m.MainTank = (flags & IxMemberMainTank) != 0;
                m.MainAssist = (flags & IxMemberMainAssist) != 0;
                m.Roles = roles;
                _ixRaidNew.Add(m);
            }
            ulong leader = p.Remaining >= 8 ? p.U64() : 0;

            RaidView r = _ixRaid;
            bool inGroup = count > 0;
            bool wasRaid = r.IsRaid, wasIn = _ixRaidInGroup;
            bool raidNow = inGroup && (type & IxGroupRaid) != 0;
            bool sameGroup = inGroup && groupGuid == _ixRaidGroup;
            // ERR_RAID_YOU_JOINED / _YOU_LEFT / _MEMBER_ADDED_S / _MEMBER_REMOVED_S (GlobalStrings.lua:3456-3459); the party's "joins the
            // party." lines are not printed for a raid list (Group.cs hook H5).
            if (raidNow && !(wasRaid && sameGroup)) IxSystem("You have joined a raid group.");
            else if (raidNow)
            {
                for (int i = 0; i < _ixRaidNew.Count; i++)
                    if (!r.Members.Contains(_ixRaidNew[i])) IxSystem((IxName(_ixRaidNew[i].Guid) ?? "?") + " has joined the raid group.");
                for (int i = 0; i < r.Members.Count; i++)
                    if (!_ixRaidNew.Contains(r.Members[i])) IxSystem((IxRaidNameOf(r.Members[i].Guid) ?? "?") + " has left the raid group.");
            }
            if (wasRaid && !raidNow && !inGroup) IxSystem("You have left the raid group.");

            r.IsRaid = raidNow;
            r.IsLfgGroup = inGroup && (type & IxGroupLfg) != 0;
            r.IsBattlegroundGroup = inGroup && (type & (IxGroupBg | IxGroupBgRaid)) != 0;
            r.MySubGroup = inGroup ? mySub : 0;
            r.IAmAssistant = inGroup && (myFlags & IxMemberAssistant) != 0;
            r.IAmMainTank = inGroup && (myFlags & IxMemberMainTank) != 0;
            r.IAmMainAssist = inGroup && (myFlags & IxMemberMainAssist) != 0;
            r.CanLead = inGroup && (leader == _player.Guid || (raidNow && r.IAmAssistant));
            r.Members.Clear();
            r.Members.AddRange(_ixRaidNew);
            if (!r.ReadyCheckActive && _ixReadyMarks <= 0f) for (int i = 0; i < r.Members.Count; i++) r.Members[i].Ready = ReadyCheckState.None;
            else for (int i = 0; i < r.Members.Count; i++) if (r.Members[i].Ready == ReadyCheckState.None && r.ReadyCheckActive) r.Members[i].Ready = ReadyCheckState.Waiting;

            if (!inGroup)
            {
                if (wasIn)
                {
                    for (int i = 0; i < r.TargetIcons.Length; i++) r.TargetIcons[i] = 0;   // the group's icons are gone with it
                    IxRaidEndReadyCheck(false);
                }
                _ixRaidGroup = 0;
            }
            else if (groupGuid != _ixRaidGroup)
            {
                // a new group: our solo marks were only ours; ask the group's list (GroupHandler.cpp:614-617 SendTargetIconList)
                for (int i = 0; i < r.TargetIcons.Length; i++) r.TargetIcons[i] = 0;
                _ixRaidGroup = groupGuid;
                IxSend(Op.MSG_RAID_TARGET_UPDATE, new PacketWriter().U8(0xFF), "MSG_RAID_TARGET_UPDATE 0xFF (the icon list of group " + IxGuidText(groupGuid) + ")");
            }
            _ixRaidInGroup = inGroup;
            r.Version++;
            if (raidNow || wasRaid)
                WowLog.Info("Online raid: SMSG_GROUP_LIST type 0x" + type.ToString("X2") + " - " + (raidNow ? "raid" : "no raid") + ", my sub group " + (mySub + 1) +
                            (r.IAmAssistant ? ", assistant" : "") + (r.CanLead ? ", can lead" : "") + ", roles 0x" + myRoles.ToString("X") + ", " + r.Members.Count + " other member(s)");
        }

        string IxRaidNameOf(ulong guid)
        {
            GroupMemberView v;
            if (_grpKnown.TryGetValue(guid, out v) && !string.IsNullOrEmpty(v.Name)) return v.Name;
            return ChatSender(guid);
        }

        // ------------------------------------------------------------------ actions (IWowInteractions)

        /// <summary>RaidFrameConvertToRaidButton: CMSG_GROUP_RAID_CONVERT (empty; leader of 2+, GroupHandler.cpp:647-670). The server answers
        /// SMSG_PARTY_COMMAND_RESULT (level errors: Group.cs) and a list with groupType RAID.</summary>
        public void ConvertToRaid()
        {
            if (_world == null) { Error(NotYet); return; }
            if (_player.Group.Count == 0 || _player.GroupLeaderGuid != _player.Guid) { IxError("You are not the party leader."); return; }   // ERR_NOT_LEADER (GlobalStrings.lua:3313)
            if (_ixRaid.IsRaid) return;
            IxSend(Op.CMSG_GROUP_RAID_CONVERT, null, "CMSG_GROUP_RAID_CONVERT");
        }

        /// <summary>RAID_PROMOTE / RAID_DEMOTE (raid leader only): CMSG_GROUP_ASSISTANT_LEADER (uint64 guid, uint8 apply; GroupHandler.cpp:709-726).</summary>
        public void SetRaidAssistant(ulong guid, bool on)
        {
            if (!IxRaidCheck(true, true)) return;
            if (_player.GroupLeaderGuid != _player.Guid) { IxError("You are not the party leader."); return; }                               // ERR_NOT_LEADER
            IxSend(Op.CMSG_GROUP_ASSISTANT_LEADER, new PacketWriter().U64(guid).U8(on ? 1 : 0), "CMSG_GROUP_ASSISTANT_LEADER " + (IxRaidNameOf(guid) ?? IxGuidText(guid)) + (on ? " on" : " off"));
        }

        /// <summary>RAID_MAINTANK (leader / assistant): MSG_PARTY_ASSIGNMENT (uint8 0 MAINTANK, uint8 apply, uint64 guid; :728-758).</summary>
        public void SetMainTank(ulong guid, bool on) { IxRaidAssign(0, guid, on, "main tank"); }

        /// <summary>RAID_MAINASSIST (leader / assistant): MSG_PARTY_ASSIGNMENT (uint8 1 MAINASSIST, uint8 apply, uint64 guid).</summary>
        public void SetMainAssist(ulong guid, bool on) { IxRaidAssign(1, guid, on, "main assist"); }

        void IxRaidAssign(int assignment, ulong guid, bool on, string what)
        {
            if (!IxRaidCheck(true, false)) return;
            IxSend(Op.MSG_PARTY_ASSIGNMENT, new PacketWriter().U8(assignment).U8(on ? 1 : 0).U64(guid), "MSG_PARTY_ASSIGNMENT " + what + " " + (IxRaidNameOf(guid) ?? IxGuidText(guid)) + (on ? " on" : " off"));
        }

        /// <summary>A member onto sub group 0..7: CMSG_GROUP_CHANGE_SUB_GROUP (string name, uint8 group; :672-707 - leader / assistant).</summary>
        public void MoveToSubGroup(ulong guid, int subGroup)
        {
            if (!IxRaidCheck(true, false)) return;
            if (subGroup < 0 || subGroup >= IxSubGroups) return;
            string name = guid == _player.Guid ? _player.Name : IxRaidNameOf(guid);
            if (string.IsNullOrEmpty(name)) { IxError("Unknown unit."); return; }                                                              // ERR_UNIT_NOT_FOUND (:3565)
            IxSend(Op.CMSG_GROUP_CHANGE_SUB_GROUP, new PacketWriter().CString(name).U8(subGroup), "CMSG_GROUP_CHANGE_SUB_GROUP " + name + " -> group " + (subGroup + 1));
        }

        /// <summary>Two members trade places: CMSG_GROUP_SWAP_SUB_GROUP (string, string; GroupHandler.cpp:1154-).</summary>
        public void SwapSubGroups(ulong guidA, ulong guidB)
        {
            if (!IxRaidCheck(true, false)) return;
            string a = guidA == _player.Guid ? _player.Name : IxRaidNameOf(guidA), b = guidB == _player.Guid ? _player.Name : IxRaidNameOf(guidB);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { IxError("Unknown unit."); return; }
            IxSend(Op.CMSG_GROUP_SWAP_SUB_GROUP, new PacketWriter().CString(a).CString(b), "CMSG_GROUP_SWAP_SUB_GROUP " + a + " <-> " + b);
        }

        /// <summary>Refused locally (ERR_NOT_IN_RAID / ERR_NOT_LEADER, GlobalStrings.lua:3312-3313) when the server would drop it.</summary>
        bool IxRaidCheck(bool needRaid, bool leaderOnly)
        {
            if (_world == null) { Error(NotYet); return false; }
            if (_player.Group.Count == 0 || (needRaid && !_ixRaid.IsRaid)) { IxError("You are not in a raid group"); return false; }          // ERR_NOT_IN_RAID
            if (!_ixRaid.CanLead) { IxError("You are not the party leader."); return false; }                                                  // ERR_NOT_LEADER
            return true;
        }

        /// <summary>RaidFrameReadyCheckButton / /readycheck: MSG_RAID_READY_CHECK (empty; leader / assistant of any group, :760-788). The
        /// server relays our guid to every member (us too) and answers "not ready" for the offline ones (Group.cpp:1993-2005).</summary>
        public void StartReadyCheck()
        {
            if (_world == null) { Error(NotYet); return; }
            if (_player.Group.Count == 0) { IxError("You aren't in a party."); return; }                                                       // ERR_NOT_IN_GROUP (GlobalStrings.lua:3311)
            if (!_ixRaid.CanLead) { IxError("You are not the party leader."); return; }                                                        // ERR_NOT_LEADER (:3313)
            if (_ixRaid.ReadyCheckActive) return;
            _ixRaid.ReadyCheckMine = true;
            IxSend(Op.MSG_RAID_READY_CHECK, null, "MSG_RAID_READY_CHECK (start)");
        }

        /// <summary>The ready-check frame's Yes / No: MSG_RAID_READY_CHECK uint8 1 / 0 (relayed to the leader and assistants as
        /// MSG_RAID_READY_CHECK_CONFIRM, Group.cpp:1982-1991).</summary>
        public void AnswerReadyCheck(bool ready)
        {
            RaidView r = _ixRaid;
            if (!r.ReadyCheckActive) { WowLog.Info("Online raid: ready-check answer without a check - ignored"); return; }
            IxSend(Op.MSG_RAID_READY_CHECK, new PacketWriter().U8(ready ? 1 : 0), "MSG_RAID_READY_CHECK " + (ready ? "ready" : "not ready"));
            r.MyReady = ready ? ReadyCheckState.Ready : ReadyCheckState.NotReady;
            r.ReadyCheckAsked = false;
            r.Version++;
        }

        /// <summary>RAID_TARGET_n (TargetFrame.lua:703-709 SetRaidTargetIcon: the unit's own icon again = clear): MSG_RAID_TARGET_UPDATE
        /// uint8 icon - 1, uint64 unit (GroupHandler.cpp:610-645: party - anyone, raid - leader / assistants; a hostile player is dropped).
        /// Without a group the server keeps nothing (`if (!group) return`) - the icon is then ours alone (WoW's solo marking).</summary>
        public void SetRaidTargetIcon(ulong unitGuid, int icon)
        {
            if (unitGuid == 0 || icon < 0 || icon > 8) return;
            RaidView r = _ixRaid;
            int had = RaidTargetIconOf(unitGuid);
            bool clear = icon == 0 || icon == had;
            if (_player.Group.Count == 0 || _world == null)
            {
                for (int i = 0; i < r.TargetIcons.Length; i++) if (r.TargetIcons[i] == unitGuid) r.TargetIcons[i] = 0;
                if (!clear) r.TargetIcons[icon - 1] = unitGuid;
                r.Version++;
                WowLog.Info("Online raid: raid target icon " + (clear ? "cleared" : icon.ToString()) + " on " + IxGuidText(unitGuid) + " (no group - ours alone)");
                return;
            }
            if (r.IsRaid && !r.CanLead) { IxError("You are not the party leader."); return; }                                                  // ERR_NOT_LEADER (:3313)
            EntityState e = Find(unitGuid);   // GroupHandler.cpp:633-640: a hostile PLAYER guid is refused - a clear sends guid 0, so it always goes out
            if (!clear && e != null && e.Kind == EntityKind.Player && e.Reaction == Reaction.Hostile) { WowLog.Info("Online raid: a hostile player cannot be marked (GroupHandler.cpp:633-640) - not sent"); return; }
            if (clear)
            {
                if (had == 0) return;
                IxSend(Op.MSG_RAID_TARGET_UPDATE, new PacketWriter().U8(had - 1).U64(0), "MSG_RAID_TARGET_UPDATE clear icon " + had);
            }
            else IxSend(Op.MSG_RAID_TARGET_UPDATE, new PacketWriter().U8(icon - 1).U64(unitGuid), "MSG_RAID_TARGET_UPDATE icon " + icon + " on " + IxGuidText(unitGuid));
        }

        public int RaidTargetIconOf(ulong unitGuid)
        {
            if (unitGuid == 0) return 0;
            ulong[] t = _ixRaid.TargetIcons;
            for (int i = 0; i < t.Length; i++) if (t[i] == unitGuid) return i + 1;
            return 0;
        }

        /// <summary>VOTE_TO_KICK in a Dungeon Finder group: CMSG_GROUP_UNINVITE_GUID (uint64 guid, string reason; GroupHandler.cpp:353-414 -
        /// an empty reason in an LFG group is refused there, :397-401).</summary>
        public void VoteToKick(ulong guid, string reason)
        {
            if (_world == null) { Error(NotYet); return; }
            if (!_ixRaid.IsLfgGroup) { WowLog.Info("Online raid: Vote to Kick outside a Dungeon Finder group - ignored"); return; }
            string why = reason != null ? reason.Trim() : "";
            if (why.Length == 0) return;
            if (why.Length > 64) why = why.Substring(0, 64);   // VOTE_BOOT_REASON_REQUIRED's edit box maxLetters 64 (StaticPopup.lua:2769-2797)
            IxSend(Op.CMSG_GROUP_UNINVITE_GUID, new PacketWriter().U64(guid).CString(why), "CMSG_GROUP_UNINVITE_GUID vote kick " + (IxRaidNameOf(guid) ?? IxGuidText(guid)));
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>MSG_RAID_TARGET_UPDATE S->C (Group.cpp:1830-1866): uint8 0 + uint64 who, uint8 icon, uint64 target (0 = cleared; one icon
        /// per unit) - or uint8 1 + (uint8 icon, uint64 target) pairs to the end (the full list).</summary>
        void IxRaidOnTargetUpdate(WowPacket p)
        {
            byte kind = p.U8();
            ulong[] t = _ixRaid.TargetIcons;
            if (kind == 1)
            {
                for (int i = 0; i < t.Length; i++) t[i] = 0;
                while (p.Remaining >= 9)
                {
                    int id = p.U8();
                    ulong target = p.U64();
                    if (id < t.Length) t[id] = target;
                }
                WowLog.Info("Online raid: MSG_RAID_TARGET_UPDATE list - " + IxIconCount() + " icon(s) set");
            }
            else
            {
                p.U64();                                 // who set it
                int id = p.U8();
                ulong target = p.U64();
                if (id >= t.Length) { IxOdd("MSG_RAID_TARGET_UPDATE", "icon " + id); return; }
                if (target != 0) for (int i = 0; i < t.Length; i++) if (t[i] == target) t[i] = 0;
                t[id] = target;
            }
            _ixRaid.Version++;
        }

        int IxIconCount()
        {
            int n = 0;
            for (int i = 0; i < _ixRaid.TargetIcons.Length; i++) if (_ixRaid.TargetIcons[i] != 0) n++;
            return n;
        }

        /// <summary>MSG_RAID_READY_CHECK S->C (uint64 initiator, to every member incl. the initiator). ERR_RAID_LEADER_READY_CHECK_START_S
        /// (GlobalStrings.lua:3454); the initiator gets no question (ReadyCheck.lua ShowReadyCheck :9-20).</summary>
        void IxRaidOnReadyCheck(WowPacket p)
        {
            ulong initiator = p.U64();
            RaidView r = _ixRaid;
            r.ReadyCheckActive = true;
            r.ReadyCheckInitiator = initiator;
            r.ReadyCheckInitiatorName = initiator == _player.Guid ? _player.Name : IxRaidNameOf(initiator) ?? IxName(initiator);
            r.ReadyCheckSecondsLeft = IxReadyCheckSeconds;
            _ixReadyAge = 0f;
            _ixReadyMarks = 0f;
            _ixReadyFinishSent = false;
            for (int i = 0; i < r.Members.Count; i++) r.Members[i].Ready = r.Members[i].Guid == initiator ? ReadyCheckState.Ready : ReadyCheckState.Waiting;
            if (initiator == _player.Guid)
            {
                r.ReadyCheckMine = true;
                r.MyReady = ReadyCheckState.Ready;
                r.ReadyCheckAsked = false;
            }
            else
            {
                r.ReadyCheckMine = false;
                r.MyReady = ReadyCheckState.Waiting;
                r.ReadyCheckAsked = true;
            }
            IxSystem((r.ReadyCheckInitiatorName ?? "?") + " has initiated a ready check.", false);
            r.Version++;
            WowLog.Info("Online raid: MSG_RAID_READY_CHECK from " + (initiator == _player.Guid ? "me" : r.ReadyCheckInitiatorName ?? IxGuidText(initiator)) + " - " + r.Members.Count + " member(s) waiting, " + IxReadyCheckSeconds + " s (ours)");
        }

        /// <summary>MSG_RAID_READY_CHECK_CONFIRM (uint64 guid, uint8 state; to the leader and assistants only, Group.cpp:1982-1991).</summary>
        void IxRaidOnReadyConfirm(WowPacket p)
        {
            ulong guid = p.U64();
            bool ready = p.U8() != 0;
            RaidView r = _ixRaid;
            if (guid == _player.Guid) r.MyReady = ready ? ReadyCheckState.Ready : ReadyCheckState.NotReady;
            RaidMemberView m = r.Find(guid);
            if (m != null) m.Ready = ready ? ReadyCheckState.Ready : ReadyCheckState.NotReady;
            r.Version++;
        }

        /// <summary>MSG_RAID_READY_CHECK_FINISHED S->C (empty, to all - :804-815): the initiator's results (READY_CHECK_ALL_READY :6028 /
        /// RAID_MEMBER_NOT_READY :5984 / RAID_MEMBERS_AFK :5983, then READY_CHECK_FINISHED :6029); we were asked and never answered ->
        /// READY_CHECK_YOU_WERE_AFK :6033 (ReadyCheck.lua:28-36).</summary>
        void IxRaidOnReadyFinished(WowPacket p)
        {
            if (!_ixRaid.ReadyCheckActive) return;
            IxRaidEndReadyCheck(true);
        }

        void IxRaidEndReadyCheck(bool texts)
        {
            RaidView r = _ixRaid;
            if (!r.ReadyCheckActive) return;
            bool mine = r.ReadyCheckMine;
            bool unanswered = r.ReadyCheckAsked || r.MyReady == ReadyCheckState.Waiting;
            var away = new StringBuilder();
            int notReady = 0, awayCount = 0;
            for (int i = 0; i < r.Members.Count; i++)
            {
                RaidMemberView m = r.Members[i];
                if (m.Ready == ReadyCheckState.Waiting) { m.Ready = ReadyCheckState.Away; awayCount++; if (away.Length > 0) away.Append(", "); away.Append(IxRaidNameOf(m.Guid) ?? "?"); }
                if (m.Ready == ReadyCheckState.NotReady)
                {
                    notReady++;
                    if (texts && mine) IxSystem((IxRaidNameOf(m.Guid) ?? "?") + " is not ready", false);
                }
            }
            if (texts)
            {
                if (mine)
                {
                    if (notReady == 0 && awayCount == 0) IxSystem("Everyone is Ready", false);
                    else if (awayCount > 0) IxSystem("The following players are Away: " + away, false);
                    IxSystem("Ready check finished", false);
                }
                else if (unanswered) IxSystem("You were Away for a ready check", false);
            }
            if (unanswered && r.MyReady == ReadyCheckState.Waiting) r.MyReady = ReadyCheckState.Away;
            r.ReadyCheckActive = false;
            r.ReadyCheckAsked = false;
            r.ReadyCheckMine = false;
            r.ReadyCheckSecondsLeft = 0f;
            _ixReadyMarks = IxReadyMarksKeep;
            r.Version++;
            WowLog.Info("Online raid: ready check finished - " + notReady + " not ready, " + awayCount + " away" + (mine ? " (mine)" : ""));
        }

        // ------------------------------------------------------------------ per frame

        void IxRaidTick(float dt)
        {
            RaidView r = _ixRaid;
            if (r.ReadyCheckActive)
            {
                r.ReadyCheckSecondsLeft = Math.Max(0f, r.ReadyCheckSecondsLeft - dt);
                _ixReadyAge += dt;
                if (r.ReadyCheckSecondsLeft <= 0f)
                {
                    if (r.ReadyCheckAsked) { r.ReadyCheckAsked = false; r.Version++; WowLog.Info("Online raid: the ready-check question timed out unanswered"); }
                    if (r.ReadyCheckMine && !_ixReadyFinishSent && _world != null)
                    {
                        _ixReadyFinishSent = true;   // once; the results print on the server's echo (MSG_RAID_READY_CHECK_FINISHED to all)
                        IxSend(Op.MSG_RAID_READY_CHECK_FINISHED, null, "MSG_RAID_READY_CHECK_FINISHED (our check's time is up)");
                    }
                }
                if (_ixReadyAge > IxReadyCheckGuard) { WowLog.Info("Online raid: the ready check never finished - ended here (ours)"); IxRaidEndReadyCheck(false); }
            }
            else if (_ixReadyMarks > 0f)
            {
                _ixReadyMarks -= dt;
                if (_ixReadyMarks <= 0f)
                {
                    for (int i = 0; i < r.Members.Count; i++) r.Members[i].Ready = ReadyCheckState.None;
                    r.MyReady = ReadyCheckState.None;
                    r.Version++;
                }
            }
        }

        bool _ixReadyFinishSent;
    }
}
