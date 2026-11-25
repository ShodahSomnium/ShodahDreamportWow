using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Deathknell
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin, display scale; files checked by the harness):
        //   Zombie.mdx:     1200 ZombieSkinBlack 1.0, 1201 ZombieSkinGreen 1.0; ZombieArm.mdx (one arm torn off): 1197 Green 1.0,
        //                   1198 SeaGreen 1.0 (mindless zombies).
        //   FelBat.mdx:     1955 BatSkin01 0.45, 3956 BatSkinBrown01 0.45 (duskbats).
        //   Skeleton.mdx:   158 SkeletonSkin 1.0, 201 SkeletonSkinGreen 1.0, 535 SkeletonSkinDarkTeal 1.0.
        //   Ghoul.mdx:      137 GhoulSkinGrey 1.0, 987 GhoulSkinBlue 1.0.
        //   MineSpider.mdx: 368 MineSpiderSkinSteel 0.55, 418 Steel 0.7 (young); 545 MineSpiderSkinGreen 0.85, 659 Olive 0.8.
        //   HumanMale/HumanFemale (CreatureDisplayInfoExtra race 1, Crusader chest/tabard textures, baked textures exist):
        //                   1737/1738/1739 tabard only (converts), 1640 f/1641 m/2047 f belt + gauntlets + tabard (initiates),
        //                   2042 m helmet + tabard, display scale 1.2 (the inquisitor).
        // Held weapons (ItemDisplayInfo, model exists): 1546 Sword_1H_Short_A_01 rusty, 1544 Sword_1H_Short_A_01 blue,
        //   2380 Sword_2H_Claymore_A_01, 1755 Buckler_Damaged_A_01 rusty (shield).
        // Spells (Spell.dbc ids, names read from the client's Spell.dbc by the probe ContentB): 585 Smite, 2050 Lesser Heal,
        //   744 Poison.
        const int SpellSmite = 585, SpellLesserHeal = 2050, SpellPoison = 744;

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

            // Shambling dead on the slopes below the crypt - the first thing a newly risen Forsaken is asked to put down.
            var zombie = Mob(MindlessZombie, "Mindless Zombie", new[] { 1200, 1201, 1197, 1198 }, 1, 2, Reaction.Hostile, 8f, 6);
            zombie.WalkSpeed = 1.6f; zombie.RunSpeed = 5.5f; zombie.AttackSpeed = 2.2f; zombie.DamageMultiplier = 0.9f;
            zombie.MinMoney = 0; zombie.MaxMoney = 2;
            zombie.Loot.Add(Drop(4540, 0.04f));
            zombie.Loot.Add(Drop(1377, 0.02f));
            L.Add(zombie);

            var bat = Mob(Duskbat, "Duskbat", new[] { 1955, 3956 }, 2, 3, Reaction.Hostile, 10f, 1);
            bat.AttackSpeed = 1.8f; bat.RunSpeed = 7.5f; bat.MinMoney = 0; bat.MaxMoney = 3;
            bat.Loot.Add(QuestDrop(ItemDuskbatWing, 0.7f, QuestWingsInTheDusk));
            bat.Loot.Add(Drop(1367, 0.02f));
            bat.Loot.Add(Drop(1370, 0.02f));
            L.Add(bat);

            var young = Mob(YoungNightWebSpider, "Young Night Web Spider", new[] { 368, 418 }, 2, 3, Reaction.Hostile, 10f, 1);
            young.AttackSpeed = 1.8f; young.MinMoney = 0; young.MaxMoney = 3;
            young.Loot.Add(QuestDrop(ItemNightWebSilk, 0.6f, QuestSilkForTheApothecary));
            young.Loot.Add(Drop(3363, 0.02f));
            L.Add(young);

            var spider = Mob(NightWebSpider, "Night Web Spider", new[] { 545, 659 }, 3, 4, Reaction.Hostile, 12f, 1);
            spider.AttackSpeed = 1.8f; spider.MinMoney = 1; spider.MaxMoney = 4;
            spider.Spells = new[] { SpellPoison }; spider.SpellInterval = 12f;
            spider.Loot.Add(QuestDrop(ItemNightWebSilk, 0.7f, QuestSilkForTheApothecary));
            spider.Loot.Add(Drop(2650, 0.02f));
            L.Add(spider);

            var skeleton = Mob(RestlessSkeleton, "Restless Skeleton", new[] { 158, 201, 535 }, 3, 4, Reaction.Hostile, 12f, 6);
            skeleton.MinMoney = 2; skeleton.MaxMoney = 6;
            skeleton.Loot.Add(Drop(2649, 0.03f));
            skeleton.Loot.Add(Drop(1366, 0.02f));
            L.Add(skeleton);

            var ghoul = Mob(WretchedGhoul, "Wretched Ghoul", new[] { 137, 987 }, 3, 4, Reaction.Hostile, 12f, 6);
            ghoul.AttackSpeed = 1.7f; ghoul.MinMoney = 1; ghoul.MaxMoney = 5;
            ghoul.Loot.Add(Drop(1364, 0.02f));
            ghoul.Loot.Add(Drop(2653, 0.02f));
            L.Add(ghoul);

            // The Scarlet camp on the north-eastern ridge: zealots who came to burn the risen dead.
            var convert = Mob(ScarletConvert, "Scarlet Convert", new[] { 1737, 1738, 1739 }, 4, 5, Reaction.Hostile, 14f, 7);
            convert.MainHandDisplayId = 1546; convert.MinMoney = 4; convert.MaxMoney = 9;
            convert.Loot.Add(QuestDrop(ItemCrimsonArmband, 0.6f, QuestCrimsonArmbands));
            convert.Loot.Add(Drop(118, 0.04f));
            convert.Loot.Add(Drop(1378, 0.02f));
            L.Add(convert);

            var initiate = Mob(ScarletInitiate, "Scarlet Initiate", new[] { 1640, 1641, 2047 }, 5, 5, Reaction.Hostile, 14f, 7);
            initiate.UnitClass = 2; initiate.MainHandDisplayId = 1544; initiate.OffHandDisplayId = 1755;
            initiate.Spells = new[] { SpellSmite }; initiate.SpellInterval = 9f;
            initiate.MinMoney = 5; initiate.MaxMoney = 11;
            initiate.Loot.Add(QuestDrop(ItemCrimsonArmband, 0.7f, QuestCrimsonArmbands));
            initiate.Loot.Add(Drop(1380, 0.02f));
            initiate.Loot.Add(Drop(2649, 0.02f));
            L.Add(initiate);

            // The named inquisitor at the far end of the Scarlet camp (final quest). Tougher than anything else here.
            var boss = Mob(InquisitorMarrenVale, "Inquisitor Marren Vale", new[] { 2042 }, 5, 5, Reaction.Hostile, 16f, 7);
            boss.UnitClass = 2; boss.MainHandDisplayId = 2380;
            boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.4f; boss.AttackSpeed = 2.8f;
            boss.Spells = new[] { SpellSmite, SpellLesserHeal }; boss.SpellInterval = 8f;
            boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.Loot.Add(Drop(118, 0.5f));
            boss.Loot.Add(Drop(2649, 0.25f));
            boss.Loot.Add(Drop(2653, 0.25f));
            L.Add(boss);
        }

        // Undead NPC displays: CreatureDisplayInfoExtra race 5 (ScourgeMale / ScourgeFemale) with an existing
        // Textures\BakedNpcTextures\ file: 1814 f robe, 1666 m Deathguard helmet + tabard, 1575 m shirt and boots,
        //   1604 m helmet, 1583 m chain + shoulders, 1586 m bandana, 1592 f shirt + gloves, 1594 f shirt, 1600 m belt,
        //   1645 f / 1646 m / 1647 m Deathguard helmet + tabard.
        // Held weapons (ItemDisplayInfo): 1546 Sword_1H_Short_A_01 rusty + 1755 rusty buckler (Deathguards),
        //   2380 Sword_2H_Claymore_A_01, 6442 Knife_1H_Dagger_A_01, 472 / 878 Stave_2H_Long_A_01.
        static CreatureTemplate Npc(int entry, string name, string subName, int display, int level, string greeting, int mainHand, int offHand)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, SubName = subName, DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level,
                Reaction = Reaction.Friendly, IsNpc = true, AggroRadius = 0f, Greeting = greeting, CreatureType = 6,
                MainHandDisplayId = mainHand, OffHandDisplayId = offHand,
            };
        }

        const string TrainerGrowth = " There is more to learn than one night can hold; come back to me as you grow, and I will show you the rest.";

        static void AddNpcs(ZoneContent z)
        {
            var L = z.Creatures;
            L.Add(Npc(ShadowPriestessVessra, "Vessra Duskhallow", "Shadow Priestess", 1814, 10,
                "Easy. Let your eyes settle - the light up here is harsh after the dark below. You died, and now you are standing. That is the whole of the miracle, and it is enough to begin with.",
                0, 0));
            L.Add(Npc(CaptainDorrin, "Dorrin Ashgrave", "Deathguard Captain", 1666, 12,
                "Another one on their feet. Good. Can you hold a blade? Then you are already more use to me than half of what crawls out of those graves.",
                1546, 1755));
            L.Add(Npc(ApothecaryMerrow, "Merrow Vantis", "Apothecary", 1575, 10,
                "Mind the vials - some of them bite. Everything in these fields is an ingredient, if you know how to ask it. Have you come to help, or to stare?",
                0, 0));
            L.Add(Npc(ExecutorHarrow, "Harrow Blackmere", "Executor", 1604, 12,
                "Keep your voice down. The zealots on the ridge have sharp ears and sharper steel. If you are here, you are here to work.",
                2380, 0));
            L.Add(Npc(WarriorTrainer, "Garrick Holloway", "Warrior Trainer", 1583, 12,
                "Your old body forgot how to fight. This one never learned. We start from nothing, then - it is the honest way." + TrainerGrowth,
                2380, 0));
            L.Add(Npc(RogueTrainer, "Silas Crane", "Rogue Trainer", 1586, 12,
                "You walked right past me. Twice. Remember that feeling - it is the whole of our craft." + TrainerGrowth,
                6442, 6442));
            L.Add(Npc(PriestTrainer, "Maudra Veil", "Priest Trainer", 1592, 12,
                "The light turned its face from us, so we learned to pray to the shadow as well. Both answer, if you ask properly." + TrainerGrowth,
                0, 0));
            L.Add(Npc(MageTrainer, "Ezra Morrowind", "Mage Trainer", 1594, 12,
                "Death sharpened my mind wonderfully. No sleep, no hunger, only study. Shall we see what yours can hold?" + TrainerGrowth,
                472, 0));
            L.Add(Npc(WarlockTrainer, "Lucan Graves", "Warlock Trainer", 1600, 12,
                "Power has a price. Fortunately, the dead have already paid the worst of it. Come closer - the fel does not bite. Much." + TrainerGrowth,
                878, 0));
            L.Add(Npc(DeathguardOne, "Olric Stane", "Deathguard", 1646, 30,
                "The crypt is behind you and the world is ahead. Keep to the path and keep your wits - not everything that walks here is one of us.",
                1546, 1755));
            L.Add(Npc(DeathguardTwo, "Brenna Hollow", "Deathguard", 1645, 30,
                "The path runs down past the graveyard to the chapel. The mindless wander there. Put them to rest if they come at you.",
                1546, 1755));
            L.Add(Npc(DeathguardThree, "Tomas Wexley", "Deathguard", 1647, 30,
                "The chapel still stands, more or less. If you fall out there, your spirit will find its way back to this yard.",
                1546, 1755));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
