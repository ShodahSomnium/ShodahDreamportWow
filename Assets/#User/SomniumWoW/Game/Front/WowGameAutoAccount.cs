using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// Stage A1 (PLAN8 2.6): the automatic WoW account bound to the Somnium account. The Online tab's "Play online as
    /// &lt;nickname&gt;" button: the account name comes from the Somnium ID (WowAutoAccount.NameFor: "S" + 16 hex digits),
    /// the password is a random key made on this computer (WowAutoAccount.NewSecret) and kept ONLY in the player's own world
    /// storage (WowAutoKeys: "wow_autoaccount_&lt;host&gt;_&lt;NAME&gt;", two copies).
    ///   key stored for (server, name)   -&gt; WowLogin.Start(server, auth port, name, key) - the usual login flow from there.
    ///   no key                          -&gt; a new key -&gt; AccountServiceClient.Register(server, 8086, id, nick, key) (only the
    ///                                      SRP6 salt + verifier leave the PC) -&gt; Created / Rekeyed: the key is stored FIRST
    ///                                      (in every phase, so an answer that arrives late is never lost), then the login.
    ///   the server refuses a stored key -&gt; once per press the account service is asked with a new key: Created (the server
    ///                                      was reset) or Rekeyed (the owner unlocked it) store it and log in; Exists = the
    ///                                      key no longer matches (plain message).
    ///   Exists / NotAllowed / SlowDown / BadRequest / ServerError / Unreachable / WrongAnswer -&gt; a plain message.
    /// No Cancel while the service is asked: cancelling after the server made the account would lose its key (the client
    /// gives up by itself after 8 s connect + 10 s answer). The key is never logged or shown: log lines name the account by
    /// its first four characters ("Online: automatic account SCF5... created (GM)").
    /// </summary>
    public sealed partial class WowGame
    {
        private WowAutoKeys _autoKeys;
        private string _autoKeysStorage = "";
        private AccountServiceClient _accountSvc;
        private string _autoSecret;             // a NEW key between Register and its answer (never logged); null otherwise
        private string _autoHost, _autoName, _autoId, _autoNick;
        private int _autoAuthPort;
        private bool _autoLogin;                // the running WowLogin is the automatic account's
        private bool _autoAccepted;             // ... and the server accepted its key
        private bool _autoRetried;              // this press: the stored key was refused and the account service was asked
        private bool _autoRepair;               // a stored copy was missing or different: write the key again once it worked
        private string _autoNote = "";
        private bool _autoNoteError;
        private float _autoAskedAt;
        private bool _autoPollErrorLogged;
        private bool _manualShown;
        private string _hostShown;

        /// <summary>Longest wait for the account service before giving up (the client's own timeouts are 8 s + 10 s).</summary>
        private const float AutoAnswerTimeout = 30f;

        // ------------------------------------------------------------------ identity / storage

        /// <summary>
        /// The Somnium ID (WowAccess.UserId, set by the whitelist gate; else the id the roster opened with - never the editor's
        /// stand-in), the nickname for the button and the automatic account name. False with the reason in plain words.
        /// </summary>
        private bool AutoIdentity(out string id, out string nick, out string name, out string why)
        {
            id = WowAccess.UserId;

            if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(_userId) && _userId != WowSaves.EditorUserId)
            {
                id = _userId;
            }

            nick = !string.IsNullOrEmpty(WowAccess.Nick) ? WowAccess.Nick.Trim() : _nick;
            name = null;
            why = null;

            if (string.IsNullOrEmpty(id))
            {
                why = Application.isEditor
                    ? "Only in the Somnium client: your Somnium account makes the WoW account."
                    : "Waiting for your Somnium account...";
                return false;
            }

            try
            {
                name = WowAutoAccount.NameFor(id);
            }
            catch (Exception)
            {
                name = null;
            }

            if (name == null)
            {
                why = "Your Somnium ID can't be used for an automatic WoW account - use a different account.";
                return false;
            }

            return true;
        }

        // Lead decision D13 (PLAN7 s7.4): which automatic accounts the account service created as GM ("host|NAME;..." in
        // WowSettings.AutoAccountGm, data only - never a key). Read at each login (WowGameOnline.AutoAccountIsGm).
        private static string AutoGmEntry(string host, string name)
        {
            return (host ?? "").Trim().ToLowerInvariant() + "|" + (name ?? "").Trim().ToUpperInvariant();
        }

        private static bool AutoGmMarked(string host, string name)
        {
            string e = AutoGmEntry(host, name);
            foreach (string part in (WowSettings.AutoAccountGm ?? "").Split(';'))
            {
                if (part == e) return true;
            }

            return false;
        }

        private void MarkAutoGm(string host, string name, bool gm)
        {
            string e = AutoGmEntry(host, name);
            var keep = new System.Collections.Generic.List<string>();
            foreach (string part in (WowSettings.AutoAccountGm ?? "").Split(';'))
            {
                if (part.Length > 0 && part != e) keep.Add(part);
            }

            if (gm) keep.Add(e);
            string next = string.Join(";", keep);

            if (next == (WowSettings.AutoAccountGm ?? ""))
            {
                return;
            }

            WowSettings.AutoAccountGm = next;
            _settingsDirty = true;
            SaveSettings("automatic account GM flag");
        }

        private WowAutoKeys AutoKeys()
        {
            if (_autoKeys == null)
            {
                try
                {
                    _autoKeys = WowSaves.OpenAutoKeys(out _autoKeysStorage);
                }
                catch (Exception e)
                {
                    WowLog.Error("Online: opening the automatic account keys' storage failed: " + e);
                    _autoKeys = new WowAutoKeys(new IWowSaveSlot[] { new WowMemorySlot() });
                    _autoKeysStorage = "memory (this visit only)";
                }
            }

            return _autoKeys;
        }

        private static string Masked(string name)
        {
            return WowSaves.Mask(name);
        }

        /// <summary>The Auto view's button, line and server (after the identity, the server or the stored key changed).</summary>
        private void RefreshAutoView()
        {
            if (_onlineUi == null)
            {
                return;
            }

            string id, nick, name, why;
            bool ok = AutoIdentity(out id, out nick, out name, out why);
            string host = WowSettings.ServerHost ?? "";
            string info;

            if (!ok)
            {
                info = why;
            }
            else if (host.Length == 0)
            {
                info = "No server is set - choose \"Use a different account\" and enter one.";
            }
            else if (AutoKeys().Has(host, name))
            {
                info = "Your WoW account " + name + " is ready on this computer.";
            }
            else
            {
                info = "First time: your WoW account " + name + " is made for you on this server.";
            }

            // No Somnium account yet (editor, or before the identity arrives): no nickname on the button ("with your Somnium account").
            _onlineUi.SetAuto(ok ? nick : null, ok && host.Length > 0, info, host.Length > 0 ? WowOnlineNames.FormatServer(host, WowSettings.ServerPort) : "");
            _settingsTab.RefreshOnline();
        }

        /// <summary>The login view the player chose (Auto, or the manual form) with a note; the Auto note is kept for later redraws.</summary>
        private void ShowIdle(string note, bool error)
        {
            if (WowSettings.OnlineManual)
            {
                _onlineUi.ShowLogin(note, error);
                return;
            }

            _autoNote = note ?? "";
            _autoNoteError = error;
            _onlineUi.ShowAuto(_autoNote, _autoNoteError);
        }

        /// <summary>A result of the automatic account on the Auto view and the status line.</summary>
        private void ShowAutoNote(string text, bool error)
        {
            _autoNote = text ?? "";
            _autoNoteError = error;

            if (_phase == Phase.Room && WowSettings.OnlineMode && _onlineUi != null && _onlineUi.Visible)
            {
                ShowOnlineView();
            }

            if (_phase == Phase.Room)
            {
                SetRoomStatus(_autoNote, error);
                _messageUntil = Time.realtimeSinceStartup + 6f;
            }
        }

        // ------------------------------------------------------------------ the button

        /// <summary>"Play online as &lt;nick&gt;".</summary>
        private void OnAutoPlay()
        {
            if (_phase != Phase.Room || _files == null || _accountSvc != null)
            {
                return;
            }

            string id, nick, name, why;

            if (!AutoIdentity(out id, out nick, out name, out why))
            {
                WowLog.Info("Online: Play online pressed, but there is no automatic account (" + why + ").");
                ShowAutoNote(why, true);
                return;
            }

            string host = WowSettings.ServerHost ?? "";
            int port = WowSettings.ServerPort;

            if (host.Length == 0)
            {
                ShowAutoNote("No server is set - choose \"Use a different account\" and enter one.", true);
                return;
            }

            if (_login != null)
            {
                DisposeLogin("Play online pressed");
            }

            _autoHost = host;
            _autoAuthPort = port;
            _autoName = name;
            _autoId = id;
            _autoNick = nick;
            _autoRetried = false;
            _autoRepair = false;
            _autoNote = "";
            _autoNoteError = false;

            bool consistent;
            string report;
            string key = AutoKeys().Find(host, name, out consistent, out report);

            if (key != null)
            {
                _autoRepair = !consistent;
                WowLog.Info("Online: automatic account " + Masked(name) + " key found (" + report + ") - logging in to " + host + ":" + port + ".");
                StartOnlineLogin(host, port, name, key, true);
                return;
            }

            WowLog.Info("Online: automatic account " + Masked(name) + " - no key on this computer (" + report + "): asking the account service at "
                + host + ":" + AccountServiceClient.DefaultPort + " to make it.");
            StartAutoRegister();
        }

        /// <summary>A new key, and the account service asked to make (or re-key) the account with it.</summary>
        private void StartAutoRegister()
        {
            string secret;

            try
            {
                secret = WowAutoAccount.NewSecret();
            }
            catch (Exception e)
            {
                WowLog.Error("Online: making a key failed: " + e.GetType().Name + ": " + e.Message);
                ShowAutoNote("This computer could not make a key for your WoW account (" + e.GetType().Name + ").", true);
                return;
            }

            _autoSecret = secret;
            _accountSvc = new AccountServiceClient();
            _autoAskedAt = Time.realtimeSinceStartup;
            _autoPollErrorLogged = false;

            try
            {
                _accountSvc.Register(_autoHost, AccountServiceClient.DefaultPort, _autoId, _autoNick, secret);
            }
            catch (Exception e)
            {
                WowLog.Error("Online: asking the account service threw " + e.GetType().Name + ": " + e.Message);
                _accountSvc = null;
                _autoSecret = null;
                ShowAutoNote("The account service could not be asked (" + e.GetType().Name + ").", true);
                return;
            }

            if (_phase == Phase.Room && WowSettings.OnlineMode && _onlineUi.Visible)
            {
                ShowOnlineView();
            }

            SetRoomStatus(AutoBusyText(), false);
        }

        private string AutoBusyText()
        {
            return (_autoRetried ? "The server did not accept this computer's key - " : "") + "Making your WoW account on " + _autoHost + "... (this takes a few seconds)";
        }

        /// <summary>Every frame while the account service is asked (all phases: a late answer still stores its key).</summary>
        private void PollAutoAccount(float now)
        {
            AccountServiceClient a = _accountSvc;

            try
            {
                a.Poll();
            }
            catch (Exception e)
            {
                if (!_autoPollErrorLogged) { _autoPollErrorLogged = true; WowLog.Error("Online: AccountServiceClient.Poll threw: " + e); }
            }

            AccountServiceClient.Status st = a.State;

            if (st == AccountServiceClient.Status.Working || st == AccountServiceClient.Status.Idle)
            {
                if (now - _autoAskedAt <= AutoAnswerTimeout)
                {
                    return;
                }

                WowLog.Warn("Online: the account service gave no answer in " + AutoAnswerTimeout.ToString("0") + " s - given up.");
                try { a.Cancel(); } catch (Exception) { }
                st = AccountServiceClient.Status.Unreachable;
            }

            _accountSvc = null;
            string secret = _autoSecret;
            _autoSecret = null;
            OnAutoAnswer(a, st, secret);
        }

        private void OnAutoAnswer(AccountServiceClient a, AccountServiceClient.Status st, string secret)
        {
            string name = _autoName;
            string given = a.AccountName;

            if (!string.IsNullOrEmpty(given) && WowAutoKeys.IsAccountName(given) && given.Trim().ToUpperInvariant() != name)
            {
                name = given.Trim().ToUpperInvariant();
                WowLog.Warn("Online: the account service named the automatic account " + Masked(name) + ", not " + Masked(_autoName) + " - using its name.");
                _autoName = name;
            }

            string port = AccountServiceClient.DefaultPort.ToString();
            string lead = _autoRetried ? "The server did not accept this computer's key. " : "";
            string msg = CleanServiceText(a.Message);

            switch (st)
            {
                case AccountServiceClient.Status.Created:
                case AccountServiceClient.Status.Rekeyed:
                    {
                        string report;
                        int copies = AutoKeys().Store(_autoHost, name, secret, out report);
                        bool created = st == AccountServiceClient.Status.Created;
                        if (created) MarkAutoGm(_autoHost, name, a.IsGm);   // lead (D13): remembered for the Mounts window's GM testing row
                        WowLog.Info("Online: automatic account " + Masked(name) + (created ? " created" + (a.IsGm ? " (GM)" : "") : " re-keyed (unlocked by the server owner)")
                            + " - key " + (copies > 0 ? "saved on this computer in " + _autoKeysStorage + " (" + report + ")" : "NOT saved (" + report + ")") + ".");
                        RefreshAutoView();

                        string warn = copies > 0 ? null
                            : "Your WoW account is ready, but this computer could not save its key. You can play now; next time the server owner has to unlock it.";

                        if (_phase == Phase.Room && WowSettings.OnlineMode && _login == null)
                        {
                            StartOnlineLogin(_autoHost, _autoAuthPort, name, secret, true);

                            if (warn != null)
                            {
                                SetRoomStatus(warn, true);
                                _messageUntil = Time.realtimeSinceStartup + 8f;
                            }
                        }
                        else
                        {
                            ShowAutoNote(warn ?? ("Your WoW account " + name + " is ready - press Play online."), warn != null);
                        }

                        break;
                    }

                case AccountServiceClient.Status.Exists:
                    WowLog.Info("Online: automatic account " + Masked(name) + (_autoRetried ? " exists, and this computer's key no longer matches it" : " exists without key on this computer")
                        + " - the server owner can unlock it.");
                    ShowAutoNote(_autoRetried
                        ? "This computer's key for your WoW account no longer matches the server. Ask the server owner to unlock it (account " + name + "), then press Play online again."
                        : "Your Somnium account already has a WoW account, but this computer doesn't have its key. Ask the server owner to unlock it (account " + name + "), then press Play online again.", true);
                    break;

                case AccountServiceClient.Status.NotAllowed:
                    WowLog.Info("Online: automatic account " + Masked(name) + " - the account service does not allow this Somnium account (allow list).");
                    ShowAutoNote(lead + "This server doesn't know your Somnium account yet - ask the server owner to add you.", true);
                    break;

                case AccountServiceClient.Status.SlowDown:
                    WowLog.Info("Online: automatic account " + Masked(name) + " - the account service says slow down (too many tries).");
                    ShowAutoNote(lead + "Too many tries in a short time. Wait ten minutes, then press Play online again.", true);
                    break;

                case AccountServiceClient.Status.BadRequest:
                    {
                        // The client's Message is the service's own "message" (or its generic sentence, which is not repeated).
                        bool own = msg.Length > 0 && !msg.StartsWith("The account service did not accept", StringComparison.Ordinal);
                        WowLog.Warn("Online: automatic account " + Masked(name) + " - the account service did not accept the request" + (own ? " (" + msg + ")" : "") + ".");
                        ShowAutoNote(lead + "The account service did not accept the request" + (own ? ": " + msg : "."), true);
                        break;
                    }

                case AccountServiceClient.Status.ServerError:
                    WowLog.Warn("Online: automatic account " + Masked(name) + " - the account service reported a server problem.");
                    ShowAutoNote(lead + "The account service has a problem on the server. Try again later, or tell the server owner.", true);
                    break;

                case AccountServiceClient.Status.WrongAnswer:
                    WowLog.Warn("Online: automatic account " + Masked(name) + " - the account service's answer was not understood.");
                    ShowAutoNote(lead + "The account service on " + _autoHost + " (port " + port + ") gave an answer this world doesn't understand - it may be a different version.", true);
                    break;

                default:   // Unreachable (and a stuck request)
                    WowLog.Warn("Online: automatic account " + Masked(name) + " - the account service at " + _autoHost + ":" + port + " is not reachable.");
                    ShowAutoNote(lead + "Can't reach the account service on " + _autoHost + " (port " + port + ").", true);
                    break;
            }
        }

        /// <summary>The service's own message for the panel: one line, no control characters, at most 160 characters.</summary>
        private static string CleanServiceText(string m)
        {
            if (string.IsNullOrEmpty(m)) return "";
            var sb = new System.Text.StringBuilder(Math.Min(m.Length, 160));

            foreach (char c in m)
            {
                if (c < ' ' || c == (char)127) continue;
                sb.Append(c);
                if (sb.Length >= 160) break;
            }

            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------ the login's answers

        /// <summary>OnLoginState Realms / Characters for the automatic account: accepted once; a copy that differed is written again.</summary>
        private void AutoLoginAccepted()
        {
            if (!_autoLogin || _autoAccepted || _login == null)
            {
                return;
            }

            _autoAccepted = true;
            _autoRetried = false;
            WowLog.Info("Online: automatic account " + Masked(_autoName) + " logged in.");

            if (_autoRepair)
            {
                _autoRepair = false;
                bool consistent;
                string report;
                string key = AutoKeys().Find(_autoHost, _autoName, out consistent, out report);

                if (key != null)
                {
                    int n = AutoKeys().Store(_autoHost, _autoName, key, out report);
                    WowLog.Info("Online: automatic account " + Masked(_autoName) + " - a stored copy of the key was missing or different; written again (" + n + " copies: " + report + ").");
                }
            }
        }

        /// <summary>
        /// OnLoginState Failed in the room: the server refused the automatic account's stored key (unknown account, or wrong
        /// password) -&gt; once per press the account service is asked with a new key. True when that started.
        /// </summary>
        private bool AutoKeyRefused(WowLogin l)
        {
            if (!_autoLogin || _autoAccepted || _autoRetried || _accountSvc != null || l == null)
            {
                return false;
            }

            string r = l.FailReason ?? "";
            bool refused = r == NetCodes.AuthResult(NetCodes.WOW_FAIL_UNKNOWN_ACCOUNT, false)
                || r == NetCodes.AuthResult(NetCodes.WOW_FAIL_UNKNOWN_ACCOUNT, true)
                || r == NetCodes.AuthResult(NetCodes.WOW_FAIL_INCORRECT_PASSWORD, true)
                || r == NetCodes.AuthResult(NetCodes.WOW_FAIL_INCORRECT_PASSWORD, false);

            if (!refused)
            {
                return false;
            }

            _autoRetried = true;
            WowLog.Info("Online: the server did not accept this computer's key for automatic account " + Masked(_autoName) + " (" + r
                + ") - asking the account service (the account may be new on this server, or unlocked by its owner).");
            DisposeLogin("the automatic account's key was refused");
            StartAutoRegister();
            return true;
        }

        // ------------------------------------------------------------------ the views' switch and the Settings page

        /// <summary>"Use a different account" (true) / "Use my Somnium account" (false).</summary>
        private void OnManualChosen(bool manual)
        {
            if (WowSettings.OnlineManual != manual)
            {
                WowSettings.OnlineManual = manual;
                WowSettings.NotifyChanged();   // remembered (the settings save)
            }

            WowLog.Info("Online: " + (manual ? "the login form for a different account" : "Play online with the automatic account") + " chosen.");

            if (_login != null && _login.State == WowLoginState.Failed)
            {
                DisposeLogin("the login view was switched");
            }

            if (_phase == Phase.Room && _login == null && _accountSvc == null && _files != null)
            {
                SetRoomStatus(ReadyStatus(null), false);   // the other view's last message does not belong to this one
                _messageUntil = 0f;
            }

            RefreshAutoView();
            ShowOnlineView();
        }

        /// <summary>The Settings tab's Online page.</summary>
        private WowSettingsTab.AutoKeyState AutoKeyState()
        {
            var s = new WowSettingsTab.AutoKeyState { Server = WowSettings.ServerHost ?? "" };
            string id, nick, name, why;

            if (AutoIdentity(out id, out nick, out name, out why))
            {
                s.Name = name;
                s.HasKey = s.Server.Length > 0 && AutoKeys().Has(s.Server, name);
            }
            else
            {
                s.Why = why;
            }

            return s;
        }

        /// <summary>The Settings tab's "Forget this computer's key", confirmed.</summary>
        private void ForgetAutoKey()
        {
            string id, nick, name, why;

            if (!AutoIdentity(out id, out nick, out name, out why))
            {
                return;
            }

            string host = WowSettings.ServerHost ?? "";
            string report;
            int n = AutoKeys().Forget(host, name, out report);
            WowLog.Info("Online: automatic account " + Masked(name) + " - this computer's key for " + host + " forgotten in Settings (" + report + ").");
            RefreshAutoView();

            if (_phase == Phase.Room)
            {
                SetRoomStatus(n > 0 ? "This computer's key for your WoW account was forgotten." : "No key for your WoW account was stored on this computer.", false);
                _messageUntil = Time.realtimeSinceStartup + 6f;
            }
        }

        /// <summary>OnlineSettingsChanged: the saved settings may bring another server or the manual choice.</summary>
        private void AutoSettingsChanged()
        {
            if (!_onlineAttached || _onlineUi == null)
            {
                return;
            }

            bool manual = WowSettings.OnlineManual;
            string host = WowSettings.ServerHost ?? "";

            if (manual == _manualShown && host == _hostShown)
            {
                return;
            }

            _manualShown = manual;
            _hostShown = host;
            RefreshAutoView();

            if (_phase == Phase.Room && WowSettings.OnlineMode && _onlineUi.Visible && _accountSvc == null && (_login == null || _login.State == WowLoginState.Idle || _login.State == WowLoginState.Failed))
            {
                ShowOnlineView();
            }
        }

        /// <summary>Leaving: a request still running is dropped (its key with it) and the new key forgotten from memory.</summary>
        private void ShutdownAutoAccount(string why)
        {
            if (_accountSvc != null)
            {
                WowLog.Warn("Online: the account service was still being asked (" + why + ") - the request is dropped.");
                try { _accountSvc.Cancel(); } catch (Exception) { }
                _accountSvc = null;
            }

            _autoSecret = null;
        }
    }
}
