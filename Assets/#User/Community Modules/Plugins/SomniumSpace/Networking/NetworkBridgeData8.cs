using Fusion;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeData8 : NetworkBridgeData
    {
        // Total 32 Bytes or memory
        [Networked, Capacity(8)] private NetworkArray<int> SyncIntArray => default;
        public override NetworkArray<int> IntArray => SyncIntArray;
        public override int IntCount => 8;
    }
}
