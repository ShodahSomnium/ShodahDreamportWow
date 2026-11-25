using Fusion;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeData : NetworkBridgeEvents
    {
        public virtual NetworkArray<int> IntArray { get; }
        public virtual int IntCount { get; }
    }
}
