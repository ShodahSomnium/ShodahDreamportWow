using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Details
{
    /// <summary>
    /// One ADT tile as the details need it: the full World.AdtFile parse (heights, layers, alpha, liquids, WMO placements) plus
    /// the two MCNK header maps AdtFile does not keep - MEASURED on Azeroth_32_48 (Details report): header 0x40..0x4F is the
    /// 8x8 low-quality texture map (2 bits per cell, LSB first, cell bit = row * 8 + col; equals the alpha-dominant layer in
    /// 96.6 % of the cells) and 0x50..0x57 is noEffectDoodad (byte = row, bit = col). Rows run along -X, cols along -Y
    /// (the AdtChunk.Alpha / TryHeight layout).
    /// </summary>
    public sealed class DetailTile
    {
        public int Map, X, Y;
        public AdtFile Adt;                                 // null = no tile there / unreadable
        public readonly byte[] LowQuality = new byte[256 * 16];   // [chunk index iy*16+ix][16]
        public readonly byte[] NoEffect = new byte[256 * 8];      // [chunk index][8]
        public float[] WmoBoxes = new float[0];            // world-space boxes (minX, minY, minZ, maxX, maxY, maxZ) of the MODF placements
        public long Used;

        /// <summary>The layer (0..3) the client's low-quality map gives cell (row, col) of chunk ci.</summary>
        public int CellLayer(int ci, int row, int col)
        {
            int bit = row * 8 + col;
            return (LowQuality[ci * 16 + (bit >> 2)] >> ((bit & 3) * 2)) & 3;
        }

        /// <summary>True when the chunk's noEffectDoodad bit forbids ground clutter in cell (row, col).</summary>
        public bool CellNoEffect(int ci, int row, int col)
        {
            return ((NoEffect[ci * 8 + row] >> col) & 1) != 0;
        }
    }

    /// <summary>
    /// Terrain facts for ground clutter and critters (pure, thread-safe): ADT tiles loaded on the details worker, kept LRU (9),
    /// queried on the main thread (height, liquid, area, "inside a building's box"). Same reading as the Audio owner's
    /// WowGround (World.WowMapData + AdtFile), plus the raw MCNK header maps.
    /// </summary>
    public sealed class DetailTerrain
    {
        public const int MaxTiles = 9;

        private readonly WowFiles _files;
        private readonly object _lock = new object();
        private readonly List<DetailTile> _tiles = new List<DetailTile>();
        private readonly Dictionary<int, WdtFile> _wdts = new Dictionary<int, WdtFile>();
        private readonly Dictionary<int, string> _dirs = new Dictionary<int, string>();
        private long _clock;

        public DetailTerrain(WowFiles files) { _files = files; }

        public int TilesLoaded { get { lock (_lock) return _tiles.Count; } }

        /// <summary>The loaded tile (or the "none" marker, Adt null) or null while it was never loaded.</summary>
        public DetailTile Get(int map, int tx, int ty)
        {
            lock (_lock)
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    var t = _tiles[i];
                    if (t.Map == map && t.X == tx && t.Y == ty) { t.Used = ++_clock; return t; }
                }
            }
            return null;
        }

        public DetailTile GetAt(int map, float x, float y)
        {
            int tx, ty;
            MapConst.WorldToTile(x, y, out tx, out ty);
            return Get(map, tx, ty);
        }

        /// <summary>Loads a tile (worker thread). Returns a short log line, or null when it was already there.</summary>
        public string LoadTile(int map, int tx, int ty)
        {
            if (Get(map, tx, ty) != null) return null;
            var t = new DetailTile { Map = map, X = tx, Y = ty };
            string line;
            try
            {
                string dir;
                WdtFile wdt;
                lock (_lock) { _dirs.TryGetValue(map, out dir); _wdts.TryGetValue(map, out wdt); }
                if (dir == null)
                {
                    dir = WowMapData.Directory(_files, map);
                    if (dir != null) lock (_lock) _dirs[map] = dir;
                }
                if (dir != null && wdt == null)
                {
                    wdt = WowMapData.LoadWdt(_files, dir);
                    if (wdt != null) lock (_lock) _wdts[map] = wdt;
                }
                byte[] bytes = null;
                if (dir != null && wdt != null && wdt.HasTile(tx, ty)) bytes = _files.Read(WdtFile.AdtPath(dir, tx, ty));
                if (bytes != null)
                {
                    t.Adt = AdtFile.Parse(bytes, tx, ty, wdt.BigAlpha);
                    int maps = ReadHeaderMaps(bytes, t);
                    t.WmoBoxes = WmoBoxes(t.Adt);
                    line = dir + " " + tx + "_" + ty + " (" + maps + " MCNK maps, " + t.Adt.Wmos.Length + " WMO boxes)";
                }
                else line = map + " " + tx + "_" + ty + " none";
            }
            catch (Exception e)
            {
                t.Adt = null;
                line = map + " " + tx + "_" + ty + " failed: " + e.Message;
            }
            lock (_lock)
            {
                t.Used = ++_clock;
                _tiles.Add(t);
                while (_tiles.Count > MaxTiles)
                {
                    int oldest = 0;
                    for (int i = 1; i < _tiles.Count; i++) if (_tiles[i].Used < _tiles[oldest].Used) oldest = i;
                    _tiles.RemoveAt(oldest);
                }
            }
            return line;
        }

        public void Clear() { lock (_lock) _tiles.Clear(); }

        // MCNK header (after the 8-byte chunk header): 0x04 IndexX, 0x08 IndexY, 0x40 low-quality map (16 B), 0x50 noEffectDoodad (8 B).
        private static int ReadHeaderMaps(byte[] b, DetailTile t)
        {
            int o = 0, n = 0;
            while (o + 8 <= b.Length)
            {
                string id = Bin.Magic(b, o);
                int size = Bin.I32(b, o + 4);
                if (size < 0 || o + 8 + size > b.Length) break;
                if (id == "MCNK" && size >= 0x58)
                {
                    int h = o + 8;
                    int ix = Bin.I32(b, h + 4), iy = Bin.I32(b, h + 8);
                    if (ix >= 0 && ix < 16 && iy >= 0 && iy < 16)
                    {
                        int ci = iy * 16 + ix;
                        Buffer.BlockCopy(b, h + 0x40, t.LowQuality, ci * 16, 16);
                        Buffer.BlockCopy(b, h + 0x50, t.NoEffect, ci * 8, 8);
                        n++;
                    }
                }
                o += 8 + size;
            }
            return n;
        }

        private static float[] WmoBoxes(AdtFile a)
        {
            var r = new float[a.Wmos.Length * 6];
            for (int i = 0; i < a.Wmos.Length; i++)
            {
                var w = a.Wmos[i];
                float x0, y0, z0, x1, y1, z1;
                MapConst.PlacementToWorld(w.MinX, w.MinY, w.MinZ, out x0, out y0, out z0);
                MapConst.PlacementToWorld(w.MaxX, w.MaxY, w.MaxZ, out x1, out y1, out z1);
                r[i * 6] = Math.Min(x0, x1); r[i * 6 + 1] = Math.Min(y0, y1); r[i * 6 + 2] = Math.Min(z0, z1);
                r[i * 6 + 3] = Math.Max(x0, x1); r[i * 6 + 4] = Math.Max(y0, y1); r[i * 6 + 5] = Math.Max(z0, z1);
            }
            return r;
        }

        // ------------------------------------------------------------------------------------------ queries (any thread)

        /// <summary>Terrain height (yards) at (x, y); false while the tile is not loaded, outside terrain or in a hole.</summary>
        public bool Height(int map, float x, float y, out float z)
        {
            z = 0f;
            var t = GetAt(map, x, y);
            return t != null && t.Adt != null && t.Adt.TryHeight(x, y, out z);
        }

        /// <summary>MCNK area id at (x, y), 0 when unknown.</summary>
        public int AreaId(int map, float x, float y)
        {
            var t = GetAt(map, x, y);
            if (t == null || t.Adt == null) return 0;
            int ci = ChunkIndex(t.Adt, x, y);
            var c = ci >= 0 ? t.Adt.Chunks[ci] : null;
            return c != null ? c.AreaId : 0;
        }

        /// <summary>Chunk index (iy*16+ix) of (x, y) inside the tile, -1 outside.</summary>
        public static int ChunkIndex(AdtFile a, float x, float y)
        {
            float fx = (a.MaxX - x) / MapConst.ChunkSize, fy = (a.MaxY - y) / MapConst.ChunkSize;
            if (fx < 0f || fy < 0f || fx >= 16f || fy >= 16f) return -1;
            return (int)fx * 16 + (int)fy;
        }

        /// <summary>True when (x, y, z) lies inside (or within margin yards of) a WMO placement box of its tile.</summary>
        public bool InsideWmo(int map, float x, float y, float z, float margin)
        {
            var t = GetAt(map, x, y);
            if (t == null) return false;
            var b = t.WmoBoxes;
            for (int i = 0; i + 5 < b.Length; i += 6)
            {
                if (x < b[i] - margin || x > b[i + 3] + margin) continue;
                if (y < b[i + 1] - margin || y > b[i + 4] + margin) continue;
                if (z < b[i + 2] - margin || z > b[i + 5] + margin) continue;
                // very large boxes (a whole city or a mountain WMO) would forbid everything: only buildings count
                if ((b[i + 3] - b[i]) * (b[i + 4] - b[i + 1]) > 160f * 160f) continue;
                return true;
            }
            return false;
        }

        /// <summary>Liquid surface (yards) at (x, y) - MH2O, else the MCLQ legacy liquid (the Audio owner's WowGround reading).</summary>
        public bool Liquid(int map, float x, float y, out float z)
        {
            z = 0f;
            var t = GetAt(map, x, y);
            if (t == null || t.Adt == null) return false;
            var a = t.Adt;
            int ci = ChunkIndex(a, x, y);
            if (ci < 0) return false;
            var c = a.Chunks[ci];
            if (c == null) return false;
            float r = (c.X - x) / MapConst.UnitSize, q = (c.Y - y) / MapConst.UnitSize;   // rows along -X, cols along -Y (0..8)
            if (a.HasMh2o && a.Liquids != null)
            {
                var list = a.Liquids[ci];
                if (list == null) return false;
                bool found = false;
                foreach (var li in list)
                {
                    float cx = q - li.XOffset, cy = r - li.YOffset;
                    if (cx < 0f || cy < 0f || cx >= li.Width || cy >= li.Height) continue;
                    int ix = (int)cx, iy = (int)cy;
                    if (!li.CellExists(ix, iy)) continue;
                    float u = cx - ix, v = cy - iy;
                    float h = (li.HeightAt(ix, iy) * (1 - u) + li.HeightAt(ix + 1, iy) * u) * (1 - v)
                              + (li.HeightAt(ix, iy + 1) * (1 - u) + li.HeightAt(ix + 1, iy + 1) * u) * v;
                    if (!found || h > z) z = h;
                    found = true;
                }
                return found;
            }
            var l = c.LegacyLiquid;
            if (l == null) return false;
            int tx = (int)q, ty = (int)r;
            if (tx < 0 || ty < 0 || tx > 7 || ty > 7 || !l.TileExists(tx, ty)) return false;
            z = (l.Heights[ty * 9 + tx] + l.Heights[ty * 9 + tx + 1] + l.Heights[(ty + 1) * 9 + tx] + l.Heights[(ty + 1) * 9 + tx + 1]) * 0.25f;
            return true;
        }

        /// <summary>True when the ground at (x, y) is under water (liquid more than depth yards above the terrain).</summary>
        public bool UnderWater(int map, float x, float y, float groundZ, float depth)
        {
            float lz;
            return Liquid(map, x, y, out lz) && lz > groundZ + depth;
        }
    }
}
