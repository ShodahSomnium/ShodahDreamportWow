using Fusion;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeData12 : NetworkBridgeData
    {
        // Total 48 Bytes or memory
        [Networked, Capacity(12)] private NetworkArray<int> SyncIntArray => default;
        public override NetworkArray<int> IntArray => SyncIntArray;
        public override int IntCount => 12;
    }
}
