using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Human start zone, Northshire Valley (Elwynn Forest, map 0 Eastern Kingdoms), as offline zone data for the local
    /// simulation. Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures,
    /// icons and item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All names of
    /// NPCs and items, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_A.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 1 (classes 1/2/4/5/8/9): map 0 (-8949.95, -132.493, 83.5312) o 0.
    /// - Graveyard = WorldSafeLocs.dbc id 105 "Elwynn Forest, Northshire" (-8935.325, -188.64627, 80.416466).
    /// - Classes = CharBaseInfo.dbc race 1: Warrior, Paladin, Rogue, Priest, Mage, Warlock (Death Knight not offered).
    /// - Every spawn/NPC position was picked by the harness probe ZaPlace on the real terrain: z = the ADT terrain height at
    ///   (x, y), area 9 Northshire Valley / 59 Northshire Vineyards / 34 Echo Ridge Mine (NPCs 9 or 59), no water, slope
    ///   &lt;= 30 degrees, no WMO/M2 collision geometry within 1-2 yards (the abbey, fences, trees, vineyard trellises), each wander
    ///   circle checked the same way. The probe ZaCheck re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 1 (what AzerothCore's Player::Create equips), minus the
    ///   hearthstone (6948, no inn offline).
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (Wolf, Kobold, Deer; Human NPC and bandit displays =
    ///   CreatureDisplayInfoExtra race 1 with an existing baked texture); weapons = ItemDisplayInfo ids whose model exists.
    /// </summary>
    public static partial class Northshire
    {
        public const string ZoneName = "Northshire Valley";
        public const int MapEasternKingdoms = 0;

        /// <summary>AreaTable ids (parent 12 Elwynn Forest): 9 Northshire Valley, 24 Northshire Abbey, 34 Echo Ridge Mine,
        /// 59 Northshire Vineyards.</summary>
        public const int AreaNorthshireValley = 9, AreaNorthshireAbbey = 24, AreaEchoRidgeMine = 34, AreaNorthshireVineyards = 59, AreaElwynnForest = 12;
        public static readonly int[] AreaIds = { AreaNorthshireValley, AreaNorthshireAbbey, AreaEchoRidgeMine, AreaNorthshireVineyards };

        /// <summary>ChrRaces id of the race that starts here (1 Human).</summary>
        public static readonly int[] Races = { 1 };

        // ---- creature entries (ours; Northshire uses 911xxx) ----
        public const int YoungWolf = 911001;
        public const int TimberWolf = 911002;
        public const int KoboldVermin = 911003;
        public const int KoboldLaborer = 911004;
        public const int ValleyDeer = 911005;
        public const int VineyardBandit = 911006;
        public const int BanditLookout = 911007;
        public const int HarlanCrowe = 911008;

        // ---- NPC entries (ours; 912xxx) ----
        public const int BrotherAldric = 912001;     // main quest giver at the start (intro, class quests, mine letter, final)
        public const int MarshalCorwin = 912002;     // wolf quests
        public const int ForemanEdda = 912003;       // on the road to the Echo Ridge Mine (kobolds)
        public const int HollisMarrow = 912004;      // at the vineyards across the river (bandits)
        public const int WarriorTrainer = 912011;
        public const int PaladinTrainer = 912012;
        public const int RogueTrainer = 912013;
        public const int PriestTrainer = 912014;
        public const int MageTrainer = 912015;
        public const int WarlockTrainer = 912016;
        public const int GuardEdric = 912021;
        public const int GuardDunstan = 912022;
        public const int GuardMara = 912023;

        // ---- quest ids (ours; 91xxx) ----
        public const int QuestMorningAtTheAbbey = 91001;
        public const int QuestWolvesAtTheFence = 91002;
        public const int QuestTimberPelts = 91003;
        public const int QuestLetterForTheForeman = 91004;
        public const int QuestEchoesInTheRidge = 91005;
        public const int QuestKoboldTrouble = 91006;
        public const int QuestTheAbbeysGrain = 91007;
        public const int QuestWordFromTheVineyards = 91008;
        public const int QuestBanditsAmongTheVines = 91009;
        public const int QuestTheStolenHarvest = 91010;
        public const int QuestTheVinecutter = 91011;
        public const int QuestWarriorCalling = 91021;
        public const int QuestPaladinCalling = 91022;
        public const int QuestRogueCalling = 91023;
        public const int QuestPriestCalling = 91024;
        public const int QuestMageCalling = 91025;
        public const int QuestWarlockCalling = 91026;

        // ---- invented quest items (Northshire range 910000+) ----
        public const int ItemMattedWolfPelt = 910001;
        public const int ItemSackOfAbbeyGrain = 910002;
        public const int ItemPilferedVineyardCrate = 910003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapEasternKingdoms,
                PlayerStart = new WowPos(-8949.95f, -132.493f, 83.5312f, 0f),
                Graveyard = new WowPos(-8935.325f, -188.64627f, 80.416466f, 0f),
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
        // re-checked by the ZaCheck probe) with our own name and simple stats; icons = ItemDisplayInfo.InventoryIcon.
        // Entries shared with Shadowglen (starter weapons, rogue/priest clothes, bread, potion) use Shadowglen's exact definitions.
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

        static ItemDef Stats(ItemDef d, int stamina, int strength, int agility, int intellect, int spirit)
        {
            d.Stamina = stamina; d.Strength = strength; d.Agility = agility; d.Intellect = intellect; d.Spirit = spirit;
            return d;
        }

        static void AddItems(ZoneContent z)
        {
            var L = z.Items;

            // Class start weapons (CharStartOutfit race 1). InventoryType: 13 one-hand, 17 two-hand, 22 off-hand weapon, 25 thrown.
            // SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip.
            L.Add(Weapon(49778, "Worn Greatsword", "INV_Sword_06", 2380, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2361, "Battered Maul", "INV_Hammer_15", 8690, 1, 17, 1, 5f, 9f, 3.4f, 1));
            L.Add(Weapon(2092, "Worn Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 13, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(50055, "Worn Parrying Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 22, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(28979, "Worn Throwing Knife", "INV_Weapon_ShortBlade_20", 40187, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(35, "Bent Oak Staff", "INV_Staff_08", 472, 1, 17, 2, 3f, 6f, 2.9f, 1));

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(38, "Plain Linen Shirt", "INV_Shirt_05", 9891, 1, 4, 0, 1));
            L.Add(Armor(39, "Plain Linen Trousers", "INV_Pants_02", 9892, 1, 7, 2, 1));
            L.Add(Armor(40, "Plain Leather Boots", "INV_Boots_06", 10141, 1, 8, 1, 1));
            L.Add(Armor(45, "Squire's Undershirt", "INV_Shirt_01", 3265, 1, 4, 0, 1));
            L.Add(Armor(44, "Squire's Breeches", "INV_Pants_02", 9937, 1, 7, 2, 1));
            L.Add(Armor(43, "Squire's Riding Boots", "INV_Boots_01", 9938, 1, 8, 1, 1));
            L.Add(Armor(49, "Footpad's Shirt", "INV_Shirt_05", 9906, 1, 4, 0, 1));
            L.Add(Armor(48, "Footpad's Trousers", "INV_Pants_02", 9913, 1, 7, 2, 1));
            L.Add(Armor(47, "Footpad's Shoes", "INV_Boots_05", 9915, 1, 8, 1, 1));
            L.Add(Armor(6098, "Initiate's Robe", "INV_Chest_Cloth_33", 12679, 1, 20, 2, 1));
            L.Add(Armor(52, "Novice's Leggings", "INV_Pants_02", 9945, 1, 7, 1, 1));
            L.Add(Armor(51, "Novice's Slippers", "INV_Boots_04", 9946, 1, 8, 1, 1));
            L.Add(Armor(53, "Novice's Undershirt", "INV_Shirt_01", 9944, 1, 4, 0, 1));
            L.Add(Armor(56, "Scribe's Robe", "INV_Chest_Cloth_23", 12647, 1, 20, 2, 1));
            L.Add(Armor(1395, "Scribe's Trousers", "INV_Pants_01", 9924, 1, 7, 1, 1));
            L.Add(Armor(55, "Scribe's Slippers", "INV_Boots_09", 9929, 1, 8, 1, 1));
            L.Add(Armor(6096, "Scribe's Shirt", "INV_Shirt_01", 2163, 1, 4, 0, 1));
            L.Add(Armor(57, "Duskwoven Robe", "INV_Chest_Cloth_24", 12645, 1, 20, 2, 1));
            L.Add(Armor(6097, "Duskwoven Shirt", "INV_Shirt_01", 2470, 1, 4, 0, 1));
            L.Add(Armor(1396, "Duskwoven Trousers", "INV_Pants_01", 3260, 1, 7, 1, 1));
            L.Add(Armor(59, "Duskwoven Slippers", "INV_Boots_01", 3261, 1, 8, 1, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 10 hands.
            L.Add(Stats(Armor(1770, "Abbey Linen Vest", "INV_Shirt_Grey_01", 14378, 1, 5, 6, 12), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(1211, "Wolfhide Jerkin", "INV_Chest_Leather_04", 14260, 1, 5, 12, 14), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(1488, "Watchman's Chain Vest", "INV_Chest_Chain", 12960, 1, 5, 25, 16), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(80, "Abbey Linen Shoes", "INV_Boots_09", 16854, 1, 8, 3, 9), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(210, "Tunnel Runner Boots", "INV_Boots_06", 14444, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(287, "Watchman's Chain Boots", "INV_Boots_01", 12745, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(203, "Abbey Linen Gloves", "INV_Gauntlets_21", 16779, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(239, "Supple Leather Gloves", "INV_Gauntlets_21", 14475, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(718, "Watchman's Chain Gloves", "INV_Gauntlets_04", 6986, 1, 10, 11, 12), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(79, "Abbey Linen Leggings", "INV_Pants_09", 16847, 2, 7, 5, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(61, "Wolfhide Leggings", "INV_Pants_Wolf", 16953, 2, 7, 10, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(286, "Watchman's Chain Leggings", "INV_Pants_03", 10400, 2, 7, 22, 22), 1, 1, 0, 0, 0));
            L.Add(Stats(Armor(2283, "Vintner's Sash", "INV_Belt_05", 16831, 1, 6, 2, 8), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(1215, "Vintner's Leather Belt", "INV_Belt_09", 16942, 1, 6, 4, 9), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(1154, "Watchman's Chain Belt", "INV_Belt_04", 6833, 1, 6, 9, 10), 1, 0, 0, 0, 0));

            // Final quest weapons (choice). InventoryType 21 = main hand.
            L.Add(Stats(Weapon(767, "Abbey Warden's Staff", "INV_Staff_08", 20443, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(816, "Vinecutter's Knife", "INV_Weapon_ShortBlade_01", 6472, 2, 13, 3, 3f, 6f, 1.7f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(766, "Northshire Mace", "INV_Mace_07", 19621, 2, 21, 3, 4f, 8f, 2.3f, 55), 2, 1, 0, 0, 0));
            L.Add(Stats(Weapon(1194, "Marshal's Greatsword", "INV_Sword_06", 22093, 2, 17, 1, 8f, 13f, 3.2f, 60), 1, 2, 0, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant). 4540 and 118 = Shadowglen's definitions.
            L.Add(new ItemDef { Entry = 4540, Name = "Travel Bread", IconPath = @"Interface\Icons\INV_Misc_Food_11", DisplayId = 6399, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 4536, Name = "Abbey Orchard Apple", IconPath = @"Interface\Icons\INV_Misc_Food_19", DisplayId = 6410, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 1205, Name = "Pressed Grape Juice", IconPath = @"Interface\Icons\INV_Drink_09", DisplayId = 18078, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 118, Name = "Minor Healing Draught", IconPath = @"Interface\Icons\INV_Potion_49", DisplayId = 15710, Quality = 1, HealAmount = 80, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands.
            L.Add(Armor(1425, "Frayed Leather Vest", "INV_Shirt_05", 14190, 0, 5, 7, 4));
            L.Add(Armor(1423, "Frayed Leather Pants", "INV_Pants_06", 14346, 0, 7, 6, 4));
            L.Add(Armor(1419, "Frayed Leather Boots", "INV_Boots_06", 14353, 0, 8, 4, 3));
            L.Add(Armor(1420, "Frayed Leather Bracers", "INV_Bracer_05", 4471, 0, 9, 2, 2));
            L.Add(Armor(1427, "Patched Cloth Shoes", "INV_Boots_09", 16798, 0, 8, 1, 2));
            L.Add(Armor(1430, "Patched Cloth Gloves", "INV_Gauntlets_18", 16797, 0, 10, 1, 2));
            L.Add(Armor(1730, "Tarnished Chain Belt", "INV_Belt_06", 6902, 0, 6, 6, 3));
            L.Add(Armor(1731, "Tarnished Chain Boots", "INV_Boots_01", 6903, 0, 8, 9, 4));

            // Invented quest items.
            L.Add(QuestItem(ItemMattedWolfPelt, "Matted Wolf Pelt", "INV_Misc_Pelt_Wolf_01", QuestTimberPelts));
            L.Add(QuestItem(ItemSackOfAbbeyGrain, "Sack of Abbey Grain", "INV_Misc_Bag_10", QuestTheAbbeysGrain));
            L.Add(QuestItem(ItemPilferedVineyardCrate, "Pilfered Vineyard Crate", "INV_Crate_03", QuestTheStolenHarvest));
        }

        /// <summary>The item with this entry, or null (linear search; zone data is small).</summary>
        public static ItemDef FindItem(ZoneContent z, int entry)
        {
            foreach (var i in z.Items) if (i.Entry == entry) return i;
            return null;
        }
    }
}
