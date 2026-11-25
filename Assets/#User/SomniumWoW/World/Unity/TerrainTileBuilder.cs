using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>Terrain vertex (48 bytes): position, normal, MCCV colour, uv0 = (chunk-local u, v, chunk indexX, indexY),
    /// layers = texture-array slots of the chunk's 4 layers (255 = none).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TerrainVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Color32 Color;
        public Vector4 Uv0;
        public Color32 Layers;

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4),
        };
    }

    /// <summary>A placed M2 doodad (Unity space).</summary>
    internal struct DoodadPlacement
    {
        public uint UniqueId;
        public string Path;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
    }

    /// <summary>A placed WMO (Unity space) with its world bounds (Unity space, for distance tests).</summary>
    internal struct WmoPlacement
    {
        public uint UniqueId;
        public string Path;
        public Vector3 Position;
        public Quaternion Rotation;
        public int DoodadSet;
        public Bounds Bounds;
    }

    /// <summary>One liquid mesh of a tile (all instances of one LiquidType id), Unity space relative to the tile origin.</summary>
    internal sealed class WaterBuild
    {
        public int LiquidType;
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Color32> Colors = new List<Color32>(); // a = depth 0..255
        public readonly List<int> Indices = new List<int>();
    }

    /// <summary>Everything the worker thread prepares for a tile; the main thread turns it into Unity objects.</summary>
    internal sealed class TileBuild
    {
        public int TileX, TileY;
        public bool Full;
        public AdtFile Adt;
        public Vector3 Origin;
        /// <summary>The origin frame every Unity position of this build is in (taken once when the job was queued; the main
        /// thread adds Frame.Offset to place it in the frame current then - PLAN4 floating origin).</summary>
        public WowSpace.Frame Frame = WowSpace.Current;
        public TerrainVertex[] Vertices;
        public ushort[] Indices;
        public int IndexCount;
        public Bounds LocalBounds;
        public byte[] Alpha;          // 1024 x 1024 RGBA32: RGB = layer 1..3 alpha, A = 255 - 255 * shadow
        public int[] Slots;           // MTEX index -> array slot (-1 none)
        public readonly List<WaterBuild> Water = new List<WaterBuild>();
        public readonly List<DoodadPlacement> Doodads = new List<DoodadPlacement>();
        public readonly List<WmoPlacement> Wmos = new List<WmoPlacement>();
        public int Chunks, Layers, HoleCells, LiquidInstances;
        public bool WindingChecked, WindingFlipped;
        public long ReadMs, ParseMs, BuildMs;
        public string Error;
    }

    /// <summary>Worker-thread tile preparation (no Unity objects touched: Vector3/Quaternion/Bounds are plain structs).</summary>
    internal static class TerrainTileBuilder
    {
        public const int AtlasSize = 1024;

        /// <summary>World-space placement rotation (MapConst.PlacementRotation) -> Unity rotation of a model built with
        /// WowSpace.PointToUnity. Ru = C M C^T with C: (x, y, z) -> (-y, z, x).</summary>
        public static Quaternion PlacementToUnity(float rx, float ry, float rz, float[] m)
        {
            MapConst.PlacementRotation(rx, ry, rz, m);
            float r00 = m[4], r01 = -m[5], r02 = -m[3];
            float r10 = -m[7], r11 = m[8], r12 = m[6];
            float r20 = -m[1], r21 = m[2], r22 = m[0];
            return FromMatrix(r00, r01, r02, r10, r11, r12, r20, r21, r22);
        }

        /// <summary>Rotation matrix (column-vector convention, as Unity's Matrix4x4) -> quaternion.</summary>
        public static Quaternion FromMatrix(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
        {
            float tr = m00 + m11 + m22;
            float x, y, z, w;
            if (tr > 0f)
            {
                float s = (float)Math.Sqrt(tr + 1f) * 2f;
                w = 0.25f * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s; x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s;
            }
            else
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s;
            }
            float n = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (n < 1e-6f) return Quaternion.identity;
            return new Quaternion(x / n, y / n, z / n, w / n);
        }

        /// <summary>Placements (doodads only when withDoodads) of a parsed tile, converted to Unity space.</summary>
        public static void Placements(TileBuild tb, int mapId, bool withDoodads)
        {
            var a = tb.Adt;
            var m = new float[9];
            if (withDoodads)
            {
                foreach (var d in a.Doodads)
                {
                    if (d.NameIndex < 0 || d.NameIndex >= a.M2Names.Length) continue;
                    float wx, wy, wz; MapConst.PlacementToWorld(d.PX, d.PY, d.PZ, out wx, out wy, out wz);
                    var p = new DoodadPlacement();
                    p.UniqueId = d.UniqueId;
                    p.Path = a.M2Names[d.NameIndex];
                    p.Position = tb.Frame.ToUnity(wx, wy, wz);
                    p.Rotation = PlacementToUnity(d.RX, d.RY, d.RZ, m);
                    p.Scale = d.Scale / 1024f;
                    tb.Doodads.Add(p);
                }
            }
            foreach (var w in a.Wmos)
                tb.Wmos.Add(ToPlacement(w, a.WmoNames, tb.Frame, m));
        }

        public static WmoPlacement ToPlacement(AdtWmoDef w, string[] names, WowSpace.Frame frame, float[] m)
        {
            var p = new WmoPlacement();
            p.UniqueId = w.UniqueId;
            p.Path = w.NameIndex >= 0 && w.NameIndex < names.Length ? names[w.NameIndex] : null;
            float wx, wy, wz; MapConst.PlacementToWorld(w.PX, w.PY, w.PZ, out wx, out wy, out wz);
            p.Position = frame.ToUnity(wx, wy, wz);
            p.Rotation = PlacementToUnity(w.RX, w.RY, w.RZ, m);
            p.DoodadSet = w.DoodadSet;
            // extents are placement space: world X = Z0 - p.z, world Y = Z0 - p.x, world Z = p.y
            var a = frame.ToUnity(MapConst.ZeroPoint - w.MinZ, MapConst.ZeroPoint - w.MinX, w.MinY);
            var b = frame.ToUnity(MapConst.ZeroPoint - w.MaxZ, MapConst.ZeroPoint - w.MaxX, w.MaxY);
            var bounds = new Bounds(a, Vector3.zero);
            bounds.Encapsulate(b);
            p.Bounds = bounds;
            return p;
        }

        /// <summary>Terrain mesh data, alpha atlas and water of a fully parsed tile. slots = array slot per MTEX index.</summary>
        public static void BuildTerrain(TileBuild tb, int mapId)
        {
            var a = tb.Adt;
            // (tile corner no longer used for vertices)
            tb.Origin = Vector3.zero; // absolute vertices (see below)
            var verts = new TerrainVertex[256 * 145];
            var idx = new ushort[256 * 256 * 3];
            var alpha = new byte[AtlasSize * AtlasSize * 4];
            int ic = 0;
            Vector3 bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), bmax = -bmin;
            for (int ci = 0; ci < 256; ci++)
            {
                var c = a.Chunks[ci];
                int vbase = ci * 145;
                if (c == null)
                {
                    for (int k = 0; k < 145; k++) verts[vbase + k].Layers = new Color32(255, 255, 255, 255);
                    continue;
                }
                tb.Chunks++;
                tb.Layers += c.LayerCount;
                var layers = new Color32(255, 255, 255, 255);
                for (int l = 0; l < c.LayerCount; l++)
                {
                    int tid = c.Layers[l].TextureId;
                    int slot = tid >= 0 && tid < tb.Slots.Length ? tb.Slots[tid] : -1;
                    byte s = (byte)(slot < 0 ? 255 : slot);
                    if (l == 0) layers.r = s; else if (l == 1) layers.g = s; else if (l == 2) layers.b = s; else layers.a = s;
                }
                if (layers.r == 255) layers.r = 0; // a chunk without a readable base texture still draws (slot 0)
                // vertices. Crack-free: vertices shared by neighbouring chunks/tiles must be bit-identical, so X/Y come from
                // the map-wide vertex grid (integer index * UnitSize, the same float maths in every chunk and tile), a
                // shared edge vertex takes height/normal/colour from its south-east-most owner chunk (measured: 66 of 8640
                // edge heights differ by up to 8e-6 yd between chunks, 0 of 576 between tiles), and every tile mesh uses
                // Unity coordinates of its build frame (tb.Frame) under a transform at that frame's integer-metre offset from the
                // current origin (iteration 4 floating origin): v + integer adds exactly in the GPU's object-to-world step, so tiles
                // built in different frames still meet bit-identically (the old per-tile origins were fractional and did not).
                int gRow0 = (tb.TileY * 16 + c.IndexY) * 8, gCol0 = (tb.TileX * 16 + c.IndexX) * 8;
                for (int k = 0; k < 145; k++)
                {
                    int rowPair = k / 17, inRow = k % 17;
                    bool inner = inRow >= 9;
                    float r = inner ? rowPair + 0.5f : rowPair;
                    float col = inner ? inRow - 9 + 0.5f : inRow;
                    AdtChunk oc = c; int ok = k;
                    if (!inner) Canonical(a, c, rowPair, inRow, out oc, out ok);
                    float gr = inner ? gRow0 + rowPair + 0.5f : gRow0 + rowPair;
                    float gc = inner ? gCol0 + (inRow - 9) + 0.5f : gCol0 + inRow;
                    float wx = MapConst.ZeroPoint - gr * MapConst.UnitSize, wy = MapConst.ZeroPoint - gc * MapConst.UnitSize;
                    float wz = oc.Z + oc.Heights[ok];
                    var v = new TerrainVertex();
                    v.Position = tb.Frame.ToUnity(wx, wy, wz);
                    float nx, ny, nz; AdtFile.NormalToWorld(oc.Normals, ok, out nx, out ny, out nz);
                    var n = WowSpace.DirToUnity(nx, ny, nz);
                    float nl = n.magnitude;
                    v.Normal = nl > 1e-4f ? n / nl : Vector3.up;
                    if (oc.Mccv != null) v.Color = new Color32(oc.Mccv[ok * 4 + 2], oc.Mccv[ok * 4 + 1], oc.Mccv[ok * 4], 255);
                    else if (c.Mccv != null) v.Color = new Color32(c.Mccv[k * 4 + 2], c.Mccv[k * 4 + 1], c.Mccv[k * 4], 255);
                    else v.Color = new Color32(127, 127, 127, 255);
                    v.Uv0 = new Vector4(col / 8f, r / 8f, c.IndexX, c.IndexY);
                    v.Layers = layers;
                    verts[vbase + k] = v;
                    bmin = Vector3.Min(bmin, v.Position); bmax = Vector3.Max(bmax, v.Position);
                }
                // triangles: 4 per cell around the inner vertex; order checked below
                for (int row = 0; row < 8; row++)
                    for (int cc = 0; cc < 8; cc++)
                    {
                        if (AdtFile.IsHole(c.Holes, row, cc)) { tb.HoleCells++; continue; }
                        ushort C = (ushort)(vbase + AdtFile.Inner(row, cc));
                        ushort tl = (ushort)(vbase + AdtFile.Outer(row, cc)), tr = (ushort)(vbase + AdtFile.Outer(row, cc + 1));
                        ushort bl = (ushort)(vbase + AdtFile.Outer(row + 1, cc)), br = (ushort)(vbase + AdtFile.Outer(row + 1, cc + 1));
                        idx[ic++] = C; idx[ic++] = tl; idx[ic++] = tr;
                        idx[ic++] = C; idx[ic++] = tr; idx[ic++] = br;
                        idx[ic++] = C; idx[ic++] = br; idx[ic++] = bl;
                        idx[ic++] = C; idx[ic++] = bl; idx[ic++] = tl;
                    }
                // alpha atlas
                for (int y = 0; y < 64; y++)
                {
                    int row0 = ((c.IndexY * 64 + y) * AtlasSize + c.IndexX * 64) * 4;
                    for (int x = 0; x < 64; x++)
                    {
                        int o = row0 + x * 4, s = y * 64 + x;
                        alpha[o] = c.Alpha[1] != null ? c.Alpha[1][s] : (byte)0;
                        alpha[o + 1] = c.Alpha[2] != null ? c.Alpha[2][s] : (byte)0;
                        alpha[o + 2] = c.Alpha[3] != null ? c.Alpha[3][s] : (byte)0;
                        alpha[o + 3] = c.Shadow != null && c.Shadow[s] != 0 ? (byte)0 : (byte)255;
                    }
                }
            }
            // Winding: Unity front faces are clockwise seen from the front, i.e. Cross(b - a, c - a) points at the viewer.
            // Measure it on the first non-degenerate triangle instead of assuming; flip every triangle if it points down.
            for (int t = 0; t + 2 < ic; t += 3)
            {
                var p0 = verts[idx[t]].Position; var p1 = verts[idx[t + 1]].Position; var p2 = verts[idx[t + 2]].Position;
                var cr = Vector3.Cross(p1 - p0, p2 - p0);
                if (cr.sqrMagnitude < 1e-8f) continue;
                tb.WindingChecked = true;
                if (cr.y < 0f)
                {
                    tb.WindingFlipped = true;
                    for (int u = 0; u + 2 < ic; u += 3) { ushort tmp = idx[u + 1]; idx[u + 1] = idx[u + 2]; idx[u + 2] = tmp; }
                }
                break;
            }
            tb.Vertices = verts;
            tb.Indices = idx;
            tb.IndexCount = ic;
            tb.Alpha = alpha;
            if (bmin.x > bmax.x) { bmin = Vector3.zero; bmax = Vector3.one; }
            var bounds = new Bounds((bmin + bmax) * 0.5f, bmax - bmin);
            tb.LocalBounds = bounds;
            BuildWater(tb, mapId);
        }

        /// <summary>MH2O (or, without MH2O, legacy MCLQ) -> one vertex/index list per LiquidType id. Depth for the shading:
        /// the stored depth byte when present, else (surface - terrain) from our own heights (0..10 yards -> 0..255).</summary>
        static void BuildWater(TileBuild tb, int mapId)
        {
            var a = tb.Adt;
            // water vertices are absolute Unity coordinates like the terrain
            var byType = new Dictionary<int, WaterBuild>();
            const float UvCells = 4f; // texture repeat every 4 liquid cells (noggit3 liquid_layer default_uv = vertex / 4)
            if (a.HasMh2o && a.Liquids != null)
            {
                for (int ci = 0; ci < 256; ci++)
                {
                    var list = a.Liquids[ci];
                    var c = a.Chunks[ci];
                    if (list == null || c == null) continue;
                    foreach (var li in list)
                    {
                        WaterBuild wb;
                        if (!byType.TryGetValue(li.LiquidType, out wb)) { wb = new WaterBuild(); wb.LiquidType = li.LiquidType; byType[li.LiquidType] = wb; }
                        tb.LiquidInstances++;
                        int w1 = li.Width + 1;
                        int baseV = wb.Positions.Count;
                        for (int vy = 0; vy <= li.Height; vy++)
                            for (int vx = 0; vx <= li.Width; vx++)
                            {
                                float row = li.YOffset + vy, col = li.XOffset + vx;
                                float wx = c.X - row * MapConst.UnitSize, wy = c.Y - col * MapConst.UnitSize;
                                float h = li.HeightAt(vx, vy);
                                wb.Positions.Add(tb.Frame.ToUnity(wx, wy, h));
                                wb.Uvs.Add(new Vector2(-wy / (UvCells * MapConst.UnitSize), -wx / (UvCells * MapConst.UnitSize)));
                                byte depth;
                                if (li.Depths != null) depth = li.Depths[vy * w1 + vx];
                                else depth = DepthFromTerrain(a, wx, wy, h);
                                wb.Colors.Add(new Color32(255, 255, 255, depth));
                            }
                        for (int y = 0; y < li.Height; y++)
                            for (int x = 0; x < li.Width; x++)
                            {
                                if (!li.CellExists(x, y)) continue;
                                int tl = baseV + y * w1 + x, tr = tl + 1, bl = tl + w1, br = bl + 1;
                                wb.Indices.Add(tl); wb.Indices.Add(tr); wb.Indices.Add(br);
                                wb.Indices.Add(tl); wb.Indices.Add(br); wb.Indices.Add(bl);
                            }
                    }
                }
            }
            else
            {
                for (int ci = 0; ci < 256; ci++)
                {
                    var c = a.Chunks[ci];
                    if (c == null || c.LegacyLiquid == null) continue;
                    var l = c.LegacyLiquid;
                    int type = l.Kind == 1 ? 2 : l.Kind == 2 ? 3 : l.Kind == 3 ? 4 : 1; // LiquidType ids 1 water, 2 ocean, 3 magma, 4 slime
                    WaterBuild wb;
                    if (!byType.TryGetValue(type, out wb)) { wb = new WaterBuild(); wb.LiquidType = type; byType[type] = wb; }
                    tb.LiquidInstances++;
                    int baseV = wb.Positions.Count;
                    for (int vy = 0; vy < 9; vy++)
                        for (int vx = 0; vx < 9; vx++)
                        {
                            float wx = c.X - vy * MapConst.UnitSize, wy = c.Y - vx * MapConst.UnitSize, h = l.Heights[vy * 9 + vx];
                            wb.Positions.Add(tb.Frame.ToUnity(wx, wy, h));
                            wb.Uvs.Add(new Vector2(-wy / (UvCells * MapConst.UnitSize), -wx / (UvCells * MapConst.UnitSize)));
                            wb.Colors.Add(new Color32(255, 255, 255, DepthFromTerrain(a, wx, wy, h)));
                        }
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            if (!l.TileExists(x, y)) continue;
                            int tl = baseV + y * 9 + x, tr = tl + 1, bl = tl + 9, br = bl + 1;
                            wb.Indices.Add(tl); wb.Indices.Add(tr); wb.Indices.Add(br);
                            wb.Indices.Add(tl); wb.Indices.Add(br); wb.Indices.Add(bl);
                        }
                }
            }
            foreach (var wb in byType.Values)
            {
                if (wb.Indices.Count == 0) continue;
                // same winding rule as the terrain: faces up (the shader draws both sides anyway)
                if (tb.WindingFlipped)
                    for (int i = 0; i + 2 < wb.Indices.Count; i += 3) { int t = wb.Indices[i + 1]; wb.Indices[i + 1] = wb.Indices[i + 2]; wb.Indices[i + 2] = t; }
                tb.Water.Add(wb);
            }
        }

        /// <summary>The owner of an outer vertex shared by several chunks of this tile: the south-east-most chunk holding it
        /// (col 8 = east neighbour's col 0, row 8 = south neighbour's row 0), so every sharer reads the same bits.</summary>
        static void Canonical(AdtFile a, AdtChunk c, int row, int col, out AdtChunk owner, out int k)
        {
            owner = c;
            while (true)
            {
                if (col == 8 && owner.IndexX < 15)
                {
                    var e = a.Chunks[owner.IndexY * 16 + owner.IndexX + 1];
                    if (e != null) { owner = e; col = 0; continue; }
                }
                if (row == 8 && owner.IndexY < 15)
                {
                    var s = a.Chunks[(owner.IndexY + 1) * 16 + owner.IndexX];
                    if (s != null) { owner = s; row = 0; continue; }
                }
                break;
            }
            k = AdtFile.Outer(row, col);
        }

        static byte DepthFromTerrain(AdtFile a, float wx, float wy, float h)
        {
            float z;
            if (!a.TryHeight(wx - 0.01f, wy - 0.01f, out z)) return 255;
            float d = (h - z) / 10f;
            return (byte)Math.Max(0, Math.Min(255, (int)(d * 255f)));
        }

        /// <summary>Frees the per-chunk data the main thread no longer needs (heights stay for TryGroundHeight).</summary>
        public static void Trim(AdtFile a)
        {
            foreach (var c in a.Chunks)
            {
                if (c == null) continue;
                c.Alpha[1] = c.Alpha[2] = c.Alpha[3] = null;
                c.Shadow = null;
                c.Mccv = null;
                c.LegacyLiquid = null;
            }
            a.Liquids = null;
        }
    }
}
