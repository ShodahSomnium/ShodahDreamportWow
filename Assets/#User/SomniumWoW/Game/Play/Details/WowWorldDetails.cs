using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Details;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// World life around the player (owner: Details, iteration 4): WoW's ground clutter (grass, flowers, pebbles from
    /// GroundEffectTexture / GroundEffectDoodad, GPU-instanced, see ClutterField) and flying birds (visual only, see
    /// AmbientLife; the ground critters are session entities since iteration 5, OfflineSession.Critters). The front end adds this component to the WoW Game object. It reads the ADT tiles
    /// itself (DetailTerrain, on its own worker thread, LRU 9 tiles), follows WowSettings.GroundClutter / ClutterDensity /
    /// AmbientLife / EffectsQuality live, and shifts its Unity-space state on WowSpace.Rebased. Nothing here is networked:
    /// every player sees their own grass and birds.
    /// </summary>
    public sealed class WowWorldDetails : MonoBehaviour
    {
        /// <summary>
        /// Tools only (the render lab's DetailsLab): drive the details at a fixed WoW spot without a session. LabMap &lt; 0 = off.
        /// </summary>
        public static WowFiles LabFiles;
        public static int LabMap = -1;
        public static Vector3 LabWow;
        public static float LabTimeOfDay = 12f;

        public static WowWorldDetails Instance { get; private set; }

        private DetailsWorker _worker;
        private WowFiles _files;
        private DetailTerrain _terrain;
        private ClutterField _clutter;
        private AmbientLife _life;
        private Transform _lifeRoot;
        private readonly HashSet<int> _tileRequests = new HashSet<int>();
        private bool _inWorld, _ready;
        private int _map = -1, _tileLogs, _throws;
        private float _px, _py, _pz, _nextStats;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            WowSpace.Rebased += OnRebased;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            WowSpace.Rebased -= OnRebased;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Leave();
        }

        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this) Instance = null;
        }

        internal ClutterField Clutter { get { return _clutter; } }
        internal AmbientLife Life { get { return _life; } }
        internal bool Ready { get { return _ready; } }

        /// <summary>One line for logs and the settings/status UI.</summary>
        public string StatusLine
        {
            get
            {
                if (_clutter == null) return "details: no game files";
                return "details: " + _clutter.StatusLine + "; " + _life.StatusLine + "; tiles " + _terrain.TilesLoaded + ", worker queue " + _worker.Pending;
            }
        }

        // ------------------------------------------------------------------------------------------ per frame

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { LogThrow("update", e); }
        }

        // The clutter is submitted per camera right before it renders (after Update/LateUpdate, after a rebase of this frame, and
        // also for cameras rendered by hand), restricted to that camera so no camera draws it twice.
        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_inWorld || _clutter == null || !WowSettings.GroundClutter || cam == null) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (WowTabletMap.IsMapCamera(cam)) return;   // iteration 5 (Map owner): no grass in the overhead map picture
            try { _clutter.Draw(WowSpace.ToUnity(_map, _px, _py, _pz), cam); }
            catch (Exception e) { LogThrow("clutter draw", e); }
        }

        private void Tick()
        {
            WowFiles files;
            bool inWorld, onTaxi = false;
            int map = -1;
            float x = 0f, y = 0f, z = 0f, tod = 12f;
            if (LabMap >= 0 && LabFiles != null)
            {
                files = LabFiles; inWorld = true; map = LabMap;
                x = LabWow.x; y = LabWow.y; z = LabWow.z; tod = LabTimeOfDay;
            }
            else
            {
                var game = WowGame.Current;
                var s = WowPlay.Session;
                files = game != null ? game.Files : null;
                if (files == null) files = WowPlay.Files;
                inWorld = s != null && s.Player != null && game != null && game.Map != null;
                if (inWorld)
                {
                    map = s.MapId;
                    x = s.Player.Pos.X; y = s.Player.Pos.Y; z = s.Player.Pos.Z;
                    onTaxi = s.Player.OnTaxi;
                    tod = game.TimeOfDay;
                }
            }
            if (!ReferenceEquals(files, _files)) OnFiles(files);
            if (_worker == null) return;
            _worker.Pump(2f);
            if (!inWorld || !_ready)
            {
                if (_inWorld) Leave();
                return;
            }
            if (!_inWorld) WowLog.Info("Details: in the world (map " + map + ") - clutter " + (WowSettings.GroundClutter ? "on" : "off") + ", life " + (WowSettings.AmbientLife ? "on" : "off"));
            _inWorld = true;
            _map = map;
            _px = x; _py = y; _pz = z;
            if (_lifeRoot != null && _lifeRoot.position.sqrMagnitude > 1e-6f) _lifeRoot.position = Vector3.zero;   // shifted as an empty leaf by a rebase
            float now = Time.unscaledTime;

            if (WowSettings.GroundClutter) _clutter.Update(map, x, y, now, RequestTile);
            else if (_clutter.ChunkCount > 0) _clutter.Clear(false);

            if (WowSettings.AmbientLife)
            {
                EnsureTiles(map, x, y, 64f);
                _life.Update(map, x, y, z, tod, onTaxi, Mathf.Min(Time.deltaTime, 0.1f), now);
            }
            else if (_life.BirdCount > 0) _life.Clear();

            if (now >= _nextStats)
            {
                bool first = _nextStats == 0f;
                _nextStats = now + (first ? 20f : 120f);
                if (!first) WowLog.Info("Details: " + StatusLine);
            }
        }

        private void EnsureTiles(int map, float x, float y, float r)
        {
            for (int i = 0; i < 4; i++)
            {
                int tx, ty;
                World.MapConst.WorldToTile(x + ((i & 1) == 0 ? r : -r), y + ((i & 2) == 0 ? r : -r), out tx, out ty);
                if (tx < 0 || ty < 0 || tx > 63 || ty > 63) continue;
                if (_terrain.Get(map, tx, ty) == null) RequestTile(tx, ty);
            }
        }

        private void RequestTile(int tx, int ty)
        {
            int map = _map;
            int key = map * 4096 + tx * 64 + ty;
            if (!_tileRequests.Add(key)) return;
            var terrain = _terrain;
            _worker.Enqueue(() =>
            {
                var sw = Stopwatch.StartNew();
                string line = terrain.LoadTile(map, tx, ty);
                long ms = sw.ElapsedMilliseconds;
                _worker.Post(() =>
                {
                    _tileRequests.Remove(key);
                    if (line != null && _tileLogs++ < 12) WowLog.Info("Details: tile " + line + ", " + ms + " ms");
                });
            });
        }

        private void OnFiles(WowFiles files)
        {
            Shutdown();
            _files = files;
            if (files == null) return;
            _worker = new DetailsWorker();
            _worker.Start();
            _terrain = new DetailTerrain(files);
            _clutter = new ClutterField(_worker, _terrain, files);
            if (_lifeRoot == null)
            {
                _lifeRoot = new GameObject("Details life").transform;
                _lifeRoot.SetParent(transform, false);
                _lifeRoot.position = Vector3.zero;
            }
            _life = new AmbientLife(files, _terrain, _worker, _lifeRoot);
            var clutter = _clutter;
            var life = _life;
            _worker.Enqueue(() =>
            {
                var sw = Stopwatch.StartNew();
                var db = ClutterDb.Load(files);
                var areas = Dbc.Load(files, "AreaTable");
                long ms = sw.ElapsedMilliseconds;
                _worker.Post(() =>
                {
                    if (!ReferenceEquals(clutter, _clutter)) return;
                    clutter.Db = db;
                    life.Areas = areas;
                    _ready = true;
                    WowLog.Info("Details: ground effects " + db.EffectCount + " (of " + db.EffectRows + " GroundEffectTexture rows), " + db.Paths.Count + " detail models (of "
                        + db.DoodadRows + " GroundEffectDoodad rows; " + db.MissingModels + " not in the files, e.g. " + (db.FirstMissing ?? "-") + "), AreaTable "
                        + (areas != null ? areas.RecordCount : 0) + " rows, " + ms + " ms");
                });
            });
        }

        private void Leave()
        {
            if (_clutter != null) _clutter.Clear(true);
            if (_life != null) _life.Clear();
            _tileRequests.Clear();
            if (_inWorld) WowLog.Info("Details: left the world - clutter and life cleared");
            _inWorld = false;
            _map = -1;
        }

        private void Shutdown()
        {
            Leave();
            if (_worker != null) _worker.Stop();
            _worker = null;
            _clutter = null;
            _life = null;
            _terrain = null;
            _ready = false;
            _files = null;
        }

        private void OnRebased(Vector3 delta)
        {
            // Unity-space state only: the clutter matrices. Critters and birds are placed from WoW coordinates every frame and
            // their GameObjects sit under a container child of the WoW Game object, which WowOrigin shifts itself.
            if (_clutter != null) _clutter.Rebase(delta);
        }

        private void LogThrow(string what, Exception e)
        {
            if (_throws++ < 10) WowLog.Error("Details: " + what + " threw: " + e);
        }
    }
}
