using Fusion;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeData6 : NetworkBridgeData
    {
        // Total 24 Bytes or memory
        [Networked, Capacity(6)] private NetworkArray<int> SyncIntArray => default;
        public override NetworkArray<int> IntArray => SyncIntArray;
        public override int IntCount => 6;
    }
}
