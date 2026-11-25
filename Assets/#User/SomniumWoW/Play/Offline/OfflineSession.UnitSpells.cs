using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// Iteration 3: threat, victims, spells cast by creatures / pets / totems, and missiles in flight (damage lands when the missile
    /// arrives: Spell.dbc Speed in yards per second).
    /// </summary>
    public sealed partial class OfflineSession
    {
        private sealed class PendingHit
        {
            public SpellInfo Spell;
            public ulong Caster, Target;
            public double At;
            public int Combo;
            public bool Immersive;         // stage S1 (PLAN10 s5): the missile of a cast released with Immersive casting
        }

        private readonly List<PendingHit> _pending = new List<PendingHit>();

        /// <summary>The player, the player's pet and totems (and friendly NPCs, iteration 4) are one side; zone creatures the other.</summary>
        private bool IsPlayerSide(OfflineUnit u)
        {
            if (u == null) return false;
            if (u == _me) return true;
            var c = u as OfflineCreature;
            return c != null && (c.Summon != SummonKind.None || (c.Tpl != null && c.Tpl.IsNpc) || c.State.Reaction == Reaction.Friendly);   // iteration 4: friendly NPCs take the player's buffs/heals
        }

        private bool IsHostilePair(OfflineUnit a, OfflineUnit b) { return a != null && b != null && IsPlayerSide(a) != IsPlayerSide(b); }

        // ------------------------------------------------------------------ threat

        private void AddThreat(OfflineCreature c, OfflineUnit who, float amount)
        {
            if (c == null || who == null || c.Summon != SummonKind.None || c.Tpl.IsNpc || c.Dead) return;
            float cur;
            c.Threat.TryGetValue(who.Guid, out cur);
            c.Threat[who.Guid] = cur + Math.Max(0.01f, amount);
        }

        /// <summary>Whom a fighting creature attacks: a taunter while the taunt lasts, else the highest threat alive, else the player.</summary>
        private OfflineUnit VictimOf(OfflineCreature c)
        {
            if (c.ForcedTimer > 0f)
            {
                var f = UnitOf(c.ForcedTarget);
                if (f != null && !f.Dead && (f != _me || !_player.Dead)) return f;
            }
            OfflineUnit best = null;
            float bt = -1f;
            foreach (var kv in c.Threat)
            {
                var u = UnitOf(kv.Key);
                if (u == null || u.Dead || (u == _me && _player.Dead)) continue;
                if (kv.Value > bt) { bt = kv.Value; best = u; }
            }
            if (best == null && !_player.Dead) return _me;
            return best;
        }

        // ------------------------------------------------------------------ casting by non-player units

        /// <summary>Starts a cast of a creature/pet/totem (instant spells go off at once). Emits CastStarted with the caster as source.</summary>
        private void StartUnitCast(OfflineUnit u, SpellInfo s, OfflineUnit target)
        {
            if (u == null || s == null || target == null) return;
            u.Casting = s;
            u.CastTargetGuid = target.Guid;
            u.CastElapsed = 0f;
            float haste = u.AuraMultiplier(SpellAura.MeleeSlow, 0);
            u.CastTotal = s.CastTimeMs / 1000f / Math.Max(0.1f, haste);
            if (u is OfflineCreature cc) { cc.State.CastingSpellId = s.Id; cc.State.CastTargetGuid = target.Guid; cc.State.Channeling = false; cc.State.CastProgress = 0f; }
            if (u.CastTotal <= 0f) { FinishUnitCast(u); return; }
            Emit(GameEventType.CastStarted, u.Guid, target.Guid, (int)(u.CastTotal * 1000f), s.Id, false, s.Name);
        }

        private void StepUnitCast(OfflineUnit u, float dt)
        {
            var s = u.Casting;
            if (s == null) return;
            var target = UnitOf(u.CastTargetGuid);
            if (u.Incapacitated || (u.Silenced && s.DmgClass == 1)) { InterruptUnitCast(u, "Interrupted"); return; }
            if (target == null || target.Dead || (target == _me && _player.Dead)) { InterruptUnitCast(u, "Invalid target"); return; }
            u.CastElapsed += dt;
            if (u is OfflineCreature cc) cc.State.CastProgress = u.CastTotal > 0 ? Math.Min(1f, u.CastElapsed / u.CastTotal) : 1f;
            if (u.CastElapsed >= u.CastTotal) FinishUnitCast(u);
        }

        private void InterruptUnitCast(OfflineUnit u, string reason)
        {
            if (u.Casting == null) return;
            int id = u.Casting.Id;
            ulong t = u.CastTargetGuid;
            u.Casting = null;
            u.CastElapsed = u.CastTotal = 0f;
            if (u is OfflineCreature cc) { cc.State.CastingSpellId = 0; cc.State.CastProgress = 0f; cc.State.CastTargetGuid = 0; }
            Emit(GameEventType.CastFailed, u.Guid, t, 0, id, false, reason);
        }

        private void FinishUnitCast(OfflineUnit u)
        {
            var s = u.Casting;
            var target = UnitOf(u.CastTargetGuid);
            u.Casting = null;
            u.CastElapsed = u.CastTotal = 0f;
            if (u is OfflineCreature cc) { cc.State.CastingSpellId = 0; cc.State.CastProgress = 0f; cc.State.CastTargetGuid = 0; }
            if (s == null || target == null) return;
            int cost = CreatureCost(u, s);
            if (u.MaxMana > 0 && cost > 0) u.Mana = Math.Max(0, u.Mana - cost);
            SetUnitCooldown(u, s);
            Emit(GameEventType.CastSucceeded, u.Guid, target.Guid, 0, s.Id, false, s.Name);
            if (u is OfflineCreature caster) caster.State.AttackCounter++;   // cast release: views may play the spell-cast animation
            LaunchOrHit(s, u, target, 0);
        }

        /// <summary>Missiles (Spell.dbc Speed > 0, e.g. Fireball, Shadow Bolt, Firebolt) land after distance / speed; everything else at once.</summary>
        private void LaunchOrHit(SpellInfo s, OfflineUnit caster, OfflineUnit target, int combo)
        {
            float d = Dist(caster.Pos, target.Pos);
            if (s.Speed > 0.5f && target != caster && d > 1f)
            {
                _pending.Add(new PendingHit { Spell = s, Caster = caster.Guid, Target = target.Guid, At = _time + Math.Min(3.0, d / s.Speed), Combo = combo, Immersive = caster == _me && ImmersiveDamage(s) });
                return;
            }
            if (caster == _me) ExecuteSpell(s, target);
            else ExecuteUnitSpell(caster, s, target);
        }

        /// <summary>Stage S1 (PLAN10 s5): a player missile lands - with the immersive damage bonus when its cast was released with Immersive casting.</summary>
        private void ExecutePlayerHit(PendingHit p, OfflineUnit target)
        {
            if (!p.Immersive) { ExecuteSpell(p.Spell, target); return; }
            int was = _immersiveCastSpell;
            _immersiveCastSpell = p.Spell.Id;
            try { ExecuteSpell(p.Spell, target); }
            finally { _immersiveCastSpell = was; }
        }

        private void StepPending()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (_time < p.At) continue;
                _pending.RemoveAt(i);
                i--;
                var caster = UnitOf(p.Caster);
                var target = UnitOf(p.Target);
                if (target == null || target.Dead || (target == _me && (_player.Dead || _player.OnTaxi))) continue;   // iteration 4: missiles miss a player who took off
                if (p.Caster == PlayerGuid) { if (!_player.Dead) ExecutePlayerHit(p, target); }
                else if (caster != null && !caster.Dead) ExecuteUnitSpell(caster, p.Spell, target);
            }
        }

        /// <summary>Magic hit roll of a non-player caster (WotLK spell table by level difference, racial resistances of the player: -2% per school, a186).</summary>
        private bool UnitSpellHits(OfflineUnit caster, SpellInfo s, OfflineUnit target)
        {
            if (!IsHostilePair(caster, target) || s.DmgClass == 0) return true;
            if (target is OfflineCreature tc && tc.Ai == AiState.Evade) return false;
            int diff = target.Level - caster.Level;
            float miss = diff <= 2 ? Math.Max(1f, 4f + diff) : 17f + (diff - 3) * 11f;
            if (target == _me) miss -= _me.AuraSum(SpellAura.ModAttackerSpellHitChance, s.SchoolMask);   // bp -3 +1 = -2 -> +2% miss
            return Rand01() * 100f >= miss;
        }

        /// <summary>A spell of a creature, pet or totem landing: damage, heals, auras, threat - the same effect data as the player's spells.</summary>
        private void ExecuteUnitSpell(OfflineUnit caster, SpellInfo s, OfflineUnit target)
        {
            bool hostile = IsHostilePair(caster, target);
            if (hostile && !UnitSpellHits(caster, s, target))
            {
                Emit(GameEventType.SpellMiss, caster.Guid, target.Guid, 0, s.Id, false, s.DmgClass == 1 ? "Resist" : "Miss");
                return;
            }
            var auraDone = new List<OfflineUnit>(2);
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                var units = new List<OfflineUnit>(2);
                if (e.IsAreaEnemy)
                {
                    float radius = e.Radius > 0 ? e.Radius : 8f;
                    var center = (e.TargetA == SpellTarget.DestAreaEnemy || e.TargetB == SpellTarget.DestAreaEnemy) && e.TargetA != SpellTarget.DestCaster ? target.Pos : caster.Pos;
                    if (IsPlayerSide(caster))
                    {
                        for (int k = 0; k < _creatures.Count; k++) { var c = _creatures[k]; if (IsAttackable(c) && Dist(center, c.Pos) <= radius + c.CombatReach) units.Add(c); }
                    }
                    else
                    {
                        if (!_player.Dead && Dist(center, _me.Pos) <= radius + _me.CombatReach) units.Add(_me);
                        if (_pet != null && _pet.Present && Dist(center, _pet.Pos) <= radius) units.Add(_pet);
                    }
                }
                else if (e.TargetA == SpellTarget.UnitCaster || (!e.TargetsEnemy && e.TargetA != SpellTarget.TargetAny && e.TargetA != 77 && !hostile)) units.Add(e.TargetA == SpellTarget.UnitCaster ? caster : target);
                else units.Add(target);
                foreach (var u in units) ApplyUnitEffect(caster, s, e, u, auraDone);
            }
        }

        private void ApplyUnitEffect(OfflineUnit caster, SpellInfo s, SpellEffectInfo e, OfflineUnit u, List<OfflineUnit> auraDone)
        {
            if (u == null || u.Dead) return;
            var c = u as OfflineCreature;
            switch (e.Effect)
            {
                case SpellEffect.SchoolDamage:
                {
                    int v = Math.Max(1, CalcValue(s, e, caster.Level));
                    if (s.SchoolMask == 1 && c != null) v = Math.Max(1, (int)(v * WowFormulas.ArmorMultiplier(c.Armor, caster.Level)));
                    if (s.SchoolMask == 1 && u == _me) v = Math.Max(1, (int)(v * WowFormulas.ArmorMultiplier(_armor, caster.Level)));
                    if (caster is OfflineCreature cc && cc.Summon == SummonKind.Pet) v = Math.Max(1, (int)(v * CommandBonus()));
                    else if (caster is OfflineCreature zc && zc.Summon == SummonKind.None) v = Math.Max(1, (int)(v * DifficultyDamage[_difficulty]));   // settings: creature spell damage
                    bool crit = Rand01() < 0.05f;
                    if (crit) v = v * 3 / 2;
                    DealDamage(caster, u, v, s.Id, crit, GameEventType.SpellDamage);
                    return;
                }
                case SpellEffect.Heal:
                    HealBy(caster, u, Math.Max(1, CalcValue(s, e, caster.Level)), s.Id, GameEventType.SpellHeal);
                    return;
                case SpellEffect.Threat: case SpellEffect.AttackMe:
                    if (c != null && IsHostilePair(caster, c))
                    {
                        // Torment / taunts: the creature turns to the caster for 3 s and gains threat (APPROXIMATED as a short taunt).
                        c.ForcedTarget = caster.Guid;
                        c.ForcedTimer = 3f;
                        AddThreat(c, caster, CalcValue(s, e, caster.Level));
                        Engage(c, caster);
                    }
                    return;
                case SpellEffect.TriggerSpell:
                {
                    var trig = _db.Find(e.TriggerSpell);
                    if (trig != null) ExecuteUnitSpell(caster, trig, u);
                    return;
                }
                case SpellEffect.ApplyAura: case SpellEffect.ApplyAreaAuraParty: case SpellEffect.ApplyAreaAuraRaid: case SpellEffect.ApplyAreaAuraFriend:
                    if (auraDone.Contains(u)) return;
                    auraDone.Add(u);
                    ApplySpellAuras(s, caster, u, 0);
                    if (c != null && IsHostilePair(caster, c)) Engage(c, caster);
                    return;
                case SpellEffectKnockBack:
                    if (u == _me) OfflineKnockbackFrom(caster, e);   // iteration 11 (motion 17c): a creature's knockback moves the player (OfflineSession.Motion.cs)
                    return;
            }
        }

        private const int SpellEffectKnockBack = 98;   // SPELL_EFFECT_KNOCK_BACK (AC SharedDefines.h)

        /// <summary>Damage from any source to any unit (routes to the player, a summon or a creature).</summary>
        private void DealDamage(OfflineUnit source, OfflineUnit target, int amount, int spellId, bool crit, GameEventType type)
        {
            if (target == _me) { DamagePlayer(amount, source as OfflineCreature, spellId, crit, type); return; }
            var c = target as OfflineCreature;
            if (c == null) return;
            if (c.Summon != SummonKind.None) DamageSummon(c, amount, source, spellId, crit, type);
            else DamageCreature(c, amount, spellId, crit, type, type != GameEventType.PeriodicDamage, source == _me ? null : source);
        }

        private void HealBy(OfflineUnit source, OfflineUnit u, int amount, int spellId, GameEventType type)
        {
            if (u == null || u.Dead || amount <= 0) return;
            u.Health = Math.Min(u.MaxHealth, u.Health + amount);
            Emit(type, source != null ? source.Guid : 0, u.Guid, amount, spellId, false, null);
            if (u is OfflineCreature cc) cc.State.Health = cc.Health;
        }

        // ------------------------------------------------------------------ creatures casting their template spells

        /// <summary>Picks and starts one of the creature's template spells when its timer is ready. True when a cast started.</summary>
        private bool TryCreatureSpell(OfflineCreature c, OfflineUnit victim, float dist)
        {
            var list = c.Tpl.Spells;
            if (list == null || list.Length == 0 || c.SpellTimer > 0f || c.Silenced || c.Incapacitated) return false;
            for (int n = 0; n < list.Length; n++)
            {
                int idx = (c.SpellIndex + n) % list.Length;
                var s = _db.Find(list[idx]);
                if (s == null || s.IsPassive || !CooldownOk(c, s)) continue;
                int cost = CreatureCost(c, s);
                if (c.MaxMana > 0 && c.Mana < cost) continue;
                bool harmful = s.IsHarmful || HasEnemyTargetEffect(s);
                OfflineUnit target;
                if (harmful)
                {
                    float max = s.RangeMax > 5f ? s.RangeMax + victim.CombatReach : MeleeRange(c, victim);
                    if (dist > max) continue;
                    if (s.AuraOf(SpellAura.PeriodicDamage) != null && HasAuraFrom(victim, s.Id, c.Guid)) continue;   // DoT still ticking
                    target = victim;
                }
                else
                {
                    bool heal = s.HasEffect(SpellEffect.Heal) || s.AuraOf(SpellAura.PeriodicHeal) != null;
                    if (heal && c.Health * 100 >= c.MaxHealth * 40) continue;
                    if (!heal && c.FindAura(s.Id) != null) continue;
                    target = c;
                }
                c.SpellIndex = idx + 1;
                c.SpellTimer = Math.Max(1.5f, c.Tpl.SpellInterval) * RandRange(0.8f, 1.2f);
                c.State.Moving = false;
                StartUnitCast(c, s, target);
                return true;
            }
            return false;
        }

        private static bool HasAuraFrom(OfflineUnit u, int spellId, ulong caster)
        {
            for (int a = 0; a < u.Auras.Count; a++) if (u.Auras[a].Spell.Id == spellId && u.Auras[a].Caster == caster) return true;
            return false;
        }

        /// <summary>A caster creature (unit class 8 with a ranged damage spell and the mana for it) keeps its distance instead of closing to melee.</summary>
        private bool CasterHoldsRange(OfflineCreature c, OfflineUnit victim, float dist)
        {
            if (c.Tpl.UnitClass != 8 || c.Tpl.Spells == null || c.Silenced) return false;
            foreach (int id in c.Tpl.Spells)
            {
                var s = _db.Find(id);
                if (s == null || s.RangeMax <= 5f || !s.HasEffect(SpellEffect.SchoolDamage)) continue;
                if (c.MaxMana > 0 && c.Mana < CreatureCost(c, s)) continue;
                if (dist <= s.RangeMax + victim.CombatReach - 1f) return true;
            }
            return false;
        }
    }
}
