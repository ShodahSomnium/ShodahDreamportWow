using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Details
{
    /// <summary>
    /// One background thread for the details (tables, ADT tiles, clutter placement, model parsing) and a queue of results the
    /// main thread runs within a time budget (the Audio owner's AudioWorker pattern). No Unity API on the worker.
    /// </summary>
    internal sealed class DetailsWorker
    {
        private readonly Queue<Action> _jobs = new Queue<Action>();
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
            _thread = new Thread(Run) { IsBackground = true, Name = "SomniumWoW details" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            _wake.Set();
            lock (_lock) { _jobs.Clear(); _posted.Clear(); }
        }

        public void Enqueue(Action job)
        {
            if (job == null || _stop) return;
            lock (_lock) _jobs.Enqueue(job);
            _wake.Set();
        }

        /// <summary>From the worker: an action for the main thread.</summary>
        public void Post(Action a)
        {
            if (a == null || _stop) return;
            lock (_lock) _posted.Enqueue(a);
        }

        public int Pending { get { lock (_lock) return _jobs.Count; } }

        /// <summary>Drops queued jobs and results (a map change / reset); the running job finishes and posts into the void.</summary>
        public void Clear()
        {
            lock (_lock) { _jobs.Clear(); _posted.Clear(); }
        }

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
                catch (Exception e) { if (_errors++ < 10) WowLog.Error("Details: main-thread step threw: " + e); }
            }
            while (_pump.Elapsed.TotalMilliseconds < budgetMs);
            _pump.Stop();
        }

        private void Run()
        {
            while (!_stop)
            {
                Action job = null;
                lock (_lock) if (_jobs.Count > 0) job = _jobs.Dequeue();
                if (job == null) { _wake.WaitOne(250); continue; }
                try { job(); }
                catch (Exception e) { if (_errors++ < 10) WowLog.Error("Details: worker job threw: " + e); }
            }
        }
    }
}
