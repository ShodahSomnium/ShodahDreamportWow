using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Details
{
    /// <summary>One GroundEffectTexture row resolved: density and the detail models (slots into ClutterDb.Paths) with weights.</summary>
    public sealed class GroundEffect
    {
        public int Id;
        public int Density;              // doodads per 1/64-chunk cell (wowdev: 0 -> 8, > 24 -> 24)
        public int[] Slots = new int[0];
        public int[] Weights = new int[0];
        public int WeightSum;
    }

    /// <summary>One placed detail doodad (WoW yards; yaw in radians about +Z).</summary>
    public struct ClutterInstance
    {
        public int Slot;
        public float X, Y, Z, Yaw, Scale;
    }

    /// <summary>
    /// WoW's ground-effect tables (pure, thread-safe after Load). Layouts MEASURED (Details report):
    /// GroundEffectTexture.dbc 24981 x 11 fields: 0 ID, 1-4 DoodadId[4], 5-8 DoodadWeight[4], 9 Density, 10 Sound;
    /// 23797 rows have density 0 and only 16 of them any doodad; weights are 1 in 3526 of 3532 used slots.
    /// GroundEffectDoodad.dbc 580 x 3 fields: 0 ID, 1 Doodadpath (a bare name, "ElwFlo01.mdl"), 2 Flags; the model is
    /// World\NoDXT\Detail\&lt;name&gt;.m2 (547 exist, 33 Tirisfal names are missing -> skipped).
    /// </summary>
    public sealed class ClutterDb
    {
        public const string DetailFolder = @"World\NoDXT\Detail\";

        /// <summary>Full .m2 paths; the index is the slot used by GroundEffect.Slots and ClutterInstance.Slot.</summary>
        public readonly List<string> Paths = new List<string>();
        private readonly Dictionary<int, GroundEffect> _effects = new Dictionary<int, GroundEffect>();
        private readonly Dictionary<int, int> _doodadSlot = new Dictionary<int, int>();
        public int EffectRows, DoodadRows, MissingModels;
        public string FirstMissing;

        public static ClutterDb Load(WowFiles files)
        {
            var db = new ClutterDb();
            var get = Dbc.Load(files, "GroundEffectTexture");
            var ged = Dbc.Load(files, "GroundEffectDoodad");
            if (get == null || ged == null) return db;
            db.DoodadRows = ged.RecordCount;
            for (int r = 0; r < ged.RecordCount; r++)
            {
                string name = ged.GetString(r, 1);
                if (string.IsNullOrEmpty(name)) continue;
                int dot = name.LastIndexOf('.');
                string path = DetailFolder + (dot > 0 ? name.Substring(0, dot) : name) + ".m2";
                if (!files.Exists(path))
                {
                    db.MissingModels++;
                    if (db.FirstMissing == null) db.FirstMissing = path;
                    continue;
                }
                db._doodadSlot[ged.GetId(r)] = db.Paths.Count;
                db.Paths.Add(path);
            }
            db.EffectRows = get.RecordCount;
            for (int r = 0; r < get.RecordCount; r++)
            {
                var slots = new List<int>(4);
                var weights = new List<int>(4);
                for (int i = 0; i < 4; i++)
                {
                    int id = get.GetInt(r, 1 + i);
                    int slot;
                    if (id <= 0 || !db._doodadSlot.TryGetValue(id, out slot)) continue;
                    int w = get.GetInt(r, 5 + i);
                    slots.Add(slot);
                    weights.Add(w > 0 ? w : 1);
                }
                if (slots.Count == 0) continue;
                int density = get.GetInt(r, 9);
                if (density <= 0) density = 8;
                if (density > 24) density = 24;
                var e = new GroundEffect { Id = get.GetId(r), Density = density, Slots = slots.ToArray(), Weights = weights.ToArray() };
                foreach (int w in e.Weights) e.WeightSum += w;
                db._effects[e.Id] = e;
            }
            return db;
        }

        public int EffectCount { get { return _effects.Count; } }

        /// <summary>The effect of an MCLY effect id, or null (no row, no existing model).</summary>
        public GroundEffect Effect(int id)
        {
            GroundEffect e;
            return id > 0 && _effects.TryGetValue(id, out e) ? e : null;
        }
    }

    /// <summary>
    /// Places a chunk's ground clutter the way the client picks it (wowdev ADT v18 and MEASURED on Azeroth_32_48): per 1/64 cell
    /// the MCNK low-quality texture map names the layer on top, noEffectDoodad switches the cell off, the layer's MCLY effect id
    /// gives the GroundEffectTexture row. Density = doodads per cell (x the player's ClutterDensity). Inside the cell each doodad
    /// gets a deterministic random spot (the same every time the chunk is built), and is kept only where that layer's blended
    /// alpha there is at least MinLayerWeight (sharp edges along roads), on terrain (not in holes) and not under water.
    /// Random yaw and a 0.85-1.15 size spread are ours (not from the files).
    /// </summary>
    public static class ClutterPlacer
    {
        public const float MinLayerWeight = 0.3f;

        public static int PlaceChunk(DetailTerrain terrain, DetailTile t, int ci, ClutterDb db, float densityScale, List<ClutterInstance> output)
        {
            if (t == null || t.Adt == null || ci < 0 || ci >= 256) return 0;
            var c = t.Adt.Chunks[ci];
            if (c == null || c.LayerCount <= 0) return 0;
            uint seed = Hash((uint)(t.Map * 7919 + t.X * 131 + t.Y * 17), (uint)ci);
            const float cell = MapConst.ChunkSize / 8f;
            int placed = 0;
            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    uint cellSeed = Hash(seed, (uint)(row * 8 + col));
                    if (t.CellNoEffect(ci, row, col)) continue;
                    int layer = t.CellLayer(ci, row, col);
                    if (layer >= c.LayerCount) continue;
                    var e = db.Effect(c.Layers[layer].EffectId);
                    if (e == null || e.WeightSum <= 0) continue;
                    float want = e.Density * densityScale;
                    int n = (int)want;
                    if (Rand01(ref cellSeed) < want - n) n++;
                    for (int k = 0; k < n; k++)
                    {
                        float u = Rand01(ref cellSeed), v = Rand01(ref cellSeed);
                        float yaw = Rand01(ref cellSeed) * 6.2831853f;
                        float scale = 0.85f + 0.3f * Rand01(ref cellSeed);
                        int pick = (int)(Rand01(ref cellSeed) * e.WeightSum);
                        int slot = e.Slots[e.Slots.Length - 1];
                        for (int s = 0, acc = 0; s < e.Slots.Length; s++)
                        {
                            acc += e.Weights[s];
                            if (pick < acc) { slot = e.Slots[s]; break; }
                        }
                        // rows run along -X, cols along -Y from the chunk corner (largest X and Y)
                        float x = c.X - (row + u) * cell, y = c.Y - (col + v) * cell;
                        if (layer > 0 || c.LayerCount > 1)
                        {
                            int tr = row * 8 + (int)(u * 8f), tc = col * 8 + (int)(v * 8f);
                            if (tr > 63) tr = 63;
                            if (tc > 63) tc = 63;
                            if (LayerWeight(c, layer, tr * 64 + tc) < MinLayerWeight) continue;
                        }
                        float z;
                        if (!t.Adt.TryHeight(x, y, out z)) continue;
                        float lz;
                        if (terrain != null && terrain.Liquid(t.Map, x, y, out lz) && lz > z + 0.05f) continue;
                        output.Add(new ClutterInstance { Slot = slot, X = x, Y = y, Z = z, Yaw = yaw, Scale = scale });
                        placed++;
                    }
                }
            }
            return placed;
        }

        /// <summary>The layer's share of texel t (0..4095) after the client's bottom-to-top alpha blend.</summary>
        public static float LayerWeight(AdtChunk c, int layer, int t)
        {
            float rem = 1f;
            for (int l = c.LayerCount - 1; l >= 1; l--)
            {
                var al = c.Alpha[l];
                float a = al != null && al.Length > t ? al[t] / 255f : 0f;
                if (l == layer) return a * rem;
                rem *= 1f - a;
            }
            return rem;
        }

        public static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h;
        }

        public static float Rand01(ref uint s)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }
    }
}
