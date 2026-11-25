using System;
using System.Collections;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW
{
    /// <summary>The Unity/Bridge half of <see cref="WowAccess"/> (Core/WowAccess.cs has the list, the rule and the notes).</summary>
    public static partial class WowAccess
    {
        /// <summary>
        /// Decides <see cref="State"/>. Waits for the username and runs before anything of the Dreamport starts. Finishes
        /// without yielding when the list is empty or in the editor (WowGame then starts in the same frame, as before).
        /// </summary>
        public static IEnumerator Check()
        {
            State = Verdict.Pending;
            if (Whitelist.Length == 0)
            {
                State = Verdict.Allowed;
                WowLog.Info("Access: the whitelist is empty: the world is open to everyone");
                yield break;
            }

            if (Application.isEditor)
            {
                State = Verdict.Allowed;
                WowLog.Info("Access: editor: the whitelist (" + Whitelist.Length + " name(s)) is not applied");
                yield break;
            }

            // Nothing starts until the username is known, however long the client takes: a slow client only delays the
            // start, and nobody gets in without a name.
            float warnAt = Time.realtimeSinceStartup + NameTimeout;
            bool warned = false;
            while (!ReadLocal())
            {
                if (!warned && Time.realtimeSinceStartup >= warnAt)
                {
                    warned = true;
                    WowLog.Warn("Access: the client has not reported the local username after " + NameTimeout.ToString("0") + " s: still waiting, nothing is started meanwhile");
                }

                yield return null;
            }

            Decide();
        }

        private static bool ReadLocal()
        {
            try
            {
                ISomniumPlayersContainer c = SomniumBridge.PlayersContainer;
                ISomniumPlayer local = c != null ? c.LocalPlayer : null;
                ISomniumPlayerProperties p = local != null ? local.Properties : null;
                if (p == null)
                {
                    return false;
                }

                Nick = p.NickName;
                UserId = p.Id;
                return !string.IsNullOrEmpty(Nick);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
