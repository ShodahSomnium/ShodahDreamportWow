using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Building blocks for the game UI in WoW's own frame coordinates: canvas units, (0, 0) at the parent's top-left, y growing
    /// downwards (as FrameXML's TOPLEFT offsets read). BLPs are uploaded as stored (row 0 = v 0 = the picture's top, rule 15), so
    /// every picture's uvRect flips v: WoW texcoords (left, right, top, bottom) become uvRect (left, bottom, right-left, top-bottom).
    /// Two build-time switches: Over (arm panels: always-on-top materials) and Pointable (desktop: buttons catch the mouse).
    /// </summary>
    public static class UiKit
    {
        /// <summary>Unity's UI layer.</summary>
        public const int UnityUiLayer = 5;

        /// <summary>The layer UI objects are made on: Unity's UI layer, or the client's after-post "Overlay" layer while UiSharp draws the VR
        /// interface after DLSS (lead 2026-10-10).</summary>
        public static int Layer { get { return UiSharp.Layer; } }

        /// <summary>Either UI layer (the avatar measurements skip both; lead 2026-10-10).</summary>
        public static bool IsUiLayer(int layer) { return UiSharp.IsUiLayer(layer); }

        /// <summary>Building an arm panel: graphics get the always-on-top materials.</summary>
        public static bool Over;

        /// <summary>Building the desktop overlay: buttons get a raycast target for the EventSystem.</summary>
        public static bool Pointable;

        /// <summary>The surface buttons register with while building.</summary>
        public static UiSurface Surface;

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static void Move(RectTransform r, float x, float y)
        {
            r.anchoredPosition = new Vector2(x, -y);
        }

        public static void Size(RectTransform r, float w, float h)
        {
            r.sizeDelta = new Vector2(w, h);
        }

        /// <summary>A whole picture (texcoords 0..1).</summary>
        public static RawImage Pic(Transform parent, string name, Texture2D tex, float x, float y, float w, float h)
        {
            return Pic(parent, name, tex, 0f, 1f, 0f, 1f, x, y, w, h);
        }

        /// <summary>A picture with WoW texcoords (left, right, top, bottom; left > right mirrors, as in FrameXML).</summary>
        public static RawImage Pic(Transform parent, string name, Texture2D tex, float l, float r, float t, float b, float x, float y, float w, float h)
        {
            RawImage img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            Coords(img, l, r, t, b);
            if (tex == null) img.color = new Color(0f, 0f, 0f, 0f);
            if (Over) img.material = UiArt.OverUi;
            return img;
        }

        public static void Coords(RawImage img, float l, float r, float t, float b)
        {
            img.uvRect = new UnityEngine.Rect(l, b, r - l, t - b);
        }

        /// <summary>A plain colour rectangle.</summary>
        public static RawImage Solid(Transform parent, string name, Color c, float x, float y, float w, float h)
        {
            RawImage img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            img.texture = null;
            img.color = c;
            img.raycastTarget = false;
            if (Over) img.material = UiArt.OverUi;
            return img;
        }

        /// <summary>A label (FRIZQT unless a font is given), outlined like FrameXML's shadowed fonts unless outline is false.</summary>
        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align,
            float x, float y, float w, float h, TMP_FontAsset font = null, bool outline = true)
        {
            var t = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset f = font != null ? font : UiArt.Friz;
            if (f != null) t.font = f;
            Material m = UiArt.TextMaterial(t.font, outline, Over);
            if (m != null) t.fontSharedMaterial = m;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            // Overflow, not Truncate: TMP hides a line whose height does not fit the rect (a size 10 FRIZQT line needs ~12.5 units).
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.text = text ?? "";
            return t;
        }

        /// <summary>A bare button over a rect; visuals are added by the caller (Hilite / Pushed).</summary>
        public static UiButton Button(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform r = Rect(parent, name, x, y, w, h);
            var b = r.gameObject.AddComponent<UiButton>();
            b.Rt = r;
            if (Pointable)
            {
                // A transparent raycast target: uGUI only sends pointer events to a Graphic that is one.
                var catcher = r.gameObject.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }
            if (Surface != null) Surface.Add(b);
            return b;
        }

        /// <summary>
        /// UIPanelButtonTemplate (UIPanelTemplates.xml:30-55): three slices of UI-Panel-Button-Up (12 / stretched / 12, texcoords
        /// 0-0.09375 / 0.09375-0.53125 / 0.53125-0.625, v 0-0.6875), Down while pressed, Highlight (ADD) on hover, Disabled greyed;
        /// label GameFontNormal gold, white while hovered.
        /// </summary>
        public static UiButton PanelButton(Transform parent, string label, float x, float y, float w, float h, Action click)
        {
            UiButton b = Button(parent, "Button " + label, x, y, w, h);
            b.Click = click;
            b.Slices = new RawImage[3];
            Texture2D up = UiArt.Tex(UiArt.PanelButtonUp);
            b.Slices[0] = Pic(b.transform, "L", up, 0f, 0.09375f, 0f, 0.6875f, 0f, 0f, 12f, h);
            b.Slices[1] = Pic(b.transform, "M", up, 0.09375f, 0.53125f, 0f, 0.6875f, 12f, 0f, w - 24f, h);
            b.Slices[2] = Pic(b.transform, "R", up, 0.53125f, 0.625f, 0f, 0.6875f, w - 12f, 0f, 12f, h);
            b.SliceUp = up;
            b.SliceDown = UiArt.Tex(UiArt.PanelButtonDown);
            b.SliceDisabled = UiArt.Tex(UiArt.PanelButtonDisabled);
            b.Hilite = Pic(b.transform, "Highlight", UiArt.Add(UiArt.PanelButtonHighlight), 0f, 0.625f, 0f, 0.6875f, 0f, 0f, w, h);
            b.Hilite.enabled = false;
            b.Label = Text(b.transform, "Text", label, Mathf.Min(12f, h - 8f), UiArt.Gold, TextAlignmentOptions.Center, 4f, 0f, w - 8f, h);
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            b.LabelColor = UiArt.Gold;
            return b;
        }

        /// <summary>UIPanelCloseButton (UIPanelTemplates.xml): 32x32 UI-Panel-MinimizeButton Up / Down / Highlight (ADD).</summary>
        public static UiButton CloseButton(Transform parent, float x, float y, Action click)
        {
            UiButton b = Button(parent, "Close", x, y, 32f, 32f);
            b.Click = click;
            b.Art = Pic(b.transform, "Up", UiArt.Tex(UiArt.CloseUp), 0f, 0f, 32f, 32f);
            b.Pushed = Pic(b.transform, "Down", UiArt.Tex(UiArt.CloseDown), 0f, 0f, 32f, 32f);
            b.Pushed.enabled = false;
            b.Hilite = Pic(b.transform, "Highlight", UiArt.Add(UiArt.CloseHighlight), 0f, 0f, 32f, 32f);
            b.Hilite.enabled = false;
            return b;
        }

        /// <summary>A 32x32 art button (page arrows, scroll arrows).</summary>
        public static UiButton ArtButton(Transform parent, string name, string up, string down, string disabled, string hilite, float x, float y, float size, Action click)
        {
            UiButton b = Button(parent, name, x, y, size, size);
            b.Click = click;
            b.Art = Pic(b.transform, "Up", UiArt.Tex(up), 0f, 0f, size, size);
            b.ArtUp = UiArt.Tex(up);
            b.ArtDisabled = disabled != null ? UiArt.Tex(disabled) : null;
            if (down != null)
            {
                b.Pushed = Pic(b.transform, "Down", UiArt.Tex(down), 0f, 0f, size, size);
                b.Pushed.enabled = false;
            }
            if (hilite != null)
            {
                b.Hilite = Pic(b.transform, "Highlight", UiArt.Add(hilite), 0f, 0f, size, size);
                b.Hilite.enabled = false;
            }
            return b;
        }

        /// <summary>
        /// A 384x512 WoW panel from four pieces (256 + 128 wide, 256 + 256 tall) - QuestFramePanelTemplate's layout
        /// (QuestFrameTemplates.xml): TopLeft, TopRight, BotLeft, BotRight.
        /// </summary>
        public static RectTransform Frame4(Transform parent, string name, string tl, string tr, string bl, string br)
        {
            RectTransform r = Rect(parent, name, 0f, 0f, 384f, 512f);
            Pic(r, "TopLeft", UiArt.Tex(tl), 0f, 0f, 256f, 256f);
            Pic(r, "TopRight", UiArt.Tex(tr), 256f, 0f, 128f, 256f);
            Pic(r, "BotLeft", UiArt.Tex(bl), 0f, 256f, 256f, 256f);
            Pic(r, "BotRight", UiArt.Tex(br), 256f, 256f, 128f, 256f);
            return r;
        }

        /// <summary>
        /// An item / spell slot (ItemButtonTemplate / SpellButtonTemplate / ActionButtonTemplate): icon, UI-Quickslot2 frame 66x66
        /// centred 1 unit down on a 36 button (64 on a 37 item button), Depress while pressed, ButtonHilight-Square (ADD) on hover.
        /// </summary>
        public static UiButton Slot(Transform parent, string name, float x, float y, float size, bool frame)
        {
            UiButton b = Button(parent, name, x, y, size, size);
            b.Icon = Pic(b.transform, "Icon", null, 0f, 0f, size, size);
            if (frame)
            {
                float f = size * 66f / 36f;
                b.Frame = Pic(b.transform, "Normal", UiArt.Tex(UiArt.Quickslot), (size - f) * 0.5f, (size - f) * 0.5f + 1f, f, f);
            }
            b.Pushed = Pic(b.transform, "Pushed", UiArt.Tex(UiArt.QuickslotDepress), 0f, 0f, size, size);
            b.Pushed.enabled = false;
            b.Hilite = Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, size, size);
            b.Hilite.enabled = false;
            return b;
        }

        // ---- iteration 11 (uicore, proposal-uicore.md 5.1.2 / 5.2 / 6.2)

        /// <summary>
        /// A full-size transparent click blocker as the FIRST child of r: a non-interactable UiButton named "Blocker" with a raycast target,
        /// so the EventSystem (WowEntityViews.PointerOverUi, the client's CheckIfMouseOnUI) sees UI under the whole window - a click on a
        /// window's paper no longer reaches the world - and a window can make it a drop area (UiKit.DropTarget: the merchant = sell).
        /// Desktop (a raycast target is added only while Pointable or on a screen surface); on a VR world surface it registers too
        /// (ButtonAt skips it: not interactable and no Tip) so DropTargetAt can find it.
        /// </summary>
        public static UiButton Blocker(RectTransform r)
        {
            if (r == null) return null;
            var go = new GameObject("Blocker", typeof(RectTransform));
            go.layer = Layer;
            var br = (RectTransform)go.transform;
            br.SetParent(r, false);
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.pivot = new Vector2(0f, 1f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            br.SetAsFirstSibling();
            var b = go.AddComponent<UiButton>();
            b.Rt = br;
            b.IsBlocker = true;
            bool screen = Pointable || (Surface != null && !Surface.IsWorld);
            if (screen)
            {
                var catcher = go.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }
            b.Interactable = false;
            if (Surface != null) Surface.Add(b);
            return b;
        }

        /// <summary>Makes b a drag source (desktop drag / VR drag; clickPicksUp: a plain click picks it up too - WoW's item slots).
        /// source may return null (then nothing is picked up and the click goes to Press: Shift-click split, item targeting...).</summary>
        public static void Draggable(UiButton b, Func<UiPickupPayload> source, bool clickPicksUp)
        {
            if (b == null) return;
            b.DragSource = source;
            b.ClickPicksUp = clickPicksUp;
            EnsureDragHandle(b);
        }

        /// <summary>Makes b a drop target for the cursor payload (the target decides what a drop means: into(payload)).</summary>
        public static void DropTarget(UiButton b, Func<UiPickupPayload, bool> accepts, Action<UiPickupPayload> into, Func<UiPickupPayload, string> hint = null)
        {
            if (b == null) return;
            b.DropAccepts = accepts;
            b.DropInto = into;
            b.DropHint = hint;
            EnsureDragHandle(b);
        }

        /// <summary>Desktop only: the EventSystem drag / drop handler (left button) on the button's own object.</summary>
        private static void EnsureDragHandle(UiButton b)
        {
            bool desktop = (b.Surface != null && !b.Surface.IsWorld) || (b.Surface == null && Pointable);
            if (!desktop) return;
            UiDragHandle h = b.GetComponent<UiDragHandle>();
            if (h == null) h = b.gameObject.AddComponent<UiDragHandle>();
            h.Button = b;
            if (b.GetComponent<Graphic>() == null)
            {
                var catcher = b.gameObject.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }
        }

        /// <summary>
        /// StaticPopupButtonTemplate (StaticPopup.xml:4-27): 128 x 21, UI-DialogBox-Button-Up / -Down / -Highlight (ADD), TexCoords
        /// 0, 1, 0, 0.71875; GameFontNormal gold, white while hovered. Shared by UiPopup (and any owner's own popup).
        /// </summary>
        public static UiButton PopupButton(Transform parent, string label, float x, float y, Action click)
        {
            const float w = 128f, h = 21f;
            UiButton b = Button(parent, "Button " + label, x, y, w, h);
            b.Art = Pic(b.transform, "Up", UiArt.Tex(UiArt.DialogButtonUp), 0f, 1f, 0f, 0.71875f, 0f, 0f, w, h);
            b.ArtUp = UiArt.Tex(UiArt.DialogButtonUp);
            b.Pushed = Pic(b.transform, "Down", UiArt.Tex(UiArt.DialogButtonDown), 0f, 1f, 0f, 0.71875f, 0f, 0f, w, h);
            b.Pushed.enabled = false;
            b.Hilite = Pic(b.transform, "Highlight", UiArt.Add(UiArt.DialogButtonHighlight), 0f, 1f, 0f, 0.71875f, 0f, 0f, w, h);
            b.Hilite.enabled = false;
            b.Label = Text(b.transform, "Label", label ?? "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, w, h);
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            b.LabelColor = UiArt.Gold;
            b.Click = click;
            return b;
        }
    }

    /// <summary>
    /// A StatusBar: an optional dark background and the UI-StatusBar fill, cropped (not stretched) to the value like WoW's
    /// StatusBar. Set() only touches Unity when the value moved.
    /// </summary>
    public sealed class UiBar
    {
        public RectTransform Root;
        public RawImage Fill;
        public RawImage Back;
        private float _w, _h, _frac = -1f;
        private Color _color = new Color(-1f, 0f, 0f, 0f);

        public static UiBar Make(Transform parent, string name, float x, float y, float w, float h, Color color, bool background)
        {
            var bar = new UiBar();
            bar.Root = UiKit.Rect(parent, name, x, y, w, h);
            if (background) bar.Back = UiKit.Solid(bar.Root, "Background", new Color(0f, 0f, 0f, 0.5f), 0f, 0f, w, h);
            bar.Fill = UiKit.Pic(bar.Root, "Fill", UiArt.Tex(UiArt.StatusBar), 0f, 0f, w, h);
            bar._w = w;
            bar._h = h;
            bar.SetColor(color);
            bar.Set(1f);
            return bar;
        }

        public void Set(float frac)
        {
            if (float.IsNaN(frac)) frac = 0f;
            frac = Mathf.Clamp01(frac);
            if (Mathf.Abs(frac - _frac) < 0.0005f) return;
            _frac = frac;
            Fill.enabled = frac > 0.0005f;
            Fill.rectTransform.sizeDelta = new Vector2(_w * frac, _h);
            UiKit.Coords(Fill, 0f, frac, 0f, 1f);
        }

        public void SetColor(Color c)
        {
            if (c == _color) return;
            _color = c;
            if (Fill.texture == null) Fill.color = c;
            else Fill.color = c;
        }

        public float Width { get { return _w; } }
        public float Value { get { return _frac; } }
    }

    /// <summary>
    /// WoW's backdrop (bgFile tiled + edgeFile's eight segments: left, right, top, bottom, top-left, top-right, bottom-left,
    /// bottom-right), resizable. The top/bottom edges are the left/right segments turned 90 degrees (as WowUi.Backdrop, whose
    /// edge-file layout the front end checked on an x4 zoom).
    /// </summary>
    public sealed class UiBackdrop
    {
        public RectTransform Root;
        private RawImage _bg, _l, _r, _t, _b, _tl, _tr, _bl, _br;
        private float _edge, _inset, _tile;

        public static UiBackdrop Make(Transform parent, string name, string bgPath, string edgePath, float edge, float inset, float tile, Color bgTint)
        {
            var d = new UiBackdrop();
            d._edge = edge;
            d._inset = inset;
            d._tile = tile;
            d.Root = UiKit.Rect(parent, name, 0f, 0f, 10f, 10f);
            Texture2D bg = bgPath != null ? TiledTex(bgPath) : null;
            d._bg = UiKit.Pic(d.Root, "Background", bg, 0f, 0f, 1f, 1f);
            d._bg.color = bg != null ? bgTint : new Color(bgTint.r * 0.3f, bgTint.g * 0.3f, bgTint.b * 0.3f, 0.92f);
            Texture2D edgeTex = edgePath != null ? EdgeTex(edgePath) : null;
            if (edgeTex != null)
            {
                d._l = UiKit.Pic(d.Root, "L", edgeTex, 0f, 0f, 1f, 1f);
                d._r = UiKit.Pic(d.Root, "R", edgeTex, 0f, 0f, 1f, 1f);
                d._t = UiKit.Pic(d.Root, "T", edgeTex, 0f, 0f, 1f, 1f);
                d._b = UiKit.Pic(d.Root, "B", edgeTex, 0f, 0f, 1f, 1f);
                d._tl = UiKit.Pic(d.Root, "TL", edgeTex, 0f, 0f, 1f, 1f);
                d._tr = UiKit.Pic(d.Root, "TR", edgeTex, 0f, 0f, 1f, 1f);
                d._bl = UiKit.Pic(d.Root, "BL", edgeTex, 0f, 0f, 1f, 1f);
                d._br = UiKit.Pic(d.Root, "BR", edgeTex, 0f, 0f, 1f, 1f);
                foreach (RawImage e in new[] { d._t, d._b })
                {
                    e.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    e.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                }
            }
            return d;
        }

        private static Texture2D TiledTex(string path)
        {
            if (UiArt.Files == null) return null;
            Texture2D t = WowTextures.Get(UiArt.Files, path, new WowTextureOptions(false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 1));
            return ReferenceEquals(t, WowTextures.Missing) ? null : t;
        }

        private static Texture2D EdgeTex(string path)
        {
            if (UiArt.Files == null) return null;
            Texture2D t = WowTextures.Get(UiArt.Files, path, new WowTextureOptions(false, TextureWrapMode.Clamp, TextureWrapMode.Repeat, 1));
            return ReferenceEquals(t, WowTextures.Missing) ? null : t;
        }

        /// <summary>A different top/bottom background inset (OptionsSliderTemplate: 3 left/right, 6 top/bottom); negative = the same as left/right.</summary>
        public float InsetY = -1f;

        /// <summary>SetBackdropBorderColor.</summary>
        public void SetEdgeColor(Color c)
        {
            foreach (RawImage e in new[] { _l, _r, _t, _b, _tl, _tr, _bl, _br }) if (e != null) e.color = c;
        }

        public void Place(float x, float y, float w, float h)
        {
            UiKit.Move(Root, x, y);
            UiKit.Size(Root, w, h);
            float e = _edge, i = _inset, iy = InsetY >= 0f ? InsetY : _inset;
            UiKit.Move(_bg.rectTransform, i, iy);
            UiKit.Size(_bg.rectTransform, Mathf.Max(1f, w - 2f * i), Mathf.Max(1f, h - 2f * iy));
            _bg.uvRect = new UnityEngine.Rect(0f, (h - 2f * iy) / _tile, (w - 2f * i) / _tile, -(h - 2f * iy) / _tile);
            if (_l == null) return;
            const float S = 1f / 8f;
            float midH = Mathf.Max(0f, h - 2f * e), midW = Mathf.Max(0f, w - 2f * e);
            UiKit.Move(_l.rectTransform, 0f, e); UiKit.Size(_l.rectTransform, e, midH);
            _l.uvRect = new UnityEngine.Rect(0f * S, midH / e, S, -midH / e);
            UiKit.Move(_r.rectTransform, w - e, e); UiKit.Size(_r.rectTransform, e, midH);
            _r.uvRect = new UnityEngine.Rect(1f * S, midH / e, S, -midH / e);
            // Rotated pieces: pivot at the centre, size (edge, length) before the 90 degree turn.
            _t.rectTransform.anchoredPosition = new Vector2(e + midW * 0.5f, -e * 0.5f);
            _t.rectTransform.sizeDelta = new Vector2(e, midW);
            _t.uvRect = new UnityEngine.Rect(0f * S, midW / e, S, -midW / e);
            _b.rectTransform.anchoredPosition = new Vector2(e + midW * 0.5f, -(h - e * 0.5f));
            _b.rectTransform.sizeDelta = new Vector2(e, midW);
            _b.uvRect = new UnityEngine.Rect(1f * S, midW / e, S, -midW / e);
            UiKit.Move(_tl.rectTransform, 0f, 0f); UiKit.Size(_tl.rectTransform, e, e); UiKit.Coords(_tl, 4f * S, 5f * S, 0f, 1f);
            UiKit.Move(_tr.rectTransform, w - e, 0f); UiKit.Size(_tr.rectTransform, e, e); UiKit.Coords(_tr, 5f * S, 6f * S, 0f, 1f);
            UiKit.Move(_bl.rectTransform, 0f, h - e); UiKit.Size(_bl.rectTransform, e, e); UiKit.Coords(_bl, 6f * S, 7f * S, 0f, 1f);
            UiKit.Move(_br.rectTransform, w - e, h - e); UiKit.Size(_br.rectTransform, e, e); UiKit.Coords(_br, 7f * S, 8f * S, 0f, 1f);
        }
    }
}
