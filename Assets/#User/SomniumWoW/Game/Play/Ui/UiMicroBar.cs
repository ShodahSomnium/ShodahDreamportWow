// iteration 11 (w0game, wave 0): moved verbatim from UiHud.cs (PLAN11 s3) - owner uicore from wave 1 on.
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>Character / Spellbook / Quest Log micro buttons (MainMenuBarMicroButtons.xml: 28x58, hit rect top inset 18) and the backpack button (37x37).</summary>
    public sealed class UiMicroBar
    {
        public const string MicroCharacterUp = @"Interface\Buttons\UI-MicroButtonCharacter-Up.blp";
        public const string MicroCharacterDown = @"Interface\Buttons\UI-MicroButtonCharacter-Down.blp";
        public const string MicroMainMenuUp = @"Interface\Buttons\UI-MicroButton-MainMenu-Up.blp";
        public const string MicroMainMenuDown = @"Interface\Buttons\UI-MicroButton-MainMenu-Down.blp";

        private readonly UiButton[] _b = new UiButton[5];
        private readonly UiWindowKind[] _kind = { UiWindowKind.Character, UiWindowKind.Spellbook, UiWindowKind.QuestLog, UiWindowKind.Bags, UiWindowKind.Settings };
        private readonly bool[] _down = new bool[5];
        private readonly UiContext _ctx;
        private readonly RawImage _portrait;
        private string _portraitPath;

        public UiMicroBar(Transform parent, UiContext ctx, float x, float y, float step, float bagX, float bagY, string[] keys)
        {
            _ctx = ctx;
            // CharacterMicroButton: UI-MicroButtonCharacter with the player's portrait 18x25 at TOP (0, -28) (MainMenuBarMicroButtons.xml);
            // MainMenuMicroButton opens the settings here (WoW's game menu leads to the options).
            string[] up = { MicroCharacterUp, UiArt.MicroSpellbookUp, UiArt.MicroQuestUp, MicroMainMenuUp };
            string[] dn = { MicroCharacterDown, UiArt.MicroSpellbookDown, UiArt.MicroQuestDown, MicroMainMenuDown };
            string[] names = { "Character Info", "Spellbook & Abilities", "Quest Log", "Settings" };
            int[] kindIndex = { 0, 1, 2, 4 };
            for (int i = 0; i < 4; i++)
            {
                // The hit rect is the lower 40 units (HitRectInsets top 18).
                UiButton b = UiKit.Button(parent, "Micro " + names[i], x + i * step, y + 18f, 28f, 40f);
                b.Art = UiKit.Pic(b.transform, "Up", UiArt.Tex(up[i]), 0f, -18f, 28f, 58f);
                b.ArtUp = UiArt.Tex(up[i]);
                b.Pushed = UiKit.Pic(b.transform, "Down", UiArt.Tex(dn[i]), 0f, -18f, 28f, 58f);
                b.Pushed.enabled = false;
                b.Hilite = UiKit.Pic(b.transform, "Highlight", UiArt.Add(UiArt.MicroHilight), 0f, -18f, 28f, 58f);
                b.Hilite.enabled = false;
                int ki = kindIndex[i];
                string tip = "<size=14>" + names[i] + "</size>" + (keys != null && ki < keys.Length ? " <color=#ffd100>(" + keys[ki] + ")</color>" : "");
                b.Tip = () => tip;
                b.Click = () => { if (_ctx.Toggle != null) _ctx.Toggle(_kind[ki]); };
                _b[ki] = b;
                if (i == 0)
                {
                    _portrait = UiKit.Pic(b.transform, "Portrait", null, 5f, -18f + 28f, 18f, 25f);
                    _portrait.enabled = false;
                }
            }
            UiButton bag = UiKit.Button(parent, "Backpack", bagX, bagY, 37f, 37f);
            bag.Art = UiKit.Pic(bag.transform, "Icon", UiArt.Tex(UiArt.Backpack), 0f, 0f, 37f, 37f);
            bag.Pushed = UiKit.Pic(bag.transform, "Pushed", UiArt.Tex(UiArt.QuickslotDepress), 0f, 0f, 37f, 37f);
            bag.Pushed.enabled = false;
            bag.Hilite = UiKit.Pic(bag.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 37f, 37f);
            bag.Hilite.enabled = false;
            bag.Checked = UiKit.Pic(bag.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 37f, 37f);
            bag.Checked.enabled = false;
            string bagTip = "<size=14>Backpack</size>" + (keys != null ? " <color=#ffd100>(" + keys[3] + ")</color>" : "");
            bag.Tip = () => bagTip;
            bag.Click = () => { if (_ctx.Toggle != null) _ctx.Toggle(UiWindowKind.Bags); };
            _b[3] = bag;
        }

        public void Refresh()
        {
            if (_ctx.IsOpen == null) return;
            PlayerState p = _ctx.P;
            if (p != null && _portrait != null)
            {
                // TemporaryPortrait-<Male|Female>-<ChrRaces client file string> (the voice setting picks the gender).
                string path = PortraitPath(p.Race, WowSettings.VoiceGender);
                if (!ReferenceEquals(path, _portraitPath) && path != _portraitPath)
                {
                    _portraitPath = path;
                    Texture2D t = path != null ? UiArt.Tex(path) : null;
                    _portrait.texture = t;
                    _portrait.color = Color.white;
                    _portrait.enabled = t != null;
                    _down[0] = !_down[0];   // re-apply the pushed look below
                }
            }
            for (int i = 0; i < 5; i++)
            {
                bool open = _ctx.IsOpen(_kind[i]);
                if (open == _down[i]) continue;
                _down[i] = open;
                UiButton b = _b[i];
                if (b == null) continue;
                if (i != 3)
                {
                    // SetButtonState("PUSHED", 1) while the frame is shown.
                    b.Art.enabled = !open;
                    b.Pushed.enabled = open;
                    if (i == 0 && _portrait != null)
                    {
                        // CharacterMicroButton_SetPushed / _SetNormal (MainMenuBarMicroButtons.lua:153-159).
                        if (open) UiKit.Coords(_portrait, 0.2666f, 0.8666f, 0f, 0.8333f);
                        else UiKit.Coords(_portrait, 0.2f, 0.8f, 0.0666f, 0.9f);
                        Color c = _portrait.color;
                        c.a = open ? 0.5f : 1f;
                        _portrait.color = c;
                    }
                }
                else b.Checked.enabled = open;
            }
        }

        /// <summary>Iteration 11 (uicore): the Character micro button's portrait for UiMicroMenu (the same rule as this bar's).</summary>
        public static string PortraitPathOf(int race, int gender) { return PortraitPath(race, gender); }

        private static readonly Dictionary<int, string>[] Portraits = { new Dictionary<int, string>(), new Dictionary<int, string>() };

        private static string PortraitPath(int race, int gender)
        {
            int g = gender == 1 ? 1 : 0;
            string path;
            if (Portraits[g].TryGetValue(race, out path)) return path;
            string key = null;
            try { key = UiArt.Files != null ? SomniumSpace.Worlds.SomniumWoW.Play.Rules.WowRaces.ClientFileString(UiArt.Files, race) : null; }
            catch (System.Exception) { key = null; }
            path = string.IsNullOrEmpty(key) ? null : @"Interface\CharacterFrame\TemporaryPortrait-" + (g == 1 ? "Female-" : "Male-") + key + ".blp";
            Portraits[g][race] = path;
            return path;
        }
    }
}
