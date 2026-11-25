using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Lead 2026-10-26 (user: "implement swimming logic just like in wow"): a WMO group's liquid (MLIQ - Stormwind's canals, inside
    /// buildings) as a surface the swimmer can ask "how high is the water here". Terrain liquid comes from the ground data
    /// (MountPlace.TryLiquid); WMO liquid was never in it (PLAN7 s7 deferred it). The triangles are the group's own liquid mesh (the
    /// render mesh is not readable after UploadMeshData), in the liquid object's local space, so WowOrigin's shifts need nothing.
    /// </summary>
    public sealed class WowLiquidSurface : MonoBehaviour
    {
        private static readonly List<WowLiquidSurface> Live = new List<WowLiquidSurface>();
        private Vector3[] _pos;
        private int[] _idx;
        private Bounds _bounds;

        public void Init(Vector3[] pos, int[] idx)
        {
            _pos = pos;
            _idx = idx;
            if (pos == null || pos.Length == 0) return;
            _bounds = new Bounds(pos[0], Vector3.zero);
            for (int i = 1; i < pos.Length; i++) _bounds.Encapsulate(pos[i]);
        }

        private void OnEnable() { Live.Add(this); }
        private void OnDisable() { Live.Remove(this); }

        /// <summary>The highest WMO liquid surface (Unity y) straight above or below a point, within maxBelow metres under it; false = none.</summary>
        public static bool TrySurface(Vector3 world, float maxBelow, out float surfaceY)
        {
            surfaceY = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < Live.Count; i++)
            {
                WowLiquidSurface s = Live[i];
                if (s == null || s._pos == null || s._idx == null) continue;
                float y;
                if (!s.TryLocal(world, out y)) continue;
                if (y < world.y - maxBelow) continue;
                if (y > surfaceY) { surfaceY = y; found = true; }
            }
            return found;
        }

        private bool TryLocal(Vector3 world, out float surfaceY)
        {
            surfaceY = 0f;
            Transform t = transform;
            Vector3 p = t.InverseTransformPoint(world);
            if (p.x < _bounds.min.x - 0.01f || p.x > _bounds.max.x + 0.01f || p.z < _bounds.min.z - 0.01f || p.z > _bounds.max.z + 0.01f) return false;
            for (int i = 0; i + 2 < _idx.Length; i += 3)
            {
                Vector3 a = _pos[_idx[i]], b = _pos[_idx[i + 1]], c = _pos[_idx[i + 2]];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-8f) continue;
                float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
                float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
                float w = 1f - u - v;
                if (u < -1e-4f || v < -1e-4f || w < -1e-4f) continue;
                Vector3 local = new Vector3(p.x, u * a.y + v * b.y + w * c.y, p.z);
                surfaceY = t.TransformPoint(local).y;
                return true;
            }
            return false;
        }
    }
}
