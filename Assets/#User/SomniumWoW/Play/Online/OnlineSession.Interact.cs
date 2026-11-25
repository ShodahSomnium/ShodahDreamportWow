// iteration 11 - owner interact (proposal-interact.md P1, 6.1 / 6.2): IWowInteractions online - the hub of the interaction partials
// (Trade / Duel / Inspect / Raid / Pvp here, Auction / Battleground / Lfg of the queues builder), their shared helpers (Ix*), and the
// summon request (gap D2 of w0play: SMSG_SUMMON_REQUEST / CMSG_SUMMON_RESPONSE).
//
// Shared helpers for every interaction partial (also the queues builder's): IxInfo(text) / IxError(text) / IxSystem(text, hud) (the
// GrpText pattern: a ChatMessage System line + the HUD's yellow line), IxName(guid) (ChatSender + one CMSG_NAME_QUERY), IxSend(op, w,
// what) (logged the 1st time and every 50th per kind), IxHandle(op, handler) (a packet shorter than its layout is logged once and
// ignored), IxOdd(op, what), IxDbc(name) (a DBC loaded once, a missing one logged once), IxYards(a, b), IxAreaName(areaId).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession : IWowInteractions
    {
        const int IxLogEvery = 50;   // ours: a send kind is logged the 1st time and then every 50th (the Group / Chat precedent)

        readonly Dictionary<string, int> _ixSends = new Dictionary<string, int>();
        readonly HashSet<string> _ixOddLogged = new HashSet<string>();
        readonly Dictionary<string, DbcTable> _ixDbc = new Dictionary<string, DbcTable>();
        SummonView _ixSummon;

        public IWowInteractions Interactions { get { return this; } }

        /// <summary>Online the realm has other players: every interaction is offered (offline: OfflineInteractions, false).</summary>
        public bool Available { get { return _world != null; } }

        partial void InitInteract()
        {
            IxTradeInit();
            IxDuelInit();
            IxInspectInit();
            IxRaidInit();       // a SECOND SMSG_GROUP_LIST handler: InitGroup (registered before) has rebuilt PlayerState.Group when it runs
            IxPvpInit();
            IxHandle(Op.SMSG_SUMMON_REQUEST, IxOnSummonRequest);
            MirrorInit();       // lead 2026-10-26: breath / fatigue (OnlineSession.Mirror.cs)
            ClockInit();        // lead 2026-11-03: the realm's time of day (OnlineSession.Clock.cs)
            InitAuction(); InitBattleground(); InitLfg();   // queues (partial methods, OnlineSession.Hooks11.cs)
            WowLog.Info("Online interact: trade, duels, inspect, raid (ready checks, raid target icons, sub groups), the PvP flag and summons are handled (iteration 11).");
        }

        partial void TickInteract(float dt)
        {
            IxTradeTick(dt);
            IxDuelTick(dt);
            IxInspectTick(dt);
            IxRaidTick(dt);
            IxPvpTick(dt);
            IxSummonTick(dt);
            MirrorTick(dt);
            ClockTick();
            TickAuction(dt); TickBattleground(dt); TickLfg(dt);   // queues
        }

        // ------------------------------------------------------------------ summons (gap D2; facts proposal-interact.md 3.6)

        /// <summary>A summon waiting for Accept / Cancel (StaticPopup "CONFIRM_SUMMON", StaticPopup.lua:2534-2558), null = none.</summary>
        public SummonView SummonRequest { get { return _ixSummon; } }

        /// <summary>
        /// SMSG_SUMMON_REQUEST (Player::SendSummonRequestFrom, Player.cpp:16708-16716): uint64 summoner, uint32 the summoner's zone (AreaTable
        /// id), uint32 MAX_PLAYER_SUMMON_DELAY * IN_MILLISECONDS (2 min, Player.h:923).
        /// </summary>
        void IxOnSummonRequest(WowPacket p)
        {
            ulong summoner = p.U64();
            int zone = (int)p.U32();
            uint ms = p.U32();
            _ixSummon = new SummonView { SummonerGuid = summoner, SummonerName = IxName(summoner), ZoneId = zone, ZoneName = IxAreaName(zone), SecondsLeft = ms / 1000f };
            WowLog.Info("Online interact: SMSG_SUMMON_REQUEST from " + (_ixSummon.SummonerName ?? IxGuidText(summoner)) + " to zone " + zone + " ('" + _ixSummon.ZoneName + "'), " + (ms / 1000) + " s to answer");
        }

        /// <summary>CONFIRM_SUMMON's Accept / Cancel: CMSG_SUMMON_RESPONSE (HandleSummonResponseOpcode, MovementHandler.cpp:870-892: uint64
        /// summoner, uint8 agree; ignored by the server while dead or in combat). Accept -> the server teleports (the existing teleport code).</summary>
        public void AnswerSummon(bool accept)
        {
            SummonView s = _ixSummon;
            if (s == null) { WowLog.Info("Online interact: summon answer (" + (accept ? "accept" : "cancel") + ") without a pending summon - ignored"); return; }
            _ixSummon = null;
            IxSend(Op.CMSG_SUMMON_RESPONSE, new PacketWriter().U64(s.SummonerGuid).U8(accept ? 1 : 0), "CMSG_SUMMON_RESPONSE " + (accept ? "accept" : "cancel") + " (" + (s.SummonerName ?? IxGuidText(s.SummonerGuid)) + ")");
        }

        void IxSummonTick(float dt)
        {
            SummonView s = _ixSummon;
            if (s == null) return;
            s.SecondsLeft -= dt;
            if (s.SummonerName == null) s.SummonerName = ChatSender(s.SummonerGuid);
            if (s.SecondsLeft > 0f) return;
            _ixSummon = null;   // the server's m_summon_expire has passed: it ignores a late answer (Player::IsSummonAsSpectator / SetSummonPoint)
            WowLog.Info("Online interact: the summon from " + (s.SummonerName ?? IxGuidText(s.SummonerGuid)) + " expired unanswered");
        }

        // ------------------------------------------------------------------ shared helpers (also the queues builder's partials)

        /// <summary>The HUD's yellow line (Info event).</summary>
        void IxInfo(string text) { _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text }); }

        /// <summary>The HUD's red line (Error event).</summary>
        void IxError(string text) { Error(text); }

        /// <summary>A WoW system line in the chat log (ChatMessage, ChatKind.System) and, unless hud is false, the HUD's yellow line.</summary>
        void IxSystem(string text, bool hud = true) { GrpText(text, hud); }

        /// <summary>The name of a player / unit (ChatSender: our names, name answers, chat, the group, the entity), null while unknown - a
        /// player's name is then asked once (CMSG_NAME_QUERY, NameQuery::Read: uint64).</summary>
        string IxName(ulong guid)
        {
            if (guid == 0) return null;
            string n = ChatSender(guid);
            if (n != null) return n;
            if (_world != null && IsPlayerGuid(guid) && _nameAsked.Add(guid))
            {
                _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(guid));
                _queriesSent++;
            }
            return null;
        }

        static string IxGuidText(ulong guid) { return "0x" + guid.ToString("X"); }

        /// <summary>Sends op (body w, null = empty). Logged the first time per kind (what's first word) and then every 50th. False offline.</summary>
        bool IxSend(ushort op, PacketWriter w, string what)
        {
            if (_world == null) return false;
            if (w != null) _world.Send(op, w);
            else _world.Send(op);
            string kind = what ?? Op.Name(op);
            int sp = kind.IndexOf(' ');
            string key = sp > 0 ? kind.Substring(0, sp) : kind;
            int n;
            _ixSends.TryGetValue(key, out n);
            _ixSends[key] = ++n;
            if (n == 1 || n % IxLogEvery == 0) WowLog.Info("Online interact: " + kind + (n > 1 ? " (#" + n + ")" : ""));
            return true;
        }

        /// <summary>Registers a handler that never throws on a short packet: logged once per opcode and ignored (the GrpOdd pattern).</summary>
        void IxHandle(ushort op, Action<WowPacket> handler)
        {
            _world.On(op, p =>
            {
                try { handler(p); }
                catch (PacketTooShortException) { IxOdd(Op.Name(op), "shorter than its layout (" + p.Data.Length + " bytes) - ignored"); }
            }, this);
        }

        void IxOdd(string opcode, string what)
        {
            if (_ixOddLogged.Add(opcode + what)) WowLog.Warn("Online interact: odd " + opcode + ": " + what + " (logged once)");
        }

        /// <summary>A DBC of the user's install, loaded once (null when missing: logged once).</summary>
        DbcTable IxDbc(string name)
        {
            DbcTable t;
            if (_ixDbc.TryGetValue(name, out t)) return t;
            t = null;
            try { t = _files != null ? Dbc.Load(_files, name) : null; }
            catch (Exception e) { WowLog.Warn("Online interact: " + name + ".dbc not read (" + e.Message + ")"); }
            if (t == null && _files != null) WowLog.Warn("Online interact: " + name + ".dbc missing - the names it gives stay empty");
            _ixDbc[name] = t;
            return t;
        }

        /// <summary>A localized string field of a DBC row by id ("" when the table / row is missing).</summary>
        string IxDbcText(string table, int id, int field)
        {
            DbcTable t = IxDbc(table);
            if (t == null || id <= 0) return "";
            int row = t.FindRow(id);
            if (row < 0) return "";
            try { return t.GetLocalizedString(row, field) ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>AreaTable.dbc AreaName_lang (field 11, DbcFields.AreaTable).</summary>
        string IxAreaName(int areaId) { return IxDbcText("AreaTable", areaId, DbcFields.AreaTable.AreaName_lang); }

        /// <summary>Ground distance in yards (WoW x / y; z added - the server's IsWithinDistInMap uses 3D).</summary>
        static float IxYards(WowPos a, WowPos b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
