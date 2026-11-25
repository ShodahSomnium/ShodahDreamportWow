using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// Footsteps of the local avatar (desktop and VR) on the WoW world. Timing: from the avatar root's horizontal movement - one
    /// step per stride, the stride growing with speed (0.75 m walking at about 1.4 m/s .. 1.8 m at 5.5 m/s running, so walking
    /// steps come about every 0.5 s and running ones every 0.33 s, close to the client's walk/run animation step events); only
    /// while grounded (a WoW collider within 0.35 m under the feet). Ground: a ray down on layer 0 (Default: terrain tiles "Tile x_y",
    /// WMO groups "Group NNN" under "WMO name", doodads) - terrain -> WowGround's dominant texture layer (GroundEffectTexture ->
    /// TerrainType), WMO -> MOMT GroundType of the hit triangle (or its texture keywords), doodad -> name keywords; shallow terrain
    /// water -> the splash sound, deeper (swimming) -> none. Sound: FootstepTerrainLookup for the race's character model
    /// (CreatureSoundData.SoundFootstepID: 7 medium/large, 8 small) and WowSettings.VoiceGender. WowSettings.Footsteps /
    /// FootstepVolume. Nothing plays in the black room (no WoW collider there). Iteration 7: silent while a ride owns the avatar
    /// (WowMount.OwnsAvatar - the driver resets it instead); the mount's own steps are AudioMountSteps.
    /// </summary>
    internal sealed class AudioFootsteps
    {
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly System.Random _rng = new System.Random();
        private Vector3 _last;
        private bool _haveLast;
        private float _accum;
        private float _still;
        private int _lastType = -1;
        private string _lastSurface;
        private int _typeLines;
        private float _nextTypeLine;
        private bool _firstLogged;

        /// <summary>What the last step was on ("Grass (terrain KalidarFerns)"), for the log / diagnostics.</summary>
        public string LastSurface { get { return _lastSurface; } }
        public int Steps { get; private set; }

        public void Reset()
        {
            _haveLast = false;
            _accum = 0f;
        }

        /// <summary>Per frame. Returns true when a step sound was requested.</summary>
        public bool Update(WowAudioDriver d, WowSoundData data, WowGround ground, int mapId, Vector3 feet, Transform avatarRoot, int race, int gender)
        {
            float dt = Time.deltaTime;
            if (!_haveLast || dt <= 0f) { _last = feet; _haveLast = true; return false; }
            Vector3 delta = feet - _last;
            _last = feet;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist > 5f) { _accum = 0f; return false; }   // teleport
            float speed = dist / dt;
            if (speed < 0.35f)
            {
                _still += dt;
                if (_still > 0.25f) _accum = 0f;
                return false;
            }
            _still = 0f;
            float stride = Mathf.Lerp(0.75f, 1.8f, Mathf.InverseLerp(1.4f, 5.5f, speed));
            if (_accum == 0f) _accum = stride * 0.6f;       // first step soon after starting to move
            _accum += dist;
            if (_accum < stride) return false;
            _accum -= stride;
            if (_accum > stride) _accum = 0f;
            if (!WowSettings.Footsteps || data == null) return false;

            // ground under the feet
            int type;
            string surface;
            bool splash;
            if (!Surface(d, data, ground, mapId, feet, avatarRoot, _hits, 1.3f, out type, out surface, out splash)) return false;

            int footId = data.RaceFootstepId(race, gender);
            int soundId = data.FootstepSound(footId, type, splash);
            if (soundId <= 0) return false;
            float vol = Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(1.4f, 5.5f, speed));
            float pitch = 0.94f + (float)_rng.NextDouble() * 0.12f;
            d.PlayEntryWith(soundId, SoundCategory.Footsteps, feet, vol, pitch, 2);
            Steps++;
            string label = (splash ? "water over " : "") + data.TerrainName(type) + " (" + surface + ")";
            _lastSurface = label;
            if (!_firstLogged)
            {
                _firstLogged = true;
                WowLog.Info("Audio: footsteps on - race " + race + (gender == 1 ? " female" : " male") + ", footstep id " + footId + ", first step on " + label + ", sound " + soundId);
            }
            else if (type != _lastType && _typeLines < 40 && Time.unscaledTime >= _nextTypeLine)
            {
                _typeLines++;
                _nextTypeLine = Time.unscaledTime + 8f;
                WowLog.Info("Audio: footsteps now on " + label);
            }
            _lastType = type;
            return true;
        }

        /// <summary>
        /// The ground a step at feet lands on (shared with the mount's steps, iteration 7): a ray down on layer 0 from 0.5 m above
        /// the feet (0.85 m long; colliders under ignore skipped), the TerrainType of what it hit (Classify) and terrain water at the
        /// feet - deeper than silentDepthYards = no step (swimming), shallower than that but over 0.1 yd = the splash sound.
        /// False when there is nothing WoW's under the feet (in the air, the room).
        /// </summary>
        internal static bool Surface(WowAudioDriver d, WowSoundData data, WowGround ground, int mapId, Vector3 feet, Transform ignore, RaycastHit[] hits, float silentDepthYards,
            out int type, out string surface, out bool splash)
        {
            type = WowSoundData.TerrainDirt;
            surface = "";
            splash = false;
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.5f, Vector3.down, hits, 0.85f, 1 << 0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || (ignore != null && c.transform.IsChildOf(ignore))) continue;
                if (best < 0 || hits[i].distance < hits[best].distance) best = i;
            }
            if (best < 0) return false;   // in the air
            var hit = hits[best];

            float wx, wy, wz;
            WowSpace.ToWow(mapId, feet, out wx, out wy, out wz);
            if (!Classify(d, data, ground, mapId, wx, wy, hit, out type, out surface)) return false;   // not a WoW collider

            // terrain water at the feet
            var s = ground != null ? ground.Query(mapId, wx, wy) : default(GroundSample);
            if (s.Known && s.HasLiquid && s.LiquidZ > wz + 0.1f)
            {
                if (s.LiquidZ - wz > silentDepthYards) return false;   // swimming
                splash = true;
            }
            return true;
        }

        /// <summary>The TerrainType of what the ray hit. False when it is not part of the WoW world (room floor, props of the Dreamport).</summary>
        private static bool Classify(WowAudioDriver d, WowSoundData data, WowGround ground, int mapId, float wx, float wy, RaycastHit hit, out int type, out string surface)
        {
            type = WowSoundData.TerrainDirt;
            surface = "";
            Transform t = hit.collider.transform;
            string name = t.name ?? "";
            if (name.StartsWith("Tile ", StringComparison.Ordinal))
            {
                var s = ground != null ? ground.Query(mapId, wx, wy) : default(GroundSample);
                if (!s.Known) { d.RequestTile(mapId, wx, wy); surface = "terrain, tile loading"; return true; }
                type = s.TerrainType;
                surface = "terrain " + ShortName(s.Texture);
                return true;
            }
            Transform parent = t.parent;
            if (name.StartsWith("Group ", StringComparison.Ordinal) && parent != null && parent.name.StartsWith("WMO ", StringComparison.Ordinal))
            {
                int group;
                string wmo = parent.name.Substring(4);
                if (name.Length >= 9 && int.TryParse(name.Substring(6, 3), out group) && ground != null)
                {
                    string root = ground.WmoPathNear(mapId, wx, wy, wmo);
                    if (root != null)
                    {
                        var g = ground.Group(root, group);
                        if (g == null) { d.RequestWmoGroup(root, group); type = WowSoundData.TerrainStone; surface = "WMO " + wmo + " (loading)"; return true; }
                        type = g.TypeOf(hit.triangleIndex);
                        surface = "WMO " + wmo + " group " + group;
                        return true;
                    }
                }
                type = WowSoundData.TerrainTypeOfName(wmo, WowSoundData.TerrainStone);
                surface = "WMO " + wmo;
                return true;
            }
            // doodads (under "WoW Map ..." / a WMO's "Doodads (set n)")
            bool world = false;
            for (Transform p = t; p != null; p = p.parent)
            {
                string pn = p.name ?? "";
                if (pn.StartsWith("WoW Map ", StringComparison.Ordinal) || pn.StartsWith("WMO ", StringComparison.Ordinal)) { world = true; break; }
            }
            if (!world) return false;
            string low = name.ToLowerInvariant();
            type = low.Contains("tree") ? WowSoundData.TerrainWood : WowSoundData.TerrainTypeOfName(name, WowSoundData.TerrainStone);
            surface = "doodad " + name;
            return true;
        }

        private static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            int i = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string f = i >= 0 ? path.Substring(i + 1) : path;
            return f.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? f.Substring(0, f.Length - 4) : f;
        }
    }
}
