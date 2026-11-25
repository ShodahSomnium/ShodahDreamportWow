using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Object manager: SMSG_UPDATE_OBJECT / SMSG_DESTROY_OBJECT -> OnlineObject -> EntityState / PlayerState, the name queries, quest
    // markers and the reputation the reactions need.
    public sealed partial class OnlineSession
    {
        // UnitFlags / UNIT_DYNFLAG_* / GO_DYNFLAG_LO_* / UNIT_NPC_FLAG_* / PLAYER_FLAGS_* (AC UnitDefines.h, SharedDefines.h, Player.h)
        const uint UnitFlagPlayerControlled = 0x00000008, UnitFlagInCombat = 0x00080000;
        const uint DynLootable = 0x0001, DynDead = 0x0020, GoDynSparkle = 0x08;
        const uint NpcQuestGiver = 0x00000002;
        const uint PlayerFlagGhost = 0x00000010, PlayerFlagContestedPvp = 0x00000100;
        const int GoTypeQuestGiver = 2;
        const int CreatureTypeTotem = 11;

        sealed class CreatureInfo
        {
            public string Name, SubName; public int Type; public bool Missing;
            public uint TypeFlags;   // iteration 11 (trainer O3): creature_template type_flags (skinning skill, CreatureData.h:252-262)
            public int Family;       // iteration 11 (pets H2c / talents): creature_template family (CreatureFamily.dbc id)
            public string IconName;  // lead fix 2026-10-03: creature_template IconName = the cursor over it (QueryHandler.cpp:117 "Directions" for guard)
            public int Rank;         // lead fix 2026-10-03: creature_template rank (QueryHandler.cpp:121) - 0 normal, 1 elite, 2 rare elite, 3 boss, 4 rare
        }
        readonly Dictionary<int, CreatureInfo> _creatureInfo = new Dictionary<int, CreatureInfo>();
        readonly Dictionary<int, string> _goNames = new Dictionary<int, string>();
        readonly Dictionary<int, string> _goIcons = new Dictionary<int, string>();   // lead fix 2026-10-03: gameobject_template IconName by entry
        readonly Dictionary<ulong, string> _playerNames = new Dictionary<ulong, string>();
        readonly HashSet<int> _creatureAsked = new HashSet<int>(), _goAsked = new HashSet<int>();
        readonly HashSet<ulong> _nameAsked = new HashSet<ulong>();
        readonly Dictionary<ulong, QuestMarker> _markers = new Dictionary<ulong, QuestMarker>();
        readonly int[] _repStanding = new int[128];
        readonly byte[] _repFlags = new byte[128];
        OnlineObject _self;
        int _myFactionTemplate, _myLevelForCon;
        ulong _myDuelKey;                                          // iteration 11 (interact H4b): PLAYER_DUEL_ARBITER ^ PLAYER_DUEL_TEAM last seen
        double _questQueryAt = -1, _lastQuestQuery = -100, _summaryAt = 5;
        int _updatePackets, _parseErrors, _blocksApplied, _creates, _queriesSent, _queryAnswers;
        bool _unknownValuesLogged;

        void RegisterObjectHandlers()
        {
            _world.On(Op.SMSG_UPDATE_OBJECT, OnUpdateObject, this);          // SMSG_COMPRESSED_UPDATE_OBJECT arrives inflated as this
            _world.On(Op.SMSG_DESTROY_OBJECT, OnDestroyObject, this);
            _world.On(Op.SMSG_CREATURE_QUERY_RESPONSE, OnCreatureQueryResponse, this);
            _world.On(Op.SMSG_NAME_QUERY_RESPONSE, OnNameQueryResponse, this);
            _world.On(Op.SMSG_GAMEOBJECT_QUERY_RESPONSE, OnGameObjectQueryResponse, this);
            _world.On(Op.SMSG_QUESTGIVER_STATUS, OnQuestGiverStatus, this);
            _world.On(Op.SMSG_QUESTGIVER_STATUS_MULTIPLE, OnQuestGiverStatusMultiple, this);
            _world.On(Op.SMSG_INITIALIZE_FACTIONS, OnInitializeFactions, this);
            _world.On(Op.SMSG_SET_FACTION_STANDING, OnSetFactionStanding, this);
        }

        /// <summary>Objects the server told us about (incl. items, our own player); for probes and diagnostics.</summary>
        public int ObjectCount { get { return _objects.Count; } }
        public OnlineObject FindObject(ulong guid) { OnlineObject o; return _objects.TryGetValue(guid, out o) ? o : null; }

        // ------------------------------------------------------------------ update object

        void OnUpdateObject(WowPacket p)
        {
            string err;
            List<UpdateBlock> blocks = UpdateObjectParser.Parse(p, out err);
            for (int i = 0; i < blocks.Count; i++) ApplyBlock(blocks[i]);
            _blocksApplied += blocks.Count;
            _updatePackets++;
            if (err != null && ++_parseErrors <= 5)
                WowLog.Warn("Online world: SMSG_UPDATE_OBJECT (" + p.Length + " bytes) " + err + " - " + blocks.Count + " blocks applied");
        }

        void ApplyBlock(UpdateBlock b)
        {
            OnlineObject o;
            switch (b.Type)
            {
                case UpdateType.Values:
                    if (!_objects.TryGetValue(b.Guid, out o))
                    {
                        if (!_unknownValuesLogged) { _unknownValuesLogged = true; WowLog.Warn("Online world: values update for unknown object " + b.Guid.ToString("X16") + " (ignored; logged once)"); }
                        return;
                    }
                    for (int i = 0; i < b.Fields.Length; i++) o.Set(b.Fields[i], b.Values[i]);
                    Refresh(o);
                    if (o.TypeId == TypeId.DynamicObject) DynUpdated(o);        // iteration 11 (spellfx hook 1)
                    break;
                case UpdateType.Movement:
                    if (_objects.TryGetValue(b.Guid, out o)) ApplyMovementBlock(o, b.Movement);
                    break;
                case UpdateType.CreateObject:
                case UpdateType.CreateObject2:
                    Create(b);
                    break;
                case UpdateType.OutOfRange:
                    foreach (ulong g in b.Guids) Remove(g);
                    break;
            }
        }

        void Create(UpdateBlock b)
        {
            OnlineObject o;
            if (_objects.TryGetValue(b.Guid, out o) && o.TypeId != b.TypeId) { Remove(b.Guid); o = null; }
            if (o == null)
            {
                o = new OnlineObject(b.Guid, b.TypeId);
                _objects[b.Guid] = o;
            }
            else Array.Clear(o.V, 0, o.V.Length);                  // a fresh create carries every non-zero field
            for (int i = 0; i < b.Fields.Length; i++) o.Set(b.Fields[i], b.Values[i]);
            _creates++;
            bool self = b.Guid == _player.Guid || (b.Movement != null && (b.Movement.Flags & UpdateType.FlagSelf) != 0);
            if (self)
            {
                _self = o;
                if (b.Movement != null) ApplyMovementBlock(o, b.Movement);
                RefreshSelf(o);
                return;
            }
            if (o.State == null && (b.TypeId == TypeId.Unit || b.TypeId == TypeId.Player || b.TypeId == TypeId.GameObject
                                    || (b.TypeId == TypeId.Corpse && DthCorpseDrawn(o))))       // rescorpse 2026-10-04: players' corpses (OnlineSession.Death.cs DthCorpseSync)
            {
                o.State = new EntityState { Guid = b.Guid };
                o.State.IsSomniumPlayer = _somniumPlayers.Contains(b.Guid);
                QuestMarker m;
                if (_markers.TryGetValue(b.Guid, out m)) o.State.Marker = m;
                _entities.Add(o.State);
            }
            if (o.State != null && b.TypeId == TypeId.GameObject) o.State.Spawned = b.Type == UpdateType.CreateObject2;   // duelsnd (Object.cpp:209-218)
            if (b.Movement != null) ApplyMovementBlock(o, b.Movement);
            Refresh(o);
            if (b.TypeId == TypeId.DynamicObject) DynCreated(o);            // iteration 11 (spellfx hook 1): ground areas
            AskNames(o);
        }

        void Remove(ulong guid)
        {
            OnlineObject o;
            if (!_objects.TryGetValue(guid, out o)) return;
            _objects.Remove(guid);
            if (o.TypeId == TypeId.DynamicObject) DynRemoved(o);            // iteration 11 (spellfx hook 1)
            DthPlayerLeft(o);                                               // rescorpse: its level stays for its corpse
            if (o.State != null) _entities.Remove(o.State);
            if (guid == _target) _target = 0;                       // gone from view (the server keeps its selection)
            if (o == _self) _self = null;
            _auraOwners.Remove(o);                                  // iteration 7
            _castingOthers.Remove(o);
        }

        /// <summary>SMSG_DESTROY_OBJECT: uint64 guid, uint8 onDeath (Object::DestroyForPlayer).</summary>
        void OnDestroyObject(WowPacket p)
        {
            ulong g = p.U64();
            Remove(g);
        }

        void ClearWorld()
        {
            _objects.Clear();
            _entities.Clear();
            _markers.Clear();
            _self = null;
            _target = 0;
            _auraOwners.Clear();                                    // iteration 7
            _castingOthers.Clear();
            DynClear();                                             // iteration 11 (spellfx hook 1)
        }

        // ------------------------------------------------------------------ mapping

        void Refresh(OnlineObject o)
        {
            if (o == _self) { RefreshSelf(o); return; }
            if (o.TypeId == TypeId.Corpse) { DthCorpseSync(o); return; }   // rescorpse 2026-10-04: corpse fields, not unit fields (OnlineSession.Death.cs)
            var s = o.State;
            if (s == null) return;
            s.Entry = o.Entry;
            float scale = o.F32(UF.OBJECT_FIELD_SCALE_X);
            s.Scale = scale > 0.01f && scale < 100f ? scale : 1f;
            if (o.TypeId == TypeId.GameObject) { RefreshGameObject(o, s); return; }

            s.DisplayId = o.I32(UF.UNIT_FIELD_DISPLAYID);
            s.NativeDisplayId = o.I32(UF.UNIT_FIELD_NATIVEDISPLAYID);   // iteration 11 (avatar): DisplayId != NativeDisplayId = transformed / shapeshifted
            s.Level = o.I32(UF.UNIT_FIELD_LEVEL);
            s.Health = o.I32(UF.UNIT_FIELD_HEALTH);
            s.MaxHealth = o.I32(UF.UNIT_FIELD_MAXHEALTH);
            int pt = o.Byte(UF.UNIT_FIELD_BYTES_0, 3);
            int maxPower = pt < 7 ? o.I32(UF.UNIT_FIELD_MAXPOWER1 + pt) : 0;
            s.Power = maxPower > 0 ? (PowerType)pt : PowerType.None;
            int div = pt == 1 || pt == 6 ? 10 : 1;                 // rage and runic power are stored x10 (offline shows 0..100)
            s.PowerValue = maxPower > 0 ? o.I32(UF.UNIT_FIELD_POWER1 + pt) / div : 0;
            s.PowerMax = maxPower / div;
            uint flags = o.U32(UF.UNIT_FIELD_FLAGS), dyn = o.U32(UF.UNIT_DYNAMIC_FLAGS), npc = o.U32(UF.UNIT_NPC_FLAGS);
            s.Dead = s.Health <= 0 || (dyn & DynDead) != 0;
            s.Lootable = (dyn & DynLootable) != 0;
            PfRefreshSkinnable(o, s, flags);               // iteration 11 (trainer O4): UNIT_FLAG_SKINNABLE
            s.InCombat = (flags & UnitFlagInCombat) != 0;
            s.DisableMove = (flags & 0x4u) != 0;           // integrator (vehicle request): UNIT_FLAG_DISABLE_MOVE (UnitDefines.h) - turrets
            s.TargetGuid = o.U64(UF.UNIT_FIELD_TARGET);
            s.StandState = o.Byte(UF.UNIT_FIELD_BYTES_1, 0);
            s.EmoteState = o.I32(UF.UNIT_NPC_EMOTESTATE);
            UpdateOtherMount(o, s);                        // iteration 7: Mounted (UNIT_FLAG_MOUNT) + MountDisplayId, held at 0 after SMSG_DISMOUNT
            int sheath = o.Byte(UF.UNIT_FIELD_BYTES_2, 0);
            s.Sheath = sheath <= 2 ? (SheathState)sheath : SheathState.Sheathed;
            ulong owner = o.U64(UF.UNIT_FIELD_SUMMONEDBY);
            if (owner == 0) owner = o.U64(UF.UNIT_FIELD_CREATEDBY);
            s.OwnerGuid = owner;
            s.CharmedByGuid = o.U64(UF.UNIT_FIELD_CHARMEDBY);            // iteration 11 (pets H2a)
            s.SpellClick = (npc & 0x01000000u) != 0;                       // UNIT_NPC_FLAG_SPELLCLICK
            s.NotSelectable = (flags & 0x02000000u) != 0;                  // lead fix 2026-10-14: UNIT_FLAG_NOT_SELECTABLE
            s.PlayerVehicle = (npc & 0x02000000u) != 0;                    // UNIT_NPC_FLAG_PLAYER_VEHICLE

            if (o.TypeId == TypeId.Player)
            {
                s.Kind = EntityKind.Player;
                s.Race = o.Byte(UF.UNIT_FIELD_BYTES_0, 0);
                s.Gender = o.Byte(UF.UNIT_FIELD_BYTES_0, 2);
                s.Skin = o.Byte(UF.PLAYER_BYTES, 0);             // Player::Create: Skin | Face << 8 | HairStyle << 16 | HairColor << 24
                s.Face = o.Byte(UF.PLAYER_BYTES, 1);
                s.HairStyle = o.Byte(UF.PLAYER_BYTES, 2);
                s.HairColor = o.Byte(UF.PLAYER_BYTES, 3);
                s.FacialHair = o.Byte(UF.PLAYER_BYTES_2, 0);
                if (s.ItemDisplayIds == null) s.ItemDisplayIds = new int[19];
                for (int i = 0; i < 19; i++)                       // SetVisibleItemSlot: PLAYER_VISIBLE_ITEM_1_ENTRYID + slot * 2 = item entry
                    s.ItemDisplayIds[i] = _db.ItemDisplayId(o.I32(UF.PLAYER_VISIBLE_ITEM_1_ENTRYID + i * 2));
                s.MainHandDisplayId = s.ItemDisplayIds[(int)EquipSlot.MainHand];
                s.OffHandDisplayId = s.ItemDisplayIds[(int)EquipSlot.OffHand];
                s.ChosenTitle = o.I32(UF.PLAYER_CHOSEN_TITLE);                 // iteration 11 (talents 2b)
                string n;
                if (_playerNames.TryGetValue(o.Guid, out n)) s.Name = n;
            }
            else
            {
                // Creature::LoadEquipment: UNIT_VIRTUAL_ITEM_SLOT_ID + i = item entry -> Item.dbc display
                s.MainHandDisplayId = _db.ItemDisplayId(o.I32(UF.UNIT_VIRTUAL_ITEM_SLOT_ID));
                s.OffHandDisplayId = _db.ItemDisplayId(o.I32(UF.UNIT_VIRTUAL_ITEM_SLOT_ID + 1));
                CreatureInfo ci;
                _creatureInfo.TryGetValue(s.Entry, out ci);
                if (ci != null && !ci.Missing) { s.Name = ci.Name; s.SubName = ci.SubName; s.CreatureType = ci.Type; s.Classification = ci.Rank; s.CursorName = ci.IconName; }   // + rank, cursor (lead fix 2026-10-03)
                string pn; if (_ptNames.TryGetValue(o.Guid, out pn)) s.Name = pn;   // iteration 11 (pets H2b): the pet's chosen name
                if (o.High == 0xF140 || (owner != 0 && owner == _player.Guid && (flags & UnitFlagPlayerControlled) != 0)) s.Kind = EntityKind.Pet;
                else if (ci != null && ci.Type == CreatureTypeTotem && owner != 0) s.Kind = EntityKind.Totem;
                else s.Kind = npc != 0 ? EntityKind.Npc : EntityKind.Creature;
                // iteration 11 (pets H2d): PetGuid becomes our UNIT_FIELD_SUMMON (PetsRefreshSelf); until pets' build says so, the old rule stays.
                if (!PetsOwnPetGuid && s.Kind == EntityKind.Pet && owner == _player.Guid) _player.PetGuid = o.Guid;
                if ((npc & NpcQuestGiver) != 0 && !_markers.ContainsKey(o.Guid)) ScheduleQuestQuery(0.5);
            }
            s.Reaction = ReactionOf(o);
            s.ConColor = s.Kind == EntityKind.Npc && s.Reaction != Reaction.Hostile ? 2 : WowFormulas.ColorCode(_player.Level, s.Level);
            SpFillUnitExtras(o, s);                        // iteration 11 (spellfx hook 1): weapon enchants, aura states, stealth
        }

        void RefreshGameObject(OnlineObject o, EntityState s)
        {
            s.Kind = EntityKind.GameObject;
            int display = o.I32(UF.GAMEOBJECT_DISPLAYID);
            if (display != s.DisplayId || (s.ModelPath == null && display > 0))
            {
                s.DisplayId = display;
                s.ModelPath = _db.GameObjectModel(display);
            }
            s.GoState = o.Byte(UF.GAMEOBJECT_BYTES_1, 0);           // GameObject.h: state byte 0, type byte 1, artkit 2, anim progress 3
            s.GoType = o.Byte(UF.GAMEOBJECT_BYTES_1, 1);
            s.Level = o.I32(UF.GAMEOBJECT_LEVEL);
            s.Lootable = (o.U16(UF.GAMEOBJECT_DYNAMIC, 0) & GoDynSparkle) != 0;
            s.Health = s.MaxHealth = 0;
            s.Power = PowerType.None;
            int faction = o.I32(UF.GAMEOBJECT_FACTION);
            s.Reaction = faction == 0 ? Reaction.Neutral : OnlineDb.ToReaction(_db.RankToPlayer(faction, _myFactionTemplate, _player.Race, (int)_player.Class, _repStanding, _repFlags, ContestedPvp));
            string n;
            if (_goNames.TryGetValue(s.Entry, out n)) s.Name = n;
            CurGoRefresh(s);                               // lead fix 2026-10-03: its cursor (IconName / gathering lock), OnlineSession.Cursor.cs
            if (s.GoType == GoTypeQuestGiver && !_markers.ContainsKey(o.Guid)) ScheduleQuestQuery(0.5);
            NoteTransport(o);                              // iteration 11 (motion 6.3.4): GO types 11 / 15 (Transport.cs)
        }

        bool ContestedPvp { get { return _self != null && (_self.U32(UF.PLAYER_FLAGS) & PlayerFlagContestedPvp) != 0; } }

        /// <summary>The unit's reaction to our player (Unit::GetReactionTo, the parts that do not need server-only state).</summary>
        Reaction ReactionOf(OnlineObject o)
        {
            if (o.Guid == _player.Guid) return Reaction.Friendly;
            ulong owner = o.State != null ? o.State.OwnerGuid : 0;
            if (owner != 0 && owner == _player.Guid) return Reaction.Friendly;          // GetCharmerOrOwnerOrSelf() == target
            int theirs = o.I32(UF.UNIT_FIELD_FACTIONTEMPLATE);
            if (_myFactionTemplate == 0 || theirs == 0) return Reaction.Neutral;       // "always neutral when no template entry found"
            bool bothPlayerControlled = (o.U32(UF.UNIT_FIELD_FLAGS) & UnitFlagPlayerControlled) != 0;
            if (bothPlayerControlled && o.TypeId == TypeId.Player)
            {
                if (DuelOpponent(o)) return Reaction.Hostile;   // iteration 11 (interact H4a): Unit.cpp:6895-6897 "duel - always hostile to opponent"
                const int FfaPvp = 0x04;                                                // UNIT_BYTE2_FLAG_FFA_PVP in UNIT_FIELD_BYTES_2 byte 1
                if (_self != null && (o.Byte(UF.UNIT_FIELD_BYTES_2, 1) & FfaPvp) != 0 && (_self.Byte(UF.UNIT_FIELD_BYTES_2, 1) & FfaPvp) != 0) return Reaction.Hostile;
                return OnlineDb.ToReaction(_db.TemplateRank(theirs, _myFactionTemplate));
            }
            if (owner != 0 && DuelOpponentGuid(owner)) return Reaction.Hostile;   // iteration 11 (interact H4a): the opponent's pet / totem (Unit.cpp:6889-6897 compares the owning players)
            return OnlineDb.ToReaction(_db.RankToPlayer(theirs, _myFactionTemplate, _player.Race, (int)_player.Class, _repStanding, _repFlags, ContestedPvp));
        }

        void RefreshAllReactions()
        {
            foreach (var o in _objects.Values)
            {
                if (o.State == null) continue;
                if (o.TypeId == TypeId.Corpse) { o.State.Reaction = DthCorpseReaction(o.State.CorpseOwnerGuid, o.State.Race); continue; }   // rescorpse
                if (o.TypeId == TypeId.GameObject) RefreshGameObject(o, o.State);
                else
                {
                    o.State.Reaction = ReactionOf(o);
                    o.State.ConColor = o.State.Kind == EntityKind.Npc && o.State.Reaction != Reaction.Hostile ? 2 : WowFormulas.ColorCode(_player.Level, o.State.Level);
                }
            }
        }

        void RefreshSelf(OnlineObject o)
        {
            var p = _player;
            int race = o.Byte(UF.UNIT_FIELD_BYTES_0, 0), cls = o.Byte(UF.UNIT_FIELD_BYTES_0, 1);
            if (race != 0) { p.Race = race; p.Faction = OnlineDb.FactionOfRace(race); }
            if (cls != 0) p.Class = (WowClass)cls;
            p.Level = Math.Max(1, o.I32(UF.UNIT_FIELD_LEVEL));
            p.Xp = o.I32(UF.PLAYER_XP);
            p.XpToNextLevel = o.I32(UF.PLAYER_NEXT_LEVEL_XP);
            p.Health = o.I32(UF.UNIT_FIELD_HEALTH);
            p.MaxHealth = o.I32(UF.UNIT_FIELD_MAXHEALTH);
            int pt = o.Byte(UF.UNIT_FIELD_BYTES_0, 3);
            int div = pt == 1 || pt == 6 ? 10 : 1;
            p.Power = pt < 7 ? (PowerType)pt : PowerType.None;
            p.PowerValue = pt < 7 ? o.I32(UF.UNIT_FIELD_POWER1 + pt) / div : 0;
            p.PowerMax = pt < 7 ? o.I32(UF.UNIT_FIELD_MAXPOWER1 + pt) / div : 0;
            p.Mana = o.I32(UF.UNIT_FIELD_POWER1);
            p.MaxMana = o.I32(UF.UNIT_FIELD_MAXPOWER1);
            p.Money = o.I32(UF.PLAYER_FIELD_COINAGE);
            uint flags = o.U32(UF.UNIT_FIELD_FLAGS), pflags = o.U32(UF.PLAYER_FLAGS);
            p.Ghost = (pflags & PlayerFlagGhost) != 0;
            p.Dead = p.Health <= 0 || p.Ghost;
            p.InCombat = (flags & UnitFlagInCombat) != 0;
            int sheath = o.Byte(UF.UNIT_FIELD_BYTES_2, 0);
            if (sheath <= 2) p.Sheath = (SheathState)sheath;
            p.ShapeshiftForm = o.Byte(UF.UNIT_FIELD_BYTES_2, 3);
            // iteration 11 (avatar hook 1): what WoW draws us as
            p.DisplayId = o.I32(UF.UNIT_FIELD_DISPLAYID);
            p.NativeDisplayId = o.I32(UF.UNIT_FIELD_NATIVEDISPLAYID);
            float osc = o.F32(UF.OBJECT_FIELD_SCALE_X); p.ObjectScale = osc > 0.01f && osc < 100f ? osc : 1f;
            p.Gender = o.Byte(UF.PLAYER_BYTES_3, 0);                                    // the real gender (Player.h:513), not UNIT_FIELD_BYTES_0 byte 2
            p.HideHelm = (pflags & 0x400u) != 0; p.HideCloak = (pflags & 0x800u) != 0;  // PLAYER_FLAGS_HIDE_HELM / _CLOAK (Player.h:469-470)
            p.Stealthed = (o.Byte(UF.PLAYER_FIELD_BYTES2, 3) & 0x20) != 0;              // PLAYER_FIELD_BYTE2_STEALTH (Player.h:553, SpellAuraEffects.cpp:1785)
            UpdateSelfMount(o);                            // iteration 7: Mounted / MountDisplayId from UNIT_FLAG_MOUNT + UNIT_FIELD_MOUNTDISPLAYID
            RefreshSelfMotion(o);                          // iteration 11 (motion 6.3.4): stunned, taxi flight (Forced.cs)
            PetsRefreshSelf(o);                            // iteration 11 (pets H2e): UNIT_FIELD_SUMMON / CHARM / CRITTER
            ReadSkills(o);                                 // iteration 7: riding skill 762
            PfReadSkills(o);                               // iteration 11 (trainer O5): every skill + PLAYER_CHARACTER_POINTS2
            var st = p.Stats;
            st.Strength = o.I32(UF.UNIT_FIELD_STAT0);
            st.Agility = o.I32(UF.UNIT_FIELD_STAT1);
            st.Stamina = o.I32(UF.UNIT_FIELD_STAT2);
            st.Intellect = o.I32(UF.UNIT_FIELD_STAT3);
            st.Spirit = o.I32(UF.UNIT_FIELD_STAT4);
            st.Armor = o.I32(UF.UNIT_FIELD_RESISTANCES);           // resistance 0 = armor
            st.AttackPower = o.I32(UF.UNIT_FIELD_ATTACK_POWER);
            st.MinDamage = o.F32(UF.UNIT_FIELD_MINDAMAGE);
            st.MaxDamage = o.F32(UF.UNIT_FIELD_MAXDAMAGE);
            st.AttackSpeed = o.I32(UF.UNIT_FIELD_BASEATTACKTIME) / 1000f;
            st.CritChance = o.F32(UF.PLAYER_CRIT_PERCENTAGE);
            st.DodgeChance = o.F32(UF.PLAYER_DODGE_PERCENTAGE);
            int ft = o.I32(UF.UNIT_FIELD_FACTIONTEMPLATE);
            ulong duelKey = o.U64(UF.PLAYER_DUEL_ARBITER) ^ o.U32(UF.PLAYER_DUEL_TEAM);   // iteration 11 (interact H4b): a duel starting / ending flips the opponent's reaction
            if (ft != _myFactionTemplate || p.Level != _myLevelForCon || duelKey != _myDuelKey)
            {
                _myFactionTemplate = ft;
                _myLevelForCon = p.Level;
                _myDuelKey = duelKey;
                RefreshAllReactions();
            }
            RefreshSelfPanes(o);                           // iteration 11 (talents 2a): talent points, glyphs, titles, currencies, stats
        }

        // ------------------------------------------------------------------ queries (QueryHandler.cpp)

        void AskNames(OnlineObject o)
        {
            if (_world == null || o.State == null) return;
            int entry = o.Entry;
            if (o.TypeId == TypeId.Player)
            {
                if (!_playerNames.ContainsKey(o.Guid) && _nameAsked.Add(o.Guid))
                {
                    _world.Send(Op.CMSG_NAME_QUERY, new PacketWriter().U64(o.Guid));               // NameQuery::Read: ObjectGuid (uint64)
                    _queriesSent++;
                }
            }
            else if (o.TypeId == TypeId.Unit)
            {
                if (entry > 0 && !_creatureInfo.ContainsKey(entry) && _creatureAsked.Add(entry))
                {
                    _world.Send(Op.CMSG_CREATURE_QUERY, new PacketWriter().U32((uint)entry).U64(o.Guid));   // uint32 entry, ObjectGuid
                    _queriesSent++;
                }
            }
            else if (o.TypeId == TypeId.GameObject)
            {
                if (entry > 0 && !_goNames.ContainsKey(entry) && _goAsked.Add(entry))
                {
                    _world.Send(Op.CMSG_GAMEOBJECT_QUERY, new PacketWriter().U32((uint)entry).U64(o.Guid));
                    _queriesSent++;
                }
            }
        }

        /// <summary>
        /// SMSG_CREATURE_QUERY_RESPONSE (HandleCreatureQueryOpcode): uint32 entry, name, 3 empty names, subname (Title), icon name,
        /// uint32 type_flags, type, family, rank, 2 kill credits, 4 display ids, float hp mod, float mana mod, uint8 racial leader,
        /// 6 quest items, uint32 movement id. Unknown entry: entry | 0x80000000 only.
        /// </summary>
        void OnCreatureQueryResponse(WowPacket p)
        {
            uint raw = p.U32();
            int entry = (int)(raw & 0x7FFFFFFF);
            var ci = new CreatureInfo();
            if ((raw & 0x80000000u) != 0 || p.Remaining == 0) ci.Missing = true;
            else
            {
                ci.Name = p.CString();
                p.CString(); p.CString(); p.CString();
                ci.SubName = p.CString();
                ci.IconName = p.CString();                         // IconName (QueryHandler.cpp:117) - the cursor (lead fix 2026-10-03)
                ci.TypeFlags = p.U32();                            // type_flags (iteration 11, trainer O3: skinning skill, CreatureData.h:252-262)
                ci.Type = (int)p.U32();
                if (p.Remaining >= 32)                             // iteration 7: family, rank, 2 kill credits, Modelid1..4 (the mount models)
                {
                    ci.Family = (int)p.U32(); ci.Rank = (int)p.U32(); p.U32(); p.U32();   // iteration 11 (pets H2c): family = creature_template.family
                    var models = new int[4];
                    for (int i = 0; i < 4; i++) models[i] = (int)p.U32();
                    LearnCreatureModels(entry, models);
                }
            }
            _creatureInfo[entry] = ci;
            _queryAnswers++;
            foreach (var o in _objects.Values)
                if (o.TypeId == TypeId.Unit && o.State != null && o.Entry == entry) Refresh(o);
        }

        /// <summary>SMSG_NAME_QUERY_RESPONSE (NameQueryResponse::Write): packed guid, uint8 NameUnknown, [name, realm name, uint8 race, sex, class, declined].</summary>
        void OnNameQueryResponse(WowPacket p)
        {
            ulong guid = p.PackedGuid();
            bool unknown = p.U8() != 0;
            _queryAnswers++;
            if (unknown) return;
            string name = p.CString();
            _playerNames[guid] = name;
            var e = Find(guid);
            if (e != null) e.Name = name;
            DthCorpseNamed(guid, name);                                     // rescorpse: "Corpse of %s"
        }

        /// <summary>SMSG_GAMEOBJECT_QUERY_RESPONSE (HandleGameObjectQueryOpcode): uint32 entry, type, displayId, name, ... | entry | 0x80000000.</summary>
        void OnGameObjectQueryResponse(WowPacket p)
        {
            uint raw = p.U32();
            int entry = (int)(raw & 0x7FFFFFFF);
            string name = "";
            if ((raw & 0x80000000u) == 0 && p.Remaining > 8)
            {
                p.U32(); p.U32();                                  // type, displayId (the update fields carry both)
                name = p.CString();
                if (p.Remaining > 3) { p.CString(); p.CString(); p.CString(); }   // name2..4 ("uint8(0)" each, QueryHandler.cpp:198)
                if (p.Remaining > 0) _goIcons[entry] = p.CString();               // IconName (:199) - the cursor (lead fix 2026-10-03)
            }
            _goNames[entry] = name;
            _queryAnswers++;
            foreach (var o in _objects.Values)
                if (o.TypeId == TypeId.GameObject && o.State != null && o.Entry == entry) { o.State.Name = name; CurGoRefresh(o.State); }
        }

        // ------------------------------------------------------------------ quest markers (QuestHandler.cpp, Player::SendQuestGiverStatusMultiple)

        void ScheduleQuestQuery(double delay)
        {
            double at = _now + delay;
            if (_questQueryAt < 0 || at < _questQueryAt) _questQueryAt = at;
        }

        void TickQueries()
        {
            if (_questQueryAt >= 0 && _now >= _questQueryAt && _now - _lastQuestQuery > 1.0)
            {
                _questQueryAt = -1;
                _lastQuestQuery = _now;
                _world.Send(Op.CMSG_QUESTGIVER_STATUS_MULTIPLE_QUERY);                // empty body -> SMSG_QUESTGIVER_STATUS_MULTIPLE
            }
            else if (_now - _lastQuestQuery > 20.0 && _markers.Count > 0)
            {
                _lastQuestQuery = _now;                                               // statuses change as quests progress
                _world.Send(Op.CMSG_QUESTGIVER_STATUS_MULTIPLE_QUERY);
            }
            if (_summaryAt > 0 && _now >= _summaryAt)
            {
                _summaryAt = -1;
                LogSummary("first 5 s");
            }
        }

        public void LogSummary(string when)
        {
            int cr = 0, npc = 0, pl = 0, go = 0, named = 0;
            foreach (var e in _entities)
            {
                if (e.Kind == EntityKind.Player) pl++; else if (e.Kind == EntityKind.GameObject) go++; else if (e.Kind == EntityKind.Npc) npc++; else cr++;
                if (!string.IsNullOrEmpty(e.Name)) named++;
            }
            WowLog.Info("Online world (" + when + "): map " + _mapId + ", " + _objects.Count + " objects, entities: " + cr + " creatures, " + npc + " NPCs, " + pl +
                        " players, " + go + " gameobjects (" + named + " named); update packets " + _updatePackets + " (" + _blocksApplied + " blocks, " + _parseErrors +
                        " malformed), queries " + _queriesSent + " sent / " + _queryAnswers + " answered, quest markers " + _markers.Count + ", unhandled: " + _world.UnhandledSummary(8));
        }

        /// <summary>DIALOG_STATUS_* (QuestDef.h) -> the marker drawn over the head.</summary>
        static QuestMarker MarkerOf(int status)
        {
            switch (status)
            {
                case 7: case 8: return QuestMarker.Available;       // AVAILABLE_REP, AVAILABLE: yellow "!"
                case 5: return QuestMarker.Incomplete;              // INCOMPLETE: grey "?"
                case 3: case 6: case 9: case 10: return QuestMarker.Complete;   // (LOW_LEVEL_)REWARD_REP, REWARD2, REWARD: yellow "?"
                default: return QuestMarker.None;                   // NONE, UNAVAILABLE, LOW_LEVEL_AVAILABLE(_REP): no marker
            }
        }

        void SetMarker(ulong guid, int status)
        {
            var m = MarkerOf(status);
            _markers[guid] = m;
            var e = Find(guid);
            if (e != null) e.Marker = m;
        }

        /// <summary>SMSG_QUESTGIVER_STATUS (PlayerMenu::SendQuestGiverStatus): uint64 guid, uint8 status.</summary>
        void OnQuestGiverStatus(WowPacket p)
        {
            ulong g = p.U64();
            SetMarker(g, p.U8());
        }

        /// <summary>SMSG_QUESTGIVER_STATUS_MULTIPLE: uint32 count, count x (uint64 guid, uint8 status).</summary>
        void OnQuestGiverStatusMultiple(WowPacket p)
        {
            uint n = p.U32();
            for (uint i = 0; i < n && p.Remaining >= 9; i++)
            {
                ulong g = p.U64();
                SetMarker(g, p.U8());
            }
        }

        // ------------------------------------------------------------------ reputation (ReputationMgr.cpp)

        /// <summary>SMSG_INITIALIZE_FACTIONS: uint32 0x80, then 128 x (uint8 flags, uint32 standing) by reputationListID.</summary>
        void OnInitializeFactions(WowPacket p)
        {
            uint n = p.U32();
            for (int i = 0; i < n && i < 128 && p.Remaining >= 5; i++)
            {
                _repFlags[i] = p.U8();
                _repStanding[i] = p.I32();
            }
            RefreshAllReactions();
        }

        /// <summary>SMSG_SET_FACTION_STANDING (ReputationMgr::SendState): float, uint8, uint32 count, count x (uint32 repListId, uint32 standing).</summary>
        void OnSetFactionStanding(WowPacket p)
        {
            p.F32(); p.U8();
            uint n = p.U32();
            for (uint i = 0; i < n && p.Remaining >= 8; i++)
            {
                uint id = p.U32();
                int standing = p.I32();
                if (id < 128) _repStanding[id] = standing;
            }
            RefreshAllReactions();
        }
    }
}
