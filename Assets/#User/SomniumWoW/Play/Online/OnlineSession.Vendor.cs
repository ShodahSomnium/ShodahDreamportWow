// iteration 11 - owner npc (proposal-npc.md 3.4 / 6.4): the merchant - SMSG_LIST_INVENTORY / SMSG_BUY_ITEM / SMSG_BUY_FAILED / SMSG_SELL_ITEM,
// CurrentMerchant, BuyItem (CMSG_BUY_ITEM / CMSG_BUY_ITEM_IN_SLOT), SellItem, BuybackItem, RepairItem (+ the client-side Repair All estimate
// from DurabilityCosts.dbc / DurabilityQuality.dbc), ItemExtendedCost.dbc, the walk-away close. AzerothCore master quoted as AC <file>:<line>.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        // ---- AzerothCore values
        const int VendMaxPurchases = 255;                // Player::BuyItemFromVendorSlot takes the count as uint8 (Player.cpp:10887)
        const int VendBuybackStart = 74, VendBuybackSlots = 12;   // BUYBACK_SLOT_START / END (Player.h:715-716)
        const int VendBagSlotLimit = 36;                 // `if (slot > MAX_BAG_SIZE && slot != NULL_SLOT) return false;` (Player.cpp:10898, MAX_BAG_SIZE Bag.h:22)
        // DBC layouts measured on the real 3.3.5a files (proposal-npc.md 3.10, harness NpcRowById)
        const int ExtCostFieldCount = 16, ExtCostHonor = 1, ExtCostArena = 2, ExtCostItem0 = 4, ExtCostCount0 = 9, ExtCostRating = 14;
        const int DurCostFieldCount = 30, DurQualityFieldCount = 2;

        // ---- ours (named here, logged once in InitVendor)
        const double VendWalkAwaySeconds = 1.0;          // out of interaction reach this long closes the merchant
        const double VendCheckSpacing = 0.25;            // the reach / repair estimate is checked 4x a second
        const double VendMoneyWatchSeconds = 5.0;        // a sale / repair whose coinage change does not come within this is forgotten

        MerchantView _merchant;
        double _vendAwaySince = -1, _vendNextCheck;
        bool _vendDbcTried;
        DbcTable _extCostDbc, _durCostDbc, _durQualityDbc;
        int _vendMoneySeen = -1;
        // a sale or repair waiting for its money (a sale's success sends no packet: the item leaves, the coinage grows, ItemHandler.cpp:637-725)
        sealed class VendPending { public ulong Item; public int Entry, Count; public string Name; public int MoneyBefore; public double Until; public bool Repair; }
        readonly List<VendPending> _vendPending = new List<VendPending>();
        int _vendBought, _vendSold;

        partial void InitVendor()
        {
            if (_world == null) return;
            _world.On(Op.SMSG_LIST_INVENTORY, OnVendList, this);
            _world.On(Op.SMSG_BUY_ITEM, OnVendBought, this);
            _world.On(Op.SMSG_BUY_FAILED, OnVendBuyFailed, this);
            _world.On(Op.SMSG_SELL_ITEM, OnVendSellFailed, this);
            WowLog.Info("Online vendor: ours - the merchant closes " + VendWalkAwaySeconds + " s out of reach (checked every " + VendCheckSpacing +
                        " s), a sale / repair waits " + VendMoneyWatchSeconds + " s for its money; the repair cost shown is the client estimate without the reputation discount");
        }

        public MerchantView CurrentMerchant { get { return _merchant; } }

        public void CloseMerchant() { VendClose("closed by the player"); }   // WoW's CloseMerchant sends nothing

        void VendClose(string why)
        {
            if (_merchant == null) return;
            WowLog.Info("Online vendor: the merchant " + (_merchant.VendorName ?? _merchant.VendorGuid.ToString("X16")) + " closes - " + why);
            _merchant = null;
            _vendAwaySince = -1;
        }

        // ------------------------------------------------------------------ server -> client

        /// <summary>
        /// SMSG_LIST_INVENTORY (WorldSession::SendListInventory, ItemHandler.cpp:854-965): uint64 vendor, uint8 count; count 0 -> one more
        /// uint8 error (0 = "Vendor has no inventory", :886-889); else count x { uint32 slot + 1 (:931 "client expects counting to start at 1"),
        /// uint32 item, uint32 displayId, int32 leftInStock (0xFFFFFFFF unlimited, :919), uint32 price (reputation discount applied), uint32
        /// maxDurability, uint32 buyCount, uint32 extendedCost }. Slots are NOT contiguous (AC skips other-class / other-faction / sold-out
        /// rows, :910-925) - always echo the slot.
        /// </summary>
        void OnVendList(WowPacket p)
        {
            if (p.Remaining < 9) return;
            ulong vendor = p.U64();
            int n = p.U8();
            int err = n == 0 && p.Remaining > 0 ? p.U8() : -1;
            if (!NpcAnswerFor(vendor) && !(_merchant != null && _merchant.VendorGuid == vendor))
            {
                WowLog.Info("Online vendor: SMSG_LIST_INVENTORY of " + vendor.ToString("X16") + " not asked for by us - ignored");
                return;
            }
            var ve = Find(vendor);
            var mv = new MerchantView { VendorGuid = vendor, VendorName = ve != null ? ve.Name : null, Empty = n == 0 };
            int firstSlot = 0, lastSlot = 0;
            for (int i = 0; i < n && p.Remaining >= 32; i++)
            {
                var it = new MerchantItemView { VendorSlot = (int)p.U32() };
                int entry = (int)p.U32();
                int display = (int)p.U32();
                it.Stock = p.I32();                                                   // -1 = unlimited
                if (it.Stock < -1) it.Stock = -1;
                it.Price = Math.Max(0, p.I32());
                it.MaxDurability = Math.Max(0, p.I32());
                it.BuyCount = Math.Max(1, p.I32());
                it.ExtendedCostId = Math.Max(0, p.I32());
                it.Item = ItemInfoView(entry, it.BuyCount);                           // the item answer fills the name / tooltip in place
                if (it.Item.DisplayId <= 0 && display > 0)
                {
                    it.Item.DisplayId = display;
                    string icon = IconOf(display);
                    if (icon != null) it.Item.IconPath = icon;
                }
                VendFillExtendedCost(it);
                mv.Items.Add(it);
                if (i == 0) firstSlot = it.VendorSlot;
                lastSlot = it.VendorSlot;
            }
            OnlineObject o;
            mv.CanRepair = _objects.TryGetValue(vendor, out o) && (o.U32(UF.UNIT_NPC_FLAGS) & NfRepair) != 0;
            NpcServiceOpened(vendor, "merchant");
            bool same = _merchant != null && _merchant.VendorGuid == vendor;
            mv.Version = same ? _merchant.Version + 1 : 1;
            _merchant = mv;
            _vendAwaySince = -1;
            _vendNextCheck = 0;
            VendRefreshRows();
            mv.RepairAllCost = mv.CanRepair ? VendRepairAllCost() : 0;
            WowLog.Info("Online vendor: SMSG_LIST_INVENTORY from " + (mv.VendorName ?? vendor.ToString("X16")) + ": " + mv.Items.Count + " items" +
                        (mv.Items.Count > 0 ? " (slots " + firstSlot + ".." + lastSlot + ")" : "") + ", repair " + (mv.CanRepair ? "yes" : "no") +
                        (n == 0 ? " - the vendor has no inventory (code " + err + ")" : ""));
        }

        /// <summary>SMSG_BUY_ITEM (Player::_StoreOrEquipNewItem, Player.cpp:10859-10864): uint64 vendor, uint32 vendorSlot + 1, int32 newCount
        /// (0xFFFFFFFF = unlimited), uint32 count (purchases). The item itself arrives with SMSG_ITEM_PUSH_RESULT (the LootReceived line).</summary>
        void OnVendBought(WowPacket p)
        {
            if (p.Remaining < 20) return;
            ulong vendor = p.U64();
            int slot = (int)p.U32();
            int newCount = p.I32();
            int count = (int)p.U32();
            var mv = _merchant;
            MerchantItemView row = null;
            if (mv != null && mv.VendorGuid == vendor) row = mv.Items.Find(x => x.VendorSlot == slot);
            if (row == null) { WowLog.Info("Online vendor: SMSG_BUY_ITEM slot " + slot + " of " + vendor.ToString("X16") + " (no open merchant row) - " + count + " bought"); return; }
            row.Stock = newCount < 0 ? -1 : newCount;
            mv.Version++;
            _vendBought++;
            int entry = row.Item != null ? row.Item.Entry : 0;
            string name = row.Item != null ? row.Item.Name : "item";
            _events.Enqueue(new GameEvent { Type = GameEventType.ItemBought, ItemEntry = entry, Amount = count * row.BuyCount, Text = name, SourceGuid = vendor });
            WowLog.Info("Online vendor: bought " + count + " x " + row.BuyCount + " '" + name + "' (slot " + slot + ", left " + (row.Stock < 0 ? "unlimited" : row.Stock.ToString()) + ")");
        }

        /// <summary>SMSG_BUY_FAILED (Player::SendBuyError, PlayerStorage.cpp:4174-4184): uint64 vendor (0 when unknown), uint32 item, [uint32 param
        /// only when > 0], uint8 BuyResult. Always shown - also with vendor 0: a gossip option's box money you lack comes this way (PlayerGossip.cpp:285-291).</summary>
        void OnVendBuyFailed(WowPacket p)
        {
            if (p.Remaining < 13) return;
            ulong vendor = p.U64();
            int item = (int)p.U32();
            if (p.Remaining >= 5) p.U32();
            int code = p.U8();
            string text = BuyResultText(code);
            WowLog.Info("Online vendor: SMSG_BUY_FAILED " + code + " for item " + item + " from " + vendor.ToString("X16") + (text != null ? " \"" + text + "\"" : " (no text)"));
            if (text != null) Error(text);
        }

        /// <summary>SMSG_SELL_ITEM = the sell ERROR (Player::SendSellError, PlayerStorage.cpp:4186-4196): uint64 vendor, uint64 item, [uint32 param
        /// when > 0], uint8 SellResult (Item.h:153-164).</summary>
        void OnVendSellFailed(WowPacket p)
        {
            if (p.Remaining < 17) return;
            ulong vendor = p.U64();
            ulong item = p.U64();
            if (p.Remaining >= 5) p.U32();
            int code = p.U8();
            InvUnlock(item);
            _vendPending.RemoveAll(x => !x.Repair && x.Item == item);
            string text = SellResultText(code);
            WowLog.Info("Online vendor: SMSG_SELL_ITEM error " + code + " for " + item.ToString("X16") + (text != null ? " \"" + text + "\"" : " (no text)"));
            if (text != null) Error(text);
        }

        /// <summary>BuyResult (Item.h:139-150) -> the client's GlobalStrings.lua text of the same meaning.</summary>
        static string BuyResultText(int code)
        {
            switch (code)
            {
                case 0: return "The item was not found.";                                // BUY_ERR_CANT_FIND_ITEM -> ERR_ITEM_NOT_FOUND (3169)
                case 1: case 7: return "That item is currently sold out.";               // ITEM_ALREADY_SOLD / ITEM_SOLD_OUT -> ERR_VENDOR_SOLD_OUT (3584)
                case 2: return "You don't have enough money.";                           // NOT_ENOUGHT_MONEY -> ERR_NOT_ENOUGH_MONEY (3307)
                case 4: return "That merchant doesn't like you.";                        // SELLER_DONT_LIKE_YOU -> ERR_VENDOR_HATES_YOU (3581)
                case 5: return "You are too far away.";                                  // DISTANCE_TOO_FAR -> ERR_VENDOR_TOO_FAR (3585)
                case 8: return "You can't carry any more of those items.";               // CANT_CARRY_MORE -> ERR_ITEM_MAX_COUNT (3163)
                case 11: return "You don't have the required rank for that item";        // RANK_REQUIRE -> ERR_CANT_EQUIP_RANK (2983)
                case 12: return "You don't have the required reputation for that item";  // REPUTATION_REQUIRE -> ERR_CANT_EQUIP_REPUTATION (2985)
            }
            return null;
        }

        /// <summary>SellResult (Item.h:153-164, AC's comments are the client texts) -> GlobalStrings.lua.</summary>
        static string SellResultText(int code)
        {
            switch (code)
            {
                case 1: return "The item was not found.";                                // ERR_ITEM_NOT_FOUND (3169)
                case 2: return "The merchant doesn't want that item.";                   // ERR_VENDOR_NOT_INTERESTED (3583)
                case 3: return "That merchant doesn't like you.";                        // ERR_VENDOR_HATES_YOU (3581)
                case 4: return "You don't own that item.";                               // ERR_NOT_OWNER (3314)
                case 6: return "You can only do that with empty bags.";                  // ERR_DESTROY_NONEMPTY_BAG (3023)
                case 7: return "You cannot sell items to this merchant";                 // ERR_VENDOR_DOESNT_BUY (3580)
                case 8: return "You must repair that item's durability to use it.";      // ERR_MUST_REPAIR_DURABILITY (3277)
                case 9: return "Internal Bag Error";                                     // ERR_INTERNAL_BAG_ERROR (3135)
            }
            return null;                                                                  // 5 "Nothing appears..." has no client string
        }

        // ------------------------------------------------------------------ client -> server

        /// <summary>
        /// Buys `purchases` x Items[itemIndex].BuyCount (CMSG_BUY_ITEM: uint64 vendor, uint32 item, uint32 slot (1-based), uint32 count, uint8 0
        /// - ItemPackets.cpp BuyItem::Read; count = purchases, Player.cpp:10887-11050). A drop on a bag slot = CMSG_BUY_ITEM_IN_SLOT (uint64
        /// vendor, uint32 item, uint32 slot, uint64 bag guid - the PLAYER guid for the backpack, uint8 slot inside it, uint32 count,
        /// BuyItemInSlot::Read); AC refuses a target slot above 36 (Player.cpp:10898) - such a drop is sent as an "anywhere" purchase.
        /// </summary>
        public bool BuyItem(int itemIndex, int purchases, int bag, int slot)
        {
            var mv = _merchant;
            if (_world == null || mv == null || itemIndex < 0 || itemIndex >= mv.Items.Count) return false;
            var it = mv.Items[itemIndex];
            int n = Math.Max(1, Math.Min(purchases, VendMaxPurchases));
            if (it.Stock >= 0 && it.Stock < n * it.BuyCount) { Error("That item is currently sold out."); return false; }        // ERR_VENDOR_SOLD_OUT (3584), the server's check Player.cpp:10950-10957
            if (it.Price > 0 && (long)it.Price * n > _player.Money) { Error("You don't have enough money."); return false; }      // ERR_NOT_ENOUGH_MONEY (3307)
            if (it.HonorCost > 0 && (long)it.HonorCost * n > _player.HonorPoints) { Error("You don't have enough honor points"); return false; }   // ERR_NOT_ENOUGH_HONOR_POINTS (3306)
            if (it.ArenaCost > 0 && (long)it.ArenaCost * n > _player.ArenaPoints) { Error("You don't have enough arena points"); return false; }   // ERR_NOT_ENOUGH_ARENA_POINTS (3304)
            int entry = it.Item != null ? it.Item.Entry : 0;
            ulong bagGuid = 0;
            if (bag == InvPos.Bag0) bagGuid = _player.Guid;
            else if (bag >= InvPos.BagSlotStart && bag < InvPos.BagSlotEnd) bagGuid = GuidAt(InvPos.Bag0, bag);
            bool inSlot = bagGuid != 0 && slot >= 0 && slot <= VendBagSlotLimit && (bag != InvPos.Bag0 || (slot >= InvPos.PackStart && slot < InvPos.PackEnd));
            if (bag >= 0 && !inSlot) WowLog.Info("Online vendor: purchase dropped on bag " + bag + " slot " + slot + " - sent as an 'anywhere' purchase (AC refuses slots above " + VendBagSlotLimit + ")");
            if (inSlot) _world.Send(Op.CMSG_BUY_ITEM_IN_SLOT, new PacketWriter().U64(mv.VendorGuid).U32((uint)entry).U32((uint)it.VendorSlot).U64(bagGuid).U8(slot).U32((uint)n));
            else _world.Send(Op.CMSG_BUY_ITEM, new PacketWriter().U64(mv.VendorGuid).U32((uint)entry).U32((uint)it.VendorSlot).U32((uint)n).U8(0));
            WowLog.Info("Online vendor: buy " + n + " x " + it.BuyCount + " '" + (it.Item != null ? it.Item.Name : "?") + "' (slot " + it.VendorSlot + ", " + (it.Price * n) + " copper" +
                        (inSlot ? ", into bag " + bag + " slot " + slot : "") + ")");
            return true;
        }

        /// <summary>CMSG_SELL_ITEM (SellItem::Read: uint64 vendor, uint64 item, uint32 count; 0 = the whole stack, ItemHandler.cpp:637-639). Success
        /// sends no packet (the item goes to a buyback slot, the coinage grows) - ItemSold is emitted when the money arrives.</summary>
        public void SellItem(int bag, int slot, int count)
        {
            var mv = _merchant;
            if (_world == null) return;
            if (mv == null) { WowLog.Info("Online vendor: sell without an open merchant - nothing sent"); return; }
            ulong g = GuidAt(bag, slot);
            OnlineObject o;
            if (g == 0 || !_objects.TryGetValue(g, out o)) { WowLog.Info("Online vendor: nothing to sell at bag " + bag + " slot " + slot); return; }
            int stack = Math.Max(1, o.I32(UF.ITEM_FIELD_STACK_COUNT));
            uint cnt = count <= 0 || count >= stack ? 0u : (uint)count;
            _world.Send(Op.CMSG_SELL_ITEM, new PacketWriter().U64(mv.VendorGuid).U64(g).U32(cnt));
            InvLock(g);
            string name = ItemInfoName(o.Entry) ?? ("Item " + o.Entry);
            _vendPending.Add(new VendPending { Item = g, Entry = o.Entry, Count = cnt == 0 ? stack : (int)cnt, Name = name, MoneyBefore = _player.Money, Until = _now + VendMoneyWatchSeconds });
            WowLog.Info("Online vendor: sell '" + name + "' x" + (cnt == 0 ? stack : (int)cnt) + " from bag " + bag + " slot " + slot + " to " + (mv.VendorName ?? "the merchant"));
        }

        /// <summary>CMSG_BUYBACK_ITEM (BuybackItem::Read: uint64 vendor, uint32 slot = BUYBACK_SLOT_START 74 + index; ItemHandler.cpp:748-796,
        /// price PLAYER_FIELD_BUYBACK_PRICE_1 + index).</summary>
        public void BuybackItem(int index)
        {
            var mv = _merchant;
            if (_world == null || mv == null || index < 0 || index >= VendBuybackSlots) return;
            var v = _player.Buyback[index];
            if (v == null) return;
            int price = _player.BuybackPrice[index];
            if (price > _player.Money) { Error("You don't have enough money."); return; }                                         // ERR_NOT_ENOUGH_MONEY (3307)
            _world.Send(Op.CMSG_BUYBACK_ITEM, new PacketWriter().U64(mv.VendorGuid).U32((uint)(VendBuybackStart + index)));
            WowLog.Info("Online vendor: buy back '" + v.Name + "' x" + v.Count + " (buyback slot " + (VendBuybackStart + index) + ", " + price + " copper)");
        }

        /// <summary>CMSG_REPAIR_ITEM (NPCHandler.cpp:759-797: uint64 npc, uint64 item (0 = all), uint8 guildBank); needs UNIT_NPC_FLAG_REPAIR.
        /// Repair All = DurabilityRepairAll (Player.cpp:4907-4921: equipment, backpack, bag items, the bags - not bank / buyback / keys).</summary>
        public void RepairItem(int bag, int slot)
        {
            var mv = _merchant;
            if (_world == null) return;
            if (mv == null || !mv.CanRepair) { WowLog.Info("Online vendor: repair without an armorer open - nothing sent"); return; }
            ulong g = bag < 0 ? 0UL : GuidAt(bag, slot);
            if (bag >= 0 && g == 0) return;
            int entry = 0;
            OnlineObject o;
            if (g != 0 && _objects.TryGetValue(g, out o)) entry = o.Entry;
            _world.Send(Op.CMSG_REPAIR_ITEM, new PacketWriter().U64(mv.VendorGuid).U64(g).U8(0));
            _vendPending.Add(new VendPending { Item = g, Entry = entry, Repair = true, MoneyBefore = _player.Money, Until = _now + VendMoneyWatchSeconds, Name = g == 0 ? "all" : ItemInfoName(entry) });
            WowLog.Info("Online vendor: repair " + (g == 0 ? "all (estimate " + mv.RepairAllCost + " copper)" : "'" + (ItemInfoName(entry) ?? "?") + "' (estimate " + VendRepairCost(g) + " copper)"));
        }

        // ------------------------------------------------------------------ per frame

        partial void TickVendor(float dt)
        {
            if (_world == null) return;
            VendWatchMoney();
            var mv = _merchant;
            if (mv == null || _now < _vendNextCheck) return;
            _vendNextCheck = _now + VendCheckSpacing;
            if (_dialog != null) { VendClose("a dialog opened (it replaces the merchant panel)"); return; }
            var e = Find(mv.VendorGuid);
            var o = FindObject(mv.VendorGuid);
            bool near = e != null && o != null && !e.Dead && !_player.Dead && QInReach(o, e, QInteractionDistance);
            if (near) _vendAwaySince = -1;
            else if (_vendAwaySince < 0) _vendAwaySince = _now;
            else if (_now - _vendAwaySince >= VendWalkAwaySeconds) { VendClose(e == null ? "the vendor is gone" : _player.Dead ? "you died" : "out of reach"); return; }
            bool changed = VendRefreshRows();
            int cost = mv.CanRepair ? VendRepairAllCost() : 0;
            if (cost != mv.RepairAllCost) { mv.RepairAllCost = cost; changed = true; }
            if (changed) mv.Version++;
        }

        /// <summary>A sale shows up as more money, a repair as less (no success packet for either).</summary>
        void VendWatchMoney()
        {
            if (_vendPending.Count == 0) { _vendMoneySeen = _player.Money; return; }
            int money = _player.Money;
            for (int i = 0; i < _vendPending.Count; i++)
            {
                var v = _vendPending[i];
                int delta = money - v.MoneyBefore;
                bool done = v.Repair ? delta < 0 : delta > 0;
                if (done)
                {
                    if (v.Repair)
                    {
                        _events.Enqueue(new GameEvent { Type = GameEventType.ItemRepaired, ItemEntry = v.Entry, Amount = -delta });
                        WowLog.Info("Online vendor: repaired " + (v.Item == 0 ? "everything" : "'" + (v.Name ?? "?") + "'") + " for " + (-delta) + " copper");
                    }
                    else
                    {
                        _vendSold++;
                        _events.Enqueue(new GameEvent { Type = GameEventType.ItemSold, ItemEntry = v.Entry, Amount = delta, Text = v.Name });
                        WowLog.Info("Online vendor: sold '" + v.Name + "' x" + v.Count + " for " + delta + " copper");
                    }
                    _vendPending.RemoveAt(i);
                    for (int k = 0; k < _vendPending.Count; k++) _vendPending[k].MoneyBefore = money;   // the next one counts from here
                    i = -1;
                    continue;
                }
                if (_now > v.Until)
                {
                    WowLog.Info("Online vendor: no money change within " + VendMoneyWatchSeconds + " s after the " + (v.Repair ? "repair" : "sale of '" + v.Name + "'") + " - forgotten");
                    if (!v.Repair) InvUnlock(v.Item);
                    _vendPending.RemoveAt(i);
                    i--;
                }
            }
            _vendMoneySeen = money;
        }

        /// <summary>Usable (red tint, MerchantFrame.lua:139-144) and the quantity picker's maximum (MerchantItemButton_OnModifiedClick,
        /// MerchantFrame.lua:412-430: the stack size, at most what the money buys) of every row whose item answer is in. True = changed.
        /// Usable = AllowableClass / AllowableRace / RequiredLevel (AC Player::CanUseItem's first checks); proficiency is not judged.</summary>
        bool VendRefreshRows()
        {
            var mv = _merchant;
            if (mv == null) return false;
            bool changed = false;
            int classMask = (int)_player.Class > 0 ? 1 << ((int)_player.Class - 1) : 0;
            int raceMask = _player.Race > 0 ? 1 << (_player.Race - 1) : 0;
            foreach (var it in mv.Items)
            {
                ItemProto d;
                bool usable = true;
                int perStack = 1;
                if (it.Item != null && _itemProtos.TryGetValue(it.Item.Entry, out d) && !d.Missing)
                {
                    usable = (classMask == 0 || (d.AllowableClass & classMask) != 0) && (raceMask == 0 || (d.AllowableRace & raceMask) != 0) && d.RequiredLevel <= Math.Max(1, _player.Level);
                    perStack = Math.Max(1, d.Stackable);
                }
                long max = perStack > 1 ? Math.Max(1, perStack / it.BuyCount) : 1;
                if (it.Price > 0) max = Math.Min(max, _player.Money / it.Price);
                if (it.HonorCost > 0) max = Math.Min(max, _player.HonorPoints / it.HonorCost);
                if (it.ArenaCost > 0) max = Math.Min(max, _player.ArenaPoints / it.ArenaCost);
                if (it.Stock >= 0) max = Math.Min(max, it.Stock / it.BuyCount);
                int m = (int)Math.Max(1, Math.Min(VendMaxPurchases, max));
                if (it.Usable != usable || it.MaxPurchases != m) { it.Usable = usable; it.MaxPurchases = m; changed = true; }
            }
            return changed;
        }

        // ------------------------------------------------------------------ DBC helpers

        void VendLoadDbcs()
        {
            if (_vendDbcTried || _files == null) return;
            _vendDbcTried = true;
            _extCostDbc = Dbc.Load(_files, "ItemExtendedCost");
            if (_extCostDbc != null && _extCostDbc.FieldCount != ExtCostFieldCount) { WowLog.Warn("Online vendor: ItemExtendedCost.dbc has " + _extCostDbc.FieldCount + " fields, not " + ExtCostFieldCount + " - extended costs not shown"); _extCostDbc = null; }
            _durCostDbc = Dbc.Load(_files, "DurabilityCosts");
            if (_durCostDbc != null && _durCostDbc.FieldCount != DurCostFieldCount) { WowLog.Warn("Online vendor: DurabilityCosts.dbc has " + _durCostDbc.FieldCount + " fields - no repair estimate"); _durCostDbc = null; }
            _durQualityDbc = Dbc.Load(_files, "DurabilityQuality");
            if (_durQualityDbc != null && _durQualityDbc.FieldCount != DurQualityFieldCount) { WowLog.Warn("Online vendor: DurabilityQuality.dbc has " + _durQualityDbc.FieldCount + " fields - no repair estimate"); _durQualityDbc = null; }
            WowLog.Info("Online vendor: ItemExtendedCost " + (_extCostDbc != null ? _extCostDbc.RecordCount + " rows" : "missing") + ", DurabilityCosts " +
                        (_durCostDbc != null ? _durCostDbc.RecordCount + " rows" : "missing") + ", DurabilityQuality " + (_durQualityDbc != null ? _durQualityDbc.RecordCount + " rows" : "missing"));
        }

        /// <summary>ItemExtendedCost.dbc (16 fields: 1 HonorPoints, 2 ArenaPoints, 4-8 ItemID[5], 9-13 ItemCount[5], 14 RequiredArenaRating) =
        /// AC ItemExtendedCostEntry reqhonorpoints / reqarenapoints / reqitem / reqitemcount / reqpersonalarenarating.</summary>
        void VendFillExtendedCost(MerchantItemView it)
        {
            if (it.ExtendedCostId <= 0) return;
            VendLoadDbcs();
            if (_extCostDbc == null) return;
            int r = _extCostDbc.FindRow(it.ExtendedCostId);
            if (r < 0) return;
            it.HonorCost = Math.Max(0, _extCostDbc.GetInt(r, ExtCostHonor));
            it.ArenaCost = Math.Max(0, _extCostDbc.GetInt(r, ExtCostArena));
            var ids = new List<int>(); var counts = new List<int>();
            for (int k = 0; k < 5; k++)
            {
                int id = _extCostDbc.GetInt(r, ExtCostItem0 + k), c = _extCostDbc.GetInt(r, ExtCostCount0 + k);
                if (id > 0 && c > 0) { ids.Add(id); counts.Add(c); AskItem(id); }
            }
            it.CostItems = ids.ToArray();
            it.CostCounts = counts.ToArray();
            it.RequiredArenaRating = Math.Max(0, _extCostDbc.GetInt(r, ExtCostRating));
        }

        /// <summary>Player::DurabilityRepairAll positions (Player.cpp:4907-4921): bag 255 slots 0..38 (equipment, the bags, the backpack) and
        /// the items inside the bags 19..22.</summary>
        int VendRepairAllCost()
        {
            var me = _self;
            if (me == null) return 0;
            long sum = 0;
            for (int s = 0; s < InvPos.PackEnd; s++) sum += VendRepairCost(me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + s * 2));
            for (int b = InvPos.BagSlotStart; b < InvPos.BagSlotEnd; b++)
            {
                OnlineObject bag;
                ulong bg = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + b * 2);
                if (bg == 0 || !_objects.TryGetValue(bg, out bag) || bag.TypeId != TypeId.Container) continue;
                int n = Math.Min(MaxBagSize, Math.Max(0, bag.I32(UF.CONTAINER_FIELD_NUM_SLOTS)));
                for (int i = 0; i < n; i++) sum += VendRepairCost(bag.U64(UF.CONTAINER_FIELD_SLOT_1 + i * 2));
            }
            return (int)Math.Min(int.MaxValue, sum);
        }

        /// <summary>Player::DurabilityRepair (Player.cpp:4923-4990) without the reputation discount and RATE_REPAIRCOST: lost = max - cur;
        /// DurabilityCosts.dbc row ItemLevel, multiplier[ItemSubClassToDurabilityMultiplierId] (weapon -> subclass, armor -> subclass + 21,
        /// else 0; ItemTemplate.h:557-568); DurabilityQuality.dbc row (Quality + 1) * 2; cost = uint32(lost * mult * qualityMod), 0 -> 1.</summary>
        int VendRepairCost(ulong g)
        {
            OnlineObject o;
            if (g == 0 || !_objects.TryGetValue(g, out o)) return 0;
            int max = o.I32(UF.ITEM_FIELD_MAXDURABILITY), cur = o.I32(UF.ITEM_FIELD_DURABILITY);
            if (max <= 0 || cur >= max) return 0;
            ItemProto d;
            if (!_itemProtos.TryGetValue(o.Entry, out d) || d.Missing) return 0;
            VendLoadDbcs();
            if (_durCostDbc == null || _durQualityDbc == null) return 0;
            int r = _durCostDbc.FindRow(d.ItemLevel);
            if (r < 0) return 0;
            int col = d.Class == ItemClassWeapon ? d.SubClass : d.Class == ItemClassArmor ? d.SubClass + 21 : 0;
            if (col < 0 || 1 + col >= _durCostDbc.FieldCount) return 0;
            uint mult = _durCostDbc.GetUInt(r, 1 + col);
            int qr = _durQualityDbc.FindRow((d.Quality + 1) * 2);
            if (qr < 0) return 0;
            double qmod = _durQualityDbc.GetFloat(qr, 1);
            uint cost = (uint)((uint)(max - cur) * mult * qmod);
            return cost == 0 ? 1 : (int)Math.Min(int.MaxValue, cost);
        }
    }
}
