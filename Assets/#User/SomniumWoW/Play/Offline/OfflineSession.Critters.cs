using System;
using System.Collections.Generic;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// WoW-style critters (iteration 5, the user 2026-09-27: "do the same as in WoW, where they are just roaming around, yellow
    /// critters"): rabbits, squirrels, deer, toads... as REAL session entities around the player. CreatureType 8 (Critter),
    /// Reaction.Neutral (yellow name), level 1, a few hit points; they never aggro, never fight back, never flee, just wander
    /// (walk to random points 10-15 yd around their spawn, pause, walk again); killing one gives no experience, no loot and no
    /// kill credit (WoW gives nothing for critters). Which critters: the zone of the player's ADT chunk (AreaTable parent chain)
    /// -> climate -> WowCritters (display ids measured). About CritterCount of them are kept 30-120 yd from the player on
    /// walkable terrain (groundZ; not steep, not under water, not inside a building's WMO box, not in a capital city), removed
    /// beyond 160 yd (a targeted one beyond 260 yd); none are spawned while the player flies a taxi. The ADT tiles (area ids,
    /// liquids, WMO boxes) are read on a thread-pool worker, never on the caller's thread.
    /// </summary>
    public sealed partial class OfflineSession
    {
        /// <summary>Critters kept around the player (0 = none). The front end sets it from WowSettings.AmbientLife.</summary>
        public static int CritterCount = DefaultCritterCount;
        public const int DefaultCritterCount = 8;
        /// <summary>Synthetic creature entries of the critters: CritterEntryBase + CritterKind.Index.</summary>
        public const int CritterEntryBase = 910000;
        /// <summary>Critters spawn this far from the player (yards) and are removed beyond CritterDespawnRange (unless targeted).</summary>
        public const float CritterSpawnMin = 30f, CritterSpawnMax = 120f, CritterDespawnRange = 160f;
        /// <summary>Each critter wanders within this many yards of its spawn point (picked per critter).</summary>
        public const float CritterWanderMin = 10f, CritterWanderMax = 15f;
        private const float CritterMaxSlope = 0.9f;        // rise per yard over 3 yd (about 42 degrees): steeper spots are skipped
        private const int CritterMaxTiles = 6;

        private readonly List<OfflineCritterSlot> _critterList = new List<OfflineCritterSlot>();
        private readonly Dictionary<int, CreatureTemplate> _critterTpl = new Dictionary<int, CreatureTemplate>();
        private readonly Dictionary<int, CritterTile> _critterTiles = new Dictionary<int, CritterTile>();
        private readonly object _critterLock = new object();
        private float _critterScan, _critterStatus = 120f;
        private int _critterSerial, _critterZone = -1, _critterWanted = -1;
        private WowClimate _critterClimate = WowClimate.Forest;
        private long _critterClock;
        private bool _critterLoading, _critterFirstLogged;
        private DbcTable _critterAreas;                    // set by the worker (under _critterLock)
        private WdtFile _critterWdt;                       // the map's WDT (worker, under _critterLock)
        private string _critterDir;
        private int _critterWdtMap = -1;
        private int _critterSpawned, _critterRemoved;
        private int _rejGround, _rejSlope, _rejWater, _rejWmo, _rejCity, _rejTile;

        private sealed class OfflineCritterSlot
        {
            public OfflineCreature C;
            public CritterKind Kind;
        }

        private sealed class CritterTile
        {
            public int Map, X, Y;
            public AdtFile Adt;                            // null = no terrain there (or unreadable)
            public float[] WmoBoxes = new float[0];        // world boxes (minX, minY, minZ, maxX, maxY, maxZ) of the tile's MODF placements
            public bool Ready;                             // written under _critterLock
            public long Used;
            public string Note;                            // worker's result line, logged on the session's thread
        }

        /// <summary>The critters alive or dead around the player right now (tests/diagnosis).</summary>
        public int CritterPresent { get { return _critterList.Count; } }

        /// <summary>True for a critter entity.</summary>
        public bool IsCritter(ulong guid)
        {
            OfflineCreature c;
            return _byGuid.TryGetValue(guid, out c) && IsCritter(c);
        }

        private static bool IsCritter(OfflineCreature c)
        {
            return c != null && c.Tpl != null && c.Tpl.Entry >= CritterEntryBase && c.Tpl.Entry < CritterEntryBase + 1000;
        }

        // ------------------------------------------------------------------ per step (Step calls this before the creatures step)

        private void StepCritters(float dt)
        {
            // Idle critters whose pause ends in this step walk to a checked point (StepWander then walks them there at their walk speed).
            for (int i = 0; i < _critterList.Count; i++)
            {
                var c = _critterList[i].C;
                if (c.Present && !c.Dead && c.Ai == AiState.Idle && c.WaitTimer <= dt) PickCritterWander(c);
            }
            _critterScan -= dt;
            if (_critterScan > 0f) return;
            _critterScan = 0.5f;
            ScanCritters(0.5f);
        }

        private void ScanCritters(float interval)
        {
            int want = Math.Max(0, Math.Min(32, CritterCount));
            if (want != _critterWanted)
            {
                if (_critterWanted >= 0 || want == 0) WowLog.Info("OfflineSession: critters " + (want > 0 ? "on (" + want + " around the player)" : "off"));
                _critterWanted = want;
            }
            var me = _me.Pos;
            for (int i = _critterList.Count - 1; i >= 0; i--)
            {
                var c = _critterList[i].C;
                if (!c.Present) { RemoveCritter(i); continue; }   // the corpse is gone
                float d = Dist2D(me, c.Pos);
                bool kept = c.Guid == _target || c == _lootCorpse;   // a targeted one stays up to the flight masters' hide range
                if (d > (kept ? FlightMasterHideRange : CritterDespawnRange)) RemoveCritter(i);
            }
            // fewer wanted (the setting): the farthest untargeted ones go
            while (_critterList.Count > want)
            {
                int far = -1; float farD = -1f;
                for (int i = 0; i < _critterList.Count; i++)
                {
                    var c = _critterList[i].C;
                    if (c.Guid == _target || c == _lootCorpse) continue;
                    float d = Dist2D(me, c.Pos);
                    if (d > farD) { farD = d; far = i; }
                }
                if (far < 0) break;
                RemoveCritter(far);
            }
            UpdateCritterClimate(me);
            if (want > 0 && _critterList.Count < want && !_player.OnTaxi) TrySpawnCritter(me);

            _critterStatus -= interval;
            if (_critterStatus <= 0f)
            {
                _critterStatus = 300f;
                int tiles;
                lock (_critterLock) tiles = _critterTiles.Count;
                WowLog.Info("OfflineSession: critters " + _critterList.Count + "/" + want + " around the player, zone " + _critterZone + " " + _critterClimate + "; spawned "
                    + _critterSpawned + ", removed " + _critterRemoved + "; spots skipped: ground " + _rejGround + ", slope " + _rejSlope + ", water " + _rejWater
                    + ", building " + _rejWmo + ", city " + _rejCity + ", tile not read yet " + _rejTile + "; " + tiles + " tiles");
            }
        }

        private void RemoveCritter(int i)
        {
            var c = _critterList[i].C;
            _critterList.RemoveAt(i);
            if (_target == c.Guid) SetTarget(0);
            if (_lootCorpse == c) CloseLoot();
            _creatures.Remove(c);
            _byGuid.Remove(c.Guid);
            c.Present = false;
            _entitiesDirty = true;
            _critterRemoved++;
        }

        private void UpdateCritterClimate(WowPos me)
        {
            if (_files == null) return;
            var t = CritterTileAt(me.X, me.Y, true);
            DbcTable areas;
            lock (_critterLock) areas = _critterAreas;
            if (t == null || t.Adt == null || areas == null) return;
            int ci = CritterChunk(t.Adt, me.X, me.Y);
            var chunk = ci >= 0 ? t.Adt.Chunks[ci] : null;
            if (chunk == null) return;
            int zone = WowCritters.ZoneOf(areas, chunk.AreaId);
            if (zone <= 0 || zone == _critterZone) return;
            _critterZone = zone;
            _critterClimate = WowCritters.ClimateOf(zone);
            var land = WowCritters.Land(_critterClimate);
            var names = new List<string>();
            foreach (var k in land) if (!names.Contains(k.Name)) names.Add(k.Name);
            var wet = WowCritters.NearWater(_critterClimate);
            WowLog.Info("OfflineSession: critters: zone " + zone + " (area " + chunk.AreaId + ") -> " + _critterClimate + ": " + string.Join(", ", names.ToArray())
                + (wet != null ? "; near water " + wet.Name : "") + (WowCritters.IsCity(areas, chunk.AreaId) ? " (a capital city: none in its streets)" : ""));
        }

        // ------------------------------------------------------------------ spawning

        private void TrySpawnCritter(WowPos me)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                double ang = _rng.NextDouble() * Math.PI * 2;
                float r2min = CritterSpawnMin * CritterSpawnMin, r2max = CritterSpawnMax * CritterSpawnMax;
                float r = (float)Math.Sqrt(r2min + (r2max - r2min) * _rng.NextDouble());   // even over the ring's area
                float x = me.X + (float)Math.Cos(ang) * r, y = me.Y + (float)Math.Sin(ang) * r, z;
                if (!CritterSpot(x, y, me.Z, true, out z)) continue;
                bool wet = CritterNearWater(x, y, 9f);
                var kind = WowCritters.Pick(_critterClimate, wet, _rng.NextDouble(), _rng.NextDouble());
                var c = SpawnCritter(kind, x, y, z);
                if (!_critterFirstLogged)
                {
                    _critterFirstLogged = true;
                    WowLog.Info("OfflineSession: first critter '" + kind.Name + "' display " + c.State.DisplayId + " at " + c.Pos + ", " + r.ToString("0") + " yd from the player ("
                        + _critterClimate + ", zone " + _critterZone + ")");
                }
                return;
            }
        }

        private OfflineCreature SpawnCritter(CritterKind kind, float x, float y, float z)
        {
            CreatureTemplate tpl;
            int entry = CritterEntryBase + kind.Index;
            if (!_critterTpl.TryGetValue(entry, out tpl))
            {
                tpl = new CreatureTemplate
                {
                    Entry = entry, Name = kind.Name, SubName = "", DisplayIds = kind.DisplayIds, MinLevel = 1, MaxLevel = 1,
                    Reaction = Reaction.Neutral, IsNpc = false, CreatureType = 8, UnitClass = 1, WalkSpeed = kind.WalkSpeed, RunSpeed = kind.WalkSpeed,
                    AggroRadius = 0f, MinMoney = 0, MaxMoney = 0
                };
                _critterTpl[entry] = tpl;
            }
            var home = new WowPos(x, y, z, (float)(_rng.NextDouble() * Math.PI * 2));
            _critterSerial = _critterSerial % 0xFFFFFF + 1;
            var c = new OfflineCreature();
            c.Tpl = tpl;
            c.Spawn = new SpawnDef { Entry = entry, Pos = home, WanderRadius = RandRange(CritterWanderMin, CritterWanderMax), RespawnSeconds = 1e9f };   // never respawns in place
            c.SpawnIndex = _critterSerial;
            c.Guid = (0xF130UL << 48) | ((ulong)(uint)entry << 24) | (ulong)(uint)_critterSerial;
            c.Home = home;
            c.State = new EntityState { Guid = c.Guid, Entry = entry, Kind = EntityKind.Creature, Name = kind.Name, SubName = "", Reaction = Reaction.Neutral, Scale = 1f };
            c.State.DisplayId = kind.DisplayIds[_rng.Next(kind.DisplayIds.Length)];
            c.BaseDisplayId = c.State.DisplayId;
            c.CombatReach = 0.75f;
            c.CanParry = false;
            c.Elite = false;
            Respawn(c);                                    // level 1, state reset, placed at home on the ground
            c.MaxHealth = kind.Health;
            c.Health = kind.Health;
            c.MaxMana = 0; c.Mana = 0;
            c.MinDamage = 0f; c.MaxDamage = 0f;
            c.State.Health = c.Health;
            c.State.MaxHealth = c.MaxHealth;
            c.State.PowerValue = 0; c.State.PowerMax = 0;
            c.State.CreatureType = 8;
            c.Pos.O = home.O;
            c.State.Pos = c.Pos;
            c.GaveXp = true;                               // WoW: no experience, no loot and no kill credit for critters (KillCreature skips all three)
            _critterList.Add(new OfflineCritterSlot { C = c, Kind = kind });
            _creatures.Add(c);
            _byGuid[c.Guid] = c;
            _entitiesDirty = true;
            _critterSpawned++;
            return c;
        }

        /// <summary>The next wander goal of an idle critter: a walkable point within its radius (and the way there's midpoint too).</summary>
        private void PickCritterWander(OfflineCreature c)
        {
            for (int k = 0; k < 4; k++)
            {
                double ang = _rng.NextDouble() * Math.PI * 2;
                float r = (float)Math.Sqrt(_rng.NextDouble()) * c.Spawn.WanderRadius;
                float x = c.Home.X + (float)Math.Cos(ang) * r, y = c.Home.Y + (float)Math.Sin(ang) * r, z, mz;
                if (Dist2D(c.Pos, new WowPos(x, y, 0f, 0f)) < 2f) continue;
                if (!CritterSpot(x, y, c.Pos.Z, false, out z)) continue;
                if (!CritterSpot((x + c.Pos.X) * 0.5f, (y + c.Pos.Y) * 0.5f, c.Pos.Z, false, out mz)) continue;
                c.MoveTarget = new WowPos(x, y, z, 0f);
                c.HasMoveTarget = true;
                c.Ai = AiState.Wander;
                return;
            }
            c.WaitTimer = 1.5f + Rand01() * 2.5f;          // nowhere good to go: look around a little longer
        }

        // ------------------------------------------------------------------ terrain checks

        /// <summary>A walkable spot: ground height known, not in a hole, not steep, not under water, not inside a building's box.</summary>
        private bool CritterSpot(float x, float y, float zHint, bool count, out float z)
        {
            z = zHint;
            CritterTile t = null;
            if (_files != null)
            {
                t = CritterTileAt(x, y, true);
                if (t == null) { if (count) _rejTile++; return false; }
                float tz;
                if (t.Adt == null || !t.Adt.TryHeight(x, y, out tz)) { if (count) _rejGround++; return false; }   // no terrain / a hole
                if (_groundZ == null) zHint = tz;
            }
            if (!CritterGround(x, y, zHint, out z)) { if (count) _rejGround++; return false; }
            float ax, bx, ay, by;
            if (!CritterGround(x - 1.5f, y, z, out ax) || !CritterGround(x + 1.5f, y, z, out bx) || !CritterGround(x, y - 1.5f, z, out ay) || !CritterGround(x, y + 1.5f, z, out by)
                || Math.Abs(bx - ax) > 3f * CritterMaxSlope || Math.Abs(by - ay) > 3f * CritterMaxSlope)
            {
                if (count) _rejSlope++;
                return false;
            }
            if (t != null)
            {
                float lz;
                if (CritterLiquid(t, x, y, out lz) && lz > z + 0.1f) { if (count) _rejWater++; return false; }
                if (CritterInsideBuilding(t, x, y, z)) { if (count) _rejWmo++; return false; }
                if (CritterInCity(t, x, y)) { if (count) _rejCity++; return false; }
            }
            return true;
        }

        /// <summary>The front end's ground height (NaN while its tile is not loaded = not here), else the ADT's terrain height.</summary>
        private bool CritterGround(float x, float y, float zHint, out float z)
        {
            z = zHint;
            if (_groundZ != null)
            {
                float g;
                try { g = _groundZ(x, y, zHint); }
                catch (Exception) { return false; }
                if (float.IsNaN(g) || float.IsInfinity(g)) return false;
                z = g;
                return true;
            }
            var t = _files != null ? CritterTileAt(x, y, false) : null;
            return t != null && t.Adt != null && t.Adt.TryHeight(x, y, out z);
        }

        private bool CritterNearWater(float x, float y, float r)
        {
            if (_files == null) return false;
            for (int k = 0; k < 6; k++)
            {
                float a = k * 1.0472f;
                float sx = x + (float)Math.Cos(a) * r, sy = y + (float)Math.Sin(a) * r, gz, lz;
                var t = CritterTileAt(sx, sy, false);
                if (t != null && t.Adt != null && t.Adt.TryHeight(sx, sy, out gz) && CritterLiquid(t, sx, sy, out lz) && lz > gz + 0.1f) return true;
            }
            return false;
        }

        private static int CritterChunk(AdtFile a, float x, float y)
        {
            float fx = (a.MaxX - x) / MapConst.ChunkSize, fy = (a.MaxY - y) / MapConst.ChunkSize;
            if (fx < 0f || fy < 0f || fx >= 16f || fy >= 16f) return -1;
            return (int)fx * 16 + (int)fy;
        }

        /// <summary>Liquid surface (yards) at (x, y): MH2O, else the MCLQ legacy liquid (the same reading as the Details' DetailTerrain.Liquid).</summary>
        private static bool CritterLiquid(CritterTile t, float x, float y, out float z)
        {
            z = 0f;
            var a = t.Adt;
            if (a == null) return false;
            int ci = CritterChunk(a, x, y);
            if (ci < 0) return false;
            var c = a.Chunks[ci];
            if (c == null) return false;
            float r = (c.X - x) / MapConst.UnitSize, q = (c.Y - y) / MapConst.UnitSize;   // rows along -X, cols along -Y (0..8)
            if (a.HasMh2o && a.Liquids != null)
            {
                var list = a.Liquids[ci];
                if (list == null) return false;
                bool found = false;
                foreach (var li in list)
                {
                    float cx = q - li.XOffset, cy = r - li.YOffset;
                    if (cx < 0f || cy < 0f || cx >= li.Width || cy >= li.Height) continue;
                    int ix = (int)cx, iy = (int)cy;
                    if (!li.CellExists(ix, iy)) continue;
                    float u = cx - ix, v = cy - iy;
                    float h = (li.HeightAt(ix, iy) * (1 - u) + li.HeightAt(ix + 1, iy) * u) * (1 - v)
                              + (li.HeightAt(ix, iy + 1) * (1 - u) + li.HeightAt(ix + 1, iy + 1) * u) * v;
                    if (!found || h > z) z = h;
                    found = true;
                }
                return found;
            }
            var l = c.LegacyLiquid;
            if (l == null) return false;
            int tx = (int)q, ty = (int)r;
            if (tx < 0 || ty < 0 || tx > 7 || ty > 7 || !l.TileExists(tx, ty)) return false;
            z = (l.Heights[ty * 9 + tx] + l.Heights[ty * 9 + tx + 1] + l.Heights[(ty + 1) * 9 + tx] + l.Heights[(ty + 1) * 9 + tx + 1]) * 0.25f;
            return true;
        }

        /// <summary>The spot's MCNK area is a capital city or one of its subzones (AreaTable flag 0x100, measured).</summary>
        private bool CritterInCity(CritterTile t, float x, float y)
        {
            DbcTable areas;
            lock (_critterLock) areas = _critterAreas;
            if (areas == null || t.Adt == null) return false;
            int ci = CritterChunk(t.Adt, x, y);
            var ch = ci >= 0 ? t.Adt.Chunks[ci] : null;
            return ch != null && WowCritters.IsCity(areas, ch.AreaId);
        }

        /// <summary>Inside (or within 2 yd of) a building's WMO placement box; boxes over 160 x 160 yd (cities, mountains) do not count.</summary>
        private static bool CritterInsideBuilding(CritterTile t, float x, float y, float z)
        {
            var b = t.WmoBoxes;
            const float m = 2f;
            for (int i = 0; i + 5 < b.Length; i += 6)
            {
                if (x < b[i] - m || x > b[i + 3] + m || y < b[i + 1] - m || y > b[i + 4] + m || z < b[i + 2] - m || z > b[i + 5] + m) continue;
                if ((b[i + 3] - b[i]) * (b[i + 4] - b[i + 1]) > 160f * 160f) continue;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ ADT tiles (thread-pool worker)

        /// <summary>The tile under (x, y) when read; null while not (request = queue it; one read at a time).</summary>
        private CritterTile CritterTileAt(float x, float y, bool request)
        {
            int tx, ty;
            MapConst.WorldToTile(x, y, out tx, out ty);
            if (tx < 0 || ty < 0 || tx > 63 || ty > 63) return null;
            int map = _zone.MapId;
            int key = (map * 64 + tx) * 64 + ty;
            CritterTile t;
            string note = null;
            lock (_critterLock)
            {
                if (_critterTiles.TryGetValue(key, out t))
                {
                    t.Used = ++_critterClock;
                    if (!t.Ready) return null;
                    note = t.Note;
                    t.Note = null;
                }
                else
                {
                    if (!request || _critterLoading) return null;
                    t = new CritterTile { Map = map, X = tx, Y = ty, Used = ++_critterClock };
                    _critterTiles[key] = t;
                    _critterLoading = true;
                    while (_critterTiles.Count > CritterMaxTiles)
                    {
                        int oldKey = -1; long oldest = long.MaxValue;
                        foreach (var kv in _critterTiles) if (kv.Value.Ready && kv.Value.Used < oldest) { oldest = kv.Value.Used; oldKey = kv.Key; }
                        if (oldKey < 0) break;
                        _critterTiles.Remove(oldKey);
                    }
                }
            }
            if (note != null) { WowLog.Info("OfflineSession: critters: tile " + note); return t; }
            if (t.Ready) return t;
            try { ThreadPool.QueueUserWorkItem(LoadCritterTile, t); }
            catch (Exception e)
            {
                lock (_critterLock) { t.Ready = true; t.Note = null; _critterLoading = false; }
                WowLog.Warn("OfflineSession: critters: no worker thread (" + e.Message + ")");
            }
            return null;
        }

        /// <summary>Worker: reads one ADT (AreaTable and the WDT the first time), keeps area ids, heights, liquids and WMO boxes.</summary>
        private void LoadCritterTile(object state)
        {
            var t = (CritterTile)state;
            AdtFile adt = null;
            float[] boxes = new float[0];
            DbcTable areas = null;
            string note;
            var t0 = DateTime.UtcNow;
            try
            {
                areas = Dbc.Load(_files, "AreaTable");
                WdtFile wdt; string dir;
                lock (_critterLock) { wdt = _critterWdtMap == t.Map ? _critterWdt : null; dir = _critterWdtMap == t.Map ? _critterDir : null; }
                if (wdt == null)
                {
                    dir = WowMapData.Directory(_files, t.Map);
                    wdt = dir != null ? WowMapData.LoadWdt(_files, dir) : null;
                    lock (_critterLock) { _critterWdt = wdt; _critterDir = dir; _critterWdtMap = t.Map; }
                }
                byte[] bytes = dir != null && wdt != null && wdt.HasTile(t.X, t.Y) ? _files.Read(WdtFile.AdtPath(dir, t.X, t.Y)) : null;
                if (bytes != null)
                {
                    adt = AdtFile.Parse(bytes, t.X, t.Y, wdt.BigAlpha);
                    foreach (var c in adt.Chunks)
                    {
                        if (c == null) continue;
                        for (int i = 0; i < c.Alpha.Length; i++) c.Alpha[i] = null;   // not needed here: keep the tile small
                        c.Shadow = null;
                        c.Mccv = null;
                    }
                    boxes = new float[adt.Wmos.Length * 6];
                    for (int i = 0; i < adt.Wmos.Length; i++)
                    {
                        var w = adt.Wmos[i];
                        float x0, y0, z0, x1, y1, z1;
                        MapConst.PlacementToWorld(w.MinX, w.MinY, w.MinZ, out x0, out y0, out z0);
                        MapConst.PlacementToWorld(w.MaxX, w.MaxY, w.MaxZ, out x1, out y1, out z1);
                        boxes[i * 6] = Math.Min(x0, x1); boxes[i * 6 + 1] = Math.Min(y0, y1); boxes[i * 6 + 2] = Math.Min(z0, z1);
                        boxes[i * 6 + 3] = Math.Max(x0, x1); boxes[i * 6 + 4] = Math.Max(y0, y1); boxes[i * 6 + 5] = Math.Max(z0, z1);
                    }
                    note = dir + " " + t.X + "_" + t.Y + " read (" + adt.Wmos.Length + " WMO boxes, " + (adt.HasMh2o ? "MH2O" : "MCLQ") + " liquids), " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms";
                }
                else note = t.Map + " " + t.X + "_" + t.Y + ": no terrain";
            }
            catch (Exception e)
            {
                adt = null;
                note = t.Map + " " + t.X + "_" + t.Y + " failed: " + e.GetType().Name + " " + e.Message;
            }
            lock (_critterLock)
            {
                t.Adt = adt;
                t.WmoBoxes = boxes;
                t.Note = note;
                t.Ready = true;
                _critterLoading = false;
                if (areas != null) _critterAreas = areas;
            }
        }
    }
}
