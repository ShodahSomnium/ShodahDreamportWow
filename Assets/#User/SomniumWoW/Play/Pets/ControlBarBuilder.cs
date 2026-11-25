// iteration 11 - owner pets (proposal-pets.md s3.1 / s3.8 / s6.0): SMSG_PET_SPELLS -> ControlBarView, shared by the online and the offline
// session (pure C#, no UnityEngine; compiled by the harness like Play/Spells). Every layout below is AzerothCore master's
// (P/acsrc11/pets/Player.cpp:9749-9983, CharmInfo.cpp / CharmInfo.h).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Pets
{
    /// <summary>One SMSG_PET_SPELLS body as the server wrote it (the four writers share ONE layout, Player.cpp:9749-9983).</summary>
    public sealed class ControlBarPacket
    {
        public ulong Guid;                 // 0 = SendRemoveControlBar (8-byte packet, Player.cpp:9978-9983)
        public int Family;                 // uint16 creature_template.family (pets only)
        public uint DurationMs;            // uint32, 0 = permanent
        public int React, Command;         // uint8 each (a charmed player: both 0 inside the uint32 0)
        public ushort Flags;               // uint16; 0x0800 only from VehicleSpellInitialize (Player.cpp:9865)
        public readonly uint[] Raw = new uint[10];   // CharmInfo::BuildActionBar (CharmInfo.cpp:263-267): action | type << 24
        public readonly List<uint> Spells = new List<uint>();            // the permanent pet's spellbook (packed like the bar)
        public readonly List<uint[]> Cooldowns = new List<uint[]>();     // {spellId, category, cooldownMs, categoryCooldownMs}
        public int Left;                   // bytes not consumed (0 = every byte read)
    }

    public static class ControlBarBuilder
    {
        // ActiveStates (CharmInfo.h:62-70) and the button packing (CharmInfo.h:31-33).
        public const uint ActPassive = 0x01, ActDisabled = 0x81, ActEnabled = 0xC1, ActCommand = 0x07, ActReaction = 0x06;
        public const ushort VehicleFlags = 0x0800;           // "DisableActions, set for all vehicles" (Player.cpp:9865)
        public const uint TargetFlagDestLocation = 0x40;     // TARGET_FLAG_DEST_LOCATION (SpellInfo.h)
        public const int TargetDestTraj = 89;                // TARGET_DEST_TRAJ (SharedDefines.h)

        public static uint ActionOf(uint packed) { return packed & 0x00FFFFFFu; }
        public static uint TypeOf(uint packed) { return packed >> 24; }

        /// <summary>Reads one SMSG_PET_SPELLS body (s3.1). A short packet throws PacketTooShortException (the WorldClient counts it).</summary>
        public static ControlBarPacket Read(WowPacket p)
        {
            var b = new ControlBarPacket();
            b.Guid = p.U64();
            if (b.Guid == 0) { b.Left = p.Remaining; return b; }
            b.Family = p.U16();
            b.DurationMs = p.U32();
            b.React = p.U8();
            b.Command = p.U8();
            b.Flags = p.U16();
            for (int i = 0; i < 10; i++) b.Raw[i] = p.U32();
            int n = p.U8();
            for (int i = 0; i < n; i++) b.Spells.Add(p.U32());
            int c = p.U8();
            for (int i = 0; i < c; i++) b.Cooldowns.Add(new[] { p.U32(), (uint)p.U16(), p.U32(), p.U32() });
            b.Left = p.Remaining;
            return b;
        }

        /// <summary>The s3.8 rule (deterministic, no timing guess): 0 -> None; flags 0x0800 -> Vehicle; the unit of our last
        /// SMSG_CLIENT_CONTROL_UPDATE(allow 1) that is not us -> Possess; our UNIT_FIELD_SUMMON (or a 0xF140 pet) -> Pet; else Charm.</summary>
        public static ControlBarKind Classify(ControlBarPacket b, ulong controlledNotUs, ulong summon)
        {
            if (b == null || b.Guid == 0) return ControlBarKind.None;
            if (b.Flags == VehicleFlags) return ControlBarKind.Vehicle;
            if (controlledNotUs != 0 && b.Guid == controlledNotUs) return ControlBarKind.Possess;
            if ((summon != 0 && b.Guid == summon) || (b.Guid >> 48) == 0xF140) return ControlBarKind.Pet;
            return ControlBarKind.Charm;
        }

        /// <summary>The bar view of a packet (Command / React from the bytes for Pet / Charm; Possess / Vehicle keep the defaults).</summary>
        public static ControlBarView Build(ControlBarPacket b, ControlBarKind kind, SpellDb db, int level)
        {
            var v = new ControlBarView();
            v.Kind = kind;
            v.UnitGuid = b.Guid;
            v.Family = kind == ControlBarKind.Pet ? b.Family : 0;
            v.DurationTotal = v.DurationLeft = b.DurationMs / 1000f;
            if (kind == ControlBarKind.Pet || kind == ControlBarKind.Charm)
            {
                if (b.Command <= 3) v.Command = (PetCommand)b.Command;
                if (b.React <= 2) v.React = (PetReact)b.React;
            }
            for (int i = 0; i < 10; i++) v.Slots[i] = Slot(b.Raw[i], kind, db, level);
            for (int i = 0; i < b.Spells.Count; i++)
            {
                uint id = ActionOf(b.Spells[i]);
                if (id == 0) continue;
                var s = SpellSlot(db, (int)id, TypeOf(b.Spells[i]), level);
                v.Spells.Add(s);
            }
            MarkActive(v);
            return v;
        }

        /// <summary>Decodes one bar button (s3.8): Vehicle = the high byte is the slot (8..15), a spell when the action is set; else
        /// 0x07 Command, 0x06 Reaction, 0x01 / 0x81 / 0xC1 with an action = Spell (0x81 / 0xC1 autocastable, 0xC1 autocast on).</summary>
        public static ControlSlotView Slot(uint raw, ControlBarKind kind, SpellDb db, int level)
        {
            uint type = TypeOf(raw), action = ActionOf(raw);
            if (kind == ControlBarKind.Vehicle) return action != 0 ? SpellSlot(db, (int)action, ActPassive, level) : Empty();
            if (type == ActCommand && action <= 3) return CommandSlot((PetCommand)action);
            if (type == ActReaction && action <= 2) return ReactSlot((PetReact)action);
            return action != 0 ? SpellSlot(db, (int)action, type, level) : Empty();
        }

        public static ControlSlotView Empty() { return new ControlSlotView { Kind = ControlSlotKind.Empty, Usable = false }; }

        /// <summary>The server type byte to send back for a spell slot (CMSG_PET_ACTION / CMSG_PET_SET_ACTION; AC accepts all three,
        /// PetHandler.cpp:325-327).</summary>
        public static uint RawType(ControlSlotView v)
        {
            if (v == null) return ActEnabled;
            return v.Autocast ? ActEnabled : v.Autocastable ? ActDisabled : ActPassive;
        }

        // PET_ACTION_* / PET_MODE_* (GlobalStrings.lua:5615-5640) and the textures of PetActionBarFrame.lua:6-12.
        static readonly string[] CommandNames = { "Stay", "Follow", "Attack", "Dismiss" };
        static readonly string[] CommandIcons = { @"Interface\Icons\Spell_Nature_TimeStop", @"Interface\Icons\Ability_Tracking", @"Interface\Icons\Ability_GhoulFrenzy", @"Interface\Icons\Spell_Shadow_Teleport" };
        static readonly string[] ReactNames = { "Passive", "Defensive", "Aggressive" };
        static readonly string[] ReactIcons = { @"Interface\Icons\Ability_Seal", @"Interface\Icons\Ability_Defend", @"Interface\Icons\Ability_Racial_BloodRage" };

        public static string CommandName(PetCommand c) { return CommandNames[(int)c & 3]; }
        public static string ReactName(PetReact r) { return ReactNames[Math.Min(2, (int)r)]; }

        public static ControlSlotView CommandSlot(PetCommand c)
        {
            return new ControlSlotView { Kind = ControlSlotKind.Command, Action = (int)c, Name = CommandNames[(int)c & 3], IconPath = CommandIcons[(int)c & 3], Tooltip = CommandNames[(int)c & 3], Harmful = c == PetCommand.Attack, NeedsUnit = c == PetCommand.Attack };
        }

        public static ControlSlotView ReactSlot(PetReact r)
        {
            int i = Math.Min(2, (int)r);
            return new ControlSlotView { Kind = ControlSlotKind.Reaction, Action = i, Name = ReactNames[i], IconPath = ReactIcons[i], Tooltip = ReactNames[i] };
        }

        /// <summary>A spell button: names, icon, tooltip and the targeting flags from Spell.dbc. Passive = SPELL_ATTR0_PASSIVE only (the type
        /// byte 0x01 is NOT "passive": the possess bar sends every creature spell as ACT_PASSIVE, CharmInfo.cpp:77-95).</summary>
        public static ControlSlotView SpellSlot(SpellDb db, int spellId, uint type, int level)
        {
            var v = new ControlSlotView { Kind = ControlSlotKind.Spell, Action = spellId };
            v.Autocastable = type == ActDisabled || type == ActEnabled;
            v.Autocast = type == ActEnabled;
            SpellInfo s = db != null ? db.Find(spellId) : null;
            if (s == null) { v.Name = "Spell " + spellId; v.Tooltip = v.Name; return v; }
            v.Name = s.Name;
            v.IconPath = s.IconPath;
            v.Passive = s.IsPassive;
            v.Harmful = s.IsHarmful;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                if (e.TargetsEnemy) v.Harmful = true;
                if (e.TargetA == TargetDestTraj || e.TargetB == TargetDestTraj) v.Aimed = true;
            }
            v.NeedsUnit = s.NeedsUnitTarget;
            v.NeedsPlace = (s.Targets & TargetFlagDestLocation) != 0 && !v.Aimed;
            v.RangeMax = s.RangeMax;
            v.Tooltip = Tip(db, s, level);
            return v;
        }

        /// <summary>Name, rank and the Spell.dbc description with its $-tokens resolved at that level (SpellText).</summary>
        public static string Tip(SpellDb db, SpellInfo s, int level)
        {
            if (s == null) return null;
            string text = s.Description;
            try { if (!string.IsNullOrEmpty(text) && db != null) text = SpellText.Resolve(db, s, text, new SpellTextContext { CasterLevel = Math.Max(1, level) }); }
            catch (Exception) { text = s.Description; }
            return s.Name + (string.IsNullOrEmpty(s.Rank) ? "" : " (" + s.Rank + ")") + (string.IsNullOrEmpty(text) ? "" : "\n" + text);
        }

        /// <summary>The current command / reaction slot is lit (PetActionBarFrame.lua:126-134).</summary>
        public static void MarkActive(ControlBarView v)
        {
            if (v == null) return;
            for (int i = 0; i < 10; i++)
            {
                var s = v.Slots[i];
                if (s == null) continue;
                if (s.Kind == ControlSlotKind.Reaction) s.Active = s.Action == (int)v.React;
                else if (s.Kind == ControlSlotKind.Command && s.Action != (int)PetCommand.Attack) s.Active = s.Action == (int)v.Command;
            }
        }

        /// <summary>The slot (0..9) holding that spell, -1 none.</summary>
        public static int SlotOf(ControlBarView v, int spellId)
        {
            if (v == null || spellId == 0) return -1;
            for (int i = 0; i < 10; i++) if (v.Slots[i] != null && v.Slots[i].Kind == ControlSlotKind.Spell && v.Slots[i].Action == spellId) return i;
            return -1;
        }

        /// <summary>The ControlBar.Spells entry of that spell, null none.</summary>
        public static ControlSlotView BookEntry(ControlBarView v, int spellId)
        {
            if (v == null) return null;
            for (int i = 0; i < v.Spells.Count; i++) if (v.Spells[i].Action == spellId) return v.Spells[i];
            return null;
        }
    }

    /// <summary>CreatureFamily.dbc (fmt "nfifiiiiixssssssssssssssssxx", AC DBCStructure.h:780-794: name field 10 localized, icon field 27) and
    /// StableSlotPrices.dbc ("ni": id = slot number, field 1 = copper) - read from the player's own files.</summary>
    public static class PetFamilyDb
    {
        static WowFiles _files;
        static DbcTable _family, _prices;

        static void Load(WowFiles files)
        {
            if (files == null || ReferenceEquals(files, _files)) return;
            _files = files;
            _family = Dbc.Load(files, "CreatureFamily");
            _prices = Dbc.Load(files, "StableSlotPrices");
        }

        public static string Name(WowFiles files, int family)
        {
            Load(files);
            int r = _family != null && family > 0 ? _family.FindRow(family) : -1;
            if (r < 0) return null;
            string n = _family.GetLocalizedString(r, 10);
            return string.IsNullOrEmpty(n) ? null : n;
        }

        public static string Icon(WowFiles files, int family)
        {
            Load(files);
            int r = _family != null && family > 0 ? _family.FindRow(family) : -1;
            if (r < 0) return null;
            string n = _family.GetString(r, 27);
            return string.IsNullOrEmpty(n) ? null : n;
        }

        /// <summary>The copper price of stable slot `slot` (1..4: 500 / 50000 / 500000 / 1500000 measured), 0 = none.</summary>
        public static int SlotPrice(WowFiles files, int slot)
        {
            Load(files);
            int r = _prices != null && slot > 0 ? _prices.FindRow(slot) : -1;
            return r >= 0 ? _prices.GetInt(r, 1) : 0;
        }
    }
}
