// iteration 11 - owner avatar (proposal-avatar.md 6.C / 6.A): the local player's WoW puppet (full-body desktop combat animation),
// immersive armour, the armour record, the emote call and vrcast's hook 11 (TryBlade / NoteCastHand).
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    public sealed partial class GearLocal
    {
        /// <summary>Ours: a one-shot keeps the puppet wanted this long after it started (s).</summary>
        public const float PuppetShotKeep = 2f;

        private readonly GearPuppet _puppet = new GearPuppet("the local player");
        private readonly Game.Armour.ArmourPieces _armour = new Game.Armour.ArmourPieces("the local player");

        /// <summary>Lead (2026-10-04, the Armour page's fitting view): the local player's immersive armour renderers shown now (helm,
        /// shoulders, the full shell), appended.</summary>
        public void CollectArmourRenderers(List<Renderer> into)
        {
            _armour.CollectRenderers(into);
            _shell.CollectRenderers(into);
        }
        private readonly Game.Armour.ArmourShell _shell = new Game.Armour.ArmourShell("the local player");   // A2: Full (experimental)
        private int _puppetCounter = -1;
        private readonly int[] _shapeOut = new int[Game.Armour.ArmourShell.ShapeInts];   // lead 2026-11-04
        private int _preloadRace = -1, _preloadGender = -1;
        private string _armourSaid;

        /// <summary>
        /// The WoW full-body pose of this frame (desktop, the WoW style chosen, the avatar drawn): the puppet of our race / gender
        /// samples what its M2Animator played this frame, then gets this frame's ids (one frame behind - ours). Without a puppet
        /// (no game files, a model that cannot be mapped) Enabled stays false and the simple arm motions play as before.
        /// </summary>
        private GearWowPose WowBody(PlayerState p, bool dead)
        {
            var w = default(GearWowPose);
            if (p == null) return w;
            if (_files != null && p.Race > 0 && (p.Race != _preloadRace || p.Gender != _preloadGender))
            {
                _preloadRace = p.Race;
                _preloadGender = p.Gender;
                GearPuppet.Preload(_files, p.Race, p.Gender);
            }
            GearWowAnim a = Anim.Wow;
            bool swim = GearWowAnim.IsSwimLoop(a.Loop);   // lead 2026-10-26: swimming animates VR legs and any combat style
            if (dead || !AnimationsOn || Look.HidesAvatar || !Body.Humanoid) return w;
            if (!swim && (_xr || WowSettings.AvatarCombatStyle != 1)) return w;
            float now = Time.time;
            // states fix 2026-10-04: a lying / sitting / kneeling loop is the client's pose (GearStatePose) - the puppet plays only the
            // one-shots on it (61 EmoteEat while eating) and the poser moves the arms only
            bool poseLoop = GearWowAnim.IsPoseLoop(a.Loop);
            int loop = poseLoop ? 0 : a.Loop;
            bool need = loop != 0 || (a.OneShot != 0 && now - a.ShotAt < PuppetShotKeep);
            if (need) { if (!_puppet.Ensure(_files, p.Race, p.Gender, GameRoot)) return w; }
            else if (!_puppet.Ready) return w;
            _puppet.SampleNow();
            bool fresh = a.Counter != _puppetCounter && a.OneShot != 0 && now - a.ShotAt < 0.3f;
            _puppetCounter = a.Counter;
            _puppet.Play(loop, a.OneShot, fresh);
            w.Enabled = true;
            w.ArmsOnly = poseLoop;
            w.On = need || _puppet.Active;
            w.S = _puppet.Sample;
            w.ForcedLegs = WowForcedMove.Gait != 0 || swim;
            w.Swim = swim;
            w.LegsOnly = swim && _xr;
            return w;
        }

        /// <summary>After the weapons: immersive armour on our avatar (the wearer's setting; nothing while a look hides the avatar).
        /// 1 = helm + shoulders, 2 = Full (experimental, A2): helm + shoulders + the armour shell.</summary>
        private void PlaceExtras(PlayerState p)
        {
            _puppet.Tick();
            int mode = Game.Armour.ArmourGate.Mode(WowSettings.ImmersiveArmour);
            if (p == null || mode < 1 || Look.HidesAvatar || !Body.Humanoid || _files == null)
            {
                _armour.Hide();
                _shell.Hide();
                SayArmour(p == null ? null : "Armour: the local player's immersive armour " + (mode < 1 ? "off" : Look.HidesAvatar ? "hidden (" + Look.Kind + ")" : "not shown (no humanoid avatar or no game files)"));
                return;
            }
            _armour.Want(_files, DisplayOf(p, 0), DisplayOf(p, 2), p.Race, p.Gender, GameRoot);
            Vector3 eye;
            bool firstPerson = FirstPerson(out eye);
            for (int ai = 0; ai < 3; ai++) _armour.Adjust[ai] = Game.Armour.ArmourAdjust.Local(ai);   // lead 2026-10-03: Armour page
            _armour.HelmInPhotos = WowSettings.HelmInPhotos;
            _armour.Place(Body.Animator, Body, Vector3.zero, WowSettings.ArmourFit, p.HideHelm, _xr, eye);
            if (mode >= 2)
            {
                int[] items = _shell.Items;
                int[] slots = Game.Armour.ArmourShellLook.Slots;
                for (int i = 0; i < slots.Length; i++) items[slots[i]] = DisplayOf(p, slots[i]);
                _shell.Want(_files, p.Gender, p.HideCloak, GameRoot);
                for (int ai = 3; ai < Game.Armour.ArmourAdjust.Slots; ai++) _shell.Adjust[ai] = Game.Armour.ArmourAdjust.Local(ai);   // lead 2026-10-04
                _shell.AllowUprightTimeout = !(p.Dead || p.StandState != 0 || p.Mounted || p.OnTaxi);   // lead 2026-10-04
                _shell.Place(Body.Animator, Body.ModelFeet, Body.Forward, Vector3.zero, WowSettings.ArmourFit, firstPerson);
            }
            else _shell.Hide();
            SayArmour("Armour: the local player's immersive armour on (" + (mode >= 2 ? "full, experimental: helm, shoulders and the armour shell" : "helm and shoulders") +
                      ", fit " + WowSettings.ArmourFit.ToString("0.00") + (p.HideHelm ? ", helm hidden by Hide Helm" : "") + (mode >= 2 && p.HideCloak ? ", cloak hidden by Hide Cloak" : "") + ")");
        }

        private void SayArmour(string line)
        {
            if (line == null || line == _armourSaid) return;
            _armourSaid = line;
            WowLog.Info(line);
        }

        private static int DisplayOf(PlayerState p, int equipSlot)
        {
            ItemView v = p.Equipment != null && p.Equipment.Length > equipSlot ? p.Equipment[equipSlot] : null;
            return v != null && v.DisplayId > 0 ? v.DisplayId : 0;
        }

        private void HideExtras()
        {
            _armour.Hide();
            _shell.Hide();
        }

        private void ClearExtras()
        {
            _puppet.Destroy();
            _armour.Destroy();
            _shell.Destroy();
            _puppetCounter = -1;
            _armourSaid = null;
        }

        /// <summary>Ints 32 bits 22-27 + 33-43: the wearer decides (everyone draws what we chose); the pieces change only with the gear.</summary>
        private WowArmourRecord ArmourRecord(PlayerState p)
        {
            var r = new WowArmourRecord
            {
                Mode = Game.Armour.ArmourGate.Mode(WowSettings.ImmersiveArmour),
                Fit = WowSettings.ArmourFit,
                Gender = p.Gender == 1 ? 1 : 0,
                HideHelm = p.HideHelm,
                HideCloak = p.HideCloak,
            };
            for (int i = 0; i < WowArmourRecord.SlotCount; i++) r.SetSlot(i, DisplayOf(p, WowArmourRecord.EquipSlots[i]));
            for (int ai = 0; ai < Game.Armour.ArmourAdjust.Slots; ai++) r.SetAdj(ai, Game.Armour.ArmourAdjust.PackLocal(ai));   // lead 2026-10-03/04: everyone sees our fit
            r.Tilts0 = Game.Armour.ArmourAdjust.PackTilts(0);
            r.Tilts1 = Game.Armour.ArmourAdjust.PackTilts(5);
            r.Tapers0 = Game.Armour.ArmourAdjust.PackTapers(0);   // lead 2026-10-04
            r.Tapers1 = Game.Armour.ArmourAdjust.PackTapers(1);
            r.Tapers2 = Game.Armour.ArmourAdjust.PackTapers(2);
            r.Ends0 = Game.Armour.ArmourAdjust.PackEnds(0);   // lead 2026-10-04: top / bottom edges
            r.Ends1 = Game.Armour.ArmourAdjust.PackEnds(1);
            r.Ends2 = Game.Armour.ArmourAdjust.PackEnds(2);
            r.Ends3 = Game.Armour.ArmourAdjust.PackEnds(3);
            // lead 2026-11-04: our own good measurement, so every viewer draws our shell exactly as we fitted it
            _shapeOut[0] = _shapeOut[1] = _shapeOut[2] = _shapeOut[3] = _shapeOut[4] = 0;
            if (r.Mode >= 2) _shell.ExportShape(_shapeOut);
            r.Shape0 = _shapeOut[0]; r.Shape1 = _shapeOut[1]; r.Shape2 = _shapeOut[2]; r.Shape3 = _shapeOut[3]; r.Shape4 = _shapeOut[4];
            return r;
        }

        /// <summary>WowGear.PlayAvatarEmote: desktop with the animations on, or any creature look (its own emote if it has it).</summary>
        public bool Emote(int animId)
        {
            bool allowed = (!_xr && AnimationsOn) || Look.Kind == AvatarLookKind.Creature;
            return Anim.Emote(animId, allowed);
        }

        /// <summary>vrcast hook 11: the blade capsule of the weapon in that hand (VR, drawn, not a shield / bow / ranged slot).</summary>
        public bool TryBlade(bool right, out Vector3 a, out Vector3 b, out float radius)
        {
            a = b = Vector3.zero;
            radius = 0f;
            if (!_xr || _hidden) return false;
            int slot = right ? _right : _left;
            GearItem it = ItemOf(slot);
            if (it == null || it.Go == null || !it.Go.activeSelf || it.IsShield || it.IsBow || slot == 17) return false;
            GearPlace.BladeCapsule(it, out a, out b, out radius);
            return true;
        }

        /// <summary>vrcast hook 11: a strike ring hit with that hand counts as a grip release (CastHandFor reports the striking hand).</summary>
        public void NoteCastHand(bool right)
        {
            _gripUpAt[right ? 1 : 0] = Time.unscaledTime;
        }
    }
}
