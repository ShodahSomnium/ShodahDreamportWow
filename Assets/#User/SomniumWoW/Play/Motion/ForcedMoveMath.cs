// iteration 11 - owner motion (proposal-motion.md 6.2.5): pure evaluation of a ForcedMoveView (no Unity) - harness-tested.
using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Motion
{
    /// <summary>
    /// Evaluates the forced movements the server (or an offline spell) puts on the player: server paths (charge, pulls, jumps, fear /
    /// confuse segments, the taxi spline), the ballistic flight of a knockback, the time-preserving comfort ease of an online taxi ride,
    /// and the transport passenger transforms. Every formula names its AzerothCore source; values of our own say "ours".
    /// </summary>
    public static class ForcedMoveMath
    {
        /// <summary>MovementUtil.cpp:24 gravity (yd/s^2).</summary>
        public const double Gravity = 19.29110527038574;
        /// <summary>MovementUtil.cpp:28-29 terminalVelocity / terminalSafefallVelocity (yd/s).</summary>
        public const float TerminalVelocity = 60.148003f, SafeFallVelocity = 7.0f;

        /// <summary>Movement::computeFallElevation (MovementUtil.cpp:60-85): yards fallen after t seconds; startVelocity + = downward.</summary>
        public static double FallElevation(double t, bool safeFall, double startVelocity)
        {
            double termVel = safeFall ? SafeFallVelocity : TerminalVelocity;
            if (startVelocity > termVel) startVelocity = termVel;
            double terminalTime = (safeFall ? SafeFallVelocity / Gravity : TerminalVelocity / Gravity) - startVelocity / Gravity;
            if (t > terminalTime)
                return termVel * (t - terminalTime) + startVelocity * terminalTime + Gravity * terminalTime * terminalTime * 0.5;
            return t * (startVelocity + t * Gravity * 0.5);
        }

        /// <summary>The vertical speed (yd/s, + = up) of the fall above at t (the derivative, capped at the terminal speed).</summary>
        public static double FallVerticalSpeed(double t, bool safeFall, double startVelocity)
        {
            double termVel = safeFall ? SafeFallVelocity : TerminalVelocity;
            if (startVelocity > termVel) startVelocity = termVel;
            double down = startVelocity + Gravity * t;
            if (down > termVel) down = termVel;
            return -down;
        }

        /// <summary>Total length of the move's points (chord lengths, AC initLengths for linear paths).</summary>
        public static double PathLength(ForcedMoveView m)
        {
            if (m == null || m.Points == null) return 0;
            double len = 0;
            int n = m.Points.Length / 3;
            for (int i = 1; i < n; i++)
            {
                double dx = m.Points[i * 3] - m.Points[i * 3 - 3], dy = m.Points[i * 3 + 1] - m.Points[i * 3 - 2], dz = m.Points[i * 3 + 2] - m.Points[i * 3 - 1];
                len += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            return len;
        }

        /// <summary>Path / Fleeing / Confused / Taxi: true once t seconds since the start reach the duration.</summary>
        public static bool PathDone(ForcedMoveView m, double t)
        {
            return m == null || t * 1000.0 >= m.DurationMs;
        }

        /// <summary>
        /// Path / Fleeing / Confused / Taxi: position (WoW) and travel orientation at t seconds since the start (clamped to the end).
        /// Linear: constant speed along straight segments (time per segment proportional to its length, AC initLengths). Smooth:
        /// uniform Catmull-Rom with the same chord-length timing, the start's virtual point = 2*p0 - p1 and the last point duplicated
        /// (exactly SplinePath.Evaluate, OnlineMovement.cs). Parabolic: + (td - tp) * 0.5 * accel * tp after ParabolicStartMs
        /// (MoveSpline::computeParabolicElevation, MoveSpline.cpp:39-40, 77). The returned O is the travel direction (or EndFacing at
        /// the end when the server fixed one).
        /// </summary>
        public static WowPos PathAt(ForcedMoveView m, double t, out float travelO)
        {
            travelO = 0f;
            if (m == null || m.Points == null || m.Points.Length < 3) return default(WowPos);
            float[] p = m.Points;
            int n = p.Length / 3;
            if (n == 1) return new WowPos(p[0], p[1], p[2], float.IsNaN(m.EndFacing) ? 0f : m.EndFacing);
            var cum = new double[n];
            for (int i = 1; i < n; i++)
            {
                double dx = p[i * 3] - p[i * 3 - 3], dy = p[i * 3 + 1] - p[i * 3 - 2], dz = p[i * 3 + 2] - p[i * 3 - 1];
                cum[i] = cum[i - 1] + Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            double total = cum[n - 1];
            double ms = t * 1000.0;
            if (ms < 0) ms = 0;
            if (ms > m.DurationMs) ms = m.DurationMs;
            float x, y, z;
            if (total <= 1e-4 || m.DurationMs <= 0)
            {
                int last = n - 1;
                x = p[last * 3]; y = p[last * 3 + 1]; z = p[last * 3 + 2];
                travelO = (float)Math.Atan2(p[last * 3 + 1] - p[last * 3 - 2], p[last * 3] - p[last * 3 - 3]);
            }
            else
            {
                double d = ms / m.DurationMs * total;
                int seg = 0;
                while (seg < n - 2 && cum[seg + 1] < d) seg++;
                double segLen = cum[seg + 1] - cum[seg];
                double u = segLen > 1e-5 ? (d - cum[seg]) / segLen : 1.0;
                if (u < 0) u = 0; else if (u > 1) u = 1;
                int i0 = Math.Max(seg - 1, 0), i1 = seg, i2 = seg + 1, i3 = Math.Min(seg + 2, n - 1);
                if (m.Smooth)
                {
                    x = (float)CatmullRomAt(p, 0, i0, i1, i2, i3, seg, n, u);
                    y = (float)CatmullRomAt(p, 1, i0, i1, i2, i3, seg, n, u);
                    z = (float)CatmullRomAt(p, 2, i0, i1, i2, i3, seg, n, u);
                }
                else
                {
                    x = (float)(p[i1 * 3] + (p[i2 * 3] - p[i1 * 3]) * u);
                    y = (float)(p[i1 * 3 + 1] + (p[i2 * 3 + 1] - p[i1 * 3 + 1]) * u);
                    z = (float)(p[i1 * 3 + 2] + (p[i2 * 3 + 2] - p[i1 * 3 + 2]) * u);
                }
                travelO = (float)Math.Atan2(p[i2 * 3 + 1] - p[i1 * 3 + 1], p[i2 * 3] - p[i1 * 3]);
            }
            if (m.ParabolicAccel != 0f && ms > m.ParabolicStartMs)
            {
                double tp = (ms - m.ParabolicStartMs) / 1000.0, td = (m.DurationMs - m.ParabolicStartMs) / 1000.0;
                z += (float)((td - tp) * 0.5 * m.ParabolicAccel * tp);
            }
            float o = Normalize(travelO);
            if (ms >= m.DurationMs && !float.IsNaN(m.EndFacing)) o = Normalize(m.EndFacing);
            travelO = Normalize(travelO);
            return new WowPos(x, y, z, o);
        }

        static double CatmullRomAt(float[] p, int k, int i0, int i1, int i2, int i3, int seg, int n, double t)
        {
            double p0 = p[i0 * 3 + k], p1 = p[i1 * 3 + k], p2 = p[i2 * 3 + k], p3 = p[i3 * 3 + k];
            if (seg == 0) p0 = 2.0 * p1 - p2;                 // virtual point before the start (SplinePath)
            if (seg + 2 > n - 1) p3 = p2;                    // AC duplicates the last point
            double t2 = t * t, t3 = t2 * t;
            return 0.5 * (2.0 * p1 + (-p0 + p2) * t + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
        }

        /// <summary>Knockback: the ballistic position at t (no collision): planar Start + dir * SpeedXY * t; z = Start.Z - fall(t) with
        /// fall = computeFallElevation(t, SlowFall, -SpeedUp) (MovementUtil.cpp:60-85). O = the launch direction.</summary>
        public static WowPos KnockbackAt(ForcedMoveView m, double t)
        {
            if (m == null) return default(WowPos);
            if (t < 0) t = 0;
            double x = m.Start.X + m.DirCos * m.SpeedXY * t;
            double y = m.Start.Y + m.DirSin * m.SpeedXY * t;
            double z = m.Start.Z - FallElevation(t, m.SlowFall, -m.SpeedUp);
            return new WowPos((float)x, (float)y, (float)z, Normalize((float)Math.Atan2(m.DirSin, m.DirCos)));
        }

        /// <summary>Knockback: the vertical speed at t (yd/s, + = up).</summary>
        public static double KnockbackVerticalSpeed(ForcedMoveView m, double t)
        {
            return m == null ? 0 : FallVerticalSpeed(Math.Max(0, t), m.SlowFall, -m.SpeedUp);
        }

        /// <summary>Taxi ride timing that keeps the server's duration with a comfort ease (decision D-M2, ours): accelerate over
        /// e = min(1.5 s, T/4), cruise at v = L / (T - e), brake over the last e. Returns the distance along the path at t.</summary>
        public static double EasedDistance(double length, double totalSeconds, double t)
        {
            if (length <= 0) return 0;
            if (totalSeconds <= 0 || t >= totalSeconds) return length;
            if (t <= 0) return 0;
            double e = Math.Min(1.5, totalSeconds / 4.0);
            double v = length / (totalSeconds - e);
            if (t < e) return 0.5 * v * t * t / e;
            if (t <= totalSeconds - e) return v * (t - e / 2);
            double r = totalSeconds - t;
            return length - 0.5 * v * r * r / e;
        }

        /// <summary>TransportBase::CalculatePassengerPosition (VehicleDefines.h:142-152): local offsets on a transport at pose t -> world.</summary>
        public static WowPos PassengerPosition(float lx, float ly, float lz, float lo, WowPos t)
        {
            double c = Math.Cos(t.O), s = Math.Sin(t.O);
            return new WowPos((float)(t.X + lx * c - ly * s), (float)(t.Y + ly * c + lx * s), t.Z + lz, Normalize(t.O + lo));
        }

        /// <summary>TransportBase::CalculatePassengerOffset (VehicleDefines.h:154-165), ported exactly incl. its tan form: world -> local
        /// offsets on a transport at pose t.</summary>
        public static WowPos PassengerOffset(float x, float y, float z, float o, WowPos t)
        {
            float lo = Normalize(o - t.O);
            double lz = z - t.Z, iny = y - t.Y, inx = x - t.X;
            double tan = Math.Tan(t.O), cos = Math.Cos(t.O), sin = Math.Sin(t.O);
            double den = cos + sin * tan;
            double ly = (iny - inx * tan) / den;
            double lx = (inx + iny * tan) / den;
            return new WowPos((float)lx, (float)ly, (float)lz, lo);
        }

        /// <summary>Position::NormalizeOrientation: [0, 2pi).</summary>
        public static float Normalize(float o)
        {
            const float TwoPi = (float)(2 * Math.PI);
            if (float.IsNaN(o) || float.IsInfinity(o)) return 0f;
            o %= TwoPi;
            if (o < 0f) o += TwoPi;
            return o;
        }

        /// <summary>Signed difference a - b in -pi..pi.</summary>
        public static float AngleDiff(float a, float b)
        {
            double d = a - b;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            return (float)d;
        }
    }
}
