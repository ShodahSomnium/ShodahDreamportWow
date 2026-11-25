using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>OBJECT_UPDATE_TYPE / OBJECT_UPDATE_FLAGS / TypeID (AzerothCore UpdateData.h, ObjectGuid.h).</summary>
    public static class UpdateType
    {
        public const byte Values = 0, Movement = 1, CreateObject = 2, CreateObject2 = 3, OutOfRange = 4, NearObjects = 5;
        public const ushort FlagSelf = 0x0001, FlagTransport = 0x0002, FlagHasTarget = 0x0004, FlagUnknown = 0x0008, FlagLowGuid = 0x0010;
        public const ushort FlagLiving = 0x0020, FlagStationary = 0x0040, FlagVehicle = 0x0080, FlagPosition = 0x0100, FlagRotation = 0x0200;
    }

    public static class TypeId
    {
        public const byte Object = 0, Item = 1, Container = 2, Unit = 3, Player = 4, GameObject = 5, DynamicObject = 6, Corpse = 7;

        /// <summary>Size of the values array of a type (UpdateFields.h *_END).</summary>
        public static int ValuesCount(byte typeId)
        {
            switch (typeId)
            {
                case Item: return UF.ITEM_END;
                case Container: return UF.CONTAINER_END;
                case Unit: return UF.UNIT_END;
                case Player: return UF.PLAYER_END;
                case GameObject: return UF.GAMEOBJECT_END;
                case DynamicObject: return UF.DYNAMICOBJECT_END;
                case Corpse: return UF.CORPSE_END;
                default: return UF.OBJECT_END;
            }
        }
    }

    /// <summary>The movement part of a create/movement block (AzerothCore Object::BuildMovementUpdate).</summary>
    public sealed class MovementBlock
    {
        public ushort Flags;
        public MovementInfo Info;                       // UPDATEFLAG_LIVING
        public readonly float[] Speeds = new float[MoveType.Count];
        public SplinePath Spline;                       // LIVING + MOVEMENTFLAG_SPLINE_ENABLED
        public int SplineTimePassedMs;
        public bool HasPosition;                        // POSITION (0x100) or STATIONARY (0x40): X,Y,Z,O below
        public float X, Y, Z, O;
        public ulong TransportGuid;                     // POSITION: packed transport guid (0 = none)
        public float TX, TY, TZ;                        // POSITION with a transport: offsets on it
        public uint LowGuid, Unknown;
        public ulong TargetGuid;                        // HAS_TARGET
        public uint TransportPathProgress;              // TRANSPORT
        public uint VehicleId; public float VehicleO;   // VEHICLE
        public long PackedRotation;                     // ROTATION (GameObject::GetPackedWorldRotation)
        public bool HasRotation { get { return (Flags & UpdateType.FlagRotation) != 0; } }

        /// <summary>
        /// Unpacks GameObject::UpdatePackedRotation: z | y &lt;&lt; 21 | x &lt;&lt; 42 with x 22 bits / 2^21 and y, z 21 bits / 2^20, the
        /// quaternion sign-normalised so w &gt;= 0.
        /// </summary>
        public static void UnpackRotation(long packed, out float x, out float y, out float z, out float w)
        {
            long lx = packed >> 42;                                  // top 22 bits, sign-extended by the arithmetic shift
            long ly = (packed << 22) >> 43;                          // bits 21..41
            long lz = (packed << 43) >> 43;                          // bits 0..20
            x = lx / (float)(1 << 21);
            y = ly / (float)(1 << 20);
            z = lz / (float)(1 << 20);
            float ww = 1f - (x * x + y * y + z * z);
            w = ww > 0f ? (float)Math.Sqrt(ww) : 0f;
        }
    }

    /// <summary>One block of SMSG_UPDATE_OBJECT.</summary>
    public sealed class UpdateBlock
    {
        public byte Type;                               // UpdateType
        public ulong Guid;
        public byte TypeId;                             // create blocks
        public MovementBlock Movement;                  // create / movement blocks
        public int[] Fields;                            // values: changed field indices (ascending)
        public uint[] Values;                           // values: their uint32 values
        public List<ulong> Guids;                       // out of range / near objects
    }

    /// <summary>
    /// SMSG_UPDATE_OBJECT (and the inflated SMSG_COMPRESSED_UPDATE_OBJECT) of 3.3.5a as AzerothCore writes it: UpdateData::BuildPacket
    /// = uint32 block count, [uint8 OUT_OF_RANGE_OBJECTS, uint32 n, n packed guids], then blocks built by Object::Build*Block:
    /// VALUES = packed guid + values; MOVEMENT = packed guid + movement; CREATE_OBJECT(2) = packed guid, uint8 typeId, movement,
    /// values. Values (Object::BuildValuesUpdate + UpdateMask::AppendToPacket) = uint8 mask block count, that many uint32 mask
    /// words, one uint32 per set bit in ascending index order.
    /// </summary>
    public static class UpdateObjectParser
    {
        /// <summary>Parses every block; on malformed data returns the blocks read so far and sets error.</summary>
        public static List<UpdateBlock> Parse(WowPacket p, out string error)
        {
            var list = new List<UpdateBlock>();
            error = null;
            int count = -1, i = 0;
            try
            {
                count = (int)p.U32();
                if (count < 0 || count > 100000) throw new FormatException("block count " + (uint)count);
                for (; i < count; i++)
                {
                    var b = new UpdateBlock { Type = p.U8() };
                    switch (b.Type)
                    {
                        case UpdateType.Values:
                            b.Guid = p.PackedGuid();
                            ReadValues(p, b);
                            break;
                        case UpdateType.Movement:
                            b.Guid = p.PackedGuid();
                            b.Movement = ReadMovement(p);
                            break;
                        case UpdateType.CreateObject:
                        case UpdateType.CreateObject2:
                            b.Guid = p.PackedGuid();
                            b.TypeId = p.U8();
                            b.Movement = ReadMovement(p);
                            ReadValues(p, b);
                            break;
                        case UpdateType.OutOfRange:
                        case UpdateType.NearObjects:
                        {
                            uint n = p.U32();
                            if (n > 100000) throw new FormatException("guid list of " + n);
                            b.Guids = new List<ulong>((int)n);
                            for (uint k = 0; k < n; k++) b.Guids.Add(p.PackedGuid());
                            break;
                        }
                        default:
                            throw new FormatException("unknown block type " + b.Type);
                    }
                    list.Add(b);
                }
                if (p.Remaining != 0) error = p.Remaining + " bytes left after " + count + " blocks";
            }
            catch (Exception e)
            {
                error = "block " + i + "/" + count + " at byte " + p.Pos + ": " + e.GetType().Name + " " + e.Message;
            }
            return list;
        }

        static void ReadValues(WowPacket p, UpdateBlock b)
        {
            int blocks = p.U8();
            var masks = new uint[blocks];
            int set = 0;
            for (int k = 0; k < blocks; k++)
            {
                masks[k] = p.U32();
                uint m = masks[k];
                while (m != 0) { set++; m &= m - 1; }
            }
            b.Fields = new int[set];
            b.Values = new uint[set];
            int n = 0;
            for (int k = 0; k < blocks; k++)
            {
                uint m = masks[k];
                for (int bit = 0; m != 0 && bit < 32; bit++)
                {
                    if ((m & (1u << bit)) == 0) continue;
                    m &= ~(1u << bit);
                    b.Fields[n] = k * 32 + bit;
                    b.Values[n] = p.U32();
                    n++;
                }
            }
        }

        public static MovementBlock ReadMovement(WowPacket p)
        {
            var m = new MovementBlock();
            m.Flags = p.U16();
            if ((m.Flags & UpdateType.FlagLiving) != 0)
            {
                m.Info = new MovementInfo();
                m.Info.Read(p);                              // Unit::BuildMovementPacket
                for (int k = 0; k < MoveType.Count; k++) m.Speeds[k] = p.F32();
                if ((m.Info.Flags & MoveFlags.SplineEnabled) != 0) m.Spline = SplinePath.ReadCreate(p, out m.SplineTimePassedMs);
                m.HasPosition = true;
                m.X = m.Info.X; m.Y = m.Info.Y; m.Z = m.Info.Z; m.O = m.Info.O;
            }
            else if ((m.Flags & UpdateType.FlagPosition) != 0)
            {
                m.TransportGuid = p.PackedGuid();
                m.X = p.F32(); m.Y = p.F32(); m.Z = p.F32();
                if (m.TransportGuid != 0) { m.TX = p.F32(); m.TY = p.F32(); m.TZ = p.F32(); }
                else { p.F32(); p.F32(); p.F32(); }        // the position again
                m.O = p.F32();
                p.F32();                                     // corpse orientation / 0
                m.HasPosition = true;
            }
            else if ((m.Flags & UpdateType.FlagStationary) != 0)
            {
                m.X = p.F32(); m.Y = p.F32(); m.Z = p.F32(); m.O = p.F32();
                m.HasPosition = true;
            }
            if ((m.Flags & UpdateType.FlagUnknown) != 0) m.Unknown = p.U32();
            if ((m.Flags & UpdateType.FlagLowGuid) != 0) m.LowGuid = p.U32();
            if ((m.Flags & UpdateType.FlagHasTarget) != 0) m.TargetGuid = p.PackedGuid();
            if ((m.Flags & UpdateType.FlagTransport) != 0) m.TransportPathProgress = p.U32();
            if ((m.Flags & UpdateType.FlagVehicle) != 0) { m.VehicleId = p.U32(); m.VehicleO = p.F32(); }
            if ((m.Flags & UpdateType.FlagRotation) != 0) m.PackedRotation = (long)p.U64();
            return m;
        }
    }
}
