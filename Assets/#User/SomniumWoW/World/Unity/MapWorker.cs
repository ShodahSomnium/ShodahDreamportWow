using System;
using System.Collections.Generic;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// One background thread for the map: jobs run nearest-first (lowest priority value first). Jobs must not touch Unity
    /// objects; they hand results back through the main-thread queue (Post / Drain).
    /// </summary>
    internal sealed class MapWorker
    {
        struct Job { public float Priority; public Action Run; public long Seq; }

        readonly object _lock = new object();
        readonly List<Job> _jobs = new List<Job>();
        readonly Queue<Action> _results = new Queue<Action>();
        readonly Thread _thread;
        volatile bool _stop;
        long _seq;
        public int Busy;

        public MapWorker(string name)
        {
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = name;
            _thread.Priority = ThreadPriority.BelowNormal;
            _thread.Start();
        }

        public int Pending { get { lock (_lock) return _jobs.Count; } }

        public void Enqueue(float priority, Action run)
        {
            lock (_lock)
            {
                _jobs.Add(new Job { Priority = priority, Run = run, Seq = _seq++ });
                Monitor.Pulse(_lock);
            }
        }

        /// <summary>Worker side: queue an action for the main thread.</summary>
        public void Post(Action mainThreadAction)
        {
            lock (_results) _results.Enqueue(mainThreadAction);
        }

        /// <summary>Main thread: takes one posted action (null when none).</summary>
        public Action TakeResult()
        {
            lock (_results) return _results.Count > 0 ? _results.Dequeue() : null;
        }

        public void Stop()
        {
            _stop = true;
            lock (_lock) { _jobs.Clear(); Monitor.PulseAll(_lock); }
            if (!_thread.Join(2000)) WowLog.Warn("Map: worker thread did not stop within 2 s (a file read is still running); it ends on its own");
        }

        void Loop()
        {
            while (!_stop)
            {
                Job job;
                lock (_lock)
                {
                    while (_jobs.Count == 0 && !_stop) Monitor.Wait(_lock);
                    if (_stop) return;
                    int best = 0;
                    for (int i = 1; i < _jobs.Count; i++)
                        if (_jobs[i].Priority < _jobs[best].Priority || (_jobs[i].Priority == _jobs[best].Priority && _jobs[i].Seq < _jobs[best].Seq)) best = i;
                    job = _jobs[best];
                    _jobs.RemoveAt(best);
                    Busy = 1;
                }
                try { job.Run(); }
                catch (Exception e) { WowLog.Error("Map: worker job failed: " + e); }
                finally { Busy = 0; }
            }
        }
    }
}
