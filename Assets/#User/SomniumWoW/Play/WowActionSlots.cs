// iteration 11 - owner vrcast (wave-0 CONTRACT, w0play: pasted from proposal-vrcast.md s5.1 verbatim; frozen numbering - the owner may add helpers)
namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    /// <summary>What an action button holds (AzerothCore ActionButtonType, Player.h:220-228).</summary>
    public enum ActionKind : byte { Empty = 0, Spell = 1, Item = 2, Macro = 3, EquipmentSet = 4, Other = 5 }

    /// <summary>
    /// Iteration 11 (vrcast): WoW's 144 action buttons - numbering, pages, packing, the VR rows. Pure C#. A packed value is exactly what
    /// SMSG_ACTION_BUTTONS / CMSG_SET_ACTION_BUTTON carry: action | type &lt;&lt; 24 (Player.h:235-236). An ITEM value (type 0x80) is NEGATIVE
    /// as an int: always test packed != 0, never packed &gt; 0. Slot = WoW's action number - 1 (ActionButton.lua:149).
    /// </summary>
    public static class WowActionSlots
    {
        public const int Count = 144;              // MAX_ACTION_BUTTONS (Player.h:261)
        public const int PerBar = 12;              // NUM_ACTIONBAR_BUTTONS (ActionButton.lua:3)
        public const int Pages = 6;                // NUM_ACTIONBAR_PAGES (ActionButton.lua:2)
        public const int PageRight = 3, PageRight2 = 4, PageBottomRight = 5, PageBottomLeft = 6;   // ActionButton.lua:6-9, MultiActionBars.xml:41/159/277/395
        public const int TypeSpell = 0x00, TypeClick = 0x01, TypeEquipSet = 0x20, TypeMacro = 0x40, TypeClickMacro = 0x41, TypeItem = 0x80;   // Player.h:220-228
        public const int MaxAction = 0x00FFFFFF;   // ACTION_BUTTON_ACTION (Player.h:235)
        public const int MaxBonusBar = 4;          // SpellShapeshiftForm field 1: highest value measured (Moonkin 4, probe VcForms)
        public const int MinRow = -2, MaxRow = 2;  // VR rows (proposal-vrcast s1.2): Right Bar 2, Right Bar, main, Bottom Left, Bottom Right

        public static int Pack(int type, int action)
        {
            if (action <= 0 || action > MaxAction || type < 0 || type > 0xFF) return 0;
            return unchecked((int)(((uint)type << 24) | (uint)action));
        }
        public static int Spell(int spellId) { return Pack(TypeSpell, spellId); }
        public static int Item(int itemEntry) { return Pack(TypeItem, itemEntry); }
        public static int TypeOf(int packed) { return (int)((uint)packed >> 24); }
        public static int ActionOf(int packed) { return packed & MaxAction; }

        /// <summary>Lead 2026-10-12: a macro button's runner (the UI's UiMacros.Run: macro id, target; true = handled) - the sessions call it.</summary>
        public static System.Func<int, ulong, bool> MacroRunner;
        public static ActionKind KindOf(int packed)
        {
            if (packed == 0) return ActionKind.Empty;
            switch (TypeOf(packed))
            {
                case TypeSpell: return ActionKind.Spell;
                case TypeItem: return ActionKind.Item;
                case TypeMacro: case TypeClickMacro: return ActionKind.Macro;
                case TypeEquipSet: return ActionKind.EquipmentSet;
                default: return ActionKind.Other;
            }
        }
        /// <summary>The spell id of a spell button, else 0.</summary>
        public static int SpellOf(int packed) { return packed != 0 && TypeOf(packed) == TypeSpell ? ActionOf(packed) : 0; }
        /// <summary>The item entry of an item button, else 0.</summary>
        public static int ItemOf(int packed) { return packed != 0 && TypeOf(packed) == TypeItem ? ActionOf(packed) : 0; }
        public static bool Valid(int slot) { return slot >= 0 && slot < Count; }
        /// <summary>0-based slot of button (0..11) on page (1..12).</summary>
        public static int Slot(int page, int button) { return (page - 1) * PerBar + button; }
        public static int PageOf(int slot) { return slot / PerBar + 1; }
        public static int ButtonOf(int slot) { return slot % PerBar + 1; }
        /// <summary>The page the main bar shows (ActionButton.lua:135-149): page 1 in a form whose bonus bar (SpellShapeshiftForm field 1) is
        /// 1..4 shows page 6 + bonus; any other page shows itself.</summary>
        public static int MainPage(int page, int bonusBar)
        {
            if (page < 1 || page > Pages) page = 1;
            return page == 1 && bonusBar > 0 && bonusBar <= MaxBonusBar ? Pages + bonusBar : page;
        }
        /// <summary>VR row -2..+2 -> its page; row 0 = the main bar's page now (mainPage = MainPage(...)).</summary>
        public static int RowPage(int row, int mainPage)
        {
            switch (row)
            {
                case 1: return PageBottomLeft;
                case 2: return PageBottomRight;
                case -1: return PageRight;
                case -2: return PageRight2;
                default: return mainPage;
            }
        }
        /// <summary>Slot of radial segment seg (0..5) of a hand on a VR row: the right hand holds buttons 1-6 of the bar, the left 7-12.</summary>
        public static int RowSlot(int row, bool rightHand, int seg, int mainPage) { return Slot(RowPage(row, mainPage), (rightHand ? 0 : 6) + seg); }
        /// <summary>The bar a slot belongs to, in WoW's words (GlobalStrings.lua 6282-6285); "Action Bar" / "Action Bar page N" are ours.</summary>
        public static string BarName(int slot)
        {
            int page = PageOf(slot);
            switch (page)
            {
                case PageBottomLeft: return "Bottom Left Bar";
                case PageBottomRight: return "Bottom Right Bar";
                case PageRight: return "Right Bar";
                case PageRight2: return "Right Bar 2";
                case 1: return "Action Bar";
                default: return "Action Bar page " + page;
            }
        }
        /// <summary>VR row names (row -2..+2), the same words.</summary>
        public static string RowName(int row) { return row == 0 ? "Action Bar" : BarName(Slot(RowPage(row, 1), 0)); }
    }
}
