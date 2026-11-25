using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Iteration 7 (owner Remote): the RideGaitModel of a mount model that is NOT the local rider's (a remote Somnium rider's mount
    /// drawn by GearRemote, an online unit's mount drawn by WowEntityView), read from its own sequences - the same inputs
    /// RideGait.Pick gets for the local mount (MoveSpeed yd/s of Walk 4 / Run 5, which ids the model has, the display scale).
    /// Plus the gait playback the two viewers share: CrossFade only on a change, one-shots held at their last frame, Speed every frame.
    /// </summary>
    public static class GearRideGait
    {
        /// <summary>The gait model of this mount (scale = the root's localScale: WowCreatures puts the display scale there).</summary>
        public static RideGaitModel Of(M2Animator anim, Transform mountRoot)
        {
            RideGaitModel m = default(RideGaitModel);
            m.Scale = mountRoot != null && mountRoot.localScale.x > 0f ? mountRoot.localScale.x : 1f;
            if (anim == null || anim.Model == null) return m;
            M2Model model = anim.Model;
            m.WalkMoveSpeed = MoveSpeed(model, RideGait.Walk);
            m.RunMoveSpeed = MoveSpeed(model, RideGait.Run);
            m.HasRun = anim.Has(RideGait.Run);
            m.HasFly = anim.Has(RideGait.Fly);
            m.HasHover = anim.Has(RideGait.Hover);
            m.HasJump = anim.Has(RideGait.Jump);
            m.HasJumpEnd = anim.Has(RideGait.JumpEnd);
            m.HasJumpLandRun = anim.Has(RideGait.JumpLandRun);
            m.HasFall = anim.Has(RideGait.Fall);
            return m;
        }

        private static float MoveSpeed(M2Model model, int animId)
        {
            int seq = model.FindSequence(animId);
            return seq >= 0 && seq < model.Sequences.Length ? model.Sequences[seq].MoveSpeed : 0f;
        }

        /// <summary>The one-shot gaits (played once and held at their last frame until the next gait arrives).</summary>
        public static bool OneShot(int animId)
        {
            return animId == RideGait.JumpStart || animId == RideGait.JumpEnd || animId == RideGait.JumpLandRun;
        }

        /// <summary>Plays animId (CrossFade with the new sequence's own blend time) when it differs from playing; the rate every frame.
        /// Returns the id now playing (as asked - the animator may play an AnimationData fallback of it).</summary>
        public static int Apply(M2Animator anim, int playing, int animId, float rate)
        {
            if (anim == null) return playing;
            if (animId != playing)
            {
                bool loop = !OneShot(animId);
                anim.CrossFade(animId, loop, -1);
                anim.HoldAtEnd = !loop;
                playing = animId;
            }
            float r = rate > 0.01f ? rate : 1f;
            if (!Mathf.Approximately(anim.Speed, r)) anim.Speed = r;
            return playing;
        }
    }
}
