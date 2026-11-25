using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using SomniumSpace.Bridge.Player;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Example of Bridge Players Container API usage. Similar to SomniumPlayersContainer component
    /// </summary>
    public class BridgePlayersContainer : MonoBehaviour
    {
        public int PlayersCount
        {
            get
            {
                var container = SomniumBridge.PlayersContainer;
                if (container == null)
                    return 0;
                
                return container.PlayersCount;
            }
        }
        
        public ISomniumPlayer LocalPlayer
        {
            get
            {
                var container = SomniumBridge.PlayersContainer;
                if (container == null)
                    return null;
                
                return container.LocalPlayer;
            }
        }
        
        public List<ISomniumPlayer> AllPlayers
        {
            get
            {
                var container = SomniumBridge.PlayersContainer;
                if (container == null)
                    return null;
                
                return container.AllPlayers;
            }
        }

        //"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
        public ISomniumPlayer GetPlayerByID(string userId)
        {
            var container = SomniumBridge.PlayersContainer;
            if (container == null)
                return null;
                
            return container.GetPlayerByID(userId);
        }
        
        //"SomniumSpace"
        public ISomniumPlayer GetPlayerByNickname(string nickname)
        {
            var container = SomniumBridge.PlayersContainer;
            if (container == null)
                return null;
                
            return container.GetPlayerByNickName(nickname);
        }
        
        [SerializeField] 
        private UnityEvent<ISomniumPlayer> _onPlayerAdded; 
        [SerializeField] 
        private UnityEvent<ISomniumPlayer> _onPlayerRemoved; 
        [SerializeField] 
        private UnityEvent<ISomniumPlayer> _onLocalPlayerAdded; 
        
        private void OnEnable()
        {
            var playersContainer = SomniumBridge.PlayersContainer;
            if (playersContainer != null)
            {
                playersContainer.OnPlayerAdded += OnPlayerAdded;
                playersContainer.OnPlayerRemoved += OnPlayerRemoved;
                playersContainer.OnLocalPlayerAdded += OnLocalPlayerAdded;
            }
        }
        
        private void OnPlayerAdded(ISomniumPlayer somniumPlayer)
        {
            _onPlayerAdded?.Invoke(somniumPlayer);
        }
        
        private void OnPlayerRemoved(ISomniumPlayer somniumPlayer)
        {
            _onPlayerRemoved?.Invoke(somniumPlayer);
        }

        private void OnLocalPlayerAdded(ISomniumPlayer somniumPlayer)
        {
            _onLocalPlayerAdded?.Invoke(somniumPlayer);
        }

        private void OnDisable()
        {
            var playersContainer = SomniumBridge.PlayersContainer;
            if (playersContainer != null)
            {
                playersContainer.OnPlayerAdded -= OnPlayerAdded;
                playersContainer.OnPlayerRemoved -= OnPlayerRemoved;
                playersContainer.OnLocalPlayerAdded -= OnLocalPlayerAdded;
            }
        }
    }
}