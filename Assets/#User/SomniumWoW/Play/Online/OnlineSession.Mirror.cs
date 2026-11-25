// Lead 2026-10-26 (user: "implement swimming logic just like in wow"): the server's mirror timers - breath under water, fatigue far out
// at sea (AC Player::HandleDrowning). Packets (AC WorldPackets::Misc, MiscPackets.cpp:101-126):
//   SMSG_START_MIRROR_TIMER: uint32 timer, uint32 value (ms), uint32 max (ms), int32 scale, uint8 paused, uint32 spell
//   SMSG_PAUSE_MIRROR_TIMER: uint32 timer, uint8 paused      SMSG_STOP_MIRROR_TIMER: uint32 timer
// The bar itself is UiMirrorTimers (MirrorTimer.xml); the drowning damage is the server's.
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        int _mirrorLogs;

        void MirrorInit()
        {
            IxHandle(Op.SMSG_START_MIRROR_TIMER, MirrorOnStart);
            IxHandle(Op.SMSG_PAUSE_MIRROR_TIMER, MirrorOnPause);
            IxHandle(Op.SMSG_STOP_MIRROR_TIMER, MirrorOnStop);
        }

        static string MirrorName(int t) { return t == 0 ? "Fatigue" : t == 1 ? "Breath" : "feign death"; }

        void MirrorOnStart(WowPacket p)
        {
            int t = (int)p.U32();
            uint value = p.U32(), max = p.U32();
            int scale = p.I32();
            bool paused = p.U8() != 0;
            if (t < 0 || t >= _player.Mirror.Length) return;
            var m = _player.Mirror[t];
            m.Active = true;
            m.Value = value / 1000f;
            m.Max = max / 1000f;
            m.Scale = scale;
            m.Paused = paused;
            if (_mirrorLogs++ < 20) WowLog.Info("Online: SMSG_START_MIRROR_TIMER " + MirrorName(t) + " " + m.Value.ToString("0.0") + " / " + m.Max.ToString("0.0") + " s, scale " + scale + (paused ? ", paused" : ""));
        }

        void MirrorOnPause(WowPacket p)
        {
            int t = (int)p.U32();
            bool paused = p.U8() != 0;
            if (t >= 0 && t < _player.Mirror.Length) _player.Mirror[t].Paused = paused;
        }

        void MirrorOnStop(WowPacket p)
        {
            int t = (int)p.U32();
            if (t < 0 || t >= _player.Mirror.Length) return;
            _player.Mirror[t].Active = false;
            if (_mirrorLogs++ < 20) WowLog.Info("Online: SMSG_STOP_MIRROR_TIMER " + MirrorName(t));
        }

        void MirrorTick(float dt)
        {
            for (int i = 0; i < _player.Mirror.Length; i++)
            {
                var m = _player.Mirror[i];
                if (!m.Active || m.Paused) continue;
                m.Value += m.Scale * dt;
                if (m.Value < 0f) m.Value = 0f;
                if (m.Value > m.Max) m.Value = m.Max;
            }
        }
    }
}
