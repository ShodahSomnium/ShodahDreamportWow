using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// A map's WDL (low-resolution far terrain): MAOF = 64x64 file offsets of MARE chunks, each MARE = 17x17 outer then
    /// 16x16 inner int16 heights (world Z in yards) for one tile, MAHO = 16 x uint16 hole rows. Pure C#.
    /// </summary>
    public sealed class WdlFile
    {
        /// <summary>Per tile [tileY * 64 + tileX]: 17*17 outer heights (row along -X, col along -Y) or null.</summary>
        public readonly short[][] Outer = new short[64 * 64][];
        /// <summary>Per tile: 16*16 inner heights or null.</summary>
        public readonly short[][] Inner = new short[64 * 64][];
        public int TileCount;

        public bool HasTile(int tileX, int tileY)
        {
            return tileX >= 0 && tileY >= 0 && tileX < 64 && tileY < 64 && Outer[tileY * 64 + tileX] != null;
        }

        /// <summary>Outer height (row 0..16, col 0..16) of a tile.</summary>
        public float OuterHeight(int tileX, int tileY, int row, int col) { return Outer[tileY * 64 + tileX][row * 17 + col]; }

        public static WdlFile Parse(byte[] b)
        {
            if (b == null) throw new InvalidDataException("WDL: no data");
            var w = new WdlFile();
            int o = 0;
            int maof = -1;
            while (o + 8 <= b.Length)
            {
                string id = Bin.Magic(b, o);
                int size = Bin.I32(b, o + 4);
                if (size < 0 || o + 8 + size > b.Length) break;
                if (id == "MAOF" && size >= 4096 * 4) maof = o + 8;
                o += 8 + size;
            }
            if (maof < 0) throw new InvalidDataException("WDL: no MAOF");
            for (int i = 0; i < 4096; i++)
            {
                int off = Bin.I32(b, maof + i * 4);
                if (off <= 0 || off + 8 + 545 * 2 > b.Length) continue;
                if (Bin.Magic(b, off) != "MARE") continue;
                int d = off + 8;
                var outer = new short[289];
                var inner = new short[256];
                for (int k = 0; k < 289; k++) outer[k] = Bin.I16(b, d + k * 2);
                for (int k = 0; k < 256; k++) inner[k] = Bin.I16(b, d + 578 + k * 2);
                w.Outer[i] = outer; w.Inner[i] = inner;
                w.TileCount++;
            }
            return w;
        }
    }
}
