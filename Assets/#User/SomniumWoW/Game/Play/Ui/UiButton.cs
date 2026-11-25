using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// A pressable rect. Desktop: the client's EventSystem sends pointer events (the overlay canvas has a GraphicRaycaster and the
    /// button a transparent raycast target). VR: UiPointer aims the right-hand ray at the surface directly (AoM's arm-bar recipe)
    /// or the right hand pokes it, and calls SetHover / Press. Visual states follow WoW's button textures: Highlight on hover,
    /// Pushed (or the Down slices) while held, greyed when not Interactable.
    /// </summary>
    public sealed class UiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public RectTransform Rt;
        public UiSurface Surface;
        public Action Click;
        public Action RightClick;
        /// <summary>A press that needs WHERE on the button it landed (slider tracks): x and y as 0..1 fractions of the rect (y from the top).</summary>
        public Action<Vector2> ClickAt;

        /// <summary>Tooltip text (TMP rich text), asked when the pointer arrives and twice a second while it stays; null = none.</summary>
        public Func<string> Tip;

        /// <summary>Lead fix (2026-10-03): the WoW cursor name while the desktop mouse is over this button (e.g. a bag item at an open vendor =
        /// "Buy", ContainerFrame.lua:785 ShowContainerSellCursor), null = the normal cursor.</summary>
        public Func<string> HoverCursor;

        // ---- iteration 11 (uicore, proposal-uicore.md 5.2.1 / npc's LongPress request): drag and drop. Set them through UiKit.Draggable /
        // UiKit.DropTarget (they add UiDragHandle on the desktop). Desktop: a LEFT click while something is on the cursor goes to
        // UiPickup.ClickOn (drop / put back) instead of Press; a left click on a ClickPicksUp button picks its payload up (WoW's item slots).
        // VR: UiPointer presses draggable buttons on the trigger RELEASE, drags past 14 units, LongPress after 0.5 s.
        public Func<UiPickupPayload> DragSource;          // what this button gives to the cursor (null = not draggable)
        public bool ClickPicksUp;                         // a plain click picks it up (item slots)
        public Func<UiPickupPayload, bool> DropAccepts;   // may the held payload be placed here
        public Action<UiPickupPayload> DropInto;          // place it here
        public Func<UiPickupPayload, string> DropHint;    // the hint while hovering with a payload
        public Action LongPress;                          // VR: 0.5 s held below the drag threshold (npc request)
        /// <summary>A window's click blocker (UiKit.Blocker): VR drops prefer real slots over it (UiSurface.DropTargetAt).</summary>
        public bool IsBlocker;

        public RawImage Hilite, Pushed, Art, Icon, Frame, Checked, Flash;
        public Texture2D ArtUp, ArtDisabled;
        public RawImage[] Slices;
        public Texture2D SliceUp, SliceDown, SliceDisabled;
        public TextMeshProUGUI Label;
        public Color LabelColor = Color.white;
        public Image Sweep;
        public TextMeshProUGUI HotKey, Count;

        private bool _interactable = true, _down;
        private float _downUntil;
        private float _lastPress;

        public bool Hovered { get; private set; }

        public bool Interactable
        {
            get { return _interactable; }
            set
            {
                if (_interactable == value) return;
                _interactable = value;
                Refresh();
            }
        }

        public void SetLabel(string text)
        {
            if (Label != null && !string.Equals(Label.text, text)) Label.text = text;
        }

        public void SetHover(bool on)
        {
            if (on == Hovered) return;
            Hovered = on;
            if (!on) _down = false;
            Refresh();
            if (Surface != null) Surface.OnHover(this, on);
        }

        public void SetDown(bool on)
        {
            if (on == _down) return;
            _down = on;
            Refresh();
        }

        /// <summary>A press from VR (shows the pushed art for a moment).</summary>
        public void PressFlash()
        {
            _down = true;
            _downUntil = Time.unscaledTime + 0.12f;
            Refresh();
        }

        private void Update()
        {
            if (_downUntil > 0f && Time.unscaledTime > _downUntil)
            {
                _downUntil = 0f;
                _down = false;
                Refresh();
            }
        }

        private Vector2 _pressAt = new Vector2(0.5f, 0.5f);

        /// <summary>Where the next press lands (VR: the ray's hit point or the fingertip; desktop: set from the mouse event).</summary>
        public void SetPressPoint(Vector3 world)
        {
            if (Rt == null) return;
            Vector3 l = Rt.InverseTransformPoint(world);
            _pressAt = Fraction(new Vector2(l.x, l.y));
        }

        /// <summary>A local point (rect space) as 0..1 fractions, y from the top.</summary>
        public Vector2 Fraction(Vector2 local)
        {
            UnityEngine.Rect r = Rt.rect;
            float fx = r.width > 0f ? (local.x - r.xMin) / r.width : 0.5f;
            float fy = r.height > 0f ? (r.yMax - local.y) / r.height : 0.5f;
            return new Vector2(Mathf.Clamp01(fx), Mathf.Clamp01(fy));
        }

        /// <summary>A drag along a ClickAt button (desktop mouse drag, VR trigger held): no press throttle.</summary>
        public void DragTo()
        {
            if (!_interactable || ClickAt == null) return;
            try { ClickAt(_pressAt); }
            catch (Exception e) { WowLog.Error("UI: button '" + name + "' failed: " + e); }
        }

        /// <summary>Desktop drag: the mouse position on this button's rect, then DragTo.</summary>
        public void DragFromScreen(PointerEventData e)
        {
            Vector2 local;
            if (e == null || Rt == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, e.position, e.pressEventCamera, out local)) return;
            _pressAt = Fraction(local);
            DragTo();
        }

        public void Press(bool right)
        {
            if (!_interactable) return;
            // One press per 0.15 s: the client can deliver a click through two raycasters in one frame.
            if (Time.unscaledTime - _lastPress < 0.15f) return;
            _lastPress = Time.unscaledTime;
            if (ClickAt != null && !right)
            {
                try { ClickAt(_pressAt); }
                catch (Exception e) { WowLog.Error("UI: button '" + name + "' failed: " + e); }
                return;
            }
            Action a = right && RightClick != null ? RightClick : Click;
            if (a == null) return;
            try { a(); }
            catch (Exception e) { WowLog.Error("UI: button '" + name + "' failed: " + e); }
        }

        private void Refresh()
        {
            if (Hilite != null) Hilite.enabled = Hovered && _interactable && Hilite.texture != null;
            if (Pushed != null) Pushed.enabled = _down && _interactable && Pushed.texture != null;
            if (Slices != null)
            {
                Texture2D t = !_interactable ? (SliceDisabled ?? SliceUp) : _down ? (SliceDown ?? SliceUp) : SliceUp;
                for (int i = 0; i < Slices.Length; i++) if (Slices[i] != null && Slices[i].texture != t) Slices[i].texture = t;
            }
            if (Art != null && ArtUp != null)
            {
                Texture2D t = !_interactable && ArtDisabled != null ? ArtDisabled : ArtUp;
                if (Art.texture != t) Art.texture = t;
                Art.enabled = !(_down && Pushed != null && Pushed.texture != null && _interactable);
            }
            if (Label != null)
            {
                Color c = !_interactable ? UiArt.Grey : Hovered ? Color.white : LabelColor;
                if (Label.color != c) Label.color = c;
                Label.rectTransform.anchoredPosition = new Vector2(Label.rectTransform.anchoredPosition.x, _down ? -1f : 0f);
            }
        }

        // ---------------------------------------------------------------- EventSystem (desktop mouse)

        public void OnPointerEnter(PointerEventData e) { SetHover(true); UiCursor.NoteUiHover(this, true); }
        public void OnPointerExit(PointerEventData e) { SetHover(false); UiCursor.NoteUiHover(this, false); }
        public void OnPointerDown(PointerEventData e) { SetDown(true); }
        public void OnPointerUp(PointerEventData e) { SetDown(false); }

        public void OnPointerClick(PointerEventData e)
        {
            // Iteration 11 (uicore 5.2.1): WoW's click-carry - left button only, before Press.
            if (e == null || e.button == PointerEventData.InputButton.Left)
            {
                if (UiPickup.Holding && (DropAccepts != null || DragSource != null)) { UiPickup.ClickOn(this); return; }
                if (!UiPickup.Holding && ClickPicksUp && DragSource != null && _interactable) { UiPickup.ClickOn(this); if (UiPickup.Holding) return; }
            }
            Vector2 local;
            if (e != null && ClickAt != null && Rt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, e.position, e.pressEventCamera, out local))
                _pressAt = Fraction(local);
            Press(e != null && e.button == PointerEventData.InputButton.Right);
        }
    }
}
