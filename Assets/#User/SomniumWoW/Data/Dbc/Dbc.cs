using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>
    /// Loads DBFilesClient\&lt;name&gt;.dbc through WowFiles, parsed once and cached per WowFiles instance (thread-safe).
    /// Dbc.Load(files, "Map") / Dbc.Load(files, "Map.dbc") / Dbc.Load(files, @"DBFilesClient\Map.dbc").
    /// Returns null (and logs once) when the file is missing or malformed. Checks the field count against
    /// DbcFields when a class of that name exists there (logs a warning on mismatch).
    /// </summary>
    public static class Dbc
    {
        /// <summary>Locale slot of a WoW locale inside a 3.3.5a locstring (see DbcTable).</summary>
        public static int LocaleSlot(string locale)
        {
            switch (locale)
            {
                case "koKR": return 1;
                case "frFR": return 2;
                case "deDE": return 3;
                case "zhCN": case "enCN": return 4;
                case "zhTW": case "enTW": return 5;
                case "esES": return 6;
                case "esMX": return 7;
                case "ruRU": return 8;
                default: return 0;   // enUS, enGB
            }
        }

        public static string PathOf(string name)
        {
            string n = name.Replace('/', '\\');
            if (!n.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase)) n += ".dbc";
            if (n.IndexOf('\\') < 0) n = "DBFilesClient\\" + n;
            return n;
        }

        /// <summary>The parsed table, cached by name on this WowFiles, or null when missing/malformed.</summary>
        public static DbcTable Load(WowFiles files, string name)
        {
            if (files == null || string.IsNullOrEmpty(name)) return null;
            string path = PathOf(name);
            string key = path.ToLowerInvariant();
            var cache = files.DbcCache;
            lock (cache)
            {
                DbcTable cached;
                if (cache.TryGetValue(key, out cached)) return cached;
            }
            DbcTable t = null;
            var bytes = files.Read(path);
            if (bytes == null)
            {
                WowLog.Warn("DBC: " + path + " not found");
            }
            else
            {
                try
                {
                    string shortName = System.IO.Path.GetFileNameWithoutExtension(path);
                    t = DbcTable.Parse(bytes, shortName);
                    t.LocaleSlot = LocaleSlot(files.Locale);
                    int expected = DbcFields.ExpectedFieldCount(shortName);
                    if (expected > 0 && expected != t.FieldCount)
                        WowLog.Warn("DBC: " + shortName + " has " + t.FieldCount + " fields, DbcFields expects " + expected + " (wrong build?)");
                    if (!t.FieldsAre4Bytes && !DbcFields.IsByteTable(shortName))
                        WowLog.Warn("DBC: " + shortName + " record size " + t.RecordSize + " != fields*4 (" + t.FieldCount * 4 + ")");
                }
                catch (Exception e)
                {
                    WowLog.Warn("DBC: " + path + " unreadable: " + e.Message);
                    t = null;
                }
            }
            lock (cache)
            {
                DbcTable raced;
                if (cache.TryGetValue(key, out raced)) return raced;
                cache[key] = t;
            }
            return t;
        }
    }
}
