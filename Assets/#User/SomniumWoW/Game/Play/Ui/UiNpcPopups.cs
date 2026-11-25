// iteration 11 - owner npcui (proposal-npc.md 6.1 / 6.3; the wave-0 stub by w0game, filled by npcui)
// The StaticPopup specs of the NPC services (uicore's UiPopup draws them): CONFIRM_BINDER, CONFIRM_BUY_BANK_SLOT, CONFIRM_HIGH_COST_ITEM,
// CONFIRM_PURCHASE_TOKEN_ITEM, GOSSIP_CONFIRM, GOSSIP_ENTER_CODE - texts / buttons / flags from StaticPopup.lua of the user's install
// (harness dump P/harness/out_d11npc/sp.txt, line per popup) and GlobalStrings.lua via UiGlobalStrings (the user's locale). Also the
// innkeeper question (Player.PendingBinderGuid has no event: polled by one small ticker under the UI root) and the HomeBound line.
// quests' UiQuestDialog shows EnterCode / GossipConfirm for the gossip options (proposal-npc.md H8).
using System;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public static class UiNpcPopups
    {
        public const int QualityUncommon = 2;   // ITEM_QUALITY_UNCOMMON (MerchantFrame.lua:497 compares the cost items' quality with it)
        private static bool _subscribed, _logged;
        private static ulong _binderAsked;
        private static int _tickFrame = -1;

        private static string Yes { get { return UiGlobalStrings.G("YES", "Yes"); } }          // GlobalStrings.lua:8982
        private static string No { get { return UiGlobalStrings.G("NO", "No"); } }             // :5181
        private static string Accept { get { return UiGlobalStrings.G("ACCEPT", "Accept"); } } // :11
        private static string Cancel { get { return UiGlobalStrings.G("CANCEL", "Cancel"); } } // :1338

        // ------------------------------------------------------------------ gossip (quests' UiQuestDialog calls these)

        /// <summary>GOSSIP_ENTER_CODE (StaticPopup.lua:2595-2622): ENTER_CODE "Please enter code:", an edit box, Accept / Cancel, exclusive,
        /// hideOnEscape -> SelectGossipOption(index, code).</summary>
        public static UiPopupSpec EnterCode(UiContext ctx, int index)
        {
            return new UiPopupSpec
            {
                Which = "GOSSIP_ENTER_CODE", Text = UiGlobalStrings.G("ENTER_CODE", "Please enter code:"),   // GlobalStrings.lua:2822
                Button1 = Accept, Button2 = Cancel, HasEditBox = true, Exclusive = true, HideOnEscape = true, EnterClicksFirstButton = true,
                OnAcceptText = code =>
                {
                    IWowSession s = Session(ctx);
                    if (s != null) s.SelectGossipOption(index, code ?? "");
                },
                Data = index
            };
        }

        /// <summary>GOSSIP_CONFIRM (StaticPopup.lua:2583-2594): text "%s" = the option's box text, a money frame (the option's cost),
        /// Accept / Cancel, exclusive, hideOnEscape -> SelectGossipOption(index).</summary>
        public static UiPopupSpec GossipConfirm(UiContext ctx, int index, string text, int money)
        {
            return new UiPopupSpec
            {
                Which = "GOSSIP_CONFIRM", Text = text ?? "", Button1 = Accept, Button2 = Cancel, Exclusive = true, HideOnEscape = true,
                Money = money > 0 ? money : -1,
                OnAccept = () =>
                {
                    IWowSession s = Session(ctx);
                    if (s != null) s.SelectGossipOption(index, null);
                },
                Data = index
            };
        }

        // ------------------------------------------------------------------ merchant

        /// <summary>MerchantFrame_ConfirmExtendedItemCost (MerchantFrame.lua:454-509): honor / arena points always ask; cost items ask when
        /// one is better than uncommon. Ours: the cost items' quality is not known to the client here (Item.dbc has no quality), so any cost
        /// item asks too (logged once).</summary>
        public static bool NeedsTokenConfirm(UiContext ctx, MerchantItemView row)
        {
            if (row == null) return false;
            bool items = false;
            if (row.CostItems != null) for (int i = 0; i < row.CostItems.Length; i++) items |= row.CostItems[i] > 0;
            if (items) Once("a purchase paid with items always asks CONFIRM_PURCHASE_TOKEN_ITEM (ours: WoW asks only above uncommon quality - not known here).");
            return row.HonorCost > 0 || row.ArenaCost > 0 || items;
        }

        /// <summary>CONFIRM_PURCHASE_TOKEN_ITEM (StaticPopup.lua:117-133): CONFIRM_PURCHASE_TOKEN_ITEM "Are you sure you wish to exchange %s for
        /// the following item?" with the costs (MERCHANT_HONOR_POINTS / MERCHANT_ARENA_POINTS / ITEM_QUANTITY_TEMPLATE joined by ", "), the
        /// item frame, Yes / No -> BuyMerchantItem(index, count).</summary>
        public static UiPopupSpec PurchaseToken(UiContext ctx, int index, int purchases, int bag, int slot)
        {
            IWowSession s = Session(ctx);
            MerchantItemView row = Row(s, index);
            var sb = new StringBuilder();
            if (row != null)
            {
                int q = Math.Max(1, purchases);
                if (row.HonorCost > 0) sb.Append(UiGlobalStrings.FormatOr("MERCHANT_HONOR_POINTS", "%d Honor Points", row.HonorCost * q));   // GlobalStrings.lua:4924
                if (row.ArenaCost > 0)
                {
                    if (sb.Length > 0) sb.Append("  ");                                                                                  // MerchantFrame.lua:471-472
                    sb.Append(UiGlobalStrings.FormatOr("MERCHANT_ARENA_POINTS", "%d Arena Points", row.ArenaCost * q));                  // :4922
                }
                if (row.CostItems != null)
                    for (int i = 0; i < row.CostItems.Length; i++)
                    {
                        int n = row.CostCounts != null && i < row.CostCounts.Length ? row.CostCounts[i] : 0;
                        if (row.CostItems[i] <= 0 || n <= 0) continue;
                        if (sb.Length > 0) sb.Append(", ");                                                                               // LIST_DELIMITER (:452)
                        sb.Append(UiGlobalStrings.FormatOr("ITEM_QUANTITY_TEMPLATE", "%d %s", n * q, ItemName(s, row.CostItems[i])));     // GlobalStrings.lua:4498
                    }
            }
            return new UiPopupSpec
            {
                Which = "CONFIRM_PURCHASE_TOKEN_ITEM",
                Text = UiGlobalStrings.FormatOr("CONFIRM_PURCHASE_TOKEN_ITEM", "Are you sure you wish to exchange %s for the following item?", sb.ToString()),   // :1822
                Button1 = Yes, Button2 = No, HideOnEscape = true, Item = row != null ? row.Item : null,
                OnAccept = () => Buy(ctx, index, purchases, bag, slot)
            };
        }

        /// <summary>CONFIRM_HIGH_COST_ITEM (StaticPopup.lua:237-252): "Are you sure you wish to purchase %s for the following amount?" (the
        /// item link), a money frame = price x count, Yes / No -> BuyMerchantItem.</summary>
        public static UiPopupSpec HighCost(UiContext ctx, int index, int purchases, int bag, int slot)
        {
            IWowSession s = Session(ctx);
            MerchantItemView row = Row(s, index);
            string link = row != null && row.Item != null ? "<color=" + UiArt.QualityHex(row.Item.Quality) + ">[" + row.Item.Name + "]</color>" : "?";
            return new UiPopupSpec
            {
                Which = "CONFIRM_HIGH_COST_ITEM",
                Text = UiGlobalStrings.FormatOr("CONFIRM_HIGH_COST_ITEM", "Are you sure you wish to purchase %s for the following amount?", link),   // GlobalStrings.lua:1815
                Button1 = Yes, Button2 = No, HideOnEscape = true, Money = row != null ? row.Price * Math.Max(1, purchases) : -1,
                OnAccept = () => Buy(ctx, index, purchases, bag, slot)
            };
        }

        private static void Buy(UiContext ctx, int index, int purchases, int bag, int slot)
        {
            IWowSession s = Session(ctx);
            if (s == null || s.CurrentMerchant == null) return;
            s.BuyItem(index, purchases, bag, slot);
        }

        // ------------------------------------------------------------------ bank, innkeeper

        /// <summary>CONFIRM_BUY_BANK_SLOT (StaticPopup.lua:437-448): "Do you want to purchase a bank slot for:" + the money frame (the next
        /// slot's cost), Yes / No -> PurchaseSlot().</summary>
        public static UiPopupSpec BuyBankSlot(UiContext ctx)
        {
            IWowSession s = Session(ctx);
            PlayerState p = s != null ? s.Player : null;
            return new UiPopupSpec
            {
                Which = "CONFIRM_BUY_BANK_SLOT", Text = UiGlobalStrings.G("CONFIRM_BUY_BANK_SLOT", "Do you want to purchase a bank slot for:"),   // GlobalStrings.lua:1803
                Button1 = Yes, Button2 = No, HideOnEscape = true, Money = p != null ? p.NextBankSlotCost : -1,
                OnAccept = () =>
                {
                    IWowSession now = Session(ctx);
                    if (now != null && now.BankerGuid != 0) now.BuyBankSlot();
                }
            };
        }

        /// <summary>CONFIRM_BINDER (StaticPopup.lua:2519-2531): "Do you want to make %s your new home?", Accept / Cancel, hidden when
        /// CheckBinderDist() fails (the session clears PendingBinderGuid out of reach) -> ConfirmBinder().</summary>
        public static UiPopupSpec Binder(IWowSession s, string place)
        {
            return new UiPopupSpec
            {
                Which = "CONFIRM_BINDER",
                Text = UiGlobalStrings.FormatOr("CONFIRM_BINDER", "Do you want to make %s your new home?", place ?? "this place"),   // GlobalStrings.lua:1802
                Button1 = Accept, Button2 = Cancel, HideOnEscape = true,
                KeepOpen = () => { IWowSession now = WowPlay.Session; return now != null && now.Player != null && now.Player.PendingBinderGuid != 0; },
                OnAccept = () => { IWowSession now = WowPlay.Session; if (now != null) now.ConfirmBinder(true); },
                OnCancel = () => { IWowSession now = WowPlay.Session; if (now != null && now.Player != null && now.Player.PendingBinderGuid != 0) now.ConfirmBinder(false); }
            };
        }

        // ------------------------------------------------------------------ the poll + events

        /// <summary>Called by each UiBags (both layouts): one ticker under the UI root polls the innkeeper question; the HomeBound line is shown once
        /// per event (ERR_DEATHBIND_SUCCESS_S, GlobalStrings.lua:3021).</summary>
        public static void Hook(UiContext ctx, Transform host)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                WowPlay.Event += OnGameEvent;
            }
            // under the top of the UI's hierarchy: the bags' own holder is hidden with the VR arm window, the question must still come
            if (host == null) return;
            host = host.root;
            if (host.Find("UiNpcPopups") != null) return;
            var go = new GameObject("UiNpcPopups");
            go.transform.SetParent(host, false);
            go.AddComponent<UiNpcPopupsTicker>();
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("UI NPC popups (npcui): CONFIRM_BINDER / CONFIRM_BUY_BANK_SLOT / CONFIRM_HIGH_COST_ITEM / CONFIRM_PURCHASE_TOKEN_ITEM / GOSSIP_CONFIRM / " +
                            "GOSSIP_ENTER_CODE (StaticPopup.lua); ours: the binder question polled every frame, the money text in tooltips (g / s / c colours).");
            }
        }

        /// <summary>Every frame (once, whichever layout ticks first): shows CONFIRM_BINDER when the session asks (SMSG_BINDER_CONFIRM).</summary>
        internal static void Tick()
        {
            if (_tickFrame == Time.frameCount) return;
            _tickFrame = Time.frameCount;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            ulong g = p != null ? p.PendingBinderGuid : 0UL;
            if (g == 0) { _binderAsked = 0; return; }
            if (g == _binderAsked) return;
            _binderAsked = g;
            UiPopup.Show(Binder(s, p.PendingBinderPlace));
        }

        private static void OnGameEvent(GameEvent e)
        {
            if (e.Type != GameEventType.HomeBound) return;
            string text = UiGlobalStrings.FormatOr("ERR_DEATHBIND_SUCCESS_S", "%s is now your home.", e.Text ?? "?");   // GlobalStrings.lua:3021
            WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = text });
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>printf of one number into a GlobalStrings text ("(%d)").</summary>
        public static string Fmt(string format, int value)
        {
            return UiGlobalStrings.FormatText(format ?? "%d", value);
        }

        /// <summary>Money as tooltip text (SetTooltipMoney draws coins; ours: numbers with g / s / c in the coin colours).</summary>
        public static string MoneyText(int copper)
        {
            if (copper < 0) copper = 0;
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var sb = new StringBuilder();
            if (g > 0) sb.Append(g).Append("<color=#ffd100>g</color> ");
            if (s > 0 || g > 0) sb.Append(s).Append("<color=#c7c7cf>s</color> ");
            sb.Append(c).Append("<color=#eda55f>c</color>");
            return sb.ToString();
        }

        private static IWowSession Session(UiContext ctx)
        {
            return ctx != null && ctx.S != null ? ctx.S : WowPlay.Session;
        }

        private static MerchantItemView Row(IWowSession s, int index)
        {
            MerchantView m = s != null ? s.CurrentMerchant : null;
            return m != null && index >= 0 && index < m.Items.Count ? m.Items[index] : null;
        }

        /// <summary>The name of a cost item: the one in the bags (badges / marks are carried), else "item #entry".</summary>
        private static string ItemName(IWowSession s, int entry)
        {
            PlayerState p = s != null ? s.Player : null;
            if (p != null && p.Bags != null)
                for (int i = 0; i < p.Bags.Count; i++)
                {
                    ItemView it = p.Bags[i];
                    if (it != null && it.Entry == entry && !string.IsNullOrEmpty(it.Name)) return "<color=" + UiArt.QualityHex(it.Quality) + ">[" + it.Name + "]</color>";
                }
            return "item #" + entry;   // ours: not in the bags, the name is not known here
        }

        private static readonly System.Collections.Generic.HashSet<string> Said = new System.Collections.Generic.HashSet<string>();

        private static void Once(string text)
        {
            if (Said.Count < 50 && Said.Add(text)) WowLog.Info("UI NPC popups: " + text);
        }
    }

    /// <summary>The innkeeper poll (UiNpcPopups.Tick) - lives under the UI root, so it stops with the UI.</summary>
    public sealed class UiNpcPopupsTicker : MonoBehaviour
    {
        private void Update()
        {
            try { UiNpcPopups.Tick(); }
            catch (Exception e) { WowLog.Error("UI NPC popups: tick failed: " + e); enabled = false; }
        }
    }
}
