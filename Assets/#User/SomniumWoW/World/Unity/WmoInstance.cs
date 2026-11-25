using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// On the root of every WMO built by WmoFactory. Spawns the WMO's doodads (MODD of set 0 + the requested set) with
    /// M2Factory.BuildStatic over several frames: all instances together spend at most WmoFactory.DoodadBudgetMs per frame.
    /// Doodads are placed in WMO-local space: position WowSpace.PointToUnity, rotation WowSpace.RotationToUnity of the MODD
    /// quaternion (x, y, z, w), uniform scale. The MODD colour is not applied (it would need a material per doodad; models
    /// keep their shared materials).
    /// </summary>
    public sealed class WmoInstance : MonoBehaviour
    {
        struct Spawn
        {
            public string Model;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale;
            public Transform Parent;   // iteration 11 (lod11): null = the doodad root; else a portal-only group's doodad container
        }

        public string Path;
        public int DoodadSet;
        public int DoodadsQueued, DoodadsBuilt, DoodadsFailed;

        /// <summary>True when every queued doodad was built (or failed).</summary>
        public bool DoodadsDone { get { return _next >= _queue.Count; } }

        readonly List<Spawn> _queue = new List<Spawn>();
        int _next;
        WowFiles _files;
        Transform _doodadRoot;
        bool _logWhenDone;
        long _spentTicks;
        int _frames;

        static int _budgetFrame = -1;
        static long _budgetTicksUsed;
        static int _totalBuilt, _totalFailed, _failLines;
        static readonly HashSet<string> FailedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Queues set 0 + doodadSet. Returns the number queued.</summary>
        public int QueueDoodads(WowFiles files, WmoRoot root, int doodadSet, bool logWhenDone) { return QueueDoodads(files, root, doodadSet, logWhenDone, null); }

        /// <summary>Iteration 11 (lod11): parents (may be null) puts a MODD index under another transform (same local frame as the
        /// doodad root) - WmoFactory's container for the doodads only a portal-only group references.</summary>
        public int QueueDoodads(WowFiles files, WmoRoot root, int doodadSet, bool logWhenDone, Dictionary<int, Transform> parents)
        {
            _files = files;
            _logWhenDone = logWhenDone;
            foreach (int i in root.DoodadsForSet(doodadSet))
            {
                var d = root.DoodadDefs[i];
                if (string.IsNullOrEmpty(d.Name)) continue;
                var q = WowSpace.RotationToUnity(d.QX, d.QY, d.QZ, d.QW);
                float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                q = len > 1e-6f ? new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len) : Quaternion.identity;
                Transform parent = null;
                if (parents != null) parents.TryGetValue(i, out parent);
                _queue.Add(new Spawn { Model = M2Path(d.Name), Position = WowSpace.PointToUnity(d.X, d.Y, d.Z), Rotation = q, Scale = d.Scale > 0f ? d.Scale : 1f, Parent = parent });
            }
            DoodadsQueued = _queue.Count;
            if (_queue.Count > 0)
            {
                var go = new GameObject("Doodads (set " + doodadSet + ")");
                go.transform.SetParent(transform, false);
                _doodadRoot = go.transform;
            }
            else enabled = false;
            return _queue.Count;
        }

        /// <summary>MODN names end in .MDX/.MDL in 3.3.5a WMOs (census: 239,027 .mdx, 11,273 .mdl); the files are .m2.</summary>
        public static string M2Path(string name)
        {
            if (name.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 4) + ".m2";
            return name;
        }

        void Update()
        {
            if (_next >= _queue.Count) { enabled = false; return; }
            if (Time.frameCount != _budgetFrame) { _budgetFrame = Time.frameCount; _budgetTicksUsed = 0; }
            long budget = (long)(WmoFactory.DoodadBudgetMs * Stopwatch.Frequency / 1000.0);
            if (_budgetTicksUsed >= budget) return;
            _frames++;
            var start = Stopwatch.GetTimestamp();
            while (_next < _queue.Count)
            {
                BuildOne(_queue[_next++]);
                if (_budgetTicksUsed + (Stopwatch.GetTimestamp() - start) >= budget) break;
            }
            long spent = Stopwatch.GetTimestamp() - start;
            _budgetTicksUsed += spent;
            _spentTicks += spent;
            if (_next >= _queue.Count)
            {
                enabled = false;
                if (_logWhenDone)
                    WowLog.Info("WMO " + Path + ": doodads set 0" + (DoodadSet > 0 ? "+" + DoodadSet : "") + " " + DoodadsBuilt + " of " + DoodadsQueued
                        + " built" + (DoodadsFailed > 0 ? ", " + DoodadsFailed + " failed" : "") + ", " + (_spentTicks * 1000 / Stopwatch.Frequency)
                        + " ms over " + _frames + " frames");
            }
        }

        void BuildOne(Spawn s)
        {
            GameObject m = null;
            if (!FailedModels.Contains(s.Model))
            {
                try
                {
                    m = M2Factory.BuildStatic(_files, s.Model);
                    if (m == null) FailedModels.Add(s.Model);   // missing/unreadable: M2Factory logged it; do not retry per doodad
                }
                catch (Exception ex)
                {
                    FailedModels.Add(s.Model);
                    if (_failLines++ < 20) WowLog.Warn("WMO doodad " + s.Model + ": M2Factory.BuildStatic threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            if (m == null) { DoodadsFailed++; _totalFailed++; return; }
            var t = m.transform;
            t.SetParent(s.Parent != null ? s.Parent : _doodadRoot, false);
            t.localPosition = s.Position;
            t.localRotation = s.Rotation;
            t.localScale = new Vector3(s.Scale, s.Scale, s.Scale);
            DoodadsBuilt++;
            _totalBuilt++;
        }

        internal static void ResetStats()
        {
            _totalBuilt = _totalFailed = _failLines = 0;
            FailedModels.Clear();
        }

        internal static string StatsText()
        {
            return "doodads " + _totalBuilt + " built, " + _totalFailed + " failed (" + FailedModels.Count + " models failed)";
        }
    }
}
