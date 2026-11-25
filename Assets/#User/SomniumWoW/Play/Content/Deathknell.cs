using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Undead start zone, Deathknell (Tirisfal Glades, map 0 Eastern Kingdoms), as offline zone data for the local simulation.
    /// Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures, icons and
    /// item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All names of NPCs and
    /// items, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_B.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 5 (classes 1/4/5/8/9): map 0 zone 85 (1676.71, 1678.31, 121.67) o 2.70526.
    ///   That point is INSIDE the crypt under the hill (MD_CRYPTONERM.WMO, group 'Stairs06' floor z 121.7; the terrain above
    ///   is 138.55) - the real Undead start: the player walks up the stairs and out of the mausoleum onto the forecourt, where
    ///   the first quest giver stands.
    /// - Graveyard = WorldSafeLocs.dbc id 94 "Tirisfal Glades, Deathknell" (1882.94, 1629.11, 94.4175), beside the chapel.
    /// - AreaTable 154 "Deathknell" (parent 85 Tirisfal Glades). Every spawn/NPC position was picked by the harness probe BPlace
    ///   on the real terrain: z = the ADT terrain height at (x, y), area 154, no water, slope &lt;= 30 degrees (no cliff within
    ///   3 yards), no WMO/M2 collision geometry within 1-1.5 yards, not under a roof; every wander circle checked the same way.
    ///   The probe ContentB re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 5 (identical for both sexes), minus the hearthstone.
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (Zombie, Skeleton, Ghoul, FelBat, MineSpider models;
    ///   Scarlet humans = CreatureDisplayInfoExtra race 1 wearing Crusader textures; Undead NPCs = CreatureDisplayInfoExtra
    ///   race 5 with an existing baked texture); held weapons = ItemDisplayInfo ids whose model exists.
    /// </summary>
    public static partial class Deathknell
    {
        public const string ZoneName = "Deathknell";
        public const int MapEasternKingdoms = 0;

        /// <summary>AreaTable ids: 154 Deathknell (parent 85 Tirisfal Glades).</summary>
        public const int AreaDeathknell = 154, AreaTirisfal = 85;
        public static readonly int[] AreaIds = { AreaDeathknell };

        // ---- creature entries (ours, 931000+) ----
        public const int MindlessZombie = 931001;
        public const int Duskbat = 931002;
        public const int RestlessSkeleton = 931003;
        public const int WretchedGhoul = 931004;
        public const int YoungNightWebSpider = 931005;
        public const int NightWebSpider = 931006;
        public const int ScarletConvert = 931007;
        public const int ScarletInitiate = 931008;
        public const int InquisitorMarrenVale = 931009;

        // ---- NPC entries (ours, 932000+) ----
        public const int ShadowPriestessVessra = 932001;  // main quest giver on the crypt forecourt (intro, class quests, final)
        public const int CaptainDorrin = 932002;          // Deathguard captain above the crypt (zombies, report)
        public const int ApothecaryMerrow = 932003;       // by the chapel (bat wings, spider silk)
        public const int ExecutorHarrow = 932004;         // on the east road (the fields, the Scarlet camp)
        public const int WarriorTrainer = 932011;
        public const int RogueTrainer = 932014;
        public const int PriestTrainer = 932015;
        public const int MageTrainer = 932018;
        public const int WarlockTrainer = 932019;
        public const int DeathguardOne = 932021;
        public const int DeathguardTwo = 932022;
        public const int DeathguardThree = 932023;

        // ---- quest ids (ours, 93000+) ----
        public const int QuestRisenAgain = 93001;
        public const int QuestTheMindless = 93002;
        public const int QuestReportToTheChapel = 93003;
        public const int QuestWingsInTheDusk = 93004;
        public const int QuestSilkForTheApothecary = 93005;
        public const int QuestTheExecutorsSummons = 93006;
        public const int QuestBonesThatWalk = 93007;
        public const int QuestWatchersOnTheRidge = 93008;
        public const int QuestCrimsonArmbands = 93009;
        public const int QuestTheInquisitor = 93010;
        public const int QuestWarriorPath = 93011;
        public const int QuestRoguePath = 93014;
        public const int QuestPriestPath = 93015;
        public const int QuestMagePath = 93018;
        public const int QuestWarlockPath = 93019;

        // ---- invented quest items (930000+) ----
        public const int ItemDuskbatWing = 930001;
        public const int ItemNightWebSilk = 930002;
        public const int ItemCrimsonArmband = 930003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapEasternKingdoms,
                PlayerStart = new WowPos(1676.71f, 1678.31f, 121.67f, 2.70526f),
                Graveyard = new WowPos(1882.94f, 1629.11f, 94.4175f, 2.70526f),
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
        // re-checked by the ContentB probe) with our own name and simple stats; icons = ItemDisplayInfo.InventoryIcon.
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

            // Class start weapons (CharStartOutfit race 5). InventoryType: 13 one-hand, 17 two-hand, 22 off-hand weapon,
            // 25 thrown. SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip.
            L.Add(Weapon(49778, "Gravewarden's Greatsword", "INV_Sword_06", 2380, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2092, "Pitted Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 13, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(50055, "Pitted Parrying Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 22, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(28979, "Chipped Throwing Knife", "INV_Weapon_ShortBlade_20", 40187, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(35, "Gnarled Yew Staff", "INV_Staff_08", 472, 1, 17, 2, 3f, 6f, 2.9f, 1));

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(6125, "Grave-Soiled Jerkin", "INV_Chest_Leather_04", 9995, 1, 4, 0, 1));
            L.Add(Armor(139, "Grave-Soiled Leggings", "INV_Pants_02", 9988, 1, 7, 2, 1));
            L.Add(Armor(140, "Grave-Soiled Boots", "INV_Boots_04", 9992, 1, 8, 1, 1));
            L.Add(Armor(2105, "Shroudstitch Shirt", "INV_Shirt_14", 10005, 1, 4, 0, 1));
            L.Add(Armor(120, "Shroudstitch Trousers", "INV_Pants_02", 10006, 1, 7, 2, 1));
            L.Add(Armor(121, "Shroudstitch Boots", "INV_Boots_05", 10008, 1, 8, 1, 1));
            L.Add(Armor(6144, "Mourner's Robe", "INV_Chest_Cloth_13", 12680, 1, 20, 2, 1));
            L.Add(Armor(53, "Mourner's Undershirt", "INV_Shirt_01", 9944, 1, 4, 0, 1));
            L.Add(Armor(52, "Mourner's Leggings", "INV_Pants_02", 9945, 1, 7, 1, 1));
            L.Add(Armor(51, "Mourner's Slippers", "INV_Boots_04", 9946, 1, 8, 1, 1));
            L.Add(Armor(6140, "Cryptscholar's Robe", "INV_Chest_Cloth_22", 12649, 1, 20, 2, 1));
            L.Add(Armor(1395, "Cryptscholar's Trousers", "INV_Pants_01", 9924, 1, 7, 1, 1));
            L.Add(Armor(6096, "Cryptscholar's Shirt", "INV_Shirt_01", 2163, 1, 4, 0, 1));
            L.Add(Armor(55, "Cryptscholar's Slippers", "INV_Boots_09", 9929, 1, 8, 1, 1));
            L.Add(Armor(6129, "Gloomweave Robe", "INV_Chest_Cloth_16", 12646, 1, 20, 2, 1));
            L.Add(Armor(1396, "Gloomweave Trousers", "INV_Pants_01", 3260, 1, 7, 1, 1));
            L.Add(Armor(59, "Gloomweave Slippers", "INV_Boots_01", 3261, 1, 8, 1, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 7 legs, 8 feet, 10 hands.
            L.Add(Stats(Armor(2957, "Cryptweave Vest", "INV_Chest_Cloth_06", 14499, 1, 5, 6, 12), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(2961, "Batwing Leather Vest", "INV_Shirt_09", 17093, 1, 5, 12, 14), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2965, "Deathguard Ringmail", "INV_Chest_Chain", 22677, 1, 5, 25, 16), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2959, "Cryptweave Slippers", "INV_Boots_09", 14525, 1, 8, 3, 9), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(2963, "Gravecreeper Boots", "INV_Boots_09", 16980, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2967, "Deathguard Sabatons", "INV_Boots_Plate_08", 22673, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(2960, "Cryptweave Gloves", "INV_Gauntlets_21", 14497, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(2964, "Gravecreeper Gloves", "INV_Gauntlets_18", 17175, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2968, "Deathguard Gauntlets", "INV_Gauntlets_28", 22676, 1, 10, 11, 12), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2958, "Cryptweave Leggings", "INV_Pants_10", 14498, 2, 7, 5, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(2962, "Gravecreeper Leggings", "INV_Pants_06", 17160, 2, 7, 10, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(2966, "Deathguard Legguards", "INV_Pants_03", 7193, 2, 7, 22, 22), 1, 1, 0, 0, 0));

            // Final quest weapons (choice). InventoryType 21 = main hand.
            L.Add(Stats(Weapon(5393, "Staff of the Quiet Crypt", "INV_Staff_09", 5108, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(2139, "Inquisitor's Bane", "INV_Weapon_ShortBlade_14", 22135, 2, 13, 3, 3f, 6f, 1.7f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(2777, "Tirisfal Yew Bow", "INV_Weapon_Bow_05", 2787, 2, 15, 0, 4f, 8f, 2.6f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(923, "Forsaken Longblade", "INV_Sword_23", 22080, 2, 21, 3, 4f, 8f, 2.2f, 60), 1, 2, 0, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant).
            L.Add(new ItemDef { Entry = 4540, Name = "Hard Crypt Bread", IconPath = @"Interface\Icons\INV_Misc_Food_11", DisplayId = 6399, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 159, Name = "Brackish Well Water", IconPath = @"Interface\Icons\INV_Drink_07", DisplayId = 18084, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 118, Name = "Apothecary's Tonic", IconPath = @"Interface\Icons\INV_Potion_49", DisplayId = 15710, Quality = 1, HealAmount = 80, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands, 20 robe.
            L.Add(Armor(1364, "Moth-Eaten Leather Vest", "INV_Shirt_05", 14339, 0, 5, 7, 4));
            L.Add(Armor(1366, "Moth-Eaten Leather Pants", "INV_Pants_06", 14338, 0, 7, 6, 4));
            L.Add(Armor(1367, "Moth-Eaten Leather Boots", "INV_Boots_06", 14354, 0, 8, 4, 3));
            L.Add(Armor(1370, "Moth-Eaten Leather Bracers", "INV_Bracer_10", 14336, 0, 9, 2, 2));
            L.Add(Armor(1377, "Mildewed Gloves", "INV_Gauntlets_18", 16657, 0, 10, 1, 2));
            L.Add(Armor(1378, "Mildewed Pants", "INV_Pants_09", 16656, 0, 7, 2, 3));
            L.Add(Armor(1380, "Mildewed Robe", "INV_Chest_Cloth_21", 12426, 0, 20, 3, 4));
            L.Add(Armor(3363, "Mildewed Belt", "INV_Belt_04", 16663, 0, 6, 1, 2));
            L.Add(Armor(2649, "Corroded Chain Belt", "INV_Belt_06", 6902, 0, 6, 6, 3));
            L.Add(Armor(2650, "Corroded Chain Boots", "INV_Boots_01", 6903, 0, 8, 9, 4));
            L.Add(Armor(2653, "Corroded Chain Gloves", "INV_Gauntlets_04", 6905, 0, 10, 7, 3));

            // Invented quest items.
            L.Add(QuestItem(ItemDuskbatWing, "Duskbat Wing", "INV_Misc_MonsterClaw_02", QuestWingsInTheDusk));
            L.Add(QuestItem(ItemNightWebSilk, "Night Web Silk Gland", "INV_Misc_Organ_06", QuestSilkForTheApothecary));
            L.Add(QuestItem(ItemCrimsonArmband, "Crimson Armband", "INV_Misc_Bandage_12", QuestCrimsonArmbands));
        }
    }
}
