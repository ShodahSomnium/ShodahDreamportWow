using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>
    /// Numbers the 3.3.5a SERVER owns (they are not in the client files). Sources, fetched from AzerothCore master and quoted in
    /// the Rules report: player_xp_for_level.sql, Formulas.h/.cpp (kill XP), player_class_stats.sql + player_race_stats.sql
    /// (base stats), creature_classlevelstats.sql (creature base health/armor/AP/damage), Creature.cpp SelectLevel, StatSystem.cpp
    /// (attack power, damage, armor, health/mana from stats), Player.cpp (regeneration). Level cap of this iteration: 10.
    /// Pure C#.
    /// </summary>
    public static class WowFormulas
    {
        /// <summary>Level cap of the offline game (iteration 2).</summary>
        public const int MaxLevel = 10;

        // ---------------------------------------------------------------- experience (REAL)

        /// <summary>player_xp_for_level: XP needed to go from level (index) to level+1. Index 0 unused.</summary>
        private static readonly int[] XpTable = { 0, 400, 900, 1400, 2100, 2800, 3600, 4500, 5400, 6500, 7600, 8700, 9800 };

        /// <summary>XP to the next level (0 at the level cap).</summary>
        public static int XpToNextLevel(int level)
        {
            if (level >= MaxLevel || level < 1) return 0;
            return XpTable[level];
        }

        /// <summary>Formulas.h GetGrayLevel.</summary>
        public static int GrayLevel(int playerLevel)
        {
            if (playerLevel <= 5) return 0;
            if (playerLevel <= 39) return playerLevel - 5 - playerLevel / 10;
            if (playerLevel <= 59) return playerLevel - 1 - playerLevel / 5;
            return playerLevel - 9;
        }

        /// <summary>Formulas.h GetZeroDifference.</summary>
        public static int ZeroDifference(int playerLevel)
        {
            if (playerLevel < 8) return 5;
            if (playerLevel < 10) return 6;
            if (playerLevel < 12) return 7;
            if (playerLevel < 16) return 8;
            if (playerLevel < 20) return 9;
            if (playerLevel < 30) return 11;
            if (playerLevel < 40) return 12;
            if (playerLevel < 45) return 13;
            if (playerLevel < 50) return 14;
            if (playerLevel < 55) return 15;
            if (playerLevel < 60) return 16;
            return 17;
        }

        /// <summary>Con colour: 0 gray, 1 green, 2 yellow, 3 orange, 4 red (Formulas.h GetColorCode).</summary>
        public static int ColorCode(int playerLevel, int mobLevel)
        {
            if (mobLevel >= playerLevel + 5) return 4;
            if (mobLevel >= playerLevel + 3) return 3;
            if (mobLevel >= playerLevel - 2) return 2;
            if (mobLevel > GrayLevel(playerLevel)) return 1;
            return 0;
        }

        /// <summary>Formulas.cpp BaseGain for classic content (nBaseExp 45) + Gain's elite x2.</summary>
        public static int KillXp(int playerLevel, int mobLevel, bool elite)
        {
            const int nBaseExp = 45;
            int baseGain;
            if (mobLevel >= playerLevel)
            {
                int diff = mobLevel - playerLevel;
                if (diff > 4) diff = 4;
                baseGain = ((playerLevel * 5 + nBaseExp) * (20 + diff) / 10 + 1) / 2;
            }
            else
            {
                int gray = GrayLevel(playerLevel);
                if (mobLevel > gray)
                {
                    int zd = ZeroDifference(playerLevel);
                    baseGain = (playerLevel * 5 + nBaseExp) * (zd + mobLevel - playerLevel) / zd;
                }
                else baseGain = 0;
            }
            if (elite) baseGain *= 2;
            return baseGain;
        }

        /// <summary>
        /// Quest XP for a player level (AzerothCore Quest::XPValue shape: diffFactor = clamp(2*(questLevel-playerLevel)+20, 1, 10),
        /// xp * diffFactor / 10, then rounded to 5/10/25/50 like the server). baseXp = the quest's reward at its own level.
        /// </summary>
        public static int QuestXp(int baseXp, int questLevel, int playerLevel)
        {
            if (baseXp <= 0) return 0;
            int diffFactor = 2 * (questLevel - playerLevel) + 20;
            if (diffFactor < 1) diffFactor = 1; else if (diffFactor > 10) diffFactor = 10;
            int xp = diffFactor * baseXp / 10;
            if (xp <= 100) xp = 5 * ((xp + 2) / 5);
            else if (xp <= 500) xp = 10 * ((xp + 5) / 10);
            else if (xp <= 1000) xp = 25 * ((xp + 12) / 25);
            else xp = 50 * ((xp + 25) / 50);
            return xp;
        }

        // ---------------------------------------------------------------- player base stats (REAL: player_class_stats + player_race_stats)

        /// <summary>Base stats of a character before items and auras.</summary>
        public struct BaseStats
        {
            public int BaseHealth, BaseMana, Strength, Agility, Stamina, Intellect, Spirit;
        }

        // (BaseHP, BaseMana, Str, Agi, Sta, Int, Spi) per level 1..10.
        private static readonly int[,] Warrior =
        {
            { 20, 0, 23, 20, 22, 20, 20 }, { 29, 0, 24, 21, 23, 20, 20 }, { 38, 0, 25, 21, 24, 20, 21 }, { 47, 0, 26, 22, 25, 20, 21 },
            { 56, 0, 28, 23, 26, 20, 21 }, { 65, 0, 29, 24, 27, 21, 21 }, { 74, 0, 30, 24, 28, 21, 22 }, { 83, 0, 31, 25, 29, 21, 22 },
            { 92, 0, 32, 26, 30, 21, 22 }, { 97, 0, 33, 26, 31, 21, 23 }
        };
        private static readonly int[,] Hunter =
        {
            { 46, 65, 20, 23, 21, 20, 21 }, { 53, 70, 20, 24, 22, 21, 22 }, { 60, 76, 21, 25, 23, 21, 22 }, { 67, 98, 21, 27, 23, 22, 23 },
            { 74, 106, 22, 28, 24, 22, 23 }, { 81, 130, 22, 29, 25, 23, 24 }, { 88, 140, 22, 30, 26, 23, 24 }, { 95, 166, 23, 31, 27, 24, 25 },
            { 102, 193, 23, 33, 28, 24, 26 }, { 109, 206, 24, 34, 28, 25, 26 }
        };
        private static readonly int[,] Rogue =
        {
            { 25, 0, 21, 23, 21, 20, 20 }, { 32, 0, 22, 24, 22, 20, 20 }, { 49, 0, 22, 25, 22, 20, 21 }, { 56, 0, 23, 27, 23, 21, 21 },
            { 63, 0, 24, 28, 24, 21, 21 }, { 70, 0, 24, 29, 24, 21, 22 }, { 87, 0, 25, 31, 25, 21, 22 }, { 94, 0, 26, 32, 25, 21, 22 },
            { 101, 0, 27, 33, 26, 21, 23 }, { 118, 0, 27, 35, 27, 22, 23 }
        };
        private static readonly int[,] Priest =
        {
            { 52, 73, 20, 20, 20, 22, 23 }, { 57, 76, 20, 20, 20, 23, 24 }, { 72, 95, 20, 20, 21, 24, 25 }, { 77, 114, 21, 21, 21, 25, 27 },
            { 92, 133, 21, 21, 21, 27, 28 }, { 101, 152, 21, 21, 22, 28, 29 }, { 112, 171, 21, 21, 22, 29, 30 }, { 117, 190, 21, 22, 22, 30, 31 },
            { 132, 209, 21, 22, 23, 31, 33 }, { 137, 212, 22, 22, 23, 33, 34 }
        };
        private static readonly int[,] Druid =
        {
            { 44, 60, 21, 20, 20, 22, 22 }, { 51, 66, 22, 20, 21, 23, 23 }, { 58, 73, 22, 21, 21, 24, 24 }, { 75, 81, 23, 21, 22, 25, 25 },
            { 82, 90, 23, 22, 22, 26, 26 }, { 89, 100, 24, 22, 23, 27, 27 }, { 106, 111, 24, 23, 24, 28, 28 }, { 113, 123, 25, 23, 24, 28, 29 },
            { 120, 136, 25, 24, 25, 29, 30 }, { 137, 150, 26, 24, 25, 30, 31 }
        };

        // Iteration 3 (player_class_stats.sql, fetched twice from AzerothCore master, identical): Paladin 2, Shaman 7, Mage 8, Warlock 9.
        private static readonly int[,] Paladin =
        {
            { 28, 60, 22, 20, 22, 20, 21 }, { 36, 78, 23, 21, 23, 21, 22 }, { 44, 98, 24, 21, 24, 21, 22 }, { 52, 104, 25, 22, 25, 22, 23 },
            { 60, 111, 26, 22, 26, 22, 24 }, { 68, 134, 27, 23, 27, 23, 24 }, { 76, 143, 28, 23, 28, 24, 25 }, { 84, 153, 29, 24, 28, 24, 25 },
            { 92, 179, 30, 24, 29, 25, 26 }, { 100, 192, 31, 25, 30, 25, 27 }
        };
        private static readonly int[,] Shaman =
        {
            { 40, 85, 21, 20, 21, 21, 22 }, { 47, 91, 22, 20, 22, 22, 23 }, { 55, 98, 22, 21, 23, 23, 24 }, { 62, 106, 23, 21, 24, 23, 25 },
            { 70, 115, 24, 22, 24, 24, 26 }, { 77, 125, 25, 22, 25, 25, 27 }, { 85, 136, 26, 22, 26, 26, 28 }, { 92, 148, 26, 23, 27, 27, 28 },
            { 100, 161, 27, 23, 28, 28, 29 }, { 107, 175, 28, 24, 29, 28, 30 }
        };
        private static readonly int[,] Mage =
        {
            { 32, 100, 20, 20, 20, 23, 22 }, { 47, 110, 20, 20, 20, 24, 23 }, { 52, 106, 20, 20, 21, 25, 24 }, { 67, 118, 20, 21, 21, 27, 25 },
            { 82, 131, 20, 21, 21, 28, 27 }, { 97, 130, 21, 21, 21, 29, 28 }, { 102, 145, 21, 21, 22, 30, 29 }, { 117, 146, 21, 21, 22, 31, 30 },
            { 132, 163, 21, 21, 22, 33, 31 }, { 137, 196, 21, 22, 23, 34, 33 }
        };
        private static readonly int[,] Warlock =
        {
            { 23, 90, 20, 20, 21, 22, 22 }, { 28, 98, 20, 20, 22, 23, 23 }, { 43, 107, 21, 21, 22, 24, 24 }, { 48, 102, 21, 21, 23, 25, 25 },
            { 63, 113, 21, 21, 23, 26, 26 }, { 68, 126, 21, 22, 24, 27, 27 }, { 83, 144, 22, 22, 24, 28, 29 }, { 88, 162, 22, 22, 25, 29, 30 },
            { 93, 180, 22, 23, 25, 30, 31 }, { 108, 198, 23, 23, 26, 31, 32 }
        };

        /// <summary>
        /// player_race_stats.sql (AzerothCore master, quoted in the Rules report): (Str, Agi, Sta, Int, Spi) added to the class stats per race.
        /// Index = ChrRaces id; races without a row add nothing.
        /// </summary>
        private static readonly int[][] RaceStats =
        {
            null,
            new[] { 0, 0, 0, 0, 0 },      // 1 Human
            new[] { 3, -3, 1, -3, 2 },    // 2 Orc
            new[] { 5, -4, 1, -1, -1 },   // 3 Dwarf
            new[] { -4, 4, 0, 0, 0 },     // 4 Night Elf
            new[] { -1, -2, 0, -2, 5 },   // 5 Undead
            new[] { 5, -4, 1, -4, 2 },    // 6 Tauren
            new[] { -5, 2, 0, 3, 0 },     // 7 Gnome
            new[] { 1, 2, 0, -4, 1 },     // 8 Troll
            null,                          // 9 Goblin (not playable in 3.3.5a)
            new[] { -3, 2, 0, 3, -2 },    // 10 Blood Elf
            new[] { 1, -3, 0, 0, 2 },     // 11 Draenei
        };

        /// <summary>The 9 classes of the offline game (every 3.3.5a class except the Death Knight, which starts at 55 elsewhere).</summary>
        public static bool IsSupportedClass(WowClass c)
        {
            return c == WowClass.Warrior || c == WowClass.Paladin || c == WowClass.Hunter || c == WowClass.Rogue || c == WowClass.Priest
                || c == WowClass.Shaman || c == WowClass.Mage || c == WowClass.Warlock || c == WowClass.Druid;
        }

        /// <summary>Base stats for class + level (1..10, clamped) plus the race modifiers of player_race_stats.</summary>
        public static BaseStats PlayerBase(WowClass cls, int level, int race)
        {
            int[,] t;
            switch (cls)
            {
                case WowClass.Paladin: t = Paladin; break;
                case WowClass.Hunter: t = Hunter; break;
                case WowClass.Rogue: t = Rogue; break;
                case WowClass.Priest: t = Priest; break;
                case WowClass.Shaman: t = Shaman; break;
                case WowClass.Mage: t = Mage; break;
                case WowClass.Warlock: t = Warlock; break;
                case WowClass.Druid: t = Druid; break;
                default: t = Warrior; break;
            }
            int i = Math.Max(1, Math.Min(MaxLevel, level)) - 1;
            var s = new BaseStats
            {
                BaseHealth = t[i, 0], BaseMana = t[i, 1], Strength = t[i, 2], Agility = t[i, 3], Stamina = t[i, 4], Intellect = t[i, 5], Spirit = t[i, 6]
            };
            if (race > 0 && race < RaceStats.Length && RaceStats[race] != null)
            {
                var r = RaceStats[race];
                s.Strength += r[0]; s.Agility += r[1]; s.Stamina += r[2]; s.Intellect += r[3]; s.Spirit += r[4];
            }
            return s;
        }

        /// <summary>The race's (Str, Agi, Sta, Int, Spi) modifiers (player_race_stats), for the race screen. Zeros for unknown races.</summary>
        public static int[] RaceStatModifiers(int race)
        {
            if (race > 0 && race < RaceStats.Length && RaceStats[race] != null) return (int[])RaceStats[race].Clone();
            return new int[5];
        }

        /// <summary>The class's main power (ChrClasses DisplayPower; hunters use mana in 3.3.5a).</summary>
        public static PowerType ClassPower(WowClass cls)
        {
            switch (cls)
            {
                case WowClass.Warrior: return PowerType.Rage;
                case WowClass.Rogue: return PowerType.Energy;
                case WowClass.DeathKnight: return PowerType.RunicPower;
                default: return PowerType.Mana;
            }
        }

        /// <summary>StatSystem.cpp GetHealthBonusFromStamina.</summary>
        public static float HealthFromStamina(float stamina)
        {
            float baseStam = stamina < 20 ? stamina : 20;
            return baseStam + (stamina - baseStam) * 10f;
        }

        /// <summary>StatSystem.cpp GetManaBonusFromIntellect.</summary>
        public static float ManaFromIntellect(float intellect)
        {
            float baseInt = intellect < 20 ? intellect : 20;
            return baseInt + (intellect - baseInt) * 15f;
        }

        /// <summary>StatSystem.cpp UpdateAttackPowerAndDamage, melee. form: 0 none, 1 cat, 5 bear, 8 dire bear.</summary>
        public static float MeleeAttackPower(WowClass cls, int level, float str, float agi, int form)
        {
            switch (cls)
            {
                case WowClass.Warrior: case WowClass.Paladin: case WowClass.DeathKnight:
                    return level * 3f + str * 2f - 20f;
                case WowClass.Hunter: case WowClass.Shaman: case WowClass.Rogue:
                    return level * 2f + str + agi - 20f;
                case WowClass.Druid:
                    if (form == 1) return str * 2f + agi - 20f;
                    if (form == 5 || form == 8) return str * 2f - 20f;
                    return str * 2f - 20f;
                case WowClass.Priest: case WowClass.Mage: case WowClass.Warlock:
                    return str - 10f;
            }
            return str - 10f;
        }

        /// <summary>StatSystem.cpp UpdateAttackPowerAndDamage, ranged.</summary>
        public static float RangedAttackPower(WowClass cls, int level, float agi, int form)
        {
            switch (cls)
            {
                case WowClass.Hunter: return level * 2f + agi - 10f;
                case WowClass.Rogue: case WowClass.Warrior: return level + agi - 10f;
                case WowClass.Druid: return (form == 1 || form == 5 || form == 8) ? 0f : agi - 10f;
            }
            return agi - 10f;
        }

        /// <summary>Physical damage kept after armor (Unit::CalcArmorReducedDamage, attacker level &lt; 60): 1 - armor/(armor+400+85*level), reduction capped at 75%.</summary>
        public static float ArmorMultiplier(float armor, int attackerLevel)
        {
            if (armor <= 0) return 1f;
            float lvl = attackerLevel;
            if (lvl > 59) lvl = lvl + 4.5f * (lvl - 59);
            float t = 0.1f * armor / (8.5f * lvl + 40f);
            t = t / (1f + t);
            if (t > 0.75f) t = 0.75f;
            return 1f - t;
        }

        /// <summary>WotLK rage conversion value c for a level (TrinityCore/AC RewardRage).</summary>
        public static float RageConversion(int level)
        {
            float c = 0.0091107836f * level * level + 3.225598133f * level + 4.2652911f;
            if (level > 70) c += 13.27f * (level - 70);
            return c;
        }

        /// <summary>Rage (display units, 1 = one rage point) the attacker gets from a white hit: (dmg/c*7.5 + f)/2, f = speed*3.5 (x2 crit, 1.75 off hand).</summary>
        public static float RageFromDealt(int damage, int level, float weaponSpeed, bool crit, bool offHand)
        {
            float f = weaponSpeed * (offHand ? 1.75f : 3.5f);
            if (crit) f *= 2f;
            float r = (damage / RageConversion(level) * 7.5f + f) / 2f;
            return r;
        }

        /// <summary>Rage (display units) the victim gets from damage taken: dmg/c*2.5.</summary>
        public static float RageFromTaken(int damage, int level)
        {
            return damage / RageConversion(level) * 2.5f;
        }

        // ---------------------------------------------------------------- creatures (REAL: creature_classlevelstats, expansion 0 columns)

        /// <summary>Creature base values for a unit class (1 warrior, 2 paladin, 8 mage; others use 1) and level.</summary>
        public struct CreatureBase
        {
            public int Health, Mana, Armor, AttackPower, RangedAttackPower;
            public float DamageBase;
        }

        // level 1..12: basehp0, basemana, basearmor, attackpower, rangedattackpower, damage_base
        private static readonly float[,] CreatureWarrior =
        {
            { 42, 0, 8, 9, 1, 0.1321f }, { 55, 0, 20, 11, 1, 0.2888f }, { 71, 0, 33, 13, 1, 0.5547f }, { 86, 0, 68, 15, 1, 0.9435f },
            { 102, 0, 111, 18, 1, 1.3708f }, { 120, 0, 165, 20, 1, 2.0661f }, { 137, 0, 230, 24, 1, 2.7669f }, { 156, 0, 306, 28, 1, 3.1993f },
            { 176, 0, 387, 31, 1, 3.7038f }, { 198, 0, 463, 34, 1, 4.1084f }, { 222, 0, 528, 38, 1, 4.6996f }, { 247, 0, 562, 42, 1, 5.4601f }
        };
        private static readonly float[,] CreaturePaladin =
        {
            { 41, 60, 7, 9, 1, 0.2381f }, { 54, 69, 19, 11, 1, 0.3119f }, { 69, 79, 33, 11, 1, 0.5952f }, { 83, 104, 66, 13, 1, 0.946f },
            { 98, 115, 109, 14, 1, 1.4567f }, { 115, 126, 163, 16, 1, 2.0933f }, { 131, 138, 208, 20, 1, 2.7273f }, { 148, 165, 303, 24, 1, 3.1076f },
            { 166, 178, 369, 27, 1, 3.595f }, { 186, 191, 460, 30, 1, 3.9604f }, { 208, 205, 526, 34, 1, 4.4552f }, { 230, 249, 560, 38, 1, 5.1436f }
        };
        private static readonly float[,] CreatureMage =
        {
            { 40, 120, 5, 8, 10, 0.1781f }, { 52, 147, 16, 10, 10, 0.3233f }, { 67, 174, 28, 12, 10, 0.4793f }, { 81, 202, 57, 13, 11, 0.5714f },
            { 95, 230, 93, 14, 11, 0.768f }, { 111, 259, 139, 15, 11, 1.1985f }, { 126, 289, 194, 15, 11, 2.159f }, { 143, 319, 265, 16, 11, 2.5217f },
            { 160, 350, 339, 17, 11, 3.03f }, { 178, 382, 423, 17, 12, 3.4451f }, { 199, 459, 447, 18, 12, 3.975f }, { 219, 537, 475, 19, 12, 4.6771f }
        };

        public static CreatureBase CreatureStats(int unitClass, int level)
        {
            float[,] t = unitClass == 2 ? CreaturePaladin : unitClass == 8 ? CreatureMage : CreatureWarrior;
            int n = t.GetLength(0);
            int i = Math.Max(1, level) - 1;
            var b = new CreatureBase();
            if (i < n)
            {
                b.Health = (int)t[i, 0]; b.Mana = (int)t[i, 1]; b.Armor = (int)t[i, 2]; b.AttackPower = (int)t[i, 3];
                b.RangedAttackPower = (int)t[i, 4]; b.DamageBase = t[i, 5];
            }
            else
            {
                // Beyond the table (never in this zone): extend linearly from the last two rows (APPROXIMATION).
                int a = n - 2, z = n - 1, k = i - z;
                b.Health = (int)(t[z, 0] + (t[z, 0] - t[a, 0]) * k); b.Mana = (int)(t[z, 1] + (t[z, 1] - t[a, 1]) * k);
                b.Armor = (int)(t[z, 2] + (t[z, 2] - t[a, 2]) * k); b.AttackPower = (int)(t[z, 3] + (t[z, 3] - t[a, 3]) * k);
                b.RangedAttackPower = (int)t[z, 4]; b.DamageBase = t[z, 5] + (t[z, 5] - t[a, 5]) * k;
            }
            return b;
        }

        /// <summary>Creature melee damage range per swing (Creature::CalculateMinMaxDamage): ((dmgBase [x1.5 max] + AP/14) * speed * mod).</summary>
        public static void CreatureDamage(CreatureBase b, float attackSpeed, float damageModifier, out float min, out float max)
        {
            float ap = b.AttackPower / 14f;
            min = (b.DamageBase + ap) * attackSpeed * damageModifier;
            max = (b.DamageBase * 1.5f + ap) * attackSpeed * damageModifier;
        }

        // ---------------------------------------------------------------- weapons without an ItemDef (APPROXIMATION)

        /// <summary>
        /// A sensible starter-weapon damage/speed by Item.dbc weapon subclass when the zone's ItemDef has none (the real numbers live in
        /// the server's item_template, not in the client files). Typical level-1..5 white weapons.
        /// </summary>
        public static void DefaultWeapon(int subclass, out float min, out float max, out float speed)
        {
            switch (subclass)
            {
                case 15: min = 1; max = 3; speed = 1.6f; return;       // dagger
                case 0: case 4: case 7: case 13: min = 2; max = 4; speed = 2.0f; return;   // 1h axe, mace, sword, fist
                case 1: case 5: case 8: case 6: case 17: min = 4; max = 7; speed = 3.0f; return;   // 2h axe, mace, sword, polearm, spear
                case 10: min = 3; max = 5; speed = 2.9f; return;       // staff
                case 2: case 3: case 18: min = 2; max = 4; speed = 2.3f; return;   // bow, gun, crossbow
                case 16: min = 2; max = 4; speed = 2.0f; return;       // thrown
                case 19: min = 3; max = 6; speed = 1.5f; return;       // wand
                default: min = 1; max = 3; speed = 2.0f; return;
            }
        }

        /// <summary>Normalized weapon speed for NORMALIZED_WEAPON_DMG (daggers 1.7, other one-handers 2.4, two-handers 3.3, ranged 2.8).</summary>
        public static float NormalizedSpeed(int subclass, bool ranged)
        {
            if (ranged) return 2.8f;
            switch (subclass)
            {
                case 15: return 1.7f;
                case 1: case 5: case 6: case 8: case 10: case 17: return 3.3f;
                default: return 2.4f;
            }
        }
    }
}
