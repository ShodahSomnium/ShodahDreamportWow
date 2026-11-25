using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// WoW's zone lighting for the time of day (PLAN rule 14), from Light.dbc through WowLightTable (the measured rules
    /// are written there and in reports/front.md):
    /// - one directional sun (the scene's RenderSettings.sun or first directional light, else one is created), colour =
    ///   LightIntBand 0 (diffuse; Terrain.shader also uses it as the terrain specular colour), direction from the time of day on a WoW-like arc (the client computes its sun in code,
    ///   not from a file: rising in the east, highest in the south at noon, setting in the west; at night the moon on the
    ///   same kind of arc, lit by the night diffuse colour);
    /// - RenderSettings ambient Trilight from band 1 (ambient): sky and equator = ambient, ground = ambient x 0.8, and the
    ///   ambient probe (what URP's SampleSH reads) set to the ambient colour;
    /// - linear fog: end = LightFloatBand 0 / 36 yards x 0.9144, start = end x LightFloatBand 1, colour band 7;
    /// - the sky dome (SomniumWoW/Sky, carrier Resources/WowSky.mat) with bands 2..7 and the sun/moon disc from 9/10.
    /// The render lab finds Apply(WowFiles, int, Vector3, float) by name: keep that signature.
    ///
    /// Somnium avatars (iteration 11.7): every client avatar shader that is lit (URP Lit, the client's own
    /// SomniumSpace/URPNewStandar = UniversalFragmentPBR + MixFog) reads the same URP state as our shaders: main light,
    /// ambient SH, Unity fog - all driven above. The one input they read that ours do not is the environment reflection
    /// (URP's _GlossyEnvironmentCubeMap = ReflectionProbe.defaultTexture), which stayed the scene's default sky (neutral
    /// daylight on every glossy or metallic avatar material). PushReflection fills a small cubemap with the zone's sky bands
    /// exactly as the sky dome maps them and makes it the scene's custom reflection. Restore puts every RenderSettings value
    /// and the sun we touched back when the world is left (WowGame.OnDestroy); the room gets the original reflection back.
    /// </summary>
    public static class WowLighting
    {
        public const string SkyShader = "SomniumWoW/Sky";
        private static readonly int TerrainSpecularOffId = Shader.PropertyToID("_WowTerrainSpecularOff");
        private static readonly int TerrainSpecularStrengthId = Shader.PropertyToID("_WowTerrainSpecularStrength");
        private static readonly int WaterStrengthId = Shader.PropertyToID("_WowWaterStrength");   // lead 2026-10-26: Water.shader

        private static Light _sun;
        private static bool _createdSun;
        private static GameObject _dome;
        private static Material _skyMaterial;
        private static WowLightSample _shown, _from, _to;
        private static readonly WowLightSample _fromBuf = new WowLightSample(), _mixBuf = new WowLightSample();
        private static float _blend = 1f, _blendSeconds = 1.5f;
        private static float _hours = 12f;
        private static string _lastSource = "";

        /// <summary>The light last applied (read by other modules, e.g. for water colours), or null.</summary>
        public static WowLightSample Current { get { return _shown; } }

        public static string LastSummary { get; private set; }

        /// <summary>Applies the zone light at unityPos on mapId at timeOfDayHours at once.</summary>
        public static void Apply(WowFiles files, int mapId, Vector3 unityPos, float timeOfDayHours)
        {
            float x, y, z;
            WowSpace.ToWow(mapId, unityPos, out x, out y, out z);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            WowLightSample s = null;

            try
            {
                s = files != null ? WowLightTable.Sample(files, mapId, x, y, z, timeOfDayHours) : null;
            }
            catch (Exception e)
            {
                WowLog.Error("Lighting: Light.dbc sampling failed: " + e);
            }

            if (s == null)
            {
                WowLog.Warn("Lighting: no Light.dbc light for map " + mapId + " at (" + x.ToString("0") + ", " + y.ToString("0") + ") - neutral light.");
                Neutral(true);
                return;
            }

            _hours = timeOfDayHours;
            _shown = s;
            _from = _to = null;
            _blend = 1f;
            _lastSource = s.Source;
            Push(s, timeOfDayHours);
            LastSummary = Describe(s, timeOfDayHours);
            WowLog.Info("Lighting: map " + mapId + " at (" + x.ToString("0") + ", " + y.ToString("0") + ", " + z.ToString("0") + ") yd, " + timeOfDayHours.ToString("0.0") + " h: " + LastSummary + " (" + watch.ElapsedMilliseconds + " ms)");
            SayAvatars(s);
        }

        /// <summary>
        /// Re-samples the light where the player is now (call about once a second); a different value is blended in over
        /// 1.5 s by Tick, so walking into another light volume changes the sky and fog smoothly.
        /// </summary>
        public static void Track(WowFiles files, int mapId, Vector3 unityPos, float timeOfDayHours)
        {
            if (files == null || _shown == null)
            {
                return;
            }

            float x, y, z;
            WowSpace.ToWow(mapId, unityPos, out x, out y, out z);
            WowLightSample s;

            try { s = WowLightTable.Sample(files, mapId, x, y, z, timeOfDayHours); }
            catch (Exception) { s = null; }

            if (s == null)
            {
                return;
            }

            _hours = timeOfDayHours;
            if (Same(s, _shown))
            {
                return;
            }

            WowLightSample.LerpInto(_shown, _shown, 0f, _fromBuf);
            _from = _fromBuf;
            _to = s;
            _blend = 0f;

            if (s.Source != _lastSource && (s.LightId != _shown.LightId || Math.Abs(s.Weight - _shown.Weight) > 0.25f))
            {
                WowLog.Info("Lighting: now " + s.Source + " (blending over " + _blendSeconds.ToString("0.0") + " s).");
                SayAvatars(s);
            }

            _lastSource = s.Source;
        }

        /// <summary>Advances a blend started by Track (call every frame while in the world).</summary>
        public static void Tick(float dt)
        {
            // Settings "Terrain shine" -> Terrain.shader (unset = 0 = on, so the render lab and loading screens keep it on)
            Shader.SetGlobalFloat(TerrainSpecularOffId, WowSettings.TerrainSpecular ? 0f : 1f);
            Shader.SetGlobalFloat(TerrainSpecularStrengthId, WowSettings.TerrainShine);
            Shader.SetGlobalFloat(WaterStrengthId, WowSettings.WaterVisibility);

            if (_to == null || _from == null || _blend >= 1f)
            {
                return;
            }

            _blend = Mathf.Min(1f, _blend + dt / _blendSeconds);
            WowLightSample.LerpInto(_fromBuf, _to, _blend, _mixBuf);
            _shown = _mixBuf;
            Push(_mixBuf, _hours);

            if (_blend >= 1f)
            {
                _from = _to = null;
            }
        }

        /// <summary>True when two samples differ by less than half a colour step everywhere (no blend needed).</summary>
        private static bool Same(WowLightSample a, WowLightSample b)
        {
            for (int i = 0; i < 18; i++)
            {
                if (Math.Abs(a.Colors[i].R - b.Colors[i].R) > 0.002f || Math.Abs(a.Colors[i].G - b.Colors[i].G) > 0.002f || Math.Abs(a.Colors[i].B - b.Colors[i].B) > 0.002f)
                {
                    return false;
                }
            }

            for (int i = 0; i < 6; i++)
            {
                if (Math.Abs(a.Floats[i] - b.Floats[i]) > 0.001f * Math.Max(1f, Math.Abs(a.Floats[i])))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>13.8: the lobby's skybox (WowLobby.Sky), set by WowGame when the scene has the lobby; null = the black room.</summary>
        public static Material RoomSky;
        private static bool _roomSkyOn;
        private static Material _origSkybox;

        /// <summary>The black room: no fog, no sky dome, a plain neutral light for the avatar. 13.8, the lobby: its skybox (seen through
        /// its windows) with the ambient light from it, no fog; the neutral sun stays for the avatar (the lobby's 23 lights are all baked:
        /// they light its walls through the lightmaps, never an avatar).</summary>
        public static void EnterRoom()
        {
            if (_dome != null) _dome.SetActive(false);
            _from = _to = null;
            _shown = null;
            _lastSource = "";
            Neutral(false);
            if (RoomSky != null)
            {
                if (!_roomSkyOn) { _origSkybox = RenderSettings.skybox; _roomSkyOn = true; }
                RenderSettings.skybox = RoomSky;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1f;
                DynamicGI.UpdateEnvironment();
            }
        }

        /// <summary>13.8: the client's skybox back (leaving the lobby for the world, or WowLighting.Restore).</summary>
        private static void LeaveRoomSky()
        {
            if (!_roomSkyOn) return;
            _roomSkyOn = false;
            RenderSettings.skybox = _origSkybox;
            _origSkybox = null;
        }

        private static void Neutral(bool fog)
        {
            Capture();
            RestoreReflection();
            Light sun = Sun();

            if (sun != null)
            {
                sun.color = new Color(1f, 0.96f, 0.9f);
                sun.intensity = 1f;
                sun.transform.rotation = Quaternion.Euler(50f, 160f, 0f);
            }

            SetAmbient(new Color(0.42f, 0.43f, 0.45f));
            RenderSettings.fog = fog;

            if (fog)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 300f;
                RenderSettings.fogEndDistance = 1200f;
                RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.85f);
            }
        }

        private static void Push(WowLightSample s, float hours)
        {
            Capture();
            LeaveRoomSky();   // 13.8

            // Sun / moon.
            bool night;
            Vector3 toSun = SunDirection(hours, out night);
            Light sun = Sun();

            if (sun != null)
            {
                sun.color = C(s.Colors[WowLightSample.Diffuse]);
                sun.intensity = 1f;
                sun.transform.rotation = Quaternion.LookRotation(-toSun, Vector3.up);
            }

            // Ambient (URP reads the ambient probe through SampleSH).
            SetAmbient(C(s.Colors[WowLightSample.Ambient]));

            // Fog.
            float end = Mathf.Max(20f, s.FogEndYards * WowSpace.MetresPerYard);
            float mult = Mathf.Clamp(s.FogMultiplier, -1f, 0.95f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogEndDistance = end;
            RenderSettings.fogStartDistance = end * mult;
            RenderSettings.fogColor = C(s.Colors[WowLightSample.Fog]);

            // Environment reflection of avatar shaders (ours never sample it): the zone's sky instead of the default sky.
            PushReflection(s);

            // Water colours of the zone (integrator, Terrain's request 3): LightIntBand 16/17 river, 14/15 ocean. The alphas
            // are the opacities Terrain measured on the Eastern Kingdoms clear-weather LightParams (water 0.65/0.5, ocean
            // 1/0.75); the zone's own LightParams alphas are not read yet.
            try
            {
                SomniumSpace.Worlds.SomniumWoW.World.WowWater.SetColors(
                    Ca(s.Colors[WowLightSample.RiverLight], 0.65f), Ca(s.Colors[WowLightSample.RiverDark], 0.5f),
                    Ca(s.Colors[WowLightSample.OceanLight], 1f), Ca(s.Colors[WowLightSample.OceanDark], 0.75f));
            }
            catch (Exception e)
            {
                if (!_waterWarned) { _waterWarned = true; WowLog.Warn("Lighting: WowWater.SetColors failed: " + e.Message); }
            }

            // Sky dome.
            Material m = SkyMaterial();

            if (m != null)
            {
                m.SetColor("_SkyTop", C(s.Colors[WowLightSample.SkyTop]));
                m.SetColor("_SkyMid", C(s.Colors[WowLightSample.SkyMiddle]));
                m.SetColor("_SkyBand1", C(s.Colors[WowLightSample.SkyBand1]));
                m.SetColor("_SkyBand2", C(s.Colors[WowLightSample.SkyBand2]));
                m.SetColor("_SkyHorizon", C(s.Colors[WowLightSample.SkyHorizon]));
                m.SetColor("_SkyFog", C(s.Colors[WowLightSample.Fog]));
                m.SetColor("_SunColor", C(s.Colors[WowLightSample.Sun]));
                m.SetColor("_SunHalo", C(s.Colors[WowLightSample.SunHalo]));
                m.SetVector("_SunDir", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
                m.SetFloat("_SunSize", night ? 3000f : 2000f);
                Dome().SetActive(true);
            }
        }

        private static Color C(WowRgb c)
        {
            return new Color(c.R, c.G, c.B, 1f);
        }

        private static bool _waterWarned;

        private static Color Ca(WowRgb c, float a)
        {
            return new Color(c.R, c.G, c.B, a);
        }

        private static void SetAmbient(Color ambient)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambient;
            RenderSettings.ambientEquatorColor = ambient;
            RenderSettings.ambientGroundColor = ambient * 0.8f;
            RenderSettings.ambientIntensity = 1f;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ambient.linear);
            RenderSettings.ambientProbe = sh;
        }

        // ---- Somnium avatars (iteration 11.7): the environment reflection, and the client's values kept for Restore.

        /// <summary>Ours: 16 px faces (6 x 256 texels, mip-mapped): a sky gradient needs no more; rewritten only on a change.</summary>
        private const int ReflectionSize = 16;
        private static Cubemap _reflection;
        private static Color[] _reflectionFace;
        private static readonly Color[] _reflectionBands = new Color[6];
        private static bool _reflectionShown;
        private static string _avatarsSaid = "";

        private static bool _captured, _sunSaved;
        private static UnityEngine.SceneManagement.Scene _capturedScene;
        private static bool _origFog;
        private static FogMode _origFogMode;
        private static Color _origFogColor, _origSky, _origEquator, _origGround, _origFlat;
        private static float _origFogStart, _origFogEnd, _origFogDensity, _origAmbientIntensity, _origReflectionIntensity;
        private static AmbientMode _origAmbientMode;
        private static SphericalHarmonicsL2 _origProbe;
        private static DefaultReflectionMode _origReflectionMode;
        private static Texture _origReflection;
        private static Light _origSun, _origSunLight;
        private static bool _origSunEnabled;
        private static Color _origSunColor;
        private static float _origSunIntensity, _origSunShadowStrength;
        private static Quaternion _origSunRotation;
        private static LightShadows _origSunShadows;

        /// <summary>Keeps the client's (the scene's) RenderSettings the first time WowLighting changes them.</summary>
        private static void Capture()
        {
            if (_captured)
            {
                return;
            }

            _captured = true;
            _capturedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            _origFog = RenderSettings.fog;
            _origFogMode = RenderSettings.fogMode;
            _origFogColor = RenderSettings.fogColor;
            _origFogStart = RenderSettings.fogStartDistance;
            _origFogEnd = RenderSettings.fogEndDistance;
            _origFogDensity = RenderSettings.fogDensity;
            _origAmbientMode = RenderSettings.ambientMode;
            _origSky = RenderSettings.ambientSkyColor;
            _origEquator = RenderSettings.ambientEquatorColor;
            _origGround = RenderSettings.ambientGroundColor;
            _origFlat = RenderSettings.ambientLight;
            _origAmbientIntensity = RenderSettings.ambientIntensity;
            _origProbe = RenderSettings.ambientProbe;
            _origReflectionMode = RenderSettings.defaultReflectionMode;
            _origReflection = RenderSettings.customReflectionTexture;
            _origReflectionIntensity = RenderSettings.reflectionIntensity;
            _origSun = RenderSettings.sun;
        }

        /// <summary>
        /// Puts back every RenderSettings value and the scene light WowLighting changed (called when the world is left:
        /// WowGame.OnDestroy). Safe to call when nothing was changed. The next Apply captures again. RenderSettings belong
        /// to the active scene: when another scene is active by now (the client already switched worlds), its settings are
        /// not ours to overwrite and only the light object is put back.
        /// </summary>
        public static void Restore()
        {
            if (!_captured)
            {
                return;
            }

            bool sameScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene() == _capturedScene;

            if (sameScene)
            {
                LeaveRoomSky();   // 13.8
                RestoreSettings();
            }

            _reflectionShown = false;

            if (_sunSaved && _origSunLight != null)
            {
                _origSunLight.enabled = _origSunEnabled;
                _origSunLight.color = _origSunColor;
                _origSunLight.intensity = _origSunIntensity;
                _origSunLight.transform.rotation = _origSunRotation;
                _origSunLight.shadows = _origSunShadows;
                _origSunLight.shadowStrength = _origSunShadowStrength;
            }

            if (_reflection != null)
            {
                UnityEngine.Object.Destroy(_reflection);
                _reflection = null;
            }

            _captured = _sunSaved = _createdSun = _avatarShadersSaid = false;
            _origSunLight = null;
            _origSun = null;
            _origReflection = null;
            _sun = null;
            _shown = _from = _to = null;
            _lastSource = _avatarsSaid = "";
            WowLog.Info("Lighting: the client's light restored (" + (sameScene ? "sun, ambient, fog, reflection " + _origReflectionMode : "the light only: another scene is active now") + ").");
        }

        private static void RestoreSettings()
        {
            RestoreReflection();
            RenderSettings.fog = _origFog;
            RenderSettings.fogMode = _origFogMode;
            RenderSettings.fogColor = _origFogColor;
            RenderSettings.fogStartDistance = _origFogStart;
            RenderSettings.fogEndDistance = _origFogEnd;
            RenderSettings.fogDensity = _origFogDensity;
            RenderSettings.ambientMode = _origAmbientMode;
            RenderSettings.ambientSkyColor = _origSky;
            RenderSettings.ambientEquatorColor = _origEquator;
            RenderSettings.ambientGroundColor = _origGround;
            RenderSettings.ambientLight = _origFlat;
            RenderSettings.ambientIntensity = _origAmbientIntensity;
            RenderSettings.ambientProbe = _origProbe;
            RenderSettings.reflectionIntensity = _origReflectionIntensity;
            RenderSettings.sun = _origSun;
        }

        /// <summary>The scene's own reflection again (the room, the neutral fallback); the zone cubemap is kept for reuse.</summary>
        private static void RestoreReflection()
        {
            if (!_reflectionShown)
            {
                return;
            }

            _reflectionShown = false;
            RenderSettings.defaultReflectionMode = _origReflectionMode;
            RenderSettings.customReflectionTexture = _origReflection;
        }

        /// <summary>
        /// The environment reflection client avatar shaders read (URP sets _GlossyEnvironmentCubeMap from
        /// ReflectionProbe.defaultTexture every frame; the scene's default is Unity's daylight sky): a cubemap of the zone's
        /// sky, mapped by elevation exactly as SkyDome.shader maps LightIntBand 2..7 (90/30/15/5/0 degrees, fog down to -30
        /// and below), lerped and stored in linear like the dome (the runtime RGBA32 cubemap is sampled without an sRGB
        /// decode - measured in the render lab: values written as gamma came back too bright). No sun disc (ours: a 16 px
        /// face would only blur it). Rewritten only when a band moved by more than half a colour step: once per zone light,
        /// and every frame of a 1.5 s blend (6 x 256 texels, no allocation).
        /// </summary>
        private static void PushReflection(WowLightSample s)
        {
            bool changed = _reflection == null;

            for (int i = 0; i < 6; i++)
            {
                Color c = C(s.Colors[WowLightSample.SkyTop + i]).linear;   // 2 top, 3 middle, 4 band 1, 5 band 2, 6 horizon, 7 fog

                if (Mathf.Abs(c.r - _reflectionBands[i].r) > 0.002f || Mathf.Abs(c.g - _reflectionBands[i].g) > 0.002f || Mathf.Abs(c.b - _reflectionBands[i].b) > 0.002f)
                {
                    changed = true;
                }

                _reflectionBands[i] = c;
            }

            if (changed)
            {
                if (_reflection == null)
                {
                    _reflection = new Cubemap(ReflectionSize, TextureFormat.RGBA32, true);
                    _reflection.name = "WoW zone reflection (runtime)";
                    _reflection.wrapMode = TextureWrapMode.Clamp;
                    _reflection.filterMode = FilterMode.Trilinear;
                    _reflectionFace = new Color[ReflectionSize * ReflectionSize];
                }

                for (int f = 0; f < 6; f++)
                {
                    for (int y = 0; y < ReflectionSize; y++)
                    {
                        for (int x = 0; x < ReflectionSize; x++)
                        {
                            float u = (x + 0.5f) / ReflectionSize * 2f - 1f, v = (y + 0.5f) / ReflectionSize * 2f - 1f;
                            _reflectionFace[y * ReflectionSize + x] = SkyAt(FaceY((CubemapFace)f, u, v));   // linear (see above)
                        }
                    }

                    _reflection.SetPixels(_reflectionFace, (CubemapFace)f);
                }

                _reflection.Apply(true);
            }

            if (!_reflectionShown || RenderSettings.customReflectionTexture != _reflection)
            {
                _reflectionShown = true;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = _reflection;
            }
        }

        /// <summary>
        /// The y (up) component of the unit direction through texel (u, v) of a cube face. Cubemap.SetPixels rows run top to
        /// bottom on the side faces (checked in the render lab: P/renderlab/out/avlight, the mirror ball shows the sky top up).
        /// </summary>
        private static float FaceY(CubemapFace face, float u, float v)
        {
            float y;

            switch (face)
            {
                case CubemapFace.PositiveY: return 1f / Mathf.Sqrt(1f + u * u + v * v);
                case CubemapFace.NegativeY: return -1f / Mathf.Sqrt(1f + u * u + v * v);
                default: y = -v; break;   // side faces: row 0 = top
            }

            return y / Mathf.Sqrt(1f + u * u + y * y);
        }

        /// <summary>SkyDome.shader's elevation bands on the linear band colours (index 0 = band 2 sky top .. 5 = band 7 fog).</summary>
        private static Color SkyAt(float dirY)
        {
            float e = Mathf.Asin(Mathf.Clamp(dirY, -1f, 1f)) * Mathf.Rad2Deg;
            Color[] b = _reflectionBands;
            if (e >= 30f) return Color.Lerp(b[1], b[0], (e - 30f) / 60f);
            if (e >= 15f) return Color.Lerp(b[2], b[1], (e - 15f) / 15f);
            if (e >= 5f) return Color.Lerp(b[3], b[2], (e - 5f) / 10f);
            if (e >= 0f) return Color.Lerp(b[4], b[3], e / 5f);
            return Color.Lerp(b[4], b[5], -e / 30f);
        }

        /// <summary>One line per zone light: what Somnium avatars are lit by now (all of it shared with our own shaders).</summary>
        private static void SayAvatars(WowLightSample s)
        {
            if (s.Source == _avatarsSaid)
            {
                return;
            }

            _avatarsSaid = s.Source;
            float end = Mathf.Max(20f, s.FogEndYards * WowSpace.MetresPerYard);   // as Push
            WowLog.Info("Lighting: avatars lit by " + s.Source + ": URP main light #" + s.Colors[WowLightSample.Diffuse].Hex + " x1, ambient #" + s.Colors[WowLightSample.Ambient].Hex +
                        " (Trilight + SH probe), Unity linear fog #" + s.Colors[WowLightSample.Fog].Hex + " " + (end * Mathf.Clamp(s.FogMultiplier, -1f, 0.95f)).ToString("0") + ".." + end.ToString("0") +
                        " m (the same globals as our shaders), reflection = the zone sky #" + s.Colors[WowLightSample.SkyTop].Hex + "/#" + s.Colors[WowLightSample.SkyHorizon].Hex + "/#" +
                        s.Colors[WowLightSample.Fog].Hex + " (" + ReflectionSize + " px cubemap" + (_reflection != null && RenderSettings.customReflectionTexture == _reflection ? "" : " NOT set") + ").");

            if (!_avatarShadersSaid)
            {
                // Once per visit: which shaders the local avatar draws with (a toon shader's own light floor, or a bundle
                // built without fog variants, is something no global can change - this line tells which case a test is).
                Transform root = WowPlayer.Root;
                if (root == null) return;
                _avatarShadersSaid = true;
                var names = new System.Collections.Generic.List<string>();
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        if (m != null && m.shader != null && !m.shader.name.StartsWith("SomniumWoW/", StringComparison.Ordinal) && !names.Contains(m.shader.name) && names.Count < 8)
                            names.Add(m.shader.name);
                WowLog.Info("Lighting: shaders under the local player root (your avatar, hands, tablet): " + (names.Count > 0 ? string.Join(", ", names.ToArray()) : "none found") + ".");
            }
        }

        private static bool _avatarShadersSaid;

        /// <summary>
        /// Direction towards the sun (day, 5:30 to 20:30) or the moon (night). Azimuth clockwise from north (+z): 90 east,
        /// 180 south, 270 west; Unity +x is east (WowSpace: x = -WoW Y, WoW Y points west). Not from the game files.
        /// </summary>
        public static Vector3 SunDirection(float hours, out bool night)
        {
            float h = ((hours % 24f) + 24f) % 24f;
            float frac, elevationMax;
            night = h < 5.5f || h > 20.5f;

            if (!night)
            {
                frac = (h - 5.5f) / 15f;
                elevationMax = 55f;
            }
            else
            {
                float n = h > 20.5f ? h - 20.5f : h + 3.5f;
                frac = n / 9f;
                elevationMax = 35f;
            }

            float az = (90f + 180f * frac) * Mathf.Deg2Rad;
            float el = (10f + elevationMax * Mathf.Sin(frac * Mathf.PI)) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        private static Light Sun()
        {
            if (_sun != null)
            {
                return _sun;
            }

            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
            {
                _sun = RenderSettings.sun;
            }
            else
            {
                foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional && l.enabled && l.gameObject.activeInHierarchy)
                    {
                        _sun = l;
                        break;
                    }
                }
            }

            if (_sun != null && !_sunSaved)
            {
                // The scene's own light: kept as it was so Restore can put it back.
                _sunSaved = true;
                _origSunLight = _sun;
                _origSunEnabled = _sun.enabled;
                _origSunColor = _sun.color;
                _origSunIntensity = _sun.intensity;
                _origSunRotation = _sun.transform.rotation;
                _origSunShadows = _sun.shadows;
                _origSunShadowStrength = _sun.shadowStrength;
            }

            if (_sun == null)
            {
                var go = new GameObject("WoW Sun");
                if (WowGame.Current != null) go.transform.SetParent(WowGame.Current.transform, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _createdSun = true;
            }

            _sun.enabled = true;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.7f;
            RenderSettings.sun = _sun;
            WowLog.Info("Lighting: sun = " + (_createdSun ? "created 'WoW Sun'" : "the scene's '" + _sun.name + "'") + ".");
            return _sun;
        }

        private static Material SkyMaterial()
        {
            if (_skyMaterial != null)
            {
                return _skyMaterial;
            }

            Material carrier = WowShaders.Carrier(SkyShader);
            Shader shader = carrier != null ? carrier.shader : WowShaders.Find(SkyShader);

            if (shader == null)
            {
                WowLog.Warn("Lighting: shader " + SkyShader + " missing (is Resources/WowSky.mat in WowGame._shaderCarriers?) - no sky dome.");
                return null;
            }

            _skyMaterial = carrier != null ? new Material(carrier) : new Material(shader);
            _skyMaterial.name = "WoW Sky (runtime)";
            return _skyMaterial;
        }

        private static GameObject Dome()
        {
            if (_dome != null)
            {
                return _dome;
            }

            _dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dome.name = "WoW Sky Dome";
            UnityEngine.Object.Destroy(_dome.GetComponent<Collider>());
            // Radius 4 km: the shader puts it at the far plane anyway; the size only keeps eye parallax negligible.
            _dome.transform.localScale = Vector3.one * 8000f;
            var r = _dome.GetComponent<MeshRenderer>();
            r.sharedMaterial = _skyMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _dome.AddComponent<WowSkyDome>();
            if (WowGame.Current != null) _dome.transform.SetParent(WowGame.Current.transform, true);
            WowLog.Info("Lighting: sky dome created (" + _skyMaterial.shader.name + ", queue " + _skyMaterial.renderQueue + ").");
            return _dome;
        }

        public static string Describe(WowLightSample s, float hours)
        {
            bool night;
            Vector3 d = SunDirection(hours, out night);
            float el = Mathf.Asin(d.y) * Mathf.Rad2Deg, az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return s.Source + "; diffuse " + s.Colors[0].Hex + ", ambient " + s.Colors[1].Hex + ", sky " + s.Colors[2].Hex + "/" + s.Colors[3].Hex + "/" + s.Colors[4].Hex + "/" +
                   s.Colors[5].Hex + "/" + s.Colors[6].Hex + ", fog " + s.Colors[7].Hex + " " + (s.FogEndYards * WowSpace.MetresPerYard * Mathf.Clamp(s.FogMultiplier, -1f, 0.95f)).ToString("0") + ".." +
                   (s.FogEndYards * WowSpace.MetresPerYard).ToString("0") + " m, " + (night ? "moon" : "sun") + " " + s.Colors[9].Hex + " at elevation " + el.ToString("0") + " azimuth " + ((az + 360f) % 360f).ToString("0");
        }
    }

    /// <summary>Keeps the sky dome centred on whichever camera is about to render (lab, desktop, both eyes' rig).</summary>
    public sealed class WowSkyDome : MonoBehaviour
    {
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += Snap;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Snap;
        }

        private void Snap(ScriptableRenderContext context, Camera camera)
        {
            if (camera != null && (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView))
            {
                transform.position = camera.transform.position;
            }
        }

        private void LateUpdate()
        {
            Camera head = WowPlayer.Head();
            if (head != null) transform.position = head.transform.position;
        }
    }
}
