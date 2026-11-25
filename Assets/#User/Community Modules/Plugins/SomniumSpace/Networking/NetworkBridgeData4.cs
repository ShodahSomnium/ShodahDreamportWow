using Fusion;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeData4 : NetworkBridgeData
    {
        // Total 16 Bytes or memory
        [Networked, Capacity(4)] private NetworkArray<int> SyncIntArray => default;
        public override NetworkArray<int> IntArray => SyncIntArray;
        public override int IntCount => 4;
    }
}
