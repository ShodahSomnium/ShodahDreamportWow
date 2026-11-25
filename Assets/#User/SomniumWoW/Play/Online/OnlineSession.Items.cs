using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Stage O2, owner items (PLAN9 s2): the player's items from the server, like the real client.
    //  - Item / container objects (SMSG_UPDATE_OBJECT TYPEID_ITEM 1 / TYPEID_CONTAINER 2) and our own PLAYER_FIELD_INV_SLOT_HEAD
    //    (+ slot * 2, AC PlayerStorage.cpp:2747 "SetGuidValue(PLAYER_FIELD_INV_SLOT_HEAD + (slot * 2), pItem->GetGUID())"; slots 0-18
    //    equipment, 19-22 bags, 23-38 the backpack = PLAYER_FIELD_PACK_SLOT_1) and CONTAINER_FIELD_SLOT_1 + i * 2 (Bag.cpp:161) fill
    //    PlayerState.Equipment / Bags / FreeBagSlots. Rebuilt only when an update packet arrived (a second SMSG_UPDATE_OBJECT /
    //    SMSG_DESTROY_OBJECT handler that just marks the inventory dirty; the objects are applied by OnlineSession.Objects first).
    //  - Names, quality, stats, spells: CMSG_ITEM_QUERY_SINGLE / SMSG_ITEM_QUERY_SINGLE_RESPONSE (ItemHandler.cpp
    //    HandleItemQuerySingleOpcode); models + icons at once from the client's Item.dbc / ItemDisplayInfo.dbc.
    //  - EquipItem = CMSG_AUTOEQUIP_ITEM, UseItem = CMSG_USE_ITEM (or CMSG_OPEN_ITEM / CMSG_QUESTGIVER_QUERY_QUEST / equip, the real
    //    client's choice by the item's data), SMSG_INVENTORY_CHANGE_FAILURE -> Error with WoW's text, SMSG_ITEM_PUSH_RESULT -> LootReceived.
    // Loot, quests and vendors show items through ItemInfoView(entry, count) (internal; the same ItemView the bags show).
    public sealed partial class OnlineSession
    {
        // ---- AzerothCore values (quoted)
        const int InvBag0 = 255;                         // INVENTORY_SLOT_BAG_0 (Player.h:657)
        const int EquipSlotEnd = 19;                     // EQUIPMENT_SLOT_END (Player.h:681)
        const int BagSlotStart = 19, BagSlotEnd = 23;    // INVENTORY_SLOT_BAG_START / END (Player.h:686-687)
        const int PackSlotStart = 23, PackSlotEnd = 39;  // INVENTORY_SLOT_ITEM_START / END (Player.h:692-693)
        const int MaxBagSize = 36;                       // CONTAINER_FIELD_SLOT_1 Size 72 (UpdateFields.h) = 36 guids
        const uint ItemFlagConjured = 0x2, ItemFlagHasLoot = 0x4, ItemFlagUniqueEquippable = 0x80000;   // ItemTemplate.h ItemFlags
        const uint ItemFlagNoUserDestroy = 0x20, ItemFlagPetition = 0x2000;   // ITEM_FLAG_NO_USER_DESTROY (ItemTemplate.h:152), ITEM_FLAG_PETITION (:160) - iteration 11 (npc)
        const uint ItemFieldFlagSoulbound = 0x1;         // ItemTemplate.h ItemFieldFlags ITEM_FIELD_FLAG_SOULBOUND
        const int KeyringSlots = 32;                     // KEYRING_SLOT_END - KEYRING_SLOT_START (Player.h:717-718, :1378 GetMaxKeyringSize) - iteration 11 (npc)
        const int BagFamilyMaskKeys = 0x100;             // BAG_FAMILY_MASK_KEYS (ItemTemplate.h:235): what the keyring accepts - iteration 11 (npc)
        const int BankItemSlots = 28;                    // BANK_SLOT_ITEM_START 39 .. END 67 (Player.h:698) - iteration 11 (npc)
        const int PlayerSlotEnd = 150;                   // PLAYER_SLOT_END (Player.h:653): bag-255 slots 0..149 - iteration 11 (npc)
        const double InvLockSeconds = 2.0;               // ours (iteration 11, npc): a moved / sold / destroyed item un-greys after this without an answer
        const int TriggerOnUse = 0, TriggerOnEquip = 1, TriggerChanceOnHit = 2, TriggerSoulstone = 4, TriggerNoDelayUse = 5;   // ItemSpelltriggerType
        const int ItemClassWeapon = 2, ItemClassArmor = 4, ItemClassQuest = 12;                         // ItemClass
        const int MaxProtoStats = 10, MaxProtoSpells = 5, MaxProtoDamages = 2, MaxProtoSockets = 3;     // MAX_ITEM_PROTO_* (ItemTemplate.h:613-616)
        const uint ItemQueryUnknown = 0x80000000u;       // "queryData << uint32(item | 0x80000000)" (ItemHandler.cpp:543)

        // ---- ours (named here, logged once in InitItems)
        const double ItemQuerySpacing = 0.04;            // s between two CMSG_ITEM_QUERY_SINGLE (25 a second; the real client sends them at once)
        const double ItemInfoWait = 2.5;                 // s a LootReceived / a deferred UseItem waits for the item's name, then goes on without it
        const double EquipWatchSeconds = 3.0;            // s after an EquipItem during which the item showing up equipped says "Equipped <name>" (offline's line)

        // ---- the tooltip DBCs (ItemSubClass, ItemLimitCategory, random suffixes, sets...) live in Play/WowItemTooltip.cs ItemTipDb

        /// <summary>SMSG_ITEM_QUERY_SINGLE_RESPONSE, the fields the views and the use logic need.</summary>
        sealed class ItemProto
        {
            public int Entry;
            public bool Missing;                          // the server answered entry | 0x80000000: no such item
            public bool Truncated;                        // the answer ended early (logged): what was read is kept
            public int Class, SubClass, DisplayId, Quality, InventoryType, AllowableClass, AllowableRace, ItemLevel, RequiredLevel;
            public uint Flags, Flags2;
            public int BuyPrice, SellPrice, MaxCount, Stackable, ContainerSlots;
            public string Name = "", Description = "";
            public int StatCount;
            public readonly int[] StatType = new int[MaxProtoStats], StatValue = new int[MaxProtoStats];
            public readonly float[] DmgMin = new float[MaxProtoDamages], DmgMax = new float[MaxProtoDamages];
            public readonly int[] DmgType = new int[MaxProtoDamages];
            public int Armor, Delay, Bonding, PageText, StartQuest, LockId, Sheath, Block, MaxDurability, Duration, ItemLimitCategory;
            public int PageMaterial, BagFamily;           // iteration 11 (npc): PageTextMaterial.dbc id, ItemBagFamily mask
            public int TotemCategory, RequiredSkill, RequiredSkillRank, RequiredDisenchantSkill;   // iteration 11 (trainer I1)
            public readonly int[] Res = new int[6];       // Holy, Fire, Nature, Frost, Shadow, Arcane
            public readonly int[] SpellId = new int[MaxProtoSpells], SpellTrigger = new int[MaxProtoSpells], SpellCharges = new int[MaxProtoSpells];
            // item tooltip fix (out11_tooltip): the rest of what the 3.3.5a tooltip draws (WowItemTooltip.cs)
            public int RequiredSpell, RequiredReputationFaction, RequiredReputationRank, ScalingStatDistribution, ScalingStatValue;
            public int RandomProperty, RandomSuffix, ItemSet, SocketBonus, GemProperties;
            public readonly int[] SpellCooldown = new int[MaxProtoSpells], SpellCategoryCooldown = new int[MaxProtoSpells];
            public readonly int[] SocketColor = new int[MaxProtoSockets];
            ItemTipData _tip;

            /// <summary>The tooltip's template data (built once per answer).</summary>
            public ItemTipData Tip
            {
                get
                {
                    if (_tip != null) return _tip;
                    var t = new ItemTipData
                    {
                        Entry = Entry, Class = Class, SubClass = SubClass, Quality = Quality, InventoryType = InventoryType, AllowableClass = AllowableClass,
                        AllowableRace = AllowableRace, ItemLevel = ItemLevel, RequiredLevel = RequiredLevel, Flags = Flags, Flags2 = Flags2, Name = Name ?? "",
                        Description = Description ?? "", RequiredSkill = RequiredSkill, RequiredSkillRank = RequiredSkillRank, RequiredSpell = RequiredSpell,
                        RequiredReputationFaction = RequiredReputationFaction, RequiredReputationRank = RequiredReputationRank, MaxCount = MaxCount,
                        ContainerSlots = ContainerSlots, SellPrice = SellPrice, StatCount = StatCount, ScalingStatDistribution = ScalingStatDistribution,
                        ScalingStatValue = ScalingStatValue, Armor = Armor, Delay = Delay, Block = Block, Bonding = Bonding, PageText = PageText,
                        StartQuest = StartQuest, RandomProperty = RandomProperty, RandomSuffix = RandomSuffix, ItemSet = ItemSet, MaxDurability = MaxDurability,
                        Duration = Duration, ItemLimitCategory = ItemLimitCategory, RequiredDisenchantSkill = RequiredDisenchantSkill, SocketBonus = SocketBonus,
                    };
                    Array.Copy(StatType, t.StatType, MaxProtoStats); Array.Copy(StatValue, t.StatValue, MaxProtoStats);
                    Array.Copy(DmgMin, t.DmgMin, MaxProtoDamages); Array.Copy(DmgMax, t.DmgMax, MaxProtoDamages); Array.Copy(DmgType, t.DmgType, MaxProtoDamages);
                    Array.Copy(Res, t.Res, 6);
                    Array.Copy(SpellId, t.SpellId, MaxProtoSpells); Array.Copy(SpellTrigger, t.SpellTrigger, MaxProtoSpells); Array.Copy(SpellCharges, t.SpellCharges, MaxProtoSpells);
                    Array.Copy(SpellCooldown, t.SpellCooldown, MaxProtoSpells); Array.Copy(SpellCategoryCooldown, t.SpellCategoryCooldown, MaxProtoSpells);
                    Array.Copy(SocketColor, t.SocketColor, MaxProtoSockets);
                    _tip = t;
                    return t;
                }
            }

            /// <summary>Player::CastItemUseSpell casts only ITEM_SPELLTRIGGER_ON_USE spells (Player.cpp: "if (spellData.SpellTrigger != ITEM_SPELLTRIGGER_ON_USE) continue;").</summary>
            public int UseSpell
            {
                get
                {
                    for (int i = 0; i < MaxProtoSpells; i++) if (SpellId[i] > 0 && SpellTrigger[i] == TriggerOnUse) return SpellId[i];
                    return 0;
                }
            }
        }

        /// <summary>The ItemView of one of our item objects, rebuilt only when what it shows changed (views compare by reference).</summary>
        sealed class OwnedView
        {
            public int Entry, Count, Durability, MaxDurability;
            public uint FieldFlags;
            public ItemProto Proto;
            public ItemView View;
            public bool Seen;
            public int InstSig, SetSig, Version;          // item tooltip fix: the roll / enchantments, the equipped set pieces, the answers seen
            public bool Incomplete;                       // a set piece / the creator had no name yet
        }

        /// <summary>A view handed out before its data was complete, filled again when answers arrive (with its own roll, if any).</summary>
        struct TipWaiting { public ItemView View; public ItemTipInstance Inst; }

        struct BagPos { public byte Bag, Slot; public ulong Guid; }
        struct PendingPush { public int Entry, Count, RandomProp; public double Until; }

        readonly Dictionary<int, ItemProto> _itemProtos = new Dictionary<int, ItemProto>();
        readonly HashSet<int> _itemAsked = new HashSet<int>();
        readonly Queue<int> _itemQueryQueue = new Queue<int>();
        readonly Dictionary<int, List<TipWaiting>> _itemViewsWaiting = new Dictionary<int, List<TipWaiting>>();
        readonly List<TipWaiting> _tipIncomplete = new List<TipWaiting>();   // filled views still missing a set piece / creator name
        readonly HashSet<int> _equippedEntries = new HashSet<int>();         // the entries we wear (item sets' n/N), rebuilt with the inventory
        int _equippedSig;
        ItemTipContext _tipCtx;
        Func<int, int> _tipSkillFn;
        bool _tipWaitsName;
        readonly Dictionary<ulong, OwnedView> _ownedViews = new Dictionary<ulong, OwnedView>();
        readonly List<ulong> _ownedDrop = new List<ulong>();
        readonly List<ItemView> _bagViews = new List<ItemView>();
        readonly List<BagPos> _bagPos = new List<BagPos>();
        readonly List<PendingPush> _pendingPushes = new List<PendingPush>();
        readonly HashSet<int> _pushOddLogged = new HashSet<int>();
        DbcTable _itemDbcI, _itemDisplayDbcI;
        bool _itemsDirty, _itemsFirstLogged, _itemsReadyLogged, _itemDbcTried, _itemQueryOddLogged, _itemFailOddLogged;
        int _equipSig;
        double _nextItemQueryAt;
        int _itemQueriesSent, _itemAnswers, _itemMissing, _itemUnresolvedSlots;
        byte _itemCastCount;
        ulong _pendingUseGuid; double _pendingUseUntil = -1;
        ulong _equipWatchGuid; double _equipWatchUntil = -1;
        // iteration 11 (npc): where every owned item is (bag << 8 | slot, rebuilt with the inventory) and the items waiting for the server
        readonly Dictionary<ulong, int> _invPosOf = new Dictionary<ulong, int>();
        struct InvLockState { public int Pos, Count; public double Until; }
        readonly Dictionary<ulong, InvLockState> _itemLocks = new Dictionary<ulong, InvLockState>();
        readonly List<ulong> _invLockDrop = new List<ulong>();
        // a partial destroy watched until the stack shrinks (ItemDestroyed)
        sealed class InvDestroyWatch { public ulong Guid; public int Entry, Count, Before; public string Name; public double Until; }
        readonly List<InvDestroyWatch> _invDestroys = new List<InvDestroyWatch>();

        void InitItems()
        {
            if (_world == null) return;
            // registered after RegisterObjectHandlers: WorldClient runs several handlers per opcode in order, so the objects are applied first
            _world.On(Op.SMSG_UPDATE_OBJECT, p => _itemsDirty = true, this);
            _world.On(Op.SMSG_DESTROY_OBJECT, p => _itemsDirty = true, this);
            _world.On(Op.SMSG_ITEM_QUERY_SINGLE_RESPONSE, OnItemQueryResponse, this);
            _world.On(Op.SMSG_INVENTORY_CHANGE_FAILURE, OnInventoryChangeFailure, this);
            _world.On(Op.SMSG_ITEM_PUSH_RESULT, OnItemPushResult, this);
            _world.On(Op.SMSG_NAME_QUERY_RESPONSE, p => { if (_tipWaitsName) { _tipWaitsName = false; TipAnswersArrived(); } }, this);   // after Objects.cs stored the name: "<Made by %s>"
            WowLog.Info("Online items: ours - item query spacing " + ItemQuerySpacing + " s, item info wait " + ItemInfoWait + " s, equip watch " + EquipWatchSeconds + " s");
        }

        void TickItems(float dt)
        {
            if (_world == null) return;
            // item queries, paced
            int sent = 0;
            while (_itemQueryQueue.Count > 0 && _now >= _nextItemQueryAt && sent < 4)
            {
                int entry = _itemQueryQueue.Dequeue();
                if (_itemProtos.ContainsKey(entry)) continue;
                _world.Send(Op.CMSG_ITEM_QUERY_SINGLE, new PacketWriter().U32((uint)entry));   // HandleItemQuerySingleOpcode: "recvData >> item" (uint32 only)
                _itemQueriesSent++;
                _nextItemQueryAt = (_nextItemQueryAt > _now - ItemQuerySpacing ? _nextItemQueryAt : _now) + ItemQuerySpacing;
                sent++;
            }
            if (_itemsDirty)
            {
                _itemsDirty = false;
                RebuildInventory();
            }
            // item pushes waiting for the item's name
            for (int i = 0; i < _pendingPushes.Count; i++)
            {
                var pp = _pendingPushes[i];
                if (!_itemProtos.ContainsKey(pp.Entry) && _now < pp.Until) continue;
                EmitLootReceived(pp.Entry, pp.Count, pp.RandomProp);
                _pendingPushes.RemoveAt(i);
                i--;
            }
            if (_pendingUseGuid != 0 && _now > _pendingUseUntil)
            {
                WowLog.Info("Online items: no item information for the item to use within " + ItemInfoWait + " s - not used");
                _pendingUseGuid = 0;
                Error("No answer from the server");                // ours (the same words as the spells' cast timeout)
            }
            if (_equipWatchGuid != 0 && _now > _equipWatchUntil) _equipWatchGuid = 0;
            InvTickLocks();                                          // iteration 11 (npc)
        }

        // ------------------------------------------------------------------ inventory (the player's own objects)

        void RebuildInventory()
        {
            var me = _self;
            if (me == null) return;                              // teleport gap (ClearWorld until our create block is back): keep the last views
            foreach (var ov in _ownedViews.Values) ov.Seen = false;
            _itemUnresolvedSlots = 0;
            _invPosOf.Clear();
            bool changed = false;
            // the entries we wear first: item set tooltips count them (ITEM_SET_NAME "%s (%d/%d)")
            _equippedEntries.Clear();
            int esig = 17;
            for (int s = 0; s < EquipSlotEnd; s++)
            {
                ulong g = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + s * 2);
                OnlineObject eo;
                int e = g != 0 && _objects.TryGetValue(g, out eo) ? eo.Entry : 0;
                if (e > 0) _equippedEntries.Add(e);
                esig = esig * 31 + e;
            }
            _equippedSig = esig;
            // equipment
            for (int s = 0; s < EquipSlotEnd; s++)
            {
                ulong g = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + s * 2);
                ItemView v = g != 0 ? InvViewAt(g, InvBag0, s) : null;
                if (!ReferenceEquals(_player.Equipment[s], v))
                {
                    _player.Equipment[s] = v;
                    changed = true;
                    if (v != null && g == _equipWatchGuid && _now <= _equipWatchUntil)
                    {
                        _equipWatchGuid = 0;
                        _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = "Equipped " + v.Name });   // offline's line (OfflineSession.Items EquipItem)
                    }
                }
            }
            // iteration 11 (npc): every container (backpack, the 4 bags, keyring, bank, bank bags), the buyback slots and the currencies;
            // views carry their (Bag, Slot) - proposal-npc.md 6.4
            InvRebuildContainers(me);
            // the backpack, then the bags in their slots (the flat list Player.Bags keeps for EquipItem / UseItem(bagIndex) and the other
            // areas' bag indices; FreeBagSlots counts every empty slot) - read from the containers just built
            _bagViews.Clear();
            _bagPos.Clear();
            int free = 0;
            for (int c = 0; c < _player.Containers.Count && c < 5; c++)
            {
                var cv = _player.Containers[c];
                for (int i = 0; i < cv.Size; i++)
                {
                    ulong g = GuidAt(cv.Bag, cv.SlotBase + i);
                    if (g == 0) { free++; continue; }
                    var v = cv.Slots[i];
                    if (v == null) continue;                          // its object is not known (yet)
                    _bagViews.Add(v);
                    _bagPos.Add(new BagPos { Bag = (byte)cv.Bag, Slot = (byte)(cv.SlotBase + i), Guid = g });
                }
            }
            var bags = _player.Bags;
            bool bagsSame = bags.Count == _bagViews.Count;
            for (int i = 0; bagsSame && i < bags.Count; i++) if (!ReferenceEquals(bags[i], _bagViews[i])) bagsSame = false;
            if (!bagsSame)
            {
                bags.Clear();
                bags.AddRange(_bagViews);
                changed = true;
            }
            if (_player.FreeBagSlots != free) { _player.FreeBagSlots = free; changed = true; }
            // forget views of items that left the inventory
            _ownedDrop.Clear();
            foreach (var kv in _ownedViews) if (!kv.Value.Seen) _ownedDrop.Add(kv.Key);
            for (int i = 0; i < _ownedDrop.Count; i++) _ownedViews.Remove(_ownedDrop[i]);
            InvCheckLocks();
            InvCheckDestroys();
            if (changed) LogInventory();
        }

        // ------------------------------------------------------------------ iteration 11 (npc): containers and positions (proposal-npc.md 3.5 / 6.4)

        /// <summary>The view of our item at (bag, slot) - also records the position and the lock state on the (shared) view.</summary>
        ItemView InvViewAt(ulong g, int bag, int slot)
        {
            _invPosOf[g] = bag << 8 | slot;
            ItemView v = OwnedViewOf(g);
            if (v != null)
            {
                v.Bag = bag;
                v.Slot = slot;
                v.Locked = _itemLocks.ContainsKey(g);
            }
            return v;
        }

        /// <summary>
        /// The item guid at a WoW position (Player.h:657-724): bag 255 -> our PLAYER_FIELD_INV_SLOT_HEAD + slot * 2 (contiguous for slots
        /// 0..149, UpdateFields.cs:301-307); bag 19..22 / 67..73 -> that bag object's CONTAINER_FIELD_SLOT_1 + slot * 2 (slot &lt; NUM_SLOTS).
        /// </summary>
        ulong GuidAt(int bag, int slot)
        {
            var me = _self;
            if (me == null || slot < 0) return 0;
            if (bag == InvBag0) return slot < PlayerSlotEnd ? me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + slot * 2) : 0;
            if (!((bag >= InvPos.BagSlotStart && bag < InvPos.BagSlotEnd) || (bag >= InvPos.BankBagStart && bag < InvPos.BankBagEnd))) return 0;
            ulong bg = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + bag * 2);
            OnlineObject b;
            if (bg == 0 || !_objects.TryGetValue(bg, out b) || b.TypeId != TypeId.Container) return 0;
            int n = Math.Min(MaxBagSize, Math.Max(0, b.I32(UF.CONTAINER_FIELD_NUM_SLOTS)));
            return slot < n ? b.U64(UF.CONTAINER_FIELD_SLOT_1 + slot * 2) : 0;
        }

        /// <summary>Player.Containers [backpack, bags 19..22], Keyring, Bank [28 slots, bank bags 67..73], Buyback, BankBagSlotsBought /
        /// NextBankSlotCost, HonorPoints / ArenaPoints. ContainerView instances are reused; a Slots array is reallocated only when the
        /// size changes; a slot holds the same ItemView instance while the item is unchanged.</summary>
        void InvRebuildContainers(OnlineObject me)
        {
            var p = _player;
            int bought = me.Byte(UF.PLAYER_BYTES_2, 2);                                          // PLAYER_BYTES_2_OFFSET_BANK_BAG_SLOTS (Player.h:507)
            p.BankBagSlotsBought = bought;
            p.NextBankSlotCost = bought < BankBagSlotsMax ? BankSlotPrice(bought + 1) : 0;
            p.HonorPoints = Math.Max(0, me.I32(UF.PLAYER_FIELD_HONOR_CURRENCY));
            p.ArenaPoints = Math.Max(0, me.I32(UF.PLAYER_FIELD_ARENA_CURRENCY));
            while (p.Containers.Count < 5) p.Containers.Add(new ContainerView());
            InvFillSlots(p.Containers[0], me, InvBag0, PackSlotStart, PackSlotEnd - PackSlotStart, 0);
            p.Containers[0].InvSlot = -1;
            p.Containers[0].BagItem = null;
            for (int b = 0; b < 4; b++) InvFillBag(p.Containers[1 + b], me, BagSlotStart + b, false);
            // the keyring: WoW shows GetKeyRingSize() slots (ContainerFrame.lua:877-901)
            if (p.Keyring == null) p.Keyring = new ContainerView();
            int maxFilled = 0, keys = 0;
            for (int i = 0; i < KeyringSlots; i++)
                if (me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + (InvPos.KeyringStart + i) * 2) != 0) { keys++; maxFilled = i + 1; }
            int mod = maxFilled % 4;
            int ksize = mod == 0 && keys < maxFilled ? maxFilled : maxFilled + (4 - mod);
            InvFillSlots(p.Keyring, me, InvBag0, InvPos.KeyringStart, Math.Min(ksize, KeyringSlots), BagFamilyMaskKeys);
            // the bank: 28 slots, then the 7 bank bag slots (Locked = not bought yet)
            while (p.Bank.Count < 1 + BankBagSlotsMax) p.Bank.Add(new ContainerView());
            InvFillSlots(p.Bank[0], me, InvBag0, InvPos.BankStart, BankItemSlots, 0);
            p.Bank[0].InvSlot = -1;
            for (int k = 0; k < BankBagSlotsMax; k++) InvFillBag(p.Bank[1 + k], me, InvPos.BankBagStart + k, k >= bought);
            // buyback 74..85: PLAYER_FIELD_VENDORBUYBACK_SLOT_1 = INV_SLOT_HEAD + 74 * 2, PLAYER_FIELD_BUYBACK_PRICE_1 / _TIMESTAMP_1 + i
            int newest = -1; uint newestStamp = 0;
            for (int i = 0; i < VendBuybackSlots; i++)
            {
                ulong g = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + (VendBuybackStart + i) * 2);
                ItemView v = g != 0 ? InvViewAt(g, InvBag0, VendBuybackStart + i) : null;
                p.Buyback[i] = v;
                p.BuybackPrice[i] = v != null ? Math.Max(0, me.I32(UF.PLAYER_FIELD_BUYBACK_PRICE_1 + i)) : 0;
                uint stamp = me.U32(UF.PLAYER_FIELD_BUYBACK_TIMESTAMP_1 + i);
                if (v != null && (newest < 0 || stamp > newestStamp)) { newest = i; newestStamp = stamp; }
            }
            p.LastBuyback = newest;
        }

        /// <summary>A run of bag-255 slots (backpack 23.., bank 39.., keyring 86..) as one container.</summary>
        void InvFillSlots(ContainerView c, OnlineObject me, int bag, int slotBase, int size, int family)
        {
            c.Bag = bag;
            c.SlotBase = slotBase;
            c.Family = family;
            c.Locked = false;
            if (c.Slots == null || c.Slots.Length != size) c.Slots = new ItemView[size];
            c.Size = size;
            int free = 0;
            for (int i = 0; i < size; i++)
            {
                ulong g = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + (slotBase + i) * 2);
                if (g == 0) free++;
                c.Slots[i] = g != 0 ? InvViewAt(g, bag, slotBase + i) : null;
            }
            c.Free = free;
        }

        /// <summary>A bag slot (19..22 or bank 67..73): the bag item and its contents (Bag = the bag slot, SlotBase 0).</summary>
        void InvFillBag(ContainerView c, OnlineObject me, int bagSlot, bool locked)
        {
            c.Bag = bagSlot;
            c.SlotBase = 0;
            c.InvSlot = bagSlot;
            c.Locked = locked;
            ulong bg = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + bagSlot * 2);
            OnlineObject bag = null;
            c.BagItem = bg != 0 ? InvViewAt(bg, InvBag0, bagSlot) : null;
            if (bg != 0 && (!_objects.TryGetValue(bg, out bag) || bag.TypeId != TypeId.Container)) { bag = null; _itemUnresolvedSlots++; }
            int size = bag != null ? Math.Min(MaxBagSize, Math.Max(0, bag.I32(UF.CONTAINER_FIELD_NUM_SLOTS))) : 0;
            ItemProto bp;
            c.Family = bag != null && _itemProtos.TryGetValue(bag.Entry, out bp) && !bp.Missing ? bp.BagFamily : 0;
            if (c.Slots == null || c.Slots.Length != size) c.Slots = new ItemView[size];
            c.Size = size;
            int free = 0;
            for (int i = 0; i < size; i++)
            {
                ulong g = bag.U64(UF.CONTAINER_FIELD_SLOT_1 + i * 2);
                if (g == 0) free++;
                c.Slots[i] = g != 0 ? InvViewAt(g, bagSlot, i) : null;
            }
            c.Free = free;
        }

        /// <summary>Our item's current position (bag &lt;&lt; 8 | slot), -1 = not in the inventory.</summary>
        int InvPosOf(ulong g)
        {
            int pos;
            return g != 0 && _invPosOf.TryGetValue(g, out pos) ? pos : -1;
        }

        /// <summary>Greys the item while the server decides (ItemView.Locked): until its position or count changes, any
        /// SMSG_INVENTORY_CHANGE_FAILURE (incl. AC's EQUIP_ERR_NONE release), or InvLockSeconds (ours).</summary>
        void InvLock(ulong g)
        {
            OnlineObject o;
            if (g == 0 || !_objects.TryGetValue(g, out o)) return;
            _itemLocks[g] = new InvLockState { Pos = InvPosOf(g), Count = o.I32(UF.ITEM_FIELD_STACK_COUNT), Until = _now + InvLockSeconds };
            OwnedView ov;
            if (_ownedViews.TryGetValue(g, out ov) && ov.View != null) ov.View.Locked = true;
        }

        void InvUnlock(ulong g)
        {
            if (g == 0 || !_itemLocks.Remove(g)) return;
            OwnedView ov;
            if (_ownedViews.TryGetValue(g, out ov) && ov.View != null) ov.View.Locked = false;
        }

        void InvUnlockAll()
        {
            if (_itemLocks.Count == 0) return;
            _invLockDrop.Clear();
            _invLockDrop.AddRange(_itemLocks.Keys);
            foreach (var g in _invLockDrop) InvUnlock(g);
        }

        void InvTickLocks()
        {
            if (_itemLocks.Count == 0) return;
            _invLockDrop.Clear();
            foreach (var kv in _itemLocks) if (_now > kv.Value.Until) _invLockDrop.Add(kv.Key);
            foreach (var g in _invLockDrop) InvUnlock(g);
        }

        /// <summary>After a rebuild: a locked item that moved, changed count or left the inventory is released.</summary>
        void InvCheckLocks()
        {
            if (_itemLocks.Count == 0) return;
            _invLockDrop.Clear();
            foreach (var kv in _itemLocks)
            {
                OnlineObject o;
                int pos = InvPosOf(kv.Key);
                if (pos < 0 || pos != kv.Value.Pos || !_objects.TryGetValue(kv.Key, out o) || o.I32(UF.ITEM_FIELD_STACK_COUNT) != kv.Value.Count) _invLockDrop.Add(kv.Key);
            }
            foreach (var g in _invLockDrop) InvUnlock(g);
        }

        /// <summary>ItemDestroyed once a destroyed item left its slot or its stack shrank (CMSG_DESTROYITEM has no success answer).</summary>
        void InvCheckDestroys()
        {
            for (int i = 0; i < _invDestroys.Count; i++)
            {
                var w = _invDestroys[i];
                OnlineObject o = null;
                bool gone = InvPosOf(w.Guid) < 0 || !_objects.TryGetValue(w.Guid, out o);
                int left = gone ? 0 : Math.Max(1, o.I32(UF.ITEM_FIELD_STACK_COUNT));
                if (!gone && left >= w.Before && _now <= w.Until) continue;
                if (gone || left < w.Before)
                {
                    int n = gone ? w.Before : w.Before - left;
                    _events.Enqueue(new GameEvent { Type = GameEventType.ItemDestroyed, ItemEntry = w.Entry, Amount = n, Text = w.Name });
                    WowLog.Info("Online items: destroyed '" + w.Name + "' x" + n);
                }
                else WowLog.Info("Online items: the destroy of '" + w.Name + "' got no answer within " + InvLockSeconds + " s");
                _invDestroys.RemoveAt(i);
                i--;
            }
        }

        /// <summary>The home changed (SMSG_BINDPOINTUPDATE): every owned item view is rebuilt with the new tooltips (the Hearthstone's $z).</summary>
        void InvRebuildTooltips()
        {
            foreach (var ov in _ownedViews.Values) ov.Proto = null;                            // forces a new view in OwnedViewOf
            _itemsDirty = true;
        }

        /// <summary>An item or a player name answer: views that were missing a set piece / creator name are built again.</summary>
        void TipAnswersArrived()
        {
            _itemsDirty = true;                                                                // owned views: OwnedViewOf compares Version
            if (_tipIncomplete.Count == 0) return;
            var list = new List<TipWaiting>(_tipIncomplete);
            _tipIncomplete.Clear();
            foreach (var w in list)
            {
                ItemProto proto;
                if (_itemProtos.TryGetValue(w.View.Entry, out proto)) FillItemView(w.View, w.View.Entry, w.View.Count, proto, w.Inst);
            }
        }

        /// <summary>The item's own state from its object: random property / suffix, enchantments, creator, charges, durability.</summary>
        ItemTipInstance InstanceOf(OnlineObject o)
        {
            var t = new ItemTipInstance { Owned = true };
            t.Durability = o.I32(UF.ITEM_FIELD_DURABILITY);
            t.MaxDurability = o.I32(UF.ITEM_FIELD_MAXDURABILITY);
            t.FieldFlags = o.U32(UF.ITEM_FIELD_FLAGS);
            t.RandomPropertyId = o.I32(UF.ITEM_FIELD_RANDOM_PROPERTIES_ID);                   // Item::SetItemRandomProperties (Item.cpp:669-705)
            t.SuffixFactor = o.I32(UF.ITEM_FIELD_PROPERTY_SEED);                               // UpdateItemSuffixFactor (Item.cpp:707-713)
            for (int s = 0; s < t.Enchant.Length; s++) t.Enchant[s] = o.I32(UF.ITEM_FIELD_ENCHANTMENT_1_1 + s * 3);   // "ITEM_FIELD_ENCHANTMENT_1_1 + slot * MAX_ENCHANTMENT_OFFSET" (Item.cpp:937)
            t.TempEnchantMs = o.I32(UF.ITEM_FIELD_ENCHANTMENT_1_1 + 1 * 3 + 1);              // TEMP_ENCHANTMENT_SLOT + ENCHANTMENT_DURATION_OFFSET (Item.cpp:938)
            t.Creator = o.U64(UF.ITEM_FIELD_CREATOR);
            for (int i = 0; i < MaxProtoSpells; i++) t.SpellCharges[i] = o.I32(UF.ITEM_FIELD_SPELL_CHARGES + i);
            t.HasCharges = true;
            t.DurationLeft = o.I32(UF.ITEM_FIELD_DURATION);
            return t;
        }

        /// <summary>Who the tooltip is for (red requirement lines, set pieces, names): one context, refreshed per build.</summary>
        ItemTipContext TipContext()
        {
            if (_tipCtx == null)
            {
                _tipCtx = new ItemTipContext();
                _tipSkillFn = id =>
                {
                    var sk = _player.Skills;
                    for (int i = 0; i < sk.Count; i++) if (sk[i].SkillId == id) return sk[i].Value;
                    return -1;
                };
                _tipCtx.ReputationRank = id =>
                {
                    var reps = _player.Reputations;
                    if (reps != null) for (int i = 0; i < reps.Count; i++) if (reps[i].FactionId == id && reps[i].HasRep) return reps[i].Rank;
                    return -1;
                };
                _tipCtx.KnowsSpell = id => _known.Count == 0 || _known.Contains(id);
                _tipCtx.ItemName = e => { ItemProto ip; return _itemProtos.TryGetValue(e, out ip) && ip.Missing ? "" : ItemInfoName(e); };   // asks the server when unknown; "" = it has none
                _tipCtx.IsEquipped = e => _equippedEntries.Contains(e);
                _tipCtx.SpellDescription = SpellDescription;
                _tipCtx.PlayerName = g =>
                {
                    string n;
                    if (g == _player.Guid && !string.IsNullOrEmpty(_player.Name)) return _player.Name;
                    if (_playerNames.TryGetValue(g, out n) && !string.IsNullOrEmpty(n)) return n;
                    if (_world != null && _nameAsked.Add(g)) _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(g));   // NameQuery::Read: ObjectGuid
                    _tipWaitsName = true;
                    return null;
                };
            }
            _tipCtx.SkillValue = _player.Skills != null && _player.Skills.Count > 0 ? _tipSkillFn : null;   // no skills yet: drawn as met
            _tipCtx.Level = Math.Max(1, _player.Level);
            _tipCtx.ClassId = (int)_player.Class;
            _tipCtx.Race = _player.Race;
            return _tipCtx;
        }

        /// <summary>The view of one of our items by guid (null = its object is unknown and never was).</summary>
        ItemView OwnedViewOf(ulong guid)
        {
            OnlineObject o;
            OwnedView ov;
            _ownedViews.TryGetValue(guid, out ov);
            if (!_objects.TryGetValue(guid, out o) || (o.TypeId != TypeId.Item && o.TypeId != TypeId.Container))
            {
                _itemUnresolvedSlots++;
                if (ov == null) return null;
                ov.Seen = true;                                   // keep what we showed until its object is back
                return ov.View;
            }
            int entry = o.Entry;
            int count = Math.Max(1, o.I32(UF.ITEM_FIELD_STACK_COUNT));
            var inst = InstanceOf(o);
            int dur = inst.Durability, maxDur = inst.MaxDurability;
            uint fflags = inst.FieldFlags;
            int isig = inst.Signature();
            ItemProto proto;
            _itemProtos.TryGetValue(entry, out proto);
            if (proto == null) AskItem(entry);
            int setSig = proto != null && proto.ItemSet > 0 ? _equippedSig : 0;                 // a set piece: its (n/N) and bonuses follow what we wear
            int version = _itemAnswers * 7919 + _playerNames.Count;
            if (ov != null && ov.Entry == entry && ov.Count == count && ov.Durability == dur && ov.MaxDurability == maxDur && ov.FieldFlags == fflags && ReferenceEquals(ov.Proto, proto)
                && ov.InstSig == isig && ov.SetSig == setSig && (!ov.Incomplete || ov.Version == version))
            {
                ov.Seen = true;
                return ov.View;
            }
            if (ov == null) { ov = new OwnedView(); _ownedViews[guid] = ov; }
            ov.Entry = entry; ov.Count = count; ov.Durability = dur; ov.MaxDurability = maxDur; ov.FieldFlags = fflags; ov.Proto = proto; ov.Seen = true;
            ov.InstSig = isig; ov.SetSig = setSig; ov.Version = version;
            ov.View = BuildItemView(entry, count, proto, inst);
            ov.Incomplete = _tipCtx != null && _tipCtx.Incomplete && proto != null && !proto.Missing;
            return ov.View;
        }

        /// <summary>
        /// Three kinds of log line: the first inventory from the server, "inventory ready" once every item has its name (the test
        /// guide's line: the equipment with names and displays), and afterwards one line whenever the equipment changes.
        /// </summary>
        void LogInventory()
        {
            int eq = 0, sig = 17;
            for (int i = 0; i < _player.Equipment.Length; i++)
            {
                var v = _player.Equipment[i];
                if (v != null) eq++;
                sig = sig * 31 + (v != null ? v.Entry : 0);
            }
            int named = 0, total = 0;
            foreach (var ov in _ownedViews.Values) { total++; if (ov.Proto != null) named++; }
            bool complete = total > 0 && named == total;
            string what;
            if (!_itemsFirstLogged) { _itemsFirstLogged = true; what = "inventory from the server"; }
            else if (complete && !_itemsReadyLogged) { _itemsReadyLogged = true; what = "inventory ready"; }
            else if (_itemsReadyLogged && sig != _equipSig) what = "equipment now";
            else return;
            _equipSig = sig;
            var sb = new StringBuilder();
            for (int i = 0; i < EquipSlotEnd; i++)
            {
                var v = _player.Equipment[i];
                if (v == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(((EquipSlot)i).ToString()).Append(' ').Append(v.Entry).Append(" '").Append(v.Name).Append("' display ").Append(v.DisplayId);
            }
            WowLog.Info("Online items: " + what + ": " + eq + " equipped, " + _player.Bags.Count + " in bags, " + _player.FreeBagSlots + " free, item info " + named + "/" + total +
                        (_itemUnresolvedSlots > 0 ? ", " + _itemUnresolvedSlots + " slots point to objects not (yet) known" : "") + (sb.Length > 0 ? " - " + sb : ""));
        }

        // ------------------------------------------------------------------ item information (CMSG/SMSG_ITEM_QUERY_SINGLE)

        void AskItem(int entry)
        {
            if (entry <= 0 || _itemProtos.ContainsKey(entry) || !_itemAsked.Add(entry)) return;
            _itemQueryQueue.Enqueue(entry);
        }

        /// <summary>
        /// SMSG_ITEM_QUERY_SINGLE_RESPONSE (ItemHandler.cpp HandleItemQuerySingleOpcode, every field in order): uint32 ItemId, Class,
        /// SubClass, int32 SoundOverrideSubclass, Name1, three empty names (uint8 0 each), DisplayInfoID, Quality, Flags, Flags2, int32 BuyPrice,
        /// SellPrice, InventoryType, AllowableClass, AllowableRace, ItemLevel, RequiredLevel, RequiredSkill, RequiredSkillRank, RequiredSpell,
        /// RequiredHonorRank, RequiredCityRank, RequiredReputationFaction, RequiredReputationRank, int32 MaxCount, int32 Stackable,
        /// ContainerSlots, StatsCount + StatsCount x (type, int32 value), ScalingStatDistribution, ScalingStatValue, 2 x (float min,
        /// float max, uint32 type), Armor, 6 x int32 resistances, Delay, AmmoType, float RangedModRange, 5 x (int32 id, trigger, int32 charges,
        /// cooldown, category, category cooldown), Bonding, Description, PageText, LanguageID, PageMaterial, StartQuest, LockID, int32 Material,
        /// Sheath, int32 RandomProperty, int32 RandomSuffix, Block, ItemSet, MaxDurability, Area, Map, BagFamily, TotemCategory,
        /// 3 x (color, content), socketBonus, GemProperties, RequiredDisenchantSkill, float ArmorDamageModifier, Duration,
        /// ItemLimitCategory, HolidayId. Unknown item: uint32 (item | 0x80000000) only.
        /// </summary>
        void OnItemQueryResponse(WowPacket p)
        {
            uint raw = p.U32();
            int entry = (int)(raw & 0x7FFFFFFFu);
            var d = new ItemProto { Entry = entry };
            if ((raw & ItemQueryUnknown) != 0 || p.Remaining == 0)
            {
                d.Missing = true;
                _itemMissing++;
                WowLog.Warn("Online items: the server does not know item " + entry + " (SMSG_ITEM_QUERY_SINGLE_RESPONSE " + raw.ToString("X8") + ")");
            }
            else
            {
                try { ReadItemProto(p, d); }
                catch (PacketTooShortException e)
                {
                    d.Truncated = true;
                    if (!_itemQueryOddLogged) { _itemQueryOddLogged = true; WowLog.Warn("Online items: item " + entry + " answer ended early (" + e.Message + ") - what was read is used (logged once)"); }
                }
                if (!d.Truncated && p.Remaining > 0 && !_itemQueryOddLogged)
                {
                    _itemQueryOddLogged = true;
                    WowLog.Warn("Online items: item " + entry + " answer has " + p.Remaining + " bytes more than AzerothCore writes (ignored; logged once)");
                }
            }
            _itemProtos[entry] = d;
            _itemAnswers++;
            // views handed out before the answer: patched in place (loot / quest windows read them every frame)
            List<TipWaiting> waiting;
            if (_itemViewsWaiting.TryGetValue(entry, out waiting))
            {
                _itemViewsWaiting.Remove(entry);
                for (int i = 0; i < waiting.Count; i++) FillItemView(waiting[i].View, entry, waiting[i].View.Count, d, waiting[i].Inst);
            }
            TipAnswersArrived();                                  // our bags / equipment of this entry get their name (new views); set pieces named
            if (_pendingUseGuid != 0)
            {
                OnlineObject o;
                if (_objects.TryGetValue(_pendingUseGuid, out o) && o.Entry == entry)
                {
                    ulong g = _pendingUseGuid;
                    _pendingUseGuid = 0;
                    RebuildInventory();                          // positions may have changed
                    UseItemByGuid(g);                            // iteration 11 (npc): wherever it is now (bags, equipment)
                }
            }
        }

        static void ReadItemProto(WowPacket p, ItemProto d)
        {
            d.Class = p.I32(); d.SubClass = p.I32(); p.I32();
            d.Name = p.CString();
            p.CString(); p.CString(); p.CString();               // Name2..4: "uint8(0x00)" each = empty strings
            d.DisplayId = p.I32(); d.Quality = p.I32(); d.Flags = p.U32(); d.Flags2 = p.U32();
            d.BuyPrice = p.I32(); d.SellPrice = p.I32(); d.InventoryType = p.I32(); d.AllowableClass = p.I32(); d.AllowableRace = p.I32();
            d.ItemLevel = p.I32(); d.RequiredLevel = p.I32();
            d.RequiredSkill = p.I32(); d.RequiredSkillRank = p.I32();   // iteration 11 (trainer I1)
            d.RequiredSpell = p.I32(); p.U32(); p.U32();          // RequiredSpell, RequiredHonorRank, RequiredCityRank
            d.RequiredReputationFaction = p.I32(); d.RequiredReputationRank = p.I32();
            d.MaxCount = p.I32(); d.Stackable = p.I32(); d.ContainerSlots = p.I32();
            uint stats = p.U32();                                 // "queryData << pProto->StatsCount" then that many pairs
            if (stats > 64) throw new PacketTooShortException("StatsCount " + stats + " is not a count");
            for (int i = 0; i < (int)stats; i++)
            {
                int type = p.I32(), value = p.I32();
                if (d.StatCount < MaxProtoStats) { d.StatType[d.StatCount] = type; d.StatValue[d.StatCount] = value; d.StatCount++; }
            }
            d.ScalingStatDistribution = p.I32(); d.ScalingStatValue = p.I32();   // ScalingStatDistribution, ScalingStatValue (heirlooms)
            for (int i = 0; i < MaxProtoDamages; i++) { d.DmgMin[i] = p.F32(); d.DmgMax[i] = p.F32(); d.DmgType[i] = p.I32(); }
            d.Armor = p.I32();
            for (int i = 0; i < 6; i++) d.Res[i] = p.I32();
            d.Delay = p.I32(); p.U32(); p.F32();                  // Delay, AmmoType, RangedModRange
            for (int i = 0; i < MaxProtoSpells; i++)
            {
                d.SpellId[i] = p.I32(); d.SpellTrigger[i] = p.I32(); d.SpellCharges[i] = p.I32();
                d.SpellCooldown[i] = p.I32(); p.U32(); d.SpellCategoryCooldown[i] = p.I32();   // cooldown, category, category cooldown (uint32(-1) = none)
            }
            d.Bonding = p.I32();
            d.Description = p.CString();
            d.PageText = p.I32(); p.U32(); d.PageMaterial = p.I32();   // PageText, LanguageID, PageMaterial (iteration 11, npc)
            d.StartQuest = p.I32(); d.LockId = p.I32(); p.I32();  // StartQuest, LockID, Material
            d.Sheath = p.I32(); d.RandomProperty = p.I32(); d.RandomSuffix = p.I32();   // Sheath, RandomProperty, RandomSuffix
            d.Block = p.I32(); d.ItemSet = p.I32(); d.MaxDurability = p.I32(); // Block, ItemSet, MaxDurability
            p.U32(); p.U32(); d.BagFamily = p.I32(); d.TotemCategory = p.I32();   // Area, Map, BagFamily (npc), TotemCategory (trainer I1)
            for (int i = 0; i < MaxProtoSockets; i++) { d.SocketColor[i] = p.I32(); p.U32(); }   // Socket[s].Color, .Content
            d.SocketBonus = p.I32(); d.GemProperties = p.I32(); d.RequiredDisenchantSkill = p.I32(); p.F32();       // socketBonus, GemProperties, RequiredDisenchantSkill (trainer I1), ArmorDamageModifier
            d.Duration = p.I32(); d.ItemLimitCategory = p.I32(); p.U32();   // Duration, ItemLimitCategory, HolidayId
        }

        /// <summary>
        /// An item for the loot / quest / vendor windows: the same ItemView the bags show, from the server's item answer (asked now if
        /// unknown; until it arrives the view says "Retrieving item information" and is filled in place when it does). inst = the item's
        /// own roll when the packet carries it (loot, roll, trade, mail, auction: random property / suffix, enchantments); null = the
        /// template only (a random item then says "&lt;Random enchantment&gt;", like WoW).
        /// </summary>
        internal ItemView ItemInfoView(int entry, int count) { return ItemInfoViewOf(entry, count, null); }

        /// <summary>ItemInfoView with the item's own roll (a separate name: probes find ItemInfoView(int, int) by reflection).</summary>
        internal ItemView ItemInfoViewOf(int entry, int count, ItemTipInstance inst)
        {
            ItemProto proto;
            _itemProtos.TryGetValue(entry, out proto);
            var v = BuildItemView(entry, Math.Max(1, count), proto, inst);
            if (proto == null && entry > 0)
            {
                AskItem(entry);
                List<TipWaiting> list;
                if (!_itemViewsWaiting.TryGetValue(entry, out list)) { list = new List<TipWaiting>(); _itemViewsWaiting[entry] = list; }
                if (list.Count < 64) list.Add(new TipWaiting { View = v, Inst = inst });
            }
            return v;
        }

        /// <summary>The item's name from the server (null until known).</summary>
        internal string ItemInfoName(int entry)
        {
            ItemProto proto;
            if (!_itemProtos.TryGetValue(entry, out proto)) { AskItem(entry); return null; }
            return proto.Missing ? null : proto.Name;
        }

        /// <summary>The item's name with its random property / suffix ("Tumultuous Ring of the Sun"; ITEM_SUFFIX_TEMPLATE "%s %s"), null until known.</summary>
        internal string ItemInfoNameOf(int entry, int randomPropertyId)
        {
            string n = ItemInfoName(entry);
            if (n == null || randomPropertyId == 0) return n;
            ItemProto proto = _itemProtos[entry];
            return ItemTip.Name(proto.Tip, new ItemTipInstance { RandomPropertyId = randomPropertyId }, ItemTipDb.Get(_files));
        }

        /// <summary>
        /// Stage O2 (integrator, items request 1 / lootdeath request 3): the bags' full WoW tooltip for the loot and quest windows, or null
        /// while the server's answer is not in yet. Asks nothing: loot and quests send their own queries, and this file's
        /// SMSG_ITEM_QUERY_SINGLE_RESPONSE handler (registered first, InitItems) has stored every answer before theirs run.
        /// </summary>
        internal string ItemInfoTooltip(int entry, int count) { return ItemInfoTooltipOf(entry, count, null); }

        internal string ItemInfoTooltipOf(int entry, int count, ItemTipInstance inst)
        {
            ItemProto proto;
            if (entry <= 0 || !_itemProtos.TryGetValue(entry, out proto) || proto.Missing) return null;
            return BuildItemView(entry, Math.Max(1, count), proto, inst).Tooltip;
        }

        /// <summary>A loot / roll / push packet's roll: "uint32 randomSuffix (the suffix factor), uint32 randomPropertyId" (LootMgr.cpp:968-969).</summary>
        internal static ItemTipInstance RollOf(int suffixFactor, int randomPropertyId)
        {
            if (randomPropertyId == 0) return null;
            return new ItemTipInstance { RandomPropertyId = randomPropertyId, SuffixFactor = suffixFactor };
        }

        ItemView BuildItemView(int entry, int count, ItemProto proto, ItemTipInstance inst)
        {
            var v = new ItemView();
            FillItemView(v, entry, count, proto, inst);
            return v;
        }

        void FillItemView(ItemView v, int entry, int count, ItemProto proto, ItemTipInstance inst)
        {
            LoadItemDbcs();
            int dbcClass = -1, dbcSub = -1, dbcDisplay = 0, dbcInv = 0, dbcSheath = 0;
            if (_itemDbcI != null)
            {
                int r = _itemDbcI.FindRow(entry);
                if (r >= 0)
                {
                    dbcClass = _itemDbcI.GetInt(r, DbcFields.Item.ClassID);
                    dbcSub = _itemDbcI.GetInt(r, DbcFields.Item.SubclassID);
                    dbcDisplay = _itemDbcI.GetInt(r, DbcFields.Item.DisplayInfoID);
                    dbcInv = _itemDbcI.GetInt(r, DbcFields.Item.InventoryType);
                    dbcSheath = _itemDbcI.GetInt(r, DbcFields.Item.SheatheType);
                }
            }
            bool known = proto != null && !proto.Missing;
            bool owned = inst != null && inst.Owned;
            int dur = owned ? inst.Durability : 0, maxDur = owned ? inst.MaxDurability : 0;
            uint fieldFlags = owned ? inst.FieldFlags : 0;
            var tipDb = known ? ItemTipDb.Get(_files) : null;
            v.Entry = entry;
            v.Count = count;
            v.DisplayId = known && proto.DisplayId > 0 ? proto.DisplayId : dbcDisplay;
            v.InventoryType = known ? proto.InventoryType : dbcInv;
            v.SheatheType = known && proto.Sheath > 0 ? proto.Sheath : dbcSheath;
            v.Quality = known ? proto.Quality : 1;
            v.Name = known ? ItemTip.Name(proto.Tip, inst, tipDb) : proto != null ? "Item " + entry : "Retrieving item information";   // RETRIEVING_ITEM_INFO (GlobalStrings.lua:6131); "Item N" ours (the server has no such item)
            v.IsQuestItem = known && (proto.Class == ItemClassQuest || proto.Bonding == 4 || proto.Bonding == 5);
            string icon = IconOf(v.DisplayId);
            v.IconPath = icon ?? @"Interface\Icons\INV_Misc_QuestionMark";
            if (known)
            {
                var ctx = TipContext();
                v.Tooltip = ItemTip.Build(proto.Tip, inst, tipDb, ctx);                       // the whole 3.3.5a tooltip (WowItemTooltip.cs)
                if (ctx.Incomplete && !owned)
                {
                    bool listed = false;
                    for (int i = 0; i < _tipIncomplete.Count; i++) if (ReferenceEquals(_tipIncomplete[i].View, v)) listed = true;
                    if (!listed && _tipIncomplete.Count < 64) _tipIncomplete.Add(new TipWaiting { View = v, Inst = inst });
                }
            }
            else v.Tooltip = v.Name;
            // iteration 11 (npc): what the bags, the merchant, the bank and the paper doll need (WowPlayTypes.cs ItemView)
            v.SellPrice = known ? Math.Max(0, proto.SellPrice) : 0;
            v.MaxDurability = owned ? Math.Max(0, maxDur) : known ? Math.Max(0, proto.MaxDurability) : 0;
            v.Durability = owned ? Math.Max(0, dur) : v.MaxDurability;
            v.ContainerSlots = known ? Math.Max(0, proto.ContainerSlots) : 0;
            v.BagFamily = known ? proto.BagFamily : 0;
            v.ItemClass = known ? proto.Class : dbcClass;
            v.ItemSubClass = known ? proto.SubClass : dbcSub;
            v.ItemLevel = known ? proto.ItemLevel : 0;
            v.RequiredLevel = known ? proto.RequiredLevel : 0;
            v.Soulbound = owned && (fieldFlags & ItemFieldFlagSoulbound) != 0;
            v.Readable = (known && proto.PageText > 0) || (owned && (fieldFlags & ItemFieldFlagReadable) != 0);
            v.Openable = known && (proto.Flags & ItemFlagHasLoot) != 0;
            v.NoDestroy = known && (proto.Flags & ItemFlagNoUserDestroy) != 0;
            v.MaxStack = known ? Math.Max(1, proto.Stackable) : 1;
        }

        void LoadItemDbcs()
        {
            if (_itemDbcTried || _files == null) return;
            _itemDbcTried = true;
            _itemDbcI = Dbc.Load(_files, "Item");
            if (_itemDbcI != null && _itemDbcI.FieldCount != DbcFields.Item.FieldCount) _itemDbcI = null;
            _itemDisplayDbcI = Dbc.Load(_files, "ItemDisplayInfo");
            if (_itemDisplayDbcI != null && _itemDisplayDbcI.FieldCount != DbcFields.ItemDisplayInfo.FieldCount) _itemDisplayDbcI = null;
            WowLog.Info("Online items: Item.dbc " + (_itemDbcI != null ? _itemDbcI.RecordCount + " rows" : "missing") + ", ItemDisplayInfo " + (_itemDisplayDbcI != null ? _itemDisplayDbcI.RecordCount + " rows" : "missing"));
        }

        string IconOf(int displayId)
        {
            if (_itemDisplayDbcI == null || displayId <= 0) return null;
            int r = _itemDisplayDbcI.FindRow(displayId);
            if (r < 0) return null;
            string icon = _itemDisplayDbcI.GetString(r, DbcFields.ItemDisplayInfo.InventoryIcon);
            return string.IsNullOrEmpty(icon) ? null : @"Interface\Icons\" + icon;
        }

        /// <summary>An item spell's Spell.dbc description with its $-tokens resolved (SpellText), for Equip: / Use: / Chance on hit: / Set: lines.</summary>
        string SpellDescription(int spellId)
        {
            var db = SpellDb.Get(_files);
            var s = db != null ? db.Find(spellId) : null;
            if (s == null) return null;
            var ctx = new SpellTextContext { CasterLevel = Math.Max(1, _player.Level), HomeName = _player.HomeName };   // HomeName: the Hearthstone's $z (iteration 11, npc H6)
            string t = SpellText.Resolve(db, s, s.Description, ctx);
            return string.IsNullOrEmpty(t) ? null : t.Trim();
        }

        // ------------------------------------------------------------------ actions

        int BagIndexOf(ulong guid)
        {
            for (int i = 0; i < _bagPos.Count; i++) if (_bagPos[i].Guid == guid) return i;
            return -1;
        }

        /// <summary>EquipItem(bagIndex) on Player.Bags (kept for the other areas' bag indices) = EquipItemAt of the same item.</summary>
        public void EquipItem(int bagIndex)
        {
            if (_world == null || bagIndex < 0 || bagIndex >= _bagPos.Count) return;
            var bp = _bagPos[bagIndex];
            EquipItemAt(bp.Bag, bp.Slot);
        }

        /// <summary>CMSG_AUTOEQUIP_ITEM (ItemPackets.cpp AutoEquipItem::Read: uint8 SourceBag, uint8 SourceSlot); the server picks the slot,
        /// swaps and answers errors (a bag item goes to a free bag slot).</summary>
        public void EquipItemAt(int bag, int slot)
        {
            if (_world == null) return;
            ulong g = GuidAt(bag, slot);
            if (g == 0) { Error("That slot is empty."); return; }                                              // ERR_SLOT_EMPTY (GlobalStrings.lua:3488)
            _world.Send(Op.CMSG_AUTOEQUIP_ITEM, new PacketWriter().U8(bag).U8(slot));
            _equipWatchGuid = g;
            _equipWatchUntil = _now + EquipWatchSeconds;
            InvLock(g);
            WowLog.Info("Online items: equip " + ItemLabelOf(g) + " from bag " + bag + " slot " + slot + " (CMSG_AUTOEQUIP_ITEM)");
        }

        /// <summary>UseItem(bagIndex) on Player.Bags (kept for the other areas' bag indices) = UseItemAt of the same item.</summary>
        public void UseItem(int bagIndex)
        {
            if (_world == null || bagIndex < 0 || bagIndex >= _bagPos.Count) return;
            UseItemPos(_bagPos[bagIndex]);
        }

        /// <summary>A right-click on an item anywhere (bags, keyring, an equipped "Use:" item). A bank item goes to the bags while the bank is
        /// open (WoW's UseContainerItem bank rule, ContainerFrame.lua:719-733); selling stays the UI's SellItem.</summary>
        public void UseItemAt(int bag, int slot)
        {
            if (_world == null) return;
            if (InvPos.IsBank(bag, slot))
            {
                if (_bankGuid != 0) BankItem(bag, slot);
                else Error("You are too far away from a bank.");                                                // ERR_NO_BANK_HERE (3323)
                return;
            }
            ulong g = GuidAt(bag, slot);
            if (g == 0) { Error("That slot is empty."); return; }                                              // ERR_SLOT_EMPTY (3488)
            UseItemPos(new BagPos { Bag = (byte)bag, Slot = (byte)slot, Guid = g });
        }

        void UseItemByGuid(ulong g)
        {
            int pos = InvPosOf(g);
            if (pos < 0) { WowLog.Info("Online items: the item to use (" + g.ToString("X16") + ") is no longer in the inventory - not used"); return; }
            UseItemPos(new BagPos { Bag = (byte)(pos >> 8), Slot = (byte)(pos & 0xFF), Guid = g });
        }

        /// <summary>
        /// What the real client does on a right click, by the item's data: an item that begins a quest -> CMSG_QUESTGIVER_QUERY_QUEST
        /// (QuestHandler.cpp HandleQuestgiverQueryQuestOpcode takes TYPEMASK_ITEM: uint64 guid, uint32 quest, uint8 1); an openable item
        /// (ITEM_FLAG_HAS_LOOT) -> CMSG_OPEN_ITEM (SpellHandler.cpp HandleOpenItemOpcode: uint8 bag, uint8 slot); an equippable item (also one
        /// with a "Use:" spell) -> EquipItemAt; else an ON_USE spell -> CMSG_USE_ITEM; a letter (ITEM_FIELD_FLAG_READABLE) -> CMSG_ITEM_TEXT_QUERY;
        /// a book (PageText) -> CMSG_READ_ITEM (OnlineSession.ItemText.cs). An equipped item only uses its "Use:" spell (UseInventoryItem).
        /// Until the server's item answer is there the use waits for it (ItemInfoWait).
        /// </summary>
        void UseItemPos(BagPos bp)
        {
            OnlineObject o;
            if (!_objects.TryGetValue(bp.Guid, out o)) { Error("The item was not found."); return; }   // ERR_ITEM_NOT_FOUND (GlobalStrings.lua:3169)
            int entry = o.Entry;
            ItemProto d;
            if (!_itemProtos.TryGetValue(entry, out d))
            {
                AskItem(entry);
                _pendingUseGuid = bp.Guid;
                _pendingUseUntil = _now + ItemInfoWait;
                WowLog.Info("Online items: use of item " + entry + " waits for its item information");
                return;
            }
            if (d.Missing) { Error("The item was not found."); return; }                                      // ERR_ITEM_NOT_FOUND (3169)
            if ((d.Flags & ItemFlagPetition) != 0) { ShowCharter(bp.Guid); return; }                          // social H3 (npc H13): a guild / arena charter
            string label = ItemLabelOf(bp.Guid);
            bool equipped = bp.Bag == InvBag0 && bp.Slot < EquipSlotEnd;
            if (!equipped && d.StartQuest > 0)
            {
                _world.Send(Op.CMSG_QUESTGIVER_QUERY_QUEST, new PacketWriter().U64(bp.Guid).U32((uint)d.StartQuest).U8(1));
                WowLog.Info("Online items: " + label + " begins quest " + d.StartQuest + " (CMSG_QUESTGIVER_QUERY_QUEST with the item)");
                return;
            }
            if (!equipped && (d.Flags & ItemFlagHasLoot) != 0)
            {
                _world.Send(Op.CMSG_OPEN_ITEM, new PacketWriter().U8(bp.Bag).U8(bp.Slot));
                WowLog.Info("Online items: open " + label + " (CMSG_OPEN_ITEM bag " + bp.Bag + " slot " + bp.Slot + ")");
                return;
            }
            // An equippable item is equipped first, also one with a "Use:" spell (a trinket, goggles): AzerothCore refuses CMSG_USE_ITEM
            // for it while it is in the bags (SpellHandler.cpp HandleUseItemOpcode: "if (proto->InventoryType != INVTYPE_NON_EQUIP &&
            // !pItem->IsEquipped()) SendEquipError(EQUIP_ERR_ITEM_NOT_FOUND ...)"), and OfflineSession.UseItem equips it too.
            if (!equipped && d.InventoryType != 0) { EquipItemAt(bp.Bag, bp.Slot); return; }
            int spell = d.UseSpell;
            if (spell > 0)
            {
                int bagIndex = BagIndexOf(bp.Guid);
                if (bagIndex >= 0 && SpItemNeedsTargeting(bagIndex, spell, label)) return;                     // spellfx hook 3: a place / an item to aim at first
                if (QItemTargetKind(spell) == 2 && QUseItemOnObject(bp, spell, label)) return;                 // quests H-I1: gameobject-target quest items
                if (TryStartGlyph(bp, spell)) return;                                                           // talents 4b: a glyph waits for its socket
                SendUseItem(bp, spell, label);
                return;
            }
            if (equipped) { WowLog.Info("Online items: " + label + " (equipped) has no use"); return; }
            if ((o.U32(UF.ITEM_FIELD_FLAGS) & ItemFieldFlagReadable) != 0) { ItxQueryItemText(bp.Guid, d.Name); return; }   // a letter (iteration 11, npc)
            if (d.PageText > 0) { ItxReadItem(bp.Bag, bp.Slot, bp.Guid, d.PageText, d.PageMaterial, d.Name); return; }        // a book (iteration 11, npc)
            Error("You can't use that item.");                                                                  // ERR_CANT_USE_ITEM (GlobalStrings.lua:2992)
        }

        /// <summary>
        /// The cursor / drag-and-drop move (gap D1, proposal-npc.md 6.4): both positions in bag 255 -> CMSG_SWAP_INV_ITEM (uint8 dst, uint8 src:
        /// DESTINATION first, SwapInventoryItem::Read ItemPackets.cpp:29-33), else CMSG_SWAP_ITEM (uint8 dstBag, dstSlot, srcBag, srcSlot,
        /// SwapItem::Read :41-47). Both end in Player::SwapItem (ItemHandler.cpp:62-151): empty slot, swap, stack merge, equip / unequip -
        /// refusals come as SMSG_INVENTORY_CHANGE_FAILURE. Bank positions need the banker open (AC refuses silently, ItemHandler.cpp:82-92).
        /// </summary>
        public void MoveItem(int fromBag, int fromSlot, int toBag, int toSlot)
        {
            if (_world == null || (fromBag == toBag && fromSlot == toSlot)) return;
            ulong g = GuidAt(fromBag, fromSlot);
            if (g == 0) { Error("That slot is empty."); return; }                                              // ERR_SLOT_EMPTY (3488)
            if ((InvPos.IsBank(fromBag, fromSlot) || InvPos.IsBank(toBag, toSlot)) && _bankGuid == 0) { Error("You are too far away from a bank."); return; }   // ERR_NO_BANK_HERE (3323)
            if (!InvValidPos(fromBag, fromSlot) || !InvValidPos(toBag, toSlot)) { WowLog.Info("Online items: move " + fromBag + "/" + fromSlot + " -> " + toBag + "/" + toSlot + " is not an inventory position - nothing sent"); return; }
            if (fromBag == InvBag0 && toBag == InvBag0) _world.Send(Op.CMSG_SWAP_INV_ITEM, new PacketWriter().U8(toSlot).U8(fromSlot));
            else _world.Send(Op.CMSG_SWAP_ITEM, new PacketWriter().U8(toBag).U8(toSlot).U8(fromBag).U8(fromSlot));
            InvLock(g);
            InvLock(GuidAt(toBag, toSlot));
            WowLog.Info("Online items: move " + ItemLabelOf(g) + " " + fromBag + "/" + fromSlot + " -> " + toBag + "/" + toSlot +
                        (fromBag == InvBag0 && toBag == InvBag0 ? " (CMSG_SWAP_INV_ITEM)" : " (CMSG_SWAP_ITEM)"));
        }

        /// <summary>CMSG_SPLIT_ITEM (SplitItem::Read: uint8 srcBag, srcSlot, dstBag, dstSlot, uint32 count; ItemHandler.cpp:34-60). No-op for
        /// count &lt;= 0 or &gt;= the stack (AC answers a whole-stack split with EQUIP_ERR_COULDNT_SPLIT_ITEMS, PlayerStorage.cpp:3546-3551 - the UI
        /// sends a whole stack as MoveItem).</summary>
        public void SplitItem(int fromBag, int fromSlot, int toBag, int toSlot, int count)
        {
            if (_world == null) return;
            ulong g = GuidAt(fromBag, fromSlot);
            OnlineObject o;
            if (g == 0 || !_objects.TryGetValue(g, out o)) { Error("That slot is empty."); return; }          // ERR_SLOT_EMPTY (3488)
            int stack = Math.Max(1, o.I32(UF.ITEM_FIELD_STACK_COUNT));
            if (count <= 0 || count >= stack) { WowLog.Info("Online items: split of " + count + " from a stack of " + stack + " - nothing sent (a whole stack is a move)"); return; }
            if ((InvPos.IsBank(fromBag, fromSlot) || InvPos.IsBank(toBag, toSlot)) && _bankGuid == 0) { Error("You are too far away from a bank."); return; }
            if (!InvValidPos(toBag, toSlot)) return;
            _world.Send(Op.CMSG_SPLIT_ITEM, new PacketWriter().U8(fromBag).U8(fromSlot).U8(toBag).U8(toSlot).U32((uint)count));
            InvLock(g);
            WowLog.Info("Online items: split " + count + " of " + stack + " " + ItemLabelOf(g) + " " + fromBag + "/" + fromSlot + " -> " + toBag + "/" + toSlot + " (CMSG_SPLIT_ITEM)");
        }

        /// <summary>
        /// CMSG_DESTROYITEM (DestroyItem::Read: uint8 bag, slot, count, data1, data2, data3; ItemHandler.cpp:280-321): count 0 = the whole stack
        /// (DestroyItem), else DestroyItemCount. count &lt;= 0 or &gt;= the stack -> one packet with 0; a partial count goes in chunks of at most
        /// 255 and never removes more than asked (npc R10). ITEM_FLAG_NO_USER_DESTROY / equipped-in-combat / non-empty bags are refused by the
        /// server (an Error event).
        /// </summary>
        public void DestroyItem(int bag, int slot, int count)
        {
            if (_world == null) return;
            ulong g = GuidAt(bag, slot);
            OnlineObject o;
            if (g == 0 || !_objects.TryGetValue(g, out o)) { Error("That slot is empty."); return; }          // ERR_SLOT_EMPTY (3488)
            int stack = Math.Max(1, o.I32(UF.ITEM_FIELD_STACK_COUNT));
            int chunks = 0;
            if (count <= 0 || count >= stack) { _world.Send(Op.CMSG_DESTROYITEM, new PacketWriter().U8(bag).U8(slot).U8(0).U8(0).U8(0).U8(0)); chunks = 1; count = stack; }
            else
            {
                for (int left = count; left > 0; left -= 255, chunks++)
                    _world.Send(Op.CMSG_DESTROYITEM, new PacketWriter().U8(bag).U8(slot).U8(Math.Min(left, 255)).U8(0).U8(0).U8(0));
            }
            InvLock(g);
            string name = ItemInfoName(o.Entry) ?? ("Item " + o.Entry);
            _invDestroys.Add(new InvDestroyWatch { Guid = g, Entry = o.Entry, Count = count, Before = stack, Name = name, Until = _now + InvLockSeconds });
            WowLog.Info("Online items: destroy " + count + " of " + stack + " '" + name + "' at " + bag + "/" + slot + " (" + chunks + " CMSG_DESTROYITEM)");
        }

        /// <summary>A drop on a bag button of the bag bar: CMSG_AUTOSTORE_BAG_ITEM (AutoStoreBagItem::Read: uint8 srcBag, srcSlot, dstBag;
        /// ItemHandler.cpp:967-1012 - CanStoreItem(dstBag, NULL_SLOT) picks the slot; dstBag 255 = the backpack, 19..22 = that bag).</summary>
        public void StoreItemInBag(int srcBag, int srcSlot, int dstBag)
        {
            if (_world == null) return;
            ulong g = GuidAt(srcBag, srcSlot);
            if (g == 0) { Error("That slot is empty."); return; }                                              // ERR_SLOT_EMPTY (3488)
            if (!(dstBag == InvBag0 || (dstBag >= InvPos.BagSlotStart && dstBag < InvPos.BagSlotEnd) || (dstBag >= InvPos.BankBagStart && dstBag < InvPos.BankBagEnd))) return;
            if ((InvPos.IsBank(srcBag, srcSlot) || (dstBag >= InvPos.BankBagStart && dstBag < InvPos.BankBagEnd)) && _bankGuid == 0) { Error("You are too far away from a bank."); return; }
            _world.Send(Op.CMSG_AUTOSTORE_BAG_ITEM, new PacketWriter().U8(srcBag).U8(srcSlot).U8(dstBag));
            InvLock(g);
            WowLog.Info("Online items: store " + ItemLabelOf(g) + " " + srcBag + "/" + srcSlot + " into bag " + dstBag + " (CMSG_AUTOSTORE_BAG_ITEM)");
        }

        /// <summary>A position the session can address: bag 255 slots 0..117 (no buyback / tokens), a bag 19..22 / 67..73 slot 0..35.</summary>
        static bool InvValidPos(int bag, int slot)
        {
            if (bag == InvBag0) return slot >= 0 && slot < InvPos.KeyringEnd && !(slot >= InvPos.BuybackStart && slot < InvPos.BuybackEnd);
            return ((bag >= InvPos.BagSlotStart && bag < InvPos.BagSlotEnd) || (bag >= InvPos.BankBagStart && bag < InvPos.BankBagEnd)) && slot >= 0 && slot < MaxBagSize;
        }

        /// <summary>
        /// CMSG_USE_ITEM (SpellHandler.cpp HandleUseItemOpcode: "recvPacket >> bagIndex >> slot >> castCount >> spellId >> itemGUID >> glyphIndex
        /// >> castFlags", then SpellCastTargets::Read: uint32 targetMask [UNIT 0x2: packed guid]). castFlags 0 (no missile data). A unit
        /// target is sent when the spell needs one and we have one; else mask 0 and AzerothCore picks the selection or the caster
        /// (Spell::InitExplicitTargets).
        /// </summary>
        void SendUseItem(BagPos bp, int spell, string label, uint glyphIndex = 0)
        {
            var db = SpellDb.Get(_files);
            var si = db != null ? db.Find(spell) : null;
            if (WowConsumeGate.IsFoodOrDrink(si))
            {
                // lead 2026-10-04 (Immersive eating, VR): refused before anything is sent - the item is not used up
                string no = WowConsumeGate.Refusal();
                if (no != null) { Error(no); WowLog.Info("Online items: use " + label + " (" + si.Name + ") refused here - " + no); return; }
            }
            _itemCastCount++;
            if (_itemCastCount == 0) _itemCastCount = 1;                           // 0 marks triggered casts in AC
            var w = new PacketWriter().U8(bp.Bag).U8(bp.Slot).U8(_itemCastCount).U32((uint)spell).U64(bp.Guid).U32(glyphIndex).U8(0);   // glyphIndex: talents 4a
            SpWriteUseTargets(w, si);                                              // spellfx hook 3: the SpellCastTargets (unit / none, or a confirmed place / item)
            _world.Send(Op.CMSG_USE_ITEM, w);
            WowLog.Info("Online items: use " + label + " -> spell " + spell + " '" + (si != null ? si.Name : "?") + "' (CMSG_USE_ITEM bag " + bp.Bag + " slot " + bp.Slot +
                        ", castCount " + _itemCastCount + (glyphIndex != 0 ? ", glyph socket " + glyphIndex : "") + ")");
        }

        string ItemLabel(int bagIndex)
        {
            var v = bagIndex >= 0 && bagIndex < _player.Bags.Count ? _player.Bags[bagIndex] : null;
            return v == null ? "item" : v.Entry + " '" + v.Name + "'";
        }

        /// <summary>"entry 'name'" of one of our items, for the log.</summary>
        string ItemLabelOf(ulong g)
        {
            OnlineObject o;
            if (g == 0 || !_objects.TryGetValue(g, out o)) return "item";
            return o.Entry + " '" + (ItemInfoName(o.Entry) ?? "?") + "'";
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>
        /// SMSG_INVENTORY_CHANGE_FAILURE (PlayerStorage.cpp Player::SendEquipError): uint8 msg; unless EQUIP_ERR_OK: uint64 item, uint64 item2,
        /// uint8 bag subclass; CANT_EQUIP_LEVEL_I (1) / PURCHASE_LEVEL_TOO_LOW (87): uint32 required level; EVENT_AUTOEQUIP_BIND_CONFIRM (81):
        /// uint64, uint32, uint64; ITEM_MAX_LIMIT_CATEGORY_* (84, 85, 89): uint32 limit category.
        /// </summary>
        void OnInventoryChangeFailure(WowPacket p)
        {
            int msg = p.U8();
            InvUnlockAll();                                                        // iteration 11 (npc): any answer releases the greyed items (incl. 59 EQUIP_ERR_NONE)
            if (msg == 0) return;                                                  // EQUIP_ERR_OK: nothing follows
            ulong item = 0; int extra = 0;
            if (p.Remaining >= 17) { item = p.U64(); p.U64(); p.U8(); }
            else if (!_itemFailOddLogged) { _itemFailOddLogged = true; WowLog.Warn("Online items: SMSG_INVENTORY_CHANGE_FAILURE " + msg + " is only " + p.Length + " bytes (logged once)"); }
            if ((msg == 1 || msg == 87 || msg == 84 || msg == 85 || msg == 89) && p.Remaining >= 4) extra = p.I32();
            else if (msg == 81 && p.Remaining >= 20) { p.U64(); p.U32(); p.U64(); }
            string text = InventoryResultText(msg, extra);
            string name = null;
            OnlineObject o;
            if (item != 0 && _objects.TryGetValue(item, out o)) name = ItemInfoName(o.Entry);
            WowLog.Info("Online items: SMSG_INVENTORY_CHANGE_FAILURE " + msg + " " + InventoryResultName(msg) + (name != null ? " for '" + name + "'" : "") +
                        (text != null ? " \"" + text + "\"" : " (no WoW text for this code - nothing shown)"));
            if (item != 0 && item == _equipWatchGuid) _equipWatchGuid = 0;
            if (text != null) Error(text);
        }

        /// <summary>
        /// The client's text of an InventoryResult code: the GlobalStrings.lua key is TrinityCore 3.3.5's name of the same code
        /// (ItemDefines.h, EQUIP_ERR_X -> ERR_X; AzerothCore's Item.h numbers them identically), the text is the key's line in GlobalStrings.lua.
        /// 59 (AzerothCore's EQUIP_ERR_NONE, sent to release an item silently), 81 (bind confirm dialog) and 83 show nothing.
        /// </summary>
        string InventoryResultText(int msg, int extra)
        {
            switch (msg)
            {
                case 1: return "You must reach level " + extra + " to use that item.";                          // ERR_CANT_EQUIP_LEVEL_I (2981)
                case 2: return "You aren't skilled enough to use that item.";                                  // ERR_CANT_EQUIP_SKILL (2986)
                case 3: return "That item does not go in that slot.";                                          // ERR_WRONG_SLOT (3600)
                case 4: case 53: case 56: case 62: case 74: return "That bag is full.";                          // ERR_BAG_FULL (2949)
                case 5: return "Can't put non-empty bags in other bags.";                                      // ERR_BAG_IN_BAG (2950)
                case 6: return "You can't trade equipped bags.";                                               // ERR_TRADE_EQUIPPED_BAG (3549)
                case 7: return "Only ammo can go there.";                                                      // ERR_AMMO_ONLY (2875)
                case 8: return "You do not have the required proficiency for that item.";                      // ERR_PROFICIENCY_NEEDED (3401)
                case 9: case 12: case 18: return "No equipment slot is available for that item.";              // ERR_NO_SLOT_AVAILABLE (3329)
                case 10: case 11: return "You can never use that item.";                                       // ERR_CANT_EQUIP_EVER (2980)
                case 13: return "Cannot equip that with a two-handed weapon.";                                 // ERR_2HANDED_EQUIPPED (2861)
                case 14: return "You cannot dual-wield";                                                       // ERR_2HSKILLNOTFOUND (2862)
                case 15: case 16: return "That item doesn't go in that container.";                            // ERR_WRONG_BAG_TYPE (3597)
                case 17: return "You can't carry any more of those items.";                                    // ERR_ITEM_MAX_COUNT (3163)
                case 19: case 55: return "This item cannot stack.";                                            // ERR_CANT_STACK (2989)
                case 20: return "This item cannot be equipped.";                                               // ERR_NOT_EQUIPPABLE (3308)
                case 21: return "These items can't be swapped.";                                               // ERR_CANT_SWAP (2990)
                case 22: return "That slot is empty.";                                                         // ERR_SLOT_EMPTY (3488)
                case 23: case 54: return "The item was not found.";                                            // ERR_ITEM_NOT_FOUND (3169)
                case 24: return "You can't drop a soulbound item.";                                            // ERR_DROP_BOUND_ITEM (3034)
                case 25: return "Out of range.";                                                               // ERR_OUT_OF_RANGE (3341)
                case 26: return "Tried to split more than number in stack.";                                   // ERR_TOO_FEW_TO_SPLIT (3538)
                case 27: return "Couldn't split those items.";                                                 // ERR_SPLIT_FAILED (3507)
                case 28: return "Missing reagent";                                                             // ERR_SPELL_FAILED_REAGENTS_GENERIC (3501)
                case 29: return "You don't have enough money.";                                                // ERR_NOT_ENOUGH_MONEY (3307)
                case 30: return "Not a bag.";                                                                  // ERR_NOT_A_BAG (3301)
                case 31: return "You can only do that with empty bags.";                                       // ERR_DESTROY_NONEMPTY_BAG (3023)
                case 32: return "You don't own that item.";                                                    // ERR_NOT_OWNER (3314)
                case 33: case 41: return "You can only equip one quiver.";                                     // ERR_ONLY_ONE_QUIVER (3334) / ERR_ONLY_ONE_BOLT (3333)
                case 34: return "You must purchase that bag slot first";                                       // ERR_NO_BANK_SLOT (3324)
                case 35: return "You are too far away from a bank.";                                           // ERR_NO_BANK_HERE (3323)
                case 36: return "Item is locked.";                                                             // ERR_ITEM_LOCKED (3162)
                case 37: return "You are stunned";                                                             // ERR_GENERIC_STUNNED (3066)
                case 38: return "You can't do that when you're dead.";                                         // ERR_PLAYER_DEAD (3387)
                case 39: return "You can't do that right now.";                                                // ERR_CLIENT_LOCKED_OUT (3008)
                case 40: case 73: return "Internal Bag Error";                                                 // ERR_INTERNAL_BAG_ERROR (3135)
                case 42: return "You can only equip one ammo pouch.";                                          // ERR_ONLY_ONE_AMMO (3332)
                case 43: return "Stackable items can't be wrapped.";                                           // ERR_CANT_WRAP_STACKABLE (2997)
                case 44: return "Equipped items can't be wrapped.";                                            // ERR_CANT_WRAP_EQUIPPED (2996)
                case 45: return "Wrapped items can't be wrapped.";                                             // ERR_CANT_WRAP_WRAPPED (2999)
                case 46: return "Bound items can't be wrapped.";                                               // ERR_CANT_WRAP_BOUND (2995)
                case 47: return "Unique items can't be wrapped.";                                              // ERR_CANT_WRAP_UNIQUE (2998)
                case 48: return "Bags can't be wrapped.";                                                      // ERR_CANT_WRAP_BAGS (2994)
                case 49: return "Already looted";                                                              // ERR_LOOT_GONE (3220)
                case 50: return "Inventory is full.";                                                          // ERR_INV_FULL (3156)
                case 51: return "Your bank is full";                                                           // ERR_BANK_FULL (2954)
                case 52: case 57: return "That item is currently sold out.";                                   // ERR_VENDOR_SOLD_OUT (3584)
                case 58: return "That object is busy.";                                                        // ERR_OBJECT_IS_BUSY (3331)
                case 60: return "You can't do that while in combat";                                           // ERR_NOT_IN_COMBAT (3310)
                case 61: return "You can't do that while disarmed";                                            // ERR_NOT_WHILE_DISARMED (3316)
                case 63: return "You don't have the required rank for that item";                              // ERR_CANT_EQUIP_RANK (2983)
                case 64: return "You don't have the required reputation for that item";                        // ERR_CANT_EQUIP_REPUTATION (2985)
                case 65: return "You cannot equip another bag of that type";                                   // ERR_TOO_MANY_SPECIAL_BAGS (3541)
                case 66: return "You can't loot that item now.";                                               // ERR_LOOT_CANT_LOOT_THAT_NOW (3218)
                case 67: return "You cannot equip more than one of those.";                                    // ERR_ITEM_UNIQUE_EQUIPPABLE (3171)
                case 68: return "You do not have the required items for that purchase";                        // ERR_VENDOR_MISSING_TURNINS (3582)
                case 69: return "You don't have enough honor points";                                          // ERR_NOT_ENOUGH_HONOR_POINTS (3306)
                case 70: return "You don't have enough arena points";                                          // ERR_NOT_ENOUGH_ARENA_POINTS (3304)
                case 71: return "You have the maximum number of those gems in your inventory or socketed into items.";   // ERR_ITEM_MAX_COUNT_SOCKETED (3165)
                case 72: return "You can't mail soulbound items.";                                             // ERR_MAIL_BOUND_ITEM (3234)
                case 75: return "You have the maximum number of those gems socketed into equipped items.";     // ERR_ITEM_MAX_COUNT_EQUIPPED_SOCKETED (3164)
                case 76: return "You cannot socket more than one of those gems into a single item.";           // ERR_ITEM_UNIQUE_EQUIPPABLE_SOCKETED (3172)
                case 77: return "At gold limit";                                                               // ERR_TOO_MUCH_GOLD (3542)
                case 78: return "You can't do that while in an arena match";                                   // ERR_NOT_DURING_ARENA_MATCH (3303)
                case 79: return "You can't trade a soulbound item.";                                           // ERR_TRADE_BOUND_ITEM (3546)
                case 80: return "You don't have the personal or team rating required to buy that item";        // ERR_CANT_EQUIP_RATING (2984)
                case 82: return "Artifacts can only be mailed to your own characters.";                        // ERR_NOT_SAME_ACCOUNT (3315)
                case 84: case 85: case 89:
                {
                    string n; int q; bool eq;
                    var tdb = ItemTipDb.Get(_files);
                    if (tdb == null || !tdb.LimitCategory(extra, out n, out q, out eq)) return null;
                    return msg == 84 ? "You can only carry " + q + " " + n                                     // ERR_ITEM_MAX_LIMIT_CATEGORY_COUNT_EXCEEDED_IS (3166)
                                     : "You can only equip " + q + (q == 1 ? " item" : " items") + " in the " + n + " category";   // ..._SOCKETED / _EQUIPPED_EXCEEDED_IS (3167-3168)
                }
                case 86: return "Your level is too high to use that item";                                     // ERR_SCALING_STAT_ITEM_LEVEL_EXCEEDED (3478)
                case 87: return "You must reach level " + extra + " to purchase that item.";                    // ERR_PURCHASE_LEVEL_TOO_LOW (3402)
                case 88: return "You do not have the required talent to equip that.";                          // ERR_CANT_EQUIP_NEED_TALENT (2982)
                case 90: return "Cannot equip item in this form";                                              // ERR_SHAPESHIFT_FORM_CANNOT_EQUIP (3485)
                case 91: return "Your inventory is full. Your satchel has been delivered to your mailbox.";    // ERR_ITEM_INVENTORY_FULL_SATCHEL (3161)
            }
            return null;                                                                                        // 59, 81, 83 and unknown codes: nothing shown
        }

        /// <summary>AzerothCore's InventoryResult names (Item.h), for the log.</summary>
        static string InventoryResultName(int msg)
        {
            switch (msg)
            {
                case 1: return "CANT_EQUIP_LEVEL_I"; case 2: return "CANT_EQUIP_SKILL"; case 3: return "ITEM_DOESNT_GO_TO_SLOT"; case 4: return "BAG_FULL";
                case 8: return "NO_REQUIRED_PROFICIENCY"; case 9: return "NO_EQUIPMENT_SLOT_AVAILABLE"; case 10: return "YOU_CAN_NEVER_USE_THAT_ITEM";
                case 13: return "CANT_EQUIP_WITH_TWOHANDED"; case 14: return "CANT_DUAL_WIELD"; case 17: return "CANT_CARRY_MORE_OF_THIS";
                case 20: return "ITEM_CANT_BE_EQUIPPED"; case 21: return "ITEMS_CANT_BE_SWAPPED"; case 22: return "SLOT_IS_EMPTY"; case 23: return "ITEM_NOT_FOUND";
                case 36: return "ITEM_LOCKED"; case 37: return "YOU_ARE_STUNNED"; case 38: return "YOU_ARE_DEAD"; case 39: return "CANT_DO_RIGHT_NOW";
                case 49: return "ALREADY_LOOTED"; case 50: return "INVENTORY_FULL"; case 58: return "OBJECT_IS_BUSY"; case 59: return "NONE";
                case 60: return "NOT_IN_COMBAT"; case 61: return "NOT_WHILE_DISARMED"; case 66: return "LOOT_CANT_LOOT_THAT_NOW"; case 67: return "ITEM_UNIQUE_EQUIPABLE";
                case 81: return "EVENT_AUTOEQUIP_BIND_CONFIRM"; case 84: return "ITEM_MAX_LIMIT_CATEGORY_COUNT_EXCEEDED"; case 87: return "PURCHASE_LEVEL_TOO_LOW";
                case 88: return "CANT_EQUIP_NEED_TALENT";
            }
            return "code";
        }

        /// <summary>
        /// SMSG_ITEM_PUSH_RESULT (PlayerStorage.cpp Player::SendNewItem): uint64 player, uint32 received (0 looted, 1 from an NPC),
        /// uint32 created, uint32 sendChatMessage, uint8 bag slot, uint32 slot (0xFFFFFFFF when added to a stack), uint32 item entry,
        /// uint32 suffix factor, int32 random property, uint32 count, uint32 count in inventory. Group loot sends it to the group: only
        /// our own becomes LootReceived (Amount = count, Text = the item's name - waits up to ItemInfoWait for the name).
        /// </summary>
        void OnItemPushResult(WowPacket p)
        {
            ulong who = p.U64();
            uint received = p.U32(), created = p.U32(), chat = p.U32();
            p.U8(); p.U32();
            int entry = p.I32();
            p.U32(); int randomProp = p.I32();                                   // suffix factor, random property ("data << int32(item->GetItemRandomPropertyId())")
            int count = p.I32();
            int total = p.Remaining >= 4 ? p.I32() : -1;
            if (who != _player.Guid)
            {
                if (_pushOddLogged.Add(-1)) WowLog.Info("Online items: SMSG_ITEM_PUSH_RESULT for another player (group loot) - not ours (logged once)");
                return;
            }
            string how = created != 0 ? "created" : received != 0 ? "received" : "looted";
            WowLog.Info("Online items: " + how + " item " + entry + " '" + (ItemInfoName(entry) ?? "?") + "' x" + count + " (" + total + " in the bags)" + (chat == 0 ? " - no chat message asked" : ""));
            if (chat == 0 || entry <= 0 || count <= 0) return;
            if (_itemProtos.ContainsKey(entry)) { EmitLootReceived(entry, count, randomProp); return; }
            AskItem(entry);
            _pendingPushes.Add(new PendingPush { Entry = entry, Count = count, RandomProp = randomProp, Until = _now + ItemInfoWait });
        }

        void EmitLootReceived(int entry, int count, int randomProp)
        {
            string name = ItemInfoNameOf(entry, randomProp) ?? "Item " + entry;   // with its random suffix ("Tumultuous Ring of the Sun");                 // "Item N" ours: the server never named it
            _events.Enqueue(new GameEvent { Type = GameEventType.LootReceived, ItemEntry = entry, Amount = count, Text = name });   // offline EmitItem's fields
        }
    }
}
