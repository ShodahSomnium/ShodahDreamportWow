using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Draenei start zone, Ammen Vale (Azuremyst Isle, map 530 Outland/Expansion01), as offline zone data for the local simulation.
    /// Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures, icons and item looks
    /// are NOT here - only ids and paths that the player's own game files resolve at runtime. All NPC names, item names, quest text and
    /// greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_D.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 11 (classes 1/2/3/5/7/8): map 530 zone 3526 (-3961.64, -13931.2, 100.615)
    ///   o 2.08364. That point lies on the broken platform WMO DRAENEI_ENTRYCRYPTBROKE (terrain below it: 99.57).
    /// - Graveyard = WorldSafeLocs.dbc id 918 "Azuremyst Isle, Ammen Vale" (-4123.139, -13660.072, 74.6) (terrain 74.60).
    /// - Every spawn/NPC position was picked by the harness probe ContentDPlace on the real terrain: z = the ADT terrain height at (x, y),
    ///   area 3526 Ammen Vale (NPCs may also stand in 3527 Crash Site, the ring around the big wreck in the middle of the vale), no water,
    ///   slope &lt;= 30 degrees, no WMO/M2 collision geometry within 1-1.5 yards and no roof overhead, each wander circle checked the same
    ///   way. The probe ContentD re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 11 (what AzerothCore's Player::Create equips), minus the
    ///   hearthstone (6948) and the hunter's arrows (2512).
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (Moth, LasherOrchid, BogBeast models; blood elf humanoid displays
    ///   for the infiltrators); Draenei NPC displays = CreatureDisplayInfoExtra race 11 with an existing baked texture; weapons =
    ///   ItemDisplayInfo ids whose model exists under Item\ObjectComponents.
    /// </summary>
    public static partial class AmmenVale
    {
        public const string ZoneName = "Ammen Vale";
        public const int MapOutland = 530;

        /// <summary>AreaTable ids: 3526 Ammen Vale and 3527 Crash Site (both children of 3524 Azuremyst Isle).</summary>
        public const int AreaAmmenVale = 3526, AreaCrashSite = 3527, AreaAzuremystIsle = 3524;
        public static readonly int[] AreaIds = { AreaAmmenVale, AreaCrashSite };

        // ---- creature entries (ours) ----
        public const int ValeMoth = 961001;
        public const int MutatedRootLasher = 961002;
        public const int VolatileMutation = 961003;
        public const int BloodElfInfiltrator = 961004;
        public const int VaelithDawnthief = 961005;

        // ---- NPC entries (ours) ----
        public const int ElderTaamun = 962001;        // main quest giver at the start (intro, class quests, pods, final)
        public const int ArtificerZhaaro = 962002;    // on the road below the start (moths, lashers)
        public const int BotanistNuraeli = 962003;    // west edge of the crash site (mutations)
        public const int VindicatorSelaar = 962004;   // south-west, watching the infiltrators
        public const int WarriorTrainer = 962011;
        public const int PaladinTrainer = 962012;
        public const int HunterTrainer = 962013;
        public const int PriestTrainer = 962015;
        public const int ShamanTrainer = 962017;
        public const int MageTrainer = 962018;
        public const int PeacekeeperVoraan = 962021;
        public const int PeacekeeperMaadun = 962022;
        public const int PeacekeeperIlaari = 962023;

        // ---- quest ids (ours) ----
        public const int QuestWakingAmongWreckage = 96001;
        public const int QuestMothsInTheLight = 96002;
        public const int QuestRootsOfTheBlight = 96003;
        public const int QuestScatteredPods = 96004;
        public const int QuestSeedsOfChange = 96005;
        public const int QuestWordToTheWatch = 96006;
        public const int QuestEyesInTheFerns = 96007;
        public const int QuestStolenShards = 96008;
        public const int QuestTheDawnthief = 96009;
        public const int QuestWarriorPath = 96011;
        public const int QuestPaladinPath = 96012;
        public const int QuestHunterPath = 96013;
        public const int QuestPriestPath = 96015;
        public const int QuestShamanPath = 96017;
        public const int QuestMagePath = 96018;

        // ---- invented quest items (Ammen Vale range 960000+) ----
        public const int ItemRootCutting = 960001;
        public const int ItemSporeGland = 960002;
        public const int ItemCrystalShard = 960003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapOutland,
                PlayerStart = new WowPos(-3961.64f, -13931.2f, 100.615f, 2.08364f),
                Graveyard = new WowPos(-4123.139f, -13660.072f, 74.6f, 2.08364f),
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

            // Class start weapons (CharStartOutfit race 11). InventoryType: 14 shield, 17 two-hand, 18 bag (quiver), 21 main hand,
            // 26 ranged (crossbow). SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip, 4 shield on the back.
            L.Add(Weapon(23346, "Worn Claymore", "INV_Sword_06", 2380, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2361, "Battered Maul", "INV_Hammer_15", 8690, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(12282, "Worn Battleaxe", "INV_Axe_09", 22291, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(23347, "Salvaged Crossbow", "INV_Weapon_Crossbow_02", 10671, 1, 26, 0, 2f, 5f, 2.3f, 1));
            L.Add(Weapon(3661, "Novice's Staff", "INV_Staff_21", 18530, 1, 17, 2, 3f, 6f, 2.9f, 1));
            L.Add(Weapon(36, "Worn Flanged Mace", "INV_Mace_03", 5194, 1, 21, 3, 2f, 4f, 2.0f, 1));
            L.Add(new ItemDef { Entry = 2362, Name = "Scuffed Round Shield", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 18730, Quality = 1, InventoryType = 14, SheatheType = 4, Armor = 6, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 2101, Name = "Worn Quiver", IconPath = @"Interface\Icons\INV_Misc_Quiver_09", DisplayId = 21328, Quality = 1, InventoryType = 18, SellPrice = 1 });

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 8 feet, 20 robe.
            L.Add(Armor(23473, "Survivor's Shirt", "INV_Shirt_05", 36124, 1, 4, 0, 1));
            L.Add(Armor(23474, "Survivor's Trousers", "INV_Pants_02", 36125, 1, 7, 2, 1));
            L.Add(Armor(23475, "Survivor's Boots", "INV_Boots_06", 36126, 1, 8, 1, 1));
            L.Add(Armor(23476, "Initiate's Shirt", "INV_Shirt_16", 36133, 1, 4, 0, 1));
            L.Add(Armor(23477, "Initiate's Trousers", "INV_Pants_01", 36134, 1, 7, 2, 1));
            L.Add(Armor(23345, "Wayfarer's Shirt", "INV_Shirt_14", 36118, 1, 4, 0, 1));
            L.Add(Armor(23344, "Wayfarer's Trousers", "INV_Pants_02", 36120, 1, 7, 2, 1));
            L.Add(Armor(23348, "Wayfarer's Boots", "INV_Boots_05", 36122, 1, 8, 1, 1));
            L.Add(Armor(1396, "Acolyte's Pants", "INV_Pants_01", 3260, 1, 7, 1, 1));
            L.Add(Armor(6097, "Acolyte's Shirt", "INV_Shirt_01", 2470, 1, 4, 0, 1));
            L.Add(Armor(23322, "Acolyte's Robe", "INV_Chest_Cloth_21", 36138, 1, 20, 2, 1));
            L.Add(Armor(23478, "Apprentice's Pants", "INV_Pants_02", 36125, 1, 7, 1, 1));
            L.Add(Armor(23479, "Apprentice's Robe", "INV_Chest_Cloth_23", 36140, 1, 20, 2, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 8 feet, 9 wrists, 10 hands, 20 robe.
            L.Add(Stats(Armor(23413, "Vale-Spun Slippers", "INV_Boots_05", 14552, 1, 8, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(23414, "Mossgrip Boots", "INV_Boots_05", 14276, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(23412, "Wreckwalker Sabatons", "INV_Boots_01", 10434, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(23407, "Vale-Spun Cuffs", "INV_Bracer_07", 16805, 1, 9, 2, 8), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(23408, "Mossgrip Bracers", "INV_Bracer_07", 17014, 1, 9, 4, 9), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(23406, "Wreckwalker Bracers", "INV_Bracer_01", 25766, 1, 9, 9, 10), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(23399, "Vale-Spun Robe", "INV_Chest_Cloth_36", 19011, 2, 20, 7, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(23405, "Mossgrip Jerkin", "INV_Chest_Leather_09", 9502, 2, 5, 14, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(23490, "Wreckwalker Hauberk", "INV_Chest_Chain_14", 38327, 2, 5, 28, 22), 1, 1, 0, 0, 0));
            L.Add(Stats(Armor(23397, "Vale-Spun Gloves", "INV_Gauntlets_05", 16817, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(20993, "Mossgrip Gloves", "INV_Gauntlets_05", 36955, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(23376, "Wreckwalker Gauntlets", "INV_Gauntlets_04", 6954, 1, 10, 11, 12), 0, 1, 0, 0, 0));

            // Final quest weapons (choice).
            L.Add(Stats(Weapon(23416, "Staff of the Mended Light", "INV_Staff_01", 35945, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(23392, "Peacekeeper's Hammer", "INV_Hammer_16", 19772, 2, 21, 3, 4f, 8f, 2.4f, 60), 1, 2, 0, 0, 0));
            L.Add(Stats(Weapon(23409, "Azure Watch Longbow", "INV_Weapon_Bow_02", 20550, 2, 15, 0, 4f, 8f, 2.6f, 55), 0, 0, 2, 0, 0));
            L.Add(Stats(Weapon(23423, "Exodar Greatblade", "INV_Sword_23", 35962, 2, 17, 1, 8f, 13f, 3.2f, 60), 1, 2, 0, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant).
            L.Add(new ItemDef { Entry = 4536, Name = "Sun-Warmed Apple", IconPath = @"Interface\Icons\INV_Misc_Food_19", DisplayId = 6410, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 1179, Name = "Cool Elekk Milk", IconPath = @"Interface\Icons\INV_Drink_Milk_01", DisplayId = 18090, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 858, Name = "Crystal-Steeped Draught", IconPath = @"Interface\Icons\INV_Potion_50", DisplayId = 15711, Quality = 1, HealAmount = 100, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 8 feet, 9 wrists, 10 hands, 14 shield, 16 back.
            L.Add(Armor(1368, "Scorched Leather Gloves", "INV_Gauntlets_18", 17184, 0, 10, 2, 2));
            L.Add(Armor(1369, "Scorched Leather Belt", "INV_Belt_24", 14335, 0, 6, 2, 2));
            L.Add(Armor(1372, "Singed Cloak", "INV_Misc_Cape_10", 23054, 0, 16, 2, 3));
            L.Add(Armor(1374, "Singed Sandals", "INV_Boots_09", 16659, 0, 8, 1, 2));
            L.Add(Armor(3365, "Singed Wristwraps", "INV_Bracer_10", 16664, 0, 9, 1, 2));
            L.Add(Armor(1425, "Scorched Leather Vest", "INV_Shirt_05", 14190, 0, 5, 5, 4));
            L.Add(Armor(1433, "Singed Tunic", "INV_Shirt_06", 16795, 0, 5, 3, 4));
            L.Add(new ItemDef { Entry = 2210, Name = "Dented Buckler", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 2552, Quality = 0, InventoryType = 14, SheatheType = 4, Armor = 5, SellPrice = 3 });

            // Invented quest items.
            L.Add(QuestItem(ItemRootCutting, "Tainted Root Cutting", "INV_Misc_Root_02", QuestRootsOfTheBlight));
            L.Add(QuestItem(ItemSporeGland, "Unstable Spore Gland", "INV_Misc_Organ_03", QuestSeedsOfChange));
            L.Add(QuestItem(ItemCrystalShard, "Stolen Crystal Shard", "INV_Misc_Gem_Crystal_01", QuestStolenShards));
        }

        /// <summary>The item with this entry, or null (linear search; zone data is small).</summary>
        public static ItemDef FindItem(ZoneContent z, int entry)
        {
            foreach (var i in z.Items) if (i.Entry == entry) return i;
            return null;
        }
    }
}
