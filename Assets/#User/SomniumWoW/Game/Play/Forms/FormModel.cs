// iteration 11 - owner avatar (proposal-avatar.md 6.F FormModel): the WoW creature model that stands in for a Somnium avatar.
using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Forms
{
    /// <summary>
    /// One creature model (a druid form, a polymorph sheep, the Night Elf wisp, a costume ...) standing where an avatar is:
    /// - Build: prepared on the entity art worker first (WowEntityViews.PrewarmDisplay; built anyway after 10 s, as remote mounts),
    ///   then WowCreatures.Build - at most ONE form build per frame for all avatars. Root scale = the display's own scale (WowCreatures)
    ///   x the object scale (OBJECT_FIELD_SCALE_X). Measured once 2 frames after the build (the Stand pose): the head key bone (6)
    ///   height and forward offset, attachment 18 (PlayerName) height, the renderer bounds top.
    /// - Place: at the avatar's feet, yaw = the avatar's flat facing smoothed exp 12/s (ours); never pitch or roll (comfort).
    ///   First person (the local copy only): the head key bone collapsed (localScale 0.001, written after M2Animator every frame -
    ///   it writes localScale itself, M2Animator.cs Apply), the root moved back by the head's forward offset so the hidden head sits
    ///   under the eyes, and a form taller than the eyes shrunk uniformly to (camera height - 0.15 m) / head height (only shrinks;
    ///   ours). Everyone else builds their own full-size copy from the record.
    /// - Animate: the gait through RideGait.Pick + GearRideGait.Apply exactly as remote mounts; a cast loop when the model has it; one-
    ///   shots (attacks, kit anims, wounds, cast releases) on top, then back to the gait; dead = Death (1) held.
    /// - Spell anchors (TrySlot): SpellVisualDb.SlotAttachment, then SlotFallback, else the root at 0.6 / 0.95 of the height (the
    ///   rule WowSpellVfx uses for units without attachments).
    /// </summary>
    public sealed class FormModel
    {
        public const float ArtWaitSeconds = 10f;       // as GearRemote's mounts (s)
        public const float FirstPersonDistance = 0.35f; // ours: camera this close to the head = first person (VR always)
        public const float EyeClearance = 0.15f;       // ours: a big form's head ends this far under the eyes in first person
        public const float YawRate = 12f;              // ours: 1/s, exp smoothing of the model's yaw
        public const float ShotMaxSeconds = 4f;        // ours: a one-shot never holds the gait longer

        private static int _buildFrame = -1;

        public readonly int DisplayId;
        public readonly float LookScale;
        private readonly string _label;
        private readonly float _askedAt;
        private GameObject _go;
        private M2Animator _anim;
        private Transform _head;
        private RideGaitModel _gait;
        private int _playing = -1;
        private bool _tried;
        private int _measureFrame;
        private bool _measured;
        private float _headH, _headF, _nameH = -1f, _top = 1f;   // model units (metres before the root scale)
        private float _baseScale = 1f, _drawScale = 1f;
        private float _yaw;
        private bool _haveYaw, _headCollapsed, _fpSaid, _shot, _deadHeld;
        private int _shotId;
        private float _shotAt;

        public FormModel(int displayId, float lookScale, string label)
        {
            DisplayId = displayId;
            LookScale = lookScale > 0.01f ? lookScale : 1f;
            _label = label;
            _askedAt = Time.unscaledTime;
        }

        public bool Ready { get { return _go != null; } }
        /// <summary>Tried and nothing came of it (no model / no files): the avatar keeps its own look.</summary>
        public bool Failed { get { return _tried && _go == null; } }
        public GameObject Go { get { return _go; } }
        public M2Animator Animator { get { return _anim; } }
        /// <summary>The model's height now (metres, as drawn), from the measured bounds.</summary>
        public float Height { get { return _top * _drawScale; } }
        public bool Has(int animId) { return _anim != null && animId > 0 && _anim.Has(animId); }
        public bool FirstPerson { get; private set; }

        /// <summary>Prepares and builds when the art worker has it (or after ArtWaitSeconds); true once built or failed.</summary>
        public bool TryBuild(WowFiles files, Transform container, float priority)
        {
            if (_go != null || _tried) return true;
            if (files == null) { _tried = true; return true; }
            bool ready = WowEntityViews.PrewarmDisplay(DisplayId, priority);
            bool late = Time.unscaledTime - _askedAt > ArtWaitSeconds;
            if ((!ready && !late) || _buildFrame == Time.frameCount) return false;
            _buildFrame = Time.frameCount;
            _tried = true;
            GameObject go = null;
            try { go = WowCreatures.Build(files, DisplayId); }
            catch (Exception e) { WowLog.Warn("Forms: display " + DisplayId + " of " + _label + " threw: " + e.Message); go = null; }
            if (go == null) { WowLog.Warn("Forms: no model for display " + DisplayId + " (" + _label + ") - the avatar keeps its own look"); return true; }
            _go = go;
            go.name = "Form " + DisplayId + " of " + _label;
            if (container != null) go.transform.SetParent(container, false);
            _baseScale = Mathf.Max(0.01f, go.transform.localScale.x) * LookScale;
            _drawScale = _baseScale;
            go.transform.localScale = Vector3.one * _baseScale;
            _anim = go.GetComponent<M2Animator>();
            _head = KeyBone(6);
            _gait = GearRideGait.Of(_anim, go.transform);
            _playing = _anim != null ? GearRideGait.Apply(_anim, -1, RideGait.Stand, 1f) : -1;
            _measureFrame = Time.frameCount + 2;
            WowLog.Info("Forms: built display " + DisplayId + " for " + _label + " (scale " + _baseScale.ToString("0.00") + (ready ? "" : ", art worker not ready after " + ArtWaitSeconds.ToString("0") + " s: built anyway") +
                        ", head bone " + (_head != null ? "found" : "missing") + ")");
            return true;
        }

        private Transform KeyBone(int key)
        {
            if (_anim == null || _anim.Model == null) return null;
            short[] look = _anim.Model.KeyBoneLookup;
            int b = look != null && key < look.Length ? look[key] : -1;
            Transform[] bones = _anim.Bones;
            return b >= 0 && bones != null && b < bones.Length ? bones[b] : null;
        }

        /// <summary>The head height / forward offset, name point and top, once, on the animated model (the Stand pose).</summary>
        private void MeasureOnce()
        {
            if (_measured || _go == null || Time.frameCount < _measureFrame) return;
            _measured = true;
            Transform t = _go.transform;
            float s = Mathf.Max(0.01f, t.localScale.x);
            if (_head != null)
            {
                Vector3 h = t.InverseTransformPoint(_head.position);
                _headH = h.y;
                _headF = h.z;
            }
            Transform name = _anim != null ? _anim.Attachment(18) : null;
            if (name != null) _nameH = t.InverseTransformPoint(name.position).y;
            bool any = false;
            Bounds b = default(Bounds);
            foreach (Renderer r in _go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) _top = Mathf.Max(0.1f, (b.max.y - t.position.y) / s);
            if (_headH <= 0.01f) _headH = 0.85f * _top;
            WowLog.Info("Forms: display " + DisplayId + " of " + _label + " measured - height " + (_top * _baseScale).ToString("0.00") + " m, head " + (_headH * _baseScale).ToString("0.00") +
                        " m up / " + (_headF * _baseScale).ToString("0.00") + " m forward, name point " + (_nameH > 0f ? (_nameH * _baseScale).ToString("0.00") + " m" : "none (bounds top + 0.3 m)"));
        }

        /// <summary>
        /// Every LateUpdate: at the feet facing forward (flat), local first person = the head collapsed and the model moved back / shrunk
        /// so the head sits under the eyes (camera = the local eye position; ignored when firstPerson is false).
        /// </summary>
        public void Place(Vector3 feet, Vector3 forward, bool firstPerson, Vector3 camera, float dt)
        {
            if (_go == null) return;
            if (!_go.activeSelf) _go.SetActive(true);
            MeasureOnce();
            forward.y = 0f;
            float target = forward.sqrMagnitude > 1e-6f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : _yaw;
            if (!_haveYaw) { _yaw = target; _haveYaw = true; }
            else _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-Mathf.Max(0f, dt) * YawRate));
            Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
            float shrink = 1f;
            FirstPerson = firstPerson;
            if (firstPerson && _measured)
            {
                float camH = camera.y - feet.y;
                float headH = _headH * _baseScale;
                if (headH > camH - EyeClearance && headH > 0.05f) shrink = Mathf.Clamp((camH - EyeClearance) / headH, 0.05f, 1f);
            }
            _drawScale = _baseScale * shrink;
            Vector3 pos = feet;
            if (firstPerson && _measured) pos -= rot * Vector3.forward * (_headF * _drawScale);
            Transform t = _go.transform;
            t.SetPositionAndRotation(pos, rot);
            if (Mathf.Abs(t.localScale.x - _drawScale) > 1e-4f) t.localScale = Vector3.one * _drawScale;
            if (firstPerson && _head != null)
            {
                _head.localScale = Vector3.one * 0.001f;
                _headCollapsed = true;
            }
            else if (_headCollapsed && _head != null)
            {
                _head.localScale = Vector3.one;
                _headCollapsed = false;
            }
            if (firstPerson && !_fpSaid && _measured)
            {
                _fpSaid = true;
                WowLog.Info("Forms: " + _label + " sees display " + DisplayId + " in first person - head hidden, " + (shrink < 0.999f ? "shrunk to " + shrink.ToString("0.00") + " in your own view only" : "true size") +
                            ", moved back " + (_headF * _drawScale).ToString("0.00") + " m (the camera itself is never moved)");
            }
        }

        public void Hide()
        {
            if (_go != null && _go.activeSelf) _go.SetActive(false);
        }

        /// <summary>The gait (or a cast loop / the held death) for this frame; a playing one-shot is left to finish first. combatReady:
        /// in combat, a model standing still plays its ReadyUnarmed (25) when it has it (the druid bears do), as WoW's creatures.</summary>
        public void Animate(RideMode mode, float forwardMps, float verticalMps, float runSpeedMps, bool jumped, bool landed, bool dead, int castLoop, float now, bool combatReady)
        {
            if (_anim == null) return;
            if (dead)
            {
                if (!_deadHeld)
                {
                    _deadHeld = true;
                    _shot = false;
                    _anim.HoldAtEnd = true;
                    _anim.Speed = 1f;
                    _anim.CrossFade(1, false);
                    _playing = 1;
                }
                return;
            }
            if (_deadHeld) { _deadHeld = false; _anim.HoldAtEnd = false; _playing = -1; }
            if (_shot)
            {
                if (_anim.PlayingOnce && _anim.PlayingAnimationId == _shotId && now - _shotAt < ShotMaxSeconds) return;
                _shot = false;
                _playing = -1;
            }
            if (castLoop > 0 && _anim.Has(castLoop))
            {
                if (_playing != castLoop) { _anim.HoldAtEnd = false; _anim.CrossFade(castLoop, true); _playing = castLoop; }
                if (!Mathf.Approximately(_anim.Speed, 1f)) _anim.Speed = 1f;
                return;
            }
            int playing = _playing == ReadyUnarmed ? RideGait.Stand : _playing;
            RideGaitPick pick = RideGait.Pick(mode, forwardMps, 0f, verticalMps, runSpeedMps, jumped, landed, _gait, playing);
            int id = pick.AnimId;
            if (id == RideGait.Stand && combatReady && mode == RideMode.Ground && _anim.Has(ReadyUnarmed)) id = ReadyUnarmed;
            _playing = GearRideGait.Apply(_anim, _playing, id, id == ReadyUnarmed ? 1f : pick.Rate);
        }

        private const int ReadyUnarmed = 25;

        /// <summary>A one-shot (attack, kit animation, wound, cast release, emote) when the model has it; false = not played.</summary>
        public bool PlayOnce(int animId, float now)
        {
            if (_anim == null || animId <= 0 || _deadHeld || !_anim.Has(animId)) return false;
            _anim.HoldAtEnd = false;
            _anim.Speed = 1f;
            _anim.CrossFade(animId, false);
            _shot = true;
            _shotId = _anim.PlayingAnimationId;
            _shotAt = now;
            _playing = -1;
            return true;
        }

        /// <summary>A spell visual slot on this model (attachments 20, 34, 19, 21, 22, 17, ... SpellVisualDb.SlotAttachment; then
        /// SlotFallback; else the root at 0.6 / 0.95 of the height). False before the model exists.</summary>
        public bool TrySlot(int slot, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (_go == null || !_go.activeSelf || slot < 0 || slot >= SpellVisualDb.SlotAttachment.Length) return false;
            Transform root = _go.transform;
            rot = root.rotation;
            Transform t = null;
            if (_anim != null)
            {
                t = _anim.Attachment(SpellVisualDb.SlotAttachment[slot]);
                int fb = SpellVisualDb.SlotFallback[slot];
                if (t == null && fb >= 0) t = _anim.Attachment(fb);
            }
            if (t != null) { pos = t.position; return true; }
            if (slot == (int)SpellVisualDb.Slot.Base || slot == (int)SpellVisualDb.Slot.World) { pos = root.position; return true; }
            float k = slot == (int)SpellVisualDb.Slot.Head || slot == (int)SpellVisualDb.Slot.Breath ? 0.95f : 0.6f;
            pos = root.position + Vector3.up * (Height * k);
            return true;
        }

        /// <summary>Where a nameplate goes: above attachment 18 when the model has it, else the bounds top + 0.3 m.</summary>
        public Vector3 NamePoint()
        {
            if (_go == null) return Vector3.zero;
            Transform root = _go.transform;
            return root.position + Vector3.up * (_nameH > 0f ? _nameH * _drawScale + 0.1f : Height + 0.3f);   // ours: 0.1 / 0.3 m clearance
        }

        public void Destroy()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _anim = null;
            _head = null;
        }
    }
}
