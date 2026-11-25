using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>How a WMO group is drawn: always (every normal group), only through its portal, or never.</summary>
    public enum WmoGroupDraw { Always = 0, ThroughPortal = 1, Never = 2 }

    /// <summary>
    /// One place a portal-only group can be seen from: inside the owner group's MOGP box, on the owner's side of the portal
    /// plane. WMO frame (yards, z up).
    /// </summary>
    public struct WmoPortalView
    {
        public int OwnerGroup, Portal, Side;
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;   // the owner group's MOGP bounding box
        public float NX, NY, NZ, D;                         // the portal plane (MOPT)

        /// <summary>The camera at (x, y, z) in the WMO frame is in the owner group's box and on its side of the portal.
        /// Side rule as WebWowViewerCpp's portal traversal: dot = n.p + d; side &lt; 0 needs dot &lt;= 0, else dot &gt;= 0.</summary>
        public bool Sees(float x, float y, float z)
        {
            if (x < MinX || x > MaxX || y < MinY || y > MaxY || z < MinZ || z > MaxZ) return false;
            float dot = NX * x + NY * y + NZ * z + D;
            return Side < 0 ? dot <= 0f : dot >= 0f;
        }
    }

    /// <summary>
    /// Iteration 11 (lod11): which WMO groups the 3.3.5a client never shows from where the player stands. We draw every group
    /// at once (no portal culling), so two kinds of groups showed where the client hides them:
    ///
    /// 1. Portal-only backdrops: MOGP UNREACHABLE (0x80), not EXTERIOR (0x8), not ALWAYSDRAW (0x10000), and no portal of their
    ///    own (MOGP portal count 0). The client draws exterior groups and ALWAYSDRAW groups from outside and reaches interior
    ///    groups only through portals (wowdev WMO: 0x8 "render all exteriors", 0x10000 ALWAYSDRAW; WebWowViewerCpp
    ///    wmoObject.cpp: exterior/ALWAYSDRAW groups first, then portal traversal into groups without EXTERIOR, a portal crossed
    ///    only when the camera is on the referencing group's side). Such a group is a stand-in painted behind someone else's
    ///    portal. Measured (probe Lod11): Stormwind.wmo group 93 'bazaarfacade03' (MOGP 0x28C1, 1399 tris) is reached only by
    ///    MOPR ref of group 257 'bazaar01' through portal 38 (plane x = -229.8 WMO frame, side -1 = the Trade District side);
    ///    it is a smaller, nearer copy of the cathedral (box x -467..-341, z 17..182) inside the real cathedral exterior
    ///    group 92 'cathy_facade01' (MOGP 0xA09, box x -550..-351, z -3..249). Census of all 1985 roots: 7 such groups
    ///    (Stormwind 93, StormwindHarbor 109/110/223, Blacktemple 39/40/48), every one reached by incoming portals only.
    ///    We draw it only while the camera is in an owner group's box on its side of that portal (the client's condition for
    ///    looking through it); never when no portal leads in. No collision: AzerothCore's vmap4_extractor skips every
    ///    UNREACHABLE group (WMOGroup::ShouldSkip, wmo.cpp:505), so the server has none there either.
    ///    Kept as before: UNREACHABLE groups that are EXTERIOR (Darnassus treetops, ...), ALWAYSDRAW (Stormwind 'mountains')
    ///    or have their own portal (GunDrak rooms: a two-way portal pair, e.g. GundrakGrate 'CentralRoom' below the grate).
    /// 2. Antiportals: a group named "antiportal" or with MOGP 0x4000000. wowdev WMO: for a group named 'antiportal' the client
    ///    calls CMapObjGroup::CreateOccluders() and sets 0x4000000 and 0x80; 0x4000000 "requires intBatchCount == 0,
    ///    extBatchCount == 0, UNREACHABLE". They are occluders, not drawn geometry (Sunwell_Raid_Facade's, placed on Quel'Danas,
    ///    is textured DUNGEONS\TEXTURES\TEMP\ANTI-PORTAL.BLP); AzerothCore's ShouldSkip skips them as well. Census: 31 groups.
    /// </summary>
    public static class WmoGroupVisibility
    {
        public const uint G_UNREACHABLE = 0x80, G_EXTERIOR = 0x8, G_ALWAYSDRAW = 0x10000, G_ANTIPORTAL = 0x4000000;

        /// <summary>Classifies group gi of a loaded WMO. views: the places a ThroughPortal group is seen from (else null).
        /// why: a short reason for the log (null for Always).</summary>
        public static WmoGroupDraw Classify(WmoModel m, int gi, out WmoPortalView[] views, out string why)
        {
            views = null; why = null;
            var g = m.Groups[gi];
            if (g == null) return WmoGroupDraw.Always;
            var root = m.Root;
            string name = root.GroupName(g.NameOffset);
            if ((g.Flags & G_ANTIPORTAL) != 0 || string.Equals(name, "antiportal", StringComparison.OrdinalIgnoreCase))
            {
                why = "antiportal (MOGP 0x" + g.Flags.ToString("X") + ", name '" + name + "'): the client makes occluders of it, AzerothCore's vmaps skip it";
                return WmoGroupDraw.Never;
            }
            if ((g.Flags & G_UNREACHABLE) == 0 || (g.Flags & (G_EXTERIOR | G_ALWAYSDRAW)) != 0 || g.PortalCount != 0) return WmoGroupDraw.Always;

            var list = new List<WmoPortalView>();
            for (int o = 0; o < m.Groups.Length; o++)
            {
                var og = m.Groups[o];
                if (og == null || o == gi) continue;
                for (int k = og.PortalStart; k < og.PortalStart + og.PortalCount && k < root.PortalRefs.Length; k++)
                {
                    var r = root.PortalRefs[k];
                    if (r.Group != gi || r.Portal < 0 || r.Portal >= root.Portals.Length) continue;
                    var p = root.Portals[r.Portal];
                    list.Add(new WmoPortalView
                    {
                        OwnerGroup = o, Portal = r.Portal, Side = r.Side,
                        MinX = og.MinX, MinY = og.MinY, MinZ = og.MinZ, MaxX = og.MaxX, MaxY = og.MaxY, MaxZ = og.MaxZ,
                        NX = p.NX, NY = p.NY, NZ = p.NZ, D = p.D,
                    });
                }
            }
            string head = "'" + name + "' (MOGP 0x" + g.Flags.ToString("X") + " unreachable interior, no portal of its own, " + g.TriangleCount + " tris)";
            if (list.Count == 0)
            {
                why = head + ": no portal leads into it - never drawn, no collision";
                return WmoGroupDraw.Never;
            }
            views = list.ToArray();
            var owners = new List<string>();
            foreach (var v in views)
            {
                string s = v.OwnerGroup + " '" + root.GroupName(m.Groups[v.OwnerGroup].NameOffset) + "'";
                if (!owners.Contains(s)) owners.Add(s);
            }
            why = head + ": drawn only while the camera is in group " + string.Join(", ", owners.ToArray()) + " on its side of the portal"
                + (views.Length > 1 ? "s (" + views.Length + ")" : " " + views[0].Portal) + " (the only way 3.3.5a shows it); no collision";
            return WmoGroupDraw.ThroughPortal;
        }
    }
}
