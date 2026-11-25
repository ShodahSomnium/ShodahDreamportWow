// iteration 11 - owner vrcast (proposal-vrcast.md s6.3): WoW's 144 action buttons online.
// - SMSG_ACTION_BUTTONS (Player::SendActionButtons, Player.cpp:5720-5746): every slot 0..143 of every type is kept in PlayerState.ActionButtons
//   (packed exactly as the packet carries it, WowActionSlots); PlayerState.ActionBar (the spell ids of 0..11) is kept in step for older readers.
// - SetActionButton / SetActionBarSlot -> CMSG_SET_ACTION_BUTTON (WorldSession::HandleSetActionButtonOpcode, MiscHandler.cpp:899-938).
// - UseActionButton = WoW's UseAction: a spell is cast, a bag item used (UseItem), an EQUIPPED item with a "Use:" spell used through
//   CMSG_USE_ITEM bag 255 + its equipment slot (SpellHandler.cpp:81 GetUseableItemByPos), macros / equipment sets say they run in the WoW client.
// - A superseded rank (SMSG_SUPERCEDED_SPELL) is swapped on every button and the swap is told to the server (it only tells the client,
//   Player.cpp:3159-3184 "send packet to swap on action bar" - without CMSG_SET_ACTION_BUTTON a relog shows the lower rank again).
// SpOnActionButtons / SetActionBarSlot moved here from OnlineSession.Spells.cs in wave 0 (same names: the registration in InitSpellsO2 stays).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        int _abSetLogs, _abUseLogs, _abPackets;
        readonly HashSet<int> _abUnknownLogged = new HashSet<int>();
        readonly HashSet<int> _abOddLogged = new HashSet<int>();

        /// <summary>ActionBar[0..11] = the spell ids of ActionButtons[0..11] (older readers of PlayerState.ActionBar).</summary>
        void AbSyncBar()
        {
            for (int k = 0; k < _player.ActionBar.Length && k < WowActionSlots.Count; k++) _player.ActionBar[k] = WowActionSlots.SpellOf(_player.ActionButtons[k]);
        }

        void AbInfo(string text) { _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text }); }

        /// <summary>A packed action in words for the log: "spell 133 'Fireball'", "item 118", "macro 3", "empty".</summary>
        string AbDescribe(int packed)
        {
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Empty: return "empty";
                case ActionKind.Spell: { int id = WowActionSlots.SpellOf(packed); return id + " '" + SpellName(id) + "'"; }
                case ActionKind.Item: { int e = WowActionSlots.ItemOf(packed); ItemProto d; return "item " + e + (_itemProtos.TryGetValue(e, out d) && !d.Missing && d.Name.Length > 0 ? " '" + d.Name + "'" : ""); }
                case ActionKind.Macro: return "macro " + WowActionSlots.ActionOf(packed);
                case ActionKind.EquipmentSet: return "equipment set " + WowActionSlots.ActionOf(packed);
                default: return "type 0x" + WowActionSlots.TypeOf(packed).ToString("X2") + " action " + WowActionSlots.ActionOf(packed);
            }
        }

        /// <summary>The bar of a slot for the summary line: 0 main page 1, 1 page 2, 2 Right Bar, 3 Right Bar 2, 4 Bottom Right, 5 Bottom Left,
        /// 6 the four form bars (72-119), 7 vehicle / possess (120-143) - ActionButton.lua:1-9 page numbers (proposal-vrcast s4.1).</summary>
        static int AbBarIndex(int slot)
        {
            int page = WowActionSlots.PageOf(slot);
            switch (page)
            {
                case 1: return 0;
                case 2: return 1;
                case WowActionSlots.PageRight: return 2;
                case WowActionSlots.PageRight2: return 3;
                case WowActionSlots.PageBottomRight: return 4;
                case WowActionSlots.PageBottomLeft: return 5;
                default: return page <= WowActionSlots.Pages + WowActionSlots.MaxBonusBar ? 6 : 7;
            }
        }

        // ------------------------------------------------------------------ SMSG_ACTION_BUTTONS

        /// <summary>
        /// SMSG_ACTION_BUTTONS (Player::SendActionButtons, Player.cpp:5720-5746): uint8 state; unless state 2 ("let client clear his current
        /// Actions", the talent spec swap Player.cpp:15530) MAX_ACTION_BUTTONS (144) x uint32 packed = action | type &lt;&lt; 24 (Player.h:220-236:
        /// 0 spell, 0x20 equipment set, 0x40 / 0x41 macro, 0x80 item; 0 = empty). Every slot and type is kept (PlayerState.ActionButtons).
        /// </summary>
        void SpOnActionButtons(WowPacket p)
        {
            int state = p.U8();
            if (state == 2)
            {
                Array.Clear(_player.ActionButtons, 0, _player.ActionButtons.Length);
                Array.Clear(_player.ActionBar, 0, _player.ActionBar.Length);
                CheckRead(p);
                WowLog.Info("Online: action buttons cleared by the server (SMSG_ACTION_BUTTONS state 2 - a talent spec swap; the new spec's buttons follow)");
                return;
            }
            int n = Math.Min(WowActionSlots.Count, p.Remaining / 4);
            int spells = 0, items = 0, macros = 0, sets = 0, others = 0, unknown = 0;
            var perBar = new int[8];
            for (int b = 0; b < WowActionSlots.Count; b++)
            {
                int v = b < n ? unchecked((int)p.U32()) : 0;
                _player.ActionButtons[b] = v;
                if (v == 0) continue;
                perBar[AbBarIndex(b)]++;
                switch (WowActionSlots.KindOf(v))
                {
                    case ActionKind.Spell:
                        spells++;
                        int id = WowActionSlots.SpellOf(v);
                        // AzerothCore keeps only known spells on buttons (IsActionButtonDataValid, Player.cpp:5768-5774) - this should stay 0.
                        if (_haveSpellbook && !_known.Contains(id)) { unknown++; if (_abUnknownLogged.Add(id)) WowLog.Info("Online: action button " + b + " holds spell " + id + " '" + SpellName(id) + "' that is not in our spell list (logged once per spell)"); }
                        break;
                    case ActionKind.Item: items++; break;
                    case ActionKind.Macro: macros++; break;
                    case ActionKind.EquipmentSet: sets++; break;
                    default: others++; break;
                }
            }
            if (n != WowActionSlots.Count) SpOdd(p, n + " buttons instead of " + WowActionSlots.Count);
            else CheckRead(p);
            AbSyncBar();
            _spDroppedButtons = macros + sets + others;   // the buttons we show but cannot run (macros / equipment sets run in the WoW client)
            _abPackets++;
            if (_abPackets <= 20 || _abPackets % 50 == 0)
                WowLog.Info("Online: action buttons from the server (SMSG_ACTION_BUTTONS state " + state + ") - " + spells + " spells, " + items + " items, " + macros + " macros, "
                            + sets + " equipment sets, " + others + " others; main 0-11: " + perBar[0] + ", page 2: " + perBar[1] + ", Right Bar: " + perBar[2]
                            + ", Right Bar 2: " + perBar[3] + ", Bottom Right: " + perBar[4] + ", Bottom Left: " + perBar[5] + ", form bars: " + perBar[6] + ", vehicle: " + perBar[7]
                            + (unknown > 0 ? "; " + unknown + " spell(s) not in our spell list" : "") + (_spDroppedButtons > 0 ? "; " + _spDroppedButtons + " run only in the WoW client" : ""));
        }

        // ------------------------------------------------------------------ CMSG_SET_ACTION_BUTTON

        /// <summary>A spell the player may put on a button: known (SMSG_INITIAL_SPELLS / learned), or one of the Mounts / Companions panes.</summary>
        bool AbMayPlaceSpell(int id)
        {
            if (id <= 0) return false;
            if (_known.Contains(id)) return true;
            var m = _player.Mounts;
            for (int i = 0; i < m.Count; i++) if (m[i] != null && m[i].SpellId == id) return true;
            var c = _player.Companions;
            if (c != null) for (int i = 0; i < c.Count; i++) if (c[i] != null && c[i].SpellId == id) return true;
            return false;
        }

        /// <summary>
        /// Puts a packed action on button slot 0..143 (0 clears it): CMSG_SET_ACTION_BUTTON uint8 button, uint32 packed (MiscHandler.cpp:899-938).
        /// A spell must be known (the server silently refuses others: IsActionButtonDataValid, Player.cpp:5768-5774 - refused here with the
        /// offline text); a type AzerothCore does not take (MiscHandler.cpp:931-934) is not sent.
        /// </summary>
        public void SetActionButton(int slot, int packed)
        {
            if (!WowActionSlots.Valid(slot)) return;
            ActionKind kind = WowActionSlots.KindOf(packed);
            if (kind == ActionKind.Spell && !AbMayPlaceSpell(WowActionSlots.SpellOf(packed)))
            {
                Error("You don't know that spell");   // the offline text (OfflineSession.ActionBars.cs)
                WowLog.Info("Online: action button " + slot + " - spell " + WowActionSlots.SpellOf(packed) + " is not known, not sent");
                return;
            }
            if (kind == ActionKind.Other)
            {
                if (_abOddLogged.Add(WowActionSlots.TypeOf(packed))) WowLog.Info("Online: action button " + slot + " - " + AbDescribe(packed) + " is not a type AzerothCore takes (MiscHandler.cpp:931-934), not sent (logged once per type)");
                return;
            }
            _player.ActionButtons[slot] = packed;
            AbSyncBar();
            if (_world == null) return;
            _world.Send(Op.CMSG_SET_ACTION_BUTTON, new PacketWriter().U8(slot).U32(unchecked((uint)packed)));
            _abSetLogs++;
            if (_abSetLogs <= 30 || _abSetLogs % 50 == 0)
                WowLog.Info("Online: action button " + slot + " (" + WowActionSlots.BarName(slot) + " " + WowActionSlots.ButtonOf(slot) + ") = " + AbDescribe(packed) + " (CMSG_SET_ACTION_BUTTON)");
        }

        /// <summary>A spell on button slot 0..143 (0 clears it) = SetActionButton(slot, WowActionSlots.Spell(spellId)).</summary>
        public void SetActionBarSlot(int slot, int spellId)
        {
            if (spellId < 0 || spellId > WowActionSlots.MaxAction) spellId = 0;
            SetActionButton(slot, spellId == 0 ? 0 : WowActionSlots.Spell(spellId));
        }

        // ------------------------------------------------------------------ UseAction

        /// <summary>WoW's UseAction for button slot 0..143 (see the file header).</summary>
        public void UseActionButton(int slot, ulong targetGuid)
        {
            if (!WowActionSlots.Valid(slot)) return;
            int packed = _player.ActionButtons[slot];
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Empty:
                    return;
                case ActionKind.Spell:
                    CastSpell(WowActionSlots.SpellOf(packed), targetGuid);
                    return;
                case ActionKind.Item:
                    AbUseItem(WowActionSlots.ItemOf(packed), slot);
                    return;
                case ActionKind.Macro:
                    if (WowActionSlots.MacroRunner != null && WowActionSlots.MacroRunner(WowActionSlots.ActionOf(packed), targetGuid)) return;   // lead 2026-10-12: our macros
                    AbInfo("Macros run in the World of Warcraft client only.");   // ours (their text is client-side account data)
                    return;
                case ActionKind.EquipmentSet:
                    AbInfo("Equipment sets run in the World of Warcraft client only.");   // ours
                    return;
                default:
                    if (_abOddLogged.Add(0x100 + WowActionSlots.TypeOf(packed))) WowLog.Info("Online: action button " + slot + " holds " + AbDescribe(packed) + " - nothing to do with it here (logged once per type)");
                    return;
            }
        }

        /// <summary>
        /// An item button: an EQUIPPED copy with a "Use:" spell is used where it is worn (a trinket); else the first bag stack is used (UseItem:
        /// food, potions, quest items - and an equippable item is equipped, WoW's weapon-swap button); a worn item without a use does nothing
        /// (WoW shows the button's green "equipped" border); none at all: nothing (WoW greys the button).
        /// </summary>
        void AbUseItem(int entry, int slot)
        {
            int worn = -1;
            for (int s = 0; s < _player.Equipment.Length; s++) if (_player.Equipment[s] != null && _player.Equipment[s].Entry == entry) { worn = s; break; }
            ItemProto d;
            bool known = _itemProtos.TryGetValue(entry, out d) && !d.Missing;
            if (worn >= 0 && (!known || d.UseSpell > 0)) { AbUseEquipped(worn, entry); return; }
            var bags = _player.Bags;
            for (int i = 0; i < bags.Count; i++)
            {
                if (bags[i] == null || bags[i].Entry != entry) continue;
                UseItem(i);
                return;
            }
            _abUseLogs++;
            if (_abUseLogs <= 20 || _abUseLogs % 50 == 0)
                WowLog.Info("Online: action button " + slot + " - " + AbDescribe(WowActionSlots.Item(entry)) + (worn >= 0 ? " is worn and has no use" : " is not in the bags or worn") + " - nothing to do");
        }

        /// <summary>CMSG_USE_ITEM with bag 255 + the equipment slot: GetUseableItemByPos (SpellHandler.cpp:81) takes an equipped item; the guid
        /// must match (124-128) - it is our PLAYER_FIELD_INV_SLOT_HEAD + slot * 2 (PlayerStorage.cpp:2747).</summary>
        void AbUseEquipped(int s, int entry)
        {
            var me = _self;
            if (me == null || _world == null) return;
            ulong g = me.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + s * 2);
            if (g == 0) { Error("The item was not found."); return; }                                      // ERR_ITEM_NOT_FOUND (GlobalStrings.lua:3169)
            ItemProto d;
            if (!_itemProtos.TryGetValue(entry, out d)) { AskItem(entry); Error("The item was not found."); return; }
            if (d.Missing) { Error("The item was not found."); return; }
            int spell = d.UseSpell;
            if (spell <= 0) { Error("You can't use that item."); return; }                                // ERR_CANT_USE_ITEM (GlobalStrings.lua:2992)
            SendUseItem(new BagPos { Bag = InvBag0, Slot = (byte)s, Guid = g }, spell, "equipped " + (d.Name.Length > 0 ? d.Name : "item " + entry) + " (equipment slot " + s + ")");
        }

        // ------------------------------------------------------------------ ranks

        /// <summary>A rank upgrade (SMSG_SUPERCEDED_SPELL) replaces the old spell on every button and tells the server (see the file header).</summary>
        void AbReplaceSpell(int oldId, int newId)
        {
            int o = WowActionSlots.Spell(oldId), n = WowActionSlots.Spell(newId);
            if (o == 0 || n == 0 || o == n) return;
            int changed = 0;
            for (int i = 0; i < WowActionSlots.Count; i++)
            {
                if (_player.ActionButtons[i] != o) continue;
                _player.ActionButtons[i] = n;
                changed++;
                if (_world != null) _world.Send(Op.CMSG_SET_ACTION_BUTTON, new PacketWriter().U8(i).U32(unchecked((uint)n)));
            }
            if (changed == 0) return;
            AbSyncBar();
            WowLog.Info("Online: rank swap " + oldId + " -> " + newId + " '" + SpellName(newId) + "' on " + changed + " action button(s), each told to the server (CMSG_SET_ACTION_BUTTON; AzerothCore only tells the client, Player.cpp:3159-3184)");
        }
    }
}
