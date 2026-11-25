using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The zone name at a position, for the in-world info label (pure; the harness probe FrontArea runs it): the ADT tile
    /// under the position (WDT MAIN [row*64+col] = &lt;Map&gt;_&lt;col&gt;_&lt;row&gt;.adt, col = floor(32 - Y/533.333), row =
    /// floor(32 - X/533.333) - the Data report's measured rule), its 256 MCNK headers (areaid at +0x34, the chunk's corner
    /// world X, Y, Z at +0x68), the chunk whose 33.333-yard square below-left of the corner holds the position, then
    /// AreaTable.dbc ("Subzone, Zone" through ParentAreaID). One tile is kept; reading it is meant for a worker thread.
    /// </summary>
    public sealed class WowAreaLookup
    {
        public const float TileSize = 533.33333f;
        public const float ChunkSize = TileSize / 16f;

        private int _map = -1, _col = -1, _row = -1;
        private readonly float[] _cornerX = new float[256];
        private readonly float[] _cornerY = new float[256];
        private readonly int[] _area = new int[256];
        private int _chunks;

        public static void TileOf(float x, float y, out int col, out int row)
        {
            col = (int)Math.Floor(32.0 - y / TileSize);
            row = (int)Math.Floor(32.0 - x / TileSize);
        }

        /// <summary>The ADT path of the tile holding (x, y) on mapId, or null when Map.dbc has no row.</summary>
        public static string AdtPath(WowFiles files, int mapId, float x, float y)
        {
            DbcTable map = Dbc.Load(files, "Map");
            int r = map != null ? map.FindRow(mapId) : -1;

            if (r < 0)
            {
                return null;
            }

            int col, row;
            TileOf(x, y, out col, out row);
            string dir = map.GetString(r, DbcFields.Map.Directory);
            return @"World\Maps\" + dir + @"\" + dir + "_" + col + "_" + row + ".adt";
        }

        /// <summary>AreaTable id at (x, y) yards on mapId (0 when unknown). Reads the tile's ADT when it changed.</summary>
        public int AreaAt(WowFiles files, int mapId, float x, float y)
        {
            int col, row;
            TileOf(x, y, out col, out row);

            if (mapId != _map || col != _col || row != _row)
            {
                _map = mapId;
                _col = col;
                _row = row;
                _chunks = 0;
                string path = AdtPath(files, mapId, x, y);
                byte[] adt = path != null ? files.Read(path) : null;

                if (adt != null)
                {
                    ReadChunks(adt);
                }
            }

            for (int i = 0; i < _chunks; i++)
            {
                if (x <= _cornerX[i] && x > _cornerX[i] - ChunkSize && y <= _cornerY[i] && y > _cornerY[i] - ChunkSize)
                {
                    return _area[i];
                }
            }

            return 0;
        }

        private void ReadChunks(byte[] b)
        {
            int p = 0;

            while (p + 8 <= b.Length && _chunks < 256)
            {
                // Chunk ids are stored reversed: "KNCM" on disk is MCNK.
                bool mcnk = b[p] == (byte)'K' && b[p + 1] == (byte)'N' && b[p + 2] == (byte)'C' && b[p + 3] == (byte)'M';
                int size = BitConverter.ToInt32(b, p + 4);

                if (size < 0 || p + 8 + size > b.Length)
                {
                    break;
                }

                if (mcnk && size >= 0x74)
                {
                    int h = p + 8;
                    _area[_chunks] = BitConverter.ToInt32(b, h + 0x34);
                    _cornerX[_chunks] = BitConverter.ToSingle(b, h + 0x68);
                    _cornerY[_chunks] = BitConverter.ToSingle(b, h + 0x6C);
                    _chunks++;
                }

                p += 8 + size;
            }
        }

        public int ChunksRead { get { return _chunks; } }

        /// <summary>"Subzone, Zone" (or the zone alone) from AreaTable.dbc, or null.</summary>
        public static string AreaName(WowFiles files, int areaId)
        {
            DbcTable area = Dbc.Load(files, "AreaTable");
            int r = area != null && areaId > 0 ? area.FindRow(areaId) : -1;

            if (r < 0)
            {
                return null;
            }

            string name = area.GetLocalizedString(r, DbcFields.AreaTable.AreaName_lang);
            int parent = area.GetInt(r, DbcFields.AreaTable.ParentAreaID);
            int pr = parent > 0 ? area.FindRow(parent) : -1;
            return pr >= 0 ? name + ", " + area.GetLocalizedString(pr, DbcFields.AreaTable.AreaName_lang) : name;
        }
    }
}
