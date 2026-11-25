using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Social
{
    /// <summary>
    /// Iteration 11 (owner social, proposal-social.md section 3): the readers of the server's social packets, pure functions over WowPacket
    /// (the harness round-trips them against AzerothCore-layout writers). Layouts: AzerothCore master 950684036946011c3d597382b3cfb5de99f7adbc,
    /// file:line in each comment (copies in P/acsrc11/social).
    /// </summary>
    public static class SocialRead
    {
        // ---------------------------------------------------------------- guild

        /// <summary>SMSG_GUILD_QUERY_RESPONSE (GuildPackets.cpp:28-43): uint32 id; CString name; 10 x CString rank name (the whole
        /// std::array, Guild.h:46); uint32 emblemStyle, emblemColor, borderStyle, borderColor, backgroundColor; uint32 rankCount.</summary>
        public static int GuildQuery(WowPacket p, out string name, string[] rankNames, int[] emblem, out int rankCount)
        {
            int id = (int)p.U32();
            name = p.CString();
            for (int i = 0; i < 10; i++) { string r = p.CString(); if (rankNames != null && i < rankNames.Length) rankNames[i] = r; }
            for (int i = 0; i < 5; i++) { int v = (int)p.U32(); if (emblem != null && i < emblem.Length) emblem[i] = v; }
            rankCount = p.Remaining >= 4 ? (int)p.U32() : 10;
            if (rankCount < 0 || rankCount > 10) rankCount = 10;
            return id;
        }

        /// <summary>SMSG_GUILD_ROSTER (GuildPackets.cpp:60-120, built by Guild.cpp:1211-1256): uint32 members; CString MOTD; CString info;
        /// uint32 ranks; ranks x { uint32 rights; uint32 goldPerDay; 6 x (uint32 tabRights, uint32 tabSlots) }; members x { uint64 guid;
        /// uint8 status (ONLINE 1 / AFK 2 / DND 4, Guild.h:230-234); CString name; int32 rank; uint8 level; uint8 class; uint8 gender;
        /// int32 zone; [float daysOffline only when status == 0]; CString note; CString officerNote }.</summary>
        public static void GuildRoster(WowPacket p, GuildView g)
        {
            uint members = p.U32();
            g.Motd = p.CString();
            g.Info = p.CString();
            uint ranks = p.U32();
            if (ranks > 10 || members > 5000) throw new FormatException("roster counts " + ranks + " ranks / " + members + " members");
            while (g.Ranks.Count < ranks) g.Ranks.Add(new GuildRankView());
            while (g.Ranks.Count > ranks) g.Ranks.RemoveAt(g.Ranks.Count - 1);
            for (int r = 0; r < ranks; r++)
            {
                GuildRankView rv = g.Ranks[r];
                rv.Rights = p.U32();
                rv.GoldPerDay = p.U32();
                for (int t = 0; t < 6; t++) { rv.TabRights[t] = p.U32(); rv.TabSlotsPerDay[t] = p.U32(); }
            }
            g.Members.Clear();
            g.OnlineCount = 0;
            for (int i = 0; i < members; i++)
            {
                var m = new GuildMemberView();
                m.Guid = p.U64();
                int status = p.U8();
                m.Name = p.CString();
                m.Rank = p.I32();
                m.Level = p.U8();
                m.Class = (WowClass)p.U8();
                m.Gender = p.U8();
                m.ZoneId = p.I32();
                if (status == 0) m.DaysOffline = p.F32();             // GuildPackets.cpp:113-114: only for offline members
                m.Note = p.CString();
                m.OfficerNote = p.CString();
                m.Online = (status & 1) != 0; m.Afk = (status & 2) != 0; m.Dnd = (status & 4) != 0;
                if (m.Online) g.OnlineCount++;
                g.Members.Add(m);
            }
            g.RosterKnown = true;
        }

        /// <summary>SMSG_GUILD_EVENT (GuildPackets.cpp:122-142): uint8 type; uint8 count; count x CString; [uint64 guid for GE_JOINED 3,
        /// GE_LEFT 4, GE_SIGNED_ON 12, GE_SIGNED_OFF 13].</summary>
        public static int GuildEvent(WowPacket p, List<string> args, out ulong guid)
        {
            int type = p.U8();
            int n = p.U8();
            args.Clear();
            for (int i = 0; i < n; i++) args.Add(p.CString());
            guid = 0;
            if ((type == 3 || type == 4 || type == 12 || type == 13) && p.Remaining >= 8) guid = p.U64();
            return type;
        }

        /// <summary>SMSG_GUILD_COMMAND_RESULT (GuildPackets.cpp:81-88): int32 command; CString name; int32 result.</summary>
        public static void GuildCommandResult(WowPacket p, out int command, out string name, out int result)
        {
            command = p.I32(); name = p.CString(); result = p.I32();
        }

        /// <summary>SMSG_GUILD_INFO (GuildPackets.cpp:50-58): CString name; uint32 packed date (ByteBuffer::AppendPackedTime: minute 0-5,
        /// hour 6-10, weekday 11-13, day-1 14-19, month-1 20-23, year-2000 24-28); int32 members; int32 accounts.</summary>
        public static void GuildInfo(WowPacket p, GuildView g, out int members)
        {
            p.CString();
            uint packed = p.U32();
            g.CreatedYear = 2000 + (int)((packed >> 24) & 0x1F);
            g.CreatedMonth = 1 + (int)((packed >> 20) & 0xF);
            g.CreatedDay = 1 + (int)((packed >> 14) & 0x3F);
            members = p.I32();
            g.NumAccounts = p.I32();
        }

        // ---------------------------------------------------------------- friends / who

        /// <summary>SMSG_CONTACT_LIST (SocialMgr.cpp:125-178): uint32 flags; uint32 count; count x { uint64 guid; uint32 contactFlags;
        /// CString note; [friends: uint8 status; [status != 0: uint32 area, uint32 level, uint32 class]] }. The lists of the flags are replaced.</summary>
        public static void ContactList(WowPacket p, List<FriendView> friends, List<IgnoreView> ignored, out uint flags)
        {
            flags = p.U32();
            uint count = p.U32();
            if ((flags & 1) != 0) friends.Clear();
            if ((flags & 2) != 0) ignored.Clear();
            for (int i = 0; i < count; i++)
            {
                ulong guid = p.U64();
                uint cflags = p.U32();
                string note = p.CString();
                if ((cflags & 1) != 0)
                {
                    var f = new FriendView { Guid = guid, Note = note ?? "" };
                    int status = p.U8();
                    if (status != 0) { f.ZoneId = (int)p.U32(); f.Level = (int)p.U32(); f.Class = (WowClass)p.U32(); }
                    f.Online = status != 0; f.Afk = (status & 2) != 0; f.Dnd = (status & 4) != 0;
                    friends.Add(f);
                }
                if ((cflags & 2) != 0) ignored.Add(new IgnoreView { Guid = guid });
            }
        }

        /// <summary>SMSG_FRIEND_STATUS (SocialMgr.cpp:263-298): uint8 result; uint64 guid; [CString note: ADDED_ONLINE 6 / ADDED_OFFLINE 7];
        /// [uint8 status; uint32 area; uint32 level; uint32 class: ADDED_ONLINE 6 / ONLINE 2].</summary>
        public static int FriendStatus(WowPacket p, out ulong guid, out string note, out int status, out int area, out int level, out int cls)
        {
            int result = p.U8();
            guid = p.U64();
            note = null; status = 0; area = 0; level = 0; cls = 0;
            if (result == 6 || result == 7) note = p.CString();
            if (result == 6 || result == 2) { status = p.U8(); area = (int)p.U32(); level = (int)p.U32(); cls = (int)p.U32(); }
            return result;
        }

        /// <summary>SMSG_WHO (MiscHandler.cpp:290-412): uint32 shown (written first, :409), uint32 matches; shown x { CString name; CString
        /// guild; uint32 level; uint32 class; uint32 race; uint8 gender; uint32 zone }.</summary>
        public static void Who(WowPacket p, List<WhoView> list, out int total)
        {
            uint shown = p.U32();
            total = (int)p.U32();
            list.Clear();
            for (int i = 0; i < shown; i++)
            {
                var w = new WhoView();
                w.Name = p.CString(); w.GuildName = p.CString();
                w.Level = (int)p.U32(); w.Class = (WowClass)p.U32(); w.Race = (int)p.U32(); w.Gender = p.U8(); w.ZoneId = (int)p.U32();
                list.Add(w);
            }
        }

        // ---------------------------------------------------------------- channels / emotes

        /// <summary>SMSG_CHANNEL_NOTIFY (Channel.cpp:952-1110): uint8 type; CString channel; then the type's tail - JOINED 0x00 / LEFT 0x01:
        /// guid; YOU_JOINED 0x02: uint8 flags, uint32 channelId, uint32 0; YOU_LEFT 0x03: uint32 channelId, uint8 isConstant; one guid:
        /// PASSWORD_CHANGED 0x07, OWNER_CHANGED 0x08, ANNOUNCEMENTS_ON/OFF 0x0D/0x0E, ALREADY_MEMBER 0x17, INVITE 0x18, VOICE_ON/OFF 0x22/0x23;
        /// a name: PLAYER_NOT_FOUND 0x09, CHANNEL_OWNER 0x0B, NOT_BANNED 0x16, PLAYER_INVITED 0x1D, INVITE_BANNED 0x1E; MODE_CHANGE 0x0C: guid,
        /// uint8 old, uint8 new; KICKED 0x12 / BANNED 0x14 / UNBANNED 0x15: two guids (bad, good); every other type: no tail.</summary>
        public static int ChannelNotify(WowPacket p, out string channel, out ulong guid1, out ulong guid2, out int flags, out int channelId, out string name)
        {
            int type = p.U8();
            channel = p.CString();
            guid1 = guid2 = 0; flags = 0; channelId = 0; name = null;
            switch (type)
            {
                case 0x00: case 0x01: guid1 = p.U64(); break;
                case 0x02: flags = p.U8(); channelId = (int)p.U32(); if (p.Remaining >= 4) p.U32(); break;
                case 0x03: channelId = (int)p.U32(); flags = p.Remaining >= 1 ? p.U8() : 0; break;   // flags = isConstant here
                case 0x07: case 0x08: case 0x0D: case 0x0E: case 0x0F: case 0x10: case 0x17: case 0x18: case 0x22: case 0x23:
                    if (p.Remaining >= 8) guid1 = p.U64();
                    break;
                case 0x09: case 0x0B: case 0x16: case 0x1D: case 0x1E: name = p.CString(); break;
                case 0x0C: guid1 = p.U64(); if (p.Remaining >= 2) { p.U8(); flags = p.U8(); } break;
                case 0x12: case 0x14: case 0x15: guid1 = p.U64(); guid2 = p.U64(); break;
                default: break;
            }
            return type;
        }

        /// <summary>SMSG_CHANNEL_LIST (Channel.cpp:689-728): uint8 1; CString name; uint8 channelFlags; uint32 count; count x (uint64 guid,
        /// uint8 memberFlags).</summary>
        public static string ChannelList(WowPacket p, List<ulong> members)
        {
            p.U8();
            string name = p.CString();
            p.U8();
            uint n = p.U32();
            members.Clear();
            for (int i = 0; i < n && p.Remaining >= 9; i++) { members.Add(p.U64()); p.U8(); }
            return name;
        }

        /// <summary>SMSG_TEXT_EMOTE (ChatHandler.cpp:690-704 EmoteChatBuilder): uint64 sender; uint32 textEmote; uint32 emoteNum; uint32
        /// nameLength (WITHOUT the NUL); then the name + NUL when nameLength &gt; 1, else a single 0 byte - one CString either way.</summary>
        public static void TextEmote(WowPacket p, out ulong sender, out int textEmote, out string targetName)
        {
            sender = p.U64();
            textEmote = (int)p.U32();
            p.U32();
            p.U32();
            targetName = p.Remaining > 0 ? p.CString() : "";
        }

        /// <summary>SMSG_EMOTE (ChatPackets.cpp:20-26): uint32 Emotes.dbc id; uint64 guid.</summary>
        public static void Emote(WowPacket p, out int emoteId, out ulong guid) { emoteId = (int)p.U32(); guid = p.U64(); }

        /// <summary>SMSG_INIT_WORLD_STATES (WorldStatePackets.cpp:22-36): int32 map; int32 zone; int32 area; ... - the zone (peek).</summary>
        public static int ZoneOfWorldStates(WowPacket p) { p.I32(); return p.I32(); }

        // ---------------------------------------------------------------- mail

        /// <summary>SMSG_MAIL_LIST_RESULT (MailHandler.cpp:698-821): uint32 realCount; uint8 shown; shown x { uint16 size (incl. itself);
        /// uint32 id; uint8 type; [type 0: uint64 sender | else uint32 senderEntry]; uint32 cod; uint32 0; uint32 stationery; uint32 money;
        /// uint32 flags; float daysLeft; uint32 templateId; CString subject; CString body; uint8 items; items x { uint8 index; uint32 itemLow;
        /// uint32 entry; 7 x (uint32 enchant, duration, charges); int32 randomProperty; uint32 suffixFactor; uint32 count; uint32 charges;
        /// uint32 maxDurability; uint32 durability; uint8 0 } }. Each entry ends where its declared size says (the reader skips odd tails).</summary>
        public static void MailList(WowPacket p, MailboxView box)
        {
            box.TotalOnServer = (int)p.U32();
            int shown = p.U8();
            box.Mails.Clear();
            for (int i = 0; i < shown; i++)
            {
                int start = p.Pos;
                int size = p.U16();
                var m = new MailView();
                m.Id = p.U32();
                m.MessageType = p.U8();
                if (m.MessageType == 0) m.SenderGuid = p.U64(); else m.SenderEntry = (int)p.U32();
                m.Cod = (int)p.U32();
                p.U32();
                m.Stationery = (int)p.U32();
                m.Money = (int)p.U32();
                m.Flags = (int)p.U32();
                m.DaysLeft = p.F32();
                m.TemplateId = (int)p.U32();
                m.Subject = p.CString();
                m.Body = p.CString();
                int items = p.U8();
                for (int k = 0; k < items; k++)
                {
                    var it = new MailItemView();
                    it.Index = p.U8();
                    it.ItemGuidLow = p.U32();
                    it.Entry = (int)p.U32();
                    for (int e = 0; e < 7; e++) { it.Enchant[e] = (int)p.U32(); p.U32(); p.U32(); }   // MAX_INSPECTED_ENCHANTMENT_SLOT 7 (Item.h:176): id, duration, charges
                    it.RandomPropertyId = p.I32();
                    it.SuffixFactor = (int)p.U32();
                    it.Count = (int)p.U32();
                    it.Charges = (int)p.U32();
                    it.MaxDurability = (int)p.U32();
                    it.Durability = (int)p.U32();
                    p.U8();
                    m.Items.Add(it);
                }
                if (size > 0 && start + size <= p.Data.Length) p.Pos = start + size;
                box.Mails.Add(m);
            }
            box.Waiting = false;
            box.Counter++;
        }

        /// <summary>SMSG_SEND_MAIL_RESULT (Player.cpp:2955-2969): uint32 mailId; uint32 action; uint32 result; [uint32 equipError when
        /// result == 1] else [uint32 itemLow; uint32 count when action == ITEM_TAKEN 2].</summary>
        public static void MailResult(WowPacket p, out uint mailId, out int action, out int result, out int equipError, out uint itemGuidLow, out int count)
        {
            mailId = p.U32(); action = (int)p.U32(); result = (int)p.U32();
            equipError = 0; itemGuidLow = 0; count = 0;
            if (result == 1) { if (p.Remaining >= 4) equipError = (int)p.U32(); }
            else if (action == 2 && p.Remaining >= 8) { itemGuidLow = p.U32(); count = (int)p.U32(); }
        }

        /// <summary>MSG_QUERY_NEXT_MAIL_TIME answer (MailHandler.cpp:891-938): float (0 = unread mail, -DAY = none); uint32 count (&lt;= 2);
        /// count x { uint64 player sender | 0; uint32 senderEntry (non-player); uint32 messageType; uint32 stationery; float delay }.</summary>
        public static bool NextMailTime(WowPacket p, List<ulong> senders, List<int> entries, List<int> types)
        {
            float f = p.F32();
            uint count = p.U32();
            senders.Clear(); entries.Clear(); types.Clear();
            for (int i = 0; i < count && p.Remaining >= 24; i++)
            {
                senders.Add(p.U64()); entries.Add((int)p.U32()); types.Add((int)p.U32()); p.U32(); p.F32();
            }
            return f >= 0f;
        }

        // ---------------------------------------------------------------- charter

        /// <summary>SMSG_PETITION_SHOWLIST (PetitionsHandler.cpp:848-881): uint64 npc; uint8 count; count x { uint32 index; uint32 entry;
        /// uint32 displayId; uint32 cost; uint32 0; uint32 requiredSigns }. Returns the count (1 = guild master, 3 = arena organizer).</summary>
        public static int PetitionShowList(WowPacket p, GuildCharterView c)
        {
            c.Registrar = true;
            c.NpcGuid = p.U64();
            int n = p.U8();
            if (n < 1) return n;
            p.U32();
            c.CharterEntry = (int)p.U32(); p.U32();
            c.Cost = (int)p.U32(); p.U32();
            c.RequiredSignatures = (int)p.U32();
            return n;
        }

        /// <summary>SMSG_PETITION_SHOW_SIGNATURES (PetitionsHandler.cpp:259-270 / :643-655): uint64 petition (= the charter item guid);
        /// uint64 owner (the asking player, or the offerer); uint32 petitionId; uint8 count; count x (uint64 player, uint32 0).</summary>
        public static void PetitionSignatures(WowPacket p, out ulong petition, out ulong owner, out uint petitionId, List<ulong> signers)
        {
            petition = p.U64();
            owner = p.U64();
            petitionId = p.U32();
            int n = p.U8();
            signers.Clear();
            for (int i = 0; i < n && p.Remaining >= 12; i++) { signers.Add(p.U64()); p.U32(); }
        }

        /// <summary>SMSG_PETITION_QUERY_RESPONSE (PetitionsHandler.cpp:298-331): uint32 petitionId; uint64 owner; CString name; uint8 0;
        /// uint32 needed (MinPetitionSigns for a guild charter); ... (the rest is constant).</summary>
        public static uint PetitionQuery(WowPacket p, out ulong owner, out string name, out int needed)
        {
            uint id = p.U32();
            owner = p.U64();
            name = p.CString();
            p.U8();
            needed = p.Remaining >= 4 ? (int)p.U32() : 0;
            return id;
        }
    }
}
