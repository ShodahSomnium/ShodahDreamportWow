using System;
using System.Collections.Generic;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Game.Audio;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;
using WmoGroupTag = SomniumSpace.Worlds.SomniumWoW.World.Tags.WmoGroupInfo;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Mounts
{
    /// <summary>
    /// Iteration 7 (lead contract PLAN7 s3.7; owner Body): where the player stands - indoors / outdoors, area, liquid - by
    /// AzerothCore's rule (Map.cpp GetFullTerrainStatusForPosition, quoted in PLAN7 s4.4):
    /// - a WMO floor counts only when the feet are inside that group's box and the floor is above the terrain height (or the feet
    ///   are below the terrain, e.g. Ironforge): `fuzzyGe(z, vmapFloor - tol) &amp;&amp; (fuzzyLt(z, gridHeight - tol) || vmapFloor &gt; gridHeight)`,
    ///   then `outdoors = mogpFlags &amp; 0x8`, overridden by the WMOAreaTable row's flags 4 (outdoors) / 2 (indoors); the row's area
    ///   when it names one, else the terrain's;
    /// - otherwise the terrain (MCNK) area and `(AreaTable flags &amp; (INSIDE|OUTSIDE)) != INSIDE` (MountAreaRules.TerrainOutdoors);
    /// - terrain liquid (MH2O / MCLQ through WowAudioDriver.Ground) counts unless the WMO group is an interior one (0x2000:
    ///   `useGridLiquid = !IsInWMOInterior(mogpFlags)`) or the WMO floor is above the liquid; WMO liquids are deferred (PLAN7 s7).
    /// The floor under the feet: one ray on layer 0 (terrain tiles and WMO groups; M2 doodad colliders are skipped as AC's area
    /// query skips them), the highest WMO-group hit that is not above the feet. Sampled every 0.2 s (on foot too);
    /// IWowSession.SetPlayerPlace on a change; "Place: ..." logged on each indoor/outdoor, area or WMO change.
    /// </summary>
    public sealed class MountPlace
    {
        // ---- ours (logged once)
        private const float SampleEvery = 0.2f;          // PLAN7 s5.4: 5 Hz
        private const float RayStartAbove = 0.5f;        // m above the feet (PLAN7 says 2 m; 0.5 m so a low ceiling / lintel between 0.5 and 2 m
                                                         // cannot hide the floor: a mesh collider reports only its FIRST hit along the ray)
        private const float RayDown = 120f;              // m below the feet searched for a WMO floor (a flyer higher up is outdoors anyway)
        private const float FloorAboveFeet = 0.6f;       // m: a "floor" above this is an overhang, not what we stand on
        private const float BoxSlack = 0.5f;             // m around a group's collider box for AC's "inside the group's box" test
        private const float Tolerance = 0.05f;           // AC GROUND_HEIGHT_TOLERANCE (SharedDefines.h), yards (used on yards)
        private const float LiquidChange = 0.05f;        // yd: a smaller change of liquid depth is not reported
        private const uint MogpInterior = 0x2000;        // AC Map.cpp IsInWMOInterior
        private const int MaxHits = 16;

        private static bool _constantsLogged;
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];

        private PlayerPlace _current;
        private bool _sent, _interiorWmo, _unknownLogged, _lastOutdoors, _lastInWmo, _lastKnown;
        private int _lastArea = int.MinValue, _lastGroupId = int.MinValue, _lastWmoId = int.MinValue;
        private float _next = -1f;
        private int _loadingTile;   // 0 idle, 1 a Ground tile is being read on a worker (Interlocked)
        private int _changes;

        public PlayerPlace Current { get { return _current; } }

        /// <summary>5 Hz (on foot too): samples the spot and calls s.SetPlayerPlace on a change.</summary>
        public void Tick(IWowSession s, int mapId, Vector3 feet, float now)
        {
            if (!_constantsLogged)
            {
                _constantsLogged = true;
                WowLog.Info("Place: ours - sampled every " + SampleEvery + " s, ray from " + RayStartAbove + " m above the feet down " + RayDown
                    + " m (layer 0, WMO groups only), floor at most " + FloorAboveFeet + " m above the feet, group box slack " + BoxSlack
                    + " m; AC tolerance " + Tolerance + " yd; terrain liquid only (WMO liquids deferred)");
            }
            if (now < _next) return;
            _next = now + SampleEvery;
            WmoGroupTag g;
            bool haveRow;
            int rowFlags;
            var p = Sample(mapId, feet, out g, out haveRow, out rowFlags);
            if (_sent && !Changed(p)) return;
            bool placeChanged = !_sent || p.Known != _lastKnown || p.Outdoors != _lastOutdoors || p.InWmo != _lastInWmo || p.AreaId != _lastArea
                || (g != null ? g.GroupId : -1) != _lastGroupId || (g != null ? g.WmoId : -1) != _lastWmoId;
            p.Where = Describe(p, g, haveRow, rowFlags);
            bool wasWet = _sent && _current.LiquidDepth > 0f;
            _current = p;
            _sent = true;
            _lastKnown = p.Known; _lastOutdoors = p.Outdoors; _lastInWmo = p.InWmo; _lastArea = p.AreaId;
            _lastGroupId = g != null ? g.GroupId : -1; _lastWmoId = g != null ? g.WmoId : -1;
            _changes++;
            if (placeChanged)
            {
                if (!p.Known) { if (!_unknownLogged) { _unknownLogged = true; WowLog.Info("Place: unknown yet (" + p.Where + ") - sessions treat it as outdoors and dry"); } }
                else WowLog.Info("Place: " + (p.Outdoors ? "outdoors" : "indoors") + " (" + p.Where + ", area " + p.AreaId + ")");
            }
            else if ((p.LiquidDepth > 0f) != wasWet)
                WowLog.Info(p.LiquidDepth > 0f ? "Place: in liquid - " + p.LiquidDepth.ToString("0.00") + " yd above the feet (surface z " + p.LiquidSurfaceZ.ToString("0.00") + ")" : "Place: out of the liquid");
            if (s != null)
            {
                try { s.SetPlayerPlace(p); }
                catch (Exception e) { WowLog.Warn("Place: SetPlayerPlace threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        /// <summary>Every frame for the motor: the terrain liquid surface (Unity y) at a point; false = dry / unknown (and inside a
        /// WMO interior group, where AC ignores terrain liquid).</summary>
        public bool TryLiquid(int mapId, Vector3 unityPos, out float surfaceY)
        {
            surfaceY = 0f;
            if (mapId < 0 || (_sent && _interiorWmo)) return false;
            var ground = WowAudioDriver.Ground;
            if (ground == null) return false;
            float wx, wy, wz;
            WowSpace.ToWow(mapId, unityPos, out wx, out wy, out wz);
            GroundSample q;
            try { q = ground.Query(mapId, wx, wy); }
            catch (Exception) { return false; }
            if (!q.Known) { RequestTile(ground, mapId, wx, wy); return false; }
            if (!q.HasLiquid) return false;
            surfaceY = WowSpace.ToUnity(mapId, wx, wy, q.LiquidZ).y;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ private

        private PlayerPlace Sample(int mapId, Vector3 feet, out WmoGroupTag group, out bool haveRow, out int rowFlags)
        {
            var p = new PlayerPlace();
            p.Outdoors = true;   // unknown = outdoors (the contract: sessions treat Known false as outdoors and dry) - so a lost sample never reads as "went indoors"
            group = null; haveRow = false; rowFlags = 0;
            _interiorWmo = false;
            if (mapId < 0) return p;
            float wx, wy, wz;
            WowSpace.ToWow(mapId, feet, out wx, out wy, out wz);

            // the terrain under the feet (AC's grid map: height, area, liquid)
            var ground = WowAudioDriver.Ground;
            GroundSample q = default(GroundSample);
            bool groundKnown = false;
            if (ground != null)
            {
                try { q = ground.Query(mapId, wx, wy); groundKnown = q.Known; }
                catch (Exception) { groundKnown = false; }
                if (!groundKnown) RequestTile(ground, mapId, wx, wy);
            }
            float terrainY = 0f;
            bool haveTerrainY = false;
            var game = WowGame.Current;
            var map = game != null ? game.Map : null;
            if (map != null && map.TryGroundHeight(feet, out terrainY)) haveTerrainY = true;
            else if (groundKnown && q.HasGround) { terrainY = WowSpace.ToUnity(mapId, wx, wy, q.GroundZ).y; haveTerrainY = true; }

            // the WMO floor under the feet
            float floorY = 0f;
            WmoGroupTag g = FindWmoFloor(feet, out floorY);
            float tolM = Tolerance * WowSpace.MetresPerYard;
            bool useWmo = g != null && (!haveTerrainY || feet.y < terrainY - tolM || floorY > terrainY);

            int terrainArea = groundKnown ? q.AreaId : 0;
            if (useWmo)
            {
                group = g;
                int area;
                haveRow = WmoAreaTable.Get(WowPlay.Files).TryGet(g.WmoId, g.NameSet, g.GroupId, out rowFlags, out area);
                p.Known = true;
                p.InWmo = true;
                p.Outdoors = WmoAreaTable.Outdoors(g.MogpFlags, haveRow, rowFlags);
                p.AreaId = haveRow && area != 0 ? area : terrainArea;   // AC: if (!data.areaId) data.areaId = gridAreaId
                _interiorWmo = (g.MogpFlags & MogpInterior) != 0;
            }
            else if (groundKnown)
            {
                p.Known = true;
                p.AreaId = terrainArea;
                p.Outdoors = MountAreaRules.TerrainOutdoors(terrainArea);
            }

            // terrain liquid: not inside a WMO interior group, and only above a WMO floor (AC: gridLiquid.Level > wmoData->floorZ)
            if (groundKnown && q.HasLiquid && !_interiorWmo)
            {
                float floorZ = float.NegativeInfinity;
                if (useWmo) { float fx, fy, fz; WowSpace.ToWow(mapId, new Vector3(feet.x, floorY, feet.z), out fx, out fy, out fz); floorZ = fz; }
                float depth = q.LiquidZ - wz;
                if (depth > 0f && q.LiquidZ > floorZ)
                {
                    p.LiquidDepth = depth;
                    p.LiquidSurfaceZ = q.LiquidZ;
                }
            }
            return p;
        }

        /// <summary>The highest WMO-group surface under the feet (not above them) whose collider box holds the feet, or null.</summary>
        internal static WmoGroupTag FindWmoFloor(Vector3 feet, out float floorY)   // duelsnd: internal - AudioMusic finds the WMO area ambience with it
        {
            floorY = 0f;
            Vector3 from = feet + Vector3.up * RayStartAbove;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, RayStartAbove + RayDown, 1, QueryTriggerInteraction.Ignore);
            WmoGroupTag best = null;
            float bestY = float.NegativeInfinity;
            for (int i = 0; i < n && i < Hits.Length; i++)
            {
                var c = Hits[i].collider;
                if (c == null) continue;
                float y = Hits[i].point.y;
                if (y > feet.y + FloorAboveFeet || y <= bestY) continue;
                WmoGroupTag info;
                if (!c.TryGetComponent(out info) || info == null) continue;
                var box = c.bounds;
                box.Expand(2f * BoxSlack);
                if (!box.Contains(feet)) continue;   // AC GroupModel::IsInsideObject: the group's box must hold the position
                best = info;
                bestY = y;
            }
            if (best != null) floorY = bestY;
            return best;
        }

        private bool Changed(PlayerPlace p)
        {
            if (p.Known != _current.Known || p.Outdoors != _current.Outdoors || p.InWmo != _current.InWmo || p.AreaId != _current.AreaId) return true;
            if ((p.LiquidDepth > 0f) != (_current.LiquidDepth > 0f)) return true;
            return Math.Abs(p.LiquidDepth - _current.LiquidDepth) > LiquidChange;
        }

        private static string Describe(PlayerPlace p, WmoGroupTag g, bool haveRow, int rowFlags)
        {
            if (g != null && p.InWmo)
                return "WMO " + FileName(g.RootPath) + " group '" + (g.GroupName ?? "") + "' (id " + g.GroupId + ", MOGP 0x" + g.MogpFlags.ToString("X")
                    + ", WMO " + g.WmoId + " name set " + g.NameSet + (haveRow ? ", WMOAreaTable flags 0x" + rowFlags.ToString("X") : ", no WMOAreaTable row: the MOGP 0x8 bit decides") + ")";
            if (!p.Known) return WowAudioDriver.Ground == null ? "no terrain reader yet" : "terrain tile not read yet";
            string name = null;
            try { name = MountAreaRules.AreaName(p.AreaId); } catch (Exception) { name = null; }
            return "terrain area " + p.AreaId + (string.IsNullOrEmpty(name) ? "" : " '" + name + "'");
        }

        private static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            int i = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return i >= 0 ? path.Substring(i + 1) : path;
        }

        /// <summary>Reads the terrain tile the ground reader lacks on a worker (one at a time; WowGround is thread-safe).</summary>
        private void RequestTile(WowGround ground, int mapId, float wx, float wy)
        {
            if (Interlocked.CompareExchange(ref _loadingTile, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { ground.LoadTile(mapId, wx, wy); }
                catch (Exception) { }
                finally { Interlocked.Exchange(ref _loadingTile, 0); }
            });
        }
    }
}
