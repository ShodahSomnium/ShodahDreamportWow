using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>One submesh of a group mesh: the triangles of one MOBA batch, already in Unity winding.</summary>
    public struct WmoSubmesh
    {
        public int Material;      // index into WmoRoot.Materials
        public int BatchType;     // 0 trans, 1 int, 2 ext (MOGP batch counts, in that order)
        public int[] Indices;     // into the group mesh's vertices, 3 per triangle, order (a, c, b) of the file's (a, b, c)
    }

    /// <summary>Everything a group needs to become Unity meshes, still in the WMO frame (yards, z up). Pure data.</summary>
    public sealed class WmoGroupGeometry
    {
        public int GroupIndex;
        public string Name = "";
        public uint Flags;
        public bool Exterior, Interior, HasMocv;

        // Render mesh: vertices are copied per batch range [minIndex, maxIndex] so each vertex belongs to one batch.
        public int VertexCount;
        public float[] Positions;   // xyz
        public float[] Normals;     // xyz
        public float[] Uv0;         // uv (MOTV 1; zeros when the group has none)
        public float[] Uv1;         // uv (MOTV 2) or null when no batch of the group uses a two-layer material
        public float[] Blend;       // per vertex 0..1 (alpha of the 2nd MOCV) or null (same condition)
        /// <summary>Per vertex R, G, B, A: rgb = the vertex's local light term (MOCV after the client's fix-up, plus the interior
        /// ambient where the unified path adds it), a = weight of the exterior light (sun + ambient). See WmoGeometry.</summary>
        public byte[] Light;
        public List<WmoSubmesh> Submeshes = new List<WmoSubmesh>();
        public int RenderTriangles;

        // Collision mesh (collidable triangles only; own compact vertex list).
        public float[] CollisionPositions;
        public int[] CollisionIndices;

        // Liquid (null when none): a grid mesh of the existing tiles.
        public float[] LiquidPositions;
        public float[] LiquidUv;
        public int[] LiquidIndices;
        public int LiquidTypeId;
        public int LiquidTiles;
    }

    /// <summary>
    /// WMO group -> mesh data (pure). Rules and where they come from:
    ///
    /// Winding: the file's triangles are counter-clockwise front faces in WoW's right-handed frame; through WowSpace's
    /// (x, y, z) -> (-y, z, x) the stored order agrees with MONR for only 0.0-0.2% of rendered triangles (probe WmoCheck on
    /// NSABBEY, the Goldshire inn, Stormwind, Dalaran), so every triangle is emitted as (a, c, b).
    ///
    /// Rendered triangles: the client draws MOBA batches (wowdev WMO/Rendering: RenderGroup_* loop over batches); triangles of a
    /// batch with MOPY material 0xFF are dropped (collision-only; measured: none exist inside batches).
    ///
    /// Collision: SMOPoly::isCollidable = F_COLLISION (0x08) || (F_RENDER (0x20) && !F_DETAIL (0x04)) over ALL triangles, so
    /// material-0xFF collision faces collide without rendering and DETAIL render faces (17.5 M of 28.9 M rendered triangles) do not.
    ///
    /// Lighting (3.3.5a, wowdev WMO/Rendering + the "Unified Rendering" post): the WMO vertex shader computes
    ///   light = saturate(dirColor * saturate(n.-dir) + ambColor) * 0.5 + vertexColor ; light = saturate(light + emissive)
    /// and the pixel shader outputs texture * light * 2 (all in gamma space). Lighting modes: exterior (sun + ambient from the
    /// day/night tables), interior (dir 0, amb = MOHD ambColor in the unified path), window (window colours, approximated by
    /// the exterior mode here), unlit (both 0). MOCV first goes through CMapObjGroup::FixColorVertexAlpha (3.3.5a version,
    /// only when MOHD flag 0x8 is clear): vertices at or after the first int batch get rgb = min(255, (c + a*c/64) / 2), a = 255;
    /// trans-batch vertices get rgb = c / 2 and keep a. Trans batches are drawn twice (exterior light x a, then interior light
    /// x (1 - a), InvSrcAlphaAdd) = one blend by the vertex alpha.
    /// This builder folds everything per vertex into Light: rgb = the vertexColor term (+ 0.5 x interior ambient where it applies),
    /// a = the weight w of the exterior term, so the shader needs only: light = 0.5 * w * exterior + rgb (see SomniumWoW/Wmo).
    ///   - unlit material: w 0, rgb = fixed MOCV (or 0.5 = texture as is when the group has no MOCV - not documented, chosen).
    ///   - group without MOCV: w 1, rgb 0 (classic path: RenderGroup_Ext lights MOCV-less groups; interior groups without MOCV,
    ///     350 of 6222, are lit the same way - an approximation).
    ///   - trans batch: w = a / 255, rgb = fixed MOCV (+ (1 - w) * 0.5 * ambColor in the unified path).
    ///   - unified path (MOHD 0x2), int/ext batch: exterior branch when the group has EXTERIOR|EXTERIOR_LIT or the material has
    ///     F_WINDOW (w 1, rgb = fixed MOCV), else interior (w 0, rgb = fixed MOCV + 0.5 * ambColor).
    ///   - classic path, int batch: w 0 (1 with F_WINDOW), rgb = fixed MOCV (the interior ambient is baked in: mean MOCV 88.6
    ///     classic vs 28.5 unified). Ext batch: w 1, rgb 0 (the post: classic MOCV carries the interior ambient and was not used on
    ///     exterior batches; that is why unified rendering was added).
    ///
    /// Liquid: MLIQ grid, tile (x, y) = Tiles[y * XTiles + x] with x along WMO x (probe WmoLiquidLayout), low 4 bits 0xF = no
    /// liquid; vertex (x, y) at corner + (x, y) * 4.1667 yd, height Heights[y * XVerts + x]. Type: see LiquidTypeOf.
    /// </summary>
    public static class WmoGeometry
    {
        /// <summary>Builds one group's mesh data. Never throws for inconsistent data: bad ranges are clamped/skipped and counted.</summary>
        public static WmoGroupGeometry Build(WmoRoot root, WmoGroup g, int groupIndex)
        {
            var o = new WmoGroupGeometry
            {
                GroupIndex = groupIndex,
                Name = groupIndex < root.Groups.Length ? root.Groups[groupIndex].Name : "",
                Flags = g.Flags,
                Exterior = g.IsExterior,
                Interior = g.IsInterior,
                HasMocv = g.Colors != null && g.Colors.Length >= g.VertexCount * 4,
            };
            int nv = g.VertexCount;
            bool haveNormals = g.Normals.Length >= nv * 3;
            bool haveUv0 = g.Uv0 != null && g.Uv0.Length >= nv * 2;

            // ---- FixColorVertexAlpha (3.3.5a) on a copy of MOCV
            byte[] fixedCv = null;
            if (o.HasMocv)
            {
                fixedCv = (byte[])g.Colors.Clone();   // B, G, R, A
                if (!root.NoFixVertexAlpha)
                {
                    int intBatchStart = 0;
                    if (g.TransBatchCount > 0 && g.TransBatchCount <= g.Batches.Length) intBatchStart = g.Batches[g.TransBatchCount - 1].MaxIndex + 1;
                    for (int i = 0; i < nv; i++)
                    {
                        int p = i * 4;
                        if (i >= intBatchStart)
                        {
                            int a = fixedCv[p + 3];
                            for (int k = 0; k < 3; k++)
                            {
                                int c = fixedCv[p + k];
                                int v = (c + a * c / 64) / 2;
                                fixedCv[p + k] = (byte)(v > 255 ? 255 : v);
                            }
                            fixedCv[p + 3] = 255;
                        }
                        else
                        {
                            for (int k = 0; k < 3; k++) fixedCv[p + k] = (byte)(fixedCv[p + k] / 2);
                        }
                    }
                }
            }
            int ambR = (int)((root.AmbientColor >> 16) & 0xFF), ambG = (int)((root.AmbientColor >> 8) & 0xFF), ambB = (int)(root.AmbientColor & 0xFF);

            // ---- does any batch use a two-layer material?
            bool twoLayer = false;
            foreach (var b in g.Batches)
                if (b.Material < root.Materials.Length && root.Materials[b.Material].Shader == 6) twoLayer = true;
            bool haveUv1 = twoLayer && g.Uv1 != null && g.Uv1.Length >= nv * 2;
            bool haveBlend = twoLayer && g.Colors2 != null && g.Colors2.Length >= nv * 4;

            // ---- count output vertices (one copy per batch range)
            int total = 0;
            var ranges = new List<int[]>();   // batchIndex, min, max
            for (int bi = 0; bi < g.Batches.Length; bi++)
            {
                var b = g.Batches[bi];
                int mn = Math.Max(0, b.MinIndex), mx = Math.Min(nv - 1, b.MaxIndex);
                if (mx < mn || b.IndexCount < 3) continue;
                ranges.Add(new[] { bi, mn, mx });
                total += mx - mn + 1;
            }
            o.VertexCount = total;
            o.Positions = new float[total * 3];
            o.Normals = new float[total * 3];
            o.Uv0 = new float[total * 2];
            o.Light = new byte[total * 4];
            if (twoLayer) { o.Uv1 = new float[total * 2]; o.Blend = new float[total]; }

            int outV = 0;
            foreach (var rg in ranges)
            {
                int bi = rg[0], mn = rg[1], mx = rg[2];
                var b = g.Batches[bi];
                int btype = bi < g.TransBatchCount ? 0 : bi < g.TransBatchCount + g.IntBatchCount ? 1 : 2;
                WmoMaterial mat = b.Material < root.Materials.Length ? root.Materials[b.Material] : null;
                int baseV = outV;
                for (int i = mn; i <= mx; i++, outV++)
                {
                    Array.Copy(g.Positions, i * 3, o.Positions, outV * 3, 3);
                    if (haveNormals) Array.Copy(g.Normals, i * 3, o.Normals, outV * 3, 3);
                    else o.Normals[outV * 3 + 2] = 1f;
                    if (haveUv0) { o.Uv0[outV * 2] = g.Uv0[i * 2]; o.Uv0[outV * 2 + 1] = g.Uv0[i * 2 + 1]; }
                    if (twoLayer)
                    {
                        if (haveUv1) { o.Uv1[outV * 2] = g.Uv1[i * 2]; o.Uv1[outV * 2 + 1] = g.Uv1[i * 2 + 1]; }
                        else if (haveUv0) { o.Uv1[outV * 2] = g.Uv0[i * 2]; o.Uv1[outV * 2 + 1] = g.Uv0[i * 2 + 1]; }
                        o.Blend[outV] = haveBlend ? g.Colors2[i * 4 + 3] / 255f : 1f;
                    }
                    VertexLight(root, g, o.HasMocv, fixedCv, i, btype, mat, ambR, ambG, ambB, o.Light, outV * 4);
                }
                // indices of this batch, remapped, reversed winding, material-0xFF triangles dropped
                int tStart = b.StartIndex / 3, tEnd = Math.Min(g.TriangleCount, (b.StartIndex + b.IndexCount) / 3);
                var idx = new List<int>(Math.Max(0, (tEnd - tStart) * 3));
                for (int t = tStart; t < tEnd; t++)
                {
                    if (t < g.PolyMaterials.Length && g.PolyMaterials[t] == 0xFF) continue;
                    int ia = g.Indices[t * 3], ib = g.Indices[t * 3 + 1], ic = g.Indices[t * 3 + 2];
                    if (ia < mn || ia > mx || ib < mn || ib > mx || ic < mn || ic > mx) continue;
                    idx.Add(baseV + ia - mn); idx.Add(baseV + ic - mn); idx.Add(baseV + ib - mn);
                }
                if (idx.Count == 0) continue;
                o.RenderTriangles += idx.Count / 3;
                o.Submeshes.Add(new WmoSubmesh { Material = b.Material, BatchType = btype, Indices = idx.ToArray() });
            }

            BuildCollision(g, o);
            BuildLiquid(root, g, o);
            return o;
        }

        static void VertexLight(WmoRoot root, WmoGroup g, bool hasMocv, byte[] cv, int i, int btype, WmoMaterial mat,
            int ambR, int ambG, int ambB, byte[] dst, int d)
        {
            int r = 0, gr = 0, b = 0, w = 255;
            if (mat != null && mat.Unlit)
            {
                w = 0;
                if (hasMocv) { r = cv[i * 4 + 2]; gr = cv[i * 4 + 1]; b = cv[i * 4]; }
                else { r = gr = b = 128; }
            }
            else if (!hasMocv)
            {
                w = 255;
            }
            else
            {
                int cr = cv[i * 4 + 2], cg = cv[i * 4 + 1], cb = cv[i * 4], ca = cv[i * 4 + 3];
                bool window = mat != null && mat.Window;
                if (btype == 0)
                {
                    w = ca;
                    r = cr; gr = cg; b = cb;
                    if (root.UnifiedRender)
                    {
                        int inv = 255 - ca;
                        r += ambR * inv / 510; gr += ambG * inv / 510; b += ambB * inv / 510;
                    }
                }
                else if (root.UnifiedRender)
                {
                    bool ext = g.ExteriorLit || window;
                    w = ext ? 255 : 0;
                    r = cr; gr = cg; b = cb;
                    if (!ext) { r += ambR / 2; gr += ambG / 2; b += ambB / 2; }
                }
                else if (btype == 1)
                {
                    w = window ? 255 : 0;
                    r = cr; gr = cg; b = cb;
                }
                else
                {
                    w = 255;
                }
            }
            dst[d] = (byte)Math.Min(255, r);
            dst[d + 1] = (byte)Math.Min(255, gr);
            dst[d + 2] = (byte)Math.Min(255, b);
            dst[d + 3] = (byte)w;
        }

        static void BuildCollision(WmoGroup g, WmoGroupGeometry o)
        {
            int nv = g.VertexCount;
            var remap = new int[nv];
            for (int i = 0; i < nv; i++) remap[i] = -1;
            var pos = new List<float>();
            var idx = new List<int>();
            int nt = Math.Min(g.TriangleCount, g.PolyFlags.Length);
            for (int t = 0; t < nt; t++)
            {
                if (!WmoGroup.IsCollidable(g.PolyFlags[t])) continue;
                int ia = g.Indices[t * 3], ib = g.Indices[t * 3 + 1], ic = g.Indices[t * 3 + 2];
                if (ia >= nv || ib >= nv || ic >= nv) continue;
                idx.Add(Remap(ia, remap, g.Positions, pos));
                idx.Add(Remap(ic, remap, g.Positions, pos));
                idx.Add(Remap(ib, remap, g.Positions, pos));
            }
            o.CollisionPositions = pos.ToArray();
            o.CollisionIndices = idx.ToArray();
        }

        static int Remap(int i, int[] remap, float[] src, List<float> pos)
        {
            if (remap[i] < 0)
            {
                remap[i] = pos.Count / 3;
                pos.Add(src[i * 3]); pos.Add(src[i * 3 + 1]); pos.Add(src[i * 3 + 2]);
            }
            return remap[i];
        }

        static void BuildLiquid(WmoRoot root, WmoGroup g, WmoGroupGeometry o)
        {
            var l = g.Liquid;
            if (l == null || l.XTiles <= 0 || l.YTiles <= 0 || l.XVerts != l.XTiles + 1 || l.YVerts != l.YTiles + 1) return;
            float T = WmoLiquid.TileSize;
            var vmap = new int[l.XVerts * l.YVerts];
            for (int i = 0; i < vmap.Length; i++) vmap[i] = -1;
            var pos = new List<float>();
            var uv = new List<float>();
            var idx = new List<int>();
            int firstType = -1;
            for (int y = 0; y < l.YTiles; y++)
                for (int x = 0; x < l.XTiles; x++)
                {
                    if (!l.TileExists(x, y)) continue;
                    if (firstType < 0) firstType = l.Tiles[y * l.XTiles + x] & 0xF;
                    int v00 = LV(l, x, y, vmap, pos, uv, T), v10 = LV(l, x + 1, y, vmap, pos, uv, T);
                    int v01 = LV(l, x, y + 1, vmap, pos, uv, T), v11 = LV(l, x + 1, y + 1, vmap, pos, uv, T);
                    // WoW frame, seen from above (+z): (x,y) (x+1,y) (x+1,y+1) is counter-clockwise; reversed for Unity like all WMO
                    // triangles => (v00, v11, v10) and (v00, v01, v11) face up in Unity.
                    idx.Add(v00); idx.Add(v11); idx.Add(v10);
                    idx.Add(v00); idx.Add(v01); idx.Add(v11);
                    o.LiquidTiles++;
                }
            if (idx.Count == 0) return;
            o.LiquidPositions = pos.ToArray();
            o.LiquidUv = uv.ToArray();
            o.LiquidIndices = idx.ToArray();
            o.LiquidTypeId = LiquidTypeOf(root, g, firstType < 0 ? 0 : firstType);
        }

        static int LV(WmoLiquid l, int x, int y, int[] vmap, List<float> pos, List<float> uv, float T)
        {
            int k = y * l.XVerts + x;
            if (vmap[k] < 0)
            {
                vmap[k] = pos.Count / 3;
                pos.Add(l.CornerX + x * T); pos.Add(l.CornerY + y * T); pos.Add(l.Heights[k]);
                // UV in tiles along WMO x and y (one texture repeat per tile, as ADT water tiles).
                uv.Add(x); uv.Add(y);
            }
            return vmap[k];
        }

        /// <summary>
        /// LiquidType.dbc id of a group's MLIQ (wowdev WMO#MLIQ, checked against the MLIQ material textures by probe WmoLiquids):
        /// MOHD flag 0x4 set: groupLiquid &lt; 21 -> WMO variant of kind (groupLiquid - 1) &amp; 3, else groupLiquid itself.
        /// Otherwise: groupLiquid 15 -> WMO variant of kind (first tile's legacy type &amp; 3) (measured: 0/4 water, 2/6 lava,
        /// 3/7 slime); groupLiquid &lt; 20 -> kind groupLiquid &amp; 3; else groupLiquid + 1.
        /// Kinds: 0 water -> 13 'WMO Water' (14 'WMO Ocean' when group flag 0x80000), 1 ocean -> 14, 2 magma -> 19, 3 slime -> 20.
        /// </summary>
        public static int LiquidTypeOf(WmoRoot root, WmoGroup g, int firstTileType)
        {
            int gl = g.GroupLiquid;
            if (root.LiquidTypeIsDbcId)
            {
                if (gl < 21) return ToWmoLiquid((gl - 1) & 3, g);
                return gl;
            }
            if (gl == 15) return ToWmoLiquid(firstTileType & 3, g);
            if (gl < 20) return ToWmoLiquid(gl & 3, g);
            return gl + 1;
        }

        static int ToWmoLiquid(int kind, WmoGroup g)
        {
            switch (kind)
            {
                case 0: return (g.Flags & WmoGroup.G_OCEAN) != 0 ? 14 : 13;
                case 1: return 14;
                case 2: return 19;
                default: return 20;
            }
        }
    }
}
