// iteration 11 - owner interact (proposal-interact.md 3.2 / 5.5 / 6.2.2): duels with another player on the realm.
// AzerothCore master: src/server/game/Handlers/DuelHandler.cpp, Spells/SpellEffects.cpp:4285-4354 (EffectDuel), Entities/Player/Player.cpp
// :6554-6738 (CheckDuelDistance / DuelComplete), :15790-15795 (SendDuelCountdown), PlayerMisc.cpp:416-428 (UpdateDuelFlag), Unit.cpp:6895-6897.
// The challenge is the real client's StartDuel(unit) = spell 7266 "Duel" cast at the player (in every spellbook: skill 183 GENERIC (DND),
// SkillLineAbility row 4214, measured by the designer) - the Spells owner's normal cast path.
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const int IxDuelSpell = 7266;               // Spell.dbc "Duel": Effect 83 SPELL_EFFECT_DUEL, range 7 (10 yd), implicit target 25
        const float IxDuelPopupSeconds = 60f;       // StaticPopupDialogs["DUEL_REQUESTED"].timeout = 60 (StaticPopup.lua:2156-2169)
        const float IxDuelBoundsSeconds = 10f;      // StaticPopupDialogs["DUEL_OUTOFBOUNDS"].timeout = 10 (:2170-2173); Player.cpp:6554-6587 (10 s outside)
        const float IxDuelCastGuard = 5f;           // ours: a challenge the server never confirmed (the cast failed) falls back to None
        const float IxDuelAskGuard = 65f;           // ours: a confirmed challenge without an answer falls back to None (the other popup declines at 60 s)

        readonly DuelView _ixDuel = new DuelView();
        float _ixDuelAge;
        int _ixDuelSaid = -1;                       // the last "Duel starting: n" printed

        public DuelView Duel { get { return _ixDuel; } }

        void IxDuelInit()
        {
            IxHandle(Op.SMSG_DUEL_REQUESTED, IxDuelOnRequested);
            IxHandle(Op.SMSG_DUEL_COUNTDOWN, IxDuelOnCountdown);
            IxHandle(Op.SMSG_DUEL_OUTOFBOUNDS, p => { _ixDuel.OutOfBounds = true; _ixDuel.SecondsLeft = IxDuelBoundsSeconds; _ixDuel.Version++; WowLog.Info("Online duel: SMSG_DUEL_OUTOFBOUNDS - back within 40 yd of the flag or the duel is lost in 10 s"); });
            IxHandle(Op.SMSG_DUEL_INBOUNDS, p => { _ixDuel.OutOfBounds = false; _ixDuel.Version++; WowLog.Info("Online duel: SMSG_DUEL_INBOUNDS"); });
            IxHandle(Op.SMSG_DUEL_COMPLETE, IxDuelOnComplete);
            IxHandle(Op.SMSG_DUEL_WINNER, IxDuelOnWinner);
        }

        // ------------------------------------------------------------------ actions (IWowInteractions)

        /// <summary>UnitPopup DUEL -> StartDuel(unit): IWowSession.CastSpell(7266, player) (SpTargets writes TARGET_FLAG_UNIT + the guid for
        /// implicit target 25). Every refusal beyond "a player, not us" is the server's cast failure (SPELL_FAILED_NO_DUELING / _OUT_OF_RANGE /
        /// _BAD_TARGETS -> the Spells owner's SMSG_CAST_FAILED text).</summary>
        public void RequestDuel(ulong playerGuid)
        {
            if (playerGuid == 0 || playerGuid == _player.Guid) return;
            if (_world == null) { Error(NotYet); return; }
            if (_ixDuel.State != DuelState.None) { WowLog.Info("Online duel: RequestDuel while " + _ixDuel.State + " - ignored (EffectDuel: one duel at a time)"); return; }
            EntityState e = Find(playerGuid);
            if (e == null || e.Kind != EntityKind.Player) { IxError("Unknown unit."); return; }   // ERR_UNIT_NOT_FOUND (GlobalStrings.lua:3565)
            _ixDuel.State = DuelState.Requested;
            _ixDuel.OpponentGuid = playerGuid;
            _ixDuel.OpponentName = IxName(playerGuid) ?? e.Name;
            _ixDuel.FlagGuid = 0;
            _ixDuel.OutOfBounds = false;
            _ixDuelAge = 0f;
            _ixDuel.Version++;
            WowLog.Info("Online duel: challenging " + (_ixDuel.OpponentName ?? IxGuidText(playerGuid)) + " (CastSpell " + IxDuelSpell + ", the Spells owner's cast)");
            CastSpell(IxDuelSpell, playerGuid);
        }

        /// <summary>StaticPopup "DUEL_REQUESTED": AcceptDuel = CMSG_DUEL_ACCEPTED (uint64 flag guid - must equal PLAYER_DUEL_ARBITER,
        /// DuelHandler.cpp:25-50); CancelDuel = CMSG_DUEL_CANCELLED (uint64, :52-75).</summary>
        public void AnswerDuel(bool accept)
        {
            if (_ixDuel.State != DuelState.Incoming) { WowLog.Info("Online duel: answer (" + (accept ? "accept" : "decline") + ") without a challenge - ignored"); return; }
            string who = _ixDuel.OpponentName ?? IxGuidText(_ixDuel.OpponentGuid);
            if (accept)
            {
                IxSend(Op.CMSG_DUEL_ACCEPTED, new PacketWriter().U64(_ixDuel.FlagGuid), "CMSG_DUEL_ACCEPTED (" + who + ")");
                // SMSG_DUEL_COUNTDOWN follows at once (HandleDuelAcceptedOpcode -> SendDuelCountdown, Player.cpp:15790-15795); until then we
                // wait as Requested with the short guard (a refused accept - a wrong flag - would otherwise hang).
                _ixDuel.State = DuelState.Requested;
                _ixDuelAge = IxDuelAskGuard - IxDuelCastGuard;
            }
            else
            {
                IxSend(Op.CMSG_DUEL_CANCELLED, new PacketWriter().U64(_ixDuel.FlagGuid), "CMSG_DUEL_CANCELLED (declining " + who + ")");
                IxDuelClear();
            }
            _ixDuel.Version++;
        }

        /// <summary>/yield /concede /forfeit (SLASH_DUEL_CANCEL1-3): CMSG_DUEL_CANCELLED - a pending challenge is withdrawn, a running duel
        /// lost (the server casts 7267 "Grovel" and the other wins, DuelHandler.cpp:52-75).</summary>
        public void ForfeitDuel()
        {
            if (_ixDuel.State == DuelState.None) return;
            if (_ixDuel.FlagGuid == 0)
            {
                WowLog.Info("Online duel: the challenge was not confirmed by the server yet - dropped locally");
                IxDuelClear();
                _ixDuel.Version++;
                return;
            }
            IxSend(Op.CMSG_DUEL_CANCELLED, new PacketWriter().U64(_ixDuel.FlagGuid), "CMSG_DUEL_CANCELLED (" + (_ixDuel.State == DuelState.Fighting ? "forfeit" : "withdraw") + ")");
            if (_ixDuel.State != DuelState.Fighting) { IxDuelClear(); _ixDuel.Version++; }   // a running duel ends with SMSG_DUEL_COMPLETE / _WINNER
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>SMSG_DUEL_REQUESTED (EffectDuel, SpellEffects.cpp:4339-4343): uint64 the flag (GAMEOBJECT_TYPE_DUEL_ARBITER), uint64 initiator.</summary>
        void IxDuelOnRequested(WowPacket p)
        {
            ulong flag = p.U64();
            ulong initiator = p.U64();
            _ixDuel.FlagGuid = flag;
            _ixDuel.OutOfBounds = false;
            _ixDuelAge = 0f;
            if (initiator == _player.Guid)
            {
                _ixDuel.State = DuelState.Requested;
                IxInfo("You have requested a duel.");                                     // ERR_DUEL_REQUESTED (GlobalStrings.lua:3036)
            }
            else
            {
                _ixDuel.State = DuelState.Incoming;
                _ixDuel.OpponentGuid = initiator;
                _ixDuel.OpponentName = IxName(initiator);
                _ixDuel.SecondsLeft = IxDuelPopupSeconds;
            }
            _ixDuel.Version++;
            WowLog.Info("Online duel: SMSG_DUEL_REQUESTED flag " + IxGuidText(flag) + ", initiator " + (initiator == _player.Guid ? "me" : _ixDuel.OpponentName ?? IxGuidText(initiator)) + " -> " + _ixDuel.State);
        }

        /// <summary>SMSG_DUEL_COUNTDOWN (Player::SendDuelCountdown, Player.cpp:15790-15795): uint32 3000 ms. DUEL_COUNTDOWN "Duel starting: %d"
        /// (GlobalStrings.lua:2027) - ours: the yellow line once a second (the C code's place).</summary>
        void IxDuelOnCountdown(WowPacket p)
        {
            uint ms = p.U32();
            _ixDuel.State = DuelState.Countdown;
            _ixDuel.SecondsLeft = ms / 1000f;
            _ixDuel.OutOfBounds = false;
            _ixDuelSaid = -1;
            IxDuelSayCountdown();
            _ixDuel.Version++;
            WowLog.Info("Online duel: SMSG_DUEL_COUNTDOWN " + ms + " ms with " + (_ixDuel.OpponentName ?? IxGuidText(_ixDuel.OpponentGuid)));
        }

        /// <summary>SMSG_DUEL_COMPLETE (DuelComplete, Player.cpp:6595-6738): uint8 1 finished / 0 interrupted (ERR_DUEL_CANCELLED :3035).</summary>
        void IxDuelOnComplete(WowPacket p)
        {
            bool finished = p.U8() != 0;
            if (!finished) IxInfo("Duel cancelled.");                                          // ERR_DUEL_CANCELLED (GlobalStrings.lua:3035)
            WowLog.Info("Online duel: SMSG_DUEL_COMPLETE (" + (finished ? "finished" : "interrupted") + ") with " + (_ixDuel.OpponentName ?? IxGuidText(_ixDuel.OpponentGuid)));
            IxDuelClear();
            _ixDuel.Version++;
        }

        /// <summary>SMSG_DUEL_WINNER (Player.cpp:6619-6625, to everyone around): uint8 0 won / 1 fled, string winner, string loser ->
        /// DUEL_WINNER_KNOCKOUT "%1$s has defeated %2$s in a duel" / DUEL_WINNER_RETREAT "%2$s has fled from %1$s in a duel" (:2030-2031).</summary>
        void IxDuelOnWinner(WowPacket p)
        {
            bool fled = p.U8() != 0;
            string winner = p.CString(), loser = p.CString();
            IxSystem(fled ? loser + " has fled from " + winner + " in a duel" : winner + " has defeated " + loser + " in a duel", false);
            WowLog.Info("Online duel: SMSG_DUEL_WINNER " + (fled ? "retreat" : "knockout") + " (" + winner + " / " + loser + ")");
        }

        // ------------------------------------------------------------------ per frame

        void IxDuelTick(float dt)
        {
            DuelView d = _ixDuel;
            if (d.State == DuelState.None) return;
            if (d.OpponentName == null && d.OpponentGuid != 0) { d.OpponentName = ChatSender(d.OpponentGuid); if (d.OpponentName != null) d.Version++; }
            // the server's start (UpdateDuelFlag, PlayerMisc.cpp:416-428): PLAYER_DUEL_TEAM set on us = IN_PROGRESS
            if (d.State != DuelState.Fighting && _self != null && _self.U32(UF.PLAYER_DUEL_TEAM) != 0 && _self.U64(UF.PLAYER_DUEL_ARBITER) != 0)
            {
                d.State = DuelState.Fighting;
                d.SecondsLeft = 0f;
                d.Version++;
                WowLog.Info("Online duel: PLAYER_DUEL_TEAM " + _self.U32(UF.PLAYER_DUEL_TEAM) + " - the duel with " + (d.OpponentName ?? IxGuidText(d.OpponentGuid)) + " is on (the opponent is hostile)");
                return;
            }
            switch (d.State)
            {
                case DuelState.Incoming:
                    d.SecondsLeft -= dt;
                    if (d.SecondsLeft <= 0f) { WowLog.Info("Online duel: the challenge timed out after " + IxDuelPopupSeconds + " s - declined (StaticPopup DUEL_REQUESTED)"); AnswerDuel(false); }
                    break;
                case DuelState.Requested:
                    _ixDuelAge += dt;
                    if ((d.FlagGuid == 0 && _ixDuelAge > IxDuelCastGuard) || _ixDuelAge > IxDuelAskGuard)
                    {
                        WowLog.Info("Online duel: the challenge to " + (d.OpponentName ?? IxGuidText(d.OpponentGuid)) + (d.FlagGuid == 0 ? " was not confirmed by the server (the cast failed)" : " got no answer") + " - dropped (ours)");
                        IxDuelClear();
                        d.Version++;
                    }
                    break;
                case DuelState.Countdown:
                    d.SecondsLeft -= dt;
                    IxDuelSayCountdown();
                    if (d.SecondsLeft <= 0f) { d.State = DuelState.Fighting; d.SecondsLeft = 0f; d.Version++; }
                    break;
                case DuelState.Fighting:
                    // DuelComplete resets PLAYER_DUEL_ARBITER / _TEAM to 0 (Player.cpp:6731-6734): a duel that ended without our seeing
                    // SMSG_DUEL_COMPLETE (a teleport, a lost packet) ends here.
                    if (_self != null && _self.U64(UF.PLAYER_DUEL_ARBITER) == 0 && d.FlagGuid != 0)
                    {
                        WowLog.Info("Online duel: PLAYER_DUEL_ARBITER back to 0 without SMSG_DUEL_COMPLETE - the duel is over");
                        IxDuelClear();
                        d.Version++;
                        break;
                    }
                    if (d.OutOfBounds)
                    {
                        float before = d.SecondsLeft;
                        d.SecondsLeft = System.Math.Max(0f, d.SecondsLeft - dt);
                        if ((int)System.Math.Ceiling(before) != (int)System.Math.Ceiling(d.SecondsLeft)) d.Version++;
                    }
                    break;
            }
        }

        void IxDuelSayCountdown()
        {
            int n = (int)System.Math.Ceiling(_ixDuel.SecondsLeft);
            if (n <= 0 || n == _ixDuelSaid) return;
            _ixDuelSaid = n;
            IxInfo("Duel starting: " + n);                                                     // DUEL_COUNTDOWN (GlobalStrings.lua:2027)
        }

        void IxDuelClear()
        {
            _ixDuel.State = DuelState.None;
            _ixDuel.OpponentGuid = 0;
            _ixDuel.OpponentName = null;
            _ixDuel.FlagGuid = 0;
            _ixDuel.SecondsLeft = 0f;
            _ixDuel.OutOfBounds = false;
            _ixDuelSaid = -1;
        }

        // ------------------------------------------------------------------ the reaction rule (hook H4, OnlineSession.Objects.cs ReactionOf)

        /// <summary>H4: this player is our duel opponent - our PLAYER_DUEL_ARBITER (UpdateFields.cs:151) is non-zero and equals theirs and
        /// both PLAYER_DUEL_TEAM (:158) are non-zero and different (the fields UpdateDuelFlag sets, PlayerMisc.cpp:416-428): always hostile
        /// (Unit.cpp:6895-6897 "duel - always hostile to opponent").</summary>
        bool DuelOpponent(OnlineObject o)
        {
            if (o == null || _self == null || o.TypeId != TypeId.Player) return false;
            ulong mine = _self.U64(UF.PLAYER_DUEL_ARBITER);
            if (mine == 0 || o.U64(UF.PLAYER_DUEL_ARBITER) != mine) return false;
            uint myTeam = _self.U32(UF.PLAYER_DUEL_TEAM), theirTeam = o.U32(UF.PLAYER_DUEL_TEAM);
            return myTeam != 0 && theirTeam != 0 && myTeam != theirTeam;
        }

        /// <summary>H4: the owning player of a pet / totem is our duel opponent (Unit.cpp:6889-6897 compares the owning players).</summary>
        bool DuelOpponentGuid(ulong playerGuid)
        {
            OnlineObject o;
            return playerGuid != 0 && _objects.TryGetValue(playerGuid, out o) && DuelOpponent(o);
        }
    }
}
