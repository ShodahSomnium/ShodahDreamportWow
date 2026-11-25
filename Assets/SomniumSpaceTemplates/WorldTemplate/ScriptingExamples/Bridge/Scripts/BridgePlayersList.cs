using TMPro;
using UnityEngine;
using SomniumSpace.Bridge.Player;
using System.Collections.Generic;

namespace SomniumSpace.Bridge.Examples
{
    public class BridgePlayersList : MonoBehaviour
    {
        [Header("Global References")]
        [SerializeField]
        private TMP_Text _playersCount;
        [SerializeField]
        private BridgePlayersContainer _playersContainer;
        
        [Header("Selected Player")]
        [SerializeField]
        private TMP_Text _nickname;
        [SerializeField]
        private TMP_Text _id;
        [SerializeField]
        private TMP_Text _isLocal;
        
        [Header("UI Spawn")]
        [SerializeField]
        private BridgePlayerItem _prefab;
        [SerializeField]
        private Transform _content;
        
        private Dictionary<ISomniumPlayer, BridgePlayerItem> _players = new Dictionary<ISomniumPlayer, BridgePlayerItem>();
        
        /// <summary>
        /// Method for BridgePlayersContainer OnPlayerAdded event
        /// </summary>
        /// <param name="somniumPlayer"></param>
        public void AddPlayer(ISomniumPlayer somniumPlayer)
        {
            //Add a dictionary UI item if it hasn't been created when the Somnium Space Player enters the game session
            if (!_players.ContainsKey(somniumPlayer))
            {
                var playerItem = Instantiate(_prefab, _content);
                playerItem.Setup(somniumPlayer, SelectPlayer);
                
                _players.Add(somniumPlayer, playerItem);
            }
            
            UpdateCounter();
        }
        
        /// <summary>
        /// Trigger when an item is chosen from the list
        /// </summary>
        /// <param name="somniumPlayer"></param>
        private void SelectPlayer(ISomniumPlayer somniumPlayer)
        {
            //Set nickname
            if (_nickname != null)
                _nickname.text = somniumPlayer?.Properties?.NickName;
            
            //Set ID
            if (_id != null)
                _id.text = somniumPlayer?.Properties?.Id;
            
            //Set is player local or remote
            if (_isLocal != null)
                _isLocal.text = somniumPlayer?.Properties?.IsLocal.ToString();
        }
        
        /// <summary>
        /// Method for BridgePlayersContainer OnPlayerRemoved event
        /// </summary>
        public void RemovePlayer(ISomniumPlayer somniumPlayer)
        {
            //Remove and delete the dictionary UI item when the Somnium Space Player exits the game session
            if (_players.TryGetValue(somniumPlayer, out var playerItem))
            {
                Destroy(playerItem.gameObject);
                
                _players.Remove(somniumPlayer);
            }
            
            UpdateCounter();
        }

        private void UpdateCounter()
        {
            //Update global players count value 
            if (_playersCount != null && _playersContainer != null)
                _playersCount.text = "Players Count: " + (_playersContainer.PlayersCount + 1); //local player including
        }
    }
}
