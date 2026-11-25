// iteration 11 - owner pets (proposal-pets.md 6.4): the data the VR radial menus (vrcast) and the keys need for the pet row and the
// vehicle / possess row, and the ONE way every UI entry point uses a control-bar slot (a NeedsPlace slot opens spellfx's ground circle).
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>ControlBar slot indices of a radial row: -1 empty, -2 Leave / Cancel, -3 previous seat, -4 next seat.</summary>
    public sealed class UiControlRow
    {
        public string Name;
        public int[] Right = new int[6];
        public int[] Left = new int[6];
    }

    public static class UiPetRows
    {
        public const int Empty = -1, Leave = -2, PrevSeat = -3, NextSeat = -4;
        // UI-Vehicles-Button-Exit-Up (VehicleMenuBar.lua:40-50); the seat arrows are ours (the page arrows of the action bar).
        public const string LeaveIcon = @"Interface\Vehicles\UI-Vehicles-Button-Exit-Up";
        public const string PrevIcon = @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Up", NextIcon = @"Interface\Buttons\UI-SpellbookIcon-NextPage-Up";

        static readonly UiControlRow _pet = new UiControlRow(), _veh = new UiControlRow();

        /// <summary>A pet / charm bar: "Pet Bar" (ours), right hand = slots 0-5, left hand = 6-9.</summary>
        public static bool PetRow(IWowSession s, out UiControlRow row)
        {
            row = null;
            ControlBarView v = s != null && s.Player != null ? s.Player.ControlBar : null;
            if (v == null || (v.Kind != ControlBarKind.Pet && v.Kind != ControlBarKind.Charm)) return false;
            _pet.Name = "Pet Bar";   // ours
            for (int i = 0; i < 6; i++) { _pet.Right[i] = SlotCode(v, i); _pet.Left[i] = i < 4 ? SlotCode(v, 6 + i) : Empty; }
            row = _pet;
            return true;
        }

        /// <summary>A vehicle / possess bar, or a passenger seat (Leave / seats only). Row 0 shows it instead of the main row.</summary>
        public static bool VehicleRow(IWowSession s, out UiControlRow row)
        {
            row = null;
            PlayerState p = s != null ? s.Player : null;
            if (p == null) return false;
            ControlBarView v = p.ControlBar;
            bool bar = v != null && (v.Kind == ControlBarKind.Vehicle || v.Kind == ControlBarKind.Possess);
            VehicleView veh = p.Vehicle;
            if (!bar && veh == null) return false;
            _veh.Name = veh != null && !string.IsNullOrEmpty(veh.Name) ? veh.Name : (s.Find(v != null ? v.UnitGuid : 0) != null ? s.Find(v.UnitGuid).Name : "Vehicle");
            for (int i = 0; i < 6; i++) { _veh.Right[i] = bar ? SlotCode(v, i) : Empty; _veh.Left[i] = Empty; }
            int k = 0;
            if (bar && v.Kind == ControlBarKind.Possess) { for (int i = 6; i < 10; i++) _veh.Left[k++] = SlotCode(v, i); _veh.Left[k++] = Leave; }
            else
            {
                if (veh == null || veh.CanLeave) _veh.Left[k++] = Leave;
                if (veh != null && veh.CanSwitchSeat) { _veh.Left[k++] = PrevSeat; _veh.Left[k++] = NextSeat; }
            }
            row = _veh;
            return true;
        }

        static int SlotCode(ControlBarView v, int i)
        {
            ControlSlotView s = v.Slots[i];
            return s != null && s.Kind != ControlSlotKind.Empty ? i : Empty;
        }

        static ControlSlotView SlotOf(IWowSession s, int code)
        {
            ControlBarView v = s != null && s.Player != null ? s.Player.ControlBar : null;
            return v != null && code >= 0 && code < 10 ? v.Slots[code] : null;
        }

        /// <summary>Uses a row code: a slot (NeedsPlace -> the ground circle first), Leave / Cancel, or a seat change.</summary>
        public static void Use(IWowSession s, int code, ulong target)
        {
            if (s == null) return;
            if (code == Leave) { s.LeaveVehicle(); return; }
            if (code == PrevSeat) { s.ChangeSeat(-1); return; }
            if (code == NextSeat) { s.ChangeSeat(-2); return; }
            ControlSlotView v = SlotOf(s, code);
            if (v == null || v.Kind == ControlSlotKind.Empty) return;
            if (v.Kind == ControlSlotKind.Spell && v.NeedsPlace) { BeginPlace(s, v, code, -1); return; }
            s.UseControlSlot(code, target);
        }

        /// <summary>A place spell of the controlled unit: spellfx's circle (UiGroundTarget.BeginCustom), then UseControlSlotAt
        /// (slot >= 0); a book spell without a slot goes to UsePetSpell (the session casts it at the current target's feet).</summary>
        public static void BeginPlace(IWowSession s, ControlSlotView v, int slot, int bookSpell)
        {
            SpellDb db = UiArt.Files != null ? SpellDb.Get(UiArt.Files) : null;
            SpellInfo info = db != null ? db.Find(v.Action) : null;
            if (info == null || slot < 0) { if (slot >= 0) s.UseControlSlot(slot, 0); else s.UsePetSpell(bookSpell, 0); return; }
            SpellTargetingView view = SpellTargeting.NewView(info, SpellTargetingKind.Ground, false, false, 0, 0f, 0, 0f);
            view.RangeMin = 0f;
            view.RangeMax = 0f;   // ours: the circle cannot judge a range measured from the pet - the session does (UseControlSlotAt)
            ulong unit = s.Player.ControlBar != null ? s.Player.ControlBar.UnitGuid : 0;
            int k = slot;
            UiGroundTarget.BeginCustom(view, dest => s.UseControlSlotAt(k, dest), null, () =>
            {
                EntityState e = s.Find(unit);
                return e != null ? e.Pos : s.Player.Pos;
            });
        }

        public static string Icon(IWowSession s, int code)
        {
            if (code == Leave) return LeaveIcon;
            if (code == PrevSeat) return PrevIcon;
            if (code == NextSeat) return NextIcon;
            ControlSlotView v = SlotOf(s, code);
            return v != null && v.Kind != ControlSlotKind.Empty ? v.IconPath : null;
        }

        public static string Tip(IWowSession s, int code)
        {
            if (code == Leave)
            {
                ControlBarView b = s != null && s.Player != null ? s.Player.ControlBar : null;
                return b != null && b.Kind == ControlBarKind.Possess ? "Cancel" : "Leave Vehicle";   // CANCEL (GS 1338) / LEAVE_VEHICLE (GS 4701)
            }
            if (code == PrevSeat) return "Previous seat";   // ours
            if (code == NextSeat) return "Next seat";       // ours
            ControlSlotView v = SlotOf(s, code);
            if (v == null || v.Kind == ControlSlotKind.Empty) return null;
            string t = v.Tooltip ?? v.Name;
            if (v.Autocastable) t += v.Autocast ? "\nAutocast on" : "\nAutocast off";   // ours
            if (!v.Usable && !string.IsNullOrEmpty(v.UnusableReason)) t += "\n" + v.UnusableReason;
            return t;
        }
    }
}
