using System;
using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Bridge;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Spell visuals from the game's own data: Spell.dbc SpellVisual[0] -> SpellVisual.dbc kits (precast, cast, impact, state,
    /// state done, channel) + missile -> SpellVisualKit.dbc effect slots (head, chest, base, hands, breath, special...) + sound
    /// -> SpellVisualEffectName.dbc model + scale (Models/SpellVisualDb.cs, layouts measured on the real files).
    /// Reacts to WowPlay.Event:
    ///   CastStarted   -> precast kit on the caster, looped until CastSucceeded / CastFailed (or cast time + 1 s)
    ///   CastSucceeded -> cast kit on the caster; missile model from the caster's missile slot to the target's destination slot
    ///                    at Spell.dbc speed, impact kit on arrival (instant impact when the speed is 0 / no missile)
    ///   ChannelStarted / ChannelEnded -> channel kit looped on the caster
    ///   AuraApplied / AuraRemoved     -> state kit looped on the unit, state-done kit when it ends
    ///   LevelUp       -> SpellVisualEffectName 21 "HARDCODED Unit Level Up" (Spells\LevelUp\LevelUp.m2) at the unit's feet
    /// Kit sounds go through WowAudio.PlaySoundEntry (also when WowSettings.SpellEffects hides the visuals).
    /// Anchors: entities via WowEntityViews.GetAttachment (M2 attachment per slot, fallback id, else the model root); the local
    /// player = the Somnium avatar: Bridge Body hands/head in VR, the avatar's humanoid Animator bones on desktop (GearBody).
    /// Added by the front end (WowSpellVfx.Ensure()); M2EffectsRunner also calls Ensure once a session runs.
    /// Stage O2 (owner Sync, PLAN9 s0 decision 2) - Somnium avatars: a unit that is a Somnium player's avatar (another player's online
    /// WoW character - their Gear record names its guid and the server's copy is not drawn - or a record-driven key of GearCast) is
    /// anchored to that AVATAR as this client draws it (WowGear.TryAvatarSlot: re-mapped, ride-corrected; nothing while it is hidden).
    /// A single-hand cast (VR: the hand that released the radial menu, from the caster's record or GearLocal) puts the kit's hand and
    /// weapon effects on that hand only and starts the missile there. Record-driven casts (AvatarCast / AvatarRelease: offline, or a
    /// character the server does not show us) draw the caster's look only - never a missile, never an impact on anything but the caster.
    /// </summary>
    public sealed class WowSpellVfx : MonoBehaviour
    {
        const float Yd = 0.9144f;
        const int MaxEffects = 128, MaxMissiles = 32;   // iteration 11 (spellfx): 64 -> 128, areas spawn many short models
        const float SpawnDistance = 90f;
        // iteration 11 (spellfx) - ours: impacts of one spell id at most this many per second (a raid's Blizzard ticks hit many units),
        // a ground area's loop fades this long, the ground under a place is searched from this high (layers 0 / 14 = VrTargetRay.WorldMask).
        const int ImpactCapPerSecond = 12;
        const float AreaFade = 0.3f, GroundProbeUp = 3f, GroundProbeDown = 40f;
        const int GroundMask = (1 << 0) | (1 << 14);

        public static WowSpellVfx Instance { get; private set; }

        /// <summary>Render-lab hooks (null in the world): game files, and an anchor resolver (guid, M2 attachment id) -> transform.</summary>
        public static WowFiles LabFiles;
        public static Func<ulong, int, Transform> LabAnchor;

        sealed class Fx
        {
            public GameObject Go;
            public ulong Unit;
            public long Key;          // loop key (precast/channel: caster; state: unit ^ spell), 0 = one-shot
            public int Kind;          // 1 precast, 2 channel, 3 state
            public float EndAt;       // one-shot end / loop safety end (float.MaxValue = until removed)
            public float DieAt = -1f; // after stopping: destroy time (particles fade)
            public int Spell;         // lead fix 2026-10-03: the aura of a state loop (kind 3), 0 = not an aura
        }

        sealed class Missile
        {
            public GameObject Go;
            public ulong Caster, Target;
            public SpellVisualDb.SpellFx Spell;
            public int DestSlot;
            public float Speed;       // m/s
            public Vector3 LastTarget;
            public float Deadline;
            public Transform Place;   // iteration 11 (spellfx): a missile to a place on the ground (Target 0): its ImpactArea kit plays there on arrival
        }

        // iteration 11 (spellfx): a ground area drawn for an AreaEffectView (its PersistentArea kit looped at the place + the CharProc 9 spawner)
        sealed class Area
        {
            public ulong Guid;
            public int SpellId;
            public Transform Anchor;      // a free object at the place (scene root; shifted on rebase)
            public long Key;              // loop key of its kit effects (kind 4)
            public string SpawnModel;     // SpellVisualKitAreaModel path (CharProc 9), null = none
            public float SpawnEvery, SpawnNext, Radius;   // seconds (1 / CharParamOne, ours), Unity metres
            public float SpawnScale = 1f;
            public bool Seen;
        }

        readonly List<Area> _areas = new List<Area>();
        readonly List<Transform> _places = new List<Transform>();      // free anchors of place kits / place missiles (destroyed with their effects)
        readonly List<float> _placeDieAt = new List<float>();          // their end (ours: 15 s - one-shot kits last 0.8..3 s, missiles 12 s at most)
        readonly Dictionary<int, int> _impactCount = new Dictionary<int, int>();
        float _impactWindowAt;
        bool _impactCapLogged, _areaRulesLogged;
        int _areasDrawn, _placeKits;
        readonly HashSet<int> _areaLogged = new HashSet<int>();

        readonly List<Fx> _fx = new List<Fx>();
        readonly List<Missile> _missiles = new List<Missile>();
        SpellVisualDb _db;
        WowFiles _dbFiles;
        bool _dbFailed;
        int _spawned, _missilesFired, _impacts, _skippedFar;
        float _logAt = -1f;

        // local player anchors (Somnium avatar)
        readonly HashSet<ulong> _reanchorSaid = new HashSet<ulong>();
        GearBody _body;
        Transform _playerRoot;
        readonly Transform[] _playerSlots = new Transform[12];
        int _bodyFrame = -1;
        bool _bodyValid;

        /// <summary>Creates the component on its own object when none exists (idempotent). Returns the instance.</summary>
        public static WowSpellVfx Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WoW Spell Vfx");
            return go.AddComponent<WowSpellVfx>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable()
        {
            WowPlay.Event += OnGameEvent;
            WowPlay.Ended += OnEnded;
            WowSettings.Changed += OnSettings;
            WowSpace.Rebased += OnRebased;
        }

        void OnDisable()
        {
            WowPlay.Event -= OnGameEvent;
            WowPlay.Ended -= OnEnded;
            WowSettings.Changed -= OnSettings;
            WowSpace.Rebased -= OnRebased;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ClearAll();
            if (_playerRoot != null) Destroy(_playerRoot.gameObject);
        }

        /// <summary>
        /// Floating origin (WowSpace.Rebased): missiles and free-standing impacts live at the scene root (WowOrigin does not
        /// move them), effects on units move with the unit roots (WowOrigin moved those); world-space particles and trails of
        /// all of them are shifted here so nothing streaks or jumps.
        /// </summary>
        void OnRebased(Vector3 d)
        {
            foreach (var m in _missiles)
            {
                if (m.Go == null) continue;
                if (m.Go.transform.parent == null) m.Go.transform.position -= d;
                m.LastTarget -= d;
                WowOrigin.ShiftEffects(m.Go, d);
            }
            foreach (var f in _fx)
            {
                if (f.Go == null) continue;
                if (f.Go.transform.parent == null) f.Go.transform.position -= d;
                WowOrigin.ShiftEffects(f.Go, d);
            }
            if (_playerRoot != null) _playerRoot.position -= d;   // re-placed on the avatar next frame anyway
            // iteration 11 (spellfx): the free anchors of ground areas and place kits live at the scene root too
            foreach (var a in _areas) if (a.Anchor != null) a.Anchor.position -= d;
            foreach (var t in _places) if (t != null) t.position -= d;
        }

        void OnEnded() { ClearAll(); }
        void OnSettings() { if (!WowSettings.SpellEffects) ClearAll(); }

        WowFiles Files { get { return LabFiles ?? WowPlay.Files; } }

        SpellVisualDb Db
        {
            get
            {
                var f = Files;
                if (f == null) return null;
                if (_db != null && _dbFiles == f) return _db;
                if (_dbFailed && _dbFiles == f) return null;
                _dbFiles = f;
                try { _db = SpellVisualDb.Get(f); }
                catch (Exception e) { _db = null; WowLog.Warn("SpellVfx: SpellVisualDb failed: " + e.Message); }
                _dbFailed = _db == null || !_db.Ok;
                if (_db != null) WowLog.Info("SpellVfx: " + _db.Summary);
                if (_dbFailed) { _db = null; return null; }
                return _db;
            }
        }

        // ------------------------------------------------------------------ events

        /// <summary>WowPlay.Event listener (public so the render lab can feed events).</summary>
        public void OnGameEvent(GameEvent e)
        {
            switch (e.Type)
            {
                case GameEventType.CastStarted: CastStarted(e); break;
                case GameEventType.CastFailed: StopLoops(1, (long)e.SourceGuid); break;
                case GameEventType.CastHeld: HeldLocal(e); break;   // lead 2026-10-11: immersive casting
                case GameEventType.CastSucceeded: CastSucceeded(e); break;
                case GameEventType.ChannelStarted: Loop(e.SourceGuid, e.SpellId, 2, (long)e.SourceGuid, e.Amount, CasterHand(e.SourceGuid, e.SpellId)); break;
                case GameEventType.ChannelEnded: StopLoops(2, (long)e.SourceGuid); break;
                case GameEventType.AuraApplied: AuraApplied(e); break;
                case GameEventType.AuraRemoved: AuraRemoved(e); break;
                case GameEventType.LevelUp: LevelUp(e); break;
                case GameEventType.SpellKit: SpellKit(e); break;   // iteration 11 (spellfx): SMSG_PLAY_SPELL_VISUAL / _IMPACT
            }
        }

        /// <summary>Iteration 11 (spellfx): a scripted kit the server plays on a unit (Amount = SpellVisualKit id).</summary>
        void SpellKit(GameEvent e)
        {
            var db = Db;
            if (db == null || e.Amount <= 0) return;
            PlayKit(db.KitById(e.Amount), e.SourceGuid, 0, 0, 0f, WowCastHand.NotSaid);
        }

        void CastStarted(GameEvent e)
        {
            StopLoops(1, (long)e.SourceGuid);
            Loop(e.SourceGuid, e.SpellId, 1, (long)e.SourceGuid, e.Amount > 0 ? e.Amount + 1000 : 0, CasterHand(e.SourceGuid, e.SpellId));
        }

        void CastSucceeded(GameEvent e)
        {
            StopLoops(1, (long)e.SourceGuid);
            var db = Db;
            var s = db != null ? db.Spell(e.SpellId) : null;
            if (s == null || s.Visual == null) return;
            var v = s.Visual;
            WowCastHand hand = CasterHand(e.SourceGuid, e.SpellId);
            PlayKit(v.Cast, e.SourceGuid, 0, 0, 0f, hand);
            bool hasMissile = v.HasMissile && v.Missile != null && s.Speed > 0.01f;
            // iteration 11 (spellfx): a spell aimed at a place plays its InstantArea / ImpactArea kits (SpellVisual fields 23 / 24) there
            // (Mass Dispel's ring, Shadowfury's burst, Blizzard's spawn kit, the Rain of Fire tick's shower); a missile flies to the place first.
            Transform place = null;
            if (e.AtDest && WowSettings.SpellEffects)
            {
                place = PlaceAnchor(e.Pos);
                if (place != null)
                {
                    _placeKits++;
                    PlayKit(v.InstantArea, 0, 0, 0, 0f, WowCastHand.NotSaid, place);
                    bool flies = hasMissile && (e.Hits == null || e.Hits.Length == 0) && FireMissile(s, e.SourceGuid, 0, hand, place);
                    if (!flies) PlayKit(v.ImpactArea, 0, 0, 0, 0f, WowCastHand.NotSaid, place);
                }
            }
            // iteration 11 (spellfx): EVERY unit the spell hit (GameEvent.Hits; before: the first only - README Known gaps O2): a missile to each
            // (Multi-Shot, Volley) or the impact kit on each (Arcane Explosion, Thunder Clap, Blizzard ticks, Chain Lightning ...)
            if (e.Hits != null && e.Hits.Length > 0)
            {
                for (int i = 0; i < e.Hits.Length; i++)
                {
                    ulong h = e.Hits[i];
                    if (h == 0 || !ImpactAllowed(e.SpellId)) continue;
                    if (hasMissile && h != e.SourceGuid && FireMissile(s, e.SourceGuid, h, hand, null)) continue;
                    Impact(s, h);
                }
                return;
            }
            if (e.AtDest) return;                                // a place without units hit: the place kits only
            ulong target = e.TargetGuid != 0 ? e.TargetGuid : e.SourceGuid;
            bool missile = hasMissile && target != e.SourceGuid;
            if (missile && FireMissile(s, e.SourceGuid, target, hand, null)) return;
            Impact(s, target);
        }

        /// <summary>Iteration 11 (spellfx): at most ImpactCapPerSecond impacts of one spell id per second (ours, logged once when it cuts).</summary>
        bool ImpactAllowed(int spellId)
        {
            float now = Time.time;
            if (now >= _impactWindowAt) { _impactCount.Clear(); _impactWindowAt = now + 1f; }
            int n;
            _impactCount.TryGetValue(spellId, out n);
            if (n >= ImpactCapPerSecond)
            {
                if (!_impactCapLogged) { _impactCapLogged = true; WowLog.Info("SpellVfx: impacts of one spell are capped at " + ImpactCapPerSecond + " per second (ours; spell " + spellId + " reached it - logged once)"); }
                return false;
            }
            _impactCount[spellId] = n + 1;
            return true;
        }

        /// <summary>Iteration 11 (spellfx): a free anchor on the ground at a WoW place (scene root, upright; destroyed when its effects end).</summary>
        Transform PlaceAnchor(WowPos pos)
        {
            var s = WowPlay.Session;
            Vector3 p = WowSpace.ToUnity(s != null ? s.MapId : 0, pos.X, pos.Y, pos.Z);
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * GroundProbeUp, Vector3.down, out hit, GroundProbeUp + GroundProbeDown, GroundMask, QueryTriggerInteraction.Ignore)) p = hit.point;
            var cam = Cam();
            if (cam != null && (p - cam.transform.position).sqrMagnitude > SpawnDistance * SpawnDistance) { _skippedFar++; return null; }
            var t = new GameObject("vfx place").transform;
            t.position = p;
            _places.Add(t);
            _placeDieAt.Add(Time.time + 15f);
            return t;
        }

        void Impact(SpellVisualDb.SpellFx s, ulong target)
        {
            if (s == null || s.Visual == null) return;
            _impacts++;
            PlayKit(s.Visual.Impact, target, 0, 0, 0f, WowCastHand.NotSaid);
            if (s.Visual.TargetImpact != null && s.Visual.TargetImpact != s.Visual.Impact) PlayKit(s.Visual.TargetImpact, target, 0, 0, 0f, WowCastHand.NotSaid);
        }

        void Loop(ulong unit, int spellId, int kind, long key, int maxMs, WowCastHand hand)
        {
            var db = Db;
            var s = db != null ? db.Spell(spellId) : null;
            if (s == null || s.Visual == null) return;
            var kit = kind == 1 ? s.Visual.Precast : kind == 2 ? s.Visual.Channel : s.Visual.State;
            PlayKit(kit, unit, kind, key, maxMs > 0 ? maxMs / 1000f : 0f, hand);
        }

        void AuraApplied(GameEvent e)
        {
            long key = AuraKey(e.TargetGuid, e.SpellId);
            for (int i = 0; i < _fx.Count; i++) if (_fx[i].Kind == 3 && _fx[i].Key == key && _fx[i].DieAt < 0f) return;   // refresh
            _auraSpell = e.SpellId;
            try { Loop(e.TargetGuid, e.SpellId, 3, key, 0, WowCastHand.NotSaid); }
            finally { _auraSpell = 0; }
        }

        int _auraSpell;               // lead fix 2026-10-03: the aura whose state loop Spawn is creating (Fx.Spell)
        float _nextAuraCheck;
        int _auraOrphans;

        /// <summary>
        /// Lead fix (2026-10-03, the user: "resurrected players keep their death aura"): an aura's state loop ends with its AuraRemoved event -
        /// but a unit can leave our view with the aura (a ghost is invisible to the living, a far player) and come back without it, and no
        /// removal ever reaches us. Online every aura of a unit in view is in EntityState.Auras (OnlineSession FillAuras keeps every slot), so
        /// once a second (ours) a state loop of ANOTHER unit whose aura is no longer there - or whose unit is gone - is stopped. Offline lists are
        /// not complete for every unit: online only.
        /// </summary>
        void CheckAuraLoops(float now)
        {
            if (now < _nextAuraCheck) return;
            _nextAuraCheck = now + 1f;
            IWowSession s = WowPlay.Session;
            if (!(s is SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession) || s.Player == null) return;
            for (int i = 0; i < _fx.Count; i++)
            {
                var f = _fx[i];
                if (f.Kind != 3 || f.DieAt >= 0f || f.Spell == 0 || f.Unit == 0 || f.Unit == s.Player.Guid) continue;
                EntityState e = s.Find(f.Unit);
                bool has = false;
                if (e != null && e.Auras != null)
                    for (int k = 0; k < e.Auras.Count && !has; k++) if (e.Auras[k] != null && e.Auras[k].SpellId == f.Spell) has = true;
                if (has) continue;
                StopLoops(3, f.Key);
                if (_auraOrphans++ < 10)
                    WowLog.Info("SpellVfx: aura " + f.Spell + " visual on " + f.Unit.ToString("X") + " stopped - " + (e == null ? "the unit left our view" : "the aura is gone") + " without a removal reaching us (logged 10 times).");
            }
        }

        void AuraRemoved(GameEvent e)
        {
            bool had = StopLoops(3, AuraKey(e.TargetGuid, e.SpellId));
            if (!had) return;
            var db = Db;
            var s = db != null ? db.Spell(e.SpellId) : null;
            if (s != null && s.Visual != null) PlayKit(s.Visual.StateDone, e.TargetGuid, 0, 0, 0f, WowCastHand.NotSaid);
        }

        static long AuraKey(ulong unit, int spell) { return (long)(unit * 1000003UL) ^ spell ^ 0x5A5A0000L; }

        void LevelUp(GameEvent e)
        {
            var db = Db;
            if (db == null || !WowSettings.SpellEffects) return;
            var eff = db.EffectById(21);   // "HARDCODED Unit Level Up" -> Spells\LevelUp\LevelUp.m2 (measured)
            if (eff == null) return;
            ulong unit = e.SourceGuid != 0 ? e.SourceGuid : PlayerGuid;
            var anchor = Anchor(unit, (int)SpellVisualDb.Slot.Base);
            if (anchor == null) return;
            Spawn(eff, anchor, unit, 0, 0, 0f, true, 3.5f);
        }

        static ulong PlayerGuid
        {
            get
            {
                var s = WowPlay.Session;
                try { return s != null && s.Player != null ? s.Player.Guid : 0UL; }
                catch (Exception) { return 0UL; }
            }
        }

        // ------------------------------------------------------------------ stage O2 (Sync): Somnium avatars

        /// <summary>
        /// A remote Somnium avatar's own cast drawn from its Gear record (GearCast: offline, or a character the server does not show
        /// us): the precast (channel = false) or channel kit looped on the avatar's slots for at most maxMs (0 = until stopped).
        /// key = GearCast's record key (GearCast.KeyOf(slot), never a real guid); GearCast stops it when the server starts showing that cast.
        /// </summary>
        public void AvatarCast(ulong key, int spellId, bool channel, int maxMs)
        {
            if (key == 0 || spellId <= 0) return;
            StopLoops(channel ? 2 : 1, (long)key);
            Loop(key, spellId, channel ? 2 : 1, (long)key, maxMs, CasterHand(key, spellId));
        }

        /// <summary>
        /// Lead 2026-10-11 (the user: "loop the sound and effects if I have not released it, remember to sync over network too"): the
        /// precast of unit's cast (its effects and its sound) keeps going while the finished spell is held - its cast-time cap lifted,
        /// started again (no cap) when none runs. It ends as any precast: CastSucceeded / CastFailed, or the caller's stop (GearCast for a
        /// Somnium avatar's record, HeldCheck for ours).
        /// </summary>
        public void HoldCast(ulong unit, int spellId)
        {
            if (unit == 0 || spellId <= 0) return;
            bool any = false;
            for (int i = 0; i < _fx.Count; i++)
            {
                var f = _fx[i];
                if (f.Kind == 1 && f.Key == (long)unit && f.DieAt < 0f) { f.EndAt = float.MaxValue; any = true; }
            }
            if (!any) Loop(unit, spellId, 1, (long)unit, 0, CasterHand(unit, spellId));
            try { WowAudio.HoldSoundEntryLoop(1, (long)unit); }
            catch (Exception ex) { WowLog.Warn("SpellVfx: holding the sound loop failed: " + ex.Message); }
            if (_heldSaid++ < 6) WowLog.Info("SpellVfx: spell " + spellId + " held - its precast effects and sound loop until it is released (" + (any ? "kept" : "started again") + ", logged 6 times).");
        }

        ulong _heldLocal;
        float _heldAt;
        int _heldSaid;

        /// <summary>Our own CastHeld: the precast loops on (HoldCast) until the release / cancel.</summary>
        void HeldLocal(GameEvent e)
        {
            if (e.SourceGuid == 0) return;
            HoldCast(e.SourceGuid, e.SpellId);
            if (e.SourceGuid == PlayerGuid) { _heldLocal = e.SourceGuid; _heldAt = Time.unscaledTime; }
        }

        /// <summary>Once a frame: our held spell is gone without a cast event (put away, cancelled): its precast stops. A cast running
        /// after the release (online: the server's cast bar) keeps it - its own events end it. Ours: 0.3 s grace after the hold.</summary>
        void HeldCheck()
        {
            if (_heldLocal == 0 || Time.unscaledTime - _heldAt < 0.3f) return;
            var s = WowPlay.Session;
            PlayerState p = null;
            try { p = s != null ? s.Player : null; } catch (Exception) { p = null; }
            if (p != null && p.HeldSpellId != 0) return;
            if (p == null || p.CastingSpellId == 0) StopLoops(1, (long)_heldLocal);
            _heldLocal = 0;
        }

        /// <summary>Lead 2026-10-12 (the user: "can't see when a remote player levels up, please sync that"): WoW's level-up effect on a
        /// Somnium avatar (GearCast: their record's level went up; key = its record key, whose anchors follow that avatar).</summary>
        public void AvatarLevelUp(ulong key)
        {
            if (key == 0) return;
            LevelUp(new GameEvent { Type = GameEventType.LevelUp, SourceGuid = key });
        }

        /// <summary>Ends a record-driven precast and channel of that avatar (particles fade).</summary>
        public void AvatarCastStop(ulong key)
        {
            if (key == 0) return;
            StopLoops(1, (long)key);
            StopLoops(2, (long)key);
        }

        /// <summary>True while a precast (kind 1) or channel (kind 2) loop runs on that unit / key.</summary>
        public bool HasLoop(int kind, ulong key)
        {
            for (int i = 0; i < _fx.Count; i++) if (_fx[i].Kind == kind && _fx[i].Key == (long)key && _fx[i].DieAt < 0f) return true;
            return false;
        }

        /// <summary>
        /// A remote Somnium avatar released a spell (its record's release counter stepped): the precast ends and the spell's cast kit
        /// plays on the avatar (the caster's look). Its impact plays only when it landed on the caster itself (atSelf) - never a missile
        /// and never an impact on a monster, which is not the same monster here (offline every player has their own).
        /// </summary>
        public void AvatarRelease(ulong key, int spellId, bool atSelf)
        {
            if (key == 0 || spellId <= 0) return;
            StopLoops(1, (long)key);
            var db = Db;
            var s = db != null ? db.Spell(spellId) : null;
            if (s == null || s.Visual == null) return;
            PlayKit(s.Visual.Cast, key, 0, 0, 0f, CasterHand(key, spellId));
            if (atSelf) Impact(s, key);
        }

        /// <summary>The hand a Somnium avatar casts this spell from (NotSaid for everyone else: the kit's own slots).</summary>
        static WowCastHand CasterHand(ulong unit, int spellId)
        {
            if (unit == 0 || (LabAnchor != null && LabHand == null)) return WowCastHand.NotSaid;
            if (LabHand != null) return LabHand(unit, spellId);
            try { return unit == PlayerGuid ? WowGear.LocalCastHand(spellId) : WowGear.AvatarCastHand(unit, spellId); }
            catch (Exception) { return WowCastHand.NotSaid; }
        }

        /// <summary>Render-lab hook (null in the world): the cast hand of a lab unit.</summary>
        public static Func<ulong, int, WowCastHand> LabHand;

        /// <summary>
        /// Where a kit slot's effect goes for this cast hand: a single-hand cast (Right / Left) moves the other hand's (and other
        /// weapon's) effect onto the casting hand, or drops it (-1) when the casting hand has its own; every other slot unchanged.
        /// </summary>
        static int HandSlot(SpellVisualDb.Kit kit, int slot, WowCastHand hand)
        {
            if (hand != WowCastHand.Right && hand != WowCastHand.Left) return slot;
            bool right = hand == WowCastHand.Right;
            int mine = right ? (int)SpellVisualDb.Slot.RightHand : (int)SpellVisualDb.Slot.LeftHand;
            int other = right ? (int)SpellVisualDb.Slot.LeftHand : (int)SpellVisualDb.Slot.RightHand;
            int mineW = right ? (int)SpellVisualDb.Slot.RightWeapon : (int)SpellVisualDb.Slot.LeftWeapon;
            int otherW = right ? (int)SpellVisualDb.Slot.LeftWeapon : (int)SpellVisualDb.Slot.RightWeapon;
            if (slot == other) return kit.Slots[mine] != null ? -1 : mine;
            if (slot == otherW) return kit.Slots[mineW] != null ? -1 : mineW;
            return slot;
        }

        /// <summary>A hand / weapon slot moved to the casting hand of a single-hand cast (the missile's start); others unchanged.</summary>
        static int CastingHandSlot(int slot, WowCastHand hand)
        {
            if (hand != WowCastHand.Right && hand != WowCastHand.Left) return slot;
            bool right = hand == WowCastHand.Right;
            if (slot == (int)SpellVisualDb.Slot.LeftHand || slot == (int)SpellVisualDb.Slot.RightHand)
                return right ? (int)SpellVisualDb.Slot.RightHand : (int)SpellVisualDb.Slot.LeftHand;
            if (slot == (int)SpellVisualDb.Slot.LeftWeapon || slot == (int)SpellVisualDb.Slot.RightWeapon)
                return right ? (int)SpellVisualDb.Slot.RightWeapon : (int)SpellVisualDb.Slot.LeftWeapon;
            return slot;
        }

        // ------------------------------------------------------------------ kits

        /// <summary>A kit's effects on a unit's slots, or (iteration 11, spellfx) every slot on one fixed anchor - a place on the ground.</summary>
        void PlayKit(SpellVisualDb.Kit kit, ulong unit, int kind, long key, float loopMax, WowCastHand hand, Transform fixedAnchor = null)
        {
            if (kit == null) return;
            Transform soundAt = fixedAnchor;
            if (WowSettings.SpellEffects && Files != null)
            {
                for (int slot = 0; slot < kit.Slots.Length; slot++)
                {
                    var eff = kit.Slots[slot];
                    if (eff == null || string.IsNullOrEmpty(eff.ModelPath)) continue;
                    if (fixedAnchor != null) { Spawn(eff, fixedAnchor, unit, kind, key, loopMax, true, 0f); continue; }
                    int at = HandSlot(kit, slot, hand);
                    if (at < 0) continue;   // a single-hand cast: this hand's effect is already on the casting hand
                    var anchor = Anchor(unit, at);
                    if (anchor == null) continue;
                    if (soundAt == null) soundAt = anchor;
                    Spawn(eff, anchor, unit, kind, key, loopMax, slot == (int)SpellVisualDb.Slot.Base || slot == (int)SpellVisualDb.Slot.World, 0f);
                }
            }
            if (kit.SoundId > 0)
            {
                if (soundAt == null) soundAt = Anchor(unit, (int)SpellVisualDb.Slot.Chest);
                // lead 2026-10-03 (audio fix, the user: "some sounds don't loop properly or they play for too long"): a precast / channel / aura /
                // area kit's sound runs while the kit does - SoundEntries flag 0x200 files loop, others play once - and ends with StopLoops.
                try { if (soundAt != null) { if (kind != 0) WowAudio.PlaySoundEntryLoop(kit.SoundId, soundAt, kind, key, loopMax); else WowAudio.PlaySoundEntry(kit.SoundId, soundAt); } }
                catch (Exception ex) { WowLog.Warn("SpellVfx: sound " + kit.SoundId + " failed: " + ex.Message); }
            }
        }

        /// <summary>One effect model on an anchor. kind 0 = one-shot (its Stand length, 0.8..3 s, or oneShotSeconds).</summary>
        void Spawn(SpellVisualDb.Effect eff, Transform anchor, ulong unit, int kind, long key, float loopMax, bool upright, float oneShotSeconds)
        {
            if (_fx.Count >= MaxEffects) return;
            var cam = Cam();
            if (cam != null && (anchor.position - cam.transform.position).sqrMagnitude > SpawnDistance * SpawnDistance) { _skippedFar++; return; }
            GameObject go;
            try { go = M2Factory.Build(Files, eff.ModelPath); }
            catch (Exception ex) { WowLog.Warn("SpellVfx: " + eff.ModelPath + " failed: " + ex.Message); return; }
            if (go == null) return;
            go.name = "vfx " + eff.Name;
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            if (upright) go.transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
            float sc = eff.Scale > 0.01f ? eff.Scale : 1f;
            go.transform.localScale = go.transform.localScale * sc;
            var fx = new Fx { Go = go, Unit = unit, Key = key, Kind = kind, Spell = kind == 3 ? _auraSpell : 0 };
            if (kind == 0)
            {
                float len = oneShotSeconds;
                if (len <= 0f)
                {
                    var anim = go.GetComponent<M2Animator>();
                    len = anim != null ? Mathf.Clamp(anim.SequenceDuration / 1000f, 0.8f, 3f) : 1.5f;
                }
                fx.EndAt = Time.time + len;
            }
            else fx.EndAt = loopMax > 0f ? Time.time + loopMax : float.MaxValue;
            _fx.Add(fx);
            _spawned++;
            _logAt = Time.realtimeSinceStartup + 4f;
        }

        bool StopLoops(int kind, long key)
        {
            try { WowAudio.StopSoundEntryLoop(kind, key); }   // lead 2026-10-03 (audio fix)
            catch (Exception ex) { WowLog.Warn("SpellVfx: stopping sound loop failed: " + ex.Message); }
            bool any = false;
            for (int i = 0; i < _fx.Count; i++)
            {
                var f = _fx[i];
                if (f.Kind != kind || f.Key != key || f.DieAt >= 0f) continue;
                Stop(f);
                any = true;
            }
            return any;
        }

        /// <summary>Stops emitting, hides the meshes, destroys after the particles had time to fade.</summary>
        static void Stop(Fx f)
        {
            f.DieAt = Time.time + 1.2f;
            if (f.Go == null) return;
            foreach (var ps in f.Go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            foreach (var r in f.Go.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.enabled = false;
        }

        // ------------------------------------------------------------------ missiles

        /// <summary>A missile from the caster to a unit, or (iteration 11, spellfx: target 0) to a place anchor on the ground.</summary>
        bool FireMissile(SpellVisualDb.SpellFx s, ulong caster, ulong target, WowCastHand hand, Transform place = null)
        {
            if (!WowSettings.SpellEffects || Files == null || _missiles.Count >= MaxMissiles) return false;
            var v = s.Visual;
            int src = v.MissileAttachment >= 0 && v.MissileAttachment < 12 ? v.MissileAttachment : (int)SpellVisualDb.Slot.RightHand;
            src = CastingHandSlot(src, hand);   // a Somnium avatar's single-hand cast: from that hand
            int dst = v.MissileDestinationAttachment >= 0 && v.MissileDestinationAttachment < 12 ? v.MissileDestinationAttachment : (int)SpellVisualDb.Slot.Chest;
            var from = Anchor(caster, src);
            var to = target != 0 ? Anchor(target, dst) : place;
            if (from == null || to == null) return false;
            var cam = Cam();
            if (cam != null && (from.position - cam.transform.position).sqrMagnitude > SpawnDistance * SpawnDistance) { _skippedFar++; return false; }
            GameObject go;
            try { go = M2Factory.Build(Files, v.Missile.ModelPath); }
            catch (Exception ex) { WowLog.Warn("SpellVfx: missile " + v.Missile.ModelPath + " failed: " + ex.Message); return false; }
            if (go == null) return false;
            go.name = "vfx missile " + s.SpellId;
            go.transform.position = from.position;
            Vector3 dir = to.position - from.position;
            if (dir.sqrMagnitude > 1e-6f) go.transform.rotation = Quaternion.LookRotation(dir);   // WoW +X (model forward) = Unity +Z
            float sc = v.Missile.Scale > 0.01f ? v.Missile.Scale : 1f;
            go.transform.localScale = Vector3.one * sc;
            var m = new Missile { Go = go, Caster = caster, Target = target, Spell = s, DestSlot = dst, Speed = s.Speed * Yd, LastTarget = to.position, Deadline = Time.time + 12f, Place = target != 0 ? null : place };
            _missiles.Add(m);
            _missilesFired++;
            if (v.MissileSound > 0)
            {
                try { WowAudio.PlaySoundEntryLoop(v.MissileSound, go.transform, 5, go.GetInstanceID(), 12f); }   // lead 2026-10-03 (audio fix): ends at the impact
                catch (Exception ex) { WowLog.Warn("SpellVfx: missile sound failed: " + ex.Message); }
            }
            _logAt = Time.realtimeSinceStartup + 4f;
            return true;
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            // iteration 11 (spellfx): the ground circle / item cursor runs whenever a session does (uicore may also add it next to WowArmUi)
            if (LabFiles == null && WowPlay.Session != null) Ui.UiGroundTarget.Ensure();
            float now = Time.time, dt = Time.deltaTime;
            if (LabFiles == null) CheckAuraLoops(now);   // lead fix 2026-10-03
            HeldCheck();   // lead 2026-10-11
            for (int i = _missiles.Count - 1; i >= 0; i--)
            {
                var m = _missiles[i];
                if (m.Go == null) { _missiles.RemoveAt(i); continue; }
                var to = m.Target != 0 ? Anchor(m.Target, m.DestSlot) : m.Place;
                if (to != null) m.LastTarget = to.position;
                Vector3 p = m.Go.transform.position, d = m.LastTarget - p;
                float dist = d.magnitude, step = m.Speed * dt;
                if (dist <= step || now > m.Deadline)
                {
                    _missiles.RemoveAt(i);
                    try { WowAudio.StopSoundEntryLoop(5, m.Go.GetInstanceID()); } catch (Exception) { }   // lead 2026-10-03 (audio fix)
                    if (m.Target != 0) Impact(m.Spell, m.Target);
                    else if (m.Place != null && m.Spell.Visual != null) PlayKit(m.Spell.Visual.ImpactArea, 0, 0, 0, 0f, WowCastHand.NotSaid, m.Place);   // iteration 11 (spellfx): the place
                    var f = new Fx { Go = m.Go, Kind = 0 };
                    m.Go.transform.position = m.LastTarget;
                    Stop(f);
                    _fx.Add(f);
                    continue;
                }
                m.Go.transform.position = p + d * (step / dist);
                m.Go.transform.rotation = Quaternion.LookRotation(d);
            }

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                if (f.Go == null) { _fx.RemoveAt(i); continue; }
                if (f.DieAt >= 0f)
                {
                    if (now >= f.DieAt) { Destroy(f.Go); _fx.RemoveAt(i); }
                    continue;
                }
                if (now >= f.EndAt) Stop(f);
            }

            UpdateAreas(now);                                   // iteration 11 (spellfx): ground areas of every caster
            for (int i = _places.Count - 1; i >= 0; i--)
            {
                if (_places[i] != null && now < _placeDieAt[i]) continue;
                if (_places[i] != null) Destroy(_places[i].gameObject);
                _places.RemoveAt(i);
                _placeDieAt.RemoveAt(i);
            }

            if (_playerRoot != null && _bodyValid && (_fx.Count > 0 || _missiles.Count > 0)) UpdatePlayerAnchors();

            if (_logAt > 0f && Time.realtimeSinceStartup >= _logAt)
            {
                _logAt = -1f;
                WowLog.Info("SpellVfx: " + _spawned + " effects spawned, " + _missilesFired + " missiles, " + _impacts + " impacts, " + _skippedFar
                    + " skipped (far), live " + _fx.Count + " effects / " + _missiles.Count + " missiles");
            }
        }

        void ClearAll()
        {
            for (int i = 0; i < _fx.Count; i++) if (_fx[i].Go != null) Destroy(_fx[i].Go);
            for (int i = 0; i < _missiles.Count; i++) if (_missiles[i].Go != null) Destroy(_missiles[i].Go);
            _fx.Clear();
            _missiles.Clear();
            // iteration 11 (spellfx): ground areas and place anchors (an area still in AreaEffects is drawn again next frame when effects are on)
            for (int i = 0; i < _areas.Count; i++) if (_areas[i].Anchor != null) Destroy(_areas[i].Anchor.gameObject);
            for (int i = 0; i < _places.Count; i++) if (_places[i] != null) Destroy(_places[i].gameObject);
            _areas.Clear();
            _places.Clear();
            _placeDieAt.Clear();
        }

        // ------------------------------------------------------------------ iteration 11 (spellfx): ground areas (proposal-spellfx.md s1.2 / s6.5)

        /// <summary>
        /// Compares the session's AreaEffects (online: dynamic objects of every caster; offline: our areas) with the drawn set. A new type-1 area
        /// gets its spell's PersistentArea kit (SpellVisual field 25) looped at the place, at the effects' own SpellVisualEffectName.Scale (NOT the
        /// radius: DynamicObject.cpp:118-122 - the client overrides the radius of ground patch visuals; the models are authored at their spells'
        /// size, measured). A kit with CharProc 9 (the area model spawner) drops SpellVisualKitAreaModel model CharParamZero at a random point
        /// inside the radius every 1 / CharParamOne seconds (ours: param 1 read as models per second). Gone -> the loop fades. Type 2 (far sight): nothing.
        /// </summary>
        void UpdateAreas(float now)
        {
            var s = WowPlay.Session;
            IReadOnlyList<AreaEffectView> list = null;
            if (s != null && WowSettings.SpellEffects && Files != null && LabFiles == null)
            {
                try { list = s.AreaEffects; } catch (Exception) { list = null; }
            }
            for (int i = 0; i < _areas.Count; i++) _areas[i].Seen = false;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    if (v == null || v.Type != 1 || v.SpellId <= 0) continue;
                    // lead fix 2026-10-10 (the user: "sometimes spells get stuck casting visual, such as warlock 'Rain of Fire'"): a ground patch
                    // whose dynamic object outlived its spell's duration (+ AreaSlack; the destroy never reached us) is let go - once (expired set)
                    if (_expiredAreas.Contains(v.Guid)) continue;
                    if (v.Age > AreaMaxAge(v.SpellId)) { _expiredAreas.Add(v.Guid); continue; }
                    Area a = null;
                    for (int k = 0; k < _areas.Count; k++) if (_areas[k].Guid == v.Guid) { a = _areas[k]; break; }
                    if (a == null) a = NewArea(v, s.MapId);
                    if (a == null) continue;
                    a.Seen = true;
                    if (a.SpawnModel != null && a.Anchor != null && now >= a.SpawnNext)
                    {
                        a.SpawnNext = now + a.SpawnEvery;
                        SpawnAreaModel(a);
                    }
                }
            }
            for (int i = _areas.Count - 1; i >= 0; i--)
            {
                var a = _areas[i];
                if (a.Seen) continue;
                for (int k = 0; k < _fx.Count; k++)
                {
                    var f = _fx[k];
                    if (f.Kind == 4 && f.Key == a.Key && f.DieAt < 0f) { Stop(f); f.DieAt = now + AreaFade + 1f; }
                }
                try { WowAudio.StopSoundEntryLoop(4, a.Key); } catch (Exception) { }   // lead 2026-10-03 (audio fix)
                if (a.Anchor != null) Destroy(a.Anchor.gameObject, AreaFade + 1.2f);
                _areas.RemoveAt(i);
            }
        }

        const float AreaSlack = 2f;   // lead fix 2026-10-10 (ours)
        readonly HashSet<ulong> _expiredAreas = new HashSet<ulong>();

        float AreaMaxAge(int spellId)
        {
            try
            {
                var si = Files != null ? SomniumSpace.Worlds.SomniumWoW.Play.Spells.SpellDb.Get(Files).Find(spellId) : null;
                int d = si == null ? 0 : si.MaxDurationMs > 0 ? si.MaxDurationMs : si.DurationMs;
                return d > 0 ? d / 1000f + AreaSlack : float.MaxValue;
            }
            catch (Exception) { return float.MaxValue; }
        }

        Area NewArea(AreaEffectView v, int map)
        {
            var db = Db;
            var fx = db != null ? db.Spell(v.SpellId) : null;
            var kit = fx != null && fx.Visual != null ? fx.Visual.PersistentArea : null;
            var a = new Area { Guid = v.Guid, SpellId = v.SpellId, Key = (long)(v.Guid * 2654435761UL) ^ 0x3C3C0000L, Radius = Math.Max(0.5f, v.Radius) * Yd };
            _areas.Add(a);
            Vector3 p = WowSpace.ToUnity(map, v.Pos.X, v.Pos.Y, v.Pos.Z);
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * GroundProbeUp, Vector3.down, out hit, GroundProbeUp + GroundProbeDown, GroundMask, QueryTriggerInteraction.Ignore)) p = hit.point;
            a.Anchor = new GameObject("vfx area " + v.SpellId).transform;
            a.Anchor.position = p;
            _areasDrawn++;
            if (kit != null)
            {
                PlayKit(kit, 0, 4, a.Key, 0f, WowCastHand.NotSaid, a.Anchor);
                for (int c = 0; c < 4; c++)
                {
                    if (kit.CharProc[c] != 9) continue;
                    string model = db.AreaModel((int)kit.CharParam0[c]);
                    float rate = kit.CharParam1[c];
                    if (model != null && rate > 0.01f) { a.SpawnModel = model; a.SpawnEvery = 1f / rate; }
                    break;
                }
            }
            if (!_areaRulesLogged)
            {
                _areaRulesLogged = true;
                WowLog.Info("SpellVfx: ground areas draw their PersistentArea kit at the place (model scale, not the radius) and the CharProc 9 area models (ours: CharParamOne = models per second)");
            }
            if (_areaLogged.Count < 64 && _areaLogged.Add(v.SpellId))
                WowLog.Info("SpellVfx: ground area of spell " + v.SpellId + " - kit " + (kit != null ? kit.Id.ToString() : "none") + (a.SpawnModel != null ? ", area model " + a.SpawnModel + " every " + a.SpawnEvery.ToString("0.00") + " s" : "") +
                            ", radius " + v.Radius.ToString("0.#") + " yd (first per spell)");
            return a;
        }

        void SpawnAreaModel(Area a)
        {
            if (_fx.Count >= MaxEffects - 8) return;            // keep room for the units' kits
            var r = UnityEngine.Random.insideUnitCircle * a.Radius;
            Vector3 p = a.Anchor.position + new Vector3(r.x, 0f, r.y);
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * GroundProbeUp, Vector3.down, out hit, GroundProbeUp + GroundProbeDown, GroundMask, QueryTriggerInteraction.Ignore)) p = hit.point;
            int before = _fx.Count;
            Spawn(new SpellVisualDb.Effect { Id = 0, Name = "area model", ModelPath = a.SpawnModel, Scale = a.SpawnScale }, a.Anchor, 0, 0, 0, 0f, true, 0f);
            if (_fx.Count > before && _fx[_fx.Count - 1].Go != null) _fx[_fx.Count - 1].Go.transform.position = p;
        }

        static Camera Cam()
        {
            var c = Camera.main;
            return c != null && c.isActiveAndEnabled ? c : null;
        }

        // ------------------------------------------------------------------ anchors

        /// <summary>The transform an effect slot of a unit sits on (null when the unit has no view).</summary>
        Transform Anchor(ulong guid, int slot)
        {
            if (guid == 0) return null;
            int att = SpellVisualDb.SlotAttachment[slot], fb = SpellVisualDb.SlotFallback[slot];
            if (LabAnchor != null)
            {
                var t = LabAnchor(guid, att);
                if (t == null && fb >= 0) t = LabAnchor(guid, fb);
                if (t == null) t = LabAnchor(guid, -1);
                return t;
            }
            if (guid == PlayerGuid)
            {
                var mine = PlayerAnchor(slot);
                if (mine != null && WowGear.LocalLook.Kind != SomniumSpace.Worlds.SomniumWoW.Play.Rules.AvatarLookKind.Creature) SayAvatarSlot(slot, _body, "the local player");
                return mine;
            }
            // Stage O2 (Sync): a Somnium player's avatar (their online character, or a record-driven key): its avatar as drawn here,
            // or nothing while that avatar is not drawn here (never the server's copy of the character, which is not drawn either).
            Transform avatar;
            if (WowGear.TryAvatarSlot(guid, slot, out avatar))
            {
                if (_reanchorSaid.Count < 64 && _reanchorSaid.Add(guid))
                    WowLog.Info("SpellVfx: " + (GearCast.IsKey(guid) ? "a Somnium avatar's record-driven effects (key " + guid.ToString("X") + ")" : "guid " + guid + " is a Somnium player's character")
                        + " - effects anchored to their avatar as drawn here (" + (avatar != null ? "shown" : "not drawn here now: none") + ")");
                if (avatar != null) SayAvatarSlot(slot, WowGear.AvatarBody(guid), "a remote Somnium avatar");
                return avatar;
            }
            var a = WowEntityViews.GetAttachment(guid, att);
            if (a == null && fb >= 0) a = WowEntityViews.GetAttachment(guid, fb);
            if (a != null) return a;
            var root = WowEntityViews.GetView(guid);
            if (root == null) return null;
            if (slot == (int)SpellVisualDb.Slot.Base || slot == (int)SpellVisualDb.Slot.World) return root;
            // no attachment: a child at a fraction of the model height (chest 0.6, head 0.95)
            string n = "vfx_slot" + slot;
            var c = root.Find(n);
            if (c == null)
            {
                float h, fp;
                if (!WowEntityViews.TryGetSize(guid, out h, out fp) || h <= 0f) h = 1.8f;
                c = new GameObject(n).transform;
                c.SetParent(root, false);
                float k = slot == (int)SpellVisualDb.Slot.Head || slot == (int)SpellVisualDb.Slot.Breath ? 0.95f : 0.6f;
                c.position = root.position + Vector3.up * (h * k);
            }
            return c;
        }

        /// <summary>The local player's slot anchors, following the Somnium avatar (VR controllers / desktop humanoid bones).</summary>
        Transform PlayerAnchor(int slot)
        {
            if (_bodyFrame != Time.frameCount)
            {
                _bodyFrame = Time.frameCount;
                if (_body == null) _body = new GearBody { Label = "spell effects" };
                try { _body.Player = SomniumBridge.PlayersContainer != null ? SomniumBridge.PlayersContainer.LocalPlayer : null; }
                catch (Exception) { _body.Player = null; }
                _bodyValid = _body.Update();
                if (_bodyValid && _playerRoot == null)
                {
                    _playerRoot = new GameObject("WoW Spell Vfx player anchors").transform;
                    for (int i = 0; i < _playerSlots.Length; i++)
                    {
                        _playerSlots[i] = new GameObject("slot" + i).transform;
                        _playerSlots[i].SetParent(_playerRoot, false);
                    }
                }
                if (_bodyValid) PlaceAnchors(_body);
            }
            return _bodyValid && _playerRoot != null ? _playerSlots[slot] : null;
        }

        /// <summary>
        /// Iteration 11 (combat feel, the user: "make hand effects follow the hands when using combat animations"). This LateUpdate runs
        /// at order 0 - before the client's IK (VRIK 9998, FBBIK 9999) and before WowGear (10000), whose GearLocal.Place poses the desktop
        /// avatar (GearPoser) and then places the weapons - so the anchors above sit at the hands as the client animated them, not as they
        /// are drawn. WowGear calls this right after GearLocal.Place (and in VR from onBeforeRender, after the held weapons): the local
        /// player's slot anchors (and every effect parented to them) move to the posed body. No allocation: the posed GearBody is
        /// GearLocal's own (already updated), HandPose reads the bones. Nothing while no effect or missile is live.
        /// </summary>
        public static void PlaceLocalAnchors(GearBody posed)
        {
            WowSpellVfx v = Instance;
            if (v == null || posed == null || !posed.Valid || v._playerRoot == null || !v._bodyValid || LabAnchor != null) return;
            if (v._fx.Count == 0 && v._missiles.Count == 0) return;
            v.PlaceAnchors(posed);
            if (!v._latePlaceSaid) { v._latePlaceSaid = true; WowLog.Info("SpellVfx: the local player's hand / weapon effects follow the posed avatar (placed again after the combat animation, every frame)"); }
        }

        bool _latePlaceSaid;

        void UpdatePlayerAnchors()
        {
            if (_bodyFrame == Time.frameCount) return;
            PlayerAnchor(0);
        }

        void PlaceAnchors(GearBody b)
        {
            // iteration 11 (avatar hook): while the avatar is drawn as a creature form (bear, cat, polymorph ...) the slots follow the form's model
            Vector3 fp; Quaternion fr;
            if (WowGear.LocalLook.Kind == SomniumSpace.Worlds.SomniumWoW.Play.Rules.AvatarLookKind.Creature)
            {
                bool any = false;
                for (int i = 0; i < _playerSlots.Length; i++) if (WowGear.TryLocalFormSlot(i, out fp, out fr)) { _playerSlots[i].SetPositionAndRotation(fp, fr); any = true; }
                if (any) return;
            }
            // vfxanchor fix 2026-10-04: the same slot rule as every remote avatar (GearSpellSlots: root-yaw head / feet / above-head
            // points at the top of the head, the chest's front; hands at the tracked / posed hands as before).
            GearSpellSlots.Place(b, Vector3.zero, _playerSlots);
        }

        /// <summary>vfxanchor fix 2026-10-04: one line per slot, the first time an effect goes on a Somnium avatar's slot (ours).</summary>
        static void SayAvatarSlot(int slot, GearBody b, string whose)
        {
            if (slot < 0 || slot >= _avatarSlotSaid.Length || _avatarSlotSaid[slot]) return;
            _avatarSlotSaid[slot] = true;
            WowLog.Info("SpellVfx: " + GearSpellSlots.Describe(slot, b) + " (" + whose + "; logged once per slot)");
        }

        static readonly bool[] _avatarSlotSaid = new bool[12];
    }
}
