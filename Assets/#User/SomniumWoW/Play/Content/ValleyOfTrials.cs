using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Orc and Troll start zone, the Valley of Trials (Durotar, map 1 Kalimdor), as offline zone data for the local
    /// simulation. Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures,
    /// icons and item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All names of
    /// NPCs and items, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_B.md):
    /// - PlayerStart = AzerothCore playercreateinfo races 2 and 8 (every class but the Death Knight): map 1 zone 14
    ///   (-618.518, -4251.67, 38.718) o 0.
    /// - Graveyard = WorldSafeLocs.dbc id 709 "Durotar, Valley of Trials" (-634.6349, -4296.0337, 40.52543).
    /// - AreaTable 363 "Valley of Trials" (parent 14 Durotar); the Burning Blade Coven cave (365) is not used. Every spawn/NPC
    ///   position was picked by the harness probe BPlace on the real terrain: z = the ADT terrain height at (x, y), area 363,
    ///   no water, slope &lt;= 30 degrees (no cliff within 3 yards), no WMO/M2 collision geometry within 1-1.5 yards, not under a
    ///   roof; every wander circle checked the same way. The probe ContentB re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows (identical for both sexes), minus the hearthstone and the
    ///   hunter's arrows. ClassStart has no race field, so classes both races share use the ORC rows; Priest and Mage (troll
    ///   only) use the troll rows. (Troll warrior/hunter/rogue/shaman rows differ slightly - see the report.)
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (Boar, Scorpion, Imp models; Burning Blade orcs =
    ///   CreatureDisplayInfoExtra race 2 in robes/leathers; Orc and Troll NPCs = CreatureDisplayInfoExtra race 2 / 8 with an
    ///   existing baked texture); held weapons = ItemDisplayInfo ids whose model exists.
    /// </summary>
    public static partial class ValleyOfTrials
    {
        public const string ZoneName = "Valley of Trials";
        public const int MapKalimdor = 1;

        /// <summary>AreaTable ids: 363 Valley of Trials (parent 14 Durotar).</summary>
        public const int AreaValleyOfTrials = 363, AreaDurotar = 14;
        public static readonly int[] AreaIds = { AreaValleyOfTrials };

        // ---- creature entries (ours, 941000+) ----
        public const int MottledBoar = 941001;
        public const int ScorpidWorker = 941002;
        public const int DuskScorpid = 941003;
        public const int VileFamiliar = 941004;
        public const int BurningBladeNeophyte = 941005;
        public const int BurningBladeThug = 941006;
        public const int VorzulAshbrand = 941007;

        // ---- NPC entries (ours, 942000+) ----
        public const int ElderGarosh = 942001;       // main quest giver beside the start (intro, class quests, delivery, final)
        public const int WarbandCaptain = 942002;    // boars, scorpid tails
        public const int ScoutZanji = 942003;        // the northern meadows (familiars, dusk scorpids, the coven scouting)
        public const int WatchLeader = 942004;       // far north, below the coven (sigils, the cult leader)
        public const int WarriorTrainer = 942011;
        public const int HunterTrainer = 942013;
        public const int RogueTrainer = 942014;
        public const int PriestTrainer = 942015;
        public const int ShamanTrainer = 942017;
        public const int MageTrainer = 942018;
        public const int WarlockTrainer = 942019;
        public const int GruntOne = 942021;
        public const int GruntTwo = 942022;
        public const int GruntThree = 942023;

        // ---- quest ids (ours, 94000+) ----
        public const int QuestBloodAndSand = 94001;
        public const int QuestTusksOfTheValley = 94002;
        public const int QuestStingers = 94003;
        public const int QuestWordToTheScout = 94004;
        public const int QuestImpsInTheRocks = 94005;
        public const int QuestDuskVenom = 94006;
        public const int QuestTheCovensShadow = 94007;
        public const int QuestSigilsOfTheBlade = 94008;
        public const int QuestAshbrandsEnd = 94009;
        public const int QuestWarriorPath = 94011;
        public const int QuestHunterPath = 94013;
        public const int QuestRoguePath = 94014;
        public const int QuestPriestPath = 94015;
        public const int QuestShamanPath = 94017;
        public const int QuestMagePath = 94018;
        public const int QuestWarlockPath = 94019;

        // ---- invented quest items (940000+) ----
        public const int ItemScorpidTail = 940001;
        public const int ItemDuskVenomGland = 940002;
        public const int ItemBladeSigil = 940003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapKalimdor,
                PlayerStart = new WowPos(-618.518f, -4251.67f, 38.718f, 0f),
                Graveyard = new WowPos(-634.6349f, -4296.0337f, 40.52543f, 0f),
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

            // Class start weapons (CharStartOutfit races 2 / 8). InventoryType: 13 one-hand, 14 shield, 15 ranged (bow),
            // 17 two-hand, 21 main hand, 22 off-hand weapon, 25 thrown. SheatheType: 1 two-hand on the back, 2 staff on the
            // back, 3 one-hand at the hip, 4 shield on the back.
            L.Add(Weapon(12282, "Chipped Battleaxe", "INV_Axe_09", 22291, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2504, "Frayed Shortbow", "INV_Weapon_Bow_05", 8106, 1, 15, 0, 2f, 5f, 2.3f, 1));
            L.Add(Weapon(2092, "Notched Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 13, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(50055, "Notched Parrying Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 22, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(25861, "Crude Throwing Axe", "INV_Axe_19", 20777, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(36, "Knotted Wooden Mace", "INV_Mace_03", 5194, 1, 21, 3, 2f, 5f, 1.9f, 1));
            L.Add(Weapon(35, "Sunbaked Staff", "INV_Staff_08", 472, 1, 17, 2, 3f, 6f, 2.9f, 1));
            L.Add(new ItemDef { Entry = 2362, Name = "Cracked Hide Shield", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 18730, Quality = 1, InventoryType = 14, SheatheType = 4, Armor = 5, SellPrice = 1 });
            // Hunter's quiver (Item.dbc class 11 quiver, InventoryType 18 = a bag, not an equipment slot).
            L.Add(new ItemDef { Entry = 2101, Name = "Scuffed Quiver", IconPath = @"Interface\Icons\INV_Misc_Quiver_09", DisplayId = 21328, Quality = 1, InventoryType = 18, SellPrice = 1 });

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(6125, "Brawler's Jerkin", "INV_Chest_Leather_04", 9995, 1, 4, 0, 1));
            L.Add(Armor(139, "Brawler's Leggings", "INV_Pants_02", 9988, 1, 7, 2, 1));
            L.Add(Armor(140, "Brawler's Boots", "INV_Boots_04", 9992, 1, 8, 1, 1));
            L.Add(Armor(127, "Trailrunner's Shirt", "INV_Shirt_07", 9996, 1, 4, 0, 1));
            L.Add(Armor(6126, "Trailrunner's Leggings", "INV_Pants_02", 10002, 1, 7, 2, 1));
            L.Add(Armor(6127, "Trailrunner's Boots", "INV_Boots_06", 10003, 1, 8, 1, 1));
            L.Add(Armor(2105, "Sandstalker's Shirt", "INV_Shirt_14", 10005, 1, 4, 0, 1));
            L.Add(Armor(120, "Sandstalker's Trousers", "INV_Pants_02", 10006, 1, 7, 2, 1));
            L.Add(Armor(121, "Sandstalker's Boots", "INV_Boots_05", 10008, 1, 8, 1, 1));
            L.Add(Armor(154, "Stormseeker's Vest", "INV_Misc_Cape_14", 10058, 1, 4, 0, 1));
            L.Add(Armor(153, "Stormseeker's Kilt", "INV_Pants_02", 10050, 1, 7, 2, 1));
            L.Add(Armor(6129, "Ashen Robe", "INV_Chest_Cloth_16", 12646, 1, 20, 2, 1));
            L.Add(Armor(1396, "Ashen Trousers", "INV_Pants_01", 3260, 1, 7, 1, 1));
            L.Add(Armor(59, "Ashen Slippers", "INV_Boots_01", 3261, 1, 8, 1, 1));
            L.Add(Armor(6144, "Spirit-Speaker's Robe", "INV_Chest_Cloth_13", 12680, 1, 20, 2, 1));
            L.Add(Armor(53, "Spirit-Speaker's Undershirt", "INV_Shirt_01", 9944, 1, 4, 0, 1));
            L.Add(Armor(52, "Spirit-Speaker's Leggings", "INV_Pants_02", 9945, 1, 7, 1, 1));
            L.Add(Armor(6140, "Hexweaver's Robe", "INV_Chest_Cloth_22", 12649, 1, 20, 2, 1));
            L.Add(Armor(1395, "Hexweaver's Trousers", "INV_Pants_01", 9924, 1, 7, 1, 1));
            L.Add(Armor(6096, "Hexweaver's Shirt", "INV_Shirt_01", 2163, 1, 4, 0, 1));
            L.Add(Armor(55, "Hexweaver's Slippers", "INV_Boots_09", 9929, 1, 8, 1, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 7 legs, 8 feet, 10 hands.
            L.Add(Stats(Armor(2957, "Dustweave Vest", "INV_Chest_Cloth_06", 14499, 1, 5, 6, 12), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(2961, "Boarhide Vest", "INV_Shirt_09", 17093, 1, 5, 12, 14), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2965, "Grunt's Ringmail", "INV_Chest_Chain", 22677, 1, 5, 25, 16), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2959, "Dustweave Slippers", "INV_Boots_09", 14525, 1, 8, 3, 9), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(2963, "Canyon Stalker Boots", "INV_Boots_09", 16980, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2967, "Grunt's Sabatons", "INV_Boots_Plate_08", 22673, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(2960, "Dustweave Gloves", "INV_Gauntlets_21", 14497, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(2964, "Canyon Stalker Gloves", "INV_Gauntlets_18", 17175, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2968, "Grunt's Gauntlets", "INV_Gauntlets_28", 22676, 1, 10, 11, 12), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2958, "Dustweave Leggings", "INV_Pants_10", 14498, 2, 7, 5, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(2962, "Canyon Stalker Leggings", "INV_Pants_06", 17160, 2, 7, 10, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(2966, "Grunt's Legguards", "INV_Pants_03", 7193, 2, 7, 22, 22), 1, 1, 0, 0, 0));

            // Final quest weapons (choice). InventoryType 21 = main hand.
            L.Add(Stats(Weapon(5393, "Staff of the Red Canyon", "INV_Staff_09", 5108, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(2139, "Ember-Quenched Dirk", "INV_Weapon_ShortBlade_14", 22135, 2, 13, 3, 3f, 6f, 1.7f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(2777, "Darkspear Hunting Bow", "INV_Weapon_Bow_05", 2787, 2, 15, 0, 4f, 8f, 2.6f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(923, "Valley Warden's Blade", "INV_Sword_23", 22080, 2, 21, 3, 4f, 8f, 2.2f, 60), 1, 2, 0, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant).
            L.Add(new ItemDef { Entry = 4540, Name = "Durotar Flatbread", IconPath = @"Interface\Icons\INV_Misc_Food_11", DisplayId = 6399, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 159, Name = "Canyon Spring Water", IconPath = @"Interface\Icons\INV_Drink_07", DisplayId = 18084, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 118, Name = "Bitterroot Draught", IconPath = @"Interface\Icons\INV_Potion_49", DisplayId = 15710, Quality = 1, HealAmount = 80, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands, 20 robe.
            L.Add(Armor(1364, "Sun-Cracked Leather Vest", "INV_Shirt_05", 14339, 0, 5, 7, 4));
            L.Add(Armor(1366, "Sun-Cracked Leather Pants", "INV_Pants_06", 14338, 0, 7, 6, 4));
            L.Add(Armor(1367, "Sun-Cracked Leather Boots", "INV_Boots_06", 14354, 0, 8, 4, 3));
            L.Add(Armor(1370, "Sun-Cracked Leather Bracers", "INV_Bracer_10", 14336, 0, 9, 2, 2));
            L.Add(Armor(1377, "Sand-Worn Gloves", "INV_Gauntlets_18", 16657, 0, 10, 1, 2));
            L.Add(Armor(1378, "Sand-Worn Pants", "INV_Pants_09", 16656, 0, 7, 2, 3));
            L.Add(Armor(1380, "Sand-Worn Robe", "INV_Chest_Cloth_21", 12426, 0, 20, 3, 4));
            L.Add(Armor(3363, "Sand-Worn Belt", "INV_Belt_04", 16663, 0, 6, 1, 2));
            L.Add(Armor(2649, "Pitted Chain Belt", "INV_Belt_06", 6902, 0, 6, 6, 3));
            L.Add(Armor(2650, "Pitted Chain Boots", "INV_Boots_01", 6903, 0, 8, 9, 4));
            L.Add(Armor(2653, "Pitted Chain Gloves", "INV_Gauntlets_04", 6905, 0, 10, 7, 3));

            // Invented quest items.
            L.Add(QuestItem(ItemScorpidTail, "Scorpid Worker Tail", "INV_Misc_MonsterTail_03", QuestStingers));
            L.Add(QuestItem(ItemDuskVenomGland, "Dusk Scorpid Venom Gland", "INV_Misc_Organ_03", QuestDuskVenom));
            L.Add(QuestItem(ItemBladeSigil, "Burning Blade Sigil", "INV_Misc_Rune_01", QuestSigilsOfTheBlade));
        }
    }
}
