// iteration 11 - owner avatar (proposal-avatar.md 6.C GearWowAnim): the WoW animation ids a Somnium avatar plays.
using SomniumSpace.Worlds.SomniumWoW.Networking;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// The WoW AnimationData ids of one avatar: a held Loop (combat stance, precast, channel, aim, an emote loop; 0 = the client's own
    /// animation) and one-shots (OneShot + Counter: swings, ability kit animations, parry / dodge / block, wounds, cast releases,
    /// emotes). The local writer (GearAnimLocal) fills it from the session and it goes into our record (WowNetWire int 32); a remote
    /// reader fills it from their record. GearPuppet (desktop full body) and FormModel (creature looks) play it.
    /// Ids (AnimationData.dbc, measured AvAnimNames / AvRigAnims / AvKitAnims on the user's files):
    ///   swings 16 AttackUnarmed, 17 Attack1H, 18 Attack2H, 19 Attack2HL, 85 Attack1HPierce, 87 AttackOff, 88 AttackOffPierce
    ///   (117 AttackUnarmedOff is on none of the 20 race models: off-hand fists use 87); ranged 46 AttackBow, 49 AttackRifle, 107
    ///   AttackThrown, 53 for a wand (ours); stances 25 ReadyUnarmed, 26 Ready1H, 27 Ready2H, 28 Ready2HL, aims 29 ReadyBow, 48
    ///   ReadyRifle, 108 ReadyThrown, 51 for a wand (ours); casts 51/52 precast, 53/54 release, 124/125 channel; defence 20-23
    ///   Parry Unarmed/1H/2H/2HL, 30 Dodge, 24 ShieldBlock, 36 Block; 9 CombatWound.
    /// </summary>
    public sealed class GearWowAnim
    {
        public const float WoundQuiet = 0.35f;   // s: no wound right after an attack / ability one-shot (WowEntityView.Animate's rule)

        public const int Unarmed = 16, Attack1H = 17, Attack2H = 18, Attack2HL = 19, Pierce1H = 85, AttackOff = 87, PierceOff = 88;
        public const int AttackBow = 46, AttackRifle = 49, AttackThrown = 107, ReadyUnarmed = 25, Ready1H = 26, Ready2H = 27, Ready2HL = 28;
        public const int ReadyBow = 29, ReadyRifle = 48, ReadyThrown = 108, PrecastDirected = 51, PrecastOmni = 52, CastDirected = 53, CastOmni = 54;
        public const int ChannelDirected = 124, ChannelOmni = 125, Dodge = 30, ShieldBlock = 24, Block = 36, CombatWound = 9;
        public const int Special1H = 57, Special2H = 58, ShieldBash = 59, EmoteDance = 69;

        // States fix 2026-10-04 (the user: "when someone dies they are still stood up", "add the rest of the animations, like drinking").
        // AnimationData.dbc MEASURED (harness StateAnims, P/harness/Probes/States/StateAnimsProbe.cs): 1 Death, 6 Dead (fallback 1; on none
        // of the 20 race models), 14 Stun, 61 EmoteEat, 96 SitGroundDown, 97 SitGround, 98 SitGroundUp, 99 SleepDown, 100 Sleep, 101 SleepUp,
        // 102 SitChairLow, 103 SitChairMed, 104 SitChairHigh, 114 KneelStart, 115 KneelLoop, 116 KneelEnd, 121 Knockdown, 122 EatingLoop (only
        // NightElfMale and ScourgeMale), 133 FishingCast, 134 FishingLoop. Every race model has 1, 14, 61, 97, 100, 102-104, 115, 133, 134.
        // Spell.dbc: Drink 430 -> SpellVisual 101, CAST kit anim 61 EmoteEat; Food 433 -> SpellVisual 51 (no unit animation in its kits);
        // Fishing 7620 -> CAST kit 133, CHANNEL kit 134. HumanMale's 61 EmoteEat holds the arms exactly where 97 SitGround holds them
        // (hands 0.28 m above the hips at t 0 in both) - WoW's eat arms go on the sitting body.
        public const int Death = 1, Stun = 14, EmoteEat = 61, SitGround = 97, Sleep = 100, SitChairLow = 102, SitChairMed = 103, SitChairHigh = 104;
        public const int KneelLoop = 115, FishingCast = 133, FishingLoop = 134;
        // Lead 2026-10-26 (user: "add the swimming animations for both VR and Non-XR"): AnimationData 41 SwimIdle, 42 Swim, 43 SwimLeft (measured on AnimationData.dbc 2026-10-26; 13.5 had 41/42 swapped),
        // 44 SwimRight, 45 SwimBackwards (RideGait.SwimIdle .. SwimBackwards, the mounts' measured ids).
        public const int SwimIdle = 41, Swim = 42, SwimLeft = 43, SwimRight = 44, SwimBackwards = 45;

        /// <summary>A swim loop (41-45): the poser lays the body in the water (desktop) or kicks the legs only (VR).</summary>
        public static bool IsSwimLoop(int id) { return id >= SwimIdle && id <= SwimBackwards; }

        /// <summary>The swim loop of the local swimmer now (WowSwim.Motion): forward 42, back 45, strafe left 43 / right 44, else 41.</summary>
        public static int SwimLoopNow()
        {
            SomniumSpace.Worlds.SomniumWoW.Play.PlayerMotion m = WowSwim.Motion;
            if (m.Forward > 0) return Swim;
            if (m.Forward < 0) return SwimBackwards;
            if (m.Strafe < 0) return SwimLeft;
            if (m.Strafe > 0) return SwimRight;
            return SwimIdle;
        }

        /// <summary>A loop WoW plays for a lying / sitting / kneeling state: the avatar shows it with the client's own pose
        /// (GearStatePose), never with our retargeted legs - the WoW puppet adds the arms of one-shots only (eating).</summary>
        public static bool IsPoseLoop(int id)
        {
            return id == Death || id == SitGround || id == Sleep || id == SitChairLow || id == SitChairMed || id == SitChairHigh || id == KneelLoop;
        }

        /// <summary>WoW's loop for a stand state (UNIT_FIELD_BYTES_1 byte 0) - WowEntityView.StandAnim's mapping: 1 sit 97, 2 / 5 chair
        /// 103, 3 sleep 100, 4 low chair 102, 6 high chair 104, 7 dead 1, 8 kneel 115; else 0.</summary>
        public static int StandLoop(int standState)
        {
            switch (standState)
            {
                case 1: return SitGround;
                case 2: case 5: return SitChairMed;
                case 3: return Sleep;
                case 4: return SitChairLow;
                case 6: return SitChairHigh;
                case 7: return Death;
                case 8: return KneelLoop;
                default: return 0;
            }
        }

        public int Loop { get; private set; }
        public int OneShot { get; private set; }
        public int Counter { get; private set; }
        /// <summary>Time.time of the last one-shot (the wound rule).</summary>
        public float ShotAt { get; private set; } = -100f;
        public bool ShotIsAttack { get; private set; }

        public void Clear()
        {
            Loop = 0;
            ShotIsAttack = false;
        }

        public void SetLoop(int id) { Loop = id > 0 && id <= WowNetWire.MaxAnimId ? id : 0; }

        /// <summary>Starts a one-shot (counter + 1). attack = a swing / shot / ability (a wound right after it is not shown).</summary>
        public void Start(int id, float now, bool attack)
        {
            if (id <= 0 || id > WowNetWire.MaxAnimId) return;
            OneShot = id;
            Counter = (Counter + 1) & 0xF;
            ShotAt = now;
            ShotIsAttack = attack;
        }

        /// <summary>A wound (9) unless an attack / ability one-shot started within WoundQuiet.</summary>
        public void Wound(float now)
        {
            if (ShotIsAttack && now - ShotAt < WoundQuiet) return;
            Start(CombatWound, now, false);
        }

        /// <summary>Remote: their record's ids; true when a new one-shot arrived (the counter changed since the last read).</summary>
        public bool Read(in WowAnimRecord r, ref int lastCounter)
        {
            Loop = r.Loop;
            bool fresh = lastCounter >= 0 && r.Counter != lastCounter && r.OneShot > 0;
            lastCounter = r.Counter;
            if (fresh) { OneShot = r.OneShot; Counter = r.Counter; }
            return fresh;
        }

        // ------------------------------------------------------------------ the rules (pure; Item.dbc weapon subclass, -1 = none)

        /// <summary>A melee swing: 16 unarmed / fist (13), 85 dagger (15), 18 two-hand axe / mace / sword (1, 5, 8), 19 polearm / staff
        /// (6, 10), else 17; off hand 88 dagger, else 87.</summary>
        public static int Swing(int sub, bool offHand)
        {
            if (offHand) return sub == 15 ? PierceOff : AttackOff;
            if (sub < 0 || sub == 13) return Unarmed;
            switch (sub)
            {
                case 15: return Pierce1H;
                case 1: case 5: case 8: return Attack2H;
                case 6: case 10: return Attack2HL;
                default: return Attack1H;
            }
        }

        /// <summary>A ranged shot: 46 bow / crossbow (2, 18), 49 gun (3), 107 thrown (16), 53 wand (19, ours); no Item.dbc row: by
        /// InventoryType (25 thrown, 26 ranged right = gun, else bow).</summary>
        public static int Shot(int sub, int inventoryType)
        {
            switch (sub)
            {
                case 2: case 18: return AttackBow;
                case 3: return AttackRifle;
                case 16: return AttackThrown;
                case 19: return CastDirected;
            }
            return inventoryType == 25 ? AttackThrown : inventoryType == 26 ? AttackRifle : AttackBow;
        }

        /// <summary>The combat stance of the main hand: 25 none / fist, 27 two-hand axe / mace / sword, 28 polearm / staff, else 26.</summary>
        public static int Stance(int sub)
        {
            if (sub < 0 || sub == 13) return ReadyUnarmed;
            switch (sub)
            {
                case 1: case 5: case 8: return Ready2H;
                case 6: case 10: return Ready2HL;
                default: return Ready1H;
            }
        }

        /// <summary>Auto Shot's aim: 29 bow / crossbow, 48 gun, 108 thrown, 51 wand (ours).</summary>
        public static int Aim(int sub)
        {
            switch (sub)
            {
                case 3: return ReadyRifle;
                case 16: return ReadyThrown;
                case 19: return PrecastDirected;
                default: return ReadyBow;
            }
        }

        /// <summary>WoW's weapon adaptation of an ability's kit animation (ours, approximate): with a two-hander 57 -> 58 and 17 -> 18;
        /// with a dagger 17 -> 85.</summary>
        public static int Adapt(int id, int mainSub)
        {
            bool twoHand = mainSub == 1 || mainSub == 5 || mainSub == 8 || mainSub == 6 || mainSub == 10;
            if (twoHand && id == Special1H) return Special2H;
            if (twoHand && id == Attack1H) return Attack2H;
            if (mainSub == 15 && id == Attack1H) return Pierce1H;
            return id;
        }

        /// <summary>Parry by the main hand: 20 unarmed / fist, 22 two-hand, 23 polearm / staff, else 21.</summary>
        public static int Parry(int sub)
        {
            if (sub < 0 || sub == 13) return 20;
            switch (sub)
            {
                case 1: case 5: case 8: return 22;
                case 6: case 10: return 23;
                default: return 21;
            }
        }

        /// <summary>An attack-like id (a creature look falls back to 16 / 17 when its model lacks it).</summary>
        public static bool IsAttack(int id)
        {
            return (id >= 16 && id <= 19) || id == 57 || id == 58 || id == 59 || id == 85 || id == 87 || id == 88 || id == 126 || id == 176 || id == 178 || id == 181;
        }

        /// <summary>An older record (no int 32): its WowMotion as a WoW one-shot (Slash1H 17, Slash1HOff 87, Swing2H 18, Swing2HSide 19,
        /// Stab 85, StabOff 88, Punch 16, PunchOff 87, ShieldBash 59, BowShot 46, GunShot 49, Throw 107, WandShot 53, CastRelease 53,
        /// CastInstant 53, CastOmni 54).</summary>
        public static int FromMotion(WowMotion m)
        {
            switch (m)
            {
                case WowMotion.Slash1H: return Attack1H;
                case WowMotion.Slash1HOff: return AttackOff;
                case WowMotion.Swing2H: return Attack2H;
                case WowMotion.Swing2HSide: return Attack2HL;
                case WowMotion.Stab: return Pierce1H;
                case WowMotion.StabOff: return PierceOff;
                case WowMotion.Punch: return Unarmed;
                case WowMotion.PunchOff: return AttackOff;
                case WowMotion.ShieldBash: return ShieldBash;
                case WowMotion.BowShot: return AttackBow;
                case WowMotion.GunShot: return AttackRifle;
                case WowMotion.Throw: return AttackThrown;
                case WowMotion.WandShot: return CastDirected;
                case WowMotion.CastRelease: return CastDirected;
                case WowMotion.CastInstant: return CastDirected;
                case WowMotion.CastOmni: return CastOmni;
                default: return 0;
            }
        }

        /// <summary>An older record's WowHold as a WoW loop (Cast 51, Channel 124, AimBow 29, AimGun 48).</summary>
        public static int FromHold(WowHold h)
        {
            switch (h)
            {
                case WowHold.Cast: return PrecastDirected;
                case WowHold.Channel: return ChannelDirected;
                case WowHold.AimBow: return ReadyBow;
                case WowHold.AimGun: return ReadyRifle;
                default: return 0;
            }
        }
    }
}
