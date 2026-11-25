// iteration 11 - owner interact (proposal-interact.md 3.5 / 5.5 / 6.2.5): the PvP flag, the PvP icons of unit frames, report AFK.
// AzerothCore master: src/server/game/Handlers/MiscHandler.cpp:500-519 (HandleTogglePvP), Entities/Player/Player.h:460-477 (PlayerFlags),
// Entities/Unit/UnitDefines.h:139-146 (UnitBytes2Flags), Handlers/BattleGroundHandler.cpp (HandleReportPvPAFK).
using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const uint IxPlayerFlagInPvp = 0x200, IxPlayerFlagPvpTimer = 0x40000;   // PLAYER_FLAGS_IN_PVP / PLAYER_FLAGS_PVP_TIMER (Player.h:460-477)
        const int IxBytes2Pvp = 0x01, IxBytes2Ffa = 0x04;                       // UNIT_BYTE2_FLAG_PVP / _FFA_PVP (UNIT_FIELD_BYTES_2 byte 1)
        const float IxPvpTimerSeconds = 300f;          // ours: the five minutes of PVP_TOGGLE_OFF_VERBOSE counted from when the timer flag appears
        const float IxPvpPoll = 0.25f;                 // ours: the flags are read 4x a second
        const float IxPvpAskWindow = 10f;              // ours: a flag change within 10 s of our CMSG_TOGGLE_PVP is the one we caused (texts)

        readonly PvpView _ixPvp = new PvpView();
        float _ixPvpPoll, _ixPvpAskAge = float.MaxValue;
        int _ixPvpAsked = -1;                          // the PLAYER_FLAGS_IN_PVP value we asked for, -1 = none
        bool _ixPvpSeen;

        public PvpView Pvp { get { return _ixPvp; } }

        void IxPvpInit() { }

        /// <summary>PVP_ENABLE / PVP_DISABLE (SetPVP(1 / nil), UnitPopup.lua:1318-1321): CMSG_TOGGLE_PVP with the explicit uint8 1 / 0
        /// (MiscHandler.cpp:502-507; empty would toggle).</summary>
        public void SetPvp(bool on)
        {
            if (_world == null) { Error(NotYet); return; }
            _ixPvpAsked = on ? 1 : 0;
            _ixPvpAskAge = 0f;
            IxSend(Op.CMSG_TOGGLE_PVP, new PacketWriter().U8(on ? 1 : 0), "CMSG_TOGGLE_PVP " + (on ? "on" : "off"));
        }

        /// <summary>The PvP icon a unit frame shows (TargetFrame_CheckFaction / PlayerFrame_UpdatePvPStatus): 3 FFA (UI-PVP-FFA), else when
        /// PvP-flagged the unit's faction group - players by race, others by FactionTemplate.dbc OurMask (field 3: 2 Alliance, 4 Horde; else
        /// none, as UnitFactionGroup returns nil) - 1 UI-PVP-Alliance, 2 UI-PVP-Horde; 0 = none.</summary>
        public int UnitPvpIcon(ulong unitGuid)
        {
            if (unitGuid == 0) return 0;
            if (unitGuid == _player.Guid)
            {
                if (_ixPvp.FreeForAll) return 3;
                if (!_ixPvp.Flagged) return 0;
                return OnlineDb.FactionOfRace(_player.Race) == WowFaction.Horde ? 2 : 1;
            }
            OnlineObject o;
            if (!_objects.TryGetValue(unitGuid, out o) || !o.IsUnit) return 0;
            int b2 = o.Byte(UF.UNIT_FIELD_BYTES_2, 1);
            if ((b2 & IxBytes2Ffa) != 0) return 3;
            if ((b2 & IxBytes2Pvp) == 0) return 0;
            if (o.TypeId == TypeId.Player)
            {
                int race = o.Byte(UF.UNIT_FIELD_BYTES_0, 0);
                return race <= 0 ? 0 : OnlineDb.FactionOfRace(race) == WowFaction.Horde ? 2 : 1;
            }
            int mask = IxDbcInt("FactionTemplate", o.I32(UF.UNIT_FIELD_FACTIONTEMPLATE), OnlineDb.FtOurMask);
            return (mask & 2) != 0 ? 1 : (mask & 4) != 0 ? 2 : 0;
        }

        /// <summary>PVP_REPORT_AFK (in a battleground): CMSG_REPORT_PVP_AFK (uint64 player; BattleGroundHandler.cpp HandleReportPvPAFK).</summary>
        public void ReportPvpAfk(ulong playerGuid)
        {
            if (_world == null || playerGuid == 0) return;
            if (_player.MapInstanceType != 3 && !_ixRaid.IsBattlegroundGroup) { WowLog.Info("Online pvp: Report player Away outside a battleground - ignored"); return; }
            IxSend(Op.CMSG_REPORT_PVP_AFK, new PacketWriter().U64(playerGuid), "CMSG_REPORT_PVP_AFK " + (IxName(playerGuid) ?? IxGuidText(playerGuid)));
        }

        /// <summary>4x a second (ours) from our own object: Flagged / FreeForAll (UNIT_FIELD_BYTES_2 byte 1), Desired / TimerRunning
        /// (PLAYER_FLAGS), the kills (PLAYER_FIELD_KILLS: today low uint16, yesterday high) and the contributions. ERR_PVP_TOGGLE_ON / OFF
        /// (GlobalStrings.lua:3403-3404) + PVP_TOGGLE_ON_VERBOSE / OFF_VERBOSE (:5869-5870) when the flag WE asked for appears (the client's C
        /// code prints them - ours: on the change we caused).</summary>
        void IxPvpTick(float dt)
        {
            PvpView v = _ixPvp;
            if (_ixPvpAskAge < float.MaxValue) _ixPvpAskAge += dt;
            if (v.TimerRunning && v.TimerSecondsLeft > 0f)
            {
                float before = v.TimerSecondsLeft;
                v.TimerSecondsLeft = Math.Max(0f, v.TimerSecondsLeft - dt);
                if ((int)before != (int)v.TimerSecondsLeft) v.Version++;
            }
            _ixPvpPoll -= dt;
            if (_ixPvpPoll > 0f || _self == null) return;
            _ixPvpPoll = IxPvpPoll;
            uint flags = _self.U32(UF.PLAYER_FLAGS);
            int b2 = _self.Byte(UF.UNIT_FIELD_BYTES_2, 1);
            bool flagged = (b2 & IxBytes2Pvp) != 0, ffa = (b2 & IxBytes2Ffa) != 0;
            bool desired = (flags & IxPlayerFlagInPvp) != 0, timer = (flags & IxPlayerFlagPvpTimer) != 0;
            uint kills = _self.U32(UF.PLAYER_FIELD_KILLS);
            int today = (int)(kills & 0xFFFF), yesterday = (int)(kills >> 16);
            int honorToday = _self.I32(UF.PLAYER_FIELD_TODAY_CONTRIBUTION), honorYesterday = _self.I32(UF.PLAYER_FIELD_YESTERDAY_CONTRIBUTION);
            bool changed = flagged != v.Flagged || ffa != v.FreeForAll || desired != v.Desired || timer != v.TimerRunning
                           || today != v.TodayKills || yesterday != v.YesterdayKills || honorToday != v.TodayHonor || honorYesterday != v.YesterdayHonor;
            if (!changed) { _ixPvpSeen = true; return; }
            if (timer && !v.TimerRunning) v.TimerSecondsLeft = IxPvpTimerSeconds;
            if (!timer) v.TimerSecondsLeft = 0f;
            if (_ixPvpSeen && desired != v.Desired && _ixPvpAsked == (desired ? 1 : 0) && _ixPvpAskAge <= IxPvpAskWindow)
            {
                if (desired) { IxInfo("PvP combat toggled on"); IxSystem("You are now flagged for PvP combat and will remain so until toggled off.", false); }   // ERR_PVP_TOGGLE_ON (:3404), PVP_TOGGLE_ON_VERBOSE (:5870)
                else { IxInfo("PvP combat toggled off"); IxSystem("You will be unflagged for PvP combat after five minutes of non-PvP action in friendly territory.", false); }   // ERR_PVP_TOGGLE_OFF (:3403), PVP_TOGGLE_OFF_VERBOSE (:5869)
                _ixPvpAsked = -1;
            }
            if (flagged != v.Flagged || desired != v.Desired || ffa != v.FreeForAll || timer != v.TimerRunning)
                WowLog.Info("Online pvp: " + (flagged ? "flagged" : "not flagged") + (ffa ? " (FFA)" : "") + ", wants PvP " + desired + (timer ? ", the 5-minute timer runs" : ""));
            v.Flagged = flagged; v.FreeForAll = ffa; v.Desired = desired; v.TimerRunning = timer;
            v.TodayKills = today; v.YesterdayKills = yesterday; v.TodayHonor = honorToday; v.YesterdayHonor = honorYesterday;
            v.Version++;
            _ixPvpSeen = true;
        }
    }
}
