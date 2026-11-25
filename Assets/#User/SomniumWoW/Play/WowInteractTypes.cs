// iteration 11 - owner interact (wave-0 CONTRACT, w0play: P/harness/Probes/D11/interact/ContractDraft.cs revision 2 verbatim + SummonView of
// gap D2). FROZEN after wave 0: changes go to the integrator.
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    public enum TradeState : byte { None = 0, Requested = 1, Incoming = 2, Open = 3 }

    /// <summary>The trade window (TradeFrame). Slots 0..5 are traded, 6 = "Will not be traded".</summary>
    public sealed class TradeView
    {
        public const int Slots = 7, TradedSlots = 6;
        public TradeState State;
        public ulong PartnerGuid;
        public string PartnerName;
        public readonly ItemView[] Mine = new ItemView[Slots];     // our offer (tracked here: the server never echoes it), the bag's own ItemView
        public readonly int[] MineBag = { -1, -1, -1, -1, -1, -1, -1 };    // InvPos bag of each of ours (-1 = empty)
        public readonly int[] MineSlot = { -1, -1, -1, -1, -1, -1, -1 };   // InvPos slot of each of ours
        public readonly ItemView[] Theirs = new ItemView[Slots];   // the partner's offer (SMSG_TRADE_STATUS_EXTENDED), ItemInfoView copies with Count
        public readonly string[] TheirEnchant = new string[Slots]; // the partner item's permanent enchantment name (SpellItemEnchantment.dbc), null = none
        public int MyMoney, TheirMoney;                            // copper
        public bool MyAccepted, TheirAccepted;                     // TradeHighlightPlayer / TradeHighlightRecipient
        public float SecondsLeft;                                  // Incoming: until StaticPopup "TRADE" declines itself (timeout 60)
        public int Version;
    }

    public enum DuelState : byte { None = 0, Requested = 1, Incoming = 2, Countdown = 3, Fighting = 4 }

    public sealed class DuelView
    {
        public DuelState State;
        public ulong OpponentGuid, FlagGuid;                       // FlagGuid = the Duel Flag gameobject (PLAYER_DUEL_ARBITER)
        public string OpponentName;
        public float SecondsLeft;          // Incoming: popup timeout (60); Countdown: until the fight (3); OutOfBounds: until the forfeit (10)
        public bool OutOfBounds;           // SMSG_DUEL_OUTOFBOUNDS until SMSG_DUEL_INBOUNDS (StaticPopup DUEL_OUTOFBOUNDS)
        public int Version;
    }

    /// <summary>One talent tree of an inspected player (TalentTab.dbc); Points = the sum of the learned ranks of its talents.</summary>
    public sealed class InspectTree
    {
        public int TabId, Order;
        public string Name;
        public int Points;
    }

    /// <summary>InspectFrame: the other player's equipment (all 19 slots, also rings / trinkets that are not visible) and talents.</summary>
    public sealed class InspectView
    {
        public ulong Guid;
        public string Name;
        public int Level, Race;
        public WowClass Class;
        public bool Loaded;                                        // SMSG_INSPECT_TALENT arrived
        public readonly ItemView[] Equipment = new ItemView[19];   // by EquipSlot (Name null until the item query answered)
        public readonly string[] Enchants = new string[19];        // "Enchanted: <name>" + gem lines from SpellItemEnchantment.dbc, null = none
        public int TalentGroups, ActiveGroup, UnspentPoints;       // TalentGroups 0 = the realm hides talents (TalentsInspecting off)
        public readonly Dictionary<int, int> TalentRanks = new Dictionary<int, int>();   // the active group: Talent.dbc id -> learned rank 1..5
        public readonly List<InspectTree> Trees = new List<InspectTree>();               // the active group, TalentTab OrderIndex order
        public readonly List<string> Glyphs = new List<string>();                        // spell names of the active group's glyphs
        public int Version;
    }

    public enum ReadyCheckState : byte { None = 0, Waiting = 1, Ready = 2, NotReady = 3, Away = 4 }

    /// <summary>Raid data of one OTHER group member (PlayerState.Group has name, class, health ...).</summary>
    public sealed class RaidMemberView
    {
        public ulong Guid;
        public int SubGroup;                       // 0..7
        public bool Assistant, MainTank, MainAssist;
        public int Roles;                          // LFG roles (LfgView.Role* bits)
        public ReadyCheckState Ready;
    }

    public sealed class RaidView
    {
        public bool IsRaid;                        // SMSG_GROUP_LIST groupType & 0x02
        public bool IsLfgGroup, IsBattlegroundGroup;   // & 0x08, & 0x01 / 0x04
        public int MySubGroup;
        public bool IAmAssistant, IAmMainTank, IAmMainAssist;
        public bool CanLead;                       // leader, or assistant in a raid (ready checks, marks, assignments, sub groups)
        public readonly List<RaidMemberView> Members = new List<RaidMemberView>();
        // ready check (MSG_RAID_READY_CHECK*)
        public bool ReadyCheckActive;
        public bool ReadyCheckAsked;               // ReadyCheckFrame is up (we are not the initiator and did not answer)
        public bool ReadyCheckMine;                // we started it: our client sends MSG_RAID_READY_CHECK_FINISHED when the time is up
        public ulong ReadyCheckInitiator;
        public string ReadyCheckInitiatorName;
        public float ReadyCheckSecondsLeft;
        public ReadyCheckState MyReady;
        // raid target icons: index icon - 1 (0 Star .. 7 Skull) -> unit guid, 0 = unused
        public readonly ulong[] TargetIcons = new ulong[8];
        public int Version;

        public RaidMemberView Find(ulong guid)
        {
            for (int i = 0; i < Members.Count; i++) if (Members[i].Guid == guid) return Members[i];
            return null;
        }
    }

    public sealed class PvpView
    {
        public bool Flagged;                       // UNIT_FIELD_BYTES_2 byte 1 & 0x01 (the PvP icon)
        public bool FreeForAll;                    // & 0x04 (UI-PVP-FFA)
        public bool Desired;                       // PLAYER_FLAGS & 0x200 (the menu's check)
        public bool TimerRunning;                  // PLAYER_FLAGS & 0x40000 (PlayerPVPTimerText)
        public float TimerSecondsLeft;             // ours: 300 s counted from when the timer flag appeared
        public int TodayKills, YesterdayKills;     // PLAYER_FIELD_KILLS (two uint16)
        public int TodayHonor, YesterdayHonor;     // PLAYER_FIELD_TODAY_CONTRIBUTION / _YESTERDAY_CONTRIBUTION
        public int Version;
    }

    /// <summary>The Browse tab's filters (CMSG_AUCTION_LIST_ITEMS). -1 = any.</summary>
    public sealed class AuctionQuery
    {
        public string Name = "";
        public int LevelMin, LevelMax;             // 0 = any
        public int InventoryType = -1, ItemClass = -1, ItemSubClass = -1, Quality = -1;
        public bool Usable;
        public int Page;                           // 50 a page
        public int SortColumn = -1;                // AUCTION_SORT_* (AuctionHouseSearcher.h:34-49), -1 = none
        public bool SortDescending;
    }

    public sealed class AuctionView
    {
        public int Id;
        public ItemView Item;                      // name / icon / tooltip (ItemInfoView), Count = the stack
        public ulong OwnerGuid, BidderGuid;
        public string OwnerName;                   // name query, null until known
        public int StartBid, MinOutbid, Buyout, Bid;   // copper; Bid 0 = no bids
        public float SecondsLeft;
        public int TimeLeftBucket;                 // 1 Short .. 4 Very Long (AUCTION_TIME_LEFT1..4)
        public bool MineHighBid;                   // BidderGuid == us
        /// <summary>What the next bid must at least be (the real client's rule): Bid == 0 ? StartBid : Bid + MinOutbid.</summary>
        public int NextBid { get { return Bid == 0 ? StartBid : Bid + MinOutbid; } }
    }

    public sealed class AuctionHouseView
    {
        public ulong AuctioneerGuid;
        public int HouseId;                        // AuctionHouse.dbc
        public string HouseName;
        public int DepositRate, CutRate;           // % (AuctionHouse.dbc fields 2 / 3)
        public readonly List<AuctionView> Results = new List<AuctionView>();
        public readonly List<AuctionView> Owned = new List<AuctionView>();
        public readonly List<AuctionView> Bids = new List<AuctionView>();
        public int TotalResults, Page;
        public bool Searching;                     // between CMSG_AUCTION_LIST_ITEMS and its answer
        public float SearchDelay;                  // seconds the Search button stays grey (the answer's delay, AUCTION_SEARCH_DELAY 300 ms)
        public string LastResult;                  // the last SMSG_AUCTION_COMMAND_RESULT text (also an Info / Error event)
        public int Version;
    }

    public enum BattlefieldStatus : byte { None = 0, Queued = 1, Invited = 2, InProgress = 3 }

    public sealed class BattlefieldQueueView
    {
        public int Slot;
        public BattlefieldStatus Status;
        public int BgTypeId, ArenaType, MapId, MinLevel, MaxLevel, ClientInstanceId;
        public string Name;                        // BattlemasterList.dbc (arenas: "Arena 2v2" style - ours, from ArenaType)
        public bool Rated, Alliance;
        public float AvgWaitSeconds, QueuedSeconds;   // Queued
        public float SecondsLeft;                  // Invited: until the invite expires; InProgress: until auto-leave (0 = none)
        public float SinceStartSeconds;            // InProgress
    }

    public sealed class BattlefieldListView
    {
        public ulong BattlemasterGuid;             // 0 = from the PvP window
        public int BgTypeId, MinLevel, MaxLevel;
        public string Name;
        public bool IsRandom, HasWin;
        public int WinHonor, WinArena, LossHonor;
        public readonly List<int> Instances = new List<int>();   // client instance ids ("Warsong Gulch 3"); none = First Available only
    }

    public sealed class BattlefieldScoreView
    {
        public ulong Guid;
        public string Name;
        public int Race;
        public WowClass Class;
        public int Team = -1;                      // 0 horde, 1 alliance (arena: the PvPTeamId), -1 unknown
        public int KillingBlows, HonorableKills, Deaths, BonusHonor, Damage, Healing;
        public int[] Objectives = new int[0];
    }

    public sealed class BattlefieldView
    {
        public readonly BattlefieldQueueView[] Queues = { new BattlefieldQueueView { Slot = 0 }, new BattlefieldQueueView { Slot = 1 } };
        public BattlefieldListView List;           // the open battlemaster / PvP-window list, null = closed
        public readonly List<BattlefieldScoreView> Scores = new List<BattlefieldScoreView>();
        public bool ScoresArena, Ended;
        public int Winner = -1;                    // 0 horde, 1 alliance
        public string[] ArenaTeams = { "", "" };
        public bool InBattleground;                // a queue slot is InProgress
        public int Version;
    }

    public enum LfgStatus : byte { None = 0, RoleCheck = 1, Queued = 2, Proposal = 3, Boot = 4, InDungeon = 5, Finished = 6 }

    public sealed class LfgDungeonView
    {
        public int Id, Entry;                      // Entry = Id | TypeId << 24 (what CMSG_LFG_JOIN sends)
        public string Name;                        // LFGDungeons.dbc
        public int MinLevel, MaxLevel, MapId, Difficulty, TypeId, Expansion;
        public int LockStatus;                     // 0 = open (LfgLockStatusType LFG.h:79-93)
        public bool Done;                          // random: the first reward of the day is taken
        public int RewardMoney, RewardXp;
        public readonly List<ItemView> RewardItems = new List<ItemView>();
    }

    public sealed class LfgMemberView
    {
        public ulong Guid;
        public string Name;
        public int Roles, Level;
        public bool Chosen;                        // role check: ready
        public bool Answered, Accepted, IsMe;      // proposal
    }

    public sealed class LfgView
    {
        public const int RoleLeader = 0x01, RoleTank = 0x02, RoleHealer = 0x04, RoleDamage = 0x08;   // LfgRoles (LFG.h:37-44)
        public LfgStatus Status;
        public bool InfoLoaded;                    // SMSG_LFG_PLAYER_INFO arrived
        public readonly List<LfgDungeonView> RandomDungeons = new List<LfgDungeonView>();
        public readonly List<LfgDungeonView> Dungeons = new List<LfgDungeonView>();   // LFGDungeons.dbc rows of TypeId 1 for our level and expansion, LockStatus filled
        public readonly List<int> Queued = new List<int>();   // entries we are queued for
        public int MyRoles;
        // role check (SMSG_LFG_ROLE_CHECK_UPDATE / SMSG_LFG_ROLE_CHOSEN)
        public bool RoleCheckAsked;
        public float RoleCheckSecondsLeft;
        public readonly List<LfgMemberView> RoleCheck = new List<LfgMemberView>();
        // queue status (SMSG_LFG_QUEUE_STATUS, seconds)
        public int AvgWait, MyWait, TankWait, HealerWait, DamageWait, TanksNeeded, HealersNeeded, DamageNeeded;
        public float QueuedSeconds;
        // proposal (SMSG_LFG_PROPOSAL_UPDATE)
        public bool ProposalAsked;
        public int ProposalId, ProposalEntry, ProposalState, MyProposalRole;
        public string ProposalDungeonName;
        public float ProposalSecondsLeft;
        public readonly List<LfgMemberView> ProposalMembers = new List<LfgMemberView>();
        // kick vote (SMSG_LFG_BOOT_PROPOSAL_UPDATE)
        public bool BootActive, BootVoted;
        public ulong BootVictim;
        public string BootVictimName, BootReason;
        public int BootVotes, BootAgree, BootNeeded;
        public float BootSecondsLeft;
        public bool InLfgDungeon;                  // in an LFG group: the eye's Teleport In / Out
        public int Version;
    }

    /// <summary>A pending summon (IWowInteractions.SummonRequest; gap D2, w0play - interact's revision-1 shape).</summary>
    public sealed class SummonView
    {
        public ulong SummonerGuid;
        public string SummonerName;
        public int ZoneId;                         // AreaTable.dbc id of the summoner's zone
        public string ZoneName;                    // AreaTable.dbc name
        public float SecondsLeft;                  // the auto-decline time (MAX_PLAYER_SUMMON_DELAY 2 min) counted down
    }
}
