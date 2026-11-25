using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Lead request 2026-10-04 (the user: "please either show the avatar on the left in a window for fitting, or move the menu to the left
    /// so I can see my avatar when fitting the armour"): the "Fitting" panel - a live picture of your own Somnium avatar with its immersive
    /// armour, LEFT of the Settings window while its Armour page is shown (desktop: the centre panel; VR: the window lid).
    ///
    /// What is drawn: WowGear.LocalFitting gives the Animator Gear follows and the armour renderers shown now (helm, shoulders, the shell).
    /// The avatar has several visual copies (GearAvatarRoots): the followed one is the main copy, whose head bone the client shrinks in VR
    /// (MainRoot.SetHeadVisibilityState: "headBone.localScale = ... _headScale * MINIMAL_SCALE_TO_WORK_WITH_MAGICA_CLOTH_PHYSICS" for the
    /// local player) and may hide in first person. The mirror copy ("MirrorRoot", layer 8 = NON_RENDERER_BY_MAIN_CAMERA_LAYER, only mirrors
    /// and the tablet camera draw it) stands where the main one does (RootsMirroringHandler.MirrorRoot: "endRoot.Position =
    /// startRoot.RootPosition", bones' local position / rotation copied, NOT their scale - so its head is whole). So the mirror copy's
    /// renderers are drawn when GearAvatarRoots.VisualRoots finds one (active), else the followed copy's.
    ///
    /// How (UiUnitPortrait's technique): our own camera into a RenderTexture, enabled only on the frames that render; in
    /// beginCameraRendering, for that camera only, the drawn renderers are moved to layer 31 (UiUnitPortrait.PortraitLayer) and the camera
    /// culls only that layer; endCameraRendering puts them back. For that render: the world's sun turned into a key light from the
    /// camera's upper left, flat ambient, fog off - all restored. Framing comes from the followed Animator's humanoid bones (the mirror
    /// copy coincides): the whole body from the front, or the selected piece's body area (the cloak from the back), turnable by 45 degrees.
    /// All sizes, angles, rates and colours below are ours.
    /// </summary>
    public sealed class UiArmourFitView
    {
        // ---- ours: the panel (UI units, WoW frame coordinates: y down from the Settings window's top-left)
        public const float PanelW = 220f, PanelH = 472f;
        public const float Gap = 4f;                    // between this panel and the Settings window's left edge
        public const float PanelTop = 14f;              // the Settings frame's top (UiSettingsWindow: frame.Place(0, 14, W, H - 14))
        public const float PicX = 16f, PicY = 26f, PicW = 188f, PicH = 376f;   // the picture (1 : 2, as the RenderTexture)
        public const float ButtonH = 22f;
        // ---- ours: the render
        public const int TexW = 256, TexH = 512;
        public const float Aspect = (float)TexW / TexH;
        public const int DrawLayer = UiUnitPortrait.PortraitLayer;   // 31: each camera moves renderers there only inside its own render
        public const float FieldOfView = 30f;           // degrees, vertical
        public const float RendersPerSecond = 12f;      // while shown; nothing while hidden
        public const float ListSeconds = 0.5f;          // the armour renderers are re-read this often (LocalFitting allocates)
        public const float RootsSeconds = 2f;           // a missing mirror copy is looked for again this often
        public const float TurnStep = 45f;              // degrees per Turn button press
        public const float KeyPitch = 25f, KeyYaw = 30f;   // the key light, relative to the camera: from the upper left
        public const float KeyIntensity = 1.25f;
        public const float Ambient = 0.55f;             // flat grey ambient during the render
        public static readonly Color Background = new Color(0.05f, 0.05f, 0.07f, 1f);
        // ---- ours: framing (metres; "x s" = times the body scale s = the head bone's height above the feet / HeadJoint)
        public const float HeadJoint = 1.6f;            // the head bone's height of a ~1.75 m adult
        public const float HeadTop = 0.2f;              // x s: the top of the head above the head bone
        public const float AnkleDrop = 0.06f;           // the floor under the lowest foot bone (the ankle)
        public const float WholeMargin = 1.12f;         // whole body: the picture's height = floor to head top x this
        public const float FloorShow = 0.1f;            // x s: the picture's bottom is never lower than this under the floor
        public const float WholePitch = 4f;             // degrees above the aim point
        public const int Pieces = 10;
        public static readonly string[] PieceNames = { "Helm", "Left shoulder", "Right shoulder", "Chest", "Waist", "Arms", "Hands", "Legs", "Feet", "Cloak" };
        private static readonly float[] PiecePitch = { 4f, 6f, 6f, 4f, 4f, 4f, 6f, 6f, 14f, 4f };
        private static readonly string[] SideNames = { "front", "front right", "right side", "back right", "back", "back left", "left side", "front left" };   // the avatar's own sides

        // Bones used for framing, and where they are assumed when the avatar has no such bone (metres x s above the feet, to the right).
        private static readonly HumanBodyBones[] BoneIds =
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };
        private const int BHead = 0, BNeck = 1, BHips = 2, BSpine = 3, BChest = 4, BUpperChest = 5, BLUpperArm = 6, BRUpperArm = 7,
                          BLLowerArm = 8, BRLowerArm = 9, BLHand = 10, BRHand = 11, BLLowerLeg = 12, BRLowerLeg = 13, BLFoot = 14, BRFoot = 15;
        private static readonly float[] FallUp = { 1.6f, 1.5f, 0.95f, 1.1f, 1.3f, 1.35f, 1.42f, 1.42f, 1.12f, 1.12f, 0.82f, 0.82f, 0.5f, 0.5f, 0.08f, 0.08f };
        private static readonly float[] FallSide = { 0f, 0f, 0f, 0f, 0f, 0f, -0.18f, 0.18f, -0.22f, 0.22f, -0.25f, 0.25f, -0.1f, 0.1f, -0.1f, 0.1f };

        private const string TipLeft = "<size=14>Turn left</size>\nTurns your avatar 45 degrees to the left in the picture.";
        private const string TipRight = "<size=14>Turn right</size>\nTurns your avatar 45 degrees to the right in the picture.";
        private const string TipZoom = "<size=14>Whole body / Zoom to piece</size>\nShows your whole avatar, or zooms in on the armour piece selected on the right.";
        private const string LabelWhole = "Whole body", LabelZoom = "Zoom to piece";
        private const string NoAvatar = "Your avatar is not found yet.";
        private const string NoArmour = "No immersive armour is shown on your avatar (Avatar page: Immersive armour).";

        private readonly RectTransform _settingsRoot;
        private RectTransform _panel;
        private UiArmourFitHook _hook;
        private RawImage _pic;
        private TextMeshProUGUI _caption, _status;
        private UiButton _zoomButton;

        // view state
        private bool _open, _zoom, _captionDirty = true;
        private int _piece;
        private float _orbit;                 // degrees the camera is turned around the avatar from the front (+ = towards its right side)
        private float _nextList, _nextRender, _rootsAt;
        private int _statusShown = -1;

        // what is drawn
        private Animator _anim;
        private readonly Transform[] _bones = new Transform[16];
        private Transform _drawRoot;
        private bool _mirror;
        private string _drawKind = "";
        private readonly List<Renderer> _armour = new List<Renderer>(32);
        private readonly List<Renderer> _body = new List<Renderer>(64);
        private readonly List<Transform> _roots = new List<Transform>(8);

        // the renderer
        private GameObject _camGo;
        private Camera _cam;
        private RenderTexture _rt;
        private int _camFrame = -10;
        private bool _hooked, _inRender, _renderOk, _haveImage, _saidRender;
        private readonly List<GameObject> _moved = new List<GameObject>(96);
        private readonly List<int> _layers = new List<int>(96);
        private int _bodyDrawn, _armourDrawn;
        private bool _savedFog, _lit;
        private Light _sun;
        private Quaternion _savedSunRot;
        private float _savedSunIntensity;
        private Color _savedSunColor;
        private SphericalHarmonicsL2 _savedAmbient;
        private float _nextSunSearch;

        private static bool _warnedBegin, _warnedFitting, _warnedCamera;

        /// <summary>Lead 2026-10-10 (the user: "Please show my avatar in armour in the character screen in the middle, just like in WoW"): a
        /// model-only view - the picture alone at (x, y, w, h) in windowRoot (WoW's CharacterModelFrame), the whole body from the front,
        /// a transparent background (the window's art shows round the avatar); no frame, caption or buttons. Ticked with piece 0, never zooms.</summary>
        public UiArmourFitView(RectTransform windowRoot, UiContext ctx, float x, float y, float w, float h)
        {
            _settingsRoot = windowRoot;
            _modelOnly = true;
            if (windowRoot == null) return;
            _panel = UiKit.Rect(windowRoot, "Character model", x, y, w, h);
            _pic = UiKit.Pic(_panel, "Picture", null, 0f, 0f, w, h);
            _pic.uvRect = new Rect(0f, 0f, 1f, 1f);
            _pic.enabled = false;
            _status = UiKit.Text(_panel, "Status", "", 10f, UiArt.Gold, TextAlignmentOptions.Top, 4f, 8f, w - 8f, 60f);
            _caption = UiKit.Text(_panel, "Caption", "", 9f, Color.white, TextAlignmentOptions.Bottom, 0f, h - 14f, w, 12f);
            _caption.enabled = false;
            _hook = _panel.gameObject.AddComponent<UiArmourFitHook>();
            _hook.Owner = this;
            _panel.gameObject.SetActive(false);
        }

        private readonly bool _modelOnly;

        /// <summary>The model view has a picture now (the avatar was found and drawn).</summary>
        public bool HasPicture { get { return _pic != null && _pic.enabled; } }

        /// <summary>Builds the panel left of the Settings window's root (hidden). Call while the layout builds the Settings window (inside
        /// its constructor), so UiKit's build switches (Over / Pointable / Surface) are those of its surface.</summary>
        public UiArmourFitView(RectTransform settingsRoot, UiContext ctx)
        {
            _settingsRoot = settingsRoot;
            if (settingsRoot == null) return;
            _panel = UiKit.Rect(settingsRoot, "Fitting", -(PanelW + Gap), PanelTop, PanelW, PanelH);
            if (UiKit.Surface != null) UiKit.Surface.HitAreas.Add(_panel);   // lead 2026-10-04: the VR laser reaches it beside the lid
            UiKit.Blocker(_panel);   // clicks on the panel stay on it (desktop), as the windows' own blockers
            UiBackdrop frame = UiBackdrop.Make(_panel, "Frame", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            frame.Place(0f, 0f, PanelW, PanelH);
            const float hw = 180f, hh = 180f * 68f / 300f;   // the Settings header (300 x 68) at our width
            UiKit.Pic(_panel, "Header", UiArt.Tex(UiSettingsWindow.Header), (PanelW - hw) * 0.5f, -12f, hw, hh);
            TextMeshProUGUI title = UiKit.Text(_panel, "Title", "Fitting", 13f, UiArt.Gold, TextAlignmentOptions.Center, (PanelW - 140f) * 0.5f, -12f + hh * 0.31f - 8f, 140f, 16f);
            title.textWrappingMode = TextWrappingModes.NoWrap;

            UiBackdrop box = UiBackdrop.Make(_panel, "Picture box", null, UiArt.TooltipBorder, 16f, 5f, 16f, new Color(0.25f, 0.25f, 0.35f));
            box.Place(PicX - 5f, PicY - 5f, PicW + 10f, PicH + 10f);
            box.SetEdgeColor(new Color(0.6f, 0.6f, 0.6f, 1f));
            UiKit.Solid(_panel, "Picture background", Background, PicX, PicY, PicW, PicH);
            _pic = UiKit.Pic(_panel, "Picture", null, PicX, PicY, PicW, PicH);
            _pic.uvRect = new Rect(0f, 0f, 1f, 1f);   // a RenderTexture: Unity's own orientation (as UiUnitPortrait)
            _pic.enabled = false;
            _status = UiKit.Text(_panel, "Status", "", 11f, UiArt.Gold, TextAlignmentOptions.Top, PicX + 8f, PicY + 8f, PicW - 16f, 60f);
            _caption = UiKit.Text(_panel, "Caption", "", 10f, new Color(0.85f, 0.85f, 0.85f), TextAlignmentOptions.Bottom, PicX + 4f, PicY + PicH - 18f, PicW - 8f, 14f);
            _caption.textWrappingMode = TextWrappingModes.NoWrap;

            float bw = (PicW - 4f) * 0.5f, by = PicY + PicH + 6f;
            UiButton left = UiKit.PanelButton(_panel, "Turn left", PicX, by, bw, ButtonH, () => Turn(TurnStep));
            left.Tip = () => TipLeft;
            UiButton right = UiKit.PanelButton(_panel, "Turn right", PicX + bw + 4f, by, bw, ButtonH, () => Turn(-TurnStep));
            right.Tip = () => TipRight;
            _zoomButton = UiKit.PanelButton(_panel, LabelZoom, PicX, by + ButtonH + 4f, PicW, ButtonH, ToggleZoom);
            _zoomButton.Tip = () => TipZoom;

            _hook = _panel.gameObject.AddComponent<UiArmourFitHook>();
            _hook.Owner = this;
            _panel.gameObject.SetActive(false);
            WowLog.Info("UI fitting view: built (" + PanelW + " x " + PanelH + " UI units left of the Settings window" + (ctx != null && ctx.Vr ? ", on the VR window lid" : ", desktop centre panel")
                        + "; shown with the Armour page, " + TexW + " x " + TexH + " RenderTexture, " + RendersPerSecond + " renders/s while shown).");
        }

        /// <summary>Every frame from the Settings window. show = the Armour page is the one shown; piece = the selected piece (0 Helm,
        /// 1 / 2 left / right shoulder, 3 chest, 4 waist, 5 arms, 6 hands, 7 legs, 8 feet, 9 cloak).</summary>
        public void Tick(float now, bool show, int piece)
        {
            if (_panel == null) return;
            bool on = show && _settingsRoot != null && _settingsRoot.gameObject.activeInHierarchy;
            if (_panel.gameObject.activeSelf != on) _panel.gameObject.SetActive(on);
            if (!on)
            {
                _open = false;
                Cam(false);
                return;
            }
            piece = Mathf.Clamp(piece, 0, Pieces - 1);
            if (!_open)
            {
                // opened: the whole body from the front (the cloak's back), the avatar looked up at once
                _open = true;
                _zoom = false;
                _orbit = 0f;
                _piece = piece;
                _nextList = _nextRender = 0f;
                _captionDirty = true;
            }
            else if (piece != _piece && !_modelOnly)
            {
                // another piece selected: zoom in on it, from its own side
                _piece = piece;
                _zoom = true;
                _orbit = 0f;
                _nextRender = 0f;
                _captionDirty = true;
            }
            if (now >= _nextList)
            {
                _nextList = now + ListSeconds;
                ReadAvatar(now);
            }
            bool render = false;
            if (_anim != null && now >= _nextRender)
            {
                _nextRender = now + 1f / RendersPerSecond;
                Ensure();
                render = _cam != null;
            }
            Cam(render);

            bool picture = _haveImage && _anim != null;
            if (_pic.enabled != picture) _pic.enabled = picture;
            int status = _anim == null ? 1 : _armour.Count == 0 ? 2 : 0;
            if (status != _statusShown)
            {
                _statusShown = status;
                _status.text = status == 1 ? NoAvatar : status == 2 && !_modelOnly ? NoArmour : "";
            }
            if (_captionDirty)
            {
                _captionDirty = false;
                int side = Mathf.RoundToInt(Mathf.Repeat(BaseYaw() + _orbit, 360f) / 45f) % 8;
                if (!_modelOnly) _caption.text = (_zoom ? PieceNames[_piece] : LabelWhole) + ", " + SideNames[side];
                if (_zoomButton != null) _zoomButton.SetLabel(_zoom ? LabelWhole : LabelZoom);
            }
        }

        /// <summary>Removes the panel, the camera and the RenderTexture.</summary>
        public void Destroy()
        {
            if (_hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBegin;
                RenderPipelineManager.endCameraRendering -= OnEnd;
                _hooked = false;
            }
            if (_inRender) Restore();
            if (_hook != null) _hook.Owner = null;
            _hook = null;
            if (_camGo != null) UnityEngine.Object.Destroy(_camGo);
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
            }
            if (_panel != null) UnityEngine.Object.Destroy(_panel.gameObject);
            _camGo = null;
            _cam = null;
            _rt = null;
            _panel = null;
            _anim = null;
            _drawRoot = null;
            _armour.Clear();
            _body.Clear();
        }

        // ------------------------------------------------------------------ buttons

        /// <summary>The avatar seems to turn by avatarDegrees (+ = clockwise seen from above = its front swings to the picture's left,
        /// "Turn left"): the camera goes round it the other way.</summary>
        private void Turn(float avatarDegrees)
        {
            UiSound.Play(WowUiSound.ButtonClick);
            _orbit = Mathf.Repeat(_orbit - avatarDegrees, 360f);
            _nextRender = 0f;
            _captionDirty = true;
        }

        private void ToggleZoom()
        {
            UiSound.Play(WowUiSound.ButtonClick);
            _zoom = !_zoom;
            _nextRender = 0f;
            _captionDirty = true;
        }

        private float BaseYaw() { return _piece == 9 ? 180f : 0f; }   // the cloak is seen from the back

        // ------------------------------------------------------------------ the avatar

        /// <summary>The followed Animator and the armour renderers (WowGear.LocalFitting), the copy to draw (the mirror copy when found).</summary>
        private void ReadAvatar(float now)
        {
            _armour.Clear();
            Animator a = null;
            bool ok;
            try { ok = WowGear.LocalFitting(_armour, out a); }
            catch (Exception e)
            {
                ok = false;
                if (!_warnedFitting) { _warnedFitting = true; WowLog.Warn("UI fitting view: WowGear.LocalFitting failed: " + e.Message); }
            }
            if (!ok || a == null)
            {
                _anim = null;
                _drawRoot = null;
                return;
            }
            if (a != _anim)
            {
                _anim = a;
                for (int i = 0; i < BoneIds.Length; i++) _bones[i] = a.isHuman ? a.GetBoneTransform(BoneIds[i]) : null;
                _drawRoot = null;
            }
            if (_drawRoot == null || !_drawRoot.gameObject.activeInHierarchy || (!_mirror && now >= _rootsAt)) FindDrawRoot(now);
        }

        private void FindDrawRoot(float now)
        {
            _rootsAt = now + RootsSeconds;
            Transform pick = null;
            GearAvatarRoots.VisualRoots(_anim, _roots);
            for (int i = 0; i < _roots.Count && pick == null; i++)
            {
                Transform t = _roots[i];
                if (t != null && t.gameObject.activeInHierarchy && GearAvatarRoots.KindOf(t) == GearAvatarRoots.Mirror) pick = t;
            }
            _roots.Clear();
            Transform was = _drawRoot;
            _mirror = pick != null;
            _drawRoot = pick != null ? pick : _anim.transform;
            if (was != _drawRoot)
                _drawKind = _mirror ? "the mirror copy 'MirrorRoot' (whole head, also in VR)" : "the followed copy '" + (GearAvatarRoots.KindOf(_anim) ?? _anim.name) + "' (no mirror copy found)";
        }

        // ------------------------------------------------------------------ the render

        private void Cam(bool on)
        {
            if (on) _camFrame = Time.frameCount;
            if (_cam != null && _cam.enabled != on) _cam.enabled = on;
        }

        /// <summary>From the panel's LateUpdate: the camera renders only on a frame Tick asked for (Tick may stop being called).</summary>
        internal void Guard()
        {
            if (_cam != null && _cam.enabled && _camFrame != Time.frameCount) _cam.enabled = false;
        }

        /// <summary>The panel went inactive (page or window closed): no more renders.</summary>
        internal void Hidden()
        {
            _open = false;
            if (_cam != null) _cam.enabled = false;
            if (_inRender) Restore();
        }

        private void Ensure()
        {
            if (_cam != null) return;
            if (_camGo != null) return;   // a failed setup is not retried every frame
            try
            {
                _rt = new RenderTexture(TexW, TexH, 24, RenderTextureFormat.ARGB32)
                {
                    name = "WoW UI fitting view",
                    useMipMap = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                };
                _rt.Create();
                _camGo = new GameObject("WoW UI fitting view camera");   // no parent: a scaled canvas must not scale it; destroyed in Destroy
                _cam = _camGo.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.orthographic = false;
                _cam.fieldOfView = FieldOfView;
                _cam.targetTexture = _rt;
                _cam.cullingMask = 1 << DrawLayer;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = _modelOnly ? new Color(0f, 0f, 0f, 0f) : Background;   // lead 2026-10-10: the model view is see-through
                _cam.depth = _modelOnly ? -60.5f : -61f;
                _cam.allowHDR = false;
                _cam.allowMSAA = false;
                _cam.useOcclusionCulling = false;
                _cam.stereoTargetEye = StereoTargetEyeMask.None;   // never an XR eye
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
                _pic.texture = _rt;
                _pic.color = Color.white;
                if (!_hooked)
                {
                    _hooked = true;
                    RenderPipelineManager.beginCameraRendering += OnBegin;
                    RenderPipelineManager.endCameraRendering += OnEnd;
                }
            }
            catch (Exception e)
            {
                _cam = null;
                if (!_warnedCamera) { _warnedCamera = true; WowLog.Warn("UI fitting view: camera setup failed (" + e.Message + ") - the picture stays empty."); }
            }
        }

        private void OnBegin(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam) return;
            _renderOk = false;
            _inRender = true;
            try
            {
                if (_anim == null || _camFrame != Time.frameCount) return;
                _moved.Clear();
                _layers.Clear();
                Transform root = _drawRoot != null ? _drawRoot : _anim.transform;
                root.GetComponentsInChildren(false, _body);
                int n = 0;
                for (int i = 0; i < _body.Count; i++)
                {
                    Renderer r = _body[i];
                    if (r == null || !r.enabled || UiKit.IsUiLayer(r.gameObject.layer)) continue;
                    _body[n++] = r;
                }
                _body.RemoveRange(n, _body.Count - n);
                _bodyDrawn = MoveAll(_body);
                _armourDrawn = MoveAll(_armour);
                _body.Clear();
                if (_moved.Count == 0) return;
                Frame();
                KeyLight();
                _renderOk = true;
            }
            catch (Exception e)
            {
                if (!_warnedBegin) { _warnedBegin = true; WowLog.Warn("UI fitting view: camera setup failed: " + e.Message); }
            }
        }

        /// <summary>Moves the shown renderers' objects to DrawLayer, remembering their layers. Returns how many.</summary>
        private int MoveAll(List<Renderer> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Renderer r = list[i];
                if (r == null || !r.enabled) continue;
                GameObject g = r.gameObject;
                if (!g.activeInHierarchy) continue;
                _moved.Add(g);
                _layers.Add(g.layer);
                g.layer = DrawLayer;
                n++;
            }
            return n;
        }

        private Vector3 P(int b, Vector3 root, Vector3 up, Vector3 right, float s)
        {
            Transform t = _bones[b];
            return t != null ? t.position : root + up * (FallUp[b] * s) + right * (FallSide[b] * s);
        }

        /// <summary>The picture's height needed for the span between two bones to fit its width, plus pad x s each side together.</summary>
        private static float Fit(float baseHeight, Vector3 a, Vector3 b, float pad, float s)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return Mathf.Max(baseHeight * s, (d.magnitude + pad * s) / Aspect);
        }

        /// <summary>Places the camera: the whole body or the selected piece's area, seen from the front (cloak: back) turned by _orbit.</summary>
        private void Frame()
        {
            Transform at = _anim.transform;
            Vector3 up = Vector3.up;
            Vector3 fwd = at.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);
            Vector3 root = at.position;
            float ground = root.y;
            if (_bones[BLFoot] != null && _bones[BRFoot] != null)
                ground = Mathf.Min(ground, Mathf.Min(_bones[BLFoot].position.y, _bones[BRFoot].position.y) - AnkleDrop);
            float s = _bones[BHead] != null ? Mathf.Clamp((_bones[BHead].position.y - ground) / HeadJoint, 0.4f, 2.5f) : 1f;
            Vector3 groundRoot = new Vector3(root.x, ground, root.z);
            Vector3 head = P(BHead, groundRoot, up, right, s), hips = P(BHips, groundRoot, up, right, s);
            Vector3 c;
            float f, pitch;
            if (!_zoom)
            {
                float top = head.y + HeadTop * s;
                c = new Vector3(hips.x, (ground + top) * 0.5f, hips.z);
                f = (top - ground) * WholeMargin;
                pitch = WholePitch;
            }
            else
            {
                pitch = PiecePitch[_piece];
                switch (_piece)
                {
                    case 0:   // helm: the head with the neck and the shoulders' tops
                        c = head + up * (0.08f * s);
                        f = 0.75f * s;
                        break;
                    case 1:
                    case 2:   // a shoulder pad on the upper arm joint
                        c = P(_piece == 1 ? BLUpperArm : BRUpperArm, groundRoot, up, right, s) + up * (0.04f * s);
                        f = 0.8f * s;
                        break;
                    case 3:   // chest: the torso, shoulder to shoulder
                    {
                        Vector3 l = P(BLUpperArm, groundRoot, up, right, s), r = P(BRUpperArm, groundRoot, up, right, s);
                        c = _bones[BUpperChest] != null ? _bones[BUpperChest].position : P(BChest, groundRoot, up, right, s);
                        f = Fit(1.0f, l, r, 0.25f, s);
                        break;
                    }
                    case 4:   // waist: between the hips and the spine
                        c = Vector3.Lerp(hips, P(BSpine, groundRoot, up, right, s), 0.5f);
                        f = 1.0f * s;
                        break;
                    case 5:   // arms: both, around the elbows
                    {
                        Vector3 l = P(BLLowerArm, groundRoot, up, right, s), r = P(BRLowerArm, groundRoot, up, right, s);
                        c = (l + r) * 0.5f;
                        f = Fit(1.2f, l, r, 0.35f, s);
                        break;
                    }
                    case 6:   // hands: both
                    {
                        Vector3 l = P(BLHand, groundRoot, up, right, s), r = P(BRHand, groundRoot, up, right, s);
                        c = (l + r) * 0.5f - up * (0.04f * s);
                        f = Fit(0.7f, l, r, 0.35f, s);
                        break;
                    }
                    case 7:   // legs: hips to feet
                    {
                        Vector3 l = P(BLLowerLeg, groundRoot, up, right, s), r = P(BRLowerLeg, groundRoot, up, right, s);
                        c = new Vector3(hips.x, (hips.y + ground) * 0.5f, hips.z);
                        f = Mathf.Max(hips.y - ground + 0.3f * s, Fit(1.0f, l, r, 0.45f, s));
                        break;
                    }
                    case 8:   // feet: both, with the shins
                    {
                        Vector3 l = P(BLFoot, groundRoot, up, right, s), r = P(BRFoot, groundRoot, up, right, s);
                        c = (l + r) * 0.5f + up * (0.1f * s);
                        f = Fit(0.6f, l, r, 0.45f, s);
                        break;
                    }
                    default:  // cloak: neck to below the knees, from the back
                    {
                        float top = P(BNeck, groundRoot, up, right, s).y + 0.05f * s;
                        float bottom = (P(BLLowerLeg, groundRoot, up, right, s).y + P(BRLowerLeg, groundRoot, up, right, s).y) * 0.5f - 0.2f * s;
                        c = new Vector3(hips.x, (top + bottom) * 0.5f, hips.z);
                        f = Mathf.Max(top - bottom + 0.3f * s, Fit(1.0f, P(BLUpperArm, groundRoot, up, right, s), P(BRUpperArm, groundRoot, up, right, s), 0.45f, s));
                        break;
                    }
                }
            }
            float lowest = ground - FloorShow * s;
            if (c.y - f * 0.5f < lowest) c.y = lowest + f * 0.5f;

            float dist = f * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 dir = Quaternion.AngleAxis(BaseYaw() + _orbit, up) * fwd;
            float p = pitch * Mathf.Deg2Rad;
            Vector3 pos = c + (dir * Mathf.Cos(p) + up * Mathf.Sin(p)) * dist;
            _camGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c - pos, up));
            _cam.nearClipPlane = Mathf.Max(0.02f, dist * 0.05f);
            _cam.farClipPlane = dist + 3f * s + 1f;
        }

        /// <summary>The key light for this render only: the world's sun turned to light the avatar from the camera's upper left, flat ambient, no fog.</summary>
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
                t.rotation = _camGo.transform.rotation * Quaternion.Euler(KeyPitch, KeyYaw, 0f);
                _sun.intensity = KeyIntensity;
                _sun.color = Color.white;
            }
            _lit = true;
        }

        private void OnEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam || !_inRender) return;
            int drawn = _moved.Count;
            Restore();
            if (!_renderOk) return;
            _haveImage = true;
            if (_saidRender) return;
            _saidRender = true;
            WowLog.Info("UI fitting view: first render - " + _drawKind + ", " + _bodyDrawn + " avatar + " + _armourDrawn + " armour renderer(s) (" + drawn
                        + " objects) on layer " + DrawLayer + ", " + TexW + " x " + TexH + " px, " + (_zoom ? PieceNames[_piece] : LabelWhole) + ", key light "
                        + (_sun != null ? "'" + _sun.name + "'" : "none (no sun found, ambient only)") + ".");
        }

        private void Restore()
        {
            _inRender = false;
            for (int i = _moved.Count - 1; i >= 0; i--)   // backwards: an object met twice gets its first layer back
            {
                GameObject g = _moved[i];
                if (g != null) g.layer = _layers[i];
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

        /// <summary>The directional light WowLighting drives (as UiUnitPortrait.Sun: RenderSettings.sun, else the first directional light, searched at most every 5 s).</summary>
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
    }

    /// <summary>On the fitting panel: stops its camera when the panel goes inactive or Tick stops asking, and cleans up with the panel.</summary>
    internal sealed class UiArmourFitHook : MonoBehaviour
    {
        public UiArmourFitView Owner;

        private void LateUpdate() { if (Owner != null) Owner.Guard(); }
        private void OnDisable() { if (Owner != null) Owner.Hidden(); }
        private void OnDestroy() { if (Owner != null) Owner.Destroy(); }
    }
}
