using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Details
{
    /// <summary>
    /// WoW's ground clutter around the player: chunks (1/256 ADT tile) within the clutter radius are placed on the details
    /// worker (ClutterPlacer: low-quality texture map -> MCLY effect -> GroundEffectTexture -> GroundEffectDoodad models), their
    /// instance matrices built in the WowSpace frame of the moment; the main thread merges them per detail model and draws them
    /// with Graphics.RenderMeshInstanced (SomniumWoW/Clutter: alpha test, wind sway, distance shrink) - no GameObjects, no
    /// shadows cast. Unity positions are shifted on WowSpace.Rebased. Settings: GroundClutter, ClutterDensity, EffectsQuality
    /// (radius 45 / 65 / 85 m).
    /// </summary>
    internal sealed class ClutterField
    {
        public const string ShaderName = "SomniumWoW/Clutter";
        private const int MaxBatch = 1023;
        private const int MaxBuildsInFlight = 24;

        private sealed class Chunk
        {
            public int Key, TileX, TileY, Ci;
            public float CX, CY;
            public bool Ready;
            public int[] Slots;
            public Matrix4x4[][] Mats;
            public int Count;
            public Bounds Bounds;
        }

        private sealed class SlotAsset
        {
            public int Slot;
            public bool Built, Failed;
            public Mesh Mesh;
            public Material[] Materials;
            public Texture[] Textures;
            public Matrix4x4[] Merged = new Matrix4x4[0];
            public int Count;
            public Bounds Bounds;
        }

        private readonly DetailsWorker _worker;
        private readonly DetailTerrain _terrain;
        private readonly WowFiles _files;
        private readonly Dictionary<int, Chunk> _chunks = new Dictionary<int, Chunk>();
        private readonly Dictionary<int, SlotAsset> _assets = new Dictionary<int, SlotAsset>();
        private readonly List<SlotAsset> _assetList = new List<SlotAsset>();
        private readonly Queue<SlotAsset> _assetQueue = new Queue<SlotAsset>();
        private readonly Dictionary<int, Material> _materials = new Dictionary<int, Material>();
        private readonly HashSet<int> _prepared = new HashSet<int>();   // worker side (lock)
        private readonly List<int> _drop = new List<int>();
        private ClutterDb _db;
        private Shader _shader;
        private bool _shaderMissingLogged, _firstLogged;
        private bool _dirty;
        private int _generation, _map = -1, _inFlight;
        private float _density = -1f, _nextScan, _lastX = float.MaxValue, _lastY = float.MaxValue;
        private Vector4 _center, _fade;
        public int Instances, ReadyChunks, Draws, Built, Failed;

        private static readonly int CenterId = Shader.PropertyToID("_WowClutterCenter");
        private static readonly int FadeId = Shader.PropertyToID("_WowClutterFade");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        public ClutterField(DetailsWorker worker, DetailTerrain terrain, WowFiles files)
        {
            _worker = worker; _terrain = terrain; _files = files;
        }

        public ClutterDb Db { get { return _db; } set { _db = value; } }

        public int ChunkCount { get { return _chunks.Count; } }

        /// <summary>Clutter radius (metres) by WowSettings.EffectsQuality.</summary>
        public static float RadiusMetres
        {
            get
            {
                int q = Mathf.Clamp(WowSettings.EffectsQuality, 0, 2);
                return q == 0 ? 45f : q == 1 ? 65f : 85f;
            }
        }

        /// <summary>Drops every chunk (and, when full, the model assets and materials too).</summary>
        public void Clear(bool full)
        {
            _generation++;
            _chunks.Clear();
            _inFlight = 0;
            _dirty = true;
            _lastX = _lastY = float.MaxValue;
            foreach (var a in _assetList) a.Count = 0;
            if (full)
            {
                _assets.Clear();
                _assetList.Clear();
                _assetQueue.Clear();
                foreach (var m in _materials.Values) if (m != null) UnityEngine.Object.Destroy(m);
                _materials.Clear();
                _map = -1;
            }
            Instances = 0; ReadyChunks = 0;
        }

        /// <summary>Main thread, every frame: chooses chunks around the player (WoW x, y), uploads finished ones, builds models.</summary>
        public void Update(int map, float px, float py, float now, Action<int, int> requestTile)
        {
            if (_db == null) return;
            float density = Mathf.Clamp(WowSettings.ClutterDensity, 0.25f, 1.5f);
            if (map != _map || Math.Abs(density - _density) > 0.001f)
            {
                if (_chunks.Count > 0) WowLog.Info("Details: clutter rebuilt (" + (map != _map ? "map " + map : "density " + density.ToString("0.00")) + ")");
                Clear(map != _map && _map >= 0);
                _map = map;
                _density = density;
            }
            float dx = px - _lastX, dy = py - _lastY;
            if (now >= _nextScan || dx * dx + dy * dy > 16f)
            {
                _nextScan = now + 0.25f;
                _lastX = px; _lastY = py;
                Scan(map, px, py, requestTile);
            }
            // at most two model uploads a frame
            for (int n = 0; n < 2 && _assetQueue.Count > 0; n++) BuildAsset(_assetQueue.Dequeue());
            if (_dirty) Merge();   // stats right even while no camera renders (Draw merges again after a rebase)
        }

        private void Scan(int map, float px, float py, Action<int, int> requestTile)
        {
            float radiusYd = RadiusMetres / WowSpace.MetresPerYard;
            float want = radiusYd + MapConst.ChunkSize * 0.71f, drop = want + 12f;
            _drop.Clear();
            foreach (var kv in _chunks)
            {
                var c = kv.Value;
                float ddx = c.CX - px, ddy = c.CY - py;
                if (ddx * ddx + ddy * ddy > drop * drop) _drop.Add(kv.Key);
            }
            foreach (int k in _drop)
            {
                Chunk c = _chunks[k];
                if (c.Ready) _dirty = true;
                _chunks.Remove(k);
            }
            int tx0, ty0, tx1, ty1;
            MapConst.WorldToTile(px + want, py + want, out tx0, out ty0);
            MapConst.WorldToTile(px - want, py - want, out tx1, out ty1);
            int minTx = Math.Min(tx0, tx1), maxTx = Math.Max(tx0, tx1), minTy = Math.Min(ty0, ty1), maxTy = Math.Max(ty0, ty1);
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (tx < 0 || ty < 0 || tx > 63 || ty > 63) continue;
                    var t = _terrain.Get(map, tx, ty);
                    if (t == null) { requestTile(tx, ty); continue; }
                    if (t.Adt == null) continue;
                    for (int ci = 0; ci < 256; ci++)
                    {
                        var ch = t.Adt.Chunks[ci];
                        if (ch == null) continue;
                        float cx = ch.X - MapConst.ChunkSize * 0.5f, cy = ch.Y - MapConst.ChunkSize * 0.5f;
                        float ddx = cx - px, ddy = cy - py;
                        if (ddx * ddx + ddy * ddy > want * want) continue;
                        int key = (tx << 14) | (ty << 8) | ci;
                        if (_chunks.ContainsKey(key)) continue;
                        if (_inFlight >= MaxBuildsInFlight) return;
                        var c = new Chunk { Key = key, TileX = tx, TileY = ty, Ci = ci, CX = cx, CY = cy };
                        _chunks[key] = c;
                        Enqueue(c, map);
                    }
                }
            }
        }

        private void Enqueue(Chunk c, int map)
        {
            int gen = _generation;
            float density = _density;
            var db = _db;
            _inFlight++;
            _worker.Enqueue(() =>
            {
                var t = _terrain.Get(map, c.TileX, c.TileY);
                var list = new List<ClutterInstance>(256);
                if (t != null && t.Adt != null) ClutterPlacer.PlaceChunk(_terrain, t, c.Ci, db, density, list);
                var frame = WowSpace.Current;
                // group by model
                var bySlot = new Dictionary<int, List<Matrix4x4>>();
                foreach (var i in list)
                {
                    List<Matrix4x4> l;
                    if (!bySlot.TryGetValue(i.Slot, out l)) { l = new List<Matrix4x4>(); bySlot[i.Slot] = l; }
                    l.Add(Trs(frame.ToUnity(i.X, i.Y, i.Z), WowSpace.OrientationToYaw(i.Yaw) * Mathf.Deg2Rad, i.Scale));
                }
                var slots = new int[bySlot.Count];
                var mats = new Matrix4x4[bySlot.Count][];
                int g = 0;
                foreach (var kv in bySlot)
                {
                    slots[g] = kv.Key;
                    mats[g] = kv.Value.ToArray();
                    g++;
                    bool prep;
                    lock (_prepared) prep = _prepared.Add(kv.Key);
                    if (prep) M2Factory.PrepareStatic(_files, db.Paths[kv.Key]);
                }
                _worker.Post(() => OnBuilt(c, gen, frame, slots, mats, list.Count));
            });
        }

        // Unity: yaw about +y (clockwise from above), uniform scale, translation. Plain arithmetic (worker-safe).
        private static Matrix4x4 Trs(Vector3 p, float yawRad, float s)
        {
            float cs = (float)Math.Cos(yawRad) * s, sn = (float)Math.Sin(yawRad) * s;
            var m = new Matrix4x4();
            m.m00 = cs; m.m01 = 0f; m.m02 = sn; m.m03 = p.x;
            m.m10 = 0f; m.m11 = s; m.m12 = 0f; m.m13 = p.y;
            m.m20 = -sn; m.m21 = 0f; m.m22 = cs; m.m23 = p.z;
            m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
            return m;
        }

        private void OnBuilt(Chunk c, int gen, WowSpace.Frame frame, int[] slots, Matrix4x4[][] mats, int count)
        {
            if (gen != _generation) return;
            _inFlight = Math.Max(0, _inFlight - 1);
            Chunk now;
            if (!_chunks.TryGetValue(c.Key, out now) || !ReferenceEquals(now, c)) return;
            Vector3 off = frame.Offset;
            bool shift = off != Vector3.zero;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int g = 0; g < mats.Length; g++)
            {
                var a = mats[g];
                for (int i = 0; i < a.Length; i++)
                {
                    if (shift) { a[i].m03 += off.x; a[i].m13 += off.y; a[i].m23 += off.z; }
                    var p = new Vector3(a[i].m03, a[i].m13, a[i].m23);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                Asset(slots[g]);
            }
            c.Slots = slots; c.Mats = mats; c.Count = count; c.Ready = true;
            c.Bounds = count > 0 ? new Bounds((min + max) * 0.5f, max - min + new Vector3(3f, 3f, 3f)) : new Bounds();
            _dirty = true;
            if (!_firstLogged && count > 0)
            {
                _firstLogged = true;
                WowLog.Info("Details: first clutter chunk " + c.TileX + "_" + c.TileY + " #" + c.Ci + ": " + count + " doodads, " + slots.Length + " models (e.g. " + _db.Paths[slots[0]] + ")");
            }
        }

        private SlotAsset Asset(int slot)
        {
            SlotAsset a;
            if (_assets.TryGetValue(slot, out a)) return a;
            a = new SlotAsset { Slot = slot };
            _assets[slot] = a;
            _assetList.Add(a);
            _assetQueue.Enqueue(a);
            return a;
        }

        private void BuildAsset(SlotAsset a)
        {
            if (a.Built) return;
            a.Built = true;
            if (_shader == null) _shader = WowShaders.Find(ShaderName);
            if (_shader == null)
            {
                a.Failed = true;
                if (!_shaderMissingLogged) { _shaderMissingLogged = true; WowLog.Warn("Details: shader " + ShaderName + " not found (carrier Resources/WowClutter.mat not in WowGame._shaderCarriers?) - no ground clutter"); }
                return;
            }
            string path = _db.Paths[a.Slot];
            GameObject go = null;
            try { go = M2Factory.BuildStatic(_files, path); }
            catch (Exception e) { WowLog.Warn("Details: " + path + " threw " + e.Message); }
            if (go == null) { a.Failed = true; Failed++; return; }
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mf != null && mr != null && mf.sharedMesh != null)
            {
                a.Mesh = mf.sharedMesh;
                var src = mr.sharedMaterials;
                int n = Math.Min(src.Length, a.Mesh.subMeshCount);
                a.Materials = new Material[n];
                a.Textures = new Texture[n];
                for (int i = 0; i < n; i++)
                {
                    Texture tex = src[i] != null && src[i].HasProperty(MainTexId) ? src[i].GetTexture(MainTexId) : null;
                    float cutoff = src[i] != null && src[i].HasProperty(CutoffId) ? src[i].GetFloat(CutoffId) : M2Materials.AlphaKeyRef;
                    a.Textures[i] = tex;
                    a.Materials[i] = MaterialFor(tex, cutoff);
                }
                Built++;
            }
            else { a.Failed = true; Failed++; }
            UnityEngine.Object.Destroy(go);
        }

        // One clutter material per texture (the zone atlases). AlphaKey batches keep their 224/255 test; blended or opaque
        // batches (not seen in the measured detail models) get 0.5.
        private Material MaterialFor(Texture tex, float cutoff)
        {
            if (cutoff < 0.3f) cutoff = 0.5f;
            int key = (tex != null ? tex.GetInstanceID() : 0) * 31 + Mathf.RoundToInt(cutoff * 255f);
            Material m;
            if (_materials.TryGetValue(key, out m) && m != null) return m;
            m = new Material(_shader) { name = "Clutter " + (tex != null ? tex.name : "untextured"), enableInstancing = true };
            m.SetTexture(MainTexId, tex);
            m.SetFloat(CutoffId, cutoff);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            _materials[key] = m;
            return m;
        }

        /// <summary>WowSpace.Rebased: every kept Unity position moves by -delta.</summary>
        public void Rebase(Vector3 delta)
        {
            foreach (var c in _chunks.Values)
            {
                if (!c.Ready) continue;
                for (int g = 0; g < c.Mats.Length; g++)
                {
                    var a = c.Mats[g];
                    for (int i = 0; i < a.Length; i++) { a[i].m03 -= delta.x; a[i].m13 -= delta.y; a[i].m23 -= delta.z; }
                }
                c.Bounds = new Bounds(c.Bounds.center - delta, c.Bounds.size);
            }
            _dirty = true;
        }

        /// <summary>Main thread, late in the frame: merges changed chunks and draws everything (player at centre, Unity).</summary>
        public void Draw(Vector3 centre, Camera cam)
        {
            if (_dirty) Merge();
            Draws = 0;
            if (Instances == 0) return;
            float r = RadiusMetres;
            _center = new Vector4(centre.x, centre.y, centre.z, 0f);
            _fade = new Vector4(r - 15f, r, 0f, 0f);
            Shader.SetGlobalVector(CenterId, _center);
            Shader.SetGlobalVector(FadeId, _fade);
            for (int k = 0; k < _assetList.Count; k++)
            {
                var a = _assetList[k];
                if (a.Count == 0 || a.Failed || !a.Built) continue;
                if (a.Mesh == null || TextureLost(a)) { RebuildAll(); return; }   // destroyed with the M2 caches
                for (int s = 0; s < a.Materials.Length; s++)
                {
                    var rp = new RenderParams(a.Materials[s])
                    {
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        worldBounds = a.Bounds,
                        layer = 0,
                        camera = cam,
                        lightProbeUsage = LightProbeUsage.BlendProbes,
                    };
                    for (int start = 0; start < a.Count; start += MaxBatch)
                    {
                        Graphics.RenderMeshInstanced(rp, a.Mesh, s, a.Merged, Math.Min(MaxBatch, a.Count - start), start);
                        Draws++;
                    }
                }
            }
        }

        private static bool TextureLost(SlotAsset a)
        {
            for (int i = 0; i < a.Textures.Length; i++)
                if (!ReferenceEquals(a.Textures[i], null) && a.Textures[i] == null) return true;
            return false;
        }

        private void RebuildAll()
        {
            WowLog.Info("Details: clutter models were destroyed (M2 caches cleared) - rebuilding " + _assetList.Count);
            lock (_prepared) _prepared.Clear();
            foreach (var m in _materials.Values) if (m != null) UnityEngine.Object.Destroy(m);
            _materials.Clear();
            _assetQueue.Clear();
            foreach (var a in _assetList)
            {
                a.Built = false; a.Failed = false; a.Mesh = null;
                _assetQueue.Enqueue(a);
            }
        }

        private void Merge()
        {
            _dirty = false;
            foreach (var a in _assetList) { a.Count = 0; a.Bounds = new Bounds(); }
            int ready = 0, total = 0;
            foreach (var c in _chunks.Values)
            {
                if (!c.Ready) continue;
                ready++;
                for (int g = 0; g < c.Slots.Length; g++)
                {
                    SlotAsset a = Asset(c.Slots[g]);
                    var src = c.Mats[g];
                    int need = a.Count + src.Length;
                    if (a.Merged.Length < need)
                    {
                        var grown = new Matrix4x4[Math.Max(need, Math.Max(256, a.Merged.Length * 2))];
                        Array.Copy(a.Merged, grown, a.Count);
                        a.Merged = grown;
                    }
                    Array.Copy(src, 0, a.Merged, a.Count, src.Length);
                    if (a.Count == 0) a.Bounds = c.Bounds; else a.Bounds.Encapsulate(c.Bounds);
                    a.Count = need;
                    total += src.Length;
                }
            }
            ReadyChunks = ready;
            Instances = total;
        }

        public string StatusLine
        {
            get
            {
                return "clutter " + ReadyChunks + "/" + _chunks.Count + " chunks, " + Instances + " doodads, " + Built + " models (" + Failed + " failed), "
                    + _materials.Count + " materials, " + Draws + " draws, radius " + RadiusMetres.ToString("0") + " m, density " + _density.ToString("0.00");
            }
        }
    }
}
