// iteration 11 - owner motion (proposal-motion.md 6.2.6): the transports' paths, pure (no Unity) - harness-tested.
// MO transports (boats, zeppelins, GO type 15): a 1:1 port of AzerothCore TransportMgr::GeneratePath (TransportMgr.cpp:118-351),
// MotionTransport::Update's frame search (Transport.cpp:216-307), CalculateSegmentPos (:581-610) and Movement::Spline catmull-rom
// (Spline.cpp:61-64 matrix, 99-112 evaluate / derivative, 159-176 3-step segment length, 240-270 InitCatmullRom; Spline.h:59
// STEPS_PER_SEGMENT 3), moved from the designer's reference port (P/harness/Probes/D11/motion/MotionTransportPort.cs TransportPathPort,
// proven by MotionTransportPoses 8 x PASS). evaluate_percent is NOT arc-length (Spline.h:98): kept as is - the server places
// passengers with the same math.
// Static transports (elevators, the Deeprun Tram, GO type 11): TransportAnimation.dbc / TransportRotation.dbc and
// StaticTransport::RelocateToProgress (Transport.cpp:1021-1066) with TransportAnimation::GetAnimNode / GetAnimRotation
// (TransportMgr.cpp:509-559).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Motion
{
    /// <summary>The path of one MO transport (a TaxiPath of TaxiPathNode rows, its gameobject_template moveSpeed / accelRate).</summary>
    public sealed class TransportPath
    {
        /// <summary>Movement::Spline&lt;double&gt; in catmull-rom mode for one same-map stretch.</summary>
        public sealed class Spl
        {
            public double[] X, Y, Z; public double[] Len; public int Lo, Hi;

            /// <summary>Spline::init_spline(controls, count, ModeCatmullrom, orientation 0): virtual first = c0 - (cos o, sin o, 0), last duplicated.</summary>
            public Spl(List<double[]> controls, double initialOrientation)
            {
                int count = controls.Count;
                X = new double[count + 2]; Y = new double[count + 2]; Z = new double[count + 2];
                for (int i = 0; i < count; i++) { X[i + 1] = controls[i][0]; Y[i + 1] = controls[i][1]; Z[i + 1] = controls[i][2]; }
                X[0] = controls[0][0] - Math.Cos(initialOrientation); Y[0] = controls[0][1] - Math.Sin(initialOrientation); Z[0] = controls[0][2];
                X[count + 1] = controls[count - 1][0]; Y[count + 1] = controls[count - 1][1]; Z[count + 1] = controls[count - 1][2];
                Lo = 1; Hi = count;
                Len = new double[Hi + 1];
                for (int i = Lo; i < Hi; i++) Len[i + 1] = Len[i] + SegLen(i);
            }

            public void Eval(int idx, double t, out double x, out double y, out double z)
            {
                double t2 = t * t, t3 = t2 * t;
                double w0 = -0.5 * t3 + t2 - 0.5 * t, w1 = 1.5 * t3 - 2.5 * t2 + 1.0, w2 = -1.5 * t3 + 2.0 * t2 + 0.5 * t, w3 = 0.5 * t3 - 0.5 * t2;
                int a = idx - 1;
                x = X[a] * w0 + X[a + 1] * w1 + X[a + 2] * w2 + X[a + 3] * w3;
                y = Y[a] * w0 + Y[a + 1] * w1 + Y[a + 2] * w2 + Y[a + 3] * w3;
                z = Z[a] * w0 + Z[a + 1] * w1 + Z[a + 2] * w2 + Z[a + 3] * w3;
            }

            public void Deriv(int idx, double t, out double x, out double y)
            {
                double t2 = t * t;
                double d0 = -1.5 * t2 + 2.0 * t - 0.5, d1 = 4.5 * t2 - 5.0 * t, d2 = -4.5 * t2 + 4.0 * t + 0.5, d3 = 1.5 * t2 - t;
                int a = idx - 1;
                x = X[a] * d0 + X[a + 1] * d1 + X[a + 2] * d2 + X[a + 3] * d3;
                y = Y[a] * d0 + Y[a + 1] * d1 + Y[a + 2] * d2 + Y[a + 3] * d3;
            }

            double SegLen(int idx)
            {
                double cx = X[idx], cy = Y[idx], cz = Z[idx], len = 0;
                for (int i = 1; i <= 3; i++)
                {
                    double nx, ny, nz; Eval(idx, i / 3.0, out nx, out ny, out nz);
                    len += Math.Sqrt((nx - cx) * (nx - cx) + (ny - cy) * (ny - cy) + (nz - cz) * (nz - cz));
                    cx = nx; cy = ny; cz = nz;
                }
                return len;
            }

            public double Length(int first, int last) { return Len[last] - Len[first]; }
        }

        /// <summary>KeyFrame (TransportMgr.h).</summary>
        public sealed class Key
        {
            public TaxiPathPoint Node; public bool Teleport; public int Index; public Spl Spline; public double InitialOrientation;
            public double DistSinceStop, DistUntilStop, DistFromPrev, NextDistFromPrev, TimeFrom, TimeTo;
            public uint Arrive, Departure, NextArrive;
            public bool Stop { get { return Node.Delay > 0; } }        // KeyFrame::IsStopFrame: node->delay > 0
        }

        public readonly List<Key> Keys = new List<Key>();
        public double Speed, Accel, AccelTime, AccelDist;
        public int Splines;
        /// <summary>The path's period in ms (TransportTemplate::pathTime = the last keyframe's DepartureTime).</summary>
        public uint PeriodMs { get { return Keys[Keys.Count - 1].Departure; } }

        /// <summary>TransportMgr::GeneratePath. Null when the path is too short to build (AC needs at least 4 nodes after trimming).</summary>
        public static TransportPath Build(IReadOnlyList<TaxiPathPoint> path, float speed, float accel)
        {
            if (path == null || path.Count < 4 || speed <= 0f || accel <= 0f) return null;
            var r = new TransportPath { Speed = speed, Accel = accel };
            var keys = r.Keys;
            var splinePath = new List<double[]>();
            // TransportMgr.cpp 123-136: allPoints + lerp(-0.2) before, lerp(-0.2) and lerp(-1) after; SplineRawInitializer uses them as is
            // (points = allPoints, lo = 1, hi = size - 2): the derivative at node i is evaluate_derivative(i + 1, 0).
            var raw = new List<double[]>();
            foreach (var n in path) raw.Add(new double[] { n.X, n.Y, n.Z });
            raw.Insert(0, Lerp(raw[0], raw[1], -0.2));
            raw.Add(Lerp(raw[raw.Count - 1], raw[raw.Count - 2], -0.2));
            raw.Add(Lerp(raw[raw.Count - 1], raw[raw.Count - 2], -1.0));
            bool mapChange = false;
            for (int i = 0; i < path.Count; i++)
            {
                if (!mapChange)
                {
                    var n = path[i];
                    if (i != path.Count - 1 && ((n.Flags & 1) != 0 || n.MapId != path[i + 1].MapId))
                    {
                        if (keys.Count == 0) return null;                    // AC would index keys.back() of an empty list
                        keys[keys.Count - 1].Teleport = true; mapChange = true;
                    }
                    else
                    {
                        double hx, hy; RawDeriv(raw, i + 1, 0.0, out hx, out hy);
                        keys.Add(new Key { Node = n, InitialOrientation = Norm(Math.Atan2(hy, hx) + Math.PI) });
                        splinePath.Add(new double[] { n.X, n.Y, n.Z });
                    }
                }
                else mapChange = false;
            }
            if (splinePath.Count >= 2) { splinePath.RemoveAt(0); keys.RemoveAt(0); splinePath.RemoveAt(splinePath.Count - 1); keys.RemoveAt(keys.Count - 1); }
            if (keys.Count < 2) return null;
            keys[keys.Count - 1].Teleport = true;
            r.AccelTime = speed / accel;
            r.AccelDist = 0.5 * speed * speed / accel;
            double accelDist = r.AccelDist;
            int firstStop = -1, lastStop = -1;
            keys[0].DistFromPrev = 0; keys[0].Index = 1;
            if (keys[0].Stop) { firstStop = 0; lastStop = 0; }
            int start = 0;
            for (int i = 1; i < keys.Count; i++)
            {
                if (keys[i - 1].Teleport || i + 1 == keys.Count)
                {
                    int extra = !keys[i - 1].Teleport ? 1 : 0;
                    var spl = new Spl(splinePath.GetRange(start, i - start + extra), 0.0);
                    r.Splines++;
                    for (int j = start; j < i + extra; j++)
                    {
                        keys[j].Index = j - start + 1;
                        keys[j].DistFromPrev = spl.Length(j - start, j + 1 - start);
                        if (j > 0) keys[j - 1].NextDistFromPrev = keys[j].DistFromPrev;
                        keys[j].Spline = spl;
                    }
                    if (keys[i - 1].Teleport) { keys[i].Index = i - start + 1; keys[i].DistFromPrev = 0; keys[i - 1].NextDistFromPrev = 0; keys[i].Spline = spl; }
                    start = i;
                }
                if (keys[i].Stop) { if (firstStop == -1) firstStop = i; lastStop = i; }
            }
            keys[keys.Count - 1].NextDistFromPrev = keys[0].DistFromPrev;
            if (firstStop == -1 || lastStop == -1) firstStop = lastStop = 0;
            double tmp = 0;
            for (int i = 0; i < keys.Count; i++) { int j = (i + lastStop) % keys.Count; if (keys[j].Stop || j == lastStop) tmp = 0; else tmp += keys[j].DistFromPrev; keys[j].DistSinceStop = tmp; }
            tmp = 0;
            for (int i = keys.Count - 1; i >= 0; i--) { int j = (i + firstStop) % keys.Count; tmp += keys[(j + 1) % keys.Count].DistFromPrev; keys[j].DistUntilStop = tmp; if (keys[j].Stop || j == firstStop) tmp = 0; }
            foreach (var k in keys)
            {
                double total = k.DistSinceStop + k.DistUntilStop;
                if (total < 2 * accelDist)
                {
                    if (k.DistSinceStop < k.DistUntilStop) { double seg = 2.0 * Math.Sqrt((k.DistUntilStop + k.DistSinceStop) / accel); k.TimeTo = seg - Math.Sqrt(2 * k.DistSinceStop / accel); }
                    else k.TimeTo = Math.Sqrt(2 * k.DistUntilStop / accel);
                }
                else if (k.DistSinceStop < accelDist) { double seg = (k.DistUntilStop + k.DistSinceStop) / speed + speed / accel; k.TimeTo = seg - Math.Sqrt(2 * k.DistSinceStop / accel); }
                else if (k.DistUntilStop < accelDist) k.TimeTo = Math.Sqrt(2 * k.DistUntilStop / accel);
                else k.TimeTo = k.DistUntilStop / speed + 0.5 * speed / accel;
            }
            double segTime = 0;
            for (int i = 0; i < keys.Count; i++) { int j = (i + lastStop) % keys.Count; if (keys[j].Stop || j == lastStop) segTime = keys[j].TimeTo; keys[j].TimeFrom = segTime - keys[j].TimeTo; }
            keys[0].Arrive = 0;
            double cur = 0;
            if (keys[0].Stop) { cur = keys[0].Node.Delay; keys[0].Departure = (uint)(cur * 1000); }
            for (int i = 1; i < keys.Count; i++)
            {
                cur += keys[i - 1].TimeTo;
                if (keys[i].Stop) { keys[i].Arrive = (uint)(cur * 1000); keys[i - 1].NextArrive = keys[i].Arrive; cur += keys[i].Node.Delay; keys[i].Departure = (uint)(cur * 1000); }
                else { cur -= keys[i].TimeTo; keys[i].Arrive = (uint)(cur * 1000); keys[i - 1].NextArrive = keys[i].Arrive; keys[i].Departure = keys[i].Arrive; }
            }
            keys[keys.Count - 1].NextArrive = keys[keys.Count - 1].Departure;
            if (r.PeriodMs == 0) return null;
            return r;
        }

        /// <summary>Transport.cpp 581-610 (now in seconds).</summary>
        double SegmentPos(Key f, double now)
        {
            double tss = f.TimeFrom + (now - f.Departure / 1000.0), tus = f.TimeTo - (now - f.Departure / 1000.0), dist, seg;
            if (tss < tus) { dist = tss < AccelTime ? 0.5 * Accel * tss * tss : AccelDist + (tss - AccelTime) * Speed; seg = dist - f.DistSinceStop; }
            else { dist = tus < AccelTime ? 0.5 * Accel * tus * tus : AccelDist + (tus - AccelTime) * Speed; seg = f.DistUntilStop - dist; }
            return seg / f.NextDistFromPrev;
        }

        /// <summary>The pose at timer ms (0 .. Period): Transport.cpp 244-307 done without iterator state.</summary>
        public void PoseAt(double timer, out double x, out double y, out double z, out double o, out int map, out bool stopped)
        {
            int i = 0;
            while (i + 1 < Keys.Count && Keys[i + 1].Arrive <= timer) i++;
            var f = Keys[i];
            map = f.Node.MapId;
            stopped = false;
            if (timer < f.Departure || f.NextDistFromPrev <= 0)
            {
                // waiting at a stop; or a teleport frame (the transport jumps to the next frame's node when it gets there: Transport.cpp 291-292)
                var n = f.NextDistFromPrev <= 0 && timer >= f.Departure && i + 1 < Keys.Count ? Keys[i + 1] : f;
                x = n.Node.X; y = n.Node.Y; z = n.Node.Z; o = n.InitialOrientation; map = n.Node.MapId; stopped = timer < f.Departure;
                return;
            }
            double u = SegmentPos(f, timer / 1000.0);
            f.Spline.Eval(f.Index, u, out x, out y, out z);
            double dx, dy;
            f.Spline.Deriv(f.Index, u, out dx, out dy);
            o = Norm(Math.Atan2(dy, dx) + Math.PI);
        }

        /// <summary>The pose at timer ms (wrapped into the period), WoW coordinates; mapId = the keyframe's map.</summary>
        public WowPos PoseAt(double timerMs, out int mapId)
        {
            double period = PeriodMs;
            double t = timerMs % period;
            if (t < 0) t += period;
            double x, y, z, o; bool stopped;
            PoseAt(t, out x, out y, out z, out o, out mapId, out stopped);
            return new WowPos((float)x, (float)y, (float)z, (float)o);
        }

        /// <summary>The first keyframe on map after a teleport frame from another map (where a ship arrives after the loading screen),
        /// false when the path never changes to that map.</summary>
        public bool TryArrivalOnMap(int map, out WowPos pose)
        {
            for (int i = 1; i < Keys.Count; i++)
            {
                if (Keys[i].Node.MapId == map && Keys[i - 1].Node.MapId != map)
                {
                    var n = Keys[i];
                    pose = new WowPos(n.Node.X, n.Node.Y, n.Node.Z, (float)n.InitialOrientation);
                    return true;
                }
            }
            pose = default(WowPos);
            return false;
        }

        /// <summary>Stops, maps and splines for the log.</summary>
        public string Summary
        {
            get
            {
                int stops = 0;
                var maps = new List<int>();
                foreach (var k in Keys) { if (k.Stop) stops++; if (!maps.Contains(k.Node.MapId)) maps.Add(k.Node.MapId); }
                return Keys.Count + " keyframes, " + Splines + " spline(s), " + stops + " stop(s), maps [" + string.Join(",", maps) + "]";
            }
        }

        static double[] Lerp(double[] a, double[] b, double t) { return new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t }; }
        static double Norm(double o) { o %= 2 * Math.PI; if (o < 0) o += 2 * Math.PI; return o; }

        static void RawDeriv(List<double[]> pts, int idx, double t, out double x, out double y)
        {
            double t2 = t * t;
            double d0 = -1.5 * t2 + 2.0 * t - 0.5, d1 = 4.5 * t2 - 5.0 * t, d2 = -4.5 * t2 + 4.0 * t + 0.5, d3 = 1.5 * t2 - t;
            int a = idx - 1;
            x = pts[a][0] * d0 + pts[a + 1][0] * d1 + pts[a + 2][0] * d2 + pts[a + 3][0] * d3;
            y = pts[a][1] * d0 + pts[a + 1][1] * d1 + pts[a + 2][1] * d2 + pts[a + 3][1] * d3;
        }
    }

    /// <summary>
    /// Static transports (GO type 11: elevators, the Deeprun Tram cars): TransportAnimation.dbc (DBCfmt.h:128 "diifffx": ID,
    /// TransportID = the GO entry, TimeIndex ms, X, Y, Z, SequenceID) and TransportRotation.dbc ("diiffff": ID, GameObjectsID,
    /// TimeIndex, X, Y, Z, W) read from the player's own client at runtime, keyed by entry and sorted by time.
    /// </summary>
    public sealed class StaticTransportAnim
    {
        struct Node { public uint Time; public float X, Y, Z, W; }

        readonly Dictionary<int, List<Node>> _path = new Dictionary<int, List<Node>>();
        readonly Dictionary<int, List<Node>> _rot = new Dictionary<int, List<Node>>();
        readonly Dictionary<int, uint> _total = new Dictionary<int, uint>();
        static StaticTransportAnim _cached;
        static WowFiles _cachedFiles;

        public int Entries { get { return _path.Count; } }
        public int Rows { get; private set; }
        public int RotationRows { get; private set; }

        /// <summary>The tables of this install (cached per WowFiles); never null (empty when the files are missing).</summary>
        public static StaticTransportAnim Get(WowFiles files)
        {
            if (_cached != null && _cachedFiles == files) return _cached;
            var a = new StaticTransportAnim();
            try { a.Load(files); }
            catch (Exception e) { WowLog.Warn("Transports: reading TransportAnimation / TransportRotation failed: " + e.Message); }
            _cached = a;
            _cachedFiles = files;
            return a;
        }

        void Load(WowFiles files)
        {
            var t = files != null ? Dbc.Load(files, "TransportAnimation") : null;
            if (t != null && t.FieldCount >= 6)
            {
                for (int r = 0; r < t.RecordCount; r++)
                {
                    int entry = t.GetInt(r, 1);
                    List<Node> l;
                    if (!_path.TryGetValue(entry, out l)) _path[entry] = l = new List<Node>();
                    // TransportMgr::AddPathNodeToTransport: Path[timeSeg] = node (a later row with the same time replaces it)
                    var n = new Node { Time = t.GetUInt(r, 2), X = t.GetFloat(r, 3), Y = t.GetFloat(r, 4), Z = t.GetFloat(r, 5) };
                    int at = l.FindIndex(q => q.Time == n.Time);
                    if (at >= 0) l[at] = n; else l.Add(n);
                    uint tot; _total.TryGetValue(entry, out tot);
                    if (n.Time > tot) _total[entry] = n.Time;
                    Rows++;
                }
                foreach (var l in _path.Values) l.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            var rt = files != null ? Dbc.Load(files, "TransportRotation") : null;
            if (rt != null && rt.FieldCount >= 7)
            {
                for (int r = 0; r < rt.RecordCount; r++)
                {
                    int entry = rt.GetInt(r, 1);
                    List<Node> l;
                    if (!_rot.TryGetValue(entry, out l)) _rot[entry] = l = new List<Node>();
                    var n = new Node { Time = rt.GetUInt(r, 2), X = rt.GetFloat(r, 3), Y = rt.GetFloat(r, 4), Z = rt.GetFloat(r, 5), W = rt.GetFloat(r, 6) };
                    int at = l.FindIndex(q => q.Time == n.Time);
                    if (at >= 0) l[at] = n; else l.Add(n);
                    RotationRows++;
                }
                foreach (var l in _rot.Values) l.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
        }

        public bool Has(int entry) { List<Node> l; return _path.TryGetValue(entry, out l) && l.Count >= 2; }

        /// <summary>TransportAnimation::TotalTime (the largest TimeIndex) = StaticTransport::GetPeriod, 0 = unknown.</summary>
        public uint Period(int entry) { uint t; return _total.TryGetValue(entry, out t) ? t : 0u; }

        /// <summary>StaticTransport::RelocateToProgress (Transport.cpp:1021-1066): spawn + R(pathRot) * lerp(anim[curr], anim[next]),
        /// orientation = spawn O + the interpolated TransportRotation angle. parentRotZ / W = GAMEOBJECT_PARENTROTATION + 2 / + 3.
        /// False when the entry has no animation.</summary>
        public bool PoseAt(int entry, uint progress, WowPos spawn, float parentRotZ, float parentRotW, out WowPos pose)
        {
            pose = spawn;
            List<Node> path;
            if (!_path.TryGetValue(entry, out path) || path.Count < 2) return false;
            // GetAnimNode (TransportMgr.cpp:509-524): the last node with time >= its TimeSeg, and the one after it (AC asserts there is one)
            int ci = -1;
            for (int i = path.Count - 1; i >= 0; i--) if (progress >= path[i].Time) { ci = i; break; }
            if (ci < 0) return false;
            if (ci == path.Count - 1) ci = path.Count - 2;            // progress == TotalTime: the end of the last segment (AC asserts)
            Node curr = path[ci], next = path[ci + 1];
            float percPos = next.Time > curr.Time ? (float)(progress - (double)curr.Time) / (next.Time - curr.Time) : 0f;
            if (percPos > 1f) percPos = 1f;
            double px = curr.X + percPos * (next.X - curr.X), py = curr.Y + percPos * (next.Y - curr.Y), pz = curr.Z + percPos * (next.Z - curr.Z);
            float sign = parentRotZ >= 0f ? 1f : -1f;
            double pathRotAngle = sign * 2.0 * Math.Acos(Clamp1(parentRotW));
            double cs = Math.Cos(pathRotAngle), sn = Math.Sin(pathRotAngle);
            double nx = px * cs - py * sn, ny = px * sn + py * cs;
            double oriRot = 0;
            List<Node> rot;
            if (_rot.TryGetValue(entry, out rot) && rot.Count > 0)
            {
                // GetAnimRotation (TransportMgr.cpp:527-559)
                for (int i = rot.Count - 1; i >= 0; i--)
                {
                    if (progress < rot[i].Time) continue;
                    Node c = rot[i], n;
                    uint nextSeg;
                    if (i != rot.Count - 1) { n = rot[i + 1]; nextSeg = n.Time; }
                    else { n = rot[0]; nextSeg = Period(entry); }
                    float percRot = nextSeg > c.Time ? (float)(progress - (double)c.Time) / (nextSeg - c.Time) : 0f;
                    double aC = (c.Z >= 0f ? 1 : -1) * 2.0 * Math.Acos(Clamp1(c.W));
                    double aN = (n.Z >= 0f ? 1 : -1) * 2.0 * Math.Acos(Clamp1(n.W));
                    oriRot = aC + percRot * (aN - aC);
                    break;
                }
            }
            pose = new WowPos((float)(nx + spawn.X), (float)(ny + spawn.Y), (float)(pz + spawn.Z), ForcedMoveMath.Normalize((float)(spawn.O + oriRot)));
            return true;
        }

        /// <summary>StaticTransport::Update (Transport.cpp:968-1019) for our clock: the progress after dt ms from progress, with the
        /// server's pause rule (pauseAtTime > 0: GOState READY travels back to 0, ACTIVE to pauseAtTime, then waits).</summary>
        public static uint Advance(uint progress, uint dt, uint period, uint pauseTime, int goState)
        {
            if (period == 0) return progress;
            if (pauseTime != 0)
            {
                if (goState == 1)                                   // GO_STATE_READY
                {
                    if (progress == 0) return 0;
                    if (progress < pauseTime) return 0;
                    if (progress + dt < period) return progress + dt;
                    return 0;
                }
                if (progress == pauseTime) return progress;
                if (progress > pauseTime) return pauseTime;
                if (progress + dt < pauseTime) return progress + dt;
                return pauseTime;
            }
            return (uint)(((ulong)progress + dt) % period);
        }

        static double Clamp1(float w) { return w > 1f ? 1.0 : w < -1f ? -1.0 : w; }
    }
}
