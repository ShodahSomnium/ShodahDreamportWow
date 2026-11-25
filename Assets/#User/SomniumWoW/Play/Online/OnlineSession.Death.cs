using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>
    /// Stage O2 (owner lootdeath): WoW's death flow online - the server decides everything, this file only follows it and fills
    /// PlayerState's corpse fields (sources quoted from AzerothCore, copies in P/acsrc and P/acsrc_lootdeath):
    /// - death = our UNIT_FIELD_HEALTH 0 (Unit::Kill; Player::KillPlayer then sends SMSG_CORPSE_RECLAIM_DELAY, uint32 ms) -> PlayerDied;
    /// - ReleaseSpirit = CMSG_REPOP_REQUEST (uint8, MiscHandler.cpp HandleRepopRequestOpcode read_skip&lt;uint8&gt;): BuildPlayerRepop sends
    ///   SMSG_PRE_RESURRECT (packed guid), casts the ghost aura 8326 (PLAYER_FLAGS_GHOST 0x10), creates the corpse (CORPSE_FIELD_OWNER =
    ///   us) and re-sends the reclaim delay; RepopAtGraveyard teleports (MSG_MOVE_TELEPORT_ACK / SMSG_NEW_WORLD, owner Movement -> the
    ///   Teleport event) and sends SMSG_DEATH_RELEASE_LOC (uint32 map, float x, y, z = the graveyard). The ghost flag -> SpiritReleased.
    /// - the corpse: MSG_CORPSE_QUERY (empty; QueryHandler.cpp HandleCorpseQueryOpcode answers uint8 found [int32 map, float x, y, z,
    ///   int32 corpse map, uint32 0] - a dungeon corpse seen from outside answers the entrance) and the corpse object itself when in view;
    /// - ReclaimCorpse = CMSG_RECLAIM_CORPSE (uint64; HandleReclaimCorpseOpcode checks ghost, delay, CORPSE_RECLAIM_RADIUS 39 yd);
    /// - spirit healer = CMSG_SPIRIT_HEALER_ACTIVATE (uint64 guid; NPCHandler.cpp: GetNPCIfCanInteractWith(UNIT_NPC_FLAG_SPIRITHEALER)
    ///   -> SendSpiritResurrect: ResurrectPlayer(0.5, sickness 15007), 25% durability, maybe a teleport). The real client reaches it through
    ///   the healer's gossip + SMSG_SPIRIT_HEALER_CONFIRM (uint64 guid, spell_generic.cpp 17251); the ghost prompt stands for both;
    /// - SMSG_RESURRECT_REQUEST (Spell::SendResurrectRequest: uint64 guid, uint32 name length, name, uint8 0, uint8 sickness [, uint32 0])
    ///   -> an Info line; "Resurrect" (ReclaimCorpse) then answers CMSG_RESURRECT_RESPONSE (uint64 guid, uint8 1).
    /// - alive again = health &gt; 0 without the ghost flag -> PlayerResurrected.
    /// </summary>
    public sealed partial class OnlineSession
    {
        const uint DthNpcSpiritHealer = 0x00004000;     // UnitDefines.h:336 UNIT_NPC_FLAG_SPIRITHEALER
        const uint DthCorpseFlagBones = 0x01;           // Corpse.h CORPSE_FLAG_BONES (Map::ConvertCorpseToBones keeps the owner)
        const float DthReclaimRadius = 39f;             // Corpse.h:35 CORPSE_RECLAIM_RADIUS "Value equal client resurrection dialog show radius."
        // Ours (not WoW's):
        const double DthScanSeconds = 0.25;             // corpse object / spirit healer scan interval while dead
        const double DthCorpseQueryRetrySeconds = 5.0;  // MSG_CORPSE_QUERY again when no answer came
        // Iteration 11 (designer quests):
        const uint DthReleaseTimerFlag = 0x08;           // Player.h:545 PLAYER_FIELD_BYTE_RELEASE_TIMER (in PLAYER_FIELD_BYTES, Player.cpp:4648)
        const double DthReclaimRetrySeconds = 1.5;       // ours: no resurrection this long after CMSG_RECLAIM_CORPSE -> position sync + one more try
        double _dthReclaimSentAt = -1;
        bool _dthReclaimRetried;

        bool _dthKnown, _dthWasDead, _dthWasGhost;
        int _dthMapAtDeath;
        int _dthCorpseRealMap;          // the map the body really lies on (MSG_CORPSE_QUERY "corpse map"; differs from CorpseMapId at a dungeon entrance)
        WowPos _dthPosAtDeath;
        bool _dthReleaseLocKnown;
        int _dthReleaseMap;
        WowPos _dthReleasePos;
        bool _dthReleasedSent;          // SpiritReleased emitted for this death
        bool _dthBodySeen;              // this session saw us lying dead (so _dthPosAtDeath is the corpse's spot; false for a login as a ghost)
        float _dthReclaimLeft;          // seconds (SMSG_CORPSE_RECLAIM_DELAY)
        ulong _dthCorpseGuid;           // our corpse object while in view
        ulong _dthHealerGuid;           // the spirit healer in reach (or confirmed by SMSG_SPIRIT_HEALER_CONFIRM)
        double _dthScanAt, _dthQueryAt = -1, _dthQueryAnswerWait = -1;
        int _dthQueryMap = int.MinValue;
        ulong _dthLastHitBy;            // the last unit that damaged us (PlayerDied SourceGuid)
        ulong _dthResBy;                // SMSG_RESURRECT_REQUEST pending (0 = none)
        string _dthResName;
        bool _dthResInstant;
        string _dthVia;                 // how the last resurrection was asked for (log)
        DbcTable _dthSafeLocs;
        bool _dthSafeLocsTried;
        readonly HashSet<string> _dthOddLogged = new HashSet<string>();
        int _dthDeaths, _dthReleases, _dthResurrections, _dthCorpseAnswers;

        void InitDeath()
        {
            _world.On(Op.SMSG_CORPSE_RECLAIM_DELAY, DthOnReclaimDelay, this);
            _world.On(Op.SMSG_PRE_RESURRECT, DthOnPreResurrect, this);
            _world.On(Op.SMSG_DEATH_RELEASE_LOC, DthOnDeathReleaseLoc, this);
            _world.On(Op.MSG_CORPSE_QUERY, DthOnCorpseQuery, this);
            _world.On(Op.SMSG_SPIRIT_HEALER_CONFIRM, DthOnSpiritHealerConfirm, this);
            _world.On(Op.SMSG_RESURRECT_REQUEST, DthOnResurrectRequest, this);
            _world.On(Op.SMSG_DURABILITY_DAMAGE_DEATH, DthOnDurabilityDamageDeath, this);
            // who hit us last (read-only peeks; owners combat / spells handle these packets): the killer of PlayerDied
            _world.On(Op.SMSG_ATTACKERSTATEUPDATE, DthOnAttackerStateUpdate, this);
            _world.On(Op.SMSG_SPELLNONMELEEDAMAGELOG, DthOnSpellDamageLog, this);
        }

        void TickDeath(float dt)
        {
            if (_dthReclaimLeft > 0f) _dthReclaimLeft = Math.Max(0f, _dthReclaimLeft - dt);
            _player.ReclaimDelay = _dthReclaimLeft;
            DthTickRequest(dt);                  // rescorpse 2026-10-04: the RESURRECT popup delay and timeout
            if (_self != null)                   // between SMSG_NEW_WORLD and our new create block the state is unknown: keep the last
            {
                bool dead = _player.Dead, ghost = _player.Ghost;
                if (!_dthKnown)
                {
                    _dthKnown = true;
                    // Neither login announces a death (no "You have died.", no death cry - the real client only shows its popup; the
                    // popups come from PlayerState.Dead / Ghost). A ghost's corpse is not known until MSG_CORPSE_QUERY or the corpse object.
                    if (ghost)                                             // logged in as a ghost (Player::LoadFromDB keeps the ghost flag)
                    {
                        WowLog.Info("Online death: logged in as a ghost at " + _player.Pos + " on map " + _mapId + " - asking where the corpse is (MSG_CORPSE_QUERY)");
                        DthDied(false); DthBecameGhost();
                    }
                    else if (dead)                                         // logged in lying dead: the Release Spirit dialog
                    {
                        WowLog.Info("Online death: logged in lying dead at " + _player.Pos + " on map " + _mapId + " - the Release Spirit popup");
                        DthDied(false);
                    }
                }
                else
                {
                    if (dead && !_dthWasDead) DthDied(true);
                    if (ghost && !_dthWasGhost) DthBecameGhost();
                    if (!dead && _dthWasDead) DthResurrected();
                }
                _dthWasDead = dead;
                _dthWasGhost = ghost;
            }
            if (_dthWasDead)
            {
                if (_now >= _dthScanAt) { _dthScanAt = _now + DthScanSeconds; DthScan(); }
                if (_dthWasGhost && _world != null)
                {
                    // ask where the corpse is after the release and after every map change (a dungeon corpse answers its entrance)
                    if (_dthQueryMap != _mapId && _self != null) _dthQueryAt = _now;
                    if (_dthQueryAt >= 0 && _now >= _dthQueryAt) DthSendCorpseQuery();
                    if (_dthQueryAnswerWait >= 0 && _now >= _dthQueryAnswerWait) { _dthQueryAnswerWait = -1; _dthQueryAt = _now; }
                }
            }
            DthUpdateReclaimable();
            // iteration 11 (quests): R2 - the server ignored the reclaim (out of ITS range): sync + one more try, then a diagnosis line
            if (_dthReclaimSentAt >= 0 && _now - _dthReclaimSentAt > DthReclaimRetrySeconds)
            {
                if (!_player.Dead) _dthReclaimSentAt = -1;
                else if (!_dthReclaimRetried && _player.Ghost && _player.CanReclaim) DthSendReclaim(true);
                else
                {
                    _dthReclaimSentAt = -1;
                    WowLog.Warn("Online death: the server did not resurrect us at the corpse (corpse " + _player.CorpsePos + " map " + _player.CorpseMapId + " / real " +
                                _dthCorpseRealMap + ", we " + _player.Pos + " map " + _mapId + ", delay " + _dthReclaimLeft.ToString("0.0") + " s) - Resurrect stays offered");
                }
            }
            // R4 / R5: the DEATH popup's second button and countdown (PLAYER_SELF_RES_SPELL UpdateFields.cs:344, PLAYER_FIELD_BYTES :342)
            int selfRes = _self != null && _player.Dead && !_player.Ghost ? (int)_self.U32(UF.PLAYER_SELF_RES_SPELL) : 0;
            if (selfRes != _player.SelfResSpellId)
            {
                _player.SelfResSpellId = selfRes;
                var sdb = SpellDb.Get(_files);
                var si = selfRes != 0 && sdb != null ? sdb.Find(selfRes) : null;
                _player.SelfResName = selfRes == 0 ? null : si != null && !string.IsNullOrEmpty(si.Name) ? si.Name : "Reincarnate";   // USE_SOULSTONE (GS:7926)
                if (selfRes != 0) WowLog.Info("Online death: self-resurrection offered - spell " + selfRes + " '" + _player.SelfResName + "' (PLAYER_SELF_RES_SPELL)");
            }
            _player.ReleaseTimer = !(_self != null && _player.Dead && !_player.Ghost) || (_self.U32(UF.PLAYER_FIELD_BYTES) & DthReleaseTimerFlag) != 0;
            _player.CorpseInInstance = _player.Ghost && _player.HasCorpse && _dthCorpseRealMap != 0 && _dthCorpseRealMap != _player.CorpseMapId;
        }

        /// <summary>Iteration 11: lying dead with Player.SelfResSpellId (Soulstone, Reincarnation) -> CMSG_SELF_RES (empty; SpellHandler.cpp:707-721).</summary>
        public void SelfResurrect()
        {
            if (_world == null || !_player.Dead || _player.Ghost || _player.SelfResSpellId == 0) return;
            _world.Send(Op.CMSG_SELF_RES);
            _dthVia = "self-resurrection (" + (_player.SelfResName ?? _player.SelfResSpellId.ToString()) + ")";
            WowLog.Info("Online death: " + _dthVia + " sent (CMSG_SELF_RES)");
        }

        void DthSendReclaim(bool retry)
        {
            bool synced = MoveSyncNow(retry ? "the corpse reclaim (retry)" : "the corpse reclaim");   // the motion area's hook: MiscHandler.cpp:658 measures
                                                                                                      // from the server's copy of our position
            ulong g = _dthCorpseGuid != 0 ? _dthCorpseGuid : _player.Guid;
            _world.Send(Op.CMSG_RECLAIM_CORPSE, new PacketWriter().U64(g));
            _dthReclaimSentAt = _now;
            _dthReclaimRetried = retry;
            _dthVia = "reclaimed the corpse";
            float dx = _player.CorpsePos.X - _player.Pos.X, dy = _player.CorpsePos.Y - _player.Pos.Y, dz = _player.CorpsePos.Z - _player.Pos.Z;
            WowLog.Info("Online death: Resurrect at the corpse sent (CMSG_RECLAIM_CORPSE " + g.ToString("X16") + (retry ? ", retry" : "") + ", " +
                        Math.Sqrt(dx * dx + dy * dy + dz * dz).ToString("0.0") + " yd" + (synced ? ", position synced first" : "") + ")");
        }

        /// <summary>Quests' Interact routes here: true = the guid is a spirit healer and its dialog was asked for.</summary>
        /// <remarks>Only for a ghost (the living never see spirit healers - CREATURE_TYPE_FLAG_VISIBLE_TO_GHOSTS): the ghost prompt
        /// (PlayerState.NearSpiritHealer) stands for WoW's gossip option + SMSG_SPIRIT_HEALER_CONFIRM dialog.</remarks>
        internal bool TrySpiritHealer(ulong guid)
        {
            OnlineObject o;
            if (!_objects.TryGetValue(guid, out o) || o.TypeId != TypeId.Unit || (o.U32(UF.UNIT_NPC_FLAGS) & DthNpcSpiritHealer) == 0) return false;
            if (!_player.Ghost) return false;
            if (!DthHealerInReach(o)) { Error("You need to be closer to interact with that target."); return true; }   // ERR_TOO_FAR_TO_INTERACT (GlobalStrings.lua:3537)
            _dthHealerGuid = guid;
            _player.NearSpiritHealer = true;
            WowLog.Info("Online death: the spirit healer " + guid.ToString("X16") + " offers the resurrection");
            return true;
        }

        public void ReleaseSpirit()
        {
            if (_world == null || !_player.Dead || _player.Ghost) return;
            _world.Send(Op.CMSG_REPOP_REQUEST, new PacketWriter().U8(0));        // HandleRepopRequestOpcode: recv_data.read_skip<uint8>()
            WowLog.Info("Online death: Release Spirit sent (CMSG_REPOP_REQUEST) at " + _player.Pos + " on map " + _mapId);
        }

        public void ReclaimCorpse()
        {
            if (_world == null || !_player.Dead) return;
            if (DthResurrectRequestReady())
            {
                _world.Send(Op.CMSG_RESURRECT_RESPONSE, new PacketWriter().U64(_dthResBy).U8(1));   // HandleResurrectResponseOpcode: guid, uint8 status (1 = accept)
                _dthVia = "accepted the resurrection of " + (_dthResName ?? _dthResBy.ToString("X16"));
                WowLog.Info("Online death: " + _dthVia + " (CMSG_RESURRECT_RESPONSE)");
                return;
            }
            if (!_player.Ghost || !_player.CanReclaim)
            {
                if (_dthOddLogged.Add("reclaim early")) WowLog.Info("Online death: Resurrect asked while not reclaimable (ghost " + _player.Ghost + ", corpse " + _player.HasCorpse +
                                                                    ", delay " + _dthReclaimLeft.ToString("0.0") + " s) - nothing sent (logged once)");
                return;
            }
            // HandleReclaimCorpseOpcode reads the guid and never uses it (it takes _player->GetCorpse()): our corpse's, else our own.
            DthSendReclaim(false);                                               // iteration 11: position sync first + one retry (R2)
        }

        public void AcceptSpiritHealer()
        {
            if (_world == null || !_player.Ghost) return;
            OnlineObject o;
            if (_dthHealerGuid == 0 || !_objects.TryGetValue(_dthHealerGuid, out o) || !DthHealerInReach(o)) { DthScan(); }
            if (_dthHealerGuid == 0) { Error("You need to be closer to interact with that target."); return; }   // ERR_TOO_FAR_TO_INTERACT (GlobalStrings.lua:3537)
            _world.Send(Op.CMSG_SPIRIT_HEALER_ACTIVATE, new PacketWriter().U64(_dthHealerGuid));
            _dthVia = "the spirit healer";
            WowLog.Info("Online death: resurrection at the spirit healer " + _dthHealerGuid.ToString("X16") + " sent (CMSG_SPIRIT_HEALER_ACTIVATE)");
        }

        // ------------------------------------------------------------------ transitions

        void DthDied(bool announce)
        {
            _dthDeaths++;
            _dthMapAtDeath = _mapId;
            _dthPosAtDeath = _player.Pos;
            _dthBodySeen = !_player.Ghost;                                        // lying dead here: the body is where we stand (not so for a ghost at login)
            _dthReleasedSent = false;
            if (!_player.Ghost) _dthReleaseLocKnown = false;                     // (a death seen only as a ghost keeps the graveyard just announced)
            _dthCorpseGuid = 0;
            _dthVia = null;
            if (_ldGuid != 0) CloseLoot();
            if (!announce) return;
            if (!DthQueued(GameEventType.PlayerDied))                         // owner combat may have announced it already this frame
                _events.Enqueue(new GameEvent { Type = GameEventType.PlayerDied, SourceGuid = _dthLastHitBy, TargetGuid = _player.Guid, Text = "You have died." });
            EntityState k = _dthLastHitBy != 0 ? Find(_dthLastHitBy) : null;
            WowLog.Info("Online death: the player died (health 0) at " + _player.Pos + " on map " + _mapId +
                        (k != null ? " - last hit by " + (k.Name ?? "?") + " (level " + k.Level + ")" : "") + ", reclaim delay " + _dthReclaimLeft.ToString("0") + " s");
        }

        void DthBecameGhost()
        {
            _dthReleases++;
            if (_dthCorpseGuid != 0) _player.HasCorpse = true;
            else if (_dthBodySeen)
            {
                _player.HasCorpse = true;
                _player.CorpseMapId = _dthMapAtDeath;                            // until MSG_CORPSE_QUERY / the corpse object answer
                _dthCorpseRealMap = _dthMapAtDeath;
                _player.CorpsePos = _dthPosAtDeath;
            }
            // else (logged in as a ghost): HasCorpse stays false until DthScan sees the corpse object or DthOnCorpseQuery answers -
            // the ghost's own spot is not the corpse (no "Resurrect" and no "Corpse 0 yd" meanwhile).
            _dthQueryAt = _now;
            if (!_dthReleasedSent)
            {
                _dthReleasedSent = true;
                // where the ghost appears: the graveyard of SMSG_DEATH_RELEASE_LOC (RepopAtGraveyard), else where we stand (no graveyard found)
                WowPos at = _dthReleaseLocKnown ? _dthReleasePos : _player.Pos;
                int atMap = _dthReleaseLocKnown ? _dthReleaseMap : _mapId;
                string grave = _dthReleaseLocKnown ? DthGraveyardName(_dthReleaseMap, _dthReleasePos) : null;
                _events.Enqueue(new GameEvent { Type = GameEventType.SpiritReleased, SourceGuid = _player.Guid, TargetGuid = _player.Guid, Pos = at, Text = grave, Amount = atMap });
                WowLog.Info("Online death: a ghost at " + at + " on map " + _mapId + (grave != null ? " (" + grave + ")" : "") +
                            (_player.HasCorpse ? ", corpse at " + _player.CorpsePos + " on map " + _player.CorpseMapId : ", corpse not known yet") + ", reclaim delay " + _dthReclaimLeft.ToString("0") + " s");
            }
        }

        void DthResurrected()
        {
            _dthResurrections++;
            _events.Enqueue(new GameEvent { Type = GameEventType.PlayerResurrected, SourceGuid = _player.Guid, TargetGuid = _player.Guid, Amount = _player.Health });
            WowLog.Info("Online death: alive again with " + _player.Health + "/" + _player.MaxHealth + " health at " + _player.Pos + " on map " + _mapId +
                        (_dthVia != null ? " (" + _dthVia + ")" : " (the server resurrected us)"));
            _player.HasCorpse = false;
            _player.NearSpiritHealer = false;
            _player.SelfResSpellId = 0; _player.SelfResName = null; _player.CorpseInInstance = false; _dthReclaimSentAt = -1;   // iteration 11
            _dthReclaimLeft = 0f;
            _player.ReclaimDelay = 0f;
            _dthResBy = 0;
            _dthResName = null;
            DthClearRequest();
            _dthCorpseGuid = 0;
            _dthHealerGuid = 0;
            _dthReleaseLocKnown = false;
            _dthQueryAt = -1;
            _dthQueryAnswerWait = -1;
            _dthQueryMap = int.MinValue;
        }

        /// <summary>CanReclaim + CorpseReclaimable: a ghost within CORPSE_RECLAIM_RADIUS of its corpse on the same map with the delay over,
        /// or a pending resurrection request that may be accepted now.</summary>
        void DthUpdateReclaimable()
        {
            bool can = false;
            if (_player.Dead)
            {
                if (DthResurrectRequestReady()) can = true;
                else if (_player.Ghost && _player.HasCorpse && _player.CorpseMapId == _mapId && _dthCorpseRealMap == _mapId && _dthReclaimLeft <= 0f)
                {
                    float dx = _player.CorpsePos.X - _player.Pos.X, dy = _player.CorpsePos.Y - _player.Pos.Y, dz = _player.CorpsePos.Z - _player.Pos.Z;
                    can = dx * dx + dy * dy + dz * dz <= DthReclaimRadius * DthReclaimRadius;
                }
            }
            if (can == _player.CanReclaim) return;
            _player.CanReclaim = can;
            _events.Enqueue(new GameEvent { Type = GameEventType.CorpseReclaimable, TargetGuid = _player.Guid, Amount = can ? 1 : 0, Pos = _player.CorpsePos });
        }

        bool DthResurrectRequestReady() { return _dthResBy != 0 && (_dthResInstant || _dthReclaimLeft <= 0f); }

        /// <summary>While dead: our corpse object (exact position) and the spirit healer in reach (NearSpiritHealer).</summary>
        void DthScan()
        {
            ulong healer = 0;
            float best = float.MaxValue;
            bool ghost = _player.Ghost;
            foreach (var o in _objects.Values)
            {
                if (o.TypeId == TypeId.Corpse)
                {
                    if (!o.HasPos || o.U64(UF.CORPSE_FIELD_OWNER) != _player.Guid || (o.U32(UF.CORPSE_FIELD_FLAGS) & DthCorpseFlagBones) != 0) continue;
                    if (_dthCorpseGuid != o.Guid) WowLog.Info("Online death: our corpse " + o.Guid.ToString("X16") + " is in view at (" + o.X.ToString("0.0") + ", " + o.Y.ToString("0.0") + ", " + o.Z.ToString("0.0") + ")");
                    _dthCorpseGuid = o.Guid;
                    _player.HasCorpse = true;
                    _player.CorpseMapId = _mapId;
                    _dthCorpseRealMap = _mapId;
                    _player.CorpsePos = new WowPos(o.X, o.Y, o.Z, o.O);
                }
                else if (ghost && o.TypeId == TypeId.Unit && (o.U32(UF.UNIT_NPC_FLAGS) & DthNpcSpiritHealer) != 0 && o.HasPos)
                {
                    if (o.State != null && o.State.Reaction == Reaction.Hostile) continue;   // GetNPCIfCanInteractWith: reaction must be above unfriendly
                    float d = LdDist(o.X, o.Y, o.Z);
                    if (d < best && DthHealerInReach(o)) { best = d; healer = o.Guid; }
                }
            }
            if (!ghost) return;
            if (healer != _dthHealerGuid && healer != 0) WowLog.Info("Online death: next to the spirit healer " + healer.ToString("X16"));
            _dthHealerGuid = healer;
            _player.NearSpiritHealer = healer != 0;
        }

        /// <summary>Player::GetNPCIfCanInteractWith: creature->IsWithinDistInMap(this, INTERACTION_DISTANCE) (both object sizes added).</summary>
        bool DthHealerInReach(OnlineObject o)
        {
            return o != null && o.HasPos && LdDist(o.X, o.Y, o.Z) <= LdInteractionDistance + LdMyReach() + LdReachOf(o);
        }

        void DthSendCorpseQuery()
        {
            _dthQueryAt = -1;
            _dthQueryMap = _mapId;
            _dthQueryAnswerWait = _now + DthCorpseQueryRetrySeconds;
            _world.Send(Op.MSG_CORPSE_QUERY);                                     // HandleCorpseQueryOpcode reads nothing
        }

        bool DthQueued(GameEventType t)
        {
            foreach (var e in _events) if (e.Type == t) return true;
            return false;
        }

        /// <summary>WorldSafeLocs.dbc AreaName of the graveyard at (map, pos) - AC's graveyard table carries the DBC's ids and spots.</summary>
        string DthGraveyardName(int map, WowPos pos)
        {
            if (!_dthSafeLocsTried)
            {
                _dthSafeLocsTried = true;
                _dthSafeLocs = Dbc.Load(_files, "WorldSafeLocs");
                if (_dthSafeLocs != null && _dthSafeLocs.FieldCount != DbcFields.WorldSafeLocs.FieldCount) _dthSafeLocs = null;
            }
            var t = _dthSafeLocs;
            if (t == null) return null;
            int bestRow = -1;
            float best = 2f * 2f;                                                 // ours: within 2 yd counts as the same spot
            for (int r = 0; r < t.RecordCount; r++)
            {
                if (t.GetInt(r, DbcFields.WorldSafeLocs.Continent) != map) continue;
                float dx = t.GetFloat(r, DbcFields.WorldSafeLocs.Loc) - pos.X, dy = t.GetFloat(r, DbcFields.WorldSafeLocs.Loc + 1) - pos.Y, dz = t.GetFloat(r, DbcFields.WorldSafeLocs.Loc + 2) - pos.Z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < best) { best = d; bestRow = r; }
            }
            if (bestRow < 0) return null;
            string n = t.GetLocalizedString(bestRow, DbcFields.WorldSafeLocs.AreaName_lang);
            return string.IsNullOrEmpty(n) ? null : n;
        }

        // ------------------------------------------------------------------ server packets

        void DthOnReclaimDelay(WowPacket p)
        {
            uint ms = p.U32();
            _dthReclaimLeft = ms / 1000f;
            _player.ReclaimDelay = _dthReclaimLeft;
            WowLog.Info("Online death: corpse reclaim delay " + (ms / 1000f).ToString("0.0") + " s (SMSG_CORPSE_RECLAIM_DELAY)");
        }

        void DthOnPreResurrect(WowPacket p)
        {
            ulong g = p.PackedGuid();
            if (g != _player.Guid) return;
            WowLog.Info("Online death: the server turns us into a ghost (SMSG_PRE_RESURRECT)");
        }

        void DthOnDeathReleaseLoc(WowPacket p)
        {
            uint map = p.U32();
            float x = p.F32(), y = p.F32(), z = p.F32();
            if (map == 0xFFFFFFFFu) { _dthReleaseLocKnown = false; return; }      // Player::ResurrectPlayer: "remove spirit healer position"
            _dthReleaseLocKnown = true;
            _dthReleaseMap = (int)map;
            _dthReleasePos = new WowPos(x, y, z, 0f);
            WowLog.Info("Online death: graveyard on map " + map + " at " + _dthReleasePos + " (SMSG_DEATH_RELEASE_LOC)");
        }

        void DthOnCorpseQuery(WowPacket p)
        {
            _dthCorpseAnswers++;
            _dthQueryAnswerWait = -1;
            int found = p.U8();
            if (found == 0)
            {
                _player.HasCorpse = false;                                         // no corpse (turned to bones): only the spirit healer is left
                WowLog.Info("Online death: the server knows no corpse of ours (MSG_CORPSE_QUERY 0)");
                return;
            }
            int map = p.I32();
            float x = p.F32(), y = p.F32(), z = p.F32();
            int corpseMap = p.Remaining >= 4 ? p.I32() : map;
            _player.HasCorpse = true;
            if (_dthCorpseGuid == 0 || map != _mapId)                             // the corpse object in view is exact; else the server's answer
            {
                _player.CorpseMapId = map;
                _player.CorpsePos = new WowPos(x, y, z, 0f);
                _dthCorpseRealMap = corpseMap;
            }
            WowLog.Info("Online death: corpse at (" + x.ToString("0.0") + ", " + y.ToString("0.0") + ", " + z.ToString("0.0") + ") on map " + map +
                        (corpseMap != map ? " (the entrance; the corpse lies on map " + corpseMap + ")" : "") + " (MSG_CORPSE_QUERY)");
        }

        void DthOnSpiritHealerConfirm(WowPacket p)
        {
            ulong g = p.U64();
            _dthHealerGuid = g;
            if (_player.Ghost) _player.NearSpiritHealer = true;
            WowLog.Info("Online death: the spirit healer " + g.ToString("X16") + " asks to confirm (SMSG_SPIRIT_HEALER_CONFIRM)");
        }

        void DthOnResurrectRequest(WowPacket p)
        {
            ulong g = p.U64();
            uint len = p.U32();
            string name = len > 0 ? p.CString() : "";
            if (p.Remaining >= 2) p.U8();                                          // Spell::SendResurrectRequest writes uint8(0) after the string
            bool sickness = p.Remaining >= 1 && p.U8() != 0;                       // 1 = a creature resurrects: resurrection sickness
            bool instant = p.Remaining >= 4;                                       // SPELL_ATTR3_NO_RES_TIMER: uint32(0) overrides the reclaim delay
            if (string.IsNullOrEmpty(name))
            {
                // "for player resurrections the name is looked up by guid"
                EntityState e = Find(g);
                string n;
                name = e != null && !string.IsNullOrEmpty(e.Name) ? e.Name : _playerNames.TryGetValue(g, out n) ? n : "Someone";   // "Someone": ours
            }
            _dthResBy = g;
            _dthResName = name;

            _dthResInstant = instant;
            _player.ResurrectRequestBy = name;
            _player.ResurrectRequestSickness = sickness;
            _player.ResurrectRequestDelay = instant ? 0f : _dthReclaimLeft;
            _player.ResurrectRequestLeft = _dthReclaimLeft + DthRequestTimeout;   // StaticPopup.lua:1198 OnShow: self.timeleft = GetCorpseRecoveryDelay() + 60
            string text = sickness ? name + " wants to resurrect you.  You will be afflicted with resurrection sickness."   // RESURRECT_REQUEST (GlobalStrings.lua:6127)
                                   : name + " wants to resurrect you";                                                         // RESURRECT_REQUEST_NO_SICKNESS (6128)
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, SourceGuid = g, TargetGuid = _player.Guid, Text = text });
            WowLog.Info("Online death: " + text + " (SMSG_RESURRECT_REQUEST" + (instant ? ", no timer" : "") + ") - \"Resurrect\" accepts it");
        }

        void DthOnDurabilityDamageDeath(WowPacket p)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, TargetGuid = _player.Guid, Text = "Your equipped items suffer a 10% durability loss." });   // DURABILITYDAMAGE_DEATH (GlobalStrings.lua:2096)
        }

        /// <summary>SMSG_ATTACKERSTATEUPDATE head (Unit::SendAttackStateUpdate): uint32 HitInfo, packed attacker, packed victim.</summary>
        void DthOnAttackerStateUpdate(WowPacket p)
        {
            p.U32();
            ulong attacker = p.PackedGuid(), victim = p.PackedGuid();
            if (victim == _player.Guid && attacker != 0 && attacker != _player.Guid) _dthLastHitBy = attacker;
        }

        /// <summary>SMSG_SPELLNONMELEEDAMAGELOG head (Unit::SendSpellNonMeleeDamageLog): packed target, packed attacker.</summary>
        void DthOnSpellDamageLog(WowPacket p)
        {
            ulong target = p.PackedGuid(), attacker = p.PackedGuid();
            if (target == _player.Guid && attacker != 0 && attacker != _player.Guid) _dthLastHitBy = attacker;
        }

        // ------------------------------------------------------------------ players' corpses as entities (rescorpse 2026-10-04)
        //
        // The user: "their dead body disappears when they release, so I can't res their dead body". AC Player::BuildPlayerRepop
        // (ac_Player.cpp:4508-4553) makes a CORPSE object (TYPEID_CORPSE 7) at the death spot (CreateCorpse, :4679-4745) and the ghost turns
        // invisible to the living (AuraEffect::HandleAuraGhost sets SERVERSIDE_VISIBILITY_GHOST, acsrc/s5/SpellAuraEffects.cpp) - their
        // player object leaves our view, the corpse object arrives. Every corpse whose owner is set and that is not bones becomes an
        // EntityState: Kind Player, Dead, CorpseOwnerGuid = CORPSE_FIELD_OWNER, the owner's WoW character look from the corpse fields,
        // never IsSomniumPlayer (a Somnium player's corpse is their WoW character lying dead). It goes with SMSG_DESTROY_OBJECT / out of
        // range (Remove) and when it turns into bones (Map::ConvertCorpseToBones, acsrc_lootdeath/Map.cpp:3126-3175: the corpse is removed,
        // bones with CORPSE_FLAG_BONES take its guid) - bones are no resurrection target (the owner has no corpse any more): no entity.

        const uint DthCorpseFlagHideHelm = 0x08, DthCorpseFlagHideCloak = 0x10;     // Corpse.h:43-44 CORPSE_FLAG_HIDE_HELM / _HIDE_CLOAK
        readonly Dictionary<ulong, int> _dthLevelSeen = new Dictionary<ulong, int>(); // ours: the last level of players that left view (their corpse's level)
        int _dthCorpsesSeen, _dthBonesSeen;

        /// <summary>A corpse object that is drawn: an owner and not bones.</summary>
        static bool DthCorpseDrawn(OnlineObject o) { return o.U64(UF.CORPSE_FIELD_OWNER) != 0 && (o.U32(UF.CORPSE_FIELD_FLAGS) & DthCorpseFlagBones) == 0; }

        /// <summary>
        /// The corpse object -> its EntityState (Create / every values update, from Refresh). Fields as AC Player::CreateCorpse writes them
        /// (ac_Player.cpp:4697-4745): CORPSE_FIELD_BYTES_1 = 0 | race &lt;&lt; 8 | gender (PLAYER_BYTES_3 byte 0) &lt;&lt; 16 | skin &lt;&lt; 24,
        /// BYTES_2 = face | hairstyle &lt;&lt; 8 | haircolor &lt;&lt; 16 | facialhair &lt;&lt; 24, DISPLAY_ID = the native display, CORPSE_FIELD_ITEM + slot =
        /// DisplayInfoID | InventoryType &lt;&lt; 24 for the 19 equipment slots, FLAGS HIDE_HELM 0x08 / HIDE_CLOAK 0x10 (those pieces are not drawn).
        /// Name "Corpse of %s" (CORPSE_TOOLTIP, GlobalStrings.lua:1867; "Corpse", :1865, until the owner's name is known - CMSG_NAME_QUERY).
        /// </summary>
        void DthCorpseSync(OnlineObject o)
        {
            if (!DthCorpseDrawn(o))
            {
                if (o.State != null)
                {
                    _entities.Remove(o.State);
                    o.State = null;
                    if (_target == o.Guid) _target = 0;
                }
                if (o.U64(UF.CORPSE_FIELD_OWNER) != 0 && ++_dthBonesSeen <= 3)
                    WowLog.Info("Online: bones " + o.Guid.ToString("X") + " of " + DthOwnerName(o.U64(UF.CORPSE_FIELD_OWNER)) + " - not drawn, no resurrection target (CORPSE_FLAG_BONES, Map::ConvertCorpseToBones; logged 3 times)");
                return;
            }
            var s = o.State;
            if (s == null)
            {
                s = o.State = new EntityState { Guid = o.Guid };
                _entities.Add(s);
            }
            ulong owner = o.U64(UF.CORPSE_FIELD_OWNER);
            bool fresh = s.CorpseOwnerGuid != owner;
            s.CorpseOwnerGuid = owner;
            s.Kind = EntityKind.Player;
            s.IsSomniumPlayer = false;                                   // drawn as the WoW character even when the owner is a Somnium player
            s.Dead = true;
            s.StandState = 7;                                            // UNIT_STAND_STATE_DEAD
            s.Entry = 0;
            float scale = o.F32(UF.OBJECT_FIELD_SCALE_X);
            s.Scale = scale > 0.01f && scale < 100f ? scale : 1f;
            uint b1 = o.U32(UF.CORPSE_FIELD_BYTES_1), b2 = o.U32(UF.CORPSE_FIELD_BYTES_2), flags = o.U32(UF.CORPSE_FIELD_FLAGS);
            s.Race = (int)(b1 >> 8) & 0xFF; s.Gender = (int)(b1 >> 16) & 0xFF; s.Skin = (int)(b1 >> 24) & 0xFF;
            s.Face = (int)b2 & 0xFF; s.HairStyle = (int)(b2 >> 8) & 0xFF; s.HairColor = (int)(b2 >> 16) & 0xFF; s.FacialHair = (int)(b2 >> 24) & 0xFF;
            s.DisplayId = s.NativeDisplayId = o.I32(UF.CORPSE_FIELD_DISPLAY_ID);
            if (s.ItemDisplayIds == null) s.ItemDisplayIds = new int[19];
            for (int i = 0; i < 19; i++) s.ItemDisplayIds[i] = (int)(o.U32(UF.CORPSE_FIELD_ITEM + i) & 0xFFFFFFu);
            if ((flags & DthCorpseFlagHideHelm) != 0) s.ItemDisplayIds[(int)EquipSlot.Head] = 0;
            if ((flags & DthCorpseFlagHideCloak) != 0) s.ItemDisplayIds[(int)EquipSlot.Back] = 0;
            s.MainHandDisplayId = s.ItemDisplayIds[(int)EquipSlot.MainHand];
            s.OffHandDisplayId = s.ItemDisplayIds[(int)EquipSlot.OffHand];
            s.Sheath = SheathState.Sheathed;
            s.Health = s.MaxHealth = 0;
            s.Power = PowerType.None; s.PowerValue = s.PowerMax = 0;
            s.Lootable = false;                                          // ours: CORPSE_FLAG_LOOTABLE (battleground insignia) is not looted here
            s.InCombat = false; s.Moving = false; s.Mounted = false; s.MountDisplayId = 0;
            if (o.HasPos) s.Pos = new WowPos(o.X, o.Y, o.Z, o.O);
            int lvl = DthOwnerLevel(owner);
            if (lvl > 0) s.Level = lvl;
            s.Reaction = DthCorpseReaction(owner, s.Race);
            s.ConColor = Rules.WowFormulas.ColorCode(_player.Level, s.Level);
            string n = DthOwnerName(owner);
            s.Name = n != null ? "Corpse of " + n : "Corpse";            // CORPSE_TOOLTIP (GlobalStrings.lua:1867) / CORPSE (:1865)
            if (n == null && owner != _player.Guid && _world != null && _nameAsked.Add(owner))
            {
                _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(owner));   // NameQuery::Read: ObjectGuid
                _queriesSent++;
            }
            if (fresh && (++_dthCorpsesSeen <= 5 || _dthCorpsesSeen % 50 == 0))
                WowLog.Info("Online: corpse of " + (n ?? owner.ToString("X")) + " in view at (" + s.Pos.X.ToString("0.0") + ", " + s.Pos.Y.ToString("0.0") + ", " + s.Pos.Z.ToString("0.0") +
                            ") - drawn lying as their character (race " + s.Race + ", display " + s.DisplayId + "), " + s.Reaction + ", selectable; a resurrection aims at it with " +
                            "TARGET_FLAG_CORPSE_ALLY + its guid " + o.Guid.ToString("X") + " #" + _dthCorpsesSeen);
        }

        string DthOwnerName(ulong owner)
        {
            if (owner == _player.Guid && !string.IsNullOrEmpty(_player.Name)) return _player.Name;
            string n;
            return _playerNames.TryGetValue(owner, out n) && !string.IsNullOrEmpty(n) ? n : null;
        }

        /// <summary>The owner's level: their player object while in view, else the last level seen (ours), else their group row, else 0.</summary>
        int DthOwnerLevel(ulong owner)
        {
            if (owner == _player.Guid) return _player.Level;
            OnlineObject oo;
            if (_objects.TryGetValue(owner, out oo) && oo.TypeId == TypeId.Player && oo.State != null && oo.State.Level > 0) return oo.State.Level;
            int l;
            if (_dthLevelSeen.TryGetValue(owner, out l)) return l;
            var g = _player.Group;
            if (g != null) for (int i = 0; i < g.Count; i++) if (g[i] != null && g[i].Guid == owner && g[i].Level > 0) return g[i].Level;
            return 0;
        }

        /// <summary>
        /// Ours (the corpse object has no faction field, Corpse.h): the owner's reaction while their player object is in view, a member of our
        /// group = friendly, else the side of the corpse's race (ChrRaces: OnlineDb.FactionOfRace) against ours - friendly or hostile.
        /// </summary>
        Reaction DthCorpseReaction(ulong owner, int race)
        {
            if (owner == _player.Guid) return Reaction.Friendly;
            OnlineObject oo;
            if (_objects.TryGetValue(owner, out oo) && oo.TypeId == TypeId.Player && oo.State != null) return oo.State.Reaction;
            var g = _player.Group;
            if (g != null) for (int i = 0; i < g.Count; i++) if (g[i] != null && g[i].Guid == owner) return Reaction.Friendly;
            if (race <= 0 || _player.Race <= 0) return Reaction.Neutral;
            return OnlineDb.FactionOfRace(race) == OnlineDb.FactionOfRace(_player.Race) ? Reaction.Friendly : Reaction.Hostile;
        }

        /// <summary>Objects.cs Remove: a player leaving view (released = invisible to the living) leaves its level for its corpse.</summary>
        void DthPlayerLeft(OnlineObject o)
        {
            if (o.TypeId != TypeId.Player || o.State == null || o.State.Level <= 0) return;
            if (_dthLevelSeen.Count > 500) _dthLevelSeen.Clear();
            _dthLevelSeen[o.Guid] = o.State.Level;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.CorpseOwnerGuid == o.Guid && e.Level <= 0) { e.Level = o.State.Level; e.ConColor = Rules.WowFormulas.ColorCode(_player.Level, e.Level); }
            }
        }

        /// <summary>States fix 2026-10-04: the drawn corpse of a player in view, null when none.</summary>
        EntityState DthCorpseOf(ulong owner)
        {
            if (owner == 0) return null;
            for (int i = 0; i < _entities.Count; i++) if (_entities[i].CorpseOwnerGuid == owner) return _entities[i];
            return null;
        }

        /// <summary>Objects.cs OnNameQueryResponse: the corpses of that player get their "Corpse of %s".</summary>
        void DthCorpseNamed(ulong owner, string name)
        {
            for (int i = 0; i < _entities.Count; i++)
                if (_entities[i].CorpseOwnerGuid == owner) _entities[i].Name = "Corpse of " + name;   // CORPSE_TOOLTIP (GlobalStrings.lua:1867)
        }

        // ------------------------------------------------------------------ the RESURRECT popup's decline and timeout (rescorpse 2026-10-04)

        const float DthRequestTimeout = 60f;    // StaticPopup.lua:1198 / 1209: timeleft = GetCorpseRecoveryDelay() + 60, timeout = 60

        /// <summary>
        /// StaticPopup "RESURRECT" / "_NO_SICKNESS" button2 DECLINE (and its timeout: StaticPopup_OnUpdate calls OnCancel(dialog, data,
        /// "timeout"), StaticPopup.lua:3292-3300; OnCancel = DeclineResurrect(), :1203-1207) = CMSG_RESURRECT_RESPONSE (uint64 guid, uint8 0);
        /// AC HandleResurrectResponseOpcode: status 0 -> clearResurrectRequestData() "reject" (MiscHandler.cpp:679-683). Lying dead the DEATH
        /// popup comes back (OnCancel shows "DEATH" while UnitIsDead). The resurrecter may cast again.
        /// </summary>
        public void DeclineResurrect(string why)
        {
            if (_dthResBy == 0) return;
            if (_world != null) _world.Send(Op.CMSG_RESURRECT_RESPONSE, new PacketWriter().U64(_dthResBy).U8(0));
            WowLog.Info("Online death: the resurrection by " + (_dthResName ?? _dthResBy.ToString("X16")) + " declined (" + why + ", CMSG_RESURRECT_RESPONSE status 0)");
            _dthResBy = 0;
            _dthResName = null;
            _dthResInstant = false;
            DthClearRequest();
            DthUpdateReclaimable();
        }

        void DthTickRequest(float dt)
        {
            if (_dthResBy == 0) { if (_player.ResurrectRequestBy != null) DthClearRequest(); return; }
            if (!_player.Dead) { _dthResBy = 0; DthClearRequest(); return; }
            _player.ResurrectRequestDelay = _dthResInstant ? 0f : _dthReclaimLeft;
            _player.ResurrectRequestLeft = Math.Max(0f, _player.ResurrectRequestLeft - dt);
            if (_player.ResurrectRequestLeft <= 0f) DeclineResurrect("the popup timed out");
        }

        void DthClearRequest()
        {
            _player.ResurrectRequestBy = null;
            _player.ResurrectRequestSickness = false;
            _player.ResurrectRequestDelay = 0f;
            _player.ResurrectRequestLeft = 0f;
        }

        /// <summary>"death: 1 deaths, 1 releases..." for the session summary and probes.</summary>
        public string DeathSummary
        {
            get
            {
                return "death: " + _dthDeaths + " deaths, " + _dthReleases + " releases, " + _dthResurrections + " resurrections, " + _dthCorpseAnswers + " corpse answers" +
                       (_player.Dead ? ", now " + (_player.Ghost ? "a ghost" : "dead") + (_player.HasCorpse ? " with a corpse on map " + _player.CorpseMapId : "") : "");
            }
        }
    }
}
