using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>One effect of a spell (Spell.dbc arrays [3]).</summary>
    public sealed class SpellEffectInfo
    {
        public int Index;
        public int Effect;                 // SpellEffect.*
        public int Aura;                   // SpellAura.* (Effect == ApplyAura / area aura)
        public int BasePoints, DieSides;   // value = BasePoints + rand(1..DieSides) (DieSides 0 = BasePoints, 1 = BasePoints+1)
        public float PointsPerLevel, PointsPerComboPoint, ValueMultiplier, DamageMultiplier, BonusMultiplier;
        public int TargetA, TargetB, Mechanic, Amplitude;   // Amplitude: periodic interval in ms
        public float Radius;               // yards (SpellRadius)
        public int MiscValue, MiscValueB, TriggerSpell, ChainTargets;
        public int ItemType;               // CREATE_ITEM: the Item.dbc id created (iteration 3)

        /// <summary>Smallest value of this effect at spell level (BasePoints + 1, or BasePoints when DieSides is 0).</summary>
        public int MinValue { get { return BasePoints + (DieSides == 0 ? 0 : (DieSides > 0 ? 1 : DieSides)); } }
        /// <summary>Largest value (BasePoints + DieSides).</summary>
        public int MaxValue { get { return BasePoints + (DieSides == 0 ? 0 : (DieSides > 0 ? DieSides : 1)); } }

        public bool IsAura
        {
            get { return Effect == SpellEffect.ApplyAura || Effect == SpellEffect.ApplyAreaAuraParty || Effect == SpellEffect.ApplyAreaAuraRaid || Effect == SpellEffect.ApplyAreaAuraFriend; }
        }

        public bool TargetsEnemy
        {
            get
            {
                return TargetA == SpellTarget.TargetEnemy || TargetA == SpellTarget.NearbyEnemy || TargetA == SpellTarget.SrcAreaEnemy || TargetA == SpellTarget.DestAreaEnemy
                    || TargetA == SpellTarget.ConeEnemy || TargetB == SpellTarget.SrcAreaEnemy || TargetB == SpellTarget.DestAreaEnemy || TargetB == SpellTarget.ConeEnemy;
            }
        }

        public bool IsAreaEnemy
        {
            get { return TargetA == SpellTarget.SrcAreaEnemy || TargetB == SpellTarget.SrcAreaEnemy || TargetA == SpellTarget.DestAreaEnemy || TargetB == SpellTarget.DestAreaEnemy || TargetA == SpellTarget.ConeEnemy || TargetB == SpellTarget.ConeEnemy; }
        }
    }

    /// <summary>
    /// A spell as read from Spell.dbc + its helper tables (see SpellFields for the verified layout). Immutable after SpellDb built it.
    /// Pure C#.
    /// </summary>
    public sealed class SpellInfo
    {
        public int Id;
        public string Name = "", Rank = "", Description = "", AuraTooltip = "";
        public string IconPath = "";
        public uint[] Attributes = new uint[8];
        public uint Stances, StancesNot;
        public int Category, Dispel, Mechanic, CasterAuraState, TargetAuraState, FacingCasterFlags;
        public int CastTimeMs;             // 0 = instant (negative DBC values, e.g. index 18 = -1000000 for shots, read as 0)
        public int RecoveryTimeMs, CategoryRecoveryTimeMs, StartRecoveryCategory, StartRecoveryTimeMs;
        public int InterruptFlags, AuraInterruptFlags;
        public int BaseLevel, SpellLevel, MaxLevel;
        public int DurationMs, DurationPerLevelMs, MaxDurationMs;   // DurationMs -1 = until cancelled
        public int PowerType;              // 0 mana, 1 rage, 3 energy, -2 health
        public int ManaCost, ManaCostPercentage;
        public float RangeMin, RangeMax, RangeMinFriend, RangeMaxFriend;
        public int RangeFlags;             // 1 melee, 2 ranged
        public float Speed;
        public int StackAmount;
        public int EquippedItemClass = -1, EquippedItemSubClassMask, EquippedItemInventoryTypeMask;
        public int SpellFamilyName;
        public uint[] SpellFamilyFlags = new uint[3];
        public int DmgClass;               // 0 none, 1 magic, 2 melee, 3 ranged
        public int PreventionType, SchoolMask, MaxTargetLevel, MaxAffectedTargets, DescriptionVariablesId, ProcChance, ProcCharges;
        public int ProcFlags;              // PROC_FLAG_* (0x8 taken melee auto attack, 0x20 taken melee ability...) - iteration 3
        public SpellEffectInfo[] Effects = new SpellEffectInfo[3];
        public bool AutoRepeat;            // Auto Shot (SPELL_ATTR2_AUTOREPEAT_FLAG 0x20)
        public int TargetCreatureType;     // mask 1 << (CreatureType - 1), 0 = any (Polymorph: beast|humanoid|critter)
        // iteration 11 (spellfx): Spell.dbc Targets (field 16, m_targets: TARGET_FLAG_* SpellInfo.h:46-67 - 0x40 DEST_LOCATION = the green circle,
        // 0x10 ITEM = the item cursor), the other aura-state / aura-spell needs, the visuals and the rune cost.
        public uint Targets;
        public int CasterAuraStateNot, TargetAuraStateNot, CasterAuraSpell, TargetAuraSpell, ExcludeCasterAuraSpell, ExcludeTargetAuraSpell;
        public int[] SpellVisual = new int[2];   // SpellVisual.dbc ids (fields 131-132)
        public int RuneCostId;                   // SpellRuneCost.dbc id (field 226)
        /// <summary>SPELL_ATTR0_HELD_ITEM_ONLY 0x200 (SharedDefines.h:379 "Client will automatically select main-hand item as cast target"): shaman imbues.</summary>
        public bool HeldItemOnly { get { return (Attributes[0] & 0x200u) != 0; } }
        /// <summary>SPELL_ATTR1_FINISHING_MOVE_DAMAGE 0x00100000 / _DURATION 0x00400000 (SharedDefines.h:427 / 429): needs combo points (Overpower too).</summary>
        public bool NeedsComboPoints { get { return (Attributes[1] & 0x00500000u) != 0; } }

        /// <summary>SPELL_ATTR1_IS_CHANNELED (0x4) or SPELL_ATTR1_IS_SELF_CHANNELED (0x40) (AzerothCore SharedDefines.h): Arcane Missiles, Drain Soul, Cannibalize.</summary>
        public bool IsChanneled { get { return (Attributes[1] & 0x44u) != 0; } }
        /// <summary>SPELL_ATTR1_ALLOW_WHILE_STEALTHED (0x20): casting it does not break Stealth (Sap 0x8040520, measured).</summary>
        public bool AllowWhileStealthed { get { return (Attributes[1] & 0x20u) != 0; } }

        // SPELL_ATTR0_*
        public bool OnNextSwing { get { return (Attributes[0] & 0x4u) != 0 || (Attributes[0] & 0x400u) != 0; } }
        public bool IsAbility { get { return (Attributes[0] & 0x10u) != 0; } }
        public bool IsPassive { get { return (Attributes[0] & 0x40u) != 0; } }
        public bool IsHiddenClientSide { get { return (Attributes[0] & 0x80u) != 0; } }
        public bool NotShapeshift { get { return (Attributes[0] & 0x10000u) != 0; } }
        public bool OnlyStealthed { get { return (Attributes[0] & 0x20000u) != 0; } }
        public bool CantUseInCombat { get { return (Attributes[0] & 0x10000000u) != 0; } }
        public bool ReqAmmo { get { return (Attributes[0] & 0x2u) != 0; } }
        /// <summary>SPELL_ATTR2_NOT_NEED_SHAPESHIFT (0x80000).</summary>
        public bool NotNeedShapeshift { get { return (Attributes[2] & 0x80000u) != 0; } }

        public SpellEffectInfo EffectOf(int effect)
        {
            for (int i = 0; i < 3; i++) if (Effects[i] != null && Effects[i].Effect == effect) return Effects[i];
            return null;
        }

        public SpellEffectInfo AuraOf(int aura)
        {
            for (int i = 0; i < 3; i++) if (Effects[i] != null && Effects[i].IsAura && Effects[i].Aura == aura) return Effects[i];
            return null;
        }

        public bool HasEffect(int effect) { return EffectOf(effect) != null; }
        public bool HasAura(int aura) { return AuraOf(aura) != null; }

        /// <summary>True when any effect hits an enemy (damage, harmful aura on an enemy target...).</summary>
        public bool IsHarmful
        {
            get
            {
                for (int i = 0; i < 3; i++) if (Effects[i] != null && Effects[i].TargetsEnemy) return true;
                return false;
            }
        }

        /// <summary>True when the spell needs a unit target picked by the player (enemy, ally or any).</summary>
        public bool NeedsUnitTarget
        {
            get
            {
                for (int i = 0; i < 3; i++)
                {
                    var e = Effects[i];
                    if (e == null) continue;
                    if (e.TargetA == SpellTarget.TargetEnemy || e.TargetA == SpellTarget.TargetAlly || e.TargetA == SpellTarget.TargetAny) return true;
                }
                return false;
            }
        }

        /// <summary>True when the spell's single target is a friend (heals/buffs cast on a target ally; falls back to self).</summary>
        public bool TargetsAlly
        {
            get
            {
                for (int i = 0; i < 3; i++) if (Effects[i] != null && Effects[i].TargetA == SpellTarget.TargetAlly) return true;
                return false;
            }
        }

        /// <summary>Uses combo points (finishing move): an effect scales with combo points.</summary>
        public bool IsFinisher
        {
            get
            {
                for (int i = 0; i < 3; i++) if (Effects[i] != null && Effects[i].PointsPerComboPoint != 0) return true;
                return (Attributes[1] & 0x100000u) != 0 || (Attributes[1] & 0x400000u) != 0;   // SPELL_ATTR1_REQ_COMBO_POINTS1/2
            }
        }

        /// <summary>Duration in ms at a caster level (-1 = until cancelled, 0 = none).</summary>
        public int DurationAt(int level)
        {
            if (DurationMs < 0) return -1;
            int d = DurationMs + DurationPerLevelMs * Math.Max(0, level - SpellLevel);
            if (MaxDurationMs > 0 && d > MaxDurationMs) d = MaxDurationMs;
            return d;
        }

        public override string ToString() { return Id + " " + Name + (Rank.Length > 0 ? " (" + Rank + ")" : ""); }
    }

    /// <summary>A shapeshift form / stance (SpellShapeshiftForm.dbc).</summary>
    public sealed class ShapeshiftInfo
    {
        public int Id;
        public string Name = "";
        public int Flags1;                 // bit 0: acts as the normal form for spells (stances); clear = "shifted"
        public int CreatureType;
        public int AttackSpeedMs;          // > 0: overrides the weapon (bear 2500, cat 1000)
        public int ModelId;
        public int BonusActionBar;   // SpellShapeshiftForm field 1: the action bar page offset in this form (1..4, ActionButton.lua:139-144), 0 = none (measured, proposal-vrcast s4.3)
        public int[] StanceSpells = new int[8];   // passive spells active while in the form
        public bool ActsAsShifted { get { return (Flags1 & 1) == 0; } }
    }
}
