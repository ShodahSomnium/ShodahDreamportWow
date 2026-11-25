using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>One class as the class creation screen shows it; every text and number read from the player's files.</summary>
    public sealed class WowClassEntry
    {
        public WowClass Class;
        public string Name = "";          // ChrClasses.dbc Name_lang ("Warrior")
        public string FileName = "";      // ChrClasses.dbc Filename ("WARRIOR"), the key of the glue tables
        public string Description = "";   // GlueStrings.lua CLASS_<FILE> (GetFlavorText: male key first, then _FEMALE)
        public List<string> InfoLines = new List<string>(); // GlueStrings.lua CLASS_INFO_<FILE>0.. ("- Role: Tank, Damage")
        public float IconLeft, IconRight, IconTop, IconBottom; // CharacterCreate.lua CLASS_ICON_TCOORDS (left, right, top, bottom)
        public float R = 1f, G = 0.82f, B = 0f;                // FrameXML\Constants.lua RAID_CLASS_COLORS
        public string Sources = "";

        public override string ToString()
        {
            return Name + " (" + FileName + "): icon " + IconLeft.ToString("0.###", CultureInfo.InvariantCulture) + "," + IconRight.ToString("0.###", CultureInfo.InvariantCulture) + ","
                + IconTop.ToString("0.###", CultureInfo.InvariantCulture) + "," + IconBottom.ToString("0.###", CultureInfo.InvariantCulture)
                + " colour " + R.ToString("0.00", CultureInfo.InvariantCulture) + "/" + G.ToString("0.00", CultureInfo.InvariantCulture) + "/" + B.ToString("0.00", CultureInfo.InvariantCulture)
                + ", " + InfoLines.Count + " info lines, description " + Description.Length + " chars [" + Sources + "]";
        }
    }

    /// <summary>
    /// The class creation screen's data, read at runtime from the player's install (pure: the harness runs it). Facts
    /// measured on the 3.3.5a files (reports2/front.md):
    /// - Icon atlas Interface\Glues\CharacterCreate\UI-CharacterCreate-Classes.blp (locale-enUS.MPQ, 256x256), the
    ///   texture of CharacterCreate.xml's CharacterCreateClassButtonTemplate; its coordinates are the table
    ///   CLASS_ICON_TCOORDS in Interface\GlueXML\CharacterCreate.lua (patch-enUS-2.MPQ) line 52, e.g.
    ///   ["WARRIOR"] = {0, 0.25, 0, 0.25} (left, right, top, bottom).
    /// - Descriptions: CharacterCreate.lua shows GetFlavorText("CLASS_"..strupper(classFileName), sex) and the lines
    ///   _G["CLASS_INFO_"..classFileName..index] from index 0; both live in Interface\GlueXML\GlueStrings.lua
    ///   (patch-enUS-3.MPQ, 874 lines of the form KEY = "value";), e.g. CLASS_WARRIOR, CLASS_INFO_WARRIOR0.
    /// - Class colours: RAID_CLASS_COLORS in Interface\FrameXML\Constants.lua (patch-enUS-3.MPQ) line 54.
    /// - Names and file names: ChrClasses.dbc Name_lang / Filename.
    /// Numbers (coordinates, colours) fall back to the values quoted above when a file cannot be read; texts fall back
    /// to the class name only (never to invented or copied text).
    /// </summary>
    public static class WowClassInfo
    {
        public const string IconAtlas = @"Interface\Glues\CharacterCreate\UI-CharacterCreate-Classes.blp";
        public const string Highlights = @"Interface\Glues\CharacterCreate\UI-CharacterCreate-Highlights.blp";
        public const string IconShadow = @"Interface\Glues\CharacterCreate\UI-CharacterCreate-IconShadow.blp";
        public const string GlueStringsPath = @"Interface\GlueXML\GlueStrings.lua";
        public const string CharacterCreatePath = @"Interface\GlueXML\CharacterCreate.lua";
        public const string ConstantsPath = @"Interface\FrameXML\Constants.lua";

        /// <summary>The Night Elf classes of 3.3.5a offered here (CharBaseInfo race 4 also has the death knight, who starts at 55 elsewhere).</summary>
        public static readonly WowClass[] NightElfClasses = { WowClass.Warrior, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.Druid };

        /// <summary>Iteration 3: the nine classes playable here, in ChrClasses id order (the death knight is excluded: he starts at 55 elsewhere).</summary>
        public static readonly WowClass[] PlayableClasses = { WowClass.Warrior, WowClass.Paladin, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.Shaman, WowClass.Mage, WowClass.Warlock, WowClass.Druid };

        /// <summary>Reads the nine playable classes. Never throws; what could not be read is named in report.</summary>
        public static List<WowClassEntry> Load(WowFiles files, out string report)
        {
            var list = new List<WowClassEntry>();
            var sb = new StringBuilder();
            Dictionary<string, string> strings = null;
            string create = null, constants = null;
            DbcTable classes = null;

            try { strings = ParseLuaStrings(ReadText(files, GlueStringsPath)); }
            catch (Exception e) { sb.Append("GlueStrings.lua: ").Append(e.Message).Append("; "); }
            try { create = ReadText(files, CharacterCreatePath); }
            catch (Exception e) { sb.Append("CharacterCreate.lua: ").Append(e.Message).Append("; "); }
            try { constants = ReadText(files, ConstantsPath); }
            catch (Exception e) { sb.Append("Constants.lua: ").Append(e.Message).Append("; "); }
            try { classes = files != null ? Dbc.Load(files, "ChrClasses") : null; }
            catch (Exception e) { sb.Append("ChrClasses.dbc: ").Append(e.Message).Append("; "); }

            if (strings == null || strings.Count == 0) sb.Append("no glue strings (" + GlueStringsPath + ") - descriptions empty; ");
            if (create == null) sb.Append("no " + CharacterCreatePath + " - icon coordinates from the quoted table; ");
            if (constants == null) sb.Append("no " + ConstantsPath + " - class colours from the quoted table; ");
            if (classes == null) sb.Append("no ChrClasses.dbc - English class names; ");

            foreach (WowClass c in PlayableClasses)
            {
                var e = new WowClassEntry { Class = c, FileName = FallbackFile(c), Name = c.ToString() };
                var src = new StringBuilder();
                int row = classes != null ? classes.FindRow((int)c) : -1;

                if (row >= 0)
                {
                    string n = classes.GetLocalizedString(row, DbcFields.ChrClasses.Name_lang);
                    string f = classes.GetString(row, DbcFields.ChrClasses.Filename);
                    if (!string.IsNullOrEmpty(n)) e.Name = n;
                    if (!string.IsNullOrEmpty(f)) e.FileName = f.ToUpperInvariant();
                    src.Append("dbc ");
                }

                string text;
                if (strings != null && (strings.TryGetValue("CLASS_" + e.FileName, out text) && text.Length > 0 || strings.TryGetValue("CLASS_" + e.FileName + "_FEMALE", out text) && text.Length > 0))
                {
                    e.Description = text;
                    src.Append("text ");
                }

                for (int i = 0; strings != null && i < 16; i++)
                {
                    if (!strings.TryGetValue("CLASS_INFO_" + e.FileName + i.ToString(CultureInfo.InvariantCulture), out text)) break;
                    e.InfoLines.Add(text);
                }

                float[] q;
                if (create != null && TryTableRow(create, "CLASS_ICON_TCOORDS", e.FileName, out q) && q.Length >= 4)
                {
                    e.IconLeft = q[0]; e.IconRight = q[1]; e.IconTop = q[2]; e.IconBottom = q[3];
                    src.Append("coords ");
                }
                else
                {
                    FallbackCoords(c, e);
                }

                float[] rgb;
                if (constants != null && TryColour(constants, e.FileName, out rgb))
                {
                    e.R = rgb[0]; e.G = rgb[1]; e.B = rgb[2];
                    src.Append("colour");
                }
                else
                {
                    FallbackColour(c, e);
                }

                e.Sources = src.ToString().Trim();
                list.Add(e);
            }

            report = list.Count + " classes" + (sb.Length > 0 ? " - " + sb.ToString().TrimEnd(' ', ';') : " (names ChrClasses.dbc, texts GlueStrings.lua, icons CharacterCreate.lua, colours Constants.lua)");
            return list;
        }

        public static WowClassEntry Find(List<WowClassEntry> list, WowClass c)
        {
            if (list == null) return null;
            foreach (WowClassEntry e in list) if (e.Class == c) return e;
            return null;
        }

        public static string ReadText(WowFiles files, string path)
        {
            if (files == null) return null;
            byte[] b = files.Read(path);
            if (b == null || b.Length == 0) return null;
            int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(b, start, b.Length - start);
        }

        /// <summary>
        /// Lines of the form <c>KEY = "value";</c> (GlueStrings.lua is generated that way). Lua escapes \n \" \\ \t are
        /// decoded; WoW's |n becomes a line break and colour codes (|cAARRGGBB ... |r) are dropped.
        /// </summary>
        public static Dictionary<string, string> ParseLuaStrings(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return d;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r', ' ', '\t');
                int eq = line.IndexOf(" = \"", StringComparison.Ordinal);
                if (eq <= 0 || !line.EndsWith("\";", StringComparison.Ordinal)) continue;
                string key = line.Substring(0, eq).Trim();
                if (key.Length == 0 || !IsIdentifier(key)) continue;
                string body = line.Substring(eq + 4, line.Length - eq - 4 - 2);
                d[key] = CleanUiText(Unescape(body));
            }

            return d;
        }

        private static bool IsIdentifier(string s)
        {
            foreach (char c in s) if (!(c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            return true;
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>WoW UI escapes: |n line break, || a bar, |cAARRGGBB and |r colour codes removed (the labels have no rich text).</summary>
        public static string CleanUiText(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('|') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '|' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[i + 1];
                if (n == 'n') { sb.Append('\n'); i++; }
                else if (n == '|') { sb.Append('|'); i++; }
                else if (n == 'r') { i++; }
                else if (n == 'c' && i + 9 < s.Length) { i += 9; }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>In a Lua table <c>NAME = { ["KEY"] = {a, b, c, d}, ... }</c>, the numbers of KEY's row.</summary>
        public static bool TryTableRow(string lua, string table, string key, out float[] values)
        {
            values = null;
            string block = Block(lua, table);
            if (block == null) return false;
            int k = block.IndexOf("[\"" + key + "\"]", StringComparison.Ordinal);
            if (k < 0) return false;
            int open = block.IndexOf('{', k);
            int close = open >= 0 ? block.IndexOf('}', open) : -1;
            if (open < 0 || close < 0) return false;
            var parts = block.Substring(open + 1, close - open - 1).Split(',');
            var list = new List<float>();
            foreach (string p in parts)
            {
                float v;
                if (float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) list.Add(v);
            }
            values = list.ToArray();
            return values.Length > 0;
        }

        /// <summary>RAID_CLASS_COLORS row <c>["HUNTER"] = { r = 0.67, g = 0.83, b = 0.45 },</c>.</summary>
        public static bool TryColour(string lua, string key, out float[] rgb)
        {
            rgb = null;
            string block = Block(lua, "RAID_CLASS_COLORS");
            if (block == null) return false;
            int k = block.IndexOf("[\"" + key + "\"]", StringComparison.Ordinal);
            if (k < 0) return false;
            int open = block.IndexOf('{', k);
            int close = open >= 0 ? block.IndexOf('}', open) : -1;
            if (open < 0 || close < 0) return false;
            float r = -1f, g = -1f, b = -1f;
            foreach (string p in block.Substring(open + 1, close - open - 1).Split(','))
            {
                int eq = p.IndexOf('=');
                if (eq < 0) continue;
                string name = p.Substring(0, eq).Trim();
                float v;
                if (!float.TryParse(p.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (name == "r") r = v; else if (name == "g") g = v; else if (name == "b") b = v;
            }
            if (r < 0f || g < 0f || b < 0f) return false;
            rgb = new[] { r, g, b };
            return true;
        }

        private static string Block(string lua, string table)
        {
            int t = lua.IndexOf(table + " = {", StringComparison.Ordinal);
            if (t < 0) return null;
            int end = lua.IndexOf("\n}", t, StringComparison.Ordinal);
            return end < 0 ? lua.Substring(t) : lua.Substring(t, end - t);
        }

        private static string FallbackFile(WowClass c)
        {
            return c.ToString().ToUpperInvariant();
        }

        // The values quoted from CharacterCreate.lua (CLASS_ICON_TCOORDS) - used only when the file cannot be read.
        private static void FallbackCoords(WowClass c, WowClassEntry e)
        {
            switch (c)
            {
                case WowClass.Warrior: Set(e, 0f, 0.25f, 0f, 0.25f); break;
                case WowClass.Rogue: Set(e, 0.49609375f, 0.7421875f, 0f, 0.25f); break;
                case WowClass.Druid: Set(e, 0.7421875f, 0.98828125f, 0f, 0.25f); break;
                case WowClass.Hunter: Set(e, 0f, 0.25f, 0.25f, 0.5f); break;
                case WowClass.Priest: Set(e, 0.49609375f, 0.7421875f, 0.25f, 0.5f); break;
                case WowClass.Mage: Set(e, 0.25f, 0.49609375f, 0f, 0.25f); break;
                case WowClass.Shaman: Set(e, 0.25f, 0.49609375f, 0.25f, 0.5f); break;
                case WowClass.Warlock: Set(e, 0.7421875f, 0.98828125f, 0.25f, 0.5f); break;
                case WowClass.Paladin: Set(e, 0f, 0.25f, 0.5f, 0.75f); break;
                default: Set(e, 0f, 0.25f, 0f, 0.25f); break;
            }
        }

        private static void Set(WowClassEntry e, float l, float r, float t, float b)
        {
            e.IconLeft = l; e.IconRight = r; e.IconTop = t; e.IconBottom = b;
        }

        // The values quoted from FrameXML\Constants.lua (RAID_CLASS_COLORS) - used only when the file cannot be read.
        private static void FallbackColour(WowClass c, WowClassEntry e)
        {
            switch (c)
            {
                case WowClass.Warrior: e.R = 0.78f; e.G = 0.61f; e.B = 0.43f; break;
                case WowClass.Hunter: e.R = 0.67f; e.G = 0.83f; e.B = 0.45f; break;
                case WowClass.Rogue: e.R = 1.0f; e.G = 0.96f; e.B = 0.41f; break;
                case WowClass.Priest: e.R = 1.0f; e.G = 1.0f; e.B = 1.0f; break;
                case WowClass.Druid: e.R = 1.0f; e.G = 0.49f; e.B = 0.04f; break;
                case WowClass.Warlock: e.R = 0.58f; e.G = 0.51f; e.B = 0.79f; break;
                case WowClass.Paladin: e.R = 0.96f; e.G = 0.55f; e.B = 0.73f; break;
                case WowClass.Mage: e.R = 0.41f; e.G = 0.8f; e.B = 0.94f; break;
                case WowClass.Shaman: e.R = 0.0f; e.G = 0.44f; e.B = 0.87f; break;
            }
        }
    }
}
