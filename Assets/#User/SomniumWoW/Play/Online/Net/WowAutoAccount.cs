using System;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// The automatic WoW account bound to the Somnium account (PLAN8 2.1-2.3). Pure C#, no network:
    ///   NameFor(somniumId)  "cf5c553c-61f8-43f4-a290-4985ca328eb4" -&gt; "SCF5C553C61F843F4" ("S" + the first 16 hex digits,
    ///                       upper case, dashes removed; 17 characters = AzerothCore MAX_ACCOUNT_STR). Not a GUID -&gt; null.
    ///   NewSecret()         the hidden password: 16 characters from ABCDEFGHJKLMNPQRSTUVWXYZ23456789 (no 0/O/1/I),
    ///                       cryptographically random, unbiased. It lives only in the player's own Somnium LocalStorage.
    ///   MakeRegistration()  what the account service stores: a random 32-byte salt and the SRP6 verifier, exactly as
    ///                       AzerothCore's SRP6::MakeRegistrationData / CalculateVerifier (see Srp6.CalculateVerifier), as
    ///                       64 upper-case hex characters each, byte 0 first (= account.salt / account.verifier binary(32)).
    /// The secret itself is never logged, shown or sent anywhere: only the salt and the verifier leave this class.
    /// </summary>
    public static class WowAutoAccount
    {
        /// <summary>The secret's characters: A-Z and 2-9 without 0/O/1/I (AzerothCore upper-cases passwords, so upper case only).</summary>
        public const string SecretAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        /// <summary>AzerothCore MAX_PASS_STR.</summary>
        public const int SecretLength = 16;
        /// <summary>AzerothCore MAX_ACCOUNT_STR ("S" + 16 hex digits).</summary>
        public const int NameLength = 17;

        /// <summary>
        /// The account name for a Somnium ID, or null when the ID is not a GUID (8-4-4-4-12 hex digits, any case, exactly
        /// 36 characters: no spaces, no braces). Null = no automatic account for this player.
        /// </summary>
        public static string NameFor(string somniumId)
        {
            if (!IsSomniumId(somniumId)) return null;
            var sb = new StringBuilder(NameLength);
            sb.Append('S');
            for (int i = 0; i < somniumId.Length && sb.Length < NameLength; i++)
            {
                char c = somniumId[i];
                if (c == '-') continue;
                if (c >= 'a' && c <= 'f') c = (char)(c - 32);
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>True when s is a GUID in the 8-4-4-4-12 form (ASCII hex digits only, any case, no surrounding characters).</summary>
        public static bool IsSomniumId(string s)
        {
            if (s == null || s.Length != 36) return false;
            for (int i = 0; i < 36; i++)
            {
                char c = s[i];
                if (i == 8 || i == 13 || i == 18 || i == 23) { if (c != '-') return false; }
                else if (!IsHexDigit(c)) return false;
            }
            return true;
        }

        /// <summary>A new random secret (16 characters from SecretAlphabet).</summary>
        public static string NewSecret()
        {
            // Rejection sampling: accept a random byte only below the largest multiple of the alphabet size (256 for 32
            // characters, so no byte is ever rejected with this alphabet - the code stays unbiased for any alphabet).
            int n = SecretAlphabet.Length;
            int limit = 256 - 256 % n;
            var chars = new char[SecretLength];
            int filled = 0;
            while (filled < SecretLength)
            {
                byte[] rnd = Srp6.RandomBytes(SecretLength * 2);
                for (int i = 0; i < rnd.Length && filled < SecretLength; i++)
                    if (rnd[i] < limit) chars[filled++] = SecretAlphabet[rnd[i] % n];
                Array.Clear(rnd, 0, rnd.Length);
            }
            string s = new string(chars);
            Array.Clear(chars, 0, chars.Length);
            return s;
        }

        /// <summary>True when s looks like a secret made by NewSecret (16 characters, all from SecretAlphabet). Use it to reject a damaged stored key.</summary>
        public static bool IsValidSecret(string s)
        {
            if (s == null || s.Length != SecretLength) return false;
            for (int i = 0; i < s.Length; i++) if (SecretAlphabet.IndexOf(s[i]) < 0) return false;
            return true;
        }

        /// <summary>
        /// A random 32-byte salt and the SRP6 verifier for (name, secret), both as 64 upper-case hex characters, byte 0 first.
        /// name and secret are upper-cased (a-z only) first, like AzerothCore's AccountMgr. Throws ArgumentException for an
        /// empty or too long name/secret (the message never contains the secret).
        /// </summary>
        public static void MakeRegistration(string name, string secret, out string saltHex, out string verifierHex)
        {
            MakeRegistration(name, secret, Srp6.RandomBytes(Srp6.KeyLength), out saltHex, out verifierHex);
        }

        /// <summary>MakeRegistration with a given 32-byte salt (for the PLAN8 2.3 test vector; real registrations use a random salt).</summary>
        public static void MakeRegistration(string name, string secret, byte[] salt, out string saltHex, out string verifierHex)
        {
            if (string.IsNullOrEmpty(name) || name.Length > NameLength) throw new ArgumentException("account name must be 1-" + NameLength + " characters", "name");
            if (string.IsNullOrEmpty(secret) || secret.Length > SecretLength) throw new ArgumentException("password must be 1-" + SecretLength + " characters", "secret");
            if (salt == null || salt.Length != Srp6.KeyLength) throw new ArgumentException("salt must be " + Srp6.KeyLength + " bytes", "salt");
            byte[] verifier = Srp6.CalculateVerifier(Srp6.UpperLatin(name), Srp6.UpperLatin(secret), salt);
            saltHex = ToHex(salt);
            verifierHex = ToHex(verifier);
        }

        /// <summary>Upper-case hex, byte 0 first.</summary>
        public static string ToHex(byte[] b)
        {
            const string digits = "0123456789ABCDEF";
            var c = new char[b.Length * 2];
            for (int i = 0; i < b.Length; i++) { c[2 * i] = digits[b[i] >> 4]; c[2 * i + 1] = digits[b[i] & 15]; }
            return new string(c);
        }

        static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
