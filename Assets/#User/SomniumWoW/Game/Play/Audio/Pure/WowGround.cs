using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>What lies under a WoW position on the terrain (pure answer of WowGround.Query).</summary>
    public struct GroundSample
    {
        public bool Known;          // the tile is loaded and the chunk exists
        public int TerrainType;     // TerrainType id of the dominant texture layer
        public string Texture;      // that layer's texture path
        public float Weight;        // its blend weight 0..1
        public int AreaId;          // MCNK area id
        public bool HasLiquid;
        public float LiquidZ;       // liquid surface (yards) when HasLiquid
        public float GroundZ;       // terrain height (yards) when HasGround
        public bool HasGround;
    }

    /// <summary>Ground types of one WMO group's collision triangles (the order WmoGeometry.BuildCollision emits them).</summary>
    public sealed class WmoGroundGroup
    {
        public string RootPath;
        public int GroupIndex;
        public sbyte[] TypeByTriangle = new sbyte[0];   // TerrainType id per collision triangle, -1 = none (material 0xFF)
        public int Dominant = WowSoundData.TerrainStone;
        public string Error;

        public int TypeOf(int triangleIndex)
        {
            if (triangleIndex >= 0 && triangleIndex < TypeByTriangle.Length && TypeByTriangle[triangleIndex] >= 0) return TypeByTriangle[triangleIndex];
            return Dominant;
        }
    }

    /// <summary>
    /// The ground under the avatar for footsteps and zone sound (pure; loading runs on the audio worker, queries on the main thread).
    /// Terrain: the ADT tile (World.AdtFile, full parse) - the chunk at the position, its layers' blend weights at that texel
    /// (sequential blending like the terrain shader: w3 = a3, w2 = a2(1-a3), w1 = a1(1-a2)(1-a3), w0 = the rest), the heaviest layer ->
    /// TerrainType (MCLY effect id -> GroundEffectTexture.Sound, else texture-name keywords); MH2O / MCLQ liquid height; MCNK area id.
    /// WMO floors: MOMT GroundType of the material of the hit collision triangle (collision triangle k = the k-th collidable MOPY
    /// triangle, exactly as WmoGeometry.BuildCollision filters them). Tiles are kept LRU (4), WMO groups (64).
    /// </summary>
    public sealed class WowGround
    {
        private sealed class Tile
        {
            public int Map, X, Y;
            public AdtFile Adt;             // null = missing / unreadable
            public int[][] LayerTypes;      // [chunk][layer]
            public long Used;
        }

        private readonly WowFiles _files;
        private readonly WowSoundData _data;
        private readonly object _lock = new object();
        private readonly List<Tile> _tiles = new List<Tile>();
        private readonly Dictionary<string, WmoGroundGroup> _groups = new Dictionary<string, WmoGroundGroup>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _groupOrder = new List<string>();
        private readonly Dictionary<string, WmoRoot> _roots = new Dictionary<string, WmoRoot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, WdtFile> _wdts = new Dictionary<int, WdtFile>();
        private long _clock;

        public WowGround(WowFiles files, WowSoundData data)
        {
            _files = files;
            _data = data;
        }

        public int TilesLoaded { get { lock (_lock) return _tiles.Count; } }

        public static void TileOf(float x, float y, out int tileX, out int tileY)
        {
            MapConst.WorldToTile(x, y, out tileX, out tileY);
        }

        /// <summary>True when the tile holding (x, y) was loaded (or found missing).</summary>
        public bool HasTile(int map, float x, float y)
        {
            int tx, ty;
            TileOf(x, y, out tx, out ty);
            return Find(map, tx, ty) != null;
        }

        private Tile Find(int map, int tx, int ty)
        {
            lock (_lock)
            {
                foreach (var t in _tiles)
                    if (t.Map == map && t.X == tx && t.Y == ty) { t.Used = ++_clock; return t; }
            }
            return null;
        }

        /// <summary>Loads (worker thread) the tile holding (x, y). Returns a log line.</summary>
        public string LoadTile(int map, float x, float y)
        {
            int tx, ty;
            TileOf(x, y, out tx, out ty);
            if (Find(map, tx, ty) != null) return null;
            var t = new Tile { Map = map, X = tx, Y = ty };
            string line;
            try
            {
                string dir = WowMapData.Directory(_files, map);
                WdtFile wdt = null;
                if (dir != null)
                {
                    lock (_lock) _wdts.TryGetValue(map, out wdt);
                    if (wdt == null)
                    {
                        wdt = WowMapData.LoadWdt(_files, dir);
                        if (wdt != null) lock (_lock) _wdts[map] = wdt;
                    }
                }
                if (dir != null && wdt != null && wdt.HasTile(tx, ty)) t.Adt = WowMapData.LoadAdt(_files, dir, tx, ty, wdt.BigAlpha);
                if (t.Adt != null)
                {
                    t.LayerTypes = new int[256][];
                    for (int i = 0; i < 256; i++)
                    {
                        var c = t.Adt.Chunks[i];
                        if (c == null) continue;
                        var lt = new int[c.LayerCount];
                        for (int l = 0; l < c.LayerCount; l++)
                        {
                            var L = c.Layers[l];
                            string tex = L.TextureId >= 0 && L.TextureId < t.Adt.Textures.Length ? t.Adt.Textures[L.TextureId] : null;
                            lt[l] = _data != null ? _data.TerrainTypeOfLayer(L.EffectId, tex) : WowSoundData.TerrainTypeOfName(tex);
                        }
                        t.LayerTypes[i] = lt;
                    }
                    line = "tile " + (dir ?? "?") + " " + tx + "_" + ty + ": " + t.Adt.Textures.Length + " textures, " + t.Adt.WmoNames.Length + " WMOs";
                }
                else line = "tile " + map + " " + tx + "_" + ty + ": none";
            }
            catch (Exception e)
            {
                t.Adt = null;
                line = "tile " + map + " " + tx + "_" + ty + " failed: " + e.Message;
            }
            lock (_lock)
            {
                t.Used = ++_clock;
                _tiles.Add(t);
                while (_tiles.Count > 4)
                {
                    int oldest = 0;
                    for (int i = 1; i < _tiles.Count; i++) if (_tiles[i].Used < _tiles[oldest].Used) oldest = i;
                    _tiles.RemoveAt(oldest);
                }
            }
            return line;
        }

        /// <summary>The terrain facts at a WoW position (Known false while the tile is not loaded).</summary>
        public GroundSample Query(int map, float x, float y)
        {
            var s = new GroundSample { TerrainType = WowSoundData.TerrainDirt };
            int tx, ty;
            TileOf(x, y, out tx, out ty);
            var t = Find(map, tx, ty);
            if (t == null || t.Adt == null) return s;
            var a = t.Adt;
            float fx = (a.MaxX - x) / MapConst.ChunkSize, fy = (a.MaxY - y) / MapConst.ChunkSize;
            if (fx < 0f || fy < 0f || fx >= 16f || fy >= 16f) return s;
            int iy = (int)fx, ix = (int)fy;
            int ci = iy * 16 + ix;
            var c = a.Chunks[ci];
            if (c == null) return s;
            s.Known = true;
            s.AreaId = c.AreaId;
            float gz;
            if (a.TryHeight(x, y, out gz)) { s.HasGround = true; s.GroundZ = gz; }

            // texel inside the chunk: row along -X, col along -Y (AdtChunk.Alpha layout)
            int row = Clamp((int)((fx - iy) * 64f), 0, 63), col = Clamp((int)((fy - ix) * 64f), 0, 63);
            int n = Math.Min(c.LayerCount, 4);
            if (n > 0)
            {
                float remaining = 1f, bestW = -1f;
                int best = 0;
                for (int l = n - 1; l >= 1; l--)
                {
                    var al = c.Alpha[l];
                    float av = al != null && al.Length >= 4096 ? al[row * 64 + col] / 255f : 0f;
                    float w = av * remaining;
                    remaining *= 1f - av;
                    if (w > bestW) { bestW = w; best = l; }
                }
                if (remaining > bestW) { bestW = remaining; best = 0; }
                var types = t.LayerTypes[ci];
                s.TerrainType = types != null && best < types.Length ? types[best] : WowSoundData.TerrainDirt;
                int texId = c.Layers[best].TextureId;
                s.Texture = texId >= 0 && texId < a.Textures.Length ? a.Textures[texId] : "";
                s.Weight = bestW;
            }

            // liquid
            float lz;
            if (LiquidAt(a, c, ci, x, y, out lz)) { s.HasLiquid = true; s.LiquidZ = lz; }
            return s;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private static bool LiquidAt(AdtFile a, AdtChunk c, int ci, float x, float y, out float z)
        {
            z = 0f;
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

        /// <summary>The full path of a WMO placed on the tile at (x, y) whose file name (no extension) is fileName, or null.</summary>
        public string WmoPathNear(int map, float x, float y, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            int tx, ty;
            TileOf(x, y, out tx, out ty);
            lock (_lock)
            {
                foreach (var t in _tiles)
                {
                    if (t.Map != map || t.Adt == null || Math.Abs(t.X - tx) > 1 || Math.Abs(t.Y - ty) > 1) continue;
                    foreach (var p in t.Adt.WmoNames)
                    {
                        if (string.IsNullOrEmpty(p)) continue;
                        int i = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
                        string f = i >= 0 ? p.Substring(i + 1) : p;
                        if (f.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase)) f = f.Substring(0, f.Length - 4);
                        if (string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase)) return p;
                    }
                }
                WdtFile wdt;
                if (_wdts.TryGetValue(map, out wdt) && wdt != null && !string.IsNullOrEmpty(wdt.GlobalWmo) && wdt.GlobalWmo.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return wdt.GlobalWmo;
            }
            return null;
        }

        private static string GroupKey(string root, int g) { return root + "#" + g; }

        /// <summary>A WMO group's per-triangle ground types when already built, else null.</summary>
        public WmoGroundGroup Group(string rootPath, int groupIndex)
        {
            lock (_lock)
            {
                WmoGroundGroup g;
                return _groups.TryGetValue(GroupKey(rootPath, groupIndex), out g) ? g : null;
            }
        }

        /// <summary>Builds (worker thread) a WMO group's ground table: root MOMT GroundType per material, group MOPY collidable triangles.</summary>
        public WmoGroundGroup BuildGroup(string rootPath, int groupIndex)
        {
            var existing = Group(rootPath, groupIndex);
            if (existing != null) return existing;
            var g = new WmoGroundGroup { RootPath = rootPath, GroupIndex = groupIndex };
            try
            {
                WmoRoot root;
                lock (_lock) _roots.TryGetValue(rootPath, out root);
                if (root == null)
                {
                    var rb = _files.Read(rootPath);
                    if (rb == null) throw new Exception("root missing");
                    root = WmoRoot.Parse(rb, rootPath);
                    lock (_lock) { _roots[rootPath] = root; if (_roots.Count > 16) _roots.Clear(); }
                }
                var gb = _files.Read(WmoRoot.GroupPath(rootPath, groupIndex));
                if (gb == null) throw new Exception("group " + groupIndex + " missing");
                var grp = WmoGroup.Parse(gb, rootPath);
                int nv = grp.VertexCount;
                int nt = Math.Min(grp.TriangleCount, grp.PolyFlags.Length);
                // MOMT GroundType is a TerrainType id, but 22893 of 25034 materials in 3.3.5a say 10 "None" (probe AudWmoCensus;
                // only Northrend-era WMOs fill it) - those take their texture name's keywords, stone when nothing matches.
                var matType = new int[root.Materials.Length];
                for (int m = 0; m < matType.Length; m++)
                {
                    var mat = root.Materials[m];
                    uint gt = mat.GroundType;
                    matType[m] = gt <= 9 ? (int)gt : WowSoundData.TerrainTypeOfName(mat.Texture1, WowSoundData.TerrainStone);
                }
                var list = new List<sbyte>(nt);
                var count = new int[12];
                for (int t = 0; t < nt; t++)
                {
                    if (!WmoGroup.IsCollidable(grp.PolyFlags[t])) continue;
                    int ia = grp.Indices[t * 3], ib = grp.Indices[t * 3 + 1], ic = grp.Indices[t * 3 + 2];
                    if (ia >= nv || ib >= nv || ic >= nv) continue;
                    int m = t < grp.PolyMaterials.Length ? grp.PolyMaterials[t] : 0xFF;
                    sbyte type = -1;
                    if (m != 0xFF && m < matType.Length)
                    {
                        type = (sbyte)matType[m];
                        count[matType[m]]++;
                    }
                    list.Add(type);
                }
                g.TypeByTriangle = list.ToArray();
                int best = -1;
                for (int k = 0; k < count.Length; k++) if (count[k] > 0 && (best < 0 || count[k] > count[best])) best = k;
                if (best >= 0) g.Dominant = best;
            }
            catch (Exception e)
            {
                g.Error = e.Message;
            }
            lock (_lock)
            {
                string key = GroupKey(rootPath, groupIndex);
                _groups[key] = g;
                _groupOrder.Add(key);
                while (_groupOrder.Count > 64) { _groups.Remove(_groupOrder[0]); _groupOrder.RemoveAt(0); }
            }
            return g;
        }
    }
}
