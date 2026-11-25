// iteration 11 - owner trainer (proposal-trainer.md 6.1): the skill / profession tables of the player's own install, read at runtime.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Professions
{
    /// <summary>A SkillLine.dbc row (56 fields: categoryId @1, DisplayName_lang @3, Description_lang @20, spellIcon @37, AlternateVerb_lang @38 - measured, harness TrSkills).</summary>
    public sealed class SkillLineInfo
    {
        public int Id, Category;
        public string Name = "", Description = "", Verb = "";
        public string IconPath;            // SpellIcon.dbc path of spellIcon, null = none
    }

    /// <summary>A SkillRaceClassInfo.dbc row (0 ID, 1 SkillID, 2 RaceMask, 3 ClassMask, 4 Flags, 5 MinLevel, 6 SkillTierID, 7 SkillCostIndex).</summary>
    public sealed class SrciRow
    {
        public int SkillId, RaceMask, ClassMask, Flags, MinLevel, TierId;
        public bool NoSkillUpMessage { get { return (Flags & 0x2) != 0; } }   // SKILL_FLAG_NO_SKILLUP_MESSAGE (AC DBCEnums.h:383-391)
        public bool AlwaysMax { get { return (Flags & 0x10) != 0; } }         // SKILL_FLAG_ALWAYS_MAX_VALUE
        public bool Abandonable { get { return (Flags & 0x20) != 0; } }       // SKILL_FLAG_UNLEARNABLE
        public bool Mono { get { return (Flags & 0x400) != 0; } }             // SKILL_FLAG_MONO_VALUE ("clientside display flag", DBCEnums.h:390)
    }

    /// <summary>A SkillLineAbility.dbc row (14 fields, AC DBCStructure.h:1633-1648; confirmed by Linen Bandage 1838 = 129 3275 0 0 0 0 1 0 1 60 30).</summary>
    public sealed class SlaRow
    {
        public int Id, SkillLine, Spell, RaceMask, ClassMask, MinRank, Superceded, Acquire, TrivialHigh, TrivialLow;
    }

    /// <summary>What a recipe needs and makes (Spell.dbc raw fields SpellDb does not read: Targets @16, RequiresSpellFocus @18, Totem @50-51,
    /// Reagent @52-59, ReagentCount @60-67, TotemCategory @222-223; AC DBCStructure.h:1677-1752).</summary>
    public sealed class RecipeFacts
    {
        public int SpellId;
        public readonly int[] Reagent = new int[8], ReagentCount = new int[8];
        public readonly int[] Totem = new int[2], TotemCategory = new int[2];
        public int Focus, Targets;
        public int CreatesItem, MinMade, MaxMade;   // first CREATE_ITEM 24 / CREATE_ITEM_2 157 / CREATE_RANDOM_ITEM 59 effect; 0 = no item
        public bool ItemTarget;                     // Targets & 0x10 (TARGET_FLAG_ITEM) and attr0 lacks 0x200 (SPELL_ATTR0_HELD_ITEM_ONLY)
    }

    /// <summary>
    /// SkillLine, SkillLineCategory, SkillLineAbility, SkillRaceClassInfo, SkillTiers, SpellFocusObject, TotemCategory, ItemSubClass, Item and the
    /// Spell.dbc fields of recipes, read from the player's install (proposal-trainer.md 3.6, every index measured by the designer's harness probes).
    /// Pure (no Unity); Get(files) is cached per WowFiles like SpellDb.Get. files null = an empty database (every lookup answers "none").
    /// </summary>
    public sealed class ProfessionDb
    {
        // Spell.dbc raw field indices (AC DBCStructure.h:1677-1752, measured 3.6)
        const int SpTargets = 16, SpFocus = 18, SpTotem = 50, SpReagent = 52, SpReagentCount = 60, SpTotemCategory = 222, SpAttr0 = 4;
        // SpellEffects (AC SharedDefines.h)
        public const int EffCreateItem = 24, EffLearnSpell = 36, EffTradeSkill = 47, EffCreateRandomItem = 59, EffSkinning = 95, EffSkill = 118, EffCreateItem2 = 157;
        public const int CategoryProfessions = 11, CategoryHidden = 12;   // SkillLineCategory 11 'Professions', 12 'Not Displayed' (measured)

        private static readonly object CacheLock = new object();
        private static WowFiles _cachedFiles;
        private static ProfessionDb _cached;

        private readonly WowFiles _files;
        private readonly SpellDb _db;
        private readonly DbcTable _spell, _icon, _focus, _totemCat, _item;
        private readonly Dictionary<int, SkillLineInfo> _skills = new Dictionary<int, SkillLineInfo>();
        private readonly Dictionary<int, string> _catName = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _catSort = new Dictionary<int, int>();
        private readonly Dictionary<int, List<SlaRow>> _slaBySpell = new Dictionary<int, List<SlaRow>>();
        private readonly Dictionary<int, List<SlaRow>> _slaBySkill = new Dictionary<int, List<SlaRow>>();
        private readonly Dictionary<int, List<SrciRow>> _srci = new Dictionary<int, List<SrciRow>>();
        private readonly Dictionary<int, int[]> _tiers = new Dictionary<int, int[]>();
        private readonly Dictionary<int, string> _subClassNames = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _tradeSkillOf = new Dictionary<int, int>();
        private readonly Dictionary<int, RecipeFacts> _recipes = new Dictionary<int, RecipeFacts>();
        private readonly object _lock = new object();

        public bool Available { get { return _skills.Count > 0; } }
        public SpellDb Spells { get { return _db; } }
        public int SkillLineCount { get { return _skills.Count; } }

        private ProfessionDb(WowFiles files)
        {
            _files = files;
            _db = SpellDb.Get(files);
            if (files == null) return;
            _spell = Dbc.Load(files, "Spell");
            _icon = Dbc.Load(files, "SpellIcon");
            _focus = Dbc.Load(files, "SpellFocusObject");
            _totemCat = Dbc.Load(files, "TotemCategory");
            _item = Dbc.Load(files, "Item");
            var sl = Dbc.Load(files, "SkillLine");
            if (sl != null && sl.FieldCount >= 56)
                for (int r = 0; r < sl.RecordCount; r++)
                {
                    var s = new SkillLineInfo();
                    s.Id = sl.GetId(r);
                    s.Category = sl.GetInt(r, 1);
                    s.Name = sl.GetLocalizedString(r, 3);
                    s.Description = sl.GetLocalizedString(r, 20);
                    s.Verb = sl.GetLocalizedString(r, 38);
                    s.IconPath = IconOf(sl.GetInt(r, 37));
                    _skills[s.Id] = s;
                }
            var cat = Dbc.Load(files, "SkillLineCategory");
            if (cat != null && cat.FieldCount >= 19)
                for (int r = 0; r < cat.RecordCount; r++) { _catName[cat.GetId(r)] = cat.GetLocalizedString(r, 1); _catSort[cat.GetId(r)] = cat.GetInt(r, 18); }
            var sla = Dbc.Load(files, "SkillLineAbility");
            if (sla != null && sla.FieldCount >= 12)
                for (int r = 0; r < sla.RecordCount; r++)
                {
                    var a = new SlaRow
                    {
                        Id = sla.GetId(r), SkillLine = sla.GetInt(r, 1), Spell = sla.GetInt(r, 2), RaceMask = sla.GetInt(r, 3), ClassMask = sla.GetInt(r, 4),
                        MinRank = sla.GetInt(r, 7), Superceded = sla.GetInt(r, 8), Acquire = sla.GetInt(r, 9), TrivialHigh = sla.GetInt(r, 10), TrivialLow = sla.GetInt(r, 11)
                    };
                    Add(_slaBySpell, a.Spell, a);
                    Add(_slaBySkill, a.SkillLine, a);
                }
            var srci = Dbc.Load(files, "SkillRaceClassInfo");
            if (srci != null && srci.FieldCount >= 8)
                for (int r = 0; r < srci.RecordCount; r++)
                {
                    var row = new SrciRow { SkillId = srci.GetInt(r, 1), RaceMask = srci.GetInt(r, 2), ClassMask = srci.GetInt(r, 3), Flags = srci.GetInt(r, 4), MinLevel = srci.GetInt(r, 5), TierId = srci.GetInt(r, 6) };
                    List<SrciRow> l;
                    if (!_srci.TryGetValue(row.SkillId, out l)) { l = new List<SrciRow>(); _srci[row.SkillId] = l; }
                    l.Add(row);
                }
            var tiers = Dbc.Load(files, "SkillTiers");
            if (tiers != null && tiers.FieldCount >= 33)
                for (int r = 0; r < tiers.RecordCount; r++)
                {
                    var v = new int[16];
                    for (int i = 0; i < 16; i++) v[i] = tiers.GetInt(r, 17 + i);
                    _tiers[tiers.GetId(r)] = v;
                }
            var sc = Dbc.Load(files, "ItemSubClass");
            if (sc != null && sc.FieldCount >= 27)
                for (int r = 0; r < sc.RecordCount; r++)
                {
                    string n = sc.GetLocalizedString(r, 10);
                    if (!string.IsNullOrEmpty(n)) _subClassNames[sc.GetInt(r, 0) << 8 | (sc.GetInt(r, 1) & 0xFF)] = n;
                }
            if (_spell != null && _spell.FieldCount < 224) { WowLog.Warn("ProfessionDb: Spell.dbc has " + _spell.FieldCount + " fields (expected 234) - recipe facts off"); _spell = null; }
            if (_totemCat != null && _totemCat.FieldCount < 20) _totemCat = null;
            if (_focus != null && _focus.FieldCount < 18) _focus = null;
        }

        private static void Add(Dictionary<int, List<SlaRow>> d, int key, SlaRow a)
        {
            List<SlaRow> l;
            if (!d.TryGetValue(key, out l)) { l = new List<SlaRow>(2); d[key] = l; }
            l.Add(a);
        }

        private string IconOf(int iconId)
        {
            if (_icon == null || iconId <= 0) return null;
            int r = _icon.FindRow(iconId);
            if (r < 0) return null;
            string p = _icon.GetString(r, 1);
            return string.IsNullOrEmpty(p) ? null : p;
        }

        /// <summary>The database of these files (cached; files null = empty).</summary>
        public static ProfessionDb Get(WowFiles files)
        {
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
            }
            var t0 = DateTime.UtcNow;
            var db = new ProfessionDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            int slaRows = 0;
            foreach (var l in db._slaBySpell.Values) slaRows += l.Count;
            WowLog.Info("ProfessionDb: " + db._skills.Count + " skill lines, " + db._catName.Count + " categories, " + slaRows + " SkillLineAbility rows, " +
                        db._srci.Count + " SkillRaceClassInfo skills, " + db._tiers.Count + " SkillTiers, " + db._subClassNames.Count + " ItemSubClass names, SpellFocusObject " +
                        (db._focus != null ? db._focus.RecordCount + " rows" : "missing") + ", TotemCategory " + (db._totemCat != null ? db._totemCat.RecordCount + " rows" : "missing") +
                        "; recipes = Spell.dbc attr0 0x20 SPELL_ATTR0_IS_TRADESKILL (SharedDefines.h:375) - read in " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms");
            return db;
        }

        // ------------------------------------------------------------------ skill lines

        public SkillLineInfo Skill(int id)
        {
            SkillLineInfo s;
            return _skills.TryGetValue(id, out s) ? s : null;
        }

        public string SkillName(int id)
        {
            var s = Skill(id);
            return s != null && s.Name.Length > 0 ? s.Name : "Skill " + id;
        }

        public string CategoryName(int cat)
        {
            string n;
            return _catName.TryGetValue(cat, out n) ? n : "";
        }

        /// <summary>SkillLineCategory SortIndex (Attributes 1, Class Skills 2, Professions 3, Secondary 4, Weapon 5, Armor 6, Languages 7, Not Displayed 8); unknown 99.</summary>
        public int CategorySort(int cat)
        {
            int n;
            return _catSort.TryGetValue(cat, out n) ? n : 99;
        }

        public bool IsPrimaryProfession(int skill)
        {
            var s = Skill(skill);
            return s != null && s.Category == CategoryProfessions;   // SpellMgr.cpp:38-49 IsPrimaryProfessionSkill: SKILL_CATEGORY_PROFESSION
        }

        public static int RaceBit(int race) { return race > 0 && race < 32 ? 1 << (race - 1) : 0; }
        public static int ClassBit(WowClass cls) { int c = (int)cls; return c > 0 && c < 32 ? 1 << (c - 1) : 0; }

        private static bool Fits(int raceMask, int classMask, int race, WowClass cls)
        {
            if (raceMask != 0 && (raceMask & RaceBit(race)) == 0) return false;
            if (classMask != 0 && (classMask & ClassBit(cls)) == 0) return false;
            return true;
        }

        /// <summary>The SkillRaceClassInfo row of a skill for this race / class (RaceMask / ClassMask 0 = all; first match, as AC's GetSkillRaceClassInfo), or null.</summary>
        public SrciRow Srci(int skill, int race, WowClass cls)
        {
            List<SrciRow> l;
            if (!_srci.TryGetValue(skill, out l)) return null;
            for (int i = 0; i < l.Count; i++) if (Fits(l[i].RaceMask, l[i].ClassMask, race, cls)) return l[i];
            return null;
        }

        /// <summary>Every skill id that has a SkillRaceClassInfo row for this race / class.</summary>
        public List<int> SkillsOf(int race, WowClass cls)
        {
            var list = new List<int>();
            foreach (var kv in _srci)
                for (int i = 0; i < kv.Value.Count; i++)
                    if (Fits(kv.Value[i].RaceMask, kv.Value[i].ClassMask, race, cls)) { list.Add(kv.Key); break; }
            return list;
        }

        /// <summary>SkillTiers Value[16] (@17-32) of a tier id (75 150 225 300 375 450 for the profession tiers), or null.</summary>
        public int[] TierValues(int tierId)
        {
            int[] v;
            return _tiers.TryGetValue(tierId, out v) ? v : null;
        }

        // ------------------------------------------------------------------ SkillLineAbility

        /// <summary>The SkillLineAbility row of a spell ON that skill line fitting this race / class, or null.</summary>
        public SlaRow Ability(int spellId, int skillLine, int race, WowClass cls)
        {
            List<SlaRow> l;
            if (!_slaBySpell.TryGetValue(spellId, out l)) return null;
            for (int i = 0; i < l.Count; i++) if (l[i].SkillLine == skillLine && Fits(l[i].RaceMask, l[i].ClassMask, race, cls)) return l[i];
            for (int i = 0; i < l.Count; i++) if (l[i].SkillLine == skillLine) return l[i];   // a row on the line for another race / class (fallback, ours)
            return null;
        }

        /// <summary>The skill lines a spell has SkillLineAbility rows on (any race / class).</summary>
        public IEnumerable<int> RecipeLinesOf(int spellId)
        {
            List<SlaRow> l;
            if (!_slaBySpell.TryGetValue(spellId, out l)) yield break;
            for (int i = 0; i < l.Count; i++) yield return l[i].SkillLine;
        }

        /// <summary>True when the spell has a SkillLineAbility row on the line fitting this race / class (RaceMask / ClassMask 0 or containing the character).</summary>
        public bool OnLine(int spellId, int skillLine, int race, WowClass cls)
        {
            List<SlaRow> l;
            if (!_slaBySpell.TryGetValue(spellId, out l)) return false;
            for (int i = 0; i < l.Count; i++) if (l[i].SkillLine == skillLine && Fits(l[i].RaceMask, l[i].ClassMask, race, cls)) return true;
            return false;
        }

        // ------------------------------------------------------------------ recipes and profession spells

        /// <summary>A recipe = Spell.dbc attr0 0x20 SPELL_ATTR0_IS_TRADESKILL ("Displayed in recipe list", AC SharedDefines.h:375): the client's own
        /// flag, measured identical to the craft-effect census on all 14 profession lines (harness TrRecipeAttr).</summary>
        public static bool IsRecipe(SpellInfo s) { return s != null && (s.Attributes[0] & 0x20u) != 0; }

        public bool IsRecipe(int spellId) { return IsRecipe(_db != null ? _db.Find(spellId) : null); }

        /// <summary>
        /// The skill line whose profession window a SPELL_EFFECT_TRADE_SKILL (47) spell opens: its SkillLineAbility row's line (Smelting 2656 -> 186,
        /// Weaponsmith 9787 -> 164, Runeforging 53428 -> 776), else its SPELL_EFFECT_SKILL (118) MiscValue; 0 = not a profession window (2842
        /// 'Poisons', 5149 'Beast Training' and every spell without effect 47 - harness TrTradeSpells).
        /// </summary>
        public int TradeSkillOf(int spellId)
        {
            if (spellId <= 0 || _db == null) return 0;
            lock (_lock)
            {
                int c;
                if (_tradeSkillOf.TryGetValue(spellId, out c)) return c;
            }
            int line = 0;
            var s = _db.Find(spellId);
            if (s != null && s.HasEffect(EffTradeSkill))
            {
                List<SlaRow> l;
                if (_slaBySpell.TryGetValue(spellId, out l) && l.Count > 0) line = l[0].SkillLine;
                else
                {
                    var sk = s.EffectOf(EffSkill);
                    if (sk != null && sk.MiscValue > 0) line = sk.MiscValue;
                }
            }
            lock (_lock) { _tradeSkillOf[spellId] = line; }
            return line;
        }

        /// <summary>The lines that have a profession window for the known spells (each known TRADE_SKILL spell's line).</summary>
        public HashSet<int> TradeLinesOf(IEnumerable<int> known)
        {
            var set = new HashSet<int>();
            if (known == null) return set;
            foreach (int id in known)
            {
                int line = TradeSkillOf(id);
                if (line != 0) set.Add(line);
            }
            return set;
        }

        /// <summary>The profession spell (TRADE_SKILL 47) of the known set that opens this line's window (e.g. 3908 Tailoring), 0 = none.</summary>
        public int TradeSpellFor(int skillLine, IEnumerable<int> known)
        {
            if (known == null) return 0;
            int best = 0;
            foreach (int id in known) if (TradeSkillOf(id) == skillLine && id > best) best = id;
            return best;
        }

        /// <summary>The reagents, tools, focus and made item of a spell (Spell.dbc raw fields + its effects), or null when the spell is unknown.</summary>
        public RecipeFacts Recipe(int spellId)
        {
            if (spellId <= 0 || _db == null) return null;
            lock (_lock)
            {
                RecipeFacts c;
                if (_recipes.TryGetValue(spellId, out c)) return c;
            }
            var s = _db.Find(spellId);
            RecipeFacts f = null;
            if (s != null)
            {
                f = new RecipeFacts { SpellId = spellId };
                int r = _spell != null ? _spell.FindRow(spellId) : -1;
                uint attr0 = s.Attributes[0];
                if (r >= 0)
                {
                    f.Targets = _spell.GetInt(r, SpTargets);
                    f.Focus = _spell.GetInt(r, SpFocus);
                    for (int i = 0; i < 2; i++) { f.Totem[i] = _spell.GetInt(r, SpTotem + i); f.TotemCategory[i] = _spell.GetInt(r, SpTotemCategory + i); }
                    for (int i = 0; i < 8; i++) { f.Reagent[i] = _spell.GetInt(r, SpReagent + i); f.ReagentCount[i] = _spell.GetInt(r, SpReagentCount + i); }
                    attr0 = _spell.GetUInt(r, SpAttr0);
                }
                f.ItemTarget = (f.Targets & 0x10) != 0 && (attr0 & 0x200u) == 0;
                for (int e = 0; e < 3; e++)
                {
                    var x = s.Effects[e];
                    if (x == null) continue;
                    if (x.Effect != EffCreateItem && x.Effect != EffCreateItem2 && x.Effect != EffCreateRandomItem) continue;
                    if (x.ItemType <= 0) continue;
                    f.CreatesItem = x.ItemType;
                    f.MinMade = Math.Max(1, x.MinValue);        // SpellEffectInfo.MinValue / MaxValue (SpellInfo.cs:19-21): Anti-Venom base 2 die 1 -> 3
                    f.MaxMade = Math.Max(f.MinMade, x.MaxValue);
                    break;
                }
            }
            lock (_lock) { _recipes[spellId] = f; }
            return f;
        }

        /// <summary>
        /// WoW's skill-up colours from the recipe's SkillLineAbility row (CraftSkillGainChance, AC PlayerUpdates.cpp:767-787): value >= TrivialHigh grey,
        /// >= (TrivialLow + TrivialHigh) / 2 green ("the midpoint of that range", :781-783), >= TrivialLow yellow, else orange. A row with
        /// TrivialHigh 0 (no skill-up known) or no row: grey (ours).
        /// </summary>
        public static RecipeDifficulty Difficulty(int value, SlaRow r)
        {
            if (r == null || r.TrivialHigh <= 0) return RecipeDifficulty.Trivial;
            if (value >= r.TrivialHigh) return RecipeDifficulty.Trivial;
            if (value >= (r.TrivialLow + r.TrivialHigh) / 2) return RecipeDifficulty.Easy;
            if (value >= r.TrivialLow) return RecipeDifficulty.Medium;
            return RecipeDifficulty.Optimal;
        }

        public string FocusName(int focusId)
        {
            if (_focus == null || focusId <= 0) return null;
            int r = _focus.FindRow(focusId);
            return r < 0 ? null : _focus.GetLocalizedString(r, 1);
        }

        public string TotemCategoryName(int cat)
        {
            if (_totemCat == null || cat <= 0) return null;
            int r = _totemCat.FindRow(cat);
            return r < 0 ? null : _totemCat.GetLocalizedString(r, 1);
        }

        /// <summary>AC IsTotemCategoryCompatiableWith (PlayerStorage.cpp:890-915): the same categoryType (@18) and (itemMask & reqMask) == reqMask (@19).</summary>
        public bool TotemCompatible(int itemCat, int reqCat)
        {
            if (reqCat == 0) return true;
            if (itemCat <= 0 || _totemCat == null) return false;
            int ri = _totemCat.FindRow(itemCat), rr = _totemCat.FindRow(reqCat);
            if (ri < 0 || rr < 0) return false;
            if (_totemCat.GetInt(ri, 18) != _totemCat.GetInt(rr, 18)) return false;
            int req = _totemCat.GetInt(rr, 19);
            return (_totemCat.GetInt(ri, 19) & req) == req;
        }

        /// <summary>
        /// The gathering spell of a skinnable corpse: the highest known spell with SPELL_EFFECT_SKINNING (95) whose EffectMiscValue is the skill's
        /// (0 Skinning 393, 1 Herb Gathering 182, 2 Mining 186, 3 Engineering 202 - measured 3.5); 0 = none known.
        /// </summary>
        public int SkinSpellFor(int skill, ICollection<int> known)
        {
            int misc = skill == 393 ? 0 : skill == 182 ? 1 : skill == 186 ? 2 : skill == 202 ? 3 : -1;
            if (misc < 0 || known == null || _db == null) return 0;
            int best = 0;
            foreach (int id in known)
            {
                if (id <= best) continue;
                var s = _db.Find(id);
                if (s == null) continue;
                for (int e = 0; e < 3; e++)
                {
                    var x = s.Effects[e];
                    if (x != null && x.Effect == EffSkinning && x.MiscValue == misc) { best = id; break; }
                }
            }
            return best;
        }

        /// <summary>The spell a trainer service teaches: the first SPELL_EFFECT_LEARN_SPELL (36) TriggerSpell (3911 'Apprentice Tailor' -> 3908), else itself.</summary>
        public int ShowSpellOf(int trainerSpellId)
        {
            var s = _db != null ? _db.Find(trainerSpellId) : null;
            if (s == null) return trainerSpellId;
            for (int e = 0; e < 3; e++)
            {
                var x = s.Effects[e];
                if (x != null && x.Effect == EffLearnSpell && x.TriggerSpell > 0) return x.TriggerSpell;
            }
            return trainerSpellId;
        }

        /// <summary>The skill line a trainer service belongs to (the shown spell's SPELL_EFFECT_SKILL MiscValue, else its first SkillLineAbility line), 0 = none.</summary>
        public int ServiceSkillLine(int showSpellId)
        {
            var s = _db != null ? _db.Find(showSpellId) : null;
            if (s != null)
            {
                var sk = s.EffectOf(EffSkill);
                if (sk != null && sk.MiscValue > 0) return sk.MiscValue;
            }
            List<SlaRow> l;
            return _slaBySpell.TryGetValue(showSpellId, out l) && l.Count > 0 ? l[0].SkillLine : 0;
        }

        /// <summary>Our recipe group: the made item's ItemSubClass DisplayName (Item.dbc ClassID @1 / SubclassID @2 -> ItemSubClass @10); "" when unknown.</summary>
        public string GroupOfItem(int itemEntry)
        {
            if (itemEntry <= 0 || _item == null) return "";
            int r = _item.FindRow(itemEntry);
            if (r < 0) return "";
            string n;
            return _subClassNames.TryGetValue(_item.GetInt(r, 1) << 8 | (_item.GetInt(r, 2) & 0xFF), out n) ? n : "";
        }
    }
}
