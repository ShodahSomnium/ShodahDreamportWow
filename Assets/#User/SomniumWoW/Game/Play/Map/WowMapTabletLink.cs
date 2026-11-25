using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.MapApp
{
    /// <summary>
    /// The Somnium client's own tablet, borrowed for the "WoW Map" app (iteration 5, owner Map). A port of the Euro Truck Sim
    /// Dreamport's EtsTabletLink (client-tested there: Player.log 2026-09-23 found every part by type), minus the truck parts
    /// (desktop snap, vehicle carry, Exit World).
    ///
    /// There is no tablet API for worlds. The client instantiates Tablet.XR / Tablet.NonXR into "MasterScene" and parents it
    /// under the local player when "tablet follows player" is on. So it is found by searching, by the client's component TYPE
    /// names first (TabletControl, TabsContainer, HomeTab, TabletStatusBarView - all four exist in the client source:
    /// VRTablet/Tablet/TabletControl.cs, Tabs/Scripts/TabsContainer.cs, Tabs/HomeTab/HomeTab.cs, Views/TabletStatusBarView.cs)
    /// with object names as the fallback. The client's own Map app (VRTablet/Applications/MapApp, Somnium's parcel map) is
    /// never touched: "WoW Map" is an extra button beside it.
    ///
    /// Safety rules (as ETS):
    ///  - never DontDestroyOnLoad: the tablet outlives the world, so OnDestroy takes the button and the canvas off it and
    ///    restores what was hidden; objects an earlier visit left (named WoW.Tablet.*) are swept at every link, and a Tabs
    ///    container an earlier visit left hidden is shown again;
    ///  - the home button is ONE client button cloned under an INACTIVE holder and stripped to Unity UI + TMP before it is ever
    ///    active (no client script wakes on the copy); its persistent onClick is replaced by a new ButtonClickedEvent; it goes
    ///    FIRST in the home grid (never where the grid's Mask can hide it) and is kept first;
    ///  - nothing of the client's Tabs container is cloned: the app's canvas is created as a sibling of the tabs under
    ///    "Canvases" (so it fades and stops taking the ray with the tablet), on the HomeTab canvas's layer, with its sorting,
    ///    camera and raycasters mirrored, following the HomeTab rect every LateUpdate (the player's own Resize is followed);
    ///  - a client tab switch (any tab's ContentView changing) closes the app;
    ///  - the tablet is never resized by default (the map fits the Small 960 x 1080 home: 640 x 720 x 1.5).
    /// Extra for the floating origin: a free-standing tablet (not under the local body, not held) is moved with the world on
    /// WowSpace.Rebased, so it stays where the player put it in the WoW world.
    /// One discovery line is logged ("Tablet: linked - ..." or "Tablet: not linked after 20 s - ...").
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class WowMapTabletLink : MonoBehaviour
    {
        /// <summary>The prefix of everything this puts on the client's tablet; swept on every link.</summary>
        public const string Owned = "WoW.Tablet.";

        /// <summary>The frame the map panel lays itself out in (scaled to fit the HomeTab rect).</summary>
        public const float FrameWidth = WowMapPanel.W, FrameHeight = WowMapPanel.H;

        /// <summary>HomeTab widths: Small 960, Large 1920 (Tablet.XR.HomeTab.prefab:424-434); Large = anything wider than this.</summary>
        public const float LargeAbove = 1440f;

        public static WowMapTabletLink Instance { get; private set; }

        [Tooltip("Discovery and one report line only; nothing is added to the client's tablet.")]
        public bool LogOnly;

        [Tooltip("Seconds between searches for the tablet while it has not been found (5 s after the first minute).")]
        public float SearchInterval = 0.5f;

        [Tooltip("Caption under the app's icon on the tablet's home screen.")]
        public string AppTitle = "WoW Map";

        [Tooltip("Press the tablet's Resize button when the app opens on a Small tablet, and again when it closes.")]
        public bool ForceLarge;

        /// <summary>The home-screen icon (Interface\WorldMap\WorldMap-Icon.blp painted upright), set by WowTabletMap.</summary>
        public Texture2D AppIcon;

        public event Action<bool> AppShown;
        public event Action LinkChanged;

        public bool IsLinked { get { return !LogOnly && _t != null && _t.Root != null; } }
        public bool IsDesktopTablet { get { return _t != null && _t.Desktop; } }
        public bool TabletOpen { get; private set; }
        public bool AppOpen { get; private set; }

        /// <summary>The 640 x 720 rectangle the map panel lays itself out in, inside the tablet's screen; null unless linked.</summary>
        public RectTransform Frame { get { return IsLinked ? _frame : null; } }

        /// <summary>The layer the client's tablet graphics carry (5, UI), which the XR ray tests per graphic.</summary>
        public int UiLayer { get { return IsLinked ? _t.UiLayer : 5; } }

        /// <summary>The last discovery result, as logged.</summary>
        public string Report { get; private set; } = "not searched yet";

        private sealed class Parts
        {
            public Transform Root;
            public bool Desktop;
            public Animator Animator;
            public bool AnimatorHasIsOpen;
            public GameObject GrabCollider;
            public XRGrabInteractable Grab;
            public Transform Canvases;
            public CanvasGroup CanvasesGroup;
            public Transform Tabs;
            public Canvas HomeCanvas;
            public RectTransform Home;
            public GridLayoutGroup HomeGrid;
            public Button ReferenceButton;
            public RectTransform StatusBar;
            public Button Back;
            public Button Resize;
            public int GridShown;
            public int UiLayer = 5;
            public readonly List<GameObject> TabContents = new List<GameObject>();
            public readonly List<UnityEvent> MeshHome = new List<UnityEvent>();
        }

        private static readonly int IsOpenHash = Animator.StringToHash("IsOpen");
        private static readonly List<MonoBehaviour> Buffer = new List<MonoBehaviour>();
        private static readonly Vector3[] Corners = new Vector3[4];

        private Parts _t;
        private GameObject _button;
        private Image _buttonIcon;
        private GameObject _app;
        private Canvas _appCanvas;
        private RectTransform _frame;
        private float _nextSlow;
        private bool[] _tabSnapshot;
        private bool _backWasActive;
        private bool _enlarged;
        private float _resizeSettleUntil;
        private Sprite _iconSprite;

        private float _nextSearch;
        private int _searches;
        private double _slowestMs;
        private float _firstSearchAt = -1f;
        private bool _notLinkedReported;
        private bool _ending;
        private float _linkedAt;
        private bool _placeReported;
        private bool _reorderReported;
        private bool _rebaseHooked;
        private int _rebaseMoves;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            // Statics survive a world re-entry in the same client session: the previous visit's instance is replaced, never trusted.
            Instance = this;
        }

        private void OnEnable()
        {
            if (!_rebaseHooked)
            {
                WowSpace.Rebased += OnRebased;
                _rebaseHooked = true;
            }
        }

        private void OnDisable()
        {
            if (_rebaseHooked)
            {
                WowSpace.Rebased -= OnRebased;
                _rebaseHooked = false;
            }
        }

        private void OnDestroy()
        {
            // The world is unloading: leave the client's tablet as it was. No events from here.
            _ending = true;
            Unlink(true, "world unloading");

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsLinked)
            {
                if (_t != null)
                {
                    Unlink(false, "the tablet was destroyed (avatar or mode change)");
                }

                if (Time.unscaledTime >= _nextSearch)
                {
                    if (_firstSearchAt < 0f)
                    {
                        _firstSearchAt = Time.unscaledTime;
                    }

                    // Every SearchInterval for the first minute (the tablet comes with the local player), then every 5 s.
                    float interval = Time.unscaledTime - _firstSearchAt < 60f ? Mathf.Max(0.1f, SearchInterval) : 5f;
                    _nextSearch = Time.unscaledTime + interval;
                    TryLink();
                }

                return;
            }

            TabletOpen = ReadTabletOpen();

            if (Time.unscaledTime < _nextSlow)
            {
                return;
            }

            _nextSlow = Time.unscaledTime + 0.25f;

            if (_appCanvas != null)
            {
                Camera eventCamera = _t.HomeCanvas != null ? _t.HomeCanvas.worldCamera : null;
                Camera wanted = eventCamera != null ? eventCamera : Camera.main;

                if (_appCanvas.worldCamera != wanted)
                {
                    _appCanvas.worldCamera = wanted;
                }
            }

            if (_buttonIcon != null && _buttonIcon.sprite == null)
            {
                SetIcon(); // the game files opened after the link
            }

            KeepButtonFirst();

            if (AppOpen)
            {
                if (TabsChangedSinceOpen())
                {
                    Close(false, "client switched tab"); // mesh Map/Chat/..., a deep link, the exit dialog
                    return;
                }

                SetLayer(_app.transform, _t.UiLayer); // rows added after the build need the ray's layer too
            }
        }

        private void LateUpdate()
        {
            if (IsLinked)
            {
                FollowHomeRect();
            }
        }

        /// <summary>
        /// WowSpace.Rebased: the world moved by -delta and the avatar with it. A tablet under the local body moves with the body;
        /// a free-standing one (follow off, not held) is moved too, so it stays at its place in the WoW world.
        /// </summary>
        private void OnRebased(Vector3 delta)
        {
            if (!IsLinked)
            {
                return;
            }

            try
            {
                Transform body = WowPlayer.Root;
                bool follows = body != null && _t.Root.IsChildOf(body);
                bool held = _t.Grab != null && _t.Grab.isSelected;

                if (!follows && !held)
                {
                    _t.Root.position -= delta;

                    if (_rebaseMoves++ < 3)
                    {
                        WowLog.Info("Tablet: the free-standing tablet moved with the world (rebase " + delta.ToString("F0") + ").");
                    }
                }
            }
            catch (Exception e)
            {
                WowLog.Warn("Tablet: rebase move failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ discovery

        private void TryLink()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Parts found;
            bool complete;
            string report;

            try
            {
                found = Discover(out report, out complete);
            }
            catch (Exception e)
            {
                found = null;
                complete = false;
                report = "the last search threw " + e.GetType().Name + ": " + e.Message;
            }

            watch.Stop();
            _searches++;
            _slowestMs = Math.Max(_slowestMs, watch.Elapsed.TotalMilliseconds);
            Report = report;

            if (!complete || LogOnly)
            {
                if (complete && LogOnly && !_notLinkedReported)
                {
                    _notLinkedReported = true;
                    WowLog.Info("Tablet (log only): found - " + report + "; " + _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms.");
                    enabled = false;
                    return;
                }

                // One line, once, so a log shows why the map fell back to its own window.
                if (!_notLinkedReported && Time.unscaledTime - _firstSearchAt > 20f)
                {
                    _notLinkedReported = true;
                    WowLog.Info("Tablet: not linked after " + (Time.unscaledTime - _firstSearchAt).ToString("0") + " s - " + report +
                                "; still searching every " + (Time.unscaledTime - _firstSearchAt < 60f ? SearchInterval.ToString("0.0") : "5") + " s; " +
                                _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms. The map opens in its own window (palm menu / N).");
                }

                return;
            }

            _t = found;
            RemoveLeftovers(_t);

            try
            {
                Inject();
            }
            catch (Exception e)
            {
                // Half an injection is worse than none: take off whatever went on, and look again later.
                WowLog.Warn("Tablet: injection failed (" + e.GetType().Name + ": " + e.Message + ") - " + report);
                Unlink(true, "injection failed");
                _nextSearch = Time.unscaledTime + 5f;
                return;
            }

            WowLog.Info("Tablet: linked - " + report + "; " + _searches + " searches, slowest " + _slowestMs.ToString("0.0") + " ms.");
            _linkedAt = Time.unscaledTime;
            _placeReported = false;

            if (LinkChanged != null)
            {
                LinkChanged();
            }
        }

        /// <summary>
        /// Four times a second while linked: the "WoW Map" button stays the grid's first child, and once (0.5 s after the link,
        /// grid active and laid out) its measured cell is logged against the grid's rect - the in-client proof that it shows.
        /// </summary>
        private void KeepButtonFirst()
        {
            if (_button == null || _t.HomeGrid == null)
            {
                return;
            }

            Transform button = _button.transform;

            if (button.parent == _t.HomeGrid.transform && button.GetSiblingIndex() != 0)
            {
                button.SetSiblingIndex(0);

                if (!_reorderReported)
                {
                    _reorderReported = true;
                    WowLog.Info("Tablet: the client re-ordered its home grid; the WoW Map button is first again.");
                }
            }

            if (_placeReported || Time.unscaledTime - _linkedAt < 0.5f || !_t.HomeGrid.gameObject.activeInHierarchy)
            {
                return;
            }

            _placeReported = true;
            var grid = (RectTransform)_t.HomeGrid.transform;
            int shown = 0, index = -1, buttons = 0;

            for (int i = 0; i < grid.childCount; i++)
            {
                Transform child = grid.GetChild(i);
                buttons += child != button && child.GetComponent<Button>() != null ? 1 : 0;

                if (!child.gameObject.activeSelf)
                {
                    continue; // not laid out
                }

                if (child == button)
                {
                    index = shown;
                }

                shown++;
            }

            Rect view = grid.rect;
            ((RectTransform)button).GetWorldCorners(Corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                Vector3 p = grid.InverseTransformPoint(Corners[i]);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            bool inside = minX >= view.xMin - 0.5f && maxX <= view.xMax + 0.5f && minY >= view.yMin - 0.5f && maxY <= view.yMax + 0.5f;
            bool masked = grid.GetComponent<Mask>() != null || grid.GetComponent<RectMask2D>() != null;
            WowLog.Info("Tablet: the WoW Map button is cell " + (index + 1) + " of " + shown + " shown in the home grid (" + buttons + " client buttons + WoW Map), at " +
                        (minX - view.xMin).ToString("0") + ".." + (maxX - view.xMin).ToString("0") + " x " + (view.yMax - maxY).ToString("0") + ".." + (view.yMax - minY).ToString("0") +
                        " of the " + view.width.ToString("0") + " x " + view.height.ToString("0") + " view, " + (masked ? "inside the mask: " : "no mask on the grid, inside its rect: ") + (inside ? "yes." : "NO."));
        }

        /// <summary>
        /// One search. Returns the parts when a tablet root was found (complete or not) and null otherwise; complete when Tabs,
        /// the HomeTab canvas, its grid and a reference button are all there.
        /// </summary>
        private static Parts Discover(out string report, out bool complete)
        {
            complete = false;
            Scene master = SceneManager.GetSceneByName("MasterScene"); // WorldsHelper.MASTER_SCENE_NAME
            bool masterLoaded = master.IsValid() && master.isLoaded;
            var roots = new List<GameObject>();
            Transform root = null;
            bool rootByType = false;

            if (masterLoaded)
            {
                master.GetRootGameObjects(roots);
                root = SearchRoots(roots, out rootByType);
            }

            int others = 0;

            for (int i = 0; root == null && i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded || scene == master)
                {
                    continue;
                }

                others++;
                scene.GetRootGameObjects(roots);
                root = SearchRoots(roots, out rootByType);
            }

            if (root == null)
            {
                report = "no transform with a TabletControl component, and none named 'Tablet.XR(...' or 'Tablet.NonXR(...', in MasterScene (" +
                         (masterLoaded ? "loaded" : "NOT loaded") + ") or " + others + " other loaded scene(s); 'Simulator' objects (remote tablets) and the world's 'WoW ...' subtrees skipped";
                return null;
            }

            var t = new Parts { Root = root };

            Transform tabs = FindTyped(root, "TabsContainer");
            string tabsHow = tabs != null ? "type" : null;

            if (tabs == null)
            {
                tabs = FindNamed(root, "Tablet.XR.Tabs", 3);
                tabsHow = tabs != null ? "name" : "-";
            }

            Transform home = FindTyped(root, "HomeTab");
            string homeHow = home != null ? "type" : null;

            if (home == null)
            {
                home = FindNamed(root, "Tablet.XR.HomeTab", 4);
                homeHow = home != null ? "name" : "-";
            }

            Transform bar = FindTyped(root, "TabletStatusBarView");
            string barHow = bar != null ? "type" : null;

            if (bar == null)
            {
                bar = FindNamed(root, "Tablet.XR.StatusBar", 3);
                barHow = bar != null ? "name" : "-";
            }

            t.Tabs = tabs;
            t.Home = home as RectTransform;
            t.StatusBar = bar as RectTransform;
            t.Canvases = tabs != null ? tabs.parent : FindNamed(root, "Canvases", 1);
            t.CanvasesGroup = t.Canvases != null ? t.Canvases.GetComponent<CanvasGroup>() : null;
            Transform grabCollider = FindNamed(root, "GrabCollider", 1);
            t.GrabCollider = grabCollider != null ? grabCollider.gameObject : null;
            t.Grab = root.GetComponent<XRGrabInteractable>();

            Canvas homeCanvas = home != null ? home.GetComponent<Canvas>() : null;
            GridLayoutGroup grid = home != null ? home.GetComponentInChildren<GridLayoutGroup>(true) : null;
            t.HomeCanvas = homeCanvas;
            t.HomeGrid = grid;
            Button reference = null;
            int uiLayer = 5;
            int gridButtons = 0;
            int gridShown = 0;

            if (grid != null)
            {
                foreach (Transform child in grid.transform)
                {
                    var b = child.GetComponent<Button>();

                    if (b == null)
                    {
                        continue;
                    }

                    gridButtons++;
                    gridShown += child.gameObject.activeSelf && !child.name.StartsWith(Owned, StringComparison.Ordinal) ? 1 : 0;

                    // Never an earlier visit's clone: a copy of a copy would carry no client look at all.
                    if (reference == null && child.GetComponentInChildren<TMP_Text>(true) != null && !child.name.StartsWith(Owned, StringComparison.Ordinal) &&
                        !child.name.StartsWith("ETS.Tablet.", StringComparison.Ordinal))
                    {
                        reference = b;
                        var graphic = child.GetComponent<Graphic>();
                        uiLayer = graphic != null ? graphic.gameObject.layer : child.gameObject.layer;
                    }
                }
            }

            t.ReferenceButton = reference;
            t.UiLayer = uiLayer;
            t.GridShown = gridShown;
            bool back = false;
            bool resize = false;

            if (bar != null)
            {
                foreach (Button b in bar.GetComponentsInChildren<Button>(true))
                {
                    bool isBack = b.name.IndexOf("Back", StringComparison.Ordinal) >= 0;
                    bool isResize = b.name.IndexOf("Resize", StringComparison.Ordinal) >= 0;
                    back |= isBack;
                    resize |= isResize;
                    if (isBack && t.Back == null) t.Back = b;
                    if (isResize && t.Resize == null) t.Resize = b;
                }
            }

            int tabCount = 0;
            int contentViews = 0;

            if (tabs != null)
            {
                foreach (Transform tab in tabs)
                {
                    tabCount++;
                    Transform content = tab.Find("ContentView");
                    contentViews += content != null ? 1 : 0;
                    t.TabContents.Add(content != null ? content.gameObject : null);
                }
            }

            var animator = root.GetComponent<Animator>();
            bool isOpenParameter = false;

            if (animator != null && animator.isActiveAndEnabled)
            {
                foreach (AnimatorControllerParameter p in animator.parameters)
                {
                    isOpenParameter |= p.type == AnimatorControllerParameterType.Bool && p.name == "IsOpen";
                }
            }

            t.Animator = animator;
            t.AnimatorHasIsOpen = isOpenParameter;
            bool meshHome = FindMeshHome(root, t.MeshHome);
            bool grab = HasType(root, "XRGrabInteractable");
            bool desktop = root.name.StartsWith("Tablet.NonXR", StringComparison.Ordinal) || !WowPlayer.XrPresent;
            t.Desktop = desktop;
            var homeRect = home as RectTransform;
            string size = (homeRect == null ? "home size unknown" :
                          "home " + homeRect.rect.width.ToString("0") + "x" + homeRect.rect.height.ToString("0") + (homeRect.rect.width > LargeAbove ? " (Large)" : " (Small)")) +
                          ", " + gridShown + " of the grid's buttons shown";

            string missing = (tabs == null ? " Tabs" : "") + (homeCanvas == null ? " HomeCanvas" : "") + (grid == null ? " HomeGrid" : "") +
                             (reference == null ? " ReferenceButton" : "") + (!back ? " Back" : "") + (!resize ? " Resize" : "") +
                             (animator == null ? " Animator" : isOpenParameter ? "" : " IsOpen");

            report = "'" + root.name + "' under '" + (root.parent != null ? root.parent.name : "(scene root)") + "' in scene '" + root.gameObject.scene.name + "', " +
                     (desktop ? "desktop" : "XR") + ", root by " + (rootByType ? "type TabletControl" : "name") +
                     "; TabsContainer by " + tabsHow + ", HomeTab by " + homeHow + ", TabletStatusBarView by " + barHow +
                     "; " + tabCount + " tabs (" + contentViews + " with a ContentView), home grid " + gridButtons + " buttons, " + size +
                     ", UI layer " + uiLayer + ", canvas layer " + (homeCanvas != null ? homeCanvas.gameObject.layer.ToString() : "-") +
                     ", raycaster " + Raycaster(homeCanvas) + ", mesh Home " + (meshHome ? "yes" : "no") + ", grab " + (grab ? "yes" : "no") +
                     (missing.Length > 0 ? "; MISSING:" + missing : "; all parts found");

            complete = tabs != null && homeCanvas != null && grid != null && reference != null;
            return t;
        }

        /// <summary>
        /// The first transform carrying a TabletControl, else the first named "Tablet.XR(" / "Tablet.NonXR(". Remote players'
        /// tablets ("Simulator") and the world's own "WoW ..." / "Wow ..." subtrees (the streamed map) are not entered.
        /// </summary>
        private static Transform SearchRoots(List<GameObject> roots, out bool byType)
        {
            byType = false;
            var stack = new Stack<Transform>();

            foreach (GameObject go in roots)
            {
                if (go != null)
                {
                    stack.Push(go.transform);
                }
            }

            Transform named = null;

            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                string name = t.name;

                if (name.StartsWith("WoW ", StringComparison.Ordinal) || name.StartsWith("Wow ", StringComparison.Ordinal) ||
                    name.IndexOf("Simulator", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                if (HasType(t, "TabletControl"))
                {
                    byType = true;
                    return t; // the client's own component: the surest mark
                }

                if (named == null && (name.StartsWith("Tablet.XR(", StringComparison.Ordinal) || name.StartsWith("Tablet.NonXR(", StringComparison.Ordinal)))
                {
                    named = t;
                }

                for (int i = 0; i < t.childCount; i++)
                {
                    stack.Push(t.GetChild(i));
                }
            }

            return named;
        }

        private static bool HasType(Transform t, string typeName)
        {
            t.GetComponents(Buffer);

            foreach (MonoBehaviour mb in Buffer)
            {
                if (mb != null && mb.GetType().Name == typeName)
                {
                    Buffer.Clear();
                    return true;
                }
            }

            Buffer.Clear();
            return false;
        }

        private static Transform FindTyped(Transform root, string typeName)
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == typeName)
                {
                    return mb.transform;
                }
            }

            return null;
        }

        private static Transform FindNamed(Transform root, string name, int maxDepth)
        {
            if (maxDepth < 0)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);

                if (child.name == name)
                {
                    return child;
                }
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform deeper = FindNamed(root.GetChild(i), name, maxDepth - 1);

                if (deeper != null)
                {
                    return deeper;
                }
            }

            return null;
        }

        /// <summary>
        /// The tablet's mesh Home button fires XRInteractableButton.ButtonPressed on a poke; the event is a public UnityEvent
        /// field, reached by reflection (optional: without it only Back closes the app).
        /// </summary>
        private static bool FindMeshHome(Transform root, List<UnityEvent> into)
        {
            Transform meshHome = FindNamed(root, "HomeButton", 8);

            if (meshHome == null)
            {
                return false;
            }

            foreach (MonoBehaviour mb in meshHome.GetComponents<MonoBehaviour>())
            {
                FieldInfo field = mb != null ? mb.GetType().GetField("ButtonPressed", BindingFlags.Public | BindingFlags.Instance) : null;
                var pressed = field != null ? field.GetValue(mb) as UnityEvent : null;

                if (pressed != null)
                {
                    into.Add(pressed);
                }
            }

            return into.Count > 0;
        }

        /// <summary>Which raycaster the home canvas carries: the app's canvas mirrors it.</summary>
        private static string Raycaster(Canvas canvas)
        {
            if (canvas == null)
            {
                return "-";
            }

            string found = "";

            foreach (MonoBehaviour mb in canvas.GetComponents<MonoBehaviour>())
            {
                string type = mb != null ? mb.GetType().Name : "";

                if (type.EndsWith("Raycaster", StringComparison.Ordinal))
                {
                    found += (found.Length > 0 ? "+" : "") + type;
                }
            }

            return found.Length > 0 ? found : "none";
        }

        // ------------------------------------------------------------------ injection

        /// <summary>What an earlier visit left on this tablet (WoW.Tablet.*) goes, and a Tabs container it left hidden is shown.</summary>
        private static void RemoveLeftovers(Parts t)
        {
            var doomed = new List<GameObject>();

            foreach (Transform child in t.HomeGrid.transform)
            {
                if (child.name.StartsWith(Owned, StringComparison.Ordinal)) doomed.Add(child.gameObject);
            }

            if (t.Canvases != null)
            {
                foreach (Transform child in t.Canvases)
                {
                    if (child.name.StartsWith(Owned, StringComparison.Ordinal)) doomed.Add(child.gameObject);
                }
            }

            foreach (GameObject go in doomed)
            {
                // Out of the grid at once (Destroy is deferred to the end of the frame, and the grid lays out before).
                go.SetActive(false);
                Destroy(go);
            }

            bool tabsShown = false;

            if (t.Tabs != null && !t.Tabs.gameObject.activeSelf)
            {
                t.Tabs.gameObject.SetActive(true);
                tabsShown = true;
            }

            if (doomed.Count > 0 || tabsShown)
            {
                WowLog.Info("Tablet: removed " + doomed.Count + " objects left by an earlier visit" + (tabsShown ? "; its hidden Tabs container shown again" : "") + ".");
            }
        }

        private void Inject()
        {
            // The home-screen button: a copy of ONE client button, made under an inactive holder so that no client script on it
            // ever wakes (Instantiate under an inactive parent runs no Awake/OnEnable), stripped to Unity UI + TextMeshPro, and
            // only then moved into the grid - first.
            var staging = new GameObject(Owned + "Staging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);

            try
            {
                _button = Instantiate(_t.ReferenceButton.gameObject, staging.transform, false);
                _button.name = Owned + "Button";
                StripToUnityUi(_button);
                _button.transform.SetParent(_t.HomeGrid.transform, false);
                _button.transform.SetSiblingIndex(0);
            }
            finally
            {
                Destroy(staging);
            }

            var button = _button.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // drops the persistent call into the destroyed client script
            button.onClick.AddListener(OnAppButton);
            button.interactable = true;

            foreach (TMP_Text caption in _button.GetComponentsInChildren<TMP_Text>(true))
            {
                caption.text = AppTitle;
            }

            Transform iconTransform = _button.transform.Find("Icon");
            _buttonIcon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            SetIcon();
            _button.SetActive(true);

            // The app's own canvas, a sibling of the client's tab canvases under "Canvases" (never inside the Tabs container,
            // which is hidden while the app shows), so the tablet's close fade hides it too.
            _app = new GameObject(Owned + "App", typeof(RectTransform));
            _app.SetActive(false);
            _app.layer = _t.HomeCanvas.gameObject.layer;
            _app.transform.SetParent(_t.Canvases != null ? _t.Canvases : _t.Tabs.parent, false);
            _appCanvas = _app.AddComponent<Canvas>();
            _appCanvas.renderMode = RenderMode.WorldSpace;
            _appCanvas.sortingLayerID = _t.HomeCanvas.sortingLayerID;
            _appCanvas.sortingOrder = _t.HomeCanvas.sortingOrder;
            _appCanvas.additionalShaderChannels = _t.HomeCanvas.additionalShaderChannels;
            _appCanvas.worldCamera = _t.HomeCanvas.worldCamera;

            // Nested canvases need their own raycaster; mirror what HomeTab really has (the NonXR variant has GraphicRaycasters),
            // both if neither.
            bool tracked = _t.HomeCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() != null;
            bool mouse = _t.HomeCanvas.GetComponent<GraphicRaycaster>() != null;

            if (tracked || !mouse)
            {
                _app.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            if (mouse || !tracked)
            {
                _app.AddComponent<GraphicRaycaster>();
            }

            // RectMask2D clips to the tablet's screen and filters raycasts to it.
            _app.AddComponent<RectMask2D>();
            var backdrop = _app.AddComponent<Image>();
            backdrop.color = new Color(0.03f, 0.03f, 0.03f, 1f);
            backdrop.raycastTarget = true; // a miss on the app never falls through to the hidden tabs

            _frame = new GameObject(Owned + "Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            _frame.SetParent(_app.transform, false);
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            _frame.gameObject.layer = _t.UiLayer;
            FollowHomeRect();

            foreach (UnityEvent pressed in _t.MeshHome)
            {
                pressed.AddListener(OnMeshHome);
            }
        }

        /// <summary>The world map globe (Interface\WorldMap\WorldMap-Icon.blp), upright, once the game files are open.</summary>
        private void SetIcon()
        {
            if (_buttonIcon == null)
            {
                return;
            }

            if (AppIcon != null)
            {
                if (_iconSprite == null || _iconSprite.texture != AppIcon)
                {
                    _iconSprite = Sprite.Create(AppIcon, new Rect(0f, 0f, AppIcon.width, AppIcon.height), new Vector2(0.5f, 0.5f), 100f);
                    _iconSprite.name = Owned + "Icon";
                }

                _buttonIcon.sprite = _iconSprite;
                _buttonIcon.preserveAspect = true;
                _buttonIcon.enabled = true;
            }
            else
            {
                // Not the client's picture under our caption: no picture until the icon can be painted.
                _buttonIcon.sprite = null;
                _buttonIcon.enabled = false;
            }
        }

        /// <summary>Every MonoBehaviour outside Unity UI and TextMeshPro goes, last-added first, in up to four passes.</summary>
        private static void StripToUnityUi(GameObject root)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                int foreign = 0;
                MonoBehaviour[] all = root.GetComponentsInChildren<MonoBehaviour>(true);

                for (int i = all.Length - 1; i >= 0; i--)
                {
                    MonoBehaviour mb = all[i];

                    if (mb == null)
                    {
                        continue;
                    }

                    string assembly = mb.GetType().Assembly.GetName().Name;

                    if (assembly == "UnityEngine.UI" || assembly == "Unity.TextMeshPro")
                    {
                        continue;
                    }

                    foreign++;
                    DestroyImmediate(mb);
                }

                if (foreign == 0)
                {
                    return;
                }
            }
        }

        private void Unlink(bool restore, string why)
        {
            if (AppOpen)
            {
                Close(restore, why);
            }

            if (_t != null)
            {
                foreach (UnityEvent pressed in _t.MeshHome)
                {
                    pressed.RemoveListener(OnMeshHome);
                }
            }

            if (_button != null)
            {
                _button.SetActive(false);
                Destroy(_button);
            }

            if (_app != null)
            {
                Destroy(_app);
            }

            if (_iconSprite != null)
            {
                Destroy(_iconSprite);
            }

            bool was = _t != null;
            _button = null;
            _buttonIcon = null;
            _app = null;
            _appCanvas = null;
            _frame = null;
            _iconSprite = null;
            _t = null;
            TabletOpen = false;

            if (was)
            {
                WowLog.Info("Tablet: unlinked (" + why + ").");

                if (!_ending && LinkChanged != null)
                {
                    LinkChanged();
                }
            }
        }

        // ------------------------------------------------------------------ showing and hiding

        private void OnAppButton()
        {
            Open("app button");
        }

        private void OnBack()
        {
            Close(true, "Back");
        }

        private void OnMeshHome()
        {
            Close(true, "mesh Home");
        }

        public void OpenApp()
        {
            Open("world");
        }

        public void CloseApp()
        {
            Close(true, "world");
        }

        private void Open(string why)
        {
            if (!IsLinked || AppOpen || _app == null)
            {
                return;
            }

            _tabSnapshot = SnapshotTabs();
            _t.Tabs.gameObject.SetActive(false);
            _app.SetActive(true);
            SetLayer(_app.transform, _t.UiLayer);
            _app.layer = _t.HomeCanvas.gameObject.layer;

            if (_t.Back != null)
            {
                _backWasActive = _t.Back.gameObject.activeSelf;
                _t.Back.gameObject.SetActive(true); // TabletStatusBarView hides it on HOME
                _t.Back.onClick.AddListener(OnBack);
            }

            float width = _t.Home != null ? _t.Home.rect.width : 0f;
            string size = width > LargeAbove ? "Large" : "Small";

            if (ForceLarge && _t.Resize != null && width <= LargeAbove && Time.unscaledTime >= _resizeSettleUntil)
            {
                _t.Resize.onClick.Invoke(); // TabletResize toggles Small <-> Large
                _enlarged = true;
                _resizeSettleUntil = Time.unscaledTime + 0.35f;
                size = "Small->Large";
            }

            AppOpen = true;
            WowLog.Info("Tablet: WoW Map opened (" + why + ", size " + size + ").");

            if (AppShown != null)
            {
                AppShown(true);
            }
        }

        private void Close(bool restoreSize, string why)
        {
            if (!AppOpen)
            {
                return;
            }

            AppOpen = false;

            if (_app != null) _app.SetActive(false);
            if (_t != null && _t.Tabs != null) _t.Tabs.gameObject.SetActive(true);

            if (_t != null && _t.Back != null)
            {
                _t.Back.onClick.RemoveListener(OnBack);
                _t.Back.gameObject.SetActive(_backWasActive);
            }

            bool shrunk = false;

            if (restoreSize && _enlarged && _t != null && _t.Resize != null && IsLargeNow())
            {
                _t.Resize.onClick.Invoke();
                _resizeSettleUntil = Time.unscaledTime + 0.35f;
                shrunk = true;
            }

            _enlarged = false;
            WowLog.Info("Tablet: WoW Map closed (" + why + (shrunk ? ", size Large->Small" : "") + ").");

            if (!_ending && AppShown != null)
            {
                AppShown(false);
            }
        }

        private bool IsLargeNow()
        {
            if (Time.unscaledTime < _resizeSettleUntil)
            {
                return true; // our own press is still tweening
            }

            RectTransform measured = _t.Home != null ? _t.Home : _t.StatusBar;
            return measured != null && measured.rect.width > LargeAbove;
        }

        // ---- Lead (2026-09-27, the user: "when im grabbing the somnium tablet, it stops the spell wrist menu, it is hard to grab the
        // menu without casting a spell"): where the client's tablet is, for VrTabletGuard (the wrist spell buttons, the grip menus,
        // the trigger ray pause at the tablet). All read-only; nothing on the tablet is changed.
        private static readonly Vector3[] _guardCorners = new Vector3[4];

        /// <summary>Lead 2026-10-05: the client's VR tablet is open (or held) now.</summary>
        public static bool TabletIsOpen
        {
            get
            {
                WowMapTabletLink i = Instance;
                if (i == null || i._t == null || i._t.Desktop) return false;
                try { return i.TabletOpen || TabletHeld; } catch (Exception) { return false; }
            }
        }

        /// <summary>A hand holds the client's tablet now (its XRGrabInteractable is selected).</summary>
        public static bool TabletHeld
        {
            get
            {
                WowMapTabletLink i = Instance;
                if (i == null || i._t == null || i._t.Grab == null) return false;
                try { return i._t.Grab.isSelected; } catch (Exception) { return false; }
            }
        }

        /// <summary>The open or held VR tablet's world box (its grab collider + its home screen); false = none / desktop / closed.</summary>
        public static bool TryTabletBounds(out Bounds b)
        {
            b = default(Bounds);
            WowMapTabletLink i = Instance;
            if (i == null || i._t == null || i._t.Root == null || i._t.Desktop) return false;
            if (!i.TabletOpen && !TabletHeld) return false;
            bool have = false;
            if (i._t.GrabCollider != null && i._t.GrabCollider.activeInHierarchy)
            {
                Collider c = i._t.GrabCollider.GetComponent<Collider>();
                if (c != null && c.enabled) { b = c.bounds; have = true; }
            }
            RectTransform r = i._t.Home != null ? i._t.Home : i._t.StatusBar;
            if (r != null && r.gameObject.activeInHierarchy)
            {
                r.GetWorldCorners(_guardCorners);
                for (int k = 0; k < 4; k++)
                {
                    if (!have) { b = new Bounds(_guardCorners[k], Vector3.zero); have = true; }
                    else b.Encapsulate(_guardCorners[k]);
                }
            }
            return have;
        }

        /// <summary>The point is within margin metres of the open / held tablet.</summary>
        public static bool NearTablet(Vector3 point, float margin)
        {
            Bounds b;
            if (!TryTabletBounds(out b)) return false;
            b.Expand(margin * 2f);
            return b.Contains(point);
        }

        /// <summary>The ray meets the open / held tablet closer than maxDistance.</summary>
        public static bool RayHitsTablet(Ray ray, float maxDistance)
        {
            Bounds b;
            if (!TryTabletBounds(out b)) return false;
            float d;
            return b.IntersectRay(ray, out d) && d <= maxDistance;
        }

        private bool ReadTabletOpen()
        {
            if (_t.Animator != null && _t.AnimatorHasIsOpen)
            {
                return _t.Animator.GetBool(IsOpenHash);
            }

            if (_t.CanvasesGroup != null)
            {
                // The close animation fades the Canvases CanvasGroup to alpha 0, not interactable.
                return _t.CanvasesGroup.alpha > 0.5f && _t.CanvasesGroup.blocksRaycasts;
            }

            return _t.GrabCollider == null || _t.GrabCollider.activeSelf;
        }

        private bool[] SnapshotTabs()
        {
            var shot = new bool[_t.TabContents.Count];

            for (int i = 0; i < shot.Length; i++)
            {
                GameObject content = _t.TabContents[i];
                shot[i] = content != null && content.activeSelf;
            }

            return shot;
        }

        private bool TabsChangedSinceOpen()
        {
            if (_tabSnapshot == null || _tabSnapshot.Length != _t.TabContents.Count)
            {
                return false;
            }

            for (int i = 0; i < _tabSnapshot.Length; i++)
            {
                GameObject content = _t.TabContents[i];

                if ((content != null && content.activeSelf) != _tabSnapshot[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void FollowHomeRect()
        {
            if (_app == null || _t.Home == null)
            {
                return;
            }

            // A home canvas caught mid close-tween (scale towards 0) is not copied.
            var rect = (RectTransform)_app.transform;

            if (rect.parent == null || _t.Home.lossyScale.x < rect.parent.lossyScale.x * 0.5f)
            {
                return;
            }

            Vector2 size = _t.Home.rect.size;
            rect.localScale = Vector3.one;
            rect.pivot = _t.Home.pivot;
            rect.sizeDelta = size;
            rect.SetPositionAndRotation(_t.Home.position, _t.Home.rotation);

            if (_frame != null)
            {
                float fit = Mathf.Min(size.x / FrameWidth, size.y / FrameHeight); // Small 960 x 1080: 1.5; Large 1920 x 1080: 1.5
                _frame.localScale = new Vector3(fit, fit, 1f);
            }
        }

        private static void SetLayer(Transform t, int layer)
        {
            // The canvas root keeps the HomeTab canvas's layer; every graphic under it takes the layer the client's own graphics
            // carry (5, UI), which the XR ray tests per graphic.
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);

                if (child.gameObject.layer != layer)
                {
                    child.gameObject.layer = layer;
                }

                SetLayer(child, layer);
            }
        }
    }
}
