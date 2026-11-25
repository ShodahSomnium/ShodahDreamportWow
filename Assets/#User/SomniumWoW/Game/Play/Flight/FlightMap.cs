using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Flight
{
    /// <summary>
    /// The flight map (FrameXML TaxiFrame, patch-enUS: TaxiFrame.xml / TaxiFrame.lua): a 384 x 512 frame of UI-TaxiFrame-TopLeft /
    /// TopRight / BotLeft / BotRight, the flight master's name (TaxiMerchant, GameFontNormal, TOP +5 -17), the continent's taxi
    /// picture ("TaxiMap" 316 x 352 at TOP -13 -75), a 16 x 16 button per known node (UI-Taxi-Icon-Green = here, -White = reachable,
    /// -Yellow = known but no route; unknown nodes are not drawn), UI-Taxi-Icon-Highlight 32 x 32 on hover, the route as UI-Taxi-Line
    /// strips (32 wide, texcoord factor 32/30) - one-hop lines from here while nothing is hovered, the hovered node's hops otherwise -,
    /// a tooltip with the name and the cost (or "You are here"), the panel close button (TOPRIGHT -29 -8).
    /// Desktop: a screen panel where WoW puts its left panels (TOPLEFT 0 -104). VR: a world panel between the player and the flight
    /// master, pointed at with the right-hand ray / poked (the UI module's UiPointer), trigger = click.
    /// </summary>
    public sealed class FlightMap
    {
        private const string Tex = "Interface\\TaxiFrame\\";
        private const float MapX = 21f, MapY = 75f, MapW = 316f, MapH = 352f; // 384/2 - 13 - 316/2 = 21
        private const float LineFactor = 32f / 30f;                            // TAXIROUTE_LINEFACTOR

        public Action<TaxiNodeView> Fly;
        /// <summary>The panel's close button was clicked (WowFlight: IWowSession.CloseTaxi + hide).</summary>
        public Action CloseClicked;

        // ---- iteration 11 (motion, uicore H17): the registry's Taxi External spec (UiTaxiSpec) polls / closes the map through these.
        /// <summary>The flight map is on screen / in the world (WowFlight.MapOpen).</summary>
        public static bool IsOpen { get { return WowFlight.MapOpen; } }
        /// <summary>Closes the open flight map as its close button does (IWowSession.CloseTaxi + hide). No-op when closed.</summary>
        public static void Close()
        {
            WowFlight f = WowFlight.Instance;
            if (f != null && WowFlight.MapOpen) f.CloseMap("the UI closed it");
        }

        private UiSurface _surface;
        private bool _vr;
        private RectTransform _panel, _lineLayer, _nodeLayer;
        private RawImage _mapPic, _portrait;
        private TextMeshProUGUI _title;
        private readonly List<UiButton> _buttons = new List<UiButton>();
        private readonly List<TaxiNodeView> _views = new List<TaxiNodeView>();
        private readonly List<Vector2> _points = new List<Vector2>();
        private readonly List<RawImage> _lines = new List<RawImage>();
        private readonly List<UiSurface> _surfaces = new List<UiSurface>();
        private UiPointer _pointer;
        private TaxiView _taxi;
        private FlightData _data;
        private int _displayMap = int.MinValue;
        private UiButton _hovered;
        private bool _linesDirty;

        public bool Shown { get { return _surface != null && _surface.Shown; } }
        public bool PointerOn { get { return _pointer != null && _vr && Shown && _pointer.OnUi; } }
        public TaxiView Taxi { get { return _taxi; } }
        /// <summary>The panel's canvas (a world panel in VR: callers may re-place it), or null before the first Show.</summary>
        public Canvas PanelCanvas { get { return _surface != null ? _surface.Canvas : null; } }

        /// <summary>Opens (or refreshes) the map for this taxi view. masterPos: the flight master in Unity (VR placement), or null.</summary>
        public void Show(TaxiView taxi, FlightData data, bool vr, string masterName, Vector3? masterPos, WowFaction faction)
        {
            if (_surface != null && _vr != vr) Destroy();
            _vr = vr;
            _data = data;
            if (_surface == null) Build();
            _taxi = taxi;
            _title.text = string.IsNullOrEmpty(masterName) ? "Flight Master" : masterName;
            Texture2D icon = UiArt.Tex(faction == WowFaction.Horde ? "Interface\\Icons\\Ability_Mount_Wyvern_01.blp" : "Interface\\Icons\\Ability_Mount_Gryphon_01.blp");
            _portrait.texture = icon != null ? icon : UiArt.Tex("Interface\\Minimap\\Tracking\\FlightMaster.blp");
            _portrait.color = _portrait.texture != null ? Color.white : Color.black;
            Fill();
            _surface.SetShown(true);
            if (_vr) Place(masterPos);
            UiSound.Named("igMainMenuOpen");
        }

        public void Hide()
        {
            if (_surface == null || !_surface.Shown) return;
            _surface.SetShown(false);
            if (_pointer != null) _pointer.Clear();
            _taxi = null;
            UiSound.Named("igMainMenuClose");
        }

        /// <summary>Once a frame while shown: the VR pointer, the tooltip, the route lines of the hovered node.</summary>
        public void Tick()
        {
            if (!Shown) return;
            if (_vr)
            {
                if (_pointer == null) _pointer = new UiPointer();
                try { _pointer.Update(_surfaces); }
                catch (Exception e) { WowLog.Warn("Flight map: pointer: " + e.Message); _pointer = null; }
            }
            _surface.Tick();
            UiButton hovered = null;
            for (int i = 0; i < _buttons.Count; i++) if (_buttons[i].gameObject.activeSelf && _buttons[i].Hovered) { hovered = _buttons[i]; break; }
            if (hovered != _hovered || _linesDirty) { _hovered = hovered; _linesDirty = false; DrawLines(); }
        }

        public void Destroy()
        {
            if (_surface != null && _surface.Canvas != null) UnityEngine.Object.Destroy(_surface.Canvas.gameObject);
            _surface = null;
            _buttons.Clear();
            _views.Clear();
            _points.Clear();
            _lines.Clear();
            _surfaces.Clear();
            _pointer = null;
            _displayMap = int.MinValue;
        }

        // ------------------------------------------------------------------------------------------------ building

        private void Build()
        {
            bool over = UiKit.Over, pointable = UiKit.Pointable;
            UiSurface surface = UiKit.Surface;
            try
            {
                UiKit.Over = false;
                UiKit.Pointable = !_vr;
                if (_vr)
                {
                    // The quest dialog's scale (UiLayouts.DialogMetresPerUnit 0.0012): 384 x 512 units = 0.46 x 0.61 m.
                    _surface = UiSurface.MakeWorld("WoW Flight Map", null, 384f, 512f, 0.0012f, false, 102);
                    SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_surface);   // 13.7: moves with the player (UiCarry)
                    _panel = _surface.Content;
                }
                else
                {
                    _surface = UiSurface.MakeScreen("WoW Flight Map", null, 40);
                    _panel = _surface.Anchor("TaxiFrame", new Vector2(0f, 1f), 384f, 512f, 0f, -104f);
                }
                UiKit.Surface = _surface;
                _surfaces.Add(_surface);

                _portrait = UiKit.Solid(_panel, "TaxiPortrait", Color.black, 8f, 9f, 58f, 58f);
                UiKit.Frame4(_panel, "Frame", Tex + "UI-TaxiFrame-TopLeft.blp", Tex + "UI-TaxiFrame-TopRight.blp", Tex + "UI-TaxiFrame-BotLeft.blp", Tex + "UI-TaxiFrame-BotRight.blp");
                _title = UiKit.Text(_panel, "TaxiMerchant", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 72f, 17f, 250f, 14f);
                _mapPic = UiKit.Pic(_panel, "TaxiMap", null, MapX, MapY, MapW, MapH);
                _lineLayer = UiKit.Rect(_panel, "TaxiRouteMap", MapX, MapY, MapW, MapH);
                _nodeLayer = UiKit.Rect(_panel, "TaxiNodes", MapX, MapY, MapW, MapH);
                UiKit.CloseButton(_panel, 384f - 29f - 32f, 8f, () => { if (CloseClicked != null) CloseClicked(); });
                _surface.Finish();
                _surface.SetShown(false);
                WowLog.Info("Flight map: built (" + (_vr ? "VR world panel" : "desktop screen panel") + ").");
            }
            finally
            {
                UiKit.Over = over;
                UiKit.Pointable = pointable;
                UiKit.Surface = surface;
            }
        }

        private void Fill()
        {
            _views.Clear();
            _points.Clear();
            TaxiNodeView current = null;
            if (_taxi != null)
                for (int i = 0; i < _taxi.Nodes.Count; i++) if (_taxi.Nodes[i] != null && (_taxi.Nodes[i].Current || _taxi.Nodes[i].Id == _taxi.CurrentNodeId)) { current = _taxi.Nodes[i]; break; }

            // The continent picture: where the current node is drawn (map 530's Quel'Thalas / Azuremyst -> 0 / 1).
            float cx, cy;
            int display = current != null ? _data.DisplayMap(current.MapId, current.X, current.Y, out cx, out cy) : (_taxi != null ? _taxi.MapId : 0);
            if (display != _displayMap)
            {
                _displayMap = display;
                _mapPic.texture = UiArt.Tex(FlightData.TaxiMapTexture(display));
                _mapPic.color = _mapPic.texture != null ? Color.white : new Color(0f, 0f, 0f, 0f);
                if (_mapPic.texture == null) WowLog.Warn("Flight map: no taxi picture " + FlightData.TaxiMapTexture(display));
            }

            bool alt = UiKit.Over, pointable = UiKit.Pointable;
            UiSurface surface = UiKit.Surface;
            UiKit.Over = false;
            UiKit.Pointable = !_vr;
            UiKit.Surface = _surface;
            int shown = 0, hidden = 0;
            try
            {
                int n = 0;
                if (_taxi != null)
                {
                    for (int i = 0; i < _taxi.Nodes.Count; i++)
                    {
                        TaxiNodeView v = _taxi.Nodes[i];
                        bool isCurrent = v != null && (v.Current || v.Id == _taxi.CurrentNodeId);
                        if (v == null || (!v.Known && !isCurrent)) { hidden++; continue; }
                        float dx, dy, u, w;
                        int m = _data.DisplayMap(v.MapId, v.X, v.Y, out dx, out dy);
                        if (m != display || !_data.MapUv(m, dx, dy, out u, out w) || u < 0f || u > 1f || w < 0f || w > 1f) { hidden++; continue; }
                        UiButton b = n < _buttons.Count ? _buttons[n] : NewButton(n);
                        n++;
                        var p = new Vector2(u * MapW, w * MapH);
                        UiKit.Move(b.Rt, p.x - 8f, p.y - 8f);
                        string icon = isCurrent ? "UI-Taxi-Icon-Green.blp" : v.Reachable ? "UI-Taxi-Icon-White.blp" : "UI-Taxi-Icon-Yellow.blp";
                        b.Art.texture = UiArt.Tex(Tex + icon);
                        b.Art.color = Color.white;
                        TaxiNodeView view = v;
                        bool here = isCurrent;
                        b.Tip = () => Tooltip(view, here);
                        b.Click = () => { if (!here && view.Reachable && Fly != null) Fly(view); };
                        b.gameObject.SetActive(true);
                        _views.Add(v);
                        _points.Add(p);
                        shown++;
                    }
                }
                for (int i = n; i < _buttons.Count; i++) _buttons[i].gameObject.SetActive(false);
            }
            finally
            {
                UiKit.Over = alt;
                UiKit.Pointable = pointable;
                UiKit.Surface = surface;
            }
            _linesDirty = true;
            WowLog.Info("Flight map: " + (_taxi != null ? _taxi.Nodes.Count : 0) + " nodes from the session, " + shown + " drawn on " + FlightData.TaxiMapTexture(display) +
                        ", " + hidden + " hidden (unknown or on another map); here = " + (current != null ? current.Name + " (" + current.Id + ")" : "none") + ".");
        }

        private UiButton NewButton(int n)
        {
            UiButton b = UiKit.Button(_nodeLayer, "TaxiButton" + (n + 1), 0f, 0f, 16f, 16f);
            b.Art = UiKit.Pic(b.transform, "Icon", null, 0f, 0f, 16f, 16f);
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(Tex + "UI-Taxi-Icon-Highlight.blp"), -8f, -8f, 32f, 32f);
            b.Hilite.enabled = false;
            b.Hilite.raycastTarget = false;
            _buttons.Add(b);
            return b;
        }

        private static string Tooltip(TaxiNodeView v, bool here)
        {
            string name = string.IsNullOrEmpty(v.Name) ? "Flight point " + v.Id : v.Name;
            if (here) return "<color=#FFFFFF>" + name + "</color>\n<color=#80FF80>You are here</color>";
            if (!v.Reachable) return "<color=#FFFFFF>" + name + "</color>\n<color=#808080>No route from here</color>";
            string hops = v.Route != null && v.Route.Count > 1 ? "\n<color=#FFD100>" + v.Route.Count + " stops</color>" : "";
            return "<color=#FFFFFF>" + name + "</color>\n" + UiArt.MoneyText(v.Cost) + hops;
        }

        // ------------------------------------------------------------------------------------------------ route lines

        private void DrawLines()
        {
            int used = 0;
            TaxiNodeView hv = null;
            if (_hovered != null)
            {
                int i = _buttons.IndexOf(_hovered);
                if (i >= 0 && i < _views.Count) hv = _views[i];
            }
            if (hv != null && hv.Reachable && !hv.Current && hv.Route != null && hv.Route.Count > 0)
            {
                // TaxiNodeOnButtonEnter: one strip per hop of the chosen route.
                for (int h = 0; h < hv.Route.Count; h++)
                {
                    int from, to, cost;
                    Vector2 a, b;
                    if (_data.TryPath(hv.Route[h], out from, out to, out cost) && PointOf(from, out a) && PointOf(to, out b)) Line(used++, a, b);
                }
            }
            else if (_taxi != null)
            {
                // DrawOneHopLines: every node one hop from here.
                Vector2 here;
                if (PointOf(_taxi.CurrentNodeId, out here))
                {
                    for (int i = 0; i < _views.Count; i++)
                    {
                        TaxiNodeView v = _views[i];
                        if (v.Reachable && v.Route != null && v.Route.Count == 1) Line(used++, here, _points[i]);
                    }
                }
            }
            for (int i = used; i < _lines.Count; i++) _lines[i].enabled = false;
        }

        private bool PointOf(int nodeId, out Vector2 p)
        {
            for (int i = 0; i < _views.Count; i++) if (_views[i].Id == nodeId) { p = _points[i]; return true; }
            p = Vector2.zero;
            return false;
        }

        /// <summary>DrawRouteLine: a strip of UI-Taxi-Line (a horizontal line across a 32 x 32 texture) from a to b, 32 units thick.</summary>
        private void Line(int index, Vector2 a, Vector2 b)
        {
            RawImage img;
            if (index < _lines.Count) img = _lines[index];
            else
            {
                bool over = UiKit.Over;
                UiKit.Over = false;
                img = UiKit.Pic(_lineLayer, "TaxiRoute" + (index + 1), UiArt.Tex(Tex + "UI-Taxi-Line.blp"), 0f, 0f, 32f, 32f);
                UiKit.Over = over;
                img.raycastTarget = false;
                _lines.Add(img);
            }
            Vector2 d = b - a;
            float len = d.magnitude * LineFactor;
            RectTransform r = img.rectTransform;
            // UiKit coordinates are y down: the strip's centre at the midpoint, turned by the segment's angle (y flipped).
            Vector2 mid = (a + b) * 0.5f;
            UiKit.Size(r, Mathf.Max(1f, len), 32f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(mid.x, -mid.y);
            r.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            img.enabled = true;
        }

        // ------------------------------------------------------------------------------------------------ VR placement

        /// <summary>Between the player and the flight master, a little to the left of the line of sight, facing the player.</summary>
        private void Place(Vector3? masterPos)
        {
            Camera head = WowPlayer.Head();
            if (head == null) return;
            Vector3 eye = head.transform.position;
            Vector3 dir = head.transform.forward;
            if (masterPos.HasValue)
            {
                Vector3 d = masterPos.Value - eye;
                d.y = 0f;
                if (d.sqrMagnitude > 0.04f) dir = d;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 pos = eye + dir * 0.8f - right * 0.15f - Vector3.up * 0.1f;
            _surface.Root.SetPositionAndRotation(pos, Quaternion.LookRotation((pos - eye).normalized, Vector3.up));
        }
    }
}
