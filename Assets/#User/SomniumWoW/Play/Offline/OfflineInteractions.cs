// iteration 11 - owner interact (filled: the designer's compiled draft is the final offline behaviour - proposal-interact.md s7)
// w0play: P/harness/Probes/D11/interact/OfflineAndFollowDraft.cs (OfflineInteractions part) verbatim + the summon members of gap D2.
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// Iteration 11 (interact): offline there are no other WoW players (Somnium players play their own simulations), so trade, duels,
    /// inspect, raids, the PvP flag, the Auction House, battlegrounds and the Dungeon Finder do not exist. Every call answers ONE Info line
    /// (ours, the PLAN10 "... is on the online realm" pattern of OfflineSession.Social.cs) and logs once per kind - a button pressed
    /// offline is never silent. The views stay empty (State None, null, empty lists).
    /// </summary>
    public sealed class OfflineInteractions : IWowInteractions
    {
        private readonly Action<string> _info;
        private readonly HashSet<string> _logged = new HashSet<string>();
        private readonly TradeView _trade = new TradeView();
        private readonly DuelView _duel = new DuelView();
        private readonly RaidView _raid = new RaidView();
        private readonly PvpView _pvp = new PvpView();
        private readonly BattlefieldView _bf = new BattlefieldView();
        private readonly LfgView _lfg = new LfgView();

        /// <summary>info = OfflineSession.EmitInfo.</summary>
        public OfflineInteractions(Action<string> info) { _info = info; }

        private void Offline(string what, string text)
        {
            if (_info != null) _info(text);
            if (_logged.Add(what)) WowLog.Info("Offline: " + what + " - " + text + " (offline there are no other WoW players).");
        }

        public bool Available { get { return false; } }

        public TradeView Trade { get { return _trade; } }
        public void InitiateTrade(ulong playerGuid) { Offline("trade", "Trading is on the online realm"); }      // ours
        public void AnswerTradeRequest(bool accept) { }
        public void SetTradeItem(int tradeSlot, int bag, int slot) { }
        public void ClearTradeItem(int tradeSlot) { }
        public void SetTradeMoney(int copper) { }
        public void AcceptTrade() { }
        public void UnacceptTrade() { }
        public void CancelTrade() { }

        public DuelView Duel { get { return _duel; } }
        public void RequestDuel(ulong playerGuid) { Offline("duel", "Duels are on the online realm"); }         // ours
        public void AnswerDuel(bool accept) { }
        public void ForfeitDuel() { }

        public InspectView Inspection { get { return null; } }
        public void InspectPlayer(ulong playerGuid) { Offline("inspect", "Inspecting is on the online realm"); } // ours
        public void CloseInspect() { }

        public RaidView Raid { get { return _raid; } }
        public void ConvertToRaid() { Offline("raid", "Raids are on the online realm"); }                      // ours
        public void SetRaidAssistant(ulong guid, bool on) { Offline("raid", "Raids are on the online realm"); }
        public void SetMainTank(ulong guid, bool on) { Offline("raid", "Raids are on the online realm"); }
        public void SetMainAssist(ulong guid, bool on) { Offline("raid", "Raids are on the online realm"); }
        public void MoveToSubGroup(ulong guid, int subGroup) { Offline("raid", "Raids are on the online realm"); }
        public void SwapSubGroups(ulong guidA, ulong guidB) { Offline("raid", "Raids are on the online realm"); }
        public void StartReadyCheck() { Offline("raid", "Raids are on the online realm"); }
        public void AnswerReadyCheck(bool ready) { }
        /// <summary>Offline a solo player marks units for himself, as WoW does without a group (the icon is only ours to see):
        /// icon 1..8, 0 or the unit's own icon again = clear; one icon per unit, one unit per icon (Group::SetTargetIcon's rule).</summary>
        public void SetRaidTargetIcon(ulong unitGuid, int icon)
        {
            if (unitGuid == 0 || icon < 0 || icon > 8) return;
            int had = RaidTargetIconOf(unitGuid);
            for (int i = 0; i < 8; i++) if (_raid.TargetIcons[i] == unitGuid) _raid.TargetIcons[i] = 0;
            if (icon != 0 && icon != had) _raid.TargetIcons[icon - 1] = unitGuid;
            _raid.Version++;
        }
        public int RaidTargetIconOf(ulong unitGuid)
        {
            if (unitGuid == 0) return 0;
            for (int i = 0; i < 8; i++) if (_raid.TargetIcons[i] == unitGuid) return i + 1;
            return 0;
        }
        public void VoteToKick(ulong guid, string reason) { }

        public PvpView Pvp { get { return _pvp; } }
        public void SetPvp(bool on) { Offline("pvp", "Player vs. Player is on the online realm"); }            // ours
        public int UnitPvpIcon(ulong unitGuid) { return 0; }
        public void ReportPvpAfk(ulong playerGuid) { }

        public AuctionHouseView AuctionHouse { get { return null; } }
        public void SearchAuctions(AuctionQuery query) { Offline("auction", "The Auction House is on the online realm"); }   // ours
        public void ListOwnAuctions() { }
        public void ListBids() { }
        public void PlaceBid(int auctionId, int copper) { }
        public void CreateAuction(int bag, int slot, int startBid, int buyout, int hours) { }
        public void CancelAuction(int auctionId) { }
        public int AuctionDeposit(int bag, int slot, int hours) { return -1; }
        public void CloseAuctionHouse() { }

        public BattlefieldView Battlefield { get { return _bf; } }
        public void RequestBattlefieldList(int bgTypeId) { Offline("battlegrounds", "Battlegrounds are on the online realm"); }   // ours
        public void JoinBattlefield(int bgTypeId, int instanceId, bool asGroup) { Offline("battlegrounds", "Battlegrounds are on the online realm"); }
        public void CloseBattlefieldList() { }
        public void AnswerBattlefieldInvite(int queueSlot, bool enter) { }
        public void LeaveBattlefield() { }
        public void RequestScores() { }

        public LfgView Lfg { get { return _lfg; } }
        public void RequestLfgInfo() { }
        public void JoinLfg(int roles, IList<int> dungeonEntries) { Offline("dungeon finder", "The Dungeon Finder is on the online realm"); }   // ours
        public void LeaveLfg() { }
        public void SetLfgRoles(int roles) { }
        public void AnswerLfgProposal(bool accept) { }
        public void LfgTeleport(bool outOfDungeon) { }
        public void LfgBootVote(bool agree) { }

        // ---- summons (gap D2, w0play): offline nobody summons
        public SummonView SummonRequest { get { return null; } }
        public void AnswerSummon(bool accept) { }
    }
}
