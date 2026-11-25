using System;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// The radial menu's arithmetic, UnityEngine-free (plain floats) so it can be reasoned about and probed on its own:
    /// the twist of the hand around its forearm axis since the grip went down, and the segment that twist points at.
    /// </summary>
    public static class VrMath
    {
        /// <summary>Degrees of roll mapped across the whole arc (each side of neutral).</summary>
        public const float MaxRoll = 75f;

        /// <summary>No segment inside +-DeadZone degrees of the roll the grip started with (release there = cancel).</summary>
        public const float DeadZone = 6f;

        /// <summary>Hysteresis at a segment boundary: the highlight moves on only this many degrees past the edge.</summary>
        public const float Sticky = 2.5f;

        /// <summary>
        /// Twist in degrees (-180..180) about the local forward (+Z) axis of the relative rotation r = inverse(start) * now,
        /// given as the quaternion's components (swing-twist decomposition: twist = normalize(0, 0, z, w)). Positive = the hand
        /// rolled counter-clockwise as seen from behind it (Unity: Euler z +90 turns right into up).
        /// </summary>
        public static float TwistDegrees(float rz, float rw)
        {
            if (Math.Abs(rz) < 1e-7f && Math.Abs(rw) < 1e-7f) return 0f;
            double a = 2.0 * Math.Atan2(rz, rw) * (180.0 / Math.PI);
            while (a > 180.0) a -= 360.0;
            while (a < -180.0) a += 360.0;
            return (float)a;
        }

        /// <summary>
        /// The segment (0 = leftmost .. count-1 = rightmost) a roll points at, or -1 inside the dead zone. Rolling the top of the
        /// hand to the left (positive twist) walks left. Half the segments sit each side of neutral; an odd count puts its middle
        /// segment on the dead zone's right edge. current = the segment highlighted last frame (for hysteresis), -1 = none.
        /// </summary>
        public static int Segment(float twistDegrees, int count, int current)
        {
            if (count <= 0) return -1;
            float a = -twistDegrees;   // + = right
            float mag = Math.Abs(a);
            float dead = current < 0 ? DeadZone : DeadZone - Sticky;
            if (mag < dead) return -1;
            int left = count / 2, right = count - left;
            int side = a < 0f ? left : right;
            if (side <= 0) return -1;
            float span = (MaxRoll - DeadZone) / side;
            int k = Pick(mag - DeadZone, span, side);   // 0 = next to neutral
            int seg = a < 0f ? left - 1 - k : left + k;
            if (current >= 0 && current != seg)
            {
                // Stay on the current segment until the roll is Sticky degrees past its edge.
                float lo, hi;
                Range(current, count, out lo, out hi);
                if (a >= lo - Sticky && a <= hi + Sticky) return current;
            }
            return seg;
        }

        /// <summary>The roll range (degrees, + = right) of a segment, outside the dead zone.</summary>
        public static void Range(int segment, int count, out float lo, out float hi)
        {
            int left = count / 2, right = count - left;
            if (segment < left)
            {
                float span = (MaxRoll - DeadZone) / Math.Max(1, left);
                int k = left - 1 - segment;
                hi = -(DeadZone + k * span);
                lo = k == left - 1 ? -360f : hi - span;
            }
            else
            {
                float span = (MaxRoll - DeadZone) / Math.Max(1, right);
                int k = segment - left;
                lo = DeadZone + k * span;
                hi = k == right - 1 ? 360f : lo + span;
            }
        }

        private static int Pick(float past, float span, int side)
        {
            int k = (int)Math.Floor(past / Math.Max(0.001f, span));
            return k < 0 ? 0 : k >= side ? side - 1 : k;
        }

        /// <summary>The centre angle (degrees, 0 = right, 90 = up) of segment i of count laid over the top half ring, left to right.</summary>
        public static float CentreAngle(int i, int count)
        {
            float step = 180f / Math.Max(1, count);
            return 180f - (i + 0.5f) * step;
        }

        // ------------------------------------------------------------------ iteration 11 (vrcast, proposal-vrcast.md 1.2 / 6.5): radial ROWS

        /// <summary>Ours: metres of push (+) / pull (-) per row along the level eye-to-hand direction captured at the grip.</summary>
        public const float RowStep = 0.07f;

        /// <summary>Ours: hysteresis - a row changes only this far past the half-way line between two rows (a still hand never flickers).</summary>
        public const float RowHold = 0.012f;

        /// <summary>
        /// The row (minRow..maxRow) a push depth selects (m, + = away from the head); current = last frame's row. With RowStep 0.07 / RowHold
        /// 0.012: from row 0 a push past 0.047 m gives row 1 and back below 0.023 m row 0 again (measured by the design check VcDesignCheck).
        /// </summary>
        public static int Row(float depth, int current, int minRow, int maxRow)
        {
            float lo = (current - 0.5f) * RowStep - RowHold, hi = (current + 0.5f) * RowStep + RowHold;
            int r = depth >= lo && depth <= hi ? current : (int)Math.Round(depth / RowStep, MidpointRounding.AwayFromZero);
            return r < minRow ? minRow : r > maxRow ? maxRow : r;
        }
    }
}
