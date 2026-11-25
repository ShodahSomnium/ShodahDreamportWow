using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.MapApp
{
    public enum MapMarkKind : byte { Hostile = 0, QuestAvailable = 1, QuestIncomplete = 2, QuestComplete = 3, FlightMaster = 4, Party = 5, Friendly = 6, Corpse = 7 }   // Corpse: stage O2 (a ghost's corpse)

    /// <summary>One thing drawn on the map (Unity position in the current frame).</summary>
    public struct MapMark
    {
        public MapMarkKind Kind;
        public Vector3 Pos;
        public int Class;      // Party: WowClass id (PartyRaidBlips cell = class - 1)
        public bool Target;    // the player's current target: ringed
    }

    /// <summary>What every panel shows this frame (filled once by WowTabletMap).</summary>
    public struct MapView
    {
        public bool InPlay;
        public Vector3 Centre;     // view centre (Unity)
        public float Yaw;          // view up direction (Unity yaw, degrees): 0 = north up
        public float Half;         // half the visible width (metres)
        public bool HaveFeet;
        public Vector3 Feet;
        public float Facing;       // the player's facing (Unity yaw, degrees)
        public bool Live;
        public bool Rotate, Panned, CanZoomIn, CanZoomOut;
        public int MapId;
        public string Zone, Coords, ZoomText;
        // Iteration 11 quest map
        public bool ZoneView;          // the World Map view: WoW's zone map art (WorldMapDetailFrame) instead of the live / classic picture
        public WowZoneInfo ZoneInfo;   // the zone it shows (null = no WorldMapArea for it)
        public bool ZoneIsPlayers;     // that zone is the one the player is in
        public int SelectedQuest;      // WORLDMAP_SETTINGS.selectedQuestId
    }

    /// <summary>
    /// The map panel (640 x 720 units, UiKit coordinates: top-left origin, y down), built into a host: the tablet's app frame,
    /// the VR world window or the desktop screen window. Top: zone name + WoW-style coordinates. Middle: the 608 x 608 map
    /// (live camera picture or classic minimap tiles) with the markers (WoW art from the game files, verified by the harness
    /// probe MapArt: Interface\Minimap\MinimapArrow.blp, ObjectIcons.blp, PartyRaidBlips.blp, Tracking\FlightMaster.blp). A
    /// click/tap on the map pans there; Recenter follows the player again. Bottom: zoom -/+, Rotate, Live/Classic, Recenter.
    /// Buttons are UiButtons: the EventSystem (tablet ray, desktop mouse) and UiPointer (VR window) both press them.
    /// Iteration 11 quest map: View cycles Live / Classic / World Map; the World Map view draws the zone's WorldMapDetailFrame art across the
    /// square (1002 x 668 at ZoneScale), the quest POI icons and blob (also on the live / classic views), and the zone's quest list under it.
    /// </summary>
    public sealed class WowMapPanel
    {
        public const float W = 640f, H = 720f;
        public const float MapX = 16f, MapY = 64f, MapSize = 608f;
        private const float Edge = MapSize * 0.5f;

        public const string ArrowTex = @"Interface\Minimap\MinimapArrow.blp";
        public const string ObjectIconsTex = @"Interface\Minimap\ObjectIcons.blp";
        public const string BlipsTex = @"Interface\Minimap\PartyRaidBlips.blp";
        public const string FlightTex = @"Interface\Minimap\Tracking\FlightMaster.blp";
        // Stage O2 (integrator, ghost request 4): the world map's corpse icon, WorldMapFrameTemplates.xml WorldMapCorpseTemplate (16 x 16):
        // <TexCoords left="0.56640625" right="0.6328125" top="0.00390625" bottom="0.0703125"/> on Interface\Minimap\POIIcons (as UiGhost's marker).
        public const string PoiIconsTex = @"Interface\Minimap\POIIcons.blp";
        private const float CorpseL = 0.56640625f, CorpseR = 0.6328125f, CorpseT = 0.00390625f, CorpseB = 0.0703125f;
        public const string ZoomInUp = @"Interface\Minimap\UI-Minimap-ZoomInButton-Up.blp";
        public const string ZoomInDown = @"Interface\Minimap\UI-Minimap-ZoomInButton-Down.blp";
        public const string ZoomInDisabled = @"Interface\Minimap\UI-Minimap-ZoomInButton-Disabled.blp";
        public const string ZoomOutUp = @"Interface\Minimap\UI-Minimap-ZoomOutButton-Up.blp";
        public const string ZoomOutDown = @"Interface\Minimap\UI-Minimap-ZoomOutButton-Down.blp";
        public const string ZoomOutDisabled = @"Interface\Minimap\UI-Minimap-ZoomOutButton-Disabled.blp";
        public const string ZoomHilite = @"Interface\Minimap\UI-Minimap-ZoomButton-Highlight.blp";

        public RectTransform Root;
        private RectTransform _map, _tiles, _marks;
        private RawImage _live, _arrow, _ring;
        private readonly RawImage[] _tileImg = new RawImage[9];
        private TextMeshProUGUI _zone, _coords, _zoom, _hint;
        private UiButton _rotate, _mode, _recenter, _zoomIn, _zoomOut;
        private readonly List<RawImage> _pool = new List<RawImage>();
        private Texture2D _icons, _blips, _flight, _poi;
        private string _zoneShown, _coordsShown, _zoomShown;
        private int _refreshes;

        // Iteration 11 quest map: the World Map view (WorldMapDetailFrame 1002 x 668: 12 tiles of 256 + the overlays) across the map square,
        // the quest blob, the POI icons and the quest list under the art.
        public const float ZoneScale = MapSize / 1002f, ZoneH = 668f * ZoneScale;   // WorldMapDetailFrame <AbsDimension x="1002" y="668"/> (WorldMapFrame.xml:528-531)
        private const int StripRows = 8;
        private const float StripTop = ZoneH + 26f, RowH = 20f, ObjLineH = 15f;   // ours
        private RectTransform _zoneArt, _questClip, _questLayer;
        private readonly RawImage[] _zoneTile = new RawImage[12];
        private readonly List<RawImage> _zoneOver = new List<RawImage>();
        private int _zoneArtId = -1;
        private WowQuestBlob _blobFill, _blobEdge;
        private readonly List<QuestPoiIcon> _poiPool = new List<QuestPoiIcon>();
        private readonly UiButton[] _row = new UiButton[StripRows];
        private readonly QuestPoiIcon[] _rowIcon = new QuestPoiIcon[StripRows];
        private readonly TextMeshProUGUI[] _rowText = new TextMeshProUGUI[StripRows];
        private readonly int[] _rowQuest = new int[StripRows];
        private RawImage _rowSel;
        private TextMeshProUGUI _objText;
        private UiButton _showObj;
        private RawImage _showObjCheck;
        private int _stripSig = int.MinValue, _blobSig = int.MinValue;
        private static readonly System.Text.StringBuilder Sb = new System.Text.StringBuilder(256);

        public bool Alive { get { return Root != null; } }

        /// <summary>Builds the panel under parent at (0, 0) of it. window = draw a border and a close button.</summary>
        public static WowMapPanel Build(Transform parent, string name, WowTabletMap owner, bool window, Action close)
        {
            var p = new WowMapPanel();
            bool pointable = UiKit.Pointable;
            UiKit.Pointable = true;   // raycast targets for the EventSystem (tablet ray, desktop mouse); harmless for UiPointer
            try
            {
                p.Root = UiKit.Rect(parent, name, 0f, 0f, W, H);
                Transform c = p.Root;
                RawImage bg = UiKit.Solid(c, "Background", new Color(0f, 0f, 0f, window ? 0.82f : 1f), window ? 4f : 0f, window ? 4f : 0f, window ? W - 8f : W, window ? H - 8f : H);
                bg.raycastTarget = true;   // a miss between buttons never falls through
                if (window)
                {
                    UiBackdrop edge = UiBackdrop.Make(c, "Border", null, UiArt.TooltipBorder, 16f, 5f, 16f, Color.white);
                    edge.Place(0f, 0f, W, H);
                    edge.SetEdgeColor(new Color(0.8f, 0.8f, 0.8f, 1f));
                    UiKit.CloseButton(c, W - 36f, 4f, close);
                }

                p._zone = UiKit.Text(c, "Zone", "", 18f, UiArt.Gold, TextAlignmentOptions.Center, 40f, 10f, W - 80f, 26f);
                p._coords = UiKit.Text(c, "Coordinates", "", 14f, Color.white, TextAlignmentOptions.Center, 40f, 38f, W - 80f, 20f);

                // The map square: black, masked; the click catcher first (under everything drawn on the map).
                p._map = UiKit.Rect(c, "Map", MapX, MapY, MapSize, MapSize);
                p._map.gameObject.AddComponent<RectMask2D>();
                UiKit.Solid(p._map, "Map background", new Color(0.06f, 0.055f, 0.05f, 1f), 0f, 0f, MapSize, MapSize);
                UiButton pan = UiKit.Button(p._map, "Map (click to pan)", 0f, 0f, MapSize, MapSize);
                pan.ClickAt = f => owner.PanTo(f);
                pan.Tip = () => "Click to look there; Recenter follows you again.";

                p._tiles = Centred(p._map, "Classic tiles", MapSize, MapSize);
                for (int i = 0; i < 9; i++)
                {
                    RectTransform r = Centred(p._tiles, "Tile " + i, 10f, 10f);
                    r.pivot = new Vector2(0f, 1f);
                    RawImage img = r.gameObject.AddComponent<RawImage>();
                    img.raycastTarget = false;
                    img.uvRect = new Rect(0f, 1f, 1f, -1f);   // BLP rows are stored top first (PLAN rule 15)
                    img.enabled = false;
                    p._tileImg[i] = img;
                }

                RectTransform live = Centred(p._map, "Live picture", MapSize, MapSize);
                p._live = live.gameObject.AddComponent<RawImage>();
                p._live.raycastTarget = false;
                p._live.enabled = false;

                // Iteration 11 quest map: the World Map view's art (WorldMapDetailTile1..12, 256 x 256 in 4 x 3 from TOPLEFT, WorldMapFrame.xml:528-640;
                // the overlays above them), clipped to the 1002 x 668 detail frame; then the quest blob and POI icons in their own clip.
                p._zoneArt = UiKit.Rect(p._map, "World map art", 0f, 0f, MapSize, ZoneH);
                p._zoneArt.gameObject.AddComponent<RectMask2D>();
                for (int i = 0; i < 12; i++)
                    p._zoneTile[i] = UiKit.Pic(p._zoneArt, "Detail tile " + (i + 1), null, (i % 4) * 256f * ZoneScale, (i / 4) * 256f * ZoneScale, 256f * ZoneScale, 256f * ZoneScale);
                p._zoneArt.gameObject.SetActive(false);
                p._questClip = UiKit.Rect(p._map, "Quest POI clip", 0f, 0f, MapSize, MapSize);
                p._questClip.gameObject.AddComponent<RectMask2D>();
                p._questLayer = UiKit.Rect(p._questClip, "Quest POIs", 0f, 0f, MapSize, MapSize);
                p._blobFill = WowQuestBlob.Make(p._questLayer, "Quest blob (fill)", UiArt.Tex(WowQuestMap.FillTex), false, WowQuestMap.FillAlpha, MapSize);
                p._blobEdge = WowQuestBlob.Make(p._questLayer, "Quest blob (border)", UiArt.Tex(WowQuestMap.BorderTex), true, WowQuestMap.BorderAlpha, MapSize);
                p._blobFill.enabled = p._blobEdge.enabled = false;

                p._marks = Centred(p._map, "Markers", MapSize, MapSize);
                p._icons = UiArt.Tex(ObjectIconsTex);
                p._blips = UiArt.Tex(BlipsTex);
                p._flight = UiArt.Tex(FlightTex);
                p._poi = UiArt.Tex(PoiIconsTex);
                p._ring = Mark(p._marks, "Target ring", p._icons, 26f);
                p._ring.enabled = false;
                p._arrow = Mark(p._marks, "Player arrow", UiArt.Tex(ArrowTex), 30f);
                SetWowCoords(p._arrow, 0f, 1f, 0f, 1f);

                p._hint = UiKit.Text(p._map, "Hint", "", 16f, UiArt.Gold, TextAlignmentOptions.Center, 20f, MapSize * 0.5f - 20f, MapSize - 40f, 40f);

                // Iteration 11 quest map: under the art, the shown zone's quests (WorldMapQuestScrollFrame's quest frames: own POI icon, title, the
                // selected one's objectives - ours: only the selected quest's objectives, for room) and WorldMapQuestShowObjectives
                // (OptionsCheckButtonTemplate, SHOW_QUEST_OBJECTIVES_ON_MAP_TEXT "Show Quest Objectives", GlobalStrings.lua:6302).
                p._rowSel = UiKit.Pic(p._map, "Selected quest bar", UiArt.Add(UiArt.QuestLogTitleHighlight), 0f, StripTop, MapSize, RowH);
                p._rowSel.color = new Color(1f, 0.824f, 0f, 1f);                  // WorldMapQuestSelectBar:SetVertexColor(1, 0.824, 0)
                p._rowSel.enabled = false;
                for (int i = 0; i < StripRows; i++)
                {
                    int k = i;
                    UiButton b = UiKit.Button(p._map, "Quest row " + (i + 1), 0f, StripTop + i * RowH, MapSize, RowH);
                    b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestLogTitleHighlight), 0f, 0f, MapSize, RowH);
                    b.Hilite.color = new Color(0.243f, 0.570f, 1f, 1f);            // WorldMapQuestHighlightBar:SetVertexColor(0.243, 0.570, 1)
                    b.Hilite.enabled = false;
                    p._rowIcon[i] = QuestPoiIcon.Make(b.transform, "POI", RowH, false, 4f, 0f);
                    p._rowText[i] = UiKit.Text(b.transform, "Title", "", 13f, UiArt.Gold, TextAlignmentOptions.Left, 30f, 0f, MapSize - 34f, RowH);
                    p._rowText[i].textWrappingMode = TextWrappingModes.NoWrap;
                    p._rowText[i].overflowMode = TextOverflowModes.Ellipsis;
                    b.Click = () => owner.SelectQuest(p._rowQuest[k]);
                    b.gameObject.SetActive(false);
                    p._row[i] = b;
                }
                p._objText = UiKit.Text(p._map, "Quest objectives", "", 11f, Color.white, TextAlignmentOptions.TopLeft, 30f, StripTop, MapSize - 34f, 60f);
                p._objText.enabled = false;
                p._showObj = TrUi.CheckBox(p._map, UiGlobalStrings.G("SHOW_QUEST_OBJECTIVES_ON_MAP_TEXT", "Show Quest Objectives"), MapSize - 196f, ZoneH + 4f, 192f,
                                                () => WowQuestMap.ShowObjectives, on => WowQuestMap.SetShowObjectives(on));
                Transform check = p._showObj.transform.Find("Check");
                p._showObjCheck = check != null ? check.GetComponent<RawImage>() : null;
                p._showObj.Tip = () => UiGlobalStrings.G("OPTION_TOOLTIP_SHOW_QUEST_OBJECTIVES_ON_MAP", "If enabled, you will be able to look at your map to see the locations of certain quest objectives.");   // GS:5441
                p._showObj.gameObject.SetActive(false);

                // Bottom row.
                float y = H - 44f;
                p._zoomOut = UiKit.ArtButton(c, "Zoom out", ZoomOutUp, ZoomOutDown, ZoomOutDisabled, ZoomHilite, 16f, y, 32f, owner.ZoomOut);
                p._zoomOut.Tip = () => "Zoom out";
                p._zoomIn = UiKit.ArtButton(c, "Zoom in", ZoomInUp, ZoomInDown, ZoomInDisabled, ZoomHilite, 52f, y, 32f, owner.ZoomIn);
                p._zoomIn.Tip = () => "Zoom in";
                p._zoom = UiKit.Text(c, "Zoom", "", 14f, Color.white, TextAlignmentOptions.Left, 92f, y + 7f, 90f, 20f);
                p._rotate = UiKit.PanelButton(c, "Rotate: Off", 186f, y + 2f, 136f, 28f, owner.ToggleRotate);
                p._rotate.Tip = () => "Turn the map with your heading";
                p._mode = UiKit.PanelButton(c, "View: Live", 330f, y + 2f, 136f, 28f, owner.ToggleMode);
                p._mode.Tip = () => "Live camera view / the game's classic minimap";
                p._recenter = UiKit.PanelButton(c, "Recenter", 474f, y + 2f, 150f, 28f, owner.Recenter);
                p._recenter.Tip = () => "Follow your position again";
            }
            finally
            {
                UiKit.Pointable = pointable;
            }
            return p;
        }

        private static RectTransform Centred(Transform parent, string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiKit.Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
            return r;
        }

        private static RawImage Mark(Transform parent, string name, Texture2D tex, float size)
        {
            RectTransform r = Centred(parent, name, size, size);
            RawImage img = r.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>WoW texcoords (left, right, top, bottom; top = 0 is the picture's first row, as FrameXML) onto a BLP texture.</summary>
        private static void SetWowCoords(RawImage img, float l, float r, float t, float b)
        {
            var uv = new Rect(l, b, r - l, t - b);
            if (img.uvRect != uv) img.uvRect = uv;
        }

        /// <summary>ObjectIcons.blp is 8 x 2 cells of 32 x 32; PartyRaidBlips.blp 8 x 4 (probe MapArt).</summary>
        private static void Cell(RawImage img, int cell, int rows)
        {
            int col = cell % 8, row = cell / 8;
            SetWowCoords(img, col / 8f, (col + 1) / 8f, row / (float)rows, (row + 1) / (float)rows);
        }

        /// <summary>World point -> map units (centre origin, y up) for the view.</summary>
        public static Vector2 Project(in MapView v, Vector3 p, float scale)
        {
            float dx = p.x - v.Centre.x, dz = p.z - v.Centre.z;
            float s = Mathf.Sin(v.Yaw * Mathf.Deg2Rad), c = Mathf.Cos(v.Yaw * Mathf.Deg2Rad);
            return new Vector2((dx * c - dz * s) * scale, (dx * s + dz * c) * scale);
        }

        /// <summary>Iteration 11 quest map: WoW world x / y to map units (centre origin, y up) in the World Map view - the 0..1 of the zone
        /// times WorldMapDetailFrame's size ("posX * WorldMapDetailFrame:GetWidth()", WorldMapFrame.lua:1711-1731 / :788-790), at ZoneScale.</summary>
        public static Vector2 ZonePoint(in MapView v, float x, float y)
        {
            float u = 0f, w = 0f;
            if (v.ZoneInfo != null) v.ZoneInfo.ToUv(x, y, out u, out w);
            return new Vector2(-Edge + u * MapSize, Edge - w * ZoneH);
        }

        /// <summary>A quest POI point (WoW yards) in map units for the current view.</summary>
        private static Vector2 QuestPoint(in MapView v, float x, float y, float scale)
        {
            if (v.ZoneView) return ZonePoint(v, x, y);
            return Project(v, WowSpace.ToUnity(v.MapId, x, y, 0f), scale);
        }

        public void Refresh(in MapView v, WowMapCamera cam, WowMapTiles tiles, List<MapMark> marks, List<QuestMapEntry> quests)
        {
            if (Root == null) return;
            _refreshes++;
            float scale = Edge / Mathf.Max(1f, v.Half);   // map units per metre
            bool zv = v.ZoneView;

            SetText(_zone, ref _zoneShown, v.InPlay ? (v.Zone ?? "") : "World of Warcraft");
            SetText(_coords, ref _coordsShown, v.InPlay ? (v.Coords ?? "") : "");
            SetText(_zoom, ref _zoomShown, v.ZoomText ?? "");
            _rotate.SetLabel(v.Rotate ? "Rotate: On" : "Rotate: Off");
            _rotate.Interactable = !zv;
            _mode.SetLabel(zv ? "View: World Map" : v.Live ? "View: Live" : "View: Classic");
            _recenter.Interactable = zv ? !v.ZoneIsPlayers : v.Panned;
            _zoomIn.Interactable = v.CanZoomIn;
            _zoomOut.Interactable = v.CanZoomOut;

            string hint = !v.InPlay ? "Enter the world to see the map." : zv ? (v.ZoneInfo == null ? "No World Map for this area." : "")
                        : !v.HaveFeet ? "Finding you..." : v.Live && (cam == null || !cam.HasImage) ? "Drawing the map..." : "";
            if (_hint.text != hint) _hint.text = hint;

            // Iteration 11 quest map: the World Map view's art.
            bool art = zv && v.InPlay && v.ZoneInfo != null;
            if (_zoneArt.gameObject.activeSelf != art) _zoneArt.gameObject.SetActive(art);
            if (art) PlaceZoneArt(v.ZoneInfo);

            // Live picture: rendered for (ShownCentre, ShownYaw, ShownHalf); placed and turned into the current view.
            bool live = !zv && v.InPlay && v.Live && cam != null && cam.HasImage && cam.Texture != null;
            if (_live.enabled != live) _live.enabled = live;
            if (live)
            {
                if (_live.texture != cam.Texture) _live.texture = cam.Texture;
                Vector2 at = Project(v, cam.ShownCentre, scale);
                float size = 2f * cam.ShownHalf * WowMapCamera.Margin * scale;
                RectTransform r = _live.rectTransform;
                r.anchoredPosition = at;
                r.sizeDelta = new Vector2(size, size);
                r.localEulerAngles = new Vector3(0f, 0f, v.Yaw - cam.ShownYaw);
            }

            // Classic tiles: the 3 x 3 ADT tiles around the view centre, in a container turned with the view.
            bool classic = !zv && v.InPlay && !v.Live && tiles != null;
            _tiles.gameObject.SetActive(classic);
            if (classic) PlaceTiles(v, tiles, scale);

            // Iteration 11 quest map: the quest blob and POI icons (under the other markers), the World Map view's quest list.
            RefreshQuests(v, quests, scale);

            // Markers. The World Map view shows the party and the corpse only (WorldMapButton_OnUpdate's raid / party / corpse frames).
            int used = 0;
            bool ringed = false;
            if (v.InPlay && marks != null)
            {
                for (int i = 0; i < marks.Count && used < 200; i++)
                {
                    MapMark m = marks[i];
                    Vector2 at;
                    if (zv)
                    {
                        if (m.Kind != MapMarkKind.Party && m.Kind != MapMarkKind.Corpse || v.ZoneInfo == null) continue;
                        float mx, my, mz;
                        WowSpace.ToWow(v.MapId, m.Pos, out mx, out my, out mz);
                        if (!v.ZoneInfo.Contains(v.MapId, mx, my)) continue;   // GetPlayerMapPosition 0, 0: hidden
                        at = ZonePoint(v, mx, my);
                    }
                    else at = Project(v, m.Pos, scale);
                    bool keepOnEdge = m.Kind == MapMarkKind.Party || m.Kind == MapMarkKind.Corpse || m.Target;
                    if (Mathf.Abs(at.x) > Edge + 8f || Mathf.Abs(at.y) > Edge + 8f)
                    {
                        if (!keepOnEdge) continue;
                        float k = (Edge - 8f) / Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y));
                        at *= k;
                    }
                    RawImage img = Pooled(used++);
                    float size;
                    Color color = Color.white;
                    switch (m.Kind)
                    {
                        case MapMarkKind.Hostile: img.texture = _icons; Cell(img, 2, 2); size = 18f; break;
                        case MapMarkKind.QuestAvailable: img.texture = _icons; Cell(img, 9, 2); size = 22f; break;
                        case MapMarkKind.QuestComplete: img.texture = _icons; Cell(img, 10, 2); size = 22f; break;
                        case MapMarkKind.QuestIncomplete: img.texture = _icons; Cell(img, 10, 2); size = 20f; color = new Color(0.55f, 0.55f, 0.55f, 1f); break;
                        case MapMarkKind.FlightMaster: img.texture = _flight; SetWowCoords(img, 0f, 1f, 0f, 1f); size = 22f; break;
                        case MapMarkKind.Party: img.texture = _blips; Cell(img, Mathf.Clamp(m.Class - 1, 0, 15), 4); size = 16f; break;
                        case MapMarkKind.Corpse: img.texture = _poi; SetWowCoords(img, CorpseL, CorpseR, CorpseT, CorpseB); size = 16f; break;
                        default: img.texture = _icons; Cell(img, 4, 2); size = 14f; break;   // own pet, a friendly target: green dot
                    }
                    if (img.color != color) img.color = color;
                    RectTransform r = img.rectTransform;
                    r.anchoredPosition = at;
                    if (r.sizeDelta.x != size) r.sizeDelta = new Vector2(size, size);
                    if (m.Target && !ringed)
                    {
                        ringed = true;
                        Cell(_ring, 3, 2);
                        float pulse = 26f + 4f * Mathf.Sin(Time.unscaledTime * 6f);
                        _ring.rectTransform.anchoredPosition = at;
                        _ring.rectTransform.sizeDelta = new Vector2(pulse, pulse);
                        _ring.color = new Color(1f, 0.85f, 0.2f, 0.75f);
                    }
                }
            }
            for (int i = used; i < _pool.Count; i++) if (_pool[i].enabled) _pool[i].enabled = false;
            if (_ring.enabled != ringed) _ring.enabled = ringed;
            if (ringed) _ring.transform.SetSiblingIndex(0);

            // The player's arrow last (on top).
            bool arrow = v.InPlay && v.HaveFeet;
            Vector2 feetAt = Vector2.zero;
            if (arrow && zv)
            {
                // the World Map view: the player inside the shown zone only ("if ( (playerX == 0 and playerY == 0) ) ... WorldMapPlayer:Hide()", :782-786)
                float fx, fy, fz;
                WowSpace.ToWow(v.MapId, v.Feet, out fx, out fy, out fz);
                arrow = v.ZoneInfo != null && v.ZoneInfo.Contains(v.MapId, fx, fy);
                if (arrow) feetAt = ZonePoint(v, fx, fy);
            }
            else if (arrow) feetAt = Project(v, v.Feet, scale);
            if (_arrow.enabled != arrow) _arrow.enabled = arrow;
            if (arrow)
            {
                Vector2 at = feetAt;
                if (Mathf.Abs(at.x) > Edge - 10f || Mathf.Abs(at.y) > Edge - 10f) at *= (Edge - 10f) / Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y));
                _arrow.rectTransform.anchoredPosition = at;
                _arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, (zv ? 0f : v.Yaw) - v.Facing);
                _arrow.transform.SetAsLastSibling();
            }
        }

        // ------------------------------------------------------------------ iteration 11 quest map

        /// <summary>
        /// The quests' POI icons (WorldMapFrame_DisplayQuestPOI: QUEST_POI_NUMERIC, or QUEST_POI_COMPLETE_SWAP for a complete quest; the
        /// selected one raised, "poiIcon:SetFrameLevel(WORLDMAP_POI_FRAMELEVEL + 1)"), the selected unfinished quest's blob, and the World
        /// Map view's quest list. The live / classic views (ours) show the player's zone's quests with the tracker's icon types
        /// (QUEST_POI_COMPLETE_IN for a complete one), an icon beyond the edge kept on it so it points the way.
        /// </summary>
        private void RefreshQuests(in MapView v, List<QuestMapEntry> quests, float scale)
        {
            bool zv = v.ZoneView;
            bool on = v.InPlay && quests != null && (zv ? v.ZoneInfo != null : v.HaveFeet);
            float clipH = zv ? ZoneH : MapSize;
            if (_questClip.sizeDelta.y != clipH) UiKit.Size(_questClip, MapSize, clipH);
            int used = 0;
            QuestMapEntry sel = null;
            QuestPoiIcon selIcon = null;
            if (on)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    QuestMapEntry e = quests[i];
                    bool selected = e.QuestId == v.SelectedQuest;
                    if (selected) sel = e;
                    if (!e.HasIcon) continue;
                    Vector2 at = QuestPoint(v, e.IconX, e.IconY, scale);
                    float lim = Edge - WowQuestMap.IconUnits * 0.5f;
                    if (zv)
                    {
                        // "keep outlying POIs within map borders" (WorldMapFrame.lua:1720-1730, 12 units in)
                        at.x = Mathf.Clamp(at.x, -Edge + 12f, Edge - 12f);
                        at.y = Mathf.Clamp(at.y, Edge - ZoneH + 12f, Edge - 12f);
                    }
                    else if (Mathf.Abs(at.x) > lim || Mathf.Abs(at.y) > lim) at *= lim / Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y));
                    QuestPoiIcon ic = PoiIcon(used++);
                    ic.Set(e.Complete ? (zv ? QuestPoiIcon.CompleteSwap : QuestPoiIcon.CompleteIn) : QuestPoiIcon.Numeric, e.Number, selected);
                    ic.Root.anchoredPosition = at;
                    if (selected) selIcon = ic;
                }
            }
            for (int i = used; i < _poiPool.Count; i++) _poiPool[i].SetShown(false);
            if (selIcon != null) selIcon.Root.SetAsLastSibling();
            // WorldMapFrame_SelectQuestFrame: "if ( questFrame.completed ) DrawQuestBlob(questId, false) else DrawQuestBlob(questId, true)"
            DrawBlob(v, on && sel != null && !sel.Complete ? sel : null, scale);
            RefreshStrip(v, zv && on ? quests : null);
        }

        private QuestPoiIcon PoiIcon(int i)
        {
            while (_poiPool.Count <= i) _poiPool.Add(QuestPoiIcon.Make(_questLayer, "Quest POI " + (_poiPool.Count + 1), WowQuestMap.IconUnits, true));
            QuestPoiIcon ic = _poiPool[i];
            ic.SetShown(true);
            ic.Root.anchorMin = ic.Root.anchorMax = ic.Root.pivot = new Vector2(0.5f, 0.5f);
            return ic;
        }

        private void DrawBlob(in MapView v, QuestMapEntry e, float scale)
        {
            int sig = e == null ? 0 : e.QuestId * 31 + e.Blob.Count * 7 + (v.ZoneInfo != null ? v.ZoneInfo.Id * 131 : 0) + (v.ZoneView ? 1 : 2);
            if (v.ZoneView && sig == _blobSig) return;                              // the World Map view stands still; the local views move every frame
            _blobSig = sig;
            _blobFill.Clear();
            _blobEdge.Clear();
            if (e != null)
            {
                _blobEdge.BandWidth = v.ZoneView ? 16f * ZoneScale : 10f;           // the border texture's 16 pixels x SetBorderScalar(1.0); ours 10 units on the local views
                for (int a = 0; a < e.Blob.Count; a++)
                {
                    QuestPoiView poi = e.Blob[a];
                    _blobFill.Begin();
                    _blobEdge.Begin();
                    for (int k = 0; k < poi.X.Length; k++)
                    {
                        Vector2 p = QuestPoint(v, poi.X[k], poi.Y[k], scale);
                        _blobFill.Add(p);
                        _blobEdge.Add(p);
                    }
                }
            }
            bool show = e != null && _blobFill.Polygons > 0;
            if (_blobFill.enabled != show) _blobFill.enabled = show;
            if (_blobEdge.enabled != show) _blobEdge.enabled = show;
            if (_blobFill.Tex == null) _blobFill.Tex = UiArt.Tex(WowQuestMap.FillTex);
            if (_blobEdge.Tex == null) _blobEdge.Tex = UiArt.Tex(WowQuestMap.BorderTex);
            _blobFill.Commit();
            _blobEdge.Commit();
        }

        /// <summary>WorldMapFrame_UpdateQuests' list: per quest its own POI (QUEST_POI_COMPLETE_IN / QUEST_POI_NUMERIC), the title; the selected
        /// quest under WorldMapQuestSelectBar with its objectives ("- " + WorldMapFrame_ReverseQuestObjective, or GetQuestLogCompletionText).</summary>
        private void RefreshStrip(in MapView v, List<QuestMapEntry> quests)
        {
            bool zv = v.ZoneView && v.InPlay;
            if (_showObj.gameObject.activeSelf != zv) _showObj.gameObject.SetActive(zv);
            if (zv && _showObjCheck != null && _showObjCheck.enabled != WowQuestMap.ShowObjectives) _showObjCheck.enabled = WowQuestMap.ShowObjectives;
            int n = quests != null ? quests.Count : 0;
            int sig = (zv ? 1 : 0) + n * 7 + v.SelectedQuest * 31;
            for (int i = 0; i < n; i++)
            {
                QuestView q = quests[i].Quest;
                sig = sig * 31 + quests[i].QuestId + quests[i].Number * 3 + (q.Title != null ? q.Title.GetHashCode() : 0) + (int)q.State;
                for (int o = 0; o < q.Objectives.Count; o++) sig = sig * 17 + q.Objectives[o].Have + (q.Objectives[o].Text != null ? q.Objectives[o].Text.GetHashCode() : 0);
            }
            if (sig == _stripSig) return;
            _stripSig = sig;
            int selIdx = -1;
            for (int i = 0; i < n; i++) if (quests[i].QuestId == v.SelectedQuest) selIdx = i;
            int first = selIdx >= StripRows ? selIdx - StripRows + 1 : 0;           // ours: the window of rows keeps the selected quest in view
            float y = StripTop;
            bool selShown = false;
            for (int r = 0; r < StripRows; r++)
            {
                int i = first + r;
                bool show = zv && i < n && y + RowH <= MapSize;
                UiButton b = _row[r];
                if (b.gameObject.activeSelf != show) b.gameObject.SetActive(show);
                if (!show) { _rowQuest[r] = 0; continue; }
                QuestMapEntry e = quests[i];
                bool selected = e.QuestId == v.SelectedQuest;
                _rowQuest[r] = e.QuestId;
                UiKit.Move(b.Rt, 0f, y);
                _rowIcon[r].Set(e.Complete ? QuestPoiIcon.CompleteIn : QuestPoiIcon.Numeric, e.Number, selected);
                _rowText[r].text = e.Quest.Title ?? "";
                _rowText[r].color = selected ? Color.white : UiArt.Gold;
                y += RowH;
                if (!selected) continue;
                selShown = true;
                UiKit.Move(_rowSel.rectTransform, 0f, y - RowH);
                Sb.Length = 0;
                int lines = 0;
                QuestView q = e.Quest;
                if (e.Complete) { Sb.Append("- ").Append(string.IsNullOrEmpty(q.LogCompletionText) ? "Ready to hand in." : q.LogCompletionText); lines = 1; }   // the tracker's fallback (ours)
                else
                    for (int o = 0; o < q.Objectives.Count; o++)
                    {
                        if (q.Objectives[o].Done) continue;
                        if (lines > 0) Sb.Append('\n');
                        Sb.Append("- ").Append(WowQuestMap.ObjectiveLine(q.Objectives[o]));
                        lines++;
                    }
                _objText.text = Sb.ToString();
                float h = Mathf.Max(1, lines) * ObjLineH;
                UiKit.Move(_objText.rectTransform, 30f, y);
                UiKit.Size(_objText.rectTransform, MapSize - 34f, h);
                y += h + 2f;
            }
            if (_rowSel.enabled != selShown) _rowSel.enabled = selShown;
            if (_objText.enabled != selShown) _objText.enabled = selShown;
        }

        /// <summary>The World Map view's art: the 12 detail tiles and the zone's overlays (WowQuestMap.ZoneOverlays), once per zone.</summary>
        private void PlaceZoneArt(WowZoneInfo z)
        {
            if (_zoneArtId == z.Id) return;
            _zoneArtId = z.Id;
            int tiles = 0;
            for (int i = 0; i < 12; i++)
            {
                Texture2D t = UiArt.Tex(@"Interface\WorldMap\" + z.Dir + @"\" + z.Dir + (i + 1) + ".blp");   // "Interface\\WorldMap\\"..mapFileName.."\\"..completeMapFileName..i (:262-265)
                RawImage img = _zoneTile[i];
                img.texture = t;
                img.color = t != null ? Color.white : new Color(0f, 0f, 0f, 0f);
                if (t != null) tiles++;
            }
            List<WowZoneOverlay> ov = WowQuestMap.ZoneOverlays(UiArt.Files, z);
            int n = ov != null ? ov.Count : 0, found = 0;
            for (int i = 0; i < n; i++)
            {
                while (_zoneOver.Count <= i) _zoneOver.Add(UiKit.Pic(_zoneArt, "Overlay " + (_zoneOver.Count + 1), null, 0f, 0f, 1f, 1f));
                RawImage img = _zoneOver[i];
                WowZoneOverlay o = ov[i];
                Texture2D t = UiArt.Tex(o.Path);
                img.texture = t;
                img.color = t != null ? Color.white : new Color(0f, 0f, 0f, 0f);
                if (t != null) found++;
                // "SetTexCoord(0, texturePixelWidth/textureFileWidth, 0, texturePixelHeight/textureFileHeight)" - the Lua's file size is the power of
                // two from 16; 95 of the 1524 overlay tiles of the 3.3.5a files are bigger (probe QuestMapData), so ours divides by the real size.
                UiKit.Coords(img, 0f, t != null && t.width > 0 ? o.W / t.width : o.U, 0f, t != null && t.height > 0 ? o.H / t.height : o.V);
                UiKit.Move(img.rectTransform, o.X * ZoneScale, o.Y * ZoneScale);
                UiKit.Size(img.rectTransform, o.W * ZoneScale, o.H * ZoneScale);
                img.enabled = true;
            }
            for (int i = n; i < _zoneOver.Count; i++) _zoneOver[i].enabled = false;
            if (_zoneLogs++ < 4)
                WowLog.Info("UI quest map: World Map art '" + z.Dir + "' (WorldMapArea " + z.Id + ") - " + tiles + "/12 detail tiles, " + found + "/" + n + " overlay textures (ours: all overlays, as explored).");
        }

        private static int _zoneLogs;

        private void PlaceTiles(in MapView v, WowMapTiles tiles, float scale)
        {
            float x, y, z;
            WowSpace.ToWow(v.MapId, v.Centre, out x, out y, out z);
            int col, row;
            WowMapTiles.TileOf(x, y, out col, out row);
            tiles.Pump(col, row);
            _tiles.localEulerAngles = new Vector3(0f, 0f, v.Yaw);
            float size = WowMapTiles.TileYards * WowSpace.MetresPerYard * scale;
            int i = 0;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++, i++)
                {
                    int tc = col + dc, tr = row + dr;
                    Texture2D t = tiles.Get(tc, tr);
                    RawImage img = _tileImg[i];
                    if (t == null)
                    {
                        if (img.enabled) img.enabled = false;
                        continue;
                    }
                    // The tile's north-west corner: max X = (32 - row) * 533.33, max Y = (32 - col) * 533.33 (yards).
                    Vector3 nw = WowSpace.ToUnity(v.MapId, (32 - tr) * WowMapTiles.TileYards, (32 - tc) * WowMapTiles.TileYards, z);
                    RectTransform r = img.rectTransform;
                    r.anchoredPosition = new Vector2((nw.x - v.Centre.x) * scale, (nw.z - v.Centre.z) * scale);
                    r.sizeDelta = new Vector2(size, size);
                    if (img.texture != t) img.texture = t;
                    if (!img.enabled) img.enabled = true;
                }
            }
        }

        private RawImage Pooled(int i)
        {
            while (_pool.Count <= i)
            {
                RawImage img = Mark(_marks, "Marker " + _pool.Count, _icons, 12f);
                img.enabled = false;
                _pool.Add(img);
            }
            RawImage m = _pool[i];
            if (!m.enabled) m.enabled = true;
            return m;
        }

        private static void SetText(TextMeshProUGUI t, ref string shown, string text)
        {
            if (string.Equals(shown, text)) return;
            shown = text;
            t.text = text;
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Root = null;
        }
    }
}
