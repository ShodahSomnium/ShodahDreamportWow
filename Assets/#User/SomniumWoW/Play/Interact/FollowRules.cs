// iteration 11 - owner interact: WoW's auto-follow rules (pure). The designer's compiled draft (P/harness/Probes/D11/interact/OfflineAndFollowDraft.cs),
// copied as is; the Game layer's WowFollow (Game/Play/Interact/WowFollow.cs) walks the avatar with it.
namespace SomniumSpace.Worlds.SomniumWoW.Play.Interact
{
    /// <summary>
    /// WoW's auto-follow (UnitPopup FOLLOW -> FollowUnit, SLASH_FOLLOW /f /follow /fol) for a Somnium avatar. In WoW it is CLIENT-side: the
    /// client walks its own character after the target and sends normal movement - nothing on the wire says "following". Here the Somnium
    /// avatar IS the WoW character, so following = the Game layer walking the avatar (WowFollow) while these rules say walk / hold / stop.
    /// Texts: AUTOFOLLOWSTART "Following %s." (GlobalStrings.lua:642), AUTOFOLLOWSTOP "You stop following %s." (:643), ERR_AUTOFOLLOW_TOO_FAR
    /// "Target is too far away." (:2943), ERR_INVALID_FOLLOW_TARGET "You can't follow that unit." (:3137), ERR_TOOBUSYTOFOLLOW "You're too busy
    /// to follow anything!" (:3535). Distances: UnitPopupButtons["FOLLOW"].dist = 4 (UnitPopup.lua:43) = CheckInteractDistance index 4,
    /// whose yards are C code - ours: 28 yd (INSPECT_DISTANCE, AzerothCore ObjectDefines.h:27, the same reach the other entries use).
    /// </summary>
    public static class FollowRules
    {
        public const float StartRange = 28f;      // yd, ours (see above)
        public const float BreakRange = 40f;      // yd, ours: farther (or not visible / another map) ends the follow
        public const float HoldDistance = 2.5f;   // yd, ours: stop walking this close behind the leader
        public const float ResumeDistance = 4f;   // yd, ours: walk again once farther than this (no stop-go jitter)
        public const float OwnMoveMetres = 0.06f; // m per frame the avatar moved that we did not move it: the player moved -> stop (ours)

        public enum Verdict : byte { Walk = 0, Hold = 1, Stop = 2 }

        /// <summary>Can following start? null = yes, else WoW's error text.</summary>
        public static string CanStart(bool targetIsPlayer, bool targetFriendly, bool targetIsSelf, float distanceYards, bool selfDead, bool selfBusy)
        {
            if (!targetIsPlayer || !targetFriendly || targetIsSelf) return "You can't follow that unit.";     // ERR_INVALID_FOLLOW_TARGET
            if (selfDead || selfBusy) return "You're too busy to follow anything!";                         // ERR_TOOBUSYTOFOLLOW (dead, on a taxi, held by a forced move)
            if (distanceYards > StartRange) return "Target is too far away.";                              // ERR_AUTOFOLLOW_TOO_FAR
            return null;
        }

        /// <summary>One frame of an active follow. walking = the avatar is being walked now (hysteresis between Hold and Resume).</summary>
        public static Verdict Next(float distanceYards, bool walking, bool leaderVisible, bool sameMap, bool selfMoved, bool selfDead, bool selfBusy)
        {
            if (!leaderVisible || !sameMap || selfMoved || selfDead || selfBusy || distanceYards > BreakRange) return Verdict.Stop;
            if (walking) return distanceYards > HoldDistance ? Verdict.Walk : Verdict.Hold;
            return distanceYards > ResumeDistance ? Verdict.Walk : Verdict.Hold;
        }

        /// <summary>How far to walk this frame (yards): the run speed, never past the hold distance.</summary>
        public static float StepYards(float distanceYards, float runSpeedYardsPerSecond, float dt)
        {
            float want = distanceYards - HoldDistance;
            if (want <= 0f || dt <= 0f) return 0f;
            float step = runSpeedYardsPerSecond * dt;
            return step < want ? step : want;
        }
    }
}
