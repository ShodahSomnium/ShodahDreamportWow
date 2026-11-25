using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>MDDF entry (36 bytes): an M2 doodad placed by a tile.</summary>
    public struct AdtDoodadDef
    {
        public int NameIndex;      // into AdtFile.M2Names (MMID index)
        public uint UniqueId;
        public float PX, PY, PZ;   // placement space (MapConst.PlacementToWorld)
        public float RX, RY, RZ;   // degrees as stored
        public ushort Scale;       // 1024 = 1.0
        public ushort Flags;

        public static AdtDoodadDef Read(byte[] b, int o)
        {
            var d = new AdtDoodadDef();
            d.NameIndex = Bin.I32(b, o); d.UniqueId = Bin.U32(b, o + 4);
            d.PX = Bin.F32(b, o + 8); d.PY = Bin.F32(b, o + 12); d.PZ = Bin.F32(b, o + 16);
            d.RX = Bin.F32(b, o + 20); d.RY = Bin.F32(b, o + 24); d.RZ = Bin.F32(b, o + 28);
            d.Scale = Bin.U16(b, o + 32); d.Flags = Bin.U16(b, o + 34);
            return d;
        }
    }

    /// <summary>MODF entry (64 bytes): a WMO placed by a tile (or the global WMO of a WDT).</summary>
    public struct AdtWmoDef
    {
        public int NameIndex;      // into AdtFile.WmoNames (MWID index)
        public uint UniqueId;
        public float PX, PY, PZ;   // placement space
        public float RX, RY, RZ;   // degrees
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ; // world bounds in placement space
        public ushort Flags, DoodadSet, NameSet, Scale;

        public static AdtWmoDef Read(byte[] b, int o)
        {
            var d = new AdtWmoDef();
            d.NameIndex = Bin.I32(b, o); d.UniqueId = Bin.U32(b, o + 4);
            d.PX = Bin.F32(b, o + 8); d.PY = Bin.F32(b, o + 12); d.PZ = Bin.F32(b, o + 16);
            d.RX = Bin.F32(b, o + 20); d.RY = Bin.F32(b, o + 24); d.RZ = Bin.F32(b, o + 28);
            d.MinX = Bin.F32(b, o + 32); d.MinY = Bin.F32(b, o + 36); d.MinZ = Bin.F32(b, o + 40);
            d.MaxX = Bin.F32(b, o + 44); d.MaxY = Bin.F32(b, o + 48); d.MaxZ = Bin.F32(b, o + 52);
            d.Flags = Bin.U16(b, o + 56); d.DoodadSet = Bin.U16(b, o + 58); d.NameSet = Bin.U16(b, o + 60); d.Scale = Bin.U16(b, o + 62);
            return d;
        }
    }

    /// <summary>MCLY entry: one texture layer of a chunk.</summary>
    public struct AdtLayer
    {
        public int TextureId;      // into AdtFile.Textures
        public uint Flags;         // 0x7 anim rotation, 0x38 anim speed, 0x40 animate, 0x80 overbright, 0x100 use alpha, 0x200 compressed alpha, 0x400 cubemap
        public int AlphaOffset;    // into MCAL data
        public int EffectId;
    }

    /// <summary>How a layer's alpha map was stored (for statistics / the report).</summary>
    public enum AlphaEncoding : byte { None = 0, Bits4 = 1, Bits8 = 2, Compressed = 3 }

    /// <summary>One MCNK: 9x9+8x8 heights, normals, up to 4 layers with decoded 64x64 alpha maps, shadow, MCCV, holes.</summary>
    public sealed class AdtChunk
    {
        public int IndexX, IndexY;
        public uint Flags;          // 0x1 MCSH, 0x2 impass, 0x4 river, 0x8 ocean, 0x10 magma, 0x20 slime, 0x40 MCCV, 0x8000 do-not-fix-alpha, 0x10000 high-res holes
        public float X, Y, Z;       // world corner (largest X and Y), Z = base height
        public int AreaId;
        public ushort Holes;        // low-res holes, 4x4 bits (see AdtFile.IsHole)
        /// <summary>145 heights relative to Z, file order: rows of 9 outer, 8 inner, 9 outer ... (17 rows).</summary>
        public readonly float[] Heights = new float[145];
        /// <summary>145 x 3 signed normals as stored (127 = 1.0). AdtFile.NormalToWorld converts.</summary>
        public readonly sbyte[] Normals = new sbyte[145 * 3];
        public int LayerCount;
        public readonly AdtLayer[] Layers = new AdtLayer[4];
        /// <summary>Alpha[k] for layers 1..3: 64x64 bytes, index [row * 64 + col] (row along -X, col along -Y); null = no map.</summary>
        public readonly byte[][] Alpha = new byte[4][];
        public readonly AlphaEncoding[] AlphaFormat = new AlphaEncoding[4];
        /// <summary>64x64, 1 = shadowed (MCSH), same layout as Alpha; null when absent.</summary>
        public byte[] Shadow;
        /// <summary>145 x BGRA (0x7F = neutral) or null.</summary>
        public byte[] Mccv;
        /// <summary>Legacy MCLQ liquid (only used when the tile has no MH2O), or null.</summary>
        public AdtLegacyLiquid LegacyLiquid;

        public bool DoNotFixAlpha { get { return (Flags & 0x8000) != 0; } }
    }

    /// <summary>
    /// A 3.3.5a monolithic ADT (World\Maps\Dir\Dir_x_y.adt): MVER 18, MHDR, MCIN, MTEX, MMDX/MMID, MWMO/MWID, MDDF, MODF,
    /// MH2O, 256 MCNK. Pure C#; parse on a worker thread. Throws InvalidDataException on a broken file.
    /// </summary>
    public sealed class AdtFile
    {
        public int TileX, TileY;
        public string[] Textures = new string[0];
        public string[] M2Names = new string[0];
        public string[] WmoNames = new string[0];
        public AdtDoodadDef[] Doodads = new AdtDoodadDef[0];
        public AdtWmoDef[] Wmos = new AdtWmoDef[0];
        /// <summary>Chunks by [indexY * 16 + indexX].</summary>
        public readonly AdtChunk[] Chunks = new AdtChunk[256];
        /// <summary>MH2O liquids by [indexY * 16 + indexX] (null entries = none), or null when the tile has no MH2O.</summary>
        public AdtLiquidInstance[][] Liquids;
        public bool HasMh2o;
        public uint MhdrFlags;

        // statistics for logs / probes
        public int AlphaMaps4Bit, AlphaMaps8Bit, AlphaMapsCompressed, AlphaSizeMismatch, SubchunkFallbacks, LayersWithoutAlpha;

        public float MaxX { get { return MapConst.TileMaxX(TileY); } }
        public float MaxY { get { return MapConst.TileMaxY(TileX); } }

        /// <summary>
        /// Parses an ADT. bigAlpha = WDT MPHD flag 0x4 | 0x80: alpha maps are 8-bit (4096 bytes) or compressed (MCLY 0x200);
        /// otherwise 4-bit (2048 bytes) with the "fix" (last row/column copied from the 63rd) unless MCNK flag 0x8000.
        /// </summary>
        public static AdtFile Parse(byte[] b, int tileX, int tileY, bool bigAlpha) { return Parse(b, tileX, tileY, bigAlpha, false); }

        /// <summary>placementsOnly: skip the 256 MCNK and MH2O (only names + MDDF/MODF), for far tiles whose WMOs are shown.</summary>
        public static AdtFile Parse(byte[] b, int tileX, int tileY, bool bigAlpha, bool placementsOnly)
        {
            if (b == null) throw new InvalidDataException("ADT: no data");
            var a = new AdtFile();
            a.TileX = tileX; a.TileY = tileY;
            int mh2o = -1, mh2oSize = 0;
            var mmdx = -1; int mmdxSize = 0; var mwmo = -1; int mwmoSize = 0;
            int[] mmid = null, mwid = null;
            var chunkStarts = new List<int>(256);
            int o = 0;
            while (o + 8 <= b.Length)
            {
                string id = Bin.Magic(b, o);
                int size = Bin.I32(b, o + 4);
                int d = o + 8;
                if (size < 0 || d + size > b.Length) throw new InvalidDataException("ADT: chunk " + id + " overruns the file at " + o);
                switch (id)
                {
                    case "MHDR": a.MhdrFlags = Bin.U32(b, d); break;
                    case "MTEX": a.Textures = SplitNames(b, d, size); break;
                    case "MMDX": mmdx = d; mmdxSize = size; break;
                    case "MMID": mmid = ReadInts(b, d, size / 4); break;
                    case "MWMO": mwmo = d; mwmoSize = size; break;
                    case "MWID": mwid = ReadInts(b, d, size / 4); break;
                    case "MDDF":
                        a.Doodads = new AdtDoodadDef[size / 36];
                        for (int i = 0; i < a.Doodads.Length; i++) a.Doodads[i] = AdtDoodadDef.Read(b, d + i * 36);
                        break;
                    case "MODF":
                        a.Wmos = new AdtWmoDef[size / 64];
                        for (int i = 0; i < a.Wmos.Length; i++) a.Wmos[i] = AdtWmoDef.Read(b, d + i * 64);
                        break;
                    case "MH2O": mh2o = d; mh2oSize = size; break;
                    case "MCNK": chunkStarts.Add(o); break;
                }
                o = d + size;
            }
            a.M2Names = ResolveNames(b, mmdx, mmdxSize, mmid);
            a.WmoNames = ResolveNames(b, mwmo, mwmoSize, mwid);
            if (placementsOnly) return a;
            foreach (int cs in chunkStarts)
            {
                var c = ParseChunk(a, b, cs, bigAlpha);
                if (c.IndexX < 0 || c.IndexX > 15 || c.IndexY < 0 || c.IndexY > 15) continue;
                a.Chunks[c.IndexY * 16 + c.IndexX] = c;
            }
            if (mh2o >= 0 && mh2oSize >= 256 * 12)
            {
                a.HasMh2o = true;
                a.Liquids = AdtLiquid.ParseMh2o(b, mh2o, mh2oSize);
            }
            return a;
        }

        /// <summary>Index of the height/normal of outer vertex (row 0..8, col 0..8).</summary>
        public static int Outer(int row, int col) { return row * 17 + col; }
        /// <summary>Index of the height/normal of inner vertex (row 0..7, col 0..7).</summary>
        public static int Inner(int row, int col) { return row * 17 + 9 + col; }

        /// <summary>True when cell (row, col) (0..7) of a chunk is a hole (low-res bitmap: bit (row/2)*4 + col/2).</summary>
        public static bool IsHole(ushort holes, int row, int col)
        {
            if (holes == 0) return false;
            return (holes & (1 << ((row >> 1) * 4 + (col >> 1)))) != 0;
        }

        /// <summary>Stored normal (as read) -> world direction. Measured against the height field (probe TerrainNormals).</summary>
        public static void NormalToWorld(sbyte[] n, int i, out float wx, out float wy, out float wz)
        {
            // Measured: stored (n0, n1, n2) = world (X, Y, Z) with Z up; see report "MCNR".
            wx = n[i * 3] / 127f; wy = n[i * 3 + 1] / 127f; wz = n[i * 3 + 2] / 127f;
        }

        /// <summary>
        /// Terrain height (world Z) at world (X, Y) inside this tile, interpolated on the same 4 triangles per cell the mesh
        /// uses. False outside the tile, in a hole, or where the chunk is missing.
        /// </summary>
        public bool TryHeight(float worldX, float worldY, out float z)
        {
            z = 0f;
            float fx = (MaxX - worldX) / MapConst.ChunkSize; // along indexY
            float fy = (MaxY - worldY) / MapConst.ChunkSize; // along indexX
            if (fx < 0f || fy < 0f || fx >= 16f || fy >= 16f) return false;
            int iy = (int)fx, ix = (int)fy;
            var c = Chunks[iy * 16 + ix];
            if (c == null) return false;
            float lr = (fx - iy) * 8f, lc = (fy - ix) * 8f; // cell coords 0..8
            int row = Math.Min(7, (int)lr), col = Math.Min(7, (int)lc);
            if (IsHole(c.Holes, row, col)) return false;
            float u = lr - row, v = lc - col; // 0..1 inside the cell (u along rows, v along cols)
            float h00 = c.Heights[Outer(row, col)], h01 = c.Heights[Outer(row, col + 1)];
            float h10 = c.Heights[Outer(row + 1, col)], h11 = c.Heights[Outer(row + 1, col + 1)];
            float hc = c.Heights[Inner(row, col)];
            // Which of the 4 triangles (centre + one edge): by the diagonals of the cell.
            float h;
            float du = u - 0.5f, dv = v - 0.5f;
            if (Math.Abs(du) > Math.Abs(dv))
            {
                if (du < 0) h = Tri(hc, h00, h01, u, v, 0); // edge row 0
                else h = Tri(hc, h10, h11, u, v, 1);
            }
            else
            {
                if (dv < 0) h = Tri(hc, h00, h10, u, v, 2);
                else h = Tri(hc, h01, h11, u, v, 3);
            }
            z = c.Z + h;
            return true;
        }

        // Barycentric height on the triangle (centre, edge corner a, edge corner b) of a cell. u along rows, v along cols.
        static float Tri(float hc, float ha, float hb, float u, float v, int edge)
        {
            // centre at (0.5, 0.5); corners: edge 0 = (0,0),(0,1); 1 = (1,0),(1,1); 2 = (0,0),(1,0); 3 = (0,1),(1,1)
            float ax, ay, bx, by;
            switch (edge)
            {
                case 0: ax = 0; ay = 0; bx = 0; by = 1; break;
                case 1: ax = 1; ay = 0; bx = 1; by = 1; break;
                case 2: ax = 0; ay = 0; bx = 1; by = 0; break;
                default: ax = 0; ay = 1; bx = 1; by = 1; break;
            }
            float cx = 0.5f, cy = 0.5f;
            float det = (ay - by) * (cx - bx) + (bx - ax) * (cy - by);
            if (Math.Abs(det) < 1e-6f) return hc;
            float wc = ((ay - by) * (u - bx) + (bx - ax) * (v - by)) / det;
            float wa = ((by - cy) * (u - bx) + (cx - bx) * (v - by)) / det;
            float wb = 1f - wc - wa;
            return wc * hc + wa * ha + wb * hb;
        }

        // ---------------------------------------------------------------------------------------------------------

        static AdtChunk ParseChunk(AdtFile a, byte[] b, int cs, bool bigAlpha)
        {
            var c = new AdtChunk();
            int size = Bin.I32(b, cs + 4);
            int end = cs + 8 + size;
            int h = cs + 8; // header
            c.Flags = Bin.U32(b, h);
            c.IndexX = Bin.I32(b, h + 0x04);
            c.IndexY = Bin.I32(b, h + 0x08);
            int nLayers = Bin.I32(b, h + 0x0C);
            int ofsHeight = Bin.I32(b, h + 0x14), ofsNormal = Bin.I32(b, h + 0x18), ofsLayer = Bin.I32(b, h + 0x1C);
            int ofsAlpha = Bin.I32(b, h + 0x24), sizeAlpha = Bin.I32(b, h + 0x28);
            int ofsShadow = Bin.I32(b, h + 0x2C), sizeShadow = Bin.I32(b, h + 0x30);
            c.AreaId = Bin.I32(b, h + 0x34);
            c.Holes = Bin.U16(b, h + 0x3C);
            int ofsLiquid = Bin.I32(b, h + 0x60), sizeLiquid = Bin.I32(b, h + 0x64);
            c.X = Bin.F32(b, h + 0x68); c.Y = Bin.F32(b, h + 0x6C); c.Z = Bin.F32(b, h + 0x70);
            int ofsMccv = Bin.I32(b, h + 0x74);

            int p = Sub(a, b, cs, end, ofsHeight, "MCVT");
            if (p >= 0) for (int i = 0; i < 145; i++) c.Heights[i] = Bin.F32(b, p + i * 4);
            p = Sub(a, b, cs, end, ofsNormal, "MCNR");
            if (p >= 0) for (int i = 0; i < 145 * 3; i++) c.Normals[i] = unchecked((sbyte)b[p + i]);
            if ((c.Flags & 0x40) != 0)
            {
                p = Sub(a, b, cs, end, ofsMccv, "MCCV");
                if (p >= 0 && p + 145 * 4 <= end) { c.Mccv = new byte[145 * 4]; Buffer.BlockCopy(b, p, c.Mccv, 0, 145 * 4); }
            }
            p = Sub(a, b, cs, end, ofsLayer, "MCLY");
            int layerBytes = p >= 0 ? Bin.I32(b, p - 4) : 0;
            c.LayerCount = Math.Min(4, Math.Min(nLayers, layerBytes / 16));
            for (int i = 0; i < c.LayerCount; i++)
            {
                int l = p + i * 16;
                c.Layers[i].TextureId = Bin.I32(b, l);
                c.Layers[i].Flags = Bin.U32(b, l + 4);
                c.Layers[i].AlphaOffset = Bin.I32(b, l + 8);
                c.Layers[i].EffectId = Bin.I32(b, l + 12);
            }
            int mcal = c.LayerCount > 1 ? Sub(a, b, cs, end, ofsAlpha, "MCAL") : -1;
            // MEASURED: Northrend ADTs (MPHD 0xe) store a wrong MCAL chunk size (512 while the layers reach offset 4621+);
            // the MCNK header's sizeAlpha (= MCAL size + 8 on Azeroth, 2056 vs 2048) is right. Use the larger, within the chunk.
            int mcalSize = mcal >= 0 ? Math.Min(end - mcal, Math.Max(Bin.I32(b, mcal - 4), sizeAlpha - 8)) : 0;
            for (int i = 1; i < c.LayerCount; i++)
            {
                if ((c.Layers[i].Flags & 0x100) == 0 || mcal < 0) { a.LayersWithoutAlpha++; continue; }
                int start = c.Layers[i].AlphaOffset;
                int next = mcalSize;
                for (int k = 1; k < c.LayerCount; k++)
                {
                    int ok = c.Layers[k].AlphaOffset;
                    if (k != i && (c.Layers[k].Flags & 0x100) != 0 && ok > start && ok < next) next = ok;
                }
                int span = next - start;
                if (start < 0 || start >= mcalSize) { a.LayersWithoutAlpha++; continue; }
                var alpha = new byte[4096];
                AlphaEncoding enc;
                if (bigAlpha)
                {
                    if ((c.Layers[i].Flags & 0x200) != 0) { enc = AlphaEncoding.Compressed; if (DecodeCompressed(b, mcal + start, mcal + mcalSize, alpha) != 4096) a.AlphaSizeMismatch++; }
                    else { enc = AlphaEncoding.Bits8; Buffer.BlockCopy(b, mcal + start, alpha, 0, Math.Min(4096, mcalSize - start)); if (span != 4096) a.AlphaSizeMismatch++; }
                }
                else
                {
                    enc = AlphaEncoding.Bits4;
                    if (span != 2048) a.AlphaSizeMismatch++;
                    Decode4Bit(b, mcal + start, Math.Min(2048, mcalSize - start), alpha, c.DoNotFixAlpha);
                }
                c.Alpha[i] = alpha;
                c.AlphaFormat[i] = enc;
                if (enc == AlphaEncoding.Bits4) a.AlphaMaps4Bit++;
                else if (enc == AlphaEncoding.Bits8) a.AlphaMaps8Bit++;
                else a.AlphaMapsCompressed++;
            }
            if ((c.Flags & 0x1) != 0 && sizeShadow > 0)
            {
                p = Sub(a, b, cs, end, ofsShadow, "MCSH");
                if (p >= 0 && p + 512 <= end)
                {
                    c.Shadow = new byte[4096];
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                            c.Shadow[y * 64 + x] = (byte)((b[p + y * 8 + (x >> 3)] >> (x & 7)) & 1);
                    if (!c.DoNotFixAlpha) FixLastRowCol(c.Shadow);
                }
            }
            if (sizeLiquid > 8 && ofsLiquid > 0 && (c.Flags & 0x3C) != 0)
            {
                int lq = cs + ofsLiquid;
                if (lq + 8 <= end && Bin.Magic(b, lq) == "MCLQ") lq += 8;
                c.LegacyLiquid = AdtLiquid.ParseMclq(b, lq, Math.Min(end, cs + ofsLiquid + sizeLiquid), c.Flags);
            }
            return c;
        }

        /// <summary>Data start of the MCNK sub-chunk expected at chunkStart + ofs (the header offsets point at the sub-chunk
        /// header), or a sequential search for it when the offset is wrong. -1 when absent.</summary>
        static int Sub(AdtFile a, byte[] b, int cs, int end, int ofs, string id)
        {
            if (ofs > 0 && cs + ofs + 8 <= end && Bin.Magic(b, cs + ofs) == id) return cs + ofs + 8;
            // Fallback: walk the sub-chunks after the 128-byte header (MCNR's size omits its 13 padding bytes).
            int o = cs + 8 + 128;
            while (o + 8 <= end)
            {
                string sid = Bin.Magic(b, o);
                int sz = Bin.I32(b, o + 4);
                if (sid == id) { a.SubchunkFallbacks++; return o + 8; }
                if (sid == "MCNR") sz = 448;
                if (sz < 0) break;
                o += 8 + sz;
            }
            return -1;
        }

        /// <summary>4-bit alpha (2048 bytes): low nibble first, value * 17. Without MCNK flag 0x8000 the last row and column
        /// are copies of the 63rd (the client's "fix alpha map").</summary>
        public static void Decode4Bit(byte[] b, int o, int available, byte[] alpha, bool doNotFix)
        {
            for (int i = 0; i < 2048; i++)
            {
                int v = i < available ? b[o + i] : 0;
                alpha[i * 2] = (byte)((v & 0x0F) * 17);
                alpha[i * 2 + 1] = (byte)((v >> 4) * 17);
            }
            if (!doNotFix) FixLastRowCol(alpha);
        }

        public static void FixLastRowCol(byte[] m)
        {
            for (int i = 0; i < 64; i++) m[i * 64 + 63] = m[i * 64 + 62];
            for (int i = 0; i < 64; i++) m[63 * 64 + i] = m[62 * 64 + i];
        }

        /// <summary>Compressed alpha: control byte, bit 7 = fill (repeat the next byte) else copy, low 7 bits = count. 4096 out.</summary>
        public static int DecodeCompressed(byte[] b, int o, int end, byte[] alpha)
        {
            int w = 0;
            while (w < 4096 && o < end)
            {
                int ctl = b[o++];
                int n = ctl & 0x7F;
                if ((ctl & 0x80) != 0)
                {
                    if (o >= end) break;
                    byte v = b[o++];
                    for (int k = 0; k < n && w < 4096; k++) alpha[w++] = v;
                }
                else
                {
                    for (int k = 0; k < n && w < 4096 && o < end; k++) alpha[w++] = b[o++];
                }
            }
            return w;
        }

        static string[] SplitNames(byte[] b, int d, int size)
        {
            var list = new List<string>();
            int o = d, end = d + size;
            while (o < end)
            {
                string s = Bin.CString(b, o, end);
                if (s.Length > 0) list.Add(s);
                o += s.Length + 1;
            }
            return list.ToArray();
        }

        static int[] ReadInts(byte[] b, int d, int n)
        {
            var r = new int[n];
            for (int i = 0; i < n; i++) r[i] = Bin.I32(b, d + i * 4);
            return r;
        }

        static string[] ResolveNames(byte[] b, int block, int size, int[] offsets)
        {
            if (block < 0 || offsets == null) return new string[0];
            var r = new string[offsets.Length];
            for (int i = 0; i < offsets.Length; i++) r[i] = Bin.CString(b, block + offsets[i], block + size);
            return r;
        }
    }
}
