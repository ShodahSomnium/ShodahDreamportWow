using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (lead contract stub, PLAN7 s3.4; bodies by Rules): the resolver that makes options A (baked table) and B (server only)
    // look the same to every caller. A mount spell names a SERVER creature entry; which CreatureDisplayInfo ids that entry has lives only in
    // the server database (acore_world.creature_template_model). LEARNED ids (a realm told us: SMSG_CREATURE_QUERY_RESPONSE, or a rider's
    // UNIT_FIELD_MOUNTDISPLAYID) win over BAKED ids (MountDisplayTable, the user's own realm, 2026-09-27); nothing = 0 + None and the caller
    // shows a labelled stand-in. Thread-safe (one lock); allocation only when something is learned or logged.
    public static class MountDisplays
    {
        /// <summary>Our limit on the saved cache (WowSettings.MountDisplayCache is stored with a 4096-character cap).</summary>
        private const int CacheMaxChars = 4096;
        // Stand-ins (PLAN7 s4.3, measured mount displays with an attachment-0 seat): 2404 Brown Horse (RidingHorse.m2), 17697 Golden Gryphon
        // (Gryphon_Mount.m2), 17699 Tawny Wind Rider (RidingWyvern.m2).
        private const int StandInGround = 2404, StandInAllianceFlyer = 17697, StandInHordeFlyer = 17699;

        private static readonly object Gate = new object();
        private static Dictionary<int, int[]> _baked;                         // parsed once from MountDisplayTable.Data
        private static readonly Dictionary<int, int[]> _learned = new Dictionary<int, int[]>();
        private static readonly HashSet<long> _logged = new HashSet<long>();
        private static int _version, _badTable;
        private static int[] _testData;                                       // harness only (ResetForTests): replaces MountDisplayTable.Data

        /// <summary>A display for the creature entry: LEARNED (a realm told us) first, then BAKED (MountDisplayTable), else 0 + None.
        /// pick chooses among several models (AC picks at random; offline: a stable hash of the character guid).</summary>
        public static int Resolve(int creatureEntry, int pick, out MountDisplaySource source)
        {
            int[] ids;
            lock (Gate)
            {
                if (!Lookup(creatureEntry, out ids, out source))
                {
                    source = MountDisplaySource.None;
                    if (_logged.Add(Key(creatureEntry, MountDisplaySource.None)))
                        WowLog.Info("MountDisplays: entry " + creatureEntry + " unknown (no learned or baked display) -> stand-in");
                    return 0;
                }
                int id = ids[(pick & 0x7fffffff) % ids.Length];
                if (_logged.Add(Key(creatureEntry, source)))
                    WowLog.Info("MountDisplays: entry " + creatureEntry + " -> " + id + " (" + (source == MountDisplaySource.Learned ? "learned" : "baked") + ")"
                        + (ids.Length > 1 ? " - pick " + ((pick & 0x7fffffff) % ids.Length + 1) + " of " + ids.Length + " (" + Join(ids) + ")" : ""));
                return id;
            }
        }

        public static bool TryGetAll(int creatureEntry, out int[] displayIds, out MountDisplaySource source)
        {
            lock (Gate)
            {
                int[] ids;
                if (!Lookup(creatureEntry, out ids, out source)) { displayIds = null; source = MountDisplaySource.None; return false; }
                displayIds = (int[])ids.Clone();
                return true;
            }
        }

        /// <summary>The labelled stand-in when Resolve gives 0: 2404 (RidingHorse.m2) for ground mounts, 17697 (Gryphon_Mount.m2) for Alliance
        /// flyers, 17699 (RidingWyvern.m2) for Horde flyers - measured mount displays with an attachment-0 seat (s4.3).</summary>
        public static int StandIn(MountKind kind, WowFaction faction)
        {
            if (kind == MountKind.Flying) return faction == WowFaction.Horde ? StandInHordeFlyer : StandInAllianceFlyer;
            return StandInGround;
        }

        /// <summary>Main thread. From SMSG_CREATURE_QUERY_RESPONSE Modelid1..4 (non-zero) of a mount entry, or "entry E ridden with
        /// UNIT_FIELD_MOUNTDISPLAYID D". Version++ when new or different.</summary>
        public static void Learn(int creatureEntry, int[] displayIds)
        {
            if (creatureEntry <= 0 || displayIds == null) return;
            int n = 0;
            for (int i = 0; i < displayIds.Length; i++) if (displayIds[i] > 0) n++;
            if (n == 0) return;
            var ids = new int[n];
            n = 0;
            for (int i = 0; i < displayIds.Length; i++) if (displayIds[i] > 0) ids[n++] = displayIds[i];
            lock (Gate)
            {
                int[] old;
                if (_learned.TryGetValue(creatureEntry, out old) && Same(old, ids)) return;
                _learned[creatureEntry] = ids;
                _version++;
                int[] baked;
                EnsureBaked();
                string vs = _baked.TryGetValue(creatureEntry, out baked) ? (Same(baked, ids) ? ", same as the baked table" : ", baked table said " + Join(baked)) : ", not in the baked table";
                WowLog.Info("MountDisplays: learned entry " + creatureEntry + " -> " + Join(ids) + (old != null ? " (was " + Join(old) + ")" : "") + vs);
            }
        }

        public static int BakedCount { get { lock (Gate) { EnsureBaked(); return _baked.Count; } } }

        public static int LearnedCount { get { lock (Gate) return _learned.Count; } }

        public static int Version { get { lock (Gate) return _version; } }

        /// <summary>"v1;284:2404;38545:31248,31007;..." - the LEARNED entries only (numbers only; the baked table is in the code), sorted by entry,
        /// cut at 4096 characters (logged).</summary>
        public static string SaveCache()
        {
            lock (Gate)
            {
                var keys = new List<int>(_learned.Keys);
                keys.Sort();
                var sb = new StringBuilder("v1");
                int dropped = 0;
                foreach (int k in keys)
                {
                    var ids = _learned[k];
                    int mark = sb.Length;
                    sb.Append(';').Append(k).Append(':');
                    for (int i = 0; i < ids.Length; i++) { if (i > 0) sb.Append(','); sb.Append(ids[i]); }
                    if (sb.Length > CacheMaxChars) { sb.Length = mark; dropped++; }
                }
                if (dropped > 0) WowLog.Warn("MountDisplays: display cache full (" + CacheMaxChars + " characters) - " + dropped + " learned entries not saved");
                return sb.ToString();
            }
        }

        /// <summary>Entries accepted; bad text ignored and counted in the log (never throws). Learned entries already known are replaced.</summary>
        public static int LoadCache(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int ok = 0, bad = 0;
            lock (Gate)
            {
                var parts = text.Split(';');
                if (parts[0].Trim() != "v1")
                {
                    WowLog.Warn("MountDisplays: display cache ignored - unknown format '" + Clip(parts[0]) + "' (expected v1)");
                    return 0;
                }
                bool changed = false;
                for (int p = 1; p < parts.Length; p++)
                {
                    string s = parts[p].Trim();
                    if (s.Length == 0) continue;
                    int colon = s.IndexOf(':');
                    int entry;
                    if (colon <= 0 || !int.TryParse(s.Substring(0, colon), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out entry) || entry <= 0) { bad++; continue; }
                    var fields = s.Substring(colon + 1).Split(',');
                    var ids = new int[fields.Length];
                    bool good = fields.Length > 0;
                    for (int i = 0; i < fields.Length && good; i++)
                        good = int.TryParse(fields[i].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ids[i]) && ids[i] > 0;
                    if (!good) { bad++; continue; }
                    int[] old;
                    if (!_learned.TryGetValue(entry, out old) || !Same(old, ids)) { _learned[entry] = ids; changed = true; }
                    ok++;
                }
                if (changed) _version++;
            }
            WowLog.Info("MountDisplays: display cache - " + ok + " learned entries loaded" + (bad > 0 ? ", " + bad + " bad entries ignored" : ""));
            return ok;
        }

        public static string Summary                                          // boot log: "N baked (source ...), M learned, stand-ins 2404/17697/17699"
        {
            get
            {
                lock (Gate)
                {
                    EnsureBaked();
                    return _baked.Count + " baked (source " + MountDisplayTable.Source + ")" + (_badTable > 0 ? " [" + _badTable + " malformed table numbers]" : "")
                        + ", " + _learned.Count + " learned, stand-ins " + StandInGround + "/" + StandInAllianceFlyer + "/" + StandInHordeFlyer;
                }
            }
        }

        // ------------------------------------------------------------------ helpers (callers hold Gate)

        private static bool Lookup(int entry, out int[] ids, out MountDisplaySource source)
        {
            if (_learned.TryGetValue(entry, out ids)) { source = MountDisplaySource.Learned; return true; }
            EnsureBaked();
            if (_baked.TryGetValue(entry, out ids)) { source = MountDisplaySource.Baked; return true; }
            ids = null;
            source = MountDisplaySource.None;
            return false;
        }

        /// <summary>Parses the flat generated table: entry, count, display ids..., entry, count, ... (option B = an empty array).</summary>
        private static void EnsureBaked()
        {
            if (_baked != null) return;
            var d = new Dictionary<int, int[]>();
            var data = _testData ?? MountDisplayTable.Data;
            int i = 0;
            while (i + 1 < data.Length)
            {
                int entry = data[i], count = data[i + 1];
                if (entry <= 0 || count <= 0 || i + 2 + count > data.Length) { _badTable = data.Length - i; break; }
                var ids = new int[count];
                Array.Copy(data, i + 2, ids, 0, count);
                d[entry] = ids;
                i += 2 + count;
            }
            if (i < data.Length && _badTable == 0) _badTable = data.Length - i;
            if (_badTable > 0) WowLog.Warn("MountDisplays: MountDisplayTable has " + _badTable + " trailing / malformed numbers - read " + d.Count + " entries");
            _baked = d;
        }

        /// <summary>Harness only: forget everything learned and parse bakedData instead of the generated table (null = the generated table;
        /// an empty array = option B).</summary>
        internal static void ResetForTests(int[] bakedData)
        {
            lock (Gate)
            {
                _testData = bakedData;
                _baked = null;
                _badTable = 0;
                _learned.Clear();
                _logged.Clear();
                _version++;
            }
        }

        private static long Key(int entry, MountDisplaySource s) { return ((long)(uint)entry << 3) | (long)s; }

        private static bool Same(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static string Join(int[] ids)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++) { if (i > 0) sb.Append(','); sb.Append(ids[i]); }
            return sb.ToString();
        }

        private static string Clip(string s) { return s.Length > 16 ? s.Substring(0, 16) + "..." : s; }
    }
}
