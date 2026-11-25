using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Iteration 12 (portal culling). Which EXTERIOR groups of a WMO the 3.3.5a client draws while the camera stands in one of its
    /// interior groups. Pure (no UnityEngine); all frames are the WMO frame (yards, z up). Main thread only (shared scratch).
    ///
    /// Client rule (wowdev WMO, MOGP flags): "0x8 SMOGroup::EXTERIOR -- If camera is AABB present in a group with this flag, and not
    /// present in any group with SMOGroup::INTERIOR, render all exteriors"; "0x10000 SMOGroup::ALWAYSDRAW -- clear 0x8 after
    /// CMapObjGroup::Create()". WebWowViewerCpp (Deamon87, wmoObject.cpp) does it like this:
    ///  - startTraversingWMOGroup(..., traversingFromInterior = true): only the camera's group is put in a view; its portals are
    ///    walked (drainPortalTraversal). A portal is crossed when the camera is on the referencing group's side
    ///    ("isInsidePortalThis = (relation-&gt;side &lt; 0) ? (dotResult &lt;= 0) : (dotResult &gt;= 0)") and its polygon, clipped by the
    ///    current frustum (MathHelper::planeCull clips the points in place), keeps 3+ points; the next frustum is the planes from the
    ///    eye through the clipped polygon's edges plus the portal plane as near plane. Within dotepsilon = 1.5^2 yards of the portal
    ///    plane the current frustum is passed on unchanged. Work items deeper than 8 portals are dropped.
    ///  - "5.2 The portal is from interior into exterior wmo group": the frustum becomes one of the exterior view's frustums; the
    ///    walk does not continue from there. Map::checkExterior then runs every WMO (the camera's one included) with
    ///    traversingFromInterior = false against those frustums: every EXTERIOR group whose box is inside one of them is drawn.
    ///    No exterior view (no such portal seen) = no exterior group at all.
    ///  - ALWAYSDRAW groups are added to the exterior view in every case ("Add all ALWAYSRENDER to Exterior").
    /// So from an interior group, an exterior group is drawn only through a visible chain of portals that ends in an exterior group.
    ///
    /// Ours: the walk starts with NO camera frustum (all directions; Unity culls the view direction per renderer), so the result
    /// depends on the camera position only (VR: both eyes, no head-turn latency) and is a superset of the client's per-view set.
    /// The close-to-portal pass-through also needs the camera within 2.25 yd of the portal polygon's box (not just of its infinite
    /// plane). Interior groups are not culled by us (drawn as before).
    /// </summary>
    public sealed class WmoPortalCuller
    {
        public const uint G_EXTERIOR = 0x8, G_UNREACHABLE = 0x80, G_ALWAYSDRAW = 0x10000;
        /// <summary>WebWowViewerCpp drainPortalTraversal: "if (workItem.localLevel &gt; 8) continue;".</summary>
        public const int MaxDepth = 8;
        /// <summary>WebWowViewerCpp wmoObject.cpp "static const float dotepsilon = pow(1.5f, 2.0f);" (yards).</summary>
        public const float CloseYards = 2.25f;
        /// <summary>Ours: work items / plane slots / polygon vertices per walk. Running out = every exterior shown (safe) + Overflow.</summary>
        public const int MaxItems = 4096, MaxPlanes = 16384, MaxPoly = 64, MaxExteriorFrustums = 1024;

        public readonly string Path;
        public readonly int GroupCount;
        /// <summary>Exterior groups this rule can hide: MOGP 0x8 and not 0x10000 (built groups only).</summary>
        public readonly int[] GatedGroups;
        public readonly float RootMinX, RootMinY, RootMinZ, RootMaxX, RootMaxY, RootMaxZ;
        /// <summary>MOGP names (for the log).</summary>
        public readonly string[] GroupNames;

        readonly uint[] _flags;
        readonly float[] _box;                        // per group: minX minY minZ maxX maxY maxZ
        readonly int[] _refStart, _refCount;          // per group, into _ref*
        readonly int[] _refPortal, _refGroup, _refSide;
        readonly float[] _plane;                      // per portal: nx ny nz d
        readonly float[] _pbox;                       // per portal: polygon box
        readonly int[] _pvStart, _pvCount;            // per portal, into _pv (vertices sorted around the centroid)
        readonly float[] _pv;
        readonly int[] _refStamp, _refLastStart, _refLastCount;   // per ref: the frustum last passed through it in walk _stamp
        int _stamp;

        // walk results (read after Traverse)
        public int LastItems, LastExteriorFrustums;
        public bool LastOverflow;

        // shared scratch (main thread only)
        static readonly float[] _planes = new float[MaxPlanes * 4];
        static readonly int[] _wGroup = new int[MaxItems], _wStart = new int[MaxItems], _wCount = new int[MaxItems], _wDepth = new int[MaxItems];
        static readonly int[] _xStart = new int[MaxExteriorFrustums], _xCount = new int[MaxExteriorFrustums];
        static readonly float[] _polyA = new float[MaxPoly * 3], _polyB = new float[MaxPoly * 3];

        WmoPortalCuller(WmoRoot root, WmoGroup[] groups, List<int> gated)
        {
            Path = root.Path;
            GroupCount = groups.Length;
            GatedGroups = gated.ToArray();
            RootMinX = root.MinX; RootMinY = root.MinY; RootMinZ = root.MinZ; RootMaxX = root.MaxX; RootMaxY = root.MaxY; RootMaxZ = root.MaxZ;
            _flags = new uint[GroupCount];
            GroupNames = new string[GroupCount];
            _box = new float[GroupCount * 6];
            _refStart = new int[GroupCount];
            _refCount = new int[GroupCount];
            var rp = new List<int>(); var rg = new List<int>(); var rs = new List<int>();
            for (int g = 0; g < GroupCount; g++)
            {
                var gr = groups[g];
                if (gr == null) continue;
                _flags[g] = gr.Flags;
                GroupNames[g] = root.GroupName(gr.NameOffset);
                _box[g * 6] = gr.MinX; _box[g * 6 + 1] = gr.MinY; _box[g * 6 + 2] = gr.MinZ;
                _box[g * 6 + 3] = gr.MaxX; _box[g * 6 + 4] = gr.MaxY; _box[g * 6 + 5] = gr.MaxZ;
                _refStart[g] = rp.Count;
                for (int k = gr.PortalStart; k < gr.PortalStart + gr.PortalCount && k < root.PortalRefs.Length; k++)
                {
                    var r = root.PortalRefs[k];
                    if (r.Portal < 0 || r.Portal >= root.Portals.Length || r.Group < 0 || r.Group >= GroupCount || r.Group == g) continue;
                    rp.Add(r.Portal); rg.Add(r.Group); rs.Add(r.Side);
                }
                _refCount[g] = rp.Count - _refStart[g];
            }
            _refPortal = rp.ToArray(); _refGroup = rg.ToArray(); _refSide = rs.ToArray();
            _refStamp = new int[_refPortal.Length]; _refLastStart = new int[_refPortal.Length]; _refLastCount = new int[_refPortal.Length];

            int np = root.Portals.Length;
            _plane = new float[np * 4];
            _pbox = new float[np * 6];
            _pvStart = new int[np];
            _pvCount = new int[np];
            var pv = new List<float>();
            var tmp = new List<float[]>();
            int nv = root.PortalVertices.Length / 3;
            for (int p = 0; p < np; p++)
            {
                var po = root.Portals[p];
                _plane[p * 4] = po.NX; _plane[p * 4 + 1] = po.NY; _plane[p * 4 + 2] = po.NZ; _plane[p * 4 + 3] = po.D;
                tmp.Clear();
                for (int v = po.StartVertex; v < po.StartVertex + po.VertexCount && v < nv; v++)
                    tmp.Add(new[] { root.PortalVertices[v * 3], root.PortalVertices[v * 3 + 1], root.PortalVertices[v * 3 + 2] });
                SortAroundCentroid(tmp, po.NX, po.NY, po.NZ);   // as WebWowViewerCpp sortVec3ArrayAgainstPlane: MOPV may be strip-ordered
                _pvStart[p] = pv.Count / 3;
                _pvCount[p] = Math.Min(tmp.Count, MaxPoly / 2);
                float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
                for (int i = 0; i < _pvCount[p]; i++)
                {
                    var t = tmp[i];
                    pv.Add(t[0]); pv.Add(t[1]); pv.Add(t[2]);
                    x0 = Math.Min(x0, t[0]); y0 = Math.Min(y0, t[1]); z0 = Math.Min(z0, t[2]);
                    x1 = Math.Max(x1, t[0]); y1 = Math.Max(y1, t[1]); z1 = Math.Max(z1, t[2]);
                }
                _pbox[p * 6] = x0; _pbox[p * 6 + 1] = y0; _pbox[p * 6 + 2] = z0; _pbox[p * 6 + 3] = x1; _pbox[p * 6 + 4] = y1; _pbox[p * 6 + 5] = z1;
            }
            _pv = pv.ToArray();
        }

        /// <summary>
        /// The culler for a loaded WMO, or null when the rule can never hide anything: no portals, no exterior group that is not
        /// ALWAYSDRAW, or no group that is not exterior (= nowhere the camera could be "inside"). built[g] false = group g is not
        /// built (WmoGroupVisibility.Never). note: one line for the log.
        /// </summary>
        public static WmoPortalCuller Create(WmoRoot root, WmoGroup[] groups, bool[] built, out string note)
        {
            note = null;
            if (root == null || groups == null || root.Portals.Length == 0 || root.PortalRefs.Length == 0 || root.PortalVertices.Length == 0) return null;
            var gated = new List<int>();
            int interiors = 0, always = 0;
            for (int g = 0; g < groups.Length; g++)
            {
                var gr = groups[g];
                if (gr == null || (built != null && g < built.Length && !built[g])) continue;
                if ((gr.Flags & G_ALWAYSDRAW) != 0) { always++; continue; }
                if ((gr.Flags & G_EXTERIOR) != 0) gated.Add(g);
                else interiors++;
            }
            if (gated.Count == 0 || interiors == 0) return null;
            var c = new WmoPortalCuller(root, groups, gated);
            var names = new List<string>();
            foreach (int g in gated) names.Add(g + " '" + root.GroupName(groups[g].NameOffset) + "'");
            note = "portal culling: " + gated.Count + " exterior groups (" + string.Join(", ", names.ToArray()) + ") are drawn while the camera is in one of "
                + interiors + " interior groups only when a chain of " + root.Portals.Length + " portals leads the eye into an exterior group and the exterior group's box is inside that portal frustum"
                + (always > 0 ? "; " + always + " ALWAYSDRAW groups always" : "");
            return c;
        }

        public bool IsExterior(int g) { return g >= 0 && g < GroupCount && (_flags[g] & G_EXTERIOR) != 0; }
        public uint Flags(int g) { return g >= 0 && g < GroupCount ? _flags[g] : 0u; }

        /// <summary>Ours: the camera can stand in this group (UNREACHABLE and ALWAYSDRAW groups are backdrops nobody stands in,
        /// e.g. Stormwind 'mountains' MOGP 0x122C1).</summary>
        public bool CanHoldCamera(int g) { return g >= 0 && g < GroupCount && (_flags[g] & (G_UNREACHABLE | G_ALWAYSDRAW)) == 0; }

        public bool InRootBox(float x, float y, float z)
        {
            return x > RootMinX && x < RootMaxX && y > RootMinY && y < RootMaxY && z > RootMinZ && z < RootMaxZ;
        }

        public bool InGroupBox(int g, float x, float y, float z)
        {
            if (g < 0 || g >= GroupCount) return false;
            int b = g * 6;
            return x >= _box[b] && x <= _box[b + 3] && y >= _box[b + 1] && y <= _box[b + 4] && z >= _box[b + 2] && z <= _box[b + 5];
        }

        /// <summary>
        /// Camera at (ex, ey, ez) in interior group start: visible[g] = true for each gated exterior group g whose box is inside a
        /// portal frustum that reaches an exterior group (others false; ALWAYSDRAW groups are not touched). Returns false when the
        /// walk ran out of scratch (LastOverflow; the caller shows every exterior). visible.Length must be &gt;= GroupCount.
        /// </summary>
        public bool Traverse(int start, float ex, float ey, float ez, bool[] visible)
        {
            foreach (int g in GatedGroups) visible[g] = false;
            LastItems = 0; LastExteriorFrustums = 0; LastOverflow = false;
            if (start < 0 || start >= GroupCount) return true;

            _stamp++;
            int planeTop = 0, head = 0, tail = 0, xf = 0;
            _wGroup[tail] = start; _wStart[tail] = 0; _wCount[tail] = 0; _wDepth[tail] = 0; tail++;
            while (head < tail)
            {
                int cur = _wGroup[head], fs = _wStart[head], fc = _wCount[head], depth = _wDepth[head];
                head++;
                if (depth > MaxDepth) continue;
                for (int k = _refStart[cur]; k < _refStart[cur] + _refCount[cur]; k++)
                {
                    int p = _refPortal[k], next = _refGroup[k], side = _refSide[k];
                    float nx = _plane[p * 4], ny = _plane[p * 4 + 1], nz = _plane[p * 4 + 2], d = _plane[p * 4 + 3];
                    float dot = nx * ex + ny * ey + nz * ez + d;
                    bool close = Math.Abs(dot) <= CloseYards && BoxDistance(p, ex, ey, ez) <= CloseYards;
                    int ns, nc;
                    if (close)
                    {
                        ns = fs; nc = fc;   // WebWowViewerCpp: "If camera is too close - just use usual frustums"
                        // the same frustum through the same portal reference again adds nothing (WebWowViewerCpp: "Suppress
                        // duplicate contributions on cyclic portal paths"); stops the A -> B -> A ping-pong at a doorway
                        if (_refStamp[k] == _stamp && _refLastStart[k] == ns && _refLastCount[k] == nc) continue;
                        _refStamp[k] = _stamp; _refLastStart[k] = ns; _refLastCount[k] = nc;
                    }
                    else
                    {
                        bool inside = side < 0 ? dot <= 0f : dot >= 0f;
                        if (!inside) continue;
                        // clip the portal polygon by the current frustum (MathHelper::planeCull)
                        int n = _pvCount[p];
                        if (n < 3) continue;
                        Array.Copy(_pv, _pvStart[p] * 3, _polyA, 0, n * 3);
                        float[] a = _polyA, b = _polyB;
                        bool overflow = false;
                        for (int f = fs; f < fs + fc && n >= 3; f++)
                        {
                            n = ClipPolygon(a, n, b, _planes[f * 4], _planes[f * 4 + 1], _planes[f * 4 + 2], _planes[f * 4 + 3], ref overflow);
                            var t = a; a = b; b = t;
                        }
                        if (overflow) { LastOverflow = true; return false; }
                        if (n < 3) continue;
                        if (planeTop + n + 1 > MaxPlanes) { LastOverflow = true; return false; }
                        // frustum: eye through every clipped edge + the portal plane (near), each facing the polygon's inside
                        float cx = 0, cy = 0, cz = 0;
                        for (int i = 0; i < n; i++) { cx += a[i * 3]; cy += a[i * 3 + 1]; cz += a[i * 3 + 2]; }
                        cx /= n; cy /= n; cz /= n;
                        ns = planeTop;
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n;
                            float ax = a[i * 3] - ex, ay = a[i * 3 + 1] - ey, az = a[i * 3 + 2] - ez;
                            float bx = a[j * 3] - ex, by = a[j * 3 + 1] - ey, bz = a[j * 3 + 2] - ez;
                            float px = ay * bz - az * by, py = az * bx - ax * bz, pz = ax * by - ay * bx;
                            float len = (float)Math.Sqrt(px * px + py * py + pz * pz);
                            if (len < 1e-6f) continue;   // degenerate edge (duplicate point)
                            px /= len; py /= len; pz /= len;
                            float pd = -(px * ex + py * ey + pz * ez);
                            if (px * cx + py * cy + pz * cz + pd < 0f) { px = -px; py = -py; pz = -pz; pd = -pd; }
                            int o = planeTop * 4;
                            _planes[o] = px; _planes[o + 1] = py; _planes[o + 2] = pz; _planes[o + 3] = pd;
                            planeTop++;
                        }
                        {
                            float s = dot > 0f ? -1f : 1f;   // near plane: the eye's side negative, beyond the portal positive
                            int o = planeTop * 4;
                            _planes[o] = nx * s; _planes[o + 1] = ny * s; _planes[o + 2] = nz * s; _planes[o + 3] = d * s;
                            planeTop++;
                        }
                        nc = planeTop - ns;
                    }
                    if ((_flags[next] & G_EXTERIOR) != 0)
                    {
                        // 5.2: interior -> exterior: one more exterior frustum; the walk stops here
                        if (xf >= MaxExteriorFrustums) { LastOverflow = true; return false; }
                        _xStart[xf] = ns; _xCount[xf] = nc; xf++;
                    }
                    else
                    {
                        if (tail >= MaxItems) { LastOverflow = true; return false; }
                        _wGroup[tail] = next; _wStart[tail] = ns; _wCount[tail] = nc; _wDepth[tail] = depth + 1; tail++;
                    }
                }
            }
            LastItems = tail;
            LastExteriorFrustums = xf;
            foreach (int g in GatedGroups)
                for (int f = 0; f < xf && !visible[g]; f++)
                    visible[g] = BoxInFrustum(g, _xStart[f], _xCount[f]);
            return true;
        }

        /// <summary>Box outside the frustum when all 8 corners are behind one plane (the usual conservative test).</summary>
        bool BoxInFrustum(int g, int fs, int fc)
        {
            int b = g * 6;
            for (int f = fs; f < fs + fc; f++)
            {
                float nx = _planes[f * 4], ny = _planes[f * 4 + 1], nz = _planes[f * 4 + 2], d = _planes[f * 4 + 3];
                float x = nx >= 0f ? _box[b + 3] : _box[b], y = ny >= 0f ? _box[b + 4] : _box[b + 1], z = nz >= 0f ? _box[b + 5] : _box[b + 2];
                if (nx * x + ny * y + nz * z + d < 0f) return false;
            }
            return true;
        }

        float BoxDistance(int p, float x, float y, float z)
        {
            int b = p * 6;
            float dx = Math.Max(Math.Max(_pbox[b] - x, 0f), x - _pbox[b + 3]);
            float dy = Math.Max(Math.Max(_pbox[b + 1] - y, 0f), y - _pbox[b + 4]);
            float dz = Math.Max(Math.Max(_pbox[b + 2] - z, 0f), z - _pbox[b + 5]);
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Sutherland-Hodgman: keeps the part of polygon src (n points) with nx*x + ny*y + nz*z + d &gt;= -1e-4 (planeCull's epsilon).</summary>
        static int ClipPolygon(float[] src, int n, float[] dst, float nx, float ny, float nz, float d, ref bool overflow)
        {
            const float eps = 1e-4f;
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float t1 = nx * src[i * 3] + ny * src[i * 3 + 1] + nz * src[i * 3 + 2] + d;
                float t2 = nx * src[j * 3] + ny * src[j * 3 + 1] + nz * src[j * 3 + 2] + d;
                bool in1 = t1 >= -eps, in2 = t2 >= -eps;
                if (in1 != in2)
                {
                    if (m >= MaxPoly) { overflow = true; return 0; }
                    float k = t1 / (t1 - t2);
                    dst[m * 3] = src[i * 3] + (src[j * 3] - src[i * 3]) * k;
                    dst[m * 3 + 1] = src[i * 3 + 1] + (src[j * 3 + 1] - src[i * 3 + 1]) * k;
                    dst[m * 3 + 2] = src[i * 3 + 2] + (src[j * 3 + 2] - src[i * 3 + 2]) * k;
                    m++;
                }
                if (in2)
                {
                    if (m >= MaxPoly) { overflow = true; return 0; }
                    dst[m * 3] = src[j * 3]; dst[m * 3 + 1] = src[j * 3 + 1]; dst[m * 3 + 2] = src[j * 3 + 2];
                    m++;
                }
            }
            return m;
        }

        static void SortAroundCentroid(List<float[]> v, float nx, float ny, float nz)
        {
            if (v.Count < 3) return;
            float cx = 0, cy = 0, cz = 0;
            foreach (var p in v) { cx += p[0]; cy += p[1]; cz += p[2]; }
            cx /= v.Count; cy /= v.Count; cz /= v.Count;
            // u = any axis in the plane, w = n x u
            float ux, uy, uz;
            if (Math.Abs(nz) < 0.9f) { ux = -ny; uy = nx; uz = 0f; } else { ux = 0f; uy = -nz; uz = ny; }
            float ul = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
            float wx = ny * uz - nz * uy, wy = nz * ux - nx * uz, wz = nx * uy - ny * ux;
            v.Sort((p, q) =>
            {
                double ap = Math.Atan2((p[0] - cx) * wx + (p[1] - cy) * wy + (p[2] - cz) * wz, (p[0] - cx) * ux + (p[1] - cy) * uy + (p[2] - cz) * uz);
                double aq = Math.Atan2((q[0] - cx) * wx + (q[1] - cy) * wy + (q[2] - cz) * wz, (q[0] - cx) * ux + (q[1] - cy) * uy + (q[2] - cz) * uz);
                return ap.CompareTo(aq);
            });
        }
    }
}
