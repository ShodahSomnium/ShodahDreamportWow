using System;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Stage A1 (PLAN8 2.6): the key of the player's automatic WoW account - its hidden, generated password - kept ONLY in
    /// the player's own world storage on this PC (Somnium LocalStorage: one AES-encrypted file per key). One text key per
    /// (server, account name): "wow_autoaccount_&lt;host&gt;_&lt;NAME&gt;", the value is the key alone (16 characters of
    /// WowAutoAccount.SecretAlphabet - a stored value that is not exactly that is treated as damaged: WowAutoAccount.IsValidSecret).
    /// It is written to every slot one after the other (WowSaves gives two copies, "&lt;key&gt;" and "&lt;key&gt;_2":
    /// LocalStorage.WriteText truncates first, and a lost key means the server owner has to unlock the account again).
    /// Find returns the first copy that is a well-formed key; Forget writes the word "forgotten" over every existing copy (the
    /// proven LocalStorage members are ReadText / WriteText / TextExists - there is no delete; not an empty value, because
    /// WowBridgeSlot takes an empty read for an undecryptable file and keeps a "_bad" copy of it with a warning).
    /// Nothing here logs or returns the key in a report: reports only say which slot held a key ("world storage: key;
    /// world storage_2: none"). Pure (IWowSaveSlot): the harness runs it.
    /// </summary>
    public sealed class WowAutoKeys
    {
        public const string KeyPrefix = "wow_autoaccount_";

        /// <summary>AzerothCore's MAX_PASS_STR; the generated keys are exactly this long.</summary>
        public const int SecretLength = 16;

        /// <summary>AzerothCore's MAX_ACCOUNT_STR.</summary>
        public const int MaxNameLength = 17;

        /// <summary>What Forget writes (never a key: lower case).</summary>
        public const string Forgotten = "forgotten";

        private readonly IWowSaveSlot[] _slots;

        public WowAutoKeys(IWowSaveSlot[] slots)
        {
            _slots = slots ?? new IWowSaveSlot[0];
        }

        /// <summary>How many places the key is written to.</summary>
        public int SlotCount { get { return _slots.Length; } }

        /// <summary>
        /// The storage key for (host, name): host lower-cased with anything but a-z 0-9 . - turned into '-', at most 64
        /// characters ("none" when empty); the name upper-cased, A-Z 0-9 only, at most 17. Null when the name is not an
        /// account name.
        /// </summary>
        public static string KeyFor(string host, string name)
        {
            if (!IsAccountName(name))
            {
                return null;
            }

            var sb = new StringBuilder(KeyPrefix);
            string h = (host ?? "").Trim().ToLowerInvariant();
            int n = 0;

            foreach (char c in h)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
                sb.Append(ok ? c : '-');
                if (++n >= 64) break;
            }

            if (n == 0) sb.Append("none");
            sb.Append('_').Append(name.Trim().ToUpperInvariant());
            return sb.ToString();
        }

        /// <summary>An automatic account name: 1-17 characters A-Z / 0-9 (any letter case, surrounding spaces ignored).</summary>
        public static bool IsAccountName(string name)
        {
            if (name == null) return false;
            string n = name.Trim();
            if (n.Length == 0 || n.Length > MaxNameLength) return false;

            foreach (char c in n)
            {
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            }

            return true;
        }

        /// <summary>
        /// A stored key: exactly what the generator makes - 16 characters of WowAutoAccount.SecretAlphabet (A-Z and 2-9 without
        /// I and O). Anything else (a torn write, a hand-edited file) is a damaged copy, never sent to the server.
        /// </summary>
        public static bool IsSecret(string s)
        {
            return s != null && s.Length == SecretLength && WowAutoAccount.IsValidSecret(s);
        }

        /// <summary>
        /// The stored key for (host, name), or null. consistent = every slot holds the same key (false when a copy is
        /// missing, damaged or different - Store again once the key is known to work). Never throws; the report never
        /// contains the key.
        /// </summary>
        public string Find(string host, string name, out bool consistent, out string report)
        {
            consistent = true;
            string key = KeyFor(host, name);

            if (key == null)
            {
                report = "not an account name";
                consistent = false;
                return null;
            }

            string found = null;
            var sb = new StringBuilder();

            foreach (IWowSaveSlot slot in _slots)
            {
                string text = null;
                bool exists;

                try { exists = slot.TryRead(key, out text); }
                catch (Exception e) { exists = false; sb.Append(slot.Name).Append(": read threw ").Append(e.GetType().Name).Append("; "); consistent = false; continue; }

                string t = text != null ? text.Trim() : "";

                if (!exists)
                {
                    sb.Append(slot.Name).Append(": none; ");
                    consistent = false;
                }
                else if (IsSecret(t))
                {
                    sb.Append(slot.Name).Append(": key; ");
                    if (found == null) found = t;
                    else if (!string.Equals(found, t, StringComparison.Ordinal)) consistent = false;
                }
                else
                {
                    sb.Append(slot.Name).Append(t == Forgotten ? ": forgotten; " : t.Length == 0 ? ": empty or unreadable; " : ": not a key; ");
                    consistent = false;
                }
            }

            if (found == null) consistent = false;
            report = sb.Length > 0 ? sb.ToString().TrimEnd(' ', ';') : "no storage";
            return found;
        }

        /// <summary>True when a well-formed key is stored for (host, name).</summary>
        public bool Has(string host, string name)
        {
            bool consistent;
            string report;
            return Find(host, name, out consistent, out report) != null;
        }

        /// <summary>Writes the key to every slot (one after the other). Returns how many took it; the report never contains the key.</summary>
        public int Store(string host, string name, string secret, out string report)
        {
            string key = KeyFor(host, name);

            if (key == null || !IsSecret(secret))
            {
                report = key == null ? "not an account name" : "not a key";
                return 0;
            }

            int ok = 0;
            var sb = new StringBuilder();

            foreach (IWowSaveSlot slot in _slots)
            {
                bool w;
                try { w = slot.Write(key, secret); }
                catch (Exception e) { w = false; sb.Append(slot.Name).Append(" threw ").Append(e.GetType().Name).Append("; "); }
                if (w) ok++;
                sb.Append(slot.Name).Append(w ? " ok; " : " FAILED; ");
            }

            report = sb.Length > 0 ? sb.ToString().TrimEnd(' ', ';') : "no storage";
            return ok;
        }

        /// <summary>Overwrites every stored copy for (host, name) with <see cref="Forgotten"/> (copies that do not exist are not created). Returns how many were overwritten.</summary>
        public int Forget(string host, string name, out string report)
        {
            string key = KeyFor(host, name);

            if (key == null)
            {
                report = "not an account name";
                return 0;
            }

            int n = 0;
            var sb = new StringBuilder();

            foreach (IWowSaveSlot slot in _slots)
            {
                string text;
                bool exists;

                try { exists = slot.TryRead(key, out text); }
                catch (Exception) { exists = true; }

                if (!exists)
                {
                    sb.Append(slot.Name).Append(": none; ");
                    continue;
                }

                bool w;
                try { w = slot.Write(key, Forgotten); }
                catch (Exception e) { w = false; sb.Append(slot.Name).Append(" threw ").Append(e.GetType().Name).Append("; "); }
                if (w) n++;
                sb.Append(slot.Name).Append(w ? ": forgotten; " : ": FAILED; ");
            }

            report = sb.Length > 0 ? sb.ToString().TrimEnd(' ', ';') : "no storage";
            return n;
        }
    }
}
