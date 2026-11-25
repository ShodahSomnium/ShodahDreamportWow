using System;
using TMPro;
using UnityEngine;
using SomniumSpace.Bridge.Player;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Somnium Player representation on UI. Responsible for item setup and its selection
    /// </summary>
    public class BridgePlayerItem : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text _nickname;

        private ISomniumPlayer _somniumPlayer;
        private Action<ISomniumPlayer> _onSelect;
        
        public void Setup(ISomniumPlayer somniumPlayer, Action<ISomniumPlayer> onSelect)
        {
            _somniumPlayer = somniumPlayer;
            _onSelect = onSelect;
         
            //Set nickname as item's name
            if (_nickname != null)
                _nickname.text = somniumPlayer?.Properties?.NickName;
            
            //Activate new item after instantiate
            gameObject.SetActive(true);
        }

        public void Select(bool isSelected)
        {
            //If toggle isOn invoke select event with current player info
            if (isSelected)
                _onSelect?.Invoke(_somniumPlayer);
        }
    }
}
