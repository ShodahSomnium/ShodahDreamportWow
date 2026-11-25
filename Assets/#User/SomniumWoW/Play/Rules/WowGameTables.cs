using System;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>
    /// The client's gt*.dbc game tables (read from the player's install at runtime): 1 float per row, row = (class-1)*100 + level-1
    /// (measured: gtChanceToMeleeCrit 1100 rows x 1 field, gtChanceToMeleeCritBase 11 rows). Used by the formulas of
    /// Player::GetMeleeCritFromAgility, GetDodgeFromAgility, OCTRegenHPPerSpirit, OCTRegenMPPerSpirit and the spell crit.
    /// Missing tables fall back to 0 (logged once). Pure C#, immutable after Load, thread-safe.
    /// </summary>
    public sealed class WowGameTables
    {
        private readonly DbcTable _meleeCrit, _meleeCritBase, _spellCrit, _spellCritBase, _octRegenHp, _regenHpPerSpt, _regenMpPerSpt;

        private WowGameTables(WowFiles files)
        {
            _meleeCrit = Dbc.Load(files, "gtChanceToMeleeCrit");
            _meleeCritBase = Dbc.Load(files, "gtChanceToMeleeCritBase");
            _spellCrit = Dbc.Load(files, "gtChanceToSpellCrit");
            _spellCritBase = Dbc.Load(files, "gtChanceToSpellCritBase");
            _octRegenHp = Dbc.Load(files, "gtOCTRegenHP");
            _regenHpPerSpt = Dbc.Load(files, "gtRegenHPPerSpt");
            _regenMpPerSpt = Dbc.Load(files, "gtRegenMPPerSpt");
        }

        public static WowGameTables Load(WowFiles files)
        {
            if (files == null) return new WowGameTables(null);
            return new WowGameTables(files);
        }

        private static float Row(DbcTable t, int row)
        {
            if (t == null || row < 0 || row >= t.RecordCount) return 0f;
            return t.GetFloat(row, 0);
        }

        private static int Idx(WowClass c, int level)
        {
            int l = Math.Max(1, Math.Min(100, level));
            return ((int)c - 1) * 100 + l - 1;
        }

        /// <summary>Melee crit percent from agility: (base + agi * ratio) * 100.</summary>
        public float MeleeCritPercent(WowClass c, int level, float agility)
        {
            return (Row(_meleeCritBase, (int)c - 1) + agility * Row(_meleeCrit, Idx(c, level))) * 100f;
        }

        /// <summary>Crit-per-agility ratio (fraction) - also the base of dodge per agility.</summary>
        public float MeleeCritPerAgility(WowClass c, int level) { return Row(_meleeCrit, Idx(c, level)); }

        /// <summary>Spell crit percent from intellect: (base + int * ratio) * 100.</summary>
        public float SpellCritPercent(WowClass c, int level, float intellect)
        {
            return (Row(_spellCritBase, (int)c - 1) + intellect * Row(_spellCrit, Idx(c, level))) * 100f;
        }

        /// <summary>OCTRegenHPPerSpirit: health per 2-second tick out of combat.</summary>
        public float HealthRegenPerTick(WowClass c, int level, float spirit)
        {
            float baseSpirit = spirit > 50 ? 50 : spirit;
            float more = spirit - baseSpirit;
            return baseSpirit * Row(_octRegenHp, Idx(c, level)) + more * Row(_regenHpPerSpt, Idx(c, level));
        }

        /// <summary>UpdateManaRegen: mana per second outside the 5-second rule = sqrt(Int) * Spirit * gtRegenMPPerSpt.</summary>
        public float ManaRegenPerSecond(WowClass c, int level, float intellect, float spirit)
        {
            return (float)Math.Sqrt(Math.Max(0f, intellect)) * spirit * Row(_regenMpPerSpt, Idx(c, level));
        }

        /// <summary>
        /// Player::GetDodgeFromAgility (TrinityCore/AC 3.3.5 shape): dodge_base[class] + agi * critRatio * crit_to_dodge[class] (percent),
        /// ignoring diminishing returns (irrelevant at level 1-10).
        /// </summary>
        public float DodgePercent(WowClass c, int level, float agility)
        {
            float dodgeBase, critToDodge;
            switch (c)
            {
                case WowClass.Warrior: dodgeBase = 0.036640f; critToDodge = 0.85f / 1.15f; break;
                case WowClass.Hunter: dodgeBase = -0.040873f; critToDodge = 1.11f / 1.15f; break;
                case WowClass.Rogue: dodgeBase = 0.020957f; critToDodge = 2.00f / 1.15f; break;
                case WowClass.Priest: dodgeBase = 0.034178f; critToDodge = 1.00f / 1.15f; break;
                case WowClass.Druid: dodgeBase = 0.056097f; critToDodge = 2.00f / 1.15f; break;
                // Iteration 3: the same TrinityCore 3.3.5 arrays, recalled (not fetchable: Player.cpp is too large for the fetch tool).
                case WowClass.Paladin: dodgeBase = 0.034943f; critToDodge = 1.00f / 1.15f; break;
                case WowClass.Shaman: dodgeBase = 0.021080f; critToDodge = 1.60f / 1.15f; break;
                case WowClass.Mage: dodgeBase = 0.036587f; critToDodge = 1.00f / 1.15f; break;
                case WowClass.Warlock: dodgeBase = 0.024211f; critToDodge = 0.97f / 1.15f; break;
                default: dodgeBase = 0.035f; critToDodge = 1.00f / 1.15f; break;
            }
            float v = 100f * (dodgeBase + agility * MeleeCritPerAgility(c, level) * critToDodge);
            return v < 0 ? 0 : v;
        }
    }
}
