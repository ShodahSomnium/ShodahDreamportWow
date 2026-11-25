// States fix 2026-10-04 (avatar state animations): the offline player's stand state - eating / drinking sits, /sit /kneel /sleep.
namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        private int _stEmoteStand;      // 1 sit, 3 sleep, 8 kneel from a text emote (WowEmoteTable.StandStateOf), 0 standing
        private WowPos _stStandPos;     // where it began
        private bool _stSaid;

        /// <summary>A text emote with a stand state (SIT 1, SLEEP 3, KNEEL 8 - EmoteSpecProc 1, measured in WowEmoteTable). Ours: offline keeps
        /// it until the player walks (CastMoveTolerance), fights, casts or dies - AzerothCore stands a sitting player up on movement
        /// (MovementHandler.cpp:502-503) and on a cast that is not seated-only (Spell.cpp:3745).</summary>
        private void StSetEmoteStand(int stand)
        {
            if (_player.Dead || stand < 0) return;
            _stEmoteStand = stand == 1 || stand == 3 || stand == 8 ? stand : 0;
            _stStandPos = _me.Pos;
            if (!_stSaid)
            {
                _stSaid = true;
                WowLog.Info("Offline: stand state " + stand + " from a text emote (ours: kept until the player walks, fights, casts or dies; eating / drinking sits too) - logged once");
            }
        }

        /// <summary>Lead 2026-10-04 (IWowSession.StandUp): the food / drink stops, an emote's sit / kneel / sleep ends.</summary>
        public void StandUp(string why)
        {
            if (_player.Dead || (_eating == null && _stEmoteStand == 0)) return;
            CancelConsumable(null);
            _stEmoteStand = 0;
            WowLog.Info("Offline: stand up (" + why + ")");
        }

        /// <summary>Per Step after TickMotion: PlayerState.StandState / Consuming (1 eat, 2 drink - the offline food aura has no spell id).
        /// Eating / drinking = sitting (AzerothCore Unit.cpp:4562-4564 "Sitdown on apply aura req seated").</summary>
        partial void TickStates(float dt)
        {
            var p = _player;
            if (p.Dead || _inCombat || _casting != null || (_stEmoteStand != 0 && Dist(_me.Pos, _stStandPos) > CastMoveTolerance)) _stEmoteStand = 0;
            int consuming = _eating == null ? 0 : _eating.HealAmount > 0 ? 1 : 2;
            p.Consuming = p.Dead ? 0 : consuming;
            p.StandState = p.Dead ? 0 : consuming != 0 ? 1 : _stEmoteStand;
            // lying dead is rooted until Release Spirit like the realm: Player::KillPlayer "SendMoveRoot(true);" (Player.cpp:4643),
            // Player::BuildPlayerRepop "if (!IsImmobilizedState()) SendMoveRoot(false);" - the lying pose must not slide (WowForcedMove locks the feet)
            if (p.Dead && !p.Ghost) p.Rooted = true;
        }
    }
}
