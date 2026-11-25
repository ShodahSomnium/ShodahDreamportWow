// iteration 11 - owner interact (proposal-interact.md 4.2 / 6.3.3): WoW's TradeFrame (FrameXML TradeFrame.xml / TradeFrame.lua), a session
// window of uicore's registry (Wanted while IWowInteractions.Trade.State == Open). Desktop: the mouse (click / drag a bag item onto our
// slot, click our slot to take it back, type the money, Trade / Cancel); VR: the laser / poke, money boxes open the headset keyboard.
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiTrade : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Trade, Frame = "TradeFrame", TitleKey = "TRADE", Title = "Trade",   // GlobalStrings.lua:7639
                Area = UiPanelArea.Left, Pushable = 1, SessionWindow = true, Palm = false,              // UIPanelWindows TradeFrame left, pushable 1 (UIParent.lua:29)
                Wanted = s => s != null && s.Interactions != null && s.Interactions.Trade != null && s.Interactions.Trade.State == TradeState.Open,
                NearGuid = s => s != null && s.Interactions != null && s.Interactions.Trade != null ? s.Interactions.Trade.PartnerGuid : 0,
                Closed = s => { if (s != null && s.Interactions != null) { WowLog.Info("UI: TradeFrame closed by the player - CancelTrade (TradeFrame.lua:156-159)."); s.Interactions.CancelTrade(); } },
                Owner = "interact", Hidden = false,
                Build = (parent, ctx, close) => new UiTrade(parent, ctx, close)
            };
        }

        private const string Art = @"Interface\TradeFrame\";
        private const float RowPitch = 44f;                      // TradeItemTemplate 37 high + 7 (TradeFrame.xml: each row 7 below the last)
        private static readonly float[] RowY = { 104f, 148f, 192f, 236f, 280f, 324f, 389f };   // row 7 (non-traded) 28 below row 6 (TradeFrame.xml:386-391)
        private const float PlayerX = 26f, RecipientX = 195f;    // TradePlayerItem1 / TradeRecipientItem1 TOPLEFT (TradeFrame.xml:411-416, :332-337)

        private sealed class Row
        {
            public UiButton Button;
            public TextMeshProUGUI Name;
            public ItemView Shown;
            public string IconPath;
            public bool IconLoaded;
            public string NameShown;
        }

        private readonly Row[] _mine = new Row[TradeView.Slots], _theirs = new Row[TradeView.Slots];
        private readonly RawImage _myPortrait, _theirPortrait;
        private readonly TextMeshProUGUI _myName, _theirName;
        private readonly RectTransform _myHighlight, _theirHighlight, _myHighlightEnchant, _theirHighlightEnchant;
        private readonly UiMoney _theirMoney;
        private readonly TMP_InputField[] _money = new TMP_InputField[3];
        private readonly UiButton _tradeButton, _cancelButton;
        private readonly Action _close;
        private int _version = -1, _moneyShown = -1;
        private bool _logged;
        private WowKeyboard _keyboard;
        private float _openedAt;

        public UiTrade(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "TradeFrame", 0f, 0f, W, H);
            UiKit.Frame4(Root, "Art", Art + "UI-TradeFrame-TopLeft.blp", Art + "UI-TradeFrame-TopRight.blp", Art + "UI-TradeFrame-BotLeft.blp", Art + "UI-TradeFrame-BotRight.blp");   // TradeFrame.xml:207-237
            // the accept highlights (TradeHighlightTemplate: top 16 / middle / bottom 16 of UI-TradeFrame-Highlight, TradeFrame.xml:4-37, :284-330)
            _myHighlight = Highlight("Highlight Player", 19f, 100f, 266f);
            _theirHighlight = Highlight("Highlight Recipient", 189f, 100f, 266f);
            _myHighlightEnchant = Highlight("Highlight Player Enchant", 19f, 370f, 61f);
            _theirHighlightEnchant = Highlight("Highlight Recipient Enchant", 189f, 370f, 61f);
            // portraits 60 x 60 at (7, 6) / (183, 6) (TradeFrame.xml:181-201) - ours: class circles (the 3D portrait is gap G-T1)
            _myPortrait = UiKit.Pic(Root, "Player Portrait", UiArt.Tex(UiArt.ClassCircles), 7f, 6f, 60f, 60f);
            _theirPortrait = UiKit.Pic(Root, "Recipient Portrait", UiArt.Tex(UiArt.ClassCircles), 183f, 6f, 60f, 60f);
            _theirPortrait.enabled = false;
            _myName = UiKit.Text(Root, "Player Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 75f, 17f, 100f, 12f);       // TradeFramePlayerNameText (:239-250)
            _theirName = UiKit.Text(Root, "Recipient Name", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 245f, 17f, 80f, 12f);  // TradeFrameRecipientNameText (:251-261)
            _myName.textWrappingMode = TextWrappingModes.NoWrap;
            _theirName.textWrappingMode = TextWrappingModes.NoWrap;
            // "Will not be traded" (TRADEFRAME_ENCHANT_SLOT_LABEL GlobalStrings.lua:7640) at (26, 374) and +170 (TradeFrame.xml:263-280)
            UiKit.Text(Root, "Player Enchant Label", "Will not be traded", 10f, Color.white, TextAlignmentOptions.Left, 26f, 374f, 150f, 12f);
            UiKit.Text(Root, "Recipient Enchant Label", "Will not be traded", 10f, Color.white, TextAlignmentOptions.Left, 196f, 374f, 150f, 12f);
            for (int i = 0; i < TradeView.Slots; i++)
            {
                _mine[i] = MakeRow(PlayerX, RowY[i], i, true);
                _theirs[i] = MakeRow(RecipientX, RowY[i], i, false);
            }
            // our money: MoneyInputFrameTemplate at (26, 73) (TradeFrame.xml:548-560) - ours: three digit boxes gold / silver / copper
            Texture2D coins = UiArt.Tex(UiArt.MoneyIcons);
            float[] boxX = { 26f, 82f, 120f };
            float[] boxW = { 40f, 24f, 24f };
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                _money[k] = WowTextField.Create(Root, "Money " + k, boxX[k], 73f, boxW[k], 16f, 10f, "", false, k == 0 ? 4 : 2);
                _money[k].contentType = TMP_InputField.ContentType.IntegerNumber;
                _money[k].onEndEdit.AddListener(_ => SendMoney());
                _money[k].gameObject.SetActive(true);
                UiKit.Pic(Root, "Coin " + k, coins, k * 0.25f, k * 0.25f + 0.25f, 0f, 1f, boxX[k] + boxW[k] + 1f, 74f, 13f, 13f);   // UI-MoneyIcons (MoneyFrame.xml)
                // VR: a poke on the box opens the headset keyboard (the VR chat's path, UiChat.OpenKeyboard)
                UiButton vrBox = UiKit.Button(Root, "Money Box " + k, boxX[k], 73f, boxW[k], 16f);
                vrBox.Click = () => { if (Ctx != null && Ctx.Vr) OpenKeyboard(kk); };
                vrBox.gameObject.SetActive(false);
                _vrBoxes[k] = vrBox;
            }
            _theirMoney = new UiMoney(Root, 384f - 40f, 78f, Color.white);   // TradeRecipientMoneyFrame TOPRIGHT (-40, -78) (TradeFrame.xml:561-571)
            // Trade 85 x 22 at BOTTOMRIGHT (-113, 55), Cancel 77 x 22 right of it +3 (TradeFrame.xml:501-545)
            _tradeButton = UiKit.PanelButton(Root, "Trade", 384f - 113f - 85f, 512f - 55f - 22f, 85f, 22f, OnTrade);      // TRADE (GlobalStrings.lua:7639)
            _cancelButton = UiKit.PanelButton(Root, "Cancel", 384f - 113f + 3f, 512f - 55f - 22f, 77f, 22f, OnCancel);    // CANCEL (:1338)
            UiKit.CloseButton(Root, 384f - 25f - 32f, 8f, () => { WowLog.Info("UI: TradeFrame close button."); if (_close != null) _close(); });   // TradeFrameCloseButton TOPRIGHT (-25, -8)
            Root.gameObject.SetActive(false);
        }

        private readonly UiButton[] _vrBoxes = new UiButton[3];

        private RectTransform Highlight(string name, float x, float y, float h)
        {
            RectTransform r = UiKit.Rect(Root, name, x, y, 161f, h);
            Texture2D t = UiArt.Tex(Art + "UI-TradeFrame-Highlight.blp");
            UiKit.Pic(r, "Top", t, 0f, 0.62890625f, 0f, 0.0625f, 0f, 0f, 161f, 16f);
            UiKit.Pic(r, "Middle", t, 0f, 0.62890625f, 0.0625f, 0.9375f, 0f, 16f, 161f, Mathf.Max(0f, h - 32f));
            UiKit.Pic(r, "Bottom", t, 0f, 0.62890625f, 0.9375f, 1f, 0f, h - 16f, 161f, 16f);
            r.gameObject.SetActive(false);
            return r;
        }

        private Row MakeRow(float x, float y, int index, bool mine)
        {
            var row = new Row();
            UiKit.Pic(Root, (mine ? "Player" : "Recipient") + " Slot " + index, UiArt.Tex(@"Interface\Buttons\UI-EmptySlot.blp"), x - 13f, y - 13f, 64f, 64f);   // TradeItemTemplate (:38-82)
            if (index == TradeView.Slots - 1) UiKit.Pic(Root, "Enchant Icon", UiArt.Tex(Art + "UI-TradeFrame-EnchantIcon.blp"), x, y, 62f, 62f);              // row 7 (:394-408)
            UiKit.Pic(Root, "Name Frame " + index, UiArt.Tex(@"Interface\QuestFrame\UI-QuestItemNameFrame.blp"), x - 13f + 64f - 20f, y - 13f, 124f, 64f);
            row.Name = UiKit.Text(Root, "Name " + index, "", 10f, UiArt.Gold, TextAlignmentOptions.Left, x - 13f + 64f - 20f + 15f, y + 3f, 90f, 30f);
            UiButton b = UiKit.Slot(Root, (mine ? "Player Item " : "Recipient Item ") + (index + 1), x, y, 37f, false);
            b.Icon.enabled = false;
            b.Count = UiKit.Text(b.transform, "Count", "", 11f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, 35f, 35f, UiArt.Numbers);
            int k = index;
            if (mine)
            {
                b.Click = () => MyClick(k);
                b.RightClick = () => MyClick(k);
                b.Tip = () => _mine[k].Shown != null ? UiTooltip.Item(_mine[k].Shown, null) : null;
                // uicore H12: a bag item dragged here is offered in this slot (WoW's TradeFrame_OnMouseUp / PickupTradeItem)
                UiKit.DropTarget(b, p => p != null && p.Kind == UiPickupKind.Item && p.Bag >= 0 && p.Slot >= 0, p => Offer(k, p.Bag, p.Slot));
            }
            else
            {
                b.Click = () => TheirClick(k);
                b.Tip = () => TheirTip(k);
            }
            row.Button = b;
            return row;
        }

        // ------------------------------------------------------------------ clicks

        private IWowInteractions Ix { get { return Ctx != null && Ctx.S != null ? Ctx.S.Interactions : null; } }

        /// <summary>Our slot: a held bag item (uicore's cursor) is placed; a filled slot is taken back (ClearTradeItem - ours: WoW puts it on the cursor).</summary>
        private void MyClick(int slot)
        {
            IWowInteractions ix = Ix;
            if (ix == null) return;
            UiPickupPayload held = UiPickup.Current;
            if (held != null && held.Kind == UiPickupKind.Item && held.Bag >= 0 && held.Slot >= 0)
            {
                Offer(slot, held.Bag, held.Slot);
                UiPickup.Clear("placed in the trade window");
                return;
            }
            if (ix.Trade.Mine[slot] != null)
            {
                WowLog.Info("UI: TradeFrame - our slot " + (slot + 1) + " taken back.");
                ix.ClearTradeItem(slot);
            }
            else Say("Right-click (or tap) an item in your bags to offer it.");   // ours
        }

        private void Offer(int slot, int bag, int bagSlot)
        {
            IWowInteractions ix = Ix;
            if (ix == null) return;
            WowLog.Info("UI: TradeFrame - offering the item at bag " + bag + " slot " + bagSlot + " in trade slot " + (slot + 1) + ".");
            ix.SetTradeItem(slot, bag, bagSlot);
        }

        /// <summary>The partner's non-traded slot (TradeRecipientItem7): spellfx's item targeting (enchanting their item) confirms here.</summary>
        private void TheirClick(int slot)
        {
            IWowSession s = Ctx != null ? Ctx.S : null;
            if (s == null || slot != TradeView.Slots - 1) return;
            if (s.Player != null && s.Player.Targeting != null)
            {
                WowLog.Info("UI: TradeFrame - the partner's non-traded slot picked for the spell being targeted (ConfirmTargetingTrade).");
                s.ConfirmTargetingTrade();
            }
        }

        private string TheirTip(int slot)
        {
            IWowInteractions ix = Ix;
            ItemView it = ix != null ? ix.Trade.Theirs[slot] : null;
            if (it == null) return null;
            string ench = ix.Trade.TheirEnchant[slot];
            return UiTooltip.Item(it, ench != null ? "<color=#1eff00>Enchanted: " + ench + "</color>" : null);
        }

        private void OnTrade()
        {
            IWowInteractions ix = Ix;
            if (ix == null) return;
            SendMoney();
            WowLog.Info("UI: TradeFrame - Trade (AcceptTrade).");
            ix.AcceptTrade();
        }

        /// <summary>TradeFrameCancelButton (TradeFrame.lua:148-154): accepted -> CancelTradeAccept, else close (CloseTrade).</summary>
        private void OnCancel()
        {
            IWowInteractions ix = Ix;
            if (ix == null) return;
            if (ix.Trade.MyAccepted) { WowLog.Info("UI: TradeFrame - Cancel (taking the accept back)."); ix.UnacceptTrade(); return; }
            WowLog.Info("UI: TradeFrame - Cancel (closing).");
            if (_close != null) _close();
            else ix.CancelTrade();
        }

        private void SendMoney()
        {
            IWowInteractions ix = Ix;
            if (ix == null || ix.Trade.State != TradeState.Open) return;
            int g = Parse(_money[0]), s = Parse(_money[1]), c = Parse(_money[2]);
            int total = Mathf.Clamp(g, 0, 9999) * 10000 + Mathf.Clamp(s, 0, 99) * 100 + Mathf.Clamp(c, 0, 99);   // TradeFrame_UpdateMoney clamps to our money (TradeFrame.lua:174-186)
            PlayerState p = Ctx.P;
            if (p != null && total > p.Money) total = p.Money;
            if (total != ix.Trade.MyMoney) ix.SetTradeMoney(total);
            ShowMoney(total);
        }

        private static int Parse(TMP_InputField f)
        {
            int v;
            return f != null && int.TryParse(f.text, out v) ? Mathf.Max(0, v) : 0;
        }

        private void ShowMoney(int copper)
        {
            _moneyShown = copper;
            for (int k = 0; k < 3; k++)
            {
                int v = k == 0 ? copper / 10000 : k == 1 ? copper / 100 % 100 : copper % 100;
                if (_money[k] != null && !_money[k].isFocused) _money[k].SetTextWithoutNotify(v > 0 ? v.ToString() : "");
            }
        }

        private void OpenKeyboard(int k)
        {
            if (_money[k] == null) return;
            if (_keyboard == null) _keyboard = WowKeyboard.Create(Root.root);
            _keyboard.Open(_money[k], k == 0 ? "Gold" : k == 1 ? "Silver" : "Copper", false, SendMoney);   // ours
        }

        private void Say(string text)
        {
            if (Ctx != null) Ctx.Say(text, UiArt.InfoYellow);
        }

        // ------------------------------------------------------------------ refresh

        protected override void OnOpen()
        {
            _version = -1;
            _moneyShown = -1;
            _openedAt = Time.unscaledTime;
            for (int k = 0; k < 3; k++) if (_money[k] != null) _money[k].SetTextWithoutNotify("");
            IWowInteractions ix = Ix;
            WowLog.Info("UI: TradeFrame opened (trading with " + (ix != null ? ix.Trade.PartnerName ?? "?" : "?") + (Ctx != null && Ctx.Vr ? ", VR" : ", desktop") + ").");
        }

        protected override void OnClose()
        {
            if (_keyboard != null && _keyboard.IsOpen) _keyboard.Close();
        }

        public override void Refresh(float now)
        {
            IWowInteractions ix = Ix;
            PlayerState p = Ctx != null ? Ctx.P : null;
            if (ix == null || p == null) return;
            TradeView t = ix.Trade;
            bool vr = Ctx.Vr;
            for (int k = 0; k < 3; k++) if (_vrBoxes[k] != null && _vrBoxes[k].gameObject.activeSelf != vr) _vrBoxes[k].gameObject.SetActive(vr);
            // rows: icons load over a few frames, names arrive with the item answers - polled every frame (cheap)
            for (int i = 0; i < TradeView.Slots; i++)
            {
                FillRow(_mine[i], t.Mine[i]);
                FillRow(_theirs[i], t.Theirs[i]);
            }
            if (t.Version == _version) return;
            _version = t.Version;
            if (!_logged) { _logged = true; WowLog.Info("UI: TradeFrame first refresh (7 slots a side, money boxes, Trade / Cancel)."); }
            _myName.text = p.Name ?? "";
            _theirName.text = t.PartnerName ?? "";
            Vector4 c = UiArt.ClassCoords(p.Class);
            UiKit.Coords(_myPortrait, c.x, c.y, c.z, c.w);
            WowClass theirClass = PartnerClass(t.PartnerGuid);
            _theirPortrait.enabled = theirClass != 0;
            if (theirClass != 0) { Vector4 tc = UiArt.ClassCoords(theirClass); UiKit.Coords(_theirPortrait, tc.x, tc.y, tc.z, tc.w); }
            _theirMoney.Set(t.TheirMoney);
            if (t.MyMoney != _moneyShown) ShowMoney(t.MyMoney);
            // TradeFrame_SetAcceptState (TradeFrame.lua:128-146): the highlight of the side that accepted
            _myHighlight.gameObject.SetActive(t.MyAccepted);
            _myHighlightEnchant.gameObject.SetActive(t.MyAccepted);
            _theirHighlight.gameObject.SetActive(t.TheirAccepted);
            _theirHighlightEnchant.gameObject.SetActive(t.TheirAccepted);
            _tradeButton.Interactable = !t.MyAccepted;
        }

        private void FillRow(Row r, ItemView it)
        {
            UiButton b = r.Button;
            if (!ReferenceEquals(it, r.Shown))
            {
                r.Shown = it;
                r.IconLoaded = false;
                r.IconPath = null;
                r.NameShown = null;
                b.Icon.enabled = false;
                r.Name.text = "";
                if (b.Count != null) b.Count.text = it != null && it.Count > 1 ? it.Count.ToString() : "";
            }
            if (it == null) return;
            if (!r.IconLoaded && !string.IsNullOrEmpty(it.IconPath)) SetIcon(b, it.IconPath, ref r.IconPath, ref r.IconLoaded);
            if (!ReferenceEquals(r.NameShown, it.Name))
            {
                r.NameShown = it.Name;
                r.Name.text = it.Name != null ? "<color=" + UiArt.QualityHex(it.Quality) + ">" + it.Name + "</color>" : "";
                if (b.Count != null) b.Count.text = it.Count > 1 ? it.Count.ToString() : "";
            }
        }

        /// <summary>The partner's class for the portrait circle: a group member's, else the online session's object (ours: null offline).</summary>
        private WowClass PartnerClass(ulong guid)
        {
            IWowSession s = Ctx != null ? Ctx.S : null;
            if (s == null || guid == 0) return 0;
            GroupMemberView m = UiUnits.GroupMember(s, guid);
            if (m != null && m.Class != 0) return m.Class;
            var online = s as SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession;
            return online != null ? online.IxClassOf(guid) : 0;
        }
    }
}
