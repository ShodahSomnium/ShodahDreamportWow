// iteration 11 - owner trainer (proposal-trainer.md 7, decision D1 = A): the offline class trainers' window, the offline Skills pane, and the
// profession members offline (no professions in our offline zones - known gap G1). Every rule here is ours.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Professions;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        const int TrOfflineOptionIndex = 2;            // ours (npc's vendor option is 1, proposal-npc.md s7)
        const int TrOfflineOptionIcon = 3;             // GossipOptionView.Icon 3 = Trainer (Interface\GossipFrame\TrainerGossipIcon)
        const string TrOfflineOptionText = "Show me what you can teach.";   // ours (WoW's "I would like to train." is server data)
        const float TrOfflineCloseYards = 10f;         // ours = the offline dialog's walk-away distance (OfflineSession.Stats.cs)

        private TrainerView _trOffline;
        private OfflineCreature _trOfflineNpc;
        private TrOfflineFacts _trOfflineFacts;
        private bool _trOfflineSkillsBuilt, _trOfflineLogged;
        private ProfessionDb _trPdb;

        private ProfessionDb TrPdb { get { if (_trPdb == null) _trPdb = ProfessionDb.Get(_files); return _trPdb; } }

        private sealed class TrOfflineFacts : ITrainerFacts
        {
            private readonly OfflineSession _s;
            public TrOfflineFacts(OfflineSession s) { _s = s; }
            public int Level { get { return _s._player.Level; } }
            public int Money { get { return _s._player.Money; } }
            public int FreeProfessions { get { return 2; } }   // ours: no professions offline (WoW's limit is 2)
            public bool Knows(int spellId) { return _s._rec.Spells.Contains(spellId); }
            public int SkillValue(int skillLine)
            {
                foreach (var v in _s._player.Skills) if (v != null && v.SkillId == skillLine) return v.Value;
                return 0;
            }
            public string TooltipOf(SpellInfo s) { return _s.TrOfflineTooltip(s); }
        }

        private TrOfflineFacts TrOfflineMe { get { if (_trOfflineFacts == null) _trOfflineFacts = new TrOfflineFacts(this); return _trOfflineFacts; } }

        private string TrOfflineTooltip(SpellInfo s)
        {
            if (s == null) return null;
            var v = new AbilityView { SpellId = s.Id, Name = s.Name, Rank = s.Rank, IconPath = s.IconPath };
            try { v.Description = SpellText.Resolve(_db, s, s.Description, TextContext()); } catch (Exception) { v.Description = s.Description; }
            PowerType pt;
            v.Cost = CostOf(s, out pt);
            v.CostType = pt;
            v.CastTime = s.CastTimeMs / 1000f;
            v.Cooldown = Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs) / 1000f;
            v.RangeMin = s.RangeMin;
            v.RangeMax = s.RangeMax;
            v.Passive = s.IsPassive;
            v.LearnedAtLevel = Math.Max(1, s.SpellLevel);
            return BuildTooltip(s, v);
        }

        // ------------------------------------------------------------------ IWowSession: trainer

        public TrainerView CurrentTrainer { get { return _trOffline; } }

        public void BuyTrainerSpell(int spellId)
        {
            if (_trOffline == null) return;
            TrainerServiceView sv = null;
            foreach (var x in _trOffline.Services) if (x.SpellId == spellId) { sv = x; break; }
            if (sv == null) return;
            if (sv.State != TrainerSpellState.Available || _player.Level < sv.ReqLevel) { EmitError("You can't learn that yet."); return; }   // ours (as online reasons 0 / 2)
            LearnSpell(spellId, true);                 // "You have learned a new ability: X." + PlaceOnBar (as LevelUp)
            RebuildSpellbook();
            Emit(GameEventType.TrainerTrained, _trOffline.NpcGuid, 0, 0, spellId, false, null);
            WowLog.Info("Offline trainer: taught " + sv.Name + (sv.Rank.Length > 0 ? " (" + sv.Rank + ")" : "") + " " + spellId + " for free (ours)");
            TrOfflineRebuild();
        }

        public void CloseTrainer()
        {
            if (_trOffline == null) return;
            _trOffline = null;
            _trOfflineNpc = null;
        }

        // ------------------------------------------------------------------ IWowSession: professions (none offline - known gap G1)

        public TradeSkillView CurrentTradeSkill { get { return null; } }
        public void OpenTradeSkill(int skillId) { EmitError("Not available offline"); }   // the existing offline phrase (OfflineSession.ActionBars.cs)
        public void CloseTradeSkill() { }
        public void CraftRecipe(int recipeSpellId, int count) { EmitError("Not available offline"); }
        public void UnlearnSkill(int skillId) { EmitError("Not available offline"); }

        // ------------------------------------------------------------------ opening (npc's offline options F3, or the quests fallback F1)

        /// <summary>F3 (npc's offline OpenDialog, after the vendor option): a trainer of the player's class offers our "train" option.</summary>
        partial void TrOfflineAddOption(OfflineCreature npc, NpcDialog d)
        {
            if (npc == null || d == null || d.Options == null) return;
            if (OfflineTrainers.TrainsClass(npc.Tpl) != _player.Class || _player.Class == WowClass.None) return;
            foreach (var o in d.Options) if (o != null && o.Index == TrOfflineOptionIndex) return;
            d.Options.Add(new GossipOptionView { Index = TrOfflineOptionIndex, Icon = TrOfflineOptionIcon, Text = TrOfflineOptionText });
        }

        /// <summary>F3: npc's offline SelectGossipOption asks this first; true = it was the trainer option.</summary>
        private bool TrOfflineSelect(int index)
        {
            if (index != TrOfflineOptionIndex || _dialogNpc == null) return false;
            if (OfflineTrainers.TrainsClass(_dialogNpc.Tpl) != _player.Class) return false;
            var npc = _dialogNpc;
            CloseDialog();
            TrOfflineOpen(npc);
            return true;
        }

        /// <summary>F1 (quests' fallback after OpenDialog in Interact): a trainer of the player's class with no quest for the player opens its window
        /// (talk again after its quests). Nothing when npc's options already carry the trainer option (F3 handles it) or it is already open.</summary>
        partial void TrOfflineAfterDialog(OfflineCreature c)
        {
            if (c == null || _player.Class == WowClass.None || OfflineTrainers.TrainsClass(c.Tpl) != _player.Class) return;
            if (_trOffline != null && _trOfflineNpc == c) return;
            if (_dialog == null || _dialogNpc != c) return;
            if (_dialog.Options != null) foreach (var o in _dialog.Options) if (o != null && o.Index == TrOfflineOptionIndex) return;
            if (_dialog.Quests.Count > 0) return;
            CloseDialog();
            TrOfflineOpen(c);
        }

        private void TrOfflineOpen(OfflineCreature npc)
        {
            if (npc == null) return;
            if (_lootCorpse != null) CloseLoot();
            _trOfflineNpc = npc;
            _trOffline = null;
            TrOfflineRebuild();
            int a = 0, u = 0, k = 0;
            foreach (var sv in _trOffline.Services) { if (sv.State == TrainerSpellState.Available) a++; else if (sv.State == TrainerSpellState.Known) k++; else u++; }
            WowLog.Info("Offline trainer: " + npc.State.Name + " (" + _player.Class + ") - " + _trOffline.Services.Count + " services: " + a + " available, " + u + " unavailable, " + k + " known");
        }

        private void TrOfflineRebuild()
        {
            var npc = _trOfflineNpc;
            if (npc == null) return;
            string greeting = string.IsNullOrEmpty(npc.Tpl.Greeting) ? "Greetings, " + _player.Name + "."
                : npc.Tpl.Greeting.Replace("$N", _player.Name).Replace("$C", _player.Class.ToString());   // as OpenDialog (OfflineSession.Quests.cs)
            var v = OfflineTrainers.Build(npc.Guid, npc.State.Name, greeting, _player.Class, _rec.Race, _player.Level, _rec.Spells, _db, TrPdb, TrOfflineMe);
            v.Version = _trOffline != null ? _trOffline.Version + 1 : 1;
            _trOffline = v;
            TrOfflineSkills();
        }

        // ------------------------------------------------------------------ ticks (F2 / F4)

        partial void TrOfflineTick(float dt)
        {
            if (!_trOfflineSkillsBuilt) TrOfflineSkills();
            if (_trOffline == null) return;
            var npc = _trOfflineNpc;
            string why = null;
            if (npc == null || !npc.Present) why = "the trainer is gone";
            else if (npc.Dead) why = "the trainer died";
            else if (_player.Dead) why = "you died";
            else if (Dist(_me.Pos, npc.Pos) > TrOfflineCloseYards) why = "more than " + TrOfflineCloseYards + " yd away";
            if (why != null) { WowLog.Info("Offline trainer: closing (" + why + ")"); CloseTrainer(); return; }
            if (TrainerBook.RefreshLocal(_trOffline, TrOfflineMe, _db, TrPdb)) _trOffline.Version++;
        }

        partial void TrOfflineLevelChanged()
        {
            TrOfflineSkills();
            if (_trOffline != null) TrOfflineRebuild();
        }

        /// <summary>Player.Skills offline (7.3): computed, nothing saved.</summary>
        private void TrOfflineSkills()
        {
            _trOfflineSkillsBuilt = true;
            var list = SkillBook.Offline(_player.Class, _rec.Race, _player.Level, _rec.Spells, TrPdb);
            _player.Skills.Clear();
            _player.Skills.AddRange(list);
            _player.SkillsVersion++;
            _player.FreeProfessions = 2;
            if (!_trOfflineLogged)
            {
                _trOfflineLogged = true;
                WowLog.Info("Offline trainer: class trainers open their window for their own class (ours); no professions offline (known gap G1)");
            }
        }
    }
}
