// iteration 11 - owner world (proposal-world.md 4.2 / 4.3): WoW's instance popups and the Raid Info window, desktop and VR.
// - INSTANCE_LOCK (StaticPopup.lua:2422-2496): INSTANCE_LOCK_TIMER / _PREVIOUSLY_SAVED (GlobalStrings.lua:4249 / :4250) with
//   INSTANCE_LOCK_SEPARATOR (:4248) and BOSSES_KILLED (:1116), counting down (SecondsToTime(ceil(left), nil, 1)); ACCEPT (:11) ->
//   AnswerInstanceLock(true), INSTANCE_LEAVE (:4247) -> AnswerInstanceLock(false); showAlert, whileDead, no Escape; at 0 it hides
//   without an answer (the server saves you). Live text, so it is our own popup in StaticPopup's look (UiPopup has no live text).
// - INSTANCE_BOOT (StaticPopup.lua:2408-2420): INSTANCE_BOOT_TIMER (:4244) with "%d %s" = seconds left and SECONDS / MINUTES (:6200 /
//   :4952) as StaticPopup_OnUpdate formats it; no buttons; hides at 0.
// - CONFIRM_RESET_INSTANCES (StaticPopup.lua:413-423): CONFIRM_RESET_INSTANCES (:1828), YES / NO (:8982 / :5181), whileDead,
//   hideOnEscape -> ResetInstances(). Static text: uicore's shared UiPopup.
// - Raid Info (RaidFrame.xml:235-470, RaidFrame.lua:98-229): RaidInfoFrame 345 x 250, the saved raid / heroic locks with their reset
//   time, Extend Raid Lock / Remove Raid Lock Extension / Reactivate Raid Lock on the selected row, Close.
// Desktop: our own overlay canvas (order 9, as UiGroup); the popups at StaticPopup1's place (TOP 0,-135), the window where WoW opens it
// beside the Social frame (ours: TOPLEFT 351, -132). VR: world panels in front of the eyes (UiGroup's pattern, 0.0014 m per unit).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    [DefaultExecutionOrder(10011)]
    public sealed class UiInstance : MonoBehaviour
    {
        // StaticPopupTemplate (StaticPopup.xml:36-74; StaticPopup_Resize :2908-2935): 320 wide, 420 with showAlert; text 290 wide at TOP
        // (0,-16); the alert icon 48 x 48 at LEFT (24, 0); height 32 + text + 8 + 21; buttons 128 x 21, button1 BOTTOMRIGHT at BOTTOM
        // (-6, 16), button2 13 to its right.
        private const float PopW = 320f, PopAlertW = 420f, TextW = 290f, ButtonW = 128f, ButtonH = 21f;
        // RaidInfoFrame (RaidFrame.xml): 345 x 250; scroll frame 305 x 157 at (16, -58); rows RaidInfoInstanceTemplate 300 x 30
        private const float WinW = 345f, WinH = 250f, RowW = 300f, RowH = 30f, ListX = 16f, ListY = 58f;
        private const int VisibleRows = 5;   // 157 / 30
        public const float VrPopupDistance = 1.1f, VrPopupDrop = 0.1f, VrWindowDistance = 0.9f, VrWindowDrop = 0.15f;   // ours (as UiGroup)
        public const float VrMetresPerUnit = 0.0014f;   // the death popup's scale (UiLayouts "WoW Popup")
        private const string DarkBackground = @"Interface\DialogFrame\UI-DialogBox-Background-Dark.blp";   // RaidInfoFrame's bgFile
        private const string DialogHeader = @"Interface\DialogFrame\UI-DialogBox-Header.blp";
        private const string ColumnTabs = @"Interface\FriendsFrame\WhoFrame-ColumnTabs.blp";

        private sealed class Pop
        {
            public RectTransform Root;
            public UiBackdrop Back;
            public RawImage Alert;
            public TextMeshProUGUI Text;
            public UiButton B1, B2;
            public float W, H;
            public bool Buttons;
        }

        private sealed class Row
        {
            public UiButton B;
            public RawImage Selected;
            public TextMeshProUGUI Name, Diff, Reset, Ext;
            public SavedInstanceView Data;
        }

        private sealed class Win
        {
            public RectTransform Root;
            public UiButton Extend, Cancel, Up, Down;
            public readonly Row[] Rows = new Row[VisibleRows];
        }

        private static UiInstance _current;
        private bool _built, _vr;
        private UiSurface _desk, _vrLockSurface, _vrBootSurface, _vrWinSurface;
        private Transform _vrRoot;
        private Pop _deskLock, _vrLock, _deskBoot, _vrBoot;
        private Win _deskWin, _vrWin;
        private IWowSession _s;
        private InstanceLockView _lockShown;
        private string _lockText, _bootText;
        private bool _lockVr, _bootShown, _bootVr, _open, _winVr, _openAsked;
        private int _seenVersion = -1, _offset;
        private ulong _selected;
        private float _nextRedraw, _reserved = -1f;
        private string _lastError;

        /// <summary>A popup / the Raid Info window is up (pointer owners).</summary>
        public static bool Shown { get { var c = _current; return c != null && (c._open || c._lockShown != null || c._bootShown); } }

        /// <summary>The SELF menu's "Raid Info" (ours in that menu; WoW reaches it from the Raid tab): opens the window, asks the server.</summary>
        public static void ShowRaidInfo()
        {
            var c = _current;
            if (c == null) { WowLog.Warn("UI: Raid Info asked before the instance UI exists"); return; }
            c._openAsked = true;
        }

        /// <summary>UnitPopup RESET_INSTANCES -> StaticPopup CONFIRM_RESET_INSTANCES (StaticPopup.lua:413-423).</summary>
        public static void ConfirmResetInstances()
        {
            UiPopup.Show(new UiPopupSpec
            {
                Which = "CONFIRM_RESET_INSTANCES",
                Text = WowInstances.G("CONFIRM_RESET_INSTANCES", "Do you really want to reset all of your instances?"),   // :1828
                Button1 = WowInstances.G("YES", "Yes"), Button2 = WowInstances.G("NO", "No"),                            // :8982 / :5181
                OnAccept = () =>
                {
                    var s = WowPlay.Session;
                    if (s != null) { s.ResetInstances(); WowLog.Info("UI: Reset all instances confirmed."); }
                },
                WhileDead = true, HideOnEscape = true
            });
        }

        /// <summary>uicore's Escape chain (WowArmUi.Escape, after UiGroup.CloseMenus): closes the Raid Info window, true if it was open.</summary>
        public static bool CloseWindows()
        {
            var c = _current;
            if (c == null || !c._open) return false;
            c.CloseWindow("Escape");
            return true;
        }

        // ------------------------------------------------------------------ lifetime

        private void Awake()
        {
            // the Play layer's instance texts in the install's own language (Interface\FrameXML\GlobalStrings.lua, read by uicore)
            WowInstances.Strings = (key, enUS) => UiGlobalStrings.G(key, enUS);
        }

        private void OnEnable() { _current = this; }

        private void OnDisable()
        {
            if (_current == this) _current = null;
            HideAll();
            UiCursor.Free(this, false);
            UiPopup.Reserve(this, 0f);
            _reserved = -1f;
        }

        private void OnDestroy()
        {
            if (_desk != null && _desk.Canvas != null) Destroy(_desk.Canvas.gameObject);
            if (_vrRoot != null) Destroy(_vrRoot.gameObject);
        }

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null)
            {
                if (_s != null) { HideAll(); _s = null; }
                _openAsked = false;
                UiCursor.Free(this, false);
                Reserve(0f);
                return;
            }
            if (s != _s) { HideAll(); _s = s; }
            try
            {
                _vr = WowPlayer.XrPresent;
                if (_openAsked) { _openAsked = false; OpenWindow(s); }
                RefreshLock(s);
                RefreshBoot(s);
                if (_open) RefreshWindow(s);
            }
            catch (Exception e) { LogOnce("instance UI", e); }
            UiCursor.Free(this, !_vr && (_open || _lockShown != null));
            float h = 0f;
            if (!_vr)
            {
                if (_lockShown != null && _deskLock != null) h += _deskLock.H + 4f;
                if (_bootShown && _deskBoot != null) h += _deskBoot.H + 4f;
            }
            Reserve(h);
        }

        private void Reserve(float h)
        {
            if (Mathf.Approximately(h, _reserved)) return;
            _reserved = h;
            UiPopup.Reserve(this, h);
        }

        private void HideAll()
        {
            HideLock();
            HideBoot();
            if (_open) CloseWindow("hidden");
        }

        // ------------------------------------------------------------------ INSTANCE_LOCK

        private void RefreshLock(IWowSession s)
        {
            InstanceLockView lk = s.Player.PendingInstanceLock;
            if (lk == null || lk.SecondsLeft <= 0f)
            {
                if (_lockShown != null) { WowLog.Info("UI: instance lock popup closed."); HideLock(); }
                return;
            }
            Build();
            // StaticPopupDialogs["INSTANCE_LOCK"].OnUpdate: the lock line, then BOSSES_KILLED, joined by INSTANCE_LOCK_SEPARATOR
            string time = WowInstances.SecondsToTime(Mathf.Ceil(lk.SecondsLeft), false, true);
            string lockLine = lk.PreviouslySaved
                ? WowInstances.F("INSTANCE_LOCK_TIMER_PREVIOUSLY_SAVED", "You have entered an |cffffd200extended|r instance. You will be saved to %1$s in %2$s!", lk.Name, time)
                : WowInstances.F("INSTANCE_LOCK_TIMER", "You have entered an instance already in progress! You will be saved to %1$s in %2$s!", lk.Name, time);
            string bosses = WowInstances.F("BOSSES_KILLED", "%d/%d Bosses Killed", lk.EncountersDone, lk.EncountersTotal);
            string text = Rich(WowInstances.F("INSTANCE_LOCK_SEPARATOR", "%s|n|n%s", lockLine, bosses));
            bool fresh = !ReferenceEquals(lk, _lockShown);
            if (!fresh && text == _lockText && _vr == _lockVr) return;
            Pop pop = _vr ? _vrLock : _deskLock;
            if (fresh || _vr != _lockVr)
            {
                _lockShown = lk;
                _lockVr = _vr;
                if (_vr)
                {
                    _deskLock.Root.gameObject.SetActive(false);
                    ShowVr(_vrLockSurface, true);
                    PlaceInFront(_vrLockSurface, VrPopupDistance, -VrPopupDrop, 0f);
                }
                else
                {
                    ShowVr(_vrLockSurface, false);
                    PlaceDesk(_deskLock, s.Player, 0f);
                    _deskLock.Root.gameObject.SetActive(true);
                }
                if (fresh)
                {
                    WowLog.Info("UI: instance lock popup (StaticPopup INSTANCE_LOCK) for " + lk.Name + " - Accept / Leave Instance, " + lk.SecondsLeft.ToString("0") + " s" + (_vr ? " (VR panel in front of you)" : "") + ".");
                }
            }
            _lockText = text;
            LayoutPop(pop, text);
        }

        private void AnswerLock(bool accept)
        {
            var s = WowPlay.Session;
            if (s == null || _lockShown == null) return;
            WowLog.Info("UI: instance lock - " + (accept ? "Accept" : "Leave Instance") + " clicked.");
            s.AnswerInstanceLock(accept);
            HideLock();
        }

        private void HideLock()
        {
            _lockShown = null;
            _lockText = null;
            if (_deskLock != null) _deskLock.Root.gameObject.SetActive(false);
            ShowVr(_vrLockSurface, false);
        }

        // ------------------------------------------------------------------ INSTANCE_BOOT

        private void RefreshBoot(IWowSession s)
        {
            float left = s.Player.InstanceBootSeconds;
            if (left <= 0f)
            {
                if (_bootShown) { WowLog.Info("UI: instance boot popup closed."); HideBoot(); }
                return;
            }
            Build();
            // StaticPopup_OnUpdate (StaticPopup.lua:3320-3339): ceil(timeleft); under 60 "n SECONDS", else ceil(minutes) "n MINUTES"
            int t = Mathf.CeilToInt(left);
            int n = t < 60 ? t : Mathf.CeilToInt(t / 60f);
            string unit = t < 60 ? WowInstances.G("SECONDS", "|4Second:Seconds;") : WowInstances.G("MINUTES", "|4Minute:Minutes;");   // :6200 / :4952
            unit = PluralFor(unit, n);
            string text = Rich(WowInstances.F("INSTANCE_BOOT_TIMER", "You are not in this instance's group.  You will be teleported to the nearest graveyard in %d %s.", n, unit));
            bool fresh = !_bootShown;
            if (!fresh && text == _bootText && _vr == _bootVr) return;
            Pop pop = _vr ? _vrBoot : _deskBoot;
            if (fresh || _vr != _bootVr)
            {
                _bootShown = true;
                _bootVr = _vr;
                if (_vr)
                {
                    _deskBoot.Root.gameObject.SetActive(false);
                    ShowVr(_vrBootSurface, true);
                    PlaceInFront(_vrBootSurface, VrPopupDistance, -VrPopupDrop - 0.18f, 0f);
                }
                else
                {
                    ShowVr(_vrBootSurface, false);
                    PlaceDesk(_deskBoot, s.Player, _lockShown != null && _deskLock != null ? _deskLock.H + 4f : 0f);
                    _deskBoot.Root.gameObject.SetActive(true);
                }
                if (fresh) WowLog.Info("UI: instance boot popup (StaticPopup INSTANCE_BOOT) - " + t + " s" + (_vr ? " (VR panel in front of you)" : "") + ".");
            }
            _bootText = text;
            LayoutPop(pop, text);
        }

        private void HideBoot()
        {
            _bootShown = false;
            _bootText = null;
            if (_deskBoot != null) _deskBoot.Root.gameObject.SetActive(false);
            ShowVr(_vrBootSurface, false);
        }

        /// <summary>"|4Second:Seconds;" alone (no number before it in the string itself): the singular for 1 (WoW's |4 grammar).</summary>
        private static string PluralFor(string text, int n)
        {
            if (text == null) return "";
            int a = text.IndexOf("|4", StringComparison.Ordinal);
            if (a < 0) return text;
            int colon = text.IndexOf(':', a + 2), semi = colon >= 0 ? text.IndexOf(';', colon + 1) : -1;
            if (colon < 0 || semi < 0) return text;
            string pick = n == 1 ? text.Substring(a + 2, colon - a - 2) : text.Substring(colon + 1, semi - colon - 1);
            return text.Substring(0, a) + pick + text.Substring(semi + 1);
        }

        /// <summary>WoW's |cAARRGGBB..|r and |n as TextMeshPro rich text.</summary>
        private static string Rich(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '|' && i + 1 < s.Length)
                {
                    char k = s[i + 1];
                    if (k == 'n') { sb.Append('\n'); i++; continue; }
                    if (k == 'r') { sb.Append("</color>"); i++; continue; }
                    if ((k == 'c' || k == 'C') && i + 9 < s.Length) { sb.Append("<color=#").Append(s, i + 4, 6).Append('>'); i += 9; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ Raid Info

        private void OpenWindow(IWowSession s)
        {
            Build();
            s.RequestRaidInfo();   // RaidFrame.lua:29 does it when the Raid tab opens
            _offset = 0;
            _seenVersion = -1;
            _nextRedraw = 0f;
            if (!_open || _vr != _winVr)
            {
                _open = true;
                _winVr = _vr;
                if (_vr)
                {
                    _deskWin.Root.gameObject.SetActive(false);
                    ShowVr(_vrWinSurface, true);
                    PlaceInFront(_vrWinSurface, VrWindowDistance, -VrWindowDrop, 0f);
                }
                else
                {
                    ShowVr(_vrWinSurface, false);
                    _deskWin.Root.gameObject.SetActive(true);
                }
            }
            WowLog.Info("UI: Raid Info opened (" + s.Player.SavedInstances.Count + " saved instance(s) known, asking the server)" + (_vr ? " - VR panel in front of you" : "") + ".");
        }

        private void CloseWindow(string why)
        {
            _open = false;
            if (_deskWin != null) _deskWin.Root.gameObject.SetActive(false);
            ShowVr(_vrWinSurface, false);
            WowLog.Info("UI: Raid Info closed (" + why + ").");
        }

        private void RefreshWindow(IWowSession s)
        {
            if (_vr != _winVr) { _open = false; OpenWindow(s); return; }   // the headset put on / taken off: shown on the right surface
            PlayerState p = s.Player;
            float now = Time.unscaledTime;
            if (p.SavedInstancesVersion == _seenVersion && now < _nextRedraw) return;
            _seenVersion = p.SavedInstancesVersion;
            _nextRedraw = now + 1f;
            Win w = _vr ? _vrWin : _deskWin;
            List<SavedInstanceView> list = p.SavedInstances;
            int max = Mathf.Max(0, list.Count - VisibleRows);
            _offset = Mathf.Clamp(_offset, 0, max);
            SavedInstanceView sel = null;
            for (int i = 0; i < list.Count; i++) if (list[i].InstanceGuid == _selected) sel = list[i];
            if (sel == null) _selected = 0;
            for (int i = 0; i < VisibleRows; i++)
            {
                Row r = w.Rows[i];
                int index = i + _offset;
                if (index >= list.Count) { r.Data = null; r.B.gameObject.SetActive(false); continue; }
                SavedInstanceView v = list[index];
                r.Data = v;
                r.B.gameObject.SetActive(true);
                bool live = v.Extended || v.Locked;
                // RaidInfoFrame_Update: name / reset grey with RAID_INSTANCE_EXPIRES_EXPIRED (:5966) when neither locked nor extended
                r.Name.text = live ? v.Name : "<color=#808080>" + v.Name + "</color>";
                r.Diff.text = v.DifficultyName ?? "";
                r.Reset.text = live ? WowInstances.SecondsToTime(v.SecondsLeft, true, false, 3)
                                    : "<color=#808080>" + WowInstances.G("RAID_INSTANCE_EXPIRES_EXPIRED", "Expired") + "</color>";
                r.Ext.enabled = v.Extended;
                r.Selected.enabled = v.InstanceGuid == _selected;
            }
            w.Up.Interactable = _offset > 0;
            w.Down.Interactable = _offset < max;
            w.Up.gameObject.SetActive(list.Count > VisibleRows);
            w.Down.gameObject.SetActive(list.Count > VisibleRows);
            // RaidInfoFrame_UpdateSelectedIndex (RaidFrame.lua:201-223)
            if (sel == null) { w.Extend.Interactable = false; w.Extend.SetLabel(WowInstances.G("EXTEND_RAID_LOCK", "Extend Raid Lock")); }
            else
            {
                w.Extend.Interactable = true;
                if (sel.Extended) w.Extend.SetLabel(WowInstances.G("UNEXTEND_RAID_LOCK", "Remove Raid Lock Extension"));      // :7830
                else if (sel.Locked) w.Extend.SetLabel(WowInstances.G("EXTEND_RAID_LOCK", "Extend Raid Lock"));             // :3626
                else w.Extend.SetLabel(WowInstances.G("REACTIVATE_RAID_LOCK", "Reactivate Raid Lock"));                     // :6025
            }
        }

        private void Select(int row)
        {
            Win w = _vr ? _vrWin : _deskWin;
            if (w == null || row < 0 || row >= VisibleRows || w.Rows[row].Data == null) return;
            _selected = w.Rows[row].Data.InstanceGuid;   // RaidInfoInstance_OnClick
            _nextRedraw = 0f;
            WowLog.Info("UI: Raid Info - " + w.Rows[row].Data.Name + " selected.");
        }

        private void Scroll(int d)
        {
            _offset += d;
            _nextRedraw = 0f;
        }

        private void ExtendClicked()
        {
            var s = WowPlay.Session;
            if (s == null) return;
            SavedInstanceView sel = null;
            foreach (var v in s.Player.SavedInstances) if (v.InstanceGuid == _selected) sel = v;
            if (sel == null) return;
            bool doExtend = !sel.Extended;   // RaidInfoExtendButton.doExtend
            s.SetSavedInstanceExtend(sel.MapId, sel.Difficulty, doExtend);   // + RequestRaidInfo (RaidFrame.lua:226-229)
            WowLog.Info("UI: Raid Info - " + (doExtend ? "extend" : "remove the extension of") + " the lock of " + sel.Name + ".");
        }

        private string RowTip(int row)
        {
            Win w = _vr ? _vrWin : _deskWin;
            var v = w != null && row >= 0 && row < VisibleRows ? w.Rows[row].Data : null;
            if (v == null) return null;
            // RaidInfoInstance_OnEnter: name, difficulty, INSTANCE_ID "Instance ID: %d" (:4246, the low 32 bits)
            return v.Name + "\n" + (v.DifficultyName ?? "") + "\n" + WowInstances.F("INSTANCE_ID", "Instance ID: %d", (long)(uint)(v.InstanceGuid & 0xFFFFFFFFUL));
        }

        // ------------------------------------------------------------------ building (lazily)

        private void Build()
        {
            if (_built) return;
            _built = true;
            UiSurface oldSurface = UiKit.Surface;
            bool oldOver = UiKit.Over, oldPointable = UiKit.Pointable;
            try
            {
                UiKit.Over = false;
                UiKit.Pointable = true;
                _desk = UiSurface.MakeScreen("WoW Instance UI (desktop)", transform, 9);
                UiKit.Surface = _desk;
                RectTransform lockHost = _desk.Anchor("Instance Lock Popup", new Vector2(0.5f, 1f), PopAlertW, 100f, 0f, -135f);
                _deskLock = MakePop(lockHost, true, true);
                RectTransform bootHost = _desk.Anchor("Instance Boot Popup", new Vector2(0.5f, 1f), PopW, 72f, 0f, -135f);
                _deskBoot = MakePop(bootHost, false, false);
                RectTransform winHost = _desk.Anchor("Raid Info Host", new Vector2(0f, 1f), WinW, WinH, 351f, -132f);
                _deskWin = MakeWin(winHost);
                _desk.Finish();
                _deskLock.Root.gameObject.SetActive(false);
                _deskBoot.Root.gameObject.SetActive(false);
                _deskWin.Root.gameObject.SetActive(false);

                UiKit.Pointable = false;
                var root = new GameObject("WoW Instance UI (VR)");
                root.transform.SetParent(transform, false);
                _vrRoot = root.transform;
                _vrLockSurface = UiSurface.MakeWorld("WoW Instance Lock (VR)", _vrRoot, PopAlertW, 100f, VrMetresPerUnit, false, 104);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrLockSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrLockSurface;
                _vrLock = MakePop(_vrLockSurface.Content, true, true);
                _vrLockSurface.Finish();
                _vrLockSurface.SetShown(false);
                _vrBootSurface = UiSurface.MakeWorld("WoW Instance Boot (VR)", _vrRoot, PopW, 72f, VrMetresPerUnit, false, 105);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrBootSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrBootSurface;
                _vrBoot = MakePop(_vrBootSurface.Content, false, false);
                _vrBootSurface.Finish();
                _vrBootSurface.SetShown(false);
                _vrWinSurface = UiSurface.MakeWorld("WoW Raid Info (VR)", _vrRoot, WinW, WinH, VrMetresPerUnit, false, 106);
                SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiCarry.Add(_vrWinSurface);   // 13.7: moves with the player (UiCarry)
                UiKit.Surface = _vrWinSurface;
                _vrWin = MakeWin(_vrWinSurface.Content);
                _vrWinSurface.Finish();
                _vrWinSurface.SetShown(false);
                WowLog.Info("UI: instance UI built (INSTANCE_LOCK / INSTANCE_BOOT popups, Raid Info " + WinW + " x " + WinH + "; desktop canvas order 9, VR panels at " + VrMetresPerUnit + " m per unit).");
            }
            finally
            {
                UiKit.Surface = oldSurface;
                UiKit.Over = oldOver;
                UiKit.Pointable = oldPointable;
            }
        }

        private Pop MakePop(Transform parent, bool alert, bool buttons)
        {
            var p = new Pop { W = alert ? PopAlertW : PopW, H = 100f, Buttons = buttons };
            p.Root = UiKit.Rect(parent, "Popup", 0f, 0f, p.W, p.H);
            p.Back = UiBackdrop.Make(p.Root, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            p.Back.Place(0f, 0f, p.W, p.H);
            if (alert)
            {
                p.Alert = UiKit.Pic(p.Root, "Alert", UiArt.Tex(UiPopup.Alert), 24f, 26f, 48f, 48f);   // STATICPOPUP_TEXTURE_ALERT at LEFT (24, 0)
                p.Alert.raycastTarget = false;
            }
            p.Text = UiKit.Text(p.Root, "Text", "", 13f, Color.white, TextAlignmentOptions.Center, (p.W - TextW) * 0.5f, 16f, TextW, 30f);   // GameFontHighlight
            if (buttons)
            {
                p.B1 = UiKit.PopupButton(p.Root, WowInstances.G("ACCEPT", "Accept"), p.W * 0.5f - 6f - ButtonW, 56f, () => AnswerLock(true));          // :11
                p.B2 = UiKit.PopupButton(p.Root, WowInstances.G("INSTANCE_LEAVE", "Leave Instance"), p.W * 0.5f + 7f, 56f, () => AnswerLock(false));  // :4247
            }
            return p;
        }

        private void LayoutPop(Pop p, string text)
        {
            if (p == null) return;
            p.Text.text = text;
            float th = Mathf.Ceil(p.Text.GetPreferredValues(text, TextW, 0f).y) + 2f;
            UiKit.Size(p.Text.rectTransform, TextW, th);
            p.H = Mathf.Max(p.Alert != null ? 80f : 72f, 32f + th + 8f + (p.Buttons ? ButtonH : 0f));   // StaticPopup_Resize
            UiKit.Size(p.Root, p.W, p.H);
            p.Back.Place(0f, 0f, p.W, p.H);
            if (p.Alert != null) UiKit.Move(p.Alert.rectTransform, 24f, (p.H - 48f) * 0.5f);
            if (p.Buttons)
            {
                float by = p.H - 16f - ButtonH;
                UiKit.Move(p.B1.Rt, p.W * 0.5f - 6f - ButtonW, by);
                UiKit.Move(p.B2.Rt, p.W * 0.5f + 7f, by);
            }
            UiSurface vs = p == _vrLock ? _vrLockSurface : p == _vrBoot ? _vrBootSurface : null;
            if (vs != null)
            {
                vs.Root.sizeDelta = new Vector2(p.W, p.H);   // VR: the panel is pointed at inside its own rect (as UiGroup)
                vs.Height = p.H;
            }
        }

        private Win MakeWin(Transform parent)
        {
            var w = new Win();
            w.Root = UiKit.Rect(parent, "Raid Info", 0f, 0f, WinW, WinH);
            UiBackdrop back = UiBackdrop.Make(w.Root, "Backdrop", DarkBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            back.Place(0f, 0f, WinW, WinH);
            RawImage hdr = UiKit.Pic(w.Root, "Header", UiArt.Tex(DialogHeader), (WinW - 256f) * 0.5f, -12f, 256f, 64f);   // TOP (0, 12)
            hdr.raycastTarget = false;
            UiKit.Text(w.Root, "Title", WowInstances.G("RAID_INFORMATION", "Raid Information"), 12f, UiArt.Gold, TextAlignmentOptions.Center, (WinW - 185f) * 0.5f, 2f, 185f, 12f);   // :5962
            ColumnLabel(w.Root, WowInstances.G("INSTANCE", "Instance"), 13f, 31f, 173f);   // RaidInfoInstanceLabel (:4243)
            ColumnLabel(w.Root, WowInstances.G("ID", "ID"), 13f + 173f, 31f, 150f);        // RaidInfoIDLabel (:4218)
            UiKit.CloseButton(w.Root, WinW - 2f - 32f, 3f, () => CloseWindow("the close button"));   // UIPanelCloseButton TOPRIGHT (-2, -3)
            Texture2D hl = UiArt.Add(UiArt.QuestTitleHighlight);
            for (int i = 0; i < VisibleRows; i++)
            {
                int k = i;
                var r = new Row();
                r.B = UiKit.Button(w.Root, "Row " + (i + 1), ListX, ListY + RowH * i, RowW, RowH);
                r.Selected = UiKit.Pic(r.B.transform, "Selected", hl, 0f, 0f, RowW, RowH);   // LockHighlight
                r.Selected.raycastTarget = false;
                r.Selected.enabled = false;
                r.B.Hilite = UiKit.Pic(r.B.transform, "Highlight", hl, 0f, 0f, RowW, RowH);
                r.B.Hilite.enabled = false;
                r.Name = UiKit.Text(r.B.transform, "Name", "", 12f, UiArt.Gold, TextAlignmentOptions.Left, 5f, 4f, 150f, 14f);          // GameFontNormal, TOPLEFT (5, -10)
                r.Diff = UiKit.Text(r.B.transform, "Difficulty", "", 10f, Color.white, TextAlignmentOptions.Left, 5f, 17f, 160f, 12f);   // GameFontHighlightSmall
                r.Reset = UiKit.Text(r.B.transform, "Reset", "", 11f, Color.white, TextAlignmentOptions.Right, RowW - 150f, 5f, 150f, 13f);   // TOPRIGHT (0, -11)
                r.Ext = UiKit.Text(r.B.transform, "Extended", Rich(WowInstances.G("EXTENDED", "|cff00ff00Extended|r")), 10f, Color.white, TextAlignmentOptions.Right, RowW - 150f, 17f, 150f, 12f);   // :3625
                r.Ext.enabled = false;
                foreach (var t in new[] { r.Name, r.Diff, r.Reset, r.Ext }) { t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis; t.raycastTarget = false; }
                r.B.Click = () => Select(k);
                r.B.Tip = () => RowTip(k);
                r.B.gameObject.SetActive(false);
                w.Rows[i] = r;
            }
            w.Up = UiKit.ArtButton(w.Root, "Scroll Up", UiArt.ScrollUpUp, UiArt.ScrollUpUp, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, ListX + RowW + 3f, ListY, 16f, () => Scroll(-1));
            w.Down = UiKit.ArtButton(w.Root, "Scroll Down", UiArt.ScrollDownUp, UiArt.ScrollDownUp, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, ListX + RowW + 3f, ListY + 157f - 16f, 16f, () => Scroll(1));
            w.Up.gameObject.SetActive(false);
            w.Down.gameObject.SetActive(false);
            w.Extend = UiKit.PanelButton(w.Root, WowInstances.G("EXTEND_RAID_LOCK", "Extend Raid Lock"), 14f, WinH - 13f - 20f, 200f, 20f, ExtendClicked);   // BOTTOMLEFT (14, 13)
            w.Extend.Interactable = false;
            w.Cancel = UiKit.PanelButton(w.Root, WowInstances.G("CLOSE", "Close"), WinW - 14f - 90f, WinH - 13f - 20f, 90f, 20f, () => CloseWindow("Close"));   // BOTTOMRIGHT (-14, 13)
            return w;
        }

        /// <summary>RaidInfoHeaderTemplate (RaidFrame.xml:5-30): WhoFrame-ColumnTabs left 5 / middle / right 4, 24 tall, GameFontHighlightSmall centred.</summary>
        private static void ColumnLabel(RectTransform parent, string text, float x, float y, float w)
        {
            Texture2D tabs = UiArt.Tex(ColumnTabs);
            UiKit.Pic(parent, "Tab Left", tabs, 0f, 0.078125f, 0f, 0.75f, x, y, 5f, 24f).raycastTarget = false;
            UiKit.Pic(parent, "Tab Middle", tabs, 0.078125f, 0.90625f, 0f, 0.75f, x + 5f, y, w - 9f, 24f).raycastTarget = false;
            UiKit.Pic(parent, "Tab Right", tabs, 0.90625f, 0.96875f, 0f, 0.75f, x + w - 4f, y, 4f, 24f).raycastTarget = false;
            UiKit.Text(parent, "Label " + text, text, 10f, Color.white, TextAlignmentOptions.Center, x, y + 5f, w, 14f).raycastTarget = false;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Desktop: StaticPopup1's place TOP (0, -135); below the death popup (StaticPopup2's place, ours) while that one is up.</summary>
        private static void PlaceDesk(Pop p, PlayerState pl, float below)
        {
            RectTransform r = (RectTransform)p.Root.parent;
            r.anchoredPosition = new Vector2(0f, (pl.Dead || pl.Ghost ? -260f : -135f) - below);
        }

        /// <summary>VR: in front of the eyes (yaw only), once when it opens - it then stands in the world (no head-locked text).</summary>
        private static void PlaceInFront(UiSurface s, float distance, float up, float right)
        {
            if (s == null) return;
            Camera head = WowPlayer.Head();
            if (head == null) return;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            s.Root.SetPositionAndRotation(head.transform.position + fwd * distance + Vector3.up * up + side * right, Quaternion.LookRotation(fwd, Vector3.up));
        }

        /// <summary>A VR panel shown / hidden, and listed for the UI's ray / poke while shown (UiVrLayout.ExtraSurfaces).</summary>
        private static void ShowVr(UiSurface s, bool on)
        {
            if (s == null) return;
            s.SetShown(on);
            List<UiSurface> extra = UiVrLayout.ExtraSurfaces;
            if (on) { if (!extra.Contains(s)) extra.Add(s); }
            else extra.Remove(s);
        }

        private void LogOnce(string where, Exception e)
        {
            string key = where + e.GetType().Name + e.Message;
            if (key == _lastError) return;
            _lastError = key;
            WowLog.Error("UI: " + where + " failed: " + e);
        }
    }
}
