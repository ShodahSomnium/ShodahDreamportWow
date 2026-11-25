// iteration 11 - owner trainer (proposal-trainer.md 6.2 U11 a): a game object's one-shot custom animation - SMSG_GAMEOBJECT_CUSTOM_ANIM (the fishing
// bobber's bite) arrives as EntityState.Emote = AnimationData 153 + n 'CustomN' on EmoteCounter: played once, then Stand again, with the
// object's GameObjectDisplayInfo Sound[6 + n] (the bobber, display 668: Sound[6] = SoundEntries 3355 'Fishing Hooked', harness TrGoDisplay).
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    internal sealed partial class WowEntityView
    {
        private int _trGoEmote = int.MinValue;
        private float _trGoUntil;

        partial void TrGoAnimate(EntityState s, float now)
        {
            if (s == null) return;
            bool custom = s.Emote >= 153 && s.Emote <= 156;   // AnimationData 153-156 'Custom0'..'Custom3' (harness TrAnims)
            if (_trGoEmote == int.MinValue) _trGoEmote = s.EmoteCounter - (custom ? 1 : 0);   // a bite that came before the model still plays once
            if (s.EmoteCounter != _trGoEmote)
            {
                _trGoEmote = s.EmoteCounter;
                if (custom)
                {
                    if (Anim != null && Anim.Has(s.Emote))
                    {
                        Anim.HoldAtEnd = false;
                        Anim.Speed = 1f;
                        Anim.Play(s.Emote, false);
                        _trGoUntil = now + DurationOf(s.Emote);
                    }
                    TrGoSound(s.DisplayId, 6 + s.Emote - 153);
                }
            }
            if (_trGoUntil > 0f && now >= _trGoUntil)
            {
                _trGoUntil = 0f;
                if (s.GoType != 0 && Anim != null && Anim.Has(AnimStand)) Anim.Play(AnimStand, true);
            }
        }

        /// <summary>GameObjectDisplayInfo.Sound[i] (fields 2-11, SoundEntries ids) played at the object (3D).</summary>
        private void TrGoSound(int displayId, int index)
        {
            if (displayId <= 0 || index < 0 || index > 9 || Root == null) return;
            var t = Dbc.Load(WowPlay.Files, "GameObjectDisplayInfo");
            if (t == null) return;
            int r = t.FindRow(displayId);
            if (r < 0) return;
            int entry = t.GetInt(r, DbcFields.GameObjectDisplayInfo.Sound + index);
            if (entry > 0) WowAudio.PlaySoundEntry(entry, Root.transform);
        }
    }
}
