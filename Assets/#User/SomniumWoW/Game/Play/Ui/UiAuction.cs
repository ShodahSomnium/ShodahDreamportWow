// iteration 11 - owner queues (proposal-interact.md 4.7 / 6.3.7): WoW's AuctionFrame (Blizzard_AuctionUI.xml / .lua of the install, dumped
// as AH.xml / AH.lua; templates AHT.xml), a session window of uicore's registry (Wanted while IWowInteractions.AuctionHouse != null).
// Browse (name, level range, rarity, usable, the class / subclass filters, sort headers, 8 rows of 50 a page, Bid / Buyout), Bids (our bids),
// Auctions (the sell slot - drag / click a held item or right-click it in the bags -, start / buyout price, 12 / 24 / 48 h, the deposit,
// Create Auction, our auctions with Cancel Auction). Desktop: the mouse; VR: the laser / poke (text and money boxes open the headset keyboard).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiAuction : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Auction, Frame = "AuctionFrame", TitleKey = "AUCTIONS", Title = "Auctions", Area = UiPanelArea.Doublewide, Pushable = 0,   // UIPanelWindows doublewide 0, width 840 (AH.lua:180)
                Width = 840f, Height = 447f, SessionWindow = true, Palm = false, OpenSound = "AuctionWindowOpen",                                            // AH.lua:326 (no close sound)
                Wanted = s => s != null && s.Interactions != null && s.Interactions.AuctionHouse != null,
                NearGuid = s => s != null && s.Interactions != null && s.Interactions.AuctionHouse != null ? s.Interactions.AuctionHouse.AuctioneerGuid : 0,
                Closed = s => { if (s != null && s.Interactions != null) s.Interactions.CloseAuctionHouse(); },
                Owner = "queues", Hidden = false,
                Build = (parent, ctx, close) => new UiAuction(parent, ctx, close)
            };
        }

        private static UiAuction _active;

        /// <summary>interact UiInteract.TryUseBagItem: a bag item right-clicked while the Auctions tab is shown goes into the sell slot
        /// (ClickAuctionSellItemButton). True = taken.</summary>
        public static bool PutInSellSlot(ItemView item)
        {
            UiAuction a = _active;
            if (a == null || !a.IsOpen || a._tab != 2 || item == null || item.Bag < 0 || item.Slot < 0) return false;
            a.SetSell(item.Bag, item.Slot);
            return true;
        }

        private const string Art = @"Interface\AuctionFrame\UI-AuctionFrame-";
        private const float FW = 832f, FH = 447f, RowH = 37f;
        private static readonly string[] TabArt = { "Browse", "Bid", "Auction" };
        private static readonly string[] Bucket = { "", "Short", "Medium", "Long", "Very Long" };   // AUCTION_TIME_LEFT1..4 (GlobalStrings.lua:627-633)
        private static readonly string[] Rarity = { "All", "Poor", "Common", "Uncommon", "Rare", "Epic", "Legendary", "Artifact" };   // ALL :526, ITEM_QUALITY0..6_DESC :4490-4496

        private sealed class ARow
        {
            public UiButton B, Item;
            public RawImage Sel;
            public TextMeshProUGUI Name, Level, Time, Who, BuyLabel;
            public RectTransform BidHolder, BuyHolder;
            public UiMoney Bid, Buy;
            public AuctionView Shown;
            public string IconPath;
            public bool IconLoaded;
        }

        private readonly Action _close;
        private readonly RawImage[] _art = new RawImage[6];
        private readonly UiQk.Tab[] _tabs = new UiQk.Tab[3];
        private readonly RectTransform[] _panes = new RectTransform[3];
        private readonly TextMeshProUGUI _title, _notice;
        private readonly List<UiQk.Box> _boxes = new List<UiQk.Box>();
        private WowKeyboard _keyboard;
        private int _tab = -1, _version = -1;

        // browse
        private readonly UiQk.Box _name, _minLevel, _maxLevel;
        private readonly UiButton _rarity, _usable, _search, _prev, _next, _bBid, _bBuyout;
        private readonly UiQk.Row[] _filters = new UiQk.Row[15];
        private readonly ARow[] _bRows = new ARow[8];
        private readonly TextMeshProUGUI _count, _searching;
        private readonly UiQk.MoneyBox _bPrice;
        private readonly List<KeyValuePair<int, int>> _filterList = new List<KeyValuePair<int, int>>();   // (class, subclass -1 = the class row)
        private readonly List<string> _filterNames = new List<string>();
        private int _quality = -1, _class = -1, _subClass = -1, _filterTop, _bTop, _bSel, _sort = -1;
        private bool _usableOn, _desc, _filtersBuilt;

        // bids
        private readonly ARow[] _dRows = new ARow[9];
        private readonly UiQk.MoneyBox _dPrice;
        private readonly UiButton _dBid, _dBuyout;
        private int _dTop, _dSel;

        // auctions
        private readonly UiButton _sellSlot, _create, _cancel;
        private readonly UiButton[] _dur = new UiButton[3];
        private readonly TextMeshProUGUI _sellName, _buyoutError, _ownTitle;
        private readonly RectTransform _depositHolder;
        private readonly UiMoney _deposit, _myMoney;
        private readonly UiQk.MoneyBox _start, _buyout;
        private readonly ARow[] _oRows = new ARow[9];
        private int _sellBag = -1, _sellSlotIndex = -1, _hours = 12, _oTop, _oSel;
        private ItemView _sellShown;
        private string _sellIcon;
        private bool _sellLoaded;

        public UiAuction(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            W = 840f;
            H = FH;
            Root = UiKit.Rect(parent, "AuctionFrame", 0f, 0f, W, H);
            // the art: TopLeft 256, Top 320, TopRight 256 / BotLeft, Bot, BotRight (AH.xml:16-58); the Bid / Auctions tabs swap in their own sets
            float[] ax = { 0f, 256f, 576f, 0f, 256f, 576f };
            float[] aw = { 256f, 320f, 256f, 256f, 320f, 256f };
            for (int i = 0; i < 6; i++) _art[i] = UiKit.Pic(Root, "Art " + i, null, ax[i], i < 3 ? 0f : 256f, aw[i], 256f);
            _title = UiKit.Text(Root, "Title", "", 13f, UiArt.Gold, TextAlignmentOptions.Center, 179f, 12f, 400f, 17f);   // BrowseTitle TOP (0, -18) of the 758 browse frame
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.CloseButton(Root, FW + 3f - 32f, 8f, () => { if (_close != null) _close(); });   // AuctionFrameCloseButton TOPRIGHT (3, -8)
            _myMoney = new UiMoney(Root, 181f, FH - 20f - 15f, Color.white);                          // AuctionFrameMoneyFrame BOTTOMRIGHT at BOTTOMLEFT (181, 20)
            _notice = UiQk.Notice(Root, 200f, 180f, 430f, 80f);
            for (int i = 0; i < 3; i++) _panes[i] = UiKit.Rect(Root, TabArt[i], 0f, 0f, FW, FH);

            // ================================================================ Browse (AH.xml:105-905)
            Transform b = _panes[0];
            UiKit.Text(b, "Name label", "Name", 10f, Color.white, TextAlignmentOptions.TopLeft, 80f, 41f, 140f, 12f);                 // BrowseNameText (80, -41), NAME :5060
            _name = Box(b, "Name", 83f, 55f, 140f, 16f, 63, false);                                                                   // BrowseName 140 x 16
            _name.F.onSubmit.AddListener(_ => Search(0));
            UiKit.Text(b, "Level label", "Level Range", 10f, Color.white, TextAlignmentOptions.BottomLeft, 230f, 33f, 120f, 14f);    // BrowseLevelText BOTTOMLEFT at (230, -47), LEVEL_RANGE :4711
            _minLevel = Box(b, "Min level", 233f, 53f, 25f, 16f, 2, true);
            UiKit.Text(b, "Hyphen", "-", 10f, Color.white, TextAlignmentOptions.Center, 258f, 53f, 12f, 16f);
            _maxLevel = Box(b, "Max level", 270f, 53f, 25f, 16f, 2, true);
            UiKit.Text(b, "Rarity label", "Rarity", 10f, Color.white, TextAlignmentOptions.BottomLeft, 318f, 33f, 100f, 14f);       // BrowseDropDownName RARITY :6019
            _rarity = UiKit.PanelButton(b, "All", 310f, 50f, 110f, 22f, () => CycleRarity(1));                                     // ours: a cycling button for UIDropDownMenu
            _rarity.RightClick = () => CycleRarity(-1);
            _usable = UiQk.Check(b, "Usable Items", 432f, 50f, 90f, () => { _usableOn = !_usableOn; UiQk.SetCheck(_usable, _usableOn); });   // IsUsableCheckButton, USABLE_ITEMS :7914
            _search = UiKit.PanelButton(b, "Search", 758f + 25f - 80f, 39f, 80f, 22f, () => Search(0));                              // BrowseSearchButton TOPRIGHT (25, -39), SEARCH :6194
            UiKit.PanelButton(b, "Reset", 78f, 75f, 80f, 22f, ResetFilters);                                                        // BrowseResetButton (78, -75), RESET :6086
            _searching = UiKit.Text(b, "Searching", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 310f, 220f, 400f, 20f);      // BrowseNoResultsText / dots (ours text "Searching...")
            // the filters: AuctionFilterButton1..15, 136 x 20 at (23, -105) (AuctionClassButtonTemplate, AHT.xml:3-47)
            for (int i = 0; i < _filters.Length; i++)
            {
                int k = i;
                _filters[i] = UiQk.MakeRow(b, "AuctionFilterButton" + (i + 1), 23f, 105f + i * 20f, 136f, 20f, new[] { 4f }, new[] { 128f }, 10f);
                UiKit.Pic(_filters[i].B.transform, "Bg", UiArt.Tex(@"Interface\AuctionFrame\UI-AuctionFrame-FilterBg.blp"), 0f, 0.53125f, 0f, 0.625f, 0f, 0f, 136f, 20f).transform.SetAsFirstSibling();
                _filters[i].B.Click = () => PickFilter(k);
            }
            UiQk.ScrollPair(b, 162f, 105f, 105f + 15f * 20f - 16f, () => { _filterTop = Math.Max(0, _filterTop - 1); _version = -1; }, () => { _filterTop++; _version = -1; });
            // sort headers (AH.xml:402-486): RARITY 214, LEVEL_ABBR 57, CLOSES_IN 91, AUCTION_CREATOR 76, CURRENT_BID 207 from (186, -82), each -2 into the last
            string[] heads = { "Rarity", "Lvl", "Time Left", "Seller", "Current Bid" };   // :6019 :4708 :1646 :602 :1897
            float[] hw = { 214f, 57f, 91f, 76f, 207f };
            int[] col = { 1, 0, 3, 7, 8 };   // AuctionSortOrder (AuctionHouseSearcher.h:34-49): RARITY 1, MINLEVEL 0, TIMELEFT 3, OWNER 7, BID 8
            float hx = 186f;
            for (int i = 0; i < 5; i++)
            {
                int c = col[i];
                UiButton h = UiKit.Button(b, "Sort " + heads[i], hx, 82f, hw[i], 19f);
                Texture2D tabs = UiArt.Tex(@"Interface\FriendsFrame\WhoFrame-ColumnTabs.blp");   // AuctionSortButtonTemplate (AHT.xml:48-)
                UiKit.Pic(h.transform, "L", tabs, 0f, 0.078125f, 0f, 0.75f, 0f, 0f, 5f, 19f);
                UiKit.Pic(h.transform, "M", tabs, 0.078125f, 0.90625f, 0f, 0.75f, 5f, 0f, hw[i] - 9f, 19f);
                UiKit.Pic(h.transform, "R", tabs, 0.90625f, 0.96875f, 0f, 0.75f, hw[i] - 4f, 0f, 4f, 19f);
                h.Label = UiKit.Text(h.transform, "Text", heads[i], 10f, Color.white, TextAlignmentOptions.MidlineLeft, 8f, 0f, hw[i] - 12f, 19f);
                h.Hilite = UiKit.Pic(h.transform, "Highlight", UiArt.Add(@"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp"), 0f, 0f, hw[i], 19f);
                h.Hilite.enabled = false;
                h.Click = () => SortBy(c);
                hx += hw[i] - 2f;
            }
            for (int i = 0; i < _bRows.Length; i++)
            {
                int k = i;
                _bRows[i] = MakeRow(b, "BrowseButton" + (i + 1), 195f, 110f + i * RowH, 597f, true, () => { _bSel = Id(Cur(0), _bTop + k); FillPrice(_bPrice, Cur(0), _bSel); _version = -1; });
            }
            UiQk.ScrollPair(b, 795f, 110f, 110f + 8f * RowH - 16f, () => { _bTop = Math.Max(0, _bTop - 1); _version = -1; }, () => { _bTop++; _version = -1; });
            _count = UiKit.Text(b, "Count", "", 10f, Color.white, TextAlignmentOptions.Center, 300f, 408f, 300f, 12f);           // BrowseSearchCountText
            _prev = UiKit.ArtButton(b, "Prev", @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Up.blp", @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Down.blp",
                @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Disabled.blp", @"Interface\Buttons\UI-Common-MouseHilight.blp", 658f, 53f, 32f, () => Page(-1));   // BrowsePrevPageButton (658, -53)
            _next = UiKit.ArtButton(b, "Next", @"Interface\Buttons\UI-SpellbookIcon-NextPage-Up.blp", @"Interface\Buttons\UI-SpellbookIcon-NextPage-Down.blp",
                @"Interface\Buttons\UI-SpellbookIcon-NextPage-Disabled.blp", @"Interface\Buttons\UI-Common-MouseHilight.blp", 758f + 70f - 32f, 53f, 32f, () => Page(1));   // TOPRIGHT (70, -53)
            UiKit.Text(b, "Prev text", "Prev", 10f, Color.white, TextAlignmentOptions.MidlineLeft, 690f, 53f, 40f, 32f);          // PREV :5717
            UiKit.Text(b, "Next text", "Next", 10f, Color.white, TextAlignmentOptions.MidlineRight, 748f - 40f, 53f, 40f, 32f);    // NEXT :5178
            UiKit.Text(b, "Bid label", "Bid", 10f, Color.white, TextAlignmentOptions.MidlineRight, 290f, 413f, 40f, 16f);          // BrowseBidText BID :735
            _bPrice = UiQk.Money(b, "Bid price", 340f, 413f, OpenKeyboard, null);                                                   // BrowseBidPrice BOTTOM (25, 18)
            AddMoneyBoxes(_bPrice);
            _bBid = UiKit.PanelButton(b, "Bid", 584f, 411f, 80f, 22f, () => Bid(0, false));                                       // BrowseBidButton
            _bBuyout = UiKit.PanelButton(b, "Buyout", 664f, 411f, 80f, 22f, () => Bid(0, true));                                  // BrowseBuyoutButton
            UiKit.PanelButton(b, "Close", 744f, 411f, 80f, 22f, () => { if (_close != null) _close(); });                       // BrowseCloseButton BOTTOMRIGHT (66, 14)

            // ================================================================ Bids (AH.xml:910-1250)
            Transform d = _panes[1];
            string[] dHeads = { "Rarity", "Lvl", "Time Left", "Status", "Current Bid" };   // BidQualitySort (65, -52) 195, LEVEL 65, CLOSES_IN 79 ... (ours: the last two)
            float[] dw = { 195f, 65f, 79f, 120f, 240f };
            float dx = 65f;
            for (int i = 0; i < 5; i++) { UiKit.Text(d, "Head " + i, dHeads[i], 10f, Color.white, TextAlignmentOptions.MidlineLeft, dx + 8f, 52f, dw[i] - 12f, 19f); dx += dw[i] - 2f; }
            for (int i = 0; i < _dRows.Length; i++)
            {
                int k = i;
                _dRows[i] = MakeRow(d, "BidButton" + (i + 1), 27f, 76f + i * RowH, 765f, true, () => { _dSel = Id(Cur(1), _dTop + k); FillPrice(_dPrice, Cur(1), _dSel); _version = -1; });   // BidButton1 (27, -76)
            }
            UiQk.ScrollPair(d, 795f, 76f, 76f + 9f * RowH - 16f, () => { _dTop = Math.Max(0, _dTop - 1); _version = -1; }, () => { _dTop++; _version = -1; });
            UiKit.Text(d, "Bid label", "Bid", 10f, Color.white, TextAlignmentOptions.MidlineRight, 250f, 413f, 40f, 16f);
            _dPrice = UiQk.Money(d, "Bid price", 300f, 413f, OpenKeyboard, null);                                                // BidBidPrice BOTTOM (-15, 18)
            AddMoneyBoxes(_dPrice);
            _dBid = UiKit.PanelButton(d, "Bid", 584f, 411f, 80f, 22f, () => Bid(1, false));
            _dBuyout = UiKit.PanelButton(d, "Buyout", 664f, 411f, 80f, 22f, () => Bid(1, true));
            UiKit.PanelButton(d, "Close", 744f, 411f, 80f, 22f, () => { if (_close != null) _close(); });

            // ================================================================ Auctions (AH.xml:1250-1800)
            Transform a = _panes[2];
            _ownTitle = UiKit.Text(a, "Title", "", 13f, UiArt.Gold, TextAlignmentOptions.Center, 179f, 12f, 400f, 17f);         // AuctionsTitle AUCTION_TITLE "%s's Auctions" :635
            UiKit.Text(a, "Create label", "Create Auction", 11f, Color.white, TextAlignmentOptions.Center, 18f, 50f, 190f, 14f);  // AuctionsTabText CREATE_AUCTION :1875
            UiKit.Text(a, "Item label", "Auction Item", 10f, UiArt.Gold, TextAlignmentOptions.TopLeft, 28f, 76f, 170f, 12f);      // AuctionsItemText AUCTION_ITEM :614
            _sellSlot = UiKit.Slot(a, "AuctionsItemButton", 28f, 92f, 37f, false);                                                  // 37 x 37 (AH.xml:1406)
            _sellSlot.Icon.enabled = false;
            _sellSlot.Count = UiKit.Text(_sellSlot.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 35f, 35f, UiArt.Numbers);
            _sellSlot.Click = SellSlotClick;
            _sellSlot.RightClick = () => ClearSell("right-click");
            _sellSlot.Tip = () => _sellShown != null ? UiTooltip.Item(_sellShown, null) : "Drag an item here to auction it";       // AUCTION_ITEM_TEXT :617
            UiKit.DropTarget(_sellSlot, p => p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0 && p.Slot >= 0, p => { SetSell(p.Bag, p.Slot); UiPickup.Clear("placed in the auction slot"); });
            _sellName = UiKit.Text(a, "Item name", "", 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 70f, 92f, 135f, 37f);
            UiKit.Text(a, "Start label", "Starting Price", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 28f, 176f, 170f, 14f);   // STARTING_PRICE :7343
            _start = UiQk.Money(a, "Start price", 35f, 192f, OpenKeyboard, () => _version = -1);
            AddMoneyBoxes(_start);
            UiKit.Text(a, "Buyout label", "Buyout Price", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 28f, 216f, 90f, 14f);    // BUYOUT_PRICE :1176
            _buyoutError = UiKit.Text(a, "Buyout error", "", 9f, UiArt.ErrorRed, TextAlignmentOptions.BottomLeft, 100f, 216f, 110f, 14f);   // AUCTION_BUYOUT_ERROR :600
            _buyout = UiQk.Money(a, "Buyout price", 35f, 232f, OpenKeyboard, () => _version = -1);
            AddMoneyBoxes(_buyout);
            UiKit.Text(a, "Duration label", "Duration", 10f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 28f, 256f, 170f, 14f);       // AUCTION_DURATION :603
            string[] durs = { "12 Hours", "24 Hours", "48 Hours" };   // AUCTION_DURATION_ONE / TWO / THREE :605-607
            int[] hrs = { 12, 24, 48 };
            for (int i = 0; i < 3; i++)
            {
                int h = hrs[i];
                _dur[i] = UiQk.Check(a, durs[i], 28f, 274f + i * 20f, 120f, () => { _hours = h; _version = -1; });
            }
            UiKit.Text(a, "Deposit label", "Deposit:", 11f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 28f, 344f, 70f, 16f);     // AuctionsDepositText DEPOSIT_COLON :1976
            _depositHolder = UiKit.Rect(a, "Deposit", 0f, 344f, 210f, 16f);
            _deposit = new UiMoney(_depositHolder, 205f, 0f, Color.white);
            _create = UiKit.PanelButton(a, "Create Auction", 18f, FH - 39f - 20f, 191f, 20f, Create);                                // AuctionsCreateAuctionButton BOTTOMLEFT (18, 39) 191 x 20
            string[] oHeads = { "Rarity", "Time Left", "High Bidder", "Current Bid" };   // :6019 :1646 HIGH_BIDDER :4174 :1897
            float[] ox = { 223f, 430f, 505f, 640f };
            for (int i = 0; i < 4; i++) UiKit.Text(a, "Head " + i, oHeads[i], 10f, Color.white, TextAlignmentOptions.MidlineLeft, ox[i] + 8f, 80f, 120f, 19f);
            for (int i = 0; i < _oRows.Length; i++)
            {
                int k = i;
                _oRows[i] = MakeRow(a, "AuctionsButton" + (i + 1), 223f, 102f + i * 34f, 570f, false, () => { _oSel = Id(Cur(2), _oTop + k); _version = -1; });
            }
            UiQk.ScrollPair(a, 795f, 102f, 102f + 9f * 34f - 16f, () => { _oTop = Math.Max(0, _oTop - 1); _version = -1; }, () => { _oTop++; _version = -1; });
            _cancel = UiKit.PanelButton(a, "Cancel Auction", 744f + 2f - 126f, 411f, 126f, 22f, CancelSelected);                    // AuctionsCancelAuctionButton CANCEL_AUCTION :1339
            UiKit.PanelButton(a, "Close", 744f, 411f, 80f, 22f, () => { if (_close != null) _close(); });                       // AuctionsCloseButton

            // tabs BROWSE / BIDS / AUCTIONS (AH.xml:59-85): Tab1 TOPLEFT at BOTTOMLEFT (15, 11), the next ones -8 into the last
            string[] tabNames = { "Browse", "Bids", "Auctions" };   // BROWSE :1120, BIDS :736, AUCTIONS :599
            float tx = 15f;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _tabs[i] = UiQk.BottomTab(Root, tabNames[i], tx, FH - 11f, () => SetTab(k));
                tx += _tabs[i].W - 8f;
            }
            Root.gameObject.SetActive(false);
            UiQueues.Ensure(parent, ctx);
        }

        // ------------------------------------------------------------------ building helpers

        private UiQk.Box Box(Transform parent, string title, float x, float y, float w, float h, int limit, bool numeric)
        {
            UiQk.Box bx = UiQk.Field(parent, title, x, y, w, h, limit, numeric);
            bx.Vr.Click = () => OpenKeyboard(bx);
            _boxes.Add(bx);
            return bx;
        }

        private void AddMoneyBoxes(UiQk.MoneyBox m)
        {
            for (int k = 0; k < 3; k++) _boxes.Add(m.B[k]);
        }

        private void OpenKeyboard(UiQk.Box bx)
        {
            if (bx == null || bx.F == null || Ctx == null || !Ctx.Vr) return;
            if (_keyboard == null) _keyboard = WowKeyboard.Create(Root.root);
            _keyboard.Open(bx.F, bx.Title, false, () => _version = -1);   // ours: the VR chat's path (UiChat.OpenKeyboard)
        }

        /// <summary>A row: BrowseButtonTemplate 597 x 37 (AHT.xml:122-): the item button, UI-AuctionItemNameFrame from x 34, Name at 43 (167 wide),
        /// Level at 205 (52), the time left (ours at 257, 88), HighBidder / Seller at 345 (78), the money at the right (bid; buyout under it).</summary>
        private ARow MakeRow(Transform parent, string name, float x, float y, float w, bool level, Action click)
        {
            var r = new ARow();
            float h = level ? RowH : 34f;
            r.B = UiKit.Button(parent, name, x, y, w, h);
            Texture2D frame = UiArt.Tex(@"Interface\AuctionFrame\UI-AuctionItemNameFrame.blp");
            UiKit.Pic(r.B.transform, "Left", frame, 0f, 0.078125f, 0f, 1f, 34f, 1f, 10f, 32f);
            UiKit.Pic(r.B.transform, "Middle", frame, 0.078125f, 0.75f, 0f, 1f, 44f, 1f, w - 54f, 32f);
            UiKit.Pic(r.B.transform, "Right", frame, 0.75f, 0.828125f, 0f, 1f, w - 10f, 1f, 10f, 32f);
            r.Sel = UiKit.Pic(r.B.transform, "Selected", UiArt.Add(@"Interface\HelpFrame\HelpFrameButton-Highlight.blp"), 0f, 0.625f, 0f, 1f, 33f, 1f, w - 33f, 32f);
            r.Sel.enabled = false;
            r.B.Hilite = UiKit.Pic(r.B.transform, "Highlight", UiArt.Add(@"Interface\HelpFrame\HelpFrameButton-Highlight.blp"), 0f, 0.625f, 0f, 1f, 33f, 1f, w - 33f, 32f);
            r.B.Hilite.enabled = false;
            r.B.Click = click;
            r.Item = UiKit.Slot(r.B.transform, "Item", 0f, 1f, 32f, false);
            r.Item.Icon.enabled = false;
            r.Item.Count = UiKit.Text(r.Item.transform, "Count", "", 10f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 31f, 31f, UiArt.Numbers);
            r.Item.Click = click;
            r.Item.Tip = () => r.Shown != null && r.Shown.Item != null ? UiTooltip.Item(r.Shown.Item, null) : null;
            r.Name = Col(r.B.transform, "Name", 43f, 167f, 11f, TextAlignmentOptions.MidlineLeft, h);
            r.Level = Col(r.B.transform, "Level", 205f, 52f, 10f, TextAlignmentOptions.Center, h);
            r.Time = Col(r.B.transform, "Time", level ? 257f : 207f, 88f, 10f, TextAlignmentOptions.Center, h);
            r.Who = Col(r.B.transform, "Who", level ? 345f : 285f, level ? 78f : 125f, 10f, TextAlignmentOptions.Center, h);
            r.BidHolder = UiKit.Rect(r.B.transform, "Bid", 0f, 3f, w, 15f);
            r.Bid = new UiMoney(r.BidHolder, w - 8f, 0f, Color.white);
            r.BuyHolder = UiKit.Rect(r.B.transform, "Buyout", 0f, 18f, w, 15f);
            r.Buy = new UiMoney(r.BuyHolder, w - 8f, 0f, Color.white);
            r.BuyLabel = UiKit.Text(r.BuyHolder, "Label", "Buyout", 9f, UiArt.Grey, TextAlignmentOptions.MidlineRight, w - 8f - 180f, 0f, 40f, 15f);   // BUYOUT :1173
            r.B.gameObject.SetActive(false);
            return r;
        }

        private static TextMeshProUGUI Col(Transform parent, string name, float x, float w, float size, TextAlignmentOptions al, float h)
        {
            TextMeshProUGUI t = UiKit.Text(parent, name, "", size, Color.white, al, x, 0f, w, h);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        // ------------------------------------------------------------------ state

        private IWowInteractions Ix { get { return UiQk.Ix(Ctx); } }
        private AuctionHouseView Ah { get { IWowInteractions ix = Ix; return ix != null ? ix.AuctionHouse : null; } }

        private List<AuctionView> Cur(int tab)
        {
            AuctionHouseView ah = Ah;
            if (ah == null) return null;
            return tab == 0 ? ah.Results : tab == 1 ? ah.Bids : ah.Owned;
        }

        private static int Id(List<AuctionView> list, int index)
        {
            return list != null && index >= 0 && index < list.Count ? list[index].Id : 0;
        }

        private static AuctionView Find(List<AuctionView> list, int id)
        {
            if (list == null || id == 0) return null;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
            return null;
        }

        private void SetTab(int t)
        {
            if (t == _tab) return;
            _tab = t;
            for (int i = 0; i < 3; i++) { _tabs[i].Select(i == t); UiQk.Show(_panes[i], i == t); }
            string[] files = { "TopLeft", "Top", "TopRight", "BotLeft", "Bot", "BotRight" };
            for (int i = 0; i < 6; i++) _art[i].texture = UiArt.Tex(Art + TabArt[t] + "-" + files[i] + ".blp");
            UiSound.Named("igCharacterInfoTab");
            IWowInteractions ix = Ix;
            if (ix != null && ix.AuctionHouse != null)
            {
                if (t == 1) ix.ListBids();          // AuctionFrameBid_OnShow -> GetBidderAuctionItems (AH.lua:281)
                else if (t == 2) ix.ListOwnAuctions();   // AuctionFrameAuctions_OnShow -> GetOwnerAuctionItems (AH.lua:1164-1170)
            }
            _version = -1;
            WowLog.Info("UI: AuctionFrame tab " + TabArt[t] + ".");
        }

        private void CycleRarity(int d)
        {
            _quality = ((_quality + 1 + d) % 8 + 8) % 8 - 1;   // -1 = All, 0..6
            _rarity.SetLabel(Rarity[_quality + 1]);
        }

        private void ResetFilters()
        {
            _name.F.SetTextWithoutNotify("");
            _minLevel.F.SetTextWithoutNotify("");
            _maxLevel.F.SetTextWithoutNotify("");
            _quality = -1;
            _rarity.SetLabel("All");
            _usableOn = false;
            UiQk.SetCheck(_usable, false);
            _class = _subClass = -1;
            _filtersBuilt = false;
            _version = -1;
        }

        private void PickFilter(int row)
        {
            int k = _filterTop + row;
            if (k < 0 || k >= _filterList.Count) return;
            KeyValuePair<int, int> f = _filterList[k];
            if (f.Value < 0) { _class = _class == f.Key ? -1 : f.Key; _subClass = -1; }
            else _subClass = _subClass == f.Value ? -1 : f.Value;
            _filtersBuilt = false;
            _version = -1;
        }

        private void BuildFilters()
        {
            if (_filtersBuilt) return;
            _filtersBuilt = true;
            _filterList.Clear();
            _filterNames.Clear();
            OnlineSession os = UiQk.Online(Ctx);
            if (os == null) return;
            foreach (KeyValuePair<int, string> c in os.AhFilterClasses())
            {
                _filterList.Add(new KeyValuePair<int, int>(c.Key, -1));
                _filterNames.Add(c.Value);
                if (c.Key != _class) continue;
                foreach (KeyValuePair<int, string> s in os.AhFilterSubClasses(c.Key))
                {
                    _filterList.Add(new KeyValuePair<int, int>(c.Key, s.Key));
                    _filterNames.Add("  " + s.Value);
                }
            }
        }

        private void SortBy(int column)
        {
            if (_sort == column) _desc = !_desc;
            else { _sort = column; _desc = false; }
            AuctionHouseView ah = Ah;
            if (ah != null && ah.Results.Count > 0) Search(ah.Page);
        }

        private void Search(int page)
        {
            IWowInteractions ix = Ix;
            if (ix == null || ix.AuctionHouse == null) return;
            var q = new AuctionQuery
            {
                Name = _name.F.text ?? "", LevelMin = UiQk.Int(_minLevel), LevelMax = UiQk.Int(_maxLevel), ItemClass = _class, ItemSubClass = _class >= 0 ? _subClass : -1,
                Quality = _quality, Usable = _usableOn, Page = Math.Max(0, page), SortColumn = _sort, SortDescending = _desc
            };
            _bSel = 0;
            _bTop = 0;
            WowLog.Info("UI: AuctionFrame - Search (page " + q.Page + (q.ItemClass >= 0 ? ", class " + q.ItemClass : "") + (q.Quality >= 0 ? ", quality " + q.Quality : "") + ").");
            ix.SearchAuctions(q);
        }

        private void Page(int d)
        {
            AuctionHouseView ah = Ah;
            if (ah == null) return;
            int p = ah.Page + d;
            if (p < 0 || p * 50 >= Math.Max(1, ah.TotalResults)) return;
            Search(p);
        }

        private static void FillPrice(UiQk.MoneyBox box, List<AuctionView> list, int id)
        {
            AuctionView a = Find(list, id);
            if (a != null) box.Set(a.NextBid);   // the row's required bid goes into the bid box (AuctionFrameBrowse_Update)
        }

        /// <summary>BID_AUCTION / BUYOUT_AUCTION (AH.lua:192-222: BID_AUCTION_CONFIRMATION :737 / BUYOUT_AUCTION_CONFIRMATION :1174 + the money;
        /// ACCEPT / CANCEL, showAlert, timeout 0, exclusive, hideOnEscape) -> PlaceBid.</summary>
        private void Bid(int tab, bool buyout)
        {
            IWowInteractions ix = Ix;
            List<AuctionView> list = Cur(tab);
            int id = tab == 0 ? _bSel : _dSel;
            AuctionView a = Find(list, id);
            if (ix == null || a == null) return;
            int price = buyout ? a.Buyout : (tab == 0 ? _bPrice : _dPrice).Copper;
            if (price <= 0) return;
            WowLog.Info("UI: AuctionFrame - " + (buyout ? "Buyout" : "Bid") + " on auction " + id + " (" + price + " copper) asks first.");
            UiPopup.Show(new UiPopupSpec
            {
                Which = buyout ? "BUYOUT_AUCTION" : "BID_AUCTION", Text = buyout ? "Buyout auction for:" : "Bid on auction for:", Money = price,
                Button1 = "Accept", Button2 = "Cancel", OnAccept = () => ix.PlaceBid(id, price),   // ACCEPT :11, CANCEL :1338
                ShowAlert = true, Timeout = 0f, Exclusive = true, HideOnEscape = true
            });
        }

        // ------------------------------------------------------------------ the sell slot

        private void SellSlotClick()
        {
            UiPickupPayload held = UiPickup.Current;
            if (held != null && held.Kind == UiPickupKind.Item && held.Bag >= 0 && held.Slot >= 0)
            {
                SetSell(held.Bag, held.Slot);
                UiPickup.Clear("placed in the auction slot");
                return;
            }
            if (_sellShown != null) ClearSell("click");
            else if (Ctx != null) Ctx.Say("Drag an item here to auction it", UiArt.InfoYellow);   // AUCTION_ITEM_TEXT :617
        }

        private ItemView BagItem(int bag, int slot)
        {
            PlayerState p = Ctx != null ? Ctx.P : null;
            if (p == null) return null;
            for (int i = 0; i < p.Bags.Count; i++) if (p.Bags[i] != null && p.Bags[i].Bag == bag && p.Bags[i].Slot == slot) return p.Bags[i];
            return null;
        }

        private void SetSell(int bag, int slot)
        {
            OnlineSession os = UiQk.Online(Ctx);
            string why = os != null ? os.AuctionSellRefusal(bag, slot) : "The Auction House is on the online realm.";   // ours offline
            if (why != null) { if (Ctx != null) Ctx.Say(why, UiArt.ErrorRed); return; }
            if (_tab != 2) SetTab(2);
            _sellBag = bag;
            _sellSlotIndex = slot;
            ItemView it = BagItem(bag, slot);
            // the start price (AuctionSellItemButton_OnEvent, AH.lua:1501-1503): max(100, floor(price * 1.5)), buyout 0
            int vendor = it != null ? it.SellPrice * Math.Max(1, it.Count) : 0;
            _start.Set(Math.Max(100, (int)Math.Floor(vendor * 1.5)));
            _buyout.Set(0);
            _version = -1;
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            WowLog.Info("UI: AuctionFrame - the item at bag " + bag + " slot " + slot + " is in the sell slot.");
        }

        private void ClearSell(string why)
        {
            if (_sellBag < 0) return;
            _sellBag = _sellSlotIndex = -1;
            _version = -1;
            WowLog.Info("UI: AuctionFrame - the sell slot cleared (" + why + ").");
        }

        private void Create()
        {
            IWowInteractions ix = Ix;
            if (ix == null || _sellBag < 0) return;
            int start = _start.Copper, buyout = _buyout.Copper;
            WowLog.Info("UI: AuctionFrame - Create Auction (" + _hours + " h).");
            ix.CreateAuction(_sellBag, _sellSlotIndex, start, buyout, _hours);
            ClearSell("created");   // ours: the slot empties at once (WoW: when the item leaves the bag)
        }

        /// <summary>CANCEL_AUCTION (AH.lua:224-245: CANCEL_AUCTION_CONFIRMATION :1340, or _MONEY :1341 + floor(bid * 0.05) (AH.lua:1298) when
        /// someone bid; ACCEPT / CANCEL, showAlert, timeout 0, exclusive, hideOnEscape).</summary>
        private void CancelSelected()
        {
            IWowInteractions ix = Ix;
            AuctionView a = Find(Cur(2), _oSel);
            if (ix == null || a == null) return;
            int cut = a.Bid > 0 ? (int)Math.Floor(a.Bid * 0.05) : 0;
            int id = a.Id;
            UiPopup.Show(new UiPopupSpec
            {
                Which = "CANCEL_AUCTION", Text = cut > 0 ? "Canceling this auction will cost you your deposit and:" : "You will lose your initial deposit by canceling this auction.",
                Money = cut > 0 ? cut : -1, Button1 = "Accept", Button2 = "Cancel", OnAccept = () => ix.CancelAuction(id),
                ShowAlert = true, Timeout = 0f, Exclusive = true, HideOnEscape = true
            });
        }

        // ------------------------------------------------------------------ open / refresh

        protected override void OnOpen()
        {
            _active = this;
            _version = -1;
            _tab = -1;
            SetTab(0);
            _filtersBuilt = false;
            AuctionHouseView ah = Ah;
            WowLog.Info("UI: AuctionFrame opened (" + (ah != null ? ah.HouseName ?? "house " + ah.HouseId : "?") + (Ctx != null && Ctx.Vr ? ", VR" : ", desktop") + ").");
        }

        protected override void OnClose()
        {
            if (_active == this) _active = null;
            if (_keyboard != null && _keyboard.IsOpen) _keyboard.Close();
            ClearSell("closed");
        }

        public override void Refresh(float now)
        {
            AuctionHouseView ah = Ah;
            PlayerState p = Ctx != null ? Ctx.P : null;
            if (ah == null || p == null) return;
            bool vr = Ctx.Vr;
            for (int i = 0; i < _boxes.Count; i++) UiQk.SyncVr(_boxes[i], vr);
            _myMoney.Set(p.Money);
            RefreshIcons(_tab == 0 ? _bRows : _tab == 1 ? _dRows : _oRows);
            if (_tab == 2) RefreshSellIcon();
            int wheel = _tab == 0 ? UiQk.Wheel(_panes[0]) : 0;
            if (wheel != 0) { _bTop = Math.Max(0, _bTop - wheel); _version = -1; }
            int ver = ah.Version * 7 + _tab;
            bool searchGrey = ah.Searching || ah.SearchDelay > 0f;
            _search.Interactable = !searchGrey;
            if (ver == _version && !_bPrice.Focused && !_dPrice.Focused) { ButtonsOnly(ah, p); return; }
            _version = ver;
            if (_tab == 0) RefreshBrowse(ah);
            else if (_tab == 1) RefreshList(_dRows, ah.Bids, ref _dTop, _dSel, 1);
            else RefreshAuctions(ah, p);
            ButtonsOnly(ah, p);
        }

        private void RefreshBrowse(AuctionHouseView ah)
        {
            _title.text = "Browse Auctions";   // BROWSE_AUCTIONS :1121
            BuildFilters();
            _filterTop = Mathf.Clamp(_filterTop, 0, Math.Max(0, _filterList.Count - _filters.Length));
            for (int i = 0; i < _filters.Length; i++)
            {
                int k = _filterTop + i;
                UiQk.Row r = _filters[i];
                UiQk.Show(r.B, k < _filterList.Count);
                if (k >= _filterList.Count) continue;
                KeyValuePair<int, int> f = _filterList[k];
                bool sel = f.Value < 0 ? f.Key == _class && _subClass < 0 : f.Key == _class && f.Value == _subClass;
                UiQk.Set(r.Col[0], _filterNames[k], f.Value < 0 ? UiArt.Gold : UiArt.White);
                r.Sel.enabled = sel;
            }
            RefreshList(_bRows, ah.Results, ref _bTop, _bSel, 0);
            UiQk.Set(_searching, ah.Searching ? "Searching..." : ah.Results.Count == 0 && ah.TotalResults == 0 && ah.LastResult == null ? "" : ah.Results.Count == 0 ? "No items found" : "", UiArt.Gold);   // ours / BROWSE_NO_RESULTS
            if (ah.TotalResults > 50)
            {
                int min = ah.Page * 50 + 1, max = Math.Min(ah.TotalResults, min + ah.Results.Count - 1);
                UiQk.Set(_count, string.Format("Items {0} - {1}  ( {2} total )", min, max, ah.TotalResults), Color.white);   // NUMBER_OF_RESULTS_TEMPLATE :5214 (AH.lua:919-926)
            }
            else UiQk.Set(_count, "", Color.white);
            UiQk.Show(_prev, ah.TotalResults > 50);
            UiQk.Show(_next, ah.TotalResults > 50);
            _prev.Interactable = ah.Page > 0;
            _next.Interactable = (ah.Page + 1) * 50 < ah.TotalResults;
        }

        private void RefreshList(ARow[] rows, List<AuctionView> list, ref int top, int sel, int tab)
        {
            top = Mathf.Clamp(top, 0, Math.Max(0, list.Count - rows.Length));
            for (int i = 0; i < rows.Length; i++)
            {
                int k = top + i;
                ARow r = rows[i];
                UiQk.Show(r.B, k < list.Count);
                if (k >= list.Count) { r.Shown = null; continue; }
                AuctionView a = list[k];
                if (!ReferenceEquals(a, r.Shown) || (a.Item != null && !ReferenceEquals(a.Item.IconPath, r.IconPath))) { r.Shown = a; r.IconLoaded = false; r.Item.Icon.enabled = false; }
                ItemView it = a.Item;
                UiQk.Set(r.Name, it != null && it.Name != null ? it.Name : "...", it != null ? UiArt.QualityColor(it.Quality) : UiArt.White);
                if (r.Item.Count != null) r.Item.Count.text = it != null && it.Count > 1 ? it.Count.ToString() : "";
                UiQk.Set(r.Level, it != null && it.RequiredLevel > 0 ? it.RequiredLevel.ToString() : "", Color.white);
                UiQk.Set(r.Time, a.TimeLeftBucket >= 1 && a.TimeLeftBucket <= 4 ? Bucket[a.TimeLeftBucket] : "", Color.white);
                string who = tab == 0 ? a.OwnerName ?? "" : tab == 1 ? (a.MineHighBid ? "High Bidder" : "Outbid")   // HIGH_BIDDER :4174 / OUTBID :5554
                                                                    : a.Bid > 0 ? (a.BidderGuid != 0 ? "High Bidder" : "") : "No Bids";   // NO_BIDS :5198 (ours: the bidder's name is not sent)
                UiQk.Set(r.Who, who, tab == 1 && !a.MineHighBid ? UiArt.ErrorRed : Color.white);
                r.Bid.Set(a.Bid > 0 ? a.Bid : a.StartBid);
                UiQk.Show(r.BuyHolder, a.Buyout > 0);
                if (a.Buyout > 0) r.Buy.Set(a.Buyout);
                r.Sel.enabled = a.Id == sel;
            }
        }

        private void RefreshIcons(ARow[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                ARow r = rows[i];
                ItemView it = r.Shown != null ? r.Shown.Item : null;
                if (it == null || r.IconLoaded || string.IsNullOrEmpty(it.IconPath)) continue;
                SetIcon(r.Item, it.IconPath, ref r.IconPath, ref r.IconLoaded);
            }
        }

        private void RefreshSellIcon()
        {
            ItemView it = _sellBag >= 0 ? BagItem(_sellBag, _sellSlotIndex) : null;
            if (_sellBag >= 0 && it == null) { ClearSell("the item left the bag"); }
            if (!ReferenceEquals(it, _sellShown))
            {
                _sellShown = it;
                _sellLoaded = false;
                _sellIcon = null;
                _sellSlot.Icon.enabled = false;
                _version = -1;
            }
            if (it != null && !_sellLoaded && !string.IsNullOrEmpty(it.IconPath)) SetIcon(_sellSlot, it.IconPath, ref _sellIcon, ref _sellLoaded);
        }

        private void RefreshAuctions(AuctionHouseView ah, PlayerState p)
        {
            _title.text = "";
            UiQk.Set(_ownTitle, (p.Name ?? "") + "'s Auctions", UiArt.Gold);   // AUCTION_TITLE :635
            ItemView it = _sellShown;
            UiQk.Set(_sellName, it != null ? it.Name ?? "..." : "", it != null ? UiArt.QualityColor(it.Quality) : UiArt.Gold);
            if (_sellSlot.Count != null) _sellSlot.Count.text = it != null && it.Count > 1 ? it.Count.ToString() : "";
            for (int i = 0; i < 3; i++) UiQk.SetCheck(_dur[i], _hours == (i == 0 ? 12 : i == 1 ? 24 : 48));
            RefreshList(_oRows, ah.Owned, ref _oTop, _oSel, 2);
        }

        /// <summary>The buttons' states (every frame: money and prices change without a view version).</summary>
        private void ButtonsOnly(AuctionHouseView ah, PlayerState p)
        {
            if (_tab == 0 || _tab == 1)
            {
                AuctionView a = Find(_tab == 0 ? ah.Results : ah.Bids, _tab == 0 ? _bSel : _dSel);
                UiQk.MoneyBox box = _tab == 0 ? _bPrice : _dPrice;
                bool mine = a != null && a.OwnerGuid == p.Guid;
                int price = box.Copper;
                // BrowseBidButton (AH.lua:908): not the high bidder, not ours, enough money, price <= MAXIMUM_BID_PRICE (and >= the required bid)
                bool canBid = a != null && !mine && !a.MineHighBid && price >= a.NextBid && price <= p.Money && (a.Buyout == 0 || price <= a.Buyout);
                bool canBuy = a != null && !mine && a.Buyout > 0 && a.Buyout <= p.Money;
                (_tab == 0 ? _bBid : _dBid).Interactable = canBid;
                (_tab == 0 ? _bBuyout : _dBuyout).Interactable = canBuy;
                return;
            }
            int start = _start.Copper, buyout = _buyout.Copper;
            IWowInteractions ix = Ix;
            int deposit = _sellBag >= 0 && ix != null ? ix.AuctionDeposit(_sellBag, _sellSlotIndex, _hours) : 0;
            UiQk.Show(_depositHolder, _sellBag >= 0 && deposit >= 0);
            if (deposit >= 0) _deposit.Set(deposit);
            bool badBuyout = buyout > 0 && buyout < start;
            UiQk.Set(_buyoutError, badBuyout ? "(Must be >= the start price)" : "", UiArt.ErrorRed);   // AUCTION_BUYOUT_ERROR :600
            // AuctionsFrameAuctions_ValidateAuction (AH.lua:1522-1547): an item, a start price, the buyout 0 or >= start, the deposit affordable
            _create.Interactable = _sellBag >= 0 && start > 0 && !badBuyout && deposit >= 0 && deposit <= p.Money;
            AuctionView own = Find(ah.Owned, _oSel);
            _cancel.Interactable = own != null;
        }
    }
}
