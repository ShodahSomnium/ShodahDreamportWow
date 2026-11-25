// iteration 11 - owner queues (proposal-interact.md P2, section 6.2.6): the Auction House online (wave-0 stub filled).
// Protocol = AzerothCore master: AuctionHouseHandler.cpp (P/acsrc11/interact), AuctionHouseSearcher.cpp:142-231 / :434-459, AuctionHouseMgr.cpp
// :91-112 / :142 / :206 / :260 / :292 / :580-584, AuctionHouseMgr.h:34-67. Texts = GlobalStrings.lua of the 3.3.5a install (GS line in comments).
// InitAuction / TickAuction are partial methods (OnlineSession.Hooks11.cs) called by interact's InitInteract / TickInteract.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const uint AhNpcFlag = 0x200000;            // UNIT_NPC_FLAG_AUCTIONEER (UnitDefines.h)
        const int AhPageSize = 50;                  // MAX_AUCTIONS_PER_PAGE (AuctionHouseMgr.h:36) = NUM_AUCTION_ITEMS_PER_PAGE (Blizzard_AuctionUI.lua:3)
        const int AhMaxBid = 2000000000;            // MAXIMUM_BID_PRICE (Blizzard_AuctionUI.lua:12)
        const int AhMinDeposit = 100;               // AH_MINIMUM_DEPOSIT (AuctionHouseMgr.h:34) x RATE_AUCTION_DEPOSIT (realm default 1 - ours: assumed, clients cannot see it)
        const int AhEnchantSlots = 7;               // MAX_INSPECTED_ENCHANTMENT_SLOT (Item.h:176)
        const float AhReachSlack = 1f;              // ours: yards added to the interaction reach before the frame closes by itself
        const float AhFarSeconds = 1f;              // ours: out of reach this long -> the frame closes (the npc designer's merchant rule)
        const float AhNameWait = 5f;                // ours: a notification waits this long for the item's name, then prints without it

        // AuctionHouse.dbc (measured 2026-10-03 with InteractDbcFacts: 21 fields): 2 DepositRate, 3 ConsignmentRate, 4 Name_lang (enUS slot)
        const int AhDbcDeposit = 2, AhDbcCut = 3, AhDbcName = 4;

        struct AhNote { public string Format; public int Entry; public double Until; }

        AuctionHouseView _ah;
        AuctionQuery _ahLastQuery;
        bool _ahResearch, _ahNamesPending;
        float _ahFar;
        DbcTable _ahDbc;
        bool _ahDbcTried;
        readonly List<AhNote> _ahNotes = new List<AhNote>();
        readonly Dictionary<ushort, int> _quSends = new Dictionary<ushort, int>();

        /// <summary>The open auction house (after Interact on an auctioneer or its gossip option), null = closed.</summary>
        public AuctionHouseView AuctionHouse { get { return _ah; } }

        partial void InitAuction()
        {
            // npc H4: the auctioneer flag opens the house with MSG_AUCTION_HELLO (AuctionHouseHandler.cpp:34-52: uint64 npc). An auctioneer
            // with the gossip flag shows its gossip first; its "Auction!" option makes the SERVER send MSG_AUCTION_HELLO (same handler).
            RegisterNpcService(AhNpcFlag, g => { QuSend(Op.MSG_AUCTION_HELLO, new PacketWriter().U64(g)); return true; }, "auction house");
            QuOn(Op.MSG_AUCTION_HELLO, AhOnHello);
            QuOn(Op.SMSG_AUCTION_LIST_RESULT, p => AhOnList(p, 0));
            QuOn(Op.SMSG_AUCTION_OWNER_LIST_RESULT, p => AhOnList(p, 1));
            QuOn(Op.SMSG_AUCTION_BIDDER_LIST_RESULT, p => AhOnList(p, 2));
            QuOn(Op.SMSG_AUCTION_COMMAND_RESULT, AhOnCommandResult);
            QuOn(Op.SMSG_AUCTION_BIDDER_NOTIFICATION, AhOnBidderNote);
            QuOn(Op.SMSG_AUCTION_OWNER_NOTIFICATION, AhOnOwnerNote);
            WowLog.Info("Online auction: the Auction House is handled (iteration 11: browse, bids, buyouts, create / cancel auctions; goods and money arrive by mail).");
        }

        // ------------------------------------------------------------------ requests (IWowInteractions)

        /// <summary>The Browse tab's Search: CMSG_AUCTION_LIST_ITEMS (AuctionHouseHandler.cpp:749-786: uint64 npc, uint32 listFrom, string name,
        /// uint8 levelMin, uint8 levelMax, uint32 inventoryType, uint32 itemClass, uint32 itemSubClass, uint32 quality, uint8 usable, uint8 getAll,
        /// uint8 sortCount, sortCount x (uint8 column, uint8 isDesc)); 0xFFFFFFFF = any.</summary>
        public void SearchAuctions(AuctionQuery q)
        {
            if (_ah == null || q == null || _world == null) return;
            _ahLastQuery = q;
            if (_ah.Searching || _ah.SearchDelay > 0f) { _ahResearch = true; return; }   // sent when the answer's delay allows (Tick)
            _ahResearch = false;
            var w = new PacketWriter().U64(_ah.AuctioneerGuid).U32((uint)(Math.Max(0, q.Page) * AhPageSize)).CString(q.Name ?? "")
                .U8(Clamp8(q.LevelMin)).U8(Clamp8(q.LevelMax))
                .U32(AhAny(q.InventoryType)).U32(AhAny(q.ItemClass)).U32(AhAny(q.ItemSubClass)).U32(AhAny(q.Quality))
                .U8(q.Usable ? 1 : 0).U8(0);                                       // getAll: never (ours)
            if (q.SortColumn >= 0 && q.SortColumn <= 10) w.U8(1).U8(q.SortColumn).U8(q.SortDescending ? 1 : 0);   // AuctionSortOrder (AuctionHouseSearcher.h:34-49)
            else w.U8(0);
            QuSend(Op.CMSG_AUCTION_LIST_ITEMS, w);
            _ah.Searching = true;
            _ah.Page = Math.Max(0, q.Page);
            _ah.Version++;
        }

        /// <summary>The Auctions tab: CMSG_AUCTION_LIST_OWNER_ITEMS (AuctionHouseHandler.cpp:723-747: uint64 npc, uint32 listFrom).</summary>
        public void ListOwnAuctions()
        {
            if (_ah == null || _world == null) return;
            QuSend(Op.CMSG_AUCTION_LIST_OWNER_ITEMS, new PacketWriter().U64(_ah.AuctioneerGuid).U32(0));
        }

        /// <summary>The Bids tab: CMSG_AUCTION_LIST_BIDDER_ITEMS (AuctionHouseHandler.cpp:671-721: uint64 npc, uint32 listFrom, uint32 n outbid ids).</summary>
        public void ListBids()
        {
            if (_ah == null || _world == null) return;
            QuSend(Op.CMSG_AUCTION_LIST_BIDDER_ITEMS, new PacketWriter().U64(_ah.AuctioneerGuid).U32(0).U32(0));
        }

        /// <summary>BID / BUYOUT: CMSG_AUCTION_PLACE_BID (uint64 npc, uint32 id, uint32 price; AuctionHouseHandler.cpp:425-592) after the real
        /// client's checks - the server drops a too-low bid without an answer.</summary>
        public void PlaceBid(int auctionId, int copper)
        {
            if (_ah == null || _world == null) return;
            AuctionView a = AhFind(auctionId);
            if (a == null) { Error("The item was not found."); return; }                                   // ERR_ITEM_NOT_FOUND (GlobalStrings.lua:3169)
            if (a.OwnerGuid == _player.Guid) { Error("You cannot bid on your own auction."); return; }      // ERR_AUCTION_BID_OWN (:2921)
            if (copper <= 0 || copper > AhMaxBid) { Error("Your bid increment is too small."); return; }    // ERR_AUCTION_BID_INCREMENT (:2920), ours for 0 / > MAXIMUM_BID_PRICE
            if (a.Buyout > 0 && copper >= a.Buyout) copper = a.Buyout;                                      // the server: price >= buyout = buyout
            else if (copper < a.NextBid || a.MineHighBid) { Error("Your bid increment is too small."); return; }   // BrowseBidButton's rule (Blizzard_AuctionUI.lua:908)
            if (copper > _player.Money) { Error("You don't have enough money."); return; }                  // ERR_NOT_ENOUGH_MONEY (:3307)
            QuSend(Op.CMSG_AUCTION_PLACE_BID, new PacketWriter().U64(_ah.AuctioneerGuid).U32((uint)auctionId).U32((uint)copper));
            WowLog.Info("Online auction: " + (a.Buyout > 0 && copper == a.Buyout ? "buyout" : "bid") + " of " + copper + " copper on auction " + auctionId + " sent.");
        }

        /// <summary>CREATE_AUCTION of the whole stack at (bag, slot): CMSG_AUCTION_SELL_ITEM (AuctionHouseHandler.cpp:117-160: uint64 npc,
        /// uint32 n, n x (uint64 item, uint32 count), uint32 bid, uint32 buyout, uint32 minutes 720 / 1440 / 2880).</summary>
        public void CreateAuction(int bag, int slot, int startBid, int buyout, int hours)
        {
            if (_ah == null || _world == null) return;
            ulong guid;
            OwnedView ov;
            string why = AhSellRefusal(bag, slot, out guid, out ov);
            if (why != null) { Error(why); return; }
            if (hours != 12 && hours != 24 && hours != 48) { WowLog.Warn("Online auction: duration " + hours + " h refused (12 / 24 / 48 only)."); return; }
            if (startBid < 1 || startBid > AhMaxBid || buyout < 0 || buyout > AhMaxBid) return;           // the frame keeps the button grey (AH.lua:1522-1547)
            if (buyout > 0 && buyout < startBid) { Error("(Must be >= the start price)"); return; }        // AUCTION_BUYOUT_ERROR (GlobalStrings.lua:600)
            int deposit = AuctionDeposit(bag, slot, hours);
            if (deposit > _player.Money) { Error("You don't have enough money."); return; }                // ERR_NOT_ENOUGH_MONEY (:3307)
            int count = Math.Max(1, ov.Count);
            QuSend(Op.CMSG_AUCTION_SELL_ITEM, new PacketWriter().U64(_ah.AuctioneerGuid).U32(1).U64(guid).U32((uint)count)
                .U32((uint)startBid).U32((uint)buyout).U32((uint)(hours * 60)));
            WowLog.Info("Online auction: create auction of item " + ov.Entry + " x" + count + " (start " + startBid + ", buyout " + buyout + ", " + hours + " h, deposit " + deposit + ") sent.");
        }

        /// <summary>CANCEL_AUCTION: CMSG_AUCTION_REMOVE_ITEM (uint64 npc, uint32 id; AuctionHouseHandler.cpp:594-669). The item comes back by mail.</summary>
        public void CancelAuction(int auctionId)
        {
            if (_ah == null || _world == null) return;
            QuSend(Op.CMSG_AUCTION_REMOVE_ITEM, new PacketWriter().U64(_ah.AuctioneerGuid).U32((uint)auctionId));
        }

        /// <summary>AuctionHouseMgr::GetAuctionDeposit (AuctionHouseMgr.cpp:91-112) for the stack at (bag, slot): -1 while its sell price is unknown.</summary>
        public int AuctionDeposit(int bag, int slot, int hours)
        {
            ulong guid;
            OwnedView ov = AhItemAt(bag, slot, out guid);
            if (ov == null || ov.Proto == null || ov.Proto.Missing) return -1;
            int rate = _ah != null ? _ah.DepositRate : 5;            // AuctionHouse.dbc: the faction houses 5, Blackwater 25 (ours: 5 before the house is known)
            return AhDepositFor(rate, ov.Proto.SellPrice, Math.Max(1, ov.Count), hours);
        }

        /// <summary>The server's arithmetic exactly (float multiplier, uint truncation): MSV 0 -> AH_MINIMUM_DEPOSIT; else
        /// uint((depositPercent * 3 / 100 * MSV * count / 3) * (hours / 12) * 3), at least AH_MINIMUM_DEPOSIT.</summary>
        internal static int AhDepositFor(int depositPercent, int sellPrice, int count, int hours)
        {
            if (sellPrice <= 0) return AhMinDeposit;
            float multiplier = depositPercent * 3 / 100f;                          // CalculatePct(float(entry->depositPercent), 3)
            uint timeHr = (uint)(hours * 3600 / 60 / 60 / 12);                     // (((time / 60) / 60) / 12) with time in seconds
            uint deposit = (uint)((multiplier * (uint)sellPrice * (uint)count / 3f) * timeHr * 3f * 1f);
            return deposit < AhMinDeposit ? AhMinDeposit : (int)Math.Min(deposit, int.MaxValue);
        }

        /// <summary>The real client sends nothing when the frame closes (CloseAuctionHouse only hides it).</summary>
        public void CloseAuctionHouse()
        {
            if (_ah == null) return;
            WowLog.Info("Online auction: the Auction House frame closed.");
            _ah = null;
            _ahResearch = false;
            _ahFar = 0f;
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>MSG_AUCTION_HELLO S->C (AuctionHouseHandler.cpp:54-75): uint64 npc, uint32 houseId (AuctionHouse.dbc), uint8 enabled.</summary>
        void AhOnHello(WowPacket p)
        {
            ulong npc = p.U64();
            int house = (int)p.U32();
            bool enabled = p.Remaining < 1 || p.U8() != 0;
            if (!enabled) { Error("The auction house is closed at the moment.\nPlease try again later."); return; }   // ERR_AUCTION_HOUSE_DISABLED (:2929)
            var v = new AuctionHouseView { AuctioneerGuid = npc, HouseId = house, DepositRate = 5, CutRate = 5 };   // ours until the DBC row is read
            DbcTable t = AhTable();
            int row = t != null ? t.FindRow(house) : -1;
            if (row >= 0)
            {
                v.DepositRate = t.GetInt(row, AhDbcDeposit);
                v.CutRate = t.GetInt(row, AhDbcCut);
                v.HouseName = t.GetLocalizedString(row, AhDbcName);
            }
            _ah = v;
            _ahFar = 0f;
            _ahResearch = false;
            NpcServiceOpened(npc, "auction house");
            WowLog.Info("Online auction: " + (v.HouseName ?? ("house " + house)) + " opened (deposit " + v.DepositRate + " %, cut " + v.CutRate + " %).");
            ListOwnAuctions();   // ours: the Auctions / Bids tabs are filled before they are first shown (the real client asks per tab)
            ListBids();
        }

        /// <summary>SMSG_AUCTION_LIST_RESULT / _OWNER_LIST_RESULT / _BIDDER_LIST_RESULT (AuctionHouseSearcher.cpp:142-231): uint32 n, n x AuctionInfo
        /// (148 B, :434-459), uint32 total, uint32 AUCTION_SEARCH_DELAY ms.</summary>
        void AhOnList(WowPacket p, int which)
        {
            AuctionHouseView v = _ah;
            int n = (int)p.U32();
            var list = new List<AuctionView>(Math.Min(n, AhPageSize * 2));
            for (int i = 0; i < n; i++) list.Add(AhReadInfo(p));
            int total = p.Remaining >= 4 ? (int)p.U32() : n;
            float delay = p.Remaining >= 4 ? p.U32() / 1000f : 0f;
            if (v == null) return;   // the frame was closed meanwhile
            List<AuctionView> into = which == 0 ? v.Results : which == 1 ? v.Owned : v.Bids;
            into.Clear();
            into.AddRange(list);
            if (which == 0)
            {
                v.TotalResults = total;
                v.Searching = false;
                v.SearchDelay = delay;
            }
            v.Version++;
        }

        AuctionView AhReadInfo(WowPacket p)
        {
            var a = new AuctionView();
            a.Id = (int)p.U32();
            int entry = (int)p.U32();
            var inst = new ItemTipInstance();
            for (int e = 0; e < AhEnchantSlots; e++) { int id = (int)p.U32(); p.U32(); p.U32(); if (e < 7) inst.Enchant[e] = id; }   // 7 x (enchant id, duration, charges)
            inst.RandomPropertyId = p.I32(); inst.SuffixFactor = (int)p.U32();   // random property id, suffix factor (AuctionHouseSearcher.cpp:446-447): the name + stats
            int count = (int)p.U32();
            p.U32(); p.U32();                            // spell charges, flags 0
            a.OwnerGuid = p.U64();
            a.StartBid = (int)p.U32();
            a.MinOutbid = (int)p.U32();
            a.Buyout = (int)p.U32();
            uint msLeft = p.U32();
            a.BidderGuid = p.U64();
            a.Bid = (int)p.U32();
            a.Item = ItemInfoViewOf(entry, Math.Max(1, count), inst);
            a.SecondsLeft = msLeft / 1000f;
            a.TimeLeftBucket = AhBucket(a.SecondsLeft);
            a.MineHighBid = a.BidderGuid != 0 && a.BidderGuid == _player.Guid;
            a.OwnerName = a.OwnerGuid == _player.Guid ? _player.Name : QuName(a.OwnerGuid);
            if (a.OwnerName == null) _ahNamesPending = true;
            return a;
        }

        /// <summary>AUCTION_TIME_LEFT1..4_DETAIL (GlobalStrings.lua:628-634): less than 30 mins, 30 mins - 2 hrs, 2 - 12 hrs, more.</summary>
        internal static int AhBucket(float seconds)
        {
            if (seconds < 30 * 60) return 1;
            if (seconds < 2 * 3600) return 2;
            if (seconds < 12 * 3600) return 3;
            return 4;
        }

        /// <summary>SMSG_AUCTION_COMMAND_RESULT (AuctionHouseHandler.cpp:77-87): uint32 id, uint32 action (0 sell, 1 cancel, 2 bid), uint32 error,
        /// [uint32 when error == 0 and action != 0]. Error codes AuctionHouseMgr.h:49-60.</summary>
        void AhOnCommandResult(WowPacket p)
        {
            int id = (int)p.U32();
            int action = (int)p.U32();
            int error = (int)p.U32();
            string text = AhResultText(action, error);
            if (_ah != null) { _ah.LastResult = text; _ah.Version++; }
            if (text != null) _events.Enqueue(new GameEvent { Type = error == 0 ? GameEventType.Info : GameEventType.Error, Text = text });
            WowLog.Info("Online auction: command result action " + action + " error " + error + " (auction " + id + ").");
            if (error != 0 || _ah == null) return;
            if (action == 0 || action == 1) ListOwnAuctions();
            else if (action == 2)
            {
                ListBids();
                for (int i = 0; i < _ah.Results.Count; i++)
                    if (_ah.Results[i].Id == id) { _ah.Results[i].MineHighBid = true; _ah.Results[i].BidderGuid = _player.Guid; }
                if (_ahLastQuery != null) _ahResearch = true;   // ours: the real client re-queries the page; sent once the delay allows
            }
        }

        /// <summary>The texts of the real client per result (proposal-interact.md 5.5; GlobalStrings.lua lines).</summary>
        internal static string AhResultText(int action, int error)
        {
            switch (error)
            {
                case 0:
                    if (action == 0) return "Auction created.";               // ERR_AUCTION_STARTED (:2939)
                    if (action == 1) return "Auction cancelled.";             // ERR_AUCTION_REMOVED (:2935)
                    return "Bid accepted.";                                   // ERR_AUCTION_BID_PLACED (:2922)
                case 1: return "Internal auction error.";                     // ERR_AUCTION_INVENTORY: AC never sends it - ours: ERR_AUCTION_DATABASE_ERROR's text
                case 2: return "Internal auction error.";                     // ERR_AUCTION_DATABASE_ERROR (:2925)
                case 3: return "You don't have enough money.";                // ERR_NOT_ENOUGH_MONEY (:3307)
                case 4: return "The item was not found.";                     // ERR_ITEM_NOT_FOUND (:3169)
                case 5: return "There is already a higher bid on that item."; // ERR_AUCTION_HIGHER_BID (:2928)
                case 7: return "Your bid increment is too small.";            // ERR_AUCTION_BID_INCREMENT (:2920)
                case 10: return "You cannot bid on your own auction.";        // ERR_AUCTION_BID_OWN (:2921)
                case 13: return "Trial accounts cannot perform that action";  // ERR_RESTRICTED_ACCOUNT (:3477)
                default: return "Internal auction error.";                    // ours (unknown code)
            }
        }

        /// <summary>SMSG_AUCTION_BIDDER_NOTIFICATION (AuctionHouseHandler.cpp:89-101): uint32 house, uint32 id, uint64 bidder, uint32 bid, uint32 diff,
        /// uint32 entry, uint32 0. bid 0 = we won (AuctionHouseMgr.cpp:142), else we were outbid (:292).</summary>
        void AhOnBidderNote(WowPacket p)
        {
            p.U32();
            int id = (int)p.U32();
            p.U64();
            uint bid = p.U32();
            p.U32();
            int entry = (int)p.U32();
            AhNotify(bid == 0 ? "You won an auction for %s" : "You have been outbid on %s.", entry);   // ERR_AUCTION_WON_S (:2941) / ERR_AUCTION_OUTBID_S (:2933)
            WowLog.Info("Online auction: " + (bid == 0 ? "won" : "outbid on") + " auction " + id + " (item " + entry + ").");
            if (_ah != null && bid != 0) ListBids();
        }

        /// <summary>SMSG_AUCTION_OWNER_NOTIFICATION (AuctionHouseHandler.cpp:103-115): uint32 id, uint32 bid, uint32 0, uint64 0, uint32 entry, uint32 0,
        /// float 0. bid > 0 = sold (AuctionHouseMgr.cpp:206), 0 = expired unsold (:260).</summary>
        void AhOnOwnerNote(WowPacket p)
        {
            int id = (int)p.U32();
            uint bid = p.U32();
            p.U32(); p.U64();
            int entry = (int)p.U32();
            AhNotify(bid > 0 ? "A buyer has been found for your auction of %s." : "Your auction of %s has expired.", entry);   // ERR_AUCTION_SOLD_S (:2938) / _EXPIRED_S (:2927)
            WowLog.Info("Online auction: own auction " + id + " " + (bid > 0 ? "sold" : "expired") + " (item " + entry + ").");
            if (_ah != null) ListOwnAuctions();
        }

        /// <summary>A system line with the item's name: printed now when the name is known, else when the item answer arrives (at most AhNameWait s).</summary>
        void AhNotify(string format, int entry)
        {
            string name = ItemInfoName(entry);
            if (name != null) { AhPrint(format, name); return; }
            _ahNotes.Add(new AhNote { Format = format, Entry = entry, Until = _now + AhNameWait });
        }

        void AhPrint(string format, string name)
        {
            GrpText(format.Replace("%s", name), false);   // a system line in the chat (ChatKind.System)
        }

        partial void TickAuction(float dt)
        {
            for (int i = _ahNotes.Count - 1; i >= 0; i--)
            {
                AhNote n = _ahNotes[i];
                string name = ItemInfoName(n.Entry);
                if (name == null && _now < n.Until) continue;
                AhPrint(n.Format, name ?? "item");        // ours: "item" when the server never named it
                _ahNotes.RemoveAt(i);
            }
            AuctionHouseView v = _ah;
            if (v == null) return;
            if (v.SearchDelay > 0f) { v.SearchDelay = Math.Max(0f, v.SearchDelay - dt); if (v.SearchDelay <= 0f) v.Version++; }
            if (_ahResearch && !v.Searching && v.SearchDelay <= 0f && _ahLastQuery != null) SearchAuctions(_ahLastQuery);
            if (_ahNamesPending) AhFillNames(v);
            // the auctioneer gone or out of reach for a second -> the frame closes (ours: the merchant rule)
            OnlineObject o;
            bool near = _objects.TryGetValue(v.AuctioneerGuid, out o) && o.State != null && AhInReach(o, o.State);
            _ahFar = near ? 0f : _ahFar + dt;
            if (_ahFar > AhFarSeconds)
            {
                WowLog.Info("Online auction: the auctioneer is out of reach - the Auction House frame closed.");
                CloseAuctionHouse();
            }
        }

        void AhFillNames(AuctionHouseView v)
        {
            bool still = false, changed = false;
            List<AuctionView>[] lists = { v.Results, v.Owned, v.Bids };
            for (int l = 0; l < lists.Length; l++)
                for (int i = 0; i < lists[l].Count; i++)
                {
                    AuctionView a = lists[l][i];
                    if (a.OwnerName != null || a.OwnerGuid == 0) continue;
                    string n = ChatSender(a.OwnerGuid);
                    if (n == null) { still = true; continue; }
                    a.OwnerName = n;
                    changed = true;
                }
            _ahNamesPending = still;
            if (changed) v.Version++;
        }

        /// <summary>Interaction reach (INTERACTION_DISTANCE 5.5 + both combat reaches, as Quests.cs QInReach) + AhReachSlack.</summary>
        bool AhInReach(OnlineObject o, EntityState e)
        {
            float mine = _self != null ? _self.F32(UF.UNIT_FIELD_COMBATREACH) : 0f;
            if (!(mine > 0f && mine < 50f)) mine = 1.5f;              // DEFAULT_COMBAT_REACH (ObjectDefines.h:45)
            float theirs = o.IsUnit ? o.F32(UF.UNIT_FIELD_COMBATREACH) : 0f;
            if (!(theirs > 0f && theirs < 50f)) theirs = 1.5f;
            float max = 5.5f + mine + theirs + AhReachSlack;          // INTERACTION_DISTANCE (ObjectDefines.h:24)
            float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z;
            return dx * dx + dy * dy + dz * dz < max * max;
        }

        // ------------------------------------------------------------------ items

        AuctionView AhFind(int id)
        {
            if (_ah == null) return null;
            List<AuctionView>[] lists = { _ah.Results, _ah.Bids, _ah.Owned };
            for (int l = 0; l < lists.Length; l++)
                for (int i = 0; i < lists[l].Count; i++) if (lists[l][i].Id == id) return lists[l][i];
            return null;
        }

        /// <summary>Our item at WoW's inventory position (bag 255 + slot 23..38 = the backpack, bag 19..22 + slot = a bag), from the items owner's list.</summary>
        OwnedView AhItemAt(int bag, int slot, out ulong guid)
        {
            guid = 0;
            for (int i = 0; i < _bagPos.Count; i++)
            {
                BagPos bp = _bagPos[i];
                if (bp.Bag != bag || bp.Slot != slot) continue;
                guid = bp.Guid;
                OwnedView ov;
                return _ownedViews.TryGetValue(bp.Guid, out ov) ? ov : null;
            }
            return null;
        }

        /// <summary>The real client's refusals when an item is put into the sell slot (its C code; texts GlobalStrings.lua:2919-2942), matching the
        /// server's checks (AuctionHouseHandler.cpp:200-220: CanBeTraded, not a non-empty bag, not conjured, no duration). Null = may be sold.</summary>
        string AhSellRefusal(int bag, int slot, out ulong guid, out OwnedView ov)
        {
            ov = AhItemAt(bag, slot, out guid);
            if (ov == null || guid == 0) return "The item was not found.";                                        // ERR_ITEM_NOT_FOUND (:3169)
            ItemProto d = ov.Proto;
            if (d == null || d.Missing) return "The item was not found.";
            if (d.Bonding == 4) return "You cannot sell a quest item.";                                            // ERR_AUCTION_QUEST_ITEM (:2934); BIND_QUEST_ITEM 4
            if ((ov.FieldFlags & ItemFieldFlagSoulbound) != 0) return "You cannot sell a soulbound item.";         // ERR_AUCTION_BOUND_ITEM (:2923)
            if ((d.Flags & 0x2u) != 0) return "You cannot auction a conjured item.";                              // ERR_AUCTION_CONJURED_ITEM (:2924); ITEM_FLAG_CONJURED
            if (d.Duration > 0) return "You cannot auction items with a limited duration.";                       // ERR_AUCTION_LIMITED_DURATION_ITEM (:2930)
            if (d.MaxDurability > 0 && ov.Durability < ov.MaxDurability) return "You must repair that item before you auction it.";   // ERR_AUCTION_REPAIR_ITEM (:2937)
            if (AhBagNotEmpty(guid)) return "You cannot sell a non-empty bag.";                                    // ERR_AUCTION_BAG (:2919)
            return null;
        }

        /// <summary>The refusal text for the item at (bag, slot) (the Auctions tab's sell slot asks before taking it), null = may be sold.</summary>
        internal string AuctionSellRefusal(int bag, int slot)
        {
            ulong g;
            OwnedView ov;
            return AhSellRefusal(bag, slot, out g, out ov);
        }

        bool AhBagNotEmpty(ulong guid)
        {
            OnlineObject bag;
            if (!_objects.TryGetValue(guid, out bag) || bag.TypeId != TypeId.Container) return false;
            int n = Math.Min(36, Math.Max(0, bag.I32(UF.CONTAINER_FIELD_NUM_SLOTS)));
            for (int i = 0; i < n; i++) if (bag.U64(UF.CONTAINER_FIELD_SLOT_1 + i * 2) != 0) return true;
            return false;
        }

        DbcTable AhTable()
        {
            if (_ahDbc == null && !_ahDbcTried)
            {
                _ahDbcTried = true;
                try { _ahDbc = _files != null ? Dbc.Load(_files, "AuctionHouse") : null; }
                catch (Exception ex) { WowLog.Warn("Online auction: AuctionHouse.dbc not read (" + ex.Message + ") - deposit 5 %, cut 5 % (ours)."); }
            }
            return _ahDbc;
        }

        // ------------------------------------------------------------------ the Browse tab's class / subclass filters (UiAuction)

        // ItemClass.dbc (measured 2026-10-03 with the harness DbcDump: 20 fields, 0 id, 3 Name_lang enUS); ItemSubClass.dbc 0 class, 1 subclass,
        // 10 display name (the field ProfessionDb.cs:136-142 reads). The order is ours: the 3.3.5a client's AuctionFrame list (GetAuctionItemClasses
        // is C code) - Weapon, Armor, Container, Consumable, Glyph, Trade Goods, Projectile, Quiver, Recipe, Gem, Miscellaneous, Quest.
        static readonly int[] AhClassOrder = { 2, 4, 1, 0, 16, 7, 6, 11, 9, 3, 15, 12 };
        DbcTable _ahClassDbc, _ahSubDbc;
        bool _ahFilterDbcTried;

        void AhFilterTables()
        {
            if (_ahFilterDbcTried) return;
            _ahFilterDbcTried = true;
            try
            {
                _ahClassDbc = _files != null ? Dbc.Load(_files, "ItemClass") : null;
                _ahSubDbc = _files != null ? Dbc.Load(_files, "ItemSubClass") : null;
                if (_ahClassDbc != null && _ahClassDbc.FieldCount < 4) _ahClassDbc = null;
                if (_ahSubDbc != null && _ahSubDbc.FieldCount < 11) _ahSubDbc = null;
            }
            catch (Exception ex) { WowLog.Warn("Online auction: ItemClass / ItemSubClass.dbc not read (" + ex.Message + ") - the class filters are hidden."); }
        }

        /// <summary>The Browse tab's class filters (id, ItemClass.dbc name) in the client's order; empty when the table is not readable.</summary>
        internal List<KeyValuePair<int, string>> AhFilterClasses()
        {
            AhFilterTables();
            var list = new List<KeyValuePair<int, string>>();
            if (_ahClassDbc == null) return list;
            foreach (int id in AhClassOrder)
            {
                int r = _ahClassDbc.FindRow(id);
                string n = r >= 0 ? _ahClassDbc.GetLocalizedString(r, 3) : null;
                if (!string.IsNullOrEmpty(n)) list.Add(new KeyValuePair<int, string>(id, n));
            }
            return list;
        }

        /// <summary>The subclass filters of one class (subclass id, ItemSubClass.dbc display name) in the table's order.</summary>
        internal List<KeyValuePair<int, string>> AhFilterSubClasses(int itemClass)
        {
            AhFilterTables();
            var list = new List<KeyValuePair<int, string>>();
            if (_ahSubDbc == null) return list;
            for (int r = 0; r < _ahSubDbc.RecordCount; r++)
            {
                if (_ahSubDbc.GetInt(r, 0) != itemClass) continue;
                string n = _ahSubDbc.GetLocalizedString(r, 10);
                if (!string.IsNullOrEmpty(n)) list.Add(new KeyValuePair<int, string>(_ahSubDbc.GetInt(r, 1), n));
            }
            return list;
        }

        // ------------------------------------------------------------------ shared by the queues partials (Auction / Battleground / Lfg)

        /// <summary>Sends and logs once per opcode, then every 50th (the Group / Chat precedent).</summary>
        void QuSend(ushort op, PacketWriter w)
        {
            if (_world == null) return;
            if (w == null) _world.Send(op); else _world.Send(op, w);
            int n;
            _quSends.TryGetValue(op, out n);
            _quSends[op] = ++n;
            if (n == 1 || n % 50 == 0) WowLog.Info("Online queues: sent " + Op.Name(op) + " (" + n + (n == 1 ? " time, logged once, then every 50th)" : " times)"));
        }

        readonly HashSet<ushort> _quOdd = new HashSet<ushort>();

        /// <summary>Registers a handler of the queues partials: a packet shorter than its layout is logged once and ignored (the interact
        /// partials' IxHandle precedent) instead of failing in WorldClient.</summary>
        void QuOn(ushort op, Action<WowPacket> handler)
        {
            _world.On(op, p =>
            {
                try { handler(p); }
                catch (PacketTooShortException)
                {
                    if (_quOdd.Add(op)) WowLog.Info("Online queues: odd " + Op.Name(op) + " - shorter than its layout (" + p.Data.Length + " bytes), ignored (logged once).");
                }
            }, this);
        }

        /// <summary>A player's name when known (ChatSender), else one CMSG_NAME_QUERY (NameQuery::Read: uint64) and null.</summary>
        string QuName(ulong guid)
        {
            if (guid == 0) return null;
            string n = ChatSender(guid);
            if (n != null) return n;
            if (_world != null && IsPlayerGuid(guid) && _nameAsked.Add(guid)) _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(guid));
            return null;
        }

        static int Clamp8(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        static uint AhAny(int v) { return v < 0 ? 0xFFFFFFFFu : (uint)v; }
    }
}
