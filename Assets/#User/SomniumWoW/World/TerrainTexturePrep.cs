using System;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Turns a tileset BLP into one slice of the map-wide terrain texture array: 256x256 DXT5 (BC3) with the full 9-level
    /// mip chain (87,408 bytes), RGB = the texture, A = its SPECULAR MASK, so every slice has the same size and format.
    ///
    /// Specular (client CVar "specular"; the user's WTF\Config.wtf has SET specular "1"): wowdev.wiki ADT/v18 (MTEX) - the
    /// client "will always try to load _s.blp texture, unless do_not_load_specular_or_height_texture_but_use_cubemap is
    /// present in MTXF"; the client's terrain ARB programs (patch.MPQ shaders\pixel\arbfp1\Terrain*.bls) lerp all four
    /// channels of the layers and add texture.a x specular colour x (1 - MCSH shadow). MEASURED (probes TerrainSpec,
    /// TerrainSpecRgb): 1067 of 1285 tileset textures have an _s twin, 256x256 DXT3 (536) / DXT5 (516) with 9 mips (plus a
    /// few palette / odd sizes); its RGB is the base texture's (mean abs diff 0.02..0.08 of 255 on the Northshire set) and
    /// its alpha is the mask (ElwynnCobbleStoneBase_s: mean 48, max 187; grass and flowers 16..32). So the _s texture is used
    /// on its own when present (Build(.., specular: true)); a texture without one gets mask 0 (no shine).
    ///
    /// Paths: a 256x256 DXT level is copied block for block (DXT5 as is; DXT3's explicit 4-bit alpha re-encoded as a DXT5
    /// alpha block; colour blocks copied - BC2/BC3 colour blocks are always 4-colour, a DXT1 block in 3-colour mode that
    /// uses index 2/3 is decoded and re-encoded); a chain that stops early is box-filtered on; anything else (MEASURED
    /// census TerrainTexCensus of the base textures: 597 of 608 are 256x256 DXT1 x 9 mips; 512x512, 128x128, 16x16, 8x8,
    /// palette, 768x128 cube strips) is decoded, resampled to 256x256 and re-encoded. Pure C# (worker thread).
    /// </summary>
    public static class TerrainTexturePrep
    {
        public const int Size = 256;
        public const int MipCount = 9;
        const int BlockBytes = 16; // BC3: 8 bytes alpha + 8 bytes colour

        /// <summary>Bytes of one slice (all mips).</summary>
        public static int SliceBytes
        {
            get { int n = 0; for (int m = 0; m < MipCount; m++) n += MipBytes(m); return n; }
        }

        public static int MipBytes(int mip)
        {
            int s = Math.Max(1, Size >> mip);
            int blocks = Math.Max(1, (s + 3) / 4);
            return blocks * blocks * BlockBytes;
        }

        /// <summary>"name_s.blp": the specular twin the client loads for a tileset texture.</summary>
        public static string SpecularPath(string name)
        {
            if (name.EndsWith(".blp", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - 4) + "_s.blp";
            return name + "_s";
        }

        /// <summary>A plain texture (specular mask 0).</summary>
        public static byte[] Build(byte[] blp, out string how) { return Build(blp, false, out how); }

        /// <summary>The slice bytes, or null if the BLP cannot be read. specular = the BLP is an _s texture (its alpha is the
        /// mask); false = a plain texture (mask 0). how = the reason / the path taken.</summary>
        public static byte[] Build(byte[] blp, bool specular, out string how)
        {
            how = null;
            BlpImage img;
            try { img = BlpImage.Decode(blp); }
            catch (Exception e) { how = "undecodable: " + e.Message; return null; }
            var dst = new byte[SliceBytes];
            // Find the stored level that is exactly Size x Size.
            int start = -1;
            for (int m = 0; m < img.MipCount; m++) if (img.MipWidth(m) == Size && img.MipHeight(m) == Size) { start = m; break; }
            bool dxt = img.Format == BlpFormat.Dxt1 || img.Format == BlpFormat.Dxt3 || img.Format == BlpFormat.Dxt5;
            // (an _s texture in DXT1 would give the client alpha 1 / 1-bit alpha: the decode path reproduces that)
            int o = 0;
            if (start >= 0 && dxt && !(specular && img.Format == BlpFormat.Dxt1))
            {
                int have = img.MipCount - start;
                int reencoded = 0;
                for (int m = 0; m < MipCount; m++)
                {
                    int bytes = MipBytes(m);
                    if (m < have)
                    {
                        var raw = img.GetRawMip(start + m);
                        reencoded += CopyBlocks(raw, img.Format, specular, dst, o, bytes / BlockBytes);
                    }
                    else
                    {
                        // chain stops early: box-filter the previous level
                        int ps = Math.Max(1, Size >> (m - 1));
                        var prev = DecodeLevel(dst, o - MipBytes(m - 1), ps);
                        var half = Downsample(prev, ps);
                        EncodeLevel(half, Math.Max(1, ps / 2), dst, o);
                    }
                    o += bytes;
                }
                how = (start == 0 ? "copied" : "copied from mip " + start) + (specular ? " (_s " + img.Format + ")" : "") +
                      (reencoded > 0 ? ", " + reencoded + " 3-colour blocks re-encoded" : "");
                return dst;
            }
            // Decode the best level and resample to Size x Size, then build the chain.
            int src = 0;
            var rgba = img.GetRgba32(src);
            int sw = img.MipWidth(src), sh = img.MipHeight(src);
            if (sw == 6 * sh)
            {
                // "Terrain Cube Maps" (e.g. TCB_CrystalSong_B.blp, 768x128 = 6 faces; MCLY flag 0x400, the client samples it as
                // a CUBE reflection). Not reproduced: the first face is used as a flat texture.
                var face = new byte[sh * sh * 4];
                for (int y = 0; y < sh; y++) Buffer.BlockCopy(rgba, y * sw * 4, face, y * sh * 4, sh * 4);
                rgba = face; sw = sh;
                how = "cube map strip, first face ";
            }
            if (!specular) for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 0;
            var level = Resample(rgba, sw, sh, Size, Size);
            int s = Size;
            for (int m = 0; m < MipCount; m++)
            {
                EncodeLevel(level, s, dst, o);
                o += MipBytes(m);
                if (s > 1) { level = Downsample(level, s); s = Math.Max(1, s / 2); }
            }
            how = (how ?? "") + "re-encoded from " + img.Width + "x" + img.Height + " " + img.Format + (specular ? " (_s)" : "");
            return dst;
        }

        /// <summary>Copies n blocks (DXT1 8-byte, DXT3/5 16-byte) as BC3. Alpha: DXT5 copied, DXT3 re-encoded, 0 for DXT1 or
        /// when keepAlpha is false. Returns the number of DXT1 3-colour blocks that had to be re-encoded.</summary>
        static int CopyBlocks(byte[] raw, BlpFormat fmt, bool keepAlpha, byte[] dst, int o, int n)
        {
            int stride = fmt == BlpFormat.Dxt1 ? 8 : 16;
            int colourOfs = fmt == BlpFormat.Dxt1 ? 0 : 8;
            int reencoded = 0;
            int[] a16 = null, px = null;
            byte[] one = null;
            for (int i = 0; i < n; i++)
            {
                int s = i * stride;
                if (s + stride > raw.Length) break;
                int d = o + i * BlockBytes;
                // alpha block (the array is zeroed: 8 zero bytes = a0 = a1 = 0 -> alpha 0)
                if (keepAlpha && fmt == BlpFormat.Dxt5) Buffer.BlockCopy(raw, s, dst, d, 8);
                else if (keepAlpha && fmt == BlpFormat.Dxt3)
                {
                    if (a16 == null) a16 = new int[16];
                    for (int k = 0; k < 16; k++) a16[k] = ((raw[s + (k >> 1)] >> ((k & 1) * 4)) & 0xF) * 17;
                    EncodeAlphaBlock(a16, dst, d);
                }
                // colour block
                int c0 = raw[s + colourOfs] | (raw[s + colourOfs + 1] << 8), c1 = raw[s + colourOfs + 2] | (raw[s + colourOfs + 3] << 8);
                bool usesHigh = ((raw[s + colourOfs + 4] | raw[s + colourOfs + 5] | raw[s + colourOfs + 6] | raw[s + colourOfs + 7]) & 0xAA) != 0;
                if (fmt == BlpFormat.Dxt1 && c0 <= c1 && usesHigh)
                {
                    // 3-colour mode (index 2 = midpoint, 3 = black): BC3 decodes 4-colour only -> decode and re-encode
                    if (one == null) { one = new byte[64]; px = new int[48]; }
                    BlpImage.DecodeDxt(raw, s, 4, 4, one, 1, false);
                    for (int k = 0; k < 16; k++) { px[k * 3] = one[k * 4]; px[k * 3 + 1] = one[k * 4 + 1]; px[k * 3 + 2] = one[k * 4 + 2]; }
                    EncodeColourBlock(px, dst, d + 8);
                    reencoded++;
                }
                else Buffer.BlockCopy(raw, s + colourOfs, dst, d + 8, 8);
            }
            return reencoded;
        }

        static byte[] DecodeLevel(byte[] src, int o, int s)
        {
            var r = new byte[s * s * 4];
            BlpImage.DecodeDxt(src, o, s, s, r, 5, false);
            return r;
        }

        static byte[] Downsample(byte[] src, int s)
        {
            int h = Math.Max(1, s / 2);
            var r = new byte[h * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int x0 = Math.Min(s - 1, x * 2), x1 = Math.Min(s - 1, x * 2 + 1), y0 = Math.Min(s - 1, y * 2), y1 = Math.Min(s - 1, y * 2 + 1);
                        int v = src[(y0 * s + x0) * 4 + c] + src[(y0 * s + x1) * 4 + c] + src[(y1 * s + x0) * 4 + c] + src[(y1 * s + x1) * 4 + c];
                        r[(y * h + x) * 4 + c] = (byte)((v + 2) >> 2);
                    }
            return r;
        }

        /// <summary>Bilinear resample (wrapping, as a tiling texture should).</summary>
        static byte[] Resample(byte[] src, int w, int h, int dw, int dh)
        {
            var r = new byte[dw * dh * 4];
            for (int y = 0; y < dh; y++)
                for (int x = 0; x < dw; x++)
                {
                    float fx = (x + 0.5f) * w / dw - 0.5f, fy = (y + 0.5f) * h / dh - 0.5f;
                    int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                    float tx = fx - x0, ty = fy - y0;
                    int xa = ((x0 % w) + w) % w, xb = (((x0 + 1) % w) + w) % w, ya = ((y0 % h) + h) % h, yb = (((y0 + 1) % h) + h) % h;
                    for (int c = 0; c < 4; c++)
                    {
                        float a = src[(ya * w + xa) * 4 + c] * (1 - tx) + src[(ya * w + xb) * 4 + c] * tx;
                        float b = src[(yb * w + xa) * 4 + c] * (1 - tx) + src[(yb * w + xb) * 4 + c] * tx;
                        r[(y * dw + x) * 4 + c] = (byte)Math.Min(255f, a * (1 - ty) + b * ty + 0.5f);
                    }
                }
            return r;
        }

        /// <summary>Simple BC3 encoder: colour = 4-colour block, endpoints = min/max along the block's luminance axis;
        /// alpha = 8-value block between the block's min and max.</summary>
        static void EncodeLevel(byte[] rgba, int s, byte[] dst, int o)
        {
            int nb = Math.Max(1, (s + 3) / 4);
            var px = new int[16 * 3];
            var a16 = new int[16];
            for (int by = 0; by < nb; by++)
                for (int bx = 0; bx < nb; bx++)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int x = Math.Min(s - 1, bx * 4 + (i & 3)), y = Math.Min(s - 1, by * 4 + (i >> 2));
                        int p = (y * s + x) * 4;
                        px[i * 3] = rgba[p]; px[i * 3 + 1] = rgba[p + 1]; px[i * 3 + 2] = rgba[p + 2];
                        a16[i] = rgba[p + 3];
                    }
                    int d = o + (by * nb + bx) * BlockBytes;
                    EncodeAlphaBlock(a16, dst, d);
                    EncodeColourBlock(px, dst, d + 8);
                }
        }

        /// <summary>16 alphas -> a DXT5 alpha block (a0 = max > a1 = min: 8-value mode; flat block: a0 = a1, indices 0).</summary>
        static void EncodeAlphaBlock(int[] a, byte[] dst, int d)
        {
            int mn = 255, mx = 0;
            for (int i = 0; i < 16; i++) { if (a[i] < mn) mn = a[i]; if (a[i] > mx) mx = a[i]; }
            dst[d] = (byte)mx; dst[d + 1] = (byte)mn;
            ulong bits = 0;
            if (mx > mn)
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bestD = int.MaxValue;
                    for (int k = 0; k < 8; k++)
                    {
                        int v = k == 0 ? mx : k == 1 ? mn : ((8 - k) * mx + (k - 1) * mn) / 7;
                        int dd = Math.Abs(a[i] - v);
                        if (dd < bestD) { bestD = dd; best = k; }
                    }
                    bits |= (ulong)best << (3 * i);
                }
            for (int k = 0; k < 6; k++) dst[d + 2 + k] = (byte)(bits >> (8 * k));
        }

        /// <summary>16 RGB pixels (px[i * 3 ..]) -> a 4-colour block.</summary>
        static void EncodeColourBlock(int[] px, byte[] dst, int d)
        {
            int lo = 0, hi = 0, lmin = int.MaxValue, lmax = int.MinValue;
            for (int i = 0; i < 16; i++)
            {
                int l = px[i * 3] * 2 + px[i * 3 + 1] * 4 + px[i * 3 + 2];
                if (l < lmin) { lmin = l; lo = i; }
                if (l > lmax) { lmax = l; hi = i; }
            }
            int c0 = To565(px[hi * 3], px[hi * 3 + 1], px[hi * 3 + 2]);
            int c1 = To565(px[lo * 3], px[lo * 3 + 1], px[lo * 3 + 2]);
            uint idx = 0;
            if (c0 != c1)
            {
                if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
                var pal = new int[12];
                From565(c0, pal, 0); From565(c1, pal, 3);
                for (int c = 0; c < 3; c++) { pal[6 + c] = (2 * pal[c] + pal[3 + c]) / 3; pal[9 + c] = (pal[c] + 2 * pal[3 + c]) / 3; }
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bestD = int.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        int dr = px[i * 3] - pal[k * 3], dg = px[i * 3 + 1] - pal[k * 3 + 1], db = px[i * 3 + 2] - pal[k * 3 + 2];
                        int dd = dr * dr + dg * dg + db * db;
                        if (dd < bestD) { bestD = dd; best = k; }
                    }
                    idx |= (uint)best << (i * 2);
                }
            }
            dst[d] = (byte)c0; dst[d + 1] = (byte)(c0 >> 8); dst[d + 2] = (byte)c1; dst[d + 3] = (byte)(c1 >> 8);
            dst[d + 4] = (byte)idx; dst[d + 5] = (byte)(idx >> 8); dst[d + 6] = (byte)(idx >> 16); dst[d + 7] = (byte)(idx >> 24);
        }

        static int To565(int r, int g, int b) { return ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255); }

        static void From565(int c, int[] p, int o)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            p[o] = (r << 3) | (r >> 2); p[o + 1] = (g << 2) | (g >> 4); p[o + 2] = (b << 3) | (b >> 2);
        }
    }
}
