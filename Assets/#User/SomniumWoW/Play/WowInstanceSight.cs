// iteration 11 - owner world (proposal-world.md 2.6): "only the party is visible inside" - which remote Somnium players are drawn on a
// dungeon / raid / battleground / arena map. Two groups in two copies of the same dungeon have the same map id; the server only shows us
// the players of OUR copy, so a remote player counts as "here" when he is in our server group or our session has his character as an
// entity. Pure C#: no UnityEngine (harness probe D11InstSight / I11 world probes).
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    public sealed class InstanceSight
    {
        readonly HashSet<ulong> _confirmed = new HashSet<ulong>();
        int _map = int.MinValue;

        /// <summary>Remote characters confirmed as being in our copy of the map during this visit.</summary>
        public int ConfirmedCount { get { return _confirmed.Count; } }

        /// <summary>
        /// Rules of 2.6, applied only when our session's Player.MapInstanceType != 0 (continents, cities and the Deeprun Tram keep the
        /// map rule of GearRemap): 1 no WoW character in their record -> hidden; 2 confirmed in this visit -> shown; 3 in our server group
        /// (name hash checked when both are known) -> shown; 4 our session has their character as a Player entity (same name hash) ->
        /// shown and confirmed (the confirmation keeps them through the visibility range edge: AC Visibility.Distance.Instances 170 /
        /// BGArenas 250 yd); 5 else hidden (another copy of this map). localMap = our origin map (WowSpace.OriginMap); a change forgets
        /// every confirmation. nameHash = WowNetWire.NameHash (passed in: Networking is not pure).
        /// </summary>
        public bool Allows(IWowSession s, int localMap, ulong remoteGuid, int remoteNameHash, Func<string, int> nameHash, out string why)
        {
            why = null;
            if (localMap != _map) { _confirmed.Clear(); _map = localMap; }
            var p = s != null ? s.Player : null;
            if (p == null || p.MapInstanceType == 0) return true;
            if (remoteGuid == 0) { why = "no WoW character here"; return false; }
            if (_confirmed.Contains(remoteGuid)) return true;
            var g = p.Group;
            if (g != null)
            {
                for (int i = 0; i < g.Count; i++)
                {
                    var m = g[i];
                    if (m == null || m.Guid != remoteGuid) continue;
                    if (!NameMatches(m.Name, remoteNameHash, nameHash)) continue;
                    return true;
                }
            }
            var e = s.Find(remoteGuid);
            if (e != null && e.Kind == EntityKind.Player && NameMatches(e.Name, remoteNameHash, nameHash))
            {
                _confirmed.Add(remoteGuid);
                return true;
            }
            why = "another copy of map " + localMap;
            return false;
        }

        static bool NameMatches(string name, int remoteNameHash, Func<string, int> nameHash)
        {
            if (remoteNameHash == 0 || string.IsNullOrEmpty(name) || nameHash == null) return true;   // not both known: the guid decides
            return nameHash(name) == remoteNameHash;
        }

        /// <summary>A remote player's origin map changed or he went to the room: a new visit may be another copy.</summary>
        public void Forget(ulong remoteGuid) { _confirmed.Remove(remoteGuid); }

        public void Clear() { _confirmed.Clear(); _map = int.MinValue; }
    }
}
