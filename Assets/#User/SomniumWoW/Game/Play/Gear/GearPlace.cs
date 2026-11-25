using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Where WoW hangs a weapon, translated onto a Somnium body (GearBody's torso frame; offsets scale with the body).
    /// Item.dbc SheatheType, 3.3.5 (TrinityCore's Item.dbc page; wowdev lists no values):
    ///   1 two-handed "Diagonally across the back pointing downwards"   -> grip over the LEFT shoulder, blade down to the right
    ///   2 staff      "Diagonally across the back pointing upwards"     -> middle on the upper back, head up over the LEFT shoulder
    /// (lead fix 2026-10-03: mirrored to match the WoW models - WowEntityView now uses attachments 27 / 30 like WoW Model Viewer 0.7
    /// database.cpp:675-680, and wowhead's sheath screenshot 1268589 shows a staff's head over the LEFT shoulder, its foot at the right leg)
    ///   3 one-handed "On the left-hand side of the character's waist"  -> left hip, blade down and back (the right hand cross-draws)
    ///   4 shield     "On the middle of the character's back"           -> face outward on the back
    ///   7 off hand   "On the right-hand side of the character's waist" -> right hip (the left hand cross-draws)
    /// Our rules where Item.dbc says 0 (Shadowglen's bows and the throwing knife): bows, guns and crossbows diagonally on the
    /// back (up to the right, crossing a two-hander or staff); thrown weapons and relics are not shown sheathed; a wand (or any
    /// InventoryType 26 shorter than 0.55 m) hangs on the right hip (lead 2026-10-10, so a hand can take it). An off-hand item with SheatheType 3 hangs on the right hip (a rogue's second dagger).
    /// Held (VR or drawn on desktop): the root (= WoW's grip) at the hand's grip pose with identity rotation - the blade
    /// comes out of the top of the fist, edges toward the knuckles; a bow's limbs run through the fist, string side toward
    /// the player; a shield lies along the forearm with its face outward.
    /// </summary>
    public static class GearPlace
    {
        public static string SlotName(int slot) { return slot == 15 ? "main hand" : slot == 16 ? "off hand" : slot == 17 ? "ranged" : "slot " + slot; }

        public static GearSpot SpotFor(GearItem it)
        {
            var i = it.Item;
            float len = it.Geo != null ? it.Geo.Length : 1f;
            if (it.Slot == 17)
            {
                // lead 2026-10-10 (the user: "or wands etc, make it so I just need to be near it"): a short gun / crossbow / wand
                // (InventoryType 26 under 0.55 m) on the right hip, where a hand can take it (ours; WoW hides wands sheathed)
                if (i.InventoryType == 15 || i.InventoryType == 26) return len < 0.55f ? (i.InventoryType == 26 ? GearSpot.HipRight : GearSpot.Hidden) : GearSpot.BackRanged;
                return GearSpot.Hidden; // 25 thrown, 28 relic
            }
            if (it.IsShield) return GearSpot.BackShield;
            switch (i.SheatheType)
            {
                case 1: return GearSpot.BackTwoHand;
                case 2: return GearSpot.BackStaff;
                case 3: return it.Slot == 16 ? GearSpot.HipRight : GearSpot.HipLeft;
                case 4: return GearSpot.BackShield;
                case 7: return GearSpot.HipRight;
                case 5: case 6: return it.Slot == 16 ? GearSpot.HipRight : GearSpot.HipLeft;
            }
            // SheatheType 0: by inventory type.
            switch (i.InventoryType)
            {
                case 17: return len > 1.6f ? GearSpot.BackStaff : GearSpot.BackTwoHand;
                case 14: return GearSpot.BackShield;
                case 23: return GearSpot.Hidden; // held-in-off-hand frills: not shown sheathed
                default: return it.Slot == 16 ? GearSpot.HipRight : GearSpot.HipLeft;
            }
        }

        /// <summary>The sheathed pose of an item's root (the grip).</summary>
        public static void Sheathed(GearBody b, GearItem it, out Vector3 pos, out Quaternion rot)
        {
            float s = b.Scale;
            Vector3 U = b.Up, R = b.Right, F = b.Forward;
            GearGeometry g = it.Geo;
            switch (it.Spot)
            {
                case GearSpot.BackTwoHand:
                {
                    pos = b.ShoulderL + U * (0.10f * s) - R * (0.02f * s) - F * (0.17f * s);
                    Vector3 blade = (-U * 0.87f + R * 0.50f - F * 0.04f).normalized;
                    rot = Quaternion.LookRotation(blade, Vector3.Cross(blade, F).normalized);
                    return;
                }
                case GearSpot.BackStaff:
                {
                    Vector3 centre = b.Chest + U * (0.05f * s) - F * (0.17f * s);
                    Vector3 dir = (U * 0.90f - R * 0.44f - F * 0.02f).normalized;
                    rot = Quaternion.LookRotation(dir, Vector3.Cross(dir, F).normalized);
                    pos = centre - rot * new Vector3(0f, 0f, g.Center.z);
                    return;
                }
                case GearSpot.HipLeft:
                case GearSpot.HipRight:
                {
                    float side = it.Spot == GearSpot.HipRight ? 1f : -1f;
                    pos = b.Hips + R * (0.19f * side * s) + F * (0.06f * s) + U * (0.06f * s);
                    Vector3 blade = (-U * 0.88f - F * 0.42f + R * (0.10f * side)).normalized;
                    Vector3 edge = F - Vector3.Dot(F, blade) * blade;
                    rot = Quaternion.LookRotation(blade, edge.normalized);
                    return;
                }
                case GearSpot.BackShield:
                {
                    Vector3 centre = b.Chest - F * (0.17f * s);
                    rot = Quaternion.LookRotation(U, R);
                    pos = centre - rot * g.Center;
                    return;
                }
                case GearSpot.BackRanged:
                {
                    Vector3 centre = b.Chest + U * (0.02f * s) - F * (0.21f * s);
                    Vector3 dir = (U * 0.85f + R * 0.52f - F * 0.02f).normalized;   // up to the right, crossing the back weapon (as WowEntityView's 31)
                    Vector3 up = F - Vector3.Dot(F, dir) * dir;
                    rot = Quaternion.LookRotation(dir, up.normalized);
                    pos = centre - rot * new Vector3(0f, 0f, g.Center.z);
                    return;
                }
                default:
                    pos = b.Chest;
                    rot = Quaternion.identity;
                    return;
            }
        }

        /// <summary>The held pose of an item's root from the hand's grip frame.</summary>
        public static void Held(GearItem it, Vector3 handPos, Quaternion handRot, out Vector3 pos, out Quaternion rot)
        {
            if (it.IsShield)
            {
                rot = handRot * Quaternion.LookRotation(Vector3.down, Vector3.forward);
                pos = handPos + handRot * new Vector3(0f, 0.06f, 0f);
                return;
            }
            pos = handPos;
            rot = handRot;
        }

        /// <summary>
        /// The part of a sheathed item a hand takes hold of (world segment): the handle of a blade (butt to just past the grip),
        /// the middle 0.8 m of a staff, bow or gun, the middle of a shield.
        /// </summary>
        public static void GrabSegment(GearItem it, Vector3 pos, Quaternion rot, out Vector3 a, out Vector3 b)
        {
            GearGeometry g = it.Geo;
            float z0, z1;
            if (it.IsShield) { Vector3 c = pos + rot * g.Center; a = c; b = c; return; }
            if (it.Spot == GearSpot.BackStaff || it.Spot == GearSpot.BackRanged)
            {
                z0 = Mathf.Max(g.Min.z, -0.4f);
                z1 = Mathf.Min(g.Max.z, 0.4f);
            }
            else
            {
                z0 = Mathf.Max(g.Min.z, -0.3f);
                z1 = Mathf.Min(g.Max.z, 0.22f);
            }
            a = pos + rot * new Vector3(0f, 0f, z0);
            b = pos + rot * new Vector3(0f, 0f, z1);
        }

        public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>The hitting part of a held weapon (world capsule): a blade from a quarter of its length to the tip; a staff end to end.</summary>
        public static void BladeCapsule(GearItem it, out Vector3 a, out Vector3 b, out float radius)
        {
            GearGeometry g = it.Geo;
            Transform t = it.T;
            float z0 = it.Spot == GearSpot.BackStaff ? g.Min.z : Mathf.Max(0.05f, g.Max.z * 0.25f);
            a = t.TransformPoint(new Vector3(0f, 0f, z0));
            b = t.TransformPoint(new Vector3(0f, 0f, g.Max.z));
            radius = g.HalfWidth;
        }
    }
}
