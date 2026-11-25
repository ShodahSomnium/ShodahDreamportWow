using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>
    /// Asks the server's account service (ServerKit wow-accounts, PLAN8 2.4: HTTP/1.1 over plain TCP, port 8086, JSON) to create
    /// the automatic WoW account of a Somnium player. Main thread only (call Poll() every frame while State is Working):
    ///   Register(host, port, somniumId, nick, secret) -&gt; Working -&gt; Created / Rekeyed / Exists / NotAllowed / SlowDown /
    ///   BadRequest / ServerError / Unreachable / WrongAnswer, with Message in player words and AccountName = the derived name.
    /// Sends POST /register {"v":1,"somniumId","nick","salt","verifier"}: only the SRP6 salt + verifier leave the PC. The secret
    /// is only used to compute the verifier (WowAutoAccount.MakeRegistration, on the worker thread) and is not kept afterwards.
    /// The socket runs on a background thread (connect timeout 8 s, answer timeout 10 s); results reach the main thread in Poll().
    /// Any answer that is not exactly the documented HTTP code + JSON "status" pair counts as WrongAnswer.
    /// </summary>
    public sealed class AccountServiceClient
    {
        public enum Status { Idle, Working, Created, Rekeyed, Exists, NotAllowed, SlowDown, BadRequest, ServerError, Unreachable, WrongAnswer }

        public const int DefaultPort = 8086;
        /// <summary>Longer Somnium nicknames are cut (the request body must stay under the service's 2048-byte limit).</summary>
        public const int MaxNickChars = 64;
        const int MaxResponseBytes = 16384;

        public int ConnectTimeoutMs = 8000;
        public int ReadTimeoutMs = 10000;

        public Status State { get; private set; }
        /// <summary>The account name derived from the Somnium ID (WowAutoAccount.NameFor), set by Register; null when the ID is not a GUID.</summary>
        public string AccountName { get; private set; }
        /// <summary>True when the service made the new account a game master (gm.txt on the server).</summary>
        public bool IsGm { get; private set; }
        /// <summary>What happened, in player words (null while Idle).</summary>
        public string Message { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }

        sealed class Job
        {
            public volatile bool Cancelled;
            public volatile bool Done;
            public Socket Socket;              // guarded by lock(this)
            public Status Result;
            public bool Gm;
            public string Message;
            public string Detail;              // for the log only (never contains the salt, verifier or secret)
            public int Http;
            public long Ms;
            public readonly Stopwatch Sw = Stopwatch.StartNew();
        }

        /// <summary>What the worker thread gets. Secret is cleared by the worker as soon as it has read it.</summary>
        sealed class Request
        {
            public Job Job;
            public string Host, Name, SomniumId, Nick, Secret;
            public int Port, ConnectMs, ReadMs;
        }

        Job _job;

        /// <summary>
        /// Starts the registration (any running one is cancelled). port &lt;= 0 = DefaultPort. The secret must be the one the
        /// caller will store after Created/Rekeyed (WowAutoAccount.NewSecret()). It is handed to the worker thread, used there
        /// only to compute the verifier (WowAutoAccount.MakeRegistration, a few ms of big-number maths kept off the frame) and
        /// dropped right after; this object never keeps it.
        /// </summary>
        public void Register(string host, int port, string somniumId, string nick, string secret)
        {
            Cancel();
            Host = (host ?? "").Trim();
            Port = port > 0 && port <= 65535 ? port : DefaultPort;
            AccountName = WowAutoAccount.NameFor(somniumId);
            if (AccountName == null) { Finish(Status.BadRequest, "Your Somnium account ID can't be used for an automatic WoW account.", "not a GUID"); return; }
            if (Host.Length == 0) { Finish(Status.BadRequest, "Enter the server address.", "no host"); return; }
            if (!IsPlainHost(Host)) { Finish(Status.Unreachable, UnreachableText(Host, Port), "invalid characters in the host"); return; }
            if (string.IsNullOrEmpty(secret) || secret.Length > WowAutoAccount.SecretLength) { Finish(Status.BadRequest, "The generated WoW password is not valid.", "bad secret"); return; }

            string cleanNick = CleanNick(nick);
            var job = new Job();
            _job = job;
            IsGm = false;
            State = Status.Working;
            Message = "Creating your WoW account on " + Host + "...";
            WowLog.Info("Online account: registering " + AccountName + " (Somnium nickname '" + cleanNick + "') at " + Host + ":" + Port);
            var req = new Request
            {
                Job = job, Host = Host, Port = Port, Name = AccountName, SomniumId = somniumId, Nick = cleanNick, Secret = secret,
                ConnectMs = ConnectTimeoutMs, ReadMs = ReadTimeoutMs
            };
            var t = new Thread(Worker);
            t.IsBackground = true;
            t.Name = "WowAccount";
            t.Start(req);
        }

        /// <summary>Hands a finished answer to the main thread (State, Message, IsGm). Call every frame while Working.</summary>
        public void Poll()
        {
            Job j = _job;
            if (j == null || !j.Done) return;
            _job = null;
            IsGm = j.Gm;
            Message = j.Message;
            State = j.Result;
            string line = "Online account: " + AccountName + " at " + Host + ":" + Port + " -> " + j.Result +
                          (j.Http > 0 ? " (HTTP " + j.Http + ")" : "") + (j.Result == Status.Created ? (j.Gm ? ", game master" : ", normal player") : "") +
                          (string.IsNullOrEmpty(j.Detail) ? "" : " - " + j.Detail) + ", " + j.Ms + " ms";
            if (j.Result == Status.Created || j.Result == Status.Rekeyed) WowLog.Info(line); else WowLog.Warn(line);
        }

        /// <summary>Stops a running request (its answer is ignored) and goes back to Idle.</summary>
        public void Cancel()
        {
            Job j = _job;
            _job = null;
            if (j != null)
            {
                lock (j)
                {
                    j.Cancelled = true;
                    try { if (j.Socket != null) j.Socket.Close(); } catch (Exception) { }
                }
            }
            State = Status.Idle;
            Message = null;
            IsGm = false;
        }

        void Finish(Status s, string message, string detail)
        {
            State = s;
            Message = message;
            IsGm = false;
            WowLog.Warn("Online account: " + (AccountName ?? "(no name)") + " at " + Host + ":" + Port + " -> " + s + " - " + detail);
        }

        // ------------------------------------------------------------------ worker thread

        static void Worker(object o)
        {
            var req = (Request)o;
            string secret = req.Secret;
            req.Secret = null;
            byte[] body;
            try
            {
                string saltHex, verifierHex;
                WowAutoAccount.MakeRegistration(req.Name, secret, out saltHex, out verifierHex);
                secret = null;
                var sb = new StringBuilder(256);
                sb.Append("{\"v\":1,\"somniumId\":"); MiniJson.WriteString(sb, req.SomniumId);
                sb.Append(",\"nick\":"); MiniJson.WriteString(sb, req.Nick);
                sb.Append(",\"salt\":\"").Append(saltHex);
                sb.Append("\",\"verifier\":\"").Append(verifierHex).Append("\"}");
                body = new UTF8Encoding(false).GetBytes(sb.ToString());
            }
            catch (Exception e)
            {
                Done(req.Job, Status.BadRequest, "The generated WoW password is not valid.", "could not compute the verifier: " + e.GetType().Name);
                return;
            }
            if (req.Job.Cancelled) return;
            Run(req.Job, req.Host, req.Port, body, req.Name, req.ConnectMs, req.ReadMs);
        }

        static void Run(Job job, string host, int port, byte[] body, string name, int connectMs, int readMs)
        {
            var sw = Stopwatch.StartNew();
            Socket s = null;
            try
            {
                s = NetUtil.Connect(host, port, connectMs, out string connectError);
                if (s == null) { Done(job, Status.Unreachable, UnreachableText(host, port), "connect: " + connectError); return; }
                lock (job)
                {
                    if (job.Cancelled) { s.Close(); return; }
                    job.Socket = s;
                }
                s.SendTimeout = readMs;
                string hostHeader = host.IndexOf(':') >= 0 ? "[" + host + "]" : host;
                string head = "POST /register HTTP/1.1\r\nHost: " + hostHeader + ":" + port.ToString(CultureInfo.InvariantCulture) +
                              "\r\nUser-Agent: SomniumWoW\r\nAccept: application/json\r\nContent-Type: application/json\r\nContent-Length: " +
                              body.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n";
                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                var request = new byte[headBytes.Length + body.Length];
                Buffer.BlockCopy(headBytes, 0, request, 0, headBytes.Length);
                Buffer.BlockCopy(body, 0, request, headBytes.Length, body.Length);
                int sent = 0;
                while (sent < request.Length) sent += s.Send(request, sent, request.Length - sent, SocketFlags.None);

                // ---- read the answer (the service closes after it; Content-Length lets us stop earlier), one deadline for all of it
                var got = new MemoryStream();
                var buf = new byte[4096];
                long deadline = sw.ElapsedMilliseconds + readMs;
                bool closed = false;
                while (true)
                {
                    long left = deadline - sw.ElapsedMilliseconds;
                    if (left <= 0) { Done(job, Status.Unreachable, UnreachableText(host, port), "no answer within " + (readMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s"); return; }
                    s.ReceiveTimeout = (int)Math.Max(1, left);
                    int r;
                    try { r = s.Receive(buf); }
                    catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut || e.SocketErrorCode == SocketError.WouldBlock)
                    {
                        Done(job, Status.Unreachable, UnreachableText(host, port), "no answer within " + (readMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s");
                        return;
                    }
                    if (r <= 0) { closed = true; break; }
                    got.Write(buf, 0, r);
                    if (got.Length > MaxResponseBytes) { Done(job, Status.WrongAnswer, WrongAnswerText(host, port), "answer longer than " + MaxResponseBytes + " bytes"); return; }
                    if (HttpAnswer.IsComplete(got.GetBuffer(), (int)got.Length)) break;
                }
                byte[] all = got.ToArray();
                if (all.Length == 0) { Done(job, Status.Unreachable, UnreachableText(host, port), "the connection was closed without an answer"); return; }
                Interpret(job, host, port, name, all, closed);
            }
            catch (Exception e)
            {
                if (job.Cancelled) return;
                if (e is SocketException || e is IOException || e is ObjectDisposedException)
                    Done(job, Status.Unreachable, UnreachableText(host, port), "connection lost (" + NetUtil.Short(e) + ")");
                else
                    Done(job, Status.WrongAnswer, WrongAnswerText(host, port), e.GetType().Name + " " + e.Message);
            }
            finally
            {
                try { if (s != null) s.Close(); } catch (Exception) { }
                if (!job.Done) Done(job, Status.Unreachable, UnreachableText(host, port), job.Cancelled ? "cancelled" : "no result");
            }
        }

        static void Interpret(Job job, string host, int port, string name, byte[] raw, bool closed)
        {
            string why;
            HttpAnswer a = HttpAnswer.Parse(raw, closed, out why);
            if (a == null) { Done(job, Status.WrongAnswer, WrongAnswerText(host, port), why); return; }
            job.Http = a.Code;
            Dictionary<string, object> json = MiniJson.ParseObject(a.Body);
            if (json == null) { Done(job, Status.WrongAnswer, WrongAnswerText(host, port), "the answer is not a JSON object"); return; }
            object o;
            string status = json.TryGetValue("status", out o) ? o as string : null;
            if (status == null) { Done(job, Status.WrongAnswer, WrongAnswerText(host, port), "no \"status\" in the answer"); return; }
            string answeredName = json.TryGetValue("name", out o) ? o as string : null;
            bool gm = json.TryGetValue("gm", out o) && o is bool && (bool)o;
            string serverMessage = json.TryGetValue("message", out o) ? CleanServerText(o as string) : null;

            Status st;
            if (a.Code == 201 && status == "created") st = Status.Created;
            else if (a.Code == 200 && status == "rekeyed") st = Status.Rekeyed;
            else if (a.Code == 409 && status == "exists") st = Status.Exists;
            else if (a.Code == 403 && status == "not_allowed") st = Status.NotAllowed;
            else if (a.Code == 429 && status == "slow_down") st = Status.SlowDown;
            else if (a.Code == 400 && status == "bad_request") st = Status.BadRequest;
            else if (a.Code == 500 && status == "error") st = Status.ServerError;
            else { Done(job, Status.WrongAnswer, WrongAnswerText(host, port), "unexpected answer: HTTP " + a.Code + " status '" + Clip(status, 40) + "'"); return; }

            if (answeredName != null && answeredName != name)
            {
                Done(job, Status.WrongAnswer, WrongAnswerText(host, port), "the service answered '" + status + "' for account '" + Clip(answeredName, 40) + "', expected " + name);
                return;
            }
            switch (st)
            {
                case Status.Created:
                    job.Gm = gm;
                    Done(job, st, "Your WoW account " + name + " was created" + (gm ? " (with game master rights)." : "."), null);
                    break;
                case Status.Rekeyed:
                    job.Gm = gm;
                    Done(job, st, "Your WoW account " + name + " now works from this computer.", null);
                    break;
                case Status.Exists:
                    Done(job, st, "Your Somnium account already has a WoW account, but this computer doesn't have its key. Ask the server owner to unlock it, then press Play online again.", null);
                    break;
                case Status.NotAllowed:
                    Done(job, st, "This server doesn't know your Somnium account yet - ask the server owner to add you.", null);
                    break;
                case Status.SlowDown:
                    Done(job, st, "The account service is busy - wait a few minutes, then try again.", null);
                    break;
                case Status.BadRequest:
                    Done(job, st, string.IsNullOrEmpty(serverMessage) ? "The account service did not accept the request." : serverMessage, serverMessage);
                    break;
                default:
                    Done(job, st, "The account service on " + host + " has a problem with its database - ask the server owner to look at it.", null);
                    break;
            }
        }

        static void Done(Job job, Status s, string message, string detail)
        {
            if (job.Done) return;
            job.Result = s;
            job.Message = message;
            job.Detail = detail;
            job.Ms = job.Sw.ElapsedMilliseconds;
            job.Done = true;                    // volatile write last: publishes the fields above to Poll()
        }

        static string UnreachableText(string host, int port)
        {
            return "Can't reach the account service on " + host + " (port " + port.ToString(CultureInfo.InvariantCulture) + ").";
        }

        static string WrongAnswerText(string host, int port)
        {
            return "Something answered on " + host + " (port " + port.ToString(CultureInfo.InvariantCulture) +
                   "), but it is not the WoW account service this world expects (a different program or version).";
        }

        static bool IsPlainHost(string host)
        {
            for (int i = 0; i < host.Length; i++) if (host[i] <= ' ' || host[i] > '~') return false;
            return true;
        }

        /// <summary>Trimmed, at most MaxNickChars UTF-16 units (never cutting a surrogate pair).</summary>
        static string CleanNick(string nick)
        {
            string n = (nick ?? "").Trim();
            if (n.Length > MaxNickChars)
            {
                int cut = MaxNickChars;
                if (char.IsHighSurrogate(n[cut - 1])) cut--;
                n = n.Substring(0, cut);
            }
            return n;
        }

        /// <summary>The service's "message" for the player: control characters removed, at most 200 characters.</summary>
        static string CleanServerText(string s)
        {
            if (s == null) return null;
            var sb = new StringBuilder(Math.Min(s.Length, 200));
            for (int i = 0; i < s.Length && sb.Length < 200; i++) if (!char.IsControl(s[i])) sb.Append(s[i]);
            return sb.ToString().Trim();
        }

        static string Clip(string s, int n)
        {
            s = CleanServerText(s) ?? "";
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }
    }

    /// <summary>A parsed HTTP/1.x response (status code + body; interim 1xx answers skipped, chunked bodies decoded).</summary>
    sealed class HttpAnswer
    {
        public int Code;
        public string Body;

        static int HeaderEnd(byte[] b, int start, int n)
        {
            for (int i = start; i + 3 < n; i++)
                if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
            return -1;
        }

        /// <summary>True when the bytes hold a final response whose Content-Length body is complete (else read until the server closes).</summary>
        public static bool IsComplete(byte[] b, int n)
        {
            int start = 0;
            while (true)
            {
                int end = HeaderEnd(b, start, n);
                if (end < 0) return false;
                string head = Encoding.ASCII.GetString(b, start, end - start);
                int code = StatusCode(head);
                if (code >= 100 && code < 200) { start = end + 4; continue; }
                long cl = ContentLength(head);
                if (cl < 0 || IsChunked(head)) return false;
                return n - (end + 4) >= cl;
            }
        }

        public static HttpAnswer Parse(byte[] b, bool closed, out string why)
        {
            why = null;
            int start = 0;
            while (true)
            {
                int end = HeaderEnd(b, start, b.Length);
                if (end < 0) { why = b.Length >= 5 && b[0] == 'H' && b[1] == 'T' && b[2] == 'T' && b[3] == 'P' ? "incomplete HTTP answer" : "not an HTTP answer"; return null; }
                string head = Encoding.ASCII.GetString(b, start, end - start);
                int code = StatusCode(head);
                if (code < 0) { why = "not an HTTP answer"; return null; }
                int bodyStart = end + 4;
                if (code >= 100 && code < 200) { start = bodyStart; continue; }
                byte[] body;
                if (IsChunked(head))
                {
                    body = Dechunk(b, bodyStart);
                    if (body == null) { why = "broken chunked body"; return null; }
                }
                else
                {
                    long cl = ContentLength(head);
                    int avail = b.Length - bodyStart;
                    if (cl >= 0)
                    {
                        if (avail < cl) { why = "the answer was cut off (" + avail + " of " + cl + " bytes)"; return null; }
                        avail = (int)cl;
                    }
                    else if (!closed) { why = "no Content-Length"; return null; }
                    body = new byte[avail];
                    Buffer.BlockCopy(b, bodyStart, body, 0, avail);
                }
                string text;
                try { text = new UTF8Encoding(false, true).GetString(body); }
                catch (ArgumentException) { why = "the answer is not UTF-8 text"; return null; }
                return new HttpAnswer { Code = code, Body = text };
            }
        }

        /// <summary>"HTTP/1.x NNN reason" -&gt; NNN, else -1.</summary>
        static int StatusCode(string head)
        {
            int eol = head.IndexOf("\r\n", StringComparison.Ordinal);
            string line = eol < 0 ? head : head.Substring(0, eol);
            if (!line.StartsWith("HTTP/1.", StringComparison.Ordinal) || line.Length < 12 || line[8] != ' ') return -1;
            int code = 0;
            for (int i = 9; i < 12; i++)
            {
                char c = line[i];
                if (c < '0' || c > '9') return -1;
                code = code * 10 + (c - '0');
            }
            if (line.Length > 12 && line[12] != ' ') return -1;
            return code;
        }

        static string Header(string head, string name)
        {
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0 && string.Equals(lines[i].Substring(0, c).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return lines[i].Substring(c + 1).Trim();
            }
            return null;
        }

        static long ContentLength(string head)
        {
            string v = Header(head, "Content-Length");
            long n;
            if (v != null && long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return n;
            return -1;
        }

        static bool IsChunked(string head)
        {
            string v = Header(head, "Transfer-Encoding");
            return v != null && v.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static byte[] Dechunk(byte[] b, int pos)
        {
            var o = new MemoryStream();
            while (true)
            {
                int eol = -1;
                for (int i = pos; i + 1 < b.Length; i++) if (b[i] == '\r' && b[i + 1] == '\n') { eol = i; break; }
                if (eol < 0) return null;
                string sizeText = Encoding.ASCII.GetString(b, pos, eol - pos);
                int semi = sizeText.IndexOf(';');
                if (semi >= 0) sizeText = sizeText.Substring(0, semi);
                int size;
                if (!int.TryParse(sizeText.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out size) || size < 0) return null;
                pos = eol + 2;
                if (size == 0) return o.ToArray();
                if (pos + size + 2 > b.Length) return null;
                o.Write(b, pos, size);
                pos += size;
                if (b[pos] != '\r' || b[pos + 1] != '\n') return null;
                pos += 2;
            }
        }
    }

    /// <summary>
    /// The small JSON the account service speaks: WriteString (escaped string literal) and ParseObject (a complete JSON document
    /// whose top level is an object; values: string, bool, double, null, nested objects/arrays as Dictionary / List). Null when
    /// the text is not valid JSON.
    /// </summary>
    static class MiniJson
    {
        public static void WriteString(StringBuilder sb, string v)
        {
            sb.Append('"');
            v = v ?? "";
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (char.IsHighSurrogate(c) && i + 1 < v.Length && char.IsLowSurrogate(v[i + 1])) { sb.Append(c).Append(v[i + 1]); i++; }
                        else if (char.IsSurrogate(c)) sb.Append("\\ufffd");                                  // lone surrogate
                        else if (c < ' ' || c == '\u007f' || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            if (text == null) return null;
            try
            {
                var r = new Reader { S = text };
                r.Ws();
                if (r.I >= text.Length || text[r.I] != '{') return null;
                var o = (Dictionary<string, object>)r.Value(0);
                r.Ws();
                return r.I == text.Length ? o : null;
            }
            catch (FormatException) { return null; }
            catch (IndexOutOfRangeException) { return null; }
            catch (ArgumentOutOfRangeException) { return null; }
            catch (OverflowException) { return null; }
        }

        sealed class Reader
        {
            public string S;
            public int I;

            public void Ws() { while (I < S.Length && (S[I] == ' ' || S[I] == '\t' || S[I] == '\n' || S[I] == '\r')) I++; }

            void Expect(char c) { if (I >= S.Length || S[I] != c) throw new FormatException(); I++; }

            public object Value(int depth)
            {
                if (depth > 32) throw new FormatException();
                Ws();
                if (I >= S.Length) throw new FormatException();
                char c = S[I];
                if (c == '{')
                {
                    I++;
                    var o = new Dictionary<string, object>();
                    Ws();
                    if (I < S.Length && S[I] == '}') { I++; return o; }
                    while (true)
                    {
                        Ws();
                        string key = Str();
                        Ws();
                        Expect(':');
                        o[key] = Value(depth + 1);
                        Ws();
                        if (I < S.Length && S[I] == ',') { I++; continue; }
                        Expect('}');
                        return o;
                    }
                }
                if (c == '[')
                {
                    I++;
                    var l = new List<object>();
                    Ws();
                    if (I < S.Length && S[I] == ']') { I++; return l; }
                    while (true)
                    {
                        l.Add(Value(depth + 1));
                        Ws();
                        if (I < S.Length && S[I] == ',') { I++; continue; }
                        Expect(']');
                        return l;
                    }
                }
                if (c == '"') return Str();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                throw new FormatException();
            }

            bool Word(string w)
            {
                if (string.CompareOrdinal(S, I, w, 0, w.Length) != 0) return false;
                I += w.Length;
                return true;
            }

            string Str()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (I >= S.Length) throw new FormatException();
                    char c = S[I++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw new FormatException();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (I >= S.Length) throw new FormatException();
                    char e = S[I++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (I + 4 > S.Length) throw new FormatException();
                            sb.Append((char)int.Parse(S.Substring(I, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                            I += 4;
                            break;
                        default: throw new FormatException();
                    }
                }
            }

            object Number()
            {
                int start = I;
                if (S[I] == '-') I++;
                if (I >= S.Length) throw new FormatException();
                if (S[I] == '0') I++;
                else if (S[I] >= '1' && S[I] <= '9') { while (I < S.Length && S[I] >= '0' && S[I] <= '9') I++; }
                else throw new FormatException();
                if (I < S.Length && S[I] == '.')
                {
                    I++;
                    int d = I;
                    while (I < S.Length && S[I] >= '0' && S[I] <= '9') I++;
                    if (I == d) throw new FormatException();
                }
                if (I < S.Length && (S[I] == 'e' || S[I] == 'E'))
                {
                    I++;
                    if (I < S.Length && (S[I] == '+' || S[I] == '-')) I++;
                    int d = I;
                    while (I < S.Length && S[I] >= '0' && S[I] <= '9') I++;
                    if (I == d) throw new FormatException();
                }
                return double.Parse(S.Substring(start, I - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
