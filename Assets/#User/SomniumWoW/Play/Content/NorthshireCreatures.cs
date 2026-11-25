using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Northshire
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin, display scale; skins decoded and looked at in the harness):
        //   Wolf.mdx:    604 WolfSkinTimber 0.55, 647 Timber 0.65 (young wolves); 380 Timber 0.85, 783 Timber 0.9 (timber wolves).
        //   Kobold.mdx:  10913 koboldskinNormal 0.7, 365 Normal 0.85 (vermin); 26 Normal 1.0, 139 Normal 1.0 (laborers).
        //   Deer.mdx:    347 DeerSkin 1.0, 654 DeerSkin 0.6 (a fawn).
        //   Bandits = HumanMale/HumanFemale displays with a CreatureDisplayInfoExtra (race 1) whose baked texture exists and whose
        //   NPC items include a red kerchief mask: 2144, 2145, 2146, 2148 (bare-chested, patched trousers), 2314 m / 2315 f (dark
        //   leather), 2316 (chain over leather, the leader).
        // CreatureType (CreatureType.dbc): 1 Beast (wolves, deer), 7 Humanoid (kobolds, bandits). Spells: 772 = Rend (Rank 1) in
        // Spell.dbc, used by the bandit leader (checked by the ZaCheck probe).
        const int SpellRend = 772;
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

            var young = Beast(YoungWolf, "Young Wolf", new[] { 604, 647 }, 1, 2, Reaction.Hostile, 8f);
            young.DamageMultiplier = 0.9f; young.MinMoney = 0; young.MaxMoney = 2;
            young.Loot.Add(Drop(4536, 0.04f));
            young.Loot.Add(Drop(1419, 0.02f));
            L.Add(young);

            var timber = Beast(TimberWolf, "Timber Wolf", new[] { 380, 783 }, 2, 3, Reaction.Hostile, 10f);
            timber.MinMoney = 1; timber.MaxMoney = 4;
            timber.Loot.Add(QuestDrop(ItemMattedWolfPelt, 0.7f, QuestTimberPelts));
            timber.Loot.Add(Drop(1420, 0.02f));
            timber.Loot.Add(Drop(1423, 0.02f));
            L.Add(timber);

            var deer = Beast(ValleyDeer, "Valley Deer", new[] { 347, 654 }, 1, 2, Reaction.Neutral, 0f);
            deer.RunSpeed = 8f; deer.DamageMultiplier = 0.8f;
            L.Add(deer);

            var vermin = Beast(KoboldVermin, "Kobold Vermin", new[] { 10913, 365 }, 2, 3, Reaction.Hostile, 10f);
            vermin.CreatureType = 7; vermin.MinMoney = 1; vermin.MaxMoney = 5;
            vermin.Loot.Add(QuestDrop(ItemSackOfAbbeyGrain, 0.55f, QuestTheAbbeysGrain));
            vermin.Loot.Add(Drop(1205, 0.05f));
            vermin.Loot.Add(Drop(1427, 0.02f));
            L.Add(vermin);

            var laborer = Beast(KoboldLaborer, "Kobold Laborer", new[] { 26, 139 }, 3, 4, Reaction.Hostile, 12f);
            laborer.CreatureType = 7; laborer.MinMoney = 3; laborer.MaxMoney = 8;
            laborer.Loot.Add(QuestDrop(ItemSackOfAbbeyGrain, 0.55f, QuestTheAbbeysGrain));
            laborer.Loot.Add(Drop(118, 0.03f));
            laborer.Loot.Add(Drop(1430, 0.02f));
            laborer.Loot.Add(Drop(1731, 0.02f));
            L.Add(laborer);

            // Bandits hold their weapons visibly (ItemDisplayInfo: 1546 Sword_1H_Short_A_01 rusty, 6442 Knife_1H_Dagger_A_01,
            // 862 Sword_1H_Long_B_01).
            var bandit = Beast(VineyardBandit, "Vineyard Bandit", new[] { 2144, 2145, 2146, 2148 }, 3, 4, Reaction.Hostile, 12f);
            bandit.CreatureType = 7; bandit.MinMoney = 4; bandit.MaxMoney = 9; bandit.MainHandDisplayId = 1546;
            bandit.Loot.Add(QuestDrop(ItemPilferedVineyardCrate, 0.6f, QuestTheStolenHarvest));
            bandit.Loot.Add(Drop(4540, 0.05f));
            bandit.Loot.Add(Drop(1425, 0.02f));
            bandit.Loot.Add(Drop(1730, 0.02f));
            L.Add(bandit);

            var lookout = Beast(BanditLookout, "Bandit Lookout", new[] { 2314, 2315 }, 4, 5, Reaction.Hostile, 14f);
            lookout.CreatureType = 7; lookout.MinMoney = 5; lookout.MaxMoney = 11; lookout.AttackSpeed = 1.8f; lookout.MainHandDisplayId = 6442;
            lookout.Loot.Add(QuestDrop(ItemPilferedVineyardCrate, 0.6f, QuestTheStolenHarvest));
            lookout.Loot.Add(Drop(118, 0.03f));
            lookout.Loot.Add(Drop(1423, 0.02f));
            L.Add(lookout);

            // The bandit leader at the far end of the vineyard (final quest). Tougher than anything else here.
            var boss = Beast(HarlanCrowe, "Harlan Crowe the Vinecutter", new[] { 2316 }, 5, 5, Reaction.Hostile, 15f);
            boss.Scale = 1.1f; boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.4f; boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.CreatureType = 7; boss.MainHandDisplayId = 862; boss.OffHandDisplayId = 6442;
            boss.Spells = new[] { SpellRend }; boss.SpellInterval = 10f;
            boss.Loot.Add(Drop(118, 0.5f));
            boss.Loot.Add(Drop(1730, 0.25f));
            boss.Loot.Add(Drop(1425, 0.25f));
            L.Add(boss);
        }

        // Human NPC displays: CreatureDisplayInfoExtra race 1 (HumanMale.m2 / HumanFemale.m2) with an existing
        // Textures\BakedNpcTextures\ file; picked by looking at the baked textures (harness ZaBakes):
        //   1501 m grey-white robe with gold trim, 1859 m Stormwind plate + blue lion tabard, 1658 f red work clothes, 1506 m green
        //   leather + wide-brimmed hat, 2032 m silver plate and mail, 1300 f red with a silver breastplate, 1297 f dark leather,
        //   1573 m white robe, 1484 m blue-violet robe with a stole, 1765 m black robe with a blue stole, 2150 / 2151 m and 3257 f
        //   Stormwind guard plate with helm and tabard.
        // Held items (ItemDisplayInfo, models exist): 1032 Sword_1H_Long_B_01, 862 Sword_1H_Long_B_01, 1550 Sword_1H_Long_A_02,
        //   1705 Shield_Crest_A_01 silver, 1644 Shield_Rectangle_B_01 green, 1378 Axe_1H_Pick_A_01 (a pick), 2380
        //   Sword_2H_Claymore_A_01, 1161 Mace_1H_Flanged_A_01, 6442 Knife_1H_Dagger_A_01, 472 / 878 Stave_2H_Long_A_01,
        //   1190 Stave_2H_Jeweled_D_01.
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

            L.Add(Npc(BrotherAldric, "Aldric Venn", "Abbey Steward", 1501, 10,
                "Peace be with you, traveler. The abbey's doors have stood open to the weary for longer than anyone can remember - and it seems the Light has sent you to us on a busy morning.",
                0, 0));
            L.Add(Npc(MarshalCorwin, "Corwin Hale", "Marshal of Northshire", 1859, 12,
                "Eyes open, recruit. The valley looks peaceful from the abbey steps, but that is exactly when trouble likes to walk in.",
                1032, 1705));
            L.Add(Npc(ForemanEdda, "Edda Brask", "Echo Ridge Foreman", 1658, 10,
                "Mind the ruts in the road. My crew hauled ore down this hill for twenty years before the ridge started chittering at us.",
                1378, 0));
            L.Add(Npc(HollisMarrow, "Hollis Marrow", "Vineyard Keeper", 1506, 8,
                "Keep your voice down, friend - the rows have ears these days. My family has tended these vines for three generations, and I am not about to hand them over to a pack of thieves.",
                0, 0));

            L.Add(Npc(WarriorTrainer, "Bram Holloway", "Warrior Trainer", 2032, 12,
                "Shoulders square, feet apart, grip firm but not tight. A warrior is built one stubborn day at a time." + TrainerGrowth,
                2380, 0));
            L.Add(Npc(PaladinTrainer, "Maren Ashby", "Paladin Trainer", 1300, 12,
                "The Light asks two things of a paladin: courage to stand in front, and the humility to kneel beside those who fall." + TrainerGrowth,
                1161, 1705));
            L.Add(Npc(RogueTrainer, "Lissa Crane", "Rogue Trainer", 1297, 12,
                "Relax - if I meant you harm, you would never have seen me standing here. Lesson one: people only notice what they expect to see." + TrainerGrowth,
                6442, 6442));
            L.Add(Npc(PriestTrainer, "Tomas Whitlow", "Priest Trainer", 1573, 12,
                "The Light does not belong to the abbey, child - it belongs to anyone willing to carry it. Let me show you how." + TrainerGrowth,
                472, 0));
            L.Add(Npc(MageTrainer, "Aldous Penrose", "Mage Trainer", 1484, 12,
                "Careful where you step - that scorch mark was a lesson in patience. Arcane power rewards the curious and punishes the careless." + TrainerGrowth,
                1190, 0));
            L.Add(Npc(WarlockTrainer, "Silas Morrow", "Warlock Trainer", 1765, 12,
                "The brothers pretend not to see me out here. Power is power, whatever its source - the trick is to stay its master." + TrainerGrowth,
                878, 0));

            L.Add(Npc(GuardEdric, "Edric Vance", "Northshire Guard", 2150, 20,
                "The abbey is under the watch's protection. If anything with teeth follows you back from the meadows, send it my way.",
                1032, 1705));
            L.Add(Npc(GuardDunstan, "Dunstan Reed", "Northshire Guard", 2151, 20,
                "This road runs down through the gate and on toward Goldshire. When you have outgrown the valley, it will be waiting for you.",
                862, 1705));
            L.Add(Npc(GuardMara, "Mara Quill", "Northshire Guard", 3257, 20,
                "The bridge is as far as I go. Past the river the vineyards belong to whoever holds them - and lately that isn't Hollis.",
                1550, 1644));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
