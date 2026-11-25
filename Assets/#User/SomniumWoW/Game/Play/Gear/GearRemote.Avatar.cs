// iteration 11 - owner avatar (proposal-avatar.md 6.F / 6.C / 6.A): another Somnium player's look (forms, transforms, ghost,
// stealth, shadowform - their record int 31, online fallback from the server's copy of an older build's character), their WoW
// full-body animation (int 32 -> a puppet of their race / gender, desktop avatars) and their immersive armour (ints 32-43).
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Forms;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    public sealed partial class GearRemote
    {
        private FormView _form;
        private GearPuppet _puppet;
        private Game.Armour.ArmourPieces _armour;
        private Game.Armour.ArmourShell _shell;   // A2: their Full (experimental) armour shell
        private WowFiles _files;
        private AvatarLook _look = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f };
        private int _animCounter = -1, _formShotCounter = -1;
        private float _shotAt = -100f;
        private bool _pendingFresh;
        private Vector3 _avLast, _avVel;
        private bool _avHave, _avAirWas;
        private string _armourSaid, _fallbackSaid;
        private const float ArmourHoldSeconds = 1f;   // lead fix 2026-10-04: armour kept in place this long after their Animator went away (ours)
        // fix 2026-10-04 (remote animation): the start / stop line of their WoW combat animation, at most one per WowSayEvery s
        private const float WowSayEvery = 5f;   // ours
        private string _wowWhy, _wowSaid, _wowSeen;
        // states fix 2026-10-04: their body state for the log (one line per change, at most one per GearStatePose.SayEvery s)
        private GearBodyState _bodySaid = GearBodyState.None;
        private float _bodySaidAt = -100f;
        private int _bodyQuiet;
        private float _wowSaidAt = -100f;
        private int _wowChanges;

        /// <summary>Lead (2026-10-04): their fit of slot i from the record (placement int + the tilt ints).</summary>
        private static Game.Armour.PieceAdjust RemoteAdjust(WowArmourRecord r, int i)
        {
            Game.Armour.PieceAdjust pa = Game.Armour.ArmourAdjust.Unpack(r.Adj(i));
            pa.Tilt = Game.Armour.ArmourAdjust.UnpackTilt(i < 5 ? r.Tilts0 : r.Tilts1, i % 5);
            pa.TaperTop = Game.Armour.ArmourAdjust.UnpackTaper(r.Tapers0, r.Tapers1, r.Tapers2, i, true);   // lead 2026-10-04
            pa.TaperBottom = Game.Armour.ArmourAdjust.UnpackTaper(r.Tapers0, r.Tapers1, r.Tapers2, i, false);
            pa.TopZ = Game.Armour.ArmourAdjust.UnpackEnd(r.Ends0, r.Ends1, r.Ends2, r.Ends3, i, 0);   // lead 2026-10-04
            pa.TopY = Game.Armour.ArmourAdjust.UnpackEnd(r.Ends0, r.Ends1, r.Ends2, r.Ends3, i, 1);
            pa.BottomZ = Game.Armour.ArmourAdjust.UnpackEnd(r.Ends0, r.Ends1, r.Ends2, r.Ends3, i, 2);
            pa.BottomY = Game.Armour.ArmourAdjust.UnpackEnd(r.Ends0, r.Ends1, r.Ends2, r.Ends3, i, 3);
            return pa;
        }
        private readonly int[] _enchantShown = { -1, -1, -1 };   // spellfx hook 7: the glow shown on each weapon (GearRemote.Tick)

        private void EnsureAvatarParts()
        {
            if (_form != null) return;
            _form = new FormView(Body.Label, false);
            _puppet = new GearPuppet(Body.Label);
            _armour = new Game.Armour.ArmourPieces(Body.Label);
            _shell = new Game.Armour.ArmourShell(Body.Label);
        }

        /// <summary>
        /// Their look: the record's int 31 (an iteration-11 build says its look, Own included). An older build (gender not said in
        /// int 32) playing online on our realm: the server's copy of their character (AvatarLooks.FromEntity - DisplayId vs
        /// NativeDisplayId). Settings "Show forms on my avatar" off = the old behaviour for everyone (a vehicle's Hidden stays).
        /// </summary>
        private AvatarLook LookOf(in WowGearState g)
        {
            var l = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f, Form = g.Form };
            if (g.Status != WowNetStatus.Playing) return l;
            if (g.Look.Kind > 0 && g.Look.Kind <= (int)AvatarLookKind.Hidden)
            {
                l.Kind = (AvatarLookKind)g.Look.Kind;
                l.DisplayId = l.Kind == AvatarLookKind.Creature ? g.Look.DisplayId : 0;
                l.Scale = g.Look.Scale > 0.01f ? g.Look.Scale : 1f;
            }
            else if (g.Armour.Gender < 0 && g.CharacterGuid != 0)
            {
                IWowSession s = WowPlay.Session;
                if (s != null && !(s is SomniumSpace.Worlds.SomniumWoW.Play.Offline.OfflineSession))
                {
                    EntityState e = null;
                    try { e = s.Find(g.CharacterGuid); } catch (System.Exception) { e = null; }
                    if (e != null)
                    {
                        l = AvatarLooks.FromEntity(e);
                        l.Form = g.Form;
                        string said = l.Kind.ToString() + l.DisplayId;
                        if (said != _fallbackSaid && l.Kind != AvatarLookKind.Own)
                        {
                            _fallbackSaid = said;
                            WowLog.Info("Forms: " + Body.Label + " runs an older build - look from the realm's copy of their character: " + AvatarLooks.Describe(l));
                        }
                    }
                }
            }
            if (!WowSettings.ShowForms && l.Kind != AvatarLookKind.Hidden) l = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f, Form = g.Form };
            return l;
        }

        /// <summary>Their WoW animation ids (int 32; an older record: their WowMotion / WowHold mapped) on a puppet of their race /
        /// gender, on a desktop avatar whose WEARER chose the WoW style (int 32 bit 28; fix 2026-10-04: it was this viewer's own
        /// WowSettings.AvatarCombatStyle + "Avatar combat animations", so a viewer who changed them saw nothing of the wearer's choice).
        /// Enabled false = the simple arm motions as before. _wowWhy says why not (the start / stop line, SayWow).</summary>
        private GearWowPose WowPose(in WowGearState g, bool desktop)
        {
            var w = default(GearWowPose);
            _wowWhy = null;
            bool said = g.Armour.Gender >= 0;
            int loop = said ? g.Anim.Loop : GearWowAnim.FromHold(g.Hold);
            int shot = said ? g.Anim.OneShot : GearWowAnim.FromMotion(g.Motion);
            int counter = said ? g.Anim.Counter : g.MotionCounter & 0xF;
            float now = Time.time;
            SayBody(g, said ? loop : 0, shot == GearWowAnim.EmoteEat && now - _shotAt < 1.5f * GearAnimLocal.EatEvery ? shot : 0);   // eating = a 61 within the last 12 s
            // states fix 2026-10-04: a lying / sitting / kneeling loop is shown by THEIR client pose (the Somnium client syncs it): our
            // retarget adds only the arms of their one-shots (61 EmoteEat while eating) - never legs, hips or torso over the pose
            bool poseLoop = said && GearWowAnim.IsPoseLoop(loop);
            if (poseLoop) loop = 0;
            bool fresh = false;
            if (counter != _animCounter)
            {
                fresh = _animCounter >= 0 && shot > 0;
                _animCounter = counter;
                if (fresh) { _shotAt = now; _pendingFresh = true; }
            }
            bool swim = said && GearWowAnim.IsSwimLoop(loop);   // lead 2026-10-26: a swimmer kicks (VR: legs only) whatever their combat style
            _wowWhy = !desktop && !swim ? "off (VR: their own hands)" : g.Dead ? "off (dead)" : g.Anim.SimpleArms && !swim ? "off (they chose the simple arm motions)" :
                      _look.HidesAvatar ? "off (their look hides the avatar)" : !Body.Humanoid ? "off (no humanoid avatar found)" : _files == null ? "off (no game files here)" : null;
            if (_wowWhy != null)
            {
                _pendingFresh = false;
                return w;
            }
            bool need = loop != 0 || (shot > 0 && now - _shotAt < GearLocal.PuppetShotKeep);
            if (need) { if (!_puppet.Ensure(_files, g.Race, said ? g.Armour.Gender : 0, _parent)) { _wowWhy = "off (no WoW puppet yet for race " + g.Race + ")"; return w; } }
            else if (!_puppet.Ready) { _wowWhy = "idle"; return w; }
            _puppet.SampleNow();
            bool play = _pendingFresh && now - _shotAt < 0.3f;
            _pendingFresh = false;
            _puppet.Play(loop, shot, play);
            w.Enabled = true;
            w.On = need || _puppet.Active;
            w.ArmsOnly = poseLoop;
            w.S = _puppet.Sample;
            // a forced move's gait (Run 5 / Walk 4 / Stun 14 / jumps 37-40) is the only time the WoW loop is a locomotion id:
            // their legs follow it while their root moves (motion hook)
            w.ForcedLegs = loop == 4 || loop == 5 || loop == 14 || (loop >= 37 && loop <= 40) || swim;
            w.Swim = swim;
            w.LegsOnly = swim && !desktop;
            return w;
        }

        /// <summary>States fix 2026-10-04: "Gear: <who> lies down (death)" etc. when their state changes (dead / ghost from the record and
        /// their look, the rest from their WoW loop; GearStatePose.OfRecord), rate-limited like GearStatePose.</summary>
        private void SayBody(in WowGearState g, int loop, int shot)
        {
            GearBodyState b = GearStatePose.OfRecord(g.Dead, g.Dead && _look.Kind == AvatarLookKind.GhostShell, loop, shot);
            if (b == _bodySaid) return;
            float now = Time.unscaledTime;
            if (now - _bodySaidAt < GearStatePose.SayEvery) { _bodyQuiet++; return; }
            bool first = _bodySaidAt < 0f && b == GearBodyState.None;
            _bodySaid = b;
            _bodySaidAt = now;
            if (first) return;
            WowLog.Info("Gear: " + Body.Label + " " + GearStatePose.Words(b) + (GearStatePose.PoseOf(b) != SomniumSpace.Bridge.Player.Components.PoseType.None
                        ? " - their client pose " + GearStatePose.PoseOf(b) + " comes through Somnium" : "") + (_bodyQuiet > 0 ? " (" + _bodyQuiet + " quicker change(s) not logged)" : ""));
            _bodyQuiet = 0;
        }

        /// <summary>Fix 2026-10-04: after Poser.Apply - one line when their WoW combat animation starts or stops showing on their
        /// avatar here (with the avatar copy posed, so a wrong copy shows in Player.log), at most one per WowSayEvery s; a change
        /// inside the window is said when it ends (the latest state), with how many changes it covered.</summary>
        private void SayWow(bool enabled, bool hidden)
        {
            string state = hidden ? "off (not drawn here)" : enabled && Poser.WowWeight > 0.001f ? "on" : _wowWhy ?? "idle";
            if (state != _wowSeen) { _wowSeen = state; _wowChanges++; }
            if (state == _wowSaid) return;
            float now = Time.unscaledTime;
            if (now - _wowSaidAt < WowSayEvery) return;
            bool first = _wowSaid == null;
            _wowSaid = state;
            int skipped = _wowChanges - 1;
            _wowChanges = 0;
            if (first && state != "on") return;   // nothing started yet: no line (and no wait) for a player who just appeared
            _wowSaidAt = now;
            Animator a = Body.Animator;
            WowLog.Info("Gear: " + Body.Label + " WoW combat animation " + state +
                        (state == "on" && a != null ? " - posing their avatar copy '" + a.name + "' (" + (GearAvatarRoots.KindOf(a) ?? "no client root") + ")" : "") +
                        (skipped > 0 ? " (" + skipped + " earlier change(s) within " + WowSayEvery.ToString("0") + " s not logged)" : ""));
        }

        /// <summary>After the poser and the weapons: their form model / shell, its gait and one-shots, their armour.</summary>
        private void PlaceAvatarExtras(in WowGearState g, Vector3 draw, in GearLocalView v)
        {
            float dt = Time.deltaTime, now = Time.time;
            Vector3 feet = Body.ModelFeet;
            if (_avHave && dt > 1e-5f)
            {
                Vector3 vel = (feet - _avLast) / dt;
                if (vel.sqrMagnitude < 3600f) _avVel = Vector3.Lerp(_avVel, vel, 1f - Mathf.Exp(-dt * FallbackSpeedRate));
            }
            _avLast = feet;
            _avHave = true;

            var f = new FormFrame { Feet = feet + draw, Forward = Body.Forward, FirstPerson = false, Camera = v.Eye, Dt = dt, HandColour = GearNameplate.ClassColour(g.Class) };
            float prio = Distance < float.MaxValue ? Distance * Distance : 100f;
            _form.Apply(_look, Body.Animator, f, v.GameRoot != null ? v.GameRoot : _parent, _files, prio);
            FormModel m = _form.Model;
            int shot = g.Armour.Gender >= 0 ? g.Anim.OneShot : GearWowAnim.FromMotion(g.Motion);
            int counter = g.Armour.Gender >= 0 ? g.Anim.Counter : g.MotionCounter & 0xF;
            if (m != null)
            {
                if (_formShotCounter >= 0 && counter != _formShotCounter) GearLocal.PlayFormShot(m, shot, now);
                RideMode mode = g.Look.Swimming ? RideMode.Swimming : g.Look.Flying ? RideMode.Flying : g.Look.Airborne ? RideMode.Airborne : RideMode.Ground;
                Vector3 fwd = Body.Forward; fwd.y = 0f;
                Vector3 planar = new Vector3(_avVel.x, 0f, _avVel.z);
                float forward = planar.magnitude * (fwd.sqrMagnitude > 1e-6f && Vector3.Dot(planar, fwd) < 0f ? -1f : 1f);
                bool air = g.Look.Airborne || g.Look.Flying;
                int castLoop = g.Cast.Casting && g.Armour.Gender >= 0 ? g.Anim.Loop : 0;
                // a creature look is never a corpse (WoW removes forms / transforms at death; the NE ghost wisp floats): no death hold
                m.Animate(mode, forward, _avVel.y, MountCatalog.BaseRun * WowSpace.MetresPerYard, air && !_avAirWas, !air && _avAirWas, false, castLoop, now, g.InCombat || g.AutoAttacking);
                _avAirWas = air;
                // their spell effects come from the creature's own points (attachments, SlotFallback, else the model's height)
                Cast.OverrideFrom(m);
            }
            _formShotCounter = counter;

            // immersive armour: the wearer's choice (int 32 bits 24-25) on their avatar while it is drawn as itself
            int mode2 = Game.Armour.ArmourGate.Mode(g.Armour.Mode);   // lead 2026-10-03: Full only once finished
            if (mode2 >= 1 && !_look.HidesAvatar && Body.Humanoid && _files != null)
            {
                _armour.Want(_files, g.Armour.Head, g.Armour.Shoulders, g.Race, g.Armour.Gender, v.GameRoot != null ? v.GameRoot : _parent);
                for (int ai = 0; ai < 3; ai++) _armour.Adjust[ai] = RemoteAdjust(g.Armour, ai);   // lead 2026-10-03/04: their placement and tilt
                _armour.Place(Body.Animator, Body, draw, g.Armour.Fit, g.Armour.HideHelm, false, null);
                if (mode2 >= 2)
                {
                    // A2: the rest of their armour as a shell (ints 33-43 by WowArmourRecord.EquipSlots; their gender, Hide Cloak, fit)
                    int[] items = _shell.Items;
                    for (int i = 0; i < WowArmourRecord.SlotCount; i++) items[WowArmourRecord.EquipSlots[i]] = g.Armour.Slot(i);
                    _shell.Want(_files, g.Armour.Gender, g.Armour.HideCloak, v.GameRoot != null ? v.GameRoot : _parent);
                    for (int ai = 3; ai < Game.Armour.ArmourAdjust.Slots; ai++) _shell.Adjust[ai] = RemoteAdjust(g.Armour, ai);   // lead 2026-10-04
                    _shell.SetWearerShape(g.Armour.Shape0, g.Armour.Shape1, g.Armour.Shape2, g.Armour.Shape3, g.Armour.Shape4);   // lead 2026-11-04: their own measurement
                    _shell.Place(Body.Animator, Body.ModelFeet, Body.Forward, draw, g.Armour.Fit, false);
                }
                else _shell.Hide();
                SayArmour("Armour: " + Body.Label + " wears immersive armour (" + (mode2 >= 2 ? "full, experimental: helm " : "helm ") + g.Armour.Head + ", shoulders " + g.Armour.Shoulders +
                          (mode2 >= 2 ? ", chest " + g.Armour.Chest + ", legs " + g.Armour.Legs + ", hands " + g.Armour.Hands + ", feet " + g.Armour.Feet + ", back " + g.Armour.Back : "") +
                          ", fit " + g.Armour.Fit.ToString("0.00") + ")");
            }
            else if (mode2 >= 1 && !_look.HidesAvatar && !Body.Humanoid && Time.unscaledTime - Body.LostAt < ArmourHoldSeconds)
            {
                // lead fix 2026-10-04: the client swapped their avatar copy (its culling stand-in in or out) - the armour stays where it
                // was for a moment instead of vanishing until the next copy is found (GearBody looks again every 0.2 s meanwhile)
            }
            else
            {
                _armour.Hide();
                _shell.Hide();
                if (mode2 < 1) SayArmour(null);
            }
            _puppet.Tick();
        }

        private void SayArmour(string line)
        {
            if (line == _armourSaid) return;
            _armourSaid = line;
            if (line != null) WowLog.Info(line);
        }

        /// <summary>The plate over a creature look (attachment 18 PlayerName, else the bounds top + 0.3 m), else over the head.</summary>
        private Vector3 PlatePoint(Vector3 overHead)
        {
            FormModel m = _form != null ? _form.Model : null;
            return m != null ? m.NamePoint() : overHead;
        }

        /// <summary>They are not drawn here: our parts hide FIRST (the form hider gives its renderers back), then GearHider hides all.</summary>
        private void HideAvatarExtras()
        {
            if (_form == null) return;
            _form.Hide();
            _armour.Hide();
            _shell.Hide();
            _puppet.Tick();
        }

        private void DestroyAvatarExtras()
        {
            if (_form == null) return;
            _form.Clear();
            _puppet.Destroy();
            _armour.Destroy();
            _shell.Destroy();
        }
    }
}
