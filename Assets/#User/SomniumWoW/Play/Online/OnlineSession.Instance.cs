// iteration 11 - owner world (proposal-world.md 2.1-2.3): area triggers (portals, exploration quests, inns, battleground buffs), dungeon /
// raid difficulty, instance resets, Raid Info, the instance lock warning, the instance boot timer, transfer-abort and exploration texts,
// and the player's rest / explored fields. AzerothCore master file:line in the comments (copies in P/acsrc11/world).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.World;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        // ---- area triggers (2.1)
        AreaTriggerTable _trigTable;
        IReadOnlyList<AreaTriggerDef> _trigOnMap;
        int _trigMap = -1, _trigTeleports = -1;
        bool _trigPrimed;
        readonly HashSet<int> _trigInside = new HashSet<int>();
        readonly List<int> _trigQueue = new List<int>();
        readonly HashSet<int> _trigPrimeLogged = new HashSet<int>();
        double _trigQueuedAt = -1;
        WowPos _trigLastPos;
        bool _trigHaveLast;
        const float TrigLeaveMargin = 0.5f;     // ours: hysteresis - out by more than 0.5 yd before a volume counts as left
        const float TrigSampleStep = 1f;        // ours: a frame step longer than 1 yd (a hitch) is also tested every 1 yd along the way
        const float TrigFlushDistance = 0.25f;  // ours: the server's copy of our position is refreshed first when it is this far off
        const double TrigFallWait = 2.0;        // ours: in the air the report waits for the landing, at most 2 s

        // ---- instance state (2.3)
        int _instMap = int.MinValue;
        int _dungeonAsked = -1, _raidAsked = -1;
        double _diffAskAt = -10, _dungeonAskedAt, _raidAskedAt, _raidInfoAskAt = -10;
        bool _dungeonKnown, _raidKnown, _instResting, _instRestKnown;
        int _dungeonPrev = -1, _raidPrev = -1;   // the value before our own request (AC answers a refusal with it)
        readonly List<double> _saveEnds = new List<double>();   // session clock when each SavedInstances row reaches 0 (a float of ~600000 s cannot count down by frames)
        const double DiffMinGap = 1.1;          // ours: AC kicks above 3 difficulty packets per second (antidos_opcode_policies.sql:90, :136)
        const double DiffAnswerWait = 3.0;      // ours: a request still unanswered after 3 s is forgotten

        partial void InitInstance()
        {
            _trigTable = AreaTriggerTable.Load(_files);
            _world.On(Op.SMSG_AREA_TRIGGER_MESSAGE, OnAreaTriggerMessage, this);
            _world.On(Op.SMSG_CORPSE_NOT_IN_INSTANCE, OnCorpseNotInInstance, this);
            _world.On(Op.SMSG_EXPLORATION_EXPERIENCE, OnExplorationExperience, this);
            _world.On(Op.MSG_SET_DUNGEON_DIFFICULTY, OnDungeonDifficulty, this);
            _world.On(Op.MSG_SET_RAID_DIFFICULTY, OnRaidDifficulty, this);
            _world.On(Op.SMSG_INSTANCE_DIFFICULTY, OnInstanceDifficulty, this);
            _world.On(Op.SMSG_INSTANCE_RESET, OnInstanceReset, this);
            _world.On(Op.SMSG_INSTANCE_RESET_FAILED, OnInstanceResetFailed, this);
            _world.On(Op.SMSG_RESET_FAILED_NOTIFY, OnResetFailedNotify, this);
            _world.On(Op.SMSG_RAID_INSTANCE_INFO, OnRaidInstanceInfo, this);
            _world.On(Op.SMSG_UPDATE_INSTANCE_OWNERSHIP, OnInstanceOwnership, this);
            _world.On(Op.SMSG_UPDATE_LAST_INSTANCE, OnLastInstance, this);
            _world.On(Op.SMSG_INSTANCE_SAVE_CREATED, OnInstanceSaveCreated, this);
            _world.On(Op.SMSG_RAID_INSTANCE_MESSAGE, OnRaidInstanceMessage, this);
            _world.On(Op.SMSG_INSTANCE_LOCK_WARNING_QUERY, OnInstanceLockQuery, this);
            _world.On(Op.SMSG_RAID_GROUP_ONLY, OnRaidGroupOnly, this);
            InstNoteMap();
            WowLog.Info("Online instances: " + (_trigTable != null ? _trigTable.Count + " area triggers on " + _trigTable.MapCount + " maps (AreaTrigger.dbc)" : "no area trigger table")
                        + "; ours: leave margin " + TrigLeaveMargin + " yd, hitch sampling every " + TrigSampleStep + " yd, position flush above " + TrigFlushDistance
                        + " yd, fall wait " + TrigFallWait + " s, difficulty requests at most one per " + DiffMinGap + " s");
        }

        partial void TickInstance(float dt)
        {
            if (_mapId != _instMap) InstNoteMap();
            TickAreaTriggers();
            InstReadSelf();
            if (dt > 0f)
            {
                var saves = _player.SavedInstances;
                for (int i = 0; i < saves.Count && i < _saveEnds.Count; i++) saves[i].SecondsLeft = (float)Math.Max(0.0, _saveEnds[i] - _now);
                var lk = _player.PendingInstanceLock;
                if (lk != null)
                {
                    lk.SecondsLeft -= dt;
                    if (lk.SecondsLeft <= 0f)
                    {
                        _player.PendingInstanceLock = null;   // the server binds us itself when the time runs out (PlayerUpdates.cpp:351-362)
                        WowLog.Info("Online instances: the instance lock warning for map " + lk.MapId + " timed out unanswered (the server saves you to it)");
                    }
                }
                if (_player.InstanceBootSeconds > 0f) _player.InstanceBootSeconds = Math.Max(0f, _player.InstanceBootSeconds - dt);
            }
            if (_dungeonAsked >= 0 && _now - _dungeonAskedAt > DiffAnswerWait) { WowLog.Info("Online instances: dungeon difficulty " + _dungeonAsked + " - no answer from the server"); _dungeonAsked = -1; }
            if (_raidAsked >= 0 && _now - _raidAskedAt > DiffAnswerWait) { WowLog.Info("Online instances: raid difficulty " + _raidAsked + " - no answer from the server"); _raidAsked = -1; }
        }

        /// <summary>The player's map changed (login, SMSG_NEW_WORLD): its Map.dbc type; the lock warning and boot timer belong to the old map.</summary>
        void InstNoteMap()
        {
            int old = _instMap;
            _instMap = _mapId;
            _player.MapInstanceType = WowInstances.InstanceType(_files, _mapId);
            if (_player.PendingInstanceLock != null) { _player.PendingInstanceLock = null; WowLog.Info("Online instances: the instance lock warning was cleared by the map change"); }
            if (_player.InstanceBootSeconds > 0f) _player.InstanceBootSeconds = 0f;
            WowLog.Info("Online instances: map " + (old == int.MinValue ? "-" : old.ToString()) + " -> " + _mapId + " '" + WowInstances.MapName(_files, _mapId) + "' instance type " + _player.MapInstanceType
                        + (WowInstances.IsWmoOnly(_files, _mapId) ? " (WMO-only)" : ""));
        }

        /// <summary>PLAYER_FLAGS_RESTING 0x20 (Player.h:464), PLAYER_REST_STATE_EXPERIENCE, PLAYER_EXPLORED_ZONES_1 .. +127 of our own object.</summary>
        void InstReadSelf()
        {
            var o = _self;
            if (o == null || o.V == null) return;
            bool resting = (o.U32(UF.PLAYER_FLAGS) & 0x20u) != 0;
            _player.Resting = resting;
            _player.RestedXp = (int)o.U32(UF.PLAYER_REST_STATE_EXPERIENCE);
            if (o.V.Length >= UF.PLAYER_EXPLORED_ZONES_1 + 128)
            {
                var z = _player.ExploredZones;
                if (z == null) _player.ExploredZones = z = new uint[128];
                for (int i = 0; i < 128; i++) z[i] = o.V[UF.PLAYER_EXPLORED_ZONES_1 + i];
            }
            if (!_instRestKnown || resting != _instResting)
            {
                if (_instRestKnown) WowLog.Info("Online instances: " + (resting ? "resting (an inn or a city)" : "no longer resting") + " at " + _player.Pos + ", rested XP " + _player.RestedXp);
                _instRestKnown = true;
                _instResting = resting;
            }
        }

        // ------------------------------------------------------------------ area triggers (2.1)

        void TickAreaTriggers()
        {
            if (_trigTable == null) return;
            if (_mapId != _trigMap)
            {
                _trigMap = _mapId;
                _trigOnMap = _trigTable.OnMap(_mapId);
                _trigInside.Clear();
                _trigQueue.Clear();
                _trigPrimed = false;
            }
            if (!_haveAvatar || _holdUntil >= 0 || _player.Guid == 0 || _world == null || _world.IsClosed) { _trigPrimed = false; _trigHaveLast = false; return; }
            if (_teleports != _trigTeleports) { _trigTeleports = _teleports; _trigPrimed = false; _trigHaveLast = false; }
            if (_player.OnTaxi) { _trigHaveLast = false; return; }   // AC ignores triggers in flight (MiscHandler.cpp:699-705); the inside set keeps its state
            var list = _trigOnMap;
            var pos = _avatarPos;
            if (!_trigPrimed)
            {
                _trigInside.Clear();
                _trigQueue.Clear();
                for (int i = 0; i < list.Count; i++) if (list[i].Contains(pos.X, pos.Y, pos.Z, 0f)) _trigInside.Add(list[i].Id);
                _trigPrimed = true;
                _trigLastPos = pos;
                _trigHaveLast = true;
                if (_trigPrimeLogged.Add(_mapId))
                    WowLog.Info("Online instances: primed on map " + _mapId + " - inside " + _trigInside.Count + " trigger(s) at arrival (ours: a volume you arrive in is reported only after leaving it and coming back; logged once per map)");
                return;
            }
            float sx = 0f, sy = 0f, sz = 0f; int samples = 0;
            if (_trigHaveLast)
            {
                sx = pos.X - _trigLastPos.X; sy = pos.Y - _trigLastPos.Y; sz = pos.Z - _trigLastPos.Z;
                float step = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (step > TrigSampleStep && step < 200f) samples = (int)Math.Floor(step / TrigSampleStep);
            }
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                bool inSet = _trigInside.Contains(t.Id);
                if (!inSet)
                {
                    bool hit = t.Contains(pos.X, pos.Y, pos.Z, 0f);
                    for (int k = 1; !hit && k <= samples; k++)
                    {
                        float f = k / (float)(samples + 1);
                        hit = t.Contains(_trigLastPos.X + sx * f, _trigLastPos.Y + sy * f, _trigLastPos.Z + sz * f, 0f);
                    }
                    if (hit)
                    {
                        _trigInside.Add(t.Id);
                        if (!_trigQueue.Contains(t.Id)) _trigQueue.Add(t.Id);
                        if (_trigQueuedAt < 0) _trigQueuedAt = _now;
                    }
                }
                else if (!t.Contains(pos.X, pos.Y, pos.Z, TrigLeaveMargin)) _trigInside.Remove(t.Id);
            }
            _trigLastPos = pos;
            _trigHaveLast = true;
            if (_trigQueue.Count > 0) InstFlushTriggers(pos);
        }

        /// <summary>2.1 step 6: AC tests a trigger against the position it last received (MiscHandler.cpp:714-721), so our position goes out
        /// first (one MSG_MOVE_HEARTBEAT with the flags the movement / rider sender last sent), then one CMSG_AREATRIGGER uint32 id per entered
        /// volume (MiscHandler.cpp:693-694). The server keeps the order (one receive queue per session, WorldSession.cpp:65-105).</summary>
        void InstFlushTriggers(WowPos pos)
        {
            bool inAir = _airborne || (_sentFlags & (MoveFlags.Falling | MoveFlags.FallingFar)) != 0;
            if (inAir && _now - _trigQueuedAt < TrigFallWait) return;
            // the server moves us / a vehicle or a transport carries us: its own copy of our position is the one that counts
            bool serverMoves = _rooted || _vehMover != 0 || _vehSeat != null || _transportGuid != 0 || _player.ForcedMove != null || _player.ControlLost;
            float dx = pos.X - _sentPos.X, dy = pos.Y - _sentPos.Y, dz = pos.Z - _sentPos.Z;
            if (!serverMoves && !inAir && dx * dx + dy * dy + dz * dz > TrigFlushDistance * TrigFlushDistance)
            {
                uint flags = _sentFlags & ~(MoveFlags.Falling | MoveFlags.FallingFar);
                SendMove(Op.MSG_MOVE_HEARTBEAT, flags, pos, _sentPos.O, 0, 0, 0, 0, 0, (flags & (MoveFlags.Flying | MoveFlags.Swimming)) != 0 ? _sentPitch : 0f);
            }
            for (int i = 0; i < _trigQueue.Count; i++)
            {
                int id = _trigQueue[i];
                _world.Send(Op.CMSG_AREATRIGGER, new PacketWriter().U32((uint)id));
                _player.AreaTriggersSent++;
                AreaTriggerDef d;
                string shape = _trigTable.TryGet(id, out d) ? d.Describe() : "?";
                WowLog.Info("Online instances: area trigger " + id + " (map " + _mapId + ", " + shape + ") entered at " + pos + " - CMSG_AREATRIGGER sent");
            }
            _trigQueue.Clear();
            _trigQueuedAt = -1;
        }

        // ------------------------------------------------------------------ messages (2.3)

        /// <summary>"msg" = one Info (yellow) + one system chat line; "err" = one Error (red) + the system chat line (ours: the colour / chat
        /// placement of C++-side messages is not in FrameXML).</summary>
        void InstMsg(string text, bool error)
        {
            if (string.IsNullOrEmpty(text)) return;
            _events.Enqueue(new GameEvent { Type = error ? GameEventType.Error : GameEventType.Info, Text = text });
            _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)ChatKind.System, Text = text });
        }

        void InstChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)ChatKind.System, Text = text });
        }

        /// <summary>SMSG_TRANSFER_ABORTED (uint32 map, uint8 reason, uint8 arg for 7 / 8 / 9 - Player.cpp:11949-11966) -> WoW's text.</summary>
        void InstTransferAborted(int map, int reason, int arg)
        {
            string text = WowInstances.TransferAbortText(_files, map, reason, arg);
            InstMsg(text, true);
            WowLog.Warn("Online world: transfer to map " + map + " aborted, reason " + reason + " arg " + arg + ": " + text);
        }

        /// <summary>SMSG_AREA_TRIGGER_MESSAGE (WorldSession.cpp:288-299): uint32 length, C string (the length is not trusted).</summary>
        void OnAreaTriggerMessage(WowPacket p)
        {
            if (p.Remaining >= 4) p.U32();
            string text = p.Remaining > 0 ? p.CString() : "";
            InstMsg(text, true);
            WowLog.Info("Online instances: area trigger message '" + text + "'");
        }

        /// <summary>SMSG_CORPSE_NOT_IN_INSTANCE (empty, MapMgr.cpp:208-209) -> ERR_CORPSE_IS_NOT_IN_INSTANCE (GlobalStrings.lua:3015).</summary>
        void OnCorpseNotInInstance(WowPacket p)
        {
            InstMsg(WowInstances.G("ERR_CORPSE_IS_NOT_IN_INSTANCE", "Your corpse is not in that instance"), true);
            WowLog.Info("Online instances: SMSG_CORPSE_NOT_IN_INSTANCE");
        }

        /// <summary>SMSG_EXPLORATION_EXPERIENCE uint32 area, uint32 xp (PlayerMisc.cpp:161-167) -> ERR_ZONE_EXPLORED_XP / ERR_ZONE_EXPLORED
        /// (GlobalStrings.lua:3603 / :3602) with the AreaTable.dbc name.</summary>
        void OnExplorationExperience(WowPacket p)
        {
            int area = (int)p.U32();
            int xp = p.Remaining >= 4 ? (int)p.U32() : 0;
            string name = WowInstances.AreaName(_files, area);
            string text = xp > 0 ? WowInstances.F("ERR_ZONE_EXPLORED_XP", "Discovered %s: %d experience gained", name, xp)
                                 : WowInstances.F("ERR_ZONE_EXPLORED", "Discovered: %s", name);
            InstMsg(text, false);
            WowLog.Info("Online instances: explored area " + area + " '" + name + "', " + xp + " XP");
        }

        /// <summary>MSG_SET_DUNGEON_DIFFICULTY S->C: uint32 difficulty, uint32 1, uint32 isInGroup (InstancePackets.cpp:35-42).</summary>
        void OnDungeonDifficulty(WowPacket p)
        {
            int v = (int)p.U32();
            InstDifficultyAnswer(false, v);
        }

        /// <summary>MSG_SET_RAID_DIFFICULTY S->C: the same layout (InstancePackets.cpp:56-63).</summary>
        void OnRaidDifficulty(WowPacket p)
        {
            int v = (int)p.U32();
            InstDifficultyAnswer(true, v);
        }

        /// <summary>The first packet (login, CharacterHandler.cpp:816) only sets the value. Our own request answered with the value it had
        /// before = ERR_*_DIFFICULTY_FAILED (GlobalStrings.lua:3038 / :3448; AC answers a refused change with the old value,
        /// MiscHandler.cpp:1286-1310); any other change = ERR_*_DIFFICULTY_CHANGED_S (:3037 / :3447).</summary>
        void InstDifficultyAnswer(bool raid, int v)
        {
            bool known = raid ? _raidKnown : _dungeonKnown;
            int asked = raid ? _raidAsked : _dungeonAsked;
            int prev = raid ? _raidPrev : _dungeonPrev;
            int cur = raid ? _player.RaidDifficulty : _player.DungeonDifficulty;
            if (raid) { _raidKnown = true; _raidAsked = -1; _player.RaidDifficulty = v; }
            else { _dungeonKnown = true; _dungeonAsked = -1; _player.DungeonDifficulty = v; }
            string what = raid ? "raid" : "dungeon";
            if (!known) { WowLog.Info("Online instances: " + what + " difficulty " + v + " (from the server)"); return; }
            if (asked >= 0 && v != asked && v == prev)
            {
                InstMsg(raid ? WowInstances.G("ERR_RAID_DIFFICULTY_FAILED", "Unable to change Raid Difficulty") : WowInstances.G("ERR_DUNGEON_DIFFICULTY_FAILED", "Unable to change Dungeon Difficulty"), true);
                WowLog.Info("Online instances: " + what + " difficulty " + asked + " refused, still " + v);
                return;
            }
            if (v != cur) InstDifficultyChanged(raid, v);
        }

        void InstDifficultyChanged(bool raid, int v)
        {
            string name = WowInstances.DifficultyText(raid, v);
            InstMsg(raid ? WowInstances.F("ERR_RAID_DIFFICULTY_CHANGED_S", "Raid Difficulty set to %s.", name)
                         : WowInstances.F("ERR_DUNGEON_DIFFICULTY_CHANGED_S", "Dungeon Difficulty set to %s.", name), false);
            WowLog.Info("Online instances: " + (raid ? "raid" : "dungeon") + " difficulty now " + v + " (" + name + ")");
        }

        /// <summary>SMSG_INSTANCE_DIFFICULTY uint32 difficulty, uint32 dynamic heroic (Player.cpp:11781-11785, every map add).</summary>
        void OnInstanceDifficulty(WowPacket p)
        {
            _player.MapDifficulty = (int)p.U32();
            _player.MapDynamicDifficulty = p.Remaining >= 4 && p.U32() != 0;
            WowLog.Info("Online instances: map " + _mapId + " difficulty " + _player.MapDifficulty + (_player.MapDynamicDifficulty ? " (dynamic)" : ""));
        }

        /// <summary>SMSG_INSTANCE_RESET uint32 map (InstancePackets.cpp:20-25) -> INSTANCE_RESET_SUCCESS (GlobalStrings.lua:4254).</summary>
        void OnInstanceReset(WowPacket p)
        {
            int map = (int)p.U32();
            string name = WowInstances.MapName(_files, map);
            InstMsg(WowInstances.F("INSTANCE_RESET_SUCCESS", "%s has been reset.", name), false);
            WowLog.Info("Online instances: map " + map + " '" + name + "' reset");
        }

        /// <summary>SMSG_INSTANCE_RESET_FAILED uint32 reason, uint32 map (InstancePackets.cpp:27-33; Player.h:797-802).</summary>
        void OnInstanceResetFailed(WowPacket p)
        {
            int reason = (int)p.U32();
            int map = p.Remaining >= 4 ? (int)p.U32() : 0;
            string name = WowInstances.MapName(_files, map);
            InstMsg(WowInstances.ResetFailedText(name, reason), true);
            WowLog.Info("Online instances: reset of map " + map + " failed, reason " + reason);
        }

        /// <summary>SMSG_RESET_FAILED_NOTIFY uint32 map (InstancePackets.cpp:49-54; Map.cpp:2259-2264) -> RESET_FAILED_NOTIFY (GlobalStrings.lua:6090).</summary>
        void OnResetFailedNotify(WowPacket p)
        {
            int map = p.Remaining >= 4 ? (int)p.U32() : 0;
            InstMsg(WowInstances.G("RESET_FAILED_NOTIFY", "The party leader has attempted to reset the instance you are in.  Please zone out to allow the instance to reset."), false);
            WowLog.Info("Online instances: the leader tried to reset map " + map + " while we are inside");
        }

        /// <summary>SMSG_RAID_INSTANCE_INFO (PlayerStorage.cpp:6701-6733): uint32 count; per bind uint32 map, uint32 difficulty, uint64
        /// instance guid, uint8 locked, uint8 extended, uint32 seconds to reset.</summary>
        void OnRaidInstanceInfo(WowPacket p)
        {
            uint count = p.U32();
            var list = _player.SavedInstances;
            list.Clear();
            _saveEnds.Clear();
            for (uint i = 0; i < count && i < 512 && p.Remaining >= 22; i++)
            {
                var s = new SavedInstanceView();
                s.MapId = (int)p.U32();
                s.Difficulty = (int)p.U32();
                s.InstanceGuid = p.U64();
                s.Locked = p.U8() != 0;
                s.Extended = p.U8() != 0;
                s.SecondsLeft = p.U32();
                s.Name = WowInstances.MapName(_files, s.MapId);
                s.DifficultyName = WowInstances.MapDifficultyName(_files, s.MapId, s.Difficulty);
                list.Add(s);
                _saveEnds.Add(_now + s.SecondsLeft);
            }
            _player.SavedInstancesVersion++;
            WowLog.Info("Online instances: raid info - " + list.Count + " saved instance(s)");
        }

        void OnInstanceOwnership(WowPacket p)
        {
            _player.HasSavedInstances = p.U32() != 0;   // SMSG_UPDATE_INSTANCE_OWNERSHIP (PlayerStorage.cpp:6757-6759)
        }

        void OnLastInstance(WowPacket p)
        {
            WowLog.Info("Online instances: last instance map " + p.U32());   // SMSG_UPDATE_LAST_INSTANCE (PlayerStorage.cpp:6771-6773)
        }

        /// <summary>SMSG_INSTANCE_SAVE_CREATED uint32 0 (PlayerStorage.cpp:6695-6697) -> INSTANCE_SAVED (GlobalStrings.lua:4255).</summary>
        void OnInstanceSaveCreated(WowPacket p)
        {
            InstMsg(WowInstances.G("INSTANCE_SAVED", "You are now saved to this instance"), false);
            _player.HasSavedInstances = true;
            WowLog.Info("Online instances: saved to map " + _mapId);
        }

        /// <summary>SMSG_RAID_INSTANCE_MESSAGE uint32 type, map, difficulty, seconds [type 4: uint8 locked, uint8 extended] (Player.cpp:11968-11998)
        /// -> the system chat line (UIParent.lua:1059-1083).</summary>
        void OnRaidInstanceMessage(WowPacket p)
        {
            int type = (int)p.U32();
            int map = (int)p.U32();
            int diff = (int)p.U32();
            uint seconds = p.U32();
            bool locked = false, extended = false;
            if (type == 4 && p.Remaining >= 2) { locked = p.U8() != 0; extended = p.U8() != 0; }
            string text = WowInstances.RaidInstanceMessage(WowInstances.MapName(_files, map), type, seconds, locked, extended);
            if (text != null) InstChat(text);
            WowLog.Info("Online instances: raid instance message type " + type + " map " + map + " difficulty " + diff + " seconds " + seconds + (type == 4 ? " locked " + locked + " extended " + extended : ""));
        }

        /// <summary>SMSG_INSTANCE_LOCK_WARNING_QUERY uint32 ms, uint32 completed encounter mask, uint8 previously saved (Map.cpp:2130-2137)
        /// -> PendingInstanceLock + InstanceLockQuery (StaticPopup INSTANCE_LOCK).</summary>
        void OnInstanceLockQuery(WowPacket p)
        {
            uint ms = p.U32();
            uint mask = p.Remaining >= 4 ? p.U32() : 0u;
            bool prev = p.Remaining >= 1 && p.U8() != 0;
            int done, total;
            WowInstances.Encounters(_files, _mapId, _player.MapDifficulty, mask, out done, out total);
            var v = new InstanceLockView
            {
                MapId = _mapId, Name = WowInstances.NameWithDifficulty(_files, _mapId, _player.MapDifficulty),
                SecondsLeft = ms / 1000f, Duration = ms / 1000f, CompletedEncounterMask = mask, EncountersDone = done, EncountersTotal = total, PreviouslySaved = prev
            };
            _player.PendingInstanceLock = v;
            _events.Enqueue(new GameEvent { Type = GameEventType.InstanceLockQuery, Amount = (int)ms });
            WowLog.Info("Online instances: instance lock warning for map " + _mapId + " '" + v.Name + "' - " + v.SecondsLeft + " s, " + done + "/" + total + " bosses killed" + (prev ? ", previously saved" : ""));
        }

        /// <summary>SMSG_RAID_GROUP_ONLY uint32 ms, uint32 1 start / 0 stop (PlayerUpdates.cpp:1426-1455) -> InstanceBootSeconds + InstanceBoot.</summary>
        void OnRaidGroupOnly(WowPacket p)
        {
            uint ms = p.U32();
            uint start = p.Remaining >= 4 ? p.U32() : 0u;
            _player.InstanceBootSeconds = start != 0 ? ms / 1000f : 0f;
            _events.Enqueue(new GameEvent { Type = GameEventType.InstanceBoot, Amount = start != 0 ? (int)ms : 0 });
            WowLog.Info("Online instances: instance boot timer " + (start != 0 ? (ms / 1000f).ToString("0") + " s" : "stopped"));
        }

        // ------------------------------------------------------------------ IWowSession

        public void SetDungeonDifficulty(int difficulty) { InstAskDifficulty(false, difficulty); }

        public void SetRaidDifficulty(int difficulty) { InstAskDifficulty(true, difficulty); }

        /// <summary>MSG_SET_*_DIFFICULTY C->S uint32 mode (InstancePackets.cpp:44-47). AC ignores a mode equal to the current one or above the
        /// maximum (MiscHandler.cpp:1272-1276; MAX_DUNGEON_DIFFICULTY 3, MAX_RAID_DIFFICULTY 4, DBCEnums.h:265-283), answers a refused change
        /// with the old value, a group leader's change with the new value to every member (Group.cpp:2292-2311) and a SOLO change with
        /// nothing (MiscHandler.cpp:1305-1307, Player.h:1952) - so solo the new value is taken at once (ours).</summary>
        void InstAskDifficulty(bool raid, int d)
        {
            string what = raid ? "raid" : "dungeon";
            if (_world == null || _world.IsClosed) return;
            if (d < 0 || d >= (raid ? 4 : 3)) { WowLog.Warn("Online instances: " + what + " difficulty " + d + " out of range - not sent"); return; }
            int cur = raid ? _player.RaidDifficulty : _player.DungeonDifficulty;
            if (d == cur) { WowLog.Info("Online instances: " + what + " difficulty already " + d + " - not sent"); return; }
            if (_now - _diffAskAt < DiffMinGap) { WowLog.Info("Online instances: " + what + " difficulty " + d + " ignored - the last request was " + (_now - _diffAskAt).ToString("0.0") + " s ago (AC kicks above 3 per second)"); return; }
            _diffAskAt = _now;
            _world.Send(raid ? Op.MSG_SET_RAID_DIFFICULTY : Op.MSG_SET_DUNGEON_DIFFICULTY, new PacketWriter().U32((uint)d));
            bool solo = _player.GroupLeaderGuid == 0 && _player.Group.Count == 0;
            if (raid) { _raidAsked = d; _raidAskedAt = _now; _raidPrev = cur; } else { _dungeonAsked = d; _dungeonAskedAt = _now; _dungeonPrev = cur; }
            WowLog.Info("Online instances: MSG_SET_" + (raid ? "RAID" : "DUNGEON") + "_DIFFICULTY " + d + (solo ? " (solo - taken at once)" : " (group - waiting for the server)"));
            if (solo)
            {
                if (raid) _player.RaidDifficulty = d; else _player.DungeonDifficulty = d;
                InstDifficultyChanged(raid, d);
            }
        }

        /// <summary>CMSG_RESET_INSTANCES (empty; MiscHandler.cpp:1255-1266: solo, or the group leader).</summary>
        public void ResetInstances()
        {
            if (_world == null || _world.IsClosed) return;
            _world.Send(Op.CMSG_RESET_INSTANCES);
            WowLog.Info("Online instances: CMSG_RESET_INSTANCES");
        }

        /// <summary>CMSG_REQUEST_RAID_INFO (empty; GroupHandler.cpp:1137-1141) -> SMSG_RAID_INSTANCE_INFO. At most one per second (ours).</summary>
        public void RequestRaidInfo()
        {
            if (_world == null || _world.IsClosed) return;
            if (_now - _raidInfoAskAt < 1.0) return;
            _raidInfoAskAt = _now;
            _world.Send(Op.CMSG_REQUEST_RAID_INFO);
            WowLog.Info("Online instances: CMSG_REQUEST_RAID_INFO");
        }

        /// <summary>CMSG_INSTANCE_LOCK_RESPONSE uint8 accept (InstancePackets.cpp:70-73; MiscHandler.cpp:1707-1722: accept binds, else the
        /// graveyard).</summary>
        public void AnswerInstanceLock(bool accept)
        {
            var lk = _player.PendingInstanceLock;
            if (lk == null) { WowLog.Info("Online instances: instance lock answer without a pending warning - ignored"); return; }
            _player.PendingInstanceLock = null;
            if (_world == null || _world.IsClosed) return;
            _world.Send(Op.CMSG_INSTANCE_LOCK_RESPONSE, new PacketWriter().U8(accept ? 1 : 0));
            WowLog.Info("Online instances: CMSG_INSTANCE_LOCK_RESPONSE " + (accept ? "accept" : "leave instance") + " (map " + lk.MapId + ")");
        }

        /// <summary>CMSG_SET_SAVED_INSTANCE_EXTEND uint32 map, uint32 difficulty, uint8 on (CalendarHandler.cpp:793-817), then RequestRaidInfo
        /// (RaidFrame.lua:226-229).</summary>
        public void SetSavedInstanceExtend(int mapId, int difficulty, bool extend)
        {
            if (_world == null || _world.IsClosed) return;
            _world.Send(Op.CMSG_SET_SAVED_INSTANCE_EXTEND, new PacketWriter().U32((uint)mapId).U32((uint)difficulty).U8(extend ? 1 : 0));
            WowLog.Info("Online instances: CMSG_SET_SAVED_INSTANCE_EXTEND map " + mapId + " difficulty " + difficulty + " " + (extend ? "extend" : "remove the extension"));
            _raidInfoAskAt = -10;
            RequestRaidInfo();
        }
    }
}
