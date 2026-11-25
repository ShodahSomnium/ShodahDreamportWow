using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>One realm of the auth server's REALM_LIST (AzerothCore AuthSession::RealmListCallback, 2.x/3.x layout).</summary>
    public sealed class RealmInfo
    {
        public const int FlagVersionMismatch = 0x01, FlagOffline = 0x02, FlagSpecifyBuild = 0x04, FlagRecommended = 0x20, FlagNew = 0x40, FlagFull = 0x80;
        public int Id;                 // realm.Id.Realm - CMSG_AUTH_SESSION must send it back (else REALM_LIST_REALM_NOT_FOUND)
        public int Type;               // RealmType (0 normal, 1 PvP, 4 normal2, 6 RP, 8 RP-PvP)
        public bool Locked;            // AllowedSecurityLevel above the account's
        public int Flags;
        public string Name;
        public string Address;         // "host:port" as sent
        public string Host;
        public int Port;
        public float Population;
        public int Characters;         // this account's characters on the realm
        public int Timezone;           // realm category
        public int Build;              // only with FlagSpecifyBuild, else 0
        public bool Offline { get { return (Flags & FlagOffline) != 0; } }
        public override string ToString() { return Name + " (" + Address + ", " + Characters + " chars" + (Offline ? ", offline" : "") + ")"; }
    }

    /// <summary>
    /// The auth-server conversation of the 3.3.5a client (port 3724), on a background thread, following AzerothCore
    /// src/server/apps/authserver/Server/AuthSession.cpp: AUTH_LOGON_CHALLENGE -&gt; AUTH_LOGON_PROOF (SRP6) -&gt; REALM_LIST.
    /// The password lives only on the worker thread's stack until the SRP proof is computed; it is never logged or stored.
    /// Poll Finished from the main thread, then read Error or SessionKey/Realms.
    /// </summary>
    public sealed class AuthClient
    {
        public const ushort Build = 12340;
        const byte CMD_LOGON_CHALLENGE = 0x00, CMD_LOGON_PROOF = 0x01, CMD_REALM_LIST = 0x10;

        public enum Phase { Idle, Connecting, Challenge, Proof, RealmList, Done, Failed }

        volatile Phase _phase = Phase.Idle;
        volatile bool _finished;
        volatile bool _cancel;
        Socket _socket;

        public Phase CurrentPhase { get { return _phase; } }
        public bool Finished { get { return _finished; } }
        /// <summary>Player-readable failure, null on success.</summary>
        public string Error { get; private set; }
        /// <summary>K (40 bytes) - the world server finds the same key in its database.</summary>
        public byte[] SessionKey { get; private set; }
        public List<RealmInfo> Realms { get; private set; }
        /// <summary>Upper-cased account name as sent (AUTH_SESSION sends it again).</summary>
        public string Account { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public uint AccountFlags { get; private set; }
        public int ConnectTimeoutMs = 10000;
        public int ReadTimeoutMs = 20000;

        /// <summary>Starts the login on a background thread. locale = the installed client's locale ("enUS").</summary>
        public void Start(string host, int port, string account, string password, string locale)
        {
            Host = host; Port = port;
            Account = Srp6.UpperLatin((account ?? "").Trim());
            string loc = string.IsNullOrEmpty(locale) || locale.Length != 4 ? "enUS" : locale;
            string pass = password ?? "";
            var t = new Thread(() => Run(host, port, pass, loc));
            t.IsBackground = true;
            t.Name = "WowAuth";
            t.Start();
        }

        public void Cancel()
        {
            _cancel = true;
            try { var s = _socket; if (s != null) s.Close(); } catch (Exception) { }
        }

        void Fail(string why)
        {
            Error = why;
            _phase = Phase.Failed;
        }

        void Run(string host, int port, string password, string locale)
        {
            try
            {
                // AzerothCore: MAX_ACCOUNT_STR 17 (AccountMgr.h); AuthSession accepts I_len <= 17 (MAX_ACCEPTED_CHALLENGE_SIZE).
                // The automatic accounts (WowAutoAccount.NameFor) are exactly 17 characters.
                if (Account.Length == 0 || Account.Length > 17) { Fail("Enter your account name."); return; }
                _phase = Phase.Connecting;
                _socket = NetUtil.Connect(host, port, ConnectTimeoutMs, out string connectError);
                if (_socket == null) { Fail("Could not reach the login server " + host + ":" + port + " (" + connectError + ")."); return; }
                _socket.ReceiveTimeout = ReadTimeoutMs;
                _socket.SendTimeout = ReadTimeoutMs;
                var stream = new NetworkStream(_socket, false);

                // ---- AUTH_LOGON_CHALLENGE (sAuthLogonChallenge_C, packed): size = 30 + I_len
                _phase = Phase.Challenge;
                byte[] user = System.Text.Encoding.UTF8.GetBytes(Account);
                var w = new PacketWriter();
                w.U8(CMD_LOGON_CHALLENGE);
                w.U8(8);                              // 'error' byte: protocol version; HandleLogonChallenge never reads it
                w.U16(30 + user.Length);
                w.Bytes(new byte[] { (byte)'W', (byte)'o', (byte)'W', 0 });   // gamename
                w.U8(3).U8(3).U8(5).U16(Build);       // 3.3.5 (12340)
                w.Bytes(new byte[] { (byte)'6', (byte)'8', (byte)'x', 0 });   // platform "x86" (stored reversed)
                w.Bytes(new byte[] { (byte)'n', (byte)'i', (byte)'W', 0 });   // os "Win" (AC: "Restore string order as its byte order is reversed")
                w.Bytes(new byte[] { (byte)locale[3], (byte)locale[2], (byte)locale[1], (byte)locale[0] }); // country, reversed
                w.U32(0);                             // timezone_bias (not read by AzerothCore)
                w.U32(0);                             // ip (not read by AzerothCore)
                w.U8(user.Length).Bytes(user);
                Send(stream, w.ToArray());

                byte[] head = NetUtil.ReadExact(stream, 3);
                if (head[0] != CMD_LOGON_CHALLENGE) { Fail("The login server answered with an unexpected packet (0x" + head[0].ToString("X2") + ")."); return; }
                if (head[2] != NetCodes.WOW_SUCCESS) { Fail(NetCodes.AuthResult(head[2], false)); return; }
                byte[] B = NetUtil.ReadExact(stream, 32);
                byte[] gBytes = NetUtil.ReadExact(stream, NetUtil.ReadExact(stream, 1)[0]);
                byte[] nBytes = NetUtil.ReadExact(stream, NetUtil.ReadExact(stream, 1)[0]);
                byte[] salt = NetUtil.ReadExact(stream, 32);
                NetUtil.ReadExact(stream, 16);        // VersionChallenge (only used by StrictVersionCheck)
                byte securityFlags = NetUtil.ReadExact(stream, 1)[0];
                if (securityFlags != 0)
                {
                    Fail((securityFlags & 0x04) != 0
                        ? "This account has a two-factor (authenticator) token - not supported here; remove it on the server."
                        : "This account needs a PIN/matrix card - not supported here.");
                    return;
                }

                // ---- AUTH_LOGON_PROOF (sAuthLogonProof_C: cmd, A[32], M1[20], crc_hash[20], number_of_keys, securityFlags)
                _phase = Phase.Proof;
                Srp6ClientProof proof = Srp6Client.Compute(Account, password, B, gBytes, nBytes, salt, out string srpError);
                password = null;
                if (proof == null) { Fail("Login failed: " + srpError + "."); return; }
                w = new PacketWriter();
                w.U8(CMD_LOGON_PROOF).Bytes(proof.A).Bytes(proof.M1);
                w.Bytes(new byte[20]);                // crc_hash: only checked when the server sets StrictVersionCheck (default off)
                w.U8(0).U8(0);
                Send(stream, w.ToArray());

                head = NetUtil.ReadExact(stream, 2);
                if (head[0] != CMD_LOGON_PROOF) { Fail("The login server answered with an unexpected packet (0x" + head[0].ToString("X2") + ")."); return; }
                if (head[1] != NetCodes.WOW_SUCCESS) { Fail(NetCodes.AuthResult(head[1], true)); return; }
                // sAuthLogonProof_S (2.x/3.x): M2[20], AccountFlags u32, SurveyId u32, LoginFlags u16
                var rest = new WowPacket(0, NetUtil.ReadExact(stream, 30));
                byte[] m2 = rest.Bytes(20);
                AccountFlags = rest.U32();
                if (!Srp6.Equal(m2, proof.ExpectedM2)) { Fail("The login server's proof did not match - not a genuine server for this account."); return; }
                SessionKey = proof.SessionKey;

                // ---- REALM_LIST: cmd + uint32 0 (REALM_LIST_PACKET_SIZE 5)
                _phase = Phase.RealmList;
                Send(stream, new PacketWriter().U8(CMD_REALM_LIST).U32(0).ToArray());
                head = NetUtil.ReadExact(stream, 3);
                if (head[0] != CMD_REALM_LIST) { Fail("The login server answered with an unexpected packet (0x" + head[0].ToString("X2") + ")."); return; }
                int size = head[1] | head[2] << 8;
                Realms = ParseRealmList(NetUtil.ReadExact(stream, size));
                _phase = Phase.Done;
                WowLog.Info("Online auth: " + Account + " logged in at " + host + ":" + port + ", " + Realms.Count + " realm(s): " + string.Join("; ", Realms.ConvertAll(r => r.ToString()).ToArray()));
            }
            catch (Exception e)
            {
                if (_cancel) Fail("Cancelled.");
                else if (e is IOException || e is SocketException || e is ObjectDisposedException)
                    Fail("Lost the connection to the login server during " + _phase + " (" + NetUtil.Short(e) + ").");
                else Fail("Login failed: " + e.GetType().Name + " " + e.Message);
            }
            finally
            {
                try { if (_socket != null) _socket.Close(); } catch (Exception) { }
                _socket = null;
                if (_phase == Phase.Failed) WowLog.Warn("Online auth failed at " + host + ":" + port + ": " + Error);
                _finished = true;
            }
        }

        static void Send(NetworkStream s, byte[] b) { s.Write(b, 0, b.Length); s.Flush(); }

        /// <summary>REALM_LIST body after cmd+size: uint32 0, uint16 count, realms, then 0x10 0x00.</summary>
        public static List<RealmInfo> ParseRealmList(byte[] body)
        {
            var p = new WowPacket(0, body);
            p.U32();
            int count = p.U16();
            var list = new List<RealmInfo>(count);
            for (int i = 0; i < count; i++)
            {
                var r = new RealmInfo();
                r.Type = p.U8();
                r.Locked = p.U8() != 0;
                r.Flags = p.U8();
                r.Name = p.CString();
                r.Address = p.CString();
                r.Population = p.F32();
                r.Characters = p.U8();
                r.Timezone = p.U8();
                r.Id = p.U8();
                if ((r.Flags & RealmInfo.FlagSpecifyBuild) != 0) { p.U8(); p.U8(); p.U8(); r.Build = p.U16(); }
                NetUtil.SplitHostPort(r.Address, 8085, out string h, out int port);
                r.Host = h; r.Port = port;
                list.Add(r);
            }
            return list;
        }
    }

    public static class NetUtil
    {
        /// <summary>Connects with a timeout; null + reason on failure.</summary>
        public static Socket Connect(string host, int port, int timeoutMs, out string error)
        {
            error = null;
            Socket s = null;
            try
            {
                System.Net.IPAddress[] addrs = System.Net.Dns.GetHostAddresses(host);
                System.Net.IPAddress addr = null;
                foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) { addr = a; break; }
                if (addr == null && addrs.Length > 0) addr = addrs[0];
                if (addr == null) { error = "unknown host"; return null; }
                s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                s.NoDelay = true;
                IAsyncResult ar = s.BeginConnect(addr, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    s.Close();
                    error = "timed out after " + (timeoutMs / 1000) + " s";
                    return null;
                }
                s.EndConnect(ar);
                return s;
            }
            catch (Exception e)
            {
                try { if (s != null) s.Close(); } catch (Exception) { }
                error = Short(e);
                return null;
            }
        }

        public static byte[] ReadExact(Stream s, int n)
        {
            var b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) throw new IOException("connection closed by the server");
                got += r;
            }
            return b;
        }

        public static string Short(Exception e)
        {
            var se = e as SocketException ?? e.InnerException as SocketException;
            if (se != null) return se.SocketErrorCode.ToString();
            return e.Message;
        }

        public static void SplitHostPort(string address, int defaultPort, out string host, out int port)
        {
            host = address ?? "";
            port = defaultPort;
            int c = host.LastIndexOf(':');
            if (c > 0 && int.TryParse(host.Substring(c + 1), out int p)) { port = p; host = host.Substring(0, c); }
        }

        public static bool IsLoopback(string host)
        {
            return host == "127.0.0.1" || host == "localhost" || host == "::1" || host.StartsWith("127.", StringComparison.Ordinal);
        }
    }
}
