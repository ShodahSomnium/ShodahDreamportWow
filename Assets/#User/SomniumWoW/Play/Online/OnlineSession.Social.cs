using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Social;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 11 (owner social, proposal-social.md 3.3-3.6 / 6.3.1-6.3.2): the social side of the realm - IWowSocial (WowSocialTypes.cs)
    // across OnlineSession.Social (friends, ignore, Who, AFK / DND, text emotes, chat channels, the shared helpers), .Guild (guild + guild
    // charters) and .Mail (the mailbox). The methods are public and implicit (other areas call OpenMailbox / ShowCharter /
    // OpenGuildRegistrar by name); State / Charter / Mailbox are explicit so the session has no ambiguous public State.
    // Every action answers like WoW: the server's result -> its GlobalStrings.lua text (enUS, key + line in a comment) as one Info / Error
    // event + one System chat line, then GameEventType.SocialChanged (coalesced: one per Tick, Amount = the SocialPart flags).
    // Ours (named, logged once in InitSocial): names wait SocNameWait for the name query then read "Unknown"; "Player not found." after
    // SocAddWait for an unanswered add friend / ignore (AzerothCore answers nothing for unknown names, Socialhandler.cpp:46-51); the contact
    // list re-ask throttle; '|' -> '/' in every guild / mail / note / channel text (a '|' in a NoHyperlinks string can KICK the player,
    // WorldSession.cpp:531-539); the channel numbers (WowChannelRules); Who answers also print in chat (up to SocWhoChatRows players + the
    // total line); the text-emote fallbacks (WowEmoteTable.Line).
    public sealed partial class OnlineSession : IWowSocial
    {
        // ---- ours
        const double SocNameWait = 3.0;                 // s a line waits for a player's name query
        const double SocAddWait = 3.0;                  // s before an unanswered add friend / ignore reads "Player not found."
        const double SocContactEvery = 30.0;            // s between two CMSG_CONTACT_LIST of RefreshFriends (the Friends tab asks while shown)
        const double SocGuildNamesEvery = 0.5;          // s between two EntityState.GuildName passes over the visible players
        const int SocWhoChatRows = 3;                   // Who answers with up to this many players print every row in chat (else the total)
        const int SocNoteMax = 48;                      // SET_FRIENDNOTE maxLetters 48 (StaticPopup.lua:1749)
        const int SocNameMax = 48;                      // a typed character name (12 letters; 48 bytes covers UTF-8)
        const int SocChannelNameMax = 99;               // ChannelHandler.cpp: names of 100 bytes and more are refused

        // ---- WoW's texts (Interface\FrameXML\GlobalStrings.lua of the 3.3.5a enUS install, line numbers of that file)
        const string SocUnknown = "Unknown";                                                         // UNKNOWN (7899)
        const string ErrFriendDbError = "Friend lookup database error.";                             // ERR_FRIEND_DB_ERROR (3054)
        const string ErrFriendListFull = "You don't have room for any more friends.";                 // ERR_FRIEND_LIST_FULL (3057)
        const string ErrFriendNotFound = "Player not found.";                                        // ERR_FRIEND_NOT_FOUND (3058)
        const string ErrFriendOffline = "{0} has gone offline.";                                     // ERR_FRIEND_OFFLINE_S (3059)
        const string ErrFriendOnline = "[{0}] has come online.";                                     // ERR_FRIEND_ONLINE_SS (3060), link head stripped
        const string ErrFriendRemoved = "{0} removed from friends list.";                            // ERR_FRIEND_REMOVED_S (3061)
        const string ErrFriendAdded = "{0} added to friends.";                                       // ERR_FRIEND_ADDED_S (3052)
        const string ErrFriendAlready = "{0} is already your friend.";                               // ERR_FRIEND_ALREADY_S (3053)
        const string ErrFriendSelf = "You can't put yourself on your friend list.";                  // ERR_FRIEND_SELF (3062)
        const string ErrFriendWrongFaction = "Friends must be part of your alliance.";              // ERR_FRIEND_WRONG_FACTION (3063)
        const string ErrFriendError = "Unknown friend response from server.";                        // ERR_FRIEND_ERROR (3056)
        const string ErrIgnoreFull = "You can't ignore any more players.";                           // ERR_IGNORE_FULL (3128)
        const string ErrIgnoreSelf = "You can't ignore yourself.";                                   // ERR_IGNORE_SELF (3131)
        const string ErrIgnoreNotFound = "Player not found.";                                        // ERR_IGNORE_NOT_FOUND (3129)
        const string ErrIgnoreAlready = "{0} is already being ignored.";                             // ERR_IGNORE_ALREADY_S (3125)
        const string ErrIgnoreAdded = "{0} is now being ignored.";                                   // ERR_IGNORE_ADDED_S (3124)
        const string ErrIgnoreRemoved = "{0} is no longer being ignored.";                           // ERR_IGNORE_REMOVED_S (3130)
        const string ErrIgnoreAmbiguous = "That name is ambiguous, type more of the player's server name";   // ERR_IGNORE_AMBIGUOUS (3126)
        const string MarkedAfk = "You are now Away.";                                                // MARKED_AFK (4900)
        const string MarkedAfkMessage = "You are now Away: {0}";                                     // MARKED_AFK_MESSAGE (4901)
        const string MarkedDnd = "You are now Busy: {0}.";                                           // MARKED_DND (4902)
        const string ClearedAfk = "You are no longer Away.";                                         // CLEARED_AFK (1628)
        const string ClearedDnd = "You are no longer marked Busy.";                                  // CLEARED_DND (1629)
        const string DefaultDndMessage = "Busy";                                                     // DEFAULT_DND_MESSAGE (1952)
        const string ChatYouJoined = "Joined Channel: [{0}]";                                        // CHAT_YOU_JOINED_NOTICE (1613), link head stripped
        const string ChatYouChanged = "Changed Channel: [{0}]";                                      // CHAT_YOU_CHANGED_NOTICE (1611)
        const string ChatYouLeft = "Left Channel: [{0}]";                                            // CHAT_YOU_LEFT_NOTICE (1615)
        const string ChatChannelJoin = "[{0}] {1} joined channel.";                                  // "[N. Name] " + CHAT_CHANNEL_JOIN_GET (1411)
        const string ChatChannelLeave = "[{0}] {1} left channel.";                                   // "[N. Name] " + CHAT_CHANNEL_LEAVE_GET (1412)
        const string ChatChannelList = "[{0}] {1}";                                                  // CHAT_CHANNEL_LIST_GET (1413) + the members (ChatFrame.lua:2772-2774)
        const string ChatNotMember = "Not on channel {0}.";                                          // CHAT_NOT_MEMBER_NOTICE (1523)
        const string ChatInvalidName = "Invalid channel name";                                       // CHAT_INVALID_NAME_NOTICE (1460)
        const string ChatNotInArea = "[{0}] You are not in the correct area for this channel.";      // CHAT_NOT_IN_AREA_NOTICE (1522)
        const string ErrTooManyChannels = "You can only be in 10 channels at a time.";               // ERR_TOO_MANY_CHAT_CHANNELS (3539)

        readonly SocialState _social = new SocialState();
        SocialPart _socDirty;
        WowEmoteTable _socEmotes;
        WowWhoNames _socNames;
        WowChannelRules _socRules;

        struct SocWaitLine { public ulong Guid; public double Until; public Action<string> Line; }
        readonly List<SocWaitLine> _socWaits = new List<SocWaitLine>();
        readonly Dictionary<ulong, bool> _socFemale = new Dictionary<ulong, bool>();          // the name query's sex byte (female emote texts)
        readonly Dictionary<int, int> _socReserved = new Dictionary<int, int>();              // constant channel id -> its number after YOU_LEFT
        readonly HashSet<string> _socLogged = new HashSet<string>();
        readonly List<ulong> _socGuids = new List<ulong>();
        readonly HashSet<int> _socUsed = new HashSet<int>();
        string _socAddFriend, _socAddIgnore;
        double _socAddFriendAt, _socAddIgnoreAt, _socContactAskedAt = -1000, _socGuildNamesAt;
        bool _socFlagsKnown, _socJoinedAtLogin, _socWhoAsked;
        uint _socFlags;
        string _socAfkText, _socDndText;
        int _socGroupType, _socGroupFlags;                                                     // SMSG_GROUP_LIST bytes 0 / 2 (Chat.cs uses them)
        int _socTextEmotes, _socChannelNotices, _socFriendStatus, _socWhoAnswers;

        public IWowSocial Social { get { return this; } }
        SocialState IWowSocial.State { get { return _social; } }

        partial void InitSocial()
        {
            _world.On(Op.SMSG_CONTACT_LIST, SocOnContactList, this);
            _world.On(Op.SMSG_FRIEND_STATUS, SocOnFriendStatus, this);
            _world.On(Op.SMSG_WHO, SocOnWho, this);
            _world.On(Op.SMSG_CHANNEL_NOTIFY, SocOnChannelNotify, this);
            _world.On(Op.SMSG_CHANNEL_LIST, SocOnChannelList, this);
            _world.On(Op.SMSG_TEXT_EMOTE, SocOnTextEmote, this);
            _world.On(Op.SMSG_EMOTE, SocOnEmote, this);
            _world.On(Op.SMSG_INIT_WORLD_STATES, SocOnWorldStates, this);   // a peek: other areas may read it too (the dispatcher rewinds per handler)
            _world.On(Op.SMSG_NAME_QUERY_RESPONSE, SocOnNameQuery, this);  // after Objects.cs's handler: the name is stored, we read the sex byte
            _world.On(Op.SMSG_GROUP_LIST, SocOnGroupList, this);            // a peek for the chat kinds (raid / battleground / assistant)
            WowLog.Info("Online social: ours (not WoW's) - names wait " + SocNameWait + " s then read \"" + SocUnknown + "\"; \"" + ErrFriendNotFound
                        + "\" after " + SocAddWait + " s for an unanswered add friend / ignore; contact list re-asked at most every " + SocContactEvery
                        + " s; '|' -> '/' in guild / mail / note / channel texts; channel numbers General 1, Trade 2, LocalDefense 3, LookingForGroup 4, "
                        + "others from 5; Who answers print in chat (up to " + SocWhoChatRows + " players, else the total); guild names of other players "
                        + "refreshed every " + SocGuildNamesEvery + " s.");
        }

        partial void TickSocial(float dt)
        {
            SocTickWaits();
            SocTickPendingAdds();
            SocTickFlags();
            SocTickGuildNames();
            GldTick();
            MailTick();
            if (_socDirty != SocialPart.None)
            {
                _events.Enqueue(new GameEvent { Type = GameEventType.SocialChanged, Amount = (int)_socDirty });
                _socDirty = SocialPart.None;
            }
        }

        // ------------------------------------------------------------------ shared helpers (Guild.cs / Mail.cs / Chat.cs use them)

        WowEmoteTable SocEmotes()
        {
            if (_socEmotes == null)
            {
                _socEmotes = WowEmoteTable.Get(_files);
                WowLog.Info("Online social: emote table - " + _socEmotes.Count + " commands from the install (" + _socEmotes.Commands + " command strings, "
                            + _socEmotes.Unresolved + " unresolved)" + (_socEmotes.Problem != null ? " - " + _socEmotes.Problem : ""));
            }
            return _socEmotes;
        }

        WowWhoNames SocNames()
        {
            if (_socNames == null)
            {
                _socNames = WowWhoNames.Get(_files);
                if (_socNames.Problem != null) WowLog.Warn("Online social: zone / race / class names unavailable - " + _socNames.Problem);
            }
            return _socNames;
        }

        WowChannelRules SocRules()
        {
            if (_socRules == null)
            {
                _socRules = WowChannelRules.Get(_files);
                if (_socRules.Problem != null) WowLog.Warn("Online social: chat channels unavailable - " + _socRules.Problem);
            }
            return _socRules;
        }

        void SocChanged(SocialPart part)
        {
            _socDirty |= part;
            if ((part & (SocialPart.Friends | SocialPart.Ignore)) != 0) _social.FriendsCounter++;
            if ((part & SocialPart.Who) != 0) _social.WhoCounter++;
            if ((part & SocialPart.Guild) != 0) _social.GuildCounter++;
            if ((part & SocialPart.Channels) != 0) _social.ChannelsCounter++;
            if ((part & SocialPart.Mail) != 0) _social.MailCounter++;
        }

        /// <summary>A WoW answer: one System chat line + one Info (yellow) or Error (red) event (the GrpText pattern, Group.cs:636).</summary>
        void SocText(string text, bool error = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)ChatKind.System, Text = text });
            _events.Enqueue(new GameEvent { Type = error ? GameEventType.Error : GameEventType.Info, Text = text });
        }

        /// <summary>A chat line only (sign-on / off, channel notices, Who rows - WoW prints those in the chat frame alone).</summary>
        void SocLine(string text, ChatKind kind = ChatKind.System, ulong source = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)kind, SourceGuid = source, Text = text });
        }

        /// <summary>null -> "", '|' -> '/', cut to max characters (proposal 5.4: every guild / mail / note / channel text).</summary>
        static string SocClean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('|', '/');
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c < ' ' ? ' ' : c);
            s = sb.ToString();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        void SocAskName(ulong g)
        {
            if (_world == null || g == 0 || !IsPlayerGuid(g) || _playerNames.ContainsKey(g) || !_nameAsked.Add(g)) return;
            _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(g));   // NameQuery::Read: ObjectGuid (uint64)
            _queriesSent++;
        }

        /// <summary>Runs the line with the player's name now, or when the name query answers, or after SocNameWait with "Unknown".</summary>
        void SocWaitName(ulong g, Action<string> line)
        {
            string n = g != 0 ? ChatSender(g) : null;
            if (n != null || g == 0) { line(n ?? SocUnknown); return; }
            SocAskName(g);
            _socWaits.Add(new SocWaitLine { Guid = g, Until = _now + SocNameWait, Line = line });
        }

        void SocTickWaits()
        {
            for (int i = 0; i < _socWaits.Count; i++)
            {
                SocWaitLine w = _socWaits[i];
                string n = ChatSender(w.Guid);
                if (n == null && _now < w.Until) continue;
                _socWaits.RemoveAt(i);
                i--;
                try { w.Line(n ?? SocUnknown); }
                catch (Exception e) { WowLog.Warn("Online social: a waiting line failed - " + e.GetType().Name + " " + e.Message); }
            }
        }

        void SocLogOnce(string key, string text)
        {
            if (_socLogged.Add(key)) WowLog.Info("Online social: " + text);
        }

        bool SocInGuild() { return _self != null ? _self.U32(UF.PLAYER_GUILDID) != 0 : _social.Guild != null; }

        /// <summary>SMSG_NAME_QUERY_RESPONSE peek (NameQueryResponse::Write, QueryPackets.cpp:25-45): packed guid, uint8 unknown, name, realm,
        /// uint8 race, uint8 sex, uint8 class. The name itself is stored by Objects.cs; here the sex byte and the friend / ignore names.</summary>
        void SocOnNameQuery(WowPacket p)
        {
            ulong g = p.PackedGuid();
            if (p.U8() != 0) return;
            string name = p.CString();
            p.CString();
            p.U8();
            _socFemale[g] = p.U8() == 1;
            bool changed = false;
            foreach (var f in _social.Friends) if (f.Guid == g && f.Name != name) { f.Name = name; changed = true; }
            foreach (var x in _social.Ignored) if (x.Guid == g && x.Name != name) { x.Name = name; changed = true; }
            if (changed) SocChanged(SocialPart.Friends | SocialPart.Ignore);
        }

        void SocOnGroupList(WowPacket p)
        {
            _socGroupType = p.U8();          // GROUPTYPE_BG 0x01, GROUPTYPE_RAID 0x02 (Group.h:87-92)
            p.U8();
            _socGroupFlags = p.U8();         // our member flags: MEMBER_FLAG_ASSISTANT 0x01 (Group.h:74)
        }

        // ------------------------------------------------------------------ friends / ignore (3.3)

        public void AddFriend(string name, string note)
        {
            string n = SocClean(name, SocNameMax).Trim();
            if (n.Length == 0 || _world == null) return;
            _world.Send(Op.CMSG_ADD_FRIEND, new PacketWriter().CString(n).CString(SocClean(note, SocNoteMax)));   // Socialhandler.cpp:38-90
            _socAddFriend = n;
            _socAddFriendAt = _now;
            WowLog.Info("Online social: add friend (" + n.Length + " letters)");
        }

        public void RemoveFriend(ulong guid)
        {
            if (guid == 0 || _world == null) return;
            _world.Send(Op.CMSG_DEL_FRIEND, new PacketWriter().U64(guid));                                         // Socialhandler.cpp:92-102
        }

        public void SetFriendNote(ulong guid, string note)
        {
            if (guid == 0 || _world == null) return;
            string n = SocClean(note, SocNoteMax);
            _world.Send(Op.CMSG_SET_CONTACT_NOTES, new PacketWriter().U64(guid).CString(n));                       // Socialhandler.cpp:148-154 (no answer)
            foreach (var f in _social.Friends) if (f.Guid == guid) f.Note = n;
            SocChanged(SocialPart.Friends);
        }

        public void AddIgnore(string name)
        {
            string n = SocClean(name, SocNameMax).Trim();
            if (n.Length == 0 || _world == null) return;
            _world.Send(Op.CMSG_ADD_IGNORE, new PacketWriter().CString(n));                                        // Socialhandler.cpp:104-137
            _socAddIgnore = n;
            _socAddIgnoreAt = _now;
        }

        public void RemoveIgnore(ulong guid)
        {
            if (guid == 0 || _world == null) return;
            _world.Send(Op.CMSG_DEL_IGNORE, new PacketWriter().U64(guid));                                         // Socialhandler.cpp:139-146
        }

        public void RefreshFriends()
        {
            if (_world == null || _now - _socContactAskedAt < SocContactEvery) return;
            _socContactAskedAt = _now;
            _world.Send(Op.CMSG_CONTACT_LIST, new PacketWriter().U32(1));                                          // Socialhandler.cpp:30-36 (flags)
        }

        void SocTickPendingAdds()
        {
            if (_socAddFriend != null && _now - _socAddFriendAt >= SocAddWait)
            {
                _socAddFriend = null;
                SocText(ErrFriendNotFound, true);
                WowLog.Info("Online social: add friend - no answer in " + SocAddWait + " s (AzerothCore answers nothing for an unknown name)");
            }
            if (_socAddIgnore != null && _now - _socAddIgnoreAt >= SocAddWait)
            {
                _socAddIgnore = null;
                SocText(ErrIgnoreNotFound, true);
            }
        }

        void SocNameFriends()
        {
            WowWhoNames names = SocNames();
            foreach (var f in _social.Friends)
            {
                if (f.Name == null) { f.Name = ChatSender(f.Guid); if (f.Name == null) SocAskName(f.Guid); }
                f.ZoneName = f.Online ? names.ZoneName(f.ZoneId) : null;
            }
            foreach (var x in _social.Ignored)
                if (x.Name == null) { x.Name = ChatSender(x.Guid); if (x.Name == null) SocAskName(x.Guid); }
        }

        /// <summary>SMSG_CONTACT_LIST (sent unasked at login with flags 7, Player.cpp:11763; answers CMSG_CONTACT_LIST).</summary>
        void SocOnContactList(WowPacket p)
        {
            uint flags;
            SocialRead.ContactList(p, _social.Friends, _social.Ignored, out flags);
            SocNameFriends();
            SocChanged(SocialPart.Friends | SocialPart.Ignore);
            SocLogOnce("contacts", "contact list - " + _social.Friends.Count + " friend(s), " + _social.Ignored.Count + " ignored (first answer; later ones not logged)");
        }

        FriendView SocFriend(ulong g)
        {
            foreach (var f in _social.Friends) if (f.Guid == g) return f;
            return null;
        }

        /// <summary>SMSG_FRIEND_STATUS (SocialMgr.cpp:263-298) - the results of SocialMgr.h:64-95 with their GlobalStrings texts.</summary>
        void SocOnFriendStatus(WowPacket p)
        {
            ulong g;
            string note;
            int status, area, level, cls;
            int r = SocialRead.FriendStatus(p, out g, out note, out status, out area, out level, out cls);
            _socFriendStatus++;
            if (r <= 10 && r != 2 && r != 3 && r != 5) _socAddFriend = null;           // an answer to the add (2 / 3 / 5 are not)
            if (r >= 11 && r <= 17) _socAddIgnore = null;
            FriendView f;
            switch (r)
            {
                case 0: SocText(ErrFriendDbError, true); break;
                case 1: SocText(ErrFriendListFull, true); break;
                case 2:                                                                   // FRIEND_ONLINE (also re-sent on AFK / DND changes)
                    f = SocFriend(g);
                    bool was = f != null && f.Online;
                    if (f != null)
                    {
                        f.Online = true; f.Afk = (status & 2) != 0; f.Dnd = (status & 4) != 0; f.ZoneId = area; f.Level = level; f.Class = (WowClass)cls;
                        f.ZoneName = SocNames().ZoneName(area);
                        SocChanged(SocialPart.Friends);
                    }
                    if (!was) SocWaitName(g, n => SocLine(string.Format(ErrFriendOnline, n)));
                    break;
                case 3:                                                                   // FRIEND_OFFLINE
                    f = SocFriend(g);
                    if (f != null) { f.Online = false; f.Afk = f.Dnd = false; f.ZoneId = 0; f.ZoneName = null; SocChanged(SocialPart.Friends); }
                    SocWaitName(g, n => SocLine(string.Format(ErrFriendOffline, n)));
                    break;
                case 4: SocText(ErrFriendNotFound, true); break;
                case 5:                                                                   // FRIEND_REMOVED
                    _social.Friends.RemoveAll(x => x.Guid == g);
                    SocChanged(SocialPart.Friends);
                    SocWaitName(g, n => SocText(string.Format(ErrFriendRemoved, n)));
                    break;
                case 6:
                case 7:                                                                   // FRIEND_ADDED_ONLINE / _OFFLINE
                    _social.Friends.RemoveAll(x => x.Guid == g);
                    f = new FriendView { Guid = g, Note = note ?? "", Online = r == 6, Afk = (status & 2) != 0, Dnd = (status & 4) != 0 };
                    if (r == 6) { f.ZoneId = area; f.Level = level; f.Class = (WowClass)cls; }
                    _social.Friends.Add(f);
                    SocNameFriends();
                    SocChanged(SocialPart.Friends);
                    SocWaitName(g, n => SocText(string.Format(ErrFriendAdded, n)));
                    break;
                case 8: SocWaitName(g, n => SocText(string.Format(ErrFriendAlready, n), true)); break;
                case 9: SocText(ErrFriendSelf, true); break;
                case 10: SocText(ErrFriendWrongFaction, true); break;
                case 11: SocText(ErrIgnoreFull, true); break;
                case 12: SocText(ErrIgnoreSelf, true); break;
                case 13: SocText(ErrIgnoreNotFound, true); break;
                case 14: SocWaitName(g, n => SocText(string.Format(ErrIgnoreAlready, n), true)); break;
                case 15:                                                                  // FRIEND_IGNORE_ADDED
                    _social.Ignored.RemoveAll(x => x.Guid == g);
                    _social.Ignored.Add(new IgnoreView { Guid = g, Name = ChatSender(g) });
                    SocNameFriends();
                    SocChanged(SocialPart.Ignore);
                    SocWaitName(g, n => SocText(string.Format(ErrIgnoreAdded, n)));
                    break;
                case 16:                                                                  // FRIEND_IGNORE_REMOVED
                    _social.Ignored.RemoveAll(x => x.Guid == g);
                    SocChanged(SocialPart.Ignore);
                    SocWaitName(g, n => SocText(string.Format(ErrIgnoreRemoved, n)));
                    break;
                case 17: SocText(ErrIgnoreAmbiguous, true); break;
                case 28: SocText(ErrFriendError, true); break;
                default: SocLogOnce("friendstatus" + r, "SMSG_FRIEND_STATUS result " + r + " (mute list / unknown) - no line"); break;
            }
        }

        // ------------------------------------------------------------------ Who (3.4)

        public void Who(string query)
        {
            if (_world == null) return;
            WowWhoQuery q = WowWhoQuery.Parse(query ?? "", SocNames());
            _world.Send(Op.CMSG_WHO, q.Body());                                                                    // MiscHandler.cpp:220-414
            _social.WhoQuery = query ?? "";
            _socWhoAsked = true;
        }

        void SocOnWho(WowPacket p)
        {
            int total;
            SocialRead.Who(p, _social.Who, out total);
            _socWhoAnswers++;
            WowWhoNames names = SocNames();
            foreach (var w in _social.Who)
            {
                w.ZoneName = names.ZoneName(w.ZoneId);
                w.RaceName = names.RaceName(w.Race);
                w.ClassName = names.ClassName((int)w.Class);
            }
            _social.WhoTotal = total;
            SocChanged(SocialPart.Who);
            if (!_socWhoAsked) return;                                                    // only our own requests print
            _socWhoAsked = false;
            if (_social.Who.Count <= SocWhoChatRows)
                foreach (var w in _social.Who) SocLine(WowWhoQuery.Line(w));
            SocLine(WowWhoQuery.TotalLine(total));
        }

        // ------------------------------------------------------------------ AFK / DND (3.6)

        public void SetAfk(string message) { SocAway(ChatMsgAfk, message, true); }

        public void SetDnd(string message) { SocAway(ChatMsgDnd, message, false); }

        /// <summary>CMSG_MESSAGECHAT CHAT_MSG_AFK / DND in LANG_UNIVERSAL (the only types AzerothCore allows in it, ChatHandler.cpp:76-83);
        /// the server flips PLAYER_FLAGS (ChatHandler.cpp:608-656) and SocTickFlags prints WoW's line on the change.</summary>
        void SocAway(int type, string message, bool afk)
        {
            if (_world == null) return;
            string m = CleanOutgoing(message);
            _world.Send(Op.CMSG_MESSAGECHAT, new PacketWriter().U32((uint)type).U32(LangUniversal).CString(m));
            if (afk) _socAfkText = m.Length > 0 ? m : null;
            else _socDndText = m.Length > 0 ? m : null;
        }

        /// <summary>PLAYER_FLAGS AFK 0x2 / DND 0x4 of our own object (Player.h:460-461): the MARKED_* / CLEARED_* lines on a change.</summary>
        void SocTickFlags()
        {
            if (_self == null) return;
            uint f = _self.U32(UF.PLAYER_FLAGS);
            if (!_socFlagsKnown)
            {
                _socFlagsKnown = true;
                _socFlags = f;
                _social.Afk = (f & 0x2) != 0;
                _social.Dnd = (f & 0x4) != 0;
                return;
            }
            if (f == _socFlags) return;
            bool afk = (f & 0x2) != 0, dnd = (f & 0x4) != 0, wasAfk = (_socFlags & 0x2) != 0, wasDnd = (_socFlags & 0x4) != 0;
            _socFlags = f;
            if (afk != wasAfk)
            {
                _social.Afk = afk;
                SocText(afk ? (_socAfkText == null ? MarkedAfk : string.Format(MarkedAfkMessage, _socAfkText)) : ClearedAfk);
                SocChanged(SocialPart.Status);
            }
            if (dnd != wasDnd)
            {
                _social.Dnd = dnd;
                SocText(dnd ? string.Format(MarkedDnd, _socDndText ?? DefaultDndMessage) : ClearedDnd);
                SocChanged(SocialPart.Status);
            }
        }

        // ------------------------------------------------------------------ text emotes (3.6)

        public bool TextEmote(string command, ulong targetGuid)
        {
            int id = SocEmotes().Find(command);
            if (id == 0) return false;
            TextEmoteById(id, targetGuid);
            return true;
        }

        public void TextEmoteById(int emotesTextId, ulong targetGuid)
        {
            if (_world == null || emotesTextId <= 0) return;
            if (_player.Dead)
            {
                SocLogOnce("emotedead", "text emote while dead - not sent (AzerothCore ignores them, ChatHandler.cpp:728)");
                return;
            }
            _world.Send(Op.CMSG_TEXT_EMOTE, new PacketWriter().U32((uint)emotesTextId).U32(0).U64(targetGuid));   // ChatHandler.cpp:714-787 (emoteNum 0, ours)
            int stand = SocEmotes().StandStateOf(emotesTextId);
            // SIT / SLEEP / KNEEL: the server leaves the stand state to the client (ChatHandler.cpp:751-773) - CMSG_STANDSTATECHANGE (uint32 state, MiscHandler.cpp:560-577)
            if (stand >= 0) _world.Send(Op.CMSG_STANDSTATECHANGE, new PacketWriter().U32((uint)stand));
        }

        /// <summary>SMSG_TEXT_EMOTE: the line as this player reads it (ChatKind.TextEmote) + GameEventType.TextEmote (the avatar's animation).</summary>
        void SocOnTextEmote(WowPacket p)
        {
            ulong sender;
            int id;
            string target;
            SocialRead.TextEmote(p, out sender, out id, out target);
            _socTextEmotes++;
            SocWaitName(sender, name => SocShowTextEmote(sender, name, id, target));
        }

        void SocShowTextEmote(ulong sender, string name, int id, string target)
        {
            WowEmoteTable t = SocEmotes();
            bool mine = sender == _player.Guid;
            bool female = mine ? _player.Gender == 1 : SocFemaleOf(sender);
            string me = _player.Name;
            string line = t.Line(id, mine ? me : name, female, target, me);
            int anim = t.AnimOf(id);
            ulong tg = SocFindByName(target);
            if (line != null) _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)ChatKind.TextEmote, SourceGuid = sender, TargetGuid = tg, Text = line });
            if (line != null || anim != 0)
                _events.Enqueue(new GameEvent { Type = GameEventType.TextEmote, SourceGuid = sender, TargetGuid = tg, SpellId = id, Amount = anim, Text = line });
            if (_socTextEmotes <= 10) WowLog.Info("Online social: text emote " + (t.TokenOf(id) ?? id.ToString()) + (mine ? " (ours)" : "") + ", anim " + anim);
        }

        bool SocFemaleOf(ulong g)
        {
            bool f;
            return _socFemale.TryGetValue(g, out f) && f;
        }

        ulong SocFindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (string.Equals(name, _player.Name, StringComparison.OrdinalIgnoreCase)) return _player.Guid;
            foreach (var e in _entities) if (e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e.Guid;
            return 0;
        }

        /// <summary>SMSG_EMOTE (ChatPackets.cpp:20-26): a unit plays an Emotes.dbc emote - its view plays the AnimationData id once
        /// (WowEntityView: EntityState.Emote on EmoteCounter). Our own avatar plays from the TextEmote event instead.</summary>
        void SocOnEmote(WowPacket p)
        {
            int emoteId;
            ulong g;
            SocialRead.Emote(p, out emoteId, out g);
            if (g == _player.Guid || emoteId <= 0) return;
            EntityState e = Find(g);
            if (e == null) return;
            int anim = SocEmotes().AnimOfEmote(emoteId);
            if (anim <= 0) return;
            e.Emote = anim;
            e.EmoteOnce = false;
            e.EmoteCounter++;
        }

        // ------------------------------------------------------------------ chat channels (3.5)

        /// <summary>SMSG_INIT_WORLD_STATES (every zone change, PlayerUpdates.cpp:1291-1296): the zone; the first one after login joins the
        /// client's default channels (the CLIENT joins at login: Player::UpdateLocalChannels returns while loading, PlayerUpdates.cpp:513-515).</summary>
        void SocOnWorldStates(WowPacket p)
        {
            int zone = SocialRead.ZoneOfWorldStates(p);
            if (zone <= 0) return;
            if (zone != _social.ZoneId) { _social.ZoneId = zone; SocChanged(SocialPart.Channels); }
            if (_socJoinedAtLogin) return;
            _socJoinedAtLogin = true;
            WowWhoNames names = SocNames();
            string zoneName = names.ZoneName(zone);
            if (zoneName == null) { WowLog.Warn("Online social: zone " + zone + " not in AreaTable - no channels joined at login"); return; }
            var joined = new List<string>();
            foreach (var r in SocRules().LoginJoins(names.ZoneFlags(zone), SocInGuild()))
            {
                string n = WowChannelRules.Name(r, zoneName);
                // CMSG_JOIN_CHANNEL (ChannelHandler.cpp:25-74): uint32 channelId; uint8 0; uint8 0; CString name; CString password
                _world.Send(Op.CMSG_JOIN_CHANNEL, new PacketWriter().U32((uint)r.Id).U8(0).U8(0).CString(n).CString(""));
                joined.Add(r.Id + " " + n);
            }
            WowLog.Info("Online social: login channels in zone " + zone + ": " + (joined.Count == 0 ? "none" : string.Join(", ", joined.ToArray())));
        }

        ChannelView SocChannel(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var c in _social.Channels) if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>A channel by its number "1".."10" or its name; null = not joined.</summary>
        ChannelView SocChannelRef(string nameOrNumber)
        {
            string s = (nameOrNumber ?? "").Trim();
            int n;
            if (int.TryParse(s, out n))
            {
                foreach (var c in _social.Channels) if (c.Number == n) return c;
                return null;
            }
            return SocChannel(s);
        }

        /// <summary>"N. Name" of a joined channel (Chat.cs puts it in front of a channel line, ChatFrame.lua:2922), else the name.</summary>
        string SocChannelDisplay(string name)
        {
            ChannelView c = SocChannel(name);
            return c != null ? c.Display : (name ?? "");
        }

        void SocAddChannel(ChannelView c)
        {
            int at = 0;
            while (at < _social.Channels.Count && _social.Channels[at].Number < c.Number) at++;
            _social.Channels.Insert(at, c);
        }

        void SocOnChannelNotify(WowPacket p)
        {
            string channel, name;
            ulong g1, g2;
            int flags, id;
            int type = SocialRead.ChannelNotify(p, out channel, out g1, out g2, out flags, out id, out name);
            _socChannelNotices++;
            ChannelView cv = SocChannel(channel);
            string disp = cv != null ? cv.Display : channel;
            switch (type)
            {
                case 0x00: if (cv != null) SocWaitName(g1, n => SocLine(string.Format(ChatChannelJoin, disp, n), ChatKind.ChannelNotice)); break;
                case 0x01: if (cv != null) SocWaitName(g1, n => SocLine(string.Format(ChatChannelLeave, disp, n), ChatKind.ChannelNotice)); break;
                case 0x02: SocYouJoined(channel, flags, id); break;
                case 0x03:                                                                // YOU_LEFT: uint32 id, uint8 isConstant
                    if (cv == null) break;
                    _social.Channels.Remove(cv);
                    if (flags != 0 && cv.ChannelId != 0) _socReserved[cv.ChannelId] = cv.Number;   // a constant channel keeps its number (ours, 3.5)
                    SocLine(string.Format(ChatYouLeft, cv.Display), ChatKind.ChannelNotice);
                    SocChanged(SocialPart.Channels);
                    break;
                case 0x21:                                                                // NOT_IN_LFG: the 3.3.5 client has no text for it
                    SocLogOnce("notinlfg", "channel " + channel + ": CHAT_NOT_IN_LFG_NOTICE (Channel.RestrictedLfg = 1, queue for a dungeon to join) - no line (no 3.3.5 text)");
                    break;
                case 0x0C:
                    SocLogOnce("modechange", "channel member mode changes (CHAT_MODE_CHANGE_NOTICE) are not shown (WoW shows none)");
                    break;
                default:
                    SocNoticeLine(type, disp, channel, g1, g2, name);
                    break;
            }
        }

        void SocYouJoined(string channel, int flags, int id)
        {
            ChannelView cv = SocChannel(channel);
            if (cv != null) { cv.Flags = flags; SocChanged(SocialPart.Channels); return; }
            if (id != 0)
                foreach (var c in _social.Channels)
                    if (c.ChannelId == id)
                    {
                        // the zone channel of a new zone replaces the old one without a YOU_LEFT (PlayerUpdates.cpp:573-577): "Changed Channel"
                        c.Name = channel;
                        c.Flags = flags;
                        SocLine(string.Format(ChatYouChanged, c.Display), ChatKind.ChannelNotice);
                        SocChanged(SocialPart.Channels);
                        return;
                    }
            _socUsed.Clear();
            foreach (var c in _social.Channels) _socUsed.Add(c.Number);
            foreach (var kv in _socReserved) if (kv.Key != id) _socUsed.Add(kv.Value);
            int number;
            if (id == 0 || !_socReserved.TryGetValue(id, out number) || _socUsed.Contains(number)) number = WowChannelRules.NumberFor(id, _socUsed);
            if (number <= 0 || _socUsed.Contains(number))
            {
                SocText(ErrTooManyChannels, true);
                WowLog.Warn("Online social: no free channel number for " + channel + " (" + _social.Channels.Count + " joined)");
                return;
            }
            cv = new ChannelView { Number = number, Name = channel, ChannelId = id, Flags = flags };
            SocAddChannel(cv);
            SocLine(string.Format(ChatYouJoined, cv.Display), ChatKind.ChannelNotice);
            SocChanged(SocialPart.Channels);
        }

        /// <summary>The other SMSG_CHANNEL_NOTIFY types: CHAT_&lt;TYPE&gt;_NOTICE of GlobalStrings.lua (1391-1606) with the channel link head
        /// stripped to "[N. Name]" (ChatFrame.lua:2791-2802), player names by name query.</summary>
        void SocNoticeLine(int type, string disp, string channel, ulong g1, ulong g2, string name)
        {
            string text = null;
            switch (type)
            {
                case 0x04: text = "Wrong password for " + channel + "."; break;                                  // CHAT_WRONG_PASSWORD_NOTICE (1606)
                case 0x05: text = string.Format(ChatNotMember, channel); break;                                   // CHAT_NOT_MEMBER_NOTICE (1523)
                case 0x06: text = "Not a moderator of [" + disp + "]."; break;                                    // CHAT_NOT_MODERATOR_NOTICE (1525)
                case 0x0A: text = "[" + disp + "] You are not the channel owner."; break;                         // CHAT_NOT_OWNER_NOTICE (1527)
                case 0x11: text = "[" + disp + "] You do not have permission to speak."; break;                   // CHAT_MUTED_NOTICE (1519)
                case 0x13: text = "[" + disp + "] You are banned from that channel."; break;                      // CHAT_BANNED_NOTICE (1397)
                case 0x19: text = "Target is in the wrong alliance for " + channel + "."; break;                  // CHAT_INVITE_WRONG_FACTION_NOTICE (1464)
                case 0x1A: text = "Wrong alliance for " + channel + "."; break;                                   // CHAT_WRONG_FACTION_NOTICE (1605)
                case 0x1B: text = ChatInvalidName; break;                                                         // CHAT_INVALID_NAME_NOTICE (1460)
                case 0x1C: text = channel + " is not moderated"; break;                                           // CHAT_NOT_MODERATED_NOTICE (1524)
                case 0x1F: text = "[" + disp + "] The number of messages that can be sent to this channel is limited, please wait to send another message."; break;   // CHAT_THROTTLED_NOTICE (1583)
                case 0x20: text = string.Format(ChatNotInArea, disp); break;                                      // CHAT_NOT_IN_AREA_NOTICE (1522)
                case 0x09: text = "[" + disp + "] Player " + name + " was not found."; break;                     // CHAT_PLAYER_NOT_FOUND_NOTICE (1556)
                case 0x0B: text = "[" + disp + "] Channel owner is " + name + "."; break;                         // CHAT_CHANNEL_OWNER_NOTICE (1414)
                case 0x16: text = "[" + disp + "] Player " + name + " is not banned."; break;                     // CHAT_PLAYER_NOT_BANNED_NOTICE (1554)
                case 0x1D: text = "[" + disp + "] You invited " + name + " to join the channel"; break;           // CHAT_PLAYER_INVITED_NOTICE (1548)
                case 0x1E: text = "[" + disp + "] " + name + " has been banned."; break;                          // CHAT_PLAYER_INVITE_BANNED_NOTICE (1550)
            }
            if (text != null) { SocLine(text, ChatKind.ChannelNotice); return; }
            string f1 = null, f2 = null;
            switch (type)
            {
                case 0x07: f1 = "[{0}] Password changed by {1}."; break;                                          // CHAT_PASSWORD_CHANGED_NOTICE (1541)
                case 0x08: f1 = "[{0}] Owner changed to {1}."; break;                                             // CHAT_OWNER_CHANGED_NOTICE (1534)
                case 0x0D: f1 = "[{0}] Channel announcements enabled by {1}."; break;                             // CHAT_ANNOUNCEMENTS_ON_NOTICE (1393)
                case 0x0E: f1 = "[{0}] Channel announcements disabled by {1}."; break;                            // CHAT_ANNOUNCEMENTS_OFF_NOTICE (1391)
                case 0x0F: f1 = "[{0}] Channel moderation enabled by {1}."; break;                                // CHAT_MODERATION_ON_NOTICE (1474)
                case 0x10: f1 = "[{0}] Channel moderation disabled by {1}."; break;                               // CHAT_MODERATION_OFF_NOTICE (1472)
                case 0x17: f1 = "[{0}] Player {1} is already on the channel."; break;                             // CHAT_PLAYER_ALREADY_MEMBER_NOTICE (1544)
                case 0x18: f1 = "{1} has invited you to join the channel '" + channel + "'."; break;              // CHAT_INVITE_NOTICE (1461) - a line, no popup (ours)
                case 0x22: f1 = "[{0}] Channel voice enabled by {1}."; break;                                     // CHAT_VOICE_ON_NOTICE (1598)
                case 0x23: f1 = "[{0}] Channel voice disabled by {1}."; break;                                    // CHAT_VOICE_OFF_NOTICE (1595)
                case 0x12: f2 = "[{0}] Player {1} kicked by {2}."; break;                                         // CHAT_PLAYER_KICKED_NOTICE (1552)
                case 0x14: f2 = "[{0}] Player {1} banned by {2}."; break;                                         // CHAT_PLAYER_BANNED_NOTICE (1546)
                case 0x15: f2 = "[{0}] Player {1} unbanned by {2}."; break;                                       // CHAT_PLAYER_UNBANNED_NOTICE (1558)
            }
            if (f1 != null) { SocWaitName(g1, n => SocLine(string.Format(f1, disp, n), ChatKind.ChannelNotice)); return; }
            if (f2 != null) { SocWaitName(g1, a => SocWaitName(g2, b => SocLine(string.Format(f2, disp, a, b), ChatKind.ChannelNotice))); return; }
            SocLogOnce("notice" + type, "SMSG_CHANNEL_NOTIFY type 0x" + type.ToString("X2") + " - no line");
        }

        public void JoinChannel(string name, string password)
        {
            if (_world == null) return;
            string n = SocClean(name, SocChannelNameMax).Trim();
            if (n.Length == 0) return;
            if (char.IsDigit(n[0])) { SocText(ChatInvalidName, true); return; }       // AzerothCore refuses names starting with a digit (ChannelHandler.cpp)
            string full;
            WowWhoNames names = SocNames();
            WowChannelRules.Row row = SocRules().Match(n, names.ZoneName(_social.ZoneId), out full);
            uint id = 0;
            if (row != null)
            {
                if (!WowChannelRules.CanJoin(row, names.ZoneFlags(_social.ZoneId), SocInGuild()))
                {
                    SocText(string.Format(ChatNotInArea, full), true);                     // the server would refuse it silently (ours: the area text)
                    return;
                }
                id = (uint)row.Id;
                n = full;
            }
            _world.Send(Op.CMSG_JOIN_CHANNEL, new PacketWriter().U32(id).U8(0).U8(0).CString(n).CString(SocClean(password, 127)));
        }

        public void LeaveChannel(string name)
        {
            if (_world == null) return;
            ChannelView c = SocChannelRef(name);
            string n = c != null ? c.Name : SocClean(name, SocChannelNameMax).Trim();
            if (n.Length == 0) return;
            int num;
            if (c == null && int.TryParse(n, out num)) { SocText(string.Format(ChatNotMember, n), true); return; }
            _world.Send(Op.CMSG_LEAVE_CHANNEL, new PacketWriter().U32(0).CString(n));                 // ChannelHandler.cpp:76-90
        }

        public void ListChannel(string name)
        {
            if (_world == null) return;
            ChannelView c = SocChannelRef(name);
            if (c == null) { SocText(string.Format(ChatNotMember, (name ?? "").Trim()), true); return; }
            _world.Send(Op.CMSG_CHANNEL_LIST, new PacketWriter().CString(c.Name));                   // ChannelHandler.cpp:92-103
        }

        /// <summary>SMSG_CHANNEL_LIST -> ChannelView.Members (names by name query) and WoW's line "[N. Name] a, b, c" once every name is in.</summary>
        void SocOnChannelList(WowPacket p)
        {
            string name = SocialRead.ChannelList(p, _socGuids);
            ChannelView cv = SocChannel(name);
            if (cv == null) return;
            cv.Members.Clear();
            cv.MembersCounter++;
            SocChanged(SocialPart.Channels);
            var guids = new List<ulong>(_socGuids);
            var names = new string[guids.Count];
            int left = guids.Count;
            if (left == 0) { SocLine(string.Format(ChatChannelList, cv.Display, ""), ChatKind.ChannelNotice); return; }
            for (int i = 0; i < guids.Count; i++)
            {
                int k = i;
                SocWaitName(guids[i], n =>
                {
                    names[k] = n;
                    cv.Members.Add(n);
                    cv.MembersCounter++;
                    SocChanged(SocialPart.Channels);
                    if (--left == 0) SocLine(string.Format(ChatChannelList, cv.Display, string.Join(", ", names)), ChatKind.ChannelNotice);
                });
            }
        }

        // ------------------------------------------------------------------ other players' guild names (EntityState.GuildName, H12)

        void SocTickGuildNames()
        {
            if (_now - _socGuildNamesAt < SocGuildNamesEvery) return;
            _socGuildNamesAt = _now;
            foreach (var o in _objects.Values)
            {
                if (o.TypeId != TypeId.Player || o.State == null) continue;
                int gid = o.I32(UF.PLAYER_GUILDID);
                o.State.GuildName = gid != 0 ? GldNameOf(gid) : null;
            }
        }

        /// <summary>Counters for the probes and the session summary.</summary>
        public string SocialSummary()
        {
            return "friends " + _social.Friends.Count + ", ignored " + _social.Ignored.Count + ", channels " + _social.Channels.Count + ", guild "
                   + (_social.Guild != null ? _social.Guild.Name + " (" + _social.Guild.Members.Count + ")" : "none") + ", text emotes " + _socTextEmotes
                   + ", channel notices " + _socChannelNotices + ", friend status " + _socFriendStatus + ", who answers " + _socWhoAnswers
                   + ", mailbox " + (_mailbox != null ? _mailbox.Mails.Count + " mails" : "closed") + ", unread " + _social.HasUnreadMail;
        }
    }
}
