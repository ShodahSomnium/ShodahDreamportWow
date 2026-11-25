using System;

namespace SomniumSpace.Worlds.SomniumWoW.Data.Mpq
{
    /// <summary>
    /// The MPQ crypt table, name hashing and block decryption, as StormLib does it (SBaseCommon.cpp:
    /// InitializeMpqCryptography, HashString, DecryptMpqBlock). Pure, thread-safe (the table is built once).
    /// Names are hashed upper-cased (ASCII only) with '/' turned into '\', exactly like StormLib's AsciiToUpperTable.
    /// </summary>
    public static class MpqCrypto
    {
        public const uint HashTableIndex = 0x000;
        public const uint HashNameA = 0x100;
        public const uint HashNameB = 0x200;
        public const uint HashFileKey = 0x300;

        private static readonly uint[] Table = Build();

        /// <summary>Key of the hash table: Hash("(hash table)", FileKey) = 0xC3AF3770.</summary>
        public static readonly uint HashTableKey = Hash("(hash table)", HashFileKey);

        /// <summary>Key of the block table: Hash("(block table)", FileKey) = 0xEC83B3A3.</summary>
        public static readonly uint BlockTableKey = Hash("(block table)", HashFileKey);

        private static uint[] Build()
        {
            var t = new uint[0x500];
            uint seed = 0x00100001;
            for (uint i1 = 0; i1 < 0x100; i1++)
            {
                uint i2 = i1;
                for (int i = 0; i < 5; i++, i2 += 0x100)
                {
                    seed = (seed * 125 + 3) % 0x2AAAAB;
                    uint t1 = (seed & 0xFFFF) << 0x10;
                    seed = (seed * 125 + 3) % 0x2AAAAB;
                    uint t2 = seed & 0xFFFF;
                    t[i2] = t1 | t2;
                }
            }
            return t;
        }

        private static uint Upper(char c)
        {
            if (c == '/') return '\\';
            if (c >= 'a' && c <= 'z') return (uint)(c - 32);
            return (uint)(c & 0xFF);
        }

        /// <summary>StormLib HashString with the given hash type (0x000 index, 0x100 name A, 0x200 name B, 0x300 file key).</summary>
        public static uint Hash(string name, uint hashType)
        {
            uint seed1 = 0x7FED7FED, seed2 = 0xEEEEEEEE;
            for (int i = 0; i < name.Length; i++)
            {
                uint ch = Upper(name[i]);
                seed1 = Table[hashType + ch] ^ (seed1 + seed2);
                seed2 = ch + seed1 + seed2 + (seed2 << 5) + 3;
            }
            return seed1;
        }

        /// <summary>The file key of an encrypted file: hash of the plain name (after the last '\' or '/').</summary>
        public static uint FileKey(string path, uint blockOffset, uint fileSize, bool fixKey)
        {
            int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string plain = cut >= 0 ? path.Substring(cut + 1) : path;
            uint key = Hash(plain, HashFileKey);
            if (fixKey) key = (key + blockOffset) ^ fileSize;
            return key;
        }

        /// <summary>Decrypts whole little-endian uint32s in place (a trailing partial uint stays as is, like StormLib).</summary>
        public static void Decrypt(byte[] data, int offset, int length, uint key)
        {
            uint seed = 0xEEEEEEEE;
            int n = length >> 2;
            for (int i = 0; i < n; i++)
            {
                int p = offset + i * 4;
                uint v = (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));
                seed += Table[0x400 + (key & 0xFF)];
                uint ch = v ^ (key + seed);
                key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
                seed = ch + seed + (seed << 5) + 3;
                data[p] = (byte)ch; data[p + 1] = (byte)(ch >> 8); data[p + 2] = (byte)(ch >> 16); data[p + 3] = (byte)(ch >> 24);
            }
        }

        /// <summary>Decrypts a uint array in place.</summary>
        public static void Decrypt(uint[] data, uint key)
        {
            uint seed = 0xEEEEEEEE;
            for (int i = 0; i < data.Length; i++)
            {
                seed += Table[0x400 + (key & 0xFF)];
                uint ch = data[i] ^ (key + seed);
                key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
                seed = ch + seed + (seed << 5) + 3;
                data[i] = ch;
            }
        }
    }
}
