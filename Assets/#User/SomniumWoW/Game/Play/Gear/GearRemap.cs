using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>Why a remote player is (not) drawn.</summary>
    public enum GearSeen { Shown = 0, Unmapped = 1, OtherMap = 2, Room = 3, Far = 4, NoBody = 5, OtherInstance = 6 }   // iteration 11 (world): OtherInstance = another dungeon / raid instance (WowInstanceGate)

    /// <summary>
    /// Floating-origin re-mapping of remote avatars (PLAN4 §2). Somnium syncs every avatar in its owner's OWN Unity frame; a
    /// remote avatar at p in their frame is at p + (their origin - ours) in ours (<see cref="WowSpace.RemoteOffset"/>).
    /// The client's objects are only moved for rendering: <see cref="Request"/> (WowGear.LateUpdate, order 10000, after the
    /// client's VRIK 9998 / FBBIK 9999) queues a root, <see cref="ApplyAll"/> (GearShift.LateUpdate, order 30000 - after every
    /// client script: the highest client order found is 20600) adds the offset, and <see cref="RestoreAll"/> (end of frame,
    /// after every camera rendered; GearShiftEarly.Update at order -30000 as the fallback) takes it off again - so Fusion's
    /// Render (right after MonoBehaviour.Update, Fusion 2.0.12), the client's drivers (Update) and IK (LateUpdate) and the
    /// physics step never see it. A root is put back only when nobody moved it since (then the other writer wins).
    /// </summary>
    public static class GearRemap
    {
        /// <summary>Hidden beyond this (metres, after re-mapping); shown again under <see cref="ShowWithin"/>.</summary>
        public const float HideBeyond = 1500f, ShowWithin = 1450f;

        private struct Shift { public Transform T; public Vector3 Delta, Original, Shifted; }

        private static readonly List<Shift> _wanted = new List<Shift>();
        private static readonly List<Shift> _applied = new List<Shift>();
        private static readonly HashSet<Transform> _seen = new HashSet<Transform>();
        private static int _moved, _foreign;

        /// <summary>Roots currently offset (for the log).</summary>
        public static int AppliedCount { get { return _applied.Count; } }

        /// <summary>
        /// Where a remote player's avatar is drawn: false = hidden (why says which rule). offset = their frame -> ours.
        /// localMap is WowSpace.OriginMap (-1 = no origin yet); localInWorld = a game runs here.
        /// </summary>
        public static bool Decide(bool localInWorld, int localMap, in WowGearState g, out Vector3 offset, out GearSeen why)
        {
            offset = Vector3.zero;
            bool remoteInRoom = g.Status != WowNetStatus.Playing || (g.OriginSaid && g.OriginMap < 0);
            if (!localInWorld) { why = remoteInRoom ? GearSeen.Shown : GearSeen.Room; return remoteInRoom; }
            if (remoteInRoom) { why = GearSeen.Room; return false; }
            if (!g.OriginSaid || localMap < 0) { why = GearSeen.Unmapped; return true; }   // an older build / no origin here yet: as before
            if (!WowSpace.RemoteOffset(g.OriginMap, g.CellX, g.CellY, g.CellZ, out offset)) { why = GearSeen.OtherMap; return false; }
            why = GearSeen.Shown;
            return true;
        }

        /// <summary>Queues a root to be offset for this frame's rendering (ignored for zero offsets and repeats).</summary>
        public static void Request(Transform t, Vector3 delta)
        {
            if (t == null || delta.sqrMagnitude < 1e-8f) return;
            _wanted.Add(new Shift { T = t, Delta = delta });
        }

        /// <summary>GearShift, order 30000: every queued root gets its offset (a root queued twice is moved once).</summary>
        public static void ApplyAll()
        {
            if (_applied.Count > 0) RestoreAll();   // last frame's end-of-frame restore did not run
            _seen.Clear();
            for (int i = 0; i < _wanted.Count; i++)
            {
                Shift s = _wanted[i];
                if (s.T == null || !_seen.Add(s.T)) continue;
                s.Original = s.T.position;
                s.Shifted = s.Original + s.Delta;
                s.T.position = s.Shifted;
                s.Shifted = s.T.position;
                _applied.Add(s);
                _moved++;
            }
            _wanted.Clear();
        }

        /// <summary>End of frame (and GearShiftEarly as the fallback): every offset root back where the client left it.</summary>
        public static void RestoreAll()
        {
            for (int i = 0; i < _applied.Count; i++)
            {
                Shift s = _applied[i];
                if (s.T == null) continue;
                if ((s.T.position - s.Shifted).sqrMagnitude < 1e-6f) s.T.position = s.Original;
                else _foreign++;   // someone else wrote it after us: theirs stands
            }
            _applied.Clear();
        }

        /// <summary>Drops this frame's queue (WowGear disabled).</summary>
        public static void Forget() { _wanted.Clear(); }

        public static string StatsLine()
        {
            return "Gear: remap - " + _moved + " root shifts so far, " + _foreign + " roots moved by someone else before the restore";
        }
    }

    /// <summary>
    /// Hides a remote player's avatar (Renderer.forceRenderingOff - the client never uses that flag on avatars, it toggles
    /// enabled / GameObjects for its LODs and culling dummy, so both stay independent) and shows it again, touching only the
    /// renderers it hid. Re-scans every second while hidden (a newly loaded avatar model).
    /// </summary>
    public sealed class GearHider
    {
        private readonly List<Renderer> _off = new List<Renderer>();
        private readonly List<Renderer> _scan = new List<Renderer>();
        private float _nextScan;
        public bool Hidden { get; private set; }

        public void Hide(Transform a, Transform b)
        {
            if (Hidden && Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            Hidden = true;
            Collect(a);
            if (b != a) Collect(b);
        }

        private void Collect(Transform t)
        {
            if (t == null) return;
            _scan.Clear();
            t.GetComponentsInChildren(true, _scan);
            for (int i = 0; i < _scan.Count; i++)
            {
                Renderer r = _scan[i];
                if (r == null || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                _off.Add(r);
            }
            _scan.Clear();
        }

        public void Show()
        {
            if (!Hidden) return;
            for (int i = 0; i < _off.Count; i++) if (_off[i] != null) _off[i].forceRenderingOff = false;
            _off.Clear();
            Hidden = false;
        }
    }
}
