using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        private enum HitOutcome : byte { Miss, Dodge, Parry, Block, Glancing, Crit, Hit, Evade }

        private bool _autoAttack, _autoShot;
        private float _swingTimer, _rangedTimer;
        private bool _outOfRangeWarned, _rangedWarned;
        private SpellInfo _nextSwing;            // queued Heroic Strike / Raptor Strike / Maul
        private int _combo;
        private ulong _comboTarget;

        /// <summary>Corpse time in seconds (Creature.cpp m_corpseDelay default 60), 30 s once looted empty.</summary>
        public const float CorpseSeconds = 60f;
        /// <summary>A creature leaves its fight and runs home beyond this distance from its spawn point (yards).</summary>
        public const float LeashRange = 40f;
        /// <summary>Creatures of the same kind within this many yards join a fight (TrinityCore assistance radius default 10).</summary>
        public const float AssistRadius = 10f;

        // ------------------------------------------------------------------ IWowSession: attacking

        private int _autoDismountSaid;   // iteration 11 (combat feel): log rate limit

        public void StartAttack(ulong guid)
        {
            if (_player.Dead) { EmitError("You are dead"); return; }
            // ERR_ATTACK_MOUNTED (GlobalStrings.lua:2915): on the taxi mount (iteration 4) and on a ridden mount (iteration 7: AC Unit.cpp
            // 7087-7089 "// player cannot attack in mount state" `if (IsPlayer() && IsMounted()) return false;` - the attack does not start).
            // Iteration 11 (combat feel): a ridden mount on the ground is left first - the client's CVar "autoDismount" ("Automatically
            // dismount when needed", Wow.exe 12340 registers it with default "1"; "autoDismountFlying" default "0"), as MountedCastGate does
            // for spells. In the air (flying) and on a taxi the attack stays refused.
            if (_player.OnTaxi || (_player.Mounted && _motion.Driven && _motion.Flying)) { EmitError("Can't attack while mounted."); return; }
            var c = CreatureOf(guid);
            if (!IsAttackable(c)) { EmitError(c != null && c.Dead ? "Your target is dead" : "You cannot attack that target"); return; }
            if (_player.Mounted)
            {
                if (_autoDismountSaid++ < 20 || _autoDismountSaid % 50 == 0) WowLog.Info("Mount: auto dismount to attack " + c.State.Name + " (offline; the client's autoDismount, default on)");
                Dismount(DismountReason.Spell, "You dismount before continuing.");            // ERR_MOUNT_FORCEDDISMOUNT (GlobalStrings.lua:3266), as MountedCastGate
                if (_player.Mounted) return;
            }
            if (_target != guid) SetTarget(guid);
            if (_autoAttack) return;
            _autoAttack = true;
            _outOfRangeWarned = false;
            if (_player.Sheath != SheathState.Melee) _player.Sheath = SheathState.Melee;
            BreakStealth();
            Emit(GameEventType.AttackStarted, PlayerGuid, guid, 0, 0, false, null);
        }

        public void StopAttack()
        {
            if (!_autoAttack) return;
            _autoAttack = false;
            _nextSwing = null;
            Emit(GameEventType.AttackStopped, PlayerGuid, _target, 0, 0, false, null);
        }

        public void SetSheath(SheathState state)
        {
            if (state == SheathState.Ranged && !_hasRanged) state = SheathState.Melee;
            _player.Sheath = state;
            if (state == SheathState.Sheathed) { StopAttack(); SetAutoShot(false); }
        }

        private void SetAutoShot(bool on)
        {
            if (_autoShot == on) return;
            _autoShot = on;
            _rangedWarned = false;
            if (on) { _player.Sheath = SheathState.Ranged; Emit(GameEventType.AttackStarted, PlayerGuid, _target, 0, 75, false, null); }
            else Emit(GameEventType.AttackStopped, PlayerGuid, _target, 0, 75, false, null);
        }

        // ------------------------------------------------------------------ player swings

        private void StepMelee(float dt)
        {
            if (_swingTimer > 0f) _swingTimer -= dt;
            if (_offTimer > 0f) _offTimer -= dt;
            if (!_autoAttack) return;
            var t = CreatureOf(_target);
            if (!IsAttackable(t)) { StopAttack(); return; }
            if (_casting != null || _me.Stunned) return;
            if (Dist(_me.Pos, t.Pos) > MeleeRange(_me, t))
            {
                if (!_outOfRangeWarned) { _outOfRangeWarned = true; EmitError("Target out of range"); }
                return;
            }
            _outOfRangeWarned = false;
            if (_hasOffhand && _form == 0 && _offTimer <= 0f)
            {
                // Off-hand: its own swing timer, 50% damage (WotLK off-hand penalty).
                float ohTime = _ohSpeed / _me.AuraMultiplier(SpellAura.ModMeleeHaste, 0);
                _offTimer += ohTime;
                if (_offTimer < ohTime * 0.5f) _offTimer = ohTime;
                float bonus = _ap / 14f * _ohSpeed;
                float pct = _me.AuraMultiplier(SpellAura.ModDamagePercentDone, 1) * 0.5f;
                WhiteHit(t, (_ohMin + bonus) * pct, (_ohMax + bonus) * pct, _ohSpeed, true);
                if (t.Dead) return;
            }
            if (_swingTimer > 0f) return;
            float mn, mx, time;
            MeleeDamageRange(false, out mn, out mx, out time);
            _swingTimer += time;
            if (_swingTimer < time * 0.5f) _swingTimer = time;
            _player.AttackCounter++;

            // An on-next-swing ability replaces the white hit when it is still affordable.
            var ns = _nextSwing;
            _nextSwing = null;
            if (ns != null)
            {
                string why = CheckCast(ns, t, false);
                if (why == null)
                {
                    PayCost(ns);
                    StartCooldown(ns);
                    ExecuteSpell(ns, t);
                    return;
                }
            }
            WhiteHit(t, mn, mx, time, false);   // rage uses the (hasted/form) attack time like GetAttackTime
        }

        private void StepRanged(float dt)
        {
            if (_rangedTimer > 0f) _rangedTimer -= dt;
            if (!_autoShot) return;
            var t = CreatureOf(_target);
            if (!IsAttackable(t) || !_hasRanged) { SetAutoShot(false); return; }
            if (_casting != null || _me.Stunned || _moving) return;   // 3.3.5a: no Auto Shot while moving
            float d = Dist(_me.Pos, t.Pos);
            var auto = _db.Find(75);
            float maxRange = (auto != null && auto.RangeMax > 0 ? auto.RangeMax : 35f) + t.CombatReach;
            if (d < MeleeRange(_me, t)) { if (!_rangedWarned) { _rangedWarned = true; EmitError("Target too close"); } return; }
            if (d > maxRange) { if (!_rangedWarned) { _rangedWarned = true; EmitError("Out of range"); } return; }
            _rangedWarned = false;
            if (_rangedTimer > 0f) return;
            float mn, mx, time;
            RangedDamageRange(false, t, out mn, out mx, out time);
            _rangedTimer = time;
            _player.AttackCounter++;
            var outcome = RollPlayerAttack(t, true, true);
            if (outcome == HitOutcome.Miss) { Emit(GameEventType.MeleeMiss, PlayerGuid, t.Guid, 0, 75, false, "Miss"); Engage(t); return; }
            float dmg = RandRange(mn, mx) * PhysicalMultiplier(t);
            bool crit = outcome == HitOutcome.Crit;
            if (crit) dmg *= 2f;
            DamageCreature(t, Math.Max(1, (int)dmg), 75, crit, GameEventType.MeleeHit, true);
        }

        private void WhiteHit(OfflineCreature t, float mn, float mx, float weaponSpeed, bool offHand)
        {
            var outcome = RollPlayerAttack(t, false, false);
            switch (outcome)
            {
                case HitOutcome.Evade: Emit(GameEventType.SpellMiss, PlayerGuid, t.Guid, 0, 0, false, "Evade"); return;
                case HitOutcome.Miss: Emit(GameEventType.MeleeMiss, PlayerGuid, t.Guid, 0, 0, false, "Miss"); Engage(t); return;
                case HitOutcome.Dodge: Emit(GameEventType.MeleeDodge, PlayerGuid, t.Guid, 0, 0, false, "Dodge"); Engage(t); return;
                case HitOutcome.Parry: Emit(GameEventType.MeleeParry, PlayerGuid, t.Guid, 0, 0, false, "Parry"); Engage(t); return;
            }
            float dmg = RandRange(mn, mx) * PhysicalMultiplier(t);
            bool crit = outcome == HitOutcome.Crit;
            if (crit) dmg *= 2f;
            if (outcome == HitOutcome.Glancing) dmg *= 1f - 0.1f * Math.Min(3, t.Level - _player.Level);
            int amount = Math.Max(1, (int)dmg);
            if (CurrentPower() == PowerType.Rage) AddPower(PowerType.Rage, WowFormulas.RageFromDealt(amount, _player.Level, weaponSpeed, crit, offHand));
            DamageCreature(t, amount, 0, crit, GameEventType.MeleeHit, true);
            OnPlayerMeleeHit(t, offHand);
        }

        /// <summary>Armor (with the player's armor penetration) and percent-damage-taken auras of the target.</summary>
        private float PhysicalMultiplier(OfflineCreature t)
        {
            float armor = t.Armor * (1f + t.AuraSum(SpellAura.ModResistancePct, 1) / 100f) + t.AuraSum(SpellAura.ModResistance, 1);
            float pen = _me.AuraSum(SpellAura.ArmorPenetrationPct, 0) / 100f;
            armor *= Math.Max(0f, 1f - pen);
            return WowFormulas.ArmorMultiplier(armor, _player.Level) * t.AuraMultiplier(SpellAura.ModDamagePercentTaken, 1);
        }

        /// <summary>
        /// The player's attack table against a creature (simplified from WotLK Unit::RollMeleeOutcomeAgainst): skill difference =
        /// 5 per level; miss 5% (+0.1% per skill point up to 10, +0.4% beyond; -0.04% below), dodge 5% +0.1%/pt, parry 5% +0.1%/pt
        /// from the front for creatures holding a weapon, glancing 10% + 1%/pt vs higher-level creatures (white hits only),
        /// crit = the player's crit - 0.04%/pt. Ranged: miss and crit only.
        /// </summary>
        private HitOutcome RollPlayerAttack(OfflineCreature t, bool special, bool ranged)
        {
            if (t.Ai == AiState.Evade) return HitOutcome.Evade;
            int skillDiff = 5 * (t.Level - _player.Level);
            float miss = skillDiff > 10 ? 6f + (skillDiff - 10) * 0.4f : skillDiff >= 0 ? 5f + skillDiff * 0.1f : 5f + skillDiff * 0.04f;
            if (!special && !ranged && _hasOffhand && _form == 0) miss += 19f;   // dual-wield white hits: 24% at equal level
            float dodge = ranged ? 0f : Math.Max(0f, 5f + skillDiff * 0.1f);
            if (t.Stunned) dodge = 0f;
            float parry = (!ranged && t.CanParry && !IsBehind(t, _me) && !t.Stunned) ? Math.Max(0f, 5f + skillDiff * 0.1f) : 0f;
            float glance = (!special && !ranged && t.Level > _player.Level) ? 10f + skillDiff : 0f;
            float crit = Math.Max(0f, _crit - skillDiff * 0.04f);
            float r = Rand01() * 100f;
            if ((r -= Math.Max(0f, miss)) < 0) return HitOutcome.Miss;
            if ((r -= dodge) < 0) return HitOutcome.Dodge;
            if ((r -= parry) < 0) return HitOutcome.Parry;
            if ((r -= glance) < 0) return HitOutcome.Glancing;
            if ((r -= crit) < 0) return HitOutcome.Crit;
            return HitOutcome.Hit;
        }

        /// <summary>A creature's table against the player: miss 5% -0.04%/pt, the player's dodge/parry -0.04%/pt, crit 5% +0.04%/pt.</summary>
        private HitOutcome RollCreatureAttack(OfflineCreature c)
        {
            int skillDiff = 5 * (c.Level - _player.Level);
            float miss = Math.Max(0f, 5f - skillDiff * 0.04f - _me.AuraSum(SpellAura.ModAttackerMeleeHitChance, 0));   // Night Elf Quickness: attackers -2% hit
            bool stunned = _me.Stunned;
            float dodge = stunned ? 0f : Math.Max(0f, _dodge - skillDiff * 0.04f);
            float parry = stunned || !_canParry || !_hasMelee ? 0f : Math.Max(0f, _parry - skillDiff * 0.04f);
            float crit = Math.Max(0f, 5f + skillDiff * 0.04f);
            float r = Rand01() * 100f;
            if ((r -= miss) < 0) return HitOutcome.Miss;
            if ((r -= dodge) < 0) return HitOutcome.Dodge;
            if ((r -= parry) < 0) return HitOutcome.Parry;
            if ((r -= crit) < 0) return HitOutcome.Crit;
            return HitOutcome.Hit;
        }

        private void CreatureSwing(OfflineCreature c)
        {
            c.State.AttackCounter++;
            var outcome = RollCreatureAttack(c);
            switch (outcome)
            {
                case HitOutcome.Miss: Emit(GameEventType.MeleeMiss, c.Guid, PlayerGuid, 0, 0, false, "Miss"); return;
                case HitOutcome.Dodge: Emit(GameEventType.MeleeDodge, c.Guid, PlayerGuid, 0, 0, false, "Dodge"); return;
                case HitOutcome.Parry: Emit(GameEventType.MeleeParry, c.Guid, PlayerGuid, 0, 0, false, "Parry"); return;
            }
            float ap = c.BaseAttackPower + c.AuraSum(SpellAura.ModAttackPower, 0);
            float mn = (c.DamageBase + ap / 14f) * c.AttackSpeed * c.DamageMod;
            float mx = (c.DamageBase * 1.5f + ap / 14f) * c.AttackSpeed * c.DamageMod;
            float dmg = RandRange(Math.Max(1f, mn), Math.Max(1f, mx));
            dmg *= WowFormulas.ArmorMultiplier(_armor, c.Level) * _me.AuraMultiplier(SpellAura.ModDamagePercentTaken, 1) * c.AuraMultiplier(SpellAura.ModDamagePercentDone, 1);
            bool crit = outcome == HitOutcome.Crit;
            if (crit) dmg *= 2f;
            int amount = Math.Max(1, (int)dmg);
            DamagePlayer(amount, c, 0, crit, GameEventType.MeleeHit);
            OnPlayerStruck(c);
            // Damage shields (Thorns) hit back.
            if (!c.Dead)
            {
                for (int a = 0; a < _me.Auras.Count; a++)
                {
                    var au = _me.Auras[a];
                    for (int i = 0; i < 3; i++)
                    {
                        var e = au.Spell.Effects[i];
                        if (e != null && e.IsAura && e.Aura == SpellAura.DamageShield && au.Amount[i] > 0)
                            DamageCreature(c, au.Amount[i], au.Spell.Id, false, GameEventType.SpellDamage, false);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ damage

        /// <summary>Damages a creature (already rolled/mitigated), emits the event, engages it, kills it at 0.</summary>
        private void DamageCreature(OfflineCreature c, int amount, int spellId, bool crit, GameEventType type, bool breaksCc, OfflineUnit source = null)
        {
            if (c == null || c.Dead || !c.Present) return;
            var src = source ?? _me;
            if (c.Summon != SummonKind.None) { DamageSummon(c, amount, src, spellId, crit, type); return; }
            if (c.Ai == AiState.Evade) { Emit(GameEventType.SpellMiss, src.Guid, c.Guid, 0, spellId, false, "Evade"); return; }
            amount = Absorb(c, amount);
            if (amount <= 0) return;
            c.Health -= amount;
            if (IsPlayerSide(src)) c.DamagedByPlayer = true;   // pet and totem kills count for the player (XP, loot, quests)
            c.State.WoundCounter++;
            Emit(type, src.Guid, c.Guid, amount, spellId, crit, null);
            if (breaksCc) BreakOnDamage(c);
            foreach (var au in c.Auras) au.DamageTaken += amount;
            CheckDamageBreak(c);
            if (c.Health <= 0) { KillCreature(c); return; }
            if (IsPlayerSide(src)) AddThreat(c, src, amount * ThreatMultiplier(src));
            Engage(c, src);
        }

        private int Absorb(OfflineUnit u, int amount)
        {
            for (int a = 0; a < u.Auras.Count && amount > 0; a++)
            {
                var au = u.Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e == null || !e.IsAura || e.Aura != SpellAura.SchoolAbsorb || au.Amount[i] <= 0) continue;
                    int take = Math.Min(amount, au.Amount[i]);
                    au.Amount[i] -= take;
                    amount -= take;
                    Emit(GameEventType.Absorb, 0, u.Guid, take, au.Spell.Id, false, "Absorb");
                    if (au.Amount[i] <= 0) au.Remaining = 0.001f;   // shield used up: removed on the next aura step
                }
            }
            return amount;
        }

        /// <summary>Auras with "damage breaks it" (Gouge, Sap, Entangling Roots...) end when the unit takes damage.</summary>
        private void BreakOnDamage(OfflineUnit u)
        {
            for (int a = u.Auras.Count - 1; a >= 0; a--)
            {
                var au = u.Auras[a];
                // AURA_INTERRUPT_FLAG_TAKE_DAMAGE = 0x2 (Gouge auraInt 2, Sap 0x480002, measured).
                if ((au.Spell.AuraInterruptFlags & 0x2) != 0 && au.Harmful) RemoveAuraAt(u, a);
            }
        }

        private void DamagePlayer(int amount, OfflineCreature source, int spellId, bool crit, GameEventType type)
        {
            if (_player.Dead) return;
            amount = Absorb(_me, amount);
            if (amount <= 0) return;
            _me.Health -= amount;
            _player.WoundCounter++;
            Emit(type, source != null ? source.Guid : 0, PlayerGuid, amount, spellId, crit, null);
            if (CurrentPower() == PowerType.Rage) AddPower(PowerType.Rage, WowFormulas.RageFromTaken(amount, _player.Level));
            CancelConsumable(null);
            BreakOnDamage(_me);
            if (_casting != null && !_me.HasAuraType(SpellAura.SchoolAbsorb))
            {
                // Iteration 7: Unit.cpp 1313-1317 - a cast with SPELL_INTERRUPT_FLAG_ABORT_ON_DMG (0x10: the mounts' 0x1F) is interrupted by
                // direct damage (not by DoTs); others keep the existing pushback.
                if ((_casting.InterruptFlags & SpellInterruptAbortOnDmg) != 0 && type != GameEventType.PeriodicDamage) { CancelCast("Interrupted"); EmitError("Interrupted"); }   // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066)
                else Pushback();
            }
            if (_me.Health <= 0) KillPlayer(source);
        }

        private void KillCreature(OfflineCreature c)
        {
            c.Health = 0;
            c.Dead = true;
            c.Engaged = false;
            c.Ai = AiState.Dead;
            c.HasMoveTarget = false;
            c.State.Moving = false;
            c.State.Running = false;
            if (c.Casting != null) InterruptUnitCast(c, "Died");
            ClearAuras(c);
            c.Threat.Clear();
            c.ForcedTimer = 0f;
            c.CorpseTimer = CorpseSeconds;
            c.RespawnTimer = Math.Max(5f, c.Spawn.RespawnSeconds);
            Emit(GameEventType.EntityDied, PlayerGuid, c.Guid, 0, 0, false, c.State.Name);
            if (_target == c.Guid) { if (_autoAttack) StopAttack(); if (_autoShot) SetAutoShot(false); }
            if (_comboTarget == c.Guid) { _combo = 0; _comboTarget = 0; }
            if (c.DamagedByPlayer && !c.GaveXp)
            {
                c.GaveXp = true;
                int xp = WowFormulas.KillXp(_player.Level, c.Level, c.Elite);
                if (xp > 0)
                {
                    GiveXp(xp, c.Guid, 0);
                    _victoriousUntil = _time + 20.0;   // "Victorious" (32216, 20 sec) enables Victory Rush
                }
                OnKillCredit(c.Tpl.Entry);
                GenerateLoot(c);
            }
        }

        private void KillPlayer(OfflineCreature killer)
        {
            _me.Health = 0;
            _player.Dead = true;
            _player.Ghost = false;
            // Iteration 7: Unit::RemoveAllAurasOnDeath (Unit.cpp 5389-5400) takes the mount away first - the aura clean-up below only
            // removes timed auras, and a ghost must not ride.
            if (_player.Mounted) Dismount(DismountReason.Death, null);
            StopAttack();
            SetAutoShot(false);
            CancelCast("You died");
            CancelConsumable(null);
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                var au = _me.Auras[a];
                if (!au.FromForm && !au.Spell.IsPassive && au.Duration > 0) RemoveAuraAt(_me, a);
            }
            EndChannel("You died");
            _statsDirty = true;
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (c.Present && !c.Dead && (c.Engaged || c.AssistDelay > 0)) StartEvade(c);
            }
            _inCombat = false;
            OnPlayerKilledGhostFlow();   // stage O2 (ghost): the 6-minute release timer + the reclaim delay escalation (OfflineSession.Ghost.cs)
            Emit(GameEventType.PlayerDied, killer != null ? killer.Guid : 0, PlayerGuid, 0, 0, false, "You have died.");
            WowLog.Info("OfflineSession: the player died" + (killer != null ? " to " + killer.State.Name + " (level " + killer.Level + ")" : ""));
        }

        /// <summary>
        /// Stage O2 (ghost): WoW's Release Spirit - the corpse stays where the player died and a ghost appears at the graveyard
        /// (OfflineSession.Ghost.cs ReleaseToGhost: AzerothCore's HandleRepopRequestOpcode / BuildPlayerRepop / RepopAtGraveyard).
        /// Coming back to life is ReclaimCorpse (at the corpse) or AcceptSpiritHealer; iterations 2-7 released straight back to life.
        /// </summary>
        public void ReleaseSpirit()
        {
            ReleaseToGhost("Release Spirit");
        }

        // ------------------------------------------------------------------ combo points

        private void AddCombo(OfflineCreature t, int n)
        {
            if (t == null) return;
            if (_comboTarget != t.Guid) { _combo = 0; _comboTarget = t.Guid; }
            _combo = Math.Min(5, _combo + n);
        }

        private int ComboOn(ulong guid) { return _comboTarget == guid ? _combo : 0; }
    }
}
