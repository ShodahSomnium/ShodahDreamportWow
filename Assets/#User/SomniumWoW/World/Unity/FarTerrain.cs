using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// The horizon: the map's WDL (17x17 heights per tile, measured: MAOF index [tileY*64+tileX], rows along -X, mean
    /// |WDL - ADT| 0.49 yd on the 25 tiles around Northshire) as one mesh out to the far radius, leaving out the tiles that
    /// show full terrain, coloured per vertex from the game's own minimap tile (Textures\Minimap\md5translate.trs ->
    /// 256x256 BLP, row 0 = north / col 0 = west like the ADT, checked on Azeroth_32_48). Drawn with the terrain shader in
    /// its "vertex colour" mode (layer slot 255), 2 m lower than the real terrain so the seams hide under it. Rebuilt on
    /// the worker when the set of full tiles changes or the focus moves a tile.
    /// </summary>
    internal sealed class FarTerrain
    {
        readonly WowMapView _view;
        readonly WowFiles _files;
        readonly string _dir;
        readonly int _mapId, _generation;
        readonly MapWorker _worker;
        WdlFile _wdl;
        Dictionary<string, string> _minimap;       // "Dir\mapX_Y.blp" -> hashed file
        readonly Dictionary<int, Color32[]> _colours = new Dictionary<int, Color32[]>(); // worker-owned cache
        bool _loading, _loaded, _building, _dirty = true;
        int _lastCx = -999, _lastCy = -999;
        GameObject _go;
        Mesh _mesh;
        Material _mat;
        const float SinkMetres = 2f;

        readonly float _radius;

        public FarTerrain(WowMapView view, WowFiles files, string dir, int mapId, MapWorker worker, int generation, float radiusMetres)
        {
            _view = view; _files = files; _dir = dir; _mapId = mapId; _worker = worker; _generation = generation; _radius = radiusMetres;
        }

        public void MarkDirty() { _dirty = true; }

        public void Step(Vector3 focus, Stopwatch frame, double budget)
        {
            if (!_loaded)
            {
                if (_loading) return;
                _loading = true;
                _worker.Enqueue(60000f, () =>
                {
                    var sw = Stopwatch.StartNew();
                    var wdl = WowMapData.LoadWdl(_files, _dir);
                    Dictionary<string, string> mm = null;
                    try { mm = ReadMinimapTable(_files); } catch (Exception e) { WowLog.Warn("Terrain: minimap table unreadable: " + e.Message); }
                    long ms = sw.ElapsedMilliseconds;
                    _worker.Post(() =>
                    {
                        _wdl = wdl; _minimap = mm; _loaded = true;
                        WowLog.Info("Terrain: far terrain " + _dir + ".wdl " + (wdl != null ? wdl.TileCount + " tiles" : "missing (no horizon)") + ", minimap table " + (mm != null ? mm.Count + " entries" : "-") + ", " + ms + " ms");
                    });
                });
                return;
            }
            if (_wdl == null || _building || _view.Root == null) return;
            float fx, fy, fz; WowSpace.ToWow(_mapId, focus, out fx, out fy, out fz);
            int cx, cy; MapConst.WorldToTile(fx, fy, out cx, out cy);
            if (!_dirty && cx == _lastCx && cy == _lastCy) return;
            _dirty = false; _lastCx = cx; _lastCy = cy;
            _building = true;
            int rt = (int)Math.Ceiling(_radius / WowSpace.MetresPerYard / MapConst.TileSize);
            var skip = new bool[4096];
            for (int ty = Math.Max(0, cy - rt); ty <= Math.Min(63, cy + rt); ty++)
                for (int tx = Math.Max(0, cx - rt); tx <= Math.Min(63, cx + rt); tx++)
                    skip[ty * 64 + tx] = _view.HasTerrain(tx, ty);
            var oframe = WowSpace.Current;   // built in the origin frame of now, placed by frame.Offset (floating origin)
            int gen = _generation;
            _worker.Enqueue(50000f, () =>
            {
                var sw = Stopwatch.StartNew();
                var pos = new List<Vector3>(); var col = new List<Color32>(); var idx = new List<int>();
                int tiles = 0;
                for (int ty = Math.Max(0, cy - rt); ty <= Math.Min(63, cy + rt); ty++)
                    for (int tx = Math.Max(0, cx - rt); tx <= Math.Min(63, cx + rt); tx++)
                    {
                        if (skip[ty * 64 + tx] || !_wdl.HasTile(tx, ty)) continue;
                        float dx = Math.Abs(tx - cx), dy = Math.Abs(ty - cy);
                        if ((dx - 0.5f) * (dx - 0.5f) + (dy - 0.5f) * (dy - 0.5f) > rt * rt) continue;
                        AddTile(tx, ty, pos, col, idx, oframe);
                        tiles++;
                    }
                long ms = sw.ElapsedMilliseconds;
                _worker.Post(() => { if (gen == _view.Generation) Upload(pos, col, idx, tiles, ms, oframe); _building = false; });
            });
        }

        void AddTile(int tx, int ty, List<Vector3> pos, List<Color32> col, List<int> idx, WowSpace.Frame frame)
        {
            float maxX = MapConst.TileMaxX(ty), maxY = MapConst.TileMaxY(tx);
            var colours = TileColours(tx, ty);
            int b = pos.Count;
            float step = MapConst.TileSize / 16f;
            for (int r = 0; r <= 16; r++)
                for (int c = 0; c <= 16; c++)
                {
                    float z = _wdl.OuterHeight(tx, ty, r, c);
                    var p = frame.ToUnity(maxX - r * step, maxY - c * step, z);
                    p.y -= SinkMetres;
                    pos.Add(p);
                    col.Add(colours != null ? colours[r * 17 + c] : new Color32(96, 104, 72, 255));
                }
            for (int r = 0; r < 16; r++)
                for (int c = 0; c < 16; c++)
                {
                    int tl = b + r * 17 + c, tr = tl + 1, bl = tl + 17, br = bl + 1;
                    // same orientation as the terrain cells (TL, TR, BR clockwise from above in Unity)
                    idx.Add(tl); idx.Add(tr); idx.Add(br);
                    idx.Add(tl); idx.Add(br); idx.Add(bl);
                }
        }

        Color32[] TileColours(int tx, int ty)
        {
            Color32[] c;
            int k = ty * 64 + tx;
            if (_colours.TryGetValue(k, out c)) return c;
            c = null;
            string hash;
            if (_minimap != null && _minimap.TryGetValue(_dir + "\\map" + tx + "_" + ty + ".blp", out hash))
            {
                var bytes = _files.Read("Textures\\Minimap\\" + hash);
                if (bytes != null)
                {
                    try
                    {
                        var img = BlpImage.Decode(bytes);
                        var rgba = img.GetRgba32(0);
                        int w = img.Width, h = img.Height;
                        c = new Color32[289];
                        for (int r = 0; r <= 16; r++)
                            for (int q = 0; q <= 16; q++)
                            {
                                // average a small block around the grid point
                                int cx = Math.Min(w - 1, q * w / 16), cy = Math.Min(h - 1, r * h / 16);
                                int sr = 0, sg = 0, sb = 0, n = 0;
                                for (int yy = Math.Max(0, cy - 4); yy <= Math.Min(h - 1, cy + 4); yy++)
                                    for (int xx = Math.Max(0, cx - 4); xx <= Math.Min(w - 1, cx + 4); xx++)
                                    {
                                        int o = (yy * w + xx) * 4;
                                        sr += rgba[o]; sg += rgba[o + 1]; sb += rgba[o + 2]; n++;
                                    }
                                c[r * 17 + q] = new Color32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n), 255);
                            }
                    }
                    catch (Exception) { c = null; }
                }
            }
            _colours[k] = c;
            return c;
        }

        static Dictionary<string, string> ReadMinimapTable(WowFiles files)
        {
            var b = files.Read("Textures\\Minimap\\md5translate.trs");
            if (b == null) return null;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var text = System.Text.Encoding.ASCII.GetString(b);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                d[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
            }
            return d;
        }

        void Upload(List<Vector3> pos, List<Color32> col, List<int> idx, int tiles, long buildMs, WowSpace.Frame frame)
        {
            var root = _view.Root;
            if (root == null) return;
            if (_go == null)
            {
                _go = new GameObject("Far terrain (WDL)");
                _go.transform.SetParent(root, false);
                _go.transform.position = Vector3.zero;
                _mesh = new Mesh();
                _mesh.name = "WoW far terrain";
                _mesh.indexFormat = IndexFormat.UInt32;
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = _go.AddComponent<MeshRenderer>();
                if (_view.TerrainShader != null)
                {
                    _mat = new Material(_view.TerrainShader);
                    _mat.name = "WoW far terrain";
                    _mat.SetTexture("_AlphaMap", Texture2D.whiteTexture);
                }
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            _go.transform.position = frame.Offset;   // exact integer metres from the current origin
            _mesh.Clear();
            if (pos.Count == 0) return;
            var sw = Stopwatch.StartNew();
            _mesh.SetVertices(pos);
            _mesh.SetColors(col);
            // uv0.xy unused, uv1 = layer slots: 255 marks "vertex colour only" in the terrain shader
            var uv0 = new List<Vector4>(pos.Count); var uv1 = new List<Vector4>(pos.Count);
            for (int i = 0; i < pos.Count; i++) { uv0.Add(Vector4.zero); uv1.Add(new Vector4(1f, 1f, 1f, 1f)); }
            _mesh.SetUVs(0, uv0);
            _mesh.SetUVs(1, uv1);
            _mesh.SetTriangles(idx, 0, true);
            _mesh.RecalculateNormals();
            WowLog.Info("Terrain: far terrain rebuilt: " + tiles + " WDL tiles, " + (idx.Count / 3) + " tris, build " + buildMs + " ms, upload " + sw.ElapsedMilliseconds + " ms");
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            _go = null; _mesh = null; _mat = null;
        }
    }
}
