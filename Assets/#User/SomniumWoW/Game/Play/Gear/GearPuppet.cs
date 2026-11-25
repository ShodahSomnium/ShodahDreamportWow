// iteration 11 - owner avatar (proposal-avatar.md 6.C GearPuppet): a hidden WoW skeleton that plays the real WoW animations of a
// race/gender, sampled every frame into the WowBodySample the Somnium avatar is posed from (GearPoser).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// The character model of the race/gender (WowModelDb.RaceModelPath) built with NO geoset drawn (bones + M2Animator only) under
    /// "WoW Avatar Puppets" at the WoW root (never rendered; its position does not matter). Plays the loop / one-shot ids GearWowAnim
    /// chose (CrossFade; a missing id follows AnimationData's fallbacks, then Stand), and each LateUpdate (WowGear, order 10000, after
    /// M2Animator's own LateUpdate) reads its mapped joints (M2Humanoid.Find) into M2Humanoid.Sample + the hands' grip frames from the
    /// bones' rotations. Built on first need (at most one puppet build per frame for all avatars), destroyed after 60 s unused (ours).
    /// </summary>
    public sealed class GearPuppet
    {
        public const float UnusedSeconds = 60f;    // ours
        public const string ContainerName = "WoW Avatar Puppets";

        private static Transform _container;
        private static int _buildFrame = -1;
        private static readonly HashSet<string> _preloaded = new HashSet<string>();

        private readonly string _label;
        private GameObject _go;
        private M2Animator _anim;
        private M2HumanoidMap _map;
        private M2HumanoidRef _ref;
        private readonly Transform[] _joint = new Transform[M2Humanoid.JointCount];
        private readonly float[] _pos = new float[M2Humanoid.JointCount * 3];
        private Quaternion _gripBindR = Quaternion.identity, _gripBindL = Quaternion.identity;
        private int _race, _gender, _failedRace = -1, _failedGender = -1;
        private float _usedAt;
        private int _loop = -1, _loopResolved = -1, _shotId = -1;
        private bool _shotLive;

        /// <summary>The body sample of this frame (Valid false before SampleNow found a pose).</summary>
        public WowBodySample Sample;

        public GearPuppet(string label) { _label = label; }

        public bool Ready { get { return _go != null && _map != null && _ref.Valid; } }
        /// <summary>WoW does something now: a held loop, or a one-shot still playing.</summary>
        public bool Active { get { return Ready && (_loop > 0 || (_shotLive && _anim.PlayingOnce)); } }

        /// <summary>Parses the race model on a worker thread (M2Factory.Preload is any-thread) so the first swing does not hitch.</summary>
        public static void Preload(WowFiles files, int race, int gender)
        {
            if (files == null || race <= 0) return;
            string path;
            try { path = WowModelDb.RaceModelPath(files, race, gender == 1 ? 1 : 0); }
            catch (Exception) { return; }
            if (string.IsNullOrEmpty(path)) return;
            lock (_preloaded) { if (!_preloaded.Add(path)) return; }
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { M2Factory.Preload(files, path); }
                catch (Exception e) { WowLog.Warn("Gear: puppet preload of " + path + " threw: " + e.Message); }
            });
        }

        private static Transform Container(Transform gameRoot)
        {
            if (_container != null) return _container;
            if (gameRoot == null) return null;
            Transform found = gameRoot.Find(ContainerName);
            if (found == null)
            {
                var go = new GameObject(ContainerName);
                found = go.transform;
                found.SetParent(gameRoot, false);
            }
            _container = found;
            return _container;
        }

        /// <summary>The skeleton of this race/gender (race 0 = not said: Human, gender &lt; 0: male - ours); false until it exists.</summary>
        public bool Ensure(WowFiles files, int race, int gender, Transform gameRoot)
        {
            _usedAt = Time.unscaledTime;
            if (race <= 0) race = 1;
            gender = gender == 1 ? 1 : 0;
            if (_go != null && race == _race && gender == _gender) return Ready;
            if (race == _failedRace && gender == _failedGender) return false;
            if (files == null || _buildFrame == Time.frameCount) return false;
            _buildFrame = Time.frameCount;
            Destroy();
            _race = race;
            _gender = gender;
            var sw = Stopwatch.StartNew();
            string path = null;
            GameObject go = null;
            try
            {
                path = WowModelDb.RaceModelPath(files, race, gender);
                go = string.IsNullOrEmpty(path) ? null : M2Factory.Build(files, path, new M2BuildOptions { ShowGeoset = id => false, Name = "WoW puppet " + race + "/" + gender + " of " + _label });
            }
            catch (Exception e) { WowLog.Warn("Gear: puppet of " + _label + " (race " + race + ", gender " + gender + ") threw: " + e.Message); go = null; }
            M2Animator anim = go != null ? go.GetComponent<M2Animator>() : null;
            M2HumanoidMap map = anim != null && anim.Model != null ? M2Humanoid.Find(anim.Model) : null;
            if (go == null || anim == null || map == null || !map.Valid)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                _failedRace = race;
                _failedGender = gender;
                WowLog.Warn("Gear: no WoW puppet for " + _label + " (" + (path ?? "race " + race) + (map != null ? ": " + map.Describe() : "") + ") - the simple arm motions stay");
                return false;
            }
            _go = go;
            _anim = anim;
            _map = map;
            Transform c = Container(gameRoot);
            if (c != null) go.transform.SetParent(c, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            Transform[] bones = anim.Bones;
            for (int j = 0; j < M2Humanoid.JointCount; j++)
            {
                int b = map.Bone(j);
                if (b < 0) b = map.Pelvis;
                _joint[j] = b >= 0 && b < bones.Length ? bones[b] : null;
            }
            // The reference, in the frame and units the bones are built in (metres, x right / y up / z forward): Stand frame 0 + bind.
            M2Model m = anim.Model;
            var stand = new float[M2Humanoid.JointCount * 3];
            var bind = new float[M2Humanoid.JointCount * 3];
            int seq = M2Pose.RestSequence(m);
            M2Humanoid.PoseJoints(m, map, seq >= 0 ? M2Pose.Compute(m, seq, 0, 0) : null, WowSpace.MetresPerYard, stand);
            M2Humanoid.PoseJoints(m, map, null, WowSpace.MetresPerYard, bind);
            _ref = M2Humanoid.Reference(map, stand, bind);
            _gripBindR = BindGrip(bind, M2Humanoid.JRElbow, M2Humanoid.JRHand);
            _gripBindL = BindGrip(bind, M2Humanoid.JLElbow, M2Humanoid.JLHand);
            _loop = -1;
            _shotLive = false;
            WowLog.Info("Gear: puppet " + path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1).Replace(".m2", "") + " for " + _label + ": " + bones.Length + " bones, map " + map.Describe() +
                        ", leg " + _ref.LegLength.ToString("0.00") + " m, arm " + _ref.ArmLength.ToString("0.00") + " m, in " + sw.ElapsedMilliseconds + " ms");
            return Ready;
        }

        /// <summary>The bind grip frame (puppet space): forward = little -> index across the knuckles (the model's forward with the
        /// palms toward the thighs in the bind A-pose; ours, approximate), up = -(wrist -> knuckles) (along the forearm).</summary>
        private static Quaternion BindGrip(float[] bind, int elbow, int hand)
        {
            var e = new Vector3(bind[elbow * 3], bind[elbow * 3 + 1], bind[elbow * 3 + 2]);
            var h = new Vector3(bind[hand * 3], bind[hand * 3 + 1], bind[hand * 3 + 2]);
            Vector3 k = (h - e).normalized;
            if (k.sqrMagnitude < 1e-6f) k = Vector3.down;
            Vector3 f = Vector3.forward - Vector3.Dot(Vector3.forward, k) * k;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.right;
            return Quaternion.LookRotation(f.normalized, -k);
        }

        /// <summary>This frame's ids: a fresh one-shot starts (CrossFade, back to the loop when it ends), else the loop is held.</summary>
        public void Play(int loop, int oneShot, bool fresh)
        {
            if (_anim == null) return;
            if (fresh && oneShot > 0)
            {
                _anim.HoldAtEnd = false;
                _anim.Speed = 1f;
                _anim.CrossFade(oneShot, false);
                _shotId = _anim.PlayingAnimationId;
                _shotLive = true;
            }
            if (_shotLive && _anim.PlayingOnce && _anim.PlayingAnimationId == _shotId) return;
            _shotLive = false;
            int want = loop > 0 ? loop : 0;
            if (_loop != want || (!_anim.PlayingOnce && _anim.PlayingAnimationId != _loopResolved))
            {
                _anim.HoldAtEnd = false;
                _anim.CrossFade(want, true);
                _loop = want;
                _loopResolved = _anim.PlayingAnimationId;
            }
        }

        /// <summary>After the puppet's M2Animator ran this frame: its joints -> Sample (+ the grips from the hand bones' rotations).</summary>
        public void SampleNow()
        {
            Sample.Valid = false;
            if (!Ready) return;
            Transform root = _go.transform;
            for (int j = 0; j < M2Humanoid.JointCount; j++)
            {
                Transform t = _joint[j];
                Vector3 p = t != null ? root.InverseTransformPoint(t.position) : Vector3.zero;
                if (j == M2Humanoid.JHeadFwd && t != null) p = root.InverseTransformPoint(t.position + t.rotation * (Vector3.forward * (0.1f * WowSpace.MetresPerYard)));
                _pos[j * 3] = p.x; _pos[j * 3 + 1] = p.y; _pos[j * 3 + 2] = p.z;
            }
            Sample = M2Humanoid.Sample(_map, _pos, _ref);
            if (!Sample.Valid) return;
            // the chest frame of the puppet (as M2Humanoid's) and the hands' grips in it
            Vector3 pelvis = P(M2Humanoid.JPelvis), neck = P(M2Humanoid.JNeck);
            Vector3 up = (neck - pelvis).normalized;
            Vector3 across = P(M2Humanoid.JRShoulder) - P(M2Humanoid.JLShoulder);
            Vector3 right = across - Vector3.Dot(across, up) * up;
            if (up.sqrMagnitude < 1e-6f || right.sqrMagnitude < 1e-8f) return;
            Quaternion chest = Quaternion.LookRotation(Vector3.Cross(right.normalized, up), up);
            Quaternion inv = Quaternion.Inverse(chest), rootInv = Quaternion.Inverse(root.rotation);
            Transform rh = _joint[M2Humanoid.JRHand], lh = _joint[M2Humanoid.JLHand];
            if (rh != null) Sample.RGrip = Q(inv * (rootInv * rh.rotation) * _gripBindR);
            if (lh != null) Sample.LGrip = Q(inv * (rootInv * lh.rotation) * _gripBindL);
        }

        private Vector3 P(int j) { return new Vector3(_pos[j * 3], _pos[j * 3 + 1], _pos[j * 3 + 2]); }
        private static M2Quat Q(Quaternion q) { return new M2Quat(q.x, q.y, q.z, q.w); }

        /// <summary>Destroyed when unused for UnusedSeconds (the caller ticks it every frame).</summary>
        public void Tick()
        {
            if (_go != null && Time.unscaledTime - _usedAt > UnusedSeconds)
            {
                WowLog.Info("Gear: puppet of " + _label + " unused for " + UnusedSeconds.ToString("0") + " s - destroyed");
                Destroy();
            }
        }

        public void Destroy()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _anim = null;
            _map = null;
            _ref = default(M2HumanoidRef);
            Sample = default(WowBodySample);
            for (int j = 0; j < _joint.Length; j++) _joint[j] = null;
            _loop = -1;
            _shotLive = false;
        }
    }
}
