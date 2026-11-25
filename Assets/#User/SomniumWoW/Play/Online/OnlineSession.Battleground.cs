// iteration 11 - owner queues (proposal-interact.md P3, section 6.2.7): battlegrounds and arenas online (wave-0 stub filled).
// Protocol = AzerothCore master: BattleGroundHandler.cpp (P/acsrc11/interact), BattlegroundMgr.cpp:196-270 (status / list / joined / left),
// :584-646 (BuildBattlegroundListPacket), Battleground.cpp:127-139 / :1373-1401 (MSG_PVP_LOG_DATA), Arena.cpp:32-63, Battleground.h:188-195,
// SharedDefines.h:3687-3692 / :3890-3909. Texts = GlobalStrings.lua of the 3.3.5a install (GS line in comments).
// InitBattleground / TickBattleground are partial methods (OnlineSession.Hooks11.cs) called by interact's InitInteract / TickInteract.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const uint BgNpcFlag = 0x100000;            // UNIT_NPC_FLAG_BATTLEMASTER (UnitDefines.h)
        const int BgRandomId = 32;                  // BATTLEGROUND_RB (SharedDefines.h:3749)
        const int BgAllArenasId = 6;                // BATTLEGROUND_AA (SharedDefines.h:3742)
        const ushort BgPortConst = 0x1F90;          // "0x1F90 constant?" (BattleGroundHandler.cpp:399, BattlegroundMgr.cpp:214)

        // BattlemasterList.dbc (measured 2026-10-03 with InteractDbcFacts: 32 fields): 1..8 MapID, 9 InstanceType (3 BG, 4 arena), 11 Name_lang,
        // 30 MinLevel, 31 MaxLevel (id 32 Random: 0 / 0)
        internal const int BgDbcMap0 = 1, BgDbcType = 9, BgDbcName = 11, BgDbcMinLevel = 30, BgDbcMaxLevel = 31;

        readonly BattlefieldView _bg = new BattlefieldView();
        readonly Dictionary<ulong, int> _bgRaceClass = new Dictionary<ulong, int>();   // name answers: race | class << 8 (scoreboard rows out of view)
        DbcTable _bgDbc;
        bool _bgDbcTried, _bgAskedAtLogin;
        double _bgLastScoreAsk = -100.0;

        /// <summary>Never null: the two queue slots, the open battlemaster list, the scoreboard.</summary>
        public BattlefieldView Battlefield { get { return _bg; } }

        partial void InitBattleground()
        {
            // npc H4: the battlemaster flag opens the list with CMSG_BATTLEMASTER_HELLO (BattleGroundHandler.cpp:37-63: uint64 npc)
            RegisterNpcService(BgNpcFlag, g => { QuSend(Op.CMSG_BATTLEMASTER_HELLO, new PacketWriter().U64(g)); return true; }, "battlemaster");
            QuOn(Op.SMSG_BATTLEFIELD_LIST, BgOnList);
            QuOn(Op.SMSG_BATTLEFIELD_STATUS, BgOnStatus);
            QuOn(Op.SMSG_GROUP_JOINED_BATTLEGROUND, BgOnGroupJoined);
            QuOn(Op.MSG_PVP_LOG_DATA, BgOnLog);
            QuOn(Op.SMSG_BATTLEGROUND_PLAYER_JOINED, p => BgOnPlayerJoinedLeft(p, true));
            QuOn(Op.SMSG_BATTLEGROUND_PLAYER_LEFT, p => BgOnPlayerJoinedLeft(p, false));
            // a SECOND handler of these opcodes (the movement / objects owners' run first): the real client asks for its queues after every
            // map change, and the name answers carry race / class for the scoreboard
            QuOn(Op.SMSG_NEW_WORLD, p => QuSend(Op.CMSG_BATTLEFIELD_STATUS, null));
            QuOn(Op.SMSG_NAME_QUERY_RESPONSE, BgOnNameAnswer);
            WowLog.Info("Online battlegrounds: battlemasters, the PvP window's list, queues (2 slots), the entry popup, scoreboard and leaving are handled (iteration 11).");
        }

        // ------------------------------------------------------------------ requests (IWowInteractions)

        /// <summary>The PvP window's list for one BattlemasterList.dbc id: CMSG_BATTLEFIELD_LIST (uint32 bgTypeId, uint8 fromWhere 1 = UI, uint8 canGainXP;
        /// BattleGroundHandler.cpp:368-391). canGainXP 1 = ours.</summary>
        public void RequestBattlefieldList(int bgTypeId)
        {
            if (_world == null || bgTypeId <= 0) return;
            QuSend(Op.CMSG_BATTLEFIELD_LIST, new PacketWriter().U32((uint)bgTypeId).U8(1).U8(1));
        }

        /// <summary>Join Battle / Join as Group: CMSG_BATTLEMASTER_JOIN (uint64 battlemaster - 0 accepted, uint32 bgTypeId, uint32 instance id
        /// (0 = first available), uint8 asGroup; BattleGroundHandler.cpp:72-296). As a group only for the leader.</summary>
        public void JoinBattlefield(int bgTypeId, int instanceId, bool asGroup)
        {
            if (_world == null || bgTypeId <= 0) return;
            if (asGroup && (_player.Group.Count == 0 || _player.GroupLeaderGuid != _player.Guid))
            {
                Error("You are not the party leader.");   // ERR_NOT_LEADER (GlobalStrings.lua:3313)
                return;
            }
            ulong master = _bg.List != null ? _bg.List.BattlemasterGuid : 0;
            QuSend(Op.CMSG_BATTLEMASTER_JOIN, new PacketWriter().U64(master).U32((uint)bgTypeId).U32((uint)Math.Max(0, instanceId)).U8(asGroup ? 1 : 0));
            WowLog.Info("Online battlegrounds: join " + BgName(bgTypeId) + (instanceId > 0 ? " " + instanceId : " (first available)") + (asGroup ? " as a group" : "") + " sent.");
        }

        public void CloseBattlefieldList()
        {
            if (_bg.List == null) return;
            _bg.List = null;
            _bg.Version++;
        }

        /// <summary>Enter Battle / Leave Queue: CMSG_BATTLEFIELD_PORT (uint8 arenaType, uint8 unk 1, uint32 bgTypeId, uint16 0x1F90, uint8 action 1 enter /
        /// 0 leave; BattleGroundHandler.cpp:393-401) with the slot's own values from SMSG_BATTLEFIELD_STATUS.</summary>
        public void AnswerBattlefieldInvite(int queueSlot, bool enter)
        {
            if (_world == null || queueSlot < 0 || queueSlot >= _bg.Queues.Length) return;
            BattlefieldQueueView q = _bg.Queues[queueSlot];
            if (q.Status == BattlefieldStatus.None) return;
            if (enter && q.Status != BattlefieldStatus.Invited) return;
            if (q.Status == BattlefieldStatus.InProgress) { LeaveBattlefield(); return; }
            QuSend(Op.CMSG_BATTLEFIELD_PORT, new PacketWriter().U8(q.ArenaType).U8(1).U32((uint)q.BgTypeId).U16(BgPortConst).U8(enter ? 1 : 0));
            WowLog.Info("Online battlegrounds: " + (enter ? "enter " : "leave the queue of ") + (q.Name ?? ("type " + q.BgTypeId)) + " (slot " + queueSlot + ").");
        }

        /// <summary>Leave Battleground / Leave Arena: CMSG_LEAVE_BATTLEFIELD (uint8 0, uint8 0, uint32 bgTypeId, uint16 0 - all skipped by the server,
        /// BattleGroundHandler.cpp:619-635; refused in combat until the battle has ended).</summary>
        public void LeaveBattlefield()
        {
            if (_world == null) return;
            int type = 0;
            for (int i = 0; i < _bg.Queues.Length; i++) if (_bg.Queues[i].Status == BattlefieldStatus.InProgress) type = _bg.Queues[i].BgTypeId;
            QuSend(Op.CMSG_LEAVE_BATTLEFIELD, new PacketWriter().U8(0).U8(0).U32((uint)type).U16(0));
        }

        /// <summary>The scoreboard: MSG_PVP_LOG_DATA (empty; BattleGroundHandler.cpp:349-366 - arenas answer only at the end).</summary>
        public void RequestScores()
        {
            if (_world == null) return;
            _bgLastScoreAsk = _now;
            QuSend(Op.MSG_PVP_LOG_DATA, null);
        }

        // ------------------------------------------------------------------ server answers

        /// <summary>SMSG_BATTLEFIELD_LIST (BattlegroundMgr.cpp:584-646): uint64 battlemaster, uint8 fromWhere, uint32 bgTypeId, uint8 0, uint8 0,
        /// uint8 hasWin, uint32 winHonor, uint32 winArena, uint32 lossHonor, uint8 isRandom, [random: uint8, uint32 x3], arena (6): uint32 0,
        /// else uint32 n + n x uint32 client instance id.</summary>
        void BgOnList(WowPacket p)
        {
            var v = new BattlefieldListView();
            v.BattlemasterGuid = p.U64();
            p.U8();
            v.BgTypeId = (int)p.U32();
            p.U8(); p.U8();
            v.HasWin = p.U8() != 0;
            v.WinHonor = (int)p.U32();
            v.WinArena = (int)p.U32();
            v.LossHonor = (int)p.U32();
            v.IsRandom = p.U8() != 0;
            if (v.IsRandom) { v.HasWin = p.U8() != 0; v.WinHonor = (int)p.U32(); v.WinArena = (int)p.U32(); v.LossHonor = (int)p.U32(); }
            int n = p.Remaining >= 4 ? (int)p.U32() : 0;
            for (int i = 0; i < n && p.Remaining >= 4; i++) v.Instances.Add((int)p.U32());
            v.Name = BgName(v.BgTypeId);
            int row = BgRow(v.BgTypeId);
            if (row >= 0) { v.MinLevel = _bgDbc.GetInt(row, BgDbcMinLevel); v.MaxLevel = _bgDbc.GetInt(row, BgDbcMaxLevel); }
            _bg.List = v;
            _bg.Version++;
            if (v.BattlemasterGuid != 0) NpcServiceOpened(v.BattlemasterGuid, "battlemaster");
            WowLog.Info("Online battlegrounds: list of " + v.Name + " (" + v.Instances.Count + " running instance(s)" + (v.BattlemasterGuid != 0 ? ", from a battlemaster" : ", PvP window") + ").");
        }

        /// <summary>SMSG_BATTLEFIELD_STATUS (BattlegroundMgr.cpp:196-246): uint32 slot; STATUS_NONE = uint64 0 only. Else uint8 arenaType, uint8 0xE
        /// arena / 0, uint32 bgTypeId, uint16 0x1F90, uint8 minLevel, uint8 maxLevel, uint32 client instance id, uint8 rated, uint32 status;
        /// 1 WAIT_QUEUE: uint32 avg ms, uint32 queued ms; 2 WAIT_JOIN: uint32 map, uint64 0, uint32 ms to removal; 3 IN_PROGRESS: uint32 map,
        /// uint64 0, uint32 ms to auto-leave, uint32 ms since start, uint8 1 alliance / 0 horde. 4 WAIT_LEAVE (Battleground.h:194) = the end.</summary>
        void BgOnStatus(WowPacket p)
        {
            int slot = (int)p.U32();
            if (slot < 0 || slot >= _bg.Queues.Length) { WowLog.Warn("Online battlegrounds: status for queue slot " + slot + " ignored (2 slots)."); return; }
            BattlefieldQueueView q = _bg.Queues[slot];
            BattlefieldStatus was = q.Status;
            if (p.Remaining == 8)
            {
                BgClear(q);
            }
            else
            {
                q.ArenaType = p.U8();
                p.U8();
                q.BgTypeId = (int)p.U32();
                p.U16();
                q.MinLevel = p.U8();
                q.MaxLevel = p.U8();
                q.ClientInstanceId = (int)p.U32();
                q.Rated = p.U8() != 0;
                uint status = p.U32();
                q.Name = BgQueueName(q.BgTypeId, q.ArenaType);
                switch (status)
                {
                    case 1:
                        q.Status = BattlefieldStatus.Queued;
                        q.AvgWaitSeconds = p.U32() / 1000f;
                        q.QueuedSeconds = p.U32() / 1000f;
                        q.SecondsLeft = 0f;
                        break;
                    case 2:
                        q.Status = BattlefieldStatus.Invited;
                        q.MapId = (int)p.U32();
                        p.U64();
                        q.SecondsLeft = p.U32() / 1000f;
                        break;
                    case 3:
                        q.Status = BattlefieldStatus.InProgress;
                        q.MapId = (int)p.U32();
                        p.U64();
                        q.SecondsLeft = p.U32() / 1000f;
                        q.SinceStartSeconds = p.U32() / 1000f;
                        q.Alliance = p.Remaining >= 1 && p.U8() != 0;
                        break;
                    case 4:
                        q.Status = BattlefieldStatus.InProgress;   // WAIT_LEAVE: the battle has ended, we are still inside
                        break;
                    default:
                        BgClear(q);
                        break;
                }
            }
            bool inside = false;
            for (int i = 0; i < _bg.Queues.Length; i++) if (_bg.Queues[i].Status == BattlefieldStatus.InProgress) inside = true;
            if (inside && !_bg.InBattleground) { _bg.Ended = false; _bg.Winner = -1; _bg.Scores.Clear(); }
            _bg.InBattleground = inside;
            _bg.Version++;
            if (q.Status != was) WowLog.Info("Online battlegrounds: queue slot " + slot + " " + was + " -> " + q.Status + (q.Status != BattlefieldStatus.None ? " (" + q.Name + ")" : "") + ".");
        }

        static void BgClear(BattlefieldQueueView q)
        {
            q.Status = BattlefieldStatus.None;
            q.BgTypeId = q.ArenaType = q.MapId = q.MinLevel = q.MaxLevel = q.ClientInstanceId = 0;
            q.Name = null;
            q.Rated = q.Alliance = false;
            q.AvgWaitSeconds = q.QueuedSeconds = q.SecondsLeft = q.SinceStartSeconds = 0f;
        }

        /// <summary>SMSG_GROUP_JOINED_BATTLEGROUND (BattlegroundMgr.cpp:248-254): int32 result [+ uint64 for -11 / -12]; texts per
        /// GroupJoinBattlegroundResult (SharedDefines.h:3890-3909) = GlobalStrings.lua lines (proposal-interact.md 5.5).</summary>
        void BgOnGroupJoined(WowPacket p)
        {
            int r = (int)p.U32();
            ulong who = p.Remaining >= 8 ? p.U64() : 0;
            string text = BgJoinText(r, BgName(r), who != 0 ? (QuName(who) ?? "Unknown") : "Unknown");   // UNKNOWN (GlobalStrings.lua:7899)
            WowLog.Info("Online battlegrounds: group join result " + r + ".");
            if (text == null) return;
            if (r > 0) GrpText(text);                                                   // a system line + the HUD line
            else _events.Enqueue(new GameEvent { Type = GameEventType.Error, Text = text });
        }

        internal static string BgJoinText(int r, string bgName, string who)
        {
            if (r > 0) return "Your group has joined the queue for " + (bgName ?? "?");                     // ERR_GROUP_JOIN_BATTLEGROUND_S (:3073)
            switch (r)
            {
                case 0: return "Your group has joined a battleground queue, but you are not eligible";      // _FAIL (:3072)
                case -2: return "You cannot join the battleground yet because you or one of your party members is flagged as a Deserter.";   // _DESERTERS (:3071)
                case -3: return "Incorrect party size for this arena.";                                     // ERR_ARENA_TEAM_PARTY_SIZE (:2899)
                case -4: return "You can only be queued for 2 battles at once";                             // ERR_BATTLEGROUND_TOO_MANY_QUEUES (:2965)
                case -5:
                case -6: return "You cannot queue for a rated match while queued for other battles";        // ERR_BATTLEGROUND_CANNOT_QUEUE_FOR_RATED (:2957)
                case -7: return "Your team has left the arena queue";                                       // ERR_BATTLEGROUND_TEAM_LEFT_QUEUE (:2964)
                case -8: return "You can't do that in a battleground.";                                     // ERR_BATTLEGROUND_NOT_IN_BATTLEGROUND (:2962)
                case -10: return "Cannot join the queue unless all members of your party are in the same battleground level range.";   // _JOIN_RANGE_INDEX (:2960)
                case -11: return (who ?? "Unknown") + " was unavailable to join the queue.";               // ERR_BATTLEGROUND_JOIN_TIMED_OUT (:2961)
                case -12: return "Join as a group failed";                                                  // ERR_BATTLEGROUND_JOIN_FAILED (:2959)
                case -13: return "You cannot queue for a battleground or arena while using the dungeon system.";   // ERR_LFG_CANT_USE_BATTLEGROUND (:3181)
                case -14: return "Can't do that while in a Random Battleground queue.";                     // ERR_IN_RANDOM_BG (:3158)
                case -15: return "Can't queue for Random Battleground while in another Battleground queue."; // ERR_IN_NON_RANDOM_BG (:3157)
                default: return null;                                                                       // -1 NONE / -9: nothing (SharedDefines.h:3894, :3902)
            }
        }

        /// <summary>MSG_PVP_LOG_DATA (Battleground.cpp:1373-1401): uint8 type (0 BG, 1 arena); arena: 2 x (uint32 lost, uint32 won, uint32 MMR) +
        /// 2 x string team (Arena.cpp:49-63); uint8 ended [+ uint8 winner 0 horde / 1 alliance]; uint32 n; per player BG (Battleground.cpp:127-139):
        /// uint64 guid, uint32 KB, HK, deaths, bonus honor, damage, healing, uint32 k + k x uint32; arena (Arena.cpp:32-47): uint64 guid, uint32 KB,
        /// uint8 team, uint32 damage, uint32 healing, uint32 0.</summary>
        void BgOnLog(WowPacket p)
        {
            bool arena = p.U8() != 0;
            if (arena)
            {
                for (int i = 0; i < 2; i++) { p.U32(); p.U32(); p.U32(); }
                _bg.ArenaTeams[0] = p.CString();
                _bg.ArenaTeams[1] = p.CString();
            }
            bool ended = p.U8() != 0;
            int winner = ended ? p.U8() : -1;
            int n = (int)p.U32();
            var rows = new List<BattlefieldScoreView>(Math.Min(n, 80));
            for (int i = 0; i < n; i++)
            {
                var s = new BattlefieldScoreView();
                s.Guid = p.U64();
                s.KillingBlows = (int)p.U32();
                if (arena)
                {
                    s.Team = p.U8();
                    s.Damage = (int)p.U32();
                    s.Healing = (int)p.U32();
                    int k = (int)p.U32();
                    for (int j = 0; j < k; j++) p.U32();
                }
                else
                {
                    s.HonorableKills = (int)p.U32();
                    s.Deaths = (int)p.U32();
                    s.BonusHonor = (int)p.U32();
                    s.Damage = (int)p.U32();
                    s.Healing = (int)p.U32();
                    int k = (int)p.U32();
                    s.Objectives = new int[Math.Max(0, Math.Min(k, 8))];
                    for (int j = 0; j < k; j++) { int o = (int)p.U32(); if (j < s.Objectives.Length) s.Objectives[j] = o; }
                }
                BgFillWho(s, !arena);
                rows.Add(s);
            }
            bool newlyEnded = ended && !_bg.Ended;
            _bg.ScoresArena = arena;
            _bg.Ended = ended;
            _bg.Winner = winner;
            _bg.Scores.Clear();
            _bg.Scores.AddRange(rows);
            _bg.Version++;
            if (newlyEnded) WowLog.Info("Online battlegrounds: the battle has ended (" + (winner == 1 ? "Alliance" : winner == 0 ? "Horde" : "?") + " wins, " + n + " players on the scoreboard).");
        }

        /// <summary>Name, race and class of a scoreboard row: the entity in view, else the name answers (BgOnNameAnswer), else one name query.</summary>
        void BgFillWho(BattlefieldScoreView s, bool teamFromRace)
        {
            s.Name = s.Guid == _player.Guid ? _player.Name : QuName(s.Guid);
            int race = 0, cls = 0;
            if (s.Guid == _player.Guid) { race = _player.Race; cls = (int)_player.Class; }
            else
            {
                OnlineObject o;
                if (_objects.TryGetValue(s.Guid, out o) && o.IsUnit) { race = o.Byte(UF.UNIT_FIELD_BYTES_0, 0); cls = o.Byte(UF.UNIT_FIELD_BYTES_0, 1); }
                int rc;
                if (race == 0 && _bgRaceClass.TryGetValue(s.Guid, out rc)) { race = rc & 0xFF; cls = (rc >> 8) & 0xFF; }
            }
            s.Race = race;
            s.Class = (WowClass)cls;
            if (teamFromRace && race > 0) s.Team = OnlineDb.FactionOfRace(race) == WowFaction.Alliance ? 1 : 0;   // the client's GetBattlefieldScore faction (C code - ours: from ChrRaces)
        }

        /// <summary>SMSG_NAME_QUERY_RESPONSE, a second reader (Objects.cs keeps the name): packed guid, uint8 unknown, [name, realm, uint8 race, sex, class].</summary>
        void BgOnNameAnswer(WowPacket p)
        {
            ulong guid = p.PackedGuid();
            if (p.U8() != 0) return;
            p.CString(); p.CString();
            int race = p.U8();
            p.U8();
            int cls = p.U8();
            _bgRaceClass[guid] = race | cls << 8;
            if (_bg.Scores.Count == 0) return;
            for (int i = 0; i < _bg.Scores.Count; i++)
            {
                BattlefieldScoreView s = _bg.Scores[i];
                if (s.Guid != guid) continue;
                BgFillWho(s, !_bg.ScoresArena);
                _bg.Version++;
            }
        }

        /// <summary>SMSG_BATTLEGROUND_PLAYER_JOINED / _LEFT (BattlegroundMgr.cpp:256-266): uint64. Logged; an open scoreboard asks again (ours).</summary>
        void BgOnPlayerJoinedLeft(WowPacket p, bool joined)
        {
            ulong g = p.U64();
            WowLog.Info("Online battlegrounds: a player " + (joined ? "joined" : "left") + " the battle (" + (ChatSender(g) ?? "0x" + g.ToString("X")) + ").");
            if (_now - _bgLastScoreAsk < 10.0) RequestScores();
        }

        partial void TickBattleground(float dt)
        {
            if (!_bgAskedAtLogin && _world != null)
            {
                _bgAskedAtLogin = true;
                QuSend(Op.CMSG_BATTLEFIELD_STATUS, null);   // the real client asks for its queues at login (BattleGroundHandler.cpp:637 HandleBattlefieldStatusOpcode)
            }
            for (int i = 0; i < _bg.Queues.Length; i++)
            {
                BattlefieldQueueView q = _bg.Queues[i];
                if (q.Status == BattlefieldStatus.Queued) q.QueuedSeconds += dt;
                else if (q.Status == BattlefieldStatus.Invited || q.Status == BattlefieldStatus.InProgress)
                {
                    if (q.SecondsLeft > 0f) q.SecondsLeft = Math.Max(0f, q.SecondsLeft - dt);
                    if (q.Status == BattlefieldStatus.InProgress) q.SinceStartSeconds += dt;
                }
            }
        }

        // ------------------------------------------------------------------ BattlemasterList.dbc

        DbcTable BgTable()
        {
            if (_bgDbc == null && !_bgDbcTried)
            {
                _bgDbcTried = true;
                try { _bgDbc = _files != null ? Dbc.Load(_files, "BattlemasterList") : null; }
                catch (Exception ex) { WowLog.Warn("Online battlegrounds: BattlemasterList.dbc not read (" + ex.Message + ") - battles are shown by id."); }
            }
            return _bgDbc;
        }

        int BgRow(int id)
        {
            DbcTable t = BgTable();
            return t != null && id > 0 ? t.FindRow(id) : -1;
        }

        /// <summary>BattlemasterList.dbc Name_lang of the id; "Battleground &lt;id&gt;" (ours) when unknown.</summary>
        internal string BgName(int id)
        {
            int row = BgRow(id);
            string n = row >= 0 ? _bgDbc.GetLocalizedString(row, BgDbcName) : null;
            return string.IsNullOrEmpty(n) ? "Battleground " + id : n;
        }

        /// <summary>The PvP window's Battlegrounds tab (PVPBattlegroundFrame, iteration 11 UI - not in the frozen contract): BattlemasterList.dbc rows of
        /// InstanceType 3 (field 9) whose level range (fields 30 / 31) holds `level`, Random Battleground (id 32, 0 / 0) first. Fresh rows each call
        /// (BgTypeId, Name, MinLevel, MaxLevel, IsRandom); empty when the table is not readable.</summary>
        public List<BattlefieldListView> BgWindowRows(int level)
        {
            var rows = new List<BattlefieldListView>();
            DbcTable t = BgTable();
            if (t == null) return rows;
            for (int r = 0; r < t.RecordCount; r++)
            {
                int id = t.GetId(r), min = t.GetInt(r, BgDbcMinLevel), max = t.GetInt(r, BgDbcMaxLevel);
                bool random = id == BgRandomId;
                if (!random && (t.GetInt(r, BgDbcType) != 3 || level < min || (max > 0 && level > max))) continue;
                var v = new BattlefieldListView { BgTypeId = id, Name = BgName(id), MinLevel = min, MaxLevel = max, IsRandom = random };
                if (random) rows.Insert(0, v); else rows.Add(v);
            }
            return rows;
        }

        /// <summary>The queue's name: the battle's name; arenas (BATTLEGROUND_AA or arenaType 2 / 3 / 5) "Arena 2v2" style (ours, from ArenaType).</summary>
        string BgQueueName(int bgTypeId, int arenaType)
        {
            if (arenaType > 0 || bgTypeId == BgAllArenasId)
                return arenaType > 0 ? "Arena " + arenaType + "v" + arenaType : BgName(bgTypeId);
            return BgName(bgTypeId);
        }
    }
}
