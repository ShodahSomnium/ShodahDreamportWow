// PKWARE Data Compression Library "explode" decoder.
// C# port of blast.c (version 1.3, 24 Aug 2013) by Mark Adler, distributed with zlib under the zlib license:
//
//   Copyright (C) 2003, 2012, 2013 Mark Adler
//   This software is provided 'as-is', without any express or implied warranty. In no event will the author be
//   held liable for any damages arising from the use of this software. Permission is granted to anyone to use this
//   software for any purpose, including commercial applications, and to alter it and redistribute it freely,
//   subject to the following restrictions: 1. The origin of this software must not be misrepresented; you must not
//   claim that you wrote the original software. If you use this software in a product, an acknowledgment in the
//   product documentation would be appreciated but is not required. 2. Altered source versions must be plainly
//   marked as such, and must not be misrepresented as being the original software. 3. This notice may not be
//   removed or altered from any source distribution.
//
// Altered: ported to C#, writes straight into a caller buffer (the whole sector is the window), stops when the
// output is full or at the end code.
using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.Data.Mpq
{
    public static class PkwareExplode
    {
        private const int MaxBits = 13;

        private sealed class Huffman
        {
            public readonly short[] Count = new short[MaxBits + 1];
            public readonly short[] Symbol;
            public int Complete;   // construct() result: 0 = complete code
            public Huffman(int n) { Symbol = new short[n]; }
        }

        // Code lengths (compact run-length form) from blast.c.
        private static readonly byte[] LitLen =
        {
            11, 124, 8, 7, 28, 7, 188, 13, 76, 4, 10, 8, 12, 10, 12, 10, 8, 23, 8,
            9, 7, 6, 7, 8, 7, 6, 55, 8, 23, 24, 12, 11, 7, 9, 11, 12, 6, 7, 22, 5,
            7, 24, 6, 11, 9, 6, 7, 22, 7, 11, 38, 7, 9, 8, 25, 11, 8, 11, 9, 12,
            8, 12, 5, 38, 5, 38, 5, 11, 7, 5, 6, 21, 6, 10, 53, 8, 7, 24, 10, 27,
            44, 253, 253, 253, 252, 252, 252, 13, 12, 45, 12, 45, 12, 61, 12, 45,
            44, 173
        };
        private static readonly byte[] LenLen = { 2, 35, 36, 53, 38, 23 };
        private static readonly byte[] DistLen = { 2, 20, 53, 230, 247, 151, 248 };
        private static readonly short[] Base = { 3, 2, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 40, 72, 136, 264 };
        private static readonly byte[] Extra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        private static readonly Huffman LitCode = Construct(LitLen, 256);
        private static readonly Huffman LenCode = Construct(LenLen, 16);
        private static readonly Huffman DistCode = Construct(DistLen, 64);

        /// <summary>0 when all three fixed codes are complete (self-check of the tables; the harness prints it).</summary>
        public static string TableCheck
        {
            get { return "lit " + LitCode.Complete + " len " + LenCode.Complete + " dist " + DistCode.Complete; }
        }

        private static Huffman Construct(byte[] rep, int maxSymbols)
        {
            var length = new short[256];
            int symbol = 0;
            for (int r = 0; r < rep.Length; r++)
            {
                int len = rep[r];
                int left = (len >> 4) + 1;
                len &= 15;
                do { length[symbol++] = (short)len; } while (--left > 0);
            }
            int n = symbol;
            var h = new Huffman(Math.Max(n, maxSymbols));
            for (int i = 0; i < n; i++) h.Count[length[i]]++;
            if (h.Count[0] == n) { h.Complete = 0; return h; }
            int leftCodes = 1;
            for (int len = 1; len <= MaxBits; len++)
            {
                leftCodes <<= 1;
                leftCodes -= h.Count[len];
                if (leftCodes < 0) { h.Complete = leftCodes; return h; }
            }
            var offs = new short[MaxBits + 1];
            offs[1] = 0;
            for (int len = 1; len < MaxBits; len++) offs[len + 1] = (short)(offs[len] + h.Count[len]);
            for (int s = 0; s < n; s++)
                if (length[s] != 0) h.Symbol[offs[length[s]]++] = (short)s;
            h.Complete = leftCodes;
            return h;
        }

        private struct Bits
        {
            public byte[] Src;
            public int Pos, End;
            public int BitBuf, BitCnt;

            public int Get(int need)
            {
                int val = BitBuf;
                while (BitCnt < need)
                {
                    if (Pos >= End) throw new InvalidDataException("PKWARE: input ran out");
                    val |= Src[Pos++] << BitCnt;
                    BitCnt += 8;
                }
                BitBuf = val >> need;
                BitCnt -= need;
                return val & ((1 << need) - 1);
            }

            public int Decode(Huffman h)
            {
                int code = 0, first = 0, index = 0;
                for (int len = 1; len <= MaxBits; len++)
                {
                    code |= Get(1) ^ 1;   // codes are stored bit-inverted
                    int count = h.Count[len];
                    if (code - count < first) return h.Symbol[index + (code - first)];
                    index += count;
                    first += count;
                    first <<= 1;
                    code <<= 1;
                }
                throw new InvalidDataException("PKWARE: bad code");
            }
        }

        /// <summary>Explodes src into dst; returns the number of bytes written (stops at the end code or when dst is full).</summary>
        public static int Explode(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstLen)
        {
            var s = new Bits { Src = src, Pos = srcOff, End = srcOff + srcLen };
            int lit = s.Get(8);
            if (lit > 1) throw new InvalidDataException("PKWARE: bad literal mode " + lit);
            int dict = s.Get(8);
            if (dict < 4 || dict > 6) throw new InvalidDataException("PKWARE: bad dictionary size " + dict);
            int o = dstOff, end = dstOff + dstLen;
            while (o < end)
            {
                if (s.Get(1) != 0)
                {
                    int symbol = s.Decode(LenCode);
                    int len = Base[symbol] + s.Get(Extra[symbol]);
                    if (len == 519) break;   // end code
                    int shift = len == 2 ? 2 : dict;
                    int dist = (s.Decode(DistCode) << shift) + s.Get(shift) + 1;
                    if (dist > o - dstOff) throw new InvalidDataException("PKWARE: distance too far back");
                    if (len > end - o) len = end - o;
                    for (int k = 0; k < len; k++, o++) dst[o] = dst[o - dist];
                }
                else
                {
                    dst[o++] = (byte)(lit != 0 ? s.Decode(LitCode) : s.Get(8));
                }
            }
            return o - dstOff;
        }
    }
}
