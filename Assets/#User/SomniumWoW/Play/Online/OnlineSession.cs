using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>
    /// The AzerothCore game session (IWowSession over the network), stage O1: every creature, NPC, player and gameobject the server
    /// sends (SMSG_UPDATE_OBJECT / SMSG_COMPRESSED_UPDATE_OBJECT) becomes an EntityState at its server position and moves as the
    /// server says (SMSG_MONSTER_MOVE splines, other players' MSG_MOVE_*); the local avatar's movement goes to the server
    /// (MSG_MOVE_START_FORWARD / STOP / HEARTBEAT / SET_FACING / JUMP / FALL_LAND); names come from the query opcodes; quest
    /// markers from SMSG_QUESTGIVER_STATUS; teleports (MSG_MOVE_TELEPORT_ACK, SMSG_NEW_WORLD) become Teleport events.
    /// Construct it when WowLogin reaches InWorld: new OnlineSession(files, login.World, login.Character, groundZ).
    /// Combat, spells, loot (O2) and gossip, quests, vendors, taxi (O3) answer with an Error event "Not available online yet".
    /// Iteration 7: the server's spellbook, MOUNT casts / auras / dismounts, the rider's movement packets, the can-fly and collision
    /// acks and GM commands from the testing buttons (OnlineSession.Spells / Mounts / Rider / Chat.cs); other spells are still O2.
    /// </summary>
    public sealed partial class OnlineSession : IWowSession
    {
        const string NotYet = "Not available online yet";

        readonly WowFiles _files;
        readonly WorldClient _world;
        readonly CharInfo _character;
        readonly Func<float, float, float, float> _groundZ;
        readonly OnlineDb _db;
        readonly PlayerState _player = new PlayerState();
        readonly List<EntityState> _entities = new List<EntityState>();
        readonly Dictionary<ulong, OnlineObject> _objects = new Dictionary<ulong, OnlineObject>();
        readonly Queue<GameEvent> _events = new Queue<GameEvent>();
        int _mapId;
        bool _disconnectReported;
        ulong _target;
        double _now;                     // session clock (s), advanced in Tick before the network is pumped
        readonly Func<uint> _clientTicks;

        public OnlineSession(WowFiles files, WorldClient world, CharInfo character, Func<float, float, float, float> groundZ)
        {
            _files = files;
            _world = world;
            _character = character;
            _groundZ = groundZ;
            _db = new OnlineDb(files);
            if (_db.Problem != null && files != null) WowLog.Warn("Online world: " + _db.Problem + " - reactions default to neutral.");
            if (character != null)
            {
                _player.Guid = character.Guid;
                _player.Name = character.Name;
                _player.Class = (WowClass)character.Class;
                _player.Race = character.Race;
                _player.Level = character.Level;
                _player.Faction = OnlineDb.FactionOfRace(character.Race);
                _player.Pos = new WowPos(character.X, character.Y, character.Z, 0f);
                _mapId = character.Map;
            }
            if (world != null && world.HasLoginPosition)
            {
                LoginVerifyWorld v = world.LoginPosition;
                _player.Pos = new WowPos(v.X, v.Y, v.Z, v.O);
                _mapId = v.Map;
            }
            _sentPos = _player.Pos;
            HoldAt(_player.Pos, 30.0);   // nothing is sent until the avatar stands at the login position (not from the login room)
            _clientTicks = ClientClock(world);
            if (world != null)
            {
                RegisterObjectHandlers();
                RegisterMovementHandlers();
                RegisterSpellHandlers();     // iteration 7: spellbook, mount casts, auras
                RegisterMountHandlers();     // iteration 7: mount state, collision / can-fly acks
                RegisterChatHandlers();      // iteration 7: system messages, GM commands (D13)
                // stage O2 (lead, PLAN9 s3): each owner registers its own handlers in its own file
                InitItems(); InitCombat(); InitSpellsO2(); InitLoot(); InitDeath(); InitQuests();
                InitGroup();   // stage S1 (lead, PLAN10)
                // iteration 11 (PLAN11 wave 0, w0play): each owner's partial method (OnlineSession.Hooks11.cs); unimplemented ones compile away
                InitNpc(); InitVendor(); InitBank(); InitItemText();               // npc
                InitTrainer(); InitProfession();                                   // trainer
                InitInstance();                                                    // world
                InitInteract();                                                    // interact (+ queues' InitAuction / InitBattleground / InitLfg inside)
                InitTaxi(); InitForced(); InitTransport();                         // motion
                InitPets(); InitVehicle();                                         // pets, vehicle
                InitTalents(); InitReputation();                                   // talents, character
                InitSocial(); InitGuild(); InitMail();                             // social
                world.Release();         // every packet queued since SMSG_LOGIN_VERIFY_WORLD now reaches the handlers above
            }
            LogMountConstants();
            WowLog.Info("Online world: session for " + (_player.Name ?? "?") + " on map " + _mapId + " at " + _player.Pos + ", DBC " +
                        (_db.Problem ?? ("FactionTemplate " + _db.RowCount("FactionTemplate") + ", Item " + _db.RowCount("Item") + ", GameObjectDisplayInfo " + _db.RowCount("GameObjectDisplayInfo"))));
        }

        /// <summary>
        /// The client clock of CMSG_TIME_SYNC_RESP (ms since the WorldClient was created: WorldClient.ClientTicks, public since the
        /// iteration-6 integration), so MovementInfo.time matches what AC's time sync measured (WorldSession::SynchronizeMovement adds
        /// _timeSyncClockDelta). Without a WorldClient (never in play) a clock of our own.
        /// </summary>
        static Func<uint> ClientClock(WorldClient world)
        {
            if (world != null) return world.ClientTicks;
            int start = Environment.TickCount;
            return () => (uint)(Environment.TickCount - start);
        }

        void Error(string text) { _events.Enqueue(new GameEvent { Type = GameEventType.Error, Text = text }); }

        // ------------------------------------------------------------------ IWowSession

        public PlayerState Player { get { return _player; } }
        public int MapId { get { return _mapId; } }
        public IReadOnlyList<EntityState> Entities { get { return _entities; } }

        public EntityState Find(ulong guid)
        {
            OnlineObject o;
            return _objects.TryGetValue(guid, out o) ? o.State : null;
        }

        public ulong TargetGuid { get { return _target; } }
        // Stage O2 (lead, PLAN9 s3): CurrentDialog is in OnlineSession.Quests.cs, CurrentLoot in OnlineSession.Loot.cs.
        // Iteration 11 (wave 0): CurrentTaxi / CloseTaxi / TakeTaxi / FinishTaxi moved to OnlineSession.Taxi.cs (owner motion).
        public bool AutoLoot { get { return _player.AutoLoot; } set { _player.AutoLoot = value; } }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f && deltaSeconds < 5f) _now += deltaSeconds;
            if (_world == null) return;
            _world.Poll();
            if (_world.IsClosed && !_disconnectReported)
            {
                _disconnectReported = true;
                Error("Disconnected from the realm: " + _world.CloseReason);
                WowLog.Info("Online world: disconnected (" + _world.CloseReason + "); " + _objects.Count + " objects known, unhandled: " + _world.UnhandledSummary(10));
            }
            if (_world.IsClosed) return;
            UpdateMovers();
            TickQueries();
            TickOwnMovement();
            // iteration 7
            TickCasts();
            TickAuras(deltaSeconds > 0f && deltaSeconds < 5f ? deltaSeconds : 0f);
            TickMountQueries();
            TickMountLog();
            TickRiderStats();
            TickGm();
            // stage O2 (lead, PLAN9 s3): per-frame work of each owner
            float o2dt = deltaSeconds > 0f && deltaSeconds < 5f ? deltaSeconds : 0f;
            TickItems(o2dt); TickCombat(o2dt); TickLoot(o2dt); TickDeath(o2dt); TickQuests(o2dt);
            TickGroup(o2dt);   // stage S1 (lead, PLAN10)
            // iteration 11 (PLAN11 wave 0, w0play): each owner's partial method (OnlineSession.Hooks11.cs)
            TickNpc(o2dt); TickVendor(o2dt); TickBank(o2dt);                       // npc
            TickTrainer(o2dt); TickProfession(o2dt);                               // trainer
            TickInstance(o2dt);                                                    // world
            TickInteract(o2dt);                                                    // interact (+ queues' Tick* inside)
            TickTaxi(o2dt); TickForced(o2dt); TickTransport(o2dt);                 // motion
            TickPets(o2dt); TickVehicle(o2dt);                                     // pets, vehicle
            TickTalents(o2dt);                                                     // talents
            TickSocial(o2dt);                                                      // social
            RefreshAbilityStates();
        }

        public bool TryDequeueEvent(out GameEvent e)
        {
            if (_events.Count > 0) { e = _events.Dequeue(); return true; }
            e = default(GameEvent);
            return false;
        }

        /// <summary>CMSG_SET_SELECTION (uint64 guid, MiscHandler HandleSetSelectionOpcode). 0 clears the target.</summary>
        public void SetTarget(ulong guid)
        {
            if (guid == _target) return;
            _target = guid;
            if (_world != null) _world.Send(Op.CMSG_SET_SELECTION, new PacketWriter().U64(guid));
        }

        /// <summary>Client-side like the real client: the nearest living hostile unit within 40 yd becomes the target.</summary>
        public void TargetNearestEnemy()
        {
            EntityState best = null;
            float bestD = 40f * 40f;
            foreach (var e in _entities)
            {
                if (e.Dead || e.Reaction != Reaction.Hostile || e.Kind == EntityKind.GameObject || e.Guid == _target) continue;
                float dx = e.Pos.X - _player.Pos.X, dy = e.Pos.Y - _player.Pos.Y, dz = e.Pos.Z - _player.Pos.Z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best != null) SetTarget(best.Guid);
            else Error("No enemies nearby");
        }

        /// <summary>
        /// The Somnium layer marks the WoW characters of Somnium players in this world (their avatar is drawn instead): the flag is
        /// kept across the character leaving and re-entering range. The session never clears it on its own.
        /// </summary>
        public void MarkSomniumPlayer(ulong guid, bool isSomniumPlayer)
        {
            if (isSomniumPlayer) _somniumPlayers.Add(guid); else _somniumPlayers.Remove(guid);
            var e = Find(guid);
            if (e != null) e.IsSomniumPlayer = isSomniumPlayer;
        }
        readonly HashSet<ulong> _somniumPlayers = new HashSet<ulong>();

        // Stage O2 (lead, PLAN9 s3): StartAttack / StopAttack -> OnlineSession.Combat.cs; CastSpell / CancelCast / CastSpellHeld /
        // ReleaseHeldSpell / SetActionBarSlot -> OnlineSession.Spells.cs; Interact / CloseDialog / quests -> OnlineSession.Quests.cs;
        // loot -> OnlineSession.Loot.cs; ReleaseSpirit / ReclaimCorpse / AcceptSpiritHealer -> OnlineSession.Death.cs; EquipItem / UseItem -> OnlineSession.Items.cs.
        /// <summary>CMSG_SET_SHEATHED (uint32 state, CombatPackets SetSheathed): others see the weapons drawn or put away.</summary>
        public void SetSheath(SheathState state)
        {
            if (_player.Sheath == state) return;
            _player.Sheath = state;
            if (_world != null) _world.Send(Op.CMSG_SET_SHEATHED, new PacketWriter().U32((uint)state));
        }
        public void SetDifficulty(int difficulty) { Error(NotYet); }
    }
}
