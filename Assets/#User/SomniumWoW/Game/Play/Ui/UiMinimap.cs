// lead 2026-10-03 (the user: "put the mini map on the top right"): WoW's MinimapCluster on the desktop HUD.
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.MapApp;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// WoW's minimap at the top right of the desktop HUD, laid out from the install's Interface\FrameXML\Minimap.xml (dumped to
    /// P/harness/out_lead11mm/Minimap.xml): MinimapCluster 192 x 192 TOPRIGHT of UIParent; MinimapBorderTop 192 x 32 TOPRIGHT
    /// (UI-Minimap-Border TexCoords 0.25-1.0 / 0-0.125); MinimapZoneText 150 x 12 (GameFontNormal) in MinimapZoneTextButton at the
    /// cluster's CENTER (7, 83); Minimap 140 x 140, CENTER at the cluster's TOP (9, -92); MinimapBackdrop 192 x 192 at the cluster's
    /// CENTER (0, -20) carrying MinimapBorder (TexCoords 0.25-1.0 / 0.125-0.875); MinimapNorthTag 16 x 16 at the Minimap's CENTER
    /// (0, 67) (shown while the minimap does not rotate, Minimap.lua:365-373 - WoW's default: north up, the arrow turns);
    /// MinimapZoomIn / MinimapZoomOut 32 x 32 at the backdrop's CENTER (71, -20) / (51, -39), HitRectInsets 4 / 4 / 2 / 6, disabled at
    /// the last / first zoom step (Minimap.lua:159-175, sounds igMiniMapZoomIn / igMiniMapZoomOut); the zone text coloured by the zone's
    /// PvP type (Minimap.lua:32-43) with Minimap_SetTooltip's lines (:49-81). Skipped: the clock, tracking, mail, world map, LFG,
    /// battlefield and difficulty buttons (other owners place mail / queue icons left of the cluster).
    /// The map: the round mask is the install's Textures\MinimapMask.blp; the outdoor minimap tiles of the current map come from
    /// WowMapTiles (Textures\Minimap\md5translate.trs - the WoW Map's Classic mode reader, its own instance here), centred on the
    /// player, north up, the player arrow Interface\Minimap\MinimapArrow.blp turned with the facing. Indoors (a WMO with its own
    /// minimap) the outdoor tiles stay: md5translate lists WMO minimaps ("WMO\...\name_NNN_XX_YY.blp", 11485 lines) but their
    /// placement needs the WMO group's bounds and transform, which is not measured here - a known gap.
    /// Zoom: the client has 6 steps (Minimap:GetZoomLevels(), Minimap.lua:92/163); the yards across each step are NOT in FrameXML -
    /// <see cref="ZoomYards"/> is ours. The zoom buttons and the mouse wheel over the map (ours) change it; it is saved
    /// (WowSettings.MinimapZoom, like WoW's minimapZoom CVar).
    /// Blips (sizes and the unit dots are ours; cells as WowMapPanel uses them, probe MapArt): ObjectIcons.blp 8 x 2 cells - a quest
    /// giver with a quest to take (yellow "!", cell 9) or to hand in (yellow "?", cell 10); Interface\Minimap\Tracking\FlightMaster.blp
    /// for flight masters (WoW shows them only with Flight Master tracking - ours: always); hostile / neutral / friendly creatures and
    /// NPCs as red / yellow / green dots (cells 2 / 3 / 4 - WoW shows units only with a tracking spell: ours); group members as
    /// PartyRaidBlips.blp class blips (cell = class - 1), out of range on the rim as Rotating-MinimapGroupArrow; a ghost's corpse as
    /// the world map's corpse icon (WorldMapFrameTemplates.xml:109-110 POIIcons TexCoords), out of range Rotating-MinimapCorpseArrow.
    /// Hovering the map lists the names under the mouse. Performance: its own sub-canvas; the tiles and blips are re-placed at most
    /// <see cref="UpdateInterval"/> apart and only when the player moved half a map unit, the zoom changed or a tile arrived; the
    /// arrow turns every frame (one transform); nothing is allocated per frame (strings only on a zone change or a tooltip).
    /// Desktop only (the VR floating HUD has none).
    /// </summary>
    public sealed class UiMinimap
    {
        public const float ClusterW = 192f, ClusterH = 192f;          // MinimapCluster Size (Minimap.xml)
        private const float MapX = 35f, MapY = 22f, MapSize = 140f;   // Minimap 140 x 140, centre (96 + 9, 92) in the cluster
        private const float MapR = MapSize * 0.5f;

        public const string BorderTex = @"Interface\Minimap\UI-Minimap-Border.blp";
        public const string NorthTex = @"Interface\Minimap\CompassNorthTag.blp";
        public const string MaskTex = @"Textures\MinimapMask.blp";    // the only MinimapMask in the install's listfile (common.MPQ)
        public const string GroupArrowTex = @"Interface\Minimap\ROTATING-MINIMAPGROUPARROW.BLP";
        public const string CorpseArrowTex = @"Interface\Minimap\ROTATING-MINIMAPCORPSEARROW.BLP";
        // WorldMapFrameTemplates.xml WorldMapCorpseTemplate: <TexCoords left="0.56640625" right="0.6328125" top="0.00390625" bottom="0.0703125"/>
        private const float CorpseL = 0.56640625f, CorpseR = 0.6328125f, CorpseT = 0.00390625f, CorpseB = 0.0703125f;

        /// <summary>Ours: yards across the 140-unit map at zoom step 0..5 (the client's 6 steps; the sizes are the table the addon
        /// library Astrolabe uses for outdoor zoom - not in FrameXML, not measured against the client).</summary>
        public static readonly float[] ZoomYards = { 466.6667f, 400f, 333.3333f, 266.6667f, 200f, 133.3333f };
        /// <summary>Ours: the map and blips are re-placed at most this often (seconds).</summary>
        public const float UpdateInterval = 0.2f;
        private const int MaxBlips = 80;                              // ours
        private const float DotSize = 16f, QuestSize = 20f, IconSize = 16f, ArrowSize = 32f, RimArrowSize = 32f;   // ours (cells are 32)
        private const float Margin = 6f;                              // ours: a blip's centre stays this far inside the map edge (the border ring covers its outer ~6 units)

        // Minimap.lua:32-43 (MinimapZoneText:SetTextColor per GetZonePVPInfo)
        private static readonly Color Sanctuary = new Color(0.41f, 0.8f, 0.94f), Hostile = new Color(1f, 0.1f, 0.1f),
            Friendly = new Color(0.1f, 1f, 0.1f), Contested = new Color(1f, 0.7f, 0f);

        public RectTransform Root;
        private readonly UiContext _ctx;
        private readonly WowMapTiles _tiles = new WowMapTiles();
        private readonly RawImage[] _tileImg = new RawImage[9];
        private readonly RawImage[] _pool = new RawImage[MaxBlips];
        private readonly string[] _names = new string[MaxBlips];
        private readonly Vector2[] _at = new Vector2[MaxBlips];
        private readonly float[] _rot = new float[MaxBlips];
        private readonly StringBuilder _tipSb = new StringBuilder(128);
        private RectTransform _map;
        private RawImage _arrow;
        private TextMeshProUGUI _zoneText;
        private UiButton _zoomIn, _zoomOut;
        private Texture2D _icons, _blips, _flight, _poi, _groupArrow, _corpseArrow;
        private WowFiles _files;
        private int _mapId = int.MinValue, _zoom, _areaId = -1, _uploads = -1, _used;
        private WowFaction _faction;
        private float _lastX = float.NaN, _lastY, _arrowYaw = float.NaN, _next;
        private bool _force = true;
        private string _zoneTip;

        /// <summary>The cluster is shown (WowSettings.ShowMinimap): UIParent.lua GetMaxUIPanelsWidth keeps panels left of it.</summary>
        public bool Shown { get { return Root != null && Root.gameObject.activeSelf; } }

        private static UiMinimap _live;

        /// <summary>Lead (2026-10-03): true while the mouse is over the shown round map - the wheel then zooms the minimap, so the
        /// cameras that read the wheel (MountCamera) leave it alone.</summary>
        public static bool OwnsWheel()
        {
            UiMinimap m = _live;
            if (m == null || m.Root == null || !m.Root.gameObject.activeInHierarchy) return false;
            Vector2 at;
            return m.MouseLocal(out at) && at.sqrMagnitude <= MapR * MapR;
        }

        public UiMinimap(UiSurface s, UiContext ctx)
        {
            _live = this;
            _ctx = ctx;
            _zoom = Mathf.Clamp(WowSettings.MinimapZoom, 0, ZoomYards.Length - 1);
            Root = s.Anchor("MinimapCluster", new Vector2(1f, 1f), ClusterW, ClusterH, 0f, 0f);
            Root.gameObject.AddComponent<Canvas>();   // its own sub-canvas: moving tiles / blips rebuild only this
            if (UiKit.Pointable) Root.gameObject.AddComponent<GraphicRaycaster>();   // a nested canvas needs its own (WowMapTabletLink.cs:864)

            Texture2D border = UiArt.Tex(BorderTex);
            UiKit.Pic(Root, "MinimapBorderTop", border, 0.25f, 1f, 0f, 0.125f, 0f, 0f, ClusterW, 32f);
            // MinimapZoneTextButton 150 x 12 centred at (96 + 7, 96 - 83); the text CENTER at its TOP (0, -5) - 16 tall here so TMP keeps the line.
            UiButton zone = UiKit.Button(Root, "MinimapZoneTextButton", 28f, 7f, 150f, 12f);
            zone.Tip = () => _zoneTip;
            _zoneText = UiKit.Text(zone.transform, "MinimapZoneText", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, -3f, 150f, 16f);
            _zoneText.textWrappingMode = TextWrappingModes.NoWrap;   // nonspacewrap="true"
            _zoneText.overflowMode = TextOverflowModes.Ellipsis;    // ours: a long name stays inside the 150 units

            // The map: a round mask over a black ground (ours: where the install has no tile) and the 3 x 3 tiles around the player.
            _map = UiKit.Rect(Root, "Minimap", MapX, MapY, MapSize, MapSize);
            RectTransform clip = UiKit.Rect(_map, "Round", 0f, 0f, MapSize, MapSize);
            RawImage maskImg = clip.gameObject.AddComponent<RawImage>();
            Texture2D mask = UiArt.Tex(MaskTex);
            maskImg.texture = mask;
            maskImg.raycastTarget = false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            UiKit.Solid(clip, "Ground", Color.black, 0f, 0f, MapSize, MapSize);
            for (int i = 0; i < _tileImg.Length; i++)
            {
                RawImage t = UiKit.Pic(clip, "Tile " + i, null, 0f, 0f, 1f, 1f);
                Centre(t.rectTransform, new Vector2(0f, 1f));
                t.uvRect = new UnityEngine.Rect(0f, 1f, 1f, -1f);   // BLP rows are stored top first (PLAN rule 15)
                t.color = Color.white;
                t.enabled = false;
                _tileImg[i] = t;
            }
            // Blips over the map (not masked: a blip on the rim is drawn whole), then the arrow, then the mouse catcher (tooltips, wheel).
            _icons = UiArt.Tex(WowMapPanel.ObjectIconsTex);
            _blips = UiArt.Tex(WowMapPanel.BlipsTex);
            _flight = UiArt.Tex(WowMapPanel.FlightTex);
            _poi = UiArt.Tex(WowMapPanel.PoiIconsTex);
            _groupArrow = UiArt.Tex(GroupArrowTex);
            _corpseArrow = UiArt.Tex(CorpseArrowTex);
            RectTransform blips = UiKit.Rect(_map, "Blips", 0f, 0f, MapSize, MapSize);
            for (int i = 0; i < MaxBlips; i++)
            {
                RawImage b = UiKit.Pic(blips, "Blip " + i, null, 0f, 0f, DotSize, DotSize);
                Centre(b.rectTransform, new Vector2(0.5f, 0.5f));
                b.color = Color.white;
                b.enabled = false;
                _pool[i] = b;
            }
            _arrow = UiKit.Pic(_map, "MinimapArrow", UiArt.Tex(WowMapPanel.ArrowTex), 0f, 0f, ArrowSize, ArrowSize);
            Centre(_arrow.rectTransform, new Vector2(0.5f, 0.5f));
            UiButton catcher = UiKit.Button(_map, "Minimap mouse", 0f, 0f, MapSize, MapSize);
            catcher.Tip = BlipTip;

            // MinimapBackdrop: the round border over the map, the north tag, the zoom buttons.
            RectTransform back = UiKit.Rect(Root, "MinimapBackdrop", 0f, 20f, ClusterW, ClusterH);
            UiKit.Pic(back, "MinimapBorder", border, 0.25f, 1f, 0.125f, 0.875f, 0f, 0f, ClusterW, ClusterH);
            UiKit.Pic(Root, "MinimapNorthTag", UiArt.Tex(NorthTex), 105f - 8f, 92f - 67f - 8f, 16f, 16f);
            _zoomIn = ZoomButton(back, "MinimapZoomIn", WowMapPanel.ZoomInUp, WowMapPanel.ZoomInDown, WowMapPanel.ZoomInDisabled, 96f + 71f, 96f + 20f, 1);
            _zoomIn.Tip = () => "Zoom In";     // ZOOM_IN (GlobalStrings.lua:8998)
            _zoomOut = ZoomButton(back, "MinimapZoomOut", WowMapPanel.ZoomOutUp, WowMapPanel.ZoomOutDown, WowMapPanel.ZoomOutDisabled, 96f + 51f, 96f + 39f, -1);
            _zoomOut.Tip = () => "Zoom Out";   // ZOOM_OUT (GlobalStrings.lua:8999)
            UpdateZoomButtons();

            bool on = WowSettings.ShowMinimap;
            Root.gameObject.SetActive(on);
            WowLog.Info("UI minimap: MinimapCluster 192 x 192 TOPRIGHT (Minimap.xml), map 140 x 140 round (" + MaskTex + (mask != null ? "" : " MISSING - square") + "), zoom step "
                + _zoom + " of 0-5 = " + ZoomYards[_zoom].ToString("0") + " yd across (sizes ours: " + ZoomYards[0].ToString("0") + " .. " + ZoomYards[5].ToString("0")
                + "), outdoor tiles via WowMapTiles (indoors too), blips: quest ! / ?, flight masters, units (ours), group, corpse; re-placed <= " + (1f / UpdateInterval).ToString("0")
                + "/s (ours); art " + (border != null && _icons != null && _blips != null && _flight != null && _poi != null ? "all found" : "MISSING some") + "; " + (on ? "shown" : "hidden (Settings: Show minimap off)") + ".");
        }

        private static void Centre(RectTransform r, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = pivot;
            r.anchoredPosition = Vector2.zero;
        }

        /// <summary>MinimapZoomIn / Out: 32 x 32 centred at (cx, cy) of the backdrop; the clickable part is HitRectInsets 4 / 4 / 2 / 6.</summary>
        private UiButton ZoomButton(Transform parent, string name, string up, string down, string disabled, float cx, float cy, int step)
        {
            float x = cx - 16f, y = cy - 16f;
            UiButton b = UiKit.Button(parent, name, x + 4f, y + 2f, 32f - 8f, 32f - 8f);
            b.Click = () => Zoom(step);
            b.ArtUp = UiArt.Tex(up);
            b.ArtDisabled = UiArt.Tex(disabled);
            b.Art = UiKit.Pic(b.transform, "Up", b.ArtUp, -4f, -2f, 32f, 32f);
            b.Pushed = UiKit.Pic(b.transform, "Down", UiArt.Tex(down), -4f, -2f, 32f, 32f);
            b.Pushed.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(WowMapPanel.ZoomHilite), -4f, -2f, 32f, 32f);
            b.Hilite.enabled = false;
            return b;
        }

        /// <summary>Minimap_ZoomInClick / Minimap_ZoomOutClick (Minimap.lua:159-175): one step, the sound, the buttons at the ends disabled.</summary>
        private void Zoom(int step)
        {
            int z = Mathf.Clamp(_zoom + step, 0, ZoomYards.Length - 1);
            if (z == _zoom) return;
            _zoom = z;
            UiSound.Named(step > 0 ? "igMiniMapZoomIn" : "igMiniMapZoomOut");
            UpdateZoomButtons();
            _force = true;
            WowSettings.MinimapZoom = z;
            WowSettings.NotifyChanged();
        }

        private void UpdateZoomButtons()
        {
            _zoomIn.Interactable = _zoom < ZoomYards.Length - 1;
            _zoomOut.Interactable = _zoom > 0;
        }

        /// <summary>Once a frame from the desktop layout.</summary>
        public void Refresh(float now)
        {
            bool want = WowSettings.ShowMinimap;
            if (Root.gameObject.activeSelf != want)
            {
                Root.gameObject.SetActive(want);
                _force = true;
                WowLog.Info("UI minimap: " + (want ? "shown" : "hidden") + " (Settings: Show minimap).");
            }
            if (!want) return;
            IWowSession s = _ctx.S;
            PlayerState p = s != null ? s.Player : null;
            if (p == null) return;
            int map = s.MapId;
            WowFiles files = UiArt.Files;
            if (map != _mapId || !ReferenceEquals(files, _files))
            {
                _mapId = map;
                _files = files;
                _tiles.SetMap(files, map);
                _areaId = -1;
                _force = true;
            }

            float px, py, pz;
            Vector3 feet;
            if (WowPlayer.TryFeet(out feet)) WowSpace.ToWow(map, feet, out px, out py, out pz);
            else { px = p.Pos.X; py = p.Pos.Y; }
            float yaw;
            string how;
            if (!WowPlayer.TryFacingYaw(out yaw, out how)) yaw = WowSpace.OrientationToYaw(p.Pos.O);
            if (!(Mathf.Abs(Mathf.DeltaAngle(yaw, _arrowYaw)) < 0.5f))
            {
                _arrowYaw = yaw;
                _arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -yaw);   // north up: a Unity yaw turns the arrow clockwise
            }

            int wheel = Wheel();
            if (wheel != 0) Zoom(wheel);
            UpdateZone(p, files);

            int col, row;
            WowMapTiles.TileOf(px, py, out col, out row);
            _tiles.Pump(col, row);
            if (!_force && now < _next) return;
            float scale = MapSize / ZoomYards[_zoom];   // map units per yard
            bool moved = float.IsNaN(_lastX) || Mathf.Abs(px - _lastX) * scale > 0.5f || Mathf.Abs(py - _lastY) * scale > 0.5f;
            if (_force || moved || _tiles.Uploads != _uploads)
            {
                _lastX = px;
                _lastY = py;
                _uploads = _tiles.Uploads;
                PlaceTiles(px, py, col, row, scale);
            }
            PlaceBlips(s, p, map, px, py, scale);
            _force = false;
            _next = now + UpdateInterval;
        }

        // ---------------------------------------------------------------- map

        private void PlaceTiles(float px, float py, int col, int row, float scale)
        {
            float size = WowMapTiles.TileYards * scale;
            int i = 0;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++, i++)
                {
                    int tc = col + dc, tr = row + dr;
                    RawImage img = _tileImg[i];
                    // The tile's north-west corner: X = (32 - row) * 533.33, Y = (32 - col) * 533.33 yards (WowMapTiles); east is -Y.
                    float left = (py - (32 - tc) * WowMapTiles.TileYards) * scale;
                    float top = ((32 - tr) * WowMapTiles.TileYards - px) * scale;
                    bool visible = left < MapR && left + size > -MapR && top > -MapR && top - size < MapR;
                    Texture2D t = visible ? _tiles.Get(tc, tr) : null;
                    if (t == null)
                    {
                        if (img.enabled) img.enabled = false;
                        continue;
                    }
                    RectTransform r = img.rectTransform;
                    r.anchoredPosition = new Vector2(left, top);
                    r.sizeDelta = new Vector2(size, size);
                    if (img.texture != t) img.texture = t;
                    if (!img.enabled) img.enabled = true;
                }
            }
        }

        // ---------------------------------------------------------------- blips

        private void PlaceBlips(IWowSession s, PlayerState p, int map, float px, float py, float scale)
        {
            int used = 0;
            IReadOnlyList<EntityState> list = s.Entities;
            int n = list != null ? list.Count : 0;
            ulong me = p.Guid;
            Vector2 at;
            // 1. units (ours: WoW shows them only with a tracking spell) - creatures, NPCs and the pets; players only as group members.
            for (int i = 0; i < n && used < MaxBlips; i++)
            {
                EntityState e = list[i];
                if (e == null || e.Guid == me || e.Dead) continue;
                if (e.Kind == EntityKind.GameObject || e.Kind == EntityKind.Player || e.Kind == EntityKind.Totem) continue;
                if (IsQuestGiver(e) || IsFlightMaster(e)) continue;   // drawn above the dots
                if (!Inside(e.Pos.X, e.Pos.Y, px, py, scale, out at)) continue;
                int cell = e.Reaction == Reaction.Hostile ? 2 : e.Reaction == Reaction.Neutral ? 3 : 4;
                Put(used++, _icons, Cell(cell, 2), DotSize, at, 0f, e.Name);
            }
            // 2. flight masters, then quest givers (creatures, NPCs and quest objects) on top.
            for (int i = 0; i < n && used < MaxBlips; i++)
            {
                EntityState e = list[i];
                if (e == null || e.Dead || e.Kind == EntityKind.Player || IsQuestGiver(e) || !IsFlightMaster(e)) continue;
                if (!Inside(e.Pos.X, e.Pos.Y, px, py, scale, out at)) continue;
                Put(used++, _flight, Whole, IconSize, at, 0f, e.Name);
            }
            for (int i = 0; i < n && used < MaxBlips; i++)
            {
                EntityState e = list[i];
                if (e == null || e.Dead || e.Kind == EntityKind.Player || !IsQuestGiver(e)) continue;
                if (!Inside(e.Pos.X, e.Pos.Y, px, py, scale, out at)) continue;
                Put(used++, _icons, Cell(e.Marker == QuestMarker.Complete ? 10 : 9, 2), QuestSize, at, 0f, e.Name);
            }
            // 3. the group (online: PlayerState.Group; the live entity when it is in sight, else the last SMSG_PARTY_MEMBER_STATS position).
            List<GroupMemberView> group = p.Group;
            for (int i = 0; group != null && i < group.Count && used < MaxBlips; i++)
            {
                GroupMemberView m = group[i];
                if (m == null || !m.Online || m.MapId != map) continue;
                EntityState e = m.Guid != 0 ? s.Find(m.Guid) : null;
                float x = e != null ? e.Pos.X : m.Pos.X, y = e != null ? e.Pos.Y : m.Pos.Y;
                if (e == null && x == 0f && y == 0f) continue;   // no position yet
                if (Inside(x, y, px, py, scale, out at)) Put(used++, _blips, Cell(Mathf.Clamp((int)m.Class - 1, 0, 31), 4), DotSize, at, 0f, m.Name);
                else Put(used++, _groupArrow, Whole, RimArrowSize, Rim(at), RimAngle(at), m.Name);
            }
            // 4. a ghost's corpse.
            if (p.Ghost && p.HasCorpse && p.CorpseMapId == map && used < MaxBlips)
            {
                if (Inside(p.CorpsePos.X, p.CorpsePos.Y, px, py, scale, out at)) Put(used++, _poi, CorpseUv, IconSize, at, 0f, "Corpse");   // CORPSE (GlobalStrings.lua:1865)
                else Put(used++, _corpseArrow, Whole, RimArrowSize, Rim(at), RimAngle(at), "Corpse");
            }
            for (int i = used; i < _used; i++)
            {
                if (_pool[i].enabled) _pool[i].enabled = false;
                _names[i] = null;
            }
            _used = used;
        }

        /// <summary>WoW (x, y) yards -> map units from the centre (x east, y north); true when inside the rim.</summary>
        private static bool Inside(float x, float y, float px, float py, float scale, out Vector2 at)
        {
            at = new Vector2((py - y) * scale, (x - px) * scale);
            float r = MapR - Margin;
            return at.sqrMagnitude <= r * r;
        }

        private static Vector2 Rim(Vector2 at)
        {
            float d = at.magnitude;
            return d > 1e-3f ? at * ((MapR - 12f) / d) : new Vector2(0f, MapR - 12f);   // ours: 12 units inside the map edge, clear of the border ring
        }

        private static float RimAngle(Vector2 at)
        {
            return Mathf.Atan2(at.y, at.x) * Mathf.Rad2Deg - 90f;   // the arrow art points up
        }

        private void Put(int i, Texture2D tex, UnityEngine.Rect uv, float size, Vector2 at, float angle, string name)
        {
            RawImage img = _pool[i];
            if (img.texture != tex) img.texture = tex;
            if (img.uvRect != uv) img.uvRect = uv;
            RectTransform r = img.rectTransform;
            r.anchoredPosition = at;
            if (r.sizeDelta.x != size) r.sizeDelta = new Vector2(size, size);
            if (_rot[i] != angle)
            {
                _rot[i] = angle;
                r.localEulerAngles = new Vector3(0f, 0f, angle);
            }
            _names[i] = name;
            _at[i] = at;
            bool show = tex != null;
            if (img.enabled != show) img.enabled = show;
        }

        private static readonly UnityEngine.Rect Whole = new UnityEngine.Rect(0f, 1f, 1f, -1f);
        private static readonly UnityEngine.Rect CorpseUv = new UnityEngine.Rect(CorpseL, CorpseB, CorpseR - CorpseL, CorpseT - CorpseB);

        /// <summary>A 32 x 32 cell of an 8-wide sheet (ObjectIcons 8 x 2, PartyRaidBlips 8 x 4), as uvRect (v flipped like UiKit.Coords).</summary>
        private static UnityEngine.Rect Cell(int cell, int rows)
        {
            int c = cell % 8, r = cell / 8;
            float l = c / 8f, t = r / (float)rows, b = (r + 1) / (float)rows;
            return new UnityEngine.Rect(l, b, 1f / 8f, t - b);
        }

        private static bool IsQuestGiver(EntityState e)
        {
            return e.Marker == QuestMarker.Available || e.Marker == QuestMarker.Complete;   // ObjectIcons has no grey "?" (in progress): not shown
        }

        /// <summary>A flight master: SMSG_TAXINODE_STATUS answered for it, or the titles WowTabletMap knows (offline SubName "Flight Master").</summary>
        private static bool IsFlightMaster(EntityState e)
        {
            if (e.TaxiStatus >= 0) return true;
            string t = e.SubName;
            if (string.IsNullOrEmpty(t)) return false;
            return t == "Flight Master" || t == "Gryphon Master" || t == "Wind Rider Master" || t == "Hippogryph Master" || t == "Bat Handler" || t == "Dragonhawk Master";
        }

        // ---------------------------------------------------------------- mouse

        /// <summary>The mouse in map units from the centre (x right, y up); false in VR / without the old Input manager.</summary>
        private bool MouseLocal(out Vector2 at)
        {
            at = Vector2.zero;
            if (WowPlayer.XrPresent) return false;
            try
            {
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_map, Input.mousePosition, null, out local)) return false;
                at = new Vector2(local.x - MapR, local.y + MapR);   // the map rect's pivot is its top-left
                return true;
            }
            catch (Exception) { return false; }   // the project may run the new Input System only
        }

        /// <summary>Ours (WoW 3.3.5a's minimap has no wheel zoom): the wheel over the round map, +1 = in.</summary>
        private int Wheel()
        {
            float y;
            try { y = Input.mouseScrollDelta.y; }
            catch (Exception) { return 0; }
            if (Mathf.Abs(y) < 0.01f) return 0;
            Vector2 at;
            if (!MouseLocal(out at) || at.sqrMagnitude > MapR * MapR) return 0;
            return y > 0f ? 1 : -1;
        }

        /// <summary>The names of the blips under the mouse (the surface re-asks twice a second while hovered).</summary>
        private string BlipTip()
        {
            Vector2 m;
            if (!MouseLocal(out m)) return null;
            _tipSb.Length = 0;
            for (int i = 0; i < _used; i++)
            {
                string name = _names[i];
                if (string.IsNullOrEmpty(name) || !_pool[i].enabled) continue;
                if ((_at[i] - m).sqrMagnitude > 8f * 8f) continue;   // ours: within 8 units
                if (_tipSb.Length > 0) _tipSb.Append('\n');
                _tipSb.Append(name);
            }
            return _tipSb.Length > 0 ? _tipSb.ToString() : null;
        }

        // ---------------------------------------------------------------- zone text

        /// <summary>
        /// GetMinimapZoneText (the subzone, else the zone) from the area the player stands in (WowMount.Place: the terrain / WMO area,
        /// 5 Hz), else the map's name; the colour by GetZonePVPInfo's type - ours from AreaTable.dbc as AzerothCore reads it
        /// (DBCEnums.h:241/245 AREA_FLAG_ARENA 0x80, AREA_FLAG_SANCTUARY 0x800; :56-58 the zone's team 2 Alliance / 4 Horde / 0 none =
        /// contested; DBCStructure.h:533-537 Ebon Hold (609) is a sanctuary); instances keep the normal colour (ours).
        /// </summary>
        private void UpdateZone(PlayerState p, WowFiles files)
        {
            PlayerPlace place = WowMount.Place;
            int area = place.Known ? place.AreaId : 0;
            if (area == _areaId && p.Faction == _faction) return;
            _areaId = area;
            _faction = p.Faction;
            string zone = null, sub = null;
            int flags = 0, team = -1;
            try
            {
                DbcTable at = Dbc.Load(files, "AreaTable");
                int r = at != null && area > 0 ? at.FindRow(area) : -1;
                if (r >= 0)
                {
                    sub = at.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang);
                    flags = at.GetInt(r, DbcFields.AreaTable.Flags);
                    int z = r;
                    for (int guard = 0; guard < 8; guard++)
                    {
                        int parent = at.GetInt(z, DbcFields.AreaTable.ParentAreaID);
                        int pr = parent > 0 ? at.FindRow(parent) : -1;
                        if (pr < 0) break;
                        z = pr;
                    }
                    zone = at.GetLocalizedString(z, DbcFields.AreaTable.AreaName_lang);
                    flags |= at.GetInt(z, DbcFields.AreaTable.Flags);
                    team = at.GetInt(z, DbcFields.AreaTable.FactionGroupMask);
                }
                int instance = 0;
                DbcTable mapT = Dbc.Load(files, "Map");
                int mr = mapT != null ? mapT.FindRow(_mapId) : -1;
                if (mr >= 0)
                {
                    instance = mapT.GetInt(mr, DbcFields.Map.InstanceType);
                    if (zone == null) zone = sub = mapT.GetLocalizedString(mr, DbcFields.Map.MapName_lang);
                }
                if (instance != 0) team = -1;
            }
            catch (Exception e)
            {
                WowLog.Warn("UI minimap: zone " + area + " unreadable: " + e.Message);
            }

            Color c = UiArt.Gold;   // NORMAL_FONT_COLOR (no PvP type)
            string territory = null;
            string ours = _faction == WowFaction.Horde ? "Horde" : "Alliance";
            if ((flags & 0x800) != 0 || _mapId == 609) { c = Sanctuary; territory = "(Sanctuary)"; }               // SANCTUARY_TERRITORY 6177
            else if ((flags & 0x80) != 0) { c = Hostile; territory = "(PvP Area)"; }                              // FREE_FOR_ALL_TERRITORY 3726
            else if (team == 2 || team == 4)
            {
                string owner = team == 2 ? "Alliance" : "Horde";                                                  // FACTION_ALLIANCE / FACTION_HORDE 3640 / 3642
                c = owner == ours ? Friendly : Hostile;
                territory = "(" + owner + " Territory)";                                                          // FACTION_CONTROLLED_TERRITORY 3641
            }
            else if (team == 0) { c = Contested; territory = "(Contested Territory)"; }                          // CONTESTED_TERRITORY 1847
            string text = sub ?? zone ?? "";
            if (_zoneText.text != text) _zoneText.text = text;
            _zoneText.color = c;
            // Minimap_SetTooltip: the zone in white, the subzone (when another name) and the territory in the PvP colour.
            if (string.IsNullOrEmpty(zone)) { _zoneTip = null; return; }
            string hex = ColorUtility.ToHtmlStringRGB(c);
            var sb = new StringBuilder();
            sb.Append("<color=#ffffff>").Append(zone).Append("</color>");
            if (!string.IsNullOrEmpty(sub) && sub != zone) sb.Append("\n<color=#").Append(hex).Append('>').Append(sub).Append("</color>");
            if (territory != null) sb.Append("\n<color=#").Append(hex).Append('>').Append(territory).Append("</color>");
            _zoneTip = sb.ToString();
        }
    }
}
