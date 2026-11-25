using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Access
{
    /// <summary>
    /// Ticks the remote-player list and its isolation every frame (the Rec Room Dreamport's RrVisitorDriver). WowGame adds
    /// it after the verdict (Core/WowAccess):
    /// - For a visitor who is not on the whitelist it is the ONLY thing that runs, in place of the whole Dreamport; its
    ///   rule is "nobody is with me", so every other player is hidden and silent on the visitor's screen.
    /// - For a whitelisted player it runs beside the Dreamport; its rule is "the player has a WoW network record"
    ///   (WowGear.HasRecord), so visitors (who never claim a slot) are hidden and silent, and testers are left to Gear.
    /// LateUpdate at order 9990: after the client's Update work, and BEFORE WowGear's LateUpdate (order 10000), so a player
    /// whose record appears is restored by the isolation in the same frame, before Gear's GearHider looks at the renderers
    /// (both use Renderer.forceRenderingOff; see WowIsolation "Hand-over to Gear"). The isolation restores everyone when
    /// this component or the world goes away (idempotent).
    /// </summary>
    [DefaultExecutionOrder(9990)]
    public sealed class WowIsolationDriver : MonoBehaviour
    {
        private int _errors;

        private void LateUpdate()
        {
            try
            {
                WowRemotes.Tick();
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3)
                {
                    WowLog.Warn("Access: hiding other players failed (" + _errors + "): " + e.Message);
                }
            }
        }

        private void OnDestroy()
        {
            WowRemotes.Reset();
        }
    }
}
