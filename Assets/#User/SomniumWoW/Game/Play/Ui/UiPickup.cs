// iteration 11 - owner uicore (proposal-uicore.md 5.2): something on the cursor - an item, a spell, an action, a mount, money - like
// WoW's Pickup* API (PickupContainerItem / PickupSpell / PickupAction). ONE cursor for the whole UI: vrcast's UiActionCursor and npc's
// item hooks are facades over it. The TARGET decides what a drop means (UiButton.DropAccepts / DropInto: move, equip, sell, place...);
// a drop on the world asks WoW's DELETE_ITEM / DELETE_GOOD_ITEM popup for an item (UIParent.lua:609-616) and calls npc's
// IWowSession.DestroyItem on Yes. Desktop: UiDragHandle (EventSystem drags, left button only) + WoW's click-carry (UiButton.OnPointerClick);
// VR: UiPointer's trigger hold / release. The payload is drawn under the mouse (desktop) or on the laser (VR).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public enum UiPickupKind : byte { None = 0, Spell = 1, Item = 2, Action = 3, Mount = 4, Money = 5, Macro = 6, MerchantItem = 7, BuybackItem = 8 }

    /// <summary>What is on the cursor (proposal-uicore.md 5.2.1).</summary>
    public sealed class UiPickupPayload
    {
        public UiPickupKind Kind;
        public int Packed, SpellId, ItemEntry;          // Packed = WowActionSlots packed action (spells / items / macros), 0 = none
        public int Bag = -1, Slot = -1;                 // an item's InvPos (bag 255 = the backpack / equipment)
        public int Count;                               // an item: how many (a split = fewer than the stack); 0 = the whole stack
        public int Index = -1;                          // a merchant / buyback row
        public int FromAction = -1;                     // the action slot it came from (-1 none)
        public int Money;
        public string IconPath, Name;
        public int Quality = -1;
        public object Source;
        public Action Restore;
        public bool Placeable { get { return Packed != 0 || (Kind == UiPickupKind.Action && Packed == 0); } }
    }

    /// <summary>The one cursor payload (proposal-uicore.md 5.2).</summary>
    public static class UiPickup
    {
        private static UiPickupPayload _cur;
        private static bool _dragging, _resolvable;
        private static float _pickedAt;
        private static int _version;
        private static UiButton _from;
        private static UiPickupPayload _dropping;   // the payload a target's DropInto is placing (a PickUp inside it = WoW's swap)
        private static int _logs;

        public static UiPickupPayload Current { get { return _cur; } }
        public static bool Holding { get { return _cur != null; } }
        /// <summary>A mouse button / the trigger is still held (vs. WoW's click-carry).</summary>
        public static bool Dragging { get { return _cur != null && _dragging; } }
        public static float PickedAt { get { return _pickedAt; } }
        public static int Version { get { return _version; } }
        /// <summary>The button it was picked from (null: picked by code, e.g. the spellbook's name, vrcast's slotting).</summary>
        public static UiButton From { get { return _cur != null ? _from : null; } }

        public static event Action<UiPickupPayload> PickedUp, Dropped, Cleared;

        /// <summary>Handlers for a payload dropped on a unit in the world (payload, unit guid) -> true = consumed (interact: trade).</summary>
        public static readonly List<Func<UiPickupPayload, ulong, bool>> DropOnUnit = new List<Func<UiPickupPayload, ulong, bool>>();

        /// <summary>UiPickup as a world-click mode of UiCursor: a left click on the world drops (DropOnWorld), a right click clears.</summary>
        private sealed class PickupMode : IUiCursorMode
        {
            public string Name { get { return "pickup"; } }
            public bool WorldClick(int button, ulong hovered)
            {
                if (_cur == null) return false;
                if (button == 0) DropOnWorld(hovered);
                else Clear("right click");   // ours: vrcast's "right-click off a button drops the cursor" (their hook 7)
                return true;
            }
            public void Cancel(string why)
            {
                if (_cur != null) ClearCore(why, true);
            }
        }

        private static readonly PickupMode Mode = new PickupMode();

        // ------------------------------------------------------------------ the API

        public static void PickUp(UiPickupPayload p, bool dragging, UiButton from)
        {
            if (p == null) return;
            // Inside a target's DropInto the old payload WAS placed (WoW's PlaceAction swap): no Restore for it.
            if (_cur != null) ClearCore(ReferenceEquals(_cur, _dropping) ? "placed" : "replaced", false);
            _cur = p;
            _dragging = dragging;
            _pickedAt = Time.unscaledTime;
            _from = from;
            _version++;
            _resolvable = false;
            if (p.Kind == UiPickupKind.Item && p.Bag >= 0)
            {
                ItemView it = ItemAt(Player(), p.Bag, p.Slot);
                _resolvable = it != null && (p.ItemEntry == 0 || it.Entry == p.ItemEntry);
            }
            UiCursor.Begin(Mode);
            PlayPickupSound(p);
            Log("picked " + p.Kind + " " + (p.Name ?? "?") + " from " + Where(p, from) + " (" + (dragging ? "drag" : "carry") + ")");
            Raise(PickedUp, p);
        }

        /// <summary>The target button takes the payload when its DropAccepts says so; false = not taken (it stays on the cursor).</summary>
        public static bool DropOn(UiButton target)
        {
            UiPickupPayload p = _cur;
            if (p == null || target == null || target.DropInto == null) return false;
            if (!Accepts(target, p)) return false;
            _dragging = false;
            _dropping = p;
            try { target.DropInto(p); }
            catch (Exception e) { WowLog.Error("UI pickup: drop on '" + target.name + "' failed: " + e); }
            finally { _dropping = null; }
            Raise(Dropped, p);
            PlayDropSound(p);
            Log("placed " + (p.Name ?? "?") + " on '" + target.name + "'");
            // The target either cleared it, swapped (a new payload on the cursor - WoW's PlaceAction) or left it: then it is placed.
            if (ReferenceEquals(_cur, p)) ClearCore("placed", false);
            return true;
        }

        public static bool Accepts(UiButton target, UiPickupPayload p)
        {
            if (target == null || p == null || target.DropAccepts == null) return false;
            try { return target.DropAccepts(p); }
            catch (Exception e) { WowLog.Warn("UI pickup: DropAccepts of '" + target.name + "' failed: " + e.Message); return false; }
        }

        /// <summary>A left click on a button while holding (WoW's click-carry): the source = put it back; an accepting button = DropOn;
        /// anything else keeps it. Not holding: a ClickPicksUp button picks its payload up.</summary>
        public static void ClickOn(UiButton b)
        {
            if (b == null) return;
            if (_cur == null)
            {
                if (b.ClickPicksUp && b.DragSource != null)
                {
                    UiPickupPayload p = null;
                    try { p = b.DragSource(); }
                    catch (Exception e) { WowLog.Warn("UI pickup: DragSource of '" + b.name + "' failed: " + e.Message); }
                    if (p != null) PickUp(p, false, b);
                }
                return;
            }
            if (ReferenceEquals(b, _from)) { Clear("put back"); return; }
            if (Accepts(b, _cur)) DropOn(b);
        }

        /// <summary>A drop on the world (5.2.6): a DropOnUnit consumer (trade) first; an item asks DELETE_ITEM / DELETE_GOOD_ITEM; the rest clears.</summary>
        public static void DropOnWorld(ulong hovered)
        {
            UiPickupPayload p = _cur;
            if (p == null) return;
            _dragging = false;
            if (p.Kind == UiPickupKind.Item && (UiPopup.IsShown(UiPickupRules.DeleteItem) || UiPopup.IsShown(UiPickupRules.DeleteGoodItem))) return;
            bool consumed = false;
            if (hovered != 0)
            {
                for (int i = 0; i < DropOnUnit.Count && !consumed; i++)
                {
                    try { consumed = DropOnUnit[i](p, hovered); }
                    catch (Exception e) { WowLog.Warn("UI pickup: a DropOnUnit handler failed: " + e.Message); }
                }
            }
            string r = UiPickupRules.WorldDrop((int)p.Kind, p.Quality, consumed);
            if (r == "trade")
            {
                Raise(Dropped, p);
                if (ReferenceEquals(_cur, p)) ClearCore("given", false);
                return;
            }
            if (r == UiPickupRules.DeleteItem || r == UiPickupRules.DeleteGoodItem) { AskDestroy(p, r); return; }
            Clear(r);
        }

        /// <summary>ClearCursor; runs the payload's Restore unless it was placed / removed / destroyed / given / dropped / gone.</summary>
        public static void Clear(string why)
        {
            if (_cur == null) return;
            ClearCore(why, false);
        }

        /// <summary>Desktop drag ended over UI that did not take it: WoW keeps it on the cursor (click-carry).</summary>
        public static void StopDragging() { _dragging = false; }

        private static void ClearCore(string why, bool fromMode)
        {
            UiPickupPayload p = _cur;
            if (p == null) return;
            _cur = null;
            _dragging = false;
            _from = null;
            _version++;
            if (!fromMode) UiCursor.End(Mode);
            if (p.Restore != null && Restores(why))
            {
                try { p.Restore(); }
                catch (Exception e) { WowLog.Warn("UI pickup: putting " + (p.Name ?? "?") + " back failed: " + e.Message); }
            }
            if (why != "placed" && why != "given") Log("cleared (" + why + ")");
            Raise(Cleared, p);
        }

        private static bool Restores(string why)
        {
            switch (why)
            {
                case "placed": case "removed": case "destroyed": case "given": case "dropped": case "gone": return false;
                default: return true;
            }
        }

        private static void Raise(Action<UiPickupPayload> ev, UiPickupPayload p)
        {
            if (ev == null) return;
            try { ev(p); }
            catch (Exception e) { WowLog.Warn("UI pickup: a listener failed: " + e.Message); }
        }

        private static void Log(string what)
        {
            _logs++;
            if (_logs <= 20 || _logs % 50 == 0) WowLog.Info("UI pickup: " + what + ".");
        }

        private static string Where(UiPickupPayload p, UiButton from)
        {
            if (p.Kind == UiPickupKind.Item && p.Bag >= 0) return "bag " + p.Bag + " slot " + p.Slot;
            if (p.FromAction >= 0) return "action button " + p.FromAction;
            if (from != null) return "'" + from.name + "'";
            return "code";
        }

        // ------------------------------------------------------------------ destroy (A4)

        private static int _askedFor;

        private static void AskDestroy(UiPickupPayload p, string which)
        {
            int bag = p.Bag, slot = p.Slot, count = p.Count;
            ItemView it = ItemAt(Player(), bag, slot);
            int shown = count > 0 ? count : it != null ? it.Count : 1;
            string name = UiPickupRules.DeleteName(p.Name ?? (it != null ? it.Name : null), UiArt.QualityHex(p.Quality >= 0 ? p.Quality : it != null ? it.Quality : 1), shown);
            bool good = which == UiPickupRules.DeleteGoodItem;
            // GlobalStrings.lua:1969-1971 (DELETE_GOOD_ITEM / DELETE_ITEM / DELETE_ITEM_CONFIRM_STRING), :8982 YES, :5181 NO.
            string text = good
                ? UiGlobalStrings.FormatOr("DELETE_GOOD_ITEM", "Do you want to destroy %s?\n\nType \"DELETE\" into the field to confirm.", name)
                : UiGlobalStrings.FormatOr("DELETE_ITEM", "Do you want to destroy %s?", name);
            string confirm = UiGlobalStrings.G("DELETE_ITEM_CONFIRM_STRING", "DELETE");
            int id = ++_askedFor;
            WowLog.Info("UI: destroy " + (p.Name ?? "?") + " x" + shown + " (bag " + bag + " slot " + slot + ") - asked (" + which + ").");
            Action accept = () =>
            {
                if (!ReferenceEquals(_cur, p)) return;
                IWowSession s = WowPlay.Session;
                if (s != null)
                {
                    try { s.DestroyItem(bag, slot, count); }
                    catch (Exception e) { WowLog.Error("UI: DestroyItem failed: " + e); }
                }
                WowLog.Info("UI: destroy " + (p.Name ?? "?") + " x" + shown + " (bag " + bag + " slot " + slot + ") - confirmed.");
                PlayDropSound(p);
                ClearCore("destroyed", false);
            };
            var spec = new UiPopupSpec
            {
                Which = which, Text = text,
                Button1 = UiGlobalStrings.G("YES", "Yes"), Button2 = UiGlobalStrings.G("NO", "No"),
                OnAccept = accept,
                OnCancel = () =>
                {
                    if (!ReferenceEquals(_cur, p)) return;
                    WowLog.Info("UI: destroy " + (p.Name ?? "?") + " (bag " + bag + " slot " + slot + ") - kept.");
                    Clear("kept");
                },
                // StaticPopup.lua:1583-1587 OnUpdate: hidden (no callbacks) once the cursor no longer holds that item.
                KeepOpen = () => ReferenceEquals(_cur, p) && (!_resolvable || StillThere(p)),
                WhileDead = true, Exclusive = true, ShowAlert = true, HideOnEscape = true,
                HasEditBox = good, MaxLetters = 32,
                Button1Enabled = good ? (Func<string, bool>)(t => UiPickupRules.DeleteConfirmOk(t, confirm)) : null,
                OnAcceptText = good ? (Action<string>)(t => accept()) : null,
                EnterClicksFirstButton = true,
                Data = id
            };
            if (UiPopup.Show(spec) == 0 && ReferenceEquals(_cur, p)) Clear("kept");
        }

        // ------------------------------------------------------------------ inventory lookups

        private static PlayerState Player()
        {
            IWowSession s = WowPlay.Session;
            return s != null ? s.Player : null;
        }

        /// <summary>The item at an InvPos address (equipment, containers, keyring, bank, buyback, else the flat Bags list), null = none.</summary>
        public static ItemView ItemAt(PlayerState p, int bag, int slot)
        {
            if (p == null || bag < 0 || slot < 0) return null;
            if (bag == InvPos.Bag0 && slot < InvPos.EquipEnd) return p.Equipment != null && slot < p.Equipment.Length ? p.Equipment[slot] : null;
            if (bag == InvPos.Bag0 && slot >= InvPos.BuybackStart && slot < InvPos.BuybackEnd)
                return p.Buyback != null && slot - InvPos.BuybackStart < p.Buyback.Length ? p.Buyback[slot - InvPos.BuybackStart] : null;
            ItemView v = InContainers(p.Containers, bag, slot);
            if (v != null) return v;
            if (p.Keyring != null) { v = InContainer(p.Keyring, bag, slot); if (v != null) return v; }
            v = InContainers(p.Bank, bag, slot);
            if (v != null) return v;
            if (p.Bags != null)
                for (int i = 0; i < p.Bags.Count; i++)
                {
                    ItemView b = p.Bags[i];
                    if (b != null && b.Bag == bag && b.Slot == slot) return b;
                }
            return null;
        }

        private static ItemView InContainers(List<ContainerView> list, int bag, int slot)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                ItemView v = InContainer(list[i], bag, slot);
                if (v != null) return v;
            }
            return null;
        }

        private static ItemView InContainer(ContainerView c, int bag, int slot)
        {
            if (c == null || c.Bag != bag || c.Slots == null) return null;
            int i = slot - c.SlotBase;
            return i >= 0 && i < c.Slots.Length ? c.Slots[i] : null;
        }

        private static bool StillThere(UiPickupPayload p)
        {
            ItemView it = ItemAt(Player(), p.Bag, p.Slot);
            return it != null && (p.ItemEntry == 0 || it.Entry == p.ItemEntry);
        }

        // ------------------------------------------------------------------ sounds (5.2.6)

        private static void PlayPickupSound(UiPickupPayload p)
        {
            switch (p.Kind)
            {
                case UiPickupKind.Item:
                    if (p.ItemEntry > 0) WowAudio.PlayItemPickup(p.ItemEntry);
                    break;
                case UiPickupKind.Spell:
                case UiPickupKind.Mount:
                    UiSound.Named("igSpellBookSpellIconPickup");   // SoundEntries 832 (DbcRows 2026-10-03)
                    break;
                case UiPickupKind.Action:
                case UiPickupKind.Macro:
                    UiSound.Named("igAbilityIconPickup");          // SoundEntries 837
                    break;
            }
        }

        private static void PlayDropSound(UiPickupPayload p)
        {
            // Spells / actions placed on a button: vrcast's PlaceOn plays igAbilityIconDrop (WowUiSound.ActionBarDrop) itself.
            if (p.Kind == UiPickupKind.Item && p.ItemEntry > 0) WowAudio.PlayItemDrop(p.ItemEntry);
        }

        // ------------------------------------------------------------------ per frame (WowArmUi): the icon, the vanished source

        private static Canvas _deskCanvas;
        private static RectTransform _deskIcon;
        private static RawImage _deskIconImg;
        private static GameObject _vrIconGo;
        private static RawImage _vrIconImg;
        private static string _iconShown;

        public static void Tick(UiContext ctx, bool vr)
        {
            UiPickupPayload p = _cur;
            if (p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0 && _resolvable && !StillThere(p)) { Clear("gone"); p = null; }
            if (p == null)
            {
                if (_deskCanvas != null && _deskCanvas.gameObject.activeSelf) _deskCanvas.gameObject.SetActive(false);
                if (_vrIconGo != null && _vrIconGo.activeSelf) _vrIconGo.SetActive(false);
                return;
            }
            Texture2D icon = !string.IsNullOrEmpty(p.IconPath) ? UiArt.Icon(p.IconPath) : null;
            if (vr) { if (_deskCanvas != null && _deskCanvas.gameObject.activeSelf) _deskCanvas.gameObject.SetActive(false); VrIcon(icon); }
            else { if (_vrIconGo != null && _vrIconGo.activeSelf) _vrIconGo.SetActive(false); DeskIcon(icon); }
        }

        /// <summary>5.2.3: "WoW Cursor Item" overlay (sorting 32000, no raycaster): the icon 32x32 at the mouse + Item.blp's pointer over its top-left.</summary>
        private static void DeskIcon(Texture2D icon)
        {
            if (_deskCanvas == null)
            {
                var go = new GameObject("WoW Cursor Item", typeof(RectTransform));
                go.layer = UiKit.Layer;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _deskCanvas = go.AddComponent<Canvas>();
                _deskCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _deskCanvas.sortingOrder = 32000;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1024f, 768f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
                bool over = UiKit.Over;
                UiKit.Over = false;
                _deskIcon = UiKit.Rect(go.transform, "Icon holder", 0f, 0f, 32f, 32f);
                _deskIcon.anchorMin = _deskIcon.anchorMax = new Vector2(0f, 0f);
                _deskIconImg = UiKit.Pic(_deskIcon, "Icon", null, 0f, 0f, 32f, 32f);
                UiKit.Pic(_deskIcon, "Pointer", UiArt.Tex(@"Interface\CURSOR\Item.blp"), 0f, 0f, 32f, 32f);   // 32x32 BLP, its 12x12 pointer at the top-left (probe UicCursors)
                UiKit.Over = over;
            }
            if (!_deskCanvas.gameObject.activeSelf) _deskCanvas.gameObject.SetActive(true);
            SetIcon(_deskIconImg, icon);
            Vector2 local;
            var root = (RectTransform)_deskCanvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, null, out local))
            {
                // root's pivot is its centre; the holder is anchored bottom-left with its pivot top-left.
                Rect r = root.rect;
                _deskIcon.anchoredPosition = new Vector2(local.x - r.xMin, local.y - r.yMin);
            }
        }

        /// <summary>5.2.4: on the surface under the laser (36 units, just in front of it) or 0.35 m along the laser, facing the head.</summary>
        private static void VrIcon(Texture2D icon)
        {
            if (_vrIconGo == null)
            {
                _vrIconGo = new GameObject("WoW Cursor Item (VR)", typeof(RectTransform));
                _vrIconGo.layer = UiKit.Layer;
                UnityEngine.Object.DontDestroyOnLoad(_vrIconGo);
                var c = _vrIconGo.AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                c.overrideSorting = true;
                c.sortingOrder = 250;
                UiSharp.Register(_vrIconGo);   // lead 2026-10-10
                var r = (RectTransform)_vrIconGo.transform;
                r.sizeDelta = new Vector2(36f, 36f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.localScale = Vector3.one * (0.04f / 36f);   // 4 cm (ours)
                bool over = UiKit.Over;
                UiKit.Over = true;
                RectTransform holder = UiKit.Rect(r, "Content", 0f, 0f, 36f, 36f);
                holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
                holder.anchoredPosition = Vector2.zero;
                _vrIconImg = UiKit.Pic(holder, "Icon", null, 0f, 0f, 36f, 36f);
                UiKit.Over = over;
            }
            if (!_vrIconGo.activeSelf) _vrIconGo.SetActive(true);
            SetIcon(_vrIconImg, icon);
            Transform t = _vrIconGo.transform;
            Camera head = WowPlayer.Head();
            if (UiPointer.LastSurface != null && UiPointer.LastSurface.Shown)
            {
                Transform sr = UiPointer.LastSurface.Root;
                t.SetPositionAndRotation(UiPointer.LastPoint - sr.forward * 0.006f, sr.rotation);
            }
            else if (UiPointer.HasRay)
            {
                Vector3 pos = UiPointer.LastRay.GetPoint(0.35f);
                Vector3 look = head != null ? pos - head.transform.position : UiPointer.LastRay.direction;
                if (look.sqrMagnitude < 1e-6f) look = Vector3.forward;
                t.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            }
        }

        private static void SetIcon(RawImage img, Texture2D icon)
        {
            if (img == null) return;
            if (icon == null)
            {
                if (_iconShown != null) { _iconShown = null; img.texture = UiArt.Tex(UiArt.QuestionMark); img.color = Color.white; }
                return;
            }
            if (img.texture != icon) { img.texture = icon; img.color = Color.white; UiKit.Coords(img, 0f, 1f, 0f, 1f); }
            _iconShown = icon.name;
        }

        /// <summary>Session end / VR-desktop switch: nothing stays on the cursor.</summary>
        public static void Reset(string why)
        {
            if (_cur != null) Clear(why);
            if (_deskCanvas != null) _deskCanvas.gameObject.SetActive(false);
            if (_vrIconGo != null) _vrIconGo.SetActive(false);
        }
    }

    /// <summary>
    /// The desktop drag handler on draggable / droppable buttons (proposal-uicore.md 5.2.1), added by UiKit.Draggable / DropTarget on the
    /// desktop only. Left button only (right / middle drags are the client's camera). OnBeginDrag sets eligibleForClick = false (the XRI
    /// UIInputModule would otherwise CLICK the source when the drag ends over it - UIInputModule.cs:566-570).
    /// </summary>
    public sealed class UiDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public UiButton Button;

        public void OnBeginDrag(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            if (Button == null || Button.DragSource == null || !Button.Interactable) return;
            e.eligibleForClick = false;
            if (UiPickup.Holding) return;   // carrying something already: the release decides (click-carry)
            UiPickupPayload p = null;
            try { p = Button.DragSource(); }
            catch (Exception ex) { WowLog.Warn("UI pickup: DragSource of '" + Button.name + "' failed: " + ex.Message); }
            if (p != null) UiPickup.PickUp(p, true, Button);
        }

        public void OnDrag(PointerEventData e) { }

        public void OnDrop(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            if (!UiPickup.Dragging || Button == null) return;
            if (!UiPickup.DropOn(Button)) UiPickup.StopDragging();   // WoW: dropped on a frame that does not take it -> it stays held
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            if (!UiPickup.Dragging) return;
            UiPickup.StopDragging();
            if (!WowEntityViews.PointerOverUi()) UiCursor.WorldClick(0, WowEntityViews.HoveredGuid);   // released over the world
        }
    }
}
