using System.Collections;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Offsets the queued remote avatar roots for rendering after every client script's LateUpdate (order 30000; the
    /// client's highest is CloneRenderer 20600, then GenericBaker 15001, TwistRelaxer 10300, FBBIK 9999, VRIK 9998) and
    /// puts them back once the frame is rendered (WaitForEndOfFrame). WowGear adds it next to itself. See GearRemap.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public sealed class GearShift : MonoBehaviour
    {
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
        private Coroutine _loop;

        private void OnEnable() { _loop = StartCoroutine(RestoreLoop()); }

        private void OnDisable()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
            GearRemap.RestoreAll();
            GearRemap.Forget();
        }

        private void LateUpdate()
        {
            try { GearRemap.ApplyAll(); }
            catch (System.Exception e) { WowLog.Error("Gear: offsetting remote avatars threw: " + e); }
        }

        private IEnumerator RestoreLoop()
        {
            while (true)
            {
                yield return EndOfFrame;
                try { GearRemap.RestoreAll(); }
                catch (System.Exception e) { WowLog.Error("Gear: restoring remote avatars threw: " + e); }
            }
        }
    }
}
