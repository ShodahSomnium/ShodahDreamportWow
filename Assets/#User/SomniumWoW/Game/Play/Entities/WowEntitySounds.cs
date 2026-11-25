using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>One CreatureSoundData slot (e.g. the wound sound): its SoundEntries variations as clips, volume and distances.</summary>
    internal sealed class WowSoundSet
    {
        public string Name;
        public float Volume = 1f;
        public float MinDistance = 8f, MaxDistance = 45f;   // yards (SoundEntries MinDistance / DistanceCutoff)
        public readonly List<AudioClip> Clips = new List<AudioClip>();
        private int _next;

        public AudioClip Pick()
        {
            if (Clips.Count == 0) return null;
            var c = Clips[_next % Clips.Count];
            _next = (_next + 1 + UnityEngine.Random.Range(0, Clips.Count)) % Clips.Count;
            return c;
        }
    }

    /// <summary>The sounds of one creature display: attack (exertion), wound, critical wound, death, aggro.</summary>
    internal sealed class WowCreatureSounds
    {
        public WowSoundSet Attack, Wound, WoundCrit, Death, Aggro;
    }

    /// <summary>
    /// Creature sounds from the player's files: CreatureDisplayInfo.SoundID (else CreatureModelData.SoundID) -> CreatureSoundData
    /// (WoWDBDefs layout 3.1.0.9767-3.3.5.12340, 38 fields, measured 1306 rows x 38 fields: 1 SoundExertionID, 3 SoundInjuryID,
    /// 4 SoundInjuryCriticalID, 6 SoundDeathID, 10 SoundAggroID) -> SoundEntries (DirectoryBase + File[10], VolumeFloat,
    /// MinDistance, DistanceCutoff) -> WAV. Every Shadowglen creature sound measured is PCM 16-bit mono 22050 Hz (RIFF fmt tag 1,
    /// 33 of 33 files), decoded here on a worker thread; other encodings are skipped with a warning. The main thread turns the
    /// samples into AudioClips (AudioClip.Create + SetData) within a small budget per frame.
    /// </summary>
    internal sealed class WowEntitySounds
    {
        private const int FieldExertion = 1, FieldInjury = 3, FieldInjuryCritical = 4, FieldDeath = 6, FieldAggro = 10;
        private const int CreatureSoundDataFields = 38;
        private const int MaxVariations = 3;

        private sealed class Decoded
        {
            public string Path;
            public float[] Samples;
            public int Rate, Channels;
        }

        private sealed class DecodedSet
        {
            public int EntryId;
            public string Name;
            public float Volume, Min, Max;
            public readonly List<Decoded> Files = new List<Decoded>();
            public WowSoundSet Built;
        }

        private sealed class DecodedCreature
        {
            public int DisplayId;
            public DecodedSet Attack, Wound, WoundCrit, Death, Aggro;
        }

        private readonly WowFiles _files;
        private readonly object _lock = new object();
        private readonly Queue<DecodedCreature> _ready = new Queue<DecodedCreature>();
        private readonly Dictionary<int, WowCreatureSounds> _byDisplay = new Dictionary<int, WowCreatureSounds>();
        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private readonly Stopwatch _pump = new Stopwatch();
        private Thread _thread;
        private volatile bool _cancel, _done;
        private int _sets, _wavs, _skipped;
        private long _workerMs;
        private bool _reported;

        public WowEntitySounds(WowFiles files)
        {
            _files = files;
        }

        // Iteration 6 (online: displays keep arriving): the worker lives for the session and takes displays queued by Start / Add.
        private readonly Queue<int> _wanted = new Queue<int>();
        private readonly HashSet<int> _asked = new HashSet<int>();

        public void Start(int[] displayIds)
        {
            if (_files == null) { _done = true; return; }
            if (displayIds != null) foreach (int d in displayIds) Add(d);
            if (_thread != null) return;
            _thread = new Thread(Work);
            _thread.IsBackground = true;
            _thread.Name = "SomniumWoW creature sounds";
            _thread.Start();
        }

        /// <summary>Queues one more display for decoding (once per display). Main thread.</summary>
        public void Add(int displayId)
        {
            if (displayId <= 0 || _files == null || !_asked.Add(displayId)) return;
            lock (_lock) { _wanted.Enqueue(displayId); Monitor.Pulse(_lock); }
        }

        public void Dispose()
        {
            _cancel = true;
            lock (_lock) Monitor.PulseAll(_lock);
            foreach (var c in _clips) if (c != null) UnityEngine.Object.Destroy(c);
            _clips.Clear();
            _byDisplay.Clear();
        }

        /// <summary>The sounds of a display (null while not decoded yet or when it has none).</summary>
        public WowCreatureSounds For(int displayId)
        {
            WowCreatureSounds s;
            return _byDisplay.TryGetValue(displayId, out s) ? s : null;
        }

        // ------------------------------------------------------------------ worker

        private void Work()
        {
            var entries = new Dictionary<int, DecodedSet>();
            var batch = new List<int>();
            bool first = true;
            while (!_cancel)
            {
                batch.Clear();
                lock (_lock)
                {
                    if (_wanted.Count == 0)
                    {
                        if (first) { first = false; _done = true; }
                        Monitor.Wait(_lock, 500);
                        continue;
                    }
                    while (_wanted.Count > 0) batch.Add(_wanted.Dequeue());
                }
                if (!Decode(batch, entries)) { _done = true; return; }
            }
        }

        /// <summary>Decodes the sound sets of these displays (false: the sound DBCs are unusable - stop).</summary>
        private bool Decode(List<int> displays, Dictionary<int, DecodedSet> entries)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var cdi = Dbc.Load(_files, "CreatureDisplayInfo");
                var cmd = Dbc.Load(_files, "CreatureModelData");
                var csd = Dbc.Load(_files, "CreatureSoundData");
                var se = Dbc.Load(_files, "SoundEntries");
                if (cdi == null || cmd == null || csd == null || se == null) { WowLog.Warn("Entities: creature sounds off (a sound DBC is missing)"); return false; }
                if (csd.FieldCount != CreatureSoundDataFields) { WowLog.Warn("Entities: CreatureSoundData.dbc has " + csd.FieldCount + " fields, expected 38 (not a 3.3.5a client?) - creature sounds off"); return false; }
                foreach (int display in displays)
                {
                    if (_cancel) return false;
                    int r = cdi.FindRow(display);
                    if (r < 0) continue;
                    int soundId = cdi.GetInt(r, DbcFields.CreatureDisplayInfo.SoundID);
                    if (soundId <= 0)
                    {
                        int mr = cmd.FindRow(cdi.GetInt(r, DbcFields.CreatureDisplayInfo.ModelID));
                        if (mr >= 0) soundId = cmd.GetInt(mr, DbcFields.CreatureModelData.SoundID);
                    }
                    int sr = soundId > 0 ? csd.FindRow(soundId) : -1;
                    if (sr < 0) continue;
                    var dc = new DecodedCreature { DisplayId = display };
                    dc.Attack = Entry(se, csd.GetInt(sr, FieldExertion), entries);
                    dc.Wound = Entry(se, csd.GetInt(sr, FieldInjury), entries);
                    dc.WoundCrit = Entry(se, csd.GetInt(sr, FieldInjuryCritical), entries);
                    dc.Death = Entry(se, csd.GetInt(sr, FieldDeath), entries);
                    dc.Aggro = Entry(se, csd.GetInt(sr, FieldAggro), entries);
                    lock (_lock) _ready.Enqueue(dc);
                }
            }
            catch (Exception e)
            {
                WowLog.Warn("Entities: creature sound worker failed: " + e.Message);
                return false;
            }
            finally
            {
                _workerMs += sw.ElapsedMilliseconds;
            }
            return true;
        }

        private DecodedSet Entry(DbcTable se, int entryId, Dictionary<int, DecodedSet> cache)
        {
            if (entryId <= 0) return null;
            DecodedSet set;
            if (cache.TryGetValue(entryId, out set)) return set;
            set = null;
            int r = se.FindRow(entryId);
            if (r >= 0)
            {
                set = new DecodedSet
                {
                    EntryId = entryId,
                    Name = se.GetString(r, DbcFields.SoundEntries.Name),
                    Volume = se.GetFloat(r, DbcFields.SoundEntries.VolumeFloat),
                    Min = se.GetFloat(r, DbcFields.SoundEntries.MinDistance),
                    Max = se.GetFloat(r, DbcFields.SoundEntries.DistanceCutoff),
                };
                string dir = se.GetString(r, DbcFields.SoundEntries.DirectoryBase) ?? "";
                for (int k = 0; k < 10 && set.Files.Count < MaxVariations; k++)
                {
                    string f = se.GetString(r, DbcFields.SoundEntries.File + k);
                    if (string.IsNullOrEmpty(f)) continue;
                    string path = dir.Length > 0 ? dir + "\\" + f : f;
                    var d = DecodeWav(path);
                    if (d != null) { set.Files.Add(d); _wavs++; }
                }
                if (set.Files.Count == 0) set = null;
                else _sets++;
            }
            cache[entryId] = set;
            return set;
        }

        /// <summary>RIFF/WAVE PCM 16-bit (fmt tag 1) -> float samples, interleaved. Null (counted) for anything else.</summary>
        private Decoded DecodeWav(string path)
        {
            byte[] b = null;
            try { b = _files.Read(path); } catch (Exception) { b = null; }
            if (b == null || b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E')
            {
                _skipped++;
                return null;
            }
            int p = 12, tag = 0, channels = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
            while (p + 8 <= b.Length)
            {
                int len = BitConverter.ToInt32(b, p + 4);
                if (len < 0) break;
                if (b[p] == 'f' && b[p + 1] == 'm' && b[p + 2] == 't' && len >= 16)
                {
                    tag = BitConverter.ToUInt16(b, p + 8);
                    channels = BitConverter.ToUInt16(b, p + 10);
                    rate = BitConverter.ToInt32(b, p + 12);
                    bits = BitConverter.ToUInt16(b, p + 22);
                }
                else if (b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a')
                {
                    dataOff = p + 8;
                    dataLen = Math.Min(len, b.Length - dataOff);
                    break;
                }
                p += 8 + len + (len & 1);
            }
            if (tag != 1 || bits != 16 || channels < 1 || channels > 2 || rate < 4000 || dataOff < 0 || dataLen < 4)
            {
                if (_skipped++ < 5) WowLog.Warn("Entities: sound " + path + " skipped (fmt tag " + tag + ", " + bits + " bit, " + channels + " ch; only PCM 16-bit is decoded)");
                return null;
            }
            int n = dataLen / 2;
            n -= n % channels;
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (short)(b[dataOff + i * 2] | (b[dataOff + i * 2 + 1] << 8)) / 32768f;
            return new Decoded { Path = path, Samples = s, Rate = rate, Channels = channels };
        }

        // ------------------------------------------------------------------ main thread

        public void Pump(float budgetMs)
        {
            _pump.Reset();
            _pump.Start();
            while (_pump.Elapsed.TotalMilliseconds < budgetMs)
            {
                DecodedCreature dc = null;
                lock (_lock) if (_ready.Count > 0) dc = _ready.Dequeue();
                if (dc == null) break;
                var cs = new WowCreatureSounds
                {
                    Attack = Build(dc.Attack), Wound = Build(dc.Wound), WoundCrit = Build(dc.WoundCrit), Death = Build(dc.Death), Aggro = Build(dc.Aggro)
                };
                _byDisplay[dc.DisplayId] = cs;
            }
            _pump.Stop();
            if (!_reported && _done)
            {
                bool empty;
                lock (_lock) empty = _ready.Count == 0;
                if (!empty) return;
                _reported = true;
                WowLog.Info("Entities: creature sounds for " + _byDisplay.Count + " displays: " + _sets + " sound entries, " + _wavs + " WAV files decoded ("
                    + _skipped + " skipped), " + _clips.Count + " clips, worker " + _workerMs + " ms");
            }
        }

        private WowSoundSet Build(DecodedSet d)
        {
            if (d == null) return null;
            if (d.Built != null) return d.Built;
            var set = new WowSoundSet
            {
                Name = d.Name,
                Volume = d.Volume > 0f ? Mathf.Clamp01(d.Volume) : 1f,
                MinDistance = d.Min > 0f ? d.Min : 8f,
                MaxDistance = d.Max > 0f ? d.Max : 45f,
            };
            foreach (var f in d.Files)
            {
                try
                {
                    var clip = AudioClip.Create(System.IO.Path.GetFileNameWithoutExtension(f.Path), f.Samples.Length / f.Channels, f.Channels, f.Rate, false);
                    clip.SetData(f.Samples, 0);
                    set.Clips.Add(clip);
                    _clips.Add(clip);
                }
                catch (Exception e)
                {
                    WowLog.Warn("Entities: AudioClip for " + f.Path + " failed: " + e.Message);
                }
                f.Samples = null;   // the clip holds the data now
            }
            d.Built = set;
            return set;
        }
    }
}
