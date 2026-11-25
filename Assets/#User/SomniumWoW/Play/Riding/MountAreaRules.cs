using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (lead contract stub, PLAN7 s3.4; bodies by Rules). Where a flying mount may fly and whether a terrain area counts as
    // outdoors - AzerothCore's rules (quoted below) on the player's own AreaTable.dbc and WorldMapArea.dbc. Pure, thread-safe after Load.
    public static class MountAreaRules
    {
        // AreaTable.dbc: DbcFields.AreaTable (ID 0, ContinentID 1, ParentAreaID 2, Flags 4, AreaName_lang 11) = AC DBCStructure.h
        // AreaTableEntry (mapid 1, zone 2 "if 0 then it's zone, else it's zone id of this area", flags 4, area_name 11-26).
        // WorldMapArea.dbc (not in DbcFields; rule 12): AC DBCStructure.h WorldMapAreaEntry "map_id; // 1", "area_id; // 2 index (continent 0
        // areas ignored)", "int32 virtual_map_id; // 8 -1 (map_id have correct map) other: virtual map where zone show".
        private const int WmaMapField = 1, WmaAreaField = 2, WmaVirtualMapField = 8;
        // AC DBCEnums.h AreaFlags.
        private const uint AreaFlagOutland = 0x00000400u;        // AREA_FLAG_OUTLAND, AreaTableEntry::IsFlyable() = flags & AREA_FLAG_OUTLAND
        private const uint AreaFlagInside = 0x02000000u;         // AREA_FLAG_INSIDE
        private const uint AreaFlagOutside = 0x04000000u;        // AREA_FLAG_OUTSIDE
        private const uint AreaFlagNoFlyZone = 0x20000000u;      // AREA_FLAG_NO_FLY_ZONE "Marks zones where you cannot fly"
        private const int MapOutland = 530, MapNorthrend = 571;  // AC MAP_OUTLAND / MAP_NORTHREND

        private struct Area { public int Map, Parent; public uint Flags; public string Name; }
        private struct Wma { public int Map, VirtualMap; }

        private sealed class Tables
        {
            public readonly Dictionary<int, Area> Areas = new Dictionary<int, Area>();
            public readonly Dictionary<int, Wma> WorldMap = new Dictionary<int, Wma>();   // by area (zone) id
        }

        private static readonly object Gate = new object();
        private static WowFiles _files;
        private static volatile Tables _t;
        private static bool _warnedNotLoaded;

        public static bool Loaded { get { var t = _t; return t != null && t.Areas.Count > 0; } }

        public static void Load(WowFiles files)                               // AreaTable + WorldMapArea, worker thread
        {
            if (files == null) return;
            lock (Gate)
            {
                if (_t != null && ReferenceEquals(_files, files)) return;
                var t0 = DateTime.UtcNow;
                var t = new Tables();
                try
                {
                    var at = Dbc.Load(files, "AreaTable");
                    var wma = Dbc.Load(files, "WorldMapArea");
                    int flyable = 0, noFly = 0, insideOnly = 0;
                    if (at != null)
                        for (int r = 0; r < at.RecordCount; r++)
                        {
                            var a = new Area
                            {
                                Map = at.GetInt(r, DbcFields.AreaTable.ContinentID),
                                Parent = at.GetInt(r, DbcFields.AreaTable.ParentAreaID),
                                Flags = at.GetUInt(r, DbcFields.AreaTable.Flags),
                                Name = at.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang) ?? ""
                            };
                            t.Areas[at.GetId(r)] = a;
                            if ((a.Flags & AreaFlagOutland) != 0) flyable++;
                            if ((a.Flags & AreaFlagNoFlyZone) != 0) noFly++;
                            if ((a.Flags & (AreaFlagInside | AreaFlagOutside)) == AreaFlagInside) insideOnly++;
                        }
                    int virt = 0;
                    if (wma != null && wma.FieldCount > WmaVirtualMapField)
                        for (int r = 0; r < wma.RecordCount; r++)
                        {
                            int area = wma.GetInt(r, WmaAreaField);
                            if (area == 0) continue;                              // continent rows are ignored by AC's index
                            var w = new Wma { Map = wma.GetInt(r, WmaMapField), VirtualMap = wma.GetInt(r, WmaVirtualMapField) };
                            t.WorldMap[area] = w;
                            if (w.VirtualMap >= 0) virt++;
                        }
                    WowLog.Info("MountAreaRules: AreaTable " + t.Areas.Count + " areas (" + flyable + " flyable 0x400, " + noFly + " no-fly 0x20000000, " + insideOnly
                        + " inside-only), WorldMapArea " + t.WorldMap.Count + " zones (" + virt + " with a virtual map), " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms"
                        + (at == null ? " - NO AreaTable.dbc: flying mounts cannot fly (FlyAnywhere off)" : "") + (wma == null ? " - no WorldMapArea.dbc" : ""));
                }
                catch (Exception e)
                {
                    WowLog.Error("MountAreaRules: reading AreaTable / WorldMapArea failed: " + e);
                }
                _files = files;
                _t = t;
            }
        }

        /// <summary>AC SpellInfo::CheckLocation (SPELL_ATTR4_ONLY_FLYING_AREAS) + Player::canFlyInZone + GetVirtualMapForMapAndZone.
        /// strict = the cast check (also refuses AREA_FLAG_NO_FLY_ZONE 0x20000000); non-strict = UpdateAreaDependentAuras.</summary>
        public static bool CanFlyHere(int mapId, int areaId, bool coldWeatherFlying, bool strict)
        {
            var t = _t;
            if (t == null || t.Areas.Count == 0)
            {
                if (!_warnedNotLoaded) { _warnedNotLoaded = true; WowLog.Warn("MountAreaRules: CanFlyHere asked before the area tables were read - answering 'no'"); }
                return false;
            }
            int zoneId = ZoneOf(areaId);
            // SpellInfo.cpp 1546: `if (HasAttribute(SPELL_ATTR4_ONLY_FLYING_AREAS) && (area_id || zone_id))` - no area at all skips the check.
            if (areaId <= 0 && zoneId <= 0) return true;
            // 1548-1552: `areaEntry = sAreaTableStore.LookupEntry(area_id); if (!areaEntry) areaEntry = sAreaTableStore.LookupEntry(zone_id);`
            Area a;
            if (!t.Areas.TryGetValue(areaId, out a) && !t.Areas.TryGetValue(zoneId, out a)) return false;
            // 1554: `if (!areaEntry || !areaEntry->IsFlyable() || (strict && (areaEntry->flags & AREA_FLAG_NO_FLY_ZONE) != 0) || !player->canFlyInZone(map_id, zone_id, this))`
            if ((a.Flags & AreaFlagOutland) == 0) return false;
            if (strict && (a.Flags & AreaFlagNoFlyZone) != 0) return false;
            // Player.cpp 13989-13995: `v_map = GetVirtualMapForMapAndZone(mapid, zone); if (v_map == MAP_NORTHREND && !...IGNORES_COLD_WEATHER...)
            // { if (!HasSpell(54197)) return false; }` - the caller folds SPELL_ATTR7_IGNORES_COLD_WEATHER_FLYING_REQUIREMENT into coldWeatherFlying.
            if (VirtualMap(t, mapId, zoneId) == MapNorthrend && !coldWeatherFlying) return false;
            return true;
        }

        public static int ZoneOf(int areaId)                                  // the top parent (AreaTable ParentAreaID chain)
        {
            var t = _t;
            if (t == null || areaId <= 0) return areaId;
            int id = areaId;
            for (int guard = 0; guard < 16; guard++)
            {
                Area a;
                if (!t.Areas.TryGetValue(id, out a) || a.Parent <= 0 || a.Parent == id) return id;
                id = a.Parent;
            }
            return id;
        }

        public static bool TerrainOutdoors(int areaId)                        // AC: (flags & (INSIDE|OUTSIDE)) != INSIDE
        {
            var t = _t;
            Area a;
            if (t == null || !t.Areas.TryGetValue(areaId, out a)) return true;
            // Map.cpp (no WMO under the unit): `data.outdoors = (areaEntry->flags & (AREA_FLAG_INSIDE | AREA_FLAG_OUTSIDE)) != AREA_FLAG_INSIDE;`
            return (a.Flags & (AreaFlagInside | AreaFlagOutside)) != AreaFlagInside;
        }

        public static string AreaName(int areaId)
        {
            var t = _t;
            Area a;
            return t != null && t.Areas.TryGetValue(areaId, out a) ? a.Name : "";
        }

        /// <summary>DBCStores.cpp 715-724: maps other than 530 / 571 are their own; there the zone's WorldMapArea row gives
        /// `wma->virtual_map_id >= 0 ? wma->virtual_map_id : wma->map_id`, else the map itself.</summary>
        private static int VirtualMap(Tables t, int mapId, int zoneId)
        {
            if (mapId != MapOutland && mapId != MapNorthrend) return mapId;
            Wma w;
            if (t.WorldMap.TryGetValue(zoneId, out w)) return w.VirtualMap >= 0 ? w.VirtualMap : w.Map;
            return mapId;
        }
    }
}
