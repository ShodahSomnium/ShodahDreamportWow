// spellui (2026-10-04, the user: "I can't see my special abilities like Auras for paladin, forms for druids etc" and "split up the
// spells into their correct slots, they are all in one at the moment"). Pure C# (no UnityEngine): the two rules below are harness-tested
// by P/harness/Probes/SpellUi (probes SpellUiStance / SpellUiTabs). Every rule was read out of the user's own Wow.exe (3.3.5a build 12340,
// image base 0x400000) and measured on the user's DBCs; what is ours is named "ours".
//
// 1. UiStanceRules - WoW's stance bar list (GetNumShapeshiftForms / GetShapeshiftFormInfo, filled at Wow.exe 0x54246A-0x542496):
//    a spell is a stance bar spell unless its AttributesEx2 has 0x2 (SPELL_ATTR2_NO_SHAPESHIFT_UI, AzerothCore SharedDefines.h:445), when
//    one of its three EffectApplyAuraName is 36 (SPELL_AURA_MOD_SHAPESHIFT) or AttributesEx2 has 0x10 (SPELL_ATTR2_USE_SHAPESHIFT_BAR "Show in
//    stance bar (client only)", SharedDefines.h:448). Measured on Spell.dbc: 0x10 = every paladin aura rank (Devotion, Retribution,
//    Concentration, Shadow / Frost / Fire Resistance, Crusader; attr2 0x11) and the death knight presences 48263 / 48265 / 48266; the forms
//    (warrior stances, druid forms, Stealth, Shadow Dance, Shadowform) have aura 36; Ghost Wolf has 0x2 (not on the bar); no hunter aspect
//    has either (attr2 0x0, no aura 36), so hunters get no stance bar.
//    A new rank takes its old rank's button (Wow.exe 0x5424C4-0x5424E8); the rank chain = SkillLineAbility.SupercededBySpell (field 8;
//    465 -> 10290 -> 643 ... Devotion Aura, 5487 Bear Form -> 9634 Dire Bear Form). The list is sorted by Spell.dbc field 215
//    (StanceBarOrder) ascending, negative last, both negative by id (comparator Wow.exe 0x53C680). Ours: passive spells stay out, ties keep
//    the spellbook order, at most 10 (NUM_SHAPESHIFT_SLOTS, BonusActionBarFrame.lua:5).
// 2. UiSpellTabs - WoW's spellbook skill-line tabs (GetSpellTabInfo Wow.exe 0x53BE70; the book is built at 0x542B35-0x542CAD):
//    a spell's tab = its SkillLineAbility row for the player's race and class -> the SkillRaceClassInfo row of that skill line for the
//    race and class (first row whose RaceMask / ClassMask is 0 or has the bit, Wow.exe 0x810ED0); Flags 0x80 set (or no row) -> tab 0,
//    "General" (GENERAL_SPELLS, GlobalStrings.lua:3792; icon "Interface\Icons\Ability_Kick", Wow.exe 0x53BF02); else the skill line's own
//    tab: SkillLine DisplayName (field 3) and the SpellIcon of field 37. Measured flags: the class trees 0x410 (tabs); racials 0x492,
//    professions / secondary skills 0x80 / 0xa0, riding 0x90, weapons / armour / languages 0x80, Defense 0x280, Lockpicking and
//    Runeforging 0x80, Attack (GENERIC) 0x492 - all General. A tab exists while it holds a spell (0x53FF90 removes an empty one).
//    Order (qsort comparators): General first, then the skill line names compared as strings (0x53BA40); inside a tab the spell names,
//    then the rank (0x53C8B0). Ours: names compared ordinal ignore-case; same name keeps the session's order (its rank / level order);
//    with several SkillLineAbility rows for the race / class the first in file order counts (0x812410 not decoded).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using F = SomniumSpace.Worlds.SomniumWoW.Play.Spells.SpellFields;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure
{
    /// <summary>One stance bar button: the highest known rank of a rank chain.</summary>
    public sealed class StanceEntry
    {
        public int SpellId;
        public int Form;                  // the SpellShapeshiftForm id of its aura 36 (0 = an aura / presence: no form)
        public int Order;                 // Spell.dbc field 215 StanceBarOrder
        public int BookIndex;             // its place in the spellbook (ours: the tie-break)
        public string Name;
        public readonly List<int> Chain = new List<int>();   // every rank of the chain (known or not): "checked" = an aura of any of them
    }

    /// <summary>One spellbook tab (GetSpellTabInfo): skill line 0 = General.</summary>
    public sealed class SpellTab
    {
        public int SkillLine;
        public string Name;
        public string IconPath;
        public readonly List<AbilityView> Spells = new List<AbilityView>();
    }

    /// <summary>The SkillLineAbility / SkillRaceClassInfo / SkillLine / SpellIcon facts both rules read (once per install).</summary>
    public sealed class UiSpellFacts
    {
        public const int MaxStances = 10;              // NUM_SHAPESHIFT_SLOTS (BonusActionBarFrame.lua:5)
        public const int MaxTabs = 8;                  // MAX_SKILLLINE_TABS (SpellBookFrame.lua:2)
        public const uint AttrNoShapeshiftUi = 0x2;    // SPELL_ATTR2_NO_SHAPESHIFT_UI (SharedDefines.h:445)
        public const uint AttrUseShapeshiftBar = 0x10; // SPELL_ATTR2_USE_SHAPESHIFT_BAR (SharedDefines.h:448)
        public const int FieldStanceBarOrder = 215;    // Spell.dbc (234 fields measured); Wow.exe 0x53C6DD reads it (in-memory field 151)
        public const int SrciGeneralFlag = 0x80;       // SkillRaceClassInfo Flags bit tested at Wow.exe 0x542B70 / 0x53C647
        public const string GeneralIcon = @"Interface\Icons\Ability_Kick";   // Wow.exe 0x53BF02 (string at 0xA0AEFC)
        public const string GeneralFallback = "General";                      // GENERAL_SPELLS (GlobalStrings.lua:3792)

        private struct Sla { public int Skill, RaceMask, ClassMask; }
        private struct Srci { public int RaceMask, ClassMask, Flags; }

        private readonly Dictionary<int, List<Sla>> _sla = new Dictionary<int, List<Sla>>();
        private readonly Dictionary<int, List<Srci>> _srci = new Dictionary<int, List<Srci>>();
        private readonly Dictionary<int, int> _next = new Dictionary<int, int>(), _prev = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _lineName = new Dictionary<int, string>(), _lineIcon = new Dictionary<int, string>();
        private readonly DbcTable _spell;
        private static UiSpellFacts _inst;
        private static WowFiles _instFiles;
        private static readonly object Lock = new object();

        public bool Ok { get; private set; }

        public static UiSpellFacts Get(WowFiles files)
        {
            if (files == null) return null;
            lock (Lock)
            {
                if (_inst != null && ReferenceEquals(_instFiles, files)) return _inst;
                _inst = new UiSpellFacts(files);
                _instFiles = files;
                WowLog.Info("UI spell rules: " + (_inst.Ok ? _inst._sla.Count + " spells with skill lines, " + _inst._srci.Count + " skill lines with race/class rows, "
                    + _inst._next.Count + " rank links (SkillLineAbility.SupercededBySpell)" : "SkillLineAbility / SkillRaceClassInfo / SkillLine missing - one General tab"));
                return _inst;
            }
        }

        private UiSpellFacts(WowFiles files)
        {
            DbcTable sla = Dbc.Load(files, "SkillLineAbility"), srci = Dbc.Load(files, "SkillRaceClassInfo"), sl = Dbc.Load(files, "SkillLine"), icons = Dbc.Load(files, "SpellIcon");
            _spell = Dbc.Load(files, "Spell");
            if (sla == null || srci == null || sl == null) return;
            for (int r = 0; r < sla.RecordCount; r++)
            {
                int spell = sla.GetInt(r, F.SkillLineAbility.Spell);
                List<Sla> l;
                if (!_sla.TryGetValue(spell, out l)) _sla[spell] = l = new List<Sla>(1);
                l.Add(new Sla { Skill = sla.GetInt(r, F.SkillLineAbility.SkillLine), RaceMask = sla.GetInt(r, F.SkillLineAbility.RaceMask), ClassMask = sla.GetInt(r, F.SkillLineAbility.ClassMask) });
                int next = sla.GetInt(r, F.SkillLineAbility.SupercededBySpell);
                if (next > 0 && next != spell && !_next.ContainsKey(spell)) { _next[spell] = next; if (!_prev.ContainsKey(next)) _prev[next] = spell; }
            }
            for (int r = 0; r < srci.RecordCount; r++)
            {
                int skill = srci.GetInt(r, F.SkillRaceClassInfo.SkillId);
                List<Srci> l;
                if (!_srci.TryGetValue(skill, out l)) _srci[skill] = l = new List<Srci>(2);
                l.Add(new Srci { RaceMask = srci.GetInt(r, F.SkillRaceClassInfo.RaceMask), ClassMask = srci.GetInt(r, F.SkillRaceClassInfo.ClassMask), Flags = srci.GetInt(r, F.SkillRaceClassInfo.Flags) });
            }
            for (int r = 0; r < sl.RecordCount; r++)
            {
                int id = sl.GetInt(r, F.SkillLine.Id);
                _lineName[id] = sl.GetLocalizedString(r, F.SkillLine.DisplayName);
                int icon = sl.GetInt(r, F.SkillLine.SpellIcon);
                int ir = icons != null && icon > 0 ? icons.FindRow(icon) : -1;
                _lineIcon[id] = ir >= 0 ? icons.GetString(ir, F.SpellIcon.TextureFilename) : null;
            }
            Ok = true;
        }

        private static bool Has(int mask, int bit) { return mask == 0 || (bit != 0 && (mask & bit) != 0); }
        private static int RaceBit(int race) { return race >= 1 && race <= 31 ? 1 << (race - 1) : 0; }
        private static int ClassBit(WowClass c) { return c == WowClass.None ? 0 : 1 << ((int)c - 1); }

        /// <summary>The spell's tab skill line for this race / class (0 = General) - the rule in the file header (2).</summary>
        public int TabLineOf(int spellId, int race, WowClass cls)
        {
            List<Sla> rows;
            if (!Ok || !_sla.TryGetValue(spellId, out rows)) return 0;
            int rb = RaceBit(race), cb = ClassBit(cls);
            for (int i = 0; i < rows.Count; i++)
            {
                if (!Has(rows[i].RaceMask, rb) || !Has(rows[i].ClassMask, cb)) continue;
                List<Srci> ci;
                if (!_srci.TryGetValue(rows[i].Skill, out ci)) return 0;
                for (int k = 0; k < ci.Count; k++)
                    if (Has(ci[k].RaceMask, rb) && Has(ci[k].ClassMask, cb)) return (ci[k].Flags & SrciGeneralFlag) != 0 ? 0 : rows[i].Skill;
                return 0;
            }
            return 0;
        }

        public string LineName(int skill) { string n; return _lineName.TryGetValue(skill, out n) ? n : null; }
        public string LineIcon(int skill) { string n; return _lineIcon.TryGetValue(skill, out n) ? n : null; }

        /// <summary>The spellbook's tabs (General first, then the skill lines by name; inside each tab the spells by name, the session's order
        /// for equal names), at most <see cref="MaxTabs"/> (ours: a ninth line's spells go to General).</summary>
        public List<SpellTab> Tabs(IList<AbilityView> book, int race, WowClass cls, string generalName)
        {
            var byLine = new Dictionary<int, SpellTab>();
            var order = new Dictionary<AbilityView, int>();
            var tabs = new List<SpellTab>();
            var general = new SpellTab { SkillLine = 0, Name = string.IsNullOrEmpty(generalName) ? GeneralFallback : generalName, IconPath = GeneralIcon };
            if (book != null)
                for (int i = 0; i < book.Count; i++)
                {
                    AbilityView v = book[i];
                    if (v == null) continue;
                    order[v] = i;
                    int line = TabLineOf(v.SpellId, race, cls);
                    SpellTab t;
                    if (line == 0) t = general;
                    else if (!byLine.TryGetValue(line, out t))
                    {
                        t = new SpellTab { SkillLine = line, Name = LineName(line) ?? ("Skill " + line), IconPath = LineIcon(line) };
                        byLine[line] = t;
                        tabs.Add(t);
                    }
                    t.Spells.Add(v);
                }
            tabs.Sort((a, b) => { int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : a.SkillLine.CompareTo(b.SkillLine); });
            while (tabs.Count > MaxTabs - 1) { general.Spells.AddRange(tabs[tabs.Count - 1].Spells); tabs.RemoveAt(tabs.Count - 1); }
            tabs.Insert(0, general);
            foreach (var t in tabs)
                t.Spells.Sort((a, b) =>
                {
                    int c = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : order[a].CompareTo(order[b]);
                });
            if (general.Spells.Count == 0 && tabs.Count > 1) tabs.RemoveAt(0);   // WoW: a tab exists while it holds a spell (0x53FF90)
            return tabs;
        }

        // ------------------------------------------------------------------ the stance bar

        /// <summary>Is this a stance bar spell (the rule in the file header (1); ours: never a passive one); form = its aura 36 misc value.</summary>
        public static bool OnStanceBar(SpellInfo s, out int form)
        {
            form = 0;
            if (s == null || s.IsPassive || (s.Attributes[2] & AttrNoShapeshiftUi) != 0) return false;
            bool on = (s.Attributes[2] & AttrUseShapeshiftBar) != 0;
            for (int i = 0; i < s.Effects.Length; i++)
            {
                SpellEffectInfo e = s.Effects[i];
                if (e == null || e.Aura != SpellAura.ModShapeshift) continue;
                on = true;
                if (form == 0 && e.MiscValue > 0) form = e.MiscValue;
            }
            return on;
        }

        /// <summary>Spell.dbc field 215 StanceBarOrder of a spell (0 when unknown).</summary>
        public int StanceOrder(int spellId)
        {
            if (_spell == null || _spell.FieldCount <= FieldStanceBarOrder) return 0;
            int r = _spell.FindRow(spellId);
            return r >= 0 ? _spell.GetInt(r, FieldStanceBarOrder) : 0;
        }

        /// <summary>The rank chain of a spell (SkillLineAbility.SupercededBySpell both ways), the spell itself when it has none.</summary>
        public void ChainOf(int spellId, List<int> into)
        {
            into.Clear();
            int root = spellId, guard = 0, p;
            while (_prev.TryGetValue(root, out p) && guard++ < 64) root = p;
            int cur = root;
            guard = 0;
            while (cur > 0 && guard++ < 64)
            {
                into.Add(cur);
                int n;
                if (!_next.TryGetValue(cur, out n)) break;
                cur = n;
            }
            if (!into.Contains(spellId)) into.Add(spellId);
        }

        /// <summary>The stance bar's buttons for a spellbook (the rule in the file header (1)).</summary>
        public List<StanceEntry> Stances(IList<AbilityView> book, SpellDb db)
        {
            var list = new List<StanceEntry>();
            if (book == null || db == null) return list;
            var known = new HashSet<int>();
            for (int i = 0; i < book.Count; i++) if (book[i] != null) known.Add(book[i].SpellId);
            var chain = new List<int>();
            for (int i = 0; i < book.Count; i++)
            {
                AbilityView v = book[i];
                if (v == null || v.Passive) continue;
                SpellInfo s = db.Find(v.SpellId);
                int form;
                if (!OnStanceBar(s, out form)) continue;
                // A later rank of the same chain in the book takes this button (Wow.exe 0x5424C4: the new rank replaces the old one).
                bool later = false;
                int n = v.SpellId, guard = 0;
                while (_next.TryGetValue(n, out n) && guard++ < 64) if (known.Contains(n)) { later = true; break; }
                if (later) continue;
                var e = new StanceEntry { SpellId = v.SpellId, Form = form, Order = StanceOrder(v.SpellId), BookIndex = i, Name = v.Name };
                ChainOf(v.SpellId, chain);
                e.Chain.AddRange(chain);
                list.Add(e);
            }
            list.Sort((a, b) =>
            {
                bool an = a.Order < 0, bn = b.Order < 0;
                if (an && bn) return a.SpellId.CompareTo(b.SpellId);
                if (an != bn) return an ? 1 : -1;
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : a.BookIndex.CompareTo(b.BookIndex);
            });
            if (list.Count > MaxStances) list.RemoveRange(MaxStances, list.Count - MaxStances);
            return list;
        }

        /// <summary>Checked: a form = the player is in it; an aura / presence = the player has an aura of its chain that the player cast
        /// (ours: a caster guid of 0 counts as the player's own).</summary>
        public static bool IsActive(StanceEntry e, PlayerState p)
        {
            if (e == null || p == null) return false;
            if (e.Form != 0) return p.ShapeshiftForm == e.Form;
            List<AuraView> auras = p.Auras;
            if (auras == null) return false;
            for (int i = 0; i < auras.Count; i++)
            {
                AuraView a = auras[i];
                if (a == null || !e.Chain.Contains(a.SpellId)) continue;
                if (a.CasterGuid == 0 || p.Guid == 0 || a.CasterGuid == p.Guid) return true;
            }
            return false;
        }
    }
}
