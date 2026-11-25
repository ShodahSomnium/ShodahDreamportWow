using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Lead fix 2026-10-03 (the user: "it is not displaying the correct player frame picture when selecting"): the unit portraits of
    /// the player / target / focus frames (desktop and the VR HUD - both build UiPlayerFrame / UiTargetFrame), as WoW draws them:
    /// UnitFrame_Update calls <c>SetPortraitTexture(self.portrait, self.unit)</c> (UnitFrame.lua:97) and again on UNIT_PORTRAIT_UPDATE
    /// (:109) - the unit's model rendered into the 64 x 64 portrait texture (TargetFrame.xml:227-235: $parentPortrait 64 x 64 at
    /// TOPRIGHT (-42, -12)), cut round under the frame's ring.
    ///
    /// Sits on the frame's portrait RawImage (its LateUpdate runs only while the frame is shown) and draws a round child image over it
    /// (<see cref="UiPortraitDisc"/>, before the frame art in the hierarchy, so the ring stays on top). What it shows:
    /// - a unit drawn as a WoW model (WowEntityViews.GetView): that model rendered by a small camera of its own into a
    ///   <see cref="Size"/> px RenderTexture (the pattern of WowMapCamera: enabled for one frame in LateUpdate, placed in
    ///   beginCameraRendering). For that camera only, the model's renderers are moved to <see cref="PortraitLayer"/> (31: unnamed in the
    ///   Somnium client's TagManager.asset and unused by this world) and the camera culls only that layer, so nothing else appears;
    ///   restored in endCameraRendering (URP 17: UniversalRenderPipeline.cs:1057 CameraRenderingScope = beginCameraRendering wraps
    ///   :1124 RenderSingleCamera, which culls at :850 and submits at :921 before the scope's EndCameraRendering). Black background, a
    ///   key light (the world's sun turned to light the face and the ambient set flat, fog off, all restored after - M2.shader:13 lights
    ///   with the URP main light + SampleSH + fog). Rendered when the unit changes, its model changes (built, rebuilt, renderer count) or it
    ///   dies / revives, else every <see cref="RefreshSeconds"/>; never more than once per <see cref="MinGap"/>. Framing: Models does not
    ///   read the M2 cameras block (M2Model.cs reads the attachments at header 0xF0 / 0xF8, no cameras), so the head is framed from the
    ///   helmet attachment 11 (M2Animator.Attachment doc; ArmourPieces.cs: it sits over the head pivot) or else the model's top.
    /// - a Somnium player (their WoW character is not drawn, their avatar is) and yourself: the Somnium profile picture
    ///   (UiPortraits - whitelisted players only, it enforces that), else the class circle (UiArt.ClassCircles / ClassCoords).
    /// - no model (out of view, not built): the class circle for a player, WoW's temporary portraits for creatures / NPCs (as before).
    /// </summary>
    [DefaultExecutionOrder(10005)]   // between WowGear's party fill (10000) and UiGroup's WowParty.Merge (10010): see SnapshotSomnium
    public sealed class UiUnitPortrait : MonoBehaviour
    {
        // ---- ours
        public const int Size = 128;                 // RenderTexture side (the portrait is 64 UI units; 2x for the VR HUD up close)
        // Lead fix 2026-10-04 (the user: the fitting view "does not work" - Player.log: "first render ... 11 objects on layer 31" yet a black
        // picture): the client's URP renderers filter layers - E:/Somnium Space/Main Build/Assets/URP/URP_Renderer.asset:104-109 and
        // URP_Renderer_CameraApp.asset:105-110 m_OpaqueLayerMask = m_TransparentLayerMask = 1475593719, which leaves out layers 3, 9, 11,
        // 12, 13, 18, 19, 27, 29 and 31 - so nothing on 31 is ever drawn. 30 ("IkContact", ProjectSettings/TagManager.asset) is drawn,
        // and no client script names it, no client prefab uses it and this world does not use it.
        public const int PortraitLayer = 30;         // drawn by the client's renderer, used by nothing else (see above)
        public const float FieldOfView = 30f;        // degrees, vertical
        public const float RefreshSeconds = 2f;      // re-render while shown (pose, light)
        public const float MinGap = 0.25f;           // at most 4 renders a second
        public const float CheckSeconds = 0.25f;     // how often the shown model is checked for a change
        public const float RecheckSeconds = 0.5f;    // Somnium player / picture lookups while shown
        public const int HelmAttachment = 11;        // M2 attachment id "helmet" (M2Animator.Attachment)
        public const float FrameOfHeight = 0.32f;    // the picture spans this share of the model's height ...
        public const float MinFrame = 0.35f, MaxFrame = 8f;   // ... within these metres
        public const float AimBelowHelm = 0.12f;     // the aim point under the helmet point, in frames
        public const float AimBelowTop = 0.4f;       // the aim point under the model's top (no helmet point), in frames
        public const float ViewYaw = 15f;            // degrees off the unit's facing (a slight three-quarter view)
        public const float ViewPitch = 5f;           // degrees above the aim point
        public const float KeyPitch = 30f, KeyYaw = 35f;   // the key light, relative to the camera: from the upper left
        public const float KeyIntensity = 1.1f;
        public const float Ambient = 0.45f;          // flat grey ambient during the portrait render
        public const float AboveTopSlack = 0.05f;    // metres: renderers wholly above the model's top (quest marker) are left out
        private const string ByHelm = "helmet attachment 11", ByTop = "model top";

        private enum Mode { None, Npc, Monster, ClassCircle, Picture, Live, Blank }

        private RawImage _art;
        private UiPortraitDisc _disc;
        private bool _render3d;
        private Mode _mode = Mode.None;
        private Texture _modeTex;
        private WowClass _modeCls = (WowClass)255;

        // what the frame shows (ShowUnit)
        private ulong _unit;
        private float _lookupAt = -1f;
        private bool _somnium;
        private Texture2D _pic;
        private WowClass _cls;

        // the renderer
        private ulong _want;
        private int _wantFrame = -10;
        private bool _wantDead;
        private ulong _noModelGuid;
        private ulong _liveGuid;
        private float _nextCheck, _nextRefresh, _lastRender = -10f;
        private ulong _renderGuid;
        private Transform _renderRoot;
        private int _renderCount;
        private bool _renderDead;
        private GameObject _go;
        private Camera _cam;
        private RenderTexture _rt;
        private bool _hooked, _inRender, _renderOk;
        private readonly List<Renderer> _list = new List<Renderer>();
        private readonly List<Renderer> _moved = new List<Renderer>();
        private readonly List<int> _layers = new List<int>();
        private bool _savedFog, _lit;
        private Light _sun;
        private Quaternion _savedSunRot;
        private float _savedSunIntensity;
        private Color _savedSunColor;
        private SphericalHarmonicsL2 _savedAmbient;
        private float _nextSunSearch;
        private string _framing;

        private static bool _saidLive, _saidPicture;

        /// <summary>Adds the portrait to a frame's 64 x 64 portrait image. render3d: target / focus frames (the player frame: false).</summary>
        public static UiUnitPortrait Attach(RawImage art, bool render3d)
        {
            UiUnitPortrait p = art.gameObject.AddComponent<UiUnitPortrait>();
            p._art = art;
            p._render3d = render3d;
            RectTransform r = UiKit.Rect(art.transform, "Unit portrait", 0f, 0f, 64f, 64f);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            p._disc = r.gameObject.AddComponent<UiPortraitDisc>();
            p._disc.raycastTarget = false;
            p._disc.material = art.material;   // UiKit.Over panels: the same always-on-top material as the frame
            p._disc.enabled = false;
            return p;
        }

        // ------------------------------------------------------------------ the frames' calls

        /// <summary>The player frame: your Somnium profile picture when known, else the class circle the frame draws itself.</summary>
        public void ShowOwn()
        {
            float now = Time.unscaledTime;
            if (now >= _lookupAt)
            {
                _lookupAt = now + RecheckSeconds;
                _pic = OwnPicture();
            }
            if (_pic != null) Apply(Mode.Picture, _pic, WowClass.None);
            else Apply(Mode.None, null, WowClass.None);   // the frame's own class circle
        }

        /// <summary>The target / focus frame, every refresh while it shows this unit.</summary>
        public void ShowUnit(IWowSession s, EntityState e)
        {
            float now = Time.unscaledTime;
            PlayerState me = s != null ? s.Player : null;
            bool self = me != null && e.Guid == me.Guid;
            if (e.Guid != _unit)
            {
                _unit = e.Guid;
                _lookupAt = -1f;
                _nextCheck = 0f;   // check the new unit's model at once
                _pic = null;
                _cls = WowClass.None;
            }
            if (now >= _lookupAt)
            {
                _lookupAt = now + RecheckSeconds;
                _somnium = self || e.IsSomniumPlayer || IsSomniumCharacter(e.Guid, e.Name);
                _cls = self ? me.Class : WowClass.None;
                _pic = null;
                if (self) _pic = OwnPicture();
                else if (_somnium || e.Kind == EntityKind.Player) _pic = SomniumPicture(s, e.Guid, _somnium, out _cls);
            }

            if (_somnium)
            {
                if (_pic != null) Apply(Mode.Picture, _pic, WowClass.None);
                else if (_cls != WowClass.None) Apply(Mode.ClassCircle, null, _cls);
                else Apply(Mode.Blank, null, WowClass.None);
                return;
            }

            _want = e.Guid;
            _wantFrame = Time.frameCount;
            _wantDead = e.Dead;
            if (_liveGuid == e.Guid && _rt != null) Apply(Mode.Live, _rt, WowClass.None);
            else if (_noModelGuid != e.Guid) Apply(Mode.Blank, null, WowClass.None);   // the first render is a frame away
            else if (e.Kind == EntityKind.Player) Apply(_cls != WowClass.None ? Mode.ClassCircle : Mode.Blank, null, _cls);
            else Apply(e.Kind == EntityKind.Npc ? Mode.Npc : Mode.Monster, null, WowClass.None);
        }

        private void Apply(Mode m, Texture tex, WowClass cls)
        {
            bool disc = m == Mode.Picture || m == Mode.Live || m == Mode.Blank;
            Color tint = m == Mode.Blank ? Color.black : _art.color;
            if (disc && _disc.color != tint) _disc.color = tint;   // the frame's dead / tapped grey
            if (m == _mode && ReferenceEquals(tex, _modeTex) && cls == _modeCls) return;
            _mode = m;
            _modeTex = tex;
            _modeCls = cls;
            _disc.enabled = disc;
            _art.enabled = !disc;
            if (disc)
            {
                _disc.texture = m == Mode.Blank ? null : tex;
                _disc.uvRect = m == Mode.Picture ? CentreSquare(tex) : new Rect(0f, 0f, 1f, 1f);   // RT / download: Unity's own orientation
                return;
            }
            if (m == Mode.ClassCircle)
            {
                _art.texture = UiArt.Tex(UiArt.ClassCircles);
                Vector4 c = UiArt.ClassCoords(cls);
                UiKit.Coords(_art, c.x, c.y, c.z, c.w);
            }
            else if (m == Mode.Npc || m == Mode.Monster)
            {
                _art.texture = UiArt.Tex(m == Mode.Npc ? UiArt.PortraitNpc : UiArt.PortraitMonster);
                UiKit.Coords(_art, 0f, 1f, 0f, 1f);
            }
        }

        private static Rect CentreSquare(Texture t)
        {
            if (t == null || t.width <= 0 || t.height <= 0) return new Rect(0f, 0f, 1f, 1f);
            float w = t.width, h = t.height, side = Mathf.Min(w, h);
            return new Rect((w - side) * 0.5f / w, (h - side) * 0.5f / h, side / w, side / h);
        }

        // ------------------------------------------------------------------ Somnium pictures

        private struct SomniumRec { public string Id, Nick; public WowClass Class; }
        private static readonly Dictionary<ulong, SomniumRec> SomniumChars = new Dictionary<ulong, SomniumRec>();
        private static int _snapFrame = -1;

        /// <summary>
        /// WowParty.Members holds WowGear's records of every other Somnium player (their character Guid, SomniumUserId, Class) between
        /// WowGear's LateUpdate (order 10000, WowGear.cs:394-404 FillParty) and UiGroup's (10010, UiGroup.cs:189-191 WowParty.Merge),
        /// which then keeps only the server group. Copied here (order 10005) so a Somnium player outside the group finds their picture too.
        /// </summary>
        private static void SnapshotSomnium()
        {
            if (_snapFrame == Time.frameCount) return;
            _snapFrame = Time.frameCount;
            List<WowPartyMember> m = WowParty.Members;
            if (m.Count == 0) { SomniumChars.Clear(); return; }
            bool fresh = false;
            for (int i = 0; i < m.Count && !fresh; i++) if (!m[i].Merged) fresh = true;
            if (!fresh) return;   // Gear did not refill this frame: keep the last copy
            SomniumChars.Clear();
            for (int i = 0; i < m.Count; i++)
            {
                WowPartyMember r = m[i];
                if (r.Merged || r.Guid == 0) continue;
                SomniumChars[r.Guid] = new SomniumRec { Id = r.SomniumUserId, Nick = r.SomniumName ?? r.Name, Class = r.Class };
            }
        }

        /// <summary>A Somnium player's picture (null until loaded / not whitelisted / unknown) and their class (None when unknown).</summary>
        private static Texture2D SomniumPicture(IWowSession s, ulong guid, bool somnium, out WowClass cls)
        {
            cls = WowClass.None;
            string id = null, nick = null;
            List<WowPartyMember> m = WowParty.Members;
            for (int i = 0; i < m.Count; i++)
            {
                if (m[i].Guid != guid) continue;
                WowPartyMember r = m[i];
                if ((int)r.Class > 0) cls = r.Class;
                if (r.IsSomnium && !string.IsNullOrEmpty(r.SomniumUserId)) { id = r.SomniumUserId; nick = r.SomniumName; }
                break;
            }
            SomniumRec rec;
            if (SomniumChars.TryGetValue(guid, out rec))
            {
                if (cls == WowClass.None && (int)rec.Class > 0) cls = rec.Class;
                if (id == null && !string.IsNullOrEmpty(rec.Id)) { id = rec.Id; nick = rec.Nick; }
            }
            if (cls == WowClass.None)
            {
                GroupMemberView g = UiUnits.GroupMember(s, guid);
                if (g != null && (int)g.Class > 0) cls = g.Class;
            }
            if (!somnium || id == null) return null;
            Texture2D t = UiPortraits.Get(id, nick);
            if (t != null && !_saidPicture)
            {
                _saidPicture = true;
                WowLog.Info("UI portrait: first Somnium profile picture shown in a target frame (" + t.width + "x" + t.height + ", cut round).");
            }
            return t;
        }

        private static Texture2D OwnPicture()
        {
            string id = WowAccess.UserId;
            if (string.IsNullOrEmpty(id)) return null;
            Texture2D t = UiPortraits.Get(id, WowAccess.Nick);
            if (t != null && !_saidPicture)
            {
                _saidPicture = true;
                WowLog.Info("UI portrait: your Somnium profile picture shown in the player frame (" + t.width + "x" + t.height + ", cut round).");
            }
            return t;
        }

        private static bool IsSomniumCharacter(ulong guid, string name)
        {
            try { return WowGear.IsSomniumCharacter(guid, name); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ the 3D portrait

        private void LateUpdate()
        {
            SnapshotSomnium();
            if (!_render3d) return;
            float now = Time.unscaledTime;
            bool render = false;
            if (_want != 0 && _wantFrame >= Time.frameCount - 1 && now >= _nextCheck)
            {
                _nextCheck = now + CheckSeconds;
                Transform root = WowEntityViews.GetView(_want);
                int n = root != null ? Collect(root, _want) : 0;
                if (n == 0)
                {
                    _noModelGuid = _want;
                    if (_liveGuid != _want) _liveGuid = 0;
                }
                else
                {
                    if (_noModelGuid == _want) _noModelGuid = 0;
                    bool changed = _want != _renderGuid || root != _renderRoot || n != _renderCount || _wantDead != _renderDead;
                    if ((changed || now >= _nextRefresh) && now >= _lastRender + MinGap)
                    {
                        render = true;
                        _renderGuid = _want;
                        _renderRoot = root;
                        _renderCount = n;
                        _renderDead = _wantDead;
                        _nextRefresh = now + RefreshSeconds;
                        _lastRender = now;
                        Ensure();
                    }
                }
            }
            if (_cam != null && _cam.enabled != render) _cam.enabled = render;
        }

        /// <summary>The unit's visible renderers into _list (not UI, not wholly above the model's top). No allocation.</summary>
        private int Collect(Transform root, ulong guid)
        {
            root.GetComponentsInChildren(false, _list);
            Vector3 top;
            float topY = WowEntityViews.TryGetTop(guid, out top) ? top.y + AboveTopSlack : float.MaxValue;
            int n = 0;
            for (int i = 0; i < _list.Count; i++)
            {
                Renderer r = _list[i];
                if (r == null || !r.enabled || UiKit.IsUiLayer(r.gameObject.layer) || r.bounds.min.y > topY) continue;
                _list[n++] = r;
            }
            _list.RemoveRange(n, _list.Count - n);
            return n;
        }

        private void Ensure()
        {
            if (_cam != null) return;
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
            {
                name = "WoW UI portrait",
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            _rt.Create();
            _go = new GameObject("WoW UI portrait camera");   // no parent: a scaled canvas must not scale it; destroyed in OnDestroy
            _cam = _go.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.orthographic = false;
            _cam.fieldOfView = FieldOfView;
            _cam.targetTexture = _rt;
            _cam.cullingMask = 1 << PortraitLayer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 1f);
            _cam.depth = -60f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;   // never an XR eye
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
                WowLog.Warn("UI portrait: URP camera data unavailable (" + e.Message + ") - the portrait camera renders with the defaults.");
            }
            if (!_hooked)
            {
                _hooked = true;
                RenderPipelineManager.beginCameraRendering += OnBegin;
                RenderPipelineManager.endCameraRendering += OnEnd;
            }
        }

        private void OnBegin(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam) return;
            _renderOk = false;
            _inRender = true;
            try
            {
                Transform root = WowEntityViews.GetView(_renderGuid);
                if (root == null || Collect(root, _renderGuid) == 0) return;
                Place(root);
                _moved.Clear();
                _layers.Clear();
                for (int i = 0; i < _list.Count; i++)
                {
                    GameObject g = _list[i].gameObject;
                    _moved.Add(_list[i]);
                    _layers.Add(g.layer);
                    g.layer = PortraitLayer;
                }
                KeyLight();
                _renderOk = true;
            }
            catch (Exception e)
            {
                WowLog.Warn("UI portrait: camera setup failed: " + e.Message);
            }
        }

        /// <summary>Frames the head: the helmet attachment (else the model's top), seen from in front of the unit's facing.</summary>
        private void Place(Transform root)
        {
            float height, footprint;
            if (!WowEntityViews.TryGetSize(_renderGuid, out height, out footprint) || height < 0.1f)
            {
                Bounds b = _list[0].bounds;
                for (int i = 1; i < _list.Count; i++) b.Encapsulate(_list[i].bounds);
                height = b.size.y;
                footprint = Mathf.Max(b.size.x, b.size.z);
            }
            float frame = Mathf.Clamp(height * FrameOfHeight, MinFrame, MaxFrame);
            Transform helm = WowEntityViews.GetAttachment(_renderGuid, HelmAttachment);
            Vector3 aim;
            float dist = frame * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            if (helm != null)
            {
                aim = helm.position - Vector3.up * (frame * AimBelowHelm);
                _framing = ByHelm;
            }
            else
            {
                Vector3 top;
                if (!WowEntityViews.TryGetTop(_renderGuid, out top)) top = root.position + Vector3.up * height;
                aim = top - Vector3.up * (frame * AimBelowTop);
                dist = Mathf.Max(dist, footprint * 0.5f + frame * 0.5f);   // outside the body when aiming at its middle
                _framing = ByTop;
            }
            Vector3 fwd = root.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 dir = Quaternion.AngleAxis(ViewYaw, Vector3.up) * fwd;
            float p = ViewPitch * Mathf.Deg2Rad;
            Vector3 pos = aim + (dir * Mathf.Cos(p) + Vector3.up * Mathf.Sin(p)) * dist;
            _go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(aim - pos, Vector3.up));
            _cam.nearClipPlane = Mathf.Max(0.02f, dist * 0.05f);
            _cam.farClipPlane = dist + Mathf.Max(height, footprint) * 2f + 1f;
        }

        /// <summary>The key light for this render only: the world's sun turned to light the face, flat ambient, no fog.</summary>
        private void KeyLight()
        {
            _savedFog = RenderSettings.fog;
            RenderSettings.fog = false;
            _savedAmbient = RenderSettings.ambientProbe;
            SphericalHarmonicsL2 sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(Ambient, Ambient, Ambient));
            RenderSettings.ambientProbe = sh;
            _sun = Sun();
            if (_sun != null)
            {
                Transform t = _sun.transform;
                _savedSunRot = t.rotation;
                _savedSunIntensity = _sun.intensity;
                _savedSunColor = _sun.color;
                t.rotation = _go.transform.rotation * Quaternion.Euler(KeyPitch, KeyYaw, 0f);
                _sun.intensity = KeyIntensity;
                _sun.color = Color.white;
            }
            _lit = true;
        }

        private void OnEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam || !_inRender) return;
            Restore();
            if (!_renderOk) return;
            _liveGuid = _renderGuid;
            if (!_saidLive)
            {
                _saidLive = true;
                IWowSession s = WowPlay.Session;
                EntityState e = s != null ? s.Find(_renderGuid) : null;
                WowLog.Info("UI portrait: first 3D portrait rendered (" + (e != null ? e.Name : "?") + ", " + _moved.Count + " renderer(s) on layer " + PortraitLayer +
                            ", " + Size + " px, framed on the " + _framing + ", at most " + (1f / MinGap).ToString("0") + " renders/s, every " + RefreshSeconds + " s while shown).");
            }
        }

        private void Restore()
        {
            _inRender = false;
            for (int i = 0; i < _moved.Count; i++)
            {
                Renderer r = _moved[i];
                if (r != null) r.gameObject.layer = _layers[i];
            }
            _moved.Clear();
            _layers.Clear();
            if (_lit)
            {
                _lit = false;
                RenderSettings.fog = _savedFog;
                RenderSettings.ambientProbe = _savedAmbient;
                if (_sun != null)
                {
                    _sun.transform.rotation = _savedSunRot;
                    _sun.intensity = _savedSunIntensity;
                    _sun.color = _savedSunColor;
                }
            }
        }

        /// <summary>The directional light WowLighting drives (as WowMapCamera.Sun: RenderSettings.sun, else the first directional light, searched at most every 5 s).</summary>
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

        private void OnDisable()
        {
            if (_cam != null) _cam.enabled = false;
            if (_inRender) Restore();
        }

        private void OnDestroy()
        {
            if (_hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBegin;
                RenderPipelineManager.endCameraRendering -= OnEnd;
                _hooked = false;
            }
            if (_inRender) Restore();
            if (_go != null) Destroy(_go);
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
            _go = null;
            _cam = null;
            _rt = null;
        }
    }

    /// <summary>
    /// A RawImage drawn as a disc (WoW's portraits are round: SetPortraitTexture's picture under the frame's ring) with a soft rim - any
    /// texture, readable or not, a RenderTexture too; no stencil, no shader. Replaces RawImage.OnPopulateMesh (RawImage.cs:139, a quad
    /// over uvRect); the mesh is rebuilt only when the graphic is dirty (texture, uvRect, colour, size).
    /// </summary>
    public sealed class UiPortraitDisc : RawImage
    {
        private const int Segments = 48;      // ours
        private const float Feather = 1f;     // ours: UI units of soft rim

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Texture tex = mainTexture;
            if (tex == null) return;
            Rect r = GetPixelAdjustedRect();
            Rect uv = uvRect;
            float sx = tex.width * tex.texelSize.x, sy = tex.height * tex.texelSize.y;   // as RawImage
            Vector2 c = r.center;
            float rad = Mathf.Min(r.width, r.height) * 0.5f;
            if (rad <= 0f) return;
            float inner = Mathf.Max(0f, rad - Feather);
            Color32 col = color;
            Color32 clear = col;
            clear.a = 0;
            vh.AddVert(new Vector3(c.x, c.y), col, Uv(c, r, uv, sx, sy));
            for (int i = 0; i < Segments; i++)
            {
                float a = i * (Mathf.PI * 2f / Segments);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 pi = c + d * inner, po = c + d * rad;
                vh.AddVert(new Vector3(pi.x, pi.y), col, Uv(pi, r, uv, sx, sy));     // 1 + 2i
                vh.AddVert(new Vector3(po.x, po.y), clear, Uv(po, r, uv, sx, sy));   // 2 + 2i
            }
            for (int i = 0; i < Segments; i++)
            {
                int j = (i + 1) % Segments;
                int ii = 1 + 2 * i, io = ii + 1, ji = 1 + 2 * j, jo = ji + 1;
                vh.AddTriangle(0, ji, ii);
                vh.AddTriangle(ii, ji, jo);
                vh.AddTriangle(ii, jo, io);
            }
        }

        private static Vector4 Uv(Vector2 p, Rect r, Rect uv, float sx, float sy)
        {
            float u = uv.xMin + (p.x - r.xMin) / r.width * uv.width;
            float v = uv.yMin + (p.y - r.yMin) / r.height * uv.height;
            return new Vector4(u * sx, v * sy);
        }
    }
}
