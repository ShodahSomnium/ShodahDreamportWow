using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>What one texture unit (batch) draws: which triangles, which texture, which blend state.</summary>
    public sealed class M2BatchInfo
    {
        public int BatchIndex, SubmeshIndex, GeosetId;
        public int BlendMode;          // 0 Opaque 1 AlphaKey 2 Alpha 3 NoAlphaAdd 4 Add 5 Mod 6 Mod2x
        public int RenderFlags;        // 1 unlit, 2 unfogged, 4 two-sided, 8 billboard, 0x10 no depth write
        public int TextureIndex = -1;  // into M2Model.Textures
        public int TextureType;        // M2TextureDef.Type (0 = TextureFile)
        public string TextureFile;
        public uint TextureFlags;      // 1 wrap U, 2 wrap V
        public int UvSet;              // 0 or 1; -1 = environment (sphere) mapping, drawn with uv 0
        public int TextureCount;
        public float Alpha = 1f;       // first key of transparency x colour alpha
        public float R = 1f, G = 1f, B = 1f;
        public float ScrollU, ScrollV; // UV animation, units per second (linear translation tracks only)
        public int ColorIndex = -1, TransparencyIndex = -1, TextureTransformIndex = -1;   // resolved into M2Model.Colors / Transparencies / TextureTransforms
        public bool Animated;          // colour, transparency or texture transform changes over time (M2MaterialAnimator drives it)
        public bool AlphaAnimated;     // transparency / colour alpha animated (kept even when the first key is 0)
        public int PriorityPlane, MaterialLayer;
        public int[] Indices;          // mesh-local vertex indices, in Unity winding order
    }

    /// <summary>
    /// A render-ready mesh of one skin profile (pure, WoW model space, yards): compacted vertices of the visible batches,
    /// one index list per batch. Unity code converts positions/normals through WowSpace and uses the indices as they are.
    /// </summary>
    public sealed class M2MeshData
    {
        /// <summary>
        /// True = each triangle is emitted as (a, c, b) instead of the stored (a, b, c). MEASURED (harness probe ModelsParse,
        /// see the Models report): with the STORED order, after WowSpace.PointToUnity only 995 of 128,742 triangles of the 20
        /// player models (0.77%) and 301 of 27,074 of 209 doodads (1.11%) have Cross(b - a, c - a) agreeing with the stored
        /// vertex normals, i.e. the stored order is back-facing for Unity. So every triangle is flipped (99.2% / 98.9% agree).
        /// The M2 collision triangles behave the same (0 of N agree with the stored face normals unflipped): flipped too.
        /// </summary>
        public static readonly bool FlipWinding = true;

        public int VertexCount;
        public float[] Positions;      // 3 per vertex (WoW space)
        public float[] Normals;        // 3 per vertex
        public float[] Uv0, Uv1;       // 2 per vertex
        public byte[] BoneWeights;     // 4 per vertex
        public byte[] BoneIndices;     // 4 per vertex (M2 bone indices)
        public int[] SourceVertex;     // mesh vertex -> M2 vertex
        public readonly List<M2BatchInfo> Batches = new List<M2BatchInfo>();

        /// <summary>
        /// Builds the mesh of the skin's batches whose submesh geoset passes showGeoset (null = all). posedPositions/
        /// posedNormals (3 per M2 vertex, may be null) replace the bind-pose vertices (static doodads bake their rest pose).
        /// </summary>
        public static M2MeshData Build(M2Model m, M2Skin s, Func<int, bool> showGeoset, float[] posedPositions, float[] posedNormals)
        {
            return Build(m, s, showGeoset, posedPositions, posedNormals, null);
        }

        /// <summary>Lead 2026-10-11: showTexture (the batch's first texture file name, null for a replaceable / none) drops batches too.</summary>
        public static M2MeshData Build(M2Model m, M2Skin s, Func<int, bool> showGeoset, float[] posedPositions, float[] posedNormals, Func<string, bool> showTexture)
        {
            var d = new M2MeshData();
            var remap = new Dictionary<int, int>();
            var src = new List<int>();
            var order = new List<int>();
            for (int i = 0; i < s.Batches.Length; i++) order.Add(i);
            // Draw order: priority plane, then material layer, then file order (stable).
            order.Sort((a, b) =>
            {
                int c = s.Batches[a].PriorityPlane.CompareTo(s.Batches[b].PriorityPlane);
                if (c != 0) return c;
                c = s.Batches[a].MaterialLayer.CompareTo(s.Batches[b].MaterialLayer);
                return c != 0 ? c : a.CompareTo(b);
            });

            foreach (int bi in order)
            {
                var b = s.Batches[bi];
                if (b.SubmeshIndex < 0 || b.SubmeshIndex >= s.Submeshes.Length) continue;
                var sm = s.Submeshes[b.SubmeshIndex];
                if (sm.IndexCount < 3) continue;
                if (showGeoset != null && !showGeoset(sm.GeosetId)) continue;
                if (showTexture != null)
                {
                    int ti = b.TextureComboIndex >= 0 && b.TextureComboIndex < m.TextureCombos.Length ? m.TextureCombos[b.TextureComboIndex] : -1;
                    string tn = ti >= 0 && ti < m.Textures.Length ? m.Textures[ti].FileName : null;
                    if (!showTexture(tn)) continue;
                }
                var info = Describe(m, b, bi);
                info.GeosetId = sm.GeosetId;
                var idx = new int[sm.IndexCount];
                for (int k = 0; k < sm.IndexCount; k += 3)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int sv = s.Indices[sm.IndexStart + k + j];
                        int mv = s.VertexLookup[sv];
                        int local;
                        if (!remap.TryGetValue(mv, out local)) { local = src.Count; remap[mv] = local; src.Add(mv); }
                        idx[k + j] = local;
                    }
                    if (FlipWinding) { int t = idx[k + 1]; idx[k + 1] = idx[k + 2]; idx[k + 2] = t; }
                }
                info.Indices = idx;
                d.Batches.Add(info);
            }

            int n = src.Count;
            d.VertexCount = n;
            d.SourceVertex = src.ToArray();
            d.Positions = new float[n * 3]; d.Normals = new float[n * 3];
            d.Uv0 = new float[n * 2]; d.Uv1 = new float[n * 2];
            d.BoneWeights = new byte[n * 4]; d.BoneIndices = new byte[n * 4];
            float[] pos = posedPositions ?? m.Positions, nrm = posedNormals ?? m.Normals;
            for (int i = 0; i < n; i++)
            {
                int v = src[i];
                Array.Copy(pos, v * 3, d.Positions, i * 3, 3);
                Array.Copy(nrm, v * 3, d.Normals, i * 3, 3);
                Array.Copy(m.Uv0, v * 2, d.Uv0, i * 2, 2);
                Array.Copy(m.Uv1, v * 2, d.Uv1, i * 2, 2);
                Array.Copy(m.BoneWeights, v * 4, d.BoneWeights, i * 4, 4);
                Array.Copy(m.BoneIndices, v * 4, d.BoneIndices, i * 4, 4);
            }
            return d;
        }

        static short At(short[] a, int i) { return i >= 0 && i < a.Length ? a[i] : (short)-1; }

        /// <summary>Material/texture facts of one batch (lookups resolved through the M2's combo tables).</summary>
        public static M2BatchInfo Describe(M2Model m, M2Batch b, int batchIndex)
        {
            var info = new M2BatchInfo
            {
                BatchIndex = batchIndex, SubmeshIndex = b.SubmeshIndex, PriorityPlane = b.PriorityPlane,
                MaterialLayer = b.MaterialLayer, TextureCount = b.TextureCount
            };
            if (b.MaterialIndex >= 0 && b.MaterialIndex < m.Materials.Length)
            {
                info.BlendMode = m.Materials[b.MaterialIndex].BlendMode;
                info.RenderFlags = m.Materials[b.MaterialIndex].Flags;
            }
            int ti = At(m.TextureCombos, b.TextureComboIndex);
            if (ti >= 0 && ti < m.Textures.Length)
            {
                info.TextureIndex = ti;
                info.TextureType = m.Textures[ti].Type;
                info.TextureFile = m.Textures[ti].FileName;
                info.TextureFlags = m.Textures[ti].Flags;
            }
            int uv = At(m.TextureCoordCombos, b.TextureCoordComboIndex);
            info.UvSet = uv == 1 ? 1 : uv == 0 ? 0 : -1;

            float alpha = 1f;
            int tr = At(m.TransparencyCombos, b.TransparencyComboIndex);
            if (tr >= 0 && tr < m.Transparencies.Length)
            {
                alpha *= Clamp01(m.Transparencies[tr].FirstValue(1f));
                info.TransparencyIndex = tr;
                if (M2Emitters.IsAnimated(m.Transparencies[tr])) info.AlphaAnimated = true;
            }
            if (b.ColorIndex >= 0 && b.ColorIndex < m.Colors.Length)
            {
                var c = m.Colors[b.ColorIndex];
                info.ColorIndex = b.ColorIndex;
                if (M2Emitters.IsAnimated(c.Alpha)) info.AlphaAnimated = true;
                if (M2Emitters.IsAnimated(c.Color) && (info.RenderFlags & 1) != 0) info.Animated = true;
                alpha *= Clamp01(c.Alpha.FirstValue(1f));
                if ((info.RenderFlags & 1) != 0)
                {
                    var rgb = c.Color.FirstValue(new M2Vec3(1, 1, 1));
                    info.R = Clamp01(rgb.X); info.G = Clamp01(rgb.Y); info.B = Clamp01(rgb.Z);
                }
            }
            info.Alpha = alpha;

            int tt = At(m.TextureTransformCombos, b.TextureTransformComboIndex);
            if (tt >= 0 && tt < m.TextureTransforms.Length)
            {
                ScrollOf(m, m.TextureTransforms[tt], out info.ScrollU, out info.ScrollV);
                var x = m.TextureTransforms[tt];
                if (M2Emitters.IsAnimated(x.Translation) || M2Emitters.IsAnimated(x.Scale)) { info.TextureTransformIndex = tt; info.Animated = true; }
            }
            if (info.AlphaAnimated) info.Animated = true;
            return info;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        /// <summary>Linear UV scroll speed (units/s) of a texture transform's translation track (first sequence/global with 2+ keys).</summary>
        public static void ScrollOf(M2Model m, M2TextureTransform t, out float u, out float v)
        {
            u = 0f; v = 0f;
            var tr = t.Translation;
            if (tr == null) return;
            for (int s = 0; s < tr.Values.Length; s++)
            {
                var vals = tr.Values[s]; var times = tr.Times[s];
                if (vals == null || vals.Length < 2 || times == null || times.Length < 2) continue;
                int dt = times[times.Length - 1] - times[0];
                if (dt <= 0) continue;
                u = (vals[vals.Length - 1].X - vals[0].X) * 1000f / dt;
                v = (vals[vals.Length - 1].Y - vals[0].Y) * 1000f / dt;
                return;
            }
        }

        /// <summary>
        /// Winding measurement: fraction of triangles (as they will be emitted) whose geometric normal in Unity space agrees
        /// with the average of the three stored vertex normals. Unity space = WowSpace.PointToUnity/DirToUnity: (x, y, z) ->
        /// (-y, z, x) (same formula, scale irrelevant). Unity's front face is clockwise, i.e. Cross(b - a, c - a) points out
        /// of the front, so a correct triangle has dot(Cross(b - a, c - a), n) &gt; 0. Degenerate triangles are skipped.
        /// </summary>
        public static void MeasureWinding(M2MeshData d, out int agree, out int total)
        {
            agree = 0; total = 0;
            foreach (var b in d.Batches)
            {
                var ix = b.Indices;
                for (int k = 0; k + 2 < ix.Length; k += 3)
                {
                    int a = ix[k], bb = ix[k + 1], c = ix[k + 2];
                    float ax = -d.Positions[a * 3 + 1], ay = d.Positions[a * 3 + 2], az = d.Positions[a * 3];
                    float bx = -d.Positions[bb * 3 + 1], by = d.Positions[bb * 3 + 2], bz = d.Positions[bb * 3];
                    float cx = -d.Positions[c * 3 + 1], cy = d.Positions[c * 3 + 2], cz = d.Positions[c * 3];
                    float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
                    float gx = uy * vz - uz * vy, gy = uz * vx - ux * vz, gz = ux * vy - uy * vx;
                    float nx = 0, ny = 0, nz = 0;
                    foreach (int q in new[] { a, bb, c })
                    {
                        nx += -d.Normals[q * 3 + 1]; ny += d.Normals[q * 3 + 2]; nz += d.Normals[q * 3];
                    }
                    float glen = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz), nlen = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (glen < 1e-9f || nlen < 1e-6f) continue;
                    total++;
                    if (gx * nx + gy * ny + gz * nz > 0f) agree++;
                }
            }
        }
    }
}
