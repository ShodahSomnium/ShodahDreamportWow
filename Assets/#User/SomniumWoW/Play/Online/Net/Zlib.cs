using System;
using System.IO;
using System.IO.Compression;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// zlib (RFC 1950) around System.IO.Compression.DeflateStream (raw RFC 1951). AzerothCore compresses with zlib's
    /// deflate()/compress() (WorldSocket.cpp compressBuff, "buf.put&lt;uint32&gt;(0, pSize)" then the zlib stream, opcode
    /// SMSG_COMPRESSED_UPDATE_OBJECT) and reads the client's addon block with uncompress() (WorldSession::ReadAddonsInfo).
    /// </summary>
    public static class Zlib
    {
        /// <summary>Inflates a zlib stream (2-byte header, deflate data, adler32) into exactly expectedSize bytes.</summary>
        public static byte[] Decompress(byte[] src, int offset, int count, int expectedSize)
        {
            if (count < 2) throw new InvalidDataException("zlib: too short");
            int cmf = src[offset], flg = src[offset + 1];
            if ((cmf & 0x0F) != 8 || ((cmf << 8) | flg) % 31 != 0) throw new InvalidDataException("zlib: bad header");
            if ((flg & 0x20) != 0) throw new InvalidDataException("zlib: preset dictionary not supported");
            if (expectedSize < 0 || expectedSize > 16 * 1024 * 1024) throw new InvalidDataException("zlib: bad size " + expectedSize);
            var outBuf = new byte[expectedSize];
            using (var ms = new MemoryStream(src, offset + 2, count - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < expectedSize)
                {
                    int n = ds.Read(outBuf, got, expectedSize - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got != expectedSize) throw new InvalidDataException("zlib: inflated " + got + " of " + expectedSize + " bytes");
            }
            return outBuf;
        }

        /// <summary>zlib-wraps data (header 78 9C, deflate, big-endian adler32).</summary>
        public static byte[] Compress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(data, 0, data.Length);
                uint a = Adler32(data);
                ms.WriteByte((byte)(a >> 24)); ms.WriteByte((byte)(a >> 16)); ms.WriteByte((byte)(a >> 8)); ms.WriteByte((byte)a);
                return ms.ToArray();
            }
        }

        public static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++) { a = (a + data[i]) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        /// <summary>SMSG_COMPRESSED_* payload: uint32 uncompressed size, then the zlib stream.</summary>
        public static byte[] InflatePacket(byte[] payload)
        {
            if (payload.Length < 6) throw new InvalidDataException("compressed packet too short");
            int size = (int)BitConverterLE.U32(payload, 0);
            return Decompress(payload, 4, payload.Length - 4, size);
        }
    }
}
