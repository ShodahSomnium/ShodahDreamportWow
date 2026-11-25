using TMPro;
using UnityEngine;

namespace SomniumSpace.Bridge.Examples
{
    public class BridgeNetworkHandler : MonoBehaviour
    {
        [Header("Network References")] 
        [SerializeField]
        private BridgeNetwork _network;
        
        [Header("Network Visual")]
        [SerializeField]
        private TMP_Text _status;
        [SerializeField]
        private TMP_Text _isConnected;
        [SerializeField]
        private TMP_Text _isHost;

        private bool _isChanging;

        /// <summary>
        /// Method for BridgeNetwork OnConnected event
        /// </summary>
        public void Connected()
        {
            _isChanging = true;
        }
        
        /// <summary>
        /// Method for BridgeNetwork OnDisconnected event
        /// </summary>
        public void Disconnected()
        {
            _isChanging = false;
        }

        /// <summary>
        /// Update network data on UI
        /// </summary>
        private void Update()
        {
            if (!_isChanging)
                return;
            
            if (_network == null)
                return;
            
            if (_status != null)
                _status.text = "Connection Status: " + _network.Status;
            
            if (_isConnected != null)
                _isConnected.text = "Is Connected: " + _network.IsConnected;
            
            if (_isHost != null)
                _isHost.text = "Is Host: " + _network.IsHost;
        }
    }
}
