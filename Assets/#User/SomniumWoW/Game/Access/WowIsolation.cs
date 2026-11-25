using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Access
{
    /// <summary>
    /// Hides, on this client only, the other players who are not "with me", and silences them (the Rec Room Dreamport's
    /// RrIsolation, ported with ONE change of rule: who is with me is decided by the caller's <see cref="Rule.IsWithMe"/>,
    /// asked with the player's Photon id, instead of Rec Room's room presence).
    ///
    /// <b>The two rules WowGame supplies</b> (Core/WowAccess decides which):
    /// <list type="bullet">
    /// <item>A visitor (not on the whitelist): nobody is with me - every other player is hidden and silent on the visitor's
    /// screen, and nothing else of the Dreamport runs.</item>
    /// <item>A whitelisted player: the remote player has a WoW network record (WowGear.HasRecord: a claimed "Wow Net Player"
    /// slot, in the room or playing). A visitor never claims a slot, so they are hidden and silent at once; a tester is left
    /// entirely to Gear's own map/room/distance hiding.</item>
    /// </list>
    /// A change must hold for <see cref="Rule.HideDelay"/> (apart) or <see cref="Rule.ShowDelay"/> (together) before
    /// anything is switched. The first decision about a newly seen player is applied at once, so a stranger is never drawn
    /// for half a second. An unknown Photon id (-1: the player's network object is not up) and a predicate that throws
    /// count as apart (fails closed).
    ///
    /// <b>Hand-over to Gear</b> (whitelisted rule). Gear's GearHider hides a recorded player's renderers with the same
    /// switch (<c>Renderer.forceRenderingOff</c>), touching only renderers it found drawn and flipped itself, and re-scans
    /// once a second while hidden. The two never own the same player at the same time: the isolation leaves every player
    /// with a record alone (no hide, no re-scan, no sweep), and Gear only knows players with a record. Hand-over order:
    /// <list type="bullet">
    /// <item>Record appears: the rule uses <c>DecideInterval 0</c> and <c>ShowDelay 0</c>, shows are applied before hides
    /// and are never capped, and the driver's LateUpdate (order 9990) runs before WowGear's (10000) - so the isolation has
    /// put every switch back in the same frame, before GearHider's first look collects the renderers it wants off. The
    /// Photon id is re-read from the Bridge at every decision, so the isolation never learns a player's id later than
    /// WowNet does.</item>
    /// <item>Record gone: WowGear.Update drops that player's GearRemote (GearHider.Show: its renderers back) before this
    /// LateUpdate sees "no record"; the isolation hides them <see cref="HideDelay"/> later with everything drawn again.</item>
    /// </list>
    ///
    /// <b>What a remote player is made of</b> (measured by the Rec Room Dreamport against the Somnium client source,
    /// CLIENT = E:\Somnium Space\Main Build\Assets\_SomniumSpace; the two names this code matches were re-checked for WoW:
    /// AvatarManager.cs:105 "Avatar for Player [nick] [longId]", InventoryItemController.cs:73 "Simulator Item: [..]").
    /// Four places, only one under Body.Root:
    /// <list type="number">
    /// <item>The body prefab <c>Player.XR.Body.Simulator</c> / <c>Player.NonXR.Body.Simulator</c>, which is
    /// <c>Body.Root.parent</c>: the CharacterController on Body.Root (only a collider: the remote pose is written to
    /// transform.position), the head trigger, the feedback VFX (with an AudioSource), and - as siblings of Body.Root - the
    /// name tag <c>SocialSystem.User.Tag</c> and the quick chat bubble.</item>
    /// <item>The avatar model <c>Avatar for Player [nick] [longId]</c>, instantiated with no parent for remote players: a
    /// scene root, with its LOD holders and the wearables parented to its bones.</item>
    /// <item>Cloth wearables and held items: scene roots named <c>Simulator Item: [..]</c>, skinned to the avatar's bones.</item>
    /// <item>The voice: <c>Voice.Speaker</c> (RemoteVoiceSpeaker + AudioSource) in the network part
    /// <c>Player.XR/NonXR.Part.Network</c>, a Fusion NetworkObject whose state authority is the player.</item>
    /// </list>
    ///
    /// <b>How they are hidden</b> - always the smallest local switch, always restored exactly:
    /// <list type="bullet">
    /// <item>Renderers: <c>Renderer.forceRenderingOff</c>, not <c>enabled</c>. The client writes <c>enabled</c> (avatar LOD
    /// changes, wearables, cloth bones) but never <c>forceRenderingOff</c> (only the Crest ocean does, anywhere in the
    /// client - re-checked), so the client's own state is never overwritten and nothing it switched meanwhile is undone on
    /// restore.</item>
    /// <item>The name tag: its CanvasGroups get alpha 0 and blocksRaycasts false. The client sets the tag Canvas'
    /// <c>enabled</c> on every frustum enter/exit (which would flash the tag), but writes the group alpha only for a quick
    /// chat; an alpha the client writes while hidden becomes the value restored.</item>
    /// <item>Other canvases next to Body.Root (the chat bubble): <c>Canvas.enabled</c> false.</item>
    /// <item>Colliders (the remote CharacterController, which would otherwise block the local capsule and VR hands, and the
    /// head trigger): <c>enabled</c> false.</item>
    /// <item>The voice (every AudioSource in the player's own NetworkObjects): <c>AudioSource.volume</c> 0, never
    /// <c>mute</c> - <c>mute</c> on the voice is the client's own mute/block state (written asynchronously and from the
    /// social menu, read back for its UI); writing it would hide a block made while hidden and undo it on restore. The
    /// volume is written only by the client's SetVoiceLevel (profile slider / Bridge, never per frame), so a volume above
    /// 0 seen on a re-scan is the client's new level: it becomes the value restored and the voice goes back to 0. Exact 0,
    /// so Unity can virtualise the channel. The one loss: a level set to exactly 0 from the profile slider while the
    /// player is hidden cannot be told from ours, and the earlier level comes back.</item>
    /// <item>Other sounds (the feedback VFX source, items): <c>AudioSource.mute</c> true. Only sources found unmuted are
    /// muted and later unmuted, so a sound already muted stays muted.</item>
    /// </list>
    /// Nothing is added, destroyed, re-parented, moved or re-materialled, and the local player's objects and our own world
    /// are never touched (a root that contains the local body or camera, or that is inside, or holds, the WoW Game object
    /// (<see cref="Rule.WorldRoot"/>) is skipped; our "Wow Net" / "Scene Networking" network objects are skipped; an avatar
    /// is matched by the remote's raw Bridge nickname only, never the local one and never when two players share it).
    ///
    /// <b>Cost</b>: decisions every <see cref="Rule.DecideInterval"/> (the predicate and one Bridge id read per player); at
    /// most <see cref="MaxApplyPerFrame"/> hides and <see cref="MaxMaintainPerFrame"/> re-scans per frame (shows are cheap
    /// restores and never wait), each hidden player re-scanned every <see cref="MaintainInterval"/> for renderers/colliders/
    /// sounds the client re-enabled or created (avatar reloads, LOD switches, outfit changes); the scene-root and
    /// NetworkObject search (a "sweep") at most once per frame, only while someone is hidden and only when needed. Frame
    /// budget <see cref="FrameBudgetMs"/> beyond the first item (Rec Room measured a cold sweep 3-4 ms, warm 0.25-0.33 ms).
    ///
    /// <b>Known limit (not fixable from the world)</b>: the client's AvatarsCullingSystem gives full avatars to the nearest
    /// 8 players within 15 m, by distance alone. Hidden players standing near you still take those slots.
    ///
    /// Started by <see cref="Start"/> (WowGame, after the verdict), ticked by <see cref="WowRemotes.Tick"/> from
    /// <see cref="WowIsolationDriver"/>, shut down - restoring everything - by <see cref="WowRemotes.Reset"/> (driver and
    /// WowGame OnDestroy; idempotent). Only Unity and proven Bridge/Fusion members are used (fusionapi/check.sh).
    /// </summary>
    public static class WowIsolation
    {
        /// <summary>Default: apart this long before a player is hidden.</summary>
        public const float HideDelay = 0.5f;

        /// <summary>Default: together this long before a player is shown again.</summary>
        public const float ShowDelay = 0.35f;

        public const float DecideInterval = 0.2f;
        public const float MaintainInterval = 0.5f;

        /// <summary>A hide re-uses a sweep this recent; an older one is redone first.</summary>
        public const float SweepFresh = 0.5f;

        /// <summary>While a hidden player's avatar or voice is not found yet (the first few tries).</summary>
        public const float SweepRetry = 2f;

        /// <summary>While anyone is hidden: new cloth items, replaced avatar or network objects.</summary>
        public const float SweepPeriod = 10f;

        public const int SweepRetries = 5;
        public const int MaxApplyPerFrame = 2;
        public const int MaxMaintainPerFrame = 2;

        /// <summary>Work per frame beyond the first item stops after this (ms).</summary>
        public const float FrameBudgetMs = 1.5f;

        /// <summary>AvatarManager.Initialize: "Avatar for Player [" + nick + "] [" + longId + "]".</summary>
        public const string AvatarPrefix = "Avatar for Player [";

        /// <summary>InventoryItemController: "Simulator Item: [type] [bundleId]" for other players' items.</summary>
        public const string ItemPrefix = "Simulator Item:";

        /// <summary>The name tag prefab (SocialSystem.User.Tag.prefab), and its component (excluded from obfuscation).</summary>
        public const string TagPrefix = "SocialSystem.User.Tag";
        public const string TagComponent = "PlayerTagView";

        /// <summary>This world's own scene network objects (WowNet's slots and SceneNetworking), never touched.</summary>
        public const string OwnNetRoot = "Wow Net";
        public const string OwnNetworkingRoot = "Scene Networking";

        /// <summary>Who is with me, and how the log words it (WowGame builds one of two).</summary>
        public sealed class Rule
        {
            /// <summary>For the log: e.g. "visitor: nobody is with you".</summary>
            public string Name = "?";

            /// <summary>Photon id (Properties.PhotonId, never -1 here) -> with me. Called on the main thread.</summary>
            public Func<int, bool> IsWithMe;

            /// <summary>For the log, why a player is shown / hidden.</summary>
            public string WithText = "with you", ApartText = "not with you";

            public float HideDelay = WowIsolation.HideDelay;
            public float ShowDelay = WowIsolation.ShowDelay;
            public float DecideInterval = WowIsolation.DecideInterval;

            /// <summary>Optional: the network room is up (the rule cannot say "with me" without it) - for one warning.</summary>
            public Func<bool> Online;

            /// <summary>For the warning when <see cref="Online"/> stays false.</summary>
            public string OfflineText = "the network room has not been up for 20 s: they stay hidden";

            /// <summary>The WoW Game object: never touched, nor anything inside it or holding it.</summary>
            public Transform WorldRoot;
        }

        private sealed class Entry
        {
            public int Id = -1;
            public string Nick = "?";
            public string RawNick;
            public WowRemotes.Remote Remote;
            public int Seen;

            public Transform Root;
            public Transform Body;
            public Transform Avatar;
            public readonly List<Transform> Extras = new List<Transform>();
            public readonly List<AudioSource> Voices = new List<AudioSource>();

            public bool Decided;
            public bool Want = true;
            public float WantSince;
            public bool With = true;
            public bool Visible = true;
            public bool Queued;
            public float NextMaintain;
            public int AvatarTries;
            public int VoiceTries;
            public bool AvatarUnmatchable;
            public bool LoggedAvatar, LoggedVoice, LoggedNoTag;

            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Collider> Colliders = new List<Collider>();
            public readonly List<Canvas> Canvases = new List<Canvas>();
            public readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
            public readonly List<float> GroupAlpha = new List<float>();
            public readonly List<bool> GroupBlocks = new List<bool>();
            public readonly List<AudioSource> Muted = new List<AudioSource>();

            /// <summary>Voices set to volume 0, and the volume each goes back to (the client's newest level).</summary>
            public readonly List<AudioSource> Quiet = new List<AudioSource>();
            public readonly List<float> QuietVolume = new List<float>();
            public bool LoggedLevel;
            public readonly HashSet<Object> Touched = new HashSet<Object>();

            public bool NeedsSweep
            {
                get { return !With || !Visible; }
            }
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<WowRemotes.Remote, Entry> ByRemote = new Dictionary<WowRemotes.Remote, Entry>();
        private static readonly List<Entry> Queue = new List<Entry>();

        private static readonly List<Renderer> ScratchRenderers = new List<Renderer>();
        private static readonly List<Collider> ScratchColliders = new List<Collider>();
        private static readonly List<AudioSource> ScratchAudio = new List<AudioSource>();
        private static readonly List<Canvas> ScratchCanvases = new List<Canvas>();
        private static readonly List<CanvasGroup> ScratchGroups = new List<CanvasGroup>();
        private static readonly List<SkinnedMeshRenderer> ScratchSkinned = new List<SkinnedMeshRenderer>();
        private static readonly List<GameObject> ScratchRoots = new List<GameObject>();
        private static readonly List<Scene> ScratchScenes = new List<Scene>();
        private static readonly List<KeyValuePair<string, Transform>> AvatarRoots = new List<KeyValuePair<string, Transform>>();
        private static readonly List<Transform> ItemRoots = new List<Transform>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static Rule _rule;
        private static bool _started;
        private static int _stamp;
        private static int _cursor;
        private static int _errors;
        private static int _askErrors;
        private static int _changeLogs;
        private static int _sweeps;
        private static int _sweepFrame = -1;
        private static float _lastSweep = -100f;
        private static float _sweepMs, _sweepMaxMs, _hideMaxMs, _tickMs, _tickMaxMs;
        private static float _voicePassMs = -1f;
        private static int _voicePassListed, _voicePassValid, _voicePassOwned;
        private static readonly System.Diagnostics.Stopwatch FrameWatch = new System.Diagnostics.Stopwatch();
        private static float _nextDecide;
        private static float _offlineSince = -1f;
        private static Transform _localRoot, _localCamera, _worldRoot;

        public static bool Running
        {
            get { return _started; }
        }

        /// <summary>True while this client is hiding the player's objects.</summary>
        public static bool IsHidden(int playerId)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Id == playerId)
                {
                    return !Entries[i].Visible;
                }
            }

            return false;
        }

        public static int HiddenCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (!Entries[i].Visible)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public static string Describe()
        {
            return "isolation (" + (_rule != null ? _rule.Name : "not started") + "): " + Entries.Count + " tracked, " + HiddenCount + " hidden, " + _sweeps + " sweeps (last "
                   + _sweepMs.ToString("0.00") + " ms, max " + _sweepMaxMs.ToString("0.00") + " ms), slowest hide " + _hideMaxMs.ToString("0.00") + " ms, slowest frame "
                   + _tickMaxMs.ToString("0.00") + " ms"
                   + (_voicePassMs >= 0f
                       ? ", last voice pass " + _voicePassListed + " NetworkObject(s), " + _voicePassValid + " valid, " + _voicePassOwned + " owned by hidden players, "
                         + _voicePassMs.ToString("0.00") + " ms (part of the sweep)"
                       : ", no voice pass yet");
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Starts (or re-rules) the isolation. WowGame calls it once, right after the verdict.</summary>
        public static void Start(Rule rule)
        {
            if (rule == null || rule.IsWithMe == null)
            {
                throw new ArgumentException("WowIsolation.Start needs a rule with IsWithMe");
            }

            _rule = rule;
            _nextDecide = 0f;
            if (_started)
            {
                // Entries keep their state: whoever is hidden stays hidden until the new rule says "with me".
                Info("rule now '" + rule.Name + "': players " + rule.ApartText + " are hidden (after " + rule.HideDelay + " s, shown after " + rule.ShowDelay + " s "
                     + rule.WithText + ", decisions every " + rule.DecideInterval + " s); " + Entries.Count + " tracked, " + HiddenCount + " hidden");
                return;
            }

            _started = true;

            // One scene-root pass now, so the first real sweep is not also the first run of this code (cold, Rec Room
            // measured 3.9-4.0 ms, warm 0.25-0.33 ms).
            var watch = System.Diagnostics.Stopwatch.StartNew();
            CacheLocal();
            CollectRoots();
            int roots = AvatarRoots.Count + ItemRoots.Count;
            AvatarRoots.Clear();
            ItemRoots.Clear();
            Info("up (" + rule.Name + "): players " + rule.ApartText + " are hidden (after " + rule.HideDelay + " s, shown after " + rule.ShowDelay + " s " + rule.WithText
                 + "; first decision at once; renderers forceRenderingOff, tag alpha, colliders, voices volume 0, other sounds muted); warm-up scan of " + ScratchScenes.Count
                 + " scene(s) " + watch.Elapsed.TotalMilliseconds.ToString("0.00") + " ms, " + roots + " avatar/item root(s)");
        }

        /// <summary>From <see cref="WowRemotes.Tick"/>, every frame.</summary>
        internal static void Tick()
        {
            if (!_started || _rule == null)
            {
                return;
            }

            FrameWatch.Reset();
            FrameWatch.Start();
            try
            {
                float now = Time.unscaledTime;
                CacheLocal();
                if (now >= _nextDecide)
                {
                    _nextDecide = now + _rule.DecideInterval;
                    Sync();
                    DecideAll(now);
                    WatchOffline(now);
                }

                ApplyQueued(now);
                MaintainSome(now);
                SweepIfDue(now);
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3)
                {
                    WowLog.Error("Isolation: tick threw (#" + _errors + "): " + e);
                }
            }

            FrameWatch.Stop();
            _tickMs = (float)FrameWatch.Elapsed.TotalMilliseconds;
            _tickMaxMs = Mathf.Max(_tickMaxMs, _tickMs);
        }

        private static bool OverBudget
        {
            get { return FrameWatch.Elapsed.TotalMilliseconds >= FrameBudgetMs; }
        }

        /// <summary>
        /// Restores everything this client hid and forgets every player (statics survive a world re-entry). Called from
        /// <see cref="WowRemotes.Reset"/> (driver and WowGame OnDestroy); safe to call twice.
        /// </summary>
        public static void Shutdown()
        {
            int restored = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (!Entries[i].Visible || Entries[i].Touched.Count > 0)
                {
                    restored++;
                }

                Restore(Entries[i]);
            }

            if (_started)
            {
                Info("shutdown: restored " + restored + " hidden player(s); " + Describe());
            }

            Entries.Clear();
            ByRemote.Clear();
            Queue.Clear();
            AvatarRoots.Clear();
            ItemRoots.Clear();
            ScratchRoots.Clear();
            ScratchScenes.Clear();
            ScratchRenderers.Clear();
            ScratchColliders.Clear();
            ScratchAudio.Clear();
            ScratchCanvases.Clear();
            ScratchGroups.Clear();
            ScratchSkinned.Clear();
            Warned.Clear();
            _rule = null;
            _started = false;
            _stamp = 0;
            _cursor = 0;
            _errors = 0;
            _askErrors = 0;
            _changeLogs = 0;
            _sweeps = 0;
            _sweepFrame = -1;
            _lastSweep = -100f;
            _sweepMs = _sweepMaxMs = _hideMaxMs = _tickMs = _tickMaxMs = 0f;
            _voicePassMs = -1f;
            _voicePassListed = _voicePassValid = _voicePassOwned = 0;
            _nextDecide = 0f;
            _offlineSince = -1f;
            _localRoot = _localCamera = _worldRoot = null;
        }

        private static void Info(string text)
        {
            WowLog.Info("Isolation: " + text);
        }

        private static void WarnOnce(string key, string text)
        {
            if (Warned.Add(key))
            {
                WowLog.Warn("Isolation: " + text);
            }
        }

        // ------------------------------------------------------------------ decisions

        private static void CacheLocal()
        {
            _localRoot = null;
            _localCamera = null;
            _worldRoot = _rule != null ? _rule.WorldRoot : null;
            try
            {
                _localRoot = WowPlayer.Root;
                Camera cam = WowPlayer.Head();
                _localCamera = cam != null ? cam.transform : null;
            }
            catch (Exception)
            {
                // The client's player is being replaced: the guards below then refuse less, but the decision pass only
                // touches Bridge remote players.
            }
        }

        /// <summary>Mirrors WowRemotes into entries; players who left are restored (their objects are usually gone).</summary>
        private static void Sync()
        {
            _stamp++;
            var all = WowRemotes.All;
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null)
                {
                    continue;
                }

                Entry e;
                if (!ByRemote.TryGetValue(r, out e))
                {
                    e = new Entry { Remote = r };
                    ByRemote[r] = e;
                    Entries.Add(e);
                }

                e.Seen = _stamp;
                e.Id = r.Id;
                e.Nick = r.Nick;
                if (e.RawNick != r.RawNick)
                {
                    e.RawNick = r.RawNick;
                    e.AvatarUnmatchable = false;
                    e.AvatarTries = 0;
                }

                e.Root = r.Root;
            }

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (e.Seen == _stamp)
                {
                    continue;
                }

                Restore(e);
                Queue.Remove(e);
                Entries.RemoveAt(i);
                if (e.Remote != null)
                {
                    ByRemote.Remove(e.Remote);
                }
            }
        }

        private static void DecideAll(float now)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                bool want = Ask(LiveId(e));
                if (!e.Decided)
                {
                    e.Decided = true;
                    e.Want = want;
                    e.With = want;
                    e.WantSince = now;
                }
                else
                {
                    if (want != e.Want)
                    {
                        e.Want = want;
                        e.WantSince = now;
                    }

                    if (e.With != e.Want && now - e.WantSince >= (e.Want ? _rule.ShowDelay : _rule.HideDelay))
                    {
                        e.With = e.Want;
                    }
                }

                bool visible = e.With;
                if (visible != e.Visible && !e.Queued)
                {
                    e.Queued = true;
                    Queue.Add(e);
                }
            }
        }

        /// <summary>The Photon id as the Bridge says it now (the list is refreshed only once a second).</summary>
        private static int LiveId(Entry e)
        {
            ISomniumPlayer p = e.Remote != null ? e.Remote.Player : null;
            if (p == null)
            {
                return e.Id;
            }

            try
            {
                ISomniumPlayerProperties props = p.Properties;
                if (props != null)
                {
                    e.Id = props.PhotonId;
                    e.Remote.Id = e.Id;
                }
            }
            catch (Exception)
            {
                // A player object mid-change: the last id stands.
            }

            return e.Id;
        }

        /// <summary>The caller's rule; an unknown id or a throwing rule counts as apart (fails closed).</summary>
        private static bool Ask(int playerId)
        {
            if (playerId < 0)
            {
                return false;
            }

            try
            {
                return _rule.IsWithMe(playerId);
            }
            catch (Exception ex)
            {
                _askErrors++;
                if (_askErrors <= 3)
                {
                    WowLog.Error("Isolation: the rule '" + _rule.Name + "' threw for player " + playerId + " (#" + _askErrors + "; counted as apart): " + ex);
                }

                return false;
            }
        }

        private static void WatchOffline(float now)
        {
            if (_rule.Online == null)
            {
                return;
            }

            bool remotes = WowRemotes.Count > 0;
            bool online;
            try
            {
                online = _rule.Online();
            }
            catch (Exception)
            {
                online = false;
            }

            if (!remotes || online)
            {
                _offlineSince = -1f;
                return;
            }

            if (_offlineSince < 0f)
            {
                _offlineSince = now;
            }
            else if (now - _offlineSince > 20f)
            {
                WarnOnce("offline", WowRemotes.Count + " remote player(s) but " + _rule.OfflineText);
            }
        }

        // ------------------------------------------------------------------ applying

        private static void ApplyQueued(float now)
        {
            // Shows first, all of them: a restore only writes back what was recorded, and a player the rule now calls "with
            // me" must be restored in THIS frame, before WowGear's LateUpdate (see "Hand-over to Gear").
            for (int i = 0; i < Queue.Count;)
            {
                var e = Queue[i];
                if (!e.With)
                {
                    i++;
                    continue;
                }

                Queue.RemoveAt(i);
                e.Queued = false;
                if (!e.Visible)
                {
                    Show(e);
                }
            }

            int done = 0;
            while (Queue.Count > 0 && done < MaxApplyPerFrame && (done == 0 || !OverBudget))
            {
                var e = Queue[0];
                bool visible = e.With;
                if (!visible && visible != e.Visible)
                {
                    // Where the avatar and the voice are: found in one frame, hidden from the next (never both in one frame).
                    if (Time.frameCount == _sweepFrame)
                    {
                        return;
                    }

                    if (now - _lastSweep > SweepFresh)
                    {
                        Sweep(now);
                        return;
                    }
                }

                Queue.RemoveAt(0);
                e.Queued = false;
                if (visible == e.Visible)
                {
                    continue;
                }

                done++;
                if (visible)
                {
                    Show(e);
                }
                else
                {
                    Hide(e, now);
                }
            }
        }

        private static void Hide(Entry e, float now)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Resolve(e);
            Scan(e);
            e.Visible = false;
            e.NextMaintain = now + MaintainInterval;
            float ms = (float)watch.Elapsed.TotalMilliseconds;
            _hideMaxMs = Mathf.Max(_hideMaxMs, ms);
            LogChange(e, false, "; " + e.Renderers.Count + " renderer(s), " + e.Colliders.Count + " collider(s), tag group(s) " + e.Groups.Count + ", canvas(es) "
                                + e.Canvases.Count + ", voice(s) silenced " + e.Quiet.Count + ", sound(s) muted " + e.Muted.Count + ", avatar " + (e.Avatar != null ? "found" : "not found") + ", "
                                + ms.ToString("0.00") + " ms");
        }

        private static void Show(Entry e)
        {
            int n = e.Renderers.Count;
            Restore(e);
            e.Visible = true;
            LogChange(e, true, "; " + n + " renderer(s) restored");
        }

        private static void LogChange(Entry e, bool shown, string detail)
        {
            _changeLogs++;
            if (_changeLogs <= 400 || _changeLogs % 50 == 0)
            {
                Info("player " + e.Id + " (" + e.Nick + ") " + (shown ? "shown: " + _rule.WithText : "hidden: " + _rule.ApartText) + detail
                     + (_changeLogs == 400 ? " (further changes: every 50th)" : ""));
            }
        }

        /// <summary>Round-robin re-scan of hidden players: renderers, colliders and sounds the client re-enabled or made.</summary>
        private static void MaintainSome(float now)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            int done = 0;
            for (int k = 0; k < Entries.Count && done < MaxMaintainPerFrame && !OverBudget; k++)
            {
                _cursor = (_cursor + 1) % Entries.Count;
                var e = Entries[_cursor];
                if (e.Visible || now < e.NextMaintain)
                {
                    continue;
                }

                done++;
                e.NextMaintain = now + MaintainInterval;
                Resolve(e);
                Scan(e);
            }
        }

        /// <summary>Body.Root, the body prefab above it; refused when it would reach local or world objects.</summary>
        private static void Resolve(Entry e)
        {
            if (e.Remote != null)
            {
                e.Root = e.Remote.Root;
            }

            Transform body = e.Root;
            if (body != null && body.parent != null && Safe(body.parent))
            {
                body = body.parent;
            }

            e.Body = body != null && Safe(body) ? body : null;
            if (e.Avatar != null && !Safe(e.Avatar))
            {
                e.Avatar = null;
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

        /// <summary>Hides what is not hidden yet (idempotent: the first hide and every re-scan).</summary>
        private static void Scan(Entry e)
        {
            ScanTree(e, e.Body);
            ScanTree(e, e.Avatar);
            for (int i = e.Extras.Count - 1; i >= 0; i--)
            {
                if (e.Extras[i] == null)
                {
                    e.Extras.RemoveAt(i);
                    continue;
                }

                ScanTree(e, e.Extras[i]);
            }

            for (int i = 0; i < e.Voices.Count; i++)
            {
                Silence(e, e.Voices[i]);
            }

            ScanCanvases(e);
        }

        private static void ScanTree(Entry e, Transform root)
        {
            if (root == null || !Safe(root))
            {
                return;
            }

            root.GetComponentsInChildren(true, ScratchRenderers);
            for (int i = 0; i < ScratchRenderers.Count; i++)
            {
                Renderer r = ScratchRenderers[i];
                if (r != null && !r.forceRenderingOff)
                {
                    r.forceRenderingOff = true;
                    if (e.Touched.Add(r))
                    {
                        e.Renderers.Add(r);
                    }
                }
            }

            root.GetComponentsInChildren(true, ScratchColliders);
            for (int i = 0; i < ScratchColliders.Count; i++)
            {
                Collider c = ScratchColliders[i];
                if (c != null && c.enabled)
                {
                    c.enabled = false;
                    if (e.Touched.Add(c))
                    {
                        e.Colliders.Add(c);
                    }
                }
            }

            root.GetComponentsInChildren(true, ScratchAudio);
            for (int i = 0; i < ScratchAudio.Count; i++)
            {
                AudioSource a = ScratchAudio[i];
                if (a != null && (e.Voices.Contains(a) || e.Quiet.Contains(a)))
                {
                    Silence(e, a);
                }
                else
                {
                    Mute(e, a);
                }
            }

            ScratchRenderers.Clear();
            ScratchColliders.Clear();
            ScratchAudio.Clear();
        }

        /// <summary>A sound that is not a voice: mute, recorded only when this client flipped it.</summary>
        private static void Mute(Entry e, AudioSource a)
        {
            if (a != null && !a.mute)
            {
                a.mute = true;
                if (e.Touched.Add(a))
                {
                    e.Muted.Add(a);
                }
            }
        }

        /// <summary>
        /// A voice: volume 0, the client's mute/block flag never touched. The first time, the current volume is recorded;
        /// on a re-scan, a volume above 0 can only be the client's (SetVoiceLevel) and replaces the recorded one.
        /// </summary>
        private static void Silence(Entry e, AudioSource a)
        {
            if (a == null)
            {
                return;
            }

            int i = e.Quiet.IndexOf(a);
            if (i < 0)
            {
                e.Quiet.Add(a);
                e.QuietVolume.Add(a.volume);
                e.Touched.Add(a);
                a.volume = 0f;
                int m = e.Muted.IndexOf(a);
                if (m >= 0)
                {
                    // Muted by an earlier scan before it was known to be a voice (found under the body before the sweep
                    // matched it): that mute was ours (it was unmuted then), so it goes back now and the flag is the client's again.
                    e.Muted.RemoveAt(m);
                    a.mute = false;
                }

                return;
            }

            float v = a.volume;
            if (v > 0f)
            {
                e.QuietVolume[i] = v;
                a.volume = 0f;
                if (!e.LoggedLevel)
                {
                    e.LoggedLevel = true;
                    Info("player " + e.Id + " (" + e.Nick + "): voice level set to " + v.ToString("0.00") + " by the client while hidden: silenced again, "
                         + "that level comes back when they are shown");
                }
            }
        }

        /// <summary>The name tag (CanvasGroup alpha) and the other canvases next to Body.Root (Canvas.enabled).</summary>
        private static void ScanCanvases(Entry e)
        {
            Transform body = e.Body;
            if (body == null || body == e.Root)
            {
                return;
            }

            for (int i = 0; i < body.childCount; i++)
            {
                Transform child = body.GetChild(i);
                if (child == e.Root)
                {
                    continue;
                }

                bool tag = child.name.StartsWith(TagPrefix, StringComparison.Ordinal) || child.GetComponent(TagComponent) != null;
                if (tag)
                {
                    child.GetComponentsInChildren(true, ScratchGroups);
                    int groups = ScratchGroups.Count;
                    for (int g = 0; g < ScratchGroups.Count; g++)
                    {
                        HideGroup(e, ScratchGroups[g]);
                    }

                    ScratchGroups.Clear();
                    if (groups > 0)
                    {
                        continue;
                    }

                    if (!e.LoggedNoTag)
                    {
                        e.LoggedNoTag = true;
                        WowLog.Warn("Isolation: player " + e.Id + "'s name tag '" + child.name + "' has no CanvasGroup: hiding its canvases instead (the client "
                                    + "re-enables them on frustum changes, so the tag may flash until the next re-scan)");
                    }
                }

                child.GetComponentsInChildren(true, ScratchCanvases);
                for (int c = 0; c < ScratchCanvases.Count; c++)
                {
                    Canvas cv = ScratchCanvases[c];
                    if (cv != null && cv.enabled)
                    {
                        cv.enabled = false;
                        if (e.Touched.Add(cv))
                        {
                            e.Canvases.Add(cv);
                        }
                    }
                }

                ScratchCanvases.Clear();
            }
        }

        /// <summary>
        /// alpha 0 and no raycasts; the value restored is the last one seen that was not ours (the client writes the tag
        /// alpha only for a quick chat, so a write while hidden is its newest wish).
        /// </summary>
        private static void HideGroup(Entry e, CanvasGroup g)
        {
            if (g == null)
            {
                return;
            }

            int i = e.Groups.IndexOf(g);
            if (i < 0)
            {
                if (g.alpha <= 0f && !g.blocksRaycasts)
                {
                    return;
                }

                e.Groups.Add(g);
                e.GroupAlpha.Add(g.alpha);
                e.GroupBlocks.Add(g.blocksRaycasts);
                e.Touched.Add(g);
            }
            else
            {
                if (g.alpha > 0f)
                {
                    e.GroupAlpha[i] = g.alpha;
                }

                if (g.blocksRaycasts)
                {
                    e.GroupBlocks[i] = true;
                }
            }

            g.alpha = 0f;
            g.blocksRaycasts = false;
        }

        /// <summary>Puts back exactly what this client changed; objects destroyed meanwhile are skipped.</summary>
        private static void Restore(Entry e)
        {
            try
            {
                for (int i = 0; i < e.Renderers.Count; i++)
                {
                    if (e.Renderers[i] != null)
                    {
                        e.Renderers[i].forceRenderingOff = false;
                    }
                }

                for (int i = 0; i < e.Colliders.Count; i++)
                {
                    if (e.Colliders[i] != null)
                    {
                        e.Colliders[i].enabled = true;
                    }
                }

                for (int i = 0; i < e.Canvases.Count; i++)
                {
                    if (e.Canvases[i] != null)
                    {
                        e.Canvases[i].enabled = true;
                    }
                }

                for (int i = 0; i < e.Groups.Count; i++)
                {
                    if (e.Groups[i] != null)
                    {
                        e.Groups[i].alpha = e.GroupAlpha[i];
                        e.Groups[i].blocksRaycasts = e.GroupBlocks[i];
                    }
                }

                for (int i = 0; i < e.Muted.Count; i++)
                {
                    if (e.Muted[i] != null)
                    {
                        e.Muted[i].mute = false;
                    }
                }

                // Voices: only the volume goes back. A volume above 0 now was set by the client after the last re-scan and is
                // its newest level, so it stays. mute (the client's mute/block) was never written.
                for (int i = 0; i < e.Quiet.Count; i++)
                {
                    AudioSource a = e.Quiet[i];
                    if (a != null && a.volume <= 0f)
                    {
                        a.volume = e.QuietVolume[i];
                    }
                }
            }
            catch (Exception ex)
            {
                WowLog.Error("Isolation: restoring player " + e.Id + " failed: " + ex);
            }

            e.Renderers.Clear();
            e.Colliders.Clear();
            e.Canvases.Clear();
            e.Groups.Clear();
            e.GroupAlpha.Clear();
            e.GroupBlocks.Clear();
            e.Muted.Clear();
            e.Quiet.Clear();
            e.QuietVolume.Clear();
            e.LoggedLevel = false;
            e.Touched.Clear();
            e.Visible = true;
        }

        // ------------------------------------------------------------------ finding the objects that live elsewhere

        private static void SweepIfDue(float now)
        {
            if (Time.frameCount == _sweepFrame || OverBudget)
            {
                return;
            }

            bool anyHidden = false, missing = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e.Visible)
                {
                    continue;
                }

                anyHidden = true;
                if ((e.Avatar == null && !e.AvatarUnmatchable && e.AvatarTries < SweepRetries) || (e.Voices.Count == 0 && e.VoiceTries < SweepRetries))
                {
                    missing = true;
                }
            }

            if (anyHidden && now - _lastSweep >= (missing ? SweepRetry : SweepPeriod))
            {
                Sweep(now);
            }
        }

        /// <summary>
        /// One pass over the scene roots (avatars, "Simulator Item" roots) and the Fusion NetworkObjects (voices) for every
        /// player who is hidden or about to be. At most once per frame.
        /// </summary>
        private static void Sweep(float now)
        {
            _sweepFrame = Time.frameCount;
            _lastSweep = now;
            bool any = false;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].NeedsSweep)
                {
                    any = true;
                }
            }

            if (!any)
            {
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            CollectRoots();
            MatchAvatars();
            TimedVoicePass();
            MatchItems();
            _sweeps++;
            _sweepMs = (float)watch.Elapsed.TotalMilliseconds;
            _sweepMaxMs = Mathf.Max(_sweepMaxMs, _sweepMs);
            if (_sweeps == 1)
            {
                Info("first sweep: " + ScratchScenes.Count + " scene(s), " + AvatarRoots.Count + " avatar root(s), " + ItemRoots.Count + " item root(s), "
                     + _sweepMs.ToString("0.00") + " ms");
            }
        }

        private static void CollectRoots()
        {
            ScratchScenes.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                AddScene(SceneManager.GetSceneAt(i));
            }

            if (_localRoot != null)
            {
                AddScene(_localRoot.gameObject.scene);
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Root != null)
                {
                    AddScene(Entries[i].Root.gameObject.scene);
                }
            }

            AvatarRoots.Clear();
            ItemRoots.Clear();
            for (int s = 0; s < ScratchScenes.Count; s++)
            {
                ScratchRoots.Clear();
                ScratchScenes[s].GetRootGameObjects(ScratchRoots);
                for (int i = 0; i < ScratchRoots.Count; i++)
                {
                    GameObject go = ScratchRoots[i];
                    if (go == null)
                    {
                        continue;
                    }

                    string n = go.name;
                    if (n.StartsWith(AvatarPrefix, StringComparison.Ordinal))
                    {
                        int end = n.LastIndexOf("] [", StringComparison.Ordinal);
                        if (end >= AvatarPrefix.Length)
                        {
                            AvatarRoots.Add(new KeyValuePair<string, Transform>(n.Substring(AvatarPrefix.Length, end - AvatarPrefix.Length), go.transform));
                        }
                    }
                    else if (n.StartsWith(ItemPrefix, StringComparison.Ordinal))
                    {
                        ItemRoots.Add(go.transform);
                    }
                }
            }

            ScratchRoots.Clear();
        }

        private static void AddScene(Scene s)
        {
            if (s.IsValid() && s.isLoaded && !ScratchScenes.Contains(s))
            {
                ScratchScenes.Add(s);
            }
        }

        private static string LocalRawNick()
        {
            try
            {
                ISomniumPlayersContainer c = SomniumBridge.PlayersContainer;
                ISomniumPlayer local = c != null ? c.LocalPlayer : null;
                ISomniumPlayerProperties props = local != null ? local.Properties : null;
                return props != null ? props.NickName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>By the remote's raw Bridge nickname; never the local nickname, never a nickname two players share.</summary>
        private static void MatchAvatars()
        {
            string localNick = LocalRawNick();
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (!e.NeedsSweep)
                {
                    continue;
                }

                e.Avatar = null;
                if (string.IsNullOrEmpty(e.RawNick) || e.RawNick == localNick)
                {
                    NoAvatar(e, string.IsNullOrEmpty(e.RawNick) ? "the Bridge gives no nickname" : "the nickname is the local player's");
                    continue;
                }

                bool shared = false;
                for (int k = 0; k < Entries.Count; k++)
                {
                    if (k != i && Entries[k].RawNick == e.RawNick)
                    {
                        shared = true;
                    }
                }

                Transform found = null;
                int matches = 0;
                for (int k = 0; k < AvatarRoots.Count; k++)
                {
                    if (AvatarRoots[k].Key == e.RawNick)
                    {
                        matches++;
                        found = AvatarRoots[k].Value;
                    }
                }

                if (shared || matches > 1)
                {
                    NoAvatar(e, "two players share the nickname");
                    continue;
                }

                if (found == null || !Safe(found))
                {
                    e.AvatarTries++;
                    if (e.AvatarTries == SweepRetries && !e.LoggedAvatar)
                    {
                        e.LoggedAvatar = true;
                        WowLog.Warn("Isolation: player " + e.Id + " (" + e.Nick + "): no '" + AvatarPrefix + e.RawNick + "] [..]' root after " + SweepRetries + " sweeps ("
                                    + AvatarRoots.Count + " avatar root(s) seen): their avatar stays drawn");
                    }

                    continue;
                }

                e.Avatar = found;
                e.AvatarTries = 0;
            }
        }

        private static void NoAvatar(Entry e, string why)
        {
            e.AvatarUnmatchable = true;
            if (!e.LoggedAvatar)
            {
                e.LoggedAvatar = true;
                WowLog.Warn("Isolation: player " + e.Id + " (" + e.Nick + "): the avatar cannot be matched safely (" + why + "): only the body, tag and voice are hidden");
            }
        }

        /// <summary>The voice pass, timed on its own; its cost and counts are kept for <see cref="Describe"/> and logged once.</summary>
        private static void TimedVoicePass()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool first = _voicePassMs < 0f;
            MatchVoices();
            _voicePassMs = (float)watch.Elapsed.TotalMilliseconds;
            if (first)
            {
                Info("first voice pass: " + _voicePassListed + " NetworkObject(s), " + _voicePassValid + " valid, " + _voicePassOwned
                     + " owned by a hidden player, " + _voicePassMs.ToString("0.00") + " ms");
            }
        }

        /// <summary>The network part (Voice.Speaker) is a Fusion NetworkObject whose state authority is the player.</summary>
        private static void MatchVoices()
        {
            _voicePassListed = _voicePassValid = _voicePassOwned = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].NeedsSweep)
                {
                    Entries[i].Voices.Clear();
                }
            }

            Fusion.NetworkObject[] all;
            try
            {
                all = Object.FindObjectsByType<Fusion.NetworkObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }
            catch (Exception ex)
            {
                WarnOnce("find-networkobjects", "listing NetworkObjects failed: " + ex.Message + " (voices stay audible)");
                return;
            }

            _voicePassListed = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                Fusion.NetworkObject no = all[i];
                if (no == null)
                {
                    continue;
                }

                int owner;
                try
                {
                    if (!no.IsValid)
                    {
                        continue;
                    }

                    owner = no.StateAuthority.PlayerId;
                }
                catch (Exception)
                {
                    continue;
                }

                _voicePassValid++;
                Entry e = Needing(owner);
                if (e == null)
                {
                    continue;
                }

                _voicePassOwned++;

                Transform t = no.transform;
                if (!Safe(t) || OurNetObject(t))
                {
                    continue;
                }

                t.GetComponentsInChildren(true, ScratchAudio);
                for (int k = 0; k < ScratchAudio.Count; k++)
                {
                    if (ScratchAudio[k] != null && !e.Voices.Contains(ScratchAudio[k]))
                    {
                        e.Voices.Add(ScratchAudio[k]);
                    }
                }

                if (ScratchAudio.Count > 0 && !e.LoggedVoice)
                {
                    e.LoggedVoice = true;
                    Info("player " + e.Id + " (" + e.Nick + "): " + ScratchAudio.Count + " sound(s) found in network object '" + t.name + "' (first '"
                         + ScratchAudio[0].name + "')");
                }

                ScratchAudio.Clear();
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (!e.NeedsSweep)
                {
                    continue;
                }

                if (e.Voices.Count == 0)
                {
                    e.VoiceTries++;
                    if (e.VoiceTries == SweepRetries)
                    {
                        WowLog.Warn("Isolation: player " + e.Id + " (" + e.Nick + "): no sound found in their network objects after " + SweepRetries + " sweeps ("
                                    + all.Length + " NetworkObject(s)): their voice may stay audible");
                    }
                }
                else
                {
                    e.VoiceTries = 0;
                }
            }
        }

        /// <summary>This world's own scene network objects ("Wow Net" slots owned by their players, "Scene Networking").</summary>
        private static bool OurNetObject(Transform t)
        {
            Transform root = t.root;
            string n = root != null ? root.name : "";
            return n == OwnNetRoot || n == OwnNetworkingRoot || t.name.StartsWith(OwnNetRoot, StringComparison.Ordinal);
        }

        private static Entry Needing(int playerId)
        {
            if (playerId < 0)
            {
                return null;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e.Id == playerId && e.NeedsSweep)
                {
                    return e;
                }
            }

            return null;
        }

        /// <summary>A "Simulator Item" root skinned to a hidden avatar's bones (cloth), or owned by a hidden player.</summary>
        private static void MatchItems()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].NeedsSweep)
                {
                    Entries[i].Extras.Clear();
                }
            }

            for (int i = 0; i < ItemRoots.Count; i++)
            {
                Transform item = ItemRoots[i];
                if (item == null || !Safe(item))
                {
                    continue;
                }

                Entry owner = null;
                item.GetComponentsInChildren(true, ScratchSkinned);
                for (int k = 0; k < ScratchSkinned.Count && owner == null; k++)
                {
                    SkinnedMeshRenderer smr = ScratchSkinned[k];
                    if (smr == null)
                    {
                        continue;
                    }

                    Transform bone = smr.rootBone;
                    if (bone == null)
                    {
                        Transform[] bones = smr.bones;
                        bone = bones != null && bones.Length > 0 ? bones[0] : null;
                    }

                    owner = bone != null ? OwnerOfBone(bone) : null;
                }

                ScratchSkinned.Clear();
                if (owner == null)
                {
                    try
                    {
                        Fusion.NetworkObject no = item.GetComponentInChildren<Fusion.NetworkObject>(true);
                        if (no != null && no.IsValid)
                        {
                            owner = Needing(no.StateAuthority.PlayerId);
                        }
                    }
                    catch (Exception)
                    {
                        owner = null;
                    }
                }

                if (owner != null && !owner.Extras.Contains(item))
                {
                    owner.Extras.Add(item);
                }
            }
        }

        private static Entry OwnerOfBone(Transform bone)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e.NeedsSweep && e.Avatar != null && bone.IsChildOf(e.Avatar))
                {
                    return e;
                }
            }

            return null;
        }
    }
}
