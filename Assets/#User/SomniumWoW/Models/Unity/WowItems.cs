using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>Held items (weapons, shields) by ItemDisplayInfo id.</summary>
    public static class WowItems
    {
        /// <summary>
        /// ItemDisplayInfo model (Item\ObjectComponents\Weapon|Shield|...\&lt;ModelName&gt;.m2, the folder found by probing -
        /// unambiguous over all 24,337 names, see the Models report) with its ModelTexture as the replaceable type 2 texture.
        /// leftHand uses ModelName[1]/ModelTexture[1] when the item has them. The root's origin is the grip: parent it to
        /// M2Animator.Attachment(1 right hand / 2 left hand / 0 shield) with identity local transform. Null when missing.
        /// </summary>
        public static GameObject Build(WowFiles files, int itemDisplayId, bool leftHand)
        {
            var info = WowModelDb.ResolveItem(files, itemDisplayId, leftHand);
            if (info.Error != null) { WowLog.Warn("WowItems: " + info.Error); return null; }
            var o = new M2BuildOptions { Name = "Item " + itemDisplayId };
            if (!string.IsNullOrEmpty(info.TexturePath)) o.ReplaceablePaths[2] = info.TexturePath;
            var go = M2Factory.Build(files, info.ModelPath, o);
            if (go != null) WowLog.Info("WowItems: display " + itemDisplayId + " -> " + info.ModelPath + " texture " + (info.TexturePath ?? "none"));
            return go;
        }
    }
}
