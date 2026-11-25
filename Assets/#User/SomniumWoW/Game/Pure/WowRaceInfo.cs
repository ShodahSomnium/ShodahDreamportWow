using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>One racial ability as the race screen lists it (Spell.dbc name, SpellIcon.dbc icon).</summary>
    public sealed class WowRacialSpell
    {
        public int Id;
        public string Name = "";
        public string IconPath = "";      // Interface\Icons\... (".blp" appended when missing)
        public bool Passive;
        public string Description = "";

        public override string ToString() { return Name + " (" + Id + (Passive ? ", passive" : "") + ")"; }
    }

    /// <summary>One faction as the faction screen shows it.</summary>
    public sealed class WowFactionEntry
    {
        public WowFaction Faction;
        public string Name = "";          // GlueStrings.lua ALLIANCE / HORDE
        public string Description = "";   // GlueStrings.lua FACTION_INFO_ALLIANCE / FACTION_INFO_HORDE
        public float IconLeft, IconRight, IconTop, IconBottom; // the crest in UI-CharacterCreate-Factions.blp
        public int[] Races = new int[0];
    }

    /// <summary>One race as the race screen shows it; every text and number read from the player's files (or Rules).</summary>
    public sealed class WowRaceEntry
    {
        public int Race;                  // ChrRaces id
        public WowFaction Faction;
        public string Name = "";          // ChrRaces.dbc Name_lang ("Night Elf")
        public string FileString = "";    // ChrRaces.dbc ClientFileString upper-cased ("NIGHTELF", "SCOURGE"), the glue key
        public string Description = "";   // GlueStrings.lua RACE_INFO_<FILE> (GetFlavorText: male key first, then _FEMALE)
        public List<string> AbilityLines = new List<string>(); // GlueStrings.lua ABILITY_INFO_<FILE>1..
        public float IconLeft, IconRight, IconTop, IconBottom;         // RACE_ICON_TCOORDS <FILE>_MALE
        public float FemaleLeft, FemaleRight, FemaleTop, FemaleBottom; // RACE_ICON_TCOORDS <FILE>_FEMALE
        public List<WowClass> Classes = new List<WowClass>();          // Rules: CharBaseInfo.dbc (death knight excluded)
        public Dictionary<WowClass, List<WowRacialSpell>> Racials = new Dictionary<WowClass, List<WowRacialSpell>>();
        public string ZoneName = "";      // the starting zone (the built zone's own name, else the WowZones display name)
        public int ZoneMapId = -1;
        public bool ZoneReady;            // WowZones.ForRace returned a zone at boot
        public string Sources = "";

        /// <summary>The racial abilities for this class (the race's first class when it cannot be that class).</summary>
        public List<WowRacialSpell> RacialsFor(WowClass cls)
        {
            List<WowRacialSpell> l;
            if (Racials.TryGetValue(cls, out l)) return l;
            if (Classes.Count > 0 && Racials.TryGetValue(Classes[0], out l)) return l;
            return new List<WowRacialSpell>();
        }

        public override string ToString()
        {
            var cl = new List<string>();
            foreach (WowClass c in Classes) cl.Add(c.ToString());
            var sp = new List<string>();
            foreach (WowRacialSpell s in RacialsFor(Classes.Count > 0 ? Classes[0] : WowClass.None)) sp.Add(s.Name);
            return Name + " (" + Race + ", " + FileString + ", " + Faction + "): classes " + string.Join("/", cl.ToArray()) + "; zone " + ZoneName + " map " + ZoneMapId + (ZoneReady ? "" : " (NOT READY)")
                + "; icon " + IconLeft.ToString("0.###", CultureInfo.InvariantCulture) + "," + IconRight.ToString("0.###", CultureInfo.InvariantCulture) + "," + IconTop.ToString("0.###", CultureInfo.InvariantCulture) + "," + IconBottom.ToString("0.###", CultureInfo.InvariantCulture)
                + "; racials " + string.Join(", ", sp.ToArray()) + "; " + AbilityLines.Count + " ability lines, description " + Description.Length + " chars [" + Sources + "]";
        }
    }

    /// <summary>
    /// The faction and race screens' data, read at runtime from the player's install (pure: the harness can run it; the
    /// boot thread calls it). Facts measured on the 3.3.5a files (reports3/front.md):
    /// - Race icons: CharacterCreate.xml:88 draws the race buttons from Interface\Glues\CharacterCreate\UI-CharacterCreate-Races
    ///   (512x256); CharacterCreate.lua:25 RACE_ICON_TCOORDS, keyed strupper(fileString).."_"..gender (CharacterCreate.lua:263),
    ///   e.g. ["HUMAN_MALE"] = {0, 0.125, 0, 0.25} (left, right, top, bottom).
    /// - Texts: CharacterCreate.lua:276 GetFlavorText("RACE_INFO_"..strupper(fileString), sex) and :265-272
    ///   _G["ABILITY_INFO_"..fileString..index] from 1, all in Interface\GlueXML\GlueStrings.lua.
    /// - Factions: GlueStrings.lua ALLIANCE / HORDE / FACTION_INFO_ALLIANCE / FACTION_INFO_HORDE; the crests are the two
    ///   64x64 halves of Interface\Glues\CharacterCreate\UI-CharacterCreate-Factions.blp (128x64: Alliance left, Horde right,
    ///   read off the decoded picture - no glue file of 3.3.5a names their coordinates).
    /// - Races, factions, classes, racial spells and starting zones: Rules (WowRaces from ChrRaces/CharBaseInfo, WowZones).
    /// Numbers fall back to the values quoted from CharacterCreate.lua when it cannot be read; texts fall back to empty.
    /// </summary>
    public static class WowRaceInfo
    {
        public const string RaceAtlas = @"Interface\Glues\CharacterCreate\UI-CharacterCreate-Races.blp";
        public const string FactionAtlas = @"Interface\Glues\CharacterCreate\UI-CharacterCreate-Factions.blp";

        /// <summary>Reads the ten races and two factions. Never throws; what could not be read is named in report.</summary>
        public static List<WowRaceEntry> Load(WowFiles files, out List<WowFactionEntry> factions, out string report)
        {
            var list = new List<WowRaceEntry>();
            factions = new List<WowFactionEntry>();
            var sb = new StringBuilder();
            Dictionary<string, string> strings = null;
            string create = null;
            SpellDb spells = null;

            try { strings = WowClassInfo.ParseLuaStrings(WowClassInfo.ReadText(files, WowClassInfo.GlueStringsPath)); }
            catch (Exception e) { sb.Append("GlueStrings.lua: ").Append(e.Message).Append("; "); }
            try { create = WowClassInfo.ReadText(files, WowClassInfo.CharacterCreatePath); }
            catch (Exception e) { sb.Append("CharacterCreate.lua: ").Append(e.Message).Append("; "); }
            try { spells = files != null ? SpellDb.Get(files) : null; }
            catch (Exception e) { sb.Append("SpellDb: ").Append(e.Message).Append("; "); }

            if (strings == null || strings.Count == 0) sb.Append("no glue strings - race texts empty; ");
            if (create == null) sb.Append("no " + WowClassInfo.CharacterCreatePath + " - race icon coordinates from the quoted table; ");
            if (spells == null || !spells.Available) sb.Append("no Spell.dbc - no racial ability names; ");

            // Factions.
            foreach (WowFaction f in new[] { WowFaction.Alliance, WowFaction.Horde })
            {
                string key = f == WowFaction.Horde ? "HORDE" : "ALLIANCE";
                var fe = new WowFactionEntry { Faction = f, Name = f.ToString(), Races = WowRaces.RacesOf(f) };
                string text;
                if (strings != null && strings.TryGetValue(key, out text) && text.Length > 0) fe.Name = text;
                if (strings != null && strings.TryGetValue("FACTION_INFO_" + key, out text)) fe.Description = text.Trim();
                fe.IconLeft = f == WowFaction.Horde ? 0.5f : 0f;
                fe.IconRight = f == WowFaction.Horde ? 1f : 0.5f;
                fe.IconTop = 0f;
                fe.IconBottom = 1f;
                factions.Add(fe);
            }

            // Races in screen order (Alliance then Horde).
            foreach (int race in WowRaces.Playable)
            {
                var e = new WowRaceEntry { Race = race, Faction = WowRaces.FactionOf(race) };
                var src = new StringBuilder();

                try
                {
                    e.Name = WowRaces.Name(files, race);
                    string fs = WowRaces.ClientFileString(files, race);
                    e.FileString = string.IsNullOrEmpty(fs) ? FallbackFile(race) : fs.ToUpperInvariant();
                    if (!string.IsNullOrEmpty(fs)) src.Append("dbc ");
                }
                catch (Exception ex)
                {
                    e.Name = "Race " + race;
                    e.FileString = FallbackFile(race);
                    sb.Append("ChrRaces ").Append(race).Append(": ").Append(ex.Message).Append("; ");
                }

                string text;
                if (strings != null && (strings.TryGetValue("RACE_INFO_" + e.FileString, out text) && text.Length > 0 || strings.TryGetValue("RACE_INFO_" + e.FileString + "_FEMALE", out text) && text.Length > 0))
                {
                    e.Description = text;
                    src.Append("text ");
                }

                for (int i = 1; strings != null && i < 16; i++)
                {
                    if (!strings.TryGetValue("ABILITY_INFO_" + e.FileString + i.ToString(CultureInfo.InvariantCulture), out text)) break;
                    e.AbilityLines.Add(text);
                }

                float[] q;
                if (create != null && WowClassInfo.TryTableRow(create, "RACE_ICON_TCOORDS", e.FileString + "_MALE", out q) && q.Length >= 4)
                {
                    e.IconLeft = q[0]; e.IconRight = q[1]; e.IconTop = q[2]; e.IconBottom = q[3];
                    src.Append("coords ");
                }
                else
                {
                    FallbackCoords(e.FileString, e);
                }

                if (create != null && WowClassInfo.TryTableRow(create, "RACE_ICON_TCOORDS", e.FileString + "_FEMALE", out q) && q.Length >= 4)
                {
                    e.FemaleLeft = q[0]; e.FemaleRight = q[1]; e.FemaleTop = q[2]; e.FemaleBottom = q[3];
                }
                else
                {
                    // The female faces sit two rows below the male ones (the quoted table).
                    e.FemaleLeft = e.IconLeft; e.FemaleRight = e.IconRight; e.FemaleTop = e.IconTop + 0.5f; e.FemaleBottom = e.IconBottom + 0.5f;
                }

                try
                {
                    foreach (WowClass c in WowRaces.Classes(files, race)) if (c != WowClass.DeathKnight && c != WowClass.None) e.Classes.Add(c);
                    if (e.Classes.Count > 0) src.Append("classes ");
                }
                catch (Exception ex)
                {
                    sb.Append("CharBaseInfo ").Append(race).Append(": ").Append(ex.Message).Append("; ");
                }

                if (spells != null && spells.Available)
                {
                    foreach (WowClass c in e.Classes)
                    {
                        var l = new List<WowRacialSpell>();

                        try
                        {
                            foreach (int id in WowRaces.RacialSpells(files, race, c))
                            {
                                SpellInfo s = spells.Find(id);
                                if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                                l.Add(new WowRacialSpell { Id = id, Name = s.Name, IconPath = s.IconPath ?? "", Passive = s.IsPassive, Description = s.Description ?? "" });
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.Append("racial spells ").Append(race).Append('/').Append(c).Append(": ").Append(ex.Message).Append("; ");
                        }

                        e.Racials[c] = l;
                    }

                    if (e.Racials.Count > 0) src.Append("racials ");
                }

                try
                {
                    WowZoneInfo zi = WowZones.InfoForRace(race);

                    if (zi != null)
                    {
                        e.ZoneName = zi.DisplayName ?? "";
                        e.ZoneMapId = zi.MapId;
                        ZoneContent z = WowZones.ForRace(race);
                        e.ZoneReady = z != null;
                        if (z != null && !string.IsNullOrEmpty(z.Name)) e.ZoneName = z.Name;
                        src.Append("zone");
                    }
                }
                catch (Exception ex)
                {
                    sb.Append("zone ").Append(race).Append(": ").Append(ex.Message).Append("; ");
                }

                e.Sources = src.ToString().Trim();
                list.Add(e);
            }

            int ready = 0;
            foreach (WowRaceEntry r in list) if (r.ZoneReady) ready++;
            report = list.Count + " races, " + factions.Count + " factions, " + ready + " starting zones ready" + (sb.Length > 0 ? " - " + sb.ToString().TrimEnd(' ', ';') : " (names ChrRaces.dbc, texts GlueStrings.lua, icons CharacterCreate.lua, classes CharBaseInfo.dbc, racials Spell.dbc)");
            return list;
        }

        public static WowRaceEntry Find(List<WowRaceEntry> list, int race)
        {
            if (list == null) return null;
            foreach (WowRaceEntry e in list) if (e.Race == race) return e;
            return null;
        }

        public static WowFactionEntry FindFaction(List<WowFactionEntry> list, WowFaction f)
        {
            if (list == null) return null;
            foreach (WowFactionEntry e in list) if (e.Faction == f) return e;
            return null;
        }

        private static string FallbackFile(int race)
        {
            switch (race)
            {
                case WowRaces.Human: return "HUMAN"; case WowRaces.Orc: return "ORC"; case WowRaces.Dwarf: return "DWARF"; case WowRaces.NightElf: return "NIGHTELF";
                case WowRaces.Undead: return "SCOURGE"; case WowRaces.Tauren: return "TAUREN"; case WowRaces.Gnome: return "GNOME"; case WowRaces.Troll: return "TROLL";
                case WowRaces.BloodElf: return "BLOODELF"; case WowRaces.Draenei: return "DRAENEI";
            }
            return "HUMAN";
        }

        // The <FILE>_MALE values quoted from CharacterCreate.lua (RACE_ICON_TCOORDS) - used only when the file cannot be read.
        private static void FallbackCoords(string file, WowRaceEntry e)
        {
            switch (file)
            {
                case "HUMAN": Set(e, 0f, 0.125f, 0f, 0.25f); break;
                case "DWARF": Set(e, 0.125f, 0.25f, 0f, 0.25f); break;
                case "GNOME": Set(e, 0.25f, 0.375f, 0f, 0.25f); break;
                case "NIGHTELF": Set(e, 0.375f, 0.5f, 0f, 0.25f); break;
                case "TAUREN": Set(e, 0f, 0.125f, 0.25f, 0.5f); break;
                case "SCOURGE": Set(e, 0.125f, 0.25f, 0.25f, 0.5f); break;
                case "TROLL": Set(e, 0.25f, 0.375f, 0.25f, 0.5f); break;
                case "ORC": Set(e, 0.375f, 0.5f, 0.25f, 0.5f); break;
                case "BLOODELF": Set(e, 0.5f, 0.625f, 0.25f, 0.5f); break;
                case "DRAENEI": Set(e, 0.5f, 0.625f, 0f, 0.25f); break;
                default: Set(e, 0f, 0.125f, 0f, 0.25f); break;
            }
        }

        private static void Set(WowRaceEntry e, float l, float r, float t, float b)
        {
            e.IconLeft = l; e.IconRight = r; e.IconTop = t; e.IconBottom = b;
        }
    }
}
