using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Iteration 7 (owner UI+Audio, PLAN7 s3.6): the M2 event block (pure, harness-testable). Header +0x100 = count, +0x104 = offset
    /// of 36-byte entries (WotLK M2Event): +0 identifier (4 ASCII chars: "$FSD" footstep, "$WNG" wing flap, ...), +4 data, +8 bone,
    /// +12 position (3 floats), +24 M2TrackBase (u16 interpolation, i16 global sequence), +28 M2Array of per-sequence M2Array&lt;u32&gt;
    /// timestamps (count, offset) - the layout of the data scout's parser (P/harness/Probes/Mount7/data/MountData2.cs Events /
    /// MountEventSeqs). As with every other track (M2Model.TrackCore) the per-sequence count / offset pairs are in the .m2 while the
    /// timestamps themselves are in the .m2 for embedded sequences (flag 0x20) or global-sequence tracks, else in the sequence's
    /// external .anim file (Read with a file reader loads those; without one only embedded sequences have times).
    /// Several entries with the same identifier (one per foot) are merged, sorted.
    /// </summary>
    public sealed class M2Events
    {
        private const int HeaderEvents = 0x100, HeaderSequences = 0x1C, SequenceSize = 64, EventSize = 36;
        private static readonly int[] None = new int[0];

        private readonly Dictionary<string, int[][]> _byId = new Dictionary<string, int[][]>(StringComparer.Ordinal);
        private int[] _aliasNext = None;
        private bool[] _alias = new bool[0];

        /// <summary>Number of event entries in the model.</summary>
        public int EventCount { get; private set; }
        /// <summary>Number of sequences of the model.</summary>
        public int SequenceCount { get; private set; }
        /// <summary>External .anim files that were needed for event times but could not be read.</summary>
        public int AnimsMissing { get; private set; }
        /// <summary>One line for the log, e.g. "5 events ($FSD, $WNG, $CAH ...), 1 .anim missing".</summary>
        public string Summary { get; private set; }

        /// <summary>The events of an .m2 (times of embedded sequences only). Never throws; a broken file gives no events.</summary>
        public static M2Events Read(byte[] m2) { return Read(m2, null, null); }

        /// <summary>As Read(m2), also reading the external .anim files (M2Model.AnimPath of modelPath) of sequences whose keys are not embedded.</summary>
        public static M2Events Read(byte[] m2, string modelPath, Func<string, byte[]> readFile)
        {
            var ev = new M2Events();
            try { ev.Parse(m2, modelPath, readFile); }
            catch (Exception e) { ev.Summary = "events unreadable (" + e.GetType().Name + ": " + e.Message + ")"; }
            if (ev.Summary == null) ev.Summary = "no events";
            return ev;
        }

        /// <summary>
        /// "$FSD" footsteps / "$WNG" wing flaps: ms inside that sequence (an alias sequence is followed to the one that holds the keys),
        /// sorted; empty when none. Allocation-free (the arrays are built once in Read).
        /// </summary>
        public int[] Times(string id, int sequenceIndex)
        {
            if (id == null || sequenceIndex < 0 || sequenceIndex >= SequenceCount) return None;
            int s = sequenceIndex;
            for (int guard = 0; guard < 8 && s >= 0 && s < _alias.Length && _alias[s]; guard++) s = _aliasNext[s];
            if (s < 0 || s >= SequenceCount) return None;
            int[][] per;
            if (!_byId.TryGetValue(id, out per) || per == null || s >= per.Length) return None;
            return per[s] ?? None;
        }

        /// <summary>True when the model has an event with this identifier (on any sequence).</summary>
        public bool Has(string id) { return id != null && _byId.ContainsKey(id); }

        private void Parse(byte[] b, string modelPath, Func<string, byte[]> readFile)
        {
            if (b == null || b.Length < 0x130 || b[0] != 'M' || b[1] != 'D' || b[2] != '2' || b[3] != '0') { Summary = "not an MD20 model"; return; }
            int seqN = I32(b, HeaderSequences), seqO = I32(b, HeaderSequences + 4);
            if (seqN < 0 || seqO < 0 || (long)seqO + (long)seqN * SequenceSize > b.Length) seqN = 0;
            SequenceCount = seqN;
            var flags = new uint[seqN];
            var animId = new int[seqN];
            var subId = new int[seqN];
            _alias = new bool[seqN];
            _aliasNext = new int[seqN];
            for (int i = 0; i < seqN; i++)
            {
                int o = seqO + i * SequenceSize;
                animId[i] = U16(b, o);
                subId[i] = U16(b, o + 2);
                flags[i] = (uint)I32(b, o + 12);
                _alias[i] = (flags[i] & 0x40) != 0;
                _aliasNext[i] = U16(b, o + 62);
            }

            int n = I32(b, HeaderEvents), off = I32(b, HeaderEvents + 4);
            if (n <= 0 || off <= 0 || (long)off + (long)n * EventSize > b.Length) { Summary = "no events"; return; }
            EventCount = n;
            var sources = new byte[seqN][];
            var tried = new bool[seqN];
            var lists = new Dictionary<string, List<int>[]>(StringComparer.Ordinal);
            for (int k = 0; k < n; k++)
            {
                int e = off + k * EventSize;
                string id = Encoding.ASCII.GetString(b, e, 4);
                bool global = (short)U16(b, e + 26) >= 0;
                int tn = I32(b, e + 28), to = I32(b, e + 32);
                List<int>[] per;
                if (!lists.TryGetValue(id, out per)) { per = new List<int>[seqN]; lists[id] = per; }
                if (tn <= 0 || to <= 0 || (long)to + (long)tn * 8 > b.Length) continue;
                int count = Math.Min(tn, seqN);
                for (int s = 0; s < count; s++)
                {
                    int c = I32(b, to + s * 8), co = I32(b, to + s * 8 + 4);
                    if (c <= 0) continue;
                    byte[] src = b;
                    if (!global && (flags[s] & 0x20) == 0)
                    {
                        if (!tried[s])
                        {
                            tried[s] = true;
                            if (readFile != null && !string.IsNullOrEmpty(modelPath) && !_alias[s])
                            {
                                try { sources[s] = readFile(M2Model.AnimPath(modelPath, animId[s], subId[s])); }
                                catch (Exception) { sources[s] = null; }
                                if (sources[s] == null) AnimsMissing++;
                            }
                        }
                        src = sources[s];
                        if (src == null) continue;
                    }
                    if (co < 0 || (long)co + (long)c * 4 > src.Length) continue;
                    List<int> l = per[s] ?? (per[s] = new List<int>(c));
                    for (int t = 0; t < c; t++) l.Add(I32(src, co + t * 4));
                }
            }
            var names = new StringBuilder();
            foreach (var kv in lists)
            {
                var arr = new int[seqN][];
                for (int s = 0; s < seqN; s++)
                {
                    if (kv.Value[s] == null || kv.Value[s].Count == 0) continue;
                    kv.Value[s].Sort();
                    arr[s] = kv.Value[s].ToArray();
                }
                _byId[kv.Key] = arr;
                if (names.Length < 80) names.Append(names.Length > 0 ? ", " : "").Append(kv.Key);
            }
            Summary = n + " events (" + names + ")" + (AnimsMissing > 0 ? ", " + AnimsMissing + " .anim missing" : "");
        }

        private static int I32(byte[] b, int o) { return o >= 0 && o + 4 <= b.Length ? BitConverter.ToInt32(b, o) : 0; }
        private static int U16(byte[] b, int o) { return o >= 0 && o + 2 <= b.Length ? BitConverter.ToUInt16(b, o) : 0; }
    }
}
