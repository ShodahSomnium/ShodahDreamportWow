// iteration 11 - owner trainer (proposal-trainer.md 7.1 / 7.2, decision D1 = A): the offline class trainers' window. Pure; every rule here is ours.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Professions
{
    public static class OfflineTrainers
    {
        private static readonly WowClass[] Classes =
        {
            WowClass.Warrior, WowClass.Paladin, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.DeathKnight,
            WowClass.Shaman, WowClass.Mage, WowClass.Warlock, WowClass.Druid
        };
        private static bool _loggedFree;

        public static string ClassDisplayName(WowClass c) { return c == WowClass.DeathKnight ? "Death Knight" : c.ToString(); }

        /// <summary>The class an offline NPC trains: our own content's SubName "&lt;class name&gt; Trainer" (e.g. "Warrior Trainer"), else None.</summary>
        public static WowClass TrainsClass(CreatureTemplate t)
        {
            if (t == null || string.IsNullOrEmpty(t.SubName)) return WowClass.None;
            foreach (var c in Classes)
                if (string.Equals(t.SubName, ClassDisplayName(c) + " Trainer", StringComparison.Ordinal)) return c;
            return WowClass.None;
        }

        /// <summary>
        /// The offline trainer window (ours): every class-kit spell (SpellDb.Kit) up to the offline level cap (WowFormulas.MaxLevel) as a free
        /// service - Known when the character knows it or a higher-level spell of the same name (LearnSpell replaces lower ranks), Available when
        /// its level is reached (a spell the character should know but does not), else Unavailable ("Requires: Level N"). Spells are still
        /// learned on level-up offline (decision D1 = A); Train teaches a missed one.
        /// </summary>
        public static TrainerView Build(ulong npcGuid, string npcName, string greeting, WowClass cls, int race, int level, ICollection<int> known,
                                        SpellDb db, ProfessionDb pdb, ITrainerFacts me)
        {
            var v = new TrainerView { NpcGuid = npcGuid, NpcName = npcName ?? "", Greeting = greeting ?? "", Kind = TrainerKind.Class, Offline = true };
            if (db == null || !db.Available) return v;
            // the best level known per spell name (a higher rank known = the lower ranks are known too)
            var bestKnown = new Dictionary<string, int>(StringComparer.Ordinal);
            if (known != null)
                foreach (int id in known)
                {
                    var s = db.Find(id);
                    if (s == null) continue;
                    int lv;
                    if (!bestKnown.TryGetValue(s.Name, out lv) || s.SpellLevel > lv) bestKnown[s.Name] = s.SpellLevel;
                }
            var seen = new HashSet<int>();
            foreach (var k in db.Kit(cls, race))
            {
                if (k.Level > WowFormulas.MaxLevel || !seen.Add(k.SpellId)) continue;
                var sv = new TrainerServiceView { SpellId = k.SpellId, ReqLevel = k.Level, MoneyCost = 0, ProfessionCost = 0 };
                int lv;
                bool isKnown = (known != null && known.Contains(k.SpellId)) || (bestKnown.TryGetValue(k.Name ?? "", out lv) && lv > k.Level);
                sv.State = isKnown ? TrainerSpellState.Known : k.Level <= level ? TrainerSpellState.Available : TrainerSpellState.Unavailable;
                TrainerBook.Display(sv, me, db, pdb);
                v.Services.Add(sv);
            }
            TrainerBook.Sort(v.Services);
            TrainerBook.RefreshLocal(v, me, db, pdb);
            if (!_loggedFree)
            {
                _loggedFree = true;
                WowLog.Info("Offline trainer: spells are free and listed up to level " + WowFormulas.MaxLevel + " (ours: no trainer prices exist offline; spells are still learned on level-up, Train teaches a missed one)");
            }
            return v;
        }
    }
}
