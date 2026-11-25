// iteration 11 - owner uicore (proposal-uicore.md hook H9): an item's pickup / drop sound when it is put on / taken off the cursor.
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Audio
{
    public sealed partial class WowAudioDriver
    {
        /// <summary>Item.dbc -> ItemDisplayInfo.GroupSoundIndex -> ItemGroupSounds [0] pickup / [1] drop, 2D; nothing when the item has none
        /// or the sound tables are not read yet.</summary>
        internal void PlayItemSound(int itemEntry, bool drop)
        {
            if (_data == null || itemEntry <= 0) return;
            int id = drop ? _data.ItemDropSound(itemEntry) : _data.ItemPickupSound(itemEntry);
            if (id > 0) PlayEntry(id, SoundCategory.Ui, Vector3.zero, null, true, 1f);
        }
    }
}
