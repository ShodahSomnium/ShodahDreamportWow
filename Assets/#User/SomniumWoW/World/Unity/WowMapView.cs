using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Streams one WoW map around a focus point: ADT terrain with its texture layers, holes and water, the M2 props and WMO
    /// buildings the tiles place, with colliders the avatar walks on. Positions are Unity space through WowSpace (the map's
    /// height offset included).
    ///
    /// Pipeline: a worker thread (MapWorker) reads + parses tiles nearest-first and prepares mesh data, the alpha atlas,
    /// water and tileset texture slices; the main thread turns results into Unity objects under a per-frame time budget
    /// (default 3 ms, at least one step per frame): texture slices, one mesh + one material per tile (a single draw call
    /// per pass: all 256 chunks share the map-wide texture array, per-chunk layer slots ride in the vertices), collider
    /// cooking via Physics.BakeMesh on the worker, water meshes, WMOs (one per frame) and doodads (distance-culled by model
    /// size). Tiles unload with hysteresis. Log lines: "Map: ...", "Terrain: tile ...".
    /// </summary>
    public sealed class WowMapView : MonoBehaviour
    {
        /// <summary>The map being shown, or -1.</summary>
        public int MapId { get; private set; } = -1;

        /// <summary>0..1 progress of the first load around the focus (drives the loading bar).</summary>
        public float Progress01 { get; private set; }

        /// <summary>Iteration 11 (world): the loaded map has only a global WMO and no terrain tiles (dungeons like the Stockade, the
        /// Deeprun Tram), so loading arrives at the server position instead of failing (WowGame.UpdateLoading).</summary>
        public bool IsWmoOnly { get { return _wdt != null && _wdt.IsWmoOnly; } }

        [Tooltip("Tiles whose nearest point is within this many metres get terrain, water and doodads.")]
        [SerializeField] float _terrainRadius = 800f;
        [SerializeField] float _terrainUnloadRadius = 1000f;
        [Tooltip("WMO buildings are shown within this many metres (of their bounds).")]
        [SerializeField] float _wmoRadius = 1200f;
        [SerializeField] float _wmoUnloadRadius = 1450f;
        [Tooltip("Largest doodad draw distance (trees); small props get less: min(this, 40 + 25 x radius).")]
        [SerializeField] float _doodadMaxRadius = 350f;
        [Tooltip("Main-thread milliseconds per frame for building (at least one step always runs).")]
        [SerializeField] float _frameBudgetMs = 3f;
        [Tooltip("Radius the first-load progress bar waits for.")]
        [SerializeField] float _firstLoadRadius = 250f;
        [Tooltip("Low-resolution WDL horizon out to this many metres (0 = off).")]
        [SerializeField] float _farRadius = 5000f;

        WowFiles _files;
        string _dir;
        WdtFile _wdt;
        int _generation;
        Vector3 _focus;
        bool _hasFocus;
        MapWorker _worker;
        TerrainTextureArray _textures;
        GameObject _root, _doodadRoot, _wmoRoot;
        Shader _terrainShader;
        readonly Dictionary<int, TileRecord> _tiles = new Dictionary<int, TileRecord>();
        readonly Dictionary<uint, WmoRecord> _wmos = new Dictionary<uint, WmoRecord>();
        readonly Dictionary<uint, int> _wmoNameSets = new Dictionary<uint, int>();   // iteration 7 (Body): MODF NameSet by uniqueId (WMOAreaTable key; WmoPlacement has none)
        readonly Dictionary<uint, DoodadRecord> _doodads = new Dictionary<uint, DoodadRecord>();
        readonly List<DoodadRecord> _doodadList = new List<DoodadRecord>();
        readonly List<WmoRecord> _wmoList = new List<WmoRecord>();
        readonly Dictionary<string, float> _modelRadius = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _failedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int _doodadCursor;
        float _nextWantedCheck;
        Vector3 _lastWantedFocus;
        bool _firstLoadDone;
        float _loadStartTime;
        readonly Stopwatch _frame = new Stopwatch();
        FarTerrain _far;
        int _shownDoodads, _builtWmos;
        float _nextStatsLog;

        static readonly int AlphaMapId = Shader.PropertyToID("_AlphaMap");

        sealed class TileRecord
        {
            public int X, Y;
            public float Distance;
            public bool WantFull;
            public bool QueuedFull, QueuedPlacements;
            public bool HaveFull, HavePlacements;
            public TileBuild Pending;        // full build waiting for its textures / activation
            public AdtFile Adt;              // trimmed (heights) once active
            public GameObject Go;
            public Mesh Mesh;
            public Texture2D AlphaTex;
            public Material Material;
            public MeshCollider Collider;
            public bool BakeQueued, Baked, ColliderReady;
            public readonly List<Mesh> WaterMeshes = new List<Mesh>();
            public int[] Slots;
            public readonly List<uint> WmoIds = new List<uint>();
            public readonly List<uint> DoodadIds = new List<uint>();
            public bool Missing;             // WDT says it exists but the file is missing/broken
        }

        sealed class WmoRecord
        {
            public WmoPlacement P;
            public int Refs;
            public GameObject Go;
            public int State; // 0 not built, 1 built, 2 failed
            public bool PrepQueued, Prepared; // integrator: WmoFactory.Prepare on the worker (Buildings' request)
            public float PrepQueuedAt;
        }

        sealed class DoodadRecord
        {
            public DoodadPlacement P;
            public int Refs;
            public GameObject Go;
            public bool Removed;
        }

        // ------------------------------------------------------------------------------------------------ public API

        /// <summary>Starts streaming mapId (Map.dbc id) around unityFocus. Replaces any map already shown.</summary>
        public void Load(WowFiles files, int mapId, Vector3 unityFocus)
        {
            Unload();
            if (files == null) { WowLog.Error("Map: Load(" + mapId + ") without game files"); return; }
            var sw = Stopwatch.StartNew();
            _files = files;
            _dir = WowMapData.Directory(files, mapId);
            if (_dir == null) { WowLog.Error("Map: map id " + mapId + " is not in Map.dbc"); return; }
            _wdt = WowMapData.LoadWdt(files, _dir);
            if (_wdt == null) { WowLog.Error("Map: " + _dir + " has no readable WDT (" + WdtFile.PathOf(_dir) + ")"); return; }
            MapId = mapId;
            _generation++;
            _focus = unityFocus;
            _hasFocus = true;
            _lastWantedFocus = new Vector3(float.MaxValue, 0, 0);
            _nextWantedCheck = 0f;
            _firstLoadDone = false;
            Progress01 = 0f;
            _loadStartTime = Time.realtimeSinceStartup;
            _terrainShader = WowShaders.Find("SomniumWoW/Terrain");
            if (_terrainShader == null)
                WowLog.Error("Map: shader SomniumWoW/Terrain missing (carrier material WowTerrain.mat not in WowGame._shaderCarriers?) - terrain will not draw");
            _root = new GameObject("WoW Map " + mapId + " " + _dir);
            _root.transform.SetParent(transform, false);
            _root.transform.position = Vector3.zero;
            _root.transform.rotation = Quaternion.identity;
            _doodadRoot = new GameObject("Doodads");
            _doodadRoot.transform.SetParent(_root.transform, false);
            _wmoRoot = new GameObject("WMOs");
            _wmoRoot.transform.SetParent(_root.transform, false);
            _textures = new TerrainTextureArray();
            _worker = new MapWorker("SomniumWoW map " + _dir);
            float fx, fy, fz; WowSpace.ToWow(mapId, unityFocus, out fx, out fy, out fz);
            int tx, ty; MapConst.WorldToTile(fx, fy, out tx, out ty);
            WowLog.Info("Map: Load " + mapId + " " + _dir + " (" + _wdt.TileCount + " tiles, MPHD 0x" + _wdt.Flags.ToString("x") + (_wdt.BigAlpha ? " big alpha" : "")
                + (_wdt.GlobalWmo != null ? ", global WMO " + _wdt.GlobalWmo : "") + ") focus wow (" + fx.ToString("0.0") + ", " + fy.ToString("0.0") + ", " + fz.ToString("0.0") + ") tile " + tx + "_" + ty
                + (_wdt.HasTile(tx, ty) ? "" : " (no ADT there)") + ", " + sw.ElapsedMilliseconds + " ms");
            if (_wdt.GlobalWmo != null)
            {
                var m = new float[9];
                var gdef = _wdt.GlobalWmoWorldDef;   // iteration 11 (world): AC's vmap rule - a MODF at (0, y, 0) stands at world (0, 0)
                if (gdef.PX != _wdt.GlobalWmoDef.PX)
                    WowLog.Info("Map: " + _dir + " global WMO at MODF (0, " + gdef.PY.ToString("0.0") + ", 0) placed at world (0, 0) like AzerothCore's vmap extractor (vmap4_extractor_wmo.cpp:559-570)");
                var p = TerrainTileBuilder.ToPlacement(gdef, new[] { _wdt.GlobalWmo }, WowSpace.Current, m);
                var rec = new WmoRecord { P = p, Refs = 1 };
                _wmoNameSets[p.UniqueId] = _wdt.GlobalWmoDef.NameSet;   // iteration 7 (Body)
                _wmos[p.UniqueId] = rec;
                _wmoList.Add(rec);
            }
            if (_farRadius > 0f && _wdt.TileCount > 0)
            {
                _far = new FarTerrain(this, _files, _dir, mapId, _worker, _generation, _farRadius);
            }
        }

        /// <summary>Moves the streaming focus (call every frame with the avatar position).</summary>
        public void SetFocus(Vector3 unityFocus)
        {
            // The black room (WowSpace.LoginRoomOrigin, 3 km above the origin) is not in the map: a focus there (the avatar
            // still standing in the room while the map loads) would unload the tiles around the spot.
            if (MapId >= 0 && (unityFocus - WowSpace.LoginRoomOrigin).sqrMagnitude < 400f * 400f)
            {
                if (!_ignoredFocusLogged) { _ignoredFocusLogged = true; WowLog.Info("Map: SetFocus " + unityFocus + " is in the login room - ignored (streaming stays at " + _focus + ")"); }
                return;
            }
            _focus = unityFocus;
            _hasFocus = true;
        }

        void OnEnable() { WowSpace.Rebased += OnRebased; }
        void OnDisable() { WowSpace.Rebased -= OnRebased; }

        /// <summary>
        /// Floating origin: WowOrigin already moved every object (tiles, far terrain, WMOs, doodads are children of zero-position
        /// containers under the WoW Game object); here only the Unity positions kept in the records move with them. Tile
        /// builds still on the worker are tagged with their frame (TileBuild.Frame) and placed by its Offset when they arrive.
        /// </summary>
        void OnRebased(Vector3 d)
        {
            _focus -= d;
            if (_lastWantedFocus.x != float.MaxValue) _lastWantedFocus -= d;
            foreach (var w in _wmoList)
            {
                w.P = Moved(w.P, -d);
            }
            foreach (var r in _doodadList)
            {
                r.P = Moved(r.P, -d);
            }
            if (_far != null) _far.MarkDirty();
        }

        static WmoPlacement Moved(WmoPlacement p, Vector3 by)
        {
            if (by == Vector3.zero) return p;
            p.Position += by;
            var b = p.Bounds;
            b.center += by;
            p.Bounds = b;
            return p;
        }

        static DoodadPlacement Moved(DoodadPlacement p, Vector3 by)
        {
            p.Position += by;
            return p;
        }

        bool _ignoredFocusLogged;
        int _colliderLogs;

        /// <summary>True when ground and colliders exist within radiusMetres of unityPos (safe to put the avatar there).</summary>
        public bool IsReadyAround(Vector3 unityPos, float radiusMetres)
        {
            if (MapId < 0 || _wdt == null) return false;
            float wx, wy, wz; WowSpace.ToWow(MapId, unityPos, out wx, out wy, out wz);
            float r = radiusMetres / WowSpace.MetresPerYard;
            int x0, y0, x1, y1;
            MapConst.WorldToTile(wx + r, wy + r, out x0, out y0);
            MapConst.WorldToTile(wx - r, wy - r, out x1, out y1);
            for (int ty = Math.Max(0, y0); ty <= Math.Min(63, y1); ty++)
                for (int tx = Math.Max(0, x0); tx <= Math.Min(63, x1); tx++)
                {
                    if (!_wdt.HasTile(tx, ty)) continue;
                    TileRecord t;
                    if (!_tiles.TryGetValue(Key(tx, ty), out t)) return false;
                    if (t.Missing) continue;
                    if (!t.ColliderReady) return false;
                }
            foreach (var w in _wmoList)
            {
                if (w.Refs <= 0) continue;
                if (Mathf.Sqrt(w.P.Bounds.SqrDistance(unityPos)) > radiusMetres) continue;
                if (w.State == 0) return false;
            }
            return true;
        }

        /// <summary>The terrain height under unityPos from the loaded tiles (no raycast needed).</summary>
        public bool TryGroundHeight(Vector3 unityPos, out float unityY)
        {
            unityY = 0f;
            if (MapId < 0) return false;
            float wx, wy, wz; WowSpace.ToWow(MapId, unityPos, out wx, out wy, out wz);
            int tx, ty; MapConst.WorldToTile(wx, wy, out tx, out ty);
            TileRecord t;
            if (!_tiles.TryGetValue(Key(tx, ty), out t) || t.Adt == null) return false;
            float z;
            if (!t.Adt.TryHeight(wx, wy, out z)) return false;
            unityY = WowSpace.ToUnity(MapId, wx, wy, z).y;
            return true;
        }

        /// <summary>
        /// The first collider surface (terrain, WMO floors, solid doodads) straight below unityPos + searchUp metres, by a
        /// physics raycast. Use for spawn points inside WMO cities, where the terrain height is under the city floor.
        /// </summary>
        public bool TryFloorHeight(Vector3 unityPos, float searchUp, out float unityY)
        {
            unityY = 0f;
            RaycastHit hit;
            var from = unityPos + Vector3.up * searchUp;
            if (Physics.Raycast(from, Vector3.down, out hit, searchUp + 2000f, 1, QueryTriggerInteraction.Ignore)) { unityY = hit.point.y; return true; }
            return TryGroundHeight(unityPos, out unityY);
        }

        /// <summary>Destroys everything built for the map.</summary>
        public void Unload()
        {
            if (_worker != null) { _worker.Stop(); _worker = null; }
            _generation++;
            foreach (var t in _tiles.Values) DestroyTileObjects(t, false);
            _tiles.Clear();
            foreach (var w in _wmoList) if (w.Go != null) Destroy(w.Go);
            _wmos.Clear(); _wmoList.Clear(); _wmoNameSets.Clear();
            foreach (var d in _doodadList) if (d.Go != null) Destroy(d.Go);
            _doodads.Clear(); _doodadList.Clear();
            _m2PrepQueued.Clear();
            if (_far != null) { _far.Dispose(); _far = null; }
            if (_textures != null) { _textures.Dispose(); _textures = null; }
            if (_root != null) Destroy(_root);
            _root = _doodadRoot = _wmoRoot = null;
            if (MapId >= 0) WowLog.Info("Map: unloaded " + MapId + " " + _dir);
            MapId = -1;
            Progress01 = 0f;
            _wdt = null;
            _dir = null;
            _shownDoodads = _builtWmos = 0;
        }

        void OnDestroy() { Unload(); }

        // ------------------------------------------------------------------------------------------------ frame loop

        void Update()
        {
            if (MapId < 0 || _worker == null) return;
            _frame.Reset(); _frame.Start();
            if (_hasFocus && (Time.unscaledTime >= _nextWantedCheck || (_focus - _lastWantedFocus).sqrMagnitude > 30f * 30f))
            {
                UpdateWanted();
                _nextWantedCheck = Time.unscaledTime + 0.5f;
                _lastWantedFocus = _focus;
            }
            // 1) results posted by the worker (cheap bookkeeping)
            for (int i = 0; i < 64; i++)
            {
                var a = _worker.TakeResult();
                if (a == null) break;
                a();
            }
            // the loading screen is up until the first load is done: build faster then
            double budget = Math.Max(0.5, _firstLoadDone ? _frameBudgetMs : Math.Max(_frameBudgetMs, 8f));
            // 2) tileset texture slices
            _textures.Pump(_frame, budget);
            // 3) tile activation, nearest first
            bool didTile = false;
            while (_frame.Elapsed.TotalMilliseconds < budget || !didTile)
            {
                var t = NearestPending();
                if (t == null) break;
                Activate(t);
                didTile = true;
            }
            // 4) colliders whose bake finished
            foreach (var t in _tiles.Values)
                if (t.Baked && !t.ColliderReady && t.Collider != null)
                {
                    var csw = Stopwatch.StartNew();
                    t.Collider.sharedMesh = t.Mesh;
                    if (_colliderLogs++ < 3 || csw.ElapsedMilliseconds > 5) WowLog.Info("Terrain: collider of tile " + t.X + "_" + t.Y + " assigned in " + csw.ElapsedMilliseconds + " ms (baked on the worker first)");
                    t.ColliderReady = true;
                }
            // 5) WMOs: at most one per frame (the Buildings owner's Build may be heavy)
            if (_frame.Elapsed.TotalMilliseconds < budget || !didTile) StepWmos();
            // 6) doodads
            StepDoodads(budget);
            // 7) far terrain
            if (_far != null) _far.Step(_focus, _frame, budget);
            UpdateProgress();
            if (Time.unscaledTime >= _nextStatsLog && _firstLoadDone)
            {
                _nextStatsLog = Time.unscaledTime + 30f;
                LogStats("Map: streaming");
            }
        }

        static int Key(int x, int y) { return y * 64 + x; }

        float TileDistance(int tx, int ty, float fx, float fy)
        {
            // yards -> metres, nearest point of the tile rectangle
            float maxX = MapConst.TileMaxX(ty), maxY = MapConst.TileMaxY(tx);
            float dx = Math.Max(0f, Math.Max(fx - maxX, (maxX - MapConst.TileSize) - fx));
            float dy = Math.Max(0f, Math.Max(fy - maxY, (maxY - MapConst.TileSize) - fy));
            return (float)Math.Sqrt(dx * dx + dy * dy) * WowSpace.MetresPerYard;
        }

        void UpdateWanted()
        {
            if (_wdt.TileCount == 0) return;
            float fx, fy, fz; WowSpace.ToWow(MapId, _focus, out fx, out fy, out fz);
            float placeRadius = _wmoRadius + 300f;           // big WMOs are listed by every tile they touch
            float placeUnload = _wmoUnloadRadius + 300f;
            int rt = (int)Math.Ceiling(placeUnload / WowSpace.MetresPerYard / MapConst.TileSize) + 1;
            int cx, cy; MapConst.WorldToTile(fx, fy, out cx, out cy);
            for (int ty = Math.Max(0, cy - rt); ty <= Math.Min(63, cy + rt); ty++)
                for (int tx = Math.Max(0, cx - rt); tx <= Math.Min(63, cx + rt); tx++)
                {
                    if (!_wdt.HasTile(tx, ty)) continue;
                    float d = TileDistance(tx, ty, fx, fy);
                    int k = Key(tx, ty);
                    TileRecord t;
                    _tiles.TryGetValue(k, out t);
                    if (t == null)
                    {
                        if (d > placeRadius) continue;
                        t = new TileRecord { X = tx, Y = ty };
                        _tiles[k] = t;
                    }
                    t.Distance = d;
                    t.WantFull = d <= _terrainRadius || (t.HaveFull && d <= _terrainUnloadRadius);
                    if (!t.WantFull && t.Pending != null) { ReleaseSlots(_textures, t.Pending.Slots); t.Pending = null; }
                    if (t.Missing) continue;
                    if (t.WantFull && !t.HaveFull && !t.QueuedFull) QueueParse(t, true);
                    else if (!t.WantFull && !t.HavePlacements && !t.QueuedPlacements && !t.QueuedFull) QueueParse(t, false);
                }
            // unload
            var drop = new List<int>();
            foreach (var kv in _tiles)
            {
                var t = kv.Value;
                float d = TileDistance(t.X, t.Y, fx, fy);
                t.Distance = d;
                if (t.HaveFull && d > _terrainUnloadRadius) DowngradeToPlacements(t);
                if (d > placeUnload) drop.Add(kv.Key);
            }
            foreach (int k in drop)
            {
                var t = _tiles[k];
                DestroyTileObjects(t, true);
                _tiles.Remove(k);
            }
        }

        void QueueParse(TileRecord t, bool full)
        {
            if (full) t.QueuedFull = true; else t.QueuedPlacements = true;
            int gen = _generation, x = t.X, y = t.Y, mapId = MapId;
            var frame = WowSpace.Current;   // the tile is built in the origin frame of now; placed later by frame.Offset
            var files = _files; var dir = _dir; bool big = _wdt.BigAlpha; var tex = _textures; var worker = _worker;
            _worker.Enqueue(t.Distance + (full ? 0f : 100000f), () =>
            {
                if (gen != _generation) return;
                var tb = new TileBuild { TileX = x, TileY = y, Full = full, Frame = frame };
                var sw = Stopwatch.StartNew();
                var bytes = files.Read(WdtFile.AdtPath(dir, x, y));
                tb.ReadMs = sw.ElapsedMilliseconds; sw.Restart();
                if (bytes == null) tb.Error = "missing " + WdtFile.AdtPath(dir, x, y);
                else
                {
                    try { tb.Adt = AdtFile.Parse(bytes, x, y, big, !full); }
                    catch (Exception e) { tb.Error = "unreadable " + WdtFile.AdtPath(dir, x, y) + ": " + e.Message; }
                }
                tb.ParseMs = sw.ElapsedMilliseconds; sw.Restart();
                if (tb.Adt != null)
                {
                    TerrainTileBuilder.Placements(tb, mapId, full);
                    if (full)
                    {
                        tb.Slots = new int[tb.Adt.Textures.Length];
                        for (int i = 0; i < tb.Slots.Length; i++)
                        {
                            bool needs;
                            tb.Slots[i] = tex.Acquire(tb.Adt.Textures[i], out needs);
                            if (tb.Slots[i] < 0) WowLog.Warn("Terrain: no texture slot left for " + tb.Adt.Textures[i]);
                            if (needs) tex.Prepare(files, tb.Adt.Textures[i], tb.Slots[i]);
                        }
                        TerrainTileBuilder.BuildTerrain(tb, mapId);
                        foreach (var wb in tb.Water) { try { WowWater.Prewarm(files, wb.LiquidType); } catch (Exception e) { WowLog.Warn("WowWater: prewarm " + wb.LiquidType + " failed: " + e.Message); } }
                    }
                }
                tb.BuildMs = sw.ElapsedMilliseconds;
                worker.Post(() => { if (gen == _generation) OnParsed(tb); else ReleaseSlots(tex, tb.Slots); });
            });
        }

        static void ReleaseSlots(TerrainTextureArray tex, int[] slots)
        {
            if (slots == null || tex == null) return;
            foreach (int s in slots) tex.Release(s);
        }

        void OnParsed(TileBuild tb)
        {
            TileRecord t;
            if (!_tiles.TryGetValue(Key(tb.TileX, tb.TileY), out t))
            {
                ReleaseSlots(_textures, tb.Slots);
                return;
            }
            if (tb.Full) t.QueuedFull = false; else t.QueuedPlacements = false;
            if (tb.Adt == null)
            {
                t.Missing = true;
                WowLog.Warn("Terrain: tile " + tb.TileX + "_" + tb.TileY + " " + tb.Error);
                return;
            }
            // placements (WMOs) are registered for either kind of parse
            if (!t.HavePlacements)
            {
                Vector3 off = tb.Frame.Offset;
                foreach (var w in tb.Wmos) AddWmo(t, Moved(w, off));
                foreach (var d in tb.Adt.Wmos) _wmoNameSets[d.UniqueId] = d.NameSet;   // iteration 7 (Body)
                t.HavePlacements = true;
            }
            if (tb.Full)
            {
                if (t.HaveFull || !t.WantFull)
                {
                    ReleaseSlots(_textures, tb.Slots); // arrived after the tile went out of range again
                    return;
                }
                t.Pending = tb;
            }
        }

        TileRecord NearestPending()
        {
            TileRecord best = null;
            foreach (var t in _tiles.Values)
            {
                if (t.Pending == null) continue;
                bool ready = true;
                foreach (int s in t.Pending.Slots) if (!_textures.IsResident(s)) { ready = false; break; }
                if (!ready) continue;
                if (best == null || t.Distance < best.Distance) best = t;
            }
            return best;
        }

        void Activate(TileRecord t)
        {
            var tb = t.Pending;
            t.Pending = null;
            var sw = Stopwatch.StartNew();
            var name = _dir + "_" + tb.TileX + "_" + tb.TileY;
            var mesh = new Mesh();
            mesh.name = "WoW terrain " + name;
            const MeshUpdateFlags F = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferParams(tb.Vertices.Length, TerrainVertex.Layout);
            mesh.SetVertexBufferData(tb.Vertices, 0, 0, tb.Vertices.Length, 0, F);
            mesh.SetIndexBufferParams(tb.IndexCount, IndexFormat.UInt16);
            mesh.SetIndexBufferData(tb.Indices, 0, 0, tb.IndexCount, F);
            mesh.subMeshCount = 1;
            var sub = new SubMeshDescriptor(0, tb.IndexCount, MeshTopology.Triangles);
            sub.bounds = tb.LocalBounds;
            sub.firstVertex = 0;
            sub.vertexCount = tb.Vertices.Length;
            mesh.SetSubMesh(0, sub, F);
            mesh.bounds = tb.LocalBounds;

            long lapMesh = sw.ElapsedMilliseconds;
            var tex = new Texture2D(TerrainTileBuilder.AtlasSize, TerrainTileBuilder.AtlasSize, TextureFormat.RGBA32, false, true);
            tex.name = "WoW alpha " + name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.LoadRawTextureData(tb.Alpha);
            tex.Apply(false, true);

            long lapTex = sw.ElapsedMilliseconds;
            Material mat = null;
            if (_terrainShader != null)
            {
                mat = new Material(_terrainShader);
                mat.name = "WoW terrain " + name;
                mat.SetTexture(AlphaMapId, tex);
            }

            var go = new GameObject("Tile " + tb.TileX + "_" + tb.TileY);
            go.layer = 0;
            go.transform.SetParent(_root.transform, false);
            go.transform.position = tb.Origin + tb.Frame.Offset;   // built in tb.Frame: exact integer metres from the current origin
            // MEASURED in the lab: adding the MeshCollider after the MeshFilter made Unity pick up and cook the 65k-triangle mesh
            // right there (15-30 ms per tile on the main thread). Added first, it stays empty until the worker bake is done.
            var col = go.AddComponent<MeshCollider>();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            t.Go = go; t.Mesh = mesh; t.AlphaTex = tex; t.Material = mat; t.Collider = col;
            t.Slots = tb.Slots;
            t.Adt = tb.Adt;
            t.HaveFull = true;
            t.Baked = false; t.ColliderReady = false; t.BakeQueued = true;
            var meshId = mesh.GetEntityId();
            int gen = _generation;
            var worker = _worker;
            _worker.Enqueue(-1f, () =>
            {
                try { Physics.BakeMesh(meshId, false); }
                catch (Exception e) { WowLog.Warn("Terrain: Physics.BakeMesh on the worker failed (" + e.GetType().Name + ": " + e.Message + "); the collider cooks on the main thread"); }
                worker.Post(() => { if (gen == _generation && t.Mesh == mesh) t.Baked = true; });
            });

            long lapObj = sw.ElapsedMilliseconds;
            // water
            int waterTris = 0;
            foreach (var wb in tb.Water)
            {
                var wm = new Mesh();
                wm.name = "WoW liquid " + wb.LiquidType + " " + name;
                if (wb.Positions.Count > 65000) wm.indexFormat = IndexFormat.UInt32;
                wm.SetVertices(wb.Positions);
                wm.SetUVs(0, wb.Uvs);
                wm.SetColors(wb.Colors);
                wm.SetTriangles(wb.Indices, 0, true);
                wm.RecalculateNormals();
                waterTris += wb.Indices.Count / 3;
                Material wmat = null;
                try { wmat = WowWater.ForLiquidType(_files, wb.LiquidType); }
                catch (Exception e) { WowLog.Warn("Terrain: water material " + wb.LiquidType + " failed: " + e.Message); }
                var wgo = new GameObject("Liquid " + wb.LiquidType);
                wgo.transform.SetParent(go.transform, false);
                wgo.AddComponent<MeshFilter>().sharedMesh = wm;
                var wr = wgo.AddComponent<MeshRenderer>();
                wr.sharedMaterial = wmat;
                wr.shadowCastingMode = ShadowCastingMode.Off;
                wr.receiveShadows = false;
                wr.lightProbeUsage = LightProbeUsage.Off;
                wr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                t.WaterMeshes.Add(wm);
            }
            long lapWater = sw.ElapsedMilliseconds;
            // doodads (deduped by uniqueId across tiles)
            Vector3 doff = tb.Frame.Offset;
            foreach (var d in tb.Doodads) AddDoodad(t, Moved(d, doff));
            TerrainTileBuilder.Trim(tb.Adt);
            if (_far != null) _far.MarkDirty();
            WowLog.Info("Terrain: tile " + name + " " + tb.Chunks + " chunks, " + tb.Layers + " layers (" + tb.Adt.Textures.Length + " textures), "
                + (tb.Adt.AlphaMaps4Bit > 0 ? tb.Adt.AlphaMaps4Bit + " 4-bit " : "") + (tb.Adt.AlphaMaps8Bit > 0 ? tb.Adt.AlphaMaps8Bit + " 8-bit " : "")
                + (tb.Adt.AlphaMapsCompressed > 0 ? tb.Adt.AlphaMapsCompressed + " rle " : "") + "alpha, " + tb.HoleCells + " hole cells, "
                + tb.Doodads.Count + " doodads, " + tb.Wmos.Count + " wmos, " + tb.LiquidInstances + " liquids (" + waterTris + " tris), "
                + (tb.IndexCount / 3) + " tris" + (tb.WindingChecked ? (tb.WindingFlipped ? " (winding flipped)" : "") : " (winding unchecked)")
                + ", read " + tb.ReadMs + " ms, parse " + tb.ParseMs + " ms, build " + tb.BuildMs + " ms, upload " + sw.ElapsedMilliseconds + " ms (mesh " + lapMesh + ", alpha " + (lapTex - lapMesh) + ", objects " + (lapObj - lapTex) + ", water " + (lapWater - lapObj) + ")");
        }

        void DowngradeToPlacements(TileRecord t)
        {
            DestroyTerrainObjects(t);
            t.HaveFull = false;
            t.WantFull = false;
            if (_far != null) _far.MarkDirty();
        }

        void DestroyTerrainObjects(TileRecord t)
        {
            if (t.Pending != null) { ReleaseSlots(_textures, t.Pending.Slots); t.Pending = null; }
            if (t.Go != null) Destroy(t.Go);
            if (t.Mesh != null) Destroy(t.Mesh);
            if (t.AlphaTex != null) Destroy(t.AlphaTex);
            if (t.Material != null) Destroy(t.Material);
            foreach (var m in t.WaterMeshes) if (m != null) Destroy(m);
            t.WaterMeshes.Clear();
            t.Go = null; t.Mesh = null; t.AlphaTex = null; t.Material = null; t.Collider = null;
            t.ColliderReady = false; t.Baked = false; t.BakeQueued = false;
            ReleaseSlots(_textures, t.Slots);
            t.Slots = null;
            t.Adt = null;
            foreach (uint id in t.DoodadIds) ReleaseDoodad(id);
            t.DoodadIds.Clear();
        }

        void DestroyTileObjects(TileRecord t, bool releaseWmos)
        {
            DestroyTerrainObjects(t);
            if (releaseWmos) foreach (uint id in t.WmoIds) ReleaseWmo(id);
            t.WmoIds.Clear();
        }

        // ------------------------------------------------------------------------------------------------ WMOs

        void AddWmo(TileRecord t, WmoPlacement p)
        {
            if (p.Path == null) return;
            WmoRecord w;
            if (!_wmos.TryGetValue(p.UniqueId, out w))
            {
                w = new WmoRecord { P = p };
                _wmos[p.UniqueId] = w;
                _wmoList.Add(w);
            }
            w.Refs++;
            t.WmoIds.Add(p.UniqueId);
        }

        void ReleaseWmo(uint id)
        {
            WmoRecord w;
            if (!_wmos.TryGetValue(id, out w)) return;
            if (--w.Refs > 0) return;
            if (w.Go != null) { Destroy(w.Go); _builtWmos--; }
            _wmos.Remove(id);
            _wmoList.Remove(w);
        }

        void StepWmos()
        {
            WmoRecord best = null;
            float bestD = float.MaxValue;
            float unload2 = _wmoUnloadRadius * _wmoUnloadRadius;
            foreach (var w in _wmoList)
            {
                float d2 = w.P.Bounds.SqrDistance(_focus);
                if (w.State == 1 && d2 > unload2 && w.Refs > 0 && _wdt.GlobalWmo == null)
                {
                    if (w.Go != null) { Destroy(w.Go); _builtWmos--; }
                    w.Go = null; w.State = 0;
                    continue;
                }
                if (w.State != 0) continue;
                if (d2 > _wmoRadius * _wmoRadius && _wdt.GlobalWmo == null) continue;
                // Integrator (Buildings' + Terrain's request): read/parse/decode the WMO on the worker (WmoFactory.Prepare),
                // then Build here only creates the Unity objects (Stormwind took 1190 ms in one frame when Build prepared it
                // on the main thread). If the worker has not finished it within 60 s, Build prepares it itself as before.
                if (!w.Prepared)
                {
                    if (!w.PrepQueued) QueuePrepareWmo(w, Mathf.Sqrt(d2));
                    if (WmoFactory.IsPrepared(w.P.Path)) w.Prepared = true;
                    else if (Time.realtimeSinceStartup - w.PrepQueuedAt < 60f) continue;
                }
                if (d2 < bestD) { bestD = d2; best = w; }
            }
            if (best == null) return;
            var sw = Stopwatch.StartNew();
            GameObject go = null;
            int nameSet; _wmoNameSets.TryGetValue(best.P.UniqueId, out nameSet);   // iteration 7 (Body): the MODF NameSet for WMOAreaTable
            try { go = WmoFactory.Build(_files, best.P.Path, best.P.DoodadSet, true, nameSet); }
            catch (Exception e) { WowLog.Warn("Map: WmoFactory.Build " + best.P.Path + " threw " + e.GetType().Name + ": " + e.Message); }
            if (go == null) { best.State = 2; return; }
            go.transform.SetParent(_wmoRoot.transform, false);
            go.transform.position = best.P.Position;
            go.transform.rotation = best.P.Rotation;
            go.transform.localScale = Vector3.one;
            best.Go = go;
            best.State = 1;
            _builtWmos++;
            long ms = sw.ElapsedMilliseconds;
            if (ms > 20) WowLog.Info("Map: WMO " + best.P.Path + " (set " + best.P.DoodadSet + ") built in " + ms + " ms" + (best.Prepared ? " (prepared on the worker)" : " (prepared on the main thread)"));
        }

        void QueuePrepareWmo(WmoRecord w, float distance)
        {
            w.PrepQueued = true;
            w.PrepQueuedAt = Time.realtimeSinceStartup;
            if (_worker == null || string.IsNullOrEmpty(w.P.Path)) return;
            int gen = _generation; var files = _files; string path = w.P.Path;
            _worker.Enqueue(distance, () =>
            {
                if (gen != _generation) return;
                try { WmoFactory.Prepare(files, path); }
                catch (Exception e) { WowLog.Warn("Map: WmoFactory.Prepare " + path + " threw " + e.GetType().Name + ": " + e.Message); }
            });
        }

        void QueuePrepareDoodad(string path, float distance)
        {
            if (_worker == null || string.IsNullOrEmpty(path) || !_m2PrepQueued.Add(path)) return;
            int gen = _generation; var files = _files;
            _worker.Enqueue(distance, () =>
            {
                if (gen != _generation) return;
                try { M2Factory.PrepareStatic(files, path); }
                catch (Exception e) { WowLog.Warn("Map: M2Factory.PrepareStatic " + path + " threw " + e.GetType().Name + ": " + e.Message); }
            });
        }

        // integrator: doodad model paths whose M2Factory.PrepareStatic is queued on the worker (Models' request)
        readonly HashSet<string> _m2PrepQueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------------------------------------------ doodads

        void AddDoodad(TileRecord t, DoodadPlacement p)
        {
            DoodadRecord d;
            if (!_doodads.TryGetValue(p.UniqueId, out d))
            {
                d = new DoodadRecord { P = p };
                _doodads[p.UniqueId] = d;
                _doodadList.Add(d);
                float dist = (p.Position - _focus).magnitude;
                if (dist <= _doodadMaxRadius + 100f) QueuePrepareDoodad(p.Path, dist);
            }
            d.Refs++;
            t.DoodadIds.Add(p.UniqueId);
        }

        void ReleaseDoodad(uint id)
        {
            DoodadRecord d;
            if (!_doodads.TryGetValue(id, out d)) return;
            if (--d.Refs > 0) return;
            if (d.Go != null) { Destroy(d.Go); _shownDoodads--; }
            d.Go = null;
            d.Removed = true;
            _doodads.Remove(id);
        }

        void StepDoodads(double budget)
        {
            int n = _doodadList.Count;
            if (n == 0) return;
            // compact removed records occasionally
            if (_doodadCursor == 0)
            {
                int w = 0;
                for (int i = 0; i < n; i++) if (!_doodadList[i].Removed) _doodadList[w++] = _doodadList[i];
                if (w < n) { _doodadList.RemoveRange(w, n - w); n = w; }
                if (n == 0) return;
            }
            bool built = false;
            // Pass A: whatever is close to the focus first (the round robin below would fill the surroundings in random order;
            // in the lab only 52 of the ~9000 known doodads were up when the first load finished).
            const float Near = 60f;
            for (int i = 0; i < n; i++)
            {
                var d = _doodadList[i];
                if (d.Removed || d.Go != null) continue;
                if ((d.P.Position - _focus).sqrMagnitude > Near * Near) continue;
                if (_failedModels.Contains(d.P.Path)) continue;
                if (built && _frame.Elapsed.TotalMilliseconds > budget) break;
                Instantiate(d);
                built = true;
            }
            int scan = Math.Min(n, 2000);
            for (int k = 0; k < scan; k++)
            {
                if (_doodadCursor >= n) _doodadCursor = 0;
                var d = _doodadList[_doodadCursor++];
                if (d.Removed) continue;
                float radius;
                if (!_modelRadius.TryGetValue(d.P.Path, out radius)) radius = 5f;
                float cull = Math.Min(_doodadMaxRadius, 40f + 25f * radius * d.P.Scale);
                float dist2 = (d.P.Position - _focus).sqrMagnitude;
                if (d.Go == null)
                {
                    if (dist2 > cull * cull) continue;
                    if (_failedModels.Contains(d.P.Path)) continue;
                    if (built && _frame.Elapsed.TotalMilliseconds > budget) continue;
                    Instantiate(d);
                    built = true;
                }
                else if (dist2 > cull * cull * 1.3225f) // 1.15 x
                {
                    Destroy(d.Go);
                    d.Go = null;
                    _shownDoodads--;
                }
            }
        }

        void Instantiate(DoodadRecord d)
        {
            GameObject go = null;
            try { go = M2Factory.BuildStatic(_files, d.P.Path); }
            catch (Exception e) { WowLog.Warn("Map: M2Factory.BuildStatic " + d.P.Path + " threw " + e.GetType().Name + ": " + e.Message); }
            if (go == null) { _failedModels.Add(d.P.Path); return; }
            var tr = go.transform;
            tr.SetParent(_doodadRoot.transform, false);
            tr.position = d.P.Position;
            tr.rotation = d.P.Rotation;
            tr.localScale = new Vector3(d.P.Scale, d.P.Scale, d.P.Scale);
            d.Go = go;
            _shownDoodads++;
            if (!_modelRadius.ContainsKey(d.P.Path))
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                float r = 1f;
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                    r = b.extents.magnitude / Math.Max(0.01f, d.P.Scale);
                }
                _modelRadius[d.P.Path] = r;
            }
        }

        // ------------------------------------------------------------------------------------------------ progress

        void UpdateProgress()
        {
            if (_firstLoadDone) { Progress01 = 1f; return; }
            if (IsReadyAround(_focus, _firstLoadRadius))
            {
                _firstLoadDone = true;
                Progress01 = 1f;
                LogStats("Map: first load around the focus done in " + (Time.realtimeSinceStartup - _loadStartTime).ToString("0.0") + " s");
                try { WowLog.Info(WowTextures.StatsLine()); } catch (Exception) { }
                return;
            }
            float wx, wy, wz; WowSpace.ToWow(MapId, _focus, out wx, out wy, out wz);
            int need = 0, done = 0;
            foreach (var t in _tiles.Values)
            {
                if (t.Distance > _firstLoadRadius) continue;
                need += 3;
                if (t.HavePlacements || t.HaveFull) done++;
                if (t.HaveFull) done++;
                if (t.ColliderReady || t.Missing) done++;
            }
            foreach (var w in _wmoList)
            {
                if (Mathf.Sqrt(w.P.Bounds.SqrDistance(_focus)) > _firstLoadRadius) continue;
                need += 2;
                if (w.State != 0) done += 2;
            }
            float p = need == 0 ? 0.05f : Mathf.Clamp01((float)done / need) * 0.98f;
            Progress01 = Math.Max(Progress01, p);
        }

        void LogStats(string what)
        {
            int terrain = 0, placements = 0;
            foreach (var t in _tiles.Values) { if (t.HaveFull) terrain++; else if (t.HavePlacements) placements++; }
            WowLog.Info(what + ": " + terrain + " terrain tiles, " + placements + " placement-only tiles, " + _shownDoodads + "/" + _doodads.Count + " doodads shown, "
                + _builtWmos + "/" + _wmos.Count + " WMOs built, texture slots " + (_textures != null ? _textures.SlotsInUse + "/" + _textures.Capacity + " (" + _textures.Uploaded + " uploaded, " + _textures.WithSpecular + " with _s.blp specular, prepare " + _textures.PrepareMs + " ms)" : "-")
                + ", worker queue " + (_worker != null ? _worker.Pending : 0));
        }

        /// <summary>For FarTerrain: which tiles currently show full terrain.</summary>
        internal bool HasTerrain(int tx, int ty)
        {
            TileRecord t;
            return _tiles.TryGetValue(Key(tx, ty), out t) && t.HaveFull;
        }

        internal Transform Root { get { return _root != null ? _root.transform : null; } }
        internal Shader TerrainShader { get { return _terrainShader; } }
        internal int Generation { get { return _generation; } }
    }

    /// <summary>Water materials shared by terrain (MH2O) and buildings (MLIQ).</summary>
    public static class WowWater
    {
        static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        static readonly Dictionary<string, Texture2DArray> Frames = new Dictionary<string, Texture2DArray>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<int, int> Kinds = new Dictionary<int, int>();

        // Defaults MEASURED (probe TerrainWaterLight): Eastern Kingdoms global light (Light.dbc row with coords 0 / radius 0),
        // clear-weather params, LightIntBand at 12:00: river light 4F5D14, river dark 335255, ocean light 114B59, ocean dark
        // 001D29 (bands 16, 17, 14, 15); LightParams alphas: water shallow 0.65 / deep 0.5, ocean 1 / 0.75. Front replaces them
        // with the zone's own light for the time of day through SetColors (colour = band, alpha = the LightParams opacity).
        static Color _riverLight = new Color32(0x4F, 0x5D, 0x14, 166), _riverDark = new Color32(0x33, 0x52, 0x55, 128);
        static Color _oceanLight = new Color32(0x11, 0x4B, 0x59, 255), _oceanDark = new Color32(0x00, 0x1D, 0x29, 191);

        static readonly int FramesId = Shader.PropertyToID("_Frames");
        static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
        static readonly int KindId = Shader.PropertyToID("_Kind");
        static readonly int LightId = Shader.PropertyToID("_ColorLight");
        static readonly int DarkId = Shader.PropertyToID("_ColorDark");

        /// <summary>A material for a LiquidType.dbc id (water, ocean, magma, slime), cached. Main thread.</summary>
        public static Material ForLiquidType(WowFiles files, int liquidTypeId)
        {
            Material m;
            if (Cache.TryGetValue(liquidTypeId, out m) && m != null) return m;
            int kind = 0; string texPattern = null; string name = "unknown";
            var t = files != null ? Dbc.Load(files, "LiquidType") : null;
            int r = t != null ? t.FindRow(liquidTypeId) : -1;
            if (r >= 0)
            {
                kind = t.GetInt(r, DbcFields.LiquidType.SoundBank); // 3.3.5a: 0 water, 1 ocean, 2 magma, 3 slime (measured on the 26 rows)
                texPattern = t.GetString(r, DbcFields.LiquidType.Texture);
                name = t.GetString(r, DbcFields.LiquidType.Name);
            }
            else if (liquidTypeId >= 1 && liquidTypeId <= 4) kind = liquidTypeId - 1;
            if (kind < 0 || kind > 3) kind = 0;
            Kinds[liquidTypeId] = kind;
            var shader = WowShaders.Find("SomniumWoW/Water");
            if (shader == null)
            {
                WowLog.Error("WowWater: shader SomniumWoW/Water missing (carrier WowWater.mat not carried?) - liquids use URP Unlit");
                shader = WowShaders.Find("Universal Render Pipeline/Unlit");
            }
            m = new Material(shader);
            m.name = "WoW liquid " + liquidTypeId + " " + name;
            m.enableInstancing = true;
            int frames = 0;
            var arr = LoadFrames(files, texPattern, out frames);
            if (arr != null) { m.SetTexture(FramesId, arr); m.SetFloat(FrameCountId, frames); }
            m.SetFloat(KindId, kind);
            Apply(m, kind);
            Cache[liquidTypeId] = m;
            WowLog.Info("WowWater: liquid " + liquidTypeId + " '" + name + "' kind " + (kind == 0 ? "water" : kind == 1 ? "ocean" : kind == 2 ? "magma" : "slime")
                + ", " + frames + " frames from " + (texPattern ?? "(none)"));
            return m;
        }

        /// <summary>Liquid kind of a LiquidType id seen so far (0 water, 1 ocean, 2 magma, 3 slime).</summary>
        public static int KindOf(int liquidTypeId)
        {
            int k;
            return Kinds.TryGetValue(liquidTypeId, out k) ? k : 0;
        }

        /// <summary>Zone colours (e.g. from Light.dbc LightIntBand river/ocean close/far, alpha = shallow/deep opacity).</summary>
        public static void SetColors(Color riverLight, Color riverDark, Color oceanLight, Color oceanDark)
        {
            _riverLight = riverLight; _riverDark = riverDark; _oceanLight = oceanLight; _oceanDark = oceanDark;
            foreach (var kv in Cache) if (kv.Value != null) Apply(kv.Value, KindOf(kv.Key));
        }

        static void Apply(Material m, int kind)
        {
            if (kind == 1) { m.SetColor(LightId, _oceanLight); m.SetColor(DarkId, _oceanDark); }
            else if (kind == 0) { m.SetColor(LightId, _riverLight); m.SetColor(DarkId, _riverDark); }
            else { m.SetColor(LightId, Color.black); m.SetColor(DarkId, Color.black); }
        }

        sealed class DecodedFrames { public int W, H; public List<byte[]> Rgba; }
        static readonly Dictionary<string, DecodedFrames> Decoded = new Dictionary<string, DecodedFrames>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> UploadedFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // under lock (Decoded)

        /// <summary>
        /// Any thread (the map worker calls it for the liquids of each tile it prepares): reads + decodes a liquid type's
        /// animation frames so ForLiquidType on the main thread only uploads them (decoding 30 frames took 56-71 ms in the lab).
        /// </summary>
        public static void Prewarm(WowFiles files, int liquidTypeId)
        {
            if (files == null) return;
            var t = Dbc.Load(files, "LiquidType");
            int r = t != null ? t.FindRow(liquidTypeId) : -1;
            if (r < 0) return;
            string pattern = t.GetString(r, DbcFields.LiquidType.Texture);
            if (string.IsNullOrEmpty(pattern)) return;
            lock (Decoded) { if (Decoded.ContainsKey(pattern) || UploadedFrames.Contains(pattern)) return; }
            var d = DecodeFrames(files, pattern);
            lock (Decoded) { if (!Decoded.ContainsKey(pattern)) Decoded[pattern] = d; }
        }

        static DecodedFrames DecodeFrames(WowFiles files, string pattern)
        {
            var d = new DecodedFrames { Rgba = new List<byte[]>() };
            for (int i = 1; i <= 64; i++)
            {
                string p = pattern.Contains("%d") ? pattern.Replace("%d", i.ToString()) : pattern;
                var bytes = files.Read(p);
                if (bytes == null) break;
                try
                {
                    var img = BlpImage.Decode(bytes);
                    int mip = 0;
                    while (img.MipWidth(mip) > 128 && mip + 1 < img.MipCount) mip++;
                    int mw = img.MipWidth(mip), mh = img.MipHeight(mip);
                    if (d.Rgba.Count == 0) { d.W = mw; d.H = mh; }
                    if (mw != d.W || mh != d.H) break;
                    d.Rgba.Add(img.GetRgba32(mip));
                }
                catch (Exception) { break; }
                if (!pattern.Contains("%d")) break;
            }
            return d;
        }

        /// <summary>The animation frames "...%d.blp" (1, 2, ... until one is missing) as a Texture2DArray (at most 128 px).</summary>
        static Texture2DArray LoadFrames(WowFiles files, string pattern, out int count)
        {
            count = 0;
            if (files == null || string.IsNullOrEmpty(pattern)) return null;
            Texture2DArray arr;
            if (Frames.TryGetValue(pattern, out arr) && arr != null) { count = arr.depth; return arr; }
            DecodedFrames d;
            lock (Decoded) { Decoded.TryGetValue(pattern, out d); }
            if (d == null) d = DecodeFrames(files, pattern);
            if (d.Rgba.Count == 0) { WowLog.Warn("WowWater: no frames for " + pattern); return null; }
            arr = new Texture2DArray(d.W, d.H, d.Rgba.Count, TextureFormat.RGBA32, true, false);
            arr.name = "WoW liquid frames " + pattern;
            arr.wrapMode = TextureWrapMode.Repeat;
            arr.filterMode = FilterMode.Trilinear;
            for (int i = 0; i < d.Rgba.Count; i++) arr.SetPixelData(d.Rgba[i], 0, i);
            arr.Apply(true, true);
            Frames[pattern] = arr;
            lock (Decoded) { Decoded.Remove(pattern); UploadedFrames.Add(pattern); }
            count = d.Rgba.Count;
            return arr;
        }
    }
}
