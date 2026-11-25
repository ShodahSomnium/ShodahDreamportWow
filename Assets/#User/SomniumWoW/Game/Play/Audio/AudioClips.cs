using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>
    /// AudioClips from the game files, cached by path with a size cap (least recently used first, never a clip that is playing).
    /// WAV: read + decoded on the audio worker (WowWav), the clip is made on the main thread (AudioClip.Create + SetData, the way
    /// the Euro Truck Sim Dreamport makes its sounds) within a time budget. MP3: Unity cannot decode MP3 bytes from memory and
    /// OnAudioFilterRead does not work in world code, so the worker copies the file (leading zero padding stripped) into
    /// Application.temporaryCachePath/SomniumWoWAudio and the clip comes from UnityWebRequestMultimedia.GetAudioClip(file://,
    /// AudioType.MPEG) - the Age of Mythology Dreamport's proven path (AomMusic). Temp copies are deleted with their clip and the
    /// whole folder at start and at shutdown. A missing / unplayable file is logged once and remembered.
    /// </summary>
    internal sealed class AudioClips
    {
        private sealed class Entry
        {
            public string Path;
            public AudioClip Clip;
            public long Bytes;
            public long Used;
            public bool Loading, Failed;
            public string TempFile;
            public List<Action<AudioClip>> Waiters;
        }

        private sealed class Pending
        {
            public Entry E;
            public PcmSound Pcm;
        }

        public const long CapBytes = 64L * 1024 * 1024;

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<Pending> _create = new Queue<Pending>();
        private readonly AudioWorker _worker;
        private readonly MonoBehaviour _host;
        private readonly Func<AudioClip, bool> _inUse;
        private readonly Stopwatch _pump = new Stopwatch();
        private readonly string _tempDir;
        private WowFiles _files;
        private long _bytes, _clock;
        private int _loaded, _failed, _evicted, _missingLogged, _tempCounter;

        public AudioClips(AudioWorker worker, MonoBehaviour host, string tempDir, Func<AudioClip, bool> inUse)
        {
            _worker = worker;
            _host = host;
            _tempDir = tempDir;
            _inUse = inUse;
        }

        public WowFiles Files { get { return _files; } set { _files = value; } }
        public int Count { get { return _entries.Count; } }
        public long Bytes { get { return _bytes; } }
        public string StatsLine { get { return _entries.Count + " clips, " + (_bytes / (1024 * 1024)) + " MB (cap " + (CapBytes / (1024 * 1024)) + "), " + _loaded + " loaded, " + _failed + " failed, " + _evicted + " evicted"; } }

        /// <summary>The clip when it is already in memory.</summary>
        public bool TryGet(string path, out AudioClip clip)
        {
            clip = null;
            Entry e;
            if (string.IsNullOrEmpty(path) || !_entries.TryGetValue(path, out e) || e.Clip == null) return false;
            e.Used = ++_clock;
            clip = e.Clip;
            return true;
        }

        /// <summary>Calls back with the clip (maybe right away; null when the file is missing or unplayable).</summary>
        public void Get(string path, Action<AudioClip> done)
        {
            if (string.IsNullOrEmpty(path)) { if (done != null) done(null); return; }
            Entry e;
            if (_entries.TryGetValue(path, out e))
            {
                e.Used = ++_clock;
                if (e.Clip != null) { if (done != null) done(e.Clip); return; }
                if (e.Failed) { if (done != null) done(null); return; }
                if (e.Loading) { if (done != null) e.Waiters.Add(done); return; }
            }
            if (_files == null) { if (done != null) done(null); return; }
            e = new Entry { Path = path, Loading = true, Used = ++_clock, Waiters = new List<Action<AudioClip>>() };
            if (done != null) e.Waiters.Add(done);
            _entries[path] = e;
            var files = _files;
            if (WowWav.IsMp3Path(path)) _worker.Enqueue(() => CopyMp3(files, e, false), true);
            else _worker.Enqueue(() => DecodeWav(files, e), true);
        }

        /// <summary>Loads a clip in the background so the first play is instant.</summary>
        public void Prefetch(string path) { Get(path, null); }

        // ------------------------------------------------------------------------------------------ WAV

        private void DecodeWav(WowFiles files, Entry e)
        {
            byte[] b = null;
            try { b = files.Read(e.Path); } catch (Exception) { b = null; }
            string error;
            var pcm = WowWav.Decode(b, e.Path, out error);
            _worker.Post(() =>
            {
                if (pcm == null) { Fail(e, error); return; }
                _create.Enqueue(new Pending { E = e, Pcm = pcm });
            });
        }

        /// <summary>Main thread: turns decoded samples into clips (budgeted, at least one per call).</summary>
        public void Pump(float budgetMs)
        {
            if (_create.Count == 0) return;
            _pump.Reset();
            _pump.Start();
            do
            {
                var p = _create.Dequeue();
                if (!_entries.ContainsKey(p.E.Path) || _entries[p.E.Path] != p.E) continue;   // cleared meanwhile
                AudioClip clip = null;
                try
                {
                    clip = AudioClip.Create(System.IO.Path.GetFileNameWithoutExtension(p.E.Path), p.Pcm.Frames, p.Pcm.Channels, p.Pcm.Rate, false);
                    clip.SetData(p.Pcm.Samples, 0);
                }
                catch (Exception ex)
                {
                    if (clip != null) UnityEngine.Object.Destroy(clip);
                    Fail(p.E, "AudioClip.Create failed: " + ex.Message);
                    continue;
                }
                Done(p.E, clip, (long)p.Pcm.Samples.Length * 2);
            }
            while (_create.Count > 0 && _pump.Elapsed.TotalMilliseconds < budgetMs);
            _pump.Stop();
        }

        // ------------------------------------------------------------------------------------------ MP3

        /// <summary>Worker: MP3 bytes -> temp file (leading padding stripped) -> main thread loads it through UnityWebRequest.</summary>
        private void CopyMp3(WowFiles files, Entry e, bool stream)
        {
            string temp = WriteTemp(files, e.Path);
            _worker.Post(() =>
            {
                if (temp == null) { Fail(e, "missing or not MPEG"); return; }
                e.TempFile = temp;
                _host.StartCoroutine(LoadMp3(temp, stream, (clip, error) =>
                {
                    if (clip == null) { DeleteQuietly(temp); e.TempFile = null; Fail(e, error); return; }
                    long bytes = 0;
                    try { bytes = new FileInfo(temp).Length; } catch (Exception) { bytes = 1024 * 1024; }
                    Done(e, clip, stream ? bytes : (long)clip.samples * clip.channels * 2);
                }));
            });
        }

        private string WriteTemp(WowFiles files, string gamePath)
        {
            byte[] b = null;
            try { b = files.Read(gamePath); } catch (Exception) { b = null; }
            int start = WowWav.AudioStart(b);
            if (b == null || start < 0) return null;
            try
            {
                Directory.CreateDirectory(_tempDir);
                int n = System.Threading.Interlocked.Increment(ref _tempCounter);
                string leaf = gamePath.Replace('/', '\\');
                leaf = leaf.Substring(leaf.LastIndexOf('\\') + 1);
                string name = n + "_" + leaf;
                foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                string full = System.IO.Path.Combine(_tempDir, name);
                using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read))
                    fs.Write(b, start, b.Length - start);
                return full;
            }
            catch (Exception ex)
            {
                WowLog.Warn("Audio: temp copy of " + gamePath + " failed: " + ex.Message);
                return null;
            }
        }

        private static IEnumerator LoadMp3(string file, bool stream, Action<AudioClip, string> done)
        {
            string url;
            try { url = new Uri(file).AbsoluteUri; }
            catch (Exception) { url = "file:///" + file.Replace('\\', '/').Replace(" ", "%20"); }
            AudioClip clip = null;
            string error = null;
            UnityWebRequest request = null;
            try
            {
                request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = stream;
            }
            catch (Exception ex)
            {
                if (request != null) request.Dispose();
                done(null, ex.GetType().Name + ": " + ex.Message);
                yield break;
            }
            using (request)
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { clip = DownloadHandlerAudioClip.GetContent(request); }
                    catch (Exception ex) { error = ex.Message; }
                    if (clip == null && error == null) error = "no audio in the file";
                }
                else error = string.IsNullOrEmpty(request.error) ? request.result.ToString() : request.error;
            }
            if (clip != null) clip.name = System.IO.Path.GetFileNameWithoutExtension(file);
            done(clip, error);
        }

        /// <summary>
        /// A long MP3 (music) as a streaming clip that is NOT cached: the caller owns it and calls ReleaseStream when done.
        /// done(clip, tempFile) with clip null when it failed (logged once per path).
        /// </summary>
        public void LoadStream(string gamePath, Action<AudioClip, string> done)
        {
            var files = _files;
            if (files == null || string.IsNullOrEmpty(gamePath)) { done(null, null); return; }
            _worker.Enqueue(() =>
            {
                string temp = WriteTemp(files, gamePath);
                _worker.Post(() =>
                {
                    if (temp == null) { LogMissing(gamePath, "missing or not MPEG"); done(null, null); return; }
                    _host.StartCoroutine(LoadMp3(temp, true, (clip, error) =>
                    {
                        if (clip == null) { DeleteQuietly(temp); LogMissing(gamePath, error); done(null, null); return; }
                        done(clip, temp);
                    }));
                });
            }, true);
        }

        public static void ReleaseStream(AudioClip clip, string tempFile)
        {
            if (clip != null) UnityEngine.Object.Destroy(clip);
            if (tempFile != null) DeleteQuietly(tempFile);
        }

        // ------------------------------------------------------------------------------------------ bookkeeping

        private void Done(Entry e, AudioClip clip, long bytes)
        {
            e.Loading = false;
            if (!_entries.ContainsKey(e.Path) || _entries[e.Path] != e)
            {
                UnityEngine.Object.Destroy(clip);
                if (e.TempFile != null) DeleteQuietly(e.TempFile);
                return;
            }
            e.Clip = clip;
            e.Bytes = bytes;
            _bytes += bytes;
            _loaded++;
            var w = e.Waiters;
            e.Waiters = new List<Action<AudioClip>>();
            foreach (var cb in w)
            {
                try { cb(clip); }
                catch (Exception ex) { WowLog.Error("Audio: clip callback threw: " + ex); }
            }
            if (_bytes > CapBytes) Evict();
        }

        private void Fail(Entry e, string why)
        {
            e.Loading = false;
            e.Failed = true;
            _failed++;
            LogMissing(e.Path, why);
            var w = e.Waiters;
            e.Waiters = new List<Action<AudioClip>>();
            foreach (var cb in w)
            {
                try { cb(null); }
                catch (Exception ex) { WowLog.Error("Audio: clip callback threw: " + ex); }
            }
        }

        private void LogMissing(string path, string why)
        {
            if (_missingLogged < 20) WowLog.Warn("Audio: " + path + " not played (" + (why ?? "?") + ") - logged once per file");
            else if (_missingLogged == 20) WowLog.Warn("Audio: more sounds could not be played (not logged any more)");
            _missingLogged++;
        }

        private void Evict()
        {
            var list = new List<Entry>();
            foreach (var e in _entries.Values) if (e.Clip != null) list.Add(e);
            list.Sort((a, b) => a.Used.CompareTo(b.Used));
            foreach (var e in list)
            {
                if (_bytes <= CapBytes * 3 / 4) break;
                if (_inUse != null && _inUse(e.Clip)) continue;
                _bytes -= e.Bytes;
                UnityEngine.Object.Destroy(e.Clip);
                if (e.TempFile != null) DeleteQuietly(e.TempFile);
                _entries.Remove(e.Path);
                _evicted++;
            }
        }

        /// <summary>Destroys every clip and temp copy.</summary>
        public void Clear()
        {
            foreach (var e in _entries.Values)
            {
                if (e.Clip != null) UnityEngine.Object.Destroy(e.Clip);
                if (e.TempFile != null) DeleteQuietly(e.TempFile);
            }
            _entries.Clear();
            _create.Clear();
            _bytes = 0;
        }

        public static void DeleteQuietly(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception) { }
        }

        /// <summary>Removes the temp folder (start and shutdown).</summary>
        public static void CleanTempDir(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception e) { WowLog.Warn("Audio: temp folder " + dir + " not cleaned: " + e.Message); }
        }
    }
}
