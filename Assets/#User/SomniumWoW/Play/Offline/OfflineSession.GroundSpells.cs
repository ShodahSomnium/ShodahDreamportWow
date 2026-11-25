// iteration 11 - owner spellfx: ground spells offline (proposal-spellfx.md s7) - our simulation with the same contract as online: WoW's
// "spell on the cursor" (Player.Targeting), the place in the spell (area centre, channel ticks at the place), persistent areas (SPELL_EFFECT_
// PERSISTENT_AREA_AURA 27: Consecration, Flamestrike's burn, Blizzard / Rain of Fire / Hurricane / Volley visuals), the every-target hit list,
// aura-state masks and the imbue enchant of the main hand. Numbers where the server's scripts are unknown are OURS (named + logged once).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        // ---- the aim
        private const float GsAimTimeout = 30f;            // ours (as online / vrcast's SlottingTimeout)
        private const float GsDefaultAreaRadius = 8f;      // ours: an area effect without a radius
        private int _gsConfirmedFor;                       // the spell whose place was just confirmed (while CastSpell / CastSpellHeld runs)
        private int _gsDestSpell;                          // the spell that was aimed at _gsDestPos (kept until its FinishSpell ends or another confirm)
        private WowPos _gsDestPos;
        private int _gsTickDestSpell;                      // a channel tick spell running at _gsTickDest (TargetA 76 DEST_CHANNEL_TARGET)
        private WowPos _gsTickDest;
        private bool _channelHasDest;                      // the running channel was aimed at a place (Effects3 StartChannel)
        private WowPos _channelDest;
        private double _gsAimSince;
        private int _gsAimSerial, _gsAimLogs, _gsAreaLogs, _gsTickLogs;
        private bool _gsRulesLogged;

        // ---- persistent areas (AC Spell::EffectPersistentAA, SpellEffects.cpp:1937-1955: a dynamic object at the place with the effect's radius)
        private sealed class GsArea
        {
            public AreaEffectView View;
            public SpellInfo Spell;
            public readonly List<SpellEffectInfo> Effects = new List<SpellEffectInfo>();
            public readonly List<float> TickIn = new List<float>();
            public float Left;                             // seconds
            public bool ChannelBound;                      // ends with the channel (AC Spell::cancel -> RemoveDynObject, Spell.cpp:3870)
            public int Level;
        }
        private readonly List<GsArea> _gsAreas = new List<GsArea>();
        private readonly List<AreaEffectView> _gsAreaViews = new List<AreaEffectView>();
        private ulong _gsAreaSerial;
        private readonly HashSet<int> _gsVisualOnlyLogged = new HashSet<int>();

        /// <summary>Spell areas on the ground (our simulation's persistent areas). Stable instances.</summary>
        public IReadOnlyList<AreaEffectView> AreaEffects { get { return _gsAreaViews; } }

        // ------------------------------------------------------------------ the aim (s7.1)

        /// <summary>CastSpell's entry (Spells.cs): a spell whose Targets ask for a place / an item waits for it. True = the aim started (or a refusal
        /// was shown); the caller returns.</summary>
        private bool GsNeedsAim(SpellInfo s)
        {
            if (_gsConfirmedFor == s.Id) return false;
            var kind = SpellTargeting.KindOf(s, false);
            if (kind == SpellTargetingKind.None) return false;
            string un = Unsupported(s);                                          // refused BEFORE the circle (Distract, Feed Pet, Pick Lock offline)
            if (un != null) { CastFailed(s.Id, un); return true; }
            string why = CheckCast(s, _me, false);                               // power, form, weapon (Volley's ranged weapon) - not the timers: one may aim during the GCD
            if (why != null) { CastFailed(s.Id, why); return true; }
            if (CooldownLeft(s) > 0f) { CastFailed(s.Id, "Spell is not ready yet"); return true; }   // ERR_SPELL_COOLDOWN (GlobalStrings.lua:3493), as online
            GsBeginAim(s, kind, _holdRequest);
            return true;
        }

        private void GsBeginAim(SpellInfo s, SpellTargetingKind kind, bool held)
        {
            if (!_gsRulesLogged)
            {
                _gsRulesLogged = true;
                WowLog.Info("OfflineSession: ground spells wait for a place (Spell.dbc Targets 0x40), the range of a place is exact offline (no talents); ours: an aim ends after " +
                            GsAimTimeout + " s, an area without a radius is " + GsDefaultAreaRadius + " yd, persistent areas tick their periodic damage on creatures inside (other area auras are drawn only)");
            }
            var v = SpellTargeting.NewView(s, kind, held, true, 0, 0f, 0, 0f);   // offline has no range talents: exact
            v.Serial = ++_gsAimSerial;
            _player.Targeting = v;
            _gsAimSince = _time;
            if (_gsAimLogs++ < 10)
                WowLog.Info("OfflineSession: " + s.Name + " waits for " + (kind == SpellTargetingKind.Ground
                    ? "a place (radius " + v.Radius.ToString("0.#") + " yd, range " + v.RangeMin.ToString("0.#") + "-" + v.RangeMax.ToString("0.#") + " yd)" : "an item") + (held ? ", immersive" : "") + " - logged 10 times");
        }

        private void GsEndAim(string why)
        {
            var v = _player.Targeting;
            if (v == null) return;
            _player.Targeting = null;
            WowLog.Info("OfflineSession: " + v.Name + " no longer waits (" + why + ")");
        }

        public void ConfirmTargetingAt(WowPos dest)
        {
            var v = _player.Targeting;
            if (v == null || v.Kind != SpellTargetingKind.Ground) return;
            var s = _db.Find(v.SpellId);
            if (s == null) { GsEndAim("unknown spell"); return; }
            string why = SpellTargeting.CheckPlace(v, _me.Pos, dest);
            if (why != null) { EmitError(why); return; }                          // "Out of range" (GlobalStrings.lua:7147) / "Target too close" (:7193); still aiming
            if (_casting != null || _held != null) { EmitError("Another action is in progress"); return; }   // SPELL_FAILED_SPELL_IN_PROGRESS (GlobalStrings.lua:7160); keep the place
            if (_gcd > 0f && s.StartRecoveryTimeMs > 0) { EmitError("Spell is not ready yet"); return; }       // the GCD: keep the circle (as online, ours)
            _player.Targeting = null;
            _gsConfirmedFor = v.SpellId;
            _gsDestSpell = v.SpellId;
            _gsDestPos = dest;
            WowLog.Info("OfflineSession: " + v.Name + " placed at " + dest + " (" + Dist(_me.Pos, dest).ToString("0.0") + " yd)" + (v.Held ? " (immersive)" : ""));
            try { if (v.Held) CastSpellHeld(v.SpellId, 0); else CastSpell(v.SpellId, 0); }
            finally { _gsConfirmedFor = 0; }
        }

        public void ConfirmTargetingItem(int bagIndex, int equipSlot)
        {
            var v = _player.Targeting;
            if (v == null || v.Kind != SpellTargetingKind.Item) return;
            _player.Targeting = null;
            CastSpellOnItem(v.SpellId, bagIndex, equipSlot);
        }

        public void ConfirmTargetingTrade()
        {
            if (_player.Targeting == null) return;
            GsEndAim("no trades offline");
            EmitError("Not available offline");
        }

        public void CancelTargeting()
        {
            GsEndAim("cancelled");                                               // no CastFailed: nothing was cast
        }

        /// <summary>Offline items carry no spells and no professions exist offline, so only a temporary enchant (effect 54) on the main hand is
        /// simulated (the existing imbue code); anything else is refused (ours).</summary>
        public void CastSpellOnItem(int spellId, int bagIndex, int equipSlot)
        {
            _player.Targeting = null;
            var s = _db.Find(spellId);
            bool temporary;
            int ench = SpellTargeting.EnchantOf(s, out temporary);
            if (s == null || ench <= 0 || !temporary) { EmitError("Not available offline"); return; }
            if (bagIndex >= 0 || equipSlot != (int)EquipSlot.MainHand) { EmitError("Only your main hand can hold it offline."); return; }   // ours
            var e = s.EffectOf(SpellEffect.EnchantItemTemporary);
            if (e != null && ApplyImbue(s, e)) WowLog.Info("OfflineSession: " + s.Name + " on the main hand (enchant " + _imbueEnchant + ")");
        }

        // ------------------------------------------------------------------ the place in the spell (s7.2)

        /// <summary>The centre of an area effect: the confirmed place when the effect aims at it (TargetA or TargetB 16 TARGET_UNIT_DEST_AREA_ENEMY) and
        /// this spell was aimed (or a channel tick at its place); the caster otherwise (15 SRC_AREA, 24 CONE - unchanged).</summary>
        private WowPos GsAreaCentre(SpellInfo s, SpellEffectInfo e)
        {
            bool atDest = e.TargetA == SpellTarget.DestAreaEnemy || e.TargetB == SpellTarget.DestAreaEnemy;
            if (atDest && s != null)
            {
                if (_gsTickDestSpell == s.Id) return _gsTickDest;
                if (_gsDestSpell == s.Id) return _gsDestPos;
            }
            return _me.Pos;
        }

        /// <summary>True when this cast of s is aimed at a place (CastSucceeded.AtDest).</summary>
        private bool GsAimedAtPlace(SpellInfo s, out WowPos place)
        {
            place = default(WowPos);
            if (s == null) return false;
            if (_gsTickDestSpell == s.Id) { place = _gsTickDest; return true; }
            if (_gsDestSpell == s.Id) { place = _gsDestPos; return true; }
            return false;
        }

        /// <summary>FinishSpell's pre-pass: every attackable creature inside the spell's enemy areas (CastSucceeded.Hits). A creature the roll then
        /// misses still shows the impact (ours; its "Miss" text shows too). null = no area.</summary>
        private ulong[] GsPreviewHits(SpellInfo s)
        {
            List<ulong> hits = null;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || !e.IsAreaEnemy) continue;
                WowPos centre = GsAreaCentre(s, e);
                float radius = e.Radius > 0 ? e.Radius : GsDefaultAreaRadius;
                for (int k = 0; k < _creatures.Count; k++)
                {
                    var c = _creatures[k];
                    if (!IsAttackable(c) || Dist(centre, c.Pos) > radius + c.CombatReach) continue;
                    if (hits == null) hits = new List<ulong>();
                    if (!hits.Contains(c.Guid)) hits.Add(c.Guid);
                }
            }
            return hits != null ? hits.ToArray() : null;
        }

        /// <summary>The CastSucceeded of a player's cast with the hit list and the place (FinishSpell / the channel ticks).</summary>
        private void GsEmitSucceeded(SpellInfo s, ulong target, ulong[] hits, bool atDest, WowPos place)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.CastSucceeded, SourceGuid = PlayerGuid, TargetGuid = target, SpellId = s.Id, Text = s.Name,
                                            Hits = hits, AtDest = atDest, Pos = place });
            if (_events.Count > 512) _events.Dequeue();
        }

        /// <summary>SPELL_EFFECT_PERSISTENT_AREA_AURA 27 (ExecuteSpell): one area per cast even when two effects are 27 (Hurricane, Flare) - the
        /// effects are collected on it. Centre: the caster for TargetA 18 TARGET_DEST_CASTER (Consecration), else the confirmed place, else the caster
        /// (AC's own fallback for a dest spell cast without a place, Spell.cpp:810-823).</summary>
        private void GsCreateArea(SpellInfo s, SpellEffectInfo e)
        {
            GsArea a = null;
            for (int i = 0; i < _gsAreas.Count; i++) if (_gsAreas[i].Spell == s && _gsAreas[i].View.Age <= 0f) { a = _gsAreas[i]; break; }
            if (a == null)
            {
                WowPos centre = e.TargetA == SpellTarget.DestCaster ? _me.Pos : _gsTickDestSpell == s.Id ? _gsTickDest : _gsDestSpell == s.Id ? _gsDestPos : _me.Pos;
                int ms = s.DurationAt(_player.Level);
                a = new GsArea { Spell = s, Left = ms > 0 ? ms / 1000f : 0f, ChannelBound = s.IsChanneled, Level = _player.Level };
                a.View = new AreaEffectView { Guid = 0xF1F0000000000000UL | ++_gsAreaSerial, SpellId = s.Id, CasterGuid = PlayerGuid, Pos = centre, Radius = e.Radius > 0f ? e.Radius : GsDefaultAreaRadius, Type = 1 };
                _gsAreas.Add(a);
                _gsAreaViews.Add(a.View);
                if (_gsAreaLogs++ < 10)
                    WowLog.Info("OfflineSession: ground area " + s.Name + " at " + centre + " r " + a.View.Radius.ToString("0.#") + " yd for " + a.Left.ToString("0.#") + " s" + (a.ChannelBound ? " (ends with the channel)" : "") + " - logged 10 times");
            }
            else if (e.Radius > a.View.Radius) a.View.Radius = e.Radius;
            a.Effects.Add(e);
            a.TickIn.Add(e.Amplitude > 0 ? e.Amplitude / 1000f : 0f);
            if (!(e.Aura == SpellAura.PeriodicDamage && e.Amplitude > 0) && _gsVisualOnlyLogged.Add(e.Aura))
                WowLog.Info("OfflineSession: offline area aura " + e.Aura + " (" + s.Name + ") is visual only (ours; logged once per aura type)");
        }

        /// <summary>Per frame, first thing of StepPlayerExtras (before its dead return: areas outlive their caster as in AC, which removes a caster's
        /// dynamic objects only on leaving the map, Unit.cpp:12177): area ticks and lifetimes, the aim's end rules, the aura-state masks.</summary>
        private void GsTick(float dt)
        {
            var v = _player.Targeting;
            if (v != null)
            {
                string why = _player.Dead || _player.Ghost ? "dead" : _player.OnTaxi ? "on a flight" : _time - _gsAimSince > GsAimTimeout ? GsAimTimeout + " s without a choice" : null;
                if (why != null) GsEndAim(why);
            }
            for (int i = _gsAreas.Count - 1; i >= 0; i--)
            {
                var a = _gsAreas[i];
                a.View.Age += dt;
                a.Left -= dt;
                for (int k = 0; k < a.Effects.Count; k++)
                {
                    var e = a.Effects[k];
                    if (e.Aura != SpellAura.PeriodicDamage || e.Amplitude <= 0) continue;
                    a.TickIn[k] -= dt;
                    if (a.TickIn[k] > 0f) continue;
                    a.TickIn[k] += e.Amplitude / 1000f;
                    int dmg = Math.Max(1, CalcValue(a.Spell, e, a.Level));
                    for (int c = 0; c < _creatures.Count; c++)
                    {
                        var cr = _creatures[c];
                        if (!IsAttackable(cr) || Dist(a.View.Pos, cr.Pos) > a.View.Radius + cr.CombatReach) continue;
                        DamageCreature(cr, dmg, a.Spell.Id, false, GameEventType.PeriodicDamage, false);   // no hit roll per tick (the offline periodic rule)
                        if (!cr.Dead) Engage(cr);
                    }
                }
                if (a.Left <= 0f) GsRemoveArea(i);
            }
            _player.AuraStateMask = GsCasterAuraStates();
            for (int c = 0; c < _creatures.Count; c++)
            {
                var cr = _creatures[c];
                if (cr.State != null) cr.State.AuraStateMask = cr.Present && !cr.Dead ? GsTargetAuraStates(cr) : 0u;
            }
        }

        private void GsRemoveArea(int i)
        {
            var a = _gsAreas[i];
            _gsAreas.RemoveAt(i);
            _gsAreaViews.Remove(a.View);
        }

        /// <summary>EndChannel (any reason): the channel's own areas go with it (AC Spell::cancel -> RemoveDynObject, Spell.cpp:3870).</summary>
        private void GsEndChannelAreas(SpellInfo s)
        {
            for (int i = _gsAreas.Count - 1; i >= 0; i--) if (_gsAreas[i].ChannelBound && _gsAreas[i].Spell == s) GsRemoveArea(i);
            _channelHasDest = false;
        }

        /// <summary>A channel tick whose spell aims at the channel's place (TargetA 76 TARGET_DEST_CHANNEL_TARGET: the Blizzard / Rain of Fire / Volley /
        /// Hurricane ticks 42208 / 42223 / 42243 / 42231 - measured): runs at the place with AtDest + the hit list. True = handled.</summary>
        private bool GsTickAtPlace(SpellInfo trig)
        {
            if (!_channelHasDest || trig == null) return false;
            bool atChannelPlace = false;
            for (int i = 0; i < 3; i++) { var e = trig.Effects[i]; if (e != null && (e.TargetA == 76 || e.TargetB == 76)) atChannelPlace = true; }
            if (!atChannelPlace) return false;
            int was = _gsTickDestSpell; WowPos wasPos = _gsTickDest;
            _gsTickDestSpell = trig.Id; _gsTickDest = _channelDest;
            try
            {
                var hits = GsPreviewHits(trig);
                GsEmitSucceeded(trig, hits != null && hits.Length > 0 ? hits[0] : 0, hits, true, _channelDest);
                if (_gsTickLogs++ < 5) WowLog.Info("OfflineSession: " + trig.Name + " " + trig.Id + " ticks at " + _channelDest + " (" + (hits != null ? hits.Length : 0) + " hit) - logged 5 times");
                ExecuteSpell(trig, _me);
            }
            finally { _gsTickDestSpell = was; _gsTickDest = wasPos; }
            return true;
        }

        // ------------------------------------------------------------------ aura states (s7.3, display + the usable checks of the views)

        private uint GsCasterAuraStates()
        {
            uint m = 0;
            int[] states = { 2, 5, 10, 13, 23 };   // the states CasterAuraState knows (Spells.cs)
            foreach (int st in states) if (CasterAuraState(st)) m |= 1u << (st - 1);
            return m;
        }

        private static uint GsTargetAuraStates(OfflineCreature c)
        {
            uint m = 0;
            int[] states = { 2, 13, 18, 23 };      // the states TargetAuraState knows (Spells.cs)
            foreach (int st in states) if (TargetAuraState(st, c)) m |= 1u << (st - 1);
            return m;
        }

        // ------------------------------------------------------------------ the imbue's enchant (s7.3)

        /// <summary>The SpellItemEnchantment id an imbue puts on the main hand: its ENCHANT_ITEM_TEMPORARY MiscValue, or for Rockbiter (MiscValue 0 in
        /// all its ranks' own rows) AC's pick by the effect's damage (SpellEffects.cpp:3143-3184 EffectEnchantItemTmp: 2 -> 36744, 4 -> 36753,
        /// 5 -> 36751, 6 -> 36754, 7 -> 36755, 9 -> 36761, 10 -> 36758, 11 -> 36760) -> that spell's own MiscValue (measured: 36744 -> 29).</summary>
        private int GsImbueEnchant(SpellInfo s, SpellEffectInfo e)
        {
            if (e == null) return 0;
            if (e.MiscValue > 0) return e.MiscValue;
            if (s.SpellFamilyName != 11 || (s.SpellFamilyFlags[0] & 0x400000u) == 0) return 0;   // SPELLFAMILY_SHAMAN, Rockbiter (SpellEffects.cpp:3143)
            int pick;
            switch (CalcValue(s, e, _player.Level))
            {
                case 2: pick = 36744; break;
                case 4: pick = 36753; break;
                case 5: pick = 36751; break;
                case 6: pick = 36754; break;
                case 7: pick = 36755; break;
                case 9: pick = 36761; break;
                case 10: pick = 36758; break;
                case 11: pick = 36760; break;
                default: return 0;
            }
            var ps = _db.Find(pick);
            var pe = ps != null ? ps.EffectOf(SpellEffect.EnchantItemTemporary) : null;
            return pe != null ? pe.MiscValue : 0;
        }
    }
}
