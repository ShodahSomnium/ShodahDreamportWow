using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// WowGame's online half (iteration 6, stage O1 - AzerothCore 3.3.5a). The Play tab's switch (WowSettings.OnlineMode)
    /// swaps the offline characters for WowOnlineScreen. WowLogin (NetCore) is polled every frame from Update:
    /// login -> realms (skipped when there is one) -> the server's characters (create through the faction/race/class screens +
    /// the name dialog, delete, enter) -> InWorld: the loading screen of SMSG_LOGIN_VERIFY_WORLD's map, WowSpace.SetOrigin +
    /// WowMapView.Load there (StartLoading, as offline), the avatar teleported onto the floor (Arrive), then
    /// new OnlineSession(files, login.World, login.Character, GroundZ) and WowPlay.Start - the offline Play flow with the server
    /// as the game. In the world PumpSession feeds the session as offline; a Teleport event (or a MapId change) onto another
    /// map puts the player in the room with the new map's loading screen and reloads the map there; a lost connection brings the
    /// player back to the room with the reason; going back to the room logs the character out (CMSG_LOGOUT_REQUEST, retried,
    /// the connection closed after 20 s of refusals). The account NAME is remembered only when ticked; the password is passed
    /// to WowLogin.Start and nowhere else, and no line here logs the account name or the password.
    /// </summary>
    public sealed partial class WowGame
    {
        private WowLogin _login;
        private OnlineSession _online;
        private OnlineSession _closing;          // the ended session whose handlers stay registered until the logout completes
        private WowOnlineScreen _onlineUi;
        private WowKeyboard _keyboard;
        private WowLoginState _loginSeen = WowLoginState.Idle;
        private int _msgSeen;
        private float _messageUntil;
        private string _statusShown;
        private List<CharInfo> _charsShown;
        private bool _charsLoadedShown;
        private float _logoutAskedAt = -1f;
        private int _logoutTries;
        private bool _onlineShown;
        private bool _onlineAttached;
        private bool _loginErrorLogged;
        private int _createRace;
        private WowClass _createClass;
        private int _farTeleports;
        private bool _enterAsked;               // Enter World pressed: the next InWorld is ours

        /// <summary>The running game (offline or online), or null.</summary>
        private IWowSession ActiveSession { get { return _session != null ? (IWowSession)_session : _online; } }

        /// <summary>True while an online game runs (WowPlay.Session is the OnlineSession).</summary>
        public bool InOnlinePlay { get { return _online != null; } }

        // ------------------------------------------------------------------ setup

        /// <summary>StartDreamport: the keyboard and the online screen (drawn once the files are read).</summary>
        private void InitOnline()
        {
            _keyboard = WowKeyboard.Create(_room.transform);
            _onlineUi = new WowOnlineScreen();
            _onlineUi.LoginPressed += OnOnlineLogin;
            _onlineUi.AutoPressed += OnAutoPlay;          // stage A1: "Play online as <nick>" (Front/WowGameAutoAccount.cs)
            _onlineUi.ManualChosen += OnManualChosen;
            _settingsTab.AutoKey = AutoKeyState;
            _settingsTab.ForgetKeyConfirmed += ForgetAutoKey;
            _onlineUi.CancelPressed += () => { DisposeLogin("cancelled by the player"); ShowIdle("Cancelled.", false); SetRoomStatus("Cancelled.", false); };
            _onlineUi.LogoutPressed += () => { DisposeLogin("logged out by the player"); ShowIdle("Logged out.", false); SetRoomStatus("Logged out.", false); };
            _onlineUi.RealmPicked += OnRealmPicked;
            _onlineUi.EnterPressed += OnOnlineEnter;
            _onlineUi.DeleteConfirmed += OnOnlineDelete;
            _onlineUi.NewPressed += OnOnlineNew;
            _onlineUi.NameCreatePressed += OnOnlineNameCreate;
            _onlineUi.NameCancelled += () => WowLog.Info("Online: naming cancelled (back on the class screen).");
            _chars.OnlineBack += () => { if (WowSettings.OnlineMode) { _onlineUi.SetVisible(true); ShowOnlineView(); } };
        }

        /// <summary>FinishBoot: the Play tab's Offline / Online switch and the online screen's data.</summary>
        private void AttachOnline()
        {
            _onlineUi.SetData(_classes, _races, _files);
            if (!WowRoom.ShowOldTabs && !WowSettings.OnlineMode) { WowSettings.OnlineMode = true; WowLog.Info("Room: offline mode is hidden (2026-11-03) - online."); }
            // 2026-11-03 (user: "make this screen the default screen"): the menu always opens on "Log In with Somnium Account"; "Log in with
            // different details" lasts until the room is left (a saved choice from an earlier visit is not reused)
            if (WowSettings.OnlineManual) { WowSettings.OnlineManual = false; WowLog.Info("Room: the menu opens on Log In with Somnium Account (the default screen)."); }
            _onlineUi.Attach(_room, _keyboard);
            _room.BuildModeSwitch(WowSettings.OnlineMode, SetOnlineMode);
            _onlineAttached = true;
            _manualShown = WowSettings.OnlineManual;
            _hostShown = WowSettings.ServerHost ?? "";
            RefreshAutoView();
            ApplyOnlineMode("boot");
        }

        /// <summary>The room's switch.</summary>
        private void SetOnlineMode(bool on)
        {
            if (_phase != Phase.Room)
            {
                return;
            }

            if (WowSettings.OnlineMode != on)
            {
                WowSettings.OnlineMode = on;
                WowLog.Info("Room: Play tab " + (on ? "online" : "offline") + ".");
                if (!on && _login != null) DisposeLogin("switched to offline");
                WowSettings.NotifyChanged();   // saves; OnSettingsChanged -> ApplyOnlineMode
            }

            ApplyOnlineMode("switch");
        }

        /// <summary>Shows the Play tab for the current mode (also after the saved settings loaded).</summary>
        private void ApplyOnlineMode(string why)
        {
            if (!_onlineAttached || _room == null)
            {
                return;
            }

            if (!WowRoom.ShowOldTabs) WowSettings.OnlineMode = true;   // 2026-11-03: offline mode hidden (a saved "offline" is ignored)
            bool on = WowSettings.OnlineMode;
            bool changed = on != _onlineShown;
            _onlineShown = on;
            _room.SetOnline(on);
            _onlineUi.ReloadRemembered();

            if (!changed && why != "boot")
            {
                return;
            }

            _chars.SetOnline(on);

            if (on)
            {
                _onlineUi.SetVisible(true);
                ShowOnlineView();
            }
            else
            {
                if (_keyboard != null) _keyboard.Close();
                _onlineUi.SetVisible(false);
                RefreshCharacters();
                if (_roster != null) _chars.ShowList();
                if (_phase == Phase.Room && _files != null) SetRoomStatus(ReadyStatus(null), false);
            }

            WowLog.Info("Room: Play tab shows the " + (on ? "online " + (WowSettings.OnlineManual ? "login form" : "\"Play online\" (automatic account)") + " (server " + (string.IsNullOrEmpty(WowSettings.ServerHost) ? "not set" : WowSettings.ServerHost) + ")" : "offline characters") + " (" + why + ").");
        }

        /// <summary>OnSettingsChanged: the saved settings may switch the tab's mode, the server or the manual / automatic choice.</summary>
        private void OnlineSettingsChanged()
        {
            if (_onlineAttached && _phase == Phase.Room && WowSettings.OnlineMode != _onlineShown)
            {
                ApplyOnlineMode("settings");
            }

            AutoSettingsChanged();
        }

        /// <summary>The online screen's view for WowLogin's state.</summary>
        private void ShowOnlineView()
        {
            if (_onlineUi == null || !WowSettings.OnlineMode)
            {
                return;
            }

            WowLogin l = _login;

            if (_accountSvc != null)
            {
                _onlineUi.ShowBusy(AutoBusyText(), false);   // stage A1: the account service is asked (no Cancel: see WowGameAutoAccount)
                return;
            }

            if (l == null || l.State == WowLoginState.Idle)
            {
                if (WowSettings.OnlineManual) _onlineUi.ShowLogin(string.IsNullOrEmpty(WowSettings.ServerHost) ? "Enter your server's address, your account and your password." : "", false);
                else _onlineUi.ShowAuto(_autoNote, _autoNoteError);
                return;
            }

            switch (l.State)
            {
                case WowLoginState.Failed:
                    if (WowSettings.OnlineManual) _onlineUi.ShowLogin(l.FailReason, true);
                    else _onlineUi.ShowAuto(l.FailReason, true);
                    break;
                case WowLoginState.Realms:
                    _onlineUi.ShowRealms(l.Realms);
                    break;
                case WowLoginState.Characters:
                    _charsShown = l.Characters;
                    _charsLoadedShown = l.CharactersLoaded;
                    _onlineUi.ShowCharacters(l.Realm != null ? l.Realm.Name : "", l.Characters, l.CharactersLoaded);
                    break;
                case WowLoginState.InWorld:
                    _onlineUi.ShowBusy(l.LoggingOut || _logoutAskedAt >= 0f ? "Logging out... (" + (l.Status ?? "") + ")" : l.Status, true);
                    break;
                default:
                    _onlineUi.ShowBusy(l.Status, true);
                    break;
            }
        }

        private void SetRoomStatus(string text, bool error)
        {
            _statusShown = text;
            if (_room != null) _room.SetStatus(text, error);
        }

        // ------------------------------------------------------------------ login

        private void OnOnlineLogin(string host, int port, string account, string password)
        {
            if (_phase != Phase.Room || _files == null)
            {
                return;
            }

            if (_accountSvc != null)
            {
                return;   // stage A1: the account service is being asked
            }

            string h = WowSettings.CleanText(host);

            if (h != WowSettings.ServerHost || port != WowSettings.ServerPort)
            {
                WowSettings.ServerHost = h;
                WowSettings.ServerPort = port;
                WowSettings.NotifyChanged();   // remembered (the settings save)
            }

            StartOnlineLogin(h, port, account, password, false);
        }

        /// <summary>The login with a typed account (manual form) or the automatic account's stored / new key (stage A1).</summary>
        /// <summary>Lead decision D13 (PLAN7 s7.4): this login uses an automatic account the account service reported as GM -> the Mounts
        /// window's "Testing (GM)" row (in-world GM commands instead of a terminal). False for manual logins.</summary>
        public static bool AutoAccountIsGm { get; private set; }

        private void StartOnlineLogin(string h, int port, string account, string password, bool auto)
        {
            AutoAccountIsGm = auto && AutoGmMarked(h, account);
            if (_login != null) DisposeLogin("a new login");
            _autoLogin = auto;
            _autoAccepted = false;
            _login = new WowLogin();
            _login.Locale = string.IsNullOrEmpty(_locale) ? "enUS" : _locale;
            _loginSeen = WowLoginState.Idle;
            _msgSeen = 0;
            _messageUntil = 0f;
            _loginErrorLogged = false;
            _charsShown = null;
            _logoutAskedAt = -1f;
            WowLog.Info("Online: logging in to " + h + ":" + port + " (locale " + _login.Locale + "; " + (auto ? "automatic account " + WowSaves.Mask(account) + ", its key is not logged" : "the account name and password are not logged") + ").");
            _login.Start(h, port, account, password);
            password = null;
            _loginSeen = _login.State;
            ShowOnlineView();
            SetRoomStatus(_login.State == WowLoginState.Failed ? _login.FailReason : _login.Status, _login.State == WowLoginState.Failed);
        }

        private void OnRealmPicked(int index)
        {
            if (_login == null || _login.State != WowLoginState.Realms) return;
            RealmInfo r = _login.Realms != null && index >= 0 && index < _login.Realms.Count ? _login.Realms[index] : null;
            WowLog.Info("Online: realm " + (r != null ? r.ToString() : index.ToString()) + " chosen.");
            _login.PickRealm(index);
            ShowOnlineView();
        }

        private void OnOnlineEnter(CharInfo c)
        {
            if (_phase != Phase.Room || _login == null || _login.State != WowLoginState.Characters || c == null) return;
            WowLog.Info("Online: Enter World as " + c + ".");
            _enterAsked = true;
            _login.EnterWorld(c.Guid);
            ShowOnlineView();
        }

        private void OnOnlineDelete(CharInfo c)
        {
            if (_phase != Phase.Room || _login == null || _login.State != WowLoginState.Characters || c == null) return;
            WowLog.Info("Online: deleting " + c + ".");
            _login.DeleteCharacter(c.Guid);
        }

        private void OnOnlineNew()
        {
            if (_phase != Phase.Room || _login == null || _login.State != WowLoginState.Characters) return;
            _onlineUi.SetVisible(false);
            _chars.ShowCreate();
        }

        /// <summary>The class screen's Create in online mode: the name step.</summary>
        private void OnlineCreate(WowClass cls, int race)
        {
            if (_login == null || _login.State != WowLoginState.Characters)
            {
                SetRoomStatus("Not connected to a realm - log in again.", true);
                _chars.ShowList();
                _onlineUi.SetVisible(true);
                ShowOnlineView();
                return;
            }

            _createRace = race;
            _createClass = cls;
            string name = WowOnlineNames.CleanName(_nick);
            string how = "from the Somnium name";

            if (name.Length == 0)
            {
                name = WowOnlineNames.CleanName(WowSaves.FallbackName);
                how = "the Somnium name has fewer than 2 letters A-Z - fallback";
            }

            WowRaceEntry re = WowRaceInfo.Find(_races, race);
            WowClassEntry ce = WowClassInfo.Find(_classes, cls);
            string what = (re != null ? re.Name : "race " + race) + " " + (ce != null ? ce.Name : cls.ToString());
            WowLog.Info("Online: naming a " + what + " - suggested '" + name + "' (" + how + ").");
            _onlineUi.ShowNameDialog(name, WowSettings.VoiceGender, what);
        }

        private void OnOnlineNameCreate(string name, int gender)
        {
            if (_login == null || _login.State != WowLoginState.Characters)
            {
                _onlineUi.SetNameError("Not connected to a realm any more - cancel and log in again.");
                return;
            }

            WowAppearance a;

            try
            {
                a = WowOnlineNames.DefaultAppearance(_files, _createRace, gender);
            }
            catch (Exception e)
            {
                a = new WowAppearance { Report = "appearance failed (" + e.GetType().Name + ") - all 0" };
            }

            WowLog.Info("Online: creating '" + name + "' race " + _createRace + " class " + (int)_createClass + " gender " + gender + " - " + a.Report + ".");
            _login.CreateCharacter(new CharCreateRequest
            {
                Name = name,
                Race = _createRace,
                Class = (int)_createClass,
                Gender = gender,
                Skin = a.Skin,
                Face = a.Face,
                HairStyle = a.HairStyle,
                HairColor = a.HairColor,
                FacialHair = a.FacialHair,
                OutfitId = 0,
            });
        }

        /// <summary>Every frame (all phases): WowLogin.Poll and what its state changes mean for the room and the world.</summary>
        private void PollLogin(float now)
        {
            WowLogin l = _login;

            try
            {
                l.Poll();
            }
            catch (Exception e)
            {
                if (!_loginErrorLogged) { _loginErrorLogged = true; WowLog.Error("Online: WowLogin.Poll threw: " + e); }
            }

            if (_login != l)
            {
                return;
            }

            WowLoginState st = l.State;

            if (st != _loginSeen)
            {
                WowLoginState prev = _loginSeen;
                _loginSeen = st;
                OnLoginState(prev, st, now);
                if (_login != l) return;
            }

            if (l.MessageCounter != _msgSeen)
            {
                _msgSeen = l.MessageCounter;
                OnLoginMessage(l.Message, now);
            }

            if (st == WowLoginState.Characters && _phase == Phase.Room && (l.Characters != _charsShown || l.CharactersLoaded != _charsLoadedShown) && _onlineUi.Visible)
            {
                ShowOnlineView();
            }

            // Back in the room with the character still in the world: the server refused the logout (combat...) - ask again,
            // and after 4 tries (20 s) close the connection.
            if (st == WowLoginState.InWorld && _online == null && _phase == Phase.Room && _logoutAskedAt >= 0f && !l.LoggingOut && now - _logoutAskedAt > 5f)
            {
                if (_logoutTries < 4)
                {
                    _logoutTries++;
                    _logoutAskedAt = now;
                    WowLog.Info("Online: the server did not log out yet - asking again (" + _logoutTries + ").");
                    l.Logout();
                }
                else
                {
                    WowLog.Warn("Online: the server refused to log the character out 4 times - connection closed.");
                    DisposeLogin("logout refused");
                    ShowIdle("The server would not log the character out, so the connection was closed.", true);
                    SetRoomStatus("The server would not log the character out, so the connection was closed.", true);
                    return;
                }
            }

            if (_phase == Phase.Room && WowSettings.OnlineMode)
            {
                string s = st == WowLoginState.Failed ? l.FailReason : l.Status;

                if (s != null && s != _statusShown && now >= _messageUntil)
                {
                    SetRoomStatus(s, st == WowLoginState.Failed);
                }

                if (_onlineUi.Current == WowOnlineScreen.View.Busy && (st == WowLoginState.Connecting || st == WowLoginState.Authenticating || st == WowLoginState.EnteringWorld || st == WowLoginState.InWorld))
                {
                    ShowOnlineView();   // live text (queue position, logout countdown)
                }
            }
        }

        private void OnLoginState(WowLoginState prev, WowLoginState st, float now)
        {
            WowLogin l = _login;

            switch (st)
            {
                case WowLoginState.Realms:
                case WowLoginState.Characters:
                    if (_autoLogin)
                    {
                        AutoLoginAccepted();   // stage A1 (the automatic name is not "remembered": it comes from the Somnium ID)
                    }
                    else if (!string.IsNullOrEmpty(l.Account) && WowSettings.RememberAccount && l.Account != WowSettings.OnlineAccount)
                    {
                        WowSettings.OnlineAccount = WowSettings.CleanText(l.Account);
                        WowSettings.NotifyChanged();
                        WowLog.Info("Online: account name remembered (ticked).");
                    }

                    if (st == WowLoginState.Characters && prev == WowLoginState.InWorld)
                    {
                        if (_closing != null && l.World != null) l.World.OffAll(_closing);
                        _closing = null;
                        _logoutAskedAt = -1f;
                        WowLog.Info("Online: logged out - back at the character list.");
                    }

                    if (st == WowLoginState.Characters && _enterAsked && _phase == Phase.Room)
                    {
                        _enterAsked = false;
                        WowLog.Warn("Online: entering the world failed - back at the character list.");
                    }

                    if (_phase == Phase.Room && WowSettings.OnlineMode && _onlineUi.Visible) ShowOnlineView();
                    break;

                case WowLoginState.InWorld:
                    if (_phase == Phase.Room && _enterAsked)
                    {
                        BeginOnlineWorld();
                    }
                    else if (_phase == Phase.Room && _online == null && _logoutAskedAt < 0f)
                    {
                        // In the world without a request of ours (should not happen): log out at once.
                        WowLog.Warn("Online: the realm put a character in the world unasked - logging out.");
                        if (l.World != null && l.World.Held) l.World.Release();
                        _logoutAskedAt = now;
                        _logoutTries = 1;
                        l.Logout();
                    }
                    break;

                case WowLoginState.Failed:
                    _closing = null;
                    _enterAsked = false;
                    _logoutAskedAt = -1f;

                    if ((_phase == Phase.World || _phase == Phase.Loading) && _mode == Mode.Online)
                    {
                        string reason = l.FailReason ?? "the connection was lost";
                        WowLog.Warn("Online: " + reason + " - back to the room.");
                        ReturnToRoom("disconnected: " + reason);   // EndOnline: views end; the connection is gone, nothing to log out
                        ShowIdle(reason, true);
                        SetRoomStatus(reason, true);
                    }
                    else if (_phase == Phase.Room && AutoKeyRefused(l))
                    {
                        // Stage A1: the server refused the stored key - the account service is asked once (WowGameAutoAccount).
                    }
                    else if (_phase == Phase.Room)
                    {
                        _onlineUi.CloseNameDialog();

                        if (WowSettings.OnlineMode)
                        {
                            _chars.ShowList();   // off the creation screens
                            _onlineUi.SetVisible(true);
                            ShowOnlineView();
                        }

                        SetRoomStatus(l.FailReason, true);
                    }
                    break;

                default:
                    if (_phase == Phase.Room && WowSettings.OnlineMode && _onlineUi.Visible) ShowOnlineView();
                    break;
            }
        }

        private void OnLoginMessage(string m, float now)
        {
            if (string.IsNullOrEmpty(m))
            {
                return;
            }

            bool ok = m == NetCodes.CharCreate(NetCodes.CHAR_CREATE_SUCCESS) || m == NetCodes.CharDelete(NetCodes.CHAR_DELETE_SUCCESS);

            if (_onlineUi.NameDialogOpen)
            {
                if (m == NetCodes.CharCreate(NetCodes.CHAR_CREATE_SUCCESS))
                {
                    WowLog.Info("Online: character created - back at the list.");
                    _onlineUi.CloseNameDialog();
                    _chars.ShowList();
                    _onlineUi.SetVisible(true);
                    ShowOnlineView();
                }
                else if (m != NetCodes.CharCreate(0x2E))   // CHAR_CREATE_IN_PROGRESS is not an answer
                {
                    _onlineUi.SetNameError(m);
                }
            }

            if (_phase == Phase.Room)
            {
                SetRoomStatus(m, !ok);
                _messageUntil = now + 6f;
            }
        }

        /// <summary>Closes every connection (auth + world); the online screen goes back to the login view.</summary>
        private void DisposeLogin(string why)
        {
            if (_login == null)
            {
                return;
            }

            WowLog.Info("Online: connection closed (" + why + ").");

            try
            {
                if (_closing != null && _login.World != null) _login.World.OffAll(_closing);
                _login.Dispose();
            }
            catch (Exception e)
            {
                WowLog.Error("Online: closing the connection threw: " + e);
            }

            _login = null;
            _closing = null;
            _logoutAskedAt = -1f;
            _loginSeen = WowLoginState.Idle;
            _enterAsked = false;
            if (_onlineUi != null) _onlineUi.CloseNameDialog();
        }

        // ------------------------------------------------------------------ into the world

        /// <summary>InWorld: the loading screen for SMSG_LOGIN_VERIFY_WORLD's map and position (the offline Play flow's StartLoading).</summary>
        private void BeginOnlineWorld()
        {
            _enterAsked = false;
            LoginVerifyWorld v = _login.WorldPos;
            CharInfo c = _login.Character;
            WowLocation loc = OnlineLocation(v.Map, v.X, v.Y, v.Z, c != null ? c.Zone : 0, c != null ? "online " + c.Name : "online");
            _playYaw = WowSpace.OrientationToYaw(v.O);
            _mode = Mode.Online;
            _farTeleports = 0;
            _logoutAskedAt = -1f;
            if (_keyboard != null) _keyboard.Close();
            _onlineUi.CloseDialog();
            _onlineUi.CloseNameDialog();
            _onlineUi.ShowBusy("Entering the world" + (c != null ? " as " + c.Name : "") + "...", false);
            WowClassEntry ce = c != null ? WowClassInfo.Find(_classes, (WowClass)c.Class) : null;
            WowRaceEntry re = c != null ? WowRaceInfo.Find(_races, c.Race) : null;
            WowLog.Info("Online: " + (c != null ? c.Name + " level " + c.Level + " " + (re != null ? re.Name : "race " + c.Race) + " " + (ce != null ? ce.Name : "class " + c.Class) : "?")
                + " enters map " + v.Map + " (" + loc.MapName + ", " + loc.Label + ") at " + v.X.ToString("0.0") + " " + v.Y.ToString("0.0") + " " + v.Z.ToString("0.0") + " o " + v.O.ToString("0.00") + " - loading.");
            StartPreload();
            StartLoading(loc, loc.Label + " - " + loc.MapName);
        }

        private WowLocation OnlineLocation(int map, float x, float y, float z, int zone, string source)
        {
            string mapName = null, area = null;
            try { mapName = WowLocations.MapName(_files, map); } catch (Exception) { mapName = null; }
            try { if (zone > 0) area = WowAreaLookup.AreaName(_files, zone); } catch (Exception) { area = null; }
            if (string.IsNullOrEmpty(mapName)) mapName = "map " + map;

            return new WowLocation
            {
                Label = string.IsNullOrEmpty(area) ? mapName : area,
                Source = source,
                DbcName = string.IsNullOrEmpty(area) ? mapName : area,
                MapId = map,
                MapName = mapName,
                X = x,
                Y = y,
                Z = z,
            };
        }

        /// <summary>Arrive in online mode: the first arrival starts the session; a far teleport's arrival resets the hold.</summary>
        private void ArriveOnline(float now, Vector3 at)
        {
            if (_online == null)
            {
                StartOnlineSession(now, at);
                return;
            }

            _holdPositionUntil = now + 2f;
            _holdTarget = at;
            _lastFeetKnown = false;
            _speed = 0f;
            WowLog.Info("Online: arrived on map " + _current.MapId + " (" + _current.MapName + ") after far teleport " + _farTeleports + "; " + _online.Entities.Count + " entities known.");
        }

        private void StartOnlineSession(float now, Vector3 at)
        {
            if (_login == null || _login.State != WowLoginState.InWorld || _login.World == null)
            {
                ReturnToRoom("the realm connection is gone");
                return;
            }

            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                WarmMountData(_files, "online session");   // iteration 7 (lead hook 7): no-op when the worker already has it
                _online = new OnlineSession(_files, _login.World, _login.Character, GroundZ);
                _events = 0;
                _speed = 0f;
                _lastFeetKnown = false;
                _holdPositionUntil = now + 2f;
                _holdTarget = at;
                _facingSource = null;
                WowPlay.Start(_online, _files);
                PlayerState ps = _online.Player;
                WowLog.Info("Online: session started in " + watch.ElapsedMilliseconds + " ms - " + (ps != null ? ps.Name + ", level " + ps.Level + " " + ps.Class + ", at " + ps.Pos : "?")
                    + " on map " + _online.MapId + ", " + _online.Entities.Count + " entities, latency " + _login.World.LatencyMs + " ms.");
            }
            catch (Exception e)
            {
                WowLog.Error("Online: starting the online session failed: " + e);
                _online = null;
                ReturnToRoom("the online game could not start (" + e.GetType().Name + ": " + e.Message + ")");
                SetRoomStatus("The online game could not start: " + e.GetType().Name + ": " + e.Message, true);
            }
        }

        /// <summary>
        /// A Teleport event online: onto another map -> far teleport; during a far teleport's loading on the same map -> the
        /// arrival spot moves. Returns false for a plain same-map teleport (TeleportInWorld does it).
        /// </summary>
        private bool OnlineTeleport(WowPos p, float now)
        {
            if (_online == null || _current == null)
            {
                return false;
            }

            int map = _online.MapId;

            if (_phase == Phase.Loading)
            {
                if (map == _current.MapId)
                {
                    _current.X = p.X;
                    _current.Y = p.Y;
                    _current.Z = p.Z;
                    _spot = WowSpace.ToUnity(map, p.X, p.Y, p.Z);
                    _playYaw = WowSpace.OrientationToYaw(p.O);
                    WowLog.Info("Online: the arrival point moved to " + p + " while loading.");
                }
                else
                {
                    StartFarTeleport(map, p, now, "another map while loading");
                }

                return true;
            }

            if (map != _current.MapId)
            {
                StartFarTeleport(map, p, now, "the server moved the player to another map");
                return true;
            }

            return false;
        }

        /// <summary>After each online Tick: a map change without a Teleport event is a far teleport too.</summary>
        private void CheckOnlineMap(float now)
        {
            if (_online != null && _phase == Phase.World && _current != null && _online.MapId != _current.MapId)
            {
                PlayerState ps = _online.Player;
                StartFarTeleport(_online.MapId, ps != null ? ps.Pos : new WowPos(_current.X, _current.Y, _current.Z, 0f), now, "the session's map changed");
            }
        }

        /// <summary>Another map: the player waits in the room with its loading screen while the map streams (the session keeps ticking).</summary>
        private void StartFarTeleport(int map, WowPos p, float now, string why)
        {
            WowMount.LetGo("far teleport (" + why + ")", false);   // iteration 7 (lead hook 5)
            WowVehicle.LetGo("far teleport (" + why + ")", false);   // integrator (vehicle request)
            _farTeleports++;
            WowLocation loc = OnlineLocation(map, p.X, p.Y, p.Z, 0, "online far teleport");
            WowLog.Info("Online: far teleport " + _farTeleports + " to map " + map + " (" + loc.MapName + ") at " + p + " (" + why + ") - loading screen, map reload.");
            WowOrigin.Cancel("far teleport");

            try { if (_map != null) _map.Unload(); }
            catch (Exception e) { WowLog.Error("WowMapView.Unload: " + e); }

            _wrist.Hide();
            _room.SetShown(true);
            WowLighting.EnterRoom();
            _placed = false;
            _placeTries = 0f;
            _playYaw = WowSpace.OrientationToYaw(p.O);
            StartLoading(loc, loc.Label + " - " + loc.MapName);
        }

        // ------------------------------------------------------------------ out of the world

        /// <summary>Ends the online game (views first) and asks the server to log the character out; the room shows "Logging out".</summary>
        private void EndOnline(string why)
        {
            OnlineSession s = _online;
            _online = null;

            if (s != null)
            {
                WowLog.Info("Online: session ended (" + why + ") after " + _events + " events" + (_login != null && _login.World != null ? ", " + _login.World.PacketsIn + " packets in / " + _login.World.PacketsOut + " out" : "") + ".");

                try
                {
                    if (WowPlay.Session == s) WowPlay.End();
                }
                catch (Exception e)
                {
                    WowLog.Error("Play: WowPlay.End threw: " + e);
                }
            }

            if (_login == null)
            {
                return;
            }

            if (_login.State == WowLoginState.InWorld && _login.World != null && !_login.World.IsClosed)
            {
                if (_login.World.Held) _login.World.Release();   // left during the first loading: nobody reads the queued packets
                if (s != null) _closing = s;
                _logoutAskedAt = Time.realtimeSinceStartup;
                _logoutTries = 1;
                WowLog.Info("Online: logging the character out (" + why + ").");
                _login.Logout();
            }
            else if (s != null && _login.World != null)
            {
                _login.World.OffAll(s);
            }
        }

        /// <summary>ReturnToRoom's end in online mode: the Play tab shows the login state again.</summary>
        private void AfterReturnOnline()
        {
            if (!WowSettings.OnlineMode || _onlineUi == null)
            {
                return;
            }

            _chars.ShowList();
            _onlineUi.SetVisible(true);
            ShowOnlineView();

            if (_login != null)
            {
                SetRoomStatus(_login.State == WowLoginState.Failed ? _login.FailReason : _login.Status, _login.State == WowLoginState.Failed);
            }
        }

        /// <summary>Leaving the Dreamport: close the connections (the server logs the character out when the socket closes).</summary>
        private void ShutdownOnline(string why)
        {
            ShutdownAutoAccount(why);   // stage A1


            if (_online != null)
            {
                try { if (WowPlay.Session == _online) WowPlay.End(); }
                catch (Exception e) { WowLog.Error("Play: WowPlay.End threw: " + e); }
                _online = null;
            }

            DisposeLogin(why);
        }
    }
}
