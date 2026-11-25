using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Access
{
    /// <summary>
    /// The other players in the world as the Bridge shows them, for <see cref="WowIsolation"/> only (the Rec Room
    /// Dreamport's RrRemotePlayers, trimmed to what the isolation reads): identity and the body root (read only - never
    /// modified), refreshed once a second and on join.
    /// - <see cref="Remote.Id"/> = <c>Properties.PhotonId</c>, the Fusion <c>PlayerRef.PlayerId</c> WowNet's slot owners use
    ///   (-1 while the player's network object is not up yet; the isolation re-reads it live at every decision).
    /// - <see cref="Remote.Root"/> = <c>References.Body.Root</c> (the remote body's CharacterController object).
    /// - Joins arrive through <c>PlayersContainer.OnPlayerAdded</c> (the list is refreshed on the next tick); leaves are
    ///   found by the 1 Hz pass (only proven Bridge members are used; <c>OnPlayerRemoved</c> is not among them).
    /// Offline and in the editor the list is empty. Ticked by <see cref="WowIsolationDriver"/>; <see cref="Reset"/> restores
    /// everything the isolation hid. Stage S1: <see cref="WowTags"/> (the Somnium name tags) is ticked right after the isolation
    /// and shut down after it.
    /// </summary>
    public static class WowRemotes
    {
        public sealed class Remote
        {
            public int Id = -1;
            public string Nick;

            /// <summary>The Bridge nickname as given (null/empty kept): the client names the avatar root after it.</summary>
            public string RawNick;

            public Transform Root;

            /// <summary>The Bridge player behind it.</summary>
            public ISomniumPlayer Player;

            internal bool Seen;
        }

        public const float RefreshInterval = 1f;

        private static readonly List<Remote> List = new List<Remote>();
        private static ISomniumPlayersContainer _subscribed;
        private static bool _dirty;
        private static float _nextRefresh;
        private static int _joinLogs;
        private static bool _loggedError;
        private static int _tickErrors;

        public static IReadOnlyList<Remote> All
        {
            get { return List; }
        }

        public static int Count
        {
            get { return List.Count; }
        }

        /// <summary>
        /// Every frame from the driver: the list (on join and once a second), then the isolation. The list step has its own
        /// try, so a failure there can never stop the isolation (a frozen isolation would keep players hidden who should be
        /// shown, or leave shown someone who should be hidden). WowIsolation.Tick catches its own errors.
        /// </summary>
        public static void Tick()
        {
            try
            {
                TickList();
            }
            catch (Exception e)
            {
                _tickErrors++;
                if (_tickErrors <= 3)
                {
                    WowLog.Error("Remote: the player list tick threw (" + _tickErrors + (_tickErrors == 3 ? ", later ones not logged" : "") + "); the isolation still runs: " + e);
                }
            }

            WowIsolation.Tick();

            // Stage S1: the Somnium name tags of the players the isolation leaves to Gear (after it, so a player it shows this
            // frame loses the tag in the same frame). WowTags catches its own errors.
            WowTags.Tick();
        }

        private static ISomniumPlayersContainer Container()
        {
            try
            {
                return SomniumBridge.PlayersContainer;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TickList()
        {
            ISomniumPlayersContainer c = Container();
            if (!ReferenceEquals(c, _subscribed))
            {
                Unsubscribe();
                if (c != null)
                {
                    try
                    {
                        c.OnPlayerAdded += OnAdded;
                        _subscribed = c;
                    }
                    catch (Exception e)
                    {
                        Warn("subscribing to OnPlayerAdded failed: " + e.Message);
                    }
                }

                _dirty = true;
            }

            float now = Time.unscaledTime;
            if (!_dirty && now < _nextRefresh)
            {
                return;
            }

            _dirty = false;
            _nextRefresh = now + RefreshInterval;
            Refresh(c);
        }

        private static void OnAdded(ISomniumPlayer p)
        {
            _dirty = true;
        }

        private static void Refresh(ISomniumPlayersContainer c)
        {
            for (int i = 0; i < List.Count; i++)
            {
                List[i].Seen = false;
            }

            if (c != null)
            {
                List<ISomniumPlayer> players = null;
                ISomniumPlayer local = null;
                try
                {
                    players = c.AllPlayers;
                    local = c.LocalPlayer;
                }
                catch (Exception e)
                {
                    Warn("reading AllPlayers failed: " + e.Message);
                }

                if (players != null)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        ISomniumPlayer p = players[i];
                        if (p == null || ReferenceEquals(p, local))
                        {
                            continue;
                        }

                        Update(p);
                    }
                }
            }

            for (int i = List.Count - 1; i >= 0; i--)
            {
                Remote r = List[i];
                if (r.Seen)
                {
                    continue;
                }

                List.RemoveAt(i);
                Log("left: " + Describe(r));
            }
        }

        /// <summary>Reads one Bridge player into its entry.</summary>
        private static void Update(ISomniumPlayer p)
        {
            int id = -1;
            string nick = null;
            Transform root = null;
            try
            {
                ISomniumPlayerProperties props = p.Properties;
                if (props != null)
                {
                    if (props.IsLocal)
                    {
                        return;
                    }

                    id = props.PhotonId;
                    nick = props.NickName;
                }

                ISomniumPlayerReferences refs = p.References;
                ISomniumPlayerBody body = refs != null ? refs.Body : null;
                if (body != null)
                {
                    root = body.Root;
                }
            }
            catch (Exception e)
            {
                // A player object mid-change: keep its entry as it was this pass; one being torn down drops out of
                // AllPlayers by itself.
                Warn("reading a player failed: " + e.Message);
                for (int i = 0; i < List.Count; i++)
                {
                    if (ReferenceEquals(List[i].Player, p))
                    {
                        List[i].Seen = true;
                    }
                }

                return;
            }

            Remote r = null;
            for (int i = 0; i < List.Count; i++)
            {
                Remote x = List[i];
                if (ReferenceEquals(x.Player, p) || (id >= 0 && x.Id == id && !x.Seen))
                {
                    r = x;
                    break;
                }
            }

            bool isNew = r == null;
            if (isNew)
            {
                r = new Remote();
                List.Add(r);
            }

            r.Player = p;
            r.Id = id;
            r.Nick = !string.IsNullOrEmpty(nick) ? nick : "Player " + (id >= 0 ? id.ToString() : "?");
            r.RawNick = nick;
            r.Root = root != null ? root : null;
            r.Seen = true;
            if (isNew)
            {
                Log("joined: " + Describe(r));
            }
        }

        private static string Describe(Remote r)
        {
            return "'" + r.Nick + "' (id " + r.Id + ", " + (r.Root == null ? "no body yet" : "body up") + ")";
        }

        private static void Log(string text)
        {
            if (_joinLogs < 40)
            {
                _joinLogs++;
                WowLog.Info("Remote: " + text + "; " + List.Count + " remote player(s)");
            }
        }

        private static void Warn(string text)
        {
            if (!_loggedError)
            {
                _loggedError = true;
                WowLog.Warn("Remote: " + text + " (later failures not logged)");
            }
        }

        private static void Unsubscribe()
        {
            if (_subscribed == null)
            {
                return;
            }

            try
            {
                _subscribed.OnPlayerAdded -= OnAdded;
            }
            catch (Exception)
            {
                // The client's container is gone with the world.
            }

            _subscribed = null;
        }

        /// <summary>Statics survive a world re-entry. The isolation first puts back what it hid; safe to call twice.</summary>
        public static void Reset()
        {
            try
            {
                WowIsolation.Shutdown();
            }
            catch (Exception e)
            {
                WowLog.Error("Remote: isolation shutdown threw: " + e);
            }

            try
            {
                WowTags.Shutdown();   // stage S1: every hidden Somnium name tag given back
            }
            catch (Exception e)
            {
                WowLog.Error("Remote: tag shutdown threw: " + e);
            }

            Unsubscribe();
            List.Clear();
            _dirty = false;
            _nextRefresh = 0f;
            _joinLogs = 0;
            _loggedError = false;
            _tickErrors = 0;
        }
    }
}
