// iteration 11 - owner world (proposal-world.md 2.3 / 2.7): pure helpers for dungeons, raids, battlegrounds and arenas - Map.dbc type and
// name, the WMO-only test, MapDifficulty.dbc / DungeonEncounter.dbc lookups, WoW's own instance texts and UIParent.lua's SecondsToTime.
// Every text is WoW's (Interface\FrameXML\GlobalStrings.lua line in the comment, enUS); the Game layer sets WowInstances.Strings to the
// install's own GlobalStrings (UiGlobalStrings.G) so another language shows its own words. Pure C#: no UnityEngine.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    public static class WowInstances
    {
        /// <summary>(key, enUS fallback) -> the install's text of a GlobalStrings key. Null = the enUS fallbacks quoted here (the harness,
        /// or before the Game layer read the file).</summary>
        public static Func<string, string, string> Strings;

        /// <summary>A GlobalStrings key's text (the install's when Strings is set, else the quoted enUS fallback).</summary>
        public static string G(string key, string enUS)
        {
            var f = Strings;
            if (f != null)
            {
                try { var s = f(key, enUS); if (!string.IsNullOrEmpty(s)) return s; }
                catch (Exception) { }
            }
            return enUS;
        }

        /// <summary>G(key) formatted like WoW's format(): %s %d %i %u %%, %n$s / %n$d positions, then |4singular:plural; picked by the
        /// number formatted before it (1 = singular).</summary>
        public static string F(string key, string enUS, params object[] args) { return Format(G(key, enUS), args); }

        public static string Format(string text, params object[] args)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length + 32);
            int next = 0;
            double last = double.NaN;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '|' && i + 1 < text.Length && text[i + 1] == '4')
                {
                    int colon = text.IndexOf(':', i + 2), semi = colon >= 0 ? text.IndexOf(';', colon + 1) : -1;
                    if (colon > 0 && semi > 0)
                    {
                        bool one = !double.IsNaN(last) && Math.Abs(last - 1.0) < 1e-9;
                        sb.Append(one ? text.Substring(i + 2, colon - i - 2) : text.Substring(colon + 1, semi - colon - 1));
                        i = semi;
                        continue;
                    }
                }
                if (c != '%' || i + 1 >= text.Length) { sb.Append(c); continue; }
                int j = i + 1;
                if (text[j] == '%') { sb.Append('%'); i = j; continue; }
                int argIndex = -1;
                int k = j;
                while (k < text.Length && char.IsDigit(text[k])) k++;
                if (k > j && k < text.Length && text[k] == '$') { argIndex = int.Parse(text.Substring(j, k - j), CultureInfo.InvariantCulture) - 1; j = k + 1; }
                while (j < text.Length && (text[j] == '.' || text[j] == '-' || char.IsDigit(text[j]) || text[j] == 'l')) j++;
                if (j >= text.Length) { sb.Append(text, i, text.Length - i); break; }
                char conv = text[j];
                if (conv != 's' && conv != 'd' && conv != 'i' && conv != 'u' && conv != 'x') { sb.Append(c); continue; }
                int ai = argIndex >= 0 ? argIndex : next++;
                object a = args != null && ai >= 0 && ai < args.Length ? args[ai] : null;
                if (conv == 's') sb.Append(a == null ? "" : Convert.ToString(a, CultureInfo.InvariantCulture));
                else
                {
                    long n = 0;
                    try { n = a == null ? 0 : Convert.ToInt64(a is float || a is double ? Math.Floor(Convert.ToDouble(a, CultureInfo.InvariantCulture)) : a, CultureInfo.InvariantCulture); } catch (Exception) { }
                    sb.Append(conv == 'x' ? n.ToString("x", CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture));
                    last = n;
                }
                i = j;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ Map.dbc

        /// <summary>Map.dbc InstanceType (DBCEnums.h:344-351 MapTypes: 0 common, 1 instance, 2 raid, 3 battleground, 4 arena); 0 unknown.</summary>
        public static int InstanceType(WowFiles files, int mapId)
        {
            var t = Dbc.Load(files, "Map");
            if (t == null) return 0;
            int r = t.FindRow(mapId);
            if (r < 0) return 0;
            int v = t.GetInt(r, DbcFields.Map.InstanceType);
            return v >= 0 && v <= 4 ? v : 0;
        }

        /// <summary>Map.dbc MapName_lang ("map N" when unknown - ours).</summary>
        public static string MapName(WowFiles files, int mapId)
        {
            var t = Dbc.Load(files, "Map");
            int r = t != null ? t.FindRow(mapId) : -1;
            string s = r >= 0 ? t.GetLocalizedString(r, DbcFields.Map.MapName_lang) : null;
            return string.IsNullOrEmpty(s) ? "map " + mapId : s;
        }

        /// <summary>AreaTable.dbc AreaName_lang ("area N" when unknown - ours).</summary>
        public static string AreaName(WowFiles files, int areaId)
        {
            var t = Dbc.Load(files, "AreaTable");
            int r = t != null ? t.FindRow(areaId) : -1;
            string s = r >= 0 ? t.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang) : null;
            return string.IsNullOrEmpty(s) ? "area " + areaId : s;
        }

        static readonly object WmoGate = new object();
        static WowFiles _wmoFiles;
        static readonly Dictionary<int, bool> _wmoOnly = new Dictionary<int, bool>();

        /// <summary>The map's WDT has only a global WMO and no ADT tiles (WdtFile.IsWmoOnly); cached per map. False when unreadable.</summary>
        public static bool IsWmoOnly(WowFiles files, int mapId)
        {
            if (files == null || mapId < 0) return false;
            lock (WmoGate)
            {
                if (!ReferenceEquals(_wmoFiles, files)) { _wmoOnly.Clear(); _wmoFiles = files; }
                bool v;
                if (_wmoOnly.TryGetValue(mapId, out v)) return v;
                string dir = WowMapData.Directory(files, mapId);
                WdtFile w = dir != null ? WowMapData.LoadWdt(files, dir) : null;
                v = w != null && w.IsWmoOnly;
                _wmoOnly[mapId] = v;
                return v;
            }
        }

        // ------------------------------------------------------------------ difficulty

        /// <summary>UnitPopup's difficulty rows: dungeon DUNGEON_DIFFICULTY1/2(/3) (GlobalStrings.lua:2035-2037), raid RAID_DIFFICULTY1-4 (:5950-5953).</summary>
        public static string DifficultyText(bool raid, int d)
        {
            if (raid)
            {
                switch (d)
                {
                    case 0: return G("RAID_DIFFICULTY1", "10 Player");               // :5950
                    case 1: return G("RAID_DIFFICULTY2", "25 Player");               // :5951
                    case 2: return G("RAID_DIFFICULTY3", "10 Player (Heroic)");      // :5952
                    case 3: return G("RAID_DIFFICULTY4", "25 Player (Heroic)");      // :5953
                }
                return d.ToString(CultureInfo.InvariantCulture);
            }
            switch (d)
            {
                case 0: return G("DUNGEON_DIFFICULTY1", "5 Player");                 // :2035
                case 1: return G("DUNGEON_DIFFICULTY2", "5 Player (Heroic)");        // :2036
                case 2: return G("DUNGEON_DIFFICULTY3", "Epic (Unused)");            // :2037
            }
            return d.ToString(CultureInfo.InvariantCulture);
        }

        // MapDifficulty.dbc (AC fmt "diisxxxxxxxxxxxxxxxxiix", DBCfmt.h:88; DBCStructure.h:1414-1424; measured 187 rows x 23 fields):
        // 0 ID, 1 MapID, 2 Difficulty, 3-18 Message_lang (+19 flags), 20 RaidDuration (s), 21 MaxPlayers, 22 the GlobalStrings key
        const int MdMap = 1, MdDifficulty = 2, MdMessage = 3, MdReset = 20, MdMaxPlayers = 21, MdKey = 22;

        static int MapDifficultyRow(WowFiles files, int mapId, int d, out DbcTable t)
        {
            t = Dbc.Load(files, "MapDifficulty");
            if (t == null || t.FieldCount < 23) return -1;
            for (int r = 0; r < t.RecordCount; r++)
                if (t.GetInt(r, MdMap) == mapId && t.GetInt(r, MdDifficulty) == d) return r;
            return -1;
        }

        /// <summary>How many MapDifficulty.dbc rows the map has (WoW names the difficulty only for maps with more than one).</summary>
        public static int MapDifficultyCount(WowFiles files, int mapId)
        {
            var t = Dbc.Load(files, "MapDifficulty");
            if (t == null || t.FieldCount < 23) return 0;
            int n = 0;
            for (int r = 0; r < t.RecordCount; r++) if (t.GetInt(r, MdMap) == mapId) n++;
            return n;
        }

        /// <summary>MapDifficulty.dbc Message_lang of (map, difficulty) - the text AC refers to with TRANSFER_ABORT_DIFFICULTY
        /// (PlayerStorage.cpp:7033-7036 `mapDiff->hasErrorMessage`); null when the row has none.</summary>
        public static string MapDifficultyText(WowFiles files, int mapId, int d)
        {
            DbcTable t;
            int r = MapDifficultyRow(files, mapId, d, out t);
            if (r < 0) return null;
            string s = t.GetLocalizedString(r, MdMessage);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>MapDifficulty.dbc field 22 (a GlobalStrings key such as RAID_DIFFICULTY_25PLAYER) as WoW's text
        /// (GlobalStrings.lua:2038-2039, :5954-5959); the UnitPopup row text of the map's type when the row is missing.</summary>
        public static string MapDifficultyName(WowFiles files, int mapId, int d)
        {
            DbcTable t;
            int r = MapDifficultyRow(files, mapId, d, out t);
            string key = r >= 0 ? t.GetString(r, MdKey) : null;
            switch (key)
            {
                case "DUNGEON_DIFFICULTY_5PLAYER": return G(key, "5 Player");                       // :2038
                case "DUNGEON_DIFFICULTY_5PLAYER_HEROIC": return G(key, "5 Player (Heroic)");       // :2039
                case "RAID_DIFFICULTY_10PLAYER": return G(key, "10 Player");                        // :5954
                case "RAID_DIFFICULTY_10PLAYER_HEROIC": return G(key, "10 Player (Heroic)");        // :5955
                case "RAID_DIFFICULTY_20PLAYER": return G(key, "20 Player");                        // :5956
                case "RAID_DIFFICULTY_25PLAYER": return G(key, "25 Player");                        // :5957
                case "RAID_DIFFICULTY_25PLAYER_HEROIC": return G(key, "25 Player (Heroic)");        // :5958
                case "RAID_DIFFICULTY_40PLAYER": return G(key, "40 Player");                        // :5959
            }
            if (!string.IsNullOrEmpty(key)) return G(key, key);
            return DifficultyText(InstanceType(files, mapId) == 2, d);
        }

        /// <summary>The instance's name with its difficulty when the map has more than one ("Naxxramas (25 Player)"; INSTANCE_DIFFICULTY_FORMAT
        /// "(%s)", GlobalStrings.lua:4245).</summary>
        public static string NameWithDifficulty(WowFiles files, int mapId, int d)
        {
            string n = MapName(files, mapId);
            if (MapDifficultyCount(files, mapId) <= 1) return n;
            return n + " " + F("INSTANCE_DIFFICULTY_FORMAT", "(%s)", MapDifficultyName(files, mapId, d));
        }

        /// <summary>DungeonEncounter.dbc rows of (map, difficulty) and how many of their encounter bits are in mask (BOSSES_KILLED;
        /// DBCStructure.h:892-902: 1 map, 2 difficulty, 4 encounter index).</summary>
        public static void Encounters(WowFiles files, int mapId, int d, uint mask, out int done, out int total)
        {
            done = 0; total = 0;
            var t = Dbc.Load(files, "DungeonEncounter");
            if (t == null || t.FieldCount < 5) return;
            for (int r = 0; r < t.RecordCount; r++)
            {
                if (t.GetInt(r, 1) != mapId || t.GetInt(r, 2) != d) continue;
                total++;
                int bit = t.GetInt(r, 4);
                if (bit >= 0 && bit < 32 && (mask & (1u << bit)) != 0) done++;
            }
        }

        // ------------------------------------------------------------------ texts

        /// <summary>SMSG_TRANSFER_ABORTED (Player.h:768-786 TransferAbortReason) -> WoW's TRANSFER_ABORT_* text (GlobalStrings.lua:7673-7686).</summary>
        public static string TransferAbortText(WowFiles files, int mapId, int reason, int arg)
        {
            switch (reason)
            {
                case 0x02: return G("TRANSFER_ABORT_MAX_PLAYERS", "Transfer Aborted: instance is full");                                   // :7679
                case 0x04: return G("TRANSFER_ABORT_TOO_MANY_INSTANCES", "You have entered too many instances recently.");                 // :7683
                case 0x06: return G("TRANSFER_ABORT_ZONE_IN_COMBAT", "Unable to zone in while an encounter is in progress.");              // :7686
                case 0x07:
                    return arg >= 2 ? G("TRANSFER_ABORT_INSUF_EXPAN_LVL2", "You must have The Wrath of the Lich King expansion installed to access this area.")   // :7677
                                    : G("TRANSFER_ABORT_INSUF_EXPAN_LVL1", "You must have The Burning Crusade expansion installed to access this area.");       // :7676
                case 0x08:
                {
                    string own = MapDifficultyText(files, mapId, arg);   // the reason AC sends 8 (PlayerStorage.cpp:7033-7036)
                    if (own != null) return own;
                    string name = MapName(files, mapId);
                    if (arg == 1) return F("TRANSFER_ABORT_DIFFICULTY2", "Heroic difficulty mode is not available for %s.", name);         // :7674
                    if (arg >= 2) return F("TRANSFER_ABORT_DIFFICULTY3", "Epic difficulty mode is not available for %s.", name);           // :7675
                    return F("TRANSFER_ABORT_DIFFICULTY1", "Normal difficulty mode is not available for %s.", name);                       // :7673
                }
                case 0x09: return G("TRANSFER_ABORT_UNIQUE_MESSAGE1", "Until you've escaped The Lich King's grasp, you cannot leave this place!");   // :7685
                case 0x0A: return G("TRANSFER_ABORT_TOO_MANY_REALM_INSTANCES", "Additional instances cannot be launched, please try again later."); // :7684
                case 0x0B: return G("TRANSFER_ABORT_NEED_GROUP", "Transfer Aborted: you must be in a raid group to enter this instance");           // :7680
                case 0x0F: return G("TRANSFER_ABORT_REALM_ONLY", "Transfer Aborted: all players in the party must be from the same realm");         // :7682
                case 0x10: return G("TRANSFER_ABORT_MAP_NOT_ALLOWED", "Map cannot be entered at this time.");                                      // :7678
            }
            // 0x01 ERROR, 0x03 NOT_FOUND, 0x0C-0x0E NOT_FOUND1-3 and anything unknown (ours for 0 / unknown)
            return G("TRANSFER_ABORT_NOT_FOUND", "Transfer Aborted: instance not found");                                                   // :7681
        }

        /// <summary>SMSG_INSTANCE_RESET_FAILED reason (Player.h:797-802) -> INSTANCE_RESET_FAILED(_OFFLINE / _ZONING) (GlobalStrings.lua:4251-4253).</summary>
        public static string ResetFailedText(string mapName, int reason)
        {
            if (reason == 1) return F("INSTANCE_RESET_FAILED_OFFLINE", "Cannot reset %s.  There are players offline in your party.", mapName);
            if (reason == 2) return F("INSTANCE_RESET_FAILED_ZONING", "Cannot reset %s.  There are players in your party attempting to zone into an instance.", mapName);
            return F("INSTANCE_RESET_FAILED", "Cannot reset %s.  There are players still inside the instance.", mapName);
        }

        /// <summary>SMSG_RAID_INSTANCE_MESSAGE (Player.cpp:11968-11998; Player.h:788-795) -> WoW's chat line (UIParent.lua:1059-1083 for
        /// type 4; GlobalStrings.lua:5964, :5972-5980). Hours / minutes of types 1-3 = ceil(s / 3600) / ceil(s / 60) (ours: the client's
        /// rounding is C++). Null for an unknown type.</summary>
        public static string RaidInstanceMessage(string mapName, int type, uint seconds, bool locked, bool extended)
        {
            switch (type)
            {
                case 1: return F("RAID_INSTANCE_WARNING_HOURS", "Your instance lock for %s will expire in %d |4hour:hours;.", mapName, (long)Math.Ceiling(seconds / 3600.0));
                case 2: return F("RAID_INSTANCE_WARNING_MIN", "Your instance lock for %s will expire in %d |4minute:minutes;!", mapName, (long)Math.Ceiling(seconds / 60.0));
                case 3: return F("RAID_INSTANCE_WARNING_MIN_SOON", "Your instance lock for %s will expire in %d |4minute:minutes;.", mapName, (long)Math.Ceiling(seconds / 60.0));
                case 4:
                    if (!locked) return F("RAID_INSTANCE_WELCOME", "Welcome to %s. Instance locks are scheduled to expire in %s.", mapName, SecondsToTime(seconds, false, true));
                    if (seconds == 0) return F("RAID_INSTANCE_WELCOME_EXTENDED", "Welcome to %s. Your instance lock has been extended.", mapName);
                    if (!extended) return F("RAID_INSTANCE_WELCOME_LOCKED", "Welcome to %s. Your instance lock is scheduled to expire in %s.", mapName, SecondsToTime(seconds, false, true));
                    return F("RAID_INSTANCE_WELCOME_LOCKED_EXTENDED", "Welcome to %s. Your instance lock is scheduled to expire in %s (extended).", mapName, SecondsToTime(seconds, false, true));
                case 5: return F("RAID_INSTANCE_EXPIRED", "Your instance lock for %s has expired.", mapName);
            }
            return null;
        }

        /// <summary>UIParent.lua:2271-2325 SecondsToTime, line by line (D_DAYS / D_HOURS / D_MINUTES / D_SECONDS :2100-2103, DAYS_ABBR :1925,
        /// HOURS_ABBR :4196, MINUTES_ABBR :4953, SECONDS_ABBR :6201, TIME_UNIT_DELIMITER " " :7578). As the original, the hours part is kept
        /// even when maxCount was reached by the days.</summary>
        public static string SecondsToTime(double secondsIn, bool noSeconds = false, bool notAbbreviated = false, int maxCount = 2)
        {
            string time = "";
            int count = 0;
            long seconds = (long)Math.Floor(secondsIn);
            string delim = G("TIME_UNIT_DELIMITER", " ");
            if (seconds >= 86400)
            {
                long t = seconds / 86400;
                time = notAbbreviated ? F("D_DAYS", "%d |4Day:Days;", t) : F("DAYS_ABBR", "%d |4Day:Days;", t);
                seconds %= 86400;
                count++;
            }
            if (seconds >= 3600)
            {
                if (time != "") time += delim;
                long t = seconds / 3600;
                time += notAbbreviated ? F("D_HOURS", "%d |4Hour:Hours;", t) : F("HOURS_ABBR", "%d |4Hr:Hr;", t);
                seconds %= 3600;
                count++;
            }
            if (count < maxCount && seconds >= 60)
            {
                if (time != "") time += delim;
                long t = seconds / 60;
                time += notAbbreviated ? F("D_MINUTES", "%d |4Minute:Minutes;", t) : F("MINUTES_ABBR", "%d |4Min:Min;", t);
                seconds %= 60;
                count++;
            }
            if (count < maxCount && seconds > 0 && !noSeconds)
            {
                if (time != "") time += delim;
                time += notAbbreviated ? F("D_SECONDS", "%d |4Second:Seconds;", seconds) : F("SECONDS_ABBR", "%d |4Sec:Sec;", seconds);
            }
            return time;
        }
    }
}
