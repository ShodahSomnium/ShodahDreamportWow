using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>MovementFlags / MovementFlags2 (AzerothCore UnitDefines.h, 3.3.5a).</summary>
    public static class MoveFlags
    {
        public const uint Forward = 0x00000001, Backward = 0x00000002, StrafeLeft = 0x00000004, StrafeRight = 0x00000008;
        public const uint Left = 0x00000010, Right = 0x00000020, PitchUp = 0x00000040, PitchDown = 0x00000080;
        public const uint Walking = 0x00000100, OnTransport = 0x00000200, DisableGravity = 0x00000400, Root = 0x00000800;
        public const uint Falling = 0x00001000, FallingFar = 0x00002000, Swimming = 0x00200000, Ascending = 0x00400000;
        public const uint Descending = 0x00800000, CanFly = 0x01000000, Flying = 0x02000000, SplineElevation = 0x04000000;
        public const uint SplineEnabled = 0x08000000, WaterWalking = 0x10000000, FallingSlow = 0x20000000, Hover = 0x40000000;
        /// <summary>MOVEMENTFLAG_MASK_MOVING.</summary>
        public const uint MaskMoving = Forward | Backward | StrafeLeft | StrafeRight | PitchUp | PitchDown | Falling | FallingFar | Ascending | Descending | SplineElevation;
        public const uint MaskTurning = Left | Right;
        // flags2
        public const ushort F2AlwaysAllowPitching = 0x0020, F2InterpolatedMovement = 0x0400;
    }

    /// <summary>UnitMoveType (AzerothCore UnitDefines.h): index into the 9 speeds of the movement block.</summary>
    public static class MoveType
    {
        public const int Walk = 0, Run = 1, RunBack = 2, Swim = 3, SwimBack = 4, TurnRate = 5, Flight = 6, FlightBack = 7, PitchRate = 8, Count = 9;
        /// <summary>Unit.cpp baseMoveSpeed[MAX_MOVE_TYPE] (defaults until the server sends the unit's speeds).</summary>
        public static readonly float[] Base = { 2.5f, 7.0f, 4.5f, 4.722222f, 2.5f, 3.141594f, 7.0f, 4.5f, 3.14f };
    }

    /// <summary>
    /// MovementInfo exactly as AzerothCore WorldSession::ReadMovementInfo / WriteMovementInfo (and Unit::BuildMovementPacket) lay it
    /// out: uint32 flags, uint16 flags2, uint32 time, x y z o; [ONTRANSPORT: packed guid, x y z o, uint32 time, int8 seat,
    /// [INTERPOLATED_MOVEMENT: uint32 time2]]; [SWIMMING|FLYING or ALWAYS_ALLOW_PITCHING: float pitch]; uint32 fallTime;
    /// [FALLING: zspeed, sinAngle, cosAngle, xyspeed]; [SPLINE_ELEVATION: float].
    /// </summary>
    public sealed class MovementInfo
    {
        public uint Flags;
        public ushort Flags2;
        public uint Time;
        public float X, Y, Z, O;
        public ulong TransportGuid;
        public float TX, TY, TZ, TO;
        public uint TransportTime, TransportTime2;
        public sbyte TransportSeat;
        public float Pitch;
        public uint FallTime;
        public float JumpZSpeed, JumpSin, JumpCos, JumpXYSpeed;
        public float SplineElevation;

        public bool Has(uint f) { return (Flags & f) != 0; }

        public void Read(WowPacket p)
        {
            Flags = p.U32();
            Flags2 = p.U16();
            Time = p.U32();
            X = p.F32(); Y = p.F32(); Z = p.F32(); O = p.F32();
            if ((Flags & MoveFlags.OnTransport) != 0)
            {
                TransportGuid = p.PackedGuid();
                TX = p.F32(); TY = p.F32(); TZ = p.F32(); TO = p.F32();
                TransportTime = p.U32();
                TransportSeat = (sbyte)p.U8();
                if ((Flags2 & MoveFlags.F2InterpolatedMovement) != 0) TransportTime2 = p.U32();
            }
            else TransportGuid = 0;
            if ((Flags & (MoveFlags.Swimming | MoveFlags.Flying)) != 0 || (Flags2 & MoveFlags.F2AlwaysAllowPitching) != 0) Pitch = p.F32();
            FallTime = p.U32();
            if ((Flags & MoveFlags.Falling) != 0)
            {
                JumpZSpeed = p.F32(); JumpSin = p.F32(); JumpCos = p.F32(); JumpXYSpeed = p.F32();
            }
            if ((Flags & MoveFlags.SplineElevation) != 0) SplineElevation = p.F32();
        }

        public void Write(PacketWriter w)
        {
            w.U32(Flags).U16(Flags2).U32(Time).F32(X).F32(Y).F32(Z).F32(O);
            if ((Flags & MoveFlags.OnTransport) != 0)
            {
                w.PackedGuid(TransportGuid).F32(TX).F32(TY).F32(TZ).F32(TO).U32(TransportTime).U8((byte)TransportSeat);
                if ((Flags2 & MoveFlags.F2InterpolatedMovement) != 0) w.U32(TransportTime2);
            }
            if ((Flags & (MoveFlags.Swimming | MoveFlags.Flying)) != 0 || (Flags2 & MoveFlags.F2AlwaysAllowPitching) != 0) w.F32(Pitch);
            w.U32(FallTime);
            if ((Flags & MoveFlags.Falling) != 0) w.F32(JumpZSpeed).F32(JumpSin).F32(JumpCos).F32(JumpXYSpeed);
            if ((Flags & MoveFlags.SplineElevation) != 0) w.F32(SplineElevation);
        }

        public MovementInfo Clone() { return (MovementInfo)MemberwiseClone(); }
    }

    /// <summary>MoveSplineFlag (AzerothCore Movement/Spline/MoveSplineFlag.h).</summary>
    public static class SplineFlags
    {
        public const uint Done = 0x00000100, Falling = 0x00000200, NoSpline = 0x00000400, Parabolic = 0x00000800, CanSwim = 0x00001000;
        public const uint Flying = 0x00002000, OrientationFixed = 0x00004000, FinalPoint = 0x00008000, FinalTarget = 0x00010000, FinalAngle = 0x00020000;
        public const uint Catmullrom = 0x00040000, Cyclic = 0x00080000, EnterCycle = 0x00100000, Animation = 0x00200000, Frozen = 0x00400000;
        public const uint TransportEnter = 0x00800000, TransportExit = 0x01000000, OrientationInversed = 0x08000000;
        public const uint MaskCatmullRom = Flying | Catmullrom;
    }

    /// <summary>
    /// A server-driven path (SMSG_MONSTER_MOVE or the spline of a create block) evaluated over time. Points are WoW positions;
    /// the unit moves at constant speed along them (AC initLengths: segment time proportional to length) over Duration ms.
    /// Catmull-Rom mode (Flying/Catmullrom flags) is a uniform Catmull-Rom through the points; linear mode joins them straight.
    /// </summary>
    public sealed class SplinePath
    {
        public uint Flags;
        public int DurationMs;
        public uint Id;
        public readonly List<float> Pts = new List<float>();   // x,y,z triples; Pts[0..2] = the start
        public bool Smooth, Cyclic;
        public int FacingKind;              // 0 none, 1 angle, 2 target, 3 point
        public float FacingAngle, FacingX, FacingY, FacingZ;
        public ulong FacingTarget;
        public float VerticalAccel;
        public int EffectStartMs;
        public double StartTime;            // session clock (s) when time 0 of the path was
        float[] _cum;                       // cumulative length at each point

        public int Count { get { return Pts.Count / 3; } }

        public void AddPoint(float x, float y, float z) { Pts.Add(x); Pts.Add(y); Pts.Add(z); _cum = null; }

        public float Length
        {
            get { Prepare(); return _cum.Length == 0 ? 0f : _cum[_cum.Length - 1]; }
        }

        void Prepare()
        {
            if (_cum != null) return;
            int n = Count;
            _cum = new float[n];
            for (int i = 1; i < n; i++)
            {
                float dx = Pts[i * 3] - Pts[i * 3 - 3], dy = Pts[i * 3 + 1] - Pts[i * 3 - 2], dz = Pts[i * 3 + 2] - Pts[i * 3 - 1];
                _cum[i] = _cum[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        /// <summary>True when the path is over at session time now (never for cyclic paths).</summary>
        public bool Finished(double now) { return !Cyclic && (now - StartTime) * 1000.0 >= DurationMs; }

        /// <summary>Position and travel direction (radians) at session time now.</summary>
        public void Evaluate(double now, out float x, out float y, out float z, out float dirO)
        {
            Prepare();
            int n = Count;
            x = y = z = 0f; dirO = 0f;
            if (n == 0) return;
            double ms = (now - StartTime) * 1000.0;
            if (ms < 0) ms = 0;
            if (Cyclic && DurationMs > 0) ms %= DurationMs;
            if (ms > DurationMs) ms = DurationMs;
            float total = _cum[n - 1];
            if (n == 1 || total <= 1e-4f || DurationMs <= 0)
            {
                int last = n - 1;
                x = Pts[last * 3]; y = Pts[last * 3 + 1]; z = Pts[last * 3 + 2];
                if (n > 1) dirO = (float)Math.Atan2(Pts[last * 3 + 1] - Pts[last * 3 - 2], Pts[last * 3] - Pts[last * 3 - 3]);
                return;
            }
            float d = (float)(ms / DurationMs) * total;
            int seg = 0;
            while (seg < n - 2 && _cum[seg + 1] < d) seg++;
            float segLen = _cum[seg + 1] - _cum[seg];
            float u = segLen > 1e-5f ? (d - _cum[seg]) / segLen : 1f;
            if (u < 0f) u = 0f; else if (u > 1f) u = 1f;
            int i0 = Math.Max(seg - 1, 0), i1 = seg, i2 = seg + 1, i3 = Math.Min(seg + 2, n - 1);
            if (Smooth)
            {
                for (int k = 0; k < 3; k++)
                {
                    float p0 = Pts[i0 * 3 + k], p1 = Pts[i1 * 3 + k], p2 = Pts[i2 * 3 + k], p3 = Pts[i3 * 3 + k];
                    if (seg == 0) p0 = 2f * p1 - p2;                 // virtual point before the start
                    if (seg + 2 > n - 1) p3 = p2;                    // AC duplicates the last point
                    float v = CatmullRom(p0, p1, p2, p3, u);
                    if (k == 0) x = v; else if (k == 1) y = v; else z = v;
                }
            }
            else
            {
                x = Pts[i1 * 3] + (Pts[i2 * 3] - Pts[i1 * 3]) * u;
                y = Pts[i1 * 3 + 1] + (Pts[i2 * 3 + 1] - Pts[i1 * 3 + 1]) * u;
                z = Pts[i1 * 3 + 2] + (Pts[i2 * 3 + 2] - Pts[i1 * 3 + 2]) * u;
            }
            dirO = (float)Math.Atan2(Pts[i2 * 3 + 1] - Pts[i1 * 3 + 1], Pts[i2 * 3] - Pts[i1 * 3]);
            // MoveSpline::computeParabolicElevation
            if ((Flags & SplineFlags.Parabolic) != 0 && ms > EffectStartMs)
            {
                float tp = (float)(ms - EffectStartMs) / 1000f, td = (DurationMs - EffectStartMs) / 1000f;
                z += (td - tp) * 0.5f * VerticalAccel * tp;
            }
        }

        static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// The body of SMSG_MONSTER_MOVE after the packed guid (and after transport guid + seat for SMSG_MONSTER_MOVE_TRANSPORT), as
        /// AzerothCore PacketBuilder::WriteMonsterMove writes it. Returns null for MonsterMoveStop (type 1): the unit stops at (x,y,z).
        /// Linear paths: uint32 count, the destination, then count-1 packed offsets from the middle of start and destination
        /// (WriteLinearPath / ByteBuffer::appendPackXYZ: 11/11/10 signed bits of 0.25 yd).
        /// </summary>
        public static SplinePath ReadMonsterMove(WowPacket p, out float sx, out float sy, out float sz, out uint splineId)
        {
            p.U8();                                          // MOVEMENTFLAG2_UNK7 toggle
            sx = p.F32(); sy = p.F32(); sz = p.F32();
            splineId = p.U32();
            byte type = p.U8();
            if (type == 1) return null;                     // MonsterMoveStop
            var s = new SplinePath { Id = splineId };
            switch (type)
            {
                case 2: s.FacingKind = 3; s.FacingX = p.F32(); s.FacingY = p.F32(); s.FacingZ = p.F32(); break;   // FacingSpot
                case 3: s.FacingKind = 2; s.FacingTarget = p.U64(); break;                                        // FacingTarget
                case 4: s.FacingKind = 1; s.FacingAngle = p.F32(); break;                                         // FacingAngle
            }
            s.Flags = p.U32();
            if ((s.Flags & SplineFlags.Animation) != 0) { p.U8(); s.EffectStartMs = p.I32(); }
            s.DurationMs = p.I32();
            if ((s.Flags & SplineFlags.Parabolic) != 0) { s.VerticalAccel = p.F32(); s.EffectStartMs = p.I32(); }
            uint count = p.U32();
            if (count > 1000) throw new FormatException("monster move with " + count + " points");
            s.Smooth = (s.Flags & SplineFlags.MaskCatmullRom) != 0;
            s.Cyclic = (s.Flags & SplineFlags.Cyclic) != 0;
            s.AddPoint(sx, sy, sz);
            if (s.Smooth)
            {
                for (uint i = 0; i < count; i++) s.AddPoint(p.F32(), p.F32(), p.F32());
                // WriteCatmullRomCyclicPath adds a fake point (flying: first = the start; ground: last = zero vector) - drop it.
                if (s.Cyclic && count > 0)
                {
                    int n = s.Count;
                    if ((s.Flags & SplineFlags.Flying) != 0) s.Pts.RemoveRange(3, 3);
                    else if (s.Pts[n * 3 - 3] == 0f && s.Pts[n * 3 - 2] == 0f && s.Pts[n * 3 - 1] == 0f) s.Pts.RemoveRange(n * 3 - 3, 3);
                    s._cum = null;
                }
            }
            else
            {
                float dx = p.F32(), dy = p.F32(), dz = p.F32();
                float mx = (sx + dx) * 0.5f, my = (sy + dy) * 0.5f, mz = (sz + dz) * 0.5f;
                for (uint i = 1; i < count; i++)
                {
                    uint packed = p.U32();
                    float ox = ((int)(packed << 21) >> 21) * 0.25f;
                    float oy = ((int)(packed << 10) >> 21) * 0.25f;
                    float oz = ((int)packed >> 22) * 0.25f;
                    s.AddPoint(mx - ox, my - oy, mz - oz);
                }
                s.AddPoint(dx, dy, dz);
            }
            return s;
        }

        /// <summary>
        /// The spline part of a living movement block (AC PacketBuilder::WriteCreate): flags, facing (angle / uint64 target / point),
        /// int32 timePassed, int32 duration, uint32 id, float 1, float 1, float vertical acceleration, int32 effect start time,
        /// uint32 node count + nodes (the spline's internal point array incl. AC's virtual end points), uint8 mode, final destination.
        /// </summary>
        public static SplinePath ReadCreate(WowPacket p, out int timePassedMs)
        {
            var s = new SplinePath();
            s.Flags = p.U32();
            if ((s.Flags & SplineFlags.FinalAngle) != 0) { s.FacingKind = 1; s.FacingAngle = p.F32(); }
            else if ((s.Flags & SplineFlags.FinalTarget) != 0) { s.FacingKind = 2; s.FacingTarget = p.U64(); }
            else if ((s.Flags & SplineFlags.FinalPoint) != 0) { s.FacingKind = 3; s.FacingX = p.F32(); s.FacingY = p.F32(); s.FacingZ = p.F32(); }
            timePassedMs = p.I32();
            s.DurationMs = p.I32();
            s.Id = p.U32();
            p.F32(); p.F32();                                // duration_mod, duration_mod_next
            s.VerticalAccel = p.F32();
            s.EffectStartMs = p.I32();
            uint nodes = p.U32();
            if (nodes > 1000) throw new FormatException("spline with " + nodes + " nodes");
            var raw = new float[nodes * 3];
            for (int i = 0; i < raw.Length; i++) raw[i] = p.F32();
            byte mode = p.U8();                              // 0 linear, 1 catmullrom
            p.F32(); p.F32(); p.F32();                       // final destination (zero when cyclic)
            s.Cyclic = (s.Flags & SplineFlags.Cyclic) != 0;
            s.Smooth = mode == 1;
            // Spline.cpp InitLinear: [c0..cn-1, cn-1]; InitCatmullRom: [virtual, c0..cn-1, virtual(, virtual when cyclic)].
            int first = s.Smooth ? 1 : 0;
            int last = (int)nodes - 1 - (s.Smooth && s.Cyclic ? 2 : 1);
            for (int i = first; i <= last; i++) s.AddPoint(raw[i * 3], raw[i * 3 + 1], raw[i * 3 + 2]);
            return s;
        }
    }
}
