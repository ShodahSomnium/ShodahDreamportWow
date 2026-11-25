using Fusion;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeData64 : NetworkBridgeData
    {
        // Total 256 Bytes or memory
        [Networked, Capacity(64)] private NetworkArray<int> SyncIntArray => default;
        public override NetworkArray<int> IntArray => SyncIntArray;
        public override int IntCount => 64;
    }
}
