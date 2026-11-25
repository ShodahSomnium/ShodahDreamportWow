using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The per-player floating origin (PLAN4 section 2). Unity (0, 0, 0) is <see cref="WowSpace.Current"/>, a point of a
    /// 256-yard grid near the local avatar. When the avatar gets more than <see cref="HorizontalLimit"/> m (horizontal) or
    /// <see cref="VerticalLimit"/> m (vertical) from it, the origin moves to the avatar's grid cell ("rebase"):
    /// 1. the avatar is teleported by -delta (DoTeleportToPoint; the client applies it a few frames later - until then
    ///    nothing moves, everything stays consistent in the old frame),
    /// 2. in the frame it has landed (checked in the done callback, Update, LateUpdate and before each camera renders):
    ///    the origin moves, every WoW-world object is shifted by -delta (see <see cref="ShiftWorld"/>), physics is synced,
    ///    and <see cref="WowSpace.Rebased"/> fires so every system shifts the Unity positions it keeps.
    /// While <see cref="AvatarDriven"/> is set (a flight or seat writes the avatar position directly every frame) there is
    /// no teleport: the rebase is committed at once and the driver moves the avatar by -delta in its Rebased handler.
    ///
    /// WHAT IS SHIFTED (owners: do NOT move these transforms yourselves in Rebased, shift only your own Unity-space state):
    /// every direct child of the WoW Game object (this component's GameObject), except anything under a WowRoom (the black
    /// room is not WoW world) and <see cref="Exclude"/>d transforms. A child that sits exactly at (0, 0, 0) with children
    /// and no renderer or collider of its own is a container: its children are shifted instead (recursively), so no
    /// container ever gets a far offset with far-off local positions under it (PLAN4: precision). Transforms outside the
    /// WoW Game object can be added with <see cref="Register"/>. World-space particles and trails are NOT moved by a
    /// transform shift: their owners call <see cref="ShiftEffects"/> in Rebased (spell VFX and M2 effects do).
    /// A transform shifted twice (by this and by a listener) is detected after the listeners ran, put back and logged.
    /// </summary>
    public sealed class WowOrigin : MonoBehaviour
    {
        /// <summary>Horizontal distance (m) from the origin that triggers a rebase.</summary>
        public const float HorizontalLimit = 1000f;

        /// <summary>Vertical distance (m) from the origin that triggers a rebase.</summary>
        public const float VerticalLimit = 500f;
        /// <summary>
        /// Lead fix (2026-10-03, the user: friends' Somnium avatars showed as the client's translucent culling stand-in and "they also
        /// can't hear my mic"): with a per-player origin, two players side by side in WoW stand hundreds of metres apart in Unity, and
        /// the Somnium client's own avatar culling (AvatarsCullingSystem: the full avatar only within its near distance, 15 m by default,
        /// by UNshifted holder positions - CS AvatarSystem/Scripts/CullingSystem/AvatarsCullingSystem.cs:190-200) and its positional voice
        /// use those raw positions. So nearby Dreamport players SHARE one origin: every player adopts the origin cell of the Somnium
        /// player with the lowest Fusion player id within <see cref="ShareRange"/> m (WowGear.LeaderOrigin; the lowest keeps its own),
        /// and does not re-centre on its own while within <see cref="FollowLimit"/> m of that shared origin. Values ours.
        /// </summary>
        public const float ShareRange = 400f;
        public const float FollowLimit = 2000f;

        /// <summary>Seconds a rebase teleport may take to land before it is given up (the world then stays where it was).</summary>
        public const float LandTimeout = 3f;

        public static WowOrigin Instance { get; private set; }

        /// <summary>
        /// Set by a flight/seat while it writes the avatar's position directly every frame: WowOrigin then does not
        /// teleport; it commits the rebase at once and the driver must move the avatar (and its path) by -delta in its
        /// WowSpace.Rebased handler.
        /// </summary>
        public static bool AvatarDriven { get; set; }

        /// <summary>A rebase teleport is on its way (the world has not moved yet). WowGame's fall guard waits meanwhile.</summary>
        public static bool Pending { get { return _pending; } }

        /// <summary>Rebases done since start (for logs and labs).</summary>
        public static int Rebases { get; private set; }

        /// <summary>The frame (Time.frameCount) of the last rebase, -1 before any.</summary>
        public static int LastRebaseFrame { get; private set; } = -1;

        /// <summary>
        /// Optional gate for code that runs without WowGame (render lab): when set, it replaces WowGame's
        /// "settled in the world" check.
        /// </summary>
        public static Func<bool> MayRebaseOverride;

        private static bool _pending;
        private static readonly HashSet<Transform> _registered = new HashSet<Transform>();
        private static readonly HashSet<Transform> _excluded = new HashSet<Transform>();

        private Vector3 _pendFeet, _pendDelta;
        private int _pcx, _pcy, _pcz, _pendVersion;
        private float _pendingSince, _retryAt;
        private string _pendHow;
        private readonly List<Transform> _shifted = new List<Transform>(1024);
        private readonly List<Vector3> _shiftedTo = new List<Vector3>(1024);
        private int _landedLogs;

        /// <summary>Also shift t on every rebase (a WoW-world object that does not live under the WoW Game object).</summary>
        public static void Register(Transform t) { if (t != null) _registered.Add(t); }
        public static void Unregister(Transform t) { if (t != null) _registered.Remove(t); }

        /// <summary>Never shift t (and what is under it) - e.g. an object that follows the avatar or handles Rebased itself.</summary>
        public static void Exclude(Transform t) { if (t != null) _excluded.Add(t); }
        public static void Include(Transform t) { if (t != null) _excluded.Remove(t); }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnCameraRendering;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                _pending = false;
            }
        }

        /// <summary>Drops a rebase teleport that has not landed (WowGame calls it before its own teleports and on unload).</summary>
        public static void Cancel(string why)
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
            WowLog.Info("Origin: pending rebase cancelled (" + why + ").");
        }

        /// <summary>
        /// WowGame, right before an in-world teleport (graveyard, spirit healer...): when the WoW destination is beyond the
        /// limits, the origin moves to its cell at once (world shifted, Rebased fired, the avatar NOT teleported - the caller
        /// teleports it next, computing the spot in the new frame). Nothing happens for a near destination.
        /// </summary>
        public void RecenterFor(float wx, float wy, float wz, string why)
        {
            _pending = false;
            WowSpace.Frame f = WowSpace.Current;

            if (f.Map < 0)
            {
                return;
            }

            Vector3 u = f.ToUnity(wx, wy, wz);

            if (u.x * u.x + u.z * u.z < HorizontalLimit * HorizontalLimit && Mathf.Abs(u.y) < VerticalLimit)
            {
                return;
            }

            WowSpace.CellOf(wx, wy, wz, out _pcx, out _pcy, out _pcz);
            _pendVersion = f.Version;
            _pendHow = "recentred for a teleport (" + why + ")";
            _pending = true;
            Commit("teleport (" + why + ")", Vector3.zero);
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (_pending)
            {
                CheckLanded("update");

                if (_pending && now - _pendingSince > LandTimeout)
                {
                    _pending = false;
                    _retryAt = now + 5f;
                    Vector3 f;
                    WowPlayer.TryFeet(out f);
                    WowLog.Warn("Origin: the rebase teleport (" + _pendHow + ") did not land within " + LandTimeout + " s (avatar at " + f.ToString("F1") + ", expected "
                        + (_pendFeet - _pendDelta).ToString("F1") + ") - world left in place, retry in 5 s.");
                }

                return;
            }

            if (now < _retryAt)
            {
                return;
            }

            WowSpace.Frame frame = WowSpace.Current;

            if (frame.Map < 0 || !MayRebase())
            {
                return;
            }

            Vector3 feet;

            if (!WowPlayer.TryFeet(out feet))
            {
                return;
            }

            // lead fix 2026-10-03: one origin shared with the nearby Somnium players (see ShareRange)
            int lx, ly, lz, leader;
            if (WowGear.LeaderOrigin(ShareRange, out lx, out ly, out lz, out leader))
            {
                if (lx != frame.CellX || ly != frame.CellY || lz != frame.CellZ)
                {
                    Vector3 inNew = feet - WowSpace.DeltaTo(lx, ly, lz);
                    if (inNew.x * inNew.x + inNew.z * inNew.z < FollowLimit * FollowLimit && Mathf.Abs(inNew.y) < 2f * VerticalLimit)
                    {
                        if (_shareLogs++ < 20) WowLog.Info("Origin: sharing the origin of Somnium player " + leader + " (cell " + lx + ", " + ly + ", " + lz + ") - our avatars, culling and voice then agree on distances.");
                        BeginCell(frame, feet, now, lx, ly, lz);
                        return;
                    }
                }
                else if (feet.x * feet.x + feet.z * feet.z < FollowLimit * FollowLimit && Mathf.Abs(feet.y) < 2f * VerticalLimit)
                {
                    return;   // sharing: no re-centring of our own while near the shared origin
                }
            }

            if (feet.x * feet.x + feet.z * feet.z < HorizontalLimit * HorizontalLimit && Mathf.Abs(feet.y) < VerticalLimit)
            {
                return;
            }

            Begin(frame, feet, now);
        }

        private int _shareLogs;

        private void LateUpdate()
        {
            if (_pending)
            {
                CheckLanded("late update");
            }

            AvatarCloth.Tick();

            if (_reportAt > 0f && Time.unscaledTime >= _reportAt)
            {
                _reportAt = 0f;
                AvatarCloth.Report("1.5 s after rebase " + Rebases);
            }
        }

        private float _reportAt;

        private void OnCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (_pending)
            {
                CheckLanded("before rendering");
            }
        }

        private static bool MayRebase()
        {
            if (MayRebaseOverride != null)
            {
                try { return MayRebaseOverride(); }
                catch (Exception) { return false; }
            }

            WowGame g = WowGame.Current;
            return g != null && g.OriginMayRebase;
        }

        private void Begin(WowSpace.Frame frame, Vector3 feet, float now)
        {
            float x, y, z;
            frame.ToWow(feet, out x, out y, out z);
            int cx, cy, cz;
            WowSpace.CellOf(x, y, z, out cx, out cy, out cz);
            BeginCell(frame, feet, now, cx, cy, cz);
        }

        /// <summary>The rebase onto a given origin cell (our own grid cell, or the shared one - lead fix 2026-10-03).</summary>
        private void BeginCell(WowSpace.Frame frame, Vector3 feet, float now, int cx, int cy, int cz)
        {
            if (cx == frame.CellX && cy == frame.CellY && cz == frame.CellZ)
            {
                _retryAt = now + 2f;
                return;
            }

            Vector3 delta = WowSpace.DeltaTo(cx, cy, cz);
            _pendFeet = feet;
            _pendDelta = delta;
            _pcx = cx; _pcy = cy; _pcz = cz;
            _pendVersion = frame.Version;
            _pendingSince = now;

            if (AvatarDriven)
            {
                _pendHow = "avatar driven (flight/seat moves itself)";
                _pending = true;
                Commit("driven", Vector3.zero);
                return;
            }

            float yaw;
            string source;

            if (!WowPlayer.TryFacingYaw(out yaw, out source))
            {
                Transform r = WowPlayer.Root;
                yaw = r != null ? r.eulerAngles.y : 0f;
            }

            _pending = true;
            _pendHow = "teleport";
            string how;
            bool ok = WowPlayer.Teleport(feet - delta, yaw, OnTeleportDone, out how);

            if (!_pending)
            {
                return; // landed (and committed) inside the call: an editor fallback
            }

            _pendHow = how;

            if (!ok)
            {
                _pending = false;
                _retryAt = now + 5f;
                WowLog.Warn("Origin: rebase teleport failed (" + how + ") - retry in 5 s.");
                return;
            }

            CheckLanded("request");
        }

        private void OnTeleportDone()
        {
            if (_pending)
            {
                CheckLanded("teleport done callback");
            }
        }

        private void CheckLanded(string where)
        {
            if (!_pending)
            {
                return;
            }

            if (WowSpace.OriginVersion != _pendVersion)
            {
                _pending = false; // a load set a new origin meanwhile
                return;
            }

            Vector3 feet;

            if (!WowPlayer.TryFeet(out feet))
            {
                return;
            }

            Vector3 expected = _pendFeet - _pendDelta;

            if ((feet - expected).sqrMagnitude >= (feet - _pendFeet).sqrMagnitude)
            {
                return;
            }

            Commit(where, feet - expected);
        }

        private void Commit(string where, Vector3 residual)
        {
            _pending = false;
            var sw = Stopwatch.StartNew();
            WowSpace.Frame old = WowSpace.Current;
            Vector3 delta = WowSpace.MoveOrigin(_pcx, _pcy, _pcz);
            int moved = 0, doubled = 0, failed = 0;

            try
            {
                moved = ShiftWorld(delta);
            }
            catch (Exception e)
            {
                WowLog.Error("Origin: shifting the world threw: " + e);
            }

            try { Physics.SyncTransforms(); }
            catch (Exception e) { WowLog.Warn("Origin: Physics.SyncTransforms threw: " + e.Message); }

            failed = WowSpace.RaiseRebased(delta);

            for (int i = 0; i < _shifted.Count; i++)
            {
                Transform t = _shifted[i];

                if (t == null)
                {
                    continue;
                }

                Vector3 p = t.position;

                if ((p - (_shiftedTo[i] - delta)).sqrMagnitude < 0.01f && delta.sqrMagnitude > 1f)
                {
                    t.position = p + delta;
                    if (doubled++ < 5) WowLog.Warn("Origin: '" + Path(t) + "' was shifted twice (by WowOrigin and by a Rebased listener) - put back. Owners: do not move transforms under the WoW Game object in Rebased.");
                }
            }

            _shifted.Clear();
            _shiftedTo.Clear();
            Rebases++;
            LastRebaseFrame = Time.frameCount;
            WowSpace.Frame f = WowSpace.Current;
            WowLog.Info("Origin: rebased map " + f.Map + " to cell (" + f.CellX + "," + f.CellY + ") delta " + delta.ToString("F0") + " m in " + sw.ElapsedMilliseconds + " ms ("
                + moved + " objects shifted, z cell " + f.CellZ + ", was (" + old.CellX + "," + old.CellY + "," + old.CellZ + "), landed at " + where
                + (residual != Vector3.zero ? ", avatar residual " + residual.magnitude.ToString("0.00") + " m" : "")
                + (failed > 0 ? ", " + failed + " Rebased listeners threw" : "") + (doubled > 0 ? ", " + doubled + " double shifts undone" : "") + ").");

            if (Rebases <= 40) _reportAt = Time.unscaledTime + 1.5f;   // 13.7: the user's avatar vanished after long rides - say what it looks like

            if (residual.magnitude > 2f && _landedLogs++ < 5)
            {
                WowLog.Warn("Origin: the avatar landed " + residual.magnitude.ToString("0.0") + " m from where it should (" + residual.ToString("F2") + ") - the client may place the head, not the root, at the teleport point.");
            }
        }

        /// <summary>Shifts every WoW-world object by -delta (see the class notes). Returns how many transforms moved.</summary>
        private int ShiftWorld(Vector3 delta)
        {
            _shifted.Clear();
            _shiftedTo.Clear();
            Transform root = transform;
            int n = 0;

            for (int i = 0; i < root.childCount; i++)
            {
                n += ShiftNode(root.GetChild(i), delta, 0);
            }

            foreach (Transform t in _registered)
            {
                if (t != null && !t.IsChildOf(root))
                {
                    n += Shift(t, delta);
                }
            }

            return n;
        }

        private int ShiftNode(Transform t, Vector3 delta, int depth)
        {
            if (t == null || _excluded.Contains(t) || t.GetComponent<WowRoom>() != null)
            {
                return 0;
            }

            // Lead, 2026-09-27 - never step inside a UI object: a hidden UI surface root sits at (0, 0, 0) and looked like a
            // container, so its canvas (a RectTransform child) was moved ~1 km away while the root kept being snapped to the
            // palm - the palm menu (and any window / radial menu not yet opened at the rebase) became invisible. A node with a
            // Canvas or a RectTransform child is shifted as one piece; its owner re-places it anyway.
            if (depth < 8 && t.childCount > 0 && t.localPosition.sqrMagnitude < 1e-10f && t.GetComponent<Renderer>() == null && t.GetComponent<Collider>() == null
                && !HoldsUi(t))
            {
                int n = 0;

                for (int i = 0; i < t.childCount; i++)
                {
                    n += ShiftNode(t.GetChild(i), delta, depth + 1);
                }

                return n;
            }

            return Shift(t, delta);
        }

        /// <summary>A UI piece: a Canvas itself, a RectTransform, or a transform whose direct children include a RectTransform.</summary>
        private static bool HoldsUi(Transform t)
        {
            if (t is RectTransform || t.GetComponent<Canvas>() != null) return true;
            for (int i = 0; i < t.childCount; i++)
            {
                if (t.GetChild(i) is RectTransform) return true;
            }
            return false;
        }

        private int Shift(Transform t, Vector3 delta)
        {
            Vector3 p = t.position - delta;
            t.position = p;
            _shifted.Add(t);
            _shiftedTo.Add(p);
            return 1;
        }

        private static string Path(Transform t)
        {
            string s = t.name;

            for (Transform p = t.parent; p != null && s.Length < 160; p = p.parent)
            {
                s = p.name + "/" + s;
            }

            return s;
        }

        // ------------------------------------------------------------------ helpers for Rebased listeners

        /// <summary>
        /// For Rebased listeners: moves the live particles of every world-simulated ParticleSystem and the points of every
        /// TrailRenderer (and world-space LineRenderer) under root by -delta, so effects stay where they were in the world
        /// after their transforms were shifted. Local-space particles need nothing.
        /// </summary>
        public static void ShiftEffects(GameObject root, Vector3 delta)
        {
            if (root == null)
            {
                return;
            }

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ShiftParticles(ps, delta);
            }

            foreach (TrailRenderer tr in root.GetComponentsInChildren<TrailRenderer>(true))
            {
                ShiftTrail(tr, delta);
            }

            foreach (LineRenderer lr in root.GetComponentsInChildren<LineRenderer>(true))
            {
                if (!lr.useWorldSpace || lr.positionCount == 0)
                {
                    continue;
                }

                var pts = new Vector3[lr.positionCount];
                lr.GetPositions(pts);
                for (int i = 0; i < pts.Length; i++) pts[i] -= delta;
                lr.SetPositions(pts);
            }
        }

        private static ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[256];

        /// <summary>Moves a world-simulated ParticleSystem's live particles by -delta (no-op for local simulation).</summary>
        public static void ShiftParticles(ParticleSystem ps, Vector3 delta)
        {
            if (ps == null || ps.main.simulationSpace != ParticleSystemSimulationSpace.World)
            {
                return;
            }

            int count = ps.particleCount;

            if (count == 0)
            {
                return;
            }

            if (_particles.Length < count)
            {
                _particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            }

            int n = ps.GetParticles(_particles, count);

            for (int i = 0; i < n; i++)
            {
                _particles[i].position -= delta;
            }

            ps.SetParticles(_particles, n);
        }

        /// <summary>Moves a TrailRenderer's points by -delta.</summary>
        public static void ShiftTrail(TrailRenderer tr, Vector3 delta)
        {
            if (tr == null || tr.positionCount == 0)
            {
                return;
            }

            var pts = new Vector3[tr.positionCount];
            int n = tr.GetPositions(pts);
            for (int i = 0; i < n; i++) pts[i] -= delta;
            tr.SetPositions(pts);
        }
    }

    /// <summary>
    /// 13.7 (the user: "after traveling far on a mount my avatar disappears" / "things with magica cloth on disappear too, when the avatar is
    /// there"). Somnium avatars carry Magica Cloth 2; the client forces each cloth's camera culling to Reset (MagicaClothHandler.ClampClothSettings,
    /// CS AvatarSystem/Scripts/Handlers/Simulator/MagicaClothHandler.cs:36-49) and Magica's own teleport check is off by default
    /// (InertiaConstraint.SerializeData teleportMode = TeleportMode.None, MagicaCloth2/Scripts/Core/Cloth/Constraints/InertiaConstraint.cs:192).
    /// A rebase moves the avatar about 1 km in one frame (the mount / flight writes the root, or the rebase teleport), and a remote avatar
    /// jumps the same way when its owner rebases; the cloth is simulated on from where it was. So every Magica cloth whose transform moved
    /// more than <see cref="JumpMetres"/> in a frame gets MagicaCloth.ResetCloth(false) (MagicaClothAPI.cs:236: reset to the current pose,
    /// camera culling state cleared). By reflection: the upload server compiles worlds without MagicaCloth2 (ArmourCape.cs notes).
    /// </summary>
    internal static class AvatarCloth
    {
        public const float JumpMetres = 8f;   // ours: a taxi at 32 yd/s moves ~0.5 m a frame

        private static Type _type;
        private static MethodInfo _reset;
        private static bool _looked;
        private static float _nextScan;
        private static int _resets, _resetLogs;
        private static readonly List<Component> _cloths = new List<Component>();
        private static readonly List<Vector3> _last = new List<Vector3>();
        private static readonly Dictionary<Component, Vector3> _keep = new Dictionary<Component, Vector3>();
        private static readonly object[] _resetArgs = { false };

        private static bool Find()
        {
            if (_looked) return _type != null && _reset != null;
            _looked = true;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("MagicaCloth2.MagicaCloth", false);
                    if (t == null) continue;
                    _type = t;
                    _reset = t.GetMethod("ResetCloth", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                    break;
                }
            }
            catch (Exception e) { WowLog.Warn("Origin: looking for Magica Cloth threw: " + e.Message); }
            WowLog.Info("Origin: Magica Cloth " + (_type == null ? "not loaded (no avatar cloth to reset)" : _reset == null ? "found without ResetCloth(bool) - cloth is not reset" : "found - avatar cloth is reset after jumps over " + JumpMetres + " m") + ".");
            return _type != null && _reset != null;
        }

        private static void Rescan()
        {
            _keep.Clear();
            for (int i = 0; i < _cloths.Count; i++) if (_cloths[i] != null) _keep[_cloths[i]] = _last[i];
            _cloths.Clear();
            _last.Clear();
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsByType(_type, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c == null) continue;
                Vector3 p;
                if (!_keep.TryGetValue(c, out p)) p = c.transform.position;
                _cloths.Add(c);
                _last.Add(p);
            }
            _keep.Clear();
        }

        /// <summary>WowOrigin.LateUpdate: resets the cloth that jumped since last frame.</summary>
        public static void Tick()
        {
            try
            {
                if (!Find()) return;
                float now = Time.unscaledTime;
                if (now >= _nextScan) { _nextScan = now + 2f; Rescan(); }
                for (int i = 0; i < _cloths.Count; i++)
                {
                    Component c = _cloths[i];
                    if (c == null) continue;
                    Vector3 p = c.transform.position;
                    float jump = (p - _last[i]).magnitude;
                    _last[i] = p;
                    if (jump < JumpMetres) continue;
                    var b = c as Behaviour;
                    if (b != null && !b.isActiveAndEnabled) continue;
                    _reset.Invoke(c, _resetArgs);
                    _resets++;
                    if (_resetLogs++ < 30) WowLog.Info("Origin: Magica cloth '" + c.name + "' (" + (c.transform.root != null ? c.transform.root.name : "-") + ") moved " + jump.ToString("0") + " m in one frame - reset (" + _resets + " resets so far).");
                }
            }
            catch (Exception e)
            {
                if (_resetLogs++ < 30) WowLog.Warn("Origin: Magica cloth reset threw: " + (e.InnerException != null ? e.InnerException.Message : e.Message));
            }
        }

        /// <summary>A log line on the local avatar's renderers (the user saw it vanish after long rides; the cause is not proven yet).</summary>
        public static void Report(string when)
        {
            try
            {
                GearBody body = WowGear.LocalBody;
                Transform root = body != null ? body.AvatarRoot : null;
                Vector3 feet;
                bool haveFeet = WowPlayer.TryFeet(out feet);
                if (root == null) { WowLog.Info("Origin: avatar report (" + when + "): no avatar model matched" + (body != null && body.OnStandIn ? " (on the client's stand-in)" : "") + "."); return; }
                Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
                int on = 0, forcedOff = 0, visible = 0, far = 0;
                float farthest = 0f;
                for (int i = 0; i < rs.Length; i++)
                {
                    Renderer r = rs[i];
                    if (r == null) continue;
                    if (r.enabled && r.gameObject.activeInHierarchy) on++;
                    if (r.forceRenderingOff) forcedOff++;
                    if (r.isVisible) visible++;
                    if (haveFeet)
                    {
                        float d = (r.bounds.center - feet).magnitude;
                        if (d > 10f) far++;
                        if (d > farthest) farthest = d;
                    }
                }
                WowLog.Info("Origin: avatar report (" + when + "): model '" + root.name + "' at " + (haveFeet ? (root.position - feet).magnitude.ToString("0.0") + " m from the feet" : "?")
                    + ", " + rs.Length + " renderers (" + on + " on, " + forcedOff + " forced off, " + visible + " seen by a camera, " + far + " with bounds > 10 m away, farthest "
                    + farthest.ToString("0") + " m), stand-in " + (body.OnStandIn ? "yes" : "no") + ", " + _cloths.Count + " Magica cloths in the scene, " + _resets + " cloth resets.");
            }
            catch (Exception e) { WowLog.Warn("Origin: avatar report threw: " + e.Message); }
        }
    }
}
