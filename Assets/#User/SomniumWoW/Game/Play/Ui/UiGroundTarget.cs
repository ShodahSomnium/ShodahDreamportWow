// iteration 11 - owner spellfx: the green circle of ground spells and the item cursor (proposal-spellfx.md s1.1 / s1.4 / s6.5).
// WoW's "spell on the cursor": while Player.Targeting is set (Blizzard, Flamestrike, Rain of Fire, Death and Decay ... - Spell.dbc Targets 0x40;
// enchants, poisons, Disenchant ... - Targets 0x10) the real client draws Interface\SpellShadow\Spell-Shadow-Acceptable.blp on the ground under the
// cursor (Wow.exe names it at file offset 6258751, next to "WorldFrame.cpp"), Spell-Shadow-Unacceptable.blp (the red sign, offset 6258699) when the
// place is out of range; left click = cast there, right click / Esc = cancel (UIParent.lua:2893-2895). VR: the client's laser (right hand) aims,
// the right trigger confirms, a wrist menu released with nothing highlighted cancels (vrcast's hook in WowVrInput), 30 s without a confirm cancels (ours).
// Registers as uicore's IUiCursorMode while aiming (entity clicks stand down, Escape step 6 cancels it, a new cursor mode replaces it).
// Ours (named + logged once): the 17 x 17 ground grid, the 0.03 m lift, the rebuild rule (0.15 m / 1/15 s), the 1.5 m red sign, the 32 px / 4 cm
// item icon, the hint line "Choose an item for <spell>.", the 30 s VR timeout, opening the bags / Character window for an item spell (decision D5).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiGroundTarget : MonoBehaviour, IUiCursorMode
    {
        const string AcceptablePath = @"Interface\SpellShadow\Spell-Shadow-Acceptable.blp";      // 256x256 DXT3 (locale MPQ), Wow.exe 6258751
        const string UnacceptablePath = @"Interface\SpellShadow\Spell-Shadow-Unacceptable.blp";  // 64x64 DXT3, Wow.exe 6258699
        const int Grid = 17;                                  // ours: vertices per side of the ground grid
        const float Lift = 0.03f;                             // ours: metres above the ground
        const float RebuildMove = 0.15f, RebuildEvery = 1f / 15f;   // ours
        const float SignMetres = 1.5f;                        // ours: the red sign's width
        const float RayMax = 200f, RaySkip = 0.3f, DropUp = 3f, DropDown = 12f;
        const float VrTimeout = 30f;                          // ours (= vrcast's SlottingTimeout)
        const float MarkerHold = 0.2f;                        // a Marker not called for this long hides
        const float IconPx = 32f, IconVrMetres = 0.04f, IconVrAlong = 0.6f;   // ours
        const int WorldMask = VrTargetRay.WorldMask;          // layers 0 / 14 (terrain, WMO) - the targeting layers

        static UiGroundTarget _inst;
        static bool _aiming, _latch, _wantsCursor;
        static SpellTargetingView _custom;
        static Action<WowPos> _customConfirm;
        static Action _customCancel;
        static Func<WowPos> _customOrigin;
        static bool _settingsLogged, _rulesLogged, _openLogged, _inputBroken;
        static int _aimLogs, _placeLogs, _cancelLogs;

        /// <summary>The UI context (set next to WowArmUi by uicore's hook): opens the bags / Character window for an item spell (decision D5)
        /// and says "Cancelled." / range texts for custom aims. Null = the player opens them (WoW's own behaviour).</summary>
        public static UiContext Ctx;

        /// <summary>Aiming, or the confirming press not yet released (the latch of s6.5).</summary>
        public static bool Active { get { return _aiming || _latch; } }
        /// <summary>The world click belongs to the aim (fallback when UiCursor does not take world clicks).</summary>
        public static bool ConsumesClicks { get { return _aiming || _latch; } }
        /// <summary>Desktop: the aim wants the cursor free.</summary>
        public static bool WantsCursor { get { return _wantsCursor; } }

        /// <summary>Creates the component on its own object when none exists (idempotent; WowSpellVfx calls it once a session runs).</summary>
        public static UiGroundTarget Ensure()
        {
            if (_inst != null) return _inst;
            var go = new GameObject("WoW Ground Target");
            return go.AddComponent<UiGroundTarget>();
        }

        /// <summary>The same circle and input for a spell not cast through the session (pets H10: a control bar slot with NeedsPlace);
        /// origin = where the range is measured from (a vehicle or pet), null = the player.</summary>
        public static void BeginCustom(SpellTargetingView view, Action<WowPos> onConfirm, Action onCancel, Func<WowPos> origin = null)
        {
            if (view == null) return;
            Ensure();
            var s = WowPlay.Session;
            try { if (s != null && s.Player != null && s.Player.Targeting != null) s.CancelTargeting(); }   // one aim at a time (WoW)
            catch (Exception e) { WowLog.Warn("Ground target: cancelling the session aim failed: " + e.Message); }
            if (_custom != null && !ReferenceEquals(_custom, view)) EndCustomInternal(true, "replaced");
            if (view.Kind == SpellTargetingKind.None) view.Kind = SpellTargetingKind.Ground;
            _custom = view;
            _customConfirm = onConfirm;
            _customCancel = onCancel;
            _customOrigin = origin;
        }

        /// <summary>Ends a custom aim without calling its cancel callback (its owner ended it).</summary>
        public static void EndCustom()
        {
            EndCustomInternal(false, "ended by its owner");
        }

        /// <summary>Draws a ring this frame only (a vehicle's ballistic aim point); hidden when not called for 0.2 s.</summary>
        public static void Marker(object owner, WowPos centre, float radius, bool acceptable)
        {
            var g = Ensure();
            if (g != null) g.SetMarker(owner ?? typeof(UiGroundTarget), centre, radius, acceptable);
        }

        static void EndCustomInternal(bool callCancel, string why)
        {
            if (_custom == null) return;
            var cancel = _customCancel;
            string name = _custom.Name;
            _custom = null;
            _customConfirm = null;
            _customCancel = null;
            _customOrigin = null;
            if (_cancelLogs < 10) { _cancelLogs++; WowLog.Info("Ground target: " + (name ?? "custom aim") + " cancelled (" + why + ")"); }
            if (callCancel && cancel != null)
            {
                try { cancel(); }
                catch (Exception e) { WowLog.Error("Ground target: the cancel of " + name + " failed: " + e); }
            }
        }

        // ------------------------------------------------------------------ per-instance state

        Ring _ring;
        readonly Dictionary<object, MarkerEntry> _markers = new Dictionary<object, MarkerEntry>();
        object _view;                 // the view being aimed (a new one restarts the aim state)
        bool _registered, _openedForView;
        float _aimSince;
        bool _havePlace, _placeOk;
        WowPos _place;
        bool _rightDown, _rightDownOnUi, _vrTrigger;
        float _rightDownAt;
        Vector2 _rightDownPos;
        // item cursor
        GameObject _overlay;
        RawImage _icon;
        TextMeshProUGUI _hint;
        GameObject _vrIcon;
        Mesh _vrIconMesh;
        MeshRenderer _vrIconRenderer;
        Texture2D _vrIconTex;

        sealed class MarkerEntry { public Ring Ring; public float LastCall; }

        void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(this); return; }
            _inst = this;
            if (!_settingsLogged)
            {
                _settingsLogged = true;
                WowLog.Info("Spell effects: weapon enchant glows " + (WowSettings.WeaponEnchantGlows ? "on" : "off") + ", ground spells " +
                            (WowSettings.GroundTargetAtScreenCentre ? "at the screen centre" : "at the mouse"));
            }
        }

        void OnDestroy()
        {
            if (_inst != this) return;
            _inst = null;
            EndMode();
            try { UiCursor.Free(this, false); } catch (Exception) { }
            _aiming = false;
            _latch = false;
            _wantsCursor = false;
            if (_ring != null) _ring.Destroy();
            foreach (var m in _markers.Values) if (m.Ring != null) m.Ring.Destroy();
            _markers.Clear();
            if (_overlay != null) Destroy(_overlay);
            if (_vrIcon != null) Destroy(_vrIcon);
            if (_vrIconMesh != null) Destroy(_vrIconMesh);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            bool vr = WowPlayer.XrPresent;
            if (!_rulesLogged)
            {
                _rulesLogged = true;
                WowLog.Info("Ground target: WoW's Spell-Shadow-Acceptable / -Unacceptable circle; ours: grid " + Grid + "x" + Grid + ", lift " + Lift.ToString("0.00") +
                            " m, rebuilt every " + RebuildMove.ToString("0.00") + " m / " + RebuildEvery.ToString("0.000") + " s, red sign " + SignMetres.ToString("0.0") +
                            " m, item icon " + IconPx.ToString("0") + " px / " + (IconVrMetres * 100f).ToString("0") + " cm, VR timeout " + VrTimeout.ToString("0") + " s");
            }

            SpellTargetingView t = p != null ? p.Targeting : null;
            if (p == null && _custom != null) EndCustomInternal(true, "no session");
            if (t != null && _custom != null) EndCustomInternal(true, "a spell of the session aims");
            SpellTargetingView view = t ?? _custom;
            bool custom = t == null && _custom != null;
            if (_registered && !ReferenceEquals(UiCursor.Mode, this)) _registered = false;   // replaced / cancelled by uicore

            if (view == null || s == null)
            {
                if (_aiming) { _aiming = false; HideAim(); }
                _view = null;
                _latch = _latch && PressHeld(vr);
                if (!_latch) EndMode();
                SetCursor(false);
                TickMarkers(now);
                return;
            }

            if (!_aiming || !ReferenceEquals(view, _view))
            {
                _aiming = true;
                _view = view;
                _aimSince = now;
                _openedForView = false;
                _rightDown = false;
                _vrTrigger = Trigger();                       // a held trigger is not a new press
                if (_aimLogs < 10)
                {
                    _aimLogs++;
                    WowLog.Info("Ground target: " + view.Name + " aimed" + (view.Kind == SpellTargetingKind.Item ? " (an item)" :
                                " (radius " + Radius(view).ToString("0.#") + " yd, " + (vr ? "the VR laser" : WowSettings.GroundTargetAtScreenCentre ? "the screen centre" : "the mouse") + ")") +
                                (custom ? " (custom)" : ""));
                }
            }
            if (!_registered)
            {
                _registered = true;
                UiCursor.Begin(this);
            }

            if (view.Kind == SpellTargetingKind.Item)
            {
                if (_ring != null) _ring.Hide();
                SetCursor(!vr);
                ItemCursor(p, view, vr);
                OpenWindowsOnce(view);
                if (!vr) DesktopInput(s, p, view, custom, false);
                else VrInput(s, p, view, custom, now, false);
            }
            else
            {
                HideItemCursor();
                SetCursor(!vr && !WowSettings.GroundTargetAtScreenCentre);
                Aim(s, p, view, custom, vr, now);
                if (!vr) DesktopInput(s, p, view, custom, true);
                else VrInput(s, p, view, custom, now, true);
            }
            TickMarkers(now);
        }

        static float Radius(SpellTargetingView v)
        {
            return v.Radius > 0f ? v.Radius : SpellTargeting.MarkerRadius;
        }

        void SetCursor(bool want)
        {
            _wantsCursor = want;
            try { UiCursor.Free(this, want); }
            catch (Exception e) { if (!_inputBroken) WowLog.Warn("Ground target: cursor: " + e.Message); }
        }

        void EndMode()
        {
            if (!_registered) return;
            _registered = false;
            UiCursor.End(this);
        }

        void HideAim()
        {
            if (_ring != null) _ring.Hide();
            HideItemCursor();
        }

        // ------------------------------------------------------------------ the aim point and the circle

        void Aim(IWowSession s, PlayerState p, SpellTargetingView view, bool custom, bool vr, float now)
        {
            Vector3 hit;
            _havePlace = AimPoint(vr, out hit);
            if (!_havePlace) { if (_ring != null) _ring.Hide(); return; }
            float x, y, z;
            WowSpace.ToWow(s.MapId, hit, out x, out y, out z);
            _place = new WowPos(x, y, z, 0f);
            WowPos from = p.Pos;
            if (custom && _customOrigin != null)
            {
                try { from = _customOrigin(); } catch (Exception) { from = p.Pos; }
            }
            _placeOk = SpellTargeting.CheckPlace(view, from, _place) == null;
            if (_ring == null) _ring = new Ring("WoW Ground Target circle");
            _ring.Draw(hit, _placeOk ? Radius(view) * WowSpace.MetresPerYard : SignMetres * 0.5f, _placeOk, now);
        }

        /// <summary>Where the aim meets the ground: desktop = the ray through the mouse (cursor visible and free, setting off) or the screen
        /// centre; VR = the client's laser on the right hand (laser off: the hand's forward; only the left tracked: its forward).</summary>
        static bool AimPoint(bool vr, out Vector3 point)
        {
            point = Vector3.zero;
            Ray ray;
            if (vr)
            {
                Transform right = WowPlayer.RightHand, left = WowPlayer.LeftHand;
                if (right != null)
                {
                    if (!VrClientLaser.TryGetLaser(right, out ray)) ray = new Ray(right.position, right.forward);
                }
                else if (left != null) ray = new Ray(left.position, left.forward);
                else return false;
            }
            else
            {
                Camera cam = Camera.main;
                if (cam == null || !cam.isActiveAndEnabled) return false;
                bool mouse = !WowSettings.GroundTargetAtScreenCentre && Cursor.visible && Cursor.lockState != CursorLockMode.Locked;
                Vector3 sp = new Vector3(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f, 0f);
                if (mouse && !_inputBroken)
                {
                    try { sp = Input.mousePosition; }
                    catch (Exception) { _inputBroken = true; }
                }
                ray = cam.ScreenPointToRay(sp);
            }
            RaycastHit h;
            if (!Physics.Raycast(ray.origin + ray.direction * RaySkip, ray.direction, out h, RayMax, WorldMask, QueryTriggerInteraction.Ignore)) return false;
            point = h.point;
            return true;
        }

        // ------------------------------------------------------------------ input

        void DesktopInput(IWowSession s, PlayerState p, SpellTargetingView view, bool custom, bool ground)
        {
            if (_inputBroken) return;
            try
            {
                bool overUi = WowEntityViews.PointerOverUi();
                if (ground && Input.GetMouseButtonDown(0) && !overUi)
                {
                    _latch = true;
                    Confirm(s, p, view, custom);
                }
                // Right button: a plain click cancels (a drag turns the camera, as in WoW).
                if (Input.GetMouseButtonDown(1))
                {
                    _rightDown = true;
                    _rightDownAt = Time.unscaledTime;
                    _rightDownPos = Input.mousePosition;
                    _rightDownOnUi = overUi;
                }
                if (_rightDown && Input.GetMouseButtonUp(1))
                {
                    _rightDown = false;
                    float moved = ((Vector2)Input.mousePosition - _rightDownPos).magnitude;
                    if (!_rightDownOnUi && Pure.UiPickupRules.PlainClick(Time.unscaledTime - _rightDownAt, moved))
                    {
                        _latch = true;
                        CancelAim("right click");
                    }
                }
                if (Input.GetKeyDown(KeyCode.Escape) && !WowArmUi.TypingSomewhere()) CancelAim("Esc");
            }
            catch (Exception e)
            {
                _inputBroken = true;
                WowLog.Warn("Ground target: legacy mouse input unavailable (" + e.Message + ") - the circle is confirmed through uicore's world clicks only.");
            }
        }

        void VrInput(IWowSession s, PlayerState p, SpellTargetingView view, bool custom, float now, bool ground)
        {
            bool trig = Trigger();
            bool pressed = trig && !_vrTrigger;
            _vrTrigger = trig;
            // lead fix 2026-10-12 (the user: "can't use items like dynamite ... the circle appears, but you can't use trigger"): PointerOnUi is
            // true for the whole aim (it includes UiCursor.TakesWorldClicks - this very mode) and for any panel the laser crosses (the open
            // bags) - only a press on one of our buttons is not the place
            if (ground && pressed && !VrClientLaser.BusyHoldOrUi && !VrClientLaser.Inspector && !WowArmUi.PointerOnButton)   // lead fix 2026-10-10: the floor's interactable does not block
            {
                _latch = true;
                GearXr.Pulse(true, 0.5f, 0.04f);              // the radial cast's pulse
                Confirm(s, p, view, custom);
                return;
            }
            if (now - _aimSince > VrTimeout)
            {
                CancelAim(VrTimeout.ToString("0") + " s without a confirm");
                Say("Cancelled.");
            }
        }

        static bool PressHeld(bool vr)
        {
            if (vr) return Trigger();
            if (_inputBroken) return false;
            try { return Input.GetMouseButton(0) || Input.GetMouseButton(1); }
            catch (Exception) { _inputBroken = true; return false; }
        }

        /// <summary>Right trigger as UiPointer / WowVrInput read it (analog > 0.5 OR triggerButton).</summary>
        static bool Trigger()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!device.isValid) return false;
            float v;
            bool down;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out v) && v > 0.5f) return true;
            return device.TryGetFeatureValue(CommonUsages.triggerButton, out down) && down;
        }

        void Confirm(IWowSession s, PlayerState p, SpellTargetingView view, bool custom)
        {
            if (!_havePlace) return;                          // the sky: nothing
            WowPos me = p.Pos;
            if (custom && _customOrigin != null)
            {
                try { me = _customOrigin(); } catch (Exception) { me = p.Pos; }
            }
            float dx = _place.X - me.X, dy = _place.Y - me.Y, dz = _place.Z - me.Z;
            float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            if (custom)
            {
                string why = SpellTargeting.CheckPlace(view, me, _place);
                if (why != null && view.RangeExact) { Say(why); return; }   // "Out of range" / "Target too close"; still aiming
                var cb = _customConfirm;
                string name = view.Name;
                _custom = null;
                _customConfirm = null;
                _customCancel = null;
                _customOrigin = null;
                if (_placeLogs < 20) { _placeLogs++; WowLog.Info("Ground target: " + name + " placed at " + _place + " (" + d.ToString("0.0") + " yd, custom)"); }
                if (cb != null)
                {
                    try { cb(_place); }
                    catch (Exception e) { WowLog.Error("Ground target: the confirm of " + name + " failed: " + e); }
                }
                return;
            }
            if (_placeLogs < 20) { _placeLogs++; WowLog.Info("Ground target: " + view.Name + " placed at " + _place + " (" + d.ToString("0.0") + " yd" + (_placeOk ? "" : ", outside its range") + ")"); }
            try { s.ConfirmTargetingAt(_place); }             // the session refuses out of range itself when the range is exact
            catch (Exception e) { WowLog.Error("Ground target: ConfirmTargetingAt failed: " + e); }
        }

        static void CancelAim(string why)
        {
            if (_custom != null) { EndCustomInternal(true, why); return; }
            var s = WowPlay.Session;
            if (s == null || s.Player == null || s.Player.Targeting == null) return;
            if (_cancelLogs < 10) { _cancelLogs++; WowLog.Info("Ground target: " + s.Player.Targeting.Name + " cancelled (" + why + ")"); }
            try { s.CancelTargeting(); }
            catch (Exception e) { WowLog.Error("Ground target: CancelTargeting failed: " + e); }
        }

        static void Say(string text)
        {
            var c = Ctx;
            if (c != null && c.Message != null)
            {
                try { c.Message(text, UiArt.ErrorRed); return; }
                catch (Exception) { }
            }
            WowLog.Info("Ground target: " + text);
        }

        // ------------------------------------------------------------------ IUiCursorMode (uicore)

        public string Name
        {
            get
            {
                var v = _view as SpellTargetingView;
                return v != null && v.Kind == SpellTargetingKind.Item ? "Item target" : "Ground target";
            }
        }

        /// <summary>The aim polls its own input (left button down confirms, a plain right click cancels, the VR trigger confirms); a world
        /// click reported by uicore is consumed so it never reaches the entities.</summary>
        public bool WorldClick(int button, ulong hovered) { return true; }

        /// <summary>Escape (step 6), a new cursor mode, session end, VR / desktop switch.</summary>
        public void Cancel(string why)
        {
            _registered = false;
            CancelAim(why ?? "cancelled");
        }

        // ------------------------------------------------------------------ item targeting (s1.4)

        void ItemCursor(PlayerState p, SpellTargetingView view, bool vr)
        {
            string iconPath = null;
            if (view.FromBagIndex >= 0 && p.Bags != null && view.FromBagIndex < p.Bags.Count && p.Bags[view.FromBagIndex] != null) iconPath = p.Bags[view.FromBagIndex].IconPath;
            if (iconPath == null)
            {
                var a = UiContext.Find(p, view.SpellId);
                if (a != null) iconPath = a.IconPath;
            }
            Texture2D tex = UiArt.Icon(iconPath);
            if (vr)
            {
                if (_overlay != null && _overlay.activeSelf) _overlay.SetActive(false);
                VrIcon(tex);
                return;
            }
            if (_vrIcon != null && _vrIcon.activeSelf) _vrIcon.SetActive(false);
            BuildOverlay();
            if (_overlay == null) return;
            if (!_overlay.activeSelf) _overlay.SetActive(true);
            if (tex != null && _icon.texture != tex) { _icon.texture = tex; _icon.uvRect = WowTextures.UiUvRect; }
            _icon.enabled = tex != null;
            Vector2 mp = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!_inputBroken)
            {
                try { mp = Input.mousePosition; } catch (Exception) { _inputBroken = true; }
            }
            ((RectTransform)_icon.transform).anchoredPosition = mp + new Vector2(IconPx * 0.5f, -IconPx * 0.5f);
            string line = "Choose an item for " + view.Name + ".";
            if (_hint.text != line) _hint.text = line;
        }

        void BuildOverlay()
        {
            if (_overlay != null) return;
            _overlay = new GameObject("WoW Item Target (desktop)", typeof(RectTransform));
            _overlay.layer = UiKit.Layer;
            _overlay.transform.SetParent(transform, false);
            var canvas = _overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;                         // ours: over the HUD and windows; no raycaster - it never takes a click
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.layer = UiKit.Layer;
            var ir = (RectTransform)iconGo.transform;
            ir.SetParent(_overlay.transform, false);
            ir.anchorMin = ir.anchorMax = Vector2.zero;
            ir.pivot = new Vector2(0.5f, 0.5f);
            ir.sizeDelta = new Vector2(IconPx, IconPx);
            _icon = iconGo.AddComponent<RawImage>();
            _icon.raycastTarget = false;
            bool over = UiKit.Over;
            UiKit.Over = false;
            try
            {
                var holder = new GameObject("Hint", typeof(RectTransform));
                holder.layer = UiKit.Layer;
                var hr = (RectTransform)holder.transform;
                hr.SetParent(_overlay.transform, false);
                hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f);
                hr.pivot = new Vector2(0.5f, 1f);
                hr.anchoredPosition = new Vector2(0f, -60f);  // ours: under the screen centre
                hr.sizeDelta = new Vector2(600f, 24f);
                _hint = UiKit.Text(holder.transform, "Text", "", 16f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 600f, 24f);
                _hint.raycastTarget = false;
            }
            finally { UiKit.Over = over; }
            _overlay.SetActive(false);
        }

        void VrIcon(Texture2D tex)
        {
            Transform right = WowPlayer.RightHand;
            Camera head = WowPlayer.Head();
            if (right == null || head == null || tex == null) { if (_vrIcon != null) _vrIcon.SetActive(false); return; }
            if (_vrIcon == null)
            {
                _vrIcon = new GameObject("WoW Item Target (VR)");
                _vrIconMesh = new Mesh { name = "item target icon" };
                _vrIconMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                _vrIconMesh.uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) };   // BLP rows top-down
                _vrIconMesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                _vrIconMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };   // both sides
                _vrIconMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
                _vrIcon.AddComponent<MeshFilter>().sharedMesh = _vrIconMesh;
                _vrIconRenderer = _vrIcon.AddComponent<MeshRenderer>();
                _vrIconRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _vrIconRenderer.receiveShadows = false;
                _vrIconRenderer.lightProbeUsage = LightProbeUsage.Off;
                _vrIconRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            if (_vrIconTex != tex)
            {
                _vrIconTex = tex;
                _vrIconRenderer.sharedMaterial = M2Effects.MaterialFor(tex, 2, true, 5, false);
            }
            Ray ray;
            if (!VrClientLaser.TryGetLaser(right, out ray)) ray = new Ray(right.position, right.forward);
            Vector3 at = ray.origin + ray.direction * IconVrAlong;
            Vector3 f = at - head.transform.position;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            _vrIcon.transform.SetPositionAndRotation(at, Quaternion.LookRotation(f, Vector3.up));
            _vrIcon.transform.localScale = Vector3.one * IconVrMetres;
            if (!_vrIcon.activeSelf) _vrIcon.SetActive(true);
        }

        void HideItemCursor()
        {
            if (_overlay != null && _overlay.activeSelf) _overlay.SetActive(false);
            if (_vrIcon != null && _vrIcon.activeSelf) _vrIcon.SetActive(false);
        }

        /// <summary>Decision D5 (ours): when neither the bags nor the Character window is open, open the Character window for a weapon /
        /// armour spell (enchants, poisons, stones, oils: EquippedItemClass 2 / 4), else the bags (Disenchant, Prospecting, Milling, Feed Pet,
        /// lockboxes) - once per aim. VR: the hint line is said once.</summary>
        void OpenWindowsOnce(SpellTargetingView view)
        {
            if (_openedForView) return;
            _openedForView = true;
            var c = Ctx;
            if (WowPlayer.XrPresent) Say("Choose an item for " + view.Name + ".");
            if (c == null || c.IsOpen == null || c.Toggle == null) return;
            try
            {
                if (c.IsOpen(UiWindowKind.Bags) || c.IsOpen(UiWindowKind.Character)) return;
                UiWindowKind k = view.ItemClass == 2 || view.ItemClass == 4 ? UiWindowKind.Character : UiWindowKind.Bags;
                c.Toggle(k);
                if (!_openLogged)
                {
                    _openLogged = true;
                    WowLog.Info("Ground target: an item spell opens the " + (k == UiWindowKind.Character ? "Character window" : "bags") + " when neither is open (decision D5, ours; logged once)");
                }
            }
            catch (Exception e) { WowLog.Warn("Ground target: opening a window for " + view.Name + " failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ passive markers (pets / vehicles)

        void SetMarker(object owner, WowPos centre, float radius, bool ok)
        {
            var s = WowPlay.Session;
            if (s == null) return;
            MarkerEntry m;
            if (!_markers.TryGetValue(owner, out m))
            {
                if (_markers.Count >= 16) return;             // ours: a handful of owners at most
                m = new MarkerEntry { Ring = new Ring("WoW Ground Target marker") };
                _markers[owner] = m;
            }
            float now = Time.unscaledTime;
            m.LastCall = now;
            Vector3 p = WowSpace.ToUnity(s.MapId, centre.X, centre.Y, centre.Z);
            float r = radius > 0f ? radius : SpellTargeting.MarkerRadius;
            m.Ring.Draw(p, ok ? r * WowSpace.MetresPerYard : SignMetres * 0.5f, ok, now);
        }

        void TickMarkers(float now)
        {
            if (_markers.Count == 0) return;
            foreach (var m in _markers.Values) if (now - m.LastCall > MarkerHold) m.Ring.Hide();
        }

        // ------------------------------------------------------------------ the ground ring mesh

        /// <summary>WoW's targeting texture on a 17 x 17 grid laid on the ground: each vertex dropped by its own ray (layers 0 / 14), lifted
        /// 0.03 m, drawn with the game's particle material (SomniumWoW/Particle, blend 2 = alpha) as the spell effects around it.</summary>
        sealed class Ring
        {
            static Material _okMat, _badMat;
            static bool _matTried;
            static WowFiles _matFiles;

            GameObject _go;
            Mesh _mesh;
            MeshRenderer _r;
            readonly Vector3[] _v = new Vector3[Grid * Grid];
            readonly Vector2[] _uv = new Vector2[Grid * Grid];
            Vector3 _centre;
            float _radius = -1f, _builtAt = -10f;
            bool _ok;
            readonly string _name;

            public Ring(string name) { _name = name; }

            static Material Mat(bool ok)
            {
                WowFiles f = WowPlay.Files;
                if (f == null) return null;
                if (!_matTried || !ReferenceEquals(f, _matFiles))
                {
                    _matTried = true;
                    _matFiles = f;
                    _okMat = Load(f, AcceptablePath);
                    _badMat = Load(f, UnacceptablePath);
                }
                return ok ? _okMat : (_badMat ?? _okMat);
            }

            static Material Load(WowFiles f, string path)
            {
                try
                {
                    Texture2D t = WowTextures.Get(f, path, WowTextureOptions.Ui);
                    if (t == null || ReferenceEquals(t, WowTextures.Missing)) { WowLog.Warn("Ground target: " + path + " missing - no circle"); return null; }
                    return M2Effects.MaterialFor(t, 2, true, 0, false);
                }
                catch (Exception e) { WowLog.Warn("Ground target: " + path + ": " + e.Message); return null; }
            }

            bool Make()
            {
                if (_go != null) return true;
                _go = new GameObject(_name);
                _mesh = new Mesh { name = _name };
                _mesh.MarkDynamic();
                var cols = new Color32[Grid * Grid];
                for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
                var tri = new int[(Grid - 1) * (Grid - 1) * 6];
                int n = 0;
                for (int j = 0; j < Grid - 1; j++)
                    for (int i = 0; i < Grid - 1; i++)
                    {
                        int k = j * Grid + i;
                        tri[n++] = k; tri[n++] = k + Grid; tri[n++] = k + 1;               // clockwise seen from above (Unity front face)
                        tri[n++] = k + 1; tri[n++] = k + Grid; tri[n++] = k + Grid + 1;
                    }
                for (int j = 0; j < Grid; j++)
                    for (int i = 0; i < Grid; i++) _uv[j * Grid + i] = new Vector2(i / (float)(Grid - 1), j / (float)(Grid - 1));
                _mesh.vertices = _v;
                _mesh.uv = _uv;
                _mesh.colors32 = cols;
                _mesh.triangles = tri;
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _r = _go.AddComponent<MeshRenderer>();
                _r.shadowCastingMode = ShadowCastingMode.Off;
                _r.receiveShadows = false;
                _r.lightProbeUsage = LightProbeUsage.Off;
                _r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _go.SetActive(false);
                return true;
            }

            public void Draw(Vector3 centre, float radiusMetres, bool ok, float now)
            {
                Material m = Mat(ok);
                if (m == null || !Make()) { Hide(); return; }
                if (_r.sharedMaterial != m) _r.sharedMaterial = m;
                bool rebuild = !_go.activeSelf || ok != _ok || Mathf.Abs(radiusMetres - _radius) > 0.01f ||
                               (centre - _centre).sqrMagnitude > RebuildMove * RebuildMove || now - _builtAt > RebuildEvery;
                if (rebuild) Build(centre, radiusMetres, ok, now);
                if (!_go.activeSelf) _go.SetActive(true);
            }

            void Build(Vector3 centre, float r, bool ok, float now)
            {
                _centre = centre;
                _radius = r;
                _ok = ok;
                _builtAt = now;
                RaycastHit h;
                for (int j = 0; j < Grid; j++)
                    for (int i = 0; i < Grid; i++)
                    {
                        float px = centre.x + (i / (float)(Grid - 1) * 2f - 1f) * r;
                        float pz = centre.z + (j / (float)(Grid - 1) * 2f - 1f) * r;
                        float y = centre.y;
                        if (Physics.Raycast(new Vector3(px, centre.y + DropUp, pz), Vector3.down, out h, DropUp + DropDown, WorldMask, QueryTriggerInteraction.Ignore)) y = h.point.y;
                        _v[j * Grid + i] = new Vector3(px - centre.x, y + Lift - centre.y, pz - centre.z);
                    }
                _go.transform.SetPositionAndRotation(centre, Quaternion.identity);
                _mesh.vertices = _v;
                _mesh.RecalculateBounds();
            }

            public void Hide()
            {
                if (_go != null && _go.activeSelf) _go.SetActive(false);
            }

            public void Destroy()
            {
                if (_go != null) UnityEngine.Object.Destroy(_go);
                if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
                _go = null;
                _mesh = null;
            }
        }
    }
}
