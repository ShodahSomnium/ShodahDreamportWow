// iteration 11 - owner world (proposal-world.md 2.6): Unity glue of InstanceSight for GearRemote (world's hook, applied in GearRemote.Place:
// after GearRemap.Decide said "shown", a remote Somnium player on a dungeon / raid / battleground / arena map is drawn only when he is in
// our server group or the server shows us his character - i.e. he is in OUR copy of the map). Continents, cities and the Deeprun Tram
// (Map.dbc InstanceType 0) keep today's rule. Voice is not touched (decision U1).
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    public static class WowInstanceGate
    {
        static readonly InstanceSight Sight = new InstanceSight();
        static readonly Dictionary<ulong, int> LastOrigin = new Dictionary<ulong, int>();   // remote character -> his last origin map
        static readonly Dictionary<ulong, bool> LastShown = new Dictionary<ulong, bool>();   // for one log line per state change
        static readonly System.Func<string, int> NameHash = WowNetWire.NameHash;
        static bool _subscribed;

        /// <summary>May this remote Somnium player be drawn? why = the reason when not (logs).</summary>
        public static bool Allows(in GearLocalView v, in WowGearState g, out string why)
        {
            why = null;
            if (!_subscribed) { _subscribed = true; WowPlay.Ended += OnEnded; }
            var s = WowPlay.Session;
            if (!v.InWorld || s == null || s.Player == null || s.Player.MapInstanceType == 0) return true;
            ulong guid = g.CharacterGuid;
            if (guid != 0)
            {
                int origin = g.OriginSaid ? g.OriginMap : -1;
                int last;
                if (LastOrigin.TryGetValue(guid, out last) && last != origin) Sight.Forget(guid);   // a new visit may be another copy
                LastOrigin[guid] = origin;
            }
            bool ok = Sight.Allows(s, v.Map, guid, g.CharacterNameHash, NameHash, out why);
            bool before;
            if (!LastShown.TryGetValue(guid, out before) || before != ok)
            {
                LastShown[guid] = ok;
                // no names: the guid's low part and the map only (privacy rule - the log carries no player data)
                WowLog.Info("Instances: remote character " + (guid & 0xFFFFFFUL) + (ok ? " shown" : " hidden - " + why) + " on map " + v.Map
                            + " (instance type " + s.Player.MapInstanceType + ", " + Sight.ConfirmedCount + " confirmed in this visit)");
            }
            return ok;
        }

        /// <summary>WowPlay.Ended: forget every remembered map and confirmation.</summary>
        public static void Reset(string why)
        {
            if (LastOrigin.Count > 0 || Sight.ConfirmedCount > 0) WowLog.Info("Instances: sight reset (" + (why ?? "?") + ")");
            Sight.Clear();
            LastOrigin.Clear();
            LastShown.Clear();
        }

        static void OnEnded() { Reset("the game ended"); }
    }
}
