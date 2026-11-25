using UnityEngine;
using SomniumSpace.Worlds.SomniumWoW.Game.Audio;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>Named interface sounds (the Audio owner maps each to the game's own sound file).</summary>
    public enum WowUiSound
    {
        ButtonClick, WindowOpen, WindowClose, SpellbookOpen, SpellbookClose, CharacterOpen, CharacterClose, BagOpen, BagClose,
        QuestLogOpen, QuestLogClose, QuestAccepted, QuestCompleted, QuestAbandoned, QuestObjective, LevelUp, LootCoin,
        LootItem, Error, TargetSelect, TargetDeselect, ActionBarDrop, Death
    }

    /// <summary>
    /// CONTRACT (owner: Audio). Every sound the world plays, read from the player's game files at runtime and mixed by the
    /// WowSettings volumes (MasterVolume x the category volume, re-read on WowSettings.Changed). Spell visuals (VFX owner) and the
    /// UI call these. All of them are safe to call at any time from the main thread: before the game files are ready, when a
    /// sound is missing, or with no WowAudioDriver in the scene they do nothing (logged once).
    ///
    /// The Audio owner also plays, on its own (callers must NOT play them again): weapon swing/impact sounds, the quest / level /
    /// loot / death sounds of WowPlay.Event (QuestAccepted, QuestCompleted, QuestRewarded, QuestAbandoned, LevelUp,
    /// MoneyReceived, LootReceived, PlayerDied), race error voice lines, NPC greetings/farewells, footsteps, zone music and
    /// ambience. The UI owner plays window open/close and clicks (PlayUi).
    /// </summary>
    public static class WowAudio
    {
        /// <summary>A SoundEntries.dbc sound (random file of the entry, its own volume/distance) at a point in the world.</summary>
        public static void PlaySoundEntry(int soundEntryId, Vector3 position)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayEntry(soundEntryId, SoundCategory.Effects, position, null, false, 1f);
        }

        /// <summary>A SoundEntries.dbc sound that follows a transform (spells on a moving creature, the player's hands).</summary>
        public static void PlaySoundEntry(int soundEntryId, Transform follow)
        {
            var d = WowAudioDriver.Ensure();
            if (d == null) return;
            if (follow == null) { d.PlayEntry(soundEntryId, SoundCategory.Effects, Vector3.zero, null, true, 1f); return; }
            d.PlayEntry(soundEntryId, SoundCategory.Effects, follow.position, follow, false, 1f);
        }

        /// <summary>A sound file from the game files (WAV/MP3 path inside the MPQs) at a point; volume 0..1 before settings.</summary>
        public static void PlayFile(string gamePath, Vector3 position, float volume)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayPath(gamePath, SoundCategory.Effects, volume, false, position, null, 8f, 45f, 1f);
        }

        /// <summary>An interface sound (2D).</summary>
        public static void PlayUi(WowUiSound sound)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayUi(sound);
        }

        // ------------------------------------------------------------------ additions (iteration 3, Audio owner)

        /// <summary>A SoundEntries.dbc sound as a 2D interface sound (no position).</summary>
        public static void PlaySoundEntryUi(int soundEntryId)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayEntry(soundEntryId, SoundCategory.Ui, Vector3.zero, null, true, 1f);
        }

        /// <summary>A SoundEntries.dbc sound by its Name ("igMainMenuOptionCheckBoxOn", as the client's FrameXML PlaySound uses), 2D.</summary>
        public static void PlayUiNamed(string soundEntryName)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayNamedUi(soundEntryName);
        }

        /// <summary>Iteration 11 (uicore hook H9): an item's pickup sound (ItemGroupSounds [0]) as a 2D interface sound - an item put on
        /// the cursor. Nothing when the item has none.</summary>
        public static void PlayItemPickup(int itemEntry)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayItemSound(itemEntry, false);
        }

        /// <summary>Iteration 11 (uicore hook H9): an item's drop sound (ItemGroupSounds [1]) - an item placed / destroyed from the cursor.</summary>
        public static void PlayItemDrop(int itemEntry)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayItemSound(itemEntry, true);
        }

        /// <summary>
        /// Iteration 11 (audio fix: "some sounds don't loop properly or they play for too long"): a SoundEntries.dbc sound that lasts
        /// as long as its reason - a spell kit's precast (kind 1), channel (2), aura state (3), ground area (4) or a missile's flight
        /// (5), keyed like WowSpellVfx's loops (kind, key). A loop file (SoundEntries Flags 0x200, measured - SoundEntryInfo.Loops)
        /// loops; any other file plays once. It ends at StopSoundEntryLoop(kind, key), after maxSeconds (&gt; 0; e.g. the cast time),
        /// when follow is destroyed, or when the session ends - with a short fade. The same sound for the same (kind, key) again
        /// before the stop does nothing (no restart, no second copy). PlaySoundEntry stays the one-shot: it never loops.
        /// </summary>
        public static void PlaySoundEntryLoop(int soundEntryId, Transform follow, int kind, long key, float maxSeconds)
        {
            var d = WowAudioDriver.Ensure();
            if (d != null) d.PlayEntryLoop(soundEntryId, follow, kind, key, maxSeconds);
        }

        /// <summary>Lead 2026-10-11 (immersive casting): the sound of (kind, key) loops on until StopSoundEntryLoop (a held spell's precast).</summary>
        public static void HoldSoundEntryLoop(int kind, long key)
        {
            var d = WowAudioDriver.Instance;
            if (d != null) d.HoldEntryLoop(kind, key);
        }

        /// <summary>Iteration 11 (audio fix): ends the sound PlaySoundEntryLoop started for (kind, key); nothing when none runs.</summary>
        public static void StopSoundEntryLoop(int kind, long key)
        {
            var d = WowAudioDriver.Instance;
            if (d != null) d.StopEntryLoop(kind, key);
        }

        /// <summary>True once the sound tables are read (sounds requested before are dropped).</summary>
        public static bool Ready
        {
            get { var d = WowAudioDriver.Instance; return d != null && d.DataReady; }
        }
    }
}
