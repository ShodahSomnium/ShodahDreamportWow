using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Stage O2 (owner ghost): what a ghost sees, for both sessions, from PlayerState only (Ghost, HasCorpse, CorpseMapId, CorpsePos,
    /// ReclaimDelay, CanReclaim):
    /// - the corpse direction marker: WoW's own minimap corpse arrow (Interface\Minimap\ROTATING-MINIMAPCORPSEARROW.BLP, 32x32, points up -
    ///   measured) turned towards the corpse relative to where the player looks, "Corpse" (GlobalStrings CORPSE_RED) with the distance in
    ///   yards, and while the reclaim delay runs RECOVER_CORPSE_TIMER "%d %s until resurrection" (the popup's delay text, shown here because
    ///   the popup only opens with "Resurrect now?"). Desktop: top centre of the HUD (ours; the minimap has the top right). VR: a small head-locked
    ///   compass below the centre of view (drawn over everything, like the low-health vignette), placed in LateUpdate and before rendering.
    /// - a marker over the corpse in the world (both modes; ours - WoW marks the corpse on the minimap and the world map only): the world
    ///   map's corpse icon (Interface\Minimap\POIIcons, WorldMapFrameTemplates.xml WorldMapCorpseTemplate TexCoords) and the distance,
    ///   facing the viewer at a constant angular size, drawn over everything so the corpse is found behind hills.
    /// - the ghost-world look (UiGhostLook): a global URP Volume of our own - the technique Somnium Euro Truck (EtsPostFx) proved in the
    ///   client and Rec Room (RrPostFx) reuses: layer 0 (the client cameras' volume mask), priority 100, a runtime profile. Desktop and
    ///   VR alike; the avatar and the client are never touched. Visitors never run the UI, so they never get it.
    /// - the per-frame refresh of the death popup (UiDeath.Tick).
    /// One component per layout, on the layout's root object (active exactly while that layout is shown).
    /// </summary>
    public sealed class UiGhost : MonoBehaviour
    {
        public const string CorpseArrowTex = @"Interface\Minimap\ROTATING-MINIMAPCORPSEARROW.BLP";
        public const string PoiIconsTex = @"Interface\Minimap\POIIcons.blp";
        // WorldMapFrameTemplates.xml WorldMapCorpseTemplate: <TexCoords left="0.56640625" right="0.6328125" top="0.00390625" bottom="0.0703125"/>
        private const float PoiL = 0.56640625f, PoiR = 0.6328125f, PoiT = 0.00390625f, PoiB = 0.0703125f;
        private static readonly Color CorpseRed = new Color(1f, 0.125f, 0.125f);   // CORPSE_RED "|cffff2020Corpse|r" (GlobalStrings.lua:1866)

        // Ours (placement / sizes).
        public const float CompassDistance = 0.8f, CompassDrop = 0.2f, CompassMetresPerUnit = 0.0011f;
        public const float MarkerLift = 2f, MarkerAngle = 0.0009f, MarkerMinScale = 0.004f, MarkerHideWithin = 4f;

        private UiDeath _popup;
        private UiContext _ctx;
        private bool _vr;
        private Transform _armUi;

        private RectTransform _desk;
        private RawImage _deskArrow;
        private TextMeshProUGUI _deskName, _deskInfo;
        private UiSurface _compass;
        private RawImage _vrArrow;
        private TextMeshProUGUI _vrName, _vrInfo;
        private UiSurface _marker;
        private TextMeshProUGUI _markerText;

        private bool _shown, _markerOn, _built;
        private int _yardsShown = int.MinValue, _delayShown = int.MinValue;
        private bool _canShown, _instShown;
        private Vector3 _corpseUnity;
        private float _arrowAngle;

        /// <summary>Called by UiDeath's constructor while a layout is being built (UiKit.Surface = the popup's surface).</summary>
        public static void Attach(UiDeath popup, UiContext ctx, UiSurface surface)
        {
            if (surface == null || surface.Root == null) return;
            try
            {
                // Desktop: the HUD's screen canvas is the layout root (its parent is the WowArmUi object). VR: the popup's world canvas
                // hangs under the layout root ("WoW Arm UI (VR)"), whose parent is the WowArmUi object.
                Transform host = surface.IsWorld ? surface.Root.parent : surface.Root;
                if (host == null) return;
                var g = host.gameObject.AddComponent<UiGhost>();
                g._popup = popup;
                g._ctx = ctx;
                g._vr = surface.IsWorld;
                g._armUi = host.parent != null ? host.parent : host;
                g.Build(surface);
            }
            catch (Exception e) { WowLog.Warn("UI: the ghost HUD could not be built: " + e.Message); }
        }

        private void Build(UiSurface surface)
        {
            UiSurface oldSurface = UiKit.Surface;
            bool oldOver = UiKit.Over, oldPointable = UiKit.Pointable;
            try
            {
                UiKit.Pointable = false;
                if (!_vr)
                {
                    UiKit.Over = false;
                    // lead 2026-10-03: top centre (was top right, which the minimap - UiMinimap, MinimapCluster TOPRIGHT - now uses);
                    // y -12 .. -88 stays clear of the popups (TOP -135, UiLayouts) and the error text (TOP -122). Ours.
                    _desk = surface.Anchor("Corpse Marker", new Vector2(0.5f, 1f), 160f, 76f, 0f, -12f);
                    _deskArrow = Arrow(_desk, 80f, 20f);
                    _deskName = UiKit.Text(_desk, "Corpse", "", 12f, Color.white, TextAlignmentOptions.Center, 0f, 42f, 160f, 16f);
                    _deskInfo = UiKit.Text(_desk, "Delay", "", 11f, UiArt.InfoYellow, TextAlignmentOptions.Center, 0f, 58f, 160f, 16f);
                    _desk.gameObject.SetActive(false);
                }
                else
                {
                    UiKit.Over = true;   // over everything, like the vignette and the arm panel
                    _compass = UiSurface.MakeWorld("WoW Corpse Compass", _armUi, 160f, 76f, CompassMetresPerUnit, false, 206);
                    UiKit.Surface = _compass;
                    _vrArrow = Arrow(_compass.Content, 80f, 20f);
                    _vrName = UiKit.Text(_compass.Content, "Corpse", "", 13f, Color.white, TextAlignmentOptions.Center, 0f, 42f, 160f, 16f);
                    _vrInfo = UiKit.Text(_compass.Content, "Delay", "", 12f, UiArt.InfoYellow, TextAlignmentOptions.Center, 0f, 58f, 160f, 16f);
                    _compass.SetShown(false);
                }
                UiKit.Over = true;
                _marker = UiSurface.MakeWorld("WoW Corpse Marker", _armUi, 140f, 100f, 0.01f, false, 98);
                UiKit.Surface = _marker;
                Texture2D poi = UiArt.Tex(PoiIconsTex);
                UiKit.Pic(_marker.Content, "Corpse icon", poi, PoiL, PoiR, PoiT, PoiB, 46f, 0f, 48f, 48f);
                _markerText = UiKit.Text(_marker.Content, "Corpse", "", 18f, CorpseRed, TextAlignmentOptions.Center, 0f, 50f, 140f, 48f);
                _marker.SetShown(false);
                _built = true;
            }
            finally
            {
                UiKit.Surface = oldSurface;
                UiKit.Over = oldOver;
                UiKit.Pointable = oldPointable;
            }
        }

        /// <summary>The corpse arrow, pivot in its centre so it turns in place (UiKit rects pivot at their top-left).</summary>
        private static RawImage Arrow(Transform parent, float cx, float cy)
        {
            RawImage img = UiKit.Pic(parent, "Corpse arrow", UiArt.Tex(CorpseArrowTex), 0f, 0f, 36f, 36f);
            RectTransform r = img.rectTransform;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(cx, -cy);
            return img;
        }

        /// <summary>
        /// The text of a pending resurrection request (online: SMSG_RESURRECT_REQUEST -> the session's Info line RESURRECT_REQUEST /
        /// _NO_SICKNESS "%s wants to resurrect you", owner lootdeath), shown by the popup while PlayerState.CanReclaim offers it; null = none.
        /// </summary>
        public static string ResurrectRequestText { get; private set; }

        private void OnEnable()
        {
            Application.onBeforeRender += BeforeRender;
            WowPlay.Event += OnGameEvent;
            WowPlay.Ended += OnEnded;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            WowPlay.Event -= OnGameEvent;
            WowPlay.Ended -= OnEnded;
            HideAll();
            UiGhostLook.Release(this);
        }

        private static void OnEnded() { ResurrectRequestText = null; }

        private static void OnGameEvent(GameEvent e)
        {
            switch (e.Type)
            {
                case GameEventType.Info:
                    if (e.Text != null && e.Text.IndexOf("wants to resurrect you", StringComparison.Ordinal) >= 0) ResurrectRequestText = e.Text;
                    break;
                case GameEventType.PlayerDied:
                case GameEventType.PlayerResurrected:
                case GameEventType.SpiritReleased:
                    ResurrectRequestText = null;
                    break;
            }
        }

        private void OnDestroy()
        {
            UiGhostLook.Release(this);
            if (_marker != null && _marker.Canvas != null) Destroy(_marker.Canvas.gameObject);
            if (_compass != null && _compass.Canvas != null) Destroy(_compass.Canvas.gameObject);
        }

        private void HideAll()
        {
            if (_desk != null && _desk.gameObject.activeSelf) _desk.gameObject.SetActive(false);
            if (_compass != null) _compass.SetShown(false);
            if (_marker != null) _marker.SetShown(false);
            _markerOn = false;
        }

        private void Update()
        {
            if (!_built) return;
            float now = Time.unscaledTime;
            try { if (_popup != null) _popup.Tick(now); }
            catch (Exception e) { LogOnce("popup", e); }
            IWowSession s = _ctx != null ? _ctx.S : null;
            PlayerState p = s != null ? s.Player : null;
            bool ghost = p != null && p.Ghost;
            try { UiGhostLook.Drive(_armUi, ghost, Time.unscaledDeltaTime, this); }
            catch (Exception e) { LogOnce("look", e); }

            bool corpse = ghost && p.HasCorpse && p.CorpseMapId == s.MapId;
            if (corpse != _shown)
            {
                _shown = corpse;
                if (corpse)
                    WowLog.Info("UI: ghost HUD on - corpse marker (" + (_vr ? "VR head-locked compass" : "desktop HUD, top centre") + ") + a marker over the corpse "
                        + p.CorpsePos + ", " + Yards(p).ToString("0") + " yd away; reclaim delay " + p.ReclaimDelay.ToString("0") + " s.");
                else if (p != null) WowLog.Info("UI: ghost HUD off (" + (p.Ghost ? "no corpse on this map" : p.Dead ? "dead" : "alive") + ").");
                _yardsShown = _delayShown = int.MinValue;
            }
            if (!corpse) { HideAll(); return; }
            _corpseUnity = WowSpace.ToUnity(s.MapId, p.CorpsePos.X, p.CorpsePos.Y, p.CorpsePos.Z);
            int yards = Mathf.RoundToInt(Yards(p));
            int delay = p.ReclaimDelay > 0f ? Mathf.CeilToInt(p.ReclaimDelay) : 0;
            if (yards != _yardsShown || delay != _delayShown || p.CanReclaim != _canShown || p.CorpseInInstance != _instShown)
            {
                _yardsShown = yards;
                _delayShown = delay;
                _canShown = p.CanReclaim;
                _instShown = p.CorpseInInstance;
                string name = "<color=#ff2020>Corpse</color>  " + yards + " yd";   // CORPSE (GlobalStrings.lua:1865), coloured as CORPSE_RED
                string info = delay > 0 ? UiDeath.TimeText("%d %s until resurrection", p.ReclaimDelay)   // RECOVER_CORPSE_TIMER (GlobalStrings.lua:6036)
                    : p.CanReclaim ? "Resurrect now?"                                                     // RECOVER_CORPSE (GlobalStrings.lua:6034)
                    : p.CorpseInInstance ? "You must enter the instance to recover your corpse" : "";   // iteration 11: RECOVER_CORPSE_INSTANCE (GlobalStrings.lua:6035)
                if (_deskName != null) { _deskName.text = name; _deskInfo.text = info; }
                if (_vrName != null) { _vrName.text = name; _vrInfo.text = info; }
                if (_markerText != null) _markerText.text = "Corpse\n<size=14><color=#ffffff>" + yards + " yd</color></size>";
            }
            if (_desk != null && !_desk.gameObject.activeSelf) _desk.gameObject.SetActive(true);
            if (_compass != null && !_compass.Shown) _compass.SetShown(true);
            Place();
        }

        private static float Yards(PlayerState p)
        {
            float dx = p.Pos.X - p.CorpsePos.X, dy = p.Pos.Y - p.CorpsePos.Y, dz = p.Pos.Z - p.CorpsePos.Z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void LateUpdate()
        {
            if (_shown) Place();
        }

        private void BeforeRender()
        {
            if (!_shown || !isActiveAndEnabled) return;
            try { Place(); }
            catch (Exception) { }
        }

        /// <summary>The arrow's bearing, the VR compass on the head, the world marker over the corpse facing the eyes.</summary>
        private void Place()
        {
            Camera cam = WowPlayer.Head();
            if (cam == null) return;
            Transform head = cam.transform;
            Vector3 to = _corpseUnity - head.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            Vector3 fwd = new Vector3(head.forward.x, 0f, head.forward.z);
            if (fwd.sqrMagnitude < 1e-6f) fwd = new Vector3(head.up.x, 0f, head.up.z);
            _arrowAngle = flat.sqrMagnitude > 1e-6f && fwd.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(fwd, flat, Vector3.up) : 0f;
            Quaternion spin = Quaternion.Euler(0f, 0f, -_arrowAngle);   // the arrow points up = straight ahead; clockwise = to the right
            if (_deskArrow != null) _deskArrow.rectTransform.localRotation = spin;
            if (_vrArrow != null) _vrArrow.rectTransform.localRotation = spin;
            if (_compass != null && _compass.Shown)
                _compass.Root.SetPositionAndRotation(head.position + head.forward * CompassDistance - head.up * CompassDrop, head.rotation);

            float dist = to.magnitude;
            bool marker = dist > MarkerHideWithin;
            if (marker != _markerOn) { _markerOn = marker; _marker.SetShown(marker); }
            if (!marker) return;
            Vector3 at = _corpseUnity + Vector3.up * MarkerLift;
            Vector3 look = at - head.position;
            if (look.sqrMagnitude < 1e-6f) return;
            Transform m = _marker.Root;
            m.SetPositionAndRotation(at, Quaternion.LookRotation(look.normalized, Vector3.up));
            float parentScale = m.parent != null ? Mathf.Max(1e-4f, m.parent.lossyScale.x) : 1f;
            m.localScale = Vector3.one * (Mathf.Max(MarkerMinScale, MarkerAngle * look.magnitude) / parentScale);
        }

        private static bool _failed;

        private static void LogOnce(string what, Exception e)
        {
            if (_failed) return;
            _failed = true;
            WowLog.Warn("UI: ghost HUD " + what + " failed (logged once): " + e.Message);
        }
    }

    /// <summary>
    /// The ghost-world look: WoW greys the world out for the dead (the "Death Effect" of its video options, DEATH_EFFECT) and lights it
    /// with the death light (Light.dbc LightParamsID[4] - measured: LightParams 3 Kalimdor / 4 Eastern Kingdoms / 2 Outland, a blue
    /// 5E99C6 diffuse, 1A3855 ambient and 00536F fog). Here one global URP Volume of our own with ColorAdjustments only: saturation and a
    /// blue colour filter derived from that death diffuse. The values are ours (the client's death shader is not readable data), named
    /// and logged. Fades in over FadeIn seconds at the release, out over FadeOut at the resurrection. The Volume is created on the first
    /// ghost and kept (weight 0, disabled) for the rest of the play session.
    /// </summary>
    internal static class UiGhostLook
    {
        public const float Saturation = -80f;                                   // ours
        public static readonly Color Filter = new Color(0.74f, 0.89f, 1f);      // ours: halfway from white to 5E99C6 normalised to its blue (0.48, 0.78, 1)
        public const float FadeIn = 1.5f, FadeOut = 0.6f;                       // ours, seconds
        public const float Priority = 100f;                                     // EtsPostFx / RrPostFx

        private static GameObject _go;
        private static Volume _vol;
        private static VolumeProfile _profile;
        private static float _weight;
        private static UiGhost _owner;

        public static float Weight { get { return _weight; } }

        public static void Drive(Transform parent, bool ghost, float dt, UiGhost who)
        {
            if (_owner != null && _owner != who && _owner.isActiveAndEnabled) return;   // the other layout drives it
            _owner = who;
            if (ghost && _vol == null) Build(parent);
            if (_vol == null) return;
            float target = ghost ? 1f : 0f;
            float before = _weight;
            _weight = Mathf.MoveTowards(_weight, target, Mathf.Max(0f, dt) / (ghost ? FadeIn : FadeOut));
            _vol.weight = _weight;
            bool on = _weight > 0.001f;
            if (_vol.enabled != on) _vol.enabled = on;
            if (before < 1f && _weight >= 1f) WowLog.Info("UI: ghost-world look in (post volume weight 1).");
            if (before > 0f && _weight <= 0f) WowLog.Info("UI: ghost-world look out (weight 0).");
        }

        public static void Release(UiGhost who)
        {
            if (_owner != who) return;
            _owner = null;
            _weight = 0f;
            if (_vol != null) { _vol.weight = 0f; _vol.enabled = false; }
        }

        private static void Build(Transform parent)
        {
            _go = new GameObject("WoW Ghost Look") { layer = 0 };   // the client cameras' volume mask is Default only (EtsPostFx)
            if (parent != null) _go.transform.SetParent(parent, false);
            _go.layer = 0;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "WoW ghost look";
            var adjust = _profile.Add<ColorAdjustments>(false);
            adjust.saturation.Override(Saturation);
            adjust.colorFilter.Override(Filter);
            _vol = _go.AddComponent<Volume>();
            _vol.isGlobal = true;
            _vol.priority = Priority;
            _vol.weight = 0f;
            // The client's AO setting adds HBAO to every global Volume's .profile, which silently clones a shared profile (RrPostFx):
            // ours is both, so its writes and ours reach the same profile. We never touch HBAO.
            _vol.sharedProfile = _profile;
            _vol.profile = _profile;
            _vol.enabled = false;
            WowLog.Info("UI: ghost-world look ready - global post volume on layer 0, priority " + Priority + ": saturation " + Saturation + ", colour filter ("
                + Filter.r.ToString("0.00") + ", " + Filter.g.ToString("0.00") + ", " + Filter.b.ToString("0.00") + "), fade in " + FadeIn + " s / out " + FadeOut + " s (values ours).");
        }
    }
}
