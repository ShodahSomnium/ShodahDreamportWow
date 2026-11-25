using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Stage S1 (owner hud; the user: "this button should 'assign focus' so you'll click on a player, press 'x' this will assign the
    /// selected player to focus ... whenever you press this button, you will select that focused person, you can triple press it to
    /// unfocus"). WoW's focus (FOCUS, GlobalStrings.lua:3710) kept on OUR side only: the realm never hears of it (the 3.3.5a client
    /// keeps its focus client-side as well). The Dreamport addition is how it is driven: one button (VR: the left controller's X,
    /// VrFocusButton; desktop: H) - no focus yet: the current target becomes the focus; with a focus: the focus becomes the target;
    /// three short presses within <see cref="TripleWindow"/> s: the focus is cleared. The focus frame (UiFocusFrame) shows it.
    /// Pure C# (no UnityEngine): the caller passes the time, so the harness can prove the button logic (Probes/S1/hud).
    /// The focus belongs to the running session: a new session (back to the room, another character, online) starts without one.
    /// </summary>
    public static class WowFocus
    {
        /// <summary>Ours: a press shorter than this is ours; longer presses are the client's (the left X "Inspector" action is a Hold
        /// of InputSystem's default 0.4 s: XRI Player Interactions.inputactions:70-78 + :392-398, InputSystem.inputsettings.asset:28).</summary>
        public const float ShortPressMax = 0.35f;
        /// <summary>Ours: three short presses within this many seconds clear the focus.</summary>
        public const float TripleWindow = 0.8f;

        private static ulong _guid;
        private static string _name;
        private static IWowSession _session;
        private static float _p1 = -100f, _p2 = -100f;   // the last two short presses (seconds, the caller's clock)
        private static int _sets, _selects, _clears;

        /// <summary>The last notice shown to the player (the harness reads it; the UI gets it as an Info event).</summary>
        public static string LastNotice { get; private set; }

        /// <summary>The focus (0 = none). Only valid for the session that is running (WowPlay.Session).</summary>
        public static ulong Guid
        {
            get
            {
                if (_guid != 0 && !ReferenceEquals(_session, WowPlay.Session))
                {
                    WowLog.Info("Focus: cleared - the game session changed.");
                    _guid = 0;
                    _name = null;
                    _session = null;
                }
                return _guid;
            }
        }

        /// <summary>The focus's name as last seen (null without a focus).</summary>
        public static string Name { get { return Guid != 0 ? _name : null; } }

        public static bool Has { get { return Guid != 0; } }

        /// <summary>Makes guid the focus (0 = clears it). how = what did it, for the log ("the X button", "a right-click on a party frame").</summary>
        public static void Set(IWowSession s, ulong guid, string how)
        {
            if (s == null) return;
            if (guid == 0) { Clear(how); return; }
            _session = s;
            _guid = guid;
            _name = NameOf(s, guid);
            _sets++;
            WowLog.Info("Focus: set to " + (_name ?? "?") + " (0x" + guid.ToString("X") + ") by " + how + " (" + _sets + " so far).");
            Notice("Focus: " + (_name ?? "?") + ".");   // ours (WoW shows the focus frame only; the line tells a VR player what the X did)
        }

        public static void Clear(string how)
        {
            if (_guid == 0) return;
            string was = _name;
            _guid = 0;
            _name = null;
            _session = null;
            _clears++;
            WowLog.Info("Focus: cleared by " + how + " (was " + (was ?? "?") + "; " + _clears + " so far).");
            Notice("Focus cleared.");   // ours
        }

        /// <summary>
        /// The focus becomes the target (WoW's "Target Focus", BINDING_NAME_TARGETFOCUS). Returns false (and says why) when there is no
        /// focus or it is not around: a unit the session cannot find is only targeted when it is a member of the server group (the
        /// realm accepts that selection; offline there are no such units).
        /// </summary>
        public static bool Select(IWowSession s, string how)
        {
            ulong g = Guid;
            if (s == null || g == 0) return false;
            bool self = s.Player != null && g == s.Player.Guid;
            EntityState e = self ? null : s.Find(g);
            if (e != null && e.Name != null) _name = e.Name;
            // Offline every unit is an entity of the session (OfflineSession.SetTarget turns an unknown guid into "no target"), so a
            // focus the session cannot find is simply not around; online a server-group member may be out of view and still selectable.
            if (!self && e == null && (s is OfflineSession || !InGroup(s, g)))
            {
                Notice("Your focus is out of range.");   // ours
                return false;
            }
            ulong before = s.TargetGuid;
            if (before != g) s.SetTarget(g);
            if (s.TargetGuid != g)
            {
                if (s.TargetGuid != before) s.SetTarget(before);   // never leave the player without the target they had
                Notice("Your focus is out of range.");   // ours (the session refused the target)
                return false;
            }
            _selects++;
            if (_selects <= 20 || _selects % 50 == 0) WowLog.Info("Focus: " + (_name ?? "?") + " targeted by " + how + " (" + _selects + " so far).");
            return true;
        }

        /// <summary>
        /// One short press of the focus button at time now (seconds): the third short press within TripleWindow clears the focus;
        /// otherwise no focus = the target becomes the focus (ERR_GENERIC_NO_TARGET "You have no target.", GlobalStrings.lua:3064, when
        /// there is none), a focus = it becomes the target. Returns what it did: "set", "select", "clear" or "none".
        /// </summary>
        public static string ShortPress(IWowSession s, float now, string how)
        {
            if (s == null) return "none";
            bool triple = now - _p2 <= TripleWindow;
            _p2 = _p1;
            _p1 = now;
            if (triple)
            {
                _p1 = _p2 = -100f;
                if (Guid == 0) return "none";
                Clear(how + " pressed three times");
                return "clear";
            }
            if (Guid == 0)
            {
                ulong t = s.TargetGuid;
                if (t == 0)
                {
                    Notice("You have no target.");   // ERR_GENERIC_NO_TARGET (GlobalStrings.lua:3064)
                    return "none";
                }
                Set(s, t, how);
                return "set";
            }
            return Select(s, how) ? "select" : "none";
        }

        /// <summary>Forgets the press history (the harness between cases; the button when a session starts).</summary>
        public static void ResetPresses() { _p1 = _p2 = -100f; }

        private static bool InGroup(IWowSession s, ulong g)
        {
            List<GroupMemberView> group = s.Player != null ? s.Player.Group : null;
            if (group == null) return false;
            for (int i = 0; i < group.Count; i++) if (group[i] != null && group[i].Guid == g) return true;
            return false;
        }

        /// <summary>The unit's name: the entity, else the group member, else the player's own.</summary>
        public static string NameOf(IWowSession s, ulong g)
        {
            if (s == null || g == 0) return null;
            if (s.Player != null && g == s.Player.Guid) return s.Player.Name;
            EntityState e = s.Find(g);
            if (e != null) return e.Name;
            List<GroupMemberView> group = s.Player != null ? s.Player.Group : null;
            if (group != null) for (int i = 0; i < group.Count; i++) if (group[i] != null && group[i].Guid == g) return group[i].Name;
            return null;
        }

        /// <summary>A yellow info line on the HUD (the same path as the session's Info events: WowArmUi -> UiMessages).</summary>
        private static void Notice(string text)
        {
            LastNotice = text;
            try { WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = text }); }
            catch (Exception e) { WowLog.Warn("Focus: notice failed: " + e.Message); }
        }
    }
}
