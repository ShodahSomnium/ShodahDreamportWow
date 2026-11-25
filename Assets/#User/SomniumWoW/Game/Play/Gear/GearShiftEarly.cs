using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// The fallback for GearShift's end-of-frame restore: first thing in the next frame's Update (order -30000, before the
    /// client's drivers at 0 and Fusion's Render right after Update) every remote avatar root offset for rendering is put
    /// back (a no-op when the end-of-frame restore already ran). WowGear adds it next to itself.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public sealed class GearShiftEarly : MonoBehaviour
    {
        private void Update()
        {
            try { GearRemap.RestoreAll(); }
            catch (System.Exception e) { WowLog.Error("Gear: early restore of remote avatars threw: " + e); }
        }
    }
}
