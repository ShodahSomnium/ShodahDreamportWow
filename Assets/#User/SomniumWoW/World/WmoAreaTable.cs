using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Iteration 7 (lead contract PLAN7 s3.6; owner Body): WMOAreaTable.dbc for the indoors/outdoors rule. Pure (NO UnityEngine).
    /// AzerothCore keys it exactly like this (DBCStores.cpp: `typedef std::tuple&lt;int16, int8, int32&gt; WMOAreaTableKey;` ...
    /// `sWMOAreaInfoByTripple[WMOAreaTableKey(entry-&gt;rootId, entry-&gt;adtId, entry-&gt;groupId)] = entry;` - a later row with the same
    /// key replaces an earlier one; GetWMOAreaTableEntryByTripple casts the query to int16 / int8 the same way). rootId = the WMO
    /// root's MOHD WMO id (+32), adtId = the MODF placement's NameSet, groupId = the group's MOGP "UniqueId" (+56).
    /// The lead's WowGame hook warms Get on the worker; Get is cached per WowFiles and thread-safe.
    /// </summary>
    public sealed class WmoAreaTable
    {
        // WMOAreaTable.dbc fields (AC DBCStructure.h WMOAreaTableEntry; measured by scout-data: 22550 rows x 28 fields)
        private const int FieldWmoId = 1;       // rootId, "used in root WMO"
        private const int FieldNameSet = 2;     // adtId, "used in adt file"
        private const int FieldGroupId = 3;     // groupId, "used in group WMO"
        private const int FieldFlags = 9;       // "used for indoor/outdoor determination" (4 = outdoors, 2 = indoors)
        private const int FieldAreaId = 10;     // "link to AreaTableEntry.ID"
        // duelsnd: the sound columns (wowdev WMOAreaTable: 4 SoundProviderPref, 5 underwater, 6 AmbienceID -> SoundAmbience, 7 ZoneMusic,
        // 8 IntroSound). Measured (harness DsWmoAreas): the Deeprun Tram Subway.wmo (MOHD wmoID 3494) row group -1 "Deeprun Tram" = ambience
        // 290 (SoundAmbience 290 = SoundEntries 6754 "SubwayStation"), group 17937 = 291 ("SubwayUnderwater"); AreaTable 2257 has none.
        private const int FieldAmbience = 6, FieldZoneMusic = 7, FieldIntro = 8;

        private static readonly object CacheLock = new object();
        private static WmoAreaTable _cached;
        private static WowFiles _cachedFiles;

        private readonly Dictionary<long, int> _rows = new Dictionary<long, int>();
        private int[] _flags = new int[0], _area = new int[0], _amb = new int[0], _music = new int[0], _intro = new int[0];
        private int _rowCount;

        public static WmoAreaTable Get(WowFiles files)   // cached per WowFiles, worker thread
        {
            lock (CacheLock) { if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached; }
            var t = new WmoAreaTable();
            if (files != null) t.Read(files);
            lock (CacheLock) { _cached = t; _cachedFiles = files; }
            return t;
        }

        public int Rows { get { return _rowCount; } }

        public bool TryGet(int wmoId, int nameSet, int groupId, out int flags, out int areaId)
        {
            flags = 0; areaId = 0;
            int r;
            if (!_rows.TryGetValue(Key(wmoId, nameSet, groupId), out r)) return false;
            flags = _flags[r];
            areaId = _area[r];
            return true;
        }

        /// <summary>duelsnd: the ambience (SoundAmbience id) and ZoneMusic of a WMO group - its own row, else (ours) the WMO-wide row
        /// (group -1, e.g. "Deeprun Tram") for what the group row leaves 0. False = neither row has a sound.</summary>
        public bool TryGetSound(int wmoId, int nameSet, int groupId, out int ambienceId, out int zoneMusicId)
        {
            ambienceId = 0; zoneMusicId = 0;
            int r;
            if (_rows.TryGetValue(Key(wmoId, nameSet, groupId), out r)) { ambienceId = _amb[r]; zoneMusicId = _music[r]; }
            if ((ambienceId == 0 || zoneMusicId == 0) && groupId != -1 && _rows.TryGetValue(Key(wmoId, nameSet, -1), out r))
            {
                if (ambienceId == 0) ambienceId = _amb[r];
                if (zoneMusicId == 0) zoneMusicId = _music[r];
            }
            return ambienceId != 0 || zoneMusicId != 0;
        }

        /// <summary>
        /// Lead 2026-10-04 (the user: "make sure all the music is correct, like walking into taverns"): the sound row of a WMO group as the
        /// client resolves it - the group's row, else the WMO-wide row (group -1); its AmbienceID (SoundAmbience), ZoneMusic, IntroSound
        /// (ZoneIntroMusicTable) and AreaTableID (whose AreaTable sounds fill what the row leaves 0). Measured (harness WmoMusic): "Lion's Pride
        /// Inn" wmo 53 set 2 group -1 ambience 170 music 156 'Zone-TavernAlliance'; "Gallows' End Tavern" music 189 'Zone-TavernHorde01';
        /// 3053 of 22550 rows have their own music, 3536 more link an AreaTable row that has. False = no row.
        /// </summary>
        public bool TryGetSoundRow(int wmoId, int nameSet, int groupId, out int ambienceId, out int zoneMusicId, out int introId, out int areaId)
        {
            ambienceId = zoneMusicId = introId = areaId = 0;
            int r;
            bool any = false;
            if (_rows.TryGetValue(Key(wmoId, nameSet, groupId), out r)) { any = true; ambienceId = _amb[r]; zoneMusicId = _music[r]; introId = _intro[r]; areaId = _area[r]; }
            if (groupId != -1 && _rows.TryGetValue(Key(wmoId, nameSet, -1), out r))
            {
                any = true;
                if (ambienceId == 0) ambienceId = _amb[r];
                if (zoneMusicId == 0) zoneMusicId = _music[r];
                if (introId == 0) introId = _intro[r];
                if (areaId == 0) areaId = _area[r];
            }
            return any;
        }

        /// <summary>AC Map.cpp (PLAN7 s4.4): outdoors = MOGP 0x8, then a row's flags 4 = outdoors / 2 = indoors override it.
        /// Quoted: `data.outdoors = (wmoData-&gt;areaInfo-&gt;mogpFlags &amp; 0x8) != 0; if (wmoEntry) { ... if (wmoEntry-&gt;Flags &amp; 4)
        /// data.outdoors = true; else if (wmoEntry-&gt;Flags &amp; 2) data.outdoors = false; }`</summary>
        public static bool Outdoors(uint mogpFlags, bool haveRow, int rowFlags)
        {
            bool outdoors = (mogpFlags & 0x8) != 0;
            if (haveRow)
            {
                if ((rowFlags & 4) != 0) outdoors = true;
                else if ((rowFlags & 2) != 0) outdoors = false;
            }
            return outdoors;
        }

        private static long Key(int wmoId, int nameSet, int groupId)
        {
            // int16 / int8 / int32 as AC's tuple (the casts truncate exactly as the C++ does)
            return ((long)(ushort)(short)wmoId << 40) | ((long)(byte)(sbyte)nameSet << 32) | (uint)groupId;
        }

        private void Read(WowFiles files)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DbcTable t = null;
            try { t = Dbc.Load(files, "WMOAreaTable"); }
            catch (Exception e) { WowLog.Warn("WmoAreaTable: reading WMOAreaTable.dbc threw " + e.GetType().Name + ": " + e.Message); }
            if (t == null) { WowLog.Warn("WmoAreaTable: WMOAreaTable.dbc missing - WMO groups decide indoors by their MOGP 0x8 bit only"); return; }
            if (t.FieldCount <= FieldAreaId || !t.FieldsAre4Bytes)
            {
                WowLog.Warn("WmoAreaTable: unexpected layout (" + t.FieldCount + " fields, record " + t.RecordSize + " B) - not used");
                return;
            }
            int n = t.RecordCount;
            _flags = new int[n];
            _area = new int[n];
            bool sound = t.FieldCount > FieldZoneMusic;
            _amb = new int[n];
            _music = new int[n];
            _intro = new int[n];
            bool introCol = t.FieldCount > FieldIntro;
            int f4 = 0, f2 = 0, dup = 0;
            for (int r = 0; r < n; r++)
            {
                _flags[r] = t.GetInt(r, FieldFlags);
                _area[r] = t.GetInt(r, FieldAreaId);
                if (sound) { _amb[r] = t.GetInt(r, FieldAmbience); _music[r] = t.GetInt(r, FieldZoneMusic); }
                if (introCol) _intro[r] = t.GetInt(r, FieldIntro);
                if ((_flags[r] & 4) != 0) f4++;
                else if ((_flags[r] & 2) != 0) f2++;
                long k = Key(t.GetInt(r, FieldWmoId), t.GetInt(r, FieldNameSet), t.GetInt(r, FieldGroupId));
                if (_rows.ContainsKey(k)) dup++;
                _rows[k] = r;   // AC: the later row wins
            }
            _rowCount = n;
            WowLog.Info("WmoAreaTable: " + n + " rows, " + _rows.Count + " (WMO, NameSet, group) keys" + (dup > 0 ? " (" + dup + " duplicate keys, the later row wins as in AC)" : "")
                + ", flags 0x4 outdoors x" + f4 + ", 0x2 indoors x" + f2 + ", " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
