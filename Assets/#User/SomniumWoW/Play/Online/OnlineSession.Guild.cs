using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Social;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 11 (owner social, proposal-social.md 3.1 / 3.2 / 6.3.3): the guild (roster, events, invites, ranks, notes, MOTD, info) and
    // guild creation with a charter (registrar, petition, signatures, turn-in). AzerothCore master (P/acsrc11/social): GuildHandler.cpp,
    // Guild.cpp / .h, GuildPackets.cpp, PetitionsHandler.cpp. Readers in Play/Social/SocialRead.cs.
    // Login (Guild::SendLoginInfo, Guild.cpp:1939-1958): GE_MOTD + SMSG_GUILD_ROSTER arrive right after SMSG_LOGIN_VERIFY_WORLD, before our
    // own object: InitGuild registers before world.Release (OnlineSession.cs), the roster creates the GuildView, PLAYER_GUILDID confirms it.
    // Ours (logged once in InitGuild): the roster / info re-ask throttle GldRefreshEvery; CMSG_GUILD_RANK always carries GR_RIGHT_EMPTY 0x40
    // (a value without it passes every right test of Guild.h:819-825 - risk R2); "Arena charters are not in this version yet."
    public sealed partial class OnlineSession
    {
        const double GldRefreshEvery = 2.0;              // ours: s between two CMSG_GUILD_ROSTER (+ CMSG_GUILD_INFO) re-asks
        const int GldMotdMax = 128;                      // StaticPopup SET_GUILDMOTD maxLetters 128 (StaticPopup.lua:2021)
        const int GldNoteMax = 31;                       // SET_GUILDPLAYERNOTE maxLetters 31 (StaticPopup.lua:2052)
        const int GldRankNameMax = 15;                   // GuildControlPopupFrameEditBox letters="15" (FriendsFrame.xml:3794)
        const int GldInfoMax = 500;                      // GuildInfoEditBox letters="500" (FriendsFrame.xml:4100)
        const int GldInviteMax = 12;                     // ADD_GUILDMEMBER maxLetters 12 (StaticPopup.lua:1905)
        const int GldGuildNameMax = 24;                  // GuildRegistrarFrameEditBox letters="24" (GuildRegistrarFrame.xml:269), RENAME_GUILD maxLetters 24
        const int GuildCharterEntry = 5863;              // GUILD_CHARTER (PetitionMgr.h:25)
        const uint UnitNpcFlagPetitioner = 0x40000;      // UNIT_NPC_FLAG_PETITIONER (UnitDefines.h:340)

        // WoW's texts (GlobalStrings.lua of the 3.3.5a enUS install, line numbers)
        const string GuildMotdTemplate = "Guild Message of the Day: {0}";                              // GUILD_MOTD_TEMPLATE (3964)
        const string ErrGuildPromote = "{0} has promoted {1} to {2}.";                                // ERR_GUILD_PROMOTE_SSS (3115)
        const string ErrGuildDemote = "{0} has demoted {1} to {2}.";                                  // ERR_GUILD_DEMOTE_SSS (3093)
        const string ErrGuildJoin = "{0} has joined the guild.";                                      // ERR_GUILD_JOIN_S (3100)
        const string ErrGuildLeave = "{0} has left the guild.";                                       // ERR_GUILD_LEAVE_S (3107)
        const string ErrGuildRemove = "{0} has been kicked out of the guild by {1}.";                 // ERR_GUILD_REMOVE_SS (3122)
        const string ErrGuildRemoveSelf = "You have been kicked out of the guild.";                   // ERR_GUILD_REMOVE_SELF (3121)
        const string ErrGuildLeaderIs = "{0} is the leader of your guild.";                           // ERR_GUILD_LEADER_IS_S (3102)
        const string ErrGuildLeaderChanged = "{0} has made {1} the new Guild Master.";                // ERR_GUILD_LEADER_CHANGED_SS (3101)
        const string ErrGuildDisbanded = "Guild has been disbanded.";                                 // ERR_GUILD_DISBANDED (3094)
        const string ErrGuildInvite = "You have invited {0} to join your guild.";                     // ERR_GUILD_INVITE_S (3099)
        const string ErrGuildQuit = "You are no longer a member of {0}.";                             // ERR_GUILD_QUIT_S (3116)
        const string ErrGuildCreate = "{0} created.";                                                 // ERR_GUILD_CREATE_S (3091)
        const string ErrGuildFounder = "Congratulations, you are a founding member of {0}!";          // ERR_GUILD_FOUNDER_S (3097)
        const string ErrGuildInternal = "Internal guild error.";                                      // ERR_GUILD_INTERNAL (3098)
        const string ErrAlreadyInGuild = "You are already in a guild.";                               // ERR_ALREADY_IN_GUILD (2870)
        const string ErrAlreadyInGuildS = "{0} is already in a guild.";                               // ERR_ALREADY_IN_GUILD_S (2871)
        const string ErrAlreadyInvitedS = "{0} has already been invited to a guild.";                 // ERR_ALREADY_INVITED_TO_GUILD_S (2866)
        const string ErrNameInvalid = "That name contains invalid characters,  Enter a new name.";    // ERR_NAME_INVALID (3280) - 3.3.5 has no ERR_GUILD_NAME_INVALID (ours by meaning)
        const string ErrGuildNameExists = "There is already a guild named \"{0}\".";                  // ERR_GUILD_NAME_EXISTS_S (3108)
        const string ErrGuildLeaderLeave = "You must promote a new Guild Master using /gleader before leaving the guild.";   // ERR_GUILD_LEADER_LEAVE (3103)
        const string ErrGuildPermissions = "You don't have permission to do that.";                   // ERR_GUILD_PERMISSIONS (3111)
        const string ErrGuildNotInGuild = "You are not in a guild.";                                  // ERR_GUILD_PLAYER_NOT_IN_GUILD (3113)
        const string ErrGuildNotInGuildS = "{0} is not in your guild.";                               // ERR_GUILD_PLAYER_NOT_IN_GUILD_S (3114)
        const string ErrGuildPlayerNotFound = "\"{0}\" not found.";                                   // ERR_GUILD_PLAYER_NOT_FOUND_S (3112)
        const string ErrGuildNotAllied = "You cannot invite players from the opposing alliance";      // ERR_GUILD_NOT_ALLIED (3109)
        const string ErrGuildRankTooHigh = "{0}'s rank is too high";                                  // ERR_GUILD_RANK_TOO_HIGH_S (3119)
        const string ErrGuildRankTooLow = "{0} is already at the lowest rank";                        // ERR_GUILD_RANK_TOO_LOW_S (3120)
        const string ErrGuildRanksLocked = "Temporary guild error.  Please try again!";               // ERR_GUILD_RANKS_LOCKED (3117)
        const string ErrGuildRankInUse = "That guild rank is currently in use.";                      // ERR_GUILD_RANK_IN_USE (3118)
        const string ErrGuildDecline = "{0} declines your guild invitation.";                         // ERR_GUILD_DECLINE_S (3092)
        const string ErrPetitionOffered = "You have requested {0}'s signature.";                      // ERR_PETITION_OFFERED_S (3366)
        const string ErrPetitionSigned = "Charter signed.";                                           // ERR_PETITION_SIGNED (3368)
        const string ErrPetitionSignedS = "{0} has signed your charter.";                             // ERR_PETITION_SIGNED_S (3369)
        const string ErrPetitionAlreadySigned = "You have already signed that charter.";              // ERR_PETITION_ALREADY_SIGNED (3358)
        const string ErrPetitionCreator = "You can't sign your own charter.";                         // ERR_PETITION_CREATOR (3360)
        const string ErrPetitionInGuild = "You are already in a guild.";                              // ERR_PETITION_IN_GUILD (3363)
        const string ErrPetitionNotSameServer = "That player is not from your server";                // ERR_PETITION_NOT_SAME_SERVER (3365)
        const string ErrPetitionNotEnough = "You need more signatures.";                              // ERR_PETITION_NOT_ENOUGH_SIGNATURES (3364)
        const string ErrPetitionDeclined = "{0} has declined to sign your petition.";                 // ERR_PETITION_DECLINED_S (3361)
        const string ErrNoGuildCharter = "You don't have a guild charter.";                           // ERR_NO_GUILD_CHARTER (3325)
        const string ErrGenericNoTarget = "You have no target.";                                      // ERR_GENERIC_NO_TARGET (3064)
        const string GldArenaCharters = "Arena charters are not in this version yet.";                // ours (gap G1)

        readonly Dictionary<int, string> _gldNames = new Dictionary<int, string>();     // guild id -> name (CMSG_GUILD_QUERY answers)
        readonly HashSet<int> _gldAsked = new HashSet<int>();
        readonly List<string> _gldArgs = new List<string>();
        readonly List<ulong> _chGuids = new List<ulong>();
        int _gldId = -1;                                                                 // PLAYER_GUILDID last seen (-1 = our object not seen yet)
        double _gldNextAt;
        bool _gldPending, _gldPendingInfo, _gldRosterLogged;
        int _gldEvents, _gldResults;
        GuildCharterView _charter;
        bool _chSigned;
        uint _chQueryId;                                                                  // the last SMSG_PETITION_QUERY_RESPONSE (applied when its signatures arrive)
        string _chQueryName;
        int _chQueryNeeded;
        ulong _chQueryOwner;

        GuildCharterView IWowSocial.Charter { get { return _charter; } }

        partial void InitGuild()
        {
            _world.On(Op.SMSG_GUILD_QUERY_RESPONSE, GldOnQuery, this);
            _world.On(Op.SMSG_GUILD_ROSTER, GldOnRoster, this);
            _world.On(Op.SMSG_GUILD_EVENT, GldOnEvent, this);
            _world.On(Op.SMSG_GUILD_COMMAND_RESULT, GldOnCommandResult, this);
            _world.On(Op.SMSG_GUILD_INVITE, GldOnInvite, this);
            _world.On(Op.SMSG_GUILD_DECLINE, p => SocText(string.Format(ErrGuildDecline, p.CString())), this);   // never sent by AC master
            _world.On(Op.SMSG_GUILD_INFO, GldOnInfo, this);
            _world.On(Op.SMSG_PETITION_SHOWLIST, GldOnShowList, this);
            _world.On(Op.SMSG_PETITION_SHOW_SIGNATURES, GldOnSignatures, this);
            _world.On(Op.SMSG_PETITION_SIGN_RESULTS, GldOnSignResult, this);
            _world.On(Op.MSG_PETITION_DECLINE, p => SocWaitName(p.U64(), n => SocText(string.Format(ErrPetitionDeclined, n))), this);
            _world.On(Op.SMSG_TURN_IN_PETITION_RESULTS, GldOnTurnIn, this);
            _world.On(Op.SMSG_PETITION_QUERY_RESPONSE, GldOnPetitionQuery, this);
            _world.On(Op.MSG_PETITION_RENAME, GldOnRename, this);
            // npc's service registry (proposal-npc.md H4): petitioners WITHOUT gossip open the registrar; guild masters have gossip and reach
            // the same SMSG_PETITION_SHOWLIST through their gossip option (PlayerGossip.cpp:368-370).
            RegisterNpcService(UnitNpcFlagPetitioner, g => { OpenGuildRegistrar(g); return true; }, "guild registrar");
            WowLog.Info("Online guild: ours - roster / info re-asked at most every " + GldRefreshEvery + " s; CMSG_GUILD_RANK always carries GR_RIGHT_EMPTY 0x40; "
                        + "arena organizers answer \"" + GldArenaCharters + "\"");
        }

        // ------------------------------------------------------------------ membership, roster, names

        void GldTick()
        {
            if (_self != null)
            {
                int id = _self.I32(UF.PLAYER_GUILDID);
                if (id != _gldId)
                {
                    _gldId = id;
                    if (id == 0)
                    {
                        if (_social.Guild != null)
                        {
                            _social.Guild = null;
                            SocChanged(SocialPart.Guild);
                            WowLog.Info("Online guild: not in a guild any more");
                        }
                    }
                    else
                    {
                        if (_social.Guild == null || (_social.Guild.Id != 0 && _social.Guild.Id != id)) _social.Guild = new GuildView();
                        _social.Guild.Id = id;
                        GldQuery(id, true);
                        GldRefresh(true);
                        SocChanged(SocialPart.Guild);
                    }
                }
                GuildView g = _social.Guild;
                if (g != null)
                {
                    int rank = _self.I32(UF.PLAYER_GUILDRANK);
                    if (rank != g.MyRank) { g.MyRank = rank; SocChanged(SocialPart.Guild); }
                }
            }
            if (_gldPending && _now >= _gldNextAt) GldSendRefresh();
        }

        void GldQuery(int id, bool force)
        {
            if (_world == null || id <= 0) return;
            if (!_gldAsked.Add(id) && !force) return;
            _world.Send(Op.CMSG_GUILD_QUERY, new PacketWriter().U32((uint)id));   // GuildPackets.cpp:20-23
        }

        /// <summary>A guild's name (another player's PLAYER_GUILDID), asked once; null until the answer.</summary>
        string GldNameOf(int id)
        {
            string n;
            if (_gldNames.TryGetValue(id, out n)) return n;
            GldQuery(id, false);
            return null;
        }

        /// <summary>CMSG_GUILD_ROSTER (+ CMSG_GUILD_INFO), at most every GldRefreshEvery (later requests wait for the next slot).</summary>
        void GldRefresh(bool withInfo)
        {
            _gldPending = true;
            if (withInfo) _gldPendingInfo = true;
            if (_now >= _gldNextAt) GldSendRefresh();
        }

        void GldSendRefresh()
        {
            if (_world == null) return;
            _world.Send(Op.CMSG_GUILD_ROSTER);                                  // GuildHandler.cpp:97-105 (empty)
            if (_gldPendingInfo) _world.Send(Op.CMSG_GUILD_INFO);               // GuildHandler.cpp:89-95 (empty)
            _gldPending = _gldPendingInfo = false;
            _gldNextAt = _now + GldRefreshEvery;
        }

        void GldOnQuery(WowPacket p)
        {
            string name;
            var ranks = new string[10];
            var emblem = new int[5];
            int count;
            int id = SocialRead.GuildQuery(p, out name, ranks, emblem, out count);
            _gldNames[id] = name ?? "";
            GuildView g = _social.Guild;
            if (g == null || !(g.Id == id || (g.Id == 0 && _gldId == id))) return;
            g.Id = id;
            g.Name = name ?? "";
            g.EmblemStyle = emblem[0]; g.EmblemColor = emblem[1]; g.BorderStyle = emblem[2]; g.BorderColor = emblem[3]; g.BackgroundColor = emblem[4];
            while (g.Ranks.Count < count) g.Ranks.Add(new GuildRankView());
            while (g.Ranks.Count > count) g.Ranks.RemoveAt(g.Ranks.Count - 1);
            for (int i = 0; i < count; i++) g.Ranks[i].Name = ranks[i] ?? "";
            SocChanged(SocialPart.Guild);
        }

        void GldOnRoster(WowPacket p)
        {
            GuildView g = _social.Guild ?? (_social.Guild = new GuildView());
            SocialRead.GuildRoster(p, g);
            if (g.Id == 0 && _gldId > 0) g.Id = _gldId;
            WowWhoNames names = SocNames();
            foreach (var m in g.Members)
            {
                m.ZoneName = names.ZoneName(m.ZoneId);
                if (m.Guid == _player.Guid && _self == null) g.MyRank = m.Rank;       // until our own object carries PLAYER_GUILDRANK
                if (!string.IsNullOrEmpty(m.Name) && m.Guid != 0 && !_playerNames.ContainsKey(m.Guid)) _playerNames[m.Guid] = m.Name;
            }
            if (g.Id > 0 && string.IsNullOrEmpty(g.Name)) GldQuery(g.Id, false);
            SocChanged(SocialPart.Guild);
            if (!_gldRosterLogged)
            {
                _gldRosterLogged = true;
                WowLog.Info("Online guild: roster - " + g.Members.Count + " members (" + g.OnlineCount + " online), " + g.Ranks.Count + " ranks (first answer; later ones not logged)");
            }
        }

        void GldOnInfo(WowPacket p)
        {
            GuildView g = _social.Guild;
            if (g == null) return;
            int members;
            SocialRead.GuildInfo(p, g, out members);
            SocChanged(SocialPart.Guild);
        }

        // ------------------------------------------------------------------ events and results (3.1 tables)

        string GldArg(int i) { return i < _gldArgs.Count ? _gldArgs[i] : ""; }

        bool GldIsMe(string name) { return !string.IsNullOrEmpty(name) && string.Equals(name, _player.Name, StringComparison.OrdinalIgnoreCase); }

        void GldOnEvent(WowPacket p)
        {
            ulong guid;
            int type = SocialRead.GuildEvent(p, _gldArgs, out guid);
            _gldEvents++;
            GuildView g = _social.Guild;
            switch (type)
            {
                case 0: SocText(string.Format(ErrGuildPromote, GldArg(0), GldArg(1), GldArg(2))); break;
                case 1: SocText(string.Format(ErrGuildDemote, GldArg(0), GldArg(1), GldArg(2))); break;
                case 2:                                                                   // GE_MOTD (login :1941-1945, change :1303)
                    if (g == null) g = _social.Guild = new GuildView();
                    g.Motd = GldArg(0);
                    if (g.Motd.Length > 0) SocText(string.Format(GuildMotdTemplate, g.Motd));
                    SocChanged(SocialPart.Guild);
                    return;                                                               // the roster follows at login; a MOTD change needs none
                case 3:
                    SocText(string.Format(ErrGuildJoin, GldArg(0)));
                    if (GldIsMe(GldArg(0)) && _gldId > 0) GldQuery(_gldId, true);
                    break;
                case 4: SocText(string.Format(ErrGuildLeave, GldArg(0))); break;
                case 5: SocText(GldIsMe(GldArg(0)) ? ErrGuildRemoveSelf : string.Format(ErrGuildRemove, GldArg(0), GldArg(1))); break;
                case 6: SocText(string.Format(ErrGuildLeaderIs, GldArg(0))); break;
                case 7: SocText(string.Format(ErrGuildLeaderChanged, GldArg(0), GldArg(1))); break;
                case 8: SocText(ErrGuildDisbanded); break;
                case 9: if (_gldId > 0) GldQuery(_gldId, true); break;                    // GE_TABARDCHANGE: the emblem (no line)
                case 10:
                case 11: if (_gldId > 0) GldQuery(_gldId, true); break;                   // GE_RANK_UPDATED / DELETED: names + count (no line)
                case 12: if (!GldIsMe(GldArg(0))) SocLine(string.Format(ErrFriendOnline, GldArg(0))); break;   // GE_SIGNED_ON - WoW prints the friend text
                case 13: if (!GldIsMe(GldArg(0))) SocLine(string.Format(ErrFriendOffline, GldArg(0))); break;
                default:
                    SocLogOnce("guildevent" + type, "SMSG_GUILD_EVENT " + type + " (guild bank) - ignored (the guild bank is gap G3)");
                    return;
            }
            GldRefresh(false);                                                            // the real client's GuildRoster() on GUILD_ROSTER_UPDATE
        }

        void GldOnCommandResult(WowPacket p)
        {
            int cmd, result;
            string name;
            SocialRead.GuildCommandResult(p, out cmd, out name, out result);
            _gldResults++;
            name = name ?? "";
            switch (result)
            {
                case 0:                                                                   // ERR_GUILD_COMMAND_SUCCESS
                    if (cmd == 1) SocText(string.Format(ErrGuildInvite, name));
                    else if (cmd == 3) SocText(string.Format(ErrGuildQuit, name));
                    else if (cmd == 0)
                    {
                        SocText(string.Format(ErrGuildCreate, name));                     // the charter was turned in: the guild exists
                        if (_charter != null) { _charter = null; SocChanged(SocialPart.Charter); }
                    }
                    else if (cmd == 14) SocText(string.Format(ErrGuildFounder, name));
                    break;
                case 1: SocText(ErrGuildInternal, true); break;
                case 2: SocText(ErrAlreadyInGuild, true); break;
                case 3: SocText(string.Format(ErrAlreadyInGuildS, name), true); break;
                case 5: SocText(string.Format(ErrAlreadyInvitedS, name), true); break;
                case 6:
                    if (cmd == 0) SocText(ErrNameInvalid, true);                          // charter name (PetitionsHandler.cpp:144-148)
                    else SocLogOnce("guildname6", "guild command " + cmd + " answered NAME_INVALID (promote / demote of yourself, Guild.cpp:1625-1629) - no line");
                    break;
                case 7: SocText(string.Format(ErrGuildNameExists, name), true); break;
                case 8: SocText(cmd == 3 || cmd == 8 ? ErrGuildLeaderLeave : ErrGuildPermissions, true); break;   // Guild.h:127-128 both = 8
                case 9: SocText(ErrGuildNotInGuild, true); break;
                case 10: SocText(string.Format(ErrGuildNotInGuildS, name), true); break;
                case 11: SocText(string.Format(ErrGuildPlayerNotFound, name), true); break;
                case 12: SocText(ErrGuildNotAllied, true); break;
                case 13: SocText(string.Format(ErrGuildRankTooHigh, name), true); break;
                case 14: SocText(string.Format(ErrGuildRankTooLow, name), true); break;
                case 17: SocText(ErrGuildRanksLocked, true); break;
                case 18: SocText(ErrGuildRankInUse, true); break;
                case 20: GldRefresh(false); break;                                        // ERR_GUILD_UNK1 "forces roster update"
                default:
                    SocLogOnce("guildresult" + result, "SMSG_GUILD_COMMAND_RESULT command " + cmd + " result " + result + " - no line (no 3.3.5 text / guild bank)");
                    break;
            }
        }

        /// <summary>SMSG_GUILD_INVITE (GuildPackets.cpp:95-101): CString inviter; CString guild -> the popup (UiSocialHost; StaticPopup
        /// GUILD_INVITE: 60 s, OnCancel = decline - the session keeps no timer of its own).</summary>
        void GldOnInvite(WowPacket p)
        {
            string from = p.CString(), guild = p.CString();
            _social.PendingGuildInviteFrom = from;
            _social.PendingGuildInviteGuild = guild;
            _social.PendingGuildInviteSince = (float)_now;
            _events.Enqueue(new GameEvent { Type = GameEventType.GuildInvite, Text = from });
            SocChanged(SocialPart.Guild);
            WowLog.Info("Online guild: invitation received - PendingGuildInviteFrom set (Accept / Decline)");
        }

        // ------------------------------------------------------------------ guild actions (GuildHandler.cpp)

        void GldSendName(ushort op, string name, int max)
        {
            string n = SocClean(name, max).Trim();
            if (n.Length == 0 || _world == null) return;
            _world.Send(op, new PacketWriter().CString(n));
        }

        public void GuildInvite(string name) { GldSendName(Op.CMSG_GUILD_INVITE, name, GldInviteMax); }               // :42-48

        public void AnswerGuildInvite(bool accept)
        {
            if (_social.PendingGuildInviteFrom == null || _world == null) return;
            _world.Send(accept ? Op.CMSG_GUILD_ACCEPT : Op.CMSG_GUILD_DECLINE);                                          // :59-87 (empty)
            _social.PendingGuildInviteFrom = null;
            _social.PendingGuildInviteGuild = null;
            SocChanged(SocialPart.Guild);
        }

        public void GuildLeave() { if (_world != null) _world.Send(Op.CMSG_GUILD_LEAVE); }                              // :125-131
        public void GuildRemove(string name) { GldSendName(Op.CMSG_GUILD_REMOVE, name, SocNameMax); }                  // :50-57
        public void GuildPromote(string name) { GldSendName(Op.CMSG_GUILD_PROMOTE, name, SocNameMax); }                // :107-114
        public void GuildDemote(string name) { GldSendName(Op.CMSG_GUILD_DEMOTE, name, SocNameMax); }                  // :116-123
        public void GuildSetLeader(string name) { GldSendName(Op.CMSG_GUILD_LEADER, name, SocNameMax); }               // :141-148
        public void GuildDisband() { if (_world != null) _world.Send(Op.CMSG_GUILD_DISBAND); }                         // :133-139

        public void GuildSetMotd(string motd)
        {
            if (_world == null) return;
            _world.Send(Op.CMSG_GUILD_MOTD, new PacketWriter().CString(SocClean(motd, GldMotdMax)));                    // :150-156 (GE_MOTD answers)
        }

        public void GuildSetInfo(string info)
        {
            if (_world == null) return;
            string t = SocClean(info, GldInfoMax);
            _world.Send(Op.CMSG_GUILD_INFO_TEXT, new PacketWriter().CString(t));                                        // :225-231 (no event)
            if (_social.Guild != null) { _social.Guild.Info = t; SocChanged(SocialPart.Guild); }
            GldRefresh(false);                                                                                          // the roster's InfoText carries it
        }

        public void GuildSetNote(string memberName, string note, bool officerNote)
        {
            string n = SocClean(memberName, SocNameMax).Trim();
            if (n.Length == 0 || _world == null) return;
            // CMSG_GUILD_SET_PUBLIC_NOTE / _OFFICER_NOTE: CString name; CString note (GuildPackets.cpp:218-222)
            _world.Send(officerNote ? Op.CMSG_GUILD_SET_OFFICER_NOTE : Op.CMSG_GUILD_SET_PUBLIC_NOTE, new PacketWriter().CString(n).CString(SocClean(note, GldNoteMax)));
            GldRefresh(false);
        }

        /// <summary>CMSG_GUILD_RANK (GuildPackets.cpp:180-192): uint32 rank; uint32 rights | GR_RIGHT_EMPTY (always, R2); CString name;
        /// uint32 goldPerDay; 6 x (uint32 tabRights, uint32 tabSlots) - the bank values echoed from the roster's rank row.</summary>
        public void GuildSetRank(int rankIndex, string name, uint rights)
        {
            if (_world == null || rankIndex < 0 || rankIndex >= 10) return;
            GuildRankView rv = _social.Guild != null && rankIndex < _social.Guild.Ranks.Count ? _social.Guild.Ranks[rankIndex] : null;
            var w = new PacketWriter().U32((uint)rankIndex).U32(rights | GuildRights.Empty).CString(SocClean(name, GldRankNameMax)).U32(rv != null ? rv.GoldPerDay : 0u);
            for (int t = 0; t < 6; t++) w.U32(rv != null ? rv.TabRights[t] : 0u).U32(rv != null ? rv.TabSlotsPerDay[t] : 0u);
            _world.Send(Op.CMSG_GUILD_RANK, w);
        }

        public void GuildAddRank(string name) { GldSendName(Op.CMSG_GUILD_ADD_RANK, name, GldRankNameMax); }          // :209-215
        public void GuildRemoveLastRank() { if (_world != null) _world.Send(Op.CMSG_GUILD_DEL_RANK); }                 // :217-223 (the lowest)
        public void GuildRefresh() { GldRefresh(true); }

        // ------------------------------------------------------------------ guild charter (3.2, PetitionsHandler.cpp)

        public void OpenGuildRegistrar(ulong npcGuid)
        {
            if (_world == null || npcGuid == 0) return;
            _world.Send(Op.CMSG_PETITION_SHOWLIST, new PacketWriter().U64(npcGuid));                                    // :838-846
        }

        void GldOnShowList(WowPacket p)
        {
            var c = new GuildCharterView();
            int n = SocialRead.PetitionShowList(p, c);
            NpcServiceOpened(c.NpcGuid, "guild registrar");                    // npc H4: the gossip dialog closes, the npc watchdog stops
            if (n != 1)
            {
                SocText(GldArenaCharters);
                WowLog.Info("Online guild: an arena organizer's charter list (" + n + " entries) - arena charters are not offered (gap G1)");
                return;
            }
            c.Counter++;
            _charter = c;
            SocChanged(SocialPart.Charter);
            WowLog.Info("Online guild: registrar - charter " + c.CharterEntry + ", cost " + c.Cost + " copper, " + c.RequiredSignatures + " signatures on this realm");
        }

        /// <summary>CMSG_PETITION_BUY (PetitionsHandler.cpp:32-61): uint64 npc; uint32 0; uint64 0; CString name; CString ""; 7 x uint32 0;
        /// uint16 0; 3 x uint32 0; 10 x CString ""; uint32 clientIndex (1 = guild); uint32 0. The registrar closes like WoW's
        /// (GuildRegistrarFrame.xml:263-264 BuyGuildCharter + HideUIPanel); the charter item arrives in the bags.</summary>
        public void BuyGuildCharter(string guildName)
        {
            if (_world == null || _charter == null || !_charter.Registrar) return;
            string n = SocClean(guildName, GldGuildNameMax).Trim();
            if (n.Length == 0) return;
            var w = new PacketWriter().U64(_charter.NpcGuid).U32(0).U64(0).CString(n).CString("");
            for (int i = 0; i < 7; i++) w.U32(0);
            w.U16(0);
            for (int i = 0; i < 3; i++) w.U32(0);
            for (int i = 0; i < 10; i++) w.CString("");
            w.U32(1).U32(0);
            _world.Send(Op.CMSG_PETITION_BUY, w);
            _charter = null;
            SocChanged(SocialPart.Charter);
            WowLog.Info("Online guild: guild charter bought (" + n.Length + " letters)");
        }

        /// <summary>The charter item was used (npc's UseItemAt, ITEM_FLAG_PETITION): CMSG_PETITION_SHOW_SIGNATURES (uint64 item, :236-273) +
        /// CMSG_PETITION_QUERY (uint32 petitionId or 0, uint64 item, :275-286). Which client action shows an owner his signatures is
        /// Wow.exe's - "use the charter" is ours, matching AzerothCore's only path for it.</summary>
        public void ShowCharter(ulong itemGuid)
        {
            if (_world == null || itemGuid == 0) return;
            uint known = _charter != null && _charter.PetitionGuid == itemGuid ? _charter.PetitionId : 0u;
            _world.Send(Op.CMSG_PETITION_SHOW_SIGNATURES, new PacketWriter().U64(itemGuid));
            _world.Send(Op.CMSG_PETITION_QUERY, new PacketWriter().U32(known).U64(itemGuid));
        }

        void GldOnSignatures(WowPacket p)
        {
            ulong pet, owner;
            uint pid;
            SocialRead.PetitionSignatures(p, out pet, out owner, out pid, _chGuids);
            var c = new GuildCharterView { Registrar = false, PetitionGuid = pet, OwnerGuid = owner, PetitionId = pid, Mine = owner == _player.Guid };
            _charter = c;
            _chSigned = false;
            if (c.Mine) c.OwnerName = _player.Name;
            else
            {
                SocWaitName(owner, n => { c.OwnerName = n; c.Counter++; SocChanged(SocialPart.Charter); });
                if (_world != null) _world.Send(Op.CMSG_PETITION_QUERY, new PacketWriter().U32(pid).U64(pet));   // an offered charter: its name
            }
            if (_chQueryId == pid && _chQueryName != null) GldApplyQuery(c);
            foreach (ulong g in _chGuids) SocWaitName(g, n => { c.Signers.Add(n); c.Counter++; SocChanged(SocialPart.Charter); });
            c.Counter++;
            SocChanged(SocialPart.Charter);
            WowLog.Info("Online guild: petition " + pid + " - " + (c.Mine ? "ours" : "offered to us") + ", " + _chGuids.Count + " signature(s)");
        }

        void GldOnPetitionQuery(WowPacket p)
        {
            ulong owner;
            string name;
            int needed;
            uint id = SocialRead.PetitionQuery(p, out owner, out name, out needed);
            _chQueryId = id;
            _chQueryName = name ?? "";
            _chQueryNeeded = needed;
            _chQueryOwner = owner;
            if (_charter != null && !_charter.Registrar && (_charter.PetitionId == id || _charter.PetitionId == 0)) GldApplyQuery(_charter);
        }

        void GldApplyQuery(GuildCharterView c)
        {
            c.GuildName = _chQueryName ?? "";
            c.Needed = _chQueryNeeded;
            if (c.PetitionId == 0) c.PetitionId = _chQueryId;
            c.Counter++;
            SocChanged(SocialPart.Charter);
        }

        public void OfferCharter(ulong playerGuid)
        {
            if (_world == null || _charter == null || _charter.Registrar || !_charter.Mine) return;
            if (playerGuid == 0 || !IsPlayerGuid(playerGuid) || playerGuid == _player.Guid) { SocText(ErrGenericNoTarget, true); return; }
            // CMSG_OFFER_PETITION (:568-657): uint32 junk; uint64 petition; uint64 player
            _world.Send(Op.CMSG_OFFER_PETITION, new PacketWriter().U32(0).U64(_charter.PetitionGuid).U64(playerGuid));
            SocWaitName(playerGuid, n => SocText(string.Format(ErrPetitionOffered, n)));      // WoW prints it client side
        }

        public void SignCharter()
        {
            if (_world == null || _charter == null || _charter.Registrar || _charter.Mine) return;
            _world.Send(Op.CMSG_PETITION_SIGN, new PacketWriter().U64(_charter.PetitionGuid).U8(1));                    // :400-545 (uint64, uint8)
            _chSigned = true;
        }

        public void DeclineCharter()
        {
            if (_world == null || _charter == null || _charter.Registrar || _charter.Mine) return;
            _world.Send(Op.MSG_PETITION_DECLINE, new PacketWriter().U64(_charter.PetitionGuid));                        // :547-566
            _charter = null;
            SocChanged(SocialPart.Charter);
        }

        void GldOnSignResult(WowPacket p)
        {
            ulong pet = p.U64(), signer = p.U64();
            int result = (int)p.U32();
            bool me = signer == _player.Guid;
            switch (result)
            {
                case 0:                                                                   // PETITION_SIGN_OK (Guild.h:176-183)
                    if (me)
                    {
                        SocText(ErrPetitionSigned);
                        if (_charter != null && !_charter.Mine && _charter.PetitionGuid == pet) { _charter = null; SocChanged(SocialPart.Charter); }
                    }
                    else
                    {
                        SocWaitName(signer, n =>
                        {
                            SocText(string.Format(ErrPetitionSignedS, n));
                            if (_charter != null && _charter.Mine && _charter.PetitionGuid == pet) { _charter.Signers.Add(n); _charter.Counter++; SocChanged(SocialPart.Charter); }
                        });
                    }
                    break;
                case 1: SocText(ErrPetitionAlreadySigned, true); break;
                case 2: SocText(ErrPetitionInGuild, true); break;
                case 3: SocText(ErrPetitionCreator, true); break;
                case 4: SocText(ErrPetitionNotSameServer, true); break;
                default: SocLogOnce("signresult" + result, "SMSG_PETITION_SIGN_RESULTS " + result + " - no line"); break;
            }
        }

        /// <summary>The registrar's "Register a Guild Charter" (TurnInGuildCharter): CMSG_TURN_IN_PETITION (uint64, :659-836) with the
        /// guild charter item (entry 5863) of the bags.</summary>
        public void TurnInCharter()
        {
            if (_world == null) return;
            ulong item = 0;
            int entry = _charter != null && _charter.Registrar && _charter.CharterEntry > 0 ? _charter.CharterEntry : GuildCharterEntry;
            foreach (var bp in _bagPos)
            {
                OnlineObject o;
                if (_objects.TryGetValue(bp.Guid, out o) && o.Entry == entry) { item = bp.Guid; break; }
            }
            if (item == 0) { SocText(ErrNoGuildCharter, true); return; }
            _world.Send(Op.CMSG_TURN_IN_PETITION, new PacketWriter().U64(item));
        }

        void GldOnTurnIn(WowPacket p)
        {
            int r = (int)p.U32();
            switch (r)
            {
                case 0:                                                                   // PETITION_TURN_OK: the command result + roster already said it
                    if (_charter != null) { _charter = null; SocChanged(SocialPart.Charter); }
                    WowLog.Info("Online guild: charter turned in - the guild exists");
                    break;
                case 2: SocText(ErrPetitionInGuild, true); break;
                case 4: SocText(ErrPetitionNotEnough, true); break;
                default: SocLogOnce("turnin" + r, "SMSG_TURN_IN_PETITION_RESULTS " + r + " - no line"); break;
            }
        }

        /// <summary>The petition window's Rename Guild: MSG_PETITION_RENAME (uint64 petition item, CString name &lt;= 24, :335-343); the
        /// server answers the same opcode with the new name (:393-396) or a guild command result (name exists / invalid).</summary>
        public void RenameCharter(string guildName)
        {
            if (_world == null || _charter == null || _charter.Registrar || !_charter.Mine) return;
            string n = SocClean(guildName, GldGuildNameMax).Trim();
            if (n.Length == 0) return;
            _world.Send(Op.MSG_PETITION_RENAME, new PacketWriter().U64(_charter.PetitionGuid).CString(n));
        }

        void GldOnRename(WowPacket p)
        {
            ulong pet = p.U64();
            string name = p.CString();
            if (_charter != null && _charter.PetitionGuid == pet) { _charter.GuildName = name ?? ""; _charter.Counter++; SocChanged(SocialPart.Charter); }
        }

        /// <summary>Closes the registrar / petition window. Closing a charter OFFERED to us that we did not sign declines it (ours, proposal
        /// 4.7 - PetitionFrame's ClosePetition, PetitionFrame.xml:375-389).</summary>
        public void CloseCharter()
        {
            if (_charter == null) return;
            if (!_charter.Registrar && !_charter.Mine && !_chSigned && _world != null)
                _world.Send(Op.MSG_PETITION_DECLINE, new PacketWriter().U64(_charter.PetitionGuid));
            _charter = null;
            SocChanged(SocialPart.Charter);
        }
    }
}
