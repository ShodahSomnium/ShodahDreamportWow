using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Riding
{
    // Iteration 7 (lead contract PLAN7 s3.3; owner Body fills Pick; Remote and Ride call it).
    /// <summary>What a mount model can play: the authored MoveSpeed (yd/s, 0 = not authored) of its Walk (4) and Run (5)
    /// sequences, its display scale (CreatureDisplayInfo x CreatureModelData), and which of the optional sequences exist.</summary>
    public struct RideGaitModel { public float WalkMoveSpeed, RunMoveSpeed, Scale; public bool HasRun, HasFly, HasHover, HasJump, HasJumpEnd, HasJumpLandRun, HasFall; }

    public struct RideGaitPick { public int AnimId; public float Rate; public bool Loop; }

    /// <summary>
    /// The mount's gait (PLAN7 s4.3), pure and allocation-free. Rules:
    /// ground - speed below 0.05 m/s = Stand; forward below 0.45 x the rider's run speed = Walk, else Run (+-10 % hysteresis on
    /// the sequence playing now; Walk when the model has no Run); backwards = WalkBackwards. Rate = |speed| / (the sequence's
    /// MoveSpeed yd/s x 0.9144 x display scale), clamped 0.5-2 (the WowEntityView.AnimateMount precedent); a MoveSpeed of 0
    /// counts as 9.0 yd/s for Run (the most common authored value, 103 of 284 mount displays) and 2.5 yd/s for Walk (200 of 284;
    /// P/harness/Probes/Mount7/data/mount_displays.csv census) - ours.
    /// airborne - the jump frame starts JumpStart (once); while it plays it stays; then Jump (loop), or Fall once the mount has
    /// fallen for 0.6 s (read as the downward speed 0.6 s of AzerothCore's gravity gives: 10.58 m/s - Pick has no clock).
    /// landing - JumpLandRun once when moving and the model has it; JumpEnd once when standing (or moving without JumpLandRun
    /// ... then the ground gait at once, so a galloping mount never skids through a standing landing); while it plays it stays.
    /// swimming - Swim / SwimIdle / SwimBackwards. flying - forward Fly 135 (the animator's AnimationData fallback plays Swim 42 on
    /// models without it), still Hover 193 when present else SwimIdle 41, back SwimBackwards 45, turning in place SwimLeft /
    /// SwimRight. Swim, fly, hover and jump animations play at rate 1 (their authored speeds are not in RideGaitModel).
    /// playing = the AnimationData id the model plays NOW (M2Animator.PlayingAnimationId, or the last pick's id): the walk/run
    /// hysteresis and the one-shot continuation read it. Every "ours" value is logged once on the first call.
    /// </summary>
    public static class RideGait
    {
        public const int Stand = 0, Walk = 4, Run = 5, WalkBackwards = 13, JumpStart = 37, Jump = 38, JumpEnd = 39, Fall = 40,
            SwimIdle = 41, Swim = 42, SwimLeft = 43, SwimRight = 44, SwimBackwards = 45, Fly = 135, JumpLandRun = 187, Hover = 193;

        // ---- values of the rule (PLAN7 s4.3 / s4.8); "ours" = not WoW's, logged once
        private const float YardsToMetres = 0.9144f;
        private const float StandBelowMps = 0.05f;          // ours (PLAN7 s4.3): below this planar speed the mount stands
        private const float WalkRunFraction = 0.45f;        // ours (PLAN7 s4.3): walk below 0.45 x the rider's run speed
        private const float Hysteresis = 0.10f;             // ours (PLAN7 s4.3): +-10 % around the walk/run threshold
        private const float RateMin = 0.5f, RateMax = 2f;   // the WowEntityView.AnimateMount clamp (WowEntityView.cs 642-657)
        private const float RunMoveSpeedFallback = 9.0f;    // ours: yd/s when Run's MoveSpeed is 0 (27 displays, e.g. the wolf)
        private const float WalkMoveSpeedFallback = 2.5f;   // ours: yd/s when Walk's MoveSpeed is 0 (31 displays)
        private const float BaseRunYards = 7f;              // AC playerBaseMoveSpeed MOVE_RUN (when the caller gives no run speed)
        private const float FallAfterSeconds = 0.6f;        // ours (PLAN7 s4.3): Fall after 0.6 s of falling
        private const float GravityYards = 19.29110527038574f;   // AC Movement/Spline/MovementUtil.cpp `double gravity`
        private const float FallBelowMps = FallAfterSeconds * GravityYards * YardsToMetres;   // 10.58 m/s downwards
        private const float TurnInPlaceDegPerSec = 30f;     // ours: a flyer turning faster than this in place plays SwimLeft/Right

        private static bool _logged;

        /// <summary>forwardMps along the heading (+/-; the planar speed when strafing), turnDegPerSec (+ = right, Unity yaw
        /// increasing), verticalMps (+ up); playing = the anim now (hysteresis); runSpeedMps = the rider's run speed.</summary>
        public static RideGaitPick Pick(RideMode mode, float forwardMps, float turnDegPerSec, float verticalMps, float runSpeedMps,
                                        bool jumpedThisFrame, bool landedThisFrame, in RideGaitModel m, int playing)
        {
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("RideGait: ours - stand below " + StandBelowMps + " m/s, walk below " + WalkRunFraction + " x run speed +-"
                    + (Hysteresis * 100f) + " %, rate clamp " + RateMin + "-" + RateMax + ", MoveSpeed 0 -> run " + RunMoveSpeedFallback
                    + " / walk " + WalkMoveSpeedFallback + " yd/s, Fall after " + FallAfterSeconds + " s (" + FallBelowMps.ToString("0.00")
                    + " m/s down), flyer turn-in-place above " + TurnInPlaceDegPerSec + " deg/s; swim/fly/jump anims at rate 1");
            }
            RideGaitPick p;
            p.Rate = 1f;
            p.Loop = true;
            float speed = Math.Abs(forwardMps);
            bool moving = speed >= StandBelowMps;

            switch (mode)
            {
                case RideMode.Airborne:
                    if (jumpedThisFrame || playing == JumpStart) { p.AnimId = JumpStart; p.Loop = false; return p; }
                    if (m.HasFall && verticalMps < -FallBelowMps) { p.AnimId = Fall; return p; }
                    p.AnimId = m.HasJump || !m.HasFall ? Jump : Fall;
                    return p;

                case RideMode.Swimming:
                    p.AnimId = forwardMps <= -StandBelowMps ? SwimBackwards : moving ? Swim : SwimIdle;
                    return p;

                case RideMode.Flying:
                    if (forwardMps >= StandBelowMps) p.AnimId = Fly;
                    else if (forwardMps <= -StandBelowMps) p.AnimId = SwimBackwards;
                    else if (turnDegPerSec > TurnInPlaceDegPerSec) p.AnimId = SwimRight;
                    else if (turnDegPerSec < -TurnInPlaceDegPerSec) p.AnimId = SwimLeft;
                    else p.AnimId = m.HasHover ? Hover : SwimIdle;
                    return p;
            }

            // ---- ground (and the landing frame, whatever mode the caller reports for it)
            if (landedThisFrame)
            {
                if (moving && forwardMps > 0f && m.HasJumpLandRun) { p.AnimId = JumpLandRun; p.Loop = false; return p; }
                if (!moving && m.HasJumpEnd) { p.AnimId = JumpEnd; p.Loop = false; return p; }
            }
            else
            {
                if (playing == JumpEnd && !moving) { p.AnimId = JumpEnd; p.Loop = false; return p; }
                if (playing == JumpLandRun && moving && forwardMps > 0f) { p.AnimId = JumpLandRun; p.Loop = false; return p; }
            }
            if (!moving) { p.AnimId = Stand; return p; }
            float scale = m.Scale > 0f ? m.Scale : 1f;
            if (forwardMps < 0f)
            {
                p.AnimId = WalkBackwards;
                p.Rate = RateOf(speed, m.WalkMoveSpeed, WalkMoveSpeedFallback, scale);
                return p;
            }
            float run = runSpeedMps > 0f ? runSpeedMps : BaseRunYards * YardsToMetres;
            float threshold = WalkRunFraction * run;
            bool wasRunning = playing == Run || playing == JumpLandRun;
            bool running = m.HasRun && speed >= threshold * (wasRunning ? 1f - Hysteresis : 1f + Hysteresis);
            if (running) { p.AnimId = Run; p.Rate = RateOf(speed, m.RunMoveSpeed, RunMoveSpeedFallback, scale); }
            else { p.AnimId = Walk; p.Rate = RateOf(speed, m.WalkMoveSpeed, WalkMoveSpeedFallback, scale); }
            return p;
        }

        private static float RateOf(float speedMps, float authoredYards, float fallbackYards, float scale)
        {
            float a = Math.Abs(authoredYards);
            if (a < 0.01f) a = fallbackYards;
            float r = speedMps / (a * YardsToMetres * scale);
            return r < RateMin ? RateMin : r > RateMax ? RateMax : r;
        }
    }
}
