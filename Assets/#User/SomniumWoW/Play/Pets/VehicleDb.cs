// iteration 11 - owner vehicle (proposal-pets.md s4.6 / 4.7 / 6.0 / 3.9 / 3.11). Pure C#: no UnityEngine.
// Vehicle.dbc / VehicleSeat.dbc / CreatureFamily.dbc / StableSlotPrices.dbc / SummonProperties.dbc read at runtime from the player's own
// client files (nothing shipped). Field indices = AzerothCore DBCStructure.h:2062-2147 (VehicleEntry / VehicleSeatEntry), :780-794
// (CreatureFamilyEntry), DBCfmt.h (P/acsrc11/pets), verified by the designer's probe PetsDbc (P/harness/Probes/D11/pets/PetsDbcProbe.cs).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Pets
{
    /// <summary>A Vehicle.dbc row.</summary>
    public sealed class VehicleInfo
    {
        public int Id;
        public uint Flags;                       // m_flags (VehicleDb.Vf*)
        public float TurnSpeed, PitchSpeed;      // rad/s
        public float PitchMinRaw, PitchMaxRaw;   // the DBC pair (used only with VEHICLE_FLAG_CUSTOM_PITCH)
        public readonly int[] Seats = new int[8];   // m_seatID[8], 0 = none
        public int SeatCount;                    // seats != 0

        /// <summary>VEHICLE_FLAG_CUSTOM_PITCH 0x40: the DBC pitch range, else -pi/2..pi/2 (VehicleDefines.h:45).</summary>
        public float PitchMin { get { return (Flags & VehicleDb.VfCustomPitch) != 0 ? PitchMinRaw : -(float)(Math.PI / 2); } }
        public float PitchMax { get { return (Flags & VehicleDb.VfCustomPitch) != 0 ? PitchMaxRaw : (float)(Math.PI / 2); } }
        public bool AllowPitching { get { return (Flags & VehicleDb.VfAllowPitching) != 0; } }
        public bool AimAdjustable { get { return (Flags & VehicleDb.VfAdjustAimAngle) != 0; } }
    }

    /// <summary>A VehicleSeat.dbc row.</summary>
    public sealed class VehicleSeatInfo
    {
        public int Id;
        public uint Flags, FlagsB;               // m_flags (VehicleDb.Sf*), m_flagsB
        public int AttachmentId;                 // m_attachmentID (a client enumeration - not an M2 attachment id, s4.6)
        public float OffX, OffY, OffZ;           // m_attachmentOffsetX/Y/Z (yards, the vehicle's frame)
        public int RideAnimLoop;                 // m_rideAnimLoop (AnimationData id)
        public int EnterSound, ExitSound;        // m_enterUISoundID / m_exitUISoundID (SoundEntries.dbc)
        public int UiSkin;                       // m_uiSkin: 0 Mechanical, 1 Natural (VehicleMenuBar.lua)

        /// <summary>VehicleSeatEntry::CanEnterOrExit (DBCStructure.h:2149-2154).</summary>
        public bool CanEnterOrExit { get { return (Flags & VehicleDb.SfCanEnterOrExit) != 0 || (Flags & (VehicleDb.SfLowerAnimEnter | VehicleDb.SfLowerAnimRide)) != 0; } }
        public bool CanSwitch { get { return (Flags & VehicleDb.SfCanSwitch) != 0; } }
        public bool CanControl { get { return (Flags & VehicleDb.SfCanControl) != 0; } }
        public bool HidePassenger { get { return (Flags & VehicleDb.SfHidePassenger) != 0; } }
        public bool CanAttack { get { return (Flags & VehicleDb.SfCanAttack) != 0; } }
        /// <summary>VEHICLE_SEAT_FLAG_CAN_CAST "Lua_UnitHasVehicleUI" (DBCEnums.h:473): WoW's VehicleMenuBar.</summary>
        public bool HasVehicleUi { get { return (Flags & VehicleDb.SfCanCast) != 0; } }
    }

    /// <summary>A CreatureFamily.dbc row (pets).</summary>
    public sealed class CreatureFamilyInfo
    {
        public int Id;
        public string Name;                      // field 10 (localized)
        public string Icon;                      // field 27 (Interface\Icons\...)
        public int SkillLine1, SkillLine2;       // fields 5 / 6
        public int FoodMask;                     // field 7
        public int PetTalentType;                // field 8
    }

    public static class VehicleDb
    {
        // ---- Vehicle.dbc fields (DBCStructure.h:2062-2097)
        public const int VehFlags = 1, VehTurnSpeed = 2, VehPitchSpeed = 3, VehPitchMin = 4, VehPitchMax = 5, VehSeat0 = 6;
        // ---- VehicleSeat.dbc fields (DBCStructure.h:2099-2147)
        public const int SeatFlags = 1, SeatAttachmentId = 2, SeatOffsetX = 3, SeatRideAnimLoop = 16, SeatEnterSound = 42, SeatExitSound = 43,
                         SeatUiSkin = 44, SeatFlagsB = 45;
        // ---- CreatureFamily.dbc (DBCStructure.h:780-794, fmt "nfifiiiiixssssssssssssssssxx")
        public const int FamSkill1 = 5, FamSkill2 = 6, FamFoodMask = 7, FamTalentType = 8, FamName = 10, FamIcon = 27;
        // ---- StableSlotPrices.dbc ("ni"), SummonProperties.dbc ("niiiii": id, category, faction, type, slot, flags)
        public const int StablePrice = 1, SummonType = 3, SummonSlot = 4;
        /// <summary>SUMMON_TYPE_MINIPET (SharedDefines.h:3532): a companion.</summary>
        public const int SummonTypeMinipet = 5;

        // ---- VehicleFlags (VehicleDefines.h:38-47)
        public const uint VfNoStrafe = 0x1, VfNoJumping = 0x2, VfFullSpeedTurning = 0x4, VfAllowPitching = 0x10, VfFullSpeedPitching = 0x20,
                          VfCustomPitch = 0x40, VfAdjustAimAngle = 0x400, VfAdjustAimPower = 0x800;
        // ---- VehicleSeatFlags (DBCEnums.h:442-475)
        public const uint SfLowerAnimEnter = 0x1, SfLowerAnimRide = 0x2, SfHidePassenger = 0x200, SfCanControl = 0x800, SfCanAttack = 0x4000,
                          SfCanEnterOrExit = 0x02000000, SfCanSwitch = 0x04000000, SfCanCast = 0x20000000;
        // ---- MovementFlags2 (UnitDefines.h:430-435)
        public const ushort F2NoStrafe = 0x1, F2NoJumping = 0x2, F2FullSpeedTurning = 0x8, F2FullSpeedPitching = 0x10, F2AlwaysAllowPitching = 0x20;

        /// <summary>Gravity of a trajectory (AC MovementUtil.cpp:24, P/acsrc11/pets/MovementUtil.cpp).</summary>
        public const float Gravity = 19.29110527f;
        /// <summary>Ours (s3.11): the arc is stepped every 0.1 s of flight, at most 8 s; the shot leaves 2 yd above the vehicle's feet.</summary>
        public const float BallisticStep = 0.1f, BallisticMaxSeconds = 8f, ShotHeight = 2f;

        private static readonly object Lock = new object();
        private static WowFiles _files;
        private static Dictionary<int, VehicleInfo> _vehicles;
        private static Dictionary<int, VehicleSeatInfo> _seats;
        private static Dictionary<int, CreatureFamilyInfo> _families;
        private static Dictionary<int, int> _stablePrices;
        private static Dictionary<int, int> _summonType;
        private static bool _loggedLoad;

        /// <summary>Reads the tables of these files once (cached; the last files win). Called by the online session at start (InitVehicle)
        /// so IsCompanion (which only gets the SpellDb) can answer.</summary>
        public static bool Load(WowFiles files)
        {
            if (files == null) return _vehicles != null;
            lock (Lock)
            {
                if (_vehicles != null && ReferenceEquals(_files, files)) return true;
                var vehicles = new Dictionary<int, VehicleInfo>();
                var seats = new Dictionary<int, VehicleSeatInfo>();
                var families = new Dictionary<int, CreatureFamilyInfo>();
                var prices = new Dictionary<int, int>();
                var summon = new Dictionary<int, int>();
                DbcTable v = null, s = null, f = null, p = null, sp = null;
                try
                {
                    v = Dbc.Load(files, "Vehicle");
                    s = Dbc.Load(files, "VehicleSeat");
                    f = Dbc.Load(files, "CreatureFamily");
                    p = Dbc.Load(files, "StableSlotPrices");
                    sp = Dbc.Load(files, "SummonProperties");
                }
                catch (Exception e) { WowLog.Warn("VehicleDb: reading the vehicle tables threw " + e.Message); }
                if (v != null && v.FieldCount > VehSeat0 + 7)
                    for (int r = 0; r < v.RecordCount; r++)
                    {
                        var vi = new VehicleInfo
                        {
                            Id = v.GetId(r), Flags = v.GetUInt(r, VehFlags), TurnSpeed = v.GetFloat(r, VehTurnSpeed), PitchSpeed = v.GetFloat(r, VehPitchSpeed),
                            PitchMinRaw = v.GetFloat(r, VehPitchMin), PitchMaxRaw = v.GetFloat(r, VehPitchMax)
                        };
                        for (int i = 0; i < 8; i++) { vi.Seats[i] = v.GetInt(r, VehSeat0 + i); if (vi.Seats[i] != 0) vi.SeatCount++; }
                        vehicles[vi.Id] = vi;
                    }
                if (s != null && s.FieldCount > SeatFlagsB)
                    for (int r = 0; r < s.RecordCount; r++)
                    {
                        var si = new VehicleSeatInfo
                        {
                            Id = s.GetId(r), Flags = s.GetUInt(r, SeatFlags), FlagsB = s.GetUInt(r, SeatFlagsB), AttachmentId = s.GetInt(r, SeatAttachmentId),
                            OffX = s.GetFloat(r, SeatOffsetX), OffY = s.GetFloat(r, SeatOffsetX + 1), OffZ = s.GetFloat(r, SeatOffsetX + 2),
                            RideAnimLoop = s.GetInt(r, SeatRideAnimLoop), EnterSound = s.GetInt(r, SeatEnterSound), ExitSound = s.GetInt(r, SeatExitSound),
                            UiSkin = s.GetInt(r, SeatUiSkin)
                        };
                        seats[si.Id] = si;
                    }
                if (f != null && f.FieldCount > FamIcon)
                    for (int r = 0; r < f.RecordCount; r++)
                        families[f.GetId(r)] = new CreatureFamilyInfo
                        {
                            Id = f.GetId(r), Name = f.GetLocalizedString(r, FamName), Icon = f.GetString(r, FamIcon), SkillLine1 = f.GetInt(r, FamSkill1),
                            SkillLine2 = f.GetInt(r, FamSkill2), FoodMask = f.GetInt(r, FamFoodMask), PetTalentType = f.GetInt(r, FamTalentType)
                        };
                if (p != null && p.FieldCount > StablePrice)
                    for (int r = 0; r < p.RecordCount; r++) prices[p.GetId(r)] = p.GetInt(r, StablePrice);
                if (sp != null && sp.FieldCount > SummonSlot)
                    for (int r = 0; r < sp.RecordCount; r++) summon[sp.GetId(r)] = sp.GetInt(r, SummonType);
                _files = files;
                _vehicles = vehicles; _seats = seats; _families = families; _stablePrices = prices; _summonType = summon;
                if (!_loggedLoad)
                {
                    _loggedLoad = true;
                    WowLog.Info("VehicleDb: " + vehicles.Count + " vehicles, " + seats.Count + " seats, " + families.Count + " creature families, " + prices.Count +
                                " stable slot prices, " + summon.Count + " summon properties read from the game files; ours: an aimed shot leaves " + ShotHeight +
                                " yd above the vehicle, its arc stepped every " + BallisticStep + " s up to " + BallisticMaxSeconds + " s (gravity " + Gravity + ", AC MovementUtil.cpp:24)");
                }
                return vehicles.Count > 0;
            }
        }

        public static VehicleInfo Vehicle(int id)
        {
            VehicleInfo v;
            var d = _vehicles;
            return d != null && id > 0 && d.TryGetValue(id, out v) ? v : null;
        }

        public static VehicleSeatInfo Seat(int seatId)
        {
            VehicleSeatInfo s;
            var d = _seats;
            return d != null && seatId > 0 && d.TryGetValue(seatId, out s) ? s : null;
        }

        /// <summary>The VehicleSeat row of seat index 0..7 of a vehicle (Vehicle.dbc m_seatID[index]); null when unknown.</summary>
        public static VehicleSeatInfo SeatOf(int vehicleId, int seatIndex)
        {
            var v = Vehicle(vehicleId);
            if (v == null || seatIndex < 0 || seatIndex >= 8) return null;
            return Seat(v.Seats[seatIndex]);
        }

        public static CreatureFamilyInfo Family(int id)
        {
            CreatureFamilyInfo f;
            var d = _families;
            return d != null && id > 0 && d.TryGetValue(id, out f) ? f : null;
        }

        public static CreatureFamilyInfo Family(WowFiles files, int id) { Load(files); return Family(id); }

        /// <summary>StableSlotPrices.dbc price (copper) of stable slot 1..4; 0 = unknown.</summary>
        public static int StableSlotPrice(int slot)
        {
            int c;
            var d = _stablePrices;
            return d != null && d.TryGetValue(slot, out c) ? c : 0;
        }

        public static int StableSlotPrice(WowFiles files, int slot) { Load(files); return StableSlotPrice(slot); }

        /// <summary>A companion (minipet) summoning spell: SPELL_EFFECT_SUMMON (28) whose EffectMiscValueB is a SummonProperties row of type 5
        /// SUMMON_TYPE_MINIPET (proposal-pets s3.9). False until the tables are loaded (Load), as before this iteration.</summary>
        public static bool IsCompanion(SpellDb db, SpellInfo s)
        {
            var d = _summonType;
            if (s == null || s.Effects == null || d == null) return false;
            for (int i = 0; i < s.Effects.Length; i++)
            {
                var e = s.Effects[i];
                int type;
                if (e != null && e.Effect == SpellEffect.Summon && d.TryGetValue(e.MiscValueB, out type) && type == SummonTypeMinipet) return true;
            }
            return false;
        }

        /// <summary>The MovementFlags2 the server gives a vehicle (Vehicle::InitMovementInfoForBase, Vehicle.cpp:640-655).</summary>
        public static ushort Flags2(uint vehicleFlags)
        {
            ushort f = 0;
            if ((vehicleFlags & VfNoStrafe) != 0) f |= F2NoStrafe;
            if ((vehicleFlags & VfNoJumping) != 0) f |= F2NoJumping;
            if ((vehicleFlags & VfFullSpeedTurning) != 0) f |= F2FullSpeedTurning;
            if ((vehicleFlags & VfAllowPitching) != 0) f |= F2AlwaysAllowPitching;
            if ((vehicleFlags & VfFullSpeedPitching) != 0) f |= F2FullSpeedPitching;
            return f;
        }

        /// <summary>TransportBase::CalculatePassengerPosition (VehicleDefines.h:143-152): a seat offset (the vehicle's frame) -> world.</summary>
        public static WowPos PassengerPosition(float lx, float ly, float lz, float lo, WowPos vehicle)
        {
            double c = Math.Cos(vehicle.O), s = Math.Sin(vehicle.O);
            float o = (float)((vehicle.O + lo) % (2 * Math.PI));
            if (o < 0f) o += (float)(2 * Math.PI);
            return new WowPos((float)(vehicle.X + lx * c - ly * s), (float)(vehicle.Y + ly * c + lx * s), vehicle.Z + lz, o);
        }

        /// <summary>
        /// Ours (s3.11 - the client's aim maths are inside Wow.exe): where a shot fired from src along facing with elevation pitch at speed v
        /// lands: the arc (gravity 19.29 yd/s^2) stepped every 0.1 s of flight against groundZ(x, y, zHint) (NaN = unknown: the level of src - 2 yd,
        /// the vehicle's feet), linearly interpolated inside the last step; never within 8 s -> the point at rangeMax (when > 0) along facing
        /// at that level, else the 8 s point.
        /// </summary>
        public static WowPos BallisticDest(WowPos src, float facing, float pitch, float v, float rangeMax, Func<float, float, float, float> groundZ)
        {
            double vxy = v * Math.Cos(pitch), vz = v * Math.Sin(pitch);
            double cx = Math.Cos(facing), cy = Math.Sin(facing);
            float flat = src.Z - ShotHeight;
            double px = src.X, py = src.Y, pz = src.Z, pg = Ground(groundZ, src.X, src.Y, src.Z, flat);
            if (v > 0f)
                for (double t = BallisticStep; t <= BallisticMaxSeconds + 1e-4; t += BallisticStep)
                {
                    double nx = src.X + cx * vxy * t, ny = src.Y + cy * vxy * t, nz = src.Z + vz * t - 0.5 * Gravity * t * t;
                    double g = Ground(groundZ, (float)nx, (float)ny, (float)nz, flat);
                    if (nz <= g)
                    {
                        double a = (pz - pg) / Math.Max(1e-6, (pz - pg) - (nz - g));
                        if (a < 0) a = 0; else if (a > 1) a = 1;
                        return new WowPos((float)(px + (nx - px) * a), (float)(py + (ny - py) * a), (float)(pg + (g - pg) * a), facing);
                    }
                    px = nx; py = ny; pz = nz; pg = g;
                }
            double d = rangeMax > 0f ? rangeMax : Math.Sqrt((px - src.X) * (px - src.X) + (py - src.Y) * (py - src.Y));
            float ex = (float)(src.X + cx * d), ey = (float)(src.Y + cy * d);
            return new WowPos(ex, ey, (float)Ground(groundZ, ex, ey, src.Z, flat), facing);
        }

        private static double Ground(Func<float, float, float, float> groundZ, float x, float y, float zHint, float flat)
        {
            if (groundZ == null) return flat;
            float g;
            try { g = groundZ(x, y, Math.Max(zHint, flat) + 2f); }
            catch (Exception) { return flat; }
            return float.IsNaN(g) || float.IsInfinity(g) ? flat : g;
        }
    }
}
