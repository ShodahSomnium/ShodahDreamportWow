using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (lead contract stub, PLAN7 s3.4; bodies by Rules). Every summonable mount of the player's own Spell.dbc: the spells of
    // SkillLineAbility skill line 777 "Mounts" that carry aura 78 SPELL_AURA_MOUNTED (309 in 3.3.5a build 12340, measured by the data
    // scout and re-checked by the rules7 probe MountCatalogCensus). Nothing is typed by hand except AzerothCore's ids and flags.
    public sealed class MountInfo
    {
        public int SpellId, CreatureEntry, CastMs, AreaGroupId, ClassMask, RaceMask, RequiredRiding;
        public int GroundPct, FlightPct, SwimPct;      // aura 32 / 207 / 58 values (BasePoints + DieSides), 0 = none
        public string Name, IconPath, Description;
        public MountKind Kind;                          // Flying when FlightPct > 0, else Ground
        public bool OutdoorsOnly, OnlyFlyingAreas, HordeOnly, AllianceOnly;
        public uint InterruptFlags, AuraInterruptFlags;
        public string Excluded;                         // null = offered offline; else the reason (s4.1)
    }

    public static class MountCatalog
    {
        public const int SkillRiding = 762, SkillMounts = 777, AuraMounted = 78, AuraMountedSpeed = 32, AuraMountedFlightSpeed = 207, AuraSwimSpeed = 58;
        public const int ApprenticeRiding = 33388, JourneymanRiding = 33391, ExpertRiding = 34090, ArtisanRiding = 34091, ColdWeatherFlying = 54197;
        public const float BaseWalk = 2.5f, BaseRun = 7f, BaseRunBack = 4.5f, BaseSwim = 4.722222f, BaseSwimBack = 2.5f,
                           BaseFlight = 7f, BaseFlightBack = 4.5f, BaseTurnRate = 3.141594f;   // AC Unit.cpp playerBaseMoveSpeed

        // Spell.dbc field of AreaGroupId (not in SpellFields; rule 12): AzerothCore DBCStructure.h SpellEntry
        // "int32 AreaGroupId; // 224 m_requiredAreaGroupId", measured by the data scout (MountData.cs `t.GetInt(r, 224)`: the 4 Qiraji tanks
        // read 38, every other mount 0) and re-read by the rules7 census.
        private const int SpellAreaGroupIdField = 224;
        // SPELL_ATTR0_* / SPELL_ATTR4_* / SPELL_ATTR7_* (AzerothCore SharedDefines.h).
        private const uint Attr0OnlyOutdoors = 0x00008000u, Attr4OnlyFlyingAreas = 0x04000000u, Attr7HordeOnly = 0x00000100u, Attr7AllianceOnly = 0x00000200u;
        // AreaGroup 38 = area 3428 "Ahn'Qiraj" only (data scout: MountAreas 38 -> 3428); the 4 Qiraji Battle Tanks carry it.
        private const int AreaGroupAhnQiraj = 38;

        private sealed class Catalog
        {
            public readonly List<MountInfo> All = new List<MountInfo>();
            public readonly Dictionary<int, MountInfo> ById = new Dictionary<int, MountInfo>();
            public string Summary = "not loaded";
        }

        private static readonly object Gate = new object();
        private static WowFiles _files;
        private static volatile Catalog _cat;

        public static bool Loaded { get { var c = _cat; return c != null && c.All.Count > 0; } }

        public static string Summary { get { var c = _cat; return c != null ? c.Summary : "not loaded"; } }

        /// <summary>Worker thread, cached per WowFiles, one log line: Spell.dbc + SkillLineAbility (777) + SpellIcon + SpellCastTimes.</summary>
        public static void Load(WowFiles files)
        {
            if (files == null) return;
            lock (Gate)   // a second caller (the session constructor while the worker still loads) waits here instead of reading twice
            {
                if (_cat != null && ReferenceEquals(_files, files)) return;
                Catalog c;
                try { c = Build(files); }
                catch (Exception e)
                {
                    c = new Catalog();
                    c.Summary = "failed (" + e.GetType().Name + ": " + e.Message + ")";
                    WowLog.Error("MountCatalog: reading the mounts failed: " + e);
                }
                _files = files;
                _cat = c;
            }
        }

        public static System.Collections.Generic.IReadOnlyList<MountInfo> All
        {
            get { var c = _cat; return c != null ? (IReadOnlyList<MountInfo>)c.All : new MountInfo[0]; }
        }

        public static MountInfo Find(int spellId)        // null = not a Mounts-line spell
        {
            var c = _cat;
            MountInfo m;
            return c != null && c.ById.TryGetValue(spellId, out m) ? m : null;
        }

        public static bool IsMount(int spellId) { return Find(spellId) != null; }

        /// <summary>The testing set: Excluded == null, the character's faction (attr7 0x100 Horde / 0x200 Alliance), its class (SkillLineAbility ClassMask).</summary>
        public static System.Collections.Generic.List<MountInfo> ForCharacter(WowFaction faction, WowClass cls, int race)
        {
            var list = new List<MountInfo>();
            var c = _cat;
            if (c == null) return list;
            int classBit = cls == WowClass.None ? 0 : 1 << ((int)cls - 1);
            int raceBit = race > 0 && race <= 32 ? 1 << (race - 1) : 0;
            foreach (var m in c.All)
            {
                if (m.Excluded != null) continue;
                if (m.HordeOnly && faction != WowFaction.Horde) continue;
                if (m.AllianceOnly && faction != WowFaction.Alliance) continue;
                if (m.ClassMask != 0 && (m.ClassMask & classBit) == 0) continue;   // class mounts: SkillLineAbility ClassMask (Warhorse 0x2 = Paladin)
                if (m.RaceMask != 0 && (m.RaceMask & raceBit) == 0) continue;      // and its RaceMask (Warhorse 0x405 = Human, Dwarf, Draenei)
                list.Add(m);
            }
            list.Sort(ByName);
            return list;
        }

        public static float RunSpeed(int groundPct) { return BaseRun * (1f + groundPct / 100f); }

        public static float FlightSpeed(int flightPct) { return BaseFlight * (1f + flightPct / 100f); }

        // ------------------------------------------------------------------ reading

        private static int ByName(MountInfo a, MountInfo b)
        {
            int k = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return k != 0 ? k : a.SpellId.CompareTo(b.SpellId);
        }

        private static Catalog Build(WowFiles files)
        {
            var t0 = DateTime.UtcNow;
            var c = new Catalog();
            var db = SpellDb.Get(files);
            var spell = Dbc.Load(files, "Spell");
            var sla = Dbc.Load(files, "SkillLineAbility");
            if (!db.Available || spell == null || sla == null)
            {
                c.Summary = "no " + (spell == null ? "Spell.dbc" : "SkillLineAbility.dbc") + " - no mounts";
                WowLog.Warn("MountCatalog: " + c.Summary);
                return c;
            }

            // The mounts = the spells of line 777. Who may ride them: the ClassMask / RaceMask of EVERY SkillLineAbility row of the spell,
            // OR-ed over the rows that set one - class mounts carry their masks on the class-line row (measured: Felsteed 5784 line 354
            // class 0x100 while its 777 row says 0; Warhorse 13819 lines 594 and 777 both class 0x2 race 0x405; Acherus Deathcharger 48778
            // line 772 class 0x20).
            var classMask = new Dictionary<int, int>();
            var raceMask = new Dictionary<int, int>();
            var order = new List<int>();
            var seen = new HashSet<int>();
            for (int r = 0; r < sla.RecordCount; r++)
            {
                int sp = sla.GetInt(r, SpellFields.SkillLineAbility.Spell);
                int cm = sla.GetInt(r, SpellFields.SkillLineAbility.ClassMask), rm = sla.GetInt(r, SpellFields.SkillLineAbility.RaceMask);
                int old;
                classMask[sp] = (classMask.TryGetValue(sp, out old) ? old : 0) | cm;
                raceMask[sp] = (raceMask.TryGetValue(sp, out old) ? old : 0) | rm;
                if (sla.GetInt(r, SpellFields.SkillLineAbility.SkillLine) == SkillMounts && seen.Add(sp)) order.Add(sp);
            }

            int missing = 0, noAura = 0, ground = 0, flying = 0, scaled = 0, noSpeed = 0, excluded = 0;
            foreach (int id in order)
            {
                var s = db.Find(id);
                if (s == null) { missing++; continue; }                    // 62140 is in SkillLineAbility 777 but not in Spell.dbc (measured)
                SpellEffectInfo mounted = null;
                int groundPct = 0, flightPct = 0, swimPct = 0;
                bool script = false, summonSame = false;
                for (int i = 0; i < 3; i++)
                {
                    var e = s.Effects[i];
                    if (e == null || !e.IsAura || e.Aura != AuraMounted) continue;
                    mounted = e;
                    break;
                }
                if (mounted == null) { noAura++; continue; }                 // 42781 "Upper Deck - Spectral Tiger Mount" (quest-complete effect)
                for (int i = 0; i < 3; i++)
                {
                    var e = s.Effects[i];
                    if (e == null) continue;
                    if (e.IsAura)
                    {
                        int v = e.BasePoints + (e.DieSides > 0 ? e.DieSides : 0);   // DieSides is 1 on every speed aura (min == max, measured)
                        if (e.Aura == AuraMountedSpeed) groundPct = Math.Max(groundPct, v);
                        else if (e.Aura == AuraMountedFlightSpeed) flightPct = Math.Max(flightPct, v);
                        else if (e.Aura == AuraSwimSpeed) swimPct = Math.Max(swimPct, v);
                    }
                    if (e.Effect == SpellEffect.ScriptEffect) script = true;
                    if (e.Effect == SpellEffect.Summon && e.MiscValue == mounted.MiscValue) summonSame = true;
                }
                int row = spell.FindRow(id);
                var m = new MountInfo();
                m.SpellId = id;
                m.CreatureEntry = mounted.MiscValue;                         // a SERVER creature entry (458 Brown Horse -> 284), not a display
                m.CastMs = s.CastTimeMs;
                m.AreaGroupId = row >= 0 && spell.FieldCount > SpellAreaGroupIdField ? spell.GetInt(row, SpellAreaGroupIdField) : 0;
                m.ClassMask = classMask[id];
                m.RaceMask = raceMask[id];
                m.GroundPct = groundPct;
                m.FlightPct = flightPct;
                m.SwimPct = swimPct;
                m.Name = s.Name ?? "";
                m.IconPath = s.IconPath ?? "";                               // SpellIcon path without ".blp", as SpellDb / AbilityView keep it
                try { m.Description = SpellText.Resolve(db, s, s.Description, new SpellTextContext()); }
                catch (Exception) { m.Description = s.Description ?? ""; }
                m.Kind = flightPct > 0 ? MountKind.Flying : MountKind.Ground;
                m.OutdoorsOnly = (s.Attributes[0] & Attr0OnlyOutdoors) != 0;
                m.OnlyFlyingAreas = (s.Attributes[4] & Attr4OnlyFlyingAreas) != 0;
                m.HordeOnly = (s.Attributes[7] & Attr7HordeOnly) != 0;
                m.AllianceOnly = (s.Attributes[7] & Attr7AllianceOnly) != 0;
                m.InterruptFlags = (uint)s.InterruptFlags;
                m.AuraInterruptFlags = (uint)s.AuraInterruptFlags;
                m.RequiredRiding = RequiredRidingOf(id, groundPct, flightPct);

                if (flightPct > 0) flying++;
                else if (groundPct > 0) ground++;
                else if (script) scaled++;
                else noSpeed++;

                // Offline exclusions (PLAN7 s4.1; listed in the Mounts window footer and the README).
                if (flightPct == 0 && groundPct == 0 && script) m.Excluded = "riding-scaled script mount - not in this iteration";   // AC spell_gen_mount picks another spell by riding skill
                else if (summonSame) m.Excluded = "vehicle mount - not in this iteration";   // aura 78 + SUMMON of the same entry: AC sets display 0 (a vehicle)
                else if (m.AreaGroupId == AreaGroupAhnQiraj) m.Excluded = "Ahn'Qiraj only";
                else if (m.AreaGroupId > 0) m.Excluded = "only in certain areas (AreaGroup " + m.AreaGroupId + ") - not in this iteration";
                if (m.Excluded != null) excluded++;

                c.All.Add(m);
                c.ById[id] = m;
            }
            c.All.Sort((a, b) => a.SpellId.CompareTo(b.SpellId));
            int ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
            c.Summary = c.All.Count + " mounts (" + ground + " ground, " + flying + " flying, " + scaled + " riding-scaled scripts, " + noSpeed + " without a speed aura; "
                + excluded + " not offered offline) from Spell.dbc + SkillLineAbility 777"
                + (missing > 0 ? " (" + missing + " listed spell" + (missing == 1 ? "" : "s") + " not in Spell.dbc)" : "")
                + (noAura > 0 ? " (" + noAura + " without aura 78 skipped)" : "") + ", " + ms + " ms";
            WowLog.Info("MountCatalog: " + c.Summary);
            return c;
        }

        /// <summary>The riding skill AzerothCore's item_template asks for the item that teaches the mount (data scout census, PLAN7 s4.1):
        /// 60 % ground 75, 100 % ground 150, 150 % flying 225, 280 / 310 % flying 300. The two turtles have no speed aura: Riding Turtle
        /// (item 23720, RequiredSkillRank 0) and Sea Turtle (item 46109, RequiredSkill 762 rank 75) - quoted from acsrc/item_template.sql.
        /// Informational: AzerothCore's Spell::CheckCast has no riding check (the gate is learning).</summary>
        private static int RequiredRidingOf(int spellId, int groundPct, int flightPct)
        {
            if (flightPct >= 280) return 300;
            if (flightPct > 0) return 225;
            if (groundPct >= 100) return 150;
            if (groundPct > 0) return 75;
            if (spellId == 64731) return 75;                                  // Sea Turtle (item 46109)
            return 0;                                                         // Riding Turtle (item 23720) and the excluded ones
        }
    }
}
