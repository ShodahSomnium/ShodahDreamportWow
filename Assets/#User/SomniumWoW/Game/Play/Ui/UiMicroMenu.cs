// iteration 11 - owner uicore (proposal-uicore.md 5.4.5): WoW's micro buttons built from the window registry.
// Desktop: the 10 MainMenuBarMicroButtons (MainMenuBarMicroButtons.xml: 28 x 58, HitRectInsets top 18, CharacterMicroButton BOTTOMLEFT
// (552, 2) of the 1024 x 53 main menu bar, every next one at the previous BOTTOMRIGHT (-3, 0) = 25 apart; order Character, Spellbook,
// Talent, Achievement, QuestLog, Socials, PVP, LFD, MainMenu, Help) + the World Map button (MicroOrder 10, ours, after them) + npc's bag
// bar at BOTTOMRIGHT (-6, 2) (UiBagBar; until it builds anything an interim backpack button). Art LoadMicroButtonTextures(name) =
// Interface\Buttons\UI-MicroButton-<name>-Up / -Down / -Disabled + UI-MicroButton-Hilight (MainMenuBarMicroButtons.lua:1-9); Character
// and PVP use UI-MicroButtonCharacter-Up / -Down with the portrait (18 x 25 at TOP (0,-28)) / UI-PVP-<faction> (36 x 36 at TOP (6,-30)).
// Disabled under the spec's MinLevel: alpha 0.5 + FEATURE_BECOMES_AVAILABLE_AT_LEVEL in red (:12-23, GlobalStrings :3673). Pushed while
// its window is open (UpdateMicroButtons). Tooltip "<Title> (<key>)" (MicroButtonTooltipText :11-17). A Hidden spec has no button
// (PLAN11 s4: nothing that does nothing); the desktop keeps WoW's fixed slots, the VR palm row packs the shown ones.
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiMicroMenu
    {
        public const float Step = 25f;               // 28 wide, each anchored -3 into the previous (MainMenuBarMicroButtons.xml:33-445)
        public const float FirstX = 552f;            // CharacterMicroButton BOTTOMLEFT (552, 2)
        public const string CharacterUp = @"Interface\Buttons\UI-MicroButtonCharacter-Up.blp";
        public const string CharacterDown = @"Interface\Buttons\UI-MicroButtonCharacter-Down.blp";

        private sealed class Btn
        {
            public int Order;
            public UiWindowSpec Spec;
            public UiButton B;
            public RawImage Up, Down, Disabled, Portrait, Faction;
            public bool Shown = true, Open, Off;
            public int OffLevel = -1;
            public string PortraitPath;
            public WowFaction FactionShown = (WowFaction)255;
        }

        private readonly List<Btn> _buttons = new List<Btn>();
        private readonly UiContext _ctx;
        private readonly bool _palm;
        private readonly float _x, _y, _step;
        private int _version = -1;
        private readonly UiBagBar _bagBar;
        private readonly UiButton _backpack;
        private bool _backpackOpen;

        /// <summary>Desktop: the micro buttons in the 1024x53 main menu bar (x 552 + 25 i) + the World Map button + the bag bar.</summary>
        public UiMicroMenu(Transform parent, UiContext ctx) : this(parent, ctx, false, FirstX, 53f - 2f - 58f, Step, 10)
        {
            // npc's request: UiBagBar at BOTTOMRIGHT (-6, 2): a 190 x 37 root whose right 37 is the backpack, the bag slots leftwards
            // (MainMenuBarBagButtons.xml:17-18, :77-79, :130-160). Until npcui's bar builds anything, an interim backpack button.
            RectTransform barRoot = UiKit.Rect(parent, "Bag bar", 1024f - 6f - 190f, 53f - 2f - 37f, 190f, 37f);
            try { _bagBar = new UiBagBar(barRoot, ctx); }
            catch (System.Exception e) { WowLog.Error("UI: the bag bar failed to build: " + e); _bagBar = null; }
            if (barRoot.childCount == 0)
            {
                _bagBar = null;
                UiButton bag = UiKit.Button(barRoot, "Backpack", 190f - 37f, 0f, 37f, 37f);
                bag.Art = UiKit.Pic(bag.transform, "Icon", UiArt.Tex(UiArt.Backpack), 0f, 0f, 37f, 37f);
                bag.Pushed = UiKit.Pic(bag.transform, "Pushed", UiArt.Tex(UiArt.QuickslotDepress), 0f, 0f, 37f, 37f);
                bag.Pushed.enabled = false;
                bag.Hilite = UiKit.Pic(bag.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 37f, 37f);
                bag.Hilite.enabled = false;
                bag.Checked = UiKit.Pic(bag.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 37f, 37f);
                bag.Checked.enabled = false;
                bag.Tip = () => "<size=14>" + UiGlobalStrings.G("BACKPACK_TOOLTIP", "Backpack") + "</size> <color=#ffd100>(" + UiRegistry.KeyText(UiRegistry.Of(UiWindowKind.Bags)) + ")</color>";   // GlobalStrings.lua:670
                bag.Click = () => { if (_ctx.Toggle != null) _ctx.Toggle(UiWindowKind.Bags); };
                _backpack = bag;
            }
        }

        private UiMicroMenu(Transform parent, UiContext ctx, bool palm, float x, float y, float step, int maxOrder)
        {
            _ctx = ctx;
            _palm = palm;
            _x = x;
            _y = y;
            _step = step;
            for (int order = 0; order <= maxOrder; order++)
            {
                if (palm && order == 9) continue;   // Help is not offered in VR (proposal 5.4.5)
                var m = new Btn { Order = order };
                // The hit rect is the lower 40 units (HitRectInsets top 18); the art hangs 18 above it.
                float bx = x + (palm ? 0f : order * step);
                m.B = UiKit.Button(parent, "Micro " + order, bx, y + 18f, 28f, 40f);
                m.Up = UiKit.Pic(m.B.transform, "Up", null, 0f, -18f, 28f, 58f);
                m.Down = UiKit.Pic(m.B.transform, "Down", null, 0f, -18f, 28f, 58f);
                m.Down.enabled = false;
                m.Disabled = UiKit.Pic(m.B.transform, "Disabled", null, 0f, -18f, 28f, 58f);
                m.Disabled.enabled = false;
                m.B.Hilite = UiKit.Pic(m.B.transform, "Highlight", UiArt.Add(UiArt.MicroHilight), 0f, -18f, 28f, 58f);
                m.B.Hilite.enabled = false;
                m.Portrait = UiKit.Pic(m.B.transform, "Portrait", null, 5f, -18f + 28f, 18f, 25f);
                m.Portrait.enabled = false;
                m.Faction = UiKit.Pic(m.B.transform, "Faction", null, 14f + 6f - 18f, -18f + 30f, 36f, 36f);
                m.Faction.enabled = false;
                Btn me = m;
                m.B.Click = () => Click(me);
                m.B.Tip = () => Tip(me);
                _buttons.Add(m);
            }
            Bind();
        }

        /// <summary>VR: row 1 of the palm menu (Character ... Game Menu; Help omitted) at (x, y), step apart (hook H7 in UiVrHud).</summary>
        public static UiMicroMenu BuildPalmRow(Transform parent, UiContext ctx, float x, float y, float step)
        {
            return new UiMicroMenu(parent, ctx, true, x, y, step, 8);
        }

        private void Click(Btn m)
        {
            if (m.Spec == null || m.Off || _ctx.Toggle == null) return;
            _ctx.Toggle(m.Spec.Kind);
        }

        private string Tip(Btn m)
        {
            if (m.Spec == null) return null;
            string title = UiRegistry.TitleOf(m.Spec);
            string key = UiRegistry.KeyText(m.Spec);
            if (m.Spec.Kind == UiWindowKind.GameMenu) key = "Escape";
            string t = "<size=14>" + title + "</size>" + (!_palm && key.Length > 0 ? " <color=#ffd100>(" + key + ")</color>" : "");
            if (m.Off && m.OffLevel > 0)
                t += "\n<color=#ff2020>" + UiGlobalStrings.FormatOr("FEATURE_BECOMES_AVAILABLE_AT_LEVEL", "This feature becomes available at level %d.", m.OffLevel) + "</color>";   // GlobalStrings.lua:3673
            return t;
        }

        /// <summary>Which spec each slot shows (the registry can change at runtime: Register again, the later wins).</summary>
        private void Bind()
        {
            _version = UiRegistry.Version;
            int packed = 0;
            for (int i = 0; i < _buttons.Count; i++)
            {
                Btn m = _buttons[i];
                UiWindowSpec spec = null;
                for (int k = 0; k < UiRegistry.All.Count && spec == null; k++)
                {
                    UiWindowSpec s = UiRegistry.All[k];
                    if (s != null && s.MicroOrder == m.Order && !s.Hidden) spec = s;
                }
                m.Spec = spec;
                bool shown = spec != null && (!_palm || spec.Palm || spec.MicroOrder >= 0);
                m.Shown = shown;
                if (m.B.gameObject.activeSelf != shown) m.B.gameObject.SetActive(shown);
                if (!shown) continue;
                if (_palm) UiKit.Move(m.B.Rt, _x + packed * _step, _y + 18f);
                else if (m.Order == 10)
                {
                    // ours: the World Map button right after the last WoW button shown (Help is not built: it takes Help's place).
                    bool help = UiRegistry.Visible(UiWindowKind.Help);
                    UiKit.Move(m.B.Rt, _x + (help ? 10 : 9) * _step, _y + 18f);
                }
                packed++;
                string up, down, off = null;
                if (spec.Micro == "#Character" || spec.Micro == "#PVP") { up = CharacterUp; down = CharacterDown; }
                else
                {
                    up = @"Interface\Buttons\UI-MicroButton-" + spec.Micro + "-Up.blp";
                    down = @"Interface\Buttons\UI-MicroButton-" + spec.Micro + "-Down.blp";
                    off = @"Interface\Buttons\UI-MicroButton-" + spec.Micro + "-Disabled.blp";
                }
                m.Up.texture = UiArt.Tex(up);
                m.Up.color = Color.white;
                m.Down.texture = UiArt.Tex(down);
                m.Down.color = Color.white;
                m.Disabled.texture = off != null ? UiArt.Tex(off) : null;
                m.Disabled.color = off != null ? Color.white : new Color(0f, 0f, 0f, 0f);
                m.B.ArtUp = null;
                m.Open = !m.Open;   // re-apply the pushed look on the next Refresh
                m.OffLevel = -1;
                m.PortraitPath = null;
                m.FactionShown = (WowFaction)255;
            }
        }

        public void Refresh()
        {
            if (UiRegistry.Version != _version) Bind();
            PlayerState p = _ctx.P;
            for (int i = 0; i < _buttons.Count; i++)
            {
                Btn m = _buttons[i];
                if (!m.Shown || m.Spec == null) continue;
                UiWindowSpec s = m.Spec;
                // Disabled by level (Constants.lua:129-131 SHOW_TALENT_LEVEL 10, SHOW_PVP_LEVEL 10, SHOW_LFD_LEVEL 15 - the spec's MinLevel).
                int need = p != null && s.MinLevel > 0 && p.Level < s.MinLevel ? s.MinLevel : 0;
                bool off = need > 0;
                if (off != m.Off || need != m.OffLevel)
                {
                    m.Off = off;
                    m.OffLevel = need;
                    Color a = new Color(1f, 1f, 1f, off ? 0.5f : 1f);
                    m.Up.color = a;
                    if (m.Disabled.texture != null) { m.Disabled.enabled = off; m.Up.enabled = !off; }
                    m.Open = !m.Open;
                }
                bool open = _ctx.IsOpen != null && _ctx.IsOpen(s.Kind);
                if (open != m.Open)
                {
                    m.Open = open;
                    // SetButtonState("PUSHED", 1) while the frame is shown.
                    bool disabledArt = m.Off && m.Disabled.texture != null;
                    m.Up.enabled = !open && !disabledArt;
                    m.Down.enabled = open && !m.Off;
                    if (m.Portrait.enabled)
                    {
                        // CharacterMicroButton_SetPushed / _SetNormal (MainMenuBarMicroButtons.lua:153-159).
                        if (open) UiKit.Coords(m.Portrait, 0.2666f, 0.8666f, 0f, 0.8333f);
                        else UiKit.Coords(m.Portrait, 0.2f, 0.8f, 0.0666f, 0.9f);
                        Color c = m.Portrait.color;
                        c.a = open ? 0.5f : 1f;
                        m.Portrait.color = c;
                    }
                }
                if (p == null) continue;
                if (s.Micro == "#Character")
                {
                    string path = UiMicroBar.PortraitPathOf(p.Race, WowSettings.VoiceGender);
                    if (path != m.PortraitPath)
                    {
                        m.PortraitPath = path;
                        Texture2D t = path != null ? UiArt.Tex(path) : null;
                        m.Portrait.texture = t;
                        m.Portrait.color = Color.white;
                        m.Portrait.enabled = t != null;
                        UiKit.Coords(m.Portrait, 0.2f, 0.8f, 0.0666f, 0.9f);
                        m.Open = !m.Open;
                    }
                }
                else if (s.Micro == "#PVP" && p.Faction != m.FactionShown)
                {
                    m.FactionShown = p.Faction;
                    Texture2D t = UiArt.Tex(@"Interface\TargetingFrame\UI-PVP-" + (p.Faction == WowFaction.Horde ? "Horde" : "Alliance") + ".blp");
                    m.Faction.texture = t;
                    m.Faction.color = Color.white;
                    m.Faction.enabled = t != null;
                }
            }
            if (_bagBar != null && p != null)
            {
                try { _bagBar.Refresh(p, Time.unscaledTime); }
                catch (System.Exception e) { WowLog.Warn("UI: the bag bar refresh failed: " + e.Message); }
            }
            if (_backpack != null && _ctx.IsOpen != null)
            {
                bool open = _ctx.IsOpen(UiWindowKind.Bags);
                if (open != _backpackOpen) { _backpackOpen = open; _backpack.Checked.enabled = open && _backpack.Checked.texture != null; }
            }
        }

        /// <summary>The micro buttons shown now (logs).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _buttons.Count; i++)
            {
                Btn m = _buttons[i];
                if (!m.Shown || m.Spec == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(UiRegistry.TitleOf(m.Spec));
            }
            return sb.ToString();
        }
    }
}
