// iteration 11 - owner trainer (proposal-trainer.md 5.3 "Skills", 6.1, 7.3): the Skills pane lines, online and offline. Pure.
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Professions
{
    public static class SkillBook
    {
        private static bool _loggedRules, _loggedOffline;

        /// <summary>
        /// Online: one SkillView per PLAYER_SKILL_INFO_1_1 slot (AC Player.h:79-89: index = id | step &lt;&lt; 16, value = value | max &lt;&lt; 16,
        /// bonus = int16 temp | int16 perm &lt;&lt; 16) whose id has a SkillLine row outside category 12 'Not Displayed' (ours: which lines the client
        /// hides is C code). SRCI flags 0x400 MONO_VALUE are shown 1/1 (a proficiency bar, SkillFrame.lua:161). Sorted by SkillLineCategory
        /// SortIndex, then name. known = the known spells (HasTradeWindow: a known TRADE_SKILL spell opens this line).
        /// </summary>
        public static List<SkillView> FromSlots(int[] idx, int[] val, int[] bonus, int race, WowClass cls, ProfessionDb pdb, ICollection<int> known)
        {
            var list = new List<SkillView>();
            if (idx == null || val == null || pdb == null) return list;
            HashSet<int> trade = pdb.TradeLinesOf(known);
            for (int i = 0; i < idx.Length && i < val.Length; i++)
            {
                int id = idx[i] & 0xFFFF;
                if (id == 0) continue;
                var line = pdb.Skill(id);
                if (line == null || line.Category == ProfessionDb.CategoryHidden) continue;
                var v = Make(line, pdb, race, cls, trade);
                v.Step = (idx[i] >> 16) & 0xFFFF;
                int value = val[i] & 0xFFFF, max = (val[i] >> 16) & 0xFFFF;
                int b = bonus != null && i < bonus.Length ? bonus[i] : 0;
                v.TempBonus = (short)(b & 0xFFFF);
                v.PermBonus = (short)((b >> 16) & 0xFFFF);
                var srci = pdb.Srci(id, race, cls);
                if (srci != null && srci.Mono) { value = 1; max = 1; }
                v.Value = value;
                v.Max = max;
                list.Add(v);
            }
            Sort(list, pdb);
            if (!_loggedRules)
            {
                _loggedRules = true;
                WowLog.Info("Skills: lines of SkillLineCategory 12 'Not Displayed' are hidden, SRCI MONO_VALUE (0x400) lines show 1/1, order = SkillLineCategory SortIndex then name (ours - the client decides in C code)");
            }
            return list;
        }

        private static SkillView Make(SkillLineInfo line, ProfessionDb pdb, int race, WowClass cls, HashSet<int> trade)
        {
            var srci = pdb.Srci(line.Id, race, cls);
            return new SkillView
            {
                SkillId = line.Id,
                Name = line.Name,
                Category = line.Category,
                CategoryName = pdb.CategoryName(line.Category),
                Abandonable = srci != null && srci.Abandonable,
                IsPrimaryProfession = line.Category == ProfessionDb.CategoryProfessions,
                HasTradeWindow = trade != null && trade.Contains(line.Id),
                IconPath = line.IconPath,
                Verb = line.Verb ?? "",
                Description = line.Description ?? ""
            };
        }

        public static void Sort(List<SkillView> list, ProfessionDb pdb)
        {
            list.Sort((a, b) =>
            {
                int c = pdb.CategorySort(a.Category).CompareTo(pdb.CategorySort(b.Category));
                if (c != 0) return c;
                c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.SkillId.CompareTo(b.SkillId);
            });
        }

        /// <summary>
        /// Offline (proposal-trainer.md 7.3, ours): the class skill lines (SkillLine category 7) that have a SkillRaceClassInfo row for the race / class
        /// AND a SkillLineAbility row of a known spell, with AzerothCore's default values in the spirit of Player::LearnDefaultSkill (Player.cpp:12138-12200,
        /// GetSkillRangeType ObjectMgr.cpp:9712-9740): MONO_VALUE -> 1/1; a SkillTiers row -> max = its first value; else LEVEL: max = level x 5;
        /// value = max when SRCI flags 0x10 ALWAYS_MAX_VALUE, else 1. Weapon / armor / language lines are server data (not derived offline).
        /// </summary>
        public static List<SkillView> Offline(WowClass cls, int race, int level, ICollection<int> known, ProfessionDb pdb)
        {
            var list = new List<SkillView>();
            if (pdb == null || !pdb.Available) return list;
            HashSet<int> trade = pdb.TradeLinesOf(known);
            foreach (int id in pdb.SkillsOf(race, cls))
            {
                var line = pdb.Skill(id);
                if (line == null || line.Category != 7) continue;
                bool has = false;
                if (known != null)
                    foreach (int sp in known) if (pdb.OnLine(sp, id, race, cls)) { has = true; break; }
                if (!has) continue;
                var srci = pdb.Srci(id, race, cls);
                var v = Make(line, pdb, race, cls, trade);
                int[] tier = srci != null && srci.TierId > 0 ? pdb.TierValues(srci.TierId) : null;
                int max = tier != null && tier[0] > 0 ? tier[0] : Math.Max(1, level) * 5;
                int value = srci != null && srci.AlwaysMax ? max : 1;
                if (srci != null && srci.Mono) { value = 1; max = 1; }
                v.Value = value;
                v.Max = max;
                list.Add(v);
            }
            Sort(list, pdb);
            if (!_loggedOffline)
            {
                _loggedOffline = true;
                WowLog.Info("Skills offline: " + list.Count + " class skill lines for " + cls + " (ours: class lines of known spells; weapon / armor / language skills are server data)");
            }
            return list;
        }

        /// <summary>
        /// The client's own messages (AzerothCore sends none - the client compares the fields): after the first snapshot only, a new skill whose SRCI
        /// flags lack 0x2 NO_SKILLUP_MESSAGE -> ERR_SKILL_GAINED_S "You have gained the %s skill." (GlobalStrings.lua:3486); a higher Value -> ERR_SKILL_UP_SI
        /// "Your skill in %s has increased to %d." (GlobalStrings.lua:3487). Every change is a SkillChanged event (Text null for max / step changes and
        /// removals; Amount 0 = removed). before null = the first snapshot: nothing.
        /// </summary>
        public static List<GameEvent> Diff(List<SkillView> before, List<SkillView> after, ProfessionDb pdb, int race, WowClass cls)
        {
            var events = new List<GameEvent>();
            if (before == null || after == null) return events;
            var old = new Dictionary<int, SkillView>();
            foreach (var s in before) if (s != null) old[s.SkillId] = s;
            var seen = new HashSet<int>();
            foreach (var s in after)
            {
                if (s == null) continue;
                seen.Add(s.SkillId);
                SkillView o;
                var srci = pdb != null ? pdb.Srci(s.SkillId, race, cls) : null;
                bool quiet = srci != null && srci.NoSkillUpMessage;
                if (!old.TryGetValue(s.SkillId, out o))
                {
                    events.Add(new GameEvent { Type = GameEventType.SkillChanged, SpellId = s.SkillId, Amount = s.Value,
                                               Text = quiet ? null : "You have gained the " + s.Name + " skill." });   // ERR_SKILL_GAINED_S (GlobalStrings.lua:3486)
                    continue;
                }
                if (s.Value > o.Value)
                    events.Add(new GameEvent { Type = GameEventType.SkillChanged, SpellId = s.SkillId, Amount = s.Value,
                                               Text = quiet ? null : "Your skill in " + s.Name + " has increased to " + s.Value + "." });   // ERR_SKILL_UP_SI (GlobalStrings.lua:3487)
                else if (s.Value != o.Value || s.Max != o.Max || s.Step != o.Step || s.TempBonus != o.TempBonus || s.PermBonus != o.PermBonus || s.HasTradeWindow != o.HasTradeWindow)
                    events.Add(new GameEvent { Type = GameEventType.SkillChanged, SpellId = s.SkillId, Amount = s.Value });
            }
            foreach (var s in before)
                if (s != null && !seen.Contains(s.SkillId)) events.Add(new GameEvent { Type = GameEventType.SkillChanged, SpellId = s.SkillId, Amount = 0 });
            return events;
        }
    }
}
