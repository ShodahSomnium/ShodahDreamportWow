using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// The offline game (iteration 2): a local simulation of one zone for the local player, implementing IWowSession. Pure C#.
    /// Spells come from the player's Spell.dbc at runtime (SpellDb); server-owned numbers from WowFormulas (AzerothCore facts).
    /// Construct it after the avatar has arrived (call OfflineSession.Preload(files) on a worker thread during the loading screen),
    /// call Tick every frame, SetPlayerPosition every frame, drain TryDequeueEvent every frame, save Snapshot() now and then.
    /// See the Rules report "API for view builders".
    /// </summary>
    public sealed partial class OfflineSession : IWowSession
    {
        /// <summary>The player's guid (creature guids use WoW's layout 0xF130 &lt;&lt; 48 | entry &lt;&lt; 24 | spawn index).</summary>
        public const ulong PlayerGuid = 1;

        /// <summary>Talk/loot distance in yards (WoW's INTERACTION_DISTANCE is 5; a little more for VR arms).</summary>
        public const float InteractRange = 6f;

        private readonly WowFiles _files;
        private readonly ZoneContent _zone;
        private readonly CharacterRecord _rec;
        private readonly Func<float, float, float, float> _groundZ;
        private readonly SpellDb _db;
        private readonly WowGameTables _gt;
        private readonly DbcTable _itemDbc, _itemDisplay;
        private readonly Random _rng;

        private readonly PlayerState _player = new PlayerState();
        private readonly PlayerUnit _me = new PlayerUnit();
        private readonly List<OfflineCreature> _creatures = new List<OfflineCreature>();
        private readonly List<EntityState> _entities = new List<EntityState>();
        private readonly Dictionary<ulong, OfflineCreature> _byGuid = new Dictionary<ulong, OfflineCreature>();
        private readonly Queue<GameEvent> _events = new Queue<GameEvent>();
        private readonly Dictionary<int, CreatureTemplate> _templates = new Dictionary<int, CreatureTemplate>();
        private readonly Dictionary<int, ItemDef> _items = new Dictionary<int, ItemDef>();
        private readonly Dictionary<int, QuestDef> _quests = new Dictionary<int, QuestDef>();

        private double _time;
        private ulong _target;
        private NpcDialog _dialog;
        private OfflineCreature _dialogNpc;
        private LootView _loot;
        private OfflineCreature _lootCorpse;
        private bool _entitiesDirty = true;
        private float _savePlayed;

        private sealed class PlayerUnit : OfflineUnit
        {
            public override bool IsPlayer { get { return true; } }
        }

        /// <summary>Reads the spell database, Item.dbc and the gt tables once (cached): call on a worker thread while loading.</summary>
        public static void Preload(WowFiles files)
        {
            if (files == null) return;
            var db = SpellDb.Get(files);
            foreach (var c in new[] { WowClass.Warrior, WowClass.Hunter, WowClass.Rogue, WowClass.Priest, WowClass.Druid }) db.Kit(c, 4);
            Dbc.Load(files, "Item");
            Dbc.Load(files, "ItemDisplayInfo");
            WowGameTables.Load(files);
            MountCatalog.Load(files);         // iteration 7: the mounts of Spell.dbc + the flying-area tables (cached)
            MountAreaRules.Load(files);
        }

        public OfflineSession(WowFiles files, ZoneContent zone, CharacterRecord character, Func<float, float, float, float> groundZ)
            : this(files, zone, character, groundZ, Environment.TickCount)
        {
        }

        /// <summary>As the main constructor, with a fixed random seed (tests/probes).</summary>
        public OfflineSession(WowFiles files, ZoneContent zone, CharacterRecord character, Func<float, float, float, float> groundZ, int seed)
        {
            var t0 = DateTime.UtcNow;
            _files = files;
            _zone = zone ?? new ZoneContent { Name = "Empty", MapId = 1 };
            _rec = character ?? CharacterRecord.CreateNew(WowClass.Warrior, "Adventurer", _zone);
            _groundZ = groundZ;
            _rng = new Random(seed);
            _db = SpellDb.Get(files);
            _gt = WowGameTables.Load(files);
            _itemDbc = files != null ? Dbc.Load(files, "Item") : null;
            _itemDisplay = files != null ? Dbc.Load(files, "ItemDisplayInfo") : null;

            foreach (var t in _zone.Creatures) if (t != null) _templates[t.Entry] = t;
            foreach (var d in _zone.Items) if (d != null) _items[d.Entry] = d;
            foreach (var q in _zone.Quests) if (q != null) _quests[q.Id] = q;

            LoadMountData();                           // iteration 7: before InitPlayer (the spellbook leaves mount spells to Player.Mounts)
            InitPlayer();
            InitIter4();
            InitMounts();
            SpawnAll();
            RefreshQuestLog();
            RefreshMarkers();
            RefreshEntityList();
            try { TalentsOfflineSync(); }   // integrator (talents request): the record's talent passives apply at once (idempotent)
            catch (Exception e) { WowLog.Warn("OfflineSession: offline talents sync failed: " + e.Message); }
            WowLog.Info("OfflineSession: " + _zone.Name + " map " + _zone.MapId + ", " + _creatures.Count + " spawns (" + _templates.Count + " templates), "
                + _quests.Count + " quests, " + _items.Count + " items; " + _rec + " with " + _player.Spellbook.Count + " spells"
                + (_db.Available ? "" : " (NO Spell.dbc - no spells)") + ", " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms");
        }

        // ------------------------------------------------------------------ IWowSession: state

        public PlayerState Player { get { return _player; } }
        public int MapId { get { return _zone.MapId; } }
        public IReadOnlyList<EntityState> Entities { get { return _entities; } }
        public ulong TargetGuid { get { return _target; } }
        public NpcDialog CurrentDialog { get { return _dialog; } }
        public LootView CurrentLoot { get { return _loot; } }

        /// <summary>The zone being simulated.</summary>
        public ZoneContent Zone { get { return _zone; } }
        /// <summary>The spell database (read from the game files).</summary>
        public SpellDb Spells { get { return _db; } }
        /// <summary>Simulation time in seconds since the session started.</summary>
        public double Time { get { return _time; } }

        public EntityState Find(ulong guid)
        {
            OfflineCreature c;
            return _byGuid.TryGetValue(guid, out c) && c.Present ? c.State : null;
        }

        /// <summary>The zone's item definition, or null.</summary>
        public ItemDef FindItemDef(int entry)
        {
            ItemDef d;
            return _items.TryGetValue(entry, out d) ? d : null;
        }

        public bool TryDequeueEvent(out GameEvent e)
        {
            if (_events.Count > 0) { e = _events.Dequeue(); return true; }
            e = default(GameEvent);
            return false;
        }

        /// <summary>A copy of the character as it stands now (position, level, bags, quests...) for saving.</summary>
        public CharacterRecord Snapshot()
        {
            var c = new CharacterRecord();
            c.Id = _rec.Id;
            c.Name = _rec.Name;
            c.Class = _rec.Class;
            c.Race = _rec.Race;
            c.Level = _player.Level;
            c.Xp = _player.Xp;
            c.Money = _player.Money;
            c.MapId = _zone.MapId;
            if (_player.Dead)
            {
                c.Pos = _zone.Graveyard; c.HasPos = true; c.Health = -1; c.Power = -1;   // come back alive at the graveyard
            }
            else
            {
                c.Pos = _me.Pos; c.HasPos = true; c.Health = _me.Health;
                c.Power = _rec.Class == WowClass.Warrior ? (int)_rage : _rec.Class == WowClass.Rogue ? (int)_energy : _mana;
            }
            c.Spells.AddRange(_rec.Spells);
            // iteration 11 (vrcast s6.2 #5, applied in wave 0): all 144 packed buttons; "bar" stays the 12 spells of page 1 for older readers
            Array.Copy(_player.ActionButtons, c.ActionButtons, WowActionSlots.Count);
            for (int k = 0; k < 12; k++) c.ActionBar[k] = WowActionSlots.SpellOf(_player.ActionButtons[k]);
            c.HasSlots = true;
            c.FormBarsFilled = _rec.FormBarsFilled;
            foreach (var s in _rec.Bags) c.Bags.Add(new ItemStack(s.Entry, s.Count) { Slot = s.Slot });   // iteration 11 (npc H11): the backpack slot
            Array.Copy(_rec.Equipment, c.Equipment, 19);
            c.Talents.AddRange(_rec.Talents);                             // iteration 11 (talents hook 9): offline talents (talents keeps _rec.Talents current)
            foreach (var q in _rec.Quests) { var n = new QuestRecord { Id = q.Id, State = q.State }; Array.Copy(q.Counts, n.Counts, 4); c.Quests.Add(n); }
            c.Rewarded.AddRange(_rec.Rewarded);
            c.PetEntry = _rec.PetEntry;                                   // iteration 4 fix: the pet that was out comes back on the next visit
            c.KnownTaxiNodes.AddRange(_player.KnownTaxiNodes);
            if (_player.OnTaxi && !_player.Dead) SnapshotTaxi(c);         // mid-flight: saved at the landing point
            c.CreatedUnix = _rec.CreatedUnix;
            c.PlayedSeconds = _rec.PlayedSeconds + (long)_savePlayed;
            return c;
        }

        // ------------------------------------------------------------------ IWowSession: per frame

        public void SetPlayerPosition(WowPos pos, bool moving)
        {
            if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsNaN(pos.Z)) return;
            _me.Pos = pos;
            _player.Pos = pos;
            // Iteration 7: while a ride drives the avatar the motion says whether the player moves (AC isMoving: forward, back, strafe,
            // ascend / descend, falling - turning in place is not moving). SetPlayerMotion comes right before this call every frame.
            _moving = _motion.Driven ? (_motion.Forward != 0 || _motion.Strafe != 0 || _motion.Vertical != 0 || _motion.Airborne) : moving;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds)) return;
            if (deltaSeconds > 0.5f) deltaSeconds = 0.5f;     // a hitch must not fast-forward fights
            while (deltaSeconds > 0f)
            {
                float dt = deltaSeconds > 0.1f ? 0.1f : deltaSeconds;
                deltaSeconds -= dt;
                Step(dt);
            }
            UpdateViews();
        }

        private void Step(float dt)
        {
            _time += dt;
            _savePlayed += dt;
            StepPlayer(dt);
            StepPending();
            StepPlayerExtras(dt);
            StepIter4(dt);
            TickMotion(dt);                                    // iteration 11 (motion 17d): fear / confuse segments, control flags (OfflineSession.Motion.cs)
            TickStates(dt);                                    // states fix 2026-10-04: stand state (eating, /sit /kneel /sleep) for the avatar poses (OfflineSession.States.cs)
            StepCritters(dt);                                  // iteration 5: WoW-style critters around the player (OfflineSession.Critters.cs)
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                StepCreature(c, dt);
                if (i < _creatures.Count && _creatures[i] != c) i--;   // a pet/totem left the list during its step
            }
            StepQuestsExplore(dt);
            if (_questDirty) QuestChanged();
            if (_entitiesDirty) RefreshEntityList();
        }

        // ------------------------------------------------------------------ events

        private void Emit(GameEventType type, ulong source, ulong target, int amount, int spellId, bool crit, string text)
        {
            var e = new GameEvent { Type = type, SourceGuid = source, TargetGuid = target, Amount = amount, SpellId = spellId, Crit = crit, Text = text };
            _events.Enqueue(e);
            if (_events.Count > 512) _events.Dequeue();   // nobody drains: keep memory bounded
        }

        private void EmitError(string text) { Emit(GameEventType.Error, 0, 0, 0, 0, false, text); }
        private void EmitInfo(string text) { Emit(GameEventType.Info, 0, 0, 0, 0, false, text); }

        /// <summary>Iteration 11 (wave 0, w0play): the answer of a wave-0 offline stub - one Info line "Not available offline yet" (ours) and one
        /// log line per kind, so a button pressed offline is never silent. Owners replace their stubs; this helper may then go unused.</summary>
        private void OfflineNotYet(string what)
        {
            EmitInfo("Not available offline yet");   // ours (PLAN11 wave 0)
            if (_offlineNotYetLogged.Add(what)) WowLog.Info("Offline: " + what + " - not available offline yet (iteration 11, wave-0 stub).");
        }
        private readonly HashSet<string> _offlineNotYetLogged = new HashSet<string>();

        private void EmitQuest(GameEventType type, int questId, string text)
        {
            var e = new GameEvent { Type = type, QuestId = questId, Text = text };
            _events.Enqueue(e);
        }

        private void EmitItem(GameEventType type, int entry, int count, string name)
        {
            var e = new GameEvent { Type = type, ItemEntry = entry, Amount = count, Text = name };
            _events.Enqueue(e);
        }

        // ------------------------------------------------------------------ spawning

        private void SpawnAll()
        {
            int index = 0;
            foreach (var sp in _zone.Spawns)
            {
                index++;
                if (sp == null) continue;
                CreatureTemplate tpl;
                if (!_templates.TryGetValue(sp.Entry, out tpl)) { WowLog.Warn("OfflineSession: spawn of unknown creature entry " + sp.Entry); continue; }
                var c = new OfflineCreature();
                c.Tpl = tpl;
                c.Spawn = sp;
                c.SpawnIndex = index;
                c.Guid = (0xF130UL << 48) | ((ulong)(uint)tpl.Entry << 24) | (ulong)(uint)index;
                c.Home = sp.Pos;
                c.State = new EntityState();
                c.State.Guid = c.Guid;
                c.State.Entry = tpl.Entry;
                c.State.Kind = tpl.IsNpc ? EntityKind.Npc : EntityKind.Creature;
                c.State.Name = tpl.Name ?? "";
                c.State.SubName = tpl.SubName ?? "";
                c.State.Reaction = tpl.IsNpc ? Reaction.Friendly : tpl.Reaction;
                c.State.Scale = tpl.Scale <= 0 ? 1f : tpl.Scale;
                c.State.MainHandDisplayId = tpl.MainHandDisplayId;
                c.State.OffHandDisplayId = tpl.OffHandDisplayId;
                c.State.DisplayId = tpl.DisplayIds != null && tpl.DisplayIds.Length > 0 ? tpl.DisplayIds[(index * 7919 + tpl.Entry) % tpl.DisplayIds.Length] : 0;
                c.BaseDisplayId = c.State.DisplayId;
                c.CombatReach = 1.5f * c.State.Scale;
                c.CanParry = tpl.MainHandDisplayId != 0;
                c.Elite = tpl.Elite;
                // Lead fix (2026-10-03): the elite dragon on the target frame and the NPC's cursor (WoW: creature_template rank / IconName)
                c.State.Classification = tpl.Elite ? 1 : 0;
                c.State.CursorName = !tpl.IsNpc ? null : SomniumSpace.Worlds.SomniumWoW.Play.Content.OfflineVendors.Sells(tpl.Entry) ? "Buy"
                    : SomniumSpace.Worlds.SomniumWoW.Play.Professions.OfflineTrainers.TrainsClass(tpl) != WowClass.None ? "Trainer" : null;
                Respawn(c);
                _creatures.Add(c);
                _byGuid[c.Guid] = c;
            }
        }

        private void Respawn(OfflineCreature c)
        {
            var tpl = c.Tpl;
            int lo = Math.Min(tpl.MinLevel, tpl.MaxLevel), hi = Math.Max(tpl.MinLevel, tpl.MaxLevel);
            c.Level = Math.Max(1, lo == hi ? lo : _rng.Next(lo, hi + 1));
            var b = WowFormulas.CreatureStats(tpl.UnitClass, c.Level);
            c.MaxHealth = Math.Max(1, (int)(b.Health * (tpl.HealthMultiplier <= 0 ? 1f : tpl.HealthMultiplier) * CreatureHealthTuning * DifficultyHealth[_difficulty]));
            c.MaxMana = Math.Max(0, b.Mana);
            c.Mana = c.MaxMana;
            c.CreatureType = tpl.CreatureType;
            c.State.CreatureType = tpl.CreatureType;
            c.State.Power = c.MaxMana > 0 ? PowerType.Mana : c.State.Power;
            c.State.PowerValue = c.Mana;
            c.State.PowerMax = c.MaxMana;
            c.Casting = null;
            c.Threat.Clear();
            c.ForcedTimer = 0f;
            c.SpellTimer = 0f;
            c.SpellIndex = 0;
            if (c.BaseDisplayId != 0) c.State.DisplayId = c.BaseDisplayId;
            c.Health = c.MaxHealth;
            c.Armor = b.Armor;
            c.AttackSpeed = tpl.AttackSpeed > 0.2f ? tpl.AttackSpeed : 2f;
            float mn, mx;
            c.DamageBase = b.DamageBase;
            c.BaseAttackPower = b.AttackPower;
            c.DamageMod = (tpl.DamageMultiplier <= 0 ? 1f : tpl.DamageMultiplier) * CreatureDamageTuning * DifficultyDamage[_difficulty];
            WowFormulas.CreatureDamage(b, c.AttackSpeed, c.DamageMod, out mn, out mx);
            c.MinDamage = Math.Max(1f, mn);
            c.MaxDamage = Math.Max(c.MinDamage, mx);
            c.Dead = false;
            c.Pos = c.Home;
            c.Pos.Z = Ground(c.Pos.X, c.Pos.Y, c.Pos.Z);
            c.Auras.Clear();
            c.Ai = AiState.Idle;
            c.WaitTimer = 1f + (float)_rng.NextDouble() * 6f;
            c.HasMoveTarget = false;
            c.Engaged = false;
            c.AssistDelay = -1f;
            c.SwingTimer = 0f;
            c.LeashTimer = 0f;
            c.LootGenerated = false;
            c.GaveXp = false;
            c.DamagedByPlayer = false;
            c.LootItems.Clear();
            c.LootMoney = 0;
            c.Present = true;
            var s = c.State;
            s.Level = c.Level;
            s.Health = c.Health;
            s.MaxHealth = c.MaxHealth;
            s.Dead = false;
            s.Lootable = false;
            s.InCombat = false;
            s.TargetGuid = 0;
            s.Evading = false;
            s.Pos = c.Pos;
            s.Emote = tpl.Emote;
            s.EmoteOnce = false;
            if (tpl.Emote != 0) s.EmoteCounter++;
            s.Auras.Clear();
            _entitiesDirty = true;
        }

        private void RefreshEntityList()
        {
            _entities.Clear();
            for (int i = 0; i < _creatures.Count; i++) if (_creatures[i].Present) _entities.Add(_creatures[i].State);
            _entitiesDirty = false;
        }

        /// <summary>groundZ from the front end (terrain height at x,y near zHint) or the hint when unknown.</summary>
        private float Ground(float x, float y, float zHint)
        {
            if (_groundZ == null) return zHint;
            float z;
            try { z = _groundZ(x, y, zHint); }
            catch (Exception) { return zHint; }
            return float.IsNaN(z) || float.IsInfinity(z) ? zHint : z;
        }

        // ------------------------------------------------------------------ geometry helpers

        private static float Dist(WowPos a, WowPos b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static float Dist2D(WowPos a, WowPos b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float Angle(WowPos from, WowPos to) { return (float)Math.Atan2(to.Y - from.Y, to.X - from.X); }

        /// <summary>WoW melee range: max(5, reachA + reachB + 4/3) yards.</summary>
        private static float MeleeRange(OfflineUnit a, OfflineUnit b) { return Math.Max(5f, a.CombatReach + b.CombatReach + 4f / 3f); }

        /// <summary>True when b stands behind a (outside a's front 180 degrees).</summary>
        private static bool IsBehind(OfflineUnit a, OfflineUnit b)
        {
            float ang = Angle(a.Pos, b.Pos) - a.Pos.O;
            while (ang > Math.PI) ang -= (float)(2 * Math.PI);
            while (ang < -Math.PI) ang += (float)(2 * Math.PI);
            return Math.Abs(ang) > Math.PI / 2;
        }

        private OfflineUnit UnitOf(ulong guid)
        {
            if (guid == PlayerGuid) return _me;
            OfflineCreature c;
            return _byGuid.TryGetValue(guid, out c) && c.Present ? c : null;
        }

        private OfflineCreature CreatureOf(ulong guid)
        {
            OfflineCreature c;
            return _byGuid.TryGetValue(guid, out c) && c.Present ? c : null;
        }

        private float Rand01() { return (float)_rng.NextDouble(); }
        private float RandRange(float a, float b) { return a + (b - a) * (float)_rng.NextDouble(); }

        // ------------------------------------------------------------------ targeting

        public void SetTarget(ulong guid)
        {
            if (guid != 0 && guid != PlayerGuid && CreatureOf(guid) == null) guid = 0;
            if (_target == guid) return;
            _target = guid;
            if (_autoAttack && (guid == 0 || !IsAttackable(CreatureOf(guid)))) StopAttack();
            if (_autoShot && (guid == 0 || !IsAttackable(CreatureOf(guid)))) SetAutoShot(false);
        }

        public void TargetNearestEnemy()
        {
            OfflineCreature best = null, first = null;
            float bestD = float.MaxValue, firstD = float.MaxValue;
            float curD = -1f;
            var cur = CreatureOf(_target);
            if (cur != null) curD = Dist(_me.Pos, cur.Pos);
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!IsAttackable(c)) continue;
                float d = Dist(_me.Pos, c.Pos);
                if (d > 40f) continue;
                if (d < firstD) { firstD = d; first = c; }
                if (c.Guid == _target) continue;
                if (d > curD && d < bestD) { bestD = d; best = c; }   // cycle outward from the current target
            }
            var pick = best ?? first;
            if (pick != null) SetTarget(pick.Guid);
            else EmitError("No enemies nearby");
        }

        private static bool IsAttackable(OfflineCreature c)
        {
            return c != null && c.Present && !c.Dead && !c.Tpl.IsNpc && c.State.Reaction != Reaction.Friendly;
        }
    }
}
