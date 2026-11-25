using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    /// <summary>Mix groups; each has its own WowSettings volume (and toggle).</summary>
    internal enum SoundCategory : byte { Effects, Ui, Footsteps, Voice, Music, Ambience }

    /// <summary>
    /// One background thread for disk and decoding work (MPQ reads, WAV decoding, ADT / WMO parsing, temp MP3 copies) with two
    /// queues (sounds first, then ground/zone data), and a queue of results the main thread runs within a time budget. No Unity
    /// API on the worker.
    /// </summary>
    internal sealed class AudioWorker
    {
        private readonly Queue<Action> _high = new Queue<Action>();
        private readonly Queue<Action> _low = new Queue<Action>();
        private readonly Queue<Action> _posted = new Queue<Action>();
        private readonly object _lock = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Stopwatch _pump = new Stopwatch();
        private Thread _thread;
        private volatile bool _stop;
        private int _errors;

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "SomniumWoW audio" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            _wake.Set();
            lock (_lock) { _high.Clear(); _low.Clear(); _posted.Clear(); }
        }

        /// <summary>Queues work for the worker (high = sounds, low = tiles/WMO groups/data).</summary>
        public void Enqueue(Action job, bool high)
        {
            if (job == null || _stop) return;
            lock (_lock) (high ? _high : _low).Enqueue(job);
            _wake.Set();
        }

        /// <summary>Queues an action for the main thread (from the worker).</summary>
        public void Post(Action a)
        {
            if (a == null || _stop) return;
            lock (_lock) _posted.Enqueue(a);
        }

        public int Pending { get { lock (_lock) return _high.Count + _low.Count; } }

        /// <summary>Main thread: runs posted results for up to budgetMs (at least one).</summary>
        public void Pump(float budgetMs)
        {
            _pump.Reset();
            _pump.Start();
            do
            {
                Action a = null;
                lock (_lock) if (_posted.Count > 0) a = _posted.Dequeue();
                if (a == null) break;
                try { a(); }
                catch (Exception e) { if (_errors++ < 10) WowLog.Error("Audio: main-thread step threw: " + e); }
            }
            while (_pump.Elapsed.TotalMilliseconds < budgetMs);
            _pump.Stop();
        }

        private void Run()
        {
            while (!_stop)
            {
                Action job = null;
                lock (_lock)
                {
                    if (_high.Count > 0) job = _high.Dequeue();
                    else if (_low.Count > 0) job = _low.Dequeue();
                }
                if (job == null) { _wake.WaitOne(250); continue; }
                try { job(); }
                catch (Exception e) { if (_errors++ < 10) WowLog.Warn("Audio: worker job threw: " + e.GetType().Name + ": " + e.Message); }
            }
        }
    }
}
