using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// The sound engine of the WoW Dreamport (one per scene, on the WoW Game object next to WowGame; the front end adds it, and
    /// WowAudio.Ensure adds it there itself if it is missing). Owns the audio worker thread, the clip cache (AudioClips), the one-shot
    /// source pool (AudioPool), zone music / ambience / login music (AudioMusic) and the local avatar's footsteps (AudioFootsteps);
    /// the event sounds (weapons, quests, loot, level, death, NPC greetings, race error voice lines) are in WowAudioDriver.Events.cs.
    /// The sound tables (WowSoundData) and the ground reader (WowGround) are built on the worker as soon as WowGame has the game
    /// files; until then every request is dropped quietly (logged once). Volumes: WowSettings.MasterVolume x the category volume,
    /// re-applied on WowSettings.Changed.
    /// </summary>
    public sealed partial class WowAudioDriver : MonoBehaviour
    {
        public static WowAudioDriver Instance { get; private set; }
        private static bool _noGameLogged;

        private AudioWorker _worker;
        private AudioClips _clips;
        private AudioPool _pool;
        private AudioMusic _music;
        private readonly AudioFootsteps _steps = new AudioFootsteps();
        private readonly AudioMountSteps _mountSteps = new AudioMountSteps();   // iteration 7: the ridden mount's own sounds
        private bool _rideLogged;
        private GameObject _root;
        private WowFiles _files;
        private WowSoundData _data;
        private WowGround _ground;
        /// <summary>Iteration 7 (lead contract, PLAN7 s3.8): the terrain liquid/area/ground source for MountPlace (null until the sound tables are read).</summary>
        public static WowGround Ground { get; private set; }
        private bool _loadingData;
        private int _loadGeneration;
        private readonly System.Random _rng = new System.Random();
        private readonly HashSet<long> _tileRequests = new HashSet<long>();
        private readonly HashSet<string> _wmoRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _missingEntries = new HashSet<int>();
        private bool _earlyLogged;
        private int _tileLines;
        private float _nextStats;
        private ISomniumPlayer _player;
        private float _nextPlayerLookup;

        /// <summary>True once the sound tables are read.</summary>
        public bool DataReady { get { return _data != null; } }

        /// <summary>One line for diagnostics (the front end may show it).</summary>
        public string StatusLine
        {
            get
            {
                return (_data != null ? _data.SoundEntryCount + " sound entries" : "tables not read") + ", " + (_clips != null ? _clips.StatsLine : "")
                    + ", steps " + _steps.Steps + (string.IsNullOrEmpty(_steps.LastSurface) ? "" : " (last on " + _steps.LastSurface + ")")
                    + (_mountSteps.Steps + _mountSteps.Flaps > 0 ? ", mount steps " + _mountSteps.Steps + ", wing flaps " + _mountSteps.Flaps : "")
                    + (_music != null && !string.IsNullOrEmpty(_music.ZoneLine) ? ", zone " + _music.ZoneLine : "");
            }
        }

        /// <summary>The driver (added to WowGame.Current's object when missing); null while there is no WowGame.</summary>
        internal static WowAudioDriver Ensure()
        {
            if (Instance != null) return Instance;
            var g = WowGame.Current;
            if (g == null)
            {
                if (!_noGameLogged) { _noGameLogged = true; WowLog.Info("Audio: a sound was requested before the WoW Game object exists - dropped (logged once)"); }
                return null;
            }
            var d = g.GetComponent<WowAudioDriver>();
            if (d == null)
            {
                d = g.gameObject.AddComponent<WowAudioDriver>();
                WowLog.Info("Audio: WowAudioDriver was not on the WoW Game object - added by WowAudio");
            }
            if (Instance == null) Instance = d;
            return d;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _root = new GameObject("WoW Audio");
            _worker = new AudioWorker();
            _worker.Start();
            _pool = new AudioPool(_root.transform);
            string temp = null;
            try { temp = Path.Combine(Application.temporaryCachePath, "SomniumWoWAudio"); }
            catch (Exception e) { WowLog.Warn("Audio: no temporary cache folder (" + e.Message + ") - MP3 music is off"); }
            if (temp != null) AudioClips.CleanTempDir(temp);
            _clips = new AudioClips(_worker, this, temp, c => (_pool != null && _pool.Uses(c)) || (_music != null && _music.Uses(c)));
            _music = new AudioMusic(_root.transform, _clips);
            WowSettings.Changed += OnSettingsChanged;
            WowPlay.Event += OnGameEvent;
            WowPlay.Started += OnSessionStarted;
            WowPlay.Ended += OnSessionEnded;
            WowLog.Info("Audio: driver awake (" + AudioPool.Size + " pooled sources, clip cache " + (AudioClips.CapBytes / (1024 * 1024)) + " MB, temp " + (temp ?? "none") + ")");
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            WowSettings.Changed -= OnSettingsChanged;
            WowPlay.Event -= OnGameEvent;
            WowPlay.Started -= OnSessionStarted;
            WowPlay.Ended -= OnSessionEnded;
            if (_worker != null) _worker.Stop();
            if (_music != null) _music.Dispose();
            if (_pool != null) _pool.Dispose();
            if (_clips != null) _clips.Clear();
            if (_root != null) Destroy(_root);
            _data = null;
            _ground = null;
            Ground = null;
        }

        private void OnSettingsChanged()
        {
            if (_pool != null) _pool.ApplyVolumes();
        }

        // ------------------------------------------------------------------------------------------ per frame

        private void Update()
        {
            var game = WowGame.Current;
            WowFiles files = game != null ? game.Files : null;
            if (files == null) files = WowPlay.Files;
            if (_worker == null || Instance != this) return;   // a duplicate being destroyed
            if (!ReferenceEquals(files, _files)) OnFiles(files);

            _worker.Pump(2f);
            _clips.Pump(3f);
            _pool.Update();
            PruneLoops();

            var s = WowPlay.Session;
            bool inSession = s != null && game != null && game.Map != null && _data != null;
            // 13.8 (the user: "Please remove the original lobby music, I've added my own"): no WotLK login music while the lobby stands -
            // its own fire / wind AudioSources (in WOW_LobbyAssets.prefab) play instead
            bool inRoom = s == null && game != null && game.Map == null && files != null && !WowLobby.AnyShown;
            int mapId = s != null ? s.MapId : -1;
            float wx = 0f, wy = 0f;
            bool havePos = false;
            if (s != null && s.Player != null) { wx = s.Player.Pos.X; wy = s.Player.Pos.Y; havePos = true; }
            float tod = game != null ? game.TimeOfDay : 12f;
            _music.Ghost = s != null && s.Player != null && s.Player.Ghost;   // stage O2 (ghost): the ghost's music and ambience (AudioMusic.Ghost)
            try { _music.Update(inRoom, inSession, _data, _ground, mapId, wx, wy, havePos, tod, RequestTile); }
            catch (Exception e) { LogThrow("music", e); }

            if (inSession)
            {
                Transform root = LocalRoot();
                // Iteration 7: while a ride owns the avatar (mounted, or falling after a dismount in the air) the avatar's own steps
                // fall silent; the mount's steps / wings / take-off and landing are AudioMountSteps.
                bool ride = WowMount.OwnsAvatar;
                if (ride != _rideLogged)
                {
                    _rideLogged = ride;
                    WowLog.Info("Audio: " + (ride ? "a ride owns the avatar - its footsteps off, the mount's own sounds on" : "the ride let go - the avatar's footsteps back") + ".");
                }
                if (!ride && root != null && s.Player != null && !s.Player.Dead)
                {
                    try { _steps.Update(this, _data, _ground, mapId, root.position, root, s.Player.Race, WowSettings.VoiceGender == 1 ? 1 : 0); }
                    catch (Exception e) { LogThrow("footsteps", e); }
                }
                else _steps.Reset();
                try { _mountSteps.Update(this, _worker, _files, _data, _ground, mapId, s.Player); }
                catch (Exception e) { LogThrow("mount sounds", e); }
                try { TickSession(s); }
                catch (Exception e) { LogThrow("session sounds", e); }
            }
            else { _steps.Reset(); _mountSteps.Reset(); }

            if (Time.unscaledTime >= _nextStats && _data != null)
            {
                _nextStats = Time.unscaledTime + 300f;
                WowLog.Info("Audio: " + StatusLine);
            }
        }

        private int _throws;
        private void LogThrow(string what, Exception e)
        {
            if (_throws++ < 10) WowLog.Error("Audio: " + what + " threw: " + e);
        }

        /// <summary>The local Somnium avatar's root (feet), looked up through the Bridge (re-tried every second while missing).</summary>
        internal Transform LocalRoot()
        {
            ISomniumPlayerBody body = null;
            if (_player == null && Time.unscaledTime >= _nextPlayerLookup)
            {
                _nextPlayerLookup = Time.unscaledTime + 1f;
                try { _player = SomniumBridge.PlayersContainer?.LocalPlayer; }
                catch (Exception) { _player = null; }
            }
            if (_player == null) return null;
            try { body = _player.References?.Body; }
            catch (Exception) { body = null; _player = null; }
            return body != null ? body.Root : null;
        }

        private void OnFiles(WowFiles files)
        {
            _files = files;
            _clips.Files = files;
            _data = null;
            _ground = null;
            Ground = null;
            _tileRequests.Clear();
            _wmoRequests.Clear();
            _missingEntries.Clear();
            _clips.Clear();
            if (files == null) return;
            int gen = ++_loadGeneration;
            _loadingData = true;
            _worker.Enqueue(() =>
            {
                WowSoundData data = null;
                WowGround ground = null;
                string error = null;
                try
                {
                    data = WowSoundData.Load(files);
                    ground = new WowGround(files, data);
                }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; data = null; }
                _worker.Post(() =>
                {
                    if (gen != _loadGeneration) return;
                    _loadingData = false;
                    if (data == null) { WowLog.Error("Audio: the sound tables could not be read (" + (error ?? "?") + ") - the Dreamport stays silent"); return; }
                    _data = data;
                    _ground = ground;
                    Ground = ground;
                    WowLog.Info("Audio: ready in " + data.LoadMs + " ms - " + data.Summary);
                    OnDataReady();
                });
            }, false);
        }

        /// <summary>Loads the terrain tile under (x, y) on the worker (once per tile) for footsteps / zone music.</summary>
        internal void RequestTile(int mapId, float x, float y)
        {
            var ground = _ground;
            if (ground == null || mapId < 0) return;
            int tx, ty;
            WowGround.TileOf(x, y, out tx, out ty);
            long key = ((long)mapId << 32) | ((long)(tx & 0xFFFF) << 16) | (long)(ty & 0xFFFF);
            if (ground.HasTile(mapId, x, y)) { _tileRequests.Remove(key); return; }
            if (!_tileRequests.Add(key)) return;
            _worker.Enqueue(() =>
            {
                string line = ground.LoadTile(mapId, x, y);
                _worker.Post(() =>
                {
                    _tileRequests.Remove(key);
                    if (_tileLines++ < 30) WowLog.Info("Audio: ground " + line);
                });
            }, false);
        }

        /// <summary>Builds a WMO group's floor types on the worker (once).</summary>
        internal void RequestWmoGroup(string rootPath, int group)
        {
            var ground = _ground;
            if (ground == null || string.IsNullOrEmpty(rootPath)) return;
            string key = rootPath + "#" + group;
            if (!_wmoRequests.Add(key)) return;
            _worker.Enqueue(() =>
            {
                var g = ground.BuildGroup(rootPath, group);
                if (g != null && g.Error != null) _worker.Post(() => { if (_tileLines++ < 30) WowLog.Warn("Audio: WMO floor " + key + ": " + g.Error); });
            }, false);
        }

        // ------------------------------------------------------------------------------------------ playback

        internal void PlayEntry(int soundEntryId, SoundCategory cat, Vector3 pos, Transform follow, bool is2D, float volume)
        {
            PlayEntryFull(soundEntryId, cat, pos, follow, is2D, volume, 1f, 1);
        }

        /// <summary>A 3D entry with its own pitch and pool priority (footsteps).</summary>
        internal void PlayEntryWith(int soundEntryId, SoundCategory cat, Vector3 pos, float volume, float pitch, int priority)
        {
            PlayEntryFull(soundEntryId, cat, pos, null, false, volume, pitch, priority);
        }

        internal void PlayEntryFull(int soundEntryId, SoundCategory cat, Vector3 pos, Transform follow, bool is2D, float volume, float pitch, int priority)
        {
            if (soundEntryId <= 0) return;
            if (_data == null) { LogEarly(); return; }
            if (volume * AudioPool.CategoryVolume(cat) <= 0.0001f) return;
            var e = _data.Entry(soundEntryId);
            string path = e != null ? e.Pick(_rng) : null;
            if (path == null)
            {
                if (_missingEntries.Add(soundEntryId) && _missingEntries.Count <= 30)
                    WowLog.Warn("Audio: sound entry " + soundEntryId + (e == null ? " is not in SoundEntries.dbc" : " ('" + e.Name + "') has no file") + " - logged once");
                return;
            }
            // Iteration 11 (audio fix): a loop file (SoundEntries Flags 0x200, SoundEntryInfo.Loops) asked for again on the same object -
            // the next precast of a spell cast over and over - replaces its copy instead of stacking a second one on it. Played this
            // way it still runs once only: the one-shot path never loops (WowAudio.PlaySoundEntryLoop is the way to last).
            int replace = e.Loops && follow != null ? soundEntryId : 0;
            PlayPath(path, cat, e.Volume * volume, is2D, pos, follow, e.MinDistance, e.Cutoff, pitch, priority, soundEntryId, replace);
        }

        internal void PlayPath(string gamePath, SoundCategory cat, float volume, bool is2D, Vector3 pos, Transform follow, float minYards, float cutoffYards, float pitch)
        {
            PlayPath(gamePath, cat, volume, is2D, pos, follow, minYards, cutoffYards, pitch, 1, 0, 0);
        }

        internal void PlayPath(string gamePath, SoundCategory cat, float volume, bool is2D, Vector3 pos, Transform follow, float minYards, float cutoffYards, float pitch, int priority)
        {
            PlayPath(gamePath, cat, volume, is2D, pos, follow, minYards, cutoffYards, pitch, priority, 0, 0);
        }

        /// <summary>entryId tags the play (0 = a bare file); replaceEntry &gt; 0 first fades out that entry's copies on the same follow.</summary>
        private void PlayPath(string gamePath, SoundCategory cat, float volume, bool is2D, Vector3 pos, Transform follow, float minYards, float cutoffYards, float pitch, int priority,
            int entryId, int replaceEntry)
        {
            if (string.IsNullOrEmpty(gamePath)) return;
            if (_files == null) { LogEarly(); return; }
            if (volume * AudioPool.CategoryVolume(cat) <= 0.0001f) return;
            AudioClip clip;
            if (_clips.TryGet(gamePath, out clip))
            {
                if (replaceEntry > 0) _pool.FadeOutEntry(follow, replaceEntry, AudioPool.StopFade);
                _pool.PlayHandle(clip, cat, volume, is2D, pos, follow, minYards, cutoffYards, pitch, priority, false, 0f, entryId);
                return;
            }
            float asked = Time.unscaledTime;
            float maxWait = cat == SoundCategory.Footsteps ? 0.35f : cat == SoundCategory.Effects ? 0.8f : 2f;
            bool hadFollow = follow != null;
            _clips.Get(gamePath, c =>
            {
                if (c == null || _pool == null || Time.unscaledTime - asked > maxWait) return;   // too late: the moment has passed
                Transform f = follow;
                if (hadFollow && f == null) return;   // what it followed is gone
                if (replaceEntry > 0) _pool.FadeOutEntry(f, replaceEntry, AudioPool.StopFade);
                _pool.PlayHandle(c, cat, volume, is2D, f != null ? f.position : pos, f, minYards, cutoffYards, pitch, priority, false, 0f, entryId);
            });
        }

        // ------------------------------------------------------------------------------------------ iteration 11: sounds that last for a reason

        // Iteration 11 (audio fix; the user: "some sounds don't loop properly or they play for too long"). Cause: every spell kit sound
        // went through the one-shot PlaySoundEntry, whatever kit it came from - a precast, channel, aura state or ground area kit (and a
        // missile's flight) played its file exactly once, measured (harness AudKitSounds / AudSpellSounds): Shadow Bolt's precast
        // 'Precast Shadow Low' is PreCastShadowMagicLow.wav 6.6 s, so the 1.2 s cast of spell 686 in the user's log sounded 5.4 s past
        // its end; the mount summons' 'Precast Holy High' 5.0 s outlasted the 1.5 s cast; the Hearthstone's 'Precast Nature High' 4.1 s
        // fell silent 6 s before its 10 s cast ended; Drain Life's channel 'LifeDrainLoop' 4.9 s stopped in the middle of a 5 s channel
        // ... and nothing could stop any of them. Now such a sound is keyed like WowSpellVfx's loops (kind, key): a loop file (Flags
        // 0x200) loops until StopEntryLoop, its life cap or the followed object's end; any other file plays once and is cut there too.
        private struct LoopKey : IEquatable<LoopKey>
        {
            public readonly int Kind;
            public readonly long Key;
            public LoopKey(int kind, long key) { Kind = kind; Key = key; }
            public bool Equals(LoopKey o) { return Kind == o.Kind && Key == o.Key; }
            public override bool Equals(object o) { return o is LoopKey && Equals((LoopKey)o); }
            public override int GetHashCode() { return Kind * 397 ^ Key.GetHashCode(); }
        }

        private sealed class LoopPlay
        {
            public int EntryId;
            public int Handle;      // AudioPool handle once it sounds (0 while its clip loads, or when it could not play)
            public bool Loading;
            public bool Loop;       // a loop file (Flags 0x200); else it played once for this reason
            public Transform Follow;
            public bool HadFollow;
            public bool Held;       // lead 2026-10-11: a held spell - loops with no cap until its stop
        }

        private readonly Dictionary<LoopKey, LoopPlay> _loops = new Dictionary<LoopKey, LoopPlay>();
        // Ours: above effects (1) and footsteps (2), so a running loop is not cut by the pool stealing its slot for a one-shot.
        private const int LoopPriority = 3;

        /// <summary>
        /// A SoundEntries sound that lasts as long as its reason (kind / key like WowSpellVfx: 1 precast, 2 channel, 3 aura state,
        /// 4 ground area, 5 missile flight - any caller-chosen kind works). Loop files loop, other files play once; maxSeconds &gt; 0
        /// caps it (the cast / channel length the caller knows). The same entry for the same (kind, key) again before StopEntryLoop
        /// does nothing - no restart, no second copy, a once-only file is not played again by an aura refresh; a different entry
        /// replaces it.
        /// </summary>
        internal void PlayEntryLoop(int soundEntryId, Transform follow, int kind, long key, float maxSeconds, bool held = false)
        {
            if (soundEntryId <= 0 || _pool == null) return;
            if (_data == null) { LogEarly(); return; }
            var k = new LoopKey(kind, key);
            LoopPlay old;
            if (_loops.TryGetValue(k, out old))
            {
                bool same = old.EntryId == soundEntryId && ReferenceEquals(old.Follow, follow);
                if (same && (old.Loading || !old.Loop || _pool.IsPlaying(old.Handle))) return;
                _pool.FadeOut(old.Handle, AudioPool.StopFade);
                _loops.Remove(k);
            }
            var e = _data.Entry(soundEntryId);
            string path = e != null ? e.Pick(_rng) : null;
            if (path == null)
            {
                if (_missingEntries.Add(soundEntryId) && _missingEntries.Count <= 30)
                    WowLog.Warn("Audio: sound entry " + soundEntryId + (e == null ? " is not in SoundEntries.dbc" : " ('" + e.Name + "') has no file") + " - logged once");
                return;
            }
            if (e.Volume * AudioPool.CategoryVolume(SoundCategory.Effects) <= 0.0001f) return;
            bool loop = e.Loops || held;
            if (held) maxSeconds = 0f;
            var p = new LoopPlay { EntryId = soundEntryId, Follow = follow, HadFollow = follow != null, Loop = loop, Held = held };
            _loops[k] = p;
            bool is2D = follow == null;
            Vector3 pos = follow != null ? follow.position : Vector3.zero;
            float asked = Time.unscaledTime;
            if (_loopLines++ < 6) WowLog.Info("Audio: sound " + soundEntryId + " '" + e.Name + "' (" + path + ") " + (loop ? "loops" : "plays once (not a loop file)") + " for kind " + kind
                + (maxSeconds > 0f ? ", at most " + maxSeconds.ToString("0.0") + " s" : "") + " - ends with its reason (logged 6 times)");
            AudioClip clip;
            if (_clips.TryGet(path, out clip))
            {
                p.Handle = _pool.PlayHandle(clip, SoundCategory.Effects, e.Volume, is2D, pos, follow, e.MinDistance, e.Cutoff, 1f, LoopPriority, loop, maxSeconds, 0);   // entry tag 0: the one-shot path's FadeOutEntry never touches a keyed sound
                return;
            }
            p.Loading = true;
            bool hadFollow = follow != null;
            _clips.Get(path, c =>
            {
                p.Loading = false;
                LoopPlay cur;
                if (!_loops.TryGetValue(k, out cur) || cur != p) return;    // stopped or replaced while the clip loaded
                Transform f = follow;
                if (c == null || _pool == null || (hadFollow && f == null)) { _loops.Remove(k); return; }
                float left = 0f;
                if (maxSeconds > 0f && !p.Held)
                {
                    left = maxSeconds - (Time.unscaledTime - asked);
                    if (left <= 0.05f) { _loops.Remove(k); return; }        // its time ran out while loading
                }
                p.Handle = _pool.PlayHandle(c, SoundCategory.Effects, e.Volume, is2D, f != null ? f.position : pos, f, e.MinDistance, e.Cutoff, 1f, LoopPriority, loop || p.Held, p.Held ? 0f : left, 0);
            });
        }

        /// <summary>
        /// Lead 2026-10-11 (the user: "loop the sound and effects if I have not released it"): the sound of (kind, key) keeps going -
        /// looped, its cap lifted - until StopEntryLoop; a once-only file that already ended starts again, looping.
        /// </summary>
        internal void HoldEntryLoop(int kind, long key)
        {
            var k = new LoopKey(kind, key);
            LoopPlay p;
            if (!_loops.TryGetValue(k, out p) || p.Held) return;
            p.Held = true;
            p.Loop = true;
            if (p.Loading) return;   // the clip's callback plays it looped
            if (_pool != null && _pool.Hold(p.Handle)) return;
            _loops.Remove(k);
            PlayEntryLoop(p.EntryId, p.Follow, kind, key, 0f, true);
        }
        private int _loopLines;

        /// <summary>Ends the sound of that (kind, key) - a short fade, never a click. Nothing when none runs.</summary>
        internal void StopEntryLoop(int kind, long key)
        {
            var k = new LoopKey(kind, key);
            LoopPlay p;
            if (!_loops.TryGetValue(k, out p)) return;
            _loops.Remove(k);
            if (_pool != null) _pool.FadeOut(p.Handle, AudioPool.StopFade);
        }

        /// <summary>Ends every keyed sound (the session ended).</summary>
        private void StopAllLoops()
        {
            foreach (var p in _loops.Values) if (_pool != null) _pool.FadeOut(p.Handle, AudioPool.StopFade);
            _loops.Clear();
            if (_pool != null) _pool.FadeOutLoops(AudioPool.StopFade);
        }

        /// <summary>
        /// Per frame: forgets keyed sounds whose object is gone, loops that ended by themselves (life cap, slot lost) and plays that
        /// never started (muted, no free slot), so the next request for that key plays again. A once-only file stays remembered until
        /// StopEntryLoop or its object's end.
        /// </summary>
        private void PruneLoops()
        {
            if (_loops.Count == 0) return;
            List<LoopKey> dead = null;
            foreach (var kv in _loops)
            {
                var p = kv.Value;
                if (p.Loading) continue;
                bool gone = (p.HadFollow && p.Follow == null) || p.Handle == 0 || (p.Loop && !_pool.IsPlaying(p.Handle));
                if (gone) (dead ?? (dead = new List<LoopKey>())).Add(kv.Key);
            }
            if (dead != null) foreach (var k in dead) _loops.Remove(k);
        }

        /// <summary>Loads an entry's files in the background so its first play is not late.</summary>
        internal void PrefetchEntry(int soundEntryId)
        {
            if (_data == null || soundEntryId <= 0) return;
            var e = _data.Entry(soundEntryId);
            if (e == null || e.Files == null) return;
            foreach (var f in e.Files) if (!WowWav.IsMp3Path(f)) _clips.Prefetch(f);
        }

        private void LogEarly()
        {
            if (_earlyLogged) return;
            _earlyLogged = true;
            WowLog.Info("Audio: sounds requested before the game files / sound tables are ready are dropped (logged once)" + (_loadingData ? " - tables loading" : ""));
        }
    }
}
