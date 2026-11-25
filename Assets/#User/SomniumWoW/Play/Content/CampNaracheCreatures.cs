using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class CampNarache
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin; "scale" = display scale x model scale, measured by the harness;
        // skins picked by decoding them, P/harness/out_zonesC/nc_skins1..3.png):
        //   TallStrider.mdx:      1219 TallStriderSkinBrown 0.7, 1041 Brown 0.8, 1220 TallStriderSkinGray 0.85 (young plainstriders).
        //   Lion.mdx:             1972 LionSkinGold 0.9, 1971 Gold 0.95 (tawny mountain cougars).
        //   KodoBeast.mdx:        1451 KotoBeastSkinGray 0.85, 1454 KotoBeastSkinBrown 0.85 (calves).
        //   Boar.mdx:             704 BoarSkinBrown 1.0, 3027 BoarSkinCrimson 1.0, 1208 Brown 1.15 (battleboars).
        //   QuillBoar.mdx:        1254 QuilBoarSkin 0.9, 1274 QuilBoarSkin 1.0, 1252 QuilBoarSkinGrey 0.9.
        //   QuillBoarCaster.mdx:  4647 QuilBoarCasterSkinBeige 1.0 (green robe), 6090 QuilBoarCasterSkinBrown 0.9 (blue robe).
        //   QuillBoarWarrior.mdx: 4653 QuilBoarSkinArmoredBrown 1.1 (the chief, x1.1 template scale).
        static CreatureTemplate Beast(int entry, string name, int[] displays, int minLevel, int maxLevel, Reaction reaction, float aggro)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, DisplayIds = displays, MinLevel = minLevel, MaxLevel = maxLevel, Reaction = reaction,
                AggroRadius = aggro, WalkSpeed = 2.5f, RunSpeed = 7f, AttackSpeed = 2f, CreatureType = 1,
            };
        }

        static LootDef Drop(int item, float chance) { return new LootDef { ItemEntry = item, Chance = chance }; }
        static LootDef QuestDrop(int item, float chance, int quest) { return new LootDef { ItemEntry = item, Chance = chance, QuestId = quest }; }

        // Spell.dbc ids the quilboar use (read from the client's Spell.dbc by the harness, probe NaracheSpells):
        //   403 'Lightning Bolt' Rank 1: school damage 13..15 nature, 1.5 s cast, 30 yd, missile speed 20.
        //   772 'Rend' Rank 1: periodic physical damage 5 every 3 s for 15 s, melee range.
        const int SpellLightningBolt = 403, SpellRend = 772;

        static void AddCreatures(ZoneContent z)
        {
            var L = z.Creatures;

            // Around the camp: skittish young striders that peck at anything that comes close.
            var strider = Beast(Plainstrider, "Plainstrider", new[] { 1219, 1041, 1220 }, 1, 2, Reaction.Hostile, 8f);
            strider.DamageMultiplier = 0.9f; strider.RunSpeed = 8f; strider.MinMoney = 0; strider.MaxMoney = 2;
            strider.Loot.Add(Drop(117, 0.05f));
            strider.Loot.Add(Drop(1374, 0.02f));
            L.Add(strider);

            // The western and south-western slopes: the mesa's big cats.
            var cougar = Beast(MountainCougar, "Mountain Cougar", new[] { 1972, 1971 }, 2, 3, Reaction.Hostile, 10f);
            cougar.AttackSpeed = 1.8f; cougar.MinMoney = 1; cougar.MaxMoney = 4;
            cougar.Loot.Add(QuestDrop(ItemCougarPelt, 0.7f, QuestCougarPelts));
            cougar.Loot.Add(Drop(1368, 0.02f));
            cougar.Loot.Add(Drop(1369, 0.02f));
            L.Add(cougar);

            // Peaceful calves grazing north-east of the camp (neutral: they only fight back).
            var calf = Beast(KodoCalf, "Kodo Calf", new[] { 1451, 1454 }, 2, 2, Reaction.Neutral, 0f);
            calf.HealthMultiplier = 1.3f; calf.DamageMultiplier = 0.8f; calf.WalkSpeed = 2f; calf.RunSpeed = 6f; calf.AttackSpeed = 2.4f;
            L.Add(calf);

            // The southern plains: boars driven up the mesa by the quilboar.
            var boar = Beast(Battleboar, "Battleboar", new[] { 704, 3027, 1208 }, 3, 4, Reaction.Hostile, 10f);
            boar.HealthMultiplier = 1.1f; boar.MinMoney = 1; boar.MaxMoney = 5;
            boar.Loot.Add(QuestDrop(ItemBattleboarTusk, 0.65f, QuestTramplersTusks));
            boar.Loot.Add(Drop(2287, 0.04f));
            boar.Loot.Add(Drop(1372, 0.02f));
            boar.Loot.Add(Drop(2642, 0.02f));
            L.Add(boar);

            // The quilboar camp on the eastern plain, below Kodo Rock.
            var quil = Beast(BristlebackQuilboar, "Bristleback Quilboar", new[] { 1254, 1274, 1252 }, 4, 5, Reaction.Hostile, 12f);
            quil.CreatureType = 7; quil.MinMoney = 2; quil.MaxMoney = 7;
            quil.Loot.Add(QuestDrop(ItemThornFetish, 0.6f, QuestThornFetishes));
            quil.Loot.Add(Drop(858, 0.03f));
            quil.Loot.Add(Drop(2645, 0.02f));
            quil.Loot.Add(Drop(2646, 0.02f));
            quil.Loot.Add(Drop(2210, 0.02f));
            L.Add(quil);

            var mystic = Beast(BristlebackMystic, "Bristleback Mystic", new[] { 4647, 6090 }, 4, 5, Reaction.Hostile, 12f);
            mystic.CreatureType = 7; mystic.UnitClass = 8; mystic.Spells = new[] { SpellLightningBolt }; mystic.SpellInterval = 7f;
            mystic.MinMoney = 3; mystic.MaxMoney = 8;
            mystic.Loot.Add(QuestDrop(ItemThornFetish, 0.6f, QuestThornFetishes));
            mystic.Loot.Add(Drop(1179, 0.05f));
            mystic.Loot.Add(Drop(1395, 0.02f));
            mystic.Loot.Add(Drop(1376, 0.02f));
            L.Add(mystic);

            // The named quilboar at the heart of the camp (final quest). Tougher than anything else on the mesa.
            var chief = Beast(ChiefBramblehide, "Chief Bramblehide", new[] { 4653 }, 5, 5, Reaction.Hostile, 15f);
            chief.CreatureType = 7; chief.Scale = 1.1f; chief.HealthMultiplier = 3f; chief.DamageMultiplier = 1.4f;
            chief.Spells = new[] { SpellRend }; chief.SpellInterval = 10f;
            chief.MinMoney = 60; chief.MaxMoney = 110;
            chief.Loot.Add(Drop(858, 0.5f));
            chief.Loot.Add(Drop(2643, 0.25f));
            chief.Loot.Add(Drop(2211, 0.25f));
            L.Add(chief);
        }

        // Tauren NPC displays: CreatureDisplayInfoExtra race 6 (TaurenMale.m2 / TaurenFemale.m2) with an existing
        // Textures\BakedNpcTextures\ file (all 810 race-6 displays have one); picked by looking at the baked textures
        // (P/harness/out_zonesC/nc_bm1.png, nc_bm2.png, nc_bf1.png):
        //   2082 m brown fur, teal-and-gold elder garb; 3442 m brown fur, hunter's harness; 2107 f spotted fur, tribal tunic;
        //   2113 f brown fur, scale mail; 2096 m black fur, gold chest plate; 3812 m brown fur, olive fur vest; 2141 m red leather
        //   with green trim; 3810 m grey fur, green leathers; 2098 m orange harness with a gold plate; 2087 m white fur, red harness;
        //   2132 f dark fur, chain mail.
        // Held weapons (ItemDisplayInfo -> model): 472 Stave_2H_Long_A_01, 1733 Polearm_2H_Bladed_A_01, 22291 Axe_2H_Battle_A_01,
        //   25627 Hammer_2H_Horde_A_03, 6606 Firearm_2H_Rifle_A_01, 19869 Mace_1H_Tauren_B_01, 18730 Shield_Round_A_01,
        //   4994 Stave_2H_Long_A_03, 5500 Axe_2H_Battle_A_01 (rusty), 3702 Polearm_2H_Bladed_B_01.
        static CreatureTemplate Npc(int entry, string name, string subName, int display, int level, string greeting, int mainHand, int offHand)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, SubName = subName, DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level,
                Reaction = Reaction.Friendly, IsNpc = true, AggroRadius = 0f, Greeting = greeting, CreatureType = 7,
                MainHandDisplayId = mainHand, OffHandDisplayId = offHand,
            };
        }

        const string TrainerGrowth = " New skills will come to you as you grow; return to me whenever you wish, and I will show you how to use them.";

        static void AddNpcs(ZoneContent z)
        {
            var L = z.Creatures;

            L.Add(Npc(ElderTayanka, "Tayanka Sunmane", "Elder of the Mesa", 2082, 10,
                "Welcome, young one. The Earth Mother has woken you on Red Cloud Mesa, where every tauren takes their first steps. Feel the wind? It has come a long way to meet you. Listen to it - and to me - and you will not lose your way.",
                472, 0));
            L.Add(Npc(HuntLeaderWahkeen, "Wahkeen Plainsrunner", "Hunt Leader", 3442, 10,
                "A new pair of hooves on the mesa. Good. The hunt feeds the camp, and the camp is always hungry. Ready to run?",
                1733, 0));
            L.Add(Npc(KeeperAponi, "Aponi Mistcaller", "Keeper of the Pyres", 2107, 10,
                "Speak softly here. The ancestors rest on these pyres, and they have been restless of late. Walk with respect, and they will watch over you.",
                0, 0));
            L.Add(Npc(ScoutTala, "Tala Thornwatch", "Eastern Watch", 2113, 12,
                "Keep low and follow my eyes. See the smoke past the grass? That is no tauren fire. The Bristleback have come up out of their ravine.",
                22291, 0));

            L.Add(Npc(WarriorTrainer, "Hoskan Ironmane", "Warrior Trainer", 2096, 12,
                "Strength is a gift from the Earth Mother; courage you must bring yourself. Stand your ground and let them break upon you like water on stone." + TrainerGrowth,
                25627, 0));
            L.Add(Npc(HunterTrainer, "Chayton Longsight", "Hunter Trainer", 3812, 12,
                "Watch how the grass moves before the cougar does. A hunter who sees first seldom has to run." + TrainerGrowth,
                6606, 0));
            L.Add(Npc(ShamanTrainer, "Makya Earthsong", "Shaman Trainer", 2141, 12,
                "Earth, fire, water and air - they speak to all of us, but only a few learn to answer. I hear them stirring around you." + TrainerGrowth,
                19869, 18730));
            L.Add(Npc(DruidTrainer, "Enapay Greenbough", "Druid Trainer", 3810, 12,
                "The grass bends, the rain falls, the herd moves on. Everything on the mesa keeps the great balance - and so shall you." + TrainerGrowth,
                4994, 0));

            L.Add(Npc(BraveAhiga, "Ahiga Tallgrass", "Brave", 2098, 30,
                "The camp is safe while the braves keep watch. Rest your hooves by the fire when the day is done.",
                1733, 0));
            L.Add(Npc(BraveSakima, "Sakima Windhoof", "Brave", 2087, 30,
                "This road winds west around the hills and down off the mesa to the rest of Mulgore. Grow strong here first; the plains below are wider than they look.",
                5500, 0));
            L.Add(Npc(BraveOnawa, "Onawa Dustmane", "Brave", 2132, 30,
                "Boars have been running up from the southern grass all week, and they do not stop for anyone. Mind your footing out there.",
                3702, 0));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
