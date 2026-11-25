// iteration 11 - owner talents (new file; lifted from the designer's prototype P/harness/Probes/D11/talents/TalentProto.cs, proven on the
// user's DBCs: proposal-talents.md 4.1 / 12.1)
// The talent trees, glyphs and pet trees of the player's own install (nothing shipped): Talent.dbc, TalentTab.dbc, GlyphProperties.dbc,
// GlyphSlot.dbc, CreatureFamily.dbc, SpellIcon.dbc and Spell.dbc, read at runtime; plus AzerothCore's learn rules (Player::LearnTalent
// Player.cpp:14283-14417, Player::LearnPetTalent :14419-14552) so the UI never sends a request the server silently drops.
// Field numbers: AC DBCStructure.h (TalentEntry :1958-1973, TalentTabEntry, GlyphPropertiesEntry, GlyphSlotEntry, CreatureFamilyEntry),
// measured on the real files by the D11 talents probes (TalentFacts*.cs): Talent 23 fields, TalentTab 24, GlyphProperties 4, GlyphSlot 3,
// CreatureFamily 28.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using F = SomniumSpace.Worlds.SomniumWoW.Play.Spells.SpellFields;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>One Talent.dbc row.</summary>
    public sealed class TalentInfo
    {
        public int Id, TabId, Row, Col;          // fields 0, 1, 2 (tier 0..10; pets 0..5), 3 (column 0..3)
        public int PrereqTalent;                  // field 13 DependsOn, 0 = none (also 0 when it names a talent that does not exist, as AC)
        public int PrereqRank;                    // the COUNT the prerequisite needs = field 16 DependsOnRank + 1; 0 = none
        public int[] RankSpells;                  // fields 4..8 (trailing zeros cut): RankSpells[k] = the spell of rank k + 1
        public bool AddToSpellBook;               // field 19
        public ulong CategoryMask;                // fields 21 | 22 << 32 (pet families: CreatureFamily.dbc field 9 = the bit)
        public int MaxRank { get { return RankSpells.Length; } }
    }

    /// <summary>One TalentTab.dbc row (a talent tree).</summary>
    public sealed class TalentTabInfo
    {
        public int Id, Order, ClassMask, PetMask, IconId;   // fields 0, 22, 20, 21, 18
        public string Name, IconPath, Background;           // 1 (locstring), SpellIcon.dbc of 18, 23 ("MageArcane" -> Interface\TalentFrame\MageArcane-TopLeft)
        public readonly List<TalentInfo> Talents = new List<TalentInfo>();   // sorted Row, Col, Id
    }

    /// <summary>One GlyphProperties.dbc row.</summary>
    public sealed class GlyphInfo { public int Id, SpellId, Type; public string IconPath; }   // fields 0, 1, 2 (0 major / 1 minor), SpellIcon of 3

    public sealed class TalentDb
    {
        public const int MaxTalentRank = 5, MaxPetTalentRank = 3;   // MAX_TALENT_RANK / MAX_PET_TALENT_RANK (AC DBCStructure.h:1954-1955)
        public const int GlyphSockets = 6;                          // MAX_GLYPH_SLOT_INDEX (AC SharedDefines.h:664)
        /// <summary>Level of glyph socket 0..5 (EffectApplyGlyph SpellEffects.cpp:4527-4549; PLAYER_GLYPHS_ENABLED Player.cpp:13631-13642).</summary>
        public static readonly int[] GlyphSocketLevel = { 15, 15, 50, 30, 70, 80 };

        // Talent.dbc / TalentTab.dbc / GlyphProperties.dbc / GlyphSlot.dbc / CreatureFamily.dbc fields (measured, see the header)
        const int TalId = 0, TalTab = 1, TalRow = 2, TalCol = 3, TalRank0 = 4, TalDependsOn = 13, TalDependsOnRank = 16, TalAddToBook = 19, TalCategory = 21;
        const int TabId = 0, TabName = 1, TabIcon = 18, TabClassMask = 20, TabPetMask = 21, TabOrder = 22, TabBackground = 23;
        const int GpId = 0, GpSpell = 1, GpType = 2, GpIcon = 3;
        const int GsId = 0, GsType = 1;
        const int CfPetTalentType = 8, CfCategory = 9;
        const int EffectApplyGlyph = 74, EffectLearnSpell = 36;     // SpellEffects (AC SharedDefines.h): SPELL_EFFECT_APPLY_GLYPH, SPELL_EFFECT_LEARN_SPELL

        static readonly object CacheLock = new object();
        static WowFiles _cachedFiles;
        static TalentDb _cached;

        readonly DbcTable _spell, _icons, _family;
        readonly Dictionary<int, TalentInfo> _talents = new Dictionary<int, TalentInfo>();
        readonly Dictionary<int, TalentTabInfo> _tabs = new Dictionary<int, TalentTabInfo>();
        readonly List<TalentTabInfo> _tabList = new List<TalentTabInfo>();
        readonly Dictionary<int, TalentInfo> _byRankSpell = new Dictionary<int, TalentInfo>();
        readonly Dictionary<int, int> _rankOfSpell = new Dictionary<int, int>();
        readonly HashSet<int> _hiddenTalentSpells = new HashSet<int>();
        readonly Dictionary<int, GlyphInfo> _glyphs = new Dictionary<int, GlyphInfo>();
        readonly Dictionary<int, int> _glyphSlotType = new Dictionary<int, int>();
        readonly Dictionary<int, int> _glyphOfSpell = new Dictionary<int, int>();
        readonly Dictionary<int, List<TalentTabInfo>> _classTabs = new Dictionary<int, List<TalentTabInfo>>();
        readonly Dictionary<long, TalentTabInfo> _petViews = new Dictionary<long, TalentTabInfo>();
        readonly List<string> _missingPrereqs = new List<string>();
        readonly object _lock = new object();
        static bool _loggedOrder, _loggedPetView;

        public bool Available { get; private set; }
        /// <summary>Talent.dbc DependsOn values naming a talent that does not exist (measured: 1756 -> 1409, 1993 -> 1994). Load() sets their
        /// PrereqTalent / PrereqRank to 0 (AC: LookupEntry null -> no check, Player.cpp:14342-14343) and logs them once.</summary>
        public IReadOnlyList<string> MissingPrereqs { get { return _missingPrereqs; } }
        public IReadOnlyList<TalentTabInfo> AllTabs { get { return _tabList; } }
        public int HiddenTalentSpellCount { get { return _hiddenTalentSpells.Count; } }

        /// <summary>The talent database of these files (cached like SpellDb.Get; files null or tables missing = an empty, unavailable database).</summary>
        public static TalentDb Get(WowFiles files)
        {
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
            }
            var t0 = DateTime.UtcNow;
            var db = new TalentDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            WowLog.Info("TalentDb: " + (db.Available
                ? db._tabList.Count + " talent trees, " + db._talents.Count + " talents, " + db._glyphs.Count + " glyphs, " + db._hiddenTalentSpells.Count
                  + " talent rank spells the server keeps out of the spellbook (AC Player.cpp:14393-14403); prerequisites naming a missing talent, treated as none like AC: "
                  + (db._missingPrereqs.Count == 0 ? "none" : string.Join(" ", db._missingPrereqs.ToArray()))
                : "Talent.dbc / TalentTab.dbc / Spell.dbc missing - no talent trees") + ", read in " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms");
            return db;
        }

        TalentDb(WowFiles files)
        {
            if (files == null) return;
            var tal = Dbc.Load(files, "Talent");
            var tab = Dbc.Load(files, "TalentTab");
            _spell = Dbc.Load(files, "Spell");
            _icons = Dbc.Load(files, "SpellIcon");
            _family = Dbc.Load(files, "CreatureFamily");
            var gp = Dbc.Load(files, "GlyphProperties");
            var gs = Dbc.Load(files, "GlyphSlot");
            if (tal == null || tab == null || _spell == null) return;
            for (int r = 0; r < tab.RecordCount; r++)
            {
                var t = new TalentTabInfo
                {
                    Id = tab.GetInt(r, TabId), Name = tab.GetLocalizedString(r, TabName), IconId = tab.GetInt(r, TabIcon), ClassMask = tab.GetInt(r, TabClassMask),
                    PetMask = tab.GetInt(r, TabPetMask), Order = tab.GetInt(r, TabOrder), Background = tab.GetString(r, TabBackground)
                };
                t.IconPath = IconPath(t.IconId);
                _tabs[t.Id] = t;
                _tabList.Add(t);
            }
            var ranks = new List<int>(5);
            for (int r = 0; r < tal.RecordCount; r++)
            {
                ranks.Clear();
                for (int k = 0; k < MaxTalentRank; k++) { int s = tal.GetInt(r, TalRank0 + k); if (s != 0) ranks.Add(s); }
                var t = new TalentInfo
                {
                    Id = tal.GetInt(r, TalId), TabId = tal.GetInt(r, TalTab), Row = tal.GetInt(r, TalRow), Col = tal.GetInt(r, TalCol), RankSpells = ranks.ToArray(),
                    PrereqTalent = tal.GetInt(r, TalDependsOn), AddToSpellBook = tal.GetInt(r, TalAddToBook) != 0,
                    CategoryMask = tal.GetUInt(r, TalCategory) | ((ulong)tal.GetUInt(r, TalCategory + 1) << 32)
                };
                t.PrereqRank = t.PrereqTalent > 0 ? tal.GetInt(r, TalDependsOnRank) + 1 : 0;
                _talents[t.Id] = t;
                TalentTabInfo owner;
                if (_tabs.TryGetValue(t.TabId, out owner)) owner.Talents.Add(t);
                for (int k = 0; k < t.RankSpells.Length; k++)
                {
                    int spell = t.RankSpells[k];
                    _byRankSpell[spell] = t;
                    _rankOfSpell[spell] = k + 1;
                    // AC Player::LearnTalent (Player.cpp:14393-14403): only addToSpellBook && !PASSIVE && no LEARN_SPELL effect go to the book;
                    // the others still arrive as SMSG_LEARNED_SPELL (SendLearnPacket :3134-3149) and in SMSG_INITIAL_SPELLS (:2828-2846).
                    int sr = _spell.FindRow(spell);
                    bool passive = sr >= 0 && (_spell.GetUInt(sr, F.Spell.Attributes) & 0x40u) != 0;   // SPELL_ATTR0_PASSIVE
                    bool learn = false;
                    for (int e = 0; e < 3 && sr >= 0; e++) if (_spell.GetInt(sr, F.Spell.Effect + e) == EffectLearnSpell) learn = true;
                    if (!(t.AddToSpellBook && !passive && !learn)) _hiddenTalentSpells.Add(spell);
                }
            }
            // a DependsOn naming a talent that does not exist = no prerequisite, as AC (Player.cpp:14342-14343 / :14484-14486)
            foreach (var t in _talents.Values)
                if (t.PrereqTalent > 0 && !_talents.ContainsKey(t.PrereqTalent))
                {
                    _missingPrereqs.Add(t.Id + "->" + t.PrereqTalent);
                    t.PrereqTalent = 0; t.PrereqRank = 0;
                }
            foreach (var t in _tabList) t.Talents.Sort(ByCell);
            _tabList.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (gp != null)
                for (int r = 0; r < gp.RecordCount; r++)
                {
                    var g = new GlyphInfo { Id = gp.GetInt(r, GpId), SpellId = gp.GetInt(r, GpSpell), Type = gp.GetInt(r, GpType), IconPath = IconPath(gp.GetInt(r, GpIcon)) };
                    _glyphs[g.Id] = g;
                }
            if (gs != null) for (int r = 0; r < gs.RecordCount; r++) _glyphSlotType[gs.GetInt(r, GsId)] = gs.GetInt(r, GsType);
            Available = true;
        }

        static int ByCell(TalentInfo a, TalentInfo b)
        {
            return a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col != b.Col ? a.Col.CompareTo(b.Col) : a.Id.CompareTo(b.Id);
        }

        string IconPath(int id)
        {
            if (_icons == null || id <= 0) return null;
            int r = _icons.FindRow(id);
            return r < 0 ? null : _icons.GetString(r, F.SpellIcon.TextureFilename);
        }

        // ------------------------------------------------------------------ lookups

        /// <summary>The class's three trees sorted by TalentTab OrderIndex (field 22): WoW's tab order.</summary>
        public IReadOnlyList<TalentTabInfo> ClassTabs(WowClass cls)
        {
            lock (_lock)
            {
                List<TalentTabInfo> l;
                if (_classTabs.TryGetValue((int)cls, out l)) return l;
                l = new List<TalentTabInfo>(3);
                int bit = cls == WowClass.None ? 0 : 1 << ((int)cls - 1);
                foreach (var t in _tabList) if (bit != 0 && (t.ClassMask & bit) != 0) l.Add(t);
                l.Sort((a, b) => a.Order.CompareTo(b.Order));
                _classTabs[(int)cls] = l;
                return l;
            }
        }

        /// <summary>The pet tree of a CreatureFamily petTalentType (0 Ferocity, 1 Tenacity, 2 Cunning): (1 &lt;&lt; type) &amp; PetMask; null for -1.</summary>
        public TalentTabInfo PetTab(int petTalentType)
        {
            if (petTalentType < 0 || petTalentType > 30) return null;
            foreach (var t in _tabList) if ((t.PetMask & (1 << petTalentType)) != 0) return t;
            return null;
        }

        public TalentTabInfo Tab(int tabId) { TalentTabInfo t; return _tabs.TryGetValue(tabId, out t) ? t : null; }
        public TalentInfo Find(int talentId) { TalentInfo t; return _talents.TryGetValue(talentId, out t) ? t : null; }

        /// <summary>Any rank spell -> its talent and the rank COUNT it stands for (1..5); null when the spell is no talent rank.</summary>
        public TalentInfo OfRankSpell(int spellId, out int rankCount)
        {
            TalentInfo t; rankCount = 0;
            if (!_byRankSpell.TryGetValue(spellId, out t)) return null;
            rankCount = _rankOfSpell[spellId];
            return t;
        }

        /// <summary>The spell of rank COUNT rankCount (1..MaxRank), 0 when there is none.</summary>
        public static int RankSpell(TalentInfo t, int rankCount)
        {
            return t != null && rankCount >= 1 && rankCount <= t.RankSpells.Length ? t.RankSpells[rankCount - 1] : 0;
        }

        /// <summary>A talent rank spell the server keeps OUT of the spellbook (not addToSpellBook &amp;&amp; !passive &amp;&amp; no LEARN_SPELL effect).</summary>
        public bool IsHiddenTalentSpell(int spellId) { return _hiddenTalentSpells.Contains(spellId); }

        public GlyphInfo Glyph(int glyphPropertiesId) { GlyphInfo g; return _glyphs.TryGetValue(glyphPropertiesId, out g) ? g : null; }

        /// <summary>The GlyphProperties id a glyph item's use spell inscribes (Spell.dbc effect 74 SPELL_EFFECT_APPLY_GLYPH, MiscValue), 0 = not a glyph spell.</summary>
        public int GlyphOfItemSpell(int spellId)
        {
            if (_spell == null || spellId <= 0) return 0;
            lock (_lock)
            {
                int g;
                if (_glyphOfSpell.TryGetValue(spellId, out g)) return g;
                g = 0;
                int r = _spell.FindRow(spellId);
                for (int e = 0; e < 3 && r >= 0; e++)
                    if (_spell.GetInt(r, F.Spell.Effect + e) == EffectApplyGlyph) { g = _spell.GetInt(r, F.Spell.EffectMiscValue + e); break; }
                _glyphOfSpell[spellId] = g;
                return g;
            }
        }

        /// <summary>GlyphSlot.dbc TypeFlags of a socket id (0 major, 1 minor), -1 unknown.</summary>
        public int GlyphSlotType(int glyphSlotId) { int t; return _glyphSlotType.TryGetValue(glyphSlotId, out t) ? t : -1; }

        /// <summary>CreatureFamily.dbc field 8 petTalentType of a family (0 Ferocity, 1 Tenacity, 2 Cunning, -1 none / unknown).</summary>
        public int PetTalentType(int family)
        {
            if (_family == null || family <= 0) return -1;
            int r = _family.FindRow(family);
            return r < 0 ? -1 : _family.GetInt(r, CfPetTalentType);
        }

        /// <summary>CreatureFamily.dbc field 9 categoryEnumID of a family (the bit in Talent.dbc CategoryMask), -1 unknown.</summary>
        public int PetCategory(int family)
        {
            if (_family == null || family <= 0) return -1;
            int r = _family.FindRow(family);
            return r < 0 ? -1 : _family.GetInt(r, CfCategory);
        }

        /// <summary>Talent points of a level (Player::CalculateTalentsPoints Player.cpp:13953-13975, Rate.Talent 1): 0 below 10, else level - 9.</summary>
        public static int PointsForLevel(int level) { return level < 10 ? 0 : level - 9; }

        /// <summary>Points spent in one tree of a spec.</summary>
        public int PointsInTab(TalentSpecView spec, int tabId)
        {
            int n = 0;
            if (spec == null) return 0;
            foreach (var kv in spec.Ranks) { var t = Find(kv.Key); if (t != null && t.TabId == tabId) n += kv.Value; }
            return n;
        }

        /// <summary>Points spent in a spec in all trees.</summary>
        public static int PointsSpent(TalentSpecView spec)
        {
            int n = 0;
            if (spec == null) return 0;
            foreach (var kv in spec.Ranks) n += kv.Value;
            return n;
        }

        // ------------------------------------------------------------------ rules

        /// <summary>AC LearnTalent / LearnPetTalent rules: null = allowed, else the reason (for the log; the UI shows the tooltip's red lines).
        /// petTalentType = CreatureFamily field 8 of the pet (the server's tree check, Player.cpp:14457-14462); the CategoryMask twin filter is
        /// UI-only (PetView).</summary>
        public string CheckLearn(WowClass cls, TalentSpecView spec, int freePoints, int talentId, int rankCount, bool pet, int petTalentType)
        {
            if (!Available) return "no talent data";
            if (spec == null) return "no talent spec";
            if (freePoints <= 0) return "no free points";
            if (rankCount < 1 || rankCount > (pet ? MaxPetTalentRank : MaxTalentRank)) return "rank " + rankCount + " out of range";
            var t = Find(talentId); if (t == null) return "unknown talent " + talentId;
            var tab = Tab(t.TabId); if (tab == null) return "unknown tree " + t.TabId;
            if (!pet && (cls == WowClass.None || (tab.ClassMask & (1 << ((int)cls - 1))) == 0)) return "not this class's tree";
            if (pet && (petTalentType < 0 || ((1 << petTalentType) & tab.PetMask) == 0)) return "not this pet's tree";
            int cur = spec.Rank(talentId);
            if (cur >= rankCount) return "already at rank " + cur;
            if (freePoints < rankCount - cur) return "needs " + (rankCount - cur) + " points, " + freePoints + " free";
            if (t.PrereqTalent > 0 && spec.Rank(t.PrereqTalent) < t.PrereqRank) return "prerequisite " + t.PrereqTalent + " needs rank " + t.PrereqRank;
            int perTier = pet ? MaxPetTalentRank : MaxTalentRank;
            if (t.Row > 0 && PointsInTab(spec, t.TabId) < t.Row * perTier) return "tier " + (t.Row + 1) + " needs " + (t.Row * perTier) + " points in the tree";
            if (rankCount - 1 >= t.RankSpells.Length) return "no rank spell";
            return null;
        }

        /// <summary>The tier of a talent is open (TalentFrameBase.lua:183-188: (tier - 1) * 5 &lt;= points in the tree; pets * 3).</summary>
        public bool TierOpen(TalentSpecView spec, TalentInfo t, bool pet)
        {
            return t != null && (t.Row == 0 || PointsInTab(spec, t.TabId) >= t.Row * (pet ? MaxPetTalentRank : MaxTalentRank));
        }

        /// <summary>Preview list -> the send order of CMSG_LEARN_PREVIEW_TALENTS(_PET): Row ascending, a same-row prerequisite before its
        /// dependant (ours: the real client's order is C code; the server learns them one by one in the order sent, SkillHandler.cpp:34-56).</summary>
        public List<KeyValuePair<int, int>> SendOrder(IList<KeyValuePair<int, int>> pairs)
        {
            if (!_loggedOrder) { _loggedOrder = true; WowLog.Info("TalentDb: preview talents are sent by tier, a same-row prerequisite first (ours: the client's order is C code) - logged once"); }
            var left = new List<KeyValuePair<int, int>>();
            if (pairs != null) foreach (var p in pairs) if (Find(p.Key) != null) left.Add(p);
            left.Sort((a, b) => ByCell(Find(a.Key), Find(b.Key)));
            var outList = new List<KeyValuePair<int, int>>(left.Count);
            var done = new HashSet<int>();
            while (left.Count > 0)
            {
                int pick = 0;
                for (int i = 0; i < left.Count; i++)
                {
                    var t = Find(left[i].Key);
                    bool waits = false;
                    if (t.PrereqTalent > 0 && !done.Contains(t.PrereqTalent))
                        for (int j = 0; j < left.Count; j++) if (left[j].Key == t.PrereqTalent) { waits = true; break; }
                    if (!waits) { pick = i; break; }
                }
                outList.Add(left[pick]); done.Add(left[pick].Key); left.RemoveAt(pick);
            }
            return outList;
        }

        /// <summary>A pet tree as one family sees it: talents with CategoryMask 0 or with the family's CreatureFamily field-9 bit (the 4 shared
        /// cells hold family twins, e.g. 2109 Dash / 2203 Dive; the client's filter is C code - ours, logged once). petCategory -1 = all.</summary>
        public TalentTabInfo PetView(TalentTabInfo tab, int petCategory)
        {
            if (tab == null || petCategory < 0 || petCategory > 63) return tab;
            if (!_loggedPetView) { _loggedPetView = true; WowLog.Info("TalentDb: a pet tree shows the talents with no family mask or with the pet family's category bit (CreatureFamily field 9; ours: the client's filter is C code) - logged once"); }
            lock (_lock)
            {
                long key = (long)tab.Id << 8 | (long)petCategory;
                TalentTabInfo v;
                if (_petViews.TryGetValue(key, out v)) return v;
                v = new TalentTabInfo { Id = tab.Id, Name = tab.Name, Background = tab.Background, Order = tab.Order, PetMask = tab.PetMask, ClassMask = tab.ClassMask, IconId = tab.IconId, IconPath = tab.IconPath };
                foreach (var t in tab.Talents) if (t.CategoryMask == 0 || (t.CategoryMask & (1UL << petCategory)) != 0) v.Talents.Add(t);
                _petViews[key] = v;
                return v;
            }
        }

        /// <summary>The SpellFamilyName of a class's own spells (ChrClasses.dbc SpellClassSet, measured: Warrior 4, Paladin 10, Hunter 9, Rogue 8,
        /// Priest 6, Death Knight 15, Shaman 11, Mage 3, Warlock 5, Druid 7 = AC SharedDefines.h SpellFamilyNames). 0 = unknown.</summary>
        public static int SpellFamilyOf(WowClass cls)
        {
            switch (cls)
            {
                case WowClass.Mage: return 3;
                case WowClass.Warrior: return 4;
                case WowClass.Warlock: return 5;
                case WowClass.Priest: return 6;
                case WowClass.Druid: return 7;
                case WowClass.Rogue: return 8;
                case WowClass.Hunter: return 9;
                case WowClass.Paladin: return 10;
                case WowClass.Shaman: return 11;
                case WowClass.DeathKnight: return 15;
                default: return 0;
            }
        }
    }
}
