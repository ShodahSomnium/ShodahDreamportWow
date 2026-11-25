using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Night Elf start zone, Shadowglen (Teldrassil, map 1 Kalimdor), as offline zone data for the local simulation.
    /// Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures, icons and
    /// item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All names of NPCs and
    /// items, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports2/content.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 4 (classes 1/3/4/5/11): map 1 (10311.3, 832.463, 1326.41) o 5.69632.
    /// - Graveyard = WorldSafeLocs.dbc id 93 "Teldrassil, Aldrassil" (10384.81, 811.5312, 1317.5382).
    /// - Every spawn/NPC position was picked by the harness probe ContentPlace on the real terrain: z = the ADT terrain height at
    ///   (x, y), area 188 Shadowglen (NPCs may also stand in 256 Aldrassil, the tree's subzone inside the bowl), no water, slope
    ///   &lt;= 30 degrees, no WMO/M2 collision geometry within 1-1.5 yards (Aldrassil's trunk, trees, rocks, lamps), and each wander
    ///   circle checked the same way. The probe ContentCheck re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 4 (what AzerothCore's Player::Create equips),
    ///   minus the hearthstone (6948, no inn offline) and the hunter's arrows (2512, ammo is not simulated).
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (Tiger, Grell, MineSpider, Satyr, Stag models;
    ///   Night Elf NPC displays = CreatureDisplayInfoExtra race 4 with an existing baked texture); weapons = ItemDisplayInfo ids
    ///   whose model exists under Item\ObjectComponents\Weapon.
    /// </summary>
    public static partial class Shadowglen
    {
        public const string ZoneName = "Shadowglen";
        public const int MapKalimdor = 1;

        /// <summary>AreaTable ids: 188 Shadowglen (parent 141 Teldrassil), 256 Aldrassil (the great tree's subzone inside the bowl).</summary>
        public const int AreaShadowglen = 188, AreaAldrassil = 256, AreaTeldrassil = 141;
        public static readonly int[] AreaIds = { AreaShadowglen, AreaAldrassil };

        // ---- creature entries (ours) ----
        public const int YoungNightsaber = 901001;
        public const int MangyNightsaber = 901002;
        public const int Nightsaber = 901003;
        public const int Grellkin = 901004;
        public const int Grell = 901005;
        public const int WebwoodSpider = 901006;
        public const int SythrelTheDefiler = 901007;
        public const int ShadowglenStag = 901008;

        // ---- NPC entries (ours) ----
        public const int KeeperLorellen = 902001;     // main quest giver at the start (intro, class quests, moonwell, final)
        public const int WardenFaelos = 902002;       // nightsaber quests
        public const int TenderAravel = 902004;       // at the moonwell (north)
        public const int ScoutVeyra = 902005;         // the western glade (grell)
        public const int WarriorTrainer = 902011;
        public const int HunterTrainer = 902012;
        public const int RogueTrainer = 902013;
        public const int PriestTrainer = 902014;
        public const int DruidTrainer = 902015;
        public const int SentinelAyrith = 902021;
        public const int SentinelMaelis = 902022;
        public const int SentinelTaelyn = 902023;

        // ---- quest ids (ours) ----
        public const int QuestWakingGlen = 90001;
        public const int QuestClawsInTheGlen = 90002;
        public const int QuestMangyPelts = 90003;
        public const int QuestMoonwellsLight = 90004;
        public const int QuestVenomInTheWater = 90005;
        public const int QuestWordToTheGladeWatch = 90006;
        public const int QuestFelStirring = 90007;
        public const int QuestTaintedCharms = 90008;
        public const int QuestDefilerOfTheGlade = 90009;
        public const int QuestWarriorPath = 90011;
        public const int QuestHunterPath = 90012;
        public const int QuestRoguePath = 90013;
        public const int QuestPriestPath = 90014;
        public const int QuestDruidPath = 90015;

        // ---- invented quest items (>= 900000) ----
        public const int ItemMangyPelt = 900001;
        public const int ItemVenomSac = 900002;
        public const int ItemFelCharm = 900003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapKalimdor,
                PlayerStart = new WowPos(10311.3f, 832.463f, 1326.41f, 5.69632f),
                Graveyard = new WowPos(10384.81f, 811.5312f, 1317.5382f, 5.69632f),
            };
            AddItems(z);
            AddCreatures(z);
            AddNpcs(z);
            AddSpawns(z);
            AddQuests(z);
            AddClassStarts(z);
            return z;
        }

        // ------------------------------------------------------------------------------------------------------------
        // Items. Entry = the real Item.dbc id (display id, inventory type and sheath type below are the Item.dbc values,
        // re-checked by the ContentCheck probe) with our own name and simple stats; icons = ItemDisplayInfo.InventoryIcon.
        // ------------------------------------------------------------------------------------------------------------

        static ItemDef Weapon(int entry, string name, string icon, int display, int quality, int invType, int sheath, float min, float max, float speed, int sell)
        {
            return new ItemDef { Entry = entry, Name = name, IconPath = @"Interface\Icons\" + icon, DisplayId = display, Quality = quality, InventoryType = invType, SheatheType = sheath, MinDamage = min, MaxDamage = max, Speed = speed, SellPrice = sell };
        }

        static ItemDef Armor(int entry, string name, string icon, int display, int quality, int invType, int armor, int sell)
        {
            return new ItemDef { Entry = entry, Name = name, IconPath = @"Interface\Icons\" + icon, DisplayId = display, Quality = quality, InventoryType = invType, Armor = armor, SellPrice = sell };
        }

        static ItemDef QuestItem(int entry, string name, string icon, int questId)
        {
            return new ItemDef { Entry = entry, Name = name, IconPath = @"Interface\Icons\" + icon, DisplayId = 0, Quality = 1, InventoryType = 0, QuestId = questId };
        }

        static void AddItems(ZoneContent z)
        {
            var L = z.Items;

            // Class start weapons (CharStartOutfit race 4). InventoryType: 13 one-hand, 15 ranged (bow), 17 two-hand,
            // 22 off-hand weapon, 25 thrown. SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip.
            L.Add(Weapon(49778, "Worn Greatsword", "INV_Sword_06", 2380, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(12282, "Worn Battleaxe", "INV_Axe_09", 22291, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2504, "Worn Shortbow", "INV_Weapon_Bow_05", 8106, 1, 15, 0, 2f, 5f, 2.3f, 1));
            L.Add(Weapon(2092, "Worn Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 13, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(50055, "Worn Parrying Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 22, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(28979, "Worn Throwing Knife", "INV_Weapon_ShortBlade_20", 40187, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(3661, "Novice's Staff", "INV_Staff_21", 18530, 1, 17, 2, 3f, 6f, 2.9f, 1));
            // Hunter's quiver (Item.dbc class 11 quiver, InventoryType 18 = a bag, not an equipment slot).
            L.Add(new ItemDef { Entry = 2101, Name = "Worn Quiver", IconPath = @"Interface\Icons\INV_Misc_Quiver_09", DisplayId = 21328, Quality = 1, InventoryType = 18, SellPrice = 1 });

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(6120, "Squire's Shirt", "INV_Shirt_05", 9983, 1, 4, 0, 1));
            L.Add(Armor(6121, "Squire's Trousers", "INV_Pants_02", 9984, 1, 7, 2, 1));
            L.Add(Armor(6122, "Squire's Boots", "INV_Boots_06", 9985, 1, 8, 1, 1));
            L.Add(Armor(148, "Tracker's Shirt", "INV_Shirt_01", 9976, 1, 4, 0, 1));
            L.Add(Armor(147, "Tracker's Trousers", "INV_Pants_02", 9975, 1, 7, 2, 1));
            L.Add(Armor(129, "Tracker's Boots", "INV_Boots_06", 9977, 1, 8, 1, 1));
            L.Add(Armor(49, "Footpad's Shirt", "INV_Shirt_05", 9906, 1, 4, 0, 1));
            L.Add(Armor(48, "Footpad's Trousers", "INV_Pants_02", 9913, 1, 7, 2, 1));
            L.Add(Armor(47, "Footpad's Shoes", "INV_Boots_05", 9915, 1, 8, 1, 1));
            L.Add(Armor(6119, "Novice's Robe", "INV_Chest_Cloth_21", 12681, 1, 20, 2, 1));
            L.Add(Armor(52, "Novice's Leggings", "INV_Pants_02", 9945, 1, 7, 1, 1));
            L.Add(Armor(51, "Novice's Slippers", "INV_Boots_04", 9946, 1, 8, 1, 1));
            L.Add(Armor(53, "Novice's Undershirt", "INV_Shirt_01", 9944, 1, 4, 0, 1));
            L.Add(Armor(6123, "Grove Robe", "INV_Chest_Cloth_34", 12683, 1, 20, 2, 1));
            L.Add(Armor(6124, "Grove Leggings", "INV_Pants_01", 9987, 1, 7, 1, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 7 legs, 8 feet, 10 hands.
            L.Add(Stats(Armor(2957, "Glenweave Vest", "INV_Chest_Cloth_06", 14499, 1, 5, 6, 12), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(2961, "Nightsaber Hide Vest", "INV_Shirt_09", 17093, 1, 5, 12, 14), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2965, "Sentinel Ringmail", "INV_Chest_Chain", 22677, 1, 5, 25, 16), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2959, "Glenweave Slippers", "INV_Boots_09", 14525, 1, 8, 3, 9), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(2963, "Pathfinder Boots", "INV_Boots_09", 16980, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2967, "Sentinel Sabatons", "INV_Boots_Plate_08", 22673, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(2960, "Glenweave Gloves", "INV_Gauntlets_21", 14497, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(2964, "Pathfinder Gloves", "INV_Gauntlets_18", 17175, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2968, "Sentinel Gauntlets", "INV_Gauntlets_28", 22676, 1, 10, 11, 12), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2958, "Glenweave Leggings", "INV_Pants_10", 14498, 2, 7, 5, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(2962, "Pathfinder Leggings", "INV_Pants_06", 17160, 2, 7, 10, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(2966, "Sentinel Legguards", "INV_Pants_03", 7193, 2, 7, 22, 22), 1, 1, 0, 0, 0));

            // Final quest weapons (choice). InventoryType 21 = main hand.
            L.Add(Stats(Weapon(5393, "Glenwarden's Staff", "INV_Staff_09", 5108, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(2139, "Moonfang Dirk", "INV_Weapon_ShortBlade_14", 22135, 2, 13, 3, 3f, 6f, 1.7f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(2777, "Starbreeze Longbow", "INV_Weapon_Bow_05", 2787, 2, 15, 0, 4f, 8f, 2.6f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(923, "Defiler's Bane", "INV_Sword_23", 22080, 2, 21, 3, 4f, 8f, 2.2f, 60), 1, 2, 0, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant).
            L.Add(new ItemDef { Entry = 4540, Name = "Travel Bread", IconPath = @"Interface\Icons\INV_Misc_Food_11", DisplayId = 6399, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 159, Name = "Glen Spring Water", IconPath = @"Interface\Icons\INV_Drink_07", DisplayId = 18084, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 118, Name = "Minor Healing Draught", IconPath = @"Interface\Icons\INV_Potion_49", DisplayId = 15710, Quality = 1, HealAmount = 80, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands, 20 robe.
            L.Add(Armor(1364, "Tattered Leather Vest", "INV_Shirt_05", 14339, 0, 5, 7, 4));
            L.Add(Armor(1366, "Tattered Leather Pants", "INV_Pants_06", 14338, 0, 7, 6, 4));
            L.Add(Armor(1367, "Tattered Leather Boots", "INV_Boots_06", 14354, 0, 8, 4, 3));
            L.Add(Armor(1370, "Tattered Leather Bracers", "INV_Bracer_10", 14336, 0, 9, 2, 2));
            L.Add(Armor(1377, "Threadbare Gloves", "INV_Gauntlets_18", 16657, 0, 10, 1, 2));
            L.Add(Armor(1378, "Threadbare Pants", "INV_Pants_09", 16656, 0, 7, 2, 3));
            L.Add(Armor(1380, "Threadbare Robe", "INV_Chest_Cloth_21", 12426, 0, 20, 3, 4));
            L.Add(Armor(3363, "Threadbare Belt", "INV_Belt_04", 16663, 0, 6, 1, 2));
            L.Add(Armor(2649, "Rusted Chain Belt", "INV_Belt_06", 6902, 0, 6, 6, 3));
            L.Add(Armor(2650, "Rusted Chain Boots", "INV_Boots_01", 6903, 0, 8, 9, 4));
            L.Add(Armor(2653, "Rusted Chain Gloves", "INV_Gauntlets_04", 6905, 0, 10, 7, 3));

            // Invented quest items.
            L.Add(QuestItem(ItemMangyPelt, "Mangy Nightsaber Pelt", "INV_Misc_Pelt_Wolf_02", QuestMangyPelts));
            L.Add(QuestItem(ItemVenomSac, "Webwood Venom Sac", "INV_Misc_Organ_06", QuestVenomInTheWater));
            L.Add(QuestItem(ItemFelCharm, "Fel-Touched Charm", "INV_Jewelry_Talisman_05", QuestTaintedCharms));
        }

        static ItemDef Stats(ItemDef d, int stamina, int strength, int agility, int intellect, int spirit)
        {
            d.Stamina = stamina; d.Strength = strength; d.Agility = agility; d.Intellect = intellect; d.Spirit = spirit;
            return d;
        }

        /// <summary>The item with this entry, or null (linear search; zone data is small).</summary>
        public static ItemDef FindItem(ZoneContent z, int entry)
        {
            foreach (var i in z.Items) if (i.Entry == entry) return i;
            return null;
        }
    }
}
