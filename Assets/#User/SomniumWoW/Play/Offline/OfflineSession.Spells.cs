using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        private SpellInfo _casting;
        private ulong _castTargetGuid;
        private float _castElapsed, _castTotal;
        private WowPos _castStartPos;
        private float _gcd, _gcdTotal;
        private readonly Dictionary<int, float> _cooldowns = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _catCooldowns = new Dictionary<int, float>();
        private readonly List<int> _cdKeys = new List<int>();
        private bool _statsDirty;
        private int _immersiveInstantNotes;     // stage S1: instants cast through CastSpellHeld (logged 5 times)

        /// <summary>A cast is cancelled when the avatar moves farther than this from where the cast began (yards, ~0.5 m).</summary>
        public const float CastMoveTolerance = 0.55f;

        // ------------------------------------------------------------------ IWowSession: casting

        public void CastSpell(int spellId, ulong targetGuid)
        {
            if (_player.Dead) { EmitError("You are dead"); return; }
            if (_player.OnTaxi) { CastFailed(spellId, "You are in flight"); return; }   // SPELL_FAILED_NOT_ON_TAXI (iteration 4)
            var s = _db.Find(spellId);
            if (s == null) { EmitError("Unknown spell"); return; }
            if (!_rec.Spells.Contains(spellId) && !KnowsMount(spellId)) { CastFailed(spellId, "You don't know that spell"); return; }   // iteration 7: the testing set of mounts counts as known
            // iteration 11 (spellfx, s7.1): a spell that needs a place / an item waits for it first - before the channel break, so pressing a
            // ground spell does not interrupt a running channel until the place is confirmed (WoW)
            if (GsNeedsAim(s)) return;
            if (_player.Targeting != null && _player.Targeting.SpellId != spellId) GsEndAim(s.Name + " pressed");   // WoW: another spell clears the one on the cursor
            if (_channel != null && _channel.Id != spellId && spellId != AutoAttackSpell) EndChannel("Interrupted");   // a new cast breaks the channel
            OfflineCreature explicitTarget = targetGuid != 0 && targetGuid != PlayerGuid ? CreatureOf(targetGuid) : null;
            if (explicitTarget != null && explicitTarget.Guid != _target) SetTarget(explicitTarget.Guid);
            if (MountCatalog.IsMount(spellId)) { CastMount(s); return; }   // iteration 7: mount spells (OfflineSession.Mounts.cs)

            if (spellId == AutoAttackSpell || s.HasEffect(SpellEffect.Attack) && s.Effects[0] != null && s.Effects[0].Effect == SpellEffect.Attack)
            {
                if (_autoAttack) StopAttack(); else StartAttack(_target);
                return;
            }
            if (s.AutoRepeat)
            {
                if (_autoShot) { SetAutoShot(false); return; }
                string why0 = CheckCast(s, CreatureOf(_target), true);
                if (why0 != null) { CastFailed(spellId, why0); return; }
                string mounted0 = MountedCastGate(s);                          // iteration 7: dismount first (ground) / refused (air)
                if (mounted0 != null) { CastFailed(spellId, mounted0); return; }
                SetAutoShot(true);
                return;
            }
            if (s.IsPassive) { CastFailed(spellId, "Passive ability"); return; }
            string unsupported = Unsupported(s);
            if (unsupported != null) { CastFailed(spellId, unsupported); return; }

            // Re-casting an active form (druid form, Stealth) cancels it; warrior stances stay.
            var formEff = s.AuraOf(SpellAura.ModShapeshift);
            if (formEff != null && formEff.MiscValue == _form && _form != 0)
            {
                if (s.SpellFamilyName == 4) return;
                LeaveForm();
                return;
            }
            // A queued on-next-swing ability is dequeued by pressing it again.
            if (s.OnNextSwing && _nextSwing != null && _nextSwing.Id == s.Id) { _nextSwing = null; return; }

            // iteration 11 (spellfx): a spell confirmed at a place is cast at the place, not at the selected creature (its death does not end the cast)
            OfflineUnit target = _gsConfirmedFor == spellId && _gsDestSpell == spellId ? (OfflineUnit)_me : ResolveTarget(s);
            string why = CheckCast(s, target, true);
            if (why != null) { CastFailed(spellId, why); return; }
            string mountedWhy = MountedCastGate(s);                             // iteration 7: dismount first (ground) / refused (air)
            if (mountedWhy != null) { CastFailed(spellId, mountedWhy); return; }

            if (s.OnNextSwing)
            {
                _nextSwing = s;
                var t = target as OfflineCreature;
                if (!_autoAttack && t != null) StartAttack(t.Guid);
                return;
            }
            if (s.CastTimeMs > 0)
            {
                _casting = s;
                _castHold = false;
                _castTargetGuid = target != null ? target.Guid : PlayerGuid;
                _castElapsed = 0f;
                _castTotal = s.CastTimeMs / 1000f / Math.Max(0.1f, _me.AuraMultiplier(SpellAura.MeleeSlow, 0));   // Berserking haste (iteration 3)
                _castStartPos = _me.Pos;
                StartGcd(s);
                BreakStealthFor(s);
                CancelConsumable(null);
                Emit(GameEventType.CastStarted, PlayerGuid, _castTargetGuid, s.CastTimeMs, s.Id, false, s.Name);
                return;
            }
            // Stage S1 (the user, 2026-09-27: "Instant cast spells can just trigger normally"): immersive casting no longer holds an instant -
            // it fires at once; only a spell with a cast time starts now and waits at its end (StepCasting -> HoldSpell) for the push.
            if (_holdRequest && _immersiveInstantNotes++ < 5) WowLog.Info("OfflineSession: " + s.Name + " is instant - fired at once (immersive casting holds only spells with a cast time; logged 5 times)");
            if (_holdRequest) FinishImmersive(s, target, true);   // stage S1 (PLAN10 s5): cast through Immersive casting - its damage bonus
            else FinishSpell(s, target, true);
        }

        public void CancelCast()
        {
            if (_casting != null) CancelCast("Interrupted");
            else if (_channel != null) EndChannel("Interrupted");
            else if (_held != null) DropHeld("Interrupted");
            else if (_nextSwing != null) _nextSwing = null;
        }

        private void CancelCast(string reason)
        {
            if (_casting == null) return;
            int id = _casting.Id;
            _casting = null;
            _castHold = false;
            _castElapsed = 0f;
            _castTotal = 0f;
            Emit(GameEventType.CastFailed, PlayerGuid, _castTargetGuid, 0, id, false, reason);
        }

        private void CastFailed(int spellId, string reason)
        {
            Emit(GameEventType.CastFailed, PlayerGuid, _target, 0, spellId, false, reason);
            EmitError(reason);
        }

        private void Pushback()
        {
            if (_casting == null) return;
            _castElapsed = Math.Max(0f, _castElapsed - 0.5f);
        }

        private OfflineUnit ResolveTarget(SpellInfo s) { return ResolveTargetFor(s, CreatureOf(_target)); }

        private static bool HasEnemyTargetEffect(SpellInfo s)
        {
            for (int i = 0; i < 3; i++) { var e = s.Effects[i]; if (e != null && (e.TargetA == SpellTarget.TargetEnemy || e.TargetA == SpellTarget.TargetAny)) return true; }
            return false;
        }

        private void StepCasting(float dt)
        {
            // Stage S1 (PLAN10 rule 6, as the realm's Dreamport hold does online): a held spell is a cast in progress - walking away from
            // where its cast began interrupts it like a cast (the push is a hand movement, the avatar stays).
            if (_held != null && _casting == null && Dist(_me.Pos, _castStartPos) > CastMoveTolerance) { DropHeld("Interrupted"); EmitError("Interrupted"); return; }
            if (_casting == null) return;
            if (_me.Stunned) { CancelCast("Interrupted"); return; }
            if (Dist(_me.Pos, _castStartPos) > CastMoveTolerance) { CancelCast("Interrupted"); EmitError("Interrupted"); return; }
            var s = _casting;
            OfflineUnit target = _castTargetGuid == PlayerGuid ? (OfflineUnit)_me : CreatureOf(_castTargetGuid);
            if (target == null || target.Dead) { CancelCast("Invalid target"); return; }
            _castElapsed += dt;
            if (_castElapsed < _castTotal) return;
            _casting = null;
            if (MountCatalog.IsMount(s.Id)) { FinishMountCast(s); return; }   // iteration 7: the mount lands (OfflineSession.Mounts.cs)
            string why = CheckCast(s, target, false);
            if (why != null) { CastFailed(s.Id, why); return; }
            if (_castHold) { HoldSpell(s, target, false); return; }   // iteration 4: immersive casting - wait for ReleaseHeldSpell
            FinishSpell(s, target, false);
        }

        private void FinishSpell(SpellInfo s, OfflineUnit target, bool startGcd)
        {
            PayCost(s);
            StartCooldown(s);
            if (startGcd) StartGcd(s);
            BreakStealthFor(s);
            // Iteration 3: CastSucceeded first (views start the missile / release animation), then the effects - missiles (Spell.dbc Speed)
            // land after their flight time (LaunchOrHit), channels start their ticking.
            // Iteration 11 (spellfx, s7.2): with every unit the areas will hit (a pre-pass) and the place of a ground spell.
            WowPos place;
            bool atDest = GsAimedAtPlace(s, out place);
            GsEmitSucceeded(s, target != null ? target.Guid : 0, GsPreviewHits(s), atDest, place);
            if (target != null && target != _me && s.Speed > 0.5f && !s.IsChanneled) LaunchOrHit(s, _me, target, 0);
            else ExecuteSpell(s, target ?? _me);
            if (s.IsChanneled && !_player.Dead) StartChannel(s, target);
            if (_gsDestSpell == s.Id) _gsDestSpell = 0;          // the place is used (a channel copied it in StartChannel)
        }

        // ------------------------------------------------------------------ checks

        /// <summary>Why a spell cannot be cast right now (null = castable). checkTimers false skips casting/GCD checks (finishing a cast).</summary>
        private string CheckCast(SpellInfo s, OfflineUnit target, bool checkTimers)
        {
            // Order: lasting reasons first (form, weapon, combat, conditions, target), then power, then timers - the UI shows the first.
            if (_player.Dead) return "You are dead";
            if (_player.OnTaxi) return "You are in flight";
            if (_me.Stunned && (s.Attributes[5] & 0x8u) == 0) return "Can't do that while stunned";          // SPELL_ATTR5_USABLE_WHILE_STUNNED (Every Man for Himself)
            if (_me.Feared && (s.Attributes[5] & 0x20000u) == 0 && !GrantsImmunity(s, 5)) return "Can't do that while feared";          // SPELL_ATTR5_USABLE_WHILE_FEARED (Will of the Forsaken)
            if (_me.Confused && (s.Attributes[5] & 0x40000u) == 0) return "Can't do that while confused";
            if (_me.Silenced && s.PreventionType == 1 && s.DmgClass == 1) return "Can't do that while silenced";
            if (IsJudgement(s) && ActiveSeal() == null) return "You must have an active Seal";
            if (s.Id == CannibalizeSpell && FindCannibalCorpse() == null) return "Requires a humanoid or undead corpse nearby";
            string shape = CheckShapeshift(s);
            if (shape != null) return shape;
            string equip = CheckEquipment(s);
            if (equip != null) return equip;
            if (s.CantUseInCombat && _inCombat) return "You can't do that while in combat";
            if (s.CasterAuraState != 0 && !CasterAuraState(s.CasterAuraState)) return s.CasterAuraState == 10 ? "You must kill an enemy that yields experience first" : "Can't do that yet";

            // Iteration 4: a helpful spell aimed at a friendly NPC / pet / totem (TARGET_UNIT_TARGET_ALLY/ANY...) - alive and in range.
            bool onFriend = HelpfulOnFriend(s, target);
            if (onFriend)
            {
                var ft = (OfflineCreature)target;
                float fmin, fmax;
                RangeOf(s, ft, out fmin, out fmax);
                if (Dist(_me.Pos, ft.Pos) > fmax) return "Out of range";
            }
            bool harmful = !onFriend && (s.IsHarmful || HasEnemyTargetEffect(s));
            if (harmful && s.NeedsUnitTarget)
            {
                var t = target as OfflineCreature;
                if (t == null) return "You have no target";
                if (t.Dead) return "Your target is dead";
                if (!IsAttackable(t)) return "Invalid target";
                if (s.TargetAuraState != 0 && !TargetAuraState(s.TargetAuraState, t)) return "Can't do that yet";
                if (s.MaxTargetLevel > 0 && t.Level > s.MaxTargetLevel) return "Target is too high level";
                if (s.TargetCreatureType != 0 && t.CreatureType > 0 && (s.TargetCreatureType & (1 << (t.CreatureType - 1))) == 0) return "Invalid target";
                float d = Dist(_me.Pos, t.Pos);
                float max, min;
                RangeOf(s, t, out min, out max);
                if (d > max) return "Out of range";
                if (min > 0f && d < min) return "Target too close";
                if ((s.Attributes[2] & 0x100000u) != 0 && s.SpellFamilyName == 8 && !IsBehind(t, _me)) return "You must be behind your target";   // SPELL_ATTR2 bit 20 (Backstab, but also Judgements - iteration 3: rogue family only; the server's behind list is Backstab/Ambush/Garrote)
            }
            PowerType pt;
            int cost = CostOf(s, out pt);
            if (cost > 0)
            {
                if (pt == PowerType.None) { if (_me.Health <= cost) return "Not enough health"; }
                else if (PowerOf(pt) < cost) return pt == PowerType.Rage ? "Not enough rage" : pt == PowerType.Energy ? "Not enough energy" : "Not enough mana";
            }
            if (checkTimers)
            {
                if (_casting != null) return "Another action is in progress";
                if (_held != null) return "Another action is in progress";
                if (_channel != null && _channel.Id == s.Id) return "Another action is in progress";
                if (_gcd > 0f && s.StartRecoveryTimeMs > 0) return "Spell is not ready yet";
                if (CooldownLeft(s) > 0f) return "Spell is not ready yet";
            }
            return null;
        }
        private void RangeOf(SpellInfo s, OfflineUnit t, out float min, out float max)
        {
            if ((s.RangeFlags & 1) != 0 || (s.RangeMax > 0f && s.RangeMax <= 5f)) { max = MeleeRange(_me, t); min = 0f; return; }
            max = s.RangeMax + t.CombatReach;
            min = s.RangeMin;
            if ((s.RangeFlags & 2) != 0) min = Math.Max(min, MeleeRange(_me, t));   // ranged shots need distance (5 yd dead zone)
            if (s.RangeMax <= 0f) max = MeleeRange(_me, t);
        }

        /// <summary>SpellInfo::CheckShapeshift (TrinityCore 3.3.5 logic, form data from SpellShapeshiftForm.dbc).</summary>
        private string CheckShapeshift(SpellInfo s)
        {
            uint mask = _form != 0 ? 1u << (_form - 1) : 0u;
            if ((mask & s.StancesNot) != 0) return "Can't do that in this form";
            if ((mask & s.Stances) != 0) return null;
            bool actAsShifted = false;
            if (_form != 0) { var f = _db.Form(_form); actAsShifted = f != null && f.ActsAsShifted; }
            if (actAsShifted)
            {
                if (s.NotShapeshift) return "Can't do that while shapeshifted";
                if (s.Stances != 0) return "Must be in the right form";
            }
            else if (!s.NotNeedShapeshift && s.Stances != 0)
            {
                if ((s.Stances & (1u << 29)) != 0) return "You must be in Stealth";          // form 30 Stealth
                if ((s.Stances & ((1u << 16) | (1u << 17) | (1u << 18))) != 0) return "Wrong stance";   // warrior stances 17/18/19
                return "Must be in the right form";
            }
            return null;
        }

        private bool CasterAuraState(int state)
        {
            switch (state)
            {
                case 10: return _time < _victoriousUntil;                               // AURA_STATE_WARRIOR_VICTORY_RUSH
                case 5: return ActiveSeal() != null;                                      // AURA_STATE_JUDGEMENT: a Seal is active (iteration 3)
                case 2: return _me.Health * 5 < _me.MaxHealth;                          // health below 20%
                case 13: return _me.Health * 100 < _me.MaxHealth * 35;                  // below 35%
                case 23: return _me.Health * 4 > _me.MaxHealth * 3;                     // above 75%
            }
            return false;
        }

        private static bool TargetAuraState(int state, OfflineCreature t)
        {
            switch (state)
            {
                case 2: return t.Health * 5 < t.MaxHealth;
                case 13: return t.Health * 100 < t.MaxHealth * 35;
                case 23: return t.Health * 4 > t.MaxHealth * 3;
                case 18:                                                                  // AURA_STATE_BLEEDING
                    for (int a = 0; a < t.Auras.Count; a++) for (int i = 0; i < 3; i++) { var e = t.Auras[a].Spell.Effects[i]; if (e != null && e.Mechanic == 15) return true; }
                    return false;
            }
            return false;
        }

        /// <summary>Equipped-item requirements (Spell.dbc EquippedItemClass/SubClassMask): weapons only.</summary>
        private string CheckEquipment(SpellInfo s)
        {
            if (s.EquippedItemClass != 2 || s.EquippedItemSubClassMask == 0) return null;
            const int rangedMask = (1 << 2) | (1 << 3) | (1 << 16) | (1 << 18) | (1 << 19);
            bool rangedSpell = (s.EquippedItemSubClassMask & ~rangedMask) == 0;
            if (rangedSpell)
            {
                if (!_hasRanged) return "Requires a ranged weapon";
                if (_rgSubclass >= 0 && (s.EquippedItemSubClassMask & (1 << _rgSubclass)) == 0) return "Requires a different ranged weapon";
                return null;
            }
            if (!_hasMelee) return s.EquippedItemSubClassMask == (1 << 15) ? "Requires a dagger" : "Requires a melee weapon";
            if (_mhSubclass >= 0 && (s.EquippedItemSubClassMask & (1 << _mhSubclass)) == 0) return s.EquippedItemSubClassMask == (1 << 15) ? "Requires a dagger" : "Requires a different weapon";
            return null;
        }

        /// <summary>Spells whose every effect needs something this offline world does not have. null = castable.</summary>
        private static string Unsupported(SpellInfo s)
        {
            if (s.HasAura(SpellAura.TrackCreatures) || s.HasAura(45)) return "Tracking needs the minimap (not simulated offline)";
            string reason = null;
            bool anySupported = false;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                string r = UnsupportedEffect(e);
                if (r == null) anySupported = true;
                else if (reason == null) reason = r;
            }
            return anySupported ? null : (reason ?? "Not available offline");
        }

        private static string UnsupportedEffect(SpellEffectInfo e)
        {
            switch (e.Effect)
            {
                case SpellEffect.TeleportUnits: return MotionTeleportSupported(e) ? null : "Teleports are not part of the offline zone";   // iteration 11 (motion 17a): TargetB 65 DEST_TARGET_BACK (Shadowstep, Killing Spree)
                case SpellEffect.PersistentAreaAura: return null;   // iteration 11 (spellfx): ground areas (Consecration, Flamestrike's burn ...)
                case SpellEffect.ResurrectNew: return "There are no other players to resurrect offline";
                case SpellEffect.TameCreature: case SpellEffect.DismissPet: case SpellEffect.FeedPet: case SpellEffect.ResurrectPet:
                    return "Hunter pets are not simulated offline yet";
                case SpellEffect.SummonPet: case SpellEffect.Summon: case SpellEffect.CreateItem: case SpellEffect.EnchantItemTemporary:
                case SpellEffect.Dispel: case SpellEffect.DispelMechanic: case SpellEffect.HealMaxHealth:
                    return null;   // iteration 3: Warlock pets, totems, conjured items, weapon imbues, dispels, Lay on Hands
                case SpellEffect.OpenLock: return "Nothing to unlock offline";
                case SpellEffect.Pickpocket: return "Pick Pocket is not simulated offline";
                case SpellEffect.ApplyAura: case SpellEffect.ApplyAreaAuraParty: case SpellEffect.ApplyAreaAuraRaid: case SpellEffect.ApplyAreaAuraFriend:
                    if (e.Aura == SpellAura.TrackCreatures || e.Aura == 45) return "Tracking needs the minimap (not simulated offline)";
                    if (e.Aura == SpellAura.PeriodicTriggerSpell && e.TriggerSpell == 0) return "Not available offline";
                    return null;
                case SpellEffect.SchoolDamage: case SpellEffect.Heal: case SpellEffect.WeaponDamage: case SpellEffect.WeaponDamageNoSchool:
                case SpellEffect.NormalizedWeaponDmg: case SpellEffect.WeaponPercentDamage: case SpellEffect.Energize: case SpellEffect.AddComboPoints:
                case SpellEffect.Charge: case SpellEffect.TriggerSpell: case SpellEffect.AttackMe: case SpellEffect.HealPct: case SpellEffect.EnergizePct:
                case SpellEffect.Attack: case SpellEffect.Threat: case SpellEffect.InterruptCast: case SpellEffect.Dummy: case SpellEffect.ScriptEffect:
                    return null;
                case 29: case 138: case 42:
                    return null;   // LEAP (Blink), LEAP_BACK (Disengage), JUMP_DEST (Feral Charge - Cat): iteration 11 motion (17a)
            }
            return "Not available offline";
        }

        // ------------------------------------------------------------------ costs and cooldowns

        /// <summary>Cost in the unit shown to the player (rage points, energy, mana, health for PowerType -2).</summary>
        private int CostOf(SpellInfo s, out PowerType pt)
        {
            switch (s.PowerType)
            {
                case -2: pt = PowerType.None; return s.ManaCostPercentage > 0 ? _baseHealth * s.ManaCostPercentage / 100 : s.ManaCost;   // health
                case 1: pt = PowerType.Rage; return s.ManaCost / 10;                        // rage is stored in tenths
                case 3: pt = PowerType.Energy; return s.ManaCost;
                case 0:
                    pt = PowerType.Mana;
                    if (s.ManaCostPercentage > 0) return _baseMana * s.ManaCostPercentage / 100;   // % of base mana (3.3.5a)
                    return s.ManaCost;
            }
            pt = PowerType.None;
            return 0;
        }

        private void PayCost(SpellInfo s)
        {
            PowerType pt;
            int cost = CostOf(s, out pt);
            if (cost <= 0) return;
            if (pt == PowerType.None) { _me.Health = Math.Max(1, _me.Health - cost); return; }
            AddPower(pt, -cost);
            if (pt == PowerType.Mana) _lastManaSpend = _time;
        }

        private void StartGcd(SpellInfo s)
        {
            if (s.StartRecoveryTimeMs <= 0) return;
            _gcdTotal = s.StartRecoveryTimeMs / 1000f;
            _gcd = _gcdTotal;
        }

        private void StartCooldown(SpellInfo s)
        {
            if (s.RecoveryTimeMs > 0) _cooldowns[s.Id] = s.RecoveryTimeMs / 1000f;
            if (s.CategoryRecoveryTimeMs > 0)
            {
                if (s.Category > 0) _catCooldowns[s.Category] = s.CategoryRecoveryTimeMs / 1000f;
                else _cooldowns[s.Id] = Math.Max(s.CategoryRecoveryTimeMs, s.RecoveryTimeMs) / 1000f;
            }
        }

        private float CooldownLeft(SpellInfo s)
        {
            float cd = 0f, v;
            if (_cooldowns.TryGetValue(s.Id, out v)) cd = v;
            if (s.Category > 0 && _catCooldowns.TryGetValue(s.Category, out v) && v > cd) cd = v;
            return cd;
        }

        private void StepCooldowns(float dt)
        {
            if (_gcd > 0f) _gcd = Math.Max(0f, _gcd - dt);
            TickDict(_cooldowns, dt);
            TickDict(_catCooldowns, dt);
        }

        private void TickDict(Dictionary<int, float> d, float dt)
        {
            if (d.Count == 0) return;
            _cdKeys.Clear();
            foreach (var k in d.Keys) _cdKeys.Add(k);
            foreach (var k in _cdKeys)
            {
                float v = d[k] - dt;
                if (v <= 0f) d.Remove(k); else d[k] = v;
            }
        }

        // ------------------------------------------------------------------ effects

        /// <summary>Effect value like SpellEffectInfo::CalcValue: base + level scaling + roll in 1..DieSides.</summary>
        private int CalcValue(SpellInfo s, SpellEffectInfo e, int casterLevel)
        {
            int bp = e.BasePoints;
            int level = casterLevel;
            if (s.MaxLevel > 0 && level > s.MaxLevel) level = s.MaxLevel;
            else if (level < s.BaseLevel) level = s.BaseLevel;
            level -= s.SpellLevel;
            bp += (int)(level * e.PointsPerLevel);
            int die = e.DieSides;
            if (die == 1) bp += 1;
            else if (die > 1) bp += _rng.Next(1, die + 1);
            else if (die < 0) bp += _rng.Next(die, 2);
            return bp;
        }

        private void ExecuteSpell(SpellInfo s, OfflineUnit target)
        {
            var mainTarget = target as OfflineCreature;
            // Iteration 4: helpful spells cast on a targeted friendly unit land on it (ally-target effects), not on the caster.
            bool onFriend = HelpfulOnFriend(s, target);
            bool harmful = !onFriend && (s.IsHarmful || HasEnemyTargetEffect(s));
            int combo = mainTarget != null ? ComboOn(mainTarget.Guid) : 0;
            if ((s.Attributes[0] & 0x100000u) != 0) StopAttack();   // SPELL_ATTR0_STOP_ATTACK_TARGET (Gouge)

            // Single-target hit roll (area effects roll per target below).
            HitOutcome outcome = HitOutcome.Hit;
            if (harmful && mainTarget != null && s.NeedsUnitTarget)
            {
                outcome = RollSpell(s, mainTarget);
                if (outcome == HitOutcome.Miss || outcome == HitOutcome.Dodge || outcome == HitOutcome.Parry || outcome == HitOutcome.Evade)
                {
                    Emit(GameEventType.SpellMiss, PlayerGuid, mainTarget.Guid, 0, s.Id, false, outcome == HitOutcome.Miss ? (s.DmgClass == 1 ? "Resist" : "Miss") : outcome.ToString());
                    if (outcome != HitOutcome.Evade) Engage(mainTarget);
                    return;
                }
            }

            // Weapon damage effects combine into one hit.
            bool weaponHit = false, normalized = false, rangedWeapon = s.DmgClass == 3;
            float weaponBonus = 0f, weaponPct = 1f;
            var auraDone = new List<OfflineUnit>(4);
            var areaTargets = new List<OfflineCreature>();
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                switch (e.Effect)
                {
                    case SpellEffect.WeaponDamage: case SpellEffect.WeaponDamageNoSchool:
                        weaponHit = true; weaponBonus += CalcValue(s, e, _player.Level); break;
                    case SpellEffect.NormalizedWeaponDmg:
                        weaponHit = true; normalized = true; weaponBonus += CalcValue(s, e, _player.Level); break;
                    case SpellEffect.WeaponPercentDamage:
                        weaponHit = true; weaponPct *= CalcValue(s, e, _player.Level) / 100f; break;
                }
            }

            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                if (e.Effect == SpellEffect.PersistentAreaAura) { GsCreateArea(s, e); continue; }   // iteration 11 (spellfx): a ground area, never per target
                if (e.IsAreaEnemy)
                {
                    areaTargets.Clear();
                    float radius = e.Radius > 0 ? e.Radius : 8f;
                    WowPos centre = GsAreaCentre(s, e);                  // iteration 11 (spellfx): the place for DEST areas, the caster otherwise
                    for (int k = 0; k < _creatures.Count; k++)
                    {
                        var c = _creatures[k];
                        if (IsAttackable(c) && Dist(centre, c.Pos) <= radius + c.CombatReach) areaTargets.Add(c);
                    }
                    foreach (var c in areaTargets)
                    {
                        var o = RollSpell(s, c);
                        if (o == HitOutcome.Miss || o == HitOutcome.Dodge || o == HitOutcome.Parry || o == HitOutcome.Evade)
                        {
                            if (i == FirstEffectIndex(s)) Emit(GameEventType.SpellMiss, PlayerGuid, c.Guid, 0, s.Id, false, o == HitOutcome.Miss ? "Miss" : o.ToString());
                            continue;
                        }
                        ApplyEffect(s, e, c, o == HitOutcome.Crit, 0, auraDone);
                        Engage(c);
                    }
                    continue;
                }
                bool onEnemy = e.TargetA == SpellTarget.TargetEnemy || (e.TargetA == SpellTarget.TargetAny || e.TargetA == 77) && mainTarget != null;   // 77 TARGET_UNIT_CHANNEL_TARGET (Arcane Missile)
                if (onEnemy)
                {
                    if (mainTarget == null || mainTarget.Dead) continue;
                    if (e.Effect == SpellEffect.WeaponDamage || e.Effect == SpellEffect.WeaponDamageNoSchool || e.Effect == SpellEffect.NormalizedWeaponDmg || e.Effect == SpellEffect.WeaponPercentDamage) continue;
                    ApplyEffect(s, e, mainTarget, outcome == HitOutcome.Crit, combo, auraDone);
                }
                else
                {
                    bool allyEffect = e.TargetA == SpellTarget.TargetAlly || e.TargetA == SpellTarget.TargetParty || e.TargetA == SpellTarget.TargetRaid;
                    ApplyEffect(s, e, onFriend && allyEffect ? target : _me, false, combo, auraDone);
                }
            }

            if (weaponHit && mainTarget != null && !mainTarget.Dead)
            {
                float mn, mx, time;
                if (rangedWeapon) RangedDamageRange(normalized, mainTarget, out mn, out mx, out time);
                else MeleeDamageRange(normalized, out mn, out mx, out time);
                float dmg = (RandRange(mn, mx) + weaponBonus) * weaponPct * PhysicalMultiplier(mainTarget);
                bool crit = outcome == HitOutcome.Crit;
                if (crit) dmg *= 2f;
                if (ImmersiveDamage(s)) dmg *= 1f + ImmersiveMode.ImmersiveDamageBonus;   // stage S1 (PLAN10 s5)
                DamageCreature(mainTarget, Math.Max(1, (int)dmg), s.Id, crit, GameEventType.MeleeHit, true);
            }

            if (s.IsFinisher && mainTarget != null && combo > 0) { _combo = 0; _comboTarget = 0; }
            if (harmful && mainTarget != null && !mainTarget.Dead) Engage(mainTarget);
        }

        private static int FirstEffectIndex(SpellInfo s) { for (int i = 0; i < 3; i++) if (s.Effects[i] != null) return i; return 0; }

        /// <summary>Hit roll for a spell: melee/ranged abilities use the attack table (special), magic uses the WotLK spell hit table.</summary>
        private HitOutcome RollSpell(SpellInfo s, OfflineCreature t)
        {
            if (t.Ai == AiState.Evade) return HitOutcome.Evade;
            if (s.DmgClass == 2) return RollPlayerAttack(t, true, false);
            if (s.DmgClass == 3) return RollPlayerAttack(t, true, true);
            if (s.DmgClass == 0) return HitOutcome.Hit;
            // Magic: 4% at equal level, 5/6% at +1/+2, 17% at +3, +11% per level beyond; lower-level targets 3/2/1% (min 1%).
            int diff = t.Level - _player.Level;
            float miss = diff <= 2 ? Math.Max(1f, 4f + diff) : 17f + (diff - 3) * 11f;
            float r = Rand01() * 100f;
            if (r < miss) return HitOutcome.Miss;
            if (Rand01() * 100f < _spellCrit && (HasDirectEffect(s))) return HitOutcome.Crit;
            return HitOutcome.Hit;
        }

        private static bool HasDirectEffect(SpellInfo s)
        {
            return s.HasEffect(SpellEffect.SchoolDamage) || s.HasEffect(SpellEffect.Heal);
        }

        private void ApplyEffect(SpellInfo s, SpellEffectInfo e, OfflineUnit u, bool crit, int combo, List<OfflineUnit> auraDone)
        {
            var c = u as OfflineCreature;
            switch (e.Effect)
            {
                case SpellEffect.SchoolDamage:
                {
                    if (c == null) return;
                    float v = CalcValue(s, e, _player.Level) + e.PointsPerComboPoint * combo + ScriptDamageBonus(s, combo);
                    if (s.SchoolMask == 1) v *= PhysicalMultiplier(c);              // physical school: armor
                    if (crit) v *= s.DmgClass == 2 || s.DmgClass == 3 ? 2f : 1.5f;
                    if (ImmersiveDamage(s)) v *= 1f + ImmersiveMode.ImmersiveDamageBonus;   // stage S1 (PLAN10 s5): released with Immersive casting
                    DamageCreature(c, Math.Max(1, (int)v), s.Id, crit, GameEventType.SpellDamage, true);
                    return;
                }
                case SpellEffect.Heal:
                {
                    int v = CalcValue(s, e, _player.Level);
                    bool hc = Rand01() * 100f < _spellCrit;
                    if (hc) v = v * 3 / 2;
                    Heal(u, v, s.Id, hc, GameEventType.SpellHeal);
                    return;
                }
                case SpellEffect.HealPct:
                    Heal(u, u.MaxHealth * CalcValue(s, e, _player.Level) / 100, s.Id, false, GameEventType.SpellHeal);
                    return;
                case SpellEffect.Energize:
                    if (u == _me) AddPower((PowerType)e.MiscValue, e.MiscValue == 1 ? CalcValue(s, e, _player.Level) / 10f : CalcValue(s, e, _player.Level));
                    return;
                case SpellEffect.EnergizePct:
                    if (u == _me)
                    {
                        var pt = (PowerType)e.MiscValue;
                        float max = pt == PowerType.Mana ? _maxMana : 100f;
                        AddPower(pt, max * CalcValue(s, e, _player.Level) / 100f);
                    }
                    return;
                case SpellEffect.AddComboPoints:
                    if (c != null) AddCombo(c, Math.Max(1, CalcValue(s, e, _player.Level)));
                    return;
                case SpellEffect.Dummy:
                    // Charge: the server script grants the rage in the DUMMY effect's value (rage tenths): "generate $/10;s2 rage".
                    if (s.HasEffect(SpellEffect.Charge) && s.SpellFamilyName == 4) AddPower(PowerType.Rage, CalcValue(s, e, _player.Level) / 10f);
                    // Life Tap (family 5, DUMMY on the caster, spell_warl_life_tap): converts the effect's value of health into as much mana.
                    else if (s.Id == CannibalizeSpell) StartCannibalize();
                    else if (s.SpellFamilyName == 5 && e.TargetA == SpellTarget.UnitCaster && s.PowerType == -2 && u == _me)
                    {
                        int v = Math.Max(1, CalcValue(s, e, _player.Level));
                        if (_me.Health > v) { _me.Health -= v; _player.WoundCounter++; Emit(GameEventType.SpellDamage, PlayerGuid, PlayerGuid, v, s.Id, false, null); AddPower(PowerType.Mana, v); }
                    }
                    return;
                case SpellEffect.ScriptEffect:
                    if (IsJudgement(s)) DoJudgement(s, c ?? CreatureOf(_target));
                    else if (s.Id == EscapeArtist) RemoveLossOfControl(false);
                    return;
                case SpellEffect.SummonPet:
                    if (!SummonPet(s, e.MiscValue)) EmitError("That pet cannot be summoned offline");
                    return;
                case SpellEffect.Summon:
                    if (!SummonTotem(s, e)) EmitError("Only totems can be summoned offline");
                    return;
                case SpellEffect.CreateItem:
                    if (u == _me) CreateItem(s, e);
                    return;
                case SpellEffect.EnchantItemTemporary:
                    ApplyImbue(s, e);
                    return;
                case SpellEffect.Dispel:
                    DispelType(u, e.MiscValue, u == _me || !IsHostilePair(_me, u));
                    return;
                case SpellEffect.DispelMechanic:
                    DispelMechanic(u, e.MiscValue);
                    return;
                case SpellEffect.HealMaxHealth:
                    Heal(u, _me.MaxHealth, s.Id, false, GameEventType.SpellHeal);   // Lay on Hands: the Paladin's maximum health
                    return;
                case SpellEffect.Charge:
                    // Offline the avatar is moved by its player: Charge only needs the 8-25 yd range (checked) and applies its effects;
                    // iteration 11 (motion 17b): the forced move to the creature (OfflineSession.Motion.cs).
                    if (c != null) { Engage(c); OfflineChargeMove(c, s); }
                    return;
                case SpellEffect.TriggerSpell:
                {
                    var trig = _db.Find(e.TriggerSpell);
                    if (trig == null) return;
                    OfflineUnit tt = HasEnemyTargetEffect(trig) || trig.IsHarmful ? (OfflineUnit)(c ?? CreatureOf(_target)) : _me;
                    if (tt != null) ExecuteTriggered(trig, tt);
                    return;
                }
                case SpellEffect.Attack:
                    if (c != null) StartAttack(c.Guid);
                    return;
                case SpellEffect.AttackMe: case SpellEffect.Threat:
                    if (c != null) Engage(c);
                    return;
                case SpellEffect.ApplyAura: case SpellEffect.ApplyAreaAuraParty: case SpellEffect.ApplyAreaAuraRaid: case SpellEffect.ApplyAreaAuraFriend:
                    // All aura effects of the spell that land on this unit are applied together (once per unit per cast).
                    if (auraDone.Contains(u)) return;
                    auraDone.Add(u);
                    ApplySpellAuras(s, _me, u, combo);
                    return;
                // iteration 11 (motion 17b): the movement effects (bodies in OfflineSession.Motion.cs)
                case SpellEffect.TeleportUnits:
                    if (c != null && e.TargetB == 65) OfflineTeleportBehind(c);
                    return;
                case 29: OfflineLeap(s, e); return;
                case 138: OfflineLeapBack(s, e); return;
                case 42: if (c != null) OfflineJumpDest(c, s, e); return;
            }
        }

        /// <summary>A triggered spell (Charge Stun, Sunder Armor's debuff, Bloodrage's rage over time): no cost, no checks.</summary>
        private void ExecuteTriggered(SpellInfo s, OfflineUnit target)
        {
            var auraDone = new List<OfflineUnit>(2);
            var c = target as OfflineCreature;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                OfflineUnit u = e.TargetA == SpellTarget.UnitCaster ? (OfflineUnit)_me : target;
                ApplyEffect(s, e, u, false, 0, auraDone);
            }
            if (c != null && (s.IsHarmful || HasEnemyTargetEffect(s))) Engage(c);
        }

        /// <summary>
        /// Attack-power parts the 3.3.5a server adds in its spell scripts (TrinityCore/AzerothCore SpellEffects; matched by
        /// SpellFamilyName + SpellFamilyFlags read from Spell.dbc, see the report): Thunder Clap +12% AP, Victory Rush AP * value%,
        /// Eviscerate + AP * combo * 3..7%, Arcane Shot + 15% RAP.
        /// </summary>
        private float ScriptDamageBonus(SpellInfo s, int combo)
        {
            var f = s.SpellFamilyFlags;
            switch (s.SpellFamilyName)
            {
                case 4:
                    if ((f[0] & 0x80u) != 0) return _ap * 0.12f;                                    // Thunder Clap
                    if ((f[1] & 0x100u) != 0)                                                        // Victory Rush: damage = AP * bp%
                    {
                        var e = s.EffectOf(SpellEffect.SchoolDamage);
                        return e != null ? _ap * (e.BasePoints + 1) / 100f - (e.BasePoints + 1) : 0f;
                    }
                    break;
                case 8:
                    if ((f[0] & 0x20000u) != 0 && combo > 0) return _ap * combo * RandRange(0.03f, 0.07f);   // Eviscerate
                    break;
                case 9:
                    if ((f[0] & 0x800u) != 0) return _rap * 0.15f;                                   // Arcane Shot
                    break;
            }
            return 0f;
        }

        /// <summary>Per-tick attack-power parts of DoTs: Rend 0.2 x (average weapon hit), Serpent Sting 20% RAP spread over the ticks.</summary>
        private int ScriptTickBonus(SpellInfo s, SpellEffectInfo e)
        {
            var f = s.SpellFamilyFlags;
            if (s.SpellFamilyName == 4 && (f[0] & 0x20u) != 0)
            {
                float mn, mx, t;
                MeleeDamageRange(false, out mn, out mx, out t);
                return (int)(0.2f * (mn + mx) / 2f);
            }
            if (s.SpellFamilyName == 9 && (f[0] & 0x4000u) != 0 && e.Amplitude > 0)
            {
                int ticks = Math.Max(1, s.DurationAt(_player.Level) / e.Amplitude);
                return (int)(_rap * 0.2f / ticks);
            }
            return 0;
        }

        private void Heal(OfflineUnit u, int amount, int spellId, bool crit, GameEventType type)
        {
            if (u == null || u.Dead || amount <= 0) return;
            int before = u.Health;
            u.Health = Math.Min(u.MaxHealth, u.Health + amount);
            Emit(type, PlayerGuid, u.Guid, amount, spellId, crit, null);
            if (u is OfflineCreature cc) cc.State.Health = cc.Health;
        }

        // ------------------------------------------------------------------ auras

        private void ApplySpellAuras(SpellInfo s, OfflineUnit caster, OfflineUnit target, int combo)
        {
            if (caster == null) caster = _me;
            // Iteration 3: harmful = caster and target on opposite sides (creature debuffs on the player, pet/totem buffs on the player).
            bool harmful = caster == target ? false : IsHostilePair(caster, target);
            int casterLevel = caster.Level > 0 ? caster.Level : _player.Level;
            int durMs = s.DurationAt(casterLevel);
            if (durMs > 0 && s.IsFinisher && s.MaxDurationMs > s.DurationMs && combo > 0)
                durMs = s.DurationMs + (s.MaxDurationMs - s.DurationMs) * combo / 5;    // Slice and Dice: longer per combo point
            if (durMs == 0) durMs = -1;

            // Same spell (or another rank of it) from the same caster: refresh / stack.
            OfflineAura existing = null;
            for (int a = 0; a < target.Auras.Count; a++)
            {
                var au = target.Auras[a];
                if (au.Caster != caster.Guid) continue;
                if (au.Spell.Id == s.Id || (au.Spell.Name == s.Name && au.Spell.Name.Length > 0 && !au.FromForm)) { existing = au; break; }
            }
            // Only one shapeshift at a time; only one permanent self aura per class family (hunter aspects, tracking).
            var shape = s.AuraOf(SpellAura.ModShapeshift);
            if (shape != null && target == _me) { if (existing == null) LeaveForm(); }
            else if (target == _me && durMs < 0 && s.SpellFamilyName != 0 && !s.IsPassive)
            {
                for (int a = _me.Auras.Count - 1; a >= 0; a--)
                {
                    var au = _me.Auras[a];
                    if (au == existing || au.FromForm || au.Spell.IsPassive || au.Duration > 0 || au == _mountAura) continue;   // iteration 7: a class mount (Warhorse) is no class aura
                    if (au.Spell.SpellFamilyName == s.SpellFamilyName && au.Spell.AuraOf(SpellAura.ModShapeshift) == null) RemoveAuraAt(_me, a);
                }
            }

            var aura = existing;
            if (aura == null)
            {
                aura = new OfflineAura();
                aura.Spell = s;
                aura.Caster = caster.Guid;
                aura.Harmful = harmful;
                aura.View = new AuraView { SpellId = s.Id, Name = s.Name, IconPath = s.IconPath, Harmful = harmful, CasterGuid = caster.Guid };
                aura.Hidden = shape != null && s.SpellFamilyName == 4;   // warrior stances live on the stance bar, not in the buff list (Active on the ability)
                target.Auras.Add(aura);
            }
            else if (aura.Spell.Id != s.Id)
            {
                aura.Spell = s;
                aura.View.SpellId = s.Id;
            }
            else if (s.StackAmount > 1 && aura.Stacks < s.StackAmount) aura.Stacks++;
            // Stage S1 (PLAN10 s5): the aura of a cast released with Immersive casting ticks with the damage bonus; any other cast of the
            // spell (a refresh without Immersive casting) makes it a normal aura again.
            aura.Immersive = caster == _me && ImmersiveDamage(s);
            aura.Duration = durMs > 0 ? durMs / 1000f : 0f;
            aura.Remaining = durMs > 0 ? durMs / 1000f : -1f;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || !e.IsAura) continue;
                bool effOnEnemy = e.TargetsEnemy || (e.TargetA == SpellTarget.TargetAny && harmful);
                aura.Active[i] = effOnEnemy == harmful;
                if (!aura.Active[i]) { aura.Amount[i] = 0; continue; }
                aura.Amount[i] = CalcValue(s, e, casterLevel) + (int)(e.PointsPerComboPoint * combo);
                aura.TickTimer[i] = e.Amplitude > 0 ? e.Amplitude / 1000f : 0f;
                aura.TickBonus[i] = (e.Aura == SpellAura.PeriodicDamage) ? ScriptTickBonus(s, e) : 0;
                if (e.Aura == SpellAura.ModShapeshift && target == _me) EnterForm(e.MiscValue);
            }
            aura.View.Duration = aura.Duration;
            aura.View.Remaining = aura.Remaining < 0 ? 0 : aura.Remaining;
            aura.Charges = s.ProcCharges > 0 ? s.ProcCharges : -1;
            aura.DamageTaken = 0;
            if (aura.Charges > 0) aura.View.Stacks = aura.Charges;
            aura.View.Stacks = aura.Stacks;
            if (target == _me) { _statsDirty = true; _aurasChanged = true; }
            else if (target is OfflineCreature cc) { RefreshCreatureAuras(cc); if (s.HasAura(SpellAura.ModStun)) { cc.State.Moving = false; cc.HasMoveTarget = false; } }
            // Iteration 3: aura events for the VFX (state kits), crowd control stops casts, Polymorph shows the sheep, Shadowmeld drops threat.
            EmitAuraApplied(aura, target);
            if (target != _me && (s.HasAura(SpellAura.ModStun) || s.HasAura(SpellAura.ModFear) || s.HasAura(SpellAura.ModConfuse)) && target.Casting != null) InterruptUnitCast(target, "Interrupted");
            if (target == _me && (s.HasAura(SpellAura.ModStun) || s.HasAura(SpellAura.ModFear) || s.HasAura(SpellAura.ModConfuse))) { CancelCast("Interrupted"); EndChannel("Interrupted"); }
            if (target is OfflineCreature tc && s.HasAura(SpellAura.Transform)) RefreshTransform(tc);
            if (target == _me && s.HasAura(SpellAura.ModTotalThreat) && s.AuraOf(SpellAura.ModTotalThreat).BasePoints < -1000000) WipePlayerThreat();
            AfterAuraApplied(s, caster, target);
        }

        private bool _aurasChanged = true;

        /// <summary>
        /// Passive spells a form applies. The server owns this list (TrinityCore/AzerothCore HandleAuraModShapeshift): the client's
        /// SpellShapeshiftForm.dbc stance spell fields are empty for these forms (measured). Names checked in Spell.dbc:
        /// 1178 "Bear Form (Passive)", 21178 "Bear Form (Passive2)", 9635 "Dire Bear Form (Passive)", 3025/48629 "Cat Form (Passive)",
        /// 21156 "Battle Stance Passive", 7376 "Defensive Stance Passive", 7381 "Berserker Stance Passive", 24905/69366 Moonkin.
        /// </summary>
        private static int[] FormPassives(int formId)
        {
            switch (formId)
            {
                case 1: return new[] { 3025, 48629 };
                case 5: return new[] { 1178, 21178 };
                case 8: return new[] { 9635, 21178 };
                case 17: return new[] { 21156 };
                case 18: return new[] { 7376 };
                case 19: return new[] { 7381 };
                case 31: return new[] { 24905, 69366 };
            }
            return new int[0];
        }

        private void EnterForm(int formId)
        {
            _form = formId;
            var f = _db.Form(formId);
            if (f != null)
            {
                var ids = new List<int>(FormPassives(formId));
                foreach (int sid in f.StanceSpells) if (sid > 0 && !ids.Contains(sid)) ids.Add(sid);
                foreach (int sid in ids)
                {
                    if (sid <= 0) continue;
                    var ps = _db.Find(sid);
                    if (ps == null || ps.SpellLevel > _player.Level) continue;
                    var au = new OfflineAura { Spell = ps, Caster = PlayerGuid, Hidden = true, FromForm = true, Remaining = -1f };
                    au.View = new AuraView { SpellId = sid, Name = ps.Name, IconPath = ps.IconPath, CasterGuid = PlayerGuid };
                    for (int i = 0; i < 3; i++) { var e = ps.Effects[i]; if (e != null && e.IsAura) au.Amount[i] = CalcValue(ps, e, _player.Level); }
                    _me.Auras.Add(au);
                }
            }
            if (formId == 5 || formId == 8) _rage = 0f;                 // shifting into bear: rage starts empty
            if (formId == 17 || formId == 18 || formId == 19) _rage = 0f;  // stance change loses the rage (no Tactical Mastery talent offline)
            if (formId == 1) _energy = 0f;
            _statsDirty = true;
        }

        private void LeaveForm()
        {
            if (_form == 0) return;
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                var au = _me.Auras[a];
                if (au.FromForm || au.Spell.AuraOf(SpellAura.ModShapeshift) != null) { _me.Auras.RemoveAt(a); EmitAuraRemoved(au, _me); }
            }
            _form = 0;
            _statsDirty = true;
            _aurasChanged = true;
        }

        private void RemoveAuraAt(OfflineUnit u, int index)
        {
            var au = u.Auras[index];
            u.Auras.RemoveAt(index);
            EmitAuraRemoved(au, u);
            if (u == _me && au == _mountAura) MountAuraGone();   // iteration 7: whatever removed it, the player is on foot again
            if (u is OfflineCreature tc && au.Spell.HasAura(SpellAura.Transform)) RefreshTransform(tc);
            if (u == _me)
            {
                if (au.Spell.AuraOf(SpellAura.ModShapeshift) != null) LeaveForm();
                _statsDirty = true;
                _aurasChanged = true;
            }
            else if (u is OfflineCreature c) RefreshCreatureAuras(c);
        }

        private void RefreshCreatureAuras(OfflineCreature c)
        {
            c.State.Auras.Clear();
            for (int a = 0; a < c.Auras.Count; a++) if (!c.Auras[a].Hidden) c.State.Auras.Add(c.Auras[a].View);
        }

        private void StepAuras(OfflineUnit u, float dt)
        {
            for (int a = u.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= u.Auras.Count) continue;
                var au = u.Auras[a];
                // periodic effects
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e == null || !e.IsAura || e.Amplitude <= 0 || !au.Active[i]) continue;
                    if (e.Aura != SpellAura.PeriodicDamage && e.Aura != SpellAura.PeriodicHeal && e.Aura != SpellAura.PeriodicEnergize && e.Aura != SpellAura.PeriodicTriggerSpell && e.Aura != SpellAura.ObsModHealth) continue;
                    au.TickTimer[i] -= dt;
                    while (au.TickTimer[i] <= 0.0001f && !u.Dead)
                    {
                        au.TickTimer[i] += e.Amplitude / 1000f;
                        Tick(au, e, i, u);
                        if (u.Dead || a >= u.Auras.Count || u.Auras[a] != au) break;
                    }
                    if (u.Dead) return;
                }
                if (a >= u.Auras.Count || u.Auras[a] != au) continue;
                if (au.Remaining >= 0f)
                {
                    au.Remaining -= dt;
                    au.View.Remaining = Math.Max(0f, au.Remaining);
                    if (au.Remaining <= 0f) RemoveAuraAt(u, a);
                }
            }
        }

        private void Tick(OfflineAura au, SpellEffectInfo e, int i, OfflineUnit u)
        {
            int amount = au.Amount[i] + au.TickBonus[i];
            switch (e.Aura)
            {
                case SpellAura.PeriodicDamage:
                    if (u is OfflineCreature c) DamageCreature(c, Math.Max(1, au.Immersive ? ImmersiveMode.Boost(amount) : amount), au.Spell.Id, false, GameEventType.PeriodicDamage, false, au.Caster == PlayerGuid ? null : UnitOf(au.Caster));
                    else if (u == _me) DamagePlayer(Math.Max(1, amount), CreatureOf(au.Caster), au.Spell.Id, false, GameEventType.PeriodicDamage);
                    break;
                case SpellAura.PeriodicHeal:
                    if (au.Caster == PlayerGuid) Heal(u, Math.Max(1, amount), au.Spell.Id, false, GameEventType.PeriodicHeal);
                    else HealBy(UnitOf(au.Caster), u, Math.Max(1, amount), au.Spell.Id, GameEventType.PeriodicHeal);
                    break;
                case SpellAura.PeriodicTriggerSpell:
                    TickTriggerSpell(au, e, u);
                    break;
                case SpellAura.ObsModHealth:   // percent of max health per tick (Cannibalize 20578)
                    Heal(u, Math.Max(1, u.MaxHealth * Math.Max(1, amount) / 100), au.Spell.Id, false, GameEventType.PeriodicHeal);
                    break;
                case SpellAura.PeriodicEnergize:
                    if (u == _me) AddPower((PowerType)e.MiscValue, e.MiscValue == 1 ? amount / 10f : amount);
                    break;
            }
        }

        // ------------------------------------------------------------------ stealth

        private void BreakStealth()
        {
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
                if (_me.Auras[a].Spell.HasAura(SpellAura.ModStealth)) RemoveAuraAt(_me, a);
        }

        /// <summary>Casting breaks auras with AURA_INTERRUPT_FLAG_CAST (0x4, Stealth) unless the spell has SPELL_ATTR1_NOT_BREAK_STEALTH (0x40, Sap).</summary>
        private void BreakStealthFor(SpellInfo s)
        {
            if ((s.Attributes[1] & 0x40u) != 0 || s.HasAura(SpellAura.ModStealth)) return;
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                var au = _me.Auras[a];
                if ((au.Spell.AuraInterruptFlags & 0x4) != 0 && !au.FromForm) RemoveAuraAt(_me, a);
            }
        }

        // ------------------------------------------------------------------ spellbook views

        private readonly Dictionary<int, AbilityView> _abilityById = new Dictionary<int, AbilityView>();

        private void RebuildSpellbook()
        {
            _player.Spellbook.Clear();
            _abilityById.Clear();
            var ctx = TextContext();
            var list = new List<SpellInfo>();
            foreach (int id in _rec.Spells) { if (MountCatalog.IsMount(id)) continue; var s = _db.Find(id); if (s != null) list.Add(s); }   // iteration 7: mounts live in Player.Mounts
            list.Sort((a, b) => a.SpellLevel != b.SpellLevel ? a.SpellLevel.CompareTo(b.SpellLevel) : a.Id.CompareTo(b.Id));
            foreach (var s in list)
            {
                var v = new AbilityView();
                v.SpellId = s.Id;
                v.Name = s.Name;
                v.Rank = s.Rank;
                v.IconPath = s.IconPath;
                v.Description = SpellText.Resolve(_db, s, s.Description, ctx);
                PowerType pt;
                v.Cost = CostOf(s, out pt);
                v.CostType = pt;
                v.CastTime = s.CastTimeMs / 1000f;
                v.Cooldown = Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs) / 1000f;
                v.RangeMin = s.RangeMin;
                v.RangeMax = s.RangeMax;
                v.Harmful = s.IsHarmful || HasEnemyTargetEffect(s);
                v.NeedsTarget = s.NeedsUnitTarget && v.Harmful;
                v.Passive = s.IsPassive;
                v.LearnedAtLevel = Math.Max(1, s.SpellLevel);
                v.Tooltip = BuildTooltip(s, v);
                _player.Spellbook.Add(v);
                _abilityById[s.Id] = v;
            }
        }

        private SpellTextContext TextContext()
        {
            return new SpellTextContext
            {
                CasterLevel = _player.Level, AttackPower = _ap, RangedAttackPower = _rap,
                MainWeaponMin = _mhMin, MainWeaponMax = _mhMax, MainWeaponSpeed = _mhSpeed,
                RangedWeaponMin = _rgMin, RangedWeaponMax = _rgMax, RangedWeaponSpeed = _rgSpeed
            };
        }

        private string BuildTooltip(SpellInfo s, AbilityView v)
        {
            var sb = new StringBuilder();
            sb.Append(s.Name);
            if (s.Rank.Length > 0) sb.Append(" (").Append(s.Rank).Append(')');
            sb.Append('\n');
            if (v.Cost > 0)
            {
                sb.Append(v.Cost).Append(' ').Append(v.CostType == PowerType.Rage ? "Rage" : v.CostType == PowerType.Energy ? "Energy" : v.CostType == PowerType.Mana ? "Mana" : "Health");
                sb.Append("   ");
            }
            if (s.RangeMax > 5f) sb.Append(s.RangeMin > 0 ? s.RangeMin.ToString("0") + "-" : "").Append(s.RangeMax.ToString("0")).Append(" yd range");
            else if (s.RangeMax > 0f) sb.Append("Melee Range");
            sb.Append('\n');
            sb.Append(s.AutoRepeat ? "Auto-repeat" : s.CastTimeMs > 0 ? (s.CastTimeMs / 1000f).ToString("0.#") + " sec cast" : s.OnNextSwing ? "Next melee" : "Instant");
            if (v.Cooldown > 0f) sb.Append("   ").Append(SpellText.DurationText((int)(v.Cooldown * 1000))).Append(" cooldown");
            sb.Append('\n');
            sb.Append(v.Description);
            string unsup = Unsupported(s);
            if (unsup != null) sb.Append("\n(").Append(unsup).Append(')');
            return sb.ToString();
        }

        private void UpdateAbilities()
        {
            var target = CreatureOf(_target);
            for (int i = 0; i < _player.Spellbook.Count; i++)
            {
                var v = _player.Spellbook[i];
                var s = _db.Find(v.SpellId);
                if (s == null) continue;
                float cd = CooldownLeft(s);
                if (s.StartRecoveryTimeMs > 0 && _gcd > cd) cd = _gcd;
                v.CooldownRemaining = cd;
                string why;
                if (s.IsPassive) why = "Passive";
                else if (s.Id == AutoAttackSpell) why = _player.Dead ? "You are dead" : (IsAttackable(target) ? null : "You have no target");
                else if (s.AutoRepeat) why = CheckCast(s, target, false);
                else why = Unsupported(s) ?? (s.OnNextSwing && _nextSwing == s ? null : CheckCast(s, ResolveTargetFor(s, target), true));
                v.Usable = why == null;
                v.UnusableReason = why;
                var shape = s.AuraOf(SpellAura.ModShapeshift);
                v.Active = (shape != null && shape.MiscValue == _form && _form != 0) || (s.Id == AutoAttackSpell && _autoAttack) || (s.AutoRepeat && _autoShot)
                    || (_nextSwing != null && _nextSwing.Id == s.Id) || (_casting != null && _casting.Id == s.Id) || (_held != null && _held.Id == s.Id)
                    || (s.HasAura(SpellAura.ModStealth) && _player.Stealthed) || (_me.FindAura(s.Id) != null && s.DurationMs < 0);
            }
        }

        /// <summary>
        /// Whom a spell goes to with this target selected (iteration 4): a helpful spell that can target an ally goes to a targeted friendly
        /// NPC / pet / totem; other helpful spells (and helpful spells with an enemy or no target) go to yourself (the client's auto self-cast);
        /// harmful spells go to the target (a friendly one then fails with "Invalid target", SPELL_FAILED_BAD_TARGETS).
        /// </summary>
        private OfflineUnit ResolveTargetFor(SpellInfo s, OfflineCreature target)
        {
            if (target != null && HelpfulOnFriend(s, target)) return target;
            return (s.IsHarmful || HasEnemyTargetEffect(s)) ? (OfflineUnit)target : _me;
        }
    }
}
