using System;
using System.Collections.Generic;
using System.IO;
using SomniumSpace.Worlds.SomniumWoW.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// World-space UI in WoW's own look, built at runtime from the player's install: the glue button art, the dialog
    /// backdrop, FRIZQT__.TTF. Layouts are in canvas units with (0, 0) at the top-left and y growing downwards, like
    /// WoW's own frame coordinates. BLPs are uploaded as stored (row 0 = v 0, PLAN rule 15), so every RawImage flips v:
    /// WoW texcoords (left, right, top, bottom) become uvRect (left, bottom, right - left, top - bottom).
    /// </summary>
    public static class WowUi
    {
        // Paths verified with the harness probe FrontPaths (reports/front.md).
        public const string LogoPath = @"Interface\Glues\Common\Glues-WoW-WotLKLogo.blp";
        public const string ButtonUp = @"Interface\Glues\Common\Glue-Panel-Button-Up.blp";
        public const string ButtonDown = @"Interface\Glues\Common\Glue-Panel-Button-Down.blp";
        public const string ButtonHighlight = @"Interface\Glues\Common\Glue-Panel-Button-Highlight.blp";
        public const string ButtonUpBlue = @"Interface\Glues\Common\Glue-Panel-Button-Up-Blue.blp";
        public const string ButtonDownBlue = @"Interface\Glues\Common\Glue-Panel-Button-Down-Blue.blp";
        public const string DialogBackground = @"Interface\DialogFrame\UI-DialogBox-Background-Dark.blp";
        public const string DialogBorder = @"Interface\DialogFrame\UI-DialogBox-Border.blp";
        public const string DialogHeader = @"Interface\DialogFrame\UI-DialogBox-Header.blp";
        public const string BarBorder = @"Interface\Glues\LoadingBar\Loading-BarBorder.blp";
        public const string BarFill = @"Interface\Glues\LoadingBar\Loading-BarFill.blp";
        public const string BarSpark = @"Interface\Glues\LoadingBar\UI-LoadingBar-Spark.blp";
        public const string FontPath = @"Fonts\FRIZQT__.TTF";

        /// <summary>Every BLP the panels use, prepared on the boot thread.</summary>
        public static readonly string[] PanelTextures = { LogoPath, ButtonUp, ButtonDown, ButtonUpBlue, ButtonDownBlue, DialogBorder, DialogHeader, BarBorder, BarFill, BarSpark, WowClassInfo.IconAtlas, WowClassInfo.IconShadow, WowRaceInfo.RaceAtlas, WowRaceInfo.FactionAtlas, WowCheckBox.Up, WowCheckBox.Down, WowCheckBox.Check, WowSlider.Background, WowSlider.Thumb };

        /// <summary>GlueButtons.xml:24-35: the up/down art uses (0, 0.578125, 0, 0.75), the highlight (0, 0.625, 0, 0.6875).</summary>
        public static readonly Rect ButtonCoords = new Rect(0f, 0f, 0.578125f, 0.75f);
        public static readonly Rect HighlightCoords = new Rect(0f, 0f, 0.625f, 0.6875f);

        /// <summary>GlueFontStyles.xml:21 GlueFontNormal (1.0, 0.78, 0); GlueFontHighlight white.</summary>
        public static readonly Color GlueGold = new Color(1f, 0.78f, 0f);
        public static readonly Color Grey = new Color(0.62f, 0.62f, 0.62f);

        public const int UiLayer = 5;

        private static WowFiles _files;
        private static TMP_FontAsset _font;
        private static string _fontSource = "not loaded";
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static Texture2D _highlight;
        private static Texture2D _backgroundTile;

        public static bool HasGameArt { get { return _files != null; } }

        /// <summary>WoW's font, else TextMeshPro's default.</summary>
        public static TMP_FontAsset Font { get { return _font != null ? _font : TMP_Settings.defaultFontAsset; } }

        public static string FontSource { get { return _fontSource; } }

        /// <summary>Main thread: the textures prepared on the boot thread become Texture2Ds; the font is built from its bytes.</summary>
        public static void Init(WowFiles files, List<WowPreparedTexture> prepared, byte[] fontBytes)
        {
            _files = files;

            if (prepared != null)
            {
                foreach (WowPreparedTexture p in prepared)
                {
                    Texture2D t = WowTextures.Create(p);
                    if (t != null) Textures[p.Path] = t;
                    if (!string.IsNullOrEmpty(p.Error)) WowLog.Warn("UI texture " + p.Path + ": " + p.Error);
                }
            }

            LoadFont(fontBytes);
        }

        /// <summary>The edge file tiles along v (the left/right edges repeat their 32-pixel segment): Repeat in v, Clamp in u.</summary>
        public static WowTextureOptions OptionsFor(string path)
        {
            return string.Equals(path, DialogBorder, StringComparison.OrdinalIgnoreCase) ? new WowTextureOptions(false, TextureWrapMode.Clamp, TextureWrapMode.Repeat, 1) : WowTextureOptions.Ui;
        }

        public static Texture2D Tex(string path)
        {
            Texture2D t;

            if (Textures.TryGetValue(path, out t) && t != null)
            {
                return t;
            }

            if (_files == null)
            {
                return null;
            }

            try
            {
                t = WowTextures.Get(_files, path, OptionsFor(path));
            }
            catch (Exception e)
            {
                WowLog.Warn("UI texture " + path + ": " + e.Message);
                t = null;
            }

            Textures[path] = t;
            return t;
        }

        /// <summary>The dialog background tiled (Repeat wrap).</summary>
        public static Texture2D BackgroundTile()
        {
            if (_backgroundTile != null || _files == null)
            {
                return _backgroundTile;
            }

            try
            {
                _backgroundTile = WowTextures.Get(_files, DialogBackground, new WowTextureOptions(false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1));
            }
            catch (Exception e)
            {
                WowLog.Warn("UI texture " + DialogBackground + ": " + e.Message);
            }

            return _backgroundTile;
        }

        /// <summary>
        /// Glue-Panel-Button-Highlight is DXT1 without alpha, drawn with alphaMode ADD in GlueButtons.xml. uGUI's default
        /// material blends by alpha, so the picture is turned into colour / max-channel with alpha = max-channel: over a
        /// dark button that looks like the additive glow without a custom UI shader.
        /// </summary>
        public static Texture2D Highlight()
        {
            if (_highlight == null)
            {
                _highlight = AdditiveAsAlpha(ButtonHighlight);
            }

            return _highlight;
        }

        private static readonly Dictionary<string, Texture2D> Additive = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A texture WoW draws with alphaMode ADD (glue highlights, the class button's hover/checked glow) as colour / max
        /// channel with alpha = max channel: over dark art uGUI's alpha blending then looks like the additive glow.
        /// </summary>
        public static Texture2D AdditiveAsAlpha(string path)
        {
            Texture2D cached;

            if (Additive.TryGetValue(path, out cached) && cached != null)
            {
                return cached;
            }

            if (_files == null)
            {
                return null;
            }

            Texture2D result = null;

            try
            {
                byte[] blp = _files.Read(path);

                if (blp != null)
                {
                    BlpImage img = BlpImage.Decode(blp);
                    byte[] px = img.GetRgba32(0);

                    for (int i = 0; i < px.Length; i += 4)
                    {
                        int m = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));

                        if (m == 0)
                        {
                            px[i + 3] = 0;
                            continue;
                        }

                        px[i] = (byte)(px[i] * 255 / m);
                        px[i + 1] = (byte)(px[i + 1] * 255 / m);
                        px[i + 2] = (byte)(px[i + 2] * 255 / m);
                        px[i + 3] = (byte)m;
                    }

                    result = WowTextures.FromRgba32(img.Width, img.Height, px, WowTextureOptions.Ui, Path.GetFileNameWithoutExtension(path) + " (alpha)", false);
                }
            }
            catch (Exception e)
            {
                WowLog.Warn("UI texture " + path + ": " + e.Message);
            }

            Additive[path] = result;
            return result;
        }

        /// <summary>
        /// FRIZQT__.TTF from the MPQs into a dynamic TextMeshPro font. TMP_FontAsset.CreateFontAsset(path, ...) loads a font
        /// file through the FontEngine, so the bytes are written once to Application.temporaryCachePath. Falls back to
        /// TextMeshPro's default face (logged).
        /// </summary>
        private static void LoadFont(byte[] bytes)
        {
            if (_font != null)
            {
                return;
            }

            if (bytes == null || bytes.Length < 12)
            {
                _fontSource = "TMP default (" + FontPath + " not readable)";
                WowLog.Warn("Font: " + _fontSource);
                return;
            }

            string path = null;

            try
            {
                string dir = Path.Combine(Application.temporaryCachePath, "SomniumWoW");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "FRIZQT__.TTF");

                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                {
                    File.WriteAllBytes(path, bytes);
                }

                _font = TMP_FontAsset.CreateFontAsset(path, 0, 72, 7, GlyphRenderMode.SDFAA, 1024, 1024);

                if (_font == null)
                {
                    var unityFont = new Font(path);
                    _font = TMP_FontAsset.CreateFontAsset(unityFont, 72, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                }
            }
            catch (Exception e)
            {
                WowLog.Warn("Font: " + FontPath + " via " + (path ?? "(no path)") + " failed: " + e.GetType().Name + ": " + e.Message);
                _font = null;
            }

            if (_font == null)
            {
                _fontSource = "TMP default (FRIZQT__.TTF could not be loaded)";
                WowLog.Warn("Font: " + _fontSource);
                return;
            }

            _font.name = "FRIZQT__ (WoW)";

            if (TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset != _font)
            {
                _font.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            }

            _fontSource = FontPath + " (" + bytes.Length + " B, dynamic SDF via " + path + ", shader " + (_font.material != null && _font.material.shader != null ? _font.material.shader.name : "?") + ")";
            WowLog.Info("Font: " + _fontSource);
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>A world-space canvas the client's pointer can use in both modes (AomUiPanel / EtsHqStage recipe).</summary>
        public static Canvas MakeCanvas(string name, Transform parent, float width, float height, float metresPerUnit)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = WowPlayer.Head();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * metresPerUnit;

            // A transparent full-size target so a ray anywhere on the panel is caught (AomUiPanel.cs:190-200).
            var catcher = Place(go.transform, "Pointer catcher", 0f, 0f, width, height).gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            return canvas;
        }

        /// <summary>A child rect at (x, y) from the parent's top-left, w x h canvas units.</summary>
        public static RectTransform Place(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        /// <summary>A BLP picture; coords = WoW texcoords (x = left, y = top, width, height), rows as stored.</summary>
        public static RawImage Picture(Transform parent, string name, Texture2D tex, Rect wowCoords, float x, float y, float w, float h)
        {
            RawImage img = Place(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.uvRect = Flip(wowCoords);
            img.raycastTarget = false;

            if (tex == null)
            {
                img.color = new Color(0f, 0f, 0f, 0f);
            }

            return img;
        }

        /// <summary>WoW texcoords (left, top, width, height) -> RawImage uvRect with v flipped (rule 15).</summary>
        public static Rect Flip(Rect wow)
        {
            return new Rect(wow.x, wow.y + wow.height, wow.width, -wow.height);
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h)
        {
            var t = Place(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = text ?? "";

            // GlueFontNormal inherits an outlined system font with a 1,-1 black shadow: a thin dark outline stands in.
            try
            {
                t.outlineWidth = 0.18f;
                t.outlineColor = new Color32(0, 0, 0, 255);
            }
            catch (Exception)
            {
                // A fallback shader without outline properties: plain text.
            }

            return t;
        }

        /// <summary>
        /// WoW's backdrop (GlueDialog.xml:43-53): a tiled background inset by 11/12/12/11 and a 32-unit edge from the
        /// 256x32 edge file's eight segments (left, right, top, bottom, top-left, top-right, bottom-left, bottom-right).
        /// The top and bottom edges are the left and right segments turned 90 degrees clockwise, so each segment's outer
        /// side faces out without relying on how segments 2 and 3 are stored.
        /// </summary>
        public static void Backdrop(Transform parent, float x, float y, float w, float h, float edge)
        {
            RectTransform box = Place(parent, "Backdrop", x, y, w, h);
            Texture2D bg = BackgroundTile();
            Texture2D border = Tex(DialogBorder);
            const float Tile = 32f;
            float il = 11f * edge / 32f, ir = 12f * edge / 32f, it = 12f * edge / 32f, ib = 11f * edge / 32f;

            RawImage back = Picture(box, "Background", bg, new Rect(0f, 0f, 1f, 1f), il, it, w - il - ir, h - it - ib);
            back.uvRect = new Rect(0f, (h - it - ib) / Tile, (w - il - ir) / Tile, -(h - it - ib) / Tile);

            if (bg == null)
            {
                back.color = new Color(0.04f, 0.04f, 0.05f, 0.94f);
            }

            if (border == null)
            {
                return;
            }

            const float S = 1f / 8f;
            float midH = h - 2f * edge, midW = w - 2f * edge;

            // Left and right edges, tiled vertically.
            RawImage left = Picture(box, "Edge L", border, new Rect(0f * S, 0f, S, 1f), 0f, edge, edge, midH);
            left.uvRect = new Rect(0f * S, midH / edge, S, -midH / edge);
            RawImage right = Picture(box, "Edge R", border, new Rect(1f * S, 0f, S, 1f), w - edge, edge, edge, midH);
            right.uvRect = new Rect(1f * S, midH / edge, S, -midH / edge);

            // Top and bottom: the left / right segment rotated 90 degrees clockwise (outer side up / down).
            Rotated(box, "Edge T", border, 0f * S, edge + midW / 2f, edge / 2f, midW, edge);
            Rotated(box, "Edge B", border, 1f * S, edge + midW / 2f, h - edge / 2f, midW, edge);

            // Corners: segments 4..7.
            Picture(box, "Corner TL", border, new Rect(4f * S, 0f, S, 1f), 0f, 0f, edge, edge);
            Picture(box, "Corner TR", border, new Rect(5f * S, 0f, S, 1f), w - edge, 0f, edge, edge);
            Picture(box, "Corner BL", border, new Rect(6f * S, 0f, S, 1f), 0f, h - edge, edge, edge);
            Picture(box, "Corner BR", border, new Rect(7f * S, 0f, S, 1f), w - edge, h - edge, edge, edge);
        }

        private static void Rotated(Transform parent, string name, Texture2D tex, float u0, float cx, float cy, float length, float edge)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(cx, -cy);
            r.sizeDelta = new Vector2(edge, length);
            r.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            img.uvRect = new Rect(u0, length / edge, 1f / 8f, -length / edge);
        }

        public static WowGlueButton Button(Transform parent, string label, float x, float y, float w, float h, float textSize, Action onClick)
        {
            RectTransform r = Place(parent, "Button " + label, x, y, w, h);
            var button = r.gameObject.AddComponent<WowGlueButton>();
            button.Build(label, textSize, onClick);
            return button;
        }
    }

    /// <summary>
    /// Keeps a room canvas where the player can read it (EtsHqStage.PlacePanel's rule). Headset: upright, at eye height,
    /// VrDistance ahead of the head. Monitor: the client's camera is an orbit several metres behind the avatar with a
    /// ~40 degree vertical field of view, so a panel standing 2.5 m in front of the avatar would be a few hundred pixels
    /// wide; there the panel is put along the camera's forward (pitch limited to +-27 degrees), square to it, at the
    /// distance where its height fills 85 % of the view. Placed 1 s after it is shown, again after the player looked more
    /// than 100 degrees away from it for 4 s, or walked more than 8 m from it (checked 4 times a second). The canvas's
    /// worldCamera is set every frame (needed for the pointer in a headset).
    /// </summary>
    public sealed class WowPanelPlacer : MonoBehaviour
    {
        public float HeightMetres = 1.5f;
        public float VrDistance = 2.5f;
        public string What = "panel";

        private Canvas _canvas;
        private float _shownAt, _nextCheck, _awayFor;
        private bool _placed;

        private void OnEnable()
        {
            _canvas = GetComponent<Canvas>();
            _shownAt = Time.unscaledTime;
            _placed = false;
            _awayFor = 0f;
        }

        public void Replace()
        {
            _placed = false;
            _shownAt = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            Camera head = WowPlayer.Head();

            if (_canvas != null)
            {
                _canvas.worldCamera = head;
            }

            if (head == null || Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.unscaledTime + 0.25f;
            Vector3 eye = head.transform.position;

            // Only when the eye is in the room (the camera may still be at a previous place).
            if ((eye - WowSpace.LoginRoomOrigin).magnitude > WowRoom.VoidRadius - 3f)
            {
                return;
            }

            if (!_placed)
            {
                if (Time.unscaledTime - _shownAt > 1f) Place(head, "shown");
                return;
            }

            Vector3 to = transform.position - eye, fwd = head.transform.forward;
            to.y = 0f;
            fwd.y = 0f;

            if (to.magnitude > 8f || to.magnitude < 0.8f)
            {
                Place(head, to.magnitude > 8f ? "the player moved away" : "the player walked into it");
                return;
            }

            bool away = fwd.sqrMagnitude > 1e-4f && Vector3.Angle(fwd, to) > 100f;
            _awayFor = away ? _awayFor + 0.25f : 0f;

            if (_awayFor > 4f)
            {
                Place(head, "the player looked away");
            }
        }

        private void Place(Camera head, string why)
        {
            bool vr = WowPlayer.XrPresent;
            Vector3 eye = head.transform.position;
            Vector3 fwd = head.transform.forward;
            float tan = Mathf.Tan(Mathf.Clamp(head.fieldOfView, 20f, 120f) * 0.5f * Mathf.Deg2Rad);
            float distance = vr ? VrDistance : Mathf.Clamp(HeightMetres / 0.85f / (2f * tan), 1.2f, 12f);
            fwd.y = vr ? 0f : Mathf.Clamp(fwd.y, -0.45f, 0.45f);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 at = eye + fwd * distance;
            float floor = WowSpace.LoginRoomOrigin.y;
            at.y = vr ? Mathf.Clamp(eye.y, floor + 0.9f, floor + 2.4f) : Mathf.Max(at.y, floor + HeightMetres * 0.5f + 0.05f);
            transform.position = at;
            transform.rotation = Quaternion.LookRotation(vr ? fwd : (at - eye).normalized, Vector3.up);
            _placed = true;
            _awayFor = 0f;
            WowLog.Info("Room: " + What + " placed " + distance.ToString("0.00") + " m in front of the " + (vr ? "headset" : "camera (" + head.fieldOfView.ToString("0") + " degrees)") + " (" + why + ").");
        }
    }

    /// <summary>
    /// GlueButtonTemplate (GlueButtons.xml:45-65): Glue-Panel-Button-Up / -Down art, the highlight on hover, the label
    /// in GlueFontNormal gold (white while hovered). The blue variant marks the selected choice. Clicked through uGUI's
    /// pointer events, which the client raises for its mouse (GraphicRaycaster) and its VR ray (TrackedDeviceGraphicRaycaster).
    /// </summary>
    public sealed class WowGlueButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private RawImage _art;
        private RawImage _glow;
        private TextMeshProUGUI _label;
        private Image _fallback;
        private Action _onClick;
        private bool _hover, _down, _selected, _interactable = true;
        private float _lastClick;

        public string Label { get { return _label != null ? _label.text : ""; } }

        public void Build(string label, float textSize, Action onClick)
        {
            _onClick = onClick;
            var rect = (RectTransform)transform;
            float w = rect.sizeDelta.x, h = rect.sizeDelta.y;

            // Something the raycasters hit even without art.
            _fallback = gameObject.AddComponent<Image>();
            _fallback.color = WowUi.HasGameArt ? new Color(0f, 0f, 0f, 0f) : new Color(0.25f, 0.05f, 0.03f, 0.9f);
            _fallback.raycastTarget = true;

            _art = WowUi.Picture(transform, "Art", WowUi.Tex(WowUi.ButtonUp), WowUi.ButtonCoords, 0f, 0f, w, h);
            _glow = WowUi.Picture(transform, "Highlight", WowUi.Highlight(), WowUi.HighlightCoords, 0f, 0f, w, h);
            _glow.gameObject.SetActive(false);
            // GlueButtonTemplate's ButtonText is centred 3 units up.
            _label = WowUi.Text(transform, "Label", label, textSize, WowUi.GlueGold, TextAlignmentOptions.Center, 6f, -3f, w - 12f, h);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            Refresh();
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Refresh(); }
        }

        public bool Interactable
        {
            get { return _interactable; }
            set { _interactable = value; if (!value) { _hover = false; _down = false; } Refresh(); }
        }

        public void SetLabel(string text)
        {
            if (_label != null) _label.text = text;
        }

        private void Refresh()
        {
            if (_art == null)
            {
                return;
            }

            string path = _selected ? (_down ? WowUi.ButtonDownBlue : WowUi.ButtonUpBlue) : (_down ? WowUi.ButtonDown : WowUi.ButtonUp);
            Texture2D t = WowUi.Tex(path);
            _art.texture = t;
            _art.color = t == null ? new Color(0f, 0f, 0f, 0f) : (_interactable ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f));

            // No glue art (the game files are not open yet - e.g. the room's folder browser before the game is found): the
            // plain box shows the state itself - blue when selected, lighter while hovered, dim when not pressable.
            if (t == null && _fallback != null)
            {
                _fallback.color = !_interactable ? new Color(0.12f, 0.1f, 0.1f, 0.9f)
                    : _selected ? (_hover ? new Color(0.1f, 0.3f, 0.65f, 0.95f) : new Color(0.06f, 0.2f, 0.5f, 0.95f))
                    : _hover ? new Color(0.42f, 0.1f, 0.05f, 0.95f) : new Color(0.25f, 0.05f, 0.03f, 0.9f);
            }
            _glow.gameObject.SetActive(_hover && _interactable && _glow.texture != null);
            _label.color = !_interactable ? WowUi.Grey : (_hover || _selected ? Color.white : WowUi.GlueGold);
            _label.rectTransform.anchoredPosition = new Vector2(6f + (_down ? 1f : 0f), 3f - (_down ? 1f : 0f));
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (!_interactable || Time.unscaledTime - _lastClick < 0.35f)
            {
                return;
            }

            _lastClick = Time.unscaledTime;
            WowLog.Info("UI: '" + Label + "' pressed (" + (e != null && e.pointerId < 0 ? "mouse" : "pointer " + (e != null ? e.pointerId.ToString() : "?")) + ")");

            try
            {
                if (_onClick != null) _onClick();
            }
            catch (Exception ex)
            {
                WowLog.Error("UI: '" + Label + "' failed: " + ex);
            }
        }

        /// <summary>Presses it from code (the wrist touch).</summary>
        public void Press()
        {
            OnPointerClick(null);
        }
    }

    /// <summary>
    /// CharacterCreateIconButtonTemplate (CharacterCreate.xml:13-62) scaled to Size units: the icon from an atlas
    /// (UI-CharacterCreate-Classes with CLASS_ICON_TCOORDS), the soft shadow behind it (58/38 of the size, alpha 0.85),
    /// the blue hover glow and the gold checked glow from UI-CharacterCreate-Highlights ((0,0.5,0,0.5) / (0.5,1,0,0.5),
    /// alphaMode ADD, drawn through WowUi.AdditiveAsAlpha), pressed = icon shifted 2,-2 (the PushedTexture anchor).
    /// </summary>
    public sealed class WowIconButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private RawImage _icon, _hover, _checked;
        private Action _onClick;
        private bool _hoverOn, _down, _selected;
        private float _size, _lastClick;
        private string _name = "";

        public static WowIconButton Create(Transform parent, string name, Texture2D atlas, Rect wowCoords, float x, float y, float size, Action onClick)
        {
            RectTransform r = WowUi.Place(parent, "Icon Button " + name, x, y, size, size);
            var b = r.gameObject.AddComponent<WowIconButton>();
            b.Build(name, atlas, wowCoords, size, onClick);
            return b;
        }

        private void Build(string name, Texture2D atlas, Rect wowCoords, float size, Action onClick)
        {
            _name = name ?? "";
            _onClick = onClick;
            _size = size;
            float shadow = size * 58f / 38f;
            var catcher = gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            Texture2D shadowTex = WowUi.Tex(WowClassInfo.IconShadow);
            if (shadowTex != null)
            {
                RawImage s = WowUi.Picture(transform, "Shadow", shadowTex, new Rect(0f, 0f, 1f, 1f), (size - shadow) / 2f, (size - shadow) / 2f, shadow, shadow);
                s.color = new Color(1f, 1f, 1f, 0.85f);
            }

            _icon = WowUi.Picture(transform, "Icon", atlas, wowCoords, 0f, 0f, size, size);
            if (atlas == null) _icon.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            Texture2D glow = WowUi.AdditiveAsAlpha(WowClassInfo.Highlights);
            _hover = WowUi.Picture(transform, "Highlight", glow, new Rect(0f, 0f, 0.5f, 0.5f), 0f, 0f, size, size);
            _checked = WowUi.Picture(transform, "Checked", glow, new Rect(0.5f, 0f, 0.5f, 0.5f), 0f, 0f, size, size);
            if (glow == null)
            {
                _hover.color = new Color(0.3f, 0.5f, 1f, 0.35f);
                _checked.color = new Color(1f, 0.8f, 0.1f, 0.45f);
            }

            Refresh();
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Refresh(); }
        }

        private void Refresh()
        {
            if (_icon == null) return;
            _hover.gameObject.SetActive(_hoverOn);
            _checked.gameObject.SetActive(_selected);
            float shift = _down ? 2f * _size / 38f : 0f;
            _icon.rectTransform.anchoredPosition = new Vector2(shift, -shift);
        }

        public void OnPointerEnter(PointerEventData e) { _hoverOn = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hoverOn = false; _down = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (Time.unscaledTime - _lastClick < 0.3f) return;
            _lastClick = Time.unscaledTime;
            WowLog.Info("UI: icon '" + _name + "' pressed (" + (e != null && e.pointerId < 0 ? "mouse" : "pointer " + (e != null ? e.pointerId.ToString() : "?")) + ")");

            try
            {
                if (_onClick != null) _onClick();
            }
            catch (Exception ex)
            {
                WowLog.Error("UI: icon '" + _name + "' failed: " + ex);
            }
        }
    }
}
