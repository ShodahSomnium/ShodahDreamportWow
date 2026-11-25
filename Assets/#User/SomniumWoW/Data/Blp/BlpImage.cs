using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>Pixel encoding of a BLP (after resolving compression + alpha depth + alpha type).</summary>
    public enum BlpFormat
    {
        Unknown = 0,
        /// <summary>BC1. AlphaDepth 0 (opaque) or 1 (1-bit punch-through alpha: index 3 in 3-colour blocks = transparent).</summary>
        Dxt1,
        /// <summary>BC2 (explicit 4-bit alpha). BLP alphaType 1.</summary>
        Dxt3,
        /// <summary>BC3 (interpolated alpha). BLP alphaType 7.</summary>
        Dxt5,
        /// <summary>256-colour BGRA palette + separate 0/1/4/8-bit alpha plane (BLP compression 1).</summary>
        Palette,
        /// <summary>Uncompressed 32-bit BGRA (BLP compression 3).</summary>
        Bgra,
        /// <summary>JPEG-compressed BLP (type 0). Not decoded.</summary>
        Jpeg,
    }

    /// <summary>
    /// BLP2 texture (WoW 3.3.5a). Pure C#; decoding is thread-safe (the object is immutable after Decode).
    /// Header (148 bytes): 'BLP2', u32 type (0 JPEG, 1 direct), u8 compression (1 palette, 2 DXT, 3 BGRA), u8 alphaDepth
    /// (0/1/4/8), u8 alphaType (DXT: 0 DXT1, 1 DXT3, 7 DXT5), u8 hasMips, u32 width, u32 height, u32 mipOffsets[16],
    /// u32 mipSizes[16]; then the 256-entry BGRA palette (compression 1) or JPEG header (type 0).
    ///
    /// ORIENTATION: pixel rows are returned exactly as stored - row 0 is the first row in the file, which is the TOP of
    /// the picture as WoW authors see it. WowTextures uploads that row as Texture2D row 0 (v = 0) and WoW's mesh UVs are
    /// used unchanged. See WowTextures for the verification.
    /// </summary>
    public sealed class BlpImage
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int MipCount { get; private set; }
        public BlpFormat Format { get; private set; }
        public int Compression { get; private set; }
        public int AlphaDepth { get; private set; }
        public int AlphaType { get; private set; }
        public bool HasMipsFlag { get; private set; }

        /// <summary>True when the texture carries alpha (alphaDepth &gt; 0).</summary>
        public bool HasAlpha { get { return AlphaDepth > 0; } }

        private byte[] _data;
        private readonly int[] _mipOffset = new int[16];
        private readonly int[] _mipSize = new int[16];
        private uint[] _palette;   // BGRA as stored, packed little-endian (B | G<<8 | R<<16 | A<<24)

        private BlpImage() { }

        public int MipWidth(int mip) { return Math.Max(1, Width >> mip); }
        public int MipHeight(int mip) { return Math.Max(1, Height >> mip); }

        /// <summary>Parses the header. Throws InvalidDataException for non-BLP2 data or bad sizes.</summary>
        public static BlpImage Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 148) throw new InvalidDataException("BLP: too short");
            if (bytes[0] != 'B' || bytes[1] != 'L' || bytes[2] != 'P' || bytes[3] != '2')
                throw new InvalidDataException("BLP: not BLP2 (magic " + (char)bytes[0] + (char)bytes[1] + (char)bytes[2] + (char)bytes[3] + ")");
            var b = new BlpImage();
            b._data = bytes;
            int type = BitConverter.ToInt32(bytes, 4);
            b.Compression = bytes[8];
            b.AlphaDepth = bytes[9];
            b.AlphaType = bytes[10];
            b.HasMipsFlag = bytes[11] != 0;
            b.Width = BitConverter.ToInt32(bytes, 12);
            b.Height = BitConverter.ToInt32(bytes, 16);
            if (b.Width <= 0 || b.Height <= 0 || b.Width > 8192 || b.Height > 8192) throw new InvalidDataException("BLP: bad size " + b.Width + "x" + b.Height);
            for (int i = 0; i < 16; i++)
            {
                b._mipOffset[i] = BitConverter.ToInt32(bytes, 20 + i * 4);
                b._mipSize[i] = BitConverter.ToInt32(bytes, 84 + i * 4);
            }

            if (type == 0) b.Format = BlpFormat.Jpeg;
            else if (b.Compression == 1) b.Format = BlpFormat.Palette;
            else if (b.Compression == 3) b.Format = BlpFormat.Bgra;
            else if (b.Compression == 2)
            {
                if (b.AlphaType == 7) b.Format = BlpFormat.Dxt5;
                else if (b.AlphaType == 1) b.Format = BlpFormat.Dxt3;
                else b.Format = BlpFormat.Dxt1;
            }
            else throw new InvalidDataException("BLP: unknown compression " + b.Compression);

            if (b.Format == BlpFormat.Palette)
            {
                b._palette = new uint[256];
                for (int i = 0; i < 256; i++) b._palette[i] = BitConverter.ToUInt32(bytes, 148 + i * 4);
            }

            // Mip count: consecutive levels whose offset/size are set, fit in the file and hold enough bytes.
            int maxLevels = 1;
            if (b.HasMipsFlag)
            {
                int w = b.Width, h = b.Height;
                maxLevels = 1;
                while ((w > 1 || h > 1) && maxLevels < 16) { w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1); maxLevels++; }
            }
            int count = 0;
            for (int m = 0; m < maxLevels; m++)
            {
                int off = b._mipOffset[m], size = b._mipSize[m];
                if (off <= 0 || size <= 0 || (long)off + size > bytes.Length) break;
                if (b.Format != BlpFormat.Jpeg && size < b.ExpectedMipBytes(m)) break;
                count++;
            }
            if (count == 0) throw new InvalidDataException("BLP: no readable mip level (" + b.Width + "x" + b.Height + " " + b.Format + ")");
            b.MipCount = count;
            return b;
        }

        /// <summary>Bytes a mip level needs in this format (DXT: whole 4x4 blocks, at least one block).</summary>
        public int ExpectedMipBytes(int mip)
        {
            int w = MipWidth(mip), h = MipHeight(mip);
            switch (Format)
            {
                case BlpFormat.Dxt1: return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8;
                case BlpFormat.Dxt3:
                case BlpFormat.Dxt5: return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16;
                case BlpFormat.Bgra: return w * h * 4;
                case BlpFormat.Palette:
                    int alphaBytes = AlphaDepth == 0 ? 0 : (w * h * AlphaDepth + 7) / 8;
                    return w * h + alphaBytes;
                default: return 0;
            }
        }

        /// <summary>
        /// The stored bytes of a mip level, trimmed to ExpectedMipBytes (DXT blocks / BGRA pixels / palette indices+alpha)
        /// - for direct GPU upload of DXT1/DXT3/DXT5 (TextureFormat.DXT1 / DXT5; DXT3 has no Unity format - decode it).
        /// </summary>
        public byte[] GetRawMip(int mip)
        {
            CheckMip(mip);
            int n = Format == BlpFormat.Jpeg ? _mipSize[mip] : ExpectedMipBytes(mip);
            var r = new byte[n];
            Buffer.BlockCopy(_data, _mipOffset[mip], r, 0, n);
            return r;
        }

        /// <summary>Copies a mip level's raw bytes into dst at dstOffset (see GetRawMip); returns the count.</summary>
        public int CopyRawMip(int mip, byte[] dst, int dstOffset)
        {
            CheckMip(mip);
            int n = ExpectedMipBytes(mip);
            Buffer.BlockCopy(_data, _mipOffset[mip], dst, dstOffset, n);
            return n;
        }

        private void CheckMip(int mip)
        {
            if (mip < 0 || mip >= MipCount) throw new ArgumentOutOfRangeException("mip " + mip + " of " + MipCount);
        }

        /// <summary>Decodes a mip level to RGBA32 (4 bytes/pixel, row 0 = first stored row). Not for JPEG BLPs.</summary>
        public byte[] GetRgba32(int mip)
        {
            CheckMip(mip);
            int w = MipWidth(mip), h = MipHeight(mip);
            var dst = new byte[w * h * 4];
            int src = _mipOffset[mip];
            switch (Format)
            {
                case BlpFormat.Dxt1: DecodeDxt(_data, src, w, h, dst, 1, AlphaDepth > 0); break;
                case BlpFormat.Dxt3: DecodeDxt(_data, src, w, h, dst, 3, true); break;
                case BlpFormat.Dxt5: DecodeDxt(_data, src, w, h, dst, 5, true); break;
                case BlpFormat.Bgra:
                    for (int i = 0; i < w * h; i++)
                    {
                        int s = src + i * 4, d = i * 4;
                        dst[d] = _data[s + 2]; dst[d + 1] = _data[s + 1]; dst[d + 2] = _data[s]; dst[d + 3] = _data[s + 3];
                    }
                    break;
                case BlpFormat.Palette: DecodePalette(src, w, h, dst); break;
                default: throw new NotSupportedException("BLP: " + Format + " decoding not supported");
            }
            return dst;
        }

        private void DecodePalette(int src, int w, int h, byte[] dst)
        {
            int n = w * h;
            int alpha = src + n;
            for (int i = 0; i < n; i++)
            {
                uint c = _palette[_data[src + i]];
                int d = i * 4;
                dst[d] = (byte)(c >> 16); dst[d + 1] = (byte)(c >> 8); dst[d + 2] = (byte)c;
                byte a;
                switch (AlphaDepth)
                {
                    case 1: a = (byte)(((_data[alpha + (i >> 3)] >> (i & 7)) & 1) != 0 ? 255 : 0); break;
                    case 4: a = (byte)(((_data[alpha + (i >> 1)] >> ((i & 1) * 4)) & 0xF) * 17); break;
                    case 8: a = _data[alpha + i]; break;
                    default: a = 255; break;
                }
                dst[d + 3] = a;
            }
        }

        /// <summary>Software S3TC decode (kind 1 = DXT1, 3 = DXT3, 5 = DXT5) into RGBA32, clipped to w x h.</summary>
        public static void DecodeDxt(byte[] data, int offset, int w, int h, byte[] dst, int kind, bool dxt1Alpha)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            int blockBytes = kind == 1 ? 8 : 16;
            var colors = new byte[16];   // 4 colours x RGBA
            var alphas = new byte[8];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int p = offset + (by * bw + bx) * blockBytes;
                    int cp = kind == 1 ? p : p + 8;
                    int c0 = data[cp] | (data[cp + 1] << 8);
                    int c1 = data[cp + 2] | (data[cp + 3] << 8);
                    uint bits = (uint)(data[cp + 4] | (data[cp + 5] << 8) | (data[cp + 6] << 16) | (data[cp + 7] << 24));
                    Rgb565(c0, colors, 0);
                    Rgb565(c1, colors, 4);
                    bool four = kind != 1 || c0 > c1;
                    for (int k = 0; k < 3; k++)
                    {
                        if (four)
                        {
                            colors[8 + k] = (byte)((2 * colors[k] + colors[4 + k] + 1) / 3);
                            colors[12 + k] = (byte)((colors[k] + 2 * colors[4 + k] + 1) / 3);
                        }
                        else
                        {
                            colors[8 + k] = (byte)((colors[k] + colors[4 + k]) / 2);
                            colors[12 + k] = 0;
                        }
                    }
                    colors[3] = colors[7] = colors[11] = 255;
                    colors[15] = (byte)(!four && dxt1Alpha ? 0 : 255);

                    ulong alphaBits = 0;
                    if (kind == 5)
                    {
                        int a0 = data[p], a1 = data[p + 1];
                        alphas[0] = (byte)a0; alphas[1] = (byte)a1;
                        if (a0 > a1)
                            for (int k = 1; k < 7; k++) alphas[k + 1] = (byte)(((7 - k) * a0 + k * a1 + 3) / 7);
                        else
                        {
                            for (int k = 1; k < 5; k++) alphas[k + 1] = (byte)(((5 - k) * a0 + k * a1 + 2) / 5);
                            alphas[6] = 0; alphas[7] = 255;
                        }
                        for (int k = 0; k < 6; k++) alphaBits |= (ulong)data[p + 2 + k] << (8 * k);
                    }

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= h) break;
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= w) continue;
                            int i = py * 4 + px;
                            int ci = (int)((bits >> (2 * i)) & 3);
                            int d = (y * w + x) * 4;
                            dst[d] = colors[ci * 4];
                            dst[d + 1] = colors[ci * 4 + 1];
                            dst[d + 2] = colors[ci * 4 + 2];
                            if (kind == 1) dst[d + 3] = colors[ci * 4 + 3];
                            else if (kind == 3)
                            {
                                int nib = (data[p + (i >> 1)] >> ((i & 1) * 4)) & 0xF;
                                dst[d + 3] = (byte)(nib * 17);
                            }
                            else dst[d + 3] = alphas[(int)((alphaBits >> (3 * i)) & 7)];
                        }
                    }
                }
            }
        }

        private static void Rgb565(int c, byte[] o, int at)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            o[at] = (byte)((r << 3) | (r >> 2));
            o[at + 1] = (byte)((g << 2) | (g >> 4));
            o[at + 2] = (byte)((b << 3) | (b >> 2));
        }

        public override string ToString()
        {
            return Width + "x" + Height + " " + Format + " (compression " + Compression + ", alphaDepth " + AlphaDepth + ", alphaType " + AlphaType + ", mips " + MipCount + (HasMipsFlag ? "" : ", no-mip flag") + ")";
        }
    }
}
