using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using SomniumSpace.Worlds.SomniumWoW.Play.Social;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Stage S1 (owner chat, PLAN10 s2): WoW's chat window, fed by the session's ChatMessage events (IWowSession.SendChat to talk).
    /// - Desktop (online): ChatFrame1 where FrameXML puts it (BOTTOMLEFT (32, 95), 430 x 120, FloatingChatFrame.xml 871-881) with its
    ///   lines in the chat-type colours, fading after displayDuration 120 s (ChatFrame.xml 4); Enter (or "/") opens the edit box below it
    ///   (ChatFrameEditBoxTemplate: UI-ChatInputBorder art, "Say: " header, 255 letters, 32 history lines - Up / Down), Enter sends, Escape
    ///   cancels; WoW's slash commands /s /y /e /me /p /g /w /t /r /i and ".gm commands"; the mouse wheel over the frame scrolls.
    ///   Offline the desktop frame stays hidden (no chat offline) and Enter is left to the Somnium client.
    /// - VR: a world window opened from the palm menu (TogglePalmWindow) with the last lines, the mode buttons (Say / Party / Yell / Emote /
    ///   Reply) and a "Type" row that opens the headset keyboard (Game/Front/WowKeyboard, used as it is); Enter on the keyboard sends.
    /// Chat bubbles over the speakers are Game/Play/Entities/WowChatBubbles (the same formatting helpers, BubbleText).
    /// Ours (Dreamport additions, named in the README): the VR window and its buttons, the "Type" row, FadeSeconds, the holder below.
    /// The Somnium client's own desktop quick chat also opens on Enter (CS Player/.../QuickChatControl.cs: only while
    /// EventSystem.currentSelectedGameObject is null, CanOpenInput). Online, while the WoW edit box is closed, a plain holder object is kept
    /// selected (HoldSelectionOnline), so Enter always opens the WoW chat and never both; offline and in VR nothing is held.
    /// Every text is shown with TMP rich text OFF inside the line (a player's "&lt;color&gt;" stays text): each line is wrapped in &lt;noparse&gt;.
    /// Iteration 11 (owner socialui, proposal-social.md 6.5.4): every chat kind of the contract (Officer, Raid, Raid Leader, Raid Warning,
    /// Battleground (+ Leader), Party Leader, AFK / DND replies, achievements, channel notices, raid boss emotes, text emotes) with WoW's
    /// prefix and colour; the edit box's Officer / Raid / Raid Warning / Battleground / channel types and WoW's social slash commands
    /// (/join /leave /chatlist /afk /dnd /who /friend /ignore /g* and every emote command of the install); clickable player names (a
    /// transparent button over each [Name] link: left = whisper, Shift = the name into an open add-name popup / the edit box / a Who
    /// search, right = WoW's FRIEND menu; VR: press = whisper, long press = the menu); ChatFrame1 rises with the Bottom Left bar
    /// (vrcast's UiBars.ChatLift). Offline the desktop frame shows the local lines (text emotes, AFK / DND) and "/" opens the edit box
    /// for slash commands (Enter stays the Somnium client's). The VR window gets Guild / Officer / Raid / Channel modes and 8 quick emotes.
    /// </summary>
    public sealed class UiChat : MonoBehaviour
    {
        // ---- WoW's (sources in the comments)
        public const int MaxLines = 128;                 // ChatFrameTemplate maxLines="128" (ChatFrame.xml:4)
        public const float DisplayDuration = 120f;       // ChatFrameTemplate displayDuration="120.0" (ChatFrame.xml:4)
        public const int HistoryLines = 32;              // ChatFrameEditBoxTemplate historyLines="32" (ChatFrame.xml:21)
        public const int Letters = 255;                  // ChatFrameEditBoxTemplate letters="255" (ChatFrame.xml:21)
        public const float TellAlertTime = 300f;         // CHAT_TELL_ALERT_TIME (ChatFrame.lua:4): "TellMessage" at most every 300 s
        public const float BackgroundAlpha = 0.25f;      // DEFAULT_CHATFRAME_ALPHA (FloatingChatFrame.lua:21), DEFAULT_CHATFRAME_COLOR black (22)
        public const float FrameX = 32f, FrameY = 95f, FrameW = 430f, FrameH = 120f;   // ChatFrame1 (FloatingChatFrame.xml:871-881)
        public const float FontSize = 14f;               // ChatFontNormal's default height 14 (ChatFrame.lua:845-848)
        public const float ButtonFrameW = 29f;           // ChatFrame1ButtonFrame width 29 (FloatingChatFrame.xml:573)

        // ---- ours
        public const float FadeSeconds = 3f;             // ours: a line fades out over 3 s after DisplayDuration
        public const bool HoldSelectionOnline = true;    // ours: see the class summary (the Somnium quick chat never opens over the WoW chat online)
        public const float VrW = 500f, VrH = 384f;       // ours: the VR window, canvas units (iteration 11: 330 -> 384 for the new mode rows)
        public const float VrMetresPerUnit = 0.0009f;    // ours: 0.45 x 0.35 m
        public const float VrFontSize = 13f;             // ours
        const int ComposeLines = 40;                     // ours: lines put into the text at most (the frame shows ~8, the VR window ~13)
        const int LinkPool = 40;                         // ours: clickable name buttons per chat surface (proposal-social.md 6.5.4)
        const float ShiftReopenSeconds = 2f;             // ours: a Shift-click on a name right after the edit box lost the focus to it reopens it
        const float GuildInfoWait = 5f;                  // ours: /ginfo prints the guild's info line when the answer comes within 5 s
        /// <summary>The VR quick emotes (ours, proposal-social.md 6.5.4): EmotesText.dbc ids WAVE 101, BOW 17, CHEER 21, DANCE 34,
        /// LAUGH 60, POINT 72, SALUTE 78, THANK 97 (measured, 4.5); the labels are the install's commands.</summary>
        static readonly int[] QuickEmotes = { 101, 17, 21, 34, 60, 72, 78, 97 };
        static readonly string[] QuickEmoteTokens = { "WAVE", "BOW", "CHEER", "DANCE", "LAUGH", "POINT", "SALUTE", "THANK" };

        // ---- art (Interface\ChatFrame, measured in the install by the chat probe's Find)
        const string BgTex = @"Interface\ChatFrame\ChatFrameBackground.blp";
        const string EditLeft = @"Interface\ChatFrame\UI-ChatInputBorder-Left2.blp", EditMid = @"Interface\ChatFrame\UI-ChatInputBorder-Mid2.blp", EditRight = @"Interface\ChatFrame\UI-ChatInputBorder-Right2.blp";
        const string FocusLeft = @"Interface\ChatFrame\UI-ChatInputBorderFocus-Left.blp", FocusMid = @"Interface\ChatFrame\UI-ChatInputBorderFocus-Mid.blp", FocusRight = @"Interface\ChatFrame\UI-ChatInputBorderFocus-Right.blp";
        const string ScrollUpUp = @"Interface\ChatFrame\UI-ChatIcon-ScrollUp-Up.blp", ScrollUpDown = @"Interface\ChatFrame\UI-ChatIcon-ScrollUp-Down.blp", ScrollUpOff = @"Interface\ChatFrame\UI-ChatIcon-ScrollUp-Disabled.blp";
        const string ScrollDownUp = @"Interface\ChatFrame\UI-ChatIcon-ScrollDown-Up.blp", ScrollDownDown = @"Interface\ChatFrame\UI-ChatIcon-ScrollDown-Down.blp", ScrollDownOff = @"Interface\ChatFrame\UI-ChatIcon-ScrollDown-Disabled.blp";
        const string ScrollEndUp = @"Interface\ChatFrame\UI-ChatIcon-ScrollEnd-Up.blp", ScrollEndDown = @"Interface\ChatFrame\UI-ChatIcon-ScrollEnd-Down.blp", ScrollEndOff = @"Interface\ChatFrame\UI-ChatIcon-ScrollEnd-Disabled.blp";
        /// <summary>WoW's chat button art (ChatFrameMenuButton, FloatingChatFrame.xml 905-926: 32 x 32, highlight UI-Common-MouseHilight ADD)
        /// - for the palm menu's Chat button (owner hud / the integrator).</summary>
        public const string ChatButtonUp = @"Interface\ChatFrame\UI-ChatIcon-Chat-Up.blp", ChatButtonDown = @"Interface\ChatFrame\UI-ChatIcon-Chat-Down.blp",
            ChatButtonHilite = @"Interface\Buttons\UI-Common-MouseHilight.blp";

        // ---- WoW's texts (Interface\FrameXML\GlobalStrings.lua, line numbers of the 3.3.5a file)
        public const string Unknown = "Unknown";                                        // UNKNOWN (7899)
        const string HelpTextSimple = "Type '/help' for a listing of a few commands.";  // HELP_TEXT_SIMPLE (4155)
        const string ErrNoReplyTarget = "You have nobody to reply to yet.";             // ERR_NO_REPLY_TARGET (3328)
        const string SayUnknown = "says something unintelligible.";                    // CHAT_SAY_UNKNOWN (1572) - OnlineSession.LanguageHeader's text

        // ---- the chat colours: the COLORS block of a fresh account's WTF\...\chat-cache.txt in the install (the client's defaults)
        static readonly Color CSay = C(255, 255, 255), CYell = C(255, 64, 64), CEmote = C(255, 128, 64), CParty = C(170, 170, 255),
            CGuild = C(64, 255, 64), CWhisper = C(255, 128, 255), CSystem = C(255, 255, 0), CChannel = C(255, 192, 192),
            CMonsterSay = C(255, 255, 159), CMonsterYell = C(255, 64, 64), CMonsterEmote = C(255, 128, 64), CMonsterWhisper = C(255, 181, 235);
        // iteration 11 (proposal-social.md 4.3, the same chat-cache.txt COLORS block)
        static readonly Color COfficer = C(64, 192, 64), CRaid = C(255, 127, 0), CRaidLeader = C(255, 72, 9), CRaidWarning = C(255, 72, 0),
            CBattleground = C(255, 127, 0), CBattlegroundLeader = C(255, 219, 183), CPartyLeader = C(118, 200, 255), CAfk = C(255, 128, 255),
            CAchievement = C(255, 255, 0), CGuildAchievement = C(64, 255, 64), CChannelNotice = C(192, 192, 192), CTextEmote = C(255, 128, 64),
            CRaidBossEmote = C(255, 221, 0);

        static Color C(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f, 1f); }

        public static UiChat Instance { get; private set; }

        /// <summary>Lines that arrived (not system, not the player's own) while the VR window was closed - for the palm button's badge.</summary>
        public static int Unread { get; private set; }

        /// <summary>Desktop: the WoW edit box has the keyboard (other modules' keys are off anyway: the TMP field is selected).</summary>
        public static bool Typing { get { UiChat c = Instance; return c != null && c._typing; } }

        /// <summary>VR: the headset keyboard is open for the chat (the target ray should not select through it).</summary>
        public static bool VrTyping { get { UiChat c = Instance; return c != null && c._keyboard != null && c._keyboard.IsOpen; } }

        /// <summary>VR: the chat window is shown.</summary>
        public static bool VrOpen { get { UiChat c = Instance; return c != null && c._vr != null && c._vr.Shown; } }

        /// <summary>The palm menu's Chat button (VR): opens / closes the chat window above the palm. On desktop: opens the edit box.</summary>
        public static void TogglePalmWindow(Transform palm)
        {
            UiChat c = Instance;
            if (c == null) { WowLog.Warn("UI: chat - TogglePalmWindow before the chat component exists (WowGame adds it)"); return; }
            c.ToggleVr(palm);
        }

        // ------------------------------------------------------------------ the log

        sealed class Line
        {
            public ChatKind Kind;
            public string Hex;        // RRGGBB
            public string Escaped;    // the formatted line, safe inside <noparse>
            public float Time;
            // iteration 11 (socialui): the clickable [Name] of a player speaker - Escaped = Pre + "[" + Name + "]" + Post
            public int Seq;           // the TMP link id
            public string Name, Pre, Post;
            public ulong Guid;
        }

        static readonly List<Line> Lines = new List<Line>();
        static int _version;
        static int _added;
        static int _seq;

        /// <summary>Adds a line of our own UI (help texts, "nobody to reply to") as a system line.</summary>
        public static void AddLocal(string text) { AddLine(ChatKind.System, text, CSystem); }

        /// <summary>Lead (2026-09-27): UnitPopup "WHISPER" -> ChatFrame_SendTell(name) (UnitPopup.lua:1193): the edit box opens as "Tell name:"
        /// on the desktop; in VR the chat's mode becomes the tell and the headset keyboard opens.</summary>
        public static void StartWhisper(string name)
        {
            UiChat c = Instance;
            if (c == null || string.IsNullOrEmpty(name)) return;
            c._mode = ChatKind.Whisper;
            c._tellTarget = name;
            if (WowPlayer.XrPresent) { c.SetHeader(); c.OpenKeyboard(); }
            else c.OpenEdit("");
            WowLog.Info("UI: chat - whisper to " + name + " opened (unit menu).");
        }

        static void AddLine(ChatKind kind, string text, Color color) { AddLine(kind, text, color, null, 0, -1); }

        /// <summary>A line; linkAt >= 0 = "[name]" stands there in text (a player speaker: the clickable name).</summary>
        static void AddLine(ChatKind kind, string text, Color color, string name, ulong guid, int linkAt)
        {
            if (string.IsNullOrEmpty(text)) return;
            var l = new Line { Kind = kind, Hex = ColorUtility.ToHtmlStringRGB(color), Escaped = Escape(text), Time = Time.unscaledTime, Seq = ++_seq };
            string link = string.IsNullOrEmpty(name) ? null : "[" + name + "]";
            if (link != null && linkAt >= 0 && linkAt + link.Length <= text.Length && string.CompareOrdinal(text, linkAt, link, 0, link.Length) == 0
                && name.IndexOf('<') < 0 && name.IndexOf('"') < 0)
            {
                l.Name = name;
                l.Guid = guid;
                l.Pre = Escape(text.Substring(0, linkAt));
                l.Post = Escape(text.Substring(linkAt + link.Length));
            }
            Lines.Add(l);
            if (Lines.Count > MaxLines) Lines.RemoveAt(0);
            _version++;
            _added++;
        }

        static Line BySeq(int seq)
        {
            for (int i = Lines.Count - 1; i >= 0; i--) if (Lines[i].Seq == seq) return Lines[i];
            return null;
        }

        /// <summary>The colour of a line (chat-cache.txt COLORS; monster whispers have their own, player whispers WHISPER).</summary>
        public static Color ColorOf(ChatKind kind, ulong speaker)
        {
            switch (kind)
            {
                case ChatKind.Officer: return COfficer;
                case ChatKind.Raid: return CRaid;
                case ChatKind.RaidLeader: return CRaidLeader;
                case ChatKind.RaidWarning: return CRaidWarning;
                case ChatKind.Battleground: return CBattleground;
                case ChatKind.BattlegroundLeader: return CBattlegroundLeader;
                case ChatKind.PartyLeader: return CPartyLeader;
                case ChatKind.Afk:
                case ChatKind.Dnd: return CAfk;                          // AFK / DND 255 128 255
                case ChatKind.Achievement: return CAchievement;
                case ChatKind.GuildAchievement: return CGuildAchievement;
                case ChatKind.ChannelNotice: return CChannelNotice;
                case ChatKind.TextEmote: return CTextEmote;
                case ChatKind.RaidBossEmote: return CRaidBossEmote;
                case ChatKind.Say: return CSay;
                case ChatKind.Yell: return CYell;
                case ChatKind.Emote: return CEmote;
                case ChatKind.Party: return CParty;
                case ChatKind.Guild: return CGuild;
                case ChatKind.Whisper: return IsPlayer(speaker) ? CWhisper : CMonsterWhisper;
                case ChatKind.WhisperSent: return CWhisper;              // WHISPER_INFORM 255 128 255
                case ChatKind.Channel: return CChannel;
                case ChatKind.MonsterSay: return CMonsterSay;
                case ChatKind.MonsterYell: return CMonsterYell;
                case ChatKind.MonsterEmote: return CMonsterEmote;
                default: return CSystem;
            }
        }

        static bool IsPlayer(ulong guid) { return guid != 0 && ((guid >> 48) & 0xFFFF) == 0; }   // ObjectGuid.h 64: HighGuid::Player = 0x0000

        /// <summary>
        /// A line as WoW's ChatFrame_MessageEventHandler prints it (ChatFrame.lua 2849-2915): players as a [Name] link, creatures by name
        /// without brackets; CHAT_SAY_GET "%s says: " (GlobalStrings 1570), CHAT_YELL_GET (1607), CHAT_EMOTE_GET "%s " (1428),
        /// CHAT_PARTY_GET "[Party] %s: " (1536), CHAT_GUILD_GET (1436), CHAT_WHISPER_GET "%s whispers: " (1600), CHAT_WHISPER_INFORM_GET
        /// "To %s: " (1601), CHAT_MONSTER_SAY_GET (1478), CHAT_MONSTER_YELL_GET (1480), CHAT_MONSTER_WHISPER_GET (1479), CHAT_MONSTER_EMOTE_GET
        /// "" + format(text, name, name) (1476), CHAT_CHANNEL_GET "%s: " after "[channel] " (1410, 2922); the chat tag flag before the name
        /// (CHAT_FLAG_AFK "&lt;Away&gt;", CHAT_FLAG_DND "&lt;Busy&gt;", CHAT_FLAG_GM "&lt;GM&gt;" 1432-1434 - the real client draws the
        /// Blizzard icon for GMs).
        /// </summary>
        public static string Format(ChatKind kind, ulong speaker, string name, string text, int tag)
        {
            int at;
            return FormatAt(kind, speaker, name, text, tag, out at);
        }

        /// <summary>Format, and where the player's "[Name]" stands in it (-1 = no clickable name).</summary>
        static string FormatAt(ChatKind kind, ulong speaker, string name, string text, int tag, out int linkAt)
        {
            linkAt = -1;
            string n = string.IsNullOrEmpty(name) ? Unknown : name;
            bool player = IsPlayer(speaker);
            string link = player ? "[" + n + "]" : n;
            string flag = Flag(tag);
            text = text ?? "";
            string head;
            // A language the character does not know: the session sends "[Orcish] " + CHAT_SAY_UNKNOWN; it reads "Name says something unintelligible."
            if (text.EndsWith(SayUnknown, StringComparison.Ordinal) && Spoken(kind))
            {
                head = GroupHead(kind) + flag;
                if (player) linkAt = head.Length;
                return head + link + " " + SayUnknown;
            }
            switch (kind)
            {
                case ChatKind.Say: if (player) linkAt = flag.Length; return flag + link + " says: " + text;
                case ChatKind.Yell: if (player) linkAt = flag.Length; return flag + link + " yells: " + text;
                case ChatKind.Emote: return flag + n + " " + text;
                case ChatKind.Party:
                case ChatKind.Guild:
                case ChatKind.Officer:
                case ChatKind.Raid:
                case ChatKind.RaidLeader:
                case ChatKind.RaidWarning:
                case ChatKind.Battleground:
                case ChatKind.BattlegroundLeader:
                case ChatKind.PartyLeader:
                    head = GroupHead(kind) + flag;
                    if (player) linkAt = head.Length;
                    return head + link + ": " + text;
                case ChatKind.Afk:   // CHAT_AFK_GET "%s is Away: " (GlobalStrings.lua:1389) - the auto reply of a whisper
                case ChatKind.Dnd:   // CHAT_DND_GET "%s does not wish to be disturbed: " (:1427)
                    {
                        string f = kind == ChatKind.Afk ? AfkGet : DndGet;
                        int s = f.IndexOf("%s", StringComparison.Ordinal);
                        if (s < 0) return f + text;
                        head = f.Substring(0, s) + flag;
                        if (player) linkAt = head.Length;
                        return head + link + f.Substring(s + 2) + text;
                    }
                case ChatKind.Achievement:
                case ChatKind.GuildAchievement:
                    {
                        // the session's "%s has earned the achievement [Title]!" (ChatFrame.lua:2762-2765: format(message, the player link))
                        int s = text.IndexOf("%s", StringComparison.Ordinal);
                        if (s < 0) return text;
                        if (player) linkAt = s;
                        return text.Substring(0, s) + link + text.Substring(s + 2);
                    }
                case ChatKind.Whisper: if (player) linkAt = flag.Length; return flag + link + " whispers: " + text;
                case ChatKind.WhisperSent: if (player) linkAt = 3; return "To " + link + ": " + text;
                case ChatKind.Channel:
                    {
                        int end = text.StartsWith("[", StringComparison.Ordinal) ? text.IndexOf("] ", StringComparison.Ordinal) : -1;
                        head = (end > 0 ? text.Substring(0, end + 2) : "") + flag;
                        if (player) linkAt = head.Length;
                        return head + link + ": " + (end > 0 ? text.Substring(end + 2) : text);
                    }
                case ChatKind.MonsterSay: return n + " says: " + text;
                case ChatKind.MonsterYell: return n + " yells: " + text;
                case ChatKind.MonsterEmote: return MonsterEmote(text, n);
                default: return text;   // System, TextEmote, ChannelNotice, RaidBossEmote: the session's complete line
            }
        }

        static bool Spoken(ChatKind k)
        {
            switch (k)
            {
                case ChatKind.Say: case ChatKind.Yell: case ChatKind.Party: case ChatKind.Guild: case ChatKind.Whisper: case ChatKind.Channel:
                case ChatKind.Officer: case ChatKind.Raid: case ChatKind.RaidLeader: case ChatKind.RaidWarning: case ChatKind.Battleground:
                case ChatKind.BattlegroundLeader: case ChatKind.PartyLeader: case ChatKind.MonsterSay: case ChatKind.MonsterYell:
                    return true;
                default: return false;
            }
        }

        static string AfkGet { get { return GsFormat("CHAT_AFK_GET", "%s is Away: "); } }
        static string DndGet { get { return GsFormat("CHAT_DND_GET", "%s does not wish to be disturbed: "); } }

        static readonly Dictionary<ChatKind, string> Heads = new Dictionary<ChatKind, string>();

        /// <summary>The group prefix before the name: CHAT_PARTY_GET "[Party] %s: " (GlobalStrings.lua:1536), CHAT_GUILD_GET (1436),
        /// CHAT_OFFICER_GET (1529), CHAT_RAID_GET (1564), CHAT_RAID_LEADER_GET (1565), CHAT_RAID_WARNING_GET (1567), CHAT_BATTLEGROUND_GET
        /// (1398), CHAT_BATTLEGROUND_LEADER_GET (1399), CHAT_PARTY_LEADER_GET (1538) - the install's text up to "%s" (its |Hchannel| link
        /// heads left out); "" for the other kinds.</summary>
        static string GroupHead(ChatKind k)
        {
            string key, fb;
            switch (k)
            {
                case ChatKind.Party: key = "CHAT_PARTY_GET"; fb = "[Party] %s: "; break;
                case ChatKind.Guild: key = "CHAT_GUILD_GET"; fb = "[Guild] %s: "; break;
                case ChatKind.Officer: key = "CHAT_OFFICER_GET"; fb = "[Officer] %s: "; break;
                case ChatKind.Raid: key = "CHAT_RAID_GET"; fb = "[Raid] %s: "; break;
                case ChatKind.RaidLeader: key = "CHAT_RAID_LEADER_GET"; fb = "[Raid Leader] %s: "; break;
                case ChatKind.RaidWarning: key = "CHAT_RAID_WARNING_GET"; fb = "[Raid Warning] %s: "; break;
                case ChatKind.Battleground: key = "CHAT_BATTLEGROUND_GET"; fb = "[Battleground] %s: "; break;
                case ChatKind.BattlegroundLeader: key = "CHAT_BATTLEGROUND_LEADER_GET"; fb = "[Battleground Leader] %s: "; break;
                case ChatKind.PartyLeader: key = "CHAT_PARTY_LEADER_GET"; fb = "[Party Leader] %s: "; break;
                default: return "";
            }
            string h;
            if (Heads.TryGetValue(k, out h)) return h;
            string f = GsFormat(key, fb);
            int s = f.IndexOf("%s", StringComparison.Ordinal);
            h = s >= 0 ? f.Substring(0, s) : fb.Substring(0, fb.IndexOf("%s", StringComparison.Ordinal));
            if (UiArt.Files != null) Heads[k] = h;   // the install's strings are loaded with the files
            return h;
        }

        /// <summary>A GlobalStrings format with its "%s" kept (SocialUi's Lua reader: escapes resolved, |H link heads left out).</summary>
        static string GsFormat(string key, string fallback)
        {
            string f = SocialUi.F(key, fallback, "%s");
            return string.IsNullOrEmpty(f) || f.IndexOf("%s", StringComparison.Ordinal) < 0 ? fallback : f;
        }

        /// <summary>What a chat bubble over the speaker reads: the words (emotes with the name, as the chat line).</summary>
        public static string BubbleText(ChatKind kind, string name, string text)
        {
            string n = string.IsNullOrEmpty(name) ? Unknown : name;
            switch (kind)
            {
                case ChatKind.Emote: return n + " " + (text ?? "");
                case ChatKind.MonsterEmote: return MonsterEmote(text, n);
                default: return text ?? "";
            }
        }

        /// <summary>Lua's format(text, name, name) on a creature emote: "%s" -> the name, "%%" -> "%".</summary>
        static string MonsterEmote(string text, string name)
        {
            if (string.IsNullOrEmpty(text)) return name;
            if (text.IndexOf('%') < 0) return text;
            var sb = new StringBuilder(text.Length + name.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '%' && i + 1 < text.Length)
                {
                    char d = text[i + 1];
                    if (d == 's') { sb.Append(name); i++; continue; }
                    if (d == '%') { sb.Append('%'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string Flag(int tag)
        {
            if ((tag & OnlineSession.ChatTagGm) != 0) return "<GM> ";
            if ((tag & OnlineSession.ChatTagDnd) != 0) return "<Busy>";
            if ((tag & OnlineSession.ChatTagAfk) != 0) return "<Away>";
            return "";
        }

        /// <summary>Text safe inside TMP's &lt;noparse&gt; (a typed "&lt;/noparse&gt;" is taken out).</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = RemoveIgnoreCase(s, "</noparse>");
            return RemoveIgnoreCase(s, "<noparse>");
        }

        static string RemoveIgnoreCase(string s, string what)
        {
            int i;
            while ((i = s.IndexOf(what, StringComparison.OrdinalIgnoreCase)) >= 0) s = s.Remove(i, what.Length);
            return s;
        }

        // ------------------------------------------------------------------ state

        private UiSurface _desk;
        private RectTransform _deskFrame, _deskEditRoot, _deskArea;
        private RawImage _deskBg;
        private RawImage[] _deskFocusArt;
        private CanvasGroup _deskEditGroup, _deskButtonsGroup;
        private TextMeshProUGUI _deskText, _deskHeader;
        private TMP_InputField _deskField;
        private CanvasScaler _deskScaler;
        private float _deskScale = 1f;
        private bool _typing, _submitted, _caretToEnd;
        private int _closedFrame = -10, _releaseAt = -1;
        private GameObject _holder;
        private bool _holding, _loggedHold;
        private int _deskOffset, _deskComposed = -1, _deskComposedOffset = -1;
        private bool _deskComposedAll, _deskFading;

        private UiSurface _vr;
        private TextMeshProUGUI _vrText, _vrHeader, _vrPreview;
        private TMP_InputField _vrField;
        private UiButton[] _vrModes;
        private WowKeyboard _keyboard;
        private bool _vrWanted, _vrPlaced;
        private Transform _vrFrom;
        private int _vrOffset, _vrComposed = -1, _vrComposedOffset = -1, _vrPlacements;

        // the edit box's mode (sticky types: ChatTypeInfo sticky = 1 for SAY, PARTY, GUILD, WHISPER, CHANNEL; 0 for YELL, EMOTE -
        // ChatFrame.lua 34-51)
        private ChatKind _mode = ChatKind.Say, _sticky = ChatKind.Say;
        private string _tellTarget, _stickyTell, _lastTellFrom;
        private readonly List<string> _history = new List<string>();
        private int _historyAt = -1;
        private float _tellTimer = float.NegativeInfinity;

        private IWowSession _session;
        private bool _subscribed, _loggedDesk, _loggedVr;

        // iteration 11 (socialui): the name buttons, the CHANNEL type, the Shift-click reopen, /ginfo, ChatLift, the VR rows
        private sealed class LinkSet
        {
            public RectTransform Clip;
            public TextMeshProUGUI Text;
            public UiButton[] Buttons;
            public int[] Seq;
            public bool Vr;
            public float ClipX, ClipY;   // the clip's top-left in the menu host (VR: the window content)
        }
        private LinkSet _deskLinks, _vrLinks;
        private int _channelTarget, _stickyChannel;               // the CHANNEL type's channel number (1..10)
        private string _lostText, _lostTell;
        private ChatKind _lostMode;
        private int _lostChannel;
        private float _lostAt = float.NegativeInfinity, _ginfoAt = float.NegativeInfinity;
        private float _chatLift = -1f;
        private UiButton[] _vrEmotes;
        private bool _loggedLinks, _loggedOffline;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (_subscribed) return;
            WowPlay.Event += OnGameEvent;
            WowPlay.Started += OnStarted;
            WowPlay.Ended += OnEnded;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                WowPlay.Event -= OnGameEvent;
                WowPlay.Started -= OnStarted;
                WowPlay.Ended -= OnEnded;
                _subscribed = false;
            }
            ReleaseHolder();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnStarted(IWowSession s)
        {
            Lines.Clear();
            _version++;
            Unread = 0;
            _deskOffset = _vrOffset = 0;
            _mode = _sticky = ChatKind.Say;
            _tellTarget = _stickyTell = _lastTellFrom = null;
            _channelTarget = _stickyChannel = 0;
            _lostText = null;
            _ginfoAt = float.NegativeInfinity;
            _tellTimer = Time.unscaledTime;   // ChatFrame_OnLoad: self.tellTimer = GetTime() (ChatFrame.lua:2289)
            WowLog.Info("UI: chat ready (" + (s is OnlineSession ? "online: WoW chat, Enter / \"/\" on desktop, the palm menu's Chat window in VR"
                                                                 : "offline: chat is on the online realm; local lines (text emotes, AFK / DND) show, \"/\" opens the edit box for slash commands on the desktop") + ").");
        }

        private void OnEnded()
        {
            CloseEdit(false);
            if (_keyboard != null) _keyboard.Close();
            _vrWanted = false;
            if (_vr != null) _vr.SetShown(false);
            ReleaseHolder();
        }

        // ------------------------------------------------------------------ events

        private void OnGameEvent(GameEvent e)
        {
            if (e.Type == GameEventType.SocialChanged) { GuildInfoLine(e.Amount); return; }
            if (e.Type != GameEventType.ChatMessage) return;
            IWowSession s = WowPlay.Session;
            var kind = (ChatKind)e.Amount;
            string name = SpeakerName(s, e.SourceGuid);
            int linkAt;
            string line = FormatAt(kind, e.SourceGuid, name, e.Text, e.SpellId, out linkAt);
            AddLine(kind, line, ColorOf(kind, e.SourceGuid), string.IsNullOrEmpty(name) ? null : name, e.SourceGuid, linkAt);
            bool mine = s != null && s.Player != null && e.SourceGuid != 0 && e.SourceGuid == s.Player.Guid;
            if (kind == ChatKind.Whisper && IsPlayer(e.SourceGuid) && !string.IsNullOrEmpty(name))
            {
                _lastTellFrom = name;   // ChatEdit_SetLastTellTarget(arg2) (ChatFrame.lua 2941)
                float now = Time.unscaledTime;
                if (now > _tellTimer) UiSound.Named("TellMessage");   // ChatFrame.lua 2943-2946
                _tellTimer = now + TellAlertTime;
            }
            // duelsnd: RaidWarningFrame_OnEvent (CHAT_MSG_RAID_WARNING) -> RaidNotice_AddMessage + PlaySound("RaidWarning") (RaidWarning.lua:107-108);
            // RaidBossEmoteFrame_OnEvent (CHAT_MSG_RAID_BOSS_EMOTE) -> PlaySound("RaidBossEmoteWarning") (:130-138). SoundEntries 8959 / 12197.
            if (kind == ChatKind.RaidWarning) UiSound.Added("RaidWarning", "RaidWarningFrame_OnEvent, RaidWarning.lua:108");
            else if (kind == ChatKind.RaidBossEmote) UiSound.Added("RaidBossEmoteWarning", "RaidBossEmoteFrame_OnEvent, RaidWarning.lua:137");
            if (kind != ChatKind.System && !mine && kind != ChatKind.WhisperSent && !VrOpen && WowPlayer.XrPresent) Unread++;
        }

        private static string SpeakerName(IWowSession s, ulong guid)
        {
            if (s == null || guid == 0) return null;
            string n = null;
            try { n = s.ChatSender(guid); }
            catch (Exception) { n = null; }
            if (!string.IsNullOrEmpty(n)) return n;
            EntityState e = s.Find(guid);
            return e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : null;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            _session = s;
            if (s == null || UiArt.Files == null)
            {
                if (_desk != null) _desk.SetShown(false);
                if (_vr != null) _vr.SetShown(false);
                ReleaseHolder();
                return;
            }
            bool online = s is OnlineSession;
            bool xr = WowPlayer.XrPresent;
            try
            {
                if (!xr)
                {
                    // iteration 11 (socialui): offline too - the local lines (text emotes, AFK / DND) and "/" for slash commands
                    if (!online)
                    {
                        ReleaseHolder();
                        if (!_loggedOffline) { _loggedOffline = true; WowLog.Info("UI: chat frame (desktop) offline: local lines only, \"/\" opens the edit box for slash commands (emotes, /afk, /dnd), Enter stays the Somnium client's."); }
                    }
                    EnsureDesk();
                    if (_desk != null)
                    {
                        _desk.SetShown(true);
                        DeskKeys();
                        DeskRefresh();
                    }
                }
                else
                {
                    if (_desk != null && _desk.Shown) { CloseEdit(false); _desk.SetShown(false); }
                    ReleaseHolder();
                }

                if (xr)
                {
                    if (_vrWanted) EnsureVr();
                    bool show = _vrWanted && _vr != null;
                    if (_vr != null && _vr.Shown != show) { _vr.SetShown(show); if (!show && _keyboard != null) _keyboard.Close(); }
                    if (show) { PlaceVr(); VrRefresh(); }
                }
                else if (_vr != null && _vr.Shown)
                {
                    _vr.SetShown(false);
                    if (_keyboard != null) _keyboard.Close();
                }
            }
            catch (Exception ex)
            {
                if (!_failed) { _failed = true; WowLog.Error("UI: chat failed (logged once): " + ex); }
            }
        }

        private bool _failed;

        // ------------------------------------------------------------------ desktop

        private void EnsureDesk()
        {
            if (_desk != null) return;
            bool pointable = UiKit.Pointable, over = UiKit.Over;
            try
            {
                UiKit.Over = false;
                UiKit.Pointable = true;
                // Below the HUD (order 5): ChatFrameTemplate frameStrata="BACKGROUND"; the edit box is DIALOG strata (on a sub-canvas).
                _desk = UiSurface.MakeScreen("WoW Chat (desktop)", transform, 4);
                UiKit.Surface = _desk;
                _deskScaler = _desk.Canvas.GetComponent<CanvasScaler>();
                // ChatFrame1: Anchor BOTTOMLEFT (32, 95), 430 x 120.
                _deskFrame = _desk.Anchor("ChatFrame1", new Vector2(0f, 0f), FrameW, FrameH, FrameX, FrameY);
                // FloatingBorderedFrame's Background: TOPLEFT (-2, 3), BOTTOMRIGHT (3, -6) (FloatingChatFrame.xml 7-28).
                _deskBg = UiKit.Pic(_deskFrame, "Background", UiArt.Tex(BgTex), -2f, -3f, FrameW + 5f, FrameH + 9f);
                _deskBg.color = new Color(0f, 0f, 0f, BackgroundAlpha);
                _deskBg.enabled = false;
                RectTransform clip = UiKit.Rect(_deskFrame, "Clip", 0f, 0f, FrameW, FrameH);
                clip.gameObject.AddComponent<RectMask2D>();
                _deskText = LogText(clip, FrameW, FontSize);
                _deskLinks = MakeLinks(clip, _deskText, false, 0f, 0f);

                // ChatFrame1ButtonFrame (left of the frame, 29 wide): up, down, bottom (ours: only while hovered / typing / scrolled).
                RectTransform buttons = UiKit.Rect(_deskFrame, "ButtonFrame", -ButtonFrameW - 2f, 0f, ButtonFrameW, FrameH);
                _deskButtonsGroup = buttons.gameObject.AddComponent<CanvasGroup>();
                UiKit.ArtButton(buttons, "ScrollUp", ScrollUpUp, ScrollUpDown, ScrollUpOff, null, 0f, FrameH - 96f, 32f, () => Scroll(ref _deskOffset, 1));
                UiKit.ArtButton(buttons, "ScrollDown", ScrollDownUp, ScrollDownDown, ScrollDownOff, null, 0f, FrameH - 64f, 32f, () => Scroll(ref _deskOffset, -1));
                UiKit.ArtButton(buttons, "ScrollToBottom", ScrollEndUp, ScrollEndDown, ScrollEndOff, null, 0f, FrameH - 32f, 32f, () => { _deskOffset = 0; });

                // The edit box: TOPLEFT (-5, -2) / TOPRIGHT (5, -2) of the frame's bottom, 32 tall (FloatingChatFrame.xml 681-703).
                RectTransform edit = SubCanvas(_deskFrame, "ChatFrame1EditBox", -5f, FrameH + 2f, FrameW + 10f, 32f);
                _deskEditRoot = edit;
                _deskEditGroup = edit.gameObject.AddComponent<CanvasGroup>();
                float w = FrameW + 10f;
                UiKit.Pic(edit, "Left", UiArt.Tex(EditLeft), 0f, 0f, 32f, 32f);
                UiKit.Pic(edit, "Mid", UiArt.Tex(EditMid), 32f, 0f, w - 64f, 32f);   // horizTile: the 32 px middle stretched (it is uniform across)
                UiKit.Pic(edit, "Right", UiArt.Tex(EditRight), w - 32f, 0f, 32f, 32f);
                _deskFocusArt = new[]
                {
                    UiKit.Pic(edit, "FocusLeft", UiArt.Tex(FocusLeft), 0f, 0f, 32f, 32f),
                    UiKit.Pic(edit, "FocusMid", UiArt.Tex(FocusMid), 32f, 0f, w - 64f, 32f),
                    UiKit.Pic(edit, "FocusRight", UiArt.Tex(FocusRight), w - 32f, 0f, 32f, 32f),
                };
                // $parentHeader: ChatFontNormal at LEFT (15, 0).
                _deskHeader = UiKit.Text(edit, "Header", "", FontSize, CSay, TextAlignmentOptions.MidlineLeft, 15f, 0f, 200f, 32f, null, false);
                _deskHeader.textWrappingMode = TextWrappingModes.NoWrap;
                _deskHeader.richText = false;
                _deskField = MakeField(edit, "Input", 60f, 0f, w - 72f, 32f, FontSize, out _deskArea);
                _deskField.onSubmit.AddListener(OnDeskSubmit);
                _deskField.onEndEdit.AddListener(OnDeskEndEdit);
                _deskField.onValueChanged.AddListener(OnDeskChanged);
                _desk.Finish();
                ShowEdit(false);
                SetHeader();
                if (!_loggedDesk)
                {
                    _loggedDesk = true;
                    WowLog.Info("UI: chat frame (desktop) - ChatFrame1 " + FrameW + " x " + FrameH + " at BOTTOMLEFT (" + FrameX + ", " + FrameY + "), lines fade after " + DisplayDuration
                                + " s, Enter or / to type, Up / Down history, mouse wheel scrolls; holding the EventSystem selection online: " + HoldSelectionOnline + ".");
                }
            }
            finally
            {
                UiKit.Surface = null;
                UiKit.Pointable = pointable;
                UiKit.Over = over;
            }
        }

        private static RectTransform SubCanvas(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform r = UiKit.Rect(parent, name, x, y, w, h);
            var c = r.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 6;   // over the HUD while typing (DIALOG strata)
            r.gameObject.AddComponent<GraphicRaycaster>();
            return r;
        }

        /// <summary>The lines: one TMP text, bottom-aligned and taller than the clip, so older lines run off the top (ScrollingMessageFrame).</summary>
        private static TextMeshProUGUI LogText(RectTransform clip, float w, float size)
        {
            TextMeshProUGUI t = UiKit.Text(clip, "Lines", "", size, Color.white, TextAlignmentOptions.BottomLeft, 0f, 0f, w, 10f, null, true);
            RectTransform r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0f, 0f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(w, 2000f);
            t.richText = true;   // only our own <color> + <noparse> tags: every line's text is inside <noparse>
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>A single-line TMP input field without a box of its own (the art is behind it); configured before it is enabled.</summary>
        private static TMP_InputField MakeField(Transform parent, string name, float x, float y, float w, float h, float size, out RectTransform area)
        {
            RectTransform r = UiKit.Rect(parent, name, x, y, w, h);
            GameObject go = r.gameObject;
            go.SetActive(false);
            var catcher = go.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            area = UiKit.Rect(r, "Text Area", 0f, 0f, w, h);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = UiKit.Text(area, "Text", "", size, Color.white, TextAlignmentOptions.MidlineLeft, 0f, 0f, w, h, null, false);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var f = go.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.targetGraphic = catcher;
            if (UiArt.Friz != null) f.fontAsset = UiArt.Friz;
            f.pointSize = size;
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.characterLimit = Letters;
            f.richText = false;
            f.customCaretColor = true;
            f.caretColor = Color.white;
            f.caretWidth = 2;
            f.selectionColor = new Color(0.9f, 0.7f, 0.1f, 0.35f);
            f.onFocusSelectAll = false;
            f.shouldHideSoftKeyboard = true;
            f.restoreOriginalTextOnEscape = false;
            f.contentType = TMP_InputField.ContentType.Standard;
            go.SetActive(true);
            return f;
        }

        private void ShowEdit(bool on)
        {
            if (_deskEditGroup == null) return;
            _deskEditGroup.alpha = on ? 1f : 0f;
            _deskEditGroup.blocksRaycasts = on;
            _deskEditGroup.interactable = on;
            if (_deskFocusArt != null) for (int i = 0; i < _deskFocusArt.Length; i++) _deskFocusArt[i].enabled = on;
        }

        /// <summary>Desktop keys: Enter / "/" open the edit box, Escape closes it, Up / Down walk the history; the selection holder.</summary>
        private void DeskKeys()
        {
            EventSystem es = EventSystem.current;
            if (_releaseAt >= 0 && Time.frameCount >= _releaseAt)
            {
                _releaseAt = -1;
                if (es != null && _deskField != null && es.currentSelectedGameObject == _deskField.gameObject && !_typing) es.SetSelectedGameObject(null);
            }
            if (_typing)
            {
                if (_deskField == null || es == null || es.currentSelectedGameObject != _deskField.gameObject)
                {
                    // Focus went elsewhere (a click on the world or another window): the edit box closes like ChatEdit_OnEditFocusLost.
                    if (Time.frameCount > _openedFrame + 1) CloseEdit(false);
                    return;
                }
                if (_caretToEnd && _deskField.isFocused) { _caretToEnd = false; _deskField.MoveTextEnd(false); }
                bool esc = false, up = false, down = false;
                try { esc = Input.GetKeyDown(KeyCode.Escape); up = Input.GetKeyDown(KeyCode.UpArrow); down = Input.GetKeyDown(KeyCode.DownArrow); }
                catch (Exception) { }
                if (esc) { CloseEdit(false); return; }
                if (up || down) History(up ? 1 : -1);
                return;
            }
            HoldSelection(es);
            if (Time.frameCount <= _closedFrame + 1) return;   // the Enter that sent must not reopen it (QuickChatControl's _lastCloseFrame rule too)
            bool enter = false, slash = false;
            try
            {
                enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                slash = Input.GetKeyDown(KeyCode.Slash) || Input.GetKeyDown(KeyCode.KeypadDivide);
            }
            catch (Exception) { return; }
            if (enter && !(_session is OnlineSession)) enter = false;   // offline: Enter is the Somnium client's (its quick chat), "/" is ours
            if (!enter && !slash) return;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null && sel != _holder) return;   // another field / the Somnium client's own chat has the keyboard
            OpenEdit(slash ? "/" : "");
        }

        private int _openedFrame = -10;

        private void OpenEdit(string initial)
        {
            if (_deskField == null) return;
            EventSystem es = EventSystem.current;
            if (es == null) { WowLog.Warn("UI: chat - no EventSystem, the edit box cannot take the keyboard"); return; }
            _typing = true;
            _submitted = false;
            _historyAt = -1;
            _openedFrame = Time.frameCount;
            ShowEdit(true);
            SetHeader();
            _deskField.interactable = true;   // off while closed: the input module's Submit (Enter) must not re-activate a closed field (TMP_InputField.OnSubmit)
            _deskField.SetTextWithoutNotify(initial ?? "");
            es.SetSelectedGameObject(_deskField.gameObject);
            _deskField.ActivateInputField();
            _caretToEnd = true;
            _deskOffset = 0;
        }

        private void CloseEdit(bool sent)
        {
            if (!_typing) return;
            _typing = false;
            _closedFrame = Time.frameCount;
            if (!sent)
            {
                // a Shift-click on a chat name takes the focus first (uGUI deselects on the press): kept for ShiftReopenSeconds (ours)
                _lostText = _deskField != null ? _deskField.text : null;
                _lostMode = _mode; _lostTell = _tellTarget; _lostChannel = _channelTarget;
                _lostAt = Time.unscaledTime;
            }
            if (_deskField != null)
            {
                _deskField.DeactivateInputField();
                _deskField.SetTextWithoutNotify("");
                _deskField.interactable = false;
            }
            ShowEdit(false);
            if (!sent) { _mode = _sticky; _tellTarget = _stickyTell; _channelTarget = _stickyChannel; }
            // The field stays selected one more frame, so the Enter of this frame cannot open the Somnium quick chat; then the holder.
            _releaseAt = Time.frameCount + 1;
        }

        private void OnDeskSubmit(string text)
        {
            if (!_typing) return;
            // The Enter that opened the box also reaches it as the input module's Submit event (TMP_InputField.OnSubmit -> SendOnSubmit): not a line.
            if (Time.frameCount <= _openedFrame) return;
            _submitted = true;
            string t = text ?? "";
            CloseEdit(true);
            Submit(t);
        }

        private void OnDeskEndEdit(string text)
        {
            // Escape or a click elsewhere (no submit): closes the edit box.
            if (_typing && !_submitted && Time.frameCount > _openedFrame + 1) CloseEdit(false);
        }

        /// <summary>ChatEdit_OnSpacePressed -> ChatEdit_ParseText(editBox, 0): "/s ", "/p ", "/w Name " ... switch the edit box's type.</summary>
        private void OnDeskChanged(string text)
        {
            if (!_typing || string.IsNullOrEmpty(text) || text[0] != '/' || text.IndexOf(' ') < 0) return;
            string rest;
            if (!TrySwitch(text, false, out rest)) return;
            _deskField.SetTextWithoutNotify(rest);
            _caretToEnd = true;
            if (_deskField.isFocused) { _caretToEnd = false; _deskField.MoveTextEnd(false); }
            SetHeader();
        }

        private void History(int dir)
        {
            if (_history.Count == 0 || _deskField == null) return;
            _historyAt = Mathf.Clamp(_historyAt + dir, -1, _history.Count - 1);
            string t = _historyAt < 0 ? "" : _history[_history.Count - 1 - _historyAt];
            _deskField.SetTextWithoutNotify(t);
            _deskField.MoveTextEnd(false);
        }

        private void SetHeader()
        {
            string h = HeaderText();
            Color c = ColorOf(_mode == ChatKind.Whisper ? ChatKind.WhisperSent : _mode, 0);
            if (_deskHeader != null)
            {
                _deskHeader.text = h;
                _deskHeader.color = c;
                float hw = _deskHeader.GetPreferredValues(h).x;
                if (_deskField != null)
                {
                    RectTransform r = (RectTransform)_deskField.transform;
                    UiKit.Move(r, 15f + hw + 2f, 0f);
                    UiKit.Size(r, Mathf.Max(40f, FrameW + 10f - (15f + hw + 2f) - 12f), 32f);
                    if (_deskArea != null) UiKit.Size(_deskArea, Mathf.Max(40f, FrameW + 10f - (15f + hw + 2f) - 12f), 32f);
                    if (_deskField.textComponent != null) { _deskField.textComponent.color = c; UiKit.Size(_deskField.textComponent.rectTransform, Mathf.Max(40f, FrameW + 10f - (15f + hw + 2f) - 12f), 32f); }
                }
            }
            if (_vrHeader != null) { _vrHeader.text = h; _vrHeader.color = c; }
            RefreshModeButtons();
        }

        /// <summary>CHAT_SAY_SEND "Say: " (1571), CHAT_YELL_SEND (1608), CHAT_EMOTE_SEND "%s " (1429), CHAT_PARTY_SEND (1539),
        /// CHAT_GUILD_SEND (1441), CHAT_WHISPER_SEND "Tell %s: " (1602); iteration 11: CHAT_OFFICER_SEND "Officer: " (1530), CHAT_RAID_SEND
        /// "Raid: " (1566), CHAT_RAID_WARNING_SEND "Raid Warning: " (1568), CHAT_BATTLEGROUND_SEND "Battleground: " (1400), CHAT_CHANNEL_SEND
        /// "[%d. %s]: " (1416).</summary>
        private string HeaderText()
        {
            switch (_mode)
            {
                case ChatKind.Yell: return "Yell: ";
                case ChatKind.Emote: return (_session != null && _session.Player != null && !string.IsNullOrEmpty(_session.Player.Name) ? _session.Player.Name : Unknown) + " ";
                case ChatKind.Party: return "Party: ";
                case ChatKind.Guild: return "Guild: ";
                case ChatKind.Whisper: return "Tell " + (_tellTarget ?? "") + ": ";
                case ChatKind.Officer: return SocialUi.G("CHAT_OFFICER_SEND", "Officer: ");
                case ChatKind.Raid: return SocialUi.G("CHAT_RAID_SEND", "Raid: ");
                case ChatKind.RaidWarning: return SocialUi.G("CHAT_RAID_WARNING_SEND", "Raid Warning: ");
                case ChatKind.Battleground: return SocialUi.G("CHAT_BATTLEGROUND_SEND", "Battleground: ");
                case ChatKind.Channel:
                    {
                        ChannelView c = ChannelByNumber(_channelTarget);
                        return SocialUi.F("CHAT_CHANNEL_SEND", "[%d. %s]: ", _channelTarget, c != null ? c.Name : "");
                    }
                default: return "Say: ";
            }
        }

        private void DeskRefresh()
        {
            // WowSettings.UiScale like the HUD: the canvas is referenced to 768 / scale units tall.
            float scale = Mathf.Clamp(WowSettings.UiScale, 0.75f, 1.5f);
            if (_deskScaler != null && scale != _deskScale)
            {
                _deskScale = scale;
                _deskScaler.referenceResolution = new Vector2(1024f / scale, 768f / scale);
            }
            CheckChannel();
            // uicore H11 / social H14: ChatFrame1 rises with the Bottom Left bar (vrcast's UiBars.ChatLift; FCF_UpdateDockPosition)
            float lift = UiBars.ChatLift;
            if (lift != _chatLift && _deskFrame != null)
            {
                _chatLift = lift;
                _deskFrame.anchoredPosition = new Vector2(FrameX, FrameY + lift);
            }
            bool hover = false;
            float wheel = 0f;
            try
            {
                Vector2 mouse = Input.mousePosition;
                hover = RectTransformUtility.RectangleContainsScreenPoint(_deskFrame, mouse, null)
                        || (_deskButtonsGroup != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_deskButtonsGroup.transform, mouse, null));
                if (hover) wheel = Input.mouseScrollDelta.y;
            }
            catch (Exception) { }
            if (wheel > 0.01f) Scroll(ref _deskOffset, 1);
            else if (wheel < -0.01f) Scroll(ref _deskOffset, -1);
            bool all = _typing || hover || _deskOffset > 0;
            if (_deskBg != null && _deskBg.enabled != all) _deskBg.enabled = all;
            if (_deskButtonsGroup != null)
            {
                float a = all ? 1f : 0f;
                if (_deskButtonsGroup.alpha != a) { _deskButtonsGroup.alpha = a; _deskButtonsGroup.blocksRaycasts = all; }
            }
            if (_deskComposed != _version || _deskComposedOffset != _deskOffset || _deskComposedAll != all || _deskFading)
            {
                _deskComposed = _version;
                _deskComposedOffset = _deskOffset;
                _deskComposedAll = all;
                _deskText.text = Compose(_deskOffset, all, out _deskFading);
                PlaceLinks(_deskLinks);
            }
        }

        private static void Scroll(ref int offset, int by)
        {
            offset = Mathf.Clamp(offset + by, 0, Mathf.Max(0, Lines.Count - 1));
        }

        /// <summary>The lines up to offset from the newest, oldest first; faded lines (after DisplayDuration + FadeSeconds) left out unless all.</summary>
        private static string Compose(int offset, bool all, out bool fading)
        {
            fading = false;
            int last = Lines.Count - 1 - offset;
            if (last < 0) return "";
            int first = Mathf.Max(0, last - ComposeLines + 1);
            float now = Time.unscaledTime;
            var sb = new StringBuilder(1024);
            for (int i = first; i <= last; i++)
            {
                Line l = Lines[i];
                float age = now - l.Time;
                float a = 1f;
                if (!all && age > DisplayDuration)
                {
                    a = 1f - (age - DisplayDuration) / FadeSeconds;
                    if (a <= 0f) continue;
                    fading = true;
                }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#").Append(l.Hex).Append(((int)(Mathf.Clamp01(a) * 255f)).ToString("X2")).Append('>');
                if (l.Name != null)
                {
                    // iteration 11: the player's [Name] as a TMP link (id = the line's Seq) outside the text's <noparse>; a button covers it
                    if (l.Pre.Length > 0) sb.Append("<noparse>").Append(l.Pre).Append("</noparse>");
                    sb.Append("<link=\"").Append(l.Seq).Append("\"><noparse>[").Append(l.Name).Append("]</noparse></link>");
                    if (l.Post.Length > 0) sb.Append("<noparse>").Append(l.Post).Append("</noparse>");
                }
                else sb.Append("<noparse>").Append(l.Escaped).Append("</noparse>");
                sb.Append("</color>");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the selection holder (desktop, online)

        private void HoldSelection(EventSystem es)
        {
            if (!HoldSelectionOnline || es == null || _typing || _releaseAt >= 0 || !(_session is OnlineSession)) return;
            if (_holder == null)
            {
                _holder = new GameObject("WoW Chat keyboard holder");
                _holder.transform.SetParent(transform, false);
            }
            if (es.currentSelectedGameObject != null) return;   // somebody else's field / button: never taken away
            es.SetSelectedGameObject(_holder);
            _holding = true;
            if (!_loggedHold)
            {
                _loggedHold = true;
                WowLog.Info("UI: chat - holding the EventSystem selection with an empty object while online, so Enter opens the WoW chat (the Somnium "
                            + "client's own quick chat opens only when nothing is selected); released offline, in VR and back in the room.");
            }
        }

        private void ReleaseHolder()
        {
            if (!_holding) return;
            _holding = false;
            EventSystem es = EventSystem.current;
            if (es != null && _holder != null && es.currentSelectedGameObject == _holder) es.SetSelectedGameObject(null);
        }

        // ------------------------------------------------------------------ sending (both modes)

        /// <summary>
        /// ChatEdit_SendText / ChatEdit_ParseText(editBox, 1) (ChatFrame.lua 3997-4110): chat-type slash commands send in that type (sticky
        /// types stay), /i /inv /invite Name invites (SLASH_INVITE1-3, GlobalStrings 6591-6593), /help lists ours, an unknown command prints
        /// HELP_TEXT_SIMPLE; a line starting with one '.' is a GM command (IWowSession.RunGmCommand, the server answers or says it aloud
        /// for a normal account), anything else goes out in the edit box's type.
        /// </summary>
        private void Submit(string raw)
        {
            IWowSession s = WowPlay.Session;
            string text = (raw ?? "").Trim();
            if (text.Length == 0) { _mode = _sticky; _tellTarget = _stickyTell; _channelTarget = _stickyChannel; SetHeader(); return; }   // empty Enter = just close, like WoW
            AddHistory(text);
            if (s == null) return;
            if (text[0] == '/')
            {
                string rest;
                if (TrySwitch(text, true, out rest))
                {
                    if (rest.Length > 0) Send(s, rest);
                    AfterSend();
                    return;
                }
                string cmd = FirstWord(text).ToUpperInvariant();
                string arg = text.Length > cmd.Length ? text.Substring(cmd.Length).Trim() : "";
                bool done;
                try { done = Slash(s, cmd, arg); }
                catch (Exception e) { done = true; WowLog.Error("UI: chat - the command " + cmd + " failed: " + e); }
                if (!done) AddLocal(HelpTextSimple);
                _mode = _sticky; _tellTarget = _stickyTell; _channelTarget = _stickyChannel;
                SetHeader();
                return;
            }
            if (text[0] == '.' && (text.Length < 2 || text[1] != '.'))
            {
                s.RunGmCommand(text);
                return;
            }
            Send(s, text);
            AfterSend();
        }

        private void Send(IWowSession s, string text)
        {
            if (_mode == ChatKind.Whisper && string.IsNullOrEmpty(_tellTarget)) { AddLocal(ErrNoReplyTarget); return; }
            string to = _mode == ChatKind.Whisper ? _tellTarget : _mode == ChatKind.Channel ? _channelTarget.ToString() : null;   // CHANNEL: "1".."10"
            try { s.SendChat(_mode, text, to); }
            catch (Exception e) { WowLog.Error("UI: chat - SendChat threw: " + e); }
            _deskOffset = _vrOffset = 0;
        }

        /// <summary>Sticky types stay for the next line; yell / emote / raid warning fall back to the last sticky type (ChatTypeInfo sticky
        /// = 1 for SAY, PARTY, RAID, GUILD, OFFICER, WHISPER, CHANNEL, BATTLEGROUND; ChatFrame.lua:34-94).</summary>
        private void AfterSend()
        {
            if (_mode == ChatKind.Say || _mode == ChatKind.Party || _mode == ChatKind.Guild || _mode == ChatKind.Whisper || _mode == ChatKind.Officer
                || _mode == ChatKind.Raid || _mode == ChatKind.Battleground || _mode == ChatKind.Channel)
            {
                _sticky = _mode;
                _stickyTell = _tellTarget;
                _stickyChannel = _channelTarget;
            }
            else
            {
                _mode = _sticky;
                _tellTarget = _stickyTell;
                _channelTarget = _stickyChannel;
            }
            SetHeader();
        }

        private void AddHistory(string text)
        {
            if (_history.Count > 0 && _history[_history.Count - 1] == text) return;
            _history.Add(text);
            if (_history.Count > HistoryLines) _history.RemoveAt(0);
        }

        private static string FirstWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int sp = s.IndexOf(' ');
            return sp < 0 ? s : s.Substring(0, sp);
        }

        /// <summary>
        /// ChatEdit_HandleChatType: a chat-type command at the start switches the type. SLASH_SAY /s /say, SLASH_YELL /y /yell /sh /shout,
        /// SLASH_EMOTE /e /em /emote /me, SLASH_PARTY /p /party, SLASH_GUILD /g /gc /gu /guild, SLASH_WHISPER /w /whisper /t /tell /send Name,
        /// SLASH_REPLY /r /reply (GlobalStrings 6480-6869). While typing (send = false) the command needs its trailing space, a whisper also
        /// the space after the name; rest = what follows.
        /// </summary>
        private bool TrySwitch(string text, bool send, out string rest)
        {
            rest = "";
            string cmd = FirstWord(text);
            if (!send && cmd.Length == text.Length) return false;
            string after = text.Length > cmd.Length ? text.Substring(cmd.Length + 1) : "";
            // iteration 11: "/1" .. "/10" = that joined channel (ChatEdit_HandleChatType, ChatFrame.lua:3924-3936; not joined = an unknown command)
            int num;
            if (cmd.Length > 1 && cmd.Length <= 3 && int.TryParse(cmd.Substring(1), out num) && num > 0 && num <= 10)
            {
                if (ChannelByNumber(num) == null) return false;
                _mode = ChatKind.Channel; _channelTarget = num; rest = after; return true;
            }
            switch (cmd.ToUpperInvariant())
            {
                // iteration 11: SLASH_OFFICER /o /osay /officer (GlobalStrings.lua:6660-6665), SLASH_RAID /raid /ra /rsay (6701-6706),
                // SLASH_RAID_WARNING /rw (6715), SLASH_BATTLEGROUND /bg /battleground (6355-6356), SLASH_CHANNEL /c /csay Name (6377-6378)
                case "/O": case "/OSAY": case "/OFFICER": _mode = ChatKind.Officer; rest = after; return true;
                case "/RA": case "/RAID": case "/RSAY": _mode = ChatKind.Raid; rest = after; return true;
                case "/RW": _mode = ChatKind.RaidWarning; rest = after; return true;
                case "/BG": case "/BATTLEGROUND": _mode = ChatKind.Battleground; rest = after; return true;
                case "/C": case "/CSAY":
                    {
                        string ch = FirstWord(after.TrimStart());
                        if (ch.Length == 0) return false;
                        string tail = after.TrimStart().Substring(ch.Length);
                        if (!send && tail.Length == 0) return false;   // the channel is still being typed
                        ChannelView cv = ChannelByRef(ch);
                        if (cv == null)
                        {
                            if (send) AddLocal(SocialUi.F("CHAT_NOT_MEMBER_NOTICE", "Not on channel %s.", ch));   // GlobalStrings.lua:1523
                            rest = "";
                            return send;
                        }
                        _mode = ChatKind.Channel;
                        _channelTarget = cv.Number;
                        rest = tail.StartsWith(" ", StringComparison.Ordinal) ? tail.Substring(1) : tail;
                        return true;
                    }
                case "/S": case "/SAY": _mode = ChatKind.Say; rest = after; return true;
                case "/Y": case "/YELL": case "/SH": case "/SHOUT": _mode = ChatKind.Yell; rest = after; return true;
                case "/E": case "/EM": case "/EMOTE": case "/ME": _mode = ChatKind.Emote; rest = after; return true;
                case "/P": case "/PARTY": _mode = ChatKind.Party; rest = after; return true;
                case "/G": case "/GC": case "/GU": case "/GUILD": _mode = ChatKind.Guild; rest = after; return true;
                case "/W": case "/WHISPER": case "/T": case "/TELL": case "/SEND":
                    {
                        string who = FirstWord(after.TrimStart());
                        if (who.Length == 0) return false;
                        string tail = after.TrimStart().Substring(who.Length);
                        if (!send && tail.Length == 0) return false;   // the name is still being typed
                        _mode = ChatKind.Whisper;
                        _tellTarget = who;
                        rest = tail.StartsWith(" ", StringComparison.Ordinal) ? tail.Substring(1) : tail;
                        return true;
                    }
                case "/R": case "/REPLY":
                    if (string.IsNullOrEmpty(_lastTellFrom))
                    {
                        if (send) AddLocal(ErrNoReplyTarget);   // only once, when the line is sent (not at every key while typing)
                        rest = "";
                        return send;
                    }
                    _mode = ChatKind.Whisper;
                    _tellTarget = _lastTellFrom;
                    rest = after;
                    return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ iteration 11: WoW's other slash commands (socialui)

        /// <summary>
        /// The slash commands that do not switch the chat type (ChatFrame.lua SlashCmdList, GlobalStrings SLASH_*): /i /invite, /help,
        /// /chat /chathelp, /join /channel /chan Name [password], /leave /chatleave /chatexit N|Name, /chatlist /chatwho /chatinfo [N|Name],
        /// /afk /away [text], /dnd /busy [text], /who [text], /friend /friends [Name [note]] (toggles; no name and no player target = the
        /// Social window), /removefriend /remfriend Name, /ignore /unignore [Name] (the target when empty), /ginvite /gremove /gpromote
        /// /gdemote /gleader [Name], /gquit, /gdisband (CONFIRM_GUILD_DISBAND), /gmotd text, /groster, /ginfo; then interact's commands
        /// (UiInteract.TrySlash: /duel /follow /inspect /trade /readycheck ...) and every emote command of the install (/wave ...) with the
        /// current target. False = unknown (HELP_TEXT_SIMPLE).
        /// </summary>
        /// <summary>Lead 2026-10-12: one macro line as if typed and sent in the chat (UiMacros.Run); false when there is no chat.</summary>
        public static bool RunLine(string line)
        {
            UiChat c = Instance;
            if (c == null) return false;
            c.Submit(line);
            return true;
        }

        /// <summary>Lead 2026-10-12 (macros): /cast /use /target /cleartarget /startattack /stopattack /stopcasting - WoW's secure commands
        /// (ChatFrame.lua SecureCmdList), ours without conditions (the first clause; [@player] casts on yourself).</summary>
        private bool GameCommand(IWowSession s, string cmd, string arg)
        {
            PlayerState p = s.Player;
            bool self;
            string what = UiMacros.StripOptions(arg, out self);
            switch (cmd)
            {
                case "/CAST": case "/SPELL": case "/USE":
                {
                    AbilityView a = cmd == "/USE" && UiMacros.FindItemIndex(p, what) >= 0 ? null : UiMacros.FindSpell(p, what);
                    if (a != null) { s.CastSpell(a.SpellId, self ? p.Guid : 0UL); return true; }
                    int bag = UiMacros.FindItemIndex(p, what);
                    if (bag >= 0) { s.UseItem(bag); return true; }
                    AddLocal("Unknown spell or item: " + what);   // ours
                    return true;
                }
                case "/TARGET": case "/TAR":
                {
                    if (what.Length == 0) return true;
                    ulong best = 0;
                    float bestD = float.MaxValue;
                    var list = s.Entities;
                    for (int i = 0; list != null && i < list.Count; i++)
                    {
                        EntityState e = list[i];
                        if (e == null || string.IsNullOrEmpty(e.Name) || e.Name.IndexOf(what, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        float dx = e.Pos.X - p.Pos.X, dy = e.Pos.Y - p.Pos.Y, d = dx * dx + dy * dy;
                        if (string.Equals(e.Name, what, StringComparison.OrdinalIgnoreCase)) d -= 1e6f;   // an exact name first
                        if (d < bestD) { bestD = d; best = e.Guid; }
                    }
                    if (best != 0) s.SetTarget(best);
                    return true;
                }
                case "/CLEARTARGET": s.SetTarget(0); return true;
                case "/STARTATTACK": if (s.TargetGuid != 0) s.StartAttack(s.TargetGuid); return true;
                case "/STOPATTACK": s.StopAttack(); return true;
                case "/STOPCASTING": s.CancelCast(); return true;
            }
            return false;
        }

        private bool Slash(IWowSession s, string cmd, string arg)
        {
            if (GameCommand(s, cmd, arg)) return true;   // lead 2026-10-12
            IWowSocial soc = SocialUi.Soc(s);
            string word = FirstWord(arg);
            string after = arg.Length > word.Length ? arg.Substring(word.Length).Trim() : "";
            switch (cmd)
            {
                case "/I": case "/INV": case "/INVITE":
                    if (word.Length > 0) s.InviteToGroup(word);
                    else AddLocal(HelpTextSimple);
                    return true;
                case "/H": case "/HELP": case "/?":
                    AddLocal("Dreamport chat commands: /s /y /e /me /p /g /o /ra /rw /bg /1../10 /c Name /w /t Name /r, /i Name (invite), /join /leave "
                             + "/chatlist, /afk /dnd, /who, /friend /ignore, /ginvite /gremove /gpromote /gdemote /gleader /gquit /gmotd /groster /ginfo, "
                             + "/duel /follow /inspect /trade /readycheck, the emotes (/wave ...), .gm commands (GM accounts).");   // ours
                    return true;
                case "/CHAT": case "/CHATHELP":
                    // CHAT_HELP_TEXT_LINE1-6 and 16 (GlobalStrings.lua:1443-1448, 1458) - only the commands that work here (lines 7-15 are
                    // channel moderation: not in this version, proposal-social.md G5)
                    int[] lines = { 1, 2, 3, 4, 5, 6, 16 };
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string key = "CHAT_HELP_TEXT_LINE" + lines[i], line = SocialUi.G(key, "");
                        if (line.Length > 0 && line != key) AddLocal(line);
                    }
                    return true;
                case "/JOIN": case "/CHANNEL": case "/CHAN":
                    if (word.Length == 0) AddLocal(SocialUi.G("CHAT_JOIN_HELP", "Type /join <channelname> [password] to create a channel or join an existing one."));   // :1466
                    else if (soc != null) soc.JoinChannel(word, after.Length > 0 ? after : null);
                    return true;
                case "/LEAVE": case "/CHATLEAVE": case "/CHATEXIT":
                    if (word.Length > 0 && soc != null) soc.LeaveChannel(word);   // SlashCmdList["LEAVE"]: nothing without a name (ChatFrame.lua:1553-1564)
                    return true;
                case "/CHATLIST": case "/CHATWHO": case "/CHATINFO":
                    if (word.Length > 0) { if (soc != null) soc.ListChannel(word); }
                    else ListJoined(soc);
                    return true;
                case "/AFK": case "/AWAY":
                    if (soc != null) soc.SetAfk(arg);   // SendChatMessage(msg, "AFK") (ChatFrame.lua:1936-1938)
                    return true;
                case "/DND": case "/BUSY":
                    if (soc != null) soc.SetDnd(arg);
                    return true;
                case "/WHO":
                    if (arg.Length == 0) UiSocial.Open(UiSocial.TabWho);   // ShowWhoPanel (ChatFrame.lua:1944-1951)
                    if (soc != null) soc.Who(arg);
                    return true;
                case "/FRIEND": case "/FRIENDS":
                    {
                        string n = word.Length > 0 ? word : TargetPlayerName(s);
                        if (string.IsNullOrEmpty(n)) { UiSocial.Open(UiSocial.TabFriends); return true; }   // ToggleFriendsPanel
                        if (soc == null) return true;
                        FriendView f = UiSocial.FriendByName(soc.State, n);
                        if (f != null) soc.RemoveFriend(f.Guid);    // AddOrRemoveFriend (ChatFrame.lua:1957-1964)
                        else soc.AddFriend(n, after);
                        return true;
                    }
                case "/REMOVEFRIEND": case "/REMFRIEND":
                    {
                        if (soc == null || word.Length == 0) return true;
                        FriendView f = UiSocial.FriendByName(soc.State, word);
                        if (f != null) soc.RemoveFriend(f.Guid);
                        else AddLocal(SocialUi.G("ERR_FRIEND_NOT_FOUND", "Player not found."));   // GlobalStrings.lua:3058
                        return true;
                    }
                case "/IGNORE":
                case "/UNIGNORE":
                    {
                        string n = word.Length > 0 ? word : TargetPlayerName(s);
                        if (string.IsNullOrEmpty(n)) { UiSocial.Open(UiSocial.TabFriends); return true; }   // ToggleIgnorePanel
                        if (soc == null) return true;
                        IgnoreView ig = UiSocial.IgnoredByName(soc.State, n);
                        if (ig != null) soc.RemoveIgnore(ig.Guid);                                 // AddOrDelIgnore / DelIgnore
                        else if (cmd == "/IGNORE") soc.AddIgnore(n);
                        else AddLocal(SocialUi.G("ERR_IGNORE_NOT_FOUND", "Player not found."));   // :3129
                        return true;
                    }
                case "/GINVITE": case "/GUILDINVITE":
                case "/GREMOVE": case "/GUILDREMOVE":
                case "/GPROMOTE": case "/GUILDPROMOTE":
                case "/GDEMOTE": case "/GUILDDEMOTE":
                case "/GLEADER": case "/GUILDLEADER":
                    {
                        string n = arg.Length > 0 ? word : TargetPlayerName(s);   // msg == "" -> UnitName("target") (ChatFrame.lua:1860-1880)
                        if (soc == null || string.IsNullOrEmpty(n)) return true;
                        if (cmd == "/GINVITE" || cmd == "/GUILDINVITE") soc.GuildInvite(n);
                        else if (cmd == "/GREMOVE" || cmd == "/GUILDREMOVE") soc.GuildRemove(n);
                        else if (cmd == "/GPROMOTE" || cmd == "/GUILDPROMOTE") soc.GuildPromote(n);
                        else if (cmd == "/GDEMOTE" || cmd == "/GUILDDEMOTE") soc.GuildDemote(n);
                        else soc.GuildSetLeader(n);
                        return true;
                    }
                case "/GQUIT": case "/GUILDQUIT":
                    if (soc != null) soc.GuildLeave();
                    return true;
                case "/GDISBAND": case "/GUILDDISBAND":
                    {
                        // IsGuildLeader() -> StaticPopup CONFIRM_GUILD_DISBAND (ChatFrame.lua:1914-1918)
                        GuildView g = soc != null ? soc.State.Guild : null;
                        if (g == null || g.MyRank != 0) return true;
                        SocialUi.Confirm("CONFIRM_GUILD_DISBAND", SocialUi.G("CONFIRM_GUILD_DISBAND", "Do you really want to disband your guild?"),   // :1812
                            SocialUi.G("ACCEPT", "Accept"), SocialUi.G("CANCEL", "Cancel"), () => { IWowSocial x = SocialUi.Soc(WowPlay.Session); if (x != null) x.GuildDisband(); });
                        return true;
                    }
                case "/GMOTD": case "/GUILDMOTD":
                    if (soc != null) soc.GuildSetMotd(arg);
                    return true;
                case "/GROSTER": case "/GUILDROSTER":
                    if (soc != null && soc.State.Guild != null) UiSocial.Open(UiSocial.TabGuild);   // IsInGuild() (ChatFrame.lua:1924-1926)
                    return true;
                case "/GINFO": case "/GUILDINFO":
                    if (soc == null) return true;
                    if (soc.State.Guild == null) { AddLocal(SocialUi.G("ERR_GUILD_PLAYER_NOT_IN_GUILD", "You are not in a guild.")); return true; }   // :3113
                    _ginfoAt = Time.unscaledTime;
                    soc.GuildRefresh();
                    return true;
            }
            if (UiInteract.TrySlash(cmd, arg)) return true;   // interact's hook (proposal-interact: /duel /follow /inspect /trade /readycheck /pvp ...)
            if (soc != null)
            {
                ulong target = s.TargetGuid;
                if (soc.TextEmote(cmd.ToLowerInvariant(), target)) return true;   // EMOTE*_CMD* of the install (WowEmoteTable)
            }
            return false;
        }

        /// <summary>/chatlist without a name (ListChannels): the joined channels in one line - ours: "Channels: [1. General - Elwynn
        /// Forest], ..." (CHANNELS GlobalStrings.lua:1362).</summary>
        private static void ListJoined(IWowSocial soc)
        {
            if (soc == null) return;
            List<ChannelView> l = soc.State.Channels;
            var sb = new StringBuilder(SocialUi.G("CHANNELS", "Channels")).Append(':');
            int n = 0;
            for (int i = 0; i < l.Count; i++)
            {
                if (l[i] == null) continue;
                sb.Append(n++ == 0 ? " " : ", ").Append('[').Append(l[i].Display).Append(']');
            }
            AddLine(ChatKind.ChannelNotice, sb.ToString(), CChannelNotice);
        }

        /// <summary>UnitName("target") of a player target, null otherwise.</summary>
        private static string TargetPlayerName(IWowSession s)
        {
            if (s == null || s.TargetGuid == 0) return null;
            EntityState e = s.Find(s.TargetGuid);
            return e != null && e.Kind == EntityKind.Player && !string.IsNullOrEmpty(e.Name) ? e.Name : null;
        }

        private static ChannelView ChannelByNumber(int n)
        {
            IWowSocial soc = SocialUi.Soc(WowPlay.Session);
            if (soc == null || n <= 0) return null;
            List<ChannelView> l = soc.State.Channels;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].Number == n) return l[i];
            return null;
        }

        /// <summary>A joined channel by its number or name (the full name, or its part before " - ": "General").</summary>
        private static ChannelView ChannelByRef(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;
            int n;
            if (int.TryParse(word, out n)) return ChannelByNumber(n);
            IWowSocial soc = SocialUi.Soc(WowPlay.Session);
            if (soc == null) return null;
            List<ChannelView> l = soc.State.Channels;
            for (int i = 0; i < l.Count; i++)
            {
                ChannelView c = l[i];
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                if (string.Equals(c.Name, word, StringComparison.OrdinalIgnoreCase) || string.Equals(ShortName(c.Name), word, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        private static string ShortName(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            int d = full.IndexOf(" - ", StringComparison.Ordinal);
            return d > 0 ? full.Substring(0, d) : full;
        }

        /// <summary>/ginfo: GUILD_NAME_TEMPLATE "Guild: %s" (GlobalStrings.lua:3966) and GUILD_INFO_TEMPLATE (3954, ours: month, day, year
        /// as UiGuild's info line) once the SMSG_GUILD_INFO answer changed the guild (within GuildInfoWait).</summary>
        private void GuildInfoLine(int parts)
        {
            if ((parts & (int)SocialPart.Guild) == 0 || Time.unscaledTime - _ginfoAt > GuildInfoWait) return;
            IWowSocial soc = SocialUi.Soc(WowPlay.Session);
            GuildView g = soc != null ? soc.State.Guild : null;
            if (g == null || g.CreatedYear <= 0) return;
            _ginfoAt = float.NegativeInfinity;
            AddLocal(SocialUi.F("GUILD_NAME_TEMPLATE", "Guild: %s", g.Name));
            AddLocal(SocialUi.F("GUILD_INFO_TEMPLATE", "Guild created %1$d-%2$d-%3$d, %4$d players, %5$d accounts", g.CreatedMonth, g.CreatedDay, g.CreatedYear, g.Members.Count, g.NumAccounts));
        }

        // ------------------------------------------------------------------ iteration 11: clickable names (socialui, proposal-social.md 4.9)

        /// <summary>A pool of transparent buttons over the [Name] links of a chat text (FloatingChatFrame.xml:505: only the links take the
        /// mouse; the rest of the chat lets clicks through to the world).</summary>
        private LinkSet MakeLinks(RectTransform clip, TextMeshProUGUI text, bool vr, float clipX, float clipY)
        {
            var set = new LinkSet { Clip = clip, Text = text, Buttons = new UiButton[LinkPool], Seq = new int[LinkPool], Vr = vr, ClipX = clipX, ClipY = clipY };
            for (int i = 0; i < LinkPool; i++)
            {
                int k = i;
                UiButton b = UiKit.Button(clip, "Name link " + (i + 1), 0f, 0f, 10f, 10f);
                RectTransform r = b.Rt;
                r.anchorMin = r.anchorMax = Vector2.zero;
                r.pivot = Vector2.zero;
                b.Click = () => LinkClick(set, k);
                if (vr) b.LongPress = () => LinkMenu(set, k);   // VR has no right click: a long press (UiPointer.LongPressSeconds) = the menu (ours)
                else b.RightClick = () => LinkMenu(set, k);
                b.gameObject.SetActive(false);
                set.Buttons[i] = b;
            }
            return set;
        }

        /// <summary>After the text changed: one button over each visible link (TMP linkInfo -> the characters' rectangle on its first line).</summary>
        private void PlaceLinks(LinkSet set)
        {
            if (set == null || set.Text == null || set.Clip == null) return;
            int used = 0;
            try
            {
                TextMeshProUGUI t = set.Text;
                t.ForceMeshUpdate();
                TMP_TextInfo ti = t.textInfo;
                Rect clip = set.Clip.rect;
                float clipW = clip.width, clipH = clip.height;
                for (int i = 0; ti != null && i < ti.linkCount && used < set.Buttons.Length; i++)
                {
                    TMP_LinkInfo li = ti.linkInfo[i];
                    int seq;
                    if (!int.TryParse(li.GetLinkID(), out seq)) continue;
                    int a = li.linkTextfirstCharacterIndex, z = a + li.linkTextLength - 1;
                    if (a < 0 || z < a || z >= ti.characterCount) continue;
                    TMP_CharacterInfo ca = ti.characterInfo[a];
                    if (ca.lineNumber < 0 || ca.lineNumber >= ti.lineCount) continue;
                    int e = a;
                    while (e < z && ti.characterInfo[e + 1].lineNumber == ca.lineNumber) e++;   // a wrapped name: its first line's part
                    TMP_CharacterInfo cb = ti.characterInfo[e];
                    TMP_LineInfo line = ti.lineInfo[ca.lineNumber];
                    // the text's pivot is the clip's bottom-left (LogText): keep the part inside the clip
                    float x0 = Mathf.Max(0f, ca.bottomLeft.x), x1 = Mathf.Min(clipW, cb.topRight.x);
                    float y0 = Mathf.Max(0f, line.descender), y1 = Mathf.Min(clipH, line.ascender);
                    if (y1 - y0 < 4f || x1 - x0 < 2f) continue;
                    UiButton b = set.Buttons[used];
                    b.Rt.anchoredPosition = new Vector2(x0, y0);
                    b.Rt.sizeDelta = new Vector2(x1 - x0, y1 - y0);
                    if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
                    set.Seq[used] = seq;
                    used++;
                }
            }
            catch (Exception ex)
            {
                if (!_linksFailed) { _linksFailed = true; WowLog.Warn("UI: chat - the name links could not be placed (logged once): " + ex.Message); }
            }
            for (int i = used; i < set.Buttons.Length; i++) if (set.Buttons[i] != null && set.Buttons[i].gameObject.activeSelf) set.Buttons[i].gameObject.SetActive(false);
            if (used > 0 && !_loggedLinks)
            {
                _loggedLinks = true;
                WowLog.Info("UI: chat - player names are clickable (" + (set.Vr ? "VR: press = whisper, long press = the FRIEND menu" : "left = whisper, Shift = the name into a popup / the edit box / Who, right = the FRIEND menu")
                            + "; " + LinkPool + " name buttons per chat surface, ours).");
            }
        }

        private bool _linksFailed;

        /// <summary>ItemRef.lua:1-69 SetItemRef "player": left = ChatFrame_SendTell; Shift = the name into an add-name popup / the edit box /
        /// SendWho("n-" .. name).</summary>
        private void LinkClick(LinkSet set, int k)
        {
            Line l = k >= 0 && k < set.Seq.Length ? BySeq(set.Seq[k]) : null;
            if (l == null || l.Name == null) return;
            if (!set.Vr && SocialUi.Shift()) { ShiftName(l.Name); return; }
            StartWhisper(l.Name);
        }

        private void ShiftName(string name)
        {
            if (SocialUi.PutNameInPopup(name)) return;   // ADD_IGNORE / ADD_FRIEND / ADD_GUILDMEMBER (ItemRef.lua:13-56)
            if (_typing && _deskField != null)
            {
                _deskField.SetTextWithoutNotify((_deskField.text ?? "") + name);   // ChatEdit_InsertLink (ItemRef.lua:57-58)
                _caretToEnd = true;
                return;
            }
            if (_lostText != null && Time.unscaledTime - _lostAt < ShiftReopenSeconds)
            {
                // the press on the name took the focus from the open edit box (uGUI deselects on press): reopen it as it was, plus the name
                _mode = _lostMode; _tellTarget = _lostTell; _channelTarget = _lostChannel;
                string t = _lostText;
                _lostText = null;
                OpenEdit(t + name);
                return;
            }
            IWowSocial soc = SocialUi.Soc(WowPlay.Session);
            if (soc != null) soc.Who(SocialUi.G("WHO_TAG_NAME", "n-") + name);   // SendWho("n-" .. name) (ItemRef.lua:61; WHO_TAG_NAME GlobalStrings.lua:8943)
        }

        /// <summary>FriendsFrame_ShowDropdown(name, 1) = the FRIEND menu (ItemRef.lua:65-66): desktop at the mouse, VR under the name.</summary>
        private void LinkMenu(LinkSet set, int k)
        {
            Line l = k >= 0 && k < set.Seq.Length ? BySeq(set.Seq[k]) : null;
            if (l == null || l.Name == null) return;
            RectTransform host;
            Vector2 at;
            if (set.Vr)
            {
                host = _vr != null ? _vr.Content : null;
                if (host == null) return;
                Vector2 p = set.Buttons[k].Rt.anchoredPosition;
                at = new Vector2(set.ClipX + p.x, set.ClipY + set.Clip.rect.height - p.y);
            }
            else
            {
                host = _desk != null ? _desk.Root : null;
                if (host == null || !UiNameMenu.MouseIn(host, out at)) return;
            }
            UiNameMenu.Open(host, at, l.Name, l.Guid, true, false, false);
        }

        // ------------------------------------------------------------------ VR window

        private void ToggleVr(Transform palm)
        {
            if (!WowPlayer.XrPresent)
            {
                if (!_typing && _desk != null && _desk.Shown) OpenEdit("");
                return;
            }
            _vrWanted = !_vrWanted;
            _vrPlaced = false;
            _vrFrom = palm;
            if (_vrWanted) Unread = 0;
            else if (_keyboard != null) _keyboard.Close();
            WowLog.Info("UI: chat window (VR) " + (_vrWanted ? "opened" : "closed") + " (palm menu).");
        }

        private void EnsureVr()
        {
            if (_vr != null) return;
            bool over = UiKit.Over;
            try
            {
                UiKit.Over = false;
                _vr = UiSurface.MakeWorld("WoW Chat Window", transform, VrW, VrH, VrMetresPerUnit, false, 103);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vr);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vr;
                Transform c = _vr.Content;
                UiBackdrop back = UiBackdrop.Make(c, "Backdrop", UiArt.TooltipBackground, UiArt.TooltipBorder, 16f, 5f, 16f, new Color(0f, 0f, 0f, 0.85f));
                back.Place(0f, 0f, VrW, VrH);
                back.SetEdgeColor(new Color(0.6f, 0.6f, 0.6f, 1f));
                UiKit.Text(c, "Title", "Chat", 14f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 14f, 6f, 200f, 24f);   // ours
                UiKit.CloseButton(c, VrW - 34f, 2f, () => { _vrWanted = false; if (_keyboard != null) _keyboard.Close(); WowLog.Info("UI: chat window (VR) closed."); });
                RectTransform clip = UiKit.Rect(c, "Clip", 14f, 32f, VrW - 60f, 196f);
                clip.gameObject.AddComponent<RectMask2D>();
                _vrText = LogText(clip, VrW - 60f, VrFontSize);
                _vrLinks = MakeLinks(clip, _vrText, true, 14f, 32f);
                UiKit.ArtButton(c, "ScrollUp", ScrollUpUp, ScrollUpDown, ScrollUpOff, null, VrW - 42f, 36f, 32f, () => Scroll(ref _vrOffset, 3));
                UiKit.ArtButton(c, "ScrollDown", ScrollDownUp, ScrollDownDown, ScrollDownOff, null, VrW - 42f, 158f, 32f, () => Scroll(ref _vrOffset, -3));
                UiKit.ArtButton(c, "ScrollToBottom", ScrollEndUp, ScrollEndDown, ScrollEndOff, null, VrW - 42f, 192f, 32f, () => { _vrOffset = 0; });

                // The mode buttons (ours): the edit box's chat type, as /s /p /g /o /y /e /r /ra /1 would set it (iteration 11: two rows,
                // proposal-social.md 6.5.4 - row A y 236: Say, Party, Guild, Officer, Yell; row B y 264: Emote, Reply, Raid, Channel).
                string[] labels = { "Say", "Party", "Guild", "Officer", "Yell", "Emote", "Reply", "Raid", "Channel" };
                _vrModes = new UiButton[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    int k = i;
                    float x = 14f + (i < 5 ? i : i - 5) * 95f, y = i < 5 ? 236f : 264f;
                    _vrModes[i] = UiKit.PanelButton(c, labels[i], x, y, 90f, 24f, () => PickMode(k));
                }
                // Row C y 292: eight quick emotes (ours) with the current target - the install's commands as labels.
                WowEmoteTable table = null;
                try { table = WowEmoteTable.Get(UiArt.Files); }
                catch (Exception) { table = null; }
                _vrEmotes = new UiButton[QuickEmotes.Length];
                for (int i = 0; i < QuickEmotes.Length; i++)
                {
                    int id = QuickEmotes[i];
                    _vrEmotes[i] = UiKit.PanelButton(c, EmoteLabel(table, id, QuickEmoteTokens[i]), 14f + i * 59f, 292f, 55f, 24f, () => QuickEmote(id));
                    if (_vrEmotes[i].Label != null) { _vrEmotes[i].Label.fontSize = Mathf.Min(_vrEmotes[i].Label.fontSize, 10f); _vrEmotes[i].Label.textWrappingMode = TextWrappingModes.NoWrap; }
                }
                // The "Type" row: the header, the text typed so far; pressing it opens the headset keyboard.
                RectTransform rowBox = UiKit.Rect(c, "Type row", 14f, 326f, VrW - 28f, 44f);
                UiKit.Solid(rowBox, "Box", new Color(0f, 0f, 0f, 0.6f), 0f, 0f, VrW - 28f, 44f);
                _vrHeader = UiKit.Text(rowBox, "Header", "Say: ", VrFontSize + 1f, CSay, TextAlignmentOptions.MidlineLeft, 8f, 0f, 170f, 44f, null, false);
                _vrHeader.textWrappingMode = TextWrappingModes.NoWrap;
                _vrHeader.richText = false;
                _vrPreview = UiKit.Text(rowBox, "Preview", "(press to type)", VrFontSize, new Color(0.7f, 0.7f, 0.7f), TextAlignmentOptions.MidlineLeft, 120f, 0f, VrW - 160f, 44f, null, false);   // ours
                _vrPreview.textWrappingMode = TextWrappingModes.NoWrap;
                _vrPreview.overflowMode = TextOverflowModes.Ellipsis;
                _vrPreview.richText = false;
                RectTransform fieldArea;
                _vrField = MakeField(rowBox, "Input", 120f, 0f, VrW - 160f, 44f, VrFontSize, out fieldArea);
                _vrField.textComponent.enabled = false;   // the preview line shows it (the field is only the keyboard's target)
                UiButton type = UiKit.Button(rowBox, "Type", 0f, 0f, VrW - 28f, 44f);
                type.Hilite = UiKit.Pic(type.transform, "Highlight", UiArt.Add(UiArt.PanelButtonHighlight), 0f, 0.625f, 0f, 0.6875f, 0f, 0f, VrW - 28f, 44f);
                type.Hilite.enabled = false;
                type.Click = OpenKeyboard;
                _vr.Finish();
                _vr.SetShown(false);
                UiVrLayout.ExtraSurfaces.Add(_vr);   // pointed at by the UI's ray / poke like the other world windows
                SetHeader();
                if (!_loggedVr)
                {
                    _loggedVr = true;
                    WowLog.Info("UI: chat window (VR) built - " + (VrW * VrMetresPerUnit).ToString("0.00") + " x " + (VrH * VrMetresPerUnit).ToString("0.00")
                                + " m, opened from the palm menu, the headset keyboard types (Game/Front/WowKeyboard).");
                }
            }
            finally
            {
                UiKit.Surface = null;
                UiKit.Over = over;
            }
        }

        private void PickMode(int k)
        {
            switch (k)
            {
                case 0: _mode = ChatKind.Say; break;
                case 1: _mode = ChatKind.Party; break;
                case 2: _mode = ChatKind.Guild; break;
                case 3: _mode = ChatKind.Officer; break;
                case 4: _mode = ChatKind.Yell; break;
                case 5: _mode = ChatKind.Emote; break;
                case 7: _mode = ChatKind.Raid; break;
                case 8:
                    {
                        // the selected channel; pressed again = the next joined channel (ours)
                        ChannelView next = NextChannel(_mode == ChatKind.Channel ? _channelTarget : 0);
                        if (next == null) return;
                        _mode = ChatKind.Channel;
                        _channelTarget = next.Number;
                        break;
                    }
                default:
                    if (string.IsNullOrEmpty(_lastTellFrom)) { AddLocal(ErrNoReplyTarget); return; }
                    _mode = ChatKind.Whisper;
                    _tellTarget = _lastTellFrom;
                    break;
            }
            // the VR buttons choose the type for the following lines too (sticky like the typed commands of a sticky type)
            if (_mode != ChatKind.Yell && _mode != ChatKind.Emote) { _sticky = _mode; _stickyTell = _tellTarget; _stickyChannel = _channelTarget; }
            SetHeader();
        }

        /// <summary>The joined channel after number (0 = the first; wraps).</summary>
        private static ChannelView NextChannel(int after)
        {
            IWowSocial soc = SocialUi.Soc(WowPlay.Session);
            if (soc == null) return null;
            List<ChannelView> l = soc.State.Channels;
            ChannelView first = null, next = null;
            for (int i = 0; i < l.Count; i++)
            {
                ChannelView c = l[i];
                if (c == null || c.Number <= 0) continue;
                if (first == null || c.Number < first.Number) first = c;
                if (c.Number > after && (next == null || c.Number < next.Number)) next = c;
            }
            return next ?? first;
        }

        private static string EmoteLabel(WowEmoteTable table, int id, string token)
        {
            string c = table != null ? table.CommandOf(id) : null;
            string s = !string.IsNullOrEmpty(c) ? c.TrimStart('/') : token.ToLowerInvariant();
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        /// <summary>A VR quick emote (ours): IWowSocial.TextEmoteById with the current target (online: CMSG_TEXT_EMOTE; offline: the local line).</summary>
        private void QuickEmote(int id)
        {
            IWowSession s = WowPlay.Session;
            IWowSocial soc = SocialUi.Soc(s);
            if (soc == null) return;
            soc.TextEmoteById(id, s.TargetGuid);
        }

        private void RefreshModeButtons()
        {
            if (_vrModes == null) return;
            int sel;
            switch (_mode)
            {
                case ChatKind.Say: sel = 0; break;
                case ChatKind.Party: sel = 1; break;
                case ChatKind.Guild: sel = 2; break;
                case ChatKind.Officer: sel = 3; break;
                case ChatKind.Yell: sel = 4; break;
                case ChatKind.Emote: sel = 5; break;
                case ChatKind.Whisper: sel = 6; break;
                case ChatKind.Raid: sel = 7; break;
                case ChatKind.Channel: sel = 8; break;
                default: sel = -1; break;
            }
            for (int i = 0; i < _vrModes.Length; i++)
            {
                UiButton b = _vrModes[i];
                if (b == null) continue;
                Color want = i == sel ? Color.white : UiArt.Gold;
                b.LabelColor = want;
                if (b.Label != null) b.Label.color = want;
            }
            if (_vrModes.Length > 8 && _vrModes[8] != null)
            {
                ChannelView c = _mode == ChatKind.Channel ? ChannelByNumber(_channelTarget) : null;
                string label = c != null ? c.Number + ". " + ShortName(c.Name) : "Channel";   // ours: "1. General"
                if (_vrModes[8].Label != null && _vrModes[8].Label.text != label) _vrModes[8].SetLabel(label);
            }
        }

        /// <summary>The VR mode buttons' states (each refresh): Reply without a tell, Guild without a guild, Officer without OfficerSpeak,
        /// Raid outside a raid, Channel with no channel joined are greyed (proposal-social.md 6.5.4).</summary>
        private void RefreshModeStates()
        {
            if (_vrModes == null || _vrModes.Length < 9) return;
            IWowSession s = _session;
            IWowSocial soc = SocialUi.Soc(s);
            GuildView g = soc != null ? soc.State.Guild : null;
            bool raid = false;
            try { raid = s != null && s.Interactions != null && s.Interactions.Raid != null && s.Interactions.Raid.IsRaid; }
            catch (Exception) { raid = false; }
            SetOn(_vrModes[6], !string.IsNullOrEmpty(_lastTellFrom));
            SetOn(_vrModes[2], g != null);
            SetOn(_vrModes[3], g != null && g.Can(GuildRights.OfficerSpeak));
            SetOn(_vrModes[7], raid);
            SetOn(_vrModes[8], soc != null && soc.State.Channels.Count > 0);
        }

        /// <summary>The CHANNEL type's channel was left (or the session changed): back to Say - ours (WoW's ChatEdit_UpdateHeader keeps the
        /// old header, ChatFrame.lua:3608-3617).</summary>
        private void CheckChannel()
        {
            bool gone = (_mode == ChatKind.Channel && ChannelByNumber(_channelTarget) == null) || (_sticky == ChatKind.Channel && ChannelByNumber(_stickyChannel) == null);
            if (!gone || (_typing && _mode == ChatKind.Channel && ChannelByNumber(_channelTarget) != null)) return;
            if (_mode == ChatKind.Channel && ChannelByNumber(_channelTarget) == null) { _mode = ChatKind.Say; _channelTarget = 0; }
            if (_sticky == ChatKind.Channel && ChannelByNumber(_stickyChannel) == null) { _sticky = ChatKind.Say; _stickyChannel = 0; }
            SetHeader();
        }

        private static void SetOn(UiButton b, bool on)
        {
            if (b != null && b.Interactable != on) b.Interactable = on;
        }

        private void OpenKeyboard()
        {
            if (_vrField == null) return;
            if (_keyboard == null) _keyboard = WowKeyboard.Create(transform);
            string title = HeaderText().TrimEnd(' ', ':');
            _keyboard.Open(_vrField, title, false, OnVrEnter);
        }

        private void OnVrEnter()
        {
            string t = _vrField != null ? _vrField.text : "";
            if (_vrField != null) _vrField.SetTextWithoutNotify("");
            Submit(t);
        }

        private void VrRefresh()
        {
            if (_vrComposed != _version || _vrComposedOffset != _vrOffset)
            {
                _vrComposed = _version;
                _vrComposedOffset = _vrOffset;
                bool fading;
                _vrText.text = Compose(_vrOffset, true, out fading);
                PlaceLinks(_vrLinks);
            }
            RefreshModeStates();
            CheckChannel();
            string typed = _vrField != null ? _vrField.text : "";
            string want = string.IsNullOrEmpty(typed) ? (VrTyping ? "" : "(press to type)") : typed;
            if (_vrPreview != null && _vrPreview.text != want)
            {
                _vrPreview.text = want;
                _vrPreview.color = string.IsNullOrEmpty(typed) ? new Color(0.7f, 0.7f, 0.7f) : ColorOf(_mode == ChatKind.Whisper ? ChatKind.WhisperSent : _mode, 0);
            }
            if (VrOpen) Unread = 0;
        }

        /// <summary>
        /// As the other palm windows (UiVrLayout.PlaceLidFree, WowTabletMap.PlaceVrWindow): above the palm menu when opened from it, else in
        /// front of the head a little down; facing the player, not nearer than 0.6 m; stays put until the player walks 1.5 m away.
        /// </summary>
        private void PlaceVr()
        {
            Camera headCam = WowPlayer.Head();
            if (headCam == null || _vr == null) return;
            Transform head = headCam.transform;
            if (_vrPlaced && (_vr.Root.position - head.position).sqrMagnitude < 1.5f * 1.5f) return;
            bool again = _vrPlaced;
            _vrPlaced = true;
            float h = VrH * VrMetresPerUnit;
            Vector3 pos;
            if (!again && _vrFrom != null && _vrFrom.gameObject.activeInHierarchy) pos = _vrFrom.position + Vector3.up * (h * 0.5f + 0.05f);
            else
            {
                Vector3 f = head.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
                pos = head.position + f * 0.7f - right * 0.15f - Vector3.up * 0.12f;
            }
            Vector3 look = pos - head.position;
            if (look.sqrMagnitude < 1e-6f) look = head.forward;
            if (look.magnitude < 0.6f) { look = look.normalized * 0.6f; pos = head.position + look; }
            _vr.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            if (_vrPlacements++ < 3)
                WowLog.Info("UI: chat window (VR) placed " + (again ? "in front of the player (walked away from it)" : _vrFrom != null ? "above the palm menu" : "in front of the player")
                            + ", " + (pos - head.position).magnitude.ToString("0.00") + " m from the head.");
        }
    }
}
