// iteration 11 - owner talents (wave-0 stub by w0play, filled by the talents builder; proposal-talents.md 7, PLAN11 s2 "offline talents option A")
// Offline talents: the trees of the player's install, the points of WoW's rule (TalentDb.PointsForLevel: level - 9; offline characters stop
// at level 10 = one point), learned with AzerothCore's rules (TalentDb.CheckLearn) and saved in CharacterRecord "talents" (pairs talentId,
// rankCount). Effects: a learned rank spell becomes a hidden permanent aura (ApplyPassive), which the offline stat rules read for the aura
// types of OfflineSession.Stats.cs RecalcStats (SimulatedAuras below); the Talents window says "Not simulated offline" for the others (ours).
// No glyphs (the first socket opens at 15), no dual spec, no pet talents, no reputation / titles offline: one Info line each (ours).
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        const string TalOnlineOnly = "Online only - play on your realm.";   // ours (proposal-talents.md 7)

        /// <summary>The aura types the offline stat rules read (OfflineSession.Stats.cs RecalcStats / attack timing): a talent made only of
        /// these is simulated offline (ours: derived from that code).</summary>
        static readonly HashSet<int> SimulatedAuras = new HashSet<int>
        {
            SpellAura.ModStat, SpellAura.ModPercentStat, SpellAura.ModTotalStatPercentage, SpellAura.ModBaseHealthPct, SpellAura.ModIncreaseHealthPercent,
            SpellAura.ModIncreaseHealth, SpellAura.ModBaseResistancePct, SpellAura.ModResistance, SpellAura.ModBaseResistance, SpellAura.ModResistancePct,
            SpellAura.ModAttackPower, SpellAura.ModRangedAttackPower, SpellAura.ModWeaponCritPercent, SpellAura.ModDodgePercent, SpellAura.ModParryPercent,
            SpellAura.ModDamagePercentDone, SpellAura.ModMeleeHaste, SpellAura.MeleeSlow, SpellAura.ModRangedHaste,
        };

        TalentDb _talOffDb;
        bool _talOffDbTried, _talOffInit;
        readonly HashSet<string> _talOffLogged = new HashSet<string>();

        TalentDb TalOffDb
        {
            get
            {
                if (_talOffDb == null && !_talOffDbTried) { _talOffDbTried = true; _talOffDb = TalentDb.Get(_files); }
                return _talOffDb;
            }
        }

        /// <summary>Offline TalentsView from the record (first call: the learned rank spells become their passive auras) and the free points of
        /// the level. Idempotent and cheap: called by the talents members and by the Talents window; the integrator also calls it once when the
        /// session starts (request in P/reports11/talents.md) so the passives apply without opening the window.</summary>
        public void TalentsOfflineSync()
        {
            var T = _player.Talents;
            var db = TalOffDb;
            bool changed = false;
            if (!_talOffInit)
            {
                _talOffInit = true;
                var spec = T.Specs[0];
                spec.Ranks.Clear();
                int kept = 0, dropped = 0;
                var rec = _rec.Talents;
                for (int i = 0; i + 1 < rec.Count; i += 2)
                {
                    int id = rec[i], count = rec[i + 1];
                    var t = db != null && db.Available ? db.Find(id) : null;
                    var tabs = db != null && db.Available ? db.ClassTabs(_rec.Class) : null;
                    bool mine = false;
                    if (t != null && tabs != null) foreach (var tab in tabs) if (tab.Id == t.TabId) mine = true;
                    if (t == null || !mine || count < 1 || count > t.MaxRank) { dropped++; continue; }
                    spec.Ranks[id] = count;
                    kept++;
                }
                // a record from another level / rule set: never more points than the level gives (drop the highest tiers first)
                int budget = TalentDb.PointsForLevel(_player.Level);
                while (TalentDb.PointsSpent(spec) > budget && spec.Ranks.Count > 0)
                {
                    int worst = 0, worstRow = -1;
                    foreach (var kv in spec.Ranks) { var t = db.Find(kv.Key); if (t != null && t.Row > worstRow) { worstRow = t.Row; worst = kv.Key; } }
                    spec.Ranks.Remove(worst);
                    dropped++;
                }
                foreach (var kv in spec.Ranks) TalOffApply(db, db.Find(kv.Key), 0, kv.Value);
                if (dropped > 0) TalOffSave();
                T.SpecCount = 1; T.ActiveSpec = 0; T.GlyphsEnabledMask = 0;
                T.Known = true;
                changed = true;
                WowLog.Info("Offline talents: " + kept + " learned talents from the record" + (dropped > 0 ? ", " + dropped + " dropped (unknown / not this class / over the level's points)" : "")
                            + (db != null && db.Available ? "" : " (no Talent.dbc - no trees)"));
            }
            int free = TalentDb.PointsForLevel(_player.Level) - TalentDb.PointsSpent(T.Specs[0]);
            if (free < 0) free = 0;
            if (free != T.FreePoints) { T.FreePoints = free; changed = true; }
            if (changed) T.Counter++;
        }

        /// <summary>True when the offline rules simulate this talent rank spell (every effect is an aura type the offline stats read).</summary>
        public bool TalentSimulatedOffline(int spellId)
        {
            var s = _db.Find(spellId);
            if (s == null) return false;
            bool any = false;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || e.Effect == 0) continue;
                if (!e.IsAura || !SimulatedAuras.Contains(e.Aura)) return false;
                any = true;
            }
            return any;
        }

        /// <summary>Talents "Reset" offline (ours: offline has no trainer gossip): unlearns every talent at no cost.</summary>
        public void ResetTalentsOffline()
        {
            TalentsOfflineSync();
            var T = _player.Talents;
            var spec = T.Specs[0];
            if (spec.Ranks.Count == 0) { EmitError("You have not spent any talent points."); return; }   // ERR_TALENT_WIPE_ERROR (GlobalStrings.lua:3509)
            var db = TalOffDb;
            foreach (var kv in spec.Ranks) TalOffApply(db, db != null ? db.Find(kv.Key) : null, kv.Value, 0);
            spec.Ranks.Clear();
            TalOffSave();
            _talOffInit = true;
            TalentsOfflineSync();
            T.Counter++;
            WowLog.Info("Offline talents: all talents unlearned (offline reset, ours)");
        }

        // ------------------------------------------------------------------ IWowSession

        public void LearnTalents(bool pet, IList<KeyValuePair<int, int>> talentRanks)
        {
            if (pet) { TalOffInfo("pet talents", "Pet talents are online only."); return; }   // ours
            if (talentRanks == null || talentRanks.Count == 0) return;
            TalentsOfflineSync();
            var T = _player.Talents;
            var db = TalOffDb;
            if (db == null || !db.Available) { WowLog.Warn("Offline talents: no talent data (Talent.dbc)"); return; }
            var spec = T.Specs[0];
            int learned = 0;
            foreach (var kv in db.SendOrder(talentRanks))
            {
                string why = db.CheckLearn(_rec.Class, spec, T.FreePoints, kv.Key, kv.Value, false, -1);
                if (why != null) { WowLog.Info("Offline talents: talent " + kv.Key + " rank " + kv.Value + " refused (" + why + ")"); continue; }
                int old = spec.Rank(kv.Key);
                spec.Ranks[kv.Key] = kv.Value;
                T.FreePoints -= kv.Value - old;
                TalOffApply(db, db.Find(kv.Key), old, kv.Value);
                learned++;
                var t = db.Find(kv.Key);
                var s = _db.Find(TalentDb.RankSpell(t, kv.Value));
                WowLog.Info("Offline talents: learned " + (s != null ? s.Name : "talent " + kv.Key) + " " + kv.Value + "/" + t.MaxRank
                            + (s != null && TalentSimulatedOffline(s.Id) ? "" : " (not simulated offline; online the realm applies it)"));
            }
            if (learned == 0) return;
            TalOffSave();
            T.Counter++;
            _statsDirty = true;
        }

        public void AnswerTalentWipe(bool accept)
        {
            var T = _player.Talents;
            if (!T.WipePending) return;                                       // never pending offline (no trainer gossip)
            T.WipePending = false; T.WipeCost = 0; T.Counter++;
        }

        public void ActivateSpec(int spec) { TalOffInfo("dual specialization", TalOnlineOnly); }
        public void InscribeGlyph(int socket)
        {
            var T = _player.Talents;
            if (socket < 0) { if (T.GlyphPick) { T.GlyphPick = false; T.GlyphPickGlyphId = 0; T.Counter++; } return; }
            TalOffInfo("glyphs", TalOnlineOnly);
        }
        public void RemoveGlyph(int socket) { TalOffInfo("glyphs", TalOnlineOnly); }
        public void SetFactionAtWar(int repListId, bool atWar) { TalOffInfo("reputation", TalOnlineOnly); }
        public void SetFactionInactive(int repListId, bool inactive) { TalOffInfo("reputation", TalOnlineOnly); }
        public void SetWatchedFaction(int repListId) { TalOffInfo("reputation", TalOnlineOnly); }
        public void SetTitle(int bitIndex) { TalOffInfo("titles", TalOnlineOnly); }

        // ------------------------------------------------------------------ helpers

        void TalOffInfo(string what, string text)
        {
            EmitInfo(text);
            if (_talOffLogged.Add(what)) WowLog.Info("Offline: " + what + " - " + text + " (logged once)");
        }

        /// <summary>Swaps the passive aura of a talent from rank COUNT oldCount to newCount (0 = none).</summary>
        void TalOffApply(TalentDb db, TalentInfo t, int oldCount, int newCount)
        {
            if (t == null) return;
            int oldSpell = TalentDb.RankSpell(t, oldCount), newSpell = TalentDb.RankSpell(t, newCount);
            if (oldSpell != 0 && oldSpell != newSpell)
            {
                for (int a = _me.Auras.Count - 1; a >= 0; a--) if (_me.Auras[a].Spell != null && _me.Auras[a].Spell.Id == oldSpell) RemoveAuraAt(_me, a);
                _statsDirty = true;
            }
            if (newSpell == 0) return;
            var s = _db.Find(newSpell);
            if (s == null) return;
            if (s.IsPassive) ApplyPassive(s);
            else if (_talOffLogged.Add("active " + newSpell)) WowLog.Info("Offline talents: " + s.Name + " is not a passive spell - not simulated offline");
        }

        void TalOffSave()
        {
            var spec = _player.Talents.Specs[0];
            _rec.Talents.Clear();
            foreach (var kv in spec.Ranks) { _rec.Talents.Add(kv.Key); _rec.Talents.Add(kv.Value); }
        }
    }
}
