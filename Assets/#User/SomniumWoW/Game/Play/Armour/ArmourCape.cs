// Cape agent (2026-10-04): the user asked "are you able to add magica cloth on the capes? ... Or if you know a better solution that
// is easier to do". Magica Cloth cannot be used: the Somnium upload server compiles worlds against a fixed reference list
// (P/build/out11_3c/runtime_server.gen.rsp) without MagicaCloth2. So this is our own light cape physics for the "Immersive armour:
// Full" shell (ArmourShell).
using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Armour
{
    /// <summary>
    /// The cape of one armour shell as a simulated chain (ours). Measured first (harness probes Cape11/CapeSkin and CapePose on the
    /// real HumanMale / HumanFemale .m2 + 00.skin): WoW skins its cape geosets (1501..1506) mostly to the chest bone (b4: 32-69 % of
    /// the weight), the collar also to the neck, clavicles and shoulders, the middle to the waist bones; it has only two cape bones,
    /// both under the waist (HumanMale b5 pivot y 0.858 -> b12 y 0.730, HumanFemale b7 0.937 -> b14 0.790), which carry only the
    /// lower third (male 1502: y 0.668 on b5 + b12, y 0.494 and 0.306 on b12 alone). In Stand the cape keeps its bind shape (male
    /// bottom z -0.448 bind, -0.487 Stand); in Run those two bones lift its bottom up to 0.5 m. Two hinges at hip height cannot fold a
    /// 1.35 m cape, so we add our own:
    /// - Rows: Rows new bones from the cape's shoulder line (WoW's shoulder joints' height through the Cloak fit: row 0, pinned) down
    ///   to its lowest vertex, each on the cape's back centre line; transforms under the shell appended to smr.bones, their bindposes
    ///   appended to the shell's own mesh (built per shell, readable); every cape vertex re-weighted to its two nearest rows by height
    ///   (above row 0: row 0). Rebuilt when the cape's vertices change (torso remap, Cloak offset / size).
    /// - Rest pose: each row rigid with the frame the shell poses WoW's torso bones in - the chest segment above WoW's chest pivot, the
    ///   hips segment below (where WoW's waist and cape bones ride) - with their scale but without the chest / legs pieces' own fit
    ///   (the cloak has its own). At rest the cape is where WoW's weights put it.
    /// - Every LateUpdate after the shell is placed: verlet in world space with fixed substeps (inertia from the avatar's movement and
    ///   turning, gravity, air drag, swing drag against the body, a pull to the hung pose: rows over the back on their rest, the rows
    ///   below along WoW's own cape slope - the torso remap doubles the flare, so the rest sticks out), each row kept at its rest distance
    ///   below the one above (follow the leader from the pinned top) and bent at most MaxBend more than the hung pose, and collision:
    ///   rows over the back may not come closer to the body than their rest pose; rows below WoW's hip joints stay behind a plane
    ///   across the avatar's facing, Give of the way from their hung pose to the back of its hip joints' leg armour, and behind
    ///   capsules along its thighs and shins with the leg armour's back radius (pushed only backward) - so on a mount, legs forward
    ///   and spread, the cape hangs behind the seat.
    ///   Each row then turns by how its neighbours swung.
    /// - Reset to the hung pose: a new rig or cape fit, the cape shown again, a frame without the shell placed, a frame over MaxFrame,
    ///   a jump of the pinned row (rebase, teleport, mount / dismount). No per-frame allocation. Nothing is networked: every client runs its own
    ///   copy for every avatar it draws.
    /// </summary>
    public sealed class ArmourCape
    {
        public const int Rows = 6;                  // ours: row 0 pinned on the shoulder line + 5 swinging rows (WoW's own cape: 2 bones)
        public const float SubStep = 1f / 120f;     // ours: s, the fixed physics step
        public const int MaxSubSteps = 8;           // ours: steps per frame at most (under 15 fps the cape runs slow instead of exploding)
        public const float MaxFrame = 0.25f;        // ours: s, a longer frame (a hitch, a load) resets the cape to its hung pose
        public const float Gravity = 9.81f;         // m/s2
        public const float AirDrag = 2.5f;          // ours: 1/s, world speed lost to the air (lab: rows 1-5 trail 23 deg walking 1.4 m/s, 54 deg running 4.5 m/s)
        public const float SwingDrag = 3f;          // ours: 1/s, swing speed against the body lost to the cloth (with the air: settles after about one swing)
        public const float ShapePull = 12f;         // ours: 1/s2, the pull back to the hung pose (rows over the back: their rest; below: WoW's own slope)
        public const float MaxBend = 30f;           // ours: deg, a row turns at most this much more than the hung pose bends there (no hard creases)
        public const float Inertia = 0.8f;          // ours: the share of the avatar's own motion the cape feels as inertia; the rest is carried along
        public const float JumpMin = 0.5f;          // ours: m, the pinned row moving more than this and more than JumpSpeed x the frame = a jump
        public const float JumpSpeed = 40f;         // ours: m/s (WoW's fastest flying, 310 % of 7 yd/s, is about 20 m/s)
        public const float MinLength = 0.2f;        // ours: m, a cape reaching less than this below its shoulder line keeps WoW's weights (no physics)
        public const float StripHalf = 0.08f;       // ours: m either side of the cape's middle that counts as its centre line
        public const float BandShare = 0.35f;       // ours: no centre vertex at all: the backmost vertex within this x the row spacing of the row's height
        public const float LegSlack = 0.01f;        // ours: m, the cape's centre line stays this far outside the leg armour
        public const float Give = 0.5f;             // ours: rows below the hips hang in from WoW's own slope at most this share of the way to the
                                                    // legs (only the centre line collides; WoW's slope is what keeps the cape's wrapped sides off the legs)

        /// <summary>The avatar this frame (ArmourShell fills it), world positions including the shell's draw offset.</summary>
        public struct Pose
        {
            public Vector3 ChestPos, HipsPos;           // the shell's chest / hips segment origins (the avatar's joints)
            public Quaternion ChestRot, HipsRot;        // their frames (up = the segment's axis, forward = the body's front)
            public Vector3 ChestScale, HipsScale;       // their scale as the shell poses them, without the pieces' own fit
            public Vector3 Facing;                      // the avatar's facing (horizontal)
            public Vector3 LHip, RHip, LKnee, RKnee, LAnkle, RAnkle;
            public float ThighRadius, ShinRadius;       // m, the leg armour's back from the leg's axis
        }

        private static readonly float AirKeep = Mathf.Exp(-AirDrag * SubStep), SwingKeep = Mathf.Exp(-SwingDrag * SubStep);

        private readonly string _label;
        private readonly Mesh _mesh;
        private readonly int[] _capeV;
        private readonly BoneWeight[] _bw0, _bw;     // the shell mesh's own weights, and the work copy with the cape on our rows
        private readonly Matrix4x4[] _bind;          // the mesh's bindposes + ours
        private readonly int _first;                 // our first bone index
        private readonly Transform[] _row = new Transform[Rows];
        private readonly Vector3 _cStart, _hStart;   // the chest / hips segments' bind frames (ArmourShell.Bind)
        private readonly Quaternion _cRot, _hRot, _cInv, _hInv;
        private readonly float _splitY, _hipY;       // bind heights: WoW's chest pivot (chest frame above), its hip joints (back rows above)
        private readonly Vector3[] _b = new Vector3[Rows];      // the rows' bind points (on the cape's centre line)
        private readonly Vector3[] _dirW = new Vector3[Rows];   // WoW's own cape slope into each row from the one above (hips bind frame)
        private readonly bool[] _onChest = new bool[Rows], _onBack = new bool[Rows];
        private readonly Vector3[] _x = new Vector3[Rows], _xp = new Vector3[Rows], _r = new Vector3[Rows], _rp = new Vector3[Rows];
        // the hung pose (the sim's target), it in a substep, and its motion per substep that the cape feels as inertia
        private readonly Vector3[] _h = new Vector3[Rows], _t = new Vector3[Rows], _u = new Vector3[Rows];
        private readonly float[] _len = new float[Rows];
        private bool _ok, _reset = true;
        private int _frame = -1, _saidOk = -1;
        private float _acc;

        /// <summary>Our rows on the shell (null = no cape vertices or a mesh we cannot re-skin). Rebind places them.</summary>
        public static ArmourCape Create(string label, SkinnedMeshRenderer smr, Mesh mesh, Transform parent, int[] capeV,
                                        Vector3 chestStart, Quaternion chestRot, Vector3 hipsStart, Quaternion hipsRot, float hipJointY)
        {
            if (smr == null || mesh == null || !mesh.isReadable || parent == null || capeV == null || capeV.Length == 0) return null;
            Transform[] bones = smr.bones;
            Matrix4x4[] bind = mesh.bindposes;
            BoneWeight[] bw = mesh.boneWeights;
            if (bones == null || bind == null || bones.Length == 0 || bones.Length != bind.Length || bw == null || bw.Length != mesh.vertexCount) return null;
            return new ArmourCape(label, smr, mesh, parent, capeV, bones, bind, bw, chestStart, chestRot, hipsStart, hipsRot, hipJointY);
        }

        private ArmourCape(string label, SkinnedMeshRenderer smr, Mesh mesh, Transform parent, int[] capeV, Transform[] bones, Matrix4x4[] bind,
                           BoneWeight[] bw, Vector3 chestStart, Quaternion chestRot, Vector3 hipsStart, Quaternion hipsRot, float hipJointY)
        {
            _label = label;
            _mesh = mesh;
            _capeV = capeV;
            _bw0 = bw;
            _bw = (BoneWeight[])bw.Clone();
            _cStart = chestStart; _cRot = chestRot; _cInv = Quaternion.Inverse(chestRot);
            _hStart = hipsStart; _hRot = hipsRot; _hInv = Quaternion.Inverse(hipsRot);
            _splitY = chestStart.y;
            _hipY = hipJointY;
            _first = bones.Length;
            var all = new Transform[bones.Length + Rows];
            Array.Copy(bones, all, bones.Length);
            _bind = new Matrix4x4[bind.Length + Rows];
            Array.Copy(bind, _bind, bind.Length);
            for (int j = 0; j < Rows; j++)
            {
                var t = new GameObject("cape row " + j).transform;
                t.SetParent(parent, false);
                _row[j] = t;
                all[_first + j] = t;
                _bind[_first + j] = Matrix4x4.identity;   // unused until Rebind weights a vertex to it
            }
            mesh.bindposes = _bind;
            smr.bones = all;
        }

        /// <summary>The rows on the cape's vertices as they are now (v, bind space: torso remap and Cloak fit applied; v0 = the same
        /// vertices as WoW has them, for its own slope); pinY = the cape's shoulder line. Re-weights the cape and resets it. Only when the
        /// cape changes.</summary>
        public void Rebind(Vector3[] v, Vector3[] v0, float pinY)
        {
            _reset = true;
            _ok = false;
            if (v == null || v0 == null || v0.Length != v.Length) return;
            float top = float.MinValue, bot = float.MaxValue, sx = 0f;
            foreach (int i in _capeV)
            {
                if (i < 0 || i >= v.Length) return;
                top = Mathf.Max(top, v[i].y); bot = Mathf.Min(bot, v[i].y); sx += v[i].x;
            }
            float mid = sx / _capeV.Length;
            pinY = Mathf.Min(pinY, top);
            float span = pinY - bot;
            if (span < MinLength)
            {
                foreach (int i in _capeV) _bw[i] = _bw0[i];
                _mesh.boneWeights = _bw;
                Say(false, "Armour: cape physics for " + _label + ": none - the cape reaches " + (span * 100f).ToString("0") + " cm below its shoulder line (under " +
                    (MinLength * 100f).ToString("0") + " cm, ours): WoW's own weights");
                return;
            }
            float step = span / (Rows - 1);
            int chest = 0, back = 0;
            Vector3 wPrev = Vector3.zero;
            for (int j = 0; j < Rows; j++)
            {
                float y = pinY - j * step;
                Vector3 w;
                _b[j] = new Vector3(mid, y, Centre(v, v0, mid, y, pinY, BandShare * step, out w));
                Vector3 dw = w - wPrev;
                dw.x = 0f;
                if (j > 0) _dirW[j] = _hInv * dw;   // WoW's own slope from the row above (no sideways part), in the hips segment's bind frame
                wPrev = w;
                _onChest[j] = y >= _splitY;
                _onBack[j] = y >= _hipY;
                if (_onChest[j]) chest++;
                if (_onBack[j] && j > 0) back++;
                _bind[_first + j] = Matrix4x4.TRS(_b[j], _onChest[j] ? _cRot : _hRot, Vector3.one).inverse;
            }
            foreach (int i in _capeV)
            {
                float u = (pinY - v[i].y) / step;   // 0 at row 0 .. Rows - 1 at the lowest vertex
                var w = new BoneWeight();
                if (u <= 0f) { w.boneIndex0 = _first; w.weight0 = 1f; }
                else if (u >= Rows - 1) { w.boneIndex0 = _first + Rows - 1; w.weight0 = 1f; }
                else
                {
                    int j = Mathf.Min((int)u, Rows - 2);
                    float t = u - j;
                    // the heavier row first (Unity's order)
                    if (t <= 0.5f) { w.boneIndex0 = _first + j; w.weight0 = 1f - t; w.boneIndex1 = _first + j + 1; w.weight1 = t; }
                    else { w.boneIndex0 = _first + j + 1; w.weight0 = t; w.boneIndex1 = _first + j; w.weight1 = 1f - t; }
                }
                _bw[i] = w;
            }
            _mesh.bindposes = _bind;
            _mesh.boneWeights = _bw;
            _ok = true;
            Say(true, "Armour: cape physics for " + _label + ": " + Rows + " rows (row 0 pinned on the shoulder line at bind y " + pinY.ToString("0.00") +
                " m, the lowest at " + bot.ToString("0.00") + " m, every " + (step * 100f).ToString("0") + " cm; " + chest + " on the chest frame, " + (Rows - chest) +
                " on the hips frame; " + back + " held off the back, " + (Rows - 1 - back) + " behind the legs), " + _capeV.Length + " cape vertices re-weighted; ours: substep 1/" +
                Mathf.RoundToInt(1f / SubStep) + " s (max " + MaxSubSteps + "), gravity " + Gravity.ToString("0.00") + ", air drag " + AirDrag.ToString("0.0") + "/s, swing drag " +
                SwingDrag.ToString("0.0") + "/s, shape pull " + ShapePull.ToString("0") + "/s2, bend " + MaxBend.ToString("0") + " deg, give " + Give.ToString("0.0") +
                ", inertia " + Inertia.ToString("0.0") + ", reset on a jump over " + JumpMin.ToString("0.0") + " m and " + JumpSpeed.ToString("0") +
                " m/s or a frame over " + MaxFrame.ToString("0.00") + " s");
        }

        /// <summary>The cape's centre line z at height y: between the nearest centre vertices (within StripHalf of its middle, under the
        /// shoulder line - above it the collar also has front vertices) above and below (lab: 67 vertices, a height band alone caught
        /// side vertices); w = the same point on WoW's own vertices (v0). Fallback: the backmost vertex within band of y.</summary>
        private float Centre(Vector3[] v, Vector3[] v0, float mid, float y, float pinY, float band, out Vector3 w)
        {
            int a = -1, b = -1;
            foreach (int i in _capeV)
            {
                Vector3 p = v[i];
                if (Mathf.Abs(p.x - mid) > StripHalf || p.y >= pinY) continue;
                if (p.y >= y && (a < 0 || p.y < v[a].y || (p.y == v[a].y && p.z < v[a].z))) a = i;
                if (p.y <= y && (b < 0 || p.y > v[b].y || (p.y == v[b].y && p.z < v[b].z))) b = i;
            }
            if (a >= 0 && b >= 0 && a != b && v[a].y - v[b].y > 1e-5f)
            {
                float t = (v[a].y - y) / (v[a].y - v[b].y);
                w = Vector3.Lerp(v0[a], v0[b], t);
                return Mathf.Lerp(v[a].z, v[b].z, t);
            }
            if (a >= 0 || b >= 0) { int c = a >= 0 ? a : b; w = v0[c]; return v[c].z; }
            int best = -1;
            foreach (int i in _capeV) if (Mathf.Abs(v[i].y - y) <= band && (best < 0 || v[i].z < v[best].z)) best = i;
            if (best < 0) best = _capeV[0];
            w = v0[best];
            return v[best].z;
        }

        private void Say(bool ok, string s)
        {
            int state = ok ? 1 : 0;
            if (state == _saidOk) return;   // once per shell build (again only if the cape turns physics on / off)
            _saidOk = state;
            WowLog.Info(s);
        }

        /// <summary>After ArmourShell.Place (every LateUpdate): hidden = the cape is not drawn (our own eye, the Cloak's Show off) -
        /// nothing runs and it starts from its hung pose when shown again.</summary>
        public void Step(bool hidden, in Pose p)
        {
            if (!_ok) return;
            int frame = Time.frameCount;
            if (hidden) { _reset = true; _frame = frame; return; }
            bool again = frame == _frame;   // placed twice in one frame: follow the new pose, no second step
            bool gap = !again && _frame >= 0 && frame != _frame + 1;
            float dt = Time.deltaTime;
            _frame = frame;
            for (int j = 0; j < Rows; j++) _r[j] = Rest(j, in p);
            for (int j = 1; j < Rows; j++) _len[j] = (_r[j] - _r[j - 1]).magnitude;
            // the hung pose the cape settles to: rows over the back on their rest, the rows below along WoW's own cape slope at their rest spacing
            // (the torso remap deepens the cape's flare by the avatar / WoW depth ratio: lab, the RPM man's centre line at y 0.20 z -0.45 -> -0.87)
            for (int j = 0; j < Rows; j++)
            {
                if (j == 0 || _onBack[j]) { _h[j] = _r[j]; continue; }
                Vector3 dir = p.HipsRot * _dirW[j];   // turned with the hips, not scaled: WoW's angle, not the torso length ratio's
                _h[j] = _h[j - 1] + (dir.sqrMagnitude > 1e-10f ? dir.normalized : Vector3.down) * _len[j];
            }
            float moved = (_h[0] - _rp[0]).magnitude;
            if (_reset || gap || (!again && (dt <= 0f || dt > MaxFrame)) || moved > Mathf.Max(JumpMin, JumpSpeed * dt))
            {
                for (int j = 0; j < Rows; j++) { _x[j] = _h[j]; _xp[j] = _h[j]; _rp[j] = _h[j]; }
                _acc = 0f;
                _reset = false;
                Apply(in p);
                return;
            }
            // the part of the avatar's motion the cape does not feel: carried rigidly (position and previous position alike)
            for (int j = 1; j < Rows; j++)
            {
                Vector3 c = (1f - Inertia) * (_h[j] - _rp[j]);
                _x[j] += c;
                _xp[j] += c;
            }
            int n = 0;
            if (!again)
            {
                _acc += dt;
                n = Mathf.Min((int)(_acc / SubStep), MaxSubSteps);
                _acc = Mathf.Min(_acc - n * SubStep, SubStep);
            }
            Vector3 fwdC = p.ChestRot * Vector3.forward, fwdH = p.HipsRot * Vector3.forward;
            Vector3 face = p.Facing;
            face.y = 0f;
            face = face.sqrMagnitude > 1e-6f ? face.normalized : fwdH;
            Vector3 legBack = 0.5f * (p.LHip + p.RHip) - face * (p.ThighRadius + LegSlack);
            Vector3 g = Vector3.down * (Gravity * SubStep * SubStep);
            float pull = ShapePull * SubStep * SubStep, cosBend = Mathf.Cos(MaxBend * Mathf.Deg2Rad), sinBend = Mathf.Sin(MaxBend * Mathf.Deg2Rad);
            for (int j = 1; j < Rows && n > 0; j++) _u[j] = (_h[j] - _rp[j]) * (Inertia / n);
            for (int s = 0; s < n; s++)
            {
                float t = (s + 1f) / n;
                for (int j = 0; j < Rows; j++) _t[j] = Vector3.LerpUnclamped(_rp[j], _h[j], t);
                _x[0] = _t[0];
                for (int j = 1; j < Rows; j++)
                {
                    Vector3 rest = _t[j];
                    Vector3 x = _x[j];
                    // the swing against the body (the rest's own motion, as far as the cape feels it) loses SwingDrag, all motion AirDrag
                    Vector3 vel = (_u[j] + (x - _xp[j] - _u[j]) * SwingKeep) * AirKeep;
                    _xp[j] = x;
                    x += vel + g + (rest - x) * pull;
                    // follow the leader: the row's rest distance below the one above, bent at most MaxBend more than the rest shape
                    Vector3 d = x - _x[j - 1];
                    float m = d.magnitude;
                    d = m > 1e-6f ? d / m : Vector3.down;
                    Vector3 restDir = rest - _t[j - 1];
                    if (j >= 2) restDir = Quaternion.FromToRotation(_t[j - 1] - _t[j - 2], _x[j - 1] - _x[j - 2]) * restDir;
                    if (restDir.sqrMagnitude > 1e-10f) d = Bend(restDir.normalized, d, cosBend, sinBend);
                    x = _x[j - 1] + d * _len[j];
                    _x[j] = Collide(j, x, rest, fwdC, fwdH, face, legBack, in p);
                }
            }
            _x[0] = _h[0];
            for (int j = 0; j < Rows; j++) _rp[j] = _h[j];
            Apply(in p);
        }

        /// <summary>Unit d, at most the bend angle from unit want (only beyond it: Vector3.RotateTowards with no magnitude change returns
        /// want itself for nearly parallel vectors - lab: each substep's tiny gravity sag was undone and the cape never hung).</summary>
        private static Vector3 Bend(Vector3 want, Vector3 d, float cosBend, float sinBend)
        {
            float c = Vector3.Dot(want, d);
            if (c >= cosBend) return d;
            Vector3 perp = d - want * c;
            float m = perp.magnitude;
            return m > 1e-6f ? want * cosBend + perp * (sinBend / m) : want;
        }

        private Vector3 Rest(int j, in Pose p)
        {
            return _onChest[j] ? p.ChestPos + p.ChestRot * Vector3.Scale(p.ChestScale, _cInv * (_b[j] - _cStart))
                               : p.HipsPos + p.HipsRot * Vector3.Scale(p.HipsScale, _hInv * (_b[j] - _hStart));
        }

        private Vector3 Collide(int j, Vector3 x, Vector3 rest, Vector3 fwdC, Vector3 fwdH, Vector3 face, Vector3 legBack, in Pose p)
        {
            if (_onBack[j])
            {
                // over the back: never closer to the body than its rest pose (which the torso remap put outside the avatar's back)
                Vector3 n = _onChest[j] ? fwdC : fwdH;
                float d = Vector3.Dot(x - rest, n);
                return d > 0f ? x - n * d : x;
            }
            // below the hip joints: behind the legs, and at most Give of the way in from WoW's own slope toward them
            float gap = Vector3.Dot(legBack - rest, face);
            Vector3 at = gap > 0f ? rest + face * (Give * gap) : legBack;
            float b = Vector3.Dot(x - at, face);
            if (b > 0f) x -= face * b;
            x = Leg(x, p.LHip, p.LKnee, p.ThighRadius + LegSlack, face);
            x = Leg(x, p.RHip, p.RKnee, p.ThighRadius + LegSlack, face);
            x = Leg(x, p.LKnee, p.LAnkle, p.ShinRadius + LegSlack, face);
            return Leg(x, p.RKnee, p.RAnkle, p.ShinRadius + LegSlack, face);
        }

        /// <summary>A leg capsule pushes the centre line only backward (against the facing), never sideways (both legs' capsules
        /// reach across the middle: lab, the run pushed the cape 0.58 m to one side).</summary>
        private static Vector3 Leg(Vector3 x, Vector3 a, Vector3 b, float r, Vector3 face)
        {
            Vector3 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(x - a, ab) / l2) : 0f;
            Vector3 d = x - (a + ab * t);
            float along = Vector3.Dot(d, face);
            float side2 = d.sqrMagnitude - along * along;
            if (side2 >= r * r) return x;
            float behind = Mathf.Sqrt(r * r - side2);   // how far behind the axis the leg's surface is at this sideways offset
            return along > -behind ? x - face * (along + behind) : x;
        }

        /// <summary>The rows' transforms: row 0 rigid with its frame, the others turned by how their neighbours swung.</summary>
        private void Apply(in Pose p)
        {
            for (int j = 0; j < Rows; j++)
            {
                Quaternion f = _onChest[j] ? p.ChestRot : p.HipsRot;
                if (j > 0)
                {
                    int a = j - 1, b = Mathf.Min(j + 1, Rows - 1);
                    Vector3 restDir = _r[b] - _r[a], simDir = _x[b] - _x[a];
                    if (restDir.sqrMagnitude > 1e-10f && simDir.sqrMagnitude > 1e-10f) f = Quaternion.FromToRotation(restDir, simDir) * f;
                }
                _row[j].SetPositionAndRotation(_x[j], f);
                _row[j].localScale = _onChest[j] ? p.ChestScale : p.HipsScale;
            }
        }
    }
}
