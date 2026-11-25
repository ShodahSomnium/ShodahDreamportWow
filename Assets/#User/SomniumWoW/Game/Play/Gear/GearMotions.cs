using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>One key of an arm track: the hand's place and (optionally) its grip orientation, in the body frame.</summary>
    public struct GearKey
    {
        public float T;                 // 0..1 of the motion
        public Vector3 Pos;             // hand, in arm lengths from the shoulder midpoint: x right, y up, z forward
        public bool Orient;
        public Quaternion Rot;          // grip frame in the body frame: forward = blade out of the top of the fist, up = -knuckles
    }

    /// <summary>One key of the torso: twist about the body's up axis (+ = turn right) and bend about its right axis (+ = forward), degrees.</summary>
    public struct GearSpineKey { public float T, Twist, Bend; }

    public sealed class GearArmTrack
    {
        public GearKey[] Keys;
        public bool Orient;             // every key carries the grip orientation
        public float Grip;              // left arm only, != 0: holds the right hand's weapon this many arm lengths along its blade (Keys unused)
    }

    public sealed class GearMotionDef
    {
        public WowMotion Kind;
        public float Duration, In, Out; // seconds; blend in/out as fractions of the duration
        public GearArmTrack Right, Left;
        public GearSpineKey[] Spine;
    }

    /// <summary>What one motion or hold asks of the body at one moment (body frame; see GearKey).</summary>
    public struct GearSample
    {
        public bool R, L, RRot, LRot, Spine;
        public Vector3 RPos, LPos;
        public Quaternion RQ, LQ;
        public float Twist, Bend;
    }

    /// <summary>
    /// Several samples blended by weight (later ones win where they drive the same part): an arm's target is the normalized
    /// weighted blend, its weight 1 - (1 - a)(1 - b). The poser then blends the client's animation toward it by that weight.
    /// </summary>
    public struct GearMix
    {
        public Vector3 RPos, LPos;
        public Quaternion RQ, LQ;
        public float RW, LW, RQW, LQW, Twist, Bend, SW;

        public bool Any { get { return RW > 0.001f || LW > 0.001f || SW > 0.001f; } }

        public void Add(in GearSample s, float w)
        {
            if (w <= 0f) return;
            if (w > 1f) w = 1f;
            if (s.R) { RPos = Vector3.Lerp(RPos, s.RPos, K(RW, w)); RW = 1f - (1f - RW) * (1f - w); }
            if (s.L) { LPos = Vector3.Lerp(LPos, s.LPos, K(LW, w)); LW = 1f - (1f - LW) * (1f - w); }
            if (s.RRot) { RQ = RQW > 0f ? Quaternion.Slerp(RQ, s.RQ, K(RQW, w)) : s.RQ; RQW = 1f - (1f - RQW) * (1f - w); }
            if (s.LRot) { LQ = LQW > 0f ? Quaternion.Slerp(LQ, s.LQ, K(LQW, w)) : s.LQ; LQW = 1f - (1f - LQW) * (1f - w); }
            if (s.Spine)
            {
                float k = K(SW, w);
                Twist = Mathf.Lerp(Twist, s.Twist, k);
                Bend = Mathf.Lerp(Bend, s.Bend, k);
                SW = 1f - (1f - SW) * (1f - w);
            }
        }

        private static float K(float had, float w) { float d = had * (1f - w) + w; return d > 1e-5f ? w / d : 1f; }
    }

    /// <summary>
    /// The desktop avatar's combat motions (our own procedural poses, nothing from the game): one-shots (WowMotion) and
    /// holds (WowHold) as hand targets for a two-bone arm IK, grip orientations for the weapon hands, and torso twist/bend.
    /// Body frame: x = the avatar's right, y = up, z = forward; positions in arm lengths from the shoulder midpoint (the
    /// right shoulder sits near (+0.3, 0, 0), a hanging hand near (+0.32, -0.95, 0.1)). Orientation: blade = where a held
    /// weapon points out of the top of the fist (little -> index knuckle), knuckle = wrist -> knuckles (GearBody.HandPose's
    /// frame, which GearPlace.Held puts the weapons in, so a posed hand carries its weapon the same way).
    /// </summary>
    public static class GearMotions
    {
        private static readonly Dictionary<int, GearMotionDef> Defs = new Dictionary<int, GearMotionDef>();

        public static GearMotionDef Get(WowMotion kind)
        {
            if (Defs.Count == 0) Build();
            GearMotionDef d;
            return Defs.TryGetValue((int)kind, out d) ? d : null;
        }

        /// <summary>Weight of a one-shot at t (0..1): smooth in over In, smooth out over Out.</summary>
        public static float Envelope(GearMotionDef d, float t)
        {
            if (t <= 0f || t >= 1f) return t <= 0f && d.In <= 0f ? 1f : 0f;
            float a = d.In > 0f ? Mathf.SmoothStep(0f, 1f, t / d.In) : 1f;
            float b = d.Out > 0f ? Mathf.SmoothStep(0f, 1f, (1f - t) / d.Out) : 1f;
            return Mathf.Min(a, b);
        }

        public static void Sample(GearMotionDef d, float t, out GearSample s)
        {
            s = default(GearSample);
            t = Mathf.Clamp01(t);
            if (d.Right != null && d.Right.Keys != null)
            {
                s.R = true;
                Eval(d.Right.Keys, t, out s.RPos, out s.RQ);
                s.RRot = d.Right.Orient;
            }
            if (d.Left != null)
            {
                if (d.Left.Grip != 0f && s.R && s.RRot)
                {
                    s.L = true;
                    s.LPos = GripPoint(s.RPos, s.RQ, d.Left.Grip);
                    s.LQ = s.RQ;
                    s.LRot = true;
                }
                else if (d.Left.Keys != null)
                {
                    s.L = true;
                    Eval(d.Left.Keys, t, out s.LPos, out s.LQ);
                    s.LRot = d.Left.Orient;
                }
            }
            if (d.Spine != null && d.Spine.Length > 0)
            {
                s.Spine = true;
                EvalSpine(d.Spine, t, out s.Twist, out s.Bend);
            }
        }

        /// <summary>A hold's pose at time (seconds, for the small idle movements).</summary>
        public static void Hold(WowHold h, float time, out GearSample s)
        {
            s = default(GearSample);
            switch (h)
            {
                case WowHold.Cast:
                {
                    // Hands gathered in front of the chest, palms facing, circling a little (gathering the spell).
                    float a = time * 7.5f;
                    Vector3 swirl = new Vector3(0f, Mathf.Sin(a) * 0.035f, Mathf.Cos(a) * 0.035f);
                    s.R = s.L = s.RRot = s.LRot = s.Spine = true;
                    s.RPos = new Vector3(0.16f, -0.25f, 0.45f) + swirl;
                    s.LPos = new Vector3(-0.16f, -0.25f, 0.45f) - swirl;
                    s.RQ = Q(0f, 1f, 0f, -0.3f, 0f, 0.95f);
                    s.LQ = Q(0f, 1f, 0f, 0.3f, 0f, 0.95f);
                    s.Bend = 5f;
                    break;
                }
                case WowHold.Channel:
                {
                    // Both arms held out toward the target, palms forward, trembling with the effort.
                    Vector3 shake = new Vector3(Mathf.Sin(time * 31f) * 0.012f, Mathf.Sin(time * 23f + 1f) * 0.012f, 0f);
                    s.R = s.L = s.RRot = s.LRot = s.Spine = true;
                    s.RPos = new Vector3(0.2f, -0.05f, 0.85f) + shake;
                    s.LPos = new Vector3(-0.2f, -0.05f, 0.85f) - shake;
                    s.RQ = Q(-1f, 0f, 0f, 0f, 1f, 0f);
                    s.LQ = Q(1f, 0f, 0f, 0f, 1f, 0f);
                    s.Bend = 4f;
                    break;
                }
                case WowHold.AimBow:
                {
                    // Bow arm straight at the target at shoulder height (bow upright in the fist), string hand at the jaw,
                    // torso turned so the bow shoulder leads.
                    s.R = s.L = s.RRot = s.LRot = s.Spine = true;
                    s.LPos = new Vector3(-0.2f, 0.15f, 0.95f);
                    s.LQ = Q(0f, 1f, 0f, 0f, 0f, 1f);
                    s.RPos = new Vector3(0.08f, 0.25f, 0.15f);
                    s.RQ = Q(0f, 1f, 0f, 0f, 0f, 1f);
                    s.Twist = 25f;
                    break;
                }
                case WowHold.AimGun:
                {
                    // Gun at the right cheek pointing at the target, the left hand under the barrel.
                    s.R = s.L = s.RRot = s.LRot = s.Spine = true;
                    s.RPos = new Vector3(0.2f, 0.05f, 0.35f);
                    s.RQ = Q(0f, 0f, 1f, -1f, 0f, 0f);
                    s.LPos = GripPoint(s.RPos, s.RQ, 0.4f);
                    s.LQ = s.RQ;
                    s.Twist = 15f;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ evaluation

        private static Vector3 GripPoint(Vector3 rightPos, Quaternion rightRot, float along)
        {
            return rightPos + rightRot * new Vector3(0f, -0.04f, along);
        }

        private static void Eval(GearKey[] k, float t, out Vector3 pos, out Quaternion rot)
        {
            if (k.Length == 1) { pos = k[0].Pos; rot = k[0].Rot; return; }
            int i = 0;
            while (i < k.Length - 2 && t > k[i + 1].T) i++;
            float span = k[i + 1].T - k[i].T;
            float u = span > 1e-5f ? Mathf.Clamp01((t - k[i].T) / span) : 1f;
            Vector3 p0 = k[i > 0 ? i - 1 : i].Pos, p1 = k[i].Pos, p2 = k[i + 1].Pos, p3 = k[i + 2 < k.Length ? i + 2 : i + 1].Pos;
            pos = CatmullRom(p0, p1, p2, p3, u);
            rot = Quaternion.Slerp(k[i].Rot, k[i + 1].Rot, u);
        }

        private static void EvalSpine(GearSpineKey[] k, float t, out float twist, out float bend)
        {
            if (k.Length == 1) { twist = k[0].Twist; bend = k[0].Bend; return; }
            int i = 0;
            while (i < k.Length - 2 && t > k[i + 1].T) i++;
            float span = k[i + 1].T - k[i].T;
            float u = span > 1e-5f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - k[i].T) / span)) : 1f;
            twist = Mathf.Lerp(k[i].Twist, k[i + 1].Twist, u);
            bend = Mathf.Lerp(k[i].Bend, k[i + 1].Bend, u);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        /// <summary>Grip rotation from a blade direction and a knuckle direction (made perpendicular to the blade).</summary>
        public static Quaternion Q(float bx, float by, float bz, float kx, float ky, float kz)
        {
            Vector3 b = new Vector3(bx, by, bz).normalized;
            Vector3 k = new Vector3(kx, ky, kz);
            k -= Vector3.Dot(k, b) * b;
            if (k.sqrMagnitude < 1e-6f) k = Mathf.Abs(b.y) < 0.9f ? Vector3.up : Vector3.forward;
            return Quaternion.LookRotation(b, -k.normalized);
        }

        // ------------------------------------------------------------------------------------------------ the motions

        private static GearKey P(float t, float x, float y, float z) { return new GearKey { T = t, Pos = new Vector3(x, y, z) }; }

        private static GearKey O(float t, float x, float y, float z, float bx, float by, float bz, float kx, float ky, float kz)
        {
            return new GearKey { T = t, Pos = new Vector3(x, y, z), Orient = true, Rot = Q(bx, by, bz, kx, ky, kz) };
        }

        private static GearSpineKey S(float t, float twist, float bend) { return new GearSpineKey { T = t, Twist = twist, Bend = bend }; }

        private static GearArmTrack Arm(params GearKey[] keys)
        {
            bool orient = true;
            for (int i = 0; i < keys.Length; i++) orient &= keys[i].Orient;
            return new GearArmTrack { Keys = keys, Orient = orient };
        }

        private static void Add(WowMotion kind, float duration, float fadeIn, float fadeOut, GearArmTrack right, GearArmTrack left, params GearSpineKey[] spine)
        {
            Defs[(int)kind] = new GearMotionDef { Kind = kind, Duration = duration, In = fadeIn, Out = fadeOut, Right = right, Left = left, Spine = spine };
        }

        /// <summary>The same motion done by the other arm: left and right swapped, x mirrored, twist reversed.</summary>
        private static void AddMirror(WowMotion kind, WowMotion of)
        {
            GearMotionDef d = Defs[(int)of];
            Defs[(int)kind] = new GearMotionDef
            {
                Kind = kind, Duration = d.Duration, In = d.In, Out = d.Out,
                Right = MirrorTrack(d.Left), Left = MirrorTrack(d.Right), Spine = MirrorSpine(d.Spine)
            };
        }

        private static GearArmTrack MirrorTrack(GearArmTrack t)
        {
            if (t == null || t.Keys == null) return null;
            var keys = new GearKey[t.Keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                GearKey k = t.Keys[i];
                k.Pos.x = -k.Pos.x;
                k.Rot = new Quaternion(k.Rot.x, -k.Rot.y, -k.Rot.z, k.Rot.w); // reflection through the body's YZ plane
                keys[i] = k;
            }
            return new GearArmTrack { Keys = keys, Orient = t.Orient };
        }

        private static GearSpineKey[] MirrorSpine(GearSpineKey[] s)
        {
            if (s == null) return null;
            var r = new GearSpineKey[s.Length];
            for (int i = 0; i < s.Length; i++) r[i] = new GearSpineKey { T = s[i].T, Twist = -s[i].Twist, Bend = s[i].Bend };
            return r;
        }

        private static void Build()
        {
            // One-hand slash: from the ready position up by the right ear (blade back over the shoulder, edge forward),
            // diagonally down across the body to the left hip.
            Add(WowMotion.Slash1H, 0.6f, 0.12f, 0.3f,
                Arm(O(0f, 0.34f, -0.8f, 0.25f, 0f, 0.4f, 1f, 0f, -1f, 0.4f),
                    O(0.3f, 0.45f, 0.28f, -0.05f, 0.2f, 0.55f, -0.8f, 0f, 0.6f, 0.8f),
                    O(0.45f, 0.12f, 0.02f, 0.72f, -0.35f, 0.45f, 0.8f, -0.5f, -0.6f, 0.6f),
                    O(0.55f, -0.1f, -0.25f, 0.85f, -0.65f, -0.3f, 0.7f, -0.6f, -0.8f, 0f),
                    O(0.72f, -0.4f, -0.6f, 0.45f, -0.7f, -0.6f, -0.2f, -0.2f, -0.4f, -0.9f),
                    O(1f, 0.34f, -0.8f, 0.25f, 0f, 0.4f, 1f, 0f, -1f, 0.4f)),
                null,
                S(0f, 0f, 0f), S(0.3f, 20f, -5f), S(0.55f, -20f, 10f), S(0.75f, -25f, 8f), S(1f, 0f, 0f));
            AddMirror(WowMotion.Slash1HOff, WowMotion.Slash1H);

            // Two-hand overhead chop: both hands on the hilt, raised over the head (blade hanging back), brought down in front.
            Add(WowMotion.Swing2H, 0.8f, 0.1f, 0.3f,
                Arm(O(0f, 0.22f, -0.55f, 0.4f, 0f, 0.5f, 0.85f, 0f, 0.85f, -0.5f),
                    O(0.35f, 0.1f, 0.55f, -0.05f, 0f, 0.35f, -0.95f, 0f, 0.95f, 0.35f),
                    O(0.46f, 0.08f, 0.4f, 0.45f, 0f, 0.95f, 0.3f, 0f, -0.3f, 0.95f),
                    O(0.56f, 0.05f, -0.15f, 0.8f, 0f, -0.35f, 0.95f, 0f, -0.95f, -0.35f),
                    O(0.7f, 0.05f, -0.55f, 0.55f, 0f, -0.85f, 0.5f, 0f, -0.5f, -0.85f),
                    O(1f, 0.22f, -0.55f, 0.4f, 0f, 0.5f, 0.85f, 0f, 0.85f, -0.5f)),
                new GearArmTrack { Grip = -0.2f },
                S(0f, 0f, 0f), S(0.35f, 5f, -10f), S(0.56f, 0f, 20f), S(0.7f, 0f, 22f), S(1f, 0f, 0f));

            // Two-hand side sweep: wound up on the right, blade flat, swept across the front to the left.
            Add(WowMotion.Swing2HSide, 0.8f, 0.1f, 0.3f,
                Arm(O(0f, 0.22f, -0.55f, 0.4f, 0f, 0.5f, 0.85f, 0f, 0.85f, -0.5f),
                    O(0.3f, 0.6f, 0.05f, -0.05f, 0.4f, 0.15f, -0.9f, 0.1f, 0f, 1f),
                    O(0.45f, 0.35f, -0.05f, 0.6f, 0.6f, 0.1f, 0.8f, -0.8f, 0f, 0.6f),
                    O(0.58f, -0.2f, -0.1f, 0.8f, -0.5f, 0f, 0.85f, -0.85f, 0f, -0.5f),
                    O(0.72f, -0.55f, -0.2f, 0.45f, -0.95f, -0.1f, -0.2f, -0.2f, 0f, -0.98f),
                    O(1f, 0.22f, -0.55f, 0.4f, 0f, 0.5f, 0.85f, 0f, 0.85f, -0.5f)),
                new GearArmTrack { Grip = -0.2f },
                S(0f, 0f, 0f), S(0.3f, 35f, 0f), S(0.58f, -15f, 5f), S(0.72f, -35f, 5f), S(1f, 0f, 0f));

            // Dagger stab: pulled back at the hip, thrust straight at the target.
            Add(WowMotion.Stab, 0.5f, 0.1f, 0.3f,
                Arm(O(0f, 0.34f, -0.8f, 0.2f, 0f, 0.2f, 1f, 0f, 1f, 0f),
                    O(0.3f, 0.38f, -0.45f, -0.05f, 0f, 0.1f, 1f, 0f, 1f, 0f),
                    O(0.5f, 0.12f, -0.2f, 0.95f, -0.1f, 0.05f, 1f, 0f, 1f, 0f),
                    O(0.65f, 0.15f, -0.25f, 0.85f, -0.1f, 0.05f, 1f, 0f, 1f, 0f),
                    O(1f, 0.34f, -0.8f, 0.2f, 0f, 0.2f, 1f, 0f, 1f, 0f)),
                null,
                S(0f, 0f, 0f), S(0.3f, 12f, 0f), S(0.5f, -18f, 8f), S(1f, 0f, 0f));
            AddMirror(WowMotion.StabOff, WowMotion.Stab);

            // Unarmed punch (and claws in cat/bear form): cocked by the chest, a straight jab, knuckles forward, palm down.
            Add(WowMotion.Punch, 0.4f, 0.1f, 0.35f,
                Arm(O(0f, 0.34f, -0.7f, 0.25f, -1f, 0f, 0f, 0f, -0.3f, 1f),
                    O(0.3f, 0.3f, -0.3f, 0.1f, -1f, 0f, 0f, 0f, 0f, 1f),
                    O(0.5f, 0.08f, -0.05f, 0.95f, -1f, 0f, 0f, 0f, 0f, 1f),
                    O(0.65f, 0.1f, -0.08f, 0.85f, -1f, 0f, 0f, 0f, 0f, 1f),
                    O(1f, 0.34f, -0.7f, 0.25f, -1f, 0f, 0f, 0f, -0.3f, 1f)),
                null,
                S(0f, 0f, 0f), S(0.3f, 8f, 0f), S(0.5f, -22f, 5f), S(1f, 0f, 0f));
            AddMirror(WowMotion.PunchOff, WowMotion.Punch);

            // Shield bash: the shield forearm drawn in, then driven forward with the left shoulder.
            Add(WowMotion.ShieldBash, 0.55f, 0.1f, 0.3f,
                null,
                Arm(P(0f, -0.34f, -0.7f, 0.25f), P(0.3f, -0.4f, -0.35f, 0.15f), P(0.5f, -0.15f, -0.05f, 0.85f),
                    P(0.65f, -0.15f, -0.07f, 0.8f), P(1f, -0.34f, -0.7f, 0.25f)),
                S(0f, 0f, 0f), S(0.3f, -10f, 0f), S(0.5f, 22f, 8f), S(1f, 0f, 0f));

            // Bow: the string hand lets go at the jaw (the arrow flies with the Auto Shot event), reaches back over the right
            // shoulder to the quiver, nocks at the bow and draws to the jaw again. The bow arm is the AimBow hold's.
            Add(WowMotion.BowShot, 1.1f, 0.05f, 0.1f,
                Arm(P(0f, 0.08f, 0.25f, 0.15f), P(0.08f, 0.22f, 0.28f, -0.08f), P(0.35f, 0.32f, 0.45f, -0.25f),
                    P(0.6f, -0.12f, 0.18f, 0.75f), P(0.9f, 0.08f, 0.25f, 0.15f), P(1f, 0.08f, 0.25f, 0.15f)),
                null);

            // Gun / crossbow: the recoil kicks the gun up and back, the left hand stays under the barrel.
            Add(WowMotion.GunShot, 0.45f, 0.05f, 0.2f,
                Arm(O(0f, 0.2f, 0.05f, 0.35f, 0f, 0f, 1f, -1f, 0f, 0f),
                    O(0.12f, 0.23f, 0.1f, 0.28f, 0f, 0.35f, 0.94f, -1f, 0f, 0f),
                    O(0.5f, 0.2f, 0.05f, 0.35f, 0f, 0f, 1f, -1f, 0f, 0f),
                    O(1f, 0.2f, 0.05f, 0.35f, 0f, 0f, 1f, -1f, 0f, 0f)),
                new GearArmTrack { Grip = 0.4f },
                S(0f, 15f, 0f), S(0.12f, 15f, -4f), S(1f, 15f, 0f));

            // Thrown weapon: an overhand throw.
            Add(WowMotion.Throw, 0.65f, 0.1f, 0.3f,
                Arm(P(0f, 0.34f, -0.8f, 0.2f), P(0.35f, 0.45f, 0.35f, -0.3f), P(0.5f, 0.2f, 0.3f, 0.85f),
                    P(0.7f, 0.05f, -0.35f, 0.7f), P(1f, 0.34f, -0.8f, 0.2f)),
                null,
                S(0f, 0f, 0f), S(0.35f, 25f, -8f), S(0.5f, -15f, 8f), S(0.7f, -20f, 12f), S(1f, 0f, 0f));

            // Wand: raised tip-up, flicked at the target.
            Add(WowMotion.WandShot, 0.5f, 0.1f, 0.3f,
                Arm(O(0f, 0.34f, -0.8f, 0.2f, 0f, 0.4f, 1f, 0f, 1f, -0.4f),
                    O(0.3f, 0.3f, 0.05f, 0.75f, 0f, 0.5f, 0.85f, 0f, 0.85f, -0.5f),
                    O(0.45f, 0.22f, 0f, 0.92f, 0f, -0.1f, 1f, 0f, 1f, 0.1f),
                    O(0.65f, 0.24f, -0.02f, 0.9f, 0f, -0.1f, 1f, 0f, 1f, 0.1f),
                    O(1f, 0.34f, -0.8f, 0.2f, 0f, 0.4f, 1f, 0f, 1f, -0.4f)),
                null,
                S(0f, 0f, 0f), S(0.45f, -8f, 4f), S(1f, 0f, 0f));

            // Spell release after a cast: from the gathered hands (the Cast hold) both palms push out at the target.
            Add(WowMotion.CastRelease, 0.6f, 0f, 0.35f,
                Arm(O(0f, 0.16f, -0.25f, 0.45f, 0f, 1f, 0f, -0.3f, 0f, 0.95f),
                    O(0.3f, 0.25f, -0.1f, 0.92f, -1f, 0f, 0f, 0f, 1f, 0f),
                    O(0.5f, 0.26f, -0.06f, 0.9f, -1f, 0f, 0f, 0f, 1f, 0f),
                    O(1f, 0.3f, -0.6f, 0.35f, 0f, 1f, 0.2f, 0f, -0.2f, 1f)),
                Arm(O(0f, -0.16f, -0.25f, 0.45f, 0f, 1f, 0f, 0.3f, 0f, 0.95f),
                    O(0.3f, -0.25f, -0.1f, 0.92f, 1f, 0f, 0f, 0f, 1f, 0f),
                    O(0.5f, -0.26f, -0.06f, 0.9f, 1f, 0f, 0f, 0f, 1f, 0f),
                    O(1f, -0.3f, -0.6f, 0.35f, 0f, 1f, 0.2f, 0f, -0.2f, 1f)),
                S(0f, 0f, 5f), S(0.3f, 0f, 9f), S(1f, 0f, 0f));

            // Instant spell at a target: the right hand cocked by the shoulder, palm thrown forward.
            Add(WowMotion.CastInstant, 0.5f, 0.1f, 0.35f,
                Arm(O(0f, 0.34f, -0.7f, 0.25f, 0f, 1f, 0.3f, 0f, -0.3f, 1f),
                    O(0.3f, 0.35f, -0.05f, 0.3f, -1f, 0f, 0f, 0f, 1f, 0f),
                    O(0.5f, 0.2f, 0.02f, 0.92f, -1f, 0f, 0f, 0f, 0.6f, 0.8f),
                    O(0.65f, 0.21f, 0f, 0.9f, -1f, 0f, 0f, 0f, 0.6f, 0.8f),
                    O(1f, 0.34f, -0.7f, 0.25f, 0f, 1f, 0.3f, 0f, -0.3f, 1f)),
                null,
                S(0f, 0f, 0f), S(0.3f, 8f, 0f), S(0.5f, -15f, 5f), S(1f, 0f, 0f));

            // A spell on yourself or a friend (buffs, heals, racials): both arms raised up and out, then lowered.
            Add(WowMotion.CastOmni, 0.7f, 0.1f, 0.35f,
                Arm(P(0f, 0.3f, -0.5f, 0.35f), P(0.4f, 0.45f, 0.45f, 0.35f), P(0.6f, 0.46f, 0.42f, 0.33f), P(1f, 0.34f, -0.8f, 0.2f)),
                Arm(P(0f, -0.3f, -0.5f, 0.35f), P(0.4f, -0.45f, 0.45f, 0.35f), P(0.6f, -0.46f, 0.42f, 0.33f), P(1f, -0.34f, -0.8f, 0.2f)),
                S(0f, 0f, 0f), S(0.4f, 0f, -6f), S(1f, 0f, 0f));
        }
    }
}
