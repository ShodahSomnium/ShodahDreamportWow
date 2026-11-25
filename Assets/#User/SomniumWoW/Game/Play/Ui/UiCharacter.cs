// iteration 11 - owner character (proposal-talents.md 4.2 / 6.1; hooks npc H9 + uicore H12 "paper doll drag and drop" and spellfx's item
// targeting, both through npcui's UiContainers slot rules). WoW's PaperDollFrame on the CharacterFrame: the 19 equipment slots, the title
// picker (PlayerTitleFrame), the two stat blocks with their category dropdowns (CharacterAttributesFrame), the five resistances
// (CharacterResistanceFrame) and the CharacterFrame tabs (UiCharacterTabs). Values: the install's FrameXML (dumps in P/harness/out_d11talents:
// PaperDollFrame.xml / .lua, CharacterFrame.xml, GlobalStrings.lua; P/harness/out_character/UIDropDownMenuTemplates.xml), file:line per
// value; "ours" = our own choice (logged once).
using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    // =====================================================================================================================
    /// <summary>
    /// The character window on PaperDollFrame's art (UI-Character-CharacterTab-L1/R1/BottomLeft/BottomRight): the 19 equipment slots where
    /// PaperDollFrame.xml puts them (left column (21, 74) step 41, right column (305, 74), weapons from (122, 385) step 42) with WoW's slot
    /// clicks (pick up, drop, right click = use, Shift = split; VR: tap = use, hold + move = drag), the title picker, the stat blocks
    /// (Base Stats / Melee / Ranged / Spell / Defenses), the resistances and the tab strip. Where WoW draws the 3D model we show health,
    /// power, experience and money (ours: there is no model).
    /// </summary>
    public sealed class UiCharacter : UiWindow
    {
        private const string Pd = @"Interface\PaperDollInfoFrame\";
        private const string LabelFrame = @"Interface\Glues\CharacterCreate\CharacterCreate-LabelFrame.blp";   // PlayerTitleFrame / UIDropDownMenuTemplate art
        private const string ArrowUp = @"Interface\ChatFrame\UI-ChatIcon-ScrollDown-Up.blp", ArrowDown = @"Interface\ChatFrame\UI-ChatIcon-ScrollDown-Down.blp";
        private const string MouseHilight = @"Interface\Buttons\UI-Common-MouseHilight.blp";
        private const float AttrX = 67f, AttrY = 291f, BlockW = 115f;   // CharacterAttributesFrame TOPLEFT (67, -291) 230x78: two 115-wide blocks (PaperDollFrame.xml:524-616)
        private const float ResX = 297f - 32f, ResY = 77f, ResH = 29f;  // CharacterResistanceFrame TOPRIGHT (297, -77) 32x160; MagicResistanceFrameTemplate 32x29 (:211-214, :754-762)
        private const float TitleX = 198f - 105f, TitleY = 55f;         // PlayerTitleFrame 210x32, TOP 9 under CharacterLevelText (:309-318; the name centre (198, 24), CharacterFrame.xml:35-56)
        private static readonly Color BrokenTint = new Color(0.9f, 0f, 0f);   // SetItemButtonTextureVertexColor(self, 0.9, 0, 0) (PaperDollFrame.lua:1264-1266)
        private const string Green = "<color=#20ff20>", Red = "<color=#ff2020>", End = "</color>";   // colorPos / colorNeg (PaperDollFrame.lua:545-546)

        // PLAYERSTAT_DROPDOWN_OPTIONS (PaperDollFrame.lua:66-71) and their texts (GlobalStrings.lua:5666-5670)
        private static readonly string[] CatKeys = { "PLAYERSTAT_BASE_STATS", "PLAYERSTAT_MELEE_COMBAT", "PLAYERSTAT_RANGED_COMBAT", "PLAYERSTAT_SPELL_COMBAT", "PLAYERSTAT_DEFENSES" };
        private static readonly string[] CatEn = { "Base Stats", "Melee", "Ranged", "Spell", "Defenses" };
        private static readonly string[] StatEn = { "Strength", "Agility", "Stamina", "Intellect", "Spirit" };   // SPELL_STAT1..5_NAME (GlobalStrings.lua:7262-7266)
        // MagicResFrame1..5 (PaperDollFrame.xml:766-870): their ids (resistance school) and rows of UI-Character-ResistanceIcons
        private static readonly int[] ResIds = { 6, 2, 3, 4, 5 };
        private static readonly float[] ResTop = { 0.2265625f, 0f, 0.11328125f, 0.33984375f, 0.453125f };
        private static readonly float[] ResBottom = { 0.33984375f, 0.11328125f, 0.2265625f, 0.453125f, 0.56640625f };
        private static readonly string[] ResEn = { "Armor", "Holy Resistance", "Fire Resistance", "Nature Resistance", "Frost Resistance", "Shadow Resistance", "Arcane Resistance" };   // RESISTANCE0..6_NAME (GlobalStrings.lua:6098-6104)
        private static readonly string[] SlotNames = { "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands", "Finger", "Finger", "Trinket", "Trinket", "Back", "Main Hand", "Off Hand", "Ranged", "Tabard" };
        // EquipSlot order of the left / right columns and the weapon row (PaperDollFrame.xml).
        private static readonly EquipSlot[] Left = { EquipSlot.Head, EquipSlot.Neck, EquipSlot.Shoulders, EquipSlot.Back, EquipSlot.Chest, EquipSlot.Shirt, EquipSlot.Tabard, EquipSlot.Wrists };
        private static readonly EquipSlot[] Right = { EquipSlot.Hands, EquipSlot.Waist, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Finger1, EquipSlot.Finger2, EquipSlot.Trinket1, EquipSlot.Trinket2 };
        private static readonly EquipSlot[] Weapons = { EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Ranged };

        private static int _leftCat = -1, _rightCat = -1;   // ours: kept for the session (WoW: the CVars playerStatLeftDropdown / -Right)
        private static bool _oursLogged;

        private readonly UiButton[] _slot = new UiButton[19];
        private readonly string[] _slotPath = new string[19];
        private readonly bool[] _slotLoaded = new bool[19];
        private readonly bool[] _slotBroken = new bool[19];
        private readonly ItemView[] _slotItem = new ItemView[19];
        private readonly TextMeshProUGUI _name, _level, _summary, _titleText;
        private readonly UiArmourFitView _model;   // lead 2026-10-10
        private readonly RawImage _portrait;
        private readonly UiMoney _money;
        private readonly RectTransform _titleFrame;
        private readonly UiCharList _titleList, _statList;
        private readonly StatBlock _left, _right;
        private readonly TextMeshProUGUI[] _resText = new TextMeshProUGUI[5];
        private readonly string[] _resTip = new string[5];
        private readonly List<int> _titleBits = new List<int>();
        private readonly List<string> _titleNames = new List<string>();
        private readonly List<string> _catNames = new List<string>();
        private List<TitleView> _titlesFrom;
        private string _titlesNameFor;
        private int _titleChosenShown = int.MinValue;
        private StatBlock _listBlock;
        private int _sig = int.MinValue;
        private float _statsAt = -10f;
        private bool _statsDirty = true;
        private WowClass _cls = (WowClass)255;

        private sealed class StatBlock
        {
            public int Category = -1, ShownCategory = -2;
            public TextMeshProUGUI DropText;
            public readonly UiButton[] Lines = new UiButton[6];
            public readonly TextMeshProUGUI[] Labels = new TextMeshProUGUI[6], Values = new TextMeshProUGUI[6];
            public readonly string[] Tips = new string[6];
            public float DropX;
        }

        public UiCharacter(Transform parent, UiContext ctx, float x, float y, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "Character", x, y, W, H);
            UiKit.Frame4(Root, "Art", UiArt.CharacterTab + "L1.blp", UiArt.CharacterTab + "R1.blp", UiArt.CharacterTab + "BottomLeft.blp", UiArt.CharacterTab + "BottomRight.blp");
            _portrait = Portrait(UiArt.Tex(UiArt.ClassCircles));
            // CharacterNameText 300x12 (GameFontNormal, white) on CharacterNameFrame CENTER (6, 232) = (198, 24) (CharacterFrame.xml:35-56);
            // CharacterLevelText (GameFontNormalSmall) TOP 6 under it (PaperDollFrame.xml:279-286)
            _name = UiKit.Text(Root, "Name", "", 13f, Color.white, TextAlignmentOptions.Center, 48f, 17f, 300f, 14f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _level = UiKit.Text(Root, "Level", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 48f, 35f, 300f, 12f);
            _level.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.CloseButton(Root, 323f, 7f, close);
            for (int i = 0; i < 8; i++) MakeSlot(Left[i], 21f, 74f + i * 41f);
            for (int i = 0; i < 8; i++) MakeSlot(Right[i], 305f, 74f + i * 41f);
            for (int i = 0; i < 3; i++) MakeSlot(Weapons[i], 122f + i * 42f, 385f);
            // ours: health / power / experience where WoW's CharacterModelFrame (65, -78) 233x215 shows the 3D model
            // lead 2026-10-10: your avatar in its armour in WoW's CharacterModelFrame (65, -78) 233 x 215 - the picture 1 : 2, centred
            _model = new UiArmourFitView(Root, ctx, 65f + (233f - 108f) * 0.5f, 78f, 108f, 215f);
            _summary = UiKit.Text(Root, "Summary", "", 10.5f, Color.white, TextAlignmentOptions.TopLeft, 72f, 104f, 188f, 120f);
            _summary.overflowMode = TextOverflowModes.Truncate;
            _summary.lineSpacing = -4f;
            _money = new UiMoney(Root, 262f, 252f, Color.white);
            // the five resistances (MagicResFrame1..5, each 32x29 under the previous; text GameFontHighlightSmall BOTTOM (0, 3))
            Texture2D resIcons = UiArt.Tex(Pd + "UI-Character-ResistanceIcons.blp");
            for (int i = 0; i < 5; i++)
            {
                UiButton rb = UiKit.Button(Root, "MagicResFrame" + (i + 1), ResX, ResY + i * ResH, 32f, ResH);
                UiKit.Pic(rb.transform, "Icon", resIcons, 0f, 1f, ResTop[i], ResBottom[i], 0f, 0f, 32f, ResH);
                _resText[i] = UiKit.Text(rb.transform, "MagicResText" + (i + 1), "0", 10f, Color.white, TextAlignmentOptions.Bottom, 0f, 0f, 32f, ResH - 3f);
                _resText[i].textWrappingMode = TextWrappingModes.NoWrap;
                int k = i;
                rb.Tip = () => _resTip[k];
            }
            _left = MakeBlock(AttrX, "PlayerStatFrameLeft");
            _right = MakeBlock(AttrX + BlockW, "PlayerStatFrameRight");
            for (int i = 0; i < CatKeys.Length; i++) _catNames.Add(UiGlobalStrings.G(CatKeys[i], CatEn[i]));
            // PlayerTitleFrame (PaperDollFrame.xml:309-410): CharacterCreate-LabelFrame left 25x50 at (0, 17 up), middle 160, right 25
            _titleFrame = UiKit.Rect(Root, "PlayerTitleFrame", TitleX, TitleY, 210f, 32f);
            Texture2D lf = UiArt.Tex(LabelFrame);
            UiKit.Pic(_titleFrame, "Left", lf, 0f, 0.1953125f, 0f, 1f, 0f, -17f, 25f, 50f);
            UiKit.Pic(_titleFrame, "Middle", lf, 0.1953125f, 0.8046875f, 0f, 1f, 25f, -17f, 160f, 50f);
            UiKit.Pic(_titleFrame, "Right", lf, 0.8046875f, 1f, 0f, 1f, 185f, -17f, 25f, 50f);
            // $parentText 140x10 LEFT of $parentLeft (27, 2): the Left texture's centre (8) 2 up
            _titleText = UiKit.Text(_titleFrame, "Text", "", 10f, Color.white, TextAlignmentOptions.MidlineLeft, 27f, 0f, 140f, 12f);
            _titleText.textWrappingMode = TextWrappingModes.NoWrap;
            _titleText.overflowMode = TextOverflowModes.Ellipsis;
            UiButton titleArea = UiKit.Button(_titleFrame, "TextArea", 20f, -2f, 150f, 16f);   // ours: the text opens the list too (a VR target)
            titleArea.Click = ToggleTitles;
            // $parentButton 24x24 TOPRIGHT of $parentRight (-16, -12)
            UiKit.ArtButton(_titleFrame, "Button", ArrowUp, ArrowDown, null, MouseHilight, 210f - 16f - 24f, -17f + 12f, 24f, ToggleTitles);
            _titleFrame.gameObject.SetActive(false);
            UiCharacterTabs.Build(Root, ctx, UiWindowKind.Character);
            // the pick lists last: they draw over the slots and the stat blocks
            _titleList = new UiCharList(Root, "PlayerTitlePickerFrame", true, 6);   // PLAYER_DISPLAYED_TITLES 6 (PaperDollFrame.lua:110)
            _statList = new UiCharList(Root, "DropDownList1", false, 8);           // UIDropDownListTemplate's 8 buttons (UIDropDownMenuTemplates.xml:219-226)
            Root.gameObject.SetActive(false);
            if (!_oursLogged)
            {
                _oursLogged = true;
                WowLog.Info("UI: character pane - ours: health / power / experience / money where WoW draws the 3D model; the stat dropdowns' choice is kept "
                            + "for the session; the title and dropdown texts open their lists too; resistances without the buff colours and the level line; "
                            + "stat tooltips without the class formulas of DEFAULT_STAT1..5_TOOLTIP (C code)");
            }
        }

        // ------------------------------------------------------------------ build helpers

        private void MakeSlot(EquipSlot s, float x, float y)
        {
            int k = (int)s;
            UiButton b = UiKit.Slot(Root, "Slot " + s, x, y, 37f, false);
            b.Icon.texture = UiArt.Tex(UiArt.SlotPrefix + UiArt.SlotBackground[k] + ".blp");
            b.Icon.color = b.Icon.texture != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            b.Icon.enabled = true;
            // npc H9 / uicore H12: WoW's paper doll slot (PaperDollItemSlotButton_OnClick, PaperDollFrame.lua:1232-1248) through npcui's shared rules:
            // drag source + click pick-up, drop target (S.MoveItem / equip), right click / VR tap = use, Shift / long press = split, item targeting first
            UiContainers.Wire(b, Ctx, () => InvPos.Bag0, () => k);
            b.Tip = () => SlotTip(k);
            _slot[k] = b;
        }

        private string SlotTip(int k)
        {
            ItemView it = _slotItem[k];
            if (it == null) return "<size=14>" + SlotNames[k] + "</size>";
            return UiContainers.Tip(Ctx, InvPos.Bag0, k);
        }

        private StatBlock MakeBlock(float x, string name)
        {
            var b = new StatBlock();
            Texture2D bg = UiArt.Tex(Pd + "UI-Character-StatBackground.blp");
            // PlayerStat<Left|Right>Top 115x16 / Middle 115x53 / Bottom 115x16 with their texcoords (PaperDollFrame.xml:534-616)
            UiKit.Pic(Root, name + "Top", bg, 0f, 0.8984375f, 0f, 0.125f, x, AttrY, BlockW, 16f);
            UiKit.Pic(Root, name + "Middle", bg, 0f, 0.8984375f, 0.125f, 0.1953125f, x, AttrY + 16f, BlockW, 53f);
            UiKit.Pic(Root, name + "Bottom", bg, 0f, 0.8984375f, 0.484375f, 0.609375f, x, AttrY + 69f, BlockW, 16f);
            for (int i = 0; i < 6; i++)
            {
                // StatFrameTemplate 104x13 (PaperDollFrame.xml:170-210): label GameFontNormalSmall LEFT, value GameFontHighlightSmall RIGHT; the first at (6, -3)
                UiButton line = UiKit.Button(Root, name + (i + 1), x + 6f, AttrY + 3f + i * 13f, 104f, 13f);
                b.Labels[i] = UiKit.Text(line.transform, "Label", "", 10f, UiArt.Gold, TextAlignmentOptions.MidlineLeft, 0f, 0f, 104f, 13f);
                b.Labels[i].textWrappingMode = TextWrappingModes.NoWrap;
                b.Values[i] = UiKit.Text(line.transform, "StatText", "", 10f, Color.white, TextAlignmentOptions.MidlineRight, 0f, 0f, 104f, 13f);
                b.Values[i].textWrappingMode = TextWrappingModes.NoWrap;
                int k = i;
                line.Tip = () => b.Tips[k];
                b.Lines[i] = line;
            }
            // the category dropdown: UIDropDownMenuTemplate (UIDropDownMenuTemplates.xml:260-340) of width 99 (PaperDollFrame.lua:1521 / :1558),
            // BOTTOMLEFT on the block's TOPLEFT (-17, -8) (PaperDollFrame.xml:726-750) -> the 149x32 frame at (x - 17, 267)
            float dx = x - 17f, dy = AttrY + 8f - 32f;
            Texture2D lf = UiArt.Tex(LabelFrame);
            UiKit.Pic(Root, name + "DropDownLeft", lf, 0f, 0.1953125f, 0f, 1f, dx, dy - 17f, 25f, 64f);
            UiKit.Pic(Root, name + "DropDownMiddle", lf, 0.1953125f, 0.8046875f, 0f, 1f, dx + 25f, dy - 17f, 99f, 64f);
            UiKit.Pic(Root, name + "DropDownRight", lf, 0.8046875f, 1f, 0f, 1f, dx + 124f, dy - 17f, 25f, 64f);
            // $parentText (GameFontHighlightSmall, justifyH RIGHT) RIGHT of $parentRight (-43, 2): right edge dx + 106, centre 2 above the Right texture's
            b.DropText = UiKit.Text(Root, name + "DropDownText", "", 10f, Color.white, TextAlignmentOptions.MidlineRight, dx + 16f, dy + 7f, 90f, 12f);
            b.DropText.textWrappingMode = TextWrappingModes.NoWrap;
            UiButton area = UiKit.Button(Root, name + "DropDownArea", dx + 20f, dy + 5f, 88f, 16f);   // ours: the text opens the list too (a VR target)
            area.Click = () => ToggleStatList(b);
            // $parentButton 24x24 TOPRIGHT of $parentRight (-16, -18)
            UiKit.ArtButton(Root, name + "DropDownButton", ArrowUp, ArrowDown, null, MouseHilight, dx + 149f - 16f - 24f, dy - 17f + 18f, 24f, () => ToggleStatList(b));
            b.DropX = dx;
            return b;
        }

        // ------------------------------------------------------------------ lists

        private void ToggleTitles()
        {
            if (_titleList.IsOpen)
            {
                UiSound.Named("igMainMenuOptionCheckBoxOff");   // PlayerTitlePickerFrame_Toggle (PaperDollFrame.lua:2659-2669)
                _titleList.Close();
                return;
            }
            _statList.Close();
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            int sel = Math.Max(0, _titleBits.IndexOf(Ctx.P != null ? Ctx.P.ChosenTitle : 0));
            _titleList.Picked = PickTitle;
            // PlayerTitlePickerFrame TOPLEFT on PlayerTitleFrame's BOTTOMLEFT (8, 20) (PaperDollFrame.xml:411-418)
            _titleList.Open(TitleX + 8f, TitleY + 32f - 20f, _titleNames, sel);
        }

        private void PickTitle(int idx)
        {
            if (idx < 0 || idx >= _titleBits.Count) return;
            UiSound.Named("igMainMenuOptionCheckBoxOff");   // PlayerTitleButton_OnClick (PaperDollFrame.lua:2670-2676)
            _titleList.Close();
            if (Ctx.S == null) return;
            Ctx.S.SetTitle(_titleBits[idx]);
            _titleText.text = _titleNames[idx];
            _titleChosenShown = int.MinValue;
        }

        private void ToggleStatList(StatBlock b)
        {
            if (_statList.IsOpen && _listBlock == b) { _statList.Close(); return; }
            _titleList.Close();
            UiSound.Named("igMainMenuOptionCheckBoxOn");
            _listBlock = b;
            _statList.Picked = i =>
            {
                _statList.Close();
                if (i < 0 || i >= CatKeys.Length) return;
                b.Category = i;
                if (b == _left) _leftCat = i; else _rightCat = i;
                _statsDirty = true;
            };
            _statList.Open(b.DropX + 16f, AttrY + 8f - 2f, _catNames, b.Category);   // ours: under the dropdown
        }

        protected override void OnClose()
        {
            if (_titleList != null) _titleList.Close();
            if (_statList != null) _statList.Close();
        }

        protected override void OnOpen()
        {
            _statsDirty = true;
        }

        // ------------------------------------------------------------------ refresh

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            if (p.Class != _cls)
            {
                _cls = p.Class;
                Vector4 c = UiArt.ClassCoords(p.Class);
                UiKit.Coords(_portrait, c.x, c.y, c.z, c.w);
                // the defaults (PaperDollFrame.lua:164-174): left Base Stats; right Spell for mages, priests, warlocks, druids, Ranged for hunters, else Melee
                if (_leftCat < 0) _leftCat = 0;
                if (_rightCat < 0)
                    _rightCat = p.Class == WowClass.Mage || p.Class == WowClass.Priest || p.Class == WowClass.Warlock || p.Class == WowClass.Druid ? 3
                              : p.Class == WowClass.Hunter ? 2 : 1;
                _statsDirty = true;
            }
            if (_left.Category != _leftCat) { _left.Category = _leftCat; _statsDirty = true; }
            if (_right.Category != _rightCat) { _right.Category = _rightCat; _statsDirty = true; }
            RefreshSlots(p);
            RefreshTitles(p);
            if (_model != null)
            {
                _model.Tick(now, IsOpen, 0);
                bool sum = !_model.HasPicture;   // the text where the model stands only while no avatar is drawn
                if (_summary.enabled != sum) _summary.enabled = sum;
            }
            _money.Set(p.Money);
            int sig = p.Level * 1000003 + p.Health * 31 + p.MaxHealth * 7 + p.PowerMax * 13 + p.PowerValue * 17 + p.Xp * 3 + (int)p.Power + p.Race * 7919 + p.Mana * 19 + p.MaxMana * 23;
            if (sig != _sig)
            {
                _sig = sig;
                _name.text = p.Name ?? "";
                // PaperDollFrame_SetLevel: PLAYER_LEVEL "Level %s %s %s" (PaperDollFrame.lua:202-204, GlobalStrings.lua:5678) - level, race, class
                _level.text = UiGlobalStrings.FormatOr("PLAYER_LEVEL", "Level %s %s %s", p.Level.ToString(CultureInfo.InvariantCulture), RaceName(p.Race), UiArt.ClassName(p.Class));
                Sb.Length = 0;
                Line("Health", p.Health + " / " + p.MaxHealth);
                string power = p.Power == PowerType.Rage ? "Rage" : p.Power == PowerType.Energy ? "Energy" : p.Power == PowerType.Focus ? "Focus" : p.Power == PowerType.RunicPower ? "Runic Power" : "Mana";
                Line(power, p.PowerValue + " / " + p.PowerMax);
                if (p.Power != PowerType.Mana && p.MaxMana > 0) Line("Mana", p.Mana + " / " + p.MaxMana);
                Sb.Append('\n');
                Line("Experience", p.XpToNextLevel > 0 ? p.Xp + " / " + p.XpToNextLevel : "max level");
                _summary.text = Sb.ToString();
            }
            if (_statsDirty || now - _statsAt >= 0.25f)   // ours: the stat lines are rebuilt 4 times a second at most
            {
                _statsAt = now;
                _statsDirty = false;
                FillBlock(_left, p);
                FillBlock(_right, p);
                FillResistances(p);
            }
            _titleList.Tick(Ctx.Vr);
            _statList.Tick(Ctx.Vr);
        }

        private void RefreshSlots(PlayerState p)
        {
            bool picks = UiContainers.SlotsPickUp(Ctx);
            for (int k = 0; k < 19; k++)
            {
                ItemView it = p.Equipment != null && k < p.Equipment.Length ? p.Equipment[k] : null;
                UiButton b = _slot[k];
                if (!ReferenceEquals(it, _slotItem[k]))
                {
                    _slotItem[k] = it;
                    _slotLoaded[k] = false;
                    _slotPath[k] = null;
                    if (it == null)
                    {
                        b.Icon.texture = UiArt.Tex(UiArt.SlotPrefix + UiArt.SlotBackground[k] + ".blp");
                        b.Icon.enabled = b.Icon.texture != null;
                    }
                }
                if (it != null && !_slotLoaded[k]) SetIcon(b, it.IconPath, ref _slotPath[k], ref _slotLoaded[k]);
                bool broken = it != null && it.MaxDurability > 0 && it.Durability <= 0;
                if (broken != _slotBroken[k] || (it == null && b.Icon.color != Color.white))
                {
                    _slotBroken[k] = broken;
                    b.Icon.color = broken ? BrokenTint : Color.white;
                }
                b.ClickPicksUp = picks;
            }
        }

        private void RefreshTitles(PlayerState p)
        {
            List<TitleView> known = p.KnownTitles;
            bool show = known != null && known.Count > 0;   // titleCount < 2 (only "None") hides the frame (PaperDollFrame.lua:2617-2619)
            if (_titleFrame.gameObject.activeSelf != show)
            {
                _titleFrame.gameObject.SetActive(show);
                if (!show) _titleList.Close();
            }
            if (!show) return;
            if (!ReferenceEquals(known, _titlesFrom) || !string.Equals(p.Name, _titlesNameFor))
            {
                _titlesFrom = known;
                _titlesNameFor = p.Name;
                // PlayerTitleFrame_UpdateTitles (PaperDollFrame.lua:2591-2658): NONE first, then strtrim(GetTitleName(i)) sorted by name (Lua's <)
                var rows = new List<KeyValuePair<string, int>>();
                for (int i = 0; i < known.Count; i++)
                    if (known[i] != null && known[i].BitIndex > 0) rows.Add(new KeyValuePair<string, int>(TitleName(known[i].Format), known[i].BitIndex));
                rows.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                _titleBits.Clear(); _titleNames.Clear();
                _titleBits.Add(0); _titleNames.Add(UiGlobalStrings.G("NONE", "None"));   // GlobalStrings.lua:5182; CMSG_SET_TITLE 0 = none
                for (int i = 0; i < rows.Count; i++) { _titleBits.Add(rows[i].Value); _titleNames.Add(rows[i].Key); }
                _titleChosenShown = int.MinValue;
                if (_titleList.IsOpen) _titleList.Close();
            }
            if (p.ChosenTitle != _titleChosenShown)
            {
                _titleChosenShown = p.ChosenTitle;
                int idx = _titleBits.IndexOf(p.ChosenTitle);
                // currentTitle 0 -> PAPERDOLL_SELECT_TITLE "Select a Title" (PaperDollFrame.lua:2622-2627, GlobalStrings.lua:5566)
                _titleText.text = p.ChosenTitle <= 0 || idx < 0 ? UiGlobalStrings.G("PAPERDOLL_SELECT_TITLE", "Select a Title") : _titleNames[idx];
            }
        }

        /// <summary>A CharTitles name without the character's name: GetTitleName is C code; FrameXML trims its result (strtrim,
        /// PaperDollFrame.lua:2605) - ours: the "%s" removed and trimmed ("Private %s" -> "Private").</summary>
        private static string TitleName(string format)
        {
            if (string.IsNullOrEmpty(format)) return "";
            return format.Replace("%s", "").Trim();
        }

        // ------------------------------------------------------------------ stats (UpdatePaperdollStats, PaperDollFrame.lua:1605-1660)

        private void FillBlock(StatBlock b, PlayerState p)
        {
            if (b.Category != b.ShownCategory)
            {
                b.ShownCategory = b.Category;
                b.DropText.text = b.Category >= 0 && b.Category < _catNames.Count ? _catNames[b.Category] : "";
            }
            StatsView st = p.Stats;
            for (int i = 0; i < 6; i++)
            {
                string label, value, tip;
                bool show = Stat(b.Category, i, p, st, out label, out value, out tip);
                UiButton line = b.Lines[i];
                if (line.gameObject.activeSelf != show) line.gameObject.SetActive(show);
                b.Tips[i] = show ? tip : null;
                if (!show) continue;
                if (b.Labels[i].text != label) b.Labels[i].text = label;
                if (b.Values[i].text != value) b.Values[i].text = value;
            }
        }

        private static string G(string key, string en) { return UiGlobalStrings.G(key, en); }

        /// <summary>STAT_FORMAT "%s:" (GlobalStrings.lua:7365).</summary>
        private static string Lbl(string name) { return UiGlobalStrings.FormatOr("STAT_FORMAT", "%s:", name); }

        /// <summary>A GlobalStrings format with WoW's escapes as TMP text (|cffRRGGBB / |r colours, |n new line).</summary>
        private static string F(string key, string en, params object[] args) { return Rich(UiGlobalStrings.FormatOr(key, en, args)); }

        private static string Rich(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('|') < 0) return s ?? "";
            var sb = new System.Text.StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '|' && i + 1 < s.Length)
                {
                    char d = s[i + 1];
                    if ((d == 'c' || d == 'C') && i + 9 < s.Length) { sb.Append("<color=#").Append(s, i + 4, 6).Append('>'); i += 9; continue; }
                    if (d == 'r' || d == 'R') { sb.Append("</color>"); i++; continue; }
                    if (d == 'n') { sb.Append('\n'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string N2(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        /// <summary>The tooltip of a stat line (PaperDollStatTooltip: tooltip white, tooltip2 NORMAL_FONT_COLOR).</summary>
        private static string Tip(string first, string second)
        {
            return "<size=14>" + first + "</size>" + (string.IsNullOrEmpty(second) ? "" : "\n<color=#ffd100>" + second + "</color>");
        }

        /// <summary>ColorPaperDollStat (PaperDollFrame.lua:1398-1414).</summary>
        private static string Colored(int effective, int pos, int neg)
        {
            effective = Math.Max(0, effective);
            if (pos == 0 && neg == 0) return effective.ToString(CultureInfo.InvariantCulture);
            return (neg < 0 ? Red : Green) + effective + End;
        }

        /// <summary>FormatPaperDollTooltipStat (PaperDollFrame.lua:1376-1396): "Name effective (base +pos -neg)".</summary>
        private static string TipStat(string name, int baseValue, int pos, int neg)
        {
            int effective = Math.Max(0, baseValue + pos + neg);
            string t = name + " " + effective;
            if (pos == 0 && neg == 0) return t;
            t += " (" + baseValue;
            if (pos > 0) t += Green + "+" + pos + End;
            if (neg < 0) t += Red + " " + neg + End;
            return t + ")";
        }

        private static int BaseStat(StatsView st, int i)
        {
            switch (i)
            {
                case 0: return st.Strength;
                case 1: return st.Agility;
                case 2: return st.Stamina;
                case 3: return st.Intellect;
                default: return st.Spirit;
            }
        }

        /// <summary>PaperDollFrame_GetArmorReduction (PaperDollFrame.lua:1497-1516).</summary>
        private static float ArmorReduction(int armor, int attackerLevel)
        {
            float lm = attackerLevel;
            if (lm > 59f) lm = lm + 4.5f * (lm - 59f);
            float temp = 0.1f * armor / (8.5f * lm + 40f);
            temp = temp / (1f + temp);
            if (temp > 0.75f) return 75f;
            if (temp < 0f) return 0f;
            return temp * 100f;
        }

        private static string Na() { return G("NOT_APPLICABLE", "N/A"); }   // GlobalStrings.lua:5191

        private bool Stat(int cat, int i, PlayerState p, StatsView st, out string label, out string value, out string tip)
        {
            label = value = tip = null;
            if (st == null || cat < 0) return false;
            int level = p.Level;
            switch (cat)
            {
                case 0:   // PLAYERSTAT_BASE_STATS: the five stats + armor (PaperDollFrame.lua:1620-1626)
                    if (i < 5)
                    {
                        string n = G("SPELL_STAT" + (i + 1) + "_NAME", StatEn[i]);
                        int total = BaseStat(st, i), pos = st.StatPos[i], neg = st.StatNeg[i];
                        label = Lbl(n);
                        value = Colored(total, pos, neg);
                        tip = Tip(TipStat(n, total - pos - neg, pos, neg), null);
                        return true;
                    }
                    return ArmorLine(p, st, out label, out value, out tip);
                case 1:   // PLAYERSTAT_MELEE_COMBAT (:1627-1634)
                    switch (i)
                    {
                        case 0: return MeleeDamage(st, out label, out value, out tip);
                        case 1:
                        {
                            // PaperDollFrame_SetAttackSpeed (:628-650): WEAPON_SPEED "%.2f" [/ off-hand]
                            string sp = N2(st.AttackSpeed) + (st.OffAttackSpeed > 0f ? " / " + N2(st.OffAttackSpeed) : "");
                            label = Lbl(G("WEAPON_SPEED", "Speed"));
                            value = sp;
                            tip = Tip(G("ATTACK_SPEED", "Attack Speed") + " " + sp, F("CR_HASTE_RATING_TOOLTIP", "Haste rating %d (%.2f%% haste)", st.Ratings[17], st.RatingBonus[17]));
                            return true;
                        }
                        case 2:
                        {
                            // PaperDollFrame_SetAttackPower (:651-662): ATTACK_POWER, MELEE_ATTACK_POWER_TOOLTIP effective / ATTACK_POWER_MAGIC_NUMBER 14 (:35)
                            int eff = Math.Max(0, st.AttackPower + st.AttackPowerPos + st.AttackPowerNeg);
                            label = Lbl(G("ATTACK_POWER", "Power"));
                            value = Colored(eff, st.AttackPowerPos, st.AttackPowerNeg);
                            tip = Tip(TipStat(G("MELEE_ATTACK_POWER", "Melee Attack Power"), st.AttackPower, st.AttackPowerPos, st.AttackPowerNeg),
                                      F("MELEE_ATTACK_POWER_TOOLTIP", "Increases damage with melee weapons by %.1f damage per second.", eff / 14f));
                            return true;
                        }
                        case 3: return RatingHit(5, level, st, out label, out value, out tip);
                        case 4:
                            // PaperDollFrame_SetMeleeCritChance (:955-964)
                            label = Lbl(G("MELEE_CRIT_CHANCE", "Crit Chance"));
                            value = N2(st.CritChance) + "%";
                            tip = Tip(G("MELEE_CRIT_CHANCE", "Crit Chance") + " " + value, F("CR_CRIT_MELEE_TOOLTIP", "Crit rating %d (+%.2f%% crit chance)", st.Ratings[8], st.RatingBonus[8]));
                            return true;
                        default:
                        {
                            // PaperDollFrame_SetExpertise (:1022-1050): expertise [/ off-hand]
                            string ex = st.Expertise + (st.OffAttackSpeed > 0f ? " / " + st.OffhandExpertise : "");
                            label = Lbl(G("STAT_EXPERTISE", "Expertise"));
                            value = ex;
                            tip = Tip(G("COMBAT_RATING_NAME24", "Expertise") + " " + ex, null);
                            return true;
                        }
                    }
                case 2:   // PLAYERSTAT_RANGED_COMBAT (:1635-1642), the sixth line hidden
                {
                    bool ranged = p.Equipment != null && p.Equipment.Length > (int)EquipSlot.Ranged && p.Equipment[(int)EquipSlot.Ranged] != null && st.RangedMaxDamage > 0f;   // GetInventoryItemTexture("player", 18) (:782-787)
                    switch (i)
                    {
                        case 0:
                            label = Lbl(G("DAMAGE", "Damage"));
                            if (!ranged) { value = Na(); tip = null; return true; }
                            value = Math.Max(Mathf.FloorToInt(st.RangedMinDamage), 1) + " - " + Math.Max(Mathf.CeilToInt(st.RangedMaxDamage), 1);
                            tip = WeaponTip(G("INVTYPE_RANGED", "Ranged"), st.RangedSpeed, value, st.RangedMinDamage, st.RangedMaxDamage);   // CharacterRangedDamageFrame_OnEnter (:1485-1495)
                            return true;
                        case 1:
                            label = Lbl(G("WEAPON_SPEED", "Speed"));
                            if (!ranged) { value = Na(); tip = null; return true; }
                            value = N2(st.RangedSpeed);
                            tip = Tip(G("ATTACK_SPEED", "Attack Speed") + " " + value, F("CR_HASTE_RATING_TOOLTIP", "Haste rating %d (%.2f%% haste)", st.Ratings[18], st.RatingBonus[18]));
                            return true;
                        case 2:
                        {
                            int eff = Math.Max(0, st.RangedAttackPower + st.RangedAttackPowerPos + st.RangedAttackPowerNeg);
                            label = Lbl(G("ATTACK_POWER", "Power"));
                            value = Colored(eff, st.RangedAttackPowerPos, st.RangedAttackPowerNeg);
                            tip = Tip(TipStat(G("RANGED_ATTACK_POWER", "Ranged Attack Power"), st.RangedAttackPower, st.RangedAttackPowerPos, st.RangedAttackPowerNeg),
                                      F("RANGED_ATTACK_POWER_TOOLTIP", "Increases damage with ranged weapons by %.1f damage per second.", eff / 14f));
                            return true;
                        }
                        case 3: return RatingHit(6, level, st, out label, out value, out tip);
                        case 4:
                            label = Lbl(G("RANGED_CRIT_CHANCE", "Crit Chance"));
                            value = N2(st.RangedCritChance) + "%";
                            tip = Tip(G("RANGED_CRIT_CHANCE", "Crit Chance") + " " + value, F("CR_CRIT_RANGED_TOOLTIP", "Crit rating %d (+%.2f%% crit chance)", st.Ratings[9], st.RatingBonus[9]));
                            return true;
                        default: return false;
                    }
                }
                case 3:   // PLAYERSTAT_SPELL_COMBAT (:1643-1650)
                    switch (i)
                    {
                        case 0:
                        {
                            // PaperDollFrame_SetSpellBonusDamage (:914-932): the smallest of the schools holy .. arcane
                            int min = int.MaxValue;
                            for (int s = 1; s < 7; s++) min = Math.Min(min, st.SpellDamage[s]);
                            label = Lbl(G("BONUS_DAMAGE", "Bonus Damage"));
                            value = min.ToString(CultureInfo.InvariantCulture);
                            tip = Tip(G("BONUS_DAMAGE", "Bonus Damage") + " " + value, null);
                            return true;
                        }
                        case 1:
                            label = Lbl(G("BONUS_HEALING", "Bonus Healing"));
                            value = st.BonusHealing.ToString(CultureInfo.InvariantCulture);
                            tip = Tip(G("BONUS_HEALING", "Bonus Healing"), F("BONUS_HEALING_TOOLTIP", "Increases your healing by up to %d", st.BonusHealing));
                            return true;
                        case 2: return RatingHit(7, level, st, out label, out value, out tip);
                        case 3:
                        {
                            // PaperDollFrame_SetSpellCritChance (:933-954): the smallest of the schools holy .. arcane, "%.2f%%"
                            float min = float.MaxValue;
                            for (int s = 1; s < 7; s++) min = Math.Min(min, st.SpellCrit[s]);
                            label = Lbl(G("SPELL_CRIT_CHANCE", "Crit Chance"));
                            value = N2(min) + "%";
                            tip = Tip(G("SPELL_CRIT_CHANCE", "Crit Chance") + " " + value, F("CR_CRIT_MELEE_TOOLTIP", "Crit rating %d (+%.2f%% crit chance)", st.Ratings[10], st.RatingBonus[10]));
                            return true;
                        }
                        case 4:
                            // PaperDollFrame_SetSpellHaste (:994-1002): CR_HASTE_SPELL rating
                            label = Lbl(G("SPELL_HASTE", "Haste Rating"));
                            value = st.Ratings[19].ToString(CultureInfo.InvariantCulture);
                            tip = Tip(G("SPELL_HASTE", "Haste Rating"), F("SPELL_HASTE_TOOLTIP", "Increases the speed that your spells cast by %.2f%%.", st.RatingBonus[19]));
                            return true;
                        default:
                        {
                            // PaperDollFrame_SetManaRegen (:1003-1021): N/A without mana; floor(x * 5)
                            label = Lbl(G("MANA_REGEN", "Mana Regen"));
                            if (p.Power != PowerType.Mana && p.MaxMana <= 0) { value = Na(); tip = null; return true; }
                            int regen = Mathf.FloorToInt(st.ManaRegen), casting = Mathf.FloorToInt(st.ManaRegenCasting);
                            value = regen.ToString(CultureInfo.InvariantCulture);
                            tip = Tip(G("MANA_REGEN", "Mana Regen"), F("MANA_REGEN_TOOLTIP", "%d mana regenerated every 5 seconds while not casting\n%d mana regenerated every 5 seconds while casting", regen, casting));
                            return true;
                        }
                    }
                default:   // PLAYERSTAT_DEFENSES (:1651-1657)
                    switch (i)
                    {
                        case 0: return ArmorLine(p, st, out label, out value, out tip);
                        case 1:
                        {
                            // PaperDollFrame_SetDefense (:448-468): base + the rating's modifier, DEFAULT_STATDEFENSE_TOOLTIP with
                            // DODGE_PARRY_BLOCK_PERCENT_PER_DEFENSE 0.04 * ((base + modifier) - level * 5) (:41, :493-499)
                            int bas = st.Defense, mod = st.DefenseRatingBonus;
                            float pct = Math.Max(0f, 0.04f * ((bas + mod) - level * 5));
                            string n = G("DEFENSE", "Defense");
                            label = Lbl(n);
                            value = Colored(bas + mod, Math.Max(0, mod), Math.Min(0, mod));
                            tip = Tip(TipStat(n, bas, Math.Max(0, mod), Math.Min(0, mod)),
                                      F("DEFAULT_STATDEFENSE_TOOLTIP", "Defense Rating %d (+%d Defense)|nIncreases chance to Dodge, Block and Parry by %.2f%%|nDecreases chance to be hit and critically hit by %.2f%%|n|cff888888(Before diminishing returns)|r",
                                        st.Ratings[1], (int)st.RatingBonus[1], pct, pct));
                            return true;
                        }
                        case 2:
                            // PaperDollFrame_SetDodge (:469-476)
                            label = Lbl(G("STAT_DODGE", "Dodge"));
                            value = N2(st.DodgeChance) + "%";
                            tip = Tip(G("DODGE_CHANCE", "Dodge Chance") + " " + value, F("CR_DODGE_TOOLTIP", "Dodge Rating of %d adds %.2f%% Dodge|n|cff888888(Before diminishing returns)|r", st.Ratings[2], st.RatingBonus[2]));
                            return true;
                        case 3:
                            label = Lbl(G("STAT_PARRY", "Parry"));
                            value = N2(st.ParryChance) + "%";
                            tip = Tip(G("PARRY_CHANCE", "Parry Chance") + " " + value, F("CR_PARRY_TOOLTIP", "Parry Rating of %d adds %.2f%% Parry|n|cff888888(Before diminishing returns)|r", st.Ratings[3], st.RatingBonus[3]));
                            return true;
                        case 4:
                            label = Lbl(G("STAT_BLOCK", "Block"));
                            value = N2(st.BlockChance) + "%";
                            tip = Tip(G("BLOCK_CHANCE", "Block Chance") + " " + value, F("CR_BLOCK_TOOLTIP", "Block Rating of %d adds %.2f%% Block\nYour block stops %d damage.", st.Ratings[4], st.RatingBonus[4], st.BlockValue));
                            return true;
                        default:
                        {
                            // PaperDollFrame_SetResilience (:501-526): the smallest of the three crit-taken ratings
                            int res = Math.Min(st.Ratings[14], Math.Min(st.Ratings[15], st.Ratings[16]));
                            label = Lbl(G("STAT_RESILIENCE", "Resilience"));
                            value = res.ToString(CultureInfo.InvariantCulture);
                            tip = Tip(G("STAT_RESILIENCE", "Resilience") + " " + value, null);
                            return true;
                        }
                    }
            }
        }

        /// <summary>PaperDollFrame_SetArmor (PaperDollFrame.lua:426-446): armor, DEFAULT_STATARMOR_TOOLTIP with the reduction against our level.</summary>
        private static bool ArmorLine(PlayerState p, StatsView st, out string label, out string value, out string tip)
        {
            int armor = st.Resist[0] != 0 ? st.Resist[0] : st.Armor;
            string n = G("ARMOR", "Armor");
            label = Lbl(n);
            value = Math.Max(0, armor).ToString(CultureInfo.InvariantCulture);
            tip = Tip(n + " " + value, F("DEFAULT_STATARMOR_TOOLTIP", "Reduces Physical Damage taken by %0.2f%%", ArmorReduction(armor, p.Level)));
            return true;
        }

        /// <summary>PaperDollFrame_SetDamage (PaperDollFrame.lua:527-627) without the buff split (UnitDamage's percent / bonus are C values).</summary>
        private static bool MeleeDamage(StatsView st, out string label, out string value, out string tip)
        {
            label = Lbl(G("DAMAGE", "Damage"));
            value = Math.Max(Mathf.FloorToInt(st.MinDamage), 1) + " - " + Math.Max(Mathf.CeilToInt(st.MaxDamage), 1);
            // CharacterDamageFrame_OnEnter (:1463-1483): main hand, then the off hand
            tip = WeaponTip(G("INVTYPE_WEAPONMAINHAND", "Main Hand"), st.AttackSpeed, value, st.MinDamage, st.MaxDamage);
            if (st.OffAttackSpeed > 0f)
            {
                string off = Math.Max(Mathf.FloorToInt(st.OffMinDamage), 1) + " - " + Math.Max(Mathf.CeilToInt(st.OffMaxDamage), 1);
                tip += "\n\n" + WeaponTip(G("INVTYPE_WEAPONOFFHAND", "Off Hand"), st.OffAttackSpeed, off, st.OffMinDamage, st.OffMaxDamage);
            }
            return true;
        }

        private static string WeaponTip(string title, float speed, string damage, float min, float max)
        {
            float dps = speed > 0f ? Math.Max((min + max) * 0.5f, 1f) / speed : 0f;
            return "<size=14>" + title + "</size>\n<color=#ffd100>" + Lbl(G("ATTACK_SPEED_SECONDS", "Attack Speed (seconds)")) + "<pos=62%>" + N2(speed)
                   + "\n" + Lbl(G("DAMAGE", "Damage")) + "<pos=62%>" + damage
                   + "\n" + Lbl(G("DAMAGE_PER_SECOND", "Damage per Second")) + "<pos=62%>" + dps.ToString("0.0", CultureInfo.InvariantCulture) + "</color>";
        }

        /// <summary>PaperDollFrame_SetRating (PaperDollFrame.lua:319-365) for the hit ratings (CombatRating 5 melee, 6 ranged, 7 spell = Lua's CR_HIT_* 6 / 7 / 8).</summary>
        private static bool RatingHit(int cr, int level, StatsView st, out string label, out string value, out string tip)
        {
            string n = G("COMBAT_RATING_NAME" + (cr + 1), "Hit Rating");
            label = Lbl(n);
            int rating = st.Ratings[cr];
            value = rating.ToString(CultureInfo.InvariantCulture);
            string second;
            if (cr == 7)
                second = F("CR_HIT_SPELL_TOOLTIP", "Increases your spell chance to hit a target of level %d by %.2f%%.\n\nSpell Penetration %d (Reduces enemy resistances by %d)", level, st.RatingBonus[7], st.SpellPenetration, st.SpellPenetration);
            else if (cr == 6)
                second = F("CR_HIT_RANGED_TOOLTIP", "Increases your ranged chance to hit a target of level %d by %.2f%%\n\nArmor penetration rating %d (Enemy Armor Reduced by up to %.2f%%).", level, st.RatingBonus[6], st.Ratings[24], st.RatingBonus[24]);
            else
                second = F("CR_HIT_MELEE_TOOLTIP", "Increases your melee chance to hit a target of level %d by %.2f%%\n\nArmor penetration rating %d (Enemy armor reduced by up to %.2f%%).", level, st.RatingBonus[5], st.Ratings[24], st.RatingBonus[24]);
            tip = Tip(n + " " + value, second);
            return true;
        }

        /// <summary>PaperDollFrame_SetResistances (PaperDollFrame.lua:366-425): "Name value" per school (the buff split and the level line are
        /// not read - ours).</summary>
        private void FillResistances(PlayerState p)
        {
            StatsView st = p.Stats;
            if (st == null) return;
            for (int i = 0; i < 5; i++)
            {
                int id = ResIds[i];
                int v = st.Resist[id];
                string t = v.ToString(CultureInfo.InvariantCulture);
                if (_resText[i].text != t) _resText[i].text = t;
                _resTip[i] = "<size=14>" + G("RESISTANCE" + id + "_NAME", ResEn[id]) + " " + t + "</size>";
            }
        }

        private int _raceFor = -1;
        private string _raceName = "";

        /// <summary>ChrRaces' localized name (WowRaces.Name), cached per race.</summary>
        private string RaceName(int race)
        {
            if (race == _raceFor) return _raceName;
            _raceFor = race;
            string n = null;
            try { n = UiArt.Files != null ? SomniumSpace.Worlds.SomniumWoW.Play.Rules.WowRaces.Name(UiArt.Files, race) : null; }
            catch (Exception e) { WowLog.Warn("UI: race name for " + race + " failed: " + e.Message); }
            _raceName = string.IsNullOrEmpty(n) ? "" : n;
            return _raceName;
        }

        private static void Line(string label, string value)
        {
            Sb.Append("<color=#ffd100>").Append(label).Append(":</color> <pos=58%>").Append(value).Append('\n');
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// The two small pick lists of the character pane. Title style = WoW's PlayerTitlePickerFrame (PaperDollFrame.xml:411-460: the
    /// UI-DialogBox backdrop, insets 11, edge 32; PlayerTitleButtonTemplate rows 16 tall (:88-120) with a 16x16 UI-CheckBox-Check 8 in, the text
    /// 3 right of it and UI-FriendsFrame-HighlightBar; PLAYER_DISPLAYED_TITLES 6 / PLAYER_TITLE_HEIGHT 16, the frame text width + 56 (+ 76
    /// with the scroll bar) wide and rows * 16 + 26 tall, PaperDollFrame.lua:110-111 / :2625-2640). Dropdown style = UIDropDownListTemplate
    /// (UIDropDownMenuTemplates.xml:185-226: UI-DialogBox-Background-Dark; UIDropDownMenuButtonTemplate rows 16 tall with an 18x18 check at
    /// the left and UI-QuestTitleHighlight, :3-40). Ours: the dropdown list's padding (15) and place, the title rows' highlight width.
    /// </summary>
    internal sealed class UiCharList
    {
        private const float RowH = 16f;
        private const string DarkBackground = @"Interface\DialogFrame\UI-DialogBox-Background-Dark.blp";
        public readonly RectTransform Root;
        public Action<int> Picked;
        private readonly bool _title;
        private readonly int _shown;
        private readonly UiBackdrop _back;
        private readonly UiButton _blocker, _up, _down;
        private readonly UiButton[] _rows;
        private readonly RawImage[] _checks;
        private readonly TextMeshProUGUI[] _texts;
        private IList<string> _items;
        private int _selected = -1, _top;
        private float _w, _h;

        public bool IsOpen { get { return Root != null && Root.gameObject.activeSelf; } }

        public UiCharList(Transform parent, string name, bool titleStyle, int shown)
        {
            _title = titleStyle;
            _shown = shown;
            Root = UiKit.Rect(parent, name, 0f, 0f, 100f, 100f);
            _blocker = UiKit.Button(Root, "Blocker", 0f, 0f, 100f, 100f);   // the frame takes the pointer (enableMouse)
            _back = UiBackdrop.Make(Root, "Backdrop", titleStyle ? UiArt.DialogBackground : DarkBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            _rows = new UiButton[shown];
            _checks = new RawImage[shown];
            _texts = new TextMeshProUGUI[shown];
            Texture2D check = UiArt.Tex(@"Interface\Buttons\UI-CheckBox-Check.blp");
            Texture2D hi = UiArt.Add(titleStyle ? @"Interface\FriendsFrame\UI-FriendsFrame-HighlightBar.blp" : @"Interface\QuestFrame\UI-QuestTitleHighlight.blp");
            for (int i = 0; i < shown; i++)
            {
                UiButton b = UiKit.Button(Root, "Button" + (i + 1), 0f, 0f, 100f, RowH);
                b.Hilite = UiKit.Pic(b.transform, "Highlight", hi, 0f, 0f, 100f, RowH);
                b.Hilite.enabled = false;
                _checks[i] = titleStyle ? UiKit.Pic(b.transform, "Check", check, 8f, 0f, 16f, 16f) : UiKit.Pic(b.transform, "Check", check, 0f, -1f, 18f, 18f);
                _checks[i].enabled = false;
                _texts[i] = UiKit.Text(b.transform, "Text", "", 10f, Color.white, TextAlignmentOptions.MidlineLeft, titleStyle ? 27f : 20f, 0f, 300f, RowH);
                _texts[i].textWrappingMode = TextWrappingModes.NoWrap;
                int k = i;
                b.Click = () => Pick(k);
                _rows[i] = b;
            }
            _up = UiKit.ArtButton(Root, "ScrollUp", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 0f, 0f, 16f, () => Scroll(-1));
            _down = UiKit.ArtButton(Root, "ScrollDown", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 0f, 0f, 16f, () => Scroll(1));
            Root.gameObject.SetActive(false);
        }

        public void Open(float x, float y, IList<string> items, int selected)
        {
            _items = items;
            _selected = selected;
            int count = items != null ? items.Count : 0;
            if (count == 0) { Close(); return; }
            float max = 0f;
            for (int i = 0; i < count; i++) max = Mathf.Max(max, _texts[0].GetPreferredValues(items[i] ?? "").x);
            bool scroll = count > _shown;
            int rows = Math.Min(count, _shown);
            if (_title)
            {
                float maxWidth = max + 10f;
                _w = maxWidth + (scroll ? 76f : 56f);
                _h = rows * RowH + 26f;
            }
            else
            {
                _w = max + 20f + 30f;
                _h = rows * RowH + 30f;
            }
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, 384f - 8f - _w));   // ours: inside the 384-wide frame
            UiKit.Move(Root, x, y);
            UiKit.Size(Root, _w, _h);
            UiKit.Size(_blocker.Rt, _w, _h);
            _back.Place(0f, 0f, _w, _h);
            _top = scroll ? Mathf.Clamp(selected - _shown + 1, 0, count - _shown) : 0;
            _up.gameObject.SetActive(scroll);
            _down.gameObject.SetActive(scroll);
            if (scroll)
            {
                UiKit.Move(_up.Rt, _w - 30f, 12f);
                UiKit.Move(_down.Rt, _w - 30f, _h - 12f - 16f);
            }
            Layout();
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
        }

        public void Close()
        {
            if (Root != null && Root.gameObject.activeSelf) Root.gameObject.SetActive(false);
        }

        public void Tick(bool vr)
        {
            if (!IsOpen || _items == null || _items.Count <= _shown) return;
            float wheel = UiCharacterText.Wheel(Root, vr);
            if (wheel > 0.01f) Scroll(-1); else if (wheel < -0.01f) Scroll(1);
        }

        private void Scroll(int by)
        {
            if (_items == null) return;
            int t = Mathf.Clamp(_top + by, 0, Math.Max(0, _items.Count - _shown));
            if (t == _top) return;
            _top = t;
            Layout();
        }

        private void Layout()
        {
            int count = _items != null ? _items.Count : 0;
            bool scroll = count > _shown;
            float padX = _title ? 10f : 15f, padY = 15f;
            float rowW = _w - padX * 2f - (scroll ? 20f : 0f);
            for (int i = 0; i < _shown; i++)
            {
                int idx = _top + i;
                UiButton b = _rows[i];
                bool on = idx < count;
                if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
                if (!on) continue;
                UiKit.Move(b.Rt, padX, padY + i * RowH);
                UiKit.Size(b.Rt, rowW, RowH);
                UiKit.Size(b.Hilite.rectTransform, rowW, RowH);
                _texts[i].text = _items[idx] ?? "";
                _checks[i].enabled = idx == _selected && _checks[i].texture != null;
            }
            _up.Interactable = _top > 0;
            _down.Interactable = _top < count - _shown;
        }

        private void Pick(int k)
        {
            int idx = _top + k;
            if (_items == null || idx < 0 || idx >= _items.Count) return;
            Action<int> cb = Picked;
            if (cb != null) cb(idx);
        }
    }
}
