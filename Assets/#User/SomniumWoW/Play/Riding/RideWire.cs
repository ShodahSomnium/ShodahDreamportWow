using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (owner Remote, PLAN7 s3.5 / s4.7): what a Somnium rider tells the other Somnium players about their mount, packed
    // into ints 17..27 of the player's Gear record (Networking/WowNetWire.cs, 64 ints, no floats). Pure: NO UnityEngine (the harness
    // runs RideWireCompat on it). Every field keeps "0 = not said", so an older build's record (ints 17+ all 0) reads as Kind 0 and
    // the viewer falls back to iteration 6's drawing; an older build reading ours sees ints 0-16 exactly as before.

    /// <summary>The rider's mount as the wire carries it (PLAN7 s3.5, signatures fixed).</summary>
    public struct RideRecord
    {
        public byte Kind;              // 0 not said, 1 ground mount, 2 flying mount, 3 taxi
        public bool Airborne, Swimming;
        public int GaitAnim;           // AnimationData id 0..1023
        public float GaitRate;         // 0..5.11 (steps of 0.01)
        public int MountUpCounter;     // 0..15
        public int Epoch;              // 0..31: +1 on teleports and on the writer's rebases (the viewer snaps instead of sliding)
        public bool RelativeSaid; public float RelYawDeg; public RideVec RelPos;   // mount root in the rider's synced-root yaw frame, metres
        public bool PoseSaid; public int PoseStampMs; public int Seq;              // stage B
        public RideVec Pos, Vel; public float Yaw;                                  // stage B: the synced root in the WRITER's origin frame
    }

    /// <summary>
    /// Ints 17..27 of a player slot (PLAN7 s4.7):
    ///   17  kind (0-1: 0 not said, 1 ground, 2 flying mount, 3 taxi) | airborne (2) | swimming (3) | gait AnimationData id (4-13)
    ///       | rate x100 (14-22, 0..511) | mount-up counter (23-26) | epoch (27-31)
    ///   18  relative yaw (0-15, 1/65536 turn) | relative pose said (16) | stage-B pose said (17)
    ///   19  relative x mm (0-15, int16) | relative z mm (16-31, int16)    - the mount root in the synced root's yaw frame (+-32 m)
    ///   20  relative y mm (0-15, int16) | spare
    ///   21  stage B: pose stamp = the writer's clock in ms at the LateUpdate that placed the root (NOT int 2: that is the record write)
    ///   22-24 stage B: the synced root position in the WRITER's origin frame, mm (int32 each)
    ///   25  stage B: vx cm/s (0-15, int16) | vz cm/s (16-31, int16)
    ///   26  stage B: vy cm/s (0-15, int16) | yaw (16-31, 1/65536 turn)
    ///   27  stage B: sample seq (0-15) | spare
    /// Kind 0 packs all eleven ints as 0; a record whose int 17 says Kind 0 unpacks as "not said" (default) whatever else it holds.
    /// </summary>
    public static class RideWire
    {
        public const int At = 17, Count = 11;            // record ints 17..27 (layout s4.7)

        public const int FlagRelativeSaid = 1 << 16;     // int 18
        public const int FlagPoseSaid = 1 << 17;         // int 18

        /// <summary>Writes ints At..At+Count-1 of ints (the rest is not touched). True when an int changed.</summary>
        public static bool Pack(in RideRecord r, int[] ints)
        {
            if (ints == null || ints.Length < At + Count) return false;
            int k17 = 0, k18 = 0, k19 = 0, k20 = 0, k21 = 0, k22 = 0, k23 = 0, k24 = 0, k25 = 0, k26 = 0, k27 = 0;
            if (r.Kind != 0)
            {
                k17 = (r.Kind & 0x3) | (r.Airborne ? 1 << 2 : 0) | (r.Swimming ? 1 << 3 : 0) | ((Clamp(r.GaitAnim, 0, 1023) & 0x3FF) << 4)
                    | ((RateBits(r.GaitRate) & 0x1FF) << 14) | ((r.MountUpCounter & 0xF) << 23) | ((r.Epoch & 0x1F) << 27);
                if (r.RelativeSaid)
                {
                    k18 |= FlagRelativeSaid | (Turn16(r.RelYawDeg) & 0xFFFF);
                    k19 = (Int16Bits(Mm(r.RelPos.X)) & 0xFFFF) | ((Int16Bits(Mm(r.RelPos.Z)) & 0xFFFF) << 16);
                    k20 = Int16Bits(Mm(r.RelPos.Y)) & 0xFFFF;
                }
                if (r.PoseSaid)
                {
                    k18 |= FlagPoseSaid;
                    k21 = r.PoseStampMs;
                    k22 = Int32Mm(r.Pos.X);
                    k23 = Int32Mm(r.Pos.Y);
                    k24 = Int32Mm(r.Pos.Z);
                    k25 = (Int16Bits(Cm(r.Vel.X)) & 0xFFFF) | ((Int16Bits(Cm(r.Vel.Z)) & 0xFFFF) << 16);
                    k26 = (Int16Bits(Cm(r.Vel.Y)) & 0xFFFF) | ((Turn16(r.Yaw) & 0xFFFF) << 16);
                    k27 = r.Seq & 0xFFFF;
                }
            }
            bool changed = false;
            changed |= Set(ints, At + 0, k17);
            changed |= Set(ints, At + 1, k18);
            changed |= Set(ints, At + 2, k19);
            changed |= Set(ints, At + 3, k20);
            changed |= Set(ints, At + 4, k21);
            changed |= Set(ints, At + 5, k22);
            changed |= Set(ints, At + 6, k23);
            changed |= Set(ints, At + 7, k24);
            changed |= Set(ints, At + 8, k25);
            changed |= Set(ints, At + 9, k26);
            changed |= Set(ints, At + 10, k27);
            return changed;
        }

        /// <summary>Ints At.. of a record; all 0 (an older build) or Kind 0 = default = not said.</summary>
        public static RideRecord Unpack(int[] ints)
        {
            RideRecord r = default(RideRecord);
            if (ints == null || ints.Length < At + Count) return r;
            int k17 = ints[At];
            int kind = k17 & 0x3;
            if (kind == 0) return r;
            r.Kind = (byte)kind;
            r.Airborne = (k17 & (1 << 2)) != 0;
            r.Swimming = (k17 & (1 << 3)) != 0;
            r.GaitAnim = (k17 >> 4) & 0x3FF;
            r.GaitRate = ((k17 >> 14) & 0x1FF) / 100f;
            r.MountUpCounter = (k17 >> 23) & 0xF;
            r.Epoch = (k17 >> 27) & 0x1F;
            int k18 = ints[At + 1];
            r.RelativeSaid = (k18 & FlagRelativeSaid) != 0;
            if (r.RelativeSaid)
            {
                r.RelYawDeg = Degrees16(k18 & 0xFFFF);
                int k19 = ints[At + 2], k20 = ints[At + 3];
                r.RelPos = new RideVec((short)(k19 & 0xFFFF) / 1000f, (short)(k20 & 0xFFFF) / 1000f, (short)((k19 >> 16) & 0xFFFF) / 1000f);
            }
            r.PoseSaid = (k18 & FlagPoseSaid) != 0;
            if (r.PoseSaid)
            {
                r.PoseStampMs = ints[At + 4];
                r.Pos = new RideVec(ints[At + 5] / 1000f, ints[At + 6] / 1000f, ints[At + 7] / 1000f);
                int k25 = ints[At + 8], k26 = ints[At + 9];
                r.Vel = new RideVec((short)(k25 & 0xFFFF) / 100f, (short)(k26 & 0xFFFF) / 100f, (short)((k25 >> 16) & 0xFFFF) / 100f);
                float yaw = Degrees16((k26 >> 16) & 0xFFFF);
                r.Yaw = yaw < 0f ? yaw + 360f : yaw;
                r.Seq = ints[At + 10] & 0xFFFF;
            }
            return r;
        }

        // ------------------------------------------------------------------ bits

        private static int RateBits(float rate)
        {
            if (float.IsNaN(rate) || rate <= 0f) return 0;
            int v = (int)Math.Round(rate * 100f);
            return v > 511 ? 511 : v;
        }

        /// <summary>Degrees -> 1/65536 of a turn (0..65535), any sign or size.</summary>
        private static int Turn16(float deg)
        {
            if (float.IsNaN(deg) || float.IsInfinity(deg)) return 0;
            double t = deg / 360.0;
            t -= Math.Floor(t);
            int v = (int)Math.Round(t * 65536.0);
            return v & 0xFFFF;
        }

        /// <summary>1/65536 of a turn -> degrees in (-180, 180].</summary>
        private static float Degrees16(int v)
        {
            float d = (v & 0xFFFF) * (360f / 65536f);
            return d > 180f ? d - 360f : d;
        }

        private static int Mm(float metres) { return Round(metres * 1000.0); }
        private static int Cm(float metresPerSecond) { return Round(metresPerSecond * 100.0); }

        private static int Round(double v)
        {
            if (double.IsNaN(v)) return 0;
            double r = Math.Round(v);
            if (r > int.MaxValue) return int.MaxValue;
            if (r < int.MinValue) return int.MinValue;
            return (int)r;
        }

        private static int Int32Mm(float metres) { return Round(metres * 1000.0); }

        /// <summary>A value clamped to int16 as its 16 bits.</summary>
        private static int Int16Bits(int v) { return Clamp(v, short.MinValue, short.MaxValue) & 0xFFFF; }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private static bool Set(int[] a, int i, int v)
        {
            if (a[i] == v) return false;
            a[i] = v;
            return true;
        }
    }
}
