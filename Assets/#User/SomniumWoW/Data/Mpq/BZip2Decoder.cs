// Pure C# bzip2 decoder, written for the WoW Dreamport from the published bzip2 stream format (Julian Seward's
// bzip2 1.0.x: "BZh" + level, 0x314159265359 blocks, MTF/RLE2 + Huffman groups of 50, inverse BWT, RLE1, CRC32/BE).
// Original code, no third-party source copied. Public domain / same licence as the rest of the world's code.
// Randomised blocks (a pre-0.9.5 bzip2 feature, never produced by modern encoders) are rejected.
using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.Data.Mpq
{
    public static class BZip2Decoder
    {
        private const int MaxGroups = 6;
        private const int GroupSize = 50;
        private const int MaxAlpha = 258;
        private const int MaxCodeLen = 23;
        private const int MaxSelectors = 18002;

        private static readonly uint[] CrcTable = BuildCrc();

        private static uint[] BuildCrc()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++) c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : c << 1;
                t[i] = c;
            }
            return t;
        }

        private sealed class BitReader
        {
            private readonly byte[] _src;
            private int _pos;
            private readonly int _end;
            private ulong _buf;
            private int _cnt;

            public BitReader(byte[] src, int off, int len) { _src = src; _pos = off; _end = off + len; }

            public bool AtEnd { get { return _cnt == 0 && _pos >= _end; } }

            public int Get(int n)   // n <= 32, MSB first
            {
                while (_cnt < n)
                {
                    if (_pos >= _end) throw new InvalidDataException("bzip2: input ran out");
                    _buf = (_buf << 8) | _src[_pos++];
                    _cnt += 8;
                }
                _cnt -= n;
                return (int)((_buf >> _cnt) & ((1UL << n) - 1));
            }

            public int Bit() { return Get(1); }

            public void Align() { _cnt -= _cnt % 8; }
        }

        /// <summary>Decodes a whole bzip2 stream (or several concatenated) into dst; returns bytes written.</summary>
        public static int Decode(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstLen)
        {
            var br = new BitReader(src, srcOff, srcLen);
            int o = dstOff, end = dstOff + dstLen;
            bool firstStream = true;
            while (true)
            {
                if (!firstStream && (br.AtEnd || o >= end)) break;
                if (br.Get(8) != 'B' || br.Get(8) != 'Z' || br.Get(8) != 'h') throw new InvalidDataException("bzip2: bad stream magic");
                int level = br.Get(8) - '0';
                if (level < 1 || level > 9) throw new InvalidDataException("bzip2: bad block size");
                firstStream = false;
                int maxBlock = level * 100000;
                int[] tt = new int[maxBlock];
                while (true)
                {
                    long magic = ((long)br.Get(24) << 24) | (uint)br.Get(24);
                    if (magic == 0x177245385090L) { br.Get(32); break; }   // end of stream + combined CRC
                    if (magic != 0x314159265359L) throw new InvalidDataException("bzip2: bad block magic");
                    uint storedCrc = (uint)br.Get(32);
                    o = DecodeBlock(br, tt, maxBlock, dst, o, end, storedCrc);
                }
                // Streams are byte-aligned after the end-of-stream marker's padding; the reader pads at byte level.
                AlignToByte(br);
                if (br.AtEnd) break;
            }
            return o - dstOff;
        }

        private static void AlignToByte(BitReader br) { br.Align(); }

        private static int DecodeBlock(BitReader br, int[] tt, int maxBlock, byte[] dst, int o, int end, uint storedCrc)
        {
            if (br.Bit() != 0) throw new InvalidDataException("bzip2: randomised blocks are not supported");
            int origPtr = br.Get(24);

            // Symbol map.
            var seqToUnseq = new byte[256];
            int nInUse = 0;
            int used16 = br.Get(16);
            for (int i = 0; i < 16; i++)
            {
                if ((used16 & (0x8000 >> i)) == 0) continue;
                int bits = br.Get(16);
                for (int j = 0; j < 16; j++)
                    if ((bits & (0x8000 >> j)) != 0) seqToUnseq[nInUse++] = (byte)(i * 16 + j);
            }
            if (nInUse == 0) throw new InvalidDataException("bzip2: no symbols");
            int alphaSize = nInUse + 2;

            int nGroups = br.Get(3);
            if (nGroups < 2 || nGroups > MaxGroups) throw new InvalidDataException("bzip2: bad group count");
            int nSelectors = br.Get(15);
            if (nSelectors < 1) throw new InvalidDataException("bzip2: no selectors");

            // Selectors (MTF coded unary).
            var mtfGroups = new byte[MaxGroups];
            for (int i = 0; i < nGroups; i++) mtfGroups[i] = (byte)i;
            var selectors = new byte[Math.Min(nSelectors, MaxSelectors)];
            for (int i = 0; i < nSelectors; i++)
            {
                int j = 0;
                while (br.Bit() != 0) { j++; if (j >= nGroups) throw new InvalidDataException("bzip2: bad selector"); }
                byte v = mtfGroups[j];
                for (; j > 0; j--) mtfGroups[j] = mtfGroups[j - 1];
                mtfGroups[0] = v;
                if (i < selectors.Length) selectors[i] = v;
            }
            nSelectors = selectors.Length;

            // Code lengths (delta coded) and decode tables.
            var limit = new int[MaxGroups, MaxCodeLen];
            var baseT = new int[MaxGroups, MaxCodeLen];
            var perm = new int[MaxGroups, MaxAlpha];
            var minLens = new int[MaxGroups];
            var len = new byte[MaxAlpha];
            for (int t = 0; t < nGroups; t++)
            {
                int curr = br.Get(5);
                int minLen = 32, maxLen = 0;
                for (int i = 0; i < alphaSize; i++)
                {
                    while (true)
                    {
                        if (curr < 1 || curr > 20) throw new InvalidDataException("bzip2: bad code length");
                        if (br.Bit() == 0) break;
                        if (br.Bit() == 0) curr++; else curr--;
                    }
                    len[i] = (byte)curr;
                    if (curr < minLen) minLen = curr;
                    if (curr > maxLen) maxLen = curr;
                }
                // hbCreateDecodeTables
                int pp = 0;
                for (int i = minLen; i <= maxLen; i++)
                    for (int j = 0; j < alphaSize; j++)
                        if (len[j] == i) perm[t, pp++] = j;
                var b = new int[MaxCodeLen];
                for (int i = 0; i < alphaSize; i++) b[len[i] + 1]++;
                for (int i = 1; i < MaxCodeLen; i++) b[i] += b[i - 1];
                var lim = new int[MaxCodeLen];
                int vec = 0;
                for (int i = minLen; i <= maxLen; i++)
                {
                    vec += b[i + 1] - b[i];
                    lim[i] = vec - 1;
                    vec <<= 1;
                }
                for (int i = minLen + 1; i <= maxLen; i++) b[i] = ((lim[i - 1] + 1) << 1) - b[i];
                for (int i = 0; i < MaxCodeLen; i++) { limit[t, i] = lim[i]; baseT[t, i] = b[i]; }
                // Codes longer than maxLen must never match.
                for (int i = maxLen + 1; i < MaxCodeLen; i++) limit[t, i] = int.MaxValue;
                minLens[t] = minLen;
            }

            // MTF / RLE2 decode into tt (low byte = symbol).
            int eob = nInUse + 1;
            var mtf = new byte[256];
            for (int i = 0; i < 256; i++) mtf[i] = (byte)i;
            var unzftab = new int[256];
            int nblock = 0;
            int groupNo = -1, groupPos = 0, g = 0;
            int runLen = 0, runN = 1;
            bool inRun = false;
            while (true)
            {
                if (groupPos == 0)
                {
                    groupNo++;
                    if (groupNo >= nSelectors) throw new InvalidDataException("bzip2: ran out of selectors");
                    groupPos = GroupSize;
                    g = selectors[groupNo];
                }
                groupPos--;
                int zn = minLens[g];
                int zvec = br.Get(zn);
                while (zvec > limit[g, zn])
                {
                    zn++;
                    if (zn >= MaxCodeLen) throw new InvalidDataException("bzip2: bad Huffman code");
                    zvec = (zvec << 1) | br.Bit();
                }
                int idx = zvec - baseT[g, zn];
                if (idx < 0 || idx >= MaxAlpha) throw new InvalidDataException("bzip2: bad Huffman index");
                int sym = perm[g, idx];

                if (sym <= 1)   // RUNA / RUNB
                {
                    if (!inRun) { inRun = true; runLen = 0; runN = 1; }
                    runLen += (sym + 1) * runN;
                    runN <<= 1;
                    if (runLen > maxBlock) throw new InvalidDataException("bzip2: run too long");
                    continue;
                }
                if (inRun)
                {
                    inRun = false;
                    byte uc = seqToUnseq[mtf[0]];
                    if (nblock + runLen > maxBlock) throw new InvalidDataException("bzip2: block overflow");
                    unzftab[uc] += runLen;
                    for (int k = 0; k < runLen; k++) tt[nblock++] = uc;
                }
                if (sym == eob) break;
                {
                    int m = sym - 1;
                    byte v = mtf[m];
                    Array.Copy(mtf, 0, mtf, 1, m);   // overlapping move (Array.Copy is memmove-safe)
                    mtf[0] = v;
                    byte uc = seqToUnseq[v];
                    if (nblock >= maxBlock) throw new InvalidDataException("bzip2: block overflow");
                    unzftab[uc]++;
                    tt[nblock++] = uc;
                }
            }
            if (origPtr < 0 || origPtr >= nblock) throw new InvalidDataException("bzip2: bad origPtr");

            // Inverse BWT.
            var cftab = new int[257];
            for (int i = 0; i < 256; i++) cftab[i + 1] = cftab[i] + unzftab[i];
            for (int i = 0; i < nblock; i++)
            {
                int uc = tt[i] & 0xFF;
                tt[cftab[uc]] |= i << 8;
                cftab[uc]++;
            }

            // Walk + RLE1 undo + CRC.
            uint crc = 0xFFFFFFFF;
            int tPos = tt[origPtr] >> 8;
            int last = -1, same = 0;
            for (int n = 0; n < nblock; n++)
            {
                tPos = tt[tPos];
                int ch = tPos & 0xFF;
                tPos >>= 8;
                if (same == 4)
                {
                    // ch is a repeat count for the previous byte.
                    for (int k = 0; k < ch; k++)
                    {
                        if (o >= end) throw new InvalidDataException("bzip2: output overflow");
                        dst[o++] = (byte)last;
                        crc = (crc << 8) ^ CrcTable[((crc >> 24) ^ (uint)last) & 0xFF];
                    }
                    same = 0;
                    last = -1;
                    continue;
                }
                if (ch == last) same++; else { same = 1; last = ch; }
                if (o >= end) throw new InvalidDataException("bzip2: output overflow");
                dst[o++] = (byte)ch;
                crc = (crc << 8) ^ CrcTable[((crc >> 24) ^ (uint)ch) & 0xFF];
            }
            crc = ~crc;
            if (crc != storedCrc) throw new InvalidDataException("bzip2: block CRC mismatch");
            // Reset tt for the next block.
            Array.Clear(tt, 0, nblock);
            return o;
        }
    }
}
