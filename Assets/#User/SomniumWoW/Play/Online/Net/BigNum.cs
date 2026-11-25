using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// Minimal unsigned big integer for SRP6 (System.Numerics is not allowed in the Somnium client). Immutable, magnitude in
    /// 32-bit limbs, least significant first. Bytes in and out are LITTLE-endian, like AzerothCore's BigNumber defaults
    /// ("BigNumber(std::array const&amp; v, bool littleEndian = true)", "ToByteArray(bool littleEndian = true)").
    /// Sizes here are 256-bit operands (512-bit products), so plain schoolbook multiply + bitwise long division is plenty fast.
    /// </summary>
    public sealed class BigNum : IComparable<BigNum>
    {
        readonly uint[] _m;   // normalized: no leading zero limbs; zero = length 0

        public static readonly BigNum Zero = new BigNum(new uint[0]);
        public static readonly BigNum One = new BigNum(new uint[] { 1 });

        BigNum(uint[] m) { _m = Normalize(m); }

        static uint[] Normalize(uint[] m)
        {
            int n = m.Length;
            while (n > 0 && m[n - 1] == 0) n--;
            if (n == m.Length) return m;
            var r = new uint[n];
            Array.Copy(m, r, n);
            return r;
        }

        public static BigNum FromUInt(uint v) { return new BigNum(new uint[] { v }); }

        /// <summary>Little-endian bytes (byte 0 = least significant).</summary>
        public static BigNum FromBytesLE(byte[] b) { return FromBytesLE(b, 0, b.Length); }

        public static BigNum FromBytesLE(byte[] b, int offset, int count)
        {
            var m = new uint[(count + 3) / 4];
            for (int i = 0; i < count; i++) m[i >> 2] |= (uint)b[offset + i] << ((i & 3) * 8);
            return new BigNum(m);
        }

        /// <summary>Little-endian bytes, zero padded to size (AzerothCore ToByteArray&lt;size&gt;). Throws when it does not fit.</summary>
        public byte[] ToBytesLE(int size)
        {
            var r = new byte[size];
            for (int i = 0; i < _m.Length * 4; i++)
            {
                byte v = (byte)(_m[i >> 2] >> ((i & 3) * 8));
                if (i < size) r[i] = v;
                else if (v != 0) throw new InvalidOperationException("BigNum does not fit in " + size + " bytes");
            }
            return r;
        }

        public bool IsZero { get { return _m.Length == 0; } }

        public int BitLength
        {
            get
            {
                if (_m.Length == 0) return 0;
                uint top = _m[_m.Length - 1];
                int bits = 0;
                while (top != 0) { bits++; top >>= 1; }
                return (_m.Length - 1) * 32 + bits;
            }
        }

        bool Bit(int i)
        {
            int limb = i >> 5;
            return limb < _m.Length && ((_m[limb] >> (i & 31)) & 1) != 0;
        }

        public int CompareTo(BigNum o)
        {
            if (_m.Length != o._m.Length) return _m.Length < o._m.Length ? -1 : 1;
            for (int i = _m.Length - 1; i >= 0; i--)
                if (_m[i] != o._m[i]) return _m[i] < o._m[i] ? -1 : 1;
            return 0;
        }

        public override bool Equals(object obj) { var o = obj as BigNum; return o != null && CompareTo(o) == 0; }
        public override int GetHashCode() { int h = 17; for (int i = 0; i < _m.Length; i++) h = h * 31 + (int)_m[i]; return h; }

        public static BigNum Add(BigNum a, BigNum b)
        {
            int n = Math.Max(a._m.Length, b._m.Length);
            var r = new uint[n + 1];
            ulong carry = 0;
            for (int i = 0; i < n; i++)
            {
                ulong s = carry;
                if (i < a._m.Length) s += a._m[i];
                if (i < b._m.Length) s += b._m[i];
                r[i] = (uint)s;
                carry = s >> 32;
            }
            r[n] = (uint)carry;
            return new BigNum(r);
        }

        /// <summary>a - b, requires a &gt;= b.</summary>
        public static BigNum Sub(BigNum a, BigNum b)
        {
            if (a.CompareTo(b) < 0) throw new InvalidOperationException("BigNum.Sub would be negative");
            var r = new uint[a._m.Length];
            long borrow = 0;
            for (int i = 0; i < a._m.Length; i++)
            {
                long d = (long)a._m[i] - borrow - (i < b._m.Length ? b._m[i] : 0u);
                borrow = d < 0 ? 1 : 0;
                r[i] = (uint)(d + (borrow << 32));
            }
            return new BigNum(r);
        }

        public static BigNum Mul(BigNum a, BigNum b)
        {
            if (a.IsZero || b.IsZero) return Zero;
            var r = new uint[a._m.Length + b._m.Length];
            for (int i = 0; i < a._m.Length; i++)
            {
                ulong carry = 0;
                ulong ai = a._m[i];
                for (int j = 0; j < b._m.Length; j++)
                {
                    ulong t = ai * b._m[j] + r[i + j] + carry;
                    r[i + j] = (uint)t;
                    carry = t >> 32;
                }
                r[i + b._m.Length] = (uint)carry;
            }
            return new BigNum(r);
        }

        /// <summary>a mod m (bitwise long division; m must be non-zero).</summary>
        public static BigNum Mod(BigNum a, BigNum m)
        {
            if (m.IsZero) throw new DivideByZeroException();
            if (a.CompareTo(m) < 0) return a;
            int n = m._m.Length + 1;
            var r = new uint[n];
            for (int bit = a.BitLength - 1; bit >= 0; bit--)
            {
                // r = (r << 1) | bit
                uint carry = a.Bit(bit) ? 1u : 0u;
                for (int i = 0; i < n; i++)
                {
                    uint next = r[i] >> 31;
                    r[i] = (r[i] << 1) | carry;
                    carry = next;
                }
                if (GreaterOrEqual(r, m._m))
                {
                    long borrow = 0;
                    for (int i = 0; i < n; i++)
                    {
                        long d = (long)r[i] - borrow - (i < m._m.Length ? m._m[i] : 0u);
                        borrow = d < 0 ? 1 : 0;
                        r[i] = (uint)(d + (borrow << 32));
                    }
                }
            }
            return new BigNum(r);
        }

        static bool GreaterOrEqual(uint[] r, uint[] m)
        {
            for (int i = r.Length - 1; i >= 0; i--)
            {
                uint mi = i < m.Length ? m[i] : 0u;
                if (r[i] != mi) return r[i] > mi;
            }
            return true;
        }

        /// <summary>(b ^ e) mod m, square-and-multiply.</summary>
        public static BigNum ModPow(BigNum b, BigNum e, BigNum m)
        {
            BigNum result = Mod(One, m);
            BigNum x = Mod(b, m);
            int bits = e.BitLength;
            for (int i = 0; i < bits; i++)
            {
                if (e.Bit(i)) result = Mod(Mul(result, x), m);
                if (i + 1 < bits) x = Mod(Mul(x, x), m);
            }
            return result;
        }

        /// <summary>Big-endian hex (for tests and logs of PUBLIC values only).</summary>
        public string ToHex()
        {
            if (IsZero) return "0";
            var sb = new System.Text.StringBuilder();
            for (int i = _m.Length - 1; i >= 0; i--) sb.Append(i == _m.Length - 1 ? _m[i].ToString("X") : _m[i].ToString("X8"));
            return sb.ToString();
        }

        /// <summary>Parses big-endian hex.</summary>
        public static BigNum FromHex(string hex)
        {
            var bytes = new byte[(hex.Length + 1) / 2];
            for (int i = 0; i < hex.Length; i++)
            {
                int c = hex[hex.Length - 1 - i];
                int v = c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
                bytes[i >> 1] |= (byte)(v << ((i & 1) * 4));
            }
            return FromBytesLE(bytes);
        }
    }
}
