using System;
using System.Collections.Generic;
using System.Threading;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.MapApp;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The "WoW Map" (iteration 5, owner Map; WowGame.Awake adds it). A live overhead map of the WoW world around the player:
    ///  - on the Somnium tablet: a "WoW Map" app button first in the home grid (WowMapTabletLink, the ETS tablet link ported),
    ///    opening the map on the tablet's screen (Tablet.XR and Tablet.NonXR);
    ///  - VR: a world window opened from the palm menu's World Map button (placed above the palm like the other palm windows);
    ///  - desktop: a screen window toggled with N (free: only the client's XR Device Simulator controls and a 3rd-party car
    ///    controller bind it) or the World Map micro button next to the WoW micro buttons.
    /// Every host shows the same view: the live camera picture (WowMapCamera: orthographic, 12 renders/s, only while a map
    /// is visible) or the game's classic minimap tiles (WowMapTiles), with markers: you (the minimap arrow, turned with your
    /// heading), the other Somnium players (party blips in class colours, where Gear draws them), quest givers (! / ?),
    /// flight masters, hostile creatures (red dots), your pet, your target ringed; zone name and WoW map coordinates on top
    /// (AreaTable via the ADT area ids, WorldMapArea.dbc bounds). Zoom 60 m .. 600 m across, rotate with heading, recenter.
    /// Iteration 11 quest map: a third view, "World Map" - WoW's zone map art with the quests' POI icons, the selected quest's blob and the
    /// zone's quest list (WowQuestMap); the quest log's Show Map and the tracker's POI buttons open it (OpenQuestMap).
    /// Order 30500: after GearShift (30000) has moved the remote avatars into our frame, so they are read where they are drawn.
    /// </summary>
    [DefaultExecutionOrder(30500)]
    public sealed class WowTabletMap : MonoBehaviour
    {
        public static WowTabletMap Instance { get; private set; }

        /// <summary>Desktop key for the map window (verified free: see the class summary).</summary>
        public const KeyCode MapKey = KeyCode.N;

        /// <summary>Visible width of the map in metres, per zoom step.</summary>
        public static readonly float[] ZoomSteps = { 60f, 100f, 160f, 250f, 400f, 600f };

        public const string MapIconTex = @"Interface\WorldMap\WorldMap-Icon.blp";
        public const string MicroWorldUp = @"Interface\Buttons\UI-MicroButton-World-Up.blp";
        public const string MicroWorldDown = @"Interface\Buttons\UI-MicroButton-World-Down.blp";

        /// <summary>VR world window size: 640 x 720 units at this many metres per unit (0.58 x 0.65 m).</summary>
        public const float WindowMetresPerUnit = 0.0009f;
        /// <summary>Desktop window: the 640 x 720 panel at this scale of WoW's 768-unit-tall UI.</summary>
        public const float DesktopScale = 0.72f;

        // Lab only (the render lab has no session): files, map and markers to show.
        public static WowFiles LabFiles;
        public static int LabMap = -1;
        public static readonly List<MapMark> LabMarks = new List<MapMark>();

        /// <summary>For per-camera work elsewhere (ground clutter): true for the map's own camera.</summary>
        public static bool IsMapCamera(Camera cam)
        {
            return WowMapCamera.IsMapCamera(cam);
        }

        /// <summary>The palm menu's World Map button (VR): toggles the map window above the palm.</summary>
        public static void TogglePalmWindow(Transform palm)
        {
            if (Instance != null) Instance.ToggleVrWindow(palm);
        }

        /// <summary>Desktop: toggles the map window (N, the World Map micro button).</summary>
        public static void ToggleDesktopWindow()
        {
            if (Instance != null) Instance.ToggleDesk("the world");
        }

        /// <summary>Iteration 11 (uicore H16): the desktop map window is open - the registry's External WorldMap spec reads it (its key N and
        /// its micro button live in uicore's registry / UiMicroMenu now).</summary>
        public static bool DesktopOpen { get { return Instance != null && Instance._deskOpen; } }

        /// <summary>Iteration 11 (uicore H16): opens / closes the desktop map window (the registry: key N, the World Map micro button, Escape).</summary>
        public static void SetDesktopWindow(bool open, string why)
        {
            if (Instance != null) Instance.SetDesk(open, why);
        }

        /// <summary>
        /// Iteration 11 quest map: WorldMap_OpenToQuest's ShowUIPanel(WorldMapFrame) (WorldMapFrame.lua:1964-1976) - the map in its World Map view,
        /// opened on the desktop, as the VR window in front of the player (VR), or shown on the tablet when its WoW Map app is open.
        /// </summary>
        public static void OpenQuestMap(string why)
        {
            if (Instance == null) return;
            Instance._zoneView = true;
            if (WowPlayer.XrPresent) { if (!Instance._vrOpen) { Instance._vrOpen = true; Instance._vrPlaced = false; Instance._vrOpenedFrom = null; } }
            else Instance.SetDesk(true, why);
            WowLog.Info("UI quest map: World Map view opened for " + why + ".");
        }

        /// <summary>Iteration 11 quest map: a map window (desktop, VR) or the tablet's WoW Map app is on screen (WatchFrameQuestPOI_OnClick's WorldMapFrame:IsShown()).</summary>
        public static bool AnyShown { get { return Instance != null && Instance._shown; } }

        /// <summary>Iteration 11 quest map: closes the map windows (WatchFrameQuestPOI_OnClick: HideUIPanel(WorldMapFrame) on the selected quest's POI).</summary>
        public static void CloseWindows(string why)
        {
            if (Instance == null) return;
            Instance.SetDesk(false, why);
            if (Instance._vrOpen) { Instance._vrOpen = false; WowLog.Info("Map: VR map window closed (" + why + ")."); }
        }

        private bool _shown;

        private WowMapTabletLink _link;
        private WowMapCamera _camera;
        private readonly WowMapTiles _tiles = new WowMapTiles();
        private readonly List<MapMark> _marks = new List<MapMark>();
        private MapView _view;

        private int _zoom = 3;
        private bool _rotate;
        private bool _live = true;
        private bool _zoneView;                 // iteration 11 quest map: WoW's World Map art of a zone with the quest POIs (View: World Map)
        private Vector3 _pan;
        private readonly List<QuestMapEntry> _quests = new List<QuestMapEntry>(), _questPool = new List<QuestMapEntry>();
        private bool _questMapLogged;

        private WowMapPanel _tabletPanel, _vrPanel, _deskPanel, _labPanel;
        private UiSurface _vrSurface, _deskSurface;
        private RectTransform _deskWindow;
        private bool _vrOpen, _deskOpen, _vrPlaced;
        private Transform _vrOpenedFrom;
        private bool _wasInPlay;
        private bool _iconTried;
        private Texture2D _icon;
        private bool _keysBroken;
        private bool _cursorFreed;
        private bool _rebaseHooked;
        private int _vrPlacements;
        private bool _loggedReady;

        // Zone name + coordinates (area read on a pool thread every second).
        private readonly WowAreaLookup _areaLookup = new WowAreaLookup();
        private volatile bool _areaBusy;
        private volatile string _areaName;
        private volatile int _zoneId;           // WorldMapArea ID of the player's zone (iteration 11 quest map: its bounds give the coordinates), 0 = none
        private float _nextArea;
        private int _areaMap = -1;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
            _camera = new WowMapCamera(transform);
            try
            {
                _link = GetComponent<WowMapTabletLink>();
                if (_link == null) _link = gameObject.AddComponent<WowMapTabletLink>();
                _link.AppShown += OnTabletApp;
            }
            catch (Exception e)
            {
                WowLog.Warn("Map: the tablet link could not start (" + e.Message + ") - the map opens in its own window only.");
            }
        }

        private void OnEnable()
        {
            _camera.Hook(true);
            if (!_rebaseHooked)
            {
                WowSpace.Rebased += OnRebased;
                _rebaseHooked = true;
            }
        }

        private void OnDisable()
        {
            _camera.Hook(false);
            if (_rebaseHooked)
            {
                WowSpace.Rebased -= OnRebased;
                _rebaseHooked = false;
            }
            FreeCursor(false);
        }

        private void OnDestroy()
        {
            _camera.Dispose();
            _tiles.Clear();
            if (_vrSurface != null) UiVrLayout.ExtraSurfaces.Remove(_vrSurface);
            if (_icon != null) Destroy(_icon);
            FreeCursor(false);
            if (Instance == this) Instance = null;
        }

        private void OnTabletApp(bool shown)
        {
            if (shown) _vrOpen = false;   // one map at a time in VR: the tablet's
        }

        private void OnRebased(Vector3 delta)
        {
            if (_vrSurface != null && _vrSurface.Shown) _vrSurface.Root.position -= delta;
        }

        // ------------------------------------------------------------------ the panel's buttons

        public bool CanZoomIn { get { return _zoom > 0 && !_zoneView; } }
        public bool CanZoomOut { get { return _zoom < ZoomSteps.Length - 1 && !_zoneView; } }

        public void ZoomIn() { if (_zoom > 0 && !_zoneView) _zoom--; }
        public void ZoomOut() { if (_zoom < ZoomSteps.Length - 1 && !_zoneView) _zoom++; }
        public void ToggleRotate() { if (!_zoneView) _rotate = !_rotate; }

        /// <summary>View: Live -> Classic -> World Map (iteration 11 quest map: WoW's zone map with the quest POIs) -> Live.</summary>
        public void ToggleMode()
        {
            if (_zoneView) { _zoneView = false; _live = true; }
            else if (_live) _live = false;
            else { _zoneView = true; WowQuestMap.ShownZone = 0; }
            WowLog.Info("Map: " + (_zoneView ? "World Map view (the zone's map art, quest POIs)" : _live ? "live camera view" : "classic minimap tiles") + ".");
        }

        /// <summary>Recenter: follow the player again; in the World Map view, back to the player's zone.</summary>
        public void Recenter() { _pan = Vector3.zero; WowQuestMap.ShownZone = 0; }

        /// <summary>Iteration 11 quest map: a quest row of the World Map view (WorldMapQuestFrame_OnMouseUp -> WorldMapFrame_SelectQuestFrame).</summary>
        public void SelectQuest(int questId) { if (questId > 0) WowQuestMap.Select(questId); }

        /// <summary>A click on the map at (x from the left, y from the top) 0..1: look there.</summary>
        public void PanTo(Vector2 f)
        {
            if (_zoneView)
            {
                // WorldMapQuestPOI_OnClick (WorldMapFrame.lua:1842-1855): a POI icon selects its quest. Ours: the nearest icon within its radius.
                Vector2 at = new Vector2((f.x - 0.5f) * WowMapPanel.MapSize, (0.5f - f.y) * WowMapPanel.MapSize);
                int best = 0;
                float bestD = WowQuestMap.IconUnits * 0.5f;
                for (int i = 0; i < _quests.Count; i++)
                {
                    QuestMapEntry e = _quests[i];
                    if (!e.HasIcon || _view.ZoneInfo == null) continue;
                    float dist = (WowMapPanel.ZonePoint(_view, e.IconX, e.IconY) - at).magnitude;
                    if (dist < bestD) { bestD = dist; best = e.QuestId; }
                }
                if (best > 0) WowQuestMap.Select(best);
                return;
            }
            float scale = (WowMapPanel.MapSize * 0.5f) / Mathf.Max(1f, _view.Half);
            float sx = (f.x - 0.5f) * WowMapPanel.MapSize, sy = (0.5f - f.y) * WowMapPanel.MapSize;
            float s = Mathf.Sin(_view.Yaw * Mathf.Deg2Rad), c = Mathf.Cos(_view.Yaw * Mathf.Deg2Rad);
            var d = new Vector3((sx * c + sy * s) / scale, 0f, (-sx * s + sy * c) / scale);
            _pan += d;
            if (_pan.magnitude > 2000f) _pan = _pan.normalized * 2000f;
        }

        // ------------------------------------------------------------------ per frame

        private WowFiles Files
        {
            get
            {
                if (LabFiles != null) return LabFiles;
                WowGame g = WowGame.Current;
                if (g != null && g.Files != null) return g.Files;
                return WowPlay.Files;
            }
        }

        private void Update()
        {
            WowFiles files = Files;
            if (files != null && UiArt.Files == null) UiArt.Files = files;   // the same WowFiles the UI uses (no cache reset)
            if (files != null && !_iconTried) PaintIcon(files);
            Keys();
        }

        private void LateUpdate()
        {
            try { Frame(); }
            catch (Exception e) { LogOnce("frame", e); }
        }

        private void Frame()
        {
            float now = Time.unscaledTime;
            WowFiles files = Files;
            IWowSession session = WowPlay.Session;
            bool inPlay = files != null && (session != null || LabMap >= 0);
            int map = LabMap >= 0 ? LabMap : session != null ? session.MapId : -1;
            bool xr = WowPlayer.XrPresent;

            if (inPlay != _wasInPlay)
            {
                _wasInPlay = inPlay;
                if (!inPlay) { _vrOpen = false; SetDesk(false, "left the world"); _pan = Vector3.zero; }
            }

            if (!_loggedReady && files != null)
            {
                _loggedReady = true;
                WowLog.Info("Map: WoW Map ready - tablet app 'WoW Map' (" + (_link != null ? "searching for the client's tablet" : "no tablet link") +
                            "), VR palm menu World Map button, desktop key " + MapKey + " / World Map micro button; zoom " + ZoomSteps[0] + ".." + ZoomSteps[ZoomSteps.Length - 1] + " m.");
            }

            // Hosts.
            bool tablet = _link != null && _link.IsLinked && _link.AppOpen && _link.TabletOpen;
            if (tablet && files != null && (_tabletPanel == null || !_tabletPanel.Alive) && _link.Frame != null)
            {
                UiKit.Surface = null;
                _tabletPanel = WowMapPanel.Build(_link.Frame, WowMapTabletLink.Owned + "Map", this, false, null);
                WowLog.Info("Map: panel built on the tablet (" + (_link.IsDesktopTablet ? "desktop" : "XR") + " tablet).");
            }
            bool vr = _vrOpen && xr && inPlay && !tablet;
            bool desk = _deskOpen && !xr && inPlay;
            EnsureVrWindow(vr, files);
            EnsureDesk(!xr && inPlay, files);
            if (_vrSurface != null) _vrSurface.SetShown(vr);
            if (_deskWindow != null && _deskWindow.gameObject.activeSelf != desk) _deskWindow.gameObject.SetActive(desk);
            FreeCursor(desk);
            bool lab = _labPanel != null && _labPanel.Alive;
            bool visible = (tablet && _tabletPanel != null && _tabletPanel.Alive) || (vr && _vrPanel != null) || (desk && _deskPanel != null) || lab;
            _shown = visible && !lab;

            // The view.
            Vector3 feet;
            bool haveFeet = WowPlayer.TryFeet(out feet);
            float facing, yaw;
            string how;
            if (!WowPlayer.TryFacingYaw(out facing, out how)) facing = 0f;
            yaw = _rotate ? facing : 0f;
            float half = ZoomSteps[_zoom] * 0.5f;
            _view = new MapView
            {
                InPlay = inPlay,
                Centre = feet + _pan,
                Yaw = yaw,
                Half = half,
                HaveFeet = haveFeet,
                Feet = feet,
                Facing = facing,
                Live = _live,
                Rotate = _rotate,
                Panned = _pan.sqrMagnitude > 0.01f,
                CanZoomIn = CanZoomIn,
                CanZoomOut = CanZoomOut,
                MapId = map,
                ZoomText = _zoneView ? "" : ZoomSteps[_zoom].ToString("0") + " m",
                ZoneView = _zoneView,
            };

            // Live camera: only while a map with the live view is on screen.
            _camera.Tick(visible && inPlay && haveFeet && _live && !_zoneView, _view.Centre, yaw, half, feet.y, now);

            // Iteration 11 quest map: the player's zone is kept while the map is hidden too (the tracker numbers its quests' POIs by it).
            if (inPlay && haveFeet) { UpdateArea(files, map, feet, now); WowQuestMap.PlayerZone = _zoneId; }
            else if (!inPlay) WowQuestMap.PlayerZone = 0;

            if (!visible)
            {
                return;
            }

            PlayerState player = session != null ? session.Player : null;
            if (inPlay && haveFeet)
            {
                float x, y, z;
                WowSpace.ToWow(map, feet, out x, out y, out z);
                _view.Zone = _areaName ?? "";
                WowZoneInfo pz = WowQuestMap.Zone(files, WowQuestMap.PlayerZone);
                float pu = 0f, pv = 0f;
                if (pz != null) pz.ToUv(x, y, out pu, out pv);
                _view.Coords = pz != null && pz.Left != pz.Right && pz.Top != pz.Bottom
                    ? (pu * 100f).ToString("0.0") + ", " + (pv * 100f).ToString("0.0")
                    : x.ToString("0") + ", " + y.ToString("0") + " yd";
                if (!_live && !_zoneView) _tiles.SetMap(files, map);
                Gather(session, map, feet);
            }
            else
            {
                _marks.Clear();
            }

            // Iteration 11 quest map: the quests with POIs of the shown zone (World Map view) or of the player's zone (live / classic).
            int zone = _zoneView ? (WowQuestMap.ShownZone > 0 ? WowQuestMap.ShownZone : WowQuestMap.PlayerZone) : WowQuestMap.PlayerZone;
            _view.ZoneInfo = _zoneView ? WowQuestMap.Zone(files, zone) : null;
            _view.ZoneIsPlayers = zone == WowQuestMap.PlayerZone;
            if (_view.ZoneInfo != null) _view.Zone = _view.ZoneInfo.Name ?? _view.Zone;
            if (_zoneView && !_view.ZoneIsPlayers) _view.Coords = "";
            WowQuestMap.Build(player, inPlay ? zone : 0, _quests, _questPool);
            if (_zoneView && _quests.Count > 0)
            {
                bool found = false;
                for (int i = 0; i < _quests.Count && !found; i++) if (_quests[i].QuestId == WowQuestMap.SelectedQuestId) found = true;
                if (!found) WowQuestMap.Select(_quests[0].QuestId);                // WorldMapFrame_DisplayQuests: "select the first quest"
            }
            _view.SelectedQuest = WowQuestMap.SelectedQuestId;
            if (!_questMapLogged && _zoneView)
            {
                _questMapLogged = true;
                WowLog.Info("UI quest map: World Map view - zone " + zone + " '" + (_view.ZoneInfo != null ? _view.ZoneInfo.Dir : "none") + "', " + _quests.Count + " quest(s) with POIs"
                            + (player != null && player.QuestLog.Count > 0 && player.QuestLog[0].Pois == null ? " (no POI data: offline, or the server has not answered)" : "") + ".");
            }

            if (tablet && _tabletPanel != null) _tabletPanel.Refresh(_view, _camera, _tiles, _marks, _quests);
            if (vr && _vrPanel != null)
            {
                PlaceVrWindow();
                _vrPanel.Refresh(_view, _camera, _tiles, _marks, _quests);
            }
            if (desk && _deskPanel != null) _deskPanel.Refresh(_view, _camera, _tiles, _marks, _quests);
            if (lab) _labPanel.Refresh(_view, _camera, _tiles, _marks, _quests);
        }

        // ------------------------------------------------------------------ markers

        private void Gather(IWowSession session, int map, Vector3 feet)
        {
            _marks.Clear();
            float range = _view.Half * 1.6f + _pan.magnitude;
            float range2 = range * range;
            if (session != null)
            {
                IReadOnlyList<EntityState> list = session.Entities;
                PlayerState p = session.Player;
                ulong target = session.TargetGuid;
                ulong me = p != null ? p.Guid : 0;
                ulong pet = p != null ? p.PetGuid : 0;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    EntityState e = list[i];
                    if (e == null || e.Kind == EntityKind.GameObject) continue;
                    Vector3 pos = WowSpace.ToUnity(map, e.Pos.X, e.Pos.Y, e.Pos.Z);
                    float dx = pos.x - feet.x, dz = pos.z - feet.z;
                    bool isTarget = e.Guid == target && target != 0;
                    if (dx * dx + dz * dz > range2 && !isTarget) continue;
                    MapMarkKind kind;
                    if (e.Marker == QuestMarker.Available) kind = MapMarkKind.QuestAvailable;
                    else if (e.Marker == QuestMarker.Complete) kind = MapMarkKind.QuestComplete;
                    else if (e.Marker == QuestMarker.Incomplete) kind = MapMarkKind.QuestIncomplete;
                    else if (IsFlightMaster(e)) kind = MapMarkKind.FlightMaster;
                    else if (e.Dead && !isTarget) continue;
                    else if (e.Reaction == Reaction.Hostile) kind = MapMarkKind.Hostile;
                    else if (e.Guid == pet || ((e.Kind == EntityKind.Pet || e.Kind == EntityKind.Totem) && e.OwnerGuid == me && me != 0)) kind = MapMarkKind.Friendly;
                    else if (isTarget) kind = MapMarkKind.Friendly;
                    else continue;
                    _marks.Add(new MapMark { Kind = kind, Pos = pos, Target = isTarget });
                }
            }
            // Stage O2 (integrator, ghost request 4): a ghost's corpse, kept on the map edge like party members (WoW's world map shows it).
            PlayerState me0 = session != null ? session.Player : null;
            if (me0 != null && me0.Ghost && me0.HasCorpse && me0.CorpseMapId == map)
                _marks.Add(new MapMark { Kind = MapMarkKind.Corpse, Pos = WowSpace.ToUnity(map, me0.CorpsePos.X, me0.CorpsePos.Y, me0.CorpsePos.Z) });
            GatherParty(map);
            for (int i = 0; i < LabMarks.Count; i++) _marks.Add(LabMarks[i]);
        }

        /// <summary>Flight masters: the offline session's (SubName "Flight Master", entry 900000 + TaxiNodes id) and the game's own titles.</summary>
        private static bool IsFlightMaster(EntityState e)
        {
            string s = e.SubName;
            if (string.IsNullOrEmpty(s)) return false;
            return s == "Flight Master" || s == "Gryphon Master" || s == "Wind Rider Master" || s == "Hippogryph Master" || s == "Bat Handler" || s == "Dragonhawk Master";
        }

        /// <summary>
        /// The other Somnium players Gear shows (WowParty: Distance finite = drawn, same map), where Gear draws them: their body
        /// root after GearShift moved it into our frame (this runs at order 30500, GearShift at 30000).
        /// </summary>
        private void GatherParty(int map)
        {
            List<WowPartyMember> members = WowParty.Members;
            if (members.Count == 0) return;
            List<ISomniumPlayer> all = null;
            try { all = SomniumBridge.PlayersContainer != null ? SomniumBridge.PlayersContainer.AllPlayers : null; }   // as WowNet (server-proven API)
            catch (Exception) { return; }
            if (all == null) return;
            for (int i = 0; i < members.Count; i++)
            {
                WowPartyMember m = members[i];
                if (m.MapId != map || m.Distance == float.MaxValue) continue;
                Transform root = null;
                try
                {
                    for (int j = 0; j < all.Count && root == null; j++)
                    {
                        ISomniumPlayer p = all[j];
                        if (p == null || p.Properties == null || p.Properties.IsLocal || p.Properties.PhotonId != m.PlayerId) continue;
                        root = p.References != null && p.References.Body != null ? p.References.Body.Root : null;
                    }
                }
                catch (Exception) { root = null; }
                if (root == null) continue;
                _marks.Add(new MapMark { Kind = MapMarkKind.Party, Pos = root.position, Class = (int)m.Class });
            }
        }

        // ------------------------------------------------------------------ zone name + coordinates

        private void UpdateArea(WowFiles files, int map, Vector3 feet, float now)
        {
            if (map != _areaMap)
            {
                _areaMap = map;
                _areaName = null;
                _zoneId = 0;
            }
            if (now < _nextArea || _areaBusy || files == null) return;
            _nextArea = now + 1f;
            float x, y, z;
            WowSpace.ToWow(map, feet, out x, out y, out z);
            _areaBusy = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int id = _areaLookup.AreaAt(files, map, x, y);
                    string n = WowAreaLookup.AreaName(files, id);
                    if (n != null) _areaName = n;
                    _zoneId = WowQuestMap.ZoneOfArea(files, map, id);   // iteration 11 quest map (the old ZoneBounds, now a WorldMapArea ID)
                }
                catch (Exception)
                {
                    // keeps the last zone
                }
                finally
                {
                    _areaBusy = false;
                }
            });
        }

        // ------------------------------------------------------------------ the tablet icon

        private void PaintIcon(WowFiles files)
        {
            _iconTried = true;
            try
            {
                byte[] blp = files.Read(MapIconTex);
                if (blp == null)
                {
                    WowLog.Warn("Map: " + MapIconTex + " missing - the tablet button has no picture.");
                    return;
                }
                BlpImage img = BlpImage.Decode(blp);
                byte[] px = img.GetRgba32(0);
                int w = img.Width, h = img.Height, row = w * 4;
                var flipped = new byte[px.Length];   // BLP rows top first -> Texture2D rows bottom first (a Sprite is not flipped)
                for (int y = 0; y < h; y++) Buffer.BlockCopy(px, y * row, flipped, (h - 1 - y) * row, row);
                _icon = WowTextures.FromRgba32(w, h, flipped, WowTextureOptions.Ui, "WoW Map icon", false);
                if (_link != null) _link.AppIcon = _icon;
            }
            catch (Exception e)
            {
                WowLog.Warn("Map: the tablet icon could not be painted (" + e.Message + ").");
            }
        }

        // ------------------------------------------------------------------ VR window

        private void ToggleVrWindow(Transform palm)
        {
            _vrOpen = !_vrOpen;
            _vrPlaced = false;
            _vrOpenedFrom = palm;
            WowLog.Info("Map: VR map window " + (_vrOpen ? "opened" : "closed") + " (palm menu).");
        }

        private void EnsureVrWindow(bool wanted, WowFiles files)
        {
            if (!wanted || _vrSurface != null || files == null) return;
            try
            {
                UiKit.Over = false;
                _vrSurface = UiSurface.MakeWorld("WoW Map Window", transform, WowMapPanel.W, WowMapPanel.H, WindowMetresPerUnit, false, 102);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrSurface;
                _vrPanel = WowMapPanel.Build(_vrSurface.Content, "Map", this, true, () => { _vrOpen = false; WowLog.Info("Map: VR map window closed."); });
                _vrSurface.Finish();
                _vrSurface.SetShown(false);
                UiVrLayout.ExtraSurfaces.Add(_vrSurface);   // pointed at by the UI's ray / poke like the other windows
            }
            finally
            {
                UiKit.Surface = null;
            }
        }

        /// <summary>
        /// As the UI's palm windows (UiVrLayout.PlaceLidFree): above the palm menu when opened from it, else in front of the head a
        /// little down; facing the player, not nearer than 0.6 m; stays put until the player walks 1.5 m away.
        /// </summary>
        private void PlaceVrWindow()
        {
            Camera headCam = WowPlayer.Head();
            if (headCam == null || _vrSurface == null) return;
            Transform head = headCam.transform;
            if (_vrPlaced && (_vrSurface.Root.position - head.position).sqrMagnitude < 1.5f * 1.5f) return;
            bool again = _vrPlaced;
            _vrPlaced = true;
            float h = WowMapPanel.H * WindowMetresPerUnit;
            Vector3 pos;
            if (!again && _vrOpenedFrom != null && _vrOpenedFrom.gameObject.activeInHierarchy) pos = _vrOpenedFrom.position + Vector3.up * (h * 0.5f + 0.05f);
            else
            {
                Vector3 f = head.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                pos = head.position + f * 0.7f - Vector3.up * 0.1f;
            }
            Vector3 look = pos - head.position;
            if (look.sqrMagnitude < 1e-6f) look = head.forward;
            if (look.magnitude < 0.6f) { look = look.normalized * 0.6f; pos = head.position + look; }
            _vrSurface.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            if (_vrPlacements++ < 3)
                WowLog.Info("Map: VR window placed " + (again ? "in front of the player (walked away from it)" : _vrOpenedFrom != null ? "above the palm menu" : "in front of the player") +
                            ", " + (pos - head.position).magnitude.ToString("0.00") + " m from the head.");
        }

        // ------------------------------------------------------------------ desktop window

        private void ToggleDesk(string why)
        {
            SetDesk(!_deskOpen, why);
        }

        private void SetDesk(bool open, string why)
        {
            if (open == _deskOpen) return;
            _deskOpen = open;
            WowLog.Info("Map: desktop map window " + (open ? "opened" : "closed") + " (" + why + ").");
        }

        private void EnsureDesk(bool wanted, WowFiles files)
        {
            if (_deskSurface != null)
            {
                if (_deskSurface.Shown != wanted) _deskSurface.SetShown(wanted);
                return;
            }
            if (!wanted || files == null) return;
            bool pointable = UiKit.Pointable;
            try
            {
                UiKit.Over = false;
                UiKit.Pointable = true;
                _deskSurface = UiSurface.MakeScreen("WoW Map (desktop)", transform, 7);
                UiKit.Surface = _deskSurface;
                // Iteration 11 (uicore H16): the World Map micro button moved to uicore's UiMicroMenu (MicroOrder 10, x 802 - the old one
                // at x 662 sat on WoW's Quest / Socials micro buttons) and the N key to uicore's registry (External spec WorldMap 20).
                // The window: top right, under WoW's minimap corner.
                _deskWindow = _deskSurface.Anchor("Map window", new Vector2(1f, 1f), WowMapPanel.W, WowMapPanel.H, -8f, -8f);
                _deskWindow.localScale = new Vector3(DesktopScale, DesktopScale, 1f);
                _deskPanel = WowMapPanel.Build(_deskWindow, "Map", this, true, () => SetDesk(false, "close button"));
                UiKit.Blocker(_deskWindow);   // iteration 11 (uicore 5.1.2): a click on the map's paper does not reach the world
                _deskSurface.Finish();
                _deskWindow.gameObject.SetActive(false);
            }
            finally
            {
                UiKit.Surface = null;
                UiKit.Pointable = pointable;
            }
        }

        private void Keys()
        {
            if (_keysBroken || WowPlayer.XrPresent || !_wasInPlay) return;
            try
            {
                EventSystem es = EventSystem.current;
                GameObject sel = es != null ? es.currentSelectedGameObject : null;
                if (sel != null && (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<InputField>() != null)) return;   // typing
                // Iteration 11 (uicore H16): N is read by uicore's key dispatcher (the registry's WorldMap spec -> SetDesktopWindow).
            }
            catch (Exception e)
            {
                _keysBroken = true;
                WowLog.Warn("Map: desktop key unavailable (legacy Input): " + e.Message);
            }
        }

        /// <summary>While the desktop map window is open the cursor is shown and free (restored as it was on close).</summary>
        private void FreeCursor(bool free)
        {
            // Iteration 11 (uicore H3): one owner of the cursor (UiCursor's reference set restores it when the last holder lets go).
            if (free == _cursorFreed) return;
            _cursorFreed = free;
            UiCursor.Free(this, free);
        }

        // ------------------------------------------------------------------ lab

        /// <summary>Render lab only: a panel under parent that always shows (the lab has no tablet, palm or screen canvas).</summary>
        public WowMapPanel LabPanel(Transform parent)
        {
            bool pointable = UiKit.Pointable;
            UiKit.Surface = null;
            _labPanel = WowMapPanel.Build(parent, "Lab map", this, false, null);
            UiKit.Pointable = pointable;
            return _labPanel;
        }

        /// <summary>Render lab: draws the live picture now (the batchmode editor renders no camera by itself).</summary>
        public void LabRenderMap()
        {
            _camera.RenderNow();
        }

        /// <summary>Render lab: live or classic, zoom step, rotate.</summary>
        public void LabSet(bool live, int zoom, bool rotate)
        {
            _live = live;
            _zoom = Mathf.Clamp(zoom, 0, ZoomSteps.Length - 1);
            _rotate = rotate;
        }

        public string StatusLine
        {
            get { return _camera.StatsLine + "; " + _tiles.Status + "; " + _marks.Count + " markers; zone '" + (_areaName ?? "-") + "'"; }
        }

        private string _lastError;

        private void LogOnce(string where, Exception e)
        {
            string key = where + e.GetType().Name + e.Message;
            if (key == _lastError) return;
            _lastError = key;
            WowLog.Error("Map: " + where + " failed: " + e);
        }
    }
}
