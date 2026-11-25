using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Spell visuals from the client's own DBCs (pure, thread-safe after Get). Layouts: WoWDBDefs definitions whose BUILD
    /// covers 3.3.5.12340 (SpellVisual.dbd, SpellVisualKit.dbd "3.1.0.9767-3.3.5.12340", SpellVisualEffectName.dbd
    /// "3.0.1.8820-3.3.5.12340"), VERIFIED on the real files by the harness probe VfxSpellVisual (field counts, record
    /// sizes, every kit / effect id resolving, every effect model existing; see the VFX report).
    /// Spell.dbc: SpellVisual[2] at fields 131/132 (AzerothCore DBCStructure.h SpellEntry: after
    /// EffectSpellClassMaskA/B/C 122..130, before SpellIconID 133), Speed 47, AttributesEx 5, DurationIndex 40.
    /// </summary>
    public sealed class SpellVisualDb
    {
        public static class SpellVisualF
        {
            public const int FieldCount = 32;
            public const int Id = 0, PrecastKit = 1, CastKit = 2, ImpactKit = 3, StateKit = 4, StateDoneKit = 5, ChannelKit = 6,
                             HasMissile = 7, MissileModel = 8, MissilePathType = 9, MissileDestinationAttachment = 10, MissileSound = 11,
                             AnimEventSoundID = 12, Flags = 13, CasterImpactKit = 14, TargetImpactKit = 15, MissileAttachment = 16,
                             MissileFollowGroundHeight = 17, MissileFollowGroundDropSpeed = 18, MissileFollowGroundApproach = 19,
                             MissileFollowGroundFlags = 20, MissileMotion = 21, MissileTargetingKit = 22, InstantAreaKit = 23,
                             ImpactAreaKit = 24, PersistentAreaKit = 25, MissileCastOffset = 26, MissileImpactOffset = 29;
        }

        public static class SpellVisualKitF
        {
            public const int FieldCount = 38;
            public const int Id = 0, StartAnimID = 1, AnimID = 2, HeadEffect = 3, ChestEffect = 4, BaseEffect = 5, LeftHandEffect = 6,
                             RightHandEffect = 7, BreathEffect = 8, LeftWeaponEffect = 9, RightWeaponEffect = 10, SpecialEffect = 11,
                             WorldEffect = 14, SoundID = 15, ShakeID = 16, CharProc = 17, CharParamZero = 21, CharParamOne = 25,
                             CharParamTwo = 29, CharParamThree = 33, Flags = 37;
        }

        public static class SpellVisualEffectNameF
        {
            public const int FieldCount = 7;
            public const int Id = 0, Name = 1, FileName = 2, AreaEffectSize = 3, Scale = 4, MinAllowedScale = 5, MaxAllowedScale = 6;
        }

        public const int SpellVisual1 = 131, SpellVisual2 = 132, SpellSpeed = 47, SpellAttributesEx = 5, SpellDurationIndex = 40;

        /// <summary>
        /// The kit's effect slots and the M2 attachment each is drawn at (wowdev DB/SpellVisualKit: "attached to Head / Chest /
        /// Base / SpellLeftHand / SpellRightHand / Breath / Special1..3"; attachment ids from the wowdev attachment table,
        /// matching the ids the Models report measured on HumanMale). Weapon slots draw on the hands; World = at the unit's feet.
        /// </summary>
        public enum Slot { Head = 0, Chest = 1, Base = 2, LeftHand = 3, RightHand = 4, Breath = 5, LeftWeapon = 6, RightWeapon = 7, Special1 = 8, Special2 = 9, Special3 = 10, World = 11 }

        public static readonly int[] SlotAttachment = { 20, 34, 19, 21, 22, 17, 2, 1, 23, 24, 25, 19 };
        /// <summary>Fallback attachment per slot when a model lacks the first choice (e.g. creature models without 34 Chest).</summary>
        public static readonly int[] SlotFallback = { 11, 15, -1, 2, 1, 20, 21, 22, 18, 18, 18, -1 };

        public sealed class Effect
        {
            public int Id;
            public string Name;
            public string ModelPath;   // .m2 path (legacy .mdx names normalised)
            public float Scale = 1f;
            public float AreaEffectSize;
        }

        public sealed class Kit
        {
            public int Id;
            public int StartAnim = -1, Anim = -1;   // AnimationData ids for the unit (-1 none)
            public readonly Effect[] Slots = new Effect[12];   // by Slot, null = none
            public int SoundId;
            public int Flags;
            // iteration 11 (spellfx): SpellVisualKit CharProc[4] (field 17..20) and its raw float params (CharParamZero..Three, fields 21-36).
            // CharProc 9 = the area model spawner (CharParamZero[0] = the SpellVisualKitAreaModel index, CharParamOne[0] = its rate - read
            // as models per second, OURS); CharProc 14 = model alpha (Stealth 312, Invisibility 3450 - ours).
            public readonly int[] CharProc = new int[4];
            public readonly float[] CharParam0 = new float[4], CharParam1 = new float[4], CharParam2 = new float[4], CharParam3 = new float[4];
            public bool HasEffects { get { for (int i = 0; i < Slots.Length; i++) if (Slots[i] != null) return true; return false; } }
        }

        public sealed class Visual
        {
            public int Id;
            public Kit Precast, Cast, Impact, State, StateDone, Channel, CasterImpact, TargetImpact;
            // iteration 11 (spellfx): the place kits (fields 22-25) - a spell aimed at the ground (InstantArea / ImpactArea at the place) and a
            // ground area (PersistentArea while the dynamic object exists).
            public Kit MissileTargeting, InstantArea, ImpactArea, PersistentArea;
            public bool HasMissile;
            public Effect Missile;
            public int MissileAttachment = -1, MissileDestinationAttachment = -1;
            public int MissileSound;
            public int MissileMotion;
            public float[] MissileCastOffset = new float[3], MissileImpactOffset = new float[3];
        }

        /// <summary>What the VFX code needs of a spell.</summary>
        public sealed class SpellFx
        {
            public int SpellId;
            public Visual Visual;          // SpellVisual[0] (null when none)
            public float Speed;            // yards per second, 0 = instant impact
            public bool Channeled;         // AttributesEx CHANNELED_1 (0x4) / CHANNELED_2 (0x40)
            public int DurationMs;         // SpellDuration base (-1 = until cancelled)
        }

        readonly DbcTable _spell, _visual, _kit, _effect, _duration;
        readonly object _lock = new object();
        readonly Dictionary<int, Visual> _visuals = new Dictionary<int, Visual>();
        readonly Dictionary<int, Kit> _kits = new Dictionary<int, Kit>();
        readonly Dictionary<int, Effect> _effects = new Dictionary<int, Effect>();
        readonly Dictionary<int, SpellFx> _spells = new Dictionary<int, SpellFx>();

        static readonly object CacheLock = new object();
        static SpellVisualDb _cached;
        static WowFiles _cachedFiles;

        public bool Ok { get { return _visual != null && _kit != null && _effect != null; } }
        public string Summary { get; private set; }

        readonly WowFiles _files;   // iteration 11 (spellfx): for the lazily read SpellVisualKitAreaModel

        SpellVisualDb(WowFiles files)
        {
            _files = files;
            if (files == null) { Summary = "no game files"; return; }
            _spell = Dbc.Load(files, "Spell");
            _visual = Dbc.Load(files, "SpellVisual");
            _kit = Dbc.Load(files, "SpellVisualKit");
            _effect = Dbc.Load(files, "SpellVisualEffectName");
            _duration = Dbc.Load(files, "SpellDuration");
            var bad = new List<string>();
            if (_visual != null && _visual.FieldCount != SpellVisualF.FieldCount) bad.Add("SpellVisual " + _visual.FieldCount + " fields (expected " + SpellVisualF.FieldCount + ")");
            if (_kit != null && _kit.FieldCount != SpellVisualKitF.FieldCount) bad.Add("SpellVisualKit " + _kit.FieldCount + " fields (expected " + SpellVisualKitF.FieldCount + ")");
            if (_effect != null && _effect.FieldCount != SpellVisualEffectNameF.FieldCount) bad.Add("SpellVisualEffectName " + _effect.FieldCount + " fields (expected " + SpellVisualEffectNameF.FieldCount + ")");
            if (bad.Count > 0)
            {
                WowLog.Warn("SpellVisualDb: unexpected layout (" + string.Join(", ", bad) + ") - spell visuals off");
                _visual = null;
            }
            Summary = "SpellVisual " + (_visual != null ? _visual.RecordCount.ToString() : "missing") + ", SpellVisualKit " + (_kit != null ? _kit.RecordCount.ToString() : "missing")
                + ", SpellVisualEffectName " + (_effect != null ? _effect.RecordCount.ToString() : "missing") + ", Spell " + (_spell != null ? _spell.RecordCount.ToString() : "missing");
        }

        /// <summary>The database of these files (cached). Any thread; the first call reads the DBCs.</summary>
        public static SpellVisualDb Get(WowFiles files)
        {
            lock (CacheLock) { if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached; }
            var db = new SpellVisualDb(files);
            lock (CacheLock) { _cached = db; _cachedFiles = files; }
            return db;
        }

        /// <summary>A spell's visual facts (cached), or null when the spell or its visual is unknown.</summary>
        public SpellFx Spell(int spellId)
        {
            if (_spell == null || spellId <= 0) return null;
            lock (_lock) { SpellFx c; if (_spells.TryGetValue(spellId, out c)) return c; }
            SpellFx fx = null;
            int r = _spell.FindRow(spellId);
            if (r >= 0)
            {
                fx = new SpellFx { SpellId = spellId };
                fx.Visual = VisualById(_spell.GetInt(r, SpellVisual1));
                fx.Speed = _spell.GetFloat(r, SpellSpeed);
                if (float.IsNaN(fx.Speed) || fx.Speed < 0f) fx.Speed = 0f;
                uint ex = _spell.GetUInt(r, SpellAttributesEx);
                fx.Channeled = (ex & 0x4u) != 0 || (ex & 0x40u) != 0;
                fx.DurationMs = 0;
                int di = _spell.GetInt(r, SpellDurationIndex);
                if (_duration != null && di > 0)
                {
                    int dr = _duration.FindRow(di);
                    if (dr >= 0) fx.DurationMs = _duration.GetInt(dr, 1);
                }
            }
            lock (_lock) _spells[spellId] = fx;
            return fx;
        }

        public Visual VisualById(int id)
        {
            if (_visual == null || id <= 0) return null;
            lock (_lock) { Visual c; if (_visuals.TryGetValue(id, out c)) return c; }
            Visual v = null;
            int r = _visual.FindRow(id);
            if (r >= 0)
            {
                var t = _visual;
                v = new Visual { Id = id };
                v.Precast = KitById(t.GetInt(r, SpellVisualF.PrecastKit));
                v.Cast = KitById(t.GetInt(r, SpellVisualF.CastKit));
                v.Impact = KitById(t.GetInt(r, SpellVisualF.ImpactKit));
                v.State = KitById(t.GetInt(r, SpellVisualF.StateKit));
                v.StateDone = KitById(t.GetInt(r, SpellVisualF.StateDoneKit));
                v.Channel = KitById(t.GetInt(r, SpellVisualF.ChannelKit));
                v.CasterImpact = KitById(t.GetInt(r, SpellVisualF.CasterImpactKit));
                v.TargetImpact = KitById(t.GetInt(r, SpellVisualF.TargetImpactKit));
                v.MissileTargeting = KitById(t.GetInt(r, SpellVisualF.MissileTargetingKit));   // iteration 11 (spellfx)
                v.InstantArea = KitById(t.GetInt(r, SpellVisualF.InstantAreaKit));
                v.ImpactArea = KitById(t.GetInt(r, SpellVisualF.ImpactAreaKit));
                v.PersistentArea = KitById(t.GetInt(r, SpellVisualF.PersistentAreaKit));
                v.HasMissile = t.GetInt(r, SpellVisualF.HasMissile) != 0;
                v.Missile = EffectById(t.GetInt(r, SpellVisualF.MissileModel));
                v.MissileAttachment = t.GetInt(r, SpellVisualF.MissileAttachment);
                v.MissileDestinationAttachment = t.GetInt(r, SpellVisualF.MissileDestinationAttachment);
                v.MissileSound = t.GetInt(r, SpellVisualF.MissileSound);
                v.MissileMotion = t.GetInt(r, SpellVisualF.MissileMotion);
                for (int i = 0; i < 3; i++)
                {
                    v.MissileCastOffset[i] = t.GetFloat(r, SpellVisualF.MissileCastOffset + i);
                    v.MissileImpactOffset[i] = t.GetFloat(r, SpellVisualF.MissileImpactOffset + i);
                }
            }
            lock (_lock) _visuals[id] = v;
            return v;
        }

        public Kit KitById(int id)
        {
            if (_kit == null || id <= 0) return null;
            lock (_lock) { Kit c; if (_kits.TryGetValue(id, out c)) return c; }
            Kit k = null;
            int r = _kit.FindRow(id);
            if (r >= 0)
            {
                var t = _kit;
                k = new Kit { Id = id };
                k.StartAnim = t.GetInt(r, SpellVisualKitF.StartAnimID);
                k.Anim = t.GetInt(r, SpellVisualKitF.AnimID);
                int[] fields = { SpellVisualKitF.HeadEffect, SpellVisualKitF.ChestEffect, SpellVisualKitF.BaseEffect, SpellVisualKitF.LeftHandEffect,
                                 SpellVisualKitF.RightHandEffect, SpellVisualKitF.BreathEffect, SpellVisualKitF.LeftWeaponEffect, SpellVisualKitF.RightWeaponEffect,
                                 SpellVisualKitF.SpecialEffect, SpellVisualKitF.SpecialEffect + 1, SpellVisualKitF.SpecialEffect + 2, SpellVisualKitF.WorldEffect };
                for (int i = 0; i < fields.Length; i++) k.Slots[i] = EffectById(t.GetInt(r, fields[i]));
                k.SoundId = t.GetInt(r, SpellVisualKitF.SoundID);
                k.Flags = t.GetInt(r, SpellVisualKitF.Flags);
                for (int i = 0; i < 4; i++)   // iteration 11 (spellfx)
                {
                    k.CharProc[i] = t.GetInt(r, SpellVisualKitF.CharProc + i);
                    k.CharParam0[i] = t.GetFloat(r, SpellVisualKitF.CharParamZero + i);
                    k.CharParam1[i] = t.GetFloat(r, SpellVisualKitF.CharParamOne + i);
                    k.CharParam2[i] = t.GetFloat(r, SpellVisualKitF.CharParamTwo + i);
                    k.CharParam3[i] = t.GetFloat(r, SpellVisualKitF.CharParamThree + i);
                }
            }
            lock (_lock) _kits[id] = k;
            return k;
        }

        public Effect EffectById(int id)
        {
            if (_effect == null || id <= 0) return null;
            lock (_lock) { Effect c; if (_effects.TryGetValue(id, out c)) return c; }
            Effect e = null;
            int r = _effect.FindRow(id);
            if (r >= 0)
            {
                string file = _effect.GetString(r, SpellVisualEffectNameF.FileName);
                if (!string.IsNullOrEmpty(file))
                {
                    e = new Effect { Id = id, Name = _effect.GetString(r, SpellVisualEffectNameF.Name), ModelPath = M2Model.NormalizeModelPath(file) };
                    float s = _effect.GetFloat(r, SpellVisualEffectNameF.Scale);
                    e.Scale = (s > 0.001f && s < 100f) ? s : 1f;
                    e.AreaEffectSize = _effect.GetFloat(r, SpellVisualEffectNameF.AreaEffectSize);
                }
            }
            lock (_lock) _effects[id] = e;
            return e;
        }

        // iteration 11 (spellfx): SpellVisualKitAreaModel.dbc (3 fields: ID, ModelName, field 2 = the index a kit's CharProc 9 names - measured 15 rows:
        // 0 Blizzard_Impact_Base, 1 RainOfFire_Impact_Base, 2 CallLightning_Impact, 4 DeathAndDecay_Area_Base, 5 ArcaneShot_Area, 9 DeathAndDecay ...).
        DbcTable _areaModels;
        bool _areaModelsTried;
        readonly Dictionary<int, string> _areaModelByIndex = new Dictionary<int, string>();

        /// <summary>The area model of a CharProc 9 index (an .m2 path), or null.</summary>
        public string AreaModel(int index)
        {
            if (index < 0) return null;
            lock (_lock)
            {
                string c;
                if (_areaModelByIndex.TryGetValue(index, out c)) return c;
                if (!_areaModelsTried)
                {
                    _areaModelsTried = true;
                    _areaModels = _files != null ? Dbc.Load(_files, "SpellVisualKitAreaModel") : null;
                    if (_areaModels != null && _areaModels.FieldCount != 3)
                    {
                        WowLog.Warn("SpellVisualDb: SpellVisualKitAreaModel has " + _areaModels.FieldCount + " fields (expected 3) - area models off");
                        _areaModels = null;
                    }
                }
                string path = null;
                if (_areaModels != null)
                {
                    int r = _areaModels.FindRowWhere(2, index);
                    if (r >= 0)
                    {
                        string file = _areaModels.GetString(r, 1);
                        if (!string.IsNullOrEmpty(file)) path = M2Model.NormalizeModelPath(file);
                    }
                }
                _areaModelByIndex[index] = path;
                return path;
            }
        }

        /// <summary>Effect rows whose model file name contains the text (case-insensitive); for lookups like the level-up model.</summary>
        public List<Effect> FindEffects(string fileContains)
        {
            var list = new List<Effect>();
            if (_effect == null || string.IsNullOrEmpty(fileContains)) return list;
            for (int r = 0; r < _effect.RecordCount; r++)
            {
                string file = _effect.GetString(r, SpellVisualEffectNameF.FileName);
                if (file != null && file.IndexOf(fileContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var e = EffectById(_effect.GetId(r));
                    if (e != null) list.Add(e);
                }
            }
            return list;
        }
    }
}
