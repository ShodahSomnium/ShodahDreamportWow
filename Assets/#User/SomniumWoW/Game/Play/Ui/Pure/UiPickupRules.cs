// iteration 11 - owner uicore (proposal-uicore.md 5.2.6 / 7.1): the pure decisions of UiPickup, harness-tested by probe UicDestroyFlow
// (P/harness/Probes/I11/uicore). Pure C#: no UnityEngine.
using System;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure
{
    public static class UiPickupRules
    {
        // UiPickupKind values (Game/Play/Ui/UiPickup.cs) - repeated here so this file stays pure.
        public const int KindNone = 0, KindSpell = 1, KindItem = 2, KindAction = 3, KindMount = 4, KindMoney = 5, KindMacro = 6,
            KindMerchantItem = 7, KindBuybackItem = 8;

        public const string DeleteItem = "DELETE_ITEM", DeleteGoodItem = "DELETE_GOOD_ITEM";

        /// <summary>UIParent.lua:609-616 (DELETE_ITEM_CONFIRM): quality (arg2) &gt;= 3 -> DELETE_GOOD_ITEM (type DELETE), else DELETE_ITEM.</summary>
        public const int GoodQuality = 3;

        /// <summary>
        /// What a drop on the world means (5.2.6): "trade" when a DropOnUnit consumer took it (interact: give to a player); for an item
        /// the popup to ask ("DELETE_ITEM" / "DELETE_GOOD_ITEM"); for everything else the reason the cursor is cleared with
        /// ("dropped" - a merchant / buyback row, a spell, a mount, money, a macro; "removed" - an action, its slot was emptied at pickup).
        /// </summary>
        public static string WorldDrop(int kind, int quality, bool consumedByUnit)
        {
            if (consumedByUnit) return "trade";
            switch (kind)
            {
                case KindItem: return quality >= GoodQuality ? DeleteGoodItem : DeleteItem;
                case KindAction: return "removed";
                case KindNone: return "nothing";
                default: return "dropped";
            }
        }

        /// <summary>DELETE_GOOD_ITEM's button 1 (StaticPopup.lua:1608-1622): enabled when strupper(text) == DELETE_ITEM_CONFIRM_STRING.</summary>
        public static bool DeleteConfirmOk(string typed, string confirmString)
        {
            if (typed == null || string.IsNullOrEmpty(confirmString)) return false;
            return string.Equals(typed.ToUpperInvariant(), confirmString.ToUpperInvariant(), StringComparison.Ordinal);
        }

        /// <summary>The item text of the delete popup's %s (ours - gap G4: the real client formats an item link in its C code): the name
        /// in its quality colour (TMP rich text) and " x&lt;count&gt;" for more than one.</summary>
        public static string DeleteName(string name, string qualityHex, int count)
        {
            string n = string.IsNullOrEmpty(name) ? "?" : name;
            string s = string.IsNullOrEmpty(qualityHex) ? n : "<color=" + qualityHex + ">" + n + "</color>";
            return count > 1 ? s + " x" + count : s;
        }

        /// <summary>VR drag start (5.2.4, ours): the hit point moved more than threshold canvas units from the press point, or left the button.</summary>
        public static bool VrDragStarts(float movedUnits, bool leftButton, float threshold = 14f)
        {
            return leftButton || movedUnits > threshold;
        }

        /// <summary>VR long press (npc's request / the split hold, ours): held at least seconds without starting a drag.</summary>
        public static bool VrLongPress(float heldSeconds, bool dragging, float seconds = 0.5f)
        {
            return !dragging && heldSeconds >= seconds;
        }

        /// <summary>WoW's plain click on the world (the rule WowEntityViews.DesktopClickFallback uses): released within 0.4 s and 6 px of the press (strictly).</summary>
        public static bool PlainClick(float heldSeconds, float movedPixels)
        {
            return heldSeconds < 0.4f && movedPixels < 6f;
        }
    }
}
