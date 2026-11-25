namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>
    /// Field indices of the spell tables of the 3.3.5a (12340) client, from AzerothCore DBCStructure.h (SpellEntry etc., comments
    /// give the m_ names) and verified on the real files by the Rules probes: Spell.dbc 234 fields x 4 = 936 B/row, names/ranks/
    /// descriptions read cleanly at 136/153/170/187 for all 49839 rows; SpellIcon 2, SpellCastTimes 4, SpellDuration 4,
    /// SpellRange 40, SpellRadius 4, SkillLineAbility 14, SkillLine 56, SkillRaceClassInfo 8, Talent 23, SpellShapeshiftForm 35,
    /// SpellDescriptionVariables 2, QuestXP 11 (see the Rules report for sample rows). Arrays: constant = element 0.
    /// </summary>
    public static class SpellFields
    {
        public static class Spell
        {
            public const int FieldCount = 234;
            public const int Id = 0, Category = 1, Dispel = 2, Mechanic = 3;
            public const int Attributes = 4;            // Attributes .. AttributesEx7 = 4..11
            public const int Stances = 12, StancesNot = 14, Targets = 16, TargetCreatureType = 17;   // TargetCreatureType: mask of 1 << (CreatureType - 1)
            public const int FacingCasterFlags = 19, CasterAuraState = 20, TargetAuraState = 21;
            public const int CastingTimeIndex = 28, RecoveryTime = 29, CategoryRecoveryTime = 30, InterruptFlags = 31, AuraInterruptFlags = 32;
            public const int ProcFlags = 34, ProcChance = 35, ProcCharges = 36;
            public const int MaxLevel = 37, BaseLevel = 38, SpellLevel = 39, DurationIndex = 40, PowerType = 41, ManaCost = 42;
            public const int ManaCostPerLevel = 43, RangeIndex = 46, Speed = 47, StackAmount = 49;
            public const int EquippedItemClass = 68, EquippedItemSubClassMask = 69, EquippedItemInventoryTypeMask = 70;
            public const int Effect = 71, EffectDieSides = 74, EffectRealPointsPerLevel = 77, EffectBasePoints = 80, EffectMechanic = 83;
            public const int EffectImplicitTargetA = 86, EffectImplicitTargetB = 89, EffectRadiusIndex = 92, EffectApplyAuraName = 95;
            public const int EffectAmplitude = 98, EffectValueMultiplier = 101, EffectChainTarget = 104, EffectItemType = 107;
            public const int EffectMiscValue = 110, EffectMiscValueB = 113, EffectTriggerSpell = 116, EffectPointsPerComboPoint = 119;
            public const int SpellIconID = 133, ActiveIconID = 134;
            public const int SpellName = 136, Rank = 153, Description = 170, ToolTip = 187;   // locstrings (16 slots + flags)
            public const int ManaCostPercentage = 204, StartRecoveryCategory = 205, StartRecoveryTime = 206, MaxTargetLevel = 207;
            public const int SpellFamilyName = 208, SpellFamilyFlags = 209, MaxAffectedTargets = 212, DmgClass = 213, PreventionType = 214;
            public const int EffectDamageMultiplier = 216, SchoolMask = 225, EffectBonusMultiplier = 229, SpellDescriptionVariableID = 232;
            // iteration 11 (spellfx; AC DBCStructure.h SpellEntry 22-226, measured by the D11 SfxSpells probe):
            public const int CasterAuraStateNot = 22, TargetAuraStateNot = 23, CasterAuraSpell = 24, TargetAuraSpell = 25;
            public const int ExcludeCasterAuraSpell = 26, ExcludeTargetAuraSpell = 27, Totem = 50, Reagent = 52, ReagentCount = 60;
            public const int SpellVisual = 131, TotemCategory = 222, AreaGroupId = 224, RuneCostID = 226;   // SpellVisual[2] = 131..132
        }

        /// <summary>SpellRuneCost.dbc (DBCfmt.h SpellRuneCostfmt "niiii"): fields 1-3 in RuneType order (0 blood, 1 unholy, 2 frost - measured:
        /// Icy Touch 241 = 0/0/1, Plague Strike 221 = 0/1/0; the comment "1=frost, 2=unholy" at DBCStructure.h:1843 is wrong), 4 = runic power gain.</summary>
        public static class SpellRuneCost { public const int FieldCount = 5, Id = 0, Blood = 1, Unholy = 2, Frost = 3, RunicPowerGain = 4; }

        public static class SpellIcon { public const int Id = 0, TextureFilename = 1; }
        public static class SpellCastTimes { public const int Id = 0, Base = 1, PerLevel = 2, Minimum = 3; }
        public static class SpellDuration { public const int Id = 0, Duration = 1, DurationPerLevel = 2, MaxDuration = 3; }
        public static class SpellRange { public const int Id = 0, RangeMinHostile = 1, RangeMinFriend = 2, RangeMaxHostile = 3, RangeMaxFriend = 4, Flags = 5, DisplayName = 6; }
        public static class SpellRadius { public const int Id = 0, Radius = 1, RadiusPerLevel = 2, RadiusMax = 3; }
        public static class SkillLineAbility { public const int Id = 0, SkillLine = 1, Spell = 2, RaceMask = 3, ClassMask = 4, MinSkillLineRank = 7, SupercededBySpell = 8, AcquireMethod = 9; }
        public static class SkillLine { public const int Id = 0, CategoryId = 1, DisplayName = 3, SpellIcon = 37; }
        public static class SkillRaceClassInfo { public const int Id = 0, SkillId = 1, RaceMask = 2, ClassMask = 3, Flags = 4; }
        public static class Talent { public const int Id = 0, TabId = 1, RankId = 4; }   // RankId[5] = 4..8 (9..12 unused)
        public static class SpellShapeshiftForm { public const int Id = 0, BonusActionBar = 1, Name = 2, Flags1 = 19, CreatureType = 20, AttackSpeed = 22, ModelIdA = 23, StanceSpell = 27; }
        public static class SpellDescriptionVariables { public const int Id = 0, Variables = 1; }

        /// <summary>SkillLine.dbc category of class skill lines ("Arms", "Holy", "Feral Combat" ...).</summary>
        public const int SkillCategoryClass = 7;
    }

    /// <summary>SpellEffects values (AzerothCore SharedDefines.h; verified on the DBC, see SpellFields).</summary>
    public static class SpellEffect
    {
        public const int Instakill = 1, SchoolDamage = 2, Dummy = 3, TeleportUnits = 5, ApplyAura = 6, HealthLeech = 9, Heal = 10;
        public const int WeaponDamageNoSchool = 17, Dodge = 20, Parry = 22, Block = 23, Weapon = 25, Defense = 26;
        public const int Energize = 30, WeaponPercentDamage = 31, OpenLock = 33, ApplyAreaAuraParty = 35, LearnSpell = 36;
        public const int DualWield = 40, SummonPet = 56, WeaponDamage = 58, Proficiency = 60, Threat = 63, TriggerSpell = 64;
        public const int ApplyAreaAuraRaid = 65, InterruptCast = 68, Pickpocket = 71, ScriptEffect = 77, Attack = 78;
        public const int AddComboPoints = 80, Charge = 96, TameCreature = 55, Skill = 118, NormalizedWeaponDmg = 121;
        public const int ApplyAreaAuraFriend = 128, HealPct = 136, EnergizePct = 137, AttackMe = 114, ResurrectNew = 113, DismissPet = 102, FeedPet = 101, ResurrectPet = 109;
        // iteration 3 (AzerothCore SharedDefines.h, quoted in the report):
        public const int CreateItem = 24, Summon = 28, Dispel = 38, EnchantItemTemporary = 54, HealMaxHealth = 67, DispelMechanic = 108;
        // iteration 11 (spellfx; AC SharedDefines.h SpellEffects):
        public const int PersistentAreaAura = 27, EnchantItem = 53, Distract = 69, AddFarsight = 72;
    }

    /// <summary>AuraType values (AzerothCore SpellAuraDefines.h; verified on the DBC).</summary>
    public static class SpellAura
    {
        public const int None = 0, PeriodicDamage = 3, Dummy = 4, PeriodicHeal = 8, ModAttackSpeed = 9, ModThreat = 10, ModTaunt = 11;
        public const int ModStun = 12, ModDamageDone = 13, ModDamageTaken = 14, DamageShield = 15, ModStealth = 16, ModResistance = 22;
        public const int PeriodicTriggerSpell = 23, PeriodicEnergize = 24, ModPacify = 25, ModRoot = 26, ModSilence = 27, ModStat = 29;
        public const int ModIncreaseSpeed = 31, ModDecreaseSpeed = 33, ModIncreaseHealth = 34, ModShapeshift = 36, ProcTriggerSpell = 42;
        public const int TrackCreatures = 44, ModParryPercent = 47, ModDodgePercent = 49, ModBlockPercent = 51, ModWeaponCritPercent = 52;
        public const int PeriodicLeech = 53, ModHitChance = 54, SchoolAbsorb = 69, ModDamagePercentDone = 79, ModPercentStat = 80;
        public const int ModBaseResistance = 83, ModRegen = 84, ModPowerRegen = 85, ModDamagePercentTaken = 87, ModHealthRegenPercent = 88;
        public const int InterruptRegen = 94, ModAttackPower = 99, ModResistancePct = 101, ModTotalThreat = 103, ModRangedAttackPower = 124;
        public const int RangedAttackPowerAttackerBonus = 127, ModIncreaseHealthPercent = 133, ModTotalStatPercentage = 137;
        public const int ModMeleeHaste = 138, ModRangedHaste = 140, ModBaseResistancePct = 142, ModStalked = 68, MechanicImmunity = 77;
        public const int ArmorPenetrationPct = 280;
        // iteration 3 (AzerothCore SpellAuraDefines.h, quoted in the report):
        public const int ModConfuse = 5, ModFear = 7, ObsModHealth = 20, SchoolImmunity = 39, DispelImmunity = 41, ModSpellHitChance = 55, Transform = 56;
        public const int ModSpellCritChance = 57, ModTotalThreatAura = 103, AddFlatModifier = 107, AddPctModifier = 108, ModRegenDuringCombat = 116, ModHealingPct = 118;
        public const int ModDamageDoneVersus = 168, ModAttackerMeleeHitChance = 184, ModAttackerRangedHitChance = 185, ModAttackerSpellHitChance = 186;
        public const int MeleeSlow = 193, PeriodicDummy = 226, ModIncreaseHealth2 = 230, MechanicDurationMod = 232, ModExpertise = 240, ModBaseHealthPct = 282;
    }

    /// <summary>Implicit targets used by the offline rules.</summary>
    public static class SpellTarget
    {
        public const int UnitCaster = 1, NearbyEnemy = 2, TargetEnemy = 6, SrcAreaEnemy = 15, DestAreaEnemy = 16, DestCaster = 18;
        public const int CasterAreaParty = 20, TargetAlly = 21, SrcCaster = 22, ConeEnemy = 24, TargetAny = 25, SrcAreaAlly = 30;
        public const int TargetParty = 57, CasterAreaRaid = 56, TargetRaid = 61;
    }
}
