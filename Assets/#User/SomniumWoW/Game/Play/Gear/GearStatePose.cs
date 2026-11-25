// States fix 2026-10-04 (the user: "When someone dies, they are still stood up in Somnium, make them lay down" + "add the rest of the
// animations, like drinking too!"): WoW's lying / sitting / kneeling states shown with the Somnium client's OWN pose system.
using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player.Components;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>What a WoW character is doing that changes its whole body (ours: the names the log and the poses use).</summary>
    public enum GearBodyState { None, Dead, Ghost, Sit, Eat, Drink, Sleep, Kneel, Chair, Stunned, Fishing, Channel, EatOrDrink }

    /// <summary>
    /// The local avatar's body state as a Somnium client pose (SomniumSpace.Bridge ISomniumPlayerPoses.SetPose - the call MountSeat already
    /// ships). PoseType values MEASURED by reflection on SomniumSpace.Bridge.dll: SittingStraight 0, SittingCrossedLegs 1, Meditation 2,
    /// SittingKneesApart 3, Kneeling 4, Squat 5, ProneForward 6, ProneRelax 7, LayDownBack 8, SittingFloor 9, SittingCoolOnFloor 10,
    /// None -1. The client (E:/Somnium Space/Main Build: PosesFeature.SetCurrentPoseByValue) writes AnimFloat.POSE_VALUE + enters
    /// PlayerStateType.POSE -> AvatarStateType.CUSTOM_POSE; AvatarFeature.PoseValue / CurrentAvatarState are what the remote
    /// NonXR/XRAvatarFeatureSimulator read, so EVERY viewer sees the pose without our record, desktop and VR wearers alike. The pose clips
    /// (AvatarSystem/Animations/Poses/FullAnimated, blend tree thresholds 0-10 in NonXRAvatarIK.controller / XRAvatarIK.controller) lie on
    /// the floor: "Prone forward" body centre RootT.y 0.06 with the original height kept; "Lay down back" root height from the feet.
    /// Our choice per state (ours; WoW's own animation in brackets, AnimationData measured):
    ///   dead, not released [1 Death, held at its last frame - HumanMale ends face forward, head 0.84 m in front of the hips, 0.10 m up]
    ///       -> ProneForward;
    ///   sleep (stand state 3) [100 Sleep, on the back] -> LayDownBack;
    ///   sit (1), eating, drinking [97 SitGround - cross-legged on the ground, HumanMale's left foot crosses under the right knee]
    ///       -> Meditation (the client's cross-legged floor pose, body centre 0.26); the eat / drink arms (61 EmoteEat) come from our
    ///       retarget (GearPoser arms only, desktop wearers);
    ///   kneel (8) [115 KneelLoop] -> Kneeling;
    ///   a chair (2, 4, 5, 6) [102-104 SitChair*] -> SittingStraight (AzerothCore puts the player on the chair's spot, GameObject.cpp:1624-1626).
    /// Held EVERY frame like MountSeat.KeepSitting (the client drops a pose on its cancel action, an avatar reload or a scale change);
    /// left once with SetPose(None) when the state ends - only a pose WE set (a pose the user picked from the client's menu is theirs).
    /// Steps aside while a ride / taxi / vehicle / forced move owns the avatar (MountSeat holds its own SittingStraight; after its Release
    /// the state's pose comes back next frame). A sitting / kneeling / sleeping avatar that walks leaves the pose at once (ours: the
    /// realm stands it up with the movement, MovementHandler.cpp:502-503, one round trip later; MoveRelease m of drift). VR: the client's XRPosesFeature lowers the
    /// view to the pose's head height (XR prefab PoseHeightDelta: ProneForward 0.135, LayDownBack 0.096, Meditation 0.432, Kneeling
    /// 0.652, SittingStraight 0.727 of the avatar height) - the wearer keeps looking around with the headset; others see the posed body.
    /// </summary>
    public sealed class GearStatePose
    {
        public const float MoveRelease = 0.5f;      // ours: m the avatar's root may drift from where a sitting / kneeling / sleeping pose began
        public const float AnchorSeconds = 1f;      // ours: the drift anchor follows the root this long after a pose begins (the client's VR pose
                                                    // entry moves the root under the head once, XRPosesFeature.SetPlayerToPose)
        public const float SayEvery = 2f;           // ours: one state line per this many seconds at most (the rest counted)
        public const float AuraScanEvery = 0.25f;   // ours: the eat / drink aura scan interval

        private readonly string _who;
        private GearBodyState _state = GearBodyState.None, _said = GearBodyState.None;
        private bool _held, _poseOk = true, _suppressed;
        private PoseType _pose = PoseType.None;
        private float _saidAt = -100f, _scanAt = -100f;
        private int _quiet;
        private int _consuming;   // 1 eat, 2 drink (the aura scan)
        private Vector3 _anchor;
        private float _anchorUntil;

        public GearStatePose(string who) { _who = who; }

        /// <summary>The state this frame (after Tick).</summary>
        public GearBodyState State { get { return _state; } }

        /// <summary>True while the client pose we set is held (GearLocal: the WoW retarget then poses the arms only).</summary>
        public bool Holding { get { return _held; } }

        /// <summary>The body state of a character from its PlayerState (consuming: 1 eat / 2 drink; fishing: its channel plays 134).</summary>
        public static GearBodyState Of(PlayerState p, int consuming, bool fishing)
        {
            if (p == null) return GearBodyState.None;
            if (p.Dead) return p.Ghost ? GearBodyState.Ghost : GearBodyState.Dead;
            switch (p.StandState)
            {
                case 1: return consuming == 1 ? GearBodyState.Eat : consuming == 2 ? GearBodyState.Drink : GearBodyState.Sit;
                case 2: case 4: case 5: case 6: return GearBodyState.Chair;
                case 3: return GearBodyState.Sleep;
                case 7: return GearBodyState.Dead;
                case 8: return GearBodyState.Kneel;
            }
            if (p.Stunned) return GearBodyState.Stunned;
            if (p.CastingSpellId != 0 && p.Channeling) return fishing ? GearBodyState.Fishing : GearBodyState.Channel;
            return GearBodyState.None;
        }

        /// <summary>The channel kit animation of a spell is 134 FishingLoop (Fishing 7620 / 18248 measured: CHANNEL kit -1/134).</summary>
        public static bool IsFishing(WowFiles files, int spellId)
        {
            if (files == null || spellId <= 0) return false;
            try
            {
                SomniumSpace.Worlds.SomniumWoW.Models.SpellVisualDb.SpellFx fx = SomniumSpace.Worlds.SomniumWoW.Models.SpellVisualDb.Get(files).Spell(spellId);
                return fx != null && fx.Visual != null && fx.Visual.Channel != null && fx.Visual.Channel.Anim == GearWowAnim.FishingLoop;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The client pose of a state (ours, see the class summary); None = no pose.</summary>
        public static PoseType PoseOf(GearBodyState s)
        {
            switch (s)
            {
                case GearBodyState.Dead: return PoseType.ProneForward;
                case GearBodyState.Sleep: return PoseType.LayDownBack;
                case GearBodyState.Sit: case GearBodyState.Eat: case GearBodyState.Drink: case GearBodyState.EatOrDrink: return PoseType.Meditation;
                case GearBodyState.Kneel: return PoseType.Kneeling;
                case GearBodyState.Chair: return PoseType.SittingStraight;
                default: return PoseType.None;
            }
        }

        /// <summary>The log words of a state ("lies down (death)", "drinks" ...).</summary>
        public static string Words(GearBodyState s)
        {
            switch (s)
            {
                case GearBodyState.Dead: return "lies down (death)";
                case GearBodyState.Ghost: return "stands up again (ghost - Release Spirit)";
                case GearBodyState.Sit: return "sits on the ground";
                case GearBodyState.Eat: return "eats (sitting)";
                case GearBodyState.Drink: return "drinks (sitting)";
                case GearBodyState.EatOrDrink: return "eats / drinks (sitting; their record carries 61 EmoteEat for both)";
                case GearBodyState.Sleep: return "sleeps";
                case GearBodyState.Kneel: return "kneels";
                case GearBodyState.Chair: return "sits on a chair";
                case GearBodyState.Stunned: return "is stunned";
                case GearBodyState.Fishing: return "fishes";
                case GearBodyState.Channel: return "channels a spell";
                default: return "stands";
            }
        }

        /// <summary>1 eating / 2 drinking: offline PlayerState.Consuming; online an aura of ours with AURA_INTERRUPT_FLAG_NOT_SEATED
        /// (0x40000) and SPELL_AURA_MOD_REGEN 84 = food / MOD_POWER_REGEN 85 = drink (AzerothCore Player.cpp:1869-1874 regenerates by
        /// exactly these; Unit.cpp:4562-4564 "Sitdown on apply aura req seated" sets UNIT_STAND_STATE_SIT). Measured: Food 433 auraInt
        /// 0x40080 aura 84, Drink 430 auraInt 0x40080 aura 85.</summary>
        public int Consuming(PlayerState p, WowFiles files)
        {
            if (p == null) return 0;
            if (p.Consuming != 0) return p.Consuming;
            float now = Time.unscaledTime;
            if (now < _scanAt) return _consuming;
            _scanAt = now + AuraScanEvery;
            _consuming = 0;
            if (files == null || p.StandState != 1 || p.Auras == null) return 0;
            SpellDb db;
            try { db = SpellDb.Get(files); } catch (Exception) { return 0; }
            for (int i = 0; i < p.Auras.Count; i++)
            {
                AuraView a = p.Auras[i];
                if (a == null || a.SpellId <= 0) continue;
                SpellInfo s = db.Find(a.SpellId);
                if (s == null || (s.AuraInterruptFlags & 0x40000) == 0) continue;
                if (s.HasAura(SpellAura.ModPowerRegen)) { _consuming = 2; break; }
                if (s.HasAura(SpellAura.ModRegen)) _consuming = 1;
            }
            return _consuming;
        }

        /// <summary>
        /// Once per frame for the LOCAL avatar: decides the state, holds / leaves the client pose and says the change. owned = a ride,
        /// taxi, vehicle or forced move owns the avatar now; root = the avatar model's root (GearBody.ModelFeet).
        /// </summary>
        public void Tick(PlayerState p, WowFiles files, bool owned, Vector3 root, bool vr = false)
        {
            int consuming = Consuming(p, files);
            bool fishing = p != null && p.Channeling && p.CastingSpellId != 0 && IsFishing(files, p.CastingSpellId);
            GearBodyState s = Of(p, consuming, fishing);
            float now = Time.unscaledTime;
            if (s != _state)
            {
                _state = s;
                _suppressed = false;
                _anchorUntil = now + AnchorSeconds;
            }
            PoseType want = PoseOf(s);
            // Lead fix 2026-10-04 (the user: "in vr when eating or drinking it instatnly gets cancclled, probably because it's not staying in
            // sitting pose, for VR remove that"): in VR the wearer's own body sits - no client pose for sitting, eating or drinking (the
            // client moved the root under the head when the pose began, XRPosesFeature.SetPlayerToPose, which went out as a move and the
            // realm stood us up; OnlineSession.SeatedHold now holds such sways too). Death, sleep, kneel and chairs keep their pose.
            bool vrOwn = vr && (s == GearBodyState.Sit || s == GearBodyState.Eat || s == GearBodyState.Drink || s == GearBodyState.EatOrDrink);
            if (vrOwn) want = PoseType.None;
            if (now < _anchorUntil || want == PoseType.None) _anchor = root;
            else if (s != GearBodyState.Dead && !_suppressed)
            {
                Vector3 d = root - _anchor;
                d.y = 0f;
                if (d.magnitude > MoveRelease) _suppressed = true;   // walked off: stand up now (the realm follows with the movement)
            }
            if (_suppressed) want = PoseType.None;
            if (owned)
            {
                // the ride's seat holds its own pose (MountSeat.KeepSitting): ours is simply not held meanwhile - no SetPose(None) that
                // would cancel the seat's pose for a frame
                _held = false;
                _pose = PoseType.None;
                Say(s, "while the ride owns the avatar (no pose of ours)");
                return;
            }
            if (want != PoseType.None) Hold(want);
            else if (_held) Leave();
            Say(s, want != PoseType.None ? "the client's pose " + want + " (held every frame)" : vrOwn ? "VR: your own body (no client pose)" : _suppressed ? "walked off - pose left" : null);
        }

        /// <summary>Leaves our pose now (the avatar is gone, the session ended).</summary>
        public void Release()
        {
            if (_held) Leave();
            _state = GearBodyState.None;
        }

        private static ISomniumPlayerPoses Poses()
        {
            try { return SomniumBridge.PlayersContainer?.LocalPlayer?.Features?.Poses; }
            catch (Exception) { return null; }
        }

        private void Hold(PoseType pose)
        {
            if (!_poseOk) return;
            ISomniumPlayerPoses poses = Poses();
            if (poses == null) return;
            try { poses.SetPose(pose); }
            catch (Exception e)
            {
                _poseOk = false;
                WowLog.Warn("Gear: the client threw on SetPose(" + pose + ") for " + _who + " (" + e.GetType().Name + ": " + e.Message + ") - the state poses stay off this session");
                return;
            }
            _held = true;
            _pose = pose;
        }

        private void Leave()
        {
            _held = false;
            _pose = PoseType.None;
            ISomniumPlayerPoses poses = Poses();
            if (poses == null) return;
            try { poses.SetPose(PoseType.None); }
            catch (Exception e) { WowLog.Warn("Gear: leaving the state pose of " + _who + " threw " + e.GetType().Name + ": " + e.Message); }
        }

        private void Say(GearBodyState s, string how)
        {
            if (s == _said) return;
            float now = Time.unscaledTime;
            if (now - _saidAt < SayEvery) { _quiet++; return; }
            _said = s;
            _saidAt = now;
            WowLog.Info("Gear: " + _who + " " + Words(s) + (how != null ? " - " + how : "") + (_quiet > 0 ? " (" + _quiet + " quicker change(s) not logged)" : ""));
            _quiet = 0;
        }

        // ------------------------------------------------------------------ viewers (GearRemote)

        /// <summary>A REMOTE wearer's state from their record: dead / ghost from the status and look, the rest from their WoW loop
        /// (int 32: GearWowAnim.StandLoop ids, 14 Stun, 134 FishingLoop). Their client pose arrives through the Somnium client itself.</summary>
        public static GearBodyState OfRecord(bool dead, bool ghost, int loop, int oneShot)
        {
            if (dead) return ghost ? GearBodyState.Ghost : GearBodyState.Dead;
            switch (loop)
            {
                case GearWowAnim.SitGround: return oneShot == GearWowAnim.EmoteEat ? GearBodyState.EatOrDrink : GearBodyState.Sit;
                case GearWowAnim.Sleep: return GearBodyState.Sleep;
                case GearWowAnim.KneelLoop: return GearBodyState.Kneel;
                case GearWowAnim.SitChairLow: case GearWowAnim.SitChairMed: case GearWowAnim.SitChairHigh: return GearBodyState.Chair;
                case GearWowAnim.Death: return GearBodyState.Dead;
                case GearWowAnim.Stun: return GearBodyState.Stunned;
                case GearWowAnim.FishingLoop: return GearBodyState.Fishing;
            }
            return GearBodyState.None;
        }
    }
}
