using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>
    /// How a BLP becomes a Texture2D. default(WowTextureOptions) = sRGB colour, Repeat/Repeat, anisotropy 1, not
    /// readable - use the presets. Linear, wrap and Readable are part of the cache key; Aniso is taken from the first
    /// request of a texture.
    /// </summary>
    public struct WowTextureOptions
    {
        /// <summary>True = linear data (no sRGB decode). WoW colour textures are sRGB: leave false for them.</summary>
        public bool Linear;
        public TextureWrapMode WrapU;
        public TextureWrapMode WrapV;
        /// <summary>Anisotropic level (0 or 1 = off). Only used when the texture has mips.</summary>
        public int Aniso;
        /// <summary>Keep the CPU copy (GetPixels works). Default false: the pixels are freed after the upload.</summary>
        public bool Readable;

        public WowTextureOptions(bool linear, TextureWrapMode wrapU, TextureWrapMode wrapV, int aniso)
        {
            Linear = linear; WrapU = wrapU; WrapV = wrapV; Aniso = aniso; Readable = false;
        }

        /// <summary>World/model colour textures: sRGB, Repeat, aniso 4.</summary>
        public static WowTextureOptions World { get { return new WowTextureOptions(false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 4); } }

        /// <summary>Terrain tilesets (seen at grazing angles): sRGB, Repeat, aniso 8.</summary>
        public static WowTextureOptions Terrain { get { return new WowTextureOptions(false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 8); } }

        /// <summary>UI / loading screens / logos / sky: sRGB, Clamp, no aniso.</summary>
        public static WowTextureOptions Ui { get { return new WowTextureOptions(false, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 1); } }

        /// <summary>M2 texture flags: bit 0 = wrap X (U), bit 1 = wrap Y (V); a clear bit = Clamp.</summary>
        public static WowTextureOptions ForM2Flags(int m2TextureFlags)
        {
            return new WowTextureOptions(false,
                (m2TextureFlags & 1) != 0 ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                (m2TextureFlags & 2) != 0 ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, 4);
        }
    }

    /// <summary>
    /// The result of WowTextures.Prepare: the whole mip chain in the upload format, made on any thread, turned into a
    /// Texture2D by WowTextures.Create on the main thread. Error != null when the file is missing or undecodable.
    /// </summary>
    public sealed class WowPreparedTexture
    {
        public string Path;
        public string Key;
        public WowTextureOptions Options;
        public int Width, Height, MipCount;
        /// <summary>DXT1 or DXT5 (uploaded as stored) or RGBA32 (decoded: DXT3, palette, BGRA).</summary>
        public TextureFormat Format;
        /// <summary>All mip levels, level 0 first, exactly the size LoadRawTextureData expects for MipCount levels.</summary>
        public byte[] Data;
        public BlpFormat SourceFormat;
        /// <summary>BLP alpha depth 0/1/4/8 (0 = opaque texture).</summary>
        public int AlphaDepth;
        public string Error;
        public long PrepareMs;
    }

    /// <summary>
    /// BLP -> Texture2D, cached by (lower-case path, options).
    ///
    /// ORIENTATION CONVENTION (PLAN rule 15; measured, see P/reports/data.md "UV orientation"):
    ///   BLP pixel rows are uploaded AS STORED: the first row in the file = Texture2D row 0 = v 0. Unity puts texture row
    ///   0 at v = 0 on every graphics API, so a WoW UV of v = 0 samples the first stored row. Measured on the real files:
    ///   HumanMale.m2's foot vertices have v 0.91..0.99 and the decoded HumanMaleSkin00_00.blp shows the toes in its
    ///   LAST stored rows -> WoW's v = 0 is the first stored row. So: WoW mesh UVs are used UNCHANGED (no 1 - v), for
    ///   M2, WMO (MOTV) and terrain alike. DXT blocks are uploaded in stored order too (block row 0 = pixel rows 0..3),
    ///   which is the same orientation as the software decode.
    ///   A texture shown with Unity's own UVs (RawImage, a quad) appears upside down: use UiUvRect
    ///   (new Rect(0, 1, 1, -1)) on RawImage.uvRect, or flip v in the UI mesh. Nothing is mirrored horizontally.
    ///
    /// FORMATS: DXT1 (alpha depth 0 or 1) and DXT5 go to the GPU as stored with their mip chain (TextureFormat.DXT1
    /// is BC1 with 1-bit alpha: 3-colour blocks' index 3 decodes to transparent black on the GPU, the same as WoW's D3D
    /// path). DXT3 (Unity has no BC2), palettised and BGRA BLPs are decoded to RGBA32 (with every stored mip). Chains
    /// that stop early (868 BLPs: Blizzard stores too few bytes for tiny non-square levels) are uploaded with the levels
    /// that exist (Texture2D mipCount = BlpImage.MipCount). A DXT BLP whose size is not a multiple of 4 (none exist in
    /// 3.3.5a, census of 106,831 BLPs) is decoded to RGBA32 instead. JPEG BLPs (none exist) give the Missing texture.
    ///
    /// THREADING: Prepare (reads the MPQ, decodes) is thread-safe and meant for worker threads. Create, Get, Missing,
    /// FromRgba32 and Clear touch UnityEngine and must run on the main thread. TryGetCached/IsCached can be called
    /// from any thread.
    ///
    /// LOG: one warning per missing/undecodable path (first 40), and StatsLine() for a summary.
    /// </summary>
    public static class WowTextures
    {
        /// <summary>RawImage.uvRect that shows a WowTextures texture upright (see the orientation convention).</summary>
        public static readonly Rect UiUvRect = new Rect(0f, 1f, 1f, -1f);

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static Texture2D _missing;
        private static long _gpuBytes, _prepareMs;
        private static int _created, _missingCount, _decodedRgba;

        /// <summary>Canonical path: trimmed, '/' -> '\', lower case (MPQ names are case-insensitive).</summary>
        public static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? "" : path.Trim().Replace('/', '\\').ToLowerInvariant();
        }

        /// <summary>The cache key of a path + options.</summary>
        public static string KeyOf(string path, WowTextureOptions o)
        {
            return NormalizePath(path) + "|" + (o.Linear ? "L" : "S") + (int)o.WrapU + (int)o.WrapV + (o.Readable ? "R" : "");
        }

        // ------------------------------------------------------------------------------------------ any thread

        /// <summary>Reads + decodes a BLP into its upload form. Any thread. Never throws (Error is set instead).</summary>
        public static WowPreparedTexture Prepare(WowFiles files, string path, WowTextureOptions options)
        {
            var sw = Stopwatch.StartNew();
            byte[] bytes = null;
            string err = null;
            try
            {
                if (files == null) err = "no WowFiles";
                else if (string.IsNullOrEmpty(path)) err = "empty path";
                else
                {
                    bytes = files.Read(path);
                    if (bytes == null) err = "not in the game files";
                }
            }
            catch (Exception e) { err = "read failed: " + e.Message; }
            WowPreparedTexture p;
            if (err != null) p = new WowPreparedTexture { Path = path, Key = KeyOf(path, options), Options = options, Error = err };
            else p = Prepare(bytes, path, options);
            p.PrepareMs = sw.ElapsedMilliseconds;
            Interlocked.Add(ref _prepareMs, p.PrepareMs);
            return p;
        }

        /// <summary>Decodes BLP bytes (e.g. read by the caller) into their upload form. Any thread. Never throws.</summary>
        public static WowPreparedTexture Prepare(byte[] blpBytes, string name, WowTextureOptions options)
        {
            var p = new WowPreparedTexture { Path = name, Key = KeyOf(name, options), Options = options };
            try
            {
                var img = BlpImage.Decode(blpBytes);
                p.Width = img.Width;
                p.Height = img.Height;
                p.MipCount = img.MipCount;
                p.SourceFormat = img.Format;
                p.AlphaDepth = img.AlphaDepth;
                if (img.Format == BlpFormat.Jpeg) { p.Error = "JPEG BLP not supported"; return p; }

                bool blockAligned = img.Width % 4 == 0 && img.Height % 4 == 0;
                bool direct = blockAligned && (img.Format == BlpFormat.Dxt1 || img.Format == BlpFormat.Dxt5);
                int total = 0;
                for (int m = 0; m < img.MipCount; m++)
                    total += direct ? img.ExpectedMipBytes(m) : img.MipWidth(m) * img.MipHeight(m) * 4;
                var data = new byte[total];
                int off = 0;
                for (int m = 0; m < img.MipCount; m++)
                {
                    if (direct) off += img.CopyRawMip(m, data, off);
                    else
                    {
                        var rgba = img.GetRgba32(m);
                        Buffer.BlockCopy(rgba, 0, data, off, rgba.Length);
                        off += rgba.Length;
                    }
                }
                p.Format = direct ? (img.Format == BlpFormat.Dxt1 ? TextureFormat.DXT1 : TextureFormat.DXT5) : TextureFormat.RGBA32;
                p.Data = data;
            }
            catch (Exception e)
            {
                p.Error = "undecodable: " + e.Message;
                p.Data = null;
            }
            return p;
        }

        /// <summary>The cached texture for path + options, if Created already. Any thread.</summary>
        public static bool TryGetCached(string path, WowTextureOptions options, out Texture2D texture)
        {
            lock (_lock) return _cache.TryGetValue(KeyOf(path, options), out texture) && (object)texture != null;
        }

        /// <summary>True when path + options was Created already (lets a worker skip Prepare). Any thread.</summary>
        public static bool IsCached(string path, WowTextureOptions options)
        {
            Texture2D t;
            return TryGetCached(path, options, out t);
        }

        // ------------------------------------------------------------------------------------------ main thread

        /// <summary>
        /// Main thread: the Texture2D for a prepared texture (the cached one when that key exists already; the shared
        /// Missing texture when p.Error is set). The texture is cached and owned by WowTextures (Clear destroys it).
        /// </summary>
        public static Texture2D Create(WowPreparedTexture p)
        {
            if (p == null) return Missing;
            Texture2D cached;
            lock (_lock) _cache.TryGetValue(p.Key, out cached);
            if (cached != null) return cached;   // Unity null check: a texture destroyed elsewhere is rebuilt

            if (p.Error != null || p.Data == null)
            {
                Warn(p.Path, p.Error ?? "no data");
                return Missing;
            }

            Texture2D t;
            try
            {
                t = new Texture2D(p.Width, p.Height, p.Format, p.MipCount, p.Options.Linear);
                t.name = FileName(p.Path);
                t.wrapModeU = p.Options.WrapU;
                t.wrapModeV = p.Options.WrapV;
                t.filterMode = p.MipCount > 1 ? FilterMode.Trilinear : FilterMode.Bilinear;
                t.anisoLevel = p.MipCount > 1 ? Math.Max(1, p.Options.Aniso) : 1;
                t.LoadRawTextureData(p.Data);
                t.Apply(false, !p.Options.Readable);
            }
            catch (Exception e)
            {
                Warn(p.Path, "upload failed (" + p.Width + "x" + p.Height + " " + p.Format + " mips " + p.MipCount + " bytes " + p.Data.Length + "): " + e.Message);
                return Missing;
            }

            lock (_lock) _cache[p.Key] = t;
            _created++;
            _gpuBytes += p.Data.Length;
            if (p.Format == TextureFormat.RGBA32) _decodedRgba++;
            return t;
        }

        /// <summary>Main thread: cached texture, else Prepare + Create now (blocking). Missing texture when absent.</summary>
        public static Texture2D Get(WowFiles files, string path, WowTextureOptions options)
        {
            Texture2D t;
            lock (_lock) _cache.TryGetValue(KeyOf(path, options), out t);
            if (t != null) return t;
            return Create(Prepare(files, path, options));
        }

        /// <summary>Main thread: Get with WowTextureOptions.World.</summary>
        public static Texture2D Get(WowFiles files, string path)
        {
            return Get(files, path, WowTextureOptions.World);
        }

        /// <summary>
        /// Main thread: an uncached texture from RGBA32 pixels (e.g. a composed character skin), row 0 = v 0 like every
        /// WowTextures texture. mips = generate a full mip chain on the GPU. The caller owns (and destroys) it.
        /// </summary>
        public static Texture2D FromRgba32(int width, int height, byte[] rgba, WowTextureOptions options, string name, bool mips)
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, mips, options.Linear);
            t.name = name ?? "WowRgba32";
            t.wrapModeU = options.WrapU;
            t.wrapModeV = options.WrapV;
            t.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            t.anisoLevel = mips ? Math.Max(1, options.Aniso) : 1;
            if (mips)
            {
                t.SetPixelData(rgba, 0);
                t.Apply(true, !options.Readable);
            }
            else
            {
                t.LoadRawTextureData(rgba);
                t.Apply(false, !options.Readable);
            }
            return t;
        }

        /// <summary>Main thread: a shared 8x8 magenta/black checker used for missing or broken textures (never cached).</summary>
        public static Texture2D Missing
        {
            get
            {
                if (_missing != null) return _missing;
                var px = new byte[8 * 8 * 4];
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int i = (y * 8 + x) * 4;
                        bool on = ((x >> 1) + (y >> 1)) % 2 == 0;
                        px[i] = on ? (byte)255 : (byte)0; px[i + 1] = 0; px[i + 2] = on ? (byte)255 : (byte)0; px[i + 3] = 255;
                    }
                _missing = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
                _missing.name = "WowMissingTexture";
                _missing.filterMode = FilterMode.Point;
                _missing.LoadRawTextureData(px);
                _missing.Apply(false, true);
                return _missing;
            }
        }

        /// <summary>Main thread: destroys every cached texture (e.g. after leaving a map). Missing stays.</summary>
        public static void Clear()
        {
            List<Texture2D> all;
            lock (_lock)
            {
                all = new List<Texture2D>(_cache.Values);
                _cache.Clear();
            }
            foreach (var t in all) if (t != null) UnityEngine.Object.Destroy(t);
            WowLog.Info("WowTextures: cleared " + all.Count + " textures");
            _gpuBytes = 0;
            _created = 0;
            _decodedRgba = 0;
        }

        /// <summary>Number of cached textures.</summary>
        public static int Count { get { lock (_lock) return _cache.Count; } }

        /// <summary>One summary line for the log: counts, GPU memory, decode time.</summary>
        public static string StatsLine()
        {
            return "WowTextures: " + Count + " cached (" + _created + " created since Clear, " + _decodedRgba + " decoded to RGBA32), "
                + (_gpuBytes / (1024.0 * 1024.0)).ToString("0.0") + " MB texture data, " + _missingCount + " missing, prepare total "
                + Interlocked.Read(ref _prepareMs) + " ms";
        }

        private static void Warn(string path, string why)
        {
            bool first;
            lock (_lock)
            {
                first = _warned.Add(NormalizePath(path));
                if (first) _missingCount++;
            }
            if (!first) return;
            if (_missingCount <= 40) WowLog.Warn("WowTextures: " + (string.IsNullOrEmpty(path) ? "(no path)" : path) + " -> Missing texture: " + why);
            else if (_missingCount == 41) WowLog.Warn("WowTextures: more missing textures, not logged individually (see StatsLine)");
        }

        private static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "WowTexture";
            int i = path.LastIndexOfAny(new[] { '\\', '/' });
            return i >= 0 ? path.Substring(i + 1) : path;
        }
    }
}
