using System;
using System.Collections.Generic;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.MapApp
{
    /// <summary>
    /// "Classic" mode: the client's own minimap tiles. Textures\Minimap\md5translate.trs maps "&lt;MapDir&gt;\map&lt;col&gt;_&lt;row&gt;.blp"
    /// to the hashed file under Textures\Minimap (measured: 1.56 MB, "Azeroth\map32_48.blp&lt;TAB&gt;b53fb7...blp"; the same table
    /// World/Unity/FarTerrain reads for its colours). A tile is 256 x 256, covers one ADT (533.33 yd) and its top-left pixel is
    /// the tile's north-west corner (col = floor(32 - Y/533.33), row = floor(32 - X/533.33), as WowAreaLookup.TileOf). Tiles are
    /// read and decoded on pool threads and uploaded a few per frame; about 25 are kept.
    /// </summary>
    public sealed class WowMapTiles
    {
        public const float TileYards = 1600f / 3f;
        private const int Keep = 25;

        private readonly object _lock = new object();
        private WowFiles _files;
        private int _map = -1;
        private string _dir;
        private Dictionary<string, string> _table;
        private bool _tableLoading, _tableFailed;
        private readonly Dictionary<int, Texture2D> _tex = new Dictionary<int, Texture2D>();
        private readonly HashSet<int> _pending = new HashSet<int>();
        private readonly HashSet<int> _missing = new HashSet<int>();
        private readonly List<Ready> _ready = new List<Ready>();
        private int _generation;
        private int _loaded, _absent;

        private struct Ready
        {
            public int Key, Generation, W, H;
            public byte[] Rgba;
        }

        public string Status
        {
            get { return "classic map: " + (_dir ?? "-") + ", table " + (_table != null ? _table.Count + " entries" : _tableFailed ? "unreadable" : "loading") + ", " + _loaded + " tiles loaded, " + _absent + " absent"; }
        }

        /// <summary>Lead 2026-10-03 (the minimap, UiMinimap): +1 per tile uploaded - a reader re-places its tiles when this changed.</summary>
        public int Uploads { get { return _loaded; } }

        public static int Key(int col, int row) { return row * 64 + col; }

        public static void TileOf(float x, float y, out int col, out int row)
        {
            col = (int)Math.Floor(32.0 - y / TileYards);
            row = (int)Math.Floor(32.0 - x / TileYards);
        }

        /// <summary>The map whose tiles are wanted (Map.dbc Directory). A new map drops the old tiles.</summary>
        public void SetMap(WowFiles files, int mapId)
        {
            if (files == _files && mapId == _map) return;
            if (files != _files) lock (_lock) { _table = null; _tableFailed = false; }
            Clear();
            _files = files;
            _map = mapId;
            _dir = null;
            if (files == null || mapId < 0) return;
            try
            {
                DbcTable map = Dbc.Load(files, "Map");
                int r = map != null ? map.FindRow(mapId) : -1;
                _dir = r >= 0 ? map.GetString(r, DbcFields.Map.Directory) : null;
            }
            catch (Exception e)
            {
                WowLog.Warn("Map: Map.dbc row " + mapId + " unreadable: " + e.Message);
            }
            EnsureTable();
        }

        private void EnsureTable()
        {
            if (_table != null || _tableLoading || _tableFailed || _files == null) return;
            _tableLoading = true;
            WowFiles files = _files;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Dictionary<string, string> d = null;
                try
                {
                    byte[] b = files.Read("Textures\\Minimap\\md5translate.trs");
                    if (b != null)
                    {
                        d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string text = System.Text.Encoding.ASCII.GetString(b);
                        foreach (string raw in text.Split('\n'))
                        {
                            string line = raw.Trim();
                            int tab = line.IndexOf('\t');
                            if (tab <= 0) continue;
                            d[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
                        }
                    }
                }
                catch (Exception)
                {
                    d = null;
                }
                lock (_lock)
                {
                    _table = d;
                    _tableFailed = d == null;
                    _tableLoading = false;
                }
                WowLog.Info("Map: minimap table " + (d != null ? d.Count + " entries" : "missing/unreadable") + ".");
            });
        }

        /// <summary>The tile's texture, or null while it loads / when the game has none there (open sea).</summary>
        public Texture2D Get(int col, int row)
        {
            if (col < 0 || col > 63 || row < 0 || row > 63 || _dir == null) return null;
            int key = Key(col, row);
            Texture2D t;
            if (_tex.TryGetValue(key, out t)) return t;
            if (_missing.Contains(key) || _pending.Contains(key)) return null;
            Dictionary<string, string> table;
            lock (_lock) table = _table;
            if (table == null) { EnsureTable(); return null; }
            string hash;
            if (!table.TryGetValue(_dir + "\\map" + col + "_" + row + ".blp", out hash))
            {
                _missing.Add(key);
                _absent++;
                return null;
            }
            _pending.Add(key);
            WowFiles files = _files;
            int generation = _generation;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = new Ready { Key = key, Generation = generation };
                try
                {
                    byte[] b = files.Read("Textures\\Minimap\\" + hash);
                    if (b != null)
                    {
                        BlpImage img = BlpImage.Decode(b);
                        r.W = img.Width;
                        r.H = img.Height;
                        r.Rgba = img.GetRgba32(0);
                    }
                }
                catch (Exception)
                {
                    r.Rgba = null;
                }
                lock (_lock) _ready.Add(r);
            });
            return null;
        }

        /// <summary>Main thread, once a frame: uploads up to two decoded tiles; drops the tiles furthest from centre beyond the cap.</summary>
        public void Pump(int centreCol, int centreRow)
        {
            for (int n = 0; n < 2; n++)
            {
                Ready r;
                lock (_lock)
                {
                    if (_ready.Count == 0) break;
                    r = _ready[_ready.Count - 1];
                    _ready.RemoveAt(_ready.Count - 1);
                }
                if (r.Generation != _generation) continue;
                _pending.Remove(r.Key);
                if (r.Rgba == null || r.W <= 0)
                {
                    _missing.Add(r.Key);
                    _absent++;
                    continue;
                }
                Texture2D t = WowTextures.FromRgba32(r.W, r.H, r.Rgba, WowTextureOptions.Ui, "WoW minimap " + (r.Key % 64) + "_" + (r.Key / 64), false);
                _tex[r.Key] = t;
                _loaded++;
            }
            if (_tex.Count > Keep) Evict(centreCol, centreRow);
        }

        private void Evict(int cc, int cr)
        {
            int worst = -1, worstD = -1;
            foreach (KeyValuePair<int, Texture2D> kv in _tex)
            {
                int d = Math.Abs(kv.Key % 64 - cc) + Math.Abs(kv.Key / 64 - cr);
                if (d > worstD) { worstD = d; worst = kv.Key; }
            }
            if (worst < 0) return;
            Texture2D t = _tex[worst];
            _tex.Remove(worst);
            if (t != null) UnityEngine.Object.Destroy(t);
        }

        public void Clear()
        {
            _generation++;
            foreach (KeyValuePair<int, Texture2D> kv in _tex)
            {
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            }
            _tex.Clear();
            _pending.Clear();
            _missing.Clear();
            lock (_lock) _ready.Clear();
        }
    }
}
