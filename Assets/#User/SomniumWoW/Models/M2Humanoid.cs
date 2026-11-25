// iteration 11 - owner avatar (proposal-avatar.md s4 + 6.C): the WoW character skeleton as humanoid joints, and the body sample the
// Somnium avatar is posed from. Pure: no Unity (the harness probes AvRigMap / AvPoseSample run it on the user's 20 race models).
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// One frame of a WoW character's body as the Somnium avatar is posed from it (GearPoser). The joint positions it is made from are
    /// in the MODEL frame: x right, y up, z forward (WoW model space converted: right = -Y, up = Z, forward = X; the origin is the ground
    /// point between the feet - the client does not root-move characters). Angles in degrees, all relative to WoW's Stand (frame 0), so
    /// the Stand pose samples to zero offsets / angles.
    /// </summary>
    public struct WowBodySample
    {
        public bool Valid;
        // LOWER BODY - in the MODEL frame, in leg lengths:
        public M2Vec3 HipsOffset;              // pelvis now - pelvis in Stand: the crouch, lunge and dodge hop of the whole body
        public float HipsYaw, HipsRoll;        // pelvis left -> right line vs the model facing (about up, about forward)
        public M2Vec3 RFoot, LFoot;            // ankle: x / z from the model origin, y = height above the Stand ankle height
        public M2Vec3 RKneePole, LKneePole;    // unit: the knee's side of the hip -> ankle line
        public float RToePitch, LToePitch;     // ankle -> toe vs horizontal, minus Stand's (0 on hooves)
        // UPPER BODY - in the CHEST frame (up = neck - pelvis, right = the shoulder joints' line; the GearBody / GearMotions frame):
        public float Twist, Bend, Lean;        // chest vs the pelvis' facing: about up (+ toward the right), about right (+ forward), about forward
        public float HeadYaw, HeadPitch;       // head vs chest (+ right, + up)
        public M2Vec3 RHand, LHand;            // wrist, in arm lengths from the shoulder-joint midpoint (GearKey.Pos convention)
        public M2Vec3 RElbowPole, LElbowPole;  // unit: the elbow's side of the shoulder -> wrist line
        public M2Quat RGrip, LGrip;            // the hand frame in the chest frame (forward = little -> index across the knuckles,
                                               // up = -(wrist -> knuckles)); filled by GearPuppet from the bones' rotations, identity here
    }

    /// <summary>The bone indices of the humanoid joints of one WoW character model (-1 = not found).</summary>
    public sealed class M2HumanoidMap
    {
        public int Pelvis = -1, Waist = -1, Chest = -1, Neck = -1, Head = -1;
        public int LClavicle = -1, RClavicle = -1, LShoulder = -1, RShoulder = -1, LElbow = -1, RElbow = -1, LHand = -1, RHand = -1;
        public int LHip = -1, RHip = -1, LKnee = -1, RKnee = -1, LAnkle = -1, RAnkle = -1, LToe = -1, RToe = -1;
        public float HipsZ;                    // bind Z of the waist (key 5), yards
        public bool Arms, Legs;
        public string Notes = "";
        public bool Valid { get { return Arms && Pelvis >= 0 && Chest >= 0 && Neck >= 0 && Head >= 0; } }

        /// <summary>The bone of a joint id (M2Humanoid.J*); HeadFwd = the head.</summary>
        public int Bone(int joint)
        {
            switch (joint)
            {
                case M2Humanoid.JPelvis: return Pelvis;
                case M2Humanoid.JChest: return Chest;
                case M2Humanoid.JNeck: return Neck;
                case M2Humanoid.JHead: case M2Humanoid.JHeadFwd: return Head;
                case M2Humanoid.JLShoulder: return LShoulder;
                case M2Humanoid.JRShoulder: return RShoulder;
                case M2Humanoid.JLElbow: return LElbow;
                case M2Humanoid.JRElbow: return RElbow;
                case M2Humanoid.JLHand: return LHand;
                case M2Humanoid.JRHand: return RHand;
                case M2Humanoid.JLHip: return LHip;
                case M2Humanoid.JRHip: return RHip;
                case M2Humanoid.JLKnee: return LKnee;
                case M2Humanoid.JRKnee: return RKnee;
                case M2Humanoid.JLAnkle: return LAnkle;
                case M2Humanoid.JRAnkle: return RAnkle;
                case M2Humanoid.JLToe: return LToe;
                case M2Humanoid.JRToe: return RToe;
                default: return -1;
            }
        }

        public string Describe()
        {
            return "pelvis " + Pelvis + " waist " + Waist + " chest " + Chest + " neck " + Neck + " head " + Head +
                   " | L arm " + LClavicle + "/" + LShoulder + "/" + LElbow + "/" + LHand + " R arm " + RClavicle + "/" + RShoulder + "/" + RElbow + "/" + RHand +
                   " | L leg " + LHip + "/" + LKnee + "/" + LAnkle + "/" + LToe + " R leg " + RHip + "/" + RKnee + "/" + RAnkle + "/" + RToe +
                   " | hipsZ " + HipsZ.ToString("0.###") + (Notes.Length > 0 ? " | " + Notes : "");
        }
    }

    /// <summary>What a body sample is measured against: WoW's Stand (frame 0) and the bind lengths (same units as the joints).</summary>
    public struct M2HumanoidRef
    {
        public bool Valid;
        public float LegLength, ArmLength;     // |hip-knee| + |knee-ankle|, |shoulder-elbow| + |elbow-wrist| in bind (mean of both sides)
        public M2Vec3 Pelvis;                  // Stand pelvis
        public float AnkleY;                   // Stand ankle height (mean)
        public float HipsYaw, HipsRoll, Twist, Bend, Lean, HeadYaw, HeadPitch, RToePitch, LToePitch;   // Stand's own angles (subtracted)
    }

    public static class M2Humanoid
    {
        // Joint ids of the float[] joint arrays (3 floats each, the model frame). HeadFwd = a point 0.1 yd in front of the head along
        // the head bone's own forward (GearPuppet / PoseJoints compute it from the bone's rotation).
        public const int JPelvis = 0, JChest = 1, JNeck = 2, JHead = 3, JHeadFwd = 4, JLShoulder = 5, JRShoulder = 6, JLElbow = 7, JRElbow = 8;
        public const int JLHand = 9, JRHand = 10, JLHip = 11, JRHip = 12, JLKnee = 13, JRKnee = 14, JLAnkle = 15, JRAnkle = 16, JLToe = 17, JRToe = 18;
        public const int JointCount = 19;

        private static int Key(M2Model m, int k) { return m.KeyBoneLookup != null && k < m.KeyBoneLookup.Length ? m.KeyBoneLookup[k] : -1; }

        private static float Dist(M2Vec3 a, M2Vec3 b)
        {
            float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>
        /// The measured rule (proposal-avatar.md s4; AvChains + AvArmUp on all 20 race/gender models): hand = the parent of any present
        /// finger key bone (R 8-12, L 13-17); walking up from the hand and skipping ancestors closer than 0.1 yd x (hipsZ / 1.16) to the
        /// current joint (twist bones): the next is the elbow, the next the shoulder joint, its parent the clavicle; chest = the parent of
        /// key 2; neck = the parent of key 6; pelvis = key 26; legs = children of key 5 with |Y| > 0.03 x s, Z &lt;= hipsZ + 0.05 x s and
        /// a subtree of >= 3 bones, followed down by the child with the largest subtree (ties: lowest Z; zero-length leaf helpers
        /// skipped): hip joint, knee, ankle = the last chain joint with bind Z >= 0.05 x hipsZ, toe = the next one (none on hooves).
        /// </summary>
        public static M2HumanoidMap Find(M2Model m)
        {
            var r = new M2HumanoidMap();
            if (m == null || m.Bones == null || m.Bones.Length == 0) { r.Notes = "no bones"; return r; }
            int n = m.Bones.Length;
            var kids = new List<int>[n];
            for (int i = 0; i < n; i++) kids[i] = new List<int>();
            for (int i = 0; i < n; i++) { int p = m.Bones[i].Parent; if (p >= 0 && p < n) kids[p].Add(i); }
            var sub = new int[n];
            for (int i = n - 1; i >= 0; i--) sub[i] = SubtreeSize(kids, i, sub);

            r.Waist = Key(m, 5);
            r.Pelvis = Key(m, 26);
            if (r.Pelvis < 0) r.Pelvis = r.Waist;
            r.Head = Key(m, 6);
            if (r.Head >= 0) r.Neck = m.Bones[r.Head].Parent;
            int kl = Key(m, 2);
            if (kl >= 0) r.Chest = m.Bones[kl].Parent;
            if (r.Chest < 0 && Key(m, 3) >= 0) r.Chest = m.Bones[Key(m, 3)].Parent;
            r.HipsZ = r.Waist >= 0 ? m.Bones[r.Waist].Pivot.Z : (r.Pelvis >= 0 ? m.Bones[r.Pelvis].Pivot.Z : 1.16f);
            float s = r.HipsZ > 0.05f ? r.HipsZ / 1.16f : 1f;
            float skip = 0.1f * s;

            // arms
            for (int side = 0; side < 2; side++)
            {
                int hand = -1;
                int k0 = side == 0 ? 13 : 8;
                for (int k = k0; k < k0 + 5 && hand < 0; k++) { int fb = Key(m, k); if (fb >= 0) hand = m.Bones[fb].Parent; }
                if (hand < 0) continue;
                int elbow = UpSkipping(m, hand, skip);
                int shoulder = elbow >= 0 ? UpSkipping(m, elbow, skip) : -1;
                int clav = shoulder >= 0 ? m.Bones[shoulder].Parent : -1;
                if (side == 0) { r.LHand = hand; r.LElbow = elbow; r.LShoulder = shoulder; r.LClavicle = clav; }
                else { r.RHand = hand; r.RElbow = elbow; r.RShoulder = shoulder; r.RClavicle = clav; }
            }
            r.Arms = r.LHand >= 0 && r.LElbow >= 0 && r.LShoulder >= 0 && r.RHand >= 0 && r.RElbow >= 0 && r.RShoulder >= 0;

            // legs
            int from = r.Waist >= 0 ? r.Waist : r.Pelvis;
            if (from >= 0)
            {
                foreach (int c in kids[from])
                {
                    M2Vec3 pv = m.Bones[c].Pivot;
                    if (Math.Abs(pv.Y) <= 0.03f * s || pv.Z > r.HipsZ + 0.05f * s || sub[c] < 3) continue;
                    List<int> chain = Chain(m, kids, sub, c);
                    if (chain.Count < 3) continue;
                    int ankle = -1, toe = -1;
                    for (int i = 2; i < chain.Count; i++)
                        if (m.Bones[chain[i]].Pivot.Z >= 0.05f * r.HipsZ) ankle = i;
                    if (ankle < 0) ankle = chain.Count - 1;
                    if (ankle + 1 < chain.Count) toe = chain[ankle + 1];
                    bool left = pv.Y > 0f;   // WoW model space: +Y = the model's left
                    if (left && r.LHip < 0) { r.LHip = chain[0]; r.LKnee = chain[1]; r.LAnkle = chain[ankle]; r.LToe = toe; }
                    else if (!left && r.RHip < 0) { r.RHip = chain[0]; r.RKnee = chain[1]; r.RAnkle = chain[ankle]; r.RToe = toe; }
                }
            }
            r.Legs = r.LHip >= 0 && r.LKnee >= 0 && r.LAnkle >= 0 && r.RHip >= 0 && r.RKnee >= 0 && r.RAnkle >= 0;
            if (!r.Arms) r.Notes += "arms not found; ";
            if (!r.Legs) r.Notes += "legs not found; ";
            return r;
        }

        private static int SubtreeSize(List<int>[] kids, int b, int[] memo)
        {
            int total = 1;
            foreach (int k in kids[b]) total += memo[k] > 0 ? memo[k] : SubtreeSize(kids, k, memo);
            return total;
        }

        /// <summary>The ancestor of b that is the next joint: the parent, skipping ancestors closer than skip to b (twist bones).</summary>
        private static int UpSkipping(M2Model m, int b, float skip)
        {
            M2Vec3 at = m.Bones[b].Pivot;
            int p = m.Bones[b].Parent;
            for (int hop = 0; hop < 8 && p >= 0; hop++)
            {
                if (Dist(m.Bones[p].Pivot, at) >= skip) return p;
                p = m.Bones[p].Parent;
            }
            return p;
        }

        private static List<int> Chain(M2Model m, List<int>[] kids, int[] sub, int start)
        {
            var l = new List<int> { start };
            int cur = start;
            for (int step = 0; step < 8; step++)
            {
                int best = -1, bs = -1;
                float bz = 1e9f;
                foreach (int i in kids[cur])
                {
                    float d = Dist(m.Bones[i].Pivot, m.Bones[cur].Pivot);
                    if (d < 1e-3f && sub[i] == 1) continue;   // a same-place leaf (twist / helper)
                    float z = m.Bones[i].Pivot.Z;
                    // a leg goes down: a child above its joint is not the next leg joint (TaurenFemale's knee 14 also parents bone 52 at
                    // z 1.55 yd, measured by the AvRigMap probe; ours: 0.05 yd)
                    if (z > m.Bones[cur].Pivot.Z + 0.05f) continue;
                    if (sub[i] > bs || (sub[i] == bs && z < bz)) { bs = sub[i]; best = i; bz = z; }
                }
                if (best < 0) break;
                l.Add(best);
                cur = best;
            }
            return l;
        }

        // ------------------------------------------------------------------ joints from a pose (pure FK matrices) and the samples

        /// <summary>WoW model space -> the model frame (x right = -Y, y up = Z, z forward = X), times scale.</summary>
        public static M2Vec3 ToFrame(M2Vec3 w, float scale) { return new M2Vec3(-w.Y * scale, w.Z * scale, w.X * scale); }

        private static M2Vec3 Mul(float[] mats, int bone, M2Vec3 p)
        {
            if (mats == null) return p;
            int o = bone * 16;
            if (o + 15 >= mats.Length) return p;
            return new M2Vec3(mats[o] * p.X + mats[o + 4] * p.Y + mats[o + 8] * p.Z + mats[o + 12],
                              mats[o + 1] * p.X + mats[o + 5] * p.Y + mats[o + 9] * p.Z + mats[o + 13],
                              mats[o + 2] * p.X + mats[o + 6] * p.Y + mats[o + 10] * p.Z + mats[o + 14]);
        }

        /// <summary>The joints of a pose (M2Pose.Compute matrices; null = bind) in the model frame, times scale (0.9144 = metres, as
        /// M2Factory builds bones; 1 = yards). Missing joints read as the pelvis.</summary>
        public static void PoseJoints(M2Model m, M2HumanoidMap map, float[] mats, float scale, float[] into)
        {
            for (int j = 0; j < JointCount; j++)
            {
                int b = map.Bone(j);
                if (b < 0) b = map.Pelvis;
                M2Vec3 w;
                if (b < 0) w = new M2Vec3(0f, 0f, 0f);
                else
                {
                    M2Vec3 pv = m.Bones[b].Pivot;
                    if (j == JHeadFwd) pv = new M2Vec3(pv.X + 0.1f, pv.Y, pv.Z);
                    w = Mul(mats, b, pv);
                }
                M2Vec3 f = ToFrame(w, scale);
                into[j * 3] = f.X; into[j * 3 + 1] = f.Y; into[j * 3 + 2] = f.Z;
            }
        }

        private static M2Vec3 J(float[] a, int j) { return new M2Vec3(a[j * 3], a[j * 3 + 1], a[j * 3 + 2]); }
        private static M2Vec3 Sub(M2Vec3 a, M2Vec3 b) { return new M2Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        private static M2Vec3 Add(M2Vec3 a, M2Vec3 b) { return new M2Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        private static M2Vec3 Mul(M2Vec3 a, float k) { return new M2Vec3(a.X * k, a.Y * k, a.Z * k); }
        private static float Dot(M2Vec3 a, M2Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        private static float Len(M2Vec3 a) { return (float)Math.Sqrt(Dot(a, a)); }
        private static M2Vec3 Cross(M2Vec3 a, M2Vec3 b) { return new M2Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        private static M2Vec3 Norm(M2Vec3 a, M2Vec3 fallback) { float l = Len(a); return l > 1e-6f ? Mul(a, 1f / l) : fallback; }
        private static float Deg(double rad) { return (float)(rad * 180.0 / Math.PI); }

        private struct Frame { public M2Vec3 R, U, F; }

        /// <summary>The chest frame (GearBody's rule): up = neck - pelvis, right = the shoulder joints' line made square to up, forward = right x up.</summary>
        private static Frame ChestFrame(float[] j)
        {
            M2Vec3 up = Norm(Sub(J(j, JNeck), J(j, JPelvis)), new M2Vec3(0f, 1f, 0f));
            M2Vec3 across = Sub(J(j, JRShoulder), J(j, JLShoulder));
            M2Vec3 right = Norm(Sub(across, Mul(up, Dot(across, up))), new M2Vec3(1f, 0f, 0f));
            return new Frame { R = right, U = up, F = Cross(right, up) };
        }

        private static float HipsYawOf(float[] j)
        {
            M2Vec3 rv = Sub(J(j, JRHip), J(j, JLHip));
            return Deg(Math.Atan2(-rv.Z, rv.X));
        }

        private static float HipsRollOf(float[] j)
        {
            M2Vec3 rv = Sub(J(j, JRHip), J(j, JLHip));
            return Deg(Math.Atan2(rv.Y, Math.Sqrt(rv.X * rv.X + rv.Z * rv.Z)));
        }

        private static void TorsoAngles(float[] j, float hipsYaw, out float twist, out float bend, out float lean)
        {
            Frame c = ChestFrame(j);
            double y = hipsYaw * Math.PI / 180.0;
            // the pelvis' facing: the model frame turned by the hips yaw about up (AngleAxis(yaw, up) * (1,0,0) = (cos, 0, -sin))
            var pr = new M2Vec3((float)Math.Cos(y), 0f, (float)-Math.Sin(y));
            var pf = new M2Vec3((float)Math.Sin(y), 0f, (float)Math.Cos(y));
            var pu = new M2Vec3(0f, 1f, 0f);
            twist = Deg(Math.Atan2(Dot(c.F, pr), Dot(c.F, pf)));
            bend = Deg(Math.Atan2(Dot(c.U, pf), Dot(c.U, pu)));
            lean = Deg(Math.Atan2(-Dot(c.U, pr), Dot(c.U, pu)));
        }

        private static void HeadAngles(float[] j, out float yaw, out float pitch)
        {
            Frame c = ChestFrame(j);
            M2Vec3 d = Sub(J(j, JHeadFwd), J(j, JHead));
            float x = Dot(d, c.R), y = Dot(d, c.U), z = Dot(d, c.F);
            yaw = Deg(Math.Atan2(x, z));
            pitch = Deg(Math.Atan2(y, Math.Sqrt(x * x + z * z)));
        }

        private static float ToePitchOf(float[] j, int ankle, int toe, bool haveToe)
        {
            if (!haveToe) return 0f;
            M2Vec3 d = Sub(J(j, toe), J(j, ankle));
            return Deg(Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)));
        }

        /// <summary>The reference of a model: Stand frame 0 joints and bind joints (same frame and units as every later sample).</summary>
        public static M2HumanoidRef Reference(M2HumanoidMap map, float[] stand, float[] bind)
        {
            var r = new M2HumanoidRef();
            if (map == null || !map.Valid || stand == null || bind == null) return r;
            float legL = map.Legs ? 0.5f * (Len(Sub(J(bind, JLKnee), J(bind, JLHip))) + Len(Sub(J(bind, JLAnkle), J(bind, JLKnee))) +
                                            Len(Sub(J(bind, JRKnee), J(bind, JRHip))) + Len(Sub(J(bind, JRAnkle), J(bind, JRKnee)))) : 0f;
            float armL = 0.5f * (Len(Sub(J(bind, JLElbow), J(bind, JLShoulder))) + Len(Sub(J(bind, JLHand), J(bind, JLElbow))) +
                                 Len(Sub(J(bind, JRElbow), J(bind, JRShoulder))) + Len(Sub(J(bind, JRHand), J(bind, JRElbow))));
            r.ArmLength = armL;
            r.LegLength = legL > 1e-4f ? legL : Math.Max(1e-3f, J(bind, JPelvis).Y);
            r.Pelvis = J(stand, JPelvis);
            r.AnkleY = map.Legs ? 0.5f * (J(stand, JLAnkle).Y + J(stand, JRAnkle).Y) : 0f;
            r.HipsYaw = map.Legs ? HipsYawOf(stand) : 0f;
            r.HipsRoll = map.Legs ? HipsRollOf(stand) : 0f;
            TorsoAngles(stand, r.HipsYaw, out r.Twist, out r.Bend, out r.Lean);
            HeadAngles(stand, out r.HeadYaw, out r.HeadPitch);
            r.LToePitch = ToePitchOf(stand, JLAnkle, JLToe, map.LToe >= 0);
            r.RToePitch = ToePitchOf(stand, JRAnkle, JRToe, map.RToe >= 0);
            r.Valid = armL > 1e-4f;
            return r;
        }

        /// <summary>The body sample of one pose (joints in the reference's frame and units). Grips are left identity (GearPuppet).</summary>
        public static WowBodySample Sample(M2HumanoidMap map, float[] j, in M2HumanoidRef rf)
        {
            var s = new WowBodySample { RGrip = M2Quat.Identity, LGrip = M2Quat.Identity };
            if (map == null || !map.Valid || !rf.Valid || j == null) return s;
            float leg = rf.LegLength, arm = rf.ArmLength;
            M2Vec3 pelvis = J(j, JPelvis);
            s.HipsOffset = Mul(Sub(pelvis, rf.Pelvis), 1f / leg);
            float hipsYaw = 0f;
            if (map.Legs)
            {
                hipsYaw = HipsYawOf(j);
                s.HipsYaw = DeltaAngle(rf.HipsYaw, hipsYaw);
                s.HipsRoll = DeltaAngle(rf.HipsRoll, HipsRollOf(j));
                M2Vec3 la = J(j, JLAnkle), ra = J(j, JRAnkle);
                s.LFoot = new M2Vec3(la.X / leg, (la.Y - rf.AnkleY) / leg, la.Z / leg);
                s.RFoot = new M2Vec3(ra.X / leg, (ra.Y - rf.AnkleY) / leg, ra.Z / leg);
                s.LKneePole = Pole(J(j, JLHip), J(j, JLKnee), la, new M2Vec3(0f, 0f, 1f));
                s.RKneePole = Pole(J(j, JRHip), J(j, JRKnee), ra, new M2Vec3(0f, 0f, 1f));
                s.LToePitch = map.LToe >= 0 ? ToePitchOf(j, JLAnkle, JLToe, true) - rf.LToePitch : 0f;
                s.RToePitch = map.RToe >= 0 ? ToePitchOf(j, JRAnkle, JRToe, true) - rf.RToePitch : 0f;
            }
            float tw, be, le;
            TorsoAngles(j, hipsYaw, out tw, out be, out le);
            s.Twist = DeltaAngle(rf.Twist, tw);
            s.Bend = DeltaAngle(rf.Bend, be);
            s.Lean = DeltaAngle(rf.Lean, le);
            float hy, hp;
            HeadAngles(j, out hy, out hp);
            s.HeadYaw = DeltaAngle(rf.HeadYaw, hy);
            s.HeadPitch = DeltaAngle(rf.HeadPitch, hp);
            Frame c = ChestFrame(j);
            M2Vec3 mid = Mul(Add(J(j, JLShoulder), J(j, JRShoulder)), 0.5f);
            s.RHand = InFrame(Sub(J(j, JRHand), mid), c, 1f / arm);
            s.LHand = InFrame(Sub(J(j, JLHand), mid), c, 1f / arm);
            s.RElbowPole = InFrame(Pole(J(j, JRShoulder), J(j, JRElbow), J(j, JRHand), Mul(c.R, 1f)), c, 1f);
            s.LElbowPole = InFrame(Pole(J(j, JLShoulder), J(j, JLElbow), J(j, JLHand), Mul(c.R, -1f)), c, 1f);
            s.Valid = true;
            return s;
        }

        private static M2Vec3 InFrame(M2Vec3 v, Frame c, float k) { return new M2Vec3(Dot(v, c.R) * k, Dot(v, c.U) * k, Dot(v, c.F) * k); }

        /// <summary>The unit direction from the a -> c line to b (the knee / elbow side), fallback when b is on the line.</summary>
        private static M2Vec3 Pole(M2Vec3 a, M2Vec3 b, M2Vec3 c, M2Vec3 fallback)
        {
            M2Vec3 d = Sub(c, a);
            float dd = Dot(d, d);
            if (dd < 1e-8f) return fallback;
            float t = Dot(Sub(b, a), d) / dd;
            return Norm(Sub(b, Add(a, Mul(d, t))), fallback);
        }

        private static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }
    }
}
