using UnityEngine;
using UnityEngine.Events;
using SomniumSpace.Bridge.Network;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Example of Bridge Network API usage. Similar to SomniumNetwork component
    /// </summary>
    public class BridgeNetwork : MonoBehaviour
    {
        public ConnectionStatus Status
        {
            get
            {
                var network = SomniumBridge.Network;
                if (network == null)
                    return ConnectionStatus.DISCONNECTED;
                
                return network.Status;
            }
        }
        
        public bool IsConnected
        {
            get
            {
                var network = SomniumBridge.Network;
                if (network == null)
                    return false;
                
                return network.IsConnected;
            }
        }
        
        public bool IsHost
        {
            get
            {
                var network = SomniumBridge.Network;
                if (network == null)
                    return false;
                
                return network.IsHost;
            }
        }
        
        [SerializeField] 
        private UnityEvent _onConnected; 
        [SerializeField] 
        private UnityEvent _onDisconnected; 
        [SerializeField] 
        private UnityEvent _onJoinedToGameSession; 
        [SerializeField] 
        private UnityEvent _onLeaveFromGameSession; 
        
        private void OnEnable()
        {
            var network = SomniumBridge.Network;
            if (network != null)
            {
                network.OnConnected += OnConnected;
                network.OnDisconnected += OnDisconnected;
                network.OnJoinedToGameSession += OnJoinedToGameSession;
                network.OnLeaveFromGameSession += OnLeaveFromGameSession;
            }
        }

        private void OnConnected()
        {
            _onConnected?.Invoke();
        }

        private void OnDisconnected()
        {
            _onDisconnected?.Invoke();
        }

        private void OnJoinedToGameSession()
        {
            _onJoinedToGameSession?.Invoke();
        }

        private void OnLeaveFromGameSession()
        {
            _onLeaveFromGameSession?.Invoke();
        }

        private void OnDisable()
        {
            var network = SomniumBridge.Network;
            if (network != null)
            {
                network.OnConnected -= OnConnected;
                network.OnDisconnected -= OnDisconnected;
                network.OnJoinedToGameSession -= OnJoinedToGameSession;
                network.OnLeaveFromGameSession -= OnLeaveFromGameSession;
            }
        }
    }
}