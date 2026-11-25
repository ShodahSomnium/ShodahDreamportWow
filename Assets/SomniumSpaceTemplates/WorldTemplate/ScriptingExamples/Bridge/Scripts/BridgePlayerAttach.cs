using UnityEngine;
using System.Collections.Generic;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Components;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Example of Bridge Player Body Reference API usage
    /// </summary>
    public class BridgePlayerAttach : SomniumPlayerAction
    {
        [SerializeField]
        private GameObject _attachableObject;
        [SerializeField]
        private Vector3 _attachPosition;
        [SerializeField]
        private Vector3 _attachRotation;
        
        private Dictionary<ISomniumPlayer, GameObject> _attachments = new Dictionary<ISomniumPlayer, GameObject>();
        
        public override void DoAction(ISomniumPlayer somniumPlayer)
        {
            if (_attachableObject == null)
                return;
            
            if (!_attachments.ContainsKey(somniumPlayer))
            {
                var attachment = Instantiate(_attachableObject, somniumPlayer.References?.Body?.RightHand);
                attachment.transform.localPosition = _attachPosition;
                attachment.transform.localRotation = Quaternion.Euler(_attachRotation);
                
                _attachments.Add(somniumPlayer, attachment);
            }
        }
    }
}