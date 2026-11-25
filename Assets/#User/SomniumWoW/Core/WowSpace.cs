using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW
{
    /// <summary>
    /// The one place WoW space meets Unity space. Every module converts through here.
    /// WoW: yards, X = north, Y = west, Z = up, right-handed; orientation in radians, 0 = +X (north),
    /// counter-clockwise seen from above. Unity: metres, x = east, y = up, z = north, left-handed; yaw in
    /// degrees, 0 = +z, clockwise seen from above. Mapping (x, y, z) -> (-y, z, x) has determinant -1, so it
    /// also flips handedness: any mesh converted through it must have its triangle winding checked (measure it).
    ///
    /// Iteration 4 - FLOATING ORIGIN (PLAN4 section 2). Unity (0, 0, 0) is no longer WoW (0, 0, 0): it is the current
    /// origin, a point of a 256-yard grid near the local player (<see cref="Current"/>). Far from Unity's origin floats
    /// get coarse (2 mm steps at 15 km = the wobble the user saw), so WowOrigin moves the origin to the player's grid cell
    /// every ~1 km and fires <see cref="Rebased"/>: whoever keeps Unity positions subtracts the delta from them. Unity
    /// positions are therefore only valid until the next Rebased; WoW positions are forever. There is no per-map height
    /// slot any more (MapOffsetY is 0): each player has their own frame, and other players are re-mapped by the
    /// difference of the origins (<see cref="RemoteOffset"/>).
    /// </summary>
    public static class WowSpace
    {
        /// <summary>WoW's unit is the yard. A WoW human stands ~1.8 m next to a Somnium avatar at this scale.</summary>
        public const float MetresPerYard = 0.9144f;

        /// <summary>The origin is always a point of this WoW grid (yards).</summary>
        public const float OriginGridYards = 256f;

        /// <summary>
        /// Where the black login room stands: 3 km above the origin, far above any WoW world near it (the world is rebased
        /// before the player is 500 m above or below the origin), and at fine float precision. The scene's spawn point
        /// (WorldConfig SpawnPointPos) must be here too: (0, 3000.9, 0).
        /// </summary>
        public static readonly Vector3 LoginRoomOrigin = new Vector3(0f, 3000f, 0f);

        /// <summary>
        /// One origin: a map and a grid cell. Immutable, so a worker thread can take <see cref="Current"/> once and build
        /// a whole tile in it (and the main thread later places the result by <see cref="Offset"/>).
        /// </summary>
        public sealed class Frame
        {
            /// <summary>Map.dbc id (-1 before the first SetOrigin).</summary>
            public readonly int Map;
            /// <summary>The origin's grid cell: WoW point = cell * 256 yards.</summary>
            public readonly int CellX, CellY, CellZ;
            /// <summary>Counts every origin change (SetOrigin and rebases).</summary>
            public readonly int Version;
            /// <summary>
            /// Where Unity's (0, 0, 0) is in "absolute" Unity metres (the pre-iteration-4 mapping, no height slot): the
            /// grid point converted and rounded to whole metres, so origins differ by exact integers and a mesh built in
            /// one frame lines up bit-exactly with one built in another (terrain seams stay closed across rebases).
            /// </summary>
            public readonly Vector3 Unity;

            internal Frame(int map, int cx, int cy, int cz, int version)
            {
                Map = map; CellX = cx; CellY = cy; CellZ = cz; Version = version;
                Unity = CellToAbsolute(cx, cy, cz);
            }

            /// <summary>The origin's WoW point (yards; x north, y west, z up).</summary>
            public Vector3 Wow { get { return new Vector3(CellX * OriginGridYards, CellY * OriginGridYards, CellZ * OriginGridYards); } }

            /// <summary>WoW position (yards) -> Unity position (metres) in this frame.</summary>
            public Vector3 ToUnity(float x, float y, float z)
            {
                // Two explicit roundings (absolute, then minus the integer origin): the same float sequence everywhere, so
                // shared terrain vertices stay bit-identical whichever frame built them.
                float ax = (float)(-y * MetresPerYard), ay = (float)(z * MetresPerYard), az = (float)(x * MetresPerYard);
                return new Vector3((float)(ax - Unity.x), (float)(ay - Unity.y), (float)(az - Unity.z));
            }

            /// <summary>Unity position (metres) in this frame -> WoW position (yards).</summary>
            public void ToWow(Vector3 unity, out float x, out float y, out float z)
            {
                const double K = (double)MetresPerYard;
                x = (float)(((double)unity.z + Unity.z) / K);
                y = (float)(-((double)unity.x + Unity.x) / K);
                z = (float)(((double)unity.y + Unity.y) / K);
            }

            /// <summary>
            /// What to add to a Unity position built in this frame to get it in the current frame (exact: integer metres).
            /// Zero while this frame is current.
            /// </summary>
            public Vector3 Offset { get { return Unity - _current.Unity; } }

            public override string ToString()
            {
                return "map " + Map + " cell (" + CellX + ", " + CellY + ", " + CellZ + ")";
            }
        }

        private static volatile Frame _current = new Frame(-1, 0, 0, 0, 0);

        /// <summary>The current origin (take it once when a worker builds in it; it never changes under you).</summary>
        public static Frame Current { get { return _current; } }

        /// <summary>The current origin's map (-1 before the first SetOrigin). For the network record (Gear).</summary>
        public static int OriginMap { get { return _current.Map; } }

        /// <summary>The current origin's WoW point (yards; x north, y west, z up). For the network record (Gear).</summary>
        public static Vector3 OriginWow { get { return _current.Wow; } }

        /// <summary>The current origin's grid cell (x, y, z; WoW point = cell * 256 yd). For the network record (Gear).</summary>
        public static Vector3Int OriginCell { get { var f = _current; return new Vector3Int(f.CellX, f.CellY, f.CellZ); } }

        public static int OriginCellX { get { return _current.CellX; } }
        public static int OriginCellY { get { return _current.CellY; } }
        public static int OriginCellZ { get { return _current.CellZ; } }

        /// <summary>Counts origin changes (SetOrigin and rebases); a cached Unity position is stale when this moved.</summary>
        public static int OriginVersion { get { return _current.Version; } }

        /// <summary>
        /// The origin moved by deltaUnity (new origin minus old origin, Unity metres) while the player stayed where they
        /// were in WoW: SUBTRACT deltaUnity from every Unity position you keep (smoothing buffers, world-space particles,
        /// missiles, grass, critters, flight paths). Fired on the main thread by WowOrigin, after the world objects were
        /// shifted and in the same frame the avatar's teleport by -deltaUnity landed.
        /// </summary>
        public static event Action<Vector3> Rebased;

        /// <summary>Fired after every origin change (SetOrigin on a load, and after Rebased). For the network record.</summary>
        public static event Action OriginChanged;

        /// <summary>The grid cell nearest to a WoW point.</summary>
        public static void CellOf(float x, float y, float z, out int cx, out int cy, out int cz)
        {
            cx = (int)Math.Floor(x / OriginGridYards + 0.5);
            cy = (int)Math.Floor(y / OriginGridYards + 0.5);
            cz = (int)Math.Floor(z / OriginGridYards + 0.5);
        }

        /// <summary>
        /// The front end, before loading a map (nothing of WoW is loaded then): the origin becomes the grid point nearest
        /// to the WoW spot (x, y, z) on map. No Rebased (nothing to shift); OriginChanged fires.
        /// </summary>
        public static void SetOrigin(int map, float x, float y, float z)
        {
            int cx, cy, cz;
            CellOf(x, y, z, out cx, out cy, out cz);
            var old = _current;
            _current = new Frame(map, cx, cy, cz, old.Version + 1);
            WowLog.Info("Origin: set to map " + map + " cell (" + cx + ", " + cy + ", " + cz + ") = WoW (" + (cx * OriginGridYards) + ", " + (cy * OriginGridYards) + ", " + (cz * OriginGridYards)
                + "), absolute Unity " + _current.Unity.ToString("F0") + " (was " + old + ")");
            Raise(OriginChanged, "OriginChanged");
        }

        /// <summary>The Unity delta (new - old) a move of the origin to cell (cx, cy, cz) would make, without moving it.</summary>
        public static Vector3 DeltaTo(int cx, int cy, int cz)
        {
            return CellToAbsolute(cx, cy, cz) - _current.Unity;
        }

        /// <summary>WowOrigin only: moves the origin to another cell of the same map; returns the Unity delta (new - old).</summary>
        internal static Vector3 MoveOrigin(int cx, int cy, int cz)
        {
            var old = _current;
            var f = new Frame(old.Map, cx, cy, cz, old.Version + 1);
            _current = f;
            return f.Unity - old.Unity;
        }

        /// <summary>WowOrigin only: tells every listener (each in its own try, so one failure does not stop the rest).</summary>
        internal static int RaiseRebased(Vector3 deltaUnity)
        {
            int failed = 0;
            var h = Rebased;
            if (h != null)
            {
                foreach (Action<Vector3> d in h.GetInvocationList())
                {
                    try { d(deltaUnity); }
                    catch (Exception e) { failed++; WowLog.Error("Origin: a Rebased listener (" + d.Method.DeclaringType + "." + d.Method.Name + ") threw: " + e); }
                }
            }
            Raise(OriginChanged, "OriginChanged");
            return failed;
        }

        private static void Raise(Action h, string what)
        {
            if (h == null) return;
            foreach (Action d in h.GetInvocationList())
            {
                try { d(); }
                catch (Exception e) { WowLog.Error("Origin: a " + what + " listener (" + d.Method.DeclaringType + "." + d.Method.Name + ") threw: " + e); }
            }
        }

        /// <summary>
        /// For drawing another player: their network record says their origin is (map, cell); a Unity position p in THEIR
        /// frame is at p + offset in ours. False (offset zero) when they are on another map or we have no origin yet.
        /// </summary>
        public static bool RemoteOffset(int map, int cellX, int cellY, int cellZ, out Vector3 offset)
        {
            var f = _current;
            if (f.Map < 0 || map != f.Map)
            {
                offset = Vector3.zero;
                return false;
            }
            offset = CellToAbsolute(cellX, cellY, cellZ) - f.Unity;
            return true;
        }

        /// <summary>A grid cell -> its absolute Unity metres, rounded to whole metres (exact in a float up to 16 km).</summary>
        private static Vector3 CellToAbsolute(int cx, int cy, int cz)
        {
            const double K = (double)MetresPerYard;
            return new Vector3((float)Math.Round(-(cy * (double)OriginGridYards) * K), (float)Math.Round(cz * (double)OriginGridYards * K), (float)Math.Round(cx * (double)OriginGridYards * K));
        }

        /// <summary>Always 0 since iteration 4 (the per-map height slots gave way to the floating origin). Kept for old callers.</summary>
        public static float MapOffsetY(int mapId)
        {
            return 0f;
        }

        /// <summary>
        /// WoW world position (yards) -> Unity world position (metres) in the CURRENT frame. mapId is not used any more
        /// (everything loaded is on the origin's map). Worker threads: take <see cref="Current"/> once and use its ToUnity.
        /// </summary>
        public static Vector3 ToUnity(int mapId, float x, float y, float z)
        {
            return _current.ToUnity(x, y, z);
        }

        /// <summary>Unity world position (metres) in the CURRENT frame -> WoW world position (yards). mapId is not used.</summary>
        public static void ToWow(int mapId, Vector3 unity, out float x, out float y, out float z)
        {
            _current.ToWow(unity, out x, out y, out z);
        }

        /// <summary>A model-space or file-space point (yards) -> Unity (metres), no origin. For mesh vertices.</summary>
        public static Vector3 PointToUnity(float x, float y, float z)
        {
            return new Vector3(-y * MetresPerYard, z * MetresPerYard, x * MetresPerYard);
        }

        /// <summary>A direction or normal -> Unity, unscaled.</summary>
        public static Vector3 DirToUnity(float x, float y, float z)
        {
            return new Vector3(-y, z, x);
        }

        /// <summary>
        /// A WoW-space rotation quaternion -> Unity. The basis change is improper (det -1), so the axis maps as a
        /// pseudo-vector: q' = (qy, -qz, -qx, qw). Check: WoW yaw +t about +Z becomes Unity yaw -t about +y.
        /// </summary>
        public static Quaternion RotationToUnity(float qx, float qy, float qz, float qw)
        {
            return new Quaternion(qy, -qz, -qx, qw);
        }

        /// <summary>WoW orientation (radians) -> Unity yaw (degrees).</summary>
        public static float OrientationToYaw(float orientation)
        {
            return -orientation * Mathf.Rad2Deg;
        }

        /// <summary>Unity yaw (degrees) -> WoW orientation in [0, 2pi).</summary>
        public static float YawToOrientation(float yawDegrees)
        {
            float o = -yawDegrees * Mathf.Deg2Rad;
            const float TwoPi = Mathf.PI * 2f;
            o %= TwoPi;
            if (o < 0f) o += TwoPi;
            return o;
        }
    }
}
