// iteration 11 - owner npc (proposal-npc.md 3.6 / 3.7 / 6.4): the bank (SMSG_SHOW_BANK, CMSG_AUTOBANK_ITEM / CMSG_AUTOSTORE_BANK_ITEM,
// CMSG_BUY_BANK_SLOT -> SMSG_BUY_BANK_SLOT_RESULT) and the innkeeper (gossip "Make this inn my home" -> SMSG_BINDER_CONFIRM -> the
// CONFIRM_BINDER popup -> CMSG_BINDER_ACTIVATE; SMSG_BINDPOINTUPDATE / SMSG_PLAYERBOUND -> PlayerState.Home*). AzerothCore master quoted.
// The bank's contents (Player.Bank) are built by OnlineSession.Items.cs RebuildInventory: AC sends every bank item object at login.
using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        // ---- AzerothCore values
        const int BankBagSlotsMax = 7;                  // NUM_BANKBAGSLOTS (Constants.lua:228): the client offers a purchase while fewer are bought
        const int BindSpell = 3286;                     // 'Bind' (NPCHandler.cpp:321 SendBindPoint)

        // ---- ours (named here, logged once in InitBank)
        const double BankWalkAwaySeconds = 1.0;         // out of interaction reach this long closes the bank (AC refuses bank moves silently beyond it)
        const double BankCheckSpacing = 0.25;           // reach checked 4x a second
        const string BinderPlaceFallback = "this inn";  // the %s of CONFIRM_BINDER when the area the player stands in is not known

        ulong _bankGuid;
        double _bankAwaySince = -1, _bankNextCheck, _binderNextCheck;
        bool _bankDbcTried;
        DbcTable _bankSlotPricesDbc;

        partial void InitBank()
        {
            if (_world == null) return;
            _world.On(Op.SMSG_SHOW_BANK, OnBankShow, this);
            _world.On(Op.SMSG_BUY_BANK_SLOT_RESULT, OnBankSlotResult, this);
            _world.On(Op.SMSG_BINDER_CONFIRM, OnBinderConfirm, this);
            _world.On(Op.SMSG_BINDPOINTUPDATE, OnBindPointUpdate, this);
            _world.On(Op.SMSG_PLAYERBOUND, OnPlayerBound, this);
            WowLog.Info("Online bank: ours - the bank closes " + BankWalkAwaySeconds + " s out of reach (checked every " + BankCheckSpacing +
                        " s); the innkeeper's question names the area you stand in ('" + BinderPlaceFallback + "' when unknown)");
        }

        // ------------------------------------------------------------------ bank

        public ulong BankerGuid { get { return _bankGuid; } }

        public void CloseBank() { BankClose("closed by the player"); }   // WoW's CloseBankFrame sends nothing

        void BankClose(string why)
        {
            if (_bankGuid == 0) return;
            WowLog.Info("Online bank: the bank of " + NpcName(_bankGuid) + " closes - " + why);
            _bankGuid = 0;
            _bankAwaySince = -1;
        }

        /// <summary>SMSG_SHOW_BANK (BankPackets.cpp ShowBank::Write: uint64 banker; WorldSession::SendShowBank BankHandler.cpp:186-192 sets
        /// m_currentBankerGUID). Accepted only after our CMSG_BANKER_ACTIVATE / gossip "Show me my bank".</summary>
        void OnBankShow(WowPacket p)
        {
            if (p.Remaining < 8) return;
            ulong g = p.U64();
            if (!NpcAnswerFor(g) && g != _bankGuid) { WowLog.Info("Online bank: SMSG_SHOW_BANK of " + g.ToString("X16") + " not asked for by us - ignored"); return; }
            NpcServiceOpened(g, "bank");
            _bankGuid = g;
            _bankAwaySince = -1;
            _bankNextCheck = 0;
            int items = 0;
            foreach (var c in _player.Bank) if (c != null && c.Size > 0) items += c.Size - c.Free;
            WowLog.Info("Online bank: the bank of " + NpcName(g) + " is open - " + items + " items in the bank, " + _player.BankBagSlotsBought + " of " + BankBagSlotsMax +
                        " bag slots bought" + (_player.NextBankSlotCost > 0 ? ", the next costs " + _player.NextBankSlotCost + " copper" : ""));
        }

        /// <summary>SMSG_BUY_BANK_SLOT_RESULT (BankPackets.cpp BuyBankSlotResult: uint32; Player.h:112-115 0 FAILED_TOO_MANY, 1 INSUFFICIENT_FUNDS,
        /// 2 NOTBANKER, 3 OK). The new slot itself arrives as PLAYER_BYTES_2 byte 2 (Items.cs rebuild).</summary>
        void OnBankSlotResult(WowPacket p)
        {
            if (p.Remaining < 4) return;
            uint r = p.U32();
            string text = null;
            switch (r)
            {
                case 0: text = "You've reached your limit of bag slots!"; break;           // ERR_BANKSLOT_FAILED_TOO_MANY (GlobalStrings.lua:2951)
                case 1: text = "You can't afford that."; break;                             // ERR_BANKSLOT_INSUFFICIENT_FUNDS (2952)
                case 2: text = "That unit is not a banker!"; break;                         // ERR_BANKSLOT_NOTBANKER (2953)
            }
            WowLog.Info("Online bank: SMSG_BUY_BANK_SLOT_RESULT " + r + (r == 3 ? " - a bank bag slot bought" : text != null ? " \"" + text + "\"" : ""));
            if (text != null) Error(text);
        }

        /// <summary>CMSG_BUY_BANK_SLOT (BuyBankSlot::Read: uint64 banker; BankHandler.cpp:143-184: price = BankBagSlotPrices.dbc[bought + 1]).
        /// The UI asked CONFIRM_BUY_BANK_SLOT first.</summary>
        public void BuyBankSlot()
        {
            if (_world == null) return;
            if (_bankGuid == 0) { Error("You are too far away from a bank."); return; }                 // ERR_NO_BANK_HERE (3323)
            int cost = _player.NextBankSlotCost;
            if (cost <= 0) { Error("You've reached your limit of bag slots!"); return; }               // ERR_BANKSLOT_FAILED_TOO_MANY (2951)
            if (cost > _player.Money) { Error("You can't afford that."); return; }                     // ERR_BANKSLOT_INSUFFICIENT_FUNDS (2952)
            _world.Send(Op.CMSG_BUY_BANK_SLOT, new PacketWriter().U64(_bankGuid));
            WowLog.Info("Online bank: buy bank bag slot " + (_player.BankBagSlotsBought + 1) + " for " + cost + " copper");
        }

        /// <summary>Right-click with the bank open (UseContainerItem's bank rule, ContainerFrame.lua:719-733): a bank item goes to the bags
        /// (CMSG_AUTOSTORE_BANK_ITEM, BankHandler.cpp:98-141), a bag item to the bank (CMSG_AUTOBANK_ITEM, :64-96); both uint8 bag, uint8 slot.</summary>
        public void BankItem(int bag, int slot)
        {
            if (_world == null) return;
            if (_bankGuid == 0) { Error("You are too far away from a bank."); return; }                 // ERR_NO_BANK_HERE (3323)
            ulong g = GuidAt(bag, slot);
            if (g == 0) { WowLog.Info("Online bank: nothing at bag " + bag + " slot " + slot); return; }
            bool fromBank = InvPos.IsBank(bag, slot);
            ushort op = fromBank ? Op.CMSG_AUTOSTORE_BANK_ITEM : Op.CMSG_AUTOBANK_ITEM;
            _world.Send(op, new PacketWriter().U8(bag).U8(slot));
            InvLock(g);
            WowLog.Info("Online bank: " + Op.Name(op) + " bag " + bag + " slot " + slot + (fromBank ? " (bank -> bags)" : " (bags -> bank)"));
        }

        /// <summary>BankBagSlotPrices.dbc (2 fields: ID, Cost; measured 1 -> 1000, 2 -> 10000, 3 -> 100000, 4..7 -> 250000 copper) of slot n.</summary>
        int BankSlotPrice(int n)
        {
            if (n < 1 || n > BankBagSlotsMax) return 0;
            if (!_bankDbcTried && _files != null)
            {
                _bankDbcTried = true;
                _bankSlotPricesDbc = Dbc.Load(_files, "BankBagSlotPrices");
                if (_bankSlotPricesDbc != null && _bankSlotPricesDbc.FieldCount != 2) { WowLog.Warn("Online bank: BankBagSlotPrices.dbc has " + _bankSlotPricesDbc.FieldCount + " fields, not 2 - no prices"); _bankSlotPricesDbc = null; }
                WowLog.Info("Online bank: BankBagSlotPrices.dbc " + (_bankSlotPricesDbc != null ? _bankSlotPricesDbc.RecordCount + " rows" : "missing"));
            }
            if (_bankSlotPricesDbc == null) return 0;
            int r = _bankSlotPricesDbc.FindRow(n);
            return r < 0 ? 0 : Math.Max(0, _bankSlotPricesDbc.GetInt(r, 1));
        }

        // ------------------------------------------------------------------ innkeeper / home

        /// <summary>SMSG_BINDER_CONFIRM (Player::SetBindPoint, Player.cpp:9111-9116: uint64 npc) after the gossip option "Make this inn my home"
        /// (OnGossipSelect case INNKEEPER, PlayerGossip.cpp:356-359) -> the CONFIRM_BINDER popup (StaticPopup.lua:2519-2531).</summary>
        void OnBinderConfirm(WowPacket p)
        {
            if (p.Remaining < 8) return;
            ulong npc = p.U64();
            _player.PendingBinderGuid = npc;
            string place = _placeSeen && _place.Known && _place.AreaId > 0 ? AreaName(_place.AreaId) : null;
            _player.PendingBinderPlace = place ?? BinderPlaceFallback;
            _binderNextCheck = 0;
            WowLog.Info("Online bank: SMSG_BINDER_CONFIRM from " + NpcName(npc) + " - asks 'Do you want to make " + _player.PendingBinderPlace + " your new home?'" +
                        (place == null ? " (ours: the area is not known)" : " (ours: the area you stand in)"));
        }

        /// <summary>Answers CONFIRM_BINDER: accept -> CMSG_BINDER_ACTIVATE (uint64 npc; NPCHandler.cpp:293-313 -> SendBindPoint: the innkeeper
        /// casts 3286 'Bind', SMSG_TRAINER_BUY_SUCCEEDED, then SMSG_BINDPOINTUPDATE + SMSG_PLAYERBOUND from Spell::EffectBind).</summary>
        public void ConfirmBinder(bool accept)
        {
            ulong npc = _player.PendingBinderGuid;
            _player.PendingBinderGuid = 0;
            if (npc == 0 || _world == null) return;
            if (!accept) { WowLog.Info("Online bank: the new home was declined"); return; }
            _world.Send(Op.CMSG_BINDER_ACTIVATE, new PacketWriter().U64(npc));
            WowLog.Info("Online bank: CMSG_BINDER_ACTIVATE to " + NpcName(npc) + " (spell " + BindSpell + " follows)");
        }

        /// <summary>SMSG_BINDPOINTUPDATE (Spell::EffectBind SpellEffects.cpp:6645-6651, also at every login Player.cpp:11768-11772): float x, y, z,
        /// uint32 map, uint32 area -> Home*; the Hearthstone tooltip's $z becomes the area's name (the item tooltips are rebuilt).</summary>
        void OnBindPointUpdate(WowPacket p)
        {
            if (p.Remaining < 20) return;
            float x = p.F32(), y = p.F32(), z = p.F32();
            int map = (int)p.U32(), area = (int)p.U32();
            bool changed = _player.HomeAreaId != area || _player.HomeMapId != map;
            _player.HomeMapId = map;
            _player.HomeAreaId = area;
            _player.HomePos = new WowPos(x, y, z, 0f);
            _player.HomeName = AreaName(area);
            WowLog.Info("Online bank: home = " + (_player.HomeName ?? "area " + area) + " (map " + map + ", " + x.ToString("0.0") + ", " + y.ToString("0.0") + ", " + z.ToString("0.0") + ")");
            if (changed) InvRebuildTooltips();
        }

        /// <summary>SMSG_PLAYERBOUND (Spell::EffectBind SpellEffects.cpp:6656-6659): uint64 caster, uint32 area -> ERR_DEATHBIND_SUCCESS_S "%s is now your home."
        /// (GlobalStrings.lua:3021) - the UI shows it from GameEvent HomeBound.</summary>
        void OnPlayerBound(WowPacket p)
        {
            if (p.Remaining < 12) return;
            ulong caster = p.U64();
            int area = (int)p.U32();
            string name = AreaName(area) ?? ("area " + area);
            _events.Enqueue(new GameEvent { Type = GameEventType.HomeBound, SourceGuid = caster, Amount = area, Text = name });
            WowLog.Info("Online bank: SMSG_PLAYERBOUND - " + name + " is now your home");
        }

        string AreaName(int area)
        {
            return area > 0 ? SpellCastResults.DbcName(_files, "AreaTable", area, DbcFields.AreaTable.AreaName_lang) : null;
        }

        // ------------------------------------------------------------------ per frame

        partial void TickBank(float dt)
        {
            if (_world == null) return;
            if (_bankGuid != 0 && _now >= _bankNextCheck)
            {
                _bankNextCheck = _now + BankCheckSpacing;
                if (_dialog != null) BankClose("a dialog opened (it replaces the bank panel)");
                else
                {
                    var e = Find(_bankGuid);
                    var o = FindObject(_bankGuid);
                    bool near = e != null && o != null && !e.Dead && !_player.Dead && QInReach(o, e, QInteractionDistance);
                    if (near) _bankAwaySince = -1;
                    else if (_bankAwaySince < 0) _bankAwaySince = _now;
                    else if (_now - _bankAwaySince >= BankWalkAwaySeconds) BankClose(e == null ? "the banker is gone" : _player.Dead ? "you died" : "out of reach");
                }
            }
            // CONFIRM_BINDER hides when CheckBinderDist() fails (StaticPopup.lua:2527-2530)
            ulong binder = _player.PendingBinderGuid;
            if (binder != 0 && _now >= _binderNextCheck)
            {
                _binderNextCheck = _now + BankCheckSpacing;
                var e = Find(binder);
                var o = FindObject(binder);
                if (e == null || o == null || _player.Dead || !QInReach(o, e, QInteractionDistance))
                {
                    _player.PendingBinderGuid = 0;
                    WowLog.Info("Online bank: the innkeeper is out of reach - the new-home question closes");
                }
            }
        }
    }
}
