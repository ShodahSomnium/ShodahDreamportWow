using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    // CONTRACT (owner: Models). Build / BuildStatic keep the stub signatures Terrain, Buildings and Front call.
    // Everything returned is in Unity space at WowSpace scale, local to the returned root (callers place the root).

    /// <summary>Optional inputs of an animated build.</summary>
    public sealed class M2BuildOptions
    {
        /// <summary>Textures for the M2's replaceable texture types (1 body, 2 object skin/item, 6 hair, 8 fur, 11-13 monster skins).</summary>
        public Dictionary<int, Texture> ReplaceableTextures = new Dictionary<int, Texture>();
        /// <summary>Game-file paths for replaceable types, loaded with the M2 texture's own wrap flags (used when no Texture is given).</summary>
        public Dictionary<int, string> ReplaceablePaths = new Dictionary<int, string>();
        /// <summary>Which submesh geoset ids to draw (null = all).</summary>
        public Func<int, bool> ShowGeoset;
        /// <summary>Lead 2026-10-11: which batches to draw by their texture's file name (null = all).</summary>
        public Func<string, bool> ShowTexture;
        /// <summary>Share materials between instances (false for per-character textures; the root then owns them).</summary>
        public bool ShareMaterials = true;
        /// <summary>Root GameObject name (default: the model's file name).</summary>
        public string Name;
    }

    /// <summary>Destroys runtime assets a built model owns (composed textures, unshared materials) with it.</summary>
    public sealed class M2OwnedAssets : MonoBehaviour
    {
        public readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        void OnDestroy() { foreach (var o in Owned) if (o != null) Destroy(o); Owned.Clear(); }
    }

    /// <summary>Builds M2 models (paths inside the MPQs; a legacy ".mdx" path is read as ".m2").</summary>
    public static class M2Factory
    {
        sealed class Parsed
        {
            public M2Model Model;
            public M2Skin Skin;
            public string Error;
        }

        /// <summary>Worker-thread result for a static model: baked mesh data + prepared textures.</summary>
        sealed class PreparedStatic
        {
            public M2Model Model;
            public M2MeshData Mesh;
            public WowPreparedTexture[] Textures;   // per batch (null = replaceable / none)
            public float[] RestMats;                // rest-pose bone matrices (null = identity) for the emitters (VFX)
            public int RestSeq = -1;
            public string Error;
        }

        /// <summary>Main-thread shared assets of a static model.</summary>
        sealed class StaticAsset
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Mesh Collision;
            public M2Effects.StaticFx Fx;   // particle / ribbon emitters at the rest pose (null = none)
            public string Name;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<string, Parsed> _parsed = new Dictionary<string, Parsed>();
        static readonly Dictionary<string, PreparedStatic> _prepared = new Dictionary<string, PreparedStatic>();
        static readonly Dictionary<string, StaticAsset> _static = new Dictionary<string, StaticAsset>();
        static readonly HashSet<string> _warned = new HashSet<string>();
        static long _parseMs, _uploadMs;
        static int _animatedBuilds, _staticInstances, _failed, _staticTris;

        static string Key(string path) { return (M2Model.NormalizeModelPath(path) ?? "").ToLowerInvariant(); }

        static void WarnOnce(string key, string msg)
        {
            bool first;
            lock (_lock) first = _warned.Add(key) && _warned.Count < 200;
            if (first) WowLog.Warn(msg);
        }

        // ------------------------------------------------------------------------------------ parsing (any thread)

        static Parsed ParseNow(WowFiles files, string path)
        {
            var p = new Parsed();
            var sw = Stopwatch.StartNew();
            try
            {
                string m2 = M2Model.NormalizeModelPath(path);
                var bytes = files.Read(m2);
                if (bytes == null) { p.Error = "not in the game files"; return p; }
                p.Model = M2Model.Parse(bytes, m2, files.Read);
                var skinBytes = files.Read(M2Model.SkinPath(m2, 0));
                if (skinBytes == null) { p.Error = "skin " + M2Model.SkinPath(m2, 0) + " missing"; return p; }
                p.Skin = M2Skin.Parse(skinBytes, M2Model.SkinPath(m2, 0), p.Model.VertexCount);
                int warns = p.Model.Warnings.Count + p.Skin.Warnings.Count;
                if (warns > 0) WowLog.Warn("M2: " + m2 + " parsed with " + warns + " warnings, first: " + (p.Model.Warnings.Count > 0 ? p.Model.Warnings[0] : p.Skin.Warnings[0]));
            }
            catch (Exception e) { p.Error = "unreadable: " + e.Message; }
            finally
            {
                lock (_lock) _parseMs += sw.ElapsedMilliseconds;
            }
            return p;
        }

        /// <summary>Parses (and caches) a model for animated builds. Any thread. False when missing/unreadable.</summary>
        public static bool Preload(WowFiles files, string m2Path)
        {
            return GetParsed(files, m2Path).Error == null;
        }

        static Parsed GetParsed(WowFiles files, string path)
        {
            string key = Key(path);
            Parsed p;
            lock (_lock) if (_parsed.TryGetValue(key, out p)) return p;
            p = files == null ? new Parsed { Error = "no WowFiles" } : ParseNow(files, path);
            lock (_lock) _parsed[key] = p;
            return p;
        }

        /// <summary>The parsed M2 (cached), or null when missing. Any thread.</summary>
        public static M2Model GetModel(WowFiles files, string m2Path)
        {
            var p = GetParsed(files, m2Path);
            return p.Error == null ? p.Model : null;
        }

        /// <summary>The parsed M2 + its skin 00 (cached). False (error set) when missing or unreadable. Any thread.</summary>
        public static bool TryGetParsed(WowFiles files, string m2Path, out M2Model model, out M2Skin skin, out string error)
        {
            var p = GetParsed(files, m2Path);
            model = p.Model; skin = p.Skin; error = p.Error;
            return p.Error == null;
        }

        /// <summary>
        /// Worker-thread half of BuildStatic: parses, bakes the rest pose and decodes the textures. Any thread; BuildStatic
        /// on the main thread then only uploads. False when the model is missing or unreadable.
        /// </summary>
        public static bool PrepareStatic(WowFiles files, string m2Path)
        {
            string key = Key(m2Path);
            lock (_lock)
            {
                if (_static.ContainsKey(key)) return true;
                PreparedStatic done;
                if (_prepared.TryGetValue(key, out done)) return done.Error == null;
            }
            var ps = PrepareStaticNow(files, m2Path);
            lock (_lock) _prepared[key] = ps;
            return ps.Error == null;
        }

        static PreparedStatic PrepareStaticNow(WowFiles files, string path)
        {
            var ps = new PreparedStatic();
            if (files == null) { ps.Error = "no WowFiles"; return ps; }
            var p = ParseNow(files, path);   // not cached: static models keep only their meshes
            if (p.Error != null) { ps.Error = p.Error; return ps; }
            var sw = Stopwatch.StartNew();
            var m = p.Model;
            float[] pos = null, nrm = null;
            ps.RestSeq = M2Pose.RestSequence(m);
            if (m.Bones.Length > 0)
            {
                int seq = ps.RestSeq;
                if (seq >= 0)
                {
                    var mats = M2Pose.Compute(m, seq, 0, 0);
                    if (!AllIdentity(mats))
                    {
                        pos = new float[m.VertexCount * 3]; nrm = new float[m.VertexCount * 3];
                        M2Pose.Skin(m, mats, pos, nrm);
                        if (m.HasEmitters) ps.RestMats = mats;
                    }
                }
            }
            var mesh = M2MeshData.Build(m, p.Skin, null, pos, nrm);
            // Zero first-key alpha is left out unless the alpha is animated on a blended batch (glows that pulse / fade in).
            mesh.Batches.RemoveAll(b => (b.Alpha <= 0f && !(b.AlphaAnimated && b.BlendMode >= 2)) || (b.TextureType != 0));
            ps.Model = m;
            ps.Mesh = mesh;
            ps.Textures = new WowPreparedTexture[mesh.Batches.Count];
            for (int i = 0; i < mesh.Batches.Count; i++)
            {
                var b = mesh.Batches[i];
                if (b.TextureType == 0 && !string.IsNullOrEmpty(b.TextureFile))
                {
                    var opt = WowTextureOptions.ForM2Flags((int)b.TextureFlags);
                    if (!WowTextures.IsCached(b.TextureFile, opt)) ps.Textures[i] = WowTextures.Prepare(files, b.TextureFile, opt);
                }
            }
            lock (_lock) _parseMs += sw.ElapsedMilliseconds;
            return ps;
        }

        static bool AllIdentity(float[] mats)
        {
            for (int b = 0; b < mats.Length; b += 16)
                for (int i = 0; i < 16; i++)
                {
                    float want = (i % 5 == 0) ? 1f : 0f;
                    if (Math.Abs(mats[b + i] - want) > 1e-4f) return false;
                }
            return true;
        }

        // ------------------------------------------------------------------------------------ static (main thread)

        /// <summary>
        /// A static instance for world props (doodads): a MeshRenderer in the model's resting pose (Stand at t = 0, baked on
        /// the CPU), sharing the cached mesh and materials between instances (GPU instancing on), plus a shared MeshCollider
        /// from the M2's collision triangles when it has any (PLAN rule 16). Batches with a replaceable texture or zero
        /// alpha are left out. Null (logged once per path) when the model is missing or unreadable.
        /// </summary>
        public static GameObject BuildStatic(WowFiles files, string m2Path)
        {
            if (string.IsNullOrEmpty(m2Path)) return null;
            string key = Key(m2Path);
            StaticAsset asset;
            lock (_lock) _static.TryGetValue(key, out asset);
            if (asset == null)
            {
                PreparedStatic ps;
                lock (_lock) { _prepared.TryGetValue(key, out ps); if (ps != null) _prepared.Remove(key); }
                if (ps == null) ps = PrepareStaticNow(files, m2Path);
                if (ps.Error != null)
                {
                    lock (_lock) _failed++;
                    WarnOnce(key, "M2: " + M2Model.NormalizeModelPath(m2Path) + " -> not built: " + ps.Error);
                    lock (_lock) _static[key] = new StaticAsset { Name = null };
                    return null;
                }
                asset = Upload(files, ps);
                lock (_lock) _static[key] = asset;
            }
            if (asset.Name == null) return null;   // failed before
            var go = new GameObject(asset.Name);
            if (asset.Mesh != null && asset.Materials.Length > 0)
            {
                go.AddComponent<MeshFilter>().sharedMesh = asset.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = asset.Materials;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
            if (asset.Collision != null)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = asset.Collision;
            }
            if (asset.Fx != null) M2Effects.AttachStatic(go, asset.Fx);
            _staticInstances++;
            return go;
        }

        static StaticAsset Upload(WowFiles files, PreparedStatic ps)
        {
            var sw = Stopwatch.StartNew();
            var m = ps.Model;
            var a = new StaticAsset { Name = string.IsNullOrEmpty(m.Name) ? FileName(m.Path) : m.Name };
            var d = ps.Mesh;
            if (d.Batches.Count > 0)
            {
                a.Mesh = MakeMesh(d, a.Name, false, null);
                a.Mesh.UploadMeshData(true);
                a.Materials = new Material[d.Batches.Count];
                for (int i = 0; i < d.Batches.Count; i++)
                {
                    var b = d.Batches[i];
                    Texture tex = ps.Textures[i] != null ? WowTextures.Create(ps.Textures[i])
                        : WowTextures.Get(files, b.TextureFile, WowTextureOptions.ForM2Flags((int)b.TextureFlags));
                    if (b.Animated)
                    {
                        // own material, animated once per frame for every instance of this doodad (M2EffectsRunner)
                        a.Materials[i] = M2Materials.Get(b, tex, false);
                        M2Effects.RegisterSharedMaterial(m, ps.RestSeq, b, a.Materials[i]);
                    }
                    else a.Materials[i] = M2Materials.Get(b, tex, true);
                    _staticTris += b.Indices.Length / 3;
                }
            }
            else a.Materials = new Material[0];
            if (m.HasCollision) a.Collision = MakeCollision(m, a.Name);
            if (m.HasEmitters)
            {
                try { a.Fx = M2Effects.PrepareStatic(files, m, ps.RestSeq, ps.RestMats); }
                catch (Exception e) { WarnOnce("fx|" + Key(m.Path), "M2Effects: " + m.Path + " emitters not built: " + e.Message); }
            }
            _uploadMs += sw.ElapsedMilliseconds;
            return a;
        }

        /// <summary>Collision mesh from the M2 bounding triangles (flipped like the render mesh, measured), readable for PhysX.</summary>
        static Mesh MakeCollision(M2Model m, string name)
        {
            int nv = m.CollisionPositions.Length / 3;
            var v = new Vector3[nv];
            for (int i = 0; i < nv; i++) v[i] = WowSpace.PointToUnity(m.CollisionPositions[i * 3], m.CollisionPositions[i * 3 + 1], m.CollisionPositions[i * 3 + 2]);
            var idx = new int[m.CollisionIndices.Length];
            for (int k = 0; k + 2 < idx.Length; k += 3)
            {
                idx[k] = m.CollisionIndices[k];
                idx[k + 1] = M2MeshData.FlipWinding ? m.CollisionIndices[k + 2] : m.CollisionIndices[k + 1];
                idx[k + 2] = M2MeshData.FlipWinding ? m.CollisionIndices[k + 1] : m.CollisionIndices[k + 2];
            }
            var mesh = new Mesh { name = name + " collision" };
            if (nv > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = v;
            mesh.triangles = idx;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Unity mesh from mesh data: WowSpace positions/normals, UVs unchanged (rule 15), one submesh per batch.</summary>
        static Mesh MakeMesh(M2MeshData d, string name, bool skinned, M2Model m)
        {
            int n = d.VertexCount;
            var v = new Vector3[n];
            var nr = new Vector3[n];
            var uv0 = new Vector2[n];
            var uv1 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = WowSpace.PointToUnity(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
                nr[i] = WowSpace.DirToUnity(d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]);
                uv0[i] = new Vector2(d.Uv0[i * 2], d.Uv0[i * 2 + 1]);
                uv1[i] = new Vector2(d.Uv1[i * 2], d.Uv1[i * 2 + 1]);
            }
            var mesh = new Mesh { name = name };
            if (n > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = v;
            mesh.normals = nr;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            if (skinned)
            {
                var bw = new BoneWeight[n];
                for (int i = 0; i < n; i++)
                {
                    int w0 = d.BoneWeights[i * 4], w1 = d.BoneWeights[i * 4 + 1], w2 = d.BoneWeights[i * 4 + 2], w3 = d.BoneWeights[i * 4 + 3];
                    int sum = w0 + w1 + w2 + w3;
                    var w = new BoneWeight();
                    if (sum <= 0) { w.boneIndex0 = 0; w.weight0 = 1f; }
                    else
                    {
                        float inv = 1f / sum;
                        w.boneIndex0 = d.BoneIndices[i * 4]; w.weight0 = w0 * inv;
                        w.boneIndex1 = d.BoneIndices[i * 4 + 1]; w.weight1 = w1 * inv;
                        w.boneIndex2 = d.BoneIndices[i * 4 + 2]; w.weight2 = w2 * inv;
                        w.boneIndex3 = d.BoneIndices[i * 4 + 3]; w.weight3 = w3 * inv;
                    }
                    bw[i] = w;
                }
                mesh.boneWeights = bw;
                var bind = new Matrix4x4[m.Bones.Length];
                for (int b = 0; b < bind.Length; b++)
                {
                    var p = m.Bones[b].Pivot;
                    bind[b] = Matrix4x4.Translate(-WowSpace.PointToUnity(p.X, p.Y, p.Z));
                }
                mesh.bindposes = bind;
            }
            mesh.subMeshCount = d.Batches.Count;
            for (int i = 0; i < d.Batches.Count; i++) mesh.SetTriangles(d.Batches[i].Indices, i, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "M2";
            int s = path.LastIndexOf('\\');
            string f = s >= 0 ? path.Substring(s + 1) : path;
            int dot = f.LastIndexOf('.');
            return dot > 0 ? f.Substring(0, dot) : f;
        }

        // ------------------------------------------------------------------------------------ animated (main thread)

        /// <summary>
        /// An animated instance: SkinnedMeshRenderer plus an M2Animator on the root, playing Stand. Replaceable textures are
        /// left out (use the overload with M2BuildOptions). Null (logged once per path) when the model is missing or unreadable.
        /// </summary>
        public static GameObject Build(WowFiles files, string m2Path)
        {
            return Build(files, m2Path, null);
        }

        /// <summary>Animated instance with replaceable textures / a geoset filter (WowCharacter, WowCreatures, WowItems use it).</summary>
        public static GameObject Build(WowFiles files, string m2Path, M2BuildOptions options)
        {
            if (string.IsNullOrEmpty(m2Path)) return null;
            var sw = Stopwatch.StartNew();
            var p = GetParsed(files, m2Path);
            if (p.Error != null)
            {
                lock (_lock) _failed++;
                WarnOnce(Key(m2Path), "M2: " + M2Model.NormalizeModelPath(m2Path) + " -> not built: " + p.Error);
                return null;
            }
            options = options ?? new M2BuildOptions();
            var m = p.Model;
            string name = options.Name ?? (string.IsNullOrEmpty(m.Name) ? FileName(m.Path) : m.Name);
            var d = M2MeshData.Build(m, p.Skin, options.ShowGeoset, null, null, options.ShowTexture);

            // Textures per batch; batches whose replaceable texture was not supplied (or with zero alpha) are left out.
            var keep = new List<M2BatchInfo>();
            var texes = new List<Texture>();
            var skippedTypes = new HashSet<int>();
            foreach (var b in d.Batches)
            {
                if (b.Alpha <= 0f && !(b.AlphaAnimated && b.BlendMode >= 2)) continue;
                Texture tex = null;
                if (b.TextureType == 0) tex = string.IsNullOrEmpty(b.TextureFile) ? null : WowTextures.Get(files, b.TextureFile, WowTextureOptions.ForM2Flags((int)b.TextureFlags));
                else
                {
                    Texture given;
                    string path;
                    if (options.ReplaceableTextures != null && options.ReplaceableTextures.TryGetValue(b.TextureType, out given) && given != null) tex = given;
                    else if (options.ReplaceablePaths != null && options.ReplaceablePaths.TryGetValue(b.TextureType, out path) && !string.IsNullOrEmpty(path))
                        tex = WowTextures.Get(files, path, WowTextureOptions.ForM2Flags((int)b.TextureFlags));
                }
                if (tex == null) { skippedTypes.Add(b.TextureType); continue; }
                keep.Add(b);
                texes.Add(tex);
            }
            d.Batches.Clear();
            d.Batches.AddRange(keep);

            var root = new GameObject(name);
            var owned = root.AddComponent<M2OwnedAssets>();   // the mesh (+ unshared materials) die with the root
            var boneT = new Transform[m.Bones.Length];
            for (int i = 0; i < boneT.Length; i++)
            {
                var b = m.Bones[i];
                boneT[i] = new GameObject("bone" + i + (b.KeyBoneId >= 0 ? "_k" + b.KeyBoneId : "")).transform;
            }
            for (int i = 0; i < boneT.Length; i++)
            {
                var b = m.Bones[i];
                Vector3 pivot = WowSpace.PointToUnity(b.Pivot.X, b.Pivot.Y, b.Pivot.Z);
                Vector3 parentPivot = Vector3.zero;
                Transform parent = root.transform;
                if (b.Parent >= 0) { var pp = m.Bones[b.Parent].Pivot; parentPivot = WowSpace.PointToUnity(pp.X, pp.Y, pp.Z); parent = boneT[b.Parent]; }
                boneT[i].SetParent(parent, false);
                boneT[i].localPosition = pivot - parentPivot;
            }

            Renderer meshRenderer = null;
            var mats = new Material[d.Batches.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = M2Materials.Get(d.Batches[i], texes[i], options.ShareMaterials);
                if (!options.ShareMaterials) owned.Owned.Add(mats[i]);
            }

            if (d.Batches.Count > 0)
            {
                bool skinned = m.Bones.Length > 0;
                var mesh = MakeMesh(d, name, skinned, m);
                owned.Owned.Add(mesh);
                var meshGo = new GameObject("mesh");
                meshGo.transform.SetParent(root.transform, false);
                if (skinned)
                {
                    var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
                    meshRenderer = smr;
                    smr.sharedMesh = mesh;
                    smr.bones = boneT;
                    smr.rootBone = root.transform;
                    var bmin = WowSpace.PointToUnity(m.BoundsMin.X, m.BoundsMin.Y, m.BoundsMin.Z);
                    var bmax = WowSpace.PointToUnity(m.BoundsMax.X, m.BoundsMax.Y, m.BoundsMax.Z);
                    var bounds = mesh.bounds;
                    bounds.Encapsulate(bmin); bounds.Encapsulate(bmax);
                    bounds.Expand(bounds.size.magnitude * 0.25f);
                    smr.localBounds = bounds;
                    smr.updateWhenOffscreen = false;
                    smr.sharedMaterials = mats;
                }
                else
                {
                    meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = meshGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    meshRenderer = mr;
                }
            }

            var att = new Dictionary<int, Transform>();
            foreach (var a in m.Attachments)
            {
                if (att.ContainsKey(a.Id)) continue;
                var t = new GameObject("att" + a.Id).transform;
                Vector3 ap = WowSpace.PointToUnity(a.Position.X, a.Position.Y, a.Position.Z);
                if (a.Bone >= 0 && a.Bone < boneT.Length)
                {
                    var pv = m.Bones[a.Bone].Pivot;
                    t.SetParent(boneT[a.Bone], false);
                    t.localPosition = ap - WowSpace.PointToUnity(pv.X, pv.Y, pv.Z);
                }
                else { t.SetParent(root.transform, false); t.localPosition = ap; }
                att[a.Id] = t;
            }

            var anim = root.AddComponent<M2Animator>();
            anim.Init(m, files, boneT, att);
            try { M2Effects.AttachAnimated(root, m, boneT, anim, files, meshRenderer, d.Batches); }
            catch (Exception e) { WarnOnce("fx|" + Key(m.Path), "M2Effects: " + m.Path + " effects not built: " + e.Message); }
            _animatedBuilds++;
            int tris = 0;
            foreach (var b in d.Batches) tris += b.Indices.Length / 3;
            WowLog.Info("M2: " + m.Path + " built: " + d.VertexCount + " verts, " + tris + " tris, " + d.Batches.Count + " batches"
                + (skippedTypes.Count > 0 ? " (left out: replaceable types " + string.Join(",", skippedTypes) + ")" : "")
                + ", " + m.Bones.Length + " bones, " + m.Sequences.Length + " sequences (.anim " + m.ExternalAnimsLoaded + "/" + m.ExternalAnimsNeeded + "), "
                + att.Count + " attachments, " + sw.ElapsedMilliseconds + " ms");
            return root;
        }

        // ------------------------------------------------------------------------------------ housekeeping

        /// <summary>One line for Player.log: cache sizes and timings.</summary>
        public static string StatsLine()
        {
            lock (_lock)
                return "M2Factory: " + _static.Count + " static models (" + _staticTris + " tris), " + _staticInstances + " static instances, "
                    + _animatedBuilds + " animated builds, " + _failed + " failed, " + M2Materials.CachedCount + " shared materials, parse "
                    + _parseMs + " ms, upload " + _uploadMs + " ms";
        }

        /// <summary>Destroys the cached static meshes and forgets parsed models (only when no instance uses them any more).</summary>
        public static void ClearCaches()
        {
            lock (_lock)
            {
                foreach (var a in _static.Values)
                {
                    if (a.Mesh != null) UnityEngine.Object.Destroy(a.Mesh);
                    if (a.Collision != null) UnityEngine.Object.Destroy(a.Collision);
                }
                _static.Clear();
                _prepared.Clear();
                _parsed.Clear();
                _warned.Clear();
            }
            ClearCollisionCache();   // iteration 11
            M2Materials.Clear();
        }

        // iteration 11: collision meshes of models that were never built static (an animated transport), main thread only
        static readonly Dictionary<string, Mesh> _collision = new Dictionary<string, Mesh>();

        /// <summary>Iteration 11 (proposal-motion.md hook 9; filled by the Models host - avatar): the cached MakeCollision mesh of an M2
        /// (the static asset's when it was built static, else made once from the parsed model), null when the model has none / is
        /// missing. For a transport's MeshCollider (WowEntityView). Main thread (a Mesh is created).</summary>
        public static Mesh CollisionMesh(WowFiles files, string m2Path)
        {
            if (files == null || string.IsNullOrEmpty(m2Path)) return null;
            string key = Key(m2Path);
            StaticAsset a;
            lock (_lock) _static.TryGetValue(key, out a);
            if (a != null && a.Collision != null) return a.Collision;
            Mesh mesh;
            lock (_lock) if (_collision.TryGetValue(key, out mesh)) return mesh;
            M2Model m = GetModel(files, m2Path);
            mesh = m != null && m.HasCollision ? MakeCollision(m, System.IO.Path.GetFileNameWithoutExtension(m2Path) + " (collision)") : null;
            lock (_lock) _collision[key] = mesh;
            return mesh;
        }

        /// <summary>Iteration 11: ClearCaches' twin for the collision meshes made by CollisionMesh.</summary>
        public static void ClearCollisionCache()
        {
            lock (_lock)
            {
                foreach (var m in _collision.Values) if (m != null) UnityEngine.Object.Destroy(m);
                _collision.Clear();
            }
        }
    }
}
