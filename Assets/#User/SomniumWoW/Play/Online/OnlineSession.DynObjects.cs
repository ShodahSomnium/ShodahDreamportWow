// iteration 11 - owner spellfx: ground areas online (proposal-spellfx.md s1.2 / s3.2 / s6.4). TYPEID_DYNAMICOBJECT create blocks (AC
// DynamicObject.cpp:28-33 UPDATEFLAG_LOWGUID | STATIONARY_POSITION | POSITION; values DynamicObject.cpp:97-131, UpdateFields.cs DYNAMICOBJECT_*)
// -> AreaEffects (every caster's: Blizzard, Consecration, Death and Decay, Flamestrike's burn, creatures' void zones in dungeons ...);
// SMSG_PLAY_SPELL_VISUAL / _IMPACT -> SpellKit events; per-unit weapon enchants, aura states and stealth (Objects.cs Refresh hook).
// DynCreated / DynRemoved / DynUpdated / DynClear / SpFillUnitExtras are partial methods declared in OnlineSession.Hooks11.cs and called
// from OnlineSession.Objects.cs (wave 0).
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        readonly List<AreaEffectView> _areaEffects = new List<AreaEffectView>();
        int _dynLogs, _kitLogs;

        /// <summary>Spell areas on the ground near the player (dynamic objects of every caster). Stable instances, updated in place.</summary>
        public IReadOnlyList<AreaEffectView> AreaEffects { get { return _areaEffects; } }

        /// <summary>A TYPEID_DYNAMICOBJECT create block; its POSITION 0x100 movement block (Object.cpp:376-406) is already in o.X/Y/Z.</summary>
        partial void DynCreated(OnlineObject o)
        {
            if (o == null || _areaEffects.Exists(x => x.Guid == o.Guid)) return;
            var a = new AreaEffectView { Guid = o.Guid };
            _areaEffects.Add(a);
            DynFill(o, a);
            _dynLogs++;
            if (_dynLogs <= 20 || _dynLogs % 50 == 0)
                WowLog.Info("Online: ground area '" + SpellName(a.SpellId) + "' " + a.SpellId + " of 0x" + a.CasterGuid.ToString("X") + " at " + a.Pos + " radius " +
                            a.Radius.ToString("0.#") + " yd (type " + a.Type + ") #" + _dynLogs);
        }

        partial void DynUpdated(OnlineObject o)
        {
            if (o == null) return;
            var a = _areaEffects.Find(x => x.Guid == o.Guid);
            if (a != null) DynFill(o, a);
        }

        void DynFill(OnlineObject o, AreaEffectView a)
        {
            a.CasterGuid = o.U64(UF.DYNAMICOBJECT_CASTER);
            a.SpellId = o.I32(UF.DYNAMICOBJECT_SPELLID);
            a.Radius = o.F32(UF.DYNAMICOBJECT_RADIUS);
            a.Type = o.Byte(UF.DYNAMICOBJECT_BYTES, 0);                               // DynamicObjectType (DynamicObject.h:27-32): 1 area spell, 2 far sight
            if (a.SpellId == 0) a.SpellId = o.Entry;                                  // OBJECT_FIELD_ENTRY = the spell id as well (DynamicObject.cpp:111 SetEntry(spellId))
            if (o.HasPos) a.Pos = new WowPos(o.X, o.Y, o.Z, o.O);
        }

        partial void DynRemoved(OnlineObject o)
        {
            if (o != null) _areaEffects.RemoveAll(x => x.Guid == o.Guid);
        }

        /// <summary>ClearWorld (a far teleport / world change): the areas go, and an aim ends.</summary>
        partial void DynClear()
        {
            _areaEffects.Clear();
            if (_player.Targeting != null) { WowLog.Info("Online: " + _player.Targeting.Name + " no longer waits (the world changed)"); _player.Targeting = null; }
        }

        void DynTick(float dt) { for (int i = 0; i < _areaEffects.Count; i++) _areaEffects[i].Age += dt; }

        /// <summary>SMSG_PLAY_SPELL_VISUAL 0x1F3 (Unit.cpp:14799-14805) / SMSG_PLAY_SPELL_IMPACT 0x1F7 (:14807-14813): uint64 guid; uint32 SpellVisualKit id.
        /// The kit's models + sound play on the unit (SpellKit event, WowSpellVfx); its animation (SpellVisualKit field 2 AnimID) plays once on another
        /// unit's model through EntityState.Emote / EmoteCounter (a trainer's kit 179 = AnimID 53 SpellCastDirected, proposal-trainer.md 3.5).</summary>
        void OnPlaySpellKit(WowPacket p, bool impact)
        {
            ulong guid = p.U64(); int kit = (int)p.U32();
            _events.Enqueue(new GameEvent { Type = GameEventType.SpellKit, SourceGuid = guid, Amount = kit });
            var e = guid != _player.Guid ? Find(guid) : null;
            var k = SomniumSpace.Worlds.SomniumWoW.Models.SpellVisualDb.Get(_files).KitById(kit);
            if (e != null && k != null && k.Anim > 0) { e.Emote = k.Anim; e.EmoteOnce = true; e.EmoteCounter++; }   // lead fix 2026-10-11: once
            if (_kitLogs++ < 10)
                WowLog.Info("Online: " + (impact ? "SMSG_PLAY_SPELL_IMPACT" : "SMSG_PLAY_SPELL_VISUAL") + " kit " + kit + " on 0x" + guid.ToString("X") +
                            (k != null && k.Anim > 0 ? " (animation " + k.Anim + ")" : "") + " (logged 10 times)");
        }

        /// <summary>Hook 1 (Objects.cs Refresh, units and players): other players' weapon enchants, aura states, stealth.</summary>
        partial void SpFillUnitExtras(OnlineObject o, EntityState s)
        {
            if (o == null || s == null) return;
            s.AuraStateMask = o.U32(UF.UNIT_FIELD_AURASTATE);                                         // per receiver (Unit.cpp:7423-7432 BuildAuraStateUpdateForTarget)
            s.Stealthed = (o.Byte(UF.UNIT_FIELD_BYTES_1, 2) & 0x02) != 0;                              // UNIT_STAND_FLAGS_CREEP (UnitDefines.h:51), byte 2 = VIS_FLAG (:28)
            if (o.TypeId != TypeId.Player) return;
            if (s.EquipEnchants == null) s.EquipEnchants = new int[19];
            for (int k = 0; k < 19; k++) s.EquipEnchants[k] = (int)o.U32(UF.PLAYER_VISIBLE_ITEM_1_ENCHANTMENT + k * 2);   // perm | temp << 16 (PlayerStorage.cpp:2968-2983)
        }
    }
}
