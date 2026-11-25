using System;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// One MH2O liquid instance of a chunk (3.3.5a): a rectangle of cells (XOffset/YOffset/Width/Height in the chunk's 8x8
    /// cells, x along indexX / -Y, y along indexY / -X) with a per-cell exists mask and optional per-vertex heights
    /// ((Width+1) x (Height+1), row-major, absolute world Z). Heights null = flat at MinHeight.
    /// </summary>
    public sealed class AdtLiquidInstance
    {
        public int LiquidType;        // LiquidType.dbc id
        public int VertexFormat;      // LVF 0 height+depth, 1 height+uv, 2 depth only, 3 height+uv+depth
        public float MinHeight, MaxHeight;
        public int XOffset, YOffset, Width, Height;
        /// <summary>Exists mask for Width*Height cells, bit (y * Width + x), LSB first. All set when the file had none.</summary>
        public ulong Exists;
        /// <summary>(Width+1)*(Height+1) heights or null.</summary>
        public float[] Heights;
        /// <summary>(Width+1)*(Height+1) depths (0..255) or null.</summary>
        public byte[] Depths;

        public bool CellExists(int x, int y)
        {
            int bit = y * Width + x;
            return bit < 64 && ((Exists >> bit) & 1UL) != 0;
        }

        /// <summary>Height at vertex (vx 0..Width, vy 0..Height).</summary>
        public float HeightAt(int vx, int vy)
        {
            if (Heights == null) return MinHeight;
            return Heights[vy * (Width + 1) + vx];
        }
    }

    /// <summary>Legacy MCLQ (pre-WotLK) liquid inside an MCNK: 9x9 heights (absolute), 8x8 tile flags.</summary>
    public sealed class AdtLegacyLiquid
    {
        public int Kind;              // 0 water (river), 1 ocean, 2 magma, 3 slime (from the MCNK flags)
        public float MinHeight, MaxHeight;
        public readonly float[] Heights = new float[81];
        /// <summary>8x8 tile flags; (flag &amp; 0x0F) == 0x0F means "no liquid here".</summary>
        public readonly byte[] Tiles = new byte[64];

        public bool TileExists(int x, int y) { return (Tiles[y * 8 + x] & 0x0F) != 0x0F; }
    }

    /// <summary>MH2O / MCLQ parsing. Pure C#.</summary>
    public static class AdtLiquid
    {
        /// <summary>MH2O at data offset d (offsets inside are relative to d). Result by [indexY * 16 + indexX].</summary>
        public static AdtLiquidInstance[][] ParseMh2o(byte[] b, int d, int size)
        {
            var result = new AdtLiquidInstance[256][];
            int end = d + size;
            for (int c = 0; c < 256; c++)
            {
                int h = d + c * 12;
                int ofsInst = Bin.I32(b, h);
                int count = Bin.I32(b, h + 4);
                if (count <= 0 || ofsInst <= 0 || count > 16) continue;
                var list = new AdtLiquidInstance[count];
                int n = 0;
                for (int i = 0; i < count; i++)
                {
                    int p = d + ofsInst + i * 24;
                    if (p + 24 > end) break;
                    var li = new AdtLiquidInstance();
                    li.LiquidType = Bin.U16(b, p);
                    li.VertexFormat = Bin.U16(b, p + 2);
                    li.MinHeight = Bin.F32(b, p + 4);
                    li.MaxHeight = Bin.F32(b, p + 8);
                    li.XOffset = b[p + 12]; li.YOffset = b[p + 13];
                    li.Width = b[p + 14]; li.Height = b[p + 15];
                    int ofsExists = Bin.I32(b, p + 16);
                    int ofsVert = Bin.I32(b, p + 20);
                    if (li.Width < 1 || li.Height < 1 || li.XOffset + li.Width > 8 || li.YOffset + li.Height > 8) continue;
                    int cells = li.Width * li.Height;
                    if (ofsExists > 0 && d + ofsExists + (cells + 7) / 8 <= end)
                    {
                        ulong m = 0;
                        int bytes = (cells + 7) / 8;
                        for (int k = 0; k < bytes; k++) m |= (ulong)b[d + ofsExists + k] << (8 * k);
                        li.Exists = m;
                    }
                    else li.Exists = cells >= 64 ? ulong.MaxValue : ((1UL << cells) - 1UL);
                    int verts = (li.Width + 1) * (li.Height + 1);
                    if (ofsVert > 0)
                    {
                        int v = d + ofsVert;
                        int lvf = li.VertexFormat;
                        bool hasHeight = lvf == 0 || lvf == 1 || lvf == 3;
                        bool hasUv = lvf == 1 || lvf == 3;
                        bool hasDepth = lvf == 0 || lvf == 2 || lvf == 3;
                        if (hasHeight && v + verts * 4 <= end)
                        {
                            li.Heights = new float[verts];
                            for (int k = 0; k < verts; k++) li.Heights[k] = Bin.F32(b, v + k * 4);
                            v += verts * 4;
                        }
                        if (hasUv) v += verts * 4;
                        if (hasDepth && v + verts <= end)
                        {
                            li.Depths = new byte[verts];
                            Buffer.BlockCopy(b, v, li.Depths, 0, verts);
                        }
                    }
                    list[n++] = li;
                }
                if (n == 0) continue;
                if (n < count) Array.Resize(ref list, n);
                result[c] = list;
            }
            return result;
        }

        /// <summary>
        /// MCLQ data (after its 8-byte header) up to end. Layout: CRange (2 floats), 81 vertices of 8 bytes (4 bytes
        /// depth/flow or uv, then float height), 64 tile bytes, then flow data (ignored).
        /// </summary>
        public static AdtLegacyLiquid ParseMclq(byte[] b, int o, int end, uint chunkFlags)
        {
            if (o + 8 + 81 * 8 + 64 > end) return null;
            var l = new AdtLegacyLiquid();
            l.Kind = (chunkFlags & 0x8) != 0 ? 1 : (chunkFlags & 0x10) != 0 ? 2 : (chunkFlags & 0x20) != 0 ? 3 : 0;
            l.MinHeight = Bin.F32(b, o); l.MaxHeight = Bin.F32(b, o + 4);
            for (int i = 0; i < 81; i++) l.Heights[i] = Bin.F32(b, o + 8 + i * 8 + 4);
            Buffer.BlockCopy(b, o + 8 + 81 * 8, l.Tiles, 0, 64);
            return l;
        }
    }
}
