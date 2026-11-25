using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>One party member for the HUD: another Somnium player in this world (their network record) and/or a member of the
    /// server group (online).</summary>
    public struct WowPartyMember
    {
        public int PlayerId;              // Fusion/Photon player id of a Somnium player, -1 for a server group member who is not one
        public string Name;               // Somnium nickname (record) / the WoW character name (server group member)
        public int Level;
        public WowClass Class;
        public int Race;
        public float HealthPct;           // 0..1
        public int MapId;                 // -1 = in the room / not in the world / offline / unknown
        public bool Vr;
        public float Distance;            // metres from the local player (after origin re-mapping), float.MaxValue when unknown

        // ---- Stage S1 (owner party, PLAN10 s2) - appended
        public ulong Guid;                // the WoW character guid (target it with IWowSession.SetTarget), 0 = unknown / offline game
        public string SomniumUserId;      // the Somnium user id (Bridge ISomniumPlayerProperties.Id) of a Somnium player, null otherwise
        public string SomniumName;        // the Somnium nickname of a Somnium player (Name is the character's name in a server group)
        public bool IsSomnium;            // a Somnium player in this world (their avatar stands here)
        public bool InGroup;              // a member of our server group (online)
        public bool Leader;               // the server group's leader
        public bool Online;               // connected (a server group member can be offline; Somnium records are always true)
        public bool Dead, Ghost;          // Dead = dead or a released spirit; Ghost = released spirit (server group members)
        public PowerType Power;           // PowerType.None when unknown (Somnium records carry no power)
        public float PowerPct;            // 0..1, 0 when unknown
        public int ZoneId;                // AreaTable id the server gave, 0 = unknown
        public Texture2D Portrait;        // the Somnium profile picture (UiPortraits), null until loaded / not a Somnium player
        internal bool Merged;             // ours: written by WowParty.Merge (tells a frame without WowGear's refill apart)
    }

    /// <summary>
    /// CONTRACT (iteration 4, extended in stage S1). The party members the HUD shows. The Gear owner (who reads every player's network
    /// record) refills Members every frame in its LateUpdate (order 10000) with the other Somnium players; stage S1: <see cref="Merge"/>
    /// (UiGroup's LateUpdate, order 10010 - every reader in Update or at a later order sees the merged list) turns it into:
    /// - online in a server group (PlayerState.Group not empty): the group's members in the server's order (InGroup, Leader, health /
    ///   power / level / zone / online / dead / ghost from the server), each Somnium player's character marked (IsSomnium, PlayerId,
    ///   SomniumUserId, their avatar's Distance and Vr from the record) - WoW's rule: the party frames are your group;
    /// - otherwise (offline, or online without a group): empty (lead 2026-09-27: WoW's rule - you invite people into your party).
    /// Portraits come from UiPortraits (whitelisted Somnium players only). Visitors never run the WoW game, so this stays empty for them.
    /// </summary>
    public static class WowParty
    {
        public static readonly List<WowPartyMember> Members = new List<WowPartyMember>();

        /// <summary>True when Members is the server group this frame (else the Somnium players, as before stage S1).</summary>
        public static bool ShowsGroup { get; private set; }

        private const float YardsToMetres = 0.9144f;           // WowSpace: 1 yard = 0.9144 m
        private const float UserIdRefresh = 2f;                 // ours: seconds between Bridge walks for the players' user ids

        private static readonly List<WowPartyMember> Records = new List<WowPartyMember>();   // the Somnium records as WowGear left them
        private static readonly List<WowPartyMember> Out = new List<WowPartyMember>();
        private static readonly Dictionary<int, string> UserIds = new Dictionary<int, string>();   // PhotonId -> user id
        private static float _nextUserIds = -1f;
        private static int _mergedFrame = -1;
        private static string _saidMode;
        private static readonly HashSet<ulong> PairedSaid = new HashSet<ulong>();

        /// <summary>
        /// Once per frame after WowGear.LateUpdate (UiGroup calls it). Reads WowGear's fresh records (entries without Merged) or keeps
        /// the last ones when Gear did not refill this frame, then writes the merged list back into <see cref="Members"/>.
        /// </summary>
        public static void Merge(IWowSession s)
        {
            if (_mergedFrame == Time.frameCount) return;
            _mergedFrame = Time.frameCount;
            List<WowPartyMember> m = Members;
            bool fresh = m.Count == 0;
            for (int i = 0; i < m.Count && !fresh; i++) if (!m[i].Merged) fresh = true;
            if (fresh)
            {
                Records.Clear();
                for (int i = 0; i < m.Count; i++) if (!m[i].Merged) Records.Add(m[i]);
            }
            RefreshUserIds();
            for (int i = 0; i < Records.Count; i++)
            {
                WowPartyMember r = Records[i];
                r.IsSomnium = true;
                r.Online = true;
                string id;
                if (string.IsNullOrEmpty(r.SomniumUserId) && UserIds.TryGetValue(r.PlayerId, out id)) r.SomniumUserId = id;
                if (string.IsNullOrEmpty(r.SomniumName)) r.SomniumName = r.Name;
                Records[i] = r;
            }

            Out.Clear();
            PlayerState p = s != null ? s.Player : null;
            List<GroupMemberView> group = p != null ? p.Group : null;
            bool showGroup = group != null && group.Count > 0;
            if (showGroup)
            {
                for (int i = 0; i < group.Count; i++) Out.Add(FromGroup(s, p, group[i]));
                PairByElimination(group);
            }
            // Lead (2026-09-27, the user: "No one should be in my party straight away, I should invite people"): no group = no party
            // frames, as in WoW (the other Somnium players were listed here before). Their avatars and name tags are unchanged.
            for (int i = 0; i < Out.Count; i++)
            {
                WowPartyMember e = Out[i];
                e.Portrait = e.IsSomnium && !string.IsNullOrEmpty(e.SomniumUserId) ? UiPortraits.Get(e.SomniumUserId, e.SomniumName) : null;
                e.Merged = true;
                Out[i] = e;
            }
            m.Clear();
            m.AddRange(Out);
            ShowsGroup = showGroup;

            string mode = showGroup ? "the server group (" + group.Count + " other member(s))" : "nobody (not in a group; " + Records.Count + " other Somnium player(s) here)";
            if (mode != _saidMode)
            {
                _saidMode = mode;
                WowLog.Info("Party: the party frames show " + mode + ".");
            }
        }

        /// <summary>A server group member as a party member; a Somnium player's character takes the record's avatar data.</summary>
        private static WowPartyMember FromGroup(IWowSession s, PlayerState p, GroupMemberView v)
        {
            var e = new WowPartyMember
            {
                PlayerId = -1,
                Name = v.Name,
                Level = v.Level,
                Class = v.Class,
                Race = v.Race,
                HealthPct = v.MaxHealth > 0 ? Mathf.Clamp01((float)v.Health / v.MaxHealth) : (v.Online ? 1f : 0f),
                MapId = v.Online ? v.MapId : -1,
                Distance = float.MaxValue,
                Guid = v.Guid,
                InGroup = true,
                Leader = v.Leader,
                Online = v.Online,
                Dead = v.Dead,
                Ghost = v.Ghost,
                Power = v.Power,
                PowerPct = v.PowerMax > 0 ? Mathf.Clamp01((float)v.PowerValue / v.PowerMax) : 0f,
                ZoneId = v.ZoneId,
            };
            int ri = RecordOf(v.Guid);
            bool somnium = v.IsSomniumPlayer || ri >= 0 || SafeIsSomniumCharacter(v.Guid, v.Name);
            if (ri >= 0)
            {
                WowPartyMember r = Records[ri];
                e.PlayerId = r.PlayerId;
                e.SomniumUserId = r.SomniumUserId;
                e.SomniumName = r.SomniumName;
                e.Vr = r.Vr;
                e.Distance = r.Distance;
            }
            e.IsSomnium = somnium;
            // The session leaves the out-of-range marking to the Somnium layer (GroupMemberView.IsSomniumPlayer "WowGear marks it").
            if (somnium && !v.IsSomniumPlayer) v.IsSomniumPlayer = true;
            if (e.Distance == float.MaxValue && v.Online && v.MapId >= 0 && s != null && v.MapId == s.MapId && p != null && (v.Pos.X != 0f || v.Pos.Y != 0f))
            {
                float dx = v.Pos.X - p.Pos.X, dy = v.Pos.Y - p.Pos.Y, dz = v.Pos.Z != 0f ? v.Pos.Z - p.Pos.Z : 0f;   // the stats carry no height
                e.Distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * YardsToMetres;
            }
            return e;
        }

        /// <summary>The record whose character is this guid: WowGear sets Guid in its records (integrator request) - else a pairing
        /// made by <see cref="PairByElimination"/>.</summary>
        private static int RecordOf(ulong guid)
        {
            if (guid == 0) return -1;
            for (int i = 0; i < Records.Count; i++) if (Records[i].Guid == guid) return i;
            return -1;
        }

        /// <summary>
        /// Without a character guid in the records: when exactly one Somnium record is in the world with no guid and exactly one group
        /// member is a Somnium character (WowGear.IsSomniumCharacter) not matched yet, they are the same person - the only unambiguous
        /// case; anything else stays unmatched (no portrait for that member).
        /// </summary>
        private static void PairByElimination(List<GroupMemberView> group)
        {
            int rec = -1, recs = 0;
            for (int i = 0; i < Records.Count; i++)
                if (Records[i].Guid == 0 && Records[i].MapId >= 0) { rec = i; recs++; }
            if (recs != 1) return;
            int mem = -1, mems = 0;
            for (int i = 0; i < Out.Count; i++)
                if (Out[i].IsSomnium && Out[i].PlayerId < 0) { mem = i; mems++; }
            if (mems != 1) return;
            WowPartyMember r = Records[rec];
            WowPartyMember e = Out[mem];
            r.Guid = e.Guid;
            Records[rec] = r;
            e.PlayerId = r.PlayerId;
            e.SomniumUserId = r.SomniumUserId;
            e.SomniumName = r.SomniumName;
            e.Vr = r.Vr;
            if (r.Distance != float.MaxValue) e.Distance = r.Distance;
            Out[mem] = e;
            if (PairedSaid.Add(e.Guid))
                WowLog.Info("Party: group member " + (e.Name ?? "?") + " is Somnium player " + (r.SomniumName ?? "?") + " (the only Somnium character in the group and the only record without a character guid).");
        }

        private static bool SafeIsSomniumCharacter(ulong guid, string name)
        {
            try { return WowGear.IsSomniumCharacter(guid, name); }
            catch (Exception) { return false; }
        }

        /// <summary>The Bridge's user id of every remote player by PhotonId, walked every 2 s (proven members:
        /// PlayersContainer.AllPlayers, ISomniumPlayer.Properties, Properties.PhotonId / Id / IsLocal).</summary>
        private static void RefreshUserIds()
        {
            float now = Time.unscaledTime;
            if (now < _nextUserIds) return;
            _nextUserIds = now + UserIdRefresh;
            List<ISomniumPlayer> all = null;
            try { all = SomniumBridge.PlayersContainer != null ? SomniumBridge.PlayersContainer.AllPlayers : null; }
            catch (Exception) { all = null; }   // no client (the editor)
            if (all == null) return;
            UserIds.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    ISomniumPlayer pl = all[i];
                    if (pl == null || pl.Properties == null || pl.Properties.IsLocal) continue;
                    string id = pl.Properties.Id;
                    if (!string.IsNullOrEmpty(id)) UserIds[pl.Properties.PhotonId] = id;
                }
                catch (Exception) { }
            }
        }

        /// <summary>World left / session over: forget everything (UiGroup.OnDisable).</summary>
        public static void Reset()
        {
            Records.Clear();
            Out.Clear();
            UserIds.Clear();
            PairedSaid.Clear();
            _nextUserIds = -1f;
            _saidMode = null;
            ShowsGroup = false;
        }
    }
}
