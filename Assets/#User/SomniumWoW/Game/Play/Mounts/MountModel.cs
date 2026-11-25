using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Mounts
{
    /// <summary>
    /// Iteration 7 (lead contract PLAN7 s3.7; owner Body): the drawn local mount.
    /// Prepare (a mount CastStarted) asks the entity art worker for the display's textures (WowEntityViews.PrewarmDisplay) every
    /// frame until they are ready, then builds it with WowCreatures.Build (at most one mount build per frame, over all
    /// MountModels), hidden (renderers off, the animator running so the seat can be measured). Show (mount-up) builds at once when
    /// nothing was prepared (logged), rebuilds when the display differs (logged), and unhides.
    /// Objects: "WoW Mount &lt;display&gt;" = a direct child of the WoW Game object (WowOrigin shifts it), scale 1, heading only;
    /// the creature model is its child with the display scale, so SeatLocal (Root space) is in metres INCLUDING the display scale
    /// and Root.TransformPoint(SeatLocal) == Root.position + Root.rotation * SeatLocal. Every GameObject is on layer 2 (neither
    /// client pointer ray hits it, the ride's proxy ignores it).
    /// Seat (PLAN7 s4.3): attachment 0 in bind pose at the build, then measured ONCE on the animated model 2 frames later in
    /// mount space (FlightRide.MeasureSeat) and fixed from then on - never the bobbing animated attachment. No attachment 0 ->
    /// MountHeight x display scale; neither -> 80 % of the model's bounds height (logged). The DRAWN model (Root's child) is shifted
    /// per playing sequence so the saddle's mean sits on that fixed seat (see "per-animation saddle offset" below); Root is not.
    /// Frames: nothing here needs an Update: a private Application.onBeforeRender pump runs only while something is pending
    /// (prepare, seat measurement) and unsubscribes itself.
    /// </summary>
    public sealed class MountModel
    {
        // ---- ours (logged once)
        private const float PrewarmPriority = 0f;           // PrewarmDisplay priority = distance squared: the rider's own mount comes first
        private const int SeatMeasureFrames = 2;            // PLAN7 s4.3: measure 2 frames after the build
        private const float BoundsSeatFraction = 0.8f;      // PLAN7 s5.4: seat at 80 % of the bounds height when nothing else is known
        private const int MountLayer = 2;                   // PLAN7 s5.4 / s4.9: Ignore Raycast, no client pointer ray hits it
        private static readonly Vector3 HiddenAt = new Vector3(0f, -500f, 0f);   // where an unplaced prepared model waits (never (0,0,0):
                                                                                  // WowOrigin would take a childful object there for a container)

        private static int _lastBuildFrame = -1;
        private static bool _constantsLogged;

        private GameObject _root, _model;
        private M2Animator _anim;
        private Renderer[] _renderers = new Renderer[0];
        private int _displayId, _pendingDisplay;
        private bool _visible, _seatMeasured, _pumping;
        private int _seatMeasureFrame;
        private Vector3 _seatLocal = new Vector3(0f, 1.7f, 0f);
        private string _seatSource = "none";
        private float _mountHeightYards;
        private RideGaitModel _gait;
        private int _requestedAnim = -1, _requestedPlayed = -1;
        private bool _requestedLoop = true;
        private float _prepareStarted;
        private UnityEngine.Events.UnityAction _pump;
        private bool _childShiftLogged, _hideLogged;
        private float _scale = 1f;

        // ---- per-animation saddle offset (ours, measured need: P/harness/Probes/Mount7/data/out/seatbob.txt and the body lab: the
        // gryphon's saddle is 1.2 m up standing but ~2.0 m in Fly / Hover, the proto-drake's 3.2 m standing but ~5.2 m hovering,
        // so one fixed seat leaves the rider inside the body in the air). The rider keeps the ONE fixed seat (PLAN7 s4.3, VR
        // comfort); instead the drawn model is shifted by (seat - the saddle's mean over the playing sequence), computed from the
        // model's own keys (attachment 0 through its bone chain, 24 samples a cycle), blended over the new sequence's BlendTime
        // like the pose itself. On the ground only the horizontal part is used (hooves stay on the ground); in the air and in
        // water (Fly 135, Hover 193, Swim* 41-45) the vertical part too, never pushing the model's feet below the ground under it.
        private const int SeatMeanSamples = 24;             // ours
        private const float MaxSeatOffset = 5f;             // ours: m, a larger computed offset is ignored (logged); the largest over all 284 mount
                                                            // displays is 4.63 m (Nether Ray Fly 135, probe MountSeatMeans)
        private readonly Dictionary<int, Vector3> _meanSeat = new Dictionary<int, Vector3>();
        private readonly HashSet<int> _offsetLogged = new HashSet<int>();
        private Vector3 _offFrom, _offTo, _offNow, _childAt;
        private float _offT, _offDur;
        private int _offDataSeq = -2;
        private int _offFrame = -1;   // the offset blend advances once per rendered frame (Place runs in Update AND LateUpdate)

        public bool Built { get { return _model != null; } }

        public int DisplayId { get { return _model != null ? _displayId : 0; } }

        public Transform Root { get { return _model != null && _root != null ? _root.transform : null; } }

        public M2Animator Animator { get { return _model != null ? _anim : null; } }

        public Vector3 SeatLocal { get { return _seatLocal; } }   // mount space, fixed after the one measurement

        /// <summary>Lead fix 2026-10-05 (the user, desktop: "make the character ride the gryphon properly, not just floating above it, same
        /// for mounts too"): the saddle (attachment 0) where the animated model draws it NOW (world), so a desktop rider moves with the
        /// gait's bob instead of hanging over a fixed mean seat. False until the seat was measured / without a mount point.</summary>
        public bool TryLiveSeat(out Vector3 world)
        {
            world = Vector3.zero;
            if (_model == null || _anim == null || !_seatMeasured) return false;
            Transform att = _anim.Attachment(0);
            if (att == null) return false;
            world = att.position;
            return true;
        }

        public float MountHeightYards { get { return _mountHeightYards; } }                    // CreatureModelData.MountHeight (field 16) as stored = AC's collision-height input

        public RideGaitModel Gait { get { return _gait; } }

        /// <summary>On a mount CastStarted: ask the art worker, build when ready (hidden). Calling it again for the same display is free.</summary>
        public void Prepare(int displayId)
        {
            LogConstantsOnce();
            if (displayId <= 0) { WowLog.Info("Mount: Prepare(" + displayId + ") ignored - no display"); return; }
            if (_model != null && _displayId == displayId) return;              // already built (hidden or shown)
            if (_pendingDisplay == displayId) { Pump(); return; }
            if (_model != null && _visible)
            {
                WowLog.Info("Mount: Prepare(" + displayId + ") while display " + _displayId + " is shown - kept; Show() rebuilds if asked");
                return;
            }
            if (_model != null) DestroyObjects();
            _pendingDisplay = displayId;
            _prepareStarted = Time.realtimeSinceStartup;
            WowLog.Info("Mount: preparing display " + displayId + " (asking the art worker, building when its textures are ready)");
            StartPump();
            Pump();
        }

        /// <summary>Mount-up: build now if not prepared (logged), rebuild when the display differs (logged), unhide.</summary>
        public void Show(int displayId)
        {
            LogConstantsOnce();
            if (displayId <= 0) { WowLog.Warn("Mount: Show(" + displayId + ") - no display to show"); return; }
            if (_model != null && _displayId != displayId)
            {
                WowLog.Info("Mount: display " + _displayId + " prepared, server says " + displayId + " - rebuilt");
                DestroyObjects();
            }
            if (_model == null)
            {
                bool wasPending = _pendingDisplay == displayId;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Build(displayId);
                if (_model == null) { WowLog.Warn("Mount: display " + displayId + " could not be built - no mount drawn"); return; }
                WowLog.Info("Mount: display " + displayId + (wasPending ? " was still waiting for its art" : " was not prepared") + " at mount-up - built now ("
                    + sw.ElapsedMilliseconds + " ms)");
            }
            _pendingDisplay = 0;
            _visible = true;
            _requestedAnim = -1;
            _requestedPlayed = -1;
            SetRendering(true);
            if (!_seatMeasured) StartPump();
        }

        /// <summary>Heading only: the mount's feet and its Unity yaw. The Root goes exactly there; the drawn model under it carries
        /// the per-animation saddle offset (see UpdateOffset), so the rider on the fixed SeatLocal never moves with the animation.</summary>
        public void Place(Vector3 feet, float yawDeg)
        {
            if (_root == null || _model == null) return;
            _root.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yawDeg, 0f));
            if (!_seatMeasured) MeasureSeat();
            // Keep the model glued to its root: if anything shifted the child (a floating-origin rebase that took the root for a
            // container while it was unplaced), it is put back - the root is the one that moves.
            var mt = _model.transform;
            if ((mt.localPosition - _childAt).sqrMagnitude > 1e-4f || mt.localRotation != Quaternion.identity)
            {
                if (!_childShiftLogged) { _childShiftLogged = true; WowLog.Warn("Mount: the model was moved under its root by " + (mt.localPosition - _childAt).ToString("F2") + " m - put back (logged once)"); }
                mt.localRotation = Quaternion.identity;
            }
            _childAt = UpdateOffset(feet);
            mt.localPosition = _childAt;
        }

        /// <summary>CrossFade on a change of the requested animation, Speed = rate every frame.</summary>
        public void Animate(RideGaitPick gait)
        {
            if (_anim == null || _model == null) return;
            int want = gait.AnimId;
            bool loop = gait.Loop;
            if (want == _requestedAnim && !_requestedLoop && _anim.PlayingAnimationId != _requestedPlayed)
            {
                // the caller keeps asking for a one-shot that has already ended (it passes its last pick as "playing"):
                // continue the way the gait rules would (PLAN7 s4.3) instead of standing still
                int next = want == RideGait.JumpStart ? RideGait.Jump : want == RideGait.JumpLandRun ? RideGait.Run : -1;
                if (next >= 0 && _anim.PlayingAnimationId != next && _anim.PlayingAnimationId == RideGait.Stand)
                {
                    _anim.CrossFade(next, true);
                    _requestedPlayed = _anim.PlayingAnimationId;
                }
            }
            else if (want != _requestedAnim)
            {
                _anim.CrossFade(want, loop);
                _requestedAnim = want;
                _requestedLoop = loop;
                _requestedPlayed = _anim.PlayingAnimationId;
            }
            _anim.Speed = gait.Rate > 0f ? gait.Rate : 1f;
        }

        /// <summary>Dismount. The M2 shader's batches are opaque or colour-animated by M2Effects (their MaterialPropertyBlocks), so a
        /// fade would need new blend states on the materials: the mount is hidden at once (logged once; README Known gaps).</summary>
        public void Hide(float fadeSeconds)
        {
            _visible = false;
            if (_model == null) return;
            SetRendering(false);
            if (!_hideLogged)
            {
                _hideLogged = true;
                WowLog.Info("Mount: display " + _displayId + " hidden at once" + (fadeSeconds > 0f ? " (asked to fade " + fadeSeconds.ToString("0.00")
                    + " s: the M2 materials are opaque or colour-animated, no fade without changing their blend state)" : ""));
            }
        }

        public void Destroy()
        {
            DestroyObjects();
            _pendingDisplay = 0;
            _visible = false;
            StopPump();
        }

        // ------------------------------------------------------------------------------------------------ private

        private static void LogConstantsOnce()
        {
            if (_constantsLogged) return;
            _constantsLogged = true;
            WowLog.Info("Mount: ours - art priority " + PrewarmPriority + " (the rider's own mount first), seat measured " + SeatMeasureFrames
                + " frames after the build then fixed, fallback seat " + (BoundsSeatFraction * 100f) + " % of the bounds height, layer " + MountLayer
                + ", one mount build per frame; saddle offset from " + SeatMeanSamples + " samples a cycle, at most " + MaxSeatOffset + " m, vertical only for Fly/Hover/Swim anims; hidden at dismount without a fade");
        }

        private void StartPump()
        {
            if (_pumping) return;
            if (_pump == null) _pump = Pump;
            Application.onBeforeRender += _pump;
            _pumping = true;
        }

        private void StopPump()
        {
            if (!_pumping) return;
            Application.onBeforeRender -= _pump;
            _pumping = false;
        }

        /// <summary>Once a frame while something is pending (Application.onBeforeRender, after every LateUpdate).</summary>
        private void Pump()
        {
            try
            {
                if (_pendingDisplay > 0 && _model == null)
                {
                    bool ready = WowEntityViews.PrewarmDisplay(_pendingDisplay, PrewarmPriority);
                    if (ready && _lastBuildFrame != Time.frameCount)
                    {
                        int d = _pendingDisplay;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Build(d);
                        _pendingDisplay = 0;
                        if (_model != null)
                            WowLog.Info("Mount: display " + d + " prepared in " + (Time.realtimeSinceStartup - _prepareStarted).ToString("0.00") + " s (build "
                                + sw.ElapsedMilliseconds + " ms), hidden until mount-up");
                        else WowLog.Warn("Mount: display " + d + " could not be built");
                    }
                }
                if (_model != null && !_seatMeasured) MeasureSeat();
                if ((_pendingDisplay <= 0 || _model != null) && (_model == null || _seatMeasured)) StopPump();
            }
            catch (Exception e)
            {
                WowLog.Warn("Mount: pump threw " + e.GetType().Name + ": " + e.Message);
                _pendingDisplay = 0;
                StopPump();
            }
        }

        private void Build(int displayId)
        {
            _lastBuildFrame = Time.frameCount;
            var files = WowPlay.Files;
            if (files == null) { WowLog.Warn("Mount: no game files - display " + displayId + " not built"); return; }
            GameObject model = null;
            try { model = WowCreatures.Build(files, displayId); }
            catch (Exception e) { WowLog.Warn("Mount: WowCreatures.Build(" + displayId + ") threw " + e.GetType().Name + ": " + e.Message); model = null; }
            if (model == null) return;

            var root = new GameObject("WoW Mount " + displayId);
            var game = WowGame.Current;
            if (game != null) root.transform.SetParent(game.transform, false);
            root.transform.localPosition = HiddenAt;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            float scale = model.transform.localScale.x > 0f ? model.transform.localScale.x : 1f;   // WowCreatures: display x model scale
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = MountLayer;

            _root = root;
            _model = model;
            _displayId = displayId;
            _anim = model.GetComponent<M2Animator>();
            if (_anim == null) _anim = model.GetComponentInChildren<M2Animator>(true);
            _renderers = root.GetComponentsInChildren<Renderer>(true);
            _requestedAnim = -1;
            _requestedPlayed = -1;
            _childShiftLogged = false;
            _scale = scale;
            _meanSeat.Clear(); _offsetLogged.Clear();
            _offFrom = _offTo = _offNow = _childAt = Vector3.zero; _offDataSeq = -2; _offT = 0f; _offDur = 0f;
            SetRendering(_visible);

            // ---- MountHeight (CreatureModelData field 16 as stored; AC Unit.cpp 16783 uses it unscaled)
            _mountHeightYards = 0f;
            string path = "?";
            try
            {
                var cd = WowModelDb.ResolveCreature(files, displayId);
                path = cd.ModelPath ?? "?";
                var cmd = Dbc.Load(files, "CreatureModelData");
                int mr = cmd != null && cd.Error == null ? cmd.FindRow(cd.ModelId) : -1;
                if (mr >= 0) _mountHeightYards = cmd.GetFloat(mr, DbcFields.CreatureModelData.MountHeight);
            }
            catch (Exception e) { WowLog.Warn("Mount: reading CreatureModelData for display " + displayId + " threw " + e.Message); }

            // ---- seat: attachment 0 in bind pose (measured on the animated model 2 frames later), else the fallbacks
            M2Model m = _anim != null ? _anim.Model : null;
            _seatMeasured = true;
            _seatSource = "fallback 1.7 m";
            _seatLocal = new Vector3(0f, 1.7f, 0f);
            bool att0 = false;
            if (m != null)
            {
                for (int i = 0; i < m.Attachments.Length; i++)
                {
                    var a = m.Attachments[i];
                    if (a == null || a.Id != 0) continue;
                    _seatLocal = WowSpace.PointToUnity(a.Position.X, a.Position.Y, a.Position.Z) * scale;
                    _seatSource = "attachment 0 (bind pose)";
                    att0 = true;
                    break;
                }
            }
            if (att0 && _anim != null && _anim.Attachment(0) != null)
            {
                _seatMeasured = false;
                _seatMeasureFrame = Time.frameCount + SeatMeasureFrames;
            }
            else if (!att0 && _mountHeightYards > 0f)
            {
                _seatLocal = new Vector3(0f, _mountHeightYards * WowSpace.MetresPerYard * scale, 0f);
                _seatSource = "no attachment 0: MountHeight " + _mountHeightYards.ToString("0.###") + " yd x scale " + scale.ToString("0.##");
            }
            else if (!att0 && m != null)
            {
                float h = (m.BoundsMax.Z - m.BoundsMin.Z) * WowSpace.MetresPerYard * scale;
                if (h > 0.1f)
                {
                    _seatLocal = new Vector3(0f, h * BoundsSeatFraction, 0f);
                    _seatSource = "no attachment 0, no MountHeight: " + (BoundsSeatFraction * 100f) + " % of the bounds height " + h.ToString("0.00") + " m";
                }
            }

            // ---- gait facts from the model's own sequences
            _gait = default(RideGaitModel);
            _gait.Scale = scale;
            if (m != null && _anim != null)
            {
                int w = m.FindSequence(RideGait.Walk), r = m.FindSequence(RideGait.Run);
                _gait.WalkMoveSpeed = w >= 0 ? m.Sequences[w].MoveSpeed : 0f;
                _gait.RunMoveSpeed = r >= 0 ? m.Sequences[r].MoveSpeed : 0f;
                _gait.HasRun = _anim.Has(RideGait.Run);
                _gait.HasFly = _anim.Has(RideGait.Fly);
                _gait.HasHover = _anim.Has(RideGait.Hover);
                _gait.HasJump = _anim.Has(RideGait.Jump);
                _gait.HasJumpEnd = _anim.Has(RideGait.JumpEnd);
                _gait.HasJumpLandRun = _anim.Has(RideGait.JumpLandRun);
                _gait.HasFall = _anim.Has(RideGait.Fall);
            }
            WowLog.Info("Mount: display " + displayId + " built (" + path + ", scale " + scale.ToString("0.##") + ", " + _renderers.Length + " renderers, layer "
                + MountLayer + ") - seat " + _seatSource + " at (fwd " + _seatLocal.z.ToString("0.00") + ", up " + _seatLocal.y.ToString("0.00") + ") m, MountHeight "
                + _mountHeightYards.ToString("0.###") + " yd; gait walk " + _gait.WalkMoveSpeed.ToString("0.##") + " / run " + _gait.RunMoveSpeed.ToString("0.##")
                + " yd/s, run " + YesNo(_gait.HasRun) + ", fly " + YesNo(_gait.HasFly) + ", hover " + YesNo(_gait.HasHover) + ", jump " + YesNo(_gait.HasJump)
                + ", jump end " + YesNo(_gait.HasJumpEnd) + ", land run " + YesNo(_gait.HasJumpLandRun) + ", fall " + YesNo(_gait.HasFall));
        }

        private void MeasureSeat()
        {
            if (_seatMeasured || _model == null || _root == null || _anim == null || Time.frameCount < _seatMeasureFrame) return;
            _seatMeasured = true;
            Transform att = _anim.Attachment(0);
            if (att == null) return;
            Vector3 was = _seatLocal;
            // in the MODEL's own space x its scale (= Root space with the model at the root), so the model offset never enters the seat
            _seatLocal = Vector3.Scale(_model.transform.InverseTransformPoint(att.position), _model.transform.localScale);
            _offDataSeq = -2;   // offsets are recomputed against the measured seat
            Vector3 pureStand;
            string pure = TryMeanSeat(_anim.DataSequence, out pureStand) ? "; the playing sequence's computed saddle mean (fwd " + pureStand.z.ToString("0.00") + ", up " + pureStand.y.ToString("0.00") + ", side " + pureStand.x.ToString("0.00") + ")" : "";
            _seatSource = "attachment 0 (measured once on the animated model)";
            WowLog.Info("Mount: display " + _displayId + " seat measured on the animated model at (fwd " + _seatLocal.z.ToString("0.00") + ", up "
                + _seatLocal.y.ToString("0.00") + ", side " + _seatLocal.x.ToString("0.00") + ") m (bind pose fwd " + was.z.ToString("0.00") + ", up "
                + was.y.ToString("0.00") + "), fixed from now on" + pure);
        }

        private static bool AirAnim(int id)
        {
            return id == RideGait.Fly || id == RideGait.Hover || (id >= RideGait.SwimIdle && id <= RideGait.SwimBackwards);
        }

        /// <summary>Once a frame from Place: follows the sequence the animator plays (also its own one-shot returns) and blends the
        /// model offset to its target over that sequence's BlendTime. Returns the child's local position (Root space, metres).</summary>
        private Vector3 UpdateOffset(Vector3 feet)
        {
            if (_anim == null) return Vector3.zero;
            int ds = _anim.DataSequence;
            if (ds != _offDataSeq)
            {
                _offDataSeq = ds;
                int played = _anim.PlayingAnimationId;
                bool air = AirAnim(played) || AirAnim(_requestedAnim);
                Vector3 target = Vector3.zero;
                Vector3 mean;
                if (TryMeanSeat(ds, out mean))
                {
                    target = _seatLocal - mean;
                    if (!air) target.y = 0f;
                    if (target.magnitude > MaxSeatOffset)
                    {
                        if (_offsetLogged.Add(ds)) WowLog.Warn("Mount: display " + _displayId + " sequence " + ds + " saddle offset " + target.ToString("F2") + " m is implausible - not used");
                        target = Vector3.zero;
                    }
                    else if (_offsetLogged.Add(ds))
                        WowLog.Info("Mount: display " + _displayId + " anim " + played + " (sequence " + ds + (air ? ", air/water" : ", ground: horizontal only") + ") saddle mean (fwd "
                            + mean.z.ToString("0.00") + ", up " + mean.y.ToString("0.00") + ", side " + mean.x.ToString("0.00") + ") m vs seat (fwd " + _seatLocal.z.ToString("0.00")
                            + ", up " + _seatLocal.y.ToString("0.00") + ") -> model offset (fwd " + target.z.ToString("0.00") + ", up " + target.y.ToString("0.00") + ", side "
                            + target.x.ToString("0.00") + ") m");
                }
                _offFrom = _offNow;
                _offTo = target;
                _offT = 0f;
                _offDur = Mathf.Max(0f, _anim.BlendTimeMs(played) / 1000f);
            }
            // once per frame: WowMount places the model in Update and again in LateUpdate, and a second += would blend the offset
            // in half the sequence's BlendTime (fixer, review finding 5)
            if (Time.frameCount != _offFrame) { _offFrame = Time.frameCount; _offT += Time.deltaTime; }
            float w = _offDur > 0f ? Mathf.Clamp01(_offT / _offDur) : 1f;
            _offNow = Vector3.LerpUnclamped(_offFrom, _offTo, w);
            Vector3 o = _offNow;
            if (o.y < -0.01f && _root != null)
            {
                // never below the ground under the mount: one ray on layer 0 (terrain, WMO floors) while the model is lowered
                RaycastHit hit;
                Vector3 from = feet + Vector3.up * 0.3f;
                if (Physics.Raycast(from, Vector3.down, out hit, 0.3f - o.y + 0.05f, 1, QueryTriggerInteraction.Ignore))
                {
                    float lowest = hit.point.y - feet.y;
                    if (o.y < lowest) o.y = Mathf.Min(0f, lowest);
                }
            }
            return o;
        }

        /// <summary>The mean position of attachment 0 over one cycle of a data sequence, Root space (metres incl. the display scale).</summary>
        private bool TryMeanSeat(int dataSeq, out Vector3 mean)
        {
            mean = Vector3.zero;
            if (dataSeq < 0 || _anim == null) return false;
            if (_meanSeat.TryGetValue(dataSeq, out mean)) return true;
            M2Model m = _anim.Model;
            if (m == null || dataSeq >= m.Sequences.Length) return false;
            M2Attachment att = null;
            for (int i = 0; i < m.Attachments.Length; i++) if (m.Attachments[i] != null && m.Attachments[i].Id == 0) { att = m.Attachments[i]; break; }
            if (att == null) { _meanSeat[dataSeq] = mean = _seatLocal; return true; }   // no attachment 0: nothing moves, offset 0
            int dur = Mathf.Max(1, m.Sequences[dataSeq].Duration);
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < SeatMeanSamples; k++) sum += AttachmentAt(m, att, dataSeq, (int)((long)k * dur / SeatMeanSamples));
            mean = sum / SeatMeanSamples * _scale;
            _meanSeat[dataSeq] = mean;
            return true;
        }

        /// <summary>Attachment a in model space (Unity axes, metres, unscaled) at a sequence time: the same bone maths M2Animator.Apply
        /// uses (localPosition = pivot - parentPivot + translation, rotation, scale; bones without tracks keep their build pose).</summary>
        private static Vector3 AttachmentAt(M2Model m, M2Attachment a, int seq, int t)
        {
            Vector3 p = WowSpace.PointToUnity(a.Position.X, a.Position.Y, a.Position.Z);
            int i = a.Bone;
            if (i < 0 || i >= m.Bones.Length) return p;
            var pv = m.Bones[i].Pivot;
            p -= WowSpace.PointToUnity(pv.X, pv.Y, pv.Z);
            for (int guard = 0; i >= 0 && i < m.Bones.Length && guard < 256; guard++)
            {
                var b = m.Bones[i];
                Vector3 pivot = WowSpace.PointToUnity(b.Pivot.X, b.Pivot.Y, b.Pivot.Z);
                Vector3 parentPivot = Vector3.zero;
                if (b.Parent >= 0 && b.Parent < m.Bones.Length) { var pp = m.Bones[b.Parent].Pivot; parentPivot = WowSpace.PointToUnity(pp.X, pp.Y, pp.Z); }
                Vector3 lp = pivot - parentPivot;
                Quaternion lr = Quaternion.identity;
                Vector3 ls = Vector3.one;
                if (!b.Translation.IsEmpty || !b.Rotation.IsEmpty || !b.Scale.IsEmpty)
                {
                    M2Vec3 v; M2Quat q;
                    if (M2Sampler.Sample(b.Translation, seq, t, m.GlobalSequences, t, out v)) lp += WowSpace.PointToUnity(v.X, v.Y, v.Z);
                    if (M2Sampler.Sample(b.Rotation, seq, t, m.GlobalSequences, t, out q)) lr = WowSpace.RotationToUnity(q.X, q.Y, q.Z, q.W);
                    if (M2Sampler.Sample(b.Scale, seq, t, m.GlobalSequences, t, out v)) ls = new Vector3(v.Y, v.Z, v.X);
                }
                p = lp + lr * Vector3.Scale(ls, p);
                i = b.Parent;
            }
            return p;
        }

        private void SetRendering(bool on)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r != null) r.forceRenderingOff = !on;
            }
        }

        private void DestroyObjects()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _model = null;
            _anim = null;
            _renderers = new Renderer[0];
            _displayId = 0;
            _seatMeasured = true;
            _requestedAnim = -1;
            _requestedPlayed = -1;
            _meanSeat.Clear(); _offsetLogged.Clear();
            _offFrom = _offTo = _offNow = _childAt = Vector3.zero; _offDataSeq = -2;
        }

        private static string YesNo(bool b) { return b ? "yes" : "no"; }
    }
}
