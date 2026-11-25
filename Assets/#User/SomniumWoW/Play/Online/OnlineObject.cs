using System;
using System.Runtime.InteropServices;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>uint &lt;-&gt; float bit pattern without allocating.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct FloatBits
    {
        [FieldOffset(0)] public uint U;
        [FieldOffset(0)] public float F;
        public static float ToFloat(uint u) { var b = new FloatBits(); b.U = u; return b.F; }
        public static uint ToUInt(float f) { var b = new FloatBits(); b.F = f; return b.U; }
    }

    /// <summary>
    /// One object the server told us about (object manager record): its update-field values (UpdateFields.h indices), its last
    /// movement and, for creatures/NPCs/players/gameobjects, the EntityState the views read.
    /// </summary>
    public sealed class OnlineObject
    {
        public ulong Guid;
        public byte TypeId;
        public uint[] V;
        public EntityState State;                // null for items, containers, dynamic objects, corpses and our own player

        // movement
        public MovementInfo Move;                // last MovementInfo of a living object (create block or MSG_MOVE_*)
        public readonly float[] Speeds = (float[])MoveType.Base.Clone();
        public SplinePath Spline;                // active server path (SMSG_MONSTER_MOVE / create spline)
        public double MoveTime;                  // session clock when Move arrived (extrapolation base)
        public bool Landed;                      // lead 2026-10-03: its jump arc reached the ground on our side (Extrapolate) - reset by its next packet
        public float X, Y, Z, O;                 // current world position
        public bool HasPos;
        public ulong TransportGuid;

        public bool QueryAsked;                  // name/creature/gameobject query sent for this object
        // Iteration 7 (owner Online): others' casts, auras and dismounts
        public int CastSpellId;                  // SMSG_SPELL_START of this unit, 0 = not casting
        public double CastStart;                 // session clock of that SPELL_START
        public int CastMs;
        public AuraView[] AuraSlots;             // SMSG_AURA_UPDATE(_ALL) by slot, null until the first
        public long DismountPair = -1;           // (flag, display) its values held at its SMSG_DISMOUNT; -1 = none

        public OnlineObject(ulong guid, byte typeId)
        {
            Guid = guid;
            TypeId = typeId;
            V = new uint[TypeId_ValuesCount(typeId)];
        }

        static int TypeId_ValuesCount(byte t) { return Online.TypeId.ValuesCount(t); }

        public uint U32(int i) { return (uint)i < (uint)V.Length ? V[i] : 0u; }
        public int I32(int i) { return (int)U32(i); }
        public float F32(int i) { return FloatBits.ToFloat(U32(i)); }
        public ulong U64(int i) { return U32(i) | (ulong)U32(i + 1) << 32; }
        public int Byte(int i, int n) { return (int)(U32(i) >> (n * 8)) & 0xFF; }
        public ushort U16(int i, int n) { return (ushort)(U32(i) >> (n * 16)); }

        public void Set(int field, uint value)
        {
            if (field < 0) return;
            if (field >= V.Length)
            {
                if (field > 4096) return;
                var nv = new uint[field + 1];
                Array.Copy(V, nv, V.Length);
                V = nv;
            }
            V[field] = value;
        }

        public bool IsUnit { get { return TypeId == Online.TypeId.Unit || TypeId == Online.TypeId.Player; } }
        public int Entry { get { return I32(UF.OBJECT_FIELD_ENTRY); } }
        /// <summary>ObjectGuid::GetHigh (0xF130 creature, 0xF140 pet, 0xF150 vehicle, 0xF110 gameobject, 0 player...).</summary>
        public int High { get { return (int)(Guid >> 48) & 0xFFFF; } }
    }
}
