using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class ColdridgeValley
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin, display scale; files checked by the harness):
        //   Wolf.mdx:          785 WolfSkinArctic 0.7, 801 WolfSkinArctic 0.85 (young snow wolves).
        //   Boar.mdx:          381 BoarSkinBlue 0.75, 607 BoarSkinBlue 0.6 (small crag boars).
        //   Troglodyte.mdx:    764 TroglodyteSkinNormal 0.7, 763 Normal 0.85 (scavengers); 765 Normal 1.0, 722 Normal 1.15 (burly).
        //   Troll.mdx:         605 TrollSkinArcticFrostManeScout 0.8, 1031 Scout 0.75, 636 Melee 0.9 (youngbloods);
        //                      661 Caster 0.8, 1032 Caster 0.75 (seers).
        //   TrollForestBoss.mdx: 27459 TrollSkinArcticFrostManeBoss 1.1 (the tribe's leader, final quest).
        // CreatureType (CreatureType.dbc): 1 Beast, 7 Humanoid (troggs and trolls).
        // Spells: 116 = Frostbolt (Rank 1) in Spell.dbc, cast by the seers and their leader (checked by the ZaCheck probe).
        const int SpellFrostbolt = 116;

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

        static void AddCreatures(ZoneContent z)
        {
            var L = z.Creatures;

            var wolf = Beast(YoungSnowWolf, "Young Snow Wolf", new[] { 785, 801 }, 1, 2, Reaction.Hostile, 8f);
            wolf.DamageMultiplier = 0.9f; wolf.MinMoney = 0; wolf.MaxMoney = 2;
            wolf.Loot.Add(Drop(117, 0.04f));
            wolf.Loot.Add(Drop(1419, 0.02f));
            L.Add(wolf);

            var boar = Beast(SmallCragBoar, "Small Crag Boar", new[] { 381, 607 }, 2, 3, Reaction.Hostile, 9f);
            boar.MinMoney = 1; boar.MaxMoney = 4; boar.HealthMultiplier = 1.1f;
            boar.Loot.Add(QuestDrop(ItemCragBoarFlank, 0.7f, QuestBoarFlanks));
            boar.Loot.Add(Drop(1420, 0.02f));
            boar.Loot.Add(Drop(1423, 0.02f));
            L.Add(boar);

            var scav = Beast(TroggScavenger, "Trogg Scavenger", new[] { 764, 763 }, 2, 3, Reaction.Hostile, 10f);
            scav.CreatureType = 7; scav.MinMoney = 1; scav.MaxMoney = 5;
            scav.Loot.Add(QuestDrop(ItemStolenSurveyGear, 0.6f, QuestScavengersInTheDig));
            scav.Loot.Add(Drop(2070, 0.05f));
            scav.Loot.Add(Drop(1427, 0.02f));
            L.Add(scav);

            var burly = Beast(BurlyTrogg, "Burly Trogg", new[] { 765, 722 }, 3, 4, Reaction.Hostile, 12f);
            burly.CreatureType = 7; burly.MinMoney = 3; burly.MaxMoney = 8; burly.AttackSpeed = 2.2f;
            burly.Loot.Add(Drop(118, 0.03f));
            burly.Loot.Add(Drop(1430, 0.02f));
            burly.Loot.Add(Drop(1731, 0.02f));
            L.Add(burly);

            var young = Beast(SnowbrandYoungblood, "Snowbrand Youngblood", new[] { 605, 1031, 636 }, 4, 5, Reaction.Hostile, 14f);
            young.CreatureType = 7; young.MinMoney = 4; young.MaxMoney = 10;
            young.Loot.Add(QuestDrop(ItemSnowbrandFetish, 0.5f, QuestFetishesOfFrost));
            young.Loot.Add(Drop(117, 0.05f));
            young.Loot.Add(Drop(1425, 0.02f));
            young.Loot.Add(Drop(1730, 0.02f));
            L.Add(young);

            // Casters: less health/damage (creature_classlevelstats class 8) and a Frostbolt every few seconds.
            var seer = Beast(SnowbrandSeer, "Snowbrand Seer", new[] { 661, 1032 }, 4, 5, Reaction.Hostile, 14f);
            seer.CreatureType = 7; seer.UnitClass = 8; seer.MinMoney = 5; seer.MaxMoney = 11;
            seer.Spells = new[] { SpellFrostbolt }; seer.SpellInterval = 7f;
            seer.Loot.Add(QuestDrop(ItemSnowbrandFetish, 0.5f, QuestFetishesOfFrost));
            seer.Loot.Add(Drop(159, 0.05f));
            seer.Loot.Add(Drop(1427, 0.02f));
            L.Add(seer);

            // The tribe's leader at the mouth of the ice cave (final quest). Tougher than anything else here.
            var boss = Beast(VakjinTheRimecaller, "Vakjin the Rimecaller", new[] { 27459 }, 5, 5, Reaction.Hostile, 15f);
            boss.CreatureType = 7; boss.Scale = 1.05f; boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.4f;
            boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.Spells = new[] { SpellFrostbolt }; boss.SpellInterval = 9f;
            boss.Loot.Add(Drop(118, 0.5f));
            boss.Loot.Add(Drop(1731, 0.25f));
            boss.Loot.Add(Drop(1425, 0.25f));
            L.Add(boss);
        }

        // Dwarf and gnome NPC displays: CreatureDisplayInfoExtra race 3 (DwarfMale/DwarfFemale) and race 7 (GnomeMale/GnomeFemale)
        // with an existing Textures\BakedNpcTextures\ file, picked by looking at the baked textures (harness ZaBakes):
        //   13326 m dwarf, blue-steel plate and helm; 1404 f dwarf, red shirt and green trousers; 20520 f gnome, green work armour;
        //   4965 f dwarf, chain; 3087 m dwarf, chain under a plate breastplate; 13637 f dwarf, blue-steel plate and helm; 1835 m
        //   dwarf, chain over dark leather; 28192 f gnome, dark purple leather; 22158 m dwarf, golden robe, white beard; 21310 f
        //   gnome, blue robe with a frost pattern; 12921 m gnome, dark red robe; 3524 m / 3526 m / 13250 f dwarf guards in
        //   red-orange plate.
        // Held items (ItemDisplayInfo, models exist): 22102 Axe_1H_Hatchet_A_03, 22291 Axe_2H_Battle_A_01, 19242 Axe_2H_War_A_01,
        //   6264 Axe_1H_Pick_A_01 (a pick), 6606 Firearm_2H_Rifle_A_01, 6595 Firearm_2H_Rifle_B_01, 1161 Mace_1H_Flanged_A_01,
        //   1705 Shield_Crest_A_01, 1644 Shield_Rectangle_B_01, 6442 Knife_1H_Dagger_A_01, 20334 Stave_2H_Long_B_03,
        //   1190 Stave_2H_Jeweled_D_01, 878 Stave_2H_Long_A_01.
        static CreatureTemplate Npc(int entry, string name, string subName, int display, int level, string greeting, int mainHand, int offHand)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, SubName = subName, DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level,
                Reaction = Reaction.Friendly, IsNpc = true, AggroRadius = 0f, Greeting = greeting, CreatureType = 7,
                MainHandDisplayId = mainHand, OffHandDisplayId = offHand,
            };
        }

        const string TrainerGrowth = " New skills will come to you as you grow; come back whenever you like, and I will show you how to use them.";

        static void AddNpcs(ZoneContent z)
        {
            var L = z.Creatures;

            L.Add(Npc(CaptainDorgan, "Dorgan Coalbrow", "Captain of Anvilmar", 13326, 12,
                "Welcome to Coldridge, lad or lass - mind the ice on the steps. Anvilmar has stood guard over this valley since before my grandfather's beard went grey, and it won't fall on my watch.",
                22102, 1705));
            L.Add(Npc(QuartermasterSenna, "Senna Copperkettle", "Anvilmar Quartermaster", 1404, 10,
                "If it's warm, filling or sharp, I've probably got it stacked somewhere. If I haven't, it means the wolves got to it first.",
                0, 0));
            L.Add(Npc(SurveyorFizzla, "Fizzla Sprocketwhistle", "Surveyor", 20520, 10,
                "Careful! You're standing on a perfectly good survey marker. Well - it WAS perfectly good, before the troggs chewed on it.",
                6264, 0));
            L.Add(Npc(MountaineerTharna, "Tharna Stonebrook", "Mountaineer", 4965, 11,
                "Keep low and keep quiet. From here you can smell the smoke of their fires on a still day - and it's been very still lately.",
                6606, 0));

            L.Add(Npc(WarriorTrainer, "Hurgan Oakbrand", "Warrior Trainer", 3087, 12,
                "Plant your feet like the mountain and swing like you mean it. Nobody ever won a fight by thinking about it too long." + TrainerGrowth,
                22291, 0));
            L.Add(Npc(PaladinTrainer, "Ysolde Brightanvil", "Paladin Trainer", 13637, 12,
                "The Light burns as bright in a forge as in any cathedral. Temper your faith the way we temper steel - heat, patience, and a good hammer." + TrainerGrowth,
                1161, 1705));
            L.Add(Npc(HunterTrainer, "Kelm Deepshot", "Hunter Trainer", 1835, 12,
                "Breathe out, squeeze, don't pull. And mind the wind - it comes down off the peaks fast enough to steal a bullet." + TrainerGrowth,
                6595, 0));
            L.Add(Npc(RogueTrainer, "Nimsy Quickcog", "Rogue Trainer", 28192, 12,
                "Big folk always look up for trouble. That's why we gnomes do so well down here, out of sight and out of reach." + TrainerGrowth,
                6442, 6442));
            L.Add(Npc(PriestTrainer, "Odrin Emberhall", "Priest Trainer", 22158, 12,
                "Sit, warm your hands. The Light is a hearth, child - it gives the most to those who keep it fed." + TrainerGrowth,
                20334, 0));
            L.Add(Npc(MageTrainer, "Pellia Frostspark", "Mage Trainer", 21310, 12,
                "Cold, heat, motion - it's all just energy looking for a clever mind to direct it. Try not to singe the dwarves' beards; they get terribly upset." + TrainerGrowth,
                1190, 0));
            L.Add(Npc(WarlockTrainer, "Grizwick Umbergear", "Warlock Trainer", 12921, 12,
                "Oh, don't mind the smell of sulphur. Every great engineer has a few experiments that talk back." + TrainerGrowth,
                878, 0));

            L.Add(Npc(GuardBorvak, "Borvak Granitehand", "Anvilmar Guard", 3524, 20,
                "Anvilmar's doors are open to friends. Anything with fangs or a club had best stay out in the snow.",
                19242, 0));
            L.Add(Npc(GuardDurm, "Durm Ashbeard", "Anvilmar Guard", 3526, 20,
                "This road runs out past the boar meadows and on to the old digs. Keep your wits about you once Anvilmar is out of sight.",
                22102, 1705));
            L.Add(Npc(GuardKelda, "Kelda Ironhold", "Anvilmar Guard", 13250, 20,
                "Beyond the pass lies the rest of Dun Morogh. Once the valley is quiet again, the mountain will be waiting for you.",
                1161, 1644));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
