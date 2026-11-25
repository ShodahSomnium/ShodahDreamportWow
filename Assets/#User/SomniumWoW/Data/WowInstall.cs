using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>
    /// Finds and validates a WoW 3.3.5a install on this PC (pure System.IO, read-only, no registry).
    /// Valid = Wow.exe + Data\common.MPQ + Data\&lt;locale&gt;\locale-&lt;locale&gt;.MPQ.
    /// Search: the hint (default D:\WOTLK\World of Warcraft 3.3.5a; a Wow.exe or Data path is accepted too), then on
    /// every ready fixed drive: \World of Warcraft*, \WOTLK\World of Warcraft*, \Games\World of Warcraft*,
    /// \Program Files (x86)\World of Warcraft*, \Program Files\World of Warcraft* (and those folders' direct children
    /// named World of Warcraft* / Wrath*). Call from a worker thread: it touches the disk.
    /// </summary>
    public static class WowInstall
    {
        public const string DefaultHint = @"D:\WOTLK\World of Warcraft 3.3.5a";

        /// <summary>Install folders tried right after the saved / default one (2026-11-04: the XR Lab PC's desktop copy).</summary>
        public static readonly string[] KnownPaths = { @"C:\Users\XR Lab\Desktop\World of Warcraft 3.3.5a" };

        /// <summary>Locales the 3.3.5a client ships, in the order we try them when Config.wtf does not say.</summary>
        public static readonly string[] KnownLocales = { "enUS", "enGB", "deDE", "frFR", "esES", "esMX", "ruRU", "koKR", "zhCN", "zhTW", "enCN", "enTW", "ptBR", "itIT" };

        /// <summary>The install root, or null. Tries the hint first, then the common locations on every fixed drive.</summary>
        public static string Find(string hint)
        {
            var tried = new List<string>();
            string h = Normalize(string.IsNullOrEmpty(hint) ? DefaultHint : hint);
            string why;
            if (h != null)
            {
                tried.Add(h);
                if (Validate(h, out why)) return h;
            }
            if (!string.IsNullOrEmpty(hint) && Normalize(DefaultHint) != h)
            {
                tried.Add(DefaultHint);
                if (Validate(DefaultHint, out why)) return DefaultHint;
            }

            // 2026-11-04 (user): known install folders on the team's PCs, tried before the drive scan
            foreach (var known in KnownPaths)
            {
                if (tried.Contains(known)) continue;
                tried.Add(known);
                if (Validate(known, out why)) return known;
            }

            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (Exception e) { WowLog.Warn("WowInstall: cannot list drives: " + e.Message); return null; }
            string[] parents = { "", "WOTLK", "Games", "Program Files (x86)", "Program Files", @"Games\WoW", "WoW" };
            foreach (var d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                }
                catch { continue; }
                string drive = d.RootDirectory.FullName;
                foreach (var p in parents)
                {
                    string parent = p.Length == 0 ? drive : Path.Combine(drive, p);
                    foreach (var cand in Candidates(parent))
                    {
                        if (tried.Contains(cand)) continue;
                        tried.Add(cand);
                        if (Validate(cand, out why)) return cand;
                    }
                }
            }
            WowLog.Warn("WowInstall: no valid 3.3.5a install found (tried " + tried.Count + " folders, first " + (tried.Count > 0 ? tried[0] : "-") + ")");
            return null;
        }

        private static IEnumerable<string> Candidates(string parent)
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(parent)) return list;
                foreach (var pattern in new[] { "World of Warcraft*", "WoW*", "Wrath*", "WOTLK*" })
                    foreach (var dir in Directory.GetDirectories(parent, pattern))
                    {
                        if (!list.Contains(dir)) list.Add(dir);
                        // One level down (e.g. D:\WOTLK\World of Warcraft 3.3.5a).
                        try
                        {
                            foreach (var sub in Directory.GetDirectories(dir, "World of Warcraft*"))
                                if (!list.Contains(sub)) list.Add(sub);
                        }
                        catch { }
                    }
            }
            catch { }
            return list;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string p = path.Trim().Trim('"');
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = Path.GetDirectoryName(p);
                p = p.TrimEnd('\\', '/');
                if (string.Equals(Path.GetFileName(p), "Data", StringComparison.OrdinalIgnoreCase)) p = Path.GetDirectoryName(p);
                return p;
            }
            catch { return null; }
        }

        /// <summary>True when root holds Wow.exe, Data\common.MPQ and the detected locale's locale-xxXX.MPQ.</summary>
        public static bool Validate(string root, out string why)
        {
            why = null;
            try
            {
                if (string.IsNullOrEmpty(root)) { why = "no path"; return false; }
                if (!Directory.Exists(root)) { why = "folder not found: " + root; return false; }
                if (!File.Exists(Path.Combine(root, "Wow.exe"))) { why = "Wow.exe missing in " + root; return false; }
                string data = Path.Combine(root, "Data");
                if (!File.Exists(Path.Combine(data, "common.MPQ"))) { why = @"Data\common.MPQ missing in " + root; return false; }
                string loc = DetectLocale(root);
                if (loc == null) { why = @"no Data\<locale>\locale-<locale>.MPQ in " + root; return false; }
                return true;
            }
            catch (Exception e)
            {
                why = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// The locale folder to use: WTF\Config.wtf's SET locale "xxXX" when that folder has locale-xxXX.MPQ, else the
        /// first of KnownLocales that has it, else any Data\xxXX folder that has it. Null when none.
        /// </summary>
        public static string DetectLocale(string root)
        {
            if (string.IsNullOrEmpty(root)) return null;
            string data = Path.Combine(root, "Data");
            try
            {
                string cfg = Path.Combine(Path.Combine(root, "WTF"), "Config.wtf");
                if (File.Exists(cfg))
                {
                    foreach (var line in File.ReadAllLines(cfg))
                    {
                        var t = line.Trim();
                        if (!t.StartsWith("SET locale ", StringComparison.OrdinalIgnoreCase)) continue;
                        var v = t.Substring(11).Trim().Trim('"');
                        if (v.Length == 4 && HasLocale(data, v)) return v;
                    }
                }
            }
            catch { }
            foreach (var l in KnownLocales) if (HasLocale(data, l)) return l;
            try
            {
                foreach (var dir in Directory.GetDirectories(data))
                {
                    var n = Path.GetFileName(dir);
                    if (n.Length == 4 && HasLocale(data, n)) return n;
                }
            }
            catch { }
            return null;
        }

        private static bool HasLocale(string data, string locale)
        {
            try { return File.Exists(Path.Combine(Path.Combine(data, locale), "locale-" + locale + ".MPQ")); }
            catch { return false; }
        }

        // ------------------------------------------------------------------ the room's folder browser (worker thread)

        /// <summary>Folders read from one folder at most (the list is paged; a huge folder like C:\Windows\WinSxS stops here).</summary>
        public const int MaxListed = 2000;

        /// <summary>Child folders checked for Wow.exe (one File.Exists each; the full Validate only where Wow.exe is).</summary>
        public const int MaxChecked = 400;

        /// <summary>One row of the folder browser: a drive or a folder.</summary>
        public sealed class FolderEntry
        {
            public string Name;      // what the row shows ("C:\  System (fixed)", "World of Warcraft 3.3.5a")
            public string Path;      // the full path
            public bool IsDrive;
            public bool IsInstall;   // Validate accepts it
            public string Locale;    // the install's locale when IsInstall
        }

        /// <summary>What the folder browser shows for one folder (or for the drive list). Built on a worker thread.</summary>
        public sealed class FolderListing
        {
            public string Folder;            // the folder listed; null = the drive list
            public string Parent;            // the folder above; null = "Up" goes to the drive list
            public readonly List<FolderEntry> Entries = new List<FolderEntry>();
            public int Total;                // folders found (Entries stops at MaxListed)
            public string Error;             // plain words when the folder (or the drive list) could not be read
            public string Install;           // the install this folder gives: itself, the folder above (a Data folder) or the one install inside; null when none
            public string InstallLocale;
            public string InstallText;       // plain words about Install
            public string Note;              // plain words: why a folder that looks like an install is not one; several installs inside
            public string Moved;             // the start folder was gone: plain words about where the list starts instead
            public long Ms;
        }

        /// <summary>
        /// A typed or pasted path made usable: surrounding spaces and quotes removed, "D:" becomes "D:\", a path to Wow.exe
        /// becomes its folder. Null when nothing is left. No disk access.
        /// </summary>
        public static string CleanPath(string typed)
        {
            if (typed == null) return null;
            string p = typed.Trim().Trim('"').Trim();
            if (p.Length == 0) return null;
            p = p.Replace('/', '\\');
            if (p.Length == 2 && p[1] == ':' && char.IsLetter(p[0])) return p + "\\";
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try { p = Path.GetDirectoryName(p); }
                catch { return null; }
            }
            if (p == null) return null;
            if (p.Length > 3) p = p.TrimEnd('\\');
            return p.Length == 0 ? null : p;
        }

        /// <summary>
        /// <see cref="Validate"/> in plain words for the player: true with the locale when root is a usable 3.3.5a install;
        /// otherwise plain says what is missing. Never throws.
        /// </summary>
        public static bool Check(string root, out string plain, out string locale)
        {
            plain = null;
            locale = null;

            try
            {
                if (string.IsNullOrEmpty(root)) { plain = "No folder given."; return false; }
                if (!Directory.Exists(root)) { plain = "This folder does not exist (or its drive is not connected): " + root; return false; }
                if (!File.Exists(Path.Combine(root, "Wow.exe"))) { plain = "There is no Wow.exe in this folder."; return false; }
                if (!File.Exists(Path.Combine(Path.Combine(root, "Data"), "common.MPQ")))
                {
                    plain = @"Wow.exe is here, but Data\common.MPQ is missing - this is not a complete World of Warcraft 3.3.5a (newer versions keep their files differently).";
                    return false;
                }
                locale = DetectLocale(root);
                if (locale == null)
                {
                    plain = @"Wow.exe and Data\common.MPQ are here, but no language folder was found (like Data\enUS with locale-enUS.MPQ).";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                plain = Plain(e);
                return false;
            }
        }

        /// <summary>An exception from listing or reading a folder, in plain words.</summary>
        public static string Plain(Exception e)
        {
            if (e == null) return "Unknown problem.";
            if (e is UnauthorizedAccessException || e is System.Security.SecurityException) return "Windows does not let this world open that folder (access denied). Pick another folder.";
            if (e is DirectoryNotFoundException) return "That folder does not exist (any more).";
            if (e is DriveNotFoundException) return "That drive does not exist on this PC.";
            if (e is PathTooLongException) return "That path is too long.";
            if (e is ArgumentException || e is NotSupportedException) return "That is not a valid folder path.";
            if (e is IOException)
            {
                // ERROR_NOT_READY (21): a card reader or disc drive without a medium.
                if ((e.HResult & 0xFFFF) == 21) return "The drive is not ready (no disc or card in it?).";
                return "The drive could not be read: " + e.Message;
            }
            return e.GetType().Name + ": " + e.Message;
        }

        /// <summary>The ready fixed and removable drives, each checked for an install at its root. Worker thread (IsReady can be slow).</summary>
        public static FolderListing ListDrives()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var r = new FolderListing();
            DriveInfo[] drives;

            try { drives = DriveInfo.GetDrives(); }
            catch (Exception e)
            {
                r.Error = "The drives of this PC could not be listed: " + Plain(e);
                r.Ms = watch.ElapsedMilliseconds;
                return r;
            }

            foreach (DriveInfo d in drives)
            {
                DriveType type;
                bool ready;

                try { type = d.DriveType; }
                catch { continue; }

                if (type != DriveType.Fixed && type != DriveType.Removable) continue;

                try { ready = d.IsReady; }
                catch { ready = false; }

                if (!ready) continue;

                string label = "";
                string root;

                try { label = d.VolumeLabel ?? ""; }
                catch { label = ""; }

                try { root = d.RootDirectory.FullName; }
                catch { root = d.Name; }

                var entry = new FolderEntry
                {
                    Name = root + (label.Length > 0 ? "  " + label : "") + (type == DriveType.Removable ? "  (removable)" : ""),
                    Path = root,
                    IsDrive = true,
                };
                MarkIfInstall(entry);
                r.Entries.Add(entry);
            }

            r.Total = r.Entries.Count;
            if (r.Entries.Count == 0) r.Error = "No ready drive was found on this PC.";
            r.Ms = watch.ElapsedMilliseconds;
            return r;
        }

        /// <summary>
        /// One folder for the browser: its sub-folders (hidden system folders left out, names sorted, installs first, at most
        /// <see cref="MaxListed"/>), which of them are installs, and the install this folder gives (itself; the folder above
        /// when it is a Data folder; the only install inside). walkUp: a folder that is gone lists its nearest existing
        /// parent instead (Moved says so). Worker thread; never throws.
        /// </summary>
        public static FolderListing List(string folder, bool walkUp)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var r = new FolderListing();

            try
            {
                string f = CleanPath(folder);

                if (f == null)
                {
                    r.Error = "No folder given.";
                    return r;
                }

                try { f = Path.GetFullPath(f); }
                catch (Exception e)
                {
                    r.Error = Plain(e) + " (" + f + ")";
                    return r;
                }

                if (!Directory.Exists(f) && File.Exists(f))
                {
                    f = Path.GetDirectoryName(f);   // a file was pasted: its folder
                }

                if (!Directory.Exists(f) && walkUp)
                {
                    string up = f;

                    while (up != null && !Directory.Exists(up))
                    {
                        try { up = Path.GetDirectoryName(up); }
                        catch { up = null; }
                    }

                    if (up != null)
                    {
                        r.Moved = f + " is not there any more - showing " + up + ".";
                        f = up;
                    }
                }

                r.Folder = f;
                r.Parent = ParentOf(f);

                if (!Directory.Exists(f))
                {
                    r.Error = "This folder does not exist (or its drive is not connected): " + f;
                    return r;
                }

                string plain, loc;

                if (Check(f, out plain, out loc))
                {
                    r.Install = f;
                    r.InstallLocale = loc;
                    r.InstallText = "World of Warcraft 3.3.5a found here (" + loc + ").";
                }
                else
                {
                    if (File.Exists(Path.Combine(f, "Wow.exe"))) r.Note = plain;

                    string p = r.Parent;
                    string ploc, pplain;

                    if (p != null && string.Equals(Path.GetFileName(f), "Data", StringComparison.OrdinalIgnoreCase) && Check(p, out pplain, out ploc))
                    {
                        r.Install = p;
                        r.InstallLocale = ploc;
                        r.InstallText = "This is the Data folder of World of Warcraft 3.3.5a in " + p + " (" + ploc + ").";
                    }
                }

                var dirs = new List<FolderEntry>();

                try
                {
                    foreach (DirectoryInfo d in new DirectoryInfo(f).EnumerateDirectories())
                    {
                        FileAttributes a;

                        try { a = d.Attributes; }
                        catch { a = 0; }

                        if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;   // $Recycle.Bin, System Volume Information
                        if (d.Name.StartsWith("$", StringComparison.Ordinal)) continue;                          // $SysReset, $WINDOWS.~BT, $WinREAgent
                        r.Total++;
                        if (dirs.Count < MaxListed) dirs.Add(new FolderEntry { Name = d.Name, Path = d.FullName });
                    }
                }
                catch (Exception e)
                {
                    r.Error = Plain(e);
                }

                dirs.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
                int installs = 0;
                FolderEntry only = null;

                for (int i = 0; i < dirs.Count && i < MaxChecked; i++)
                {
                    if (MarkIfInstall(dirs[i]))
                    {
                        installs++;
                        only = dirs[i];
                    }
                }

                if (r.Install == null && installs == 1)
                {
                    r.Install = only.Path;
                    r.InstallLocale = only.Locale;
                    r.InstallText = "World of Warcraft 3.3.5a found inside this folder: " + only.Name + " (" + only.Locale + ").";
                }
                else if (r.Install == null && installs > 1)
                {
                    r.Note = installs + " World of Warcraft 3.3.5a folders are in here - open the one you want.";
                }

                foreach (FolderEntry e in dirs) if (e.IsInstall) r.Entries.Add(e);
                foreach (FolderEntry e in dirs) if (!e.IsInstall) r.Entries.Add(e);
            }
            catch (Exception e)
            {
                r.Error = Plain(e);
            }
            finally
            {
                r.Ms = watch.ElapsedMilliseconds;
            }

            return r;
        }

        /// <summary>True (and IsInstall + Locale set) when the entry's folder is a usable install; Wow.exe is looked for first.</summary>
        private static bool MarkIfInstall(FolderEntry e)
        {
            try
            {
                if (!File.Exists(Path.Combine(e.Path, "Wow.exe"))) return false;
                string plain, loc;
                if (!Check(e.Path, out plain, out loc)) return false;
                e.IsInstall = true;
                e.Locale = loc;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The folder above, or null at a drive root / share root.</summary>
        public static string ParentOf(string folder)
        {
            try
            {
                DirectoryInfo p = Directory.GetParent(folder);
                return p != null ? p.FullName : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>True when both name the same folder (letter case and a trailing backslash ignored). No disk access.</summary>
        public static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string x = a.Replace('/', '\\'), y = b.Replace('/', '\\');
            if (x.Length > 3) x = x.TrimEnd('\\');
            if (y.Length > 3) y = y.TrimEnd('\\');
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ the saved folder (per Somnium account, this PC)

        public const string SavedKeyPrefix = "wow_install_path_";
        public const string SavedHeader = "SOMNIUMWOW-INSTALL 1";

        /// <summary>The storage key of the account's saved game folder: "wow_install_path_" + the Somnium id (a-z A-Z 0-9 - _ only).</summary>
        public static string SavedKeyFor(string userId)
        {
            var sb = new System.Text.StringBuilder(SavedKeyPrefix);
            foreach (char c in userId ?? "")
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
                if (sb.Length >= 80) break;
            }
            if (sb.Length == SavedKeyPrefix.Length) sb.Append("anonymous");
            return sb.ToString();
        }

        /// <summary>The stored text: header, "path=...", "end" (a write torn before "end" is not taken).</summary>
        public static string EncodeSaved(string path)
        {
            return SavedHeader + "\npath=" + (path ?? "") + "\nend\n";
        }

        /// <summary>The path in a stored text, or null when the text is not a whole saved folder (another format, torn, empty path).</summary>
        public static string DecodeSaved(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Replace("\r", "").Split('\n');
            if (lines.Length < 3 || lines[0].Trim() != SavedHeader) return null;
            string path = null;
            bool end = false;

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("path=", StringComparison.Ordinal) && path == null) path = lines[i].Substring(5).Trim();
                else if (lines[i].Trim() == "end") { end = true; break; }
            }

            if (!end || string.IsNullOrEmpty(path)) return null;
            foreach (char c in path) if (c < ' ') return null;
            return path;
        }
    }
}
