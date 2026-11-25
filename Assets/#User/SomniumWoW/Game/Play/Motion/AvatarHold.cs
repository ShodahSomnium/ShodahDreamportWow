// iteration 11 - owner motion (proposal-motion.md 6.2.7): the standing takeover of the Somnium avatar for a forced move (charge,
// knockback, fear...) and a held transport arrival. Exactly the Bridge members the mounts' seat already ships (Mounts/MountSeat.cs
// TakeOver :142-157): motion collision / fly / teleport off, speed / gravity / jump 0, the falling animation off, VRIK legs off unless
// keepLegs, the avatar's CharacterController disabled (its radius read first) - without MountSeat's sitting pose, hands or head. The
// release keeps MountSeat.Release's no-drop order (:276-317): the root written on the spot with the controller still off, VRIK / falling
// animation / motion given back, the controller as it was, then the official teleport (DoTeleportToPoint).
using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Motion
{
    public sealed class AvatarHold
    {
        /// <summary>The log prefix.</summary>
        public string Tag = "Forced move";
        public Transform Root { get; private set; }
        public bool Taken { get; private set; }
        public bool KeepLegs { get; private set; }
        public bool CcFound { get; private set; }
        public float CcRadius { get; private set; } = 0.25f;
        public float CcHeight { get; private set; } = 1.82f;
        public Vector3 CcCenter { get; private set; }

        private CharacterController _cc;
        private bool _ccWas;
        private ISomniumPlayerMotion _motion;
        private ISomniumPlayerAnimation _animation;
        private ISomniumPlayerAvatar _avatar;

        /// <summary>Takes the avatar (root = WowPlayer.Root). keepLegs: VRIK's procedural steps stay on (fear / confuse: seconds of
        /// walking). False = nothing was taken.</summary>
        public bool TakeOver(Transform root, bool keepLegs, out string why)
        {
            why = null;
            if (root == null) { why = "no avatar"; return false; }
            Root = root;
            KeepLegs = keepLegs;
            ISomniumPlayer player = null;
            try { player = SomniumBridge.PlayersContainer?.LocalPlayer; } catch (Exception) { player = null; }
            try
            {
                _motion = player?.Features?.Motion;
                _animation = player?.Features?.Animation;
                _avatar = player?.Features?.Avatar;
            }
            catch (Exception e) { WowLog.Warn(Tag + ": reading the player's features threw " + e.Message); }
            _cc = root.GetComponentInParent<CharacterController>();
            if (_cc == null) _cc = root.GetComponentInChildren<CharacterController>();
            CcFound = _cc != null;
            if (_cc != null) { CcRadius = _cc.radius; CcHeight = _cc.height; CcCenter = _cc.center; }
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(true);
                    _motion.SetFlyModeDisableState(true);
                    _motion.SetTeleportationDisableState(true);
                    _motion.SetMovementSpeed(0f, 0f, 0f);
                    _motion.SetGravity(0f, 0f);
                    _motion.SetJump(0f, 0f);
                }
                catch (Exception e) { WowLog.Warn(Tag + ": motion settings threw " + e.Message); }
            }
            try { _animation?.SetFallingAnimationDisabled(true); } catch (Exception) { }
            if (!keepLegs) { try { _avatar?.SetVRIKLegMovementDisabled(true); } catch (Exception) { } }
            if (_cc != null) { _ccWas = _cc.enabled; _cc.enabled = false; }
            Taken = true;
            return true;
        }

        /// <summary>The held avatar this frame: desktop position + heading (never pitch / roll), VR position only (the player's own
        /// yaw is kept - comfort).</summary>
        public void Write(Vector3 feet, float yaw, bool vr)
        {
            if (Root == null) return;
            if (vr) Root.position = feet;
            else Root.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Gives the avatar back on spot (MountSeat.Release's order) and teleports it there officially.</summary>
        public void Release(Vector3 spot, float yaw, bool vr, out string how)
        {
            how = "none";
            if (!Taken) return;
            Taken = false;
            if (Root != null)
            {
                if (vr) yaw = Root.eulerAngles.y;
                Root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));   // no drop: the controller is still off
            }
            if (!KeepLegs) { try { _avatar?.SetVRIKLegMovementDisabled(false); } catch (Exception) { } }
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(false);
                    _motion.SetFlyModeDisableState(false);
                    _motion.SetTeleportationDisableState(false);
                    _motion.SetMovementSpeed(1f, 1f, 1f);
                    _motion.SetGravity(1f, 1f);
                    _motion.SetJump(1f, 1f);
                }
                catch (Exception e) { WowLog.Warn(Tag + ": restoring the motion settings threw " + e.Message); }
            }
            try { _animation?.SetFallingAnimationDisabled(false); } catch (Exception) { }
            if (_cc != null) _cc.enabled = _ccWas;
            if (Root != null && !WowPlayer.Teleport(spot, yaw, out how))
            {
                // No Bridge (editor / lab): move the controller the documented way (off, write, on).
                bool was = _cc != null && _cc.enabled;
                if (_cc != null) _cc.enabled = false;
                Root.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
                if (_cc != null) _cc.enabled = was;
                how = "direct write";
            }
            _cc = null;
            _motion = null; _animation = null; _avatar = null;
        }
    }
}
