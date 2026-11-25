using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// Iteration 7 (owner UI+Audio, PLAN7 s4.3 / s5.6): the local ridden mount's own sounds while a ride owns the avatar
    /// (WowMount.OwnsAvatar with a drawn mount, WowMount.MountRoot's M2Animator).
    /// - Steps at the model's "$FSD" events (Models/M2Events: 4 per Walk / Run cycle on the horse, ram, wolf, gryphon, wind rider;
    ///   2 on the mechanostrider and the proto-drake - measured by the ui7 probe) of the sequence the animator plays (DataSequence,
    ///   TimeMs; a loop wrap fires the events of both ends), so the hooves sound where the legs land at any gait rate.
    /// - The sound: the display's CreatureSoundData.SoundFootstepID (CreatureDisplayInfo.SoundID, else CreatureModelData.SoundID:
    ///   10 horse, 8 wolf, 117 mechanostrider, 178 flying mounts) through FootstepTerrainLookup for the ground under the mount's feet
    ///   (AudioFootsteps.Surface - the avatar's own classification ray, from WowMount.Feet); terrain water: splash, silent once deeper
    ///   than AzerothCore's swim depth 0.75 x the collision height (Unit.cpp 4217-4219).
    /// - Wing flaps (SoundWingFlapID) at the "$WNG" events (only on the swim / fly / hover / jump sequences - never Stand/Walk/Run).
    /// - Take-off (SoundJumpStartID, the flyers' FlyingMountTakeOff) when PlayerMotion.Flying starts or a jump starts, landing
    ///   (SoundJumpEndID, FlyingMountLandUni) when a flight touches down or a jump lands - ground mounts use the same fields when
    ///   their row has them (the horse's has none).
    /// The model file, its events and the sound ids are read once per display on the audio worker.
    /// </summary>
    internal sealed class AudioMountSteps
    {
        private sealed class Prepared
        {
            public int Display;
            public string ModelPath;
            public M2Events Events;
            public int Footstep, WingFlap, JumpStart, JumpEnd;
            public string Error;
        }

        private const int MaxEventsPerFrame = 4;   // ours: a hitch never fires a burst of steps
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly System.Random _rng = new System.Random();
        private Prepared _ready;
        private int _wantDisplay = -1, _gen, _lines;
        private string _wantPath;
        private Transform _root;
        private M2Animator _anim;
        private int _lastSeq = -1, _lastAnim = -1, _lastT;
        private bool _wasFlying, _haveMotion;
        private int _lastType = -1;

        public int Steps { get; private set; }
        public int Flaps { get; private set; }

        /// <summary>Forgets the playing position (not the prepared display): the next frame starts fresh.</summary>
        public void Reset()
        {
            _lastSeq = -1;
            _lastAnim = -1;
            _haveMotion = false;
            _wasFlying = false;
        }

        public void Update(WowAudioDriver d, AudioWorker worker, WowFiles files, WowSoundData data, WowGround ground, int mapId, PlayerState p)
        {
            Transform root = WowMount.MountRoot;
            if (!WowMount.OwnsAvatar || root == null || data == null || files == null || p == null) { Reset(); return; }
            if (!ReferenceEquals(root, _root))
            {
                _root = root;
                _anim = root.GetComponent<M2Animator>();
                if (_anim == null) _anim = root.GetComponentInChildren<M2Animator>();
                Reset();
            }
            if (_anim == null || _anim.Model == null) return;
            int display = p.MountDisplayId;
            string path = _anim.Model.Path;
            if (display != _wantDisplay || !string.Equals(path, _wantPath, StringComparison.OrdinalIgnoreCase)) Prepare(d, worker, files, data, display, path);
            Prepared r = _ready;
            if (r == null || r.Display != display) return;

            PlayerMotion m = WowMount.Motion;
            Vector3 feet = WowMount.Feet;
            Vector3 seat = feet + Vector3.up * Mathf.Max(0.5f, WowMount.SeatHeight);

            // Take-off / landing / jumps (the motion WowMount published this frame; it runs at order -500, before this Update).
            if (_haveMotion)
            {
                bool start = (m.Flying && !_wasFlying) || m.Jumped;
                bool end = (!m.Flying && _wasFlying && !m.Airborne) || m.Landed;
                if (start && r.JumpStart > 0) { d.PlayEntryWith(r.JumpStart, SoundCategory.Effects, seat, 1f, 1f, 1); Line("take-off / jump start", r.JumpStart, data); }
                if (end && r.JumpEnd > 0) { d.PlayEntryWith(r.JumpEnd, SoundCategory.Effects, feet, 1f, 1f, 1); Line("landing / jump end", r.JumpEnd, data); }
            }
            _wasFlying = m.Flying;
            _haveMotion = true;

            // The model's events between the last frame's time and this frame's, on the sequence that plays.
            int seq = _anim.DataSequence, anim = _anim.PlayingAnimationId, t = _anim.TimeMs;
            if (seq < 0 || seq != _lastSeq || anim != _lastAnim) { _lastSeq = seq; _lastAnim = anim; _lastT = t; return; }
            int from = _lastT;
            _lastT = t;
            if (t == from) return;
            M2Model model = _anim.Model;
            int dur = seq < model.Sequences.Length ? model.Sequences[seq].Duration : 0;
            int[] fsd = r.Footstep > 0 ? r.Events.Times("$FSD", seq) : null;
            int[] wng = r.WingFlap > 0 ? r.Events.Times("$WNG", seq) : null;
            int fired = 0;
            if (t > from) fired += Fire(d, data, ground, mapId, p, r, fsd, wng, from, t, feet, seat, fired);
            else
            {
                // Looped: the end of the cycle, then its start.
                if (dur > 0) fired += Fire(d, data, ground, mapId, p, r, fsd, wng, from, dur, feet, seat, fired);
                fired += Fire(d, data, ground, mapId, p, r, fsd, wng, -1, t, feet, seat, fired);
            }
        }

        /// <summary>Plays the events with from &lt; time &lt;= to. Returns how many played.</summary>
        private int Fire(WowAudioDriver d, WowSoundData data, WowGround ground, int mapId, PlayerState p, Prepared r, int[] fsd, int[] wng, int from, int to, Vector3 feet, Vector3 seat, int already)
        {
            int n = 0;
            if (fsd != null)
                for (int i = 0; i < fsd.Length && already + n < MaxEventsPerFrame; i++)
                    if (fsd[i] > from && fsd[i] <= to && Step(d, data, ground, mapId, p, r, feet)) n++;
            if (wng != null)
                for (int i = 0; i < wng.Length && already + n < MaxEventsPerFrame; i++)
                    if (wng[i] > from && wng[i] <= to)
                    {
                        d.PlayEntryWith(r.WingFlap, SoundCategory.Effects, seat, 0.9f, 0.95f + (float)_rng.NextDouble() * 0.1f, 1);
                        Flaps++;
                        n++;
                        if (Flaps == 1) WowLog.Info("Audio: mount wing flaps on - sound " + r.WingFlap + " '" + data.EntryName(r.WingFlap) + "' at the model's $WNG events.");
                    }
            return n;
        }

        private bool Step(WowAudioDriver d, WowSoundData data, WowGround ground, int mapId, PlayerState p, Prepared r, Vector3 feet)
        {
            if (!WowSettings.Footsteps) return false;
            float silent = p.CollisionHeight > 0f ? p.CollisionHeight * 0.75f : 1.3f;   // AC's swim depth for this mount (yards)
            int type;
            string surface;
            bool splash;
            if (!AudioFootsteps.Surface(d, data, ground, mapId, feet, WowMount.MountRoot, _hits, silent, out type, out surface, out splash)) return false;
            int soundId = data.FootstepSound(r.Footstep, type, splash);
            if (soundId <= 0) return false;
            d.PlayEntryWith(soundId, SoundCategory.Footsteps, feet, 1f, 0.94f + (float)_rng.NextDouble() * 0.12f, 2);
            Steps++;
            if (Steps == 1 || (type != _lastType && _lines < 20))
            {
                _lines++;
                WowLog.Info("Audio: mount footsteps " + (Steps == 1 ? "on - first step" : "now") + " on " + (splash ? "water over " : "") + data.TerrainName(type) + " (" + surface + "), footstep id " + r.Footstep + ", sound " + soundId + " '" + data.EntryName(soundId) + "'.");
            }
            _lastType = type;
            return true;
        }

        private int _lineCount;
        private void Line(string what, int id, WowSoundData data)
        {
            if (_lineCount++ < 6) WowLog.Info("Audio: mount " + what + " - sound " + id + " '" + data.EntryName(id) + "'.");
        }

        /// <summary>Reads the model's events and the display's sound ids on the audio worker (once per display / model).</summary>
        private void Prepare(WowAudioDriver d, AudioWorker worker, WowFiles files, WowSoundData data, int display, string path)
        {
            _wantDisplay = display;
            _wantPath = path;
            _ready = null;
            Reset();
            int gen = ++_gen;
            worker.Enqueue(() =>
            {
                var r = new Prepared { Display = display, ModelPath = path };
                try
                {
                    byte[] b = files.Read(path);
                    r.Events = b != null ? M2Events.Read(b, path, q => files.Read(q)) : M2Events.Read(null);
                    if (b == null) r.Error = "model file missing";
                    data.CreatureSounds(display, out r.Footstep, out r.WingFlap, out r.JumpStart, out r.JumpEnd);
                }
                catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; if (r.Events == null) r.Events = M2Events.Read(null); }
                worker.Post(() =>
                {
                    if (gen != _gen) return;
                    _ready = r;
                    // The first steps must not come late (a footstep clip that is not loaded within 0.35 s is dropped, PlayPath).
                    if (r.Footstep > 0)
                    {
                        for (int t = 0; t <= WowSoundData.TerrainWater; t++) d.PrefetchEntry(data.FootstepSound(r.Footstep, t, false));
                        d.PrefetchEntry(data.FootstepSound(r.Footstep, WowSoundData.TerrainDirt, true));
                    }
                    d.PrefetchEntry(r.WingFlap);
                    d.PrefetchEntry(r.JumpStart);
                    d.PrefetchEntry(r.JumpEnd);
                    WowLog.Info("Audio: mount sounds for display " + display + " (" + path + "): footstep id " + (r.Footstep > 0 ? r.Footstep.ToString() : "none")
                        + ", wing flap " + Name(r.WingFlap, data) + ", take-off / jump start " + Name(r.JumpStart, data) + ", landing / jump end " + Name(r.JumpEnd, data)
                        + "; " + r.Events.Summary + (r.Error != null ? " - " + r.Error : "") + ".");
                });
            }, false);
        }

        private static string Name(int id, WowSoundData data)
        {
            if (id <= 0) return "none";
            string n = data.EntryName(id);
            return id + (n.Length > 0 ? " '" + n + "'" : "");
        }
    }
}
