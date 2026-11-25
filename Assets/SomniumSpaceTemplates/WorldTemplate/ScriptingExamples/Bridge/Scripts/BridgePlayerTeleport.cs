using UnityEngine;
using UnityEngine.Events;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Components;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Example of Bridge Player Motion Feature API usage. Similar to SomniumPlayerTeleport component
    /// </summary>
    public class BridgePlayerTeleport : SomniumPlayerAction
    {
        [SerializeField]
        private Vector3 _teleportPoint;
        [SerializeField]
        private Vector3 _teleportDirection;
        [SerializeField] 
        private UnityEvent _teleportDone; 
        
        public override void DoAction(ISomniumPlayer somniumPlayer)
        {
            somniumPlayer?.Features?.Motion?.DoTeleportToPoint(_teleportPoint, _teleportDirection, TeleportDone);
        }
        
        private void TeleportDone()
        {
            _teleportDone?.Invoke();
        }
    }
}