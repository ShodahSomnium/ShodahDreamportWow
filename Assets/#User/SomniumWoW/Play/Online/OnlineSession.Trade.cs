// iteration 11 - owner interact (proposal-interact.md 3.1 / 5.5 / 6.2.1): trading with another player on the realm.
// AzerothCore master: src/server/game/Handlers/TradeHandler.cpp, Entities/Player/TradeData.cpp, SharedDefines.h:3804-3830 (TradeStatus),
// ObjectDefines.h:29 (TRADE_DISTANCE). Our own offer is tracked here: the server never echoes it (TradeData::Update sends the partner's
// view to the OTHER side only, TradeData.cpp:54-73 / :100-106) - the real client shows its own side from its own bags too.
using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const float IxTradeYards = 11.11f;        // TRADE_DISTANCE (ObjectDefines.h:29)
        const float IxTradeAskGuard = 65f;        // ours: Requested falls back to None without an answer (the other client's popup declines at 60 s)
        const float IxTradePopupSeconds = 60f;    // StaticPopupDialogs["TRADE"].timeout = 60 (StaticPopup.lua:1291-1302)
        const int IxTradeSlotNonTraded = 6;       // TRADE_SLOT_NONTRADED (TradeData.h:29-32)
        const int IxBondingQuest = 4, IxBondingQuest2 = 5;   // BIND_QUEST_ITEM / BIND_QUEST_ITEM1 (ItemTemplate.h)
        // TradeStatus (SharedDefines.h:3804-3830)
        const uint IxTsBusy = 0, IxTsBeginTrade = 1, IxTsOpenWindow = 2, IxTsCanceled = 3, IxTsAccept = 4, IxTsBusy2 = 5, IxTsNoTarget = 6,
                   IxTsBackToTrade = 7, IxTsComplete = 8, IxTsRejected = 9, IxTsTooFar = 10, IxTsWrongFaction = 11, IxTsCloseWindow = 12,
                   IxTsIgnoreYou = 14, IxTsYouStunned = 15, IxTsTargetStunned = 16, IxTsYouDead = 17, IxTsTargetDead = 18, IxTsYouLogout = 19,
                   IxTsTargetLogout = 20, IxTsTrialAccount = 21, IxTsWrongRealm = 22, IxTsNotOnTaplist = 23;

        readonly TradeView _ixTrade = new TradeView();
        readonly ulong[] _ixTradeMine = new ulong[TradeView.Slots];   // the item guid of each of our slots (Mine / MineBag / MineSlot follow it)
        float _ixTradeAskAge;
        int _ixTradeBagsCount = -1;
        object _ixTradeBagsRef;

        public TradeView Trade { get { return _ixTrade; } }

        void IxTradeInit()
        {
            IxHandle(Op.SMSG_TRADE_STATUS, IxTradeOnStatus);
            IxHandle(Op.SMSG_TRADE_STATUS_EXTENDED, IxTradeOnExtended);
        }

        // ------------------------------------------------------------------ actions (IWowInteractions)

        /// <summary>UnitPopup TRADE -> InitiateTrade(unit): CMSG_INITIATE_TRADE (uint64 target, TradeHandler.cpp:721-724). Refused here
        /// first where WoW's texts exist (5.5); the server checks the rest and answers SMSG_TRADE_STATUS to the TARGET only (:853-855).</summary>
        public void InitiateTrade(ulong playerGuid)
        {
            if (playerGuid == 0) return;
            if (playerGuid == _player.Guid) { IxError("You can't trade with yourself."); return; }                   // ERR_TRADE_SELF (GlobalStrings.lua:3555)
            if (_world == null) { Error(NotYet); return; }
            if (_ixTrade.State != TradeState.None) { WowLog.Info("Online trade: InitiateTrade while a trade is " + _ixTrade.State + " - ignored (TradeHandler.cpp:726-729 ignores it too)"); return; }
            EntityState e = Find(playerGuid);
            if (e == null || e.Kind != EntityKind.Player) { IxError("Unknown unit."); return; }                         // ERR_UNIT_NOT_FOUND (:3565)
            if (_player.Dead || _player.Ghost) { IxError("You can't do that when you're dead."); return; }              // ERR_PLAYER_DEAD (:3387)
            if (e.Dead) { IxError("You can't trade with dead players."); return; }                                      // ERR_TRADE_TARGET_DEAD (:3557)
            if (IxYards(_player.Pos, e.Pos) > IxTradeYards) { IxError("Trade target is too far away."); return; }       // ERR_TRADE_TOO_FAR (:3561)
            IxTradeClear();
            _ixTrade.State = TradeState.Requested;
            _ixTrade.PartnerGuid = playerGuid;
            _ixTrade.PartnerName = IxName(playerGuid) ?? e.Name;
            _ixTradeAskAge = 0f;
            _ixTrade.Version++;
            IxSend(Op.CMSG_INITIATE_TRADE, new PacketWriter().U64(playerGuid), "CMSG_INITIATE_TRADE to " + (_ixTrade.PartnerName ?? IxGuidText(playerGuid)));
            IxInfo("You have requested to trade with " + (_ixTrade.PartnerName ?? "?") + ".");                       // ERR_INITIATE_TRADE_S (:3133) - ours: printed on send
        }

        /// <summary>StaticPopup "TRADE" (OnAccept BeginTrade / OnCancel CancelTrade): CMSG_BEGIN_TRADE (empty, :692-702) or CMSG_CANCEL_TRADE.</summary>
        public void AnswerTradeRequest(bool accept)
        {
            if (_ixTrade.State != TradeState.Incoming) { WowLog.Info("Online trade: answer (" + (accept ? "yes" : "no") + ") without a request - ignored"); return; }
            string who = _ixTrade.PartnerName ?? IxGuidText(_ixTrade.PartnerGuid);
            if (accept)
            {
                IxSend(Op.CMSG_BEGIN_TRADE, null, "CMSG_BEGIN_TRADE (accepting " + who + ")");
                _ixTrade.SecondsLeft = 0f;   // stays Incoming until OPEN_WINDOW (the popup hides when the window opens)
                _ixTrade.State = TradeState.Requested;
                _ixTradeAskAge = 0f;
            }
            else
            {
                IxSend(Op.CMSG_CANCEL_TRADE, null, "CMSG_CANCEL_TRADE (declining " + who + ")");
                IxTradeClear();
            }
            _ixTrade.Version++;
        }

        /// <summary>CMSG_SET_TRADE_ITEM (uint8 tradeSlot, uint8 bag, uint8 slot; TradeHandler.cpp:870-880). tradeSlot -1 = the first free of
        /// 0..5 (TradeFrame_GetAvailableSlot, TradeFrame.lua:188-197; none free = nothing, as WoW). Refused here when the server would cancel
        /// the WHOLE trade (TradeHandler.cpp:886-912): quest / soulbound items in slots 0..5, non-empty bags (ours: WoW's texts).</summary>
        public void SetTradeItem(int tradeSlot, int bag, int slot)
        {
            if (_ixTrade.State != TradeState.Open) { WowLog.Info("Online trade: SetTradeItem without an open trade - ignored"); return; }
            int bi = IxBagIndexOf(bag, slot);
            if (bi < 0) { WowLog.Info("Online trade: SetTradeItem(" + tradeSlot + ", " + bag + ", " + slot + ") - no item there"); return; }
            ulong guid = _bagPos[bi].Guid;
            int ts = tradeSlot;
            if (ts < 0)
            {
                ts = -1;
                for (int i = 0; i < TradeView.TradedSlots; i++) if (_ixTradeMine[i] == 0 || _ixTradeMine[i] == guid) { ts = i; break; }
                if (ts < 0) { WowLog.Info("Online trade: no free trade slot (1..6) - nothing placed (as WoW)"); return; }
            }
            if (ts >= TradeView.Slots) return;
            OwnedView ov;
            _ownedViews.TryGetValue(guid, out ov);
            if (ts < IxTradeSlotNonTraded && ov != null)
            {
                bool bound = (ov.FieldFlags & ItemFieldFlagSoulbound) != 0;
                bool quest = ov.Proto != null && (ov.Proto.Bonding == IxBondingQuest || ov.Proto.Bonding == IxBondingQuest2);
                if (quest) { IxError("You can't trade a quest item."); return; }                                      // ERR_TRADE_QUEST_ITEM (:3553)
                if (bound) { IxError("You can't trade a soulbound item."); return; }                                  // ERR_TRADE_BOUND_ITEM (:3546)
            }
            if (ov != null && ov.Proto != null && ov.Proto.ContainerSlots > 0 && IxBagHasItems(guid)) { IxError("You can't trade non-empty bags."); return; }   // ERR_TRADE_BAG (:3543)
            for (int i = 0; i < TradeView.Slots; i++)
                if (i != ts && _ixTradeMine[i] == guid) { IxTradeSetMine(i, 0); IxSend(Op.CMSG_CLEAR_TRADE_ITEM, new PacketWriter().U8(i), "CMSG_CLEAR_TRADE_ITEM " + i + " (the item moved to slot " + ts + ")"); }   // ours: it MOVES
            IxSend(Op.CMSG_SET_TRADE_ITEM, new PacketWriter().U8(ts).U8(_bagPos[bi].Bag).U8(_bagPos[bi].Slot), "CMSG_SET_TRADE_ITEM slot " + ts + " (bag " + _bagPos[bi].Bag + ", slot " + _bagPos[bi].Slot + ")");
            IxTradeSetMine(ts, guid);
            _ixTrade.MyAccepted = false;
            _ixTrade.TheirAccepted = false;   // TradeData::SetItem clears both accepts (TradeData.cpp:54-73)
            _ixTrade.Version++;
        }

        /// <summary>CMSG_CLEAR_TRADE_ITEM (uint8 tradeSlot, TradeHandler.cpp:935-948).</summary>
        public void ClearTradeItem(int tradeSlot)
        {
            if (_ixTrade.State != TradeState.Open || tradeSlot < 0 || tradeSlot >= TradeView.Slots) return;
            if (_ixTradeMine[tradeSlot] == 0) return;
            IxSend(Op.CMSG_CLEAR_TRADE_ITEM, new PacketWriter().U8(tradeSlot), "CMSG_CLEAR_TRADE_ITEM " + tradeSlot);
            IxTradeSetMine(tradeSlot, 0);
            _ixTrade.MyAccepted = false;
            _ixTrade.TheirAccepted = false;
            _ixTrade.Version++;
        }

        /// <summary>CMSG_SET_TRADE_GOLD (uint32 copper, TradeHandler.cpp:858-868), clamped to 0..our money (TradeFrame_UpdateMoney, TradeFrame.lua:174-186).</summary>
        public void SetTradeMoney(int copper)
        {
            if (_ixTrade.State != TradeState.Open) return;
            int c = Math.Max(0, Math.Min(copper, _player.Money));
            if (c == _ixTrade.MyMoney) return;
            IxSend(Op.CMSG_SET_TRADE_GOLD, new PacketWriter().U32((uint)c), "CMSG_SET_TRADE_GOLD " + c);
            _ixTrade.MyMoney = c;
            _ixTrade.MyAccepted = false;
            _ixTrade.TheirAccepted = false;
            _ixTrade.Version++;
        }

        /// <summary>The Trade button (AcceptTrade): CMSG_ACCEPT_TRADE (AC reads nothing, TradeHandler.cpp:237; empty).</summary>
        public void AcceptTrade()
        {
            if (_ixTrade.State != TradeState.Open || _ixTrade.MyAccepted) return;
            IxSend(Op.CMSG_ACCEPT_TRADE, null, "CMSG_ACCEPT_TRADE");
            _ixTrade.MyAccepted = true;
            _ixTrade.Version++;
        }

        /// <summary>Cancel while we accepted (CancelTradeAccept, TradeFrame.lua:148-154): CMSG_UNACCEPT_TRADE (empty, :683-690).</summary>
        public void UnacceptTrade()
        {
            if (_ixTrade.State != TradeState.Open || !_ixTrade.MyAccepted) return;
            IxSend(Op.CMSG_UNACCEPT_TRADE, null, "CMSG_UNACCEPT_TRADE");
            _ixTrade.MyAccepted = false;
            _ixTrade.Version++;
        }

        /// <summary>Closing the window (CloseTrade, TradeFrame.lua:156-159) / declining / withdrawing a request: CMSG_CANCEL_TRADE (empty;
        /// Player::TradeCancel -> TRADE_CANCELED to both, PlayerStorage.cpp:4198-4216).</summary>
        public void CancelTrade()
        {
            if (_ixTrade.State == TradeState.None) return;
            IxSend(Op.CMSG_CANCEL_TRADE, null, "CMSG_CANCEL_TRADE (" + _ixTrade.State + " with " + (_ixTrade.PartnerName ?? IxGuidText(_ixTrade.PartnerGuid)) + ")");
            IxTradeClear();
            _ixTrade.Version++;
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>SMSG_TRADE_STATUS (WorldSession::SendTradeStatus, TradeHandler.cpp:35-62): uint32 status; BEGIN_TRADE + uint64 trader;
        /// OPEN_WINDOW + uint32 trade id; CLOSE_WINDOW + uint32 InventoryResult, uint8 isTargetError, uint32 limited item; WRONG_REALM /
        /// NOT_ON_TAPLIST + uint8 slot. The texts: proposal-interact.md 5.5 (GlobalStrings.lua lines).</summary>
        void IxTradeOnStatus(WowPacket p)
        {
            uint status = p.U32();
            string who = _ixTrade.PartnerName ?? (_ixTrade.PartnerGuid != 0 ? IxName(_ixTrade.PartnerGuid) : null) ?? "?";
            switch (status)
            {
                case IxTsBeginTrade:
                    {
                        ulong trader = p.U64();
                        IxTradeClear();
                        _ixTrade.State = TradeState.Incoming;
                        _ixTrade.PartnerGuid = trader;
                        _ixTrade.PartnerName = IxName(trader);
                        _ixTrade.SecondsLeft = IxTradePopupSeconds;
                        IxInfo((_ixTrade.PartnerName ?? "Someone") + " has requested to trade with you.");                   // ERR_TRADE_REQUEST_S (:3554)
                        break;
                    }
                case IxTsOpenWindow:
                    {
                        if (p.Remaining >= 4) p.U32();   // the trade id (0)
                        ulong partner = _ixTrade.PartnerGuid;
                        string name = _ixTrade.PartnerName;
                        IxTradeClear();
                        _ixTrade.State = TradeState.Open;
                        _ixTrade.PartnerGuid = partner;
                        _ixTrade.PartnerName = name ?? IxName(partner);
                        _ixTradeBagsRef = null;
                        break;
                    }
                case IxTsAccept:
                    _ixTrade.TheirAccepted = true;
                    break;
                case IxTsBackToTrade:
                    // sent when either side's accept was cleared (an item / money changed) or the partner un-accepted (TradeData.cpp:122-135):
                    // like the real client both highlights go off (the server may still hold ours - documented, proposal 9.1 R4).
                    _ixTrade.MyAccepted = false;
                    _ixTrade.TheirAccepted = false;
                    break;
                case IxTsComplete:
                    IxTradeClear();
                    IxInfo("Trade complete.");                                                                       // ERR_TRADE_COMPLETE (:3548)
                    break;
                case IxTsCanceled:
                case IxTsRejected:
                    IxTradeClear();
                    IxInfo("Trade cancelled.");                                                                      // ERR_TRADE_CANCELLED (:3547)
                    break;
                case IxTsBusy:
                case IxTsBusy2:
                    IxTradeClear();
                    IxError(who + " is busy right now.");                                                            // ERR_PLAYER_BUSY_S (:3386)
                    break;
                case IxTsNoTarget: IxTradeClear(); IxError("Unknown unit."); break;                                  // ERR_UNIT_NOT_FOUND (:3565) - ours: the closest text
                case IxTsTooFar: IxTradeClear(); IxError("Trade target is too far away."); break;                     // ERR_TRADE_TOO_FAR (:3561)
                case IxTsWrongFaction: IxTradeClear(); IxError("Target is unfriendly."); break;                     // ERR_PLAYER_WRONG_FACTION (:3396)
                case IxTsIgnoreYou: IxTradeClear(); IxError(who + " is ignoring you."); break;                       // ERR_IGNORING_YOU_S (:3132)
                case IxTsYouStunned: IxTradeClear(); IxError("You are stunned"); break;                              // ERR_GENERIC_STUNNED (:3066)
                case IxTsTargetStunned: IxTradeClear(); IxError("Target is stunned"); break;                         // ERR_TARGET_STUNNED (:3514)
                case IxTsYouDead: IxTradeClear(); IxError("You can't do that when you're dead."); break;            // ERR_PLAYER_DEAD (:3387)
                case IxTsTargetDead: IxTradeClear(); IxError("You can't trade with dead players."); break;          // ERR_TRADE_TARGET_DEAD (:3557)
                case IxTsYouLogout: IxTradeClear(); IxError("You are logging out"); break;                           // ERR_LOGGING_OUT (:3214)
                case IxTsTargetLogout: IxTradeClear(); IxError("That player is logging out"); break;                // ERR_TARGET_LOGGING_OUT (:3511)
                case IxTsTrialAccount: IxTradeClear(); IxError("Trial accounts cannot perform that action"); break; // ERR_RESTRICTED_ACCOUNT (:3477)
                case IxTsCloseWindow:
                    {
                        int result = (int)p.U32();
                        bool targetError = p.U8() != 0;
                        if (p.Remaining >= 4) p.U32();   // the limited item's entry
                        IxTradeClear();
                        bool bagFull = result == 4 || result == 50 || result == 53 || result == 56 || result == 62 || result == 74;   // EQUIP_ERR_BAG_FULL* / INV_FULL (Items.cs InventoryResultText)
                        if (bagFull) IxError(targetError ? "Trade failed, target doesn't have enough space." : "Trade failed, you don't have enough space.");   // ERR_TRADE_TARGET_BAG_FULL (:3556) / ERR_TRADE_BAG_FULL (:3544)
                        else if (result == 29) IxError("You don't have enough money.");                             // EQUIP_ERR_NOT_ENOUGH_MONEY -> ERR_NOT_ENOUGH_MONEY (:3307)
                        else IxError(InventoryResultText(result, 0));
                        WowLog.Info("Online trade: SMSG_TRADE_STATUS CLOSE_WINDOW (InventoryResult " + result + (targetError ? ", the partner's" : "") + ")");
                        break;
                    }
                case IxTsWrongRealm:
                case IxTsNotOnTaplist:
                    {
                        int slot = p.Remaining >= 1 ? p.U8() : -1;
                        if (slot >= 0 && slot < TradeView.Slots) IxTradeSetMine(slot, 0);
                        IxError(status == IxTsWrongRealm ? "You may only trade conjured items to players from other realms"                        // ERR_TRADE_WRONG_REALM (:3562)
                                                         : "You may only trade bound items to players that were originally eligible to loot the item");   // ERR_TRADE_NOT_ON_TAPLIST (:3552)
                        break;
                    }
                default:
                    IxOdd("SMSG_TRADE_STATUS", "status " + status + " not handled");
                    break;
            }
            _ixTrade.Version++;
            WowLog.Info("Online trade: SMSG_TRADE_STATUS " + status + " -> " + _ixTrade.State + (_ixTrade.PartnerGuid != 0 ? " with " + (_ixTrade.PartnerName ?? IxGuidText(_ixTrade.PartnerGuid)) : ""));
        }

        /// <summary>SMSG_TRADE_STATUS_EXTENDED (WorldSession::SendUpdateTrade, TradeHandler.cpp:74-121): uint8 whichPlayer (1 = the partner's
        /// offer), uint32 trade id, uint32 7, uint32 7, uint32 money, uint32 spell on the non-traded slot, then 7 x { uint8 slot; uint32 entry,
        /// displayId, count, wrapped; uint64 gift creator; uint32 permanent enchant; 3 x uint32 gem enchants; uint64 creator; uint32 charges,
        /// suffix factor; int32 random property; uint32 lock id, max durability, durability } (an empty slot: the 18 uint32 are 0).</summary>
        void IxTradeOnExtended(WowPacket p)
        {
            byte which = p.U8();
            p.U32(); p.U32(); p.U32();
            uint money = p.U32();
            uint spell = p.U32();
            if (which != 1)
            {
                WowLog.Info("Online trade: SMSG_TRADE_STATUS_EXTENDED for our own side (a spell " + spell + " on our non-traded slot) - not shown (gap: enchanting through trade)");
                return;
            }
            _ixTrade.TheirMoney = (int)Math.Min(money, (uint)int.MaxValue);
            for (int k = 0; k < TradeView.Slots && p.Remaining >= 1; k++)
            {
                int idx = p.U8();
                int entry = (int)p.U32();
                p.U32();                         // display id
                int count = (int)p.U32();
                p.U32();                         // wrapped
                p.U64();                         // gift creator
                int perm = (int)p.U32();
                var inst = new ItemTipInstance();
                inst.Enchant[0] = perm;
                for (int s = 0; s < 3; s++) inst.Enchant[2 + s] = (int)p.U32();   // gem enchantments (SOCK_ENCHANTMENT_SLOT..+2)
                inst.Creator = p.U64();          // creator
                p.U32(); inst.SuffixFactor = (int)p.U32(); inst.RandomPropertyId = p.I32();   // charges, suffix factor, random property (TradeHandler.cpp:104-106)
                p.U32(); p.U32(); p.U32();       // lock id, max durability, durability
                if (idx < 0 || idx >= TradeView.Slots) continue;
                ItemView cur = _ixTrade.Theirs[idx];
                if (entry <= 0) { _ixTrade.Theirs[idx] = null; _ixTrade.TheirEnchant[idx] = null; continue; }
                if (cur == null || cur.Entry != entry || cur.Count != Math.Max(1, count) || inst.RandomPropertyId != 0 || perm > 0) _ixTrade.Theirs[idx] = ItemInfoViewOf(entry, count, inst);   // a fresh view, filled in place when the item answer arrives
                _ixTrade.TheirEnchant[idx] = perm > 0 ? IxEnchantName(perm) : null;
            }
            if (spell != 0) WowLog.Info("Online trade: the partner's non-traded slot carries spell " + spell + " (an enchant offered to us)");
            _ixTrade.Version++;
            WowLog.Info("Online trade: SMSG_TRADE_STATUS_EXTENDED - " + (_ixTrade.PartnerName ?? "the partner") + " offers " + IxTradeCount(_ixTrade.Theirs) + " item(s) and " + money + " copper");
        }

        // ------------------------------------------------------------------ per frame

        void IxTradeTick(float dt)
        {
            TradeView t = _ixTrade;
            switch (t.State)
            {
                case TradeState.Incoming:
                    t.SecondsLeft -= dt;
                    if (t.PartnerName == null) { t.PartnerName = ChatSender(t.PartnerGuid); if (t.PartnerName != null) t.Version++; }
                    if (t.SecondsLeft <= 0f)
                    {
                        WowLog.Info("Online trade: the request of " + (t.PartnerName ?? IxGuidText(t.PartnerGuid)) + " timed out after " + IxTradePopupSeconds + " s - declined (StaticPopup TRADE OnCancel)");
                        AnswerTradeRequest(false);
                    }
                    break;
                case TradeState.Requested:
                    _ixTradeAskAge += dt;
                    if (_ixTradeAskAge > IxTradeAskGuard)
                    {
                        WowLog.Info("Online trade: no answer from " + (t.PartnerName ?? IxGuidText(t.PartnerGuid)) + " in " + IxTradeAskGuard + " s - the request is dropped (ours)");
                        IxTradeClear();
                        t.Version++;
                    }
                    break;
                case TradeState.Open:
                    if (!ReferenceEquals(_ixTradeBagsRef, _player.Bags) || _ixTradeBagsCount != _player.Bags.Count || _ixTradeBagSig != IxTradeBagSig()) IxTradeRederive();
                    break;
            }
        }

        int _ixTradeBagSig;
        int IxTradeBagSig()
        {
            int sig = _bagPos.Count;
            for (int i = 0; i < _bagPos.Count; i++) sig = sig * 31 + (int)(_bagPos[i].Guid ^ (_bagPos[i].Guid >> 32)) + _bagPos[i].Bag * 7 + _bagPos[i].Slot;
            return sig;
        }

        /// <summary>After a bag rebuild: Mine / MineBag / MineSlot again from the item guids; an offered item no longer in the bags (sold,
        /// destroyed, moved to the bank) is taken out of the trade (ours; AzerothCore would cancel at the accept).</summary>
        void IxTradeRederive()
        {
            _ixTradeBagsRef = _player.Bags;
            _ixTradeBagsCount = _player.Bags.Count;
            _ixTradeBagSig = IxTradeBagSig();
            bool changed = false;
            for (int i = 0; i < TradeView.Slots; i++)
            {
                ulong g = _ixTradeMine[i];
                if (g == 0) continue;
                if (BagIndexOf(g) < 0)
                {
                    WowLog.Info("Online trade: the item in our trade slot " + i + " left the bags - cleared");
                    IxSend(Op.CMSG_CLEAR_TRADE_ITEM, new PacketWriter().U8(i), "CMSG_CLEAR_TRADE_ITEM " + i + " (the item is gone)");
                    IxTradeSetMine(i, 0);
                }
                else IxTradeSetMine(i, g);
                changed = true;
            }
            if (changed) _ixTrade.Version++;
        }

        // ------------------------------------------------------------------ helpers

        void IxTradeSetMine(int i, ulong guid)
        {
            _ixTradeMine[i] = guid;
            int bi = guid != 0 ? BagIndexOf(guid) : -1;
            if (bi < 0 || bi >= _player.Bags.Count)
            {
                if (guid != 0) _ixTradeMine[i] = 0;
                _ixTrade.Mine[i] = null;
                _ixTrade.MineBag[i] = -1;
                _ixTrade.MineSlot[i] = -1;
                return;
            }
            _ixTrade.Mine[i] = _player.Bags[bi];
            _ixTrade.MineBag[i] = _bagPos[bi].Bag;
            _ixTrade.MineSlot[i] = _bagPos[bi].Slot;
        }

        void IxTradeClear()
        {
            TradeView t = _ixTrade;
            t.State = TradeState.None;
            t.PartnerGuid = 0;
            t.PartnerName = null;
            t.MyMoney = t.TheirMoney = 0;
            t.MyAccepted = t.TheirAccepted = false;
            t.SecondsLeft = 0f;
            for (int i = 0; i < TradeView.Slots; i++)
            {
                _ixTradeMine[i] = 0;
                t.Mine[i] = null;
                t.MineBag[i] = -1;
                t.MineSlot[i] = -1;
                t.Theirs[i] = null;
                t.TheirEnchant[i] = null;
            }
        }

        /// <summary>The bag list index of the item at InvPos (bag, slot), -1 when none (Items.cs keeps _bagPos parallel to PlayerState.Bags).</summary>
        int IxBagIndexOf(int bag, int slot)
        {
            for (int i = 0; i < _bagPos.Count; i++) if (_bagPos[i].Bag == bag && _bagPos[i].Slot == slot) return i;
            return -1;
        }

        /// <summary>Game-layer helper (UiInteract.TryUseBagItem): the InvPos of the bag list entry k, false when out of range.</summary>
        internal bool IxInvPosOf(int bagIndex, out int bag, out int slot)
        {
            bag = slot = -1;
            if (bagIndex < 0 || bagIndex >= _bagPos.Count) return false;
            bag = _bagPos[bagIndex].Bag;
            slot = _bagPos[bagIndex].Slot;
            return true;
        }

        /// <summary>Game-layer helper (UiTrade portraits): a visible player's class (UNIT_FIELD_BYTES_0 byte 1), 0 when not in sight.</summary>
        internal WowClass IxClassOf(ulong guid)
        {
            OnlineObject o;
            return guid != 0 && _objects.TryGetValue(guid, out o) && o.IsUnit ? (WowClass)o.Byte(UF.UNIT_FIELD_BYTES_0, 1) : 0;
        }

        /// <summary>A bag (container item) that holds something (CONTAINER_FIELD_SLOT_n of its object).</summary>
        bool IxBagHasItems(ulong bagGuid)
        {
            OnlineObject bag;
            if (!_objects.TryGetValue(bagGuid, out bag) || bag.TypeId != TypeId.Container) return false;
            int n = Math.Max(0, Math.Min(36, bag.I32(UF.CONTAINER_FIELD_NUM_SLOTS)));
            for (int i = 0; i < n; i++) if (bag.U64(UF.CONTAINER_FIELD_SLOT_1 + i * 2) != 0) return true;
            return false;
        }

        /// <summary>SpellItemEnchantment.dbc Name_lang (field 14, measured by the designer: id 1 "Rockbiter 3").</summary>
        string IxEnchantName(int enchantId)
        {
            string n = IxDbcText("SpellItemEnchantment", enchantId, 14);
            return n.Length > 0 ? n : null;
        }

        static int IxTradeCount(ItemView[] items)
        {
            int n = 0;
            for (int i = 0; i < items.Length; i++) if (items[i] != null) n++;
            return n;
        }
    }
}
