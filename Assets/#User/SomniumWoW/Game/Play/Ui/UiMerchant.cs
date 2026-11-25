// iteration 11 - owner npcui (proposal-npc.md 1 / 4 / 6.1 / 6.3 / H7; the wave-0 stub by w0game, filled by npcui)
// MerchantFrame (MerchantFrame.xml / .lua of the user's install, harness UiDump into P/harness/out_d11npc; file:line per value): the
// Merchant tab (10 items a page, paging, the buy-back-last button, an armorer's repair buttons, the money) and the Buyback tab (12).
// Desktop like WoW: left-click picks a merchant item up (click a bag slot = buy it into that slot), right-click buys one lot, Shift-click
// asks the quantity (StackSplitFrame); a bag item right-clicked or dropped on the window is sold. VR (ours, proposal-npc.md 1): tap = buy
// one lot, hold = the quantity, drag onto a bag slot = buy into it; a bag item tapped or dragged onto the window is sold.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiMerchant : UiWindow
    {
        public const int ItemsPerPage = 10;          // MERCHANT_ITEMS_PER_PAGE (MerchantFrame.lua:1)
        public const int BuybackPerPage = 12;        // BUYBACK_ITEMS_PER_PAGE (MerchantFrame.lua:2)
        public const int HighPriceCost = 1500000;    // MERCHANT_HIGH_PRICE_COST (MerchantFrame.lua:378)
        private const string Art = @"Interface\MerchantFrame\";
        private const float RowW = 153f, RowH = 44f; // MerchantItemTemplate (MerchantFrame.xml:4-7)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Merchant, Frame = "MerchantFrame", TitleKey = "MERCHANT", Title = "Merchant",   // GlobalStrings.lua:4921
                Area = UiPanelArea.Left, Pushable = 0, Width = 384f, Height = 512f, SessionWindow = true, Palm = false,   // UIPanelWindows (UIParent.lua:28)
                Wanted = s => s.CurrentMerchant != null,
                NearGuid = s => s.CurrentMerchant != null ? s.CurrentMerchant.VendorGuid : 0UL,
                Closed = s => s.CloseMerchant(),
                OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",   // MerchantFrame.lua:48 / :62
                Owner = "npcui", Hidden = false,
                Build = (parent, ctx, close) => new UiMerchant(parent, ctx, close)
            };
        }

        // ------------------------------------------------------------------ the shared state (bags / paper doll read it)

        private static UiMerchant _shown;           // the open merchant window (one layout at a time)
        private static bool _repair;
        private static bool _splitOpen;
        private static readonly RepairCursorMode RepairCursor = new RepairCursorMode();
        private static bool _logged;

        /// <summary>The repair cursor (ShowRepairCursor, MerchantRepairItemButton OnClick, MerchantFrame.xml:453-461): a click on an item repairs it.</summary>
        public static bool RepairMode { get { return _repair; } }

        /// <summary>MerchantFrame.selectedTab == 2 (no selling from the bags, ContainerFrame.lua:722-725).</summary>
        public static bool BuybackTabShown { get { return _shown != null && _shown.IsOpen && _shown._tab == 2; } }

        /// <summary>HideRepairCursor (a right click, the merchant closing, the Buyback tab).</summary>
        public static void EndRepairMode(string why)
        {
            if (!_repair) return;
            _repair = false;
            UiCursor.End(RepairCursor);
            UiContainers.Note("repair cursor off (" + why + ").");
        }

        private static void BeginRepairMode()
        {
            if (_repair) return;
            UiPickup.Clear("repair cursor");
            _repair = true;
            UiCursor.Begin(RepairCursor);
            UiContainers.Note("repair cursor on: a click on an item in the bags or on the character repairs it.");
        }

        /// <summary>The repair cursor as a world-click mode: a click on the world ends it (ours), Escape too (uicore's chain).</summary>
        private sealed class RepairCursorMode : IUiCursorMode
        {
            public string Name { get { return "repair"; } }
            public bool WorldClick(int button, ulong hovered) { EndRepairMode(button == 0 ? "a click on the world" : "right click"); return true; }
            public void Cancel(string why) { if (_repair) { _repair = false; UiContainers.Note("repair cursor off (" + why + ")."); } }
        }

        // ------------------------------------------------------------------ one item row (MerchantItemTemplate / MerchantBuyBackItem)

        private sealed class Row
        {
            public RectTransform Rt;
            public RawImage Slot, NameFrame;
            public TextMeshProUGUI Name;
            public UiItemButton Item;
            public UiSmallMoney Money;
            public string NameShown;
        }

        private readonly Row[] _rows = new Row[BuybackPerPage];
        private readonly Row _last;                                  // MerchantBuyBackItem (the newest buyback entry, merchant tab)
        private readonly RawImage _portrait;
        private readonly TextMeshProUGUI _name, _pageText, _repairText, _prevText, _nextText;
        private readonly RawImage[] _buybackArt = new RawImage[4], _borders = new RawImage[2];
        private readonly UiButton _prev, _next, _repairAll, _repairItem;
        private readonly RawImage _repairAllIcon, _repairItemIcon, _repairLock;
        private readonly UiMoney _money;
        private readonly UiNpcTab _tab1, _tab2;
        private readonly Action _close;
        private int _tab = 1, _page = 1, _pages = 1;
        private ulong _vendor;
        private string _nameShown, _pageShown;
        private bool _buybackPortrait;

        public UiMerchant(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            W = 384f;
            H = 512f;
            Root = UiKit.Rect(parent, "MerchantFrame", 0f, 0f, W, H);
            // enableMouse (MerchantFrame.xml:107): the paper takes the clicks; a bag item dropped on it is sold
            UiButton blocker = UiKit.Blocker(Root);
            UiKit.DropTarget(blocker, AcceptsSale, Sell, SaleHint);

            _portrait = Portrait(UiArt.Tex(UiArt.PortraitNpc));                                      // MerchantFramePortrait 60x60 (7,-6)
            UiKit.Frame4(Root, "Art", Art + "UI-Merchant-TopLeft.blp", Art + "UI-Merchant-TopRight.blp",
                Art + "UI-Merchant-BotLeft.blp", Art + "UI-Merchant-BotRight.blp");                 // MerchantFrame.xml:138-169
            _buybackArt[0] = UiKit.Pic(Root, "BuybackFrameTopLeft", UiArt.Tex(Art + "UI-BuyBack-TopLeft.blp"), 19f, 71f, 256f, 256f);   // :202-238
            _buybackArt[1] = UiKit.Pic(Root, "BuybackFrameTopRight", UiArt.Tex(Art + "UI-BuyBack-TopRight.blp"), 275f, 71f, 64f, 256f);
            _buybackArt[2] = UiKit.Pic(Root, "BuybackFrameBotLeft", UiArt.Tex(Art + "UI-BuyBack-BotLeft.blp"), 19f, 327f, 256f, 128f);
            _buybackArt[3] = UiKit.Pic(Root, "BuybackFrameBotRight", UiArt.Tex(Art + "UI-BuyBack-BotRight.blp"), 275f, 327f, 64f, 128f);
            Texture2D border = UiArt.Tex(Art + "UI-Merchant-BottomBorder.blp");
            _borders[0] = UiKit.Pic(Root, "MerchantFrameBottomLeftBorder", border, 0f, 1f, 0f, 0.4765625f, 14f, 366f, 256f, 61f);              // BOTTOMLEFT (14,85) (:240-251)
            _borders[1] = UiKit.Pic(Root, "MerchantFrameBottomRightBorder", border, 0f, 0.296875f, 0.4765625f, 0.953125f, 270f, 366f, 76f, 61f); // (:252-262)

            _name = UiKit.Text(Root, "MerchantNameText", "", 13f, UiArt.Gold, TextAlignmentOptions.Top, 72f, 17f, 240f, 16f);   // TOP (0,-17) GameFontNormal
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _pageText = UiKit.Text(Root, "MerchantPageText", "", 13f, UiArt.Gold, TextAlignmentOptions.Bottom, 126f, 346f, 104f, 16f);   // BOTTOM (-14,150), 104 wide
            _pageText.textWrappingMode = TextWrappingModes.NoWrap;
            _repairText = UiKit.Text(Root, "MerchantRepairText", UiGlobalStrings.G("REPAIR_ITEMS", "Repair Items"), 10f, Color.white,
                TextAlignmentOptions.BottomLeft, 26f, 395f, 80f, 14f);                                // BOTTOMLEFT (26,103) GameFontHighlightSmall (GlobalStrings.lua:6061)
            _repairText.textWrappingMode = TextWrappingModes.NoWrap;

            // items: MerchantItem1 TOPLEFT (24,-80), odd ones below the previous odd one, even ones (12,0) right of the odd one (:265-354)
            for (int i = 0; i < BuybackPerPage; i++) _rows[i] = MakeRow(Root, "MerchantItem" + (i + 1), i, false);
            _last = MakeRow(Root, "MerchantBuyBackItem", -1, true);
            UiKit.Move(_last.Rt, 189f, 385f);                                                         // TOPLEFT of MerchantItem10's BOTTOMLEFT (0,-53) (:536-545)

            // repair (MerchantFrame.xml:373-466): Repair All BOTTOMRIGHT at BOTTOMLEFT (172,91), Repair an Item (-2,0) left of it
            Texture2D icons = UiArt.Tex(Art + "UI-Merchant-RepairIcons.blp");
            _repairAll = UiKit.Button(Root, "MerchantRepairAllButton", 136f, 385f, 36f, 36f);
            _repairAllIcon = UiKit.Pic(_repairAll.transform, "Icon", icons, 0.28125f, 0.5625f, 0f, 0.5625f, 0f, 0f, 36f, 36f);
            RepairArt(_repairAll);
            _repairAll.Click = RepairAll;
            _repairAll.Tip = RepairAllTip;
            _repairItem = UiKit.Button(Root, "MerchantRepairItemButton", 98f, 385f, 36f, 36f);
            _repairItemIcon = UiKit.Pic(_repairItem.transform, "Icon", icons, 0f, 0.28125f, 0f, 0.5625f, 0f, 0f, 36f, 36f);
            RepairArt(_repairItem);
            _repairLock = UiKit.Pic(_repairItem.transform, "Locked", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 36f, 36f);   // LockHighlight in repair mode (:786-790)
            _repairLock.enabled = false;
            _repairItem.Click = () => { if (_repair) EndRepairMode("the button again"); else BeginRepairMode(); };
            _repairItem.Tip = () => "<size=14>" + UiGlobalStrings.G("REPAIR_AN_ITEM", "Repair an Item") + "</size>\n<color=#00ff00>" +
                                     (Ctx.Vr ? "Then tap an item in your bags." : "Then click an item in your bags or on your character.") + "</color>";   // GlobalStrings.lua:6059 + ours

            // paging (MerchantFrame.xml:658-730): CENTER at BOTTOMLEFT (37,156) / (324,156)
            _prev = UiKit.ArtButton(Root, "MerchantPrevPageButton", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.MouseHilight, 21f, 340f, 32f, () => Turn(-1));
            _next = UiKit.ArtButton(Root, "MerchantNextPageButton", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.MouseHilight, 308f, 340f, 32f, () => Turn(1));
            _prevText = UiKit.Text(Root, "Prev", UiGlobalStrings.G("PREV", "Prev"), 13f, UiArt.Gold, TextAlignmentOptions.Left, 53f, 348f, 60f, 16f);    // GlobalStrings.lua:5717
            _nextText = UiKit.Text(Root, "Next", UiGlobalStrings.G("NEXT", "Next"), 13f, UiArt.Gold, TextAlignmentOptions.Right, 245f, 348f, 60f, 16f);   // :5178

            _money = new UiMoney(Root, 344f, 432f, Color.white);                                      // MerchantMoneyFrame BOTTOMRIGHT (-40,67)
            UiKit.CloseButton(Root, 322f, 8f, () => { if (_close != null) _close(); });               // TOPRIGHT (-30,-8)

            // tabs: MerchantFrameTab1 CENTER at BOTTOMLEFT (60,46), Tab2 (-16,0) right of it (:732-771)
            _tab1 = UiNpcTab.Make(Root, UiGlobalStrings.G("MERCHANT", "Merchant"), 0f, 450f, () => SetTab(1));   // GlobalStrings.lua:4921
            UiKit.Move(_tab1.B.Rt, 60f - _tab1.W * 0.5f, 450f);
            _tab2 = UiNpcTab.Make(Root, UiGlobalStrings.G("BUYBACK", "Buyback"), 60f + _tab1.W * 0.5f - 16f, 450f, () => SetTab(2));   // :1171
            _tab1.Select(true);

            Root.gameObject.SetActive(false);
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI merchant (npcui): WoW's MerchantFrame (MerchantFrame.xml/.lua); ours: VR tap = buy one lot, hold = quantity, the hint lines, " +
                            "a click on the world ends the repair cursor, the repair buttons greyed (not desaturated) when nothing needs repair.");
            }
        }

        private static void RepairArt(UiButton b)
        {
            b.Pushed = UiKit.Pic(b.transform, "Pushed", UiArt.Tex(UiArt.QuickslotDepress), 0f, 0f, 36f, 36f);   // PushedTexture UI-Quickslot-Depress
            b.Pushed.enabled = false;
            b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 36f, 36f);   // ButtonHilight-Square ADD
            b.Hilite.enabled = false;
        }

        private Row MakeRow(Transform parent, string name, int i, bool buybackLast)
        {
            var r = new Row();
            float x = i >= 0 && i % 2 == 1 ? 24f + RowW + 12f : 24f;
            r.Rt = UiKit.Rect(parent, name, x, 80f, RowW, buybackLast ? 37f : RowH);
            r.Slot = UiKit.Pic(r.Rt, "SlotTexture", UiArt.Tex(UiArt.EmptySlot), -13f, -13f, 64f, 64f);                  // TOPLEFT (-13,13)
            if (buybackLast)
            {
                r.NameFrame = UiKit.Pic(r.Rt, "NameFrame", UiArt.Tex(Art + "UI-Merchant-LabelSlots.blp"), 42f, -3f, 128f, 64f);   // LEFT of the slot's RIGHT (-9,-10)
                r.Name = UiKit.Text(r.Rt, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Left, 46f, -6f, 90f, 30f);           // 90x30 (-5,10)
                r.Money = new UiSmallMoney(r.Rt, "MoneyFrame", 42f, 23f);                                                        // BOTTOMLEFT of NameFrame (0,25)
            }
            else
            {
                r.NameFrame = UiKit.Pic(r.Rt, "NameFrame", UiArt.Tex(Art + "UI-Merchant-LabelSlots.blp"), 42f, -2f, 128f, 78f);   // LEFT of the slot's RIGHT (-9,-18)
                r.Name = UiKit.Text(r.Rt, "Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Left, 46f, -3f, 100f, 30f);          // GameFontNormalSmall 100x30 (-5,7)
                r.Money = new UiSmallMoney(r.Rt, "MoneyFrame", 44f, 32f);                                                        // BOTTOMLEFT of NameFrame (2,31)
            }
            r.Name.overflowMode = TextOverflowModes.Ellipsis;
            r.Item = new UiItemButton(r.Rt, "ItemButton", 0f, 0f, 37f);                                                         // ItemButtonTemplate TOPLEFT
            r.Item.WithStock();
            UiButton b = r.Item.B;
            if (buybackLast)
            {
                b.Click = () => BuybackLast();
                b.RightClick = () => BuybackLast();
                b.Tip = () => BuybackTip(Ctx.P != null ? Ctx.P.LastBuyback : -1);
            }
            else
            {
                int k = i;
                UiKit.Draggable(b, () => MerchantPayload(k), true);                      // PickupMerchantItem (MerchantItemButton_OnClick / OnDragStart)
                UiKit.DropTarget(b, AcceptsSale, Sell, SaleHint);                         // a bag item clicked on an item button is sold too
                b.Click = () => RowClick(k, b, false);
                b.RightClick = () => RowClick(k, b, true);
                b.LongPress = () => { if (_tab == 1) Quantity(k, b); };
                b.Tip = () => RowTip(k);
            }
            return r;
        }

        // ------------------------------------------------------------------ the window

        protected override void OnOpen()
        {
            _shown = this;
            _tab = 1;                                    // PanelTemplates_SetTab(MerchantFrame, 1) (MerchantFrame_OnShow, :43)
            _page = 1;                                   // MERCHANT_SHOW: self.page = 1 (:31)
            _tab1.Select(true);
            _tab2.Select(false);
            _vendor = Ctx.S != null && Ctx.S.CurrentMerchant != null ? Ctx.S.CurrentMerchant.VendorGuid : 0UL;
            UiBags b = UiBags.Current;
            if (b != null) b.ServiceOpen(true);         // BACKPACK_WAS_OPEN = OpenBackpack() (:39-40)
        }

        protected override void OnClose()
        {
            // MerchantFrame_OnHide (:51-63): CloseBackpack if it was closed, ResetCursor, the token popups hide
            UiBags b = UiBags.Current;
            if (b != null) b.ServiceOpen(false);
            EndRepairMode("the merchant closed");
            UiPickupPayload c = UiPickup.Current;
            if (c != null && (c.Kind == UiPickupKind.MerchantItem || c.Kind == UiPickupKind.BuybackItem)) UiPickup.Clear("the merchant closed");
            if (_splitOpen && UiStackSplit.IsOpen) UiStackSplit.Cancel("the merchant closed");
            _splitOpen = false;
            UiPopup.HideWhich("CONFIRM_PURCHASE_TOKEN_ITEM");
            UiPopup.HideWhich("CONFIRM_HIGH_COST_ITEM");
            if (ReferenceEquals(_shown, this)) _shown = null;
        }

        public override void Refresh(float now)
        {
            IWowSession s = Ctx.S;
            PlayerState p = Ctx.P;
            MerchantView m = s != null ? s.CurrentMerchant : null;
            if (m == null || p == null) return;
            _shown = this;
            if (m.VendorGuid != _vendor) { _vendor = m.VendorGuid; _page = 1; _tab = 1; _tab1.Select(true); _tab2.Select(false); }
            if (_repair) UiCursor.SetKind(UiCursorKind.Repair);
            _money.Set(p.Money);
            if (_tab == 1) RefreshMerchant(m, p);
            else RefreshBuyback(p);
        }

        private void SetTab(int tab)
        {
            if (tab == _tab) return;
            _tab = tab;
            _tab1.Select(tab == 1);
            _tab2.Select(tab == 2);
            if (tab == 2) EndRepairMode("the Buyback tab");
            if (_splitOpen && UiStackSplit.IsOpen) { UiStackSplit.Cancel("tab"); _splitOpen = false; }
        }

        private void Turn(int d)
        {
            UiSound.Named("igMainMenuOptionCheckBoxOn");   // MerchantPrevPageButton_OnClick / Next (:340 / :346)
            _page = Mathf.Clamp(_page + d, 1, Math.Max(1, _pages));
        }

        private void SetName(string text)
        {
            if (string.Equals(text, _nameShown)) return;
            _nameShown = text;
            _name.SetText(text ?? "");
        }

        private void SetPortrait(bool buyback)
        {
            if (buyback == _buybackPortrait) return;
            _buybackPortrait = buyback;
            _portrait.texture = UiArt.Tex(buyback ? Art + "UI-BuyBack-Icon.blp" : UiArt.PortraitNpc);   // MerchantFrame_UpdateBuybackInfo (:264)
        }

        /// <summary>MerchantFrame_UpdateMerchantInfo (MerchantFrame.lua:72-228).</summary>
        private void RefreshMerchant(MerchantView m, PlayerState p)
        {
            SetName(m.VendorName);
            SetPortrait(false);
            int n = m.Items.Count;
            _pages = Math.Max(1, (n + ItemsPerPage - 1) / ItemsPerPage);
            if (_page > _pages) _page = _pages;
            bool pickUps = UiContainers.SlotsPickUp(Ctx);
            for (int i = 0; i < BuybackPerPage; i++)
            {
                Row r = _rows[i];
                bool on = i < ItemsPerPage;
                if (r.Rt.gameObject.activeSelf != on) r.Rt.gameObject.SetActive(on);
                if (!on) continue;
                UiKit.Move(r.Rt, i % 2 == 1 ? 24f + RowW + 12f : 24f, 80f + (i / 2) * (RowH + 8f));   // (0,-8) between rows (:223-227)
                int index = (_page - 1) * ItemsPerPage + i;
                MerchantItemView mi = index < n ? m.Items[index] : null;
                if (mi == null || mi.Item == null)
                {
                    EmptyRow(r);
                    continue;
                }
                r.Item.Show(true);
                SetRowName(r, mi.Item.Name);
                Color frame, slot, icon;
                if (mi.Stock == 0)
                {
                    if (!mi.Usable) frame = slot = icon = new Color(0.5f, 0f, 0f);       // :130-136
                    else frame = slot = icon = new Color(0.5f, 0.5f, 0.5f);              // :137-142
                }
                else if (!mi.Usable) { frame = slot = Color.red; icon = new Color(0.9f, 0f, 0f); }   // :145-149
                else { frame = new Color(0.5f, 0.5f, 0.5f); slot = Color.white; icon = Color.white; } // :150-154
                Tint(r, frame, slot);
                r.Item.Set(mi.Item, mi.BuyCount, icon);                                              // SetItemButtonCount(quantity)
                r.Item.SetStock(mi.Stock);                                                           // SetItemButtonStock(numAvailable)
                r.Item.B.ClickPicksUp = pickUps;
                bool ext = mi.HonorCost > 0 || mi.ArenaCost > 0 || (mi.CostItems != null && mi.CostItems.Length > 0);
                r.Money.Show(true);
                if (ext) r.Money.SetAll(mi.Price, mi.HonorCost, mi.ArenaCost, HonorIcon(p), mi.CostItems, mi.CostCounts);
                else r.Money.Set(mi.Price);
            }
            // the buy-back-last button (:157-174)
            int last = p.LastBuyback;
            ItemView bb = last >= 0 && last < p.Buyback.Length ? p.Buyback[last] : null;
            if (!_last.Rt.gameObject.activeSelf) _last.Rt.gameObject.SetActive(true);
            if (bb != null)
            {
                SetRowName(_last, bb.Name);
                _last.Item.Show(true);
                _last.Item.Set(bb, bb.Count, Color.white);
                _last.Money.Show(true);
                _last.Money.Set(p.BuybackPrice[last]);
                Tint(_last, new Color(0.5f, 0.5f, 0.5f), Color.white);
            }
            else
            {
                SetRowName(_last, "");
                _last.Item.Set(null, 0, Color.white);
                _last.Item.Show(false);
                _last.Money.Show(false);
                Tint(_last, new Color(0.5f, 0.5f, 0.5f), new Color(0.4f, 0.4f, 0.4f));
            }
            // paging (:177-209)
            bool paged = n > ItemsPerPage;
            ShowPaging(paged);
            if (paged)
            {
                string pt = UiGlobalStrings.FormatOr("MERCHANT_PAGE_NUMBER", "Page %s of %s", _page.ToString(), _pages.ToString());   // GlobalStrings.lua:4925
                if (!string.Equals(pt, _pageShown)) { _pageShown = pt; _pageText.SetText(pt); }
                _prev.Interactable = _page > 1;
                _next.Interactable = _page < _pages;
            }
            // repair (MerchantFrame_UpdateRepairButtons :554-590, MerchantFrame_UpdateCanRepairAll :531-543)
            bool repair = m.CanRepair;
            SetActive(_repairAll, repair);
            SetActive(_repairItem, repair);
            if (_repairText.enabled != repair) _repairText.enabled = repair;
            if (repair)
            {
                bool can = m.RepairAllCost > 0;
                _repairAll.Interactable = can;
                Color c = can ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                if (_repairAllIcon.color != c) _repairAllIcon.color = c;
                if (_repairLock.enabled != _repair) _repairLock.enabled = _repair;
            }
            else if (_repair) EndRepairMode("not an armorer");
            ShowMerchantParts(true);
        }

        /// <summary>MerchantFrame_UpdateBuybackInfo (MerchantFrame.lua:262-318): the 12 buyback slots in their order.</summary>
        private void RefreshBuyback(PlayerState p)
        {
            SetName(UiGlobalStrings.G("MERCHANT_BUYBACK", "Merchant Buyback"));   // GlobalStrings.lua:4923
            SetPortrait(true);
            for (int i = 0; i < BuybackPerPage; i++)
            {
                Row r = _rows[i];
                if (!r.Rt.gameObject.activeSelf) r.Rt.gameObject.SetActive(true);
                UiKit.Move(r.Rt, i % 2 == 1 ? 24f + RowW + 12f : 24f, 80f + (i / 2) * (RowH + 15f));   // (0,-15) between rows (:269-273)
                ItemView it = i < p.Buyback.Length ? p.Buyback[i] : null;
                if (it == null) { EmptyRow(r); continue; }
                r.Item.Show(true);
                SetRowName(r, it.Name);
                Tint(r, new Color(0.5f, 0.5f, 0.5f), Color.white);
                r.Item.Set(it, it.Count, Color.white);
                r.Item.SetStock(-1);
                r.Item.B.ClickPicksUp = false;                                   // a buyback item is not picked up: any click buys it back (:407-408)
                r.Money.Show(true);
                r.Money.Set(i < p.BuybackPrice.Length ? p.BuybackPrice[i] : 0);
            }
            if (_last.Rt.gameObject.activeSelf) _last.Rt.gameObject.SetActive(false);
            ShowPaging(false);
            SetActive(_repairAll, false);
            SetActive(_repairItem, false);
            if (_repairText.enabled) _repairText.enabled = false;
            ShowMerchantParts(false);
        }

        private static void EmptyRow(Row r)
        {
            // :213-221 / :303-310: the button hides, the name frame 0.5 grey, the slot 0.4
            SetRowName(r, "");
            r.Item.Set(null, 0, Color.white);
            r.Item.SetStock(-1);
            r.Item.Show(false);
            r.Money.Show(false);
            Tint(r, new Color(0.5f, 0.5f, 0.5f), new Color(0.4f, 0.4f, 0.4f));
        }

        private static void SetRowName(Row r, string text)
        {
            if (string.Equals(text, r.NameShown)) return;
            r.NameShown = text;
            r.Name.SetText(text ?? "");
        }

        private static void Tint(Row r, Color nameFrame, Color slot)
        {
            if (r.NameFrame.color != nameFrame) r.NameFrame.color = nameFrame;   // SetItemButtonNameFrameVertexColor
            if (r.Slot.color != slot) r.Slot.color = slot;                       // SetItemButtonSlotVertexColor
        }

        private static void SetActive(UiButton b, bool on)
        {
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
        }

        private void ShowPaging(bool on)
        {
            SetActive(_prev, on);
            SetActive(_next, on);
            if (_prevText.enabled != on) _prevText.enabled = on;
            if (_nextText.enabled != on) _nextText.enabled = on;
            if (_pageText.enabled != on) _pageText.enabled = on;
        }

        private void ShowMerchantParts(bool merchant)
        {
            for (int i = 0; i < _buybackArt.Length; i++) if (_buybackArt[i].enabled == merchant) _buybackArt[i].enabled = !merchant;
            for (int i = 0; i < _borders.Length; i++) if (_borders[i].enabled != merchant) _borders[i].enabled = merchant;
        }

        private static string HonorIcon(PlayerState p)
        {
            // "Interface\\PVPFrame\\PVP-Currency-"..factionGroup (MerchantFrame.lua:462-465)
            return @"Interface\PVPFrame\PVP-Currency-" + (p != null && p.Faction == WowFaction.Horde ? "Horde" : "Alliance") + ".blp";
        }

        // ------------------------------------------------------------------ clicks

        private MerchantItemView ItemOf(int k)
        {
            MerchantView m = Ctx.S != null ? Ctx.S.CurrentMerchant : null;
            int index = (_page - 1) * ItemsPerPage + k;
            return m != null && _tab == 1 && k < ItemsPerPage && index >= 0 && index < m.Items.Count ? m.Items[index] : null;
        }

        private int IndexOf(int k) { return (_page - 1) * ItemsPerPage + k; }

        /// <summary>PickupMerchantItem: the merchant item on the cursor (Index = the list index); dropped on a bag slot it is bought there.</summary>
        private UiPickupPayload MerchantPayload(int k)
        {
            if (_tab != 1 || UiContainers.SpecialMode(Ctx)) return null;
            MerchantItemView mi = ItemOf(k);
            if (mi == null || mi.Item == null || mi.Stock == 0) return null;
            ItemView it = mi.Item;
            return new UiPickupPayload
            {
                Kind = UiPickupKind.MerchantItem, Index = IndexOf(k), ItemEntry = it.Entry, Count = mi.BuyCount,
                IconPath = it.IconPath, Name = it.Name, Quality = it.Quality, Source = mi
            };
        }

        /// <summary>MerchantItemButton_OnClick (MerchantFrame.lua:379-411): Buyback tab = buy back; right click (VR tap) = buy one lot
        /// (an extended cost / a high price asks first); desktop Shift-click = the quantity (OnModifiedClick :412-430).</summary>
        private void RowClick(int k, UiButton b, bool right)
        {
            IWowSession s = Ctx.S;
            if (s == null) return;
            if (_tab == 2) { Buyback(k); return; }
            if (UiContainers.ItemTargeting(Ctx) || _repair) return;
            if (!right && !Ctx.Vr)
            {
                if (UiContainers.ShiftHeld(Ctx)) { Quantity(k, b); return; }
                return;   // a plain left click picks it up (uicore's click-carry) - nothing else here
            }
            if (ItemOf(k) == null) return;
            UiContainers.BuyChecked(Ctx, IndexOf(k), 1, -1, -1);
        }

        /// <summary>OpenStackSplitFrame(maxStack) - the max is MaxPurchases (the session's min of stack, money and stock).</summary>
        private void Quantity(int k, UiButton b)
        {
            MerchantItemView mi = ItemOf(k);
            if (mi == null || mi.MaxPurchases <= 1) return;                      // "if ( maxStack > 1 )" (:419)
            int index = IndexOf(k);
            ulong vendor = _vendor;
            _splitOpen = true;
            UiStackSplit.Open(mi.MaxPurchases, b, n =>
            {
                _splitOpen = false;
                IWowSession s = Ctx.S;
                if (n <= 0 || s == null || s.CurrentMerchant == null || s.CurrentMerchant.VendorGuid != vendor) return;
                UiContainers.BuyChecked(Ctx, index, n, -1, -1);                  // SplitStack -> MerchantFrame_ConfirmExtendedItemCost / BuyMerchantItem (:369-376)
            });
        }

        private void Buyback(int i)
        {
            IWowSession s = Ctx.S;
            PlayerState p = Ctx.P;
            if (s == null || p == null || i < 0 || i >= p.Buyback.Length || p.Buyback[i] == null) return;
            s.BuybackItem(i);                                                     // BuybackItem(self:GetID()) (:409)
        }

        private void BuybackLast()
        {
            PlayerState p = Ctx.P;
            if (p != null && p.LastBuyback >= 0) Buyback(p.LastBuyback);           // BuybackItem(GetNumBuybackItems()) (:602-604)
        }

        private void RepairAll()
        {
            IWowSession s = Ctx.S;
            if (s == null || s.CurrentMerchant == null) return;
            s.RepairItem(-1, 0);                                                  // RepairAllItems()
            UiSound.Named("ITEM_REPAIR");                                          // (:402-406)
        }

        // ------------------------------------------------------------------ selling by drop

        private bool AcceptsSale(UiPickupPayload p)
        {
            if (p == null || p.Kind != UiPickupKind.Item || p.Bag < 0 || _tab != 1) return false;
            if (InvPos.IsBank(p.Bag, p.Slot)) return false;
            if (InvPos.IsEquipment(p.Bag, p.Slot)) return false;   // ours: worn items are not sold by a drop (take them off first)
            return Ctx.S != null && Ctx.S.CurrentMerchant != null;
        }

        private void Sell(UiPickupPayload p)
        {
            IWowSession s = Ctx.S;
            if (s == null || p == null) return;
            ItemView it = UiContainers.ItemAt(s.Player, p.Bag, p.Slot);
            int count = p.Count > 0 && it != null && p.Count < it.Count ? p.Count : 0;
            s.SellItem(p.Bag, p.Slot, count);
        }

        private string SaleHint(UiPickupPayload p)
        {
            if (!AcceptsSale(p)) return null;
            ItemView it = Ctx.S != null ? UiContainers.ItemAt(Ctx.S.Player, p.Bag, p.Slot) : null;
            if (it != null && it.SellPrice <= 0) return UiGlobalStrings.G("ITEM_UNSELLABLE", "No sell price");   // GlobalStrings.lua:4534
            return "<color=#00ff00>Sell it.</color>";   // ours
        }

        // ------------------------------------------------------------------ tooltips

        private string RowTip(int k)
        {
            if (_tab == 2) return BuybackTip(k);
            MerchantItemView mi = ItemOf(k);
            if (mi == null || mi.Item == null) return null;
            Sb.Length = 0;
            if (mi.Stock == 0) Sb.Append("<color=#ff2020>Sold out</color>\n");   // ours
            if (mi.HonorCost > 0) Sb.Append(UiGlobalStrings.FormatOr("MERCHANT_HONOR_POINTS", "%d Honor Points", mi.HonorCost)).Append('\n');   // GlobalStrings.lua:4924
            if (mi.ArenaCost > 0) Sb.Append(UiGlobalStrings.FormatOr("MERCHANT_ARENA_POINTS", "%d Arena Points", mi.ArenaCost)).Append('\n');   // :4922
            if (mi.RequiredArenaRating > 0) Sb.Append("Requires personal arena rating ").Append(mi.RequiredArenaRating).Append('\n');   // ours
            if (Ctx.Vr) Sb.Append("<color=#00ff00>Tap to buy. Hold to choose how many. Drag onto a bag slot to buy it there.</color>");   // ours
            else Sb.Append("<color=#00ff00>Right-click to buy. Shift-click to buy several. Click and place in a bag slot.</color>");      // ours
            return UiTooltip.Item(mi.Item, Sb.ToString());
        }

        private string BuybackTip(int i)
        {
            PlayerState p = Ctx.P;
            if (p == null || i < 0 || i >= p.Buyback.Length || p.Buyback[i] == null) return null;
            return UiTooltip.Item(p.Buyback[i], "<color=#00ff00>" + (Ctx.Vr ? "Tap" : "Click") + " to buy it back.</color>");   // ours
        }

        private string RepairAllTip()
        {
            MerchantView m = Ctx.S != null ? Ctx.S.CurrentMerchant : null;
            if (m == null) return null;
            // OnEnter (:388-396): REPAIR_ALL_ITEMS + the cost when something needs repair
            if (m.RepairAllCost <= 0) return "<size=14>" + UiGlobalStrings.G("REPAIR_ALL_ITEMS", "Repair All Items") + "</size>";
            return "<size=14>" + UiGlobalStrings.G("REPAIR_ALL_ITEMS", "Repair All Items") + "</size>\n" + UiNpcPopups.MoneyText(m.RepairAllCost);   // GlobalStrings.lua:6058
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// SmallMoneyFrameTemplate as a STATIC money frame (MoneyFrame.lua:34-40 / :340-400: a denomination shows only when above 0; a zero total
    /// shows 0 copper), left to right from its anchor, then the alternate currencies of an extended cost (honor, arena points, up to three
    /// cost items: the number then the icon, AltCurrencyFrame_Update / MerchantFrame_UpdateAltCurrency :231-260). Coins = UI-MoneyIcons
    /// (gold 0-0.25, silver 0.25-0.5, copper 0.5-0.75, MoneyFrame.xml) at 13 units like UiMoney.
    /// </summary>
    public sealed class UiSmallMoney
    {
        public const float Icon = 13f;
        public const float Gap = 2f, Spacing = 4f;       // ours: number -> icon, icon -> next number
        public const string ArenaIcon = @"Interface\PVPFrame\PVP-ArenaPoints-Icon.blp";   // MerchantFrame.lua:470
        public readonly RectTransform Root;
        public float Width { get; private set; }
        private readonly List<TextMeshProUGUI> _n = new List<TextMeshProUGUI>();
        private readonly List<RawImage> _i = new List<RawImage>();
        private readonly UiSurface _surface;
        private readonly bool _pointable, _over;
        private Color _color = Color.white;
        private int _sig = int.MinValue;
        private bool _pending;

        public UiSmallMoney(Transform parent, string name, float x, float y)
        {
            _surface = UiKit.Surface;
            _pointable = UiKit.Pointable;
            _over = UiKit.Over;
            Root = UiKit.Rect(parent, name, x, y, 160f, 14f);
            for (int k = 0; k < 3; k++) MakePart();
        }

        public void Show(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        /// <summary>SetMoneyFrameColor (white / red, BankFrame.lua UpdateBagSlotStatus).</summary>
        public void SetColor(Color c)
        {
            if (c == _color) return;
            _color = c;
            for (int k = 0; k < _n.Count; k++) _n[k].color = c;
        }

        public void Set(int copper) { SetAll(copper, 0, 0, null, null, null); }

        public void SetAll(int copper, int honor, int arena, string honorIcon, int[] items, int[] counts)
        {
            int sig = copper;
            unchecked
            {
                sig = sig * 31 + honor;
                sig = sig * 31 + arena;
                sig = sig * 31 + (honorIcon != null ? honorIcon.Length : 0);
                if (items != null) for (int i = 0; i < items.Length; i++) sig = sig * 31 + items[i] * 7 + (counts != null && i < counts.Length ? counts[i] : 0);
            }
            if (sig == _sig && !_pending) return;
            _sig = sig;
            _pending = false;
            int k = 0;
            float x = 0f;
            bool alt = honor > 0 || arena > 0 || (items != null && items.Length > 0);
            if (copper > 0 || !alt)
            {
                int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
                Texture2D coins = UiArt.Tex(UiArt.MoneyIcons);
                if (g > 0) x = Part(k++, x, g, coins, 0f);
                if (s > 0) x = Part(k++, x, s, coins, 0.25f);
                if (c > 0 || copper <= 0) x = Part(k++, x, c, coins, 0.5f);
            }
            if (honor > 0) x = Part(k++, x, honor, UiArt.Tex(honorIcon), -1f);
            if (arena > 0) x = Part(k++, x, arena, UiArt.Tex(ArenaIcon), -1f);
            if (items != null)
                for (int i = 0; i < items.Length && i < 3; i++)                  // MAX_ITEM_COST 3 (MerchantFrame.lua:3)
                {
                    int n = counts != null && i < counts.Length ? counts[i] : 0;
                    if (items[i] <= 0 || n <= 0) continue;
                    string path = UiMail.ItemIcon(items[i]);
                    Texture2D t = path != null ? UiArt.Icon(path) : null;
                    if (t == null && path != null) _pending = true;                 // the icon loads over a few frames
                    x = Part(k++, x, n, t, -1f);
                }
            for (int j = k; j < _n.Count; j++) { _n[j].enabled = false; _i[j].enabled = false; }
            Width = Mathf.Max(0f, x - Spacing);
        }

        private void MakePart()
        {
            using (new UiBuildScope(_surface, _pointable, _over))
            {
                TextMeshProUGUI t = UiKit.Text(Root, "N" + _n.Count, "", 11f, _color, TextAlignmentOptions.Left, 0f, 0f, 40f, 14f, UiArt.Numbers);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.enabled = false;
                _n.Add(t);
                RawImage r = UiKit.Pic(Root, "I" + _i.Count, null, 0f, 1f, Icon, Icon);
                r.enabled = false;
                _i.Add(r);
            }
        }

        /// <summary>One "number + icon" part at x; u >= 0 = a UI-MoneyIcons coin (u..u+0.25), else the whole texture. Returns the next x.</summary>
        private float Part(int k, float x, int value, Texture2D tex, float u)
        {
            while (_n.Count <= k) MakePart();
            TextMeshProUGUI t = _n[k];
            string s = value.ToString();
            t.SetText(s);
            float w = Mathf.Ceil(t.GetPreferredValues(s).x) + 1f;
            UiKit.Move(t.rectTransform, x, 0f);
            UiKit.Size(t.rectTransform, w, 14f);
            t.enabled = true;
            RawImage r = _i[k];
            r.texture = tex;
            if (u >= 0f) UiKit.Coords(r, u, u + 0.25f, 0f, 1f);
            else UiKit.Coords(r, 0f, 1f, 0f, 1f);
            UiKit.Move(r.rectTransform, x + w + Gap, 0.5f);
            r.enabled = tex != null;
            return x + w + Gap + Icon + Spacing;
        }
    }

    // =====================================================================================================================
    /// <summary>CharacterFrameTabButtonTemplate (UI-Character-ActiveTab / -InActiveTab / -Tab-Highlight, the selected one 5 higher) - the
    /// same art and sizes as UiSocialHost.BottomTab (width = the text + 40, at least 60; ours there and here).</summary>
    public sealed class UiNpcTab
    {
        private const string TabOff = @"Interface\PaperDollInfoFrame\UI-Character-InActiveTab.blp";
        private const string TabOn = @"Interface\PaperDollInfoFrame\UI-Character-ActiveTab.blp";
        private const string TabHilite = @"Interface\PaperDollInfoFrame\UI-Character-Tab-Highlight.blp";
        public UiButton B;
        public float W;
        private readonly RawImage[] _off = new RawImage[3], _on = new RawImage[3];
        private TextMeshProUGUI _label;
        private bool _selected = true;

        public static UiNpcTab Make(Transform parent, string label, float x, float y, Action click)
        {
            var t = new UiNpcTab();
            t.B = UiKit.Button(parent, "Tab " + label, x, y, 100f, 32f);
            t._label = UiKit.Text(t.B.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 100f, 32f);
            t._label.textWrappingMode = TextWrappingModes.NoWrap;
            float tw = Mathf.Ceil(t._label.GetPreferredValues(label).x);
            t.W = Mathf.Max(60f, tw + 40f);
            float mid = t.W - 40f;
            UiKit.Size(t.B.Rt, t.W, 32f);
            UiKit.Size(t._label.rectTransform, t.W, 32f);
            Texture2D off = UiArt.Tex(TabOff), on = UiArt.Tex(TabOn);
            t._off[0] = UiKit.Pic(t.B.transform, "L", off, 0f, 0.15625f, 0f, 1f, 0f, 0f, 20f, 32f);
            t._off[1] = UiKit.Pic(t.B.transform, "M", off, 0.15625f, 0.84375f, 0f, 1f, 20f, 0f, mid, 32f);
            t._off[2] = UiKit.Pic(t.B.transform, "R", off, 0.84375f, 1f, 0f, 1f, 20f + mid, 0f, 20f, 32f);
            t._on[0] = UiKit.Pic(t.B.transform, "L on", on, 0f, 0.15625f, 0f, 1f, 0f, -5f, 20f, 32f);
            t._on[1] = UiKit.Pic(t.B.transform, "M on", on, 0.15625f, 0.84375f, 0f, 1f, 20f, -5f, mid, 32f);
            t._on[2] = UiKit.Pic(t.B.transform, "R on", on, 0.84375f, 1f, 0f, 1f, 20f + mid, -5f, 20f, 32f);
            t.B.Hilite = UiKit.Pic(t.B.transform, "Highlight", UiArt.Add(TabHilite), 10f, 2f, t.W - 20f, 32f);
            t.B.Hilite.enabled = false;
            t._label.transform.SetAsLastSibling();
            t.B.Click = click;
            t.Select(false);
            return t;
        }

        public void Select(bool on)
        {
            if (on == _selected) return;
            _selected = on;
            for (int i = 0; i < 3; i++) { _off[i].enabled = !on; _on[i].enabled = on; }
            _label.color = on ? Color.white : UiArt.Gold;
            UiKit.Move(_label.rectTransform, 0f, on ? -4f : -1f);
        }
    }
}
