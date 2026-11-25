using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// VR pointing at the game's world canvases, copied from what works in the Age of Mythology Dreamport:
    /// - the ray is the client's own XRRayInteractor nearest the right hand (AomBridge.Pointer: "demonstrably the line the player is
    ///   aiming"), else the right hand's forward;
    /// - it is met with each canvas rectangle directly (AomUiPanel.PointAt), the nearest hit wins, the button under it lights up and
    ///   a dot marks the spot;
    /// - the trigger presses (AomVr.Trigger: analog trigger > 0.5 OR triggerButton - many runtimes report only the float);
    /// - and, like the iteration-1 wrist panel, the right hand can poke a button on the arm (within 2.5 cm in front of it for
    ///   60 ms; re-armed once the hand has left it).
    /// </summary>
    public sealed class UiPointer
    {
        public bool OnUi { get; private set; }
        public UiButton Hovered { get; private set; }

        private XRRayInteractor _ray;
        private float _nextSearch;
        private bool _held, _loggedRay, _loggedFallback, _describedDevice, _loggedLaser;
        private UiButton _pokeButton;
        private float _pokeFor;
        private bool _pokeArmed = true;
        private UiSurface _dotOn;
        private int _presses;

        public const float PokeDepth = 0.025f;

        public void Update(List<UiSurface> surfaces)
        {
            Transform right = WowPlayer.RightHand;
            Ray ray;
            bool hasRay = GetRay(right, out ray);
            bool trigger = Trigger();

            UiSurface hitSurface = null;
            UiButton hit = null;
            Vector3 hitPoint = Vector3.zero;
            float best = float.MaxValue;
            if (hasRay)
            {
                for (int i = 0; i < surfaces.Count; i++)
                {
                    UiSurface s = surfaces[i];
                    if (s == null || !s.IsWorld || !s.Shown) continue;
                    float d;
                    Vector3 p;
                    UiButton b;
                    if (s.RayHit(ray, out d, out p, out b) && d < best && d < 6f)
                    {
                        best = d;
                        hitSurface = s;
                        hit = b;
                        hitPoint = p;
                    }
                }
            }

            OnUi = hitSurface != null;

            // Poke with the right hand (arm panels are within reach; world windows usually are not).
            UiButton poked = null;
            if (right != null && VrTabletGuard.PokeAllowed(right.position))   // lead: no poke from a grabbing hand / at the Somnium tablet
            {
                for (int i = 0; i < surfaces.Count; i++)
                {
                    UiSurface s = surfaces[i];
                    if (s == null || !s.IsWorld || !s.Shown) continue;
                    UiButton b;
                    float depth;
                    if (s.PokeHit(right.position, PokeDepth, out b, out depth)) { poked = b; break; }
                }
            }
            if (poked != null)
            {
                OnUi = true;
                if (poked != _pokeButton) { _pokeButton = poked; _pokeFor = 0f; }
                _pokeFor += Time.unscaledDeltaTime;
                if (_pokeArmed && _pokeFor > 0.06f)
                {
                    _pokeArmed = false;
                    poked.PressFlash();
                    poked.SetPressPoint(right.position);
                    poked.Press(false);
                    Log("poke", poked);
                }
                hit = poked;
            }
            else
            {
                _pokeButton = null;
                _pokeFor = 0f;
                _pokeArmed = true;
            }

            if (hit != Hovered)
            {
                if (Hovered != null) Hovered.SetHover(false);
                Hovered = hit;
                if (Hovered != null) Hovered.SetHover(true);
            }

            if (_dotOn != null && _dotOn != hitSurface) _dotOn.ShowDot(false, Vector3.zero);
            _dotOn = hitSurface;
            if (hitSurface != null) hitSurface.ShowDot(true, hit != null && poked == null ? hitPoint : hitPoint);

            // Iteration 11 (uicore 5.2.4): what the laser is on, for UiPickup's VR icon.
            HasRay = hasRay;
            LastRay = ray;
            LastSurface = hitSurface;
            LastPoint = hitPoint;

            if (poked == null) TriggerEdges(trigger, hit, hitSurface, hitPoint);
            else { _pressButton = null; _vrDragging = false; }
            _held = trigger;
            if (UiPickup.Holding) OnUi = true;   // while something is on the cursor the trigger never targets a unit (5.2.4)
        }

        // ------------------------------------------------------------------ iteration 11 (uicore 5.2.4): VR drag and drop

        /// <summary>The laser of the last Update (UiPickup draws the held icon on it).</summary>
        public static bool HasRay { get; private set; }
        public static Ray LastRay { get; private set; }
        public static UiSurface LastSurface { get; private set; }
        public static Vector3 LastPoint { get; private set; }

        public const float DragUnits = 14f;        // ours: the hit point moves this far (canvas units) from the press = a drag
        public const float LongPressSeconds = 0.5f; // ours: npc's split hold / LongPress
        private UiButton _pressButton;
        private UiSurface _pressSurface;
        private Vector3 _pressLocal;
        private float _pressAt;
        private bool _vrDragging, _longFired;

        private void TriggerEdges(bool trigger, UiButton hit, UiSurface hitSurface, Vector3 hitPoint)
        {
            bool down = trigger && !_held, up = !trigger && _held;
            if (down)
            {
                _vrDragging = false;
                _longFired = false;
                _pressButton = null;
                if (hit != null && UiPickup.Holding && !UiPickup.Dragging && (hit.DropAccepts != null || hit.DragSource != null))
                {
                    // Something carried (the spellbook's name, a click pickup): the press drops it on / puts it back to this button.
                    hit.PressFlash();
                    UiPickup.ClickOn(hit);
                    Log("trigger (drop)", hit);
                    return;
                }
                if (hit != null && (hit.DragSource != null || hit.LongPress != null) && hitSurface != null)
                {
                    // Press on the RELEASE (holding to drag must never use / cast first).
                    _pressButton = hit;
                    _pressSurface = hitSurface;
                    _pressLocal = hitSurface.Root.InverseTransformPoint(hitPoint);
                    _pressAt = Time.unscaledTime;
                    hit.SetDown(true);
                    return;
                }
                if (hit != null)
                {
                    // The trigger presses on its down edge while the ray is on a button.
                    hit.PressFlash();
                    hit.SetPressPoint(hitPoint);
                    hit.Press(false);
                    Log("trigger", hit);
                }
                return;
            }
            if (trigger && _held)
            {
                if (_pressButton != null && !_vrDragging && !_longFired)
                {
                    bool left = hit != _pressButton;
                    float moved = left || hitSurface != _pressSurface ? float.MaxValue
                        : Vector3.Distance(hitSurface.Root.InverseTransformPoint(hitPoint), _pressLocal);
                    if (_pressButton.DragSource != null && UiPickupRules.VrDragStarts(moved, left, DragUnits))
                    {
                        UiPickupPayload p = null;
                        try { p = _pressButton.DragSource(); }
                        catch (Exception e) { WowLog.Warn("UI pickup: DragSource of '" + _pressButton.name + "' failed: " + e.Message); }
                        _pressButton.SetDown(false);
                        if (p != null)
                        {
                            UiPickup.PickUp(p, true, _pressButton);
                            _vrDragging = true;
                            Haptic(0.25f, 0.03f);   // ours
                        }
                        else _pressButton = null;
                    }
                    else if (_pressButton != null && _pressButton.LongPress != null && UiPickupRules.VrLongPress(Time.unscaledTime - _pressAt, false, LongPressSeconds))
                    {
                        _longFired = true;
                        _pressButton.SetDown(false);
                        try { _pressButton.LongPress(); }
                        catch (Exception e) { WowLog.Error("UI: long press on '" + _pressButton.name + "' failed: " + e); }
                        Haptic(0.25f, 0.03f);
                        Log("long press", _pressButton);
                    }
                }
                else if (_pressButton == null && hit != null && hit.ClickAt != null && !UiPickup.Holding)
                {
                    // Holding the trigger on a slider track drags its thumb along with the ray.
                    hit.SetPressPoint(hitPoint);
                    hit.DragTo();
                }
                return;
            }
            if (up)
            {
                UiButton pressed = _pressButton;
                _pressButton = null;
                if (pressed != null) pressed.SetDown(false);
                if (_vrDragging)
                {
                    _vrDragging = false;
                    if (!UiPickup.Holding) return;
                    UiPickup.StopDragging();
                    if (hitSurface != null)
                    {
                        UiButton target = hitSurface.DropTargetAt(hitPoint);
                        if (target == null || !UiPickup.DropOn(target)) UiPickup.Clear("back");   // VR has no click-carry
                        else Log("drop", target);
                    }
                    else UiCursor.WorldClick(0, WowEntityViews.HoveredGuid);   // released in the air: the world (destroy popup / trade)
                    return;
                }
                if (pressed != null && !_longFired && pressed == hit)
                {
                    pressed.PressFlash();
                    pressed.SetPressPoint(hitPoint);
                    pressed.Press(false);
                    Log("trigger (release)", pressed);
                }
            }
        }

        private static void Haptic(float amplitude, float seconds)
        {
            try { SomniumSpace.Worlds.SomniumWoW.Game.Gear.GearXr.Pulse(true, amplitude, seconds); }
            catch (Exception) { }
        }

        public void Clear()
        {
            if (Hovered != null) Hovered.SetHover(false);
            Hovered = null;
            if (_dotOn != null) _dotOn.ShowDot(false, Vector3.zero);
            _dotOn = null;
            OnUi = false;
            if (_pressButton != null) _pressButton.SetDown(false);
            _pressButton = null;
            _vrDragging = false;
            LastSurface = null;
            HasRay = false;
        }

        private void Log(string how, UiButton b)
        {
            _presses++;
            if (_presses <= 20 || _presses % 50 == 0)
                WowLog.Info("UI: '" + b.name + "' on " + (b.Surface != null ? b.Surface.Name : "?") + " pressed by the " + how + " (VR, press " + _presses + ").");
        }

        private bool GetRay(Transform right, out Ray ray)
        {
            ray = default(Ray);
            if (right == null) return false;
            // Stage S1 (laser, reports10/laser.md request 1; applied by the integrator): the same ray as the client's own laser
            // (VrClientLaser: its Somnium pointer, stabilized origin), so the dot and the button hover sit where the laser points
            // (O2 used the hand's forward - a second direction, the user's "separate yellow dot"). The old search stays the fallback.
            Ray laser;
            if (VrClientLaser.TryGetLaser(right, out laser))
            {
                ray = laser;
                if (!_loggedLaser) { _loggedLaser = true; WowLog.Info("UI: VR pointer = " + VrClientLaser.Describe + "."); }
                return true;
            }
            if (_ray != null && (!_ray.isActiveAndEnabled || (_ray.transform.position - right.position).sqrMagnitude > 0.25f)) _ray = null;
            if (_ray == null && Time.unscaledTime > _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _ray = null;
                float bestD = 0.25f;   // squared metres: a ray origin never sits half a metre from its own hand
                try
                {
                    XRRayInteractor[] all = UnityEngine.Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    for (int i = 0; i < all.Length; i++)
                    {
                        XRRayInteractor c = all[i];
                        if (c == null || !c.isActiveAndEnabled) continue;
                        float d = (c.transform.position - right.position).sqrMagnitude;
                        if (d < bestD) { bestD = d; _ray = c; }
                    }
                }
                catch (Exception e)
                {
                    if (!_loggedFallback) WowLog.Warn("UI: XRRayInteractor search failed: " + e.Message);
                }
            }
            if (_ray != null)
            {
                Transform from = _ray.rayOriginTransform != null ? _ray.rayOriginTransform : _ray.transform;
                ray = new Ray(from.position, from.forward);
                if (!_loggedRay) { _loggedRay = true; WowLog.Info("UI: VR pointer = the client's ray interactor '" + _ray.name + "' (right hand)."); }
                return true;
            }
            ray = new Ray(right.position, right.forward);
            if (!_loggedFallback) { _loggedFallback = true; WowLog.Info("UI: VR pointer = the right hand's forward (no XRRayInteractor near the right hand yet)."); }
            return true;
        }

        private bool Trigger()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!device.isValid) return false;
            if (!_describedDevice)
            {
                _describedDevice = true;
                float f;
                bool b;
                WowLog.Info("UI: right controller '" + device.name + "' reports trigger(float) " + (device.TryGetFeatureValue(CommonUsages.trigger, out f) ? "yes" : "NO")
                    + ", triggerButton(bool) " + (device.TryGetFeatureValue(CommonUsages.triggerButton, out b) ? "yes" : "NO") + ".");
            }
            float v;
            bool down;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out v) && v > 0.5f) return true;
            return device.TryGetFeatureValue(CommonUsages.triggerButton, out down) && down;
        }
    }
}
