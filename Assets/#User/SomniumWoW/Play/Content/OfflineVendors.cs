// iteration 11 - owner npc (proposal-npc.md s7, decision U2): the offline vendors - OURS, no game data. Which existing offline NPC sells,
// the option text, what it sells (the zone's own food / drink / potion ItemDefs) and the price rule are our own choices (logged once by
// OfflineSession.Vendor.cs). The offline zones have no armorer, banker or innkeeper (README gap G8).
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static class OfflineVendors
    {
        /// <summary>The vendor option of the NPC's dialog (ours; WoW's "I want to browse your goods" is server data, gossip_menu_option).</summary>
        public const string OptionText = "Let me see what you have for sale.";
        public const int OptionIndex = 1;                // ours: the option's id in the offline dialog
        public const int OptionIcon = 1;                 // GOSSIP_ICON_VENDOR (GossipDef.h:61-71) -> Interface\GossipFrame\VendorGossipIcon
        public const int PriceFactor = 25, MinPrice = 5; // ours: copper per item = max(MinPrice, PriceFactor x the item's sell price)

        /// <summary>One NPC per start zone (ours, proposal-npc.md s7): the zone's steward / quartermaster / apothecary-like NPC.</summary>
        static readonly int[] Entries =
        {
            Northshire.BrotherAldric,          // Aldric Venn, Abbey Steward
            ColdridgeValley.QuartermasterSenna, // Senna Copperkettle, Anvilmar Quartermaster
            Deathknell.ApothecaryMerrow,       // Merrow Vantis, Apothecary
            Shadowglen.TenderAravel,           // Aravel Silverpool
            AmmenVale.BotanistNuraeli,         // Botanist Nuraeli
            CampNarache.KeeperAponi,           // Aponi Mistcaller, Keeper of the Pyres
            SunstriderIsle.ArcanistVeyselle,   // Arcanist Veyselle
            ValleyOfTrials.ElderGarosh,        // Elder Garosh
        };

        /// <summary>True when this creature entry sells offline.</summary>
        public static bool Sells(int creatureEntry)
        {
            return Array.IndexOf(Entries, creatureEntry) >= 0;
        }

        /// <summary>What an offline vendor sells: the zone's consumables (food, drink, potions - HealAmount or ManaAmount), never quest items.</summary>
        public static bool ForSale(ItemDef d)
        {
            return d != null && d.Entry > 0 && d.QuestId == 0 && (d.HealAmount > 0 || d.ManaAmount > 0);
        }

        /// <summary>Copper per item (ours).</summary>
        public static int PriceOf(ItemDef d)
        {
            return Math.Max(MinPrice, PriceFactor * Math.Max(0, d != null ? d.SellPrice : 0));
        }

        /// <summary>The zone's items for sale, in the zone file's order (each entry once).</summary>
        public static List<ItemDef> Stock(ZoneContent zone)
        {
            var list = new List<ItemDef>();
            if (zone == null) return list;
            foreach (var d in zone.Items)
            {
                if (!ForSale(d)) continue;
                bool dup = false;
                foreach (var x in list) if (x.Entry == d.Entry) { dup = true; break; }
                if (!dup) list.Add(d);
            }
            return list;
        }

        /// <summary>For the log line: "zone: N items at our prices".</summary>
        public static string Describe(ZoneContent zone)
        {
            var s = Stock(zone);
            var parts = new List<string>();
            foreach (var d in s) parts.Add(d.Name + " " + PriceOf(d) + "c");
            return (zone != null ? zone.Name : "?") + ": " + s.Count + " items (" + string.Join(", ", parts.ToArray()) + ")";
        }
    }
}
