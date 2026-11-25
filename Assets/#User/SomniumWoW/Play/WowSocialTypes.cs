// iteration 11 - owner social (wave-0 CONTRACT, w0play: P/harness/Probes/D11/social/SocialContractDraft.cs verbatim minus its 3-line header).
// FROZEN after wave 0: changes go to the integrator.
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // ---- Iteration 11 (owner social): friends, ignore, Who, guild, channels, text emotes, AFK / DND, mail, guild charters ----------

    /// <summary>What changed (GameEvent.Amount of GameEventType.SocialChanged; flags). Views may also just poll the Counter fields.</summary>
    public enum SocialPart { None = 0, Friends = 1, Ignore = 2, Who = 4, Guild = 8, Channels = 16, Mail = 32, Charter = 64, Status = 128 }

    /// <summary>
    /// CONTRACT (shared, pure C#): the social side of one running game - IWowSession.Social. Online = the AzerothCore realm
    /// (OnlineSession.Social / .Guild / .Mail .cs); offline every action answers one Info line ("... is on the online realm", ours).
    /// Every member is called from Unity's main thread. Owners may ADD members; do not rename or remove.
    /// Names are character names as typed (the server normalizes them); texts are cut to WoW's client limits by the session.
    /// </summary>
    public interface IWowSocial
    {
        /// <summary>Lists and flags the views draw (never null; the lists are updated in place).</summary>
        SocialState State { get; }

        // ---- friends / ignore (FriendsFrame Friends + Ignore tabs, /friend /removefriend /ignore /unignore)
        /// <summary>CMSG_ADD_FRIEND (name, note). AC answers SMSG_FRIEND_STATUS, or nothing for an unknown name ("Player not found." after 3 s, ours).</summary>
        void AddFriend(string name, string note);
        /// <summary>CMSG_DEL_FRIEND (guid).</summary>
        void RemoveFriend(ulong guid);
        /// <summary>CMSG_SET_CONTACT_NOTES (guid, note &lt;= 48 letters).</summary>
        void SetFriendNote(ulong guid, string note);
        /// <summary>CMSG_ADD_IGNORE (name).</summary>
        void AddIgnore(string name);
        /// <summary>CMSG_DEL_IGNORE (guid).</summary>
        void RemoveIgnore(ulong guid);
        /// <summary>CMSG_CONTACT_LIST (1): fresh online states / zones / levels (the Friends tab asks every 30 s while shown, ours).</summary>
        void RefreshFriends();

        // ---- Who (Who tab, /who)
        /// <summary>CMSG_WHO from WoW's who text ("goldshire", "10-20", n-"Name" g-"Guild" z-"Zone" r-"Race" c-"Class"; empty = everyone).</summary>
        void Who(string query);

        // ---- AFK / DND (/afk /dnd)
        /// <summary>CMSG_MESSAGECHAT CHAT_MSG_AFK: toggles like /afk (null or "" while away = back; a text = the auto reply).</summary>
        void SetAfk(string message);
        /// <summary>CMSG_MESSAGECHAT CHAT_MSG_DND, like /dnd.</summary>
        void SetDnd(string message);

        // ---- text emotes (/wave ... - the install's EMOTE*_CMD* commands)
        /// <summary>A typed emote command ("/wave", case-insensitive). False = not an emote command of the install. targetGuid 0 = none.</summary>
        bool TextEmote(string command, ulong targetGuid);
        /// <summary>CMSG_TEXT_EMOTE by EmotesText.dbc id (the VR quick emotes).</summary>
        void TextEmoteById(int emotesTextId, ulong targetGuid);

        // ---- chat channels (Chat tab, /join /leave /chatlist; talking = IWowSession.SendChat(ChatKind.Channel, text, channel name or "1".."10"))
        void JoinChannel(string name, string password);
        /// <summary>CMSG_LEAVE_CHANNEL; name may be the channel number "1".."10".</summary>
        void LeaveChannel(string name);
        /// <summary>CMSG_CHANNEL_LIST -> ChannelView.Members.</summary>
        void ListChannel(string name);

        // ---- guild (Guild tab, /g* commands)
        void GuildInvite(string name);
        /// <summary>Answers State.PendingGuildInviteFrom: CMSG_GUILD_ACCEPT / CMSG_GUILD_DECLINE.</summary>
        void AnswerGuildInvite(bool accept);
        void GuildLeave();
        void GuildRemove(string name);
        void GuildPromote(string name);
        void GuildDemote(string name);
        void GuildSetLeader(string name);
        void GuildDisband();
        void GuildSetMotd(string motd);
        void GuildSetInfo(string info);
        void GuildSetNote(string memberName, string note, bool officerNote);
        /// <summary>CMSG_GUILD_RANK for rank index (0 = Guild Master) with a new name and rights; the bank values are echoed unchanged.</summary>
        void GuildSetRank(int rankIndex, string name, uint rights);
        void GuildAddRank(string name);
        /// <summary>CMSG_GUILD_DEL_RANK (always the lowest rank).</summary>
        void GuildRemoveLastRank();
        /// <summary>CMSG_GUILD_ROSTER + CMSG_GUILD_INFO (throttled: at most every 2 s, ours).</summary>
        void GuildRefresh();

        // ---- guild charter (guild creation)
        /// <summary>The registrar window of a guild master NPC (GuildRegistrarFrame) or a petition window (PetitionFrame); null = none.
        /// Opened by the server's answers (SMSG_PETITION_SHOWLIST / SMSG_PETITION_SHOW_SIGNATURES).</summary>
        GuildCharterView Charter { get; }
        /// <summary>CMSG_PETITION_SHOWLIST (uint64 npc). Registered with the npc area's RegisterNpcService(UNIT_NPC_FLAG_PETITIONER) for
        /// petitioners without gossip; guild masters (gossip) reach the same answer through their gossip option.</summary>
        void OpenGuildRegistrar(ulong npcGuid);
        /// <summary>CMSG_PETITION_BUY at the open registrar (name &lt;= 24 letters, GuildRegistrarFrame.xml EditBox letters="24").</summary>
        void BuyGuildCharter(string guildName);
        /// <summary>The charter item was used (Items.cs UseItem, ITEM_FLAG_PETITION): CMSG_PETITION_SHOW_SIGNATURES + CMSG_PETITION_QUERY.</summary>
        void ShowCharter(ulong itemGuid);
        /// <summary>CMSG_OFFER_PETITION of the open own charter to a player.</summary>
        void OfferCharter(ulong playerGuid);
        /// <summary>CMSG_PETITION_SIGN for a charter someone offered.</summary>
        void SignCharter();
        /// <summary>MSG_PETITION_DECLINE for a charter someone offered.</summary>
        void DeclineCharter();
        /// <summary>The registrar's "Register a Guild Charter" (GuildRegistrarFrame.xml TurnInGuildCharter): CMSG_TURN_IN_PETITION with the
        /// guild charter item (entry 5863) of the bags; none -> Error ERR_NO_GUILD_CHARTER "You don't have a guild charter." (GS:3325).</summary>
        void TurnInCharter();
        /// <summary>The petition window's "Rename Guild" (own charter): MSG_PETITION_RENAME (uint64 petition item guid, CString name &lt;= 24,
        /// PetitionsHandler.cpp:335-343); the server answers the same opcode with the new name (:393-396) or a guild command result.</summary>
        void RenameCharter(string guildName);
        void CloseCharter();

        // ---- mail
        /// <summary>The open mailbox (inbox + the send draft); null = closed.</summary>
        MailboxView Mailbox { get; }
        /// <summary>A mailbox gameobject (type 19, Quests.cs QUseGameObject) or a mailbox creature (npc's RegisterNpcService(0x04000000)) was used:
        /// CMSG_GET_MAIL_LIST, Mailbox opens.
        /// (AzerothCore's data has no creature with UNIT_NPC_FLAG_MAILBOX: census of creature_template.sql, 0 of 29947 rows.)</summary>
        void OpenMailbox(ulong mailboxGuid);
        void CloseMailbox();
        void RefreshMailbox();
        /// <summary>A mail was opened: CMSG_MAIL_MARK_AS_READ when unread.</summary>
        void ReadMail(uint mailId);
        void TakeMailMoney(uint mailId);
        /// <summary>CMSG_MAIL_TAKE_ITEM (a C.O.D. mail charges its C.O.D. - the UI asks first).</summary>
        void TakeMailItem(uint mailId, uint itemGuidLow);
        /// <summary>WoW's Shift-click on an inbox row (AutoLootMailItem, MailFrame.lua:274-279; never for a C.O.D. mail): the money, then every
        /// item, one request after the previous answer (ours: no burst).</summary>
        void TakeAllFromMail(uint mailId);
        void ReturnMail(uint mailId);
        void DeleteMail(uint mailId);
        /// <summary>CMSG_MAIL_CREATE_TEXT_ITEM: a permanent copy of the letter in the bags.</summary>
        void CopyMailText(uint mailId);
        /// <summary>Attaches the item at WoW's inventory position (bag, slot) - Player.h:657-724 numbering (npc's InvSlot = uicore's InvPos;
        /// ItemView.Bag + Slot): 255 + 23..38 the backpack,
        /// 19..22 + slot a bag) - to the send draft; false + an Error event when not possible (12 already, a non-empty bag, soulbound, the
        /// same item twice, no mailbox open).</summary>
        bool AttachMailItem(int bag, int slot);
        void DetachMailItem(int attachmentIndex);
        /// <summary>CMSG_SEND_MAIL with the draft's attachments; money XOR cod (copper). The answer is an Info / Error event and Mailbox.LastSendOk.</summary>
        void SendMail(string to, string subject, string body, int money, int cod);
    }

    /// <summary>Everything the social views draw. Lists are server order unless noted; counters +1 on every change.</summary>
    public sealed class SocialState
    {
        public readonly List<FriendView> Friends = new List<FriendView>();
        public readonly List<IgnoreView> Ignored = new List<IgnoreView>();
        public int FriendsCounter;

        public readonly List<WhoView> Who = new List<WhoView>();
        public int WhoTotal;                  // matches on the server (SMSG_WHO second count); Who.Count = shown (<= 49 by the realm's config)
        public int WhoCounter;
        public string WhoQuery;               // the text of the last request

        public GuildView Guild;               // null = not in a guild (PLAYER_GUILDID 0)
        public int GuildCounter;
        public string PendingGuildInviteFrom; // SMSG_GUILD_INVITE waiting for AnswerGuildInvite (inviter name), null = none
        public string PendingGuildInviteGuild;
        public float PendingGuildInviteSince; // session seconds (the 60 s popup timeout of StaticPopup GUILD_INVITE)

        public readonly List<ChannelView> Channels = new List<ChannelView>();   // by Number
        public int ChannelsCounter;
        public int ZoneId;                    // SMSG_INIT_WORLD_STATES (AreaTable zone id), 0 = not known yet

        public bool Afk, Dnd;                 // PLAYER_FLAGS 0x2 / 0x4 of the player's own object

        public bool HasUnreadMail;            // the new-mail icon
        public readonly List<string> UnreadMailFrom = new List<string>();   // MSG_QUERY_NEXT_MAIL_TIME senders (<= 2)
        public int MailCounter;
    }

    public sealed class FriendView
    {
        public ulong Guid;
        public string Name;                   // null until the name query answers
        public string Note = "";
        public bool Online, Afk, Dnd;         // SMSG_CONTACT_LIST / SMSG_FRIEND_STATUS status 1 / 2 / 4
        public int Level;
        public WowClass Class;
        public int ZoneId;                    // AreaTable zone id, 0 = offline / unknown
        public string ZoneName;               // AreaTable AreaName_lang of the install
    }

    public sealed class IgnoreView
    {
        public ulong Guid;
        public string Name;
    }

    public sealed class WhoView
    {
        public string Name, GuildName;
        public int Level;
        public WowClass Class;
        public int Race, Gender;
        public int ZoneId;
        public string ZoneName, RaceName, ClassName;   // from the install's AreaTable / ChrRaces / ChrClasses
    }

    /// <summary>Rank rights (AzerothCore Guild.h:74-94 GuildRankRights). Test with GuildView.Can (all bits of the value).</summary>
    public static class GuildRights
    {
        public const uint ChatListen = 0x41, ChatSpeak = 0x42, OfficerListen = 0x44, OfficerSpeak = 0x48, Invite = 0x50, Remove = 0x60,
            Promote = 0xC0, Demote = 0x140, SetMotd = 0x1040, EditPublicNote = 0x2040, ViewOfficerNote = 0x4040, EditOfficerNote = 0x8040,
            ModifyInfo = 0x10040, All = 0x1DF1FF;
        /// <summary>GR_RIGHT_EMPTY: part of every right above AND of every rights value the client sends (Guild.h:76). AzerothCore tests
        /// `(rankRights &amp; right) != GR_RIGHT_EMPTY` (Guild.h:819-825): a rank value WITHOUT 0x40 would pass every test - so CMSG_GUILD_RANK
        /// always carries Empty | the checked bits.</summary>
        public const uint Empty = 0x40;
        /// <summary>Guild Control's checkboxes 1..13 (GuildControlPopupFrameCheckbox&lt;i&gt;, labels GUILDCONTROL_OPTION&lt;i&gt; GlobalStrings.lua
        /// 3887-3905; the rights by their names, Guild.h:77-89): 1 guild chat listen ... 13 modify guild info. 14 is deprecated (FriendsFrame.lua
        /// :1789), 15-17 are guild bank rights (gap G3, echoed unchanged).</summary>
        public static readonly uint[] ControlBits = { 0, 0x1, 0x2, 0x4, 0x8, 0x80, 0x100, 0x10, 0x20, 0x1000, 0x2000, 0x4000, 0x8000, 0x10000 };
    }

    public sealed class GuildRankView
    {
        public string Name = "";
        public uint Rights;
        public uint GoldPerDay;
        public readonly uint[] TabRights = new uint[6], TabSlotsPerDay = new uint[6];   // echoed in CMSG_GUILD_RANK (guild bank: out of scope)
    }

    public sealed class GuildMemberView
    {
        public ulong Guid;
        public string Name;
        public int Rank;                      // index into GuildView.Ranks (0 = Guild Master)
        public int Level;
        public WowClass Class;
        public int Gender;
        public int ZoneId;
        public string ZoneName;
        public bool Online, Afk, Dnd;
        public float DaysOffline;             // offline members only (SMSG_GUILD_ROSTER float)
        public string Note = "", OfficerNote = "";   // OfficerNote empty unless the player's rank may view it
    }

    public sealed class GuildView
    {
        public int Id;
        public string Name = "";
        public string Motd = "", Info = "";
        public int MyRank;                    // PLAYER_GUILDRANK
        public readonly List<GuildRankView> Ranks = new List<GuildRankView>();
        public readonly List<GuildMemberView> Members = new List<GuildMemberView>();
        public int OnlineCount;
        public int NumAccounts;               // SMSG_GUILD_INFO
        public int CreatedYear, CreatedMonth, CreatedDay;   // SMSG_GUILD_INFO packed date, 0 = not asked yet
        public int EmblemStyle, EmblemColor, BorderStyle, BorderColor, BackgroundColor;
        public bool RosterKnown;              // the first SMSG_GUILD_ROSTER arrived

        /// <summary>AzerothCore's own test (Guild.h:819-825 _HasRankRight): (rights &amp; right) != GR_RIGHT_EMPTY.</summary>
        public bool Can(uint right)
        {
            if (MyRank < 0 || MyRank >= Ranks.Count) return false;
            return (Ranks[MyRank].Rights & right) != GuildRights.Empty;
        }

        public string RankName(int rank) { return rank >= 0 && rank < Ranks.Count ? Ranks[rank].Name : ""; }
    }

    public sealed class ChannelView
    {
        public int Number;                    // 1..10: General 1, Trade 2, LocalDefense 3, LookingForGroup 4 always; others the lowest free from 5
        public string Name;                   // the server's full name, e.g. "General - Elwynn Forest"
        public int ChannelId;                 // ChatChannels.dbc id, 0 = custom
        public int Flags;                     // the YOU_JOINED channel flags (Channel.h ChannelFlags)
        public readonly List<string> Members = new List<string>();   // the last CMSG_CHANNEL_LIST answer (names by name query)
        public int MembersCounter;
        public string Display { get { return Number + ". " + Name; } }
    }

    public sealed class GuildCharterView
    {
        public bool Registrar;                // true = the purchase window (SMSG_PETITION_SHOWLIST); false = a petition (signatures)
        public ulong NpcGuid;
        public int CharterEntry, Cost, RequiredSignatures;   // registrar
        public ulong PetitionGuid, OwnerGuid;
        public uint PetitionId;
        public string GuildName = "", OwnerName;
        public bool Mine;                     // the player's own charter (Request Signature / Turn In) - else offered to us (Sign / Decline)
        public readonly List<string> Signers = new List<string>();
        public int Needed;                    // SMSG_PETITION_QUERY_RESPONSE "needed" (the realm's MinPetitionSigns)
        public int Counter;
    }

    public sealed class MailItemView
    {
        public int Index;                     // the attachment slot in the mail
        public uint ItemGuidLow;              // CMSG_MAIL_TAKE_ITEM id
        public int Entry, Count, Charges, Durability, MaxDurability, RandomPropertyId;
        public int SuffixFactor;              // item tooltip fix: the random suffix's factor
        public readonly int[] Enchant = new int[7];   // enchantment ids, slots 0..6 (permanent, temporary, 3 gems, bonus, prismatic)
        public ItemView Item;                 // name / icon / quality / tooltip (Name null until the item query answers)
    }

    public sealed class MailView
    {
        public uint Id;
        public int MessageType;               // 0 player, 2 auction, 3 creature, 4 gameobject, 5 calendar (Mail.h:35-42)
        public ulong SenderGuid;              // players
        public int SenderEntry;               // creature / gameobject entry, auction house id
        public string Sender;                 // name (null until known -> "Unknown")
        public string Subject = "", Body = "";
        public int Money, Cod;                // copper
        public int Flags;                     // MAIL_CHECK_MASK_* (READ 1, RETURNED 2, COPIED 4, COD_PAYMENT 8, HAS_BODY 0x10)
        public float DaysLeft;
        public int Stationery, TemplateId;
        public readonly List<MailItemView> Items = new List<MailItemView>();
        public bool Read { get { return (Flags & 1) != 0; } }
        public bool Returned { get { return (Flags & 2) != 0; } }
        public bool Copied { get { return (Flags & 4) != 0; } }
        public bool IsGm { get { return Stationery == 61; } }
        /// <summary>Ours (WoW's InboxItemCanDelete lives in Wow.exe): Return for a player's mail that still carries items or money and was
        /// not returned already; Delete otherwise (AC refuses to delete a C.O.D. mail).</summary>
        public bool CanReturn { get { return MessageType == 0 && !Returned && (Items.Count > 0 || Money > 0); } }
        public bool CanReply { get { return MessageType == 0 && SenderGuid != 0; } }
    }

    public sealed class MailboxView
    {
        public ulong MailboxGuid;
        public readonly List<MailView> Mails = new List<MailView>();   // server order (newest first on AzerothCore)
        public int TotalOnServer;             // SMSG_MAIL_LIST_RESULT realCount (more than Mails.Count = "Your inbox is full.")
        public bool Waiting;                  // a list request is in flight
        public readonly List<ItemView> Attachments = new List<ItemView>();   // the send draft (<= 12): the bag items' own views (ItemView.Bag / Slot = where they are)
        public bool? LastSendOk;              // null = nothing sent yet / in flight
        public int Counter;
        public const int PostagePerItem = 30; // copper, MailHandler.cpp:162 "price hardcoded in client"
        public int Postage { get { return PostagePerItem * System.Math.Max(1, Attachments.Count); } }
    }
}
