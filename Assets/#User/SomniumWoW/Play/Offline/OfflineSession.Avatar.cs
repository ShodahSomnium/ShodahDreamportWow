// iteration 11 - owner avatar (proposal-avatar.md s7): what the OFFLINE character is drawn as, so the Somnium avatar follows it
// (Game/Play/Forms). Called by npc's OfflineSession.Items.cs UpdatePlayerView after `p.Stealthed = ...` (partial declared in
// OfflineSession.Hooks11.cs; until that call is in, the offline look stays the avatar's own).
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        private bool _avLimitsSaid;
        private int _avLastDisplay = -1;

        /// <summary>
        /// Offline PlayerState.DisplayId / NativeDisplayId / Gender / ObjectScale / HideHelm / HideCloak, mirroring AzerothCore:
        /// the newest TRANSFORM (56) aura -> WowFormModels.TransformDisplay(misc creature entry); else a Night Elf ghost -> the wisp
        /// (Player::BuildPlayerRepop casts 20584 for Night Elves, Player.cpp:4510-4513; 20584 = TRANSFORM 12861, measured); else the
        /// form -> WowFormModels.ModelForForm (Unit::GetModelForForm); else the race's own display. Offline characters have no gender
        /// and no hair/skin colour (ours: male, the "255" default form models) - said once.
        /// </summary>
        partial void RefreshAvatarLook(PlayerState p)
        {
            if (p == null) return;
            p.Gender = 0;
            p.ObjectScale = 1f;
            p.HideHelm = false;
            p.HideCloak = false;
            p.NativeDisplayId = WowFormModels.NativeDisplay(_files, p.Race, 0);
            int display = 0, formSpell = 0;
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                OfflineAura au = _me.Auras[a];
                if (au == null || au.Spell == null) continue;
                SpellEffectInfo eff;
                if (display == 0 && au.Has(SpellAura.Transform, out eff)) display = WowFormModels.TransformDisplay(eff.MiscValue);
                if (formSpell == 0 && _form != 0 && au.Has(SpellAura.ModShapeshift, out eff) && eff.MiscValue == _form) formSpell = au.Spell.Id;
            }
            if (display == 0 && p.Ghost && p.Race == 4) display = WowFormModels.TransformDisplay(12861);
            if (display == 0 && _form != 0) display = WowFormModels.ModelForForm(_files, _form, formSpell, p.Race);
            p.DisplayId = display != 0 ? display : p.NativeDisplayId;
            if (!_avLimitsSaid)
            {
                _avLimitsSaid = true;
                WowLog.Info("Forms: offline looks (ours) - no gender (male) and no hair/skin colour, so AzerothCore's default form models " +
                            "(player_shapeshift_model CustomizationID 255) and male animations / helms; costumes and holiday transforms do not exist offline");
            }
            if (p.DisplayId != _avLastDisplay)
            {
                _avLastDisplay = p.DisplayId;
                WowLog.Info("Forms: offline character drawn as display " + p.DisplayId + " (native " + p.NativeDisplayId + ", form " + _form +
                            (formSpell != 0 ? " spell " + formSpell : "") + (p.Ghost ? ", ghost" : "") + ")");
            }
        }
    }
}
