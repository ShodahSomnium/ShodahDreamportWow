using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    public enum WowLoginState { Idle, Connecting, Authenticating, Realms, Characters, EnteringWorld, InWorld, Failed }

    /// <summary>
    /// The login flow for the front end, main thread only (call Poll() every frame):
    ///   Start(host, port, account, password) -&gt; Connecting -&gt; Authenticating -&gt; Realms (auto-picked when there is one)
    ///   -&gt; Characters (list / create / delete) -&gt; EnterWorld(guid) -&gt; EnteringWorld -&gt; InWorld (World + Character + WorldPos ready)
    ///   Failed at any point, with FailReason in player words. Logout() in the world goes back to Characters.
    /// At InWorld the WorldClient is HELD (packets queue up): construct OnlineSession(files, login.World, login.Character, groundZ);
    /// it registers its handlers and calls World.Release(), and pumps World.Poll() from its Tick from then on.
    /// Credentials: the password goes straight to AuthClient's worker thread and is never stored, logged or sent elsewhere.
    /// </summary>
    public sealed class WowLogin : IDisposable
    {
        public WowLoginState State { get; private set; }
        /// <summary>Why Failed, in player words.</summary>
        public string FailReason { get; private set; }
        /// <summary>What is happening right now ("Connecting to wow.example.com...", "In queue: position 12").</summary>
        public string Status { get; private set; }
        /// <summary>Last result of a character action (created, name taken, deleted, login failed...). MessageCounter +1 each time.</summary>
        public string Message { get; private set; }
        public int MessageCounter { get; private set; }

        public string Host { get; private set; }
        public int Port { get; private set; }
        /// <summary>The upper-cased account name (the only credential the front end may remember).</summary>
        public string Account { get; private set; }
        public List<RealmInfo> Realms { get; private set; }
        public RealmInfo Realm { get; private set; }
        public List<CharInfo> Characters { get; private set; }
        /// <summary>True once SMSG_CHAR_ENUM arrived for the current request.</summary>
        public bool CharactersLoaded { get; private set; }
        public WorldClient World { get; private set; }
        /// <summary>The character that entered the world, and where (SMSG_LOGIN_VERIFY_WORLD).</summary>
        public CharInfo Character { get; private set; }
        public LoginVerifyWorld WorldPos { get; private set; }

        /// <summary>The installed client's locale, sent in the logon challenge.</summary>
        public string Locale = "enUS";
        public bool AutoPickSingleRealm = true;

        /// <summary>Fires (main thread) on every state change.</summary>
        public event Action<WowLoginState> StateChanged;

        AuthClient _auth;
        bool _worldWasReady;
        bool _logoutPending;
        /// <summary>A logout was requested and SMSG_LOGOUT_COMPLETE has not arrived yet.</summary>
        public bool LoggingOut { get { return _logoutPending; } }

        public void Start(string host, int port, string account, string password)
        {
            Reset();
            Host = (host ?? "").Trim();
            Port = port > 0 ? port : 3724;
            if (Host.Length == 0) { SetFailed("Enter the server address."); return; }
            if (string.IsNullOrEmpty(account)) { SetFailed("Enter your account name."); return; }
            if (string.IsNullOrEmpty(password)) { SetFailed("Enter your password."); return; }
            _auth = new AuthClient();
            _auth.Start(Host, Port, account, password, Locale);
            Account = _auth.Account;
            Status = "Connecting to " + Host + "...";
            SetState(WowLoginState.Connecting);
        }

        public void PickRealm(int index)
        {
            if (State != WowLoginState.Realms || Realms == null || index < 0 || index >= Realms.Count) return;
            RealmInfo r = Realms[index];
            if (r.Offline && Realms.Count > 1) { Say("The realm " + r.Name + " is offline (its world server is not running)."); return; }
            if (r.Offline) { SetFailed("The realm " + r.Name + " is offline - its world server is not running."); return; }
            Realm = r;
            string host = r.Host;
            if (NetUtil.IsLoopback(host) && !NetUtil.IsLoopback(Host))
            {
                WowLog.Warn("Online login: realm '" + r.Name + "' lists address " + r.Address + " (loopback) - using the login host " + Host +
                            " instead. Set realmlist.address to the public address on the server (ServerKit does).");
                host = Host;
            }
            World = new WorldClient(host, r.Port, Account, _auth.SessionKey, r.Id);
            World.On(Op.SMSG_CHAR_ENUM, OnCharEnum, this);
            World.On(Op.SMSG_CHAR_CREATE, OnCharCreate, this);
            World.On(Op.SMSG_CHAR_DELETE, OnCharDelete, this);
            World.On(Op.SMSG_CHARACTER_LOGIN_FAILED, OnLoginFailed, this);
            World.On(Op.SMSG_LOGIN_VERIFY_WORLD, OnVerifyWorld, this);
            World.On(Op.SMSG_LOGOUT_RESPONSE, OnLogoutResponse, this);
            World.On(Op.SMSG_LOGOUT_COMPLETE, OnLogoutComplete, this);
            World.Connect();
            Status = "Connecting to " + r.Name + "...";
            SetState(WowLoginState.Authenticating);
        }

        public void RefreshCharacters()
        {
            if (World == null || !World.IsReady) return;
            CharactersLoaded = false;
            World.Send(Op.CMSG_CHAR_ENUM);
        }

        public void CreateCharacter(CharCreateRequest req)
        {
            if (State != WowLoginState.Characters || req == null) return;
            if (string.IsNullOrEmpty(req.Name)) { Say(NetCodes.CharCreate(0x59)); return; }
            World.Send(Op.CMSG_CHAR_CREATE, req.Build());
            Status = "Creating " + req.Name + "...";
        }

        public void DeleteCharacter(ulong guid)
        {
            if (State != WowLoginState.Characters) return;
            World.Send(Op.CMSG_CHAR_DELETE, new PacketWriter().U64(guid).ToArray());
            Status = "Deleting character...";
        }

        /// <summary>Logs the character into the world (CMSG_PLAYER_LOGIN). Needs the character list first (the server only accepts listed guids).</summary>
        public void EnterWorld(ulong guid)
        {
            if (State != WowLoginState.Characters || Characters == null) return;
            CharInfo c = Characters.Find(x => x.Guid == guid);
            if (c == null) { Say(NetCodes.LoginFailed(5)); return; }
            Character = c;
            World.Send(Op.CMSG_PLAYER_LOGIN, new PacketWriter().U64(guid).ToArray());
            Status = "Entering the world as " + c.Name + "...";
            SetState(WowLoginState.EnteringWorld);
        }

        /// <summary>In the world: asks the server to log out (it may take 20 s unless resting). Ends in Characters.</summary>
        public void Logout()
        {
            if (State != WowLoginState.InWorld || World == null) return;
            _logoutPending = true;
            World.Send(Op.CMSG_LOGOUT_REQUEST);
            Status = "Logging out...";
        }

        public void Poll()
        {
            if (_auth != null && State == WowLoginState.Connecting && _auth.CurrentPhase >= AuthClient.Phase.Challenge && !_auth.Finished)
            {
                Status = "Logging in as " + Account + "...";
                SetState(WowLoginState.Authenticating);
            }
            if (_auth != null && _auth.Finished && (State == WowLoginState.Connecting || State == WowLoginState.Authenticating) && World == null)
            {
                if (_auth.Error != null) { SetFailed(_auth.Error); return; }
                Realms = _auth.Realms;
                if (Realms == null || Realms.Count == 0) { SetFailed("The server lists no realms."); return; }
                Status = "Choose a realm.";
                SetState(WowLoginState.Realms);
                if (AutoPickSingleRealm && Realms.Count == 1) PickRealm(0);
            }
            if (World == null) return;
            if (State < WowLoginState.InWorld || _logoutPending) World.Poll();   // in the world OnlineSession.Tick pumps it
            if (World.IsClosed)
            {
                if (State != WowLoginState.Failed && State != WowLoginState.Idle)
                    SetFailed(State == WowLoginState.InWorld ? "Disconnected from the realm: " + World.CloseReason : World.CloseReason);
                return;
            }
            if (State == WowLoginState.Authenticating)
            {
                if (World.State == WorldClient.Status.Queued) Status = "In the login queue, position " + World.QueuePosition + "...";
                if (World.IsReady && !_worldWasReady)
                {
                    _worldWasReady = true;
                    Characters = new List<CharInfo>();
                    Status = "Loading characters...";
                    SetState(WowLoginState.Characters);
                    RefreshCharacters();
                }
            }
        }

        void OnCharEnum(WowPacket p)
        {
            Characters = CharInfo.ReadEnum(p);
            CharactersLoaded = true;
            if (State == WowLoginState.Characters) Status = Characters.Count == 0 ? "No characters yet - create one." : "Choose a character.";
            WowLog.Info("Online login: " + Characters.Count + " character(s) on " + (Realm != null ? Realm.Name : "?") +
                        (Characters.Count > 0 ? ": " + string.Join("; ", Characters.ConvertAll(c => c.ToString()).ToArray()) : ""));
        }

        void OnCharCreate(WowPacket p)
        {
            byte code = p.U8();
            Say(NetCodes.CharCreate(code));
            if (code == NetCodes.CHAR_CREATE_SUCCESS) RefreshCharacters();
            Status = "Choose a character.";
        }

        void OnCharDelete(WowPacket p)
        {
            byte code = p.U8();
            Say(NetCodes.CharDelete(code));
            if (code == NetCodes.CHAR_DELETE_SUCCESS) RefreshCharacters();
            Status = "Choose a character.";
        }

        void OnLoginFailed(WowPacket p)
        {
            byte code = p.U8();
            Say(NetCodes.LoginFailed(code));
            if (State == WowLoginState.EnteringWorld) { Status = "Choose a character."; SetState(WowLoginState.Characters); }
        }

        void OnVerifyWorld(WowPacket p)
        {
            var v = new LoginVerifyWorld { Map = (int)p.U32(), X = p.F32(), Y = p.F32(), Z = p.F32(), O = p.F32() };
            WorldPos = v;
            if (State != WowLoginState.EnteringWorld) return;   // AC sends it only at login (HandlePlayerLoginFromDB / ...ToCharInWorld)
            World.LoginPosition = v;
            World.HasLoginPosition = true;
            World.Hold();   // keep the following world packets for the world layer
            Status = "In the world.";
            WowLog.Info("Online login: " + (Character != null ? Character.Name : "?") + " entered map " + v.Map + " at " +
                        v.X.ToString("0.0") + " " + v.Y.ToString("0.0") + " " + v.Z.ToString("0.0") + " o " + v.O.ToString("0.00"));
            SetState(WowLoginState.InWorld);
        }

        void OnLogoutResponse(WowPacket p)
        {
            uint reason = p.U32();
            bool instant = p.U8() != 0;
            if (reason != 0) { _logoutPending = false; Say("You can't log out now."); Status = "In the world."; }
            else Status = instant ? "Logging out..." : "Logging out in 20 seconds...";
        }

        void OnLogoutComplete(WowPacket p)
        {
            if (State != WowLoginState.InWorld) return;
            _logoutPending = false;
            World.Release();
            Character = null;
            Status = "Choose a character.";
            SetState(WowLoginState.Characters);
            RefreshCharacters();
        }

        void Say(string m)
        {
            Message = m;
            MessageCounter++;
            WowLog.Info("Online login: " + m);
        }

        void SetFailed(string reason)
        {
            FailReason = reason;
            Status = reason;
            SetState(WowLoginState.Failed);
        }

        void SetState(WowLoginState s)
        {
            if (State == s) return;
            State = s;
            WowLog.Info("Online login: " + s + (s == WowLoginState.Failed ? " - " + FailReason : ""));
            var h = StateChanged;
            if (h != null) h(s);
        }

        void Reset()
        {
            Dispose();
            State = WowLoginState.Idle;
            FailReason = null; Status = null; Realms = null; Realm = null; Characters = null; CharactersLoaded = false;
            Character = null; _worldWasReady = false; _logoutPending = false;
        }

        /// <summary>Closes every connection (auth and world).</summary>
        public void Dispose()
        {
            if (_auth != null) { _auth.Cancel(); _auth = null; }
            if (World != null) { World.OffAll(this); World.Dispose(); World = null; }
        }
    }
}
