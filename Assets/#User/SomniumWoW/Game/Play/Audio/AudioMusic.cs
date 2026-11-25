using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// Zone music and ambience like the client, only while a session runs; the WotLK login music in the black room.
    /// The area under the player (the MCNK area id of WowGround's tile, AreaTable) -> ZoneMusic (day/night SoundEntries, the silence
    /// interval between tracks: 180-300 s in every start zone) and SoundAmbience (day/night loops); subzone fields that are 0 inherit
    /// the parent zone's (Shadowglen has no ambience of its own and plays Teldrassil's). A change of zone crossfades (music 3 s,
    /// ambience 2 s); entering a zone with a ZoneIntroMusicTable sound plays it first unless it played within MinDelayMinutes.
    /// Night = 20:00-6:00 of the time of day chosen on the room panel (an approximation of the client's day/night switch).
    /// </summary>
    internal sealed class AudioMusic
    {
        private sealed class Layer
        {
            public readonly AudioSource[] Src = new AudioSource[2];
            public readonly AudioClip[] Clip = new AudioClip[2];
            public readonly string[] Temp = new string[2];
            public readonly bool[] Owned = new bool[2];
            public readonly float[] Level = new float[2];
            public readonly float[] Want = new float[2];
            public readonly float[] Base = new float[2];
            public readonly float[] StartedAt = new float[2];
            public int Active;
            public float FadeSeconds;
            public SoundCategory Cat;

            /// <summary>A clip is (about to be) heard: wanted up and its source playing (or just started).</summary>
            public bool Playing
            {
                get
                {
                    int i = Active;
                    return Clip[i] != null && Want[i] > 0f && (Src[i].isPlaying || Time.unscaledTime - StartedAt[i] < 1f);
                }
            }
        }

        private readonly Layer _music = new Layer { FadeSeconds = 3f, Cat = SoundCategory.Music };
        private readonly Layer _amb = new Layer { FadeSeconds = 2f, Cat = SoundCategory.Ambience };
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<int, float> _introPlayed = new Dictionary<int, float>();
        private readonly GameObject _root;
        private readonly AudioClips _clips;

        private enum Mode { Off, Room, Zone }
        private Mode _mode = Mode.Off;
        private int _musicId = -1;          // ZoneMusic id in effect
        private int _ambEntry = -1;         // SoundEntries id of the ambience loop in effect
        private int _areaId = -1;
        private float _nextTrackAt = -1f;
        private int _generation;
        private bool _loading;
        private float _nextAreaCheck;
        private bool _wasMusicOn = true;
        private string _lastZoneLine;

        public const string LoginMusic = @"Sound\Music\GlueScreenMusic\WotLK_main_title.mp3";

        public AudioMusic(Transform parent, AudioClips clips)
        {
            _clips = clips;
            _root = new GameObject("WoW Audio music");
            _root.transform.SetParent(parent, false);
            for (int i = 0; i < 2; i++)
            {
                _music.Src[i] = MakeSource("music " + i);
                _amb.Src[i] = MakeSource("ambience " + i);
            }
        }

        private AudioSource MakeSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            s.priority = 16;
            return s;
        }

        public string ZoneLine { get { return _lastZoneLine; } }

        /// <summary>
        /// Stage O2 (owner ghost): the player is a ghost (PlayerState.Ghost, set by WowAudioDriver every frame). Like the client, the zone's
        /// music and ambience give way to the ghost's: ZoneMusic 'GhostMusic (DONOTRENAME)' and the loop 'Ghost (DONOTRENAME)'
        /// (WowSoundData.GhostZone, measured); both crossfade back to the zone's at the resurrection.
        /// </summary>
        public bool Ghost;
        private bool _wasGhost;

        private ZoneSoundInfo ZoneOf(WowSoundData data, int areaId)
        {
            if (Ghost)
            {
                var g = data.GhostZone();
                if (g != null) return g;
            }
            return data.Zone(areaId);
        }

        public bool Uses(AudioClip c)
        {
            if (c == null) return false;
            for (int i = 0; i < 2; i++) if (_music.Clip[i] == c || _amb.Clip[i] == c) return true;
            return false;
        }

        public static bool IsNight(float hours)
        {
            float h = hours % 24f;
            if (h < 0f) h += 24f;
            return h >= 20f || h < 6f;
        }

        /// <summary>Per frame. inRoom / zone = what the player is in; data/ground may be null (nothing plays).</summary>
        public void Update(bool inRoom, bool inSession, WowSoundData data, WowGround ground, int mapId, float wowX, float wowY, bool havePos,
            float timeOfDay, Action<int, float, float> requestTile)
        {
            float now = Time.unscaledTime;
            bool musicOn = WowSettings.Music && WowSettings.MusicVolume > 0.001f && WowSettings.MasterVolume > 0.001f;
            Mode want = inSession && data != null && ground != null ? Mode.Zone : inRoom && data != null ? Mode.Room : Mode.Off;
            if (want != _mode)
            {
                _mode = want;
                _generation++;
                _loading = false;
                _musicId = -1; _ambEntry = -1; _areaId = -1; _nextTrackAt = -1f;
                FadeOut(_music);
                FadeOut(_amb);
                if (_mode == Mode.Room) WowLog.Info("Audio: room - login music " + LoginMusic);
            }

            if (_mode == Mode.Room)
            {
                if (musicOn && !_music.Playing && !_loading && now >= _nextTrackAt) StartTrack(LoginMusic, true);
                if (!musicOn && _music.Playing) FadeOut(_music);
            }
            else if (_mode == Mode.Zone && havePos)
            {
                if (Ghost != _wasGhost)
                {
                    _wasGhost = Ghost;
                    _nextAreaCheck = 0f;   // switch at once
                    var g = Ghost && data != null ? data.GhostZone() : null;
                    WowLog.Info("Audio: " + (Ghost ? (g != null ? "ghost - music " + g.ZoneMusicId + " '" + g.MusicSetName + "' (day " + g.MusicDay + ", night " + g.MusicNight + "), ambience " + g.AmbienceDay : "ghost - the ghost music / ambience rows are missing, the zone's stay") : "alive again - the zone's music and ambience") + ".");
                }
                if (now >= _nextAreaCheck)
                {
                    _nextAreaCheck = now + 0.5f;
                    bool haveWmo = WmoSound(data);   // lead 2026-10-04: the building under the feet (its WMOAreaTable sound row)
                    var s = ground.Query(mapId, wowX, wowY);
                    if (!s.Known && requestTile != null) requestTile(mapId, wowX, wowY);
                    // duelsnd: WMO-only maps (the Deeprun Tram, dungeons) have no terrain - the building's row alone
                    if (s.Known || haveWmo) ZoneTick(data, s.Known ? s.AreaId : 0, IsNight(timeOfDay), now, musicOn, haveWmo);
                }
                if (musicOn && _musicId > 0 && !_loading && !_music.Playing)
                {
                    if (_nextTrackAt < 0f) _nextTrackAt = now + SilenceSeconds(data, IsNight(timeOfDay));
                    else if (now >= _nextTrackAt) { _nextTrackAt = -1f; StartZoneTrack(data, IsNight(timeOfDay), false); }
                }
                if (musicOn && !_wasMusicOn && _musicId > 0 && !_music.Playing && !_loading) StartZoneTrack(data, IsNight(timeOfDay), false);
                if (!musicOn && _music.Playing) FadeOut(_music);
            }
            _wasMusicOn = musicOn;
            Fade(_music, Time.unscaledDeltaTime);
            Fade(_amb, Time.unscaledDeltaTime);
        }

        // Lead 2026-10-04 (the user: "make sure all the music is correct, like walking into taverns"): the WMO group under the feet and
        // its WMOAreaTable sound row (World/WmoAreaTable.TryGetSoundRow: the group's row, else the WMO-wide row). As the client resolves a
        // building's sound: the row's own ZoneMusic / AmbienceID / IntroSound, else its AreaTableID's (Zone: subzone fields inherit their
        // parent), else the terrain zone's - an inn plays its tavern music and ambience ("Lion's Pride Inn": music 156 Zone-TavernAlliance,
        // ambience 170). Was (duelsnd): the building's ambience only where the zone had none, its music never. A change must hold for
        // SettleSeconds (ours) so a doorway does not flip the music back and forth.
        public const float SettleSeconds = 1f;
        private int _wmoMusic, _wmoAmbience, _wmoIntro, _wmoArea, _wmoKey = -1, _wmoPendKey = -1;
        private float _wmoPendSince;
        private string _wmoName;

        /// <summary>Reads the building under the feet (every 0.5 s); true = it has a WMOAreaTable sound row in effect now.</summary>
        private bool WmoSound(WowSoundData data)
        {
            Vector3 feet;
            int music = 0, amb = 0, intro = 0, area = 0, key = 0;
            string name = null;
            if (data != null && WowPlay.Files != null && WowPlayer.TryFeet(out feet))
            {
                float floorY;
                var g = SomniumSpace.Worlds.SomniumWoW.Game.Mounts.MountPlace.FindWmoFloor(feet, out floorY);
                if (g != null && SomniumSpace.Worlds.SomniumWoW.World.WmoAreaTable.Get(WowPlay.Files).TryGetSoundRow(g.WmoId, g.NameSet, g.GroupId, out amb, out music, out intro, out area)
                    && (amb != 0 || music != 0 || intro != 0 || area != 0))
                {
                    key = (((music * 397) ^ amb) * 397 ^ intro) * 397 ^ area;
                    if (key == 0) key = 1;
                    name = "WMO " + g.WmoId + " set " + g.NameSet + " group " + g.GroupId;
                }
            }
            float now = Time.unscaledTime;
            if (key != _wmoKey)
            {
                if (key != _wmoPendKey) { _wmoPendKey = key; _wmoPendSince = now; }
                else if (now - _wmoPendSince >= SettleSeconds || _wmoKey == -1)
                {
                    _wmoKey = key; _wmoMusic = music; _wmoAmbience = amb; _wmoIntro = intro; _wmoArea = area; _wmoName = name;
                }
            }
            else _wmoPendKey = key;
            return _wmoKey > 0;
        }

        private void ZoneTick(WowSoundData data, int areaId, bool night, float now, bool musicOn, bool haveWmo)
        {
            var z = ZoneOf(data, areaId);
            int zoneKey = areaId;
            if (haveWmo && !Ghost)
            {
                // the building's row, else its linked area, else the terrain zone (lead 2026-10-04)
                var linked = _wmoArea > 0 ? data.Zone(_wmoArea) : null;
                int m = _wmoMusic != 0 ? _wmoMusic : linked != null && linked.ZoneMusicId != 0 ? linked.ZoneMusicId : z != null ? z.ZoneMusicId : 0;
                int a = _wmoAmbience != 0 ? _wmoAmbience : linked != null && linked.AmbienceId != 0 ? linked.AmbienceId : z != null ? z.AmbienceId : 0;
                int i = _wmoIntro != 0 ? _wmoIntro : linked != null && linked.IntroId != 0 ? linked.IntroId : z != null ? z.IntroId : 0;
                string nm = linked != null && !string.IsNullOrEmpty(linked.Name) ? linked.Name : z != null ? z.Name : "";
                z = data.Compose(nm + " (" + _wmoName + ")", areaId, m, a, i);
                zoneKey = areaId ^ (_wmoKey * 7919);
            }
            _zone = z;
            int musicId = z != null ? z.ZoneMusicId : 0;
            int amb = z != null ? (night && z.AmbienceNight > 0 ? z.AmbienceNight : z.AmbienceDay) : 0;
            if (zoneKey != _areaId)
            {
                _areaId = zoneKey;
                string line = z == null ? "area " + areaId + " (not in AreaTable)" : "'" + z.Name + "' (area " + areaId + "): music " + musicId + " '" + z.MusicSetName
                    + "' (day " + z.MusicDay + ", night " + z.MusicNight + "), ambience " + z.AmbienceId + " (day " + z.AmbienceDay + ", night " + z.AmbienceNight
                    + "), intro " + z.IntroId + (haveWmo && !Ghost ? " - the building's sound row" : "");
                if (line != _lastZoneLine) WowLog.Info("Audio: zone " + line);
                _lastZoneLine = line;
            }
            if (musicId != _musicId)
            {
                bool first = _musicId < 0;
                _musicId = musicId;
                _nextTrackAt = -1f;
                if (musicId <= 0) FadeOut(_music);
                else if (musicOn)
                {
                    bool intro = false;
                    if (z.IntroSound > 0)
                    {
                        float last;
                        if (!_introPlayed.TryGetValue(z.IntroId, out last) || now - last > Math.Max(1, z.IntroMinDelayMinutes) * 60f)
                        {
                            _introPlayed[z.IntroId] = now;
                            intro = true;
                        }
                    }
                    StartZoneTrack(data, night, intro);
                }
                else if (!first) FadeOut(_music);
            }
            if (amb != _ambEntry)
            {
                _ambEntry = amb;
                if (amb <= 0) FadeOut(_amb);
                else StartAmbience(data, amb);
            }
        }

        private ZoneSoundInfo _zone;   // lead 2026-10-04: the sound set in effect (the zone's, or a building's composed one)

        private float SilenceSeconds(WowSoundData data, bool night)
        {
            var z = _zone;
            if (z == null) return 180f;
            int mn = night ? z.SilenceMinNight : z.SilenceMinDay, mx = night ? z.SilenceMaxNight : z.SilenceMaxDay;
            if (mn <= 0 && mx <= 0) return 180f;
            if (mx < mn) mx = mn;
            return (mn + (float)_rng.NextDouble() * (mx - mn)) / 1000f;
        }

        private void StartZoneTrack(WowSoundData data, bool night, bool intro)
        {
            var z = _zone;
            if (z == null) return;
            int entryId = intro ? z.IntroSound : (night && z.MusicNight > 0 ? z.MusicNight : z.MusicDay);
            var e = data.Entry(entryId);
            string file = e != null ? e.Pick(_rng) : null;
            if (file == null) return;
            StartTrack(file, false, e.Volume);
        }

        private void StartTrack(string gamePath, bool loop, float volume = 1f)
        {
            _loading = true;
            int gen = _generation;
            if (WowWav.IsMp3Path(gamePath))
            {
                _clips.LoadStream(gamePath, (clip, temp) =>
                {
                    if (gen != _generation) { AudioClips.ReleaseStream(clip, temp); return; }
                    _loading = false;
                    if (clip == null) { _nextTrackAt = Time.unscaledTime + 8f; return; }   // a set file the MPQs lack (5 of 703, AudSmoke): another pick soon
                    CrossTo(_music, clip, temp, true, volume, loop);
                    WowLog.Info("Audio: music " + gamePath + (loop ? " (loop)" : ""));
                });
            }
            else
            {
                _clips.Get(gamePath, clip =>
                {
                    if (gen != _generation) return;
                    _loading = false;
                    if (clip == null) { _nextTrackAt = Time.unscaledTime + 8f; return; }   // a set file the MPQs lack (5 of 703, AudSmoke): another pick soon
                    CrossTo(_music, clip, null, false, volume, loop);
                    WowLog.Info("Audio: music " + gamePath + (loop ? " (loop)" : ""));
                });
            }
        }

        private void StartAmbience(WowSoundData data, int entryId)
        {
            var e = data.Entry(entryId);
            string file = e != null ? e.Pick(_rng) : null;
            if (file == null) { FadeOut(_amb); return; }
            int gen = _generation;
            float vol = e.Volume;
            _clips.Get(file, clip =>
            {
                if (gen != _generation || entryId != _ambEntry || clip == null) return;
                CrossTo(_amb, clip, null, false, vol, true);
                WowLog.Info("Audio: ambience " + e.Name + " (" + file + ", " + clip.length.ToString("0") + " s loop)");
            });
        }

        private void CrossTo(Layer l, AudioClip clip, string temp, bool owned, float baseVolume, bool loop)
        {
            int next = l.Clip[l.Active] == null ? l.Active : 1 - l.Active;
            Release(l, next);
            l.Want[1 - next] = 0f;
            l.Active = next;
            var s = l.Src[next];
            l.Clip[next] = clip;
            l.Temp[next] = temp;
            l.Owned[next] = owned;
            l.Base[next] = baseVolume;
            l.Level[next] = 0f;
            l.Want[next] = 1f;
            l.StartedAt[next] = Time.unscaledTime;
            s.clip = clip;
            s.loop = loop;
            s.volume = 0f;
            s.Play();
        }

        private static void FadeOut(Layer l)
        {
            l.Want[0] = 0f;
            l.Want[1] = 0f;
        }

        /// <summary>Moves each source's level toward its target; frees sources that faded out or finished their (non-loop) clip.</summary>
        private static void Fade(Layer l, float dt)
        {
            float step = l.FadeSeconds > 0f ? dt / l.FadeSeconds : 1f;
            float cat = AudioPool.CategoryVolume(l.Cat);
            float now = Time.unscaledTime;
            for (int i = 0; i < 2; i++)
            {
                var s = l.Src[i];
                if (l.Clip[i] == null) continue;
                l.Level[i] = Mathf.MoveTowards(l.Level[i], l.Want[i], step);
                s.volume = l.Base[i] * l.Level[i] * cat;
                bool faded = l.Want[i] <= 0f && l.Level[i] <= 0f;
                bool ended = !s.isPlaying && now - l.StartedAt[i] > 1f;
                if (faded || ended) Release(l, i);
            }
        }

        private static void Release(Layer l, int i)
        {
            var s = l.Src[i];
            if (s != null) { s.Stop(); s.loop = false; s.clip = null; }   // iteration 11: a freed source never keeps a loop setting (CrossTo sets it per clip)
            if (l.Owned[i]) AudioClips.ReleaseStream(l.Clip[i], l.Temp[i]);
            l.Clip[i] = null;
            l.Temp[i] = null;
            l.Owned[i] = false;
            l.Level[i] = 0f;
            l.Want[i] = 0f;
        }

        public void Dispose()
        {
            _generation++;
            for (int i = 0; i < 2; i++) { Release(_music, i); Release(_amb, i); }
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
