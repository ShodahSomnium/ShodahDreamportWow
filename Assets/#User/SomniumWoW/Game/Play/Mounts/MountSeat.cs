using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Mounts
{
    /// <summary>
    /// The rider's seat (iteration 7, owner Ride; PLAN7 s5.3 "Takeover", "Hips / eye", "Release"; s4.9). The avatar is taken over
    /// exactly as the taxi (Flight/FlightRide.TakeOver) and the Euro Truck Sim seat (EtsTruckSeat.TrySeat) do it - only the ten Bridge
    /// members the taxi already ships: motion collision / fly / teleport off, speed / gravity / jump 0, falling animation off, VRIK
    /// legs off, the avatar's CharacterController disabled (its radius, height, centre, stepOffset and slopeLimit are read first for
    /// the proxy) - and held in PoseType.SittingStraight every frame (the client drops a pose on its cancel action, an avatar reload
    /// or a scale change). The root is always Quaternion.Euler(0, heading, 0) (VR comfort: never the mount's pitch or roll).
    /// Desktop: root = saddle - up x hips, the hips measured on the avatar's humanoid Animator 10 frames after the pose has lowered
    /// the head (ETS PoseLowered), averaged over 5 frames, clamped 0.25-1.1 m (fallback 0.45 m = FlightRide.SeatDrop).
    /// VR: the same root, then ETS PlaceHead (EtsTruckSeat.cs 1063-1102): the rig is shifted so the headset lands on the eye point
    /// (saddle + the camera's height above the hips in the pose, measured the same way), the lean since sitting down kept but clamped
    /// to 0.45 m, the rest re-taken past 0.7 m; the pose entry's re-centring is undone (ETS UndoPoseRecentre 975-1050: the head offset's
    /// turn taken out, both hand offsets reset). Release: the root written on the ground spot while the controller is still off (no
    /// drop), the pose left FIRST, everything given back in the ETS mirror order (EtsTruckSeat.Release 1255-1327), then the official
    /// teleport (DoTeleportToPoint) unless the caller teleports itself.
    /// </summary>
    public sealed class MountSeat
    {
        /// <summary>
        /// Lead (2026-10-04, the user on the Charger: "I am inside it not sitting on it" + "a slider that can be saved in settings"):
        /// WoW puts its rider's ORIGIN on the mount's attachment 0 and the rider's Mount animation (AnimationData 91) holds the pelvis
        /// above that origin - measured (harness probe RiderPelvis, the pelvis pivot through its animated chain, 12 samples): HumanMale
        /// 0.249 m, HumanFemale 0.228 m (other races 0.14-0.42 m). We had put the avatar's hips bone ON attachment 0, so its seat and
        /// thighs sank into the saddle. Now the hips sit RiderPelvis above it (the human mean - a Somnium avatar is human-shaped), plus
        /// Settings > Avatar "Mount seat height" (WowSettings.MountSeatHeight, saved).
        /// </summary>
        public const float RiderPelvis = 0.24f;

        /// <summary>The saddle point the hips go on: attachment 0 + RiderPelvis + the user's seat height.</summary>
        public static Vector3 Raised(Vector3 saddle)
        {
            return saddle + Vector3.up * (RiderPelvis + Mathf.Clamp(WowSettings.MountSeatHeight, -0.30f, 0.50f));
        }

        /// <summary>The sitting pose's hips above its root before they are measured (FlightRide.SeatDrop).</summary>
        public const float HipsFallback = 0.45f;
        /// <summary>Ours (PLAN7 s4.8): measured hips clamped to this range, m.</summary>
        public const float HipsMin = 0.25f, HipsMax = 1.1f;
        /// <summary>Ours: the hips (and the VR eye) are sampled from 10 frames after the pose is in, over 5 frames.</summary>
        public const int MeasureDelayFrames = 10, MeasureFrames = 5;
        /// <summary>Ours: without the client's pose offset, sampling starts this many frames after the first SetPose.</summary>
        public const int MeasureDelayNoOffset = 20;
        /// <summary>Ours: the VR eye above the saddle until measured, m (avatar 1.75 m x 0.7267 sitting head height - 0.45 m hips).</summary>
        public const float EyeFallback = 0.82f, EyeMin = 0.3f, EyeMax = 1.5f;
        /// <summary>Ours: a new eye measurement is eased in over this long (smoothstep), never stepped - the measurement ends at or
        /// after the mount-up blink, and a re-measurement mid-ride has no blink (fixer, review finding 4).</summary>
        public const float EyeEaseSeconds = 0.3f;
        /// <summary>ETS EtsTruckSeat.LeanLimit = 0.45f (line 91); the rest re-taken past 0.7 m (line 1090).</summary>
        public const float LeanLimit = 0.45f, LeanRerest = 0.7f;
        private const string PoseOffsetName = "HeadCustomPoseOffset";

        /// <summary>The log prefix: "Ride" for a mount, "Flight" for the taxi (FlightRide uses this seat too).</summary>
        public string Tag = "Ride";

        public Transform Root { get; private set; }
        public bool Taken { get; private set; }

        // the avatar's own controller (read at takeover, logged)
        public bool CcFound { get; private set; }
        public float CcRadius { get; private set; } = 0.25f;
        public float CcHeight { get; private set; } = 1.82f;
        public float CcStep { get; private set; } = 0.3f;
        public float CcSlope { get; private set; } = 45f;
        public Vector3 CcCenter { get; private set; }
        /// <summary>The avatar controller's centre now (the client's XR driver moves it under the headset; it stays readable while disabled).</summary>
        public Vector3 CcCenterNow { get { return _cc != null ? _cc.center : CcCenter; } }

        /// <summary>Where the avatar stands: the root plus the flat controller-centre offset turned by the root (exactly the offset the
        /// client's PlayerTransformNetworkDriver adds - in VR the controller follows the headset across the room).</summary>
        public Vector3 SyncedRoot
        {
            get
            {
                if (Root == null) return Vector3.zero;
                Vector3 c = Root.rotation * CcCenterNow;
                c.y = 0f;
                return Root.position + c;
            }
        }

        public float Hips { get; private set; } = HipsFallback;
        public bool HipsMeasured { get; private set; }
        /// <summary>The eye above the saddle PlaceHead uses now (eased toward EyeTarget).</summary>
        public float EyeAboveSaddle { get; private set; } = EyeFallback;
        /// <summary>The measured eye (the fallback until measured); EyeAboveSaddle reaches it EyeEaseSeconds after the measurement.</summary>
        public float EyeTarget { get { return _eyeGoal; } }
        public bool EyeMeasured { get; private set; }
        public bool Sitting { get { return _sitting && _poseOk; } }
        /// <summary>Degrees of the client's pose-entry turn undone (VR; the last one), -1 = none seen.</summary>
        public float PoseTurnUndone { get; private set; } = -1f;
        public bool HandsFound { get { return _leftHandOffset != null && _rightHandOffset != null; } }
        public Vector3 LastWritten { get; private set; }
        public float LastYaw { get; private set; }
        /// <summary>A pose re-entry was seen (the measurement restarts); WowMount logs it.</summary>
        public int PoseEntries { get; private set; }

        private CharacterController _cc;
        private bool _ccWas;
        private ISomniumPlayerMotion _motion;
        private ISomniumPlayerAnimation _animation;
        private ISomniumPlayerPoses _poses;
        private ISomniumPlayerAvatar _avatar;
        private Transform _head, _leftHand, _rightHand;
        private Transform _poseOffset, _poseOffsetFrom, _leftHandOffset, _rightHandOffset;
        private bool _poseOk = true, _sitting, _poseReported;
        private Transform _rig;
        private bool _restKnown;
        private Vector3 _headRest, _headShift;
        private Animator _humanoid;
        private Transform _hipsBone;
        private float _nextHumanoidSearch, _humanoidSearchUntil;
        private bool _humanoidMissingLogged;
        private int _firstPoseFrame = -1, _lowFrame = -1, _samples;
        private float _hipsSum, _eyeSum;
        private float _eyeFrom = EyeFallback, _eyeGoal = EyeFallback, _eyeT = 1f;
        private bool _measuring;
        private string _poseFindLine;

        /// <summary>Takes the avatar over (FlightRide.TakeOver + ETS head / hand anchors). False = nothing was taken.</summary>
        public bool TakeOver(Transform root, out string why)
        {
            why = null;
            if (root == null) { why = "no avatar"; return false; }
            Root = root;
            ISomniumPlayer player = null;
            try { player = SomniumBridge.PlayersContainer?.LocalPlayer; } catch (Exception) { player = null; }
            try
            {
                _motion = player?.Features?.Motion;
                _animation = player?.Features?.Animation;
                _poses = player?.Features?.Poses;
                _avatar = player?.Features?.Avatar;
                _head = player?.References?.Body?.Head;
                _leftHand = player?.References?.Body?.LeftHand;
                _rightHand = player?.References?.Body?.RightHand;
            }
            catch (Exception e) { WowLog.Warn(Tag + ": reading the player's features threw " + e.Message); }
            _cc = root.GetComponentInParent<CharacterController>();
            if (_cc == null) _cc = root.GetComponentInChildren<CharacterController>();
            CcFound = _cc != null;
            if (_cc != null)
            {
                CcRadius = _cc.radius;
                CcHeight = _cc.height;
                CcCenter = _cc.center;
                CcStep = _cc.stepOffset;
                CcSlope = _cc.slopeLimit;
            }
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(true);
                    _motion.SetFlyModeDisableState(true);
                    _motion.SetTeleportationDisableState(true);
                    _motion.SetMovementSpeed(0f, 0f, 0f);
                    _motion.SetGravity(0f, 0f);
                    _motion.SetJump(0f, 0f);
                }
                catch (Exception e) { WowLog.Warn(Tag + ": motion settings threw " + e.Message); }
            }
            try { _animation?.SetFallingAnimationDisabled(true); } catch (Exception) { }
            try { _avatar?.SetVRIKLegMovementDisabled(true); } catch (Exception) { }
            if (_cc != null) { _ccWas = _cc.enabled; _cc.enabled = false; }
            _poseOk = true;
            _sitting = true;
            _poseReported = false;
            _poseOffset = _poseOffsetFrom = _leftHandOffset = _rightHandOffset = null;
            _rig = null;
            _restKnown = false;
            _headShift = Vector3.zero;
            _humanoid = null;
            _hipsBone = null;
            _humanoidMissingLogged = false;
            _nextHumanoidSearch = 0f;
            _humanoidSearchUntil = Time.unscaledTime + 10f;
            Hips = HipsFallback;
            HipsMeasured = false;
            EyeAboveSaddle = _eyeFrom = _eyeGoal = EyeFallback;
            _eyeT = 1f;
            EyeMeasured = false;
            PoseTurnUndone = -1f;
            PoseEntries = 0;
            RestartMeasure();
            _firstPoseFrame = -1;
            LastYaw = root.eulerAngles.y;
            LastWritten = root.position;
            Taken = true;
            return true;
        }

        /// <summary>A short summary of the avatar controller for the log.</summary>
        public string ControllerLine
        {
            get
            {
                return CcFound
                    ? "r " + CcRadius.ToString("0.00") + " / h " + CcHeight.ToString("0.00") + " / step " + CcStep.ToString("0.00") + " / slope " + CcSlope.ToString("0") + " / centre " + CcCenter.ToString("F2") + " from the avatar controller"
                    : "no avatar controller found (defaults r 0.25 / step 0.3 / slope 45)";
            }
        }

        /// <summary>Update (-500): the root at the saddle for everything later in the frame (VR: with last frame's head shift).</summary>
        public void WriteRoot(Vector3 saddle, float yaw, bool vr)
        {
            if (Root == null) return;
            saddle = Raised(saddle);   // lead 2026-10-04
            Vector3 p = saddle - Vector3.up * Hips;
            if (vr && _rig == Root) p += _headShift;
            Root.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            LastWritten = p;
            LastYaw = yaw;
        }

        /// <summary>LateUpdate (a): the client's own snap / smooth turn since the Update write (XRBodyTransformer at -205), as a
        /// heading change. False (0) on a frame where the client re-entered the pose (its turn to the head's yaw is not a steer).</summary>
        public bool TryReadClientTurn(out float delta, out bool poseEntry)
        {
            delta = 0f;
            poseEntry = false;
            if (Root == null) return false;
            if (_poseOffset != null && _poseOffset.localRotation != Quaternion.identity) { poseEntry = true; return false; }
            delta = Mathf.DeltaAngle(LastYaw, Root.eulerAngles.y);
            return Mathf.Abs(delta) > 0.01f;
        }

        /// <summary>LateUpdate (b)-(d): SittingStraight held FIRST (VR: the pose entry's re-centring undone), the root written again,
        /// VR PlaceHead onto the eye point / desktop hips on the saddle. Then the hips / eye measurement ticks.</summary>
        public void Seat(Vector3 saddle, float yaw, bool vr)
        {
            if (Root == null) return;
            saddle = Raised(saddle);   // lead 2026-10-04
            bool entered = KeepSitting(vr);
            if (entered)
            {
                PoseEntries++;
                if (vr) _restKnown = false;   // the pose lowered the eyes in the rig: not a lean (ETS)
                if (PoseEntries > 1) RestartMeasure();   // a re-entry (avatar reload / scale change): measure again
            }
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = saddle - Vector3.up * Hips;
            Root.SetPositionAndRotation(p, rot);
            if (vr)
            {
                if (_eyeT < 1f)
                {
                    _eyeT = Mathf.Min(1f, _eyeT + Time.deltaTime / EyeEaseSeconds);
                    EyeAboveSaddle = Mathf.SmoothStep(_eyeFrom, _eyeGoal, _eyeT);
                }
                PlaceHead(saddle + Vector3.up * EyeAboveSaddle);
                _headShift = _rig == Root ? Root.position - p : Vector3.zero;
            }
            else _headShift = Vector3.zero;
            LastWritten = Root.position;
            LastYaw = yaw;
            TickMeasure(vr);
        }

        /// <summary>The fall after a dismount in the air: out of the pose once (the root then follows the proxy, standing).</summary>
        public void LeavePose()
        {
            if (!_sitting) return;
            _sitting = false;
            if (_poses != null)
            {
                try { _poses.SetPose(PoseType.None); }
                catch (Exception e) { WowLog.Warn(Tag + ": leaving the sitting pose threw " + e.GetType().Name + ": " + e.Message); }
            }
            _headShift = Vector3.zero;
        }

        /// <summary>The falling phase's root: standing on the proxy's feet, heading only.</summary>
        public void WriteStanding(Vector3 feet, float yaw)
        {
            if (Root == null) return;
            Root.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            LastWritten = feet;
            LastYaw = yaw;
        }

        /// <summary>
        /// Gives the avatar back: the root written at the spot while the controller is still off (no drop), the pose left FIRST, VRIK
        /// legs, motion, falling animation, the controller as it was, then the teleport unless the caller teleports (placeAvatar false).
        /// </summary>
        public void Release(Vector3 spot, float yaw, bool placeAvatar, out string how)
        {
            how = "none";
            if (!Taken) return;
            Taken = false;
            if (Root != null) Root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));   // no drop: the controller is still off
            if (_poses != null)
            {
                try { _poses.SetPose(PoseType.None); }
                catch (Exception e) { WowLog.Warn(Tag + ": leaving the sitting pose threw " + e.GetType().Name + ": " + e.Message); }
            }
            _sitting = false;
            try { _avatar?.SetVRIKLegMovementDisabled(false); } catch (Exception) { }
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(false);
                    _motion.SetFlyModeDisableState(false);
                    _motion.SetTeleportationDisableState(false);
                    _motion.SetMovementSpeed(1f, 1f, 1f);
                    _motion.SetGravity(1f, 1f);
                    _motion.SetJump(1f, 1f);
                }
                catch (Exception e) { WowLog.Warn(Tag + ": restoring the motion settings threw " + e.Message); }
            }
            try { _animation?.SetFallingAnimationDisabled(false); } catch (Exception) { }
            if (_cc != null) _cc.enabled = _ccWas;
            if (!placeAvatar) how = "none (the caller teleports)";
            else if (Root != null && !WowPlayer.Teleport(spot, yaw, out how))
            {
                // No Bridge (editor / lab): the controller is back on, so move it the documented way (off, write, on).
                bool was = _cc != null && _cc.enabled;
                if (_cc != null) _cc.enabled = false;
                Root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
                if (_cc != null) _cc.enabled = was;
                how = "direct write";
            }
            _cc = null;
            _motion = null; _animation = null; _poses = null; _avatar = null;
            _humanoid = null; _hipsBone = null; _rig = null;
        }

        // ------------------------------------------------------------------------------------------------ the pose (ETS)

        private bool KeepSitting(bool vr)
        {
            if (_poses == null || !_poseOk || !_sitting) return false;
            Camera cam = vr ? WowPlayer.Head() : null;
            Transform eyes = cam != null ? cam.transform : _head;
            FindPoseOffset(eyes);
            Vector3 before = eyes != null ? Root.InverseTransformPoint(eyes.position) : Vector3.zero;
            try { _poses.SetPose(PoseType.SittingStraight); }
            catch (Exception e)
            {
                _poseOk = false;
                WowLog.Warn(Tag + ": the client threw on SetPose (" + e.GetType().Name + ": " + e.Message + "); riding without the sitting pose.");
                return false;
            }
            if (_firstPoseFrame < 0) _firstPoseFrame = Time.frameCount;
            if (vr && eyes != null && _poseOffset != null) UndoPoseRecentre(eyes);
            bool moved = eyes != null && (Root.InverseTransformPoint(eyes.position) - before).sqrMagnitude > 0.03f * 0.03f;
            if (!_poseReported)
            {
                _poseReported = true;
                WowLog.Info(Tag + ": avatar seated (PoseType.SittingStraight, held every frame); " + (_poseFindLine ?? "no pose offset search yet") + ".");
            }
            return moved;
        }

        private void UndoPoseRecentre(Transform eyes)
        {
            if (_poseOffset.localRotation != Quaternion.identity)
            {
                PoseTurnUndone = Quaternion.Angle(Quaternion.identity, _poseOffset.localRotation);
                _poseOffset.localRotation = Quaternion.identity;
                Vector3 inRig = Root.InverseTransformPoint(eyes.position);
                _poseOffset.position -= Root.TransformVector(new Vector3(inRig.x, 0f, inRig.z));
            }
            UndoHandRecentre(_leftHandOffset);
            UndoHandRecentre(_rightHandOffset);
        }

        private void UndoHandRecentre(Transform hand)
        {
            if (hand == null) return;
            if (hand.localRotation != Quaternion.identity) hand.localRotation = Quaternion.identity;
            hand.localPosition = _poseOffset.localPosition;
        }

        private void FindPoseOffset(Transform from)
        {
            if (from == null || from == _poseOffsetFrom) return;
            _poseOffsetFrom = from;
            _poseOffset = null;
            for (Transform t = from; t != null && t != Root; t = t.parent)
            {
                if (t.name == PoseOffsetName) { _poseOffset = t; break; }
            }
            _leftHandOffset = AncestorNamed(_leftHand, "LeftHandCustomPoseOffset", Root);
            _rightHandOffset = AncestorNamed(_rightHand, "RightHandCustomPoseOffset", Root);
            _poseFindLine = _poseOffset != null
                ? "the client's " + PoseOffsetName + " is above '" + from.name + "'; hand offsets " + (_leftHandOffset != null ? "left found" : "left MISSING") + ", " + (_rightHandOffset != null ? "right found" : "right MISSING")
                : "no " + PoseOffsetName + " above '" + from.name + "' (VR keeps the client's re-centring; the hips are sampled " + MeasureDelayNoOffset + " frames after the pose instead)";
            WowLog.Info(Tag + ": seat pose - " + _poseFindLine + ".");
        }

        private static Transform AncestorNamed(Transform from, string name, Transform stop)
        {
            for (Transform t = from; t != null && t != stop; t = t.parent)
            {
                if (t.name == name) return t;
            }
            return null;
        }

        /// <summary>ETS EtsTruckSeat.PlaceHead: the rig shifted so the eyes land on the eye point, the lean since sitting kept (clamped).</summary>
        private void PlaceHead(Vector3 eye)
        {
            Camera head = WowPlayer.Head();
            if (head == null) return;
            if (_rig == null)
            {
                bool carried = head.transform.IsChildOf(Root);
                _rig = carried ? Root : head.transform.root;
                WowLog.Info(Tag + ": VR seat - the camera " + (carried ? "rides on the body" : "is outside the body") + "; moving rig '" + _rig.name + "'.");
                if (!carried) _rig.rotation = Root.rotation;
            }
            else if (_rig != Root) _rig.rotation = Root.rotation;
            Vector3 inRig = _rig.InverseTransformPoint(head.transform.position);
            if (!_restKnown || (inRig - _headRest).sqrMagnitude > LeanRerest * LeanRerest)
            {
                _headRest = inRig;
                _restKnown = true;
            }
            Vector3 lean = Vector3.ClampMagnitude(inRig - _headRest, LeanLimit);
            Vector3 wanted = eye + _rig.TransformVector(lean);
            _rig.position += wanted - head.transform.position;
        }

        private bool PoseLowered { get { return _poseOffset != null && _poseOffset.localPosition.y < -0.05f; } }

        // ------------------------------------------------------------------------------------------------ hips / eye measurement

        private void RestartMeasure()
        {
            _measuring = true;
            _lowFrame = -1;
            _samples = 0;
            _hipsSum = 0f;
            _eyeSum = 0f;
        }

        private void TickMeasure(bool vr)
        {
            if (!_measuring || !_sitting) return;
            if (_hipsBone == null && !FindHumanoid())
            {
                if (Time.unscaledTime > _humanoidSearchUntil && !_humanoidMissingLogged)
                {
                    _humanoidMissingLogged = true;
                    _measuring = false;
                    WowLog.Warn(Tag + ": no humanoid Animator with a Hips bone found for the local avatar within 10 s - hips stay at the fallback " + HipsFallback.ToString("0.00") + " m" +
                                (vr ? ", eye " + EyeFallback.ToString("0.00") + " m above the saddle" : "") + ".");
                }
                return;
            }
            if (_lowFrame < 0)
            {
                bool lowered = _poseOffset != null ? PoseLowered : _firstPoseFrame >= 0 && Time.frameCount >= _firstPoseFrame + MeasureDelayNoOffset - MeasureDelayFrames;
                if (lowered) _lowFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount < _lowFrame + MeasureDelayFrames) return;
            float hy = Root.InverseTransformPoint(_hipsBone.position).y;
            float ey = 0f;
            if (vr)
            {
                Camera head = WowPlayer.Head();
                if (head != null) ey = Root.InverseTransformPoint(head.transform.position).y - hy;
            }
            _hipsSum += hy;
            _eyeSum += ey;
            if (++_samples < MeasureFrames) return;
            _measuring = false;
            float avg = _hipsSum / _samples;
            Hips = Mathf.Clamp(avg, HipsMin, HipsMax);
            HipsMeasured = true;
            string line = Tag + ": hips " + Hips.ToString("0.00") + " m above the root measured (avatar Animator '" + (_humanoid != null ? _humanoid.name : "?") + "', " + _samples + " frames, raw " + avg.ToString("0.000") +
                          (Mathf.Abs(avg - Hips) > 1e-4f ? ", clamped to " + HipsMin.ToString("0.00") + "-" + HipsMax.ToString("0.00") : "") + ")";
            if (vr)
            {
                float e = _eyeSum / _samples;
                _eyeFrom = EyeAboveSaddle;
                _eyeGoal = Mathf.Clamp(e, EyeMin, EyeMax);
                _eyeT = 0f;   // Seat() eases EyeAboveSaddle to it over EyeEaseSeconds
                EyeMeasured = true;
                line += "; eye " + _eyeGoal.ToString("0.00") + " m above the saddle (raw " + e.ToString("0.000") + ", eased in from " + _eyeFrom.ToString("0.00") + " m over " + EyeEaseSeconds.ToString("0.0") + " s)";
            }
            WowLog.Info(line + ".");
        }

        private bool FindHumanoid()
        {
            if (Time.unscaledTime < _nextHumanoidSearch) return false;
            _nextHumanoidSearch = Time.unscaledTime + 1f;
            Animator best = null;
            try
            {
                Animator[] under = Root.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < under.Length && best == null; i++)
                {
                    Animator a = under[i];
                    if (a != null && a.isHuman && a.avatar != null && a.GetBoneTransform(HumanBodyBones.Hips) != null) best = a;
                }
                if (best == null && _head != null)
                {
                    // The client may keep the model outside Body.Root: the humanoid whose head bone is at Body.Head (GearBody.Find).
                    Animator[] all = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
                    float bestD = 0.6f;
                    for (int i = 0; i < all.Length; i++)
                    {
                        Animator a = all[i];
                        if (a == null || !a.isActiveAndEnabled || !a.isHuman || a.avatar == null) continue;
                        Transform h = a.GetBoneTransform(HumanBodyBones.Head);
                        if (h == null || a.GetBoneTransform(HumanBodyBones.Hips) == null) continue;
                        float d = (h.position - _head.position).magnitude;
                        if (d < bestD) { bestD = d; best = a; }
                    }
                }
            }
            catch (Exception e) { WowLog.Warn(Tag + ": searching the avatar's Animator threw " + e.Message); }
            if (best == null) return false;
            _humanoid = best;
            _hipsBone = best.GetBoneTransform(HumanBodyBones.Hips);
            return _hipsBone != null;
        }
    }
}
