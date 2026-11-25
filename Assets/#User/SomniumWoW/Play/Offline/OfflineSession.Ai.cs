using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        /// <summary>
        /// Creature state machine: Idle (pause) -> Wander (walk to a random point within WanderRadius of the spawn) -> Chase (run to
        /// melee range) -> Attack (swing timer) -> Evade (run home immune, full health) -> Dead (corpse, loot) -> Despawned -> respawn.
        /// Hostile creatures aggro within AggroRadius - (player level - creature level) yards (TrinityCore GetAttackDistance shape,
        /// min 5; gray creatures do not aggro); neutral ones only when attacked; NPCs never fight.
        /// </summary>
        private void StepCreature(OfflineCreature c, float dt)
        {
            if (c.Summon != SummonKind.None) { StepSummon(c, dt); return; }   // the player's pet / totems (iteration 3)
            if (!c.Present)
            {
                c.RespawnTimer -= dt;
                if (c.RespawnTimer <= 0f) Respawn(c);
                return;
            }
            if (c.Dead)
            {
                c.CorpseTimer -= dt;
                c.RespawnTimer -= dt;
                if (c.CorpseTimer <= 0f)
                {
                    if (_lootCorpse == c) CloseLoot();
                    c.Present = false;
                    _entitiesDirty = true;
                    if (_target == c.Guid) SetTarget(0);
                }
                return;
            }

            StepAuras(c, dt);
            if (c.Dead) return;

            if (c.Tpl.IsNpc) { StepNpc(c, dt); return; }

            switch (c.Ai)
            {
                case AiState.Idle:
                case AiState.Wander:
                    RegenOutOfCombat(c, dt);
                    if (c.AssistDelay > 0f)
                    {
                        c.AssistDelay -= dt;
                        if (c.AssistDelay <= 0f) { c.AssistDelay = -1f; if (!_player.Dead) Engage(c); break; }
                    }
                    if (CanAggro(c)) { Engage(c); break; }
                    if (CanAggroPet(c)) { Engage(c, _pet); break; }
                    StepWander(c, dt);
                    break;
                case AiState.Chase:
                case AiState.Attack:
                    StepFight(c, dt);
                    break;
                case AiState.Evade:
                    StepEvade(c, dt);
                    break;
            }
            SyncCreatureState(c);
        }

        private void RegenOutOfCombat(OfflineCreature c, float dt)
        {
            // Creature::RegenerateHealth out of combat: a third of max health every 2 s.
            if (c.Health < c.MaxHealth) c.Health = Math.Min(c.MaxHealth, c.Health + Math.Max(1, (int)(c.MaxHealth / 6f * dt + 0.5f)));
        }

        private bool CanAggro(OfflineCreature c)
        {
            if (_player.Dead || _player.OnTaxi || c.State.Reaction != Reaction.Hostile || c.Stunned) return false;   // iteration 4: flying players are ignored
            if (WowFormulas.ColorCode(_player.Level, c.Level) == 0) return false;          // gray: ignores you
            float radius = (c.Tpl.AggroRadius > 0 ? c.Tpl.AggroRadius : 10f) - (_player.Level - c.Level);
            if (radius < 5f) radius = 5f;
            if (_player.Stealthed)
            {
                // Stealth: noticed only very close, in front (APPROXIMATION of WoW's stealth detection).
                radius = IsBehind(c, _me) ? 1.5f : Math.Max(1.5f, 4f - (_player.Level - c.Level));
            }
            float d = Dist(c.Pos, _me.Pos);
            if (d > radius) return false;
            return Math.Abs(c.Pos.Z - _me.Pos.Z) < 12f;
        }

        /// <summary>A hostile creature notices the player's pet walking into its aggro radius (same rules as for the player).</summary>
        private bool CanAggroPet(OfflineCreature c)
        {
            if (_pet == null || !_pet.Present || _pet.Dead || c.State.Reaction != Reaction.Hostile || c.Incapacitated) return false;
            if (WowFormulas.ColorCode(_player.Level, c.Level) == 0) return false;
            float radius = Math.Max(5f, (c.Tpl.AggroRadius > 0 ? c.Tpl.AggroRadius : 10f) - (_pet.Level - c.Level));
            return Dist(c.Pos, _pet.Pos) <= radius && Math.Abs(c.Pos.Z - _pet.Pos.Z) < 12f;
        }

        /// <summary>The creature enters combat with the player (aggro, being hit, assisting a neighbour).</summary>
        private void Engage(OfflineCreature c) { Engage(c, _me); }

        /// <summary>
        /// The creature enters combat with a unit of the player's side (the player, the pet or a totem): it goes on the creature's threat
        /// list (iteration 3) and the creature fights whoever holds the most threat (VictimOf).
        /// </summary>
        private void Engage(OfflineCreature c, OfflineUnit who)
        {
            if (c == null || c.Dead || c.Tpl.IsNpc || c.Summon != SummonKind.None || c.Ai == AiState.Evade || _player.Dead || _player.OnTaxi) return;
            if (IsCritter(c)) return;   // iteration 5: critters never fight back (they keep wandering; OfflineSession.Critters.cs)
            if (who == null || !IsPlayerSide(who)) who = _me;
            if (!c.Threat.ContainsKey(who.Guid)) AddThreat(c, who, 0.01f);
            if (c.Engaged) return;
            c.SpellTimer = RandRange(0.3f, 1.2f);   // casters open with a spell soon after aggro
            c.Engaged = true;
            c.Ai = AiState.Chase;
            c.AssistDelay = -1f;
            c.HasMoveTarget = false;
            c.LeashTimer = 0f;
            c.SwingTimer = Math.Min(c.SwingTimer, 0.3f + Rand01() * 0.4f);
            c.State.InCombat = true;
            c.State.TargetGuid = who.Guid;
            CancelConsumable("You are in combat");
            // Creatures of the same kind nearby come to help (TrinityCore CallAssistance, radius 10, delay 1.5 s).
            for (int i = 0; i < _creatures.Count; i++)
            {
                var o = _creatures[i];
                if (o == c || !o.Present || o.Dead || o.Engaged || o.Tpl.IsNpc || o.Tpl.Entry != c.Tpl.Entry || o.AssistDelay > 0f) continue;
                if (o.Ai == AiState.Evade || o.Stunned) continue;
                if (Dist(o.Pos, c.Pos) <= AssistRadius) o.AssistDelay = 1.5f;
            }
        }

        private void StartEvade(OfflineCreature c)
        {
            c.Engaged = false;
            c.Ai = AiState.Evade;
            c.AssistDelay = -1f;
            c.EvadeSafety = 0f;
            c.State.InCombat = false;
            c.State.TargetGuid = 0;
            c.State.Evading = true;
            c.Threat.Clear();
            c.ForcedTimer = 0f;
            c.ForcedTarget = 0;
            if (c.Casting != null) InterruptUnitCast(c, "Interrupted");
            // Auras from the player's side fall off when a creature evades.
            for (int a = c.Auras.Count - 1; a >= 0; a--) if (a < c.Auras.Count && c.Auras[a].Caster != c.Guid) RemoveAuraAt(c, a);
            RefreshCreatureAuras(c);
            if (_comboTarget == c.Guid) { _combo = 0; _comboTarget = 0; }
        }

        private void StepWander(OfflineCreature c, float dt)
        {
            if (c.Spawn.WanderRadius <= 0.1f)
            {
                // Stands still; drift back home if displaced.
                if (Dist2D(c.Pos, c.Home) > 1f) MoveToward(c, c.Home, c.Tpl.WalkSpeed, dt, false);
                else { c.State.Moving = false; c.Pos.O = c.Home.O; }
                c.Ai = AiState.Idle;
                return;
            }
            if (c.Ai == AiState.Idle)
            {
                c.State.Moving = false;
                c.WaitTimer -= dt;
                if (c.WaitTimer > 0f) return;
                double ang = _rng.NextDouble() * Math.PI * 2;
                float r = (float)Math.Sqrt(_rng.NextDouble()) * c.Spawn.WanderRadius;
                c.MoveTarget = new WowPos(c.Home.X + (float)Math.Cos(ang) * r, c.Home.Y + (float)Math.Sin(ang) * r, c.Home.Z, 0f);
                c.HasMoveTarget = true;
                c.Ai = AiState.Wander;
                return;
            }
            // Wander
            if (!c.HasMoveTarget || MoveToward(c, c.MoveTarget, c.Tpl.WalkSpeed, dt, false))
            {
                c.HasMoveTarget = false;
                c.Ai = AiState.Idle;
                c.WaitTimer = 3f + (float)_rng.NextDouble() * 7f;
            }
        }

        private void StepFight(OfflineCreature c, float dt)
        {
            if (_player.Dead || _player.OnTaxi) { StartEvade(c); return; }   // iteration 4: no combat with a player on a flight
            if (Dist2D(c.Pos, c.Home) > LeashRange) { StartEvade(c); return; }
            if (c.ForcedTimer > 0f) c.ForcedTimer -= dt;
            if (c.SpellTimer > 0f) c.SpellTimer -= dt;
            if (c.SwingTimer > 0f) c.SwingTimer -= dt;
            // Crowd control (iteration 3): stunned / polymorphed creatures stand (a sheep ambles), feared ones run away.
            if (c.Stunned || c.Confused)
            {
                if (c.Casting != null) InterruptUnitCast(c, "Interrupted");
                if (c.Confused && !c.Stunned) StepConfused(c, dt); else c.State.Moving = false;
                return;
            }
            if (c.Feared) { if (c.Casting != null) InterruptUnitCast(c, "Interrupted"); StepFlee(c, dt); return; }
            var victim = VictimOf(c);
            if (victim == null) { StartEvade(c); return; }
            c.State.TargetGuid = victim.Guid;
            if (c.Casting != null) { c.State.Moving = false; FaceToward(c, victim.Pos); StepUnitCast(c, dt); return; }
            float range = MeleeRange(c, victim);
            float d = Dist(c.Pos, victim.Pos);
            FaceToward(c, victim.Pos);
            if (TryCreatureSpell(c, victim, d)) return;
            if (d > range && CasterHoldsRange(c, victim, d)) { c.State.Moving = false; c.LeashTimer = 0f; c.Ai = AiState.Attack; return; }
            if (d > range)
            {
                c.Ai = AiState.Chase;
                c.LeashTimer += dt;
                if (c.LeashTimer > 20f) { StartEvade(c); return; }   // cannot reach the player (up a ledge...) for 20 s
                if (!c.Rooted)
                {
                    float speed = (c.Tpl.RunSpeed > 0 ? c.Tpl.RunSpeed : 7f) * SpeedMultiplier(c);
                    // stop a little inside melee range
                    float stop = range * 0.75f;
                    float k = d > 0.01f ? Math.Max(0f, (d - stop) / d) : 0f;
                    var goal = new WowPos(c.Pos.X + (victim.Pos.X - c.Pos.X) * k, c.Pos.Y + (victim.Pos.Y - c.Pos.Y) * k, victim.Pos.Z, 0f);
                    MoveToward(c, goal, speed, dt, true);
                    FaceToward(c, victim.Pos);
                }
                else c.State.Moving = false;
                return;
            }
            c.Ai = AiState.Attack;
            c.State.Moving = false;
            c.LeashTimer = 0f;
            if (c.SwingTimer <= 0f && !c.Pacified)
            {
                c.SwingTimer += c.AttackSpeed / (c.AuraMultiplier(SpellAura.ModMeleeHaste, 0) * c.AuraMultiplier(SpellAura.MeleeSlow, 0));
                if (c.SwingTimer < 0.2f) c.SwingTimer = c.AttackSpeed;
                if (victim == _me) CreatureSwing(c); else CreatureSwingAt(c, victim);
            }
        }

        private void StepEvade(OfflineCreature c, float dt)
        {
            c.EvadeSafety += dt;
            float speed = (c.Tpl.RunSpeed > 0 ? c.Tpl.RunSpeed : 7f) * 1.2f;
            bool home = MoveToward(c, c.Home, speed, dt, true) || c.EvadeSafety > 20f;
            if (!home) return;
            c.Pos = c.Home;
            c.Pos.Z = Ground(c.Home.X, c.Home.Y, c.Home.Z);
            c.Health = c.MaxHealth;
            if (c.MaxMana > 0) c.Mana = c.MaxMana;
            ClearAuras(c);
            RefreshCreatureAuras(c);
            c.Ai = AiState.Idle;
            c.WaitTimer = 2f + (float)_rng.NextDouble() * 4f;
            c.State.Evading = false;
            c.State.Moving = false;
            c.State.Running = false;
            c.DamagedByPlayer = false;
        }

        private void StepNpc(OfflineCreature c, float dt)
        {
            // NPCs never fight. They wander if they have a radius and face the player while talking.
            if (_dialogNpc == c) { c.State.Moving = false; FaceToward(c, _me.Pos); SyncCreatureState(c); return; }
            StepWander(c, dt);
            SyncCreatureState(c);
        }

        /// <summary>Moves toward a point on the ground; returns true when arrived (within 0.5 yd).</summary>
        private bool MoveToward(OfflineCreature c, WowPos goal, float speed, float dt, bool running)
        {
            float slow = Math.Max(0.05f, c.AuraMultiplier(SpellAura.ModDecreaseSpeed, 0));
            if (c.Rooted || c.Stunned) { c.State.Moving = false; return false; }
            float dx = goal.X - c.Pos.X, dy = goal.Y - c.Pos.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d < 0.5f) { c.State.Moving = false; c.State.Running = false; return true; }
            float step = Math.Min(d, speed * slow * dt);
            float nx = c.Pos.X + dx / d * step, ny = c.Pos.Y + dy / d * step;
            c.Pos.X = nx;
            c.Pos.Y = ny;
            c.Pos.Z = Ground(nx, ny, c.Pos.Z);
            c.Pos.O = (float)Math.Atan2(dy, dx);
            c.State.Moving = true;
            c.State.Running = running;
            return d - step < 0.5f;
        }

        private static void FaceToward(OfflineCreature c, WowPos p)
        {
            float dx = p.X - c.Pos.X, dy = p.Y - c.Pos.Y;
            if (dx * dx + dy * dy > 0.0001f) c.Pos.O = (float)Math.Atan2(dy, dx);
        }

        private void SyncCreatureState(OfflineCreature c)
        {
            var s = c.State;
            s.Pos = c.Pos;
            s.Health = c.Health;
            s.MaxHealth = c.MaxHealth;
            s.Level = c.Level;
            s.Dead = c.Dead;
        }
    }
}
