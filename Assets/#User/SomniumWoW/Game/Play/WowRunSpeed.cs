using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Lead (2026-09-27, the user: "make somnium players run the same speed as normal wow players"). On foot in the world the
    /// Somnium avatar moves at the character's WoW run speed (PlayerState.RunSpeed, yd/s - 7 = WoW's base run; online the server's
    /// value, so Sprint, slows and ghost speed follow it), through the proven Bridge member ISomniumPlayerMotion.SetMovementSpeed
    /// (nonXR, locomotion, nonXRSprint) = multipliers clamped 0..10 (BridgePlayerMotion, measured in P/reports7/scout-riding.md).
    /// The client's base speeds (its own settings, read from the client source):
    ///   desktop walk 1.6 m/s, desktop Shift sprint 6 m/s (Player/Configs/Additional/NonXR.Movement.Settings.asset HorizontalSpeed /
    ///   SprintSpeed), VR stick 5 m/s forward, sideways and back (Player.XR.Controls.Movement.prefab ForwardSpeed /
    ///   HorizontalAndBackwardSpeed; LocomotionSpeedController.moveSpeed = standard * run * fly * multiplier).
    /// So walk and sprint both become the WoW run speed on desktop; in VR the stick is the WoW run speed. The client's VR stamina run
    /// (RunningController, x1.5 on the stick click) is not reachable from a world and still adds on top - README Known gaps.
    /// A mount ride / flight (WowMount.OwnsAvatar, WowFlight.Flying) owns the avatar's speed: this steps aside and applies again
    /// the frame after it gives the avatar back (the ride restores 1,1,1). Leaving the world restores the client's own speed.
    /// </summary>
    /// Lead (2026-10-03, the user: "In Somnium you walk by default and then sprint when you hold down shift ... So we walk normally, with
    /// the normal walking animation in both wow client and somnium. Hold shift and we move normally like in wow ... Only do this for
    /// Non-XR players"): on DESKTOP the client's plain walk (1.6 m/s) becomes WoW's WALK speed (PlayerState.WalkSpeed, 2.5 yd/s base) and
    /// its Shift sprint (6 m/s) WoW's RUN speed; <see cref="DesktopWalking"/> tells the session to send MOVEMENTFLAG_WALKING (+ the real
    /// client's MSG_MOVE_SET_WALK_MODE / SET_RUN_MODE on a change) so WoW clients show the walk animation. VR is unchanged (the stick runs).
    public sealed class WowRunSpeed : MonoBehaviour
    {
        /// <summary>Desktop, on foot, Shift not held: the character walks (WoW's walk mode). False in VR, on a mount, a flight or a vehicle.</summary>
        public static bool DesktopWalking { get; private set; }
        private float _lastWalk = -1f;
        private bool _loggedWalkRule;
        private bool _inputBroken;

        private bool ShiftHeld()
        {
            if (_inputBroken) return false;
            try { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); }
            catch (Exception) { _inputBroken = true; return false; }
        }

        public const float DesktopWalk = 1.6f, DesktopSprint = 6f, VrStick = 5f;   // m/s, the client's own settings (see above)
        private const float YardsToMetres = 0.9144f;
        private const float RefreshSeconds = 2f;   // ours: re-applied this often while on foot (something else may have set it)

        private bool _applied, _loggedFirst, _frozen;
        private int _frozenLogs;
        private float _lastRun = -1f, _nextRefresh;

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            DesktopWalking = s != null && s.Player != null && !WowPlayer.XrPresent && !WowMount.OwnsAvatar && !WowFlight.Flying && !WowSwim.Active && !ShiftHeld();
            if (s == null || s.Player == null)
            {
                Restore("no game running");
                return;
            }
            if (WowMount.OwnsAvatar || WowFlight.Flying)
            {
                _applied = false;   // the ride / flight owns the speed now; applied again after it gives the avatar back
                _frozen = false;
                return;
            }
            // Stage S1 (integrator, reports10/chat.md request 4): while the desktop WoW chat edit box has the keyboard, the client's
            // own W A S D (Input System actions a world cannot switch off) would walk the avatar with every letter typed. As in WoW,
            // typing does not move you: the on-foot speed multipliers are 0 while UiChat.Typing (NonXRMovementProvider.GetMovingVector
            // multiplies the horizontal speed by them - 0 = no step; turning, jump and gravity are not touched), back at once after.
            // Iteration 11 (motion hook 6): the same freeze while WowForcedMove.FeetLocked (rooted / stunned / feared / a forced move).
            bool typing = SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiChat.Typing;
            if (typing || WowForcedMove.FeetLocked)
            {
                if (!_frozen && Freeze())
                {
                    _frozen = true;
                    _applied = true;   // Restore gives the client's own speed back if the game ends while typing
                    if (_frozenLogs < 3)
                    {
                        _frozenLogs++;
                        WowLog.Info(typing ? "Speed: the WoW chat edit box is open - the avatar does not walk while typing (on-foot speed 0; logged 3 times)."
                            : "Speed: rooted / stunned / feared / a forced move - the avatar does not walk (on-foot speed 0; logged 3 times).");
                    }
                }
                return;
            }
            if (_frozen) { _frozen = false; _applied = false; }   // re-applied just below (same run speed: no log line)
            float runYd = s.Player.RunSpeed > 0.1f ? s.Player.RunSpeed : 7f;
            float walkYd = s.Player.WalkSpeed > 0.1f ? s.Player.WalkSpeed : 2.5f;   // AC playerBaseMoveSpeed MOVE_WALK 2.5 (online: the server's value)
            if (WowSwim.Active)   // lead 2026-10-26: in the water every step is the swim speed (AC Unit::GetSpeed MOVE_SWIM; walk mode does not apply)
            {
                runYd = s.Player.SwimSpeed > 0.1f ? s.Player.SwimSpeed : 4.722222f;
                walkYd = runYd;
            }
            float now = Time.unscaledTime;
            if (_applied && Mathf.Abs(runYd - _lastRun) < 0.01f && Mathf.Abs(walkYd - _lastWalk) < 0.01f && now < _nextRefresh) return;
            if (Apply(runYd, walkYd))
            {
                bool changed = Mathf.Abs(runYd - _lastRun) >= 0.01f || Mathf.Abs(walkYd - _lastWalk) >= 0.01f;
                _applied = true;
                _lastRun = runYd;
                _lastWalk = walkYd;
                if (!_loggedWalkRule)
                {
                    _loggedWalkRule = true;
                    WowLog.Info("Speed: desktop walks by default at the WoW walk speed " + walkYd.ToString("0.##") + " yd/s = " + (walkYd * YardsToMetres).ToString("0.00")
                        + " m/s (the client's walk x" + (walkYd * YardsToMetres / DesktopWalk).ToString("0.00") + "); Shift = the WoW run speed (the client's sprint); WoW clients see walking (MOVEMENTFLAG_WALKING). VR: the stick runs, as before.");
                }
                _nextRefresh = now + RefreshSeconds;
                if (!_loggedFirst || changed)
                {
                    _loggedFirst = true;
                    float mps = runYd * YardsToMetres;
                    WowLog.Info("Speed: on foot at the WoW run speed " + runYd.ToString("0.##") + " yd/s = " + mps.ToString("0.00") + " m/s (desktop walk x"
                        + (mps / DesktopWalk).ToString("0.00") + ", sprint x" + (mps / DesktopSprint).ToString("0.00") + ", VR stick x"
                        + (mps / VrStick).ToString("0.00") + "; the client's VR stamina run still adds x1.5).");
                }
            }
        }

        private void OnDisable()
        {
            Restore("the game component stopped");
        }

        private static ISomniumPlayerMotion Motion()
        {
            try
            {
                ISomniumPlayer player = SomniumBridge.PlayersContainer?.LocalPlayer;
                return player?.Features?.Motion;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Apply(float runYd, float walkYd)
        {
            ISomniumPlayerMotion m = Motion();
            if (m == null) return false;
            float mps = runYd * YardsToMetres, walkMps = walkYd * YardsToMetres;
            try
            {
                // lead 2026-10-03: the desktop walk = WoW's walk speed, the desktop sprint (Shift) = WoW's run speed, the VR stick = WoW's run speed
                m.SetMovementSpeed(Mathf.Clamp(walkMps / DesktopWalk, 0f, 10f), Mathf.Clamp(mps / VrStick, 0f, 10f), Mathf.Clamp(mps / DesktopSprint, 0f, 10f));
                return true;
            }
            catch (Exception e)
            {
                WowLog.Warn("Speed: SetMovementSpeed threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Stage S1: no walking while the desktop chat edit box is open (all three multipliers 0, clamped 0..10 by the Bridge).</summary>
        private static bool Freeze()
        {
            ISomniumPlayerMotion m = Motion();
            if (m == null) return false;
            try
            {
                m.SetMovementSpeed(0f, 0f, 0f);
                return true;
            }
            catch (Exception e)
            {
                WowLog.Warn("Speed: SetMovementSpeed (typing) threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private void Restore(string why)
        {
            _frozen = false;
            if (!_applied) return;
            _applied = false;
            _lastRun = -1f;
            if (WowMount.OwnsAvatar || WowFlight.Flying) return;   // the ride gives its own values back
            ISomniumPlayerMotion m = Motion();
            if (m == null) return;
            try { m.SetMovementSpeed(1f, 1f, 1f); } catch (Exception) { }
            WowLog.Info("Speed: the client's own movement speed restored (" + why + ").");
        }
    }
}
