using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Entities;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Draws every IWowSession entity (not the player) as its WoW model in the world: animated, on the ground, facing,
    /// smoothed between sim updates, with WoW's nameplates, the selection circle under the target, quest markers, loot
    /// sparkle and floating combat text, and a click box per entity (layer 16) for pointing/clicking (desktop and VR) and
    /// the Gear owner's weapon hits. Clicking selects the target; clicking the target again talks to an NPC / loots a corpse
    /// (IWowSession.Interact; the session answers "You are too far away" itself).
    /// The front end adds this component to the WoW Game object; it follows WowPlay.Started / Ended / Event and reads
    /// WowPlay.Session every frame. Models are built over frames (nearest first, a small budget) after a worker thread has
    /// parsed every model the zone needs. Signatures of TryGetGuid / GetView stay (Gear and UI call them).
    /// Iteration 6 (online, AzerothCore): entities arrive all session long, hundreds at once in a city - one art worker lives for the
    /// session and prepares nearest-first; models are built within DrawRange only, nearest first under a per-frame budget, hidden
    /// beyond it, animated only on screen; despawned views are parked (at most MaxParked). Other WoW players are drawn as their
    /// characters (WowCharacter body from the server's look fields, weapons by sheath state, mounts), gameobjects as their
    /// GameObjectDisplayInfo model (full rotation, doors open/closed; a click uses them), and a Somnium player's own WoW character
    /// (EntityState.IsSomniumPlayer or WowGear.IsSomniumCharacter) is not drawn - their avatar is there - but keeps its click box.
    /// NPC stand states (sit, sleep, kneel, dead) and emote states (working, fishing) play their looping sequences.
    /// </summary>
    public sealed class WowEntityViews : MonoBehaviour
    {
        private static WowEntityViews _instance;
        private static readonly Dictionary<Collider, ulong> ColliderGuids = new Dictionary<Collider, ulong>();

        private const float PlateRange = 40f;          // metres: nameplates beyond this only for the target
        private const float MarkerGlyphBottomYd = 0.16f; // TalkToMe / TalkToMeQuestionMark: lowest glyph vertex at rest (harness EntTops)
        private const float ParkSeconds = 180f;        // a despawned entity's view waits this long for its respawn
        private const float BuildBudgetMs = 4f;        // model builds per frame: at least one, more while under this
        private const int CombatTextPool = 32;
        // Iteration 6 (online: hundreds of units, players and gameobjects in range, arriving all session long):
        private const float DrawRange = 160f;          // metres: models beyond are hidden and not built (AC visibility: 90 yd on continents, 170 yd in instances)
        private const int MaxParked = 150;             // despawned views kept for a respawn; the oldest go first beyond this
        private const float ArtWaitSeconds = 10f;      // a model whose art the worker has not delivered by then is built anyway (reads files here)
        private const int Candidates = 8;              // nearest unbuilt views checked for readiness each frame
        private const int RequeueFrames = 20;          // every unbuilt view re-asks the worker (with its distance) this often
        private const float StatsEvery = 60f;

        private IWowSession _session;
        private WowFiles _files;
        private int _mapId;
        private Transform _container;
        private WowEntityArt _art;
        private WowEntitySounds _sounds;
        private WowCombatText _text;
        private WowSelectionCircle _circle;
        private readonly Dictionary<ulong, WowEntityView> _views = new Dictionary<ulong, WowEntityView>();
        private readonly List<WowEntityView> _list = new List<WowEntityView>();
        private readonly Stopwatch _build = new Stopwatch();
        private bool _artStarted, _subscribed, _inputBroken, _allBuiltLogged;
        private float _begunAt;
        private int _builtModels, _failedModels, _builtCharacters, _builtObjects;
        private float _slowestBuildMs, _firstBuildAt = -1f;
        private ulong _lastClickGuid;
        private float _lastClickTime = -10f;
        private ClickKind _lastClickKind;

        /// <summary>The entity under the desktop pointer / VR ray (0 = none).</summary>
        public static ulong HoveredGuid { get; private set; }

        // ------------------------------------------------------------------ contract (signatures stay)

        /// <summary>The entity guid a collider belongs to (weapon swing hits, pointer clicks). False when not an entity.</summary>
        /// <summary>Iteration 7 (lead contract, PLAN7 s3.7; owner Remote): asks the entity art worker for a creature display's
        /// textures (WowEntityArt.WantDisplay; priority = distance squared, the nearest first). True once it is ready to build without
        /// a hitch. Also true when there is no art worker to ask (no session's entity views, no game files): build now, as before.</summary>
        public static bool PrewarmDisplay(int displayId, float priority)
        {
            if (displayId <= 0) return true;
            var me = _instance;
            WowEntityArt art = me != null ? me._art : null;
            if (art == null) return true;
            return art.WantDisplay(displayId, priority);
        }

        public static bool TryGetGuid(Collider collider, out ulong guid)
        {
            guid = 0;
            if ((object)collider == null || collider == null) return false;
            if (!ColliderGuids.TryGetValue(collider, out guid)) return false;
            if (!collider.gameObject.activeInHierarchy) { guid = 0; return false; }
            return true;
        }

        /// <summary>The root transform of an entity's model, or null (for UI anchoring).</summary>
        public static Transform GetView(ulong guid)
        {
            var me = _instance;
            if (me == null) return null;
            WowEntityView v;
            if (!me._views.TryGetValue(guid, out v) || v.Parked || v.Root == null) return null;
            return v.Root.transform;
        }

        // ------------------------------------------------------------------ additions

        /// <summary>
        /// An M2 attachment point on an entity's model (spell effect origins and impacts; ids in reports/models.md, e.g.
        /// 1 right hand, 2 left hand), or null when the entity has no built model or no such attachment.
        /// </summary>
        public static Transform GetAttachment(ulong guid, int attachmentId)
        {
            var root = GetView(guid);
            if (root == null) return null;
            var anim = root.GetComponentInChildren<M2Animator>();
            return anim != null ? anim.Attachment(attachmentId) : null;
        }

        /// <summary>A world point just above the entity's model (speech bubbles, UI anchors). False when no view.</summary>
        public static bool TryGetTop(ulong guid, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            var me = _instance;
            WowEntityView v;
            if (me == null || !me._views.TryGetValue(guid, out v) || v.Parked || v.Root == null) return false;
            worldPos = v.Root.transform.position + Vector3.up * v.Top;
            return true;
        }

        /// <summary>The model's height and footprint diameter in metres (defaults until the model is built). False when no view.</summary>
        public static bool TryGetSize(ulong guid, out float height, out float footprint)
        {
            height = 0f;
            footprint = 0f;
            var me = _instance;
            WowEntityView v;
            if (me == null || !me._views.TryGetValue(guid, out v) || v.Parked) return false;
            height = v.Top;
            footprint = v.Footprint;
            return true;
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            _instance = this;
            if (!_subscribed)
            {
                WowPlay.Started += OnStarted;
                WowPlay.Ended += OnEnded;
                WowPlay.Event += OnEvent;
                WowSettings.Changed += OnSettingsChanged;
                WowSpace.Rebased += OnRebased;
                _subscribed = true;
            }
            if (WowPlay.Session != null && _session == null) Begin(WowPlay.Session);
        }

        /// <summary>Floating origin: WowOrigin moved the view roots (children of the zero-position "WoW Entities" container);
        /// the smoothing state kept in Unity metres moves with them, so nothing glides or snaps.</summary>
        private void OnRebased(Vector3 d)
        {
            foreach (var v in _list) v.OnRebased(d);
            if (_text != null) _text.OnRebased(d);
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                WowPlay.Started -= OnStarted;
                WowPlay.Ended -= OnEnded;
                WowPlay.Event -= OnEvent;
                WowSettings.Changed -= OnSettingsChanged;
                WowSpace.Rebased -= OnRebased;
                _subscribed = false;
            }
            End();
            if (_instance == this) _instance = null;
        }

        private void OnStarted(IWowSession session)
        {
            if (!isActiveAndEnabled) return;
            End();
            Begin(session);
        }

        /// <summary>WowSettings changed (UI): plates, markers and names are read every frame; floating numbers go at once when turned off.</summary>
        private void OnSettingsChanged()
        {
            if (!WowSettings.ShowCombatText && _text != null) _text.Clear();
        }

        private void OnEnded()
        {
            End();
        }

        private void Begin(IWowSession session)
        {
            if (session == null) return;
            _session = session;
            _files = WowPlay.Files;
            _mapId = session.MapId;
            _begunAt = Time.time;
            _artStarted = false;
            _allBuiltLogged = false;
            _builtModels = _failedModels = 0;
            _slowestBuildMs = 0f;
            _firstBuildAt = -1f;
            _dialogGuid = 0;
            _nextStats = Time.time + 20f;
            _builtCharacters = _builtObjects = 0;
            _loggedTops.Clear();
            var go = new GameObject("WoW Entities");
            _container = go.transform;
            _container.SetParent(transform, false);
            _art = new WowEntityArt(_files);
            _sounds = new WowEntitySounds(_files);
            _text = new WowCombatText(_container, _art, CombatTextPool);
            _circle = new WowSelectionCircle(_container, null);
            WowLog.Info("Entities: session on map " + _mapId + (_files == null ? " (no game files: nameplates and click boxes only)" : "") + ", " + session.Entities.Count + " entities listed");
        }

        private void End()
        {
            if (_session == null && _container == null) return;
            foreach (var v in _list) Forget(v);
            _list.Clear();
            _views.Clear();
            ColliderGuids.Clear();
            _ghostBoxes.Clear();                     // rescorpse: their GameObjects go with the container below
            HoveredGuid = 0;
            if (_art != null) _art.Dispose();
            if (_sounds != null) _sounds.Dispose();
            _sounds = null;
            _art = null;
            if (_text != null) _text.Destroy();
            _text = null;
            if (_circle != null) _circle.Destroy();
            _circle = null;
            if (_container != null) Destroy(_container.gameObject);
            _container = null;
            if (_session != null) WowLog.Info("Entities: session ended, views destroyed (" + _builtModels + " models were built)");
            _session = null;
        }

        private void Forget(WowEntityView v)
        {
            if (v.Click != null && v.Click.Box != null) ColliderGuids.Remove(v.Click.Box);
            if (v.Root != null) Destroy(v.Root);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            var s = _session;
            if (s == null) return;
            if (!ReferenceEquals(s, WowPlay.Session)) { End(); if (WowPlay.Session != null) Begin(WowPlay.Session); return; }
            float dt = Time.deltaTime, now = Time.time;
            int frame = Time.frameCount;
            var list = s.Entities;
            if (list == null) return;

            if (!_artStarted && (list.Count > 0 || now - _begunAt > 1.5f)) StartArt(list);
            if (_art != null) _art.Pump(2f);
            if (_sounds != null && _art != null && _art.Ready) _sounds.Pump(1f);
            if (_art != null && _art.SelectCircle != null && _circle != null) _circle.SetTexture(_art.SelectCircle);
            UpdateDialog(s, now);
            var mp = s.Player != null ? s.Player.Pos : new WowPos();
            Vector3 mePos = WowSpace.ToUnity(_mapId, mp.X, mp.Y, mp.Z);
            // Iteration 6 (integrator wiring): online, tell the session which players are Somnium players' WoW characters (their
            // Gear record says it; their avatar is drawn instead). Both ways, so a character is drawn again once its owner leaves.
            var online = s as SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession;

            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null) continue;
                if (online != null && e.Kind == EntityKind.Player)
                {
                    bool somniumChar = WowGear.IsSomniumCharacter(e.Guid, e.Name);
                    if (somniumChar != e.IsSomniumPlayer) online.MarkSomniumPlayer(e.Guid, somniumChar);
                }
                WowEntityView v;
                if (!_views.TryGetValue(e.Guid, out v))
                {
                    v = new WowEntityView(e, _container, this, _art);
                    _views[e.Guid] = v;
                    _list.Add(v);
                    if (v.Click != null && v.Click.Box != null) ColliderGuids[v.Click.Box] = e.Guid;
                }
                else if (v.Parked)
                {
                    v.Parked = false;
                    v.State = e;
                    v.Root.SetActive(true);
                    v.ResetForRespawn();
                }
                v.State = e;
                v.Seen = frame;
                if (v.Stale(_art)) { v.DestroyModel(); v.Sounds = null; }   // display / look / Somnium flag changed: rebuild
                v.UpdatePose(e, _mapId, dt);
                if (v.Sounds == null && _sounds != null && e.Kind != EntityKind.GameObject && e.Kind != EntityKind.Player)
                {
                    v.Sounds = _sounds.For(e.DisplayId);
                    if (v.Sounds == null && _artStarted) _sounds.Add(e.DisplayId);
                }
                v.UpdateGear();
                v.SyncMount(_files, _art, (v.Pos - mePos).sqrMagnitude, now);
                v.Animate(e, _files, dt, now);
                v.UpdateMarker(_files);
            }

            int parked = 0, oldest = -1;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var v = _list[i];
                if (v.Seen == frame) continue;
                if (!v.Parked)
                {
                    v.Parked = true;
                    v.ParkedAt = now;
                    v.Root.SetActive(false);
                    v.ReleaseArt();
                    if (HoveredGuid == v.Guid) HoveredGuid = 0;
                }
                else if (now - v.ParkedAt > ParkSeconds)
                {
                    Forget(v);
                    _views.Remove(v.Guid);
                    _list.RemoveAt(i);
                    continue;
                }
                parked++;
                if (oldest < 0 || v.ParkedAt < _list[oldest].ParkedAt) oldest = i;
            }
            // Online, units leave visibility all the time (SMSG_DESTROY_OBJECT / out of range): keep only the newest MaxParked.
            if (parked > MaxParked && oldest >= 0)
            {
                var v = _list[oldest];
                Forget(v);
                _views.Remove(v.Guid);
                _list.RemoveAt(oldest);
            }

            UpdateGhostBoxes(s);
            BuildSome(s, now);
            DesktopClickFallback();
        }

        // ------------------------------------------------------------------ a Somnium ghost stands for its corpse (rescorpse 2026-10-04)

        private readonly Dictionary<ulong, WowEntityClick> _ghostBoxes = new Dictionary<ulong, WowEntityClick>();   // corpse guid -> a box on its owner's avatar
        private readonly List<ulong> _ghostGone = new List<ulong>();
        private int _ghostLogged;

        /// <summary>
        /// Ours (not WoW's): a released player is a ghost, invisible to the living (AC AuraEffect::HandleAuraGhost), so the server drops their
        /// player object and its click box goes with it - but a Somnium player's ghost AVATAR still walks here. While their corpse is in
        /// view (EntityState.CorpseOwnerGuid) and their unit is not, a click box follows that avatar (WowGear.TryRiderAnchor) and a click /
        /// the VR ray on it selects the CORPSE - the target a resurrection needs (WoW itself only lets you aim at the corpse).
        /// </summary>
        private void UpdateGhostBoxes(IWowSession s)
        {
            var list = s.Entities;
            ulong me = s.Player != null ? s.Player.Guid : 0;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || e.CorpseOwnerGuid == 0 || e.CorpseOwnerGuid == me) continue;
                Vector3 feet = Vector3.zero; float seat = 0f, yaw = 0f;
                bool show = s.Find(e.CorpseOwnerGuid) == null && WowGear.TryRiderAnchor(e.CorpseOwnerGuid, null, out feet, out seat, out yaw);
                WowEntityClick box;
                _ghostBoxes.TryGetValue(e.Guid, out box);
                if (!show)
                {
                    if (box != null && box.gameObject.activeSelf) box.gameObject.SetActive(false);
                    continue;
                }
                if (box == null)
                {
                    var go = new GameObject("Ghost click box " + e.Guid.ToString("X"));
                    go.transform.SetParent(_container, false);
                    box = WowEntityClick.Create(go, e.Guid, this, new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 1.8f, 0.9f));
                    _ghostBoxes[e.Guid] = box;
                    if (box.Box != null) ColliderGuids[box.Box] = e.Guid;
                    if (++_ghostLogged <= 5)
                        WowLog.Info("Entities: the ghost avatar of a Somnium player (owner " + e.CorpseOwnerGuid.ToString("X") + ") walks here while '" + e.Name +
                                    "' lies at its spot - a click on the avatar selects that corpse (ours: WoW aims a resurrection at the corpse; logged 5 times)");
                }
                if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);
                box.transform.SetPositionAndRotation(feet + Vector3.up * seat, Quaternion.Euler(0f, yaw, 0f));
            }
            if (_ghostBoxes.Count == 0) return;
            _ghostGone.Clear();
            foreach (var kv in _ghostBoxes) if (s.Find(kv.Key) == null || kv.Value == null) _ghostGone.Add(kv.Key);
            for (int i = 0; i < _ghostGone.Count; i++)
            {
                WowEntityClick box;
                if (_ghostBoxes.TryGetValue(_ghostGone[i], out box) && box != null)
                {
                    if (box.Box != null) ColliderGuids.Remove(box.Box);
                    Destroy(box.gameObject);
                }
                _ghostBoxes.Remove(_ghostGone[i]);
            }
        }

        private ulong _dialogGuid;
        private readonly HashSet<int> _loggedTops = new HashSet<int>();

        /// <summary>
        /// The player's NPC dialog (IWowSession.CurrentDialog): when it opens the NPC plays WoW's talk emote once (EmoteTalk, when
        /// its model has it) and turns to face the player while it stays open; it returns to its own facing when it closes.
        /// </summary>
        private void UpdateDialog(IWowSession s, float now)
        {
            var dlg = s.CurrentDialog;
            ulong dg = dlg != null ? dlg.NpcGuid : 0;
            WowEntityView v;
            if (dg != _dialogGuid)
            {
                if (_dialogGuid != 0 && _views.TryGetValue(_dialogGuid, out v)) v.Facing = false;
                _dialogGuid = dg;
                if (dg != 0 && _views.TryGetValue(dg, out v) && !v.Parked)
                {
                    v.Talk(_files, now);
                    WowLog.Info("Entities: dialog with " + (v.State != null ? v.State.Name : "?") + " - talk emote" + (v.Anim != null && v.Anim.Has(60) ? "" : " (model has none)") + ", facing the player");
                }
            }
            if (dg == 0 || s.Player == null || !_views.TryGetValue(dg, out v)) return;
            var pp = s.Player.Pos;
            v.Facing = true;
            v.FacePoint = WowSpace.ToUnity(_mapId, pp.X, pp.Y, pp.Z);
        }

        private void StartArt(IReadOnlyList<EntityState> list)
        {
            _artStarted = true;
            var displays = new HashSet<int>();
            var weapons = new HashSet<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null) continue;
                if (e.DisplayId > 0) displays.Add(e.DisplayId);
                if (e.MainHandDisplayId > 0) weapons.Add(e.MainHandDisplayId);
                if (e.OffHandDisplayId > 0) weapons.Add(e.OffHandDisplayId);
            }
            // Displays the player's class summons later, prepared now with the rest (no hitch on the first summon): the Warlock's
            // Imp 4449 / Voidwalker 1132 and the Mage's Polymorph sheep 856 (ids quoted by Rules: OfflineSession.Summons, reports3/rules.md).
            var pl = _session != null ? _session.Player : null;
            if (pl != null && pl.Class == WowClass.Warlock) { displays.Add(4449); displays.Add(1132); }
            if (pl != null && pl.Class == WowClass.Mage) displays.Add(856);
            var d = new int[displays.Count];
            displays.CopyTo(d);
            var w = new int[weapons.Count];
            weapons.CopyTo(w);
            _art.Start(d, w);
            if (_sounds != null) _sounds.Start(d);
            WowLog.Info("Entities: preparing " + d.Length + " creature displays and " + w.Length + " held items on a worker thread");
        }

        private readonly WowEntityView[] _cand = new WowEntityView[Candidates];
        private readonly float[] _candD = new float[Candidates];

        /// <summary>
        /// Builds models nearest-first within DrawRange: at least one per frame, more while under BuildBudgetMs. A view is built once
        /// the art worker has prepared what it needs (so the main thread only uploads/assembles), or ArtWaitSeconds after it asked.
        /// Every RequeueFrames frames all unbuilt views re-ask the worker with their current distance (the worker takes the nearest).
        /// </summary>
        private void BuildSome(IWowSession s, float now)
        {
            if (_art == null) return;
            var pp = s.Player != null ? s.Player.Pos : new WowPos();
            Vector3 me = WowSpace.ToUnity(_mapId, pp.X, pp.Y, pp.Z);
            float range2 = DrawRange * DrawRange;
            bool requeue = Time.frameCount % RequeueFrames == 0;
            int nc = 0;
            for (int i = 0; i < _list.Count; i++)
            {
                var v = _list[i];
                if (v.ModelTried || v.Parked) continue;
                float d = (v.Pos - me).sqrMagnitude;
                if (d > range2) continue;
                if (requeue) v.ArtReady(_art, d, now);
                // Keep the Candidates nearest (insertion into a small sorted array).
                if (nc < Candidates) nc++;
                else if (d >= _candD[nc - 1]) continue;
                int k = nc - 1;
                while (k > 0 && _candD[k - 1] > d) { _cand[k] = _cand[k - 1]; _candD[k] = _candD[k - 1]; k--; }
                _cand[k] = v;
                _candD[k] = d;
            }
            _build.Reset();
            _build.Start();
            int built = 0;
            for (int c = 0; c < nc && built < 4; c++)
            {
                var best = _cand[c];
                _cand[c] = null;
                bool ready = best.ArtReady(_art, _candD[c], now);
                if (!ready && now - best.ArtAskedAt < ArtWaitSeconds) continue;
                if (built > 0 && _build.Elapsed.TotalMilliseconds >= BuildBudgetMs) break;
                float t0 = (float)_build.Elapsed.TotalMilliseconds;
                if (_firstBuildAt < 0f) _firstBuildAt = now;
                best.BuildModel(_files, _art);
                best.MarkBuilt(now);
                if (best.Model != null && best.BuiltLook == WowEntityView.Look.Creature && _loggedTops.Count < 80 && _loggedTops.Add(best.BuiltDisplayId))
                    WowLog.Info("Entities: head of " + best.State.Name + " (display " + best.BuiltDisplayId + ", "
                        + (best.Anim != null && best.Anim.Model != null ? System.IO.Path.GetFileName(best.Anim.Model.Path ?? "") : "?") + " x" + best.TotalScale.ToString("0.##")
                        + "): " + best.Top.ToString("0.00") + " m from the " + best.TopSource + " (bind-pose bounds " + best.BoundsTop.ToString("0.00") + " m)");
                float ms = (float)_build.Elapsed.TotalMilliseconds - t0;
                if (ms > _slowestBuildMs) _slowestBuildMs = ms;
                if (best.Model != null) { _builtModels++; if (best.BuiltLook == WowEntityView.Look.Character) _builtCharacters++; else if (best.BuiltLook == WowEntityView.Look.GameObject) _builtObjects++; }
                else if (_files != null && best.ModelTried && best.BuiltLook != WowEntityView.Look.Somnium && best.State.DisplayId > 0) _failedModels++;
                built++;
                if (_build.Elapsed.TotalMilliseconds >= BuildBudgetMs) break;
            }
            _build.Stop();

            if (built > 0 || _allBuiltLogged) return;
            for (int i = 0; i < _list.Count; i++) if (!_list[i].ModelTried && !_list[i].Parked && (_list[i].Pos - me).sqrMagnitude <= range2) return;
            if (_list.Count == 0) return;
            _allBuiltLogged = true;
            WowLog.Info("Entities: " + _list.Count + " views, " + _builtModels + " models built, " + _failedModels + " failed, in "
                + (_firstBuildAt >= 0f ? (now - _firstBuildAt).ToString("0.0") : "0") + " s after the art was ready (" + (now - _begunAt).ToString("0.0")
                + " s after the session started); slowest build " + _slowestBuildMs.ToString("0") + " ms. " + M2Factory.StatsLine());
        }

        private void LateUpdate()
        {
            var s = _session;
            if (s == null || _container == null) return;
            var cam = WowPlayer.Head();
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            ulong target = s.TargetGuid;
            float now = Time.time;
            WowEntityView targetView = null;
            bool artReady = _art != null && _art.Ready;
            int created = 0, animating = 0, hidden = 0;

            for (int i = 0; i < _list.Count; i++)
            {
                var v = _list[i];
                if (v.Parked) continue;
                var e = v.State;
                bool isTarget = v.Guid == target && target != 0;
                if (isTarget) targetView = v;
                Vector3 feet = v.Root.transform.position;
                float dist = cam != null ? Vector3.Distance(camPos, feet) : 10f;
                v.UpdateLod(dist, DrawRange);
                if (v.Model != null) { if (v.Anim != null && v.Anim.enabled) animating++; if (dist > DrawRange) hidden++; }

                // Settings: NPCs, pets, totems and other friendly units follow ShowFriendlyNames, hostile/neutral creatures
                // ShowNameplates; the target and the unit under the pointer always keep theirs (like WoW's mouseover name).
                // Iteration 6: a Somnium player's character has no plate here (Gear draws theirs over the avatar); a gameobject shows its
                // name only under the pointer (WoW's tooltip), never a bar.
                bool isObject = e.Kind == EntityKind.GameObject;
                bool friendly = e.Kind == EntityKind.Npc || e.Kind == EntityKind.Pet || e.Kind == EntityKind.Totem || e.Reaction == Reaction.Friendly;
                bool allowed = friendly ? WowSettings.ShowFriendlyNames : WowSettings.ShowNameplates;
                bool showPlate = isTarget || (v.Hovered && !e.Dead) || (allowed && !e.Dead && dist < PlateRange);
                if (isObject) showPlate = v.Hovered;
                if (e.IsSomniumPlayer || (v.ModelTried && v.BuiltLook == WowEntityView.Look.Somnium)) showPlate = false;
                // lead fix 2026-10-14 (the user: "I see things that are not actually there, but are clickable, stuff like 'watcher' ... they
                // block clicking on things like gryphon masters"): a unit WoW never lets you click (UNIT_FLAG_NOT_SELECTABLE) or whose model
                // draws nothing (Creature\InvisibleStalker, 0 batches - the triggers and stalkers) has no name and no click box
                if (v.Model != null && !ReferenceEquals(v.InvisibleFor, v.Model)) { v.InvisibleFor = v.Model; v.Invisible = v.Model.GetComponentInChildren<Renderer>(true) == null; }
                bool phantom = e.Kind != EntityKind.Player && (e.NotSelectable || (v.Model != null && v.Invisible));
                if (phantom) showPlate = false;
                if (v.Click != null && v.Click.Box != null && v.Click.Box.enabled == phantom) v.Click.Box.enabled = !phantom;
                // Canvases are made a few per frame (not all 80 in the first frame), once the plate art is uploaded.
                if (showPlate && v.Plate == null && artReady && created < 6)
                {
                    v.Plate = new WowNameplate(v.Root.transform, _art, e.Kind == EntityKind.Npc || isObject);
                    created++;
                }
                if (v.Plate != null) v.Plate.SetVisible(showPlate);
                // The head: the standing-pose top of this model (a corpse lies down: half of it).
                v.PlaceRider();
                float head = (e.Dead ? v.Top * 0.5f : v.Top) + v.RiderLift;
                float plateTop = head;
                if (showPlate && v.Plate != null)
                {
                    v.Plate.Refresh(e, isTarget || v.Hovered, v.PlateName());
                    v.Plate.Alpha = isTarget || v.Hovered ? 1f : 0.85f;
                    // The plate's pivot is its bottom edge: it sits a constant angle above the head and grows upward with
                    // distance, so it never reaches into the head nor drifts away from it.
                    Vector3 head0 = feet + Vector3.up * head;
                    float d = cam != null ? Vector3.Distance(camPos, head0) : 10f;
                    Vector3 anchor = head0 + Vector3.up * WowNameplate.GapFor(d);
                    v.Plate.Place(anchor, camPos, d);
                    plateTop = anchor.y - feet.y + v.Plate.VisibleUnits * WowNameplate.ScaleFor(d);
                }
                if (v.MarkerGo != null)
                {
                    bool showMarker = WowSettings.ShowQuestMarkers;
                    if (v.MarkerGo.activeSelf != showMarker) v.MarkerGo.SetActive(showMarker);
                    if (showMarker)
                    {
                        float ms = 0.4f * Mathf.Max(1f, dist / 6f);   // constant angle beyond 6 m (lab e3: at 13 m the "!" was 18 px, hard to see)
                        v.MarkerGo.transform.localScale = new Vector3(ms, ms, ms);
                        // The glyph spans z 0.16..0.72 yd at rest (harness EntTops on TalkToMe*.m2; the header bounds -0.5..1.23 are wider):
                        // its lowest point sits 3 cm over the plate's visible top (or the head).
                        v.MarkerGo.transform.position = feet + Vector3.up * (plateTop + 0.03f - MarkerGlyphBottomYd * WowSpace.MetresPerYard * ms);
                    }
                }
                bool sparkle = e.Lootable && (e.Dead || e.Kind == EntityKind.GameObject);   // iteration 11 (quests H-E1): quest objects sparkle (GAMEOBJECT_DYNAMIC 0x08)
                if (sparkle && v.Sparkle == null && artReady) v.Sparkle = new WowSparkle(v.Root.transform, _art.Sparkle);
                if (v.Sparkle != null)
                {
                    v.Sparkle.SetVisible(sparkle);
                    if (sparkle) v.Sparkle.Tick(feet + Vector3.up * 0.45f, camPos, now);
                }
            }

            // Iteration 7: a Somnium player's character whose avatar is not drawn here shows no circle either (WowEntityView.AnchorHidden).
            if (targetView != null && _circle != null && targetView.State.Kind != EntityKind.GameObject && !targetView.AnchorHidden)
            {
                var te = targetView.State;
                Color c = te.Dead ? WowEntityColors.Dead : WowEntityColors.For(te);
                _circle.Show(targetView.Root.transform.position, targetView.Footprint, c, camPos);
            }
            else if (_circle != null) _circle.Hide();

            if (_text != null) _text.Tick(Time.deltaTime, cam);

            if (now >= _nextStats && _list.Count > 0)
            {
                _nextStats = now + StatsEvery;
                int parked = 0, built = 0, players = 0, objects = 0, somnium = 0;
                for (int i = 0; i < _list.Count; i++)
                {
                    var v = _list[i];
                    if (v.Parked) { parked++; continue; }
                    if (v.Model != null) built++;
                    var k = v.State.Kind;
                    if (k == EntityKind.Player) { players++; if (v.State.IsSomniumPlayer) somnium++; }
                    else if (k == EntityKind.GameObject) objects++;
                }
                WowLog.Info("Entities: " + (_list.Count - parked) + " in range (" + players + " players, " + somnium + " of them Somnium avatars, " + objects
                    + " gameobjects), " + built + " models drawn, " + hidden + " hidden beyond " + DrawRange.ToString("0") + " m, " + animating + " animating, "
                    + parked + " parked; built so far " + _builtModels + " (" + _builtCharacters + " characters, " + _builtObjects + " gameobjects), " + _failedModels
                    + " failed; " + (_art != null ? _art.StatsLine() : "no art"));
            }
        }

        private float _nextStats;

        // ------------------------------------------------------------------ input

        /// <summary>
        /// Stage O2 (integrator, combat request to quests): a second click on a targeted, living, non-friendly creature interacts with
        /// it, and both sessions' Interact start the attack (OfflineSession.Quests.cs / OnlineSession.Quests.cs) - WoW's right-click.
        /// Lead fix 2026-10-26 (user: "some hostile NPCs remove the immersive striking ring"): a HOSTILE unit with npc flags (gossip, vendor,
        /// quest giver - Kind Npc online) is attacked too, as OnlineSession.Interact already does; neutral flagged NPCs still talk.
        /// </summary>
        internal static bool AttackOnClick(IWowSession s, EntityState e)
        {
            if (e == null || s == null || s.Player == null || s.Player.Dead || e.Dead) return false;
            return (e.Kind == EntityKind.Creature && e.Reaction != Reaction.Friendly) || (e.Kind == EntityKind.Npc && e.Reaction == Reaction.Hostile);
        }

        /// <summary>How a click reached an entity. Lead 2026-09-27 (the user: "it is right click to start attacking, not left click", "right
        /// click to loot too", "right click to speak to people"): WoW's mouse on the desktop - left = select, right = select and interact
        /// (attack, talk, loot, use an object). VR has one trigger: the first press selects, a second on the target interacts (unchanged).</summary>
        internal enum ClickKind { Select, Interact, Vr }

        internal void OnEntityClicked(ulong guid, string how, ClickKind kind)
        {
            if (Ui.UiCursor.TakesWorldClicks) return;   // iteration 11 (uicore H5): the click belongs to the item / spell / targeting on the cursor
            var s = _session;
            if (s == null) return;
            float now = Time.unscaledTime;
            if (guid == _lastClickGuid && now - _lastClickTime < 0.35f && kind == _lastClickKind) return;   // one press seen by two paths
            _lastClickGuid = guid;
            _lastClickTime = now;
            _lastClickKind = kind;
            var e = s.Find(guid);
            if (e == null) return;
            try
            {
                if (e.Kind == EntityKind.GameObject)
                {
                    // WoW never targets a gameobject: a right-click (VR: the trigger) uses it (a door, a chest, a mailbox, a quest object).
                    if (kind == ClickKind.Select) return;
                    WowLog.Info("Entities: " + how + " on gameobject " + e.Name + " -> use");
                    s.Interact(guid);
                    return;
                }
                bool wasTarget = s.TargetGuid == guid;
                if (!wasTarget)
                {
                    s.SetTarget(guid);
                    WowLog.Info("Entities: " + how + " on " + e.Name + " -> target");
                }
                if (kind == ClickKind.Interact || (kind == ClickKind.Vr && wasTarget))
                {
                    if (e.Kind == EntityKind.Npc || (e.Dead && e.Lootable) || AttackOnClick(s, e))   // stage O2: a targeted enemy -> attack (WoW's right-click)
                    {
                        WowLog.Info("Entities: " + how + " on targeted " + e.Name + " -> " + (e.Dead ? "loot" : AttackOnClick(s, e) ? "attack" : "talk"));
                        s.Interact(guid);
                    }
                    Ui.UiDeath.ReopenHealer(s, e);   // stage O2 fix: a ghost clicking the spirit healer again gets his offer back after a Cancel
                }
            }
            catch (Exception ex)
            {
                WowLog.Warn("Entities: click on " + e.Name + " failed: " + ex.Message);
            }
        }

        private float _downAt = -10f, _rightDownAt = -10f;
        private Vector3 _downPos, _rightDownPos;
        private bool _downOnUi, _rightDownOnUi;
        private static readonly List<RaycastResult> UiHits = new List<RaycastResult>();

        /// <summary>
        /// True when a UI element (anything with a RectTransform: our HUD/windows, the client's tablet and menus) is under the
        /// mouse. IsPointerOverGameObject is not usable for this: the client's desktop PhysicsRaycaster also reports the terrain.
        /// </summary>
        internal static bool PointerOverUi()   // iteration 11 (spellfx hook 6b): internal - UiGroundTarget uses the same test
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = Input.mousePosition };
            UiHits.Clear();
            es.RaycastAll(ped, UiHits);
            for (int i = 0; i < UiHits.Count; i++)
            {
                var go = UiHits[i].gameObject;
                if (go != null && Ui.UiCursor.IsCatcher(go)) continue;   // iteration 11 (uicore H5): the first-person catcher is not UI for world clicks
                if (go != null && go.GetComponent<RectTransform>() != null) return true;
            }
            return false;
        }

        /// <summary>Clears the target (a click / trigger on the ground; WoW's behaviour). No-op without a target.</summary>
        internal void ClearTarget(string how)
        {
            if (Ui.UiCursor.TakesWorldClicks) return;   // iteration 11 (uicore H5)
            var s = _session;
            if (s == null || s.TargetGuid == 0) return;
            try
            {
                s.SetTarget(0);
                _lastClickGuid = 0;
                WowLog.Info("Entities: " + how + " -> target cleared");
            }
            catch (Exception ex)
            {
                WowLog.Warn("Entities: clearing the target failed: " + ex.Message);
            }
        }

        internal void OnEntityHover(ulong guid, bool on)
        {
            WowEntityView v;
            if (!_views.TryGetValue(guid, out v)) return;
            v.Hovered = on;
            if (on) HoveredGuid = guid;
            else if (HoveredGuid == guid) HoveredGuid = 0;
        }

        /// <summary>
        /// Desktop, belt and braces (EtsTruckDoor's pattern): if the client's pointer event never arrives, a plain left click
        /// on an entity box still counts. Not in VR, and not when the pointer is over anything the EventSystem sees (then the
        /// pointer event does the job).
        /// </summary>
        private void DesktopClickFallback()
        {
            if (_inputBroken || WowPlayer.XrPresent) return;
            try
            {
                if (Ui.UiCursor.TakesWorldClicks) return;   // iteration 11 (uicore H5): UiCursor routes the plain world clicks to its mode
                // Lead 2026-09-27: WoW's right-click - a plain one (released within 0.4 s and 6 px - ours, so a right-drag of the camera is no click),
                // not on any UI - on an entity selects it and interacts (attack / talk / loot / use).
                if (Input.GetMouseButtonDown(1))
                {
                    _rightDownAt = Time.unscaledTime;
                    _rightDownPos = Input.mousePosition;
                    _rightDownOnUi = PointerOverUi();
                }
                if (Input.GetMouseButtonUp(1) && !_rightDownOnUi && Time.unscaledTime - _rightDownAt < 0.4f
                    && ((Vector2)Input.mousePosition - (Vector2)_rightDownPos).sqrMagnitude < 36f && !PointerOverUi())
                {
                    var rcam = WowPlayer.Head();
                    RaycastHit rhit;
                    ulong rguid;
                    int rmask = (1 << WowEntityClick.Layer) | (1 << 0) | (1 << 14);
                    if (rcam != null && Physics.Raycast(rcam.ScreenPointToRay(Input.mousePosition), out rhit, 300f, rmask, QueryTriggerInteraction.Ignore)
                        && TryGetGuid(rhit.collider, out rguid) && rhit.distance <= 80f)
                        OnEntityClicked(rguid, "right-click", ClickKind.Interact);
                }
                // Lead, 2026-09-27 (the user: "if you click on the ground, you deselect the thing you have selected"): a plain
                // left click - pressed and released within 0.4 s and 6 px, not on any UI - that hits no entity clears the target.
                if (Input.GetMouseButtonDown(0))
                {
                    _downAt = Time.unscaledTime;
                    _downPos = Input.mousePosition;
                    _downOnUi = PointerOverUi();
                }
                bool up = Input.GetMouseButtonUp(0);
                if (!Input.GetMouseButtonDown(0) && !up) return;
                if (!up && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;   // the pointer event selects
                if (up && PointerOverUi()) return;
                var cam = WowPlayer.Head();
                if (cam == null) return;
                RaycastHit hit;
                int mask = (1 << WowEntityClick.Layer) | (1 << 0) | (1 << 14);
                ulong guid = 0;
                bool onEntity = Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out hit, 300f, mask, QueryTriggerInteraction.Ignore)
                                && TryGetGuid(hit.collider, out guid);
                if (!up)
                {
                    if (onEntity && hit.distance <= 80f) OnEntityClicked(guid, "mouse", ClickKind.Select);
                    return;
                }
                bool plainClick = !_downOnUi && Time.unscaledTime - _downAt < 0.4f && ((Vector2)Input.mousePosition - (Vector2)_downPos).sqrMagnitude < 36f;
                if (plainClick && !onEntity) ClearTarget("mouse on the ground");
            }
            catch (Exception e)
            {
                _inputBroken = true;
                WowLog.Warn("Entities: legacy mouse input unavailable (" + e.Message + "); clicks come only through the client's pointer events");
            }
        }

        // ------------------------------------------------------------------ combat text

        private void OnEvent(GameEvent e)
        {
            var s = _session;
            if (s == null || _text == null) return;
            ulong me = s.Player != null ? s.Player.Guid : 0;
            // Stage O2 (integrator, spells request 3): online every caster's spell events arrive, so like WoW only fights of the player,
            // the player's pet or totems float numbers/words; the victim's wound sound still plays for every fight (Number, show false).
            bool show = e.SourceGuid == 0 || Mine(s, e.SourceGuid, me) || e.TargetGuid == 0 || Mine(s, e.TargetGuid, me);
            switch (e.Type)
            {
                case GameEventType.MeleeHit:
                    Number(e.TargetGuid, me, e.Amount, e.Crit, e.Crit ? WowCombatText.Yellow : WowCombatText.White, WowCombatText.SelfDamage, '-', show);
                    break;
                case GameEventType.SpellDamage:
                case GameEventType.PeriodicDamage:
                    Number(e.TargetGuid, me, e.Amount, e.Crit, WowCombatText.Yellow, WowCombatText.SelfSpell, '-', show);
                    break;
                case GameEventType.SpellHeal:
                case GameEventType.PeriodicHeal:
                    Number(e.TargetGuid == 0 ? me : e.TargetGuid, me, e.Amount, e.Crit, WowCombatText.Green, WowCombatText.SelfHeal, '+', show);
                    break;
                case GameEventType.MeleeMiss:
                    if (show) Word(e.TargetGuid, me, string.IsNullOrEmpty(e.Text) ? "Miss" : e.Text);
                    break;
                case GameEventType.MeleeDodge:
                    if (show) Word(e.TargetGuid, me, string.IsNullOrEmpty(e.Text) ? "Dodge" : e.Text);
                    break;
                case GameEventType.MeleeParry:
                    if (show) Word(e.TargetGuid, me, string.IsNullOrEmpty(e.Text) ? "Parry" : e.Text);
                    break;
                case GameEventType.MeleeBlock:
                    if (show) Word(e.TargetGuid, me, string.IsNullOrEmpty(e.Text) ? "Block" : e.Text);
                    break;
                case GameEventType.SpellMiss:
                    if (show) Word(e.TargetGuid, me, string.IsNullOrEmpty(e.Text) ? "Miss" : e.Text);
                    break;
                case GameEventType.Absorb:
                    if (show) Word(e.TargetGuid == 0 ? me : e.TargetGuid, me, "Absorb");
                    break;
            }
        }

        /// <summary>The player, the player's pet or one of the player's totems/summons (EntityState.OwnerGuid).</summary>
        private static bool Mine(IWowSession s, ulong guid, ulong me)
        {
            if (guid == 0) return false;
            if (guid == me) return true;
            if (s.Player != null && s.Player.PetGuid == guid) return true;
            EntityState en = s.Find(guid);
            return en != null && me != 0 && en.OwnerGuid == me;
        }

        private void Number(ulong target, ulong me, int amount, bool crit, Color entityColor, Color selfColor, char selfPrefix, bool show)
        {
            if (amount <= 0) return;
            Vector3 p;
            if (target != 0 && target == me)
            {
                if (show && WowSettings.ShowCombatText && SelfSpot(out p)) _text.Number(p, amount, selfPrefix, selfColor, crit, true);
                return;
            }
            if (show && WowSettings.ShowCombatText && EntitySpot(target, out p)) _text.Number(p, amount, selfPrefix == '+' ? '+' : '\0', entityColor, crit, false);
            WowEntityView v;
            if (selfPrefix == '-' && _views.TryGetValue(target, out v) && !v.Parked) v.PlayWound(crit, Time.time);   // CreatureSoundData injury sounds
        }

        private void Word(ulong target, ulong me, string word)
        {
            if (!WowSettings.ShowCombatText) return;
            Vector3 p;
            if (target != 0 && target == me)
            {
                if (SelfSpot(out p)) _text.Word(p, word, WowCombatText.SelfDamage, true);
                return;
            }
            if (EntitySpot(target, out p)) _text.Word(p, word, WowCombatText.Grey, false);
        }

        private bool EntitySpot(ulong guid, out Vector3 p)
        {
            p = Vector3.zero;
            WowEntityView v;
            if (guid == 0 || !_views.TryGetValue(guid, out v) || v.Parked) return false;
            p = v.Root.transform.position + Vector3.up * (v.Top * 0.9f + 0.1f);
            return true;
        }

        /// <summary>In front of the player's eyes, a little low (damage taken, heals).</summary>
        private static bool SelfSpot(out Vector3 p)
        {
            p = Vector3.zero;
            var cam = WowPlayer.Head();
            if (cam == null) return false;
            var t = cam.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = t.up;
            fwd.Normalize();
            p = t.position + fwd * 1.6f + Vector3.up * -0.15f;
            return true;
        }
    }
}
