// iteration 11 - owner spellfx: WoW's "spell on the cursor" rules (proposal-spellfx.md s1.1 / s1.4 / s4 / s6.1). Port of SpellTargetingProto
// (P/harness/Probes/D11/spellfx/SpellFxProtos.cs, 68 / 68 checks on the real files). Pure C#.
using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    public static class SpellTargeting
    {
        public const uint TargetFlagItem = 0x10, TargetFlagDestLocation = 0x40, TargetFlagGameObjectItem = 0x4000;   // SpellInfo.h:46-67
        /// <summary>Implicit targets whose reference type is DEST (AC SpellInfo.cpp _data, TARGET_REFERENCE_TYPE_DEST).</summary>
        public static readonly HashSet<int> DestReference = new HashSet<int> { 8, 16, 28, 29, 31, 34, 52, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 91, 107 };
        /// <summary>Ours: the drawn radius of a place spell without an area (Eagle Eye, Far Sight).</summary>
        public const float MarkerRadius = 2f;
        /// <summary>TARGET_GAMEOBJECT_ITEM_TARGET (Pick Lock: a selected chest, else an item).</summary>
        public const int TargetGameObjectItem = 26;

        /// <summary>
        /// What the spell waits for before it is cast. Ground: Spell.dbc Targets has DEST_LOCATION 0x40 (only the DBC column decides - AC's derived
        /// explicit mask also marks Tranquility, and AC fills a missing place itself, Spell.cpp:810-823). Item: Targets has ITEM 0x10 and the spell
        /// is not SPELL_ATTR0_HELD_ITEM_ONLY 0x200 (SharedDefines.h:379 - the client picks the main hand itself), or an effect aims at implicit
        /// target 26 TARGET_GAMEOBJECT_ITEM_TARGET (Pick Lock) and no game object is selected (gameObjectSelected).
        /// </summary>
        public static SpellTargetingKind KindOf(SpellInfo s, bool gameObjectSelected)
        {
            if (s == null) return SpellTargetingKind.None;
            if ((s.Targets & TargetFlagDestLocation) != 0) return SpellTargetingKind.Ground;
            if ((s.Targets & TargetFlagItem) != 0) return s.HeldItemOnly ? SpellTargetingKind.None : SpellTargetingKind.Item;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e != null && (e.TargetA == TargetGameObjectItem || e.TargetB == TargetGameObjectItem))
                    return gameObjectSelected ? SpellTargetingKind.None : SpellTargetingKind.Item;
            }
            return SpellTargetingKind.None;
        }

        /// <summary>The circle's radius: the largest SpellRadius of the effects aimed at the place (TargetA / TargetB a DEST reference, radius
        /// above 0); 0 = none (measured: Blizzard 8, Flamestrike 5, Death and Decay 10, Mass Dispel 15, Freezing Arrow 2).</summary>
        public static float GroundRadius(SpellInfo s)
        {
            float r = 0f;
            if (s == null) return r;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || e.Radius <= 0f) continue;
                if (DestReference.Contains(e.TargetA) || DestReference.Contains(e.TargetB)) r = Math.Max(r, e.Radius);
            }
            return r;
        }

        /// <summary>
        /// The view of a waiting spell. Range of a place = AC WorldObject::GetSpellMaxRangeForTarget / Min with no unit target = the FRIENDLY
        /// pair (Object.cpp:3967-3989: GetMaxRange(true)); rangeModPct / rangeModFlat = SPELLMOD_RANGE 5 and radiusModPct / radiusModFlat =
        /// SPELLMOD_RADIUS 6 of our talents when known (AC Player::ApplySpellMod: base * (1 + pct / 100) + flat) - then RangeExact.
        /// </summary>
        public static SpellTargetingView NewView(SpellInfo s, SpellTargetingKind kind, bool held, bool modsKnown, int rangeModPct, float rangeModFlat, int radiusModPct, float radiusModFlat)
        {
            var v = new SpellTargetingView { SpellId = s.Id, Name = s.Name, Kind = kind, Held = held };
            if (kind == SpellTargetingKind.Ground)
            {
                float r = GroundRadius(s);
                v.Radius = r > 0f ? r * (1f + radiusModPct / 100f) + radiusModFlat : MarkerRadius;
                v.RangeMin = s.RangeMinFriend;
                v.RangeMax = s.RangeMaxFriend > 0f ? s.RangeMaxFriend * (1f + rangeModPct / 100f) + rangeModFlat : 0f;
                v.RangeExact = modsKnown;
            }
            else if (kind == SpellTargetingKind.Item)
            {
                v.ItemClass = s.EquippedItemClass;
                v.ItemSubClassMask = s.EquippedItemSubClassMask;
                v.ItemInventoryTypeMask = s.EquippedItemInventoryTypeMask;
            }
            return v;
        }

        /// <summary>
        /// AC Spell::CheckRange for a place at cast start (strict: no +10 % slack - Spell.cpp:7361-7363 is for !strict only): beyond the max range
        /// (3D) -> "Out of range" (SPELL_FAILED_OUT_OF_RANGE, GlobalStrings.lua:7147); within the min range -> "Target too close"
        /// (SPELL_FAILED_TOO_CLOSE, :7193) (Spell.cpp:7417-7422). RangeMax 0 = no limit. null = fine.
        /// </summary>
        public static string CheckPlace(SpellTargetingView v, WowPos me, WowPos dest)
        {
            if (v == null) return null;
            float dx = dest.X - me.X, dy = dest.Y - me.Y, dz = dest.Z - me.Z;
            float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (v.RangeMax > 0f && d > v.RangeMax) return "Out of range";
            if (v.RangeMin > 0f && d < v.RangeMin) return "Target too close";
            return null;
        }

        /// <summary>The enchant a spell applies: MiscValue of ENCHANT_ITEM 53 / ENCHANT_ITEM_TEMPORARY 54 (a SpellItemEnchantment id), 0 = none.</summary>
        public static int EnchantOf(SpellInfo s, out bool temporary)
        {
            temporary = false;
            if (s == null) return 0;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null) continue;
                if (e.Effect == SpellEffect.EnchantItem) return e.MiscValue;
                if (e.Effect == SpellEffect.EnchantItemTemporary) { temporary = true; return e.MiscValue; }
            }
            return 0;
        }
    }
}
