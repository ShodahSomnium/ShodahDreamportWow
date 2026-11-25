using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// A fixed pool of AudioSources for one-shot sounds (2D interface sounds and 3D world sounds that may follow a transform).
    /// 3D sounds fall off like the client's: full volume inside SoundEntries MinDistance, then inversely with distance, silent at
    /// DistanceCutoff (yards -> metres; a custom rolloff curve per distance pair, cached). Volumes are the sound's own volume x
    /// WowSettings (master x category), re-applied to every playing sound when the settings change.
    /// Iteration 11 (audio fix, "some sounds don't loop properly or they play for too long"): a sound may now loop (only a keyed
    /// spell loop, WowAudioDriver.PlayEntryLoop), carry a life cap, and be faded out by its handle; a sound whose followed transform is
    /// destroyed fades out (before: it kept playing where the object was - a missile's 4-22 s flight loop went on after the missile).
    /// Every way back into the pool goes through Release: stopped, loop off, no clip.
    /// </summary>
    internal sealed class AudioPool
    {
        private sealed class Slot
        {
            public AudioSource Src;
            public Transform T;
            public SoundCategory Cat;
            public float BaseVolume;
            public Transform Follow;
            public bool HadFollow;      // iteration 11: tells a destroyed follow (Unity null) from "never had one"
            public float Started;
            public int Priority;
            public bool Active;
            public int Handle;          // iteration 11: unique per play (0 = none), so a stolen slot is never stopped for its old owner
            public int EntryId;         // iteration 11: the SoundEntries id (0 = a file without an entry)
            public float Gain = 1f;     // iteration 11: the fade factor
            public bool Fading;
            public float FadeStart, FadeLength, FadeFrom;
            public float EndAt = -1f;   // iteration 11: the life cap (unscaled time; < 0 = none)
        }

        public const int Size = 32;
        // Ours (no client figure known): a sound that ends before its file does fades over this long instead of clicking off.
        public const float StopFade = 0.25f;
        private readonly Slot[] _slots = new Slot[Size];
        private readonly Dictionary<long, AnimationCurve> _curves = new Dictionary<long, AnimationCurve>();
        private readonly GameObject _root;
        private int _stolen, _handles;

        public AudioPool(Transform parent)
        {
            _root = new GameObject("WoW Audio pool");
            _root.transform.SetParent(parent, false);
            for (int i = 0; i < Size; i++)
            {
                var go = new GameObject("sound " + i);
                go.transform.SetParent(_root.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.dopplerLevel = 0f;
                src.spread = 0f;
                _slots[i] = new Slot { Src = src, T = go.transform };
            }
        }

        public int Stolen { get { return _stolen; } }

        public static float CategoryVolume(SoundCategory c)
        {
            float m = Mathf.Clamp01(WowSettings.MasterVolume);
            switch (c)
            {
                case SoundCategory.Effects:
                case SoundCategory.Ui: return m * Mathf.Clamp01(WowSettings.EffectsVolume);
                case SoundCategory.Footsteps: return WowSettings.Footsteps ? m * Mathf.Clamp01(WowSettings.FootstepVolume) : 0f;
                case SoundCategory.Voice: return m * Mathf.Clamp01(WowSettings.VoiceVolume);
                case SoundCategory.Music: return WowSettings.Music ? m * Mathf.Clamp01(WowSettings.MusicVolume) : 0f;
                case SoundCategory.Ambience: return m * Mathf.Clamp01(WowSettings.AmbienceVolume);
            }
            return m;
        }

        /// <summary>Plays a clip once; returns the source (null when nothing could play: no clip or the category is muted).</summary>
        public AudioSource Play(AudioClip clip, SoundCategory cat, float baseVolume, bool is2D, Vector3 pos, Transform follow,
            float minYards, float cutoffYards, float pitch, int priority)
        {
            var s = Start(clip, cat, baseVolume, is2D, pos, follow, minYards, cutoffYards, pitch, priority, false, 0f, 0);
            return s != null ? s.Src : null;
        }

        /// <summary>
        /// Iteration 11: plays a clip, looped or once, with an optional life cap (seconds, 0 = none), and returns its handle (0 when
        /// nothing could play) for IsPlaying / FadeOut. entryId tags the play for FadeOutEntry.
        /// </summary>
        public int PlayHandle(AudioClip clip, SoundCategory cat, float baseVolume, bool is2D, Vector3 pos, Transform follow,
            float minYards, float cutoffYards, float pitch, int priority, bool loop, float lifeSeconds, int entryId)
        {
            var s = Start(clip, cat, baseVolume, is2D, pos, follow, minYards, cutoffYards, pitch, priority, loop, lifeSeconds, entryId);
            return s != null ? s.Handle : 0;
        }

        private Slot Start(AudioClip clip, SoundCategory cat, float baseVolume, bool is2D, Vector3 pos, Transform follow,
            float minYards, float cutoffYards, float pitch, int priority, bool loop, float lifeSeconds, int entryId)
        {
            if (clip == null) return null;
            float vol = baseVolume * CategoryVolume(cat);
            if (vol <= 0.0001f) return null;
            var s = Free(priority);
            if (s == null) return null;
            Release(s);   // a stolen slot: its old sound ends here (stopped, loop off) before the new one is set up
            s.Active = true;
            s.Cat = cat;
            s.BaseVolume = baseVolume;
            s.Follow = follow;
            s.HadFollow = follow != null;
            s.Started = Time.unscaledTime;
            s.Priority = priority;
            s.Handle = ++_handles;
            if (s.Handle == 0) s.Handle = ++_handles;   // after a wrap: 0 means "none"
            s.EntryId = entryId;
            s.EndAt = lifeSeconds > 0f ? s.Started + lifeSeconds : -1f;
            var src = s.Src;
            src.clip = clip;
            src.volume = vol;
            src.pitch = pitch;
            src.loop = loop;
            if (is2D)
            {
                src.spatialBlend = 0f;
                s.T.localPosition = Vector3.zero;
            }
            else
            {
                src.spatialBlend = 1f;
                float min = Mathf.Max(0.5f, minYards * WowSpace.MetresPerYard);
                float max = Mathf.Max(min + 1f, cutoffYards * WowSpace.MetresPerYard);
                src.rolloffMode = AudioRolloffMode.Custom;
                src.minDistance = min;
                src.maxDistance = max;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Curve(min, max));
                s.T.position = follow != null ? follow.position : pos;
            }
            src.Play();
            return s;
        }

        private Slot Free(int priority)
        {
            Slot oldest = null;
            for (int i = 0; i < Size; i++)
            {
                var s = _slots[i];
                if (!s.Active || !s.Src.isPlaying) return s;
                if (s.Priority <= priority && (oldest == null || s.Priority < oldest.Priority || (s.Priority == oldest.Priority && s.Started < oldest.Started))) oldest = s;
            }
            if (oldest != null) _stolen++;
            return oldest;
        }

        private AnimationCurve Curve(float min, float max)
        {
            long key = ((long)Mathf.RoundToInt(min * 10f) << 32) | (uint)Mathf.RoundToInt(max * 10f);
            AnimationCurve c;
            if (_curves.TryGetValue(key, out c)) return c;
            // x = distance / maxDistance; full inside min, then min/d, faded to 0 over the last 20 % before the cutoff
            float r = min / max;
            var keys = new List<Keyframe>();
            keys.Add(new Keyframe(0f, 1f));
            keys.Add(new Keyframe(r, 1f));
            for (int i = 1; i <= 10; i++)
            {
                float x = r + (1f - r) * i / 10f;
                float v = r / x;
                if (x > 0.8f) v *= Mathf.Clamp01((1f - x) / 0.2f);
                keys.Add(new Keyframe(x, v));
            }
            c = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            _curves[key] = c;
            return c;
        }

        /// <summary>
        /// Per frame: follows transforms, frees finished slots (dropping their clip reference), runs fades and life caps; a sound whose
        /// followed object was destroyed fades out (iteration 11).
        /// </summary>
        public void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < Size; i++)
            {
                var s = _slots[i];
                if (!s.Active) continue;
                if (!s.Src.isPlaying) { Release(s); continue; }
                if (s.Follow != null) s.T.position = s.Follow.position;
                else if (s.HadFollow) Fade(s, StopFade, now);           // the object it belonged to is gone
                if (s.EndAt >= 0f && now >= s.EndAt) Fade(s, StopFade, now);
                if (!s.Fading) continue;
                float g = s.FadeLength > 0f ? 1f - (now - s.FadeStart) / s.FadeLength : 0f;
                if (g <= 0f) { Release(s); continue; }
                s.Gain = s.FadeFrom * g;
                s.Src.volume = s.BaseVolume * CategoryVolume(s.Cat) * s.Gain;
            }
        }

        private static void Fade(Slot s, float seconds, float now)
        {
            if (s.Fading) return;
            s.Fading = true;
            s.FadeStart = now;
            s.FadeLength = seconds;
            s.FadeFrom = s.Gain;
        }

        /// <summary>Back into the pool: stopped, not looping, no clip, no owner.</summary>
        private static void Release(Slot s)
        {
            if (s.Src != null)
            {
                s.Src.Stop();
                s.Src.loop = false;
                s.Src.clip = null;
            }
            s.Active = false;
            s.Follow = null;
            s.HadFollow = false;
            s.Handle = 0;
            s.EntryId = 0;
            s.Gain = 1f;
            s.Fading = false;
            s.EndAt = -1f;
        }

        public void ApplyVolumes()
        {
            for (int i = 0; i < Size; i++)
            {
                var s = _slots[i];
                if (s.Active) s.Src.volume = s.BaseVolume * CategoryVolume(s.Cat) * s.Gain;
            }
        }

        public bool Uses(AudioClip clip)
        {
            if (clip == null) return false;
            for (int i = 0; i < Size; i++) if (_slots[i].Active && _slots[i].Src.clip == clip) return true;
            return false;
        }

        /// <summary>Lead 2026-10-11 (immersive casting, a held spell): that play loops with no life cap until it is faded out; false when
        /// it already ended or fades.</summary>
        public bool Hold(int handle)
        {
            if (handle == 0) return false;
            for (int i = 0; i < Size; i++)
            {
                var s = _slots[i];
                if (!s.Active || s.Handle != handle) continue;
                if (s.Fading || !s.Src.isPlaying) return false;
                s.EndAt = -1f;
                s.Src.loop = true;
                return true;
            }
            return false;
        }

        /// <summary>Iteration 11: true while that play still sounds and is not fading out.</summary>
        public bool IsPlaying(int handle)
        {
            if (handle == 0) return false;
            for (int i = 0; i < Size; i++)
            {
                var s = _slots[i];
                if (s.Active && s.Handle == handle) return !s.Fading && s.Src.isPlaying;
            }
            return false;
        }

        /// <summary>Iteration 11: fades that play out (nothing when it already ended or its slot went to another sound).</summary>
        public void FadeOut(int handle, float seconds)
        {
            if (handle == 0) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < Size; i++) if (_slots[i].Active && _slots[i].Handle == handle) Fade(_slots[i], seconds, now);
        }

        /// <summary>Iteration 11: fades out every copy of a SoundEntries row that follows this transform (a loop sound played again on
        /// the same object replaces its copy instead of stacking a second one).</summary>
        public void FadeOutEntry(Transform follow, int entryId, float seconds)
        {
            if (follow == null || entryId <= 0) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < Size; i++)
            {
                var s = _slots[i];
                if (s.Active && s.EntryId == entryId && s.Follow == follow) Fade(s, seconds, now);
            }
        }

        /// <summary>Iteration 11: fades out every looping sound (the session ended: nothing it looped for exists any more).</summary>
        public void FadeOutLoops(float seconds)
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < Size; i++) if (_slots[i].Active && _slots[i].Src.loop) Fade(_slots[i], seconds, now);
        }

        public void Stop(AudioSource src)
        {
            if (src == null) return;
            for (int i = 0; i < Size; i++) if (_slots[i].Src == src) Release(_slots[i]);
        }

        public void StopAll()
        {
            for (int i = 0; i < Size; i++) Release(_slots[i]);
        }

        public void Dispose()
        {
            StopAll();
            if (_root != null) Object.Destroy(_root);
        }
    }
}
