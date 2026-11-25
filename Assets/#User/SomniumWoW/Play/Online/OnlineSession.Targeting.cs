// iteration 11 - owner spellfx: WoW's "spell on the cursor" online (proposal-spellfx.md s1.1 / s1.4 / s6.3 / s6.4) - a spell waiting for a
// place (the green circle: Spell.dbc Targets 0x40) or an item (Targets 0x10: enchants, poisons, stones, oils, Disenchant ...), the CMSG_USE_ITEM
// targets of such an item, SMSG_ITEM_ENCHANT_TIME_UPDATE / SMSG_ENCHANTMENTLOG and the death knight rune opcodes.
// CMSG_CAST_SPELL (SpellHandler.cpp:378-552) / CMSG_USE_ITEM (SpellHandler.cpp:73-194) carry SpellCastTargets (Spell.cpp:163-214 Read):
// uint32 mask; [unit/GO/corpse: packed guid]; [ITEM 0x10 / TRADE_ITEM 0x1000: packed item guid]; [SOURCE 0x20]; [DEST 0x40: packed transport
// guid + x y z]; [STRING 0x2000].
using System;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        int _spAimSerial, _spAimLogs;
        double _spAimSince;
        const double SpAimTimeout = 30.0;                   // ours (as vrcast's SlottingTimeout): an aim nobody confirms ends
        int _spConfirmedFor;                                // the spell whose place / item was just confirmed (consumed by SpTargets / SpWriteUseTargets)
        bool _spConfirmedHasDest; WowPos _spConfirmedDest; ulong _spConfirmedItem;
        bool _spConfirmedTrade;                             // the trade partner's non-traded slot (TARGET_FLAG_TRADE_ITEM + 6)
        const ulong TradeSlotNonTraded = 6;                 // TRADE_SLOT_NONTRADED (TradeData.h:31) - sent in the packed "guid" (Spell.cpp:526-529)
        int _spHeldAimFor; bool _spHeldHasDest; WowPos _spHeldDest; ulong _spHeldItem;   // the same, kept with a client-side held spell
        bool _spRangeNoteLogged, _spAimRulesLogged;

        /// <summary>True (and the aim started) when s must wait for a place / an item. held = from CastSpellHeld (immersive).</summary>
        bool SpNeedsAim(SpellInfo s, ulong targetGuid, bool held)
        {
            if (s == null || _spConfirmedFor == s.Id) return false;
            ulong pick = targetGuid != 0 ? targetGuid : _target;
            var te = pick != 0 && pick != _player.Guid ? Find(pick) : null;
            var kind = SpellTargeting.KindOf(s, te != null && te.Kind == EntityKind.GameObject);
            if (kind == SpellTargetingKind.None) return false;
            SpBeginAim(s, kind, held, -1);
            return true;
        }

        void SpBeginAim(SpellInfo s, SpellTargetingKind kind, bool held, int fromBag)
        {
            if (!_spAimRulesLogged)
            {
                _spAimRulesLogged = true;
                WowLog.Info("Online: spells that need a place (Spell.dbc Targets 0x40) or an item (0x10) wait on the cursor; ours: an aim ends after " +
                            SpAimTimeout + " s without a choice, the place is checked against the friendly range pair (AC Object.cpp:3967-3989), line of sight is the server's");
            }
            int rp, radP; float rf, radF;
            bool known = SpRangeMods(s, out rp, out rf, out radP, out radF);
            var v = SpellTargeting.NewView(s, kind, held, known, rp, rf, radP, radF);
            v.FromBagIndex = fromBag;
            if (fromBag >= 0 && fromBag < _player.Bags.Count && _player.Bags[fromBag] != null && !string.IsNullOrEmpty(_player.Bags[fromBag].Name)) v.Name = _player.Bags[fromBag].Name;
            v.Serial = ++_spAimSerial;
            _player.Targeting = v;                          // replaces an aim already waiting (WoW: another spell on the cursor swaps it)
            _spAimSince = _now;
            if (_spAimLogs++ < 10)
                WowLog.Info("Online: " + v.Name + " waits for " + (kind == SpellTargetingKind.Ground
                    ? "a place (Spell.dbc Targets 0x40: radius " + v.Radius.ToString("0.#") + " yd, range " + v.RangeMin.ToString("0.#") + "-" + v.RangeMax.ToString("0.#") + (v.RangeExact ? "" : " without talent range mods") + ")"
                    : "an item (Targets 0x10" + (v.ItemClass >= 0 ? ", item class " + v.ItemClass : ", any item") + ")") + (held ? ", immersive" : "") + " - logged 10 times");
        }

        /// <summary>Our talent SPELLMOD_RANGE 5 / SPELLMOD_RADIUS 6 for s (Player::ApplySpellMod: base * (1 + pct/100) + flat). Filled by the
        /// talents area's spell-modifier table through SpRangeModsFromTalents (proposal-talents.md 3.11, SpellModValue ops 5 / 6); until that is
        /// implemented it returns false = the place check only colours the circle and the server decides (never refused by us).</summary>
        bool SpRangeMods(SpellInfo s, out int rangePct, out float rangeFlat, out int radiusPct, out float radiusFlat)
        {
            bool known = false;
            rangePct = radiusPct = 0; rangeFlat = radiusFlat = 0f;
            SpRangeModsFromTalents(s.Id, ref known, ref rangePct, ref rangeFlat, ref radiusPct, ref radiusFlat);
            return known;
        }

        /// <summary>The talents area (or the integrator) implements this in OnlineSession.Talents.cs: known = true and the SPELLMOD_RANGE 5 /
        /// SPELLMOD_RADIUS 6 sums of the spell (SpellModValue). Unimplemented = compiled away (the range is not exact).</summary>
        partial void SpRangeModsFromTalents(int spellId, ref bool known, ref int rangePct, ref float rangeFlat, ref int radiusPct, ref float radiusFlat);

        public void ConfirmTargetingAt(WowPos dest)
        {
            var v = _player.Targeting;
            if (v == null || v.Kind != SpellTargetingKind.Ground) return;
            var si = Db.Find(v.SpellId);
            if (si != null && v.FromBagIndex < 0 && SpTimerLeft(si) > SpQueueWindow) { Error("Spell is not ready yet"); return; }   // ERR_SPELL_COOLDOWN (GlobalStrings.lua:3493): the GCD - keep the circle (ours: the place is not lost)
            string why = SpellTargeting.CheckPlace(v, _haveAvatar ? _avatarPos : _player.Pos, dest);
            if (why != null && v.RangeExact) { Error(why); return; }                 // "Out of range" (GlobalStrings.lua:7147) / "Target too close" (:7193); still aiming
            if (why != null && !_spRangeNoteLogged)
            {
                _spRangeNoteLogged = true;
                WowLog.Info("Online: " + v.Name + " placed beyond its base range (" + why + ") - sent anyway, the server decides (our range talents are not known; logged once)");
            }
            _player.Targeting = null;
            SpConfirm(v, true, dest, 0);
        }

        public void ConfirmTargetingItem(int bagIndex, int equipSlot)
        {
            var v = _player.Targeting;
            if (v == null || v.Kind != SpellTargetingKind.Item) return;
            ulong item = SpItemGuid(bagIndex, equipSlot);
            if (item == 0) { Error("Item is gone"); return; }                         // SPELL_FAILED_ITEM_GONE (GlobalStrings.lua:7072); still aiming
            _player.Targeting = null;
            SpConfirm(v, false, default(WowPos), item);
        }

        public void ConfirmTargetingTrade()
        {
            var v = _player.Targeting;
            if (v == null || v.Kind != SpellTargetingKind.Item) return;
            _player.Targeting = null;
            _spConfirmedTrade = true;
            try { SpConfirm(v, false, default(WowPos), 0); }
            finally { _spConfirmedTrade = false; }
        }

        void SpConfirm(SpellTargetingView v, bool hasDest, WowPos dest, ulong item)
        {
            _spConfirmedFor = v.SpellId; _spConfirmedHasDest = hasDest; _spConfirmedDest = dest; _spConfirmedItem = item;
            WowLog.Info("Online: " + v.Name + " " + (hasDest ? "placed at " + dest : _spConfirmedTrade ? "on the trade partner's item (TARGET_FLAG_TRADE_ITEM, slot 6)" : "on item 0x" + item.ToString("X")) + (v.Held ? " (immersive)" : ""));
            try
            {
                if (v.FromBagIndex >= 0) UseItem(v.FromBagIndex);                       // the items owner's UseItem -> SpItemNeedsTargeting false -> SendUseItem -> SpWriteUseTargets
                else if (v.Held) CastSpellHeld(v.SpellId, 0);
                else SpCast(v.SpellId, 0);
            }
            finally { _spConfirmedFor = 0; _spConfirmedHasDest = false; _spConfirmedItem = 0; }
        }

        public void CancelTargeting()
        {
            var v = _player.Targeting;
            if (v == null) return;
            _player.Targeting = null;
            WowLog.Info("Online: " + v.Name + " no longer waits (cancelled)");      // no CastFailed: nothing was cast
        }

        public void CastSpellOnItem(int spellId, int bagIndex, int equipSlot)
        {
            ulong item = SpItemGuid(bagIndex, equipSlot);
            if (item == 0) { Error("Item is gone"); return; }                         // SPELL_FAILED_ITEM_GONE (GlobalStrings.lua:7072)
            _player.Targeting = null;                                                 // replaces a waiting spell (WoW)
            var s = Db.Find(spellId);
            SpConfirm(new SpellTargetingView { SpellId = spellId, Name = s != null ? s.Name : "spell " + spellId, Kind = SpellTargetingKind.Item }, false, default(WowPos), item);
        }

        /// <summary>The guid of a bag item (Player.Bags index = _bagPos index, OnlineSession.Items.cs) or of an equipped item
        /// (PLAYER_FIELD_INV_SLOT_HEAD + slot * 2, PlayerStorage.cpp:2747). 0 = none.</summary>
        ulong SpItemGuid(int bagIndex, int equipSlot)
        {
            if (bagIndex >= 0) return bagIndex < _bagPos.Count ? _bagPos[bagIndex].Guid : 0;
            if (equipSlot >= 0 && equipSlot < 19 && _self != null) return _self.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + equipSlot * 2);
            return 0;
        }

        /// <summary>Hook 3 (OnlineSession.Items.cs UseItem, before SendUseItem): an item whose spell needs a place / an item waits for it.</summary>
        bool SpItemNeedsTargeting(int bagIndex, int spell, string label)
        {
            var s = Db.Find(spell);
            if (s == null || _spConfirmedFor == spell) return false;
            var kind = SpellTargeting.KindOf(s, false);
            if (kind == SpellTargetingKind.None) return false;
            SpBeginAim(s, kind, false, bagIndex);
            if (_player.Targeting != null && string.IsNullOrEmpty(_player.Targeting.Name)) _player.Targeting.Name = label;
            return true;
        }

        /// <summary>Hook 3 (SendUseItem): the SpellCastTargets of CMSG_USE_ITEM (SpellHandler.cpp:193 targets.Read; Spell.cpp:163-214): the
        /// confirmed place / item / trade slot, a HELD_ITEM_ONLY spell's main hand, else today's unit / none.</summary>
        void SpWriteUseTargets(PacketWriter w, SpellInfo si)
        {
            if (si != null && _spConfirmedFor == si.Id && _spConfirmedHasDest) { w.U32(TfDestLocation).PackedGuid(0).F32(_spConfirmedDest.X).F32(_spConfirmedDest.Y).F32(_spConfirmedDest.Z); return; }
            if (si != null && _spConfirmedFor == si.Id && _spConfirmedItem != 0) { w.U32(TfItem).PackedGuid(_spConfirmedItem); return; }
            if (si != null && _spConfirmedFor == si.Id && _spConfirmedTrade) { w.U32(TfTradeItem).PackedGuid(TradeSlotNonTraded); return; }   // Spell.cpp:526-529
            if (si != null && si.HeldItemOnly && (si.Targets & TfItem) != 0 && _self != null)
            {
                ulong main = _self.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + 15 * 2);                      // SPELL_ATTR0_HELD_ITEM_ONLY: the main hand (SharedDefines.h:379)
                if (main != 0) { w.U32(TfItem).PackedGuid(main); return; }
            }
            bool unit = si != null && si.NeedsUnitTarget && _target != 0 && _target != _player.Guid;   // today's rule (Items.cs SendUseItem), unchanged
            if (unit) w.U32(TfUnit).PackedGuid(_target); else w.U32(0);
        }

        /// <summary>Per frame (from TickAuras): an aim ends on death, as a ghost, on a flight or after SpAimTimeout (a far teleport: DynClear).</summary>
        void SpTickAim()
        {
            var v = _player.Targeting;
            if (v == null) return;
            string why = _player.Dead || _player.Ghost ? "dead" : _player.OnTaxi ? "on a flight" : _now - _spAimSince > SpAimTimeout ? SpAimTimeout + " s without a choice" : null;
            if (why == null) return;
            _player.Targeting = null;
            WowLog.Info("Online: " + v.Name + " no longer waits (" + why + ")");
        }

        /// <summary>SMSG_ITEM_ENCHANT_TIME_UPDATE 0x1EB (ItemHandler.cpp:1051-1059, ItemPackets.cpp:125-133): uint64 item; uint32 slot (0 PERM, 1 TEMP,
        /// Item.h:167-184); uint32 duration SECONDS (PlayerStorage.cpp:4403); uint64 player.</summary>
        void OnItemEnchantTimeUpdate(WowPacket p)
        {
            ulong item = p.U64(); uint slot = p.U32(); uint seconds = p.U32(); p.U64();
            if (slot != 1 || _self == null) return;
            for (int s = 15; s <= 17; s++)
                if (_self.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + s * 2) == item) _spTempEnchantLeft[s - 15] = seconds;
            if (_spEnchTimeLogs++ < 5) WowLog.Info("Online: temporary enchant of item 0x" + item.ToString("X") + ": " + seconds + " s left (SMSG_ITEM_ENCHANT_TIME_UPDATE; logged 5 times)");
        }
        readonly float[] _spTempEnchantLeft = new float[3];
        int _spEnchTimeLogs;

        /// <summary>SMSG_ENCHANTMENTLOG 0x1D7 (ItemHandler.cpp:1041-1049, ItemPackets.cpp:115-123): packed owner, packed caster, uint32 item entry,
        /// uint32 enchant. The 3.3.5 client shows nothing for it - logged (first 10).</summary>
        void OnEnchantmentLog(WowPacket p)
        {
            ulong owner = p.PackedGuid(); ulong caster = p.PackedGuid(); uint entry = p.U32(); uint ench = p.U32();
            if (_spEnchLogs++ < 10) WowLog.Info("Online: enchant " + ench + " on item " + entry + " of 0x" + owner.ToString("X") + " by 0x" + caster.ToString("X") + " (SMSG_ENCHANTMENTLOG; logged 10 times)");
        }
        int _spEnchLogs;

        // Runes (death knights). RUNE_LIST of our SPELL_GO feeds _runes (OnSpellGo); these three adjust it.
        readonly RuneTracker _runes = new RuneTracker();
        readonly float[] _runeRegen = new float[4];
        void OnConvertRune(WowPacket p) { int i = p.U8(), t = p.U8(); _runes.Convert(i, t); }                                   // Player.cpp:13751-13759
        void OnResyncRunes(WowPacket p)                                                                                         // Player.cpp:13761-13771
        {
            int n = (int)p.U32();
            if (n < 0 || n > 64) return;
            var t = new int[n]; var g = new byte[n];
            for (int i = 0; i < n; i++) { t[i] = p.U8(); g[i] = (byte)p.U8(); }
            _runes.Resync(t, g);
        }
        void OnAddRunePower(WowPacket p) { _runes.AddPower(p.U32()); }                                                        // Player.cpp:13773-13778
    }
}
