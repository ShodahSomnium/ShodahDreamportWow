using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>Desktop mouse drag along a slider track (only slider tracks carry this, so no other button takes drags).</summary>
    public sealed class UiDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public UiButton Button;
        public Action Ended;

        public void OnBeginDrag(PointerEventData e) { if (Button != null) Button.DragFromScreen(e); }
        public void OnDrag(PointerEventData e) { if (Button != null) Button.DragFromScreen(e); }
        public void OnEndDrag(PointerEventData e) { if (Ended != null) Ended(); }
    }

    /// <summary>
    /// OptionsCheckButtonTemplate (OptionsPanelTemplates.xml:10-60): 26x26 UI-CheckBox-Up / -Down (pushed) / -Highlight (ADD) /
    /// -Check, the label (GameFontNormal) to its right; igMainMenuOptionCheckBoxOn / Off on click (lines 20/22). The whole row
    /// (box + label) is the button so a VR ray or finger finds it easily.
    /// </summary>
    public sealed class UiCheck
    {
        public const string Up = @"Interface\Buttons\UI-CheckBox-Up.blp";
        public const string Down = @"Interface\Buttons\UI-CheckBox-Down.blp";
        public const string Highlight = @"Interface\Buttons\UI-CheckBox-Highlight.blp";
        public const string Check = @"Interface\Buttons\UI-CheckBox-Check.blp";

        public readonly UiButton B;
        private readonly RawImage _check;
        private readonly Func<bool> _get;
        private int _shown = -1;

        public UiCheck(Transform parent, string label, string hint, float x, float y, float w, Func<bool> get, Action<bool> set, string tip, Action changed)
        {
            _get = get;
            B = UiKit.Button(parent, "Check " + label, x, y, w, 26f);
            B.Art = UiKit.Pic(B.transform, "Up", UiArt.Tex(Up), 0f, 0f, 26f, 26f);
            B.ArtUp = UiArt.Tex(Up);
            B.Pushed = UiKit.Pic(B.transform, "Down", UiArt.Tex(Down), 0f, 0f, 26f, 26f);
            B.Pushed.enabled = false;
            B.Hilite = UiKit.Pic(B.transform, "Highlight", UiArt.Add(Highlight), 0f, 0f, 26f, 26f);
            B.Hilite.enabled = false;
            _check = UiKit.Pic(B.transform, "Check", UiArt.Tex(Check), 0f, 0f, 26f, 26f);
            _check.enabled = false;
            string text = label + (string.IsNullOrEmpty(hint) ? "" : " <color=#9d9d9d>(" + hint + ")</color>");
            TextMeshProUGUI t = UiKit.Text(B.transform, "Text", text, 12f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 27f, 1f, w - 27f, 26f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            string tipText = "<size=14>" + label + "</size>" + (string.IsNullOrEmpty(tip) ? "" : "\n" + tip);
            B.Tip = () => tipText;
            B.Click = () =>
            {
                bool v = !_get();
                set(v);
                UiSound.Named(v ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
                if (changed != null) changed();
                Refresh();
            };
        }

        public void Refresh()
        {
            int v = _get() ? 1 : 0;
            if (v == _shown) return;
            _shown = v;
            _check.enabled = v == 1 && _check.texture != null;
        }
    }

    /// <summary>
    /// OptionsSliderTemplate (OptionsPanelTemplates.xml:76-142): UI-SliderBar-Background / -Border backdrop (edge 8, insets 3 left/right,
    /// 6 top/bottom), the 32x32 UI-SliderBar-Button-Horizontal thumb, the label above (GameFontHighlight) - widened, with the value on the
    /// right and - / + step buttons at the ends (VR fingers). Tap anywhere on the track to jump there; drag it (desktop mouse, or hold
    /// the VR trigger) to slide.
    /// </summary>
    public sealed class UiSlider
    {
        public const string Background = @"Interface\Buttons\UI-SliderBar-Background.blp";
        public const string Border = @"Interface\Buttons\UI-SliderBar-Border.blp";
        public const string Thumb = @"Interface\Buttons\UI-SliderBar-Button-Horizontal.blp";
        public const float RowHeight = 44f;

        private readonly float _min, _max, _step;
        private readonly Func<float> _get;
        private readonly Action<float> _set;
        private readonly Func<float, string> _format;
        private readonly Action _changed;
        private readonly RawImage _thumb;
        private readonly TextMeshProUGUI _value;
        private readonly float _trackX, _trackW, _trackY;
        private float _shown = float.NaN;

        public UiSlider(Transform parent, string label, float x, float y, float w, float min, float max, float step,
            Func<float> get, Action<float> set, Func<float, string> format, string tip, Action changed)
        {
            _min = min; _max = max; _step = step;
            _get = get; _set = set; _format = format; _changed = changed;
            TextMeshProUGUI l = UiKit.Text(parent, label, label, 12f, Color.white, TextAlignmentOptions.TopLeft, x, y, w - 90f, 15f);
            l.textWrappingMode = TextWrappingModes.NoWrap;
            _value = UiKit.Text(parent, label + " value", "", 12f, UiArt.Gold, TextAlignmentOptions.TopRight, x + w - 90f, y, 90f, 15f);
            _value.textWrappingMode = TextWrappingModes.NoWrap;

            _trackX = x + 28f;
            _trackW = w - 56f;
            _trackY = y + 19f;
            UiBackdrop bd = UiBackdrop.Make(parent, label + " track", Background, Border, 8f, 3f, 8f, Color.white);
            bd.InsetY = 6f;
            bd.Place(_trackX, _trackY, _trackW, 17f);

            // The press area: the track plus 10 above and below (HitRectInsets top -10, bottom -10).
            UiButton track = UiKit.Button(parent, label + " slider", _trackX, _trackY - 10f, _trackW, 37f);
            string tipText = "<size=14>" + label + "</size>" + (string.IsNullOrEmpty(tip) ? "" : "\n" + tip);
            track.Tip = () => tipText;
            track.ClickAt = f => SetFromTrack(f.x);
            if (UiKit.Pointable)
            {
                var drag = track.gameObject.AddComponent<UiDrag>();
                drag.Button = track;
            }
            _thumb = UiKit.Pic(parent, label + " thumb", UiArt.Tex(Thumb), _trackX, _trackY - 7.5f, 32f, 32f);

            UiButton minus = UiKit.PanelButton(parent, "-", x, y + 17f, 24f, 21f, () => Nudge(-1));
            minus.Tip = () => tipText;
            UiButton plus = UiKit.PanelButton(parent, "+", x + w - 24f, y + 17f, 24f, 21f, () => Nudge(1));
            plus.Tip = () => tipText;
        }

        private float Snap(float v)
        {
            if (_step > 0f) v = _min + Mathf.Round((v - _min) / _step) * _step;
            return Mathf.Clamp(v, _min, _max);
        }

        /// <summary>The thumb centre travels 6 units inside each end of the track.</summary>
        private void SetFromTrack(float fx)
        {
            float px = fx * _trackW;
            float f = Mathf.Clamp01((px - 6f) / Mathf.Max(1f, _trackW - 12f));
            Apply(Snap(_min + f * (_max - _min)));
        }

        private void Nudge(int dir)
        {
            float step = _step > 0f ? _step : (_max - _min) / 20f;
            Apply(Snap(_get() + dir * step));
        }

        private void Apply(float v)
        {
            if (Mathf.Abs(v - _get()) < 1e-5f) return;
            _set(v);
            if (_changed != null) _changed();
            Refresh();
        }

        public void Refresh()
        {
            float v = _get();
            if (v == _shown) return;
            _shown = v;
            float f = _max > _min ? Mathf.Clamp01((v - _min) / (_max - _min)) : 0f;
            UiKit.Move(_thumb.rectTransform, _trackX + 6f + f * (_trackW - 12f) - 16f, _trackY - 7.5f);
            _value.text = _format != null ? _format(v) : v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The settings window, WoW's Interface Options look in a 384x512 panel (so it fits the VR window lid and the desktop panel spot):
    /// UI-DialogBox backdrop (edge 32, insets 11) with the UI-DialogBox-Header, four category buttons like OptionsListButtonTemplate
    /// (GameFontNormal, UI-QuestLogTitleHighlight ADD; igMainMenuOptionCheckBoxOn on click), a Tooltip-Border box (border 0.6 grey)
    /// with the page, Defaults at BOTTOMLEFT (16, 16) and Close at BOTTOMRIGHT (-16, 16) (InterfaceOptionsFrame.xml). Every
    /// WowSettings field is here; changes apply at once (WowSettings.NotifyChanged, at most every 0.15 s while sliding, and when the
    /// window closes) and the front end saves them.
    /// </summary>
    public sealed class UiSettingsWindow : UiWindow
    {
        // Stage S1 (owner hud): a fifth page "HUD" (the top-left HUD group's size and place, Reset HUD, the focus button) and
        // "Return to the room" at the bottom of every page (moved here from the wrist / the desktop label - the user's request).
        // Iteration 11 (w0game): three more pages - "Bars" (vrcast's ActionBars page), "Avatar" (forms, combat animation style, immersive
        // armour) and "Controls" (cursor, ground spells, talent preview, flight map, VR comfort). Their rows are "live rows": hidden until
        // the feature's owner calls WowSettings.SetLive(<save key>); a page without a shown row has no tab. Up to six tabs stay in one row
        // (67 wide for five as before, 56 for six); seven or eight make two rows of four (84 wide) and the page box moves down 12 units.
        public static readonly string[] PageNames = { "Display", "Effects", "Audio", "Gameplay", "HUD", "Bars", "Avatar", "Controls", "Armour", "Macros" };
        public const int Pages = 10;   // lead 2026-10-03: + "Armour" (Armour Adjustment); lead 2026-10-12: + "Macros" (UiMacroPage, always shown)
        public const int MacrosPage = 9;
        private UiMacroPage _macros;
        public const int FixedPages = 5;   // Display .. HUD always have a tab
        public const string Header = @"Interface\DialogFrame\UI-DialogBox-Header.blp";

        private readonly RectTransform[] _page = new RectTransform[Pages];
        private readonly UiButton[] _tab = new UiButton[Pages];
        private readonly RawImage[] _tabLit = new RawImage[Pages];
        private readonly List<UiCheck>[] _checks = new List<UiCheck>[Pages];
        private readonly List<UiSlider>[] _sliders = new List<UiSlider>[Pages];

        /// <summary>Iteration 11: a row shown only while WowSettings.IsLive(Key) (and, VrOnly, in VR); stacked from BaseY of its page.</summary>
        private sealed class LiveRow
        {
            public int Page;
            public string Key;
            public bool VrOnly;
            public RectTransform Rt;
            public float Height, Gap;
        }
        private readonly List<LiveRow> _live = new List<LiveRow>();
        private readonly float[] _liveBase = new float[Pages];
        private readonly float[] _liveY = new float[Pages];
        private readonly bool[] _pageShown = new bool[Pages];
        private UiBackdrop _box;
        private UiArmourFitView _fit;   // lead 2026-10-04: the Armour page's fitting view (UiArmourFitView)
        private int _liveShown = -1;
        private int _vrShown = -1;
        private UiButton _room;
        private float _roomArmedUntil = -1f;
        private bool _roomArmed;
        private int _current = -1;
        private bool _dirty;
        private float _lastNotify;

        public UiSettingsWindow(Transform parent, UiContext ctx, float x, float y, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "Settings", x, y, W, H);
            UiBackdrop frame = UiBackdrop.Make(Root, "Frame", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            frame.Place(0f, 14f, W, H - 14f);
            UiKit.Pic(Root, "Header", UiArt.Tex(Header), (W - 300f) * 0.5f, 2f, 300f, 68f);
            TextMeshProUGUI title = UiKit.Text(Root, "Title", "Settings", 13f, UiArt.Gold, TextAlignmentOptions.Center, (W - 200f) * 0.5f, 15f, 200f, 16f);
            title.textWrappingMode = TextWrappingModes.NoWrap;

            // Category buttons along the top (stage S1: five, 67 wide instead of four 84 wide; iteration 11: placed by LayoutTabs).
            for (int i = 0; i < Pages; i++)
            {
                int k = i;
                const float tw = 67f;
                float bx = 20f + i * (tw + 2f);
                UiButton b = UiKit.Button(Root, "Page " + PageNames[i], bx, 50f, tw, 20f);
                _tabLit[i] = UiKit.Pic(b.transform, "Selected", UiArt.Add(UiArt.QuestLogTitleHighlight), 0f, 1f, tw, 18f);
                _tabLit[i].color = new Color(1f, 0.82f, 0f, 0.55f);
                _tabLit[i].enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.QuestLogTitleHighlight), 0f, 1f, tw, 18f);
                b.Hilite.enabled = false;
                b.Label = UiKit.Text(b.transform, "Text", PageNames[i], 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, tw, 20f);
                b.Label.textWrappingMode = TextWrappingModes.NoWrap;
                b.LabelColor = UiArt.Gold;
                b.Click = () => { if (_current != k) UiSound.Named("igMainMenuOptionCheckBoxOn"); Show(k); };
                _tab[i] = b;
            }

            UiBackdrop box = UiBackdrop.Make(Root, "Box", null, UiArt.TooltipBorder, 16f, 5f, 16f, new Color(0.25f, 0.25f, 0.35f));
            box.Place(16f, 74f, W - 32f, 392f);
            box.SetEdgeColor(new Color(0.6f, 0.6f, 0.6f, 1f));
            _box = box;

            for (int i = 0; i < Pages; i++)
            {
                _page[i] = UiKit.Rect(Root, "Page " + PageNames[i] + " content", 0f, 0f, W, H);
                _checks[i] = new List<UiCheck>();
                _sliders[i] = new List<UiSlider>();
                _liveBase[i] = Top;
            }
            BuildDisplay(_page[0], 0);
            BuildEffects(_page[1], 1);
            BuildAudio(_page[2], 2);
            BuildGameplay(_page[3], 3);
            BuildHud(_page[4], 4);
            BuildBars(5);       // iteration 11 (vrcast hook 10)
            BuildAvatar(6);     // iteration 11 (avatar hook 5)
            BuildControls(7);   // iteration 11 (uicore H2, spellfx 13, talents 8, motion 11)
            BuildArmour(8);     // lead 2026-10-03: Armour Adjustment
            _macros = new UiMacroPage(_page[MacrosPage], ctx, Root.root);   // lead 2026-10-12: macros
            _fit = new UiArmourFitView(Root, ctx);   // lead 2026-10-04: the avatar while fitting (left of this window)
            Relayout();

            UiButton defaults = UiKit.PanelButton(Root, "Defaults", 16f, H - 16f - 22f, 96f, 22f, () =>
            {
                UiSound.Play(WowUiSound.ButtonClick);
                WowSettings.ResetDefaults();   // raises Changed itself
                _dirty = false;
                Ctx.Say("Settings reset to their defaults.", UiArt.InfoYellow);
            });
            defaults.Tip = () => "<size=14>Defaults</size>\nPuts every setting back to its default value - except your armour fitting (the Armour page, Armour fit) and mount seat height, which are kept.";
            UiKit.PanelButton(Root, "Close", W - 16f - 96f, H - 16f - 22f, 96f, 22f, close);
            // Stage S1: "Return to the room" (ours) between Defaults and Close, on every page. It saves and ends the game like the K key
            // (WowGame.ReturnToRoom; online it logs the character out), so it asks for a second press within RoomConfirm seconds.
            _room = UiKit.PanelButton(Root, RoomLabel, 16f + 96f + 8f, H - 16f - 22f, W - 32f - 2f * 96f - 16f, 22f, OnRoom);
            // Iteration 11 (uicore H10 / PLAN11 s2 D-K2): once K opens the Skills window in the world (WowGame.BackKeyInWorld false),
            // the key returns to the room only while the world is loading - the tooltip says which.
            _room.Tip = () => "<size=14>" + RoomLabel + "</size>\nSaves your character and takes you back to the black room (the "
                + WowPlayer.BackKeyName + (WowGame.BackKeyInWorld ? " key does the same on the monitor). Press twice."
                    : " key does the same on the monitor while the world is loading; in the world it opens your Skills). Press twice.");
            Show(0);
            Root.gameObject.SetActive(false);
        }

        public const string RoomLabel = "Return to the room";      // ours
        public const string RoomConfirmLabel = "Press again to leave";   // ours
        public const float RoomConfirm = 3f;                        // ours: seconds the second press is waited for

        private void OnRoom()
        {
            float now = Time.unscaledTime;
            if (!_roomArmed || now > _roomArmedUntil)
            {
                _roomArmed = true;
                _roomArmedUntil = now + RoomConfirm;
                _room.SetLabel(RoomConfirmLabel);
                UiSound.Play(WowUiSound.ButtonClick);
                WowLog.Info("UI: Settings 'Return to the room' pressed once - waiting " + RoomConfirm.ToString("0") + " s for the second press.");
                return;
            }
            _roomArmed = false;
            _room.SetLabel(RoomLabel);
            UiSound.Play(WowUiSound.ButtonClick);
            Flush();
            WowGame g = WowGame.Current;
            if (g == null) { WowLog.Warn("UI: Settings 'Return to the room' - no game object to ask."); return; }
            if (Ctx != null && Ctx.Toggle != null && Ctx.IsOpen != null && Ctx.IsOpen(UiWindowKind.Settings)) Ctx.Toggle(UiWindowKind.Settings);
            g.ReturnToRoom("the Settings window's " + RoomLabel + " button");
        }

        private const float RowX = 30f, RowW = 324f, Top = 86f;

        private void Changed() { _dirty = true; }

        private float AddCheck(int page, float y, string label, string hint, Func<bool> get, Action<bool> set, string tip)
        {
            _checks[page].Add(new UiCheck(_page[page], label, hint, RowX, y, RowW, get, set, tip, Changed));
            return y + 28f;
        }

        private float AddSlider(int page, float y, string label, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> fmt, string tip)
        {
            _sliders[page].Add(new UiSlider(_page[page], label, RowX, y, RowW, min, max, step, get, set, fmt, tip, Changed));
            return y + UiSlider.RowHeight;
        }

        private static string Percent(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }
        private static string Times(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture) + "x"; }

        private void BuildDisplay(RectTransform p, int k)
        {
            float y = Top;
            y = AddCheck(k, y, "Nameplates", null, () => WowSettings.ShowNameplates, v => WowSettings.ShowNameplates = v, "Health plates over creatures and NPCs.");
            y = AddCheck(k, y, "NPC names", "not targeted", () => WowSettings.ShowFriendlyNames, v => WowSettings.ShowFriendlyNames = v, "NPC names above their heads even when they are not your target.");
            y = AddCheck(k, y, "Floating combat text", null, () => WowSettings.ShowCombatText, v => WowSettings.ShowCombatText = v, "Damage and healing numbers rising from the target.");
            y = AddCheck(k, y, "Somnium player plates", null, () => WowSettings.ShowPlayerPlates, v => WowSettings.ShowPlayerPlates = v, "Name, level and class over other players.");
            y = AddCheck(k, y, "Quest markers", null, () => WowSettings.ShowQuestMarkers, v => WowSettings.ShowQuestMarkers = v, "The ! and ? above quest givers.");
            y = AddCheck(k, y, "Floating HUD", "VR", () => WowSettings.VrHud, v => WowSettings.VrHud = v, "A small player, target and party display at the top left of your view.");
            y = AddCheck(k, y, "Forearm action bar", "VR", () => WowSettings.VrForearmBar, v => WowSettings.VrForearmBar = v, "The old panel along your left forearm (the grip radial menus replace it).");
            y = AddCheck(k, y, "Low health red edges", null, () => WowSettings.LowHealthVignette, v => WowSettings.LowHealthVignette = v, "The edges of the view turn red and pulse at 20% health and below.");
            y += 8f;
            y = AddSlider(k, y, "Interface scale (desktop)", 0.75f, 1.5f, 0.05f, () => WowSettings.UiScale, v => WowSettings.UiScale = v, Times, "Size of the game interface on the monitor.");
            y = AddSlider(k, y, "Arm panel scale (VR)", 0.75f, 1.5f, 0.05f, () => WowSettings.ArmUiScale, v => WowSettings.ArmUiScale = v, Times, "Size of the panel on your left forearm and its windows.");
            AddSlider(k, y, "Quest text size (VR)", 0.75f, 2f, 0.05f, () => WowSettings.VrQuestSize, v => WowSettings.VrQuestSize = v, Times,
                "Size of the quest window, the quest log and the objectives list in VR.");   // lead 2026-10-10 (ours)
        }

        private static readonly string[] Quality = { "Low", "Medium", "High" };
        private static readonly string[] Gender = { "Male", "Female" };
        private static readonly string[] Difficulty = { "Easy", "Normal", "Hard" };

        private static string Named(string[] names, float v)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(v), 0, names.Length - 1);
            return names[i];
        }

        private void BuildEffects(RectTransform p, int k)
        {
            float y = Top;
            y = AddCheck(k, y, "Spell effects", null, () => WowSettings.SpellEffects, v => WowSettings.SpellEffects = v, "Cast glows, missiles, impacts and aura effects.");
            y = AddCheck(k, y, "World particles", null, () => WowSettings.WorldParticles, v => WowSettings.WorldParticles = v, "Torches, fires, smoke and glows on the world's props.");
            y += 8f;
            y = AddSlider(k, y, "Effects quality", 0f, 2f, 1f, () => WowSettings.EffectsQuality, v => WowSettings.EffectsQuality = Mathf.RoundToInt(v), v => Named(Quality, v), "Particle counts and how far away effects are drawn.");
            y += 8f;
            y = AddCheck(k, y, "Ground clutter", null, () => WowSettings.GroundClutter, v => WowSettings.GroundClutter = v, "Grass, flowers and pebbles on the ground.");
            y = AddSlider(k, y, "Clutter density", 0.25f, 1.5f, 0.05f, () => WowSettings.ClutterDensity, v => WowSettings.ClutterDensity = v, Times, "How much grass and how many flowers grow around you.");
            y = AddCheck(k, y, "Birds and critters", null, () => WowSettings.AmbientLife, v => WowSettings.AmbientLife = v, "Birds in the sky and small animals on the ground.");
            y = AddCheck(k, y, "Terrain shine", null, () => WowSettings.TerrainSpecular, v => WowSettings.TerrainSpecular = v, "The sun glinting on roads, rocks and snow (WoW's terrain specular).");
            y = AddSlider(k, y, "Shine strength", 0.25f, 4f, 0.25f, () => WowSettings.TerrainShine, v => WowSettings.TerrainShine = v, Times, "How strongly the ground reflects the sun (1.00x = WoW's own amount).");
            y = AddSlider(k, y, "Water visibility", 0.5f, 2.5f, 0.25f, () => WowSettings.WaterVisibility, v => WowSettings.WaterVisibility = v, Times, "How strongly lakes, rivers and the sea reflect the sky and the sun, and how see-through they are.");
            // Iteration 11 (spellfx hook 13, live row - shown once spellfx calls WowSettings.SetLive("weapon_enchant_glows")).
            _liveBase[k] = y;
            LiveCheck(k, "weapon_enchant_glows", false, 0f, "Weapon enchant glows", null, () => WowSettings.WeaponEnchantGlows, v => WowSettings.WeaponEnchantGlows = v,
                "Glows of weapon enchants, poisons and imbues on weapons.");
            // lead 2026-10-10 (shown by UiSharp when the client's renderers have the after-post pass)
            LiveCheck(k, UiSharp.SettingKey, true, 0f, "Sharp VR interface", "VR", () => WowSettings.SharpVrUi, v => WowSettings.SharpVrUi = v,
                "Draws the game's panels, menus, nameplates and texts after Somnium's DLSS upscaling, so they stay crisp. Turn it off if they shake against the world.");
        }

        private void BuildAudio(RectTransform p, int k)
        {
            float y = Top - 4f;
            y = AddSlider(k, y, "Master volume", 0f, 1f, 0.05f, () => WowSettings.MasterVolume, v => WowSettings.MasterVolume = v, Percent, "Every sound of the game.");
            y = AddCheck(k, y, "Music", null, () => WowSettings.Music, v => WowSettings.Music = v, "Zone music.");
            y = AddSlider(k, y, "Music volume", 0f, 1f, 0.05f, () => WowSettings.MusicVolume, v => WowSettings.MusicVolume = v, Percent, null);
            y = AddSlider(k, y, "Sound effects", 0f, 1f, 0.05f, () => WowSettings.EffectsVolume, v => WowSettings.EffectsVolume = v, Percent, "Spells, weapons, creatures and the interface.");
            y = AddSlider(k, y, "Ambience", 0f, 1f, 0.05f, () => WowSettings.AmbienceVolume, v => WowSettings.AmbienceVolume = v, Percent, "Wind, birds, water and the zone's background sounds.");
            y = AddCheck(k, y, "Footsteps", null, () => WowSettings.Footsteps, v => WowSettings.Footsteps = v, "Your steps by ground type.");
            y = AddSlider(k, y, "Footstep volume", 0f, 1f, 0.05f, () => WowSettings.FootstepVolume, v => WowSettings.FootstepVolume = v, Percent, null);
            y = AddSlider(k, y, "Voices", 0f, 1f, 0.05f, () => WowSettings.VoiceVolume, v => WowSettings.VoiceVolume = v, Percent, "NPC greetings and your character's voice lines.");
            AddSlider(k, y, "Character voice", 0f, 1f, 1f, () => WowSettings.VoiceGender, v => WowSettings.VoiceGender = Mathf.RoundToInt(v), v => Named(Gender, v), "Your race's voice lines (\"Not enough mana\", ...) in a male or female voice.");
        }

        private void BuildGameplay(RectTransform p, int k)
        {
            float y = Top;
            y = AddSlider(k, y, "Difficulty", 0f, 2f, 1f, () => WowSettings.Difficulty, v => WowSettings.Difficulty = Mathf.RoundToInt(v), v => Named(Difficulty, v), "Creature health and damage: easy, normal or hard.");
            y += 4f;
            y = AddCheck(k, y, "Auto loot", null, () => WowSettings.AutoLoot, v => WowSettings.AutoLoot = v, "Opening a corpse takes everything at once.");
            y = AddCheck(k, y, "Avatar combat animations", "desktop", () => WowSettings.AvatarCombatAnimations, v => WowSettings.AvatarCombatAnimations = v, "Your Somnium avatar swings, casts and draws a bow when your character does.");
            y = AddCheck(k, y, "Controller vibration", "VR", () => WowSettings.Haptics, v => WowSettings.Haptics = v, "Short pulses on hits and presses.");
            y = AddCheck(k, y, "Immersive casting", "VR", () => WowSettings.ImmersiveCasting, v => WowSettings.ImmersiveCasting = v, "A finished cast waits in your hand until you push that hand forward, like throwing the spell. +10% damage on spells you push (buff \"Immersive Mode\"; online on a realm with the Dreamport hold module).");
            y = AddCheck(k, y, "Fast travel", "flights", () => WowSettings.FastTravel, v => WowSettings.FastTravel = v, "A flight fades out and you arrive at once instead of riding the route.");
            y = AddCheck(k, y, "Unlock all flight paths", "testing", () => WowSettings.UnlockAllFlightPaths, v => WowSettings.UnlockAllFlightPaths = v, "Every flight path of your faction counts as discovered. Paths you really discover are still saved.");
            // Iteration 7 (PLAN7 s5.6, decision D2: both on by default; the page's 2 free rows).
            y = AddCheck(k, y, "Unlock all mounts", "testing", () => WowSettings.UnlockAllMounts, v => WowSettings.UnlockAllMounts = v, "Every mount of your faction and class, with the riding to use it. Never saved to your character; switch off for WoW's rules.");
            y = AddCheck(k, y, "Fly anywhere", "testing", () => WowSettings.FlyAnywhere, v => WowSettings.FlyAnywhere = v, "Flying mounts fly in any zone offline (in WoW 3.3.5a only Outland and Northrend allow flying). Online the realm decides.");
            y += 8f;
            y = AddSlider(k, y, "Target reach (VR)", 20f, 300f, 10f, () => WowSettings.VrTargetRange, v => WowSettings.VrTargetRange = v, v => Mathf.RoundToInt(v) + " m", "How far your pointing ray can select creatures and NPCs.");
            AddSlider(k, y, "Swing speed for a hit (VR)", 0.5f, 4f, 0.1f, () => WowSettings.SwingSpeed, v => WowSettings.SwingSpeed = v, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " m/s", "How fast your hand must move for a VR weapon swing to count as a hit.");
        }

        private static string Metres(float v, string plus, string minus)
        {
            int cm = Mathf.RoundToInt(v * 100f);
            if (cm == 0) return "centre";
            return (Mathf.Abs(cm) / 100f).ToString("0.00", CultureInfo.InvariantCulture) + " m " + (cm > 0 ? plus : minus);
        }

        /// <summary>
        /// Stage S1 (the user: "Make the top left hud that shows health and target and party members the ability to resize and position
        /// in the settings"): the size and place of the top-left group - desktop (fractions of the screen) and VR (the floating HUD,
        /// metres in its own plane) keep their own values; Reset HUD puts both back where WoW / iteration 4 put them; and how the focus
        /// button works (a Dreamport addition).
        /// </summary>
        private void BuildHud(RectTransform p, int k)
        {
            float y = Top - 4f;
            y = AddSlider(k, y, "HUD size (desktop)", WowSettings.HudScaleMin, WowSettings.HudScaleMax, 0.05f, () => WowSettings.HudScale, v => WowSettings.HudScale = v, Times,
                "Size of the player, target, party and focus frames at the top left of the monitor (on top of the interface scale).");
            y = AddSlider(k, y, "HUD across (desktop)", 0f, WowSettings.HudOffsetMaxX, 0.02f, () => WowSettings.HudOffsetX, v => WowSettings.HudOffsetX = v, Percent,
                "Moves those frames to the right, in parts of the screen width (0% = WoW's place). They always stay on the screen.");
            y = AddSlider(k, y, "HUD down (desktop)", 0f, WowSettings.HudOffsetMaxY, 0.02f, () => WowSettings.HudOffsetY, v => WowSettings.HudOffsetY = v, Percent,
                "Moves those frames down, in parts of the screen height.");
            y = AddSlider(k, y, "HUD size (VR)", WowSettings.HudScaleMin, WowSettings.HudScaleMax, 0.05f, () => WowSettings.VrHudScale, v => WowSettings.VrHudScale = v, Times,
                "Size of the floating HUD at the top left of your view.");
            y = AddSlider(k, y, "HUD left / right (VR)", -WowSettings.VrHudOffsetMax, WowSettings.VrHudOffsetMax, 0.02f, () => WowSettings.VrHudOffsetX, v => WowSettings.VrHudOffsetX = v,
                v => Metres(v, "right", "left"), "Moves the floating HUD sideways (it still follows your view as before).");
            y = AddSlider(k, y, "HUD down / up (VR)", -WowSettings.VrHudOffsetMax, WowSettings.VrHudOffsetMax, 0.02f, () => WowSettings.VrHudOffsetY, v => WowSettings.VrHudOffsetY = v,
                v => Metres(v, "up", "down"), "Moves the floating HUD up or down.");
            y += 4f;
            UiButton reset = UiKit.PanelButton(p, "Reset HUD", RowX, y, 110f, 22f, () =>
            {
                UiSound.Play(WowUiSound.ButtonClick);
                WowSettings.ResetHud();   // raises Changed itself
                WowLog.Info("UI: Settings 'Reset HUD' - the HUD group back to its default size and place (desktop and VR).");
                Ctx.Say("HUD size and position reset.", UiArt.InfoYellow);   // ours
            });
            reset.Tip = () => "<size=14>Reset HUD</size>\nThe player, target, party and focus frames back to their normal size and place (monitor and VR).";
            // Lead 2026-10-03 (the user: "put the mini map on the top right"): beside Reset HUD, so the help text below keeps its room.
            _checks[k].Add(new UiCheck(p, "Show minimap", "desktop", RowX + 124f, y - 2f, RowW - 124f, () => WowSettings.ShowMinimap, v => WowSettings.ShowMinimap = v,
                "WoW's minimap at the top right of the monitor: the map around you, quest givers, flight masters, your group and your corpse. Zoom with its + / - buttons or the mouse wheel.", Changed));
            y += 28f;
            TextMeshProUGUI help = UiKit.Text(p, "Focus help",
                "<color=#ffd100>Focus</color> (a Dreamport addition): target someone, then press <color=#ffffff>" + VrFocusKeys.Vr + "</color> on the left controller"
                + " (a short press; holding it is Somnium's inspector) or <color=#ffffff>" + VrFocusKeys.Desktop + "</color> on the keyboard: they become your focus."
                + " Press again to target your focus, three times quickly to clear it. Click a party frame to target that player.",
                10f, Color.white, TextAlignmentOptions.TopLeft, RowX, y, RowW, 466f - 8f - y);
            help.textWrappingMode = TextWrappingModes.Normal;
        }

        // ---------------------------------------------------------------------------------------------------------------- iteration 11

        private void LiveCheck(int page, string key, bool vrOnly, float gap, string label, string hint, Func<bool> get, Action<bool> set, string tip)
        {
            RectTransform rt = UiKit.Rect(_page[page], "Row " + key, 0f, 0f, W, 28f);
            _checks[page].Add(new UiCheck(rt, label, hint, RowX, 0f, RowW, get, set, tip, Changed));
            _live.Add(new LiveRow { Page = page, Key = key, VrOnly = vrOnly, Rt = rt, Height = 28f, Gap = gap });
            rt.gameObject.SetActive(false);
        }

        private void LiveSlider(int page, string key, bool vrOnly, float gap, string label, float min, float max, float step, Func<float> get, Action<float> set,
            Func<float, string> fmt, string tip)
        {
            RectTransform rt = UiKit.Rect(_page[page], "Row " + key, 0f, 0f, W, UiSlider.RowHeight);
            _sliders[page].Add(new UiSlider(rt, label, RowX, 0f, RowW, min, max, step, get, set, fmt, tip, Changed));
            _live.Add(new LiveRow { Page = page, Key = key, VrOnly = vrOnly, Rt = rt, Height = UiSlider.RowHeight, Gap = gap });
            rt.gameObject.SetActive(false);
        }

        private static readonly string[] StrikeBars = { "Action Bar", "Bottom Left Bar", "Bottom Right Bar", "Right Bar", "Right Bar 2" };   // vrcast (ours)

        /// <summary>vrcast's ActionBars page (proposal-vrcast.md hook 10). Texts: GlobalStrings.lua lines in the comments, the rest ours.</summary>
        private void BuildBars(int k)
        {
            LiveCheck(k, "show_bottom_left_bar", false, 0f, "Bottom Left Bar", "desktop", () => WowSettings.ShowBottomLeftBar, v => WowSettings.ShowBottomLeftBar = v,
                "Toggles an optional actionbar in the bottom left area of the screen.");   // SHOW_MULTIBAR1_TEXT 6282 / OPTION_TOOLTIP_SHOW_MULTIBAR1 5425
            LiveCheck(k, "show_bottom_right_bar", false, 0f, "Bottom Right Bar", "desktop", () => WowSettings.ShowBottomRightBar, v => WowSettings.ShowBottomRightBar = v,
                "Toggles an optional actionbar in the bottom right area of the screen.");   // 6283 / 5426
            LiveCheck(k, "show_right_bar", false, 0f, "Right Bar", "desktop", () => WowSettings.ShowRightBar, v => WowSettings.ShowRightBar = v,
                "Toggles an optional actionbar on the right side of the screen.");   // 6284 / 5427
            LiveCheck(k, "show_right_bar_2", false, 0f, "Right Bar 2", "desktop", () => WowSettings.ShowRightBar2, v => WowSettings.ShowRightBar2 = v,
                "Toggles an additional optional actionbar on the right side of the screen.");   // 6285 / 5428
            LiveCheck(k, "lock_action_bars", false, 0f, "Lock ActionBars", null, () => WowSettings.LockActionBars, v => WowSettings.LockActionBars = v,
                "Prevents the user from picking up/dragging spells on the action bar. Hold Shift to drag anyway.");   // LOCK_ACTIONBAR_TEXT 4773 / OPTION_TOOLTIP_LOCK_ACTIONBAR 5371 (first sentence) + ours
            LiveCheck(k, "always_show_action_bars", false, 0f, "Always Show ActionBars", "desktop", () => WowSettings.AlwaysShowActionBars, v => WowSettings.AlwaysShowActionBars = v,
                "Empty buttons of the extra bars stay visible.");   // ALWAYS_SHOW_MULTIBARS_TEXT 536 + ours
            LiveCheck(k, "vr_rows", false, 8f, "Pull / push rows", "VR", () => WowSettings.VrRows, v => WowSettings.VrRows = v,
                "While a spell menu is open, push the hand away or pull it towards you for the other bars.");
            LiveCheck(k, "vr_rows_remember", false, 0f, "Rows remember the last one", "VR", () => WowSettings.VrRowsRemember, v => WowSettings.VrRowsRemember = v,
                "A spell menu opens on the row that hand used last.");
            LiveCheck(k, "immersive_strike", false, 0f, "Immersive strike", "VR", () => WowSettings.ImmersiveStrike, v => WowSettings.ImmersiveStrike = v,
                "With an enemy selected, a circle of your attacks stands in front of you: hit one with your weapon to use it.");
            LiveSlider(k, "strike_ring_bar", false, 0f, "Strike ring bar", 0f, 4f, 1f, () => WowSettings.StrikeRingBar, v => WowSettings.StrikeRingBar = Mathf.RoundToInt(v),
                v => Named(StrikeBars, v), "Which bar's attacks the strike ring shows.");
            LiveSlider(k, "strike_ring_distance", false, 0f, "Strike ring distance", 0.25f, 1.20f, 0.05f, () => WowSettings.StrikeRingDistance, v => WowSettings.StrikeRingDistance = v,
                v => v.ToString("0.00", CultureInfo.InvariantCulture) + " m", "How far in front of you the strike ring stands.");
            LiveSlider(k, "strike_ring_size", false, 0f, "Strike ring icon size", 0.5f, 2f, 0.1f, () => WowSettings.StrikeRingSize, v => WowSettings.StrikeRingSize = v,
                v => "x" + v.ToString("0.0", CultureInfo.InvariantCulture), "How big the attack icons of the strike ring are (bigger icons are easier to hit).");
            LiveSlider(k, "strike_ring_spread", false, 0f, "Strike ring spread", 0.5f, 2f, 0.1f, () => WowSettings.StrikeRingSpread, v => WowSettings.StrikeRingSpread = v,
                v => "x" + v.ToString("0.0", CultureInfo.InvariantCulture), "How wide the circle of attack icons is (the space between them).");
            LiveSlider(k, "strike_ring_height", false, 0f, "Strike ring height", -0.50f, 0.30f, 0.05f, () => WowSettings.StrikeRingHeight, v => WowSettings.StrikeRingHeight = v,
                v => (v > 0.001f ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture) + " m", "Raise or lower the strike ring (0 = its centre 0.30 m below your eyes).");
            LiveCheck(k, "immersive_eating", true, 8f, "Immersive eating", "VR", () => WowSettings.ImmersiveEating, v => WowSettings.ImmersiveEating = v,
                "Sit down in real life to eat and drink. Standing up stops eating, as standing up does in WoW.");   // lead 2026-10-04 (ours)
        }

        private static readonly string[] CombatStyles = { "Simple (arms)", "WoW (full body)" };                      // avatar (ours)
        private static readonly string[] ArmourModes = { "Off", "Helm and shoulders", "Full (experimental)" };      // avatar (ours)

        /// <summary>The avatar area (proposal-avatar.md hook 5): forms on the avatar, the desktop combat animation style, immersive armour.
        /// "immersive_armour" offers Off / Helm and shoulders / Full (experimental) - A2 (the armour shell, Game/Play/Armour/ArmourShell) is built.</summary>
        private void BuildAvatar(int k)
        {
            LiveCheck(k, "show_forms", false, 0f, "Show forms on my avatar", null, () => WowSettings.ShowForms, v => WowSettings.ShowForms = v,
                "Bear, cat, travel form, polymorph and other shapes change your Somnium avatar into the creature, for you and everyone else.");
            // lead 2026-10-12 (the user: "add the ability to hide weapons, like you can armour"): shown by WowGear (SetLive)
            LiveChecks2(k, "show_weapons", 4f,
                "Show main hand", () => WowSettings.ShowMainHand, v => WowSettings.ShowMainHand = v, "Your main-hand weapon on your avatar, sheathed and in your hand (everyone sees your choice). Hidden = it cannot be drawn either.",
                "Show off hand", () => WowSettings.ShowOffHand, v => WowSettings.ShowOffHand = v, "Your off-hand weapon, shield or held item on your avatar (everyone sees your choice). Hidden = it cannot be drawn either.");
            LiveCheck(k, "show_ranged", false, 0f, "Show ranged weapon", null, () => WowSettings.ShowRanged, v => WowSettings.ShowRanged = v,
                "Your bow, gun, crossbow or wand on your avatar (everyone sees your choice). Hidden = it cannot be drawn either.");
            LiveSlider(k, "avatar_combat_style", false, 0f, "Combat animation style (desktop)", 0f, 1f, 1f, () => WowSettings.AvatarCombatStyle,
                v => WowSettings.AvatarCombatStyle = Mathf.RoundToInt(v), v => Named(CombatStyles, v),
                "WoW (full body): stances, attacks and casts move the whole avatar, feet too. Simple (arms): the earlier arm motions.");
            LiveSlider(k, "immersive_armour", false, 0f, "Immersive armour", 0f, Game.Armour.ArmourGate.FullReady ? 2f : 1f, 1f, () => WowSettings.ImmersiveArmour,
                v => WowSettings.ImmersiveArmour = Mathf.RoundToInt(v), v => Named(ArmourModes, v),
                "Your equipped armour on your avatar (everyone sees it). Helm and shoulders: those two pieces. Full (experimental): also your chest, legs, gloves, boots, belt, bracers, tabard and cloak, painted on a WoW body shell that follows your avatar.");
            LiveSlider(k, "armour_fit", false, 0f, "Armour fit", 0.5f, 1.4f, 0.05f, () => WowSettings.ArmourFit, v => WowSettings.ArmourFit = v, Times,
                "Size of the armour pieces on your avatar (with Full: how thick the armour sits around your body).");
            LiveSlider(k, "mount_seat_height", false, 6f, "Mount seat height", -0.30f, 0.50f, 0.01f, () => WowSettings.MountSeatHeight, v => WowSettings.MountSeatHeight = v,
                v => (v > 0.0001f ? "+" : "") + (v * 100f).ToString("0", CultureInfo.InvariantCulture) + " cm",
                "Raises (+) or lowers (-) you in the saddle of every mount, on top of WoW's own riding height. Saved; others see you where you sit.");   // lead 2026-10-04
        }

        private static readonly string[] ArmourPieceNames = Game.Armour.ArmourAdjust.Names;   // ours (2026-10-04: + the full shell's 7 pieces)

        /// <summary>
        /// Lead (2026-10-03, the user: "make a simple settings menu "Armour Adjustment" then you can select the armour, move it xyz and also
        /// scale it, then save ... show/hide them individually"): the placement is per piece SLOT (a new helm keeps it), saved with the
        /// settings and sent to the other Somnium players. With "Mirror the shoulders" one shoulder moves the other the same way, mirrored.
        /// </summary>
        private void BuildArmour(int k)
        {
            // lead 2026-10-04: the page's title row gave its height to the top / bottom rows (the tab says "Armour")
            LiveSlider(k, "armour_piece", false, 0f, "Armour piece", 0f, Game.Armour.ArmourAdjust.Slots - 1, 1f, () => WowSettings.ArmourPiece,
                v => WowSettings.ArmourPiece = Mathf.Clamp(Mathf.RoundToInt(v), 0, Game.Armour.ArmourAdjust.Slots - 1), ArmourPieceLabel,
                "Which piece the rows below adjust: the helm and shoulders, and with Immersive armour \"Full\" the chest, waist, arms, hands, legs, feet and cloak. The fit belongs to the slot: new gear sits where you put the old.");
            LiveChecks2(k, "armour_checks_a", 0f,
                "Show this piece", () => WowSettings.ArmourShow[ArmourSel], v => ArmourSet(-1, v ? 1f : 0f), "Show or hide the selected piece on your avatar (everyone sees your choice).",
                "Hide avatar part", () => ArmourSel == 0 ? WowSettings.HelmHideHair : WowSettings.ArmourHideAvatar[ArmourSel],
                v => { if (ArmourSel == 0) WowSettings.HelmHideHair = v; else WowSettings.ArmourHideAvatar[ArmourSel] = v; },
                "Hides your avatar's own body under the selected piece, so only the WoW armour shows (no clipping). Helm: your head is hidden and a snapshot of your face is shown inside the helm. Chest, waist, arms, hands, legs, feet: that part of your avatar (skin and clothes). Shoulders and cloak: nothing. Everyone sees it.");   // lead 2026-10-04
            LiveChecks2(k, "armour_checks_b", 0f,
                "Mirror shoulders", () => WowSettings.ArmourMirrorShoulders, v => WowSettings.ArmourMirrorShoulders = v, "Adjusting one shoulder moves the other the same way, mirrored.",
                "Helm in photos", () => WowSettings.HelmInPhotos, v => WowSettings.HelmInPhotos = v,
                "VR and first person: your helm is hidden from your own eyes; with this on it still shows in Somnium camera pictures and in mirrors.");
            // lead 2026-10-04 (the user: "finer controls ... top start and bottom end"): two sliders a row - Left / right | Down / up,
            // Back / forward | Tilt, Size alone, Taper top | Taper bottom, then the full shell pieces' Top and Bottom edge rows. Rows that do
            // not apply to the selected piece are not shown (ArmourRows): Mirror shoulders / Helm in photos only for the helm and shoulders,
            // the tapers from the chest on, the edges for chest .. feet.
            LiveSliders2(k, "armour_xy", 0f,
                "Left / right", -0.30f, 0.30f, 0.005f, () => ArmourGet(0), v => ArmourSet(0, v), Cm,
                "Moves the selected piece to your right (+) or left (-). Arms, hands, legs and feet: + moves both sides outward.",
                "Down / up", -0.30f, 0.30f, 0.005f, () => ArmourGet(1), v => ArmourSet(1, v), Cm, "Moves the selected piece up (+) or down (-).");
            LiveSliders2(k, "armour_z_tilt", 0f,
                "Back / forward", -0.30f, 0.30f, 0.005f, () => ArmourGet(2), v => ArmourSet(2, v), Cm, "Moves the selected piece forward (+) or back (-).",
                "Tilt", -45f, 45f, 1f, () => ArmourGet(4), v => ArmourSet(4, v), v => (v > 0.01f ? "+" : "") + v.ToString("0", CultureInfo.InvariantCulture) + "\u00b0",
                "Tilts the selected piece: + leans its top forward, - back (the chest and waist about their middle, arms and legs about the joint; the cloak: + swings its hem back).");
            LiveSlider(k, "armour_size", false, 0f, "Size", 0.5f, 2f, 0.05f, () => ArmourGet(3), v => ArmourSet(3, v), Times,
                "Scales the selected piece (on top of Armour fit). Chest to feet: how thick it sits around you, about your body's middle (feet: the boot's length too). Cloak: the whole cloak.");
            LiveSliders2(k, "armour_taper", 0f,
                "Taper top", 0.5f, 1.5f, 0.02f, () => ArmourGet(5), v => ArmourSet(5, v), Times,
                "Full armour pieces: how wide the piece is at its top (chest and waist: their upper edge; arms and legs: at the shoulder / hip; cloak: the shoulders).",
                "Taper bottom", 0.5f, 1.5f, 0.02f, () => ArmourGet(6), v => ArmourSet(6, v), Times,
                "Full armour pieces: how wide the piece is at its bottom (chest and waist: their lower edge; arms and legs: at the wrist / ankle; cloak: the hem).");
            LiveSliders2(k, "armour_top", 0f,
                "Top forward", -0.15f, 0.15f, 0.005f, () => ArmourGet(7), v => ArmourSet(7, v), Cm,
                "Moves the TOP edge of the selected piece forward (+) or back (-); the piece leans between its top and bottom without turning (straightens a bent piece).",
                "Top up", -0.15f, 0.15f, 0.005f, () => ArmourGet(8), v => ArmourSet(8, v), Cm,
                "Moves the TOP edge of the selected piece up (+) or down (-): where the piece starts. The piece stretches to its bottom edge.");
            LiveSliders2(k, "armour_bottom", 0f,
                "Bottom forward", -0.15f, 0.15f, 0.005f, () => ArmourGet(9), v => ArmourSet(9, v), Cm,
                "Moves the BOTTOM edge of the selected piece forward (+) or back (-) - e.g. a robe's hem pulled in under you.",
                "Bottom up", -0.15f, 0.15f, 0.005f, () => ArmourGet(10), v => ArmourSet(10, v), Cm,
                "Moves the BOTTOM edge of the selected piece up (+) or down (-): where the piece ends (a robe's length).");
            LiveButtons(k, "armour_buttons", 2f, "Reset this piece", ArmourReset, "<size=14>Reset this piece</size>\nThe selected piece shown again, at its normal place and size (with mirroring, the other shoulder's place and size too).",
                "Save", ArmourSave, "<size=14>Save</size>\nKeeps this placement for these slots (it is also saved on its own as you change it).");
        }

        private static int ArmourSel { get { return Mathf.Clamp(WowSettings.ArmourPiece, 0, Game.Armour.ArmourAdjust.Slots - 1); } }

        /// <summary>The piece's name; the full shell's pieces say when Immersive armour is not on Full.</summary>
        private static string ArmourPieceLabel(float v)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(v), 0, Game.Armour.ArmourAdjust.Slots - 1);
            bool full = Game.Armour.ArmourGate.Mode(WowSettings.ImmersiveArmour) == 2;
            return ArmourPieceNames[i] + (i >= Game.Armour.ArmourAdjust.Chest && !full ? " (needs Full)" : "");
        }

        private static bool Shoulder(int i) { return i == Game.Armour.ArmourAdjust.LShoulder || i == Game.Armour.ArmourAdjust.RShoulder; }

        private static float ArmourGet(int axis)
        {
            int i = ArmourSel;
            return axis == 0 ? WowSettings.ArmourX[i] : axis == 1 ? WowSettings.ArmourY[i] : axis == 2 ? WowSettings.ArmourZ[i] : axis == 4 ? WowSettings.ArmourTilt[i]
                 : axis == 5 ? WowSettings.ArmourTaperTop[i] : axis == 6 ? WowSettings.ArmourTaperBottom[i]
                 : axis == 7 ? WowSettings.ArmourTopZ[i] : axis == 8 ? WowSettings.ArmourTopY[i] : axis == 9 ? WowSettings.ArmourBottomZ[i] : axis == 10 ? WowSettings.ArmourBottomY[i]
                 : WowSettings.ArmourSize[i];
        }

        /// <summary>axis 0 x, 1 y, 2 z, 3 size, -1 shown (v > 0.5); the other shoulder follows the placement mirrored (x negated) with
        /// "Mirror the shoulders" - never the show / hide (the user: "show/hide them individually").</summary>
        private static void ArmourSet(int axis, float v)
        {
            int i = ArmourSel;
            ArmourWrite(i, axis, v);
            if (Shoulder(i) && axis >= 0 && WowSettings.ArmourMirrorShoulders) ArmourWrite(3 - i, axis, axis == 0 ? -v : v);   // show / hide stays per piece
        }

        private static void ArmourWrite(int i, int axis, float v)
        {
            switch (axis)
            {
                case -1: WowSettings.ArmourShow[i] = v > 0.5f; break;
                case 0: WowSettings.ArmourX[i] = v; break;
                case 1: WowSettings.ArmourY[i] = v; break;
                case 2: WowSettings.ArmourZ[i] = v; break;
                case 3: WowSettings.ArmourSize[i] = v; break;
                case 4: WowSettings.ArmourTilt[i] = v; break;
                case 5: WowSettings.ArmourTaperTop[i] = v; break;
                case 6: WowSettings.ArmourTaperBottom[i] = v; break;
                case 7: WowSettings.ArmourTopZ[i] = v; break;      // lead 2026-10-04: the edges
                case 8: WowSettings.ArmourTopY[i] = v; break;
                case 9: WowSettings.ArmourBottomZ[i] = v; break;
                case 10: WowSettings.ArmourBottomY[i] = v; break;
            }
        }

        private void ArmourReset()
        {
            UiSound.Play(WowUiSound.ButtonClick);
            int i = ArmourSel;
            for (int pass = 0; pass < 2; pass++)
            {
                int k = pass == 0 ? i : 3 - i;
                if (pass == 1 && (!Shoulder(i) || !WowSettings.ArmourMirrorShoulders)) break;
                if (pass == 0) WowSettings.ArmourShow[k] = true;
                WowSettings.ArmourX[k] = WowSettings.ArmourY[k] = WowSettings.ArmourZ[k] = 0f; WowSettings.ArmourSize[k] = 1f; WowSettings.ArmourTilt[k] = 0f; WowSettings.ArmourTaperTop[k] = WowSettings.ArmourTaperBottom[k] = 1f;
                WowSettings.ArmourTopZ[k] = WowSettings.ArmourTopY[k] = WowSettings.ArmourBottomZ[k] = WowSettings.ArmourBottomY[k] = 0f;   // lead 2026-10-04
            }
            Changed();
            Ctx.Say(ArmourPieceNames[i] + " placement reset.", UiArt.InfoYellow);   // ours
        }

        private void ArmourSave()
        {
            UiSound.Play(WowUiSound.ButtonClick);
            Changed();
            var sb = new System.Text.StringBuilder("UI: Armour Adjustment saved -");
            for (int i = 0; i < Game.Armour.ArmourAdjust.Slots; i++) sb.Append(i == 0 ? " " : ", ").Append(ArmourPieceNames[i].ToLowerInvariant()).Append(' ').Append(Adj(i));
            WowLog.Info(sb.Append(WowSettings.HelmHideHair ? "; face in helm on." : ".").ToString());
            // lead 2026-10-04: written to the account's storage now (not 2 s later), so a fitting session is never lost
            bool stored = WowGame.SaveSettingsNow("the Armour page's Save button");
            Ctx.Say(stored ? "Armour placement saved." : "Armour placement kept - it is written to your account as soon as you are signed in.", UiArt.InfoYellow);   // ours
        }

        private static string Adj(int i)
        {
            return (WowSettings.ArmourShow[i] ? "" : "hidden, ") + "(" + WowSettings.ArmourX[i].ToString("0.000", CultureInfo.InvariantCulture) + ", " + WowSettings.ArmourY[i].ToString("0.000", CultureInfo.InvariantCulture)
                + ", " + WowSettings.ArmourZ[i].ToString("0.000", CultureInfo.InvariantCulture) + ") m x" + WowSettings.ArmourSize[i].ToString("0.00", CultureInfo.InvariantCulture)
                + (Mathf.Abs(WowSettings.ArmourTilt[i]) > 0.01f ? " tilt " + WowSettings.ArmourTilt[i].ToString("0", CultureInfo.InvariantCulture) + " deg" : "")
                + (Mathf.Abs(WowSettings.ArmourTaperTop[i] - 1f) > 0.001f || Mathf.Abs(WowSettings.ArmourTaperBottom[i] - 1f) > 0.001f
                    ? " taper " + WowSettings.ArmourTaperTop[i].ToString("0.00", CultureInfo.InvariantCulture) + "/" + WowSettings.ArmourTaperBottom[i].ToString("0.00", CultureInfo.InvariantCulture) : "")
                + (Mathf.Abs(WowSettings.ArmourTopZ[i]) + Mathf.Abs(WowSettings.ArmourTopY[i]) + Mathf.Abs(WowSettings.ArmourBottomZ[i]) + Mathf.Abs(WowSettings.ArmourBottomY[i]) > 1e-4f
                    ? " top " + Cm(WowSettings.ArmourTopZ[i]) + " fwd " + Cm(WowSettings.ArmourTopY[i]) + " up, bottom " + Cm(WowSettings.ArmourBottomZ[i]) + " fwd " + Cm(WowSettings.ArmourBottomY[i]) + " up" : "");
        }

        /// <summary>Lead 2026-10-04: the Armour page's rows for the selected piece (WowSettings.SetLive, quiet) - Mirror shoulders / Helm in
        /// photos for the helm and shoulders, the tapers for chest .. cloak, the top / bottom edges for chest .. feet.</summary>
        private static void ArmourRows(int sel)
        {
            WowSettings.SetLive("armour_checks_b", sel <= Game.Armour.ArmourAdjust.RShoulder, false);
            WowSettings.SetLive("armour_taper", sel >= Game.Armour.ArmourAdjust.Chest, false);
            bool ends = sel >= Game.Armour.ArmourAdjust.Chest && sel <= Game.Armour.ArmourAdjust.Feet;
            WowSettings.SetLive("armour_top", ends, false);
            WowSettings.SetLive("armour_bottom", ends, false);
        }

        private int _armourRowsFor = -1;

        private static string Cm(float v) { return (v > 0.0001f ? "+" : "") + (v * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " cm"; }

        /// <summary>Two checkboxes side by side in one row (lead 2026-10-04: the Armour page's height).</summary>
        private void LiveChecks2(int page, string key, float gap, string la, Func<bool> ga, Action<bool> sa, string ta, string lb, Func<bool> gb, Action<bool> sb, string tb)
        {
            RectTransform rt = UiKit.Rect(_page[page], "Row " + key, 0f, 0f, W, 28f);
            float half = RowW * 0.5f;
            _checks[page].Add(new UiCheck(rt, la, null, RowX, 0f, half - 4f, ga, sa, ta, Changed));
            _checks[page].Add(new UiCheck(rt, lb, null, RowX + half + 4f, 0f, half - 4f, gb, sb, tb, Changed));
            _live.Add(new LiveRow { Page = page, Key = key, VrOnly = false, Rt = rt, Height = 28f, Gap = gap });
            rt.gameObject.SetActive(false);
        }

        /// <summary>Two sliders side by side in one row (lead 2026-10-04: the Armour page's height).</summary>
        private void LiveSliders2(int page, string key, float gap,
            string la, float mina, float maxa, float stepa, Func<float> ga, Action<float> sa, Func<float, string> fa, string ta,
            string lb, float minb, float maxb, float stepb, Func<float> gb, Action<float> sb, Func<float, string> fb, string tb)
        {
            RectTransform rt = UiKit.Rect(_page[page], "Row " + key, 0f, 0f, W, UiSlider.RowHeight);
            float half = RowW * 0.5f;
            _sliders[page].Add(new UiSlider(rt, la, RowX, 0f, half - 6f, mina, maxa, stepa, ga, sa, fa, ta, Changed));
            _sliders[page].Add(new UiSlider(rt, lb, RowX + half + 6f, 0f, half - 6f, minb, maxb, stepb, gb, sb, fb, tb, Changed));
            _live.Add(new LiveRow { Page = page, Key = key, VrOnly = false, Rt = rt, Height = UiSlider.RowHeight, Gap = gap });
            rt.gameObject.SetActive(false);
        }

        /// <summary>A gold title line at the top of a page (lead 2026-10-03).</summary>
        private void LiveTitle(int page, string key, string text)
        {
            RectTransform rt = UiKit.Rect(_page[page], "Row " + key, 0f, 0f, W, 16f);   // 2026-10-04: 16 (the Tilt row needed the room)
            TextMeshProUGUI t = UiKit.Text(rt, "Title", text, 12f, UiArt.Gold, TextAlignmentOptions.Left, RowX, 0f, RowW, 16f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            _live.Add(new LiveRow { Page = page, Key = key, VrOnly = false, Rt = rt, Height = 16f, Gap = 0f });
            rt.gameObject.SetActive(false);
        }

        /// <summary>A row of two panel buttons (lead 2026-10-03).</summary>
        private void LiveButtons(int page, string key, float gap, string a, Action onA, string aTip, string b, Action onB, string bTip)
        {
            RectTransform rt = UiKit.Rect(_page[page], "Row " + key, 0f, 0f, W, 28f);
            UiButton x = UiKit.PanelButton(rt, a, RowX, 3f, 150f, 22f, onA);
            x.Tip = () => aTip;
            UiButton y = UiKit.PanelButton(rt, b, RowX + 160f, 3f, 110f, 22f, onB);
            y.Tip = () => bTip;
            _live.Add(new LiveRow { Page = page, Key = key, VrOnly = false, Rt = rt, Height = 28f, Gap = gap });
            rt.gameObject.SetActive(false);
        }

        private static readonly string[] ForcedMoveComfort = { "Dim", "Blink", "Smooth" };   // motion D-M1 (ours)

        /// <summary>Controls (ours): uicore's cursor rows (H2), spellfx's ground spells (13), talents' preview (8), motion's flight map and VR
        /// comfort rows (11).</summary>
        private void BuildControls(int k)
        {
            LiveCheck(k, "wow_cursor_art", false, 0f, "WoW mouse cursor", "desktop", () => WowSettings.WowCursorArt, v => WowSettings.WowCursorArt = v,
                "The game's own cursor: the pointer, the sword over enemies, the speech bubble over NPCs and the other WoW cursors.");
            LiveCheck(k, "wow_cursor_first_person", false, 0f, "WoW cursor in first person (hold Alt)", "desktop", () => WowSettings.WowCursorFirstPerson,
                v => WowSettings.WowCursorFirstPerson = v, "In first person, hold Alt (or open a window) to get a cursor for clicking the world and the interface.");
            LiveCheck(k, "ground_target_screen_centre", false, 0f, "Ground spells at the screen centre", "desktop", () => WowSettings.GroundTargetAtScreenCentre,
                v => WowSettings.GroundTargetAtScreenCentre = v, "Place ground spells (Blizzard, Consecration...) where the screen centre looks instead of at the mouse.");
            LiveCheck(k, "preview_talents", false, 0f, "Preview Talent Changes", null, () => WowSettings.PreviewTalents, v => WowSettings.PreviewTalents = v,
                "If enabled, you will be able to review your talents in the Talent Interface before spending your talent points. Always on in VR.");   // PREVIEW_TALENT_CHANGES 5718 / OPTION_PREVIEW_TALENT_CHANGES_DESCRIPTION 5254 + ours
            LiveCheck(k, "taxi_map_after_discovery", false, 0f, "Flight map after a discovery", "flights", () => WowSettings.TaxiMapAfterDiscovery,
                v => WowSettings.TaxiMapAfterDiscovery = v, "Open the flight map right after a new flight path is discovered.");
            LiveCheck(k, "vr_facing_assist", true, 0f, "Facing assist", "VR", () => WowSettings.VrFacingAssist, v => WowSettings.VrFacingAssist = v,
                "After \"Target needs to be in front of you.\" the next cast faces the target for you when you look within 100 degrees of it. Your view never turns.");
            LiveSlider(k, "vr_menu_style", true, 8f, "Spell menu style", 0f, 1f, 1f, () => WowSettings.VrMenuStyle, v => WowSettings.VrMenuStyle = Mathf.RoundToInt(v),
                v => Mathf.RoundToInt(v) == 1 ? "Gear Select" : "Twist wrist",
                "Twist wrist: grip and twist the hand to choose, push / pull for another bar. Gear Select: grip and MOVE the hand left / right to choose, up / down for another bar; release to cast.");   // lead 2026-10-10 (ours)
            LiveSlider(k, "wrist_sensitivity", true, 0f, "Spell menu twist sensitivity", 0f, 1f, 0.05f, () => WowSettings.WristSensitivity, v => WowSettings.WristSensitivity = v,
                v => Mathf.RoundToInt(v * 100f) + "%", "How far you twist your wrist to move around a spell menu: higher = less twist (50% = as before).");   // lead 2026-10-05 (ours)
            LiveCheck(k, "immersive_bow", true, 8f, "Immersive bow", "VR", () => WowSettings.ImmersiveBow, v => WowSettings.ImmersiveBow = v,
                "With a bow in one hand, the other hand takes an arrow from behind your back, puts it on the string and pulls: your shots show beside the bow, let go on the one you want. Up or down before pulling for another bar.");   // lead 2026-10-10 (ours)
            LiveSlider(k, "vr_forced_move", true, 8f, "Forced movement comfort (VR)", 0f, 2f, 1f, () => WowSettings.VrForcedMove,
                v => WowSettings.VrForcedMove = Mathf.RoundToInt(v), v => Named(ForcedMoveComfort, v),
                "Charges, knockbacks and fears move you: Dim darkens the view while you are moved, Blink makes it black, Smooth shows everything.");
        }

        /// <summary>Shows the live rows of the features that work (WowSettings.IsLive; VR-only rows in VR), stacks them on their page,
        /// gives a tab only to pages with something to show and places the tabs (one or two rows).</summary>
        private void Relayout()
        {
            bool vr = Ctx != null && Ctx.Vr;
            _liveShown = WowSettings.LiveVersion;
            _vrShown = vr ? 1 : 0;
            for (int p = 0; p < Pages; p++) { _liveY[p] = _liveBase[p]; _pageShown[p] = p < FixedPages || p == MacrosPage; }
            for (int i = 0; i < _live.Count; i++)
            {
                LiveRow r = _live[i];
                bool on = WowSettings.IsLive(r.Key) && (!r.VrOnly || vr);
                if (r.Rt.gameObject.activeSelf != on) r.Rt.gameObject.SetActive(on);
                if (!on) continue;
                if (_liveY[r.Page] > _liveBase[r.Page]) _liveY[r.Page] += r.Gap;
                UiKit.Move(r.Rt, 0f, _liveY[r.Page]);
                _liveY[r.Page] += r.Height;
                _pageShown[r.Page] = true;
            }
            int n = 0;
            for (int p = 0; p < Pages; p++) if (_pageShown[p]) n++;
            bool two = n > 6;
            int perRow = n > 8 ? 5 : 4;   // lead 2026-10-03: nine pages fit two rows of five
            float tw = two ? (n > 8 ? 67f : 84f) : n == 6 ? 56f : 67f;
            int slot = 0;
            for (int p = 0; p < Pages; p++)
            {
                UiButton b = _tab[p];
                if (b == null) continue;
                if (b.gameObject.activeSelf != _pageShown[p]) b.gameObject.SetActive(_pageShown[p]);
                if (!_pageShown[p]) continue;
                int col = two ? slot % perRow : slot, row = two ? slot / perRow : 0;
                RectTransform rt = (RectTransform)b.transform;
                UiKit.Move(rt, 20f + col * (tw + 2f), two ? 44f + row * 20f : 50f);
                UiKit.Size(rt, tw, 20f);
                if (_tabLit[p] != null) UiKit.Size(_tabLit[p].rectTransform, tw, 18f);
                if (b.Hilite != null) UiKit.Size(b.Hilite.rectTransform, tw, 18f);
                if (b.Label != null) UiKit.Size(b.Label.rectTransform, tw, 20f);
                slot++;
            }
            float shift = two ? 12f : 0f;   // ours: room for the second tab row
            if (_box != null) _box.Place(16f, 74f + shift, W - 32f, 392f - shift);
            for (int p = 0; p < Pages; p++) UiKit.Move(_page[p], 0f, shift);
            if (_current >= 0 && !_pageShown[_current]) Show(0);
        }

        private void Show(int k)
        {
            if (k < 0 || k >= Pages || !_pageShown[k]) k = 0;
            _current = k;
            for (int i = 0; i < Pages; i++)
            {
                _page[i].gameObject.SetActive(i == k);
                _tabLit[i].enabled = i == k && _tabLit[i].texture != null;
                _tab[i].LabelColor = i == k ? Color.white : UiArt.Gold;
                if (!_tab[i].Hovered && _tab[i].Label != null) _tab[i].Label.color = _tab[i].LabelColor;
            }
            RefreshPage();
        }

        private void RefreshPage()
        {
            if (_current < 0) return;
            List<UiCheck> c = _checks[_current];
            for (int i = 0; i < c.Count; i++) c[i].Refresh();
            List<UiSlider> s = _sliders[_current];
            for (int i = 0; i < s.Count; i++) s[i].Refresh();
        }

        protected override void OnClose() { Flush(); }

        /// <summary>Raises WowSettings.Changed for pending edits (every 0.15 s at most while sliding; at once otherwise).</summary>
        public void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            _lastNotify = Time.unscaledTime;
            WowSettings.NotifyChanged();
        }

        public override void Refresh(float now)
        {
            if (_roomArmed && Time.unscaledTime > _roomArmedUntil) { _roomArmed = false; _room.SetLabel(RoomLabel); }
            if (WowSettings.IsLive("armour_piece") && ArmourSel != _armourRowsFor) { _armourRowsFor = ArmourSel; ArmourRows(_armourRowsFor); }   // lead 2026-10-04
            if (_liveShown != WowSettings.LiveVersion || _vrShown != (Ctx != null && Ctx.Vr ? 1 : 0)) Relayout();   // iteration 11: live rows
            if (_dirty && now - _lastNotify >= 0.15f) Flush();
            RefreshPage();   // reads WowSettings every frame, so Defaults / a load from elsewhere show at once
            if (_fit != null) _fit.Tick(now, _current == 8, WowSettings.ArmourPiece);   // lead 2026-10-04
            if (_macros != null) { if (_current == MacrosPage) _macros.Refresh(now); else _macros.Closed(); }   // lead 2026-10-12
        }
    }
}
