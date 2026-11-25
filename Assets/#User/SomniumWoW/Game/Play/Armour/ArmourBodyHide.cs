// Lead 2026-10-04 (the user: "add a checkbox on the armour section 'Hide Avatar Part' - so if I place a chest piece on, but my avatars
// body is clipping through, I can hide my avatar part to only show the immersive armour, the armour of WoW").
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Armour
{
    /// <summary>
    /// Hides the avatar's body under a full-armour piece, on every visual copy (main, desktop, the mirror copy the Somnium camera and
    /// mirrors draw), skin and clothes alike: a COPY of each affected skinned mesh whose vertices of that body part are moved onto their
    /// own bone's bind position (inside the body) - the triangles collapse and draw nothing. Only vertex positions change: the index
    /// buffer, and with it Unity 6's mesh LOD ranges, stay as they are (ArmourHeadCut's SetTriangles reset them: Player.log "MeshLod levels
    /// ... would be out of range"). The originals come back when the flags go off, the armour goes, or around a measurement (Suspend).
    /// - Body parts by each vertex's strongest bone, walked up to a humanoid bone of the followed Animator (matched by name in every copy):
    ///   waist = Hips + Spine; chest = Chest + UpperChest + the shoulders (clavicles); arms = upper + lower arms; hands = hands + fingers;
    ///   legs = upper + lower legs; feet = feet + toes. The neck and head are left (the helm's "Face in helm" hides the head its own way).
    /// - Recomputed only when the wanted parts or the avatar's meshes change, at most every MinGap. Values ours.
    /// </summary>
    public sealed class ArmourBodyHide
    {
        public const float MinGap = 0.25f;

        private sealed class Swap { public SkinnedMeshRenderer R; public Mesh Original, Copy; }

        private readonly string _label;
        private readonly List<Swap> _swaps = new List<Swap>();
        private readonly List<Transform> _roots = new List<Transform>();
        private readonly HashSet<SkinnedMeshRenderer> _seen = new HashSet<SkinnedMeshRenderer>();
        private readonly Dictionary<string, int> _partOfName = new Dictionary<string, int>();
        private int _key = int.MinValue, _logs;
        private float _lastAt = -10f;
        private bool _suspended;

        public ArmourBodyHide(string label) { _label = label; }

        /// <summary>Every frame from ArmourShell.Place: mask = the ArmourAdjust slots (3 chest .. 8 feet) to hide, as bits 1 << slot.</summary>
        public void Apply(Animator avatar, int mask, int avatarKey, float now)
        {
            if (_suspended) return;
            if (avatar == null || !avatar.isHuman || mask == 0) { Restore(); _key = int.MinValue; return; }
            bool stale = false;
            for (int i = 0; i < _swaps.Count; i++)
                if (_swaps[i].R == null || _swaps[i].R.sharedMesh != _swaps[i].Copy) { stale = true; break; }
            int key = mask * 31 + avatarKey;
            if (!stale && key == _key) return;
            if (now - _lastAt < MinGap) return;
            _lastAt = now;
            Restore();
            _key = key;
            try { Build(avatar, mask); }
            catch (Exception e)
            {
                Restore();
                if (_logs++ < 6) WowLog.Warn("Armour: hiding the avatar under the armour of " + _label + " failed (" + e.GetType().Name + ": " + e.Message + ") - the avatar is drawn whole.");
            }
        }

        /// <summary>The originals back for a moment (ArmourShell.Measure bakes the avatar's real shape); Resume re-applies next frame.</summary>
        public void Suspend() { Restore(); _key = int.MinValue; _suspended = true; }
        public void Resume() { _suspended = false; }

        private void Build(Animator avatar, int mask)
        {
            _partOfName.Clear();
            Name(avatar, HumanBodyBones.Hips, ArmourAdjust.Waist); Name(avatar, HumanBodyBones.Spine, ArmourAdjust.Waist);
            Name(avatar, HumanBodyBones.Chest, ArmourAdjust.Chest); Name(avatar, HumanBodyBones.UpperChest, ArmourAdjust.Chest);
            Name(avatar, HumanBodyBones.LeftShoulder, ArmourAdjust.Chest); Name(avatar, HumanBodyBones.RightShoulder, ArmourAdjust.Chest);
            Name(avatar, HumanBodyBones.LeftUpperArm, ArmourAdjust.Arms); Name(avatar, HumanBodyBones.RightUpperArm, ArmourAdjust.Arms);
            Name(avatar, HumanBodyBones.LeftLowerArm, ArmourAdjust.Arms); Name(avatar, HumanBodyBones.RightLowerArm, ArmourAdjust.Arms);
            Name(avatar, HumanBodyBones.LeftHand, ArmourAdjust.Hands); Name(avatar, HumanBodyBones.RightHand, ArmourAdjust.Hands);
            Name(avatar, HumanBodyBones.LeftUpperLeg, ArmourAdjust.Legs); Name(avatar, HumanBodyBones.RightUpperLeg, ArmourAdjust.Legs);
            Name(avatar, HumanBodyBones.LeftLowerLeg, ArmourAdjust.Legs); Name(avatar, HumanBodyBones.RightLowerLeg, ArmourAdjust.Legs);
            Name(avatar, HumanBodyBones.LeftFoot, ArmourAdjust.Feet); Name(avatar, HumanBodyBones.RightFoot, ArmourAdjust.Feet);
            Name(avatar, HumanBodyBones.LeftToes, ArmourAdjust.Feet); Name(avatar, HumanBodyBones.RightToes, ArmourAdjust.Feet);
            // the neck and head stop the walk (left visible)
            Name(avatar, HumanBodyBones.Neck, -1); Name(avatar, HumanBodyBones.Head, -1);

            GearAvatarRoots.VisualRoots(avatar, _roots);
            _seen.Clear();
            int meshes = 0, hidden = 0, all = 0;
            for (int r = 0; r < _roots.Count; r++)
            {
                if (_roots[r] == null) continue;
                foreach (SkinnedMeshRenderer smr in _roots[r].GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null || !_seen.Add(smr)) continue;
                    Mesh src = smr.sharedMesh;
                    if (src == null || !src.isReadable) continue;
                    Transform[] bones = smr.bones;
                    Matrix4x4[] bind = src.bindposes;
                    if (bones == null || bones.Length == 0 || bind == null || bind.Length != bones.Length) continue;
                    var part = new int[bones.Length];
                    var at = new Vector3[bones.Length];   // each bone's own place in the mesh's bind space
                    for (int b = 0; b < bones.Length; b++) at[b] = bind[b].inverse.MultiplyPoint3x4(Vector3.zero);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        part[b] = -1;
                        for (Transform t = bones[b]; t != null; t = t.parent)
                        {
                            int p;
                            if (_partOfName.TryGetValue(t.name, out p)) { part[b] = p; break; }
                        }
                    }
                    BoneWeight[] w = src.boneWeights;
                    Vector3[] v = src.vertices;
                    if (w.Length != v.Length) continue;
                    all++;
                    int moved = 0;
                    for (int i = 0; i < v.Length; i++)
                    {
                        BoneWeight bw = w[i];
                        int bi = bw.boneIndex0; float best = bw.weight0;
                        if (bw.weight1 > best) { best = bw.weight1; bi = bw.boneIndex1; }
                        if (bw.weight2 > best) { best = bw.weight2; bi = bw.boneIndex2; }
                        if (bw.weight3 > best) { bi = bw.boneIndex3; }
                        if (bi < 0 || bi >= part.Length) continue;
                        int p = part[bi];
                        if (p < 0 || (mask & (1 << p)) == 0) continue;
                        v[i] = at[bi];
                        moved++;
                    }
                    if (moved == 0) continue;
                    Mesh copy = UnityEngine.Object.Instantiate(src);
                    copy.name = src.name + " (WoW armour: part hidden)";
                    copy.vertices = v;   // positions only - the index buffer and its LOD ranges are untouched
                    smr.sharedMesh = copy;
                    _swaps.Add(new Swap { R = smr, Original = src, Copy = copy });
                    meshes++;
                    hidden += moved;
                }
            }
            if (_logs++ < 8)
                WowLog.Info("Armour: avatar of " + _label + " hidden under the armour (" + Parts(mask) + ") - " + hidden + " vertices on " + meshes + " of " + all + " skinned meshes, "
                            + _roots.Count + " visual copies (positions only; the meshes' LOD ranges untouched).");
        }

        private void Name(Animator a, HumanBodyBones hb, int part)
        {
            Transform t = a.GetBoneTransform(hb);
            if (t != null && !_partOfName.ContainsKey(t.name)) _partOfName[t.name] = part;
        }

        private static string Parts(int mask)
        {
            var sb = new System.Text.StringBuilder();
            for (int s = ArmourAdjust.Chest; s <= ArmourAdjust.Feet; s++)
                if ((mask & (1 << s)) != 0) sb.Append(sb.Length > 0 ? ", " : "").Append(ArmourAdjust.Names[s].ToLowerInvariant());
            return sb.ToString();
        }

        public void Restore()
        {
            for (int i = 0; i < _swaps.Count; i++)
            {
                Swap s = _swaps[i];
                if (s.R != null && s.R.sharedMesh == s.Copy) s.R.sharedMesh = s.Original;
                if (s.Copy != null) UnityEngine.Object.Destroy(s.Copy);
            }
            _swaps.Clear();
        }

        public void Destroy() { Restore(); _key = int.MinValue; _suspended = false; }
    }
}
