using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class Shadowglen
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin; "scale" = display scale x model scale, measured):
        //   Tiger.mdx:     11448 TigerSkinBlackSpotted 0.7, 14318 BlackSpotted 0.85 (cubs); 3030 TigerSkinDark 1.0, 6805 Dark 0.85
        //                  (patchy grey coat = mangy); 11449 TigerSkinBlackStriped 0.85, 321 BlackStriped 1.0 (nightsaber).
        //   Grell.mdx:     1016 GrellSkinRed 0.75, 1185 GrellSkinBlue 0.85 (grellkin); 911 GrellSkinRed 1.0, 1015 Red 1.15 (grell).
        //   MineSpider.mdx: 545 MineSpiderSkinGreen 0.85, 2541 Green 0.7 (webwood spiders).
        //   Satyr.mdx:     2015 SatyrSkinCrimson 1.15 (the named satyr).
        //   Stag.mdx:      1917 WapitiSkinBrown 1.0, 8013 Brown 1.15.
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

            var young = Beast(YoungNightsaber, "Young Nightsaber", new[] { 11448, 14318 }, 1, 2, Reaction.Hostile, 8f);
            young.DamageMultiplier = 0.9f; young.MinMoney = 0; young.MaxMoney = 2;
            young.Loot.Add(Drop(4540, 0.04f));
            young.Loot.Add(Drop(1367, 0.02f));
            L.Add(young);

            var mangy = Beast(MangyNightsaber, "Mangy Nightsaber", new[] { 3030, 6805 }, 2, 3, Reaction.Hostile, 10f);
            mangy.MinMoney = 1; mangy.MaxMoney = 4;
            mangy.Loot.Add(QuestDrop(ItemMangyPelt, 0.7f, QuestMangyPelts));
            mangy.Loot.Add(Drop(1366, 0.02f));
            mangy.Loot.Add(Drop(1370, 0.02f));
            L.Add(mangy);

            var saber = Beast(Nightsaber, "Nightsaber", new[] { 11449, 321 }, 3, 3, Reaction.Neutral, 0f);
            saber.HealthMultiplier = 1.1f; saber.MinMoney = 1; saber.MaxMoney = 5;
            saber.Loot.Add(Drop(1364, 0.03f));
            L.Add(saber);

            var stag = Beast(ShadowglenStag, "Shadowglen Stag", new[] { 1917, 8013 }, 1, 2, Reaction.Neutral, 0f);
            stag.RunSpeed = 8f; stag.DamageMultiplier = 0.8f;
            L.Add(stag);

            var grellkin = Beast(Grellkin, "Grellkin", new[] { 1016, 1185 }, 2, 3, Reaction.Hostile, 10f);
            grellkin.MinMoney = 2; grellkin.MaxMoney = 6;
            grellkin.Loot.Add(QuestDrop(ItemFelCharm, 0.6f, QuestTaintedCharms));
            grellkin.Loot.Add(Drop(159, 0.05f));
            grellkin.Loot.Add(Drop(1377, 0.03f));
            grellkin.Loot.Add(Drop(1378, 0.02f));
            L.Add(grellkin);

            var grell = Beast(Grell, "Grell", new[] { 911, 1015 }, 3, 4, Reaction.Hostile, 12f);
            grell.MinMoney = 4; grell.MaxMoney = 9;
            grell.Loot.Add(QuestDrop(ItemFelCharm, 0.6f, QuestTaintedCharms));
            grell.Loot.Add(Drop(118, 0.03f));
            grell.Loot.Add(Drop(1380, 0.02f));
            grell.Loot.Add(Drop(2653, 0.02f));
            L.Add(grell);

            var spider = Beast(WebwoodSpider, "Webwood Spider", new[] { 545, 2541 }, 3, 5, Reaction.Hostile, 12f);
            spider.AttackSpeed = 1.8f; spider.MinMoney = 0; spider.MaxMoney = 3;
            spider.Loot.Add(QuestDrop(ItemVenomSac, 0.65f, QuestVenomInTheWater));
            spider.Loot.Add(Drop(2650, 0.02f));
            spider.Loot.Add(Drop(3363, 0.02f));
            L.Add(spider);

            // The named satyr at the far end of the western glade (final quest). Tougher than anything else here.
            var boss = Beast(SythrelTheDefiler, "Sythrel the Defiler", new[] { 2015 }, 5, 5, Reaction.Hostile, 15f);
            boss.Scale = 1.1f; boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.4f; boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.Loot.Add(Drop(118, 0.5f));
            boss.Loot.Add(Drop(2649, 0.25f));
            boss.Loot.Add(Drop(3363, 0.25f));
            L.Add(boss);
        }

        // Night Elf NPC displays: CreatureDisplayInfoExtra race 4 (male NightElfMale.m2 / female NightElfFemale.m2) with an
        // existing Textures\BakedNpcTextures\ file; picked by looking at the baked textures:
        //   1702 f silver/gold gown, 1701 m grey leather, 1709 m blue-purple robe, 15307 f silver/green
        //   armour, 1542 m chain + fur (scale 1.2), 1703 m fur/leather, 1719 f dark leather, 1681 f purple gown, 29510 m red-brown
        //   robe (scale 1.1), 23664 f purple-gold sentinel armour (scale 1.2), 2034 f silver plate, 25060 f dark blue-silver plate.
        // Held weapons (ItemDisplayInfo): 5925 Polearm_2H_Bladed_B_01 purple (a glaive), 2787 Bow_1H_Standard_A_01 purple,
        //   8106 Bow_1H_Short_A_01, 2380 Sword_2H_Claymore_A_01, 6442 Knife_1H_Dagger_A_01, 18530 Stave_2H_Other_A_01.
        static CreatureTemplate Npc(int entry, string name, string subName, int display, int level, string greeting, int mainHand, int offHand)
        {
            return new CreatureTemplate
            {
                Entry = entry, Name = name, SubName = subName, DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level,
                Reaction = Reaction.Friendly, IsNpc = true, AggroRadius = 0f, Greeting = greeting,
                MainHandDisplayId = mainHand, OffHandDisplayId = offHand,
            };
        }

        const string TrainerGrowth = " New skills will come to you as you grow; return to me whenever you wish, and I will show you how to use them.";

        static void AddNpcs(ZoneContent z)
        {
            var L = z.Creatures;

            L.Add(Npc(KeeperLorellen, "Lorellen Dawnbough", "Keeper of the Glen", 1702, 10,
                "Welcome, child of the stars. Every one of us once woke as you have now, beneath Aldrassil's boughs, with the whole of the world still to learn. Breathe. Listen to the glen. It will tell you where you are needed.",
                0, 0));
            L.Add(Npc(WardenFaelos, "Faelos Nightbrook", "Warden of Shadowglen", 1701, 10,
                "Keep your voice low. The glen hears more than you think, and not everything that listens is friendly. What do you need?",
                0, 0));
            L.Add(Npc(TenderAravel, "Aravel Silverpool", "Moonwell Tender", 1709, 10,
                "Mind the water's edge. The moonwell gives freely, but it asks that we care for it in return - and lately it has been asking more than usual.",
                0, 0));
            L.Add(Npc(ScoutVeyra, "Veyra Shadesong", "Glade Watch", 15307, 12,
                "Stay behind me and keep your eyes on the treeline. The western glade has not been ours for many nights.",
                2787, 0));

            L.Add(Npc(WarriorTrainer, "Tharion Bladesworn", "Warrior Trainer", 1542, 12,
                "Steel and stubbornness - that is what a warrior is made of. You have the second already." + TrainerGrowth,
                2380, 0));
            L.Add(Npc(HunterTrainer, "Kaelen Swiftarrow", "Hunter Trainer", 1703, 12,
                "The wild does not forgive a careless hunter, but it rewards a patient one." + TrainerGrowth,
                8106, 0));
            L.Add(Npc(RogueTrainer, "Nysera Duskveil", "Rogue Trainer", 1719, 12,
                "You found me. Good - most people walk straight past. Stay in the shadows long enough and they will start to feel like home." + TrainerGrowth,
                6442, 6442));
            L.Add(Npc(PriestTrainer, "Serelith Moonvale", "Priest Trainer", 1681, 12,
                "Be welcome in Elune's light. Mend the wounded, shield the weak, and the Goddess will walk beside you." + TrainerGrowth,
                0, 0));
            L.Add(Npc(DruidTrainer, "Oranthir Deeproot", "Druid Trainer", 29510, 12,
                "Can you feel the roots beneath your feet? Everything in the glen is bound together - the cub, the tree and you." + TrainerGrowth,
                18530, 0));

            L.Add(Npc(SentinelAyrith, "Ayrith Moonblade", "Sentinel", 23664, 30,
                "Shadowglen is under the Sentinels' watch. Rest easy near the tree, young one - nothing reaches Aldrassil while we stand here.",
                5925, 0));
            L.Add(Npc(SentinelMaelis, "Maelis Thornwind", "Sentinel", 2034, 30,
                "This road leads out of the glen and on toward Dolanaar. When you have grown strong enough to walk it, you will know.",
                2787, 0));
            L.Add(Npc(SentinelTaelyn, "Taelyn Nightsong", "Sentinel", 25060, 30,
                "The path north runs past the moonwell to the webwood hollows. Watch for silk across the trail - the spiders do not share.",
                5925, 0));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
