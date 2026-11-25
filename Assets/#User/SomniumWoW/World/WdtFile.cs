using System;
using System.IO;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// A map's WDT (World\Maps\&lt;Dir&gt;\&lt;Dir&gt;.wdt): MPHD flags, MAIN tile table (64x64, entry [row*64+col] = file
    /// Dir_col_row.adt, measured by Data 687/687), and for WMO-only maps (dungeons) the global WMO (MWMO + MODF).
    /// Pure C#.
    /// </summary>
    public sealed class WdtFile
    {
        /// <summary>MPHD flags. 0x1 = WMO-only map (global WMO, no ADTs), 0x2 = ADTs have MCCV, 0x4 = big alpha (8-bit
        /// 4096-byte alpha maps, compressed allowed), 0x8 = doodads sorted by size, 0x80 = height texturing (also big alpha).</summary>
        public uint Flags;
        /// <summary>MAIN flags per tile, index [tileY * 64 + tileX] (bit 0 = ADT exists).</summary>
        public readonly uint[] Tiles = new uint[64 * 64];
        public int TileCount;
        /// <summary>WMO-only maps: the global WMO path, else null.</summary>
        public string GlobalWmo;
        /// <summary>WMO-only maps: its placement (MODF), valid when GlobalWmo != null.</summary>
        public AdtWmoDef GlobalWmoDef;

        public bool BigAlpha { get { return (Flags & 0x4) != 0 || (Flags & 0x80) != 0; } }
        public bool IsWmoOnly { get { return GlobalWmo != null && TileCount == 0; } }

        /// <summary>Iteration 11 (world): the global WMO's placement as AzerothCore's vmap extractor stands it in the world
        /// (vmap4_extractor_wmo.cpp:559-570: `if (x == 0 && z == 0) x = z = 533.33333f * 32` before fixCoords), so a MODF at placement
        /// (0, y, 0) stands at world (0, 0) where the server puts the player, not 17066 yd away. The extents move with it (ours: AC moves
        /// only the position; our loader waits for the WMO by its extents). Rotation and every other field unchanged. A non-zero MODF
        /// position is returned as is. Only meaningful when GlobalWmo != null.</summary>
        public AdtWmoDef GlobalWmoWorldDef
        {
            get
            {
                var d = GlobalWmoDef;
                if (GlobalWmo == null || d.PX != 0f || d.PZ != 0f) return d;
                d.PX = MapConst.ZeroPoint; d.PZ = MapConst.ZeroPoint;
                d.MinX += MapConst.ZeroPoint; d.MaxX += MapConst.ZeroPoint;
                d.MinZ += MapConst.ZeroPoint; d.MaxZ += MapConst.ZeroPoint;
                return d;
            }
        }

        public bool HasTile(int tileX, int tileY)
        {
            if (tileX < 0 || tileY < 0 || tileX > 63 || tileY > 63) return false;
            return (Tiles[tileY * 64 + tileX] & 1) != 0;
        }

        public static string PathOf(string mapDirectory) { return "World\\Maps\\" + mapDirectory + "\\" + mapDirectory + ".wdt"; }

        public static string AdtPath(string mapDirectory, int tileX, int tileY)
        {
            return "World\\Maps\\" + mapDirectory + "\\" + mapDirectory + "_" + tileX + "_" + tileY + ".adt";
        }

        public static string WdlPath(string mapDirectory) { return "World\\Maps\\" + mapDirectory + "\\" + mapDirectory + ".wdl"; }

        /// <summary>Throws InvalidDataException on a broken file.</summary>
        public static WdtFile Parse(byte[] b)
        {
            if (b == null) throw new InvalidDataException("WDT: no data");
            var w = new WdtFile();
            int o = 0;
            while (o + 8 <= b.Length)
            {
                string id = Bin.Magic(b, o);
                int size = Bin.I32(b, o + 4);
                int d = o + 8;
                if (size < 0 || d + size > b.Length) throw new InvalidDataException("WDT: chunk " + id + " overruns the file at " + o);
                switch (id)
                {
                    case "MPHD": w.Flags = Bin.U32(b, d); break;
                    case "MAIN":
                        for (int i = 0; i < 4096 && i * 8 + 8 <= size; i++)
                        {
                            w.Tiles[i] = Bin.U32(b, d + i * 8);
                            if ((w.Tiles[i] & 1) != 0) w.TileCount++;
                        }
                        break;
                    case "MWMO":
                        if (size > 1) { string s = Bin.CString(b, d, d + size); if (s.Length > 0) w.GlobalWmo = s; }
                        break;
                    case "MODF":
                        if (size >= 64) w.GlobalWmoDef = AdtWmoDef.Read(b, d);
                        break;
                }
                o = d + size;
            }
            return w;
        }
    }
}
