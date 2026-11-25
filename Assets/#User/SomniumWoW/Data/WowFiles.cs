using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SomniumSpace.Worlds.SomniumWoW.Data.Mpq;

namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>
    /// A read-only, load-ordered view over the client's MPQ archives, exactly as the 3.3.5a client resolves a file
    /// (patches over base, locale over neutral, delete markers hide lower copies). Reads are thread-safe.
    /// Order: see ArchiveOrder (highest priority first) and the Data report.
    /// </summary>
    public sealed class WowFiles : IDisposable
    {
        /// <summary>The install folder (the one holding Wow.exe and Data\).</summary>
        public string Root { get; private set; }

        /// <summary>The locale folder in use, e.g. "enUS".</summary>
        public string Locale { get; private set; }

        private readonly List<MpqArchive> _archives = new List<MpqArchive>();   // highest priority first
        private readonly object _listLock = new object();
        private List<string> _listFile;

        /// <summary>Parsed DBCs of this instance (Dbc.Load's cache; lock it before use).</summary>
        internal readonly Dictionary<string, DbcTable> DbcCache = new Dictionary<string, DbcTable>();

        /// <summary>The opened archives, highest priority first.</summary>
        public IReadOnlyList<MpqArchive> Archives { get { return _archives; } }

        /// <summary>
        /// Archive file names relative to Data\, highest priority first ("{0}" = locale). Files that do not exist are
        /// skipped. Custom patches (patch-X.MPQ / patch-xxXX-X.MPQ, X = 4..9 or A..Z) are inserted above patch-3.
        ///
        /// Source: Wow.exe 3.3.5a itself. Its patch list (code at VA 0x405AB0) is, in order: patch-4, patch-%s-4,
        /// patch-3, patch-%s-3, patch-2, patch-%s-2, patch, patch-%s - each patch level pairs the neutral and the
        /// locale archive, newest level first. The base list (table at VA 0xAB6278) is common, common-2, locale,
        /// speech, expansion-locale, lichking-locale, expansion-speech, lichking-speech, expansion, lichking.
        /// Measured on the install (listfile union, 204,187 names): base archives never share a file with each other
        /// and locale archives never share a file with neutral ones, so only "higher patch level wins, patch beats base"
        /// changes a result - and that is verified: every multi-copy DBC resolves to the copy with the most rows and the
        /// highest id (e.g. Map.dbc patch-enUS-3 135 rows > patch-enUS-2 134 > patch-enUS 125 > locale-enUS 118).
        /// </summary>
        public static string[] ArchiveOrder(string locale)
        {
            return new[]
            {
                "patch-3.MPQ",
                "{0}\\patch-{0}-3.MPQ",
                "patch-2.MPQ",
                "{0}\\patch-{0}-2.MPQ",
                "patch.MPQ",
                "{0}\\patch-{0}.MPQ",
                "{0}\\lichking-locale-{0}.MPQ",
                "{0}\\expansion-locale-{0}.MPQ",
                "{0}\\locale-{0}.MPQ",
                "{0}\\lichking-speech-{0}.MPQ",
                "{0}\\expansion-speech-{0}.MPQ",
                "{0}\\speech-{0}.MPQ",
                "lichking.MPQ",
                "expansion.MPQ",
                "common-2.MPQ",
                "common.MPQ",
            };
        }

        private WowFiles() { }

        /// <summary>Opens every archive under installRoot\Data in the client's priority order.</summary>
        public static WowFiles Open(string installRoot, string locale)
        {
            if (string.IsNullOrEmpty(installRoot)) throw new ArgumentException("installRoot is empty");
            if (string.IsNullOrEmpty(locale)) locale = WowInstall.DetectLocale(installRoot) ?? "enUS";
            var sw = Stopwatch.StartNew();
            var f = new WowFiles { Root = installRoot, Locale = locale };
            string data = Path.Combine(installRoot, "Data");
            ushort lcid = LocaleId(locale);

            var names = new List<string>();
            // Custom patches first (highest), then the stock list.
            names.AddRange(CustomPatches(data, locale));
            foreach (var n in ArchiveOrder(locale)) names.Add(string.Format(n, locale));

            long bytes = 0;
            foreach (var rel in names)
            {
                string path = Path.Combine(data, rel);
                if (!File.Exists(path)) continue;
                try
                {
                    var a = MpqArchive.Open(path);
                    a.PreferredLocale = lcid;
                    f._archives.Add(a);
                    bytes += new FileInfo(path).Length;
                }
                catch (Exception e)
                {
                    WowLog.Error("WowFiles: cannot open " + rel + ": " + e.GetType().Name + ": " + e.Message);
                }
            }
            if (f._archives.Count == 0)
            {
                f.Dispose();
                throw new FileNotFoundException("No MPQ archive could be opened under " + data);
            }
            WowLog.Info("WowFiles: " + f._archives.Count + " archives (" + (bytes >> 20) + " MB) opened from " + data + ", locale " + locale + ", " + sw.ElapsedMilliseconds + " ms");
            return f;
        }

        private static IEnumerable<string> CustomPatches(string data, string locale)
        {
            // patch-4.MPQ .. patch-9.MPQ and patch-A..Z (private-server custom patches), highest last letter first.
            var result = new List<string>();
            var suffixes = new List<string>();
            for (char c = '4'; c <= '9'; c++) suffixes.Add(c.ToString());
            for (char c = 'A'; c <= 'Z'; c++) suffixes.Add(c.ToString());
            for (int i = suffixes.Count - 1; i >= 0; i--)
            {
                string s = suffixes[i];
                string loc = locale + "\\patch-" + locale + "-" + s + ".MPQ";
                string bas = "patch-" + s + ".MPQ";
                if (File.Exists(Path.Combine(data, bas))) result.Add(bas);
                if (File.Exists(Path.Combine(data, loc))) result.Add(loc);
            }
            return result;
        }

        /// <summary>MPQ hash-table locale id (Windows LANGID) for a WoW locale; 0 = neutral.</summary>
        public static ushort LocaleId(string locale)
        {
            switch (locale)
            {
                case "enUS": return 0x409;
                case "enGB": return 0x809;
                case "deDE": return 0x407;
                case "frFR": return 0x40C;
                case "esES": return 0x40A;
                case "esMX": return 0x80A;
                case "ruRU": return 0x419;
                case "koKR": return 0x412;
                case "zhCN": return 0x804;
                case "zhTW": return 0x404;
                case "ptBR": return 0x416;
                case "itIT": return 0x410;
                default: return 0;
            }
        }

        private static string Clean(string path)
        {
            return path == null ? null : path.Replace('/', '\\').TrimStart('\\');
        }

        /// <summary>The archive and block that serve a path, or null (missing or hidden by a delete marker).</summary>
        public MpqArchive Resolve(string path, out int blockIndex)
        {
            blockIndex = -1;
            if (string.IsNullOrEmpty(path)) return null;
            var h = MpqArchive.NameHash.Of(Clean(path));
            for (int i = 0; i < _archives.Count; i++)
            {
                var r = _archives[i].Lookup(h, out blockIndex);
                if (r == MpqArchive.LookupResult.Found) return _archives[i];
                if (r == MpqArchive.LookupResult.Deleted) { blockIndex = -1; return null; }
            }
            blockIndex = -1;
            return null;
        }

        /// <summary>True when the file exists in any archive (and is not deleted by a patch). Case-insensitive, '/' or '\'.</summary>
        public bool Exists(string path)
        {
            int b;
            return Resolve(path, out b) != null;
        }

        /// <summary>The file's bytes from the highest-priority archive holding it, or null when missing.</summary>
        public byte[] Read(string path)
        {
            int b;
            var a = Resolve(path, out b);
            if (a == null) return null;
            try
            {
                return a.ReadBlock(b, Clean(path));
            }
            catch (Exception e)
            {
                WowLog.Warn("WowFiles: " + path + " in " + a.Name + " unreadable: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Name of the archive that serves the path (diagnostics), or null.</summary>
        public string ArchiveOf(string path)
        {
            int b;
            var a = Resolve(path, out b);
            return a == null ? null : a.Name;
        }

        /// <summary>The file size (uncompressed) without reading it, or -1 when missing.</summary>
        public long SizeOf(string path)
        {
            int b;
            var a = Resolve(path, out b);
            return a == null ? -1 : a.Blocks[b].FileSize;
        }

        /// <summary>
        /// Union of every archive's (listfile) (names as stored, de-duplicated case-insensitively). Built once, lazily;
        /// costs a few seconds and tens of MB, so use it for tools/diagnostics, not per frame.
        /// </summary>
        public IReadOnlyList<string> ListFile()
        {
            lock (_listLock)
            {
                if (_listFile != null) return _listFile;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var list = new List<string>();
                foreach (var a in _archives)
                    foreach (var n in a.ReadListFile())
                        if (seen.Add(n)) list.Add(n);
                _listFile = list;
                return list;
            }
        }

        public void Dispose()
        {
            foreach (var a in _archives) a.Dispose();
            _archives.Clear();
        }
    }
}
