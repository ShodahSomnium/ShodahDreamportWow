// Lead 2026-10-04 (the user: "in vr when eating or drinking it instatnly gets cancclled, probably because it's not staying in sitting pose,
// for VR remove that, but add an immersion toggle, so people have to sit down irl to eat and drink haha!").
using SomniumSpace.Worlds.SomniumWoW.Game.Armour;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// "Immersive eating" (Settings, VR, off by default): food and drink only while the wearer sits down in REAL LIFE, and standing up
    /// while eating stops it - as standing up does in WoW (the food / drink aura has AURA_INTERRUPT_FLAG_NOT_SEATED).
    /// - Sitting = the headset's height above the avatar's root (the play area's floor) under SitShare x the standing height, held for
    ///   SettleSeconds (no flicker). The standing height = the avatar's own T-pose head height (ArmourFaceCard.TPoseHeadHeight - the
    ///   Somnium avatar's skeleton x its scale) + EyeOverHead, or the highest headset height seen this session when that is higher (a
    ///   wearer taller than the avatar), at most MaxOverAvatar x the avatar's. Without a skeleton: EyeFallback.
    /// - Refused when standing: WowConsumeGate (both sessions ask it just before a food / drink starts, so nothing is used up) - the UI
    ///   error RefuseText.
    /// - Standing up while eating / drinking for StandUpSeconds: IWowSession.StandUp (online CMSG_STANDSTATECHANGE 0) - once per meal.
    /// Values ours. The client pose for sitting / eating / drinking is not set in VR at all (GearStatePose): the wearer's own body sits.
    /// </summary>
    public sealed partial class GearLocal
    {
        public const float SitShare = 0.82f, SettleSeconds = 0.4f, StandUpSeconds = 0.6f, EyeOverHead = 0.08f, EyeFallback = 1.6f, MaxOverAvatar = 1.2f;
        public const string RefuseText = "Sit down (in real life) to eat or drink.";

        /// <summary>VR: the wearer sits in real life now (see the class summary); false on desktop.</summary>
        public static bool SittingInRealLife { get; private set; }
        private static bool _eatVr, _eatLive;
        private float _eatTallest, _eatFlipSince = -1f, _eatStandSince = -1f;
        private bool _eatSitting, _eatStoodUp, _eatSaid;
        private GearBodyState _eatState;

        /// <summary>WowConsumeGate.SitRule: null = allowed.</summary>
        private static string SitRule()
        {
            if (!WowSettings.ImmersiveEating || !_eatVr) return null;
            return SittingInRealLife ? null : RefuseText;
        }

        /// <summary>Once per frame after the state pose (GearLocal.Tick).</summary>
        private void StepEating(IWowSession s, PlayerState p)
        {
            WowConsumeGate.SitRule = SitRule;
            if (!_eatLive) { _eatLive = true; WowSettings.SetLive("immersive_eating"); }   // the Settings row (VR only) - the feature works
            _eatVr = _xr;
            if (!_xr || p == null) { SittingInRealLife = false; _eatSitting = false; _eatFlipSince = -1f; return; }
            float now = Time.unscaledTime;
            float eye = Body.HeadPos.y - Body.Feet.y;
            float avatar = -1f;
            Animator a = Body.Animator;
            Transform head = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            float h;
            if (head != null && ArmourFaceCard.TPoseHeadHeight(a.avatar, head, out h)) avatar = h + EyeOverHead;
            bool owned = WowMount.OwnsAvatar || WowFlight.Flying || WowVehicle.OwnsAvatar || WowForcedMove.OwnsAvatar || p.OnTaxi || p.Dead;
            if (!owned && eye > _eatTallest) _eatTallest = avatar > 0f ? Mathf.Min(eye, avatar * MaxOverAvatar) : Mathf.Min(eye, 2.2f);
            float standing = Mathf.Max(avatar > 0f ? avatar : EyeFallback, _eatTallest);
            bool sitting = eye < SitShare * standing;
            if (sitting != _eatSitting)
            {
                if (_eatFlipSince < 0f) _eatFlipSince = now;
                else if (now - _eatFlipSince >= SettleSeconds) { _eatSitting = sitting; _eatFlipSince = -1f; }
            }
            else _eatFlipSince = -1f;
            SittingInRealLife = _eatSitting;
            if (WowSettings.ImmersiveEating && !_eatSaid)
            {
                _eatSaid = true;
                WowLog.Info("Gear: immersive eating on (VR) - standing height " + standing.ToString("0.00") + " m (avatar " + (avatar > 0f ? avatar.ToString("0.00") + " m" : "unknown") +
                            ", tallest seen " + _eatTallest.ToString("0.00") + " m), sitting below " + (SitShare * standing).ToString("0.00") + " m; now " + eye.ToString("0.00") + " m ("
                            + (_eatSitting ? "sitting" : "standing") + ")");
            }
            // standing up in real life while eating / drinking: stand up in WoW (once per meal)
            GearBodyState st = StatePose.State;
            bool meal = st == GearBodyState.Eat || st == GearBodyState.Drink;
            if (st != _eatState) { _eatState = st; _eatStoodUp = false; _eatStandSince = -1f; }
            if (!WowSettings.ImmersiveEating || !meal || _eatSitting || _eatStoodUp) { _eatStandSince = -1f; return; }
            if (_eatStandSince < 0f) { _eatStandSince = now; return; }
            if (now - _eatStandSince < StandUpSeconds) return;
            _eatStoodUp = true;
            WowLog.Info("Gear: immersive eating - the wearer stood up in real life (head " + eye.ToString("0.00") + " m of " + standing.ToString("0.00") + " m) - standing up in WoW too");
            try { s.StandUp("stood up in real life (Immersive eating)"); }
            catch (System.Exception e) { WowLog.Warn("Gear: immersive eating - StandUp threw " + e.Message); }
        }
    }
}
