// iteration 11 - owner spellfx: the death knight's six runes as the 3.3.5 client keeps them (proposal-spellfx.md s3.2 / s3.3 / s6.1). Port of
// RuneTrackerProto (P/harness/Probes/D11/spellfx/SpellFxProtos.cs, checked by the SfxProtos probe). Pure C#.
using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    /// <summary>
    /// Server order 0-1 blood, 2-3 unholy, 4-5 frost (Player.cpp:13780-13788 runeSlotTypes; RuneType 0 blood, 1 unholy, 2 frost, 3 death -
    /// Player.h:394-400). Regeneration is client-side ("Runes act as cooldowns, and they don't need to send any data", Player.cpp:1813): every
    /// rune counts down on its own; a full cooldown = 1 / PLAYER_RUNE_REGEN[its base type] seconds (10 s at login, Player.cpp:13810-13811).
    /// Display only - the server decides every cast.
    /// </summary>
    public sealed class RuneTracker
    {
        public const int Count = 6;
        public const float BaseCooldown = 10f;                 // RUNE_BASE_COOLDOWN 10000 ms (Player.h:389)
        static readonly int[] BaseTypes = { 0, 0, 1, 1, 2, 2 };
        readonly int[] _type = (int[])BaseTypes.Clone();
        readonly float[] _left = new float[Count];             // seconds until ready
        readonly float[] _full = { BaseCooldown, BaseCooldown, BaseCooldown, BaseCooldown, BaseCooldown, BaseCooldown };

        public int TypeOf(int i) { return _type[i]; }
        public bool Ready(int i) { return _left[i] <= 0f; }
        public float Left(int i) { return _left[i]; }

        /// <summary>
        /// SMSG_SPELL_GO CAST_FLAG_RUNE_LIST 0x200000 (Spell.cpp:5033-5052): before / after = rune-ready masks (bit i = rune i ready);
        /// passed[] = one byte per rune set before and clear after, in rune order: (baseCd - cd) / baseCd * 255 ("rune cooldown passed").
        /// Such a rune starts its cooldown at full * (1 - passed / 255); a rune clear before and set after became ready (Blood Tap, Empower Rune Weapon).
        /// </summary>
        public void OnSpellGo(int before, int after, byte[] passed)
        {
            int k = 0;
            for (int i = 0; i < Count; i++)
            {
                int m = 1 << i;
                if ((before & m) != 0 && (after & m) == 0)
                {
                    byte p = passed != null && k < passed.Length ? passed[k] : (byte)0;
                    k++;
                    _left[i] = _full[i] * (1f - p / 255f);
                }
                else if ((before & m) == 0 && (after & m) != 0) _left[i] = 0f;
            }
        }

        /// <summary>SMSG_CONVERT_RUNE (Player.cpp:13751-13759): uint8 index, uint8 new type.</summary>
        public void Convert(int index, int type) { if (index >= 0 && index < Count && type >= 0 && type <= 3) _type[index] = type; }

        /// <summary>SMSG_RESYNC_RUNES (Player.cpp:13761-13771): count x (uint8 type, uint8 255 - cooldown * 51) - read as passed / 255 of the full
        /// cooldown like RUNE_LIST (ours). Not sent by AC master (SpellEffects.cpp:6141 commented out); handled for completeness.</summary>
        public void Resync(int[] types, byte[] passed)
        {
            if (types == null || passed == null) return;
            for (int i = 0; i < Count && i < types.Length && i < passed.Length; i++)
            {
                if (types[i] >= 0 && types[i] <= 3) _type[i] = types[i];
                _left[i] = passed[i] >= 255 ? 0f : _full[i] * (1f - passed[i] / 255f);
            }
        }

        /// <summary>SMSG_ADD_RUNE_POWER (Player.cpp:13773-13778): uint32 mask - those runes ready. Never called in AC master.</summary>
        public void AddPower(uint mask) { for (int i = 0; i < Count; i++) if ((mask & (1u << i)) != 0) _left[i] = 0f; }

        /// <summary>Per frame. regen[4] = PLAYER_RUNE_REGEN_1..4 (UpdateFields 0x0519, runes per second by RuneType; 0 = not known: 10 s).</summary>
        public void Tick(float dt, float[] regen)
        {
            for (int i = 0; i < Count; i++)
            {
                float r = regen != null && BaseTypes[i] < regen.Length ? regen[BaseTypes[i]] : 0f;
                float full = r > 0.0001f ? 1f / r : BaseCooldown;
                if (Math.Abs(full - _full[i]) > 0.0001f && _left[i] > 0f) _left[i] *= full / _full[i];   // haste change mid-cooldown: keep the fraction
                _full[i] = full;
                if (_left[i] > 0f) _left[i] = Math.Max(0f, _left[i] - dt);
            }
        }

        /// <summary>
        /// AC Spell::CheckRuneCost (Spell.cpp:5584-5631) for a spell with PowerType POWER_RUNE 5: ready runes of each type pay their own type
        /// first; what is left must be covered by ready death runes. Costs in RuneType order (SpellDb.RuneCost: blood, unholy, frost).
        /// </summary>
        public bool CanPay(int blood, int unholy, int frost)
        {
            int[] need = { blood, unholy, frost };
            int death = 0;
            for (int i = 0; i < Count; i++)
            {
                if (_left[i] > 0f) continue;
                int t = _type[i];
                if (t == 3) death++;
                else if (need[t] > 0) need[t]--;
            }
            int rest = Math.Max(0, need[0]) + Math.Max(0, need[1]) + Math.Max(0, need[2]);
            return rest <= death;
        }

        /// <summary>Copies the state into the contract's views (PlayerState.Runes; stable instances).</summary>
        public void Fill(RuneView[] into)
        {
            if (into == null) return;
            for (int i = 0; i < Count && i < into.Length; i++)
            {
                if (into[i] == null) into[i] = new RuneView();
                into[i].Type = _type[i]; into[i].BaseType = BaseTypes[i]; into[i].Ready = _left[i] <= 0f; into[i].Cooldown = _full[i]; into[i].Remaining = _left[i];
            }
        }

        /// <summary>All six ready, base types (a new character / a re-login).</summary>
        public void Reset()
        {
            for (int i = 0; i < Count; i++) { _type[i] = BaseTypes[i]; _left[i] = 0f; _full[i] = BaseCooldown; }
        }
    }
}
