// iteration 11 - owner npcui (proposal-npc.md 4 / 6.1 / H7; the wave-0 stub by w0game, filled by npcui)
// BankFrame (BankFrame.xml / .lua of the user's install, harness UiDump into P/harness/out_d11npc; file:line per value): the 28 bank
// slots, the 7 bank bag slots (bought ones open their ContainerFrame, unbought ones red), "purchase space for an additional bag" with
// CONFIRM_BUY_BANK_SLOT, the money. Slots work like the bags (UiContainers): left-click / drag picks up, right-click (VR tap) moves the
// item to the bags (the bags' items go to the bank the same way), Shift-click splits. The bags open with the bank (ours, decision U4).
using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiBank : UiWindow
    {
        public const int GenericSlots = 28;          // NUM_BANKGENERIC_SLOTS (Constants.lua)
        public const int BagSlots = 7;               // NUM_BANKBAGSLOTS (Constants.lua:228)
        private const float SlotStepX = 49f;         // 37 + (12, 0) (BankFrame.xml BankFrameItem2..)
        private const float SlotStepY = 44f;         // 37 + (0, -7)
        private static readonly Color Unbought = new Color(1f, 0.1f, 0.1f);   // UpdateBagSlotStatus (BankFrame.lua:93-99)

        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Bank, Frame = "BankFrame", TitleKey = "BANK", Title = "Bank", Area = UiPanelArea.Left, Pushable = 6,
                Width = 425f, Height = 512f,                                         // UIPanelWindows BankFrame (UIParent.lua:30)
                SessionWindow = true, Palm = false,
                Wanted = s => s.BankerGuid != 0,
                NearGuid = s => s.BankerGuid,
                Closed = s => s.CloseBank(),
                OpenSound = "igMainMenuOpen", CloseSound = "igMainMenuClose",       // BankFrame.lua:179 / :201
                Owner = "npcui", Hidden = false,
                Build = (parent, ctx, close) => new UiBank(parent, ctx, close)
            };
        }

        private static UiBank _shown;
        private static bool _logged;

        /// <summary>The open bank frame's root (UiBags keeps its columns right of it, ContainerFrame.lua:553), null = closed.</summary>
        public static RectTransform ShownRoot { get { return _shown != null && _shown.IsOpen ? _shown.Root : null; } }

        private readonly UiItemButton[] _items = new UiItemButton[GenericSlots];
        private readonly UiItemButton[] _bags = new UiItemButton[BagSlots];
        private readonly RawImage[] _bagOpen = new RawImage[BagSlots];
        private readonly bool[] _bagPicture = new bool[BagSlots];
        private readonly TextMeshProUGUI _title;
        private readonly RectTransform _purchase;
        private readonly UiSmallMoney _cost;
        private readonly UiMoney _money;
        private readonly Action _close;
        private string _titleShown;

        public UiBank(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            W = 425f;
            H = 512f;
            Root = UiKit.Rect(parent, "BankFrame", 0f, 0f, W, H);
            UiKit.Blocker(Root);                                                                                     // enableMouse
            Portrait(UiArt.Tex(UiArt.PortraitNpc));                                                                  // BankPortraitTexture 60x60 (7,-6)
            UiKit.Pic(Root, "Art", UiArt.Tex(@"Interface\BankFrame\UI-BankFrame.blp"), 0f, 0f, 512f, 512f);          // 512x512 TOPLEFT
            _title = UiKit.Text(Root, "BankFrameTitleText", "", 12f, Color.white, TextAlignmentOptions.Center, 89.5f, 15f, 240f, 16f);   // CENTER (-3,233) GameFontHighlight
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Text(Root, "ItemSlotText", UiGlobalStrings.G("ITEMSLOTTEXT", "Item Slots"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 89.5f, 48f, 240f, 16f);   // CENTER (-3,200), GlobalStrings.lua:4332
            UiKit.Text(Root, "BagSlotText", UiGlobalStrings.G("BAGSLOTTEXT", "Bag Slots"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 89.5f, 250f, 240f, 16f);    // CENTER (-3,-2), :673
            UiKit.CloseButton(Root, 371f, 8f, () => { if (_close != null) _close(); });                              // TOPRIGHT (-22,-8)

            // BankFrameItem1..28: TOPLEFT (40,-73), 7 a row (BankFrame.xml)
            for (int i = 0; i < GenericSlots; i++)
            {
                int k = i;
                var b = new UiItemButton(Root, "BankFrameItem" + (i + 1), 40f + (i % 7) * SlotStepX, 73f + (i / 7) * SlotStepY, 37f);
                UiContainers.Wire(b.B, ctx, () => InvPos.Bag0, () => InvPos.BankStart + k);
                _items[i] = b;
            }
            // BankFrameBag1..7: below BankFrameItem22 (0,-32), (12,0) apart
            for (int i = 0; i < BagSlots; i++)
            {
                int k = i;
                var b = new UiItemButton(Root, "BankFrameBag" + (i + 1), 40f + i * SlotStepX, 274f, 37f);
                _bagOpen[i] = UiKit.Pic(b.B.transform, "HighlightFrameTexture", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 37f, 37f);   // BankItemButtonBagTemplate
                _bagOpen[i].enabled = false;
                UiKit.Draggable(b.B, () => BagPayload(k), true);
                UiKit.DropTarget(b.B, p => AcceptsBagDrop(p, k), p => DropOnBag(p, k), p => "<color=#00ff00>Put it in this bag.</color>");   // ours
                b.B.Click = () => BagClick(k);
                b.B.RightClick = () => BagClick(k);
                b.B.Tip = () => BagTip(k);
                _bags[i] = b;
            }

            // BankFramePurchaseInfo 380x72 CENTER (-23,-105): the label, "Cost:" + money, the Purchase button
            _purchase = UiKit.Rect(Root, "BankFramePurchaseInfo", -0.5f, 325f, 380f, 72f);
            UiKit.Text(_purchase, "Label", UiGlobalStrings.G("BANKSLOTPURCHASE_LABEL", "Do you wish to purchase space for an additional bag?"), 12f, Color.white,
                TextAlignmentOptions.Left, 42f, 8f, 380f, 16f).textWrappingMode = TextWrappingModes.NoWrap;            // GlobalStrings.lua:676, CENTER (42,20)
            string costLabel = UiGlobalStrings.G("COSTS_LABEL", "Cost:");                                              // :1868
            TextMeshProUGUI cost = UiKit.Text(_purchase, "BankFrameSlotCost", costLabel, 12f, UiArt.Gold, TextAlignmentOptions.Left, 45f, 39f, 80f, 14f);   // LEFT (45,-10)
            cost.textWrappingMode = TextWrappingModes.NoWrap;
            float cw = Mathf.Ceil(cost.GetPreferredValues(costLabel).x);
            _cost = new UiSmallMoney(_purchase, "BankFrameDetailMoneyFrame", 45f + cw + 10f, 39f);                   // LEFT of the label's RIGHT (10,0)
            UiKit.PanelButton(_purchase, UiGlobalStrings.G("BANKSLOTPURCHASE", "Purchase"), 246f, 35.5f, 124f, 21f, Purchase);   // RIGHT (-10,-10), :675

            _money = new UiMoney(Root, 395f, 395f, Color.white);                                                     // BankFrameMoneyFrame BOTTOMRIGHT (-30,103)
            Root.gameObject.SetActive(false);
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI bank (npcui): WoW's BankFrame (BankFrame.xml/.lua); ours: the bags open with the bank, the hint lines, VR tap = move to bags / bank.");
            }
        }

        protected override void OnOpen()
        {
            _shown = this;
            UiBags b = UiBags.Current;
            if (b != null) b.ServiceOpen(true);
        }

        protected override void OnClose()
        {
            UiPopup.HideWhich("CONFIRM_BUY_BANK_SLOT");   // BankFrame_OnHide (:199-210): the popup hides, the bank bags close
            UiBags b = UiBags.Current;
            if (b != null) b.ServiceOpen(false);
            if (ReferenceEquals(_shown, this)) _shown = null;
        }

        public override void Refresh(float now)
        {
            IWowSession s = Ctx.S;
            PlayerState p = Ctx.P;
            if (s == null || p == null || s.BankerGuid == 0) return;
            _shown = this;
            EntityState e = s.Find(s.BankerGuid);
            string title = e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : UiGlobalStrings.G("BANK", "Bank");   // BankFrameTitleText = UnitName("npc")
            if (!string.Equals(title, _titleShown)) { _titleShown = title; _title.SetText(title); }
            bool pickUps = UiContainers.SlotsPickUp(Ctx);
            ContainerView bank = p.Bank != null && p.Bank.Count > 0 ? p.Bank[0] : null;
            for (int i = 0; i < GenericSlots; i++)
            {
                ItemView it = bank != null && bank.Slots != null && i < bank.Slots.Length ? bank.Slots[i] : null;
                _items[i].Set(it, it != null ? it.Count : 0, Color.white);
                _items[i].B.ClickPicksUp = pickUps;
            }
            UiBags bags = UiBags.Current;
            Texture2D empty = null;
            for (int i = 0; i < BagSlots; i++)
            {
                bool bought = i < p.BankBagSlotsBought;
                ContainerView c = p.Bank != null && i + 1 < p.Bank.Count ? p.Bank[i + 1] : null;
                ItemView bag = c != null ? c.BagItem : null;
                UiItemButton b = _bags[i];
                Color tint = bought ? Color.white : Unbought;
                if (bag != null)
                {
                    _bagPicture[i] = false;
                    b.Set(bag, 0, tint);
                }
                else
                {
                    if (!_bagPicture[i])
                    {
                        if (empty == null) empty = UiArt.Tex(UiContainers.EmptyBagSlot);   // GetInventorySlotInfo("Bag1..") slot texture
                        b.SetPicture(empty);
                        _bagPicture[i] = true;
                    }
                    if (b.B.Icon.color != tint) b.B.Icon.color = tint;
                }
                b.B.ClickPicksUp = pickUps && bought;
                bool open = bags != null && bags.IsBagShown(UiContainers.BankBagFirstId + i);   // UpdateBagButtonHighlight (:236-250)
                if (_bagOpen[i].enabled != open) _bagOpen[i].enabled = open;
            }
            // UpdateBagSlotStatus (:85-118)
            bool full = p.BankBagSlotsBought >= BagSlots;
            if (_purchase.gameObject.activeSelf == full) _purchase.gameObject.SetActive(!full);
            if (!full)
            {
                _cost.Set(p.NextBankSlotCost);
                _cost.SetColor(p.Money >= p.NextBankSlotCost ? Color.white : UiArt.ErrorRed);   // SetMoneyFrameColor white / red
            }
            _money.Set(p.Money);
        }

        // ------------------------------------------------------------------ the bag slots

        private int InvSlot(int k) { return InvPos.BankBagStart + k; }

        private bool Bought(int k)
        {
            PlayerState p = Ctx.P;
            return p != null && k < p.BankBagSlotsBought;
        }

        private UiPickupPayload BagPayload(int k)
        {
            return Bought(k) ? UiContainers.PayloadAt(Ctx, InvPos.Bag0, InvSlot(k)) : null;   // BankFrameItemButton OnDragStart: PickupBagFromSlot
        }

        private bool AcceptsBagDrop(UiPickupPayload p, int k)
        {
            return p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0 && Bought(k) && !(p.Bag == InvPos.Bag0 && p.Slot == InvSlot(k));
        }

        /// <summary>PutItemInBag(inventoryID) (BankFrameItemButtonBag_OnClick :251-262): a bag onto the slot is equipped there, any other item
        /// goes into that bag.</summary>
        private void DropOnBag(UiPickupPayload p, int k)
        {
            IWowSession s = Ctx.S;
            PlayerState pl = Ctx.P;
            if (s == null || pl == null || p == null || p.Bag < 0) return;
            ItemView held = UiContainers.ItemAt(pl, p.Bag, p.Slot);
            ItemView there = UiContainers.ItemAt(pl, InvPos.Bag0, InvSlot(k));
            if (there == null || (held != null && held.ContainerSlots > 0)) s.MoveItem(p.Bag, p.Slot, InvPos.Bag0, InvSlot(k));
            else s.StoreItemInBag(p.Bag, p.Slot, InvSlot(k));
        }

        private void BagClick(int k)
        {
            if (!Bought(k)) { UiContainers.Note("bank bag slot " + (k + 1) + " is not bought - use Purchase."); return; }
            UiBags b = UiBags.Current;
            if (b != null) b.ToggleBag(UiContainers.BankBagFirstId + k);   // ToggleBag(id) when nothing was put in (:256-262)
        }

        private string BagTip(int k)
        {
            PlayerState p = Ctx.P;
            ItemView bag = UiContainers.ItemAt(p, InvPos.Bag0, InvSlot(k));
            if (bag != null) return UiTooltip.Item(bag, null);
            return Bought(k) ? "<size=14>" + UiGlobalStrings.G("BANK_BAG", "Bag Slot") + "</size>"                     // GlobalStrings.lua:677
                             : "<size=14>" + UiGlobalStrings.G("BANK_BAG_PURCHASE", "Purchasable Bag Slot") + "</size>";   // :678
        }

        private void Purchase()
        {
            PlayerState p = Ctx.P;
            if (p == null || p.BankBagSlotsBought >= BagSlots) return;
            UiPopup.Show(UiNpcPopups.BuyBankSlot(Ctx));   // BankFramePurchaseButton -> StaticPopup_Show("CONFIRM_BUY_BANK_SLOT")
        }
    }
}
