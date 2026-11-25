using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SomniumSpace.Worlds.SomniumWoW.Game.MapApp
{
    /// <summary>
    /// The live map: a top-down orthographic camera high above the local player rendering the WoW world into a RenderTexture,
    /// ONLY while a map is visible and at most <see cref="RendersPerSecond"/> times a second. Between renders the camera is
    /// disabled; a render is one frame with the camera enabled (switched in LateUpdate, as the Euro Truck Sim mirrors do -
    /// a camera switched on inside the render callbacks would only draw next frame), placed in beginCameraRendering.
    ///
    /// For its own render only (begin/endCameraRendering, restored right after): fog off (WoW's linear fog would wash the
    /// ground out from 500 m up), and the sun and ambient lifted so a night or dusk map stays readable ("exposure").
    /// Culling: Default, TransparentFX, Water, Terrain, Environment (client TagManager layers 0 1 4 14 23) - not UI (5),
    /// the tablet (6 XRInteractable, 9 Overlay, 10 TabletUI) or avatars (24 Player). Ground clutter skips it
    /// (WowWorldDetails asks <see cref="IsMapCamera"/>). Cheap: no shadows, no depth/colour copies, no post (as EtsMirrorsUrp).
    ///
    /// Height and clip: the camera stands <see cref="Height"/> m above the player's feet; the near plane cuts everything more
    /// than <see cref="OpenAbove"/> m above the feet outdoors (12 m up to 250 m wide views: tree canopy and roofs around the player are
    /// cut, the ground shows; higher ground is cut too and shows as the dark background; 30 m at 400 m, 60 m at 600 m), and just under the roof when a collider is within 30 m over the
    /// head (inside a building / cave: the roof is cut away, the floor shows). Far plane: <see cref="Below"/> m under the feet.
    /// Follows the floating origin by itself: it is placed from the avatar's current position at every render.
    /// </summary>
    public sealed class WowMapCamera
    {
        public const int Size = 1024;
        /// <summary>The picture covers this much more than the visible square, so it can slide/turn between renders without gaps.</summary>
        public const float Margin = 1.25f;
        public const float RendersPerSecond = 12f;
        public const float Height = 500f, Below = 250f;

        /// <summary>Lab only: a fixed clip height over the feet (metres), &lt; 0 = the zoom rule.</summary>
        public static float LabClipAbove = -1f;

        /// <summary>How far above the feet the picture is cut outdoors, for a view half this wide (metres).</summary>
        public static float OpenAbove(float half)
        {
            if (LabClipAbove >= 0f) return LabClipAbove;
            float width = half * 2f;
            // Lead, iteration 5: 12/30/60 m cut every hill above that into black holes (lab m2_map_live); outdoors keep cliffs and
            // show canopy from above like WoW's own minimap; indoors RoofClip still cuts at the roof.
            return width <= 250.5f ? 45f : width <= 400.5f ? 70f : 110f;
        }
        public const int CullMask = (1 << 0) | (1 << 1) | (1 << 4) | (1 << 14) | (1 << 23);
        private const int RoofMask = (1 << 0) | (1 << 14);

        private static Camera _active;

        /// <summary>For other systems' per-camera work (clutter, billboards): true for the map camera.</summary>
        public static bool IsMapCamera(Camera cam)
        {
            return cam != null && _active != null && cam == _active;
        }

        private readonly Transform _parent;
        private GameObject _go;
        private Camera _cam;
        private RenderTexture _rt;
        private float _next;
        private bool _hooked;
        private bool _inRender;

        // What the picture shows (set when a render is scheduled: the render runs later in the same frame, before any camera
        // that shows it, so the UI placed this frame already matches it).
        public bool HasImage { get; private set; }
        public Vector3 ShownCentre { get; private set; }
        public float ShownYaw { get; private set; }
        public float ShownHalf { get; private set; }

        private Vector3 _centre;
        private float _yaw, _half, _feetY, _clipY;
        private bool _indoor;

        // Restored after our render.
        private bool _savedFog;
        private Light _sun;
        private float _savedSun;
        private SphericalHarmonicsL2 _savedAmbient;
        private bool _lifted;
        private float _nextSunSearch;
        private Renderer _dome;
        private bool _domeHidden;
        private float _nextDomeSearch;

        // Stats for the log.
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();
        private int _renders;
        private double _totalMs, _maxMs;
        private double _steadyMs;
        private int _steadyN;
        private float _lastLift;
        private float _nextLog;
        private int _logs;

        public WowMapCamera(Transform parent)
        {
            _parent = parent;
        }

        public RenderTexture Texture { get { return _rt; } }
        public int Renders { get { return _renders; } }
        public string StatsLine
        {
            get
            {
                return "live map: " + _renders + " renders at " + Size + " px, CPU avg " + (_renders > 0 ? (_totalMs / _renders).ToString("0.00") : "-") + " ms (after the first 10: " + (_steadyN > 0 ? (_steadyMs / _steadyN).ToString("0.00") : "-") + " ms), max " +
                       _maxMs.ToString("0.00") + " ms, light lift x" + _lastLift.ToString("0.00") + (_indoor ? ", indoors (roof cut at +" + (_clipY - _feetY).ToString("0.0") + " m)" : "");
            }
        }

        public void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;
            if (on)
            {
                RenderPipelineManager.beginCameraRendering += OnBegin;
                RenderPipelineManager.endCameraRendering += OnEnd;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering -= OnBegin;
                RenderPipelineManager.endCameraRendering -= OnEnd;
                if (_inRender) Restore();
            }
        }

        /// <summary>
        /// LateUpdate: visible = some map shows the live picture. centre = the view's centre (Unity, feet height), yaw = the view's
        /// up direction (Unity yaw, degrees), half = half the visible width in metres.
        /// </summary>
        public void Tick(bool visible, Vector3 centre, float yaw, float half, float feetY, float now)
        {
            if (!visible)
            {
                if (_cam != null && _cam.enabled) _cam.enabled = false;
                return;
            }

            Ensure();
            bool render = now >= _next;
            if (render)
            {
                _next = now + 1f / RendersPerSecond;
                _centre = centre;
                _yaw = yaw;
                _half = half;
                _feetY = feetY;
                RoofClip(centre, feetY);
                ShownCentre = centre;
                ShownYaw = yaw;
                ShownHalf = half;
                HasImage = true;
            }

            if (_cam.enabled != render) _cam.enabled = render;
        }

        private void RoofClip(Vector3 centre, float feetY)
        {
            RaycastHit hit;
            Vector3 head = new Vector3(centre.x, feetY + 1.8f, centre.z);
            _indoor = Physics.Raycast(head, Vector3.up, out hit, 30f, RoofMask, QueryTriggerInteraction.Ignore);
            _clipY = _indoor ? Mathf.Max(feetY + 2.2f, hit.point.y - 0.2f) : feetY + OpenAbove(_half);
        }

        private void Ensure()
        {
            if (_cam != null) return;
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
            {
                name = "WoW Map live",
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            _rt.Create();
            _go = new GameObject("WoW Map camera");
            if (_parent != null) _go.transform.SetParent(_parent, false);   // destroyed with the world (never DontDestroyOnLoad)
            _cam = _go.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.orthographic = true;
            _cam.targetTexture = _rt;
            _cam.cullingMask = CullMask;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.10f, 0.10f, 0.11f, 1f);   // cut-away high ground and the void read as dark slate
            _cam.depth = -50f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            try
            {
                UniversalAdditionalCameraData data = _cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderShadows = false;
                    data.requiresDepthOption = CameraOverrideOption.Off;
                    data.requiresColorOption = CameraOverrideOption.Off;
                    data.renderPostProcessing = false;
                    data.antialiasing = AntialiasingMode.None;
                    data.volumeLayerMask = 0;
                }
            }
            catch (Exception e)
            {
                WowLog.Warn("Map: URP camera data unavailable (" + e.Message + ") - the map camera renders with the defaults.");
            }
            _active = _cam;
            WowLog.Info("Map: live camera created (" + Size + " px RenderTexture, " + RendersPerSecond + " renders/s while visible, culling layers 0 1 4 14 23, " +
                        Height + " m up, clip 12/30/60 m over the feet outdoors by zoom, sky dome hidden)."); 
        }

        private void OnBegin(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam) return;
            try
            {
                _watch.Reset();
                _watch.Start();
                float camY = _feetY + Height;
                Vector3 up = new Vector3(Mathf.Sin(_yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_yaw * Mathf.Deg2Rad));
                _go.transform.SetPositionAndRotation(new Vector3(_centre.x, camY, _centre.z), Quaternion.LookRotation(Vector3.down, up));
                _cam.orthographicSize = Mathf.Max(5f, _half * Margin);
                _cam.nearClipPlane = Mathf.Max(1f, camY - _clipY);
                _cam.farClipPlane = camY - (_feetY - Below);

                _savedFog = RenderSettings.fog;
                RenderSettings.fog = false;
                Light sun = Sun();
                float lift = 1f;
                if (sun != null)
                {
                    Color c = sun.color;
                    float lum = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * sun.intensity;
                    lift = Mathf.Clamp(0.55f / Mathf.Max(0.05f, lum), 1f, 2.2f);   // noon: none (lab: sun luminance 0.59); dusk and night: up to x2.2
                    _savedSun = sun.intensity;
                    sun.intensity = _savedSun * lift;
                }
                _sun = sun;
                _savedAmbient = RenderSettings.ambientProbe;
                RenderSettings.ambientProbe = _savedAmbient * Mathf.Clamp(lift, 1f, 1.6f);
                _dome = Dome();
                if (_dome != null && _dome.enabled) { _dome.enabled = false; _domeHidden = true; }
                _lastLift = lift;
                _lifted = true;
                _inRender = true;
            }
            catch (Exception e)
            {
                WowLog.Warn("Map: camera setup failed: " + e.Message);
            }
        }

        private void OnEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam || !_inRender) return;
            Restore();
            _watch.Stop();
            double ms = _watch.Elapsed.TotalMilliseconds;
            _renders++;
            _totalMs += ms;
            if (_renders > 10) { _steadyMs += ms; _steadyN++; }
            if (ms > _maxMs) _maxMs = ms;
            float now = Time.unscaledTime;
            if (_renders == 1 || (now >= _nextLog && _logs < 6))
            {
                _nextLog = now + 60f;
                _logs++;
                WowLog.Info("Map: " + StatsLine + ".");
            }
        }

        private void Restore()
        {
            _inRender = false;
            RenderSettings.fog = _savedFog;
            if (_domeHidden)
            {
                if (_dome != null) _dome.enabled = true;
                _domeHidden = false;
            }
            if (_lifted)
            {
                if (_sun != null) _sun.intensity = _savedSun;
                RenderSettings.ambientProbe = _savedAmbient;
                _lifted = false;
            }
        }

        /// <summary>
        /// WowLighting's sky dome ("WoW Sky Dome", a sphere on layer 0 that snaps to every camera): hidden for the map's render, so
        /// cut-away ground shows the dark background instead of sky blue that reads as water (searched at most every 5 s).
        /// </summary>
        private Renderer Dome()
        {
            if (_dome != null) return _dome;
            if (Time.unscaledTime < _nextDomeSearch) return null;
            _nextDomeSearch = Time.unscaledTime + 5f;
            GameObject go = GameObject.Find("WoW Sky Dome");
            _dome = go != null ? go.GetComponent<Renderer>() : null;
            return _dome;
        }

        /// <summary>The directional light WowLighting drives (RenderSettings.sun, else the first directional light; searched at most every 5 s).</summary>
        private Light Sun()
        {
            if (_sun != null && _sun.isActiveAndEnabled) return _sun;
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional) return RenderSettings.sun;
            if (Time.unscaledTime < _nextSunSearch) return null;
            _nextSunSearch = Time.unscaledTime + 5f;
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l != null && l.type == LightType.Directional && l.isActiveAndEnabled) return l;
            }
            return null;
        }

        /// <summary>Render lab only (batchmode draws no camera by itself): renders the scheduled picture now.</summary>
        public void RenderNow()
        {
            if (_cam != null && HasImage) _cam.Render();
        }

        public void Dispose()
        {
            Hook(false);
            if (_active == _cam) _active = null;
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
            }
            _go = null;
            _cam = null;
            _rt = null;
            HasImage = false;
        }
    }
}
