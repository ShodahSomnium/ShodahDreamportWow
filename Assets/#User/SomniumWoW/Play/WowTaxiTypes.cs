using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // CONTRACT (iteration 4, pure C#). The flight map a flight master opens (IWowSession.CurrentTaxi). Node positions are
    // WoW world coordinates from the client's TaxiNodes.dbc; routes come from TaxiPath.dbc / TaxiPathNode.dbc.
    // Owners may ADD fields; do not rename or remove.

    public sealed class TaxiNodeView
    {
        public int Id;                    // TaxiNodes.dbc id
        public string Name;               // from TaxiNodes.dbc (read at runtime)
        public int MapId;
        public float X, Y, Z;
        public bool Known;                // discovered by this character
        public bool Current;              // the node the player is at
        public bool Reachable;            // a route exists from the current node for this faction
        public int Cost;                  // copper from the current node (0 when not reachable)
        public int PathId;                // TaxiPath.dbc id from the current node (direct), 0 when not direct
        public List<int> Route = new List<int>(); // TaxiPath ids hop by hop from the current node (multi-hop flights)
    }

    public sealed class TaxiView
    {
        public ulong MasterGuid;          // the flight master entity
        public int CurrentNodeId;
        public int MapId;
        public List<TaxiNodeView> Nodes = new List<TaxiNodeView>(); // every node of this map for the player's faction
    }
}
