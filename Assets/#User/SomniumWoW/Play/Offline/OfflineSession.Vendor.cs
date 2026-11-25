// iteration 11 - owner npc (proposal-npc.md s7): the offline merchant (OUR simulation - which NPC sells, what and for how much is ours,
// Play/Content/OfflineVendors.cs): the vendor option in the NPC's dialog, the lone-option auto-select (GossipFrame.lua:10-17), buying,
// selling, a 12-entry buyback list (in memory, like AzerothCore's per-session buyback), and the answers of the members that have nothing
// offline (no armorer, bank, innkeeper or readable item exists in the offline zones - README gap G8). The item positions (MoveItem ...)
// are in OfflineSession.Items.cs.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        // ---- ours (logged once with the zone's stock)
        private const float OffVendorCloseYards = 10f;    // the offline dialog's walk-away rule (OfflineSession.Stats.cs "10 yd, like the client")
        private const int OffBuybackSlots = 12;           // as WoW: BUYBACK_SLOT_START 74 .. END 86 (AC Player.h:711-712)
        private const int OffPurchaseLimit = 255;         // as online: AC takes the purchase count as uint8

        private MerchantView _merchant;
        private OfflineCreature _merchantNpc;
        private NpcDialog _offOptDialog;                  // the last dialog the options pass saw
        private ulong _offLastDialogNpc;                  // the NPC whose dialog was open in the previous frame (a refresh is not a new conversation)
        private sealed class OffBuyback { public int Entry, Count, Price; public long Stamp; }
        private readonly OffBuyback[] _offBuyback = new OffBuyback[OffBuybackSlots];
        private long _offBuybackStamp;
        private int _offMoneySeen = -1;
        private bool _offVendorLogged;
        private readonly HashSet<string> _offNothingLogged = new HashSet<string>();

        // ------------------------------------------------------------------ dialog options (called every frame from UpdateViews)

        private void OffVendorTick()
        {
            var d = _dialog;
            if (d != null && !ReferenceEquals(d, _offOptDialog))
            {
                _offOptDialog = d;
                bool fresh = _offLastDialogNpc != d.NpcGuid;
                OffDialogOptions(_dialogNpc, d, fresh);
            }
            if (_dialog == null) _offOptDialog = null;
            _offLastDialogNpc = _dialog != null ? _dialog.NpcGuid : 0;
            var mv = _merchant;
            if (mv == null) return;
            var npc = _merchantNpc;
            if (_dialog != null) { OffCloseMerchant("a dialog opened (it replaces the merchant panel)"); return; }
            if (npc == null || !npc.Present || _player.Dead || Dist(_me.Pos, npc.Pos) > OffVendorCloseYards) { OffCloseMerchant(_player.Dead ? "you died" : "you walked away"); return; }
            if (_offMoneySeen != _player.Money)
            {
                _offMoneySeen = _player.Money;
                bool changed = false;
                foreach (var row in mv.Items)
                {
                    int m = OffMaxPurchases(row);
                    if (row.MaxPurchases != m) { row.MaxPurchases = m; changed = true; }
                }
                if (changed) mv.Version++;
            }
        }

        /// <summary>The options of an offline dialog: our vendor option (OfflineVendors), then the trainer's (trainer F3); a NEW conversation
        /// with no quests and exactly one non-gossip option chooses it at once (GossipFrame.lua:10-17).</summary>
        private void OffDialogOptions(OfflineCreature npc, NpcDialog d, bool fresh)
        {
            if (npc == null || d == null) return;
            if (!_offVendorLogged)
            {
                _offVendorLogged = true;
                WowLog.Info("OfflineSession: ours - offline vendor of " + OfflineVendors.Describe(_zone) + ", price = max(" + OfflineVendors.MinPrice + ", " +
                            OfflineVendors.PriceFactor + " x sell price) copper, unlimited stock; the merchant closes beyond " + OffVendorCloseYards + " yd; buyback " + OffBuybackSlots + " (not saved)");
            }
            if (OfflineVendors.Sells(npc.Tpl.Entry) && OfflineVendors.Stock(_zone).Count > 0 && d.Options.Find(o => o.Index == OfflineVendors.OptionIndex) == null)
                d.Options.Add(new GossipOptionView { Index = OfflineVendors.OptionIndex, Icon = OfflineVendors.OptionIcon, Text = OfflineVendors.OptionText });
            TrOfflineAddOption(npc, d);                                                     // trainer F3 (after the vendor option)
            if (fresh && d.Quests.Count == 0 && d.Options.Count == 1 && d.Options[0].Icon != 0)
            {
                WowLog.Info("OfflineSession: " + d.NpcName + " has only the option '" + d.Options[0].Text + "' - chosen directly (GossipFrame.lua:10-17)");
                SelectGossipOption(d.Options[0].Index, null);
            }
        }

        public void SelectGossipOption(int optionIndex, string code)
        {
            if (TrOfflineSelect(optionIndex)) return;                                       // trainer F3: its own option
            var d = _dialog;
            var npc = _dialogNpc;
            if (d == null || npc == null) return;
            if (optionIndex == OfflineVendors.OptionIndex && OfflineVendors.Sells(npc.Tpl.Entry)) { OffOpenMerchant(npc); return; }
            WowLog.Info("OfflineSession: " + d.NpcName + " has no option " + optionIndex + " - nothing");
        }

        // ------------------------------------------------------------------ the merchant

        public MerchantView CurrentMerchant { get { return _merchant; } }

        public void CloseMerchant() { OffCloseMerchant("closed by the player"); }

        private void OffCloseMerchant(string why)
        {
            if (_merchant == null) return;
            WowLog.Info("OfflineSession: the merchant " + _merchant.VendorName + " closes - " + why);
            _merchant = null;
            _merchantNpc = null;
        }

        private void OffOpenMerchant(OfflineCreature npc)
        {
            var stock = OfflineVendors.Stock(_zone);
            var mv = new MerchantView { VendorGuid = npc.Guid, VendorName = npc.State.Name, CanRepair = false, Empty = stock.Count == 0, Version = 1 };
            for (int i = 0; i < stock.Count; i++)
            {
                var d = stock[i];
                var row = new MerchantItemView { VendorSlot = i + 1, Item = ItemViewOf(d.Entry, 1), Price = OfflineVendors.PriceOf(d), BuyCount = 1, Stock = -1, Usable = true };
                row.MaxPurchases = OffMaxPurchases(row);
                mv.Items.Add(row);
            }
            CloseDialog();                                                                  // the merchant replaces the dialog (WoW's left panel)
            _merchant = mv;
            _merchantNpc = npc;
            _offMoneySeen = _player.Money;
            WowLog.Info("OfflineSession: the merchant " + mv.VendorName + " opens - " + mv.Items.Count + " items");
        }

        private int OffMaxPurchases(MerchantItemView row)
        {
            var d = row.Item != null ? FindItemDef(row.Item.Entry) : null;
            long m = Math.Max(1, MaxStack(d) / Math.Max(1, row.BuyCount));
            if (row.Price > 0) m = Math.Min(m, _player.Money / row.Price);
            return (int)Math.Max(1, Math.Min(OffPurchaseLimit, m));
        }

        /// <summary>How many of the item fit into the backpack (stacks of it with room + free slots).</summary>
        private int OffRoomFor(int entry, int maxStack)
        {
            long room = (long)Math.Max(0, CharacterRecord.BagSlots - _rec.Bags.Count) * maxStack;
            foreach (var s in _rec.Bags) if (s.Entry == entry && s.Count < maxStack) room += maxStack - s.Count;
            return (int)Math.Min(int.MaxValue, room);
        }

        /// <summary>All or nothing, like AC's CanStoreNewItem; a drop on an empty backpack slot puts the purchase there.</summary>
        public bool BuyItem(int itemIndex, int purchases, int bag, int slot)
        {
            var mv = _merchant;
            if (mv == null || itemIndex < 0 || itemIndex >= mv.Items.Count) return false;
            if (_player.Dead) { EmitError("You can't do that when you're dead."); return false; }          // ERR_PLAYER_DEAD (GlobalStrings.lua:3387)
            var row = mv.Items[itemIndex];
            int entry = row.Item != null ? row.Item.Entry : 0;
            var d = FindItemDef(entry);
            if (d == null) return false;
            int n = Math.Max(1, Math.Min(purchases, OffPurchaseLimit));
            long cost = (long)row.Price * n;
            if (cost > _player.Money) { EmitError("You don't have enough money."); return false; }           // ERR_NOT_ENOUGH_MONEY (3307)
            int max = MaxStack(d);
            int count = n * Math.Max(1, row.BuyCount);
            if (OffRoomFor(entry, max) < count) { EmitError("Inventory is full."); return false; }          // ERR_INV_FULL (3156)
            if (OffIsPack(bag, slot) && OffStackAt(bag, slot) == null && count <= max) _rec.Bags.Add(new ItemStack(entry, count) { Slot = slot - InvPos.PackStart });
            else _rec.AddToBags(entry, count, max);
            _player.Money -= (int)cost;
            EmitItem(GameEventType.LootReceived, entry, count, d.Name);                                       // online: SMSG_ITEM_PUSH_RESULT's line
            EmitItem(GameEventType.ItemBought, entry, count, d.Name);
            WowLog.Info("OfflineSession: bought " + count + " x '" + d.Name + "' for " + cost + " copper");
            OnItemsChanged(entry);
            RefreshBagsAndEquipment();
            return true;
        }

        /// <summary>Sells a backpack stack (count 0 = all) for ItemDef.SellPrice x count (ours); 0 = "The merchant doesn't want that item."</summary>
        public void SellItem(int bag, int slot, int count)
        {
            if (_merchant == null) return;
            var s = OffStackAt(bag, slot);
            if (s == null) { WowLog.Info("OfflineSession: nothing to sell at bag " + bag + " slot " + slot); return; }
            var d = FindItemDef(s.Entry);
            if (d == null || d.SellPrice <= 0) { EmitError("The merchant doesn't want that item."); return; }  // ERR_VENDOR_NOT_INTERESTED (3583)
            int n = count <= 0 || count >= s.Count ? s.Count : count;
            int money = d.SellPrice * n;
            int entry = s.Entry;
            s.Count -= n;
            if (s.Count <= 0) _rec.Bags.Remove(s);
            _player.Money += money;
            OffAddBuyback(entry, n, money);
            EmitItem(GameEventType.ItemSold, entry, money, d.Name);
            WowLog.Info("OfflineSession: sold " + n + " x '" + d.Name + "' for " + money + " copper");
            OnItemsChanged(entry);
            RefreshBagsAndEquipment();
        }

        /// <summary>The buyback list (WoW: fills 74.. upward, then replaces the OLDEST entry, AC PlayerStorage.cpp:4046-4095); buying back costs what was paid.</summary>
        private void OffAddBuyback(int entry, int count, int price)
        {
            int slot = -1;
            for (int i = 0; i < OffBuybackSlots; i++) if (_offBuyback[i] == null) { slot = i; break; }
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < OffBuybackSlots; i++) if (_offBuyback[i].Stamp < _offBuyback[slot].Stamp) slot = i;
            }
            _offBuyback[slot] = new OffBuyback { Entry = entry, Count = count, Price = price, Stamp = ++_offBuybackStamp };
        }

        /// <summary>Player.Buyback / BuybackPrice / LastBuyback from the in-memory list (RefreshBagsAndEquipment calls it).</summary>
        private void OffBuybackViews()
        {
            int newest = -1;
            for (int i = 0; i < OffBuybackSlots; i++)
            {
                var b = _offBuyback[i];
                ItemView v = null;
                if (b != null)
                {
                    v = ItemViewOf(b.Entry, b.Count);
                    v.Bag = InvPos.Bag0;
                    v.Slot = InvPos.BuybackStart + i;
                    if (newest < 0 || b.Stamp > _offBuyback[newest].Stamp) newest = i;
                }
                _player.Buyback[i] = v;
                _player.BuybackPrice[i] = b != null ? b.Price : 0;
            }
            _player.LastBuyback = newest;
        }

        public void BuybackItem(int index)
        {
            if (_merchant == null || index < 0 || index >= OffBuybackSlots) return;
            var b = _offBuyback[index];
            if (b == null) return;
            if (b.Price > _player.Money) { EmitError("You don't have enough money."); return; }              // ERR_NOT_ENOUGH_MONEY (3307)
            int max = MaxStack(FindItemDef(b.Entry));
            if (OffRoomFor(b.Entry, max) < b.Count) { EmitError("Inventory is full."); return; }             // ERR_INV_FULL (3156)
            _rec.AddToBags(b.Entry, b.Count, max);
            _player.Money -= b.Price;
            _offBuyback[index] = null;
            WowLog.Info("OfflineSession: bought back " + b.Count + " x item " + b.Entry + " for " + b.Price + " copper");
            OnItemsChanged(b.Entry);
            RefreshBagsAndEquipment();
        }

        public void RepairItem(int bag, int slot) { EmitInfo("Nothing to repair."); }                      // ours: offline items have no durability

        // ------------------------------------------------------------------ nothing offline (no banker, innkeeper or readable item exists offline)

        private void OffNothing(string what)
        {
            if (_offNothingLogged.Add(what)) WowLog.Info("OfflineSession: " + what + " - there is none offline (iteration 11, gap G8)");
        }

        public ulong BankerGuid { get { return 0; } }
        public void CloseBank() { }
        public void BuyBankSlot() { OffNothing("bank slot"); }
        public void BankItem(int bag, int slot) { OffNothing("bank"); }

        public void ConfirmBinder(bool accept) { _player.PendingBinderGuid = 0; OffNothing("innkeeper"); }

        public ItemTextView CurrentItemText { get { return null; } }
        public void ItemTextPage(int delta) { }
        public void CloseItemText() { }
    }
}
