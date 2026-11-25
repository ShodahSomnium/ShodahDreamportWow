using System;
using System.Security.Cryptography;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// SRP6 exactly as AzerothCore's src/common/Cryptography/Authentication/SRP6.cpp does it (the client half), plus the
    /// shared helpers. All numbers are 32-byte LITTLE-endian arrays on the wire.
    ///   g = 7, N = 894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7 (big-endian hex; AC stores it reversed)
    ///   v = g ^ H(s || H(U || ':' || P)) mod N                (CalculateVerifier; U, P upper-cased a-z only)
    ///   B = (g^b + 3v) % N                                     (_B, so k = 3)
    ///   u = H(A || B), S = (B - 3 g^x) ^ (a + u x) mod N       (client side of "S = (A * v^u) ^ b mod N")
    ///   K = SHA1Interleave(S)                                   (40 bytes)
    ///   M1 = H(H(N) xor H(g), H(U), s, A, B, K)                (VerifyChallengeResponse "ourM")
    ///   M2 = H(A, M1, K)                                        (GetSessionVerifier)
    /// </summary>
    public static class Srp6
    {
        public const int KeyLength = 32;     // EPHEMERAL_KEY_LENGTH, SALT_LENGTH, VERIFIER_LENGTH
        public const int SessionKeyLength = 40;
        public static readonly BigNum N = BigNum.FromHex("894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7");
        public static readonly BigNum G = BigNum.FromUInt(7);
        public static readonly BigNum K3 = BigNum.FromUInt(3);

        /// <summary>SHA1 over the concatenation of the parts.</summary>
        public static byte[] Sha1(params byte[][] parts)
        {
            using (var sha = SHA1.Create())
            {
                for (int i = 0; i < parts.Length; i++) sha.TransformBlock(parts[i], 0, parts[i].Length, null, 0);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return sha.Hash;
            }
        }

        public static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s); }

        /// <summary>AzerothCore Utf8ToUpperOnlyLatin: only a-z become A-Z (wcharToUpperOnlyLatin / isBasicLatinCharacter).</summary>
        public static string UpperLatin(string s)
        {
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++) if (c[i] >= 'a' && c[i] <= 'z') c[i] = (char)(c[i] - 32);
            return new string(c);
        }

        /// <summary>x = H(s || H(U ':' P)) as a little-endian number. U and P must already be upper-cased.</summary>
        public static BigNum ComputeX(string upperUser, string upperPass, byte[] salt)
        {
            byte[] inner = Sha1(Utf8(upperUser), Utf8(":"), Utf8(upperPass));
            return BigNum.FromBytesLE(Sha1(salt, inner));
        }

        /// <summary>SRP6::CalculateVerifier (32 bytes LE). Used by the harness's fake server with a generated test password.</summary>
        public static byte[] CalculateVerifier(string upperUser, string upperPass, byte[] salt)
        {
            return BigNum.ModPow(G, ComputeX(upperUser, upperPass, salt), N).ToBytesLE(KeyLength);
        }

        /// <summary>SRP6::SHA1Interleave: split S (32 LE bytes) into even/odd halves from the first non-zero byte, hash both, interleave.</summary>
        public static byte[] Interleave(byte[] S)
        {
            const int half = KeyLength / 2;
            var buf0 = new byte[half];
            var buf1 = new byte[half];
            for (int i = 0; i < half; i++) { buf0[i] = S[2 * i]; buf1[i] = S[2 * i + 1]; }
            int p = 0;
            while (p < KeyLength && S[p] == 0) ++p;
            if ((p & 1) != 0) ++p;
            p /= 2;
            var h0in = new byte[half - p];
            var h1in = new byte[half - p];
            Array.Copy(buf0, p, h0in, 0, half - p);
            Array.Copy(buf1, p, h1in, 0, half - p);
            byte[] hash0 = Sha1(h0in), hash1 = Sha1(h1in);
            var K = new byte[SessionKeyLength];
            for (int i = 0; i < 20; i++) { K[2 * i] = hash0[i]; K[2 * i + 1] = hash1[i]; }
            return K;
        }

        /// <summary>H(N) xor H(g) with N, g as the little-endian byte arrays AzerothCore hashes (N: 32 bytes, g: 1 byte).</summary>
        public static byte[] NgHash(byte[] nBytes, byte[] gBytes)
        {
            byte[] nh = Sha1(nBytes), gh = Sha1(gBytes);
            var r = new byte[20];
            for (int i = 0; i < 20; i++) r[i] = (byte)(nh[i] ^ gh[i]);
            return r;
        }

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        public static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }
    }

    /// <summary>The client's answer to AUTH_LOGON_CHALLENGE: A, M1 to send, K and the expected M2 to keep.</summary>
    public sealed class Srp6ClientProof
    {
        public byte[] A;              // 32 LE
        public byte[] M1;             // 20
        public byte[] SessionKey;     // K, 40
        public byte[] ExpectedM2;     // 20
    }

    public static class Srp6Client
    {
        /// <summary>
        /// Computes the proof. user/pass are upper-cased here (AzerothCore stores the verifier of the upper-cased pair, see
        /// AccountMgr::CreateAccount "Utf8ToUpperOnlyLatin(username); Utf8ToUpperOnlyLatin(password);"). gBytes/nBytes/salt/B are
        /// exactly as received (little-endian). Returns null (with reason) when the server values are unsafe.
        /// </summary>
        public static Srp6ClientProof Compute(string user, string pass, byte[] B, byte[] gBytes, byte[] nBytes, byte[] salt, out string error)
        {
            error = null;
            string U = Srp6.UpperLatin(user), P = Srp6.UpperLatin(pass);
            BigNum n = BigNum.FromBytesLE(nBytes), g = BigNum.FromBytesLE(gBytes), b = BigNum.FromBytesLE(B);
            if (n.BitLength < 200) { error = "server sent a weak SRP modulus"; return null; }
            if (BigNum.Mod(b, n).IsZero) { error = "server sent an invalid SRP value"; return null; }
            BigNum x = Srp6.ComputeX(U, P, salt);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                BigNum a = BigNum.FromBytesLE(Srp6.RandomBytes(32)); // private ephemeral, never leaves this method
                BigNum A = BigNum.ModPow(g, a, n);
                if (A.IsZero) continue;
                byte[] aBytes = A.ToBytesLE(Srp6.KeyLength);
                BigNum u = BigNum.FromBytesLE(Srp6.Sha1(aBytes, B));
                if (u.IsZero) continue;
                // S = (B - k * g^x) ^ (a + u*x) mod N ; keep it non-negative: B + k*(N - g^x mod N)
                BigNum gx = BigNum.ModPow(g, x, n);
                BigNum kgx = BigNum.Mod(BigNum.Mul(Srp6.K3, gx), n);
                BigNum baseV = BigNum.Mod(BigNum.Add(BigNum.Mod(b, n), BigNum.Sub(n, kgx)), n);
                BigNum exp = BigNum.Add(a, BigNum.Mul(u, x));
                byte[] S = BigNum.ModPow(baseV, exp, n).ToBytesLE(Srp6.KeyLength);
                byte[] K = Srp6.Interleave(S);
                byte[] M1 = Srp6.Sha1(Srp6.NgHash(nBytes, gBytes), Srp6.Sha1(Srp6.Utf8(U)), salt, aBytes, B, K);
                return new Srp6ClientProof { A = aBytes, M1 = M1, SessionKey = K, ExpectedM2 = Srp6.Sha1(aBytes, M1, K) };
            }
            error = "could not compute the SRP proof";
            return null;
        }
    }
}
