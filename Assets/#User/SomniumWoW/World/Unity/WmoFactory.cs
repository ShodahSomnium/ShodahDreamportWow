using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Builds a WMO (root + its group files) as a Unity GameObject in Unity space at WowSpace scale, local to the returned root:
    /// one child per group (one mesh, one submesh per MOBA batch, shared materials on SomniumWoW/Wmo with the baked MOCV
    /// lighting), a MeshCollider per group on layer 0 from the collidable triangles, the group's MLIQ liquid (material from
    /// WowWater.ForLiquidType), and the doodads of set 0 + the requested set (M2Factory.BuildStatic, spawned over several
    /// frames by WmoInstance within DoodadBudgetMs per frame). Geometry, meshes and materials are cached per path; every new
    /// instance shares them.
    ///
    /// Threading: Prepare (any thread) reads, parses, builds the mesh arrays and decodes the textures; Build (main thread) makes
    /// the Unity objects - it calls Prepare itself when nobody did. Call WmoFactory.Prepare on a worker for each WMO a tile
    /// places, then Build on the main thread.
    /// Rules and measurements: World/WmoGeometry.cs, Shaders/WmoShader.shader, P/reports/buildings.md.
    /// Iteration 11 (lod11): antiportal groups are not built; portal-only backdrops (Stormwind's cathedral stand-in) get no
    /// collider and are shown by WmoPortalGate only where the client shows them (World/WmoGroupVisibility.cs).
    /// Iteration 12 (portal culling): exterior groups of a WMO with portals are switched by WmoPortalGate as the client's portal
    /// walk does while the camera stands in one of its interior groups (World/WmoPortalCuller.cs).
    /// </summary>
    public static class WmoFactory
    {
        /// <summary>Main-thread milliseconds per frame that all WMO instances together spend spawning doodads.</summary>
        public static float DoodadBudgetMs = 3f;

        sealed class GroupData
        {
            public int Index;
            public string Name;
            public bool Exterior, Interior, HasMocv;
            public uint MogpFlags;              // iteration 7 (Body): the group's MOGP flags (0x8 exterior, 0x2000 interior)
            public int GroupId;                 // iteration 7 (Body): MOGP UniqueId (+56) = WMOAreaTable WMOGroupID
            public int RenderTris, CollisionTris, LiquidTiles, LiquidType;
            public WmoPortalView[] Views;       // iteration 11 (lod11): non-null = portal-only group, drawn by WmoPortalGate
            public List<int> OwnDoodads;        // iteration 11 (lod11): MODD indices only this portal-only group references (MODR)
            public bool GatedExterior;          // iteration 12: exterior group WmoPortalCuller may hide (WmoPortalGate)
            // arrays until the Unity objects exist
            public Vector3[] Pos, Nrm, CollPos, LiqPos;
            public Vector2[] Uv0, Uv1, Blend, LiqUv;
            public Color32[] Col;
            public int[][] Sub;
            public int[] SubMat;
            public int[] CollIdx, LiqIdx;
            public Vector3[] LiqSurfPos; public int[] LiqSurfIdx;   // lead 2026-10-26: kept for WowLiquidSurface (swimming); the mesh is not readable
            // Unity objects (main thread)
            public Mesh Mesh, Collision, Liquid;
            public Material[] Materials;
            public Material LiquidMaterial;
        }

        sealed class Entry
        {
            public readonly object Lock = new object();
            public string Path;
            public volatile bool Prepared;
            public string Error;
            public bool ErrorLogged, Built, Logged;
            public WmoRoot Root;
            public GroupData[] Groups = new GroupData[0];
            public int GroupsMissing;
            public string FirstGroupError;
            public List<WowPreparedTexture> Textures = new List<WowPreparedTexture>();
            public long ParseMs, PrepareMs, BuildMs;
            public int Instances, NewMaterials, TextureCount;
            public readonly List<string> VisibilityNotes = new List<string>();   // iteration 11 (lod11): logged once with the WMO line
            public WmoPortalCuller Culler;     // iteration 12: null when no group of this WMO can ever be hidden by portals
        }

        static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        static readonly Dictionary<int, Material> LiquidMaterials = new Dictionary<int, Material>();
        static readonly HashSet<int> LiquidFailed = new HashSet<int>();
        static int _instances;

        static string KeyOf(string path) { return WowTextures.NormalizePath(path); }

        static Entry GetEntry(string path)
        {
            string key = KeyOf(path);
            lock (Entries)
            {
                Entry e;
                if (!Entries.TryGetValue(key, out e)) Entries[key] = e = new Entry { Path = path };
                return e;
            }
        }

        /// <summary>True when the WMO was prepared (successfully or not) and Build will not read files.</summary>
        public static bool IsPrepared(string wmoPath)
        {
            if (string.IsNullOrEmpty(wmoPath)) return false;
            lock (Entries)
            {
                Entry e;
                return Entries.TryGetValue(KeyOf(wmoPath), out e) && (e.Prepared || e.Error != null);
            }
        }

        /// <summary>
        /// Any thread: reads + parses the WMO and its groups, builds the mesh arrays (Unity space) and decodes the textures its
        /// batches use. Returns false when the WMO is missing or unreadable (Build then returns null). Safe to call repeatedly.
        /// </summary>
        public static bool Prepare(WowFiles files, string wmoPath)
        {
            if (files == null || string.IsNullOrEmpty(wmoPath)) return false;
            var e = GetEntry(wmoPath);
            lock (e.Lock)
            {
                if (e.Prepared) return true;
                if (e.Error != null) return false;
                var sw = Stopwatch.StartNew();
                try
                {
                    var model = WmoModel.Load(files, wmoPath);
                    e.Root = model.Root;
                    e.ParseMs = model.ParseMs;
                    var groups = new List<GroupData>();
                    for (int i = 0; i < model.Groups.Length; i++)
                    {
                        if (model.Groups[i] == null)
                        {
                            e.GroupsMissing++;
                            if (e.FirstGroupError == null) e.FirstGroupError = model.GroupErrors[i];
                            continue;
                        }
                        // iteration 11 (lod11): groups the 3.3.5a client shows only through a portal, or never (World/WmoGroupVisibility.cs)
                        WmoPortalView[] views;
                        string why;
                        var draw = WmoGroupVisibility.Classify(model, i, out views, out why);
                        if (draw == WmoGroupDraw.Never)
                        {
                            e.VisibilityNotes.Add("group " + i + " not built: " + why);
                            continue;
                        }
                        var gd = Convert(WmoGeometry.Build(model.Root, model.Groups[i], i));
                        gd.MogpFlags = model.Groups[i].Flags;      // iteration 7 (Body): WMOAreaTable key + AC's indoor bit
                        gd.GroupId = model.Groups[i].UniqueId;
                        if (draw == WmoGroupDraw.ThroughPortal)
                        {
                            gd.Views = views;
                            gd.CollPos = new Vector3[0];
                            gd.CollIdx = new int[0];
                            gd.CollisionTris = 0;
                            e.VisibilityNotes.Add("group " + i + " " + why);
                        }
                        groups.Add(gd);
                    }
                    e.Groups = groups.ToArray();
                    // iteration 12 (portal culling): exterior groups the client only shows through portals from interior groups
                    var built = new bool[model.Groups.Length];
                    foreach (var gd in groups) if (gd.Index >= 0 && gd.Index < built.Length) built[gd.Index] = true;
                    string cullNote;
                    e.Culler = WmoPortalCuller.Create(model.Root, model.Groups, built, out cullNote);
                    if (e.Culler != null)
                    {
                        e.VisibilityNotes.Add(cullNote);
                        foreach (var gd in groups) gd.GatedExterior = System.Array.IndexOf(e.Culler.GatedGroups, gd.Index) >= 0;
                    }
                    MarkOwnDoodads(model, e.Groups);   // iteration 11 (lod11)

                    // textures the batches use, decoded now (off the main thread) unless WowTextures has them already
                    var seen = new HashSet<string>();
                    foreach (var g in e.Groups)
                        foreach (int mi in g.SubMat)
                        {
                            if (mi < 0 || mi >= e.Root.Materials.Length) continue;
                            var m = e.Root.Materials[mi];
                            var o = WmoMaterials.TextureOptions(m);
                            AddTexture(files, e, seen, m.Texture1, o);
                            if (WmoMaterials.UsesTexture2(m)) AddTexture(files, e, seen, m.Texture2, o);
                        }
                    e.TextureCount = seen.Count;
                    e.PrepareMs = sw.ElapsedMilliseconds;
                    e.Prepared = true;
                    return true;
                }
                catch (FileNotFoundException ex) { e.Error = ex.Message; }
                catch (Exception ex) { e.Error = "unreadable: " + ex.GetType().Name + ": " + ex.Message; }
                return false;
            }
        }

        /// <summary>Iteration 11 (lod11): for each portal-only group, the MODD indices no other group of the WMO references in its
        /// MODR (Stormwind 'bazaarfacade03': its 1 doodad is shared with 'TreeFacades06', so none; Blacktemple 'BossSummit_facade':
        /// 120 of 129).</summary>
        static void MarkOwnDoodads(WmoModel model, GroupData[] groups)
        {
            bool any = false;
            foreach (var g in groups) if (g.Views != null || g.GatedExterior) any = true;
            if (!any) return;
            var refs = new Dictionary<int, int>();   // MODD index -> number of groups referencing it
            foreach (var mg in model.Groups)
            {
                if (mg == null) continue;
                var seen = new HashSet<int>();
                foreach (ushort r in mg.DoodadRefs)
                    if (seen.Add(r)) { int c; refs.TryGetValue(r, out c); refs[r] = c + 1; }
            }
            foreach (var g in groups)
            {
                if ((g.Views == null && !g.GatedExterior) || g.Index < 0 || g.Index >= model.Groups.Length || model.Groups[g.Index] == null) continue;
                foreach (ushort r in model.Groups[g.Index].DoodadRefs)
                {
                    int c;
                    if (refs.TryGetValue(r, out c) && c == 1)
                    {
                        if (g.OwnDoodads == null) g.OwnDoodads = new List<int>();
                        if (!g.OwnDoodads.Contains(r)) g.OwnDoodads.Add(r);
                    }
                }
            }
        }

        static void AddTexture(WowFiles files, Entry e, HashSet<string> seen, string path, WowTextureOptions o)
        {
            if (string.IsNullOrEmpty(path)) return;
            string key = WowTextures.KeyOf(path, o);
            if (!seen.Add(key) || WowTextures.IsCached(path, o)) return;
            e.Textures.Add(WowTextures.Prepare(files, path, o));
        }

        static GroupData Convert(WmoGroupGeometry g)
        {
            var d = new GroupData
            {
                Index = g.GroupIndex, Name = g.Name, Exterior = g.Exterior, Interior = g.Interior, HasMocv = g.HasMocv,
                RenderTris = g.RenderTriangles, LiquidTiles = g.LiquidTiles, LiquidType = g.LiquidTypeId,
            };
            int n = g.VertexCount;
            d.Pos = new Vector3[n];
            d.Nrm = new Vector3[n];
            d.Uv0 = new Vector2[n];
            d.Col = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                d.Pos[i] = WowSpace.PointToUnity(g.Positions[i * 3], g.Positions[i * 3 + 1], g.Positions[i * 3 + 2]);
                d.Nrm[i] = WowSpace.DirToUnity(g.Normals[i * 3], g.Normals[i * 3 + 1], g.Normals[i * 3 + 2]);
                d.Uv0[i] = new Vector2(g.Uv0[i * 2], g.Uv0[i * 2 + 1]);   // PLAN rule 15: WoW UVs unchanged
                d.Col[i] = new Color32(g.Light[i * 4], g.Light[i * 4 + 1], g.Light[i * 4 + 2], g.Light[i * 4 + 3]);
            }
            if (g.Uv1 != null)
            {
                d.Uv1 = new Vector2[n];
                d.Blend = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    d.Uv1[i] = new Vector2(g.Uv1[i * 2], g.Uv1[i * 2 + 1]);
                    d.Blend[i] = new Vector2(g.Blend[i], 0f);
                }
            }
            d.Sub = new int[g.Submeshes.Count][];
            d.SubMat = new int[g.Submeshes.Count];
            for (int s = 0; s < g.Submeshes.Count; s++) { d.Sub[s] = g.Submeshes[s].Indices; d.SubMat[s] = g.Submeshes[s].Material; }

            int nc = g.CollisionPositions.Length / 3;
            d.CollPos = new Vector3[nc];
            for (int i = 0; i < nc; i++) d.CollPos[i] = WowSpace.PointToUnity(g.CollisionPositions[i * 3], g.CollisionPositions[i * 3 + 1], g.CollisionPositions[i * 3 + 2]);
            d.CollIdx = g.CollisionIndices;
            d.CollisionTris = d.CollIdx.Length / 3;

            if (g.LiquidPositions != null)
            {
                int nl = g.LiquidPositions.Length / 3;
                d.LiqPos = new Vector3[nl];
                d.LiqUv = new Vector2[nl];
                for (int i = 0; i < nl; i++)
                {
                    d.LiqPos[i] = WowSpace.PointToUnity(g.LiquidPositions[i * 3], g.LiquidPositions[i * 3 + 1], g.LiquidPositions[i * 3 + 2]);
                    d.LiqUv[i] = new Vector2(g.LiquidUv[i * 2], g.LiquidUv[i * 2 + 1]);
                }
                d.LiqIdx = g.LiquidIndices;
            }
            return d;
        }

        /// <summary>
        /// Main thread. Null (logged once per path) when missing. doodadSet: MODF's set index (set 0 is always included;
        /// another valid index adds that set). withDoodads false: no doodads at all. Same as the 5-argument Build with NameSet 0.
        /// </summary>
        public static GameObject Build(WowFiles files, string wmoPath, int doodadSet, bool withDoodads) { return Build(files, wmoPath, doodadSet, withDoodads, 0); }

        /// <summary>
        /// Iteration 7 (lead contract PLAN7 s3.6; owner Body): Build with the MODF NameSet, which WMOAreaTable rows are keyed by
        /// (AC: (root WMO id, adt NameSet, group id)). Every group GameObject gets a World.Tags.WmoGroupInfo with those ids and
        /// its MOGP flags, so MountPlace can apply AzerothCore's indoor rule to whatever the ray under the player hits.
        /// </summary>
        public static GameObject Build(WowFiles files, string wmoPath, int doodadSet, bool withDoodads, int nameSet)
        {
            if (files == null || string.IsNullOrEmpty(wmoPath)) return null;
            var e = GetEntry(wmoPath);
            if (!e.Prepared && e.Error == null) Prepare(files, wmoPath);
            if (!e.Prepared)
            {
                if (!e.ErrorLogged)
                {
                    e.ErrorLogged = true;
                    WowLog.Warn("WMO " + wmoPath + ": not built - " + e.Error);
                }
                return null;
            }

            var sw = Stopwatch.StartNew();
            lock (e.Lock)
            {
                if (!e.Built)
                {
                    try { MakeUnityObjects(files, e); }
                    catch (Exception ex)
                    {
                        e.Error = "Unity objects failed: " + ex.GetType().Name + ": " + ex.Message;
                        e.Prepared = false;
                        WowLog.Error("WMO " + wmoPath + ": " + e.Error + "\n" + ex.StackTrace);
                        e.ErrorLogged = true;
                        return null;
                    }
                    e.Built = true;
                }
            }

            var root = new GameObject("WMO " + FileName(wmoPath));
            var inst = root.AddComponent<WmoInstance>();
            inst.Path = wmoPath;
            inst.DoodadSet = doodadSet;
            int groupsMade = 0;
            WmoPortalGate gate = null;
            if (e.Culler != null)   // iteration 12 (portal culling)
            {
                gate = root.AddComponent<WmoPortalGate>();
                gate.SetCuller(e.Culler, wmoPath);
            }
            Dictionary<int, Transform> doodadParents = null;
            foreach (var g in e.Groups)
            {
                if (g.Mesh == null && g.Collision == null && g.Liquid == null) continue;
                var go = new GameObject("Group " + g.Index.ToString("000") + (string.IsNullOrEmpty(g.Name) ? "" : " " + g.Name) + (g.Exterior ? " (ext)" : g.Interior ? " (int)" : ""));
                go.layer = 0;   // PLAN rule 16: Default layer, the avatar's capsule collides with it
                go.transform.SetParent(root.transform, false);
                var info = go.AddComponent<SomniumSpace.Worlds.SomniumWoW.World.Tags.WmoGroupInfo>();   // iteration 7 (Body)
                info.WmoId = e.Root != null ? e.Root.WmoId : 0;
                info.NameSet = nameSet;
                info.GroupId = g.GroupId;
                info.MogpFlags = g.MogpFlags;
                info.GroupName = g.Name;
                info.RootPath = wmoPath;
                if (g.Mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = g.Mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = g.Materials;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    mr.lightProbeUsage = LightProbeUsage.Off;          // SampleSH = RenderSettings ambient (PLAN rule 14)
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                if (g.Collision != null)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = g.Collision;
                    if (e.Culler != null) gate.AddGroupCollider(mc, g.Index);   // iteration 12: the camera-group ray
                }
                if (g.Liquid != null && g.LiquidMaterial != null)
                {
                    var lq = new GameObject("Liquid type " + g.LiquidType);
                    lq.layer = 0;
                    lq.transform.SetParent(go.transform, false);
                    lq.AddComponent<MeshFilter>().sharedMesh = g.Liquid;
                    var lr = lq.AddComponent<MeshRenderer>();
                    lr.sharedMaterial = g.LiquidMaterial;
                    lr.shadowCastingMode = ShadowCastingMode.Off;
                    lr.lightProbeUsage = LightProbeUsage.Off;
                    lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    if (g.LiqSurfPos != null) lq.AddComponent<WowLiquidSurface>().Init(g.LiqSurfPos, g.LiqSurfIdx);   // lead 2026-10-26: swimming in WMO water
                }
                if (g.Views != null)   // iteration 11 (lod11): portal-only group, shown by the gate where the client would show it
                {
                    if (gate == null) gate = root.AddComponent<WmoPortalGate>();
                    GameObject own = null;
                    if (withDoodads && g.OwnDoodads != null && g.OwnDoodads.Count > 0)
                    {
                        // the doodads only this group references (MODR) appear and vanish with it
                        own = new GameObject("Doodads of group " + g.Index.ToString("000"));
                        own.transform.SetParent(go.transform, false);
                        if (doodadParents == null) doodadParents = new Dictionary<int, Transform>();
                        foreach (int di in g.OwnDoodads) doodadParents[di] = own.transform;
                    }
                    gate.Add(go.GetComponentsInChildren<Renderer>(true), g.Views, own);
                }
                if (g.GatedExterior && gate != null)   // iteration 12 (portal culling): drawn only where the client's portal walk sees it
                {
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    Transform own = null;
                    if (withDoodads && g.OwnDoodads != null && g.OwnDoodads.Count > 0)
                    {
                        // the doodads only this group references (MODR): their renderers follow the group's (colliders stay)
                        own = new GameObject("Doodads of group " + g.Index.ToString("000")).transform;
                        own.SetParent(go.transform, false);
                        if (doodadParents == null) doodadParents = new Dictionary<int, Transform>();
                        foreach (int di in g.OwnDoodads) doodadParents[di] = own;
                    }
                    gate.AddExterior(g.Index, rs, own);
                }
                groupsMade++;
            }

            int queued = 0;
            if (withDoodads) queued = inst.QueueDoodads(files, e.Root, doodadSet, !e.Logged, doodadParents);
            e.Instances++;
            _instances++;

            if (!e.Logged)
            {
                e.Logged = true;
                int ext = 0, intr = 0, tris = 0, coll = 0, liq = 0, subs = 0;
                foreach (var g in e.Groups)
                {
                    if (g.Exterior) ext++; if (g.Interior) intr++;
                    tris += g.RenderTris; coll += g.CollisionTris; if (g.LiquidTiles > 0) liq++;
                    subs += g.SubMat.Length;
                }
                WowLog.Info("WMO " + wmoPath + ": " + e.Groups.Length + " groups (" + ext + " ext, " + intr + " int"
                    + (e.GroupsMissing > 0 ? ", " + e.GroupsMissing + " MISSING: " + e.FirstGroupError : "") + "), " + subs + " batches, "
                    + tris + " tris, " + coll + " collision tris, " + e.Root.Materials.Length + " materials (" + e.NewMaterials + " new), "
                    + e.TextureCount + " textures, " + liq + " liquids, MOHD flags 0x" + e.Root.Flags.ToString("X")
                    + (e.Root.UnifiedRender ? " (unified)" : " (classic)") + ", doodads " + (withDoodads ? queued + " queued (set " + doodadSet + ")" : "off")
                    + "; prepare " + e.PrepareMs + " ms (parse " + e.ParseMs + "), unity " + e.BuildMs + " ms, instance " + sw.ElapsedMilliseconds + " ms");
                foreach (var note in e.VisibilityNotes) WowLog.Info("World: WMO " + wmoPath + " " + note);   // iteration 11 (lod11)
            }
            return root;
        }

        static void MakeUnityObjects(WowFiles files, Entry e)
        {
            var sw = Stopwatch.StartNew();
            foreach (var p in e.Textures) WowTextures.Create(p);   // uploads (cached by WowTextures)
            e.Textures.Clear();
            Func<string, WowTextureOptions, Texture2D> tex = (path, o) => WowTextures.Get(files, path, o);
            string baseName = FileName(e.Path);
            foreach (var g in e.Groups)
            {
                if (g.Sub.Length > 0)
                {
                    var mesh = new Mesh { name = "WMO " + baseName + " g" + g.Index.ToString("000") };
                    mesh.indexFormat = g.Pos.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(g.Pos);
                    mesh.SetNormals(g.Nrm);
                    mesh.SetUVs(0, g.Uv0);
                    if (g.Uv1 != null) { mesh.SetUVs(1, g.Uv1); mesh.SetUVs(2, g.Blend); }
                    mesh.SetColors(g.Col);
                    mesh.subMeshCount = g.Sub.Length;
                    g.Materials = new Material[g.Sub.Length];
                    for (int s = 0; s < g.Sub.Length; s++)
                    {
                        mesh.SetTriangles(g.Sub[s], s, false);
                        int mi = g.SubMat[s];
                        bool created;
                        g.Materials[s] = WmoMaterials.Get(mi >= 0 && mi < e.Root.Materials.Length ? e.Root.Materials[mi] : null, tex, out created);
                        if (created) e.NewMaterials++;
                    }
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    g.Mesh = mesh;
                }
                if (g.CollIdx.Length >= 3)
                {
                    var c = new Mesh { name = "WMO " + baseName + " g" + g.Index.ToString("000") + " collision" };
                    c.indexFormat = g.CollPos.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    c.SetVertices(g.CollPos);
                    c.SetTriangles(g.CollIdx, 0, true);
                    // Cook once here: every MeshCollider that gets this mesh afterwards uses the baked data (Unity docs,
                    // Physics.BakeMesh), so further instances of the same WMO do not cook again.
                    Physics.BakeMesh(c.GetEntityId(), false);
                    g.Collision = c;   // stays readable: PhysX cooks from it
                }
                if (g.LiqIdx != null && g.LiqIdx.Length >= 3)
                {
                    var mat = LiquidMaterial(files, g.LiquidType);
                    if (mat != null)
                    {
                        var l = new Mesh { name = "WMO " + baseName + " g" + g.Index.ToString("000") + " liquid " + g.LiquidType };
                        l.SetVertices(g.LiqPos);
                        var up = new Vector3[g.LiqPos.Length];
                        for (int i = 0; i < up.Length; i++) up[i] = Vector3.up;
                        l.SetNormals(up);
                        l.SetUVs(0, g.LiqUv);
                        l.SetTriangles(g.LiqIdx, 0, true);
                        l.UploadMeshData(true);
                        g.Liquid = l;
                        g.LiquidMaterial = mat;
                        g.LiqSurfPos = g.LiqPos;
                        g.LiqSurfIdx = g.LiqIdx;
                    }
                }
                // the arrays live on in the meshes; drop the managed copies
                g.Pos = g.Nrm = g.CollPos = g.LiqPos = null;
                g.Uv0 = g.Uv1 = g.Blend = g.LiqUv = null;
                g.Col = null;
                g.Sub = new int[0][];
                g.CollIdx = new int[0];
                g.LiqIdx = null;
            }
            e.BuildMs = sw.ElapsedMilliseconds;
        }

        static Material LiquidMaterial(WowFiles files, int liquidType)
        {
            Material m;
            if (LiquidMaterials.TryGetValue(liquidType, out m) && m != null) return m;
            if (LiquidFailed.Contains(liquidType)) return null;
            try { m = WowWater.ForLiquidType(files, liquidType); }
            catch (Exception ex)
            {
                WowLog.Warn("WMO liquid type " + liquidType + ": WowWater.ForLiquidType failed (" + ex.GetType().Name + ": " + ex.Message + ") - WMO liquids of this type not drawn");
                m = null;
            }
            if (m == null) { LiquidFailed.Add(liquidType); return null; }
            LiquidMaterials[liquidType] = m;
            return m;
        }

        /// <summary>
        /// Destroys every cached WMO mesh and material and forgets the parsed files (main thread). Only when no WMO instance
        /// is alive any more (e.g. after unloading a map). WowWater's liquid materials are not destroyed (Terrain owns them).
        /// </summary>
        public static void Clear()
        {
            lock (Entries)
            {
                foreach (var e in Entries.Values)
                    foreach (var g in e.Groups)
                    {
                        if (g.Mesh != null) DestroyAsset(g.Mesh);
                        if (g.Collision != null) DestroyAsset(g.Collision);
                        if (g.Liquid != null) DestroyAsset(g.Liquid);
                    }
                Entries.Clear();
            }
            LiquidMaterials.Clear();
            LiquidFailed.Clear();
            WmoMaterials.Clear();
            WmoInstance.ResetStats();
        }

        /// <summary>One line for Player.log: caches, instances, doodads.</summary>
        public static string StatsLine()
        {
            int prepared = 0, built = 0, failed = 0, groups = 0;
            long tris = 0;
            lock (Entries)
            {
                foreach (var e in Entries.Values)
                {
                    if (e.Prepared) prepared++;
                    if (e.Error != null) failed++;
                    if (e.Built) { built++; groups += e.Groups.Length; foreach (var g in e.Groups) tris += g.RenderTris; }
                }
            }
            return "WmoFactory: " + prepared + " WMOs prepared, " + built + " built, " + failed + " failed, " + _instances + " instances, "
                + groups + " groups, " + tris + " tris cached, " + WmoMaterials.Count + " materials; " + WmoInstance.StatsText();
        }

        internal static void DestroyAsset(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        internal static string FileName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            int i = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
            string f = i >= 0 ? p.Substring(i + 1) : p;
            return f.EndsWith(".wmo", StringComparison.OrdinalIgnoreCase) ? f.Substring(0, f.Length - 4) : f;
        }
    }
}
