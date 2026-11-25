using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online.Net
{
    /// <summary>One visible item on a character-list entry (per slot: uint32 DisplayInfoID, uint8 InventoryType, uint32 enchant aura).</summary>
    public struct CharItem
    {
        public int DisplayId;        // ItemDisplayInfo.dbc id, 0 = empty
        public int InventoryType;    // Item.dbc InventoryType
        public int EnchantAura;      // SpellItemEnchantment aura_id (weapon glow), 0 = none
    }

    /// <summary>
    /// One character of SMSG_CHAR_ENUM, layout from AzerothCore Player::BuildEnumData:
    /// guid u64, name cstring, race, class, gender, skin, face, hairStyle, hairColor, facialStyle, level (u8), zone u32, map u32,
    /// x y z float, guild id u32, character flags u32, customize flags u32, first login u8, pet display/level/family u32 x3,
    /// then INVENTORY_SLOT_BAG_END (23) x { u32 display, u8 inventory type, u32 enchant aura }.
    /// </summary>
    public sealed class CharInfo
    {
        public const int ItemSlots = 23;      // 19 equipment slots + 4 bags (INVENTORY_SLOT_BAG_END)
        public ulong Guid;
        public string Name;
        public int Race, Class, Gender, Skin, Face, HairStyle, HairColor, FacialHair, Level;
        public int Zone;                      // 0 on first login (AT_LOGIN_FIRST hides it)
        public int Map;
        public float X, Y, Z;
        public int GuildId;
        public uint Flags;                    // CHARACTER_FLAG_* as sent (raw)
        public uint CustomizeFlags;           // CHAR_CUSTOMIZE_FLAG_* (raw): 0 none, else the character must be customized/changed first
        public bool FirstLogin;
        public int PetDisplayId, PetLevel, PetFamily;
        public CharItem[] Items = new CharItem[ItemSlots];

        public override string ToString()
        {
            return Name + " (guid " + Guid + ", level " + Level + " race " + Race + " class " + Class + ", map " + Map + " at " +
                   X.ToString("0.0") + " " + Y.ToString("0.0") + " " + Z.ToString("0.0") + ")";
        }

        public static CharInfo Read(WowPacket p)
        {
            var c = new CharInfo();
            c.Guid = p.U64();
            c.Name = p.CString();
            c.Race = p.U8(); c.Class = p.U8(); c.Gender = p.U8();
            c.Skin = p.U8(); c.Face = p.U8(); c.HairStyle = p.U8(); c.HairColor = p.U8(); c.FacialHair = p.U8();
            c.Level = p.U8();
            c.Zone = (int)p.U32();
            c.Map = (int)p.U32();
            c.X = p.F32(); c.Y = p.F32(); c.Z = p.F32();
            c.GuildId = (int)p.U32();
            c.Flags = p.U32();
            c.CustomizeFlags = p.U32();
            c.FirstLogin = p.U8() != 0;
            c.PetDisplayId = (int)p.U32(); c.PetLevel = (int)p.U32(); c.PetFamily = (int)p.U32();
            for (int i = 0; i < ItemSlots; i++)
            {
                c.Items[i].DisplayId = (int)p.U32();
                c.Items[i].InventoryType = p.U8();
                c.Items[i].EnchantAura = (int)p.U32();
            }
            return c;
        }

        /// <summary>SMSG_CHAR_ENUM: u8 count, then entries (WorldSession::HandleCharEnum).</summary>
        public static List<CharInfo> ReadEnum(WowPacket p)
        {
            int n = p.U8();
            var list = new List<CharInfo>(n);
            for (int i = 0; i < n; i++) list.Add(Read(p));
            return list;
        }

        /// <summary>Writes the same layout (the harness's fake server uses it).</summary>
        public void Write(PacketWriter w)
        {
            w.U64(Guid).CString(Name).U8(Race).U8(Class).U8(Gender).U8(Skin).U8(Face).U8(HairStyle).U8(HairColor).U8(FacialHair).U8(Level);
            w.U32((uint)Zone).U32((uint)Map).F32(X).F32(Y).F32(Z).U32((uint)GuildId).U32(Flags).U32(CustomizeFlags).Bool(FirstLogin);
            w.U32((uint)PetDisplayId).U32((uint)PetLevel).U32((uint)PetFamily);
            for (int i = 0; i < ItemSlots; i++) w.U32((uint)Items[i].DisplayId).U8(Items[i].InventoryType).U32((uint)Items[i].EnchantAura);
        }
    }

    /// <summary>CMSG_CHAR_CREATE body (WorldSession::HandleCharCreateOpcode reads name, then 9 uint8: race, class, gender, skin, face,
    /// hairStyle, hairColor, facialHair, outfitId).</summary>
    public sealed class CharCreateRequest
    {
        public string Name;
        public int Race, Class, Gender, Skin, Face, HairStyle, HairColor, FacialHair, OutfitId;

        public byte[] Build()
        {
            return new PacketWriter().CString(Name).U8(Race).U8(Class).U8(Gender).U8(Skin).U8(Face).U8(HairStyle).U8(HairColor)
                .U8(FacialHair).U8(OutfitId).ToArray();
        }
    }

    /// <summary>SMSG_LOGIN_VERIFY_WORLD: map u32, x y z orientation floats (WorldSession::HandlePlayerLoginFromDB).</summary>
    public struct LoginVerifyWorld
    {
        public int Map;
        public float X, Y, Z, O;
    }
}
