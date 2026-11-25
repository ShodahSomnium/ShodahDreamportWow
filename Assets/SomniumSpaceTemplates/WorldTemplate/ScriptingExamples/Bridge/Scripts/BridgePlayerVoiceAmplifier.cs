using UnityEngine;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Components;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Example of Bridge Player Voice Feature API usage. Similar to SomniumPlayerVoiceAmplifier component
    /// </summary>
    public class BridgePlayerVoiceAmplifier : SomniumPlayerAction
    {
        [SerializeField]
        private bool _amplify = true;

        [SerializeField] 
        private float _distance = 200f;
        
        public override void DoAction(ISomniumPlayer somniumPlayer)
        {
            if (_amplify)
                somniumPlayer?.Features?.Voice?.EnableAmplify(_distance);
            else
                somniumPlayer?.Features?.Voice?.DisableAmplify();
        }
    }
}