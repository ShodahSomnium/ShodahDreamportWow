// iteration 11 - owner world (proposal-world.md 2.1): AreaTrigger.dbc of the player's own install, grouped by map, with AzerothCore's own
// inside test (so whatever the client reports, the server accepts). Pure C#: no UnityEngine.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>One AreaTrigger.dbc row (measured: 1220 rows x 10 fields, 40 B/row; it equals AC's `areatrigger` table for all 1217 server
    /// rows, ObjectMgr.cpp:7245-7290).</summary>
    public struct AreaTriggerDef
    {
        public int Id, Map;
        public float X, Y, Z, Radius, Length, Width, Height, Yaw;

        public bool Sphere { get { return Radius > 0f; } }

        /// <summary>AC Player::IsInAreaTriggerRadius (Player.cpp:2215-2235) WITHOUT the object size (the server subtracts the player's 1.5,
        /// Object.cpp:1316-1320, so ours is stricter by 1.5 yd): a sphere tests the 3D distance; a box is Position::IsWithinBox
        /// (Position.cpp:118-145) with half sizes. margin = extra yards on the radius / half sizes (our hysteresis). A box with a side
        /// &lt;= 0 never contains anything (trigger 5049; the server would need an exact float match).</summary>
        public bool Contains(float x, float y, float z, float margin)
        {
            if (Radius > 0f)
            {
                float dx = x - X, dy = y - Y, dz = z - Z, r = Radius + margin;
                return dx * dx + dy * dy + dz * dz <= r * r;
            }
            if (Length <= 0f || Width <= 0f || Height <= 0f) return false;
            double rotation = 2 * Math.PI - Yaw;
            double s = Math.Sin(rotation), c = Math.Cos(rotation);
            float bx = x - X, by = y - Y;
            float rx = (float)(X + bx * c - by * s);
            float ry = (float)(Y + by * c + bx * s);
            return Math.Abs(rx - X) <= Length / 2f + margin && Math.Abs(ry - Y) <= Width / 2f + margin && Math.Abs(z - Z) <= Height / 2f + margin;
        }

        public string Describe()
        {
            return Radius > 0f ? "sphere r " + Radius.ToString("0.##") : "box " + Length.ToString("0.##") + "x" + Width.ToString("0.##") + "x" + Height.ToString("0.##");
        }
    }

    /// <summary>AreaTrigger.dbc grouped by map (built once per WowFiles; Dbc.Load caches the table itself).</summary>
    public sealed class AreaTriggerTable
    {
        // AreaTrigger.dbc fields (measured, P/harness/Probes/D11/world/AcInstance.cs LoadDbc): 0 ID, 1 ContinentID, 2 X, 3 Y, 4 Z, 5 Radius,
        // 6 BoxLength, 7 BoxWidth, 8 BoxHeight, 9 BoxYaw
        const int FId = 0, FMap = 1, FX = 2, FY = 3, FZ = 4, FRadius = 5, FLength = 6, FWidth = 7, FHeight = 8, FYaw = 9;

        static readonly object Gate = new object();
        static WowFiles _cachedFiles;
        static AreaTriggerTable _cached;
        static readonly AreaTriggerDef[] None = new AreaTriggerDef[0];

        readonly Dictionary<int, AreaTriggerDef[]> _byMap = new Dictionary<int, AreaTriggerDef[]>();
        readonly Dictionary<int, AreaTriggerDef> _byId = new Dictionary<int, AreaTriggerDef>();

        public int Count { get { return _byId.Count; } }
        public int MapCount { get { return _byMap.Count; } }

        /// <summary>The table of this install (cached), or null + one WowLog.Warn when AreaTrigger.dbc is missing / too short.</summary>
        public static AreaTriggerTable Load(WowFiles files)
        {
            if (files == null) return null;
            lock (Gate)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                DbcTable t = Dbc.Load(files, "AreaTrigger");
                if (t == null || t.FieldCount < 10 || !t.FieldsAre4Bytes)
                {
                    WowLog.Warn("Online instances: AreaTrigger.dbc missing or not 10 x 4-byte fields (" + (t == null ? "null" : t.FieldCount + " fields") + ") - no area trigger is reported (portals, exploration quests, inns)");
                    return null;
                }
                var table = new AreaTriggerTable();
                var lists = new Dictionary<int, List<AreaTriggerDef>>();
                for (int r = 0; r < t.RecordCount; r++)
                {
                    var d = new AreaTriggerDef
                    {
                        Id = t.GetInt(r, FId), Map = t.GetInt(r, FMap), X = t.GetFloat(r, FX), Y = t.GetFloat(r, FY), Z = t.GetFloat(r, FZ),
                        Radius = t.GetFloat(r, FRadius), Length = t.GetFloat(r, FLength), Width = t.GetFloat(r, FWidth), Height = t.GetFloat(r, FHeight), Yaw = t.GetFloat(r, FYaw)
                    };
                    table._byId[d.Id] = d;
                    List<AreaTriggerDef> l;
                    if (!lists.TryGetValue(d.Map, out l)) lists[d.Map] = l = new List<AreaTriggerDef>();
                    l.Add(d);
                }
                foreach (var kv in lists) table._byMap[kv.Key] = kv.Value.ToArray();
                _cachedFiles = files;
                _cached = table;
                return table;
            }
        }

        /// <summary>The triggers of a map (an empty list for a map without any).</summary>
        public IReadOnlyList<AreaTriggerDef> OnMap(int mapId)
        {
            AreaTriggerDef[] l;
            return _byMap.TryGetValue(mapId, out l) ? l : None;
        }

        public bool TryGet(int id, out AreaTriggerDef def) { return _byId.TryGetValue(id, out def); }
    }
}
