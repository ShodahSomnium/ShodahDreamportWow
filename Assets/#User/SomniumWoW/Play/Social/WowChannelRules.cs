using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Social
{
    /// <summary>
    /// Iteration 11 (owner social, proposal-social.md 3.5 / 4.4 / 6.4): the constant chat channels of the install's ChatChannels.dbc
    /// (6 rows x 37 fields measured: ID 0, Flags 1, FactionGroup 2, Name_lang 3 (locstring, 17 fields), Shortcut_lang 20 (17 fields)) and
    /// AzerothCore's rules for them:
    /// - Player::CanJoinConstantChannelInZone (Player.cpp:5058-5074) with LFG.Location.All = 0 (the default): ZONE_DEP rows not in arena
    ///   zones, CITY_ONLY rows only where the zone has AREA_FLAG_SLAVE_CAPITAL, GUILD_REQ rows only without a guild.
    /// - The name (Player::UpdateLocalChannels, PlayerUpdates.cpp:551-567): GLOBAL rows use the pattern as it is, the others replace "%s" by
    ///   "City" (CITY_ONLY - AC acore_string 819 LANG_CHANNEL_CITY, Language.h:769; ours in English, R4) or the zone's AreaTable name. On a
    ///   stock realm the server keeps the CLIENT's name (ChannelHandler.cpp:44-58), so these names must equal the real client's.
    /// - The numbers (ours, 3.5): General 1, Trade 2, LocalDefense 3, LookingForGroup 4 always; any other channel the lowest free from 5
    ///   (WoW's limit: 10 channels, ERR_TOO_MANY_CHAT_CHANNELS GlobalStrings.lua 3539).
    /// Pure C#.
    /// </summary>
    public sealed class WowChannelRules
    {
        // ChatChannels.dbc Flags (AzerothCore Channel.h:92-102 ChannelDBCFlags)
        public const int FlagInitial = 0x1, FlagZoneDep = 0x2, FlagGlobal = 0x4, FlagTrade = 0x8, FlagCityOnly = 0x10, FlagCityOnly2 = 0x20,
            FlagDefense = 0x10000, FlagGuildReq = 0x20000, FlagLfg = 0x40000;
        // AreaTable.Flags (DBCEnums.h:237 AREA_FLAG_SLAVE_CAPITAL, :250 AREA_FLAG_ARENA_INSTANCE)
        public const int AreaSlaveCapital = 0x8, AreaArenaInstance = 0x10000;
        public const int MaxChannels = 10;
        /// <summary>The CITY_ONLY name part (AC acore_string 819 'City'; the real client's word is in Wow.exe - ours, English).</summary>
        public const string CityName = "City";
        const int FieldId = 0, FieldFlags = 1, FieldName = 3, FieldShortcut = 20;

        public sealed class Row { public int Id, Flags; public string Pattern, Shortcut; }

        public readonly List<Row> Rows = new List<Row>();
        public string Problem;

        static readonly object Gate = new object();
        static WowFiles _cachedFiles;
        static WowChannelRules _cached;

        public static WowChannelRules Get(WowFiles files)
        {
            lock (Gate)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                var r = Load(files);
                _cachedFiles = files;
                _cached = r;
                return r;
            }
        }

        public static WowChannelRules Load(WowFiles files)
        {
            var rules = new WowChannelRules();
            try
            {
                DbcTable c = files != null ? Dbc.Load(files, "ChatChannels") : null;
                if (c == null) { rules.Problem = "ChatChannels.dbc missing"; return rules; }
                if (c.FieldCount < FieldShortcut + 1) { rules.Problem = "ChatChannels.dbc has " + c.FieldCount + " fields (37 expected)"; return rules; }
                for (int r = 0; r < c.RecordCount; r++)
                    rules.Rows.Add(new Row
                    {
                        Id = c.GetInt(r, FieldId), Flags = c.GetInt(r, FieldFlags),
                        Pattern = c.GetLocalizedString(r, FieldName) ?? "", Shortcut = c.GetLocalizedString(r, FieldShortcut) ?? ""
                    });
            }
            catch (Exception e) { rules.Problem = e.GetType().Name + ": " + e.Message; }
            return rules;
        }

        public Row Find(int id)
        {
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Id == id) return Rows[i];
            return null;
        }

        /// <summary>Player::CanJoinConstantChannelInZone (Player.cpp:5058-5074), LFG.Location.All off.</summary>
        public static bool CanJoin(Row ch, int zoneFlags, bool inGuild)
        {
            if (ch == null) return false;
            if ((ch.Flags & FlagZoneDep) != 0 && (zoneFlags & AreaArenaInstance) != 0) return false;
            if ((ch.Flags & FlagCityOnly) != 0 && (zoneFlags & AreaSlaveCapital) == 0) return false;
            if ((ch.Flags & FlagGuildReq) != 0 && inGuild) return false;
            return true;
        }

        /// <summary>The channel's name in a zone (PlayerUpdates.cpp:551-567).</summary>
        public static string Name(Row ch, string zoneName)
        {
            if (ch == null) return "";
            string p = ch.Pattern ?? "";
            if ((ch.Flags & FlagGlobal) != 0 || p.IndexOf("%s", StringComparison.Ordinal) < 0) return p;
            return p.Replace("%s", (ch.Flags & FlagCityOnly) != 0 ? CityName : (zoneName ?? ""));
        }

        /// <summary>The login set: the INITIAL rows the zone allows (General, Trade, LocalDefense, LookingForGroup on the 3.3.5a DBC - the same
        /// four the client's default ZONECHANNELS 0x02200003 of chat-cache.txt holds).</summary>
        public List<Row> LoginJoins(int zoneFlags, bool inGuild)
        {
            var l = new List<Row>();
            foreach (var r in Rows) if ((r.Flags & FlagInitial) != 0 && CanJoin(r, zoneFlags, inGuild)) l.Add(r);
            return l;
        }

        /// <summary>
        /// A typed channel name that means a constant channel: its full name in this zone ("General - Elwynn Forest"), its short name
        /// ("General", Shortcut_lang - ours: rejoins the zone's channel) or a GLOBAL name ("WorldDefense"). Null = a custom channel (id 0).
        /// </summary>
        public Row Match(string typed, string zoneName, out string fullName)
        {
            fullName = null;
            if (string.IsNullOrEmpty(typed)) return null;
            foreach (var r in Rows)
            {
                string n = Name(r, zoneName);
                if (string.Equals(typed, n, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(r.Shortcut) && string.Equals(typed, r.Shortcut, StringComparison.OrdinalIgnoreCase)))
                {
                    fullName = n;
                    return r;
                }
            }
            return null;
        }

        /// <summary>Ours (3.5): ids 1, 2, 22, 26 keep the numbers 1-4; any other channel the lowest free number from 5; 0 = none free.</summary>
        public static int NumberFor(int channelId, ICollection<int> used)
        {
            switch (channelId) { case 1: return 1; case 2: return 2; case 22: return 3; case 26: return 4; }
            for (int n = 5; n <= MaxChannels; n++) if (used == null || !used.Contains(n)) return n;
            return 0;
        }
    }
}
