using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>
    /// The clickable part of an entity: a solid box on layer 16 (the layer both Somnium pointers hit that is not ground or
    /// UI - desktop PhysicsRaycaster layers 0,1,4,5,14,16; VR far ray 0,5,6,10,14,16,23; scout/bridge_api.md section 4 and
    /// EtsTruckDoor.cs), touching nothing physically (excludeLayers = all). Desktop: the client's EventSystem delivers
    /// IPointerClick/Enter/Exit here. VR: an XRSimpleInteractable (interaction layer 4 'XRInteractable', which the client's
    /// Near-Far interactor carries) - the trigger (activated) clicks, hover highlights.
    /// </summary>
    public sealed class WowEntityClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>The layer both client pointers can hit that is not ground or UI.</summary>
        public const int Layer = 16;

        /// <summary>XRI interaction layers: bit 4 'XRInteractable' (+ bit 3, SDK_Cone's union 24).</summary>
        private const int InteractionLayers = (1 << 3) | (1 << 4);

        internal ulong Guid;
        internal WowEntityViews Owner;
        private BoxCollider _box;
        private XRSimpleInteractable _xr;

        public BoxCollider Box { get { return _box; } }

        /// <summary>Builds the box and the interactable on go (collider first: the interactable collects colliders on Awake).</summary>
        internal static WowEntityClick Create(GameObject go, ulong guid, WowEntityViews owner, Vector3 center, Vector3 size)
        {
            go.layer = Layer;
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            box.isTrigger = false;
            box.excludeLayers = ~0;
            box.providesContacts = false;
            var c = go.AddComponent<WowEntityClick>();
            c.Guid = guid;
            c.Owner = owner;
            c._box = box;
            try
            {
                var xr = go.AddComponent<XRSimpleInteractable>();
                xr.interactionLayers = InteractionLayers;
                xr.activated.AddListener(c.OnActivated);
                xr.hoverEntered.AddListener(c.OnHoverEntered);
                xr.hoverExited.AddListener(c.OnHoverExited);
                c._xr = xr;
            }
            catch (System.Exception e)
            {
                WowLog.Warn("Entities: VR interactable not added (" + e.Message + ") - desktop clicks still work");
            }
            return c;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Middle) return;
            // Lead 2026-09-27: desktop left = select, right = interact (WoW); a pointer click in VR counts as the trigger
            bool right = eventData != null && eventData.button == PointerEventData.InputButton.Right;
            if (Owner != null) Owner.OnEntityClicked(Guid, right ? "right-click" : "click", WowPlayer.XrPresent ? WowEntityViews.ClickKind.Vr : right ? WowEntityViews.ClickKind.Interact : WowEntityViews.ClickKind.Select);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Owner != null) Owner.OnEntityHover(Guid, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Owner != null) Owner.OnEntityHover(Guid, false);
        }

        private void OnActivated(ActivateEventArgs args)
        {
            // Integrator (UI owner's request): a trigger press aimed at the left-arm UI must not also target the creature
            // behind the panel (PointerOnUi = the right-hand ray or poke is on a UI panel this frame).
            if (WowArmUi.PointerOnUi || WowFlight.PointerOnMap) return;   // iteration 4: the flight map counts as our UI
            if (Owner != null) Owner.OnEntityClicked(Guid, "VR trigger", WowEntityViews.ClickKind.Vr);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (Owner != null) Owner.OnEntityHover(Guid, true);
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (Owner != null) Owner.OnEntityHover(Guid, false);
        }

        private void OnDestroy()
        {
            if (_xr != null)
            {
                _xr.activated.RemoveListener(OnActivated);
                _xr.hoverEntered.RemoveListener(OnHoverEntered);
                _xr.hoverExited.RemoveListener(OnHoverExited);
            }
        }
    }
}
