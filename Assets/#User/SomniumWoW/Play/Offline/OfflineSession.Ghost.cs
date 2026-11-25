using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    // Stage O2 (owner ghost, PLAN9 s0 decision 3 + s2): WoW 3.3.5a's death flow offline, AzerothCore's rules (quoted below):
    //   died (PlayerDied, the "Release Spirit" popup, 6 minutes until the automatic release)
    //   -> ReleaseSpirit: the corpse stays where the player died, a ghost appears at the zone's graveyard (SpiritReleased + Teleport),
    //      the Ghost aura 8326 (+ 20584 for night elves), the spirit healer shows (only to ghosts), the reclaim delay starts
    //   -> run back: within CORPSE_RECLAIM_RADIUS (39 yd) of the corpse with the delay over = PlayerState.CanReclaim ("Resurrect")
    //   -> ReclaimCorpse: alive at 50% health and mana, no penalty
    //   or AcceptSpiritHealer next to the spirit healer: alive at 50% + Resurrection Sickness 15007 from level 11 (durability is not
    //      modelled offline, so its 25% durability loss is not either - README Known gaps).
    //
    // PlayerState flags (the convention every offline rule already relies on - all ~30 `_player.Dead` checks keep a ghost inert):
    //   Dead  = not alive: lying dead AND as a ghost (AzerothCore !IsAlive(): a ghost is DeathState::Dead + PLAYER_FLAGS_GHOST)
    //   Ghost = released spirit (PLAYER_FLAGS_GHOST); "Release Spirit" is offered for Dead && !Ghost only.
    //
    // Per-step hook: StepDeath(dt) must run every step while the player is dead. No step function of the ghost row runs then
    // (StepPlayer returns first while dead), so the integrator adds ONE call in OfflineSession.Stats.cs StepPlayer:
    //   if (_player.Dead) { _inCombat = false; StepDeath(dt); return; }
    // (report P/reports9/ghost.md, request 1). Until then the release timer / reclaim state do not advance (the probe says so).
    public sealed partial class OfflineSession
    {
        // ---- AzerothCore's rules (quoted in the ghost report)
        /// <summary>Corpse.h:35 #define CORPSE_RECLAIM_RADIUS 39 (yards; MiscHandler.cpp:658 IsWithinDist(_player, CORPSE_RECLAIM_RADIUS, true) = 3D).</summary>
        public const float CorpseReclaimRadius = 39f;
        /// <summary>Player.cpp:151 static uint32 copseReclaimDelay[MAX_DEATH_COUNT] = { 30, 60, 120 } (seconds).</summary>
        private static readonly int[] CorpseReclaimDelays = { 30, 60, 120 };
        /// <summary>Player.h:76 #define DEATH_EXPIRE_STEP (5*MINUTE); Player.h:77 #define MAX_DEATH_COUNT 3.</summary>
        private const double DeathExpireStep = 300.0;
        private const int MaxDeathCount = 3;
        /// <summary>Player.cpp:4654 "6 minutes until repop at graveyard": m_deathTimer = 6 * MINUTE * IN_MILLISECONDS (PlayerUpdates.cpp:364-376 repops when it runs out, not in instances).</summary>
        public const float AutoReleaseSeconds = 360f;
        /// <summary>worldserver.conf.dist Death.SicknessLevel = 11 (Player.cpp:4618 CONFIG_DEATH_SICKNESS_LEVEL).</summary>
        public const int SicknessLevel = 11;
        /// <summary>worldserver.conf.dist Death.CorpseReclaimDelay.PvE = 1 (enabled): offline deaths are all PvE.</summary>
        private static readonly bool CorpseReclaimDelayPvE = true;
        /// <summary>MiscHandler.cpp:662 ResurrectPlayer(InBattleground() ? 1.0f : 0.5f); NPCHandler.cpp:262 ResurrectPlayer(0.5f, true).</summary>
        private const float ResurrectPercent = 0.5f;
        /// <summary>Player.cpp:4513-4517 BuildPlayerRepop: CastSpell(this, 20584) for RACE_NIGHTELF, then CastSpell(this, 8326) (SPELL_AURA_GHOST).</summary>
        public const int GhostSpell = 8326, WispGhostSpell = 20584, ResurrectionSicknessSpell = 15007;
        private const int NightElfRace = 4;

        // ---- the spirit healer (offline)
        /// <summary>Ours: an offline-only creature entry (the offline flight masters use 900000 + node).</summary>
        public const int SpiritHealerEntry = 899990;
        /// <summary>CreatureDisplayInfo 5233 = CreatureModelData 1492 Creature\spirithealer\SpiritHealer.mdx at scale 1, alpha 255
        /// (measured, probe GhostFacts; 12824 / 17548 / 27302 are the half, 180-alpha and quarter variants).</summary>
        public const int SpiritHealerDisplay = 5233;
        /// <summary>Ours: where the spirit healer stands, yards in front of the graveyard point (the ghost arrives facing him).</summary>
        public const float SpiritHealerAhead = 5f;

        private double _deathExpire;              // AC m_deathExpireTime (session seconds), PvE escalation of the reclaim delay
        private double _ghostTime;                // AC Corpse::GetGhostTime(): when the spirit was released
        private float _releaseLeft = -1f;         // AC m_deathTimer (seconds), -1 = not running
        private WowPos _deathPos;                 // where the body fell (KillPlayer): the corpse is made there at the release
        private bool _deathPosKnown;
        private OfflineCreature _spiritHealer;
        private int _deathCount;
        private float _deathStepAt = -1f;         // _time of the last StepDeath (the probe checks the StepPlayer hook with it)
        private bool _reclaimLogged, _healerLogged;

        /// <summary>Session time of the last StepDeath call (-1 = never): lets a probe see whether StepPlayer's hook is in place.</summary>
        public float DeathStepTime { get { return _deathStepAt; } }

        /// <summary>The spirit healer's guid while it is shown (a ghost), else 0.</summary>
        public ulong SpiritHealerGuid { get { return _spiritHealer != null && _spiritHealer.Present ? _spiritHealer.Guid : 0; } }

        // ------------------------------------------------------------------ dying (called from KillPlayer)

        /// <summary>Player::KillPlayer's part of the flow: the 6-minute release timer and UpdateCorpseReclaimDelay (PlayerUpdates.cpp:1956-1979).</summary>
        private bool _deathAirborne;              // iteration 11 (quests): killed in the air on a ride (R3)

        private void OnPlayerKilledGhostFlow()
        {
            _deathPos = _me.Pos;
            _deathPosKnown = true;
            _deathAirborne = _motion.Driven && (_motion.Flying || _motion.Airborne);
            _releaseLeft = AutoReleaseSeconds;
            _deathCount++;
            if (CorpseReclaimDelayPvE)
            {
                double now = _time;
                if (now < _deathExpire)
                {
                    // "full and partly periods 1..3"
                    long count = (long)((_deathExpire - now) / DeathExpireStep) + 1;
                    _deathExpire = count < MaxDeathCount ? now + (count + 1) * DeathExpireStep : now + MaxDeathCount * DeathExpireStep;
                }
                else _deathExpire = now + DeathExpireStep;
            }
            _player.HasCorpse = false;
            _player.CanReclaim = false;
            _player.NearSpiritHealer = false;
            _player.ReclaimDelay = 0f;
        }

        /// <summary>Player::GetCorpseReclaimDelay(pvp = false) (Player.cpp:13243-13258): 30 / 60 / 120 s by how many deaths the 5-minute windows hold.</summary>
        private int CorpseReclaimDelayNow()
        {
            if (!CorpseReclaimDelayPvE) return 0;
            double now = _time;
            // "0..2 full period - should be std::ceil(x)-1 but not floor(x)"
            long count = now < _deathExpire - 1.0 ? (long)((_deathExpire - 1.0 - now) / DeathExpireStep) : 0;
            if (count < 0) count = 0;
            if (count >= CorpseReclaimDelays.Length) count = CorpseReclaimDelays.Length - 1;
            return CorpseReclaimDelays[count];
        }

        // ------------------------------------------------------------------ releasing (ReleaseSpirit in OfflineSession.Combat.cs)

        /// <summary>
        /// WorldSession::HandleRepopRequestOpcode (MiscHandler.cpp:59-88) -> Player::BuildPlayerRepop (Player.cpp:4508-4554) + RepopAtGraveyard
        /// (Player.cpp:5006-5045): the corpse at the body, a ghost (8326; + 20584 for night elves) at the closest graveyard, the reclaim delay.
        /// </summary>
        private void ReleaseToGhost(string why)
        {
            if (!_player.Dead || _player.Ghost) return;   // "if (GetPlayer()->IsAlive() || GetPlayer()->HasPlayerFlag(PLAYER_FLAGS_GHOST)) return;"
            _releaseLeft = -1f;                            // RepopAtGraveyard: "stop countdown until repop" (m_deathTimer = 0)
            if (_player.Mounted) Dismount(DismountReason.Teleport, null);   // iteration 7: never mounted through a teleport
            CloseLoot();
            CloseDialog();
            SetTarget(0);

            // the corpse where the body lies (CreateCorpse at the player's location; the body was rooted there by KillPlayer)
            _player.HasCorpse = true;
            _player.CorpseMapId = _zone.MapId;
            WowPos body = _deathPosKnown ? _deathPos : _me.Pos;
            if (_deathAirborne)
            {
                // AzerothCore lets a body killed in the air fall before the corpse is made (KillPlayer MoveFall, Player.cpp:4637-4638; the corpse at
                // the release, BuildPlayerRepop :4527-4534): ours - onto the terrain under it (WMO floors are not sampled offline)
                float g = Ground(body.X, body.Y, body.Z);
                if (!float.IsNaN(g) && g < body.Z) body.Z = g;
            }
            _player.CorpsePos = body;
            _ghostTime = _time;                            // corpse->ResetGhostTime()
            _player.Ghost = true;                          // PLAYER_FLAGS_GHOST; Dead stays true (the player is not alive)
            AddGhostAura(GhostSpell, false);
            if (_rec.Race == NightElfRace) AddGhostAura(WispGhostSpell, true);   // ours: hidden - the buff frame shows one "Ghost" (both carry the same name and tooltip)
            _statsDirty = true;

            // RepopAtGraveyard: TeleportTo(ClosestGrave) - offline, the zone's graveyard
            WowPos grave = _zone.Graveyard;
            _me.Pos = grave;
            _player.Pos = grave;
            ShowSpiritHealer(grave);
            int delay = CorpseReclaimDelayNow();
            _player.ReclaimDelay = delay;
            _player.CanReclaim = false;
            _player.NearSpiritHealer = false;
            _reclaimLogged = _healerLogged = false;

            _events.Enqueue(new GameEvent { Type = GameEventType.SpiritReleased, SourceGuid = PlayerGuid, TargetGuid = PlayerGuid, Pos = grave, Amount = delay, Text = null });
            _events.Enqueue(new GameEvent { Type = GameEventType.Teleport, Pos = grave, TargetGuid = PlayerGuid, Text = "Graveyard" });
            WowLog.Info("OfflineSession: spirit released (" + why + ") - a ghost at the graveyard " + grave + "; corpse at " + _player.CorpsePos + " on map " + _player.CorpseMapId
                + ", " + Dist(grave, _player.CorpsePos).ToString("0") + " yd away; reclaim radius " + CorpseReclaimRadius + " yd, reclaim delay " + delay
                + " s (AC copseReclaimDelay, death " + _deathCount + " of this session); Ghost aura " + GhostSpell + (_rec.Race == NightElfRace ? " + wisp " + WispGhostSpell : "")
                + "; spirit healer " + (_spiritHealer != null ? "at " + _spiritHealer.Pos : "missing") + ".");
        }

        /// <summary>An aura cast on the ghost like BuildPlayerRepop's CastSpell(this, id, true): permanent, the spell's own values.</summary>
        private void AddGhostAura(int spellId, bool hidden)
        {
            var s = _db.Find(spellId);
            if (s == null) { WowLog.Warn("OfflineSession: spell " + spellId + " is not in Spell.dbc - no Ghost aura."); return; }
            for (int a = 0; a < _me.Auras.Count; a++) if (_me.Auras[a].Spell.Id == spellId) return;
            var au = new OfflineAura { Spell = s, Caster = PlayerGuid, Hidden = hidden, Remaining = -1f, Duration = 0f };
            au.View = new AuraView { SpellId = s.Id, Name = s.Name, IconPath = s.IconPath, CasterGuid = PlayerGuid };
            for (int i = 0; i < 3; i++) { var e = s.Effects[i]; if (e != null && e.IsAura) au.Amount[i] = CalcValue(s, e, _player.Level); }
            _me.Auras.Add(au);
            _aurasChanged = true;
            EmitAuraApplied(au, _me);
        }

        private void RemoveGhostAuras()
        {
            // ResurrectPlayer: RemoveAurasDueToSpell(20584) "speed bonuses"; RemoveAurasDueToSpell(8326) "SPELL_AURA_GHOST"
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                int id = _me.Auras[a].Spell.Id;
                if (id == GhostSpell || id == WispGhostSpell) RemoveAuraAt(_me, a);
            }
        }

        // ------------------------------------------------------------------ per step while dead (the StepPlayer hook)

        /// <summary>
        /// Every step while the player is dead: the automatic release after 6 minutes (Player::Update's m_deathTimer), and for a ghost the
        /// reclaim delay, "within 39 yd of the corpse" (CanReclaim, CorpseReclaimable events) and "next to the spirit healer" (NearSpiritHealer).
        /// </summary>
        private void StepDeath(float dt)
        {
            _deathStepAt = (float)_time;
            if (!_player.Dead) return;
            if (!_player.Ghost)
            {
                if (_releaseLeft > 0f)
                {
                    _releaseLeft -= dt;
                    if (_releaseLeft <= 0f) { _releaseLeft = -1f; ReleaseToGhost("automatically after 6 minutes, AC m_deathTimer"); }
                }
                return;
            }

            // The reclaim delay (HandleReclaimCorpseOpcode: GhostTime + GetCorpseReclaimDelay() must have passed).
            double ready = _ghostTime + CorpseReclaimDelayNow();
            _player.ReclaimDelay = (float)Math.Max(0.0, ready - _time);
            float d = _player.HasCorpse ? Dist(_me.Pos, _player.CorpsePos) : float.MaxValue;
            bool inReach = _player.HasCorpse && _player.CorpseMapId == _zone.MapId && d <= CorpseReclaimRadius;
            bool can = inReach && _player.ReclaimDelay <= 0f;
            if (can != _player.CanReclaim)
            {
                _player.CanReclaim = can;
                Emit(GameEventType.CorpseReclaimable, PlayerGuid, PlayerGuid, can ? 1 : 0, 0, false, null);
                if (can || _reclaimLogged)
                    WowLog.Info("OfflineSession: the corpse is " + (can ? "in reach (" + d.ToString("0") + " yd, delay over) - Resurrect offered" : "out of reach again (" + d.ToString("0") + " yd)") + ".");
                _reclaimLogged = true;
            }

            // The spirit healer: talked to (targeted) and within the gossip distance (StaticPopup XP_LOSS hides once CheckSpiritHealerDist fails).
            bool near = false;
            var h = _spiritHealer;
            if (h != null && h.Present && _target == h.Guid) near = Dist(_me.Pos, h.Pos) <= InteractRange + h.CombatReach;
            if (near != _player.NearSpiritHealer)
            {
                _player.NearSpiritHealer = near;
                if (near || _healerLogged)
                    WowLog.Info("OfflineSession: " + (near ? "the spirit healer offers his resurrection (targeted, " + Dist(_me.Pos, h.Pos).ToString("0.0") + " yd)" : "left the spirit healer") + ".");
                _healerLogged = true;
            }
        }

        // ------------------------------------------------------------------ IWowSession: coming back

        /// <summary>WorldSession::HandleReclaimCorpseOpcode (MiscHandler.cpp:633-667): a ghost within 39 yd of its corpse after the delay -> alive at 50%.</summary>
        public void ReclaimCorpse()
        {
            if (!_player.Dead || !_player.Ghost || !_player.HasCorpse) return;   // "body not released yet" / no corpse: silently, like the server
            double ready = _ghostTime + CorpseReclaimDelayNow();
            if (ready > _time) { WowLog.Info("OfflineSession: Resurrect refused - the reclaim delay has " + (ready - _time).ToString("0") + " s left."); return; }
            float d = Dist(_me.Pos, _player.CorpsePos);
            if (_player.CorpseMapId != _zone.MapId || d > CorpseReclaimRadius) { WowLog.Info("OfflineSession: Resurrect refused - the corpse is " + d.ToString("0") + " yd away (radius " + CorpseReclaimRadius + ")."); return; }
            ResurrectPlayer(ResurrectPercent, false, "at the corpse");
        }

        /// <summary>WorldSession::HandleSpiritHealerActivateOpcode + SendSpiritResurrect (NPCHandler.cpp:238-290): alive at 50% with Resurrection Sickness.</summary>
        public void AcceptSpiritHealer()
        {
            if (!_player.Dead || !_player.Ghost) return;
            var h = _spiritHealer;
            // GetNPCIfCanInteractWith(guid, UNIT_NPC_FLAG_SPIRITHEALER): the healer must be there and within the interaction distance.
            if (h == null || !h.Present || Dist(_me.Pos, h.Pos) > InteractRange + h.CombatReach)
            {
                WowLog.Info("OfflineSession: spirit healer resurrection refused - not next to the spirit healer.");
                return;
            }
            ResurrectPlayer(ResurrectPercent, true, "by the spirit healer");
            // SendSpiritResurrect: DurabilityLossAll(DurabilityLoss.OnSpiritResurrect 25%) - offline items have no durability (Known gaps);
            // the corpse's nearest graveyard is this zone's only one, so no teleport.
        }

        /// <summary>Player::ResurrectPlayer (Player.cpp:4556-4636) + SpawnCorpseBones.</summary>
        private void ResurrectPlayer(float restorePercent, bool applySickness, string how)
        {
            bool couldReclaim = _player.CanReclaim;
            RemoveGhostAuras();
            _player.Dead = false;
            _player.Ghost = false;
            _releaseLeft = -1f;
            RecalcStats();
            if (restorePercent > 0f)
            {
                _me.Health = Math.Max(1, (int)(_me.MaxHealth * restorePercent));
                _mana = (int)(_maxMana * restorePercent);
                _rage = 0f;
                _energy = 100f * restorePercent;   // SetPower(POWER_ENERGY, GetMaxPower(POWER_ENERGY) * restore_percent), max energy 100 offline
            }
            _player.HasCorpse = false;                     // SpawnCorpseBones
            _player.CanReclaim = false;
            _player.NearSpiritHealer = false;
            _player.ReclaimDelay = 0f;
            _deathPosKnown = false;
            if (couldReclaim) Emit(GameEventType.CorpseReclaimable, PlayerGuid, PlayerGuid, 0, 0, false, null);
            HideSpiritHealer();
            string sick = "no Resurrection Sickness";
            if (applySickness)
            {
                int secs = SicknessSeconds(_player.Level);
                if (secs > 0) { ApplySickness(secs); sick = "Resurrection Sickness " + ResurrectionSicknessSpell + " for " + secs + " s"; }
                else sick = "no Resurrection Sickness below level " + SicknessLevel;
            }
            _statsDirty = true;
            Emit(GameEventType.PlayerResurrected, PlayerGuid, PlayerGuid, _me.Health, 0, false, null);
            WowLog.Info("OfflineSession: resurrected " + how + " - " + _me.Health + "/" + _me.MaxHealth + " health (" + (int)(restorePercent * 100f) + "%), " + sick
                + (applySickness ? "; the spirit healer's 25% durability loss is not modelled offline" : "") + ".");
        }

        /// <summary>
        /// Player.cpp:4613-4634: "Characters from level 1-10 are not affected by resurrection sickness. Characters from level 11-19 will suffer
        /// from one minute of sickness for each level they are above 10. Characters level 20 and up suffer from ten minutes of sickness."
        /// (delta = (level - startLevel + 1) * MINUTE below startLevel + 9; else the spell's 10 minutes.) 0 = none.
        /// </summary>
        public static int SicknessSeconds(int level)
        {
            if (level < SicknessLevel) return 0;
            if (level < SicknessLevel + 9) return (level - SicknessLevel + 1) * 60;
            return 600;
        }

        private void ApplySickness(int seconds)
        {
            var s = _db.Find(ResurrectionSicknessSpell);
            if (s == null) { WowLog.Warn("OfflineSession: spell 15007 is not in Spell.dbc - no Resurrection Sickness."); return; }
            ApplySpellAuras(s, _me, _me, 0);
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                if (au.Spell.Id != ResurrectionSicknessSpell) continue;
                // aur->SetDuration(delta * IN_MILLISECONDS); a debuff in WoW's buff frame (-75% attributes, -75% damage).
                au.Duration = seconds;
                au.Remaining = seconds;
                au.Harmful = true;
                au.View.Harmful = true;
                au.View.Duration = seconds;
                au.View.Remaining = seconds;
                _aurasChanged = true;
                _statsDirty = true;
                break;
            }
        }

        // ------------------------------------------------------------------ the spirit healer (visible to ghosts only)

        private void ShowSpiritHealer(WowPos grave)
        {
            if (_spiritHealer == null)
            {
                // "Spirit Healer": the client's own name for it (GlobalStrings.lua:7330 SPIRIT_HEALER_RELEASE_RED "|cffff2020Spirit Healer|r").
                var tpl = new CreatureTemplate
                {
                    Entry = SpiritHealerEntry, Name = "Spirit Healer", SubName = "", DisplayIds = new[] { SpiritHealerDisplay },
                    MinLevel = 1, MaxLevel = 1, Reaction = Reaction.Friendly, IsNpc = false, UnitClass = 2, CreatureType = 0
                };
                var c = new OfflineCreature();
                c.Tpl = tpl;
                c.Spawn = new SpawnDef { Entry = SpiritHealerEntry, Pos = grave, WanderRadius = 0f, RespawnSeconds = 0f };
                c.SpawnIndex = 1;
                c.Guid = (0xF130UL << 48) | ((ulong)(uint)SpiritHealerEntry << 24) | 1UL;
                c.State = new EntityState();
                c.State.Guid = c.Guid;
                c.State.Entry = SpiritHealerEntry;
                c.State.Kind = EntityKind.Creature;   // a friendly creature: a click targets it (Kind Npc would route to Interact, which refuses the dead)
                c.State.Name = tpl.Name;
                c.State.SubName = "";
                c.State.Reaction = Reaction.Friendly;
                c.State.Scale = 1f;
                c.State.DisplayId = SpiritHealerDisplay;
                c.BaseDisplayId = SpiritHealerDisplay;
                c.CombatReach = 1.5f;
                c.Present = false;
                _creatures.Add(c);
                _byGuid[c.Guid] = c;
                _spiritHealer = c;
            }
            var h = _spiritHealer;
            // Ours: SpiritHealerAhead yards in front of the graveyard point, facing back at the arriving ghost.
            var at = new WowPos(grave.X + (float)Math.Cos(grave.O) * SpiritHealerAhead, grave.Y + (float)Math.Sin(grave.O) * SpiritHealerAhead, grave.Z, grave.O + (float)Math.PI);
            at.Z = Ground(at.X, at.Y, at.Z);
            h.Home = at;
            h.Spawn.Pos = at;
            h.Tpl.MinLevel = h.Tpl.MaxLevel = Math.Max(1, _player.Level);   // ours: a non-combat helper shown at the player's level
            Respawn(h);
            h.RespawnTimer = float.MaxValue;
            _entitiesDirty = true;
        }

        private void HideSpiritHealer()
        {
            var h = _spiritHealer;
            if (h == null || !h.Present) return;
            h.Present = false;
            h.RespawnTimer = float.MaxValue;   // StepCreature respawns absent creatures when their timer runs out: never for this one
            if (_target == h.Guid) SetTarget(0);
            _entitiesDirty = true;
        }
    }
}
