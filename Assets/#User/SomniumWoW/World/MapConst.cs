using System;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// WoW map grid constants and the conversions between ADT/WDT indexing, MDDF/MODF placement space and WoW world
    /// space (yards; X north, Y west, Z up). Pure C# (runs in the harness). Measured on the real files (reports/terrain.md):
    /// - WDT MAIN entry [row*64+col] is file Map_col_row.adt. Here tileX = the FIRST number of the file name, tileY = the second.
    /// - A tile's corner with the largest world X and Y is ((32 - tileY) * TileSize, (32 - tileX) * TileSize).
    /// - MCNK chunk indexX runs toward -Y (east), indexY toward -X (south). MCNK position (@0x68) is the world corner.
    /// - MDDF/MODF positions p are "placement space": worldX = ZeroPoint - p[2], worldY = ZeroPoint - p[0], worldZ = p[1].
    /// </summary>
    public static class MapConst
    {
        public const float TileSize = 1600f / 3f;          // 533.333 yards
        public const float ChunkSize = TileSize / 16f;     // 33.333 yards
        public const float UnitSize = ChunkSize / 8f;      // 4.1667 yards (one MCVT cell)
        public const float ZeroPoint = 32f * TileSize;     // 17066.666 yards
        /// <summary>Terrain diffuse textures repeat this many times across one chunk (noggit3 World.h detail_size = 8).</summary>
        public const float TextureRepeatsPerChunk = 8f;

        /// <summary>The tile (tileX = first file-name number, tileY = second) holding world (X, Y).</summary>
        public static void WorldToTile(float worldX, float worldY, out int tileX, out int tileY)
        {
            tileX = (int)Math.Floor(32.0 - worldY / TileSize);
            tileY = (int)Math.Floor(32.0 - worldX / TileSize);
        }

        /// <summary>World X of a tile's north edge (its largest X).</summary>
        public static float TileMaxX(int tileY) { return (32 - tileY) * TileSize; }

        /// <summary>World Y of a tile's west edge (its largest Y).</summary>
        public static float TileMaxY(int tileX) { return (32 - tileX) * TileSize; }

        /// <summary>World X/Y of the centre of a tile.</summary>
        public static void TileCenter(int tileX, int tileY, out float worldX, out float worldY)
        {
            worldX = TileMaxX(tileY) - TileSize * 0.5f;
            worldY = TileMaxY(tileX) - TileSize * 0.5f;
        }

        /// <summary>MDDF/MODF placement position -> world position.</summary>
        public static void PlacementToWorld(float px, float py, float pz, out float wx, out float wy, out float wz)
        {
            wx = ZeroPoint - pz;
            wy = ZeroPoint - px;
            wz = py;
        }


        /// <summary>
        /// The world-space rotation (3x3, row-major m[r*3+c], acting on column vectors in model space: X forward, Y left,
        /// Z up) of an MDDF/MODF placement with rotation (rx, ry, rz) in degrees as stored. world = pos + scale * M * model.
        /// MEASURED (probe TerrainRotations, 3726 unique MODF placements of 4 continents): M = Rz(ry + 180) * Ry(rx) * Rx(rz)
        /// reproduces the stored MODF extents from each WMO's MOHD box within 1 yard for 3657/3726 placements and 690/696
        /// of the tilted ones (|rx| or |rz| > 2 degrees); every other axis/sign/order/offset combination scores far lower.
        /// </summary>
        public static void PlacementRotation(float rx, float ry, float rz, float[] m)
        {
            const float D = (float)(Math.PI / 180.0);
            float yaw = (ry + 180f) * D, pitch = rx * D, roll = rz * D;
            // M = Rz(yaw) * Ry(pitch) * Rx(roll)
            float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
            float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
            float cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);
            m[0] = cy * cp; m[1] = cy * sp * sr - sy * cr; m[2] = cy * sp * cr + sy * sr;
            m[3] = sy * cp; m[4] = sy * sp * sr + cy * cr; m[5] = sy * sp * cr - cy * sr;
            m[6] = -sp;     m[7] = cp * sr;                m[8] = cp * cr;
        }
    }

    /// <summary>Little-endian reads and chunk magic helpers for the map formats.</summary>
    public static class Bin
    {
        public static int I32(byte[] b, int o) { return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24); }
        public static uint U32(byte[] b, int o) { return (uint)I32(b, o); }
        public static ushort U16(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }
        public static short I16(byte[] b, int o) { return (short)(b[o] | (b[o + 1] << 8)); }
        public static float F32(byte[] b, int o) { return BitConverter.ToSingle(b, o); }

        /// <summary>A chunk id as stored reversed in the file ("REVM") -> as written in the docs ("MVER").</summary>
        public static string Magic(byte[] b, int o)
        {
            if (o < 0 || o + 4 > b.Length) return "????";
            return new string(new[] { (char)b[o + 3], (char)b[o + 2], (char)b[o + 1], (char)b[o] });
        }

        /// <summary>The NUL-terminated ASCII string at o, bounded by end.</summary>
        public static string CString(byte[] b, int o, int end)
        {
            if (o < 0 || o >= end) return "";
            int e = o;
            while (e < end && b[e] != 0) e++;
            return Encoding.ASCII.GetString(b, o, e - o);
        }
    }
}
