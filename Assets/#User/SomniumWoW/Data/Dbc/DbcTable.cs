using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Data
{
    /// <summary>
    /// A parsed WDBC client database (3.3.5a): 20-byte header ('WDBC', recordCount, fieldCount, recordSize,
    /// stringBlockSize), fixed-size records of 4-byte fields, then the string block (field value = byte offset into it).
    /// Field indices come from DbcFields (named constants for build 12340). Immutable after Parse, thread-safe reads
    /// (the id index is built once under a lock).
    ///
    /// Localised strings: 17 fields = 16 locale slots + a flags mask. Slot order (3.3.5a): 0 enUS/enGB, 1 koKR,
    /// 2 frFR, 3 deDE, 4 zhCN, 5 zhTW, 6 esES, 7 esMX, 8 ruRU, 9-15 unused. Measured on this enUS install: slot 0 is
    /// filled, slots 1-15 are empty. GetLocalizedString(row, firstField) reads LocaleSlot, falling back to the first
    /// non-empty slot.
    /// </summary>
    public sealed class DbcTable
    {
        public const int HeaderSize = 20;

        /// <summary>Locale slot used by GetLocalizedString (set from the WoW locale by Dbc.Load; 0 = enUS/enGB).</summary>
        public int LocaleSlot { get; set; }

        public string Name { get; private set; }
        public int RecordCount { get; private set; }
        public int FieldCount { get; private set; }
        public int RecordSize { get; private set; }
        public int StringBlockSize { get; private set; }

        private byte[] _data;
        private int _stringsOffset;
        private Dictionary<int, int> _idIndex;
        private readonly object _indexLock = new object();

        private DbcTable() { }

        /// <summary>Parses a WDBC file. Throws InvalidDataException when the header or sizes do not add up.</summary>
        public static DbcTable Parse(byte[] bytes, string name)
        {
            if (bytes == null || bytes.Length < HeaderSize) throw new InvalidDataException(name + ": too short for a DBC");
            if (bytes[0] != 'W' || bytes[1] != 'D' || bytes[2] != 'B' || bytes[3] != 'C')
                throw new InvalidDataException(name + ": not a WDBC file (magic " + (char)bytes[0] + (char)bytes[1] + (char)bytes[2] + (char)bytes[3] + ")");
            var t = new DbcTable();
            t.Name = name;
            t.RecordCount = BitConverter.ToInt32(bytes, 4);
            t.FieldCount = BitConverter.ToInt32(bytes, 8);
            t.RecordSize = BitConverter.ToInt32(bytes, 12);
            t.StringBlockSize = BitConverter.ToInt32(bytes, 16);
            long need = HeaderSize + (long)t.RecordCount * t.RecordSize + t.StringBlockSize;
            if (t.RecordCount < 0 || t.RecordSize < 0 || t.StringBlockSize < 0 || need > bytes.Length)
                throw new InvalidDataException(name + ": sizes exceed the file (" + need + " > " + bytes.Length + ")");
            t._data = bytes;
            t._stringsOffset = HeaderSize + t.RecordCount * t.RecordSize;
            return t;
        }

        /// <summary>True when every field is 4 bytes (fieldCount * 4 == recordSize) - true for every table this world uses.</summary>
        public bool FieldsAre4Bytes { get { return FieldCount * 4 == RecordSize; } }

        private int Offset(int row, int field)
        {
            if ((uint)row >= (uint)RecordCount || (uint)field >= (uint)FieldCount)
                throw new ArgumentOutOfRangeException(Name + ": row " + row + " field " + field + " out of range (" + RecordCount + " x " + FieldCount + ")");
            return HeaderSize + row * RecordSize + field * 4;
        }

        public int GetInt(int row, int field) { return BitConverter.ToInt32(_data, Offset(row, field)); }

        public uint GetUInt(int row, int field) { return BitConverter.ToUInt32(_data, Offset(row, field)); }

        public float GetFloat(int row, int field) { return BitConverter.ToSingle(_data, Offset(row, field)); }

        /// <summary>One byte of a record (for 1-byte-field tables such as CharBaseInfo; byteOffset within the record).</summary>
        public byte GetByte(int row, int byteOffset)
        {
            if ((uint)row >= (uint)RecordCount || (uint)byteOffset >= (uint)RecordSize) throw new ArgumentOutOfRangeException(Name + ": row " + row + " byte " + byteOffset);
            return _data[HeaderSize + row * RecordSize + byteOffset];
        }

        /// <summary>The row's id (field 0).</summary>
        public int GetId(int row) { return GetInt(row, 0); }

        /// <summary>A string field (UTF-8), "" for offset 0 or an offset outside the string block.</summary>
        public string GetString(int row, int field)
        {
            return StringAt(GetInt(row, field));
        }

        /// <summary>The string at a string-block offset ("" when out of range).</summary>
        public string StringAt(int offset)
        {
            if (offset <= 0 || offset >= StringBlockSize) return "";
            int start = _stringsOffset + offset;
            int end = start;
            int limit = _stringsOffset + StringBlockSize;
            while (end < limit && _data[end] != 0) end++;
            return Encoding.UTF8.GetString(_data, start, end - start);
        }

        /// <summary>
        /// A localised string (firstField = the field index of the locstring's slot 0, i.e. the DbcFields constant):
        /// LocaleSlot's text, else the first non-empty of the 16 slots, else "".
        /// </summary>
        public string GetLocalizedString(int row, int firstField)
        {
            int slot = LocaleSlot;
            if (slot >= 0 && slot < 16)
            {
                var s = GetString(row, firstField + slot);
                if (s.Length > 0) return s;
            }
            for (int i = 0; i < 16; i++)
            {
                if (i == slot) continue;
                var s = GetString(row, firstField + i);
                if (s.Length > 0) return s;
            }
            return "";
        }

        /// <summary>The row whose field 0 (ID) equals id, or -1.</summary>
        public int FindRow(int id)
        {
            var idx = _idIndex;
            if (idx == null)
            {
                lock (_indexLock)
                {
                    if (_idIndex == null)
                    {
                        var d = new Dictionary<int, int>(RecordCount);
                        for (int r = 0; r < RecordCount; r++)
                        {
                            int key = GetInt(r, 0);
                            if (!d.ContainsKey(key)) d[key] = r;
                        }
                        _idIndex = d;
                    }
                    idx = _idIndex;
                }
            }
            int row;
            return idx.TryGetValue(id, out row) ? row : -1;
        }

        /// <summary>First row whose int field equals value (linear scan), or -1.</summary>
        public int FindRowWhere(int field, int value)
        {
            for (int r = 0; r < RecordCount; r++) if (GetInt(r, field) == value) return r;
            return -1;
        }

        /// <summary>All rows whose int field equals value (linear scan).</summary>
        public List<int> RowsWhere(int field, int value)
        {
            var list = new List<int>();
            for (int r = 0; r < RecordCount; r++) if (GetInt(r, field) == value) list.Add(r);
            return list;
        }

        public override string ToString()
        {
            return Name + " (" + RecordCount + " rows x " + FieldCount + " fields, " + RecordSize + " B/row, strings " + StringBlockSize + " B)";
        }
    }
}
