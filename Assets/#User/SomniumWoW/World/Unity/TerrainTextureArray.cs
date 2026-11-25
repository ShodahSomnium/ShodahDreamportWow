using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// The map-wide Texture2DArray of tileset textures (256x256 DXT5 = RGB + specular mask from the _s.blp, 9 mips, one slice per texture; see TerrainTexturePrep)
    /// bound globally as _WowTerrainLayers. Slots are reference-counted by the tiles that use them: worker threads Acquire a
    /// slot (and prepare the slice bytes), the main thread uploads (temporary Texture2D + Graphics.CopyTexture into the
    /// slice, so the array is never re-uploaded as a whole) and grows the array when it runs out. A slot whose count drops to
    /// zero keeps its texture until another texture needs the slot, so walking back and forth does not re-upload.
    /// </summary>
    internal sealed class TerrainTextureArray
    {
        public const int MaxSlots = 254; // vertex layer bytes use 255 as "no layer"
        public static readonly int GlobalId = Shader.PropertyToID("_WowTerrainLayers");

        readonly object _lock = new object();
        readonly Dictionary<string, int> _slotOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly string[] _nameOf = new string[MaxSlots];
        readonly int[] _refs = new int[MaxSlots];
        readonly bool[] _resident = new bool[MaxSlots];
        readonly byte[][] _cpuCopy = new byte[MaxSlots][]; // only in the fallback (no CopyTexture) mode
        readonly Queue<KeyValuePair<int, byte[]>> _pending = new Queue<KeyValuePair<int, byte[]>>();
        int _highWater;              // slots ever handed out
        Texture2DArray _array;       // main thread only
        int _capacity;
        bool _copyMode;
        public int Uploaded, Failed, WithSpecular; // WithSpecular: slices made from a _s.blp (worker threads, under _lock)
        public long PrepareMs;

        public TerrainTextureArray()
        {
            _copyMode = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
        }

        public int SlotsInUse { get { lock (_lock) { int n = 0; for (int i = 0; i < _highWater; i++) if (_refs[i] > 0) n++; return n; } } }
        public int Capacity { get { return _capacity; } }

        /// <summary>
        /// Any thread. The slot for a tileset texture (refcount + 1). When the texture still has to be prepared, the caller
        /// must call Provide(slot, bytes) (or Provide(slot, null) on failure). -1 when all slots are taken.
        /// </summary>
        public int Acquire(string name, out bool needsData)
        {
            needsData = false;
            lock (_lock)
            {
                int s;
                if (_slotOf.TryGetValue(name, out s)) { _refs[s]++; return s; }
                s = -1;
                if (_highWater < MaxSlots) s = _highWater++;
                else
                {
                    for (int i = 0; i < MaxSlots; i++) if (_refs[i] == 0) { s = i; break; }
                    if (s < 0) return -1;
                    if (_nameOf[s] != null) _slotOf.Remove(_nameOf[s]);
                }
                _nameOf[s] = name;
                _slotOf[name] = s;
                _refs[s] = 1;
                _resident[s] = false;
                needsData = true;
                return s;
            }
        }

        /// <summary>Any thread. Queues the slice bytes for upload (null = the texture failed; the slot shows grey).</summary>
        public void Provide(int slot, byte[] sliceBytes)
        {
            lock (_lock) { _pending.Enqueue(new KeyValuePair<int, byte[]>(slot, sliceBytes)); }
        }

        /// <summary>Main thread. One reference less (the texture stays until the slot is reused).</summary>
        public void Release(int slot)
        {
            if (slot < 0) return;
            lock (_lock) { if (_refs[slot] > 0) _refs[slot]--; }
        }

        /// <summary>True once the slot's texture has been uploaded (or failed and was filled grey).</summary>
        public bool IsResident(int slot)
        {
            if (slot < 0) return true;
            lock (_lock) { return _resident[slot]; }
        }

        /// <summary>Main thread: uploads pending slices within the time budget. Returns the number uploaded.</summary>
        public int Pump(Stopwatch frame, double budgetMs)
        {
            int n = 0;
            while (true)
            {
                KeyValuePair<int, byte[]> item;
                int need;
                lock (_lock)
                {
                    if (_pending.Count == 0) break;
                    item = _pending.Dequeue();
                    need = _highWater;
                }
                EnsureCapacity(need);
                byte[] bytes = item.Value ?? Grey();
                if (item.Value == null) Failed++;
                Upload(item.Key, bytes);
                lock (_lock) { _resident[item.Key] = true; }
                Uploaded++;
                n++;
                if (frame.Elapsed.TotalMilliseconds > budgetMs) break;
            }
            return n;
        }

        void EnsureCapacity(int need)
        {
            if (_array != null && need <= _capacity) return;
            int cap = Math.Max(32, _capacity);
            while (cap < need) cap *= 2;
            cap = Math.Min(MaxSlots, cap);
            var a = new Texture2DArray(TerrainTexturePrep.Size, TerrainTexturePrep.Size, cap, TextureFormat.DXT5, TerrainTexturePrep.MipCount, false);
            a.name = "WoW terrain layers (" + cap + ")";
            a.wrapMode = TextureWrapMode.Repeat;
            a.filterMode = FilterMode.Trilinear;
            a.anisoLevel = 8;
            if (_copyMode)
            {
                a.Apply(false, true); // allocate on the GPU; slices arrive by CopyTexture
                if (_array != null)
                    for (int s = 0; s < _capacity; s++)
                        if (_resident[s]) Graphics.CopyTexture(_array, s, a, s);
            }
            else
            {
                for (int s = 0; s < _capacity; s++)
                    if (_cpuCopy[s] != null) SetSlice(a, s, _cpuCopy[s]);
                a.Apply(false, false);
            }
            if (_array != null) UnityEngine.Object.Destroy(_array);
            WowLog.Info("Terrain: texture array " + (_array == null ? "created" : "grown") + " to " + cap + " slices (256x256 DXT5 RGB + specular mask, " + (_copyMode ? "CopyTexture" : "SetPixelData") + " uploads)");
            _array = a;
            _capacity = cap;
            Shader.SetGlobalTexture(GlobalId, _array);
        }

        void Upload(int slot, byte[] bytes)
        {
            if (_copyMode)
            {
                var t = new Texture2D(TerrainTexturePrep.Size, TerrainTexturePrep.Size, TextureFormat.DXT5, TerrainTexturePrep.MipCount, false);
                t.LoadRawTextureData(bytes);
                t.Apply(false, true);
                Graphics.CopyTexture(t, 0, _array, slot);
                UnityEngine.Object.Destroy(t);
            }
            else
            {
                _cpuCopy[slot] = bytes;
                SetSlice(_array, slot, bytes);
                _array.Apply(false, false);
            }
        }

        static void SetSlice(Texture2DArray a, int slot, byte[] bytes)
        {
            int o = 0;
            for (int m = 0; m < TerrainTexturePrep.MipCount; m++)
            {
                int n = TerrainTexturePrep.MipBytes(m);
                a.SetPixelData(bytes, m, slot, o);
                o += n;
            }
        }

        static byte[] _grey;
        static byte[] Grey()
        {
            if (_grey != null) return _grey;
            var b = new byte[TerrainTexturePrep.SliceBytes];
            for (int i = 0; i < b.Length; i += 16) { b[i + 8] = 0x10; b[i + 9] = 0x84; b[i + 10] = 0x10; b[i + 11] = 0x84; } // alpha (mask) 0, 565 grey, indices 0
            _grey = b;
            return b;
        }

        /// <summary>Worker thread: reads + prepares one tileset texture for a slot handed out by Acquire.</summary>
        public void Prepare(WowFiles files, string name, int slot)
        {
            var sw = Stopwatch.StartNew();
            byte[] slice = null;
            string how = null;
            try
            {
                // The client loads name_s.blp (same RGB + specular mask in alpha) for every tileset texture (TerrainTexturePrep);
                // without one: the plain texture, mask 0.
                var spec = files.Read(TerrainTexturePrep.SpecularPath(name));
                if (spec != null)
                {
                    slice = TerrainTexturePrep.Build(spec, true, out how);
                    if (slice != null) lock (_lock) { WithSpecular++; }
                }
                if (slice == null)
                {
                    string specHow = spec != null ? "_s.blp " + how + "; " : "";
                    var blp = files.Read(name);
                    if (blp == null) how = specHow + "not in the game files";
                    else { slice = TerrainTexturePrep.Build(blp, false, out how); how = specHow + how; }
                }
            }
            catch (Exception e) { how = e.Message; slice = null; }
            if (slice == null) WowLog.Warn("Terrain: tileset texture " + name + " -> grey: " + how);
            lock (_lock) { PrepareMs += sw.ElapsedMilliseconds; }
            Provide(slot, slice);
        }

        /// <summary>Main thread. Destroys the array (map unload).</summary>
        public void Dispose()
        {
            if (_array != null) UnityEngine.Object.Destroy(_array);
            _array = null;
            _capacity = 0;
        }
    }
}
