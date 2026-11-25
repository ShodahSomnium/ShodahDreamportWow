using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// One canvas of the game UI with the buttons built on it, its tooltip and (world canvases) the pointer dot.
    /// World canvases (VR) carry NO raycasters: UiPointer meets the ray with the canvas rectangle itself (AomUiPanel.PointAt: the
    /// client's raycaster does not reach a small panel held close behind the arm), so the client's own pointer can never press
    /// the same button a second time. The desktop overlay has a GraphicRaycaster for the client's mouse.
    /// </summary>
    public sealed class UiSurface
    {
        public string Name;
        public Canvas Canvas;
        public RectTransform Root;
        /// <summary>Top-left anchored container: (0, 0) is the canvas's top-left, y down (UiKit coordinates).</summary>
        public RectTransform Content;
        public bool IsWorld, IsArm;
        public float Width, Height;
        public readonly List<UiButton> Buttons = new List<UiButton>();
        /// <summary>Lead (2026-10-04): panels hanging outside this surface's rect that the laser may still hit (the Armour page's
        /// fitting view beside the VR lid); their buttons are in Buttons like any other.</summary>
        public readonly List<RectTransform> HitAreas = new List<RectTransform>();
        public UiTooltip Tooltip;
        public RawImage Dot;
        /// <summary>13.7: a window standing in the world that <see cref="UiCarry"/> moves with the player's rig (set by <see cref="UiCarry.Add"/>).</summary>
        public bool Carried;

        private UiButton _tipFor;
        private float _tipRefresh;

        public static UiSurface MakeWorld(string name, Transform parent, float w, float h, float metresPerUnit, bool arm, int sortingOrder)
        {
            var s = new UiSurface { Name = name, IsWorld = true, IsArm = arm, Width = w, Height = h };
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiKit.Layer;
            go.transform.SetParent(parent, false);
            s.Canvas = go.AddComponent<Canvas>();
            s.Canvas.renderMode = RenderMode.WorldSpace;
            s.Canvas.overrideSorting = true;
            s.Canvas.sortingOrder = sortingOrder;
            UiSharp.Register(go);   // lead 2026-10-10: after DLSS while Settings "Sharp VR interface" is on
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            s.Root = (RectTransform)go.transform;
            s.Root.pivot = new Vector2(0.5f, 0.5f);
            s.Root.sizeDelta = new Vector2(w, h);
            s.Root.localScale = Vector3.one * metresPerUnit;
            s.Content = UiKit.Rect(s.Root, "Content", 0f, 0f, w, h);
            return s;
        }

        public static UiSurface MakeScreen(string name, Transform parent, int sortingOrder)
        {
            var s = new UiSurface { Name = name, IsWorld = false, IsArm = false, Width = 1024f, Height = 768f };
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiKit.Layer;
            go.transform.SetParent(parent, false);
            s.Canvas = go.AddComponent<Canvas>();
            s.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s.Canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            // WoW's UIParent is 768 units tall at UI scale 1; widths follow the screen's aspect.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1024f, 768f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            s.Root = (RectTransform)go.transform;
            s.Content = s.Root;
            return s;
        }

        /// <summary>A container anchored to a point of the screen (anchor/pivot in 0..1, offset in units, y up as Unity).</summary>
        public RectTransform Anchor(string name, Vector2 anchor, float w, float h, float offsetX, float offsetY)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiKit.Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(Content, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = anchor;
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(offsetX, offsetY);
            return r;
        }

        public void Add(UiButton b)
        {
            b.Surface = this;
            Buttons.Add(b);
        }

        public bool Shown { get { return Canvas != null && Canvas.gameObject.activeSelf; } }

        public void SetShown(bool on)
        {
            if (Canvas != null && Canvas.gameObject.activeSelf != on)
            {
                Canvas.gameObject.SetActive(on);
                if (!on) ClearHover();
            }
        }

        public void ClearHover()
        {
            for (int i = 0; i < Buttons.Count; i++) if (Buttons[i] != null && Buttons[i].Hovered) Buttons[i].SetHover(false);
            if (Tooltip != null) Tooltip.Hide();
            _tipFor = null;
            if (Dot != null) Dot.enabled = false;
        }

        /// <summary>Builds the tooltip and the pointer dot last, so they draw over everything else on this canvas.</summary>
        public void Finish()
        {
            bool over = UiKit.Over;
            UiKit.Over = IsArm;
            Tooltip = UiTooltip.Make(Content);
            if (IsWorld)
            {
                Dot = UiKit.Pic(Content, "Pointer dot", UiArt.Add(UiArt.CastSpark), 0f, 0f, 14f, 14f);
                // Stage S1 (laser request 2, integrator): white like the end of the client's own laser (Near-Far Interactor Right.prefab
                // m_HoverHitProperties key2 = (1,1,1,1)) - it was yellow (1, 0.9, 0.4), the user's "separate yellow dot". With UiPointer
                // on the client's laser it sits on the laser's line; it stays the only pointer mark on our panels (the client draws no
                // line there: they have no TrackedDeviceGraphicRaycaster).
                Dot.color = new Color(1f, 1f, 1f, 0.9f);
                Dot.enabled = false;
            }
            UiKit.Over = over;
        }

        // ---------------------------------------------------------------- pointing (VR)

        /// <summary>The ray meets this canvas (its front side): where, how far, and the button under that point (snapped to the nearest within 8 units).</summary>
        public bool RayHit(Ray ray, out float distance, out Vector3 point, out UiButton button)
        {
            distance = 0f;
            point = Vector3.zero;
            button = null;
            if (!Shown) return false;
            Vector3 n = Root.forward;
            float facing = Vector3.Dot(ray.direction, n);
            if (facing <= 1e-4f) return false;   // from behind or edge-on (a canvas is read looking along its forward)
            var plane = new Plane(n, Root.position);
            float d;
            if (!plane.Raycast(ray, out d) || d <= 0f) return false;
            point = ray.GetPoint(d);
            Vector3 local = Root.InverseTransformPoint(point);
            UnityEngine.Rect r = Root.rect;
            if (local.x < r.xMin || local.x > r.xMax || local.y < r.yMin || local.y > r.yMax)
            {
                // Tooltips and banners may hang outside the rect; they are not pressable, so nothing to do - unless a registered
                // hit area (lead 2026-10-04) is there.
                if (!InHitArea(point)) return false;
            }
            distance = d;
            button = ButtonAt(point, 8f);
            return true;
        }

        /// <summary>A fingertip / hand position within maxDepth metres in front of the canvas, over a button.</summary>
        public bool PokeHit(Vector3 p, float maxDepth, out UiButton button, out float depth)
        {
            button = null;
            depth = 0f;
            if (!Shown) return false;
            Vector3 n = Root.forward;
            depth = Vector3.Dot(p - Root.position, -n);   // > 0 in front of the face
            if (depth < -0.015f || depth > maxDepth) return false;
            button = ButtonAt(p - n * Vector3.Dot(p - Root.position, n), 0f);
            return button != null;
        }

        private bool InHitArea(Vector3 worldPoint)
        {
            for (int i = 0; i < HitAreas.Count; i++)
            {
                RectTransform a = HitAreas[i];
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                Vector3 lp = a.InverseTransformPoint(worldPoint);
                if (a.rect.Contains(new Vector2(lp.x, lp.y))) return true;
            }
            return false;
        }

        private UiButton ButtonAt(Vector3 worldPoint, float snap)
        {
            UiButton best = null, inside = null;
            float bestD = snap, insideArea = float.MaxValue;
            for (int i = 0; i < Buttons.Count; i++)
            {
                UiButton b = Buttons[i];
                if (b == null || !b.isActiveAndEnabled || !b.Interactable && b.Tip == null) continue;
                if (Clipped(b, worldPoint)) continue;   // lead fix 2026-10-04
                Vector3 lp = b.Rt.InverseTransformPoint(worldPoint);
                UnityEngine.Rect r = b.Rt.rect;
                if (r.Contains(new Vector2(lp.x, lp.y)))
                {
                    // lead fix 2026-10-10 (the user: "Mailbox - can't click anything to accept received item. UI seems to be above Laser"):
                    // the FIRST button containing the point won, so a big button built earlier (a mail row, a window blocker) took the ray
                    // over the small one inside it (an item icon). Now the smallest containing button wins (the most specific).
                    float area = r.width * r.height;
                    if (inside == null || area < insideArea) { inside = b; insideArea = area; }
                    continue;
                }
                if (inside != null) continue;
                if (snap > 0f)
                {
                    float dx = Mathf.Max(r.xMin - lp.x, 0f, lp.x - r.xMax), dy = Mathf.Max(r.yMin - lp.y, 0f, lp.y - r.yMax);
                    float dd = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dd < bestD) { bestD = dd; best = b; }
                }
            }
            return inside ?? best;
        }

        /// <summary>
        /// Lead fix 2026-10-04 (the user: "let people accept quests without needing to scroll through it all"): the point lies outside one of
        /// the button's RectMask2D parents - the button is clipped away there (drawn nowhere), so the VR ray / poke must not find it. The
        /// quest dialog's reward and title buttons (built before Accept, in its scrolled "Content" RectMask2D) lay invisible over the Accept
        /// button below the text until the text was scrolled to its end, and the ray took them first. Desktop clicks go through Unity's
        /// GraphicRaycaster, which already asks RectMask2D (ICanvasRaycastFilter). The masks of each button are looked up once (cached).
        /// </summary>
        private bool Clipped(UiButton b, Vector3 worldPoint)
        {
            RectMask2D[] masks;
            if (!_masks.TryGetValue(b, out masks))
            {
                masks = b.GetComponentsInParent<RectMask2D>(true);
                _masks[b] = masks;
            }
            for (int i = 0; i < masks.Length; i++)
            {
                RectMask2D m = masks[i];
                if (m == null || !m.isActiveAndEnabled) continue;
                RectTransform mt = m.rectTransform;
                Vector3 lp = mt.InverseTransformPoint(worldPoint);
                if (!mt.rect.Contains(new Vector2(lp.x, lp.y))) return true;
            }
            return false;
        }

        private readonly Dictionary<UiButton, RectMask2D[]> _masks = new Dictionary<UiButton, RectMask2D[]>();

        // ---------------------------------------------------------------- iteration 11 (uicore, proposal-uicore.md 5.2.4 / 5.3 / 5.4.3)

        /// <summary>A world surface takes a new size in canvas units (the lid / dialog / popup surfaces fit the window shown on them);
        /// Content stays anchored at the top-left. The screen surface ignores it (its size is the screen's).</summary>
        public void Resize(float w, float h)
        {
            if (!IsWorld || Root == null) return;
            Width = w;
            Height = h;
            Root.sizeDelta = new Vector2(w, h);
            if (Content != null && Content != Root) Content.sizeDelta = new Vector2(w, h);
        }

        /// <summary>VR drop target under a world point: the first shown registered button with DropAccepts whose rect contains the point and
        /// that accepts the payload on the cursor (Interactable ignored: a full slot still takes a swap); else the innermost accepting
        /// window Blocker (its rect also contains the point); null = none.</summary>
        public UiButton DropTargetAt(Vector3 worldPoint)
        {
            UiPickupPayload p = UiPickup.Current;
            if (p == null) return null;
            UiButton blocker = null;
            float blockerArea = float.MaxValue;
            for (int i = 0; i < Buttons.Count; i++)
            {
                UiButton b = Buttons[i];
                if (b == null || !b.isActiveAndEnabled || b.DropAccepts == null || b.Rt == null) continue;
                if (Clipped(b, worldPoint)) continue;   // lead fix 2026-10-04
                Vector3 lp = b.Rt.InverseTransformPoint(worldPoint);
                UnityEngine.Rect r = b.Rt.rect;
                if (!r.Contains(new Vector2(lp.x, lp.y))) continue;
                if (!UiPickup.Accepts(b, p)) continue;
                if (b.IsBlocker)
                {
                    float area = r.width * r.height;
                    if (area < blockerArea) { blockerArea = area; blocker = b; }
                    continue;
                }
                return b;
            }
            return blocker;
        }

        /// <summary>Moves the tooltip and the pointer dot back on top after buttons were built later (registered windows, popups).</summary>
        public void KeepOnTop()
        {
            if (Tooltip != null && Tooltip.RootRect != null) Tooltip.RootRect.SetAsLastSibling();
            if (Dot != null) Dot.rectTransform.SetAsLastSibling();
        }

        /// <summary>Drops destroyed buttons from the list (popups come and go).</summary>
        public void Prune()
        {
            for (int i = Buttons.Count - 1; i >= 0; i--) if (Buttons[i] == null) Buttons.RemoveAt(i);
            if (_masks.Count > 0)
            {
                // lead fix 2026-10-04: the clip cache forgets destroyed buttons too
                _gone.Clear();
                foreach (KeyValuePair<UiButton, RectMask2D[]> kv in _masks) if (kv.Key == null) _gone.Add(kv.Key);
                for (int i = 0; i < _gone.Count; i++) _masks.Remove(_gone[i]);
                _gone.Clear();
            }
        }

        private readonly List<UiButton> _gone = new List<UiButton>();

        public void ShowDot(bool on, Vector3 worldPoint)
        {
            if (Dot == null) return;
            if (!on) { if (Dot.enabled) Dot.enabled = false; return; }
            Vector3 local = Content.InverseTransformPoint(worldPoint);
            UnityEngine.Rect cr = Content.rect;
            Dot.rectTransform.anchoredPosition = new Vector2(local.x - cr.xMin - 7f, local.y - cr.yMax + 7f);
            if (!Dot.enabled) Dot.enabled = true;
        }

        // ---------------------------------------------------------------- tooltip

        public void OnHover(UiButton b, bool on)
        {
            if (Tooltip == null) return;
            if (on)
            {
                _tipFor = b;
                _tipRefresh = 0f;
                ShowTip();
            }
            else if (_tipFor == b)
            {
                _tipFor = null;
                Tooltip.Hide();
            }
        }

        /// <summary>Once a frame: re-asks the hovered button's tooltip twice a second (usable state and reasons change).</summary>
        public void Tick()
        {
            if (_tipFor == null) return;
            if (!_tipFor.isActiveAndEnabled) { _tipFor.SetHover(false); _tipFor = null; Tooltip.Hide(); return; }
            _tipRefresh += Time.unscaledDeltaTime;
            if (_tipRefresh > 0.5f) { _tipRefresh = 0f; ShowTip(); }
        }

        private void ShowTip()
        {
            string text = null;
            try { text = _tipFor.Tip != null ? _tipFor.Tip() : null; }
            catch (System.Exception e) { WowLog.Warn("UI: tooltip of '" + _tipFor.name + "' failed: " + e.Message); }
            if (string.IsNullOrEmpty(text)) { Tooltip.Hide(); return; }
            // The button's corners in Content coordinates (x right, y up from the top-left).
            UnityEngine.Rect r = _tipFor.Rt.rect;
            Vector3 tl = Content.InverseTransformPoint(_tipFor.Rt.TransformPoint(new Vector3(r.xMin, r.yMax, 0f)));
            Vector3 br = Content.InverseTransformPoint(_tipFor.Rt.TransformPoint(new Vector3(r.xMax, r.yMin, 0f)));
            float cw = IsWorld ? Width : Root.rect.width, ch = IsWorld ? Height : Root.rect.height;
            // Relative to the container's top-left (the screen canvas's pivot is its centre, a world Content's its top-left).
            UnityEngine.Rect cr = Content.rect;
            Tooltip.Show(text, tl.x - cr.xMin, cr.yMax - tl.y, br.x - cr.xMin, cr.yMax - br.y, cw, ch, IsWorld, IsArm);
        }
    }

    /// <summary>GameTooltip: UI-Tooltip-Background tinted (0.09, 0.09, 0.19), UI-Tooltip-Border edge 16, insets 5 (GameTooltipTemplate.xml:4-12).</summary>
    public sealed class UiTooltip
    {
        public const float Width = 260f;
        private RectTransform _root;
        /// <summary>Iteration 11 (uicore): the tooltip's root (UiSurface.KeepOnTop puts it back on top).</summary>
        public RectTransform RootRect { get { return _root; } }
        private UiBackdrop _back;
        private TextMeshProUGUI _text;
        private string _shown;

        public static UiTooltip Make(Transform parent)
        {
            var t = new UiTooltip();
            t._root = UiKit.Rect(parent, "Tooltip", 0f, 0f, Width, 40f);
            t._back = UiBackdrop.Make(t._root, "Backdrop", UiArt.TooltipBackground, UiArt.TooltipBorder, 16f, 5f, 16f, UiArt.TooltipBg);
            t._text = UiKit.Text(t._root, "Text", "", 12f, Color.white, TextAlignmentOptions.TopLeft, 10f, 9f, Width - 20f, 30f);
            t._text.overflowMode = TextOverflowModes.Overflow;
            t._root.gameObject.SetActive(false);
            return t;
        }

        public void Hide()
        {
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            _shown = null;
        }

        /// <summary>Shows text next to a button (its corners in canvas units from the top-left): above it on a world panel, beside it on the screen.</summary>
        public void Show(string text, float left, float top, float right, float bottom, float canvasW, float canvasH, bool world, bool arm)
        {
            if (!string.Equals(text, _shown))
            {
                _shown = text;
                _text.text = text;
            }
            Vector2 pref = _text.GetPreferredValues(text, Width - 20f, 0f);
            float h = Mathf.Ceil(pref.y) + 18f;
            UiKit.Size(_text.rectTransform, Width - 20f, h - 18f);
            _back.Place(0f, 0f, Width, h);
            UiKit.Size(_root, Width, h);
            float x, y;
            if (world)
            {
                // Above the panel (clear of its frames; on the arm also clear of the message lines).
                x = Mathf.Clamp(left, -40f, Mathf.Max(-40f, canvasW - Width + 40f));
                y = -h - (arm ? 62f : 6f);
            }
            else
            {
                x = right + 8f;
                if (x + Width > canvasW) x = left - Width - 8f;
                y = top - h - 4f;
                if (y < 0f) y = bottom + 4f;
                if (y + h > canvasH) y = canvasH - h;
                if (x < 0f) x = 0f;
            }
            UiKit.Move(_root, x, y);
            _root.SetAsLastSibling();
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        private static readonly StringBuilder Sb = new StringBuilder(512);

        /// <summary>An ability's tooltip: the session's ready text (name, cost/range, cast/cooldown, description) coloured like GameTooltip, plus why it cannot be used now.</summary>
        public static string Ability(AbilityView v, string extra)
        {
            if (v == null) return null;
            Sb.Length = 0;
            string src = v.Tooltip ?? v.Name ?? "";
            string[] lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0) Sb.Append("<size=14><color=#ffffff>").Append(lines[0]).Append("</color></size>");
                else if (i <= 2) { if (lines[i].Length > 0) Sb.Append('\n').Append("<color=#ffffff>").Append(lines[i]).Append("</color>"); }
                else Sb.Append('\n').Append("<color=#ffd100>").Append(lines[i]).Append("</color>");
            }
            if (v.Passive) Sb.Append("\n<color=#ffffff>Passive</color>");
            else if (!v.Usable && !string.IsNullOrEmpty(v.UnusableReason) && v.UnusableReason != "Spell is not ready yet")
                Sb.Append("\n<color=#ff2020>").Append(v.UnusableReason).Append("</color>");
            else if (v.CooldownRemaining > 1.6f)
                Sb.Append("\n<color=#ffffff>Cooldown remaining: ").Append(Mathf.CeilToInt(v.CooldownRemaining)).Append(" sec</color>");
            if (!string.IsNullOrEmpty(extra)) Sb.Append('\n').Append(extra);
            return Sb.ToString();
        }

        /// <summary>An item's tooltip: its name in the quality colour, then the session's text.</summary>
        public static string Item(ItemView it, string extra)
        {
            if (it == null) return null;
            Sb.Length = 0;
            string src = it.Tooltip;
            if (string.IsNullOrEmpty(src)) src = it.Name ?? "";
            string[] lines = src.Split('\n');
            Sb.Append("<size=14><color=").Append(UiArt.QualityHex(it.Quality)).Append('>').Append(lines[0]).Append("</color></size>");
            for (int i = 1; i < lines.Length; i++) Sb.Append('\n').Append(lines[i]);
            if (it.Count > 1) Sb.Append("\nStack: ").Append(it.Count);
            if (!string.IsNullOrEmpty(extra)) Sb.Append('\n').Append(extra);
            return Sb.ToString();
        }
    }

    /// <summary>
    /// 13.7 (the user: "have all the menus be usable when moving, currently they spawn and then move with the world ... such as when on a
    /// flight path, no one can use any menus apart from the wrist menu"). The VR windows that stand in the world (dialog, popups, the free
    /// lid, chat, map, flight map, group / instance / dungeon finder popups) were placed once in front of the head and stayed there. Now each
    /// frame they move with the player's rig (WowPlayer.Root): its movement always, its turn only while a flight, mount or ship steers the
    /// avatar (WowOrigin.AvatarDriven), so a snap turn on foot still lets the player turn to a window beside them. Head motion never moves
    /// them. A rebase is left out (the owners' Rebased handlers already move their windows with the world).
    /// </summary>
    public static class UiCarry
    {
        private static readonly List<UiSurface> _all = new List<UiSurface>();
        private static Transform _root;
        private static Vector3 _pos;
        private static float _yaw;
        private static bool _have, _hooked;

        public static void Add(UiSurface s)
        {
            if (s == null) return;
            s.Carried = true;
            if (!_all.Contains(s)) _all.Add(s);
        }

        /// <summary>UiVrLayout.Place (LateUpdate and before rendering): the shown carried windows follow the rig's change since the last call.</summary>
        public static void Step()
        {
            if (!_hooked) { _hooked = true; WowSpace.Rebased += d => { if (_have) _pos -= d; }; }
            Transform r = WowPlayer.Root;
            if (r == null) { _have = false; return; }
            Vector3 p = r.position;
            float yaw = r.eulerAngles.y;
            if (!_have || r != _root) { _root = r; _pos = p; _yaw = yaw; _have = true; return; }
            Vector3 from = _pos;
            float turn = WowOrigin.AvatarDriven ? Mathf.DeltaAngle(_yaw, yaw) : 0f;
            _pos = p;
            _yaw = yaw;
            if ((p - from).sqrMagnitude < 1e-8f && Mathf.Abs(turn) < 0.01f) return;
            Quaternion q = Quaternion.Euler(0f, turn, 0f);
            for (int i = 0; i < _all.Count; i++)
            {
                UiSurface s = _all[i];
                if (s == null || !s.Carried || !s.Shown || s.Root == null) continue;
                Transform t = s.Root;
                t.SetPositionAndRotation(p + q * (t.position - from), q * t.rotation);
            }
        }
    }
}
