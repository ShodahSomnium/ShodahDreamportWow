using System;

namespace SomniumSpace.Worlds.SomniumWoW
{
    /// <summary>
    /// The private-testing gate (the Rec Room Dreamport's RrAccess, ported). While the Dreamport is shown only to the
    /// team, the world starts only for the Somnium accounts listed in <see cref="Whitelist"/>. Everyone else sees
    /// NOTHING of it: no room, no panel, no text, no music; the game files are not read, no component, network slot,
    /// save or setting is touched (WowGame starts nothing before the verdict). Privacy works both ways through the
    /// isolation (Game/Access/WowIsolation): a visitor treats nobody as "with me", so every other player is hidden and
    /// silent on the visitor's screen; and a visitor claims no WoW network slot, so the testers' isolation hides and
    /// silences the visitor.
    ///
    /// The check (Core/Unity/WowAccessCheck.cs, the Unity/Bridge half of this class) reads the local player's
    /// <c>Properties.NickName</c> (the username on the name tag) and <c>Properties.Id</c> from the Bridge (proven members,
    /// fusionapi/known_good.txt). The comparison ignores letter case and surrounding spaces. The Bridge fills the local
    /// player a moment after the world starts, so the check waits for it, with a warning in the log after
    /// <see cref="NameTimeout"/> seconds. Nothing starts while it waits, so the gate fails closed. The editor (no client)
    /// is always let in. Player.log gets one line with the verdict, the username and the id, so an id can be copied into
    /// the list as well. This half is pure C# (the offline harness runs <see cref="Matches(string, string, string[])"/>).
    /// </summary>
    public static partial class WowAccess
    {
        /// <summary>
        /// Who may play. Add Somnium usernames (as on the name tag; any letter case) or Somnium user ids (Player.log shows
        /// each player's at start: "Access: ... (id ...)"). Leave the list empty to open the world to everyone.
        /// </summary>
        public static readonly string[] Whitelist =
        {
            "Shodah",
            "Wildy",
            "Meloz",
            "ꨄ Rage Quit ꨄ",
            "denevraut",   // 2026-10-05: added at the user's request   // 2026-10-04: added at the user's request (exact name-tag text; if it does not match, her Player.log "Access:" line gives the exact name and id)
            "Shodah Dev",  // 2026-11-04: added at the user's request (their stage account)
            "Destos",      // 2026-11-04: added at the user's request
            "Ydliw",       // 2026-11-04: added at the user's request
            "StageUser9",  // 2026-11-04: stage test account (user)
            "StageUser13", // 2026-11-04: stage test account (user)
            "StageUser12", // 2026-11-04: stage test account (user)
            "StageUser19", // 2026-11-04: stage test account (user)
            "StageUser7",  // 2026-11-04: stage test account (user)
            "StageUser8",  // 2026-11-04: stage test account (user)
            "StageUser11", // 2026-11-04: stage test account (user)
            "StageUser65", // 2026-11-04: stage test account (user)
            "StageUser6",  // 2026-11-04: stage test account (user)
            "StageUser14", // 2026-11-04: stage test account (user)
            "StageUser4",  // 2026-11-04: stage test account (user)
            "StageUser16", // 2026-11-04: stage test account (user)
            "StageUser3",  // 2026-11-04: stage test account (user)
            "StageUser5",  // 2026-11-04: stage test account (user)
            "StageUser15", // 2026-11-04: stage test account (user)
            "StageUser17", // 2026-11-04: stage test account (user)
            "GingasVR",    // 2026-11-06: added at the user's request
        };

        /// <summary>After this many seconds without a username from the client, the log says the check is still waiting.</summary>
        public const float NameTimeout = 30f;

        public enum Verdict
        {
            Pending,
            Allowed,
            Denied,
        }

        public static Verdict State { get; private set; }

        /// <summary>The local player's username and Somnium id as the client reported them (null before, or in the editor).</summary>
        public static string Nick { get; private set; }

        public static string UserId { get; private set; }

        public static bool Allowed
        {
            get { return State == Verdict.Allowed; }
        }

        /// <summary>True when the username or the id is on <see cref="Whitelist"/> (letter case and spaces ignored).</summary>
        public static bool Matches(string nick, string id)
        {
            return Matches(nick, id, Whitelist);
        }

        /// <summary>The rule of <see cref="Matches(string, string)"/> against any list (blank entries never match).</summary>
        public static bool Matches(string nick, string id, string[] list)
        {
            if (list == null)
            {
                return false;
            }

            foreach (string entry in list)
            {
                string e = entry != null ? entry.Trim() : "";
                if (e.Length == 0)
                {
                    continue;
                }

                if ((!string.IsNullOrEmpty(nick) && string.Equals(e, nick.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(id) && string.Equals(e, id.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Reset()
        {
            State = Verdict.Pending;
            Nick = null;
            UserId = null;
        }

        /// <summary>The verdict once the username is known, with its one Player.log line.</summary>
        private static void Decide()
        {
            string who = Nick + " (id " + (string.IsNullOrEmpty(UserId) ? "unknown" : UserId) + ")";
            State = Matches(Nick, UserId) ? Verdict.Allowed : Verdict.Denied;
            WowLog.Info("Access: " + (State == Verdict.Allowed
                ? who + " is on the whitelist: starting the Dreamport"
                : who + " is not on the whitelist: nothing is loaded or shown, other players are hidden and silent"));
        }
    }
}
