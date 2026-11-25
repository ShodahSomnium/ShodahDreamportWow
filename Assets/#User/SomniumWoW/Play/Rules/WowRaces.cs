using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>
    /// The 10 playable races of 3.3.5a (iteration 3). The avatar stays the Somnium avatar: a race is its racial abilities, its racial
    /// stat modifiers (player_race_stats), its starting zone (WowZones) and its faction. Names, icons and descriptions come from the
    /// player's files (ChrRaces.dbc); class availability from CharBaseInfo.dbc. Pure C#.
    /// </summary>
    public static class WowRaces
    {
        public const int Human = 1, Orc = 2, Dwarf = 3, NightElf = 4, Undead = 5, Tauren = 6, Gnome = 7, Troll = 8, BloodElf = 10, Draenei = 11;

        /// <summary>Alliance races in the character screen's order (ChrRaces.dbc Alliance = 0, measured).</summary>
        public static readonly int[] Alliance = { Human, Dwarf, NightElf, Gnome, Draenei };
        /// <summary>Horde races in the character screen's order (ChrRaces.dbc Alliance = 1, measured).</summary>
        public static readonly int[] Horde = { Orc, Undead, Tauren, Troll, BloodElf };
        /// <summary>All 10 playable races, Alliance first.</summary>
        public static readonly int[] Playable = { Human, Dwarf, NightElf, Gnome, Draenei, Orc, Undead, Tauren, Troll, BloodElf };

        public static bool IsPlayable(int race) { return Array.IndexOf(Playable, race) >= 0; }

        /// <summary>The faction of a playable race (Alliance for unknown ids).</summary>
        public static WowFaction FactionOf(int race) { return Array.IndexOf(Horde, race) >= 0 ? WowFaction.Horde : WowFaction.Alliance; }

        public static int[] RacesOf(WowFaction f) { return f == WowFaction.Horde ? Horde : Alliance; }

        /// <summary>The race's name from ChrRaces.dbc (localized), or an English fallback without files.</summary>
        public static string Name(WowFiles files, int race)
        {
            var t = files != null ? Dbc.Load(files, "ChrRaces") : null;
            if (t != null)
            {
                int r = t.FindRow(race);
                if (r >= 0)
                {
                    string n = t.GetLocalizedString(r, DbcFields.ChrRaces.Name_lang);
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            switch (race)
            {
                case Human: return "Human"; case Orc: return "Orc"; case Dwarf: return "Dwarf"; case NightElf: return "Night Elf"; case Undead: return "Undead";
                case Tauren: return "Tauren"; case Gnome: return "Gnome"; case Troll: return "Troll"; case BloodElf: return "Blood Elf"; case Draenei: return "Draenei";
            }
            return "Race " + race;
        }

        /// <summary>ChrRaces.dbc ClientFileString ("Human", "NightElf", "Scourge", "BloodElf"...) - the key of the race's icon/texture files.</summary>
        public static string ClientFileString(WowFiles files, int race)
        {
            var t = files != null ? Dbc.Load(files, "ChrRaces") : null;
            if (t == null) return "";
            int r = t.FindRow(race);
            return r >= 0 ? t.GetString(r, DbcFields.ChrRaces.ClientFileString) : "";
        }

        // CharBaseInfo.dbc measured on 3.3.5a (62 rows of byte race, byte class) - used only when the file cannot be read.
        private static readonly Dictionary<int, WowClass[]> MeasuredClasses = new Dictionary<int, WowClass[]>
        {
            { Human,    new[] { WowClass.Warrior, WowClass.Paladin, WowClass.Rogue, WowClass.Priest, WowClass.Mage, WowClass.Warlock } },
            { Orc,      new[] { WowClass.Warrior, WowClass.Hunter, WowClass.Rogue, WowClass.Shaman, WowClass.Warlock } },
            { Dwarf,    new[] { WowClass.Warrior, WowClass.Paladin, WowClass.Hunter, WowClass.Rogue, WowClass.Priest } },
            { NightElf, new[] { WowClass.Warrior, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.Druid } },
            { Undead,   new[] { WowClass.Warrior, WowClass.Rogue, WowClass.Priest, WowClass.Mage, WowClass.Warlock } },
            { Tauren,   new[] { WowClass.Warrior, WowClass.Hunter, WowClass.Shaman, WowClass.Druid } },
            { Gnome,    new[] { WowClass.Warrior, WowClass.Rogue, WowClass.Mage, WowClass.Warlock } },
            { Troll,    new[] { WowClass.Warrior, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.Shaman, WowClass.Mage } },
            { BloodElf, new[] { WowClass.Paladin, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.Mage, WowClass.Warlock } },
            { Draenei,  new[] { WowClass.Warrior, WowClass.Paladin, WowClass.Hunter, WowClass.Priest, WowClass.Shaman, WowClass.Mage } },
        };

        private static readonly object CacheLock = new object();
        private static WowFiles _cacheFiles;
        private static Dictionary<int, List<WowClass>> _cache;

        /// <summary>
        /// The classes a race can be, from CharBaseInfo.dbc (byte race, byte class; measured 62 rows), in ChrClasses order, without the
        /// Death Knight (it starts at level 55 in another zone). Falls back to the measured table when the file is missing.
        /// </summary>
        public static List<WowClass> Classes(WowFiles files, int race)
        {
            lock (CacheLock)
            {
                if (_cache == null || !ReferenceEquals(_cacheFiles, files))
                {
                    _cache = new Dictionary<int, List<WowClass>>();
                    _cacheFiles = files;
                    var t = files != null ? Dbc.Load(files, "CharBaseInfo") : null;
                    if (t != null && t.RecordSize == 2)
                    {
                        for (int r = 0; r < t.RecordCount; r++)
                        {
                            int rc = t.GetByte(r, DbcFields.CharBaseInfo.RaceID), cl = t.GetByte(r, DbcFields.CharBaseInfo.ClassID);
                            var c = (WowClass)cl;
                            if (!WowFormulas.IsSupportedClass(c)) continue;
                            List<WowClass> l;
                            if (!_cache.TryGetValue(rc, out l)) { l = new List<WowClass>(); _cache[rc] = l; }
                            if (!l.Contains(c)) l.Add(c);
                        }
                        foreach (var l in _cache.Values) l.Sort((a, b) => ((int)a).CompareTo((int)b));
                    }
                    else
                    {
                        if (files != null) WowLog.Warn("WowRaces: CharBaseInfo.dbc unreadable - using the measured race/class table");
                        foreach (var kv in MeasuredClasses) _cache[kv.Key] = new List<WowClass>(kv.Value);
                    }
                }
                List<WowClass> res;
                return _cache.TryGetValue(race, out res) ? new List<WowClass>(res) : new List<WowClass>();
            }
        }

        /// <summary>True when CharBaseInfo allows this race/class combination (and the class is offered offline).</summary>
        public static bool CanBe(WowFiles files, int race, WowClass cls) { return Classes(files, race).Contains(cls); }

        /// <summary>Racial abilities + passives of the race for the class (spell ids, actives first), read from SkillLineAbility.</summary>
        public static List<int> RacialSpells(WowFiles files, int race, WowClass cls)
        {
            if (files == null) return new List<int>();
            return SpellDb.Get(files).RacialSpells(race, cls);
        }
    }
}
