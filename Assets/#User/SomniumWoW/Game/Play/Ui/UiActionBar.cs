// iteration 11 (w0game, wave 0): moved verbatim from UiHud.cs (PLAN11 s3) - owner vrcast from wave 1 on.
// iteration 11 (vrcast, proposal-vrcast.md s6.4 + uicore H13): one button shows any of WoW's 144 action slots (Action, set by its bar every
// frame: the main bar follows the page and the form's bonus bar) - spells, items (count, the green "equipped" border), macros; empty buttons of
// the extra bars hide unless the grid shows (something on the cursor, or Always Show ActionBars); drag a button off (Shift, or with Lock
// ActionBars off) / drop anything placeable on it through uicore's UiPickup (UiKit.Draggable / UiKit.DropTarget); click = UseAction, or
// place what is on the cursor.
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// One action button (ActionButtonTemplate, ActionButton.lua): the action's icon, UI-Quickslot2 frame (UI-Quickslot at alpha 0.5 for an
    /// empty slot while the grid shows, ActionButton.lua:249-254 / 279), cooldown sweep, the usable tint (ActionButton.lua:317-326: usable white,
    /// not enough power (0.5, 0.5, 1), unusable 0.4 grey; out of range red as the task asks and the hot key red as ActionButton.lua:471 does),
    /// CheckButtonHilight while the ability is active, the red flash while auto-attacking, the cost in the power colour (spells) or the count
    /// (consumable / stackable items), UI-ActionButton-Border green for a worn item (ActionButton.lua:227-234). Tap = UseAction; with something on
    /// the cursor tap = put it here.
    /// </summary>
    public sealed class UiActionSlot
    {
        public const int AutoAttack = 6603;
        public const string EmptyFrame = @"Interface\Buttons\UI-Quickslot.blp";            // ActionButton.lua:249-254 (an empty slot's NormalTexture)
        public const string EquippedBorder = @"Interface\Buttons\UI-ActionButton-Border.blp";  // ActionButtonTemplate.xml:48-58
        public UiButton B;
        /// <summary>The 0-based action slot shown (0..143); the bar sets it every frame.</summary>
        public int Action;
        /// <summary>Extra bars: an empty button is hidden unless the grid shows.</summary>
        public bool HideWhenEmpty;
        private readonly UiContext _ctx;
        private readonly UiActionView _view = new UiActionView();
        private readonly RawImage _empty, _border;
        private readonly TextMeshProUGUI _name;
        private int _shownPacked = int.MinValue, _tint = -1, _countShown = int.MinValue, _countKind = -1;
        private string _iconPath, _nameShown;
        private bool _iconLoaded, _active, _pickGlow, _flashOn, _hotRed, _equipped, _gridFrame, _visible = true;
        private float _sweep = -1f;

        public UiActionSlot(Transform parent, UiContext ctx, int slot, float x, float y, string hotkey)
        {
            _ctx = ctx;
            Action = slot;
            _empty = UiKit.Solid(parent, "Action " + (slot + 1) + " empty", new Color(0f, 0f, 0f, 0.45f), x, y, 36f, 36f);
            B = UiKit.Slot(parent, "Action " + (slot + 1), x, y, 36f, true);
            B.Checked = UiKit.Pic(B.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 36f, 36f);
            B.Checked.enabled = false;
            B.Flash = UiKit.Pic(B.transform, "Flash", UiArt.Tex(UiArt.QuickslotRed), 0f, 0f, 36f, 36f);
            B.Flash.enabled = false;
            // Cooldown sweep: a radial fill darkening what is left (CooldownFrameTemplate draws a clock wipe).
            var sweepRt = UiKit.Rect(B.transform, "Cooldown", 0f, 0f, 36f, 36f);
            B.Sweep = sweepRt.gameObject.AddComponent<Image>();
            B.Sweep.sprite = UiArt.WhiteSprite;
            B.Sweep.type = Image.Type.Filled;
            B.Sweep.fillMethod = Image.FillMethod.Radial360;
            B.Sweep.fillOrigin = (int)Image.Origin360.Top;
            B.Sweep.fillClockwise = false;
            B.Sweep.color = new Color(0f, 0f, 0f, 0.7f);
            B.Sweep.raycastTarget = false;
            if (UiKit.Over) B.Sweep.material = UiArt.OverUi;
            B.Sweep.enabled = false;
            // Border (UI-ActionButton-Border 62x62 centred, ADD; green 0.35 for a worn item).
            _border = UiKit.Pic(B.transform, "Border", UiArt.Add(EquippedBorder), -13f, -13f, 62f, 62f);
            _border.color = new Color(0f, 1f, 0f, 0.35f);
            _border.enabled = false;
            // NumberFontNormalSmallGray hot key (top right), NumberFontNormal count (bottom right), the macro name (BOTTOM (0, 2)).
            B.HotKey = UiKit.Text(B.transform, "HotKey", hotkey ?? "", 11f, new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.TopRight, -2f, 1f, 36f, 12f, UiArt.Numbers);
            B.Count = UiKit.Text(B.transform, "Count", "", 12f, Color.white, TextAlignmentOptions.BottomRight, 0f, 22f, 34f, 13f, UiArt.Numbers);
            _name = UiKit.Text(B.transform, "Name", "", 9f, Color.white, TextAlignmentOptions.Bottom, 0f, 24f, 36f, 10f);
            B.Click = OnClick;
            B.RightClick = OnClick;
            B.Tip = Tip;
            // uicore H13: drag a button off (WoW's PickupAction; locked bars need Shift) / drop anything placeable on it.
            UiKit.Draggable(B, DragPayload, false);
            UiKit.DropTarget(B, pl => pl != null && pl.Placeable, pl => UiActionCursor.PlaceOn(_ctx.S, Action, _ctx.Vr), DropHint);
        }

        private static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>WoW's PickupAction: locked bars (ActionBarFrame.xml:18-24) pick up only with PICKUPACTION = SHIFT (Bindings.xml:1334); the
        /// button is emptied at once, Restore puts it back when the pick is cancelled.</summary>
        private UiPickupPayload DragPayload()
        {
            IWowSession s = _ctx.S;
            if (s == null || s.Player == null || !WowActionSlots.Valid(Action)) return null;
            if (WowSettings.LockActionBars && !ShiftHeld()) return null;
            int slot = Action;
            int packed = s.Player.ActionButtons[slot];
            if (packed == 0) return null;
            UiActions.Resolve(s.Player, packed, _view);
            var pl = new UiPickupPayload
            {
                Kind = UiPickupKind.Action,
                Packed = packed,
                SpellId = WowActionSlots.SpellOf(packed),
                ItemEntry = WowActionSlots.ItemOf(packed),
                FromAction = slot,
                Name = _view.Name,
                IconPath = _view.IconPath,
            };
            pl.Restore = () => { if (s.Player != null && s.Player.ActionButtons[slot] == 0) s.SetActionButton(slot, packed); };
            s.SetActionButton(slot, 0);
            return pl;
        }

        private string DropHint(UiPickupPayload pl)
        {
            if (pl == null || !pl.Placeable) return null;
            return pl.Packed == 0 ? "Clear this button" : "Place " + (pl.Name ?? "") + " here";
        }

        private void OnClick()
        {
            IWowSession s = _ctx.S;
            if (s == null) return;
            if (UiActionCursor.Active) { UiActionCursor.PlaceOn(s, Action, _ctx.Vr); return; }
            s.UseActionButton(Action, 0);
        }

        private string Tip()
        {
            IWowSession s = _ctx.S;
            if (s == null || s.Player == null || !WowActionSlots.Valid(Action)) return null;
            string verb = _ctx.Vr ? "Tap" : "Click";
            string pick = UiActionCursor.Active ? "<color=#00ff00>" + (UiActionCursor.Packed == 0 ? verb + " to clear this button" : verb + " to place " + UiActionCursor.Name + " here") + "</color>" : null;
            int packed = s.Player.ActionButtons[Action];
            if (packed == 0) return pick ?? (HideWhenEmpty || _ctx.Vr ? null : "<color=#808080>" + WowActionSlots.BarName(Action) + " button " + WowActionSlots.ButtonOf(Action) + " - empty</color>");
            return UiActions.Tooltip(s.Player, packed, pick);
        }

        public void Refresh(PlayerState p, float now)
        {
            int packed = p != null && p.ActionButtons != null && WowActionSlots.Valid(Action) ? p.ActionButtons[Action] : 0;
            UiActions.Resolve(p, packed, _view);
            bool has = _view.Has;
            bool cursor = UiActionCursor.Active;
            bool grid = cursor || (HideWhenEmpty && WowSettings.AlwaysShowActionBars);
            bool visible = !HideWhenEmpty || has || grid;
            if (visible != _visible) { _visible = visible; B.gameObject.SetActive(visible); }
            bool darkEmpty = !has && !HideWhenEmpty;
            if (_empty.enabled != darkEmpty) _empty.enabled = darkEmpty;
            if (!visible) return;

            if (packed != _shownPacked)
            {
                _shownPacked = packed;
                _iconPath = null;
                _iconLoaded = false;
                B.Icon.enabled = false;
            }
            string path = has ? _view.IconPath : null;
            if (!ReferenceEquals(path, _iconPath) && path != _iconPath) { _iconPath = path; _iconLoaded = false; B.Icon.enabled = false; }
            if (!_iconLoaded && has)
            {
                Texture2D t = UiArt.Icon(path);
                if (t != null) { B.Icon.texture = t; B.Icon.color = Color.white; B.Icon.enabled = true; _iconLoaded = true; _tint = -1; }
            }

            // The slot frame: UI-Quickslot2 with an action; an empty extra-bar slot shows UI-Quickslot at alpha 0.5 while the grid shows.
            bool gridFrame = !has && HideWhenEmpty;
            if (gridFrame != _gridFrame && B.Frame != null)
            {
                _gridFrame = gridFrame;
                B.Frame.texture = UiArt.Tex(gridFrame ? EmptyFrame : UiArt.Quickslot);
                B.Frame.color = gridFrame ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
            }

            // Tint.
            int tint = _view.Tint;
            if (tint != _tint && _iconLoaded)
            {
                _tint = tint;
                B.Icon.color = UiActions.TintColor(tint);
            }
            bool hotRed = tint == 3;
            if (hotRed != _hotRed) { _hotRed = hotRed; B.HotKey.color = hotRed ? UiArt.ErrorRed : new Color(0.6f, 0.6f, 0.6f); }

            // Cooldown sweep (spells; items have none - known gap).
            float frac = _view.Cooldown;
            if (Mathf.Abs(frac - _sweep) > 0.004f)
            {
                _sweep = frac;
                B.Sweep.enabled = frac > 0f;
                B.Sweep.fillAmount = frac;
            }

            // Active (stance, form, Stealth, auto attack, queued swing, the ridden mount) and the "choose a slot" glow.
            bool pickGlow = cursor;
            bool active = has && _view.Active || pickGlow;
            if (active != _active || pickGlow != _pickGlow)
            {
                _active = active;
                _pickGlow = pickGlow;
                B.Checked.enabled = active;
                B.Checked.color = pickGlow ? new Color(0.4f, 1f, 0.4f, 1f) : Color.white;
            }
            bool flash = has && _view.SpellId == AutoAttack && _view.Ability != null && _view.Ability.Active && (Mathf.Repeat(now, 0.8f) < 0.4f);
            if (flash != _flashOn) { _flashOn = flash; B.Flash.enabled = flash; }
            if (_view.Equipped != _equipped) { _equipped = _view.Equipped; _border.enabled = _equipped; }

            // Cost (spells, in the power colour) or count (consumable / stackable items).
            int countKind, count;
            if (_view.Kind == ActionKind.Item) { countKind = _view.ShowCount ? 100 : -1; count = _view.ShowCount ? _view.Count : 0; }
            else if (_view.Ability != null && _view.Ability.CostType != PowerType.None && _view.Ability.Cost > 0) { countKind = (int)_view.Ability.CostType; count = _view.Ability.Cost; }
            else { countKind = -1; count = 0; }
            if (count != _countShown || countKind != _countKind)
            {
                _countShown = count;
                _countKind = countKind;
                if (countKind == 100) { B.Count.SetText("{0}", count); B.Count.color = Color.white; }
                else if (countKind >= 0) { B.Count.SetText("{0}", count); B.Count.color = CostColor((PowerType)countKind); }
                else B.Count.SetText("");
            }
            string label = _view.Kind == ActionKind.Macro ? "Macro" : "";
            if (label != _nameShown) { _nameShown = label; _name.text = label; }
        }

        private static Color CostColor(PowerType t)
        {
            switch (t)
            {
                case PowerType.Mana: return new Color(0.45f, 0.65f, 1f);
                case PowerType.Rage: return new Color(1f, 0.35f, 0.35f);
                case PowerType.Energy: return new Color(1f, 1f, 0.3f);
                case PowerType.Focus: return new Color(1f, 0.6f, 0.35f);
                default: return Color.white;
            }
        }
    }

    /// <summary>
    /// 12 action buttons in a row or a column (ActionButton1 BOTTOMLEFT (8, 4), each next +42: ActionBarFrame.xml:41-100; the multi bars the
    /// same with their own first slot, MultiActionBars.xml). A Main bar (the desktop main bar, the VR forearm bar) shows the page WoW's main
    /// bar shows (UiBars.MainPage: Shift+1..6, the arrows, the form's bonus bar); a Fixed bar shows firstSlot .. +11.
    /// </summary>
    public sealed class UiActionBar
    {
        public enum Kind { Main, Fixed }
        public readonly UiActionSlot[] Slots = new UiActionSlot[12];
        public static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
        /// <summary>Ours: the Bottom Left Bar's keys Alt+1..Alt+= (WoW binds no extra bar, DefaultBindings.wtf).</summary>
        public static readonly string[] AltKeys = { "A-1", "A-2", "A-3", "A-4", "A-5", "A-6", "A-7", "A-8", "A-9", "A-0", "A--", "A-=" };
        public readonly Kind BarKind;
        public readonly int FirstSlot;

        public UiActionBar(Transform parent, UiContext ctx, float x, float y, bool hotkeys)
            : this(parent, ctx, x, y, hotkeys, -1, false, null)
        {
        }

        /// <summary>firstSlot -1 = a Main bar; keys null = "1".."=" when hotkeys.</summary>
        public UiActionBar(Transform parent, UiContext ctx, float x, float y, bool hotkeys, int firstSlot, bool vertical, string[] keys)
        {
            BarKind = firstSlot < 0 ? Kind.Main : Kind.Fixed;
            FirstSlot = firstSlot < 0 ? 0 : firstSlot;
            string[] k = hotkeys ? keys ?? Keys : null;
            for (int i = 0; i < 12; i++)
            {
                float bx = vertical ? x : x + i * 42f, by = vertical ? y + i * 42f : y;
                Slots[i] = new UiActionSlot(parent, ctx, FirstSlot + i, bx, by, k != null ? k[i] : "");
                Slots[i].HideWhenEmpty = BarKind == Kind.Fixed;
            }
            if (UiActionCursor.Say == null && ctx != null) UiActionCursor.Say = ctx.Say;
            if (UiKit.Pointable) EnsureRaycaster(parent);
        }

        /// <summary>A nested canvas needs its own GraphicRaycaster for the mouse (WowMapTabletLink.cs:864) - the bars' buttons sit on sub-canvases.</summary>
        public static void EnsureRaycaster(Transform parent)
        {
            if (parent == null) return;
            Canvas c = parent.GetComponentInParent<Canvas>();
            if (c != null && !c.isRootCanvas && c.GetComponent<GraphicRaycaster>() == null)
            {
                c.gameObject.AddComponent<GraphicRaycaster>();
                WowLog.Info("UI: a mouse raycaster on the sub-canvas '" + c.name + "' (its buttons take clicks)");
            }
        }

        public void Refresh(PlayerState p, float now)
        {
            UiBars.Tick(p);
            if (BarKind == Kind.Main)
            {
                int first = (UiBars.MainPage - 1) * WowActionSlots.PerBar;
                for (int i = 0; i < 12; i++) Slots[i].Action = first + i;
            }
            for (int i = 0; i < 12; i++) Slots[i].Refresh(p, now);
        }
    }
}
