using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>A TaxiNodes.dbc row (iteration 4).</summary>
    public sealed class TaxiNodeInfo
    {
        public int Id, MapId;
        public float X, Y, Z;
        public string Name;
        public int MountHorde, MountAlliance;     // MountCreatureID[0] / [1] (server creature ids, 0 = not this faction's)
        public int MountFor(WowFaction f) { return f == WowFaction.Horde ? MountHorde : MountAlliance; }
    }

    /// <summary>One TaxiPathNode.dbc row: a point of a flight path, in path order.</summary>
    public struct TaxiPathPoint
    {
        public int Index, MapId;
        public float X, Y, Z;
        public int Flags;                          // 0 normal; 1 / 2 seen on boat/portal paths (map changes)
        public int Delay;                          // seconds to wait at this point (0 on ordinary flight paths)
    }

    /// <summary>A TaxiPath.dbc row with its points (TaxiPathNode.dbc) and length.</summary>
    public sealed class TaxiPathInfo
    {
        public int Id, From, To, Cost;             // Cost in copper
        public readonly List<TaxiPathPoint> Points = new List<TaxiPathPoint>();
        public float Length;                       // yards along the points (same-map segments)
    }

    /// <summary>
    /// Flight paths read at runtime from the player's own client files (pure C#): TaxiNodes.dbc (ID, map, x, y, z, name locstring 5..21,
    /// MountCreatureID Horde 22 / Alliance 23), TaxiPath.dbc (ID, From, To, Cost), TaxiPathNode.dbc (ID, PathID, NodeIndex, MapID, x, y, z,
    /// Flags, Delay, ArrivalEventID, DepartureEventID) - layouts verified on the 3.3.5a files (Rules report iteration 4).
    /// The flight network of a faction (flight masters, route finding through known nodes, mount displays) is derived here.
    /// </summary>
    public sealed class WowTaxiDb
    {
        /// <summary>Paid paths below this cost are the developers' test paths (1-5 copper: Filming, Test SP1, PattyMacDevTest); real ones cost 50+.</summary>
        public const int MinRealCost = 10;

        private static readonly object CacheLock = new object();
        private static WowTaxiDb _cached;
        private static WowFiles _cachedFiles;

        private readonly WowFiles _files;
        private readonly Dictionary<int, TaxiNodeInfo> _nodes = new Dictionary<int, TaxiNodeInfo>();
        private readonly Dictionary<int, TaxiPathInfo> _paths = new Dictionary<int, TaxiPathInfo>();
        private readonly Dictionary<long, TaxiPathInfo> _byPair = new Dictionary<long, TaxiPathInfo>();
        private readonly HashSet<int> _spellPaths = new HashSet<int>();
        private readonly Dictionary<int, List<TaxiPathInfo>>[] _edges = { new Dictionary<int, List<TaxiPathInfo>>(), new Dictionary<int, List<TaxiPathInfo>>() };
        // Iteration 11 (motion): the flight-master paths that cross a map (probe MotionTaxiMaps: 9 paths between Eastern Kingdoms map 0 and
        // Quel'Thalas on map 530 - Light's Hope Chapel <-> Tranquillien / Zul'Aman / Shattered Sun Staging Area, Ironforge -> Shattered Sun).
        // Kept apart so the offline routes are unchanged; online routes may use them (FindRoute crossMap).
        private readonly Dictionary<int, List<TaxiPathInfo>>[] _edgesX = { new Dictionary<int, List<TaxiPathInfo>>(), new Dictionary<int, List<TaxiPathInfo>>() };
        private readonly Dictionary<string, int> _displayCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public bool Available { get; private set; }

        /// <summary>The flight data of these files (read once, cached).</summary>
        public static WowTaxiDb Get(WowFiles files)
        {
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
            }
            var db = new WowTaxiDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            return db;
        }

        private static long Pair(int a, int b) { return ((long)a << 32) | (uint)b; }

        private WowTaxiDb(WowFiles files)
        {
            _files = files;
            var t0 = DateTime.UtcNow;
            var n = files != null ? Dbc.Load(files, "TaxiNodes") : null;
            var p = files != null ? Dbc.Load(files, "TaxiPath") : null;
            var pn = files != null ? Dbc.Load(files, "TaxiPathNode") : null;
            if (n == null || p == null || pn == null || n.FieldCount < 24 || p.FieldCount < 4 || pn.FieldCount < 9)
            {
                WowLog.Warn("WowTaxiDb: TaxiNodes/TaxiPath/TaxiPathNode.dbc missing or unexpected - no flight paths");
                return;
            }
            for (int r = 0; r < n.RecordCount; r++)
            {
                var ni = new TaxiNodeInfo
                {
                    Id = n.GetId(r), MapId = n.GetInt(r, DbcFields.TaxiNodes.ContinentID),
                    X = n.GetFloat(r, DbcFields.TaxiNodes.Pos), Y = n.GetFloat(r, DbcFields.TaxiNodes.Pos + 1), Z = n.GetFloat(r, DbcFields.TaxiNodes.Pos + 2),
                    Name = n.GetLocalizedString(r, DbcFields.TaxiNodes.Name_lang),
                    MountHorde = n.GetInt(r, DbcFields.TaxiNodes.MountCreatureID), MountAlliance = n.GetInt(r, DbcFields.TaxiNodes.MountCreatureID + 1)
                };
                _nodes[ni.Id] = ni;
            }
            for (int r = 0; r < p.RecordCount; r++)
            {
                var pi = new TaxiPathInfo { Id = p.GetId(r), From = p.GetInt(r, 1), To = p.GetInt(r, 2), Cost = p.GetInt(r, 3) };
                _paths[pi.Id] = pi;
                _byPair[Pair(pi.From, pi.To)] = pi;
            }
            for (int r = 0; r < pn.RecordCount; r++)
            {
                TaxiPathInfo pi;
                if (!_paths.TryGetValue(pn.GetInt(r, 1), out pi)) continue;
                pi.Points.Add(new TaxiPathPoint
                {
                    Index = pn.GetInt(r, 2), MapId = pn.GetInt(r, 3), X = pn.GetFloat(r, 4), Y = pn.GetFloat(r, 5), Z = pn.GetFloat(r, 6),
                    Flags = pn.GetInt(r, 7), Delay = pn.GetInt(r, 8)
                });
            }
            foreach (var pi in _paths.Values)
            {
                pi.Points.Sort((a, b) => a.Index.CompareTo(b.Index));
                float len = 0f;
                for (int i = 1; i < pi.Points.Count; i++)
                {
                    var a = pi.Points[i - 1]; var b = pi.Points[i];
                    if (a.MapId != b.MapId) continue;
                    float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                    len += (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }
                pi.Length = len;
            }
            // Quest flights: paths started by a spell's SEND_TAXI effect (123, misc value = TaxiPath id) are not flight-master routes.
            var sp = Dbc.Load(files, "Spell");
            if (sp != null && sp.FieldCount > SpellFields.Spell.EffectMiscValue + 2)
                for (int r = 0; r < sp.RecordCount; r++)
                    for (int j = 0; j < 3; j++)
                        if (sp.GetInt(r, SpellFields.Spell.Effect + j) == 123) _spellPaths.Add(sp.GetInt(r, SpellFields.Spell.EffectMiscValue + j));
            BuildEdges(WowFaction.Alliance);
            BuildEdges(WowFaction.Horde);
            Available = true;
            WowLog.Info("WowTaxiDb: " + _nodes.Count + " nodes, " + _paths.Count + " paths (" + pn.RecordCount + " points, " + _spellPaths.Count + " quest paths), network "
                + _edges[0].Count + " Alliance / " + _edges[1].Count + " Horde nodes, " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms");
        }

        /// <summary>
        /// Flight-master edges of a faction: a TaxiPath between two nodes of the same map that both carry this faction's mount, flown both ways
        /// (the reverse path exists), and either paid (>= MinRealCost) or free and not a quest flight (Rut'theran Village - Auberdine).
        /// This drops quest/scripted flights (one-way, SEND_TAXI), the druid-only Nighthaven routes (one-way), the Death Knight's Acherus and dev nodes.
        /// </summary>
        private void BuildEdges(WowFaction f)
        {
            var edges = _edges[(int)f];
            foreach (var pi in _paths.Values)
            {
                TaxiNodeInfo a, b;
                if (!_nodes.TryGetValue(pi.From, out a) || !_nodes.TryGetValue(pi.To, out b)) continue;
                bool cross = a.MapId != b.MapId;   // iteration 11 (motion): two-map paths go to _edgesX (online only)
                if (!TaxiMount(a.MountFor(f)) || !TaxiMount(b.MountFor(f)) || pi.Points.Count < 2) continue;
                TaxiPathInfo back;
                // lead fix 2026-10-14 (the user: "in outland, I cannot fly between flight masters"; harness OutlandTaxi: the Dark Portal 129 ->
                // Honor Hold 564 (back 566) and -> Temple of Telhamat 582 (no back) cost 0 and had no route): a path flown both ways is a
                // flight-master route even when a quest spell also uses it; a one-way path is one unless a quest spell sends it (SEND_TAXI).
                // The realm still decides each flight.
                _byPair.TryGetValue(Pair(pi.To, pi.From), out back);
                if (back == null && !cross && _spellPaths.Contains(pi.Id)) continue;
                if (back == null && !cross && pi.Cost > 0 && pi.Cost < MinRealCost) continue;
                List<TaxiPathInfo> l;
                if (cross)
                {
                    var ex = _edgesX[(int)f];
                    if (!ex.TryGetValue(pi.From, out l)) ex[pi.From] = l = new List<TaxiPathInfo>();
                    l.Add(pi);
                    continue;
                }
                if (!edges.TryGetValue(pi.From, out l)) edges[pi.From] = l = new List<TaxiPathInfo>();
                l.Add(pi);
                if (!edges.ContainsKey(pi.To)) edges[pi.To] = new List<TaxiPathInfo>();
            }
        }

        /// <summary>The regular taxi mounts (gryphon, hippogryph, wind rider, bat, dragonhawk); others are scripted/class flights (Acherus 32981: Death Knights only).</summary>
        private static bool TaxiMount(int creature) { return creature == 541 || creature == 3837 || creature == 2224 || creature == 3574 || creature == 19917 || creature == 28361; }

        private bool RealPath(TaxiPathInfo pi) { return pi.Cost >= MinRealCost || (pi.Cost == 0 && !_spellPaths.Contains(pi.Id)); }

        // ------------------------------------------------------------------ lookups

        public TaxiNodeInfo Node(int id) { TaxiNodeInfo n; return _nodes.TryGetValue(id, out n) ? n : null; }
        public TaxiPathInfo Path(int id) { TaxiPathInfo p; return _paths.TryGetValue(id, out p) ? p : null; }
        /// <summary>The TaxiPath.dbc row from -> to, or null.</summary>
        public TaxiPathInfo PathBetween(int from, int to) { TaxiPathInfo p; return _byPair.TryGetValue(Pair(from, to), out p) ? p : null; }
        /// <summary>The points of a TaxiPath id in order (empty when unknown).</summary>
        public IReadOnlyList<TaxiPathPoint> PathPoints(int pathId) { var p = Path(pathId); return p != null ? (IReadOnlyList<TaxiPathPoint>)p.Points : new TaxiPathPoint[0]; }

        /// <summary>True when the node has a flight master of this faction (it is part of the faction's flight network).</summary>
        public bool IsFlightMasterNode(int nodeId, WowFaction f) { return _edges[(int)f].ContainsKey(nodeId); }

        /// <summary>Every flight-master node of this faction on a map, by id.</summary>
        public List<TaxiNodeInfo> FlightMasterNodes(int mapId, WowFaction f)
        {
            var l = new List<TaxiNodeInfo>();
            foreach (var id in _edges[(int)f].Keys) { var n = Node(id); if (n != null && n.MapId == mapId) l.Add(n); }
            l.Sort((a, b) => a.Id.CompareTo(b.Id));
            return l;
        }

        /// <summary>
        /// The cheapest-to-fly route (shortest total path length) from -> to for this faction, hopping only through known nodes (like the
        /// client's multi-hop routes). Returns the TaxiPath ids hop by hop, or null when there is none. The destination must be known.
        /// </summary>
        public List<int> FindRoute(int from, int to, WowFaction f, ICollection<int> known)
        {
            return FindRoute(from, to, f, known, false);
        }

        /// <summary>As FindRoute; crossMap = true (online, iteration 11 motion) also flies the two-map paths (the server continues them
        /// after a far teleport: TaxiHandler.cpp:216-241).</summary>
        public List<int> FindRoute(int from, int to, WowFaction f, ICollection<int> known, bool crossMap)
        {
            var edges = _edges[(int)f];
            var edgesX = crossMap ? _edgesX[(int)f] : null;
            if (from == to || !(edges.ContainsKey(from) || (edgesX != null && edgesX.ContainsKey(from))) || !edges.ContainsKey(to)) return null;
            if (known != null && !known.Contains(to)) return null;
            var dist = new Dictionary<int, float>();
            var via = new Dictionary<int, TaxiPathInfo>();
            var done = new HashSet<int>();
            dist[from] = 0f;
            while (true)
            {
                int cur = -1; float best = float.MaxValue;
                foreach (var kv in dist) if (!done.Contains(kv.Key) && kv.Value < best) { best = kv.Value; cur = kv.Key; }
                if (cur < 0) return null;
                if (cur == to) break;
                done.Add(cur);
                if (cur != from && known != null && !known.Contains(cur)) continue;   // never fly through an unknown node
                for (int pass = 0; pass < 2; pass++)
                {
                    List<TaxiPathInfo> outs;
                    var set = pass == 0 ? edges : edgesX;
                    if (set == null || !set.TryGetValue(cur, out outs)) continue;
                    foreach (var e in outs)
                    {
                        if (done.Contains(e.To)) continue;
                        if (known != null && !known.Contains(e.To)) continue;
                        float d = best + Math.Max(1f, e.Length);
                        float old;
                        if (!dist.TryGetValue(e.To, out old) || d < old) { dist[e.To] = d; via[e.To] = e; }
                    }
                }
            }
            var route = new List<int>();
            int n2 = to;
            while (n2 != from) { var e = via[n2]; route.Add(e.Id); n2 = e.From; }
            route.Reverse();
            return route;
        }

        // ------------------------------------------------------------------ iteration 11 (motion): the online flight map

        /// <summary>SMSG_SHOWTAXINODES' taximask (14 x uint32, PlayerTaxi.h:35-39): node n is known when mask[(n-1)/32] has bit (n-1)%32.</summary>
        public static bool MaskKnown(uint[] mask, int node)
        {
            if (mask == null || node <= 0) return false;
            int field = (node - 1) / 32;
            return field < mask.Length && (mask[field] & (1u << ((node - 1) % 32))) != 0;
        }

        /// <summary>Every node id set in a taximask, ascending.</summary>
        public static List<int> KnownFromMask(uint[] mask)
        {
            var l = new List<int>();
            if (mask == null) return l;
            for (int f = 0; f < mask.Length; f++)
                for (int b = 0; b < 32; b++)
                    if ((mask[f] & (1u << b)) != 0) l.Add(f * 32 + b + 1);
            return l;
        }

        /// <summary>The nodes the online flight map lists at a node of mapId: this map's flight-master nodes plus every node linked to
        /// them through a two-map path and that node's own network (Quel'Thalas at Light's Hope Chapel). FlightMap draws only those
        /// on the current continent picture (WorldMapTransforms).</summary>
        public List<TaxiNodeInfo> ViewNodes(int mapId, WowFaction f)
        {
            var l = FlightMasterNodes(mapId, f);
            var seen = new HashSet<int>();
            foreach (var n in l) seen.Add(n.Id);
            var edgesX = _edgesX[(int)f];
            var queue = new Queue<int>();
            foreach (var kv in edgesX)
            {
                if (!seen.Contains(kv.Key)) continue;
                foreach (var e in kv.Value) if (seen.Add(e.To)) queue.Enqueue(e.To);
            }
            var edges = _edges[(int)f];
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                var n = Node(id);
                if (n != null) l.Add(n);
                List<TaxiPathInfo> outs;
                if (edges.TryGetValue(id, out outs)) foreach (var e in outs) if (seen.Add(e.To)) queue.Enqueue(e.To);
            }
            return l;
        }

        /// <summary>The flight-master node of mapId nearest to (x, y, z) within maxYards, 0 when none (the destination of a flight whose
        /// route we did not ask for: a quest flight, a login mid-flight).</summary>
        public int NearestNode(int mapId, float x, float y, float z, float maxYards)
        {
            int best = 0; float bestD = maxYards * maxYards;
            foreach (var n in _nodes.Values)
            {
                if (n.MapId != mapId) continue;
                float dx = n.X - x, dy = n.Y - y, dz = n.Z - z, d = dx * dx + dy * dy + dz * dz;
                if (d < bestD) { bestD = d; best = n.Id; }
            }
            return best;
        }

        /// <summary>
        /// The map a flight continues on after a spline that ends at (x, y, z) on mapId (FlightPathMovementGenerator builds the spline up
        /// to GetPathAtMapEnd, WaypointMovementGenerator.cpp:553-570, from the TaxiPathNode positions themselves): the TaxiPathNode at
        /// that point (|d| &lt; 0.5 yd, the preferred paths first, then every path) whose next node is on another map. -1 = none.
        /// </summary>
        public int ContinuesOnMap(IList<int> preferPaths, int mapId, float x, float y, float z)
        {
            if (preferPaths != null)
                foreach (int id in preferPaths) { int m = NextMapAt(Path(id), mapId, x, y, z); if (m >= 0) return m; }
            foreach (var p in _paths.Values) { int m = NextMapAt(p, mapId, x, y, z); if (m >= 0) return m; }
            return -1;
        }

        private static int NextMapAt(TaxiPathInfo p, int mapId, float x, float y, float z)
        {
            if (p == null) return -1;
            for (int i = 0; i + 1 < p.Points.Count; i++)
            {
                var a = p.Points[i];
                if (a.MapId != mapId) continue;
                float dx = a.X - x, dy = a.Y - y, dz = a.Z - z;
                if (dx * dx + dy * dy + dz * dz >= 0.25f) continue;
                var b = p.Points[i + 1];
                if (b.MapId != mapId) return b.MapId;
            }
            return -1;
        }

        public int RouteCost(IList<int> route) { int c = 0; if (route != null) foreach (var id in route) { var p = Path(id); if (p != null) c += p.Cost; } return c; }
        public float RouteLength(IList<int> route) { float c = 0; if (route != null) foreach (var id in route) { var p = Path(id); if (p != null) c += p.Length; } return c; }

        /// <summary>The last point of a route (the landing point), or false when the route is empty.</summary>
        public bool RouteEnd(IList<int> route, out TaxiPathPoint end)
        {
            end = default(TaxiPathPoint);
            if (route == null || route.Count == 0) return false;
            var p = Path(route[route.Count - 1]);
            if (p == null || p.Points.Count == 0) return false;
            end = p.Points[p.Points.Count - 1];
            return true;
        }

        public bool RouteStart(IList<int> route, out TaxiPathPoint start)
        {
            start = default(TaxiPathPoint);
            if (route == null || route.Count == 0) return false;
            var p = Path(route[0]);
            if (p == null || p.Points.Count == 0) return false;
            start = p.Points[0];
            return true;
        }

        // ------------------------------------------------------------------ known nodes

        /// <summary>
        /// The nodes a new character knows (AzerothCore PlayerTaxi::InitTaxiNodesForLevel): its race's capital (Night Elves Auberdine + Rut'theran),
        /// the faction's Outland entry (Alliance 100 Honor Hold, Horde 99 Thrallmar) and 213 Shattered Sun Staging Area at level 68+.
        /// </summary>
        public static int[] InitialNodes(int race, int level)
        {
            var l = new List<int>(4);
            switch (race)
            {
                case 1: l.Add(2); break;
                case 2: case 8: l.Add(23); break;
                case 3: case 7: l.Add(6); break;
                case 4: l.Add(26); l.Add(27); break;
                case 5: l.Add(11); break;
                case 6: l.Add(22); break;
                case 10: l.Add(82); break;
                case 11: l.Add(94); break;
            }
            l.Add(WowRaces.FactionOf(race) == WowFaction.Alliance ? 100 : 99);
            if (level >= 68) l.Add(213);
            return l.ToArray();
        }

        // ------------------------------------------------------------------ displays

        /// <summary>Taxi mount kinds (by the mount creature on the node: 541 gryphon, 3837 hippogryph, 2224 wind rider, 3574 bat, 19917/28361 dragonhawk).</summary>
        public enum MountKind { Gryphon, Hippogryph, WindRider, Bat, Dragonhawk }

        public static MountKind KindOf(int mountCreature, WowFaction f)
        {
            switch (mountCreature)
            {
                case 541: return MountKind.Gryphon;
                case 3837: return MountKind.Hippogryph;
                case 2224: return MountKind.WindRider;
                case 3574: return MountKind.Bat;
                case 19917: case 28361: return MountKind.Dragonhawk;
            }
            return f == WowFaction.Horde ? MountKind.WindRider : MountKind.Gryphon;
        }

        public MountKind KindAt(int nodeId, WowFaction f) { var n = Node(nodeId); return KindOf(n != null ? n.MountFor(f) : 0, f); }

        private static readonly string[] MountModels = { @"Creature\Gryphon\Gryphon.mdx", @"Creature\Hippogryph\Hippogryph.mdx", @"Creature\Wyvern\Wyvern.mdx", @"Creature\FelBat\BatTaxi.mdx", @"Creature\DragonHawk\DragonHawk.mdx" };
        private static readonly int[] MountPreferred = { 6852, 1936, 6851, 1566, 19259 };   // measured: the displays of those models (Rules report)

        /// <summary>The CreatureDisplayInfo id of the taxi mount at this node (found by the model's file name), 0 when none.</summary>
        public int MountDisplayId(int nodeId, WowFaction f)
        {
            var k = KindAt(nodeId, f);
            return DisplayForModel(MountModels[(int)k], MountPreferred[(int)k]);
        }

        private int DisplayForModel(string model, int preferred)
        {
            string key = model;
            lock (_displayCache)
            {
                int cached;
                if (_displayCache.TryGetValue(key, out cached)) return cached;
            }
            int found = 0;
            var cdi = Dbc.Load(_files, "CreatureDisplayInfo");
            var cmd = Dbc.Load(_files, "CreatureModelData");
            if (cdi != null && cmd != null)
            {
                if (preferred > 0 && ModelOf(cdi, cmd, preferred).Equals(model, StringComparison.OrdinalIgnoreCase)) found = preferred;
                else
                {
                    var models = new HashSet<int>();
                    for (int r = 0; r < cmd.RecordCount; r++)
                        if (string.Equals(cmd.GetString(r, DbcFields.CreatureModelData.ModelName), model, StringComparison.OrdinalIgnoreCase)) models.Add(cmd.GetId(r));
                    for (int r = 0; r < cdi.RecordCount && models.Count > 0; r++)
                    {
                        if (!models.Contains(cdi.GetInt(r, DbcFields.CreatureDisplayInfo.ModelID))) continue;
                        int id = cdi.GetId(r);
                        if (found == 0 || id < found) found = id;
                    }
                }
            }
            lock (_displayCache) _displayCache[key] = found;
            return found;
        }

        private static string ModelOf(DbcTable cdi, DbcTable cmd, int displayId)
        {
            int r = cdi.FindRow(displayId);
            if (r < 0) return "";
            int mr = cmd.FindRow(cdi.GetInt(r, DbcFields.CreatureDisplayInfo.ModelID));
            return mr >= 0 ? (cmd.GetString(mr, DbcFields.CreatureModelData.ModelName) ?? "") : "";
        }

        // Flight master NPCs: displays of a fitting race (measured: model folder + CreatureDisplayInfoExtra race), our own names.
        private static readonly int[][] MasterDisplays =
        {
            new[] { 3007, 2417, 5037, 5128 },   // gryphon masters: dwarf male x3, human male
            new[] { 1931, 1932 },               // hippogryph masters: night elf male / female
            new[] { 1311, 2098, 1652 },         // wind rider masters: orc male, tauren male, orc male
            new[] { 2638, 12569, 3832 },        // bat handlers: undead male, female, male
            new[] { 16057, 16059 },             // dragonhawk masters: blood elf male / female
        };
        private static readonly string[] MasterFolders = { "Dwarf|Human", "NightElf", "Orc|Tauren", "Scourge", "BloodElf" };

        private static readonly Dictionary<int, string[]> NamesByDisplay = new Dictionary<int, string[]>
        {
            { 3007, new[] { "Brannoc Ironquill", "Durgan Emberstout", "Holdrik Coppervane" } },
            { 2417, new[] { "Morlun Anvilgrip", "Thradin Frostmantle", "Galdur Brightkeg" } },
            { 5037, new[] { "Kelgan Deepbarrel", "Orvik Slatebrow", "Bardin Hollowpeak" } },
            { 5128, new[] { "Edric Hallowell", "Simon Ashcombe", "Rowan Tessford" } },
            { 1931, new[] { "Aldrenor Duskwhisper", "Ilthalas Moonbrook", "Faelorn Nightbloom" } },
            { 1932, new[] { "Sylvaera Starbough", "Nerethil Leafsong", "Aynara Silverglade" } },
            { 1311, new[] { "Grushak Ironjaw", "Morrog Ashfang", "Durkath Stormhide" } },
            { 2098, new[] { "Hanoka Earthmane", "Mahka Cloudhoof", "Orun Plainstrider" } },
            { 1652, new[] { "Karnug Bloodstone", "Vrask Dustmaw", "Tharok Redspine" } },
            { 2638, new[] { "Thaddeus Blackmire", "Osric Hollowgrave", "Lucan Mortwell" } },
            { 12569, new[] { "Mirelle Ashgrave", "Beatrix Coldwater", "Agnes Dimmoor" } },
            { 3832, new[] { "Edmund Pallbrook", "Silas Wormwood", "Garrick Vane" } },
            { 16057, new[] { "Solanar Brightwing", "Kaelen Dawnspire", "Therion Sunthread" } },
            { 16059, new[] { "Liralei Sunflare", "Vessara Goldleaf", "Aneira Embersky" } },
        };

        /// <summary>The flight master's CreatureDisplayInfo id at this node (a fitting race for the node's mount), verified in the files; 0 when none.</summary>
        public int MasterDisplayId(int nodeId, WowFaction f)
        {
            int k = (int)KindAt(nodeId, f);
            var list = MasterDisplays[k];
            var cdi = Dbc.Load(_files, "CreatureDisplayInfo");
            var cmd = Dbc.Load(_files, "CreatureModelData");
            if (cdi == null || cmd == null) return list[nodeId % list.Length];
            var folders = MasterFolders[k].Split('|');
            for (int i = 0; i < list.Length; i++)
            {
                int d = list[(nodeId + i) % list.Length];
                string m = ModelOf(cdi, cmd, d);
                foreach (var fo in folders) if (m.IndexOf(@"Character\" + fo + @"\", StringComparison.OrdinalIgnoreCase) >= 0) return d;
            }
            return 0;
        }

        /// <summary>Our own name for the flight master of this node and display.</summary>
        public static string MasterName(int nodeId, int displayId)
        {
            string[] names;
            if (!NamesByDisplay.TryGetValue(displayId, out names)) return "Flight Master";
            int len = 1;
            foreach (var l in MasterDisplays) if (Array.IndexOf(l, displayId) >= 0) len = l.Length;
            return names[(nodeId / len) % names.Length];   // nodes sharing a display differ in nodeId / len
        }
    }
}
