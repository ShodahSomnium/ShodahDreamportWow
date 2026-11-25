using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Iteration 11 (combat feel, the user: "Auto put weapon away when casting a ranged spell" / "when getting on mount").
    /// Desktop: a ranged spell cast of ours puts the drawn weapons to their sheath place; they come back with the next return to
    /// melee - auto attack starting (or Auto Shot), a swing / shot landing (not within PutAwayHold of the put-away, not while
    /// casting), a melee ability, or the Z key. The session's sheath state (UNIT_FIELD_BYTES_2 / CMSG_SET_SHEATHED) is NOT changed:
    /// offline SetSheath(Sheathed) stops the auto attack and a cast must not, and the real client's own cast sheathing is a client-side
    /// rule (AC SharedDefines.h:388 SPELL_ATTR0_DO_NOT_SHEATH 0x00040000 "Don't shealthe weapons (client only)"; AC never sets a
    /// player's sheath byte itself - its only SetSheath for players is HandleSetSheathedOpcode, CombatHandler.cpp:73-82).
    /// Mounting puts them away too (desktop and VR) and they stay away while mounted (the Z key and VR grabs are refused). Dismounting
    /// changes nothing (AC Unit::Dismount, Unit.cpp 10323-10345, touches no sheath state): they stay away until a return to melee.
    /// Others see it: our record's held hands (ints Right / Left, from _right / _left) are empty, so their GearRemote draws the
    /// weapons at the sheath places; our WoW stance loop (record int 32) is not the weapon stance while they are away.
    ///
    /// The ranged-spell rule (ours, from Spell.dbc; harness probe CombatFeel/SheathRule measured it):
    ///   not passive, not on-next-swing, not Attr0 IS_ABILITY 0x10 (warrior / rogue / hunter skills, wand Shoot), not Attr0
    ///   USES_RANGED_SLOT 0x2 (weapon shots), DmgClass 0 none / 1 magic (2 melee and 3 ranged are weapon attacks: Crusader Strike,
    ///   Judgement, Hammer of Wrath keep the weapon), not a mount spell, and aimed beyond melee reach (RangeMax > 5 yd - Exorcism 879:
    ///   30 yd, Holy Light 635: 40 yd, Hammer of Justice 853: 10 yd) or with a cast time / channel (Hearthstone 8690).
    ///   Self spells without a cast time (seals, auras, Consecration: RangeMax 0) keep the weapon.
    /// NOTE (measured): WoW's own client-only attribute DO_NOT_SHEATH is SET on Exorcism (Attr0 0x00050000), Hammer of Justice and the
    /// blessings, and clear on Holy Light / Flash of Light / Frostbolt - the real client keeps the weapon for Exorcism. The user asked
    /// for Exorcism, so the range rule above decides, not that attribute.
    /// A melee ability (draws again): on-next-swing, the melee range flag (SpellRange flags 1), or DmgClass 2 / 3 with a weapon
    /// required (EquippedItemClass 2: Divine Storm); a ranged weapon ability (USES_RANGED_SLOT: Arcane Shot, wand Shoot) draws too.
    /// VR: a cast never takes a weapon out of a VR hand (the player's own hands decide); mounting does (the hands let go, the
    /// session is told Sheathed as for any VR put-back) and no weapon can be grabbed while mounted.
    /// </summary>
    public sealed partial class GearLocal
    {
        /// <summary>Ours: a spell aimed farther than this (yards) is a ranged spell. 5 yd = the melee abilities' SpellRange (Crusader
        /// Strike 35395, Heroic Strike 78, Sinister Strike 1752: RangeMax 5 with the melee flag; measured, harness SheathRule).</summary>
        public const float MeleeReachYards = 5f;
        /// <summary>Ours: after a put-away a landing swing / shot does not draw for this long (an instant spell next to a swing would
        /// flicker the weapon); the Z key, auto attack starting and melee abilities draw at once.</summary>
        public const float PutAwayHold = 0.5f;

        private const uint Attr0UsesRangedSlot = 0x2u;   // AC SharedDefines.h:371 SPELL_ATTR0_USES_RANGED_SLOT

        private bool _putAway;            // desktop: our weapons are at their sheath places though the session says drawn
        private float _putAwayAt = -10f;
        private bool _wasMounted;
        private int _sheatheCast;         // the CastingSpellId already judged
        private int _sheatheCounter = -1; // AttackCounter last seen by the draw rule
        private int _putAwaySaid, _drawSaid, _mountedKeySaid;
        private float _drawPendingAt = -10f;  // a return to melee while still mounted (online the dismount follows the server): draw at the dismount
        /// <summary>Ours: how long such a pending draw waits for the dismount (s).</summary>
        private const float DrawPendingSeconds = 3f;

        /// <summary>The weapons are at their sheath places although the session's sheath state says drawn (a ranged cast, the mount).</summary>
        public bool WeaponsPutAway { get { return !_xr && _putAway; } }

        /// <summary>The ranged-spell rule (see the class notes): this cast puts drawn weapons away.</summary>
        public static bool PutsWeaponsAway(SpellInfo s)
        {
            if (s == null || s.IsPassive || s.OnNextSwing || s.IsAbility || (s.Attributes[0] & Attr0UsesRangedSlot) != 0) return false;
            if (s.DmgClass != 0 && s.DmgClass != 1) return false;
            if (MountCatalog.IsMount(s.Id)) return false;
            return s.RangeMax > MeleeReachYards || s.CastTimeMs > 0 || s.IsChanneled;
        }

        /// <summary>A melee (or ranged weapon) ability: using it draws the weapons again.</summary>
        public static bool DrawsWeapons(SpellInfo s)
        {
            if (s == null || s.IsPassive) return false;
            if (s.OnNextSwing || (s.RangeFlags & 1) != 0 || (s.Attributes[0] & Attr0UsesRangedSlot) != 0) return true;
            return (s.DmgClass == 2 || s.DmgClass == 3) && s.EquippedItemClass == 2;   // a weapon attack (Divine Storm); not Battle Stance (DmgClass 2, no weapon)
        }

        private void ClearSheathe()
        {
            _putAway = false;
            _putAwayAt = -10f;
            _wasMounted = false;
            _drawPendingAt = -10f;
            _sheatheCast = 0;
            _sheatheCounter = -1;
        }

        /// <summary>Tick, before the hands are decided: the mount, a new cast, a landed swing.</summary>
        private void StepSheathe(IWowSession s, PlayerState p)
        {
            bool mounted = p.Mounted && !p.Dead;
            if (mounted && !_wasMounted)
            {
                if (_xr)
                {
                    if (_right != 0 || _left != 0)
                    {
                        _right = _left = 0;
                        ApplySheath(s, p);
                        SayPutAway("Gear: VR hands let go of the weapons for the mount (" + p.MountDisplayId + "); no weapon can be drawn while mounted");
                    }
                }
                else PutAway(p, "the mount (display " + p.MountDisplayId + ")");
                _drawPendingAt = -10f;
            }
            if (!mounted && _wasMounted && Time.time - _drawPendingAt <= DrawPendingSeconds) { _drawPendingAt = -10f; _wasMounted = false; Draw(p, "attacking after the auto dismount"); }
            _wasMounted = mounted;

            int casting = p.Dead ? 0 : p.CastingSpellId;
            if (casting != 0 && casting != _sheatheCast) Judge(p, casting);
            _sheatheCast = casting;

            if (_sheatheCounter >= 0 && p.AttackCounter != _sheatheCounter && casting == 0 && Time.time - _putAwayAt >= PutAwayHold)
                Draw(p, p.Sheath == SheathState.Ranged ? "a shot" : "a swing");
            _sheatheCounter = p.AttackCounter;
        }

        /// <summary>OnEvent: instants (CastSucceeded of a spellbook spell), auto attack / Auto Shot starting.</summary>
        private void SheatheEvent(PlayerState p, GameEvent e)
        {
            if (p == null || e.SourceGuid != p.Guid || p.Guid == 0) return;
            if (e.Type == GameEventType.AttackStarted) { Draw(p, e.SpellId == 75 ? "Auto Shot" : "auto attack"); return; }
            if (e.Type != GameEventType.CastSucceeded || e.SpellId <= 0 || e.SpellId == 75) return;
            if (!InSpellbook(p, e.SpellId)) return;                              // triggered spells (procs) are not a cast of ours
            Judge(p, e.SpellId);
        }

        private void Judge(PlayerState p, int spellId)
        {
            SpellInfo sp = null;
            try { sp = _files != null ? SpellDb.Get(_files).Find(spellId) : null; }
            catch (Exception) { sp = null; }
            if (sp == null) return;
            if (PutsWeaponsAway(sp)) PutAway(p, "a ranged cast (" + SpellName(spellId) + ")");
            else if (DrawsWeapons(sp)) Draw(p, "a melee ability (" + SpellName(spellId) + ")");
        }

        private void PutAway(PlayerState p, string why)
        {
            if (_xr || _putAway || p.Sheath == SheathState.Sheathed) return;   // VR hands decide; nothing drawn: nothing to put away
            _putAway = true;
            _putAwayAt = Time.time;
            SayPutAway("Gear: weapon sheathed for " + why + " - other Somnium players see it sheathed too");
        }

        private void Draw(PlayerState p, string why)
        {
            if (!_putAway) return;
            if (p.Mounted && !p.Dead) { _drawPendingAt = Time.time; return; }   // stays away while mounted (an auto dismount draws when it lands)
            _putAway = false;
            _drawPendingAt = -10f;
            _drawSaid++;
            if (_drawSaid <= 20 || _drawSaid % 50 == 0) WowLog.Info("Gear: weapons drawn again (" + why + ")");
        }

        private void SayPutAway(string line)
        {
            _putAwaySaid++;
            if (_putAwaySaid <= 20 || _putAwaySaid % 50 == 0) WowLog.Info(line);
        }

        /// <summary>The Z key on the desktop. True = handled here (mounted, or weapons put away while the session says drawn).</summary>
        private bool SheatheKey(PlayerState p)
        {
            if (p.Mounted && !p.Dead)
            {
                if (_mountedKeySaid++ < 5) WowLog.Info("Gear: the Z key - the weapons stay sheathed while mounted");
                return true;
            }
            if (!_putAway) return false;
            if (p.Sheath == SheathState.Sheathed) { _putAway = false; return false; }   // the normal toggle draws
            Draw(p, "the Z key");
            return true;
        }
    }
}
