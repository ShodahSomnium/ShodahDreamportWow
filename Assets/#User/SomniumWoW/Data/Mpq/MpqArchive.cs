using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Data.Mpq
{
    /// <summary>
    /// One MPQ archive, read-only, StormLib-compatible for what the 3.3.5a client archives use (measured on the
    /// install: every archive has a v2 "format 1" 44-byte header at offset 0, 4096-byte sectors, no hi-block table,
    /// all table/file offsets below 4 GiB but above 2 GiB, so offsets are handled as unsigned/64-bit).
    ///
    /// Lookup: hash-table probing with name hashes A/B from the start index, stopping at a never-used slot
    /// (0xFFFFFFFF), skipping deleted slots (0xFFFFFFFE); locale preference = the configured locale, else neutral (0),
    /// else the first match. Files: single-unit or sectored, stored/compressed (multi-method mask)/imploded,
    /// encrypted (+ fix-key). Sector CRCs are ignored. Delete markers are reported (Lookup returns Deleted).
    ///
    /// Thread-safe: raw reads are serialised by a lock on the FileStream; decryption/decompression run outside it.
    /// </summary>
    public sealed class MpqArchive : IDisposable
    {
        public const uint FlagImplode = 0x00000100;
        public const uint FlagCompress = 0x00000200;
        public const uint FlagEncrypted = 0x00010000;
        public const uint FlagFixKey = 0x00020000;
        public const uint FlagPatchFile = 0x00100000;
        public const uint FlagSingleUnit = 0x01000000;
        public const uint FlagDeleteMarker = 0x02000000;
        public const uint FlagSectorCrc = 0x04000000;
        public const uint FlagExists = 0x80000000;

        private const uint HashFree = 0xFFFFFFFF;
        private const uint HashDeleted = 0xFFFFFFFE;

        public struct BlockEntry
        {
            public long FilePos;      // absolute position in the archive file (archive offset + block offset, 64-bit)
            public uint BlockOffset;  // low 32 bits of the offset relative to the MPQ header (fix-key input)
            public uint CompressedSize;
            public uint FileSize;
            public uint Flags;
        }

        public enum LookupResult { Missing, Found, Deleted }

        private readonly object _lock = new object();
        private FileStream _stream;

        private uint[] _hashA, _hashB, _hashBlock;
        private ushort[] _hashLocale;
        private BlockEntry[] _blocks;
        private uint _hashMask;

        public string Path { get; private set; }
        public string Name { get; private set; }
        public long ArchiveOffset { get; private set; }
        public int FormatVersion { get; private set; }
        public int SectorSize { get; private set; }
        public int HashTableSize { get { return _hashA == null ? 0 : _hashA.Length; } }
        public int BlockTableSize { get { return _blocks == null ? 0 : _blocks.Length; } }
        public bool HasHiBlockTable { get; private set; }

        /// <summary>Preferred MPQ locale (Windows LANGID, 0 = neutral). WoW's archives store 0 for (nearly) everything.</summary>
        public ushort PreferredLocale { get; set; }

        public IReadOnlyList<BlockEntry> Blocks { get { return _blocks; } }

        private MpqArchive() { }

        /// <summary>Opens the archive and reads its tables. Throws InvalidDataException on a malformed archive.</summary>
        public static MpqArchive Open(string path)
        {
            var a = new MpqArchive();
            a.Path = path;
            a.Name = System.IO.Path.GetFileName(path);
            a._stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            try
            {
                a.ReadTables();
            }
            catch
            {
                a._stream.Dispose();
                throw;
            }
            return a;
        }

        private static uint U32(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
        private static ushort U16(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }

        private void ReadTables()
        {
            long len = _stream.Length;
            var hdr = new byte[0x2C];
            long off = 0;
            bool found = false;
            // The header sits at offset 0 on every 3.3.5a archive; StormLib scans 512-byte boundaries (and follows an
            // 'MPQ\x1B' user-data header) - done here too for safety, limited to the first 64 MB.
            while (off + 32 <= len && off < 64L * 1024 * 1024)
            {
                RawRead(off, hdr, 0, (int)Math.Min(hdr.Length, len - off));
                uint magic = U32(hdr, 0);
                if (magic == 0x1A51504D) { found = true; break; }             // 'MPQ\x1A'
                if (magic == 0x1B51504D) { off += U32(hdr, 8); continue; }   // 'MPQ\x1B' user data -> header offset
                off += 512;
            }
            if (!found) throw new InvalidDataException(Name + ": no MPQ header");

            ArchiveOffset = off;
            uint headerSize = U32(hdr, 4);
            FormatVersion = U16(hdr, 0x0C);
            SectorSize = 512 << U16(hdr, 0x0E);
            long hashPos = U32(hdr, 0x10);
            long blockPos = U32(hdr, 0x14);
            int hashCount = (int)U32(hdr, 0x18);
            int blockCount = (int)U32(hdr, 0x1C);
            long hiBlockPos = 0;
            if (FormatVersion >= 1 && headerSize >= 0x2C)
            {
                hiBlockPos = (long)(U32(hdr, 0x20) | ((ulong)U32(hdr, 0x24) << 32));
                hashPos |= (long)U16(hdr, 0x28) << 32;
                blockPos |= (long)U16(hdr, 0x2A) << 32;
            }
            if (hashCount <= 0 || (hashCount & (hashCount - 1)) != 0)
                throw new InvalidDataException(Name + ": hash table size " + hashCount + " is not a power of two");

            // Hash table (encrypted).
            var ht = ReadUInts(ArchiveOffset + hashPos, hashCount * 4);
            MpqCrypto.Decrypt(ht, MpqCrypto.HashTableKey);
            _hashA = new uint[hashCount]; _hashB = new uint[hashCount]; _hashBlock = new uint[hashCount]; _hashLocale = new ushort[hashCount];
            for (int i = 0; i < hashCount; i++)
            {
                _hashA[i] = ht[i * 4];
                _hashB[i] = ht[i * 4 + 1];
                _hashLocale[i] = (ushort)(ht[i * 4 + 2] & 0xFFFF);   // u16 locale, u16 platform
                _hashBlock[i] = ht[i * 4 + 3];
            }
            _hashMask = (uint)hashCount - 1;

            // Block table (encrypted) + optional hi-block table (u16 per block, the offset's bits 32..47).
            var bt = ReadUInts(ArchiveOffset + blockPos, blockCount * 4);
            MpqCrypto.Decrypt(bt, MpqCrypto.BlockTableKey);
            ushort[] hi = null;
            if (hiBlockPos != 0)
            {
                HasHiBlockTable = true;
                var hb = new byte[blockCount * 2];
                RawRead(ArchiveOffset + hiBlockPos, hb, 0, hb.Length);
                hi = new ushort[blockCount];
                for (int i = 0; i < blockCount; i++) hi[i] = U16(hb, i * 2);
            }
            _blocks = new BlockEntry[blockCount];
            for (int i = 0; i < blockCount; i++)
            {
                uint lo = bt[i * 4];
                long rel = lo | (hi != null ? (long)hi[i] << 32 : 0);
                _blocks[i].BlockOffset = lo;
                _blocks[i].FilePos = ArchiveOffset + rel;
                _blocks[i].CompressedSize = bt[i * 4 + 1];
                _blocks[i].FileSize = bt[i * 4 + 2];
                _blocks[i].Flags = bt[i * 4 + 3];
            }
        }

        private uint[] ReadUInts(long pos, int count)
        {
            var b = new byte[count * 4];
            int got = RawRead(pos, b, 0, b.Length);
            if (got != b.Length) throw new InvalidDataException(Name + ": table at " + pos + " truncated (" + got + "/" + b.Length + ")");
            var u = new uint[count];
            Buffer.BlockCopy(b, 0, u, 0, b.Length);
            if (!BitConverter.IsLittleEndian) for (int i = 0; i < count; i++) u[i] = U32(b, i * 4);
            return u;
        }

        /// <summary>Reads raw bytes at an absolute file position (thread-safe). Returns the count read.</summary>
        public int RawRead(long pos, byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_stream == null) throw new ObjectDisposedException(Name);
                _stream.Position = pos;
                int total = 0;
                while (total < count)
                {
                    int n = _stream.Read(buffer, offset + total, count - total);
                    if (n <= 0) break;
                    total += n;
                }
                return total;
            }
        }

        /// <summary>The three name hashes of a path (they do not depend on the archive, so callers can reuse them).</summary>
        public struct NameHash
        {
            public uint Index, A, B;
            public string Path;

            public static NameHash Of(string path)
            {
                NameHash h;
                h.Path = path;
                h.Index = MpqCrypto.Hash(path, MpqCrypto.HashTableIndex);
                h.A = MpqCrypto.Hash(path, MpqCrypto.HashNameA);
                h.B = MpqCrypto.Hash(path, MpqCrypto.HashNameB);
                return h;
            }
        }

        /// <summary>Finds the block index for a path; Deleted when the chosen entry is a patch delete marker.</summary>
        public LookupResult Lookup(NameHash h, out int blockIndex)
        {
            blockIndex = -1;
            if (_hashA == null) return LookupResult.Missing;
            uint start = h.Index & _hashMask;
            uint i = start;
            int neutral = -1, first = -1, exact = -1;
            do
            {
                uint bi = _hashBlock[i];
                if (bi == HashFree) break;
                if (bi != HashDeleted && _hashA[i] == h.A && _hashB[i] == h.B && bi < (uint)_blocks.Length)
                {
                    ushort loc = _hashLocale[i];
                    if (first < 0) first = (int)bi;
                    if (loc == PreferredLocale && exact < 0) exact = (int)bi;
                    if (loc == 0 && neutral < 0) neutral = (int)bi;
                }
                i = (i + 1) & _hashMask;
            } while (i != start);

            int pick = exact >= 0 ? exact : (neutral >= 0 ? neutral : first);
            if (pick < 0) return LookupResult.Missing;
            uint flags = _blocks[pick].Flags;
            if ((flags & FlagExists) == 0) return LookupResult.Missing;
            blockIndex = pick;
            return (flags & FlagDeleteMarker) != 0 ? LookupResult.Deleted : LookupResult.Found;
        }

        public LookupResult Lookup(string path, out int blockIndex) { return Lookup(NameHash.Of(path), out blockIndex); }

        public bool Contains(string path) { int b; return Lookup(path, out b) == LookupResult.Found; }

        /// <summary>The file's bytes, or null when missing/deleted. path is needed for the key of encrypted files.</summary>
        public byte[] ReadFile(string path)
        {
            int bi;
            if (Lookup(path, out bi) != LookupResult.Found) return null;
            return ReadBlock(bi, path);
        }

        /// <summary>Reads and unpacks one block. Throws InvalidDataException on corrupt data or an unsupported codec.</summary>
        public byte[] ReadBlock(int blockIndex, string path)
        {
            var b = _blocks[blockIndex];
            uint flags = b.Flags;
            if ((flags & FlagExists) == 0 || (flags & FlagDeleteMarker) != 0) return null;
            if ((flags & FlagPatchFile) != 0)
                throw new InvalidDataException(Name + ": " + path + " is an incremental patch file (not used by 3.3.5a)");
            int fileSize = checked((int)b.FileSize);
            var output = new byte[fileSize];
            if (fileSize == 0) return output;

            bool encrypted = (flags & FlagEncrypted) != 0;
            uint key = encrypted ? MpqCrypto.FileKey(path, b.BlockOffset, b.FileSize, (flags & FlagFixKey) != 0) : 0;
            bool packed = (flags & (FlagCompress | FlagImplode)) != 0;

            if ((flags & FlagSingleUnit) != 0)
            {
                var raw = new byte[b.CompressedSize];
                if (RawRead(b.FilePos, raw, 0, raw.Length) != raw.Length) throw new InvalidDataException(Name + ": " + path + " truncated");
                if (encrypted) MpqCrypto.Decrypt(raw, 0, raw.Length, key);
                Unpack(raw, 0, raw.Length, output, 0, fileSize, flags, path);
                return output;
            }

            int sectorSize = SectorSize;
            int sectors = (fileSize + sectorSize - 1) / sectorSize;
            if (!packed)
            {
                // Stored sectors, back to back.
                if (RawRead(b.FilePos, output, 0, fileSize) != fileSize) throw new InvalidDataException(Name + ": " + path + " truncated");
                if (encrypted)
                    for (int s = 0; s < sectors; s++)
                    {
                        int so = s * sectorSize;
                        MpqCrypto.Decrypt(output, so, Math.Min(sectorSize, fileSize - so), key + (uint)s);
                    }
                return output;
            }

            // Sector offset table: sectors + 1 entries (one more when sector CRCs are present - ignored).
            int entries = sectors + 1;
            var tableBytes = new byte[entries * 4];
            if (RawRead(b.FilePos, tableBytes, 0, tableBytes.Length) != tableBytes.Length) throw new InvalidDataException(Name + ": " + path + " sector table truncated");
            if (encrypted) MpqCrypto.Decrypt(tableBytes, 0, tableBytes.Length, key - 1);
            var offsets = new uint[entries];
            for (int i = 0; i < entries; i++) offsets[i] = U32(tableBytes, i * 4);
            if (offsets[0] != (uint)tableBytes.Length && offsets[0] != (uint)tableBytes.Length + 4)
                throw new InvalidDataException(Name + ": " + path + " bad sector table (first " + offsets[0] + ", expected " + tableBytes.Length + ")");
            uint total = offsets[sectors];
            if (total > b.CompressedSize) throw new InvalidDataException(Name + ": " + path + " sector table ends past the block (" + total + " > " + b.CompressedSize + ")");

            // One read for the whole packed body.
            var body = new byte[total];
            if (RawRead(b.FilePos, body, 0, (int)total) != (int)total) throw new InvalidDataException(Name + ": " + path + " truncated");
            for (int s = 0; s < sectors; s++)
            {
                int start = (int)offsets[s], end = (int)offsets[s + 1];
                if (end < start || end > body.Length) throw new InvalidDataException(Name + ": " + path + " bad sector " + s);
                int outOff = s * sectorSize;
                int outLen = Math.Min(sectorSize, fileSize - outOff);
                if (encrypted) MpqCrypto.Decrypt(body, start, end - start, key + (uint)s);
                Unpack(body, start, end - start, output, outOff, outLen, flags, path);
            }
            return output;
        }

        private void Unpack(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstLen, uint flags, string path)
        {
            if (srcLen == dstLen)
            {
                // Stored as is (a unit that did not shrink is never compressed).
                Buffer.BlockCopy(src, srcOff, dst, dstOff, srcLen);
                return;
            }
            if (srcLen > dstLen) throw new InvalidDataException(Name + ": " + path + " packed sector larger than its data (" + srcLen + " > " + dstLen + ")");
            int got;
            if ((flags & FlagCompress) != 0)
                got = MpqCompression.Decompress(src, srcOff, srcLen, dst, dstOff, dstLen, Name + ":" + path);
            else
                got = PkwareExplode.Explode(src, srcOff, srcLen, dst, dstOff, dstLen);
            if (got != dstLen) throw new InvalidDataException(Name + ": " + path + " unpacked " + got + " of " + dstLen + " bytes");
        }

        /// <summary>The internal (listfile), split into names (empty when absent).</summary>
        public List<string> ReadListFile()
        {
            var list = new List<string>();
            byte[] lf = null;
            try { lf = ReadFile("(listfile)"); } catch (Exception e) { WowLog.Warn("MPQ " + Name + ": (listfile) unreadable: " + e.Message); }
            if (lf == null) return list;
            var text = Encoding.UTF8.GetString(lf);
            foreach (var line in text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_stream != null) { _stream.Dispose(); _stream = null; }
            }
        }

        public override string ToString()
        {
            return Name + " (v" + FormatVersion + ", sector " + SectorSize + ", hash " + HashTableSize + ", blocks " + BlockTableSize + ")";
        }
    }
}
