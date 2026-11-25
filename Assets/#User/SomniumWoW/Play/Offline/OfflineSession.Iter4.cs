using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// Iteration 4 (Rules): flight paths and flight masters (TaxiNodes/TaxiPath/TaxiPathNode.dbc through WowTaxiDb: a flight master NPC at
    /// every flight-master node of the player's faction on this map, discovery, route through known nodes, cost, OnTaxi) and immersive
    /// held casts (CastSpellHeld / ReleaseHeldSpell). Friendly-target helpful spells live in OfflineSession.Spells.cs (ResolveTarget).
    /// </summary>
    public sealed partial class OfflineSession
    {
        /// <summary>Flight masters join Entities within this distance of the player (yards) and leave beyond FlightMasterHideRange.</summary>
        public const float FlightMasterShowRange = 200f, FlightMasterHideRange = 260f;
        /// <summary>Synthetic creature entries of the flight masters: FlightMasterEntryBase + TaxiNodes id.</summary>
        public const int FlightMasterEntryBase = 900000;
        /// <summary>Flight masters' level (ours: the 3.3.5 flight masters are high-level townsfolk).</summary>
        public const int FlightMasterLevel = 55;
        /// <summary>A held spell is dropped after this many seconds (stage S1, integrator: 30 s like the realm's hold module, PLAN10 s1.6; was 10).</summary>
        public const float HeldTimeout = 30f;
        /// <summary>A held spell is dropped when its target is farther than the spell's range plus this (yards).</summary>
        public const float HeldRangeSlack = 5f;

        private WowTaxiDb _taxiDb;
        private readonly List<OfflineCreature> _taxiMasters = new List<OfflineCreature>();
        private readonly Dictionary<ulong, int> _masterNode = new Dictionary<ulong, int>();
        private float _masterScan;
        private TaxiView _taxi;
        private OfflineCreature _taxiMaster;
        private int _taxiNode;

        private SpellInfo _held;
        private ulong _heldTargetGuid;
        private float _heldTimer;
        private bool _holdRequest, _castHold;

        // Stage S1 (integrator, PLAN10 s5 - a Dreamport house rule): the spell released with Immersive casting whose effects run now
        // (+ImmersiveMode.ImmersiveDamageBonus on its direct damage; its missile and its damaging aura carry the flag), 0 = none.
        private int _immersiveCastSpell;
        private int _immersiveReleases;
        private bool _immersiveBuffShown;
        private readonly AuraView _immersiveView = ImmersiveMode.NewBuff();

        /// <summary>
        /// Stage S1 (PLAN10 s5): a spell released with Immersive casting (the push of a held cast, or an instant / channel cast through
        /// CastSpellHeld) - FinishSpell with the immersive damage bonus for this one cast (direct damage now, its missile when it lands,
        /// the periodic damage of the aura it applies). Damage only; heals are never changed.
        /// </summary>
        private void FinishImmersive(SpellInfo s, OfflineUnit target, bool startGcd)
        {
            _immersiveReleases++;
            if (_immersiveReleases == 1)
                WowLog.Info("OfflineSession: Immersive casting - spells released with it deal +" + (ImmersiveMode.ImmersiveDamageBonus * 100f).ToString("0") + "% damage (ImmersiveDamageBonus "
                            + ImmersiveMode.ImmersiveDamageBonus.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", ours - a Dreamport house rule; logged once)");
            if (_immersiveReleases <= 5) WowLog.Info("OfflineSession: " + s.Name + " released with Immersive casting - +10% damage for this cast (" + _immersiveReleases + ")");
            int was = _immersiveCastSpell;
            _immersiveCastSpell = s.Id;
            try { FinishSpell(s, target, startGcd); }
            finally { _immersiveCastSpell = was; }
        }

        /// <summary>This spell's damage is part of a cast released with Immersive casting (FinishImmersive / its missile).</summary>
        private bool ImmersiveDamage(SpellInfo s) { return _immersiveCastSpell != 0 && s != null && s.Id == _immersiveCastSpell; }

        /// <summary>Stage S1: the "Immersive Mode" buff follows ImmersiveMode.Active (VR + Immersive casting) - refills the aura list on a change.</summary>
        private void StepImmersiveBuff()
        {
            if (ImmersiveMode.Active == _immersiveBuffShown) return;
            _immersiveBuffShown = ImmersiveMode.Active;
            _aurasChanged = true;
            WowLog.Info("OfflineSession: buff '" + ImmersiveMode.BuffName + "' " + (_immersiveBuffShown ? "shown (Immersive casting is on in VR: +10% damage on spells you push)" : "removed (Immersive casting off / not in VR)"));
        }

        /// <summary>The flight data (null without files).</summary>
        public WowTaxiDb TaxiDb { get { return _taxiDb; } }

        /// <summary>
        /// Testing switch (the user, 2026-09-27: "unlock all the flight paths for now so I can test"): every flight master node of
        /// the player's faction on every map counts as known for the flight map and routes. Real discoveries are still added to
        /// Player.KnownTaxiNodes and saved as before, so switching this off brings back the true list. The front end sets it from
        /// WowSettings.UnlockAllFlightPaths.
        /// </summary>
        public static bool UnlockAllFlightPaths = true;

        private HashSet<int> _allTaxiNodes;

        /// <summary>The nodes that count as known right now (all of the faction's while UnlockAllFlightPaths is on).</summary>
        private ICollection<int> KnownNodes
        {
            get
            {
                if (!UnlockAllFlightPaths || _taxiDb == null || !_taxiDb.Available) return _player.KnownTaxiNodes;
                if (_allTaxiNodes == null)
                {
                    _allTaxiNodes = new HashSet<int>(_player.KnownTaxiNodes);
                    var f = WowRaces.FactionOf(_rec.Race);
                    foreach (int map in new[] { 0, 1, 530, 571 })
                        foreach (var n in _taxiDb.FlightMasterNodes(map, f)) _allTaxiNodes.Add(n.Id);
                    WowLog.Info("OfflineSession: all " + _allTaxiNodes.Count + " flight paths of the " + f + " unlocked (testing switch)");
                }
                return _allTaxiNodes;
            }
        }

        // ------------------------------------------------------------------ setup / per step

        private void InitIter4()
        {
            foreach (int id in _rec.KnownTaxiNodes) if (!_player.KnownTaxiNodes.Contains(id)) _player.KnownTaxiNodes.Add(id);
            foreach (int id in WowTaxiDb.InitialNodes(_rec.Race, _player.Level)) if (!_player.KnownTaxiNodes.Contains(id)) _player.KnownTaxiNodes.Add(id);
            _taxiDb = _files != null ? WowTaxiDb.Get(_files) : null;
            if (_taxiDb == null || !_taxiDb.Available) return;
            var faction = WowRaces.FactionOf(_rec.Race);
            foreach (var n in _taxiDb.FlightMasterNodes(_zone.MapId, faction)) CreateFlightMaster(n, faction);
            WowLog.Info("OfflineSession: " + _taxiMasters.Count + " flight masters on map " + _zone.MapId + " (" + faction + "), " + _player.KnownTaxiNodes.Count + " known flight paths");
        }

        private void CreateFlightMaster(TaxiNodeInfo n, WowFaction f)
        {
            int display = _taxiDb.MasterDisplayId(n.Id, f);
            var tpl = new CreatureTemplate
            {
                Entry = FlightMasterEntryBase + n.Id, Name = WowTaxiDb.MasterName(n.Id, display), SubName = "Flight Master",
                DisplayIds = display != 0 ? new[] { display } : new int[0], MinLevel = FlightMasterLevel, MaxLevel = FlightMasterLevel,
                Reaction = Reaction.Friendly, IsNpc = true, CreatureType = 7, UnitClass = 1
            };
            // Beside the node (3 yd east), facing it: the flight path starts at the node itself.
            float ox = n.X + 3f, oy = n.Y;
            var home = new WowPos(ox, oy, n.Z, (float)Math.Atan2(n.Y - oy, n.X - ox));
            var c = new OfflineCreature();
            c.Tpl = tpl;
            c.Spawn = new SpawnDef { Entry = tpl.Entry, Pos = home, WanderRadius = 0f, RespawnSeconds = 60f };
            c.SpawnIndex = 0;
            c.Guid = (0xF130UL << 48) | ((ulong)(uint)tpl.Entry << 24);
            c.Home = home;
            c.State = new EntityState { Guid = c.Guid, Entry = tpl.Entry, Kind = EntityKind.Npc, Name = tpl.Name, SubName = tpl.SubName, Reaction = Reaction.Friendly, Scale = 1f };
            c.State.DisplayId = display;
            c.BaseDisplayId = display;
            c.CombatReach = 1.5f;
            c.Present = false;
            _taxiMasters.Add(c);
            _masterNode[c.Guid] = n.Id;
        }

        private void StepIter4(float dt)
        {
            StepHeld(dt);
            _masterScan -= dt;
            if (_masterScan <= 0f) { _masterScan = 1f; ScanFlightMasters(); }
            if (_taxi != null && (_taxiMaster == null || !_taxiMaster.Present || Dist(_me.Pos, _taxiMaster.Pos) > InteractRange + _taxiMaster.CombatReach + 6f)) CloseTaxi();
        }

        /// <summary>Flight masters near the player are in Entities (views draw them), far ones are not.</summary>
        private void ScanFlightMasters()
        {
            for (int i = 0; i < _taxiMasters.Count; i++)
            {
                var m = _taxiMasters[i];
                float d = Dist2D(_me.Pos, m.Home);
                if (!m.Present && d < FlightMasterShowRange)
                {
                    Respawn(m);
                    _creatures.Add(m);
                    _byGuid[m.Guid] = m;
                    _entitiesDirty = true;
                }
                else if (m.Present && d > FlightMasterHideRange)
                {
                    if (_target == m.Guid) SetTarget(0);
                    if (_dialogNpc == m) CloseDialog();
                    if (_taxiMaster == m) CloseTaxi();
                    _creatures.Remove(m);
                    _byGuid.Remove(m.Guid);
                    m.Present = false;
                    _entitiesDirty = true;
                    continue;
                }
                if (m.Present)
                {
                    // On the terrain, or at the node's height when that is higher (towers and platforms: the flight path starts up there).
                    float z = Ground(m.Home.X, m.Home.Y, m.Home.Z);
                    // Integrator (lab, iteration 4): also when the terrain is far ABOVE the node - Ironforge's stand is inside the
                    // mountain (a WMO under the terrain), and the terrain height put its master on the mountain top, out of reach.
                    if (z < m.Home.Z - 2f || z > m.Home.Z + 8f) z = m.Home.Z;
                    m.Pos.Z = z;
                    m.State.Pos = m.Pos;
                }
            }
        }

        /// <summary>True for a flight master entity.</summary>
        public bool IsFlightMaster(ulong guid) { return _masterNode.ContainsKey(guid); }

        private bool IsFlightMaster(OfflineCreature c) { return c != null && _masterNode.ContainsKey(c.Guid); }

        // ------------------------------------------------------------------ IWowSession: flight paths

        public TaxiView CurrentTaxi { get { return _taxi; } }

        public void CloseTaxi()
        {
            _taxi = null;
            _taxiMaster = null;
            _taxiNode = 0;
        }

        /// <summary>Interact on a flight master (in range, alive): discovers its node, opens the flight map.</summary>
        private void OpenTaxi(OfflineCreature master)
        {
            int node;
            if (!_masterNode.TryGetValue(master.Guid, out node)) return;
            CloseDialog();
            if (!_player.KnownTaxiNodes.Contains(node))
            {
                _player.KnownTaxiNodes.Add(node);
                EmitInfo("New flight path discovered!");                       // ERR_NEWTAXIPATH
                var ni = _taxiDb.Node(node);
                WowLog.Info("OfflineSession: discovered flight path " + node + " '" + (ni != null ? ni.Name : "?") + "'");
            }
            _taxiMaster = master;
            _taxiNode = node;
            _taxi = BuildTaxiView(master.Guid, node);
            bool any = false;
            foreach (var v in _taxi.Nodes) if (v.Reachable) { any = true; break; }
            if (!any) EmitError("You don't know any flight locations connected to this one.");   // ERR_TAXINOPATHS
        }

        private TaxiView BuildTaxiView(ulong masterGuid, int current)
        {
            var view = new TaxiView { MasterGuid = masterGuid, CurrentNodeId = current, MapId = _zone.MapId };
            var f = WowRaces.FactionOf(_rec.Race);
            foreach (var n in _taxiDb.FlightMasterNodes(_zone.MapId, f))
            {
                var v = new TaxiNodeView { Id = n.Id, Name = n.Name, MapId = n.MapId, X = n.X, Y = n.Y, Z = n.Z };
                v.Known = KnownNodes.Contains(n.Id);
                v.Current = n.Id == current;
                if (!v.Current)
                {
                    var route = _taxiDb.FindRoute(current, n.Id, f, KnownNodes);
                    if (route != null)
                    {
                        v.Reachable = true;
                        v.Route.AddRange(route);
                        v.Cost = UnlockAllFlightPaths ? 0 : _taxiDb.RouteCost(route);   // testing switch: free flights
                    }
                    var direct = _taxiDb.PathBetween(current, n.Id);
                    v.PathId = direct != null && _taxiDb.IsFlightMasterNode(n.Id, f) ? direct.Id : 0;
                }
                view.Nodes.Add(v);
            }
            return view;
        }

        public bool TakeTaxi(int destinationNodeId)
        {
            string why = CheckTaxi(destinationNodeId);
            if (why != null) { EmitError(why); return false; }
            var f = WowRaces.FactionOf(_rec.Race);
            var route = _taxiDb.FindRoute(_taxiNode, destinationNodeId, f, KnownNodes);
            int cost = UnlockAllFlightPaths ? 0 : _taxiDb.RouteCost(route);   // testing switch: free flights
            // AzerothCore ActivateTaxiPathTo charges the first hop at the start and each next hop when it begins; offline pays the whole route now.
            SpendMoney(cost);
            ulong master = _taxiMaster != null ? _taxiMaster.Guid : 0;
            int from = _taxiNode;
            StopAttack();
            if (_autoShot) SetAutoShot(false);
            if (_nextSwing != null) _nextSwing = null;
            BreakStealth();
            DismissPet(false);                         // the pet leaves for the flight and comes back on landing (RestorePet)
            CloseDialog();
            CloseLoot();
            CloseTaxi();
            _player.OnTaxi = true;
            _player.TaxiRoute.Clear();
            _player.TaxiRoute.AddRange(route);
            _player.TaxiPathId = route[0];
            _player.TaxiDestinationNode = destinationNodeId;
            _player.MountDisplayId = _taxiDb.MountDisplayId(from, f);
            TaxiPathPoint start;
            WowPos pos = _me.Pos;
            if (_taxiDb.RouteStart(route, out start)) pos = new WowPos(start.X, start.Y, start.Z, _me.Pos.O);
            var dest = _taxiDb.Node(destinationNodeId);
            var e = new GameEvent { Type = GameEventType.TaxiStarted, SourceGuid = PlayerGuid, TargetGuid = master, Amount = route[0], Pos = pos, Text = dest != null ? dest.Name : "" };
            _events.Enqueue(e);
            WowLog.Info("OfflineSession: flight " + from + " -> " + destinationNodeId + " '" + e.Text + "', " + route.Count + " hop(s) (paths " + string.Join(",", route) + "), "
                + (int)_taxiDb.RouteLength(route) + " yd, " + cost + " copper, mount display " + _player.MountDisplayId);
            return true;
        }

        /// <summary>Why the flight to this node cannot be taken now (client error strings, AzerothCore ActivateTaxiPathTo order), null = go.</summary>
        private string CheckTaxi(int dest)
        {
            if (_player.Dead) return "You are dead";
            if (_player.OnTaxi) return "You are busy and can't use the taxi service now.";
            if (_taxiDb == null || _taxi == null || _taxiMaster == null || !_taxiMaster.Present) return "There is no taxi vendor nearby!";
            if (Dist(_me.Pos, _taxiMaster.Pos) > InteractRange + _taxiMaster.CombatReach + 4f) return "You are too far away from the taxi stand!";
            if (dest == _taxiNode) return "You are already there!";
            if (_inCombat || _me.Stunned || _me.Rooted || _casting != null || _channel != null || _held != null) return "You are busy and can't use the taxi service now.";
            // Iteration 7: AC Player.cpp 10426-10431 (taximaster case) "// not let cheating with start flight mounted" `if (IsMounted())
            // { SendActivateTaxiReply(ERR_TAXIPLAYERALREADYMOUNTED); return false; }` (lead decision D12: refuse).
            if (_player.Mounted) return "You are already mounted! Dismount first.";   // ERR_TAXIPLAYERALREADYMOUNTED (GlobalStrings.lua:3522)
            if (_form != 0) { var fo = _db.Form(_form); if (fo != null && fo.ActsAsShifted) return "You can't take a taxi while shapeshifted!"; }
            var f = WowRaces.FactionOf(_rec.Race);
            var n = _taxiDb.Node(dest);
            if (n == null || n.MapId != _zone.MapId || !_taxiDb.IsFlightMasterNode(dest, f)) return "The selected path doesn't exist!";
            if (!KnownNodes.Contains(dest)) return "You haven't reached that taxi node on foot yet!";
            var route = _taxiDb.FindRoute(_taxiNode, dest, f, KnownNodes);
            if (route == null || route.Count == 0) return "There is no direct path to that destination!";
            if (!UnlockAllFlightPaths && _player.Money < _taxiDb.RouteCost(route)) return "You don't have enough money!";
            return null;
        }

        private void SpendMoney(int copper)
        {
            if (copper <= 0) return;
            _player.Money = Math.Max(0, _player.Money - copper);
        }

        public void FinishTaxi()
        {
            if (!_player.OnTaxi) return;
            TaxiPathPoint end;
            WowPos pos = _me.Pos;
            if (_taxiDb != null && _taxiDb.RouteEnd(_player.TaxiRoute, out end)) pos = new WowPos(end.X, end.Y, end.Z, _me.Pos.O);
            else { var n = _taxiDb != null ? _taxiDb.Node(_player.TaxiDestinationNode) : null; if (n != null) pos = new WowPos(n.X, n.Y, n.Z, _me.Pos.O); }
            int dest = _player.TaxiDestinationNode;
            _me.Pos = pos;
            _player.Pos = pos;
            _player.OnTaxi = false;
            _player.TaxiPathId = 0;
            _player.TaxiRoute.Clear();
            _player.TaxiDestinationNode = 0;
            _player.MountDisplayId = 0;
            _castStartPos = pos;
            var e = new GameEvent { Type = GameEventType.TaxiFinished, SourceGuid = PlayerGuid, Pos = pos };
            var dn = _taxiDb != null ? _taxiDb.Node(dest) : null;
            e.Text = dn != null ? dn.Name : null;
            _events.Enqueue(e);
            RestorePet();
            WowLog.Info("OfflineSession: landed at " + dest + " '" + e.Text + "' " + pos);
            ScanFlightMasters();
        }

        /// <summary>Saved mid-flight: the character is written at the landing point (it lands there on the next visit).</summary>
        private void SnapshotTaxi(CharacterRecord c)
        {
            TaxiPathPoint end;
            if (_taxiDb != null && _taxiDb.RouteEnd(_player.TaxiRoute, out end)) { c.Pos = new WowPos(end.X, end.Y, end.Z, _me.Pos.O); c.HasPos = true; }
        }

        // ------------------------------------------------------------------ IWowSession: immersive (held) casting

        /// <summary>
        /// Like CastSpell, but the spell does not fire when its cast time is over: it is held (Player.HeldSpellId, CastHeld event) until
        /// ReleaseHeldSpell. Instants are held at once. Auto-attack, Auto Shot, on-next-swing abilities and form toggles behave as CastSpell.
        /// </summary>
        public void CastSpellHeld(int spellId, ulong targetGuid)
        {
            if (MountCatalog.IsMount(spellId)) { CastSpell(spellId, targetGuid); return; }   // iteration 7: a mount spell never "holds"
            _holdRequest = true;
            try { CastSpell(spellId, targetGuid); }
            finally { _holdRequest = false; }
            if (_casting != null && _casting.Id == spellId) _castHold = true;
        }

        /// <summary>Fires the held spell at targetGuid when that is a valid target for it (else at the target it was cast at).</summary>
        public void ReleaseHeldSpell(ulong targetGuid)
        {
            if (_held == null) return;
            var s = _held;
            OfflineUnit target = UnitOf(_heldTargetGuid);
            if (targetGuid != 0)
            {
                OfflineUnit alt = targetGuid == PlayerGuid ? (OfflineUnit)_me : CreatureOf(targetGuid);
                if (alt != null && alt != target && ValidHeldTarget(s, alt))
                {
                    target = alt;
                    if (alt != _me && _target != alt.Guid) SetTarget(alt.Guid);
                }
            }
            _held = null;
            _heldTimer = 0f;
            if (target == null || target.Dead) { CastFailed(s.Id, "Invalid target"); return; }
            string why = CheckCast(s, target, false);
            if (why != null) { CastFailed(s.Id, why); return; }
            FinishImmersive(s, target, false);   // stage S1 (PLAN10 s5): the push releases it with the immersive damage bonus
        }

        /// <summary>The cast is over (or the spell is instant): hold it instead of firing.</summary>
        private void HoldSpell(SpellInfo s, OfflineUnit target, bool startGcd)
        {
            if (startGcd) { StartGcd(s); BreakStealthFor(s); CancelConsumable(null); }
            _castHold = false;
            _held = s;
            _heldTargetGuid = target != null ? target.Guid : PlayerGuid;
            _heldTimer = 0f;
            Emit(GameEventType.CastHeld, PlayerGuid, _heldTargetGuid, 0, s.Id, false, s.Name);
        }

        private void DropHeld(string reason)
        {
            if (_held == null) return;
            int id = _held.Id;
            ulong t = _heldTargetGuid;
            _held = null;
            _heldTimer = 0f;
            Emit(GameEventType.CastFailed, PlayerGuid, t, 0, id, false, reason);
        }

        private void StepHeld(float dt)
        {
            if (_held == null) return;
            _heldTimer += dt;
            if (_player.Dead) { DropHeld("You are dead"); return; }
            if (_heldTimer > HeldTimeout) { DropHeld("Interrupted"); return; }
            if (_me.Stunned || _me.Feared || _me.Confused || (_me.Silenced && _held.PreventionType == 1 && _held.DmgClass == 1)) { DropHeld("Interrupted"); return; }
            var target = UnitOf(_heldTargetGuid);
            if (target == null || target.Dead) { DropHeld("Invalid target"); return; }
            if (target != _me)
            {
                float min, max;
                RangeOf(_held, target, out min, out max);
                if (Dist(_me.Pos, target.Pos) > max + HeldRangeSlack) { DropHeld("Out of range"); EmitError("Out of range"); }
            }
        }

        /// <summary>Can the held spell be re-aimed at this unit? Harmful: a living attackable creature; helpful: yourself or a friendly unit it can target.</summary>
        private bool ValidHeldTarget(SpellInfo s, OfflineUnit u)
        {
            if (u == null || u.Dead) return false;
            var c = u as OfflineCreature;
            if (HelpfulOnFriend(s, c)) return true;
            if (s.IsHarmful || HasEnemyTargetEffect(s)) return c != null && IsAttackable(c);
            return u == _me;
        }

        private void UpdateViewsIter4()
        {
            _player.HeldSpellId = _held != null ? _held.Id : 0;
            _player.HeldTargetGuid = _held != null ? _heldTargetGuid : 0;
        }

        // ------------------------------------------------------------------ friendly targets (iteration 4)

        /// <summary>A living unit on the player's side the player may help: friendly NPCs (flight masters, quest givers), the pet, totems.</summary>
        private static bool IsFriendlyUnit(OfflineCreature c)
        {
            return c != null && c.Present && !c.Dead && (c.Summon != SummonKind.None || c.Tpl.IsNpc || c.State.Reaction == Reaction.Friendly);
        }

        /// <summary>The spell has an effect aimed at a picked ally (TARGET_UNIT_TARGET_ALLY / _ANY / _PARTY / _RAID) and none at an enemy only.</summary>
        private static bool CanTargetAlly(SpellInfo s)
        {
            if (s.IsHarmful) return false;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                if (e.TargetA == SpellTarget.TargetAlly || e.TargetA == SpellTarget.TargetAny || e.TargetA == SpellTarget.TargetParty || e.TargetA == SpellTarget.TargetRaid) return true;
            }
            return false;
        }

        /// <summary>True when s is cast as a helpful spell on this friendly creature (heals, buffs, dispels on a targeted friend).</summary>
        private static bool HelpfulOnFriend(SpellInfo s, OfflineUnit u)
        {
            var c = u as OfflineCreature;
            return c != null && IsFriendlyUnit(c) && CanTargetAlly(s);
        }
    }
}
