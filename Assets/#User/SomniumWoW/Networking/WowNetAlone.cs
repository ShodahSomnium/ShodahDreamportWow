using System;

namespace SomniumSpace.Worlds.SomniumWoW.Networking
{
    /// <summary>
    /// Stage S1 (PLAN10 s0.7, pure C# - the harness runs it): the "alone" rule of the session diagnostics. The user
    /// (2026-09-27): "I don't see somnium players avatars at the start, wildy logged out and came back and then we could see
    /// each other" - the lead read that Player.log: player 2 appeared only after the relog, so the two were most likely in two
    /// different Somnium instances (two Fusion sessions of this world). No speculative fix; instead WowNet logs the session and
    /// its players, and this class decides the one-time on-screen hint:
    /// <list type="bullet">
    /// <item>Alone = in the Fusion room (joined, its player list read) and no player in it but this client.</item>
    /// <item>"The whitelist says others play" = <see cref="OthersOnList"/> is not 0: the whitelist names someone who is not this
    /// player, or it is empty (the world open to everyone).</item>
    /// <item>Alone for <see cref="AloneSeconds"/> without a break while others may play -> due; the hint is shown once per visit
    /// (<see cref="MarkShown"/>) as soon as there is a place to show it (the WoW world: the wrist panel / desktop info label and the
    /// game's message line). Someone joining (or the room lost) before that cancels it; the next 60 s alone count again.</item>
    /// </list>
    /// The session name is never logged as such: the client itself logs it encrypted (CS/Network/Scripts/NetworkRunnerHandler.cs:245
    /// <c>runner.SessionInfo?.Name, isEncrypt: !Application.isEditor</c>), so WowNet logs <see cref="Fingerprint"/> of it - two
    /// players' logs with the same fingerprint were in the same instance.
    /// </summary>
    public sealed class WowNetAlone
    {
        /// <summary>Ours (PLAN10 s0.7): alone this long before the hint.</summary>
        public const double AloneSeconds = 60.0;

        /// <summary>Ours: how long the wrist panel / desktop info label shows the hint.</summary>
        public const float HintSeconds = 30f;

        /// <summary>Our words for the game's message line (two short lines: the VR HUD's line is about 40 characters wide).</summary>
        public const string MessageTop = "No other Somnium player in this instance";
        public const string MessageBelow = "A friend may be in another instance";

        /// <summary>Our words for the wrist panel / desktop info label.</summary>
        public const string HintText = "No other Somnium player is in this instance of the world.\nIf a friend is playing, they are in another instance:\nleaving and re-entering the world may bring you together.";

        private double _aloneSince = -1.0;

        /// <summary>Alone for 60 s now and not shown yet this visit.</summary>
        public bool Due { get; private set; }

        /// <summary>Shown this visit (never again).</summary>
        public bool Shown { get; private set; }

        /// <summary>How many times it became due (for the log).</summary>
        public int DueCount { get; private set; }

        /// <summary>
        /// Once a frame. <paramref name="inRoom"/> = the Fusion room is joined and its player list was read; <paramref name="otherPlayers"/>
        /// = players in it other than this client. Returns true at the moment it becomes due (the caller logs it).
        /// </summary>
        public bool Step(double now, bool inRoom, int otherPlayers, bool othersMayPlay)
        {
            bool alone = inRoom && otherPlayers == 0 && othersMayPlay;
            if (!alone || Shown)
            {
                _aloneSince = -1.0;
                Due = false;
                return false;
            }

            if (_aloneSince < 0.0)
            {
                _aloneSince = now;
            }

            if (Due || now - _aloneSince < AloneSeconds)
            {
                return false;
            }

            Due = true;
            DueCount++;
            return true;
        }

        /// <summary>The hint is on screen: never again this visit.</summary>
        public void MarkShown()
        {
            Shown = true;
            Due = false;
        }

        /// <summary>Seconds alone so far (0 when not alone).</summary>
        public double AloneFor(double now)
        {
            return _aloneSince < 0.0 ? 0.0 : now - _aloneSince;
        }

        /// <summary>
        /// Entries of the whitelist that are not this player (WowAccess.Matches' rule: letter case and spaces ignored, blank entries
        /// skipped); -1 = the list is empty or has only blank entries, i.e. the world is open to everyone (others may play too).
        /// </summary>
        public static int OthersOnList(string[] list, string nick, string id)
        {
            if (list == null)
            {
                return -1;
            }

            int entries = 0, others = 0;
            var one = new string[1];
            for (int i = 0; i < list.Length; i++)
            {
                string e = list[i] != null ? list[i].Trim() : "";
                if (e.Length == 0)
                {
                    continue;
                }

                entries++;
                one[0] = e;
                if (!WowAccess.Matches(nick, id, one))
                {
                    others++;
                }
            }

            return entries == 0 ? -1 : others;
        }

        /// <summary>"The whitelist says others play" (see <see cref="OthersOnList"/>).</summary>
        public static bool OthersMayPlay(string[] list, string nick, string id)
        {
            return OthersOnList(list, nick, id) != 0;
        }

        /// <summary>FNV-1a (32 bit) of the text as "#xxxxxxxx" ("#none" for null / empty): the same name gives the same fingerprint.</summary>
        public static string Fingerprint(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "#none";
            }

            uint h = 2166136261u;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                h ^= (uint)(c & 0xFF);
                h *= 16777619u;
                h ^= (uint)(c >> 8);
                h *= 16777619u;
            }

            return "#" + h.ToString("x8");
        }
    }
}
