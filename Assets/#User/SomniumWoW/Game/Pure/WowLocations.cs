using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>A place the room panel offers: a row of the game's own TaxiNodes.dbc or WorldSafeLocs.dbc.</summary>
    public sealed class WowLocation
    {
        public string Label;     // the button text
        public string Source;    // "TaxiNodes 2" / "WorldSafeLocs 105"
        public string DbcName;   // the row's enUS name
        public int MapId;
        public string MapName;   // Map.dbc MapName_lang
        public float X, Y, Z;    // WoW yards on the map

        public override string ToString()
        {
            return Label + " (" + Source + " '" + DbcName + "', map " + MapId + " " + MapName + ", " + X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                   Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ", " + Z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// The curated shortlist (pure: the harness runs it). Every entry is a row id verified with the harness probe
    /// FrontLocations on the 3.3.5a files (see reports/front.md): flight masters from TaxiNodes.dbc where the place has
    /// one, else a WorldSafeLocs.dbc graveyard (Northshire, Goldshire, Darnassus have no flight master). Neither table
    /// stores a facing; the arrival picks the direction with free level ground for the line-up.
    /// </summary>
    public static class WowLocations
    {
        private struct Pick
        {
            public string Label, Table;
            public int Id;
            public Pick(string label, string table, int id) { Label = label; Table = table; Id = id; }
        }

        private static readonly Pick[] Curated =
        {
            new Pick("Northshire Valley", "WorldSafeLocs", 105), // 'Elwynn Forest, Northshire'
            new Pick("Goldshire", "WorldSafeLocs", 106),         // 'Elwynn Forest, Goldshire'
            new Pick("Stormwind", "TaxiNodes", 2),               // 'Stormwind, Elwynn'
            new Pick("Ironforge", "TaxiNodes", 6),               // 'Ironforge, Dun Morogh'
            new Pick("Darnassus", "WorldSafeLocs", 90),          // 'Teldrassil, Darnassus'
            new Pick("The Exodar", "TaxiNodes", 94),             // 'The Exodar'
            new Pick("Booty Bay", "TaxiNodes", 18),              // 'Booty Bay, Stranglethorn'
            new Pick("Orgrimmar", "TaxiNodes", 23),              // 'Orgrimmar, Durotar'
            new Pick("Thunder Bluff", "TaxiNodes", 22),          // 'Thunder Bluff, Mulgore'
            new Pick("Undercity", "TaxiNodes", 11),              // 'Undercity, Tirisfal'
            new Pick("Silvermoon City", "TaxiNodes", 82),        // 'Silvermoon City'
            new Pick("The Crossroads", "TaxiNodes", 25),         // 'Crossroads, The Barrens'
            new Pick("Shattrath", "TaxiNodes", 128),             // 'Shattrath, Terokkar Forest'
            new Pick("Dalaran", "TaxiNodes", 310),               // 'Dalaran'
            new Pick("Valiance Keep", "TaxiNodes", 245),         // 'Valiance Keep, Borean Tundra'
        };

        /// <summary>Resolves the shortlist against the DBCs. Rows that are missing are left out and named in report.</summary>
        public static List<WowLocation> Load(WowFiles files, out string report)
        {
            var list = new List<WowLocation>();
            var sb = new StringBuilder();
            DbcTable taxi = Dbc.Load(files, "TaxiNodes");
            DbcTable safe = Dbc.Load(files, "WorldSafeLocs");
            DbcTable map = Dbc.Load(files, "Map");

            foreach (Pick p in Curated)
            {
                bool isTaxi = p.Table == "TaxiNodes";
                DbcTable t = isTaxi ? taxi : safe;
                int row = t != null ? t.FindRow(p.Id) : -1;

                if (row < 0)
                {
                    sb.Append("missing ").Append(p.Table).Append(' ').Append(p.Id).Append(" (").Append(p.Label).Append("); ");
                    continue;
                }

                var loc = new WowLocation
                {
                    Label = p.Label,
                    Source = p.Table + " " + p.Id,
                    DbcName = isTaxi ? t.GetLocalizedString(row, DbcFields.TaxiNodes.Name_lang) : t.GetLocalizedString(row, DbcFields.WorldSafeLocs.AreaName_lang),
                    MapId = isTaxi ? t.GetInt(row, DbcFields.TaxiNodes.ContinentID) : t.GetInt(row, DbcFields.WorldSafeLocs.Continent),
                    X = t.GetFloat(row, isTaxi ? DbcFields.TaxiNodes.Pos : DbcFields.WorldSafeLocs.Loc),
                    Y = t.GetFloat(row, (isTaxi ? DbcFields.TaxiNodes.Pos : DbcFields.WorldSafeLocs.Loc) + 1),
                    Z = t.GetFloat(row, (isTaxi ? DbcFields.TaxiNodes.Pos : DbcFields.WorldSafeLocs.Loc) + 2),
                };

                int mr = map != null ? map.FindRow(loc.MapId) : -1;
                loc.MapName = mr >= 0 ? map.GetLocalizedString(mr, DbcFields.Map.MapName_lang) : "map " + loc.MapId;

                if (loc.X == 0f && loc.Y == 0f)
                {
                    sb.Append("no position ").Append(loc.Source).Append("; ");
                    continue;
                }

                list.Add(loc);
            }

            report = list.Count + " of " + Curated.Length + " locations" + (sb.Length > 0 ? " - " + sb.ToString().TrimEnd(' ', ';') : "");
            return list;
        }

        /// <summary>Map.dbc MapName_lang of a map ("Kalimdor"), or "map N".</summary>
        public static string MapName(WowFiles files, int mapId)
        {
            try
            {
                DbcTable map = files != null ? Dbc.Load(files, "Map") : null;
                int mr = map != null ? map.FindRow(mapId) : -1;
                string n = mr >= 0 ? map.GetLocalizedString(mr, DbcFields.Map.MapName_lang) : null;
                return string.IsNullOrEmpty(n) ? "map " + mapId : n;
            }
            catch (Exception)
            {
                return "map " + mapId;
            }
        }

        /// <summary>Map.dbc -> LoadingScreens.dbc: the loading picture of a map (the widescreen file when the row has one), or null.</summary>
        public static string LoadingScreenOf(WowFiles files, int mapId, bool wide, out string why)
        {
            why = null;
            DbcTable map = Dbc.Load(files, "Map");
            DbcTable ls = Dbc.Load(files, "LoadingScreens");

            if (map == null || ls == null)
            {
                why = "Map.dbc or LoadingScreens.dbc unreadable";
                return null;
            }

            int mr = map.FindRow(mapId);
            int id = mr >= 0 ? map.GetInt(mr, DbcFields.Map.LoadingScreenID) : 0;
            int lr = ls.FindRow(id);

            if (lr < 0)
            {
                why = "map " + mapId + " LoadingScreenID " + id + " has no LoadingScreens row";
                return null;
            }

            string file = ls.GetString(lr, DbcFields.LoadingScreens.FileName);

            if (string.IsNullOrEmpty(file))
            {
                why = "LoadingScreens " + id + " has no file";
                return null;
            }

            if (!file.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
            {
                file += ".blp";
            }

            // Measured (probe FrontLoading): the four continents' rows have HasWideScreen 1 and "<name>Wide.blp" exists.
            if (wide && ls.GetInt(lr, DbcFields.LoadingScreens.HasWideScreen) != 0)
            {
                string w = file.Substring(0, file.Length - 4) + "Wide.blp";

                if (files.Exists(w))
                {
                    why = "LoadingScreens " + id + " (widescreen)";
                    return w;
                }
            }

            why = "LoadingScreens " + id;
            return files.Exists(file) ? file : null;
        }
    }
}
