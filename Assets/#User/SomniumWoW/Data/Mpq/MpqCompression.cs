using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.Data.Mpq
{
    /// <summary>
    /// Multi-method sector decompression (StormLib SCompDecompress): the first byte is a mask; the methods are undone
    /// in StormLib's order bzip2 (0x10), PKWARE DCL (0x08), zlib (0x02), Huffman (0x01), ADPCM stereo (0x80),
    /// ADPCM mono (0x40), sparse (0x20). Huffman/ADPCM are only used for WAV audio and are NOT implemented: such a
    /// sector throws NotSupportedException (logged once per codec) instead of returning garbage.
    /// Usage counters (for the harness census) are kept per mask.
    /// </summary>
    public static class MpqCompression
    {
        public const byte Huffman = 0x01;
        public const byte Zlib = 0x02;
        public const byte Pkware = 0x08;
        public const byte Bzip2 = 0x10;
        public const byte Sparse = 0x20;
        public const byte AdpcmMono = 0x40;
        public const byte AdpcmStereo = 0x80;
        public const byte Lzma = 0x12;

        /// <summary>How many sectors were seen per mask byte (index = mask). Read by the harness census.</summary>
        public static readonly long[] MaskCounts = new long[256];

        private static int _warnedHuffman, _warnedAdpcm, _warnedOther;

        /// <summary>Decompresses one unit; returns the number of bytes written to dst.</summary>
        public static int Decompress(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstLen, string what)
        {
            if (srcLen < 1) throw new InvalidDataException(what + ": empty compressed unit");
            byte mask = src[srcOff];
            Interlocked.Increment(ref MaskCounts[mask]);
            int dataOff = srcOff + 1, dataLen = srcLen - 1;

            if (mask == Lzma) throw Unsupported(ref _warnedOther, "LZMA (0x12)", what);
            if ((mask & (Huffman | AdpcmMono | AdpcmStereo)) != 0)
            {
                if ((mask & Huffman) != 0 && (mask & (AdpcmMono | AdpcmStereo)) == 0)
                    throw Unsupported(ref _warnedHuffman, "Huffman (0x01, WAV audio only)", what);
                throw Unsupported(ref _warnedAdpcm, "ADPCM/Huffman mask 0x" + mask.ToString("X2") + " (WAV audio only)", what);
            }
            if ((mask & Sparse) != 0) throw Unsupported(ref _warnedOther, "sparse (0x20)", what);
            int known = Bzip2 | Pkware | Zlib;
            if ((mask & ~known) != 0 || mask == 0) throw Unsupported(ref _warnedOther, "mask 0x" + mask.ToString("X2"), what);

            // Chain: each stage feeds the next; StormLib gives every stage the full output size.
            byte[] curBuf = src; int curOff = dataOff, curLen = dataLen;
            int stages = 0;
            if ((mask & Bzip2) != 0) stages++;
            if ((mask & Pkware) != 0) stages++;
            if ((mask & Zlib) != 0) stages++;

            if ((mask & Bzip2) != 0)
            {
                stages--;
                byte[] target = stages == 0 ? dst : new byte[dstLen];
                int to = stages == 0 ? dstOff : 0;
                int n = BZip2Decoder.Decode(curBuf, curOff, curLen, target, to, dstLen);
                curBuf = target; curOff = to; curLen = n;
            }
            if ((mask & Pkware) != 0)
            {
                stages--;
                byte[] target = stages == 0 ? dst : new byte[dstLen];
                int to = stages == 0 ? dstOff : 0;
                int n = PkwareExplode.Explode(curBuf, curOff, curLen, target, to, dstLen);
                curBuf = target; curOff = to; curLen = n;
            }
            if ((mask & Zlib) != 0)
            {
                stages--;
                int n = Inflate(curBuf, curOff, curLen, dst, dstOff, dstLen, what);
                curLen = n;
            }
            return curLen;
        }

        private static Exception Unsupported(ref int warned, string codec, string what)
        {
            if (Interlocked.Exchange(ref warned, 1) == 0) WowLog.Warn("MPQ: unsupported compression " + codec + " (first: " + what + ")");
            return new NotSupportedException("MPQ compression " + codec + " not supported: " + what);
        }

        /// <summary>zlib stream (2-byte header, deflate body, adler32 ignored) into dst; returns bytes written.</summary>
        public static int Inflate(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstLen, string what)
        {
            if (srcLen < 2) throw new InvalidDataException(what + ": zlib unit too short");
            byte cmf = src[srcOff], flg = src[srcOff + 1];
            if ((cmf & 0x0F) != 8 || ((cmf << 8) | flg) % 31 != 0) throw new InvalidDataException(what + ": bad zlib header " + cmf.ToString("x2") + flg.ToString("x2"));
            if ((flg & 0x20) != 0) throw new InvalidDataException(what + ": zlib preset dictionary not supported");
            using (var ms = new MemoryStream(src, srcOff + 2, srcLen - 2, false))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int total = 0;
                while (total < dstLen)
                {
                    int n = ds.Read(dst, dstOff + total, dstLen - total);
                    if (n <= 0) break;
                    total += n;
                }
                return total;
            }
        }
    }
}
