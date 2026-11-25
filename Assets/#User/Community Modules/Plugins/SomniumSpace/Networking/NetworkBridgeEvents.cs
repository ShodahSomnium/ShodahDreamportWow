using Fusion;
using System;
using System.Reflection;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridgeEvents : NetworkBehaviour, IStateAuthorityChanged
    {
        public event Action OnSpawned;  // Called when the object is spawned in the network
        public event Action<NetworkRunner, bool> OnDespawned;  // Called when the object is despawned in the network
        public event Action OnFixedUpdateNetwork;  // Called once per network‐simulation step (State Authority)
        public event Action OnRender;  // Called once per network‐simulation step
        public event Action<bool> OnStateAuthorityChanged; // Called when state authority is gain or lost
        public event Action<byte[]> OnMessageToAll; // All clients including the state authority owner
        public event Action<byte[]> OnMessageToProxies; // Proxies are all clients except the state authority owner
        public event Action<byte[]> OnMessageToController; // Controller is the state authority owner
        public event Action<byte[]> OnMessageToPlayer; // Send a message to a specific player

        // Called when the object is ready so you can initialize your networked variables.
        public override void Spawned()
        {
            OnSpawned?.Invoke();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            OnDespawned?.Invoke(runner, hasState);
        }

        // RPC send byte array to all including local, messageID and message can be anything you want.
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SendMessageToAll(byte[] message, RpcInfo info = default)
        {
            OnMessageToAll?.Invoke(message);
        }

        // RPC send byte array only to others, messageID and message can be anything you want.
        [Rpc(RpcSources.All, RpcTargets.Proxies)]
        public void RPC_SendMessageToProxies(byte[] message, RpcInfo info = default)
        {
            OnMessageToProxies?.Invoke(message);
        }

        // RPC send byte array only to state authority, messageID and message can be anything you want.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SendMessageToController(byte[] message, RpcInfo info = default)
        {
            OnMessageToController?.Invoke(message);
        }

        // RPC send byte array only to a specific player.
        [Rpc(sources: RpcSources.All, targets: RpcTargets.All)]
        public void RPC_SendMessageToPlayer([RpcTarget] PlayerRef player, byte[] message) 
        {
            OnMessageToPlayer?.Invoke(message);
        }

        // Internal Callback, do not use directly.
        public void StateAuthorityChanged()
        {
            OnStateAuthorityChanged?.Invoke(Object.HasStateAuthority);
        }

        // Fusion calls this once per network‐simulation step
        public override void FixedUpdateNetwork()
        {
            OnFixedUpdateNetwork?.Invoke();
        }

        // Same frequency as Update, use to render networked objects
        public override void Render()
        {
            OnRender?.Invoke();
        }
    }
}
