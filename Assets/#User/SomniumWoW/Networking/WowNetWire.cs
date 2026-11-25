using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;

namespace SomniumSpace.Worlds.SomniumWoW.Networking
{
    /// <summary>What a player's slot says about their character: in the room or playing, and what they carry.</summary>
    public enum WowNetStatus { Free = 0, Present = 1, Playing = 2 }

    /// <summary>What a hand holds, as the wire carries it.</summary>
    public enum WowHandHolds { None = 0, MainHand = 1, OffHand = 2, Ranged = 3 }

    /// <summary>One equipped weapon on the wire: ItemDisplayInfo id, Item.dbc SheatheType and InventoryType.</summary>
    public struct WowWireItem
    {
        public int DisplayId, SheatheType, InventoryType;

        public bool Empty { get { return DisplayId <= 0; } }

        public bool SameAs(WowWireItem o)
        {
            return DisplayId == o.DisplayId && SheatheType == o.SheatheType && InventoryType == o.InventoryType;
        }
    }

    /// <summary>
    /// A one-shot motion of a desktop (non-XR) avatar (Game/Play/Gear/GearMotions has the poses). The sender picks it from the
    /// weapon it swings (Item.dbc weapon subclass) or the spell it casts; everyone plays the same one. Values are on the wire.
    /// </summary>
    public enum WowMotion
    {
        None = 0,
        Slash1H = 1, Slash1HOff = 2, Swing2H = 3, Swing2HSide = 4, Stab = 5, StabOff = 6, Punch = 7, PunchOff = 8,
        ShieldBash = 9, BowShot = 10, GunShot = 11, Throw = 12, WandShot = 13,
        CastRelease = 14, CastInstant = 15, CastOmni = 16
    }

    /// <summary>A pose a desktop avatar holds while a state lasts (blended in and out). Values are on the wire.</summary>
    public enum WowHold { None = 0, Cast = 1, Channel = 2, AimBow = 3, AimGun = 4 }

    /// <summary>Stage O2 (owner Sync): what the writer's own cast is doing (int 28 bits 20-21). Values are on the wire.</summary>
    public enum WowCastState { None = 0, Casting = 1, Channeling = 2, Held = 3 }

    /// <summary>Stage O2: the avatar hand a cast comes from (VR: the hand that released the radial menu; desktop: both, instants the
    /// right). NotSaid = the viewer uses the spell's own effect slots (WoW's look: both hands). Values are on the wire.</summary>
    public enum WowCastHand { NotSaid = 0, Right = 1, Left = 2, Both = 3 }

    /// <summary>
    /// Stage O2 (owner Sync, PLAN9 s0 decision 2): the Somnium-only extras of the writer's own spell casting - ints 28-30 of the gear
    /// record - so other Somnium players see the cast ON THE AVATAR. The server (online) knows the cast itself but not the hand, the VR
    /// held spell of immersive casting, or anything at all offline. Every field "0 = not said": an older record reads as no cast.
    /// </summary>
    public struct WowCastRecord
    {
        public int SpellId;               // Spell.dbc id of the cast in progress / held spell, 0 = none
        public WowCastState State;
        public WowCastHand Hand;
        public int Counter;               // +1 per cast started (mod 16): a change = a new cast (the same spell again too)
        public bool Immersive;            // VR immersive casting: the glow grows in Hand while the cast runs, then the spell is held
        public int TotalMs;               // cast / channel length in ms (0 = unknown), 0..262143
        public float Progress;            // 0..1 of TotalMs at the moment the timing was written (the viewer extrapolates)
        public int ReleaseSpellId;        // the last own spell released (CastSucceeded of the writer), 0 = none said yet
        public int ReleaseCounter;        // +1 per release (mod 16): a change = play that spell's cast kit on the avatar again
        public WowCastHand ReleaseHand;
        public bool ReleaseAtSelf;        // it landed on the caster (a self heal / buff): the viewer may play its impact on the avatar

        public bool Casting { get { return SpellId > 0 && State != WowCastState.None; } }
    }

    /// <summary>Iteration 11 (owner avatar): what the writer's avatar looks like (int 31). Kind = Play.Rules.AvatarLookKind as a number.</summary>
    public struct WowLookRecord
    {
        public int Kind;                  // AvatarLookKind: 0 own, 1 creature, 2 ghost shell, 3 stealth shell, 4 shadow tint, 5 hidden
        public int DisplayId;             // CreatureDisplayInfo id while Kind == 1 (0-65535), else 0
        public float Scale;               // object scale (OBJECT_FIELD_SCALE_X), 1 = normal (wire: x 64 in 8 bits, 0 = 1.0 / not said)
        public bool Swimming, Airborne, Flying;   // the writer's own locomotion decision for its creature model's gait
    }

    /// <summary>Iteration 11 (owner avatar): the WoW AnimationData ids the writer's avatar plays (int 32 bits 0-21; bit 28 its style).</summary>
    public struct WowAnimRecord
    {
        public int OneShot;               // the last one-shot started (attack, ability kit, parry, wound, emote), 0 = none said
        public int Counter;               // +1 per one-shot started (mod 16): a change = play OneShot again
        public int Loop;                  // the held loop (combat stance, precast, channel, aim, an emote loop), 0 = the client's own animation
        public bool SimpleArms;           // fix 2026-10-04 (remote animation): the writer chose the simple (arms) combat style - int 32 bit 28.
                                          // 0 (and every older record) = the WoW full-body style, the default. Viewers draw the WRITER's choice.
    }

    /// <summary>Iteration 11 (owner avatar): immersive armour (int 32 bits 22-27, ints 33-43). The pieces are ItemDisplayInfo ids in
    /// the order Head, Shoulders, Chest, Shirt, Tabard, Waist, Legs, Feet, Wrists, Hands, Back (EquipSlot 0, 2, 4, 3, 18, 5, 6, 7, 8, 9,
    /// 14) - eleven fields, not an array, so a record copy never allocates (UnpackGear runs per remote per frame).</summary>
    public struct WowArmourRecord
    {
        public int Mode;                  // the wearer's choice: 0 off, 1 helm + shoulders, 2 full (experimental)
        public float Fit;                 // 0.8 .. 1.4 (1 = not said)
        public int Gender;                // 0 male, 1 female, -1 not said (an older record)
        public bool HideHelm, HideCloak;
        public int Head, Shoulders, Chest, Shirt, Tabard, Waist, Legs, Feet, Wrists, Hands, Back;
        public int AdjHelm, AdjLeft, AdjRight;   // lead 2026-10-03: ArmourAdjust.Pack per piece (ints 45-47; 0 = not said = default placement)
        public int AdjChest, AdjWaist, AdjArms, AdjHands, AdjLegs, AdjFeet, AdjCloak;   // lead 2026-10-04: the full shell's pieces (ints 48-54)
        public int Tilts0, Tilts1;   // lead 2026-10-04: ArmourAdjust.PackTilts - slots 0-4 (int 55), 5-9 (int 56); 0 = no tilt
        public int Tapers0, Tapers1, Tapers2;   // lead 2026-10-04: ArmourAdjust.PackTapers - the shell pieces' top / bottom taper (ints 57-59)
        public int Ends0, Ends1, Ends2, Ends3;   // lead 2026-10-04: ArmourAdjust.PackEnds - the shell pieces' top / bottom edge offsets (ints 60-63)
        /// <summary>Lead 2026-11-04: the wearer's own body measurement for the full shell (ArmourShell.ExportShape, 5 x 30 bits; all 0 = not
        /// said). Carried in the spare high bits of ints 33 (24-30), 34-43 (17-30) and 44 (24-30) - 154 bits, see WowNetWire.ShapeChunks.</summary>
        public int Shape0, Shape1, Shape2, Shape3, Shape4;

        /// <summary>The packed fit of ArmourAdjust slot i (0 helm .. 9 cloak).</summary>
        public int Adj(int i)
        {
            switch (i)
            {
                case 0: return AdjHelm; case 1: return AdjLeft; case 2: return AdjRight; case 3: return AdjChest; case 4: return AdjWaist;
                case 5: return AdjArms; case 6: return AdjHands; case 7: return AdjLegs; case 8: return AdjFeet; case 9: return AdjCloak;
                default: return 0;
            }
        }

        public void SetAdj(int i, int v)
        {
            switch (i)
            {
                case 0: AdjHelm = v; break; case 1: AdjLeft = v; break; case 2: AdjRight = v; break; case 3: AdjChest = v; break; case 4: AdjWaist = v; break;
                case 5: AdjArms = v; break; case 6: AdjHands = v; break; case 7: AdjLegs = v; break; case 8: AdjFeet = v; break; case 9: AdjCloak = v; break;
            }
        }


        public const int SlotCount = 11;
        /// <summary>EquipSlot of piece i (the order above).</summary>
        public static readonly int[] EquipSlots = { 0, 2, 4, 3, 18, 5, 6, 7, 8, 9, 14 };

        public int Slot(int i)
        {
            switch (i)
            {
                case 0: return Head; case 1: return Shoulders; case 2: return Chest; case 3: return Shirt; case 4: return Tabard;
                case 5: return Waist; case 6: return Legs; case 7: return Feet; case 8: return Wrists; case 9: return Hands; case 10: return Back;
                default: return 0;
            }
        }

        public void SetSlot(int i, int v)
        {
            switch (i)
            {
                case 0: Head = v; break; case 1: Shoulders = v; break; case 2: Chest = v; break; case 3: Shirt = v; break; case 4: Tabard = v; break;
                case 5: Waist = v; break; case 6: Legs = v; break; case 7: Feet = v; break; case 8: Wrists = v; break; case 9: Hands = v; break;
                case 10: Back = v; break;
            }
        }
    }

    /// <summary>A player's gear record (ints 4-10 of their slot).</summary>
    public struct WowGearState
    {
        public WowNetStatus Status;
        public bool Xr, Dead, InCombat, RoomMaster, AutoAttacking;
        public int Class, Level, HealthPercent, Sheath, Form, AttackCounter;
        public int Race;                  // ChrRaces id, 0 = not said (older build)
        public WowMotion Motion;          // the last one-shot motion started
        public int MotionCounter;         // +1 per motion started (mod 256): a change = play Motion again
        public WowHold Hold;
        public WowHandHolds Right, Left;
        public WowWireItem Main, Off, Ranged;
        // iteration 4: the writer's floating origin (Core/WowSpace) and flights
        public bool OriginSaid;           // the record carries OriginMap/Cell* (false = a record of an older build: drawn as before)
        public int OriginMap;             // Map.dbc id of the writer's origin; -1 = not in the world (the room) or not said
        public int CellX, CellY, CellZ;   // the writer's origin cell (WoW point = cell * WowSpace.OriginGridYards)
        public bool OnTaxi;               // flying a flight path
        public int MountDisplayId;        // CreatureDisplayInfo id of the mount under the avatar, 0 = none
        public ulong CharacterGuid;       // iteration 6: the writer's WoW character guid while logged in to a realm (online), 0 = offline / not said
        public int CharacterNameHash;     // iteration 6: WowNetWire.NameHash of that character's name (tells two realms' equal guids apart), 0 = not said
        public RideRecord Ride;           // iteration 7: the writer's ridden mount (ints 17-27, Play/Riding/RideWire), Kind 0 = not said (on foot / an older build)
        public WowCastRecord Cast;        // stage O2: the writer's own cast, held spell and last release (ints 28-30), all 0 = not said
        // iteration 11 (owner avatar, proposal-avatar.md 5.5) - ints 31-43; int 44 spellfx's weapon-enchant visuals (proposal-spellfx.md hook 7)
        public WowLookRecord Look;        // int 31, all 0 = not said (an older build, or the avatar's own look)
        public WowAnimRecord Anim;        // int 32 bits 0-21 + 28: the WoW animation ids the avatar plays (desktop full body, creature one-shots), its style
        public WowArmourRecord Armour;    // int 32 bits 22-27 + ints 33-43: gender, immersive armour mode, hide helm / cloak, the armour pieces
        public int EnchantMain, EnchantOff, EnchantRanged;   // int 44: ItemVisuals ids of the weapon-enchant glows (8 bits each), 0 = none

        /// <summary>Iteration 11: the writer is an iteration-11 build playing (int 32 says the gender) - its int 31 look is meant,
        /// Own included. False = an older record: an online viewer may fall back to the server's copy (AvatarLooks.FromEntity).</summary>
        public bool LookSaid { get { return Armour.Gender >= 0; } }

        public WowWireItem Item(WowHandHolds which)
        {
            switch (which)
            {
                case WowHandHolds.MainHand: return Main;
                case WowHandHolds.OffHand: return Off;
                case WowHandHolds.Ranged: return Ranged;
                default: return default(WowWireItem);
            }
        }
    }

    /// <summary>
    /// A player slot's 64 ints, "Wow Net Player 0..49" (a NetworkBridgeData64 each), the Euro Truck Sim Dreamport's proven
    /// pattern (EtsPlayerWire / EtsSlotRules / EtsSlotClaim) cut down to what the weapons need. Only the slot's owner writes
    /// it. Pure: no Fusion, no Unity, so the rules can be reasoned about (and later probed) outside the client.
    ///
    ///   0  magic 'WOW1' (0 = never written; anything else is not a slot of this build)
    ///   1  seq, +1 on every write of the record
    ///   2  stamp: the writer's clock in ms (for the log only)
    ///   3  owner PlayerId+1 (0-15) | claim generation (16-23) | spare (24-31)
    ///   4  status (0-3: 0 free, 1 present in the room, 2 playing) | flags (16 VR, 17 dead, 18 in combat,
    ///      19 the writer is the room master, 20 auto-attacking)
    ///   5  class (0-7, ChrClasses id) | level (8-15) | health percent (16-23) | sheath state (24-25) | race (26-30,
    ///      ChrRaces id, 0 = not said: a record of an iteration-2 build)
    ///   6  shapeshift form (0-7) | right hand holds (8-9) | left hand holds (10-11) | attack counter mod 16 (12-15)
    ///   7  main hand: ItemDisplayInfo id (0-19) | SheatheType (20-23) | InventoryType (24-28)
    ///   8  off hand: the same
    ///   9  ranged: the same
    ///  10 animation (iteration 3): motion kind (0-7, WowMotion) | motion counter mod 256 (8-15, +1 per motion started)
    ///      | hold (16-19, WowHold: casting, channelling, aiming) - a desktop avatar plays them; VR avatars (flag 16) are left alone
    ///  11 origin (iteration 4): origin map id + 1 (0-11, 0 = none: in the room) | on a flight path (12) | the origin is said
    ///      (13, set by every iteration-4 writer; 0 = a record of an older build, drawn without re-mapping as before)
    ///  12 origin cell: x (0-9) | y (10-19) | z (20-29), each a 10-bit two's complement cell index (-512..511, WoW point =
    ///      cell * 256 yards), see Core/WowSpace.RemoteOffset
    ///  13 mount: CreatureDisplayInfo id (0-23) of the mount under the avatar (a flight path's gryphon/wyvern), 0 = none
    ///  14 online character guid, low 32 bits (iteration 6; 0 with 15 = offline / not said)
    ///  15 online character guid, high 32 bits
    ///  16 online character name hash (NameHash: FNV-1a 32 of the upper-cased name, 0 = not said)
    ///  17-27 iteration 7: the ridden mount (Play/Riding/RideWire: kind, gait and rate, mount-up counter, epoch, the mount's pose
    ///      relative to the synced root, the stage-B stamped root pose); all 0 = not said (on foot, or an older build)
    ///  28 stage O2 cast: spell id (0-19, Spell.dbc id, 0 = none) | state (20-21, WowCastState: 1 casting, 2 channelling, 3 held)
    ///      | hand (22-23, WowCastHand: 0 not said, 1 right, 2 left, 3 both) | cast counter mod 16 (24-27, +1 per cast started)
    ///      | immersive (28: VR immersive casting - the glow grows in that hand, then the spell is held for the push)
    ///  29 stage O2 cast timing: total ms (0-17, 0 = unknown) | progress at the write x 1000 (18-27, 0..1000). Written with a new
    ///      cast and rewritten only when the viewer's extrapolation would be more than WowCastWriter.ResyncProgress off (pushback,
    ///      an early end) - a steady cast changes int 29 once
    ///  30 stage O2 release: spell id (0-19) of the last own spell released | release counter mod 16 (20-23) | hand (24-25) |
    ///      at self (26: it landed on the caster)
    ///  31 iteration 11 look (proposal-avatar.md 5.5): CreatureDisplayInfo id (0-15, 0 = own avatar) | AvatarLookKind (16-19) | object
    ///     scale x 64 (20-27, 0 = 1.0 / not said) | swimming (28) | airborne (29: jumping / falling) | flying (30: a flight form in the air)
    ///  32 iteration 11 anim + body: one-shot AnimationData id (0-8, 0 = none said) | one-shot counter mod 16 (9-12, +1 per one-shot
    ///     started) | loop AnimationData id (13-21, 0 = the client's own animation) | gender + 1 (22-23, 0 = not said: an older record) |
    ///     immersive armour mode (24-25: 0 off, 1 helm + shoulders, 2 full) | hide helm (26) | hide cloak (27)
    ///  33 iteration 11 helm ItemDisplayInfo id (0-16) | armour fit (17-23: n = round((fit - 0.5) / 0.01), 1..127; 0 = 1.0 not said)
    ///  34-43 iteration 11 shoulders, chest, shirt, tabard, waist, legs, feet, wrists, hands, back: ItemDisplayInfo id (0-16) each
    ///  44 iteration 11 (spellfx hook 7): weapon-enchant ItemVisuals ids - main hand (0-7) | off hand (8-15) | ranged (16-23); 0 = no glow
    ///  45-63 spare 0
    /// Bit 31 of every int stays 0. Size: 45 ints written, Fusion sends changed ints only (a player standing still costs nothing; a
    /// steady desktop ride changes only ints 21-27; a cast changes ints 28-29 at its start and 28 at its end, a release int 30; a form
    /// change int 31 once; an attack int 32 once; armour nothing while the gear is constant). Magic unchanged: an older build reads
    /// ints 0-30 of ours and ignores the rest; ours reads an older record's 11-44 as 0 = "not said".
    /// </summary>
    public static class WowNetWire
    {
        /// <summary>Scene objects "Wow Net Player 0".."Wow Net Player 49" (the YAML is reports2/gear_scene.yaml).</summary>
        public const string ObjectPrefix = "Wow Net Player ";

        public const int Magic = 0x574F5731; // 'WOW1'
        public const int Ints = 64;
        public const int Used = 64;   // 2026-10-04: + 60-63 the armour pieces' top / bottom edges - the record is full (NetworkBridgeData64). Was 60;   // 2026-10-04: + 57-59 the armour tapers. Was 57;   // 2026-10-04: + 55-56 the armour tilts. Was 55;   // lead 2026-10-03: + ints 45-47 the armour placement; 2026-10-04: + 48-54 the full shell's pieces. Iteration 11 (was 31): ints 31-43 = the avatar's look / WoW animation / armour, 44 = weapon-enchant
                                      // visuals (stage O2 was 28 -> 31: ints 28-30 = the cast; iteration 7 17 -> 28: ints 17-27 = RideWire)

        public const int AtMagic = 0;
        public const int AtSeq = 1;
        public const int AtStamp = 2;
        public const int AtOwner = 3;
        public const int AtStatus = 4;
        public const int AtCharacter = 5;
        public const int AtHands = 6;
        public const int AtMain = 7;
        public const int AtOff = 8;
        public const int AtRanged = 9;
        public const int AtAnim = 10;
        public const int AtOrigin = 11;
        public const int AtCell = 12;
        public const int AtMount = 13;
        public const int AtCharLow = 14;
        public const int AtCharHigh = 15;
        public const int AtCharName = 16;
        public const int AtCast = 28;
        public const int AtCastTiming = 29;
        public const int AtRelease = 30;
        public const int AtLook = 31;        // iteration 11 (avatar)
        public const int AtWowAnim = 32;     // iteration 11 (avatar; proposal-avatar.md 5.5 calls it AtAnim - that name is int 10's)
        public const int AtArmour = 33;      // iteration 11 (avatar): the helm + fit; 34-43 the other pieces
        public const int AtEnchant = 44;     // iteration 11 (spellfx hook 7, reserved by proposal-avatar.md 5.5)
        /// <summary>Lead 2026-11-04: where WowArmourRecord.Shape0-4 travel - (int, first bit, bit count) in the spare high bits the readers
        /// mask off (helm & fit bits 0-23 of 33, the ItemDisplay ids & MaxItemDisplay of 34-43, the enchants bits 0-23 of 44). Bit 31 stays 0.</summary>
        public static readonly int[,] ShapeChunks =
        {
            { 33, 24, 7 }, { 34, 17, 14 }, { 35, 17, 14 }, { 36, 17, 14 }, { 37, 17, 14 }, { 38, 17, 14 }, { 39, 17, 14 },
            { 40, 17, 14 }, { 41, 17, 14 }, { 42, 17, 14 }, { 43, 17, 14 }, { 44, 24, 7 },
        };

        private static int[] ShapeHighBits(in WowArmourRecord r)
        {
            var hi = new int[12];
            int[] src = { r.Shape0, r.Shape1, r.Shape2, r.Shape3, r.Shape4 };
            int pos = 0;
            for (int c = 0; c < ShapeChunks.GetLength(0); c++)
                for (int b = 0; b < ShapeChunks[c, 2]; b++, pos++)
                    if (pos < 150 && ((src[pos / 30] >> (pos % 30)) & 1) != 0) hi[ShapeChunks[c, 0] - 33] |= 1 << (ShapeChunks[c, 1] + b);
            return hi;
        }

        private static void ReadShape(int[] from, ref WowArmourRecord r)
        {
            var dst = new int[5];
            int pos = 0;
            for (int c = 0; c < ShapeChunks.GetLength(0); c++)
                for (int b = 0; b < ShapeChunks[c, 2]; b++, pos++)
                    if (pos < 150 && ((from[ShapeChunks[c, 0]] >> (ShapeChunks[c, 1] + b)) & 1) != 0) dst[pos / 30] |= 1 << (pos % 30);
            r.Shape0 = dst[0]; r.Shape1 = dst[1]; r.Shape2 = dst[2]; r.Shape3 = dst[3]; r.Shape4 = dst[4];
        }

        public const int AtArmourAdjust = 45;   // lead 2026-10-03: 45 helm, 46 left shoulder, 47 right shoulder (ArmourAdjust); 2026-10-04: 48-54 = slots 3-9

        public const int MaxLookDisplay = 0xFFFF, MaxAnimId = 0x1FF, MaxItemDisplay = 0x1FFFF;
        public const int LookFlagSwimming = 1 << 28, LookFlagAirborne = 1 << 29, LookFlagFlying = 1 << 30;
        public const int AnimFlagHideHelm = 1 << 26, AnimFlagHideCloak = 1 << 27;
        public const int AnimFlagSimpleArms = 1 << 28;   // fix 2026-10-04: the writer's combat style is the simple arms (0 = WoW full body)

        public const int CastFlagImmersive = 1 << 28;
        public const int ReleaseFlagSelf = 1 << 26;
        public const int MaxSpellId = 0xFFFFF;
        public const int MaxCastMs = 0x3FFFF;

        public const int OriginFlagTaxi = 1 << 12;
        public const int OriginFlagSaid = 1 << 13;

        public const int FlagXr = 1 << 16;
        public const int FlagDead = 1 << 17;
        public const int FlagCombat = 1 << 18;
        public const int FlagMaster = 1 << 19;
        public const int FlagAttacking = 1 << 20;

        /// <summary>The slot's owner as a PlayerId, -1 when never written or free.</summary>
        public static int OwnerOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] & 0xFFFF) - 1 : -1;
        }

        public static int GenerationOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] >> 16) & 0xFF : 0;
        }

        public static WowNetStatus StatusOf(int[] from)
        {
            if (from == null || from.Length < Used || from[AtMagic] != Magic) return WowNetStatus.Free;
            int s = from[AtStatus] & 0xF;
            return s <= 2 ? (WowNetStatus)s : WowNetStatus.Free;
        }

        /// <summary>The free record: 'WOW1', seq + 1, the stamp, owner 0 with the claim generation kept, all else 0.</summary>
        public static void WriteFree(int[] into, int stampMs)
        {
            int seq = into[AtSeq] + 1;
            int generation = GenerationOf(into);
            Array.Clear(into, 0, into.Length);
            into[AtMagic] = Magic;
            into[AtSeq] = seq;
            into[AtStamp] = stampMs;
            into[AtOwner] = generation << 16;
        }

        /// <summary>A fresh claim: every int cleared, then owner = this client, the new generation, status Present.</summary>
        public static void WriteClaim(int[] into, int localId, int generation, int stampMs)
        {
            int seq = into[AtMagic] == Magic ? into[AtSeq] + 1 : 1;
            Array.Clear(into, 0, into.Length);
            into[AtMagic] = Magic;
            into[AtSeq] = seq;
            into[AtStamp] = stampMs;
            into[AtOwner] = ((localId < 0 ? 0 : localId + 1) & 0xFFFF) | ((generation & 0xFF) << 16);
            into[AtStatus] = (int)WowNetStatus.Present;
        }

        static int PackItem(WowWireItem i)
        {
            if (i.DisplayId <= 0) return 0;
            return (i.DisplayId & 0xFFFFF) | ((i.SheatheType & 0xF) << 20) | ((i.InventoryType & 0x1F) << 24);
        }

        static WowWireItem UnpackItem(int v)
        {
            return new WowWireItem { DisplayId = v & 0xFFFFF, SheatheType = (v >> 20) & 0xF, InventoryType = (v >> 24) & 0x1F };
        }

        /// <summary>Ints 4-9 from a gear state; the header (0-3) is kept. True when anything changed.</summary>
        public static bool PackGear(in WowGearState s, int[] into)
        {
            int status = ((int)s.Status & 0xF)
                | (s.Xr ? FlagXr : 0) | (s.Dead ? FlagDead : 0) | (s.InCombat ? FlagCombat : 0)
                | (s.RoomMaster ? FlagMaster : 0) | (s.AutoAttacking ? FlagAttacking : 0);
            int character = (s.Class & 0xFF) | ((s.Level & 0xFF) << 8) | ((Clamp(s.HealthPercent, 0, 100) & 0xFF) << 16) | ((s.Sheath & 0x3) << 24)
                | ((s.Race & 0x1F) << 26);
            int anim = ((int)s.Motion & 0xFF) | ((s.MotionCounter & 0xFF) << 8) | (((int)s.Hold & 0xF) << 16);
            int hands = (s.Form & 0xFF) | (((int)s.Right & 0x3) << 8) | (((int)s.Left & 0x3) << 10) | ((s.AttackCounter & 0xF) << 12);
            bool changed = false;
            changed |= Set(into, AtStatus, status);
            changed |= Set(into, AtCharacter, character);
            changed |= Set(into, AtHands, hands);
            changed |= Set(into, AtMain, PackItem(s.Main));
            changed |= Set(into, AtOff, PackItem(s.Off));
            changed |= Set(into, AtRanged, PackItem(s.Ranged));
            changed |= Set(into, AtAnim, anim);
            int origin = s.OriginSaid ? (OriginFlagSaid | (s.OriginMap < 0 ? 0 : Clamp(s.OriginMap + 1, 0, 0xFFF))) : 0;
            if (s.OnTaxi) origin |= OriginFlagTaxi;
            int cell = s.OriginSaid ? (Cell10(s.CellX) | (Cell10(s.CellY) << 10) | (Cell10(s.CellZ) << 20)) : 0;
            changed |= Set(into, AtOrigin, origin);
            changed |= Set(into, AtCell, cell);
            changed |= Set(into, AtMount, s.MountDisplayId > 0 ? s.MountDisplayId & 0xFFFFFF : 0);
            changed |= Set(into, AtCharLow, (int)(uint)(s.CharacterGuid & 0xFFFFFFFFUL));
            changed |= Set(into, AtCharHigh, (int)(uint)(s.CharacterGuid >> 32));
            changed |= Set(into, AtCharName, s.CharacterGuid != 0 ? s.CharacterNameHash : 0);
            changed |= RideWire.Pack(s.Ride, into);
            changed |= PackCast(s.Cast, into);
            changed |= PackLook(s, into);
            return changed;
        }

        /// <summary>Iteration 11 (avatar): ints 31-44 from a gear state (the look, the WoW animation, the armour, the enchant glows;
        /// nothing else touched). The gender is said only while Playing (it marks an iteration-11 record). True when anything changed.</summary>
        public static bool PackLook(in WowGearState s, int[] into)
        {
            if (into == null || into.Length < Used) return false;
            WowLookRecord l = s.Look;
            int look = 0;
            if (l.Kind > 0)
            {
                look = (l.Kind == 1 ? Clamp(l.DisplayId, 0, MaxLookDisplay) : 0) | ((l.Kind & 0xF) << 16);
                if (l.Kind == 1) look |= (Clamp((int)Math.Round((float.IsNaN(l.Scale) ? 1f : l.Scale) * 64.0), 1, 255) & 0xFF) << 20;
            }
            if (l.Swimming) look |= LookFlagSwimming;
            if (l.Airborne) look |= LookFlagAirborne;
            if (l.Flying) look |= LookFlagFlying;
            WowAnimRecord a = s.Anim;
            WowArmourRecord r = s.Armour;
            int anim = (Clamp(a.OneShot, 0, MaxAnimId)) | ((a.Counter & 0xF) << 9) | (Clamp(a.Loop, 0, MaxAnimId) << 13);
            if (s.Status == WowNetStatus.Playing && r.Gender >= 0) anim |= ((r.Gender & 1) + 1) << 22;
            anim |= (Clamp(r.Mode, 0, 3) & 0x3) << 24;
            if (r.HideHelm) anim |= AnimFlagHideHelm;
            if (r.HideCloak) anim |= AnimFlagHideCloak;
            if (a.SimpleArms) anim |= AnimFlagSimpleArms;
            int fit = 0;
            if (r.Fit > 0f && !float.IsNaN(r.Fit) && Math.Abs(r.Fit - 1f) > 0.004f) fit = Clamp((int)Math.Round((r.Fit - 0.5) / 0.01), 1, 127);
            bool changed = false;
            changed |= Set(into, AtLook, look);
            changed |= Set(into, AtWowAnim, anim);
            int[] hi = ShapeHighBits(r);   // lead 2026-11-04: the wearer's measurement in the spare high bits of ints 33-44
            changed |= Set(into, AtArmour, Clamp(r.Head, 0, MaxItemDisplay) | (fit << 17) | hi[0]);
            for (int i = 1; i < WowArmourRecord.SlotCount; i++) changed |= Set(into, AtArmour + i, Clamp(r.Slot(i), 0, MaxItemDisplay) | hi[i]);
            int ench = (Clamp(s.EnchantMain, 0, 0xFF)) | (Clamp(s.EnchantOff, 0, 0xFF) << 8) | (Clamp(s.EnchantRanged, 0, 0xFF) << 16);
            changed |= Set(into, AtEnchant, ench | hi[11]);
            for (int ai = 0; ai < 10; ai++) changed |= Set(into, AtArmourAdjust + ai, r.Adj(ai));   // 45-54
            changed |= Set(into, AtArmourAdjust + 10, r.Tilts0);   // 55
            changed |= Set(into, AtArmourAdjust + 11, r.Tilts1);   // 56
            changed |= Set(into, AtArmourAdjust + 12, r.Tapers0);   // 57
            changed |= Set(into, AtArmourAdjust + 13, r.Tapers1);   // 58
            changed |= Set(into, AtArmourAdjust + 14, r.Tapers2);   // 59
            changed |= Set(into, AtArmourAdjust + 15, r.Ends0);   // 60 (lead 2026-10-04)
            changed |= Set(into, AtArmourAdjust + 16, r.Ends1);   // 61
            changed |= Set(into, AtArmourAdjust + 17, r.Ends2);   // 62
            changed |= Set(into, AtArmourAdjust + 18, r.Ends3);   // 63
            return changed;
        }

        /// <summary>Iteration 11 (avatar): ints 31-44 into s (UnpackGear calls it). An older record reads all 0: look own, no animation
        /// said, gender -1 (not said), armour off, no enchant glows.</summary>
        public static void UnpackLook(int[] from, ref WowGearState s)
        {
            s.Look = default(WowLookRecord);
            s.Anim = default(WowAnimRecord);
            s.Armour = default(WowArmourRecord);
            s.Armour.Gender = -1;
            s.Armour.Fit = 1f;
            s.Look.Scale = 1f;
            s.EnchantMain = s.EnchantOff = s.EnchantRanged = 0;
            if (from == null || from.Length < Used) return;
            int look = from[AtLook];
            int kind = (look >> 16) & 0xF;
            s.Look.Kind = kind <= 5 ? kind : 0;
            s.Look.DisplayId = s.Look.Kind == 1 ? look & MaxLookDisplay : 0;
            int sc = (look >> 20) & 0xFF;
            s.Look.Scale = sc == 0 ? 1f : sc / 64f;
            s.Look.Swimming = (look & LookFlagSwimming) != 0;
            s.Look.Airborne = (look & LookFlagAirborne) != 0;
            s.Look.Flying = (look & LookFlagFlying) != 0;
            int anim = from[AtWowAnim];
            s.Anim.OneShot = anim & MaxAnimId;
            s.Anim.Counter = (anim >> 9) & 0xF;
            s.Anim.Loop = (anim >> 13) & MaxAnimId;
            s.Anim.SimpleArms = (anim & AnimFlagSimpleArms) != 0;
            int g = (anim >> 22) & 0x3;
            s.Armour.Gender = g == 0 ? -1 : (g - 1) & 1;
            s.Armour.Mode = (anim >> 24) & 0x3;
            s.Armour.HideHelm = (anim & AnimFlagHideHelm) != 0;
            s.Armour.HideCloak = (anim & AnimFlagHideCloak) != 0;
            int helm = from[AtArmour];
            s.Armour.Head = helm & MaxItemDisplay;
            int fit = (helm >> 17) & 0x7F;
            s.Armour.Fit = fit == 0 ? 1f : 0.5f + fit * 0.01f;
            for (int i = 1; i < WowArmourRecord.SlotCount; i++) s.Armour.SetSlot(i, from[AtArmour + i] & MaxItemDisplay);
            ReadShape(from, ref s.Armour);   // lead 2026-11-04
            int ench = from[AtEnchant];
            s.EnchantMain = ench & 0xFF;
            s.EnchantOff = (ench >> 8) & 0xFF;
            s.EnchantRanged = (ench >> 16) & 0xFF;
            for (int ai = 0; ai < 10; ai++) s.Armour.SetAdj(ai, from[AtArmourAdjust + ai]);   // 45-54
            s.Armour.Tilts0 = from[AtArmourAdjust + 10];
            s.Armour.Tilts1 = from[AtArmourAdjust + 11];
            s.Armour.Tapers0 = from[AtArmourAdjust + 12];
            s.Armour.Tapers1 = from[AtArmourAdjust + 13];
            s.Armour.Tapers2 = from[AtArmourAdjust + 14];
            s.Armour.Ends0 = from[AtArmourAdjust + 15];   // lead 2026-10-04
            s.Armour.Ends1 = from[AtArmourAdjust + 16];
            s.Armour.Ends2 = from[AtArmourAdjust + 17];
            s.Armour.Ends3 = from[AtArmourAdjust + 18];
        }

        /// <summary>Stage O2: ints 28-30 from a cast record (nothing else touched). True when anything changed.</summary>
        public static bool PackCast(in WowCastRecord c, int[] into)
        {
            if (into == null || into.Length < Used) return false;
            int cast = 0, timing = 0, release = 0;
            if (c.SpellId > 0 && c.SpellId <= MaxSpellId && c.State != WowCastState.None)
            {
                cast = (c.SpellId & MaxSpellId) | (((int)c.State & 0x3) << 20) | (((int)c.Hand & 0x3) << 22) | ((c.Counter & 0xF) << 24)
                       | (c.Immersive ? CastFlagImmersive : 0);
                int total = Clamp(c.TotalMs, 0, MaxCastMs);
                float p = float.IsNaN(c.Progress) ? 0f : c.Progress;
                int prog = total > 0 ? Clamp((int)Math.Round(p * 1000.0), 0, 1000) : 0;
                timing = total | (prog << 18);
            }
            if (c.ReleaseSpellId > 0 && c.ReleaseSpellId <= MaxSpellId)
                release = (c.ReleaseSpellId & MaxSpellId) | ((c.ReleaseCounter & 0xF) << 20) | (((int)c.ReleaseHand & 0x3) << 24) | (c.ReleaseAtSelf ? ReleaseFlagSelf : 0);
            bool changed = false;
            changed |= Set(into, AtCast, cast);
            changed |= Set(into, AtCastTiming, timing);
            changed |= Set(into, AtRelease, release);
            return changed;
        }

        /// <summary>Stage O2: the cast record of ints 28-30 (all "not said" for a short array or an older record).</summary>
        public static WowCastRecord UnpackCast(int[] from)
        {
            var c = default(WowCastRecord);
            if (from == null || from.Length < Used) return c;
            int a = from[AtCast], t = from[AtCastTiming], r = from[AtRelease];
            int spell = a & MaxSpellId;
            var state = (WowCastState)((a >> 20) & 0x3);
            if (spell > 0 && state != WowCastState.None)
            {
                c.SpellId = spell;
                c.State = state;
                c.Hand = (WowCastHand)((a >> 22) & 0x3);
                c.Counter = (a >> 24) & 0xF;
                c.Immersive = (a & CastFlagImmersive) != 0;
                c.TotalMs = t & MaxCastMs;
                int prog = (t >> 18) & 0x3FF;
                c.Progress = c.TotalMs > 0 ? Math.Min(prog, 1000) / 1000f : 0f;
            }
            int rs = r & MaxSpellId;
            if (rs > 0)
            {
                c.ReleaseSpellId = rs;
                c.ReleaseCounter = (r >> 20) & 0xF;
                c.ReleaseHand = (WowCastHand)((r >> 24) & 0x3);
                c.ReleaseAtSelf = (r & ReleaseFlagSelf) != 0;
            }
            return c;
        }

        /// <summary>False unless the slot holds this build's magic.</summary>
        public static bool UnpackGear(int[] from, out WowGearState s)
        {
            s = default(WowGearState);
            if (from == null || from.Length < Used || from[AtMagic] != Magic) return false;
            int status = from[AtStatus];
            int st = status & 0xF;
            s.Status = st <= 2 ? (WowNetStatus)st : WowNetStatus.Free;
            s.Xr = (status & FlagXr) != 0;
            s.Dead = (status & FlagDead) != 0;
            s.InCombat = (status & FlagCombat) != 0;
            s.RoomMaster = (status & FlagMaster) != 0;
            s.AutoAttacking = (status & FlagAttacking) != 0;
            int c = from[AtCharacter];
            s.Class = c & 0xFF;
            s.Level = (c >> 8) & 0xFF;
            s.HealthPercent = (c >> 16) & 0xFF;
            s.Sheath = (c >> 24) & 0x3;
            s.Race = (c >> 26) & 0x1F;
            int h = from[AtHands];
            s.Form = h & 0xFF;
            s.Right = (WowHandHolds)((h >> 8) & 0x3);
            s.Left = (WowHandHolds)((h >> 10) & 0x3);
            s.AttackCounter = (h >> 12) & 0xF;
            s.Main = UnpackItem(from[AtMain]);
            s.Off = UnpackItem(from[AtOff]);
            s.Ranged = UnpackItem(from[AtRanged]);
            int a = from[AtAnim];
            int mk = a & 0xFF, hk = (a >> 16) & 0xF;
            s.Motion = mk <= (int)WowMotion.CastOmni ? (WowMotion)mk : WowMotion.None;
            s.MotionCounter = (a >> 8) & 0xFF;
            s.Hold = hk <= (int)WowHold.AimGun ? (WowHold)hk : WowHold.None;
            int o = from[AtOrigin], cl = from[AtCell];
            s.OriginSaid = (o & OriginFlagSaid) != 0;
            s.OriginMap = s.OriginSaid ? (o & 0xFFF) - 1 : -1;
            s.OnTaxi = (o & OriginFlagTaxi) != 0;
            s.CellX = s.OriginSaid ? Signed10(cl) : 0;
            s.CellY = s.OriginSaid ? Signed10(cl >> 10) : 0;
            s.CellZ = s.OriginSaid ? Signed10(cl >> 20) : 0;
            s.MountDisplayId = from[AtMount] & 0xFFFFFF;
            s.CharacterGuid = (ulong)(uint)from[AtCharLow] | ((ulong)(uint)from[AtCharHigh] << 32);
            s.CharacterNameHash = from[AtCharName];
            s.Ride = RideWire.Unpack(from);
            s.Cast = UnpackCast(from);
            UnpackLook(from, ref s);
            return true;
        }

        /// <summary>
        /// A character name as one int, the same on every client: FNV-1a 32 over the UTF-16 code units of the upper-cased
        /// (invariant) name; 0 for none, and a real hash of 0 becomes 1 (0 means "not said"). Character guids are small counters
        /// per realm database (AzerothCore: HighGuid::Player is 0), so two realms easily share one - the name tells them apart.
        /// </summary>
        public static int NameHash(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.ToUpperInvariant();
            uint h = 2166136261u;
            for (int i = 0; i < n.Length; i++)
            {
                h ^= n[i];
                h *= 16777619u;
            }
            return h == 0 ? 1 : (int)h;
        }

        static bool Set(int[] a, int i, int v)
        {
            if (a[i] == v) return false;
            a[i] = v;
            return true;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>A cell index as 10 bits (two's complement, clamped to -512..511).</summary>
        static int Cell10(int v) { return Clamp(v, -512, 511) & 0x3FF; }

        /// <summary>The low 10 bits of v as a signed cell index.</summary>
        static int Signed10(int v) { v &= 0x3FF; return v >= 512 ? v - 1024 : v; }
    }

    /// <summary>
    /// Iteration 7 (owner Remote): the writer's side of ints 17-27 - which changes are worth rewriting, so a steady ride changes
    /// only the stage-B ints 21-27 (PLAN7 s4.7): the gait rate is rewritten in 0.05 steps (or with a new gait), the relative pose
    /// only past 5 mm / 0.2 deg (constant on desktop; in VR it moves when the seat re-rests the head). Pure (RideWireCompat runs it).
    /// </summary>
    public static class WowRideWriter
    {
        public const float RelMoveMetres = 0.005f, RelTurnDegrees = 0.2f, RateStep = 0.05f;   // ours (PLAN7 s4.7)

        /// <summary>The rate as the wire carries it (x100, 0..5.11): what a viewer reads back.</summary>
        public static float QuantisedRate(float rate)
        {
            if (float.IsNaN(rate) || rate <= 0f) return 0f;
            int v = (int)Math.Round(rate * 100f);
            return (v > 511 ? 511 : v) / 100f;
        }

        /// <summary>next with the gait rate and the relative pose of sent kept while they changed less than the steps above.</summary>
        public static RideRecord Steady(in RideRecord sent, RideRecord next)
        {
            next.GaitRate = QuantisedRate(next.GaitRate);
            if (sent.Kind == 0 || next.Kind == 0) return next;
            if (sent.GaitAnim == next.GaitAnim && Math.Abs(next.GaitRate - sent.GaitRate) < RateStep - 0.001f) next.GaitRate = sent.GaitRate;
            if (next.RelativeSaid && sent.RelativeSaid)
            {
                RideVec d = next.RelPos - sent.RelPos;
                double turn = (next.RelYawDeg - sent.RelYawDeg) % 360.0;
                if (turn > 180.0) turn -= 360.0;
                else if (turn < -180.0) turn += 360.0;
                if (d.Length <= RelMoveMetres && Math.Abs(turn) <= RelTurnDegrees)
                {
                    next.RelPos = sent.RelPos;
                    next.RelYawDeg = sent.RelYawDeg;
                }
            }
            return next;
        }
    }

    /// <summary>
    /// Stage O2 (owner Sync): the writer's side of ints 28-30 - the cast timing (int 29) is written once per cast and rewritten only
    /// when a viewer extrapolating from the last written timing would be more than ResyncProgress off (a pushback, a channel cut short),
    /// so a steady cast changes nothing between its start and its end. Pure (the O2 sync probe runs it). Also the viewer's
    /// extrapolation (<see cref="Predict"/>).
    /// </summary>
    public sealed class WowCastWriter
    {
        /// <summary>Ours: how far (fraction of the cast) the viewers' extrapolated progress may drift before int 29 is rewritten.</summary>
        public const float ResyncProgress = 0.03f;

        private WowCastRecord _sent;
        private double _timingAt;
        private bool _have;
        private int _timingWrites;

        /// <summary>How often the timing was (re)written since the start (for the log and the probe).</summary>
        public int TimingWrites { get { return _timingWrites; } }

        /// <summary>The progress a viewer draws <paramref name="seconds"/> after it read the timing of c (0..1; 0 when the length is unknown).</summary>
        public static float Predict(in WowCastRecord c, double seconds)
        {
            if (c.TotalMs <= 0) return c.State == WowCastState.Held ? 1f : 0f;
            if (c.State == WowCastState.Held) return 1f;
            double p = c.Progress + (seconds < 0.0 ? 0.0 : seconds) * 1000.0 / c.TotalMs;
            return p <= 0.0 ? 0f : (p >= 1.0 ? 1f : (float)p);
        }

        /// <summary>The progress as the wire carries it (x1000).</summary>
        public static float Quantised(float progress)
        {
            if (float.IsNaN(progress) || progress <= 0f) return 0f;
            if (progress >= 1f) return 1f;
            return (float)Math.Round(progress * 1000.0) / 1000f;
        }

        /// <summary>next as it goes on the wire at time now (seconds, any clock): the timing of the last cast written kept while the
        /// same cast runs and the viewers' extrapolation stays within ResyncProgress; everything else as given.</summary>
        public WowCastRecord Step(WowCastRecord next, double now)
        {
            if (!next.Casting)
            {
                next.SpellId = 0; next.State = WowCastState.None; next.Hand = WowCastHand.NotSaid; next.Counter = 0; next.Immersive = false;
                next.TotalMs = 0; next.Progress = 0f;
                _sent = next;
                _have = true;
                return next;
            }
            if (next.TotalMs < 0) next.TotalMs = 0;
            if (next.TotalMs > WowNetWire.MaxCastMs) next.TotalMs = WowNetWire.MaxCastMs;
            next.Progress = next.TotalMs > 0 ? Quantised(next.Progress) : 0f;
            bool same = _have && _sent.Casting && _sent.SpellId == next.SpellId && _sent.State == next.State && _sent.Counter == next.Counter && _sent.TotalMs == next.TotalMs;
            if (same)
            {
                float predicted = Predict(_sent, now - _timingAt);
                if (Math.Abs(predicted - next.Progress) <= ResyncProgress)
                {
                    next.Progress = _sent.Progress;
                    _sent = next;
                    return next;
                }
            }
            _timingAt = now;
            _timingWrites++;
            _sent = next;
            _have = true;
            return next;
        }

        public void Reset()
        {
            _sent = default(WowCastRecord);
            _have = false;
            _timingAt = 0.0;
        }
    }

    /// <summary>
    /// What every client makes of the player slots it reads (EtsSlotRules' rules, unchanged in meaning). "present" is the
    /// room's ActivePlayers as PlayerIds, "slot" a slot's ints as last read.
    /// </summary>
    public static class WowSlotRules
    {
        public static bool Contains(IReadOnlyList<int> present, int playerId)
        {
            if (present == null || playerId < 0) return false;
            for (int i = 0; i < present.Count; i++) if (present[i] == playerId) return true;
            return false;
        }

        /// <summary>A slot this client may claim: never written, written free, owned by a player no longer in the room, or already ours.</summary>
        public static bool FreeFor(int[] slot, int localId, IReadOnlyList<int> present)
        {
            if (slot == null || slot.Length < WowNetWire.Used || slot[WowNetWire.AtMagic] != WowNetWire.Magic) return true;
            int owner = WowNetWire.OwnerOf(slot);
            return owner < 0 || owner == localId || !Contains(present, owner);
        }

        /// <summary>Written, its owner in the room and not this client.</summary>
        public static bool Listed(int[] slot, int localId, IReadOnlyList<int> present)
        {
            int owner = WowNetWire.OwnerOf(slot);
            return owner >= 0 && owner != localId && Contains(present, owner);
        }

        /// <summary>A slot whose player is drawn with weapons and a nameplate: Listed and playing.</summary>
        public static bool Counts(int[] slot, int localId, IReadOnlyList<int> present)
        {
            return Listed(slot, localId, present) && WowNetWire.StatusOf(slot) == WowNetStatus.Playing;
        }

        /// <summary>
        /// Housekeeping by whoever holds a slot's authority: write it free when it names a player who has left (the master
        /// gets the orphans from SceneNetworking), or this client itself while it is not the slot this client claims.
        /// </summary>
        public static bool ToFree(int[] slot, bool haveAuthority, bool claimedHere, int localId, IReadOnlyList<int> present)
        {
            if (!haveAuthority || claimedHere || slot == null || slot.Length < WowNetWire.Used || slot[WowNetWire.AtMagic] != WowNetWire.Magic) return false;
            int owner = WowNetWire.OwnerOf(slot);
            return owner >= 0 && (owner == localId || !Contains(present, owner));
        }
    }

    /// <summary>
    /// This client's claim on one player slot: EtsSlotClaim's state machine (Idle -> Asking -> Confirming -> Owned, the
    /// same timings), which the ETS probe room ran against a simulated Fusion through joins, leaves, simultaneous claims
    /// and a master hand-over. WowNet feeds it what the room says each frame and carries out what it asks
    /// (RequestStateAuthority on AskAuthority; the claim record on WriteClaim). Nothing is sent on leaving: the slot's
    /// owner stops being present and whoever holds the authority writes it free.
    /// </summary>
    public sealed class WowSlotClaim
    {
        public const int Slots = 50;
        public const double AuthorityWait = 3.0;
        public const double ConfirmAfter = 1.0;
        public const double AskEvery = 1.0;
        public const double LostPatience = 1.5;
        public const double RoundPause = 5.0;

        public enum Stage { Idle, Asking, Confirming, Owned }

        public struct Inputs
        {
            public double Now;
            public int LocalId;
            public bool Allowed;     // the room is up and the slots are found
            public int SlotCount;
        }

        public Stage Current { get; private set; }
        public int Slot { get; private set; } = -1;
        public int Generation { get; private set; }
        public int AskAuthority { get; private set; } = -1;
        public bool WriteClaim { get; private set; }
        public bool Holding { get { return Slot >= 0 && (Current == Stage.Confirming || Current == Stage.Owned); } }
        public double ClaimSeconds { get; private set; }

        private readonly bool[] _tried = new bool[Slots];
        private double _since, _nextAsk, _lostAt = -1.0, _roundAt = double.NegativeInfinity, _began;
        private int _localId = -1;
        private string _note;

        public string TakeNote()
        {
            string note = _note;
            _note = null;
            return note;
        }

        public void Reset()
        {
            Current = Stage.Idle;
            Slot = -1;
            _lostAt = -1.0;
            Array.Clear(_tried, 0, _tried.Length);
            _roundAt = double.NegativeInfinity;
        }

        public void Step(in Inputs input, int[][] slots, bool[] valid, bool[] authority, IReadOnlyList<int> present)
        {
            AskAuthority = -1;
            WriteClaim = false;

            if (!input.Allowed || input.LocalId < 0 || input.LocalId != _localId)
            {
                if (Current != Stage.Idle || Slot >= 0)
                    _note = "claim dropped (" + (!input.Allowed ? "the room is not up" : "a new local player id") + ")";
                Reset();
                _localId = input.LocalId;
                if (!input.Allowed || input.LocalId < 0) return;
            }

            double now = input.Now;
            int count = Math.Min(Math.Min(input.SlotCount, Slots), slots.Length);

            switch (Current)
            {
                case Stage.Idle:
                {
                    if (now < _roundAt) return;
                    int k = FindFree(input.LocalId, count, slots, valid, present);
                    if (k < 0)
                    {
                        bool any = false;
                        for (int i = 0; i < count; i++) any |= _tried[i];
                        Array.Clear(_tried, 0, _tried.Length);
                        _roundAt = now + RoundPause;
                        _note = any ? "no slot could be claimed this round; trying all again in " + RoundPause + " s" : "no free slot (" + count + " slots)";
                        return;
                    }
                    Slot = k;
                    Current = Stage.Asking;
                    _since = now;
                    _began = now;
                    _nextAsk = now + AskEvery;
                    AskAuthority = k;
                    return;
                }

                case Stage.Asking:
                {
                    if (!valid[Slot] || !WowSlotRules.FreeFor(slots[Slot], input.LocalId, present))
                    {
                        Give("slot " + Slot + " taken by player " + WowNetWire.OwnerOf(slots[Slot]) + " before the claim; trying the next");
                        return;
                    }
                    if (authority[Slot])
                    {
                        Generation = (WowNetWire.GenerationOf(slots[Slot]) + 1) & 0xFF;
                        WriteClaim = true;
                        Current = Stage.Confirming;
                        _since = now;
                        return;
                    }
                    if (now - _since >= AuthorityWait)
                    {
                        Give("no authority over slot " + Slot + " in " + AuthorityWait + " s; trying the next");
                        return;
                    }
                    if (now >= _nextAsk)
                    {
                        AskAuthority = Slot;
                        _nextAsk = now + AskEvery;
                    }
                    return;
                }

                case Stage.Confirming:
                {
                    int owner = WowNetWire.OwnerOf(slots[Slot]);
                    if (owner >= 0 && owner != input.LocalId && WowSlotRules.Contains(present, owner))
                    {
                        Give("slot " + Slot + " lost to player " + owner + ", trying " + ((Slot + 1) % Math.Max(1, count)));
                        return;
                    }
                    if (!authority[Slot])
                    {
                        if (owner == input.LocalId)
                        {
                            // Our record stands but a later request took the authority (see EtsSlotClaim): ask it back
                            // after LostPatience and confirm afresh once it is here again.
                            if (_lostAt < 0.0)
                            {
                                _lostAt = now;
                                _nextAsk = now + LostPatience;
                            }
                            else if (now >= _nextAsk)
                            {
                                AskAuthority = Slot;
                                _nextAsk = now + AskEvery;
                            }
                            return;
                        }
                        if (now - _since >= AuthorityWait) Give("authority over slot " + Slot + " went elsewhere and nobody wrote it; trying the next");
                        return;
                    }
                    if (_lostAt >= 0.0)
                    {
                        _lostAt = -1.0;
                        _since = now;
                        return;
                    }
                    if (owner != input.LocalId)
                    {
                        if (now - _since >= ConfirmAfter)
                        {
                            WriteClaim = true;
                            _since = now;
                        }
                        return;
                    }
                    if (now - _since >= ConfirmAfter)
                    {
                        Current = Stage.Owned;
                        ClaimSeconds = now - _began;
                        _lostAt = -1.0;
                        Array.Clear(_tried, 0, _tried.Length);
                        _note = "slot " + Slot + " claimed (generation " + Generation + ") in " + Math.Round(ClaimSeconds * 1000.0) + " ms";
                    }
                    return;
                }

                case Stage.Owned:
                {
                    int owner = WowNetWire.OwnerOf(slots[Slot]);
                    if (owner >= 0 && owner != input.LocalId && WowSlotRules.Contains(present, owner))
                    {
                        int lost = Slot;
                        Reset();
                        _note = "slot " + lost + " was taken over by player " + owner + "; claiming again";
                        return;
                    }
                    if (!authority[Slot])
                    {
                        if (_lostAt < 0.0)
                        {
                            _lostAt = now;
                            _nextAsk = now + LostPatience;
                        }
                        else if (now >= _nextAsk)
                        {
                            AskAuthority = Slot;
                            _nextAsk = now + AskEvery;
                        }
                        return;
                    }
                    _lostAt = -1.0;
                    if (owner != input.LocalId)
                    {
                        Generation = (Generation + 1) & 0xFF;
                        WriteClaim = true;
                        _note = "slot " + Slot + " read " + (owner < 0 ? "free" : "as player " + owner + "'s (gone)") + " while held; claimed again (generation " + Generation + ")";
                    }
                    return;
                }
            }
        }

        private void Give(string why)
        {
            if (Slot >= 0 && Slot < _tried.Length) _tried[Slot] = true;
            Current = Stage.Idle;
            Slot = -1;
            _lostAt = -1.0;
            _note = why;
        }

        private int FindFree(int localId, int count, int[][] slots, bool[] valid, IReadOnlyList<int> present)
        {
            if (count <= 0) return -1;
            int k0 = localId % count;
            for (int i = 0; i < count; i++)
            {
                int k = (k0 + i) % count;
                if (valid[k] && !_tried[k] && WowSlotRules.FreeFor(slots[k], localId, present)) return k;
            }
            return -1;
        }
    }
}
