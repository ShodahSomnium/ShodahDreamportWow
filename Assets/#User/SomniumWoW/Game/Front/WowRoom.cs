using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The black room at WowSpace.LoginRoomOrigin (the scene's spawn point, floor at y 0): an unlit black sphere seen
    /// from inside (EtsHqStage's void, so the client's sky is hidden), a barely visible floor with box colliders and
    /// 4 m walls on layer 0 so nobody walks off, and the WoW-styled panel 2.5 m in front of the spawn at eye height,
    /// facing it: the WotLK logo, the version, two tabs - Play (default: the characters and the class creation screen,
    /// built by WowCharacterScreen into PlayRoot) and Sightseeing (iteration 1's location buttons and the model line-up
    /// toggle) - the time-of-day row both tabs share, and the status line.
    /// </summary>
    public sealed class WowRoom : MonoBehaviour
    {
        public const float VoidRadius = 30f;
        public const float FloorHalf = 12f;
        public const float WallHeight = 4f;
        public const float KeepRadius = 14f;

        /// <summary>The panel: 1100 x 840 units at 2 mm = 2.2 x 1.68 m, centre 1.6 m up, 2.5 m in front (+z) of the spawn.</summary>
        public const float PanelWidth = 1100f, PanelHeight = 840f, MetresPerUnit = 0.002f;
        public static readonly Vector3 PanelCentre = new Vector3(0f, 1.6f, 2.5f);

        /// <summary>The tab content area (PlayRoot / the location buttons): panel units from the top-left.</summary>
        public const float ContentTop = 296f, ContentHeight = 336f;

        public static readonly float[] Hours = { 6f, 12f, 19f, 23f };
        public static readonly string[] HourNames = { "Dawn 6:00", "Day 12:00", "Dusk 19:00", "Night 23:00" };

        public enum Tab { Play = 0, Sightseeing = 1, Settings = 2 }

        /// <summary>Content height when the time-of-day row is hidden (the Settings tab, the character creation screens): ContentTop .. 712.</summary>
        public const float TallHeight = 416f;

        private GameObject _void;
        private GameObject _floor;
        private Canvas _panel;
        private TextMeshProUGUI _status;
        private readonly List<WowGlueButton> _locationButtons = new List<WowGlueButton>();
        private readonly List<WowGlueButton> _hourButtons = new List<WowGlueButton>();
        private WowGlueButton _lineupButton, _playTab, _sightTab, _settingsTab;
        private WowGlueButton _offlineButton, _onlineButton;   // iteration 6: the Play tab's Offline / Online switch
        private RectTransform _playOnly;
        private RectTransform _playRoot, _sightRoot, _sightOnly, _settingsRoot, _timeRow;
        /// <summary>Lead 2026-11-03 (user: "remove offline mode and sightseeing, so just hide them for now"): the Play / Sightseeing tabs, the
        /// Offline / Online switch and the time-of-day row are not shown (their code stays); the Settings button sits at the bottom right.
        /// Set true to bring the old tab row back.</summary>
        public static bool ShowOldTabs = false;
        /// <summary>Lead 2026-11-03: the bottom row (panel units): the account button left (WowOnlineScreen), Settings / Back right.</summary>
        public const float BottomY = 770f, BottomH = 52f;
        private bool _playTall;
        private string _statusText = "";
        private bool _statusError;
        private bool _interactive = true;
        private Tab _tab = Tab.Play;

        public Canvas Panel { get { return _panel; } }

        /// <summary>The Play tab's content area (1100 x ContentHeight units, (0, 0) at its top-left), or null before the files are read.</summary>
        public RectTransform PlayRoot { get { return _playRoot; } }

        public Tab CurrentTab { get { return _tab; } }

        /// <summary>Raised when the player switches tabs.</summary>
        public event Action<Tab> TabChanged;

        /// <summary>13.8: the lobby the room stands in (the menu in its door, no black void), null = the black room.</summary>
        private WowLobby _lobby;

        public WowLobby Lobby { get { return _lobby; } }

        /// <summary>Where the player stands facing the menu: the lobby's star facing its door, else the black room's spawn facing +z.</summary>
        public Vector3 SpawnPoint { get { return _lobby != null ? _lobby.Star : WowSpace.LoginRoomOrigin; } }
        public float SpawnYaw { get { return _lobby != null ? _lobby.SpawnYaw : 0f; } }

        public static WowRoom Create(Transform parent, WowLobby lobby)
        {
            var go = new GameObject("Wow Room");
            go.transform.SetParent(parent, false);
            go.transform.position = WowSpace.LoginRoomOrigin;
            go.transform.rotation = Quaternion.identity;
            var room = go.AddComponent<WowRoom>();
            room._lobby = lobby;
            if (lobby == null) room.BuildVoid();   // 13.8: the lobby has its own floor, walls, sky and colliders
            return room;
        }

        private void BuildVoid()
        {
            _void = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _void.name = "Wow Room Void";
            UnityEngine.Object.Destroy(_void.GetComponent<Collider>());
            _void.transform.SetParent(transform, false);
            _void.transform.localScale = Vector3.one * (VoidRadius * 2f);

            // Inside out: triangles turned round and normals reversed, so the sphere is seen from within (EtsHqStage).
            var filter = _void.GetComponent<MeshFilter>();
            Mesh mesh = Instantiate(filter.sharedMesh);
            int[] tris = mesh.triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3) { int t = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = t; }
            mesh.triangles = tris;
            Vector3[] normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
            mesh.normals = normals;
            filter.sharedMesh = mesh;
            Unlit(_void.GetComponent<MeshRenderer>(), Color.black);

            // A floor you can see a little (it helps in a headset) and stand on.
            _floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _floor.name = "Wow Room Floor";
            UnityEngine.Object.Destroy(_floor.GetComponent<Collider>());
            _floor.transform.SetParent(transform, false);
            _floor.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            _floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _floor.transform.localScale = new Vector3(FloorHalf * 2f, FloorHalf * 2f, 1f);
            Unlit(_floor.GetComponent<MeshRenderer>(), new Color(0.035f, 0.033f, 0.03f));

            Box("Wow Room Floor Collider", new Vector3(0f, -0.5f, 0f), new Vector3(FloorHalf * 2f, 1f, FloorHalf * 2f));
            Box("Wow Room Wall N", new Vector3(0f, WallHeight / 2f, FloorHalf + 0.5f), new Vector3(FloorHalf * 2f + 2f, WallHeight, 1f));
            Box("Wow Room Wall S", new Vector3(0f, WallHeight / 2f, -FloorHalf - 0.5f), new Vector3(FloorHalf * 2f + 2f, WallHeight, 1f));
            Box("Wow Room Wall E", new Vector3(FloorHalf + 0.5f, WallHeight / 2f, 0f), new Vector3(1f, WallHeight, FloorHalf * 2f + 2f));
            Box("Wow Room Wall W", new Vector3(-FloorHalf - 0.5f, WallHeight / 2f, 0f), new Vector3(1f, WallHeight, FloorHalf * 2f + 2f));
        }

        private static void Unlit(MeshRenderer r, Color c)
        {
            Shader shader = WowShaders.Find("Universal Render Pipeline/Unlit");

            if (shader != null)
            {
                var m = new Material(shader) { color = c };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                r.sharedMaterial = m;
            }

            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void Box(string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = 0;
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        /// <summary>
        /// (Re)builds the panel. With no locations (still reading, or an error) only the logo area and the status show;
        /// after the boot the tabs, the location buttons, the time-of-day row and an empty PlayRoot (the caller fills it).
        /// </summary>
        public void BuildPanel(List<WowLocation> locations, Action<WowLocation> onPick, Action<float> onHour, Action onLineup, float hour, bool lineup)
        {
            if (_panel != null)
            {
                Destroy(_panel.gameObject);
            }

            _locationButtons.Clear();
            _hourButtons.Clear();
            _lineupButton = _playTab = _sightTab = _settingsTab = null;
            _offlineButton = _onlineButton = null;
            _playOnly = null;
            _playRoot = _sightRoot = _sightOnly = _settingsRoot = _timeRow = null;
            if (_lobby != null)
            {
                // 13.8: the menu stands in the lobby's door (WowLobby.MenuLocal), fixed, as wide as the door allows; no placer.
                float mpu = WowLobby.MenuWidth / PanelWidth;
                _panel = WowUi.MakeCanvas("Wow Room Panel", transform, PanelWidth, PanelHeight, mpu);
                _panel.transform.SetPositionAndRotation(_lobby.Menu, _lobby.MenuRotation);
            }
            else
            {
                _panel = WowUi.MakeCanvas("Wow Room Panel", transform, PanelWidth, PanelHeight, MetresPerUnit);
                _panel.transform.localPosition = PanelCentre;
                // A canvas is read from the side its forward points away from: forward +z, away from the spawn at the origin.
                _panel.transform.localRotation = Quaternion.identity;
                var placer = _panel.gameObject.AddComponent<WowPanelPlacer>();
                placer.HeightMetres = PanelHeight * MetresPerUnit;
                placer.VrDistance = PanelCentre.z;
                placer.What = "room panel";
            }
            Transform root = _panel.transform;

            WowUi.Backdrop(root, 0f, 0f, PanelWidth, PanelHeight, 32f);

            Texture2D logo = WowUi.Tex(WowUi.LogoPath);

            if (logo != null)
            {
                WowUi.Picture(root, "WotLK Logo", logo, new Rect(0f, 0f, 1f, 1f), (PanelWidth - 400f) / 2f, 12f, 400f, 200f);
            }
            else
            {
                WowUi.Text(root, "Title", "World of Warcraft", 56f, WowUi.GlueGold, TextAlignmentOptions.Center, 50f, 60f, PanelWidth - 100f, 90f);
            }

            WowUi.Text(root, "Version", ShowOldTabs ? WowVersion.Label + "  -  play offline or online (your AzerothCore server)  +  sightseeing" : WowVersion.Label, 20f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 208f, PanelWidth - 100f, 28f);

            if (locations != null)
            {
                if (ShowOldTabs)
                {
                    _playTab = WowUi.Button(root, "Play", 215f, 242f, 210f, 46f, 23f, () => SetTab(Tab.Play, true));
                    _sightTab = WowUi.Button(root, "Sightseeing", 445f, 242f, 210f, 46f, 23f, () => SetTab(Tab.Sightseeing, true));
                    _settingsTab = WowUi.Button(root, "Settings", 675f, 242f, 210f, 46f, 23f, () => SetTab(Tab.Settings, true));
                }
                else
                {
                    // 2026-11-03: Settings at the bottom right; it reads "Back" while the settings are open
                    _settingsTab = WowUi.Button(root, "Settings", PanelWidth - 50f - 330f, BottomY, 330f, BottomH, 23f, () => SetTab(_tab == Tab.Settings ? Tab.Play : Tab.Settings, true));
                }
                _playRoot = WowUi.Place(root, "Play Tab", 0f, ContentTop, PanelWidth, ContentHeight);
                _sightRoot = WowUi.Place(root, "Sightseeing Tab", 0f, ContentTop, PanelWidth, ContentHeight);
                _settingsRoot = WowUi.Place(root, "Settings Tab", 0f, ContentTop, PanelWidth, TallHeight);
                _timeRow = WowUi.Place(root, "Time Row", 0f, 0f, PanelWidth, PanelHeight);
                _sightOnly = WowUi.Place(root, "Sightseeing Row", 0f, 0f, PanelWidth, PanelHeight);

                if (locations.Count > 0)
                {
                    WowUi.Text(_sightRoot, "Locations Title", "Choose a location", 26f, WowUi.GlueGold, TextAlignmentOptions.Center, 50f, 0f, PanelWidth - 100f, 34f);
                }
                else
                {
                    WowUi.Text(_sightRoot, "Locations Title", "No location could be read (see the status line).", 22f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 40f, PanelWidth - 100f, 34f);
                }

                const float Bw = 320f, Bh = 46f, Gap = 20f;

                for (int i = 0; i < locations.Count && i < 15; i++)
                {
                    WowLocation loc = locations[i];
                    int col = i % 3, row = i / 3;
                    WowGlueButton b = WowUi.Button(_sightRoot, loc.Label, 50f + col * (Bw + Gap), 38f + row * 50f, Bw, Bh, 21f, () => { if (onPick != null) onPick(loc); });
                    _locationButtons.Add(b);
                }

                // The time of day both tabs use; the line-up toggle only with Sightseeing.
                WowUi.Text(_timeRow, "Time Title", "Time of day", 22f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 636f, 400f, 30f);

                for (int i = 0; i < Hours.Length; i++)
                {
                    float h = Hours[i];
                    WowGlueButton b = WowUi.Button(_timeRow, HourNames[i], 50f + i * 180f, 668f, 172f, 44f, 19f, () => { if (onHour != null) onHour(h); });
                    _hourButtons.Add(b);
                }

                WowUi.Text(_sightOnly, "Lineup Title", "Model line-up", 22f, WowUi.GlueGold, TextAlignmentOptions.Left, 796f, 636f, 260f, 30f);
                _lineupButton = WowUi.Button(_sightOnly, "On", 790f, 668f, 260f, 44f, 19f, () => { if (onLineup != null) onLineup(); });
                SetHour(hour);
                SetLineup(lineup);
                SetTab(_tab, false);
            }

            _status = ShowOldTabs || locations == null
                ? WowUi.Text(root, "Status", _statusText, 22f, Color.white, TextAlignmentOptions.Center, 60f, 720f, PanelWidth - 120f, 104f)
                : WowUi.Text(root, "Status", _statusText, 20f, Color.white, TextAlignmentOptions.Center, 60f, 716f, PanelWidth - 120f, 50f);   // 2026-11-03: above the bottom row
            if (!ShowOldTabs) { _status.enableAutoSizing = true; _status.fontSizeMin = 12f; _status.fontSizeMax = 20f; }
            SetStatus(_statusText, _statusError);
            SetInteractive(_interactive);
        }

        /// <summary>Shows one tab (the Play tab's content is PlayRoot, filled by WowCharacterScreen).</summary>
        public void SetTab(Tab tab, bool byPlayer)
        {
            bool changed = tab != _tab;
            _tab = tab;
            if (_playRoot != null) _playRoot.gameObject.SetActive(tab == Tab.Play);
            if (_sightRoot != null) _sightRoot.gameObject.SetActive(tab == Tab.Sightseeing);
            if (_sightOnly != null) _sightOnly.gameObject.SetActive(tab == Tab.Sightseeing);
            if (_settingsRoot != null) _settingsRoot.gameObject.SetActive(tab == Tab.Settings);
            if (_playOnly != null) _playOnly.gameObject.SetActive(tab == Tab.Play);
            if (_playTab != null) _playTab.Selected = tab == Tab.Play;
            if (_sightTab != null) _sightTab.Selected = tab == Tab.Sightseeing;
            if (_settingsTab != null) _settingsTab.Selected = tab == Tab.Settings;
            if (_settingsTab != null && !ShowOldTabs) _settingsTab.SetLabel(tab == Tab.Settings ? "Back" : "Settings");
            RefreshTimeRow();

            if (byPlayer)
            {
                WowLog.Info("Room: tab " + tab + (changed ? "" : " (already shown)") + ".");
                var h = TabChanged;
                if (h != null && changed) h(tab);
            }
        }

        /// <summary>
        /// Iteration 6: the Play tab's Offline / Online switch, right of the tabs (call after every BuildPanel with locations).
        /// changed(true) = online. Shown only with the Play tab.
        /// </summary>
        public void BuildModeSwitch(bool online, Action<bool> changed)
        {
            if (_panel == null || _playRoot == null || !ShowOldTabs) return;   // 2026-11-03: offline mode hidden
            if (_playOnly != null) Destroy(_playOnly.gameObject);
            _playOnly = WowUi.Place(_panel.transform, "Play Mode Switch", 0f, 0f, PanelWidth, PanelHeight);
            _offlineButton = WowUi.Button(_playOnly, "Offline", 893f, 242f, 86f, 46f, 17f, () => { if (changed != null) changed(false); });
            _onlineButton = WowUi.Button(_playOnly, "Online", 981f, 242f, 86f, 46f, 17f, () => { if (changed != null) changed(true); });
            SetOnline(online);
            SetTab(_tab, false);
            SetInteractive(_interactive);
        }

        /// <summary>Highlights the switch's current side.</summary>
        public void SetOnline(bool online)
        {
            if (_offlineButton != null) _offlineButton.Selected = !online;
            if (_onlineButton != null) _onlineButton.Selected = online;
        }

        public void SetStatus(string text, bool error)
        {
            _statusText = text ?? "";
            _statusError = error;

            if (_status != null)
            {
                _status.text = _statusText;
                _status.color = error ? new Color(1f, 0.25f, 0.2f) : Color.white;
            }
        }

        public void SetHour(float hour)
        {
            for (int i = 0; i < _hourButtons.Count; i++) _hourButtons[i].Selected = Mathf.Abs(Hours[i] - hour) < 0.01f;
        }

        public void SetLineup(bool on)
        {
            if (_lineupButton == null) return;
            _lineupButton.SetLabel(on ? "On" : "Off");
            _lineupButton.Selected = on;
        }

        /// <summary>Buttons pressable or not (while loading nothing is).</summary>
        public void SetInteractive(bool interactive)
        {
            _interactive = interactive;
            foreach (WowGlueButton b in _locationButtons) b.Interactable = interactive;
            foreach (WowGlueButton b in _hourButtons) b.Interactable = interactive;
            if (_lineupButton != null) _lineupButton.Interactable = interactive;
            if (_playTab != null) _playTab.Interactable = interactive;
            if (_sightTab != null) _sightTab.Interactable = interactive;
            if (_settingsTab != null) _settingsTab.Interactable = interactive;
            if (_offlineButton != null) _offlineButton.Interactable = interactive;
            if (_onlineButton != null) _onlineButton.Interactable = interactive;
        }

        /// <summary>The Settings tab's content area (1100 x TallHeight units), or null before the files are read.</summary>
        public RectTransform SettingsRoot { get { return _settingsRoot; } }

        /// <summary>The character creation screens use the time-of-day row's space too (TallHeight): true hides the row on the Play tab.</summary>
        public void SetPlayTall(bool tall)
        {
            _playTall = tall;
            RefreshTimeRow();
        }

        private void RefreshTimeRow()
        {
            if (_timeRow != null) _timeRow.gameObject.SetActive(ShowOldTabs && (_tab == Tab.Sightseeing || (_tab == Tab.Play && !_playTall)));   // 2026-11-03: the realm's clock sets the time
        }

        public bool Interactive { get { return _interactive; } }

        public void SetPanelShown(bool shown)
        {
            if (_panel != null) _panel.gameObject.SetActive(shown);
        }

        /// <summary>The whole room on or off (off while in the world).</summary>
        public void SetShown(bool shown)
        {
            gameObject.SetActive(shown);
            if (_lobby != null) _lobby.Show(shown);   // 13.8: the lobby is the room (hidden in the world)
        }

        private void LateUpdate()
        {
            if (_panel != null && _panel.gameObject.activeInHierarchy)
            {
                _panel.worldCamera = WowPlayer.Head();
            }
        }

        /// <summary>True when feet are outside the floor or far below/above it (the room's keep rule, as EtsHqStage.KeepInVoid).</summary>
        public static bool IsAway(Vector3 feet)
        {
            Vector3 d = feet - WowSpace.LoginRoomOrigin;
            return new Vector2(d.x, d.z).magnitude > KeepRadius || d.y < -2f || d.y > 20f;
        }
    }
}
