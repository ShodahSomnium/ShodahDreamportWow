// iteration 11 - owner npcui (proposal-npc.md 4 / 6.1 / 6.3; the wave-0 stub by w0game, filled by npcui)
// The shared item-slot rules of every inventory slot (bags, keyring, bank, and the paper doll through PayloadAt / SlotClick), one item
// button (UiItemButton = ItemButtonTemplate), WoW's ContainerFrame (UiContainerFrame) and the bag bar (UiBagBar = MainMenuBarBagButtons,
// built by uicore's UiMicroMenu). Values: the FrameXML of the user's install (harness UiDump into P/harness/out_d11npc - ContainerFrame.lua
// / .xml, MainMenuBarBagButtons.xml / .lua, ItemButtonTemplate.xml, Constants.lua, GlobalStrings.lua), file:line per value; "ours" = our
// own choice (UiBags logs them once).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>WoW's item-slot rules for every inventory slot, one place for all windows (positions are InvPos pairs).</summary>
    public static class UiContainers
    {
        public const string BagComponents = @"Interface\ContainerFrame\UI-Bag-Components";     // ContainerFrame.lua:399-414 (+ "-Bank" / "-Keyring")
        public const string Bag1Slot = @"Interface\ContainerFrame\UI-Bag-1Slot.blp";            // ContainerFrame.xml:141-152
        public const string QuestBorder = @"Interface\ContainerFrame\UI-Icon-QuestBorder.blp";  // TEXTURE_ITEM_QUEST_BORDER (Constants.lua:473)
        public const string KeyRingIcon = @"Interface\ContainerFrame\KeyRing-Bag-Icon.blp";     // ContainerFrame.lua:497
        public const string EmptyBagSlot = @"Interface\PaperDoll\UI-PaperDoll-Slot-Bag.blp";    // an empty bag button (proposal-npc.md 4)
        public const string KeyRingButton = @"Interface\Buttons\UI-Button-KeyRing";             // MainMenuBarBagButtons.xml:210-218
        public const int KeyringId = -2;                                                         // KEYRING_CONTAINER (Constants.lua:224)
        public const int BankBagFirstId = 5, BankBagLastId = 11;                                 // NUM_BAG_SLOTS 4 + NUM_BANKBAGSLOTS 7 (Constants.lua:226-228)
        public static readonly Color LockedTint = new Color(0.5f, 0.5f, 0.5f);                  // SetItemButtonDesaturated(locked, 0.5, 0.5, 0.5) (ContainerFrame.lua:286)

        // ------------------------------------------------------------------ where an item is

        /// <summary>The item at an InvPos (bag 255: equipment 0-18, bag items 19-22 / 67-73, backpack, bank, keyring; else a bag's slot); null = empty.</summary>
        public static ItemView ItemAt(PlayerState p, int bag, int slot)
        {
            if (p == null) return null;
            if (bag == InvPos.Bag0)
            {
                if (slot >= 0 && slot < InvPos.EquipEnd) return p.Equipment != null && slot < p.Equipment.Length ? p.Equipment[slot] : null;
                if (slot >= InvPos.BagSlotStart && slot < InvPos.BagSlotEnd)
                {
                    ContainerView c = At(p.Containers, slot - InvPos.BagSlotStart + 1);
                    return c != null ? c.BagItem : null;
                }
                if (slot >= InvPos.BankBagStart && slot < InvPos.BankBagEnd)
                {
                    ContainerView c = At(p.Bank, slot - InvPos.BankBagStart + 1);
                    return c != null ? c.BagItem : null;
                }
                // The session has no container list yet (a session built before npc's Containers): the old flat backpack list.
                if ((p.Containers == null || p.Containers.Count == 0) && slot >= InvPos.PackStart && slot < InvPos.PackEnd)
                {
                    int i = slot - InvPos.PackStart;
                    return p.Bags != null && i < p.Bags.Count ? p.Bags[i] : null;
                }
            }
            ItemView it;
            if (p.Containers != null) for (int i = 0; i < p.Containers.Count; i++) if (SlotOf(p.Containers[i], bag, slot, out it)) return it;
            if (SlotOf(p.Keyring, bag, slot, out it)) return it;
            if (p.Bank != null) for (int i = 0; i < p.Bank.Count; i++) if (SlotOf(p.Bank[i], bag, slot, out it)) return it;
            return null;
        }

        private static ContainerView At(List<ContainerView> list, int i)
        {
            return list != null && i >= 0 && i < list.Count ? list[i] : null;
        }

        private static bool SlotOf(ContainerView c, int bag, int slot, out ItemView it)
        {
            it = null;
            if (c == null || c.Bag != bag || c.Slots == null) return false;
            int i = slot - c.SlotBase;
            if (i < 0 || i >= c.Size || i >= c.Slots.Length) return false;
            it = c.Slots[i];
            return true;
        }

        /// <summary>Player.Bags index of the item at (bag, slot) - what ConfirmTargetingItem / UiInteract.TryUseBagItem take; -1 = not in the bags.</summary>
        public static int BagIndexOf(PlayerState p, int bag, int slot)
        {
            if (p == null || p.Bags == null) return -1;
            ItemView it = ItemAt(p, bag, slot);
            for (int i = 0; i < p.Bags.Count; i++)
            {
                ItemView b = p.Bags[i];
                if (b == null) continue;
                if ((it != null && ReferenceEquals(b, it)) || (b.Bag == bag && b.Slot == slot)) return i;
            }
            return -1;
        }

        /// <summary>The first empty keyring slot (InvPos slot 86..), -1 = none / no keyring.</summary>
        public static int FreeKeyringSlot(PlayerState p)
        {
            ContainerView k = p != null ? p.Keyring : null;
            if (k == null || k.Slots == null) return -1;
            for (int i = 0; i < k.Size && i < k.Slots.Length; i++) if (k.Slots[i] == null) return k.SlotBase + i;
            return -1;
        }

        // ------------------------------------------------------------------ modes that own a slot click

        /// <summary>A spell waits for an item (spellfx: Player.Targeting.Kind == Item) - ContainerFrame.lua:698-700 SpellCanTargetItem first.</summary>
        public static bool ItemTargeting(UiContext ctx)
        {
            PlayerState p = ctx != null ? ctx.P : null;
            return p != null && p.Targeting != null && p.Targeting.Kind == SpellTargetingKind.Item;
        }

        /// <summary>A click on an item slot belongs to a mode (item targeting, the merchant's repair cursor), not to the pick-up.</summary>
        public static bool SpecialMode(UiContext ctx)
        {
            return ItemTargeting(ctx) || UiMerchant.RepairMode;
        }

        /// <summary>Desktop Shift (SPLITSTACK / OPENALLBAGS modifier); false in VR.</summary>
        public static bool ShiftHeld(UiContext ctx)
        {
            if (ctx != null && ctx.Vr) return false;
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>Whether a plain left click on an item slot picks the item up right now (uicore reads UiButton.ClickPicksUp at the click):
        /// not while a mode owns the click and not with Shift (the stack split) - the slot's Click gets those clicks instead.</summary>
        public static bool SlotsPickUp(UiContext ctx)
        {
            return !SpecialMode(ctx) && !ShiftHeld(ctx);
        }

        /// <summary>Item targeting / the repair cursor take the click on (bag, slot); true = consumed.</summary>
        public static bool SpecialClick(UiContext ctx, int bag, int slot)
        {
            IWowSession s = ctx != null ? ctx.S : null;
            PlayerState p = s != null ? s.Player : null;
            if (s == null || p == null) return false;
            if (ItemTargeting(ctx))
            {
                if (InvPos.IsEquipment(bag, slot)) { s.ConfirmTargetingItem(-1, slot); return true; }
                int idx = BagIndexOf(p, bag, slot);
                if (idx >= 0) s.ConfirmTargetingItem(idx, -1);
                else Note("item targeting: the item at bag " + bag + " slot " + slot + " is not in the bags - not a target (bank / keyring).");
                return true;
            }
            if (UiMerchant.RepairMode)
            {
                if (ItemAt(p, bag, slot) != null) { s.RepairItem(bag, slot); UiSound.Named("ITEM_REPAIR"); }   // MerchantFrame.xml repair OnClick sound
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the cursor payload of a slot (uicore's UiPickup)

        /// <summary>The item at (bag, slot) as a cursor payload (bag 255 = the backpack / equipment, InvPos numbering); null when empty,
        /// locked (it waits for the server) or while a mode owns the click (spellfx's item targeting, the repair cursor).</summary>
        public static UiPickupPayload PayloadAt(UiContext ctx, int bag, int slot)
        {
            PlayerState p = ctx != null ? ctx.P : null;
            if (p == null || SpecialMode(ctx)) return null;
            ItemView it = ItemAt(p, bag, slot);
            if (it == null || it.Locked) return null;
            return ItemPayload(it, bag, slot, 0);
        }

        /// <summary>An item payload (Count 0 = the whole stack). Packed = the item as an action button would hold it (WoW: any item goes on a bar).</summary>
        public static UiPickupPayload ItemPayload(ItemView it, int bag, int slot, int count)
        {
            return new UiPickupPayload
            {
                Kind = UiPickupKind.Item, ItemEntry = it.Entry, Packed = it.Entry > 0 ? WowActionSlots.Item(it.Entry) : 0,
                Bag = bag, Slot = slot, Count = count, IconPath = it.IconPath, Name = it.Name, Quality = it.Quality, Source = it
            };
        }

        /// <summary>Shift-click / VR long press on a stack: WoW's StackSplitFrame (OpenStackSplitFrame(itemCount), ContainerFrame.lua:743-752);
        /// the chosen count goes onto the cursor (SplitContainerItem = a payload with Count, placed by the target's SplitItem).</summary>
        public static void OpenSplit(UiContext ctx, UiButton near, int bag, int slot)
        {
            PlayerState p = ctx != null ? ctx.P : null;
            ItemView it = ItemAt(p, bag, slot);
            if (it == null || it.Locked || it.Count <= 1) return;
            int stack = it.Count;
            UiStackSplit.Open(stack, near, n =>
            {
                PlayerState now = ctx.P;
                ItemView cur = ItemAt(now, bag, slot);
                if (cur == null || n <= 0) return;
                UiPickup.PickUp(ItemPayload(cur, bag, slot, n >= cur.Count ? 0 : n), false, near);
            });
        }

        // ------------------------------------------------------------------ clicks

        /// <summary>
        /// A press on an item slot that the cursor framework did not take: item targeting / repair first; desktop left click (Shift = the
        /// split, otherwise it reaches here only without uicore's pick-up) ; VR tap and the right click = WoW's UseContainerItem.
        /// </summary>
        public static void SlotClick(UiContext ctx, UiButton b, int bag, int slot, bool right)
        {
            if (ctx == null || ctx.S == null) return;
            if (right && UiMerchant.RepairMode) { UiMerchant.EndRepairMode("right click"); return; }   // WoW: a right click drops the repair cursor
            if (SpecialClick(ctx, bag, slot)) return;
            if (!right && !ctx.Vr)
            {
                if (ShiftHeld(ctx)) { OpenSplit(ctx, b, bag, slot); return; }
                if (UiPickup.Holding) return;   // uicore's ClickOn places it
            }
            UseContainerItem(ctx, bag, slot);
        }

        /// <summary>WoW's UseContainerItem rules (ContainerFrame.lua:719-733 + the client's C side): mail / trade take it first, then sell to
        /// the open merchant (not on its Buyback tab), bank <-> bags while the bank is open, else use / equip / open / read it. Equipped items
        /// are only used (UseInventoryItem, PaperDollFrame.lua:1244-1246).</summary>
        public static void UseContainerItem(UiContext ctx, int bag, int slot)
        {
            IWowSession s = ctx != null ? ctx.S : null;
            PlayerState p = s != null ? s.Player : null;
            if (p == null || ItemAt(p, bag, slot) == null) return;
            if (InvPos.IsEquipment(bag, slot)) { s.UseItemAt(bag, slot); return; }
            if (UiMail.TryAttachFromBag(bag, slot)) return;                      // socialui H4: an open Send Mail tab takes it
            int idx = BagIndexOf(p, bag, slot);
            if (idx >= 0 && UiInteract.TryUseBagItem(idx)) return;               // interact H8: the trade window / the Auctions tab
            if (s.CurrentMerchant != null)
            {
                if (UiMerchant.BuybackTabShown) return;                          // MerchantFrame.selectedTab == 2: no sale (ContainerFrame.lua:722-725)
                if (!InvPos.IsBank(bag, slot)) { s.SellItem(bag, slot, 0); return; }
            }
            if (s.BankerGuid != 0) { s.BankItem(bag, slot); return; }
            s.UseItemAt(bag, slot);
        }

        // ------------------------------------------------------------------ drops on a slot

        /// <summary>What an item slot (bag / keyring / bank slot) takes: an inventory item (move / swap / merge / split) or a merchant row (buy into it).</summary>
        public static bool AcceptsDrop(UiContext ctx, UiPickupPayload p, int bag, int slot)
        {
            if (p == null || ctx == null || ctx.S == null) return false;
            if (p.Kind == UiPickupKind.Item) return p.Bag >= 0;
            if (p.Kind == UiPickupKind.MerchantItem) return ctx.S.CurrentMerchant != null && p.Index >= 0;
            return false;
        }

        /// <summary>The drop: the target decides (uicore 5.2.2) - MoveItem, SplitItem (a payload with fewer than the stack) or a purchase into the slot.</summary>
        public static void DropOnSlot(UiContext ctx, UiPickupPayload p, int bag, int slot)
        {
            IWowSession s = ctx != null ? ctx.S : null;
            if (p == null || s == null) return;
            if (p.Kind == UiPickupKind.Item && p.Bag >= 0)
            {
                if (p.Bag == bag && p.Slot == slot) return;   // put back
                ItemView from = ItemAt(s.Player, p.Bag, p.Slot);
                if (p.Count > 0 && from != null && p.Count < from.Count) s.SplitItem(p.Bag, p.Slot, bag, slot, p.Count);
                else s.MoveItem(p.Bag, p.Slot, bag, slot);
                return;
            }
            if (p.Kind == UiPickupKind.MerchantItem) BuyChecked(ctx, p.Index, 1, bag, slot);   // ContainerFrame.lua:707-714
        }

        /// <summary>The hint line while something is held over the slot (ours).</summary>
        public static string DropHint(UiContext ctx, UiPickupPayload p, int bag, int slot)
        {
            if (p == null) return null;
            if (p.Kind == UiPickupKind.MerchantItem) return "<color=#00ff00>Buy into this slot.</color>";
            if (p.Kind == UiPickupKind.Item && p.Count > 0) return "<color=#00ff00>Place " + p.Count + " here.</color>";
            return null;
        }

        /// <summary>WoW's purchase rules: an extended cost asks CONFIRM_PURCHASE_TOKEN_ITEM (MerchantFrame.lua:454-509), a price of
        /// MERCHANT_HIGH_PRICE_COST or more asks CONFIRM_HIGH_COST_ITEM (:378, :521-530), else BuyItem at once.</summary>
        public static void BuyChecked(UiContext ctx, int index, int purchases, int bag, int slot)
        {
            IWowSession s = ctx != null ? ctx.S : null;
            MerchantView m = s != null ? s.CurrentMerchant : null;
            if (m == null || index < 0 || index >= m.Items.Count || purchases <= 0) return;
            MerchantItemView row = m.Items[index];
            if (UiNpcPopups.NeedsTokenConfirm(ctx, row)) { UiPopup.Show(UiNpcPopups.PurchaseToken(ctx, index, purchases, bag, slot)); return; }
            if (purchases == 1 && row.Price >= UiMerchant.HighPriceCost) { UiPopup.Show(UiNpcPopups.HighCost(ctx, index, purchases, bag, slot)); return; }
            s.BuyItem(index, purchases, bag, slot);
        }

        // ------------------------------------------------------------------ tooltips

        /// <summary>The item tooltip of (bag, slot) with WoW's merchant line and our action hint.</summary>
        public static string Tip(UiContext ctx, int bag, int slot)
        {
            PlayerState p = ctx != null ? ctx.P : null;
            ItemView it = ItemAt(p, bag, slot);
            if (it == null) return null;
            return UiTooltip.Item(it, Hint(ctx, it, bag, slot));
        }

        private static string Hint(UiContext ctx, ItemView it, int bag, int slot)
        {
            IWowSession s = ctx.S;
            PlayerState p = ctx.P;
            bool vr = ctx.Vr;
            if (ItemTargeting(ctx))
                return "<color=#00ff00>" + (vr ? "Tap" : "Click") + " to use " + (p.Targeting.Name ?? "the spell") + " on this item.</color>";   // ours (spellfx hook 11)
            if (UiMerchant.RepairMode) return "<color=#00ff00>" + (vr ? "Tap" : "Click") + " to repair this item.</color>";   // ours
            string q = it.IsQuestItem && (it.Tooltip == null || it.Tooltip.IndexOf("Quest Item", StringComparison.Ordinal) < 0)
                ? "<color=#ffd100>" + UiGlobalStrings.G("ITEM_BIND_QUEST", "Quest Item") + "</color>\n" : "";   // GlobalStrings.lua:4341
            if (InvPos.IsEquipment(bag, slot)) return q.Length > 0 ? q.TrimEnd('\n') : null;
            if (s != null && s.CurrentMerchant != null && !UiMerchant.BuybackTabShown && !InvPos.IsBank(bag, slot))
            {
                if (it.SellPrice <= 0) return q + UiGlobalStrings.G("ITEM_UNSELLABLE", "No sell price");   // GlobalStrings.lua:4534 (GameTooltip, white)
                return q + "<color=#00ff00>" + (vr ? "Tap to sell." : "Right-click to sell.") + "</color>";   // ours
            }
            if (s != null && s.BankerGuid != 0)
                return q + "<color=#00ff00>" + (vr ? "Tap" : "Right-click") + (InvPos.IsBank(bag, slot) ? " to take it to your bags." : " to put it in the bank.") + "</color>";   // ours
            if (vr) return q + "<color=#00ff00>Tap to use. Hold and move to drag.</color>";   // ours
            return q.Length > 0 ? q.TrimEnd('\n') : null;
        }

        // ------------------------------------------------------------------ wiring a slot button

        /// <summary>Makes b a WoW item slot: drag source + click pick-up (uicore), drop target, Shift / long-press split, right click = use.</summary>
        public static void Wire(UiButton b, UiContext ctx, Func<int> bagOf, Func<int> slotOf)
        {
            UiKit.Draggable(b, () => PayloadAt(ctx, bagOf(), slotOf()), true);
            UiKit.DropTarget(b, p => AcceptsDrop(ctx, p, bagOf(), slotOf()), p => DropOnSlot(ctx, p, bagOf(), slotOf()), p => DropHint(ctx, p, bagOf(), slotOf()));
            b.Click = () => SlotClick(ctx, b, bagOf(), slotOf(), false);
            b.RightClick = () => SlotClick(ctx, b, bagOf(), slotOf(), true);
            b.LongPress = () => OpenSplit(ctx, b, bagOf(), slotOf());
            b.Tip = () => Tip(ctx, bagOf(), slotOf());
            b.HoverCursor = () => SellCursor(ctx, bagOf(), slotOf());   // lead fix 2026-10-03 (the user: "No icon for selling")
        }

        /// <summary>ContainerFrameItemButton_OnEnter (ContainerFrame.lua:779-786): with the merchant's own tab shown, a bag item gets the sell
        /// cursor (ShowContainerSellCursor: "Buy"; "UnableBuy" for an item without a sell price - ours, the client decides that itself).</summary>
        private static string SellCursor(UiContext ctx, int bag, int slot)
        {
            IWowSession s = ctx != null ? ctx.S : null;
            if (s == null || s.CurrentMerchant == null || UiMerchant.BuybackTabShown || UiMerchant.RepairMode) return null;
            if (InvPos.IsBank(bag, slot) || InvPos.IsEquipment(bag, slot)) return null;
            ItemView it = ItemAt(ctx.P, bag, slot);
            if (it == null) return null;
            return it.SellPrice > 0 ? "Buy" : "UnableBuy";
        }

        // ------------------------------------------------------------------ logging

        private static readonly HashSet<string> Noted = new HashSet<string>();

        /// <summary>One log line per distinct text (ours).</summary>
        public static void Note(string text)
        {
            if (Noted.Count > 200 || !Noted.Add(text)) return;
            WowLog.Info("UI bags: " + text);
        }
    }

    /// <summary>Restores UiKit's build flags after building widgets outside the layout's Build (lazily made container frames / slots).</summary>
    public struct UiBuildScope : IDisposable
    {
        private readonly UiSurface _surface;
        private readonly bool _pointable, _over, _on;

        public UiBuildScope(UiSurface surface, bool pointable, bool over)
        {
            _surface = UiKit.Surface;
            _pointable = UiKit.Pointable;
            _over = UiKit.Over;
            _on = true;
            UiKit.Surface = surface;
            UiKit.Pointable = pointable;
            UiKit.Over = over;
        }

        public void Dispose()
        {
            if (!_on) return;
            UiKit.Surface = _surface;
            UiKit.Pointable = _pointable;
            UiKit.Over = _over;
        }
    }

    /// <summary>
    /// One ItemButtonTemplate (ItemButtonTemplate.xml): the icon, Count BOTTOMRIGHT (-5, 2) NumberFontNormal, Stock TOPLEFT (0, -2)
    /// NumberFontNormalYellow, the quest border (ContainerFrameItemButtonTemplate IconQuestTexture 37x38 at TOP, ContainerFrame.xml:14-21),
    /// locked = drawn at 0.5 grey. The UI-Quickslot2 frame shows only with an item (the container art draws the empty slots).
    /// </summary>
    public sealed class UiItemButton
    {
        public readonly UiButton B;
        private readonly RawImage _quest;
        private readonly float _size;
        private TextMeshProUGUI _stock;
        private ItemView _item;
        private string _path;
        private bool _loaded;
        private int _count = -1, _stockShown = int.MinValue;

        public UiItemButton(Transform parent, string name, float x, float y, float size)
        {
            _size = size;
            B = UiKit.Slot(parent, name, x, y, size, true);
            if (B.Frame != null) B.Frame.enabled = false;
            B.Icon.enabled = false;
            _quest = UiKit.Pic(B.transform, "Quest", UiArt.Tex(UiContainers.QuestBorder), 0f, 0f, size, size + 1f);
            _quest.transform.SetSiblingIndex(1);
            _quest.enabled = false;
            B.Count = UiKit.Text(B.transform, "Count", "", 12f, Color.white, TextAlignmentOptions.BottomRight, 0f, size - 15f, size - 2f, 14f, UiArt.Numbers);
            B.Count.textWrappingMode = TextWrappingModes.NoWrap;
        }

        public ItemView Item { get { return _item; } }

        /// <summary>Adds the Stock number (merchant rows).</summary>
        public void WithStock()
        {
            if (_stock != null) return;
            _stock = UiKit.Text(B.transform, "Stock", "", 12f, UiArt.Gold, TextAlignmentOptions.TopLeft, 0f, 2f, _size, 14f, UiArt.Numbers);
            _stock.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary>SetItemButtonStock: "(%d)" (MERCHANT_STOCK, GlobalStrings.lua:4926) while a limited stock is above 0.</summary>
        public void SetStock(int stock)
        {
            if (_stock == null || stock == _stockShown) return;
            _stockShown = stock;
            if (stock > 0) _stock.SetText(UiNpcPopups.Fmt(UiGlobalStrings.G("MERCHANT_STOCK", "(%d)"), stock));
            else _stock.SetText("");
        }

        public void Show(bool on)
        {
            if (B.gameObject.activeSelf != on) B.gameObject.SetActive(on);
        }

        /// <summary>Shows an item (null = empty). count = the number drawn (shown above 1); tint = the icon colour when not locked.</summary>
        public void Set(ItemView it, int count, Color tint)
        {
            if (!ReferenceEquals(it, _item))
            {
                _item = it;
                _loaded = false;
                _path = null;
                B.Icon.enabled = false;
                _count = -1;
                if (B.Frame != null) B.Frame.enabled = it != null;
            }
            bool q = it != null && it.IsQuestItem;
            if (_quest.enabled != q) _quest.enabled = q;
            if (it == null)
            {
                if (_count != 0) { _count = 0; B.Count.SetText(""); }
                return;
            }
            if (!ReferenceEquals(it.IconPath, _path)) { _path = it.IconPath; _loaded = false; }
            if (!_loaded)
            {
                Texture2D t = UiArt.Icon(_path);
                if (t != null)
                {
                    B.Icon.texture = t;
                    UiKit.Coords(B.Icon, 0f, 1f, 0f, 1f);
                    B.Icon.enabled = true;
                    _loaded = true;
                }
            }
            int c = count > 1 ? count : 0;
            if (c != _count)
            {
                _count = c;
                if (c > 0) B.Count.SetText("{0}", c);
                else B.Count.SetText("");
            }
            Color want = it.Locked ? UiContainers.LockedTint : tint;
            if (B.Icon.color != want) B.Icon.color = want;
            if (B.Frame != null && B.Frame.color != tint) B.Frame.color = tint;   // SetItemButtonNormalTextureVertexColor (merchant rows)
        }

        /// <summary>A texture of our own in the icon spot (the backpack / an empty slot picture); null hides it.</summary>
        public void SetPicture(Texture2D t)
        {
            _item = null;
            _path = null;
            _loaded = false;
            B.Icon.texture = t;
            B.Icon.color = Color.white;
            UiKit.Coords(B.Icon, 0f, 1f, 0f, 1f);
            B.Icon.enabled = t != null;
            if (B.Frame != null) B.Frame.enabled = true;
            if (_quest.enabled) _quest.enabled = false;
            if (_count != 0) { _count = 0; B.Count.SetText(""); }
        }
    }

    /// <summary>
    /// One WoW ContainerFrame (ContainerFrameTemplate, ContainerFrame.xml:70-262, and ContainerFrame_GenerateFrame, ContainerFrame.lua:
    /// 371-530): the backpack picture or the UI-Bag-Components slices for a bag of any size (4 columns, 37x37 buttons 42 / 41 apart from
    /// the bottom right), the portrait (bag icon / KeyRing-Bag-Icon), the name, the close button; the backpack also shows the money and
    /// (ours, kept from iteration 10) the free slot count. Slot buttons are made on first need (MAX_CONTAINER_ITEMS 36).
    /// </summary>
    public sealed class UiContainerFrame
    {
        public const int MaxItems = 36;               // MAX_CONTAINER_ITEMS (ContainerFrame.lua:3)
        public const float Width = 192f;              // CONTAINER_WIDTH (ContainerFrame.lua:8)
        public const float BackpackHeight = 240f;     // BACKPACK_HEIGHT (ContainerFrame.lua:14)
        private const float RowHeight = 41f;          // ContainerFrame.lua:418
        private const int RowsInTexture = 6;          // ROWS_IN_BG_TEXTURE (ContainerFrame.lua:5)
        private const float TextureHeight = 512f;     // BG_TEXTURE_HEIGHT (ContainerFrame.lua:7)

        public readonly int Id;                       // 0 backpack, 1..4 bags, -2 keyring, 5..11 bank bags (ContainerFrame ids)
        public readonly RectTransform Root;
        public float W = Width, H = BackpackHeight;
        public int Size { get; private set; }

        private readonly UiContext _ctx;
        private readonly UiSurface _surface;
        private readonly bool _pointable, _over;
        private readonly RawImage _top, _bottom, _one, _portrait;
        private readonly RawImage[] _mid = new RawImage[2];
        private readonly TextMeshProUGUI _name;
        private readonly UiButton _close, _portraitButton;
        private readonly List<UiItemButton> _slots = new List<UiItemButton>();
        private readonly UiMoney _money;
        private readonly TextMeshProUGUI _free;
        private ContainerView _cv;
        private string _portraitPath;
        private bool _portraitLoaded;
        private int _freeShown = -1;
        private string _nameShown;

        public UiContainerFrame(Transform parent, UiContext ctx, int id, Action<int> toggle, UiSurface surface, bool pointable, bool over)
        {
            Id = id;
            _ctx = ctx;
            _surface = surface;
            _pointable = pointable;
            _over = over;
            Size = -1;
            Root = UiKit.Rect(parent, "ContainerFrame " + id, 0f, 0f, Width, BackpackHeight);
            if (pointable) UiKit.Blocker(Root);   // desktop: the frame takes the clicks on its art (enableMouse, ContainerFrame.xml:70)
            if (id == 0)
            {
                // ContainerFrame.lua:383-387: the backpack picture, 256 tall, TOPRIGHT of the 192 frame (the portrait sticks out left).
                _top = UiKit.Pic(Root, "BackgroundTop", UiArt.Tex(UiArt.BackpackBackground), -64f, 0f, 256f, 256f);
            }
            else
            {
                string suffix = id >= UiContainers.BankBagFirstId ? "-Bank" : id == UiContainers.KeyringId ? "-Keyring" : "";   // ContainerFrame.lua:401-406
                Texture2D t = UiArt.Tex(UiContainers.BagComponents + suffix + ".blp");
                _top = UiKit.Pic(Root, "BackgroundTop", t, -64f, 0f, 256f, 94f);
                _mid[0] = UiKit.Pic(Root, "BackgroundMiddle1", t, -64f, 94f, 256f, 10f);
                _mid[1] = UiKit.Pic(Root, "BackgroundMiddle2", t, -64f, 94f, 256f, 10f);
                _bottom = UiKit.Pic(Root, "BackgroundBottom", t, -64f, 94f, 256f, 10f);
                _one = UiKit.Pic(Root, "Background1Slot", UiArt.Tex(UiContainers.Bag1Slot), 0f, 0f, 99f, 74f);
                UiKit.Coords(_one, 0f, 0.7734375f, 0f, 0.578125f);   // ContainerFrame.xml:152
                _one.enabled = false;
                _portrait = UiKit.Pic(Root, "Portrait", null, 7f, 5f, 40f, 40f);   // ContainerFrame.xml:76-84
                _portrait.enabled = false;
            }
            _name = UiKit.Text(Root, "Name", "", 12f, Color.white, TextAlignmentOptions.Left, 47f, 8f, 112f, 16f);   // GameFontHighlight 112x12 at (47,-10)
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _portraitButton = UiKit.Button(Root, "PortraitButton", 7f, 5f, 40f, 40f);   // ContainerFrame.xml:202-236 (tooltip only)
            _portraitButton.Tip = PortraitTip;
            _close = UiKit.CloseButton(Root, Width - 32f, 1f, () => { if (toggle != null) toggle(Id); });   // TOPRIGHT (0,-1), ContainerFrame.xml:238-246
            if (id == 0)
            {
                _free = UiKit.Text(Root, "Free", "", 9f, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Left, 12f, 216f, 60f, 14f);   // ours (iteration 10)
                _money = new UiMoney(Root, 182f, 215f, Color.white);   // ContainerFrameMoneyFrame TOPRIGHT (-2,-216) (ContainerFrame.xml:157-163)
            }
        }

        private string PortraitTip()
        {
            if (Id == 0) return "<size=14>" + UiGlobalStrings.G("BACKPACK_TOOLTIP", "Backpack") + "</size>";   // GlobalStrings.lua:670
            if (Id == UiContainers.KeyringId) return "<size=14>" + UiGlobalStrings.G("KEYRING", "Keyring") + "</size>";   // GlobalStrings.lua:4565
            ItemView bag = _cv != null ? _cv.BagItem : null;
            return bag != null ? UiTooltip.Item(bag, null) : null;
        }

        public void Show(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        public bool Shown { get { return Root.gameObject.activeSelf; } }

        /// <summary>ContainerFrame_GenerateFrame for a size: the art slices, the height, the button places.</summary>
        private void Layout(int size)
        {
            Size = size;
            if (size > _slots.Count)
            {
                using (new UiBuildScope(_surface, _pointable, _over))
                {
                    for (int i = _slots.Count; i < size; i++)
                    {
                        int k = i;
                        var b = new UiItemButton(Root, "Item " + (i + 1), 0f, 0f, 37f);
                        UiContainers.Wire(b.B, _ctx, () => _cv != null ? _cv.Bag : -1, () => _cv != null ? _cv.SlotBase + k : -1);
                        _slots.Add(b);
                    }
                }
                _close.transform.SetAsLastSibling();
            }
            float firstX = Width - 12f - 37f;   // the first button's BOTTOMRIGHT at x -12 (ContainerFrame.lua:510-514)
            float bottomY;                      // the first button's top
            if (Id == 0)
            {
                W = Width;
                H = BackpackHeight;
                bottomY = 208f - 37f;           // backpack: BOTTOMRIGHT at the frame's TOPRIGHT (-12, -208) (ContainerFrame.lua:511)
            }
            else if (size == 1)
            {
                // the 1-slot bag (ContainerFrame.lua:425-433 / 476-487): 99 x 70, UI-Bag-1Slot, the button BOTTOMRIGHT (-10, 5)
                W = 99f;
                H = 70f;
                _one.enabled = true;
                _top.enabled = _bottom.enabled = _mid[0].enabled = _mid[1].enabled = false;
                firstX = W - 10f - 37f;
                bottomY = H - 5f - 37f;
            }
            else
            {
                W = Width;
                _one.enabled = false;
                _top.enabled = _bottom.enabled = true;
                int rows = (size + 3) / 4;
                int remaining = rows - 1;
                float topH;
                if (size % 4 == 2) { UiKit.Coords(_top, 0f, 1f, 0.189453125f, 0.330078125f); topH = 72f; }     // the bag with two slots on top (:440-443)
                else if (rows == 1) { UiKit.Coords(_top, 0f, 1f, 0.00390625f, 0.16796875f); topH = 86f; }       // one row (:446-448)
                else { UiKit.Coords(_top, 0f, 1f, 0.00390625f, 0.18359375f); topH = 94f; }                      // (:450-451)
                UiKit.Size(_top.rectTransform, 256f, topH);
                float y = topH, midH = 0f;
                const float FirstRowTexCoordOffset = 0.353515625f;   // :463
                const float FirstRowPixelOffset = 9f;                // :462
                for (int i = 0; i < _mid.Length; i++) _mid[i].enabled = false;
                if (rows > 1)
                {
                    int count = (remaining + RowsInTexture - 1) / RowsInTexture;
                    for (int i = 0; i < count && i < _mid.Length; i++)
                    {
                        float h;
                        if (remaining > RowsInTexture) { h = RowsInTexture * RowHeight + FirstRowTexCoordOffset; remaining -= RowsInTexture; }   // :466-472 (WoW's own sum)
                        else h = remaining * RowHeight - FirstRowPixelOffset;                                                                     // :474-479
                        RawImage m = _mid[i];
                        m.enabled = true;
                        UiKit.Coords(m, 0f, 1f, FirstRowTexCoordOffset, h / TextureHeight + FirstRowTexCoordOffset);
                        UiKit.Move(m.rectTransform, -64f, y);
                        UiKit.Size(m.rectTransform, 256f, h);
                        y += h;
                        midH += h;
                    }
                }
                UiKit.Coords(_bottom, 0f, 1f, 0.330078125f, 0.349609375f);   // ContainerFrame.xml:127
                UiKit.Move(_bottom.rectTransform, -64f, y);
                UiKit.Size(_bottom.rectTransform, 256f, 10f);
                H = topH + 10f + midH;                                         // :496
                bottomY = H - 9f - 37f;                                        // BOTTOMRIGHT (-12, 9) (:513)
            }
            for (int i = 0; i < _slots.Count; i++)
            {
                bool on = i < size;
                _slots[i].Show(on);
                if (!on) continue;
                int j = size - i;                       // button j shows slot size - j + 1 (ContainerFrame.lua:505-507)
                int col = (j - 1) % 4, row = (j - 1) / 4;
                UiKit.Move(_slots[i].B.Rt, firstX - col * 42f, bottomY - row * RowHeight);   // 5 left (37 + 5), 4 up (37 + 4) (:517-521)
            }
            UiKit.Size(Root, W, H);
            UiKit.Move(_close.Rt, W - 32f, 1f);
            if (_portrait != null) _portrait.enabled = size > 0 && _portraitLoaded;
        }

        /// <summary>Draws a container (null / size 0 = nothing). pickUps = UiContainers.SlotsPickUp for this frame.</summary>
        public void Refresh(ContainerView cv, bool pickUps)
        {
            _cv = cv;
            int size = cv != null ? Math.Min(cv.Size, MaxItems) : 0;
            if (size != Size) Layout(size);
            if (cv == null) return;
            // name (ContainerFrame.lua:492-500)
            string name = Id == 0 ? UiGlobalStrings.G("BACKPACK_TOOLTIP", "Backpack")
                : Id == UiContainers.KeyringId ? UiGlobalStrings.G("KEYRING", "Keyring")
                : size == 1 ? "" : cv.BagItem != null ? cv.BagItem.Name : "";
            if (!string.Equals(name, _nameShown)) { _nameShown = name; _name.SetText(name ?? ""); }
            // portrait (SetBagPortraitTexture: the bag's icon, round; the keyring's own icon)
            if (_portrait != null)
            {
                // lead fix 2026-10-11 (the user: "the bags are not showing on their icon displays"): the backpack shows Button-Backpack-Up
                string path = Id == UiContainers.KeyringId ? UiContainers.KeyRingIcon : Id == 0 ? UiArt.Backpack : cv.BagItem != null ? cv.BagItem.IconPath : null;
                if (!ReferenceEquals(path, _portraitPath)) { _portraitPath = path; _portraitLoaded = false; _portrait.enabled = false; }
                if (!_portraitLoaded && path != null)
                {
                    Texture2D t = Id == UiContainers.KeyringId || Id == 0 ? UiArt.Tex(path) : UiArt.Icon(path);
                    if (t != null)
                    {
                        bool round;
                        _portrait.texture = UiRoundPortrait.Of(t, out round);
                        _portrait.color = Color.white;   // lead fix 2026-10-11: UiKit.Pic made it without a texture - clear (0, 0, 0, 0), drawn invisible
                        _portrait.enabled = true;
                        _portraitLoaded = true;
                    }
                }
            }
            ItemView[] slots = cv.Slots;
            for (int i = 0; i < size; i++)
            {
                ItemView it = slots != null && i < slots.Length ? slots[i] : null;
                UiItemButton b = _slots[i];
                b.Set(it, it != null ? it.Count : 0, Color.white);
                b.B.ClickPicksUp = pickUps;
            }
            if (Id == 0)
            {
                PlayerState p = _ctx.P;
                if (p != null)
                {
                    _money.Set(p.Money);
                    int free = p.FreeBagSlots;
                    if (free != _freeShown) { _freeShown = free; _free.SetText("{0} free", free); }
                }
            }
        }
    }

    /// <summary>
    /// MainMenuBarBagButtons (MainMenuBarBagButtons.xml / .lua): the backpack button 37x37 BOTTOMRIGHT (-6, 2) of the bar art, the four
    /// bag slots 30x30 to its left (-5, then -4 apart), the keyring button 18x39 (-6) when keys exist. Click = open / close that container
    /// (Shift = all bags, BagSlotButton_OnModifiedClick); an item dropped on a button goes INTO that container (PutItemInBag /
    /// PutItemInBackpack / PutKeyInKeyRing); a bag dropped on an empty bag slot is equipped there; dragging a bag button picks the bag up.
    /// Built by uicore's UiMicroMenu (desktop) with the main menu bar art as parent.
    /// </summary>
    public sealed class UiBagBar
    {
        public const float W = 198f, H = 39f;    // 18 + 6 + 4 x 30 + 3 x 4 + 5 + 37 (MainMenuBarBagButtons.xml:77-175)
        private readonly UiContext _ctx;
        private readonly RectTransform _root;
        private readonly UiButton _backpack, _keyring;
        private readonly UiItemButton[] _bag = new UiItemButton[4];
        private readonly RawImage[] _empty = new RawImage[4];
        private readonly RawImage[] _checked = new RawImage[5];   // 0 backpack, 1..4 the bags (CheckedTexture CheckButtonHilight ADD, MainMenuBarBagButtons.xml:75 / :128)

        public UiBagBar(Transform parent, UiContext ctx)
        {
            _ctx = ctx;
            var go = new GameObject("MainMenuBarBagButtons", typeof(RectTransform));
            go.layer = UiKit.Layer;
            _root = (RectTransform)go.transform;
            _root.SetParent(parent, false);
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-6f, 2f);   // MainMenuBarBackpackButton BOTTOMRIGHT (-6, 2) (MainMenuBarBagButtons.xml:82-84)
            _root.sizeDelta = new Vector2(W, H);

            // the backpack (ItemButtonTemplate 37x37, Interface\Buttons\Button-Backpack-Up)
            var pack = new UiItemButton(_root, "MainMenuBarBackpackButton", 161f, 2f, 37f);
            pack.SetPicture(UiArt.Tex(UiArt.Backpack));
            _backpack = pack.B;
            _checked[0] = Checked(_backpack.transform, 37f);
            _backpack.Click = () => BagClick(0);
            _backpack.RightClick = () => BagClick(0);
            _backpack.Tip = () => "<size=14>" + UiGlobalStrings.G("BACKPACK_TOOLTIP", "Backpack") + "</size>";
            UiKit.DropTarget(_backpack, p => p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0, p => Store(p, InvPos.Bag0), p => "<color=#00ff00>Put it in the backpack.</color>");

            // CharacterBag0Slot..3: RIGHT -> LEFT (-5, 0) of the backpack, then (-4, 0) (MainMenuBarBagButtons.xml:130-164)
            float x = 161f - 5f - 30f;
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = new UiItemButton(_root, "CharacterBag" + i + "Slot", x, 5.5f, 30f);
                if (b.B.Frame != null) { UiKit.Size(b.B.Frame.rectTransform, 50f, 50f); UiKit.Move(b.B.Frame.rectTransform, -10f, -9f); }   // NormalTexture 50x50 (:36-37), CENTER (0,-1)
                _empty[i] = UiKit.Pic(b.B.transform, "Empty", UiArt.Tex(UiContainers.EmptyBagSlot), 0f, 0f, 30f, 30f);
                _empty[i].transform.SetSiblingIndex(0);
                _checked[1 + i] = Checked(b.B.transform, 30f);
                UiKit.Draggable(b.B, () => UiContainers.PayloadAt(_ctx, InvPos.Bag0, InvPos.BagSlotStart + k), false);   // BagSlotButton_OnDrag: PickupBagFromSlot
                UiKit.DropTarget(b.B, p => AcceptsBag(p, InvPos.BagSlotStart + k), p => DropOnBagSlot(p, InvPos.BagSlotStart + k), p => "<color=#00ff00>Put it in this bag.</color>");
                b.B.Click = () => BagClick(k + 1);
                b.B.RightClick = () => BagClick(k + 1);
                b.B.Tip = () => BagTip(k);
                _bag[i] = b;
                x -= 4f + 30f;
            }

            // KeyRingButton 18x39, RIGHT -> LEFT (-6, 0) of CharacterBag3Slot (MainMenuBarBagButtons.xml:166-219), shown when keys exist
            _keyring = UiKit.Button(_root, "KeyRingButton", 0f, 1f, 18f, 39f);
            _keyring.Art = UiKit.Pic(_keyring.transform, "Normal", UiArt.Tex(UiContainers.KeyRingButton + ".blp"), 0f, 0.5625f, 0f, 0.609375f, 0f, 0f, 18f, 39f);
            _keyring.Pushed = UiKit.Pic(_keyring.transform, "Pushed", UiArt.Tex(UiContainers.KeyRingButton + "-Down.blp"), 0f, 0.5625f, 0f, 0.609375f, 0f, 0f, 18f, 39f);
            _keyring.Pushed.enabled = false;
            _keyring.Hilite = UiKit.Pic(_keyring.transform, "Highlight", UiArt.Add(UiContainers.KeyRingButton + "-Highlight.blp"), 0f, 0.5625f, 0f, 0.609375f, 0f, 0f, 18f, 39f);
            _keyring.Hilite.enabled = false;
            _keyring.Click = () => BagClick(UiContainers.KeyringId);
            _keyring.RightClick = () => BagClick(UiContainers.KeyringId);
            _keyring.Tip = () => "<size=14>" + UiGlobalStrings.G("KEYRING", "Keyring") + "</size>";   // MainMenuBarBagButtons.xml OnEnter
            UiKit.DropTarget(_keyring, p => p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0, PutKey, p => "<color=#00ff00>Put it on the keyring.</color>");
            _keyring.gameObject.SetActive(false);
        }

        private static RawImage Checked(Transform parent, float size)
        {
            RawImage r = UiKit.Pic(parent, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, size, size);
            r.enabled = false;
            return r;
        }

        private void BagClick(int id)
        {
            UiBags bags = UiBags.Current;
            if (bags == null) return;
            if (id != UiContainers.KeyringId && UiContainers.ShiftHeld(_ctx)) { bags.OpenAllBags(); return; }   // IsModifiedClick("OPENALLBAGS") (:26-31)
            bags.ToggleBag(id);
        }

        private string BagTip(int i)
        {
            PlayerState p = _ctx.P;
            ItemView bag = UiContainers.ItemAt(p, InvPos.Bag0, InvPos.BagSlotStart + i);
            if (bag != null) return UiTooltip.Item(bag, null);
            return "<size=14>" + UiGlobalStrings.G("BAGSLOT", "Bag") + "</size>";   // GlobalStrings.lua:672
        }

        private void Store(UiPickupPayload p, int dstBag)
        {
            IWowSession s = _ctx.S;
            if (s == null || p == null || p.Bag < 0) return;
            s.StoreItemInBag(p.Bag, p.Slot, dstBag);
        }

        private bool AcceptsBag(UiPickupPayload p, int invSlot)
        {
            return p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0 && !(p.Bag == InvPos.Bag0 && p.Slot == invSlot);
        }

        /// <summary>PutItemInBag (BagSlotButton_OnClick): a bag onto an empty bag slot (or another bag: a swap) is equipped there; any other
        /// item goes into that bag.</summary>
        private void DropOnBagSlot(UiPickupPayload p, int invSlot)
        {
            IWowSession s = _ctx.S;
            PlayerState pl = _ctx.P;
            if (s == null || pl == null || p == null || p.Bag < 0) return;
            ItemView held = UiContainers.ItemAt(pl, p.Bag, p.Slot);
            ItemView there = UiContainers.ItemAt(pl, InvPos.Bag0, invSlot);
            bool heldIsBag = held != null && held.ContainerSlots > 0;
            if (there == null || heldIsBag) s.MoveItem(p.Bag, p.Slot, InvPos.Bag0, invSlot);
            else s.StoreItemInBag(p.Bag, p.Slot, invSlot);
        }

        /// <summary>PutKeyInKeyRing: the first free keyring slot.</summary>
        private void PutKey(UiPickupPayload p)
        {
            IWowSession s = _ctx.S;
            if (s == null || p == null || p.Bag < 0) return;
            int free = UiContainers.FreeKeyringSlot(s.Player);
            if (free < 0) { UiContainers.Note("the keyring has no free slot (or there is no keyring)."); return; }
            s.MoveItem(p.Bag, p.Slot, InvPos.Bag0, free);
        }

        public void Refresh(PlayerState p, float now)
        {
            if (p == null) return;
            UiBags bags = UiBags.Current;
            for (int i = 0; i < 4; i++)
            {
                ItemView bag = UiContainers.ItemAt(p, InvPos.Bag0, InvPos.BagSlotStart + i);
                _bag[i].Set(bag, 0, Color.white);
                if (_empty[i].enabled != (bag == null)) _empty[i].enabled = bag == null;
                bool open = bags != null && bags.IsBagShown(i + 1);
                if (_checked[1 + i].enabled != open) _checked[1 + i].enabled = open;
            }
            bool packOpen = bags != null && bags.IsBagShown(0);
            if (_checked[0].enabled != packOpen) _checked[0].enabled = packOpen;
            bool keys = false;
            ContainerView k = p.Keyring;
            if (k != null && k.Slots != null) for (int i = 0; i < k.Slots.Length && !keys; i++) keys = k.Slots[i] != null;
            if (_keyring.gameObject.activeSelf != keys) _keyring.gameObject.SetActive(keys);
        }
    }
}
