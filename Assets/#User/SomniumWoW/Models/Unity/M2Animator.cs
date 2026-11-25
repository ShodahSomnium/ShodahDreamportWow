using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Plays an M2's own animations on the bones built by M2Factory.Build (CPU sampling in LateUpdate, linear / slerp
    /// between keys, step where the track says so, global sequences on their own clock). Bone i's GameObject stands at its
    /// pivot relative to its parent's pivot; a frame sets localPosition = pivot - parentPivot + translation, localRotation,
    /// localScale, which equals M2's T(pivot) T(t) R S T(-pivot) (see the Models report). Everything converts through
    /// WowSpace. Billboard bones: spherical (flag 0x8) turn their +X (Unity +z) to the camera; cylindrical (0x10 / 0x20 / 0x40,
    /// lock X / Y / Z) turn only about that axis of their own frame (lock Z: +X to the camera; lock X / Y: +Z to the camera).
    /// Animation ids are AnimationData.dbc ids: 0 Stand, 4 Walk, 5 Run, 1 Death, 17 Attack1H, 18 Attack2H, 26 Ready1H,
    /// 60 EmoteTalk, 66 EmoteBow, 67 EmoteWave, 69 EmoteDance (the Models report lists more).
    /// </summary>
    public sealed class M2Animator : MonoBehaviour
    {
        M2Model _model;
        WowFiles _files;
        Transform[] _bones = new Transform[0];
        Vector3[] _basePos = new Vector3[0];
        bool[] _animated = new bool[0];
        int[] _billboards = new int[0];
        readonly Dictionary<int, Transform> _attachments = new Dictionary<int, Transform>();
        int _seq = -1, _dataSeq = -1, _animId = -1;
        float _timeMs;
        long _globalMs;
        bool _loop = true;
        bool _heldRaised;
        readonly HashSet<int> _warned = new HashSet<int>();

        /// <summary>Playback speed multiplier (1 = normal).</summary>
        public float Speed { get; set; } = 1f;

        /// <summary>The parsed model (null before M2Factory initialises the animator).</summary>
        public M2Model Model { get { return _model; } }

        /// <summary>AnimationData id of the playing animation (-1 none).</summary>
        public int PlayingAnimationId { get { return _animId; } }

        /// <summary>The bone transforms (index = M2 bone index).</summary>
        public Transform[] Bones { get { return _bones; } }

        /// <summary>Index of the sequence whose keys play now (aliases resolved), -1 none. Effects sample their tracks here.</summary>
        public int DataSequence { get { return _dataSeq; } }

        /// <summary>Milliseconds into the playing sequence.</summary>
        public int TimeMs { get { return (int)_timeMs; } }

        /// <summary>Milliseconds on the global-sequence clock.</summary>
        public long GlobalMs { get { return _globalMs; } }

        /// <summary>Duration (ms) of the playing sequence (0 none).</summary>
        public int SequenceDuration { get { return _model != null && _seq >= 0 && _seq < _model.Sequences.Length ? _model.Sequences[_seq].Duration : 0; } }

        /// <summary>True while a one-shot (loop = false) animation plays; false once it has returned to Stand.</summary>
        public bool PlayingOnce { get { return !_loop; } }

        /// <summary>
        /// Holds the playing sequence at its last frame instead of looping / returning to Stand (one-shot spell effects keep
        /// their last pose while their particles die out). Cleared by the next Play.
        /// </summary>
        public bool HoldAtEnd { get; set; }

        /// <summary>Raised when a loop = false animation reached its end (before it returns to Stand / holds).</summary>
        public event System.Action Finished;

        internal void Init(M2Model model, WowFiles files, Transform[] bones, Dictionary<int, Transform> attachments)
        {
            _model = model;
            _files = files;
            _bones = bones ?? new Transform[0];
            _attachments.Clear();
            if (attachments != null) foreach (var kv in attachments) _attachments[kv.Key] = kv.Value;
            int n = _bones.Length;
            _basePos = new Vector3[n];
            _animated = new bool[n];
            var bb = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var b = model.Bones[i];
                Vector3 pivot = WowSpace.PointToUnity(b.Pivot.X, b.Pivot.Y, b.Pivot.Z);
                Vector3 parentPivot = Vector3.zero;
                if (b.Parent >= 0) { var p = model.Bones[b.Parent].Pivot; parentPivot = WowSpace.PointToUnity(p.X, p.Y, p.Z); }
                _basePos[i] = pivot - parentPivot;
                _animated[i] = !b.Translation.IsEmpty || !b.Rotation.IsEmpty || !b.Scale.IsEmpty;
                if (b.IsBillboard) bb.Add(i);
            }
            _billboards = bb.ToArray();
            Play(0, true);
        }

        /// <summary>True when the model has this AnimationData id with loaded keys.</summary>
        public bool Has(int animationId)
        {
            if (_model == null) return false;
            int s = _model.FindSequence(animationId);
            if (s < 0) return false;
            s = _model.ResolveAlias(s);
            return s >= 0 && _model.Sequences[s].DataLoaded;
        }

        // ---- Iteration 7 (Body, PLAN7 s5.4): cross-fades. While _fading, every animated bone is the blend of the FROM pose and the
        // new sequence: position lerp, rotation slerp, scale lerp, weight = elapsed / blend (linear, real time). The FROM pose is the
        // old sequence still sampled at its own running time ("live"), or - when a cross-fade interrupts a running one - a snapshot of
        // the bones at that moment ("frozen"), so a blend never pops. Buffers are sized once, on the first CrossFade of this
        // animator (animators that never cross-fade - doodads, NPCs - pay nothing). Play() cancels a blend and snaps, as before.
        bool _fading, _fromLive, _fromLoop, _crossFaded;
        int _fromSeq = -1, _fromDataSeq = -1;
        float _fromTimeMs, _blendMs, _blendElapsedMs;
        Vector3[] _snapPos, _snapScale;
        Quaternion[] _snapRot;

        /// <summary>Iteration 7 (PLAN7 s3.6; owner Body): like Play, but blends every bone from the pose now to the new sequence
        /// over blendMs (-1 = the new sequence's own BlendTime; 0 = snap). Play is unchanged, so every existing caller keeps
        /// snapping. Asking for the sequence that already plays changes nothing but the loop flag (no restart). A one-shot
        /// (loop = false) started here returns to Stand with Stand's blend time instead of snapping. No allocation per frame.</summary>
        public void CrossFade(int animationId, bool loop, int blendMs = -1)
        {
            if (_model == null || _model.Sequences.Length == 0) return;
            int seq, id;
            if (!Resolve(animationId, true, out seq, out id)) return;
            if (blendMs < 0) blendMs = _model.Sequences[seq].BlendTime;
            if (seq == _seq && _dataSeq >= 0)
            {
                // the same sequence: keep it running (a cross-fade to itself would restart it)
                _loop = loop;
                _crossFaded = true;
                return;
            }
            if (blendMs <= 0 || _dataSeq < 0 || _bones.Length == 0)
            {
                Start(seq, id, loop);
                _fading = false;
                _crossFaded = true;
                Apply();
                return;
            }
            int n = _bones.Length;
            if (_snapPos == null || _snapPos.Length != n) { _snapPos = new Vector3[n]; _snapRot = new Quaternion[n]; _snapScale = new Vector3[n]; }
            if (_fading && _blendElapsedMs < _blendMs)
            {
                // interrupting a blend: start from exactly what is drawn now
                for (int i = 0; i < n; i++)
                {
                    var t = _bones[i];
                    if (t == null) continue;
                    _snapPos[i] = t.localPosition; _snapRot[i] = t.localRotation; _snapScale[i] = t.localScale;
                }
                _fromLive = false;
            }
            else
            {
                _fromLive = true;
                _fromSeq = _seq;
                _fromDataSeq = _dataSeq;
                _fromTimeMs = _timeMs;
                _fromLoop = _loop;
            }
            Start(seq, id, loop);
            _fading = true;
            _crossFaded = true;
            _blendMs = blendMs;
            _blendElapsedMs = 0f;
            Apply();
        }

        /// <summary>Iteration 7 (PLAN7 s3.6; owner Body): the BlendTime (ms, sequence record +28) of the sequence Play would pick for
        /// animationId (after AnimationData's fallbacks), 0 when none.</summary>
        public int BlendTimeMs(int animationId)
        {
            int seq, id;
            if (_model == null || !Resolve(animationId, false, out seq, out id)) return 0;
            return _model.Sequences[seq].BlendTime;
        }

        /// <summary>The sequence Play picks for an id: AnimationData's fallback chain, then Stand, then the first loaded sequence.</summary>
        bool Resolve(int animationId, bool log, out int seq, out int id)
        {
            id = animationId; seq = -1;
            if (_model == null || _model.Sequences.Length == 0) return false;
            for (int hop = 0; hop < 6 && seq < 0; hop++)
            {
                if (Has(id)) { seq = _model.FindSequence(id); break; }
                int fb = _files != null ? WowModelDb.AnimationFallback(_files, id) : -1;
                if (fb < 0 || fb == id) break;
                id = fb;
            }
            if (seq < 0 && Has(0)) { id = 0; seq = _model.FindSequence(0); }
            if (seq < 0)
            {
                for (int i = 0; i < _model.Sequences.Length; i++) if (_model.Sequences[i].DataLoaded && !_model.Sequences[i].IsAlias) { seq = i; id = _model.Sequences[i].AnimId; break; }
            }
            if (seq < 0) return false;
            if (log && id != animationId && _warned.Add(animationId))
                WowLog.Info("M2Animator: " + _model.Path + " has no animation " + animationId + ", playing " + id);
            return true;
        }

        void Start(int seq, int id, bool loop)
        {
            _seq = seq;
            _dataSeq = _model.ResolveAlias(seq);
            _animId = id;
            _loop = loop;
            _timeMs = 0f;
            _heldRaised = false;
        }

        /// <summary>
        /// Plays an animation by AnimationData.dbc id. Missing ids follow AnimationData's fallback chain, then Stand, then
        /// the first sequence. loop = false plays it once and returns to Stand.
        /// </summary>
        public void Play(int animationId, bool loop)
        {
            if (_model == null || _model.Sequences.Length == 0) return;
            int seq, id;
            if (!Resolve(animationId, true, out seq, out id)) return;
            _fading = false;
            _crossFaded = false;
            Start(seq, id, loop);
            Apply();
        }

        /// <summary>
        /// Lead 2026-10-10 (immersive bow): holds an animation at timeMs (Speed 0 keeps it there; the next Play / Speed resumes) and
        /// applies the bones now - the bow's BowPull bent to the draw.
        /// </summary>
        public void Scrub(int animationId, float timeMs)
        {
            if (_model == null || _model.Sequences.Length == 0) return;
            if (_animId != animationId || _loop) Play(animationId, false);
            if (_animId != animationId) return;
            Speed = 0f;
            HoldAtEnd = true;
            _fading = false;
            _timeMs = Mathf.Clamp(timeMs, 0f, Mathf.Max(0, SequenceDuration - 1));
            Apply();
        }

        bool[] _closed = new bool[0];
        int _closedSeq = -1;

        /// <summary>
        /// Closes (or opens) a hand around a held item: the finger bones (key bones 8-12 right, 13-17 left, WMV's key-bone
        /// table) and their children take the HandsClosed (15) pose instead of the playing animation, as WoW does.
        /// </summary>
        public void CloseHand(bool right, bool closed)
        {
            if (_model == null) return;
            int s = _model.FindSequence(15);
            if (s >= 0) s = _model.ResolveAlias(s);
            if (s < 0 || !_model.Sequences[s].DataLoaded) return;
            _closedSeq = s;
            if (_closed.Length != _bones.Length) _closed = new bool[_bones.Length];
            // As WoW Model Viewer 0.7 does it: only the five finger key bones take the fist pose (their child segments keep
            // the playing animation, relative to the closed key bone). Measured: no finger key bone is an ancestor of the
            // hand attachments 1/2 on any of the 20 player models (probe ModelsHands), so the weapon itself never turns.
            int first = right ? 8 : 13;
            for (int k = first; k < first + 5; k++)
            {
                if (k >= _model.KeyBoneLookup.Length) break;
                int bi = _model.KeyBoneLookup[k];
                if (bi < 0 || bi >= _bones.Length) continue;
                _closed[bi] = closed;
                _animated[bi] = true;
            }
            Apply();
        }

        /// <summary>The transform of an M2 attachment point by attachment id (1 right hand, 2 left hand, 0 shield / left wrist,
        /// 11 helmet, 26/27 back sheaths, 32/33 hip sheaths; see the Models report), or null.</summary>
        public Transform Attachment(int attachmentId)
        {
            Transform t;
            return _attachments.TryGetValue(attachmentId, out t) ? t : null;
        }

        void LateUpdate()
        {
            if (_model == null || _dataSeq < 0) return;
            float dt = Time.deltaTime * 1000f;
            _globalMs += (long)(dt * Mathf.Max(0f, Speed));
            _timeMs += dt * Speed;
            if (_fading)
            {
                // the old sequence keeps running during the blend; the blend itself runs on real time
                _blendElapsedMs += dt;
                if (_blendElapsedMs >= _blendMs) _fading = false;
                else if (_fromLive && _fromSeq >= 0 && _fromSeq < _model.Sequences.Length)
                {
                    _fromTimeMs += dt * Speed;
                    int fdur = Mathf.Max(1, _model.Sequences[_fromSeq].Duration);
                    if (_fromTimeMs >= fdur) _fromTimeMs = _fromLoop ? _fromTimeMs % fdur : fdur - 1;
                    if (_fromTimeMs < 0f) _fromTimeMs = 0f;
                }
            }
            int dur = Mathf.Max(1, _model.Sequences[_seq].Duration);
            if (_timeMs >= dur)
            {
                if (_loop) _timeMs %= dur;
                else if (HoldAtEnd) { _timeMs = dur - 1; if (!_heldRaised) { _heldRaised = true; var h0 = Finished; if (h0 != null) h0(); } }
                else if (_crossFaded)
                {
                    // a one-shot started by CrossFade ends in a blend to Stand (its last frame is the FROM pose), not a snap
                    _timeMs = dur - 1;
                    var h1 = Finished; if (h1 != null) h1();
                    if (_crossFaded && !_loop && _seq >= 0 && (int)_timeMs >= dur - 1) CrossFade(0, true, -1);
                    return;
                }
                else { var h = Finished; if (h != null) h(); Play(0, true); return; }
            }
            if (_timeMs < 0f) _timeMs = 0f;
            Apply();
        }

        void Apply()
        {
            int t = (int)_timeMs;
            var bones = _model.Bones;
            bool fading = _fading && _blendMs > 0f;
            float w = fading ? Mathf.Clamp01(_blendElapsedMs / _blendMs) : 1f;
            int ft = (int)_fromTimeMs;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (!_animated[i] || _bones[i] == null) continue;
                var b = bones[i];
                int seq = _dataSeq, time = t;
                bool closed = _closedSeq >= 0 && i < _closed.Length && _closed[i];
                if (closed) { seq = _closedSeq; time = 0; }
                M2Vec3 v; M2Quat q;
                Vector3 pos = _basePos[i];
                if (M2Sampler.Sample(b.Translation, seq, time, _model.GlobalSequences, _globalMs, out v)) pos += WowSpace.PointToUnity(v.X, v.Y, v.Z);
                Quaternion rot = M2Sampler.Sample(b.Rotation, seq, time, _model.GlobalSequences, _globalMs, out q)
                    ? WowSpace.RotationToUnity(q.X, q.Y, q.Z, q.W) : Quaternion.identity;
                Vector3 scl = M2Sampler.Sample(b.Scale, seq, time, _model.GlobalSequences, _globalMs, out v)
                    ? new Vector3(v.Y, v.Z, v.X) : Vector3.one;
                if (fading && !closed)
                {
                    Vector3 fp, fs; Quaternion fr;
                    if (_fromLive)
                    {
                        fp = _basePos[i];
                        if (M2Sampler.Sample(b.Translation, _fromDataSeq, ft, _model.GlobalSequences, _globalMs, out v)) fp += WowSpace.PointToUnity(v.X, v.Y, v.Z);
                        fr = M2Sampler.Sample(b.Rotation, _fromDataSeq, ft, _model.GlobalSequences, _globalMs, out q)
                            ? WowSpace.RotationToUnity(q.X, q.Y, q.Z, q.W) : Quaternion.identity;
                        fs = M2Sampler.Sample(b.Scale, _fromDataSeq, ft, _model.GlobalSequences, _globalMs, out v)
                            ? new Vector3(v.Y, v.Z, v.X) : Vector3.one;
                    }
                    else { fp = _snapPos[i]; fr = _snapRot[i]; fs = _snapScale[i]; }
                    pos = Vector3.LerpUnclamped(fp, pos, w);
                    rot = Quaternion.Slerp(fr, rot, w);
                    scl = Vector3.LerpUnclamped(fs, scl, w);
                }
                _bones[i].localPosition = pos;
                _bones[i].localRotation = rot;
                _bones[i].localScale = scl;
            }
            if (_billboards.Length > 0)
            {
                var cam = FindCamera();
                if (cam != null)
                {
                    Vector3 cp = cam.transform.position, up = cam.transform.up;
                    for (int k = 0; k < _billboards.Length; k++)
                    {
                        int bi = _billboards[k];
                        var bt = _bones[bi];
                        if (bt == null) continue;
                        Vector3 to = cp - bt.position;
                        if (to.sqrMagnitude <= 1e-8f) continue;
                        uint fl = _model.Bones[bi].Flags & 0x78u;
                        if ((fl & 0x8u) != 0) { bt.rotation = Quaternion.LookRotation(to, up); continue; }   // spherical (unchanged)
                        // vfxanchor fix 2026-10-04 (the user: Flash of Light "going to hip ... look bad"): CYLINDRICAL billboards turn only
                        // about their locked axis (wowdev bone flags 0x10 / 0x20 / 0x40 = cylindrical_billboard_lock_x / _y / _z;
                        // WebWowViewerCpp AnimationManager::calcBoneMatrix "case 0x40": keeps the bone's Z column, rebuilds Y and X
                        // around it). Before, all of them turned fully to the camera like a spherical one: HolyLight_Low_Head's root
                        // bone (0x40, pivot 2.86 yd below the head) tipped its light column by the camera's elevation into a long
                        // slanted streak from the hip. The locked axis is the bone's own frame this frame (parent x its animated
                        // rotation; identity when not animated - its localRotation still holds last frame's billboard).
                        Quaternion baseRot = (bt.parent != null ? bt.parent.rotation : Quaternion.identity) * (_animated[bi] ? bt.localRotation : Quaternion.identity);
                        if ((fl & 0x40u) != 0)
                        {
                            Vector3 ax = baseRot * Vector3.up;                       // WoW +Z
                            Vector3 f = to - Vector3.Dot(to, ax) * ax;               // +X (Unity +z) to the camera about +Z
                            bt.rotation = f.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(f, ax) : baseRot;
                        }
                        else if ((fl & 0x10u) != 0)
                        {
                            Vector3 ax = baseRot * Vector3.forward;                  // WoW +X
                            Vector3 u = to - Vector3.Dot(to, ax) * ax;               // +Z (Unity +y) to the camera about +X
                            bt.rotation = u.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(ax, u) : baseRot;
                        }
                        else
                        {
                            Vector3 ax = baseRot * Vector3.right;                    // WoW -Y (lock Y = 0x20)
                            Vector3 u = to - Vector3.Dot(to, ax) * ax;               // +Z (Unity +y) to the camera about Y
                            bt.rotation = u.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(Vector3.Cross(ax, u.normalized), u) : baseRot;
                        }
                    }
                }
            }
        }

        static Camera _cam;
        static int _camFrame = -1;

        /// <summary>The rendering camera, re-found once per frame (Camera.main is not reliable in the client).</summary>
        internal static Camera FindCamera()
        {
            if (_camFrame == Time.frameCount && _cam != null) return _cam;
            _camFrame = Time.frameCount;
            var c = Camera.main;
            if (c == null || !c.isActiveAndEnabled)
            {
                c = null;
                foreach (var x in Camera.allCameras)
                    if (x.isActiveAndEnabled && x.targetTexture == null && (c == null || x.depth > c.depth)) c = x;
            }
            _cam = c;
            return c;
        }
    }
}
