// iteration 11 - owner interact (wave-0 CONTRACT, w0play: P/harness/Probes/D11/interact/ContractDraft.cs revision 2 verbatim + the summon
// members of gap D2 (w0play report): motion's proposal says summons are interact's, interact's says motion's - PLAN11 s2 puts "summon" in
// interact's P1 scope, so they live here, as interact's revision 1 had them). FROZEN after wave 0: changes go to the integrator.
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    /// <summary>
    /// CONTRACT (iteration 11, designer interact; pure C#). What one player does WITH other players on the realm: trade, duels, inspect,
    /// the raid (assistants, main tank / assist, sub groups, ready checks, raid target icons, vote kicks), the PvP flag, the Auction House,
    /// battlegrounds and the Dungeon Finder. IWowSession.Interactions returns it. Online: OnlineSession implements it (partials Trade / Duel /
    /// Inspect / Raid / Pvp / Auction / Battleground / Lfg). Offline: OfflineInteractions (Available = false; every call answers one Info).
    /// Every member is called from Unity's main thread. Views (the *View classes) are stable instances updated in place; their Version goes
    /// +1 on every change (views redraw on a change). Items are addressed by WoW's inventory position (InvPos: bag 255 + slot 23..38 =
    /// backpack, bag 19..22 + slot 0..n-1 = the bag in that slot) - ItemView.Bag / Slot of PlayerState.Bags. Owners may ADD members;
    /// never rename or remove.
    /// </summary>
    public interface IWowInteractions
    {
        /// <summary>False offline (no other WoW players): the UI greys these features out with the online-realm hint.</summary>
        bool Available { get; }

        // ---- trade (TradeHandler.cpp, TradeData.cpp) -----------------------------------------------------------------
        /// <summary>Never null; State None = no trade.</summary>
        TradeView Trade { get; }
        /// <summary>UnitPopup TRADE: CMSG_INITIATE_TRADE (State Requested until the other side answers).</summary>
        void InitiateTrade(ulong playerGuid);
        /// <summary>StaticPopup "TRADE" (State Incoming): true = CMSG_BEGIN_TRADE, false = CMSG_CANCEL_TRADE.</summary>
        void AnswerTradeRequest(bool accept);
        /// <summary>Puts our item at (bag, slot) in trade slot 0..6 (-1 = the first free of 0..5): CMSG_SET_TRADE_ITEM.</summary>
        void SetTradeItem(int tradeSlot, int bag, int slot);
        /// <summary>CMSG_CLEAR_TRADE_ITEM.</summary>
        void ClearTradeItem(int tradeSlot);
        /// <summary>CMSG_SET_TRADE_GOLD (copper, clamped to 0..Player.Money).</summary>
        void SetTradeMoney(int copper);
        /// <summary>The Trade button: CMSG_ACCEPT_TRADE.</summary>
        void AcceptTrade();
        /// <summary>Cancel while we accepted (TradeFrameCancelButton): CMSG_UNACCEPT_TRADE.</summary>
        void UnacceptTrade();
        /// <summary>Closing the window / declining / Requested -> cancel: CMSG_CANCEL_TRADE.</summary>
        void CancelTrade();

        // ---- duels (DuelHandler.cpp, spell 7266) ---------------------------------------------------------------------
        DuelView Duel { get; }
        /// <summary>UnitPopup DUEL = StartDuel: IWowSession.CastSpell(7266, playerGuid) (the Spells owner's cast).</summary>
        void RequestDuel(ulong playerGuid);
        /// <summary>StaticPopup "DUEL_REQUESTED": true = CMSG_DUEL_ACCEPTED, false = CMSG_DUEL_CANCELLED.</summary>
        void AnswerDuel(bool accept);
        /// <summary>/yield /concede /forfeit (SLASH_DUEL_CANCEL1-6): CMSG_DUEL_CANCELLED - a pending request is cancelled, a running duel lost.</summary>
        void ForfeitDuel();

        // ---- inspect (MiscHandler.cpp HandleInspectOpcode) ------------------------------------------------------------
        /// <summary>The open inspection, null = none.</summary>
        InspectView Inspection { get; }
        /// <summary>UnitPopup INSPECT: CMSG_INSPECT (28 yd); Inspection fills when SMSG_INSPECT_TALENT and the item answers arrive.</summary>
        void InspectPlayer(ulong playerGuid);
        void CloseInspect();

        // ---- raid (GroupHandler.cpp, Group.cpp) ----------------------------------------------------------------------
        /// <summary>Never null. Names / health of members stay in PlayerState.Group; this adds what raids need.</summary>
        RaidView Raid { get; }
        /// <summary>RaidFrameConvertToRaidButton (leader of a party of 2+, level 10+): CMSG_GROUP_RAID_CONVERT.</summary>
        void ConvertToRaid();
        /// <summary>RAID_PROMOTE / RAID_DEMOTE (raid leader): CMSG_GROUP_ASSISTANT_LEADER.</summary>
        void SetRaidAssistant(ulong guid, bool on);
        /// <summary>RAID_MAINTANK (leader / assistant): MSG_PARTY_ASSIGNMENT 0.</summary>
        void SetMainTank(ulong guid, bool on);
        /// <summary>RAID_MAINASSIST (leader / assistant): MSG_PARTY_ASSIGNMENT 1.</summary>
        void SetMainAssist(ulong guid, bool on);
        /// <summary>Leader / assistant in a raid, a member onto an empty place of sub group 0..7: CMSG_GROUP_CHANGE_SUB_GROUP.</summary>
        void MoveToSubGroup(ulong guid, int subGroup);
        /// <summary>Leader / assistant in a raid, two members trade places: CMSG_GROUP_SWAP_SUB_GROUP.</summary>
        void SwapSubGroups(ulong guidA, ulong guidB);
        /// <summary>RaidFrameReadyCheckButton / /readycheck (leader / assistant): MSG_RAID_READY_CHECK (empty).</summary>
        void StartReadyCheck();
        /// <summary>The ready-check frame's Yes / No: MSG_RAID_READY_CHECK uint8 1 / 0.</summary>
        void AnswerReadyCheck(bool ready);
        /// <summary>RAID_TARGET_n: icon 1..8 (Star .. Skull), 0 = clear; the same icon again clears it (SetRaidTargetIcon, TargetFrame.lua:703-709).
        /// MSG_RAID_TARGET_UPDATE.</summary>
        void SetRaidTargetIcon(ulong unitGuid, int icon);
        /// <summary>The icon 1..8 on that unit, 0 = none.</summary>
        int RaidTargetIconOf(ulong unitGuid);
        /// <summary>VOTE_TO_KICK in a Dungeon Finder group (StaticPopup VOTE_BOOT_REASON_REQUIRED's text): CMSG_GROUP_UNINVITE_GUID with the reason.</summary>
        void VoteToKick(ulong guid, string reason);

        // ---- PvP flag (MiscHandler.cpp HandleTogglePvP) ---------------------------------------------------------------
        PvpView Pvp { get; }
        /// <summary>PVP_ENABLE / PVP_DISABLE: CMSG_TOGGLE_PVP uint8 1 / 0.</summary>
        void SetPvp(bool on);
        /// <summary>The PvP icon a unit frame shows for this unit (TargetFrame_CheckFaction): 0 none, 1 UI-PVP-Alliance, 2 UI-PVP-Horde, 3 UI-PVP-FFA.</summary>
        int UnitPvpIcon(ulong unitGuid);
        /// <summary>PVP_REPORT_AFK (in a battleground): CMSG_REPORT_PVP_AFK.</summary>
        void ReportPvpAfk(ulong playerGuid);

        // ---- auction house (AuctionHouseHandler.cpp) ------------------------------------------------------------------
        /// <summary>The open auction house (after Interact on an auctioneer or its gossip option), null = closed.</summary>
        AuctionHouseView AuctionHouse { get; }
        /// <summary>The Browse tab's Search (page = query.Page): CMSG_AUCTION_LIST_ITEMS.</summary>
        void SearchAuctions(AuctionQuery query);
        /// <summary>The Auctions tab: CMSG_AUCTION_LIST_OWNER_ITEMS.</summary>
        void ListOwnAuctions();
        /// <summary>The Bids tab: CMSG_AUCTION_LIST_BIDDER_ITEMS.</summary>
        void ListBids();
        /// <summary>BID (copper) or BUYOUT (copper = its buyout): CMSG_AUCTION_PLACE_BID, after the real client's own checks (Error otherwise).</summary>
        void PlaceBid(int auctionId, int copper);
        /// <summary>CREATE_AUCTION of the whole stack at (bag, slot), hours 12 / 24 / 48: CMSG_AUCTION_SELL_ITEM.</summary>
        void CreateAuction(int bag, int slot, int startBid, int buyout, int hours);
        /// <summary>CANCEL_AUCTION: CMSG_AUCTION_REMOVE_ITEM.</summary>
        void CancelAuction(int auctionId);
        /// <summary>The deposit (copper) for the item at (bag, slot) and the duration (AuctionHouseMgr::GetAuctionDeposit), -1 while its sell price is unknown.</summary>
        int AuctionDeposit(int bag, int slot, int hours);
        void CloseAuctionHouse();

        // ---- battlegrounds (BattleGroundHandler.cpp) ------------------------------------------------------------------
        /// <summary>Never null: the two queue slots, the open battlemaster list, the scoreboard.</summary>
        BattlefieldView Battlefield { get; }
        /// <summary>The PvP window's list for one BattlemasterList.dbc id: CMSG_BATTLEFIELD_LIST.</summary>
        void RequestBattlefieldList(int bgTypeId);
        /// <summary>Join Battle / Join as Group: CMSG_BATTLEMASTER_JOIN (instanceId 0 = first available).</summary>
        void JoinBattlefield(int bgTypeId, int instanceId, bool asGroup);
        void CloseBattlefieldList();
        /// <summary>StaticPopup "CONFIRM_BATTLEFIELD_ENTRY" or the queue icon's Enter Battle / Leave Queue: CMSG_BATTLEFIELD_PORT 1 / 0.</summary>
        void AnswerBattlefieldInvite(int queueSlot, bool enter);
        /// <summary>Leave Battleground / Leave Arena: CMSG_LEAVE_BATTLEFIELD.</summary>
        void LeaveBattlefield();
        /// <summary>The scoreboard: MSG_PVP_LOG_DATA (empty).</summary>
        void RequestScores();

        // ---- dungeon finder (LFGHandler.cpp, LFGMgr.cpp) --------------------------------------------------------------
        /// <summary>Never null.</summary>
        LfgView Lfg { get; }
        /// <summary>Opening the Dungeon Finder: CMSG_LFD_PLAYER_LOCK_INFO_REQUEST (+ CMSG_LFD_PARTY_LOCK_INFO_REQUEST in a group).</summary>
        void RequestLfgInfo();
        /// <summary>Find Group: CMSG_LFG_JOIN (roles: LfgView.Role* bits; entries = LfgDungeonView.Entry values).</summary>
        void JoinLfg(int roles, IList<int> dungeonEntries);
        /// <summary>Leave Queue: CMSG_LFG_LEAVE.</summary>
        void LeaveLfg();
        /// <summary>The role-check popup: CMSG_LFG_SET_ROLES (0 = decline).</summary>
        void SetLfgRoles(int roles);
        /// <summary>Enter Dungeon / Leave Queue: CMSG_LFG_PROPOSAL_RESULT.</summary>
        void AnswerLfgProposal(bool accept);
        /// <summary>Teleport Out Of / To Dungeon: CMSG_LFG_TELEPORT.</summary>
        void LfgTeleport(bool outOfDungeon);
        /// <summary>StaticPopup VOTE_BOOT_PLAYER: CMSG_LFG_SET_BOOT_VOTE.</summary>
        void LfgBootVote(bool agree);

        // ---- summons (gap D2, w0play; facts: proposal-interact.md s3.6 - SpellEffects.cpp:4405-4432, MovementHandler.cpp:870-892) ----------
        /// <summary>A summon waiting for Accept / Cancel (SMSG_SUMMON_REQUEST: uint64 summoner, uint32 zone, uint32 auto-decline ms), null = none.
        /// StaticPopup "CONFIRM_SUMMON" (StaticPopup.lua:2534-2558); offline always null.</summary>
        SummonView SummonRequest { get; }
        /// <summary>CONFIRM_SUMMON's Accept / Cancel: CMSG_SUMMON_RESPONSE (uint64 summoner, uint8 agree). The server teleports on accept.</summary>
        void AnswerSummon(bool accept);
    }
}
