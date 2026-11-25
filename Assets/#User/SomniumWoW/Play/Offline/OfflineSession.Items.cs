using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        // food / drink in progress
        private ItemDef _eating;
        private float _eatElapsed, _eatHealRate, _eatManaRate, _eatHealFrac, _eatManaFrac;
        private WowPos _eatPos;
        private readonly AuraView _eatView = new AuraView();

        // ------------------------------------------------------------------ loot

        private void GenerateLoot(OfflineCreature c)
        {
            c.LootGenerated = true;
            c.LootItems.Clear();
            var t = c.Tpl;
            if (t.MaxMoney > 0) c.LootMoney = _rng.Next(Math.Max(0, t.MinMoney), Math.Max(t.MinMoney, t.MaxMoney) + 1);
            foreach (var l in t.Loot)
            {
                if (l == null) continue;
                if (l.QuestId != 0 && !QuestNeedsItem(l.QuestId, l.ItemEntry)) continue;      // quest drops only while needed
                if (Rand01() > l.Chance) continue;
                int n = Math.Max(1, l.MinCount == l.MaxCount ? l.MinCount : _rng.Next(Math.Min(l.MinCount, l.MaxCount), Math.Max(l.MinCount, l.MaxCount) + 1));
                c.LootItems.Add(new ItemStack(l.ItemEntry, n));
            }
            c.State.Lootable = c.LootMoney > 0 || c.LootItems.Count > 0;
        }

        private void OpenLoot(OfflineCreature c)
        {
            _lootCorpse = c;
            var v = new LootView { CorpseGuid = c.Guid, Money = c.LootMoney };
            foreach (var s in c.LootItems) v.Items.Add(ItemViewOf(s.Entry, s.Count));
            _loot = v;
            if (_autoLoot && !_inAutoLoot) { _inAutoLoot = true; try { LootAll(); } finally { _inAutoLoot = false; } }   // AutoLoot setting (iteration 3)
        }

        public void CloseLoot()
        {
            if (_lootCorpse != null && !_lootCorpse.State.Lootable && _lootCorpse.CorpseTimer > 30f) _lootCorpse.CorpseTimer = 30f;
            _loot = null;
            _lootCorpse = null;
        }

        public void LootItem(int index)
        {
            var c = _lootCorpse;
            if (c == null || _loot == null) return;
            if (Dist(_me.Pos, c.Pos) > InteractRange + c.CombatReach + 2f) { EmitError("You are too far away"); CloseLoot(); return; }
            if (index < 0 || index >= c.LootItems.Count) return;
            var s = c.LootItems[index];
            int left = GiveItem(s.Entry, s.Count);
            if (left > 0) { s.Count = left; EmitError("Inventory is full"); OpenLoot(c); return; }
            c.LootItems.RemoveAt(index);
            AfterLoot(c);
        }

        public void LootAll()
        {
            var c = _lootCorpse;
            if (c == null || _loot == null) return;
            if (Dist(_me.Pos, c.Pos) > InteractRange + c.CombatReach + 2f) { EmitError("You are too far away"); CloseLoot(); return; }
            if (c.LootMoney > 0)
            {
                _player.Money += c.LootMoney;
                Emit(GameEventType.MoneyReceived, c.Guid, PlayerGuid, c.LootMoney, 0, false, "You loot " + MoneyText(c.LootMoney));
                c.LootMoney = 0;
            }
            for (int i = c.LootItems.Count - 1; i >= 0; i--)
            {
                var s = c.LootItems[i];
                int left = GiveItem(s.Entry, s.Count);
                if (left > 0) { s.Count = left; EmitError("Inventory is full"); continue; }
                c.LootItems.RemoveAt(i);
            }
            AfterLoot(c);
        }

        private void AfterLoot(OfflineCreature c)
        {
            c.State.Lootable = c.LootMoney > 0 || c.LootItems.Count > 0;
            if (!c.State.Lootable) CloseLoot();
            else OpenLoot(c);
        }

        /// <summary>Puts items into the bags; returns how many did not fit. Emits LootReceived and updates collect objectives.</summary>
        private int GiveItem(int entry, int count)
        {
            if (entry <= 0 || count <= 0) return 0;
            var d = FindItemDef(entry);
            int left = _rec.AddToBags(entry, count, MaxStack(d));
            int got = count - left;
            if (got > 0)
            {
                EmitItem(GameEventType.LootReceived, entry, got, d != null ? d.Name : ("Item " + entry));
                OnItemsChanged(entry);
                RefreshBagsAndEquipment();
            }
            return left;
        }

        private static int MaxStack(ItemDef d)
        {
            if (d == null) return 1;
            if (d.QuestId != 0 || d.HealAmount > 0 || d.ManaAmount > 0) return 20;
            return d.InventoryType == 0 ? 20 : 1;
        }

        // ------------------------------------------------------------------ bags and equipment

        public void EquipItem(int bagIndex)
        {
            if (bagIndex < 0 || bagIndex >= _rec.Bags.Count) return;
            if (_player.Dead) { EmitError("You are dead"); return; }
            if (_casting != null) { EmitError("Another action is in progress"); return; }
            if (!MountedItemGate()) return;                 // iteration 7: food and drink are spells - dismount first / refused in the air
            var stack = _rec.Bags[bagIndex];
            int invType = InventoryTypeOf(stack.Entry);
            int slot = CharacterRecord.EquipSlotFor(invType, _rec.Equipment);
            if (slot < 0) { EmitError("You can't equip that"); return; }
            int old = _rec.Equipment[slot];
            _rec.Equipment[slot] = stack.Entry;
            if (stack.Count > 1) stack.Count--; else _rec.Bags.RemoveAt(bagIndex);
            if (old != 0) _rec.AddToBags(old, 1, 1);
            // A two-hander frees the off hand.
            if (invType == 17 && _rec.Equipment[(int)EquipSlot.OffHand] != 0)
            {
                int off = _rec.Equipment[(int)EquipSlot.OffHand];
                _rec.Equipment[(int)EquipSlot.OffHand] = 0;
                _rec.AddToBags(off, 1, 1);
            }
            if (slot == (int)EquipSlot.OffHand && InventoryTypeOf(_rec.Equipment[(int)EquipSlot.MainHand]) == 17)
            {
                int main = _rec.Equipment[(int)EquipSlot.MainHand];
                _rec.Equipment[(int)EquipSlot.MainHand] = 0;
                _rec.AddToBags(main, 1, 1);
            }
            _swingTimer = Math.Max(_swingTimer, 0.5f);
            _statsDirty = true;
            RecalcStats();
            RebuildSpellbook();
            RefreshBagsAndEquipment();
            var d = FindItemDef(_rec.Equipment[slot]);
            EmitInfo("Equipped " + (d != null ? d.Name : "item"));
        }

        public void UseItem(int bagIndex)
        {
            if (bagIndex < 0 || bagIndex >= _rec.Bags.Count) return;
            if (_player.Dead) { EmitError("You are dead"); return; }
            var stack = _rec.Bags[bagIndex];
            var d = FindItemDef(stack.Entry);
            if (d == null) { EmitError("Item is not usable"); return; }
            if (d.HealAmount <= 0 && d.ManaAmount <= 0)
            {
                if (CharacterRecord.EquipSlotFor(InventoryTypeOf(stack.Entry), _rec.Equipment) >= 0) { EquipItem(bagIndex); return; }
                EmitError("Item is not usable");
                return;
            }
            if (d.UseSeconds <= 0f)
            {
                if (!MountedItemGate()) return;             // iteration 7: potions are spells - dismount first / refused in the air
                // Potion: instant.
                if (d.HealAmount > 0) Heal(_me, d.HealAmount, 0, false, GameEventType.SpellHeal);
                if (d.ManaAmount > 0 && _maxMana > 0) AddPower(PowerType.Mana, d.ManaAmount);
                ConsumeOne(bagIndex);
                return;
            }
            // Food / drink: sit and restore over time; not in combat; moving, combat or damage stops it.
            if (_inCombat) { EmitError("You can't do that while in combat"); return; }
            if (_casting != null) { EmitError("Another action is in progress"); return; }
            if (!MountedItemGate()) return;                 // iteration 7: food and drink are spells - dismount first / refused in the air
            string sitNo = WowConsumeGate.Refusal();        // lead 2026-10-04: Immersive eating (VR) - before the item is used
            if (sitNo != null) { EmitError(sitNo); return; }
            _eating = d;
            _eatElapsed = 0f;
            _eatHealRate = d.HealAmount / d.UseSeconds;
            _eatManaRate = d.ManaAmount / d.UseSeconds;
            _eatHealFrac = _eatManaFrac = 0f;
            _eatPos = _me.Pos;
            _eatView.SpellId = 0;
            _eatView.Name = d.Name;
            _eatView.IconPath = d.IconPath;
            _eatView.Duration = d.UseSeconds;
            _eatView.Remaining = d.UseSeconds;
            _eatView.Harmful = false;
            _eatView.CasterGuid = PlayerGuid;
            _aurasChanged = true;
            ConsumeOne(bagIndex);
            EmitInfo(d.HealAmount > 0 ? "Eating " + d.Name : "Drinking " + d.Name);
        }

        private void ConsumeOne(int bagIndex)
        {
            var s = _rec.Bags[bagIndex];
            int entry = s.Entry;
            if (s.Count > 1) s.Count--; else _rec.Bags.RemoveAt(bagIndex);
            OnItemsChanged(entry);
            RefreshBagsAndEquipment();
        }

        private void StepConsumable(float dt)
        {
            if (_eating == null) return;
            if (Dist(_me.Pos, _eatPos) > CastMoveTolerance * 2f) { CancelConsumable(null); return; }
            _eatElapsed += dt;
            _eatHealFrac += _eatHealRate * dt;
            _eatManaFrac += _eatManaRate * dt;
            if (_eatHealFrac >= 1f) { int v = (int)_eatHealFrac; _eatHealFrac -= v; _me.Health = Math.Min(_me.MaxHealth, _me.Health + v); }
            if (_eatManaFrac >= 1f) { int v = (int)_eatManaFrac; _eatManaFrac -= v; if (_maxMana > 0) _mana = Math.Min(_maxMana, _mana + v); }
            _eatView.Remaining = Math.Max(0f, _eating.UseSeconds - _eatElapsed);
            bool full = (_eatHealRate <= 0 || _me.Health >= _me.MaxHealth) && (_eatManaRate <= 0 || _maxMana <= 0 || _mana >= _maxMana);
            if (_eatElapsed >= _eating.UseSeconds || full) { _eating = null; _aurasChanged = true; }
        }

        private void CancelConsumable(string reason)
        {
            if (_eating == null) return;
            _eating = null;
            _aurasChanged = true;
            if (reason != null) EmitError(reason);
        }

        // ------------------------------------------------------------------ item views

        private int InventoryTypeOf(int entry)
        {
            if (entry == 0) return 0;
            var d = FindItemDef(entry);
            if (d != null && d.InventoryType != 0) return d.InventoryType;
            if (_itemDbc != null) { int r = _itemDbc.FindRow(entry); if (r >= 0) return _itemDbc.GetInt(r, DbcFields.Item.InventoryType); }
            return 0;
        }

        private readonly Dictionary<int, ItemView> _itemViewCache = new Dictionary<int, ItemView>();

        /// <summary>An item for the UI: the zone's ItemDef (name, stats) + Item.dbc / ItemDisplayInfo.dbc from the files (display, icon, slot).</summary>
        public ItemView ItemViewOf(int entry, int count)
        {
            ItemView proto;
            if (!_itemViewCache.TryGetValue(entry, out proto))
            {
                proto = BuildItemView(entry);
                _itemViewCache[entry] = proto;
            }
            return new ItemView
            {
                Entry = proto.Entry, DisplayId = proto.DisplayId, Name = proto.Name, IconPath = proto.IconPath, Quality = proto.Quality, Count = count,
                InventoryType = proto.InventoryType, SheatheType = proto.SheatheType, IsQuestItem = proto.IsQuestItem, Tooltip = proto.Tooltip,
                // iteration 11 (npc): the bag / merchant fields (Bag / Slot are set where the item sits, RefreshBagsAndEquipment)
                SellPrice = proto.SellPrice, MaxStack = proto.MaxStack, ItemClass = proto.ItemClass, ItemSubClass = proto.ItemSubClass,
                ItemLevel = proto.ItemLevel, RequiredLevel = proto.RequiredLevel
            };
        }

        private ItemView BuildItemView(int entry)
        {
            var d = FindItemDef(entry);
            var v = new ItemView { Entry = entry, Name = d != null && !string.IsNullOrEmpty(d.Name) ? d.Name : "Item " + entry, Quality = d != null ? d.Quality : 1 };
            int dbcDisplay = 0, dbcInv = 0, dbcSheath = 0, dbcClass = -1, dbcSub = -1;
            if (_itemDbc != null)
            {
                int r = _itemDbc.FindRow(entry);
                if (r >= 0)
                {
                    dbcDisplay = _itemDbc.GetInt(r, DbcFields.Item.DisplayInfoID);
                    dbcInv = _itemDbc.GetInt(r, DbcFields.Item.InventoryType);
                    dbcSheath = _itemDbc.GetInt(r, DbcFields.Item.SheatheType);
                    dbcClass = _itemDbc.GetInt(r, DbcFields.Item.ClassID);
                    dbcSub = _itemDbc.GetInt(r, DbcFields.Item.SubclassID);
                }
            }
            v.DisplayId = d != null && d.DisplayId != 0 ? d.DisplayId : dbcDisplay;
            v.InventoryType = d != null && d.InventoryType != 0 ? d.InventoryType : dbcInv;
            v.SheatheType = d != null && d.SheatheType != 0 ? d.SheatheType : dbcSheath;
            v.IsQuestItem = d != null && d.QuestId != 0;
            v.IconPath = d != null && !string.IsNullOrEmpty(d.IconPath) ? d.IconPath : "";
            if (v.IconPath.Length == 0 && _itemDisplay != null && v.DisplayId > 0)
            {
                int r = _itemDisplay.FindRow(v.DisplayId);
                if (r >= 0)
                {
                    string icon = _itemDisplay.GetString(r, DbcFields.ItemDisplayInfo.InventoryIcon);
                    if (!string.IsNullOrEmpty(icon)) v.IconPath = @"Interface\Icons\" + icon;
                }
            }
            if (v.IconPath.Length == 0) v.IconPath = @"Interface\Icons\INV_Misc_QuestionMark";
            v.Tooltip = ItemTooltip(v, d, dbcClass, dbcSub);
            // iteration 11 (npc): ItemDef.SellPrice is ours (every zone file); offline items have no durability, no bag slots, no page text
            v.SellPrice = d != null ? Math.Max(0, d.SellPrice) : 0;
            v.MaxStack = MaxStack(d);
            v.ItemClass = dbcClass;
            v.ItemSubClass = dbcSub;
            return v;
        }

        /// <summary>
        /// The 3.3.5a item tooltip offline (item tooltip fix, out11_tooltip): the same builder as online (Play/WowItemTooltip.cs) over the
        /// zone file's ItemDef + Item.dbc (class / subclass / slot); "Quest Item" for a quest's item; the zone's consumable line as Use:.
        /// </summary>
        private string ItemTooltip(ItemView v, ItemDef d, int itemClass, int subclass)
        {
            var t = new ItemTipData { Entry = v.Entry, Name = v.Name ?? "", Quality = v.Quality, InventoryType = v.InventoryType, Class = itemClass, SubClass = subclass };
            if (v.IsQuestItem) t.Bonding = 4;                                                    // ITEM_BIND_QUEST "Quest Item"
            float mn, mx, spd;
            if (itemClass == 2 || (d != null && d.MaxDamage > 0))
            {
                if (d != null && d.MaxDamage > 0) { mn = d.MinDamage; mx = d.MaxDamage; spd = d.Speed; }
                else WowFormulas.DefaultWeapon(subclass, out mn, out mx, out spd);
                t.DmgMin[0] = mn; t.DmgMax[0] = mx; t.Delay = (int)Math.Round(spd * 1000f);
            }
            if (d != null)
            {
                t.Armor = d.Armor;
                int[] types = { 4, 3, 7, 5, 6 };                                                   // ITEM_MOD_STRENGTH / AGILITY / STAMINA / INTELLECT / SPIRIT (ItemTemplate.h:29-33)
                int[] values = { d.Strength, d.Agility, d.Stamina, d.Intellect, d.Spirit };
                for (int i = 0; i < types.Length; i++)
                    if (values[i] != 0 && t.StatCount < ItemTip.MaxStats) { t.StatType[t.StatCount] = types[i]; t.StatValue[t.StatCount] = values[i]; t.StatCount++; }
                if (d.HealAmount > 0 || d.ManaAmount > 0)
                {
                    var sb = new StringBuilder("Use: Restores ");
                    if (d.HealAmount > 0) sb.Append(d.HealAmount).Append(" health");
                    if (d.HealAmount > 0 && d.ManaAmount > 0) sb.Append(" and ");
                    if (d.ManaAmount > 0) sb.Append(d.ManaAmount).Append(" mana");
                    if (d.UseSeconds > 0) sb.Append(" over ").Append(d.UseSeconds.ToString("0")).Append(" sec.  Must remain seated while eating.");
                    t.UseText = sb.ToString();
                }
                t.SellPrice = Math.Max(0, d.SellPrice);
            }
            var ctx = new ItemTipContext { Level = Math.Max(1, _player.Level), ClassId = (int)_player.Class, Race = _player.Race };
            return ItemTip.Build(t, null, ItemTipDb.Get(_files), ctx);
        }

        /// <summary>
        /// Player.Bags / Equipment / Containers from the record. Iteration 11 (npc, proposal-npc.md s7): the backpack is slot-addressed
        /// (ItemStack.Slot 0..15, record key "bagpos"): the record's list is kept in slot order, so Player.Bags[i] (backpack order, as online)
        /// stays the view of _rec.Bags[i] for EquipItem / UseItem(bagIndex). Views carry (Bag 255, Slot 23 + backpack slot) / (255, equipment
        /// slot). Containers = [the 16-slot backpack, 4 empty bag slots] (offline has no bag items - gap G8); Keyring null, Bank empty.
        /// </summary>
        private void RefreshBagsAndEquipment()
        {
            OffNormalizeBagSlots();
            var pack = OffContainers();
            for (int k = 0; k < pack.Slots.Length; k++) pack.Slots[k] = null;
            _player.Bags.Clear();
            foreach (var s in _rec.Bags)
            {
                var v = ItemViewOf(s.Entry, s.Count);
                v.Bag = InvPos.Bag0;
                v.Slot = InvPos.PackStart + s.Slot;
                _player.Bags.Add(v);
                if (s.Slot >= 0 && s.Slot < pack.Slots.Length) pack.Slots[s.Slot] = v;
            }
            pack.Free = Math.Max(0, CharacterRecord.BagSlots - _rec.Bags.Count);
            for (int i = 0; i < 19; i++)
            {
                var v = _rec.Equipment[i] != 0 ? ItemViewOf(_rec.Equipment[i], 1) : null;
                if (v != null) { v.Bag = InvPos.Bag0; v.Slot = i; }
                _player.Equipment[i] = v;
            }
            _player.FreeBagSlots = CharacterRecord.BagSlots - _rec.Bags.Count;
            OffBuybackViews();
        }

        /// <summary>Unique backpack slots 0..15 (a missing / out-of-range / duplicate one takes the first free) and the list in slot order.</summary>
        private void OffNormalizeBagSlots()
        {
            var used = new bool[CharacterRecord.BagSlots];
            foreach (var s in _rec.Bags)
            {
                if (s.Slot >= 0 && s.Slot < used.Length && !used[s.Slot]) used[s.Slot] = true;
                else s.Slot = -1;
            }
            foreach (var s in _rec.Bags)
            {
                if (s.Slot >= 0) continue;
                for (int k = 0; k < used.Length; k++) if (!used[k]) { s.Slot = k; used[k] = true; break; }
            }
            _rec.Bags.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        }

        /// <summary>Player.Containers offline: [0] the backpack (Bag 255, SlotBase 23, 16 slots), [1..4] the empty bag slots 19..22.</summary>
        private ContainerView OffContainers()
        {
            var cs = _player.Containers;
            if (cs.Count == 5 && cs[0].Slots.Length == CharacterRecord.BagSlots) return cs[0];
            cs.Clear();
            cs.Add(new ContainerView { Bag = InvPos.Bag0, SlotBase = InvPos.PackStart, InvSlot = -1, Size = CharacterRecord.BagSlots, Slots = new ItemView[CharacterRecord.BagSlots], Free = CharacterRecord.BagSlots });
            for (int b = InvPos.BagSlotStart; b < InvPos.BagSlotEnd; b++) cs.Add(new ContainerView { Bag = b, SlotBase = 0, InvSlot = b, Size = 0 });
            return cs[0];
        }

        // ------------------------------------------------------------------ iteration 11 (npc): item positions offline (gap D1 / proposal-npc.md s7)
        // The backpack (255, 23..38) and the equipment (255, 0..18); offline has no bags, bank or keyring (gap G8).

        private static bool OffIsPack(int bag, int slot) { return bag == InvPos.Bag0 && slot >= InvPos.PackStart && slot < InvPos.PackStart + CharacterRecord.BagSlots; }
        private static bool OffIsEquip(int bag, int slot) { return bag == InvPos.Bag0 && slot >= 0 && slot < InvPos.EquipEnd; }

        private ItemStack OffStackAt(int bag, int slot)
        {
            if (!OffIsPack(bag, slot)) return null;
            int k = slot - InvPos.PackStart;
            foreach (var s in _rec.Bags) if (s.Slot == k) return s;
            return null;
        }

        private bool OffNoPosition(int bag, int slot)
        {
            if (OffIsPack(bag, slot) || OffIsEquip(bag, slot)) return false;
            WowLog.Info("OfflineSession: bag " + bag + " slot " + slot + " - offline there are only the backpack and the equipment");
            EmitError("That item does not go in that slot.");                                   // ERR_WRONG_SLOT (GlobalStrings.lua:3600)
            return true;
        }

        /// <summary>The equipment slots an InventoryType may go to (CharacterRecord.EquipSlotFor's table, both slots of a pair; a one-hand
        /// weapon in the off hand only for a dual wielder).</summary>
        private bool OffSlotAccepts(int invType, int k)
        {
            switch (invType)
            {
                case 1: return k == (int)EquipSlot.Head;
                case 2: return k == (int)EquipSlot.Neck;
                case 3: return k == (int)EquipSlot.Shoulders;
                case 4: return k == (int)EquipSlot.Shirt;
                case 5: case 20: return k == (int)EquipSlot.Chest;
                case 6: return k == (int)EquipSlot.Waist;
                case 7: return k == (int)EquipSlot.Legs;
                case 8: return k == (int)EquipSlot.Feet;
                case 9: return k == (int)EquipSlot.Wrists;
                case 10: return k == (int)EquipSlot.Hands;
                case 11: return k == (int)EquipSlot.Finger1 || k == (int)EquipSlot.Finger2;
                case 12: return k == (int)EquipSlot.Trinket1 || k == (int)EquipSlot.Trinket2;
                case 16: return k == (int)EquipSlot.Back;
                case 17: case 21: return k == (int)EquipSlot.MainHand;
                case 13: return k == (int)EquipSlot.MainHand || (k == (int)EquipSlot.OffHand && CanDualWield());
                case 14: case 22: case 23: return k == (int)EquipSlot.OffHand;
                case 15: case 25: case 26: case 28: return k == (int)EquipSlot.Ranged;
                case 19: return k == (int)EquipSlot.Tabard;
            }
            return false;
        }

        /// <summary>The WoW text for an item that may not go into equipment slot k.</summary>
        private void OffWrongSlot(int invType, int k)
        {
            if (invType == 13 && k == (int)EquipSlot.OffHand) EmitError("You cannot dual-wield");            // ERR_2HSKILLNOTFOUND (GlobalStrings.lua:2862)
            else EmitError("That item does not go in that slot.");                                           // ERR_WRONG_SLOT (3600)
        }

        public void UseItemAt(int bag, int slot)
        {
            if (OffNoPosition(bag, slot)) return;
            if (OffIsEquip(bag, slot)) { if (_rec.Equipment[slot] == 0) EmitError("That slot is empty."); return; }   // offline equipment has no "Use:" spell
            var s = OffStackAt(bag, slot);
            if (s == null) { EmitError("That slot is empty."); return; }                                       // ERR_SLOT_EMPTY (3488)
            UseItem(_rec.Bags.IndexOf(s));
        }

        public void EquipItemAt(int bag, int slot)
        {
            if (OffNoPosition(bag, slot)) return;
            if (OffIsEquip(bag, slot)) return;                                                                  // already equipped
            var s = OffStackAt(bag, slot);
            if (s == null) { EmitError("That slot is empty."); return; }
            EquipItem(_rec.Bags.IndexOf(s));
        }

        /// <summary>Moves like Player::SwapItem: into an empty slot, merging a stack of the same item, swapping, equipping onto / unequipping
        /// from an equipment slot (the old item takes the source slot).</summary>
        public void MoveItem(int fromBag, int fromSlot, int toBag, int toSlot)
        {
            if (fromBag == toBag && fromSlot == toSlot) return;
            if (OffNoPosition(fromBag, fromSlot) || OffNoPosition(toBag, toSlot)) return;
            bool srcEq = OffIsEquip(fromBag, fromSlot), dstEq = OffIsEquip(toBag, toSlot);
            if ((srcEq || dstEq) && _player.Dead) { EmitError("You can't do that when you're dead."); return; }   // ERR_PLAYER_DEAD (3387)
            if ((srcEq || dstEq) && _casting != null) { EmitError("Another action is in progress"); return; }       // the offline EquipItem rule
            if (!srcEq && !dstEq)
            {
                var src = OffStackAt(fromBag, fromSlot);
                if (src == null) { EmitError("That slot is empty."); return; }
                var dst = OffStackAt(toBag, toSlot);
                int max = MaxStack(FindItemDef(src.Entry));
                if (dst == null) src.Slot = toSlot - InvPos.PackStart;
                else if (dst.Entry == src.Entry && dst.Count < max)
                {
                    int move = Math.Min(max - dst.Count, src.Count);
                    dst.Count += move; src.Count -= move;
                    if (src.Count <= 0) _rec.Bags.Remove(src);
                }
                else { int t = src.Slot; src.Slot = dst.Slot; dst.Slot = t; }
                RefreshBagsAndEquipment();
                return;
            }
            if (!srcEq && dstEq) { var src = OffStackAt(fromBag, fromSlot); if (src == null) { EmitError("That slot is empty."); return; } OffEquipInto(src, toSlot); return; }
            if (srcEq && !dstEq)
            {
                int entry = _rec.Equipment[fromSlot];
                if (entry == 0) { EmitError("That slot is empty."); return; }
                var dst = OffStackAt(toBag, toSlot);
                if (dst != null)
                {
                    int dt = InventoryTypeOf(dst.Entry);
                    if (dst.Count != 1 || !OffSlotAccepts(dt, fromSlot)) { OffWrongSlot(dt, fromSlot); return; }
                    OffEquipInto(dst, fromSlot);                                                                // the dropped-on item is equipped, the old one takes its slot
                    return;
                }
                _rec.Equipment[fromSlot] = 0;
                _rec.Bags.Add(new ItemStack(entry, 1) { Slot = toSlot - InvPos.PackStart });
                OffAfterEquipChange(null);
                return;
            }
            // equipment -> equipment (rings, trinkets, main / off hand)
            int a = _rec.Equipment[fromSlot], b = _rec.Equipment[toSlot];
            if (a == 0) { EmitError("That slot is empty."); return; }
            int at = InventoryTypeOf(a), bt = InventoryTypeOf(b);
            if (!OffSlotAccepts(at, toSlot)) { OffWrongSlot(at, toSlot); return; }
            if (b != 0 && !OffSlotAccepts(bt, fromSlot)) { OffWrongSlot(bt, fromSlot); return; }
            _rec.Equipment[toSlot] = a;
            _rec.Equipment[fromSlot] = b;
            OffAfterEquipChange(null);
        }

        /// <summary>Equips one item of the stack into equipment slot k (the old item goes to the stack's slot, a two-hander / an off hand
        /// frees the other hand into the backpack - the offline EquipItem rules).</summary>
        private void OffEquipInto(ItemStack src, int k)
        {
            int invType = InventoryTypeOf(src.Entry);
            if (!OffSlotAccepts(invType, k)) { OffWrongSlot(invType, k); return; }
            if (!MountedItemGate()) return;                                                                     // iteration 7: equipping is refused while riding (offline rule)
            int old = _rec.Equipment[k];
            int freeSlots = CharacterRecord.BagSlots - _rec.Bags.Count + (src.Count == 1 ? 1 : 0) - (old != 0 ? 1 : 0);
            bool freesOff = invType == 17 && _rec.Equipment[(int)EquipSlot.OffHand] != 0;
            bool freesMain = k == (int)EquipSlot.OffHand && InventoryTypeOf(_rec.Equipment[(int)EquipSlot.MainHand]) == 17;
            if ((freesOff || freesMain) && freeSlots < 1) { EmitError("Inventory is full."); return; }        // ERR_INV_FULL (3156)
            int slot = src.Slot;
            _rec.Equipment[k] = src.Entry;
            if (src.Count > 1) src.Count--; else _rec.Bags.Remove(src);
            if (old != 0)
            {
                if (src.Count >= 1 && _rec.Bags.Contains(src)) _rec.AddToBags(old, 1, 1);
                else _rec.Bags.Add(new ItemStack(old, 1) { Slot = slot });
            }
            if (freesOff)
            {
                int off = _rec.Equipment[(int)EquipSlot.OffHand];
                _rec.Equipment[(int)EquipSlot.OffHand] = 0;
                _rec.AddToBags(off, 1, 1);
            }
            if (freesMain)
            {
                int main = _rec.Equipment[(int)EquipSlot.MainHand];
                _rec.Equipment[(int)EquipSlot.MainHand] = 0;
                _rec.AddToBags(main, 1, 1);
            }
            OffAfterEquipChange(_rec.Equipment[k]);
        }

        private void OffAfterEquipChange(int? equipped)
        {
            _swingTimer = Math.Max(_swingTimer, 0.5f);
            _statsDirty = true;
            RecalcStats();
            RebuildSpellbook();
            RefreshBagsAndEquipment();
            if (equipped.HasValue && equipped.Value != 0)
            {
                var d = FindItemDef(equipped.Value);
                EmitInfo("Equipped " + (d != null ? d.Name : "item"));                                         // the offline EquipItem line
            }
        }

        /// <summary>Splits count items off a backpack stack onto an empty slot or a stack of the same item with room (else ERR_SPLIT_FAILED).</summary>
        public void SplitItem(int fromBag, int fromSlot, int toBag, int toSlot, int count)
        {
            if (OffNoPosition(fromBag, fromSlot) || OffNoPosition(toBag, toSlot)) return;
            var src = OffStackAt(fromBag, fromSlot);
            if (src == null) { EmitError("That slot is empty."); return; }
            if (count <= 0 || count >= src.Count) return;                                                       // a whole stack is a MoveItem (the contract)
            if (!OffIsPack(toBag, toSlot)) { EmitError("Couldn't split those items."); return; }                // ERR_SPLIT_FAILED (3507)
            var dst = OffStackAt(toBag, toSlot);
            int max = MaxStack(FindItemDef(src.Entry));
            if (dst == null) _rec.Bags.Add(new ItemStack(src.Entry, count) { Slot = toSlot - InvPos.PackStart });
            else if (dst.Entry == src.Entry && dst.Count + count <= max) dst.Count += count;
            else { EmitError("Couldn't split those items."); return; }
            src.Count -= count;
            RefreshBagsAndEquipment();
        }

        /// <summary>Destroys (after the UI's DELETE_ITEM popup): the whole stack for count &lt;= 0 or &gt;= the stack, else that many. A quest
        /// item's objective count drops at once (OnItemsChanged, as a server would).</summary>
        public void DestroyItem(int bag, int slot, int count)
        {
            if (OffNoPosition(bag, slot)) return;
            int entry, n;
            if (OffIsEquip(bag, slot))
            {
                entry = _rec.Equipment[slot];
                if (entry == 0) { EmitError("That slot is empty."); return; }
                if (_player.Dead) { EmitError("You can't do that when you're dead."); return; }
                _rec.Equipment[slot] = 0;
                n = 1;
                _statsDirty = true;
                RecalcStats();
                RebuildSpellbook();
            }
            else
            {
                var s = OffStackAt(bag, slot);
                if (s == null) { EmitError("That slot is empty."); return; }
                entry = s.Entry;
                n = count <= 0 || count >= s.Count ? s.Count : count;
                s.Count -= n;
                if (s.Count <= 0) _rec.Bags.Remove(s);
            }
            var d = FindItemDef(entry);
            string name = d != null ? d.Name : "Item " + entry;
            EmitItem(GameEventType.ItemDestroyed, entry, n, name);
            WowLog.Info("OfflineSession: destroyed '" + name + "' x" + n);
            OnItemsChanged(entry);
            RefreshBagsAndEquipment();
        }

        /// <summary>A drop on a bag button: offline only the backpack exists - an equipped item goes to its first free slot.</summary>
        public void StoreItemInBag(int srcBag, int srcSlot, int dstBag)
        {
            if (OffNoPosition(srcBag, srcSlot)) return;
            if (dstBag != InvPos.Bag0) { EmitError("That item does not go in that slot."); return; }          // no bags offline (gap G8)
            if (!OffIsEquip(srcBag, srcSlot)) return;                                                           // already in the backpack
            int entry = _rec.Equipment[srcSlot];
            if (entry == 0) { EmitError("That slot is empty."); return; }
            if (_rec.FirstFreeBagSlot() < 0) { EmitError("Inventory is full."); return; }                       // ERR_INV_FULL (3156)
            _rec.Equipment[srcSlot] = 0;
            _rec.AddToBags(entry, 1, 1);
            OffAfterEquipChange(null);
        }

        // ------------------------------------------------------------------ per-frame view update

        private void UpdateViews()
        {
            if (_statsDirty) { _statsDirty = false; RecalcStats(); }
            if (_questDirty) QuestChanged();
            UpdatePlayerView();
            UpdateViewsIter4();
            UpdateAbilities();
            UpdateMountViews();                        // iteration 7: Player.Mounts (usable / active), the testing switches, speeds
            OffVendorTick();                           // iteration 11 (npc, OfflineSession.Vendor.cs): dialog options, the merchant's walk-away
            var target = CreatureOf(_target);
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!c.Present) continue;
                var s = c.State;
                s.Pos = c.Pos;
                s.Health = c.Health;
                s.MaxHealth = c.MaxHealth;
                s.Dead = c.Dead;
                s.ComboPoints = _comboTarget == c.Guid ? _combo : 0;
                s.ConColor = c.Tpl.IsNpc ? 2 : WowFormulas.ColorCode(_player.Level, c.Level);
            }
        }

        private void UpdatePlayerView()
        {
            var p = _player;
            p.Level = _me.Level = p.Level;
            p.Health = Math.Max(0, _me.Health);
            p.MaxHealth = _me.MaxHealth;
            var pt = CurrentPower();
            p.Power = pt;
            p.PowerValue = PowerOf(pt);
            p.PowerMax = pt == PowerType.Mana ? _maxMana : 100;
            p.Mana = _mana;
            p.MaxMana = _maxMana;
            p.ComboPoints = _comboTarget == _target && _target != 0 ? _combo : 0;
            p.InCombat = _inCombat;
            p.AutoAttacking = _autoAttack;
            p.AutoShooting = _autoShot;
            p.CastingSpellId = _casting != null ? _casting.Id : 0;
            p.CastElapsed = _casting != null ? Math.Min(_castElapsed, _castTotal) : 0f;
            p.CastTotal = _casting != null ? _castTotal : 0f;
            UpdatePlayerView3(p);
            p.GcdRemaining = _gcd;
            p.GcdTotal = _gcdTotal;
            p.Pos = _me.Pos;
            p.ShapeshiftForm = _form;
            AbFormBarFirstFill();                      // iteration 11 (vrcast hook, OfflineSession.ActionBars.cs): a form's bar is filled the first time
            p.Stealthed = _me.HasAuraType(SpellAura.ModStealth);
            RefreshAvatarLook(p);                      // iteration 11 (avatar hook, OfflineSession.Avatar.cs): the avatar's form / look
            float mn, mx, time;
            MeleeDamageRange(false, out mn, out mx, out time);
            p.SwingProgress = time > 0 ? Math.Max(0f, Math.Min(1f, 1f - _swingTimer / time)) : 1f;
            StepImmersiveBuff();   // stage S1 (PLAN10 s5): the "Immersive Mode" buff
            if (_aurasChanged)
            {
                _aurasChanged = false;
                p.Auras.Clear();
                for (int a = 0; a < _me.Auras.Count; a++) if (!_me.Auras[a].Hidden && !_me.Auras[a].Spell.IsPassive) p.Auras.Add(_me.Auras[a].View);
                if (_eating != null) p.Auras.Add(_eatView);
                if (_immersiveBuffShown) p.Auras.Add(_immersiveView);   // stage S1: our own buff, last in the row
            }
        }
    }
}
