// Lead 2026-11-03 (user: "can we sync the time with server side please? Just like how WoW does it"): the realm's clock.
// AzerothCore sends it once at login (Player::SendInitialPacketsBeforeAddToMap, Player.cpp:11808-11812):
//   SMSG_LOGIN_SETTIMESPEED: uint32 packed time (ByteBuffer::AppendPackedTime: (year-100) << 24 | month << 20 | (day-1) << 14 |
//   weekday << 11 | hour << 6 | minute - the server's LOCAL time), float game speed (0.01666667 game minutes per real second,
//   i.e. real time), uint32 0. The client then runs its day / night from it; so does PlayerState.GameTimeHours here.
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        double _clockMinutes = -1;   // game minutes since midnight at _clockAt
        double _clockAt;
        float _clockSpeed = 1f / 60f;

        void ClockInit()
        {
            IxHandle(Op.SMSG_LOGIN_SETTIMESPEED, ClockOnTimeSpeed);
        }

        void ClockOnTimeSpeed(WowPacket p)
        {
            uint packed = p.U32();
            float speed = p.Remaining >= 4 ? p.F32() : 1f / 60f;
            int minute = (int)(packed & 0x3F), hour = (int)((packed >> 6) & 0x1F);
            if (minute > 59 || hour > 23) { WowLog.Warn("Online: SMSG_LOGIN_SETTIMESPEED with an odd time 0x" + packed.ToString("X8") + " - the clock is not set"); return; }
            _clockMinutes = hour * 60 + minute;
            _clockAt = _now;
            _clockSpeed = speed > 0f && speed < 100f ? speed : 1f / 60f;
            _player.GameTimeHours = (float)(_clockMinutes / 60.0);
            WowLog.Info("Online: the realm's time is " + hour.ToString("00") + ":" + minute.ToString("00") + " (SMSG_LOGIN_SETTIMESPEED, speed " +
                        _clockSpeed.ToString("0.########") + " game min / s) - the world's day and night follow it");
        }

        void ClockTick()
        {
            if (_clockMinutes < 0) return;
            double minutes = _clockMinutes + (_now - _clockAt) * _clockSpeed;   // speed = game minutes per real second (1/60 = real time)
            minutes %= 1440.0;
            if (minutes < 0) minutes += 1440.0;
            _player.GameTimeHours = (float)(minutes / 60.0);
        }
    }
}
