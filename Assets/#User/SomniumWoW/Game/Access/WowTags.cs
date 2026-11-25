// THE SWITCH (PLAN10 s0.6). Defined: the Somnium name tags are hidden with the Bridge's own player-tag member,
// ISomniumPlayerInfo.SetPlayerTagDisableState - UNPROVEN, FIRST USED HERE (not in fusionapi/known_good.txt; the next upload is its
// test). Comment the line out and that member is not compiled at all (the upload then needs no unproven member): the proven fallback
// is used instead - the tag's CanvasGroup alpha 0, the way WowIsolation already hides a visitor's tag.
#define WOW_TAGS_BRIDGE

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Access
{
    /// <summary>
    /// Stage S1 (the user, 2026-09-27: "please hide somnium name players, it is a bridge feature"): on a whitelisted player's
    /// screen, the Somnium name tag (nickname plate) over every other Somnium player of this world is hidden while the Dreamport
    /// runs (the black room and the WoW world), and given back when the world is left (WowRemotes.Reset: WowGame and the driver's
    /// OnDestroy) or the Dreamport stops. A visitor's screen (not on the whitelist) is never touched here: WowGame.Current is
    /// only set for a whitelisted player, and a visitor keeps today's isolation only (PLAN10 s1.2).
    ///
    /// <b>Whose tags</b>: the players WowIsolation leaves to Gear - they have a WoW network record (WowGear.HasRecord: a
    /// whitelisted Somnium player whose Dreamport runs) and the isolation is not hiding them. A visitor's tag is already hidden
    /// by the isolation with everything else of the visitor, so nothing more is done for them (s1.2 "no tags touched for them
    /// beyond today's isolation"). Once taken, a player's tag stays hidden until they leave or this world ends.
    ///
    /// <b>How (the Bridge, <c>#define WOW_TAGS_BRIDGE</c>)</b>: <c>player.Features.Info.SetPlayerTagDisableState(true)</c> per
    /// remote player - the Bridge's own <c>SomniumPlayerTagRules</c> component does exactly this for every player in AllPlayers
    /// every CHECK_INTERVAL = 0.1 s (SomniumSpace.Bridge.dll IL, read with P/tags_meta). The client's side
    /// (CS = E:/Somnium Space/Main Build/Assets/_SomniumSpace): Bridge/Scripts/Components/Player/BridgePlayerInfo.cs:15-18
    /// <c>_infoFeature?.SetPlayerTagActiveState(!isDisabled)</c>; Player/Scripts/Components/Features/Common/InfoFeature.cs:42
    /// <c>AreTagsActive => _isInFrustum &amp;&amp; _isPlayerTagActive &amp;&amp; (...)</c> and :179-184 (the flag, then
    /// RefreshDisplay -> PlayerTagView.SetDisplayState -> the tag's <c>Canvas.enabled</c>, PlayerTagView.cs:241-257). The flag
    /// survives frustum changes (OnEnterFrustum re-reads AreTagsActive). Re-applied once a second (ours; the SDK itself does it
    /// ten times a second), which also covers an un-block (PlayerTagView.SetBlockActive forces the canvas on). Checked: the tag
    /// canvas (the prefab SocialSystem.User.Tag has exactly one Canvas, PlayerTagView._canvas) must be off; seen on at
    /// <see cref="FailChecks"/> passes in a row, or the call throws (a client without the member: MissingMethodException from
    /// the one method that references it), and this visit switches to the fallback - logged.
    ///
    /// <b>The fallback</b> (the switch off, or at run time after a failure): the tag's CanvasGroups get alpha 0 and no raycasts
    /// (WowIsolation's technique and its restore rule: the value restored is the last one seen that was not ours), checked every
    /// frame (so the isolation showing a player again, which puts its own recorded alpha back, is covered in the same frame -
    /// this runs after the isolation) and re-scanned once a second.
    ///
    /// <b>Known effect</b>: the client's inspector mode shows the tags through the same flag (InfoFeature.cs:42), so while this
    /// runs the inspector mode does not show them in this world either.
    /// Ticked by <see cref="WowRemotes.Tick"/> after the isolation; <see cref="Shutdown"/> from <see cref="WowRemotes.Reset"/>.
    /// </summary>
    public static class WowTags
    {
#if WOW_TAGS_BRIDGE
        /// <summary>True when the Bridge member is compiled in (see the switch at the top of this file).</summary>
        public const bool BridgeCompiled = true;
#else
        public const bool BridgeCompiled = false;
#endif

        /// <summary>The switch as a value (static readonly, so neither branch below is "unreachable code" in either setting).</summary>
        private static readonly bool UseBridge = BridgeCompiled;

        /// <summary>Ours: re-apply (Bridge) / re-scan (fallback) and check each hidden tag this often (s).</summary>
        public const float KeepEvery = 1f;

        /// <summary>Ours: the tag canvas seen on this many checks in a row after the Bridge call -> the fallback for this visit.</summary>
        public const int FailChecks = 2;

        /// <summary>Ours: per-player log lines at most.</summary>
        public const int MaxPlayerLogs = 40;

        private sealed class Tagged
        {
            public WowRemotes.Remote Remote;
            public ISomniumPlayer Player;
            public int Id;
            public string Nick;

            /// <summary>Hidden through the Bridge (restore = SetPlayerTagDisableState(false)).</summary>
            public bool Bridge;
            public Canvas TagCanvas;
            public int OnChecks;
            public bool Verified, LoggedNoTag, LoggedNoGroup;

            /// <summary>The isolation hid them last frame (their record went away): its show puts its own alpha back.</summary>
            public bool IsoHidden;

            /// <summary>Fallback: the tag's CanvasGroups this client set to alpha 0, and what each goes back to.</summary>
            public readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
            public readonly List<float> GroupAlpha = new List<float>();
            public readonly List<bool> GroupBlocks = new List<bool>();
        }

        private static readonly List<Tagged> List = new List<Tagged>();
        private static readonly List<CanvasGroup> ScratchGroups = new List<CanvasGroup>();
        private static bool _active;
        private static bool _fallback;
        private static string _fallbackWhy;
        private static float _nextKeep;
        private static int _logs;
        private static int _errors;
        private static int _hiddenEver, _bridgeCalls;
        private static Transform _localRoot, _localCamera, _worldRoot;

        /// <summary>True while this client hides the other Somnium players' name tags.</summary>
        public static bool Active
        {
            get { return _active; }
        }

        /// <summary>True when this visit uses the fallback (the switch is off, or the Bridge member failed here).</summary>
        public static bool UsingFallback
        {
            get { return !UseBridge || _fallback; }
        }

        public static string Describe()
        {
            return "tags " + (_active ? "hidden" : "not hidden") + " (" + (UsingFallback ? "fallback: tag CanvasGroup alpha" + (_fallbackWhy != null ? " - " + _fallbackWhy : "") : "Bridge SetPlayerTagDisableState")
                   + "): " + List.Count + " player(s) now, " + _hiddenEver + " ever, " + _bridgeCalls + " Bridge call(s)";
        }

        /// <summary>From <see cref="WowRemotes.Tick"/>, every frame, right after WowIsolation.Tick.</summary>
        internal static void Tick()
        {
            try
            {
                bool want = WowAccess.Allowed && WowGame.Current != null;
                if (!want)
                {
                    if (_active)
                    {
                        Stop("the Dreamport is not running");
                    }

                    return;
                }

                if (!_active)
                {
                    _active = true;
                    _nextKeep = 0f;
                    WowLog.Info("Tags: on - the Somnium name tags of the other Somnium players in this world are hidden while the Dreamport runs, given back when you leave ("
                                + (UseBridge
                                    ? "the Bridge's ISomniumPlayerInfo.SetPlayerTagDisableState - unproven, first used here; fallback ready: the tag's CanvasGroup alpha"
                                    : "the fallback: the tag's CanvasGroup alpha (the Bridge member is switched off in WowTags.cs)")
                                + "; re-applied every " + KeepEvery + " s; visitors are left to the isolation)");
                }

                CacheLocal();
                float now = Time.unscaledTime;
                bool keep = now >= _nextKeep;
                if (keep)
                {
                    _nextKeep = now + KeepEvery;
                }

                DropLeft();
                TakeNew();
                for (int i = 0; i < List.Count; i++)
                {
                    Tagged t = List[i];
                    if (!ReferenceEquals(t.Player, t.Remote.Player) && t.Remote.Player != null)
                    {
                        // WowRemotes matched a new Bridge player object to this entry (a re-created player): start over on it.
                        Restore(t);
                        t.Player = t.Remote.Player;
                        t.TagCanvas = null;
                        t.Verified = false;
                        t.OnChecks = 0;
                        Apply(t, "a new player object");
                        continue;
                    }

                    if (t.Remote.Id >= 0) t.Id = t.Remote.Id;   // WowRemotes re-reads the Photon id; follow it
                    bool isoHidden = WowIsolation.IsHidden(t.Id);
                    bool isoShown = t.IsoHidden && !isoHidden;
                    t.IsoHidden = isoHidden;
                    if (keep)
                    {
                        Keep(t);
                    }
                    else if (!t.Bridge)
                    {
                        // The isolation showing them again this frame restored the alpha it recorded: re-scan at once (no flash).
                        if (isoShown) HideByAlpha(t);
                        else GuardGroups(t);
                    }
                }
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3)
                {
                    WowLog.Error("Tags: tick threw (#" + _errors + "): " + e);
                }
            }
        }

        /// <summary>Gives every tag back and forgets every player (statics survive a world re-entry). Safe to call twice.</summary>
        public static void Shutdown()
        {
            if (_active || List.Count > 0)
            {
                Stop("leaving the world");
            }

            _active = false;
            _fallback = false;
            _fallbackWhy = null;
            _nextKeep = 0f;
            _logs = 0;
            _errors = 0;
            _hiddenEver = 0;
            _bridgeCalls = 0;
            _localRoot = _localCamera = _worldRoot = null;
            ScratchGroups.Clear();
        }

        // ------------------------------------------------------------------ players

        private static void Stop(string why)
        {
            int n = List.Count;
            for (int i = 0; i < List.Count; i++)
            {
                Restore(List[i]);
            }

            List.Clear();
            _active = false;
            WowLog.Info("Tags: off (" + why + ") - " + n + " player tag(s) given back; " + Describe());
        }

        private static void DropLeft()
        {
            var all = WowRemotes.All;
            for (int i = List.Count - 1; i >= 0; i--)
            {
                Tagged t = List[i];
                bool present = false;
                for (int j = 0; j < all.Count; j++)
                {
                    if (ReferenceEquals(all[j], t.Remote))
                    {
                        present = true;
                        break;
                    }
                }

                if (present)
                {
                    continue;
                }

                // They left: the client destroys their player object with its tag; nothing is written to a player being torn down.
                List.RemoveAt(i);
                PlayerLog("player " + t.Id + " '" + t.Nick + "' left - no longer tracked");
            }
        }

        private static void TakeNew()
        {
            var all = WowRemotes.All;
            for (int i = 0; i < all.Count; i++)
            {
                WowRemotes.Remote r = all[i];
                if (r == null || r.Player == null || r.Id < 0 || Tracked(r))
                {
                    continue;
                }

                // Visitors stay the isolation's (hidden with everything else); a Somnium player is taken the frame the isolation shows them.
                if (WowIsolation.IsHidden(r.Id) || !WowGear.HasRecord(r.Id))
                {
                    continue;
                }

                var t = new Tagged { Remote = r, Player = r.Player, Id = r.Id, Nick = r.Nick };
                List.Add(t);
                _hiddenEver++;
                Apply(t, "has a WoW network record");
            }
        }

        private static bool Tracked(WowRemotes.Remote r)
        {
            for (int i = 0; i < List.Count; i++)
            {
                if (ReferenceEquals(List[i].Remote, r))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Apply(Tagged t, string why)
        {
            if (UseBridge && !_fallback)
            {
                string err;
                if (TryBridge(t.Player, true, out err))
                {
                    t.Bridge = true;
                    PlayerLog("player " + t.Id + " '" + t.Nick + "' (" + why + "): Somnium name tag hidden with the Bridge (SetPlayerTagDisableState(true))");
                    return;
                }

                SwitchToFallback("the Bridge call threw for player " + t.Id + ": " + err);
                return;   // SwitchToFallback hid every tracked player's tag the fallback way, this one included
            }

            HideByAlpha(t);
            PlayerLog("player " + t.Id + " '" + t.Nick + "' (" + why + "): Somnium name tag hidden (fallback: " + t.Groups.Count + " CanvasGroup(s) alpha 0)");
        }

        /// <summary>Once a second: the Bridge's check + keep-alive, or the fallback's re-scan.</summary>
        private static void Keep(Tagged t)
        {
            if (!t.Bridge)
            {
                HideByAlpha(t);
                return;
            }

            Canvas c = TagCanvasOf(t);
            if (c != null)
            {
                bool on = c.enabled && c.gameObject.activeInHierarchy;
                if (on)
                {
                    t.OnChecks++;
                    if (t.OnChecks >= FailChecks)
                    {
                        SwitchToFallback("player " + t.Id + "'s tag canvas stayed on for " + t.OnChecks + " checks after SetPlayerTagDisableState(true)");
                        return;
                    }
                }
                else
                {
                    t.OnChecks = 0;
                    if (!t.Verified)
                    {
                        t.Verified = true;
                        PlayerLog("player " + t.Id + ": the Bridge hid the name tag (its canvas '" + c.name + "' is off)");
                    }
                }
            }

            string err;
            if (!TryBridge(t.Player, true, out err))
            {
                SwitchToFallback("the Bridge keep-alive call threw for player " + t.Id + ": " + err);
            }
        }

        private static void SwitchToFallback(string why)
        {
            if (_fallback)
            {
                return;
            }

            _fallback = true;
            _fallbackWhy = why;
            WowLog.Warn("Tags: the Bridge player-tag member did not work here (" + why + ") - this visit uses the proven fallback (the tag's CanvasGroup alpha)");
            for (int i = 0; i < List.Count; i++)
            {
                Tagged t = List[i];
                if (t.Bridge)
                {
                    string err;
                    TryBridge(t.Player, false, out err);   // give the client's own flag back; errors ignored (the member may not exist)
                    t.Bridge = false;
                }

                HideByAlpha(t);
                PlayerLog("player " + t.Id + " '" + t.Nick + "': Somnium name tag hidden (fallback: " + t.Groups.Count + " CanvasGroup(s) alpha 0)");
            }
        }

        private static void Restore(Tagged t)
        {
            try
            {
                if (t.Bridge)
                {
                    string err;
                    if (!TryBridge(t.Player, false, out err))
                    {
                        WowLog.Warn("Tags: giving player " + t.Id + "'s name tag back through the Bridge failed: " + err);
                    }

                    t.Bridge = false;
                }

                for (int i = 0; i < t.Groups.Count; i++)
                {
                    CanvasGroup g = t.Groups[i];
                    if (g != null)
                    {
                        g.alpha = t.GroupAlpha[i];
                        g.blocksRaycasts = t.GroupBlocks[i];
                    }
                }
            }
            catch (Exception e)
            {
                WowLog.Error("Tags: restoring player " + t.Id + "'s name tag failed: " + e);
            }

            t.Groups.Clear();
            t.GroupAlpha.Clear();
            t.GroupBlocks.Clear();
        }

        // ------------------------------------------------------------------ the Bridge member (the only place it is referenced)

        /// <summary>The Bridge call inside a try: a client whose Bridge lacks the member throws when <c>BridgeSet</c> is compiled, here.</summary>
        private static bool TryBridge(ISomniumPlayer p, bool disabled, out string error)
        {
            error = null;
#if WOW_TAGS_BRIDGE
            if (p == null)
            {
                error = "no Bridge player";
                return false;
            }

            try
            {
                BridgeSet(p, disabled);
                _bridgeCalls++;
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
#else
            error = "the Bridge member is switched off (WOW_TAGS_BRIDGE)";
            return false;
#endif
        }

#if WOW_TAGS_BRIDGE
        /// <summary>
        /// UNPROVEN - FIRST USED HERE (PLAN10 s0.6; the upload is its test): <c>ISomniumPlayerFeatures.Info</c> +
        /// <c>ISomniumPlayerInfo.SetPlayerTagDisableState(bool)</c> ("Set disabling state for Player tag of other players",
        /// SomniumSpace.Bridge.xml). Not inlined, so a missing member fails at this call, inside TryBridge's try.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BridgeSet(ISomniumPlayer p, bool disabled)
        {
            ISomniumPlayerFeatures features = p.Features;
            ISomniumPlayerInfo info = features != null ? features.Info : null;
            if (info == null)
            {
                throw new InvalidOperationException("the player has no Info feature");
            }

            info.SetPlayerTagDisableState(disabled);
        }
#endif

        // ------------------------------------------------------------------ the tag objects (check + fallback)

        /// <summary>The tag roots: children of the remote body prefab (Body.Root.parent) named SocialSystem.User.Tag* or holding PlayerTagView.</summary>
        private static Transform BodyOf(Tagged t)
        {
            Transform root = t.Remote != null ? t.Remote.Root : null;
            Transform body = root != null ? root.parent : null;
            return body != null && Safe(body) ? body : null;
        }

        private static bool IsTag(Transform child)
        {
            return child.name.StartsWith(WowIsolation.TagPrefix, StringComparison.Ordinal) || child.GetComponent(WowIsolation.TagComponent) != null;
        }

        private static Canvas TagCanvasOf(Tagged t)
        {
            if (t.TagCanvas != null)
            {
                return t.TagCanvas;
            }

            Transform body = BodyOf(t);
            if (body == null)
            {
                return null;
            }

            for (int i = 0; i < body.childCount; i++)
            {
                Transform child = body.GetChild(i);
                if (child == t.Remote.Root || !IsTag(child))
                {
                    continue;
                }

                Canvas c = child.GetComponentInChildren<Canvas>(true);   // the prefab's one Canvas (PlayerTagView._canvas)
                if (c != null)
                {
                    t.TagCanvas = c;
                    return c;
                }
            }

            if (!t.LoggedNoTag)
            {
                t.LoggedNoTag = true;
                PlayerLog("player " + t.Id + ": no name tag object found next to the body yet - the Bridge call is trusted until it is");
            }

            return null;
        }

        /// <summary>Fallback: every CanvasGroup of the tag to alpha 0 / no raycasts (the isolation's HideGroup rule).</summary>
        private static void HideByAlpha(Tagged t)
        {
            Transform body = BodyOf(t);
            if (body == null)
            {
                return;
            }

            int found = 0;
            for (int i = 0; i < body.childCount; i++)
            {
                Transform child = body.GetChild(i);
                if (child == t.Remote.Root || !IsTag(child))
                {
                    continue;
                }

                child.GetComponentsInChildren(true, ScratchGroups);
                for (int g = 0; g < ScratchGroups.Count; g++)
                {
                    found++;
                    HideGroup(t, ScratchGroups[g]);
                }

                ScratchGroups.Clear();
            }

            if (found == 0 && !t.LoggedNoGroup)
            {
                t.LoggedNoGroup = true;
                WowLog.Warn("Tags: player " + t.Id + "'s name tag has no CanvasGroup (or is not up yet): the fallback cannot hide it; re-tried every " + KeepEvery + " s");
            }
        }

        /// <summary>Every frame between the re-scans: a group written back by the client (or by the isolation showing them) goes to 0 again.</summary>
        private static void GuardGroups(Tagged t)
        {
            for (int i = 0; i < t.Groups.Count; i++)
            {
                CanvasGroup g = t.Groups[i];
                if (g != null && (g.alpha > 0f || g.blocksRaycasts))
                {
                    HideGroup(t, g);
                }
            }
        }

        private static void HideGroup(Tagged t, CanvasGroup g)
        {
            if (g == null)
            {
                return;
            }

            int i = t.Groups.IndexOf(g);
            if (i < 0)
            {
                if (g.alpha <= 0f && !g.blocksRaycasts)
                {
                    return;   // not shown by the client right now: not ours to record
                }

                t.Groups.Add(g);
                t.GroupAlpha.Add(g.alpha);
                t.GroupBlocks.Add(g.blocksRaycasts);
            }
            else
            {
                if (g.alpha > 0f)
                {
                    t.GroupAlpha[i] = g.alpha;
                }

                if (g.blocksRaycasts)
                {
                    t.GroupBlocks[i] = true;
                }
            }

            g.alpha = 0f;
            g.blocksRaycasts = false;
        }

        // ------------------------------------------------------------------ guards

        private static void CacheLocal()
        {
            _localRoot = null;
            _localCamera = null;
            _worldRoot = WowGame.Current != null ? WowGame.Current.transform : null;
            try
            {
                _localRoot = WowPlayer.Root;
                Camera cam = WowPlayer.Head();
                _localCamera = cam != null ? cam.transform : null;
            }
            catch (Exception)
            {
                // The client's player is being replaced: only remote Bridge players are looked at anyway.
            }
        }

        /// <summary>Not the local player's (body, camera) and not this world's (the WoW Game object, inside or above it).</summary>
        private static bool Safe(Transform t)
        {
            if (t == null)
            {
                return false;
            }

            if (_localRoot != null && (_localRoot == t || _localRoot.IsChildOf(t)))
            {
                return false;
            }

            if (_localCamera != null && _localCamera.IsChildOf(t))
            {
                return false;
            }

            if (_worldRoot != null && (_worldRoot == t || t.IsChildOf(_worldRoot) || _worldRoot.IsChildOf(t)))
            {
                return false;
            }

            return true;
        }

        private static void PlayerLog(string text)
        {
            if (_logs >= MaxPlayerLogs)
            {
                return;
            }

            _logs++;
            WowLog.Info("Tags: " + text + (_logs == MaxPlayerLogs ? " (later per-player lines not logged)" : ""));
        }
    }
}
