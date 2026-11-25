using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class AmmenVale
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin; scale = display scale x model scale, measured by the harness):
        //   Moth.mdx:         17709 MothSkinBlue 1.0, 17574 MothSkinBeige 1.0 (vale moths).
        //   LasherOrchid.mdx: 17714 LasherOrchidSkinPurple 1.0, 17712 LasherOrchidSkinGreen 1.0 (mutated root lashers).
        //   BogBeast.mdx:     1084 BogBeastSkinDiseased 1.0, 1086 BogBeastSkinDiseased 1.3 (volatile mutations; template scale 0.85).
        //   Blood elf humanoid displays (CreatureDisplayInfoExtra race 10, baked textures exist): 16055 male / 15525 female red-brown
        //   leather (infiltrators), 17958 male black-and-gold plate, scale 1.2 (the named thief).
        // Held weapons (ItemDisplayInfo): 33252 Knife_1H_Blood_A_02, 33237 Knife_1H_Blood_A_01, 37409 Sword_2H_Blood_B_01.
        static CreatureTemplate Beast(int entry, string name, int[] displays, int minLevel, int maxLevel, Reaction reaction, float aggro)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, DisplayIds = displays, MinLevel = minLevel, MaxLevel = maxLevel, Reaction = reaction,
                AggroRadius = aggro, WalkSpeed = 2.5f, RunSpeed = 7f, AttackSpeed = 2f,
            };
        }

        static LootDef Drop(int item, float chance) { return new LootDef { ItemEntry = item, Chance = chance }; }
        static LootDef QuestDrop(int item, float chance, int quest) { return new LootDef { ItemEntry = item, Chance = chance, QuestId = quest }; }

        static void AddCreatures(ZoneContent z)
        {
            var L = z.Creatures;

            var moth = Beast(ValeMoth, "Vale Moth", new[] { 17709, 17574 }, 1, 2, Reaction.Hostile, 8f);
            moth.CreatureType = 1; moth.DamageMultiplier = 0.9f; moth.MinMoney = 0; moth.MaxMoney = 2;
            moth.Loot.Add(Drop(4536, 0.04f));
            moth.Loot.Add(Drop(1374, 0.02f));
            L.Add(moth);

            var lasher = Beast(MutatedRootLasher, "Mutated Root Lasher", new[] { 17714, 17712 }, 2, 3, Reaction.Hostile, 10f);
            lasher.WalkSpeed = 2f; lasher.MinMoney = 1; lasher.MaxMoney = 4;
            lasher.Loot.Add(QuestDrop(ItemRootCutting, 0.7f, QuestRootsOfTheBlight));
            lasher.Loot.Add(Drop(1368, 0.02f));
            lasher.Loot.Add(Drop(3365, 0.02f));
            L.Add(lasher);

            var mutation = Beast(VolatileMutation, "Volatile Mutation", new[] { 1084, 1086 }, 3, 4, Reaction.Hostile, 12f);
            mutation.Scale = 0.85f; mutation.AttackSpeed = 2.2f; mutation.MinMoney = 2; mutation.MaxMoney = 6;
            mutation.Loot.Add(QuestDrop(ItemSporeGland, 0.65f, QuestSeedsOfChange));
            mutation.Loot.Add(Drop(1179, 0.05f));
            mutation.Loot.Add(Drop(1369, 0.03f));
            mutation.Loot.Add(Drop(1433, 0.02f));
            L.Add(mutation);

            var spy = Beast(BloodElfInfiltrator, "Blood Elf Infiltrator", new[] { 16055, 15525 }, 4, 5, Reaction.Hostile, 12f);
            spy.CreatureType = 7; spy.MainHandDisplayId = 33252; spy.OffHandDisplayId = 33237; spy.AttackSpeed = 1.8f; spy.MinMoney = 3; spy.MaxMoney = 8;
            spy.Loot.Add(QuestDrop(ItemCrystalShard, 0.6f, QuestStolenShards));
            spy.Loot.Add(Drop(858, 0.03f));
            spy.Loot.Add(Drop(1372, 0.02f));
            spy.Loot.Add(Drop(2210, 0.02f));
            L.Add(spy);

            // The named thief at the far south-west corner of the vale (final quest). Tougher than anything else here.
            var boss = Beast(VaelithDawnthief, "Vaelith Dawnthief", new[] { 17958 }, 5, 5, Reaction.Hostile, 15f);
            boss.CreatureType = 7; boss.MainHandDisplayId = 37409; boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.4f; boss.AttackSpeed = 2.6f;
            boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.Loot.Add(Drop(858, 0.5f));
            boss.Loot.Add(Drop(1425, 0.25f));
            boss.Loot.Add(Drop(1369, 0.25f));
            L.Add(boss);
        }

        // Draenei NPC displays: CreatureDisplayInfoExtra race 11 (DraeneiMale.m2 / DraeneiFemale.m2) with an existing
        // Textures\BakedNpcTextures\ file; picked by looking at the baked textures (harness dr_l1.png / dr_l2.png):
        //   16593 m dark blue robe with gold trim (scale 1.1), 16385 m violet cloth with crystal chains, 16222 f brown leather straps,
        //   17234 f silver-and-olive plate, 17015 m bronze plate (scale 1.2), 16729 m golden plate, 28768 f leather with fur trim,
        //   16200 f violet robe with gold trim, 16224 m blue scale mail, 16913 m red robe with gold, 17162 / 17164 (scale 1.1) m and
        //   17165 f silver-blue plate with a helmet (the vale's peacekeepers).
        // Held weapons (ItemDisplayInfo): 38713 Stave_2H_Draenei_A_02, 38099 Sword_1H_Draenei_B_01, 38085 Shield_Draenei_A_01,
        //   37050 Sword_2H_Draenei_A_02, 36711 Mace_2H_Draenei_A_02, 37057 Bow_2H_Crossbow_Draenei_A_01, 37054 Stave_2H_Draenei_A_01,
        //   37034 Mace_1H_Draenei_A_01, 38264 Stave_2H_Draenei_A_03, 37208 Sword_1H_Draenei_A_01, 36637 Shield_Draenei_A_02,
        //   36706 Mace_2H_Draenei_A_01, 37191 Sword_2H_Draenei_A_01.
        static CreatureTemplate Npc(int entry, string name, string subName, int display, int level, string greeting, int mainHand, int offHand)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, SubName = subName, DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level,
                Reaction = Reaction.Friendly, IsNpc = true, AggroRadius = 0f, Greeting = greeting, CreatureType = 7,
                MainHandDisplayId = mainHand, OffHandDisplayId = offHand,
            };
        }

        const string TrainerGrowth = " What you are ready for will come to you as you grow. Return to me whenever you wish, and I will show you how it is done.";

        static void AddNpcs(ZoneContent z)
        {
            var L = z.Creatures;

            L.Add(Npc(ElderTaamun, "Elder Taamun", "Voice of the Survivors", 16593, 10,
                "Easy now - you have been asleep a long while, and the fall was not gentle on any of us. The Light kept you whole. Now the vale needs every pair of hands it can find.",
                38713, 0));
            L.Add(Npc(ArtificerZhaaro, "Artificer Zhaaro", "Crystal Warden", 16385, 10,
                "Mind the shards on the path; some of them still hum. Every crystal we recover from the wreck is another light we can keep burning.",
                0, 0));
            L.Add(Npc(BotanistNuraeli, "Botanist Nuraeli", "Keeper of Seedlings", 16222, 10,
                "Look at this soil. It should be resting, and instead it is growing things no world ever meant to grow. Something from the wreck has seeped into the roots.",
                0, 0));
            L.Add(Npc(VindicatorSelaar, "Vindicator Selaar", "Watch of the Vale", 17234, 12,
                "Keep low. There are eyes in the ferns to the south, and they are not ours. I would rather they did not learn how few of us are standing.",
                38099, 38085));

            L.Add(Npc(WarriorTrainer, "Kordaan", "Warrior Trainer", 17015, 12,
                "We walked out of fire and wreckage. Whatever the vale throws at us next, we meet it standing - and you will learn to stand in front." + TrainerGrowth,
                37050, 0));
            L.Add(Npc(PaladinTrainer, "Oraelos", "Paladin Trainer", 16729, 12,
                "The Light did not abandon us in the fall, and it will not abandon you now. Hold it close, and let it be your shield for those who cannot shield themselves." + TrainerGrowth,
                36711, 0));
            L.Add(Npc(HunterTrainer, "Aeleth", "Hunter Trainer", 28768, 12,
                "A new world, full of creatures we have never named. Watch them first. Learn how they move before you ever draw on one." + TrainerGrowth,
                37057, 0));
            L.Add(Npc(PriestTrainer, "Ishaara", "Priest Trainer", 16200, 12,
                "So many hurt, and so much still to mend. Come - the Light answers those who ask with a steady heart." + TrainerGrowth,
                37054, 0));
            L.Add(Npc(ShamanTrainer, "Tzuraan", "Shaman Trainer", 16224, 12,
                "Listen. The spirits of this island are wary of us - we came crashing down from their sky. We must earn their trust, one quiet word at a time." + TrainerGrowth,
                37034, 38085));
            L.Add(Npc(MageTrainer, "Zaevor", "Mage Trainer", 16913, 12,
                "The wreck is leaking raw energy into the vale. A careless mind would drink it in. A disciplined one learns to shape it." + TrainerGrowth,
                38264, 0));

            L.Add(Npc(PeacekeeperVoraan, "Voraan", "Peacekeeper", 17162, 30,
                "Rest a moment if you need to. Nothing reaches the waking platform while the peacekeepers stand here.",
                37208, 36637));
            L.Add(Npc(PeacekeeperMaadun, "Maadun", "Peacekeeper", 17164, 30,
                "The graveyard behind me holds too many already. Walk carefully out there, and come back to us.",
                36706, 0));
            L.Add(Npc(PeacekeeperIlaari, "Ilaari", "Peacekeeper", 17165, 30,
                "This road runs down from the platform to the crash site. Stay on it after dark - the moths are drawn to anything that glows.",
                37191, 0));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
