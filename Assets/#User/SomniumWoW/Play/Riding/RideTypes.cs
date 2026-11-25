using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (lead contract PLAN7 s3.3; owner Ride). Pure riding types (NO UnityEngine - the harness compiles this). Every public
    // signature of the contract is unchanged; the members marked "Ride addition" are internal bookkeeping of RideMotion.

    /// <summary>Unity axes (x right, y up, z forward), metres: a pure mirror of Vector3 so the harness can run the ride's maths.</summary>
    public struct RideVec
    {
        public float X, Y, Z;
        public RideVec(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static RideVec operator +(RideVec a, RideVec b) { return new RideVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static RideVec operator -(RideVec a, RideVec b) { return new RideVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static RideVec operator *(RideVec a, float k) { return new RideVec(a.X * k, a.Y * k, a.Z * k); }
        public float Length { get { return (float)System.Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public float PlanarLength { get { return (float)System.Math.Sqrt(X * X + Z * Z); } }
    }

    public enum RideMode : byte { Ground = 0, Airborne = 1, Flying = 2, Swimming = 3 }

    public enum RideSpeedStep : byte { Stop = 0, Walk = 1, Run = 2 }

    /// <summary>One frame of player input, already mapped from keys / mouse / XR sticks by MountInput.</summary>
    public struct RideIntent
    {
        public int Forward;            // -1, 0, +1
        public RideSpeedStep Step;     // Walk / Run while Forward != 0 (desktop: always Run)
        public int Strafe;             // -1, 0, +1 (desktop Q / E)
        public float TurnAxis;         // -1..1 shaped; + = turn RIGHT (D key, VR stick right) = Unity yaw increasing
        public float TurnDeltaDeg;     // Unity yaw degrees added this frame, + = right (desktop mouse steering; the client's VR snap / smooth turn read back)
        public bool JumpPressed, AscendHeld, DescendHeld;
        public bool ClimbValid;        // ClimbDeg is meaningful (desktop right mouse held; VR head pitch outside the dead zone)
        public float ClimbDeg;         // wanted climb angle while flying, + up, capped +-60
    }

    /// <summary>Speeds in m/s (PlayerState yd/s x 0.9144) plus WoW's physics and our tuning (PLAN7 s4.8).</summary>
    public struct RideLimits
    {
        public float Walk, Run, RunBack, Swim, SwimBack, Flight, FlightBack;
        public float TurnRateDeg;      // at |TurnAxis| = 1 (keys: Player.TurnRate in degrees; VR stick: VrSteerDeg, s4.8)
        public bool CanFly;
        public float Gravity, Terminal, JumpSpeed, AscendFactor;              // m/s^2, m/s, m/s, x Flight
        public float AccelGround, DecelGround, AccelFlight, DecelFlight;       // seconds 0 -> full, full -> 0
        /// <summary>Ride addition (internal): seconds from 0 to the full TurnRateDeg (keys 0.08, VR stick 0.15).</summary>
        internal float TurnRamp;

        // ---- WoW's numbers (AzerothCore, quoted in PLAN7 s4.2) ----------------------------------------------------------------
        /// <summary>AzerothCore Movement/Spline/MovementUtil.cpp "double gravity = 19.29110527038574;" (yd/s^2 = 17.640 m/s^2).</summary>
        internal const float GravityYards = 19.29110527038574f;
        /// <summary>AzerothCore MovementUtil.cpp "float terminalVelocity = 60.148003f;" (yd/s = 55.0 m/s).</summary>
        internal const float TerminalYards = 60.148003f;
        internal const float MetresPerYard = 0.9144f;

        // ---- ours (PLAN7 s4.8; WowMount logs every one of them once at start) ----------------------------------------------------
        /// <summary>Ours: 7.5 m/s up at take-off = a 1.59 m apex under 17.64 m/s^2. WoW's value is a client constant (not in
        /// AzerothCore); the Online builder logs the first relayed real-client MSG_MOVE_JUMP so it can be measured and set here.</summary>
        internal const float JumpLaunch = 7.5f;
        /// <summary>Ours: ascend / descend speed = 0.6 x FlightSpeed (the real client's rate is not in AzerothCore).</summary>
        internal const float AscendShare = 0.6f;
        /// <summary>Ours (comfort; WoW starts and stops instantly - decision D9): seconds 0 -> target / target -> 0, smoothstep on speed.</summary>
        internal const float GroundAccel = 0.20f, GroundDecel = 0.15f, FlightAccel = 0.35f, FlightDecel = 0.30f;
        /// <summary>Ours: the keys reach the full turn rate in 0.08 s (a tap is not a snap); the VR stick in 0.15 s (ETS steering).</summary>
        internal const float KeyTurnRamp = 0.08f, VrTurnRamp = 0.15f;
        /// <summary>Ours: VR left-stick steering at full deflection, deg/s (ETS left-stick steering, EtsVr / EtsTruckController).</summary>
        internal const float VrSteerDeg = 90f;

        /// <summary>The limits for this frame from the session's PlayerState (yd/s -> m/s). vr = the left stick steers (VrSteerDeg)
        /// instead of the keys' WoW turn rate. A null player (or a zero speed) gives AzerothCore's base speeds.</summary>
        public static RideLimits From(PlayerState p, bool vr)
        {
            RideLimits l;
            const float K = MetresPerYard;
            l.Walk = Pos(p != null ? p.WalkSpeed : 0f, MountCatalog.BaseWalk) * K;
            l.Run = Pos(p != null ? p.RunSpeed : 0f, MountCatalog.BaseRun) * K;
            l.RunBack = Pos(p != null ? p.RunBackSpeed : 0f, MountCatalog.BaseRunBack) * K;
            l.Swim = Pos(p != null ? p.SwimSpeed : 0f, MountCatalog.BaseSwim) * K;
            l.SwimBack = Pos(p != null ? p.SwimBackSpeed : 0f, MountCatalog.BaseSwimBack) * K;
            l.Flight = Pos(p != null ? p.FlightSpeed : 0f, MountCatalog.BaseFlight) * K;
            l.FlightBack = Pos(p != null ? p.FlightBackSpeed : 0f, MountCatalog.BaseFlightBack) * K;
            float turn = Pos(p != null ? p.TurnRate : 0f, MountCatalog.BaseTurnRate) * (float)(180.0 / Math.PI);
            l.TurnRateDeg = vr ? VrSteerDeg : turn;
            l.TurnRamp = vr ? VrTurnRamp : KeyTurnRamp;
            l.CanFly = p != null && p.CanFly;
            l.Gravity = GravityYards * K;
            l.Terminal = TerminalYards * K;
            l.JumpSpeed = JumpLaunch;
            l.AscendFactor = AscendShare;
            l.AccelGround = GroundAccel;
            l.DecelGround = GroundDecel;
            l.AccelFlight = FlightAccel;
            l.DecelFlight = FlightDecel;
            return l;
        }

        /// <summary>Ride addition: every speed x k (the logged "world ahead not ready" cap; ours). Turn rate and physics unchanged.</summary>
        internal RideLimits Scaled(float k)
        {
            RideLimits l = this;
            l.Walk *= k; l.Run *= k; l.RunBack *= k; l.Swim *= k; l.SwimBack *= k; l.Flight *= k; l.FlightBack *= k;
            return l;
        }

        private static float Pos(float v, float fallback) { return v > 0.01f && !float.IsNaN(v) && !float.IsInfinity(v) ? v : fallback; }
    }

    /// <summary>What the proxy found this frame (MountMotor fills it after CharacterController.Move).</summary>
    public struct RideGround
    {
        public bool Grounded, HaveCollider;  // HaveCollider = a layer-0 collider under the proxy within 60 m, or the proxy > 60 m above the known ADT terrain (ground built)
        public float LiquidDepth;            // m of liquid above the proxy's feet, 0 = dry
        public float LiquidSurfaceY;         // Unity y of the surface when LiquidDepth > 0
        public float TerrainY;               // ADT terrain height under the proxy (float.NaN = unknown), for the fall-through rescue
        public float CollisionHeight;        // m (PlayerState.CollisionHeight x 0.9144, or the MountModel estimate, s4.2)
    }

    public struct RideState
    {
        public RideMode Mode;
        public float Heading;          // Unity yaw, degrees
        public float TurnRate;         // current shaped turn rate, deg/s
        public float SpeedFactor;      // 0..1 of the ramp
        public float FallTime, ClimbDeg, Pitch;
        public RideVec Velocity;       // m/s this frame
        public RideVec JumpVelocity;   // planar velocity frozen at take-off (WoW's jump vector)
        public int Forward, Strafe, Turn, Vertical;   // Forward / Strafe = the direction in use (kept while a stop ramps down); Turn +1 = left (PlayerMotion's sign)
        public bool Walking, AtSpeed, Jumped, Landed;

        // Ride additions (internal; RideMotion's bookkeeping, not part of the contract):
        internal float RampFrom, RampTo;   // m/s: the speed ramp runs RampFrom -> RampTo while SpeedFactor goes 0 -> 1 (smoothstep)
        internal float Ungrounded;         // s without ground under the proxy while in Ground mode (a short grace before falling)
    }
}
