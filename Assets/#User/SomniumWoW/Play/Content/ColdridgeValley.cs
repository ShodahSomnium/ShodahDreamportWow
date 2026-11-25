using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Dwarf and Gnome start zone, Coldridge Valley (Dun Morogh, map 0 Eastern Kingdoms), as offline zone data for the local
    /// simulation. Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures,
    /// icons and item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All names of
    /// NPCs and items, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_A.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 3 (classes 1/2/3/4/5) and race 7 (1/4/8/9): map 0, zone 1,
    ///   (-6240.32, 331.033, 382.758), orientation 6.17716 (the dwarf rows; the gnome rows use 0).
    /// - Graveyard = WorldSafeLocs.dbc id 100 "Dun Morogh, Anvilmar" (-6164.226, 336.32114, 399.79337).
    /// - Classes = CharBaseInfo.dbc race 3: Warrior, Paladin, Hunter, Rogue, Priest; race 7: Warrior, Rogue, Mage, Warlock
    ///   (Death Knight not offered).
    /// - Every spawn/NPC position was picked by the harness probe ZaPlace on the real terrain: z = the ADT terrain height at
    ///   (x, y), area 132 Coldridge Valley, no water, slope &lt;= 30 degrees, no WMO/M2 collision geometry within 1-2 yards
    ///   (Anvilmar, the dig sites' timbers, the troll tents, rocks and pines), each wander circle checked the same way. The probe
    ///   ZaCheck re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 3 / race 7 (what AzerothCore's Player::Create equips),
    ///   minus the hearthstone (6948, no inn offline) and the hunter's bullets (2516, ammo is not simulated).
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (arctic Wolf, Boar, Troglodyte, Troll/TrollForestBoss
    ///   with the arctic tribe skins); dwarf and gnome NPC displays = CreatureDisplayInfoExtra race 3 / 7 with an existing baked
    ///   texture; weapons = ItemDisplayInfo ids whose model exists.
    /// </summary>
    public static partial class ColdridgeValley
    {
        public const string ZoneName = "Coldridge Valley";
        public const int MapEasternKingdoms = 0;

        /// <summary>AreaTable ids (parent 1 Dun Morogh): 132 Coldridge Valley, 77 Anvilmar, 800 Coldridge Pass. The ADT chunks of
        /// the valley floor all carry 132.</summary>
        public const int AreaColdridgeValley = 132, AreaAnvilmar = 77, AreaColdridgePass = 800, AreaDunMorogh = 1;
        public static readonly int[] AreaIds = { AreaColdridgeValley, AreaAnvilmar };

        /// <summary>ChrRaces ids of the races that start here (3 Dwarf, 7 Gnome).</summary>
        public static readonly int[] Races = { 3, 7 };

        // ---- creature entries (ours; Coldridge uses 921xxx) ----
        public const int YoungSnowWolf = 921001;
        public const int SmallCragBoar = 921002;
        public const int TroggScavenger = 921003;
        public const int BurlyTrogg = 921004;
        public const int SnowbrandYoungblood = 921005;
        public const int SnowbrandSeer = 921006;
        public const int VakjinTheRimecaller = 921007;

        // ---- NPC entries (ours; 922xxx) ----
        public const int CaptainDorgan = 922001;       // main quest giver at the start (intro, class quests, surveyor letter, final)
        public const int QuartermasterSenna = 922002;  // wolves and boars near Anvilmar
        public const int SurveyorFizzla = 922003;      // at the ruined dig west of Anvilmar (troggs)
        public const int MountaineerTharna = 922004;   // the western watch facing the troll camp
        public const int WarriorTrainer = 922011;
        public const int PaladinTrainer = 922012;
        public const int HunterTrainer = 922013;
        public const int RogueTrainer = 922014;
        public const int PriestTrainer = 922015;
        public const int MageTrainer = 922016;
        public const int WarlockTrainer = 922017;
        public const int GuardBorvak = 922021;
        public const int GuardDurm = 922022;
        public const int GuardKelda = 922023;

        // ---- quest ids (ours; 92xxx) ----
        public const int QuestAColdWelcome = 92001;
        public const int QuestTeethInTheSnow = 92002;
        public const int QuestBoarFlanks = 92003;
        public const int QuestWordForTheSurveyor = 92004;
        public const int QuestScavengersInTheDig = 92005;
        public const int QuestTheDeeperDig = 92006;
        public const int QuestAMessageForTheWatch = 92007;
        public const int QuestSmokeOverTheTents = 92008;
        public const int QuestSnowbrandRaiders = 92009;
        public const int QuestFetishesOfFrost = 92010;
        public const int QuestTheRimecaller = 92011;
        public const int QuestWarriorCalling = 92021;
        public const int QuestPaladinCalling = 92022;
        public const int QuestHunterCalling = 92023;
        public const int QuestRogueCalling = 92024;
        public const int QuestPriestCalling = 92025;
        public const int QuestMageCalling = 92026;
        public const int QuestWarlockCalling = 92027;

        // ---- invented quest items (Coldridge range 920000+) ----
        public const int ItemCragBoarFlank = 920001;
        public const int ItemStolenSurveyGear = 920002;
        public const int ItemSnowbrandFetish = 920003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapEasternKingdoms,
                PlayerStart = new WowPos(-6240.32f, 331.033f, 382.758f, 6.17716f),
                Graveyard = new WowPos(-6164.226f, 336.32114f, 399.79337f, 0f),
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
        // Entries shared with Shadowglen / Northshire (starter weapons and clothes, potion, poor drops) use their exact definitions.
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

            // Class start weapons (CharStartOutfit race 3 / 7). InventoryType: 13 one-hand, 17 two-hand, 22 off-hand weapon,
            // 25 thrown, 26 ranged (gun). SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip.
            L.Add(Weapon(12282, "Worn Battleaxe", "INV_Axe_09", 22291, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(49778, "Worn Greatsword", "INV_Sword_06", 2380, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2361, "Battered Maul", "INV_Hammer_15", 8690, 1, 17, 1, 5f, 9f, 3.4f, 1));
            L.Add(Weapon(2092, "Worn Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 13, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(50055, "Worn Parrying Dagger", "INV_Weapon_ShortBlade_05", 6442, 1, 22, 3, 1f, 3f, 1.6f, 1));
            L.Add(Weapon(25861, "Worn Throwing Axe", "INV_Axe_19", 20777, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(28979, "Worn Throwing Knife", "INV_Weapon_ShortBlade_20", 40187, 1, 25, 0, 1f, 3f, 2.0f, 1));
            L.Add(Weapon(35, "Bent Oak Staff", "INV_Staff_08", 472, 1, 17, 2, 3f, 6f, 2.9f, 1));
            L.Add(Weapon(2508, "Worn Hunting Rifle", "INV_Weapon_Rifle_01", 6606, 1, 26, 0, 2f, 5f, 2.3f, 1));
            // Hunter's ammo pouch (Item.dbc class 11, InventoryType 18 = a bag, not an equipment slot).
            L.Add(new ItemDef { Entry = 2102, Name = "Worn Ammo Pouch", IconPath = @"Interface\Icons\INV_Misc_Ammo_Bullet_01", DisplayId = 1816, Quality = 1, InventoryType = 18, SellPrice = 1 });

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(38, "Plain Linen Shirt", "INV_Shirt_05", 9891, 1, 4, 0, 1));
            L.Add(Armor(39, "Plain Linen Trousers", "INV_Pants_02", 9892, 1, 7, 2, 1));
            L.Add(Armor(40, "Plain Leather Boots", "INV_Boots_06", 10141, 1, 8, 1, 1));
            L.Add(Armor(6117, "Squire's Wool Shirt", "INV_Shirt_01", 9972, 1, 4, 0, 1));
            L.Add(Armor(6118, "Squire's Wool Breeches", "INV_Pants_02", 9974, 1, 7, 2, 1));
            L.Add(Armor(43, "Squire's Riding Boots", "INV_Boots_01", 9938, 1, 8, 1, 1));
            L.Add(Armor(148, "Tracker's Shirt", "INV_Shirt_01", 9976, 1, 4, 0, 1));
            L.Add(Armor(147, "Tracker's Trousers", "INV_Pants_02", 9975, 1, 7, 2, 1));
            L.Add(Armor(129, "Tracker's Boots", "INV_Boots_06", 9977, 1, 8, 1, 1));
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

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 3 shoulders, 8 feet, 9 wrists, 16 back.
            L.Add(Stats(Armor(710, "Anvilmar Wool Cuffs", "INV_Bracer_11", 16936, 1, 9, 2, 8), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(763, "Ramhide Bracers", "INV_Bracer_08", 17007, 1, 9, 4, 9), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(1182, "Mountaineer's Chain Bracers", "INV_Bracer_07", 6852, 1, 9, 9, 10), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(80, "Snowtrail Slippers", "INV_Boots_09", 16854, 1, 8, 3, 9), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(210, "Trogg-Hunter's Boots", "INV_Boots_06", 14444, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(287, "Coldridge Chain Boots", "INV_Boots_01", 12745, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(1379, "Surveyor's Mantle", "INV_Shoulder_06", 5494, 2, 3, 5, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(1371, "Boarhide Shoulderpads", "INV_Shoulder_08", 5495, 2, 3, 9, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(1445, "Anvilmar Chain Spaulders", "INV_Shoulder_07", 10167, 2, 3, 20, 22), 1, 1, 0, 0, 0));
            L.Add(Stats(Armor(1270, "Snowdrift Cloak", "INV_Misc_Cape_15", 23113, 1, 16, 5, 10), 1, 0, 0, 0, 0));

            // Final quest weapons (choice).
            L.Add(Stats(Weapon(791, "Frostbitten Staff", "INV_Staff_02", 20334, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(816, "Coldridge Skinning Knife", "INV_Weapon_ShortBlade_01", 6472, 2, 13, 3, 3f, 6f, 1.7f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(853, "Anvilmar Hatchet", "INV_Axe_11", 22102, 2, 13, 3, 4f, 8f, 2.3f, 55), 2, 1, 0, 0, 0));
            L.Add(Stats(Weapon(1196, "Mountaineer's Greataxe", "INV_Axe_04", 22114, 2, 17, 1, 8f, 13f, 3.2f, 60), 1, 2, 0, 0, 0));
            L.Add(Stats(Weapon(2100, "Coldridge Longrifle", "INV_Weapon_Rifle_06", 8258, 2, 26, 0, 4f, 8f, 2.5f, 60), 0, 0, 2, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant). 118 = Shadowglen's definition.
            L.Add(new ItemDef { Entry = 2070, Name = "Anvilmar Hard Cheese", IconPath = @"Interface\Icons\INV_Misc_Food_06", DisplayId = 6353, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 117, Name = "Salted Ram Jerky", IconPath = @"Interface\Icons\INV_Misc_Food_16", DisplayId = 2473, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 159, Name = "Cold Spring Water", IconPath = @"Interface\Icons\INV_Drink_07", DisplayId = 18084, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 118, Name = "Minor Healing Draught", IconPath = @"Interface\Icons\INV_Potion_49", DisplayId = 15710, Quality = 1, HealAmount = 80, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality; Northshire's definitions). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands.
            L.Add(Armor(1425, "Frayed Leather Vest", "INV_Shirt_05", 14190, 0, 5, 7, 4));
            L.Add(Armor(1423, "Frayed Leather Pants", "INV_Pants_06", 14346, 0, 7, 6, 4));
            L.Add(Armor(1419, "Frayed Leather Boots", "INV_Boots_06", 14353, 0, 8, 4, 3));
            L.Add(Armor(1420, "Frayed Leather Bracers", "INV_Bracer_05", 4471, 0, 9, 2, 2));
            L.Add(Armor(1427, "Patched Cloth Shoes", "INV_Boots_09", 16798, 0, 8, 1, 2));
            L.Add(Armor(1430, "Patched Cloth Gloves", "INV_Gauntlets_18", 16797, 0, 10, 1, 2));
            L.Add(Armor(1730, "Tarnished Chain Belt", "INV_Belt_06", 6902, 0, 6, 6, 3));
            L.Add(Armor(1731, "Tarnished Chain Boots", "INV_Boots_01", 6903, 0, 8, 9, 4));

            // Invented quest items.
            L.Add(QuestItem(ItemCragBoarFlank, "Crag Boar Flank", "INV_Misc_Food_14", QuestBoarFlanks));
            L.Add(QuestItem(ItemStolenSurveyGear, "Stolen Surveying Gear", "INV_Misc_Gear_01", QuestScavengersInTheDig));
            L.Add(QuestItem(ItemSnowbrandFetish, "Snowbrand Frost Fetish", "INV_Misc_Idol_03", QuestFetishesOfFrost));
        }

        /// <summary>The item with this entry, or null (linear search; zone data is small).</summary>
        public static ItemDef FindItem(ZoneContent z, int entry)
        {
            foreach (var i in z.Items) if (i.Entry == entry) return i;
            return null;
        }
    }
}
