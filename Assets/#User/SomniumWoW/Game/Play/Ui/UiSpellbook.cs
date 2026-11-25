// iteration 11 (w0game, wave 0): moved verbatim from UiWindows.cs (PLAN11 s3) - owner vrcast from wave 1 on.
// iteration 11 (vrcast, proposal-vrcast.md 1.1 / 1.4 / 6.5, uicore H14, pets H12): the spell goes onto WoW's action cursor (UiActionCursor) -
// desktop: drag the icon (or Shift-click it, SpellBookFrame.lua:381-384), right-click it or click its name (kept); a click casts. VR: a tap on
// the icon OR the name lifts the spell (U5) and both wrist menus pop up (WowVrInput's slotting: grip, twist, push / pull, release). WoW's
// SpellBookFrameTabButton1 / 2 at the bottom (SpellBookFrame.xml:281-300): the Pet / Demon tab while the pet has spells opens the pet book.
// spellui (2026-10-04, the user: "split up the spells into their correct slots, they are all in one at the moment"): WoW's skill-line tabs
// SpellBookSkillLineTab1..8 down the right edge (SpellBookFrame.xml:9-44 template, :512-583 anchors: Tab1 TOPLEFT at the frame's TOPRIGHT
// (-32, -65), each next 17 below the previous; 32 x 32, SpellBook-SkillLineTab 64 x 64 behind at (-3, 11), the tab's texture as the normal
// texture, ButtonHilight-Square / CheckButtonHilight ADD), tooltip = the tab name (:33-36), the selected one checked
// (SpellBookFrame.lua:114-119), a page per tab (SPELLBOOK_PAGENUMBERS, :40-48 / :526 / :543), igAbiliityPageTurn on a change (:562-564).
// Which tab, the names, icons and order: UiSpellFacts.Tabs (Game/Play/Ui/Pure/UiSpellRules.cs, header (2), read out of Wow.exe).
// Ours: the selected tab and each tab's page follow the skill line (WoW keeps the tab index); no tab flash on learning.
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    // =====================================================================================================================
    /// <summary>
    /// SpellBookFrame (SpellBookFrame.xml): UI-SpellbookPanel four pieces, 12 spell buttons per page (SpellButton1 at (34, 85),
    /// column +157, row +50, left column first), name and rank beside each, page arrows at (50, 407) / (314, 407). Click an icon to
    /// cast; drag it (or Shift-click it, right-click it, click the name) to put the spell on an action button; "Clear a button" empties one.
    /// </summary>
    public sealed class UiSpellbook : UiWindow
    {
        public const string TabUnselected = @"Interface\SpellBook\UI-SpellBook-Tab-Unselected.blp";   // SpellBookFrame.xml:76
        public const string Tab1Selected = @"Interface\SpellBook\UI-SpellBook-Tab1-Selected.blp";     // :289 / :299 (DisabledTexture)
        public const string TabHighlight = @"Interface\SpellBook\UI-SpellbookPanel-Tab-Highlight.blp"; // :77 (ADD)
        private readonly UiButton[] _icon = new UiButton[12];
        private readonly UiButton[] _nameBtn = new UiButton[12];
        private readonly TextMeshProUGUI[] _name = new TextMeshProUGUI[12];
        private readonly TextMeshProUGUI[] _rank = new TextMeshProUGUI[12];
        private readonly string[] _iconPath = new string[12];
        private readonly bool[] _iconLoaded = new bool[12];
        private readonly int[] _spellAt = new int[12];
        private readonly TextMeshProUGUI _page;
        private readonly UiButton _prev, _next;
        private readonly RectTransform _tabs;
        private readonly UiButton _petTab;
        private readonly TextMeshProUGUI _petTabText;
        private int _pageIndex, _sig = int.MinValue, _shownPage = -1, _petTabShown = -1;
        // spellui: the skill-line tabs
        public const string SkillLineTabArt = @"Interface\SpellBook\SpellBook-SkillLineTab.blp";   // SpellBookFrame.xml:15
        private readonly UiButton[] _lineTab = new UiButton[UiSpellFacts.MaxTabs];
        private readonly string[] _lineTabIcon = new string[UiSpellFacts.MaxTabs];
        private readonly bool[] _lineTabIconLoaded = new bool[UiSpellFacts.MaxTabs];
        private List<SpellTab> _lineTabs = new List<SpellTab>();
        private readonly Dictionary<int, int> _pageOfLine = new Dictionary<int, int>();   // SPELLBOOK_PAGENUMBERS (ours: by skill line)
        private int _selLine, _shownTab = -1;
        private string _tabsLogged;

        public UiSpellbook(Transform parent, UiContext ctx, float x, float y, System.Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "Spellbook", x, y, W, H);
            UiKit.Frame4(Root, "Art", UiArt.SpellbookPanel + "TopLeft.blp", UiArt.SpellbookPanel + "TopRight.blp", UiArt.SpellbookPanel + "BotLeft.blp", UiArt.SpellbookPanel + "BotRight.blp");
            Portrait(UiArt.Tex(UiArt.SpellbookIcon));
            Title("Spellbook", 18f);
            UiKit.CloseButton(Root, 324f, 9f, close);
            for (int k = 0; k < 12; k++)
            {
                int i = k;
                float bx = 34f + (k / 6) * 157f, by = 85f + (k % 6) * 50f;
                _icon[k] = UiKit.Slot(Root, "Spell " + (k + 1), bx, by, 37f, true);
                _icon[k].Click = () => Cast(i);
                _icon[k].RightClick = () => Pick(i);
                _icon[k].Tip = () => SpellTip(i, true);
                // uicore H14: drag the icon = PickupSpell (the book never changes).
                UiKit.Draggable(_icon[k], () => SpellPayload(i), false);
                _nameBtn[k] = UiKit.Button(Root, "Spell " + (k + 1) + " name", bx + 39f, by, 112f, 37f);
                _nameBtn[k].Hilite = UiKit.Pic(_nameBtn[k].transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 0f, 4f, 112f, 30f);
                _nameBtn[k].Hilite.enabled = false;
                _nameBtn[k].Click = () => Pick(i);
                _nameBtn[k].Tip = () => SpellTip(i, false);
                _name[k] = UiKit.Text(Root, "Spell " + (k + 1) + " text", "", 11f, UiArt.Gold, TextAlignmentOptions.TopLeft, bx + 41f, by + 1f, 108f, 24f);
                _rank[k] = UiKit.Text(Root, "Spell " + (k + 1) + " rank", "", 10f, new Color(0.30f, 0.18f, 0f), TextAlignmentOptions.TopLeft, bx + 41f, by + 23f, 108f, 13f, null, false);
                _icon[k].gameObject.SetActive(false);
                _nameBtn[k].gameObject.SetActive(false);
            }
            _page = UiKit.Text(Root, "Page", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 127f, 402f, 102f, 15f);
            _prev = UiKit.ArtButton(Root, "Prev page", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.ButtonHilightSquare, 34f, 391f, 32f, () => { _pageIndex--; UiSound.Named("igAbiliityPageTurn"); });
            _next = UiKit.ArtButton(Root, "Next page", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.ButtonHilightSquare, 298f, 391f, 32f, () => { _pageIndex++; UiSound.Named("igAbiliityPageTurn"); });
            UiButton clear = UiKit.PanelButton(Root, "Clear a button", 132f, 378f, 112f, 20f, ClearAButton);
            clear.Tip = () => "<size=14>Clear a button</size>\n" + (VrSlotting ? "Then grip a hand, twist and push or pull to the button to empty, release." : "Then " + Verb.ToLowerInvariant() + " the action button to empty.");
            // SpellBookFrameTabButton1 CENTER at BOTTOMLEFT (79, 61), 128x64 (SpellBookFrame.xml:45-78, 281-290) - the selected (disabled) Spellbook
            // tab; Tab2 LEFT = Tab1 RIGHT + (-20, 0) (:291-300), shown while the pet has spells (SpellBookFrame.lua:127-139).
            _tabs = UiKit.Rect(Root, "Tabs", 0f, 0f, W, H);
            UiKit.Pic(_tabs, "SpellBookFrameTabButton1", UiArt.Tex(Tab1Selected), 79f - 64f, 512f - 61f - 32f, 128f, 64f);
            UiKit.Text(_tabs, "SpellBookFrameTabButton1 text", "Spellbook", 10f, Color.white, TextAlignmentOptions.Center, 79f - 64f, 512f - 61f - 32f - 3f, 128f, 64f);   // SPELLBOOK (GS 6903)
            _petTab = UiKit.Button(_tabs, "SpellBookFrameTabButton2", 79f + 64f - 20f, 512f - 61f - 32f, 128f, 64f);
            UiKit.Pic(_petTab.transform, "Normal", UiArt.Tex(TabUnselected), 0f, 0f, 128f, 64f);
            _petTab.Hilite = UiKit.Pic(_petTab.transform, "Highlight", UiArt.Add(TabHighlight), 0f, 0f, 128f, 64f);
            _petTab.Hilite.enabled = false;
            _petTabText = UiKit.Text(_petTab.transform, "Text", "Pet", 10f, UiArt.Gold, TextAlignmentOptions.Center, 0f, -3f, 128f, 64f);
            _petTab.Click = OpenPetBook;
            _petTab.Tip = () => "<size=14><color=#ffffff>" + _petTabText.text + "</color></size>";
            _petTab.gameObject.SetActive(false);
            // spellui: SpellBookSkillLineTab1..8 (SpellBookFrame.xml:512-583) - Tab1 TOPLEFT = the frame's TOPRIGHT + (-32, -65), each next one
            // TOPLEFT = the previous one's BOTTOMLEFT + (0, -17): x = W - 32, y = 65 + 49 i.
            for (int i = 0; i < _lineTab.Length; i++)
            {
                int k = i;
                float tx = W - 32f, ty = 65f + i * (32f + 17f);
                UiButton t = UiKit.Button(Root, "SpellBookSkillLineTab" + (i + 1), tx, ty, 32f, 32f);
                UiKit.Pic(t.transform, "Background", UiArt.Tex(SkillLineTabArt), -3f, -11f, 64f, 64f);   // BACKGROUND layer, TOPLEFT (-3, 11)
                t.Icon = UiKit.Pic(t.transform, "Normal", null, 0f, 0f, 32f, 32f);                           // NormalTexture = GetSpellTabInfo's texture
                t.Icon.enabled = false;
                t.Hilite = UiKit.Pic(t.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 32f, 32f);
                t.Hilite.enabled = false;
                t.Checked = UiKit.Pic(t.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 32f, 32f);
                t.Checked.enabled = false;
                t.Click = () => SelectTab(k);
                t.Tip = () => k < _lineTabs.Count ? "<size=14><color=#ffffff>" + _lineTabs[k].Name + "</color></size>" : null;   // GameTooltip:SetText(self.tooltip)
                t.gameObject.SetActive(false);
                _lineTab[i] = t;
            }
            Root.gameObject.SetActive(false);
        }

        private SpellTab CurrentTab
        {
            get
            {
                for (int i = 0; i < _lineTabs.Count; i++) if (_lineTabs[i].SkillLine == _selLine) return _lineTabs[i];
                return _lineTabs.Count > 0 ? _lineTabs[0] : null;
            }
        }

        private int CurrentTabIndex
        {
            get
            {
                for (int i = 0; i < _lineTabs.Count; i++) if (_lineTabs[i].SkillLine == _selLine) return i;
                return 0;
            }
        }

        /// <summary>SpellBookSkillLineTab_OnClick (SpellBookFrame.lua:556-582): the page sound on a change, that tab's own page.</summary>
        private void SelectTab(int i)
        {
            if (i < 0 || i >= _lineTabs.Count) return;
            int line = _lineTabs[i].SkillLine;
            SpellTab cur = CurrentTab;
            if (cur != null && cur.SkillLine == line) return;
            if (cur != null) _pageOfLine[cur.SkillLine] = _pageIndex;
            UiSound.Named("igAbiliityPageTurn");
            _selLine = line;
            int page;
            _pageIndex = _pageOfLine.TryGetValue(line, out page) ? page : 0;
            _shownPage = -1;
        }

        /// <summary>The tabs of the player's book (UiSpellFacts.Tabs; one General tab with every spell if the files are not readable).</summary>
        private void RebuildTabs(PlayerState p)
        {
            SpellTab before = CurrentTab;
            if (before != null) _pageOfLine[before.SkillLine] = _pageIndex;
            UiSpellFacts facts = WowPlay.Files != null ? UiSpellFacts.Get(WowPlay.Files) : null;
            string general = UiGlobalStrings.G("GENERAL_SPELLS", UiSpellFacts.GeneralFallback);   // GlobalStrings.lua:3792
            if (facts != null && facts.Ok) _lineTabs = facts.Tabs(p.Spellbook, p.Race, p.Class, general);
            else
            {
                var all = new SpellTab { SkillLine = 0, Name = general, IconPath = UiSpellFacts.GeneralIcon };
                all.Spells.AddRange(p.Spellbook);
                _lineTabs = new List<SpellTab> { all };
            }
            SpellTab now = CurrentTab;
            if (now != null && (before == null || before.SkillLine != now.SkillLine)) { _selLine = now.SkillLine; int pg; _pageIndex = _pageOfLine.TryGetValue(_selLine, out pg) ? pg : 0; }
            var sb = new StringBuilder();
            for (int i = 0; i < _lineTabs.Count; i++) sb.Append(i > 0 ? ", " : "").Append(_lineTabs[i].Name).Append(" (").Append(_lineTabs[i].Spells.Count).Append(')');
            string summary = sb.ToString();
            if (summary != _tabsLogged)
            {
                _tabsLogged = summary;
                WowLog.Info("UI spellbook: tabs " + summary + " for " + p.Class + " (race " + p.Race + ", " + p.Spellbook.Count + " spells) - Wow.exe's rule: SkillLineAbility -> SkillRaceClassInfo flag 0x80 = General.");
            }
            for (int i = 0; i < _lineTab.Length; i++) { _lineTabIcon[i] = null; _lineTabIconLoaded[i] = false; }
            _shownTab = -1;
            _shownPage = -1;
        }

        /// <summary>SpellBookFrame_Update (SpellBookFrame.lua:103-124): a tab per skill line, its texture, the selected one checked.</summary>
        private void RefreshTabButtons()
        {
            int sel = CurrentTabIndex;
            for (int i = 0; i < _lineTab.Length; i++)
            {
                UiButton t = _lineTab[i];
                bool on = i < _lineTabs.Count;
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
                if (!on) continue;
                string path = _lineTabs[i].IconPath;
                if (!ReferenceEquals(path, _lineTabIcon[i])) { _lineTabIcon[i] = path; _lineTabIconLoaded[i] = false; }
                if (!_lineTabIconLoaded[i])
                {
                    Texture2D tex = UiArt.Icon(path);
                    // lead fix 2026-10-10 (the user, a screenshot: the tabs blank): UiKit.Pic made the picture without a texture, so its colour
                    // was clear (0, 0, 0, 0) - the loaded icon was drawn invisible; white as WoW's NormalTexture
                    if (tex != null) { t.Icon.texture = tex; t.Icon.color = Color.white; t.Icon.enabled = true; _lineTabIconLoaded[i] = true; }
                }
                if (_shownTab != sel) t.Checked.enabled = i == sel;
            }
            _shownTab = sel;
        }

        /// <summary>VR without the forearm bar: the spell is placed through the wrist menus (WowVrInput's slotting).</summary>
        private bool VrSlotting { get { return Ctx.Vr && !WowSettings.VrForearmBar; } }
        private string Verb { get { return Ctx.Vr ? "Tap" : "Click"; } }

        private AbilityView SpellAt(int k)
        {
            SpellTab t = CurrentTab;   // spellui: the selected skill-line tab (SpellBook_GetSpellID: offset + page, SpellBookFrame.lua:591-600)
            int idx = _pageIndex * 12 + k;
            return t != null && k >= 0 && idx < t.Spells.Count ? t.Spells[idx] : null;
        }

        private static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private void Cast(int k)
        {
            AbilityView v = SpellAt(k);
            if (v == null || Ctx.S == null) return;
            // VR: a tap on the icon lifts the spell too (U5); desktop: Shift-click picks it up (PICKUPACTION, SpellBookFrame.lua:381-384),
            // and a click while something is on the cursor picks this spell instead.
            if (VrSlotting || UiActionCursor.Active || (!Ctx.Vr && ShiftHeld())) { Pick(k); return; }
            Ctx.S.CastSpell(v.SpellId, 0);
        }

        /// <summary>The drag payload of a spellbook icon (passive spells cannot be picked up).</summary>
        private UiPickupPayload SpellPayload(int k)
        {
            AbilityView v = SpellAt(k);
            if (v == null || v.Passive) return null;
            return new UiPickupPayload { Kind = UiPickupKind.Spell, Packed = WowActionSlots.Spell(v.SpellId), SpellId = v.SpellId, Name = v.Name, IconPath = v.IconPath };
        }

        private void Pick(int k)
        {
            AbilityView v = SpellAt(k);
            if (v == null) return;
            if (v.Passive) { Ctx.Say(v.Name + " is passive.", UiArt.ErrorRed); return; }
            if (UiActionCursor.Active && UiActionCursor.Packed == WowActionSlots.Spell(v.SpellId)) { UiActionCursor.Clear(); Ctx.Say("Cancelled.", UiArt.InfoYellow); return; }
            UiActionCursor.Pick(WowActionSlots.Spell(v.SpellId), v.Name, v.IconPath, -1, false);
            Ctx.Say(VrSlotting ? "Grip a hand, twist and push or pull to choose a slot, release to place " + v.Name + "."
                               : Verb + " an action button to place " + v.Name + ".", UiArt.InfoYellow);
        }

        private void ClearAButton()
        {
            UiActionCursor.Pick(0, "", null, -1, false);
            Ctx.Say(VrSlotting ? "Grip a hand, twist and push or pull to choose a slot, release to clear it." : Verb + " an action button to clear it.", UiArt.InfoYellow);
        }

        private string SpellTip(int k, bool icon)
        {
            AbilityView v = SpellAt(k);
            if (v == null) return null;
            string hint;
            if (v.Passive) hint = null;
            else if (VrSlotting) hint = "Tap to put it on a bar: then grip a hand, twist, push or pull, release.";
            else if (Ctx.Vr) hint = icon ? "Tap to cast. Tap the name to put it on an action button." : "Tap to put it on an action button.";
            else hint = icon ? "Click to cast. Drag it (or Shift-click it) onto an action button." : "Click to put it on an action button.";
            return UiTooltip.Ability(v, hint != null ? "<color=#00ff00>" + hint + "</color>" : null);
        }

        /// <summary>pets H12: the pet book (UiWindowKind.PetBook, the pets owner's window) - shown only while that window is registered and
        /// visible (not Hidden), so the tab never does nothing.</summary>
        private void OpenPetBook()
        {
            if (Ctx.Toggle == null) return;
            if (Ctx.IsOpen == null || !Ctx.IsOpen(UiWindowKind.PetBook)) Ctx.Toggle(UiWindowKind.PetBook);
            if (Ctx.IsOpen != null && Ctx.IsOpen(UiWindowKind.Spellbook)) Ctx.Toggle(UiWindowKind.Spellbook);   // WoW swaps the book's content
        }

        private bool PetTabWanted(PlayerState p)
        {
            if (p == null || p.ControlBar == null || p.ControlBar.Spells.Count == 0) return false;
            UiWindowSpec spec = UiRegistry.Of(UiWindowKind.PetBook);
            return spec != null && !spec.Hidden;
        }

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            List<AbilityView> book = p.Spellbook;
            int sig = book.Count * 7919 + p.Level + p.Race * 131 + (int)p.Class * 1009;
            for (int i = 0; i < book.Count; i++) if (book[i] != null) sig = sig * 31 + book[i].SpellId;
            bool rebuilt = sig != _sig;
            if (rebuilt) { _sig = sig; RebuildTabs(p); }   // SPELLS_CHANGED -> SpellBookFrame_Update (SpellBookFrame.lua:58-61)
            RefreshTabButtons();
            SpellTab tab = CurrentTab;
            int pages = Mathf.Max(1, ((tab != null ? tab.Spells.Count : 0) + 11) / 12);   // SpellBook_GetCurrentPage: ceil(numSpells / 12) of the tab
            _pageIndex = Mathf.Clamp(_pageIndex, 0, pages - 1);
            if (rebuilt || _shownPage != _pageIndex)
            {
                _shownPage = _pageIndex;
                _page.SetText("Page {0}", _pageIndex + 1);
                _prev.Interactable = _pageIndex > 0;
                _next.Interactable = _pageIndex < pages - 1;
                for (int k = 0; k < 12; k++)
                {
                    AbilityView v = SpellAt(k);
                    bool on = v != null;
                    _icon[k].gameObject.SetActive(on);
                    _nameBtn[k].gameObject.SetActive(on);
                    _name[k].enabled = on;
                    _rank[k].enabled = on;
                    _spellAt[k] = on ? v.SpellId : 0;
                    if (!on) continue;
                    _name[k].text = v.Name ?? "";
                    // Racial abilities keep their Spell.dbc rank text (AbilityView.Rank = SpellInfo.Rank, OfflineSession.Spells.cs:969): "Racial" / "Racial Passive".
                    _rank[k].text = v.Passive ? (v.Rank != null && v.Rank.StartsWith("Racial", System.StringComparison.Ordinal) ? (v.Rank == "Racial" ? "Racial Passive" : v.Rank) : "Passive") : string.IsNullOrEmpty(v.Rank) ? "" : v.Rank;
                    _iconLoaded[k] = false;
                    _icon[k].Icon.enabled = false;
                }
            }
            for (int k = 0; k < 12; k++)
            {
                if (_spellAt[k] == 0) continue;
                AbilityView v = SpellAt(k);
                if (v == null) continue;
                if (SetIcon(_icon[k], v.IconPath, ref _iconPath[k], ref _iconLoaded[k]))
                {
                    Color c = v.Passive ? new Color(0.75f, 0.75f, 0.75f) : (v.UnusableReason != null && v.UnusableReason.EndsWith("offline", System.StringComparison.Ordinal)) ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
                    if (_icon[k].Icon.color != c) _icon[k].Icon.color = c;
                }
            }
            bool pet = PetTabWanted(p);
            if ((pet ? 1 : 0) != _petTabShown)
            {
                _petTabShown = pet ? 1 : 0;
                _petTab.gameObject.SetActive(pet);
                // HasPetSpells' token: PET_TYPE_DEMON "Demon" / PET_TYPE_PET "Pet" (GlobalStrings.lua:5650-5651); a warlock's pets are demons.
                if (pet) _petTabText.text = p.Class == WowClass.Warlock ? "Demon" : "Pet";
            }
        }
    }
}
