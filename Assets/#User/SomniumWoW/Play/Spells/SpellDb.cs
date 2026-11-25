using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using F = SomniumSpace.Worlds.SomniumWoW.Play.Spells.SpellFields;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>A spell of a class kit: learned automatically when the character reaches Level.</summary>
    public sealed class KitSpell
    {
        public int SpellId;
        public int Level;                  // Spell.dbc SpellLevel (the trainer level)
        public string Name = "";           // rank group key (ranks share the name)
        public string Rank = "";
        public int SkillLine;
    }

    /// <summary>
    /// Spell data read from the player's Spell.dbc (+ SpellIcon, SpellCastTimes, SpellDuration, SpellRange, SpellRadius,
    /// SkillLineAbility, SkillLine, SkillRaceClassInfo, Talent, TalentTab, SpellShapeshiftForm, SpellDescriptionVariables) at runtime.
    /// Nothing is hard-coded except ids. SpellInfo objects are built lazily and cached; everything is thread-safe.
    /// Get(files) is cached per WowFiles (the first call reads Spell.dbc, ~47 MB: call SpellDb.Get on a worker thread during loading).
    /// </summary>
    public sealed class SpellDb
    {
        private static readonly object CacheLock = new object();
        private static WowFiles _cachedFiles;
        private static SpellDb _cached;

        private readonly WowFiles _files;
        private readonly DbcTable _spell, _icon, _cast, _dur, _range, _radius, _forms, _descVars;
        private readonly Dictionary<int, SpellInfo> _infos = new Dictionary<int, SpellInfo>();
        private readonly Dictionary<int, ShapeshiftInfo> _formInfos = new Dictionary<int, ShapeshiftInfo>();
        private readonly Dictionary<int, List<KitSpell>> _kits = new Dictionary<int, List<KitSpell>>();
        private readonly object _lock = new object();

        /// <summary>True when Spell.dbc was read (false = no game files: no spells at all).</summary>
        public bool Available { get { return _spell != null; } }
        public WowFiles Files { get { return _files; } }

        private SpellDb(WowFiles files)
        {
            _files = files;
            if (files == null) return;
            _spell = Dbc.Load(files, "Spell");
            _icon = Dbc.Load(files, "SpellIcon");
            _cast = Dbc.Load(files, "SpellCastTimes");
            _dur = Dbc.Load(files, "SpellDuration");
            _range = Dbc.Load(files, "SpellRange");
            _radius = Dbc.Load(files, "SpellRadius");
            _forms = Dbc.Load(files, "SpellShapeshiftForm");
            _descVars = Dbc.Load(files, "SpellDescriptionVariables");
            if (_spell != null && _spell.FieldCount != F.Spell.FieldCount)
                WowLog.Warn("SpellDb: Spell.dbc has " + _spell.FieldCount + " fields, expected " + F.Spell.FieldCount + " (wrong client build?) - spells may be wrong");
        }

        /// <summary>The spell database of these files (cached; files null = an empty database).</summary>
        public static SpellDb Get(WowFiles files)
        {
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
            }
            var t0 = DateTime.UtcNow;
            var db = new SpellDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            WowLog.Info("SpellDb: " + (db._spell == null ? "no Spell.dbc" : db._spell.RecordCount + " spells") + " read in " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms");
            return db;
        }

        /// <summary>A spell by id, or null.</summary>
        public SpellInfo Find(int id)
        {
            if (_spell == null || id <= 0) return null;
            lock (_lock)
            {
                SpellInfo s;
                if (_infos.TryGetValue(id, out s)) return s;
            }
            int r = _spell.FindRow(id);
            SpellInfo info = r < 0 ? null : Build(r);
            lock (_lock) { _infos[id] = info; }
            return info;
        }

        private SpellInfo Build(int r)
        {
            var t = _spell;
            var s = new SpellInfo();
            s.Id = t.GetId(r);
            s.Name = t.GetLocalizedString(r, F.Spell.SpellName);
            s.Rank = t.GetLocalizedString(r, F.Spell.Rank);
            s.Description = t.GetLocalizedString(r, F.Spell.Description);
            s.AuraTooltip = t.GetLocalizedString(r, F.Spell.ToolTip);
            for (int i = 0; i < 8; i++) s.Attributes[i] = t.GetUInt(r, F.Spell.Attributes + i);
            s.Category = t.GetInt(r, F.Spell.Category);
            s.Dispel = t.GetInt(r, F.Spell.Dispel);
            s.Mechanic = t.GetInt(r, F.Spell.Mechanic);
            s.Stances = t.GetUInt(r, F.Spell.Stances);
            s.StancesNot = t.GetUInt(r, F.Spell.StancesNot);
            s.FacingCasterFlags = t.GetInt(r, F.Spell.FacingCasterFlags);
            s.CasterAuraState = t.GetInt(r, F.Spell.CasterAuraState);
            s.TargetAuraState = t.GetInt(r, F.Spell.TargetAuraState);
            s.RecoveryTimeMs = t.GetInt(r, F.Spell.RecoveryTime);
            s.CategoryRecoveryTimeMs = t.GetInt(r, F.Spell.CategoryRecoveryTime);
            s.InterruptFlags = t.GetInt(r, F.Spell.InterruptFlags);
            s.AuraInterruptFlags = t.GetInt(r, F.Spell.AuraInterruptFlags);
            s.ProcChance = t.GetInt(r, F.Spell.ProcChance);
            s.ProcCharges = t.GetInt(r, F.Spell.ProcCharges);
            s.ProcFlags = t.GetInt(r, F.Spell.ProcFlags);
            s.MaxLevel = t.GetInt(r, F.Spell.MaxLevel);
            s.BaseLevel = t.GetInt(r, F.Spell.BaseLevel);
            s.SpellLevel = t.GetInt(r, F.Spell.SpellLevel);
            s.PowerType = t.GetInt(r, F.Spell.PowerType);
            s.ManaCost = t.GetInt(r, F.Spell.ManaCost);
            s.ManaCostPercentage = t.GetInt(r, F.Spell.ManaCostPercentage);
            s.Speed = t.GetFloat(r, F.Spell.Speed);
            s.StackAmount = t.GetInt(r, F.Spell.StackAmount);
            s.EquippedItemClass = t.GetInt(r, F.Spell.EquippedItemClass);
            s.EquippedItemSubClassMask = t.GetInt(r, F.Spell.EquippedItemSubClassMask);
            s.EquippedItemInventoryTypeMask = t.GetInt(r, F.Spell.EquippedItemInventoryTypeMask);
            s.StartRecoveryCategory = t.GetInt(r, F.Spell.StartRecoveryCategory);
            s.StartRecoveryTimeMs = t.GetInt(r, F.Spell.StartRecoveryTime);
            s.MaxTargetLevel = t.GetInt(r, F.Spell.MaxTargetLevel);
            s.SpellFamilyName = t.GetInt(r, F.Spell.SpellFamilyName);
            for (int i = 0; i < 3; i++) s.SpellFamilyFlags[i] = t.GetUInt(r, F.Spell.SpellFamilyFlags + i);
            s.MaxAffectedTargets = t.GetInt(r, F.Spell.MaxAffectedTargets);
            s.DmgClass = t.GetInt(r, F.Spell.DmgClass);
            s.PreventionType = t.GetInt(r, F.Spell.PreventionType);
            s.SchoolMask = t.GetInt(r, F.Spell.SchoolMask);
            s.DescriptionVariablesId = t.GetInt(r, F.Spell.SpellDescriptionVariableID);
            s.AutoRepeat = (s.Attributes[2] & 0x20u) != 0;   // SPELL_ATTR2_AUTOREPEAT_FLAG (Auto Shot attr2 0x20, measured)
            s.TargetCreatureType = t.GetInt(r, F.Spell.TargetCreatureType);
            // iteration 11 (spellfx): the Targets column (the circle / the item cursor), aura needs, visuals, rune cost (SpellFields).
            s.Targets = t.GetUInt(r, F.Spell.Targets);
            s.CasterAuraStateNot = t.GetInt(r, F.Spell.CasterAuraStateNot);
            s.TargetAuraStateNot = t.GetInt(r, F.Spell.TargetAuraStateNot);
            s.CasterAuraSpell = t.GetInt(r, F.Spell.CasterAuraSpell);
            s.TargetAuraSpell = t.GetInt(r, F.Spell.TargetAuraSpell);
            s.ExcludeCasterAuraSpell = t.GetInt(r, F.Spell.ExcludeCasterAuraSpell);
            s.ExcludeTargetAuraSpell = t.GetInt(r, F.Spell.ExcludeTargetAuraSpell);
            s.SpellVisual[0] = t.GetInt(r, F.Spell.SpellVisual);
            s.SpellVisual[1] = t.GetInt(r, F.Spell.SpellVisual + 1);
            s.RuneCostId = t.GetInt(r, F.Spell.RuneCostID);

            // Icon
            int iconId = t.GetInt(r, F.Spell.SpellIconID);
            if (_icon != null && iconId > 0)
            {
                int ir = _icon.FindRow(iconId);
                if (ir >= 0) s.IconPath = _icon.GetString(ir, F.SpellIcon.TextureFilename);
            }
            // Cast time
            int castIdx = t.GetInt(r, F.Spell.CastingTimeIndex);
            if (_cast != null && castIdx > 0)
            {
                int cr = _cast.FindRow(castIdx);
                if (cr >= 0)
                {
                    int ms = _cast.GetInt(cr, F.SpellCastTimes.Base);
                    if (ms < 0) ms = 0;   // index 18 = -1000000 (Auto Shot, Serpent Sting, Arcane Shot...): instant
                    s.CastTimeMs = ms;
                }
            }
            // Duration
            int durIdx = t.GetInt(r, F.Spell.DurationIndex);
            if (_dur != null && durIdx > 0)
            {
                int dr = _dur.FindRow(durIdx);
                if (dr >= 0)
                {
                    s.DurationMs = _dur.GetInt(dr, F.SpellDuration.Duration);
                    s.DurationPerLevelMs = _dur.GetInt(dr, F.SpellDuration.DurationPerLevel);
                    s.MaxDurationMs = _dur.GetInt(dr, F.SpellDuration.MaxDuration);
                    // Measured oddity: SpellDuration id 2 stores 300000010 as base with max 30000 - trust the max when base > max.
                    if (s.MaxDurationMs > 0 && s.DurationMs > s.MaxDurationMs) s.DurationMs = s.MaxDurationMs;
                }
            }
            // Range
            int rangeIdx = t.GetInt(r, F.Spell.RangeIndex);
            if (_range != null && rangeIdx > 0)
            {
                int rr = _range.FindRow(rangeIdx);
                if (rr >= 0)
                {
                    s.RangeMin = _range.GetFloat(rr, F.SpellRange.RangeMinHostile);
                    s.RangeMinFriend = _range.GetFloat(rr, F.SpellRange.RangeMinFriend);
                    s.RangeMax = _range.GetFloat(rr, F.SpellRange.RangeMaxHostile);
                    s.RangeMaxFriend = _range.GetFloat(rr, F.SpellRange.RangeMaxFriend);
                    s.RangeFlags = _range.GetInt(rr, F.SpellRange.Flags);
                }
            }
            // Effects
            for (int e = 0; e < 3; e++)
            {
                int eff = t.GetInt(r, F.Spell.Effect + e);
                if (eff == 0) continue;
                var x = new SpellEffectInfo();
                x.Index = e;
                x.Effect = eff;
                x.Aura = t.GetInt(r, F.Spell.EffectApplyAuraName + e);
                x.BasePoints = t.GetInt(r, F.Spell.EffectBasePoints + e);
                x.DieSides = t.GetInt(r, F.Spell.EffectDieSides + e);
                x.PointsPerLevel = t.GetFloat(r, F.Spell.EffectRealPointsPerLevel + e);
                x.PointsPerComboPoint = t.GetFloat(r, F.Spell.EffectPointsPerComboPoint + e);
                x.ValueMultiplier = t.GetFloat(r, F.Spell.EffectValueMultiplier + e);
                x.DamageMultiplier = t.GetFloat(r, F.Spell.EffectDamageMultiplier + e);
                x.BonusMultiplier = t.GetFloat(r, F.Spell.EffectBonusMultiplier + e);
                x.TargetA = t.GetInt(r, F.Spell.EffectImplicitTargetA + e);
                x.TargetB = t.GetInt(r, F.Spell.EffectImplicitTargetB + e);
                x.Mechanic = t.GetInt(r, F.Spell.EffectMechanic + e);
                x.Amplitude = t.GetInt(r, F.Spell.EffectAmplitude + e);
                x.MiscValue = t.GetInt(r, F.Spell.EffectMiscValue + e);
                x.MiscValueB = t.GetInt(r, F.Spell.EffectMiscValueB + e);
                x.TriggerSpell = t.GetInt(r, F.Spell.EffectTriggerSpell + e);
                x.ChainTargets = t.GetInt(r, F.Spell.EffectChainTarget + e);
                x.ItemType = t.GetInt(r, F.Spell.EffectItemType + e);
                int radIdx = t.GetInt(r, F.Spell.EffectRadiusIndex + e);
                if (_radius != null && radIdx > 0)
                {
                    int rr = _radius.FindRow(radIdx);
                    if (rr >= 0) x.Radius = _radius.GetFloat(rr, F.SpellRadius.Radius);
                }
                s.Effects[e] = x;
            }
            return s;
        }

        /// <summary>A shapeshift form / stance by id (SpellShapeshiftForm), or null.</summary>
        public ShapeshiftInfo Form(int id)
        {
            if (_forms == null || id <= 0) return null;
            lock (_lock)
            {
                ShapeshiftInfo f;
                if (_formInfos.TryGetValue(id, out f)) return f;
            }
            ShapeshiftInfo info = null;
            int r = _forms.FindRow(id);
            if (r >= 0)
            {
                info = new ShapeshiftInfo();
                info.Id = id;
                info.Name = _forms.GetLocalizedString(r, F.SpellShapeshiftForm.Name);
                info.Flags1 = _forms.GetInt(r, F.SpellShapeshiftForm.Flags1);
                info.CreatureType = _forms.GetInt(r, F.SpellShapeshiftForm.CreatureType);
                info.AttackSpeedMs = _forms.GetInt(r, F.SpellShapeshiftForm.AttackSpeed);
                info.ModelId = _forms.GetInt(r, F.SpellShapeshiftForm.ModelIdA);
                info.BonusActionBar = _forms.GetInt(r, F.SpellShapeshiftForm.BonusActionBar);   // iteration 11 (vrcast hook)
                for (int i = 0; i < 8; i++) info.StanceSpells[i] = _forms.GetInt(r, F.SpellShapeshiftForm.StanceSpell + i);
            }
            lock (_lock) { _formInfos[id] = info; }
            return info;
        }

        /// <summary>The "$name=expr" definitions of a SpellDescriptionVariables row ("" when none).</summary>
        public string DescriptionVariables(int id)
        {
            if (_descVars == null || id <= 0) return "";
            int r = _descVars.FindRow(id);
            return r < 0 ? "" : _descVars.GetString(r, F.SpellDescriptionVariables.Variables);
        }

        // iteration 11 (spellfx): SpellRuneCost.dbc (death knight rune costs).
        private DbcTable _runeCost;
        private bool _runeCostTried, _runeCostBad;
        private readonly Dictionary<int, int[]> _runeCosts = new Dictionary<int, int[]>();

        /// <summary>A SpellRuneCost row as { blood, unholy, frost, runicPowerGain } (fields 1-4, RuneType order - measured, see
        /// SpellFields.SpellRuneCost; AC indexes runeCost[RuneType], Spell.cpp:5584-5631). null = no row / no table. Cached per id.</summary>
        public int[] RuneCost(int runeCostId)
        {
            if (_files == null || runeCostId <= 0) return null;
            lock (_lock)
            {
                int[] c;
                if (_runeCosts.TryGetValue(runeCostId, out c)) return c;
                if (!_runeCostTried)
                {
                    _runeCostTried = true;
                    try { _runeCost = Dbc.Load(_files, "SpellRuneCost"); } catch (Exception e) { WowLog.Warn("SpellDb: SpellRuneCost.dbc not read: " + e.Message); }
                    if (_runeCost != null && _runeCost.FieldCount != F.SpellRuneCost.FieldCount)
                    {
                        _runeCostBad = true;
                        WowLog.Warn("SpellDb: SpellRuneCost.dbc has " + _runeCost.FieldCount + " fields, expected " + F.SpellRuneCost.FieldCount + " - rune costs are not shown");
                    }
                }
                if (_runeCost == null || _runeCostBad) return null;
                int r = _runeCost.FindRow(runeCostId);
                c = r < 0 ? null : new[] { _runeCost.GetInt(r, F.SpellRuneCost.Blood), _runeCost.GetInt(r, F.SpellRuneCost.Unholy),
                                           _runeCost.GetInt(r, F.SpellRuneCost.Frost), _runeCost.GetInt(r, F.SpellRuneCost.RunicPowerGain) };
                _runeCosts[runeCostId] = c;
                return c;
            }
        }

        // ------------------------------------------------------------------ class kits

        /// <summary>
        /// Spells a character of this class/race learns by level (sorted by level), derived from the files: the class's skill lines
        /// (SkillRaceClassInfo ClassMask == class bit, SkillLine category 7), their SkillLineAbility rows whose ClassMask has the
        /// class bit and whose RaceMask allows the race, minus talents (Talent.dbc ranks and spells named like a talent of the class),
        /// passives, spells with BaseLevel/SpellLevel 0, spells only ever triggered by another spell, and ExcludedKitSpells.
        /// </summary>
        public List<KitSpell> Kit(WowClass cls, int race)
        {
            int key = (int)cls * 100 + race;
            lock (_lock)
            {
                List<KitSpell> k;
                if (_kits.TryGetValue(key, out k)) return k;
            }
            var list = BuildKit(cls, race);
            lock (_lock) { _kits[key] = list; }
            return list;
        }

        /// <summary>
        /// Server-side effect spells that the class-line filter cannot tell from real abilities (measured, see the report):
        /// 20647 "Execute" (the damage half of Execute 5308, cast by the server's script).
        /// </summary>
        public static readonly int[] ExcludedKitSpells =
        {
            20647,   // "Execute": the damage half of Execute 5308, cast by the server's script
            20187,   // "Judgement of Righteousness": the damage the server casts when Judgement unleashes Seal of Righteousness
                     //   (21084's third effect holds 20186 + 1 = 20187, measured)
            43339,   // "Focused": the proc buff of the Shamanistic Focus talent, applied by the server script
        };

        private List<KitSpell> BuildKit(WowClass cls, int race)
        {
            var result = new List<KitSpell>();
            if (_spell == null) return result;
            var sl = Dbc.Load(_files, "SkillLine");
            var sla = Dbc.Load(_files, "SkillLineAbility");
            var srci = Dbc.Load(_files, "SkillRaceClassInfo");
            var tal = Dbc.Load(_files, "Talent");
            var tab = Dbc.Load(_files, "TalentTab");
            if (sl == null || sla == null || srci == null) { WowLog.Warn("SpellDb: skill tables missing - no class kit for " + cls); return result; }
            int classBit = 1 << ((int)cls - 1);
            int raceBit = 1 << (race - 1);

            var classSkills = new HashSet<int>();
            for (int r = 0; r < srci.RecordCount; r++)
            {
                if (srci.GetInt(r, F.SkillRaceClassInfo.ClassMask) != classBit) continue;
                int rm = srci.GetInt(r, F.SkillRaceClassInfo.RaceMask);
                if (rm != 0 && (rm & raceBit) == 0) continue;
                int skill = srci.GetInt(r, F.SkillRaceClassInfo.SkillId);
                int slr = sl.FindRow(skill);
                if (slr >= 0 && sl.GetInt(slr, F.SkillLine.CategoryId) == F.SkillCategoryClass) classSkills.Add(skill);
            }

            // Talents: every rank spell, and the names of this class's talent spells.
            var talentSpells = new HashSet<int>();
            var talentNames = new HashSet<string>();
            var talentRefs = new HashSet<int>();
            if (tal != null)
            {
                for (int r = 0; r < tal.RecordCount; r++)
                {
                    bool ofClass = false;
                    if (tab != null)
                    {
                        int tr = tab.FindRow(tal.GetInt(r, F.Talent.TabId));
                        // TalentTab.dbc ClassMask at field 20 (AzerothCore TalentTabEntry; measured 24 fields).
                        if (tr >= 0 && tab.FieldCount > 20 && (tab.GetInt(tr, 20) & classBit) != 0) ofClass = true;
                    }
                    for (int k = 0; k < 5; k++)
                    {
                        int s = tal.GetInt(r, F.Talent.RankId + k);
                        if (s <= 0) continue;
                        talentSpells.Add(s);
                        if (!ofClass) continue;
                        int sr = _spell.FindRow(s);
                        if (sr < 0) continue;
                        if (k == 0) talentNames.Add(_spell.GetLocalizedString(sr, F.Spell.SpellName));
                        // Spells a talent's tooltip refers to ("$12484s1" in Improved Blizzard) are the talent's effects (Chilled), not trainer
                        // abilities - collected here, dropped below when they cost nothing and have no global cooldown.
                        CollectSpellRefs(_spell.GetLocalizedString(sr, F.Spell.Description), talentRefs);
                        for (int e = 0; e < 3; e++) { int trig = _spell.GetInt(sr, F.Spell.EffectTriggerSpell + e); if (trig > 0) talentRefs.Add(trig); }
                    }
                }
            }

            // Candidates
            var cands = new List<KitSpell>();
            var candIds = new HashSet<int>();
            for (int r = 0; r < sla.RecordCount; r++)
            {
                int skill = sla.GetInt(r, F.SkillLineAbility.SkillLine);
                if (!classSkills.Contains(skill)) continue;
                int cm = sla.GetInt(r, F.SkillLineAbility.ClassMask);
                if ((cm & classBit) == 0) continue;
                int rm = sla.GetInt(r, F.SkillLineAbility.RaceMask);
                if (rm != 0 && (rm & raceBit) == 0) continue;
                int spell = sla.GetInt(r, F.SkillLineAbility.Spell);
                if (candIds.Contains(spell) || talentSpells.Contains(spell) || Array.IndexOf(ExcludedKitSpells, spell) >= 0) continue;
                int sr = _spell.FindRow(spell);
                if (sr < 0) continue;
                int baseLevel = _spell.GetInt(sr, F.Spell.BaseLevel), spellLevel = _spell.GetInt(sr, F.Spell.SpellLevel);
                if (baseLevel < 1 || spellLevel < 1) continue;
                uint attr0 = _spell.GetUInt(sr, F.Spell.Attributes);
                if ((attr0 & 0x40u) != 0) continue;              // passive
                string name = _spell.GetLocalizedString(sr, F.Spell.SpellName);
                if (talentNames.Contains(name)) continue;       // effect spell of a talent (e.g. Vampiric Touch's mana return)
                if (talentRefs.Contains(spell) && _spell.GetInt(sr, F.Spell.StartRecoveryTime) == 0 && _spell.GetInt(sr, F.Spell.ManaCost) == 0
                    && _spell.GetInt(sr, F.Spell.ManaCostPercentage) == 0) continue;   // talent effect (Chilled 12484-12486 of Improved Blizzard)
                candIds.Add(spell);
                cands.Add(new KitSpell { SpellId = spell, Level = spellLevel, Name = name, Rank = _spell.GetLocalizedString(sr, F.Spell.Rank), SkillLine = skill });
            }

            // Spells triggered by another spell's effect (not a "learn spell" effect) are not trained abilities (Tame Beast's
            // taming half, talent procs). One pass over Spell.dbc.
            var triggered = new HashSet<int>();
            for (int r = 0; r < _spell.RecordCount; r++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int trig = _spell.GetInt(r, F.Spell.EffectTriggerSpell + e);
                    if (trig <= 0 || !candIds.Contains(trig)) continue;
                    int eff = _spell.GetInt(r, F.Spell.Effect + e);
                    // Only aura procs/periodic triggers and TRIGGER_SPELL count (measured: "Force Player Stance" 54304 force-casts
                    // Defensive Stance with effect 140, "Path of Defense" 8121 teaches it with effect 36 - both must not hide it).
                    if (eff != SpellEffect.ApplyAura && eff != SpellEffect.TriggerSpell) continue;
                    if (_spell.GetId(r) == trig) continue;
                    triggered.Add(trig);
                }
            }
            foreach (var c in cands) if (!triggered.Contains(c.SpellId)) result.Add(c);
            result.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.SpellId.CompareTo(b.SpellId));
            return result;
        }

        /// <summary>
        /// The kit spells a character of this level knows: for every rank group (same name) the highest rank at or below level.
        /// </summary>
        public List<KitSpell> KnownAt(WowClass cls, int race, int level)
        {
            var kit = Kit(cls, race);
            var best = new Dictionary<string, KitSpell>();
            var order = new List<string>();
            foreach (var k in kit)
            {
                if (k.Level > level) continue;
                KitSpell cur;
                if (!best.TryGetValue(k.Name, out cur)) { best[k.Name] = k; order.Add(k.Name); }
                else if (k.Level > cur.Level || (k.Level == cur.Level && k.SpellId > cur.SpellId)) best[k.Name] = k;
            }
            var list = new List<KitSpell>();
            foreach (var n in order) list.Add(best[n]);
            return list;
        }

        /// <summary>Adds every "$12345" spell id referenced in a tooltip text.</summary>
        private static void CollectSpellRefs(string text, HashSet<int> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != '$' || !char.IsDigit(text[i + 1])) continue;
                int j = i + 1, v = 0;
                while (j < text.Length && char.IsDigit(text[j]) && v < 10000000) { v = v * 10 + (text[j] - '0'); j++; }
                if (v > 0) into.Add(v);
                i = j - 1;
            }
        }

        // ------------------------------------------------------------------ racial abilities (iteration 3)

        private readonly Dictionary<int, List<int>> _racials = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> _racialLines = new Dictionary<int, int>();

        /// <summary>
        /// The race's racial skill line, from the files: the SkillLine (category 9) whose SkillRaceClassInfo row names exactly this race
        /// (measured on 3.3.5a: 754 Human, 125 Orc, 101 Dwarf, 126 Night Elf, 220 Undead, 124 Tauren, 753 Gnome, 733 Troll, 756 Blood Elf,
        /// 760 Draenei - each SRCI RaceMask a single race bit, ClassMask 1535). 0 when not found.
        /// </summary>
        public int RacialSkillLine(int race)
        {
            if (race <= 0 || race > 31) return 0;
            lock (_lock) { int v; if (_racialLines.TryGetValue(race, out v)) return v; }
            int found = 0;
            var sl = _files != null ? Dbc.Load(_files, "SkillLine") : null;
            var srci = _files != null ? Dbc.Load(_files, "SkillRaceClassInfo") : null;
            var sla = _files != null ? Dbc.Load(_files, "SkillLineAbility") : null;
            if (sl != null && srci != null)
            {
                // Several category-9 lines name a single race (the racial line and the race's riding skill: Horse Riding 148 for Humans,
                // Mechanostrider Piloting for Gnomes, Raptor Riding for Trolls - measured): the racial line is the one with the most abilities.
                int raceBit = 1 << (race - 1), bestRows = 0;
                for (int r = 0; r < srci.RecordCount; r++)
                {
                    if (srci.GetInt(r, F.SkillRaceClassInfo.RaceMask) != raceBit) continue;
                    int skill = srci.GetInt(r, F.SkillRaceClassInfo.SkillId);
                    int slr = sl.FindRow(skill);
                    if (slr < 0 || sl.GetInt(slr, F.SkillLine.CategoryId) != 9) continue;
                    int rows = 0;
                    if (sla != null) for (int k = 0; k < sla.RecordCount; k++) if (sla.GetInt(k, F.SkillLineAbility.SkillLine) == skill) rows++;
                    if (rows > bestRows || found == 0) { if (rows > bestRows) bestRows = rows; found = skill; }
                }
            }
            lock (_lock) { _racialLines[race] = found; }
            return found;
        }

        /// <summary>
        /// Racial abilities and passives of a race for a class, from the files: the SkillLineAbility rows of the race's racial skill line whose
        /// RaceMask is 0 or has the race and whose ClassMask is 0 or has the class (Blood Fury, Arcane Torrent and Gift of the Naaru have one
        /// row per class group). Passives included (Quickness, Endurance, resistances, weapon specializations...). Sorted: actives first.
        /// </summary>
        public List<int> RacialSpells(int race, WowClass cls)
        {
            int key = race * 100 + (int)cls;
            lock (_lock) { List<int> v; if (_racials.TryGetValue(key, out v)) return v; }
            var list = new List<int>();
            int line = RacialSkillLine(race);
            var sla = _files != null && line != 0 ? Dbc.Load(_files, "SkillLineAbility") : null;
            if (sla != null && _spell != null)
            {
                int raceBit = 1 << (race - 1), classBit = 1 << ((int)cls - 1);
                var passives = new List<int>();
                for (int r = 0; r < sla.RecordCount; r++)
                {
                    if (sla.GetInt(r, F.SkillLineAbility.SkillLine) != line) continue;
                    int rm = sla.GetInt(r, F.SkillLineAbility.RaceMask), cm = sla.GetInt(r, F.SkillLineAbility.ClassMask);
                    if (rm != 0 && (rm & raceBit) == 0) continue;
                    if (cm != 0 && (cm & classBit) == 0) continue;
                    int spell = sla.GetInt(r, F.SkillLineAbility.Spell);
                    var s = Find(spell);
                    if (s == null || list.Contains(spell) || passives.Contains(spell)) continue;
                    if (s.IsPassive) passives.Add(spell); else list.Add(spell);
                }
                list.AddRange(passives);
            }
            lock (_lock) { _racials[key] = list; }
            return list;
        }
    }
}
