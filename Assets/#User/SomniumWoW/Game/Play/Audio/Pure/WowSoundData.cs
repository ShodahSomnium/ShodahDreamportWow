using System;
using System.Collections.Generic;
using System.Diagnostics;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>One SoundEntries.dbc row: its files (full game paths), weights, volume and distances (yards).</summary>
    public sealed class SoundEntryInfo
    {
        public int Id;
        public int Type;
        public string Name;
        public string[] Files;
        public int[] Weights;
        public int WeightSum;
        public float Volume = 1f;
        public int Flags;
        public float MinDistance = 8f;
        public float Cutoff = 45f;

        /// <summary>
        /// Iteration 11 (audio fix): SoundEntries.Flags 0x200 marks the loop files. AzerothCore names the field only (DBCStructure.h
        /// SoundEntriesEntry "25 m_flags"), so the bit was measured (harness AudLoopFlags / AudKitSounds / AudSpellSounds, 12941 rows):
        /// 1218 rows carry it, 312 of the 385 rows whose name or file says "loop"; every precast and channel sound of the common spells
        /// looked at carries it ('Precast Fire Low' PreCastFireLow.wav 5.1 s, 'Precast Shadow Low' 6.6 s, 'LifeDrainLoop' 4.9 s,
        /// 'MissileFireLoop' 4.0 s) while their cast / impact sounds do not ('Fire Cast' 0.8 s, 'Shadow Cast', 'Holy Cast' flags 0).
        /// Only a sound that has a reason to last (WowAudio.PlaySoundEntryLoop) is looped by it; a plain one-shot never is.
        /// </summary>
        public const int FlagLoop = 0x200;
        public bool Loops { get { return (Flags & FlagLoop) != 0; } }

        /// <summary>A random file by the Freq weights (null when the entry has no file).</summary>
        public string Pick(Random rng)
        {
            if (Files == null || Files.Length == 0) return null;
            if (Files.Length == 1 || WeightSum <= 0) return Files[rng.Next(Files.Length)];
            int r = rng.Next(WeightSum);
            for (int i = 0; i < Files.Length; i++)
            {
                r -= Weights[i];
                if (r < 0) return Files[i];
            }
            return Files[Files.Length - 1];
        }
    }

    /// <summary>The sounds of one area as the client resolves them (0 fields inherit the parent zone's).</summary>
    public sealed class ZoneSoundInfo
    {
        public int AreaId;
        public string Name = "";
        public int ZoneMusicId, AmbienceId, IntroId;
        public string MusicSetName = "";
        public int MusicDay, MusicNight;                 // SoundEntries ids
        public int SilenceMinDay, SilenceMinNight, SilenceMaxDay, SilenceMaxNight;  // ms
        public int AmbienceDay, AmbienceNight;           // SoundEntries ids
        public int IntroSound, IntroMinDelayMinutes;     // SoundEntries id
    }

    /// <summary>
    /// Every sound table the world uses, read once from the player's files (pure C#; the harness probes it). Layouts are
    /// WoWDBDefs 3.3.5.12340 and were measured on the real files (see P/reports3/audio.md); a table whose field count differs
    /// is not used (logged once) and its sounds stay silent. Build on a worker thread (Load), read from any thread after.
    /// </summary>
    public sealed class WowSoundData
    {
        // SoundEntries.dbc 30 fields (DbcFields.SoundEntries).
        // WeaponSwingSounds2.dbc: ID, SwingType, Crit, SoundID.
        private const int SwingFields = 4;
        // WeaponImpactSounds.dbc: ID, WeaponSubClassID, ParrySoundType, ImpactSoundID[10], CritImpactSoundID[10].
        private const int ImpactFields = 23;
        // ItemSubClass.dbc: ClassID, SubClassID, ..., 9 WeaponSwingSize, DisplayName_lang, VerboseName_lang = 44 fields.
        private const int ItemSubClassFields = 44, IscSwingSize = 9;
        // TerrainType.dbc: TerrainID, TerrainDesc, FootstepSprayRun, FootstepSprayWalk, SoundID, Flags.
        private const int TerrainTypeFields = 6;
        // FootstepTerrainLookup.dbc: ID, CreatureFootstepID, TerrainSoundID, SoundID, SoundIDSplash.
        private const int FootstepFields = 5;
        // GroundEffectTexture.dbc (DbcFields.GroundEffectTexture): field 10 Sound = TerrainType id.
        // CreatureSoundData.dbc 38 fields: 6 SoundDeathID, 9 SoundFootstepID, 25 CreatureImpactType.
        private const int CsdFields = 38, CsdDeath = 6, CsdFootstep = 9, CsdImpactType = 25;
        // NPCSounds.dbc: ID, SoundID[4] (greeting, farewell, pissed, -).
        private const int NpcSoundsFields = 5;
        // VocalUISounds.dbc: ID, VocalUIEnum, RaceID, NormalSoundID[2] (male, female), PissedSoundID[2].
        private const int VocalFields = 7;
        // ZoneMusic.dbc (DbcFields.ZoneMusic) 8, SoundAmbience.dbc: ID, AmbienceID[2] (day, night), ZoneIntroMusicTable.dbc:
        // ID, Name, SoundID, Priority, MinDelayMinutes. ItemGroupSounds.dbc: ID, Sound[4] ([0] pickup, [1] drop).
        private const int AmbienceFields = 3, IntroFields = 5, ItemGroupFields = 5;

        /// <summary>TerrainType ids (TerrainType.dbc TerrainID).</summary>
        public const int TerrainDirt = 0, TerrainMetallic = 1, TerrainStone = 2, TerrainSnow = 3, TerrainWood = 4, TerrainGrass = 5,
            TerrainLeaves = 6, TerrainSand = 7, TerrainSoggy = 8, TerrainDustyGrass = 9, TerrainNone = 10, TerrainWater = 11;

        /// <summary>WeaponImpactSounds slots (measured from the entry names).</summary>
        public const int SlotFlesh = 0, SlotChain = 1, SlotPlate = 2, SlotShieldMetal = 3, SlotShieldWood = 4, SlotParryMetal = 5,
            SlotParryWood = 6, SlotWood = 7, SlotStone = 8, SlotEthereal = 9;

        /// <summary>Item subclass used for unarmed attacks (fist weapons: "Unarmed_Generic").</summary>
        public const int SubclassUnarmed = 13;

        private readonly WowFiles _files;
        private DbcTable _se, _swing, _impact, _isc, _terrain, _foot, _get, _csd, _cdi, _cmd, _npc, _vocal, _area, _zm, _amb, _intro,
            _item, _idi, _igs, _races;
        private readonly Dictionary<int, SoundEntryInfo> _entries = new Dictionary<int, SoundEntryInfo>();
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> _terrainSound = new Dictionary<int, int>();   // TerrainType id -> TerrainTypeSounds id
        private readonly Dictionary<long, int> _footstep = new Dictionary<long, int>();       // (footstep id, sound id) -> row
        private readonly Dictionary<int, ZoneSoundInfo> _zones = new Dictionary<int, ZoneSoundInfo>();
        private readonly object _lock = new object();

        public int SoundEntryCount { get { return _se != null ? _se.RecordCount : 0; } }
        public string Summary { get; private set; }
        public long LoadMs { get; private set; }

        private WowSoundData(WowFiles files) { _files = files; }

        /// <summary>Reads every table (tens of ms; call on a worker thread). Never throws; missing tables leave their sounds silent.</summary>
        public static WowSoundData Load(WowFiles files)
        {
            var sw = Stopwatch.StartNew();
            var d = new WowSoundData(files);
            var missing = new List<string>();
            d._se = d.Table("SoundEntries", DbcFields.SoundEntries.FieldCount, missing);
            d._swing = d.Table("WeaponSwingSounds2", SwingFields, missing);
            d._impact = d.Table("WeaponImpactSounds", ImpactFields, missing);
            d._isc = d.Table("ItemSubClass", ItemSubClassFields, missing);
            d._terrain = d.Table("TerrainType", TerrainTypeFields, missing);
            d._foot = d.Table("FootstepTerrainLookup", FootstepFields, missing);
            d._get = d.Table("GroundEffectTexture", DbcFields.GroundEffectTexture.FieldCount, missing);
            d._csd = d.Table("CreatureSoundData", CsdFields, missing);
            d._cdi = d.Table("CreatureDisplayInfo", DbcFields.CreatureDisplayInfo.FieldCount, missing);
            d._cmd = d.Table("CreatureModelData", DbcFields.CreatureModelData.FieldCount, missing);
            d._npc = d.Table("NPCSounds", NpcSoundsFields, missing);
            d._vocal = d.Table("VocalUISounds", VocalFields, missing);
            d._area = d.Table("AreaTable", DbcFields.AreaTable.FieldCount, missing);
            d._zm = d.Table("ZoneMusic", DbcFields.ZoneMusic.FieldCount, missing);
            d._amb = d.Table("SoundAmbience", AmbienceFields, missing);
            d._intro = d.Table("ZoneIntroMusicTable", IntroFields, missing);
            d._item = d.Table("Item", DbcFields.Item.FieldCount, missing);
            d._idi = d.Table("ItemDisplayInfo", DbcFields.ItemDisplayInfo.FieldCount, missing);
            d._igs = d.Table("ItemGroupSounds", ItemGroupFields, missing);
            d._races = d.Table("ChrRaces", DbcFields.ChrRaces.FieldCount, missing);
            if (d._se != null)
            {
                for (int r = 0; r < d._se.RecordCount; r++)
                {
                    string n = d._se.GetString(r, DbcFields.SoundEntries.Name);
                    if (n.Length > 0 && !d._byName.ContainsKey(n)) d._byName[n] = d._se.GetId(r);
                }
            }
            if (d._terrain != null)
                for (int r = 0; r < d._terrain.RecordCount; r++) d._terrainSound[d._terrain.GetInt(r, 0)] = d._terrain.GetInt(r, 4);
            if (d._foot != null)
                for (int r = 0; r < d._foot.RecordCount; r++)
                {
                    long key = ((long)d._foot.GetInt(r, 1) << 32) | (uint)d._foot.GetInt(r, 2);
                    if (!d._footstep.ContainsKey(key)) d._footstep[key] = r;
                }
            d.LoadMs = sw.ElapsedMilliseconds;
            d.Summary = "SoundEntries " + d.SoundEntryCount + " (" + d._byName.Count + " names), swing " + Rows(d._swing) + ", impact " + Rows(d._impact)
                + ", footstep lookup " + Rows(d._foot) + ", terrain types " + Rows(d._terrain) + ", ZoneMusic " + Rows(d._zm) + ", SoundAmbience " + Rows(d._amb)
                + ", NPCSounds " + Rows(d._npc) + ", VocalUISounds " + Rows(d._vocal) + (missing.Count > 0 ? "; NOT USED: " + string.Join(", ", missing) : "");
            return d;
        }

        private static string Rows(DbcTable t) { return t == null ? "-" : t.RecordCount.ToString(); }

        private DbcTable Table(string name, int fields, List<string> missing)
        {
            DbcTable t = null;
            try { t = Dbc.Load(_files, name); }
            catch (Exception e) { WowLog.Warn("Audio: " + name + ".dbc unreadable: " + e.Message); }
            if (t == null) { missing.Add(name + " (missing)"); return null; }
            if (t.FieldCount != fields)
            {
                WowLog.Warn("Audio: " + name + ".dbc has " + t.FieldCount + " fields, expected " + fields + " (3.3.5a 12340) - its sounds stay off");
                missing.Add(name + " (" + t.FieldCount + " fields)");
                return null;
            }
            return t;
        }

        // ------------------------------------------------------------------------------------------ SoundEntries

        /// <summary>The entry (cached), or null when the id is 0/unknown or it has no file.</summary>
        public SoundEntryInfo Entry(int id)
        {
            if (id <= 0 || _se == null) return null;
            lock (_lock)
            {
                SoundEntryInfo e;
                if (_entries.TryGetValue(id, out e)) return e;
                e = null;
                int r = _se.FindRow(id);
                if (r >= 0)
                {
                    string dir = _se.GetString(r, DbcFields.SoundEntries.DirectoryBase).Trim().TrimEnd('\\', '/');
                    var files = new List<string>(4);
                    var weights = new List<int>(4);
                    for (int k = 0; k < 10; k++)
                    {
                        string f = _se.GetString(r, DbcFields.SoundEntries.File + k).Trim();
                        if (f.Length == 0) continue;
                        files.Add(dir.Length > 0 ? dir + "\\" + f : f);
                        weights.Add(Math.Max(0, _se.GetInt(r, DbcFields.SoundEntries.Freq + k)));
                    }
                    if (files.Count > 0)
                    {
                        e = new SoundEntryInfo
                        {
                            Id = id,
                            Type = _se.GetInt(r, DbcFields.SoundEntries.SoundType),
                            Name = _se.GetString(r, DbcFields.SoundEntries.Name),
                            Files = files.ToArray(),
                            Weights = weights.ToArray(),
                            Flags = _se.GetInt(r, DbcFields.SoundEntries.Flags),
                        };
                        int sum = 0;
                        foreach (int w in e.Weights) sum += w;
                        e.WeightSum = sum;
                        float v = _se.GetFloat(r, DbcFields.SoundEntries.VolumeFloat);
                        e.Volume = v > 0f && v <= 4f ? Math.Min(1f, v) : 1f;
                        float mn = _se.GetFloat(r, DbcFields.SoundEntries.MinDistance), mx = _se.GetFloat(r, DbcFields.SoundEntries.DistanceCutoff);
                        e.MinDistance = mn > 0f && mn < 1000f ? mn : 8f;
                        e.Cutoff = mx > e.MinDistance && mx < 5000f ? mx : Math.Max(45f, e.MinDistance * 4f);
                    }
                }
                _entries[id] = e;
                return e;
            }
        }

        /// <summary>SoundEntries id by its Name ("igMainMenuOption"), 0 when absent.</summary>
        public int IdByName(string name)
        {
            int id;
            return !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out id) ? id : 0;
        }

        // Stage O2 (owner ghost): the client's ghost music and ambience. Both rows are named "(DONOTRENAME)" = the client finds them by
        // name, so they are looked up by name here too (measured, probe GhostFacts2): ZoneMusic 36 'GhostMusic (DONOTRENAME)' (SoundEntries
        // 3820 'Zone Music Day Ghost' / 3821 night, Sound\Music\ZoneMusic\GhostMusic\GhostMusic03.mp3, silence 300-600 s) and SoundEntries
        // 4160 'Ghost (DONOTRENAME)' (Sound\Ambience\GhostState.wav, the loop of SoundAmbience 462).
        public const string GhostMusicSet = "GhostMusic (DONOTRENAME)";
        public const string GhostAmbienceEntry = "Ghost (DONOTRENAME)";
        public const int GhostAreaId = -2;   // ours: the pseudo area the ghost "zone" is filed under
        private ZoneSoundInfo _ghostZone;
        private bool _ghostTried;

        /// <summary>The ghost's music and ambience as a zone (null when the tables lack them).</summary>
        public ZoneSoundInfo GhostZone()
        {
            lock (_lock)
            {
                if (_ghostTried) return _ghostZone;
                _ghostTried = true;
                int mr = -1;
                for (int r = 0; _zm != null && r < _zm.RecordCount && mr < 0; r++)
                    if (string.Equals(_zm.GetString(r, DbcFields.ZoneMusic.SetName), GhostMusicSet, StringComparison.OrdinalIgnoreCase)) mr = r;
                int amb = IdByName(GhostAmbienceEntry);
                if (mr < 0 && amb <= 0) return null;
                var z = new ZoneSoundInfo { AreaId = GhostAreaId, Name = "Ghost" };
                if (mr >= 0)
                {
                    z.ZoneMusicId = _zm.GetId(mr);
                    z.MusicSetName = _zm.GetString(mr, DbcFields.ZoneMusic.SetName);
                    z.SilenceMinDay = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMin);
                    z.SilenceMinNight = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMin + 1);
                    z.SilenceMaxDay = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMax);
                    z.SilenceMaxNight = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMax + 1);
                    z.MusicDay = _zm.GetInt(mr, DbcFields.ZoneMusic.Sounds);
                    z.MusicNight = _zm.GetInt(mr, DbcFields.ZoneMusic.Sounds + 1);
                }
                z.AmbienceDay = z.AmbienceNight = amb;
                _ghostZone = z;
                return z;
            }
        }

        // ------------------------------------------------------------------------------------------ weapons

        /// <summary>Item.dbc facts of a weapon entry. False when the entry is not in Item.dbc (e.g. a made-up item).</summary>
        public bool TryItem(int entry, out int itemClass, out int subclass, out bool metal, out int displayId)
        {
            itemClass = 0; subclass = -1; metal = true; displayId = 0;
            if (_item == null || entry <= 0) return false;
            int r = _item.FindRow(entry);
            if (r < 0) return false;
            itemClass = _item.GetInt(r, DbcFields.Item.ClassID);
            subclass = _item.GetInt(r, DbcFields.Item.SubclassID);
            int over = _item.GetInt(r, DbcFields.Item.Sound_override_subclassID);
            if (over >= 0 && itemClass == 2) subclass = over;
            metal = _item.GetInt(r, DbcFields.Item.Material) != 2;   // 1 metal, 2 wood (measured on the starter weapons)
            displayId = _item.GetInt(r, DbcFields.Item.DisplayInfoID);
            return true;
        }

        /// <summary>ItemSubClass.WeaponSwingSize of a weapon subclass (0 light, 1 medium, 2 heavy); unarmed = 0.</summary>
        public int SwingSize(int weaponSubclass)
        {
            if (weaponSubclass < 0 || weaponSubclass == SubclassUnarmed) return 0;
            if (_isc != null)
            {
                for (int r = 0; r < _isc.RecordCount; r++)
                {
                    if (_isc.GetInt(r, 0) == 2 && _isc.GetInt(r, 1) == weaponSubclass)
                    {
                        int s = _isc.GetInt(r, IscSwingSize);
                        return s < 0 ? 1 : Math.Min(2, s);
                    }
                }
            }
            return 1;
        }

        /// <summary>WeaponSwingSounds2 SoundID for a swing size and crit, 0 when none.</summary>
        public int SwingSound(int swingSize, bool crit)
        {
            if (_swing == null) return 0;
            int fallback = 0;
            for (int r = 0; r < _swing.RecordCount; r++)
            {
                if (_swing.GetInt(r, 1) != swingSize) continue;
                if ((_swing.GetInt(r, 2) != 0) == crit) return _swing.GetInt(r, 3);
                fallback = _swing.GetInt(r, 3);
            }
            return fallback;
        }

        /// <summary>WeaponImpactSounds sound for a weapon subclass (the row whose ParrySoundType matches the material), slot, crit.</summary>
        public int ImpactSound(int weaponSubclass, bool metal, int slot, bool crit)
        {
            if (_impact == null || slot < 0 || slot > 9) return 0;
            int best = -1;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                int sub = pass == 0 ? weaponSubclass : SubclassUnarmed;
                for (int r = 0; r < _impact.RecordCount; r++)
                {
                    if (_impact.GetInt(r, 1) != sub) continue;
                    if (best < 0) best = r;
                    if ((_impact.GetInt(r, 2) != 0) == metal) { best = r; break; }
                }
            }
            if (best < 0) return 0;
            int id = _impact.GetInt(best, (crit ? 13 : 3) + slot);
            if (id <= 0 && crit) id = _impact.GetInt(best, 3 + slot);
            if (id <= 0) id = _impact.GetInt(best, (crit ? 13 : 3) + SlotFlesh);
            return id;
        }

        /// <summary>
        /// A weapon subclass guessed from an NPC's held item model (ItemDisplayInfo ModelName keywords: Sword_2H, Knife/Dagger,
        /// Axe_1H, Mace_2H, Polearm, Stave/Staff, Bow, Gun, Crossbow...). -1 when there is no item. An approximation: displays do
        /// not carry the item subclass.
        /// </summary>
        public int SubclassOfItemDisplay(int itemDisplayId)
        {
            if (itemDisplayId <= 0 || _idi == null) return -1;
            int r = _idi.FindRow(itemDisplayId);
            if (r < 0) return -1;
            string m = _idi.GetString(r, DbcFields.ItemDisplayInfo.ModelName).ToLowerInvariant();
            bool two = m.Contains("_2h");
            if (m.Contains("polearm") || m.Contains("spear") || m.Contains("halberd")) return 6;
            if (m.Contains("stave") || m.Contains("staff")) return 10;
            if (m.Contains("crossbow")) return 18;
            if (m.Contains("bow")) return 2;
            if (m.Contains("rifle") || m.Contains("gun") || m.Contains("firearm")) return 3;
            if (m.Contains("knife") || m.Contains("dagger")) return 15;
            if (m.Contains("fist") || m.Contains("claw")) return 13;
            if (m.Contains("axe")) return two ? 1 : 0;
            if (m.Contains("mace") || m.Contains("hammer") || m.Contains("club")) return two ? 5 : 4;
            if (m.Contains("sword") || m.Contains("blade")) return two ? 8 : 7;
            if (m.Contains("shield") || m.Contains("buckler")) return -1;
            return two ? 8 : 7;
        }

        /// <summary>CreatureSoundData.CreatureImpactType of a creature display (the WeaponImpactSounds slot its body takes), 0 = flesh.</summary>
        public int CreatureImpactSlot(int displayId)
        {
            int r = CsdRowOfDisplay(displayId);
            if (r < 0) return SlotFlesh;
            int t = _csd.GetInt(r, CsdImpactType);
            return t >= 0 && t <= 9 ? t : SlotFlesh;
        }

        // Iteration 7 (mounts, PLAN7 s4.3, CreatureSoundData 3.3.5a layout measured by scout-data and checked by the ui7 probe
        // M2EventsProbe): 11 SoundWingFlapID, 26 SoundJumpStartID, 27 SoundJumpEndID (9 SoundFootstepID = CsdFootstep above).
        private const int CsdWingFlap = 11, CsdJumpStart = 26, CsdJumpEnd = 27;

        /// <summary>
        /// Iteration 7: a creature display's own sounds through the chain CreatureDisplayInfo.SoundID, else CreatureModelData.SoundID
        /// -> CreatureSoundData: footstep id (FootstepTerrainLookup.CreatureFootstepID), wing flap, jump start and jump end
        /// (SoundEntries ids). False (all 0) when the display has no sound row.
        /// </summary>
        public bool CreatureSounds(int displayId, out int footstepId, out int wingFlap, out int jumpStart, out int jumpEnd)
        {
            footstepId = wingFlap = jumpStart = jumpEnd = 0;
            int r = CsdRowOfDisplay(displayId);
            if (r < 0) return false;
            footstepId = _csd.GetInt(r, CsdFootstep);
            wingFlap = _csd.GetInt(r, CsdWingFlap);
            jumpStart = _csd.GetInt(r, CsdJumpStart);
            jumpEnd = _csd.GetInt(r, CsdJumpEnd);
            return true;
        }

        /// <summary>SoundEntries.Name of an entry ("FlyingMountTakeOff"), "" when unknown - for log lines.</summary>
        public string EntryName(int soundEntryId)
        {
            SoundEntryInfo e = soundEntryId > 0 ? Entry(soundEntryId) : null;
            return e != null && e.Name != null ? e.Name : "";
        }

        private int CsdRowOfDisplay(int displayId)
        {
            if (_cdi == null || _csd == null || displayId <= 0) return -1;
            int r = _cdi.FindRow(displayId);
            if (r < 0) return -1;
            int s = _cdi.GetInt(r, DbcFields.CreatureDisplayInfo.SoundID);
            if (s <= 0 && _cmd != null)
            {
                int mr = _cmd.FindRow(_cdi.GetInt(r, DbcFields.CreatureDisplayInfo.ModelID));
                if (mr >= 0) s = _cmd.GetInt(mr, DbcFields.CreatureModelData.SoundID);
            }
            return s > 0 ? _csd.FindRow(s) : -1;
        }

        // ------------------------------------------------------------------------------------------ player character (race)

        /// <summary>The character display of a race and gender (0 male, 1 female), 0 when unknown.</summary>
        public int RaceDisplay(int race, int gender)
        {
            if (_races == null) return 0;
            int r = _races.FindRow(race);
            if (r < 0) return 0;
            return _races.GetInt(r, gender == 1 ? DbcFields.ChrRaces.FemaleDisplayID : DbcFields.ChrRaces.MaleDisplayID);
        }

        /// <summary>FootstepTerrainLookup CreatureFootstepID of a race's character model (7 medium/large, 8 small), 7 when unknown.</summary>
        public int RaceFootstepId(int race, int gender)
        {
            int r = CsdRowOfDisplay(RaceDisplay(race, gender));
            int f = r >= 0 ? _csd.GetInt(r, CsdFootstep) : 0;
            return f > 0 ? f : 7;
        }

        /// <summary>The death cry of a race's character model (CreatureSoundData.SoundDeathID), 0 when none.</summary>
        public int RaceDeathSound(int race, int gender)
        {
            int r = CsdRowOfDisplay(RaceDisplay(race, gender));
            return r >= 0 ? _csd.GetInt(r, CsdDeath) : 0;
        }

        /// <summary>ChrRaces.SplashSoundID (the race's water entry splash), 0 when none.</summary>
        public int RaceSplashSound(int race)
        {
            if (_races == null) return 0;
            int r = _races.FindRow(race);
            return r >= 0 ? _races.GetInt(r, DbcFields.ChrRaces.SplashSoundID) : 0;
        }

        // ------------------------------------------------------------------------------------------ footsteps

        /// <summary>TerrainType id of a terrain layer: MCLY effect id -> GroundEffectTexture.Sound; else by texture name keywords.</summary>
        public int TerrainTypeOfLayer(int effectId, string texturePath)
        {
            if (effectId > 0 && _get != null)
            {
                int r = _get.FindRow(effectId);
                if (r >= 0)
                {
                    int t = _get.GetInt(r, DbcFields.GroundEffectTexture.Sound);
                    if (t >= 0 && t <= 11) return t;
                }
            }
            return TerrainTypeOfName(texturePath);
        }

        /// <summary>TerrainType by texture/model name keywords (fallback where the data has no ground effect; an approximation).</summary>
        public static int TerrainTypeOfName(string name) { return TerrainTypeOfName(name, TerrainDirt); }

        /// <summary>As TerrainTypeOfName with the type to use when no keyword matches.</summary>
        public static int TerrainTypeOfName(string name, int fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            string n = name.ToLowerInvariant();
            int slash = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/'));
            if (slash >= 0) n = n.Substring(slash + 1);
            if (n.Contains("snow") || n.Contains("ice")) return TerrainSnow;
            if (n.Contains("sand") || n.Contains("beach") || n.Contains("desert")) return TerrainSand;
            if (n.Contains("wood") || n.Contains("plank") || n.Contains("bridge") || n.Contains("dock") || n.Contains("board") || n.Contains("log")
                || n.Contains("treering") || n.Contains("trunk") || n.Contains("bark") || n.Contains("beam")) return TerrainWood;
            if (n.Contains("metal") || n.Contains("iron") || n.Contains("grate")) return TerrainMetallic;
            if (n.Contains("mud") || n.Contains("swamp") || n.Contains("marsh") || n.Contains("bog")) return TerrainSoggy;
            if (n.Contains("leaf") || n.Contains("leaves") || n.Contains("needle") || n.Contains("fern")) return TerrainLeaves;
            if (n.Contains("grass") || n.Contains("flower") || n.Contains("moss") || n.Contains("weed") || n.Contains("bush") || n.Contains("crop")) return TerrainGrass;
            if (n.Contains("rock") || n.Contains("stone") || n.Contains("cobble") || n.Contains("brick") || n.Contains("tile") || n.Contains("cliff")
                || n.Contains("road") || n.Contains("floor") || n.Contains("marble") || n.Contains("boulder") || n.Contains("stair")) return TerrainStone;
            return fallback;
        }

        /// <summary>
        /// The footstep sound (or the shallow-water splash) of a character footstep id on a TerrainType, 0 when none. Steps: the
        /// row whose TerrainSoundID = TerrainType.SoundID (measured: id 7, sound 3 = CharacterMediumLargeStone for Stone). Splashes:
        /// the SoundIDSplash column lines up with the TerrainType id itself (measured: row 0 FootstepSplashMediumDirt, 3 ..Snow,
        /// 4 ..Wood, 5 ..Grass), so it is looked up by that.
        /// </summary>
        public int FootstepSound(int footstepId, int terrainType, bool splash)
        {
            int soundType;
            if (splash) soundType = terrainType >= 0 && terrainType <= 9 ? terrainType : 0;
            else if (!_terrainSound.TryGetValue(terrainType, out soundType) || soundType <= 0) soundType = 1;   // dirt
            if (_foot == null) return 0;
            int r;
            if (!_footstep.TryGetValue(((long)footstepId << 32) | (uint)soundType, out r)
                && !_footstep.TryGetValue(((long)footstepId << 32) | 1u, out r)
                && !_footstep.TryGetValue((7L << 32) | (uint)soundType, out r)) return 0;
            int id = _foot.GetInt(r, splash ? 4 : 3);
            if (id <= 0 && splash) id = _foot.GetInt(r, 3);
            return id;
        }

        /// <summary>TerrainType.TerrainDesc ("Grass"), for log lines.</summary>
        public string TerrainName(int terrainType)
        {
            if (_terrain != null)
                for (int r = 0; r < _terrain.RecordCount; r++)
                    if (_terrain.GetInt(r, 0) == terrainType) return _terrain.GetString(r, 1);
            return "type " + terrainType;
        }

        // ------------------------------------------------------------------------------------------ zones

        /// <summary>duelsnd: a SoundAmbience row's day / night SoundEntries (fields 1 / 2, as Zone reads them); false when unknown.</summary>
        public bool AmbienceEntries(int ambienceId, out int day, out int night)
        {
            day = night = 0;
            if (ambienceId <= 0 || _amb == null) return false;
            lock (_lock)
            {
                int ar = _amb.FindRow(ambienceId);
                if (ar < 0) return false;
                day = _amb.GetInt(ar, 1);
                night = _amb.GetInt(ar, 2);
                return day > 0 || night > 0;
            }
        }

        /// <summary>Lead 2026-10-04 (building music): a sound set from explicit ZoneMusic / SoundAmbience / ZoneIntroMusicTable ids (a WMO
        /// area's own, or its linked area's, or the zone's), filled like Zone; cached per id triple. Never null.</summary>
        public ZoneSoundInfo Compose(string name, int areaId, int zoneMusicId, int ambienceId, int introId)
        {
            long key = ((long)(zoneMusicId & 0xFFFFF) << 40) | ((long)(ambienceId & 0xFFFFF) << 20) | (long)(introId & 0xFFFFF);
            lock (_lock)
            {
                ZoneSoundInfo z;
                if (_composed.TryGetValue(key, out z)) return z;
                z = new ZoneSoundInfo { AreaId = areaId, Name = name ?? "", ZoneMusicId = zoneMusicId, AmbienceId = ambienceId, IntroId = introId };
                int mr = _zm != null && zoneMusicId > 0 ? _zm.FindRow(zoneMusicId) : -1;
                if (mr >= 0)
                {
                    z.MusicSetName = _zm.GetString(mr, DbcFields.ZoneMusic.SetName);
                    z.SilenceMinDay = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMin);
                    z.SilenceMinNight = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMin + 1);
                    z.SilenceMaxDay = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMax);
                    z.SilenceMaxNight = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMax + 1);
                    z.MusicDay = _zm.GetInt(mr, DbcFields.ZoneMusic.Sounds);
                    z.MusicNight = _zm.GetInt(mr, DbcFields.ZoneMusic.Sounds + 1);
                }
                int ar = _amb != null && ambienceId > 0 ? _amb.FindRow(ambienceId) : -1;
                if (ar >= 0) { z.AmbienceDay = _amb.GetInt(ar, 1); z.AmbienceNight = _amb.GetInt(ar, 2); }
                int ir = _intro != null && introId > 0 ? _intro.FindRow(introId) : -1;
                if (ir >= 0) { z.IntroSound = _intro.GetInt(ir, 2); z.IntroMinDelayMinutes = _intro.GetInt(ir, 4); }
                _composed[key] = z;
                return z;
            }
        }

        private readonly Dictionary<long, ZoneSoundInfo> _composed = new Dictionary<long, ZoneSoundInfo>();

        /// <summary>The music/ambience/intro of an area (subzone fields that are 0 take the parent zone's), cached; null when unknown.</summary>
        public ZoneSoundInfo Zone(int areaId)
        {
            if (areaId <= 0 || _area == null) return null;
            lock (_lock)
            {
                ZoneSoundInfo z;
                if (_zones.TryGetValue(areaId, out z)) return z;
                z = null;
                int r = _area.FindRow(areaId);
                if (r >= 0)
                {
                    z = new ZoneSoundInfo { AreaId = areaId, Name = _area.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang) };
                    int cur = r;
                    for (int depth = 0; depth < 4 && cur >= 0; depth++)
                    {
                        if (z.AmbienceId == 0) z.AmbienceId = _area.GetInt(cur, DbcFields.AreaTable.AmbienceID);
                        if (z.ZoneMusicId == 0) z.ZoneMusicId = _area.GetInt(cur, DbcFields.AreaTable.ZoneMusic);
                        if (z.IntroId == 0) z.IntroId = _area.GetInt(cur, DbcFields.AreaTable.IntroSound);
                        int parent = _area.GetInt(cur, DbcFields.AreaTable.ParentAreaID);
                        cur = parent > 0 && parent != _area.GetId(cur) ? _area.FindRow(parent) : -1;
                    }
                    int mr = _zm != null && z.ZoneMusicId > 0 ? _zm.FindRow(z.ZoneMusicId) : -1;
                    if (mr >= 0)
                    {
                        z.MusicSetName = _zm.GetString(mr, DbcFields.ZoneMusic.SetName);
                        z.SilenceMinDay = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMin);
                        z.SilenceMinNight = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMin + 1);
                        z.SilenceMaxDay = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMax);
                        z.SilenceMaxNight = _zm.GetInt(mr, DbcFields.ZoneMusic.SilenceIntervalMax + 1);
                        z.MusicDay = _zm.GetInt(mr, DbcFields.ZoneMusic.Sounds);
                        z.MusicNight = _zm.GetInt(mr, DbcFields.ZoneMusic.Sounds + 1);
                    }
                    int ar = _amb != null && z.AmbienceId > 0 ? _amb.FindRow(z.AmbienceId) : -1;
                    if (ar >= 0) { z.AmbienceDay = _amb.GetInt(ar, 1); z.AmbienceNight = _amb.GetInt(ar, 2); }
                    int ir = _intro != null && z.IntroId > 0 ? _intro.FindRow(z.IntroId) : -1;
                    if (ir >= 0) { z.IntroSound = _intro.GetInt(ir, 2); z.IntroMinDelayMinutes = _intro.GetInt(ir, 4); }
                }
                _zones[areaId] = z;
                return z;
            }
        }

        // ------------------------------------------------------------------------------------------ voices

        /// <summary>NPCSounds of a creature display: which 0 = greeting, 1 = farewell, 2 = pissed; 0 when none.</summary>
        public int NpcSound(int displayId, int which)
        {
            if (_cdi == null || _npc == null || displayId <= 0 || which < 0 || which > 3) return 0;
            int r = _cdi.FindRow(displayId);
            if (r < 0) return 0;
            int nr = _npc.FindRow(_cdi.GetInt(r, DbcFields.CreatureDisplayInfo.NPCSoundID));
            return nr >= 0 ? _npc.GetInt(nr, 1 + which) : 0;
        }

        /// <summary>VocalUISounds line of a race (gender 0 male, 1 female) for a VocalUIEnum, 0 when none.</summary>
        public int VocalSound(int race, int vocalEnum, int gender)
        {
            if (_vocal == null || vocalEnum < 0) return 0;
            for (int r = 0; r < _vocal.RecordCount; r++)
            {
                if (_vocal.GetInt(r, 2) != race || _vocal.GetInt(r, 1) != vocalEnum) continue;
                int id = _vocal.GetInt(r, gender == 1 ? 4 : 3);
                if (id <= 0) id = _vocal.GetInt(r, gender == 1 ? 3 : 4);
                return id > 0 ? id : 0;
            }
            return 0;
        }

        /// <summary>
        /// The VocalUIEnum for a red error text (the offline session's and the client's wording), -1 when the client has no voice
        /// line for it (e.g. facing: 3.3.5a ships none). Enums measured in VocalUISounds (same for every race): 0 InventoryFull,
        /// 5 ItemCooling, 11 InvalidTarget, 12 SpellCooling, 15 NoMana, 45 CantCastGenericNoTarget, 46 CantCastOutOfRange,
        /// 50 AbilityCooling, 57 CantUseTooFar, 63 NoRage, 64 NoEnergy.
        /// </summary>
        public static int VocalEnumForError(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            string t = text.ToLowerInvariant();
            if (t.Contains("not enough rage")) return 63;
            if (t.Contains("not enough energy")) return 64;
            if (t.Contains("not enough mana")) return 15;
            if (t.Contains("out of range")) return 46;
            if (t.Contains("too far away")) return 57;
            if (t.Contains("invalid target") || t.Contains("cannot attack that target") || t.Contains("can't attack that target")) return 11;
            if (t.Contains("no target")) return 45;
            if (t.Contains("item is not ready")) return 5;
            if (t.Contains("ability is not ready")) return 50;
            if (t.Contains("not ready yet") || t.Contains("can't do that yet")) return 12;
            if (t.Contains("inventory is full") || t.Contains("bags are full")) return 0;
            return -1;
        }

        // ------------------------------------------------------------------------------------------ items

        /// <summary>The pickup sound of an item (Item.dbc -> ItemDisplayInfo.GroupSoundIndex -> ItemGroupSounds [0]), 0 when none.</summary>
        public int ItemPickupSound(int itemEntry)
        {
            int cls, sub, disp; bool metal;
            if (_idi == null || _igs == null || !TryItem(itemEntry, out cls, out sub, out metal, out disp)) return 0;
            int r = _idi.FindRow(disp);
            if (r < 0) return 0;
            int g = _igs.FindRow(_idi.GetInt(r, DbcFields.ItemDisplayInfo.GroupSoundIndex));
            return g >= 0 ? _igs.GetInt(g, 1) : 0;
        }

        /// <summary>Iteration 11 (uicore hook H9): the drop sound of an item - ItemGroupSounds column 2, the twin of ItemPickupSound's
        /// column 1 (ID, Sound[4]: pickup, drop, ...); 0 when none.</summary>
        public int ItemDropSound(int itemEntry)
        {
            int cls, sub, disp; bool metal;
            if (_idi == null || _igs == null || !TryItem(itemEntry, out cls, out sub, out metal, out disp)) return 0;
            int r = _idi.FindRow(disp);
            if (r < 0) return 0;
            int g = _igs.FindRow(_idi.GetInt(r, DbcFields.ItemDisplayInfo.GroupSoundIndex));
            return g >= 0 ? _igs.GetInt(g, 2) : 0;
        }
    }
}
