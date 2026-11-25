// iteration 11 - owner vrcast (proposal-vrcast.md s7): WoW's 144 action buttons offline.
// The record keeps all 144 packed buttons (CharacterRecord.ActionButtons, key "slots"; old saves migrate 0..11 from "bar") and the session
// loads / saves them (OfflineSession.Stats.cs / OfflineSession.cs, wave 0). Here: SetActionButton / SetActionBarSlot (any of the 144),
// UseActionButton (spells, bag items; worn items, macros and equipment sets answer with an Info), the offline world's own help PlaceOnBar on
// the bar the player SEES, the rank swap on all 144, and the first fill of a form's bar (AbFormBarFirstFill: online the server's
// playercreateinfo_action gives warriors / druids their form bars; offline characters only had 0..11, and a warrior stands in Battle Stance
// from login - without the fill the main bar would show an empty stance bar).
using System;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        private int _abLogs, _abLastForm = -1;

        /// <summary>ActionBar[0..11] = the spell ids of ActionButtons[0..11] (older readers of PlayerState.ActionBar).</summary>
        private void AbSyncBar()
        {
            for (int k = 0; k < _player.ActionBar.Length && k < WowActionSlots.Count; k++) _player.ActionBar[k] = WowActionSlots.SpellOf(_player.ActionButtons[k]);
        }

        /// <summary>One button, no checks (internal fills); keeps ActionBar in step.</summary>
        private void AbSet(int slot, int packed)
        {
            if (!WowActionSlots.Valid(slot)) return;
            _player.ActionButtons[slot] = packed;
            if (slot < _player.ActionBar.Length) _player.ActionBar[slot] = WowActionSlots.SpellOf(packed);
        }

        /// <summary>
        /// Puts a packed action on button slot 0..143 (0 clears it). A spell must be known (or a known mount) - else "You don't know that
        /// spell" (the offline text since iteration 3); an item any entry (AzerothCore checks only that the template exists, Player.cpp:5777-5783);
        /// macros / equipment sets never come from the offline UI.
        /// </summary>
        public void SetActionButton(int slot, int packed)
        {
            if (!WowActionSlots.Valid(slot)) return;
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Empty:
                case ActionKind.Item:
                    break;
                case ActionKind.Spell:
                    int id = WowActionSlots.SpellOf(packed);
                    if (!_rec.Spells.Contains(id) && !KnowsMount(id)) { EmitError("You don't know that spell"); return; }   // iteration 7: known mounts too
                    break;
                default:
                    EmitError("Not available offline");   // ours
                    return;
            }
            AbSet(slot, packed);
            _abLogs++;
            if (_abLogs <= 30 || _abLogs % 50 == 0)
                WowLog.Info("Offline: action button " + slot + " (" + WowActionSlots.BarName(slot) + " " + WowActionSlots.ButtonOf(slot) + ") = " + AbDescribe(packed));
        }

        /// <summary>A spell on button slot 0..143 (0 clears it) = SetActionButton(slot, WowActionSlots.Spell(spellId)).</summary>
        public void SetActionBarSlot(int slot, int spellId)
        {
            if (spellId < 0 || spellId > WowActionSlots.MaxAction) spellId = 0;
            SetActionButton(slot, spellId == 0 ? 0 : WowActionSlots.Spell(spellId));
        }

        /// <summary>WoW's UseAction for button slot 0..143: a spell is cast; an item -> the first bag stack (UseItem: potions, food, an item to
        /// equip); only worn -> an Info (offline items with a "Use:" effect do not exist); macros / equipment sets -> the online Info texts.</summary>
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
                    int entry = WowActionSlots.ItemOf(packed);
                    for (int i = 0; i < _rec.Bags.Count; i++)
                        if (_rec.Bags[i] != null && _rec.Bags[i].Entry == entry) { UseItem(i); return; }
                    for (int e = 0; e < _rec.Equipment.Length; e++)
                        if (_rec.Equipment[e] == entry) { EmitInfo("Using equipped items is not available offline."); return; }   // ours
                    return;   // none left: nothing (WoW greys the button)
                case ActionKind.Macro:
                    if (WowActionSlots.MacroRunner != null && WowActionSlots.MacroRunner(WowActionSlots.ActionOf(packed), 0UL)) return;   // lead 2026-10-12: our macros
                    EmitInfo("Macros run in the World of Warcraft client only.");
                    return;
                case ActionKind.EquipmentSet:
                    EmitInfo("Equipment sets run in the World of Warcraft client only.");
                    return;
            }
        }

        /// <summary>
        /// The offline world's own help (WoW 3.3.5a never places a learned spell itself): a new spell that is on no button goes to the first
        /// empty button of the bar the player SEES now - the main bar's page 1, or the form's bar (WowActionSlots.MainPage, SpellShapeshiftForm
        /// field 1) - so a warrior in Battle Stance gets Rend on 72-83, not on the hidden 0-11.
        /// </summary>
        private void PlaceOnBar(int spellId)
        {
            int packed = WowActionSlots.Spell(spellId);
            if (packed == 0 || Array.IndexOf(_player.ActionButtons, packed) >= 0) return;
            int first = WowActionSlots.Slot(WowActionSlots.MainPage(1, AbBonusBar(_form)), 0);
            for (int b = 0; b < WowActionSlots.PerBar; b++)
                if (_player.ActionButtons[first + b] == 0) { AbSet(first + b, packed); return; }
        }

        /// <summary>A rank upgrade replaces the old spell on every button (OfflineSession.Stats.cs LearnSpell; it used the 12 before).</summary>
        private void AbReplaceSpell(int oldId, int newId)
        {
            int o = WowActionSlots.Spell(oldId), n = WowActionSlots.Spell(newId);
            if (o == 0) return;
            for (int i = 0; i < WowActionSlots.Count; i++) if (_player.ActionButtons[i] == o) AbSet(i, n);
        }

        /// <summary>SpellShapeshiftForm field 1 of a form (its action bar page offset 1..4, 0 none; measured, proposal-vrcast s4.3).</summary>
        private int AbBonusBar(int form)
        {
            if (form <= 0) return 0;
            var f = _db.Form(form);
            return f != null && f.BonusActionBar > 0 && f.BonusActionBar <= WowActionSlots.MaxBonusBar ? f.BonusActionBar : 0;
        }

        /// <summary>
        /// Called where PlayerState.ShapeshiftForm is written (npc's OfflineSession.Items.cs, hook) and, as a safety net, by the UI's UiBars.Tick
        /// through AbFormBarCheck: the first time a form with a bonus bar is entered and that bar (72 + (b-1)*12 .. +11) is empty, the main bar's
        /// 0..11 are copied into it once (bit b-1 of CharacterRecord.FormBarsFilled, saved). Cheap: returns at once while the form is unchanged.
        /// </summary>
        partial void AbFormBarFirstFill()
        {
            if (_form == _abLastForm) return;
            _abLastForm = _form;
            int b = AbBonusBar(_form);
            if (b <= 0 || (_rec.FormBarsFilled & (1 << (b - 1))) != 0) return;
            int first = WowActionSlots.Slot(WowActionSlots.Pages + b, 0);
            for (int k = 0; k < WowActionSlots.PerBar; k++) if (_player.ActionButtons[first + k] != 0) { _rec.FormBarsFilled |= 1 << (b - 1); return; }
            int n = 0;
            for (int k = 0; k < WowActionSlots.PerBar; k++)
            {
                int v = _player.ActionButtons[k];
                if (v == 0) continue;
                AbSet(first + k, v);
                n++;
            }
            _rec.FormBarsFilled |= 1 << (b - 1);
            var f = _db.Form(_form);
            WowLog.Info("Offline: form bar " + b + " (" + (f != null && f.Name.Length > 0 ? f.Name : "form " + _form) + ") filled from the main bar (" + n + " slots, slots " + first + "-" + (first + 11) + ")");
        }

        /// <summary>The UI's per-frame safety net for AbFormBarFirstFill (UiBars.Tick; main thread, the session's own thread).</summary>
        internal void AbFormBarCheck()
        {
            AbFormBarFirstFill();
        }

        private string AbDescribe(int packed)
        {
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Empty: return "empty";
                case ActionKind.Spell: { var s = _db.Find(WowActionSlots.SpellOf(packed)); return WowActionSlots.SpellOf(packed) + (s != null ? " '" + s.Name + "'" : ""); }
                case ActionKind.Item: { var d = FindItemDef(WowActionSlots.ItemOf(packed)); return "item " + WowActionSlots.ItemOf(packed) + (d != null ? " '" + d.Name + "'" : ""); }
                default: return "type 0x" + WowActionSlots.TypeOf(packed).ToString("X2") + " " + WowActionSlots.ActionOf(packed);
            }
        }
    }
}
