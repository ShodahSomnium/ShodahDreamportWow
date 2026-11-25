using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    /// <summary>
    /// The local ride's motion, pure and allocation-free (iteration 7, owner Ride; PLAN7 s5.3 "Motion", numbers s4.2 / s4.8).
    /// One call per RENDERED frame (any dt): the displacement it returns is handed to the proxy CharacterController's Move, whose
    /// collision result comes back as the next frame's RideGround. Everything that is not WoW's (ramps, filters, grace times, the
    /// swim hold) is a named constant here or in RideLimits, commented "ours", and logged once by WowMount at start.
    ///
    /// Speed: the wanted planar speed is Forward x (Walk | Run | RunBack) on the ground, Swim | SwimBack in liquid, Flight | FlightBack
    /// flying; strafing moves at the forward speed, diagonals normalised (what normal clients dead-reckon). It ramps from the speed
    /// now to the wanted one with a smoothstep over AccelGround / DecelGround (Accel/DecelFlight flying), scaled by the share of the
    /// full speed that changes; a reversal (forward -> back) stops first. The direction in use (Forward / Strafe / Walking) stays set
    /// until a stop has ramped down, so the online STOP carries the position where the mount really stopped. At steady state the
    /// speed is EXACTLY the session's (online dead reckoning depends on it); AtSpeed marks the frame a ramp arrives.
    /// Turning: MoveTowards to TurnAxis x TurnRateDeg in TurnRamp seconds, plus TurnDeltaDeg as given (mouse steering).
    /// Ground: pulled down each frame by max(the client's stepOffset 0.3 m, the planar travel x tan 45) so slopes and steps are
    /// followed (the client's own trick, NonXRMovementProvider 187-190); no ground for UngroundedGrace -> Airborne.
    /// Airborne: WoW's gravity down to its terminal velocity, the planar velocity frozen at take-off (WoW's jump vector), FallTime.
    /// Flying (only while CanFly): no gravity, the heading's forward pitched by the climb angle, +- AscendFactor x Flight for
    /// ascend / descend, hovering without input, landing when it touches ground while not climbing; CanFly lost -> Airborne.
    /// Swimming (AzerothCore: liquid above the feet > 0.75 x collision height): the feet held 0.8 x collision height under the
    /// surface, no gravity; out below 0.65 x, or on the bed with less than 0.75 x.
    /// No collider under the proxy (the world not built yet): altitude held, no gravity, no ground pull.
    /// </summary>
    public static class RideMotion
    {
        // ---- ours (PLAN7 s4.8) -------------------------------------------------------------------------------------------------
        /// <summary>Ours: a longer frame (a hitch, a loading stall) is stepped as this many seconds (no tunnelling, no huge fall).</summary>
        internal const float MaxStepSeconds = 0.25f;
        /// <summary>The grounded pull per frame at least: the Somnium avatar controller's stepOffset 0.3 m (client prefabs, PLAN7 s4.2).</summary>
        internal const float GroundSnapMin = 0.3f;
        /// <summary>The grounded pull also covers the planar travel x tan(slopeLimit 45) = x 1 (down-slopes up to the client's slope limit).</summary>
        internal const float GroundSnapSlope = 1f;
        /// <summary>Ours: seconds without ground (in Ground mode, with a collider below) before the mount falls - a one-frame miss of
        /// CharacterController.isGrounded on a bump must not become a jump and a landing online.</summary>
        internal const float UngroundedGrace = 0.05f;
        /// <summary>Ours: the vertical step filter's rate limit = max(3 m/s, planar speed x tan 55 deg); snap beyond 0.6 m.</summary>
        internal const float StepFilterMinRate = 3f, StepFilterSlopeTan = 1.4281480f, StepFilterSnap = 0.6f;
        /// <summary>AzerothCore Unit.cpp 4217-4219: swimming when liquid - z > 0.75 x collision height ("exact mirror of client logic").</summary>
        internal const float SwimEnter = 0.75f;
        /// <summary>Ours: hold the feet at 0.8 x collision height under the surface; leave below 0.65 x (hysteresis).</summary>
        internal const float SwimHold = 0.8f, SwimLeave = 0.65f;
        /// <summary>Ours: 1/s, exponential approach to the swim hold depth.</summary>
        internal const float SwimHoldRate = 6f;
        /// <summary>Ours: 1/s, exponential approach of the climb angle to the wanted one; the climb is capped +-60 degrees.</summary>
        internal const float ClimbRate = 8f, MaxClimbDeg = 60f;
        /// <summary>Ours: a target speed change under 2 % of the speed adjusts the running ramp instead of starting a new one (so a
        /// slowly easing cap is followed exactly instead of restarting a smoothstep every frame).</summary>
        internal const float RetargetTolerance = 0.02f;
        /// <summary>The collision height used when neither the session nor the model knows one: the horse's 2.88 yd (PLAN7 s4.2).</summary>
        internal const float DefaultCollisionHeight = 2.88f * 0.9144f;
        /// <summary>ETS dead zone (EtsVr.cs:85 DeadZone = 0.15f), rescaled past it.</summary>
        internal const float StickDeadZone = 0.15f;
        /// <summary>Ours (PLAN7 s4.8, decision D8): VR speed steps from the left stick's |Y|: walk above 0.20, run above 0.75,
        /// run -> walk below 0.68, stop below 0.15.</summary>
        internal const float StickWalkOn = 0.20f, StickRunOn = 0.75f, StickRunOff = 0.68f, StickStopBelow = 0.15f;

        private const float Deg2Rad = (float)(Math.PI / 180.0);

        /// <summary>One rendered frame (any dt): updates s and returns the displacement for the proxy's CharacterController.Move. Allocation-free.</summary>
        public static RideVec Step(ref RideState s, in RideIntent i, in RideLimits l, in RideGround g, float dt)
        {
            s.Jumped = false;
            s.Landed = false;
            s.AtSpeed = false;
            if (!(dt > 0f)) return default(RideVec);
            if (dt > MaxStepSeconds) dt = MaxStepSeconds;
            float ch = g.CollisionHeight > 0.1f ? g.CollisionHeight : DefaultCollisionHeight;

            // ---- turning (keys / stick at the turn rate, ramped; mouse / read-back degrees as given)
            float axis = Clamp(i.TurnAxis, -1f, 1f);
            float rateMax = Math.Abs(l.TurnRateDeg);
            float ramp = l.TurnRamp > 1e-4f ? l.TurnRamp : RideLimits.KeyTurnRamp;
            s.TurnRate = MoveTowards(s.TurnRate, axis * rateMax, rateMax / ramp * dt);
            float delta = float.IsNaN(i.TurnDeltaDeg) || float.IsInfinity(i.TurnDeltaDeg) ? 0f : i.TurnDeltaDeg;
            s.Heading = WrapDeg(s.Heading + s.TurnRate * dt + delta);
            s.Turn = axis > 0.5f ? -1 : axis < -0.5f ? 1 : 0;   // PlayerMotion.Turn: +1 = turning left (Unity yaw decreasing)

            // ---- mode changes
            bool ascend = i.AscendHeld && !i.DescendHeld;
            bool descend = i.DescendHeld && !i.AscendHeld;
            switch (s.Mode)
            {
                case RideMode.Ground:
                    if (g.Grounded || !g.HaveCollider) s.Ungrounded = 0f; else s.Ungrounded += dt;
                    if (l.CanFly && ascend) Enter(ref s, RideMode.Flying);                                 // take off (no jump first)
                    else if (g.LiquidDepth > SwimEnter * ch) Enter(ref s, RideMode.Swimming);
                    else if (i.JumpPressed && g.Grounded)
                    {
                        s.JumpVelocity = Planar(s.Velocity);
                        s.Velocity = new RideVec(s.JumpVelocity.X, l.JumpSpeed, s.JumpVelocity.Z);
                        Enter(ref s, RideMode.Airborne);
                        s.Jumped = true;
                    }
                    else if (s.Ungrounded > UngroundedGrace)
                    {
                        s.JumpVelocity = Planar(s.Velocity);                                             // walked off an edge: falls with its momentum
                        s.Velocity = new RideVec(s.JumpVelocity.X, 0f, s.JumpVelocity.Z);
                        Enter(ref s, RideMode.Airborne);
                    }
                    break;
                case RideMode.Airborne:
                    if (l.CanFly && ascend) Enter(ref s, RideMode.Flying);
                    else if (g.LiquidDepth > SwimEnter * ch) Enter(ref s, RideMode.Swimming);
                    else if (g.Grounded && s.Velocity.Y <= 0f)
                    {
                        Enter(ref s, RideMode.Ground);
                        s.Landed = true;
                    }
                    break;
                case RideMode.Flying:
                    if (!l.CanFly)
                    {
                        s.JumpVelocity = Planar(s.Velocity);
                        s.Velocity = new RideVec(s.JumpVelocity.X, Math.Min(0f, s.Velocity.Y), s.JumpVelocity.Z);
                        Enter(ref s, RideMode.Airborne);
                    }
                    else if (g.Grounded && !ascend && s.Velocity.Y <= 0.01f) Enter(ref s, RideMode.Ground);   // a flying landing: no FALL_LAND
                    else if (g.LiquidDepth > SwimEnter * ch && s.Velocity.Y <= 0f) Enter(ref s, RideMode.Swimming);
                    break;
                case RideMode.Swimming:
                    if (l.CanFly && ascend) Enter(ref s, RideMode.Flying);
                    else if (g.LiquidDepth < SwimLeave * ch || (g.Grounded && g.LiquidDepth < SwimEnter * ch)) Enter(ref s, RideMode.Ground);
                    break;
            }

            // ---- the speed ramp (frozen while airborne: WoW's jump keeps its launch velocity; the ramp resumes on landing)
            float speed = RampSpeed(ref s);
            if (s.Mode != RideMode.Airborne) speed = UpdateRamp(ref s, i, l, dt);

            // ---- velocity by mode
            float h = s.Heading * Deg2Rad;
            float fx = (float)Math.Sin(h), fz = (float)Math.Cos(h);      // forward (Unity yaw 0 = +z)
            float rx = fz, rz = -fx;                                     // right
            float len = (float)Math.Sqrt(s.Forward * s.Forward + s.Strafe * s.Strafe);
            float kf = len > 0f ? s.Forward / len : 0f, ks = len > 0f ? s.Strafe / len : 0f;
            RideVec move = default(RideVec);
            switch (s.Mode)
            {
                case RideMode.Ground:
                {
                    s.ClimbDeg = 0f; s.Pitch = 0f; s.Vertical = 0; s.FallTime = 0f;
                    s.Velocity = new RideVec((rx * ks + fx * kf) * speed, 0f, (rz * ks + fz * kf) * speed);
                    move = s.Velocity * dt;
                    if (g.HaveCollider) move.Y = -Math.Max(GroundSnapMin, move.PlanarLength * GroundSnapSlope);
                    break;
                }
                case RideMode.Airborne:
                {
                    s.ClimbDeg = 0f; s.Pitch = 0f; s.Vertical = 0;
                    s.FallTime += dt;
                    float vy = s.Velocity.Y;
                    if (g.HaveCollider)
                    {
                        float before = vy;
                        vy = Math.Max(-l.Terminal, vy - l.Gravity * dt);
                        move.Y = (before + vy) * 0.5f * dt;                     // exact for constant gravity (a frame-rate independent arc)
                    }
                    else { vy = 0f; move.Y = 0f; }                                // nothing built below: hold the altitude
                    s.Velocity = new RideVec(s.JumpVelocity.X, vy, s.JumpVelocity.Z);
                    move.X = s.JumpVelocity.X * dt;
                    move.Z = s.JumpVelocity.Z * dt;
                    break;
                }
                case RideMode.Flying:
                {
                    s.FallTime = 0f;
                    float wantClimb = i.ClimbValid ? Clamp(i.ClimbDeg, -MaxClimbDeg, MaxClimbDeg) : 0f;
                    s.ClimbDeg += (wantClimb - s.ClimbDeg) * (1f - (float)Math.Exp(-ClimbRate * dt));
                    float c = s.ClimbDeg * Deg2Rad;
                    float cc = (float)Math.Cos(c), sc = (float)Math.Sin(c);
                    s.Vertical = ascend ? 1 : descend ? -1 : 0;
                    s.Pitch = c;
                    float wantVy = kf * sc * speed + s.Vertical * l.AscendFactor * l.Flight;
                    float accel = l.Flight / Math.Max(1e-3f, l.AccelFlight);    // vertical changes ramp like the flight speed does
                    float vy = MoveTowards(s.Velocity.Y, wantVy, accel * dt);
                    s.Velocity = new RideVec((rx * ks + fx * kf * cc) * speed, vy, (rz * ks + fz * kf * cc) * speed);
                    move = s.Velocity * dt;
                    break;
                }
                case RideMode.Swimming:
                {
                    s.ClimbDeg = 0f; s.Pitch = 0f; s.Vertical = 0; s.FallTime = 0f;
                    float hold = g.LiquidDepth - SwimHold * ch;                   // + = deeper than the hold depth: rise
                    float dy = hold * (1f - (float)Math.Exp(-SwimHoldRate * dt));
                    s.Velocity = new RideVec((rx * ks + fx * kf) * speed, dy / dt, (rz * ks + fz * kf) * speed);
                    move = s.Velocity * dt;
                    move.Y = dy;
                    break;
                }
            }
            return move;
        }

        /// <summary>The visible y after the vertical step filter (stairs, terrain seams). Pass-through while airborne / flying.
        /// The visible height follows the proxy exactly while the proxy's height changes no faster than max(3 m/s, planar speed x tan
        /// 55 deg) - so a steady slope (30 deg at 12.8 m/s = 7.4 m/s) has zero lag - and at that rate while it jumps (a stair: the
        /// controller's stepOffset lifts the proxy in one frame); a gap past 0.6 m snaps (a teleport, a fall). Ours (PLAN7 s4.8); the
        /// plan's extra "exp 30/s" stage is left out on purpose: it lags a steady slope by rate / 30 (0.25 m at 7.4 m/s).</summary>
        public static float FilterY(float visibleY, float proxyY, float planarSpeed, bool airborne, float dt)
        {
            if (airborne || float.IsNaN(visibleY) || float.IsInfinity(visibleY)) return proxyY;
            float gap = proxyY - visibleY;
            if (gap > StepFilterSnap || gap < -StepFilterSnap) return proxyY;
            if (!(dt > 0f)) return visibleY;
            float rate = Math.Max(StepFilterMinRate, Math.Abs(planarSpeed) * StepFilterSlopeTan);
            float max = rate * dt;
            if (gap > max) return visibleY + max;
            if (gap < -max) return visibleY - max;
            return proxyY;
        }

        /// <summary>VR left stick Y -> speed step with hysteresis (PLAN7 s4.8): walk above 0.20, run above 0.75, run -> walk below
        /// 0.68, stop below 0.15. The sign (forward / back) is the caller's.</summary>
        public static RideSpeedStep StickStep(float stickY, RideSpeedStep current)
        {
            float a = Math.Abs(stickY);
            if (float.IsNaN(a)) return RideSpeedStep.Stop;
            switch (current)
            {
                case RideSpeedStep.Run:
                    if (a < StickStopBelow) return RideSpeedStep.Stop;
                    return a < StickRunOff ? RideSpeedStep.Walk : RideSpeedStep.Run;
                case RideSpeedStep.Walk:
                    if (a < StickStopBelow) return RideSpeedStep.Stop;
                    return a > StickRunOn ? RideSpeedStep.Run : RideSpeedStep.Walk;
                default:
                    if (a <= StickWalkOn) return RideSpeedStep.Stop;
                    return a > StickRunOn ? RideSpeedStep.Run : RideSpeedStep.Walk;
            }
        }

        /// <summary>ETS dead zone 0.15 rescaled, then a quadratic response, sign kept.</summary>
        public static float ShapeAxis(float raw)
        {
            if (float.IsNaN(raw)) return 0f;
            float a = Math.Abs(raw);
            if (a <= StickDeadZone) return 0f;
            float t = Math.Min(1f, (a - StickDeadZone) / (1f - StickDeadZone));
            return raw < 0f ? -t * t : t * t;
        }

        /// <summary>The planar speed the ramp gives now (m/s).</summary>
        internal static float RampNow(in RideState s)
        {
            return s.RampFrom + (s.RampTo - s.RampFrom) * Smooth(s.SpeedFactor);
        }

        // ------------------------------------------------------------------------------------------------ internals

        private static float RampSpeed(ref RideState s) { return RampNow(s); }

        private static float UpdateRamp(ref RideState s, in RideIntent i, in RideLimits l, float dt)
        {
            int fwd = i.Forward > 0 ? 1 : i.Forward < 0 ? -1 : 0;
            int str = i.Strafe > 0 ? 1 : i.Strafe < 0 ? -1 : 0;
            bool want = fwd != 0 || str != 0;
            float now = RampNow(s);
            float target;
            bool inUse = s.Forward != 0 || s.Strafe != 0;
            if (want && inUse && fwd * s.Forward + str * s.Strafe < 0 && now > 0.01f)
                target = 0f;                                                   // a reversal: stop first, in the direction in use
            else if (want)
            {
                s.Forward = fwd;
                s.Strafe = str;
                s.Walking = s.Mode == RideMode.Ground && fwd > 0 && i.Step == RideSpeedStep.Walk;
                target = Target(s.Mode, fwd, s.Walking, l);
            }
            else target = 0f;                                                  // stopping: the direction in use stays until the ramp is down

            float top = Math.Max(Math.Max(target, s.RampTo), 0.1f);
            if (Math.Abs(target - s.RampTo) > RetargetTolerance * top)
            {
                s.RampFrom = now;
                s.RampTo = target;
                s.SpeedFactor = 0f;
            }
            else s.RampTo = target;

            bool flying = s.Mode == RideMode.Flying;
            float up = flying ? l.AccelFlight : l.AccelGround, down = flying ? l.DecelFlight : l.DecelGround;
            float span = Math.Abs(s.RampTo - s.RampFrom), full = Math.Max(s.RampTo, s.RampFrom);
            float dur = (s.RampTo >= s.RampFrom ? up : down) * (full > 1e-4f ? span / full : 0f);
            bool arriving = s.SpeedFactor < 1f;
            s.SpeedFactor = dur > 1e-5f ? Math.Min(1f, s.SpeedFactor + dt / dur) : 1f;
            if (arriving && s.SpeedFactor >= 1f && s.RampTo > 0.01f && span > 1e-4f) s.AtSpeed = true;
            float speed = RampNow(s);
            if (speed <= 1e-4f && s.RampTo <= 1e-4f)
            {
                speed = 0f;
                s.Forward = 0;
                s.Strafe = 0;
                s.Walking = false;
            }
            return speed;
        }

        private static float Target(RideMode mode, int fwd, bool walking, in RideLimits l)
        {
            switch (mode)
            {
                case RideMode.Swimming: return fwd < 0 ? l.SwimBack : l.Swim;
                case RideMode.Flying: return fwd < 0 ? l.FlightBack : l.Flight;
                default: return fwd < 0 ? l.RunBack : walking ? l.Walk : l.Run;
            }
        }

        private static void Enter(ref RideState s, RideMode mode)
        {
            s.Mode = mode;
            s.Ungrounded = 0f;
            if (mode != RideMode.Airborne) s.FallTime = 0f;
            if (mode == RideMode.Ground || mode == RideMode.Swimming) s.Velocity.Y = 0f;
            if (mode != RideMode.Ground) s.Walking = false;
        }

        private static RideVec Planar(RideVec v) { return new RideVec(v.X, 0f, v.Z); }

        private static float Smooth(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

        private static float MoveTowards(float from, float to, float maxDelta)
        {
            if (Math.Abs(to - from) <= maxDelta) return to;
            return from + Math.Sign(to - from) * maxDelta;
        }

        private static float WrapDeg(float a)
        {
            if (float.IsNaN(a) || float.IsInfinity(a)) return 0f;
            a %= 360f;
            if (a < 0f) a += 360f;
            return a;
        }
    }
}
