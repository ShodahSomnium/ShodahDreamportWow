using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>Iteration 11 (avatar): the WoW full-body pose a poser may apply this frame (GearPuppet's sample).</summary>
    public struct GearWowPose
    {
        public bool Enabled;              // the WoW style is chosen and a puppet exists: the simple motions are off
        public bool On;                   // WoW does something now (a loop, a one-shot): blended in 0.2 s, out 0.3 s
        public WowBodySample S;
        public bool ForcedLegs;           // motion hook: a forced move (charge, knockback) - the legs follow WoW even while moving
        public bool ArmsOnly;             // states fix 2026-10-04: the body is in a client pose (lying, sitting, kneeling): only WoW's arms (eating)
        public bool Swim;                 // lead 2026-10-26: a swim loop (41-45) - the body lies in the water at the shoulders' height
        public bool LegsOnly;             // lead 2026-10-26: a VR swimmer - only WoW's kicking legs, straightened to the upright tracked body
    }

    /// <summary>
    /// Plays GearMotions on one desktop (non-XR) Somnium avatar by posing its humanoid bones (Animator.GetBoneTransform:
    /// spine, chest, upper chest, head, upper/lower arms, hands) in LateUpdate, after the client animated it (WowGear runs at
    /// execution order 10000: after the Animator and its OnAnimatorIK foot IK, VRIK 9998 and the seat FullBodyBipedIK 9999).
    /// The client's animation is the base: every frame the poser blends from what the Animator wrote toward the motion by
    /// the motion's weight (arms: two-bone IK to the hand target + the grip orientation; torso: twist/bend spread over the
    /// spine bones, the head kept looking where it looked). Nothing is kept: the Animator writes the bones again next
    /// frame. If it did not (a culled Animator), the poser puts back what it found before writing, so poses never pile up.
    /// With no motion and no hold the bones are not touched at all. Never used on a VR avatar (the caller checks).
    /// Iteration 11 (avatar, proposal-avatar.md 6.C): the WoW style - a WowBodySample of the real WoW animation (GearPuppet) drives
    /// the hips (the only position written), both legs (two-bone IK to WoW's feet, ground-clamped, knees by WoW's poles, toes by
    /// WoW's pitch), the torso (WoW's twist / bend / lean relative to the hips), the head (30 % of WoW's turn: the client's mouse
    /// look keeps the rest; ours) and both arms (WoW's hands, elbow poles and grips). Legs / hips only while the avatar stands
    /// (planar speed &lt; 0.25 m/s full, > 0.6 m/s none, airborne none; ours) - walking keeps the client's own legs.
    /// </summary>
    public sealed class GearPoser
    {
        private const int Spine = 0, Chest = 1, UpperChest = 2, Head = 3, LUpper = 4, LLower = 5, LHand = 6, RUpper = 7, RLower = 8, RHand = 9;
        private const int Hips = 10, Neck = 11, LThigh = 12, LShin = 13, LFoot = 14, LToes = 15, RThigh = 16, RShin = 17, RFoot = 18, RToes = 19, Count = 20;
        private static readonly HumanBodyBones[] Ids =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.Hips, HumanBodyBones.Neck,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
        };

        // ours (logged once): the WoW style's blend times, the stillness rule, the head share
        public const float WowIn = 0.2f, WowOut = 0.3f, StillFull = 0.25f, StillNone = 0.6f, HeadShare = 0.3f;
        public const float MaxHipsYaw = 45f;   // ours: the pelvis turn applied (DraeneiMale Ready2H samples 78 deg, AvPoseSample) - the feet still land where WoW puts them
        private static bool _wowSaid;

        private sealed class Bone { public Transform T; public Quaternion Base, Written; public bool Wrote; }

        private struct Shot { public GearMotionDef Def; public float Start; }

        public string Label = "player";

        private readonly Bone[] _b = new Bone[Count];
        private Transform _rIndex, _rLittle, _rMiddle, _lIndex, _lLittle, _lMiddle;
        private Animator _anim;
        private Avatar _boundAvatar;   // fix 2026-10-04 (avatar change): the Animator.avatar the bones were bound from
        private float _rebindSaidAt = -100f;
        private int _rebindQuiet;
        private const float RebindSayEvery = 5f;   // ours
        private readonly List<Shot> _shots = new List<Shot>(4);
        private WowHold _hold, _holdShown;
        private float _holdW;
        private int _played;
        // iteration 11: the hips position (the only position written) and the WoW style's state
        private Vector3 _hipsBase, _hipsWritten;
        private bool _hipsWrote;
        private float _wowW, _ankleH = -1f, _legLen = -1f;
        private Vector3 _lastRoot, _rootVel;
        private bool _haveRoot, _legs;
        private WowBodySample _last;

        /// <summary>True when the bones were posed this frame (the weapons then follow the posed hands, no extra chop).</summary>
        public bool Posed { get; private set; }

        /// <summary>Iteration 11: how much of the WoW style is applied now (0..1; the arms' weight).</summary>
        public float WowWeight { get { return _wowW; } }

        public GearPoser()
        {
            for (int i = 0; i < Count; i++) _b[i] = new Bone();
        }

        /// <summary>Starts a one-shot (a newer one replaces an older one that drives the same arms).</summary>
        public void Play(WowMotion kind, float now)
        {
            GearMotionDef d = GearMotions.Get(kind);
            if (d == null) return;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                GearMotionDef o = _shots[i].Def;
                bool sameArms = (o.Right != null) == (d.Right != null) && (o.Left != null) == (d.Left != null);
                if (sameArms || o.Kind == kind) _shots.RemoveAt(i);
            }
            if (_shots.Count >= 3) _shots.RemoveAt(0);
            _shots.Add(new Shot { Def = d, Start = now });
            _played++;
            if (_played <= 12 || _played % 200 == 0) WowLog.Info("Gear: avatar of " + Label + " plays " + kind + " (motion " + _played + ")");
        }

        public void SetHold(WowHold h) { _hold = h; }

        /// <summary>Drops every motion and hold (the bones go back to the client's animation from the next frame).</summary>
        public void Stop()
        {
            _shots.Clear();
            _hold = WowHold.None;
            _holdW = 0f;
            _wowW = 0f;
        }

        private int _begun = -1;

        /// <summary>
        /// Once per frame, before anything reads the body (GearBody.Update): what the Animator wrote this frame becomes the
        /// base; a bone it left alone (a culled Animator) still carries our last pose, so that is put back first.
        /// </summary>
        public void Begin()
        {
            if (_begun == Time.frameCount) return;
            _begun = Time.frameCount;
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T == null) continue;
                if (b.Wrote && Mathf.Abs(Quaternion.Dot(b.T.localRotation, b.Written)) > 0.99999f) b.T.localRotation = b.Base;
                b.Wrote = false;
                b.Base = b.T.localRotation;
            }
            Transform h = _b[Hips].T;
            if (h != null)
            {
                if (_hipsWrote && (h.localPosition - _hipsWritten).sqrMagnitude < 1e-10f) h.localPosition = _hipsBase;
                _hipsWrote = false;
                _hipsBase = h.localPosition;
            }
        }

        /// <summary>Stops, and puts back at once any bone that still carries our pose (when Apply will not run again soon).</summary>
        public void Release()
        {
            Stop();
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T != null && b.Wrote && Mathf.Abs(Quaternion.Dot(b.T.localRotation, b.Written)) > 0.99999f) b.T.localRotation = b.Base;
                b.Wrote = false;
            }
            Transform h = _b[Hips].T;
            if (h != null && _hipsWrote && (h.localPosition - _hipsWritten).sqrMagnitude < 1e-10f) h.localPosition = _hipsBase;
            _hipsWrote = false;
            Posed = false;
        }

        /// <summary>
        /// Poses the avatar for this frame. body must be updated this frame (its torso frame is the motion's frame).
        /// allowed = false (VR, the setting off, dead) blends everything out.
        /// </summary>
        public bool Apply(GearBody body, bool allowed, float now, float dt)
        {
            return Apply(body, allowed, now, dt, default(GearWowPose));
        }

        /// <summary>Iteration 11: as Apply, with the WoW style (wow.Enabled: the WoW full-body pose replaces the simple motions).</summary>
        public bool Apply(GearBody body, bool allowed, float now, float dt, in GearWowPose wow)
        {
            Posed = false;
            Animator a = body != null && body.Valid ? body.Animator : null;
            if (a != _anim) { Bind(a); _begun = -1; }
            else if (a != null && AvatarChanged(a)) { Bind(a); _begun = -1; }   // fix 2026-10-04: a new avatar on the same Animator
            if (_anim == null) return false;
            Begin();

            if (!allowed) { _shots.Clear(); _holdW = 0f; _wowW = 0f; return false; }

            if (wow.Enabled)
            {
                _shots.Clear();
                _holdW = 0f;
                _wowW = Mathf.MoveTowards(_wowW, wow.On && wow.S.Valid ? 1f : 0f, dt / (wow.On ? WowIn : WowOut));
                if (wow.S.Valid) _last = wow.S;
                if (_wowW <= 0.001f || !_last.Valid) { if (!wow.ArmsOnly) Calibrate(dt); return false; }
                if (!_wowSaid)
                {
                    _wowSaid = true;
                    WowLog.Info("Gear: WoW full-body animation (ours) - blend in " + WowIn.ToString("0.0") + " s / out " + WowOut.ToString("0.0") + " s, legs and hips while standing (< " +
                                StillFull.ToString("0.00") + " m/s full, > " + StillNone.ToString("0.0") + " m/s none, airborne none), head " + (HeadShare * 100f).ToString("0") + " % of WoW's turn, pelvis turn within " + MaxHipsYaw.ToString("0") + " deg");
                }
                ApplyWow(body, wow, dt);
                return Written();
            }
            _wowW = 0f;
            Calibrate(dt);

            if (_hold != WowHold.None) _holdShown = _hold;
            _holdW = Mathf.MoveTowards(_holdW, _hold != WowHold.None ? 1f : 0f, dt / 0.18f);
            for (int i = _shots.Count - 1; i >= 0; i--)
                if (now - _shots[i].Start >= _shots[i].Def.Duration) _shots.RemoveAt(i);

            var mix = new GearMix();
            GearSample s;
            if (_holdW > 0f && _holdShown != WowHold.None)
            {
                GearMotions.Hold(_holdShown, now, out s);
                mix.Add(s, Mathf.SmoothStep(0f, 1f, _holdW));
            }
            for (int i = 0; i < _shots.Count; i++)
            {
                GearMotionDef d = _shots[i].Def;
                float t = (now - _shots[i].Start) / d.Duration;
                GearMotions.Sample(d, t, out s);
                mix.Add(s, GearMotions.Envelope(d, t));
            }
            if (!mix.Any) return false;

            // The motion's frame: the torso as the client animated it (GearBody), the shoulder midpoint, the arm length.
            Vector3 up = body.Up, fwd = body.Forward;
            Quaternion frame = Quaternion.LookRotation(fwd, up);
            Vector3 mid = (body.ShoulderL + body.ShoulderR) * 0.5f;
            float arm = ArmLengthBoth(body);

            if (mix.SW > 0.001f) Torso(mix.Twist * mix.SW, mix.Bend * mix.SW, up, body.Right);
            if (mix.RW > 0.001f) Limb(RUpper, mix.RW, mid + frame * (mix.RPos * arm), mix.RPos.y, 1f, frame);
            if (mix.LW > 0.001f) Limb(LUpper, mix.LW, mid + frame * (mix.LPos * arm), mix.LPos.y, -1f, frame);
            if (mix.RQW > 0.001f && mix.RW > 0.001f) Grip(RHand, _rIndex, _rLittle, _rMiddle, frame * mix.RQ, mix.RQW * mix.RW);
            if (mix.LQW > 0.001f && mix.LW > 0.001f) Grip(LHand, _lIndex, _lLittle, _lMiddle, frame * mix.LQ, mix.LQW * mix.LW);
            return Written();
        }

        /// <summary>Marks every bone that differs from its base as ours this frame (Begin puts it back if the Animator does not).</summary>
        private bool Written()
        {
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T == null) continue;
                Quaternion q = b.T.localRotation;
                if (Mathf.Abs(Quaternion.Dot(q, b.Base)) > 0.9999999f) continue;
                b.Written = q;
                b.Wrote = true;
                Posed = true;
            }
            Transform h = _b[Hips].T;
            if (h != null && (h.localPosition - _hipsBase).sqrMagnitude > 1e-12f)
            {
                _hipsWritten = h.localPosition;
                _hipsWrote = true;
                Posed = true;
            }
            return Posed;
        }

        private float ArmLengthBoth(GearBody body)
        {
            float armL = ArmLength(LUpper), armR = ArmLength(RUpper);
            float arm = armL > 0f && armR > 0f ? (armL + armR) * 0.5f : Mathf.Max(armL, armR);
            if (arm < 0.2f) arm = 0.55f * body.Scale;
            return arm;
        }

        /// <summary>Fix 2026-10-04 (the user: "when I change avatar, the animations break, my clothing moves but my avatar does not"):
        /// the bound Animator shows another skeleton now - another Animator.avatar, or a bone we hold was destroyed with the old model.
        /// Said once per RebindSayEvery s ("Gear: avatar of ... changed ... re-bound").</summary>
        private bool AvatarChanged(Animator a)
        {
            string why = null;
            if (a.avatar != _boundAvatar) why = "its humanoid avatar is now '" + (a.avatar != null ? a.avatar.name : "none") + "'";
            else if (Gone(_b[Hips].T) || Gone(_b[Head].T) || Gone(_b[LUpper].T) || Gone(_b[RUpper].T) || Gone(_b[LHand].T) || Gone(_b[RHand].T)) why = "a bone of the old skeleton was destroyed";
            if (why == null) return false;
            float now = Time.unscaledTime;
            if (now - _rebindSaidAt >= RebindSayEvery)
            {
                WowLog.Info("Gear: avatar of " + Label + " changed (" + why + ") - re-bound" + (_rebindQuiet > 0 ? " (" + _rebindQuiet + " more since the last line)" : ""));
                _rebindSaidAt = now;
                _rebindQuiet = 0;
            }
            else _rebindQuiet++;
            return true;
        }

        private static bool Gone(Transform t) { return !ReferenceEquals(t, null) && t == null; }

        private void Bind(Animator a)
        {
            // Hand the old avatar's bones back as the Animator left them.
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T != null && b.Wrote && Mathf.Abs(Quaternion.Dot(b.T.localRotation, b.Written)) > 0.99999f) b.T.localRotation = b.Base;
                b.T = null;
                b.Wrote = false;
            }
            _hipsWrote = false;
            _anim = a;
            _boundAvatar = a != null ? a.avatar : null;
            _rIndex = _rLittle = _rMiddle = _lIndex = _lLittle = _lMiddle = null;
            _ankleH = -1f;
            _legLen = -1f;
            _haveRoot = false;
            if (a == null || !a.isHuman) { _anim = null; return; }
            for (int i = 0; i < Count; i++) _b[i].T = a.GetBoneTransform(Ids[i]);
            _rIndex = a.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _rLittle = a.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            _rMiddle = a.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _lIndex = a.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _lLittle = a.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            _lMiddle = a.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            bool arms = _b[RUpper].T != null && _b[RLower].T != null && _b[RHand].T != null && _b[LUpper].T != null && _b[LLower].T != null && _b[LHand].T != null;
            _legs = _b[Hips].T != null && _b[LThigh].T != null && _b[LShin].T != null && _b[LFoot].T != null && _b[RThigh].T != null && _b[RShin].T != null && _b[RFoot].T != null;
            if (_legs)
            {
                float l = (_b[LShin].T.position - _b[LThigh].T.position).magnitude + (_b[LFoot].T.position - _b[LShin].T.position).magnitude;
                float r = (_b[RShin].T.position - _b[RThigh].T.position).magnitude + (_b[RFoot].T.position - _b[RShin].T.position).magnitude;
                _legLen = 0.5f * (l + r);
                if (_legLen < 0.2f) _legs = false;
            }
            WowLog.Info("Gear: avatar of " + Label + " can be posed: arms " + arms + ", spine " + (_b[Spine].T != null) + ", chest " + (_b[Chest].T != null || _b[UpperChest].T != null) +
                        ", fingers " + (_rIndex != null && _rLittle != null && _rMiddle != null && _lIndex != null && _lLittle != null && _lMiddle != null) +
                        ", legs " + _legs + (_legs ? " (leg " + _legLen.ToString("0.00") + " m)" : "") + ", neck " + (_b[Neck].T != null));
            if (!arms) _anim = null;
        }

        private float ArmLength(int upper)
        {
            Transform u = _b[upper].T, l = _b[upper + 1].T, h = _b[upper + 2].T;
            if (u == null || l == null || h == null) return 0f;
            return (l.position - u.position).magnitude + (h.position - l.position).magnitude;
        }

        /// <summary>Twist about the body's up axis and bend about its right axis, spread over spine / chest / upper chest; the head keeps most of its look.</summary>
        private void Torso(float twist, float bend, Vector3 up, Vector3 right)
        {
            if (Mathf.Abs(twist) < 0.05f && Mathf.Abs(bend) < 0.05f) return;
            Spread(Quaternion.AngleAxis(twist, up) * Quaternion.AngleAxis(bend, right));
        }

        /// <summary>A world rotation of the upper body spread over spine / chest / upper chest (0.3 / 0.35 / 0.35 of the bones there);
        /// the head keeps 75 % of where it looked.</summary>
        private void Spread(Quaternion delta)
        {
            Transform head = _b[Head].T;
            Quaternion headWas = head != null ? head.rotation : Quaternion.identity;
            float w0 = _b[Spine].T != null ? 0.3f : 0f, w1 = _b[Chest].T != null ? 0.35f : 0f, w2 = _b[UpperChest].T != null ? 0.35f : 0f;
            float sum = w0 + w1 + w2;
            if (sum <= 0f) return;
            for (int i = Spine; i <= UpperChest; i++)
            {
                Transform t = _b[i].T;
                if (t == null) continue;
                float share = (i == Spine ? w0 : i == Chest ? w1 : w2) / sum;
                t.rotation = Quaternion.Slerp(Quaternion.identity, delta, share) * t.rotation;
            }
            if (head != null) head.rotation = Quaternion.Slerp(head.rotation, headWas, 0.75f);
        }

        /// <summary>Two-bone IK of one arm to a world target, blended from the animated pose by weight (in local space).</summary>
        private void Limb(int upper, float weight, Vector3 target, float targetY, float side, Quaternion frame)
        {
            // Elbow: down, out and back for low hands, out and forward for raised ones.
            float raise = Mathf.Clamp01((targetY + 0.3f) / 0.6f);
            Vector3 poleBody = Vector3.Lerp(new Vector3(0.6f * side, -0.7f, -0.35f), new Vector3(0.75f * side, 0.05f, 0.4f), raise);
            TwoBone(_b[upper].T, _b[upper + 1].T, _b[upper + 2].T, target, frame * poleBody, weight, frame * Vector3.right);
        }

        /// <summary>Two-bone IK (upper, lower, end) to a world target with a pole direction, blended from the animated pose by weight.</summary>
        private static void TwoBone(Transform u, Transform l, Transform h, Vector3 target, Vector3 pole, float weight, Vector3 sideFallback)
        {
            if (u == null || l == null || h == null) return;
            Quaternion uBase = u.localRotation, lBase = l.localRotation;

            Vector3 s = u.position, e = l.position, w = h.position;
            float a = (e - s).magnitude, b = (w - e).magnitude;
            if (a < 1e-4f || b < 1e-4f) return;
            Vector3 toT = target - s;
            float dist = toT.magnitude;
            if (dist < 1e-4f) return;
            Vector3 dir = toT / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.999f);

            pole -= Vector3.Dot(pole, dir) * dir;
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.Cross(dir, sideFallback).normalized;
            else pole.Normalize();

            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = s + dir * (a * cosA) + pole * (a * sinA);

            u.rotation = Quaternion.FromToRotation(e - s, elbow - s) * u.rotation;
            Vector3 e1 = l.position, w1 = h.position;
            Vector3 want = s + dir * d - e1;
            if ((w1 - e1).sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f) l.rotation = Quaternion.FromToRotation(w1 - e1, want) * l.rotation;

            if (weight < 0.999f)
            {
                Quaternion uIk = u.localRotation, lIk = l.localRotation;
                u.localRotation = Quaternion.Slerp(uBase, uIk, weight);
                l.localRotation = Quaternion.Slerp(lBase, lIk, weight);
            }
        }

        /// <summary>Turns the hand so its grip frame (GearBody.HandPose's, from the finger bones) matches the wanted world rotation.</summary>
        private void Grip(int hand, Transform index, Transform little, Transform middle, Quaternion want, float weight)
        {
            Transform h = _b[hand].T;
            if (h == null || index == null || little == null || middle == null) return;
            Vector3 k = middle.position - h.position;
            if (k.sqrMagnitude < 1e-8f) return;
            k.Normalize();
            Vector3 f = index.position - little.position;
            f -= Vector3.Dot(f, k) * k;
            if (f.sqrMagnitude < 1e-8f) return;
            Quaternion now = Quaternion.LookRotation(f.normalized, -k);
            Quaternion turned = want * Quaternion.Inverse(now) * h.rotation;
            h.rotation = Quaternion.Slerp(h.rotation, turned, Mathf.Clamp01(weight));
        }

        // ------------------------------------------------------------------ iteration 11: the WoW style

        /// <summary>The avatar's own ankle height above its model root while it stands unposed (exp 2/s), and its root speed.</summary>
        private void Calibrate(float dt)
        {
            if (_anim == null) return;
            Vector3 rp = _anim.transform.position;
            if (_haveRoot && dt > 1e-5f)
            {
                Vector3 v = (rp - _lastRoot) / dt;
                if (v.sqrMagnitude < 3600f) _rootVel = Vector3.Lerp(_rootVel, v, 1f - Mathf.Exp(-dt * 10f));
            }
            _lastRoot = rp;
            _haveRoot = true;
            if (!_legs) return;
            float planar = new Vector2(_rootVel.x, _rootVel.z).magnitude;
            if (planar > 0.1f || Mathf.Abs(_rootVel.y) > 0.5f) { if (_ankleH < 0f) _ankleH = 0.08f * _legLen / 0.85f; return; }
            float ah = Mathf.Min(_b[LFoot].T.position.y, _b[RFoot].T.position.y) - rp.y;
            ah = Mathf.Clamp(ah, 0.02f, 0.25f);
            _ankleH = _ankleH < 0f ? ah : Mathf.Lerp(_ankleH, ah, 1f - Mathf.Exp(-dt * 2f));
        }

        private static Vector3 V(M2Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }
        private static Quaternion Q(M2Quat q) { return new Quaternion(q.X, q.Y, q.Z, q.W); }

        private void ApplyWow(GearBody body, in GearWowPose wow, float dt)
        {
            WowBodySample s = _last;
            Transform root = _anim.transform;
            Vector3 rp = root.position;
            if (_haveRoot && dt > 1e-5f)
            {
                Vector3 v = (rp - _lastRoot) / dt;
                if (v.sqrMagnitude < 3600f) _rootVel = Vector3.Lerp(_rootVel, v, 1f - Mathf.Exp(-dt * 10f));
            }
            _lastRoot = rp;
            _haveRoot = true;
            float planar = new Vector2(_rootVel.x, _rootVel.z).magnitude;
            float still = Mathf.Clamp01(Mathf.InverseLerp(StillNone, StillFull, planar));
            if (Mathf.Abs(_rootVel.y) > 1.5f) still = 0f;
            if (wow.ForcedLegs) still = 1f;
            float armW = Mathf.SmoothStep(0f, 1f, _wowW);
            float legW = _legs ? armW * still : 0f;
            if (_ankleH < 0f) _ankleH = Mathf.Clamp(Mathf.Min(_b[LFoot].T != null ? _b[LFoot].T.position.y : rp.y, _b[RFoot].T != null ? _b[RFoot].T.position.y : rp.y) - rp.y, 0.02f, 0.25f);

            // The avatar's model frame: its root on the ground, facing the root's forward (flat).
            Vector3 f0 = root.forward; f0.y = 0f;
            if (f0.sqrMagnitude < 1e-6f) f0 = body.Forward;
            Quaternion M = Quaternion.LookRotation(f0.normalized, Vector3.up);
            Quaternion footWasL = _b[LFoot].T != null ? _b[LFoot].T.rotation : Quaternion.identity;
            Quaternion footWasR = _b[RFoot].T != null ? _b[RFoot].T.rotation : Quaternion.identity;

            if (wow.ArmsOnly) { ArmsOnly(body, s, armW); return; }   // states fix 2026-10-04: the client's pose keeps hips, legs, torso, head
            if (wow.Swim) { if (ApplySwim(body, s, armW, M, wow.LegsOnly)) return; }   // lead 2026-10-26
            // 1. hips: WoW's crouch / lunge / hop and the pelvis' turn (legs weight)
            Transform hips = _b[Hips].T;
            if (hips != null && legW > 0.001f)
            {
                hips.position += M * (V(s.HipsOffset) * (_legLen * legW));
                hips.rotation = Quaternion.AngleAxis(Mathf.Clamp(s.HipsYaw, -MaxHipsYaw, MaxHipsYaw) * legW, Vector3.up) * Quaternion.AngleAxis(s.HipsRoll * legW, M * Vector3.forward) * hips.rotation;
            }
            // 2. legs: two-bone IK to WoW's ankles (from the model origin, ground-clamped at the avatar's own ankle height)
            if (legW > 0.001f)
            {
                Leg(_b[LThigh].T, _b[LShin].T, _b[LFoot].T, rp, M, s.LFoot, s.LKneePole, s.LToePitch, footWasL, legW);
                Leg(_b[RThigh].T, _b[RShin].T, _b[RFoot].T, rp, M, s.RFoot, s.RKneePole, s.RToePitch, footWasR, legW);
            }
            // 3. torso: the chest to WoW's twist / bend / lean relative to the pelvis' facing (arms weight)
            Vector3 cu, cr, cf;
            if (ChestFrame(body, out cu, out cr, out cf))
            {
                Quaternion cur = Quaternion.LookRotation(cf, cu);
                Quaternion pel = M * Quaternion.AngleAxis(Mathf.Clamp(s.HipsYaw, -MaxHipsYaw, MaxHipsYaw) * legW, Vector3.up);
                Quaternion des = pel * Quaternion.AngleAxis(s.Twist, Vector3.up) * Quaternion.AngleAxis(s.Bend, Vector3.right) * Quaternion.AngleAxis(s.Lean, Vector3.forward);
                Spread(Quaternion.Slerp(Quaternion.identity, des * Quaternion.Inverse(cur), armW));
            }
            // 4. head: a share of WoW's turn on top of where the client looks
            Transform head = _b[Head].T;
            if (head != null && ChestFrame(body, out cu, out cr, out cf))
                head.rotation = Quaternion.AngleAxis(s.HeadYaw * HeadShare * armW, cu) * Quaternion.AngleAxis(-s.HeadPitch * HeadShare * armW, cr) * head.rotation;
            // 5. arms + grips in the chest frame as posed now (the GearMotions convention: arm lengths from the shoulder midpoint)
            if (ChestFrame(body, out cu, out cr, out cf))
            {
                Quaternion C = Quaternion.LookRotation(cf, cu);
                Vector3 mid = (_b[LUpper].T.position + _b[RUpper].T.position) * 0.5f;
                float arm = ArmLengthBoth(body);
                TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, mid + C * (V(s.RHand) * arm), C * V(s.RElbowPole), armW, cr);
                TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, mid + C * (V(s.LHand) * arm), C * V(s.LElbowPole), armW, -cr);
                Grip(RHand, _rIndex, _rLittle, _rMiddle, C * Q(s.RGrip), armW);
                Grip(LHand, _lIndex, _lLittle, _lMiddle, C * Q(s.LGrip), armW);
            }
        }

        // ours (lead 2026-10-26): the body pitch taken from WoW's chest bend, the share of the neck's drop the hips are lifted by
        public const float SwimMaxPitch = 85f, SwimLift = 0.85f;
        private static bool _swimSaid;

        /// <summary>
        /// Lead 2026-10-26 (swimming): WoW's swim loop on the avatar. Desktop: the whole body is pitched about the hips by WoW's chest bend
        /// (the swimmer lies in the water as WoW draws it) and lifted so the neck stays near where it stood (shoulders at the waterline -
        /// WowSwim floats the feet 0.75 x height under the surface); the legs reach WoW's ankles measured FROM THE PELVIS (never clamped
        /// to the ground); then WoW's torso remainder, head share and arms as for any loop. VR (legsOnly): the tracked body stays upright -
        /// only the legs kick, WoW's pelvis -> ankle vectors turned back by the bend so they hang under the hips. False = no legs (the
        /// normal path runs).
        /// </summary>
        private bool ApplySwim(GearBody body, in WowBodySample s, float w, Quaternion M, bool legsOnly)
        {
            Transform hips = _b[Hips].T;
            if (!_legs || hips == null || w <= 0.001f) return false;
            if (!_swimSaid)
            {
                _swimSaid = true;
                WowLog.Info("Gear: swimming animation (WoW 41-45) - desktop: body pitched by WoW's chest bend (max " + SwimMaxPitch + " deg), lifted by " +
                            SwimLift + " of the neck's drop, legs from the pelvis; VR: legs only, straightened to the upright body");
            }
            Vector3 right = M * Vector3.right;
            float pitch = Mathf.Clamp(s.Bend, -10f, SwimMaxPitch);
            if (!legsOnly)
            {
                Transform neck = _b[Neck].T != null ? _b[Neck].T : _b[Head].T;
                float neckY = neck != null ? neck.position.y : 0f;
                hips.rotation = Quaternion.AngleAxis(Mathf.Clamp(s.HipsYaw, -MaxHipsYaw, MaxHipsYaw) * w, Vector3.up) * Quaternion.AngleAxis(pitch * w, right) * hips.rotation;
                if (neck != null) hips.position += Vector3.up * ((neckY - neck.position.y) * SwimLift);
            }
            // legs: WoW's ankle - pelvis (leg lengths, the model frame), from our pelvis
            Quaternion legFrame = legsOnly ? M * Quaternion.AngleAxis(-pitch, Vector3.right) : M;
            Vector3 off = V(s.HipsOffset);
            Vector3 hp = hips.position;
            SwimLeg(_b[LThigh].T, _b[LShin].T, _b[LFoot].T, hp, legFrame, V(s.LFoot) - off - Vector3.up, s.LKneePole, s.LToePitch, w, legsOnly, right);
            SwimLeg(_b[RThigh].T, _b[RShin].T, _b[RFoot].T, hp, legFrame, V(s.RFoot) - off - Vector3.up, s.RKneePole, s.RToePitch, w, legsOnly, right);
            if (legsOnly) return true;
            // torso remainder (WoW's absolute bend vs the pitched chest), head, arms - ApplyWow's steps 3-5
            Vector3 cu, cr, cf;
            if (ChestFrame(body, out cu, out cr, out cf))
            {
                Quaternion cur = Quaternion.LookRotation(cf, cu);
                Quaternion pel = M * Quaternion.AngleAxis(Mathf.Clamp(s.HipsYaw, -MaxHipsYaw, MaxHipsYaw) * w, Vector3.up);
                Quaternion des = pel * Quaternion.AngleAxis(s.Twist, Vector3.up) * Quaternion.AngleAxis(s.Bend, Vector3.right) * Quaternion.AngleAxis(s.Lean, Vector3.forward);
                Spread(Quaternion.Slerp(Quaternion.identity, des * Quaternion.Inverse(cur), w));
            }
            Transform head = _b[Head].T;
            if (head != null && ChestFrame(body, out cu, out cr, out cf))
                head.rotation = Quaternion.AngleAxis(s.HeadYaw * HeadShare * w, cu) * Quaternion.AngleAxis(-s.HeadPitch * HeadShare * w, cr) * head.rotation;
            if (ChestFrame(body, out cu, out cr, out cf))
            {
                Quaternion C = Quaternion.LookRotation(cf, cu);
                Vector3 mid = (_b[LUpper].T.position + _b[RUpper].T.position) * 0.5f;
                float arm = ArmLengthBoth(body);
                TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, mid + C * (V(s.RHand) * arm), C * V(s.RElbowPole), w, cr);
                TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, mid + C * (V(s.LHand) * arm), C * V(s.LElbowPole), w, -cr);
                Grip(RHand, _rIndex, _rLittle, _rMiddle, C * Q(s.RGrip), w);
                Grip(LHand, _lIndex, _lLittle, _lMiddle, C * Q(s.LGrip), w);
            }
            return true;
        }

        private void SwimLeg(Transform u, Transform l, Transform f, Vector3 pelvis, Quaternion frame, Vector3 ankleFromPelvis, M2Vec3 kneePole, float toePitch, float w, bool legsOnly, Vector3 right)
        {
            if (u == null || l == null || f == null) return;
            Quaternion footWas = f.rotation;
            Vector3 target = pelvis + frame * (ankleFromPelvis * _legLen);
            TwoBone(u, l, f, target, frame * V(kneePole), w, right);
            if (legsOnly) { f.rotation = footWas; return; }   // VR: the foot keeps its own turn
            f.rotation = Quaternion.Slerp(f.rotation, Quaternion.AngleAxis(-toePitch, right) * footWas, w);
        }

        /// <summary>States fix 2026-10-04: WoW's hands, elbow poles and grips in the chest frame of the CLIENT's pose (sitting, kneeling) -
        /// the eat / drink arms (61 EmoteEat) on a seated avatar; nothing else is touched.</summary>
        private void ArmsOnly(GearBody body, in WowBodySample s, float armW)
        {
            Vector3 cu, cr, cf;
            if (!ChestFrame(body, out cu, out cr, out cf)) return;
            Quaternion C = Quaternion.LookRotation(cf, cu);
            Vector3 mid = (_b[LUpper].T.position + _b[RUpper].T.position) * 0.5f;
            float arm = ArmLengthBoth(body);
            TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, mid + C * (V(s.RHand) * arm), C * V(s.RElbowPole), armW, cr);
            TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, mid + C * (V(s.LHand) * arm), C * V(s.LElbowPole), armW, -cr);
            Grip(RHand, _rIndex, _rLittle, _rMiddle, C * Q(s.RGrip), armW);
            Grip(LHand, _lIndex, _lLittle, _lMiddle, C * Q(s.LGrip), armW);
        }

        private void Leg(Transform u, Transform l, Transform f, Vector3 rp, Quaternion M, M2Vec3 foot, M2Vec3 kneePole, float toePitch, Quaternion footWas, float w)
        {
            if (u == null || l == null || f == null) return;
            float L = _legLen;
            Vector3 target = rp + M * new Vector3(foot.X * L, 0f, foot.Z * L) + Vector3.up * (_ankleH + Mathf.Max(0f, foot.Y * L));
            TwoBone(u, l, f, target, M * V(kneePole), w, M * Vector3.right);
            // the foot keeps the client's own orientation, turned by WoW's toe pitch (+ toe up)
            Quaternion want = Quaternion.AngleAxis(-toePitch, M * Vector3.right) * footWas;
            f.rotation = Quaternion.Slerp(f.rotation, want, w);
        }

        /// <summary>The avatar's chest frame from its bones now (GearBody's rule: up = neck - hips, right = the upper arms' line).</summary>
        private bool ChestFrame(GearBody body, out Vector3 up, out Vector3 right, out Vector3 fwd)
        {
            up = Vector3.up; right = Vector3.right; fwd = Vector3.forward;
            Transform hips = _b[Hips].T, neck = _b[Neck].T != null ? _b[Neck].T : _b[Head].T;
            Transform lu = _b[LUpper].T, ru = _b[RUpper].T;
            if (lu == null || ru == null) return false;
            Vector3 hp = hips != null ? hips.position : body.Hips;
            Vector3 np = neck != null ? neck.position : body.HeadPos;
            Vector3 u = np - hp;
            if (u.sqrMagnitude < 1e-6f) return false;
            up = u.normalized;
            Vector3 across = ru.position - lu.position;
            Vector3 r = across - Vector3.Dot(across, up) * up;
            if (r.sqrMagnitude < 1e-8f) return false;
            right = r.normalized;
            fwd = Vector3.Cross(right, up);
            return true;
        }
    }
}
