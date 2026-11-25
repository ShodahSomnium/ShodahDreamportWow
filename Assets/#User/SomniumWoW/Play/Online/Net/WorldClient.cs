using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// The world-server connection of the 3.3.5a client, following AzerothCore src/server/game/Server/WorldSocket.cpp:
    /// - server header: 2-byte big-endian size (payload + 2) + uint16 LE opcode; "large" packets (size &gt; 0x7FFF) use 3 size bytes
    ///   with 0x80 set in the first (ServerPktHeader.h). Client header: 2-byte big-endian size (payload + 4) + uint32 LE opcode
    ///   (ClientPktHeader, IsValidSize: size &lt; 10240).
    /// - SMSG_AUTH_CHALLENGE (uint32 1, 4-byte seed, 32 bytes) -&gt; CMSG_AUTH_SESSION (plain), then headers are ARC4-drop1024
    ///   encrypted both ways (WorldCrypt).
    /// - SMSG_COMPRESSED_UPDATE_OBJECT / SMSG_COMPRESSED_MOVES are inflated here and dispatched as SMSG_UPDATE_OBJECT / SMSG_MULTIPLE_MOVES.
    /// - keepalive: CMSG_PING every PingIntervalSeconds (AC counts pings closer than 27 s as over-speed), SMSG_TIME_SYNC_REQ is
    ///   answered on the socket thread. SMSG_WARDEN_DATA is never answered. SMSG_PONG, SMSG_TIME_SYNC_REQ, SMSG_WARDEN_DATA and SMSG_AUTH_CHALLENGE/RESPONSE are consumed
    ///   on the socket thread and never reach handlers.
    /// Sockets run on two background threads; received packets wait in a queue that Poll() drains on the MAIN thread into the
    /// handlers registered with On(). Hold()/Release() park that queue (WowLogin holds it at SMSG_LOGIN_VERIFY_WORLD so no world
    /// packet is lost before the world layer has registered its handlers).
    /// </summary>
    public sealed class WorldClient : IDisposable
    {
        public enum Status { Idle, Connecting, Authenticating, Queued, Ready, Closed }

        struct Outgoing { public ushort Opcode; public byte[] Body; public bool Plain; }

        sealed class Handler { public Action<WowPacket> Fn; public object Owner; }

        public readonly string Host;
        public readonly int Port;
        public readonly string Account;
        public readonly int RealmId;
        readonly byte[] _key;

        volatile Status _status = Status.Idle;
        volatile string _closeReason;
        volatile bool _authOk, _closing;
        volatile int _queuePosition;
        Socket _socket;
        WorldCrypt _crypt;           // created when CMSG_AUTH_SESSION is queued; Encrypt used only by the writer, Decrypt only by the reader
        Thread _reader, _writer;

        readonly object _sendLock = new object();
        readonly Queue<Outgoing> _sendQueue = new Queue<Outgoing>();
        readonly object _inLock = new object();
        List<WowPacket> _inbox = new List<WowPacket>();
        List<WowPacket> _work = new List<WowPacket>();
        bool _held;
        bool _closedReported;

        readonly Dictionary<ushort, Handler[]> _handlers = new Dictionary<ushort, Handler[]>();
        readonly Dictionary<ushort, int> _unhandled = new Dictionary<ushort, int>();
        readonly HashSet<ushort> _handlerErrorsLogged = new HashSet<ushort>();

        // keepalive
        uint _pingSeq;
        int _pingSentTick, _lastPongTick;
        volatile int _latencyMs;
        volatile bool _wardenLogged;
        readonly int _startTick = Environment.TickCount;

        public float PingIntervalSeconds = 30f;
        /// <summary>No SMSG_PONG for this long after a ping = the connection is dead.</summary>
        public float PongTimeoutSeconds = 90f;
        public int ConnectTimeoutMs = 10000;

        public Status State { get { return _status; } }
        public bool IsReady { get { return _status == Status.Ready; } }
        public bool IsClosed { get { return _status == Status.Closed; } }
        /// <summary>Player-readable reason once Closed.</summary>
        public string CloseReason { get { return _closeReason; } }
        public int QueuePosition { get { return _queuePosition; } }
        public int LatencyMs { get { return _latencyMs; } }
        public long PacketsIn, PacketsOut;
        /// <summary>SMSG_LOGIN_VERIFY_WORLD of the current login (set by WowLogin; HasLoginPosition false before).</summary>
        public LoginVerifyWorld LoginPosition;
        public bool HasLoginPosition;

        /// <summary>Fires once on the main thread (inside Poll) when the connection closes, with the reason.</summary>
        public event Action<string> Disconnected;

        /// <param name="realmId">RealmInfo.Id of the picked realm (AUTH_SESSION's RealmID must match the world server's realm).</param>
        public WorldClient(string host, int port, string account, byte[] sessionKey, int realmId)
        {
            Host = host; Port = port; Account = account; RealmId = realmId;
            _key = (byte[])sessionKey.Clone();
        }

        /// <summary>Starts connecting (background threads). Watch State / CloseReason.</summary>
        public void Connect()
        {
            if (_status != Status.Idle) return;
            _status = Status.Connecting;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "WowWorldRead" };
            _reader.Start();
        }

        // ------------------------------------------------------------------ main-thread API

        /// <summary>Registers a handler for a server opcode (main thread). Several handlers per opcode run in order; owner groups them for OffAll.</summary>
        public void On(ushort opcode, Action<WowPacket> handler, object owner = null)
        {
            _handlers.TryGetValue(opcode, out Handler[] old);
            int n = old == null ? 0 : old.Length;
            var arr = new Handler[n + 1];
            if (old != null) Array.Copy(old, arr, n);
            arr[n] = new Handler { Fn = handler, Owner = owner };
            _handlers[opcode] = arr;
        }

        public void Off(ushort opcode, Action<WowPacket> handler)
        {
            if (!_handlers.TryGetValue(opcode, out Handler[] old)) return;
            var list = new List<Handler>(old);
            list.RemoveAll(h => h.Fn == handler);
            if (list.Count == 0) _handlers.Remove(opcode); else _handlers[opcode] = list.ToArray();
        }

        /// <summary>Removes every handler registered with this owner.</summary>
        public void OffAll(object owner)
        {
            var keys = new List<ushort>(_handlers.Keys);
            foreach (ushort k in keys)
            {
                var list = new List<Handler>(_handlers[k]);
                list.RemoveAll(h => ReferenceEquals(h.Owner, owner));
                if (list.Count == 0) _handlers.Remove(k); else _handlers[k] = list.ToArray();
            }
        }

        /// <summary>Queues a client packet. Packets sent before the session is ready wait until AUTH_OK.</summary>
        public void Send(ushort opcode, byte[] body)
        {
            if (_status == Status.Closed) return;
            if (body == null) body = new byte[0];
            if (body.Length + 4 >= 10240) { WowLog.Warn("Online world: " + Op.Name(opcode) + " too large (" + body.Length + " bytes), not sent"); return; }
            lock (_sendLock)
            {
                _sendQueue.Enqueue(new Outgoing { Opcode = opcode, Body = body });
                Monitor.PulseAll(_sendLock);
            }
        }

        public void Send(ushort opcode, PacketWriter w) { Send(opcode, w.ToArray()); }
        public void Send(ushort opcode) { Send(opcode, new byte[0]); }

        /// <summary>Stops Poll() dispatching after the current packet; later packets stay queued until Release().</summary>
        public void Hold() { _held = true; }
        public void Release() { _held = false; }
        public bool Held { get { return _held; } }

        /// <summary>Main thread: dispatches every queued packet to its handlers (and reports a disconnect once).</summary>
        public void Poll()
        {
            if (!_held)
            {
                lock (_inLock)
                {
                    var t = _inbox; _inbox = _work; _work = t;
                }
                int i = 0;
                for (; i < _work.Count && !_held; i++) Dispatch(_work[i]);
                if (i < _work.Count)
                {
                    lock (_inLock) _inbox.InsertRange(0, _work.GetRange(i, _work.Count - i));
                }
                _work.Clear();
            }
            if (_status == Status.Closed && !_closedReported)
            {
                bool empty;
                lock (_inLock) empty = _inbox.Count == 0;
                if (empty || _held)
                {
                    _closedReported = true;
                    var d = Disconnected;
                    if (d != null) d(_closeReason);
                }
            }
        }

        void Dispatch(WowPacket p)
        {
            if (!_handlers.TryGetValue(p.Opcode, out Handler[] hs))
            {
                _unhandled.TryGetValue(p.Opcode, out int c);
                _unhandled[p.Opcode] = c + 1;
                return;
            }
            for (int k = 0; k < hs.Length; k++)
            {
                p.Pos = 0;
                try { hs[k].Fn(p); }
                catch (Exception e)
                {
                    if (_handlerErrorsLogged.Add(p.Opcode))
                        WowLog.Error("Online world: handler for " + p.Name + " (" + p.Length + " bytes) failed: " + e.GetType().Name + " " + e.Message);
                }
            }
        }

        /// <summary>"SMSG_X x12, SMSG_Y x3" of the opcodes that arrived with no handler (for one summary log line).</summary>
        public string UnhandledSummary(int max)
        {
            var list = new List<KeyValuePair<ushort, int>>(_unhandled);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < list.Count && i < max; i++) parts.Add(Op.Name(list[i].Key) + " x" + list[i].Value);
            if (list.Count > max) parts.Add("... " + (list.Count - max) + " more");
            return list.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }

        public void Dispose() { Close("Disconnected."); }

        public void Close(string reason)
        {
            if (_status == Status.Closed) return;
            if (_closeReason == null) _closeReason = reason;
            _closing = true;
            _status = Status.Closed;
            try { var s = _socket; if (s != null) s.Close(); } catch (Exception) { }
            lock (_sendLock) Monitor.PulseAll(_sendLock);
        }

        // ------------------------------------------------------------------ socket threads

        /// <summary>The client clock of CMSG_TIME_SYNC_RESP (ms since this WorldClient was created); OnlineSession stamps MovementInfo.time with it. Thread-safe.</summary>
        public uint ClientTicks() { return (uint)(Environment.TickCount - _startTick); }

        void ReadLoop()
        {
            string where = "connecting";
            try
            {
                _socket = NetUtil.Connect(Host, Port, ConnectTimeoutMs, out string err);
                if (_socket == null) { Close("Could not reach the realm " + Host + ":" + Port + " (" + err + ")."); return; }
                if (_closing) { _socket.Close(); return; }
                _socket.ReceiveTimeout = 30000;
                _status = Status.Authenticating;
                WowLog.Info("Online world: connected to " + Host + ":" + Port);
                _writer = new Thread(WriteLoop) { IsBackground = true, Name = "WowWorldWrite" };
                _writer.Start();
                var stream = new NetworkStream(_socket, false);
                var hdr = new byte[5];
                var raw = new byte[5];
                bool firstAfterSession = false;
                where = "reading";
                while (!_closing)
                {
                    // ---- header: 4 bytes, or 5 when the (decrypted) first byte has 0x80 (ServerPktHeader isLargePacket)
                    ReadInto(stream, hdr, 0, 1);
                    raw[0] = hdr[0];
                    WorldCrypt crypt = _crypt;
                    if (crypt != null) crypt.Decrypt(hdr, 0, 1);
                    bool large = (hdr[0] & 0x80) != 0;
                    int hlen = large ? 5 : 4;
                    ReadInto(stream, hdr, 1, hlen - 1);
                    Array.Copy(hdr, 1, raw, 1, hlen - 1);
                    if (crypt != null) crypt.Decrypt(hdr, 1, hlen - 1);
                    int size = large ? ((hdr[0] & 0x7F) << 16) | (hdr[1] << 8) | hdr[2] : (hdr[0] << 8) | hdr[1];
                    ushort opcode = (ushort)(hdr[hlen - 2] | hdr[hlen - 1] << 8);
                    if (firstAfterSession)
                    {
                        firstAfterSession = false;
                        // AzerothCore sends AUTH_UNKNOWN_ACCOUNT before AuthCrypt.Init (plain); every other answer is encrypted.
                        if (opcode != Op.SMSG_AUTH_RESPONSE && raw[0] == 0x00 && raw[1] == 0x03 && raw[2] == 0xEE && raw[3] == 0x01)
                        {
                            byte code = hlen == 5 ? raw[4] : NetUtil.ReadExact(stream, 1)[0];
                            Close(NetCodes.WorldAuth(code));
                            return;
                        }
                    }
                    if (size < 2 || opcode >= Op.Count || size > 0x400000)
                    {
                        Close("The world connection went out of sync (header size " + size + ", opcode " + opcode + ").");
                        return;
                    }
                    byte[] body = size > 2 ? NetUtil.ReadExact(stream, size - 2) : new byte[0];
                    PacketsIn++;
                    if (opcode == Op.SMSG_AUTH_CHALLENGE)
                    {
                        SendAuthSession(body);
                        firstAfterSession = true;
                        continue;
                    }
                    HandleOnSocketThread(opcode, body);
                }
            }
            catch (Exception e)
            {
                if (!_closing)
                {
                    string why = e is IOException || e is SocketException || e is ObjectDisposedException
                        ? "The realm closed the connection" + (_authOk ? "." : " while " + where + " (" + NetUtil.Short(e) + ").")
                        : "World connection error: " + e.GetType().Name + " " + e.Message;
                    Close(why);
                }
            }
            finally
            {
                if (_closeReason == null) _closeReason = "Disconnected.";
                _status = Status.Closed;
                _closing = true;
                lock (_sendLock) Monitor.PulseAll(_sendLock);
                WowLog.Info("Online world: closed (" + _closeReason + "), in " + PacketsIn + " out " + PacketsOut + " packets; no handler for: " + UnhandledSummarySafe());
            }
        }

        string UnhandledSummarySafe()
        {
            try { return UnhandledSummary(12); } catch (Exception) { return "?"; }   // main thread may be mutating it
        }

        static void ReadInto(Stream s, byte[] b, int off, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, off + got, n - got);
                if (r <= 0) throw new IOException("connection closed by the server");
                got += r;
            }
        }

        /// <summary>WorldSocket::HandleAuthSession field order; digest = SHA1(account, uint32 0, clientSeed, serverSeed, K).</summary>
        void SendAuthSession(byte[] challenge)
        {
            var p = new WowPacket(Op.SMSG_AUTH_CHALLENGE, challenge);
            p.U32();                                   // 1
            byte[] serverSeed = p.Bytes(4);            // _authSeed
            byte[] clientSeed = Srp6.RandomBytes(4);
            byte[] accountBytes = System.Text.Encoding.UTF8.GetBytes(Account);
            byte[] digest = Srp6.Sha1(accountBytes, new byte[4], clientSeed, serverSeed, _key);
            var w = new PacketWriter();
            w.U32(AuthClient.Build);                   // Build
            w.U32(0);                                  // LoginServerID
            w.CString(Account);                        // Account
            w.U32(0);                                  // LoginServerType
            w.Bytes(clientSeed);                       // LocalChallenge
            w.U32(0);                                  // RegionID
            w.U32(0);                                  // BattlegroupID
            w.U32((uint)RealmId);                      // RealmID (checked against the world server's realm)
            w.U64(0);                                  // DosResponse (read, not checked)
            w.Bytes(digest);
            // Addon block (WorldSession::ReadAddonsInfo): uint32 uncompressed size + zlib { uint32 addonsCount = 0, uint32 currentTime }.
            byte[] addons = new PacketWriter().U32(0).U32(0).ToArray();
            w.U32((uint)addons.Length).Bytes(Zlib.Compress(addons));
            lock (_sendLock)
            {
                _crypt = new WorldCrypt(_key, false);  // AC: _authCrypt.Init(account.SessionKey) before answering
                _sendQueue.Enqueue(new Outgoing { Opcode = Op.CMSG_AUTH_SESSION, Body = w.ToArray(), Plain = true });
                Monitor.PulseAll(_sendLock);
            }
        }

        void HandleOnSocketThread(ushort opcode, byte[] body)
        {
            switch (opcode)
            {
                case Op.SMSG_AUTH_RESPONSE:
                {
                    byte code = body.Length > 0 ? body[0] : (byte)0xFF;
                    if (code == NetCodes.AUTH_OK)
                    {
                        _queuePosition = 0;
                        _socket.ReceiveTimeout = 0;
                        _lastPongTick = Environment.TickCount;
                        _pingSentTick = Environment.TickCount - (int)(PingIntervalSeconds * 1000) + 2000; // first ping ~2 s after login
                        _authOk = true;
                        _status = Status.Ready;
                        lock (_sendLock) Monitor.PulseAll(_sendLock);
                        WowLog.Info("Online world: session ready (AUTH_OK) as " + Account + " on realm " + RealmId);
                    }
                    else if (code == NetCodes.AUTH_WAIT_QUEUE)
                    {
                        // SendAuthWaitQueue: code, uint32 position, uint8 / long SendAuthResponse form: position at offset 11
                        int pos = body.Length >= 16 ? (int)BitConverterLE.U32(body, 11) : body.Length >= 5 ? (int)BitConverterLE.U32(body, 1) : 0;
                        _queuePosition = pos;
                        _socket.ReceiveTimeout = 0;
                        _status = Status.Queued;
                        WowLog.Info("Online world: in the login queue, position " + pos);
                    }
                    else
                    {
                        Close(NetCodes.WorldAuth(code));
                        return;
                    }
                    return;   // handled here (state), not forwarded
                }
                case Op.SMSG_PONG:
                    _lastPongTick = Environment.TickCount;
                    _latencyMs = Math.Max(0, Environment.TickCount - _pingSentTick);
                    return;
                case Op.SMSG_TIME_SYNC_REQ:
                {
                    if (body.Length >= 4)
                    {
                        uint counter = BitConverterLE.U32(body, 0);
                        Send(Op.CMSG_TIME_SYNC_RESP, new PacketWriter().U32(counter).U32(ClientTicks()).ToArray());
                    }
                    return;
                }
                case Op.SMSG_WARDEN_DATA:
                    if (!_wardenLogged)
                    {
                        _wardenLogged = true;
                        WowLog.Warn("Online world: server has Warden on - switch it off (see ServerKit). Warden packets are never answered.");
                    }
                    return;   // not forwarded, never answered
                case Op.SMSG_COMPRESSED_UPDATE_OBJECT:
                    body = Zlib.InflatePacket(body);
                    opcode = Op.SMSG_UPDATE_OBJECT;
                    break;
                case Op.SMSG_COMPRESSED_MOVES:
                    body = Zlib.InflatePacket(body);
                    opcode = Op.SMSG_MULTIPLE_MOVES;
                    break;
            }
            var pkt = new WowPacket(opcode, body);
            lock (_inLock) _inbox.Add(pkt);
        }

        void WriteLoop()
        {
            try
            {
                var stream = new NetworkStream(_socket, false);
                var hdr = new byte[6];
                while (true)
                {
                    Outgoing o;
                    bool havePacket = false;
                    o = default(Outgoing);
                    lock (_sendLock)
                    {
                        while (!_closing)
                        {
                            if (_sendQueue.Count > 0 && (_authOk || _sendQueue.Peek().Plain)) { o = _sendQueue.Dequeue(); havePacket = true; break; }
                            if (_authOk && Environment.TickCount - _pingSentTick >= (int)(PingIntervalSeconds * 1000)) break;
                            Monitor.Wait(_sendLock, 250);
                        }
                    }
                    if (_closing) return;
                    if (!havePacket)
                    {
                        if (Environment.TickCount - _lastPongTick > (int)(PongTimeoutSeconds * 1000) && _pingSeq > 0)
                        {
                            Close("The realm stopped answering (no pong for " + (int)PongTimeoutSeconds + " s).");
                            return;
                        }
                        _pingSentTick = Environment.TickCount;
                        o = new Outgoing { Opcode = Op.CMSG_PING, Body = new PacketWriter().U32(++_pingSeq).U32((uint)_latencyMs).ToArray() };
                    }
                    int size = o.Body.Length + 4;
                    hdr[0] = (byte)(size >> 8); hdr[1] = (byte)size;
                    hdr[2] = (byte)o.Opcode; hdr[3] = (byte)(o.Opcode >> 8); hdr[4] = 0; hdr[5] = 0;
                    if (!o.Plain) _crypt.Encrypt(hdr, 0, 6);
                    var frame = new byte[6 + o.Body.Length];
                    Array.Copy(hdr, frame, 6);
                    Array.Copy(o.Body, 0, frame, 6, o.Body.Length);
                    stream.Write(frame, 0, frame.Length);
                    PacketsOut++;
                }
            }
            catch (Exception e)
            {
                if (!_closing) Close("Could not send to the realm (" + NetUtil.Short(e) + ").");
            }
        }
    }
}
