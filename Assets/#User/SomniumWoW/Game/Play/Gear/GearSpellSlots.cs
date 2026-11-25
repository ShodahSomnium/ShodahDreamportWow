using SomniumSpace.Worlds.SomniumWoW.Models;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// vfxanchor fix 2026-10-04 (the user, a paladin: "spells that have effects on the body, I think they are going to hip and they look
    /// bad"). Where a Somnium avatar holds each SpellVisualKit slot - ONE rule for the local player (WowSpellVfx.PlaceAnchors, desktop
    /// and VR) and every remote avatar (GearCast.Place), so the two can never drift apart again.
    ///
    /// The slots are WoW M2 attachments (SpellVisualDb.SlotAttachment {20,34,19,21,22,17,2,1,23,24,25,19}, SlotFallback
    /// {11,15,-1,2,1,20,21,22,18,18,18,-1}); names from WoW Model Viewer's POSITION_SLOTS enum (wow_enums.h): 1 RIGHT_PALM, 2 LEFT_PALM,
    /// 11 HELMET, 15 BUST, 17 FACE, 18 ABOVE_CHARACTER, 19 GROUND, 20 TOP_OF_HEAD, 21 LEFT_PALM2, 22 RIGHT_PALM2, 23 PRE_CAST_2L,
    /// 24 PRE_CAST_2R, 25 PRE_CAST_3, 34 BUST3 ("Spell Impact"). Measured on WoW's HumanMale.m2 (harness SfxAttach, yards, X forward,
    /// Z up): 20 (-0.08, 0, 2.03), 18 (-0.08, 0, 2.21), 34 (0.18, 0, 1.43), 17 (0.17, 0, 1.80), 19 (0, 0, 0); 23-25 exist only on the
    /// Draenei models, so Special1-3 fall back to 18 above the head on every other player model. In the render lab (WoW Human male
    /// playing ReadySpellOmni 52 / SpellCastOmni 54) the frames of 19 and 20 (and 18) stay on the MODEL ROOT (forward (0,0,1)) while
    /// 34 turns with the chest bone (forward (0.94, 0.13, 0.31)) - the WoW cast animations twist the torso ~70 degrees.
    ///
    /// Before: every body slot took the yaw of the shoulder line (GearBody.Forward), so while the WoW cast animation twists the posed
    /// torso the head, feet and above-head effects turned 65-90 degrees sideways (lab: forward (0.91, 0, 0.42) at 52, (1.00, 0, 0.10) at
    /// 54); the head point sat 0.1 m over the head bone (RPM template: 0.13 m under the top of its head) and Special1-3 at the neck.
    /// Ours now:
    ///   Base / World (19)   the feet, frame = the avatar model's root yaw (humanoid: its Animator transform; else GearBody.Forward)
    ///   Head (20)           the top of the head: the humanoid Head bone + CrownAboveHeadBone x Scale up (else Body.Head + 0.1 m),
    ///                       frame = the root yaw
    ///   Special1-3 (23-25, fallback 18)  above the head: the crown + AboveCrown x the crown's height, frame = the root yaw
    ///   Chest (34)          the upper chest bone + ChestForward x the crown's height along the torso's forward, frame = the torso yaw
    ///   Breath (17)         in front of the mouth as before (Body.Head + 0.12 m forward - 0.08 m), frame = the root yaw
    ///   Left / RightHand (21 / 22)    the hand (VR: the tracked controller; desktop: the posed hand bone) as before, frame = the torso yaw
    ///   Left / RightWeapon (2 / 1)    the hand's grip frame as before (weapon enchant glows unchanged)
    /// </summary>
    public static class GearSpellSlots
    {
        /// <summary>Ours, measured on the Somnium template avatar (RPM Men.fbx, render lab): drawn mesh top 1.866 m, Head bone 1.638 m,
        /// GearBody.Scale 0.976 -> 0.228 / 0.976 = 0.234 m from the Head bone to the top of the head per unit of Scale.</summary>
        public const float CrownAboveHeadBone = 0.234f;

        /// <summary>WoW HumanMale: 18 is 0.18 yd over 20 (2.21 - 2.03), as a fraction of the crown's height (2.03 yd).</summary>
        public const float AboveCrown = 0.18f / 2.03f;

        /// <summary>WoW HumanMale: 34 is 0.18 yd in front of the body axis at 1.43 yd, as a fraction of the crown's height (2.03 yd).</summary>
        public const float ChestForward = 0.18f / 2.03f;

        public static readonly string[] SlotNames = { "Head", "Chest", "Base", "LeftHand", "RightHand", "Breath", "LeftWeapon", "RightWeapon", "Special1", "Special2", "Special3", "World" };

        /// <summary>Places the 12 slot anchors (null entries skipped) at the posed body b, shifted by draw (a remote's drawn offset).</summary>
        public static void Place(GearBody b, Vector3 draw, Transform[] slots)
        {
            if (b == null || slots == null || slots.Length < 12) return;
            Quaternion torso = Yaw(b.Forward);
            Quaternion root = Yaw(RootForward(b));
            Vector3 crown = Crown(b);
            float crownH = Mathf.Max(0.5f, crown.y - b.Feet.y);
            Set(slots, SpellVisualDb.Slot.Head, crown + draw, root);
            Set(slots, SpellVisualDb.Slot.Chest, b.Chest + Flat(b.Forward) * (ChestForward * crownH) + draw, torso);
            Set(slots, SpellVisualDb.Slot.Base, b.Feet + draw, root);
            Set(slots, SpellVisualDb.Slot.World, b.Feet + draw, root);
            Set(slots, SpellVisualDb.Slot.Breath, b.HeadPos + b.Forward * 0.12f - Vector3.up * 0.08f + draw, root);
            Vector3 special = crown + Vector3.up * (AboveCrown * crownH) + draw;
            Set(slots, SpellVisualDb.Slot.Special1, special, root);
            Set(slots, SpellVisualDb.Slot.Special2, special, root);
            Set(slots, SpellVisualDb.Slot.Special3, special, root);
            Vector3 p; Quaternion r;
            if (b.HandPose(false, out p, out r)) { Set(slots, SpellVisualDb.Slot.LeftHand, p + draw, torso); Set(slots, SpellVisualDb.Slot.LeftWeapon, p + draw, r); }
            if (b.HandPose(true, out p, out r)) { Set(slots, SpellVisualDb.Slot.RightHand, p + draw, torso); Set(slots, SpellVisualDb.Slot.RightWeapon, p + draw, r); }
        }

        /// <summary>One line for the log: the M2 attachment of a slot and where this avatar holds it.</summary>
        public static string Describe(int slot, GearBody b)
        {
            if (slot < 0 || slot >= 12) return "slot " + slot;
            int att = SpellVisualDb.SlotAttachment[slot], fb = SpellVisualDb.SlotFallback[slot];
            string head = b != null && b.Humanoid ? "the humanoid Head bone + " + CrownAboveHeadBone.ToString("0.000") + " m x scale " + b.Scale.ToString("0.00") + " up (top of the head)" : "Body.Head + 0.10 m up";
            string rootYaw = b != null && b.Humanoid ? "the avatar model's root yaw" : "the body yaw";
            string where;
            switch ((SpellVisualDb.Slot)slot)
            {
                case SpellVisualDb.Slot.Head: where = head + ", frame " + rootYaw; break;
                case SpellVisualDb.Slot.Chest: where = (b != null && b.Humanoid ? "the UpperChest bone" : "the chest point") + " + " + ChestForward.ToString("0.000") + " x crown height forward, frame the torso yaw"; break;
                case SpellVisualDb.Slot.Base:
                case SpellVisualDb.Slot.World: where = "the feet, frame " + rootYaw; break;
                case SpellVisualDb.Slot.Breath: where = "Body.Head + 0.12 m forward - 0.08 m (the mouth), frame " + rootYaw; break;
                case SpellVisualDb.Slot.LeftHand:
                case SpellVisualDb.Slot.RightHand: where = (b != null && b.XrRight != null ? "the tracked VR controller" : "the posed hand bone (palm)") + ", frame the torso yaw"; break;
                case SpellVisualDb.Slot.LeftWeapon:
                case SpellVisualDb.Slot.RightWeapon: where = (b != null && b.XrRight != null ? "the tracked VR controller" : "the posed hand bone") + ", frame the hand's grip"; break;
                default: where = "the top of the head + " + AboveCrown.ToString("0.000") + " x crown height up (WoW falls back to 18 above the character), frame " + rootYaw; break;
            }
            return "attachment " + att + " (" + SlotNames[slot] + (fb >= 0 ? ", fallback " + fb : "") + ") on the avatar -> " + where;
        }

        /// <summary>The top of the head (the WoW Head attachment 20).</summary>
        public static Vector3 Crown(GearBody b)
        {
            Animator a = b.Animator;
            Transform hb = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            if (hb != null) return hb.position + b.Up * (CrownAboveHeadBone * b.Scale);
            return b.HeadPos + Vector3.up * 0.1f;
        }

        /// <summary>The avatar model's facing (WoW's model root frame): its Animator transform, else the body's forward.</summary>
        public static Vector3 RootForward(GearBody b)
        {
            Animator a = b.Animator;
            if (a != null)
            {
                // the WoW cast animations twist the torso up to ~85 degrees from the root (lab, SpellCastOmni 54); a root more than
                // 120 degrees from the torso is not this avatar's facing (an odd rig) - the torso's yaw then
                Vector3 f = Flat(a.transform.forward);
                if (f.sqrMagnitude > 1e-6f && Vector3.Dot(f, Flat(b.Forward)) > -0.5f) return f;
            }
            return b.Forward;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero; }

        static Quaternion Yaw(Vector3 forward)
        {
            Vector3 f = Flat(forward);
            return Quaternion.LookRotation(f.sqrMagnitude > 1e-6f ? f : Vector3.forward, Vector3.up);
        }

        static void Set(Transform[] slots, SpellVisualDb.Slot s, Vector3 pos, Quaternion rot)
        {
            Transform t = slots[(int)s];
            if (t != null) t.SetPositionAndRotation(pos, rot);
        }
    }
}
