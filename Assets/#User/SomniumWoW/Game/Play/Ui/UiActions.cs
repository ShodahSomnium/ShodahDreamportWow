// iteration 11 - owner vrcast (proposal-vrcast.md s6.1): what one of WoW's 144 action buttons holds, resolved for every view that draws it
// (the desktop bars, the VR radial rows, the strike ring, the cursor): icon, name, item count, the usable tint, the cooldown sweep, active,
// equipped (the green border) and the tooltip. Packed values are WowActionSlots' (spell | item 0x80 | macro 0x40 | equipment set 0x20 << 24).
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>One packed action resolved for drawing (UiActions.Resolve fills it; a view keeps one instance per button).</summary>
    public sealed class UiActionView
    {
        public int Packed;
        public ActionKind Kind;
        public int SpellId, ItemEntry;
        public AbilityView Ability;     // a spell from the spellbook / Mounts / Companions / Recipes (null: an item, a macro, or a spell none of them lists)
        public ItemView Item;           // an item: the first bag stack, else the worn copy; null when the player has none
        public int Count;               // items: how many are in the bags
        public bool ShowCount;          // items: WoW shows the count of consumable / stackable items (ActionButton.lua: IsConsumableAction / IsStackableAction)
        public bool Equipped;           // items: one is worn (the green border, ActionButton.lua:227-234)
        public string Name = "", IconPath;
        public int Tint;                // 0 empty, 1 usable (white), 2 not enough power (0.5, 0.5, 1), 3 out of range (red), 4 unusable (0.4 grey)
        public float Cooldown;          // 0..1 of the radial sweep still to go
        public bool Active;             // stance / form / auto attack / a queued swing / the ridden mount (CheckButtonHilight)
        public bool Has { get { return Kind != ActionKind.Empty; } }
    }

    public static class UiActions
    {
        /// <summary>A macro / an equipment set has no icon we can read (their data is client-side account data): WoW's question mark.</summary>
        public const string UnknownIcon = UiArt.QuestionMark;

        // The last name / icon seen for an item entry, so a button keeps its picture (greyed, count 0) after the last one is used up.
        private static readonly Dictionary<int, KeyValuePair<string, string>> SeenItems = new Dictionary<int, KeyValuePair<string, string>>();

        /// <summary>The ability with this spell id: the spellbook, Player.Mounts (UiContext.Find), then the Companions and Recipes lists.</summary>
        public static AbilityView Ability(PlayerState p, int spellId)
        {
            if (p == null || spellId == 0) return null;
            AbilityView v = UiContext.Find(p, spellId);
            if (v != null) return v;
            v = FindIn(p.Companions, spellId);
            return v ?? FindIn(p.Recipes, spellId);
        }

        private static AbilityView FindIn(List<AbilityView> list, int spellId)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].SpellId == spellId) return list[i];
            return null;
        }

        /// <summary>Spell.dbc facts of a spell none of the lists has (a button the server kept): its name and icon.</summary>
        private static SpellInfo DbSpell(int spellId)
        {
            if (spellId <= 0 || WowPlay.Files == null) return null;
            SpellDb db = SpellDb.Get(WowPlay.Files);
            return db != null ? db.Find(spellId) : null;
        }

        /// <summary>The usable tint of an ability (ActionButton.lua:317-326; out of range red as the task asks).</summary>
        public static int TintOf(AbilityView v)
        {
            if (v == null) return 0;
            string why = v.UnusableReason;
            if (v.Usable || why == "Spell is not ready yet") return 1;
            if (why != null && why.StartsWith("Not enough", System.StringComparison.Ordinal)) return 2;
            if (why == "Out of range" || why == "Target too close") return 3;
            return 4;
        }

        public static Color TintColor(int tint)
        {
            return tint == 2 ? new Color(0.5f, 0.5f, 1f) : tint == 3 ? new Color(0.85f, 0.2f, 0.2f) : tint == 4 ? new Color(0.4f, 0.4f, 0.4f) : Color.white;
        }

        /// <summary>The radial sweep still to go (the session's CooldownRemaining includes the global cooldown when longer).</summary>
        public static float CooldownOf(PlayerState p, AbilityView v)
        {
            if (v == null || p == null || v.CooldownRemaining <= 0.02f) return 0f;
            float total;
            if (p.GcdTotal > 0f && Mathf.Abs(v.CooldownRemaining - p.GcdRemaining) < 0.06f) total = p.GcdTotal;
            else total = Mathf.Max(v.Cooldown, v.CooldownRemaining);
            return total > 0f ? Mathf.Clamp01(v.CooldownRemaining / total) : 0f;
        }

        /// <summary>Fills into with what the packed action is right now.</summary>
        public static void Resolve(PlayerState p, int packed, UiActionView into)
        {
            into.Packed = packed;
            into.Kind = WowActionSlots.KindOf(packed);
            into.SpellId = WowActionSlots.SpellOf(packed);
            into.ItemEntry = WowActionSlots.ItemOf(packed);
            into.Ability = null;
            into.Item = null;
            into.Count = 0;
            into.ShowCount = false;
            into.Equipped = false;
            into.Cooldown = 0f;
            into.Active = false;
            into.Tint = 0;
            into.Name = "";
            into.IconPath = null;
            switch (into.Kind)
            {
                case ActionKind.Empty:
                    return;
                case ActionKind.Spell:
                {
                    AbilityView v = Ability(p, into.SpellId);
                    into.Ability = v;
                    if (v != null)
                    {
                        into.Name = v.Name ?? "";
                        into.IconPath = v.IconPath;
                        into.Tint = TintOf(v);
                        into.Cooldown = CooldownOf(p, v);
                        into.Active = UiContext.IsActive(p, v);
                    }
                    else
                    {
                        SpellInfo si = DbSpell(into.SpellId);
                        into.Name = si != null ? si.Name : "Spell " + into.SpellId;
                        into.IconPath = si != null && si.IconPath.Length > 0 ? si.IconPath : UnknownIcon;
                        into.Tint = 1;   // the server decides (WoW draws a known spell white)
                    }
                    return;
                }
                case ActionKind.Item:
                {
                    int entry = into.ItemEntry;
                    if (p != null)
                    {
                        List<ItemView> bags = p.Bags;
                        for (int i = 0; i < bags.Count; i++)
                        {
                            ItemView it = bags[i];
                            if (it == null || it.Entry != entry) continue;
                            if (into.Item == null) into.Item = it;
                            into.Count += Mathf.Max(1, it.Count);
                        }
                        ItemView[] eq = p.Equipment;
                        for (int s = 0; eq != null && s < eq.Length; s++)
                        {
                            if (eq[s] == null || eq[s].Entry != entry) continue;
                            into.Equipped = true;
                            if (into.Item == null) into.Item = eq[s];
                            break;
                        }
                    }
                    ItemView view = into.Item;
                    KeyValuePair<string, string> seen;
                    if (view != null)
                    {
                        into.Name = view.Name ?? "";
                        into.IconPath = view.IconPath;
                        if (!SeenItems.TryGetValue(entry, out seen) || seen.Key != view.Name || seen.Value != view.IconPath)
                            SeenItems[entry] = new KeyValuePair<string, string>(view.Name, view.IconPath);
                    }
                    else if (SeenItems.TryGetValue(entry, out seen)) { into.Name = seen.Key ?? ""; into.IconPath = seen.Value; }
                    else { into.Name = "Item " + entry; into.IconPath = UnknownIcon; }
                    into.ShowCount = !into.Equipped && (view == null || view.MaxStack > 1 || view.ItemClass == 0 || into.Count > 1);
                    into.Tint = into.Count > 0 || into.Equipped ? 1 : 4;
                    return;
                }
                case ActionKind.Macro:
                {
                    // lead 2026-10-12: our macros (UiMacros) - their name and icon
                    UiMacros.Macro m = UiMacros.Get(WowActionSlots.ActionOf(packed));
                    into.Name = m != null ? m.Name : "Macro";   // MACRO (GlobalStrings.lua:4850)
                    into.IconPath = m != null ? UiMacros.IconOf(p, m) : UnknownIcon;
                    into.Tint = 1;
                    return;
                }
                case ActionKind.EquipmentSet:
                    into.Name = "Equipment set";   // ours
                    into.IconPath = UnknownIcon;
                    into.Tint = 1;
                    return;
                default:
                    into.Name = "Unknown action";   // ours
                    into.IconPath = UnknownIcon;
                    into.Tint = 4;
                    return;
            }
        }

        private static readonly UiActionView Scratch = new UiActionView();

        /// <summary>The name of a packed action (main thread).</summary>
        public static string NameOf(PlayerState p, int packed)
        {
            Resolve(p, packed, Scratch);
            return Scratch.Name;
        }

        /// <summary>The icon path of a packed action (main thread); null when empty.</summary>
        public static string IconOf(PlayerState p, int packed)
        {
            Resolve(p, packed, Scratch);
            return Scratch.IconPath;
        }

        /// <summary>The tooltip of a packed action, extra appended (a green "place here" line while something is on the cursor).</summary>
        public static string Tooltip(PlayerState p, int packed, string extra)
        {
            Resolve(p, packed, Scratch);
            switch (Scratch.Kind)
            {
                case ActionKind.Empty:
                    return extra;
                case ActionKind.Spell:
                    if (Scratch.Ability != null) return UiTooltip.Ability(Scratch.Ability, extra);
                    return "<size=14><color=#ffffff>" + Scratch.Name + "</color></size>" + (string.IsNullOrEmpty(extra) ? "" : "\n" + extra);
                case ActionKind.Item:
                    if (Scratch.Item != null) return UiTooltip.Item(Scratch.Item, (Scratch.Equipped ? "<color=#00ff00>Equipped</color>" : null) + (string.IsNullOrEmpty(extra) ? "" : (Scratch.Equipped ? "\n" : "") + extra));
                    return "<size=14><color=#ffffff>" + Scratch.Name + "</color></size>\n<color=#808080>None in your bags</color>" + (string.IsNullOrEmpty(extra) ? "" : "\n" + extra);
                case ActionKind.Macro:
                    return UiMacros.Tooltip(UiMacros.Get(WowActionSlots.ActionOf(packed))) + (string.IsNullOrEmpty(extra) ? "" : "\n" + extra);   // lead 2026-10-12
                case ActionKind.EquipmentSet:
                    return "<size=14><color=#ffffff>Equipment set</color></size>\n<color=#ffd100>Runs in the World of Warcraft client only.</color>" + (string.IsNullOrEmpty(extra) ? "" : "\n" + extra);
                default:
                    return extra;
            }
        }
    }
}
