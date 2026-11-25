using System;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The account's settings text (WowSettings.Serialize) kept in every slot, the WowRoster way (pure: the harness can run
    /// it): its own versioned key per Somnium account, two copies written one after the other (LocalStorage.WriteText
    /// truncates first), a header with a generation and an FNV-1a body check; the best good copy wins; a copy that exists
    /// but does not decode is kept under "&lt;key&gt;_bad" before anything overwrites it; a copy written by a newer format is
    /// read (WowSettings.Load ignores keys it does not know) but never overwritten.
    /// Text: "SOMNIUMWOW-SETTINGS 1" / "gen=N" / "body=XXXXXXXX" then the WowSettings lines.
    /// </summary>
    public sealed class WowSettingsStore
    {
        public const int Format = 1;
        public const string Header = "SOMNIUMWOW-SETTINGS";
        public const string KeyPrefix = "somniumwow_settings_v1_";

        private readonly IWowSaveSlot[] _slots;
        private long _gen;

        public string Key { get; private set; }
        public bool ReadOnly { get; private set; }
        public long Generation { get { return _gen; } }
        public string LastReport { get; private set; }
        public int LastBytes { get; private set; }

        public WowSettingsStore(string userId, IWowSaveSlot[] slots)
        {
            _slots = slots ?? new IWowSaveSlot[0];
            Key = KeyFor(userId);
            LastReport = "";
        }

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

        /// <summary>The saved settings body (null when none could be read). Never throws.</summary>
        public string Load()
        {
            var report = new StringBuilder();
            string best = null;
            long bestGen = -1;

            for (int i = 0; i < _slots.Length; i++)
            {
                IWowSaveSlot slot = _slots[i];
                string text;
                bool exists;

                try { exists = slot.TryRead(Key, out text); }
                catch (Exception e) { report.Append(slot.Name).Append(": read threw ").Append(e.GetType().Name).Append("; "); continue; }

                if (!exists)
                {
                    report.Append(slot.Name).Append(": none; ");
                    continue;
                }

                string body, reason;
                long gen;
                int format;

                if (TryDecode(text, out body, out gen, out format, out reason))
                {
                    if (format > Format)
                    {
                        ReadOnly = true;
                        report.Append(slot.Name).Append(": format ").Append(format).Append(" (newer - read-only); ");
                    }
                    else
                    {
                        report.Append(slot.Name).Append(": good gen ").Append(gen).Append("; ");
                    }

                    if (gen > bestGen) { bestGen = gen; best = body; }
                }
                else
                {
                    report.Append(slot.Name).Append(": ").Append(reason);

                    if (!string.IsNullOrEmpty(text))
                    {
                        bool kept = false;
                        try { kept = slot.Write(Key + "_bad", text); }
                        catch (Exception) { kept = false; }
                        if (kept) report.Append(" - kept as ").Append(Key).Append("_bad");
                    }

                    report.Append("; ");
                }
            }

            _gen = Math.Max(0, bestGen);
            LastReport = report.ToString().TrimEnd(' ', ';');
            return best;
        }

        /// <summary>Writes body to every slot (one after the other). False when read-only or no slot took it.</summary>
        public bool Save(string body)
        {
            if (ReadOnly)
            {
                LastReport = "read-only (saved by a newer format)";
                return false;
            }

            long gen = _gen + 1;
            string text = Encode(body ?? "", gen);
            LastBytes = text.Length;
            var report = new StringBuilder();
            int ok = 0;

            foreach (IWowSaveSlot slot in _slots)
            {
                bool w;
                try { w = slot.Write(Key, text); }
                catch (Exception e) { w = false; report.Append(slot.Name).Append(" threw ").Append(e.GetType().Name).Append("; "); }
                if (w) ok++;
                report.Append(slot.Name).Append(w ? " ok; " : " FAILED; ");
            }

            if (ok > 0) _gen = gen;
            LastReport = report.ToString().TrimEnd(' ', ';');
            return ok > 0;
        }

        public static string Encode(string body, long gen)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append(' ').Append(Format.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("gen=").Append(gen.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("body=").Append(WowRoster.Fnv(body).ToString("X8", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(body);
            return sb.ToString();
        }

        public static bool TryDecode(string text, out string body, out long gen, out int format, out string reason)
        {
            body = null;
            gen = 0;
            format = 0;

            if (string.IsNullOrEmpty(text)) { reason = "empty or undecryptable"; return false; }
            string t = text.Replace("\r\n", "\n");
            int l1 = t.IndexOf('\n');
            int l2 = l1 >= 0 ? t.IndexOf('\n', l1 + 1) : -1;
            int l3 = l2 >= 0 ? t.IndexOf('\n', l2 + 1) : -1;
            if (l3 < 0) { reason = "damaged (short)"; return false; }

            string h = t.Substring(0, l1);
            if (!h.StartsWith(Header + " ", StringComparison.Ordinal) || !int.TryParse(h.Substring(Header.Length + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out format))
            {
                reason = "damaged (header)";
                return false;
            }

            string g = t.Substring(l1 + 1, l2 - l1 - 1);
            if (!g.StartsWith("gen=", StringComparison.Ordinal) || !long.TryParse(g.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out gen))
            {
                reason = "damaged (gen)";
                return false;
            }

            string b = t.Substring(l2 + 1, l3 - l2 - 1);
            uint check;
            if (!b.StartsWith("body=", StringComparison.Ordinal) || !uint.TryParse(b.Substring(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out check))
            {
                reason = "damaged (body line)";
                return false;
            }

            string rest = t.Substring(l3 + 1);
            if (WowRoster.Fnv(rest) != check) { reason = "damaged (body check mismatch)"; return false; }
            body = rest;
            reason = "";
            return true;
        }
    }
}
