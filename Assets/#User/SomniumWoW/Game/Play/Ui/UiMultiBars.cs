// iteration 11 - owner vrcast (proposal-vrcast.md 5.4 / 6.4): UiBars (the main bar's page, the form's bonus bar, which extra bars show and how
// far other frames move for them - UIParent.lua:1161-1202 / 1755-1795), UiMultiBars (MultiBarBottomLeft / BottomRight / Right / Left on the
// desktop, MultiActionBars.xml) and UiPager (ActionBarUpButton / DownButton + MainMenuBarPageNumber, ActionBarFrame.xml:149-198,
// MainMenuBar.xml:316-324). Built by uicore's UiDesktopLayout (hook 8); nothing of this is drawn in VR (the VR rows are the bars there).
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public static class UiBars
    {
        /// <summary>UIParent.lua:1161 actionBarOffset.</summary>
        public const float ActionBarOffset = 45f;
        /// <summary>UIParent.lua:1211-1222: the "pet" flag x35 (a pet / stance / possess / totem bar shown).</summary>
        public const float PetOffset = 35f;

        public static int Page = 1;                                   // 1..6 (Shift+1..6, the arrows); client state as in WoW (not saved, starts at 1)
        private static int _bonus, _frame = -1, _desktopFrame = -100, _stanceFrame = -100, _logSig = int.MinValue;
        private static bool _petBar;

        /// <summary>The form's bonus action bar (SpellShapeshiftForm field 1, 1..4; 0 none), refreshed by Tick.</summary>
        public static int BonusBar { get { return _bonus; } }
        /// <summary>The page the main bar shows (ActionButton.lua:131-149): page 1 in a form with a bonus bar shows page 6 + bonus.</summary>
        public static int MainPage { get { return WowActionSlots.MainPage(Page, BonusBar); } }
        // what is shown on the monitor (from WowSettings while the desktop bars are built; read by others to move their frames)
        public static bool ShowBottomLeft, ShowBottomRight, ShowRight, ShowRight2, StanceShown;

        /// <summary>CastingBarFrame: +45 with either bottom bar (bottomEither), +35 with a pet / stance bar (pet) - except the pet part with
        /// only the bottom RIGHT bar (UIParent.lua:1266-1270).</summary>
        public static float CastBarLift
        {
            get
            {
                bool either = ShowBottomLeft || ShowBottomRight, pet = _petBar || StanceShown;
                float y = (either ? ActionBarOffset : 0f) + (pet ? PetOffset : 0f);
                if (either && ShowBottomRight && pet && !ShowBottomLeft) y -= PetOffset;
                return y;
            }
        }
        /// <summary>CONTAINER_OFFSET_Y: +45 with either bottom bar (UIParent.lua:1199).</summary>
        public static float ContainerLiftY { get { return ShowBottomLeft || ShowBottomRight ? ActionBarOffset : 0f; } }
        /// <summary>CONTAINER_OFFSET_X: rightRight 48 with the Right Bar only, rightLeft 93 with both right bars (UIParent.lua:1198).</summary>
        public static float ContainerShiftX { get { return ShowRight2 ? 2f * ActionBarOffset + 3f : ShowRight ? ActionBarOffset + 3f : 0f; } }
        /// <summary>PETACTIONBAR_YPOS: +45 with the Bottom Left Bar (UIParent.lua:1201).</summary>
        public static float PetBarLift { get { return ShowBottomLeft ? ActionBarOffset : 0f; } }
        /// <summary>ChatFrame1: bottomLeft = actionBarOffset - 8 (UIParent.lua:1187).</summary>
        public static float ChatLift { get { return ShowBottomLeft ? ActionBarOffset - 8f : 0f; } }

        /// <summary>Once per frame (WowArmUi, WowVrInput and the bars call it; frame-guarded).</summary>
        public static void Tick(PlayerState p)
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            int bonus = 0;
            if (p != null && p.ShapeshiftForm > 0 && WowPlay.Files != null)
            {
                SpellDb db = SpellDb.Get(WowPlay.Files);
                ShapeshiftInfo form = db != null ? db.Form(p.ShapeshiftForm) : null;
                if (form != null && form.BonusActionBar > 0 && form.BonusActionBar <= WowActionSlots.MaxBonusBar) bonus = form.BonusActionBar;
            }
            _bonus = bonus;
            bool desk = f - _desktopFrame <= 2;
            ShowBottomLeft = desk && WowSettings.ShowBottomLeftBar;
            ShowBottomRight = desk && WowSettings.ShowBottomRightBar;
            ShowRight = desk && WowSettings.ShowRightBar;
            ShowRight2 = ShowRight && WowSettings.ShowRightBar2;   // MultiBarLeft shows only with MultiBarRight (MultiActionBars.lua:48)
            StanceShown = desk && f - _stanceFrame <= 2;
            _petBar = desk && p != null && (p.ControlBar != null || p.PetGuid != 0);
            // The offline session's safety net for the first fill of a form's bar (its hook sits where ShapeshiftForm is written).
            var os = WowPlay.Session as OfflineSession;
            if (os != null) os.AbFormBarCheck();
            int sig = (ShowBottomLeft ? 1 : 0) | (ShowBottomRight ? 2 : 0) | (ShowRight ? 4 : 0) | (ShowRight2 ? 8 : 0) | (WowSettings.LockActionBars ? 16 : 0)
                      | (WowSettings.AlwaysShowActionBars ? 32 : 0) | (desk ? 64 : 0) | Page << 8 | _bonus << 12;
            if (sig != _logSig)
            {
                _logSig = sig;
                if (desk)
                    WowLog.Info("UI: action bars - Bottom Left " + OnOff(ShowBottomLeft) + ", Bottom Right " + OnOff(ShowBottomRight) + ", Right " + OnOff(ShowRight)
                                + ", Right 2 " + OnOff(ShowRight2) + "; lock " + OnOff(WowSettings.LockActionBars) + "; always show " + OnOff(WowSettings.AlwaysShowActionBars)
                                + "; page " + Page + (_bonus > 0 ? ", form bar " + _bonus + " (main bar = page " + MainPage + ")" : ""));
            }
        }

        private static string OnOff(bool b) { return b ? "on" : "off"; }

        /// <summary>The desktop bars were refreshed this frame (UiMultiBars.Refresh).</summary>
        internal static void NoteDesktop() { _desktopFrame = Time.frameCount; }
        /// <summary>The stance bar shows (UiStanceBar.Refresh, at least one form).</summary>
        internal static void NoteStance() { _stanceFrame = Time.frameCount; }

        /// <summary>WoW: ChangeActionBarPage (the ACTIONPAGE1..6 bindings call it directly, Bindings.xml:311-328; the arrows play
        /// UChatScrollButton first, ActionBarFrame.xml:166).</summary>
        public static void SetPage(int page)
        {
            if (page < 1 || page > WowActionSlots.Pages || page == Page) return;
            Page = page;
        }

        /// <summary>The pages the arrows may show: a page whose extra bar is on screen is skipped (VIEWABLE_ACTION_BAR_PAGES set nil for a
        /// shown bar, MultiActionBars.lua:24-54).</summary>
        private static bool Viewable(int page)
        {
            switch (page)
            {
                case WowActionSlots.PageBottomLeft: return !ShowBottomLeft;
                case WowActionSlots.PageBottomRight: return !ShowBottomRight;
                case WowActionSlots.PageRight: return !ShowRight;
                case WowActionSlots.PageRight2: return !ShowRight2;
                default: return true;
            }
        }

        /// <summary>ActionBar_PageUp (ActionButton.lua:45-57): the next viewable page, else page 1.</summary>
        public static void PageUp()
        {
            int next = 0;
            for (int i = Page + 1; i <= WowActionSlots.Pages; i++) if (Viewable(i)) { next = i; break; }
            if (next == 0) next = 1;
            SetPage(next);
        }

        /// <summary>ActionBar_PageDown (ActionButton.lua:59-78): the previous viewable page, else the last viewable one.</summary>
        public static void PageDown()
        {
            int prev = 0;
            for (int i = Page - 1; i >= 1; i--) if (Viewable(i)) { prev = i; break; }
            if (prev == 0) for (int i = WowActionSlots.Pages; i >= 1; i--) if (Viewable(i)) { prev = i; break; }
            if (prev != 0) SetPage(prev);
        }

        private static readonly KeyCode[] StanceKeys =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10
        };
        private static bool _keysLogged;

        /// <summary>
        /// Iteration 11 (vrcast hook in WowArmUi.Keys, PLAN11 s2 keys): the twelve action keys with WoW's modifiers - plain = the main bar
        /// (or the vehicle bar while it has the keys, pets H), Shift+1..6 = bar page 1..6 (WoW's SHIFT-1..6 ACTIONPAGE1..6), Alt+1..= = the
        /// Bottom Left Bar (ours, U2), Ctrl+1..0 = the pet bar (WoW's BONUSACTIONBUTTON1..10 keys, pets); Ctrl+F1..F10 = the stance bar
        /// (SHAPESHIFTBUTTON1..10). keys = WowArmUi.ActionKeys (1..0, -, =). Call once per frame on the desktop when no text field has the keyboard.
        /// </summary>
        public static void Keys(IWowSession s, KeyCode[] keys, UiActionBar main, UiMultiBars multi, UiStanceBar stance)
        {
            if (s == null || keys == null) return;
            if (!_keysLogged)
            {
                _keysLogged = true;
                WowLog.Info("UI: action keys - 1..= main bar, Shift+1..6 bar page, Alt+1..= Bottom Left Bar (ours), Ctrl+1..0 pet bar, Ctrl+F1..F10 stance bar");
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            for (int i = 0; i < keys.Length && i < WowActionSlots.PerBar; i++)
            {
                if (!Input.GetKeyDown(keys[i])) continue;
                if (ctrl) { if (i < 10) UiPetBar.PressKey(s, i); continue; }
                if (shift) { if (i < WowActionSlots.Pages) SetPage(i + 1); continue; }
                if (alt)
                {
                    if (multi != null && ShowBottomLeft && multi.BottomLeft != null) multi.BottomLeft.Slots[i].B.Press(false);
                    continue;
                }
                if (UiVehicleBar.TryPressKey(s, i)) continue;
                if (main != null) main.Slots[i].B.Press(false);
            }
            if (ctrl && stance != null)
                for (int i = 0; i < StanceKeys.Length; i++)
                    if (Input.GetKeyDown(StanceKeys[i])) stance.Press(i);
        }
    }

    /// <summary>
    /// The Bottom Left / Bottom Right / Right / Right 2 bars on the desktop (built by uicore's UiDesktopLayout after the main bar). Positions
    /// (MultiActionBars.xml:508-541, UIParent.lua:1180-1181): MultiBarBottomLeft 17 above ActionButton1's top (buttons x 8 + 42 i), MultiBarBottomRight
    /// 10 right of it (x 518 + 42 i), MultiBarRight BOTTOMRIGHT (-7, 98) of the screen (buttons from its top, 42 down), MultiBarLeft 5 left of it.
    /// Our XP bar never turns into WoW's max-level bar, so its -5 at max level (UIParent.lua:1180 maxLevel) does not apply.
    /// </summary>
    public sealed class UiMultiBars
    {
        public readonly UiActionBar BottomLeft, BottomRight, Right, Right2;
        public readonly UiPager Pager;
        private readonly RectTransform _bottom, _right;
        private readonly RectTransform _blRoot, _brRoot, _rRoot, _r2Root;

        public UiMultiBars(UiSurface s, UiContext ctx, RectTransform mainButtons)
        {
            // 1024 x 100 at the bottom centre (the main bar's 1024 x 53 and the bars above it); 100 x 600 at the bottom right.
            _bottom = s.Anchor("Multi bars bottom", new Vector2(0.5f, 0f), 1024f, 100f, 0f, 0f);
            _right = s.Anchor("Multi bars right", new Vector2(1f, 0f), 100f, 600f, 0f, 0f);
            _blRoot = SubCanvas(_bottom, "MultiBarBottomLeft", 0f, 0f, 1024f, 100f);
            _brRoot = SubCanvas(_bottom, "MultiBarBottomRight", 0f, 0f, 1024f, 100f);
            _rRoot = SubCanvas(_right, "MultiBarRight", 0f, 0f, 100f, 600f);
            _r2Root = SubCanvas(_right, "MultiBarLeft", 0f, 0f, 100f, 600f);
            // top-left coordinates: the bottom bars' buttons bottom 57 above the screen bottom (4 + 36 + 17) -> top 100 - 57 - 36 = 7
            BottomLeft = new UiActionBar(_blRoot, ctx, 8f, 7f, true, WowActionSlots.Slot(WowActionSlots.PageBottomLeft, 0), false, UiActionBar.AltKeys);
            BottomRight = new UiActionBar(_brRoot, ctx, 518f, 7f, false, WowActionSlots.Slot(WowActionSlots.PageBottomRight, 0), false, null);
            // the right bars' first button: top 98 + 500 above the screen bottom -> 600 - 598 = 2; right edge 7 (Right) / 50 (Right 2) from the screen's
            Right = new UiActionBar(_rRoot, ctx, 100f - 7f - 36f, 2f, false, WowActionSlots.Slot(WowActionSlots.PageRight, 0), true, null);
            Right2 = new UiActionBar(_r2Root, ctx, 100f - 50f - 36f, 2f, false, WowActionSlots.Slot(WowActionSlots.PageRight2, 0), true, null);
            Pager = mainButtons != null ? new UiPager(mainButtons, ctx) : null;
            WowSettings.SetLive("show_bottom_left_bar");
            WowSettings.SetLive("show_bottom_right_bar");
            WowSettings.SetLive("show_right_bar");
            WowSettings.SetLive("show_right_bar_2");
            WowSettings.SetLive("lock_action_bars");
            WowSettings.SetLive("always_show_action_bars");
            WowLog.Info("UI: the four extra action bars are built (Bottom Left 61-72 keys Alt+1..=, Bottom Right 49-60, Right 25-36, Right 2 37-48 - WoW's action numbers) and the page arrows");
        }

        private static RectTransform SubCanvas(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform r = UiKit.Rect(parent, name, x, y, w, h);
            r.gameObject.AddComponent<Canvas>();
            if (UiKit.Pointable) r.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();   // a nested canvas needs its own (WowMapTabletLink.cs:864)
            return r;
        }

        public void Refresh(PlayerState p, float now)
        {
            UiBars.NoteDesktop();
            UiBars.Tick(p);
            Set(_blRoot, BottomLeft, UiBars.ShowBottomLeft, p, now);
            Set(_brRoot, BottomRight, UiBars.ShowBottomRight, p, now);
            Set(_rRoot, Right, UiBars.ShowRight, p, now);
            Set(_r2Root, Right2, UiBars.ShowRight2, p, now);
            if (Pager != null) Pager.Refresh();
        }

        private static void Set(RectTransform root, UiActionBar bar, bool on, PlayerState p, float now)
        {
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (on && p != null) bar.Refresh(p, now);
        }
    }

    /// <summary>ActionBarUpButton / ActionBarDownButton (32x32, CENTER at (522, -22) / (522, -42) of the 1024x53 art frame, ActionBarFrame.xml:149-198)
    /// and MainMenuBarPageNumber (GameFontNormalSmall, MainMenuBar.xml:316-324) in the main bar's button rect.</summary>
    public sealed class UiPager
    {
        public const string UpUp = @"Interface\MainMenuBar\UI-MainMenu-ScrollUpButton-Up.blp";
        public const string UpDown = @"Interface\MainMenuBar\UI-MainMenu-ScrollUpButton-Down.blp";
        public const string UpHighlight = @"Interface\MainMenuBar\UI-MainMenu-ScrollUpButton-Highlight.blp";
        public const string DownUp = @"Interface\MainMenuBar\UI-MainMenu-ScrollDownButton-Up.blp";
        public const string DownDown = @"Interface\MainMenuBar\UI-MainMenu-ScrollDownButton-Down.blp";
        public const string DownHighlight = @"Interface\MainMenuBar\UI-MainMenu-ScrollDownButton-Highlight.blp";
        private readonly TextMeshProUGUI _page;
        private int _shown = -1;

        public UiPager(RectTransform mainButtons, UiContext ctx)
        {
            UiButton up = UiKit.ArtButton(mainButtons, "ActionBarUpButton", UpUp, UpDown, UiArt.ScrollUpDisabled, UpHighlight, 506f, 6f, 32f, () => { UiSound.Named("UChatScrollButton"); UiBars.PageUp(); });
            up.Tip = () => "<size=14><color=#ffffff>Next action bar page</color></size>\n<color=#ffd100>Shift+1..6 picks a page.</color>";
            UiButton down = UiKit.ArtButton(mainButtons, "ActionBarDownButton", DownUp, DownDown, UiArt.ScrollDownDisabled, DownHighlight, 506f, 26f, 32f, () => { UiSound.Named("UChatScrollButton"); UiBars.PageDown(); });
            down.Tip = () => "<size=14><color=#ffffff>Previous action bar page</color></size>\n<color=#ffd100>Shift+1..6 picks a page.</color>";
            _page = UiKit.Text(mainButtons, "MainMenuBarPageNumber", "1", 10f, UiArt.Gold, TextAlignmentOptions.Center, 532f, 25.5f, 20f, 12f);
        }

        public void Refresh()
        {
            int page = UiBars.Page;
            if (page == _shown) return;
            _shown = page;
            _page.SetText("{0}", page);
        }
    }
}
