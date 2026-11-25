using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The game UI (owner: UI, iteration 2). The front end adds this component to the WoW Game object; it listens to
    /// WowPlay.Started / Ended / Event and reads WowPlay.Session every frame.
    /// - VR: along the LEFT forearm like the Age of Mythology Dreamport (player / target frames, cast bar, XP bar, 12 action buttons,
    ///   Character / Spellbook / Quest Log / Backpack buttons), a window standing up from the arm, the NPC quest frame and loot in
    ///   the world, the death popup in front of the head; pointed at with the right-hand ray + trigger or poked (Ui/UiPointer).
    /// - Desktop: the same on a screen overlay where WoW puts it, with keys (see Keys below; checked against the Somnium client's own
    ///   bindings, reports2/ui.md). Nothing catches the mouse outside the drawn frames, so Somnium's controls stay free.
    /// All art, icons and fonts come from the player's install at runtime (Ui/UiArt).
    /// Iteration 4: VR floating HUD (player / target / party frames, cast bars) top-left of the view, the palm menu (look at the left
    /// palm), the forearm panel only with WowSettings.VrForearmBar, the low-health vignette (VR + desktop), desktop party frames and
    /// the target's cast bar, settings rows for the new settings; VR world windows shift on WowSpace.Rebased.
    /// Iteration 11 (uicore, proposal-uicore.md 5.1-5.5): every window comes from the registry (UiRegistry: toggled, session and External
    /// windows, OpenRequest); the desktop places them with WoW's own panel manager (Pure/UiPanelRules = UIParent.lua's
    /// FramePositionDelegate); the keys are one dispatcher over the registry's chords + vrcast's bar keys (Pure/UiKeyMap is the table);
    /// Escape runs WoW's ToggleGameMenu chain; UiCursor / UiPickup / UiPopup tick here; the cursor is UiCursor's reference set.
    /// </summary>
    public sealed class WowArmUi : MonoBehaviour
    {
        [Header("VR arm panel (metres)")]
        [SerializeField] private float _armMetresPerUnit = 0.0006f;
        [SerializeField] private float _armLift = 0.03f;
        [SerializeField] private float _armBack = 0.13f;
        [SerializeField] private float _armSide = 0f;
        [SerializeField] private float _lidMetresPerUnit = 0.00055f;
        [SerializeField] private float _lidAngle = 55f;

        [Header("VR floating HUD (iteration 4)")]
        [SerializeField] private float _hudDistance = 1f;
        [SerializeField] private float _hudMetresPerUnit = 0.0007f;
        [SerializeField] private float _hudYaw = -17f;
        [SerializeField] private float _hudPitch = 14f;
        [SerializeField] private float _hudFollow = 5f;

        [Header("VR palm menu (iteration 4)")]
        [SerializeField] private float _palmMetresPerUnit = 0.0009f;
        [SerializeField] private float _palmEnter = 0.55f;
        [SerializeField] private float _palmExit = 0.3f;
        [SerializeField] private float _palmDwell = 0.2f;

        /// <summary>
        /// Desktop keys. Somnium's client binds (2D Input Actions.inputactions) W A S D, arrows, Q/E rotate, TAB = TABLET, M, Space,
        /// F interact, R pop-up menu, I inspector, X, Shift, Escape (no listener - proposal-uicore.md 3.1); its scripts read S, LeftControl,
        /// F, D, Z, G, U, F4-F6, E, keypad digits. So WoW's Tab (target nearest enemy) is T here, and V toggles auto-attack. Iteration 11:
        /// the window keys are the registry's (UiWindowSpec.Key / Key2); the whole table is Pure/UiKeyMap (the Key Bindings window).
        /// </summary>
        public static readonly KeyCode[] ActionKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus, KeyCode.Equals
        };
        public const KeyCode SpellbookKey = KeyCode.P, QuestLogKey = KeyCode.L, CharacterKey = KeyCode.C, BagsKey = KeyCode.B;
        public const KeyCode TargetKey = KeyCode.T, AttackKey = KeyCode.V, SettingsKey = KeyCode.O;
        /// <summary>Iteration 7 (a Dreamport addition, PLAN7 s5.6): J opens the Mounts window - bound nowhere in the Somnium client (scout-offline); WoW has no mounts key.</summary>
        public const KeyCode MountsKey = KeyCode.J;

        /// <summary>True while the VR pointer (ray or poke) is on one of this UI's panels, something is on the cursor or a cursor mode
        /// (ground targeting) takes the trigger: other views should not act on that trigger press.</summary>
        public static bool PointerOnUi { get; private set; }

        /// <summary>Lead fix 2026-10-12: VR - the laser is on one of our buttons (PointerOnUi is also true for a whole panel and while a cursor
        /// mode such as the ground circle runs, so the ground circle reads this one).</summary>
        public static bool PointerOnButton { get; private set; }

        /// <summary>True while a desktop window, popup or held item of this UI is up (the cursor is freed for it).</summary>
        public static bool WindowOpen { get; private set; }

        /// <summary>Iteration 11 (uicore 6.2): a cursor mode (an item / spell on the cursor, ground targeting) owns the world clicks.</summary>
        public static bool CursorBusy { get { return UiCursor.TakesWorldClicks; } }

        private static readonly int KindCount = (int)UiWindowKind.Max + 1;
        private readonly UiContext _ctx = new UiContext();
        private readonly bool[] _open = new bool[KindCount];        // iteration 11: every UiWindowKind (was 1..6)
        private readonly int[] _openStamp = new int[KindCount];     // the order windows opened in (VR keeps the newest; the manager re-shows in order)
        private readonly int[] _lastReq = new int[KindCount];       // OpenRequest values seen (talents U2)
        private readonly bool[] _refused = new bool[KindCount];     // a session / External window the manager refused: not retried until it closes
        private readonly bool[] _failedLog = new bool[KindCount];
        private int _stamp;
        private ulong _lastTarget;
        private bool _hadDialog, _hadLoot;
        private readonly UiPointer _pointer = new UiPointer();
        private UiDesktopLayout _desk;
        private UiVrLayout _vr;
        private UiLayout _active;
        private IWowSession _session;

        // Lead 2026-10-04 (the user: "add Shift+loot to loot all"): 3.3.5a Interface\FrameXML\Bindings.xml:1327
        // <ModifiedClick action="AUTOLOOTTOGGLE" default="SHIFT"/> - Shift held inverts Auto Loot for the loot it opens. The session reads
        // its AutoLoot when the loot answer arrives, so the Shift state is held ShiftLootHold after the key goes up (ours: the click ->
        // answer round trip).
        private const float ShiftLootHold = 0.5f;
        private float _shiftLootUntil = -1f;
        private bool _shiftLootSaid;

        private void ShiftLoot()
        {
            IWowSession s = _session;
            if (s == null) return;
            float now = Time.unscaledTime;
            bool shift;
            try { shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }
            catch (Exception) { return; }
            if (shift) _shiftLootUntil = now + ShiftLootHold;
            bool want = WowSettings.AutoLoot ^ (now < _shiftLootUntil);
            if (s.AutoLoot == want) return;
            s.AutoLoot = want;
            if (!_shiftLootSaid && want != WowSettings.AutoLoot)
            {
                _shiftLootSaid = true;
                WowLog.Info("UI: Shift held - auto loot " + (want ? "on" : "off") + " for the loot it opens (AUTOLOOTTOGGLE, Bindings.xml:1327).");
            }
        }
        private bool _keysBroken, _loggedStart, _wasVr;
        private float _lastPump;
        private readonly UiVignette _vignette = new UiVignette();

        // the desktop panel manager (WoW's UIPanelWindows rules, Pure/UiPanelRules)
        private UiPanelRules _rules = new UiPanelRules();
        private readonly Dictionary<int, UiPanelRules.Panel> _panels = new Dictionary<int, UiPanelRules.Panel>();
        private int _windowLogs, _escapeLogs;
        private static bool _keysLogged;

        private void Awake()
        {
            _ctx.Toggle = Toggle;
            _ctx.IsOpen = k => k != UiWindowKind.None && (int)k < KindCount && _open[(int)k];
            _ctx.Message = (t, c) => { if (_active != null && _active.Messages != null) _active.Messages.Add(t, c, Time.unscaledTime); };
            UiActionCursor.Say = _ctx.Say;   // vrcast hook 7
            // spellfx: the ground-targeting circle (an IUiCursorMode once built).
            try { if (GetComponent<UiGroundTarget>() == null) gameObject.AddComponent<UiGroundTarget>(); }
            catch (Exception e) { WowLog.Warn("UI: ground targeting component failed: " + e.Message); }
            UiGroundTarget.Ctx = _ctx;   // integrator (spellfx request): decision D5 (bags / Character open for item spells) + HUD message texts
            // uicore's two settings rows work (UiCursor art + the first-person catcher).
            WowSettings.SetLive("wow_cursor_art");
            WowSettings.SetLive("wow_cursor_first_person");
            LogKeys();
        }

        /// <summary>Once: the key table's check (proposal-uicore.md 5.5 / 10.1) and K = Skills in the world (PLAN11 s2 D-K2).</summary>
        private static void LogKeys()
        {
            if (_keysLogged) return;
            _keysLogged = true;
            try
            {
                List<string> accepted;
                List<string> problems = UiKeyMap.Check(out accepted);
                var owners = new HashSet<string>();
                for (int i = 0; i < UiKeyMap.Entries.Count; i++) owners.Add(UiKeyMap.Entries[i].Owner ?? "");
                if (problems.Count == 0)
                    WowLog.Info("UI keys: " + UiKeyMap.Entries.Count + " desktop chords from " + owners.Count + " owners; no collision with the client's keys ("
                        + accepted.Count + " accepted overlaps).");
                else
                    for (int i = 0; i < problems.Count; i++) WowLog.Warn("UI keys: " + problems[i]);
            }
            catch (Exception e) { WowLog.Warn("UI keys: the key table check failed: " + e.Message); }
        }

        private void OnEnable()
        {
            WowPlay.Started += OnStarted;
            WowPlay.Ended += OnEnded;
            WowPlay.Event += OnEvent;
            WowSettings.Changed += ApplyGameplaySettings;
            Application.onBeforeRender += OnBeforeRender;
            WowSpace.Rebased += OnRebased;
            if (WowPlay.Session != null) OnStarted(WowPlay.Session);
        }

        private void OnDisable()
        {
            WowPlay.Started -= OnStarted;
            WowPlay.Ended -= OnEnded;
            WowPlay.Event -= OnEvent;
            WowSettings.Changed -= ApplyGameplaySettings;
            Application.onBeforeRender -= OnBeforeRender;
            WowSpace.Rebased -= OnRebased;
            OnEnded();
        }

        private void OnStarted(IWowSession s)
        {
            _session = s;
            _ctx.S = s;
            UiPickup.Reset("a new session");
            UiPopup.Reset("a new session");
            UiArt.Files = WowPlay.Files;
            LoadGlobalStrings();
            ResetWindows();
            _loggedStart = false;
            _lastTarget = 0;
            _hadDialog = _hadLoot = false;
            // K = Skills in the world once the Skills window works (PLAN11 s2 D-K2); until then K stays "back to the room".
            WowGame.BackKeyInWorld = !UiRegistry.Visible(UiWindowKind.Skills);
            ApplyGameplaySettings();
        }

        /// <summary>Once: the install's Interface\FrameXML\GlobalStrings.lua (proposal-uicore.md 5.7) - popups, micro buttons and menus show
        /// the user's own client text (their locale); nothing shipped. Until it is read every caller shows its quoted enUS fallback.</summary>
        private static void LoadGlobalStrings()
        {
            if (UiGlobalStrings.Loaded || WowPlay.Files == null) return;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (UiGlobalStrings.Load(WowPlay.Files.Read(UiGlobalStrings.Path)))
                    WowLog.Info("UI: " + UiGlobalStrings.Count + " strings read from the install's " + UiGlobalStrings.Path + " in " + sw.ElapsedMilliseconds + " ms ("
                        + UiGlobalStrings.OddLines + " other lines).");
                else WowLog.Warn("UI: " + UiGlobalStrings.Path + " not found in the install - the UI shows its English fallbacks.");
            }
            catch (Exception e) { WowLog.Warn("UI: reading " + UiGlobalStrings.Path + " failed (" + e.Message + ") - the UI shows its English fallbacks."); }
        }

        private void ResetWindows()
        {
            for (int i = 0; i < KindCount; i++) { _open[i] = false; _openStamp[i] = 0; _lastReq[i] = 0; _refused[i] = false; }
            _stamp = 0;
            _rules = new UiPanelRules();
            _panels.Clear();
        }

        /// <summary>
        /// WowSettings.Difficulty / AutoLoot -> the session (on start and whenever the settings change). SetDifficulty returns at once
        /// when the value is unchanged (OfflineSession.Effects3.cs:38), so doing it here is harmless if the front end does it as well.
        /// </summary>
        private void ApplyGameplaySettings()
        {
            IWowSession s = _session;
            if (s == null) return;
            try
            {
                int d = Mathf.Clamp(WowSettings.Difficulty, 0, 2);
                bool changed = s.Player.Difficulty != d || s.AutoLoot != WowSettings.AutoLoot;
                s.SetDifficulty(d);
                s.AutoLoot = WowSettings.AutoLoot;
                if (changed) WowLog.Info("UI: gameplay settings applied to the session (difficulty " + d + ", auto loot " + (WowSettings.AutoLoot ? "on" : "off") + ").");
            }
            catch (Exception e) { LogOnce("settings", e); }
        }

        /// <summary>Interface sounds that follow the session's state: target select / lost (TargetFrame.lua), the NPC quest frame (QuestFrame.lua), an empty loot window (LootFrame.lua).</summary>
        private void StateSounds()
        {
            ulong t = _session.TargetGuid;
            if (t != _lastTarget)
            {
                if (t != 0) UiSound.Play(WowUiSound.TargetSelect);
                else UiSound.Play(WowUiSound.TargetDeselect);
                _lastTarget = t;
            }
            bool dialog = _session.CurrentDialog != null;
            if (dialog != _hadDialog)
            {
                _hadDialog = dialog;
                UiSound.Named(dialog ? "igQuestListOpen" : "igQuestListClose");
            }
            LootView loot = _session.CurrentLoot;
            bool hasLoot = loot != null;
            if (hasLoot != _hadLoot)
            {
                _hadLoot = hasLoot;
                if (hasLoot && loot.Money == 0 && loot.Items.Count == 0) UiSound.Named("LOOTWINDOWOPENEMPTY");
            }
        }

        private void OnEnded()
        {
            _session = null;
            _ctx.S = null;
            UiPickup.Reset("the session ended");
            UiPopup.Reset("the session ended");
            UiCursor.Release("the session ended");
            if (_desk != null) _desk.SetShown(false);
            if (_vr != null) _vr.SetShown(false);
            _active = null;
            _pointer.Clear();
            PointerOnUi = false;
            WindowOpen = false;
            ResetWindows();
            _vignette.Reset();
        }

        /// <summary>WowSpace.Rebased (floating origin): the VR windows standing in the world shift with it.</summary>
        private void OnRebased(Vector3 deltaUnity)
        {
            try { if (_vr != null) _vr.Shift(deltaUnity); }
            catch (Exception e) { LogOnce("rebase", e); }
        }

        private void OnEvent(GameEvent e)
        {
            if (_active == null) return;
            float now = Time.unscaledTime;
            _active.OnEvent(e, now);
            UiMessages m = _active.Messages;
            switch (e.Type)
            {
                case GameEventType.Error:
                    m.Add(e.Text, UiArt.ErrorRed, now);
                    break;
                case GameEventType.Info:
                case GameEventType.QuestAccepted:
                case GameEventType.QuestProgress:
                case GameEventType.QuestAbandoned:
                case GameEventType.QuestRewarded:
                case GameEventType.SpellLearned:
                case GameEventType.MoneyReceived:
                    m.Add(e.Text, UiArt.InfoYellow, now);
                    break;
                case GameEventType.XpGain:
                    if (e.Amount > 0) m.Add("+" + e.Amount + " experience", new Color(0.75f, 0.55f, 1f), now);
                    break;
                case GameEventType.LevelUp:
                    m.Add(e.Text, UiArt.InfoYellow, now);
                    _active.Banner.Show("Level " + e.Amount, string.IsNullOrEmpty(e.Text) ? "" : e.Text, now);
                    break;
                case GameEventType.QuestCompleted:
                    m.Add(e.Text, UiArt.InfoYellow, now);
                    _active.Banner.Show("Quest Complete", QuestTitle(e.QuestId, e.Text), now);
                    break;
                case GameEventType.LootReceived:
                    m.Add("You receive loot: " + e.Text + (e.Amount > 1 ? " x" + e.Amount : ""), new Color(0.3f, 1f, 0.3f), now);
                    break;
                case GameEventType.PlayerDied:
                    m.Add("You have died.", UiArt.ErrorRed, now);
                    break;
                case GameEventType.PlayerResurrected:
                    m.Add("You are alive again.", UiArt.InfoYellow, now);
                    break;
                case GameEventType.Dismounted:
                    // Iteration 7: WoW's message when there is one (e.g. "You dismount before continuing."); the same line from an Info
                    // event just refreshes (UiMessages). A chosen dismount says nothing, as in WoW.
                    if (!string.IsNullOrEmpty(e.Text)) m.Add(e.Text, UiArt.InfoYellow, now);
                    break;
            }
            // Iteration 11 (trainer U9): VR-only haptics (ours): a trained service, a fishing bite (both hands), an own recipe cast.
            if (WowPlayer.XrPresent)
            {
                try
                {
                    switch (e.Type)
                    {
                        case GameEventType.TrainerTrained: Gear.GearXr.Pulse(true, 0.3f, 0.1f); break;
                        case GameEventType.FishingBite: Gear.GearXr.Pulse(true, 0.3f, 0.15f); Gear.GearXr.Pulse(false, 0.3f, 0.15f); break;
                        case GameEventType.CastSucceeded:
                            if (_session != null && e.SourceGuid == _session.Player.Guid && IsRecipe(_session.Player, e.SpellId)) Gear.GearXr.Pulse(true, 0.1f, 0.05f);
                            break;
                    }
                }
                catch (Exception ex) { LogOnce("haptics", ex); }
            }
        }

        private static bool IsRecipe(PlayerState p, int spellId)
        {
            List<AbilityView> r = p != null ? p.Recipes : null;
            if (r == null || spellId == 0) return false;
            for (int i = 0; i < r.Count; i++) if (r[i] != null && r[i].SpellId == spellId) return true;
            return false;
        }

        private string QuestTitle(int questId, string fallback)
        {
            if (_session != null)
            {
                var log = _session.Player.QuestLog;
                for (int i = 0; i < log.Count; i++) if (log[i].Id == questId) return log[i].Title;
            }
            return fallback ?? "";
        }

        // ------------------------------------------------------------------ windows (the registry, proposal-uicore.md 5.4)

        private bool IsVr { get { return _active != null && _active == _vr && _vr != null; } }

        private static bool IsSession(UiWindowSpec w) { return w != null && w.SessionWindow; }

        /// <summary>The player's toggle (keys, micro buttons, palm, a window's X): opens a closed window, closes an open one.</summary>
        private void Toggle(UiWindowKind k)
        {
            int i = (int)k;
            if (k == UiWindowKind.None || i <= 0 || i >= KindCount) return;
            if (_open[i]) Close(k, true, "toggle");
            else Open(k, true, "toggle");
        }

        /// <summary>Opens k. byPlayer: the player asked (an External window is told to open). False = refused (dead, the manager).</summary>
        private bool Open(UiWindowKind k, bool byPlayer, string why)
        {
            int i = (int)k;
            if (_open[i]) return true;
            UiWindowSpec w = UiRegistry.Of(k);
            if (w != null && w.Hidden) return false;   // PLAN11 s4: a window whose owner has not built it never opens
            bool vr = IsVr;
            string refusal = null;
            if (vr)
            {
                // VR: one toggled window on the lid at a time (iteration 4 rule); one session window in the world at a time (proposal 5.4.3).
                bool session = IsSession(w);
                for (int j = 1; j < KindCount; j++)
                {
                    if (j == i || !_open[j]) continue;
                    // lead 2026-10-10 (the user: "I cannot open my bags and character screen at the same time"): the bags stay open beside
                    // another lid window (UiVrLayout.ApplyWindows puts them next to the lid), as WoW keeps them open with everything
                    if (!session && (k == UiWindowKind.Bags || (UiWindowKind)j == UiWindowKind.Bags)) continue;
                    if (IsSession(UiRegistry.Of((UiWindowKind)j)) == session) Close((UiWindowKind)j, true, "another window opened");
                }
            }
            else if (!DeskShow(k, out refusal))
            {
                if (refusal == "dead")
                {
                    // ERR_PLAYER_DEAD (GlobalStrings.lua:3387) - WoW's NotWhileDeadError is C code (probe UicGrep).
                    _ctx.Say(UiGlobalStrings.G("ERR_PLAYER_DEAD", "You can't do that when you're dead."), UiArt.ErrorRed);
                }
                LogWindow(k, w, "refused (" + refusal + ")");
                return false;
            }
            _open[i] = true;
            _openStamp[i] = ++_stamp;
            UiSound.Open(k, true);
            if (w != null && w.ExtSetOpen != null && byPlayer)
            {
                try { if (w.ExtIsOpen == null || !w.ExtIsOpen()) w.ExtSetOpen(true); }
                catch (Exception e) { WowLog.Warn("UI windows: " + k + " could not open: " + e.Message); }
            }
            if (_active != null) _active.ApplyWindows(_open);
            LogWindow(k, w, "opened" + (vr ? " (VR)" : " at " + SlotOf(k)) + (why != "toggle" ? " (" + why + ")" : ""));
            return true;
        }

        /// <summary>Closes k. byPlayer: the player (X, Escape, Toggle, the panel manager pushing it out) closed it - the session / the
        /// External owner is told (Closed / ExtSetOpen(false)); false: the game closed it (nothing to tell).</summary>
        private void Close(UiWindowKind k, bool byPlayer, string why, bool hiddenByManager = false)
        {
            int i = (int)k;
            if (!_open[i]) return;
            UiWindowSpec w = UiRegistry.Of(k);
            _open[i] = false;
            UiSound.Open(k, false);
            if (!hiddenByManager && !IsVr) DeskHide(k);
            if (byPlayer && w != null)
            {
                try
                {
                    if (w.ExtSetOpen != null) { if (w.ExtIsOpen == null || w.ExtIsOpen()) w.ExtSetOpen(false); }
                    else if (w.SessionWindow && w.Closed != null && _session != null) w.Closed(_session);
                }
                catch (Exception e) { WowLog.Warn("UI windows: closing " + k + " failed: " + e.Message); }
            }
            // VR: a spell being placed from the spellbook / mounts window goes with it (iteration 10 rule); the desktop keeps it (WoW).
            if (IsVr && (k == UiWindowKind.Spellbook || k == UiWindowKind.Mounts) && _ctx.PickSpell >= 0 && !UiPickup.Dragging) _ctx.CancelPick();
            if (_active != null) _active.ApplyWindows(_open);
            LogWindow(k, w, "closed (" + why + ")");
        }

        private void LogWindow(UiWindowKind k, UiWindowSpec w, string what)
        {
            _windowLogs++;
            if (_windowLogs <= 30 || _windowLogs % 50 == 0)
                WowLog.Info("UI windows: " + k + " (" + (w != null ? w.Frame : "?") + ") " + what + ".");
        }

        // ---- the desktop panel manager

        private UiPanelRules.Panel PanelFor(UiWindowKind k)
        {
            int i = (int)k;
            UiPanelRules.Panel p;
            if (_panels.TryGetValue(i, out p)) return p;
            UiWindowSpec w = UiRegistry.Of(k);
            string area = null;
            if (w != null)
            {
                switch (w.Area)
                {
                    case UiPanelArea.Left: area = UiPanelRules.AreaLeft; break;
                    case UiPanelArea.Center: area = UiPanelRules.AreaCenter; break;
                    case UiPanelArea.Doublewide: area = UiPanelRules.AreaDoublewide; break;
                    case UiPanelArea.Full: area = UiPanelRules.AreaFull; break;
                }
            }
            p = area == null ? null : new UiPanelRules.Panel
            {
                Kind = i, Name = w.Frame ?? k.ToString(), Area = area, Pushable = w.Pushable, FrameWidth = w.Width,
                XOffset = w.XOffset, YOffset = w.YOffset, WhileDead = w.WhileDead
            };
            _panels[i] = p;   // null = Apart (not a UIPanel): shown where it is
            return p;
        }

        private void RulesInputs()
        {
            if (_desk != null) { float sw = _desk.ScreenWidth; if (sw > 100f) _rules.ScreenWidth = sw; }
            _rules.MinimapShown = _desk != null && _desk.MinimapShown; if (_rules.MinimapShown) _rules.MinimapLeft = _desk.MinimapLeft;   // lead 2026-10-03: the minimap
            PlayerState p = _session != null ? _session.Player : null;
            _rules.Dead = p != null && p.Dead && !p.Ghost;   // UnitIsDead("player"): a ghost is not dead
        }

        /// <summary>ShowUIPanel; the panels it pushed out close (their Closed runs), the positions follow. False = refused ("failed" / "dead").</summary>
        private bool DeskShow(UiWindowKind k, out string refusal)
        {
            refusal = null;
            UiPanelRules.Panel p = PanelFor(k);
            if (p == null) return true;
            RulesInputs();
            _rules.ClearEvents();
            _rules.ShowUIPanel(p);
            if (!p.Shown)
            {
                refusal = _rules.Events.Contains("dead " + p.Name) ? "dead" : "failed";
                AfterManager(k);
                return false;
            }
            AfterManager(k);
            return true;
        }

        private void DeskHide(UiWindowKind k)
        {
            UiPanelRules.Panel p;
            if (!_panels.TryGetValue((int)k, out p) || p == null || !p.Shown) return;
            RulesInputs();
            _rules.ClearEvents();
            _rules.HideUIPanel(p);
            AfterManager(k);
        }

        private void AfterManager(UiWindowKind self)
        {
            var hidden = new List<UiPanelRules.Panel>(_rules.HiddenByManager);
            bool bags = _rules.Events.Contains("closeallbags");
            _rules.ClearEvents();
            for (int n = 0; n < hidden.Count; n++)
            {
                int j = hidden[n].Kind;
                if (j == (int)self || j <= 0 || j >= KindCount || !_open[j]) continue;
                Close((UiWindowKind)j, true, "pushed out by " + self, true);
            }
            if (bags && _open[(int)UiWindowKind.Bags]) Close(UiWindowKind.Bags, true, "a centre window (closeallbags)");
            ApplyPositions();
        }

        private void ApplyPositions()
        {
            foreach (KeyValuePair<int, UiPanelRules.Panel> kv in _panels)
            {
                UiPanelRules.Panel p = kv.Value;
                if (p == null || !p.Shown || !p.Placed) continue;
                UiWindowKind k = (UiWindowKind)p.Kind;
                UiWindowSpec w = UiRegistry.Of(k);
                try
                {
                    if (w != null && w.ExtPlace != null) w.ExtPlace(p.X, p.Y);
                    else if (_desk != null) _desk.PlaceWindow(k, p.X, p.Y);
                }
                catch (Exception e) { LogOnce("place " + k, e); }
            }
        }

        private string SlotOf(UiWindowKind k)
        {
            UiPanelRules.Panel p;
            if (!_panels.TryGetValue((int)k, out p) || p == null) return "its own place";
            string[] slots = { UiPanelRules.Left, UiPanelRules.Center, UiPanelRules.Right, UiPanelRules.Doublewide, UiPanelRules.Fullscreen };
            for (int i = 0; i < slots.Length; i++) if (_rules.GetUIPanel(slots[i]) == p) return slots[i] + (p.Placed ? " x " + p.X.ToString("0") : "");
            return "?";
        }

        /// <summary>Desktop <-> VR: the manager starts over from the open windows (oldest first); VR keeps the newest toggled and the newest
        /// session window.</summary>
        private void ModeSwitched(bool vr)
        {
            _rules = new UiPanelRules();
            _panels.Clear();
            var order = new List<int>();
            for (int i = 1; i < KindCount; i++) if (_open[i]) order.Add(i);
            order.Sort((a, b) => _openStamp[a].CompareTo(_openStamp[b]));
            int newestToggled = -1, newestSession = -1;
            for (int n = 0; n < order.Count; n++)
            {
                if (IsSession(UiRegistry.Of((UiWindowKind)order[n]))) newestSession = order[n];
                else newestToggled = order[n];
            }
            for (int n = 0; n < order.Count; n++)
            {
                int i = order[n];
                UiWindowKind k = (UiWindowKind)i;
                if (vr)
                {
                    if (i != newestToggled && i != newestSession) Close(k, true, "VR shows one window at a time", true);
                }
                else
                {
                    string refusal;
                    if (!DeskShow(k, out refusal)) Close(k, true, "the desktop panels (" + refusal + ")", true);
                }
            }
        }

        /// <summary>Per frame: session windows follow Wanted, External windows follow ExtIsOpen, OpenRequest opens (proposal 5.4.8).</summary>
        private void SyncRegistry()
        {
            IWowSession s = _session;
            List<UiWindowSpec> all = UiRegistry.All;
            for (int n = 0; n < all.Count; n++)
            {
                UiWindowSpec w = all[n];
                int k = (int)w.Kind;
                if (k <= 0 || k >= KindCount) continue;
                try
                {
                    if (w.Hidden)
                    {
                        if (_open[k]) Close(w.Kind, true, "hidden");
                        continue;
                    }
                    bool wanted;
                    bool has = true;
                    if (w.ExtIsOpen != null) wanted = w.ExtIsOpen();
                    else if (w.SessionWindow && w.Wanted != null) wanted = w.Wanted(s);
                    else { wanted = _open[k]; has = false; }
                    if (has)
                    {
                        if (!wanted) _refused[k] = false;
                        if (wanted != _open[k])
                        {
                            if (wanted)
                            {
                                if (!_refused[k] && !Open(w.Kind, false, w.SessionWindow ? "the game opened it" : "it opened itself"))
                                {
                                    // Refused by the manager: the session / the owner hears it as the player's close (WoW hides the frame).
                                    _refused[k] = true;
                                    if (w.ExtSetOpen != null) w.ExtSetOpen(false);
                                    else if (w.Closed != null && s != null) w.Closed(s);
                                }
                            }
                            else Close(w.Kind, false, w.SessionWindow ? "the game closed it" : "it closed itself");
                        }
                    }
                    if (w.OpenRequest != null)
                    {
                        int v = w.OpenRequest(s);
                        if (v != _lastReq[k])
                        {
                            _lastReq[k] = v;
                            if (v > 0 && !_open[k]) Open(w.Kind, true, "requested by the game");
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!_failedLog[k])
                    {
                        _failedLog[k] = true;
                        WowLog.Error("UI windows: " + w.Kind + " (" + w.Frame + ", owner " + w.Owner + ") failed in the registry loop (logged once): " + e);
                    }
                }
            }
        }

        // 13.7 (the user: "have all the menus be usable when moving" / "I opened my bags, I walked a bit and then it closed"): the
        // 2026-09-27 rule that closed VR palm / wrist windows once the root moved 0.6 m (asked for when windows followed the player badly)
        // is gone - the world windows now move with the player (UiCarry in UiSurface.cs), so they stay open and usable.

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (_session == null || WowPlay.Session == null)
            {
                if (_active != null) OnEnded();
                return;
            }
            float now = Time.unscaledTime;
            bool vr = WowPlayer.XrPresent;
            UiLayout want;
            try
            {
                want = vr ? (UiLayout)Vr() : Desk();
            }
            catch (Exception e)
            {
                WowLog.Error("UI: building the " + (vr ? "VR" : "desktop") + " UI failed: " + e);
                enabled = false;
                return;
            }
            if (want != _active)
            {
                bool switched = _active != null;
                if (_active != null) _active.SetShown(false);
                _active = want;
                _active.SetShown(true);
                if (switched || vr != _wasVr)
                {
                    UiPickup.Reset(vr ? "switched to VR" : "switched to the desktop");
                    if (vr) UiCursor.Release("switched to VR");
                    ModeSwitched(vr);
                }
                _wasVr = vr;
                _active.ApplyWindows(_open);
                _pointer.Clear();
                WowLog.Info("UI: " + (vr ? "VR arm UI" : "desktop HUD") + " active.");
            }
            _ctx.Vr = vr;
            UiArt.PumpIcons(4);

            try { SyncRegistry(); }
            catch (Exception e) { LogOnce("registry", e); }

            if (!vr) Keys();
            if (!vr) ShiftLoot();   // lead 2026-10-04

            try { StateSounds(); }
            catch (Exception e) { LogOnce("sounds", e); }

            try { UiBars.Tick(_session.Player); }   // vrcast hook 7: pages, bonus bar, the bars' visibility
            catch (Exception e) { LogOnce("bars", e); }

            try { _active.Refresh(now); }
            catch (Exception e) { LogOnce("refresh", e); }

            try { _vignette.Refresh(transform, _session.Player, vr, Time.unscaledDeltaTime); }
            catch (Exception e) { LogOnce("vignette", e); }

            try { UiPickup.Tick(_ctx, vr); }
            catch (Exception e) { LogOnce("pickup", e); }
            try { UiPopup.Tick(transform, vr, _session); }
            catch (Exception e) { LogOnce("popups", e); }


            bool windows = _active.AnyWindowOpen;
            WindowOpen = windows && !vr;
            UiCursor.Free(this, !vr && windows);
            try { UiCursor.Tick(transform, vr, windows, _session); }
            catch (Exception e) { LogOnce("cursor", e); }

            if (!_loggedStart)
            {
                _loggedStart = true;
                PlayerState p = _session.Player;
                WowLog.Info("UI: game UI for " + p.Name + " (level " + p.Level + " " + UiArt.ClassName(p.Class) + ", " + p.Spellbook.Count + " spells, " + (p.Mounts != null ? p.Mounts.Count : 0) + " mounts); "
                    + UiRegistry.All.Count + " windows registered (" + VisibleKinds() + "); keys 1-0 - = action bar, Shift+1..6 page, Alt+1..= Bottom Left Bar, "
                    + "Escape = the Game Menu chain, T target nearest enemy (Tab is Somnium's tablet), V auto-attack; the Key Bindings window lists the rest.");
            }
            if (now - _lastPump > 30f)
            {
                _lastPump = now;
                WowLog.Info("UI: " + UiArt.IconStats + ".");
            }
        }

        private static string VisibleKinds()
        {
            var sb = new System.Text.StringBuilder();
            int hidden = 0;
            for (int i = 0; i < UiRegistry.All.Count; i++)
            {
                UiWindowSpec w = UiRegistry.All[i];
                if (w.Hidden) { hidden++; continue; }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(w.Kind);
            }
            return sb + "; " + hidden + " not built yet";
        }

        private void LateUpdate()
        {
            if (_active == null || _active != _vr || _vr == null) { PointerOnUi = false; PointerOnButton = false; return; }
            try
            {
                _vr.Place(false);
                _pointer.Update(_vr.Surfaces);
                _vignette.Place(WowPlayer.Head());
                PointerOnUi = _pointer.OnUi || UiCursor.TakesWorldClicks;
                PointerOnButton = _pointer.Hovered != null;
            }
            catch (Exception e) { LogOnce("vr", e); }
        }

        private void OnBeforeRender()
        {
            // Follow the arm with the pose the frame is rendered with (tracking updates just before rendering).
            if (_active != null && _active == _vr && _vr != null)
            {
                try { _vr.Place(true); _vignette.Place(WowPlayer.Head()); }
                catch (Exception) { }
            }
        }

        private UiVrLayout Vr()
        {
            if (_vr != null) return _vr;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _vr = new UiVrLayout
            {
                Ctx = _ctx, ArmMetresPerUnit = _armMetresPerUnit, ArmLift = _armLift, ArmBack = _armBack, ArmSide = _armSide,
                LidMetresPerUnit = _lidMetresPerUnit, LidAngle = _lidAngle, Pointer = _pointer
            };
            _vr.Hud.Distance = _hudDistance;
            _vr.Hud.MetresPerUnit = _hudMetresPerUnit;
            _vr.Hud.Yaw = _hudYaw;
            _vr.Hud.Pitch = _hudPitch;
            _vr.Hud.Follow = _hudFollow;
            _vr.Palm.MetresPerUnit = _palmMetresPerUnit;
            _vr.Palm.Enter = _palmEnter;
            _vr.Palm.Exit = _palmExit;
            _vr.Palm.Dwell = _palmDwell;
            _vr.Build(transform);
            _vr.SetShown(false);
            WowLog.Info("UI: VR arm UI built in " + sw.ElapsedMilliseconds + " ms (" + Buttons(_vr) + " buttons, font " + (UiArt.Friz != null ? UiArt.Friz.name : "none") + "); floating HUD "
                + (WowSettings.VrHud ? "on" : "off") + ", forearm action bar " + (WowSettings.VrForearmBar ? "on" : "off") + ", palm menu: look at your left palm.");
            return _vr;
        }

        private UiDesktopLayout Desk()
        {
            if (_desk != null) return _desk;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _desk = new UiDesktopLayout { Ctx = _ctx };
            _desk.Build(transform);
            _desk.SetShown(false);
            WowLog.Info("UI: desktop HUD built in " + sw.ElapsedMilliseconds + " ms (" + Buttons(_desk) + " buttons, font " + (UiArt.Friz != null ? UiArt.Friz.name : "none") + ").");
            return _desk;
        }

        private static int Buttons(UiLayout l)
        {
            int n = 0;
            for (int i = 0; i < l.Surfaces.Count; i++) if (l.Surfaces[i] != null) n += l.Surfaces[i].Buttons.Count;
            return n;
        }

        private string _lastError;

        private void LogOnce(string where, Exception e)
        {
            string key = where + e.GetType().Name + e.Message;
            if (key == _lastError) return;
            _lastError = key;
            WowLog.Error("UI: " + where + " failed: " + e);
        }

        // ------------------------------------------------------------------ desktop keys (proposal-uicore.md 5.5) and Escape (5.1.7)

        private void Keys()
        {
            if (_keysBroken) return;
            try
            {
                if (TypingSomewhere()) return;
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (Input.GetKeyDown(KeyCode.Escape)) { Escape(); return; }

                // The action bars (vrcast hook 7 + pets H9a, one loop in UiBars.Keys); the stack split frame takes the digits while open.
                if (_desk != null && !UiStackSplit.TakesKeys) UiBars.Keys(_session, ActionKeys, _desk.Bar, _desk.MultiBars, _desk.Stance);

                // Bags (npc H10): Shift+B all bags (OPENALLBAGS, DefaultBindings.wtf:101); F8..F11 = ToggleBag(4..1) (Bindings.xml:617-628).
                if (!ctrl && !alt)
                {
                    UiBags bags = UiBags.Current;
                    if (bags != null)
                    {
                        if (shift && Input.GetKeyDown(KeyCode.B)) bags.OpenAllBags();
                        if (!shift)
                            for (int f = 0; f < 4; f++)
                                if (Input.GetKeyDown(KeyCode.F8 + f)) bags.ToggleBag(4 - f);
                    }
                }

                // Window keys from the registry (Key / Key2 with their Shift flags; never with Ctrl / Alt).
                if (!ctrl && !alt)
                {
                    List<UiWindowSpec> all = UiRegistry.All;
                    for (int n = 0; n < all.Count; n++)
                    {
                        UiWindowSpec w = all[n];
                        if (w.Hidden || w.SessionWindow) continue;
                        bool k1 = w.Key != KeyCode.None && shift == w.KeyShift && Input.GetKeyDown(w.Key);
                        bool k2 = w.Key2 != KeyCode.None && shift == w.Key2Shift && Input.GetKeyDown(w.Key2);
                        if (k1 || k2) Toggle(w.Kind);
                    }
                }

                if (!shift && !ctrl && !alt && Input.GetKeyDown(TargetKey)) _session.TargetNearestEnemy();
                if (!shift && !ctrl && !alt && Input.GetKeyDown(AttackKey))
                {
                    if (_session.Player.AutoAttacking) _session.StopAttack();
                    else if (_session.TargetGuid != 0) _session.StartAttack(_session.TargetGuid);
                    else { _session.TargetNearestEnemy(); if (_session.TargetGuid != 0) _session.StartAttack(_session.TargetGuid); }
                }
            }
            catch (Exception e)
            {
                _keysBroken = true;
                WowLog.Warn("UI: desktop keys unavailable (legacy Input): " + e.Message);
            }
        }

        /// <summary>
        /// WoW's ToggleGameMenu (UIParent.lua:2868-2903) for the Dreamport: the stack split, popups (StaticPopup_EscapePressed), the Game
        /// Menu, a centre window, UiGroup's unit menu (CloseMenus), a cast (SpellStopCasting), the cursor mode (SpellStopTargeting / the
        /// held item, ours), every window (CloseAllWindows), the target (ClearTarget), else the Game Menu opens.
        /// </summary>
        private void Escape()
        {
            string what;
            if (UiStackSplit.IsOpen) { UiStackSplit.Cancel("escape"); what = "the stack split"; }
            else if (UiPopup.EscapePressed()) what = "a popup";
            else if (_open[(int)UiWindowKind.GameMenu]) { Close(UiWindowKind.GameMenu, true, "escape"); what = "the Game Menu"; }
            else if (CloseCentre()) what = "a centre window";
            else if (UiGroup.CloseMenus()) what = "the unit menu";
            else if (SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiInstance.CloseWindows()) what = "Raid Info";   // integrator (world request)
            else if (_session.Player.CastingSpellId != 0) { _session.CancelCast(); what = "the cast"; }
            else if (UiCursor.CancelMode("escape")) what = "the cursor";
            else if (CloseAll()) what = "every window";
            else if (_session.TargetGuid != 0) { _session.SetTarget(0); what = "the target"; }
            else { Open(UiWindowKind.GameMenu, true, "escape"); what = "nothing - the Game Menu opened"; }
            _escapeLogs++;
            if (_escapeLogs <= 20 || _escapeLogs % 50 == 0) WowLog.Info("UI: Escape -> closed " + what + ".");
        }

        private bool CloseCentre()
        {
            bool any = false;
            for (int i = 1; i < KindCount; i++)
            {
                if (!_open[i]) continue;
                UiWindowSpec w = UiRegistry.Of((UiWindowKind)i);
                if (w != null && w.Area == UiPanelArea.Center) { Close((UiWindowKind)i, true, "escape"); any = true; }
            }
            return any;
        }

        /// <summary>CloseAllWindows (UIParent.lua:2170): the bags first, then every panel (session windows hear Closed).</summary>
        private bool CloseAll()
        {
            bool any = false;
            if (_open[(int)UiWindowKind.Bags]) { Close(UiWindowKind.Bags, true, "escape"); any = true; }
            for (int i = 1; i < KindCount; i++)
                if (_open[i]) { Close((UiWindowKind)i, true, "escape"); any = true; }
            return any;
        }

        /// <summary>A text field (the client's chat, any input field) has the keyboard: no game keys. Public since iteration 7 (the ride's keys, PLAN7 s4.9).</summary>
        public static bool TypingSomewhere()
        {
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
        }
    }
}
