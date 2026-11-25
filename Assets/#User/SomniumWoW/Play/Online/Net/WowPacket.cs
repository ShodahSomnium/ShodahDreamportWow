using System;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>Thrown by WowPacket readers when a packet is shorter than its layout (the dispatcher logs it per opcode).</summary>
    public sealed class PacketTooShortException : Exception
    {
        public PacketTooShortException(string m) : base(m) { }
    }

    /// <summary>
    /// One received world packet (payload WITHOUT the header, already decrypted and - for SMSG_COMPRESSED_* - inflated).
    /// Little-endian readers like AzerothCore's ByteBuffer. Pos is reset to 0 before every handler call.
    /// </summary>
    public sealed class WowPacket
    {
        public ushort Opcode;
        public byte[] Data;
        public int Pos;
        public int Length { get { return Data.Length; } }
        public int Remaining { get { return Data.Length - Pos; } }
        public string Name { get { return Op.Name(Opcode); } }

        public WowPacket(ushort opcode, byte[] data) { Opcode = opcode; Data = data ?? new byte[0]; }

        void Need(int n)
        {
            if (Pos + n > Data.Length)
                throw new PacketTooShortException(Op.Name(Opcode) + ": needs " + n + " bytes at " + Pos + " of " + Data.Length);
        }

        public byte U8() { Need(1); return Data[Pos++]; }
        public bool Bool() { return U8() != 0; }
        public ushort U16() { Need(2); ushort v = (ushort)(Data[Pos] | Data[Pos + 1] << 8); Pos += 2; return v; }
        public uint U32() { Need(4); uint v = BitConverterLE.U32(Data, Pos); Pos += 4; return v; }
        public int I32() { return (int)U32(); }
        public ulong U64() { Need(8); ulong v = BitConverterLE.U32(Data, Pos) | (ulong)BitConverterLE.U32(Data, Pos + 4) << 32; Pos += 8; return v; }
        public float F32() { Need(4); float v = BitConverterLE.F32(Data, Pos); Pos += 4; return v; }

        public byte[] Bytes(int n) { Need(n); var r = new byte[n]; Array.Copy(Data, Pos, r, 0, n); Pos += n; return r; }
        public void Skip(int n) { Need(n); Pos += n; }

        /// <summary>Null-terminated UTF-8 string (ByteBuffer operator&gt;&gt;(std::string&amp;)). A missing terminator reads to the end.</summary>
        public string CString()
        {
            int start = Pos;
            while (Pos < Data.Length && Data[Pos] != 0) Pos++;
            string s = Encoding.UTF8.GetString(Data, start, Pos - start);
            if (Pos < Data.Length) Pos++;
            return s;
        }

        /// <summary>Packed guid: a mask byte, then one byte for every set bit (ByteBuffer::readPackGUID).</summary>
        public ulong PackedGuid()
        {
            byte mask = U8();
            ulong g = 0;
            for (int i = 0; i < 8; i++)
                if ((mask & (1 << i)) != 0) g |= (ulong)U8() << (i * 8);
            return g;
        }
    }

    /// <summary>Little-endian packet body builder (ByteBuffer operator&lt;&lt;).</summary>
    public sealed class PacketWriter
    {
        readonly MemoryStream _s = new MemoryStream(64);

        public int Length { get { return (int)_s.Length; } }
        public PacketWriter U8(int v) { _s.WriteByte((byte)v); return this; }
        public PacketWriter Bool(bool v) { _s.WriteByte(v ? (byte)1 : (byte)0); return this; }
        public PacketWriter U16(int v) { _s.WriteByte((byte)v); _s.WriteByte((byte)(v >> 8)); return this; }
        public PacketWriter U32(uint v) { for (int i = 0; i < 4; i++) _s.WriteByte((byte)(v >> (i * 8))); return this; }
        public PacketWriter I32(int v) { return U32((uint)v); }
        public PacketWriter U64(ulong v) { for (int i = 0; i < 8; i++) _s.WriteByte((byte)(v >> (i * 8))); return this; }
        public PacketWriter F32(float v) { return U32(BitConverterLE.FloatBits(v)); }
        public PacketWriter Bytes(byte[] b) { _s.Write(b, 0, b.Length); return this; }
        public PacketWriter Bytes(byte[] b, int off, int n) { _s.Write(b, off, n); return this; }

        /// <summary>UTF-8 + null terminator.</summary>
        public PacketWriter CString(string s)
        {
            if (!string.IsNullOrEmpty(s)) { var b = Encoding.UTF8.GetBytes(s); _s.Write(b, 0, b.Length); }
            _s.WriteByte(0);
            return this;
        }

        /// <summary>Packed guid (ByteBuffer::appendPackGUID).</summary>
        public PacketWriter PackedGuid(ulong g)
        {
            byte mask = 0;
            var tmp = new byte[8];
            int n = 0;
            for (int i = 0; i < 8; i++)
            {
                byte b = (byte)(g >> (i * 8));
                if (b != 0) { mask |= (byte)(1 << i); tmp[n++] = b; }
            }
            _s.WriteByte(mask);
            _s.Write(tmp, 0, n);
            return this;
        }

        public byte[] ToArray() { return _s.ToArray(); }
    }

    /// <summary>Endian-explicit helpers: integers are assembled arithmetically, floats go through the same-machine bit pattern.</summary>
    public static class BitConverterLE
    {
        public static uint U32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }

        public static float F32(byte[] b, int o)
        {
            uint u = U32(b, o);
            return BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
        }

        public static uint FloatBits(float v)
        {
            uint u = BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);
            return u;
        }
    }
}
