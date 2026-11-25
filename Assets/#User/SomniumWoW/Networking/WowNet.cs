using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Network.Bridge;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Networking
{
    /// <summary>
    /// Everyone's weapons over the world's Photon Fusion session, the Euro Truck Sim Dreamport's way (EtsNet): the
    /// Community Modules' shared-mode session every player in the instance is already in, networked objects placed in the
    /// scene by hand ("Wow Net Player 0..49", a NetworkObject + NetworkBridgeData64 each, registered by this project's
    /// SceneNetworking copy), because none can be made at run time and no behaviour of the world's own can be networked.
    ///
    /// Each client claims one free slot (WowSlotClaim: RequestStateAuthority, then the claim record), writes its own gear
    /// record there (WowNetWire) and nothing anywhere else, and reads everyone else's. Leaving sends nothing: the slot's
    /// owner is no longer in the room, every client stops drawing it, and whoever holds its authority (SceneNetworking gives
    /// orphans to the master) writes it free. A player is joined to their Somnium avatar through the Bridge: the slot's
    /// owner PlayerId == ISomniumPlayer.Properties.PhotonId (EtsNet.NameOf's rule).
    ///
    /// Alone, in the editor, before the room is joined or in a scene without the slot objects, it does nothing.
    /// Uses only Fusion/Somnium members the upload server has compiled for the sibling worlds (fusionapi/check.sh).
    ///
    /// Stage S1 (PLAN10 s0.7) session diagnostics - two players who cannot see each other were most likely in two Somnium
    /// instances: at "room joined" the Fusion session (a fingerprint of its name - the client logs the name itself encrypted -,
    /// its region and player count, read by reflection because NetworkRunner.SessionInfo is not a proven member: no compiled
    /// reference, a missing property only says "not readable"), the player ids in it (Runner.ActivePlayers) and how many other
    /// players the Bridge lists; every join / leave with the new player list; and <see cref="WowNetAlone"/>'s one-time hint when
    /// this client is alone in the session for 60 s while the whitelist names others (<see cref="Alone"/>, shown by
    /// Game/Vr/WowWrist).
    /// </summary>
    public sealed class WowNet
    {
        public const int MaxSlots = 50;

        /// <summary>At most this many own-slot writes a second (Fusion itself sends at its own rate).</summary>
        private const float WriteEvery = 0.1f;
        /// <summary>Iteration 7: while our record carries a stage-B ride pose (WowGearState.Ride.PoseSaid = a ridden mount owns the
        /// avatar) the interval drops to 1/30 s, ETS's figure (ours), so the stamped samples are fresh when Fusion sends (16 Hz).</summary>
        private const float RideWriteEvery = 1f / 30f;
        private const float PresentEvery = 1f;
        private const float ServeEvery = 0.25f;

        /// <summary>Another player whose slot counts: in the room and playing.</summary>
        public sealed class Remote
        {
            public int Slot;
            public int PlayerId;
            public ISomniumPlayer Player;   // null until the Bridge lists them
            public string Name;             // Somnium nickname, or "Player N"
            public WowGearState Gear;
        }

        private readonly NetworkBridgeData[] _slotObjects = new NetworkBridgeData[MaxSlots];
        private readonly int[][] _slots = new int[MaxSlots][];
        private readonly bool[] _valid = new bool[MaxSlots];
        private readonly bool[] _authority = new bool[MaxSlots];
        private readonly bool[] _counts = new bool[MaxSlots];
        private readonly Remote[] _remoteBySlot = new Remote[MaxSlots];
        private readonly List<Remote> _remotes = new List<Remote>(MaxSlots);
        private readonly List<int> _present = new List<int>(64);
        private readonly WowSlotClaim _claim = new WowSlotClaim();
        private float _noneValidSince = -1f;
        private bool _noneValidWarned;
        private readonly int[] _own = new int[WowNetWire.Ints];
        private readonly long[] _loggedAs = new long[MaxSlots];
        private readonly string[] _loggedName = new string[MaxSlots];

        private int _slotCount;
        private bool _slotsComplete;
        private int _slotsLogged = -1;
        private float _nextSlotFind;
        private float _presentAt;
        private float _nextServe;
        private float _nextWrite;
        private float _nextPlayerWalk;
        private bool _forceWrite;
        private bool _dirty;
        private bool _rideRate;
        private bool _subscribed;
        private bool _wasOnline;
        private int _writes, _reads;
        private readonly float _t0 = Time.realtimeSinceStartup;

        // Stage S1 (PLAN10 s0.7): session diagnostics.
        /// <summary>Ours: join / leave lines at most per visit.</summary>
        private const int MaxRoomLogs = 60;
        /// <summary>Kill switch of the reflection read of NetworkRunner.SessionInfo (static readonly: no "unreachable code").</summary>
        private static readonly bool ReadSessionInfo = true;
        private readonly WowNetAlone _alone = new WowNetAlone();
        private readonly int _othersOnList;
        private bool _presentRead;
        private int _roomLogs;
        private string _sessionTag = "#none";

        /// <summary>This visit's alone rule (Game/Vr/WowWrist shows its hint once it is due); null before the first WowNet.</summary>
        public static WowNetAlone Alone { get; private set; }

        public WowNet()
        {
            for (int k = 0; k < MaxSlots; k++) _slots[k] = new int[WowNetWire.Ints];
            Alone = _alone;   // statics survive a world re-entry: each visit starts a fresh rule
            _othersOnList = WowNetAlone.OthersOnList(WowAccess.Whitelist, WowAccess.Nick, WowAccess.UserId);
        }

        private static NetworkRunner Runner { get { return SceneNetworking.NetworkRunnerRef; } }

        /// <summary>In a room: SceneNetworking has joined and has a runner.</summary>
        public bool Online { get { return SceneNetworking.IsNetworkReady && Runner != null; } }

        public int LocalId { get { return Runner != null ? Runner.LocalPlayer.PlayerId : -1; } }

        /// <summary>This client's slot, -1 until claimed.</summary>
        public int MySlot { get { return _claim.Holding ? _claim.Slot : -1; } }

        /// <summary>The other players whose slots count this frame (in the room and playing).</summary>
        public IReadOnlyList<Remote> Remotes { get { return _remotes; } }

        public int SlotCount { get { return _slotCount; } }

        private static bool Valid(NetworkBehaviour behaviour)
        {
            return behaviour != null && behaviour.Object != null && behaviour.Object.IsValid;
        }

        private int StampMs() { return (int)((Time.realtimeSinceStartup - _t0) * 1000f); }

        /// <summary>Once per frame (WowGear.Update): read every slot, step the claim, keep house, write our own record.</summary>
        public void Tick(in WowGearState own)
        {
            bool online = Online;
            if (online && !_subscribed)
            {
                SceneNetworking.OnOtherPlayerJoined += OnRoomPlayerJoined;
                SceneNetworking.OnOtherPlayerLeft += OnRoomPlayerLeft;
                _subscribed = true;
            }
            if (online != _wasOnline)
            {
                _wasOnline = online;
                if (online)
                {
                    // Stage S1: which session this is, who is in it (the lead could not tell two instances apart from the O2 log).
                    string session = SessionLine();
                    WowLog.Info("Net: room joined (player id " + LocalId + ", master " + SceneNetworking.IsMasterClient + ") - " + session + "; " + PlayersLine()
                                + "; the Bridge lists " + BridgeOthers() + " other Somnium player(s); the whitelist names " + (_othersOnList < 0 ? "nobody (open to everyone)" : _othersOnList + " other player(s)"));
                }
                else WowLog.Info("Net: offline (no room: weapons are shown locally only)");
            }

            if (online) FindSlots();
            RefreshPresent(online);
            int localId = LocalId;
            StepAlone(online, localId);

            for (int k = 0; k < _slotCount; k++)
            {
                NetworkBridgeData data = _slotObjects[k];
                int[] slot = _slots[k];
                bool valid = online && Valid(data);
                _valid[k] = valid;
                _authority[k] = valid && data.Object.HasStateAuthority;
                _counts[k] = false;
                if (!valid)
                {
                    if (slot[WowNetWire.AtMagic] != 0) Array.Clear(slot, 0, slot.Length);
                    continue;
                }
                NetworkArray<int> ints = data.IntArray;
                int n = Math.Min(ints.Length, WowNetWire.Used);
                for (int i = 0; i < n; i++)
                {
                    int v = ints.Get(i);
                    if (v != slot[i]) { slot[i] = v; _reads++; }
                }
                _counts[k] = WowSlotRules.Counts(slot, localId, _present);
            }

            // Lead, 2026-09-27: a session joined behind another master read no slot at all for 10 minutes ("no free slot" x143,
            // every slot object invalid). Say it plainly once, so the next log shows it at once.
            if (online)
            {
                bool anyValid = false;
                for (int k = 0; k < _slotCount; k++) if (_valid[k]) { anyValid = true; break; }
                if (anyValid) _noneValidSince = -1f;
                else if (_noneValidSince < 0f) _noneValidSince = Time.realtimeSinceStartup;
                else if (!_noneValidWarned && _slotCount > 0 && Time.realtimeSinceStartup - _noneValidSince > 20f)
                {
                    _noneValidWarned = true;
                    WowLog.Warn("Net: none of the " + _slotCount + " player slots is attached after 20 s (local player " + localId + ", master " + SceneNetworking.IsMasterClient
                        + ") - the room's master may be on another upload of this world; weapons, nameplates and party frames will not sync until everyone re-enters the world.");
                }
            }

            StepClaim(online, localId);

            if (Time.unscaledTime >= _nextServe)
            {
                _nextServe = Time.unscaledTime + ServeEvery;
                Housekeeping(localId);
            }

            RefreshRemotes();
            WriteOwn(own, localId);
        }

        private void OnRoomPlayersChanged(PlayerRef player)
        {
            _presentAt = 0f;
            _nextPlayerWalk = 0f;
        }

        private void OnRoomPlayerJoined(PlayerRef player)
        {
            OnRoomPlayersChanged(player);
            RoomLog("player " + player.PlayerId + " joined");
        }

        private void OnRoomPlayerLeft(PlayerRef player)
        {
            OnRoomPlayersChanged(player);
            RoomLog("player " + player.PlayerId + " left");
        }

        // ------------------------------------------------------------------ stage S1: session diagnostics (PLAN10 s0.7)

        /// <summary>One join / leave line with the session's player list as Fusion reports it at that moment (capped per visit).</summary>
        private void RoomLog(string what)
        {
            try
            {
                if (_roomLogs >= MaxRoomLogs) return;
                _roomLogs++;
                if (_sessionTag == "#none") SessionLine();   // the name was not set yet at "room joined": read it again
                WowLog.Info("Net: " + what + " the Fusion session " + _sessionTag + " - " + PlayersLine() + "; the Bridge lists " + BridgeOthers() + " other Somnium player(s)"
                            + (_roomLogs == MaxRoomLogs ? " (later joins / leaves not logged)" : ""));
            }
            catch (Exception e)
            {
                WowLog.Warn("Net: logging a session join / leave failed: " + e.Message);
            }
        }

        /// <summary>"N player(s) in it (ids a, b)" from Runner.ActivePlayers (proven), this client marked.</summary>
        private string PlayersLine()
        {
            NetworkRunner runner = Runner;
            if (runner == null) return "no runner";
            int n = 0, local = LocalId;
            var ids = new System.Text.StringBuilder();
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (n > 0) ids.Append(", ");
                ids.Append(player.PlayerId);
                if (player.PlayerId == local) ids.Append(" (me)");
                n++;
            }
            return n + " player(s) in it (ids " + ids + ")";
        }

        /// <summary>Players the Bridge lists besides this one (the Somnium instance as the client sees it).</summary>
        private static int BridgeOthers()
        {
            List<ISomniumPlayer> all = null;
            try { all = SomniumBridge.PlayersContainer?.AllPlayers; }
            catch (Exception) { /* no client (the editor) */ }
            if (all == null) return 0;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                ISomniumPlayer p = all[i];
                if (p != null && p.Properties != null && !p.Properties.IsLocal) n++;
            }
            return n;
        }

        /// <summary>
        /// "Fusion session #xxxxxxxx (N characters), region R, P of M player(s)" - NetworkRunner.SessionInfo read by reflection (not a
        /// proven member; Fusion 2's SessionInfo has Name, Region, PlayerCount, MaxPlayers - the WoW project's Fusion.Runtime.dll).
        /// The name itself is never logged (the client logs it encrypted), only its fingerprint (WowNetAlone.Fingerprint).
        /// </summary>
        private string SessionLine()
        {
            if (!ReadSessionInfo) return "Fusion session not read (switched off)";
            try
            {
                object info = Prop(Runner, "SessionInfo");
                if (info == null) return "Fusion session not readable (no NetworkRunner.SessionInfo)";
                string name = Prop(info, "Name") as string;
                _sessionTag = WowNetAlone.Fingerprint(name);
                object region = Prop(info, "Region"), count = Prop(info, "PlayerCount"), max = Prop(info, "MaxPlayers");
                return "Fusion session " + _sessionTag + " (a fingerprint of its " + (name != null ? name.Length.ToString() : "?") + "-character name: the same on two players' logs = the same instance)"
                       + ", region " + (region ?? "?") + ", " + (count ?? "?") + " of " + (max ?? "?") + " player(s) by SessionInfo";
            }
            catch (Exception e)
            {
                return "Fusion session not readable (" + e.GetType().Name + ")";
            }
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            try
            {
                PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p != null ? p.GetValue(o, null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The alone rule, once a frame: in the room with its list read, other players in it, and the whitelist.</summary>
        private void StepAlone(bool online, int localId)
        {
            int others = 0;
            for (int i = 0; i < _present.Count; i++) if (_present[i] != localId) others++;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_alone.Step(now, online && _presentRead, others, _othersOnList != 0)) return;
            if (_sessionTag == "#none") SessionLine();
            if (_alone.DueCount <= 3)
                WowLog.Info("Net: alone in the Fusion session " + _sessionTag + " for " + WowNetAlone.AloneSeconds.ToString("0") + " s (only this client, player id " + localId
                            + "; the Bridge lists " + BridgeOthers() + " other Somnium player(s); the whitelist names " + (_othersOnList < 0 ? "nobody (open to everyone)" : _othersOnList + " other player(s)")
                            + ") - a friend playing now is most likely in another Somnium instance; the one-time hint shows in the WoW world");
        }

        /// <summary>"Wow Net Player 0..49" by name (as EtsNet.FindSlots), once a second until all are found, then every 5 s.</summary>
        private void FindSlots()
        {
            if (_slotsComplete || Time.unscaledTime < _nextSlotFind) return;
            _nextSlotFind = Time.unscaledTime + 1f;
            int found = 0, highest = -1;
            foreach (NetworkBridgeData data in UnityEngine.Object.FindObjectsByType<NetworkBridgeData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string name = data.name;
                int k;
                if (!name.StartsWith(WowNetWire.ObjectPrefix, StringComparison.Ordinal) || !int.TryParse(name.Substring(WowNetWire.ObjectPrefix.Length), out k) || k < 0 || k >= MaxSlots)
                    continue;
                if (data.IntCount < WowNetWire.Used)
                {
                    Debug.LogWarning("[SomniumWoW] Net: '" + name + "' holds " + data.IntCount + " ints, a player slot needs " + WowNetWire.Used + "; use a NetworkBridgeData64.");
                    continue;
                }
                _slotObjects[k] = data;
                found++;
                highest = Math.Max(highest, k);
            }
            int count = 0;
            while (count < MaxSlots && _slotObjects[count] != null) count++;
            _slotCount = count;
            _slotsComplete = count == MaxSlots;
            if (found != _slotsLogged)
            {
                _slotsLogged = found;
                WowLog.Info("Net: " + found + " player slot object(s) found ('" + WowNetWire.ObjectPrefix + "0.." + highest + "'), " + count + " usable" +
                            (_slotsComplete ? "." : "; still looking every 5 s (the scene needs the 'Wow Net' objects from reports2/gear_scene.yaml)."));
            }
            if (!_slotsComplete) _nextSlotFind = Time.unscaledTime + 5f;
        }

        private void RefreshPresent(bool online)
        {
            if (!online)
            {
                if (_present.Count > 0) _present.Clear();
                _presentRead = false;
                return;
            }
            if (Time.unscaledTime < _presentAt) return;
            _presentAt = Time.unscaledTime + PresentEvery;
            _present.Clear();
            foreach (PlayerRef player in Runner.ActivePlayers) _present.Add(player.PlayerId);
            _presentRead = true;
        }

        private void StepClaim(bool online, int localId)
        {
            var input = new WowSlotClaim.Inputs { Now = Time.realtimeSinceStartupAsDouble, LocalId = localId, Allowed = online && _slotCount > 0, SlotCount = _slotCount };
            _claim.Step(input, _slots, _valid, _authority, _present);
            if (_claim.AskAuthority >= 0)
            {
                NetworkBridgeData data = _slotObjects[_claim.AskAuthority];
                if (Valid(data)) data.Object.RequestStateAuthority();
            }
            if (_claim.WriteClaim && _claim.Slot >= 0)
            {
                WowNetWire.WriteClaim(_own, localId, _claim.Generation, StampMs());
                _forceWrite = true;
            }
            string note = _claim.TakeNote();
            if (note != null) WowLog.Info("Net: " + note + ".");
        }

        private void Housekeeping(int localId)
        {
            for (int k = 0; k < _slotCount; k++)
            {
                if (!WowSlotRules.ToFree(_slots[k], _authority[k], k == _claim.Slot, localId, _present)) continue;
                int gone = WowNetWire.OwnerOf(_slots[k]);
                WowNetWire.WriteFree(_slots[k], StampMs());
                Push(_slotObjects[k], _slots[k]);
                WowLog.Info("Net: slot " + k + " written free (" + (gone == localId ? "this client's own record, left behind by a lost claim" : "player " + gone + " left") + ").");
            }
        }

        /// <summary>Sets the ints that differ (Fusion sends a changed int only); nothing without the object's authority.</summary>
        private void Push(NetworkBridgeData data, int[] record)
        {
            if (!Valid(data) || !data.Object.HasStateAuthority) return;
            NetworkArray<int> ints = data.IntArray;
            int n = Math.Min(ints.Length, record.Length);
            for (int i = 0; i < n; i++)
            {
                if (ints.Get(i) != record[i]) { ints.Set(i, record[i]); _writes++; }
            }
        }

        private void WriteOwn(in WowGearState own, int localId)
        {
            int k = MySlot;
            if (k < 0 || k >= _slotCount || !_authority[k]) return;
            if (WowNetWire.OwnerOf(_own) != localId) return; // the claim record is written first (StepClaim)
            // Stage O2 (Sync) fix: PackGear writes into _own at once, so a change packed while the write interval was still running
            // compared equal on every later frame and was never pushed until something else changed (a motion counter, a cast start
            // or a release right after another write was lost that way). _dirty keeps it until it is pushed.
            if (WowNetWire.PackGear(own, _own)) _dirty = true;
            if (!_dirty && !_forceWrite) return;
            if (!_forceWrite && Time.unscaledTime < _nextWrite) return;
            _dirty = false;
            bool riding = own.Ride.PoseSaid;
            if (riding != _rideRate)
            {
                _rideRate = riding;
                WowLog.Info("Net: own record written at most every " + (riding ? Mathf.RoundToInt(RideWriteEvery * 1000f) + " ms (riding: ints 17-27 carry the stamped ride pose)" : Mathf.RoundToInt(WriteEvery * 1000f) + " ms"));
            }
            _nextWrite = Time.unscaledTime + (riding ? RideWriteEvery : WriteEvery);
            _forceWrite = false;
            _own[WowNetWire.AtSeq]++;
            _own[WowNetWire.AtStamp] = StampMs();
            Push(_slotObjects[k], _own);
        }

        /// <summary>The counting slots as Remotes, each joined to its Bridge player (one walk of AllPlayers at most once a second while one is missing, else every 5 s).</summary>
        private void RefreshRemotes()
        {
            _remotes.Clear();
            bool missing = false;
            for (int k = 0; k < _slotCount; k++)
            {
                if (!_counts[k])
                {
                    if (_remoteBySlot[k] != null)
                    {
                        WowLog.Info("Net: slot " + k + " (" + _remoteBySlot[k].Name + ") no longer drawn");
                        _remoteBySlot[k] = null;
                        _loggedAs[k] = 0;
                        _loggedName[k] = null;
                    }
                    continue;
                }
                Remote r = _remoteBySlot[k];
                int owner = WowNetWire.OwnerOf(_slots[k]);
                if (r == null || r.PlayerId != owner)
                {
                    r = new Remote { Slot = k, PlayerId = owner, Name = "Player " + owner };
                    _remoteBySlot[k] = r;
                    missing = true;
                }
                WowNetWire.UnpackGear(_slots[k], out r.Gear);
                if (r.Player == null) missing = true;
                _remotes.Add(r);
                long said = ((long)r.Gear.Level << 56) ^ ((long)r.Gear.Class << 48) ^ ((long)r.Gear.Main.DisplayId << 28) ^ ((long)r.Gear.Off.DisplayId << 8) ^ r.Gear.Ranged.DisplayId ^ ((long)(r.Gear.Xr ? 1 : 0) << 47);
                if (_loggedAs[k] != said || _loggedName[k] != r.Name)
                {
                    _loggedAs[k] = said;
                    _loggedName[k] = r.Name;
                    WowLog.Info("Net: slot " + k + " = player " + owner + " '" + r.Name + "': level " + r.Gear.Level + " class " + r.Gear.Class + ", main " + r.Gear.Main.DisplayId + "/" + r.Gear.Main.SheatheType +
                                ", off " + r.Gear.Off.DisplayId + "/" + r.Gear.Off.SheatheType + ", ranged " + r.Gear.Ranged.DisplayId + (r.Gear.Xr ? ", VR" : ", desktop") + (r.Player == null ? " (avatar not found yet)" : ""));
                }
            }

            float now = Time.unscaledTime;
            if (now < _nextPlayerWalk || (!missing && now < _nextPlayerWalk + 4f)) return;
            _nextPlayerWalk = now + 1f;
            List<ISomniumPlayer> all = null;
            try { all = SomniumBridge.PlayersContainer?.AllPlayers; }
            catch (Exception) { /* no client (the editor) */ }
            for (int i = 0; i < _remotes.Count; i++)
            {
                Remote r = _remotes[i];
                ISomniumPlayer found = null;
                if (all != null)
                {
                    for (int j = 0; j < all.Count; j++)
                    {
                        ISomniumPlayer p = all[j];
                        if (p?.Properties != null && !p.Properties.IsLocal && p.Properties.PhotonId == r.PlayerId) { found = p; break; }
                    }
                }
                r.Player = found;
                string nick = found?.Properties?.NickName;
                if (!string.IsNullOrEmpty(nick)) r.Name = nick;
            }
        }

        /// <summary>
        /// Access isolation (Game/Access/WowIsolation, whitelisted rule): true while that player (Fusion PlayerId = Bridge
        /// PhotonId) holds a written slot here - claimed, in the room (Listed) and not free: in the room OR playing, so it
        /// is a superset of <see cref="Remotes"/>. Read from this frame's copies (Tick). A client that never started the
        /// Dreamport (a visitor: no WowGear, no WowNet) never has one.
        /// </summary>
        public bool HasRecord(int playerId)
        {
            if (playerId < 0) return false;
            int localId = LocalId;
            for (int k = 0; k < _slotCount; k++)
            {
                int[] slot = _slots[k];
                if (_valid[k] && WowNetWire.OwnerOf(slot) == playerId && WowNetWire.StatusOf(slot) != WowNetStatus.Free && WowSlotRules.Listed(slot, localId, _present)) return true;
            }
            return false;
        }

        /// <summary>For the log: slots, the own slot, ints read/written since the last call.</summary>
        public string StatsLine()
        {
            string s = "Net: " + _slotCount + " slots, mine " + MySlot + ", " + _remotes.Count + " other player(s) drawn, " + _reads + " ints read, " + _writes + " written";
            _reads = 0;
            _writes = 0;
            return s;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                SceneNetworking.OnOtherPlayerJoined -= OnRoomPlayerJoined;
                SceneNetworking.OnOtherPlayerLeft -= OnRoomPlayerLeft;
                _subscribed = false;
            }
        }
    }
}
