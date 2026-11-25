// iteration 11 - owner interact (proposal-interact.md 3.3 / 4.4 / 6.2.3): inspecting another player (InspectFrame).
// AzerothCore master: src/server/game/Handlers/MiscHandler.cpp:977-1049 (HandleInspectOpcode), Entities/Player/Player.cpp:14749-14905
// (BuildPlayerTalentsInfoData / BuildEnchantmentsInfoData), ObjectDefines.h:27 (INSPECT_DISTANCE 28 yd). DBC layouts measured by the
// designer in the user's install: TalentTab (0 ID, 1 Name_lang, 20 ClassMask, 21 PetTalentMask, 22 OrderIndex), Talent (0 ID, 1 TabID),
// GlyphProperties (0 ID, 1 SpellID), SpellItemEnchantment (14 Name_lang).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const float IxInspectYards = 28f;              // INSPECT_DISTANCE (ObjectDefines.h:27)
        const int IxEquipSlots = 19;                   // EQUIPMENT_SLOT_END
        const int IxEnchantSlots = 12;                 // MAX_ENCHANTMENT_SLOT (Item.h:183)
        const int IxTabName = 1, IxTabClassMask = 20, IxTabPetMask = 21, IxTabOrder = 22, IxTalentTab = 1, IxGlyphSpell = 1, IxEnchantNameField = 14;

        InspectView _ixInspect;

        public InspectView Inspection { get { return _ixInspect; } }

        void IxInspectInit()
        {
            IxHandle(Op.SMSG_INSPECT_TALENT, IxInspectOnTalent);
        }

        /// <summary>UnitPopup INSPECT -> InspectUnit: CMSG_INSPECT (uint64). The server answers nothing beyond 28 yd or for a valid attack
        /// target (MiscHandler.cpp:977-996) - refused here with a text. The equipment the object shows (PLAYER_VISIBLE_ITEM_n, PUBLIC) is
        /// drawn at once, the full list (rings, trinkets, enchants) replaces it when SMSG_INSPECT_TALENT arrives.</summary>
        public void InspectPlayer(ulong playerGuid)
        {
            if (playerGuid == 0 || playerGuid == _player.Guid) return;
            if (_world == null) { Error(NotYet); return; }
            OnlineObject o;
            EntityState e = Find(playerGuid);
            if (e == null || e.Kind != EntityKind.Player || !_objects.TryGetValue(playerGuid, out o)) { IxError("Unknown unit."); return; }   // ERR_UNIT_NOT_FOUND (GlobalStrings.lua:3565)
            if (e.Reaction == Reaction.Hostile) { WowLog.Info("Online inspect: " + e.Name + " is hostile - the server would ignore CMSG_INSPECT (MiscHandler.cpp:994-997)"); return; }
            if (IxYards(_player.Pos, e.Pos) > IxInspectYards) { IxError("You need to be closer to interact with that target."); return; }   // ERR_TOO_FAR_TO_INTERACT (:3537) - ours: the closest text
            var v = new InspectView { Guid = playerGuid, Name = IxName(playerGuid) ?? e.Name, Level = e.Level, Race = o.Byte(UF.UNIT_FIELD_BYTES_0, 0), Class = (WowClass)o.Byte(UF.UNIT_FIELD_BYTES_0, 1) };
            for (int i = 0; i < IxEquipSlots; i++)
            {
                int entry = o.I32(UF.PLAYER_VISIBLE_ITEM_1_ENTRYID + i * 2);   // PLAYER_VISIBLE_ITEM_n_ENTRYID / _ENCHANTMENT pairs (UpdateFields.cs:260-)
                if (entry > 0) v.Equipment[i] = ItemInfoView(entry, 1);
            }
            v.Version = 1;
            _ixInspect = v;
            IxSend(Op.CMSG_INSPECT, new PacketWriter().U64(playerGuid), "CMSG_INSPECT " + (v.Name ?? IxGuidText(playerGuid)));
        }

        public void CloseInspect()
        {
            if (_ixInspect == null) return;
            WowLog.Info("Online inspect: closed (" + (_ixInspect.Name ?? IxGuidText(_ixInspect.Guid)) + ")");
            _ixInspect = null;
        }

        /// <summary>
        /// SMSG_INSPECT_TALENT (MiscHandler.cpp:998-1016): packed guid; BuildPlayerTalentsInfoData (Player.cpp:14749-14781): uint32 unspent
        /// points, uint8 spec count, uint8 active spec, per spec { uint8 n, n x (uint32 Talent id, uint8 rank 0..4), uint8 6, 6 x uint16
        /// GlyphProperties id } (realm TalentsInspecting off: uint32 0, uint8 0, uint8 0); BuildEnchantmentsInfoData (:14866-14905): uint32
        /// slotUsedMask, per used slot in order { uint32 entry, uint16 enchant mask (12 bits), per set bit uint16 SpellItemEnchantment id,
        /// int16 random property, packed guid creator, uint32 suffix }.
        /// </summary>
        void IxInspectOnTalent(WowPacket p)
        {
            ulong guid = p.PackedGuid();
            InspectView v = _ixInspect;
            if (v == null || v.Guid != guid) { WowLog.Info("Online inspect: SMSG_INSPECT_TALENT for " + IxGuidText(guid) + " - not the open inspection, ignored"); return; }
            v.UnspentPoints = (int)p.U32();
            int groups = p.U8();
            int active = p.U8();
            v.TalentGroups = groups;
            v.ActiveGroup = active;
            v.TalentRanks.Clear();
            v.Glyphs.Clear();
            var glyphIds = new List<int>();
            for (int g = 0; g < groups; g++)
            {
                int n = p.U8();
                for (int i = 0; i < n; i++)
                {
                    int talent = (int)p.U32();
                    int rank = p.U8();
                    if (g == active) v.TalentRanks[talent] = rank + 1;
                }
                int glyphs = p.U8();
                for (int i = 0; i < glyphs; i++)
                {
                    int glyph = p.U16();
                    if (g == active && glyph != 0) glyphIds.Add(glyph);
                }
            }
            uint mask = p.U32();
            for (int i = 0; i < IxEquipSlots; i++) { v.Equipment[i] = null; v.Enchants[i] = null; }
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1u << i)) == 0) continue;
                int entry = (int)p.U32();
                int enchMask = p.U16();
                var lines = new List<string>();
                for (int j = 0; j < IxEnchantSlots; j++)
                {
                    if ((enchMask & (1 << j)) == 0) continue;
                    int ench = p.U16();
                    // PERM_ENCHANTMENT_SLOT 0 = "Enchanted: <name>"; TEMP 1, the sockets 2..4 and PRISMATIC 6 by name (ours: under the item);
                    // BONUS 5 and the random-property slots 7..11 are the item's own suffix stats - not listed.
                    if (j == 5 || j >= 7) continue;
                    string name = IxEnchantName(ench);
                    if (name == null) continue;
                    lines.Add(j == 0 ? "Enchanted: " + name : name);
                }
                p.U16();                // random property (int16)
                p.PackedGuid();         // creator
                p.U32();                // suffix factor
                if (i >= IxEquipSlots) continue;
                if (entry > 0) v.Equipment[i] = ItemInfoView(entry, 1);
                v.Enchants[i] = lines.Count > 0 ? string.Join("\n", lines) : null;
            }
            IxInspectTrees(v);
            for (int i = 0; i < glyphIds.Count; i++)
            {
                int spell = IxDbcInt("GlyphProperties", glyphIds[i], IxGlyphSpell);
                if (spell > 0) v.Glyphs.Add(SpellName(spell));
            }
            v.Loaded = true;
            v.Version++;
            WowLog.Info("Online inspect: SMSG_INSPECT_TALENT for " + (v.Name ?? IxGuidText(guid)) + " - " + groups + " talent group(s), " + v.TalentRanks.Count + " talents, " + v.Glyphs.Count + " glyphs, " + IxTradeCount(v.Equipment) + " items");
        }

        /// <summary>The 3 talent trees of the inspected class (TalentTab rows with the class bit and no pet mask, OrderIndex order) and the
        /// points spent in each (the sum of the learned ranks of the Talent rows of that tab).</summary>
        void IxInspectTrees(InspectView v)
        {
            v.Trees.Clear();
            DbcTable tabs = IxDbc("TalentTab"), talents = IxDbc("Talent");
            if (tabs == null || v.Class == 0) return;
            int classBit = 1 << ((int)v.Class - 1);
            for (int r = 0; r < tabs.RecordCount; r++)
            {
                if ((tabs.GetInt(r, IxTabClassMask) & classBit) == 0 || tabs.GetInt(r, IxTabPetMask) != 0) continue;
                string name = "";
                try { name = tabs.GetLocalizedString(r, IxTabName) ?? ""; } catch (Exception) { }
                v.Trees.Add(new InspectTree { TabId = tabs.GetId(r), Order = tabs.GetInt(r, IxTabOrder), Name = name });
            }
            v.Trees.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (talents == null) return;
            foreach (KeyValuePair<int, int> kv in v.TalentRanks)
            {
                int row = talents.FindRow(kv.Key);
                if (row < 0) continue;
                int tab = talents.GetInt(row, IxTalentTab);
                for (int t = 0; t < v.Trees.Count; t++) if (v.Trees[t].TabId == tab) { v.Trees[t].Points += kv.Value; break; }
            }
        }

        int IxDbcInt(string table, int id, int field)
        {
            DbcTable t = IxDbc(table);
            if (t == null || id <= 0) return 0;
            int row = t.FindRow(id);
            return row >= 0 ? t.GetInt(row, field) : 0;
        }

        /// <summary>InspectFrame_OnUpdate (Blizzard_InspectUI.lua:91-95): the inspected player out of sight closes the frame. Names / items
        /// fill in as the answers arrive (the views are filled in place by Items.cs).</summary>
        void IxInspectTick(float dt)
        {
            InspectView v = _ixInspect;
            if (v == null) return;
            if (Find(v.Guid) == null)
            {
                WowLog.Info("Online inspect: " + (v.Name ?? IxGuidText(v.Guid)) + " is no longer in sight - InspectFrame closes");
                _ixInspect = null;
                return;
            }
            if (v.Name == null) { v.Name = ChatSender(v.Guid); if (v.Name != null) v.Version++; }
        }
    }
}
