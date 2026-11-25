using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Flight
{
    /// <summary>One TaxiPathNode.dbc row (WoW world yards).</summary>
    public struct FlightPoint
    {
        public int Map;
        public float X, Y, Z;
        public int Flags;   // TaxiPathNode.Flags (bit 0 = teleport / map change, bit 1 = stop)
        public int Delay;   // seconds at a stop
        public FlightPoint(int map, float x, float y, float z, int flags, int delay) { Map = map; X = x; Y = y; Z = z; Flags = flags; Delay = delay; }
    }

    /// <summary>
    /// PURE (no UnityEngine): the client's taxi data the flight map and the flight need, read from the player's own install.
    /// Layouts verified on the real 3.3.5a files (P/reports4/flights.md):
    ///   TaxiPath.dbc      ID, FromNode, ToNode, Cost
    ///   TaxiPathNode.dbc  ID, PathID, NodeIndex, MapID, Loc x, y, z, Flags, Delay, ArrivalEventID, DepartureEventID (11 fields)
    ///   WorldMapContinent.dbc fields 9, 10 = TaxiMin (x, y), 11, 12 = TaxiMax (x, y): the taxi map's world rectangle
    ///   WorldMapTransforms.dbc ID, MapID, RegionMin x, y, RegionMax x, y, NewMapID, Offset x, y, NewDungeonMapID
    ///     (Quel'Thalas and the Azuremyst isles are map 530 but drawn on the Eastern Kingdoms / Kalimdor taxi maps).
    /// Taxi map pictures (the TaxiFrame's "TaxiMap" texture, 316 x 352 on the frame): Interface\TaxiFrame\TAXIMAP{0,1,530,571}.
    /// </summary>
    public sealed class FlightData
    {
        private struct PathRow { public int From, To, Cost; }
        private struct MapTransform { public int Map, NewMap; public float MinX, MinY, MaxX, MaxY, OffX, OffY; }

        private readonly Dictionary<int, PathRow> _paths = new Dictionary<int, PathRow>();
        private readonly Dictionary<int, List<FlightPoint>> _nodes = new Dictionary<int, List<FlightPoint>>();
        private readonly Dictionary<int, float[]> _taxiRect = new Dictionary<int, float[]>(); // map -> minX, minY, maxX, maxY
        private readonly List<MapTransform> _transforms = new List<MapTransform>();

        public int PathCount { get { return _paths.Count; } }
        public int PathNodeCount { get; private set; }
        public string Summary { get; private set; }

        /// <summary>Stitching distance of AzerothCore's FlightPathMovementGenerator (SKIP_SPLINE_POINT_DISTANCE_SQ = 40^2).</summary>
        public const float SkipDistance = 40f;

        public static FlightData Load(WowFiles files)
        {
            var d = new FlightData();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DbcTable paths = Dbc.Load(files, "TaxiPath");
            if (paths != null)
            {
                for (int r = 0; r < paths.RecordCount; r++)
                    d._paths[paths.GetId(r)] = new PathRow { From = paths.GetInt(r, 1), To = paths.GetInt(r, 2), Cost = paths.GetInt(r, 3) };
            }
            DbcTable nodes = Dbc.Load(files, "TaxiPathNode");
            var index = new Dictionary<int, List<KeyValuePair<int, FlightPoint>>>();
            if (nodes != null && nodes.FieldCount >= 9)
            {
                for (int r = 0; r < nodes.RecordCount; r++)
                {
                    int path = nodes.GetInt(r, 1);
                    List<KeyValuePair<int, FlightPoint>> list;
                    if (!index.TryGetValue(path, out list)) { list = new List<KeyValuePair<int, FlightPoint>>(); index[path] = list; }
                    list.Add(new KeyValuePair<int, FlightPoint>(nodes.GetInt(r, 2),
                        new FlightPoint(nodes.GetInt(r, 3), nodes.GetFloat(r, 4), nodes.GetFloat(r, 5), nodes.GetFloat(r, 6), nodes.GetInt(r, 7), nodes.GetInt(r, 8))));
                }
                foreach (var kv in index)
                {
                    kv.Value.Sort((a, b) => a.Key.CompareTo(b.Key));
                    var pts = new List<FlightPoint>(kv.Value.Count);
                    for (int i = 0; i < kv.Value.Count; i++) pts.Add(kv.Value[i].Value);
                    d._nodes[kv.Key] = pts;
                    d.PathNodeCount += pts.Count;
                }
            }
            DbcTable cont = Dbc.Load(files, "WorldMapContinent");
            if (cont != null && cont.FieldCount >= 13)
            {
                for (int r = 0; r < cont.RecordCount; r++)
                    d._taxiRect[cont.GetInt(r, 1)] = new[] { cont.GetFloat(r, 9), cont.GetFloat(r, 10), cont.GetFloat(r, 11), cont.GetFloat(r, 12) };
            }
            DbcTable tr = Dbc.Load(files, "WorldMapTransforms");
            if (tr != null && tr.FieldCount >= 9)
            {
                for (int r = 0; r < tr.RecordCount; r++)
                    d._transforms.Add(new MapTransform
                    {
                        Map = tr.GetInt(r, 1), MinX = tr.GetFloat(r, 2), MinY = tr.GetFloat(r, 3), MaxX = tr.GetFloat(r, 4), MaxY = tr.GetFloat(r, 5),
                        NewMap = tr.GetInt(r, 6), OffX = tr.GetFloat(r, 7), OffY = tr.GetFloat(r, 8)
                    });
            }
            d.Summary = "TaxiPath " + d._paths.Count + " paths, TaxiPathNode " + d.PathNodeCount + " nodes in " + d._nodes.Count + " paths, " +
                        "WorldMapContinent " + d._taxiRect.Count + " taxi maps, WorldMapTransforms " + d._transforms.Count + " rows, " + sw.ElapsedMilliseconds + " ms";
            return d;
        }

        public bool TryPath(int pathId, out int fromNode, out int toNode, out int cost)
        {
            PathRow p;
            if (_paths.TryGetValue(pathId, out p)) { fromNode = p.From; toNode = p.To; cost = p.Cost; return true; }
            fromNode = toNode = cost = 0;
            return false;
        }

        /// <summary>The TaxiPath id from one node to another (0 when there is no direct path).</summary>
        public int FindPath(int fromNode, int toNode)
        {
            foreach (var kv in _paths) if (kv.Value.From == fromNode && kv.Value.To == toNode) return kv.Key;
            return 0;
        }

        /// <summary>A path's nodes in NodeIndex order (null when the path has none).</summary>
        public List<FlightPoint> Nodes(int pathId)
        {
            List<FlightPoint> l;
            return _nodes.TryGetValue(pathId, out l) ? l : null;
        }

        /// <summary>WorldMapTransforms: the map a point is drawn on (map 530's Quel'Thalas -> 0, Azuremyst -> 1) and its position there.</summary>
        public int DisplayMap(int map, float x, float y, out float dx, out float dy)
        {
            for (int i = 0; i < _transforms.Count; i++)
            {
                MapTransform t = _transforms[i];
                if (t.Map == map && x >= t.MinX && x <= t.MaxX && y >= t.MinY && y <= t.MaxY)
                {
                    dx = x + t.OffX; dy = y + t.OffY;
                    return t.NewMap;
                }
            }
            dx = x; dy = y;
            return map;
        }

        /// <summary>
        /// Where a (display-map) world point sits on that map's taxi picture: u from the left, v from the top, 0..1.
        /// u = (maxY - y) / (maxY - minY), v = (maxX - x) / (maxX - minX) (WoW x is north, y is west). Verified by drawing every
        /// TaxiNodes row on TAXIMAP0/1 (nodes land on Teldrassil, Azuremyst, Quel'Thalas, Booty Bay ...).
        /// </summary>
        public bool MapUv(int displayMap, float x, float y, out float u, out float v)
        {
            float[] r;
            if (!_taxiRect.TryGetValue(displayMap, out r) || r[2] - r[0] == 0f || r[3] - r[1] == 0f) { u = v = 0f; return false; }
            u = (r[3] - y) / (r[3] - r[1]);
            v = (r[2] - x) / (r[2] - r[0]);
            return true;
        }

        public bool HasTaxiMap(int displayMap) { return _taxiRect.ContainsKey(displayMap); }

        /// <summary>The TaxiFrame picture of a continent (SetTaxiMap: TAXIMAP0 / TAXIMAP1 / TaxiMap530 / Taximap571).</summary>
        public static string TaxiMapTexture(int displayMap)
        {
            return "Interface\\TaxiFrame\\TAXIMAP" + displayMap + ".BLP";
        }

        /// <summary>
        /// One continuous flight over several TaxiPaths, stitched as AzerothCore's FlightPathMovementGenerator::LoadPath does:
        /// from a later hop the first two nodes and those within 40 yd of its start are dropped, from an earlier hop those within
        /// 40 yd of its end and its last node, so the flight passes the intermediate flight masters without landing. Cut at the
        /// first map change (GetPathAtMapEnd). note tells what was dropped/cut.
        /// </summary>
        public List<FlightPoint> BuildRoute(IList<int> pathIds, out string note)
        {
            var route = new List<FlightPoint>();
            note = "";
            int hops = pathIds != null ? pathIds.Count : 0;
            for (int h = 0; h < hops; h++)
            {
                List<FlightPoint> nodes = Nodes(pathIds[h]);
                if (nodes == null || nodes.Count == 0) { note += "path " + pathIds[h] + " has no nodes; "; continue; }
                FlightPoint start = nodes[0], end = nodes[nodes.Count - 1];
                bool first = h == 0, last = h == hops - 1;
                bool passed = false;
                for (int i = 0; i < nodes.Count; i++)
                {
                    FlightPoint p = nodes[i];
                    if (passed || first || route.Count == 0 || !Near(route[route.Count - 1], p))
                    {
                        if ((first || (!Near(start, p) && i >= 2)) && (last || (!Near(end, p) && i < nodes.Count - 1)))
                        {
                            passed = true;
                            route.Add(p);
                        }
                    }
                    else
                    {
                        route.RemoveAt(route.Count - 1);
                    }
                }
            }
            if (route.Count > 0)
            {
                int map = route[0].Map;
                for (int i = 1; i < route.Count; i++)
                {
                    if (route[i].Map != map)
                    {
                        note += "cut at node " + i + "/" + route.Count + " (map " + map + " -> " + route[i].Map + "); ";
                        route.RemoveRange(i, route.Count - i);
                        break;
                    }
                }
            }
            return route;
        }

        private static bool Near(FlightPoint a, FlightPoint b)
        {
            if (a.Map != b.Map) return false;
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy <= SkipDistance * SkipDistance;
        }
    }

    /// <summary>
    /// PURE: the flight's curve. A flying server spline is Catmull-Rom (AzerothCore MoveSplineFlag.h: Mask_CatmullRom = Flying |
    /// Catmullrom) walked at a constant speed (TaxiFlightSpeed = 32 yd/s). Uniform Catmull-Rom through the points (ends
    /// duplicated), an arc-length table so a distance maps to a point. Doubles throughout: WoW coordinates reach 17 000 yd.
    /// </summary>
    public sealed class FlightSpline
    {
        private readonly double[] _x, _y, _z;
        private readonly double[] _cum;     // arc length at each sample
        private readonly int _segments;
        private const int Samples = 16;     // per segment

        public double Length { get; private set; }
        public int PointCount { get { return _x.Length; } }

        public FlightSpline(IList<double> xs, IList<double> ys, IList<double> zs)
        {
            int n = xs.Count;
            _x = new double[n]; _y = new double[n]; _z = new double[n];
            for (int i = 0; i < n; i++) { _x[i] = xs[i]; _y[i] = ys[i]; _z[i] = zs[i]; }
            _segments = Math.Max(0, n - 1);
            _cum = new double[_segments * Samples + 1];
            double len = 0, px = 0, py = 0, pz = 0;
            for (int k = 0; k <= _segments * Samples; k++)
            {
                double x, y, z;
                AtParam(k / (double)Samples, out x, out y, out z);
                if (k > 0) len += Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py) + (z - pz) * (z - pz));
                _cum[k] = len;
                px = x; py = y; pz = z;
            }
            Length = len;
        }

        /// <summary>The point at parameter t (0 .. segments): segment floor(t), local fraction.</summary>
        private void AtParam(double t, out double x, out double y, out double z)
        {
            int n = _x.Length;
            if (n == 0) { x = y = z = 0; return; }
            if (n == 1 || t <= 0) { x = _x[0]; y = _y[0]; z = _z[0]; return; }
            if (t >= _segments) { x = _x[n - 1]; y = _y[n - 1]; z = _z[n - 1]; return; }
            int i = (int)Math.Floor(t);
            double f = t - i;
            int i0 = Math.Max(0, i - 1), i1 = i, i2 = Math.Min(n - 1, i + 1), i3 = Math.Min(n - 1, i + 2);
            x = Cr(_x[i0], _x[i1], _x[i2], _x[i3], f);
            y = Cr(_y[i0], _y[i1], _y[i2], _y[i3], f);
            z = Cr(_z[i0], _z[i1], _z[i2], _z[i3], f);
        }

        private static double Cr(double p0, double p1, double p2, double p3, double t)
        {
            double t2 = t * t, t3 = t2 * t;
            return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        /// <summary>The point at arc length s (yards from the start, clamped).</summary>
        public void At(double s, out double x, out double y, out double z)
        {
            if (_segments == 0 || s <= 0) { AtParam(0, out x, out y, out z); return; }
            if (s >= Length) { AtParam(_segments, out x, out y, out z); return; }
            int lo = 0, hi = _cum.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_cum[mid] <= s) lo = mid; else hi = mid; }
            double seg = _cum[hi] - _cum[lo];
            double f = seg > 1e-9 ? (s - _cum[lo]) / seg : 0;
            AtParam((lo + f) / Samples, out x, out y, out z);
        }

        /// <summary>The horizontal travel direction at s (WoW x, y; normalised; (1, 0) when unknown).</summary>
        public void Heading(double s, out double dx, out double dy)
        {
            double ax, ay, az, bx, by, bz;
            At(s - 3, out ax, out ay, out az);
            At(s + 3, out bx, out by, out bz);
            dx = bx - ax; dy = by - ay;
            double l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-6) { dx = 1; dy = 0; return; }
            dx /= l; dy /= l;
        }
    }
}
