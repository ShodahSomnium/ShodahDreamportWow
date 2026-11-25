using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Everything the game UI draws with, read from the player's install at runtime: paths (each checked on the real files by the
    /// harness probe UiArt, reports2/ui.md), texcoords and colours taken from the client's own FrameXML (file:line in the
    /// comments), textures (WowTextures, UI options), icons (loaded on a pool thread, created a few per frame), the three WoW fonts
    /// and the shared materials (outlined text, always-on-top for the arm panels).
    /// </summary>
    public static class UiArt
    {
        // ---------------------------------------------------------------- paths (probe UiArt: all exist)
        public const string Quickslot = @"Interface\Buttons\UI-Quickslot2.blp";
        public const string QuickslotDepress = @"Interface\Buttons\UI-Quickslot-Depress.blp";
        public const string QuickslotRed = @"Interface\Buttons\UI-QuickslotRed.blp";
        public const string ButtonHilightSquare = @"Interface\Buttons\ButtonHilight-Square.blp";
        public const string CheckButtonHilight = @"Interface\Buttons\CheckButtonHilight.blp";
        public const string PanelButtonUp = @"Interface\Buttons\UI-Panel-Button-Up.blp";
        public const string PanelButtonDown = @"Interface\Buttons\UI-Panel-Button-Down.blp";
        public const string PanelButtonHighlight = @"Interface\Buttons\UI-Panel-Button-Highlight.blp";
        public const string PanelButtonDisabled = @"Interface\Buttons\UI-Panel-Button-Disabled.blp";
        public const string CloseUp = @"Interface\Buttons\UI-Panel-MinimizeButton-Up.blp";
        public const string CloseDown = @"Interface\Buttons\UI-Panel-MinimizeButton-Down.blp";
        public const string CloseHighlight = @"Interface\Buttons\UI-Panel-MinimizeButton-Highlight.blp";
        public const string PrevUp = @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Up.blp";
        public const string PrevDown = @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Down.blp";
        public const string PrevDisabled = @"Interface\Buttons\UI-SpellbookIcon-PrevPage-Disabled.blp";
        public const string NextUp = @"Interface\Buttons\UI-SpellbookIcon-NextPage-Up.blp";
        public const string NextDown = @"Interface\Buttons\UI-SpellbookIcon-NextPage-Down.blp";
        public const string NextDisabled = @"Interface\Buttons\UI-SpellbookIcon-NextPage-Disabled.blp";
        public const string ScrollUpUp = @"Interface\Buttons\UI-ScrollBar-ScrollUpButton-Up.blp";
        public const string ScrollUpDisabled = @"Interface\Buttons\UI-ScrollBar-ScrollUpButton-Disabled.blp";
        public const string ScrollDownUp = @"Interface\Buttons\UI-ScrollBar-ScrollDownButton-Up.blp";
        public const string ScrollDownDisabled = @"Interface\Buttons\UI-ScrollBar-ScrollDownButton-Disabled.blp";
        public const string ScrollHighlight = @"Interface\Buttons\UI-ScrollBar-ScrollUpButton-Highlight.blp";
        public const string EmptySlot = @"Interface\Buttons\UI-EmptySlot.blp";
        public const string MouseHilight = @"Interface\Buttons\UI-Common-MouseHilight.blp";
        public const string MicroSpellbookUp = @"Interface\Buttons\UI-MicroButton-Spellbook-Up.blp";
        public const string MicroSpellbookDown = @"Interface\Buttons\UI-MicroButton-Spellbook-Down.blp";
        public const string MicroQuestUp = @"Interface\Buttons\UI-MicroButton-Quest-Up.blp";
        public const string MicroQuestDown = @"Interface\Buttons\UI-MicroButton-Quest-Down.blp";
        public const string MicroCharacterUp = @"Interface\Buttons\UI-MicroButton-CharacterNightElf-Up.blp";
        public const string MicroCharacterDown = @"Interface\Buttons\UI-MicroButton-CharacterNightElf-Down.blp";
        public const string MicroHilight = @"Interface\Buttons\UI-MicroButton-Hilight.blp";
        public const string Backpack = @"Interface\Buttons\Button-Backpack-Up.blp";
        public const string DialogButtonUp = @"Interface\Buttons\UI-DialogBox-Button-Up.blp";
        public const string DialogButtonDown = @"Interface\Buttons\UI-DialogBox-Button-Down.blp";
        public const string DialogButtonHighlight = @"Interface\Buttons\UI-DialogBox-Button-Highlight.blp";

        public const string TargetingFrame = @"Interface\TargetingFrame\UI-TargetingFrame.blp";
        public const string LevelBackground = @"Interface\TargetingFrame\UI-TargetingFrame-LevelBackground.blp";
        public const string StatusBar = @"Interface\TargetingFrame\UI-StatusBar.blp";
        public const string ClassCircles = @"Interface\TargetingFrame\UI-Classes-Circles.blp";
        public const string TargetDead = @"Interface\TargetingFrame\TargetDead.blp";
        public const string PortraitMonster = @"Interface\CharacterFrame\TemporaryPortrait-Monster.blp";
        public const string PortraitNpc = @"Interface\CharacterFrame\TemporaryPortrait.blp";
        public const string StateIcon = @"Interface\CharacterFrame\UI-StateIcon.blp";
        public const string ComboPoint = @"Interface\ComboFrame\ComboPoint.blp";
        public const string CastBorder = @"Interface\CastingBar\UI-CastingBar-Border.blp";
        public const string CastSpark = @"Interface\CastingBar\UI-CastingBar-Spark.blp";
        public const string CastFlash = @"Interface\CastingBar\UI-CastingBar-Flash.blp";
        public const string MainMenuBar = @"Interface\MainMenuBar\UI-MainMenuBar-Dwarf.blp";
        public const string EndCap = @"Interface\MainMenuBar\UI-MainMenuBar-EndCap-Dwarf.blp";

        public const string QuestGreeting = @"Interface\QuestFrame\UI-QuestGreeting-";     // TopLeft TopRight BotLeft BotRight
        public const string QuestLog = @"Interface\QuestFrame\UI-QuestLog-";               // TopLeft TopRight BotLeft BotRight
        public const string SpellbookPanel = @"Interface\Spellbook\UI-SpellbookPanel-";     // TopLeft TopRight BotLeft BotRight
        public const string CharacterTab = @"Interface\PaperDollInfoFrame\UI-Character-CharacterTab-"; // L1 R1 BottomLeft BottomRight
        public const string QuestItemNameFrame = @"Interface\QuestFrame\UI-QuestItemNameFrame.blp";
        public const string QuestItemHighlight = @"Interface\QuestFrame\UI-QuestItemHighlight.blp";
        public const string QuestTitleHighlight = @"Interface\QuestFrame\UI-QuestTitleHighlight.blp";
        public const string QuestLogTitleHighlight = @"Interface\QuestFrame\UI-QuestLogTitleHighlight.blp";
        public const string HorizontalBreak = @"Interface\QuestFrame\UI-HorizontalBreak.blp";
        public const string QuestLogBookIcon = @"Interface\QuestFrame\UI-QuestLog-BookIcon.blp";
        public const string AvailableQuestIcon = @"Interface\GossipFrame\AvailableQuestIcon.blp";
        public const string ActiveQuestIcon = @"Interface\GossipFrame\ActiveQuestIcon.blp";
        public const string IncompleteQuestIcon = @"Interface\GossipFrame\IncompleteQuestIcon.blp";
        public const string SpellbookIcon = @"Interface\Spellbook\Spellbook-Icon.blp";
        public const string BackpackBackground = @"Interface\ContainerFrame\UI-BackpackBackground.blp";
        public const string LootPanel = @"Interface\LootFrame\UI-LootPanel.blp";
        public const string MoneyIcons = @"Interface\MoneyFrame\UI-MoneyIcons.blp";
        public const string DialogBackground = @"Interface\DialogFrame\UI-DialogBox-Background.blp";
        public const string DialogBorder = @"Interface\DialogFrame\UI-DialogBox-Border.blp";
        public const string TooltipBackground = @"Interface\Tooltips\UI-Tooltip-Background.blp";
        public const string TooltipBorder = @"Interface\Tooltips\UI-Tooltip-Border.blp";
        public const string QuestionMark = @"Interface\Icons\INV_Misc_QuestionMark.blp";
        public const string CoinIcon = @"Interface\Icons\INV_Misc_Coin_01.blp";
        public const string SlotPrefix = @"Interface\PaperDoll\UI-PaperDoll-Slot-";

        public const string FontFriz = @"Fonts\FRIZQT__.TTF";
        public const string FontMorpheus = @"Fonts\MORPHEUS.ttf";
        public const string FontArialN = @"Fonts\ARIALN.TTF";

        /// <summary>Paper doll slot backgrounds by EquipSlot (GetInventorySlotInfo names; Back uses the chest picture - there is no -Back file).</summary>
        public static readonly string[] SlotBackground =
        {
            "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrists", "Hands",
            "Finger", "Finger", "Trinket", "Trinket", "Chest", "MainHand", "SecondaryHand", "Ranged", "Tabard"
        };

        // ---------------------------------------------------------------- colours (FrameXML)
        public static readonly Color Gold = new Color(1f, 0.82f, 0f);                 // NORMAL_FONT_COLOR
        public static readonly Color White = Color.white;                             // HIGHLIGHT_FONT_COLOR
        public static readonly Color Grey = new Color(0.5f, 0.5f, 0.5f);              // GRAY_FONT_COLOR
        public static readonly Color ErrorRed = new Color(1f, 0.1f, 0.1f);            // RED_FONT_COLOR
        public static readonly Color InfoYellow = new Color(1f, 1f, 0f);              // YELLOW_FONT_COLOR
        public static readonly Color QuestText = new Color(0f, 0f, 0f);               // QuestFont (FontStyles.xml:185)
        public static readonly Color QuestTitle = new Color(0.18f, 0.12f, 0.06f);     // "Parchment" title colour (Constants.lua:40)
        public static readonly Color XpPurple = new Color(0.58f, 0f, 0.55f);          // MainMenuBar.xml:168
        public static readonly Color HealthGreen = new Color(0f, 1f, 0f);
        public static readonly Color CastYellow = new Color(1f, 0.7f, 0f);            // CastingBarFrame.xml BarColor
        public static readonly Color TooltipBg = new Color(0.09f, 0.09f, 0.19f, 1f);  // GameTooltip.lua:4

        /// <summary>PowerBarColor (UnitFrame.lua:3-9).</summary>
        public static Color PowerColor(PowerType p)
        {
            switch (p)
            {
                case PowerType.Rage: return new Color(1f, 0f, 0f);
                case PowerType.Focus: return new Color(1f, 0.5f, 0.25f);
                case PowerType.Energy: return new Color(1f, 1f, 0f);
                case PowerType.Happiness: return new Color(0f, 1f, 1f);
                case PowerType.RunicPower: return new Color(0f, 0.82f, 1f);
                default: return new Color(0f, 0f, 1f);
            }
        }

        /// <summary>QuestDifficultyColors (Constants.lua:403-409) by EntityState.ConColor 0 grey .. 4 red.</summary>
        public static Color ConColor(int con)
        {
            switch (con)
            {
                case 0: return new Color(0.5f, 0.5f, 0.5f);
                case 1: return new Color(0.25f, 0.75f, 0.25f);
                case 2: return new Color(1f, 1f, 0f);
                case 3: return new Color(1f, 0.5f, 0.25f);
                default: return new Color(1f, 0.1f, 0.1f);
            }
        }

        /// <summary>Level difference -> the same colours (GetQuestDifficultyColor, UIParent.lua:3355-3371, simplified gray band).</summary>
        public static Color DifficultyColor(int level, int playerLevel)
        {
            int d = level - playerLevel;
            if (d >= 5) return ConColor(4);
            if (d >= 3) return ConColor(3);
            if (d >= -2) return ConColor(2);
            if (level > WowGrayLevel(playerLevel)) return ConColor(1);
            return ConColor(0);
        }

        private static int WowGrayLevel(int playerLevel)
        {
            // AzerothCore Formulas.h GetGrayLevel: level <= 5 -> 0, 6..39 -> level - 5 - level/10 (quoted in reports2/content.md).
            return playerLevel <= 5 ? 0 : playerLevel - 5 - playerLevel / 10;
        }

        /// <summary>UnitSelectionColor (engine): hostile red, neutral yellow, friendly green.</summary>
        public static Color ReactionColor(Reaction r)
        {
            return r == Reaction.Hostile ? new Color(1f, 0f, 0f) : r == Reaction.Neutral ? new Color(1f, 1f, 0f) : new Color(0f, 1f, 0f);
        }

        /// <summary>Item quality colours (GetItemQualityColor, engine).</summary>
        public static Color QualityColor(int q)
        {
            switch (q)
            {
                case 0: return new Color(0.616f, 0.616f, 0.616f);
                case 2: return new Color(0.118f, 1f, 0f);
                case 3: return new Color(0f, 0.439f, 0.867f);
                case 4: return new Color(0.639f, 0.208f, 0.933f);
                case 5: return new Color(1f, 0.502f, 0f);
                default: return Color.white;
            }
        }

        public static string QualityHex(int q)
        {
            switch (q)
            {
                case 0: return "#9d9d9d";
                case 2: return "#1eff00";
                case 3: return "#0070dd";
                case 4: return "#a335ee";
                case 5: return "#ff8000";
                default: return "#ffffff";
            }
        }

        /// <summary>CLASS_ICON_TCOORDS (Constants.lua:91-98) on UI-Classes-Circles: left, right, top, bottom.</summary>
        public static Vector4 ClassCoords(WowClass c)
        {
            switch (c)
            {
                case WowClass.Warrior: return new Vector4(0f, 0.25f, 0f, 0.25f);
                case WowClass.Mage: return new Vector4(0.25f, 0.49609375f, 0f, 0.25f);
                case WowClass.Rogue: return new Vector4(0.49609375f, 0.7421875f, 0f, 0.25f);
                case WowClass.Druid: return new Vector4(0.7421875f, 0.98828125f, 0f, 0.25f);
                case WowClass.Hunter: return new Vector4(0f, 0.25f, 0.25f, 0.5f);
                case WowClass.Shaman: return new Vector4(0.25f, 0.49609375f, 0.25f, 0.5f);
                case WowClass.Priest: return new Vector4(0.49609375f, 0.7421875f, 0.25f, 0.5f);
                case WowClass.Warlock: return new Vector4(0.7421875f, 0.98828125f, 0.25f, 0.5f);
                case WowClass.Paladin: return new Vector4(0f, 0.25f, 0.5f, 0.75f);
                default: return new Vector4(0.25f, 0.49609375f, 0.5f, 0.75f);
            }
        }

        public static string ClassName(WowClass c)
        {
            return c == WowClass.DeathKnight ? "Death Knight" : c.ToString();
        }

        // ---------------------------------------------------------------- textures

        private static WowFiles _files;
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Additive = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();
        private static readonly object IconLock = new object();
        private static readonly HashSet<string> IconPending = new HashSet<string>();
        private static readonly List<KeyValuePair<string, WowPreparedTexture>> IconReady = new List<KeyValuePair<string, WowPreparedTexture>>();
        private static int _iconsLoaded, _iconsMissing;

        public static WowFiles Files
        {
            get { return _files; }
            set
            {
                if (!ReferenceEquals(_files, value))
                {
                    _files = value;
                    Textures.Clear();
                    Additive.Clear();
                    lock (IconLock) { Icons.Clear(); IconPending.Clear(); IconReady.Clear(); }
                }
            }
        }

        public static string IconStats { get { return _iconsLoaded + " icons loaded, " + _iconsMissing + " missing"; } }

        /// <summary>A UI texture (Clamp), cached; null when the files are not open or the file is missing (logged once by WowTextures).</summary>
        public static Texture2D Tex(string path)
        {
            Texture2D t;
            if (Textures.TryGetValue(path, out t)) return t;
            if (_files == null) return null;
            try
            {
                t = WowTextures.Get(_files, path, WowTextureOptions.Ui);
                if (ReferenceEquals(t, WowTextures.Missing)) t = null;
            }
            catch (Exception e)
            {
                WowLog.Warn("UI: texture " + path + ": " + e.Message);
                t = null;
            }
            Textures[path] = t;
            return t;
        }

        /// <summary>
        /// A texture that FrameXML draws with alphaMode="ADD" (highlights, sparks: DXT1 without alpha). uGUI's default material
        /// blends by alpha, so the picture becomes colour / max-channel with alpha = max-channel: over the art it reads as the
        /// additive glow (the same trick as WowUi.Highlight).
        /// </summary>
        public static Texture2D Add(string path)
        {
            Texture2D t;
            if (Additive.TryGetValue(path, out t)) return t;
            t = null;
            if (_files != null)
            {
                try
                {
                    byte[] blp = _files.Read(path);
                    if (blp != null)
                    {
                        BlpImage img = BlpImage.Decode(blp);
                        byte[] px = img.GetRgba32(0);
                        for (int i = 0; i < px.Length; i += 4)
                        {
                            int m = (int)Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                            m = m * px[i + 3] / 255;
                            if (m == 0) { px[i + 3] = 0; continue; }
                            int mx = Math.Max(1, (int)Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
                            px[i] = (byte)Math.Min(255, px[i] * 255 / mx);
                            px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / mx);
                            px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / mx);
                            px[i + 3] = (byte)m;
                        }
                        t = WowTextures.FromRgba32(img.Width, img.Height, px, WowTextureOptions.Ui, Path.GetFileNameWithoutExtension(path) + " (add)", true);
                    }
                }
                catch (Exception e)
                {
                    WowLog.Warn("UI: additive texture " + path + ": " + e.Message);
                }
            }
            Additive[path] = t;
            return t;
        }

        /// <summary>
        /// An icon (Interface\Icons\..., with or without .blp) - null while it loads on a pool thread (call again next frame; the
        /// raw string is the key, so asking again allocates nothing). A missing icon becomes the question mark.
        /// </summary>
        public static Texture2D Icon(string raw)
        {
            if (string.IsNullOrEmpty(raw)) raw = QuestionMark;
            Texture2D t;
            lock (IconLock)
            {
                if (Icons.TryGetValue(raw, out t)) return t;
                if (IconPending.Contains(raw) || _files == null) return null;
                IconPending.Add(raw);
            }
            string path = raw.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".blp";
            WowFiles files = _files;
            string key = raw;
            ThreadPool.QueueUserWorkItem(delegate
            {
                WowPreparedTexture p = WowTextures.Prepare(files, path, WowTextureOptions.Ui);
                lock (IconLock) { if (ReferenceEquals(files, _files)) IconReady.Add(new KeyValuePair<string, WowPreparedTexture>(key, p)); }
            });
            return null;
        }

        /// <summary>Main thread, once a frame: uploads up to max prepared icons.</summary>
        public static void PumpIcons(int max)
        {
            for (int n = 0; n < max; n++)
            {
                KeyValuePair<string, WowPreparedTexture> kv;
                lock (IconLock)
                {
                    if (IconReady.Count == 0) return;
                    kv = IconReady[IconReady.Count - 1];
                    IconReady.RemoveAt(IconReady.Count - 1);
                }
                Texture2D t = null;
                if (kv.Value != null && kv.Value.Error == null)
                {
                    t = WowTextures.Create(kv.Value);
                    if (ReferenceEquals(t, WowTextures.Missing)) t = null;
                }
                if (t == null)
                {
                    _iconsMissing++;
                    if (_iconsMissing <= 10) WowLog.Warn("UI: icon " + kv.Key + " " + (kv.Value != null ? kv.Value.Error : "not prepared") + " - showing the question mark.");
                    t = Tex(QuestionMark);
                }
                else
                {
                    _iconsLoaded++;
                }
                lock (IconLock)
                {
                    Icons[kv.Key] = t;
                    IconPending.Remove(kv.Key);
                }
            }
        }

        private static Sprite _white;

        /// <summary>A white sprite for filled Images (the cooldown sweep: Image.Type.Filled needs a sprite).</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (_white != null) return _white;
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                t.SetPixels32(px);
                t.Apply(false, true);
                t.name = "WowUi white";
                _white = Sprite.Create(t, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
                _white.name = "WowUi white";
                return _white;
            }
        }

        // ---------------------------------------------------------------- fonts

        private static TMP_FontAsset _morpheus, _arialN;
        private static bool _morpheusTried, _arialTried;

        /// <summary>FRIZQT__.TTF (the front end's WowUi loads it once at boot).</summary>
        public static TMP_FontAsset Friz { get { return WowUi.Font; } }

        /// <summary>MORPHEUS.ttf - quest titles (QuestFont_Shadow_Huge, Fonts.xml:219).</summary>
        public static TMP_FontAsset Morpheus
        {
            get
            {
                if (!_morpheusTried) { _morpheusTried = true; _morpheus = LoadFont(FontMorpheus); }
                return _morpheus != null ? _morpheus : Friz;
            }
        }

        /// <summary>ARIALN.TTF - numbers on buttons (NumberFont_*, Fonts.xml:171-205).</summary>
        public static TMP_FontAsset Numbers
        {
            get
            {
                if (!_arialTried) { _arialTried = true; _arialN = LoadFont(FontArialN); }
                return _arialN != null ? _arialN : Friz;
            }
        }

        /// <summary>A font file from the MPQs into a dynamic TextMeshPro font (same recipe as WowUi.LoadFont: through a temp file).</summary>
        private static TMP_FontAsset LoadFont(string mpqPath)
        {
            if (_files == null) return null;
            string path = null;
            try
            {
                byte[] bytes = _files.Read(mpqPath);
                if (bytes == null || bytes.Length < 12) { WowLog.Warn("UI: font " + mpqPath + " not in the game files - using FRIZQT."); return null; }
                string dir = Path.Combine(Application.temporaryCachePath, "SomniumWoW");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, Path.GetFileName(mpqPath.Replace('\\', '/')));
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) File.WriteAllBytes(path, bytes);
                TMP_FontAsset f = TMP_FontAsset.CreateFontAsset(path, 0, 72, 7, GlyphRenderMode.SDFAA, 1024, 1024);
                if (f == null)
                {
                    var unityFont = new Font(path);
                    f = TMP_FontAsset.CreateFontAsset(unityFont, 72, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                }
                if (f == null) { WowLog.Warn("UI: font " + mpqPath + " could not be created - using FRIZQT."); return null; }
                f.name = Path.GetFileNameWithoutExtension(path) + " (WoW)";
                if (Friz != null && Friz != f) f.fallbackFontAssetTable = new List<TMP_FontAsset> { Friz };
                WowLog.Info("UI: font " + mpqPath + " (" + bytes.Length + " B) loaded.");
                return f;
            }
            catch (Exception e)
            {
                WowLog.Warn("UI: font " + mpqPath + " via " + (path ?? "(no path)") + " failed: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- materials

        private static readonly Dictionary<int, Material> TextMaterials = new Dictionary<int, Material>();
        private static Material _overUi;

        /// <summary>
        /// The shared material for text in a font: outlined (the black shadow FrameXML fonts carry) and/or always on top (the arm
        /// panels: AomUiPanel.Lift's recipe - unity_GUIZTestMode Always, queue Overlay). One per font and flag set, so hundreds of
        /// labels share a handful of materials.
        /// </summary>
        public static Material TextMaterial(TMP_FontAsset font, bool outline, bool over)
        {
            if (font == null || font.material == null) return null;
            int key = font.GetInstanceID() * 4 + (outline ? 1 : 0) + (over ? 2 : 0);
            Material m;
            if (TextMaterials.TryGetValue(key, out m) && m != null) return m;
            m = new Material(font.material);
            m.name = font.name + (outline ? " outline" : "") + (over ? " over" : "");
            if (outline && m.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                m.EnableKeyword(ShaderUtilities.Keyword_Outline);
                m.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
                m.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 1f));
            }
            if (over)
            {
                m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                m.renderQueue = (int)RenderQueue.Overlay;
            }
            TextMaterials[key] = m;
            return m;
        }

        /// <summary>uGUI's default material with depth testing off (for RawImage / Image on the arm panels).</summary>
        public static Material OverUi
        {
            get
            {
                if (_overUi != null) return _overUi;
                _overUi = new Material(Canvas.GetDefaultCanvasMaterial());
                _overUi.name = "WowUi over everything";
                _overUi.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                _overUi.renderQueue = (int)RenderQueue.Overlay;
                return _overUi;
            }
        }

        // ---------------------------------------------------------------- money

        /// <summary>"1g 2s 3c" style text (no allocation-free promise: call on change only).</summary>
        public static string MoneyText(int copper)
        {
            if (copper < 0) copper = 0;
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            if (g > 0) return g + "g " + s + "s " + c + "c";
            if (s > 0) return s + "s " + c + "c";
            return c + "c";
        }
    }
}
