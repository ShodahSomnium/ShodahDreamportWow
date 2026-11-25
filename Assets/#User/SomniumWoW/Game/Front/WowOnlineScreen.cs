using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The Play tab when the room's switch says Online (iteration 6, stage O1). Views, drawn into WowRoom.PlayRoot:
    /// LOGIN (server host[:port], account, masked password, "Remember account name", Login - desktop types into TMP fields,
    /// a headset gets WowKeyboard when a field is selected), BUSY (what WowLogin is doing + Cancel), REALMS (only when the
    /// server lists more than one), CHARACTERS (the server's characters in the offline list's look: race icon by the
    /// character's gender, class icon, name in the class colour, "Level N Race Class", the zone from AreaTable; Enter World,
    /// Delete with a confirmation, New character, Log out) and the NAME dialog that ends character creation (the cleaned
    /// Somnium nickname, male/female, the server's error text, editable). UI only: WowGame owns WowLogin and reacts to the
    /// events. The password is handed to LoginPressed and the field is emptied at once; nothing here logs it.
    /// Stage A1: AUTO (the default login view) - the big "Play online as &lt;Somnium nickname&gt;" button (the automatic WoW
    /// account bound to the Somnium account: WowGame makes / finds it), a line saying what will happen, the result or error
    /// in plain words, the server, and "Use a different account", which opens the LOGIN form above (it has "Use my Somnium
    /// account" to come back; the choice is remembered in WowSettings.OnlineManual by WowGame).
    /// </summary>
    public sealed class WowOnlineScreen
    {
        public enum View { Hidden, Login, Busy, Realms, Characters, Auto }

        /// <summary>Login pressed: host, port, account, password (use it at once, keep nothing).</summary>
        public event Action<string, int, string, string> LoginPressed;
        /// <summary>Stage A1: "Play online as &lt;nick&gt;" pressed.</summary>
        public event Action AutoPressed;
        /// <summary>Stage A1: true = "Use a different account" (the manual form), false = "Use my Somnium account" (back to Auto).</summary>
        public event Action<bool> ManualChosen;
        public event Action CancelPressed;
        public event Action<int> RealmPicked;
        public event Action<CharInfo> EnterPressed;
        public event Action<CharInfo> DeleteConfirmed;
        public event Action NewPressed;
        public event Action LogoutPressed;
        /// <summary>The name dialog's Create: name, gender (0 male, 1 female).</summary>
        public event Action<string, int> NameCreatePressed;
        public event Action NameCancelled;

        public const int MaxCharacters = 10;   // AzerothCore CharactersPerRealm default

        private WowRoom _room;
        private WowKeyboard _keyboard;
        private WowFiles _files;
        private List<WowClassEntry> _classes = new List<WowClassEntry>();
        private List<WowRaceEntry> _races = new List<WowRaceEntry>();
        private GameObject _content, _dialog, _nameDialog;
        private View _view = View.Hidden;
        private bool _visible;

        // Login view state (kept across rebuilds; never the password).
        private TMP_InputField _server, _account, _password;
        private string _serverText, _accountText;
        private string _serverFilled, _accountFilled;   // what ReloadRemembered last put in (a field the player has not edited follows the saved value)

        // Auto view (stage A1).
        private string _autoNick = "";   // empty = no Somnium account known yet (the button then says "with your Somnium account")
        private bool _autoAvailable;
        private string _autoInfo = "";
        private string _autoServer = "";
        private string _autoNote = "";
        private bool _autoNoteError;
        private WowGlueButton _autoButton;
        private TextMeshProUGUI _autoInfoLabel, _autoNoteLabel, _autoServerLabel;

        // Busy / realms / characters view data.
        private string _busyText = "";
        private bool _busyCancel = true;
        private List<RealmInfo> _realms = new List<RealmInfo>();
        private List<CharInfo> _chars = new List<CharInfo>();
        private bool _charsLoaded;
        private string _realmName = "";
        private string _loginNote = "";
        private bool _loginNoteError;

        // Name dialog.
        private TMP_InputField _nameField;
        private TextMeshProUGUI _nameError;
        private WowGlueButton _male, _female, _createButton;
        private int _gender;
        private readonly Dictionary<ulong, string> _zoneNames = new Dictionary<ulong, string>();

        public View Current { get { return _view; } }
        public bool NameDialogOpen { get { return _nameDialog != null; } }

        public void SetData(List<WowClassEntry> classes, List<WowRaceEntry> races, WowFiles files)
        {
            _classes = classes ?? new List<WowClassEntry>();
            _races = races ?? new List<WowRaceEntry>();
            _files = files;
        }

        /// <summary>Hooks into the room's Play tab (after every WowRoom.BuildPanel).</summary>
        public void Attach(WowRoom room, WowKeyboard keyboard)
        {
            _room = room;
            _keyboard = keyboard;
            _content = null;
            _dialog = null;
            _nameDialog = null;
            Rebuild();
        }

        /// <summary>Shown (online mode on the Play tab, not creating a character) or not.</summary>
        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (!visible) { CloseDialog(); CloseNameDialog(); CloseKeyboard(); }
            Rebuild();
        }

        public bool Visible { get { return _visible; } }

        /// <summary>After the saved settings loaded: empty server / account fields take the remembered values.</summary>
        public void ReloadRemembered()
        {
            SaveLoginTexts();
            string server = WowOnlineNames.FormatServer(WowSettings.ServerHost, WowSettings.ServerPort);
            string account = WowSettings.RememberAccount ? WowSettings.OnlineAccount : "";

            // Empty, or still what was filled in before (e.g. the default server before the saved settings loaded): take the saved value.
            if (string.IsNullOrEmpty(_serverText) || _serverText == _serverFilled)
            {
                _serverText = server;
                _serverFilled = server;
                if (_server != null) _server.text = server;
            }

            if (string.IsNullOrEmpty(_accountText) || _accountText == _accountFilled)
            {
                _accountText = account;
                _accountFilled = account;
                if (_account != null) _account.text = account;
            }
        }

        /// <summary>
        /// Stage A1: what the Auto view shows - the nickname on the button (null / empty while no Somnium account is known:
        /// "Play online with your Somnium account"), whether the button works (a Somnium ID is known), the line under it (what
        /// will happen, or why it is not available) and the server. Refreshes the view when shown.
        /// </summary>
        public void SetAuto(string nick, bool available, string info, string server)
        {
            string n = string.IsNullOrEmpty(nick) ? "" : nick.Trim();
            bool resize = n != _autoNick;
            _autoNick = n;
            _autoAvailable = available;
            _autoInfo = info ?? "";
            _autoServer = server ?? "";
            if (resize && _view == View.Auto && _content != null) Rebuild();   // the button's text size follows the label's length
            else RefreshAuto();
        }

        /// <summary>Stage A1: the Auto view with a note under the button (a result or an error in plain words).</summary>
        public void ShowAuto(string note, bool error)
        {
            _autoNote = note ?? "";
            _autoNoteError = error;
            if (_view == View.Auto && _content != null) { RefreshAuto(); return; }
            _view = View.Auto;
            Rebuild();
        }

        /// <summary>The note currently shown under the Auto button (for the lab / tests).</summary>
        public string AutoNote { get { return _autoNote; } }

        public void ShowLogin(string note, bool error)
        {
            _loginNote = note ?? "";
            _loginNoteError = error;
            if (_view == View.Login && _content != null) { RefreshLoginNote(); return; }
            _view = View.Login;
            Rebuild();
        }

        public void ShowBusy(string text, bool cancel)
        {
            text = text ?? "";
            if (_view == View.Busy && _busyText == text && _busyCancel == cancel && _content != null) return;
            bool same = _view == View.Busy && _busyCancel == cancel && _content != null;
            _busyText = text;
            _busyCancel = cancel;
            if (same && _busyLabel != null) { _busyLabel.text = text; return; }
            _view = View.Busy;
            Rebuild();
        }

        private TextMeshProUGUI _busyLabel, _loginNoteLabel;

        public void ShowRealms(List<RealmInfo> realms)
        {
            _realms = realms != null ? new List<RealmInfo>(realms) : new List<RealmInfo>();
            _view = View.Realms;
            Rebuild();
        }

        public void ShowCharacters(string realm, List<CharInfo> chars, bool loaded)
        {
            _realmName = realm ?? "";
            _chars = chars != null ? new List<CharInfo>(chars) : new List<CharInfo>();
            _charsLoaded = loaded;
            _view = View.Characters;
            Rebuild();
        }

        // ------------------------------------------------------------------ drawing

        private void Rebuild()
        {
            CloseDialog();
            CloseKeyboard();
            if (_view == View.Login) SaveLoginTexts();
            _server = _account = _password = null;
            _busyLabel = _loginNoteLabel = null;
            _autoButton = null;
            _autoInfoLabel = _autoNoteLabel = _autoServerLabel = null;

            if (_content != null)
            {
                UnityEngine.Object.Destroy(_content);
                _content = null;
            }

            if (_room == null || _room.PlayRoot == null || !_visible || _view == View.Hidden)
            {
                return;
            }

            _room.SetPlayTall(false);
            RectTransform root = WowUi.Place(_room.PlayRoot, "Online " + _view, 0f, 0f, WowRoom.PanelWidth, WowRoom.ContentHeight);
            _content = root.gameObject;

            switch (_view)
            {
                case View.Login: BuildLogin(root); break;
                case View.Auto: BuildAuto(root); break;
                case View.Busy: BuildBusy(root); break;
                case View.Realms: BuildRealms(root); break;
                case View.Characters: BuildCharacters(root); break;
            }
        }

        private void SaveLoginTexts()
        {
            if (_server != null) _serverText = _server.text;
            if (_account != null) _accountText = _account.text;
        }

        private void BuildLogin(RectTransform root)
        {
            WowUi.Text(root, "Title", "Log in to your WoW server", 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 600f, 36f);

            if (_serverText == null) _serverText = _serverFilled = WowOnlineNames.FormatServer(WowSettings.ServerHost, WowSettings.ServerPort);
            if (_accountText == null) _accountText = _accountFilled = WowSettings.RememberAccount ? WowSettings.OnlineAccount : "";

            WowUi.Text(root, "Server Label", "Server", 22f, WowUi.GlueGold, TextAlignmentOptions.Left, 60f, 56f, 180f, 34f);
            _server = WowTextField.Create(root, "Server", 250f, 50f, 520f, 46f, 22f, "host name or IP (:port)", false, 128);
            _server.text = _serverText ?? "";
            WowUi.Text(root, "Account Label", "Account", 22f, WowUi.GlueGold, TextAlignmentOptions.Left, 60f, 110f, 180f, 34f);
            _account = WowTextField.Create(root, "Account", 250f, 104f, 520f, 46f, 22f, "account name", false, 32);
            _account.text = _accountText ?? "";
            WowUi.Text(root, "Password Label", "Password", 22f, WowUi.GlueGold, TextAlignmentOptions.Left, 60f, 164f, 180f, 34f);
            _password = WowTextField.Create(root, "Password", 250f, 158f, 520f, 46f, 22f, "password", true, 32);

            Hook(_server, "Server", false, () => Focus(_account));
            Hook(_account, "Account", false, () => Focus(_password));
            Hook(_password, "Password", true, PressLogin);

            WowCheckBox.Create(root, "Remember account name", 250f, 214f, 420f, 34f, WowSettings.RememberAccount, on =>
            {
                WowSettings.RememberAccount = on;
                if (!on) WowSettings.OnlineAccount = "";
                WowLog.Info("Online: remember account name " + (on ? "on" : "off") + ".");
                WowSettings.NotifyChanged();
            });

            WowUi.Button(root, "Login", 800f, 102f, 250f, 52f, 24f, PressLogin);
            TextMeshProUGUI hint = WowUi.Text(root, "Hint", "Your own AzerothCore 3.3.5a server (see ServerKit).", 15f, WowUi.Grey, TextAlignmentOptions.Center, 790f, 160f, 270f, 44f);
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 11f;
            hint.fontSizeMax = 15f;
            BottomAccountButton(root, "Log In with Somnium Account", false);

            _loginNoteLabel = WowUi.Text(root, "Note", "", 17f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 256f, WowRoom.PanelWidth - 100f, 34f);
            _loginNoteLabel.enableAutoSizing = true;
            _loginNoteLabel.fontSizeMin = 12f;
            _loginNoteLabel.fontSizeMax = 17f;
            WowUi.Text(root, "Privacy", "The password goes only to the login server and is never saved; only the account name is remembered (when ticked).", 14f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 294f, WowRoom.PanelWidth - 100f, 36f);
            RefreshLoginNote();
        }

        /// <summary>Stage A1: the automatic account's view (see the class summary).</summary>
        private void BuildAuto(RectTransform root)
        {
            WowUi.Text(root, "Title", "Play online", 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 480f, 36f);
            _autoServerLabel = WowUi.Text(root, "Server", "", 17f, WowUi.Grey, TextAlignmentOptions.Right, 540f, 10f, 510f, 26f);
            _autoServerLabel.enableAutoSizing = true;
            _autoServerLabel.fontSizeMin = 12f;
            _autoServerLabel.fontSizeMax = 17f;

            // 2026-11-03 (user: "a big button in the middle 'Log In with Somnium Account'")
            _autoButton = WowUi.Button(root, AutoLabel(), 175f, 58f, 750f, 112f, AutoTextSize(), () =>
            {
                var h = AutoPressed;
                if (h != null) h();
            });

            _autoInfoLabel = WowUi.Text(root, "Info", "", 17f, WowUi.Grey, TextAlignmentOptions.Center, 60f, 178f, WowRoom.PanelWidth - 120f, 30f);
            _autoInfoLabel.enableAutoSizing = true;
            _autoInfoLabel.fontSizeMin = 12f;
            _autoInfoLabel.fontSizeMax = 17f;
            _autoNoteLabel = WowUi.Text(root, "Note", "", 19f, Color.white, TextAlignmentOptions.Center, 50f, 210f, WowRoom.PanelWidth - 100f, 64f);
            _autoNoteLabel.enableAutoSizing = true;
            _autoNoteLabel.fontSizeMin = 12f;
            _autoNoteLabel.fontSizeMax = 19f;

            BottomAccountButton(root, "Log in with different details", true);

            TextMeshProUGUI privacy = WowUi.Text(root, "Privacy", "Nothing to type: the account name comes from your Somnium ID, and its password is random and kept only in this world's storage on this computer.", 14f, WowUi.Grey, TextAlignmentOptions.Center, 50f, 290f, WowRoom.PanelWidth - 100f, 36f);
            privacy.enableAutoSizing = true;
            privacy.fontSizeMin = 11f;
            privacy.fontSizeMax = 14f;
            RefreshAuto();
        }

        private string AutoLabel()
        {
            return "Log In with Somnium Account";   // 2026-11-03: the user's wording; the nickname is on the line under it
        }

        private float AutoTextSize()
        {
            return 38f;
        }

        /// <summary>2026-11-03: the bottom-left button of the panel (WowRoom.BottomY), drawn with this view (so it hides with the Play
        /// content, e.g. while Settings are open): the Auto view offers the manual form, the form offers the Somnium login back.</summary>
        private void BottomAccountButton(RectTransform root, string label, bool manual)
        {
            WowUi.Button(root, label, 50f, WowRoom.BottomY - WowRoom.ContentTop, 450f, WowRoom.BottomH, 22f, () =>
            {
                if (!manual) SaveLoginTexts();
                var h = ManualChosen;
                if (h != null) h(manual);
            });
        }

        private void RefreshAuto()
        {
            if (_view != View.Auto || _content == null) return;

            if (_autoButton != null)
            {
                _autoButton.SetLabel(AutoLabel());
                _autoButton.Interactable = _autoAvailable;
            }

            if (_autoServerLabel != null) _autoServerLabel.text = _autoServer.Length > 0 ? "Server " + _autoServer : "";
            if (_autoInfoLabel != null) _autoInfoLabel.text = (_autoNick.Length > 0 ? "Signs in as " + _autoNick + (_autoInfo.Length > 0 ? "  -  " : "") : "") + _autoInfo;

            if (_autoNoteLabel != null)
            {
                _autoNoteLabel.text = _autoNote;
                _autoNoteLabel.color = _autoNoteError ? new Color(1f, 0.3f, 0.25f) : Color.white;
            }
        }

        private void RefreshLoginNote()
        {
            if (_loginNoteLabel == null) return;
            _loginNoteLabel.text = _loginNote;
            _loginNoteLabel.color = _loginNoteError ? new Color(1f, 0.3f, 0.25f) : Color.white;
        }

        private void Hook(TMP_InputField f, string title, bool masked, Action onEnter)
        {
            if (f == null) return;
            f.onSelect.AddListener(_ =>
            {
                if (WowPlayer.XrPresent && _keyboard != null) _keyboard.Open(f, title, masked, onEnter);
            });
            f.onSubmit.AddListener(_ =>
            {
                if (WowPlayer.XrPresent) return;   // the headset keyboard's Enter does it
                try { if (onEnter != null) onEnter(); }
                catch (Exception e) { WowLog.Error("Online: Enter in " + title + " failed: " + e); }
            });
        }

        private void Focus(TMP_InputField f)
        {
            if (f == null) return;
            if (WowPlayer.XrPresent)
            {
                if (_keyboard != null)
                {
                    bool masked = f == _password;
                    string title = f == _server ? "Server" : f == _account ? "Account" : "Password";
                    Action enter = f == _server ? (Action)(() => Focus(_account)) : f == _account ? (Action)(() => Focus(_password)) : PressLogin;
                    _keyboard.Open(f, title, masked, enter);
                }
                return;
            }
            f.Select();
            f.ActivateInputField();
        }

        private void PressLogin()
        {
            if (_server == null || _account == null || _password == null) return;
            string host;
            int port;

            if (!WowOnlineNames.ParseServer(_server.text, 3724, out host, out port))
            {
                ShowLogin(string.IsNullOrEmpty(_server.text) ? "Enter the server address (host name or IP, optionally :port)." : "The server address is not valid (host name or IP, optionally :port).", true);
                return;
            }

            string account = (_account.text ?? "").Trim();
            string password = _password.text ?? "";
            _password.text = "";   // the field keeps nothing
            SaveLoginTexts();
            CloseKeyboard();
            var h = LoginPressed;
            if (h != null) h(host, port, account, password);
        }

        private void BuildBusy(RectTransform root)
        {
            _busyLabel = WowUi.Text(root, "Busy", _busyText, 26f, WowUi.GlueGold, TextAlignmentOptions.Center, 60f, 80f, WowRoom.PanelWidth - 120f, 110f);
            _busyLabel.enableAutoSizing = true;
            _busyLabel.fontSizeMin = 14f;
            _busyLabel.fontSizeMax = 26f;
            if (_busyCancel) WowUi.Button(root, "Cancel", 425f, 220f, 250f, 48f, 22f, () => { var h = CancelPressed; if (h != null) h(); });
        }

        private void BuildRealms(RectTransform root)
        {
            WowUi.Text(root, "Title", "Choose a realm", 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 520f, 36f);
            WowUi.Button(root, "Log out", 850f, -4f, 200f, 44f, 21f, () => { var h = LogoutPressed; if (h != null) h(); });

            for (int i = 0; i < _realms.Count && i < 10; i++)
            {
                RealmInfo r = _realms[i];
                int col = i / 5, row = i % 5;
                RectTransform box = WowUi.Place(root, "Realm " + i, 50f + col * 510f, 46f + row * 56f, 490f, 52f);
                var bg = box.gameObject.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.35f);
                bg.raycastTarget = false;
                TextMeshProUGUI nm = WowUi.Text(box, "Name", r.Name ?? "?", 19f, r.Offline ? WowUi.Grey : WowUi.GlueGold, TextAlignmentOptions.Left, 12f, -2f, 300f, 26f);
                nm.enableAutoSizing = true;
                nm.fontSizeMin = 12f;
                nm.fontSizeMax = 19f;
                string detail = WowOnlineNames.RealmTypeName(r.Type) + "  -  " + r.Characters + " character" + (r.Characters == 1 ? "" : "s") + (r.Offline ? "  -  offline" : "") + (r.Locked ? "  -  locked" : "");
                WowUi.Text(box, "Detail", detail, 14f, Color.white, TextAlignmentOptions.Left, 12f, 24f, 300f, 22f);
                int index = i;
                WowGlueButton b = WowUi.Button(box, "Enter Realm", 322f, 5f, 164f, 42f, 18f, () => { var h = RealmPicked; if (h != null) h(index); });
                b.Interactable = !r.Offline;
            }

            if (_realms.Count == 0)
            {
                WowUi.Text(root, "Empty", "The server lists no realms.", 22f, Color.white, TextAlignmentOptions.Center, 90f, 120f, WowRoom.PanelWidth - 180f, 60f);
            }
        }

        private void BuildCharacters(RectTransform root)
        {
            TextMeshProUGUI title = WowUi.Text(root, "Title", "Choose a character" + (_realmName.Length > 0 ? " - " + _realmName : ""), 26f, WowUi.GlueGold, TextAlignmentOptions.Left, 56f, 2f, 470f, 36f);
            title.enableAutoSizing = true;
            title.fontSizeMin = 16f;
            title.fontSizeMax = 26f;
            WowUi.Text(root, "Count", _chars.Count + " of " + MaxCharacters, 18f, WowUi.Grey, TextAlignmentOptions.Right, 520f, 10f, 110f, 26f);
            WowGlueButton add = WowUi.Button(root, "New character", 640f, -4f, 220f, 44f, 20f, () => { var h = NewPressed; if (h != null) h(); });
            add.Interactable = _charsLoaded && _chars.Count < MaxCharacters && _classes.Count > 0 && _races.Count > 0;
            WowUi.Button(root, "Log out", 870f, -4f, 180f, 44f, 20f, () => { var h = LogoutPressed; if (h != null) h(); });

            if (!_charsLoaded)
            {
                WowUi.Text(root, "Loading", "Loading characters...", 24f, WowUi.GlueGold, TextAlignmentOptions.Center, 90f, 120f, WowRoom.PanelWidth - 180f, 50f);
                return;
            }

            if (_chars.Count == 0)
            {
                WowUi.Text(root, "Empty", "No character on this realm yet.\nChoose \"New character\": faction, race and class, then a name (your Somnium name is suggested).", 21f, Color.white, TextAlignmentOptions.Center, 90f, 100f, WowRoom.PanelWidth - 180f, 110f);
                return;
            }

            Texture2D classAtlas = WowUi.Tex(WowClassInfo.IconAtlas);
            Texture2D raceAtlas = WowUi.Tex(WowRaceInfo.RaceAtlas);

            for (int i = 0; i < _chars.Count && i < MaxCharacters; i++)
            {
                CharInfo c = _chars[i];
                WowClassEntry e = WowClassInfo.Find(_classes, (WowClass)c.Class);
                WowRaceEntry re = WowRaceInfo.Find(_races, c.Race);
                int col = i / 5, row = i % 5;
                RectTransform r = WowUi.Place(root, "Character " + i, 50f + col * 510f, 46f + row * 56f, 490f, 52f);
                var bg = r.gameObject.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.35f);
                bg.raycastTarget = false;

                if (re != null) WowUi.Picture(r, "Race Icon", raceAtlas, RaceRect(re, c.Gender), 4f, 6f, 40f, 40f);
                if (e != null) WowUi.Picture(r, "Class Icon", classAtlas, new Rect(e.IconLeft, e.IconTop, e.IconRight - e.IconLeft, e.IconBottom - e.IconTop), 48f, 6f, 40f, 40f);

                Color cc = e != null ? new Color(e.R, e.G, e.B) : WowUi.GlueGold;
                string raceName = re != null ? re.Name : "";
                string className = e != null ? e.Name : ClassWords((WowClass)c.Class);
                TextMeshProUGUI nm = WowUi.Text(r, "Name", c.Name, 17f, cc, TextAlignmentOptions.Left, 96f, -3f, 200f, 24f);
                nm.enableAutoSizing = true;
                nm.fontSizeMin = 11f;
                nm.fontSizeMax = 17f;
                TextMeshProUGUI lvl = WowUi.Text(r, "Level", "Level " + c.Level + " " + (raceName.Length > 0 ? raceName + " " : "") + className, 14f, Color.white, TextAlignmentOptions.Left, 96f, 16f, 200f, 20f);
                lvl.enableAutoSizing = true;
                lvl.fontSizeMin = 10f;
                lvl.fontSizeMax = 14f;
                TextMeshProUGUI zn = WowUi.Text(r, "Zone", ZoneName(c), 13f, WowUi.Grey, TextAlignmentOptions.Left, 96f, 33f, 200f, 19f);
                zn.enableAutoSizing = true;
                zn.fontSizeMin = 10f;
                zn.fontSizeMax = 13f;
                CharInfo pick = c;
                WowUi.Button(r, "Enter World", 300f, 5f, 118f, 42f, 16f, () => { var h = EnterPressed; if (h != null) h(pick); });
                WowUi.Button(r, "Delete", 420f, 5f, 66f, 42f, 14f, () => ConfirmDelete(pick));
            }
        }

        private static string ClassWords(WowClass c)
        {
            return c == WowClass.DeathKnight ? "Death Knight" : c.ToString();
        }

        private static Rect RaceRect(WowRaceEntry e, int gender)
        {
            if (gender == 1) return new Rect(e.FemaleLeft, e.FemaleTop, e.FemaleRight - e.FemaleLeft, e.FemaleBottom - e.FemaleTop);
            return new Rect(e.IconLeft, e.IconTop, e.IconRight - e.IconLeft, e.IconBottom - e.IconTop);
        }

        /// <summary>The character list's zone (AreaTable id; 0 before the first login) -> its name, else the map's name.</summary>
        private string ZoneName(CharInfo c)
        {
            string n;
            if (_zoneNames.TryGetValue(c.Guid, out n)) return n;
            n = "";
            try
            {
                if (_files != null && c.Zone > 0) n = WowAreaLookup.AreaName(_files, c.Zone) ?? "";
                if (n.Length == 0 && _files != null) n = WowLocations.MapName(_files, c.Map) ?? "";
            }
            catch (Exception)
            {
                n = "";
            }
            if (_zoneNames.Count > 64) _zoneNames.Clear();
            _zoneNames[c.Guid] = n;
            return n;
        }

        // ------------------------------------------------------------------ delete confirmation

        private void ConfirmDelete(CharInfo c)
        {
            if (_room == null || _room.Panel == null || c == null) return;
            CloseDialog();
            WowRaceEntry re = WowRaceInfo.Find(_races, c.Race);
            WowClassEntry e = WowClassInfo.Find(_classes, (WowClass)c.Class);
            string what = c.Name + ", level " + c.Level + " " + (re != null ? re.Name + " " : "") + (e != null ? e.Name : ClassWords((WowClass)c.Class));
            WowLog.Info("Online: delete asked for " + what + " (guid " + c.Guid + ").");

            RectTransform blocker = WowUi.Place(_room.Panel.transform, "Online Delete Dialog", 0f, 0f, WowRoom.PanelWidth, WowRoom.PanelHeight);
            _dialog = blocker.gameObject;
            var dim = _dialog.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true;
            const float W = 640f, H = 240f;
            float x = (WowRoom.PanelWidth - W) / 2f, y = 330f;
            WowUi.Backdrop(blocker, x, y, W, H, 32f);
            WowUi.Text(blocker, "Question", "Delete " + what + " from the server?\nThis cannot be undone.", 23f, Color.white, TextAlignmentOptions.Center, x + 40f, y + 36f, W - 80f, 90f);
            CharInfo target = c;
            WowUi.Button(blocker, "Delete", x + 80f, y + 150f, 220f, 48f, 22f, () =>
            {
                CloseDialog();
                WowLog.Info("Online: delete confirmed for " + target.Name + " (guid " + target.Guid + ").");
                var h = DeleteConfirmed;
                if (h != null) h(target);
            });
            WowUi.Button(blocker, "Cancel", x + W - 300f, y + 150f, 220f, 48f, 22f, () => { WowLog.Info("Online: delete cancelled."); CloseDialog(); });
        }

        public void CloseDialog()
        {
            if (_dialog != null)
            {
                UnityEngine.Object.Destroy(_dialog);
                _dialog = null;
            }
        }

        // ------------------------------------------------------------------ name dialog (end of character creation)

        /// <summary>The name step: the suggested name (editable), male/female, what is being created ("Night Elf Druid").</summary>
        public void ShowNameDialog(string name, int gender, string what)
        {
            if (_room == null || _room.Panel == null) return;
            CloseNameDialog();
            _gender = gender == 1 ? 1 : 0;
            RectTransform blocker = WowUi.Place(_room.Panel.transform, "Online Name Dialog", 0f, 0f, WowRoom.PanelWidth, WowRoom.PanelHeight);
            _nameDialog = blocker.gameObject;
            var dim = _nameDialog.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true;
            const float W = 700f, H = 380f;
            float x = (WowRoom.PanelWidth - W) / 2f, y = 250f;
            WowUi.Backdrop(blocker, x, y, W, H, 32f);
            WowUi.Text(blocker, "Title", "Name your character", 26f, WowUi.GlueGold, TextAlignmentOptions.Center, x + 40f, y + 26f, W - 80f, 36f);
            WowUi.Text(blocker, "What", what ?? "", 18f, WowUi.Grey, TextAlignmentOptions.Center, x + 40f, y + 64f, W - 80f, 26f);
            _nameField = WowTextField.Create(blocker, "Name", x + 150f, y + 100f, 400f, 50f, 26f, "name (letters only)", false, WowOnlineNames.MaxName);
            _nameField.text = name ?? "";
            Hook(_nameField, "Name", false, PressCreate);
            _male = WowUi.Button(blocker, "Male", x + 150f, y + 162f, 196f, 44f, 20f, () => SetGender(0));
            _female = WowUi.Button(blocker, "Female", x + 354f, y + 162f, 196f, 44f, 20f, () => SetGender(1));
            _nameError = WowUi.Text(blocker, "Error", "", 18f, new Color(1f, 0.3f, 0.25f), TextAlignmentOptions.Center, x + 40f, y + 214f, W - 80f, 56f);
            _nameError.enableAutoSizing = true;
            _nameError.fontSizeMin = 12f;
            _nameError.fontSizeMax = 18f;
            _createButton = WowUi.Button(blocker, "Create", x + 90f, y + 290f, 230f, 50f, 22f, PressCreate);
            WowUi.Button(blocker, "Cancel", x + W - 320f, y + 290f, 230f, 50f, 22f, () =>
            {
                CloseNameDialog();
                var h = NameCancelled;
                if (h != null) h();
            });
            SetGender(_gender);
            if (WowPlayer.XrPresent && _keyboard != null) _keyboard.Open(_nameField, "Name", false, PressCreate);
        }

        private void SetGender(int g)
        {
            _gender = g;
            if (_male != null) _male.Selected = g == 0;
            if (_female != null) _female.Selected = g == 1;
        }

        private void PressCreate()
        {
            if (_nameField == null) return;
            string name = WowOnlineNames.Normalize(_nameField.text);
            string problem = WowOnlineNames.NameProblem(name);

            if (problem != null)
            {
                SetNameError(problem);
                if (WowPlayer.XrPresent && _keyboard != null) _keyboard.Open(_nameField, "Name", false, PressCreate);
                return;
            }

            _nameField.text = name;
            SetNameError("Creating " + name + "...", false);
            if (_createButton != null) _createButton.Interactable = false;
            var h = NameCreatePressed;
            if (h != null) h(name, _gender);
        }

        /// <summary>The server's answer (or our own check) under the name field; the name stays editable.</summary>
        public void SetNameError(string text)
        {
            SetNameError(text, true);
        }

        private void SetNameError(string text, bool error)
        {
            if (_nameError == null) return;
            _nameError.text = text ?? "";
            _nameError.color = error ? new Color(1f, 0.3f, 0.25f) : Color.white;
            if (error && _createButton != null) _createButton.Interactable = true;
        }

        public void CloseNameDialog()
        {
            if (_nameDialog != null)
            {
                if (_keyboard != null && _keyboard.Target == _nameField) _keyboard.Close();
                UnityEngine.Object.Destroy(_nameDialog);
                _nameDialog = null;
            }

            _nameField = null;
            _nameError = null;
            _male = _female = _createButton = null;
        }

        private void CloseKeyboard()
        {
            if (_keyboard == null || !_keyboard.IsOpen) return;
            TMP_InputField t = _keyboard.Target;
            if (t == null || t == _server || t == _account || t == _password) _keyboard.Close();
        }
    }
}
