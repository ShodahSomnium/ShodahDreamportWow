using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.MapApp;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// Lead (2026-09-27). The user: "when im grabbing the somnium tablet, it stops the spell wrist menu, it is hard to grab the
    /// menu without casting a spell". Reaching for the client's tablet with the right hand passed over the left-arm spell buttons
    /// (UiPointer pokes a button 2.5 cm in front of it after 0.06 s) and gripping the tablet opened that hand's half-radial spell
    /// menu (WowVrInput: grip = menu, release = cast). So, in VR:
    /// - the wrist/arm POKE pauses while the right grip is held (a grabbing hand never pokes), while a hand holds the tablet, and
    ///   while the right hand is at the open tablet;
    /// - a hand's grip spell menu does not open while that hand is at the open / held tablet;
    /// - the trigger ray does not target (or clear the target) through the tablet;
    /// each for Grace seconds after the condition ends (the hand coming back from the tablet does not poke or open on the way).
    /// The tablet's place comes from WowMapTabletLink (read-only). Without a linked tablet nothing changes.
    /// </summary>
    public static class VrTabletGuard
    {
        /// <summary>Ours: metres around the tablet's box that count as "at the tablet".</summary>
        public const float Margin = 0.12f;
        /// <summary>Ours: seconds the pause lasts after the hand left the tablet / the grip opened / the tablet was let go.</summary>
        public const float Grace = 0.5f;

        private static float _pokeUntil, _leftUntil, _rightUntil;
        private static bool _pokeSaid, _menuSaid, _raySaid;

        /// <summary>The right hand may poke our arm / wrist buttons now.</summary>
        public static bool PokeAllowed(Vector3 rightHand)
        {
            float now = Time.unscaledTime;
            bool block = GearXr.GripHeld(true) || WowMapTabletLink.TabletHeld || WowMapTabletLink.NearTablet(rightHand, Margin);
            if (block) _pokeUntil = now + Grace;
            bool allowed = now >= _pokeUntil;
            if (!allowed && !_pokeSaid && (WowMapTabletLink.TabletHeld || WowMapTabletLink.NearTablet(rightHand, Margin)))
            {
                _pokeSaid = true;
                WowLog.Info("VR input: right hand at the Somnium tablet - the arm / wrist spell buttons pause (and while the right grip is held), "
                    + Grace + " s after (said once).");
            }
            return allowed;
        }

        /// <summary>This hand's grip may open its spell menu now (false while the hand is at the open / held tablet, + Grace).</summary>
        public static bool MenuAllowed(bool right, Vector3 hand)
        {
            float now = Time.unscaledTime;
            // lead 2026-10-05 (the user: "Wrist menu is still appearing when I try to grab the tablet ... block it when the tablet laser is
            // showing when the tablet is open"): no spell menu on either hand while the Somnium tablet is open or held
            if (WowMapTabletLink.TabletIsOpen || WowMapTabletLink.NearTablet(hand, Margin))
            {
                if (right) _rightUntil = now + Grace; else _leftUntil = now + Grace;
            }
            bool allowed = now >= (right ? _rightUntil : _leftUntil);
            if (!allowed && !_menuSaid)
            {
                _menuSaid = true;
                WowLog.Info("VR input: the Somnium tablet is open / held or the grip is at it - no spell menu (said once).");
            }
            return allowed;
        }

        private static float _palmUntil;
        private static bool _palmSaid;

        /// <summary>Lead 2026-10-05 (the user: "make the wrist menu not activate when I am grabbing the tablet, it is super hard to spawn the
        /// Somnium tablet, and move it"): the palm menu may open now - not while the tablet is held, either hand is at the tablet, or the LEFT
        /// grip is held (spawning / grabbing it), + Grace after.</summary>
        public static bool PalmAllowed(Vector3 leftHand, Vector3 rightHand)
        {
            float now = Time.unscaledTime;
            bool block = GearXr.GripHeld(false) || WowMapTabletLink.TabletHeld || WowMapTabletLink.NearTablet(leftHand, Margin) || WowMapTabletLink.NearTablet(rightHand, Margin);
            if (block) _palmUntil = now + Grace;
            bool allowed = now >= _palmUntil;
            if (!allowed && !_palmSaid)
            {
                _palmSaid = true;
                WowLog.Info("VR input: the left grip is held or a hand is at the Somnium tablet - the palm menu stays closed (said once).");
            }
            return allowed;
        }

        /// <summary>The trigger ray may target along this ray (false when it meets the open / held tablet first).</summary>
        public static bool RayAllowed(Ray aim, float maxDistance)
        {
            if (!WowMapTabletLink.RayHitsTablet(aim, maxDistance)) return true;
            if (!_raySaid)
            {
                _raySaid = true;
                WowLog.Info("VR input: the trigger ray is on the Somnium tablet - no targeting through it (said once).");
            }
            return false;
        }
    }
}
