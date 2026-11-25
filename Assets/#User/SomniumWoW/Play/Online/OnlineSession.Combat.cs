using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Stage O2, owner combat (PLAN9 s2): melee auto-attack against AzerothCore, every unit's swings, XP, levels, kills and the
    // player's death - filling the SAME PlayerState / EntityState fields and GameEvents the offline game fills
    // (Play/Offline/OfflineSession.Combat.cs), so the existing animations, sounds and combat text play online.
    //
    // The server is the only truth (PLAN9 decision 1): the client only asks (CMSG_ATTACKSWING / CMSG_ATTACKSTOP) and shows what the
    // server reports. Protocol = AzerothCore master (copies in P/acsrc/Unit.cpp, P/acsrc/o2combat/*):
    //   CMSG_ATTACKSWING   uint64 victim                  CombatHandler.cpp 28-66 (HandleAttackSwingOpcode; invalid -> SMSG_ATTACKSTOP)
    //   CMSG_ATTACKSTOP    (empty)                        CombatHandler.cpp 68-71 (HandleAttackStopOpcode -> Unit::AttackStop)
    //   SMSG_ATTACKSTART   uint64 attacker, uint64 victim Unit.cpp 3251-3261 (SendMeleeAttackStart)
    //   SMSG_ATTACKSTOP    packed attacker, packed victim, uint32 nowDead   CombatPackets.cpp 25-34 (SAttackStop::Write)
    //   SMSG_ATTACKERSTATEUPDATE                          Unit.cpp 6664-6749 (SendAttackStateUpdate), flags below
    //   SMSG_ATTACKSWING_NOTINRANGE / BADFACING / DEADTARGET / CANT_ATTACK, SMSG_CANCEL_COMBAT: empty   PlayerMisc.cpp 80-152
    //   SMSG_LOG_XPGAIN                                   Player.cpp 2385-2402 (SendLogXPGain)
    //   SMSG_LEVELUP_INFO  uint32 level, uint32 healthDelta, uint32 power[7], uint32 stat[5]   MiscPackets.cpp 34-46 + .h 44-55
    //   SMSG_PARTYKILLLOG  uint64 killer, uint64 victim   Unit.cpp 13629-13632 (Unit::Kill)
    //   SMSG_AI_REACTION   uint64 unit, uint32 reaction   Creature.cpp 2483-2494 (SendAIReaction), Unit.cpp 12623-12634 (pets)
    //
    // Events follow the offline semantics: every melee result that involves the player or the player's pet/totem/guardian becomes
    // a MeleeHit / MeleeMiss / MeleeDodge / MeleeParry / MeleeBlock / Absorb / SpellMiss (Evade, Immune) event (WowEntityViews shows
    // them as floating combat text, like WoW which floats only your own and your pet's numbers); EVERY unit's swing steps the
    // attacker's AttackCounter and a damaging hit the victim's WoundCounter, so creatures and other players animate and play their
    // attack / wound sounds for all fights around (the user's report: "enemies do not do any attack animations or sound").
    public sealed partial class OnlineSession
    {
        // HitInfo (AC Unit.h 96-125)
        const uint HitInfoDebug = 0x00000001, HitInfoOffhand = 0x00000004, HitInfoMiss = 0x00000010,
                   HitInfoFullAbsorb = 0x00000020, HitInfoPartialAbsorb = 0x00000040, HitInfoFullResist = 0x00000080, HitInfoPartialResist = 0x00000100,
                   HitInfoCritical = 0x00000200, HitInfoBlock = 0x00002000, HitInfoNoAnimation = 0x00040000,
                   HitInfoRageGain = 0x00800000, HitInfoFakeDamage = 0x01000000;
        // VictimState (AC Unit.h 83-94)
        const int VictimIntact = 0, VictimHit = 1, VictimDodge = 2, VictimParry = 3, VictimBlocks = 5, VictimEvades = 6, VictimImmune = 7, VictimDeflects = 8;
        // AiReaction (AC SharedDefines.h 3471-3478): AI_REACTION_HOSTILE "sent on every attack, triggers aggro sound (used in client packet handler)"
        const uint AiReactionHostile = 2;
        // Ours: how long a hit / kill log still names the killer of a unit that dies afterwards (seconds).
        const double CombatKillerMemory = 10.0;
        // Ours: a CMSG_ATTACKSWING / CMSG_ATTACKSTOP still waits for its SMSG_ATTACKSTART / SMSG_ATTACKSTOP answer this long (seconds).
        const double CombatAnswerWindow = 3.0;
        // Ours: no SMSG_ATTACKSTART this long after CMSG_ATTACKSWING = the server did not start the attack (Unit::Attack returns false
        // without a packet for a dead / evading / GM victim, Unit.cpp 7080-7120): the local auto-attack ends (seconds).
        const double CombatStartTimeout = 3.0;

        /// <summary>Per unit in view: what the death detection and the killer attribution need.</summary>
        sealed class UnitCombat
        {
            public bool WasDead, DiedSent;
            public ulong LastAttacker, Killer;
            public double LastHitAt = -1000, KillAt = -1000;
        }

        readonly Dictionary<ulong, UnitCombat> _unitCombat = new Dictionary<ulong, UnitCombat>();
        readonly List<ulong> _combatScratch = new List<ulong>();
        readonly HashSet<ushort> _combatOddLogged = new HashSet<ushort>();
        ulong _attackGuid;                 // victim of our auto-attack (CMSG_ATTACKSWING sent / SMSG_ATTACKSTART confirmed)
        ulong _awaitStartFor;              // CMSG_ATTACKSWING sent, no SMSG_ATTACKSTART for us yet (an SMSG_ATTACKSTOP now = refused)
        double _awaitStartAt;
        // Our CMSG_ATTACKSWING (true) / CMSG_ATTACKSTOP (false) in the order sent, waiting for the server's answer (it answers in order:
        // SWING -> SMSG_ATTACKSTART, or SMSG_ATTACKSTOP when refused, or nothing when Unit::Attack returns false; STOP -> SMSG_ATTACKSTOP when
        // it was attacking). An answer with later requests still outstanding is stale: the local state already shows the later request.
        readonly List<bool> _attackReqSwing = new List<bool>();
        readonly List<double> _attackReqAt = new List<double>();
        ulong _seenTarget;                 // the selection last frame (auto-attack follows a new selection, like offline SetTarget)
        double _lastMainSwingAt = -1000;   // our last main-hand swing (SwingProgress)
        bool _selfKnown, _selfWasDead, _playerDiedSent, _wasInCombatForLog;
        ulong _lastAttackerOfMe;
        double _lastHitMeAt = -1000, _unitCombatPruneAt;
        int _combatPackets, _combatOdd, _swingsMine, _hitsMine, _swingsAtMe, _swingsOthers, _kills, _xpGained;

        /// <summary>For probes and diagnostics: combat packets parsed and parsed with an odd layout (logged once per opcode).</summary>
        public int CombatPacketsParsed { get { return _combatPackets; } }
        public int CombatParseProblems { get { return _combatOdd; } }

        void InitCombat()
        {
            if (_world == null) return;
            _world.On(Op.SMSG_ATTACKSTART, OnCombatAttackStart, this);
            _world.On(Op.SMSG_ATTACKSTOP, OnCombatAttackStop, this);
            _world.On(Op.SMSG_ATTACKERSTATEUPDATE, OnCombatStateUpdate, this);
            _world.On(Op.SMSG_ATTACKSWING_NOTINRANGE, p => OnCombatSwingError(p, "You are too far away!", false), this);        // ERR_BADATTACKPOS (GlobalStrings.lua:2946)
            _world.On(Op.SMSG_ATTACKSWING_BADFACING, p => OnCombatSwingError(p, "You are facing the wrong way!", false), this);  // ERR_BADATTACKFACING (GlobalStrings.lua:2945)
            _world.On(Op.SMSG_ATTACKSWING_DEADTARGET, p => OnCombatSwingError(p, "Your target is dead", true), this);            // SPELL_FAILED_TARGETS_DEAD (GlobalStrings.lua:7166)
            _world.On(Op.SMSG_ATTACKSWING_CANT_ATTACK, p => OnCombatSwingError(p, "You cannot attack that target.", true), this); // ERR_INVALID_ATTACK_TARGET (GlobalStrings.lua:3136)
            _world.On(Op.SMSG_CANCEL_COMBAT, OnCombatCancel, this);
            _world.On(Op.SMSG_LOG_XPGAIN, OnCombatXpGain, this);
            _world.On(Op.SMSG_LEVELUP_INFO, OnCombatLevelUp, this);
            _world.On(Op.SMSG_PARTYKILLLOG, OnCombatPartyKill, this);
            _world.On(Op.SMSG_AI_REACTION, OnCombatAiReaction, this);
            WowLog.Info("Online combat: melee auto-attack, every unit's swings (SMSG_ATTACKERSTATEUPDATE), XP, levels, kills and death from the server; ours: killer memory "
                        + CombatKillerMemory + " s, answer window " + CombatAnswerWindow + " s, start timeout " + CombatStartTimeout + " s, log budget " + CombatLogBudget + " lines per kind");
        }

        void CombatEmit(GameEventType type, ulong source, ulong target, int amount, int spellId, bool crit, string text)
        {
            _events.Enqueue(new GameEvent { Type = type, SourceGuid = source, TargetGuid = target, Amount = amount, SpellId = spellId, Crit = crit, Text = text });
        }

        // Log budgets per kind of line (ours): 0 attack state, 1 swing results, 2 XP, 3 kills, 4 combat summaries.
        const int CombatLogBudget = 40;
        readonly int[] _combatLogLines = new int[5];

        void CombatLog(int kind, string line)
        {
            if (_combatLogLines[kind]++ < CombatLogBudget) WowLog.Info("Online combat: " + line);
        }

        void CombatOdd(WowPacket p, string what)
        {
            _combatOdd++;
            if (_combatOddLogged.Add(p.Opcode))
                WowLog.Warn("Online combat: " + p.Name + " (" + p.Length + " bytes) " + what + " - handled as far as read (logged once per opcode)");
        }

        string CombatName(ulong guid)
        {
            if (guid == 0) return "nobody";
            if (guid == _player.Guid) return "me";
            var e = Find(guid);
            return (e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : "?") + " (" + guid.ToString("X") + ")";
        }

        /// <summary>The player, or a unit the player owns (pet, totem, guardian): its melee results become events (offline semantics).</summary>
        bool CombatMine(ulong guid)
        {
            if (guid == 0 || _player.Guid == 0) return false;
            if (guid == _player.Guid) return true;
            var e = Find(guid);
            return e != null && e.OwnerGuid == _player.Guid;
        }

        UnitCombat CombatUnit(ulong guid)
        {
            UnitCombat u;
            if (!_unitCombat.TryGetValue(guid, out u))
            {
                u = new UnitCombat();
                var e = Find(guid);
                u.WasDead = e != null && e.Dead;
                u.DiedSent = u.WasDead;
                _unitCombat[guid] = u;
            }
            return u;
        }

        /// <summary>A unit could be attacked from the client's point of view (the real client checks this before CMSG_ATTACKSWING;
        /// the server decides the rest - IsValidAttackTarget in HandleAttackSwingOpcode).</summary>
        bool CombatAttackable(EntityState e)
        {
            return e != null && e.Guid != _player.Guid && e.Kind != EntityKind.GameObject && e.Reaction != Reaction.Friendly && !e.Dead;
        }

        // ------------------------------------------------------------------ IWowSession: attacking

        int _autoDismountSaid;   // iteration 11 (combat feel): log rate limit

        public void StartAttack(ulong guid)
        {
            if (_player.Dead) { Error("Can't attack while dead."); return; }                                          // ERR_ATTACK_DEAD (GlobalStrings.lua:2913)
            // AC Unit.cpp 7087-7089 "// player cannot attack in mount state" - the server refuses without a packet, so the client says it.
            // Iteration 11 (combat feel): on a ground mount the client's CVar "autoDismount" (Wow.exe 12340 registers "Automatically dismount
            // when needed" with default "1"; "autoDismountFlying" default "0") leaves the mount first: CMSG_CANCEL_MOUNT_AURA, then the swing
            // below. AC processes one session's packets in arrival order (WorldSession::Update / LockedQueue::next stops at the first packet
            // its filter defers); HandleCancelMountAuraOpcode (MiscHandler.cpp 1475-1494) runs Dismount() -> RemoveUnitFlag(UNIT_FLAG_MOUNT)
            // (Unit.cpp 10323-10329) at once, so Unit::Attack's IsMounted() test no longer refuses. Flying in the air / a taxi: still refused.
            if (_player.OnTaxi || (_player.Mounted && _motion.Driven && _motion.Flying)) { Error("Can't attack while mounted."); return; }   // ERR_ATTACK_MOUNTED (GlobalStrings.lua:2915)
            if (guid == 0) { Error("There is nothing to attack."); return; }                                         // ERR_NO_ATTACK_TARGET (GlobalStrings.lua:3322)
            var e = Find(guid);
            if (e == null || guid == _player.Guid || e.Kind == EntityKind.GameObject || e.Reaction == Reaction.Friendly)
            {
                Error("You cannot attack that target.");                                                             // ERR_INVALID_ATTACK_TARGET (GlobalStrings.lua:3136)
                return;
            }
            if (_target != guid) SetTarget(guid);
            _seenTarget = _target;
            if (e.Dead) { Error("Your target is dead"); return; }                                                    // SPELL_FAILED_TARGETS_DEAD (GlobalStrings.lua:7166)
            if (_player.AutoAttacking && _attackGuid == guid) return;                                                // already swinging at it (offline: return)
            if (_player.Mounted)
            {
                if (_autoDismountSaid++ < 20 || _autoDismountSaid % 50 == 0) WowLog.Info("Mount: auto dismount to attack " + e.Name + " (CMSG_CANCEL_MOUNT_AURA before CMSG_ATTACKSWING; the client's autoDismount, default on)");
                if (_now - _dismountAskedAt > 1.0) Dismount();   // a cast's auto dismount this instant (SpCast, on-next-swing) already asked
            }
            bool was = _player.AutoAttacking;
            if (_player.Sheath != SheathState.Melee) SetSheath(SheathState.Melee);                                   // offline draws the weapons too; CMSG_SET_SHEATHED
            FlushFacing(guid);                              // iteration 11 (motion): the facing reaches the server before the swing request
            if (_world != null) _world.Send(Op.CMSG_ATTACKSWING, new PacketWriter().U64(guid));
            CombatRequest(true);
            _awaitStartFor = guid;
            _awaitStartAt = _now;
            _attackGuid = guid;
            _player.AutoAttacking = true;
            if (!was) CombatEmit(GameEventType.AttackStarted, _player.Guid, guid, 0, 0, false, null);
            CombatLog(0, "CMSG_ATTACKSWING " + CombatName(guid) + (was ? " (switched target)" : "") + ", " + e.Reaction + ", level " + e.Level);
        }

        public void StopAttack()
        {
            if (!_player.AutoAttacking) return;
            if (_world != null) _world.Send(Op.CMSG_ATTACKSTOP);
            CombatRequest(false);
            CombatLog(0, "CMSG_ATTACKSTOP (" + CombatName(_attackGuid) + ")");
            EndAutoAttack();
        }

        /// <summary>Our auto-attack ends locally: AttackStopped like offline StopAttack (SourceGuid = the player, TargetGuid = the victim).</summary>
        void EndAutoAttack()
        {
            if (!_player.AutoAttacking) return;
            ulong victim = _attackGuid != 0 ? _attackGuid : _target;
            _player.AutoAttacking = false;
            _attackGuid = 0;
            _awaitStartFor = 0;
            CombatEmit(GameEventType.AttackStopped, _player.Guid, victim, 0, 0, false, null);
        }

        void CombatRequest(bool swing)
        {
            _attackReqSwing.Add(swing);
            _attackReqAt.Add(_now);
        }

        /// <summary>Matches a server answer to our requests: 0 = none waiting (the server acted on its own), 1 = it answers the latest
        /// request, 2 = it answers an older one while later requests are still outstanding (stale). SMSG_ATTACKSTART answers up to and
        /// including the oldest CMSG_ATTACKSWING; SMSG_ATTACKSTOP answers the oldest request of either kind. popped = the kind answered.</summary>
        int CombatAnswer(bool start, out bool poppedSwing)
        {
            poppedSwing = false;
            if (_attackReqSwing.Count == 0) return 0;
            if (start)
            {
                int i = _attackReqSwing.IndexOf(true);
                if (i < 0) { _attackReqSwing.Clear(); _attackReqAt.Clear(); return 0; }
                _attackReqSwing.RemoveRange(0, i + 1);
                _attackReqAt.RemoveRange(0, i + 1);
                poppedSwing = true;
            }
            else
            {
                poppedSwing = _attackReqSwing[0];
                _attackReqSwing.RemoveAt(0);
                _attackReqAt.RemoveAt(0);
            }
            return _attackReqSwing.Count == 0 ? 1 : 2;
        }

        /// <summary>
        /// For the spells owner (request in P/reports9/combat.md): an on-next-swing spell (Heroic Strike, Raptor Strike, Maul) replaces
        /// the white swing - AC AttackerStateUpdate casts it instead of sending SMSG_ATTACKERSTATEUPDATE - so its SMSG_SPELL_GO should
        /// step the swing here: the attacker's attack animation and our swing timer, as offline (StepMelee counts it too).
        /// </summary>
        internal void CombatMainHandSwing(ulong attacker)
        {
            if (attacker == 0) return;
            if (attacker == _player.Guid) { _player.AttackCounter++; _lastMainSwingAt = _now; return; }
            var e = Find(attacker);
            if (e != null) e.AttackCounter++;
        }

        /// <summary>For the spells owner (request): spell damage names the killer of a unit / of the player that dies right after.</summary>
        internal void CombatNoteDamage(ulong attacker, ulong victim)
        {
            if (attacker == 0 || victim == 0) return;
            if (victim == _player.Guid) { _lastAttackerOfMe = attacker; _lastHitMeAt = _now; return; }
            if (Find(victim) == null) return;
            var u = CombatUnit(victim);
            u.LastAttacker = attacker;
            u.LastHitAt = _now;
        }

        // ------------------------------------------------------------------ server: attack state

        /// <summary>SMSG_ATTACKSTART: uint64 attacker, uint64 victim (Unit::SendMeleeAttackStart, Unit.cpp 3251-3261).</summary>
        void OnCombatAttackStart(WowPacket p)
        {
            ulong attacker, victim;
            try { attacker = p.U64(); victim = p.U64(); }
            catch (PacketTooShortException) { CombatOdd(p, "too short"); return; }
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes left over");
            if (attacker != _player.Guid) return;                  // others: their swings (SMSG_ATTACKERSTATEUPDATE) animate them
            bool swing;
            if (CombatAnswer(true, out swing) == 2)
            {
                CombatLog(0, "SMSG_ATTACKSTART on " + CombatName(victim) + " answers an older request (a later one is still waiting) - ignored");
                return;
            }
            _awaitStartFor = 0;
            _attackGuid = victim;
            if (!_player.AutoAttacking)
            {
                _player.AutoAttacking = true;                      // the server started it (a spell that starts the attack, a login in combat...)
                CombatEmit(GameEventType.AttackStarted, _player.Guid, victim, 0, 0, false, null);
            }
            CombatLog(0, "SMSG_ATTACKSTART - the server swings at " + CombatName(victim));
        }

        /// <summary>SMSG_ATTACKSTOP: packed attacker, packed victim (0 = none), uint32 nowDead (SAttackStop::Write, CombatPackets.cpp 25-34).</summary>
        void OnCombatAttackStop(WowPacket p)
        {
            ulong attacker, victim;
            uint nowDead = 0;
            try
            {
                attacker = p.PackedGuid();
                victim = p.PackedGuid();
                if (p.Remaining >= 4) nowDead = p.U32(); else CombatOdd(p, "without the nowDead field");
            }
            catch (PacketTooShortException) { CombatOdd(p, "too short"); return; }
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes left over");
            if (attacker != _player.Guid) return;
            bool swing;
            int answer = CombatAnswer(false, out swing);
            if (answer == 2) return;                               // an older request's answer: the local state shows a later request
            if (answer == 1 && !swing) return;                    // the answer to our own CMSG_ATTACKSTOP: stopped locally already
            if (!_player.AutoAttacking) return;
            bool refused = answer == 1 && swing;                  // HandleAttackSwingOpcode: unknown / invalid target -> SendMeleeAttackStop
            CombatLog(0, "SMSG_ATTACKSTOP - the server stopped our attack on " + CombatName(victim) + (refused ? " (refused)" : "") + (nowDead != 0 ? ", nowDead" : ""));
            EndAutoAttack();
            if (refused && !_player.Dead) Error("You cannot attack that target.");                                   // ERR_INVALID_ATTACK_TARGET (GlobalStrings.lua:3136)
        }

        /// <summary>SMSG_ATTACKSWING_NOTINRANGE / BADFACING / DEADTARGET / CANT_ATTACK (empty; PlayerMisc.cpp 80-152). The server sends the
        /// first two once per episode while it keeps trying (PlayerUpdates.cpp 172-190: m_swingErrorMsg); AC master never sends the last two
        /// (no caller of SendAttackSwingDeadTarget / CantAttack in Unit, Player*, Spell*, Creature, Pet*, *Handler.cpp) - handled anyway.</summary>
        void OnCombatSwingError(WowPacket p, string text, bool ends)
        {
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes (expected empty)");
            Error(text);
            CombatLog(0, p.Name + " -> '" + text + "'");
            if (ends) EndAutoAttack();
        }

        /// <summary>SMSG_CANCEL_COMBAT (empty): "melee and ranged forced attack cancel" (Unit::CombatStop, Unit.cpp 7281; PlayerMisc.cpp 142-146).</summary>
        void OnCombatCancel(WowPacket p)
        {
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes (expected empty)");
            if (!_player.AutoAttacking) return;
            CombatLog(0, "SMSG_CANCEL_COMBAT - auto-attack off");
            EndAutoAttack();
        }

        // ------------------------------------------------------------------ server: every swing

        /// <summary>
        /// SMSG_ATTACKERSTATEUPDATE (Unit::SendAttackStateUpdate, Unit.cpp 6664-6749), read by its flags and counts:
        /// uint32 HitInfo, packed attacker, packed target, uint32 full damage, uint32 overkill, uint8 count,
        /// count x (uint32 school, float damage, uint32 damage), [HitInfo & (FULL|PARTIAL)_ABSORB: count x uint32 absorb],
        /// [HitInfo & (FULL|PARTIAL)_RESIST: count x uint32 resist], uint8 TargetState, uint32 unknown, uint32 melee spell id,
        /// [HitInfo & BLOCK: uint32 blocked], [HitInfo & RAGE_GAIN: uint32], [HitInfo & DEBUG: uint32, 11 floats, uint32].
        /// Outcomes as Unit::CalculateMeleeDamage (Unit.cpp 1678-1960) sets them: miss = HITINFO_MISS + INTACT, evade = MISS + EVADES,
        /// dodge / parry = DODGE / PARRY, block = HIT + HITINFO_BLOCK (a full block: BLOCKS), immune = IS_IMMUNE.
        /// </summary>
        void OnCombatStateUpdate(WowPacket p)
        {
            uint hit;
            ulong attacker, victim;
            int damage, absorb = 0, resist = 0, state = VictimHit, blocked = 0;
            try
            {
                hit = p.U32();
                attacker = p.PackedGuid();
                victim = p.PackedGuid();
                damage = (int)Math.Min(p.U32(), (uint)int.MaxValue);
                p.U32();                                           // overkill
                int count = p.U8();
                if (count > 8) { CombatOdd(p, "sub-damage count " + count); return; }
                for (int i = 0; i < count; i++) { p.U32(); p.F32(); p.U32(); }          // school mask, float damage, uint32 damage
                if ((hit & (HitInfoFullAbsorb | HitInfoPartialAbsorb)) != 0) for (int i = 0; i < count; i++) absorb += (int)p.U32();
                if ((hit & (HitInfoFullResist | HitInfoPartialResist)) != 0) for (int i = 0; i < count; i++) resist += (int)p.U32();
                state = p.U8();
                p.U32();                                           // "Unknown attackerstate"
                p.U32();                                           // "Melee spellid" (always 0 in AC)
                if ((hit & HitInfoBlock) != 0) blocked = (int)p.U32();
                if ((hit & HitInfoRageGain) != 0) p.U32();
                if ((hit & HitInfoDebug) != 0) { p.U32(); for (int i = 0; i < 11; i++) p.F32(); p.U32(); }
            }
            catch (PacketTooShortException) { CombatOdd(p, "too short for its flags"); return; }
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes left over (HitInfo 0x" + hit.ToString("X") + ")");
            ApplySwing(hit, attacker, victim, damage, absorb, resist, state, blocked);
        }

        void ApplySwing(uint hit, ulong attacker, ulong victim, int damage, int absorb, int resist, int state, int blocked)
        {
            ulong me = _player.Guid;
            bool offhand = (hit & HitInfoOffhand) != 0;
            bool missed = (hit & HitInfoMiss) != 0 || state == VictimIntact || state == VictimEvades || state == VictimImmune
                          || state == VictimDodge || state == VictimParry || state == VictimDeflects;

            // animations for everyone (EntityState / PlayerState counters, offline: +1 per swing, +1 per damaging hit)
            if ((hit & HitInfoNoAnimation) == 0)
            {
                if (attacker == me && me != 0)
                {
                    if (!offhand) { _player.AttackCounter++; _lastMainSwingAt = _now; }   // off-hand swings: events only (GearAnimLocal counts them)
                }
                else
                {
                    var a = Find(attacker);
                    if (a != null) a.AttackCounter++;
                }
            }
            bool wounded = !missed && state != VictimBlocks && (damage > 0 || (hit & HitInfoFakeDamage) != 0);
            if (wounded)
            {
                if (victim == me && me != 0) _player.WoundCounter++;
                else
                {
                    var v = Find(victim);
                    if (v != null) v.WoundCounter++;
                }
            }
            if (damage > 0 || absorb > 0)
            {
                if (victim == me) { _lastAttackerOfMe = attacker; _lastHitMeAt = _now; }
                else if (victim != 0 && Find(victim) != null) { var u = CombatUnit(victim); u.LastAttacker = attacker; u.LastHitAt = _now; }
            }

            if (attacker == me) { _swingsMine++; if (damage > 0) _hitsMine++; }
            else if (victim == me) _swingsAtMe++;
            else _swingsOthers++;

            // events (offline semantics): only fights of the player or the player's own units
            if (!CombatMine(attacker) && !CombatMine(victim)) return;
            bool crit = (hit & HitInfoCritical) != 0;
            string outcome;
            if (state == VictimEvades) { CombatEmit(GameEventType.SpellMiss, attacker, victim, 0, 0, false, "Evade"); outcome = "Evade"; }        // COMBAT_TEXT_EVADE (GlobalStrings.lua:1753)
            else if (state == VictimImmune) { CombatEmit(GameEventType.SpellMiss, attacker, victim, 0, 0, false, "Immune"); outcome = "Immune"; } // COMBAT_TEXT_IMMUNE (1756)
            else if (state == VictimDodge) { CombatEmit(GameEventType.MeleeDodge, attacker, victim, 0, 0, false, "Dodge"); outcome = "Dodge"; }  // COMBAT_TEXT_DODGE (1752)
            else if (state == VictimParry) { CombatEmit(GameEventType.MeleeParry, attacker, victim, 0, 0, false, "Parry"); outcome = "Parry"; }  // COMBAT_TEXT_PARRY (1760)
            else if (state == VictimDeflects) { CombatEmit(GameEventType.MeleeMiss, attacker, victim, 0, 0, false, "Deflect"); outcome = "Deflect"; } // COMBAT_TEXT_DEFLECT (1751)
            else if (missed) { CombatEmit(GameEventType.MeleeMiss, attacker, victim, 0, 0, false, "Miss"); outcome = "Miss"; }                   // COMBAT_TEXT_MISS (1758)
            else if (state == VictimBlocks && damage <= 0) { CombatEmit(GameEventType.MeleeBlock, attacker, victim, 0, 0, false, "Block"); outcome = "Block"; } // COMBAT_TEXT_BLOCK (1749)
            else
            {
                // offline Absorb: SourceGuid 0, TargetGuid = the shielded unit, Amount = absorbed, Text "Absorb"; then the rest as a hit
                if (absorb > 0) CombatEmit(GameEventType.Absorb, 0, victim, absorb, 0, false, "Absorb");                                        // COMBAT_TEXT_ABSORB (1747)
                if (damage > 0) CombatEmit(GameEventType.MeleeHit, attacker, victim, damage, 0, crit, null);
                else if (resist > 0 && absorb <= 0) CombatEmit(GameEventType.MeleeMiss, attacker, victim, 0, 0, false, "Resist");            // COMBAT_TEXT_RESIST (1762)
                outcome = damage > 0 ? (crit ? "Crit " : "Hit ") + damage : absorb > 0 ? "Absorb" : "no damage";
            }
            bool firstMine = attacker == me && _swingsMine <= 3, firstAtMe = victim == me && _swingsAtMe <= 3;
            if (firstMine || firstAtMe)
                CombatLog(1, "SMSG_ATTACKERSTATEUPDATE " + CombatName(attacker) + " -> " + CombatName(victim) + ": " + outcome
                          + (offhand ? " (off-hand)" : "") + (absorb > 0 ? ", absorbed " + absorb : "") + (blocked > 0 ? ", blocked " + blocked : "")
                          + " (HitInfo 0x" + hit.ToString("X") + ", state " + state + ")");
        }

        // ------------------------------------------------------------------ server: XP, levels, kills, aggro

        /// <summary>SMSG_LOG_XPGAIN (Player::SendLogXPGain, Player.cpp 2385-2402): uint64 victim (0 = not a kill), uint32 given XP (with the
        /// rested / RaF bonus), uint8 type (0 kill, 1 other: quests via RewardQuest -> GiveXP(xp, nullptr)), [kill: uint32 XP without the
        /// bonus, float group rate], uint8 RaF.</summary>
        void OnCombatXpGain(WowPacket p)
        {
            ulong victim;
            uint given, basic;
            int type;
            try
            {
                victim = p.U64();
                given = p.U32();
                type = p.U8();
                basic = given;
                if (type == 0) { basic = p.U32(); p.F32(); }
                p.U8();                                            // recruit-a-friend bonus included
            }
            catch (PacketTooShortException) { CombatOdd(p, "too short"); return; }
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes left over (type " + type + ")");
            int amount = (int)Math.Min(given, (uint)int.MaxValue);
            _xpGained += amount;
            // offline GiveXp: XpGain Amount = the XP, TargetGuid = the killed unit (0 otherwise), QuestId (0 here: the server does not say)
            _events.Enqueue(new GameEvent { Type = GameEventType.XpGain, Amount = amount, TargetGuid = victim });
            CombatLog(2, "SMSG_LOG_XPGAIN +" + amount + " XP" + (type == 0 ? " for " + CombatName(victim) + " (" + basic + " without the rested bonus)" : " (not a kill)"));
        }

        /// <summary>SMSG_LEVELUP_INFO (Player::GiveLevel -> LevelUpInfo::Write, MiscPackets.cpp 34-46; MAX_POWERS 7, MAX_STATS 5 = 56 bytes).</summary>
        void OnCombatLevelUp(WowPacket p)
        {
            int level, health;
            var extra = new System.Text.StringBuilder();
            try
            {
                level = p.I32();
                health = p.I32();
                for (int i = 0; i < 12 && p.Remaining >= 4; i++)
                {
                    int v = p.I32();
                    if (i == 0) extra.Append(", mana ").Append(v >= 0 ? "+" : "").Append(v);
                    else if (i >= 7) extra.Append(i == 7 ? ", stats " : " ").Append(v >= 0 ? "+" : "").Append(v);
                }
            }
            catch (PacketTooShortException) { CombatOdd(p, "too short"); return; }
            _combatPackets++;
            if (p.Length != 56 || p.Remaining != 0) CombatOdd(p, "expected 56 bytes");
            // offline LevelUp: SourceGuid = the player, Amount = the new level, Text = the message (here WoW's own words)
            CombatEmit(GameEventType.LevelUp, _player.Guid, 0, level, 0, false, "Congratulations, you have reached level " + level + "!");  // LEVEL_UP (GlobalStrings.lua:4714)
            WowLog.Info("Online combat: SMSG_LEVELUP_INFO level " + level + " (health +" + health + extra + ")");
        }

        /// <summary>SMSG_PARTYKILLLOG: uint64 killer (the player with the killing blow), uint64 victim (Unit::Kill, Unit.cpp 13629-13632;
        /// to the killer, or its group).</summary>
        void OnCombatPartyKill(WowPacket p)
        {
            ulong killer, victim;
            try { killer = p.U64(); victim = p.U64(); }
            catch (PacketTooShortException) { CombatOdd(p, "too short"); return; }
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes left over");
            if (victim == 0) return;
            if (victim == _player.Guid) { _lastAttackerOfMe = killer; _lastHitMeAt = _now; return; }
            var e = Find(victim);
            var u = CombatUnit(victim);
            u.Killer = killer;
            u.KillAt = _now;
            // sent before the victim's health-0 values update; a unit already dead (or no longer in view) is announced now
            if (e == null || e.Dead) UnitDied(victim, e, u);
        }

        /// <summary>SMSG_AI_REACTION: uint64 unit, uint32 reaction (Creature::SendAIReaction, Creature.cpp 2483-2494). HOSTILE "triggers aggro
        /// sound (used in client packet handler)": the unit is in combat now, so its view plays the aggro sound at once (InCombat false -> true;
        /// the server's UNIT_FLAG_IN_COMBAT update follows and keeps it).</summary>
        void OnCombatAiReaction(WowPacket p)
        {
            ulong guid;
            uint reaction;
            try { guid = p.U64(); reaction = p.U32(); }
            catch (PacketTooShortException) { CombatOdd(p, "too short"); return; }
            _combatPackets++;
            if (p.Remaining != 0) CombatOdd(p, p.Remaining + " bytes left over");
            if (reaction != AiReactionHostile) return;
            var e = Find(guid);
            if (e != null && e.Kind != EntityKind.GameObject && !e.Dead && !e.InCombat) e.InCombat = true;
        }

        // ------------------------------------------------------------------ per frame

        void TickCombat(float dt)
        {
            if (_player.Guid == 0) return;
            // our swing timer (offline: 1 - swingTimer / attack time): the server swings, we time it from its reports
            float speed = _player.Stats != null ? _player.Stats.AttackSpeed : 0f;       // UNIT_FIELD_BASEATTACKTIME / 1000 (RefreshSelf)
            double since = _now - _lastMainSwingAt;
            _player.SwingProgress = speed > 0.05f ? (float)Math.Max(0.0, Math.Min(1.0, since / speed)) : 1f;
            while (_attackReqAt.Count > 0 && _now - _attackReqAt[0] > CombatAnswerWindow) { _attackReqAt.RemoveAt(0); _attackReqSwing.RemoveAt(0); }   // never answered

            // auto-attack follows a new selection (offline SetTarget: an attackable one is attacked, anything else stops the attack)
            if (_target != _seenTarget)
            {
                _seenTarget = _target;
                if (_player.AutoAttacking && _target != _attackGuid)
                {
                    var t = _target != 0 ? Find(_target) : null;
                    if (CombatAttackable(t)) StartAttack(_target);
                    else StopAttack();
                }
            }
            if (_player.AutoAttacking)
            {
                var v = Find(_attackGuid);
                if (v != null && v.Dead) EndAutoAttack();          // offline KillCreature stops the attack; the server's SMSG_ATTACKSTOP follows
                else if (_awaitStartFor != 0 && _now - _awaitStartAt > CombatStartTimeout)
                {
                    CombatLog(0, "no SMSG_ATTACKSTART within " + CombatStartTimeout + " s for " + CombatName(_awaitStartFor) + " - the server did not start the attack");
                    EndAutoAttack();
                }
            }

            // kills: a unit in view going from alive to dead (health 0 / UNIT_DYNFLAG_DEAD, Objects.cs) -> EntityDied
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Kind == EntityKind.GameObject) continue;
                UnitCombat u;
                if (!_unitCombat.TryGetValue(e.Guid, out u))
                {
                    u = new UnitCombat { WasDead = e.Dead, DiedSent = e.Dead };        // first seen already dead: a corpse, no event
                    _unitCombat[e.Guid] = u;
                    continue;
                }
                if (e.Dead && !u.WasDead) UnitDied(e.Guid, e, u);
                else if (!e.Dead && u.WasDead) { u.DiedSent = false; u.Killer = 0; u.LastAttacker = 0; }   // respawned / resurrected
                u.WasDead = e.Dead;
            }

            TickPlayerDeath();

            if (_player.InCombat) _wasInCombatForLog = true;
            else if (_wasInCombatForLog)
            {
                _wasInCombatForLog = false;
                CombatLog(4, "left combat - this session so far: swings mine " + _swingsMine + " (" + _hitsMine + " hit), at me " + _swingsAtMe + ", others " + _swingsOthers
                          + "; kills " + _kills + ", XP +" + _xpGained + "; packets " + _combatPackets + ", odd " + _combatOdd);
            }

            if (_now >= _unitCombatPruneAt)
            {
                _unitCombatPruneAt = _now + 10.0;
                _combatScratch.Clear();
                foreach (var k in _unitCombat.Keys) if (!_objects.ContainsKey(k)) _combatScratch.Add(k);
                for (int i = 0; i < _combatScratch.Count; i++) _unitCombat.Remove(_combatScratch[i]);
            }
        }

        /// <summary>EntityDied like offline KillCreature: SourceGuid = the killer (0 unknown), TargetGuid = the unit, Text = its name.</summary>
        void UnitDied(ulong guid, EntityState e, UnitCombat u)
        {
            if (u.DiedSent) return;
            u.DiedSent = true;
            u.WasDead = true;
            ulong killer = u.Killer != 0 && _now - u.KillAt <= CombatKillerMemory ? u.Killer
                         : u.LastAttacker != 0 && _now - u.LastHitAt <= CombatKillerMemory ? u.LastAttacker : 0;
            CombatEmit(GameEventType.EntityDied, killer, guid, 0, 0, false, e != null ? e.Name : null);
            if (killer == _player.Guid || CombatMine(killer))
            {
                _kills++;
                CombatLog(3, CombatName(guid) + " died - killing blow " + CombatName(killer) + (u.Killer != 0 ? " (SMSG_PARTYKILLLOG)" : ""));
            }
        }

        /// <summary>
        /// PlayerDied like offline KillPlayer (SourceGuid = the killer, TargetGuid = the player, Text "You have died.") when our own health
        /// reaches 0 (UNIT_FIELD_HEALTH, RefreshSelf) - once, not for a character that logs in dead or as a ghost. The ghost / release flow
        /// is the lootdeath owner's (OnlineSession.Death.cs). If another owner already queued PlayerDied this frame, it is not repeated.
        /// </summary>
        void TickPlayerDeath()
        {
            bool dead = _player.Dead;
            if (!_selfKnown)
            {
                if (_self == null) return;
                _selfKnown = true;
                _selfWasDead = dead;
                _playerDiedSent = dead;
                return;
            }
            if (dead && !_selfWasDead && !_player.Ghost && !_playerDiedSent)
            {
                _playerDiedSent = true;
                EndAutoAttack();                                   // offline KillPlayer: StopAttack first
                ulong killer = _now - _lastHitMeAt <= CombatKillerMemory ? _lastAttackerOfMe : 0;
                bool queued = false;
                foreach (var q in _events) if (q.Type == GameEventType.PlayerDied) { queued = true; break; }
                if (!queued) CombatEmit(GameEventType.PlayerDied, killer, _player.Guid, 0, 0, false, "You have died.");
                WowLog.Info("Online combat: the player died" + (killer != 0 ? " to " + CombatName(killer) : "") + " (health 0 from the server)");
            }
            if (!dead) _playerDiedSent = false;
            _selfWasDead = dead;
        }

        // ------------------------------------------------------------------ iteration 11 (spellfx): combo points online

        /// <summary>SMSG_UPDATE_COMBO_POINTS 0x39D (Unit::SendComboPoints, Unit.cpp:12891-12905): packed guid of the combo target (empty = none);
        /// uint8 points. Also a warrior's Overpower point (Unit.cpp:12444-12451, cleared by the reactive timer 12972-12976). ComboFrame.lua:19-60
        /// shows them for the target only - the views read EntityState.ComboPoints / PlayerState.ComboPoints.</summary>
        void OnUpdateComboPoints(WowPacket p)
        {
            ulong on = p.PackedGuid(); int pts = p.U8();
            if (_player.ComboTarget != 0 && _player.ComboTarget != on) { var old = Find(_player.ComboTarget); if (old != null) old.ComboPoints = 0; }
            _player.ComboTarget = pts > 0 ? on : 0;
            _player.ComboPoints = pts;
            var e = on != 0 ? Find(on) : null;
            if (e != null) e.ComboPoints = pts;
            if (_comboLogs++ < 10) WowLog.Info("Online: combo points " + pts + (on != 0 ? " on 0x" + on.ToString("X") : "") + " (SMSG_UPDATE_COMBO_POINTS; logged 10 times)");
        }
        int _comboLogs; bool _petComboLogged;
    }
}
