// States fix 2026-10-04 (avatar state animations): our stand state follows the avatar's walking like the real client.
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        int _stStandUpFrom = -1;   // the kneel (8) / sleep (3) we already asked to leave, -1 none
        int _seatSt = -1, _seatHoldLogs;   // lead fix 2026-10-04: the stand state the seat spot belongs to
        WowPos _seatPos;

        /// <summary>Lead 2026-10-04 (IWowSession.StandUp): CMSG_STANDSTATECHANGE 0 (HandleStandStateChangeOpcode accepts STAND,
        /// MiscHandler.cpp:560-576; Unit::SetStandState removes the NOT_SEATED auras - the food / drink).</summary>
        public void StandUp(string why)
        {
            if (_world == null || _player.Dead || _player.StandState == 0) return;
            _world.Send(Op.CMSG_STANDSTATECHANGE, new PacketWriter().U32(0));
            WowLog.Info("Online: stand up (" + why + ") - CMSG_STANDSTATECHANGE 0");
        }

        /// <summary>
        /// Lead fix 2026-10-04 (the user: "in vr when eating or drinking it instatnly gets cancclled"): AzerothCore stands a seated player on
        /// ANY movement packet with a moving / turning flag - "if (plrMover->IsSitState() && (movementInfo.flags & (MOVEMENTFLAG_MASK_MOVING |
        /// MOVEMENTFLAG_MASK_TURNING))) plrMover->SetStandState(UNIT_STAND_STATE_STAND);" (MovementHandler.cpp:503-504) - and the food / drink
        /// aura goes with the seat (AURA_INTERRUPT_FLAG_NOT_SEATED). A VR avatar sways with the headset (and the client moved its root under
        /// the head when our sitting pose began), which read as walking (WowGame: avatar speed over 0.4 m/s) and sent MSG_MOVE_START_*.
        /// While seated with PlayerMotion.SeatedSway set, the START is held until the avatar is that far from where it sat (then the realm
        /// stands it up as before). True = hold it this frame.
        /// </summary>
        bool SeatedHold(WowPos pos, bool startingToMove)
        {
            int st = _player.StandState;
            if (st != _seatSt) { _seatSt = st; _seatPos = pos; }
            if (!startingToMove || _motion.SeatedSway <= 0f || st == 0 || st == 7 || _player.Dead) return false;
            float dx = pos.X - _seatPos.X, dy = pos.Y - _seatPos.Y;
            if (dx * dx + dy * dy >= _motion.SeatedSway * _motion.SeatedSway) return false;
            if (_seatHoldLogs++ < 4) WowLog.Info("Online: seated (stand state " + st + ") - the avatar sways " + System.Math.Sqrt(dx * dx + dy * dy).ToString("0.00") + " yd, under " + _motion.SeatedSway.ToString("0.00") + " yd: no move sent (the realm would stand us up, MovementHandler.cpp:503-504)");
            return true;
        }

        /// <summary>
        /// SendMove hook: the avatar walks while kneeling or sleeping -> CMSG_STANDSTATECHANGE 0, once per kneel / sleep. AzerothCore stands a
        /// player up on movement only from the SIT states ("if (plrMover->IsSitState() && (movementInfo.flags & (MOVEMENTFLAG_MASK_MOVING |
        /// MOVEMENTFLAG_MASK_TURNING))) plrMover->SetStandState(UNIT_STAND_STATE_STAND);" MovementHandler.cpp:502-503; IsSitState = sit +
        /// the chairs, Unit.cpp:12711-12718), so kneel / sleep would stay set while the avatar walks (its client pose with it). Ours: the
        /// stand-up for these two (HandleStandStateChangeOpcode accepts STAND, MiscHandler.cpp:560-576; the real client's own rule for
        /// them is not measured).
        /// </summary>
        void StStandUpOnMove(uint flags)
        {
            int st = _player.StandState;
            bool kneelOrSleep = st == 3 || st == 8;
            if (!kneelOrSleep) { _stStandUpFrom = -1; return; }
            if ((flags & MoveFlags.MaskMoving) == 0 || _stStandUpFrom == st || _world == null) return;
            _stStandUpFrom = st;
            _world.Send(Op.CMSG_STANDSTATECHANGE, new PacketWriter().U32(0));
            WowLog.Info("Online: the avatar walks while " + (st == 3 ? "sleeping" : "kneeling") + " - CMSG_STANDSTATECHANGE 0 (stand up; ours, the realm only does it for sitting)");
        }
    }
}
