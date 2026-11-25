// Lead fix (2026-10-03, the user: "when I polymorph into something, I can still see my avatar ... make sure that polymorph, all of them,
// hides the player avatar for all"). Player.log: "Gear: body of the local player: humanoid Animator 'MirrorRoot'" - the forms hid the
// MIRROR copy of the avatar (drawn only by mirrors and the tablet camera) and left the copy the main camera draws visible.
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// The visual copies ("roots") of one Somnium avatar, found by the client's type NAMES (no reference to client types): an avatar
    /// holder (MainRootsHolder : RootsHolder, CS AvatarSystem/Scripts/Holders) owns a MainRoot (NonXRMainRoot on desktop) = what the
    /// main camera draws, a MirrorRoot ("Root for the tablet camera and mirrors", MirrorRoot.cs: SetLayerToAllChild(NON_RENDERER_BY_MAIN_CAMERA_LAYER))
    /// posed like the main one, a TeleportRoot and a WearablesRoot ("only functional root, it doesn't have visual", RootsHolder.cs
    /// SetActiveAllVisualRoots). Each root has its own humanoid Animator (BaseAvatarRoot._animatorRef). The client's AnimatorHandler
    /// disables the MAIN Animator and updates it by hand for far avatars (SetManualUpdateRate, layer >= 2), so "enabled" does not
    /// tell which copy is seen.
    /// Fix 2026-10-04 (remote animation, avatar change): the WearablesRoot is a copy too. BaseAvatarRoot.Initialize instantiates the
    /// avatar model under EVERY root and names it "Root" (CS Roots/Base/BaseAvatarRoot.cs:140 `_root.name = "Root";`); the
    /// RootsMirroringHandler copies the main copy's bones into the wearables copy every LateUpdate (order 0), so its head lies exactly
    /// where the main one's does; WearablesRoot.Initialize draws it as shadows only for the local player (`SetShadowMode(ShadowsOnly)`)
    /// and not at all for a remote one (`ActivateAllRenderers(false)`). Unknown here it was a "no client root" candidate equal to the
    /// main copy (Player.log: "humanoid Animator 'Root' (no client root ...)" after an avatar change) - posing it moved nothing visible.
    /// </summary>
    internal static class GearAvatarRoots
    {
        public const string Main = "MainRoot", NonXrMain = "NonXRMainRoot", Mirror = "MirrorRoot", Teleport = "TeleportRoot", Wearables = "WearablesRoot";
        private const int MaxUp = 3, MaxHolderUp = 12;
        private static readonly List<MonoBehaviour> Buf = new List<MonoBehaviour>(16);
        private static readonly List<MonoBehaviour> All = new List<MonoBehaviour>(64);

        /// <summary>The client root type on this object or up to 3 parents above it ("MainRoot", "NonXRMainRoot", "MirrorRoot",
        /// "TeleportRoot", "WearablesRoot"), null when none (a world's own humanoid, an unknown client build).</summary>
        public static string KindOf(Component c)
        {
            Transform t = c != null ? c.transform : null;
            for (int up = 0; t != null && up <= MaxUp; up++, t = t.parent)
            {
                string k = RootTypeOn(t);
                if (k != null) return k;
            }
            return null;
        }

        /// <summary>A copy the main camera does not draw (the mirror / tablet-camera copy, the teleport preview, the wearables copy).</summary>
        public static bool IsSecondary(Component c)
        {
            string k = KindOf(c);
            return k == Mirror || k == Teleport || k == Wearables;
        }

        /// <summary>Fix 2026-10-04: the copy the main camera draws (MainRoot, NonXRMainRoot - also a culled avatar's CULLING holder's).</summary>
        public static bool IsMain(Component c)
        {
            string k = KindOf(c);
            return k == Main || k == NonXrMain;
        }

        /// <summary>
        /// Fix 2026-10-04 (the user: remote players saw the armour "all messed up" after "I went behind them"): a copy of the client's
        /// STAND-IN avatar, not the player's own. AvatarsCullingSystem culls another player farther than its near distance (15 m,
        /// AVATARS_LODS_NEAR_DISTANCE) or past the nearest 8: AvatarHoldersHandler.SetMainHolderCulling(true) deactivates the MAIN holder's
        /// visual roots and activates the CULLING holder's model (CullingRootsHolder, CS Holders/Common), a generic avatar with its own
        /// humanoid Animator (named "Root", under its own NonXRMainRoot - so KindOf says "NonXRMainRoot"); StartSwitchingAvatar shows the
        /// DUMMY holder's (DummyRootsHolder) while a new avatar loads. Player.log: "humanoid Animator 'Root' (NonXRMainRoot ...)", "measured
        /// the avatar: 0 meshes, 0 vertices, 2 not readable, 0 of 14 segments" - the armour shell then fell back to WoW's own body.
        /// </summary>
        public static bool IsStandIn(Component c)
        {
            Transform holder = HolderOf(c != null ? c.transform : null);
            if (holder == null) return false;
            Buf.Clear();
            holder.GetComponents(Buf);
            bool standIn = false;
            for (int i = 0; i < Buf.Count && !standIn; i++)
            {
                if (Buf[i] == null) continue;
                string n = Buf[i].GetType().Name;
                standIn = n == CullingHolder || n == DummyHolder;
            }
            Buf.Clear();
            return standIn;
        }

        public const string CullingHolder = "CullingRootsHolder", DummyHolder = "DummyRootsHolder";

        /// <summary>
        /// Every visual root of the avatar this model belongs to (main, mirror, teleport): searched under its holder (the nearest
        /// parent with a "...RootsHolder" component), else under the model's top object - then only roots whose head bone lies within
        /// 1 m of the model's (never another player's avatar). Always contains the model itself.
        /// </summary>
        public static void VisualRoots(Animator model, List<Transform> into)
        {
            into.Clear();
            if (model == null) return;
            into.Add(model.transform);
            Transform holder = HolderOf(model.transform);
            Transform scope = holder != null ? holder : model.transform.root;
            if (scope == null) return;
            Transform myHead = model.isHuman ? model.GetBoneTransform(HumanBodyBones.Head) : null;
            All.Clear();
            scope.GetComponentsInChildren(true, All);
            for (int i = 0; i < All.Count; i++)
            {
                MonoBehaviour m = All[i];
                if (m == null) continue;
                string n = m.GetType().Name;
                if (n != Main && n != NonXrMain && n != Mirror && n != Teleport) continue;
                Transform t = m.transform;
                if (Contains(into, t)) continue;
                if (holder == null && myHead != null)
                {
                    Animator a = t.GetComponentInChildren<Animator>(true);
                    Transform h = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null;
                    if (h == null || (h.position - myHead.position).sqrMagnitude > 1f) continue;
                }
                into.Add(t);
            }
            All.Clear();
        }

        private static bool Contains(List<Transform> list, Transform t)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == t) return true;
            return false;
        }

        /// <summary>The avatar holder above this transform (a component whose type name ends with "RootsHolder"), null when none.</summary>
        public static Transform HolderOf(Transform t)
        {
            if (t == null) return null;
            for (int up = 0; t != null && up <= MaxHolderUp; up++, t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                    if (Buf[i] != null && Buf[i].GetType().Name.EndsWith("RootsHolder", System.StringComparison.Ordinal)) { Buf.Clear(); return t; }
            }
            Buf.Clear();
            return null;
        }

        private static string RootTypeOn(Transform t)
        {
            Buf.Clear();
            t.GetComponents(Buf);
            string found = null;
            for (int i = 0; i < Buf.Count && found == null; i++)
            {
                if (Buf[i] == null) continue;
                string n = Buf[i].GetType().Name;
                if (n == Main || n == NonXrMain || n == Mirror || n == Teleport || n == Wearables) found = n;
            }
            Buf.Clear();
            return found;
        }
    }
}
