using System;
using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Details
{
    /// <summary>
    /// Visual-only sky life (not session entities, no colliders, nothing networked): loose flocks of birds circling overhead as
    /// scenery. They do not react to the player: each flock circles and drifts around its own spot and is only re-placed when
    /// the player has left it far behind (more than 120 yd). Models are CreatureDisplayInfo ids (CritterTable, MEASURED to have
    /// Fly data) built with WowCreatures.Build and recycled per display. Iteration 5 (the user: "do the same as in WoW"): the
    /// ground critters are no longer here - they are real neutral session entities that wander (OfflineSession.Critters).
    /// Everything is kept in WoW coordinates and placed through WowSpace every frame; the GameObjects live under a container
    /// child of the WoW Game object, which WowOrigin shifts on a rebase (so nothing to shift here). Setting: AmbientLife;
    /// counts by EffectsQuality.
    /// </summary>
    internal sealed class AmbientLife
    {
        private const int AnimStand = 0, AnimWalk = 4, AnimFly = 135;   // AnimationData.dbc ids (Details report)
        private const float ReplaceYd = 120f;                          // a flock this far from the player is re-placed

        private sealed class Bird
        {
            public GameObject Go;
            public M2Animator Anim;
            public float Phase, Radius, Height, Bob;
        }

        private sealed class Flock
        {
            public int Display;
            public float HX, HY;                                       // the flock's own spot: it drifts around here
            public float CX, CY, Alt, Radius, Omega, Phase, TX, TY, Retarget;
            public readonly List<Bird> Birds = new List<Bird>();
        }

        private readonly WowFiles _files;
        private readonly DetailTerrain _terrain;
        private readonly DetailsWorker _worker;
        private readonly Transform _root;
        private readonly Dictionary<int, Stack<GameObject>> _pool = new Dictionary<int, Stack<GameObject>>();
        private readonly HashSet<int> _ready = new HashSet<int>(), _requested = new HashSet<int>(), _failed = new HashSet<int>();
        private readonly List<Flock> _flocks = new List<Flock>();
        private readonly System.Random _rng = new System.Random(20260926);
        private DbcTable _areas;
        private WowClimate _climate = WowClimate.Forest;
        private int _zone = -1, _map = -1, _builds;
        private float _nextZone, _nextBuild;
        private bool _loggedFirstBird;
        private int _skyOffMap = int.MinValue;   // iteration 11 (world): the map the "no flocks here" state was logged for
        private bool _skyOff;
        public int Spawned, Recycled;

        public AmbientLife(WowFiles files, DetailTerrain terrain, DetailsWorker worker, Transform root)
        {
            _files = files; _terrain = terrain; _worker = worker; _root = root;
        }

        public DbcTable Areas { set { _areas = value; } }
        public int BirdCount { get { int n = 0; foreach (var f in _flocks) n += f.Birds.Count; return n; } }
        public WowClimate Climate { get { return _climate; } }
        public int Zone { get { return _zone; } }

        private float R01() { return (float)_rng.NextDouble(); }

        // ------------------------------------------------------------------------------------------ models

        /// <summary>Parses the display's model on the worker first (WowCreatures.Build then only uploads on the main thread).</summary>
        private void Request(int display)
        {
            if (_ready.Contains(display) || _requested.Contains(display) || _failed.Contains(display)) return;
            _requested.Add(display);
            var files = _files;
            _worker.Enqueue(() =>
            {
                var cd = WowModelDb.ResolveCreature(files, display);
                bool ok = cd.Error == null && !string.IsNullOrEmpty(cd.ModelPath) && M2Factory.Preload(files, cd.ModelPath);
                string err = cd.Error ?? (ok ? null : "model " + cd.ModelPath + " not loadable");
                _worker.Post(() =>
                {
                    _requested.Remove(display);
                    if (ok) _ready.Add(display);
                    else { _failed.Add(display); WowLog.Warn("Details: bird display " + display + " skipped: " + err); }
                });
            });
        }

        private GameObject Take(int display)
        {
            Stack<GameObject> s;
            if (_pool.TryGetValue(display, out s))
            {
                while (s.Count > 0)
                {
                    var g = s.Pop();
                    if (g == null) continue;
                    g.SetActive(true);
                    return g;
                }
            }
            if (_failed.Contains(display)) return null;
            if (!_ready.Contains(display)) { Request(display); return null; }
            // one model build per frame and at most five a second (an animated build costs milliseconds; pooled reuse is free)
            if (_builds >= 1 || Time.unscaledTime < _nextBuild) return null;
            _builds++;
            _nextBuild = Time.unscaledTime + 0.2f;
            GameObject go = null;
            try { go = WowCreatures.Build(_files, display); }
            catch (Exception e) { WowLog.Warn("Details: WowCreatures.Build(" + display + ") threw " + e.Message); }
            if (go == null) { _failed.Add(display); return null; }
            go.name = "Details life " + display;
            go.transform.SetParent(_root, true);
            float extra = CritterTable.ExtraScale(display);
            if (extra != 1f) go.transform.localScale *= extra;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);   // visual only
            return go;
        }

        private void Give(int display, GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            Stack<GameObject> s;
            if (!_pool.TryGetValue(display, out s)) { s = new Stack<GameObject>(); _pool[display] = s; }
            s.Push(go);
            Recycled++;
        }

        private static void Play(M2Animator a, int anim, ref int playing)
        {
            if (a == null || playing == anim) return;
            int id = anim;
            if (!a.Has(id)) id = anim == AnimFly ? AnimWalk : AnimStand;
            if (!a.Has(id)) id = AnimStand;
            a.Play(id, true);
            playing = anim;
        }

        // ------------------------------------------------------------------------------------------ tools (render lab)

        internal bool TryGetBird(out Vector3 unityPos, out Vector3 forward)
        {
            foreach (var f in _flocks)
                foreach (var b in f.Birds)
                    if (b.Go != null) { unityPos = b.Go.transform.position; forward = b.Go.transform.forward; return true; }
            unityPos = Vector3.zero; forward = Vector3.forward;
            return false;
        }

        // ------------------------------------------------------------------------------------------ per frame

        public void Update(int map, float px, float py, float pz, float timeOfDay, bool onTaxi, float dt, float now)
        {
            _builds = 0;
            if (map != _map) { Clear(); _map = map; }
            if (now >= _nextZone)
            {
                _nextZone = now + 4f;
                int area = _terrain.AreaId(map, px, py);
                int zone = CritterTable.ZoneOf(_areas, area);
                if (zone > 0 && zone != _zone)
                {
                    _zone = zone;
                    var c = CritterTable.ClimateOf(zone);
                    _climate = c;
                    Request(CritterTable.Bird(c, false));
                    Request(CritterTable.Bird(c, true));
                    WowLog.Info("Details: zone " + zone + " (area " + area + ") -> " + c + " sky: birds " + CritterTable.Bird(c, false) + " / night " + CritterTable.Bird(c, true));
                }
            }
            int q = Mathf.Clamp(WowSettings.EffectsQuality, 0, 2);
            int wantFlocks = q == 0 ? 1 : 2, perFlock = q == 0 ? 3 : q == 1 ? 4 : 5;
            if (SkyLifeOff(map)) wantFlocks = 0;   // iteration 11 (world): no flocks in dungeons, raids, battlegrounds, arenas or WMO-only maps (ours)
            bool night = timeOfDay >= 20f || timeOfDay < 6f;
            UpdateFlocks(map, px, py, pz, dt, now, night, wantFlocks, perFlock);
        }

        /// <summary>Iteration 11 (world, proposal-world.md 2.4): our own bird flocks do not belong in dungeons, raids, battlegrounds, arenas
        /// (the session's Map.dbc InstanceType != 0) or WMO-only maps such as the Deeprun Tram (ours). Logged once per map.</summary>
        private bool SkyLifeOff(int map)
        {
            var s = WowPlay.Session;
            bool off = (s != null && s.Player != null && s.Player.MapInstanceType != 0) || WowInstances.IsWmoOnly(_files, map);
            if (map != _skyOffMap || off != _skyOff)
            {
                _skyOffMap = map;
                _skyOff = off;
                if (off) WowLog.Info("Details: map " + map + " is a dungeon / raid / battleground / arena or WMO-only - no bird flocks (ours)");
            }
            return off;
        }

        // ------------------------------------------------------------------------------------------ birds

        private void UpdateFlocks(int map, float px, float py, float pz, float dt, float now, bool night, int wantFlocks, int perFlock)
        {
            int display = CritterTable.Bird(_climate, night);
            while (_flocks.Count > wantFlocks) RemoveFlock(_flocks.Count - 1);
            while (_flocks.Count < wantFlocks) _flocks.Add(NewFlock(px, py, display, 55f + R01() * 25f));
            for (int f = 0; f < _flocks.Count; f++)
            {
                var fl = _flocks[f];
                float dx = fl.CX - px, dy = fl.CY - py;
                bool far = dx * dx + dy * dy > ReplaceYd * ReplaceYd;
                if (far || fl.Display != display)
                {
                    // left far behind (or a new species at night/day change): a new flock somewhere around the player
                    RemoveFlock(f);
                    fl = NewFlock(px, py, display, 70f);
                    _flocks.Insert(f, fl);
                }
                // loose drift around the flock's own spot (not towards the player)
                if (now >= fl.Retarget)
                {
                    fl.Retarget = now + 12f + R01() * 10f;
                    float a = R01() * 6.2831853f, r = R01() * 45f;
                    fl.TX = fl.HX + (float)Math.Cos(a) * r; fl.TY = fl.HY + (float)Math.Sin(a) * r;
                }
                float mx = fl.TX - fl.CX, my = fl.TY - fl.CY, len = (float)Math.Sqrt(mx * mx + my * my);
                if (len > 0.5f) { float s = Math.Min(len, 2.5f * dt); fl.CX += mx / len * s; fl.CY += my / len * s; }
                float gz;
                float ground = _terrain.Height(map, fl.CX, fl.CY, out gz) ? Math.Max(gz, pz - 5f) : pz;
                fl.Phase += fl.Omega * dt;
                while (fl.Birds.Count < perFlock)
                {
                    var go = Take(fl.Display);
                    if (go == null) break;
                    var b = new Bird { Go = go, Anim = go.GetComponentInChildren<M2Animator>(), Phase = (R01() - 0.5f) * 1.8f, Radius = (R01() - 0.5f) * 8f, Height = (R01() - 0.5f) * 6f, Bob = R01() * 6.28f };
                    int playing = -1;
                    Play(b.Anim, AnimFly, ref playing);
                    if (b.Anim != null) b.Anim.Speed = 0.9f + R01() * 0.2f;
                    fl.Birds.Add(b);
                    Spawned++;
                    if (!_loggedFirstBird)
                    {
                        _loggedFirstBird = true;
                        WowLog.Info("Details: first bird display " + fl.Display + ", flock circling r " + fl.Radius.ToString("0") + " yd, " + (fl.Alt).ToString("0") + " yd above ground");
                    }
                }
                for (int i = 0; i < fl.Birds.Count; i++)
                {
                    var b = fl.Birds[i];
                    if (b.Go == null) continue;
                    float th = fl.Phase + b.Phase, r = Math.Max(6f, fl.Radius + b.Radius);
                    float bx = fl.CX + (float)Math.Cos(th) * r, by = fl.CY + (float)Math.Sin(th) * r;
                    float bz = ground + fl.Alt + b.Height + (float)Math.Sin(now * 0.7f + b.Bob) * 1.2f;
                    // tangent of the circle in WoW space, then Unity yaw; bank into the turn
                    float sgn = fl.Omega >= 0f ? 1f : -1f;
                    float tx = -(float)Math.Sin(th) * sgn, ty = (float)Math.Cos(th) * sgn;
                    Vector3 fwd = WowSpace.DirToUnity(tx, ty, 0f);
                    Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, 18f * sgn);
                    b.Go.transform.SetPositionAndRotation(WowSpace.ToUnity(map, bx, by, bz), rot);
                }
            }
        }

        private Flock NewFlock(float px, float py, int display, float dist)
        {
            float a = R01() * 6.2831853f;
            bool vulture = display == CritterTable.CarrionBird;
            var fl = new Flock
            {
                Display = display,
                CX = px + (float)Math.Cos(a) * dist,
                CY = py + (float)Math.Sin(a) * dist,
                Alt = vulture ? 50f + R01() * 15f : 40f + R01() * 12f,   // above the Elwynn canopy (the lab showed crows in 30 m trees at 30 yd)
                Radius = vulture ? 25f + R01() * 15f : 12f + R01() * 14f,
            };
            float speed = vulture ? 6f : 8f;   // yd/s along the circle (the Fly sequences carry no MoveSpeed)
            fl.Omega = speed / fl.Radius * (R01() < 0.5f ? -1f : 1f);
            fl.Phase = R01() * 6.28f;
            fl.HX = fl.CX; fl.HY = fl.CY;
            fl.TX = fl.CX; fl.TY = fl.CY;
            return fl;
        }

        private void RemoveFlock(int i)
        {
            var fl = _flocks[i];
            _flocks.RemoveAt(i);
            foreach (var b in fl.Birds) Give(fl.Display, b.Go);
            fl.Birds.Clear();
        }

        /// <summary>Destroys every bird (pooled ones too).</summary>
        public void Clear()
        {
            foreach (var f in _flocks) foreach (var b in f.Birds) if (b.Go != null) UnityEngine.Object.Destroy(b.Go);
            _flocks.Clear();
            foreach (var s in _pool.Values) foreach (var g in s) if (g != null) UnityEngine.Object.Destroy(g);
            _pool.Clear();
            _zone = -1;
            _nextZone = 0f;
        }

        public string StatusLine
        {
            get { return "life " + _climate + " (zone " + _zone + "): " + BirdCount + " birds in " + _flocks.Count + " flocks; spawned " + Spawned + ", recycled " + Recycled + ", failed displays " + _failed.Count; }
        }
    }
}
