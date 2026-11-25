using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Social
{
    /// <summary>
    /// Iteration 11 (owner social, proposal-social.md 3.4 / 6.4): the install's zone / race / class names (AreaTable AreaName_lang 11,
    /// ChrRaces Name_lang 14, ChrClasses Name_lang 4 - W/Data/DbcFields.cs) for the Who query words and for every view that shows a zone,
    /// race or class by id (friends, guild roster, Who rows). Read once per WowFiles. Pure C#.
    /// </summary>
    public sealed class WowWhoNames
    {
        /// <summary>Lower-case zone / area name -> AreaTable ids (several areas can share a name).</summary>
        public readonly Dictionary<string, List<uint>> Zones = new Dictionary<string, List<uint>>();
        public readonly Dictionary<string, int> Races = new Dictionary<string, int>(), Classes = new Dictionary<string, int>();
        readonly Dictionary<int, string> _zoneName = new Dictionary<int, string>(), _raceName = new Dictionary<int, string>(), _className = new Dictionary<int, string>();
        readonly Dictionary<int, int> _zoneFlags = new Dictionary<int, int>();
        public string Problem;

        static readonly object Gate = new object();
        static WowFiles _cachedFiles;
        static WowWhoNames _cached;

        public static WowWhoNames Get(WowFiles files)
        {
            lock (Gate)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                var n = Load(files);
                _cachedFiles = files;
                _cached = n;
                return n;
            }
        }

        public static WowWhoNames Load(WowFiles files)
        {
            var w = new WowWhoNames();
            try
            {
                DbcTable a = files != null ? Dbc.Load(files, "AreaTable") : null;
                DbcTable cr = files != null ? Dbc.Load(files, "ChrRaces") : null;
                DbcTable cc = files != null ? Dbc.Load(files, "ChrClasses") : null;
                if (a == null || cr == null || cc == null) { w.Problem = "AreaTable / ChrRaces / ChrClasses missing"; return w; }
                for (int r = 0; r < a.RecordCount; r++)
                {
                    int id = a.GetInt(r, DbcFields.AreaTable.ID);
                    w._zoneFlags[id] = a.GetInt(r, DbcFields.AreaTable.Flags);
                    string n = a.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang);
                    if (string.IsNullOrEmpty(n)) continue;
                    w._zoneName[id] = n;
                    List<uint> l;
                    string k = n.ToLowerInvariant();
                    if (!w.Zones.TryGetValue(k, out l)) w.Zones[k] = l = new List<uint>();
                    l.Add((uint)id);
                }
                for (int r = 0; r < cr.RecordCount; r++)
                {
                    string n = cr.GetLocalizedString(r, DbcFields.ChrRaces.Name_lang);
                    if (string.IsNullOrEmpty(n)) continue;
                    int id = cr.GetInt(r, 0);
                    w._raceName[id] = n;
                    w.Races[n.ToLowerInvariant()] = id;
                }
                for (int r = 0; r < cc.RecordCount; r++)
                {
                    string n = cc.GetLocalizedString(r, DbcFields.ChrClasses.Name_lang);
                    if (string.IsNullOrEmpty(n)) continue;
                    int id = cc.GetInt(r, 0);
                    w._className[id] = n;
                    w.Classes[n.ToLowerInvariant()] = id;
                }
            }
            catch (Exception e) { w.Problem = e.GetType().Name + ": " + e.Message; }
            return w;
        }

        public string ZoneName(int id) { string n; return id != 0 && _zoneName.TryGetValue(id, out n) ? n : null; }
        public string RaceName(int id) { string n; return _raceName.TryGetValue(id, out n) ? n : null; }
        public string ClassName(int id) { string n; return _className.TryGetValue(id, out n) ? n : null; }
        /// <summary>AreaTable.Flags of a zone (AREA_FLAG_SLAVE_CAPITAL 0x8, AREA_FLAG_ARENA_INSTANCE 0x10000 - DBCEnums.h:237 / :250), 0 = unknown.</summary>
        public int ZoneFlags(int id) { int f; return _zoneFlags.TryGetValue(id, out f) ? f : 0; }
    }

    /// <summary>
    /// WoW's /who text -> the CMSG_WHO fields (AzerothCore MiscHandler.cpp:227-262 reads: uint32 levelMin, uint32 levelMax, CString name,
    /// CString guild, uint32 raceMask, uint32 classMask, uint32 zoneCount (&lt;= 10, else the request is dropped), zones, uint32 strCount
    /// (&lt;= 4), strings). The tags are WoW's (GlobalStrings.lua 8941-8945): n-"name", g-"guild", z-"zone", r-"race", c-"class"; "10-20" /
    /// "60" levels; quoted multi-word values; other words are free strings (the server matches them against name, guild and zone).
    /// Defaults (ours, what the fields mean): 0 - 100 (>= MAX_LEVEL = "up to 255", MiscHandler.cpp:283-284), every race / class.
    /// </summary>
    public sealed class WowWhoQuery
    {
        public const int MaxZones = 10, MaxWords = 4;
        public uint LevelMin = 0, LevelMax = 100;
        public string Name = "", Guild = "";
        public uint RaceMask = 0xFFFFFFFF, ClassMask = 0xFFFFFFFF;
        public readonly List<uint> Zones = new List<uint>();
        public readonly List<string> Words = new List<string>();

        /// <summary>Splits at white space outside double quotes (the quotes are dropped).</summary>
        public static List<string> Tokens(string text)
        {
            var l = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            foreach (char ch in text ?? "")
            {
                if (ch == '"') { q = !q; continue; }
                if (char.IsWhiteSpace(ch) && !q) { if (sb.Length > 0) { l.Add(sb.ToString()); sb.Length = 0; } continue; }
                sb.Append(ch);
            }
            if (sb.Length > 0) l.Add(sb.ToString());
            return l;
        }

        public static WowWhoQuery Parse(string text, WowWhoNames names)
        {
            var w = new WowWhoQuery();
            foreach (string tok in Tokens(text))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                string low = t.ToLowerInvariant();
                int dash = t.IndexOf('-');
                uint a, b;
                if (dash > 0 && uint.TryParse(t.Substring(0, dash), out a) && uint.TryParse(t.Substring(dash + 1), out b)) { w.LevelMin = a; w.LevelMax = b; continue; }
                if (uint.TryParse(t, out a)) { w.LevelMin = a; w.LevelMax = a; continue; }
                if (low.Length > 2 && low[1] == '-')
                {
                    string v = t.Substring(2), vl = low.Substring(2);
                    switch (low[0])
                    {
                        case 'n': w.Name = Clean(v); continue;
                        case 'g': w.Guild = Clean(v); continue;
                        case 'z':
                            List<uint> ids;
                            if (names != null && names.Zones.TryGetValue(vl, out ids))
                            {
                                foreach (uint id in ids) if (w.Zones.Count < MaxZones && !w.Zones.Contains(id)) w.Zones.Add(id);
                                continue;
                            }
                            break;   // an unknown zone word: a free word (the server matches zone names too)
                        case 'r':
                            int race;
                            if (names != null && names.Races.TryGetValue(vl, out race) && race >= 1 && race <= 32)
                            {
                                if (w.RaceMask == 0xFFFFFFFF) w.RaceMask = 0;
                                w.RaceMask |= 1u << (race - 1);
                                continue;
                            }
                            break;
                        case 'c':
                            int cls;
                            if (names != null && names.Classes.TryGetValue(vl, out cls) && cls >= 1 && cls <= 32)
                            {
                                if (w.ClassMask == 0xFFFFFFFF) w.ClassMask = 0;
                                w.ClassMask |= 1u << (cls - 1);
                                continue;
                            }
                            break;
                    }
                }
                if (w.Words.Count < MaxWords) w.Words.Add(Clean(t));
            }
            return w;
        }

        /// <summary>No '|' in a string the server validates for hyperlinks (ours: '|' -> '/').</summary>
        static string Clean(string s) { return (s ?? "").Replace('|', '/'); }

        /// <summary>The CMSG_WHO body (field order of MiscHandler.cpp:227-262).</summary>
        public byte[] Body()
        {
            var p = new PacketWriter().U32(LevelMin).U32(LevelMax).CString(Name).CString(Guild).U32(RaceMask).U32(ClassMask).U32((uint)Zones.Count);
            foreach (uint z in Zones) p.U32(z);
            p.U32((uint)Words.Count);
            foreach (string s in Words) p.CString(s);
            return p.ToArray();
        }

        /// <summary>WHO_LIST_FORMAT "|Hplayer:%s|h[%s]|h: Level %d %s %s - %s" / WHO_LIST_GUILD_FORMAT "... %s %s &lt;%s&gt; - %s"
        /// (GlobalStrings.lua 8938-8939) with the link head stripped like every chat line: "[Name]: Level 20 Orc Warrior - Durotar".</summary>
        public static string Line(WhoView w)
        {
            if (w == null) return "";
            string race = w.RaceName ?? "", cls = w.ClassName ?? "", zone = w.ZoneName ?? "";
            return string.IsNullOrEmpty(w.GuildName)
                ? "[" + w.Name + "]: Level " + w.Level + " " + race + " " + cls + " - " + zone
                : "[" + w.Name + "]: Level " + w.Level + " " + race + " " + cls + " <" + w.GuildName + "> - " + zone;
        }

        /// <summary>WHO_NUM_RESULTS "%d |4player:players; total" (GlobalStrings.lua 8940; |4 = WoW's singular / plural choice).</summary>
        public static string TotalLine(int total) { return total + (total == 1 ? " player total" : " players total"); }
    }
}
