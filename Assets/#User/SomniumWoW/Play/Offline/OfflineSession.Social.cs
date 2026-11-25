using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Social;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    // Stage S1 (lead contract stub, PLAN10 s3; bodies: owner chat): chat and server groups exist only online. Offline there is no party
    // (lead 2026-09-27, the user: "No one should be in my party straight away, I should invite people") and the Somnium players talk with
    // Somnium's own voice chat (and, on desktop, the Somnium client's own Enter quick chat, which the chat panel leaves alone offline). Every call answers with one Info line (ours)
    // and logs once per kind, so a button pressed offline is never silent.
    public sealed partial class OfflineSession
    {
        private bool _chatOfflineLogged, _groupOfflineLogged;

        public void SendChat(ChatKind kind, string text, string whisperTo)
        {
            EmitInfo("Chat is on the online realm");   // ours (PLAN10 s2)
            if (!_chatOfflineLogged)
            {
                _chatOfflineLogged = true;
                WowLog.Info("Offline: chat line (" + kind + ") not sent - chat is on the online realm (offline, Somnium's voice / quick chat is the chat).");
            }
        }

        public void InviteToGroup(string characterName) { GroupOffline("invite"); }

        public void AnswerGroupInvite(bool accept) { GroupOffline(accept ? "accept" : "decline"); }

        public void LeaveGroup() { GroupOffline("leave"); }

        public void UninviteFromGroup(ulong guid) { GroupOffline("uninvite"); }

        public void PromoteToLeader(ulong guid) { GroupOffline("promote"); }

        public void SetLootMethod(int method, int threshold) { GroupOffline("loot method"); }

        public void RollOnLoot(LootRollView roll, int rollType) { GroupOffline("loot roll"); }

        private void GroupOffline(string what)
        {
            EmitInfo("Groups are on the online realm");   // ours (PLAN10 s2)
            if (!_groupOfflineLogged)
            {
                _groupOfflineLogged = true;
                WowLog.Info("Offline: group " + what + " ignored - groups are on the online realm (offline, the Somnium players here are the party).");
            }
        }

        /// <summary>Offline nobody chats: only the player's own name is known (the chat panel's own lines).</summary>
        public string ChatSender(ulong guid)
        {
            PlayerState p = Player;
            return p != null && guid != 0 && guid == p.Guid ? p.Name : null;
        }
    }
}

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    // Iteration 11 (owner social, proposal-social.md section 7): IWowSocial offline. There is no realm: no other WoW players, no guild, no
    // mailboxes in our own zones - State stays empty (the Social window shows empty lists, the Guild tab disabled as for a guildless
    // character), Mailbox / Charter stay null, and every friends / ignore / Who / channel / guild / charter / mail call answers one Info
    // line "<what> ... on the online realm" (ours, the S1 wording) + one log line per kind.
    // What works offline (ours, decision D3): text emotes - the line as the sender reads it (EmotesTextData slots 2 / 6 of the install,
    // WowEmoteTable) as a TextEmote chat line + GameEventType.TextEmote with the animation (UiSocialHost plays it on the avatar, which the
    // avatar area syncs to the other Somnium players) - and AFK / DND as flags with WoW's MARKED_* / CLEARED_* lines (nobody can whisper
    // you offline, so there is no auto reply). The offline player has no stand state: SIT / KNEEL / SLEEP print nothing and move nothing.
    public sealed partial class OfflineSession : IWowSocial
    {
        // ours (the S1 "Chat is on the online realm" wording)
        private const string SocFriendsOnline = "Friends are on the online realm";
        private const string SocWhoOnline = "Who is on the online realm";
        private const string SocChannelsOnline = "Chat channels are on the online realm";
        private const string SocGuildsOnline = "Guilds are on the online realm";
        private const string SocMailOnline = "Mail is on the online realm";
        // WoW's texts (GlobalStrings.lua of the 3.3.5a enUS install, line numbers)
        private const string SocMarkedAfk = "You are now Away.";                 // MARKED_AFK (4900)
        private const string SocMarkedAfkMessage = "You are now Away: {0}";      // MARKED_AFK_MESSAGE (4901)
        private const string SocMarkedDnd = "You are now Busy: {0}.";            // MARKED_DND (4902)
        private const string SocClearedAfk = "You are no longer Away.";          // CLEARED_AFK (1628)
        private const string SocClearedDnd = "You are no longer marked Busy.";   // CLEARED_DND (1629)
        private const string SocDefaultDnd = "Busy";                             // DEFAULT_DND_MESSAGE (1952)

        private readonly SocialState _social = new SocialState();
        private readonly HashSet<string> _socOfflineLogged = new HashSet<string>();
        private WowEmoteTable _socEmotes;

        public IWowSocial Social { get { return this; } }
        SocialState IWowSocial.State { get { return _social; } }

        private void SocOnline(string what, string text)
        {
            EmitInfo(text);
            if (_socOfflineLogged.Add(what)) WowLog.Info("Offline: " + what + " - \"" + text + "\" (ours; offline there is no realm).");
        }

        private WowEmoteTable SocEmotes()
        {
            if (_socEmotes == null)
            {
                _socEmotes = WowEmoteTable.Get(_files);
                WowLog.Info("Offline: emote table - " + _socEmotes.Count + " commands from the install" + (_socEmotes.Problem != null ? " - " + _socEmotes.Problem : ""));
            }
            return _socEmotes;
        }

        // ---- friends / ignore / Who / channels: the online realm's
        public void AddFriend(string name, string note) { SocOnline("friends", SocFriendsOnline); }
        public void RemoveFriend(ulong guid) { SocOnline("friends", SocFriendsOnline); }
        public void SetFriendNote(ulong guid, string note) { SocOnline("friends", SocFriendsOnline); }
        public void AddIgnore(string name) { SocOnline("ignore", SocFriendsOnline); }
        public void RemoveIgnore(ulong guid) { SocOnline("ignore", SocFriendsOnline); }
        public void RefreshFriends() { }
        public void Who(string query) { SocOnline("who", SocWhoOnline); }
        public void JoinChannel(string name, string password) { SocOnline("channels", SocChannelsOnline); }
        public void LeaveChannel(string name) { SocOnline("channels", SocChannelsOnline); }
        public void ListChannel(string name) { SocOnline("channels", SocChannelsOnline); }

        // ---- AFK / DND: local flags + WoW's lines (AzerothCore's rules, ChatHandler.cpp:608-656: AFK on clears DND and the other way round;
        // an empty text while on = off; a text while on = only a new reply, no line)
        public void SetAfk(string message) { SocAway(true, message); }
        public void SetDnd(string message) { SocAway(false, message); }

        private void SocAway(bool afk, string message)
        {
            string m = (message ?? "").Trim();
            bool on = afk ? _social.Afk : _social.Dnd;
            if (on)
            {
                if (m.Length > 0) return;                                        // a new auto reply only
                if (afk) _social.Afk = false; else _social.Dnd = false;
                SocAwayLine(afk ? SocClearedAfk : SocClearedDnd);
            }
            else
            {
                if (afk)
                {
                    _social.Afk = true;
                    SocAwayLine(m.Length > 0 ? string.Format(SocMarkedAfkMessage, m) : SocMarkedAfk);
                    if (_social.Dnd) { _social.Dnd = false; SocAwayLine(SocClearedDnd); }
                }
                else
                {
                    _social.Dnd = true;
                    SocAwayLine(string.Format(SocMarkedDnd, m.Length > 0 ? m : SocDefaultDnd));
                    if (_social.Afk) { _social.Afk = false; SocAwayLine(SocClearedAfk); }
                }
            }
            Emit(GameEventType.SocialChanged, 0, 0, (int)SocialPart.Status, 0, false, null);
        }

        private void SocAwayLine(string text)
        {
            Emit(GameEventType.ChatMessage, 0, 0, (int)ChatKind.System, 0, false, text);
            EmitInfo(text);
        }

        // ---- text emotes: local (the install's files are there offline too)
        public bool TextEmote(string command, ulong targetGuid)
        {
            int id = SocEmotes().Find(command);
            if (id == 0) return false;
            TextEmoteById(id, targetGuid);
            return true;
        }

        public void TextEmoteById(int emotesTextId, ulong targetGuid)
        {
            if (emotesTextId <= 0) return;
            if (_player.Dead)
            {
                if (_socOfflineLogged.Add("emotedead")) WowLog.Info("Offline: text emote while dead - nothing (like AzerothCore, ChatHandler.cpp:728).");
                return;
            }
            WowEmoteTable t = SocEmotes();
            EntityState target = targetGuid != 0 ? Find(targetGuid) : null;
            string me = _player.Name ?? "";
            string line = t.Line(emotesTextId, me, false, target != null ? target.Name : null, me);
            int anim = t.AnimOf(emotesTextId);
            if (line != null) Emit(GameEventType.ChatMessage, _player.Guid, target != null ? targetGuid : 0, (int)ChatKind.TextEmote, 0, false, line);
            if (line != null || anim != 0) Emit(GameEventType.TextEmote, _player.Guid, target != null ? targetGuid : 0, anim, emotesTextId, false, line);
            int stand = t.StandStateOf(emotesTextId);
            if (stand >= 0) StSetEmoteStand(stand);   // states fix 2026-10-04: /sit /kneel /sleep offline (OfflineSession.States.cs; was: no stand state)
        }

        // ---- guild / charter / mail: the online realm's
        public void GuildInvite(string name) { SocOnline("guild", SocGuildsOnline); }
        public void AnswerGuildInvite(bool accept) { }
        public void GuildLeave() { SocOnline("guild", SocGuildsOnline); }
        public void GuildRemove(string name) { SocOnline("guild", SocGuildsOnline); }
        public void GuildPromote(string name) { SocOnline("guild", SocGuildsOnline); }
        public void GuildDemote(string name) { SocOnline("guild", SocGuildsOnline); }
        public void GuildSetLeader(string name) { SocOnline("guild", SocGuildsOnline); }
        public void GuildDisband() { SocOnline("guild", SocGuildsOnline); }
        public void GuildSetMotd(string motd) { SocOnline("guild", SocGuildsOnline); }
        public void GuildSetInfo(string info) { SocOnline("guild", SocGuildsOnline); }
        public void GuildSetNote(string memberName, string note, bool officerNote) { SocOnline("guild", SocGuildsOnline); }
        public void GuildSetRank(int rankIndex, string name, uint rights) { SocOnline("guild", SocGuildsOnline); }
        public void GuildAddRank(string name) { SocOnline("guild", SocGuildsOnline); }
        public void GuildRemoveLastRank() { SocOnline("guild", SocGuildsOnline); }
        public void GuildRefresh() { }

        GuildCharterView IWowSocial.Charter { get { return null; } }
        public void OpenGuildRegistrar(ulong npcGuid) { SocOnline("charter", SocGuildsOnline); }
        public void BuyGuildCharter(string guildName) { SocOnline("charter", SocGuildsOnline); }
        public void ShowCharter(ulong itemGuid) { SocOnline("charter", SocGuildsOnline); }
        public void OfferCharter(ulong playerGuid) { SocOnline("charter", SocGuildsOnline); }
        public void SignCharter() { SocOnline("charter", SocGuildsOnline); }
        public void DeclineCharter() { }
        public void TurnInCharter() { SocOnline("charter", SocGuildsOnline); }
        public void RenameCharter(string guildName) { SocOnline("charter", SocGuildsOnline); }
        public void CloseCharter() { }

        MailboxView IWowSocial.Mailbox { get { return null; } }
        public void OpenMailbox(ulong mailboxGuid) { SocOnline("mail", SocMailOnline); }
        public void CloseMailbox() { }
        public void RefreshMailbox() { }
        public void ReadMail(uint mailId) { }
        public void TakeMailMoney(uint mailId) { SocOnline("mail", SocMailOnline); }
        public void TakeMailItem(uint mailId, uint itemGuidLow) { SocOnline("mail", SocMailOnline); }
        public void TakeAllFromMail(uint mailId) { SocOnline("mail", SocMailOnline); }
        public void ReturnMail(uint mailId) { SocOnline("mail", SocMailOnline); }
        public void DeleteMail(uint mailId) { SocOnline("mail", SocMailOnline); }
        public void CopyMailText(uint mailId) { SocOnline("mail", SocMailOnline); }
        public bool AttachMailItem(int bag, int slot) { SocOnline("mail", SocMailOnline); return false; }
        public void DetachMailItem(int attachmentIndex) { }
        public void SendMail(string to, string subject, string body, int money, int cod) { SocOnline("mail", SocMailOnline); }
    }
}
