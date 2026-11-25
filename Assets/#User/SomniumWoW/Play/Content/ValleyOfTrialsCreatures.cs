using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class ValleyOfTrials
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin, display scale; files checked by the harness):
        //   Boar.mdx:      704 BoarSkinBrown 1.0, 193 BoarSkinIvory 1.0 (mottled boars).
        //   Scorpion.mdx:  2485 ScorpionskinBeige 0.7, 2486 Beige 0.85 (workers); 2488 ScorpionskinBlack 1.0 (dusk scorpids).
        //   Imp.mdx:       10811 ImpSkinRed 0.5 (vile familiars).
        //   OrcMale/OrcFemale (CreatureDisplayInfoExtra race 2, baked textures exist): 1324 / 1326 / 1366 robes (neophytes),
        //                  1335 / 1368 (bandana) / 1370 leathers (thugs), 1323 robe + shirt (the cult leader).
        // Held weapons (ItemDisplayInfo, model exists): 472 / 878 Stave_2H_Long_A_01, 19126 Axe_1H_Horde_B_01.
        // Spells (Spell.dbc ids, names read from the client's Spell.dbc by the probe ContentB): 3110 Firebolt, 686 Shadow Bolt,
        //   772 Rend, 348 Immolate, 744 Poison.
        const int SpellFirebolt = 3110, SpellShadowBolt = 686, SpellRend = 772, SpellImmolate = 348, SpellPoison = 744;

        static CreatureTemplate Mob(int entry, string name, int[] displays, int minLevel, int maxLevel, Reaction reaction, float aggro, int creatureType)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, DisplayIds = displays, MinLevel = minLevel, MaxLevel = maxLevel, Reaction = reaction,
                AggroRadius = aggro, WalkSpeed = 2.5f, RunSpeed = 7f, AttackSpeed = 2f, CreatureType = creatureType,
            };
        }

        static LootDef Drop(int item, float chance) { return new LootDef { ItemEntry = item, Chance = chance }; }
        static LootDef QuestDrop(int item, float chance, int quest) { return new LootDef { ItemEntry = item, Chance = chance, QuestId = quest }; }

        static void AddCreatures(ZoneContent z)
        {
            var L = z.Creatures;

            // Boars root through the scrub around the camp; they only fight back when struck.
            var boar = Mob(MottledBoar, "Mottled Boar", new[] { 704, 193 }, 1, 2, Reaction.Neutral, 0f, 1);
            boar.DamageMultiplier = 0.9f; boar.MinMoney = 0; boar.MaxMoney = 2;
            boar.Loot.Add(Drop(4540, 0.04f));
            boar.Loot.Add(Drop(1367, 0.02f));
            L.Add(boar);

            var worker = Mob(ScorpidWorker, "Scorpid Worker", new[] { 2485, 2486 }, 2, 3, Reaction.Hostile, 10f, 1);
            worker.AttackSpeed = 1.8f; worker.MinMoney = 0; worker.MaxMoney = 3;
            worker.Loot.Add(QuestDrop(ItemScorpidTail, 0.7f, QuestStingers));
            worker.Loot.Add(Drop(1370, 0.02f));
            worker.Loot.Add(Drop(1377, 0.02f));
            L.Add(worker);

            var dusk = Mob(DuskScorpid, "Dusk Scorpid", new[] { 2488 }, 3, 4, Reaction.Hostile, 12f, 1);
            dusk.AttackSpeed = 1.8f; dusk.MinMoney = 1; dusk.MaxMoney = 4;
            dusk.Spells = new[] { SpellPoison }; dusk.SpellInterval = 12f;
            dusk.Loot.Add(QuestDrop(ItemDuskVenomGland, 0.65f, QuestDuskVenom));
            dusk.Loot.Add(Drop(2650, 0.02f));
            L.Add(dusk);

            // Imps the Burning Blade called up and could not control, loose among the northern rocks.
            var imp = Mob(VileFamiliar, "Vile Familiar", new[] { 10811 }, 3, 4, Reaction.Hostile, 12f, 3);
            imp.UnitClass = 8; imp.Spells = new[] { SpellFirebolt }; imp.SpellInterval = 7f;
            imp.MinMoney = 1; imp.MaxMoney = 5;
            imp.Loot.Add(Drop(3363, 0.02f));
            imp.Loot.Add(Drop(1378, 0.02f));
            L.Add(imp);

            // The cult below the coven cave in the far north.
            var neophyte = Mob(BurningBladeNeophyte, "Burning Blade Neophyte", new[] { 1324, 1326, 1366 }, 4, 5, Reaction.Hostile, 14f, 7);
            neophyte.UnitClass = 8; neophyte.MainHandDisplayId = 472;
            neophyte.Spells = new[] { SpellShadowBolt }; neophyte.SpellInterval = 7f;
            neophyte.MinMoney = 4; neophyte.MaxMoney = 9;
            neophyte.Loot.Add(QuestDrop(ItemBladeSigil, 0.6f, QuestSigilsOfTheBlade));
            neophyte.Loot.Add(Drop(159, 0.05f));
            neophyte.Loot.Add(Drop(1380, 0.02f));
            L.Add(neophyte);

            var thug = Mob(BurningBladeThug, "Burning Blade Thug", new[] { 1335, 1368, 1370 }, 4, 5, Reaction.Hostile, 14f, 7);
            thug.MainHandDisplayId = 19126; thug.Spells = new[] { SpellRend }; thug.SpellInterval = 10f;
            thug.MinMoney = 5; thug.MaxMoney = 11;
            thug.Loot.Add(QuestDrop(ItemBladeSigil, 0.6f, QuestSigilsOfTheBlade));
            thug.Loot.Add(Drop(118, 0.04f));
            thug.Loot.Add(Drop(1364, 0.02f));
            thug.Loot.Add(Drop(2649, 0.02f));
            L.Add(thug);

            // The named cult leader beyond the thugs (final quest). Tougher than anything else here.
            var boss = Mob(VorzulAshbrand, "Vorzul Ashbrand", new[] { 1323 }, 5, 5, Reaction.Hostile, 16f, 7);
            boss.UnitClass = 8; boss.Scale = 1.15f; boss.MainHandDisplayId = 878;
            boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.4f; boss.AttackSpeed = 2.6f;
            boss.Spells = new[] { SpellImmolate, SpellShadowBolt }; boss.SpellInterval = 7f;
            boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.Loot.Add(Drop(118, 0.5f));
            boss.Loot.Add(Drop(2653, 0.25f));
            boss.Loot.Add(Drop(3363, 0.25f));
            L.Add(boss);
        }

        // Orc NPC displays (CreatureDisplayInfoExtra race 2, OrcMale / OrcFemale) and Troll NPC displays (race 8, TrollMale /
        // TrollFemale) with an existing Textures\BakedNpcTextures\ file: orc 1371 m robe, 1871 m shoulders + tabard (display
        // scale 1.1), 2885 f helmet, 1311 m leather + shoulders, 3743 m bandana, 4464 m shoulders + gauntlets, 1325 f robe,
        // 1906 m / 1907 f / 2890 m helmet (grunts); troll 3995 m, 2898 m shoulders, 3996 f, 3993 m.
        // Held weapons (ItemDisplayInfo): 22645 Axe_2H_Horde_C_02, 22598 Axe_2H_Horde_C_01, 19126 Axe_1H_Horde_B_01,
        //   860 Mace_1H_Flanged_B_01, 1755 Buckler_Damaged_A_01 (shield), 8106 Bow_1H_Short_A_01, 6442 Knife_1H_Dagger_A_01,
        //   472 / 878 Stave_2H_Long_A_01.
        static CreatureTemplate Npc(int entry, string name, string subName, int display, int level, string greeting, int mainHand, int offHand)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, SubName = subName, DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level,
                Reaction = Reaction.Friendly, IsNpc = true, AggroRadius = 0f, Greeting = greeting, CreatureType = 7,
                MainHandDisplayId = mainHand, OffHandDisplayId = offHand,
            };
        }

        const string TrainerGrowth = " The valley only teaches the first lessons; come back to me as you grow stronger, and I will teach you the next.";

        static void AddNpcs(ZoneContent z)
        {
            var L = z.Creatures;
            L.Add(Npc(ElderGarosh, "Garosh Emberhide", "Valley Elder", 1371, 10,
                "The sun is hot, the ground is hard and the valley does not care who you were before. Good. Here you will find out who you are.",
                0, 0));
            L.Add(Npc(WarbandCaptain, "Rakka Ironjaw", "Warband Captain", 1871, 12,
                "Stand straight when you talk to me. The valley feeds on the weak, and I have no time to bury them. Are you ready to work?",
                22645, 0));
            L.Add(Npc(ScoutZanji, "Zanji Talonspear", "Darkspear Scout", 3995, 12,
                "Quiet now, mon. The rocks up here have ears, and some of them have claws too. You come to help or to make noise?",
                8106, 0));
            L.Add(Npc(WatchLeader, "Urtha Bloodfang", "Watch Leader", 2885, 12,
                "You can smell the smoke from here. The cave above us is full of traitors wearing our colours. Keep your weapon out.",
                19126, 0));
            L.Add(Npc(WarriorTrainer, "Drogash Steelbrow", "Warrior Trainer", 1311, 12,
                "Blood and sweat. Mostly yours, at first. That is how a warrior is made." + TrainerGrowth,
                22598, 0));
            L.Add(Npc(HunterTrainer, "Tak'ren Farshot", "Hunter Trainer", 2898, 12,
                "The beasts of this land are clever, mon. A good hunter is cleverer - and patient, always patient." + TrainerGrowth,
                8106, 0));
            L.Add(Npc(RogueTrainer, "Skarn Quickblade", "Rogue Trainer", 3743, 12,
                "Everyone watches the warrior with the big axe. Nobody watches the one standing behind him. Be that one." + TrainerGrowth,
                6442, 6442));
            L.Add(Npc(PriestTrainer, "Zel'ani Moonsong", "Priest Trainer", 3996, 12,
                "The spirits of the loa watch over this valley. Learn to speak with them, and they will lend you their strength." + TrainerGrowth,
                472, 0));
            L.Add(Npc(ShamanTrainer, "Thrak Stormcaller", "Shaman Trainer", 4464, 12,
                "Listen - the wind over the canyon, the fire in the stone. The elements are always speaking. A shaman learns to answer." + TrainerGrowth,
                860, 1755));
            L.Add(Npc(MageTrainer, "Rhazi Emberweave", "Mage Trainer", 3993, 12,
                "Magic be a wild thing, like the jungle. You do not tame it - you learn where it bites." + TrainerGrowth,
                472, 0));
            L.Add(Npc(WarlockTrainer, "Vasha Gloomheart", "Warlock Trainer", 1325, 12,
                "The Burning Blade drank from the same well we study and drowned in it. We do not drown. We command." + TrainerGrowth,
                878, 0));
            L.Add(Npc(GruntOne, "Borg Hardfist", "Grunt", 1906, 30,
                "If you fall out there, your spirit wakes up here by the stones. Try not to make a habit of it.",
                22645, 0));
            L.Add(Npc(GruntTwo, "Grenda Skullsplit", "Grunt", 1907, 30,
                "Boars in the meadows around the camp, scorpids to the north. Both will bite. Only one of them tastes good afterwards.",
                22645, 0));
            L.Add(Npc(GruntThree, "Tovak Grimtusk", "Grunt", 2890, 30,
                "This path runs north to the rocks where the imps nest, and on to the cave of the Burning Blade. Go in strong or do not go at all.",
                22598, 0));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
