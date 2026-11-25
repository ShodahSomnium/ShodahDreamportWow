// iteration 11 - owner vrcast (proposal-vrcast.md s5.4; uicore H13): WoW's cursor for actions (PickupAction / PickupSpell / PlaceAction) - a
// FACADE over uicore's one cursor UiPickup (proposal-uicore.md 5.2): what it holds is UiPickup.Current when that is placeable on an action
// button (Packed != 0, or Kind Action with Packed 0 = "clear the next button"). While UiPickup does not take a pick (its wave-0 stub keeps
// nothing), the facade keeps the pick itself, so placing spells on buttons never stops working (logged once which way it runs).
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public static class UiActionCursor
    {
        private static UiPickupPayload _own;     // our own pick while UiPickup did not take it
        private static float _ownAt;
        private static bool _ownDrag, _modeLogged;
        private static int _version, _places;

        /// <summary>WowArmUi.Awake sets it to UiContext.Say (the message lines); the bars set it too when it is still null.</summary>
        public static System.Action<string, Color> Say;

        private static UiPickupPayload Cur
        {
            get
            {
                UiPickupPayload c = UiPickup.Current;
                return c ?? _own;
            }
        }

        /// <summary>Something an action button can take is on the cursor.</summary>
        public static bool Active { get { UiPickupPayload c = Cur; return c != null && c.Placeable; } }
        /// <summary>The packed action on the cursor (WowActionSlots); 0 with Active = "clear the button".</summary>
        public static int Packed { get { UiPickupPayload c = Cur; return c != null && c.Placeable ? c.Packed : 0; } }
        public static string Name { get { UiPickupPayload c = Cur; return c != null && c.Placeable && c.Name != null ? c.Name : ""; } }
        public static string IconPath { get { UiPickupPayload c = Cur; return c != null && c.Placeable ? c.IconPath : null; } }
        /// <summary>The action slot it came from (-1: the spellbook, the Mounts pane, the bags, a swap).</summary>
        public static int FromSlot { get { UiPickupPayload c = Cur; return c != null && c.Placeable ? c.FromAction : -1; } }
        /// <summary>A desktop drag still in progress (dropped on nothing = thrown away).</summary>
        public static bool FromDrag { get { return UiPickup.Current != null ? UiPickup.Dragging : _own != null && _ownDrag; } }
        /// <summary>Time.unscaledTime of the pick (the VR slotting timeout).</summary>
        public static float PickedAt { get { return UiPickup.Current != null ? UiPickup.PickedAt : _ownAt; } }
        /// <summary>+1 on every change.</summary>
        public static int Version { get { return _version + UiPickup.Version; } }

        private static UiPickupKind KindFor(int packed, int fromSlot)
        {
            if (fromSlot >= 0 || packed == 0) return UiPickupKind.Action;
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Item: return UiPickupKind.Item;
                case ActionKind.Spell: return UiPickupKind.Spell;
                case ActionKind.Macro: return UiPickupKind.Macro;
                default: return UiPickupKind.Action;
            }
        }

        /// <summary>PickupAction / PickupSpell: puts packed on the cursor (replacing what was there). packed 0 = "clear the next button".</summary>
        public static void Pick(int packed, string name, string iconPath, int fromSlot, bool fromDrag)
        {
            var pl = new UiPickupPayload
            {
                Kind = KindFor(packed, fromSlot),
                Packed = packed,
                SpellId = WowActionSlots.SpellOf(packed),
                ItemEntry = WowActionSlots.ItemOf(packed),
                FromAction = fromSlot,
                Name = name ?? "",
                IconPath = iconPath,
            };
            PickPayload(pl, fromDrag);
        }

        /// <summary>A payload built by a view (a bar's drag source, with its Restore) onto the cursor.</summary>
        public static void PickPayload(UiPickupPayload pl, bool fromDrag)
        {
            if (pl == null) return;
            _own = null;
            UiPickup.PickUp(pl, fromDrag, null);
            bool taken = ReferenceEquals(UiPickup.Current, pl);
            if (!taken) { _own = pl; _ownAt = Time.unscaledTime; _ownDrag = fromDrag; }
            _version++;
            if (!_modeLogged)
            {
                _modeLogged = true;
                WowLog.Info("UI: the action cursor runs " + (taken ? "over uicore's UiPickup (one cursor for the whole UI)" : "on its own state (UiPickup did not take the pick - its stub)") + " (logged once)");
            }
        }

        /// <summary>ClearCursor for an action (cancel): uicore's Clear runs the payload's Restore (a dragged button gets its action back).</summary>
        public static void Clear()
        {
            UiPickupPayload c = UiPickup.Current;
            if (c != null && c.Placeable) UiPickup.Clear("cancel");
            if (_own != null) { UiPickupPayload own = _own; _own = null; if (own.Restore != null) { try { own.Restore(); } catch (System.Exception e) { WowLog.Warn("UI: action cursor restore failed: " + e.Message); } } }
            _version++;
        }

        /// <summary>Placed: nothing to restore.</summary>
        private static void ClearPlaced()
        {
            UiPickupPayload c = UiPickup.Current;
            if (c != null && c.Placeable) UiPickup.Clear("placed");
            _own = null;
            _version++;
        }

        private static void Tell(string text, Color c)
        {
            if (Say != null) Say(text, c);
        }

        /// <summary>
        /// PlaceAction on slot: s.SetActionButton(slot, Packed), the igAbilityIconDrop sound, "X placed on &lt;bar&gt; button N." (or "&lt;bar&gt;
        /// button N cleared."); returns the slot's previous value. Desktop (vr false): a non-empty previous value goes onto the cursor (WoW's
        /// swap); VR: it is dropped. A value the session refuses (it raises its own Error) stays on the cursor.
        /// </summary>
        public static int PlaceOn(IWowSession s, int slot, bool vr)
        {
            UiPickupPayload c = Cur;
            if (s == null || c == null || !c.Placeable || !WowActionSlots.Valid(slot)) return 0;
            PlayerState p = s.Player;
            if (p == null || p.ActionButtons == null) return 0;
            int old = p.ActionButtons[slot];
            int packed = c.Packed;
            string name = c.Name ?? "";
            s.SetActionButton(slot, packed);
            if (p.ActionButtons[slot] != packed) return old;   // refused: the session said why; the cursor keeps it
            UiSound.Play(WowUiSound.ActionBarDrop);             // igAbilityIconDrop
            string where = WowActionSlots.BarName(slot) + " button " + WowActionSlots.ButtonOf(slot);
            Tell(packed == 0 ? where + " cleared." : name + " placed on " + where + ".", UiArt.InfoYellow);
            _places++;
            if (_places <= 30 || _places % 50 == 0)
                WowLog.Info("UI: " + (packed == 0 ? "cleared " + where : name + " placed on " + where) + " (slot " + slot + (vr ? ", VR" : "") + ")"
                            + (old != 0 && old != packed ? (vr || packed == 0 ? " - replaced " : " - swapped, on the cursor now: ") + UiActions.NameOf(p, old) : ""));
            if (!vr && old != 0 && old != packed && packed != 0)
            {
                // WoW's PlaceAction: what the button held is now on the cursor (drop it on another button, or cancel to throw it away).
                string oldName = UiActions.NameOf(p, old), oldIcon = UiActions.IconOf(p, old);
                ClearPlaced();
                PickPayload(new UiPickupPayload
                {
                    Kind = UiPickupKind.Action,
                    Packed = old,
                    SpellId = WowActionSlots.SpellOf(old),
                    ItemEntry = WowActionSlots.ItemOf(old),
                    FromAction = -1,
                    Name = oldName,
                    IconPath = oldIcon,
                }, false);
                return old;
            }
            ClearPlaced();
            return old;
        }
    }
}
