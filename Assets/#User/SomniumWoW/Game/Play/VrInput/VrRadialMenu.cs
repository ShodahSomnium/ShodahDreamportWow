using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// Iteration 11 (vrcast, proposal-vrcast.md 1.2 / 6.5): which rows a radial menu offers this frame - the five action bars (row -2 Right Bar 2,
    /// -1 Right Bar, 0 the main bar's page / form bar, +1 Bottom Left Bar, +2 Bottom Right Bar) and, from the pets owner (their H9b), the pet bar
    /// as row +3 while there is one and the vehicle / possess bar instead of row 0 while in control. Slotting (something on the action cursor)
    /// offers the five action bars only. WowVrInput fills one per frame.
    /// </summary>
    internal sealed class VrRows
    {
        public int MaxRow = WowActionSlots.MaxRow;
        public UiControlRow Pet, Vehicle;
        public bool Slotting;

        public UiControlRow ControlOf(int row)
        {
            if (Slotting) return null;
            if (row == 0) return Vehicle;
            return row == WowActionSlots.MaxRow + 1 ? Pet : null;
        }

        public bool Exists(int row) { return row >= WowActionSlots.MinRow && row <= MaxRow; }

        public string NameOf(int row)
        {
            if (!Exists(row)) return "";
            UiControlRow c = ControlOf(row);
            if (c != null) return c.Name ?? "";
            return row <= WowActionSlots.MaxRow ? WowActionSlots.RowName(row) : "";
        }
    }

    /// <summary>
    /// One hand's half-radial spell menu: a world-space canvas of two half bands (VrArt.HalfRings) with six WoW action buttons
    /// on the outer band - the same pieces the desktop action bar draws (UiActionBar.UiActionSlot: the action's icon, the
    /// UI-Quickslot2 frame at 66/36 of the icon, the black Radial360 cooldown sweep, the usable tint, CheckButtonHilight when
    /// Active, ButtonHilight-Square when highlighted) - and the slot's key on the inner band. The highlighted segment gets a
    /// gold wedge (VrArt.Bands cut by a Filled Radial360) and its details below the flat edge: name, cost / cast time,
    /// cooldown or why it cannot be used. Drawn over everything (UiKit.Over at build time), like the arm panels.
    /// Iteration 11 (vrcast): ROWS - the row's name above the ring, a depth ladder of pips at the ring's outer end (the lit one = this row),
    /// two faint ghost rings 7 cm in front of / behind it carrying the neighbour rows' names (the indicator that a push / pull reveals another
    /// set); the buttons are the current row's only. Slotting (WoW's action cursor): a green wedge, "Place X", the cursor's icon ghosted in
    /// the highlighted button; a preview (alpha 0.6, no highlight) follows each hand until a grip.
    /// Lead 2026-10-10 (the user, a mock-up of six boxed buttons in a row with the details box under them: "If I use Gear Select menu, can
    /// you make the UI a row of squares? In the same style, but not the twist menu repurposed"): Bar = the Gear Select layout - no rings
    /// or wedge; six tiles left to right (WoW's tooltip backdrop, its border gold, the same action button on top, the key under a gold
    /// line), the highlighted tile brighter and larger, the row's name above, the details box below, the row ladder upright at the outer
    /// end and the neighbour rows as faint bars GearRowMetres above (push row) / below (pull row). Sizes ours.
    /// </summary>
    internal sealed class VrRadialMenu
    {
        public const int Count = 6;
        private const float W = 512f, H = 360f, Cy = 256f;   // canvas units; the ring centre is at (256, Cy) from the top left
        public const float MetresPerUnit = 0.34f / 512f;      // outer radius 0.17 m
        private const float IconR = 200f, KeyR = 110f, Icon = 60f;
        private const int MaxPips = 6;                        // rows -2..+3 (+3 = the pets owner's pet row)
        // Lead 2026-10-10: the Gear Select bar (canvas units, ours) - 6 tiles TileW x TileH at Pitch, the row's name above, the details box
        // from InfoTop; the transform sits on the canvas's bottom centre (the grip's anchor), so the box clears the hand.
        private const float BarW = 600f, BarH = 236f, Pitch = 88f, TileW = 78f, TileH = 104f, TileTop = 30f, BarIcon = 54f, InfoTop = 142f;
        private const float BarHiScale = 1.15f, PipStep = 20f;
        // Lead 2026-10-10 (immersive bow): the bow's column - the same tiles stacked upward at ColPitch from the selection point
        // (ColCx, ColSel = the canvas centre, the transform's pivot), scrolled down one ColPitch per slot drawn; ColScale of the ring's size.
        private const float ColW = 1120f, ColH = 1300f, ColCx = 560f, ColSel = 650f, ColPitch = 112f, ColScale = 0.8f;
        private static readonly Color EdgeGold = new Color(0.85f, 0.66f, 0.12f), EdgeBright = new Color(1f, 0.92f, 0.45f);
        private static readonly Color TileBack = new Color(0.04f, 0.04f, 0.09f, 0.72f);
        /// <summary>Ours: the preview's alpha (proposal-vrcast 1.4: "60 % opaque").</summary>
        public const float PreviewAlpha = 0.6f;

        private static readonly string[] KeysRight = { "1", "2", "3", "4", "5", "6" };
        private static readonly string[] KeysLeft = { "7", "8", "9", "0", "-", "=" };
        private static readonly string[] AltRight = { "A-1", "A-2", "A-3", "A-4", "A-5", "A-6" };
        private static readonly string[] AltLeft = { "A-7", "A-8", "A-9", "A-0", "A--", "A-=" };
        private static readonly string[] NumRight = { "1", "2", "3", "4", "5", "6" };
        private static readonly string[] NumLeft = { "7", "8", "9", "10", "11", "12" };
        private static readonly Color WedgeGold = new Color(1f, 0.78f, 0.25f, 0.42f);
        private static readonly Color WedgeGreen = new Color(0.25f, 1f, 0.35f, 0.42f);
        private static readonly Color PlaceGreen = new Color(0.25f, 1f, 0.35f, 1f);
        private static readonly Color KeyWhite = new Color(0.85f, 0.85f, 0.85f), KeyGrey = new Color(0.55f, 0.55f, 0.55f);

        private sealed class Seg
        {
            public RectTransform Rt;
            public RawImage Icon, Frame, Checked, Hilite, Empty, Ghost;
            public Image Sweep;
            public TextMeshProUGUI Key, Cost;
            public readonly UiActionView View = new UiActionView();
            public int Shown = int.MinValue, Tint = -1, CostShown = -1, CountKind = -2;
            public string IconPath, GhostPath;
            public bool IconLoaded, GhostLoaded;
            public float SweepShown = -1f;
            public bool Big;
            public UiBackdrop Back;              // lead 2026-10-10: the Gear Select tile (null on the ring)
            public float HiScale = 1.2f;
        }

        private sealed class Ghost
        {
            public GameObject Go;
            public RectTransform Root;
            public TextMeshProUGUI Name;
            public string Shown;
        }

        public readonly GameObject Go;
        public readonly bool Right;
        /// <summary>Lead 2026-10-10: the Gear Select bar layout (else the half ring).</summary>
        public readonly bool Bar;
        /// <summary>Lead 2026-10-10: the immersive bow's column (Right = the string hand; the details on the side away from it).</summary>
        public readonly bool Column;
        private readonly RectTransform _column;
        private float _scroll = float.NaN;
        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly Image _wedge;
        private readonly Seg[] _segs = new Seg[Count];
        private readonly TextMeshProUGUI _name, _line2, _line3, _title;
        private readonly RawImage _infoBack;
        private readonly RawImage[] _pips = new RawImage[MaxPips];
        private readonly Ghost _near, _far;
        private int _shownHi = -2, _keysRow = int.MinValue, _pipSig = int.MinValue;
        private bool _wedgeGreen;
        private string _shownName, _shown2, _shown3, _shownTitle;
        private Color _titleColor = UiArt.Gold;

        public VrRadialMenu(bool right, bool bar = false, bool column = false)
        {
            Right = right;
            Bar = bar && !column;
            Column = column;
            bool over = UiKit.Over;
            UiKit.Over = true;
            try
            {
                Go = new GameObject((column ? "WoW VR bow column (" : bar ? "WoW VR gear bar (" : "WoW VR radial menu (") + (right ? "right" : "left") + ")", typeof(RectTransform));
                Go.layer = UiKit.Layer;
                var canvas = Go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 400;
                UiSharp.Register(Go);   // lead 2026-10-10
                var scaler = Go.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 3f;
                _group = Go.AddComponent<CanvasGroup>();
                _group.interactable = false;
                _group.blocksRaycasts = false;
                _root = (RectTransform)Go.transform;
                _root.localScale = Vector3.one * MetresPerUnit;
                string[] keys = right ? KeysRight : KeysLeft;
                if (column)
                {
                    _root.sizeDelta = new Vector2(ColW, ColH);
                    _root.pivot = new Vector2(0.5f, 0.5f);   // the selection point on the anchor
                    _root.localScale = Vector3.one * (MetresPerUnit * ColScale);
                    RectTransform cc = UiKit.Rect(_root, "Content", 0f, 0f, ColW, ColH);
                    _column = UiKit.Rect(cc, "Column", 0f, 0f, ColW, ColH);
                    for (int i = 0; i < Count; i++) _segs[i] = MakeTile(_column, i, keys[i], ColCx - TileW * 0.5f, ColSel - TileH * 0.5f - i * ColPitch);
                    UiBackdrop sel = UiBackdrop.Make(cc, "Selection", UiArt.TooltipBackground, UiArt.TooltipBorder, 16f, 5f, 16f, new Color(0f, 0f, 0f, 0f));
                    sel.Place(ColCx - TileW * 0.5f - 10f, ColSel - TileH * 0.5f - 10f, TileW + 20f, TileH + 20f);
                    sel.SetEdgeColor(PlaceGreen);
                    float ix = right ? ColCx - TileW * 0.5f - 26f - 340f : ColCx + TileW * 0.5f + 26f;   // away from the string hand
                    _title = UiKit.Text(cc, "Row", "", 16f, UiArt.Gold, TextAlignmentOptions.Center, ix, ColSel - 46f - 26f, 340f, 24f);
                    for (int k = 0; k < MaxPips; k++) { _pips[k] = UiKit.Pic(cc, "Row pip " + k, VrArt.Glow, 0f, 0f, 14f, 14f); _pips[k].enabled = false; }
                    _infoBack = UiKit.Solid(cc, "Info back", new Color(0.02f, 0.02f, 0.05f, 0.72f), ix, ColSel - 46f, 340f, 92f);
                    _name = UiKit.Text(cc, "Name", "", 24f, UiArt.Gold, TextAlignmentOptions.Center, ix, ColSel - 42f, 340f, 28f);
                    _line2 = UiKit.Text(cc, "Cost", "", 18f, Color.white, TextAlignmentOptions.Center, ix, ColSel - 12f, 340f, 24f);
                    _line3 = UiKit.Text(cc, "Reason", "", 18f, UiArt.ErrorRed, TextAlignmentOptions.Center, ix, ColSel + 12f, 340f, 24f);
                    _infoBack.enabled = false;
                    Go.SetActive(false);
                    return;
                }
                if (bar)
                {
                    _root.sizeDelta = new Vector2(BarW, BarH);
                    _root.pivot = new Vector2(0.5f, 0f);   // the bottom centre on the anchor
                    RectTransform bc = UiKit.Rect(_root, "Content", 0f, 0f, BarW, BarH);
                    for (int i = 0; i < Count; i++) _segs[i] = MakeTile(bc, i, keys[i]);
                    _title = UiKit.Text(bc, "Row", "", 16f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 2f, BarW, 24f);
                    for (int k = 0; k < MaxPips; k++)
                    {
                        _pips[k] = UiKit.Pic(bc, "Row pip " + k, VrArt.Glow, (right ? BarW - 20f : 20f) - 7f, 0f, 14f, 14f);
                        _pips[k].uvRect = new Rect(0f, 0f, 1f, 1f);
                        _pips[k].enabled = false;
                    }
                    float ix = (BarW - 340f) * 0.5f;
                    _infoBack = UiKit.Solid(bc, "Info back", new Color(0.02f, 0.02f, 0.05f, 0.72f), ix, InfoTop, 340f, 92f);
                    _name = UiKit.Text(bc, "Name", "", 24f, UiArt.Gold, TextAlignmentOptions.Center, ix, InfoTop + 4f, 340f, 28f);
                    _line2 = UiKit.Text(bc, "Cost", "", 18f, Color.white, TextAlignmentOptions.Center, ix, InfoTop + 34f, 340f, 24f);
                    _line3 = UiKit.Text(bc, "Reason", "", 18f, UiArt.ErrorRed, TextAlignmentOptions.Center, ix, InfoTop + 58f, 340f, 24f);
                    _infoBack.enabled = false;
                    _near = MakeGhost("near");
                    _far = MakeGhost("far");
                    Go.SetActive(false);
                    return;
                }
                _root.sizeDelta = new Vector2(W, H);
                _root.pivot = new Vector2(0.5f, (H - Cy) / H);   // the transform sits on the ring centre
                RectTransform content = UiKit.Rect(_root, "Content", 0f, 0f, W, H);

                RawImage rings = UiKit.Pic(content, "Rings", VrArt.HalfRings(Count), 0f, 0f, W, Cy);
                rings.uvRect = new Rect(0f, 0f, 1f, 1f);   // VrArt pictures are in Unity orientation, not WoW's

                RectTransform wr = UiKit.Rect(content, "Highlight wedge", 0f, 0f, 2f * Cy, 2f * Cy);
                wr.pivot = new Vector2(0.5f, 0.5f);
                wr.anchoredPosition = new Vector2(W * 0.5f, -Cy);
                _wedge = wr.gameObject.AddComponent<Image>();
                _wedge.sprite = VrArt.Bands;
                _wedge.type = Image.Type.Filled;
                _wedge.fillMethod = Image.FillMethod.Radial360;
                _wedge.fillOrigin = (int)Image.Origin360.Top;
                _wedge.fillClockwise = true;
                _wedge.fillAmount = 0.5f / Count;
                _wedge.color = WedgeGold;
                _wedge.raycastTarget = false;
                _wedge.material = UiArt.OverUi;
                _wedge.enabled = false;

                for (int i = 0; i < Count; i++) _segs[i] = MakeSeg(content, i, keys[i]);

                // the row's name above the ring; the depth ladder at the ring's outer end (right end for the right hand)
                _title = UiKit.Text(content, "Row", "", 16f, UiArt.Gold, TextAlignmentOptions.Center, 0f, -30f, W, 24f);
                for (int k = 0; k < MaxPips; k++)
                {
                    _pips[k] = UiKit.Pic(content, "Row pip " + k, VrArt.Glow, (right ? W - 30f : 30f) - 7f, 0f, 14f, 14f);
                    _pips[k].uvRect = new Rect(0f, 0f, 1f, 1f);
                    _pips[k].enabled = false;
                }

                _infoBack = UiKit.Solid(content, "Info back", new Color(0.02f, 0.02f, 0.05f, 0.72f), 96f, Cy + 6f, W - 192f, 92f);
                _name = UiKit.Text(content, "Name", "", 24f, UiArt.Gold, TextAlignmentOptions.Center, 96f, Cy + 10f, W - 192f, 28f);
                _line2 = UiKit.Text(content, "Cost", "", 18f, Color.white, TextAlignmentOptions.Center, 96f, Cy + 40f, W - 192f, 24f);
                _line3 = UiKit.Text(content, "Reason", "", 18f, UiArt.ErrorRed, TextAlignmentOptions.Center, 96f, Cy + 64f, W - 192f, 24f);
                _infoBack.enabled = false;

                _near = MakeGhost("near");
                _far = MakeGhost("far");
            }
            finally
            {
                UiKit.Over = over;
            }
            Go.SetActive(false);
        }

        /// <summary>A ghost ring: the half bands only (alpha 0.18) and the neighbour row's name (alpha 0.5) - ours.</summary>
        private Ghost MakeGhost(string which)
        {
            var g = new Ghost();
            g.Go = new GameObject("WoW VR radial ghost (" + (Right ? "right" : "left") + ", " + which + ")", typeof(RectTransform));
            g.Go.layer = UiKit.Layer;
            var canvas = g.Go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 399;
            UiSharp.Register(g.Go);   // lead 2026-10-10
            var scaler = g.Go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            var group = g.Go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            g.Root = (RectTransform)g.Go.transform;
            if (Bar)
            {
                // lead 2026-10-10: a faint bar - six empty tiles (alpha 0.3) and the row's name (alpha 0.5), aligned with the main bar
                g.Root.sizeDelta = new Vector2(BarW, BarH);
                g.Root.pivot = new Vector2(0.5f, 0f);
                g.Root.localScale = Vector3.one * MetresPerUnit;
                RectTransform bc = UiKit.Rect(g.Root, "Content", 0f, 0f, BarW, BarH);
                var tiles = UiKit.Rect(bc, "Tiles", 0f, 0f, BarW, BarH).gameObject.AddComponent<CanvasGroup>();
                tiles.alpha = 0.3f;
                tiles.interactable = false;
                tiles.blocksRaycasts = false;
                for (int i = 0; i < Count; i++)
                {
                    UiBackdrop t = UiBackdrop.Make(tiles.transform, "Tile " + (i + 1), UiArt.TooltipBackground, UiArt.TooltipBorder, 16f, 5f, 16f, TileBack);
                    t.Place(TileX(i), TileTop, TileW, TileH);
                    t.SetEdgeColor(EdgeGold);
                }
                g.Name = UiKit.Text(bc, "Row", "", 16f, new Color(1f, 0.82f, 0f, 0.5f), TextAlignmentOptions.Center, 0f, 2f, BarW, 24f);
                g.Go.SetActive(false);
                return g;
            }
            g.Root.sizeDelta = new Vector2(W, H);
            g.Root.pivot = new Vector2(0.5f, (H - Cy) / H);
            g.Root.localScale = Vector3.one * MetresPerUnit;
            RectTransform content = UiKit.Rect(g.Root, "Content", 0f, 0f, W, H);
            RawImage rings = UiKit.Pic(content, "Rings", VrArt.HalfRings(Count), 0f, 0f, W, Cy);
            rings.uvRect = new Rect(0f, 0f, 1f, 1f);
            rings.color = new Color(1f, 1f, 1f, 0.18f);
            g.Name = UiKit.Text(content, "Row", "", 16f, new Color(1f, 0.82f, 0f, 0.5f), TextAlignmentOptions.Center, 0f, -30f, W, 24f);
            g.Go.SetActive(false);
            return g;
        }

        /// <summary>Lead 2026-10-10: the left edge of Gear Select tile i (seg 0 leftmost, as VrMath.Segment counts).</summary>
        private static float TileX(int i)
        {
            return BarW * 0.5f + (i - (Count - 1) * 0.5f) * Pitch - TileW * 0.5f;
        }

        /// <summary>Lead 2026-10-10: a Gear Select tile - the tooltip backdrop with a gold border, the action button (as MakeSeg's) on
        /// top, a gold line, the key under it. Rt = the tile (it grows when highlighted).</summary>
        private Seg MakeTile(RectTransform content, int i, string key)
        {
            return MakeTile(content, i, key, TileX(i), TileTop);
        }

        /// <summary>A tile with its top left at (x, y) (lead 2026-10-10: the bow's column stacks them).</summary>
        private Seg MakeTile(RectTransform content, int i, string key, float x, float y)
        {
            var s = new Seg();
            s.HiScale = BarHiScale;
            s.Rt = UiKit.Rect(content, "Slot " + (i + 1), x, y, TileW, TileH);
            s.Rt.pivot = new Vector2(0.5f, 0.5f);
            s.Rt.anchoredPosition = new Vector2(x + TileW * 0.5f, -(y + TileH * 0.5f));
            s.Back = UiBackdrop.Make(s.Rt, "Tile", UiArt.TooltipBackground, UiArt.TooltipBorder, 16f, 5f, 16f, TileBack);
            s.Back.Place(0f, 0f, TileW, TileH);
            s.Back.SetEdgeColor(EdgeGold);
            float ix = (TileW - BarIcon) * 0.5f;
            RectTransform b = UiKit.Rect(s.Rt, "Button", ix, 10f, BarIcon, BarIcon);
            s.Empty = UiKit.Solid(b, "Empty", new Color(0f, 0f, 0f, 0.45f), 0f, 0f, BarIcon, BarIcon);
            s.Icon = UiKit.Pic(b, "Icon", null, 0f, 0f, BarIcon, BarIcon);
            s.Icon.enabled = false;
            var sweepRt = UiKit.Rect(b, "Cooldown", 0f, 0f, BarIcon, BarIcon);
            s.Sweep = sweepRt.gameObject.AddComponent<Image>();
            s.Sweep.sprite = UiArt.WhiteSprite;
            s.Sweep.type = Image.Type.Filled;
            s.Sweep.fillMethod = Image.FillMethod.Radial360;
            s.Sweep.fillOrigin = (int)Image.Origin360.Top;
            s.Sweep.fillClockwise = false;
            s.Sweep.color = new Color(0f, 0f, 0f, 0.7f);
            s.Sweep.raycastTarget = false;
            s.Sweep.material = UiArt.OverUi;
            s.Sweep.enabled = false;
            s.Ghost = UiKit.Pic(b, "Placing", null, 0f, 0f, BarIcon, BarIcon);
            s.Ghost.enabled = false;
            float f = BarIcon * 66f / 36f;
            s.Frame = UiKit.Pic(b, "Normal", UiArt.Tex(UiArt.Quickslot), (BarIcon - f) * 0.5f, (BarIcon - f) * 0.5f + 1f, f, f);
            s.Checked = UiKit.Pic(b, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, BarIcon, BarIcon);
            s.Checked.enabled = false;
            s.Hilite = UiKit.Pic(b, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, BarIcon, BarIcon);
            s.Hilite.enabled = false;
            s.Cost = UiKit.Text(b, "Cost", "", 14f, Color.white, TextAlignmentOptions.BottomRight, 0f, BarIcon - 17f, BarIcon - 2f, 16f, UiArt.Numbers);
            UiKit.Solid(s.Rt, "Line", new Color(1f, 0.82f, 0f, 0.55f), 9f, 72f, TileW - 18f, 2f);
            s.Key = UiKit.Text(s.Rt, "Key " + key, key, 22f, KeyWhite, TextAlignmentOptions.Center, 0f, 75f, TileW, 26f, UiArt.Numbers);
            return s;
        }

        private Seg MakeSeg(RectTransform content, int i, string key)
        {
            var s = new Seg();
            float a = VrMath.CentreAngle(i, Count) * Mathf.Deg2Rad;
            float cx = W * 0.5f + Mathf.Cos(a) * IconR, cy = Cy - Mathf.Sin(a) * IconR;
            s.Rt = UiKit.Rect(content, "Slot " + (i + 1), cx - Icon * 0.5f, cy - Icon * 0.5f, Icon, Icon);
            s.Rt.pivot = new Vector2(0.5f, 0.5f);
            s.Rt.anchoredPosition = new Vector2(cx, -cy);
            s.Empty = UiKit.Solid(s.Rt, "Empty", new Color(0f, 0f, 0f, 0.45f), 0f, 0f, Icon, Icon);
            s.Icon = UiKit.Pic(s.Rt, "Icon", null, 0f, 0f, Icon, Icon);
            s.Icon.enabled = false;
            var sweepRt = UiKit.Rect(s.Rt, "Cooldown", 0f, 0f, Icon, Icon);
            s.Sweep = sweepRt.gameObject.AddComponent<Image>();
            s.Sweep.sprite = UiArt.WhiteSprite;
            s.Sweep.type = Image.Type.Filled;
            s.Sweep.fillMethod = Image.FillMethod.Radial360;
            s.Sweep.fillOrigin = (int)Image.Origin360.Top;
            s.Sweep.fillClockwise = false;
            s.Sweep.color = new Color(0f, 0f, 0f, 0.7f);
            s.Sweep.raycastTarget = false;
            s.Sweep.material = UiArt.OverUi;
            s.Sweep.enabled = false;
            s.Ghost = UiKit.Pic(s.Rt, "Placing", null, 0f, 0f, Icon, Icon);
            s.Ghost.enabled = false;
            float f = Icon * 66f / 36f;
            s.Frame = UiKit.Pic(s.Rt, "Normal", UiArt.Tex(UiArt.Quickslot), (Icon - f) * 0.5f, (Icon - f) * 0.5f + 1f, f, f);
            s.Checked = UiKit.Pic(s.Rt, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, Icon, Icon);
            s.Checked.enabled = false;
            s.Hilite = UiKit.Pic(s.Rt, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, Icon, Icon);
            s.Hilite.enabled = false;
            s.Cost = UiKit.Text(s.Rt, "Cost", "", 15f, Color.white, TextAlignmentOptions.BottomRight, 0f, Icon - 18f, Icon - 2f, 17f, UiArt.Numbers);
            float ka = a, kx = W * 0.5f + Mathf.Cos(ka) * KeyR, ky = Cy - Mathf.Sin(ka) * KeyR;
            s.Key = UiKit.Text(content, "Key " + key, key, 24f, KeyWhite, TextAlignmentOptions.Center, kx - 20f, ky - 14f, 40f, 28f, UiArt.Numbers);
            return s;
        }

        public bool Shown { get { return Go != null && Go.activeSelf; } }

        public void Show(bool on)
        {
            if (Go != null && Go.activeSelf != on) Go.SetActive(on);
            if (!on)
            {
                _shownHi = -2;
                ShowGhost(_near, false);
                ShowGhost(_far, false);
            }
        }

        private static void ShowGhost(Ghost g, bool on)
        {
            if (g != null && g.Go != null && g.Go.activeSelf != on) g.Go.SetActive(on);
        }

        /// <summary>Places the ring centre at pos, facing the eye (upright: world up as the up hint).</summary>
        public void Place(Vector3 pos, Vector3 eye)
        {
            _root.SetPositionAndRotation(pos, Face(pos, eye));
        }

        /// <summary>Lead 2026-10-10 (bow column): places the selection point at pos facing the eye with up as the column's up, and
        /// scrolls the column down u slots (slot u sits on the selection point).</summary>
        public void PlaceColumn(Vector3 pos, Vector3 eye, Vector3 up, float u)
        {
            Vector3 f = pos - eye;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            Vector3 upv = Vector3.ProjectOnPlane(up, f.normalized);
            if (upv.sqrMagnitude < 1e-6f) upv = Vector3.up;
            _root.SetPositionAndRotation(pos, Quaternion.LookRotation(f.normalized, upv.normalized));
            if (_column != null && !(Mathf.Abs(u - _scroll) < 1e-4f)) { _scroll = u; UiKit.Move(_column, 0f, u * ColPitch); }
        }

        /// <summary>
        /// Iteration 11: the ring at centre and its ghosts one RowStep along axisWorld (the push direction) - far = the push row (row + 1),
        /// near = the pull row (row - 1) - each shown only when that row exists.
        /// </summary>
        public void Place(Vector3 centre, Vector3 eye, Vector3 axisWorld, bool ghostNear, bool ghostFar)
        {
            _root.SetPositionAndRotation(centre, Face(centre, eye));
            // lead 2026-10-10: the Gear Select bar's rows are up (push row) / down (pull row), GearRowMetres apart, as the hand moves
            Vector3 step = Bar ? Vector3.up * WowVrInput.GearRowMetres : axisWorld * VrMath.RowStep;
            PlaceGhost(_far, ghostFar, centre + step, eye);
            PlaceGhost(_near, ghostNear, centre - step, eye);
        }

        private void PlaceGhost(Ghost g, bool on, Vector3 pos, Vector3 eye)
        {
            on = on && Shown && g != null;
            ShowGhost(g, on);
            if (on) g.Root.SetPositionAndRotation(pos, Face(pos, eye));
        }

        private static Quaternion Face(Vector3 pos, Vector3 eye)
        {
            Vector3 f = pos - eye;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            return Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        /// <summary>The 0-based action slot of segment seg on row (the right hand holds buttons 1-6 of the row's bar, the left 7-12).</summary>
        public int SlotAt(int row, int seg)
        {
            return WowActionSlots.RowSlot(row, Right, seg, UiBars.MainPage);
        }

        /// <summary>The packed action on segment seg of row (0 = empty).</summary>
        public int ActionAt(PlayerState p, int row, int seg)
        {
            int slot = SlotAt(row, seg);
            return p != null && p.ActionButtons != null && WowActionSlots.Valid(slot) ? p.ActionButtons[slot] : 0;
        }

        /// <summary>The control-bar code of segment seg on a pet / vehicle row (UiControlRow: -1 empty, -2 Leave, -3 / -4 seats).</summary>
        public int ControlAt(UiControlRow row, int seg)
        {
            if (row == null || seg < 0 || seg >= Count) return -1;
            int[] codes = Right ? row.Right : row.Left;
            return codes != null && seg < codes.Length ? codes[seg] : -1;
        }

        /// <summary>
        /// Per frame while shown: the row's buttons, the highlight (-1 none) and its details. preview = the slotting preview at a hand before
        /// the grip (alpha 0.6, no highlight).
        /// </summary>
        public void Refresh(IWowSession s, PlayerState p, VrRows rows, int row, int highlighted, bool preview, float now)
        {
            if (p == null || rows == null) return;
            if (preview) highlighted = -1;
            _group.alpha = preview ? PreviewAlpha : 1f;
            UiControlRow control = rows.ControlOf(row);
            bool slotting = rows.Slotting && UiActionCursor.Active;
            bool empty = true;
            for (int i = 0; i < Count; i++)
            {
                bool hi = i == highlighted;
                if (control != null) { RefreshControlSeg(_segs[i], s, ControlAt(control, i), hi); empty &= ControlAt(control, i) == -1; }
                else
                {
                    int packed = ActionAt(p, row, i);
                    RefreshSeg(_segs[i], p, packed, hi, slotting && hi);
                    empty &= packed == 0;
                }
            }
            RefreshKeys(row, control != null);
            RefreshLadder(rows, row);

            if (highlighted != _shownHi && _wedge != null)
            {
                _shownHi = highlighted;
                _wedge.enabled = highlighted >= 0;
                if (highlighted >= 0)
                {
                    float span = 180f / Count;
                    float delta = VrMath.CentreAngle(highlighted, Count) + span * 0.5f - 90f;
                    _wedge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, delta);
                }
            }
            if (_wedge != null && _wedge.enabled && _wedgeGreen != slotting) { _wedgeGreen = slotting; _wedge.color = slotting ? WedgeGreen : WedgeGold; }

            // the title: the row's name - in slotting "Place <name>" in green
            string title;
            Color tc;
            if (slotting) { title = UiActionCursor.Packed == 0 ? "Clear a button" : "Place " + UiActionCursor.Name; tc = PlaceGreen; }
            else { title = rows.NameOf(row); tc = UiArt.Gold; }
            if (title != _shownTitle || tc != _titleColor) { _shownTitle = title; _titleColor = tc; _title.text = title ?? ""; _title.color = tc; }
            if (_near != null && _near.Shown != rows.NameOf(row - 1)) { _near.Shown = rows.NameOf(row - 1); _near.Name.text = _near.Shown; }
            if (_far != null && _far.Shown != rows.NameOf(row + 1)) { _far.Shown = rows.NameOf(row + 1); _far.Name.text = _far.Shown; }

            string name = null, l2 = null, l3 = null;
            if (slotting)
            {
                if (highlighted >= 0)
                {
                    int slot = SlotAt(row, highlighted);
                    int old = ActionAt(p, row, highlighted);
                    name = WowActionSlots.BarName(slot) + " button " + WowActionSlots.ButtonOf(slot);
                    if (old == 0) l2 = UiActionCursor.Packed == 0 ? "Empty" : "Release to place here";
                    else l2 = (UiActionCursor.Packed == 0 ? "Clear " : "Replace ") + UiActions.NameOf(p, old);
                }
                else if (!preview && Bar) { name = "Move left or right to choose a button"; l2 = "Move up or down for the other bars"; }   // lead 2026-10-10
                else if (!preview) { name = "Twist to choose a button"; l2 = "Push or pull for the other bars"; }
            }
            else if (control != null)
            {
                if (highlighted >= 0)
                {
                    int code = ControlAt(control, highlighted);
                    if (code == -1) { name = "Empty"; l2 = "Release to cancel"; }
                    else { string tip = UiPetRows.Tip(s, code); name = FirstLine(tip) ?? control.Name; }
                }
            }
            else if (highlighted >= 0)
            {
                Seg seg = _segs[highlighted];
                UiActionView v = seg.View;
                if (!v.Has) { name = "Empty"; l2 = empty ? "Put spells here from the spellbook" : "Release to cancel"; }
                else if (v.Kind == ActionKind.Spell && v.Ability != null)
                {
                    AbilityView a = v.Ability;
                    name = string.IsNullOrEmpty(a.Rank) ? a.Name : a.Name + " (" + a.Rank + ")";
                    string cost = a.CostType != PowerType.None && a.Cost > 0 ? "<color=#" + ColorUtility.ToHtmlStringRGB(CostColor(a.CostType)) + ">" + a.Cost + " " + PowerName(a.CostType) + "</color>" : null;
                    string cast = a.CastTime > 0.01f ? a.CastTime.ToString("0.#") + " sec cast" : "Instant";
                    l2 = cost != null ? cost + "   " + cast : cast;
                    if (a.CooldownRemaining > 0.05f) l3 = "Cooldown " + (a.CooldownRemaining >= 60f ? Mathf.CeilToInt(a.CooldownRemaining / 60f) + " min" : a.CooldownRemaining.ToString("0.0") + " sec");
                    else if (!a.Usable && !string.IsNullOrEmpty(a.UnusableReason)) l3 = a.UnusableReason;
                }
                else if (v.Kind == ActionKind.Item)
                {
                    name = v.Name;
                    l2 = v.Equipped ? "Equipped" : v.Count > 0 ? (v.Count > 1 ? v.Count + " in your bags" : "Use") : null;
                    if (!v.Equipped && v.Count == 0) l3 = "None in your bags";
                }
                else if (v.Kind == ActionKind.Macro || v.Kind == ActionKind.EquipmentSet) { name = v.Name; l2 = "Runs in the World of Warcraft client only"; }
                else name = v.Name;
            }
            else if (Column && !preview) { name = empty ? "Empty row" : "Pull back to choose"; l2 = empty ? "Put spells here from the spellbook" : "Up or down for another bar"; }   // lead 2026-10-10 (ours)
            else if (empty && !preview) { name = "Empty row"; l2 = "Put spells here from the spellbook"; }   // ours
            bool any = name != null;
            if (_infoBack.enabled != any) _infoBack.enabled = any;
            if (name != _shownName || l2 != _shown2 || l3 != _shown3)
            {
                _shownName = name;
                _shown2 = l2;
                _shown3 = l3;
                _name.text = name ?? "";
                _line2.text = l2 ?? "";
                _line3.text = l3 ?? "";
            }
        }

        private static string FirstLine(string tip)
        {
            if (string.IsNullOrEmpty(tip)) return null;
            int n = tip.IndexOf('\n');
            string first = n >= 0 ? tip.Substring(0, n) : tip;
            return System.Text.RegularExpressions.Regex.Replace(first, "<[^>]*>", "");
        }

        /// <summary>Key labels per row: the main row 1-6 / 7-= (as the keys), the Bottom Left Bar A-1.. (ours), other rows the button numbers in grey.</summary>
        private void RefreshKeys(int row, bool control)
        {
            int sig = control ? 1000 + row : row;
            if (sig == _keysRow) return;
            _keysRow = sig;
            string[] keys;
            Color c = KeyWhite;
            if (control || row == 0) keys = Right ? KeysRight : KeysLeft;
            else if (row == 1) keys = Right ? AltRight : AltLeft;
            else { keys = Right ? NumRight : NumLeft; c = KeyGrey; }
            if (control) c = KeyGrey;
            for (int i = 0; i < Count; i++) { _segs[i].Key.text = keys[i]; _segs[i].Key.color = c; }
        }

        /// <summary>The depth ladder: one pip per row (top = the furthest push row), the current one lit (ours).</summary>
        private void RefreshLadder(VrRows rows, int row)
        {
            if (Column) return;   // lead 2026-10-10: the bow names its row instead
            int sig = rows.MaxRow * 100 + row + 50;
            if (sig == _pipSig) return;
            _pipSig = sig;
            int n = rows.MaxRow - WowActionSlots.MinRow + 1;
            for (int k = 0; k < MaxPips; k++)
            {
                bool on = k < n && WowSettings.VrRows;
                _pips[k].enabled = on;
                if (!on) continue;
                int r = rows.MaxRow - k;
                // rows +2..-2 at Cy - 120 + 48 k (the push rows up the ladder); a pet row +3 one step higher
                float y = Cy - 120f + 48f * (WowActionSlots.MaxRow - r) - 7f;
                float x = (Right ? W - 30f : 30f) - 7f;
                if (Bar)
                {
                    // lead 2026-10-10: beside the bar's outer tile, PipStep apart round the tiles' middle (up = the higher row, as the hand)
                    y = TileTop + TileH * 0.5f + PipStep * (WowActionSlots.MaxRow - r - 2) - 7f;
                    x = (Right ? BarW - 20f : 20f) - 7f;
                }
                UiKit.Move(_pips[k].rectTransform, x, y);
                bool lit = r == row;
                _pips[k].color = lit ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0.65f, 0.65f, 0.65f, 0.45f);
                UiKit.Size(_pips[k].rectTransform, lit ? 18f : 14f, lit ? 18f : 14f);
            }
        }

        private static void RefreshSeg(Seg s, PlayerState p, int packed, bool hi, bool placing)
        {
            UiActionView v = s.View;
            UiActions.Resolve(p, packed, v);
            if (packed != s.Shown) { s.Shown = packed; s.IconPath = null; s.IconLoaded = false; s.Icon.enabled = false; s.CostShown = -1; s.CountKind = -2; }
            string path = v.Has ? v.IconPath : null;
            if (path != s.IconPath) { s.IconPath = path; s.IconLoaded = false; s.Icon.enabled = false; }
            if (!s.IconLoaded && v.Has)
            {
                Texture2D t = UiArt.Icon(path);
                if (t != null) { s.Icon.texture = t; s.Icon.enabled = true; s.IconLoaded = true; s.Tint = -1; }
            }
            bool has = v.Has;
            if (s.Empty.enabled == has) s.Empty.enabled = !has;

            int tint = v.Tint;
            if (tint != s.Tint && s.IconLoaded)
            {
                s.Tint = tint;
                s.Icon.color = UiActions.TintColor(tint);
            }

            float frac = v.Cooldown;
            if (Mathf.Abs(frac - s.SweepShown) > 0.004f)
            {
                s.SweepShown = frac;
                s.Sweep.enabled = frac > 0f;
                s.Sweep.fillAmount = frac;
            }

            if (s.Checked.enabled != (has && v.Active)) s.Checked.enabled = has && v.Active;   // iteration 7: also the ridden mount
            if (s.Hilite.enabled != hi) s.Hilite.enabled = hi;
            Grow(s, hi);

            // slotting: the cursor's icon ghosted in the highlighted button
            string ghost = placing && UiActionCursor.Packed != 0 ? UiActionCursor.IconPath : null;
            if (ghost != s.GhostPath) { s.GhostPath = ghost; s.GhostLoaded = false; s.Ghost.enabled = false; }
            if (ghost != null && !s.GhostLoaded)
            {
                Texture2D t = UiArt.Icon(ghost);
                if (t != null) { s.Ghost.texture = t; s.Ghost.color = new Color(1f, 1f, 1f, 0.6f); s.Ghost.enabled = true; s.GhostLoaded = true; }
            }

            int kind, cost;
            if (v.Kind == ActionKind.Item) { kind = v.ShowCount ? 100 : -1; cost = v.ShowCount ? v.Count : 0; }
            else if (v.Ability != null && v.Ability.CostType != PowerType.None && v.Ability.Cost > 0) { kind = (int)v.Ability.CostType; cost = v.Ability.Cost; }
            else { kind = -1; cost = 0; }
            if (cost != s.CostShown || kind != s.CountKind)
            {
                s.CostShown = cost;
                s.CountKind = kind;
                if (kind == 100) { s.Cost.SetText("{0}", cost); s.Cost.color = Color.white; }
                else if (kind >= 0) { s.Cost.SetText("{0}", cost); s.Cost.color = CostColor((PowerType)kind); }
                else s.Cost.SetText("");
            }
        }

        /// <summary>A pet / vehicle row segment (the pets owner's UiPetRows: icon by code; -1 empty).</summary>
        private static void RefreshControlSeg(Seg s, IWowSession sess, int code, bool hi)
        {
            int key = code == -1 ? 0 : -1000000 - code;   // never a packed action value
            if (key != s.Shown) { s.Shown = key; s.IconPath = null; s.IconLoaded = false; s.Icon.enabled = false; s.CostShown = -1; s.CountKind = -2; s.Cost.SetText(""); }
            bool has = code != -1;
            string path = has ? UiPetRows.Icon(sess, code) : null;
            if (path != s.IconPath) { s.IconPath = path; s.IconLoaded = false; s.Icon.enabled = false; }
            if (!s.IconLoaded && has)
            {
                Texture2D t = UiArt.Icon(path);
                if (t != null) { s.Icon.texture = t; s.Icon.color = Color.white; s.Icon.enabled = true; s.IconLoaded = true; }
            }
            if (s.Empty.enabled == has) s.Empty.enabled = !has;
            if (s.Sweep.enabled) { s.Sweep.enabled = false; s.SweepShown = 0f; }
            if (s.Checked.enabled) s.Checked.enabled = false;
            if (s.Ghost.enabled) { s.Ghost.enabled = false; s.GhostPath = null; }
            if (s.Hilite.enabled != hi) s.Hilite.enabled = hi;
            Grow(s, hi);
        }

        /// <summary>The highlighted button larger (ring x1.2, Gear Select tile x1.15 with a bright border; lead 2026-10-10).</summary>
        private static void Grow(Seg s, bool hi)
        {
            if (s.Big == hi) return;
            s.Big = hi;
            s.Rt.localScale = Vector3.one * (hi ? s.HiScale : 1f);
            if (s.Back != null) s.Back.SetEdgeColor(hi ? EdgeBright : EdgeGold);
        }

        /// <summary>The spellbook entry of a spell id, else its Player.Mounts entry (iteration 7: mounts on the action bar are on the
        /// radial menus too), Companions, Recipes (UiActions.Ability); null when none has it.</summary>
        public static AbilityView Find(PlayerState p, int spellId)
        {
            return UiActions.Ability(p, spellId);
        }

        private static Color CostColor(PowerType t)
        {
            switch (t)
            {
                case PowerType.Mana: return new Color(0.45f, 0.65f, 1f);
                case PowerType.Rage: return new Color(1f, 0.35f, 0.35f);
                case PowerType.Energy: return new Color(1f, 1f, 0.3f);
                case PowerType.Focus: return new Color(1f, 0.6f, 0.35f);
                default: return Color.white;
            }
        }

        private static string PowerName(PowerType t)
        {
            switch (t)
            {
                case PowerType.Mana: return "Mana";
                case PowerType.Rage: return "Rage";
                case PowerType.Energy: return "Energy";
                case PowerType.Focus: return "Focus";
                case PowerType.RunicPower: return "Runic Power";
                default: return "";
            }
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
            if (_near != null && _near.Go != null) Object.Destroy(_near.Go);
            if (_far != null && _far.Go != null) Object.Destroy(_far.Go);
        }
    }
}
