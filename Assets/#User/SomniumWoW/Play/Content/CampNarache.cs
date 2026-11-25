using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    /// <summary>
    /// The Tauren start zone, Camp Narache on Red Cloud Mesa (Mulgore, map 1 Kalimdor), as offline zone data for the local
    /// simulation. Pure data (no file reads): creature/NPC templates, spawns, quests, items and class starts. Models, textures,
    /// icons and item looks are NOT here - only ids and paths that the player's own game files resolve at runtime. All names of
    /// NPCs and items, quest text and greetings are our own writing.
    ///
    /// Where the numbers come from (details: scratchpad reports3/zones_C.md):
    /// - PlayerStart = AzerothCore playercreateinfo race 6 (classes 1/3/7/11): map 1 zone 215 (-2917.58, -257.98, 52.9968) o 0.
    /// - Graveyard = WorldSafeLocs.dbc id 34 "Mulgore, Red Cloud Mesa" (-2944.5564, -153.21503, 65.786026), beside the burial pyres.
    /// - Classes = CharBaseInfo.dbc race 6: Warrior, Hunter, Shaman, Druid (Death Knight not offered).
    /// - Every spawn/NPC position was picked by the harness probe NaracheSpots/NarachePlace (P/harness/Probes/ContentC.cs) on the
    ///   real terrain: z = the ADT terrain height at (x, y), area 220 Red Cloud Mesa (NPCs may also stand in 221 Camp Narache,
    ///   the huts' subzone), no water, slope &lt;= 30 degrees, no WMO/M2 collision geometry within 1-1.5 yards (the huts, windbreak
    ///   fences, pyres, lamps, trees), and each wander circle checked the same way. The probe NaracheCheck re-validates all of it.
    /// - Class start items = the client's CharStartOutfit.dbc rows for race 6 (what AzerothCore's Player::Create equips), minus the
    ///   hearthstone (6948, no inn offline) and the hunter's bullets (2516, ammo is not simulated).
    /// - Display ids: CreatureDisplayInfo rows whose model/skin files exist (TallStrider, Lion, KodoBeast, Boar, QuillBoar,
    ///   QuillBoarCaster, QuillBoarWarrior models); Tauren NPC displays = CreatureDisplayInfoExtra race 6 with an existing baked
    ///   texture; weapons = ItemDisplayInfo ids whose model exists under Item\ObjectComponents.
    /// Invented ids live in the Camp Narache range: creatures 951xxx, NPCs 952xxx, quest items 950001+, quests 95001+.
    /// </summary>
    public static partial class CampNarache
    {
        public const string ZoneName = "Camp Narache";
        public const int MapKalimdor = 1;

        /// <summary>AreaTable ids: 221 Camp Narache (the huts), 220 Red Cloud Mesa (the plateau around it), both children of 215 Mulgore.</summary>
        public const int AreaCampNarache = 221, AreaRedCloudMesa = 220, AreaMulgore = 215;
        public static readonly int[] AreaIds = { AreaCampNarache, AreaRedCloudMesa };

        // ---- creature entries (ours) ----
        public const int Plainstrider = 951001;
        public const int MountainCougar = 951002;
        public const int KodoCalf = 951003;
        public const int Battleboar = 951004;
        public const int BristlebackQuilboar = 951005;
        public const int BristlebackMystic = 951006;
        public const int ChiefBramblehide = 951007;

        // ---- NPC entries (ours) ----
        public const int ElderTayanka = 952001;        // main quest giver at the start (intro, class quests, the rim, final)
        public const int HuntLeaderWahkeen = 952002;   // plainstriders and cougars
        public const int KeeperAponi = 952003;         // at the burial pyres by the graveyard (west)
        public const int ScoutTala = 952004;           // the eastern watch, facing the quilboar camp
        public const int WarriorTrainer = 952011;
        public const int HunterTrainer = 952012;
        public const int ShamanTrainer = 952013;
        public const int DruidTrainer = 952014;
        public const int BraveAhiga = 952021;
        public const int BraveSakima = 952022;
        public const int BraveOnawa = 952023;

        // ---- quest ids (ours) ----
        public const int QuestMorningOnTheMesa = 95001;
        public const int QuestFirstHunt = 95002;
        public const int QuestCougarPelts = 95003;
        public const int QuestViewFromTheRim = 95004;
        public const int QuestTramplersTusks = 95005;
        public const int QuestSmokeInTheEast = 95006;
        public const int QuestBristlebackIncursion = 95007;
        public const int QuestThornFetishes = 95008;
        public const int QuestChiefBramblehide = 95009;
        public const int QuestWarriorPath = 95011;
        public const int QuestHunterPath = 95012;
        public const int QuestShamanPath = 95013;
        public const int QuestDruidPath = 95014;

        // ---- invented quest items (Camp Narache range 950000+) ----
        public const int ItemCougarPelt = 950001;
        public const int ItemBattleboarTusk = 950002;
        public const int ItemThornFetish = 950003;

        /// <summary>A fresh copy of the zone (callers may modify it).</summary>
        public static ZoneContent Build()
        {
            var z = new ZoneContent
            {
                Name = ZoneName,
                MapId = MapKalimdor,
                PlayerStart = new WowPos(-2917.58f, -257.98f, 52.9968f, 0f),
                Graveyard = new WowPos(-2944.5564f, -153.21503f, 65.786026f, 4.9f),
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
        // re-checked by the NaracheCheck probe) with our own name and simple stats; icons = ItemDisplayInfo.InventoryIcon.
        // Entries were chosen apart from Shadowglen's so a merged item table would never see two names for one id.
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

            // Class start weapons (CharStartOutfit race 6). InventoryType: 14 shield, 17 two-hand, 21 main hand, 26 ranged (gun).
            // SheatheType: 1 two-hand on the back, 2 staff on the back, 3 one-hand at the hip, 4 shield on the back.
            L.Add(Weapon(2361, "Battleworn Maul", "INV_Hammer_15", 8690, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(12282, "Worn Plains Axe", "INV_Axe_09", 22291, 1, 17, 1, 5f, 9f, 3.3f, 1));
            L.Add(Weapon(2508, "Old Thunder-Rifle", "INV_Weapon_Rifle_01", 6606, 1, 26, 0, 2f, 5f, 2.3f, 1));
            L.Add(Weapon(36, "Worn Totem Mace", "INV_Mace_03", 5194, 1, 21, 3, 2f, 5f, 1.9f, 1));
            L.Add(Weapon(35, "Bent Oak Staff", "INV_Staff_08", 472, 1, 17, 2, 3f, 6f, 2.9f, 1));
            L.Add(new ItemDef { Entry = 2362, Name = "Stitched Hide Shield", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 18730, Quality = 1, InventoryType = 14, SheatheType = 4, Armor = 7, SellPrice = 1 });
            // Hunter's ammo pouch (Item.dbc class 11 ammo pouch, InventoryType 18 = a bag, not an equipment slot).
            L.Add(new ItemDef { Entry = 2102, Name = "Hide Shot Pouch", IconPath = @"Interface\Icons\INV_Misc_Ammo_Bullet_01", DisplayId = 1816, Quality = 1, InventoryType = 18, SellPrice = 1 });

            // Class start clothes. InventoryType: 4 shirt, 7 legs, 20 robe.
            L.Add(Armor(6125, "Brave's Hide Jerkin", "INV_Chest_Leather_04", 9995, 1, 4, 0, 1));
            L.Add(Armor(139, "Brave's Leggings", "INV_Pants_02", 9988, 1, 7, 2, 1));
            L.Add(Armor(127, "Plainsrunner's Shirt", "INV_Shirt_07", 9996, 1, 4, 0, 1));
            L.Add(Armor(6126, "Plainsrunner's Leggings", "INV_Pants_02", 10002, 1, 7, 2, 1));
            L.Add(Armor(154, "Earthcaller's Mantle-Shirt", "INV_Misc_Cape_14", 10058, 1, 4, 0, 1));
            L.Add(Armor(153, "Earthcaller's Kilt", "INV_Pants_02", 10050, 1, 7, 2, 1));
            L.Add(Armor(6139, "Mesa Druid's Robe", "INV_Chest_Cloth_13", 12684, 1, 20, 2, 1));
            L.Add(Armor(6124, "Mesa Druid's Leggings", "INV_Pants_01", 9987, 1, 7, 1, 1));

            // Quest reward armour (choice of cloth / leather / mail). InventoryType: 5 chest, 7 legs, 8 feet, 10 hands.
            L.Add(Stats(Armor(2578, "Windweave Tunic", "INV_Chest_Fur", 10891, 1, 5, 6, 12), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(1794, "Cougar-Hide Vest", "INV_Chest_Wolf", 14272, 1, 5, 12, 14), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2016, "Mesa Warden's Hauberk", "INV_Chest_Chain", 1727, 1, 5, 25, 16), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2583, "Windweave Sandals", "INV_Boots_09", 13524, 1, 8, 3, 9), 0, 0, 0, 0, 1));
            L.Add(Stats(Armor(1788, "Plainstalker Boots", "INV_Boots_Wolf", 16990, 1, 8, 6, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2037, "Mesa Warden's Sabatons", "INV_Boots_01", 11447, 1, 8, 13, 12), 1, 0, 0, 0, 0));
            L.Add(Stats(Armor(2158, "Windweave Wraps", "INV_Gauntlets_23", 14478, 1, 10, 3, 9), 0, 0, 0, 1, 0));
            L.Add(Stats(Armor(1791, "Plainstalker Grips", "INV_Gauntlets_05", 972, 1, 10, 5, 10), 0, 0, 1, 0, 0));
            L.Add(Stats(Armor(2230, "Mesa Warden's Gauntlets", "INV_Gauntlets_05", 6930, 1, 10, 11, 12), 0, 1, 0, 0, 0));
            L.Add(Stats(Armor(2159, "Windweave Trousers", "INV_Pants_07", 14479, 2, 7, 5, 18), 0, 0, 0, 1, 1));
            L.Add(Stats(Armor(1792, "Plainstalker Breeches", "INV_Pants_Wolf", 6731, 2, 7, 10, 20), 1, 0, 1, 0, 0));
            L.Add(Stats(Armor(2152, "Mesa Warden's Legguards", "INV_Pants_03", 2989, 2, 7, 22, 22), 1, 1, 0, 0, 0));

            // Final quest weapons (choice): a maul for warriors, a mace for shamans, a staff for druids, a rifle for hunters.
            L.Add(Stats(Weapon(1197, "Tuskbreaker Maul", "INV_Mace_02", 5226, 2, 17, 1, 7f, 12f, 3.2f, 60), 1, 2, 0, 0, 0));
            L.Add(Stats(Weapon(766, "Earthmother's Cudgel", "INV_Mace_07", 19621, 2, 21, 3, 3f, 7f, 2.0f, 55), 1, 0, 0, 1, 1));
            L.Add(Stats(Weapon(1159, "Staff of the Red Cloud", "INV_Staff_08", 4994, 2, 17, 2, 7f, 12f, 3.0f, 60), 0, 0, 0, 2, 2));
            L.Add(Stats(Weapon(2509, "Longhorn Rifle", "INV_Weapon_Rifle_02", 6607, 2, 26, 0, 4f, 8f, 2.6f, 55), 0, 0, 2, 0, 0));

            // Consumables (restored over UseSeconds; 0 = instant).
            L.Add(new ItemDef { Entry = 117, Name = "Plains Jerky", IconPath = @"Interface\Icons\INV_Misc_Food_16", DisplayId = 2473, Quality = 1, HealAmount = 61, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 2287, Name = "Roasted Strider Haunch", IconPath = @"Interface\Icons\INV_Misc_Food_18", DisplayId = 2474, Quality = 1, HealAmount = 90, UseSeconds = 18f, SellPrice = 2 });
            L.Add(new ItemDef { Entry = 1179, Name = "Fresh Kodo Milk", IconPath = @"Interface\Icons\INV_Drink_Milk_01", DisplayId = 18090, Quality = 1, ManaAmount = 151, UseSeconds = 18f, SellPrice = 1 });
            L.Add(new ItemDef { Entry = 858, Name = "Earthroot Draught", IconPath = @"Interface\Icons\INV_Potion_50", DisplayId = 15711, Quality = 1, HealAmount = 90, UseSeconds = 0f, SellPrice = 5 });

            // Occasional drops (poor quality). InventoryType: 5 chest, 6 waist, 7 legs, 8 feet, 9 wrists, 10 hands, 14 shield, 16 back.
            L.Add(Armor(1368, "Scuffed Leather Gloves", "INV_Gauntlets_18", 17184, 0, 10, 2, 2));
            L.Add(Armor(1369, "Scuffed Leather Belt", "INV_Belt_24", 14335, 0, 6, 2, 2));
            L.Add(Armor(1372, "Moth-Eaten Cloak", "INV_Misc_Cape_10", 23054, 0, 16, 2, 2));
            L.Add(Armor(1374, "Worn Plains Sandals", "INV_Boots_09", 16659, 0, 8, 1, 2));
            L.Add(Armor(1376, "Frayed Hide Cape", "INV_Misc_Cape_10", 23090, 0, 16, 2, 2));
            L.Add(Armor(1395, "Patched Cloth Leggings", "INV_Pants_01", 9924, 0, 7, 2, 3));
            L.Add(Armor(2642, "Loose Ringmail Boots", "INV_Boots_01", 6903, 0, 8, 9, 4));
            L.Add(Armor(2643, "Loose Ringmail Bracers", "INV_Bracer_03", 6904, 0, 9, 5, 3));
            L.Add(Armor(2645, "Loose Ringmail Gloves", "INV_Gauntlets_04", 6905, 0, 10, 7, 3));
            L.Add(Armor(2646, "Loose Ringmail Leggings", "INV_Pants_03", 2217, 0, 7, 10, 4));
            L.Add(new ItemDef { Entry = 2210, Name = "Splintered Buckler", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 2552, Quality = 0, InventoryType = 14, SheatheType = 4, Armor = 9, SellPrice = 3 });
            L.Add(new ItemDef { Entry = 2211, Name = "Quilboar War-Shield", IconPath = @"Interface\Icons\INV_Shield_09", DisplayId = 18656, Quality = 0, InventoryType = 14, SheatheType = 4, Armor = 12, SellPrice = 4 });

            // Invented quest items.
            L.Add(QuestItem(ItemCougarPelt, "Mountain Cougar Pelt", "INV_Misc_Pelt_Bear_01", QuestCougarPelts));
            L.Add(QuestItem(ItemBattleboarTusk, "Battleboar Tusk", "INV_Misc_Bone_08", QuestTramplersTusks));
            L.Add(QuestItem(ItemThornFetish, "Bristleback Thorn Fetish", "INV_Misc_Idol_02", QuestThornFetishes));
        }

        /// <summary>The item with this entry, or null (linear search; zone data is small).</summary>
        public static ItemDef FindItem(ZoneContent z, int entry)
        {
            foreach (var i in z.Items) if (i.Entry == entry) return i;
            return null;
        }
    }
}
