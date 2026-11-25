using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Models;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// On the root of a WMO whose groups the 3.3.5a client does not always draw. Two jobs, both in LateUpdate:
    ///
    /// 1. Iteration 11 (lod11): portal-only groups (WmoGroupVisibility.ThroughPortal, e.g. Stormwind's 'bazaarfacade03' cathedral
    ///    stand-in): their renderers - and the doodads only they reference in MODR (WmoFactory parents them to a container per
    ///    group) - are on only while the camera is inside an owner group's box on the owner's side of the portal into them.
    ///
    /// 2. Iteration 12 (portal culling, World/WmoPortalCuller.cs): exterior groups (MOGP 0x8, not 0x10000). The camera's group is
    ///    found like the client's getGroupWmoThatCameraIsInside (WebWowViewerCpp): among the groups whose MOGP box holds the
    ///    camera, the one with the nearest floor below it - here: the nearest hit of a ray straight down (WMO -z, at most
    ///    MaxEyeAboveFloorYards) on this WMO's group MeshColliders (their collidable triangles; downward rays only hit up-facing
    ///    triangles), not below the terrain. Camera outside the root box, on no floor, or in an exterior group: every exterior
    ///    group is drawn (as before). Camera in an interior group: an
    ///    exterior group is drawn only when WmoPortalCuller's portal walk from that group sees it (Stormwind: from Cathedral Square
    ///    'holy01' no portal chain reaches an exterior group, so 'cathy_facade01', the city-wide cathedral stand-in, is off).
    ///    Only renderers are switched (the group's mesh + liquid, and the renderers of doodads only it references); colliders stay.
    ///    Recomputed when the camera moved RecomputeYards, every RedoSeconds, or RetrySeconds after finding no floor (all ours);
    ///    no per-frame allocations.
    /// </summary>
    public sealed class WmoPortalGate : MonoBehaviour
    {
        sealed class Gated
        {
            public Renderer[] Renderers;
            public GameObject Doodads;   // container of the doodads only this group references (null when none)
            public WmoPortalView[] Views;
            public bool Shown;
        }

        sealed class Exterior
        {
            public int Group;
            public Renderer[] Renderers;
            public Transform Doodads;                                  // container of the doodads only this group references
            public readonly List<Renderer> DoodadRenderers = new List<Renderer>();
            public bool Shown = true;
        }

        /// <summary>Ours: the camera must move this far (yards, WMO frame) before the camera group and the portal walk are redone.</summary>
        public const float RecomputeYards = 0.1f;
        /// <summary>Ours: seconds between rescans of a hidden group's doodad renderers (doodads spawn over several frames and
        /// M2 effects add emitters later).</summary>
        public const float DoodadRescanSeconds = 1f;
        /// <summary>Ours: a standing camera still redoes the camera group + walk this often (s), and RetrySeconds when no floor of
        /// this WMO was found (new WMO colliders are only in the physics scene after the next simulation step).</summary>
        public const float RedoSeconds = 2f, RetrySeconds = 0.25f;
        /// <summary>Ours: a camera more than this many yards above every floor of the WMO under it counts as outside (all exteriors
        /// drawn, as before). A walking or mounted eye is a few metres over its floor; higher means flying, which 3.3.5a does not
        /// allow in Azeroth - from there the client's rule only takes away the canals / harbour / gate seen over the roofs.</summary>
        public const float MaxEyeAboveFloorYards = 10f;

        readonly List<Gated> _groups = new List<Gated>();

        WmoPortalCuller _culler;
        string _path;
        readonly List<Exterior> _ext = new List<Exterior>();
        readonly Dictionary<Collider, int> _colliderGroup = new Dictionary<Collider, int>();
        bool[] _visible;
        bool _haveLast, _allShown = true, _overflowLogged;
        float _lx, _ly, _lz, _nextRescan, _redoAt;
        WowMapView _map;      // the map this WMO is placed by (terrain height for the bottom border); null for entity WMOs
        bool _mapLooked;

        static readonly RaycastHit[] Hits = new RaycastHit[64];
        static readonly HashSet<string> Logged = new HashSet<string>();

        /// <summary>Registers one group's renderers and its own doodad container (both hidden until the camera is where the
        /// client would show the group).</summary>
        public void Add(Renderer[] renderers, WmoPortalView[] views, GameObject doodads)
        {
            var g = new Gated { Renderers = renderers, Views = views, Doodads = doodads, Shown = true };
            Show(g, false);
            _groups.Add(g);
        }

        /// <summary>Iteration 12: the WMO's portal culler (one per WMO file, shared by its placements).</summary>
        public void SetCuller(WmoPortalCuller culler, string path)
        {
            _culler = culler;
            _path = path;
            _visible = culler != null ? new bool[culler.GroupCount] : null;
        }

        /// <summary>Iteration 12: a group's MeshCollider, for the camera-group ray.</summary>
        public void AddGroupCollider(Collider c, int group)
        {
            if (c != null) _colliderGroup[c] = group;
        }

        /// <summary>Iteration 12: an exterior group the culler may hide: its renderers and its own doodad container (or null).</summary>
        public void AddExterior(int group, Renderer[] renderers, Transform doodads)
        {
            _ext.Add(new Exterior { Group = group, Renderers = renderers, Doodads = doodads });
        }

        void LateUpdate()
        {
            var cam = M2Animator.FindCamera();
            bool haveCam = cam != null;
            float x = 0f, y = 0f, z = 0f;
            if (haveCam)
            {
                // Unity local (metres) -> WMO frame (yards): inverse of WowSpace.PointToUnity (x, y, z) -> (-y, z, x) * MetresPerYard
                Vector3 u = transform.InverseTransformPoint(cam.transform.position);
                x = u.z / WowSpace.MetresPerYard;
                y = -u.x / WowSpace.MetresPerYard;
                z = u.y / WowSpace.MetresPerYard;
            }
            for (int i = 0; i < _groups.Count; i++)
            {
                var g = _groups[i];
                bool see = false;
                if (haveCam)
                    for (int v = 0; v < g.Views.Length && !see; v++) see = g.Views[v].Sees(x, y, z);
                if (see != g.Shown) Show(g, see);
            }
            if (_culler != null && _ext.Count > 0) UpdateExteriors(haveCam ? cam : null, x, y, z);
        }

        void UpdateExteriors(Camera cam, float x, float y, float z)
        {
            if (cam == null || !_culler.InRootBox(x, y, z))
            {
                _haveLast = false;
                if (!_allShown) ShowAllExteriors();
                return;
            }
            if (_haveLast && Time.unscaledTime < _redoAt && Mathf.Abs(x - _lx) < RecomputeYards && Mathf.Abs(y - _ly) < RecomputeYards && Mathf.Abs(z - _lz) < RecomputeYards)
            {
                if (!_allShown && Time.unscaledTime >= _nextRescan) RescanHiddenDoodads();
                return;
            }
            _haveLast = true; _lx = x; _ly = y; _lz = z;
            _redoAt = Time.unscaledTime + RedoSeconds;

            int g = CameraGroup(cam.transform.position, x, y, z);
            if (g < 0) _redoAt = Time.unscaledTime + RetrySeconds;   // no floor yet: a just-built WMO's colliders join the physics scene next step
            if (g < 0 || _culler.IsExterior(g))
            {
                if (!_allShown) ShowAllExteriors();
                return;
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!_culler.Traverse(g, x, y, z, _visible))
            {
                if (!_overflowLogged)
                {
                    _overflowLogged = true;
                    if (Logged.Add("overflow " + _path))
                        WowLog.Warn("World: WMO " + _path + " portal walk from group " + g + " '" + _culler.GroupNames[g] + "' ran out of scratch (" + _culler.LastItems
                            + " items) - every exterior group stays drawn there");
                }
                if (!_allShown) ShowAllExteriors();
                return;
            }
            bool anyHidden = false;
            for (int i = 0; i < _ext.Count; i++)
            {
                var e = _ext[i];
                bool on = _visible[e.Group];
                if (!on) anyHidden = true;
                if (on != e.Shown) ShowExterior(e, on);
            }
            _allShown = !anyHidden;
            if (!_allShown && Time.unscaledTime >= _nextRescan) RescanHiddenDoodads();
            if (anyHidden && Logged.Add(_path))
            {
                double us = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                var shown = new System.Text.StringBuilder(); var hidden = new System.Text.StringBuilder();
                for (int i = 0; i < _ext.Count; i++)
                {
                    var sb = _visible[_ext[i].Group] ? shown : hidden;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(_ext[i].Group).Append(" '").Append(_culler.GroupNames[_ext[i].Group]).Append('\'');
                }
                WowLog.Info("World: WMO " + _path + " portal culling applies: camera in interior group " + g + " '" + _culler.GroupNames[g] + "' (MOGP 0x"
                    + _culler.Flags(g).ToString("X") + ") at (" + x.ToString("0.0") + ", " + y.ToString("0.0") + ", " + z.ToString("0.0") + "); exterior groups seen through its portals: ["
                    + shown + "], hidden: [" + hidden + "] (walk " + _culler.LastItems + " items, " + _culler.LastExteriorFrustums + " exterior frustums, " + us.ToString("0") + " us; logged once per WMO)");
            }
        }

        /// <summary>Nearest hit straight down (WMO -z) on one of this WMO's group colliders whose MOGP box holds the camera, that
        /// can hold a camera (not UNREACHABLE / ALWAYSDRAW) and that is not under the terrain (the client's bottomBorder: Map.cpp
        /// takes the ADT height under the camera when it is below the camera and getGroupWmoThatCameraIsInside only accepts floors
        /// above it); -1 when none.</summary>
        int CameraGroup(Vector3 world, float x, float y, float z)
        {
            float down = Mathf.Min((z - _culler.RootMinZ) * WowSpace.MetresPerYard + 1f, MaxEyeAboveFloorYards * WowSpace.MetresPerYard);
            if (down <= 0f) return -1;
            if (!_mapLooked) { _mapLooked = true; _map = GetComponentInParent<WowMapView>(); }
            float terrainY = float.NegativeInfinity, ground;
            if (_map != null && _map.TryGroundHeight(world, out ground) && ground < world.y) terrainY = ground;
            int n = Physics.RaycastNonAlloc(world, -transform.up, Hits, down, 1 << 0, QueryTriggerInteraction.Ignore);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                int gi;
                if (c == null || !_colliderGroup.TryGetValue(c, out gi)) continue;
                if (Hits[i].distance >= bestD || Hits[i].point.y <= terrainY || !_culler.CanHoldCamera(gi) || !_culler.InGroupBox(gi, x, y, z)) continue;
                bestD = Hits[i].distance;
                best = gi;
            }
            return best;
        }

        void ShowAllExteriors()
        {
            for (int i = 0; i < _ext.Count; i++)
                if (!_ext[i].Shown) ShowExterior(_ext[i], true);
            _allShown = true;
        }

        void ShowExterior(Exterior e, bool on)
        {
            e.Shown = on;
            foreach (var r in e.Renderers)
                if (r != null) r.enabled = on;
            if (e.Doodads == null) return;
            e.Doodads.GetComponentsInChildren(true, e.DoodadRenderers);
            for (int i = 0; i < e.DoodadRenderers.Count; i++)
                if (e.DoodadRenderers[i] != null) e.DoodadRenderers[i].enabled = on;
        }

        void RescanHiddenDoodads()
        {
            _nextRescan = Time.unscaledTime + DoodadRescanSeconds;
            for (int i = 0; i < _ext.Count; i++)
                if (!_ext[i].Shown && _ext[i].Doodads != null) ShowExterior(_ext[i], false);
        }

        static void Show(Gated g, bool on)
        {
            g.Shown = on;
            foreach (var r in g.Renderers)
                if (r != null) r.enabled = on;
            if (g.Doodads != null) g.Doodads.SetActive(on);
        }
    }
}
