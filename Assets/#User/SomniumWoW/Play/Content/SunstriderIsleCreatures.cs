using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Content
{
    public static partial class SunstriderIsle
    {
        // Creature displays (CreatureDisplayInfo id -> model, skin; scale = display scale x model scale, read by the harness):
        //   ManaWurm.mdx:        15501 ManaWurmSkinSilver1 1.05, 15589 ManaWurmSkinSilver1 1.15 (mana wyrms).
        //   DruidCat.mdx:        15591 LynxSkinRed 1.15, 20815 LynxSkinRed 1.3 (Eversong lynx; the cubs use 15591 at template scale 0.55).
        //   LesserManaFiend.mdx: 15438 FiendSkin 1.0 (arcane wraiths).
        //   ManaFiendGreen.mdx:  15406 ManaFiendSkinGreen 0.75 (tainted arcane wraiths; template scale 1.3).
        //   Blood elf humanoid display (CreatureDisplayInfoExtra race 10, baked texture exists): 21746 female, dark blue robe with gold
        //   trim and fur, red fel-touched eyes, scale 1.2 (the exiled magistrix).
        // Held weapon (ItemDisplayInfo): 33254 Stave_2H_Blood_A_02.
        // Spell (Spell.dbc, name printed by the ContentD probe): 116 Frostbolt (Rank 1).
        const int SpellFrostbolt = 116;

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

            // Mana wyrms drift over the lawns around the spire, drawn to the ley energy of the isle.
            var wyrm = Beast(ManaWyrm, "Mana Wyrm", new[] { 15501, 15589 }, 1, 2, Reaction.Hostile, 8f);
            wyrm.DamageMultiplier = 0.9f; wyrm.MinMoney = 0; wyrm.MaxMoney = 2;
            wyrm.Loot.Add(Drop(4540, 0.04f));
            wyrm.Loot.Add(Drop(1377, 0.02f));
            L.Add(wyrm);

            // Lynx cubs play on the western slope; they only fight back (neutral), and no quest asks for them.
            var cub = Beast(LynxCub, "Lynx Cub", new[] { 15591 }, 1, 2, Reaction.Neutral, 0f);
            cub.Scale = 0.55f; cub.CreatureType = 1; cub.DamageMultiplier = 0.8f; cub.MinMoney = 0; cub.MaxMoney = 1;
            cub.Loot.Add(Drop(1367, 0.02f));
            L.Add(cub);

            var lynx = Beast(EversongLynx, "Eversong Lynx", new[] { 15591, 20815 }, 2, 3, Reaction.Hostile, 10f);
            lynx.CreatureType = 1; lynx.AttackSpeed = 1.8f; lynx.MinMoney = 1; lynx.MaxMoney = 4;
            lynx.Loot.Add(QuestDrop(ItemLynxPelt, 0.7f, QuestPeltsForTheWatch));
            lynx.Loot.Add(Drop(1370, 0.02f));
            lynx.Loot.Add(Drop(1366, 0.02f));
            L.Add(lynx);

            var wraith = Beast(ArcaneWraith, "Arcane Wraith", new[] { 15438 }, 3, 4, Reaction.Hostile, 12f);
            wraith.WalkSpeed = 2f; wraith.AttackSpeed = 2.2f; wraith.MinMoney = 2; wraith.MaxMoney = 6;
            wraith.Loot.Add(QuestDrop(ItemArcaneResidue, 0.65f, QuestResidueOfTheWraiths));
            wraith.Loot.Add(Drop(159, 0.05f));
            wraith.Loot.Add(Drop(3363, 0.03f));
            wraith.Loot.Add(Drop(1378, 0.02f));
            L.Add(wraith);

            var tainted = Beast(TaintedArcaneWraith, "Tainted Arcane Wraith", new[] { 15406 }, 4, 5, Reaction.Hostile, 12f);
            tainted.Scale = 1.3f; tainted.WalkSpeed = 2f; tainted.AttackSpeed = 2.2f; tainted.MinMoney = 3; tainted.MaxMoney = 8;
            tainted.Loot.Add(QuestDrop(ItemCorruptedCrystal, 0.6f, QuestCorruptedCrystals));
            tainted.Loot.Add(Drop(118, 0.03f));
            tainted.Loot.Add(Drop(1376, 0.02f));
            tainted.Loot.Add(Drop(2211, 0.02f));
            L.Add(tainted);

            // The exile at the far south-west end of the hollow (final quest): a caster, tougher than anything else on the isle.
            var boss = Beast(MagistrixVelanya, "Magistrix Velanya the Exiled", new[] { 21746 }, 5, 5, Reaction.Hostile, 15f);
            boss.CreatureType = 7; boss.UnitClass = 8; boss.MainHandDisplayId = 33254;
            boss.HealthMultiplier = 3f; boss.DamageMultiplier = 1.3f; boss.AttackSpeed = 2.4f;
            boss.Spells = new[] { SpellFrostbolt }; boss.SpellInterval = 7f;
            boss.MinMoney = 60; boss.MaxMoney = 110;
            boss.Loot.Add(Drop(118, 0.5f));
            boss.Loot.Add(Drop(1364, 0.25f));
            boss.Loot.Add(Drop(3363, 0.25f));
            L.Add(boss);
        }

        // Blood Elf NPC displays: CreatureDisplayInfoExtra race 10 (BloodElfMale.m2 / BloodElfFemale.m2) with an existing
        // Textures\BakedNpcTextures\ file; picked by looking at the baked textures (harness out_zonesD/si_bakes.png):
        //   19124 m red-and-black robe with gold (scale 1.1), 15908 f teal-and-gold leather, 19155 f crimson robe with a green gem,
        //   16624 m black-and-gold plate with red, 17815 m crimson plate (scale 1.25), 16290 f red-and-gold leather, 16818 m black
        //   leather with straps, 16812 f violet-and-brown cloth with gold, 16769 m green-and-violet cloth, 16822 m olive-and-violet
        //   cloth, 17988 m red-brown leather with a helmet, 16765 / 16804 f violet-and-gold with a helmet (the isle's guardians).
        // Held weapons (ItemDisplayInfo): 33234 Stave_2H_Blood_A_01, 38084 Bow_1H_Blood_A_02, 39332 Wand_1H_Blood_A_01,
        //   38590 Offhand_Blood_A_02, 33220 Sword_1H_Blood_A_01, 33305 Shield_Blood_A_01, 38854 Mace_2H_Blood_A_01,
        //   37000 Bow_1H_Blood_B_01, 38633 Knife_1H_Blood_A_03, 38853 Knife_1H_Blood_A_04, 39488 Mace_1H_Blood_A_01,
        //   41453 Offhand_Blood_A_01, 33254 Stave_2H_Blood_A_02, 38457 Knife_1H_Blood_A_05, 37410 Polearm_2H_Blood_Elf_D_01,
        //   36725 Sword_1H_Blood_A_03, 33291 Shield_Blood_A_02.
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

            L.Add(Npc(MagisterAerondis, "Magister Aerondis", "Warden of the Isle", 19124, 10,
                "Welcome to your first morning on the isle. Every one of us began here, on these same lawns, with the same wide eyes. Breathe - and then let us see what you are made of.",
                33234, 0));
            L.Add(Npc(RangerTalvaria, "Ranger Talvaria", "Isle Ranger", 15908, 10,
                "Quiet steps, sharp eyes. The isle looks gentle, but half of what lives on it would happily take a bite out of a careless novice.",
                38084, 0));
            L.Add(Npc(ArcanistVeyselle, "Arcanist Veyselle", "Scholar of the Ley Lines", 19155, 10,
                "Can you feel it humming under your feet? The ley lines run close to the surface here. Most days that is a blessing. Lately, it has been something else.",
                39332, 38590));
            L.Add(Npc(LieutenantDathrien, "Lieutenant Dathrien", "Sunstrider Watch", 16624, 12,
                "Stay behind the line of trees unless you mean to fight. Whatever is spreading through that hollow does not care that you are new here.",
                33220, 33305));

            L.Add(Npc(PaladinTrainer, "Saelthar", "Paladin Trainer", 17815, 12,
                "The Light is not a gift we were born with - it is one we have chosen to take up. Carry it with pride, and carry it for others." + TrainerGrowth,
                38854, 0));
            L.Add(Npc(HunterTrainer, "Nerilla", "Hunter Trainer", 16290, 12,
                "Watch the lynxes before you ever hunt one. See how they wait, how they choose their moment. Then do it better." + TrainerGrowth,
                37000, 0));
            L.Add(Npc(RogueTrainer, "Kaeldris", "Rogue Trainer", 16818, 12,
                "You found me, so you are not entirely hopeless. The rest is patience, soft boots and knowing exactly where to put the blade." + TrainerGrowth,
                38633, 38853));
            L.Add(Npc(PriestTrainer, "Solaira", "Priest Trainer", 16812, 12,
                "Faith is a discipline, like any other. Pray with care, heal with purpose, and never let pride speak louder than compassion." + TrainerGrowth,
                39488, 41453));
            L.Add(Npc(MageTrainer, "Vaeryn", "Mage Trainer", 16769, 12,
                "Magic is our birthright and our temptation. Learn to shape it with a clear head, and never take more than you need." + TrainerGrowth,
                33254, 0));
            L.Add(Npc(WarlockTrainer, "Zarethil", "Warlock Trainer", 16822, 12,
                "Some call our art dangerous. They are right. Power always is - which is why it belongs in steady, disciplined hands." + TrainerGrowth,
                38457, 41453));

            L.Add(Npc(GuardianLorathil, "Lorathil", "Isle Guardian", 17988, 30,
                "Rest easy by the spire, novice. Nothing on this isle will trouble you while we stand watch.",
                37410, 0));
            L.Add(Npc(GuardianFaeleth, "Faeleth", "Isle Guardian", 16765, 30,
                "If you fall out there, you will wake here, by the graveyard. It is not a lesson anyone enjoys twice.",
                37410, 0));
            L.Add(Npc(GuardianIrindel, "Irindel", "Isle Guardian", 16804, 30,
                "This path leads south to the lynx meadows and the hollow beyond. Keep your wits about you once you pass the old ring.",
                36725, 33291));
        }

        /// <summary>The template with this entry, or null (linear search; zone data is small).</summary>
        public static CreatureTemplate FindCreature(ZoneContent z, int entry)
        {
            foreach (var c in z.Creatures) if (c.Entry == entry) return c;
            return null;
        }
    }
}
