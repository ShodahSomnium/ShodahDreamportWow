using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 7 (owner Online, lead decision D13): GM commands for the in-world testing buttons, sent as chat like a real client types
    // them, and the server's system messages / notifications back as Info events.
    // Stage S1 (owner chat, PLAN10 s2): every SMSG_MESSAGECHAT / SMSG_GM_MESSAGECHAT type becomes a ChatMessage event (say, yell, emote,
    // party, whisper, monster say / yell / emote / whisper, system ...), unknown speakers are asked for by CMSG_NAME_QUERY and their line
    // waits for the answer (at most ChatNameWait), and SendChat sends CMSG_MESSAGECHAT say / yell / emote / party / whisper / guild.
    //
    // AzerothCore (master d80ce1d) facts this relies on (P/harness/Probes/Mount7/online/acsrc/ChatHandler.cpp, online7/acsrc/Chat.cpp, PlayerMisc.cpp,
    // P/acsrc/Player.cpp, Unit.cpp, ObjectMgr.cpp, SharedDefines.h, P/acsrc_chat/Hyperlinks.cpp):
    // - HandleMessagechatOpcode (ChatHandler.cpp 61-): uint32 type; uint32 lang; then per type (318-343): SAY / EMOTE / YELL / PARTY /
    //   GUILD / RAID...: CString msg; WHISPER: CString to, CString msg; CHANNEL: CString channel, CString msg.
    //   `if (lang == LANG_UNIVERSAL && type != CHAT_MSG_AFK && type != CHAT_MSG_DND)` = "Possible hacking-attempt" -> refused (76-83). So
    //   every line goes out in the character's faction language (LANG_COMMON 7 / LANG_ORCISH 1; every race of the faction has its skill;
    //   a GM in .gm on mode is switched to universal by the server, 238-240).
    // - Commands are parsed before anything is said: `if (ChatHandler(this).ParseCommands(msg.c_str())) return;` (371) - for an account
    //   without command rights _ParseCommands returns false ("Pretend commands don't exist for regular players") and the line is SAID aloud.
    //   Chat.cpp ParseCommands: a single '.' or '!' first, never "..": the UI sends '.'-lines through RunGmCommand, SendChat refuses them.
    // - Messages longer than 255 bytes or containing "|0" are dropped (361). The line is cut at the first \n / \r, any control character
    //   below 0x20 except tab drops it (isNasty, 42-55 + 381-405), and ValidateHyperlinksAndMaybeKick (WorldSession.cpp 895-907) KICKS the
    //   player for a '|' that is not "||", |H, |h, |c or |r (Hyperlinks.cpp CheckAllLinks 405-450). So SendChat escapes every '|' as "||"
    //   (what WoW's edit box does), turns control characters into spaces and cuts at 255 UTF-8 bytes.
    // - The sender hears his own line: Player::Say / Yell / TextEmote (Player.cpp 9573-9668) SendDirectMessage to himself; party chat is
    //   broadcast to the whole group including the sender (ChatHandler.cpp 464-469); Player::Whisper (9675-9724) sends CHAT_MSG_WHISPER to
    //   the target and CHAT_MSG_WHISPER_INFORM to the sender with sender = the TARGET (BuildChatPacket(data, msgType, lang, target, target, text)).
    //   No local echo is ever made: the chat log shows what the server sent, like WoW.
    // - Player::TextEmote: CHAT_MSG_EMOTE in LANG_UNIVERSAL; players of the other faction get the same line with an EMPTY text (9656-9664)
    //   - WoW prints CHAT_EMOTE_UNKNOWN "makes some strange gestures." (GlobalStrings.lua 1430).
    // - Flood: UpdateSpeakTime runs BEFORE the command is parsed (commands count): within ChatFlood.MessageDelay (1 s) of the previous
    //   message the counter grows, at ChatFlood.MessageCount (10) the player is muted for ChatFlood.MuteTime (10 s) (worldserver.conf.dist
    //   4355-4389). GMs with RBAC_PERM_SKIP_CHECK_CHAT_SPAM are never counted, and in master the REGULAR case falls through to `return`
    //   (PlayerMisc.cpp 44-49: only addon messages are counted today) - the GM pacing below keeps a normal account safe even if that changes.
    // - Answers: ChatHandler::BuildChatPacket (Chat.cpp 273-351): uint8 type; int32 lang; uint64 sender; uint32 0; then
    //   MONSTER_SAY / PARTY / YELL / WHISPER / EMOTE, RAID_BOSS_EMOTE / WHISPER, BATTLENET: uint32 len + senderName, uint64 receiver,
    //   [uint32 len + receiverName when the receiver is set and neither a player nor a pet]; WHISPER_FOREIGN: len + senderName, uint64
    //   receiver; BG_SYSTEM_*: uint64 receiver [+ len + name when not a player]; ACHIEVEMENT / GUILD_ACHIEVEMENT: uint64 receiver; every other
    //   type: [len + senderName when gmMessage], [CString channel for CHAT_MSG_CHANNEL], uint64 receiver. Then uint32 len + message,
    //   uint8 chatTag, [uint32 achievementId for the achievement types]. gmMessage (the sender has RBAC_PERM_COMMAND_GM_CHAT) switches the
    //   opcode to SMSG_GM_MESSAGECHAT (Chat.cpp 277). Every "len" counts the terminating NUL (`uint32(s.length() + 1)` then `<< s`).
    //   Chat tag (Player::GetChatTag, Player.cpp 1373-1389; Player.h 839-844): AFK 1, DND 2, GM 4, COM 8, DEV 16.
    // - ChatHandler::SendNotification -> SMSG_NOTIFICATION (CString); WorldSession::SendPlayerNotFoundNotice -> SMSG_CHAT_PLAYER_NOT_FOUND
    //   (CString name), SendPlayerAmbiguousNotice -> SMSG_CHAT_PLAYER_AMBIGUOUS (CString name), SendWrongFactionNotice ->
    //   SMSG_CHAT_WRONG_FACTION (empty) (ChatHandler.cpp 814-832).
    // - LANG_ADDON (0xFFFFFFFF) lines are addon traffic and never shown (the immersive-hold owner reads its own from SMSG_MESSAGECHAT;
    //   WorldClient calls every registered handler with the packet rewound).
    public sealed partial class OnlineSession
    {
        const int GmBurst = 8;                  // ours: fewer than AC's default ChatFlood.MessageCount 10 inside one flood window
        const double GmSpacing = 0.2;           // ours: s between the commands of a burst
        const double GmPause = 2.2;             // ours: > 2 x ChatFlood.MessageDelay 1 s: the flood counter restarts at 1
        const double ChatNameWait = 3.0;        // ours: s a line from an unknown player waits for SMSG_NAME_QUERY_RESPONSE before it is shown as "Unknown"
        const int ChatMaxBytes = 255;           // ChatHandler.cpp 361: msg.length() > 255 is dropped
        const int ChatNamesKept = 2000;         // ours: speaker names remembered from the packets (monsters, GM senders) before the cache is cleared

        // SharedDefines.h 3382-3437 ChatMsg
        const int ChatMsgSystem = 0x00, ChatMsgSay = 0x01, ChatMsgParty = 0x02, ChatMsgRaid = 0x03, ChatMsgGuild = 0x04, ChatMsgOfficer = 0x05,
            ChatMsgYell = 0x06, ChatMsgWhisper = 0x07, ChatMsgWhisperForeign = 0x08, ChatMsgWhisperInform = 0x09, ChatMsgEmote = 0x0A,
            ChatMsgTextEmote = 0x0B, ChatMsgMonsterSay = 0x0C, ChatMsgMonsterParty = 0x0D, ChatMsgMonsterYell = 0x0E, ChatMsgMonsterWhisper = 0x0F,
            ChatMsgMonsterEmote = 0x10, ChatMsgChannel = 0x11, ChatMsgChannelJoin = 0x12, ChatMsgChannelNoticeUser = 0x16, ChatMsgAfk = 0x17,
            ChatMsgDnd = 0x18, ChatMsgIgnored = 0x19, ChatMsgBgNeutral = 0x24, ChatMsgBgAlliance = 0x25, ChatMsgBgHorde = 0x26,
            ChatMsgRaidLeader = 0x27, ChatMsgRaidWarning = 0x28, ChatMsgRaidBossEmote = 0x29, ChatMsgRaidBossWhisper = 0x2A,
            ChatMsgFiltered = 0x2B, ChatMsgBattleground = 0x2C, ChatMsgBattlegroundLeader = 0x2D, ChatMsgRestricted = 0x2E,
            ChatMsgBattlenet = 0x2F, ChatMsgAchievement = 0x30, ChatMsgGuildAchievement = 0x31, ChatMsgPartyLeader = 0x33;
        // SharedDefines.h 721-742 Language
        const int LangUniversal = 0, LangOrcish = 1, LangCommon = 7, LangAddon = -1;
        // ObjectGuid.h 64 (HighGuid::Player = 0x0000) and HighGuid::Pet = 0xF140: the receiver-name rule of BuildChatPacket
        const int HighGuidPet = 0xF140;
        // Player.h 839-844
        public const int ChatTagAfk = 0x01, ChatTagDnd = 0x02, ChatTagGm = 0x04;

        // WoW's texts (Interface\FrameXML\GlobalStrings.lua of the 3.3.5a install, line numbers of that file)
        const string ErrChatPlayerNotFound = "No player named '{0}' is currently playing.";             // ERR_CHAT_PLAYER_NOT_FOUND_S (3001)
        const string ErrChatPlayerAmbiguous = "{0}: More than one player matches, type more of their server name";   // ERR_CHAT_PLAYER_AMBIGUOUS_S (3000)
        const string ErrChatWrongFaction = "You can only whisper to members of your alliance.";         // ERR_CHAT_WRONG_FACTION (3005)
        const string ErrNotInGroup = "You aren't in a party.";                                          // ERR_NOT_IN_GROUP (3311)
        const string ChatEmoteUnknown = "makes some strange gestures.";                                 // CHAT_EMOTE_UNKNOWN (1430)
        const string ChatSayUnknown = "says something unintelligible.";                                 // CHAT_SAY_UNKNOWN (1572)
        const string ChatIgnored = "{0} is ignoring you.";                                              // CHAT_IGNORED (1459)
        const string ChatRestricted = "A trial account may only send whispers to characters that have you on their friends list.";   // CHAT_RESTRICTED (1569)
        // iteration 11 (social)
        const string ErrNotInRaid = "You are not in a raid group";                                       // ERR_NOT_IN_RAID (3312)
        const int ChatGroupTypeBg = 0x01, ChatGroupTypeRaid = 0x02;                                       // GROUPTYPE_BG / GROUPTYPE_RAID (Group.h:87-92)
        // Achievement.dbc (talents' measured layout, proposal-talents.md:366; AC Achievementfmt DBCfmt.h:21): 62 fields, Title_lang at 4
        const int AchievementFieldCount = 62, AchievementTitle = 4;
        DbcTable _achievements;
        bool _achievementsTried;

        readonly Queue<string> _gmQueue = new Queue<string>();
        double _gmNextAt, _gmLastSentAt = -100;
        int _gmInBurst, _gmSent, _chatIgnored, _systemMessages;

        // stage S1 chat
        struct PendingChat { public GameEvent E; public ulong WaitFor; public double Until; }
        readonly List<PendingChat> _chatPending = new List<PendingChat>();
        readonly Dictionary<ulong, string> _chatNames = new Dictionary<ulong, string>();   // speaker names the packets carried (monsters, GMs, foreign whispers)
        readonly HashSet<int> _chatTypesLogged = new HashSet<int>();
        int _chatReceived, _chatShown, _chatSent, _chatAddon, _chatNameQueries, _chatNameTimeouts, _chatLogged;
        DbcTable _languages;
        bool _languagesTried;

        /// <summary>GM commands sent so far (probes).</summary>
        public int GmCommandsSent { get { return _gmSent; } }
        public int GmCommandsQueued { get { return _gmQueue.Count; } }
        /// <summary>Stage S1 (probes): chat lines received / shown (ChatMessage events) / sent, addon lines skipped, name queries asked for speakers.</summary>
        public int ChatReceived { get { return _chatReceived; } }
        public int ChatShown { get { return _chatShown; } }
        public int ChatSent { get { return _chatSent; } }
        public int ChatAddonSkipped { get { return _chatAddon; } }
        public int ChatNameQueries { get { return _chatNameQueries; } }
        public int ChatPendingCount { get { return _chatPending.Count; } }

        void RegisterChatHandlers()
        {
            _world.On(Op.SMSG_MESSAGECHAT, p => OnMessageChat(p, false), this);
            _world.On(Op.SMSG_GM_MESSAGECHAT, p => OnMessageChat(p, true), this);
            _world.On(Op.SMSG_NOTIFICATION, OnNotification, this);
            _world.On(Op.SMSG_CHAT_PLAYER_NOT_FOUND, p => ChatSystemLine(string.Format(ErrChatPlayerNotFound, p.CString()), "player not found"), this);
            _world.On(Op.SMSG_CHAT_PLAYER_AMBIGUOUS, p => ChatSystemLine(string.Format(ErrChatPlayerAmbiguous, p.CString()), "player ambiguous"), this);
            _world.On(Op.SMSG_CHAT_WRONG_FACTION, p => ChatSystemLine(ErrChatWrongFaction, "wrong faction"), this);
            WowLog.Info("Online: chat - ours (not WoW's): a line from an unknown player waits " + ChatNameWait + " s for its name query; lines in a language "
                        + "the character does not know read \"" + ChatSayUnknown + "\" (WoW scrambles the words); system lines also show as yellow messages.");
        }

        void QueueGmCommand(string command)
        {
            string c = command == null ? "" : command.Trim();
            if (c.Length < 2 || c[0] != '.' || c[1] == '.')
            {
                WowLog.Warn("Online: GM command refused - it must start with one '.': '" + c + "' (nothing sent)");
                return;
            }
            if (c.Length > 255 || c.IndexOf('\n') >= 0 || c.IndexOf('\r') >= 0 || c.IndexOf("|0", StringComparison.Ordinal) >= 0)
            {
                WowLog.Warn("Online: GM command refused - longer than 255, a line break or '|0' (AzerothCore drops those): '" + c + "'");
                return;
            }
            if (_world == null) { WowLog.Warn("Online: GM command '" + c + "' - no realm connection"); return; }
            _gmQueue.Enqueue(c);
            if (_gmQueue.Count == 2 || (_gmQueue.Count > 2 && _gmQueue.Count % 10 == 0))
                WowLog.Info("Online: GM commands queued: " + _gmQueue.Count + " waiting (" + GmBurst + " per burst, " + GmPause + " s pause - paced against the realm's chat flood rule)");
        }

        void TickGm()
        {
            TickChat();
            if (_gmQueue.Count == 0 || _world == null || _now < _gmNextAt) return;
            if (_now - _gmLastSentAt >= GmPause) _gmInBurst = 0;
            string c = _gmQueue.Dequeue();
            int lang = FactionLanguage();
            // CMSG_MESSAGECHAT (HandleMessagechatOpcode): uint32 type CHAT_MSG_SAY; uint32 language; CString message
            _world.Send(Op.CMSG_MESSAGECHAT, new PacketWriter().U32(ChatMsgSay).U32((uint)lang).CString(c));
            _gmSent++;
            _gmLastSentAt = _now;
            WowLog.Info("Online: GM command '" + c + "'" + (_gmQueue.Count > 0 ? " (" + _gmQueue.Count + " more queued)" : ""));
            if (++_gmInBurst >= GmBurst) { _gmInBurst = 0; _gmNextAt = _now + GmPause; }
            else _gmNextAt = _now + GmSpacing;
        }

        int FactionLanguage() { return _player.Faction == WowFaction.Horde ? LangOrcish : LangCommon; }

        // ------------------------------------------------------------------ receiving

        /// <summary>SMSG_MESSAGECHAT / SMSG_GM_MESSAGECHAT (ChatHandler::BuildChatPacket): every type, parsed by its type, never a fixed length.</summary>
        void OnMessageChat(WowPacket p, bool gm)
        {
            int type = p.U8();
            int lang = p.I32();
            ulong sender = p.U64();
            p.U32();                                            // "some flags" (always 0)
            string senderName = null, channel = null;
            ulong receiver = 0;
            switch (type)
            {
                case ChatMsgMonsterSay:
                case ChatMsgMonsterParty:
                case ChatMsgMonsterYell:
                case ChatMsgMonsterWhisper:
                case ChatMsgMonsterEmote:
                case ChatMsgRaidBossEmote:
                case ChatMsgRaidBossWhisper:
                case ChatMsgBattlenet:
                    senderName = SizedString(p);
                    receiver = p.U64();
                    if (receiver != 0 && !IsPlayerGuid(receiver) && !IsPetGuid(receiver)) SizedString(p);   // receiver name
                    break;
                case ChatMsgWhisperForeign:
                    senderName = SizedString(p);
                    receiver = p.U64();
                    break;
                case ChatMsgBgNeutral:
                case ChatMsgBgAlliance:
                case ChatMsgBgHorde:
                    receiver = p.U64();
                    if (receiver != 0 && !IsPlayerGuid(receiver)) SizedString(p);
                    break;
                case ChatMsgAchievement:
                case ChatMsgGuildAchievement:
                    receiver = p.U64();
                    break;
                default:
                    if (gm) senderName = SizedString(p);
                    if (type == ChatMsgChannel) channel = p.CString();
                    receiver = p.U64();
                    break;
            }
            string raw = SizedString(p);
            int tag = p.Remaining > 0 ? p.U8() : 0;             // chatTag
            // iteration 11 (social): the achievement types end with the uint32 achievement id (BuildChatPacket, Chat.cpp; AchievementMgr.cpp:721 / :759)
            int achievement = (type == ChatMsgAchievement || type == ChatMsgGuildAchievement) && p.Remaining >= 4 ? p.I32() : 0;
            _chatReceived++;

            if (lang == LangAddon) { _chatAddon++; return; }   // addon traffic: never shown

            if (type == ChatMsgSystem)
            {
                string text = StripUiCodes(raw);
                if (text.Length == 0) return;
                _systemMessages++;
                WowLog.Info("Online: system message \"" + text + "\"");
                _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text });   // as since iteration 7 (the GM buttons' answers)
                EmitChat(0, ChatKind.System, text, 0, 0);
                return;
            }

            ChatKind kind;
            if (!KindOf(type, out kind))
            {
                _chatIgnored++;
                if (_chatTypesLogged.Add(type)) WowLog.Info("Online: chat type 0x" + type.ToString("X2") + " not shown (" + ChatTypeName(type) + ")");
                return;
            }

            if (!string.IsNullOrEmpty(senderName)) RememberName(sender, senderName);
            string body = StripUiCodes(raw);
            switch (type)
            {
                case ChatMsgEmote:
                    if (body.Length == 0) body = ChatEmoteUnknown;       // the other faction's emote (Player::TextEmote, opposite team)
                    break;
                case ChatMsgIgnored:
                    body = string.Format(ChatIgnored, body);             // the text is the name of who ignores us (ChatHandler.cpp 800)
                    break;
                case ChatMsgRestricted:
                    body = ChatRestricted;
                    break;
                case ChatMsgChannel:
                    // ChatFrame.lua 2922: "[N. Name] " before the line - the numbered name of the joined channel (iteration 11, social: SocChannelDisplay)
                    if (!string.IsNullOrEmpty(channel)) body = "[" + SocChannelDisplay(channel) + "] " + body;
                    break;
                case ChatMsgRaidBossEmote:
                    // iteration 11 (social): Lua's format(text, name, name) like a monster emote ("%s" -> the boss, "%%" -> "%") - UiChat prints the kind as it is
                    body = ChatNameInto(body, senderName);
                    break;
                case ChatMsgAchievement:
                case ChatMsgGuildAchievement:
                    // iteration 11 (social, 3.6): broadcast_text 29245 '%s has earned the achievement $a!' - the session puts the [title] in for $a,
                    // UiChat the clickable [Name] for %s (ChatFrame.lua:2762-2765)
                    body = body.Replace("$a", "[" + ChatAchievementName(achievement) + "]");
                    break;
            }
            if (body.Length == 0) return;
            if (kind != ChatKind.System && kind != ChatKind.Achievement && kind != ChatKind.GuildAchievement) body = LanguageHeader(lang, body);
            if (type == ChatMsgRaidWarning) _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = body });   // WoW's RaidWarningFrame (ours: the yellow line)

            // Who is shown as the speaker: WHISPER_INFORM's sender is the one we whispered to (Player::Whisper). System-type lines
            // (ignored / restricted / battleground) carry no speaker.
            ulong speaker = kind == ChatKind.System ? 0 : sender;
            EmitChat(speaker, kind, body, tag, receiver);
        }

        /// <summary>ChatMsg -> ChatKind (the contract's kinds; iteration 11, social: every group / officer / AFK / DND / achievement kind
        /// of its own). False = not shown (channel join / leave types of SMSG_MESSAGECHAT, loot / skill / combat ...).</summary>
        static bool KindOf(int type, out ChatKind kind)
        {
            switch (type)
            {
                case ChatMsgSay: kind = ChatKind.Say; return true;
                case ChatMsgYell: kind = ChatKind.Yell; return true;
                case ChatMsgEmote:
                case ChatMsgTextEmote: kind = ChatKind.Emote; return true;     // AC sends text emotes as SMSG_TEXT_EMOTE; a 0x0B line is a scripted one
                case ChatMsgParty: kind = ChatKind.Party; return true;
                case ChatMsgPartyLeader: kind = ChatKind.PartyLeader; return true;
                case ChatMsgRaid: kind = ChatKind.Raid; return true;
                case ChatMsgRaidLeader: kind = ChatKind.RaidLeader; return true;
                case ChatMsgRaidWarning: kind = ChatKind.RaidWarning; return true;
                case ChatMsgBattleground: kind = ChatKind.Battleground; return true;
                case ChatMsgBattlegroundLeader: kind = ChatKind.BattlegroundLeader; return true;
                case ChatMsgGuild: kind = ChatKind.Guild; return true;
                case ChatMsgOfficer: kind = ChatKind.Officer; return true;
                case ChatMsgAfk: kind = ChatKind.Afk; return true;              // a whisper's AFK / DND auto reply (CHAT_AFK_GET / CHAT_DND_GET)
                case ChatMsgDnd: kind = ChatKind.Dnd; return true;
                case ChatMsgAchievement: kind = ChatKind.Achievement; return true;
                case ChatMsgGuildAchievement: kind = ChatKind.GuildAchievement; return true;
                case ChatMsgWhisper:
                case ChatMsgWhisperForeign:
                case ChatMsgMonsterWhisper:
                case ChatMsgRaidBossWhisper: kind = ChatKind.Whisper; return true;
                case ChatMsgWhisperInform: kind = ChatKind.WhisperSent; return true;
                case ChatMsgChannel: kind = ChatKind.Channel; return true;
                case ChatMsgMonsterSay:
                case ChatMsgMonsterParty: kind = ChatKind.MonsterSay; return true;
                case ChatMsgMonsterYell: kind = ChatKind.MonsterYell; return true;
                case ChatMsgMonsterEmote: kind = ChatKind.MonsterEmote; return true;
                case ChatMsgRaidBossEmote: kind = ChatKind.RaidBossEmote; return true;   // iteration 11 (social): its own kind (the session puts the name in)
                case ChatMsgIgnored:
                case ChatMsgRestricted:
                case ChatMsgBgNeutral:
                case ChatMsgBgAlliance:
                case ChatMsgBgHorde: kind = ChatKind.System; return true;
                default: kind = ChatKind.System; return false;
            }
        }

        static string ChatTypeName(int type)
        {
            switch (type)
            {
                case ChatMsgAfk: return "CHAT_MSG_AFK";
                case ChatMsgDnd: return "CHAT_MSG_DND";
                case ChatMsgFiltered: return "CHAT_MSG_FILTERED";
                case ChatMsgAchievement: return "CHAT_MSG_ACHIEVEMENT";
                case ChatMsgGuildAchievement: return "CHAT_MSG_GUILD_ACHIEVEMENT";
                case ChatMsgBattlenet: return "CHAT_MSG_BATTLENET";
                default: return type >= ChatMsgChannelJoin && type <= ChatMsgChannelNoticeUser ? "a channel notice" : "loot / skill / combat / other";
            }
        }

        /// <summary>
        /// WoW's language header (ChatFrame.lua 2901-2906: `if arg3 ~= "" and arg3 ~= "Universal" and arg3 ~= self.defaultLanguage then
        /// "[" .. arg3 .. "] "`): a line in a language other than universal and the character's default language (Common / Orcish) reads
        /// "[Darnassian] ..." with the Languages.dbc name. A language the character has no skill for (ObjectMgr.cpp 231-252 lang_description
        /// -> SKILL_LANG_*, SharedDefines.h 3115-3221, from the character's PLAYER_SKILL_INFO) reads CHAT_SAY_UNKNOWN - ours: the real client
        /// scrambles the words with LanguageWords.dbc (not rebuilt here).
        /// </summary>
        string LanguageHeader(int lang, string body)
        {
            if (lang == LangUniversal || lang == LangAddon || lang == FactionLanguage()) return body;
            string name = LanguageName(lang);
            string head = name != null ? "[" + name + "] " : "";
            return head + (KnowsLanguage(lang) ? body : ChatSayUnknown);
        }

        string LanguageName(int lang)
        {
            if (!_languagesTried)
            {
                _languagesTried = true;
                try { _languages = _files != null ? Dbc.Load(_files, "Languages") : null; }
                catch (Exception e) { _languages = null; WowLog.Warn("Online: Languages.dbc unreadable (" + e.Message + ") - no language names in chat"); }
                // Languages.dbc (3.3.5a, measured by the chat probe): 17 rows x 18 fields: ID, Name_lang (locstring, 17 fields)
                if (_languages != null && _languages.FieldCount != 18) { WowLog.Warn("Online: Languages.dbc has " + _languages.FieldCount + " fields (18 expected) - no language names"); _languages = null; }
            }
            if (_languages == null) return null;
            int row = _languages.FindRow(lang);
            if (row < 0) return null;
            string n = _languages.GetLocalizedString(row, 1);
            return string.IsNullOrEmpty(n) ? null : n;
        }

        /// <summary>Iteration 11 (social): an achievement's title for the ACHIEVEMENT / GUILD_ACHIEVEMENT lines - Achievement.dbc Title_lang
        /// (field 4) of the install, read through the shared DBC cache (the same table talents' AchievementDb reads); unknown -> "Achievement id".</summary>
        string ChatAchievementName(int id)
        {
            if (!_achievementsTried)
            {
                _achievementsTried = true;
                try { _achievements = _files != null ? Dbc.Load(_files, "Achievement") : null; }
                catch (Exception e) { _achievements = null; WowLog.Warn("Online: Achievement.dbc unreadable (" + e.Message + ") - achievement lines show the id"); }
                if (_achievements != null && _achievements.FieldCount != AchievementFieldCount)
                {
                    WowLog.Warn("Online: Achievement.dbc has " + _achievements.FieldCount + " fields (" + AchievementFieldCount + " expected) - achievement lines show the id");
                    _achievements = null;
                }
            }
            int row = _achievements != null && id > 0 ? _achievements.FindRow(id) : -1;
            string n = row >= 0 ? _achievements.GetLocalizedString(row, AchievementTitle) : null;
            if (!string.IsNullOrEmpty(n)) return n;
            SocLogOnce("achname", "achievement " + id + " has no title in Achievement.dbc - shown as \"Achievement " + id + "\" (logged once)");
            return "Achievement " + id;
        }

        /// <summary>Lua's format(text, name, name) of a creature / boss emote: "%s" -> the name, "%%" -> "%".</summary>
        static string ChatNameInto(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('%') < 0) return text ?? "";
            return text.Replace("%%", "\u0001").Replace("%s", name ?? "").Replace("\u0001", "%");
        }

        /// <summary>lang_description (ObjectMgr.cpp 231-252): the skill a language needs (0 = none needed).</summary>
        static int LanguageSkill(int lang)
        {
            switch (lang)
            {
                case 1: return 109;   // LANG_ORCISH -> SKILL_LANG_ORCISH
                case 2: return 113;   // LANG_DARNASSIAN
                case 3: return 115;   // LANG_TAURAHE
                case 6: return 111;   // LANG_DWARVISH -> SKILL_LANG_DWARVEN
                case 7: return 98;    // LANG_COMMON
                case 8: return 139;   // LANG_DEMONIC -> SKILL_LANG_DEMON_TONGUE
                case 9: return 140;   // LANG_TITAN
                case 10: return 137;  // LANG_THALASSIAN
                case 11: return 138;  // LANG_DRACONIC
                case 12: return 141;  // LANG_KALIMAG -> SKILL_LANG_OLD_TONGUE
                case 13: return 313;  // LANG_GNOMISH
                case 14: return 315;  // LANG_TROLL
                case 33: return 673;  // LANG_GUTTERSPEAK
                case 35: return 759;  // LANG_DRAENEI
                default: return 0;    // universal, zombie, gnomish / goblin binary: no skill
            }
        }

        /// <summary>The character has the language's skill (PLAYER_SKILL_INFO_1_1: 128 x (id | step &lt;&lt; 16, value | max &lt;&lt; 16, bonus)).
        /// True while the character's own object has not arrived (nothing is hidden on a guess).</summary>
        bool KnowsLanguage(int lang)
        {
            int skill = LanguageSkill(lang);
            if (skill == 0) return true;
            OnlineObject me;
            if (!_objects.TryGetValue(_player.Guid, out me) || me == null) return true;
            for (int i = 0; i < 128; i++)
            {
                int idx = UF.PLAYER_SKILL_INFO_1_1 + i * 3;
                if (me.U16(idx, 0) == skill) return me.U16(idx + 1, 0) > 0;
            }
            return false;
        }

        /// <summary>A ChatMessage event now, or after the speaker's name query (players the session has no name for).</summary>
        void EmitChat(ulong speaker, ChatKind kind, string text, int tag, ulong receiver)
        {
            var e = new GameEvent { Type = GameEventType.ChatMessage, SourceGuid = speaker, TargetGuid = receiver, Amount = (int)kind, Text = text, SpellId = tag, ItemEntry = 0 };
            ulong wait = 0;
            if (speaker != 0 && ChatSender(speaker) == null && IsPlayerGuid(speaker))
            {
                wait = speaker;
                if (_world != null && _nameAsked.Add(speaker))
                {
                    _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(speaker));   // NameQuery::Read: ObjectGuid (uint64)
                    _queriesSent++;
                    _chatNameQueries++;
                }
            }
            // Lines keep their order: once one waits, the ones after it wait behind it.
            if (wait != 0 || _chatPending.Count > 0)
            {
                _chatPending.Add(new PendingChat { E = e, WaitFor = wait, Until = _now + ChatNameWait });
                return;
            }
            ShowChat(e);
        }

        void TickChat()
        {
            while (_chatPending.Count > 0)
            {
                PendingChat c = _chatPending[0];
                bool known = c.WaitFor == 0 || ChatSender(c.WaitFor) != null;
                if (!known && _now < c.Until) return;
                if (!known)
                {
                    _chatNameTimeouts++;
                    if (_chatNameTimeouts <= 5) WowLog.Warn("Online: chat - no name for speaker " + c.WaitFor.ToString("X") + " after " + ChatNameWait + " s (shown as Unknown)");
                }
                _chatPending.RemoveAt(0);
                ShowChat(c.E);
            }
        }

        void ShowChat(GameEvent e)
        {
            _chatShown++;
            _events.Enqueue(e);
            // Privacy: the Player.log gets the kind, the speaker and the length - never what other players wrote (system lines are logged above).
            _chatLogged++;
            if ((ChatKind)e.Amount != ChatKind.System && (_chatLogged <= 20 || _chatLogged % 50 == 0))
                WowLog.Info("Online: chat " + (ChatKind)e.Amount + " from " + (ChatSender(e.SourceGuid) ?? (e.SourceGuid == 0 ? "-" : "Unknown")) + " (" + (e.Text ?? "").Length + " chars)"
                            + (_chatLogged == 20 ? " (further lines every 50th)" : ""));
        }

        /// <summary>A system line of our own session (SMSG_CHAT_PLAYER_NOT_FOUND ...): yellow message + a System chat line, like CHAT_MSG_SYSTEM.</summary>
        void ChatSystemLine(string text, string why)
        {
            WowLog.Info("Online: chat " + why + ": \"" + text + "\"");
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text });
            EmitChat(0, ChatKind.System, text, 0, 0);
        }

        void RememberName(ulong guid, string name)
        {
            if (guid == 0 || string.IsNullOrEmpty(name)) return;
            if (_chatNames.Count >= ChatNamesKept) _chatNames.Clear();
            _chatNames[guid] = name;
        }

        static bool IsPlayerGuid(ulong g) { return g != 0 && ((g >> 48) & 0xFFFF) == 0; }
        static bool IsPetGuid(ulong g) { return ((g >> 48) & 0xFFFF) == HighGuidPet; }

        /// <summary>uint32 length (with the NUL) + the string + NUL, as BuildChatPacket writes names and the message; odd lengths read what is there.</summary>
        static string SizedString(WowPacket p)
        {
            uint len = p.U32();
            if (len == 0) return "";
            if (len > (uint)p.Remaining)
            {
                WowLog.Warn("Online: " + p.Name + " string length " + len + " > " + p.Remaining + " bytes left - read what is there");
                return p.CString();
            }
            int n = (int)len;
            int end = p.Pos + n;
            int textLen = n;
            while (textLen > 0 && p.Data[p.Pos + textLen - 1] == 0) textLen--;
            string s = Encoding.UTF8.GetString(p.Data, p.Pos, textLen);
            p.Pos = end;
            return s;
        }

        /// <summary>SMSG_NOTIFICATION (ChatHandler::SendNotification): CString, one line.</summary>
        void OnNotification(WowPacket p)
        {
            string text = StripUiCodes(p.CString());
            if (text.Length == 0) return;
            WowLog.Info("Online: notification \"" + text + "\"");
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text });
        }

        /// <summary>WoW's UI escape codes out of a chat line: |cAARRGGBB, |r, |H...|h link heads, |h, "||" = '|'.</summary>
        public static string StripUiCodes(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('|') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '|' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == '|') { sb.Append('|'); i++; continue; }
                    if ((n == 'c' || n == 'C') && i + 9 < s.Length) { i += 9; continue; }
                    if (n == 'r' || n == 'R' || n == 'h') { i++; continue; }
                    if (n == 'H')
                    {
                        int end = s.IndexOf("|h", i + 2, StringComparison.Ordinal);
                        if (end >= 0) { i = end + 1; continue; }
                    }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ sending (stage S1)

        /// <summary>
        /// Iteration 11 (social): also Officer, Raid (RAID_LEADER when leading), RaidWarning, Battleground, Party as PARTY_LEADER when leading,
        /// and Channel (whisperTo = the channel's number "1".."10" or its name).
        /// CMSG_MESSAGECHAT for Say / Yell / Emote / Party / Guild / Whisper (whisperTo = the character name as typed; the server normalizes
        /// it). The line goes out in the faction language; '|' is escaped as "||", control characters become spaces, at most 255 UTF-8 bytes.
        /// A line starting with a single '.' is a GM command (RunGmCommand) and is refused here; ".." / "..." are said aloud like in WoW.
        /// Party without a group answers ERR_NOT_IN_GROUP like the client (the server would drop it silently, ChatHandler.cpp 454-460).
        /// </summary>
        public void SendChat(ChatKind kind, string text, string whisperTo)
        {
            string msg = CleanOutgoing(text);
            if (msg.Length == 0) return;
            if (msg[0] == '.' && (msg.Length < 2 || msg[1] != '.'))
            {
                WowLog.Warn("Online: chat line starting with '.' not sent as chat - GM commands go through RunGmCommand");
                return;
            }
            if (_world == null) { WowLog.Warn("Online: chat not sent - no realm connection"); return; }
            int type;
            bool inGroup = _player.Group.Count > 0 || _player.GroupLeaderGuid != 0;
            bool leader = inGroup && _player.GroupLeaderGuid == _player.Guid;
            bool raid = inGroup && (_socGroupType & ChatGroupTypeRaid) != 0, bg = inGroup && (_socGroupType & ChatGroupTypeBg) != 0;
            switch (kind)
            {
                case ChatKind.Say: type = ChatMsgSay; break;
                case ChatKind.Yell: type = ChatMsgYell; break;
                case ChatKind.Emote: type = ChatMsgEmote; break;
                case ChatKind.Guild:
                case ChatKind.Officer:
                    // iteration 11 (social): the client knows it is not in a guild (ERR_GUILD_PLAYER_NOT_IN_GUILD; the server would drop it silently)
                    if (_self != null && _self.U32(UF.PLAYER_GUILDID) == 0) { ChatSystemLine(ErrGuildNotInGuild, "guild chat without a guild"); return; }
                    type = kind == ChatKind.Officer ? ChatMsgOfficer : ChatMsgGuild;   // officer chat needs GR_RIGHT_OFFCHATSPEAK (Guild.cpp:2175-2186)
                    break;
                case ChatKind.Party:
                case ChatKind.PartyLeader:
                    if (!inGroup) { ChatSystemLine(ErrNotInGroup, "party chat without a group"); return; }
                    type = leader ? ChatMsgPartyLeader : ChatMsgParty;                 // PARTY_LEADER only from the leader (ChatHandler.cpp:451-472)
                    break;
                case ChatKind.Raid:
                case ChatKind.RaidLeader:
                    if (!raid) { Error(ErrNotInRaid); return; }                       // ERR_NOT_IN_RAID; the server would drop it (ChatHandler.cpp:500-516)
                    type = leader ? ChatMsgRaidLeader : ChatMsgRaid;                   // RAID_LEADER only from the raid's leader (:517-535)
                    break;
                case ChatKind.RaidWarning:
                    if (!raid) { Error(ErrNotInRaid); return; }
                    if (!leader && (_socGroupFlags & 0x01) == 0)                       // MEMBER_FLAG_ASSISTANT (Group.h:74)
                        SocLogOnce("rwnorights", "raid warning without leader / assistant rights - sent, the server drops it (ChatHandler.cpp:540-553; WoW has no text for it)");
                    type = ChatMsgRaidWarning;
                    break;
                case ChatKind.Battleground:
                case ChatKind.BattlegroundLeader:
                    type = bg && leader ? ChatMsgBattlegroundLeader : ChatMsgBattleground;   // the server checks the battleground group itself (:555-580)
                    break;
                case ChatKind.Channel:
                    {
                        ChannelView c = SocChannelRef(whisperTo);
                        if (c == null) { Error(string.Format(ChatNotMember, (whisperTo ?? "").Trim())); return; }   // CHAT_NOT_MEMBER_NOTICE (GlobalStrings.lua 1523)
                        whisperTo = c.Name;
                        type = ChatMsgChannel;
                        break;
                    }
                case ChatKind.Whisper:
                    type = ChatMsgWhisper;
                    whisperTo = (whisperTo ?? "").Trim();
                    if (whisperTo.Length == 0) { WowLog.Warn("Online: whisper without a name - nothing sent"); return; }
                    break;
                default:
                    WowLog.Warn("Online: chat kind " + kind + " cannot be sent");
                    return;
            }
            var w = new PacketWriter().U32((uint)type).U32((uint)FactionLanguage());
            if (type == ChatMsgWhisper || type == ChatMsgChannel) w.CString(whisperTo);   // WHISPER: CString to / CHANNEL: CString channel (ChatHandler.cpp:274-277), then the message
            w.CString(msg);
            _world.Send(Op.CMSG_MESSAGECHAT, w);
            _chatSent++;
            if (_chatSent <= 20 || _chatSent % 50 == 0)
                WowLog.Info("Online: chat sent " + kind + (type == ChatMsgWhisper ? " to " + whisperTo : "") + " (" + msg.Length + " chars, language " + FactionLanguage() + ")");
        }

        /// <summary>What the edit box of WoW sends: trimmed, control characters as spaces, '|' as "||" (Hyperlinks.cpp CheckAllLinks), cut at 255 bytes.</summary>
        public static string CleanOutgoing(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '|') sb.Append("||");
                else if (c < ' ' && c != '\t') sb.Append(' ');   // isNasty: every control character but tab drops the whole line
                else sb.Append(c);
            }
            string s = sb.ToString().Trim();
            // At most ChatMaxBytes UTF-8 bytes, cut at a character boundary (never half a surrogate pair, never half a "||").
            if (Encoding.UTF8.GetByteCount(s) <= ChatMaxBytes) return s;
            int bytes = 0, cut = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int len = char.IsHighSurrogate(s[i]) && i + 1 < s.Length ? 2 : 1;
                int b = Encoding.UTF8.GetByteCount(s.Substring(i, len));
                if (bytes + b > ChatMaxBytes) break;
                bytes += b;
                i += len - 1;
                cut = i + 1;
            }
            s = s.Substring(0, cut);
            // An odd run of '|' at the end is half an escape: drop one.
            int pipes = 0;
            for (int i = s.Length - 1; i >= 0 && s[i] == '|'; i--) pipes++;
            if (pipes % 2 == 1) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd();
        }

        /// <summary>The name of a speaker: the character itself, the server's name answers (players), the names chat packets carried
        /// (monsters, GMs), the group list, then the entity's own name. Null = not known yet.</summary>
        public string ChatSender(ulong guid)
        {
            if (guid == 0) return null;
            if (guid == _player.Guid && !string.IsNullOrEmpty(_player.Name)) return _player.Name;
            string n;
            if (_playerNames.TryGetValue(guid, out n) && !string.IsNullOrEmpty(n)) return n;
            if (_chatNames.TryGetValue(guid, out n) && !string.IsNullOrEmpty(n)) return n;
            List<GroupMemberView> group = _player.Group;
            if (group != null)
                for (int i = 0; i < group.Count; i++)
                    if (group[i] != null && group[i].Guid == guid && !string.IsNullOrEmpty(group[i].Name)) return group[i].Name;
            EntityState e = Find(guid);
            if (e != null && !string.IsNullOrEmpty(e.Name)) return e.Name;
            return null;
        }
    }
}
