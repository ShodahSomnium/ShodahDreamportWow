using UnityEngine;

namespace SomniumSpace.Bridge.Examples
{
    /// <summary>
    /// Example of Bridge Environment API usage. Similar to SomniumEnvironment component
    /// </summary>
    public class BridgeEnvironment : MonoBehaviour
    {
        [SerializeField] 
        private Material _skybox;
        [SerializeField] 
        private Light _sunSource;

        [SerializeField, Range(0, 8)] 
        private float _ambientIntensity = 1f;
        [SerializeField, ColorUsage(false, true)]
        private Color _ambientLightColor = Color.white;
        [SerializeField, ColorUsage(false, true)]
        private Color _ambientSkyColor = Color.white;
        [SerializeField, ColorUsage(false, true)]
        private Color _ambientEquatorColor = Color.white;
        [SerializeField, ColorUsage(false, true)]
        private Color _ambientGroundColor = Color.white;

        public void SetSkybox()
        {
            SomniumBridge.Environment?.SetSkybox(_skybox);
        }
        
        public void SetSunSource()
        {
            SomniumBridge.Environment?.SetSunSource(_sunSource);
        }
        
        public void SetAmbientModeSkybox()
        {
            SomniumBridge.Environment?.SetAmbientModeSkybox(_ambientIntensity);
        }
        
        public void SetAmbientModeGradient()
        {
            SomniumBridge.Environment?.SetAmbientModeGradient(_ambientSkyColor, _ambientEquatorColor, _ambientGroundColor);
        }
        
        public void SetAmbientModeColor()
        {
            SomniumBridge.Environment?.SetAmbientModeColor(_ambientLightColor);
        }
        
        public void UpdateEnvironment()
        {
            SomniumBridge.Environment?.UpdateEnvironment();
        }
    }
}