using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Blood Elf start zone, Sunstrider Isle (north of Eversong Woods, map 530 Outland/Expansion01), as offline zone data for the
    /// local simulation. Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures,
    /// icons and item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All NPC names, item
    /// names, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_D.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 10 (classes 2/3/4/5/8/9): map 530 zone 3431 (10349.6, -6357.29, 33.4026)
    ///   o 5.31605 (the ADT terrain height there is 33.40).
    /// - Graveyard = WorldSafeLocs.dbc id 912 "Eversong Woods, Sunstrider Isle" (10458.548, -6364.6143, 39.790714).
    /// - Every spawn/NPC position was picked by the harness probe ContentDPlace on the real terrain: z = the ADT terrain height at (x, y),
    ///   area 3431 Sunstrider Isle, no water, slope &lt;= 30 degrees, no WMO/M2 collision geometry within 1-1.5 yards and no roof
    ///   overhead, each wander circle checked the same way. The probe ContentD re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 10 (what AzerothCore's Player::Create equips), minus the
    ///   hearthstone (6948) and the hunter's arrows (2512).
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (ManaWurm, DruidCat with the lynx skins, LesserManaFiend,
    ///   ManaFiendGreen); Blood Elf NPC displays = CreatureDisplayInfoExtra race 10 with an existing baked texture; weapons =
    ///   ItemDisplayInfo ids whose model exists under Item\ObjectComponents.
    /// </summary>
    public static partial class SunstriderIsle
    {
        public const string ZoneName = "Sunstrider Isle";
        public const int MapOutland = 530;

        /// <summary>AreaTable id 3431 Sunstrider Isle (child of 3430 Eversong Woods).</summary>
        public const int AreaSunstriderIsle = 3431, AreaEversongWoods = 3430;
        public static readonly int[] AreaIds = { AreaSunstriderIsle };

        // ---- creature entries (ours) ----
        public const int ManaWyrm = 971001;
        public const int LynxCub = 971002;
        public const int EversongLynx = 971003;
        public const int ArcaneWraith = 971004;
        public const int TaintedArcaneWraith = 971005;
        public const int MagistrixVelanya = 971006;

        // ---- NPC entries (ours) ----
        public const int MagisterAerondis = 972001;   // main quest giver at the start (intro, class quests, the old ring, final)
        public const int RangerTalvaria = 972002;     // beside the start (mana wyrms, lynx pelts)
        public const int ArcanistVeyselle = 972003;   // south-east, below the wraith hill (arcane residue)
        public const int LieutenantDathrien = 972004; // south-west, watching the tainted hollow
        public const int PaladinTrainer = 972012;
        public const int HunterTrainer = 972013;
        public const int RogueTrainer = 972014;
        public const int PriestTrainer = 972015;
        public const int MageTrainer = 972018;
        public const int WarlockTrainer = 972019;
        public const int GuardianLorathil = 972021;
        public const int GuardianFaeleth = 972022;
        public const int GuardianIrindel = 972023;

        // ---- quest ids (ours) ----
        public const int QuestMorningOnTheIsle = 97001;
        public const int QuestWyrmsOnTheLawns = 97002;
        public const int QuestPeltsForTheWatch = 97003;
        public const int QuestTheOldRing = 97004;
        public const int QuestResidueOfTheWraiths = 97005;
        public const int QuestWarningForTheLieutenant = 97006;
        public const int QuestTheTaintedHollow = 97007;
        public const int QuestCorruptedCrystals = 97008;
        public const int QuestTheExilesEnd = 97009;
        public const int QuestPaladinPath = 97012;
        public const int QuestHunterPath = 97013;
        public const int QuestRoguePath = 97014;
        public const int QuestPriestPath = 97015;
        public const int QuestMagePath = 97018;
        public const int QuestWarlockPath = 97019;

        // ---- invented quest items (Sunstrider Isle range 970000+) ----
        public const int ItemLynxPelt = 970001;
        public const int ItemArcaneResidue = 970002;
        public const int ItemCorruptedCrystal = 970003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapOutland,
                PlayerStart = new WowPos(10349.6f, -6357.29f, 33.4026f, 5.31605f),
                Graveyard = new WowPos(10458.548f, -6364.6143f, 39.790714f, 5.31605f),
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
        // Items. Entry = the real Item.dbc id (display id, inventory type and sheath type below are the Item.dbc values, re-checked by
        // the ContentD probe) with our own name and simple stats; icons = ItemDisplayInfo.InventoryIcon.
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

            // Class start weapons (CharStartOutfit race 10). InventoryType: 13 one-hand, 15 bow, 17 two-hand, 18 bag (quiver),
            // 22 off-hand weapon, 25 thrown. SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip.
            L.Add(Weapon(23346, "Initiate's Greatsword", "INV_Sword_06", 2380, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(12282, "Worn Battleaxe", "INV_Axe_09", 22291, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(20980, "Sunwood Shortbow", "INV_Weapon_Bow_05", 8106, 1, 15, 0, 2f, 5f, 2.3f, 1));
            L.Add(Weapon(20982, "Novice's Dagger", "INV_Weapon_ShortBlade_18", 37170, 1, 13, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(50057, "Novice's Parrying Dagger", "INV_Weapon_ShortBlade_18", 37170, 1, 22, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(28979, "Light Throwing Knife", "INV_Weapon_ShortBlade_20", 40187, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(20978, "Apprentice's Spellstaff", "INV_Staff_08", 33299, 1, 17, 2, 3f, 6f, 2.9f, 1));
            L.Add(new ItemDef { Entry = 2101, Name = "Worn Quiver", IconPath = @"Interface\Icons\INV_Misc_Quiver_09", DisplayId = 21328, Quality = 1, InventoryType = 18, SellPrice = 1 });

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(24143, "Initiate's Tunic", "INV_Chest_Chain_13", 36789, 1, 4, 0, 1));
            L.Add(Armor(24145, "Initiate's Leggings", "INV_Pants_Mail_08", 36790, 1, 7, 2, 1));
            L.Add(Armor(24146, "Initiate's Boots", "INV_Boots_Chain_01", 36793, 1, 8, 1, 1));
            L.Add(Armor(20901, "Ranger's Jerkin", "INV_Chest_Leather_07", 33352, 1, 4, 0, 1));
            L.Add(Armor(20899, "Ranger's Breeches", "INV_Pants_02", 33351, 1, 7, 2, 1));
            L.Add(Armor(20900, "Ranger's Boots", "INV_Boots_06", 33350, 1, 8, 1, 1));
            L.Add(Armor(20897, "Stalker's Vest", "INV_Chest_Leather_09", 33353, 1, 4, 0, 1));
            L.Add(Armor(20896, "Stalker's Pants", "INV_Pants_02", 33342, 1, 7, 2, 1));
            L.Add(Armor(20898, "Stalker's Boots", "INV_Boots_05", 36764, 1, 8, 1, 1));
            L.Add(Armor(20891, "Neophyte's Robe", "INV_Chest_Cloth_33", 33256, 1, 20, 2, 1));
            L.Add(Armor(53, "Neophyte's Shirt", "INV_Shirt_01", 9944, 1, 4, 0, 1));
            L.Add(Armor(52, "Neophyte's Pants", "INV_Pants_02", 9945, 1, 7, 1, 1));
            L.Add(Armor(51, "Neophyte's Boots", "INV_Boots_04", 9946, 1, 8, 1, 1));
            L.Add(Armor(20893, "Apprentice's Robe", "INV_Chest_Cloth_38", 33258, 1, 20, 2, 1));
            L.Add(Armor(20894, "Apprentice's Pants", "INV_Pants_Cloth_02", 33259, 1, 7, 1, 1));
            L.Add(Armor(6096, "Apprentice's Shirt", "INV_Shirt_01", 2163, 1, 4, 0, 1));
            L.Add(Armor(20895, "Apprentice's Boots", "INV_Boots_09", 33260, 1, 8, 1, 1));
            L.Add(Armor(20892, "Acolyte's Robe", "INV_Chest_Cloth_16", 33257, 1, 20, 2, 1));
            L.Add(Armor(1396, "Acolyte's Pants", "INV_Pants_01", 3260, 1, 7, 1, 1));
            L.Add(Armor(59, "Acolyte's Shoes", "INV_Boots_01", 3261, 1, 8, 1, 1));
            L.Add(Armor(6097, "Acolyte's Shirt", "INV_Shirt_01", 2470, 1, 4, 0, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 8 feet, 9 wrists, 10 hands.
            L.Add(Stats(Armor(20985, "Spellweave Slippers", "INV_Boots_09", 16576, 1, 8, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(20921, "Lynxhide Boots", "INV_Boots_06", 33272, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(20915, "Sunguard Sabatons", "INV_Boots_01", 33270, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(20988, "Spellweave Cuffs", "INV_Bracer_03", 16929, 1, 9, 2, 8), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(20922, "Lynxhide Bracers", "INV_Bracer_11", 33275, 1, 9, 4, 9), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(20916, "Sunguard Bracers", "INV_Bracer_03", 33266, 1, 9, 9, 10), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(20990, "Spellweave Tunic", "INV_Shirt_Red_01", 16575, 2, 5, 7, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(20925, "Lynxhide Jerkin", "INV_Shirt_06", 33274, 2, 5, 14, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(20919, "Sunguard Hauberk", "INV_Chest_Chain_13", 33268, 2, 5, 28, 22), 1, 1, 0, 0, 0));
            L.Add(Stats(Armor(20987, "Spellweave Gloves", "INV_Gauntlets_18", 16969, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(20923, "Lynxhide Gloves", "INV_Gauntlets_18", 33276, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(20917, "Sunguard Gauntlets", "INV_Gauntlets_04", 33269, 1, 10, 11, 12), 0, 1, 0, 0, 0));

            // Final quest weapons (choice). InventoryType: 13 one-hand, 15 bow, 21 main hand.
            L.Add(Stats(Weapon(20977, "Blade of the Morning Watch", "INV_Sword_01", 33304, 2, 21, 3, 4f, 8f, 2.4f, 60), 1, 2, 0, 0, 0));
            L.Add(Stats(Weapon(20981, "Scepter of the Quiet Wells", "INV_Mace_02", 37294, 2, 21, 3, 3f, 7f, 2.2f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(20910, "Isle Ranger's Longbow", "INV_Weapon_Bow_02", 20723, 2, 15, 0, 4f, 8f, 2.6f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(20983, "Dawnlight Dirk", "INV_Weapon_ShortBlade_18", 37171, 2, 13, 3, 3f, 6f, 1.7f, 55), 0, 0, 2, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant).
            L.Add(new ItemDef { Entry = 4540, Name = "Sunbaked Loaf", IconPath = @"Interface\Icons\INV_Misc_Food_11", DisplayId = 6399, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 159, Name = "Silverspring Water", IconPath = @"Interface\Icons\INV_Drink_07", DisplayId = 18084, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 118, Name = "Phial of Mending Light", IconPath = @"Interface\Icons\INV_Potion_49", DisplayId = 15710, Quality = 1, HealAmount = 80, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands, 14 shield, 16 back.
            L.Add(Armor(1364, "Frayed Leather Vest", "INV_Shirt_05", 14339, 0, 5, 5, 4));
            L.Add(Armor(1366, "Frayed Leather Pants", "INV_Pants_06", 14338, 0, 7, 4, 3));
            L.Add(Armor(1367, "Frayed Leather Boots", "INV_Boots_06", 14354, 0, 8, 2, 2));
            L.Add(Armor(1370, "Frayed Leather Bracers", "INV_Bracer_10", 14336, 0, 9, 1, 2));
            L.Add(Armor(1376, "Faded Cloak", "INV_Misc_Cape_10", 23090, 0, 16, 2, 3));
            L.Add(Armor(1377, "Faded Gloves", "INV_Gauntlets_18", 16657, 0, 10, 1, 2));
            L.Add(Armor(1378, "Faded Trousers", "INV_Pants_09", 16656, 0, 7, 2, 3));
            L.Add(Armor(3363, "Faded Sash", "INV_Belt_04", 16663, 0, 6, 1, 2));
            L.Add(new ItemDef { Entry = 2211, Name = "Chipped Oval Shield", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 18656, Quality = 0, InventoryType = 14, SheatheType = 4, Armor = 5, SellPrice = 3 });

            // Invented quest items.
            L.Add(QuestItem(ItemLynxPelt, "Eversong Lynx Pelt", "INV_Misc_Pelt_13", QuestPeltsForTheWatch));
            L.Add(QuestItem(ItemArcaneResidue, "Shimmering Arcane Residue", "INV_Enchant_DustIllusion", QuestResidueOfTheWraiths));
            L.Add(QuestItem(ItemCorruptedCrystal, "Corrupted Mana Crystal", "INV_Misc_Gem_Amethyst_01", QuestCorruptedCrystals));
        }

        /// <summary>The item with this entry, or null (linear search; zone data is small).</summary>
        public static ItemDef FindItem(ZoneContent z, int entry)
        {
            foreach (var i in z.Items) if (i.Entry == entry) return i;
            return null;
        }
    }
}
