using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>One offline starting zone: its builder, map and the races that start there.</summary>
    public sealed class WowZoneInfo
    {
        public string Key;                 // "Northshire" (the builder class name)
        public string DisplayName;         // "Northshire Valley" (UI; the zone's own ZoneContent.Name wins once built)
        public int MapId;                  // Map.dbc id
        public int[] Races;                // ChrRaces ids that start here
        public WowFaction Faction;
        internal Func<ZoneContent> Builder;
    }

    /// <summary>
    /// Race -> starting zone registry (iteration 3). Every 3.3.5a race starts in its real starting zone: Human Northshire Valley (map 0),
    /// Dwarf + Gnome Coldridge Valley (map 0), Night Elf Shadowglen (map 1), Draenei Ammen Vale (map 530), Orc + Troll Valley of Trials
    /// (map 1), Undead Deathknell (map 0), Tauren Camp Narache (map 1), Blood Elf Sunstrider Isle (map 530). Zones are built lazily
    /// on first use and cached (ZoneContent is read-only after Build). A zone whose Build() still returns null (not written yet) is
    /// "not ready": ForRace returns null and IsReady false. Pure C#.
    /// </summary>
    public static class WowZones
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, ZoneContent> Built = new Dictionary<string, ZoneContent>();
        private static readonly HashSet<string> Failed = new HashSet<string>();

        private static readonly WowZoneInfo[] Zones =
        {
            new WowZoneInfo { Key = "Northshire", DisplayName = "Northshire Valley", MapId = 0, Races = new[] { 1 }, Faction = WowFaction.Alliance, Builder = Northshire.Build },
            new WowZoneInfo { Key = "ColdridgeValley", DisplayName = "Coldridge Valley", MapId = 0, Races = new[] { 3, 7 }, Faction = WowFaction.Alliance, Builder = ColdridgeValley.Build },
            new WowZoneInfo { Key = "Shadowglen", DisplayName = "Shadowglen", MapId = 1, Races = new[] { 4 }, Faction = WowFaction.Alliance, Builder = Shadowglen.Build },
            new WowZoneInfo { Key = "AmmenVale", DisplayName = "Ammen Vale", MapId = 530, Races = new[] { 11 }, Faction = WowFaction.Alliance, Builder = AmmenVale.Build },
            new WowZoneInfo { Key = "ValleyOfTrials", DisplayName = "Valley of Trials", MapId = 1, Races = new[] { 2, 8 }, Faction = WowFaction.Horde, Builder = ValleyOfTrials.Build },
            new WowZoneInfo { Key = "Deathknell", DisplayName = "Deathknell", MapId = 0, Races = new[] { 5 }, Faction = WowFaction.Horde, Builder = Deathknell.Build },
            new WowZoneInfo { Key = "CampNarache", DisplayName = "Camp Narache", MapId = 1, Races = new[] { 6 }, Faction = WowFaction.Horde, Builder = CampNarache.Build },
            new WowZoneInfo { Key = "SunstriderIsle", DisplayName = "Sunstrider Isle", MapId = 530, Races = new[] { 10 }, Faction = WowFaction.Horde, Builder = SunstriderIsle.Build },
        };

        /// <summary>All 8 starting zones (name, map, races), Alliance first.</summary>
        public static IReadOnlyList<WowZoneInfo> All { get { return Zones; } }

        /// <summary>The registry entry of the race's starting zone (null for unknown races).</summary>
        public static WowZoneInfo InfoForRace(int race)
        {
            foreach (var z in Zones) if (Array.IndexOf(z.Races, race) >= 0) return z;
            return null;
        }

        /// <summary>The registry entry by key ("Shadowglen") or by the built zone's Name, or null.</summary>
        public static WowZoneInfo Info(string keyOrName)
        {
            if (string.IsNullOrEmpty(keyOrName)) return null;
            foreach (var z in Zones) if (z.Key == keyOrName || z.DisplayName == keyOrName) return z;
            return null;
        }

        /// <summary>The zone's display name for the race screen ("Northshire Valley").</summary>
        public static string DisplayNameForRace(int race)
        {
            var z = InfoForRace(race);
            return z != null ? z.DisplayName : "";
        }

        /// <summary>The race's starting zone, built once and cached; null while that zone's Build() still returns null.</summary>
        public static ZoneContent ForRace(int race)
        {
            var z = InfoForRace(race);
            return z != null ? Get(z) : null;
        }

        /// <summary>True when the race's zone is written (Build() returns a zone).</summary>
        public static bool IsReady(int race) { return ForRace(race) != null; }

        /// <summary>The built zone of a registry entry (cached), or null when not written yet / its builder threw (logged once).</summary>
        public static ZoneContent Get(WowZoneInfo info)
        {
            if (info == null) return null;
            lock (Lock)
            {
                ZoneContent zc;
                if (Built.TryGetValue(info.Key, out zc)) return zc;
                if (Failed.Contains(info.Key)) return null;
            }
            ZoneContent built = null;
            try { built = info.Builder(); }
            catch (Exception e)
            {
                WowLog.Error("WowZones: " + info.Key + ".Build threw: " + e);
                lock (Lock) Failed.Add(info.Key);
                return null;
            }
            if (built == null) return null;   // not written yet: ask again next time (cheap)
            lock (Lock)
            {
                ZoneContent zc;
                if (Built.TryGetValue(info.Key, out zc)) return zc;
                Built[info.Key] = built;
            }
            WowLog.Info("WowZones: " + info.Key + " built - '" + built.Name + "' map " + built.MapId + ", " + built.Creatures.Count + " creature templates, "
                + built.Spawns.Count + " spawns, " + built.Quests.Count + " quests, " + built.ClassStarts.Count + " class starts");
            return built;
        }

        /// <summary>The registry entry whose built zone is this instance (or whose map + name match), or null.</summary>
        public static WowZoneInfo InfoOf(ZoneContent zone)
        {
            if (zone == null) return null;
            lock (Lock) { foreach (var kv in Built) if (ReferenceEquals(kv.Value, zone)) return Info(kv.Key); }
            foreach (var z in Zones) if (z.MapId == zone.MapId && (z.DisplayName == zone.Name || z.Key == zone.Name)) return z;
            return null;
        }
    }
}
