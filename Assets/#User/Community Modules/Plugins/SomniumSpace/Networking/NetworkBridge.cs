using Fusion;
using System;
using System.Reflection;
using UnityEngine;

namespace SomniumSpace.Network.Bridge
{
    [Obfuscation(Exclude = true)]
    public class NetworkBridge : NetworkBehaviour, IStateAuthorityChanged
    {
        /// <summary>
        /// LEGACY - DO NOT USE ANYMORE
        /// </summary>

        public event Action OnSpawned;  // Called when the object is spawned in the network
        public event Action OnNetworkTick;  // Called once per network‐simulation step
        public event Action<bool> OnStateAuthorityChanged; // Called when state authority is gain or lost
        public event Action<byte, byte[]> OnMessageToAll; // All clients including the state authority owner
        public event Action<byte, byte[]> OnMessageToProxies; // Proxies are all clients except the state authority owner
        public event Action<byte, byte[]> OnMessageToController; // Controller is the state authority owner

        // General purpose networked variables, be attentive to maximum arrays capacity.
        [Networked, Capacity(512)] public NetworkArray<byte> SyncByteArray => default;
        [Networked, Capacity(8)] public NetworkArray<int> SyncIntArray => default;
        [Networked, Capacity(8)] public NetworkArray<float> SyncFloatArray => default;
        [Networked, Capacity(8)] public NetworkArray<Vector2> SyncVector2Array => default;
        [Networked, Capacity(8)] public NetworkArray<Vector3> SyncVector3Array => default;
        [Networked, Capacity(8)] public NetworkArray<Vector4> SyncVector4Array => default;
        [Networked, Capacity(4)] public NetworkArray<NetworkString<_16>> SyncStringArray => default;

        // Called when the object is ready so you can initialize your networked variables.
        public override void Spawned()
        {
            OnSpawned?.Invoke();
        }

        // RPC send byte array to all including local, messageID and message can be anything you want.
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SendMessageToAll(byte messageID, byte[] message, RpcInfo info = default)
        {
            OnMessageToAll?.Invoke(messageID, message);
        }

        // RPC send byte array only to others, messageID and message can be anything you want.
        [Rpc(RpcSources.All, RpcTargets.Proxies)]
        public void RPC_SendMessageToProxies(byte messageID, byte[] message, RpcInfo info = default)
        {
            OnMessageToProxies?.Invoke(messageID, message);
        }

        // RPC send byte array only to state authority, messageID and message can be anything you want.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SendMessageToController(byte messageID, byte[] message, RpcInfo info = default)
        {
            OnMessageToController?.Invoke(messageID, message);
        }

        // Internal Callback, do not use directly.
        public void StateAuthorityChanged()
        {
            OnStateAuthorityChanged?.Invoke(Object.HasStateAuthority);
        }

        // Fusion calls this once per network‐simulation step
        public override void FixedUpdateNetwork()
        {
            OnNetworkTick?.Invoke();
        }
    }
}
