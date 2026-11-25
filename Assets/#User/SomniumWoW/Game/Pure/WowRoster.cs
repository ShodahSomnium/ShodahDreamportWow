using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>One place the roster can be kept (the world's LocalStorage, or memory in the editor). Must not throw.</summary>
    public interface IWowSaveSlot
    {
        /// <summary>For the log: "world storage", "world storage_2", "memory (editor)".</summary>
        string Name { get; }

        /// <summary>False when nothing is stored under the key; true with the stored text otherwise (which may be damaged or empty).</summary>
        bool TryRead(string key, out string text);

        /// <summary>True when the text was stored.</summary>
        bool Write(string key, string text);
    }

    /// <summary>
    /// The local account's characters (at most 10), kept in every slot (pure: the harness runs it). The Euro Truck Sim
    /// Dreamport's EtsProfileBook pattern, whose pitfalls it follows:
    /// - The client's LocalStorage.WriteText is File.WriteAllText (truncates first), so the roster is written to TWO keys
    ///   one after the other: a crash mid-write leaves at most one copy torn.
    /// - ReadText returns "" for a file it cannot decrypt, so a damaged copy looks empty: the slot asks TextExists
    ///   first and a copy that exists but does not decode is kept under "&lt;key&gt;_bad" before anything overwrites it.
    /// - A copy written by a newer format is never overwritten (read-only: shown, not saved).
    /// Text format (versioned; each character is CharacterRecord.Serialize with its own FNV check):
    /// <code>
    /// SOMNIUMWOW-ROSTER 1
    /// gen=12                  (raised by every commit; the highest good copy wins on load)
    /// count=3
    /// body=1A2B3C4D           (FNV-1a over the character text below)
    /// SOMNIUMWOW-CHARACTER 1 ... (CharacterRecord.SerializeMany)
    /// </code>
    /// A copy whose body check fails still gives back every character whose own check passes (salvage).
    /// </summary>
    public sealed class WowRoster
    {
        public const int MaxCharacters = 10;
        public const int Format = 1;
        public const string Header = "SOMNIUMWOW-ROSTER";
        public const string KeyPrefix = "somniumwow_roster_v1_";

        private readonly IWowSaveSlot[] _slots;
        private readonly List<CharacterRecord> _chars = new List<CharacterRecord>();

        public string UserId { get; private set; }
        public string Key { get; private set; }
        public IReadOnlyList<CharacterRecord> Characters { get { return _chars; } }
        public long Generation { get; private set; }

        /// <summary>A stored copy is from a newer build: nothing is written, so it is not clobbered.</summary>
        public bool ReadOnly { get; private set; }

        /// <summary>A copy (or a character in it) could not be read. Worth telling the player once.</summary>
        public bool HadDamage { get; private set; }

        /// <summary>What was found where and which copy won (one log line).</summary>
        public string LoadReport { get; private set; }

        /// <summary>Why characters were skipped (damaged / newer / unsupported), for the log.</summary>
        public List<string> Problems { get; private set; }

        public bool LastCommitFailed { get; private set; }
        public int LastBytes { get; private set; }
        public string LastCommitReport { get; private set; }

        public WowRoster(string userId, IWowSaveSlot[] slots)
        {
            UserId = userId ?? "";
            Key = KeyFor(UserId);
            _slots = slots ?? new IWowSaveSlot[0];
            LoadReport = "";
            LastCommitReport = "";
            Problems = new List<string>();
        }

        /// <summary>The storage key for an account: versioned prefix + the id's letters, digits, '-' and '_' (a file name in the client).</summary>
        public static string KeyFor(string userId)
        {
            var sb = new StringBuilder(KeyPrefix);
            foreach (char c in userId ?? "")
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
                if (sb.Length >= 80) break;
            }
            if (sb.Length == KeyPrefix.Length) sb.Append("anonymous");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ load

        private enum CopyState { None, Good, Salvaged, Unreadable, Newer }

        public void Load()
        {
            var report = new StringBuilder();
            int n = _slots.Length;
            var states = new CopyState[n];
            var texts = new string[n];
            var gens = new long[n];
            var recs = new List<CharacterRecord>[n];
            Problems.Clear();
            ReadOnly = false;
            HadDamage = false;

            for (int i = 0; i < n; i++)
            {
                string text = null;
                bool present;
                try { present = _slots[i].TryRead(Key, out text); }
                catch (Exception e) { present = false; report.Append(_slots[i].Name).Append(": read threw ").Append(e.GetType().Name).Append("; "); HadDamage = true; }

                if (!present) { states[i] = CopyState.None; report.Append(_slots[i].Name).Append(": none; "); continue; }

                texts[i] = text ?? "";
                var problems = new List<string>();
                List<CharacterRecord> list;
                long gen;
                string reason;
                int format;
                bool whole = TryDecode(texts[i], out list, out gen, out reason, out format, problems);
                recs[i] = list;
                gens[i] = gen;

                bool newer = format > Format;
                foreach (string p in problems) if (p != null && p.StartsWith("saved by a newer", StringComparison.Ordinal)) newer = true;

                if (newer) { states[i] = CopyState.Newer; ReadOnly = true; }
                else if (whole) states[i] = CopyState.Good;
                else if (list.Count > 0) states[i] = CopyState.Salvaged;
                else states[i] = CopyState.Unreadable;

                if (states[i] != CopyState.Good) HadDamage = true;
                foreach (string p in problems) Problems.Add(_slots[i].Name + ": " + p);
                report.Append(_slots[i].Name).Append(": ").Append(states[i]).Append(" gen ").Append(gen).Append(", ").Append(list.Count).Append(" character(s)")
                      .Append(string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")").Append("; ");
            }

            // The winner: the good copy with the highest generation; else (newer or salvaged only) the readable copy with the most characters.
            int best = -1;
            for (int i = 0; i < n; i++)
                if (states[i] == CopyState.Good && (best < 0 || gens[i] > gens[best])) best = i;
            bool fromGood = best >= 0;
            if (best < 0)
                for (int i = 0; i < n; i++)
                    if ((states[i] == CopyState.Salvaged || states[i] == CopyState.Newer) && recs[i] != null && (best < 0 || recs[i].Count > recs[best].Count || (recs[i].Count == recs[best].Count && gens[i] > gens[best]))) best = i;

            _chars.Clear();
            if (best >= 0)
            {
                foreach (CharacterRecord r in recs[best])
                    if (_chars.Count < MaxCharacters && Find(r.Id) == null) _chars.Add(r);
                Generation = gens[best];
            }
            else Generation = 0;

            if (ReadOnly)
            {
                LoadReport = report.Append("READ-ONLY (a newer format is stored; nothing will be written)").ToString();
                return;
            }

            // Keep every damaged copy once before anything can overwrite it.
            for (int i = 0; i < n; i++)
                if (texts[i] != null && texts[i].Length > 0 && (states[i] == CopyState.Salvaged || states[i] == CopyState.Unreadable))
                    if (SafeWrite(_slots[i], Key + "_bad", texts[i])) report.Append(_slots[i].Name).Append(": damaged copy kept as ").Append(Key).Append("_bad; ");

            if (best < 0)
            {
                LoadReport = report.Append(HadDamage ? "no readable copy - starting with no characters" : "no saved characters yet").ToString();
                return;
            }

            // Heal: every slot that does not hold the winner gets it (the exact good text, or a fresh commit of salvaged characters).
            int healed = 0;
            bool needsCommit = !fromGood;
            if (fromGood)
            {
                for (int i = 0; i < n; i++)
                    if (i != best && (states[i] != CopyState.Good || gens[i] != gens[best]) && SafeWrite(_slots[i], Key, texts[best])) healed++;
            }
            report.Append("using ").Append(_slots[best].Name).Append(" gen ").Append(Generation).Append(" (").Append(_chars.Count).Append(" character(s))");
            if (healed > 0) report.Append(", healed ").Append(healed).Append(" copy(ies)");
            LoadReport = report.ToString();
            if (needsCommit) Commit("salvaged after damage");
        }

        /// <summary>
        /// Reads a roster text. True only when the whole copy is intact (header, body check, count); list always holds every
        /// character that passed its own check. format = the roster format read (0 when there is no header).
        /// </summary>
        public static bool TryDecode(string text, out List<CharacterRecord> list, out long gen, out string reason, out int format, List<string> problems)
        {
            list = new List<CharacterRecord>();
            gen = 0;
            reason = null;
            format = 0;

            if (string.IsNullOrEmpty(text)) { reason = "empty (unreadable or not decryptable)"; return false; }

            string norm = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int body = norm.IndexOf(CharacterRecord.Header, StringComparison.Ordinal);
            string head = body >= 0 ? norm.Substring(0, body) : norm;
            string chars = body >= 0 ? norm.Substring(body) : "";
            int count = -1;
            uint check = 0;
            bool hasCheck = false, hasHeader = false;

            foreach (string raw in head.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(Header, StringComparison.Ordinal))
                {
                    hasHeader = true;
                    int.TryParse(line.Substring(Header.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out format);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1).Trim();
                if (k == "gen") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out gen);
                else if (k == "count") int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                else if (k == "body") hasCheck = uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out check);
            }

            if (hasHeader && format > Format) { reason = "saved by a newer version of the world (roster format " + format + ", this world reads up to " + Format + ")"; return false; }

            if (chars.Length > 0) list = CharacterRecord.ParseMany(chars, problems);

            if (!hasHeader) { reason = body >= 0 ? "no roster header" : "not a Somnium WoW roster"; return false; }
            if (!hasCheck) { reason = "no body check (truncated)"; return false; }
            if (Fnv(chars) != check) { reason = "damaged (body check mismatch)"; return false; }
            if (count >= 0 && count != list.Count) { reason = "count " + count + " but " + list.Count + " readable"; return false; }
            return true;
        }

        public static string Encode(IEnumerable<CharacterRecord> records, long gen)
        {
            var list = new List<CharacterRecord>();
            foreach (CharacterRecord r in records) if (r != null) list.Add(r);
            string chars = CharacterRecord.SerializeMany(list);
            var sb = new StringBuilder(chars.Length + 96);
            sb.Append(Header).Append(' ').Append(Format.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("gen=").Append(gen.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("count=").Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("body=").Append(Fnv(chars).ToString("X8", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(chars);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ changes

        public CharacterRecord Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (CharacterRecord r in _chars) if (r.Id == id) return r;
            return null;
        }

        /// <summary>Adds a new character (refused at MaxCharacters or read-only). Does not commit.</summary>
        public bool Add(CharacterRecord r, out string refusal)
        {
            refusal = null;
            if (r == null) { refusal = "no character"; return false; }
            if (ReadOnly) { refusal = "your characters were saved by a newer version of this world and are read-only here"; return false; }
            if (_chars.Count >= MaxCharacters) { refusal = "at most " + MaxCharacters + " characters"; return false; }
            if (Find(r.Id) != null) { refusal = "already there"; return false; }
            _chars.Add(r);
            return true;
        }

        /// <summary>Replaces the character with the same Id (the session's snapshot), or adds it when missing and there is room.</summary>
        public void Put(CharacterRecord r)
        {
            if (r == null) return;
            for (int i = 0; i < _chars.Count; i++)
                if (_chars[i].Id == r.Id) { _chars[i] = r; return; }
            if (_chars.Count < MaxCharacters) _chars.Add(r);
        }

        public bool Remove(string id)
        {
            if (ReadOnly) return false;
            for (int i = 0; i < _chars.Count; i++)
                if (_chars[i].Id == id) { _chars.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>Writes the roster to every slot with the next generation. True if at least one slot took it.</summary>
        public bool Commit(string why)
        {
            if (ReadOnly)
            {
                LastCommitFailed = true;
                LastCommitReport = "read-only (a newer format is stored)";
                return false;
            }

            Generation++;
            string text = Encode(_chars, Generation);
            LastBytes = Encoding.UTF8.GetByteCount(text);
            int ok = 0;
            var sb = new StringBuilder();
            foreach (IWowSaveSlot slot in _slots)
            {
                bool w = SafeWrite(slot, Key, text);
                if (w) ok++;
                sb.Append(slot.Name).Append(w ? " ok" : " FAILED").Append("; ");
            }
            LastCommitFailed = ok == 0;
            LastCommitReport = sb.ToString().TrimEnd(' ', ';') + " (" + (why ?? "") + ")";
            return ok > 0;
        }

        private static bool SafeWrite(IWowSaveSlot slot, string key, string text)
        {
            try { return slot.Write(key, text); }
            catch (Exception) { return false; }
        }

        /// <summary>FNV-1a 32 over the UTF-16 code units (as CharacterRecord's check).</summary>
        public static uint Fnv(string s)
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }
    }

    /// <summary>Keeps the roster for this visit only (the SDK editor, where the Bridge has no storage). Static: survives leaving Play mode's room.</summary>
    public sealed class WowMemorySlot : IWowSaveSlot
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Name { get { return "memory (editor)"; } }

        public bool TryRead(string key, out string text)
        {
            lock (Values) return Values.TryGetValue(key, out text);
        }

        public bool Write(string key, string text)
        {
            lock (Values) Values[key] = text;
            return true;
        }
    }
}
