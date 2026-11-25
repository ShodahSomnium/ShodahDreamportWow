using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Stage S1 (owner party, PLAN10 s1.2 / s1.4): the Somnium profile picture of a Somnium player in this world, for the party frames.
    /// The ONLY class of this world that uses UnityEngine.Networking for pictures.
    ///
    /// The address is the Somnium client's own rule (client source, read only):
    /// - UI/Previews/Scripts/PreviewsContainer.cs:18 <c>USER_PROFILE_PREVIEW_KEYWORD = "userimage/{0}/userprofileimage.png"</c>;
    /// - SocialSystem/User/Scripts/UserPreviewView.cs:84 <c>ArgumentsContainer.IdentityAPI?.GetBaseURL(PreviewsContainer.USER_PROFILE_PREVIEW_KEYWORD, userid)</c>;
    /// - UriObject.GetBaseURL (SomniumSpace.Common.dll): <c>Base + endOfURL</c>, then <c>string.Format(text, args)</c> (Relative is not used);
    /// - the release API config, Assets/Resources/LaunchDevConfig.asset <c>API: ReleaseConfig: IdentityUri: Base: https://somniumspace.org/</c>
    ///   (copied from the server's app config: Editor/EditorConfig/DevEditorHelper.cs:417 <c>argsConfig.API.ReleaseConfig = appConfig.API.Clone()</c>).
    ///   The launcher passes the API at run time and a world cannot read it, so the address is fixed here (<see cref="IdentityBase"/>).
    /// - the user id is the one the client's own name tag uses: Player/Scripts/Player/Features/InfoView/PlayerTagView.cs:156
    ///   <c>_preview.LoadPreview(playerManager?.Properties?.UserId)</c>, which the Bridge exposes as ISomniumPlayerProperties.Id
    ///   (Bridge/Scripts/Components/Player/BridgePlayerProperties.cs <c>public string Id =&gt; _playerProperties?.UserId;</c>).
    /// - a plain GET without headers, as PreviewsContainer.DownloadAsync does.
    ///
    /// UnityWebRequestTexture is NOT in the upload server's reference set (UnityEngine.UnityWebRequestTextureModule is missing from
    /// P/build/runtime_server.head), so the picture is fetched with UnityWebRequest.Get + DownloadHandlerBuffer (UnityWebRequestModule)
    /// and decoded with ImageConversion.LoadImage (ImageConversionModule) - both in the set. The texture is made here in memory (never
    /// written to disk; UnityWebRequest keeps no cache), in Unity's usual orientation (no uv flip, unlike the game's BLPs).
    /// Privacy (PLAN10 s1.2): only for whitelisted Somnium players (WowAccess.Matches on the nickname or the id) - WowParty asks only for
    /// players with a WoW network record, which visitors never have; one timeout, a single retry, one log line per failure.
    /// </summary>
    public static class UiPortraits
    {
        public const string IdentityBase = "https://somniumspace.org/";                     // the release IdentityUri Base (see above)
        public const string ProfileKeyword = "userimage/{0}/userprofileimage.png";         // PreviewsContainer.USER_PROFILE_PREVIEW_KEYWORD
        public const int TimeoutSeconds = 15;       // ours (the client's HttpClient uses 30 s: PreviewsContainer.cs:57)
        public const float RetryAfter = 10f;        // ours: seconds before the single retry
        public const int MaxConcurrent = 2;         // ours
        public const int MaxBytes = 4 * 1024 * 1024;   // ours: a bigger answer is not a profile picture

        private enum State { Waiting, Loading, Done, Failed, Denied }

        private sealed class Entry
        {
            public string Id, Nick;
            public State State;
            public UnityWebRequest Request;
            public Texture2D Texture;
            public int Tries;
            public float NextAt;
            public float StartedAt;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Entry> Work = new List<Entry>();
        private static int _loading, _loaded, _failed;

        /// <summary>The picture of this Somnium user (null until it is loaded, or when there is none). The first call queues the load
        /// for a whitelisted player; the rest is <see cref="Pump"/>'s.</summary>
        public static Texture2D Get(string userId, string nick)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            Entry e;
            if (Cache.TryGetValue(userId, out e))
            {
                if (e.State == State.Denied && !string.IsNullOrEmpty(nick) && nick != e.Nick && Allowed(userId, nick))
                {
                    e.Nick = nick;   // the nickname arrived after a "Player N" placeholder
                    e.State = State.Waiting;
                    e.NextAt = 0f;
                }
                return e.Texture;
            }
            e = new Entry { Id = userId, Nick = nick };
            Cache[userId] = e;
            if (!Allowed(userId, nick))
            {
                e.State = State.Denied;
                WowLog.Info("Portraits: " + (nick ?? "?") + " is not on the world's whitelist - no picture is fetched.");
                return null;
            }
            e.State = State.Waiting;
            return null;
        }

        /// <summary>Whitelisted (PLAN10 s1.2): the local player, or a nickname / id on WowAccess.Whitelist.</summary>
        private static bool Allowed(string userId, string nick)
        {
            if (!string.IsNullOrEmpty(WowAccess.UserId) && string.Equals(WowAccess.UserId, userId, StringComparison.OrdinalIgnoreCase)) return true;
            return WowAccess.Matches(nick, userId);
        }

        public static string UrlOf(string userId)
        {
            return IdentityBase + string.Format(ProfileKeyword, Uri.EscapeDataString(userId));
        }

        /// <summary>Main thread, every frame (UiGroup.Update): starts waiting loads (at most <see cref="MaxConcurrent"/>), finishes done ones.</summary>
        public static void Pump()
        {
            if (Cache.Count == 0) return;
            float now = Time.unscaledTime;
            Work.Clear();
            foreach (Entry e in Cache.Values) if (e.State == State.Waiting || e.State == State.Loading) Work.Add(e);
            for (int i = 0; i < Work.Count; i++)
            {
                Entry e = Work[i];
                if (e.State == State.Loading) Finish(e, now);
                else if (_loading < MaxConcurrent && now >= e.NextAt) Start(e, now);
            }
        }

        private static void Start(Entry e, float now)
        {
            string url = UrlOf(e.Id);
            try
            {
                var r = UnityWebRequest.Get(url);
                r.timeout = TimeoutSeconds;
                r.SendWebRequest();
                e.Request = r;
                e.State = State.Loading;
                e.StartedAt = now;
                e.Tries++;
                _loading++;
                if (e.Tries == 1) WowLog.Info("Portraits: loading " + (e.Nick ?? "?") + "'s Somnium profile picture (" + url + ").");
            }
            catch (Exception ex)
            {
                e.Request = null;
                Failed(e, now, "request", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Finish(Entry e, float now)
        {
            UnityWebRequest r = e.Request;
            if (r == null) { e.State = State.Waiting; return; }
            if (!r.isDone) return;
            _loading--;
            e.Request = null;
            try
            {
                if (r.result != UnityWebRequest.Result.Success)
                {
                    string kind = r.result == UnityWebRequest.Result.ProtocolError ? "HTTP " + r.responseCode
                                : r.result == UnityWebRequest.Result.ConnectionError ? "connection" : "data";
                    Failed(e, now, kind, r.error);
                    return;
                }
                byte[] data = r.downloadHandler != null ? r.downloadHandler.data : null;
                if (data == null || data.Length == 0 || data.Length > MaxBytes)
                {
                    Failed(e, now, "size", (data == null ? 0 : data.Length) + " bytes");
                    return;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, data, false))   // kept readable: the HUD cuts it round (UiRoundPortrait reads its pixels)
                {
                    UnityEngine.Object.Destroy(tex);
                    Failed(e, now, "decode", data.Length + " bytes are not a PNG / JPG");
                    return;
                }
                tex.name = "Somnium portrait " + (e.Nick ?? e.Id);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                e.Texture = tex;
                e.State = State.Done;
                _loaded++;
                WowLog.Info("Portraits: " + (e.Nick ?? "?") + "'s profile picture loaded (" + tex.width + " x " + tex.height + ", " + data.Length + " bytes, " +
                            (now - e.StartedAt).ToString("0.0") + " s, try " + e.Tries + "; kept in memory only).");
            }
            catch (Exception ex)
            {
                Failed(e, now, "exception", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                r.Dispose();
            }
        }

        private static void Failed(Entry e, float now, string kind, string detail)
        {
            bool retry = e.Tries < 2;
            e.State = retry ? State.Waiting : State.Failed;
            e.NextAt = now + RetryAfter;
            if (!retry) _failed++;
            WowLog.Warn("Portraits: " + (e.Nick ?? "?") + "'s profile picture failed (" + kind + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + ")" +
                        (retry ? " - one retry in " + RetryAfter.ToString("0") + " s." : " - no picture (the class icon stays)."));
        }

        /// <summary>One line for the log: how many pictures are loaded / failed.</summary>
        public static string Stats
        {
            get { return "portraits " + _loaded + " loaded, " + _failed + " failed, " + _loading + " loading, " + Cache.Count + " known"; }
        }

        /// <summary>World left: every request aborted, every picture destroyed (UiGroup.OnDestroy).</summary>
        public static void Clear()
        {
            foreach (Entry e in Cache.Values)
            {
                if (e.Request != null) { try { e.Request.Abort(); e.Request.Dispose(); } catch (Exception) { } e.Request = null; }
                if (e.Texture != null) { UnityEngine.Object.Destroy(e.Texture); e.Texture = null; }
            }
            Cache.Clear();
            Work.Clear();
            _loading = 0;
        }
    }
}
