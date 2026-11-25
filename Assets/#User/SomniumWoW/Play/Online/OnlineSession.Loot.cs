using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>
    /// Stage O2 (owner lootdeath): loot online, as the real client does it against AzerothCore (sources quoted in P/acsrc_lootdeath).
    /// - Interact on a dead lootable creature (quests' Interact routes here first: TryLootInteract) = CMSG_LOOT (uint64 guid;
    ///   LootHandler.cpp HandleLootOpcode, which only accepts creatures - gameobject loot is opened by the server after
    ///   CMSG_GAMEOBJ_USE, so ANY SMSG_LOOT_RESPONSE opens CurrentLoot).
    /// - SMSG_LOOT_RESPONSE (Player::SendLoot / SendLootError): uint64 guid, uint8 lootType; LOOT_NONE (0) = uint8 LootError;
    ///   else the LootView (LootMgr.cpp operator&lt;&lt;(LootView)): uint32 gold, uint8 count, count x (uint8 slot, LootItem = uint32 itemid,
    ///   uint32 count, uint32 DisplayInfoID, uint32 randomSuffix, uint32 randomPropertyId, then uint8 LootSlotType).
    /// - LootItem = CMSG_AUTOSTORE_LOOT_ITEM (uint8 slot; HandleAutostoreLootItemOpcode), money = CMSG_LOOT_MONEY (empty;
    ///   HandleLootMoneyOpcode), CloseLoot = CMSG_LOOT_RELEASE (uint64 guid; HandleLootReleaseOpcode). The server answers
    ///   SMSG_LOOT_REMOVED (uint8 slot; Player::SendNotifyLootItemRemoved), SMSG_LOOT_CLEAR_MONEY (empty; SendNotifyLootMoneyRemoved),
    ///   SMSG_LOOT_MONEY_NOTIFY (uint32 amount, uint8 1 = "You loot..." / 0 = "Your share..."), SMSG_LOOT_RELEASE_RESPONSE (uint64 guid,
    ///   uint8 1; SendLootRelease). The item itself arrives as SMSG_ITEM_PUSH_RESULT (owner items -> LootReceived).
    /// - Item names/quality come from the server (SMSG_ITEM_QUERY_SINGLE_RESPONSE, ItemHandler.cpp HandleItemQuerySingleOpcode);
    ///   icons from ItemDisplayInfo.dbc of the loot's DisplayInfoID. The window shows once the names are known (at most
    ///   LdNameWaitSeconds), because the loot window redraws on count/entry changes only.
    /// - Group loot (roll / master / locked slot types) is not shown: groups online are stage O3b (README Known gaps).
    /// </summary>
    public sealed partial class OnlineSession
    {
        LootView _loot;

        public LootView CurrentLoot { get { return _loot; } }

        // LootMgr.h enum LootSlotType
        const int LdSlotAllowLoot = 0, LdSlotOwner = 4;
        // ObjectDefines.h:24 INTERACTION_DISTANCE 5.5f; ObjectDefines.h:44 DEFAULT_WORLD_OBJECT_SIZE 0.388999998569489f
        const float LdInteractionDistance = 5.5f, LdDefaultObjectSize = 0.388999998569489f;
        const int LdItemClassQuest = 12;                 // ItemTemplate.h ITEM_CLASS_QUEST
        // Ours (not WoW's):
        const float LdWalkAwayYards = 10f;               // the loot window closes beyond this (the same value as OfflineSession.Stats)
        const double LdNameWaitSeconds = 2.0;            // longest wait for the item names before the window shows anyway
        const double LdAskTimeoutSeconds = 5.0;          // a CMSG_LOOT without any answer is forgotten after this
        const double LdResendSeconds = 1.0;              // one CMSG_AUTOSTORE_LOOT_ITEM per slot per second at most
        const double LdAutoLootGraceSeconds = 1.0;       // auto loot: the window waits this long for the server to take everything

        sealed class LdEntry { public int Slot, Entry, Count, DisplayId, SlotType; public double AskedAt = -100; public ItemView View; public ItemTipInstance Roll; }
        sealed class LdItemInfo { public string Name; public int Class, Quality, InventoryType, DisplayId; public bool Missing; }

        readonly List<LdEntry> _ldEntries = new List<LdEntry>();          // the open loot's lootable slots, in LootView.Items order
        readonly Dictionary<int, LdItemInfo> _ldItemInfo = new Dictionary<int, LdItemInfo>();
        readonly HashSet<int> _ldItemAsked = new HashSet<int>();
        readonly HashSet<string> _ldOddLogged = new HashSet<string>();
        ulong _ldGuid;                   // the loot the server opened (0 = none)
        ulong _ldLastGuid;               // the last one (MONEY_NOTIFY may arrive after the window closed itself)
        int _ldMoney;
        double _ldOpenedAt, _ldMoneyAskedAt = -100, _ldAutoUntil = -1;
        bool _ldPublished;
        ulong _ldAskGuid;                // CMSG_LOOT sent, no answer yet
        double _ldAskAt;
        DbcTable _ldItemDisplay;
        bool _ldItemDisplayTried;
        int _ldResponses, _ldErrors, _ldRemoved, _ldMoneyNotifies, _ldReleases, _ldAsked, _ldItemAnswers;

        void InitLoot()
        {
            _world.On(Op.SMSG_LOOT_RESPONSE, LdOnLootResponse, this);
            _world.On(Op.SMSG_LOOT_REMOVED, LdOnLootRemoved, this);
            _world.On(Op.SMSG_LOOT_CLEAR_MONEY, LdOnClearMoney, this);
            _world.On(Op.SMSG_LOOT_MONEY_NOTIFY, LdOnMoneyNotify, this);
            _world.On(Op.SMSG_LOOT_RELEASE_RESPONSE, LdOnReleaseResponse, this);
            _world.On(Op.SMSG_ITEM_QUERY_SINGLE_RESPONSE, LdOnItemQuery, this);   // owner items may handle it too: several handlers per opcode run in order
        }

        void TickLoot(float dt)
        {
            if (_ldAskGuid != 0 && _now - _ldAskAt > LdAskTimeoutSeconds)
            {
                WowLog.Warn("Online loot: no answer to CMSG_LOOT for " + _ldAskGuid.ToString("X16") + " in " + LdAskTimeoutSeconds + " s");
                _ldAskGuid = 0;
            }
            if (_ldGuid == 0) return;
            if (!_ldPublished && _now >= _ldAutoUntil && (LdNamesKnown() || _now - _ldOpenedAt >= LdNameWaitSeconds)) LdPublish();
            // WoW closes the loot when the looter dies or walks away (ours: 10 yd, like OfflineSession.Stats); a creature or gameobject
            // that left the view closes it too. Items (containers opened from the bags) have no position.
            if (_player.Dead) { CloseLoot(); return; }
            int high = (int)(_ldGuid >> 48) & 0xFFFF;
            if (high == 0x4000) return;                                           // HighGuid::Item
            OnlineObject o;
            if (!_objects.TryGetValue(_ldGuid, out o)) { WowLog.Info("Online loot: " + _ldGuid.ToString("X16") + " left the view - loot closed"); CloseLoot(); return; }
            if (o.HasPos && LdDist(o.X, o.Y, o.Z) > LdWalkAwayYards) CloseLoot();
        }

        /// <summary>Quests' Interact routes here first: true = the guid is a lootable corpse / object and the loot request was sent.</summary>
        /// <remarks>Also true (with the Error line) for a dead creature that cannot be looted now - a dead unit is never a gossip target.
        /// Gameobjects answer false: the quests owner's CMSG_GAMEOBJ_USE makes the server open their loot.</remarks>
        internal bool TryLootInteract(ulong guid)
        {
            OnlineObject o;
            if (_world == null || !_objects.TryGetValue(guid, out o) || o.State == null || o.TypeId != TypeId.Unit) return false;
            var s = o.State;
            if (!s.Dead) return false;
            if (!s.Lootable && o.U32(UF.UNIT_NPC_FLAGS) != 0) return false;   // iteration 11 (= npc H2): npc flags = alive for the server (Creature.cpp:1988) - the routing talks to it
            if (_player.Dead) { Error("You can't do that when you're dead."); return true; }              // ERR_PLAYER_DEAD (GlobalStrings.lua:3387)
            if (!s.Lootable) { if (PfTrySkin(o)) return true; Error("There is nothing to loot"); return true; }   // ours, as OfflineSession.Interact; iteration 11: trainer L1 (skinning)
            float reach = LdInteractionDistance + LdMyReach() + LdReachOf(o);                             // Player::SendLoot: creature->IsWithinDistInMap(this, INTERACTION_DISTANCE)
            if (o.HasPos && LdDist(o.X, o.Y, o.Z) > reach) { Error("You are too far away to loot that corpse."); return true; }   // ERR_LOOT_TOO_FAR (GlobalStrings.lua:3230)
            _world.Send(Op.CMSG_LOOT, new PacketWriter().U64(guid));
            _ldAskGuid = guid;
            _ldAskAt = _now;
            _ldAsked++;
            return true;
        }

        public void LootItem(int index)
        {
            if (_world == null || _loot == null || _ldGuid == 0) return;
            if (index < 0 || index >= _ldEntries.Count) return;
            LdAskItem(_ldEntries[index]);
        }

        public void LootAll()
        {
            if (_world == null || _ldGuid == 0) return;
            LdTakeAll();
        }

        public void CloseLoot()
        {
            if (_ldGuid != 0 && _world != null) _world.Send(Op.CMSG_LOOT_RELEASE, new PacketWriter().U64(_ldGuid));
            LdClear();
        }

        // ------------------------------------------------------------------ server packets

        void LdOnLootResponse(WowPacket p)
        {
            ulong guid = p.U64();
            int type = p.U8();
            if (guid == _ldAskGuid) _ldAskGuid = 0;
            if (type == 0)                                                        // LOOT_NONE: Player::SendLootError
            {
                int err = p.Remaining > 0 ? p.U8() : -1;
                string text = LdLootErrorText(err);
                _ldErrors++;
                if (text != null) Error(text);
                else LdOdd("error " + err, "SMSG_LOOT_RESPONSE with unknown LootError " + err + " for " + guid.ToString("X16"));
                WowLog.Info("Online loot: " + guid.ToString("X16") + " refused (LootError " + err + (text != null ? " \"" + text + "\"" : "") + ")");
                return;
            }
            LdClear();                                                            // a new loot replaces the open one (Player::SendLoot released it)
            int gold = (int)p.U32();
            int count = p.U8();
            int shown = 0, skipped = 0;
            for (int i = 0; i < count; i++)
            {
                if (p.Remaining < 22) { LdOdd("short", "SMSG_LOOT_RESPONSE says " + count + " items but holds " + i + " (" + p.Remaining + " bytes left)"); break; }
                var e = new LdEntry { Slot = p.U8(), Entry = (int)p.U32(), Count = (int)p.U32(), DisplayId = (int)p.U32() };
                int suffixFactor = (int)p.U32(), randomProp = (int)p.U32();       // randomSuffix (the suffix factor), randomPropertyId (LootMgr.cpp:968-969)
                e.Roll = RollOf(suffixFactor, randomProp);                        // item tooltip fix: "of the Bear" + its stats
                e.SlotType = p.U8();
                if (e.SlotType != LdSlotAllowLoot && e.SlotType != LdSlotOwner)
                {
                    skipped++;
                    LdOdd("slot " + e.SlotType, "loot slot type " + e.SlotType + " (group roll / master / locked) is not shown - groups online are stage O3b");
                    continue;
                }
                if (e.Count < 1) e.Count = 1;
                if (e.DisplayId <= 0) e.DisplayId = _db.ItemDisplayId(e.Entry);
                _ldEntries.Add(e);
                LdQueryItem(e.Entry);
                shown++;
            }
            _ldGuid = guid;
            _ldLastGuid = guid;
            _ldMoney = gold;
            _ldOpenedAt = _now;
            _ldPublished = false;
            _ldResponses++;
            WowLog.Info("Online loot: opened " + guid.ToString("X16") + " (loot type " + type + "): " + gold + " copper, " + shown + " items" +
                        (skipped > 0 ? " (" + skipped + " group slots not shown)" : "") + (_player.AutoLoot ? ", auto loot" : ""));
            _ldAutoUntil = -1;
            if (_player.AutoLoot)
            {
                LdTakeAll();                                                      // AutoLoot setting (iteration 3): like the real client's auto loot
                _ldAutoUntil = _now + LdAutoLootGraceSeconds;                     // the window only shows if something stays (bags full)
            }
            if (_ldAutoUntil < 0 && LdNamesKnown()) LdPublish();
        }

        void LdOnLootRemoved(WowPacket p)
        {
            int slot = p.U8();
            if (_ldGuid == 0) return;
            for (int i = 0; i < _ldEntries.Count; i++)
            {
                if (_ldEntries[i].Slot != slot) continue;
                _ldEntries.RemoveAt(i);
                _ldRemoved++;
                LdAfterChange();
                return;
            }
        }

        void LdOnClearMoney(WowPacket p)
        {
            if (_ldGuid == 0) return;
            _ldMoney = 0;
            LdAfterChange();
        }

        void LdOnMoneyNotify(WowPacket p)
        {
            int amount = (int)p.U32();
            bool sole = p.Remaining > 0 ? p.U8() != 0 : true;
            _ldMoneyNotifies++;
            string text = sole ? "You loot " + LdMoneyText(amount)                      // YOU_LOOT_MONEY (GlobalStrings.lua:8989)
                               : "Your share of the loot is " + LdMoneyText(amount) + ".";  // LOOT_MONEY_SPLIT (GlobalStrings.lua:4809)
            _events.Enqueue(new GameEvent { Type = GameEventType.MoneyReceived, SourceGuid = _ldLastGuid, TargetGuid = _player.Guid, Amount = amount, Text = text });
        }

        void LdOnReleaseResponse(WowPacket p)
        {
            ulong guid = p.U64();
            _ldReleases++;
            if (guid == _ldAskGuid && guid != _ldGuid)
            {
                // Player::SendLoot releases without a loot window when the creature is gone, alive or out of INTERACTION_DISTANCE.
                _ldAskGuid = 0;
                WowLog.Info("Online loot: the server released " + guid.ToString("X16") + " without opening it (Player::SendLoot: gone, alive or out of reach)");
                return;
            }
            if (guid != 0 && guid == _ldGuid)
            {
                WowLog.Info("Online loot: the server closed " + guid.ToString("X16"));
                LdClear();                                                        // closed by the server: nothing to send back
            }
        }

        /// <summary>
        /// SMSG_ITEM_QUERY_SINGLE_RESPONSE head (ItemHandler.cpp HandleItemQuerySingleOpcode): uint32 ItemId (| 0x80000000 = no such
        /// item), uint32 Class, uint32 SubClass, int32 SoundOverrideSubclass, string Name, 3 x uint8 0, uint32 DisplayInfoID, uint32 Quality,
        /// uint32 Flags, uint32 Flags2, int32 BuyPrice, uint32 SellPrice, uint32 InventoryType, ... (the rest is owner items').
        /// </summary>
        void LdOnItemQuery(WowPacket p)
        {
            uint raw = p.U32();
            int entry = (int)(raw & 0x7FFFFFFF);
            var info = new LdItemInfo();
            if ((raw & 0x80000000u) != 0) info.Missing = true;
            else
            {
                try
                {
                    info.Class = (int)p.U32();
                    p.U32(); p.U32();                                             // SubClass, SoundOverrideSubclass
                    info.Name = p.CString();
                    p.CString(); p.CString(); p.CString();                        // Name2..4 (sent as empty strings)
                    info.DisplayId = (int)p.U32();
                    info.Quality = (int)p.U32();
                    p.U32(); p.U32(); p.U32(); p.U32();                           // Flags, Flags2, BuyPrice, SellPrice
                    info.InventoryType = (int)p.U32();
                }
                catch (PacketTooShortException)
                {
                    LdOdd("item short", "SMSG_ITEM_QUERY_SINGLE_RESPONSE for " + entry + " ends early (" + p.Length + " bytes) - what was read is kept");
                }
            }
            _ldItemInfo[entry] = info;
            _ldItemAnswers++;
            if (_ldGuid == 0) return;
            bool mine = false;
            for (int i = 0; i < _ldEntries.Count; i++)
                if (_ldEntries[i].Entry == entry) { mine = true; if (_ldEntries[i].View != null) LdFillView(_ldEntries[i].View, _ldEntries[i]); }
            if (mine && !_ldPublished && _now >= _ldAutoUntil && LdNamesKnown()) LdPublish();
        }

        // ------------------------------------------------------------------ helpers

        void LdAskItem(LdEntry e)
        {
            if (_now - e.AskedAt < LdResendSeconds) return;
            e.AskedAt = _now;
            _world.Send(Op.CMSG_AUTOSTORE_LOOT_ITEM, new PacketWriter().U8(e.Slot));
        }

        /// <summary>Loot everything: CMSG_LOOT_MONEY when there is money, then CMSG_AUTOSTORE_LOOT_ITEM for every lootable slot.</summary>
        void LdTakeAll()
        {
            if (_ldMoney > 0 && _now - _ldMoneyAskedAt >= LdResendSeconds)
            {
                _ldMoneyAskedAt = _now;
                _world.Send(Op.CMSG_LOOT_MONEY);
            }
            for (int i = 0; i < _ldEntries.Count; i++) LdAskItem(_ldEntries[i]);
        }

        /// <summary>After a slot or the money went: the view follows; an empty loot closes (the real client releases it then).</summary>
        void LdAfterChange()
        {
            if (_ldEntries.Count == 0 && _ldMoney <= 0) { CloseLoot(); return; }
            if (_ldPublished) LdRebuildView();
        }

        bool LdNamesKnown()
        {
            for (int i = 0; i < _ldEntries.Count; i++)
                if (!_ldItemInfo.ContainsKey(_ldEntries[i].Entry)) return false;
            return true;
        }

        void LdPublish()
        {
            _ldPublished = true;
            _loot = new LootView { CorpseGuid = _ldGuid };
            LdRebuildView();
            int missing = 0;
            for (int i = 0; i < _ldEntries.Count; i++) if (!_ldItemInfo.ContainsKey(_ldEntries[i].Entry)) missing++;
            if (missing > 0) WowLog.Info("Online loot: shown after " + LdNameWaitSeconds + " s with " + missing + " item names still unknown");
        }

        void LdRebuildView()
        {
            if (_loot == null) return;
            _loot.Money = _ldMoney;
            _loot.Items.Clear();
            for (int i = 0; i < _ldEntries.Count; i++)
            {
                var e = _ldEntries[i];
                if (e.View == null) { e.View = new ItemView(); LdFillView(e.View, e); }
                _loot.Items.Add(e.View);
            }
        }

        void LdFillView(ItemView v, LdEntry e)
        {
            LdItemInfo info;
            _ldItemInfo.TryGetValue(e.Entry, out info);
            v.Entry = e.Entry;
            v.Count = e.Count;
            v.DisplayId = e.DisplayId > 0 ? e.DisplayId : (info != null ? info.DisplayId : 0);
            v.Name = info != null && !info.Missing && !string.IsNullOrEmpty(info.Name) ? (e.Roll != null ? ItemInfoNameOf(e.Entry, e.Roll.RandomPropertyId) ?? info.Name : info.Name) : "Item " + e.Entry;   // ours until the server named it; + the random suffix
            v.Quality = info != null && !info.Missing ? info.Quality : 1;
            v.InventoryType = info != null ? info.InventoryType : 0;
            v.IsQuestItem = info != null && info.Class == LdItemClassQuest;
            v.IconPath = LdIconOf(v.DisplayId);
            v.Tooltip = ItemInfoTooltipOf(e.Entry, e.Count, e.Roll);                      // the bags' tooltip (Items.cs); null -> UiTooltip.Item falls back to the name
        }

        void LdQueryItem(int entry)
        {
            if (entry <= 0 || _ldItemInfo.ContainsKey(entry) || !_ldItemAsked.Add(entry)) return;
            _world.Send(Op.CMSG_ITEM_QUERY_SINGLE, new PacketWriter().U32((uint)entry));   // HandleItemQuerySingleOpcode reads uint32 item
        }

        string LdIconOf(int displayId)
        {
            if (displayId <= 0) return null;
            if (!_ldItemDisplayTried)
            {
                _ldItemDisplayTried = true;
                _ldItemDisplay = Dbc.Load(_files, "ItemDisplayInfo");
                if (_ldItemDisplay != null && _ldItemDisplay.FieldCount != DbcFields.ItemDisplayInfo.FieldCount) _ldItemDisplay = null;
            }
            if (_ldItemDisplay == null) return null;
            int r = _ldItemDisplay.FindRow(displayId);
            if (r < 0) return null;
            string icon = _ldItemDisplay.GetString(r, DbcFields.ItemDisplayInfo.InventoryIcon);
            return string.IsNullOrEmpty(icon) ? null : @"Interface\Icons\" + icon;
        }

        void LdClear()
        {
            _loot = null;
            _ldGuid = 0;
            _ldEntries.Clear();
            _ldMoney = 0;
            _ldPublished = false;
            _ldMoneyAskedAt = -100;
        }

        float LdDist(float x, float y, float z)
        {
            float dx = x - _player.Pos.X, dy = y - _player.Pos.Y, dz = z - _player.Pos.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>WorldObject::GetObjectSize: UNIT_FIELD_COMBATREACH for units, DEFAULT_WORLD_OBJECT_SIZE x scale otherwise.</summary>
        static float LdReachOf(OnlineObject o)
        {
            if (o != null && o.IsUnit)
            {
                float r = o.F32(UF.UNIT_FIELD_COMBATREACH);
                if (r > 0f && r < 100f) return r;
            }
            float scale = o != null ? o.F32(UF.OBJECT_FIELD_SCALE_X) : 1f;
            return LdDefaultObjectSize * (scale > 0.01f && scale < 100f ? scale : 1f);
        }

        float LdMyReach() { return _self != null ? LdReachOf(_self) : 1.5f; }       // DEFAULT_PLAYER_COMBAT_REACH (ObjectDefines.h) until our fields arrive

        /// <summary>LootMgr.h enum LootError -> the client's texts (GlobalStrings.lua; the same words LootMgr.h quotes). null = unknown.</summary>
        static string LdLootErrorText(int err)
        {
            switch (err)
            {
                case 0: return "You don't have permission to loot that corpse.";      // ERR_LOOT_DIDNT_KILL (GlobalStrings.lua:3219)
                case 4: return "You are too far away to loot that corpse.";           // ERR_LOOT_TOO_FAR (3230)
                case 5: return "You must be facing the corpse to loot it.";           // ERR_LOOT_BAD_FACING (3216)
                case 6: return "Someone is already looting that corpse.";             // ERR_LOOT_LOCKED (3221)
                case 8: return "You need to be standing up to loot something!";       // ERR_LOOT_NOTSTANDING (3225)
                case 9: return "You can't loot anything while stunned!";              // ERR_LOOT_STUNNED (3229)
                case 10: return "Player not found";                                   // ERR_LOOT_PLAYER_NOT_FOUND (3227)
                case 11: return "Maximum play time exceeded";                         // ERR_PLAY_TIME_EXCEEDED (3397)
                case 12: return "That player's inventory is full";                    // ERR_LOOT_MASTER_INV_FULL (3222)
                case 13: return "Player has too many of that item already";           // ERR_LOOT_MASTER_UNIQUE_ITEM (3224)
                case 14: return "Can't assign item to that player";                   // ERR_LOOT_MASTER_OTHER (3223)
                case 15: return "Your target has already had its pockets picked";     // ERR_ALREADY_PICKPOCKETED (2872)
                case 16: return "You can't do that while shapeshifted.";              // ERR_NOT_WHILE_SHAPESHIFTED (3320)
                default: return null;
            }
        }

        /// <summary>GOLD_AMOUNT / SILVER_AMOUNT / COPPER_AMOUNT ("%d Gold", GlobalStrings.lua:3843 / 1860), joined like OfflineSession.MoneyText.</summary>
        static string LdMoneyText(int copper)
        {
            if (copper < 0) copper = 0;
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            string t = "";
            if (g > 0) t = g + " Gold";
            if (s > 0) t += (t.Length > 0 ? ", " : "") + s + " Silver";
            if (c > 0 || t.Length == 0) t += (t.Length > 0 ? ", " : "") + c + " Copper";
            return t;
        }

        void LdOdd(string key, string text)
        {
            if (_ldOddLogged.Add(key)) WowLog.Warn("Online loot: " + text + " (logged once)");
        }

        /// <summary>"loot: 3 opened, 1 refused, 5 slots taken..." for the session summary and probes.</summary>
        public string LootSummary
        {
            get
            {
                return "loot: " + _ldAsked + " asked, " + _ldResponses + " opened, " + _ldErrors + " refused, " + _ldRemoved + " slots taken, " +
                       _ldMoneyNotifies + " money, " + _ldReleases + " releases, " + _ldItemAnswers + " item answers";
            }
        }
    }
}
