using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (owner Remote, PLAN7 s3.5 / s4.9): stage B of the remote rider - the ETS way of drawing a fast thing smoothly.
    // Pure: NO UnityEngine (the harness runs RideTimelineSim on it).

    /// <summary>One stamped pose of a remote rider's synced root (ints 21-27), positions in ONE frame (the writer's origin frame).</summary>
    public struct RideSample { public int Seq, StampMs, Epoch; public RideVec Position, Velocity; public float Yaw; }

    /// <summary>
    /// A port of ETS's EtsNetTimeline (ETS/EtsNetWire.cs 221-473) on RideVec: samples are placed on the WRITER's clock (the stamp,
    /// carried past int wrap-round), so network jitter does not become position jitter; the render clock (now minus a lag) is steered,
    /// never stepped: the lag aims at the least-delayed recent arrival + the recent spread + 1.1 x the send interval + 5 ms, clamped
    /// MinDelay..MaxDelay, and moves toward it at Slew = 0.05 s per second; positions follow a cubic Hermite through the samples'
    /// velocities; past the newest sample it is carried on along its velocity for at most MaxExtrapolation, then held; a move of more
    /// than 50 m between two samples, or a new epoch (a teleport, the writer's rebase), is snapped to, not slid to. Allocation-free.
    /// ETS's LeadIn option (its traffic cars) is not ported: a rider arrives with a mount-up, the view fades the correction in.
    /// One change from ETS (measured, harness RideTimelineSim, 26.2 m/s on a 60 m turn, 16 Hz, 30-120 ms jitter): ETS moves the lag
    /// by up to Slew x dt toward its target (bang-bang), so the drawn speed jumps by +-5 % whenever the target moves - drawn error
    /// 8.6 cm RMS / 23 cm max and a 45 mm kink in one frame. Steering the lag's RATE (error x LagGain, within +-Slew, changing at
    /// most LagAccel per second) gives 0.9-1.4 cm RMS / 7-14 cm max and at most 5 mm per frame; the lag still never steps.
    /// </summary>
    public sealed class RideTimeline
    {
        public float MinDelay = 0.03f, MaxDelay = 0.4f, MaxExtrapolation = 0.25f;

        /// <summary>The render lag over the best one: how far behind the newest data the rider is drawn, seconds.</summary>
        public float Delay { get; private set; }

        /// <summary>True for the one Sample() result that followed a snap (a new epoch or a 50 m jump).</summary>
        public bool WasSnap { get; private set; }

        /// <summary>Snaps since the timeline was made (new epochs after the first, and 50 m jumps).</summary>
        public int Snaps { get; private set; }

        /// <summary>Samples held.</summary>
        public int Count { get { return _count; } }

        private struct Entry { public double Time; public RideSample S; }

        private const int Capacity = 64, LagCapacity = 64;
        private const double LagWindow = 2.5;    // seconds an arrival counts toward the lag (ETS)
        private const double Slew = 0.05;        // how fast the render lag may change, s/s (ETS)
        private const double LagGain = 0.5;      // 1/s: the lag's rate = error x this (ours; a 2 s time constant filters the target's steps)
        private const double LagAccel = 0.1;     // s/s^2: how fast that rate may change (ours; 0 -> Slew in 0.5 s)
        private const float JumpMetres = 50f;    // a move between two samples this long is a jump, not riding (ETS)

        private readonly Entry[] _samples = new Entry[Capacity];
        private int _count;
        private int _lastSeq = int.MinValue;
        private int _lastEpoch = -1;
        private readonly double[] _arrivals = new double[LagCapacity];
        private readonly double[] _lags = new double[LagCapacity];
        private int _lagCount, _lagNext;
        private double _renderLag = double.NaN, _lastNow = double.NaN, _lagRate;
        private long _stampMs;       // the writer's clock unwrapped (ms)
        private int _lastStamp;
        private bool _haveStamp, _snapPending, _hadEpoch;

        public void Clear()
        {
            _count = 0;
            _lagCount = 0;
            _lagNext = 0;
            _renderLag = double.NaN;
            _lastNow = double.NaN;
            _lagRate = 0.0;
            _lastEpoch = -1;
            _lastSeq = int.MinValue;
            _haveStamp = false;
            _hadEpoch = false;
            _snapPending = false;
            Delay = 0f;
        }

        /// <summary>A sample read off the network at local time now (seconds). False when it was not new (same seq, or not later).</summary>
        public bool Add(in RideSample s, double now)
        {
            if (s.Seq == _lastSeq) return false;
            _lastSeq = s.Seq;
            if (s.Epoch != _lastEpoch)
            {
                if (_hadEpoch) Snaps++;
                _hadEpoch = true;
                _lastEpoch = s.Epoch;
                _count = 0;
                _lagCount = 0;
                _lagNext = 0;
                _renderLag = double.NaN;
                _haveStamp = false;      // a new epoch may bring a new writer clock (a new rider in the slot)
                _snapPending = true;
            }
            double stamp = Unwrap(s.StampMs);
            if (_count > 0 && stamp <= _samples[_count - 1].Time) return false;
            if (_count > 0)
            {
                RideVec d = s.Position - _samples[_count - 1].S.Position;
                if (d.X * d.X + d.Y * d.Y + d.Z * d.Z > JumpMetres * JumpMetres)
                {
                    _count = 0;
                    _snapPending = true;
                    Snaps++;
                }
            }
            _arrivals[_lagNext] = now;
            _lags[_lagNext] = now - stamp;
            _lagNext = (_lagNext + 1) % LagCapacity;
            _lagCount = Math.Min(_lagCount + 1, LagCapacity);
            if (_count == Capacity)
            {
                Array.Copy(_samples, 1, _samples, 0, Capacity - 1);
                _count--;
            }
            _samples[_count].Time = stamp;
            _samples[_count].S = s;
            _count++;
            return true;
        }

        /// <summary>The rider's synced root at local time now (same frame as the samples). False until there is anything to show.</summary>
        public bool Sample(double now, out RideVec position, out float yawDeg)
        {
            position = default(RideVec);
            yawDeg = 0f;
            if (_count == 0) return false;

            double best = double.MaxValue, worst = double.MinValue;
            for (int i = 0; i < _lagCount; i++)
            {
                if (now - _arrivals[i] > LagWindow) continue;
                if (_lags[i] < best) best = _lags[i];
                if (_lags[i] > worst) worst = _lags[i];
            }
            if (best == double.MaxValue) best = worst = _lags[(_lagNext + LagCapacity - 1) % LagCapacity];

            double interval = _count > 1 ? (_samples[_count - 1].Time - _samples[Math.Max(0, _count - 9)].Time) / Math.Min(8, _count - 1) : 0.05;
            double delay = Clamp(worst - best + interval * 1.1 + 0.005, MinDelay, MaxDelay);
            double target = best + delay;
            double step = double.IsNaN(_lastNow) ? 0.0 : Math.Max(0.0, now - _lastNow);
            _lastNow = now;
            if (double.IsNaN(_renderLag) || Math.Abs(target - _renderLag) > 0.5) { _renderLag = target; _lagRate = 0.0; }
            else
            {
                // ETS steps the lag by up to Slew x dt toward the target (bang-bang): the drawn speed then jumps between 95 %, 100 % and
                // 105 % - at 26 m/s a 2-4 cm kink in one frame (RideTimelineSim). Here the lag's RATE follows the error (time constant
                // 1 / LagGain) within +-Slew and changes by at most LagAccel per second, so the drawn speed changes smoothly too.
                double wantRate = Clamp((target - _renderLag) * LagGain, -Slew, Slew);
                double dr = LagAccel * step;
                _lagRate += Clamp(wantRate - _lagRate, -dr, dr);
                _renderLag += _lagRate * step;
            }
            Delay = (float)(_renderLag - best);
            double t = now - _renderLag;
            WasSnap = _snapPending;
            _snapPending = false;

            if (_count == 1 || t <= _samples[0].Time)
            {
                position = _samples[0].S.Position;
                yawDeg = _samples[0].S.Yaw;
                return true;
            }
            Entry newest = _samples[_count - 1];
            if (t >= newest.Time)
            {
                float ahead = (float)Math.Min(t - newest.Time, MaxExtrapolation);
                position = newest.S.Position + newest.S.Velocity * ahead;
                yawDeg = newest.S.Yaw;
                return true;
            }
            int b = 1;
            while (b < _count - 1 && _samples[b].Time < t) b++;
            Entry from = _samples[b - 1], to = _samples[b];
            float span = (float)(to.Time - from.Time);
            float u = span > 1e-4f ? (float)Clamp((t - from.Time) / span, 0.0, 1.0) : 1f;
            position = Hermite(from.S.Position, from.S.Velocity, to.S.Position, to.S.Velocity, span, u);
            yawDeg = LerpAngle(from.S.Yaw, to.S.Yaw, u);
            if (b > 2)
            {
                int drop = b - 2;
                Array.Copy(_samples, drop, _samples, 0, _count - drop);
                _count -= drop;
            }
            return true;
        }

        /// <summary>Cubic Hermite between two positions with their velocities over a span of seconds (ETS).</summary>
        public static RideVec Hermite(RideVec p0, RideVec v0, RideVec p1, RideVec v1, float span, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f, h10 = u3 - 2f * u2 + u, h01 = -2f * u3 + 3f * u2, h11 = u3 - u2;
            return p0 * h00 + v0 * (h10 * span) + p1 * h01 + v1 * (h11 * span);
        }

        private static float LerpAngle(float a, float b, float u)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            else if (d < -180f) d += 360f;
            float r = a + d * u;
            r %= 360f;
            return r < 0f ? r + 360f : r;
        }

        private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>The writer's ms clock (WowMount's frame time in ms; signed, wraps after 24.9 days) as a continuous count, in seconds.
        /// The wrapping difference to the last stamp is added, so the wrap from int.MaxValue to int.MinValue is one millisecond
        /// (ETS's version added 2^32 there; harmless in ETS's use, fixed here).</summary>
        private double Unwrap(int stampMs)
        {
            if (!_haveStamp)
            {
                _haveStamp = true;
                _stampMs = (uint)stampMs;
            }
            else _stampMs += unchecked(stampMs - _lastStamp);
            _lastStamp = stampMs;
            return _stampMs / 1000.0;
        }
    }
}
