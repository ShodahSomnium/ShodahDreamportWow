using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Access;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The scene's root ("WoW Game" object; PLAN section 6). Access first (private testing): Awake sets the log sinks and
    /// runs Gate - nothing below starts before Core/WowAccess says the player is on the whitelist; a visitor gets only
    /// Game/Access/WowIsolation (every other player hidden and silent), no text, no room. Then StartDreamport (formerly
    /// Awake), and the isolation hides only players without a WoW network record. Boot: version, shader carriers, then on a worker
    /// thread the install (WowInstall), the archives (WowFiles), the location shortlist, the class screen's data and the
    /// panel art; meanwhile the black room stands at WowSpace.LoginRoomOrigin with the status line.
    /// The room panel has two tabs. PLAY (default): the account's saved characters (WowRoster in Bridge LocalStorage,
    /// named after the Somnium nickname) and the class creation screen (WowCharacterScreen); Play shows WoW's loading
    /// screen for Kalimdor, streams the map around the character's saved position (WowMapView), reads the spell tables on
    /// a worker (OfflineSession.Preload), teleports the avatar onto the floor there, applies the zone's light, starts the
    /// offline game in the race's starting zone (WowZones.ForRace) (OfflineSession) and hands it to the views (WowPlay.Start). Every frame in play: the avatar's
    /// position/facing/walking go to the session, the session ticks, its events are fanned out (WowPlay.Publish) and its
    /// Teleport events move the avatar. Saves on level-up, quest changes, death, every 30 s, on going back and on leaving.
    /// SIGHTSEEING: iteration 1 unchanged - a location button streams the map there, arrival with the model line-up.
    /// In the world: the map follows the avatar, a fall guard puts it back on the ground, the light follows the light
    /// volumes. Back to the room with the K key (desktop) or the wrist button (VR). Everything another module provides
    /// is called inside try/catch: a missing piece becomes a status line, never a crash.
    /// </summary>
    public sealed partial class WowGame : MonoBehaviour
    {
        [Tooltip("Every carrier material of a custom shader (Resources/Wow*.mat). Shaders reach the upload only through them.")]
        [SerializeField] private Material[] _shaderCarriers;

        [Tooltip("Where World of Warcraft 3.3.5a is installed (Wow.exe folder, or its Data folder).")]
        [SerializeField] private string _installHint = @"D:\WOTLK\World of Warcraft 3.3.5a";

        [Tooltip("Time of day chosen at start (hours): 6 dawn, 12 day, 19 dusk, 23 night.")]
        [SerializeField] private float _timeOfDay = 12f;

        [Tooltip("Build the model line-up at each sightseeing arrival (also a toggle on the room panel).")]
        [SerializeField] private bool _lineup = true;

        private enum Phase { Booting, Room, Loading, World }

        private enum Mode { Sightseeing, Play, Online }

        public static WowGame Current { get; private set; }

        private const int GroundMask = (1 << 0) | (1 << 14);
        private const float GroundProbeDown = 8f;   // m, ours: how far below the session's hint (~2 yd above the feet) a floor collider counts (GroundZ)
        private const float ReadyRadius = 30f;
        private const float LoadTimeout = 240f;

        /// <summary>Seconds between autosaves while playing.</summary>
        private const float AutosaveSeconds = 30f;

        /// <summary>Horizontal avatar speed (m/s, smoothed) above which the character counts as moving (cancels casts, stops Auto Shot).</summary>
        private const float MovingSpeed = 0.4f;
        /// <summary>Lead fix 2026-11-05 (user: mining on a slope "Can't do that while moving" - the avatar slides down it slowly): m/s the feet
        /// must sink (smoothed) for a move WITHOUT movement input to count as a slide, not a walk (ours).</summary>
        private const float SlideSinkSpeed = 0.05f;
        private float _sinkSpeed;
        private bool _slideSaid;
        private UnityEngine.XR.InputDevice _moveStick;
        private float _moveStickScan;
        private static readonly List<UnityEngine.XR.InputDevice> MoveStickFound = new List<UnityEngine.XR.InputDevice>();

        /// <summary>Lead fix 2026-11-05: the player is asking to move now - desktop W A S D / Q E / arrows, VR the left stick (the client's
        /// locomotion stick) past 0.2. A legacy Input failure counts as input (the old rule then holds).</summary>
        private bool MoveInputHeld()
        {
            try
            {
                if (WowPlayer.XrPresent)
                {
                    if (!_moveStick.isValid && Time.unscaledTime >= _moveStickScan)
                    {
                        _moveStickScan = Time.unscaledTime + 1f;
                        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Controller | UnityEngine.XR.InputDeviceCharacteristics.Left, MoveStickFound);
                        if (MoveStickFound.Count > 0) _moveStick = MoveStickFound[0];
                    }
                    Vector2 st;
                    return _moveStick.isValid && _moveStick.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out st) && st.magnitude > 0.2f;
                }
                return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E)
                    || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
            }
            catch (Exception) { return true; }
        }

        /// <summary>Lead fix 2026-11-05: on foot the character moves when the feet go faster than MovingSpeed - unless nothing asks to
        /// move and the feet sink (the client's controller sliding down a slope): then it stands, so casts, gathering and mining work (WoW
        /// clients do not slide on such slopes). VR room-scale walking on level ground still counts (it does not sink).</summary>
        private bool FootMoving()
        {
            if (_speed <= MovingSpeed) return false;
            if (_sinkSpeed < SlideSinkSpeed || MoveInputHeld()) return true;
            if (!_slideSaid) { _slideSaid = true; WowLog.Info("Play: the avatar slides down a slope without movement input - not moving (casts and gathering allowed; logged once)."); }
            return false;
        }
        private const float SeatedSwayMetres = 0.5f;   // lead fix 2026-10-04 (ours, = GearStatePose.MoveRelease): VR - drift allowed while seated

        /// <summary>A saved position further than this from the zone's start (yards) is not in the zone: the character starts at the zone's start.</summary>
        private const float MaxStartDistance = 900f;

        private Phase _phase = Phase.Booting;
        private Mode _mode = Mode.Sightseeing;
        private WowRoom _room;
        private WowLoadingScreen _loading;
        private WowMapView _map;
        private WowLineup _lineupObject;
        private WowWrist _wrist;
        private readonly WowCharacterScreen _chars = new WowCharacterScreen();

        // Boot (worker thread -> main thread).
        private Thread _bootThread;
        private volatile bool _bootDone;
        private string _bootError;
        private WowFiles _files;
        private List<WowLocation> _locations;
        private string _locationReport = "";
        private List<WowPreparedTexture> _prepared;
        private byte[] _fontBytes;
        private string _root, _locale;
        private long _bootMs;
        private float _bootStart;
        private List<WowClassEntry> _classes;
        private string _classReport = "";
        private List<WowRaceEntry> _races;
        private List<WowFactionEntry> _factions;
        private string _raceReport = "";

        // Install (the game folder): this Somnium account's saved folder is tried first, then _installHint and the drive
        // search; when nothing is found the room's folder browser opens (Front/WowInstallPicker); a folder chosen there
        // re-runs the boot in the room and is saved (WowSaves.SaveInstallPath) once it worked.
        private WowInstallPicker _picker;
        private WowFiles _bootFiles;                 // the boot's result (worker -> FinishBoot); null = it failed
        private volatile bool _bootNotFound;         // the boot found no install at all
        private volatile string _bootSavedBad;       // worker: why the saved folder was not used (plain words)
        private string _bootSource = "";             // worker: how the boot's folder was found
        private string _bootChosen;                  // the folder the running boot was asked to use (the browser), or null
        private string _installSource = "";          // how the folder in use was found
        private string _installSaved;                // this account's saved folder (null = none, or not looked up yet)
        private string _installSavedBad;             // why the saved folder was not usable at the last boot, or null
        private bool _installSavedRead;              // the saved folder was looked up (the Somnium account was known)
        private string _installSaveLater;            // a chosen folder that worked, saved once the Somnium account is known
        private WowFiles _oldFiles;                  // the previous install's files, closed a little after the new ones took over
        private float _oldFilesCloseAt;
        private float _nextInstallPoll;
        private const float InstallIdentityWait = 8f;
        private const float OldFilesCloseDelay = 10f;
        private const string PickFolderText = "Choose your World of Warcraft 3.3.5a folder - the one with Wow.exe.";

        // Settings (iteration 3): the room's Settings tab, the account's store, a debounced save on WowSettings.Changed.
        private readonly WowSettingsTab _settingsTab = new WowSettingsTab();
        private WowSettingsStore _settingsStore;
        private bool _settingsDirty, _settingsLoading, _settingsSubscribed;
        private float _settingsDirtyAt;
        private const float SettingsSaveDelay = 2f;

        // Room.
        private bool _placed;
        private float _lastTeleport = -10f;
        private int _putBack;
        private float _placeTries;
        private bool _eventSystemChecked;

        // Characters (the roster is per Somnium account; nothing about the player is kept in a static).
        private WowRoster _roster;
        private string _userId, _nick = WowSaves.FallbackName;
        private float _nextIdentityPoll;
        private bool _identityWaitLogged;

        // Loading / world.
        private WowLocation _current;
        private Vector3 _spot;
        private float _loadStart, _readySince = -1f;
        private bool _mapErrorLogged, _focusErrorLogged, _readyErrorLogged, _groundErrorLogged;
        private Vector3 _arrival;
        private float _arrivalYaw;
        private float _worldSince, _teleportAt, _repeatSince;
        private bool _teleportLanded;
        private int _teleportTries;
        private Vector3 _lastSafe;
        private bool _lastSafeKnown;
        private float _noGround;
        private int _fallRescues;
        private float _lastRescueAt = -100f;
        private float _nextLight;

        // Play.
        private CharacterRecord _playRecord;
        private ZoneContent _zone;
        private OfflineSession _session;
        private float _playYaw;
        private Thread _preloadThread;
        private volatile bool _preloadDone;
        private volatile string _preloadError;
        private long _preloadMs;
        private bool _preloadLogged;
        private float _nextSave, _lastSaveAt = -100f;
        private bool _saveSoon;
        private string _saveSoonWhy;
        private Vector3 _lastFeet;
        private bool _lastFeetKnown;
        private float _speed;
        private float _holdPositionUntil;
        private Vector3 _holdTarget;
        private string _facingSource;
        private float _tickErrorAt = -100f;
        private int _events;

        public WowFiles Files { get { return _files; } }

        // Access (private testing, Core/WowAccess): nothing of the Dreamport starts before the verdict.
        private bool _started;
        private bool _isolating;

        private void Awake()
        {
            // Lead, 2026-09-27 (the user: a visitor must see "nothing about WoW"): the world's log lines are held back until the
            // whitelist verdict - flushed to Player.log for a whitelisted player, dropped (and nothing more written) for a visitor.
            _heldLog.Clear();
            WowLog.Sink = s => HoldLog(0, s);
            WowLog.WarnSink = s => HoldLog(1, s);
            WowLog.ErrorSink = s => HoldLog(2, s);
            StartCoroutine(Gate());
        }

        private readonly List<KeyValuePair<int, string>> _heldLog = new List<KeyValuePair<int, string>>();

        private void HoldLog(int level, string line)
        {
            if (_heldLog.Count < 500) _heldLog.Add(new KeyValuePair<int, string>(level, line));
        }

        /// <summary>After the verdict: whitelisted = real sinks + the held lines; visitor = silence.</summary>
        private void ReleaseLog(bool allowed)
        {
            if (!allowed)
            {
                WowLog.Sink = null;
                WowLog.WarnSink = null;
                WowLog.ErrorSink = null;
                _heldLog.Clear();
                return;
            }
            WowLog.Sink = Debug.Log;
            WowLog.WarnSink = Debug.LogWarning;
            WowLog.ErrorSink = Debug.LogError;
            foreach (var kv in _heldLog)
            {
                if (kv.Key == 0) Debug.Log(kv.Value);
                else if (kv.Key == 1) Debug.LogWarning(kv.Value);
                else Debug.LogError(kv.Value);
            }
            _heldLog.Clear();
        }

        /// <summary>
        /// The whitelist gate (Core/WowAccess). While the client has not reported the username, and for good when the player
        /// is not on the list, only the isolation runs, with the rule "nobody is with me": every other player is hidden and
        /// silent on this screen, and NOTHING of the Dreamport is built, read, added, loaded or saved (no text, no room).
        /// Allowed: <see cref="StartDreamport"/>. The check is driven by hand so an immediate verdict (the editor, an empty
        /// list) starts the Dreamport inside Awake, exactly as before.
        /// </summary>
        private IEnumerator Gate()
        {
            IEnumerator check = WowAccess.Check();
            bool waited = false;
            while (check.MoveNext())
            {
                if (!_isolating)
                {
                    StartIsolation(NobodyRule("waiting for the whitelist check"));
                }

                if (!waited)
                {
                    waited = true;
                    ShowPlainFloor();   // the user: before the verdict and for visitors "just a plane, nothing about WoW"
                }

                yield return check.Current;
            }

            if (!WowAccess.Allowed)
            {
                StartIsolation(NobodyRule("visitor (not on the whitelist)"));
                ShowPlainFloor();
                ReleaseLog(false);
                yield break;
            }

            ReleaseLog(true);
            HidePlainFloor();
            StartDreamport();
        }

        // ------------------------------------------------------------------ the visitor's plane (lead, 2026-09-27)

        private GameObject _plainFloor;

        /// <summary>Plain grey walkable plane under the spawn point: all a visitor (or anyone before the verdict) ever sees.</summary>
        private void ShowPlainFloor()
        {
            if (_plainFloor != null) return;
            try
            {
                _plainFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10 x 10 m with a MeshCollider
                _plainFloor.name = "Floor";
                _plainFloor.transform.SetParent(transform, false);
                _plainFloor.transform.position = WowSpace.LoginRoomOrigin;
                _plainFloor.transform.rotation = Quaternion.identity;
                _plainFloor.transform.localScale = new Vector3(6f, 1f, 6f);      // 60 x 60 m
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");        // shipped by the client itself
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                var r = _plainFloor.GetComponent<Renderer>();
                if (r != null && sh != null)
                {
                    var m = new Material(sh);
                    Color grey = new Color(0.42f, 0.42f, 0.42f, 1f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", grey);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
                    r.sharedMaterial = m;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Floor: " + e.Message);
            }
        }

        private void HidePlainFloor()
        {
            if (_plainFloor == null) return;
            var r = _plainFloor.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) Destroy(r.sharedMaterial);
            Destroy(_plainFloor);
            _plainFloor = null;
        }

        /// <summary>A visitor's rule (and everyone's while the check waits): nobody is with me.</summary>
        private WowIsolation.Rule NobodyRule(string who)
        {
            return new WowIsolation.Rule
            {
                Name = who + ": nobody is with you",
                IsWithMe = id => false,
                WithText = "with you",
                ApartText = "not shown to a visitor",
                WorldRoot = transform,
            };
        }

        /// <summary>
        /// A whitelisted player's rule: the other player has a WoW network record (WowGear.HasRecord). Visitors never claim
        /// a slot, so they are hidden and silent at once; testers are left to Gear. Show at once and decide every frame, so a
        /// player whose record appears is restored before WowGear's LateUpdate (WowIsolation "Hand-over to Gear").
        /// </summary>
        private WowIsolation.Rule RecordRule()
        {
            return new WowIsolation.Rule
            {
                Name = "whitelisted: players with a WoW network record",
                IsWithMe = WowGear.HasRecord,
                WithText = "has a WoW network record (left to Gear)",
                ApartText = "no WoW network record (not on the whitelist, or not started yet)",
                ShowDelay = 0f,
                DecideInterval = 0f,
                Online = () => Networking.SceneNetworking.IsNetworkReady,
                OfflineText = "the network room has not been up for 20 s: nobody can have a WoW network record, so they stay hidden",
                WorldRoot = transform,
            };
        }

        private void StartIsolation(WowIsolation.Rule rule)
        {
            try
            {
                WowIsolation.Start(rule);
                _isolating = true;
                AddOnce<WowIsolationDriver>();
            }
            catch (Exception e)
            {
                WowLog.Error("Access: starting the isolation failed: " + e);
            }
        }

        /// <summary>Everything the world does, for a whitelisted player only (this was Awake before the whitelist).</summary>
        private void StartDreamport()
        {
            _started = true;
            Current = this;
            WowSpace.Rebased += OnOriginRebased;
            WowLog.Info("WoW Dreamport " + WowVersion.Label + ": " + WowVersion.Summary);
            WowShaders.Carry(_shaderCarriers);

            // Statics survive a re-entry (the client keeps the world's assembly loaded): a session left from a visit that
            // did not end cleanly must not reach this one's views.
            if (WowPlay.Session != null)
            {
                WowLog.Warn("Play: a session from an earlier visit was still set - ended.");
                WowPlay.End();
            }

            // The game views (their owners build them; the front end only adds them once).
            AddOnce<WowEntityViews>();
            AddOnce<WowArmUi>();
            AddOnce<WowGear>();
            // Stage S1 (party, reports10/party.md request 1): the server group merged into WowParty, the Somnium profile
            // pictures, WoW's invite popup and unit menu. After WowGear (its order 10010 runs after WowGear's refill).
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiGroup>();
            // Iteration 11 (w0game, wave 0): the interact hub + follow + raid marks (proposal-interact.md 6.3.2 / H10) and the instance
            // popups (proposal-world.md hook 7), after UiGroup. Wave-0 stubs are empty components until their owners build them.
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiInteract>();
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Interact.WowFollow>();
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Interact.WowRaidMarks>();
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiInstance>();
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiQueues>();   // integrator (queues request): BG / LFD popups and icons, independent of when the windows are built

            // Iteration 3 (reports3/audio.md, reports3/vfx.md): the sound driver (room music, footsteps, zone music start on
            // their own) and the spell visuals.
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Audio.WowAudioDriver>();
            AddOnce<WowSpellVfx>();

            // Iteration 4 (lead): floating origin, flights, VR radial input, world details (owners fill them in).
            AddOnce<WowOrigin>();
            AddOnce<WowFlight>();
            AddOnce<WowMount>();   // iteration 7 (lead hook 1): the local rider
            AddOnce<WowVehicle>();   // iteration 11 (pets H7a): vehicle seats; Drives(p) for WowMount
            AddOnce<WowRunSpeed>();
            AddOnce<WowSwim>();       // lead 2026-10-26: on-foot swimming (Game/Play/WowSwim.cs)   // lead (2026-09-27): Somnium players on foot at the WoW run speed
            AddOnce<WowForcedMove>(); AddOnce<TransportCarry>();   // iteration 11 (motion hook 7): forced moves, transports
            AddOnce<WowVrInput>();
            AddOnce<WowWorldDetails>();

            // Iteration 5 (lead): the WoW Map app on the Somnium tablet (live overhead camera) + its fallback window.
            AddOnce<WowTabletMap>();

            // Stage S1 (chat, reports10/chat.md request 1): the chat window (desktop ChatFrame1 + edit box, VR palm window) and
            // WoW's chat bubbles over the speakers. Whitelisted players only, like every game view (visitors never get either).
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiChat>();
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiSocialHost>();   // iteration 11 (social H7): guild invite, new mail, emotes
            AddOnce<SomniumSpace.Worlds.SomniumWoW.Game.Entities.WowChatBubbles>();

            // Access: from now on the isolation hides and silences only players without a WoW network record (visitors);
            // everyone with a record is Gear's (it was "nobody is with me" while the whitelist check waited).
            StartIsolation(RecordRule());

            if (!_settingsSubscribed)
            {
                WowSettings.Changed += OnSettingsChanged;
                _settingsSubscribed = true;
            }

            // 13.8: the artist's lobby (inactive in the scene until now - only an allowed player gets this far; a visitor keeps the plane)
            WowLobby lobby = WowLobby.Find();
            WowLighting.RoomSky = lobby != null ? lobby.Sky : null;
            WowLog.Info(lobby != null ? "Lobby: found - the menu room is the WoW lobby." : "Lobby: none in the scene - the black room.");
            _room = WowRoom.Create(transform, lobby);
            if (lobby != null) lobby.Show(true);
            _loading = WowLoadingScreen.Create(_room.transform);
            _wrist = WowWrist.Create(transform);
            _wrist.BackPressed += () => ReturnToRoom("the wrist button");
            _chars.PlayPressed += OnPlayCharacter;
            _chars.CreatePressed += OnCreateCharacter;
            _chars.DeleteConfirmed += OnDeleteCharacter;
            InitOnline();   // iteration 6: the keyboard and the online screen (Front/WowGameOnline.cs)
            _room.BuildPanel(null, null, null, null, _timeOfDay, _lineup);
            _room.SetStatus("Reading game files...", false);
            WowLighting.EnterRoom();

            _bootStart = Time.realtimeSinceStartup;
            InitInstall();   // the folder browser; the boot itself starts in Update once the saved folder could be looked up (TryStartBoot)
        }

        private void AddOnce<T>() where T : Component
        {
            try
            {
                if (GetComponent<T>() == null)
                {
                    gameObject.AddComponent<T>();
                }
            }
            catch (Exception e)
            {
                WowLog.Error("Awake: adding " + typeof(T).Name + " failed: " + e);
            }
        }

        /// <summary>
        /// Worker thread: disk and archive work only (no Unity API). The folder: chosen (the room's folder browser) alone;
        /// else the account's saved folder when it still validates, else WowInstall.Find(hint) (the hint, the default, the
        /// drive search). Everything is read into locals and committed at the end with _bootFiles, so a failed re-run leaves
        /// the game that is loaded untouched (FinishBoot).
        /// </summary>
        private void Boot(string chosen, string saved, string hint)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            WowFiles files = null;
            bool committed = false;

            try
            {
                string root = null, why, plain, loc;

                if (chosen != null)
                {
                    if (!WowInstall.Check(chosen, out plain, out loc))
                    {
                        _bootError = "The chosen folder cannot be used: " + plain;
                        return;
                    }

                    root = chosen;
                    _bootSource = "the folder you chose";
                }
                else if (saved != null)
                {
                    if (WowInstall.Validate(saved, out why))
                    {
                        root = saved;
                        _bootSource = "your saved choice";
                        WowLog.Info("Install: the saved game folder is used: " + saved + ".");
                    }
                    else
                    {
                        WowInstall.Check(saved, out plain, out loc);
                        _bootSavedBad = plain ?? why;
                        WowLog.Warn("Install: the saved game folder " + saved + " is not usable now (" + why + ") - searching the usual places.");
                    }
                }

                if (root == null && chosen == null)
                {
                    root = WowInstall.Find(string.IsNullOrEmpty(hint) ? null : hint);
                    _bootSource = root != null && WowInstall.SameFolder(root, string.IsNullOrEmpty(hint) ? WowInstall.DefaultHint : hint) ? "the default place" : "the automatic search";
                }

                if (root == null)
                {
                    _bootNotFound = true;
                    _bootError = "World of Warcraft 3.3.5a not found. Tried: " + (saved != null ? "the saved folder " + saved + ", " : "") + (string.IsNullOrEmpty(hint) ? WowInstall.DefaultHint : hint) + " and a short drive search.";
                    return;
                }

                if (!WowInstall.Validate(root, out why))
                {
                    _bootError = "Not a usable WoW 3.3.5a install: " + root + " (" + why + ").";
                    return;
                }

                string locale = WowInstall.DetectLocale(root);

                try
                {
                    files = WowFiles.Open(root, locale);
                }
                catch (Exception e)
                {
                    _bootError = "Could not open the game archives in " + root + "\\Data (locale " + (locale ?? "?") + "): " + e.Message;
                    return;
                }

                string report;
                List<WowLocation> locations = WowLocations.Load(files, out report);
                byte[] fontBytes = files.Read(WowUi.FontPath);
                var prepared = new List<WowPreparedTexture>();
                foreach (string p in WowUi.PanelTextures) prepared.Add(WowTextures.Prepare(files, p, WowUi.OptionsFor(p)));

                // The class creation screen: names, glue texts, icon coordinates and colours from the files.
                string classReport;
                List<WowClassEntry> classes = WowClassInfo.Load(files, out classReport);

                // The faction and race screens: races, glue texts, icons, classes, racial spells (Spell.dbc is read here,
                // so the race screen and the first Play do not parse it on the main thread) and the starting zones (built).
                List<WowRaceEntry> races = null;
                List<WowFactionEntry> factions = null;
                string raceReport;

                try
                {
                    var rw = System.Diagnostics.Stopwatch.StartNew();
                    races = WowRaceInfo.Load(files, out factions, out raceReport);
                    raceReport = raceReport + ", " + rw.ElapsedMilliseconds + " ms";
                }
                catch (Exception e)
                {
                    raceReport = "reading the races failed: " + e.GetType().Name + ": " + e.Message;
                    WowLog.Error("Boot: races: " + e);
                }

                // The lighting tables, so the first arrival does not parse them on the main thread.
                Dbc.Load(files, "Light");
                Dbc.Load(files, "LightParams");
                Dbc.Load(files, "LightIntBand");
                Dbc.Load(files, "LightFloatBand");
                Dbc.Load(files, "LoadingScreens");
                Dbc.Load(files, "Map");

                // Commit (FinishBoot takes _bootFiles over as _files on the main thread).
                _root = root;
                _locale = locale;
                _locations = locations;
                _locationReport = report;
                _fontBytes = fontBytes;
                _prepared = prepared;
                _classes = classes;
                _classReport = classReport;
                _races = races;
                _factions = factions;
                _raceReport = raceReport;
                _bootFiles = files;
                committed = true;
                WarmMountData(files, "game files opened");   // iteration 7 (lead hook 7): mount catalogue + area rules on the worker

                if (locations.Count == 0)
                {
                    _bootError = "The game files opened (" + root + ") but no location could be read from TaxiNodes.dbc / WorldSafeLocs.dbc: " + report;
                }
            }
            catch (Exception e)
            {
                _bootError = "Reading the game files failed (" + (chosen ?? hint ?? "") + "): " + e.GetType().Name + ": " + e.Message;
                WowLog.Error("Boot: " + e);
            }
            finally
            {
                if (!committed && files != null)
                {
                    try { files.Dispose(); }
                    catch (Exception) { }
                }

                _bootMs = watch.ElapsedMilliseconds;
                _bootDone = true;
            }
        }

        private void FinishBoot()
        {
            WowFiles booted = _bootFiles;
            _bootFiles = null;
            string chosen = _bootChosen;
            _bootChosen = null;

            if (chosen == null)
            {
                _installSavedBad = _bootSavedBad;   // a chosen folder is not the saved one: that note stays as it was
            }

            if (booted == null)
            {
                FinishBootFailed(chosen);
                return;
            }

            if (_files != null && !ReferenceEquals(booted, _files))
            {
                // A folder chosen in the room: the old files stay open a little longer (a worker may still be reading), and
                // everything that polls WowGame.Files (sound, world details, the tablet map) takes the new ones next frame.
                CloseOldFiles("a newer game folder replaced them first");
                _oldFiles = _files;
                _oldFilesCloseAt = Time.realtimeSinceStartup + OldFilesCloseDelay;

                try { if (ReferenceEquals(SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiArt.Files, _oldFiles)) SomniumSpace.Worlds.SomniumWoW.Game.Ui.UiArt.Files = booted; }
                catch (Exception e) { WowLog.Warn("Install: handing the new files to the UI art failed: " + e.Message); }
            }

            _files = booted;
            _installSource = _bootSource;

            if (chosen != null)
            {
                _installSaved = chosen;
                _installSavedBad = null;
                SaveChosenInstall(chosen);
            }

            if (_picker != null) _picker.Close();
            WowLog.Info("Install: boot result - ready: " + _root + " (" + _locale + ", " + _installSource + ") in " + _bootMs + " ms" + (_installSavedBad != null ? "; the saved folder " + _installSaved + " was not usable: " + _installSavedBad : "") + ".");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            WowUi.Init(_files, _prepared, _fontBytes);
            _prepared = null;
            _fontBytes = null;
            _room.BuildPanel(_locations ?? new List<WowLocation>(), OnPick, OnHour, OnToggleLineup, _timeOfDay, _lineup);
            _chars.SetClasses(_classes);
            _chars.SetRaces(_races, _factions, _files);
            _chars.Attach(_room);
            _settingsTab.Attach(_room);
            AttachOnline();   // iteration 6: the Play tab's Offline / Online switch
            WowPlay.Classes = _classes;
            WowPlay.Races = _races;
            RefreshCharacters();

            if (_bootError != null)
            {
                WowLog.Error("Boot: " + _bootError + " (" + _bootMs + " ms)");
                _room.SetStatus(_bootError, true);
            }
            else if (_installSavedBad != null)
            {
                _room.SetStatus("Your saved game folder " + _installSaved + " is not usable now (" + _installSavedBad + ") - using " + _root + " instead (" + _installSource + "). Settings > Game to change it.", true);
            }
            else if (chosen != null)
            {
                _room.SetStatus("Game folder changed. " + ReadyStatus(null), false);
            }
            else
            {
                _room.SetStatus(ReadyStatus(null), false);
            }

            _room.SetInteractive(true);
            _phase = Phase.Room;
            WowLog.Info("Boot: game files ready in " + _bootMs + " ms (" + _root + ", " + _locale + "), panel built in " + watch.ElapsedMilliseconds + " ms; " + _locationReport + "; font " + WowUi.FontSource + "; " + WowTextures.StatsLine());
            WowLog.Info("Class screen: " + _classReport);

            if (_classes != null)
            {
                foreach (WowClassEntry c in _classes) WowLog.Info("Class screen: " + c);
            }

            WowLog.Info("Race screen: " + _raceReport);

            if (_races != null)
            {
                foreach (WowRaceEntry r in _races) WowLog.Info("Race screen: " + r);
            }

            if (_locations != null)
            {
                foreach (WowLocation l in _locations) WowLog.Info("Location: " + l);
            }
        }

        private string ReadyStatus(string last)
        {
            string where = "(" + _root + ", " + _locale + ")";
            return last == null ? (WowRoom.ShowOldTabs ? "Ready. Play a character or go sightseeing.   " : "Ready.   ") + where : "Back in the room. (Last: " + last + ")   " + where;
        }

        // ------------------------------------------------------------------ install (the game folder)

        /// <summary>StartDreamport: the room's folder browser and the Settings tab's Game page.</summary>
        private void InitInstall()
        {
            _bootThread = null;
            _picker = WowInstallPicker.Create(_room);
            _picker.Chosen += OnInstallChosen;
            _picker.Cancelled += () =>
            {
                if (_phase == Phase.Room && _files != null) _room.SetStatus(ReadyStatus(null), false);
            };
            _settingsTab.Install = InstallState;
            _settingsTab.ChangeFolderPressed += () => OpenInstallPicker("Settings > Game > Change...", _files != null ? _root : _installSaved, "", _files != null);
        }

        /// <summary>
        /// Phase.Booting before the first boot: the saved folder is per Somnium account, so the boot waits for the account
        /// (in the client it is known at once - the whitelist check read it; the editor says "editor" after 2 s), at most
        /// InstallIdentityWait seconds; then it searches without it and looks the saved folder up later (TickInstall).
        /// </summary>
        private void TryStartBoot(float now)
        {
            string id, nick;
            bool known = WowSaves.TryIdentity(now - _bootStart, out id, out nick);

            if (!known && now - _bootStart < InstallIdentityWait)
            {
                return;
            }

            string saved = null;

            if (known)
            {
                saved = ReadSavedInstall(id);
            }
            else
            {
                WowLog.Info("Install: the Somnium account is still unknown after " + InstallIdentityWait.ToString("0") + " s - searching without the saved game folder (looked up once the account is known).");
            }

            StartBoot(null, saved, "start");
        }

        /// <summary>Main thread: this account's saved folder from the world storage (not checked here: the boot worker does that).</summary>
        private string ReadSavedInstall(string id)
        {
            string report;
            string saved = WowSaves.ReadInstallPath(id, out report);
            _installSavedRead = true;
            _installSaved = saved;
            WowLog.Info("Install: " + (saved != null ? "saved game folder " + saved : "no saved game folder") + " for " + WowSaves.Mask(id) + " (" + report + ").");
            return saved;
        }

        /// <summary>Runs the boot on a worker (the first time, or again in the room with a folder chosen in the browser).</summary>
        private void StartBoot(string chosen, string saved, string why)
        {
            _bootDone = false;
            _bootError = null;
            _bootNotFound = false;
            _bootSavedBad = null;
            _bootSource = "";
            _bootFiles = null;
            _bootChosen = chosen;
            _bootMs = 0;
            _phase = Phase.Booting;

            if (_files != null)
            {
                _room.SetInteractive(false);   // the loaded game stays shown, but nothing starts while the new folder is read
            }

            _room.SetStatus(chosen != null ? "Reading the game files in " + chosen + "..." : "Reading game files...", false);
            _settingsTab.RefreshInstall();
            string hint = _installHint;
            WowLog.Info("Install: boot started (" + why + ") - " + (chosen != null ? "the chosen folder " + chosen
                : saved != null ? "the saved folder " + saved + " first, then the usual places" : "the usual places (" + (string.IsNullOrEmpty(hint) ? WowInstall.DefaultHint : hint) + ", then a drive search)") + ".");
            _bootThread = new Thread(() => Boot(chosen, saved, hint)) { IsBackground = true, Name = "WoW boot" };
            _bootThread.Start();
        }

        /// <summary>
        /// The boot gave no files. With a game already loaded (a folder chosen in the room): that game stays, the browser
        /// reopens with the reason. Without one: the friendly "Choose your ... folder" and the browser at once (only a
        /// whitelisted player ever gets here - visitors never reach StartDreamport).
        /// </summary>
        private void FinishBootFailed(string chosen)
        {
            _phase = Phase.Room;
            WowLog.Warn("Boot: " + _bootError + " (" + _bootMs + " ms)");

            if (_files != null)
            {
                WowLog.Warn("Install: boot result - the chosen folder " + (chosen ?? "?") + " could not be used; the game files from " + _root + " stay in use.");
                _room.SetInteractive(true);
                _room.SetStatus("That folder could not be used - still using " + _root + ".", true);
                OpenInstallPicker("the chosen folder failed", chosen, _bootError, true);
                _picker.SetMessage(_bootError, true);
                _settingsTab.RefreshInstall();
                return;
            }

            string message;

            if (_bootNotFound)
            {
                message = _installSavedBad != null ? "Your saved folder " + _installSaved + " is not usable now: " + _installSavedBad : "The game was not found in the usual places on this PC.";
                WowLog.Info("Install: boot result - no World of Warcraft 3.3.5a found (" + _bootError + ").");
            }
            else
            {
                message = _bootError;
                WowLog.Warn("Install: boot result - failed: " + _bootError);
            }

            _room.SetStatus(PickFolderText, false);
            OpenInstallPicker("the game was not found", chosen ?? _installSaved, message, false);

            if (chosen != null)
            {
                _picker.SetMessage(_bootError, true);
            }
        }

        /// <summary>Opens the room's folder browser (only in the room, never while loading or in the world).</summary>
        private void OpenInstallPicker(string why, string start, string message, bool canCancel)
        {
            if (_picker == null)
            {
                return;
            }

            if (_phase != Phase.Room)
            {
                WowLog.Info("Install: folder browser not opened (" + why + ") - " + (_phase == Phase.Booting ? "the game files are being read" : "only in the room") + ".");
                return;
            }

            _picker.Open(start, message, canCancel, why);
        }

        /// <summary>"Use this folder": re-runs the boot with it (the loaded game stays until the new one is ready).</summary>
        private void OnInstallChosen(string root)
        {
            if (_phase != Phase.Room)
            {
                _picker.SetMessage("The game folder can be changed only in the room, while nothing is loading.", true);
                return;
            }

            if (_accountSvc != null)
            {
                _picker.SetMessage("The server is still answering - try again in a moment.", true);
                return;
            }

            if (_files != null && WowInstall.SameFolder(root, _root))
            {
                _picker.Close();
                _installSaved = root;
                _installSavedBad = null;
                SaveChosenInstall(root);
                _room.SetStatus("That folder is already in use - it is now saved as your game folder.", false);
                _settingsTab.RefreshInstall();
                return;
            }

            if (_login != null)
            {
                DisposeLogin("the game folder is being changed");
                ShowIdle("Logged out: the game folder was changed.", false);
            }

            _picker.Close();
            StartBoot(root, null, "a folder chosen in the browser");
        }

        /// <summary>Saves a chosen folder that worked for this Somnium account (or once the account is known).</summary>
        private void SaveChosenInstall(string path)
        {
            string id = _userId, nick;

            if (id == null && !WowSaves.TryIdentity(Time.realtimeSinceStartup - _bootStart, out id, out nick))
            {
                id = null;
            }

            if (id == null)
            {
                _installSaveLater = path;
                WowLog.Info("Install: the chosen folder " + path + " is saved once the Somnium account is known.");
                return;
            }

            _installSaveLater = null;
            string report;
            bool ok = WowSaves.SaveInstallPath(id, path, out report);
            if (ok) WowLog.Info("Install: saved " + path + " as the game folder for " + WowSaves.Mask(id) + " (" + report + ").");
            else WowLog.Warn("Install: could NOT save " + path + " as the game folder for " + WowSaves.Mask(id) + " (" + report + ").");
        }

        /// <summary>Every frame: close the replaced files, save a chosen folder late, look the saved folder up late.</summary>
        private void TickInstall(float now)
        {
            if (_oldFiles != null && now >= _oldFilesCloseAt)
            {
                CloseOldFiles("the new game files took over " + OldFilesCloseDelay.ToString("0") + " s ago");
            }

            // (The saved folder is looked up late only between boots: one that ran without it first ends.)
            if (now < _nextInstallPoll || _bootThread == null || (_installSaveLater == null && (_installSavedRead || _phase == Phase.Booting)))
            {
                return;
            }

            _nextInstallPoll = now + 0.5f;
            string id, nick;

            if (!WowSaves.TryIdentity(now - _bootStart, out id, out nick))
            {
                return;
            }

            if (_installSaveLater != null)
            {
                SaveChosenInstall(_installSaveLater);
            }

            if (!_installSavedRead && _phase != Phase.Booting)
            {
                string saved = ReadSavedInstall(id);

                // The first boot ran without the account and found nothing: the saved folder now gets its turn.
                if (saved != null && _phase == Phase.Room && _files == null)
                {
                    if (_picker != null) _picker.Close();
                    StartBoot(null, saved, "the saved folder, once the Somnium account was known");
                }
                else if (saved != null && _files != null && !WowInstall.SameFolder(saved, _root))
                {
                    WowLog.Info("Install: the saved folder " + saved + " is not the one in use (" + _root + "); it is tried first next time.");
                }

                _settingsTab.RefreshInstall();
            }
        }

        private void CloseOldFiles(string why)
        {
            WowFiles f = _oldFiles;

            if (f != null && ReferenceEquals(f, WowPlay.Files) && WowPlay.Session != null && why != "leaving the world")
            {
                _oldFilesCloseAt = Time.realtimeSinceStartup + OldFilesCloseDelay;   // a running session still reads them: later
                return;
            }

            _oldFiles = null;

            if (f == null || ReferenceEquals(f, _files))
            {
                return;
            }

            try
            {
                f.Dispose();
                WowLog.Info("Install: the previous game files (" + f.Root + ") closed (" + why + ").");
            }
            catch (Exception e)
            {
                WowLog.Warn("Install: closing the previous game files threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>The Settings tab's Game page.</summary>
        private WowSettingsTab.InstallState InstallState()
        {
            return new WowSettingsTab.InstallState
            {
                Folder = _files != null ? _root : null,
                Locale = _files != null ? _locale : null,
                Source = _installSource ?? "",
                Saved = _installSaved,
                SavedProblem = _installSavedBad,
                CanChange = _phase == Phase.Room && _picker != null,
                Why = _phase == Phase.Booting ? "The game files are being read..." : _phase == Phase.Room ? "" : "Only in the room.",
            };
        }

        private void Update()
        {
            if (!_started)
            {
                return;   // before the verdict, or a visitor: nothing of the Dreamport runs (Gate)
            }

            float now = Time.realtimeSinceStartup;

            if (!_eventSystemChecked && now - _bootStart > 1.5f)
            {
                _eventSystemChecked = true;
                WowPlayer.EnsureEditorEventSystem();
            }

            if (_roster == null && _phase != Phase.Booting && now >= _nextIdentityPoll)
            {
                _nextIdentityPoll = now + 0.5f;
                TryOpenRoster(now);
            }

            if (_settingsDirty && _settingsStore != null && now - _settingsDirtyAt >= SettingsSaveDelay)
            {
                SaveSettings("changed");
            }

            if (_login != null)
            {
                PollLogin(now);   // iteration 6: the online login / realm connection (every phase)
            }

            if (_accountSvc != null)
            {
                PollAutoAccount(now);   // stage A1: the account service's answer (every phase: its key is stored whenever it arrives)
            }

            TickInstall(now);   // the game folder: replaced files closed, a late save / saved-folder lookup

            switch (_phase)
            {
                case Phase.Booting:
                    if (_bootThread == null) TryStartBoot(now);   // waits for the Somnium account (the saved folder is per account)
                    else if (_bootDone) FinishBoot();
                    KeepInRoom(now);
                    break;
                case Phase.Room:
                    KeepInRoom(now);
                    break;
                case Phase.Loading:
                    KeepInRoom(now);
                    UpdateLoading(now);
                    break;
                case Phase.World:
                    UpdateWorld(now);
                    break;
            }

            // Iteration 11 (uicore H10, PLAN11 s2 D-K2): K = Skills in the world once uicore's key table binds it (BackKeyInWorld false);
            // while the world is loading K always returns to the room.
            if ((_phase == Phase.Loading || (_phase == Phase.World && BackKeyInWorld)) && WowPlayer.BackKeyPressed())
            {
                ReturnToRoom("the " + WowPlayer.BackKeyName + " key");
            }
        }

        // ------------------------------------------------------------------ room

        private void KeepInRoom(float now)
        {
            if (!_placed)
            {
                if (now - _placeTries < 0.25f)
                {
                    return;
                }

                _placeTries = now;
                string how;

                if (WowPlayer.Teleport(_room.SpawnPoint + new Vector3(0f, 0.05f, 0f), _room.SpawnYaw, out how))
                {
                    _placed = true;
                    _lastTeleport = now;
                    WowLog.Info("Room: player placed at " + _room.SpawnPoint + " facing the panel, yaw " + _room.SpawnYaw.ToString("0") + " (" + how + "), " + (now - _bootStart).ToString("0.0") + " s after start.");
                }

                return;
            }

            if (now - _lastTeleport < 1.5f)
            {
                return;
            }

            Vector3 feet;

            if (!WowPlayer.TryFeet(out feet) || !WowRoom.IsAway(feet))
            {
                return;
            }

            string h;

            if (WowPlayer.Teleport(_room.SpawnPoint + new Vector3(0f, 0.05f, 0f), _room.SpawnYaw, out h))
            {
                _lastTeleport = now;
                _putBack++;
                if (_putBack <= 10) WowLog.Info("Room: the player was at " + feet + " - put back (" + _putBack + ").");
            }
        }

        private bool _clockSaid;

        /// <summary>Lead 2026-11-03: online the world's time of day is the realm's (PlayerState.GameTimeHours from SMSG_LOGIN_SETTIMESPEED,
        /// running in real time) - the sun, sky, fog and water colours follow it every second, as in the WoW client.</summary>
        private void FollowServerClock()
        {
            IWowSession s = WowPlay.Session;
            float h = s != null && s.Player != null ? s.Player.GameTimeHours : -1f;
            if (h < 0f) return;
            _timeOfDay = h;
            if (!_clockSaid)
            {
                _clockSaid = true;
                WowLog.Info("Play: time of day from the realm's clock - " + Mathf.FloorToInt(h).ToString("00") + ":" + Mathf.FloorToInt((h % 1f) * 60f).ToString("00") + ".");
            }
        }

        private void OnHour(float hours)
        {
            _timeOfDay = hours;
            _room.SetHour(hours);
            WowLog.Info("Room: time of day " + hours.ToString("0.0") + " h.");
        }

        private void OnToggleLineup()
        {
            _lineup = !_lineup;
            _room.SetLineup(_lineup);
            WowLog.Info("Room: model line-up " + (_lineup ? "on" : "off") + ".");
        }

        // ------------------------------------------------------------------ characters

        private void TryOpenRoster(float now)
        {
            string id, nick;

            if (!WowSaves.TryIdentity(now - _bootStart, out id, out nick))
            {
                if (!_identityWaitLogged && now - _bootStart > 5f)
                {
                    _identityWaitLogged = true;
                    WowLog.Info("Characters: waiting for the Somnium account (LocalPlayer.Properties.Id is not set yet).");
                }

                _chars.ShowWaiting("Waiting for your Somnium account...");
                return;
            }

            try
            {
                string storage;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                _roster = WowSaves.Open(id, out storage);
                _userId = id;
                _nick = string.IsNullOrEmpty(nick) ? WowSaves.FallbackName : nick;

                // The Somnium avatar IS the character: every character carries the account's current nickname.
                foreach (CharacterRecord r in _roster.Characters) r.Name = _nick;

                WowLog.Info("Characters: " + _roster.Characters.Count + " loaded for " + _nick + " (" + WowSaves.Mask(id) + ") from " + storage + " in " + watch.ElapsedMilliseconds + " ms - " + _roster.LoadReport + ".");
                foreach (CharacterRecord r in _roster.Characters) WowLog.Info("Characters: " + r + " (id " + r.Id + ", " + r.Quests.Count + " quests, " + r.Rewarded.Count + " rewarded, played " + r.PlayedSeconds + " s).");
                foreach (string p in _roster.Problems) WowLog.Warn("Characters: skipped a damaged character - " + p);

                if (_roster.ReadOnly)
                {
                    _room.SetStatus("Your characters were saved by a newer version of this world: they are shown but cannot be changed here.", true);
                }
                else if (_roster.HadDamage)
                {
                    _room.SetStatus("A saved copy of your characters was damaged; " + (_roster.Characters.Count > 0 ? "the readable characters were kept." : "nothing could be recovered (kept as a _bad file)."), true);
                }
            }
            catch (Exception e)
            {
                WowLog.Error("Characters: opening the saved characters failed: " + e);
                _roster = new WowRoster(id, new IWowSaveSlot[] { new WowMemorySlot() });
                _roster.Load();
                _room.SetStatus("Your saved characters could not be read (" + e.GetType().Name + "); new ones are kept for this visit only.", true);
            }

            OpenSettings(id);
            RefreshCharacters();
            RefreshAutoView();   // stage A1: the Online tab's "Play online as <nick>" now knows the Somnium account
        }

        // ------------------------------------------------------------------ settings (iteration 3)

        /// <summary>Loads the account's saved settings (its own key, two copies) into WowSettings once the identity is known.</summary>
        private void OpenSettings(string id)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string storage, body;
                _settingsStore = WowSaves.OpenSettings(id, out storage, out body);
                int n = 0;

                if (body != null)
                {
                    _settingsLoading = true;
                    try { n = WowSettings.Load(body); }
                    finally { _settingsLoading = false; }
                    _settingsDirty = false;
                }

                WowLog.Info("Settings: " + (body != null ? n + " values loaded" : "none saved yet (defaults)") + " for " + WowSaves.Mask(id) + " from " + storage + " in " + watch.ElapsedMilliseconds + " ms - "
                    + _settingsStore.LastReport + (_settingsStore.ReadOnly ? " (read-only: saved by a newer version of this world)" : "") + "; difficulty " + WowSettings.Difficulty + ", auto loot " + (WowSettings.AutoLoot ? "on" : "off") + ".");

                // Iteration 7 (lead hook 6): the learned mount displays (numbers only) come back from the settings.
                int learned = MountDisplays.LoadCache(WowSettings.MountDisplayCache);
                _mountCacheVersion = MountDisplays.Version;
                WowLog.Info("Mounts: display table - " + MountDisplays.Summary + " (" + learned + " learned entries restored from the settings).");
            }
            catch (Exception e)
            {
                WowLog.Error("Settings: opening the saved settings failed: " + e);
                _settingsStore = null;
            }
        }

        private void OnSettingsChanged()
        {
            OfflineSession.UnlockAllFlightPaths = WowSettings.UnlockAllFlightPaths;   // testing switch (lead, 2026-09-27)
            CopyMotionSettings();   // iteration 11 (motion hook 11)
            OfflineSession.UnlockAllMounts = WowSettings.UnlockAllMounts; OfflineSession.FlyAnywhere = WowSettings.FlyAnywhere;   // iteration 7 (lead hook 2)
            OfflineSession.CritterCount = WowSettings.AmbientLife ? OfflineSession.DefaultCritterCount : 0;   // WoW-style critters (iteration 5)
            ApplySessionSettings("settings changed");
            OnlineSettingsChanged();

            if (_settingsLoading)
            {
                return;
            }

            _settingsDirty = true;
            _settingsDirtyAt = Time.realtimeSinceStartup;
        }

        /// <summary>The settings that belong to the front end's session: difficulty and auto loot (Rules' API).</summary>
        private void ApplySessionSettings(string why)
        {
            if (_session == null)
            {
                return;
            }

            try
            {
                PlayerState ps = _session.Player;

                if (ps == null || ps.Difficulty != WowSettings.Difficulty)
                {
                    _session.SetDifficulty(WowSettings.Difficulty);
                    WowLog.Info("Play: difficulty " + (WowSettings.Difficulty == 0 ? "easy" : WowSettings.Difficulty == 2 ? "hard" : "normal") + " (" + why + ").");
                }

                if (_session.AutoLoot != WowSettings.AutoLoot)
                {
                    _session.AutoLoot = WowSettings.AutoLoot;
                    WowLog.Info("Play: auto loot " + (WowSettings.AutoLoot ? "on" : "off") + " (" + why + ").");
                }
            }
            catch (Exception e)
            {
                WowLog.Error("Play: applying the settings failed (" + why + "): " + e);
            }
        }

        /// <summary>Writes the settings when they changed (debounced in Update; at once when leaving).</summary>
        // ---- Iteration 7 (lead hooks 6 and 7, PLAN7 s3.10) -------------------------------------------------------------
        private int _mountCacheVersion;
        private float _mountCacheCheckAt;
        private WowFiles _mountDataWarmed;

        /// <summary>Once a second in play: a mount display learned from a realm is saved with the settings (numbers only).</summary>
        private void TickMountCache(float now)
        {
            if (now < _mountCacheCheckAt)
            {
                return;
            }

            _mountCacheCheckAt = now + 1f;
            int v = MountDisplays.Version;

            if (v == _mountCacheVersion)
            {
                return;
            }

            _mountCacheVersion = v;
            WowSettings.MountDisplayCache = MountDisplays.SaveCache();
            _settingsDirty = true;
            SaveSettings("mount display cache");
        }

        /// <summary>SpellDb (~47 MB on its first read), the mount catalogue, the area rules and WMOAreaTable, on a worker; once per WowFiles.</summary>
        private void WarmMountData(WowFiles files, string why)
        {
            if (files == null || ReferenceEquals(files, _mountDataWarmed))
            {
                return;
            }

            _mountDataWarmed = files;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    SpellDb.Get(files);
                    MountCatalog.Load(files);
                    MountAreaRules.Load(files);
                    WmoAreaTable.Get(files);
                    WowLog.Info("Mounts: data warmed in " + watch.ElapsedMilliseconds + " ms (" + why + ") - catalogue " + MountCatalog.Summary + ".");
                }
                catch (Exception e)
                {
                    WowLog.Error("Mounts: warming the mount data failed (" + why + "): " + e);
                }
            });
        }

        /// <summary>Lead (2026-10-04, the user: "when someone spends time doing it, that it properly saves"): an explicit save (the Armour
        /// page's Save button) - written to both storage copies at once instead of SettingsSaveDelay later. False when nothing could be
        /// written (no account store yet).</summary>
        public static bool SaveSettingsNow(string why)
        {
            WowGame g = Current;
            if (g == null || g._settingsStore == null) return false;
            g._settingsDirty = true;
            g.SaveSettings(why);
            return !g._settingsDirty;
        }

        private void SaveSettings(string why)
        {
            if (!_settingsDirty)
            {
                return;
            }

            _settingsDirty = false;

            if (_settingsStore == null)
            {
                return;
            }

            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool ok = _settingsStore.Save(WowSettings.Serialize());

                if (ok) WowLog.Info("Settings: saved " + _settingsStore.LastBytes + " bytes, gen " + _settingsStore.Generation + ", " + watch.ElapsedMilliseconds + " ms - " + _settingsStore.LastReport + " (" + why + ").");
                else WowLog.Warn("Settings: NOT saved - " + _settingsStore.LastReport + " (" + why + ").");
            }
            catch (Exception e)
            {
                WowLog.Error("Settings: saving threw (" + why + "): " + e);
            }
        }

        private void RefreshCharacters()
        {
            if (_roster == null)
            {
                _chars.ShowWaiting("Waiting for your Somnium account...");
                return;
            }

            string note = _roster.ReadOnly ? "Read-only: saved by a newer version of this world." : "";
            _chars.SetRoster(_roster.Characters, _nick, _roster.ReadOnly, note);
        }

        private void OnCreateCharacter(WowClass cls, int race)
        {
            if (_phase == Phase.Room && _chars.Online)
            {
                OnlineCreate(cls, race);   // iteration 6: the server creates it (the name step first)
                return;
            }

            if (_phase != Phase.Room || _roster == null)
            {
                return;
            }

            CharacterRecord rec;

            try
            {
                if (!Play.Rules.WowRaces.CanBe(_files, race, cls))
                {
                    WowLog.Warn("Character create: race " + race + " cannot be a " + cls + " (CharBaseInfo.dbc) - not created.");
                    _room.SetStatus("That race cannot be a " + cls + ".", true);
                    return;
                }

                if (WowZones.ForRace(race) == null)
                {
                    string zn = WowZones.DisplayNameForRace(race);
                    WowLog.Warn("Character create: the starting zone of race " + race + " (" + zn + ") is not ready - not created.");
                    _room.SetStatus((zn.Length > 0 ? zn : "This race's starting zone") + " is not ready in this version - choose another race.", true);
                    return;
                }

                rec = CharacterRecord.CreateNew(cls, race, _nick);
            }
            catch (Exception e)
            {
                WowLog.Error("Class screen: creating a " + cls + " failed: " + e);
                _room.SetStatus("The character could not be created (" + e.GetType().Name + ": " + e.Message + ").", true);
                return;
            }

            string refusal;

            if (!_roster.Add(rec, out refusal))
            {
                WowLog.Warn("Class screen: " + cls + " not created - " + refusal + ".");
                _room.SetStatus("Not created: " + refusal + ".", true);
                return;
            }

            WowLog.Info("Class screen: created " + rec + " (id " + rec.Id + ", " + rec.Spells.Count + " start spells, items " + string.Join(",", Array.ConvertAll(rec.Equipment, x => x.ToString())) + ").");
            Commit("created " + rec);
            RefreshCharacters();
            OnPlayCharacter(rec);
        }

        private void OnDeleteCharacter(CharacterRecord rec)
        {
            if (_phase != Phase.Room || _roster == null || rec == null)
            {
                return;
            }

            if (_roster.Remove(rec.Id))
            {
                WowLog.Info("Characters: deleted " + rec + " (id " + rec.Id + ").");
                Commit("deleted " + rec);
                _room.SetStatus("Deleted " + rec + ".", false);
            }
            else
            {
                _room.SetStatus("Could not delete " + rec + (_roster.ReadOnly ? " (read-only)." : "."), true);
            }

            RefreshCharacters();
            _chars.ShowList();
        }

        /// <summary>Writes the roster and logs "Save: ok N bytes ..." (or why not).</summary>
        private bool Commit(string why)
        {
            if (_roster == null)
            {
                return false;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok;

            try
            {
                ok = _roster.Commit(why);
            }
            catch (Exception e)
            {
                WowLog.Error("Save: commit threw (" + why + "): " + e);
                return false;
            }

            if (ok)
            {
                WowLog.Info("Save: ok " + _roster.LastBytes + " bytes, gen " + _roster.Generation + ", " + _roster.Characters.Count + " character(s), " + watch.ElapsedMilliseconds + " ms - " + _roster.LastCommitReport + ".");
            }
            else
            {
                WowLog.Warn("Save: FAILED - " + _roster.LastCommitReport + ".");
            }

            return ok;
        }

        // ------------------------------------------------------------------ loading

        private void OnPick(WowLocation loc)
        {
            if (_phase != Phase.Room || _files == null || loc == null)
            {
                return;
            }

            _mode = Mode.Sightseeing;
            StartLoading(loc, loc.Label + " - " + loc.MapName);
        }

        private void OnPlayCharacter(CharacterRecord rec)
        {
            if (_phase != Phase.Room || _files == null || rec == null)
            {
                return;
            }

            // Iteration 3: the race's starting zone (Rules' registry). Old saves without a race load as Night Elves (Shadowglen).
            ZoneContent zone = null;
            try { zone = WowZones.ForRace(rec.Race); }
            catch (Exception e) { WowLog.Error("Play: WowZones.ForRace(" + rec.Race + ") threw: " + e); }

            if (zone == null)
            {
                string zn = WowZones.DisplayNameForRace(rec.Race);
                WowLog.Warn("Play: no zone for race " + rec.Race + " (" + zn + ") - " + rec + " cannot be played in this version.");
                _room.SetStatus((zn.Length > 0 ? zn : "This character's starting zone") + " is not ready in this version, so " + rec.Name + " cannot be played yet.", true);
                return;
            }

            _zone = zone;
            rec.Name = _nick;
            float dx = rec.Pos.X - _zone.PlayerStart.X, dy = rec.Pos.Y - _zone.PlayerStart.Y;
            bool bad = float.IsNaN(rec.Pos.X) || float.IsNaN(rec.Pos.Y) || float.IsNaN(rec.Pos.Z) || float.IsInfinity(rec.Pos.Z);

            if (rec.HasPos && (rec.MapId != _zone.MapId || bad || dx * dx + dy * dy > MaxStartDistance * MaxStartDistance))
            {
                WowLog.Warn("Play: the saved position " + rec.Pos + " on map " + rec.MapId + " is not in " + _zone.Name + " - starting at the zone's start.");
                rec.HasPos = false;
            }

            WowPos p = rec.HasPos && rec.MapId == _zone.MapId ? rec.Pos : _zone.PlayerStart;
            _playRecord = rec;
            _playYaw = WowSpace.OrientationToYaw(p.O);
            string area = null;
            if (_zone.MapId == 1 && string.Equals(_zone.Name, "Shadowglen", StringComparison.Ordinal))
            {
                try { area = WowAreaLookup.AreaName(_files, Shadowglen.AreaShadowglen); } catch (Exception) { area = null; }
            }

            var loc = new WowLocation
            {
                Label = string.IsNullOrEmpty(area) ? _zone.Name : area,
                Source = "character " + rec.Id,
                DbcName = _zone.Name,
                MapId = _zone.MapId,
                MapName = WowLocations.MapName(_files, _zone.MapId),
                X = p.X,
                Y = p.Y,
                Z = p.Z,
            };

            WowClassEntry ce = WowClassInfo.Find(_classes, rec.Class);
            WowRaceEntry re = WowRaceInfo.Find(_races, rec.Race);
            WowLog.Info("Play: " + rec.Name + " " + (re != null ? re.Name + " " : "race " + rec.Race + " ") + (ce != null ? ce.Name : rec.Class.ToString()) + " " + rec.Level + " (id " + rec.Id + ") in " + _zone.Name + " (map " + _zone.MapId + ") at " + p + (rec.HasPos ? " (saved position)" : " (zone start)") + ", " + _timeOfDay.ToString("0.0") + " h.");
            _mode = Mode.Play;
            StartPreload();
            StartLoading(loc, loc.Label + " - " + loc.MapName);
        }

        /// <summary>The spell tables, Item.dbc and the gt tables on a worker while the map streams (cached per WowFiles).</summary>
        private void StartPreload()
        {
            if (_preloadDone || _preloadThread != null)
            {
                return;
            }

            WowFiles files = _files;
            _preloadLogged = false;
            _preloadThread = new Thread(() =>
            {
                var w = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    OfflineSession.Preload(files);
                }
                catch (Exception e)
                {
                    _preloadError = e.GetType().Name + ": " + e.Message;
                }
                finally
                {
                    _preloadMs = w.ElapsedMilliseconds;
                    _preloadDone = true;
                }
            }) { IsBackground = true, Name = "WoW play preload" };
            _preloadThread.Start();
        }

        private void StartLoading(WowLocation loc, string title)
        {
            _current = loc;
            // Floating origin (PLAN4 section 2): nothing of WoW is loaded now, so the origin simply becomes the grid point
            // next to the spot; everything loaded from here on is built around it (the room stays at LoginRoomOrigin).
            WowOrigin.Cancel("loading " + loc.Label);
            WowSpace.SetOrigin(loc.MapId, loc.X, loc.Y, loc.Z);
            _spot = WowSpace.ToUnity(loc.MapId, loc.X, loc.Y, loc.Z);
            WowLog.Info("Load: " + loc + " -> Unity " + _spot.ToString("F1") + ", " + _timeOfDay.ToString("0.0") + " h, " + (_mode == Mode.Play ? "play" : "line-up " + (_lineup ? "on" : "off")) + ".");
            _room.SetInteractive(false);
            _chars.CloseDialog();
            _room.SetStatus("Loading " + loc.Label + "...", false);

            try
            {
                _loading.Show(_files, loc.MapId, title);
            }
            catch (Exception e)
            {
                WowLog.Error("Loading screen failed: " + e);
            }

            _room.SetPanelShown(false);

            try
            {
                if (_map == null)
                {
                    var go = new GameObject("Wow Map View");
                    go.transform.SetParent(transform, false);
                    _map = go.AddComponent<WowMapView>();
                }

                _map.Load(_files, loc.MapId, _spot);
            }
            catch (Exception e)
            {
                Fail("The map could not be loaded (WowMapView.Load: " + e.GetType().Name + ": " + e.Message + ").", e);
                return;
            }

            _loadStart = Time.realtimeSinceStartup;
            _readySince = -1f;
            _mapErrorLogged = _focusErrorLogged = _readyErrorLogged = _groundErrorLogged = false;
            _phase = Phase.Loading;
        }

        /// <summary>Back to the room panel with a red status line (the path or call that failed).</summary>
        private void Fail(string message, Exception e)
        {
            WowLog.Error("Load: " + message + (e != null ? "\n" + e : ""));

            if (_mode == Mode.Online)
            {
                EndOnline("loading failed");   // iteration 6: the character is logged out again
            }

            try { if (_map != null) _map.Unload(); }
            catch (Exception) { }

            _loading.Hide();
            _room.SetShown(true);
            _room.SetPanelShown(true);
            _room.SetInteractive(true);
            _room.SetStatus(message, true);
            _playRecord = null;
            _phase = Phase.Room;

            if (_mode == Mode.Online && WowSettings.OnlineMode)
            {
                _chars.ShowList();
                _onlineUi.SetVisible(true);
                ShowOnlineView();
                SetRoomStatus(message, true);
                _messageUntil = Time.realtimeSinceStartup + 8f;
            }
        }

        private void UpdateLoading(float now)
        {
            float progress = 0f;
            bool ready = false;

            if (_online != null)
            {
                // Iteration 6: a far teleport's reload - the realm keeps talking, the session keeps ticking (no position is sent).
                PumpSession(now, Vector3.zero, false);

                if (_phase != Phase.Loading)
                {
                    return;
                }
            }

            try
            {
                progress = _map.Progress01;
                _map.SetFocus(_spot);
            }
            catch (Exception e)
            {
                if (!_mapErrorLogged) { _mapErrorLogged = true; WowLog.Error("Load: WowMapView.Progress01/SetFocus: " + e); }
            }

            try
            {
                ready = _map.IsReadyAround(_spot, ReadyRadius);
            }
            catch (Exception e)
            {
                if (!_readyErrorLogged) { _readyErrorLogged = true; WowLog.Error("Load: WowMapView.IsReadyAround: " + e); }
            }

            float elapsed = now - _loadStart;
            bool play = _mode == Mode.Play || _mode == Mode.Online;

            if (play && _preloadDone && !_preloadLogged)
            {
                _preloadLogged = true;
                if (_preloadError != null) WowLog.Error("Play: OfflineSession.Preload failed after " + _preloadMs + " ms: " + _preloadError);
                else WowLog.Info("Play: spells, items and game tables read in " + _preloadMs + " ms (worker thread).");
            }

            bool spellsReady = !play || _preloadDone;
            _loading.SetProgress(ready && spellsReady ? 1f : Mathf.Min(progress, spellsReady ? 1f : 0.97f), elapsed.ToString("0") + " s" + (spellsReady ? "" : "   reading spells..."));

            if (ready && spellsReady)
            {
                if (_readySince < 0f)
                {
                    _readySince = now;
                    WowLog.Info("Load: ready around the spot after " + elapsed.ToString("0.0") + " s (Progress01 " + progress.ToString("0.00") + ").");
                }

                // Colliders (terrain or a building floor) under the spot, or 4 s more for them - 15 s when the DBC z is
                // more than 8 m off the terrain (a building floor is expected: Undercity below it, Dalaran above it).
                RaycastHit hit;
                bool floor = Physics.Raycast(_spot + Vector3.up * 2f, Vector3.down, out hit, 30f, GroundMask, QueryTriggerInteraction.Ignore);
                // Integrator (iteration 3): a floor clearly BELOW the position means the building the position stands on is
                // not built yet (Ammen Vale's start platform is 0.96 m above the terrain; a saved spot on a WMO floor): wait up
                // to 10 s for its collider instead of arriving inside it (lab r1 arrived on the terrain 0.96 m below the start).
                bool below = floor && _spot.y - hit.point.y > 0.35f;
                if (below) floor = false;
                float gy;
                float wait = TryGround(_spot, out gy) && Mathf.Abs(gy - _spot.y) > 8f ? 15f : below ? 10f : 4f;

                if (floor || now - _readySince > wait)
                {
                    Arrive(now, "ready after " + elapsed.ToString("0.0") + " s");
                }

                return;
            }

            if (elapsed > LoadTimeout)
            {
                float gy;

                bool ground = spellsReady && TryGround(_spot, out gy);
                bool wmoOnly = !ground && spellsReady && _map != null && _map.IsWmoOnly;   // iteration 11 (world hook 7; false until world fills IsWmoOnly)
                if (ground)
                {
                    Arrive(now, "NOT ready after " + LoadTimeout.ToString("0") + " s (Progress01 " + progress.ToString("0.00") + ") - arriving on the terrain height anyway");
                }
                else if (wmoOnly)
                {
                    Arrive(now, "NOT ready after " + LoadTimeout.ToString("0") + " s - WMO-only map, arriving at the server's position");
                }
                else
                {
                    Fail("The map did not become ready around " + _current.Label + " in " + LoadTimeout.ToString("0") + " s (Progress01 " + progress.ToString("0.00") + (spellsReady ? ", no ground height" : ", spells still being read") + ").", null);
                }
            }
        }

        private bool TryGround(Vector3 p, out float y)
        {
            y = 0f;

            try
            {
                return _map != null && _map.TryGroundHeight(p, out y);
            }
            catch (Exception e)
            {
                if (!_groundErrorLogged) { _groundErrorLogged = true; WowLog.Error("WowMapView.TryGroundHeight: " + e); }
                return false;
            }
        }

        /// <summary>The floor under a position: a collider under it (terrain or a building floor, e.g. Undercity, Dalaran, Aldrassil), else the terrain height (never below the given z), else the given z.</summary>
        private string FloorHeight(Vector3 spot, out float y)
        {
            RaycastHit hit;

            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out hit, 30f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return "collider '" + hit.collider.name + "' " + (spot.y - y).ToString("0.00") + " m below the position";
            }

            float gy;

            if (TryGround(spot, out gy))
            {
                y = Mathf.Max(gy, spot.y);
                return "terrain height " + gy.ToString("0.00") + (gy >= spot.y ? "" : " (below the given z " + spot.y.ToString("0.00") + ": the given z is used)");
            }

            y = spot.y;
            return "the given z (no collider, no terrain height)";
        }

        /// <summary>The facing (0, 45 ... 315 degrees, north first) with open level ground 5 m ahead for the line-up.</summary>
        private float ChooseFacing(Vector3 at, out string why)
        {
            float bestYaw = 0f, bestScore = float.MaxValue;

            for (int k = 0; k < 8; k++)
            {
                float yaw = k * 45f;
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                float score = 0f;
                bool ok = true;

                foreach (float side in new[] { 0f, -3f, 3f })
                {
                    Vector3 p = at + q * new Vector3(side, 0f, 5f);

                    if (Physics.Linecast(at + Vector3.up * 1.2f, p + Vector3.up * 1.2f, GroundMask, QueryTriggerInteraction.Ignore))
                    {
                        ok = false;
                        break;
                    }

                    float gy;

                    if (!GroundAt(p, at.y, out gy) || Mathf.Abs(gy - at.y) > 1.5f)
                    {
                        ok = false;
                        break;
                    }

                    score += Mathf.Abs(gy - at.y);
                }

                if (ok && score < bestScore - 0.05f)
                {
                    bestScore = score;
                    bestYaw = yaw;
                }
            }

            why = bestScore < float.MaxValue ? "open ground at yaw " + bestYaw.ToString("0") + " (height spread " + bestScore.ToString("0.00") + " m)" : "no open level direction found - yaw 0 (north)";
            return bestYaw;
        }

        /// <summary>The ground at p: a collider within 3 m above / 6 m below nearY, else the terrain height.</summary>
        public bool GroundAt(Vector3 p, float nearY, out float y)
        {
            RaycastHit hit;

            if (Physics.Raycast(new Vector3(p.x, nearY + 3f, p.z), Vector3.down, out hit, 9f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return true;
            }

            return TryGround(p, out y);
        }

        private void Arrive(float now, string readiness)
        {
            float y;
            string how = FloorHeight(_spot, out y);
            Vector3 at = new Vector3(_spot.x, y + 0.1f, _spot.z);
            string facing;
            float yaw;

            if (_mode == Mode.Play || _mode == Mode.Online)
            {
                yaw = _playYaw;
                facing = _mode == Mode.Online ? "the server's orientation" : "the character's saved facing";
            }
            else
            {
                yaw = ChooseFacing(at, out facing);
            }

            _arrival = at;
            _arrivalYaw = yaw;

            string tp;
            bool moved = WowPlayer.Teleport(at, yaw, out tp);
            _teleportAt = now;
            _repeatSince = now;
            _teleportLanded = false;
            _teleportTries = 1;
            WowLog.Info("Arrive: " + _current.Label + " (" + readiness + "): ground " + how + "; teleport to " + at.ToString("F2") + " yaw " + yaw.ToString("0") + " (" + facing + ") - " + (moved ? tp : "FAILED: " + tp) + ".");

            try
            {
                FollowServerClock();
                WowLighting.Apply(_files, _current.MapId, at, _timeOfDay);
            }
            catch (Exception e)
            {
                WowLog.Error("Arrive: WowLighting.Apply failed: " + e);
            }

            _loading.Hide();
            _room.SetShown(false);

            if (_mode == Mode.Sightseeing && _lineup)
            {
                try
                {
                    var go = new GameObject("Wow Line-up");
                    go.transform.SetParent(transform, false);
                    go.transform.SetPositionAndRotation(new Vector3(at.x, y, at.z), Quaternion.Euler(0f, yaw, 0f));
                    _lineupObject = go.AddComponent<WowLineup>();
                    _lineupObject.Begin(_files, this);
                }
                catch (Exception e)
                {
                    WowLog.Error("Arrive: line-up failed: " + e);
                }
            }

            _wrist.Show(_current.Label + " - " + _current.MapName, _current.DbcName);
            _worldSince = now;
            _lastSafe = at;
            _lastSafeKnown = true;
            _noGround = 0f;
            _fallRescues = 0;
            _nextLight = now + 1f;
            _areaName = null;
            _areaShown = null;
            _nextArea = now;
            _phase = Phase.World;
            WowLog.Info("Arrive: " + WowTextures.StatsLine());
            // Integrator: the Models and Buildings summaries after the map load (their owners' requests).
            try { WowLog.Info("Arrive: " + SomniumSpace.Worlds.SomniumWoW.Models.M2Factory.StatsLine()); } catch (Exception) { }
            try { WowLog.Info("Arrive: " + WmoFactory.StatsLine()); } catch (Exception) { }

            if (_mode == Mode.Play)
            {
                StartSession(now, at);
            }
            else if (_mode == Mode.Online)
            {
                ArriveOnline(now, at);   // iteration 6: OnlineSession + WowPlay.Start (or a far teleport's arrival)
            }
        }

        // ------------------------------------------------------------------ play

        private void StartSession(float now, Vector3 at)
        {
            if (_playRecord == null || _zone == null)
            {
                return;
            }

            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();

                // The record starts on the floor the avatar was put on (the session keeps the saved x, y and facing).
                if (_playRecord.HasPos)
                {
                    float wx, wy, wz;
                    WowSpace.ToWow(_zone.MapId, at - new Vector3(0f, 0.1f, 0f), out wx, out wy, out wz);
                    _playRecord.Pos = new WowPos(_playRecord.Pos.X, _playRecord.Pos.Y, wz, _playRecord.Pos.O);
                }

                OfflineSession.UnlockAllFlightPaths = WowSettings.UnlockAllFlightPaths;   // testing switch (lead, 2026-09-27)
                CopyMotionSettings();   // iteration 11 (motion hook 11)
                OfflineSession.UnlockAllMounts = WowSettings.UnlockAllMounts; OfflineSession.FlyAnywhere = WowSettings.FlyAnywhere;   // iteration 7 (lead hook 2)
                OfflineSession.CritterCount = WowSettings.AmbientLife ? OfflineSession.DefaultCritterCount : 0;   // WoW-style critters (iteration 5)
                _session = new OfflineSession(_files, _zone, _playRecord, GroundZ);
                _events = 0;
                _speed = 0f;
                _lastFeetKnown = false;
                _holdPositionUntil = now + 2f;
                _holdTarget = at;
                _facingSource = null;
                _saveSoon = false;
                _nextSave = now + AutosaveSeconds;
                _lastSaveAt = now;
                ApplySessionSettings("start");
                WowPlay.Start(_session, _files);
                PlayerState ps = _session.Player;
                WowLog.Info("Play: session started in " + watch.ElapsedMilliseconds + " ms - " + ps.Name + ", level " + ps.Level + " " + ps.Class + ", " + ps.Health + "/" + ps.MaxHealth + " health, "
                    + ps.Spellbook.Count + " spells, " + ps.QuestLog.Count + " quests, " + _session.Entities.Count + " entities; player at " + ps.Pos + ".");
            }
            catch (Exception e)
            {
                WowLog.Error("Play: starting the offline session failed: " + e);
                _session = null;
                ReturnToRoom("the game could not start (" + e.GetType().Name + ": " + e.Message + ")");
                _room.SetStatus("The game could not start: " + e.GetType().Name + ": " + e.Message, true);
            }
        }

        /// <summary>The session's ground callback: WoW (x, y) near zHint -> the terrain's WoW z in yards, NaN while the tile is not loaded.</summary>
        private float GroundZ(float x, float y, float zHint)
        {
            int map = _current != null ? _current.MapId : _zone != null ? _zone.MapId : 1;   // the loaded map (online: the server's)
            float uy;
            Vector3 at = WowSpace.ToUnity(map, x, y, zHint);

            // Lead fix (2026-10-03, the user: "On Somnium, I see people who are playing wow walking through floors" - Stormwind): this was
            // the TERRAIN height only, and the session glides walking players / creatures along it between their packets
            // (OnlineSession.Movement Extrapolate snaps to it within 4 yd) - inside a city built of WMO floors above the terrain they sank
            // to the terrain under the floor. Now the first floor collider under the hint (terrain or building, the GroundMask the arrival
            // uses; callers pass a hint ~2 yd above the expected feet), within GroundProbeDown, else the terrain height as before.
            RaycastHit hit;
            if (Physics.Raycast(at, Vector3.down, out hit, GroundProbeDown, GroundMask, QueryTriggerInteraction.Ignore))
            {
                uy = hit.point.y;
            }
            else if (!TryGround(at, out uy))
            {
                return float.NaN;
            }

            float wx, wy, wz;
            WowSpace.ToWow(map, new Vector3(0f, uy, 0f), out wx, out wy, out wz);
            return wz;
        }

        /// <summary>
        /// WowOrigin asks every frame: true only while settled in the world (not loading, no teleport of ours on its way,
        /// not right after one), so a rebase never runs during a loading screen or fights an arrival/graveyard teleport.
        /// </summary>
        public bool OriginMayRebase
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                return _phase == Phase.World && _teleportLanded && now - _teleportAt > 2.5f && now > _holdPositionUntil;
            }
        }

        /// <summary>WowSpace.Rebased: the Unity positions this class keeps move with the world (the objects were moved by WowOrigin).</summary>
        private void OnOriginRebased(Vector3 d)
        {
            _spot -= d;
            _arrival -= d;
            _lastSafe -= d;
            _holdTarget -= d;
            _lastFeet -= d;
        }

        /// <summary>Every frame in play: the avatar -> the session, Tick, the events fanned out, the saves.</summary>
        private void PumpSession(float now, Vector3 feet, bool haveFeet)
        {
            IWowSession s = ActiveSession;   // offline, or online (iteration 6)

            if (s == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            TickMountCache(now);

            // Iteration 7 (lead hook 3): while a ride owns the avatar the session gets the MOUNT's feet and heading (never the seated root).
            bool riding = WowMount.OwnsAvatar;

            if (riding)
            {
                feet = WowMount.Feet;
                haveFeet = true;
            }
            else if (WowVehicle.OwnsAvatar)   // iteration 11 (pets H7a): a vehicle seat holds the avatar - the seat's feet
            {
                feet = WowVehicle.Feet;
                haveFeet = true;
            }

            bool valid = haveFeet && (feet - WowSpace.LoginRoomOrigin).magnitude > 60f && !WowOrigin.Pending;   // (a rebase teleport on its way: feet may be in the new frame already)

            // Online: while the session is on another map than the one loaded (a far teleport on its way) nothing is sent.
            if (valid && _current != null && s.MapId != _current.MapId)
            {
                valid = false;
            }

            // Right after a teleport the avatar may still stand at the old place for a few frames: hold the position.
            if (valid && !riding && now < _holdPositionUntil && (feet - _holdTarget).magnitude > 10f)
            {
                valid = false;
            }

            if (valid)
            {
                if (riding)
                {
                    _speed = 0f;   // the ride says whether it moves (WowMount.Moving); no smoothing of the avatar's speed
                }
                else if (_lastFeetKnown && dt > 1e-4f)
                {
                    Vector3 d = feet - _lastFeet;
                    float sink = Mathf.Abs(d.y) > 5f ? 0f : -d.y / dt;   // lead fix 2026-11-05: m/s the feet go down
                    _sinkSpeed = Mathf.Lerp(_sinkSpeed, sink, 1f - Mathf.Exp(-dt * 4f));
                    d.y = 0f;
                    float inst = d.magnitude > 5f ? 0f : d.magnitude / dt;
                    _speed = Mathf.Lerp(_speed, inst, 1f - Mathf.Exp(-dt * 10f));
                }

                _lastFeet = feet;
                _lastFeetKnown = true;
                float x, y, z;
                WowSpace.ToWow(s.MapId, feet, out x, out y, out z);
                float yaw;
                string source;
                float o;

                if (riding)
                {
                    source = "mount";
                    o = WowSpace.YawToOrientation(WowMount.HeadingYaw);
                }
                else
                {
                    o = WowPlayer.TryFacingYaw(out yaw, out source) ? WowSpace.YawToOrientation(yaw) : s.Player.Pos.O;
                }

                if (source != _facingSource)
                {
                    _facingSource = source;
                    WowLog.Info("Play: facing from the " + source + ".");
                }

                try
                {
                    PlayerMotion footMotion = WowMount.Motion;   // iteration 7 (lead hook 3): default (Driven false) on foot
                    if (!footMotion.Driven && WowSwim.Active) footMotion = WowSwim.Motion;   // lead 2026-10-26: swimming on foot (rider sender: swim flags)
                    if (!footMotion.Driven) footMotion.Walking = WowRunSpeed.DesktopWalking;   // lead 2026-10-03: desktop walks by default, Shift runs
                    // lead fix 2026-10-04 (VR eating cancelled): a seated VR avatar's sway is not a walk (OnlineSession.SeatedHold)
                    if (!footMotion.Driven && WowPlayer.XrPresent) footMotion.SeatedSway = SeatedSwayMetres / WowSpace.MetresPerYard;
                    s.SetPlayerMotion(footMotion);
                    s.SetPlayerPosition(new WowPos(x, y, z, o), riding ? WowMount.Moving : FootMoving());
                }
                catch (Exception e)
                {
                    TickError("SetPlayerPosition", e, now);
                }
            }

            try
            {
                s.Tick(dt);
            }
            catch (Exception e)
            {
                TickError("Tick", e, now);
            }

            GameEvent ev;
            int n = 0;

            while (n < 512)
            {
                bool got;

                try
                {
                    got = s.TryDequeueEvent(out ev);
                }
                catch (Exception e)
                {
                    TickError("TryDequeueEvent", e, now);
                    break;
                }

                if (!got)
                {
                    break;
                }

                n++;
                _events++;
                OnSessionEvent(ev, now);
                WowPlay.Publish(ev);

                if (ActiveSession != s)
                {
                    return;
                }
            }

            if (_online != null)
            {
                CheckOnlineMap(now);   // iteration 6: the server keeps the character; nothing to save here
                return;
            }

            // The 30 s autosave waits while fighting (two encrypted file writes on the main thread; ETS measured ~8 ms),
            // at most 90 s; saves asked for by level-ups, quests, loot and death go ahead 3 s after the last save.
            bool autosave = now >= _nextSave && (!InCombat(s) || now >= _nextSave + 90f);

            if (autosave || (_saveSoon && now - _lastSaveAt > 3f))
            {
                SaveSession(_saveSoon ? _saveSoonWhy : "autosave", now);
            }
        }

        private static bool InCombat(IWowSession s)
        {
            try { return s.Player != null && s.Player.InCombat; }
            catch (Exception) { return false; }
        }

        private void TickError(string what, Exception e, float now)
        {
            if (now - _tickErrorAt < 10f)
            {
                return;
            }

            _tickErrorAt = now;
            WowLog.Error("Play: " + (_online != null ? "OnlineSession." : "OfflineSession.") + what + " threw (logged at most every 10 s): " + e);
        }

        /// <summary>What the front end itself does with a game event: teleports, and save requests.</summary>
        private void OnSessionEvent(GameEvent e, float now)
        {
            switch (e.Type)
            {
                case GameEventType.Teleport:
                    if (!OnlineTeleport(e.Pos, now)) TeleportInWorld(e.Pos, now, "the game moved the player");   // online: another map = far teleport
                    break;
                case GameEventType.LevelUp:
                    RequestSave("level " + e.Amount);
                    break;
                case GameEventType.QuestAccepted:
                case GameEventType.QuestCompleted:
                case GameEventType.QuestRewarded:
                case GameEventType.QuestAbandoned:
                case GameEventType.QuestProgress:
                    RequestSave(e.Type + " " + e.QuestId);
                    break;
                case GameEventType.PlayerDied:
                case GameEventType.PlayerResurrected:
                case GameEventType.LootReceived:
                    RequestSave(e.Type.ToString());
                    break;
            }
        }

        private void RequestSave(string why)
        {
            if (!_saveSoon)
            {
                _saveSoonWhy = why;
            }

            _saveSoon = true;
        }

        /// <summary>A Teleport event (e.g. Release Spirit -> the graveyard): the avatar onto the floor there, facing as the game says.</summary>
        // Lead fix 2026-10-04 (the user: "I hearthstoned to Stormwind and fell through the map"; Player.log: "Play: teleport to (-8866.6,
        // 671.4, 97.9) (the game moved the player): ground the given z (no collider, no terrain height)"): a teleport inside the map to a
        // place that has not streamed in yet put the player at the server's z over nothing - the 2 s hold ran out first, and the fall
        // guard's "last safe ground" was that same empty spot. Now, as TransportCarry's ship arrival: the avatar is HELD (AvatarHold:
        // gravity and collision off) at the target until a floor collider is under it, or the map is ready around it with a terrain
        // height, then set down on that floor; at most GroundWaitMax s (ours), then let go where it is.
        public const float GroundWaitMax = 30f;
        private bool _skyErrorLogged, _sharpErrorLogged;   // lead 2026-10-10
        private Game.Motion.AvatarHold _groundHold;
        private WowPos _groundHoldPos;
        private int _groundHoldMap;
        private float _groundHoldYaw, _groundHoldSince;
        private bool _groundHoldDrives;

        private void StartGroundHold(WowPos p, int map, float yaw, float now)
        {
            ReleaseGroundHold(null, false);
            Transform root = WowPlayer.Root;
            var hold = new Game.Motion.AvatarHold { Tag = "Teleport" };
            string why;
            if (!hold.TakeOver(root, false, out why)) { WowLog.Warn("Play: the teleport target has no ground yet and the avatar could not be held (" + why + ") - the fall guard watches."); return; }
            _groundHold = hold;
            _groundHoldPos = p;
            _groundHoldMap = map;
            _groundHoldYaw = yaw;
            _groundHoldSince = now;
            _groundHoldDrives = !WowOrigin.AvatarDriven;
            WowOrigin.AvatarDriven = true;   // a rebase while held moves the avatar with the world (TransportCarry does the same)
            WowLog.Info("Play: the teleport target has not loaded yet - the avatar is held there until its floor is in (at most " + GroundWaitMax + " s).");
        }

        /// <summary>Each frame while held: kept on the target (from its WoW position, so a rebase cannot move it away), set down once
        /// the ground is there.</summary>
        private void TickGroundHold(float now)
        {
            if (_groundHold == null) return;
            if (WowFlight.Flying || WowMount.OwnsAvatar || WowVehicle.OwnsAvatar || WowForcedMove.OwnsAvatar) { ReleaseGroundHold("another ride took the avatar", false); return; }
            Vector3 spot = WowSpace.ToUnity(_groundHoldMap, _groundHoldPos.X, _groundHoldPos.Y, _groundHoldPos.Z);
            bool vr = WowPlayer.XrPresent;
            _groundHold.Write(spot, _groundHoldYaw, vr);
            RaycastHit hit;
            bool floor = Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out hit, 30f, GroundMask, QueryTriggerInteraction.Ignore);
            float gy;
            bool ready = floor || (_map != null && _map.IsReadyAround(spot, 10f) && TryGround(spot, out gy));
            if (!ready)
            {
                if (now - _groundHoldSince > GroundWaitMax) ReleaseGroundHold("no ground after " + GroundWaitMax + " s - let go where it is", false);
                return;
            }
            float y;
            string how = FloorHeight(spot, out y);
            Vector3 at = new Vector3(spot.x, y + 0.1f, spot.z);
            _groundHold.Write(at, _groundHoldYaw, vr);
            ReleaseGroundHold("the ground loaded after " + (now - _groundHoldSince).ToString("0.0") + " s: " + how, true, at);
            _arrival = at;
            _teleportAt = now;
            _repeatSince = now;
            _teleportLanded = false;
            _teleportTries = 1;
            _lastSafe = at;
            _lastSafeKnown = true;
            _noGround = 0f;
            _holdPositionUntil = now + 2f;
            _holdTarget = at;
        }

        private void ReleaseGroundHold(string why, bool placed, Vector3 at = default(Vector3))
        {
            Game.Motion.AvatarHold h = _groundHold;
            _groundHold = null;
            if (h == null) return;
            string how;
            Vector3 spot = placed ? at : (h.Root != null ? h.Root.position : at);
            h.Release(spot, _groundHoldYaw, WowPlayer.XrPresent, out how);
            if (_groundHoldDrives) WowOrigin.AvatarDriven = false;
            _groundHoldDrives = false;
            if (why != null) WowLog.Info("Play: teleport hold ended - " + why + " (" + how + ").");
        }

        private void TeleportInWorld(WowPos p, float now, string why)
        {
            ReleaseGroundHold(null, false);   // lead fix 2026-10-04
            WowMount.LetGo("teleport (" + why + ")", false);   // iteration 7 (lead hook 5)
            WowVehicle.LetGo("teleport (" + why + ")", false);   // integrator (vehicle request): a seat lets go before the teleport
            int map = _current != null ? _current.MapId : 1;
            if (WowOrigin.Instance != null) WowOrigin.Instance.RecenterFor(p.X, p.Y, p.Z, why);
            Vector3 spot = WowSpace.ToUnity(map, p.X, p.Y, p.Z);
            float y;
            string how = FloorHeight(spot, out y);
            Vector3 at = new Vector3(spot.x, y + 0.1f, spot.z);
            float yaw = WowSpace.OrientationToYaw(p.O);
            string tp;
            bool moved = WowPlayer.Teleport(at, yaw, out tp);
            _arrival = at;
            _arrivalYaw = yaw;
            _teleportAt = now;
            _repeatSince = now;
            _teleportLanded = false;
            _teleportTries = 1;
            _lastSafe = at;
            _lastSafeKnown = true;
            _noGround = 0f;
            _lastFeetKnown = false;
            _speed = 0f;
            _holdPositionUntil = now + 2f;
            _holdTarget = at;
            WowLog.Info("Play: teleport to " + p + " (" + why + "): ground " + how + " -> " + at.ToString("F2") + " yaw " + yaw.ToString("0") + " - " + (moved ? tp : "FAILED: " + tp) + ".");
            // lead fix 2026-10-04: nothing under the target yet - no floor collider and either no terrain height or the area still streaming
            // (a city's WMO floors load after the terrain under them: placed on the terrain height, the player would stand under the city)
            if (!how.StartsWith("collider", StringComparison.Ordinal) && (how.StartsWith("the given z", StringComparison.Ordinal) || _map == null || !_map.IsReadyAround(spot, 10f)))
                StartGroundHold(p, map, yaw, now);
        }

        /// <summary>Snapshot of the running character into the roster and a commit to storage.</summary>
        private void SaveSession(string why, float now)
        {
            _saveSoon = false;
            _lastSaveAt = now;
            _nextSave = now + AutosaveSeconds;

            if (_session == null || _roster == null)
            {
                return;
            }

            CharacterRecord snap;

            try
            {
                snap = _session.Snapshot();
            }
            catch (Exception e)
            {
                WowLog.Error("Save: OfflineSession.Snapshot threw (" + why + "): " + e);
                return;
            }

            if (snap == null)
            {
                return;
            }

            snap.Name = _nick;
            _roster.Put(snap);
            _playRecord = snap;
            Commit(why + ": " + snap);
        }

        private void EndSession(string why)
        {
            if (_session == null)
            {
                return;
            }

            SaveSession(why, Time.realtimeSinceStartup);
            WowLog.Info("Play: session ended (" + why + ") after " + _session.Time.ToString("0") + " s, " + _events + " events.");
            _session = null;

            try
            {
                WowPlay.End();
            }
            catch (Exception e)
            {
                WowLog.Error("Play: WowPlay.End threw: " + e);
            }
        }

        // ------------------------------------------------------------------ world

        private void UpdateWorld(float now)
        {
            Vector3 feet;
            bool haveFeet = WowPlayer.TryFeet(out feet);

            if (haveFeet && !WowOrigin.Pending)   // a landed-but-uncommitted rebase teleport must not pull the streaming focus 1 km away
            {
                try
                {
                    // Integrator (Flights' request): while a flight carries the avatar, stream a few seconds ahead on the route.
                    _map.SetFocus(WowFlight.Flying ? WowFlight.StreamFocus : WowMount.OwnsAvatar ? WowMount.StreamFocus
                        : WowVehicle.OwnsAvatar ? WowVehicle.StreamFocus : feet);   // + iteration 7 (lead hook 4) + iteration 11 (pets H7a)
                }
                catch (Exception e)
                {
                    if (!_focusErrorLogged) { _focusErrorLogged = true; WowLog.Error("World: WowMapView.SetFocus: " + e); }
                }
            }

            WowLighting.Tick(Time.deltaTime);
            try { WowSkybox.Tick(_files); } catch (Exception e) { if (!_skyErrorLogged) { _skyErrorLogged = true; WowLog.Error("World: WowSkybox.Tick: " + e); } }   // lead 2026-10-10
            try { Ui.UiSharp.Tick(); } catch (Exception e) { if (!_sharpErrorLogged) { _sharpErrorLogged = true; WowLog.Error("World: UiSharp.Tick: " + e); } }   // lead 2026-10-10

            if (haveFeet)
            {
                // The teleport may land late (the client applies it asynchronously) - repeat it a few times, but only until
                // it has landed once (within 5 m): a player who then walks away must not be pulled back.
                if (!_teleportLanded && (feet - _arrival).magnitude < 5f)
                {
                    _teleportLanded = true;
                }

                if (!_teleportLanded && now - _repeatSince < 8f && _teleportTries < 4 && now - _teleportAt > 1.5f && (feet - _arrival).magnitude > 15f)
                {
                    string tp;
                    WowPlayer.Teleport(_arrival, _arrivalYaw, out tp);
                    _teleportAt = now;
                    _teleportTries++;
                    WowLog.Info("Arrive: the player is still at " + feet.ToString("F1") + " - teleport repeated (" + _teleportTries + ").");
                }
                // The client put the player back at the scene's spawn point (respawn height): the room is the right place.
                else if (now - _worldSince > 3f && (feet - WowSpace.LoginRoomOrigin).magnitude < 60f)
                {
                    ReturnToRoom("the player is back at the spawn point (client respawn)");
                    return;
                }
                else if (_groundHold != null)
                {
                    TickGroundHold(now);   // lead fix 2026-10-04: held at the teleport target until its ground has loaded
                }
                else if (!WowFlight.Flying && !WowMount.OwnsAvatar && !WowForcedMove.OwnsAvatar && !WowVehicle.OwnsAvatar)   // integrator (Flights' request) + iteration 7 (lead hook 4): a flight or a ride owns the avatar - no fall rescue mid-air; iteration 11 (motion hook 7, pets H7a): nor during a forced move / in a vehicle seat
                {
                    FallGuard(now, feet);
                }
            }

            PumpSession(now, feet, haveFeet);

            if (_phase != Phase.World || !haveFeet)
            {
                return;
            }

            if (now >= _nextLight)
            {
                _nextLight = now + 1f;
                FollowServerClock();   // lead 2026-11-03: online, the realm's time of day (WoW's clock)
                WowLighting.Track(_files, _current.MapId, feet, _timeOfDay);
            }

            UpdateArea(now, feet);
        }

        // The zone name for the info label: ADT chunk area id -> AreaTable, read on a pool thread every 2 s.
        private readonly WowAreaLookup _areaLookup = new WowAreaLookup();
        private volatile bool _areaBusy;
        private volatile string _areaName;
        private string _areaShown;
        private float _nextArea;

        private void UpdateArea(float now, Vector3 feet)
        {
            string name = _areaName;

            if (name != null && name != _areaShown)
            {
                _areaShown = name;
                _wrist.SetArea(name);
                WowLog.Info("World: zone '" + name + "'.");
            }

            if (now < _nextArea || _areaBusy)
            {
                return;
            }

            _nextArea = now + 2f;
            float x, y, z;
            WowSpace.ToWow(_current.MapId, feet, out x, out y, out z);
            int map = _current.MapId;
            WowFiles files = _files;
            _areaBusy = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int id = _areaLookup.AreaAt(files, map, x, y);
                    string n = WowAreaLookup.AreaName(files, id);
                    if (n != null) _areaName = n;
                }
                catch (Exception)
                {
                    // the label keeps its last zone
                }
                finally
                {
                    _areaBusy = false;
                }
            });
        }

        /// <summary>
        /// Puts a fallen player back: no collider within 400 m below the feet AND (more than 20 m under the terrain height,
        /// or 3 s without ground while below the last safe height). A player standing on a building floor under the
        /// terrain (Undercity) has a collider below and is left alone.
        /// </summary>
        private void FallGuard(float now, Vector3 feet)
        {
            if (now - _teleportAt < 2.5f || WowOrigin.Pending || WowOrigin.LastRebaseFrame == Time.frameCount)
            {
                return;
            }

            RaycastHit hit;
            bool below = Physics.Raycast(feet + Vector3.up, Vector3.down, out hit, 400f, GroundMask, QueryTriggerInteraction.Ignore);

            if (below)
            {
                if (feet.y - hit.point.y < 2.5f)
                {
                    _lastSafe = hit.point;
                    _lastSafeKnown = true;
                }

                _noGround = 0f;
                return;
            }

            _noGround += Time.deltaTime;
            float gy;
            bool known = TryGround(feet, out gy);
            bool fell = (known && feet.y < gy - 20f) || (_noGround > 3f && feet.y < (known ? gy : _lastSafe.y) + 1f);

            if (!fell)
            {
                return;
            }

            Vector3 target;
            string where;

            if (_lastSafeKnown && now - _lastRescueAt > 10f)
            {
                target = _lastSafe + Vector3.up * 0.3f;
                where = "the last ground stood on";
            }
            else if (TryGround(_arrival, out gy))
            {
                target = new Vector3(_arrival.x, Mathf.Max(gy, _arrival.y) + 0.5f, _arrival.z);
                where = "the arrival point's terrain height";
            }
            else
            {
                target = _arrival + Vector3.up * 0.5f;
                where = "the arrival point";
            }

            string tp;
            WowPlayer.Teleport(target, _arrivalYaw, out tp);
            _teleportAt = now;
            _lastRescueAt = now;
            _noGround = 0f;
            _fallRescues++;
            _lastFeetKnown = false;
            if (_fallRescues <= 20) WowLog.Warn("Fall guard: the player fell to " + feet.ToString("F1") + " (terrain " + (known ? gy.ToString("0.0") : "unknown") + ", no collider below) - put back on " + where + " " + target.ToString("F1") + " (" + _fallRescues + ").");
        }

        // ------------------------------------------------------------------ iteration 11 (w0game, wave 0: uicore H10, motion hook 11)

        /// <summary>True (iteration 10 behaviour): the back key (K) returns to the room while loading AND in the world. uicore sets it
        /// false once its key table binds K = Skills in the world (PLAN11 s2, D-K2); then K returns to the room only while loading
        /// (in the world: Escape > Exit Game or Settings > Return to the room).</summary>
        public static bool BackKeyInWorld = true;

        /// <summary>Game Menu > Exit Game (and any other UI): = ReturnToRoom on the running game; logged when there is none.</summary>
        public static void ReturnToRoomFromUi(string why)
        {
            WowGame g = Current;
            if (g == null) { WowLog.Warn("Room: '" + why + "' asked to return to the room - no game object."); return; }
            g.ReturnToRoom(why);
        }

        /// <summary>Game Menu > Logout: back to the character list. Online the character is logged out and the room's Play tab shows the
        /// realm's character list again; offline the offline characters - both are ReturnToRoom's end (EndOnline / AfterReturnOnline).</summary>
        public void RequestLogout()
        {
            ReturnToRoom("the Game Menu's Logout");
        }

        /// <summary>Motion's static switches of the online session (proposal-motion.md hook 11), next to OfflineSession.UnlockAllFlightPaths.</summary>
        private static void CopyMotionSettings()
        {
            SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession.TaxiMapAfterDiscovery = WowSettings.TaxiMapAfterDiscovery;
            SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession.FacingAssist = WowSettings.VrFacingAssist && WowPlayer.XrPresent;
        }

        /// <summary>Saves and ends the game (in play), unloads the map and puts the player back in the room.</summary>
        public void ReturnToRoom(string why)
        {
            WowSkybox.Drop();   // lead 2026-10-10
            ReleaseGroundHold("back to the room", false);   // lead fix 2026-10-04
            WowMount.LetGo("back to the room (" + why + ")", false);   // iteration 7 (lead hook 5)
            WowVehicle.LetGo("back to the room (" + why + ")", false);   // integrator (vehicle request)
            if (_phase != Phase.World && _phase != Phase.Loading)
            {
                return;
            }

            WowOrigin.Cancel("back to the room");
            WowLog.Info("Room: back to the room (" + why + ") from " + (_current != null ? _current.Label : "?") + (_mode == Mode.Play ? " (play)" : _mode == Mode.Online ? " (online)" : "") + ".");
            EndSession("back to the room");
            if (_mode == Mode.Online) EndOnline(why);   // iteration 6: views end, the character is logged out
            _playRecord = null;

            try { if (_map != null) _map.Unload(); }
            catch (Exception e) { WowLog.Error("WowMapView.Unload: " + e); }

            if (_lineupObject != null)
            {
                Destroy(_lineupObject.gameObject);
                _lineupObject = null;
            }

            _wrist.Hide();
            _loading.Hide();
            _room.SetShown(true);
            _room.SetPanelShown(true);
            _room.SetInteractive(true);
            _room.SetStatus(ReadyStatus(_current != null ? _current.Label : "-"), false);
            RefreshCharacters();
            if (_roster != null) _chars.ShowList();
            WowLighting.EnterRoom();
            _placed = false;
            _placeTries = 0f;
            _phase = Phase.Room;
            if (_mode == Mode.Online) AfterReturnOnline();
            WowLog.Info("Room: " + WowTextures.StatsLine());
        }

        /// <summary>The time of day chosen on the panel (hours).</summary>
        public float TimeOfDay { get { return _timeOfDay; } }

        /// <summary>The map view, for the line-up's and the views' ground heights (may be null).</summary>
        public WowMapView Map { get { return _map; } }

        /// <summary>True while a game runs (offline, or online since iteration 6; WowPlay.Session is set).</summary>
        public bool InPlay { get { return _session != null || _online != null; } }

        private void OnApplicationPause(bool paused)
        {
            if (!_started)
            {
                return;
            }

            if (paused && _session != null)
            {
                SaveSession("client paused", Time.realtimeSinceStartup);
            }

            if (paused)
            {
                SaveSettings("client paused");
            }
        }

        private void OnApplicationQuit()
        {
            if (!_started)
            {
                return;
            }

            if (_session != null)
            {
                SaveSession("client closing", Time.realtimeSinceStartup);
            }

            SaveSettings("client closing");
            ShutdownOnline("client closing");
        }

        private void OnDestroy()
        {
            // First and on its own: other players' hidden renderers, colliders and voices must be restored even if a later
            // step throws (idempotent: the driver's OnDestroy calls it too). Then the verdict is forgotten (statics survive).
            try
            {
                WowRemotes.Reset();
            }
            catch (Exception e)
            {
                WowLog.Error("Access: isolation shutdown failed: " + e);
            }

            WowAccess.Reset();
            _isolating = false;

            if (!_started)
            {
                return;   // a visitor, or left before the verdict: nothing else was started
            }

            // The client's sun / ambient / fog / reflection back (iteration 11.7: avatars are lit by the zone light).
            try { WowLighting.Restore(); }
            catch (Exception e) { WowLog.Error("Lighting: restoring the client's light threw: " + e); }

            WowSpace.Rebased -= OnOriginRebased;
            SaveSettings("leaving the world");
            CloseOldFiles("leaving the world");   // the install replaced in the room, if its delay had not run out

            if (_settingsSubscribed)
            {
                WowSettings.Changed -= OnSettingsChanged;
                _settingsSubscribed = false;
            }

            _settingsTab.Detach();

            try
            {
                EndSession("leaving the world");
            }
            catch (Exception e)
            {
                WowLog.Error("Play: ending the session on destroy threw: " + e);
            }

            try
            {
                ShutdownOnline("leaving the world");
            }
            catch (Exception e)
            {
                WowLog.Error("Online: closing on destroy threw: " + e);
            }

            if (WowPlay.Session != null)
            {
                WowPlay.End();
            }

            if (Current == this) Current = null;
        }
    }
}
