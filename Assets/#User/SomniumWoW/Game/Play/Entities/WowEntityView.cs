using System;
using System.Collections.Generic;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>
    /// One session entity drawn in the world: a root at the entity's (smoothed) position and facing, the WoW model under it
    /// (built later, over frames, by WowEntityViews), NPC weapons on the hand attachments, the click box, the nameplate,
    /// the quest marker and the loot sparkle. Animation follows the EntityState every frame (see Animate).
    /// </summary>
    internal sealed partial class WowEntityView
    {
        // Iteration 11 (w0game, wave 0): partial-method hooks other owners implement in their own new partial file of this class
        // (e.g. Game/Play/Entities/WowEntityView.Trainer.cs) as `partial void Name(same parameters) { ... }` - once each. Until then the
        // compiler removes the calls (inert).
        /// <summary>trainer U11 (a): a game object's one-shot custom animation (EntityState.Emote on EmoteCounter, AnimationData 153 + n)
        /// and its GameObjectDisplayInfo sound; first thing of AnimateObject every frame.</summary>
        partial void TrGoAnimate(EntityState s, float now);

        // AnimationData.dbc ids (Models report, measured).
        private const int AnimStand = 0, AnimDeath = 1, AnimWalk = 4, AnimRun = 5, AnimStandWound = 8, AnimCombatWound = 9,
            AnimAttackUnarmed = 16, AnimAttack1H = 17, AnimAttack2H = 18, AnimReadyUnarmed = 25, AnimReady1H = 26, AnimReady2H = 27,
            AnimSpellCast = 32, AnimReadySpellDirected = 51, AnimReadySpellOmni = 52, AnimSpellCastDirected = 53, AnimSpellCastOmni = 54;

        private static readonly int[] AttackUnarmedList = { AnimAttackUnarmed, AnimAttack1H };
        private static readonly int[] Attack1HList = { AnimAttack1H, AnimAttackUnarmed };
        private static readonly int[] Attack2HList = { AnimAttack2H, AnimAttack1H, AnimAttackUnarmed };
        private static readonly int[] ReadyUnarmedList = { AnimReadyUnarmed };
        private static readonly int[] Ready1HList = { AnimReady1H, AnimReadyUnarmed };
        private static readonly int[] Ready2HList = { AnimReady2H, AnimReady1H, AnimReadyUnarmed };
        private static readonly int[] WoundList = { AnimCombatWound, AnimStandWound };
        private static readonly int[] CastingList = { AnimReadySpellOmni, AnimReadySpellDirected };
        private static readonly int[] CastDoneList = { AnimSpellCastOmni, AnimSpellCastDirected, AnimSpellCast };

        public readonly ulong Guid;
        public EntityState State;
        public readonly GameObject Root;
        public readonly WowEntityClick Click;
        public WowNameplate Plate;
        /// <summary>Lead fix 2026-10-14: the built model draws nothing (Creature\InvisibleStalker: 0 batches) - checked once per model.</summary>
        public bool Invisible;
        public GameObject InvisibleFor;          // created on first need (WowEntityViews creates a few per frame once the art is ready)
        public WowSparkle Sparkle;          // created on first need

        public GameObject Model;
        public M2Animator Anim;
        public float DisplayScale = 1f;
        public int BuiltDisplayId;
        public bool ModelTried;
        private float _appliedScale = -1f;

        // Iteration 6 (online): what is drawn for this entity.
        public enum Look { Creature, Character, GameObject, Somnium }
        /// <summary>What the model was built as (valid once ModelTried).</summary>
        public Look BuiltLook;
        /// <summary>Main-thread time the art for the current look was first asked for (a model is built anyway 10 s later).</summary>
        public float ArtAskedAt = -1f;
        private WowCharJob _charJob;
        private int _lookHash;                                   // the character look the model was built from (rebuild on change)
        private readonly GameObject[] _gear = new GameObject[3]; // a player's main hand, off hand, ranged (EquipSlot 15, 16, 17)
        private readonly WowWeaponInfo[] _gearInfo = new WowWeaponInfo[3];
        private readonly int[] _gearIds = new int[3];
        private readonly float[] _gearLength = new float[3];      // metres: longest side of the built item (short wands are not shown sheathed)
        private int _placedSheath = -1;

        /// <summary>Parked (left visibility): a prepared-but-unbuilt character look (a 1 MB composed texture) is let go.</summary>
        public void ReleaseArt() { if (!ModelTried) { _charJob = null; ArtAskedAt = -1f; } }
        private Renderer _visRenderer;
        private bool _modelHidden;

        public GameObject MarkerGo;
        public QuestMarker MarkerKind;

        public Vector3 Pos;
        public float Yaw;
        public bool Placed;
        public bool Parked;
        public float ParkedAt;
        public int Seen;
        public bool Hovered;

        /// <summary>While true (the player's dialog with this NPC is open) the view turns toward FacePoint instead of the sim facing.</summary>
        public bool Facing;
        public Vector3 FacePoint;

        /// <summary>Where Top came from: "standing pose" (skinned Stand vertices), "bounds" (mesh bounds) or "default".</summary>
        public string TopSource = "default";
        private float _standTop = -1f;          // metres at scale 1 (-1 = not measured)
        private string _petName, _petNameFrom, _petNameOwner;
        private float _groundY;
        private bool _haveGround;
        private Vector3 _groundFor = new Vector3(float.NaN, 0f, 0f);

        /// <summary>This display's creature sounds (set by WowEntityViews once decoded; null = silent).</summary>
        public WowCreatureSounds Sounds;
        private AudioSource _audio;
        private bool _wasInCombat;
        private float _lastWoundSound = -10f;
        // Stage O2 (integrator, combat request): a WoundCounter step with no combat event (online, other players' melee fights) still
        // plays the victim's wound sound a moment later; when an event already played it, PlayWound's 0.35 s guard skips this one.
        private float _woundSoundDue = -1f;
        private const float WoundSoundDelay = 0.1f;   // ours: lets the event's PlayWound (crit variant) come first

        /// <summary>Plays one variation of a sound set from the entity (3D, logarithmic roll-off between SoundEntries' distances).</summary>
        public void PlaySound(WowSoundSet set)
        {
            if (set == null || Root == null || !Root.activeInHierarchy) return;
            var clip = set.Pick();
            if (clip == null) return;
            if (_audio == null)
            {
                _audio = Root.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.rolloffMode = AudioRolloffMode.Logarithmic;
                _audio.dopplerLevel = 0f;
            }
            _audio.minDistance = set.MinDistance * WowSpace.MetresPerYard;
            _audio.maxDistance = Mathf.Max(_audio.minDistance + 1f, set.MaxDistance * WowSpace.MetresPerYard);
            _audio.PlayOneShot(clip, set.Volume * WowSettings.MasterVolume * WowSettings.EffectsVolume);
        }

        /// <summary>The hit sound for a landed hit (critical wound when the event says crit), at most every 0.35 s.</summary>
        public void PlayWound(bool crit, float now)
        {
            if (Sounds == null || now - _lastWoundSound < 0.35f) return;
            _lastWoundSound = now;
            PlaySound(crit && Sounds.WoundCrit != null ? Sounds.WoundCrit : Sounds.Wound);
        }

        /// <summary>Model top above the feet and footprint diameter, metres (defaults until the model is built).</summary>
        public float Top = 1.9f, Footprint = 1f;
        private Vector3 _boxCenter = new Vector3(0f, 0.9f, 0f), _boxSize = new Vector3(0.9f, 1.8f, 0.9f);

        private WowWeaponInfo _mainHand, _offHand;

        // animation state
        private int _lastAttack, _lastWound, _lastEmote;
        private bool _wasCasting, _deadShown, _holding;
        private float _oneShotUntil;
        private bool _oneShotIsAttack;
        private int _base = -1;
        private int _idleEmote;
        private float _deathT, _deathDur;
        private float _speedYd;
        private WowPos _lastSimPos;
        private bool _haveSimPos;

        public WowEntityView(EntityState s, Transform parent, WowEntityViews owner, WowEntityArt art)
        {
            Guid = s.Guid;
            State = s;
            Root = new GameObject("WoW " + (s.Name ?? "entity") + " " + s.Guid.ToString("X"));
            Root.transform.SetParent(parent, false);
            var hit = new GameObject("Click box");
            hit.transform.SetParent(Root.transform, false);
            Click = WowEntityClick.Create(hit, s.Guid, owner, _boxCenter, _boxSize);
            _lastAttack = s.AttackCounter;
            _lastWound = s.WoundCounter;
            _lastEmote = s.EmoteCounter - (s.Emote != 0 ? 1 : 0);   // the spawn bump plays the NPC's emote once the model exists
        }

        // ------------------------------------------------------------------ model

        /// <summary>What this entity should be drawn as right now (a player in a creature form - bear, sheep - is drawn as that creature
        /// once the art worker has resolved its display).</summary>
        public Look WantedLook(WowEntityArt art)
        {
            var s = State;
            // Iteration 11 (pets H7c): the vehicle WowMount draws as our "mount", a unit hidden in a seat, or a vehicle another Somnium
            // player drives (GearRemote draws it) - no model of ours. Inert until vehicle / motion fill DrawnVehicle / HiddenInSeat / VehicleId.
            if (s.Kind != EntityKind.GameObject && ((WowVehicle.DrawnVehicle != 0 && s.Guid == WowVehicle.DrawnVehicle) || s.HiddenInSeat
                || (s.CharmedByGuid != 0 && s.VehicleId != 0 && !s.DisableMove && WowGear.IsSomniumCharacter(s.CharmedByGuid)))) return Look.Somnium;   // integrator (vehicle request): a turret another Somnium player mans stays drawn
            if (s.Kind == EntityKind.GameObject) return Look.GameObject;
            if (s.Kind != EntityKind.Player) return Look.Creature;
            if (s.IsSomniumPlayer || WowGear.IsSomniumCharacter(s.Guid, s.Name)) return Look.Somnium;
            if (s.Race <= 0) return Look.Creature;
            if (s.DisplayId <= 0 || art == null) return Look.Character;
            bool known;
            bool character = art.IsCharacterDisplay(s.DisplayId, out known);
            return !known || character ? Look.Character : Look.Creature;
        }

        /// <summary>A hash of a player's look (race, gender, skin, face, hair, facial hair, visible items): a change rebuilds the model.</summary>
        public static int LookHash(EntityState s)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + s.Race; h = h * 31 + s.Gender; h = h * 31 + s.Skin; h = h * 31 + s.Face;
                h = h * 31 + s.HairStyle; h = h * 31 + s.HairColor; h = h * 31 + s.FacialHair;
                var items = s.ItemDisplayIds;
                if (items != null) for (int i = 0; i < items.Length; i++) h = h * 31 + items[i];
                return h;
            }
        }

        /// <summary>True when the model built for this entity no longer matches it (display, look, Somnium flag): rebuild.</summary>
        public bool Stale(WowEntityArt art)
        {
            if (!ModelTried) return false;
            if (art != null && State.Kind == EntityKind.Player && State.DisplayId > 0 && State.DisplayId != BuiltDisplayId)
            {
                // A player's display changed (a shapeshift, a polymorph, back again): have the worker say whether it is a race model.
                bool known;
                art.IsCharacterDisplay(State.DisplayId, out known);
                if (!known) art.WantDisplay(State.DisplayId, 0f);
            }
            var want = WantedLook(art);
            if (want != BuiltLook) return true;
            if (want == Look.Character) return LookHash(State) != _lookHash;
            if (want == Look.Somnium) return false;
            return BuiltDisplayId != State.DisplayId;
        }

        /// <summary>
        /// Asks the art worker for what this entity needs (nearest first by priority = distance squared) and says whether the model
        /// can be built now without reading files on the main thread. Main thread; call while !ModelTried.
        /// </summary>
        public bool ArtReady(WowEntityArt art, float priority, float now)
        {
            if (art == null) return true;
            if (ArtAskedAt < 0f) ArtAskedAt = now;
            var s = State;
            switch (WantedLook(art))
            {
                case Look.Somnium:
                    return true;
                case Look.GameObject:
                    return art.WantGameObject(s.DisplayId, priority);
                case Look.Character:
                {
                    bool ok = true;
                    if (s.DisplayId > 0) { bool known; art.IsCharacterDisplay(s.DisplayId, out known); if (!known) { art.WantDisplay(s.DisplayId, priority); return false; } }
                    var items = s.ItemDisplayIds;
                    if (items != null)
                        for (int k = 15; k <= 17 && k < items.Length; k++) if (items[k] > 0 && !art.WantWeapon(items[k], priority)) ok = false;
                    int hash = LookHash(s);
                    if (_charJob == null || _lookHash != hash)
                    {
                        _lookHash = hash;
                        _charJob = art.WantCharacter(new CharacterLook
                        {
                            Race = s.Race, Gender = s.Gender, Skin = s.Skin, Face = s.Face, HairStyle = s.HairStyle, HairColor = s.HairColor,
                            FacialHair = s.FacialHair, ItemDisplayIds = s.ItemDisplayIds
                        }, priority);
                    }
                    else if (!_charJob.Done && Mathf.Abs(_charJob.Priority - priority) > priority * 0.5f + 25f) art.Reprioritise(_charJob, priority);
                    return ok && art.ReadyToBuild(_charJob);
                }
                default:
                {
                    bool ok = art.WantDisplay(s.DisplayId, priority);
                    if (s.MainHandDisplayId > 0 && !art.WantWeapon(s.MainHandDisplayId, priority)) ok = false;
                    if (s.OffHandDisplayId > 0 && !art.WantWeapon(s.OffHandDisplayId, priority)) ok = false;
                    return ok;
                }
            }
        }

        /// <summary>Builds what WantedLook says (creature / player character / gameobject / nothing for a Somnium player's character)
        /// and sizes the click box / plate height from the model. Main thread.</summary>
        public void BuildModel(WowFiles files, WowEntityArt art)
        {
            var look = WantedLook(art);
            BuiltLook = look;
            ArtAskedAt = -1f;
            if (look == Look.Somnium) { ModelTried = true; BuiltDisplayId = State.DisplayId; _lookHash = LookHash(State); return; }
            if (look == Look.Character) { BuildCharacter(files, art); return; }
            if (look == Look.GameObject) { BuildGameObject(files, art); return; }
            ModelTried = true;
            BuiltDisplayId = State.DisplayId;
            if (files == null || State.DisplayId <= 0) return;
            GameObject go = null;
            try { go = WowCreatures.Build(files, State.DisplayId); }
            catch (Exception e) { WowLog.Warn("Entities: " + State.Name + " display " + State.DisplayId + " threw: " + e.Message); }
            if (go == null) { WowLog.Warn("Entities: no model for " + State.Name + " (display " + State.DisplayId + ") - shown as nameplate + click box only"); return; }
            LogGear(State.Name, State.DisplayId);
            Model = go;
            Anim = go.GetComponent<M2Animator>();
            DisplayScale = go.transform.localScale.x > 0f ? go.transform.localScale.x : 1f;
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _appliedScale = -1f;
            ApplyScale();

            // Integrator (iteration 3): measure the body BEFORE the held items are attached - both measurements take the first
            // SkinnedMeshRenderer under the model, which was the held weapon for armed NPCs (lab r1: Corwin Hale's head top at
            // 0.26 m, his name plate inside his chest).
            _standTop = StandTop(files, BuiltDisplayId, Model, Anim);
            MeasureBounds();
            if (Anim != null)
            {
                if (State.MainHandDisplayId > 0) _mainHand = Hold(files, art, State.MainHandDisplayId, false);
                if (State.OffHandDisplayId > 0) _offHand = Hold(files, art, State.OffHandDisplayId, true);
            }
            AfterBuild();
        }

        // NPC gear fix (iteration 11): one log line per humanoid NPC display (its helm / shoulders / cape / geosets), at most 200.
        private static readonly System.Collections.Generic.HashSet<int> GearLogged = new System.Collections.Generic.HashSet<int>();

        private static void LogGear(string name, int displayId)
        {
            if (GearLogged.Count >= 200 || GearLogged.Contains(displayId)) return;
            var plan = NpcGear.Cached(displayId);
            if (plan == null || plan.Summary == null) return;
            GearLogged.Add(displayId);
            WowLog.Info("Entities: " + name + " display " + displayId + ": " + plan.Summary);
        }

        /// <summary>Common tail of every build: the animation state restarts, the visibility renderer is found.</summary>
        private void AfterBuild()
        {
            _lastAttack = State.AttackCounter;   // swings/hits that happened before the model existed are not replayed
            _lastWound = State.WoundCounter;
            _base = -1;
            _deadShown = false;
            _holding = false;
            _oneShotUntil = 0f;
            _modelHidden = false;
            _goState = -1;
            _emoteStateSeen = -1;
            _visRenderer = null;
            if (Model == null) return;
            var smr = BodyRenderer(Model);
            _visRenderer = smr != null ? (Renderer)smr : Model.GetComponentInChildren<Renderer>();
        }

        // ------------------------------------------------------------------ player characters (iteration 6)

        /// <summary>
        /// Another WoW player: the body from the worker's composed look (WowCharacter.BuildBody - skin, face, hair, armour textures and
        /// geosets), the weapons of EquipSlot 15/16/17 built separately and placed by the server's sheath state (UpdateGear).
        /// </summary>
        private void BuildCharacter(WowFiles files, WowEntityArt art)
        {
            ModelTried = true;
            BuiltDisplayId = State.DisplayId;
            var job = _charJob;
            _charJob = null;              // the composed texture (1 MB) is not kept
            if (files == null) return;
            if (job == null || !job.Done)
            {
                // Waited too long for the worker (or no job): compose here, once.
                _lookHash = LookHash(State);
                job = new WowCharJob();
            }
            GameObject go = null;
            try
            {
                if (job.Res != null && job.Model != null) go = WowCharacter.BuildBody(files, job.Res, null, null, job.Name, job.Model);
                else if (job.Error == null)
                {
                    go = WowCharacter.Build(files, new CharacterLook
                    {
                        Race = State.Race, Gender = State.Gender, Skin = State.Skin, Face = State.Face, HairStyle = State.HairStyle,
                        HairColor = State.HairColor, FacialHair = State.FacialHair, ItemDisplayIds = WithoutWeapons(State.ItemDisplayIds)
                    });
                }
            }
            catch (Exception e) { WowLog.Warn("Entities: player " + State.Name + " (race " + State.Race + ") threw: " + e.Message); }
            if (go == null)
            {
                WowLog.Warn("Entities: no character model for player " + State.Name + " (race " + State.Race + " gender " + State.Gender + (job.Error != null ? ": " + job.Error : "") + ") - click box and name only");
                return;
            }
            Model = go;
            Anim = go.GetComponent<M2Animator>();
            DisplayScale = 1f;
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _appliedScale = -1f;
            ApplyScale();
            _standTop = StandTop(files, -State.Race * 2 - State.Gender - 1, Model, Anim);   // cached per race/gender (negative keys: not display ids)
            MeasureBounds();
            _placedSheath = -1;
            for (int k = 0; k < 3; k++) { _gear[k] = null; _gearInfo[k] = null; _gearIds[k] = 0; _gearVisual[k] = 0; }
            _stealthShown = false;   // iteration 11 (spellfx hook 6): a new model gets the look again
            if (Anim != null) BuildGear(files, art);
            AfterBuild();
        }

        private static int[] WithoutWeapons(int[] items)
        {
            if (items == null) return null;
            var c = (int[])items.Clone();
            for (int k = 15; k <= 17 && k < c.Length; k++) c[k] = 0;
            return c;
        }

        /// <summary>Builds the player's main hand / off hand / ranged item models (parked under the model; UpdateGear places them).</summary>
        private void BuildGear(WowFiles files, WowEntityArt art)
        {
            var items = State.ItemDisplayIds;
            if (items == null) return;
            for (int k = 0; k < 3; k++)
            {
                int slot = 15 + k;
                int id = slot < items.Length ? items[slot] : 0;
                if (id <= 0) continue;
                var w = art != null ? art.Weapon(id) : null;
                if (w != null && w.Error != null) continue;
                bool left = k == 1 || (w != null && w.Bow);
                GameObject item = null;
                try { item = WowItems.Build(files, id, left); }
                catch (Exception e) { WowLog.Warn("Entities: item " + id + " of " + State.Name + " threw: " + e.Message); }
                if (item == null) continue;
                item.transform.SetParent(Model.transform, false);
                _gear[k] = item;
                _gearInfo[k] = w;
                _gearIds[k] = id;
                // Iteration 11 (spellfx hook 6): the weapon enchant glow (inert until spellfx fills ItemVisualDb / WowEnchantVisuals).
                _gearVisual[k] = GearVisual(files, k);
                WowEnchantVisuals.Apply(files, item, _gearVisual[k]);
                float len = 0f;
                foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                {
                    var b = r.bounds;
                    len = Mathf.Max(len, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                }
                _gearLength[k] = len;
            }
        }

        /// <summary>
        /// Puts a player's weapons where the server's sheath state says (UNIT_FIELD_BYTES_2 byte 0 via EntityState.Sheath): Melee - main
        /// hand in the right palm (attachment 1), off hand in the left palm (2) or a shield on the left wrist (0); Ranged - a bow in the
        /// left palm, a gun / crossbow / wand / thrown weapon in the right; everything else hangs on the M2 sheath attachments by Item.dbc
        /// SheatheType, attached with the attachment's own (Blizzard-authored) frame and identity local rotation. MEASURED (harness
        /// SheathFacts / SheathCensus, Stand pose, WoW axes x fwd / y left / z up): a weapon item model runs along its +X (blade, shaft,
        /// barrel; Sword_2H_Claymore_C_01 X -0.38..1.66 = pommel..tip, Stave_2H_Long_A_02 -1.01..1.56, Bow_1H_Short_A_01 -0.88..0.89,
        /// Firearm_2H_Shotgun_B_01 -0.31..0.79; under 0.3 across). The sheath frames' +X, alike on all 20 player models: 26 down to the
        /// left, 27 down to the right, 30 up to the left, 31 up to the right (59 degrees from horizontal on HumanMale), 32/33 down and back
        /// at the left/right hip; 28 +Y (a shield's outer face, as on the wrist attachment 0) backward. So TrinityCore's Item.dbc "1
        /// two-handed: diagonally across the back pointing downwards" is 26/27 and "2 staff: ... pointing upwards" is 30/31; the main hand
        /// takes 27 / 30 / 32 and the off hand crosses to 26 / 31 / 33 (WoW Model Viewer 0.7 database.cpp + charcontrol.cpp), as the client
        /// shows (wowhead screenshot of the Sunfury Battlemage's Stave from behind: head over the LEFT shoulder, foot at the right thigh,
        /// ~60 degrees = 30). Iteration 6 took the frames' +Z for the blade and swapped 1 and 2 (a staff hung from the right shoulder,
        /// pointing down). So: 1 -> 27 (titan-grip off hand 26), 2 -> 30 (off hand 31), 3/5/6 -> 32 (off hand 33), 7 -> 33, 4 -> 28;
        /// bows, guns, crossbows -> 31 (our rule, no source: up to the right, crossing a staff or a two-hander); thrown weapons, relics,
        /// held-in-off-hand items and short (&lt; 0.55 m) wands are not shown sheathed.
        /// </summary>
        public void UpdateGear()
        {
            if (BuiltLook != Look.Character || Anim == null) return;
            int sheath = (int)State.Sheath;
            if (sheath == _placedSheath) return;
            _placedSheath = sheath;
            for (int k = 0; k < 3; k++)
            {
                var item = _gear[k];
                if (item == null) continue;
                var w = _gearInfo[k];
                int attachment = -1;
                bool inHand = false;
                if (sheath == (int)SheathState.Melee && k < 2)
                {
                    inHand = true;
                    attachment = k == 0 ? 1 : (w != null && w.Shield ? 0 : 2);
                }
                else if (sheath == (int)SheathState.Ranged && k == 2)
                {
                    inHand = true;
                    attachment = w != null && w.Bow ? 2 : 1;
                }
                else attachment = SheathAttachment(k, w, _gearLength[k]);
                Transform point = attachment >= 0 ? FirstAttachment(attachment) : null;
                if (point == null) { item.SetActive(false); continue; }
                item.SetActive(true);
                item.transform.SetParent(point, false);
                item.transform.localPosition = Vector3.zero;
                item.transform.localRotation = Quaternion.identity;
                item.transform.localScale = Vector3.one;
                if (inHand && !(w != null && w.Shield)) Anim.CloseHand(attachment == 1, true);
            }
            // Hands with nothing in them open again.
            bool right = false, left = false;
            for (int k = 0; k < 3; k++)
            {
                if (_gear[k] == null || !_gear[k].activeSelf || _gear[k].transform.parent == null) continue;
                var p = _gear[k].transform.parent;
                if (p == Anim.Attachment(1)) right = true;
                if (p == Anim.Attachment(2)) left = true;
            }
            if (!right) Anim.CloseHand(true, false);
            if (!left) Anim.CloseHand(false, false);
            _mainHand = sheath == (int)SheathState.Melee ? _gearInfo[0] : null;
            _offHand = sheath == (int)SheathState.Melee ? _gearInfo[1] : null;
        }

        /// <summary>The attachment (with fallbacks when a model lacks it), or null.</summary>
        private Transform FirstAttachment(int id)
        {
            var t = Anim.Attachment(id);
            if (t != null) return t;
            if (id == 28) return Anim.Attachment(27) ?? Anim.Attachment(26);   // no shield sheath: a back sheath
            if (id == 26) return Anim.Attachment(27);                          // the mirror partner (all 20 player models have 26-33)
            if (id == 27) return Anim.Attachment(26);
            if (id == 30) return Anim.Attachment(31);
            if (id == 31) return Anim.Attachment(30);
            if (id == 33) return Anim.Attachment(32);
            return null;
        }

        /// <summary>The sheath attachment for slot index k (0 main hand, 1 off hand, 2 ranged), -1 = not shown sheathed.</summary>
        private static int SheathAttachment(int k, WowWeaponInfo w, float length)
        {
            int sheathe = w != null ? w.SheatheType : -1, inv = w != null ? w.InventoryType : -1;
            if (k == 2)
            {
                if (inv == 25 || inv == 28) return -1;                 // thrown, relic
                if (inv == 26 && length < 0.55f) return -1;            // a wand
                return 31;                                             // bow, gun, crossbow: diagonally on the back, up to the right
            }
            if ((w != null && w.Shield) || sheathe == 4 || inv == 14) return 28;
            switch (sheathe)
            {
                case 1: return k == 0 ? 27 : 26;                       // two-handed: pointing down, hilt over the left shoulder
                case 2: return k == 0 ? 30 : 31;                       // staff: pointing up, head over the left shoulder
                case 3: case 5: case 6: return k == 0 ? 32 : 33;
                case 7: return 33;
            }
            if (inv == 23) return -1;                                  // held in the off hand: not shown sheathed
            if (inv == 17 || (w != null && w.TwoHand)) return k == 0 ? 27 : 26;
            return k == 0 ? 32 : 33;
        }

        // ------------------------------------------------------------------ gameobjects (iteration 6)

        /// <summary>A gameobject: its GameObjectDisplayInfo model (M2, animated - braziers, banners - or a WMO such as a ship), click box
        /// from the display's GeoBox.</summary>
        private void BuildGameObject(WowFiles files, WowEntityArt art)
        {
            ModelTried = true;
            BuiltDisplayId = State.DisplayId;
            if (files == null || State.DisplayId <= 0 || art == null) return;
            var g = art.GoInfo(State.DisplayId);
            if (g == null) { art.WantGameObject(State.DisplayId, 0f); ModelTried = false; return; }   // not resolved yet: try again later
            if (g.Error != null) return;
            GameObject go = null;
            try
            {
                if (g.Wmo) go = SomniumSpace.Worlds.SomniumWoW.World.WmoFactory.Build(files, g.ModelPath, 0, true);
                else go = M2Factory.Build(files, g.ModelPath, new M2BuildOptions { Name = State.Name ?? "Gameobject" });
            }
            catch (Exception e) { WowLog.Warn("Entities: gameobject " + State.Name + " (" + g.ModelPath + ") threw: " + e.Message); }
            if (go == null) return;
            Model = go;
            Anim = go.GetComponent<M2Animator>();
            _goModelPath = g.Wmo ? null : g.ModelPath;   // duelsnd: its $GCn sound events (WowEntityView.Spawn.cs)
            DisplayScale = 1f;
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _appliedScale = -1f;
            ApplyScale();
            if (State.IsTransport) AddTransportBody(files, g);   // iteration 11 (motion hook 8; IsTransport stays false until motion's Transport.cs)
            bool box = g.MaxX > g.MinX && g.MaxY > g.MinY && g.MaxZ > g.MinZ;
            float s = TotalScale;
            if (box)
            {
                Vector3 a = WowSpace.PointToUnity(g.MinX, g.MinY, g.MinZ), b = WowSpace.PointToUnity(g.MaxX, g.MaxY, g.MaxZ);
                Vector3 lo = Vector3.Min(a, b) * s, hi = Vector3.Max(a, b) * s;
                _boxCenter = (lo + hi) * 0.5f;
                _boxSize = new Vector3(Mathf.Max(0.3f, hi.x - lo.x), Mathf.Max(0.3f, hi.y - lo.y), Mathf.Max(0.3f, hi.z - lo.z));
                Top = Mathf.Max(0.3f, hi.y);
                BoundsTop = hi.y;
                TopSource = "GeoBox";
                Footprint = Mathf.Max(0.6f, Mathf.Max(_boxSize.x, _boxSize.z) * 1.1f);
                ApplyBox(false);
            }
            else if (!g.Wmo) MeasureBounds();
            AfterBuild();
        }

        /// <summary>
        /// Level of detail, once a frame: beyond drawRange the model is hidden (the click box and name stay); the M2 animation is
        /// sampled only while the model is on screen (any camera) or within 25 m, and not for a corpse holding its last frame.
        /// </summary>
        public void UpdateLod(float distance, float drawRange)
        {
            if (Model == null) return;
            bool hide = distance > drawRange;
            if (hide != _modelHidden)
            {
                _modelHidden = hide;
                Model.SetActive(!hide);
                if (_mount != null) _mount.SetActive(!hide);
            }
            bool seen = distance < 25f || _visRenderer == null || _visRenderer.isVisible;
            if (_mountAnim != null && _mountAnim.enabled != (!hide && seen)) _mountAnim.enabled = !hide && seen;
            if (Anim == null) return;
            bool animate = !hide && !(_deadShown && _holding) && seen;
            if (Anim.enabled != animate) Anim.enabled = animate;
        }

        // ------------------------------------------------------------------ mounts, stand states, emote states, doors (iteration 6)

        // AnimationData.dbc ids MEASURED by name on the real file (harness ViewsFacts): Mount 91, SitGround 97, Sleep 100, SitChairLow 102,
        // SitChairMed 103, SitChairHigh 104, KneelLoop 115, Close 146, Closed 147, Open 148, Opened 149.
        private const int AnimMount = 91, AnimSitGround = 97, AnimSleep = 100, AnimSitChairLow = 102, AnimSitChairMed = 103, AnimSitChairHigh = 104,
            AnimKneelLoop = 115, AnimClose = 146, AnimClosed = 147, AnimOpen = 148, AnimOpened = 149;
        private static int _mountsLogged;

        private GameObject _mount;
        private M2Animator _mountAnim;
        private Transform _mountSeat;
        private int _mountId, _mountWanted, _mountBase = -1;
        private float _mountAskedAt = -1f, _mountTop = 1.5f, _appliedLift;
        private int _goState = -1;
        private int _emoteStateSeen = -1, _emoteAnim, _emoteStand;

        /// <summary>Metres the rider sits above the root while mounted (0 on foot): the name plate and click box rise with it.</summary>
        public float RiderLift;

        /// <summary>
        /// UNIT_FIELD_MOUNTDISPLAYID (EntityState.MountDisplayId): the mount's creature model under the unit, the rider on the mount's
        /// attachment 0 (WMV wmv07_enums.h "ATT_LEFT_WRIST = 0, // Mountpoint" - Gear seats remote avatars the same way) playing Mount.
        /// Built once the art worker has the mount display (or 10 s after asking). Main thread, once a frame.
        /// </summary>
        public void SyncMount(WowFiles files, WowEntityArt art, float priority, float now)
        {
            int want = (BuiltLook == Look.Creature || BuiltLook == Look.Character) && Model != null && !State.Dead ? State.MountDisplayId : 0;
            if (want == _mountId) return;
            if (want <= 0 || files == null) { DropMount(); return; }
            if (_mountWanted != want) { _mountWanted = want; _mountAskedAt = now; }
            if (art != null && !art.WantDisplay(want, priority) && now - _mountAskedAt < 10f) return;
            DropMount();
            _mountId = want;
            GameObject go = null;
            try { go = WowCreatures.Build(files, want); }
            catch (Exception e) { WowLog.Warn("Entities: mount display " + want + " of " + State.Name + " threw: " + e.Message); }
            if (go == null) { WowLog.Warn("Entities: no model for mount display " + want + " of " + State.Name); return; }
            _mount = go;
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _mountAnim = go.GetComponent<M2Animator>();
            _mountSeat = _mountAnim != null ? _mountAnim.Attachment(0) : null;
            _mountTop = 1.5f;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) _mountTop = Mathf.Max(_mountTop, r.bounds.max.y - go.transform.position.y);
            _mountBase = -1;
            _gaitModelHave = false;
            _base = -1;
            if (_mountsLogged++ < 20)
                WowLog.Info("Entities: " + State.Name + " rides mount display " + want + " (" + (_mountSeat != null ? "mount point 0" : "no mount point: seat at 80% of " + _mountTop.ToString("0.0") + " m")
                    + ", rider animation " + (Anim != null && Anim.Has(AnimMount) ? "Mount" : "Stand (no Mount sequence)") + ")");
        }

        private void DropMount()
        {
            if (_mount != null) UnityEngine.Object.Destroy(_mount);
            _mount = null;
            _mountAnim = null;
            _mountSeat = null;
            _mountId = 0;
            _mountBase = -1;
            _gaitModelHave = false;
            _base = -1;
            if (Model != null) Model.transform.localPosition = Vector3.zero;
            RiderLift = 0f;
            if (_appliedLift != 0f) { _appliedLift = 0f; ApplyBox(State != null && State.Dead); }
        }

        /// <summary>The rider onto the mount's seat (after the mount's bones moved this frame, or one frame late). LateUpdate.</summary>
        public void PlaceRider()
        {
            if (_mount == null || Model == null) return;
            Vector3 seat = _mountSeat != null ? Root.transform.InverseTransformPoint(_mountSeat.position) : new Vector3(0f, 0.8f * _mountTop, 0f);
            Model.transform.localPosition = seat;
            RiderLift = Mathf.Max(0f, seat.y);
            if (Mathf.Abs(RiderLift - _appliedLift) > 0.05f && Click != null && Click.Box != null)
            {
                _appliedLift = RiderLift;
                Click.Box.center = _boxCenter + Vector3.up * (RiderLift * 0.5f);
                Click.Box.size = new Vector3(Mathf.Max(_boxSize.x, 1.2f), _boxSize.y + RiderLift, Mathf.Max(_boxSize.z, 2f));
            }
        }

        /// <summary>
        /// Iteration 7 (owner Remote): the mount's gait by RideGait.Pick (the local ride's own rules: Stand / Walk / Run / WalkBackwards
        /// on the ground, Swim* in water, Fly / Hover / SwimIdle in the air) from what the session says of the unit - EntityState.Flying /
        /// Swimming (its movement flags) and Pitch - and its measured motion, through M2Animator.CrossFade (the model's own blend
        /// times). This fixes iteration 6's gap "flying mounts use the walk / run / stand animations". The unit's run speed is not in
        /// EntityState: its WALKING flag (EntityState.Running false) decides Walk vs Run, as WoW's own client does for other players.
        /// </summary>
        private void AnimateMount(EntityState s)
        {
            if (_mountAnim == null) return;
            if (!_gaitModelHave) { _gaitModel = GearRideGait.Of(_mountAnim, _mount.transform); _gaitModelHave = true; }
            RideMode mode = s.Flying ? RideMode.Flying : s.Swimming ? RideMode.Swimming : RideMode.Ground;
            float forward = s.Moving ? _speedYd * WowSpace.MetresPerYard : 0f;
            float vertical = _speedVz * WowSpace.MetresPerYard;
            if ((s.Flying || s.Swimming) && s.Moving && Mathf.Abs(vertical) < 0.05f && Mathf.Abs(s.Pitch) > 0.02f) vertical = forward * Mathf.Sin(s.Pitch);
            // Walk vs Run by the unit's WALKING flag: Pick's threshold is 0.45 x the run speed.
            float run = s.Running ? Mathf.Max(forward, 0.1f) : Mathf.Max(forward / 0.3f, 0.1f);
            RideGaitPick pick = RideGait.Pick(mode, forward, _turnDeg, vertical, run, false, false, _gaitModel, _mountBase);
            _mountBase = GearRideGait.Apply(_mountAnim, _mountBase, pick.AnimId, pick.Rate);
        }

        private RideGaitModel _gaitModel;
        private bool _gaitModelHave;
        private Vector3 _lastTarget;
        private bool _haveLastTarget, _riderPathSaid;
        private float _speedVz, _turnDeg, _lastSimYaw;

        /// <summary>UNIT_FIELD_BYTES_1 byte 0 (AC UnitDefines.h UNIT_STAND_STATE_*: 1 sit, 2 sit chair, 3 sleep, 4/5/6 low/medium/high chair,
        /// 8 kneel) -> the looping sequence the model has, -1 = stand.</summary>
        private int StandAnim(int standState)
        {
            int a;
            switch (standState)
            {
                case 1: a = AnimSitGround; break;
                case 2: case 5: a = AnimSitChairMed; break;
                case 3: a = AnimSleep; break;
                case 4: a = AnimSitChairLow; break;
                case 6: a = AnimSitChairHigh; break;
                case 8: a = AnimKneelLoop; break;
                default: return -1;
            }
            if (Anim.Has(a)) return a;
            if ((a == AnimSitChairLow || a == AnimSitChairHigh) && Anim.Has(AnimSitChairMed)) return AnimSitChairMed;
            if (a != AnimKneelLoop && Anim.Has(AnimSitGround)) return AnimSitGround;
            return -1;
        }

        private static Dictionary<int, int> _emoteAnims;
        private static Dictionary<int, int> _emoteStands;

        /// <summary>
        /// UNIT_NPC_EMOTESTATE (an Emotes.dbc id) -> its looping animation. Emotes.dbc MEASURED (harness ViewsFacts): 7 fields, [2] AnimID -
        /// 173 STATE_WORK -> 136 EmoteWorkNoSheathe, 233 STATE_WORK_MINING -> 136, 10 STATE_DANCE -> 69, 379 STATE_FISHING -> 134; 13
        /// STATE_SIT has AnimID 0 and [4] 1 / [5] 1 (EmoteSpecProc / its parameter: a stand state), returned as standState. Main thread.
        /// </summary>
        private static int EmoteAnim(WowFiles files, int emote, out int standState)
        {
            standState = 0;
            if (emote <= 0 || files == null) return 0;
            if (_emoteAnims == null)
            {
                _emoteAnims = new Dictionary<int, int>();
                _emoteStands = new Dictionary<int, int>();
                var t = Dbc.Load(files, "Emotes");
                if (t != null && t.FieldCount == 7)
                    for (int r = 0; r < t.RecordCount; r++)
                    {
                        int id = t.GetInt(r, 0);
                        _emoteAnims[id] = t.GetInt(r, 2);
                        if (t.GetInt(r, 4) == 1) _emoteStands[id] = t.GetInt(r, 5);
                    }
                else WowLog.Warn("Entities: Emotes.dbc " + (t == null ? "missing" : "has " + t.FieldCount + " fields, expected 7") + " - NPC emote states not animated");
            }
            _emoteStands.TryGetValue(emote, out standState);
            int anim;
            return _emoteAnims.TryGetValue(emote, out anim) ? anim : 0;
        }

        /// <summary>A door (GoType 0): GOState 0/2 active = open, 1 ready = closed (EntityState.GoState) -> Opened / Closed, with Open /
        /// Close played on a change (held at their end).</summary>
        private void AnimateObject(EntityState s, float now)
        {
            SpawnAnimate(s, now);  // duelsnd: a spawned duel flag plays Spawn + its GameObjectDisplayInfo sound (WowEntityView.Spawn.cs)
            TrGoAnimate(s, now);   // iteration 11 (trainer U11 a): the one-shot custom animation + sound (partial, inert until trainer)
            if (s.GoType != 0) return;
            int st = s.GoState == 1 ? 1 : 0;
            if (st == _goState) return;
            bool first = _goState < 0;
            _goState = st;
            if (st == 0)
            {
                if (!first && Anim.Has(AnimOpen)) { Anim.HoldAtEnd = true; Anim.Play(AnimOpen, false); }
                else if (Anim.Has(AnimOpened)) { Anim.HoldAtEnd = false; Anim.Play(AnimOpened, true); }
                else if (Anim.Has(AnimOpen)) { Anim.HoldAtEnd = true; Anim.Play(AnimOpen, false); Anim.Speed = first ? 20f : 1f; }
            }
            else
            {
                if (!first && Anim.Has(AnimClose)) { Anim.HoldAtEnd = true; Anim.Speed = 1f; Anim.Play(AnimClose, false); }
                else if (Anim.Has(AnimClosed)) { Anim.HoldAtEnd = false; Anim.Speed = 1f; Anim.Play(AnimClosed, true); }
                else { Anim.HoldAtEnd = false; Anim.Speed = 1f; Anim.Play(AnimStand, true); }
            }
        }

        private WowWeaponInfo Hold(WowFiles files, WowEntityArt art, int displayId, bool offHand)
        {
            var w = art.Weapon(displayId);
            if (w.Error != null) return null;
            // Main hand on the right palm (1); a bow in the LEFT palm (2) as WoW holds bows; off hand on the left palm, or the
            // left wrist (0) for a shield (attachment ids measured by Models).
            int attachment = w.Shield ? 0 : (offHand || w.Bow) ? 2 : 1;
            bool left = attachment != 1;
            var point = Anim.Attachment(attachment);
            if (point == null) { WowLog.Warn("Entities: " + State.Name + " model has no attachment " + attachment + " for item " + displayId); return w; }
            GameObject item = null;
            try { item = WowItems.Build(files, displayId, left); }
            catch (Exception e) { WowLog.Warn("Entities: item " + displayId + " threw: " + e.Message); }
            if (item == null) return w;
            item.transform.SetParent(point, false);
            WowEnchantVisuals.Apply(files, item, SomniumSpace.Worlds.SomniumWoW.Play.Spells.ItemVisualDb.Get(files).VisualOfDisplay(displayId));   // iteration 11 (spellfx hook 6): the item's own glow
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            if (!w.Shield) Anim.CloseHand(attachment == 1, true);
            return w;
        }

        // ---- iteration 11 (spellfx hook 6): weapon enchant glows of a player's weapons and other units' stealth look
        private readonly int[] _gearVisual = new int[3];
        private bool _stealthShown;

        /// <summary>The ItemVisuals id of gear slot k (15 + k): the enchant (EntityState.EquipEnchants) or the item's own glow.</summary>
        private int GearVisual(WowFiles files, int k)
        {
            int slot = 15 + k;
            int[] en = State.EquipEnchants;
            return SomniumSpace.Worlds.SomniumWoW.Play.Spells.ItemVisualDb.Get(files).VisualFor(en != null && slot < en.Length ? en[slot] : 0, _gearIds[k]);
        }

        /// <summary>Every frame (units): re-applies a gear glow whose visual changed; the translucent look on Stealthed changes.</summary>
        private void FxTick(EntityState s, WowFiles files)
        {
            if (files != null && BuiltLook == Look.Character)
                for (int k = 0; k < 3; k++)
                {
                    if (_gear[k] == null) continue;
                    int v = GearVisual(files, k);
                    if (v == _gearVisual[k]) continue;
                    _gearVisual[k] = v;
                    WowEnchantVisuals.Apply(files, _gear[k], v);
                }
            if (s.Stealthed != _stealthShown && Model != null)
            {
                _stealthShown = s.Stealthed;
                if (_stealthShown) WowStealthLook.Apply(Model, 0.3f);   // Stealth state kit 312 CharParamZero[1] (proposal-spellfx.md file list, measured)
                else WowStealthLook.Clear(Model);
            }
        }

        /// <summary>Iteration 11 (motion hook 8): an M2 transport (elevator, tram car) gets a MeshCollider child on layer 0 from its M2
        /// collision mesh (to stand on); every transport root a kinematic Rigidbody (moving colliders).</summary>
        private void AddTransportBody(WowFiles files, WowGoInfo g)
        {
            if (!g.Wmo && Model != null)
            {
                Mesh m = M2Factory.CollisionMesh(files, g.ModelPath);
                if (m != null)
                {
                    var col = new GameObject("Transport collision");
                    col.layer = 0;
                    col.transform.SetParent(Model.transform, false);
                    col.AddComponent<MeshCollider>().sharedMesh = m;
                }
            }
            Rigidbody rb = Root.GetComponent<Rigidbody>();
            if (rb == null) rb = Root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        private void MeasureBounds()
        {
            if (Model == null) return;
            Bounds b = new Bounds();
            bool have = false;
            var smr = BodyRenderer(Model);
            if (smr != null && smr.sharedMesh != null) { b = smr.sharedMesh.bounds; have = true; }
            else
            {
                var mf = Model.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) { b = mf.sharedMesh.bounds; have = true; }
            }
            if (!have && Anim != null && Anim.Model != null)
            {
                var m = Anim.Model;
                b = new Bounds(Vector3.zero, Vector3.zero);
                b.SetMinMax(Vector3.Min(WowSpace.PointToUnity(m.BoundsMin.X, m.BoundsMin.Y, m.BoundsMin.Z), WowSpace.PointToUnity(m.BoundsMax.X, m.BoundsMax.Y, m.BoundsMax.Z)),
                            Vector3.Max(WowSpace.PointToUnity(m.BoundsMin.X, m.BoundsMin.Y, m.BoundsMin.Z), WowSpace.PointToUnity(m.BoundsMax.X, m.BoundsMax.Y, m.BoundsMax.Z)));
                have = true;
            }
            if (!have) return;
            float s = TotalScale;
            Vector3 c = b.center * s, size = b.size * s;
            if (_standTop > 0.05f) { Top = Mathf.Max(0.15f, _standTop * s); TopSource = "standing pose"; }
            else { Top = Mathf.Max(0.4f, b.max.y * s); TopSource = "bounds"; }
            float helm = HelmTop();   // NPC gear fix (iteration 11): a helm's crest / horns reach above the head - the plate goes above them
            if (helm > Top) { Top = helm; TopSource += " + helm"; }
            BoundsTop = b.max.y * s;
            Footprint = Mathf.Max(0.6f, Mathf.Max(size.x, size.z) * 1.1f);
            _boxCenter = c;
            _boxSize = new Vector3(Mathf.Max(0.5f, size.x), Mathf.Max(0.5f, size.y), Mathf.Max(0.5f, size.z));
            ApplyBox(State.Dead);
        }

        /// <summary>Highest point (metres above the root) of what hangs on the helm attachment (11, NpcGear), 0 = nothing. Build / rescale only.</summary>
        private float HelmTop()
        {
            var att = Anim != null ? Anim.Attachment(NpcGear.AttachHelm) : null;
            if (att == null || Root == null) return 0f;
            float top = 0f;
            for (int i = 0; i < att.childCount; i++)
            {
                var r = att.GetChild(i).GetComponentInChildren<Renderer>();
                if (r != null) top = Mathf.Max(top, r.bounds.max.y - Root.transform.position.y);
            }
            return top;
        }

        /// <summary>Top of the mesh bounds (bind pose), metres - kept for the log comparison with the standing-pose top.</summary>
        public float BoundsTop;

        // Standing-pose head top per display (metres at scale 1, -1 = could not be measured). Main thread only.
        private static readonly System.Collections.Generic.Dictionary<int, float> StandTops = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>Number of displays measured so far (for the log).</summary>
        public static int StandTopsMeasured { get { return StandTops.Count; } }

        /// <summary>
        /// The real top of the model as it stands: the drawn vertices of the built mesh (only the geosets this display shows)
        /// skinned with the bones of the model's rest sequence (Stand, else its first sequence - M2Pose.RestSequence) at time 0,
        /// highest point in metres at scale 1. The bind-pose mesh bounds are wrong for models whose bind pose is not their stance
        /// (crouching imps, spiders, quadrupeds); this is the pose the creature shows when idle. Cached per display id.
        /// </summary>
        /// <summary>The body's skinned renderer: the one with the most vertices (held weapons are separate, smaller skinned
        /// meshes under the hand attachments). Build/rescale time only.</summary>
        private static SkinnedMeshRenderer BodyRenderer(GameObject model)
        {
            SkinnedMeshRenderer best = null;
            int most = -1;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int n = r != null && r.sharedMesh != null ? r.sharedMesh.vertexCount : -1;
                if (n > most) { most = n; best = r; }
            }
            return best;
        }

        private static float StandTop(WowFiles files, int displayId, GameObject model, M2Animator anim)
        {
            float cached;
            if (StandTops.TryGetValue(displayId, out cached)) return cached;
            float top = -1f;
            try { top = ComputeStandTop(files, model, anim); }
            catch (Exception e) { WowLog.Warn("Entities: standing-pose top of display " + displayId + " threw: " + e.Message); }
            StandTops[displayId] = top;
            return top;
        }

        /// <summary>
        /// The vertices of the batches that are really seen at rest: blend Opaque/AlphaKey/Alpha (not additive glows, whose quads
        /// reach past the head) with transparency x colour alpha >= 0.05 at the rest sequence's time 0. MEASURED (harness EntTops):
        /// VoidWalker.m2 batches 0 and 1 have alpha 0 at rest and reach 3.45 yd, the seen body tops out at 2.92 yd (attachment 18,
        /// the client's name point, is at 3.08 yd). The built mesh's submeshes are the drawn batches in M2MeshData's order (priority
        /// plane, material layer, file order); they are matched to the skin's batches by index count, in order. Null = use all
        /// (no skin, no match, or no solid batch).
        /// </summary>
        private static bool[] SolidVertices(WowFiles files, M2Model m, int seq, Mesh mesh, int vertexCount)
        {
            M2Model pm; M2Skin skin; string err;
            if (files == null || m.Path == null || !M2Factory.TryGetParsed(files, m.Path, out pm, out skin, out err) || skin == null || !ReferenceEquals(pm, m)) return null;
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < skin.Batches.Length; i++)
            {
                var b = skin.Batches[i];
                if (b.SubmeshIndex < 0 || b.SubmeshIndex >= skin.Submeshes.Length || skin.Submeshes[b.SubmeshIndex].IndexCount < 3) continue;
                order.Add(i);
            }
            order.Sort((a, b) =>
            {
                int c = skin.Batches[a].PriorityPlane.CompareTo(skin.Batches[b].PriorityPlane);
                if (c != 0) return c;
                c = skin.Batches[a].MaterialLayer.CompareTo(skin.Batches[b].MaterialLayer);
                return c != 0 ? c : a.CompareTo(b);
            });
            var use = new bool[vertexCount];
            int j = 0, solid = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int count = (int)mesh.GetIndexCount(sub);
                while (j < order.Count && skin.Submeshes[skin.Batches[order[j]].SubmeshIndex].IndexCount != count) j++;
                if (j >= order.Count) return null;   // the mesh does not match the skin: measure everything
                int bi = order[j++];
                var info = M2MeshData.Describe(m, skin.Batches[bi], bi);
                if (info.BlendMode > 2) continue;
                float alpha = 1f, v;
                if (info.TransparencyIndex >= 0 && M2Sampler.Sample(m.Transparencies[info.TransparencyIndex], seq, 0, m.GlobalSequences, 0, out v)) alpha *= v;
                if (info.ColorIndex >= 0 && M2Sampler.Sample(m.Colors[info.ColorIndex].Alpha, seq, 0, m.GlobalSequences, 0, out v)) alpha *= v;
                if (alpha < 0.05f) continue;
                var tris = mesh.GetTriangles(sub);
                for (int t = 0; t < tris.Length; t++) if (tris[t] >= 0 && tris[t] < vertexCount) use[tris[t]] = true;
                solid++;
            }
            return solid > 0 ? use : null;
        }

        private static float ComputeStandTop(WowFiles files, GameObject model, M2Animator anim)
        {
            if (model == null || anim == null || anim.Model == null) return -1f;
            var smr = BodyRenderer(model);
            if (smr == null || smr.sharedMesh == null) return -1f;
            var m = anim.Model;
            int seq = M2Pose.RestSequence(m);
            if (seq < 0 || m.Bones.Length == 0) return -1f;
            float[] mats = M2Pose.Compute(m, seq, 0, 0);
            var mesh = smr.sharedMesh;
            var verts = mesh.vertices;
            var bw = mesh.boneWeights;
            if (verts == null || verts.Length == 0 || bw == null || bw.Length != verts.Length) return -1f;
            bool[] use = SolidVertices(files, m, seq, mesh, verts.Length);
            int nb = m.Bones.Length;
            float k = 1f / WowSpace.MetresPerYard;
            float best = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                if (use != null && !use[i]) continue;
                // Unity mesh vertex (bind pose) -> M2 model space: the inverse of WowSpace.PointToUnity (-y, z, x) * 0.9144.
                Vector3 u = verts[i];
                float px = u.z * k, py = -u.x * k, pz = u.y * k;
                var w = bw[i];
                float z = SkinZ(mats, nb, w.boneIndex0, w.weight0, px, py, pz) + SkinZ(mats, nb, w.boneIndex1, w.weight1, px, py, pz)
                        + SkinZ(mats, nb, w.boneIndex2, w.weight2, px, py, pz) + SkinZ(mats, nb, w.boneIndex3, w.weight3, px, py, pz);
                if (z > best) best = z;
            }
            return best == float.MinValue ? -1f : best * WowSpace.MetresPerYard;
        }

        /// <summary>Weighted Z (up) of one bone's transform of the point (column-major 4x4 from M2Pose.Compute).</summary>
        private static float SkinZ(float[] mats, int nb, int bone, float w, float px, float py, float pz)
        {
            if (w <= 0f || bone < 0 || bone >= nb) return 0f;
            int o = bone * 16;
            return w * (mats[o + 2] * px + mats[o + 6] * py + mats[o + 10] * pz + mats[o + 14]);
        }

        /// <summary>The name line: "&lt;Owner&gt;'s Imp" for a pet (Rules: Name = the pet, SubName = its owner), else the name.</summary>
        public string PlateName()
        {
            var s = State;
            if (s == null) return null;
            if (s.Kind != EntityKind.Pet || string.IsNullOrEmpty(s.SubName)) return s.Name;
            if (_petName == null || !ReferenceEquals(s.Name, _petNameFrom) || !ReferenceEquals(s.SubName, _petNameOwner))
            {
                _petNameFrom = s.Name;
                _petNameOwner = s.SubName;
                _petName = s.SubName + "'s " + (s.Name ?? "Pet");
            }
            return _petName;
        }

        private const int AnimEmoteTalk = 60;   // AnimationData.dbc "EmoteTalk" (Models report, measured; checked by name below)

        /// <summary>The player opened a dialog with this NPC: WoW's talk emote once, when the model has it and is not busy.</summary>
        public void Talk(WowFiles files, float now)
        {
            if (Anim == null || State == null || State.Dead || State.Moving) return;
            if (!Anim.Has(AnimEmoteTalk)) return;
            string name = files != null ? WowModelDb.AnimationName(files, AnimEmoteTalk) : null;
            if (name != null && !string.Equals(name, "EmoteTalk", StringComparison.OrdinalIgnoreCase)) return;
            if (now < _oneShotUntil && _oneShotIsAttack) return;
            PlayOnce(AnimEmoteTalk, false, now);
        }

        private void ApplyBox(bool dead)
        {
            if (Click == null || Click.Box == null) return;
            if (!dead) { Click.Box.center = _boxCenter; Click.Box.size = _boxSize; return; }
            if (State != null && State.Kind == EntityKind.Player)
            {
                // rescorpse (2026-10-04, ours): a dead player / a player's corpse (the resurrection target) lies along the ground - the last Death
                // frame reaches 0.4-1.0 standing heights from the root in any direction by race (HumanMale x -1.08..1.14 y -0.95..0.78 yd of a 2.13 yd
                // standing top; TaurenMale x -0.80..2.15; DwarfMale x -1.53..0.66 - P/harness/Probes/O2/rescorpse/ResCorpseFactsProbe.cs) and is at most
                // 0.18-0.37 standing heights high: a square of 1.3 standing heights round the root, 0.4 high, so a click on the body selects it.
                float stand = Mathf.Max(0.5f, _boxSize.y);
                float side = Mathf.Max(1.2f, stand * 1.3f), high = Mathf.Max(0.5f, stand * 0.4f);
                Click.Box.center = new Vector3(0f, high * 0.5f, 0f);
                Click.Box.size = new Vector3(side, high, side);
                return;
            }
            // A corpse lies down: keep the footprint, half the height, sitting on the ground.
            float h = Mathf.Max(0.5f, _boxSize.y * 0.5f);
            Click.Box.center = new Vector3(_boxCenter.x, h * 0.5f, _boxCenter.z);
            Click.Box.size = new Vector3(_boxSize.x, h, _boxSize.z);
        }

        public float TotalScale { get { return DisplayScale * (State != null && State.Scale > 0f ? State.Scale : 1f); } }

        private void ApplyScale()
        {
            if (Model == null) return;
            float s = TotalScale;
            if (Mathf.Approximately(s, _appliedScale)) return;
            bool remeasure = _appliedScale > 0f;   // a later scale change (not the build): plate height / box follow it
            _appliedScale = s;
            Model.transform.localScale = new Vector3(s, s, s);
            if (remeasure) MeasureBounds();
        }

        public void DestroyModel()
        {
            if (Model != null) UnityEngine.Object.Destroy(Model);
            Model = null;
            Anim = null;
            ModelTried = false;
            _mainHand = _offHand = null;
            _visRenderer = null;
            _modelHidden = false;
            _placedSheath = -1;
            ArtAskedAt = -1f;
            for (int k = 0; k < 3; k++) { _gear[k] = null; _gearInfo[k] = null; _gearIds[k] = 0; _gearVisual[k] = 0; }
            _stealthShown = false;   // iteration 11 (spellfx hook 6): a new model gets the look again
        }

        // ------------------------------------------------------------------ pose

        /// <summary>Moves the root toward the entity's position/facing (smoothed; snaps on big jumps such as respawn/evade).</summary>
        public void UpdatePose(EntityState s, int mapId, float dt)
        {
            if (ModelTried && BuiltLook == Look.Somnium) { SomniumDeadBox(s); if (FollowSomniumAvatar(s)) return; }
            else if (AnchorHidden || _anchorLift != 0f) { SetAnchorHidden(false); ApplyAnchorLift(0f); }
            Vector3 target = WowSpace.ToUnity(mapId, s.Pos.X, s.Pos.Y, s.Pos.Z);
            if (s.Kind == EntityKind.Totem) target.y = PlantedY(target);
            float yaw = WowSpace.OrientationToYaw(s.Pos.O);
            if (Facing && !s.Dead && !s.Moving)
            {
                Vector3 to = FacePoint - target;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;   // model forward = +z at yaw 0
            }
            // Iteration 7 (owner Remote, PLAN7 s5.5 P3): a normal client's player riding a mount moves at 11-29 yd/s. Following the
            // dead-reckoned position with exp 15/s drew it ~1 m behind at 14 yd/s; instead it is drawn ON the dead-reckoned path and
            // only the jump of a new server position is kept as an offset that decays (the same 15/s: 95 % in 0.2 s).
            bool rider = s.Kind == EntityKind.Player && (s.Mounted || s.MountDisplayId > 0) && !s.IsSomniumPlayer;
            if (!Placed || s.IsTransport || (target - Pos).sqrMagnitude > 64f)   // iteration 11 (motion hook 8): transports unsmoothed
            {
                Pos = target;
                Yaw = yaw;
                Placed = true;
            }
            else if (rider && _haveLastTarget)
            {
                Pos = target + (Pos - _lastTarget) * Mathf.Exp(-dt * 15f);
                if (!s.Dead) Yaw = Mathf.LerpAngle(Yaw, yaw, 1f - Mathf.Exp(-dt * 12f));
                if (!_riderPathSaid) { _riderPathSaid = true; WowLog.Info("Entities: " + s.Name + " rides (another player's client): drawn on its dead-reckoned path, server corrections decayed over 0.2 s"); }
            }
            else
            {
                Pos = Vector3.Lerp(Pos, target, 1f - Mathf.Exp(-dt * 15f));
                if (!s.Dead) Yaw = Mathf.LerpAngle(Yaw, yaw, 1f - Mathf.Exp(-dt * 12f));
            }
            _lastTarget = target;
            _haveLastTarget = true;
            Quaternion rot = Quaternion.Euler(0f, Yaw, 0f);
            if (s.Kind == EntityKind.GameObject)
            {
                // A gameobject's full rotation (tilted logs, signs); none said: its orientation, unsmoothed.
                // Iteration 11 (motion hook 8): an MO transport (boat, zeppelin) is sent with an identity world rotation (AC Transport.cpp:129-131);
                // its heading is the path's orientation in Pos.O. Static transports (elevators, tram) keep their spawn rotation.
                if (s.IsTransport && s.GoType == 15) rot = Quaternion.Euler(0f, yaw, 0f);
                else if (s.RotX != 0f || s.RotY != 0f || s.RotZ != 0f || s.RotW != 0f) rot = WowSpace.RotationToUnity(s.RotX, s.RotY, s.RotZ, s.RotW);
                else rot = Quaternion.Euler(0f, yaw, 0f);
            }
            Root.transform.SetPositionAndRotation(Pos, rot);
            ApplyScale();

            // Measured ground speed (yards/s) for matching the walk/run cycle to the movement.
            if (_haveSimPos && dt > 0f)
            {
                float dx = s.Pos.X - _lastSimPos.X, dy = s.Pos.Y - _lastSimPos.Y;
                float v = Mathf.Sqrt(dx * dx + dy * dy) / dt;
                if (v > 40f) v = _speedYd;   // a teleport, not a speed
                _speedYd = Mathf.Lerp(_speedYd, s.Moving ? v : 0f, 1f - Mathf.Exp(-dt * 8f));
                // Iteration 7: vertical speed (yards/s, + up) and turn rate (Unity degrees/s, + right) for the mount's gait (RideGait.Pick).
                float vz = (s.Pos.Z - _lastSimPos.Z) / dt;
                if (Mathf.Abs(vz) > 40f) vz = _speedVz;
                _speedVz = Mathf.Lerp(_speedVz, s.Moving ? vz : 0f, 1f - Mathf.Exp(-dt * 8f));
                float simYaw = WowSpace.OrientationToYaw(s.Pos.O);
                float turn = Mathf.DeltaAngle(_lastSimYaw, simYaw) / dt;
                if (Mathf.Abs(turn) > 720f) turn = 0f;   // a facing set at once, not a turn
                _turnDeg = Mathf.Lerp(_turnDeg, turn, 1f - Mathf.Exp(-dt * 8f));
            }
            _lastSimYaw = WowSpace.OrientationToYaw(s.Pos.O);
            _lastSimPos = s.Pos;
            _haveSimPos = true;
        }

        // ------------------------------------------------------------------ iteration 7 (owner Remote): a Somnium rider's WoW character

        /// <summary>True while this is a Somnium player's character whose avatar is not drawn here: no click box, no target circle.</summary>
        public bool AnchorHidden { get; private set; }
        private float _anchorLift;
        private bool _anchorSaid;

        /// <summary>
        /// A Somnium player's own WoW character (Look.Somnium: not drawn, their avatar is there): its click box and the target circle
        /// follow their AVATAR as this client draws it (WowGear.TryRiderAnchor: re-mapped, ride-smoothed; the mount's feet while they
        /// ride, the box raised by the seat height), not the lagging server position (several metres behind at mount speeds). Hidden
        /// while that avatar is hidden (another map, the room, far). Neither: the server position, as in iteration 6. True = placed.
        /// </summary>
        private bool FollowSomniumAvatar(EntityState s)
        {
            Vector3 feet;
            float seat, yaw;
            if (WowGear.TryRiderAnchor(s.Guid, s.Name, out feet, out seat, out yaw))
            {
                SetAnchorHidden(false);
                Pos = feet;
                Yaw = yaw;
                Placed = true;
                Root.transform.SetPositionAndRotation(Pos, Quaternion.Euler(0f, Yaw, 0f));
                ApplyAnchorLift(seat);
                _lastSimPos = s.Pos;
                _haveSimPos = true;
                if (!_anchorSaid)
                {
                    _anchorSaid = true;
                    WowLog.Info("Entities: " + s.Name + " (a Somnium player) - click box and target circle follow their avatar as drawn here" + (seat > 0.05f ? ", raised " + seat.ToString("0.00") + " m (mounted)" : ""));
                }
                return true;
            }
            SetAnchorHidden(WowGear.SomniumAvatarHidden(s.Guid, s.Name));
            ApplyAnchorLift(0f);
            return false;
        }

        /// <summary>States fix 2026-10-04: a Somnium player lying dead (their avatar holds the client pose ProneForward, GearStatePose) gets
        /// the lying click box of a dead player (ApplyBox), so a click on the body selects them for a resurrection; standing again, the old box.</summary>
        private void SomniumDeadBox(EntityState s)
        {
            bool dead = s.Dead && !(s.MountDisplayId > 0);
            if (dead == _somniumDeadBox) return;
            _somniumDeadBox = dead;
            if (_anchorLift > 0.05f) return;
            ApplyBox(dead);
        }
        private bool _somniumDeadBox;

        private void SetAnchorHidden(bool hidden)
        {
            if (AnchorHidden == hidden) return;
            AnchorHidden = hidden;
            if (Click != null && Click.Box != null) Click.Box.enabled = !hidden;
            WowLog.Info("Entities: " + (State != null ? State.Name : "?") + " (a Somnium player) - " + (hidden ? "their avatar is not drawn here: click box and circle hidden" : "click box shown again"));
        }

        private void ApplyAnchorLift(float lift)
        {
            _anchorLift = lift;
            RiderLift = lift;
            if (Click == null || Click.Box == null || Mathf.Abs(lift - _appliedLift) <= 0.05f) return;
            _appliedLift = lift;
            if (lift <= 0f) { _appliedLift = 0f; ApplyBox(State != null && State.Dead); return; }
            Click.Box.center = _boxCenter + Vector3.up * (lift * 0.5f);
            Click.Box.size = new Vector3(Mathf.Max(_boxSize.x, 1.2f), _boxSize.y + lift, Mathf.Max(_boxSize.z, 2f));
        }

        private const int GroundMask = (1 << 0) | (1 << 14);   // terrain / buildings (as the selection circle)

        /// <summary>
        /// A totem stands on the drawn ground: the session places it at its terrain height, but a WMO floor or a slope between
        /// height samples could leave it floating or sunk - one ray down (only when the totem's position changes) settles it.
        /// </summary>
        private float PlantedY(Vector3 p)
        {
            if (_haveGround && Mathf.Abs(p.x - _groundFor.x) < 0.01f && Mathf.Abs(p.z - _groundFor.z) < 0.01f && Mathf.Abs(p.y - _groundFor.y) < 0.01f) return _groundY;
            _groundFor = p;
            _haveGround = true;
            RaycastHit h;
            _groundY = Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out h, 3f, GroundMask, QueryTriggerInteraction.Ignore) ? h.point.y : p.y;
            return _groundY;
        }

        /// <summary>Floating origin (WowSpace.Rebased): the root was moved by WowOrigin; the Unity positions kept here follow.</summary>
        public void OnRebased(Vector3 d)
        {
            Pos -= d;
            _lastTarget -= d;
            FacePoint -= d;
            _haveGround = false;
        }

        /// <summary>After a despawn/respawn: snap to the new position and restart the animation state.</summary>
        public void ResetForRespawn()
        {
            Placed = false;
            _haveSimPos = false;
            _base = -1;
            _oneShotUntil = 0f;
            if (_deadShown && Anim != null) { Anim.Speed = 1f; Anim.Play(AnimStand, true); }
            _deadShown = false;
            _holding = false;
            if (State != null) { _lastAttack = State.AttackCounter; _lastWound = State.WoundCounter; }
            ApplyBox(false);
        }

        // ------------------------------------------------------------------ animation

        private int First(int[] ids)
        {
            for (int i = 0; i < ids.Length; i++) if (Anim.Has(ids[i])) return ids[i];
            return -1;
        }

        private float DurationOf(int animId)
        {
            var m = Anim.Model;
            if (m == null) return 1f;
            int seq = m.FindSequence(animId);
            if (seq < 0) return 1f;
            return Mathf.Max(0.05f, m.Sequences[seq].Duration / 1000f);
        }

        // Lead 2026-10-03: the jump sequence (AnimationData.dbc ids, measured) and its state.
        private const int AnimJumpStart = 37, AnimJump = 38, AnimJumpEnd = 39, AnimFall = 40, AnimJumpLandRun = 187;
        private int _lastJump = int.MinValue;
        private bool _airShown;

        private void PlayOnce(int animId, bool attack, float now)
        {
            if (animId < 0) return;
            Anim.Speed = 1f;
            Anim.Play(animId, false);
            _oneShotUntil = now + DurationOf(animId);
            _oneShotIsAttack = attack;
            _base = -1;
        }

        private int[] AttackList()
        {
            if (_mainHand == null || _mainHand.Bow || _mainHand.Shield) return AttackUnarmedList;
            return _mainHand.TwoHand ? Attack2HList : Attack1HList;
        }

        private int[] ReadyList()
        {
            if (_mainHand == null || _mainHand.Bow || _mainHand.Shield) return ReadyUnarmedList;
            return _mainHand.TwoHand ? Ready2HList : Ready1HList;
        }

        /// <summary>
        /// Stand / Walk / Run (Moving, Running) with the cycle speed matched to the ground speed; combat Ready stance when the
        /// model has one; an attack on every AttackCounter change (AttackUnarmed / Attack1H / Attack2H by the held weapon, the
        /// first the model has); CombatWound on WoundCounter; ReadySpell while CastingSpellId != 0, SpellCast when it ends; the
        /// NPC's Emote on EmoteCounter (AnimationData names "Emote*" play once, other ids loop as the idle); Death once, then
        /// the last frame is held (no model here has a Dead sequence).
        /// </summary>
        public void Animate(EntityState s, WowFiles files, float dt, float now)
        {
            if (Anim == null) return;
            if (s.Kind == EntityKind.GameObject) { AnimateObject(s, now); return; }   // iteration 11 (trainer U11 a): + now
            FxTick(s, files);   // iteration 11 (spellfx hook 6)
            if (_mount != null) AnimateMount(s);

            if (s.Dead || (s.StandState == 7 && _mount == null))   // UNIT_STAND_STATE_DEAD: an NPC lying dead (scenery corpses)
            {
                if (!_deadShown)
                {
                    bool fast = !Placed || now - _builtAt < 0.2f;   // already dead when the view appeared
                    StartDeath(fast);
                    if (!fast && Sounds != null) PlaySound(Sounds.Death);
                }
                _wasInCombat = false;
                TickDeath(dt);
                _lastAttack = s.AttackCounter;
                _lastWound = s.WoundCounter;
                _lastEmote = s.EmoteCounter;
                _wasCasting = false;
                _woundSoundDue = -1f;
                return;
            }
            if (_deadShown)
            {
                _deadShown = false;
                _holding = false;
                Anim.Speed = 1f;
                _base = -1;
                ApplyBox(false);
            }

            if (s.AttackCounter != _lastAttack)
            {
                _lastAttack = s.AttackCounter;
                PlayOnce(First(AttackList()), true, now);
                if (Sounds != null) PlaySound(Sounds.Attack);
            }
            if (s.WoundCounter != _lastWound)
            {
                _lastWound = s.WoundCounter;
                if (!(now < _oneShotUntil && _oneShotIsAttack)) PlayOnce(First(WoundList), false, now);
                _woundSoundDue = now + WoundSoundDelay;
            }
            if (_woundSoundDue >= 0f && now >= _woundSoundDue) { _woundSoundDue = -1f; PlayWound(false, now); }
            if (s.EmoteCounter != _lastEmote)
            {
                _lastEmote = s.EmoteCounter;
                _idleEmote = 0;
                if (s.Emote > 0 && Anim.Has(s.Emote))
                {
                    string name = files != null ? WowModelDb.AnimationName(files, s.Emote) : null;
                    // lead fix 2026-10-11 (the user: "when getting a spell, the NPC loops its animation"): a spell kit's animation (the trainer's
                    // kit 179 = 53 SpellCastDirected) plays once (EntityState.EmoteOnce), never as the idle loop
                    if (s.EmoteOnce || (name != null && name.StartsWith("Emote", StringComparison.OrdinalIgnoreCase))) PlayOnce(s.Emote, false, now);
                    else { _idleEmote = s.Emote; _base = -1; }
                }
            }
            // Lead (2026-10-03, the user: "make the jumping of people on WoW client look good on Somnium"): WoW's jump sequence -
            // JumpStart once on the jump (EntityState.JumpCounter), Jump while in the air (Fall when the model has no Jump), and on landing
            // JumpLandRun while moving / JumpEnd standing (AnimationData.dbc 37 JumpStart, 38 Jump, 39 JumpEnd, 40 Fall, 187 JumpLandRun -
            // measured). Played like attacks: moving does not cut them short.
            bool air = s.Airborne && _mount == null;
            if (s.JumpCounter != _lastJump)
            {
                bool firstSeen = _lastJump == int.MinValue;
                _lastJump = s.JumpCounter;
                if (!firstSeen && air && Anim.Has(AnimJumpStart)) PlayOnce(AnimJumpStart, true, now);
            }
            if (_airShown && !air && _mount == null)
            {
                int land = s.Moving && Anim.Has(AnimJumpLandRun) ? AnimJumpLandRun : AnimJumpEnd;
                if (Anim.Has(land)) PlayOnce(land, true, now);
            }
            _airShown = air;

            bool casting = s.CastingSpellId != 0;
            if (s.InCombat && !_wasInCombat && s.Reaction != Reaction.Friendly && Sounds != null) PlaySound(Sounds.Aggro);
            _wasInCombat = s.InCombat;
            if (_wasCasting && !casting) PlayOnce(First(CastDoneList), false, now);
            _wasCasting = casting;

            int want;
            if (s.EmoteState != _emoteStateSeen) { _emoteStateSeen = s.EmoteState; _emoteAnim = EmoteAnim(files, s.EmoteState, out _emoteStand); _base = -1; }
            if (_mount != null) want = Anim.Has(AnimMount) ? AnimMount : AnimStand;
            else if (air && (Anim.Has(AnimJump) || Anim.Has(AnimFall))) want = Anim.Has(AnimJump) ? AnimJump : AnimFall;   // lead 2026-10-03
            else if (s.Moving) want = s.Running ? AnimRun : AnimWalk;
            else if (casting) { want = First(CastingList); if (want < 0) want = AnimStand; }
            else if (s.InCombat) { want = First(ReadyList()); if (want < 0) want = AnimStand; }
            else
            {
                int st = StandAnim(State.StandState != 0 ? State.StandState : _emoteStand);
                if (st >= 0) want = st;
                else if (_emoteAnim > 0 && Anim.Has(_emoteAnim)) want = _emoteAnim;
                else want = _idleEmote > 0 ? _idleEmote : AnimStand;
            }

            if (now < _oneShotUntil)
            {
                // Starting to move cuts a wound/emote short (no blending in M2Animator); an attack plays out.
                if (!(s.Moving && !_oneShotIsAttack)) return;
                _oneShotUntil = 0f;
            }
            if (want != _base)
            {
                Anim.Play(want, true);
                _base = want;
            }
            if (want == AnimWalk || want == AnimRun)
            {
                float ratio = 1f;
                var m = Anim.Model;
                int seq = m != null ? m.FindSequence(want) : -1;
                float natural = seq >= 0 ? m.Sequences[seq].MoveSpeed : 0f;
                if (natural > 0.1f && _speedYd > 0.2f) ratio = Mathf.Clamp(_speedYd / (natural * TotalScale), 0.5f, 2f);
                Anim.Speed = ratio;
            }
            else if (!Mathf.Approximately(Anim.Speed, 1f)) Anim.Speed = 1f;
        }

        private float _builtAt = -10f;

        public void MarkBuilt(float now) { _builtAt = now; }

        private void StartDeath(bool fast)
        {
            _deadShown = true;
            _holding = false;
            _oneShotUntil = 0f;
            _base = -1;
            ApplyBox(true);
            if (!Anim.Has(AnimDeath)) { Anim.Speed = 1f; _holding = true; return; }
            // Played looped (Play(id, false) would return to Stand) and the clock is stopped just before the end, so the
            // corpse keeps the last frame. A unit that is already dead when its view appears gets there at 20x.
            float speed = fast ? 20f : 1f;
            Anim.Speed = speed;
            Anim.Play(AnimDeath, true);
            var m = Anim.Model;
            int seq = m.FindSequence(AnimDeath);
            _deathDur = seq >= 0 ? m.Sequences[seq].Duration : 1000f;
            _deathT = Time.deltaTime * 1000f * speed;   // M2Animator adds this frame's time in its LateUpdate
        }

        private void TickDeath(float dt)
        {
            if (_holding) return;
            float next = _deathT + dt * 1000f * Anim.Speed;
            if (next >= _deathDur - 5f) { Anim.Speed = 0f; _holding = true; }
            else _deathT = next;
        }

        // ------------------------------------------------------------------ quest marker

        /// <summary>Rebuilds the "!" / "?" model when the entity's marker changes (WoW's own TalkToMe models).</summary>
        public void UpdateMarker(WowFiles files)
        {
            var want = State.Dead && State.Kind != EntityKind.Npc ? QuestMarker.None : State.Marker;   // iteration 11 (quests H-E2): a corpse-looking quest NPC shows its '!'
            if (want == MarkerKind && (want == QuestMarker.None || MarkerGo != null)) return;
            MarkerKind = want;
            if (MarkerGo != null) { UnityEngine.Object.Destroy(MarkerGo); MarkerGo = null; }
            if (want == QuestMarker.None || files == null) return;
            string path = want == QuestMarker.Available ? WowEntityArt.MarkerAvailable
                : want == QuestMarker.Complete ? WowEntityArt.MarkerComplete : WowEntityArt.MarkerIncomplete;
            try { MarkerGo = M2Factory.Build(files, path, new M2BuildOptions { Name = "Quest marker" }); }
            catch (Exception e) { WowLog.Warn("Entities: quest marker " + path + " threw: " + e.Message); MarkerGo = null; }
            if (MarkerGo == null) return;
            MarkerGo.transform.SetParent(Root.transform, false);
            MarkerGo.transform.localRotation = Quaternion.identity;
            MarkerGo.transform.localScale = Vector3.one * 0.4f;
        }
    }
}
