using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Iteration 7 (owner UI+Audio, PLAN7 s5.6): the Mounts window, WoW 3.3.5a's Companions / Mounts pane (PetPaperDollFrame.xml,
    /// PetPaperDollFrameCompanionFrame) on the art read from the install. All positions from the XML (converted to the 384x512 frame's
    /// top-left units): UI-Character-General-TopLeft at TOPLEFT (2, -1), -TopRight at TOPRIGHT (2, -1), UI-PetPaperDollFrame-BotLeft
    /// at TOPLEFT (2, -257), -BotRight at TOPLEFT (258, -257); the portrait 60x60 at (7, -6), the title (PetNameText = MOUNTS) and the
    /// close button (CharacterFrame.xml); the model backdrop UI-PetFrame-Frame 320x235 at CENTER (-8, 64) - it shows the selected
    /// mount's BIG ICON and one line "Model: server | baked | learned | stand-in" (a 3D preview is deferred, PLAN7 s7.1);
    /// CompanionSelectedName 290x13 at CENTER (-13, 0); the big button 150x25 at CENTER (-13, -25) reading MOUNT "Mount" or
    /// BINDING_NAME_DISMOUNT "Dismount" (PetPaperDollFrame.lua 407-414); the slot tray UI-PetFrame-Slots 338x128 at BOTTOMLEFT (13, 79);
    /// 12 CompanionButtons 37x37 (1 at TOPLEFT (60, -316); 2..6 RIGHT of the previous + 4/4/5/4/5; 7 below 1 at (0, -5); 8..12 the same),
    /// empty = UI-PetFrame-Slots-Mounts (the horse silhouette), selected = UI-Button-Outline (ADD, 66x66 centred), ridden =
    /// CheckButtonHilight (ADD); the page arrows (the spellbook's art) at BOTTOMLEFT (110, 83) and +80, "Page %s of %s"
    /// (MERCHANT_PAGE_NUMBER) at CENTER (-13, -155). Sorted by name.
    /// Clicks as WoW's CompanionButton_OnClick: a click selects, a click on the selected mount (so a double-click) or the big button
    /// mounts / dismounts; right-click a slot (VR: tap the name) then an action button = put it on the bar (the spellbook's gesture).
    /// Dreamport additions (README): the J key and the VR palm button open it (WoW has no micro button for mounts); a footer line
    /// (the testing switch, online, the exclusions); the "Testing (GM)" row, online with a GM automatic account only (lead decision
    /// D13): "Learn riding + mounts" and "Go to Outland (test flying)" send GM commands through IWowSession.RunGmCommand.
    /// </summary>
    public sealed class UiMounts : UiWindow
    {
        public const int PerPage = 12;   // NUM_COMPANIONS_PER_PAGE
        public const string GeneralTopLeft = @"Interface\PaperDollInfoFrame\UI-Character-General-TopLeft.blp";
        public const string GeneralTopRight = @"Interface\PaperDollInfoFrame\UI-Character-General-TopRight.blp";
        public const string PetBotLeft = @"Interface\PetPaperDollFrame\UI-PetPaperDollFrame-BotLeft.blp";
        public const string PetBotRight = @"Interface\PetPaperDollFrame\UI-PetPaperDollFrame-BotRight.blp";
        public const string PetFrameArt = @"Interface\PetPaperDollFrame\UI-PetFrame-Frame.blp";
        public const string SlotsArt = @"Interface\PetPaperDollFrame\UI-PetFrame-Slots.blp";
        public const string SlotsMountsArt = @"Interface\PetPaperDollFrame\UI-PetFrame-Slots-Mounts.blp";
        public const string ButtonOutline = @"Interface\Buttons\UI-Button-Outline.blp";
        /// <summary>The VR palm button's icon (SpellIcon of 458 Brown Horse, PLAN7 s4.1).</summary>
        public const string MountsIcon = @"Interface\Icons\Ability_Mount_RidingHorse";

        // CompanionButton1..6 x (TOPLEFT 60, then + 37 + 4/4/5/4/5) and the two rows (-316, then -5 below).
        private static readonly float[] SlotX = { 60f, 101f, 142f, 184f, 225f, 267f };
        private static readonly float[] SlotY = { 316f, 358f };

        // Lead decision D13: "Go to Outland (test flying)" - acore_world.game_tele id 475 'HellfirePeninsula', map 530,
        // -211.237 4278.54 86.5678 (verified on the user's realm; also P/harness/Probes/Mount7/online/acsrc/game_tele.sql:516).
        public const string OutlandTele = ".tele HellfirePeninsula";

        /// <summary>
        /// Ours (PLAN7 s7.4 names ".learn id"): the button sends ".player learn NAME id" instead - the same handler
        /// (Acore::PlayerCommand::HandleLearnSpellCommand) and the same security 2 ('learn' and 'player learn' rows of command.sql, lines
        /// 317 / 468), but with the character's own name, because ".learn" teaches the SELECTED player: with an NPC targeted it answers
        /// "Player not found" and with another player targeted it would teach them (cs_learn.cpp 70-81 getSelectedPlayer, Chat.cpp 382-392;
        /// cs_player.cpp 33 / 44-53). False = the lead's literal ".learn id" (after clearing the target).
        /// </summary>
        public const bool LearnByName = true;

        private static readonly int[] RidingSpells = { MountCatalog.ApprenticeRiding, MountCatalog.JourneymanRiding, MountCatalog.ExpertRiding, MountCatalog.ArtisanRiding, MountCatalog.ColdWeatherFlying };
        private static readonly int[] RidingSkillOf = { 75, 150, 225, 300, -1 };   // SkillLine 762 value each rank gives (PLAN7 s4.1); -1 = Cold Weather Flying

        private readonly UiButton[] _slot = new UiButton[PerPage];
        private readonly RawImage[] _empty = new RawImage[PerPage];
        private readonly RawImage[] _outline = new RawImage[PerPage];
        private readonly RawImage[] _ridden = new RawImage[PerPage];
        private readonly string[] _iconPath = new string[PerPage];
        private readonly bool[] _iconLoaded = new bool[PerPage];
        private readonly int[] _spellAt = new int[PerPage];
        private readonly UiButton _big, _nameBtn, _summon, _prev, _next, _gmLearn, _gmTele;
        private readonly RawImage _portrait;
        private readonly TextMeshProUGUI _name, _source, _footer, _gmLabel, _page;
        private string _bigPath;
        private bool _bigLoaded;
        private readonly List<AbilityView> _sorted = new List<AbilityView>();
        private readonly List<AbilityView> _scratch = new List<AbilityView>();
        private int _pageIndex, _sig = int.MinValue, _shownSig = int.MinValue, _selected, _sourceSig = int.MinValue, _gmShownFor = -1;
        private string _footerShown, _sourceShown, _summonShown;
        private IWowSession _forSession;
        private WowClass _cls = (WowClass)255;
        private float _gmBusyUntil;
        private int _actions;
        private string _exclusions;
        private bool _exclusionsDone;

        // Iteration 11 (integrator, pets H11): WoW's PetPaperDollFrameTab2 COMPANIONS / Tab3 MOUNTS (PetPaperDollFrame.xml:937-980).
        // Tab1 PET is not drawn (no pet paperdoll - gap). The tabs are two small panel buttons (ours: TabButtonTemplate art not measured).
        public const string SlotsCompanionsArt = @"Interface\PetPaperDollFrame\UI-PetFrame-Slots-Companions.blp";   // PetPaperDollFrame.lua:206
        private bool _critters;
        private UiButton _tabCompanions, _tabMounts;
        private TextMeshProUGUI _title;
        private bool _tabsShownFor, _tabsInit, _critterLogged;
        private Texture _emptyMountTex, _emptyCritterTex;

        public UiMounts(Transform parent, UiContext ctx, float x, float y, System.Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "Mounts", x, y, W, H);
            // PetPaperDollFrame's four pieces (offset 2, 1 like the XML), then the companion frame's backdrop and slot tray.
            RectTransform art = UiKit.Rect(Root, "Art", 0f, 0f, W, H);
            UiKit.Pic(art, "TopLeft", UiArt.Tex(GeneralTopLeft), 2f, 1f, 256f, 256f);
            UiKit.Pic(art, "TopRight", UiArt.Tex(GeneralTopRight), 258f, 1f, 128f, 256f);
            UiKit.Pic(art, "BotLeft", UiArt.Tex(PetBotLeft), 2f, 257f, 256f, 256f);
            UiKit.Pic(art, "BotRight", UiArt.Tex(PetBotRight), 258f, 257f, 128f, 256f);
            UiKit.Pic(art, "Slot tray", UiArt.Tex(SlotsArt), 0.009765625f, 0.66015625f, 0f, 1f, 13f, 512f - 79f - 128f, 338f, 128f);
            UiKit.Pic(art, "Model backdrop", UiArt.Tex(PetFrameArt), 0f, 0.6171875f, 0f, 0.890625f, 192f - 8f - 160f, 256f - 64f - 117.5f, 320f, 235f);
            _portrait = Portrait(UiArt.Tex(UiArt.ClassCircles));
            // PetNameText (GameFontHighlight) = MOUNTS "Mounts" (GlobalStrings.lua:4980), at CharacterNameText's place.
            TextMeshProUGUI title = UiKit.Text(Root, "Title", "Mounts", 13f, Color.white, TextAlignmentOptions.Center, 78f, 16f, 240f, 17f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            // CharacterFrameCloseButton: CENTER at TOPRIGHT (-44, -25), 32x32.
            UiKit.CloseButton(Root, 384f - 44f - 16f, 25f - 16f, close);

            // The selected mount: its big icon in the model area (TOPLEFT (39, -90), 288x160), the model source line, the footer.
            _big = UiKit.Slot(Root, "Selected mount icon", 183f - 32f, 98f, 64f, true);
            _big.Click = () => PickSelected();
            _big.RightClick = () => PickSelected();
            _big.Tip = () => SelectedTip();
            _source = UiKit.Text(Root, "Model source", "", 10f, new Color(0.85f, 0.85f, 0.85f), TextAlignmentOptions.Center, 34f, 168f, 290f, 14f);
            _source.textWrappingMode = TextWrappingModes.NoWrap;
            _footer = UiKit.Text(Root, "Footer", "", 9.5f, new Color(1f, 0.82f, 0f), TextAlignmentOptions.Top, 40f, 184f, 278f, 60f);
            // The Testing (GM) row (online + an automatic GM account only).
            _gmLabel = UiKit.Text(Root, "Testing (GM)", "Testing (GM)", 10f, new Color(0.3f, 1f, 0.3f), TextAlignmentOptions.Center, 34f, 206f, 290f, 13f);
            _gmLearn = UiKit.PanelButton(Root, "Learn riding + mounts", 32f, 221f, 150f, 22f, GmLearn);
            _gmLearn.Label.fontSize = 10f;
            _gmLearn.Tip = () => "<size=14>Learn riding + mounts</size>\n<color=#ffd100>Dreamport testing tool.</color> Asks your realm (GM commands sent as chat lines) to teach this character every riding skill, Cold Weather Flying and every mount of its faction and class. The realm's answers appear as messages.";
            _gmTele = UiKit.PanelButton(Root, "Go to Outland (test flying)", 186f, 221f, 150f, 22f, GmTele);
            _gmTele.Label.fontSize = 10f;
            _gmTele.Tip = () => "<size=14>Go to Outland (test flying)</size>\n<color=#ffd100>Dreamport testing tool.</color> GM teleport to Hellfire Peninsula (" + OutlandTele + "), where flying mounts may fly.";
            SetGmShown(false);

            // CompanionSelectedName (DialogButtonNormalText) - a button: tap it (VR) to put the selected mount on an action button.
            _nameBtn = UiKit.Button(Root, "Selected mount name", 192f - 13f - 145f, 256f - 6.5f - 2f, 290f, 17f);
            _nameBtn.Hilite = UiKit.Pic(_nameBtn.transform, "Highlight", UiArt.Add(UiArt.QuestTitleHighlight), 45f, 0f, 200f, 17f);
            _nameBtn.Hilite.enabled = false;
            _nameBtn.Click = () => PickSelected();
            _nameBtn.RightClick = () => PickSelected();
            _nameBtn.Tip = () => SelectedTip();
            // 17 units tall (the XML's 13 is the font box): TMP hides a line whose height does not fit its rect (UiKit.Text).
            _name = UiKit.Text(_nameBtn.transform, "Text", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 290f, 17f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            // CompanionSummonButton 150x25 at CENTER (-13, -25).
            _summon = UiKit.PanelButton(Root, "Mount", 192f - 13f - 75f, 256f + 25f - 12.5f, 150f, 25f, Summon);
            _summon.Tip = () => SelectedTip();

            for (int k = 0; k < PerPage; k++)
            {
                int i = k;
                float bx = SlotX[k % 6], by = SlotY[k / 6];
                // The disabled look of an empty CompanionButton (the horse silhouette), under the button.
                _empty[k] = UiKit.Pic(Root, "Mount slot " + (k + 1) + " empty", UiArt.Tex(SlotsMountsArt), 0.203125f, 0.765625f, 0.203125f, 0.7625625f, bx, by, 37f, 37f);
                UiButton b = UiKit.Slot(Root, "Mount slot " + (k + 1), bx, by, 37f, false);
                _outline[k] = UiKit.Pic(b.transform, "Checked", UiArt.Add(ButtonOutline), (37f - 66f) * 0.5f, (37f - 66f) * 0.5f, 66f, 66f);
                _outline[k].enabled = false;
                _ridden[k] = UiKit.Pic(b.transform, "Active", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 37f, 37f);
                _ridden[k].enabled = false;
                b.Click = () => SlotClick(i);
                b.RightClick = () => SlotRightClick(i);
                b.Tip = () => SlotTip(i);
                b.gameObject.SetActive(false);
                _slot[k] = b;
            }
            // CompanionPrevPageButton BOTTOMLEFT (110, 83) 32x32; Next LEFT of its RIGHT + 80; UI-Common-MouseHilight (ADD).
            _prev = UiKit.ArtButton(Root, "Prev page", UiArt.PrevUp, UiArt.PrevDown, UiArt.PrevDisabled, UiArt.MouseHilight, 110f, 512f - 83f - 32f, 32f, () => { _pageIndex--; UiSound.Named("igAbiliityPageTurn"); });
            _next = UiKit.ArtButton(Root, "Next page", UiArt.NextUp, UiArt.NextDown, UiArt.NextDisabled, UiArt.MouseHilight, 110f + 32f + 80f, 512f - 83f - 32f, 32f, () => { _pageIndex++; UiSound.Named("igAbiliityPageTurn"); });
            // CompanionPageNumber (GameFontHighlightSmallOutline) CENTER (-13, -155).
            _page = UiKit.Text(Root, "Page", "", 10f, Color.white, TextAlignmentOptions.Center, 179f - 60f, 411f - 7f, 120f, 14f);
            _page.textWrappingMode = TextWrappingModes.NoWrap;
            _title = title;
            _emptyMountTex = _empty[0] != null ? _empty[0].texture : null;
            _emptyCritterTex = UiArt.Tex(SlotsCompanionsArt);
            // PetPaperDollFrameTab2 / Tab3: Tab1's spot TOPLEFT (70, -39), each next LEFT = previous RIGHT (ours: 100 x 22 panel buttons).
            _tabCompanions = UiKit.PanelButton(Root, UiGlobalStrings.G("COMPANIONS", "Companions"), 70f, 39f, 100f, 22f, () => SetCritters(true));
            _tabMounts = UiKit.PanelButton(Root, UiGlobalStrings.G("MOUNTS", "Mounts"), 170f, 39f, 100f, 22f, () => SetCritters(false));
            _tabCompanions.Label.fontSize = 10f;
            _tabMounts.Label.fontSize = 10f;
            _tabCompanions.gameObject.SetActive(false);
            _tabMounts.gameObject.SetActive(false);
            UiCharacterTabs.Build(Root, ctx, UiWindowKind.Mounts);   // integrator (character request): the CharacterFrame tab strip
            Root.gameObject.SetActive(false);
        }

        /// <summary>pets H11: the Companions / Mounts tab (PetPaperDollFrame_SetCompanionTab, ours as panel buttons).</summary>
        private void SetCritters(bool critters)
        {
            if (_critters == critters) return;
            _critters = critters;
            _selected = 0;
            _pageIndex = 0;
            _sig = int.MinValue;
            _shownSig = int.MinValue;
            _sourceSig = int.MinValue;
            _footerShown = null;
            _gmShownFor = -1;
            UiSound.Named("igCharacterInfoTab");
            _title.text = critters ? UiGlobalStrings.G("COMPANIONS", "Companions") : UiGlobalStrings.G("MOUNTS", "Mounts");   // GlobalStrings COMPANIONS / MOUNTS
            Texture t = critters ? _emptyCritterTex : _emptyMountTex;
            for (int k = 0; k < PerPage; k++) if (_empty[k] != null) _empty[k].texture = t;
            _tabCompanions.Interactable = !critters;
            _tabMounts.Interactable = critters;
            if (!_critterLogged && critters) { _critterLogged = true; WowLog.Info("UI: Mounts window - Companions tab (PetPaperDollFrame.xml:937-980; tabs are panel buttons - ours)."); }
        }

        // ------------------------------------------------------------------ the list

        private AbilityView At(int k)
        {
            int idx = _pageIndex * PerPage + k;
            return idx >= 0 && idx < _sorted.Count ? _sorted[idx] : null;
        }

        private AbilityView Selected()
        {
            for (int i = 0; i < _sorted.Count; i++) if (_sorted[i] != null && _sorted[i].SpellId == _selected) return _sorted[i];
            return null;
        }

        /// <summary>The ridden mount: Player.Mounted with this mount spell. (Integrator: not AbilityView.Active - offline Active is also
        /// true DURING the mount's cast (the action button's "current spell" state), which made the pane read "Dismount" and light the
        /// ridden glow before the mount-up, and the button then did nothing; WoW's pane shows the summoned companion only.)</summary>
        private bool Ridden(AbilityView v)
        {
            PlayerState p = Ctx.P;
            if (_critters) return v != null && p != null && p.CompanionGuid != 0 && p.CompanionSpellId == v.SpellId;   // pets H11: the summoned companion
            return v != null && p != null && p.Mounted && p.MountSpellId == v.SpellId;
        }

        private static int CompareByName(AbilityView a, AbilityView b)
        {
            int c = string.Compare(a.Name ?? "", b.Name ?? "", System.StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : a.SpellId.CompareTo(b.SpellId);
        }

        /// <summary>Re-sorts Player.Mounts by name when the list changed (its ids and count).</summary>
        private void SyncList(PlayerState p)
        {
            List<AbilityView> m = _critters ? p.Companions : p.Mounts;   // pets H11
            int sig = m != null ? m.Count * 7919 : 0;
            if (m != null) for (int i = 0; i < m.Count; i++) if (m[i] != null) sig = sig * 31 + m[i].SpellId;
            if (sig == _sig && _sorted.Count == (m != null ? CountNonNull(m) : 0))
            {
                // Same ids: keep the (maybe rebuilt) instances current.
                if (m != null) ReplaceInstances(m);
                return;
            }
            _sig = sig;
            _sorted.Clear();
            if (m != null) for (int i = 0; i < m.Count; i++) if (m[i] != null) _sorted.Add(m[i]);
            _sorted.Sort(CompareByName);
            if (Selected() == null) _selected = _sorted.Count > 0 ? _sorted[0].SpellId : 0;   // PetPaperDollFrame: idMount = the first mount
        }

        private static int CountNonNull(List<AbilityView> m)
        {
            int n = 0;
            for (int i = 0; i < m.Count; i++) if (m[i] != null) n++;
            return n;
        }

        private void ReplaceInstances(List<AbilityView> m)
        {
            for (int i = 0; i < _sorted.Count; i++)
            {
                AbilityView v = _sorted[i];
                if (v == null) continue;
                for (int j = 0; j < m.Count; j++)
                {
                    AbilityView w = m[j];
                    if (w != null && w.SpellId == v.SpellId) { if (!ReferenceEquals(w, v)) _sorted[i] = w; break; }
                }
            }
        }

        // ------------------------------------------------------------------ clicks

        private void SlotClick(int k)
        {
            AbilityView v = At(k);
            if (v == null) return;
            if (Ctx.PickSpell >= 0) { Pick(v); return; }
            UiSound.Named("igMainMenuOptionCheckBoxOn");   // CompanionButtonTemplate OnClick
            // CompanionButton_OnClick: a click on the selected mount calls (or dismisses) it; on another one it selects it.
            if (v.SpellId == _selected) Act(v, false);
            else { _selected = v.SpellId; _shownSig = int.MinValue; }
        }

        private void SlotRightClick(int k)
        {
            AbilityView v = At(k);
            if (v == null) return;
            if (_selected != v.SpellId) { _selected = v.SpellId; _shownSig = int.MinValue; }
            Pick(v);
        }

        private void PickSelected()
        {
            AbilityView v = Selected();
            if (v != null) Pick(v);
        }

        /// <summary>The spellbook's gesture: the mount waits for an action button (UiActionSlot places it; SetActionBarSlot accepts known mounts).</summary>
        private void Pick(AbilityView v)
        {
            if (Ctx.PickSpell == v.SpellId) { Ctx.CancelPick(); Ctx.Say("Cancelled.", UiArt.InfoYellow); return; }
            Ctx.StartPick(v.SpellId, v.Name);
        }

        private void Summon()
        {
            AbilityView v = Selected();
            if (v == null) return;
            Act(v, true);
        }

        /// <summary>CompanionSummonButton_OnClick: Dismount when the selected mount is ridden (sound On), else cast it (sound Off).</summary>
        private void Act(AbilityView v, bool buttonSound)
        {
            IWowSession s = Ctx.S;
            if (s == null || v == null) return;
            bool ridden = Ridden(v);
            if (buttonSound) UiSound.Named(ridden ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            if (_critters)
            {
                // pets H11 (PetPaperDollFrame.lua:409-413): Summon = the companion's spell, Dismiss = DismissCompanion
                if (_actions++ < 40) WowLog.Info("UI: Mounts window - " + (ridden ? "Dismiss" : "Summon") + " companion '" + v.Name + "' (spell " + v.SpellId + ").");
                if (ridden) s.DismissCompanion();
                else s.CastSpell(v.SpellId, 0);
                return;
            }
            if (_actions++ < 40) WowLog.Info("UI: Mounts window - " + (ridden ? "Dismount" : "Mount") + " '" + v.Name + "' (spell " + v.SpellId + ").");
            if (ridden) s.Dismount();
            else s.CastSpell(v.SpellId, 0);
        }

        // ------------------------------------------------------------------ tooltips

        private string SlotTip(int k)
        {
            AbilityView v = At(k);
            if (v == null) return null;
            string verb = _critters ? (Ridden(v) ? "dismiss" : "summon") : (Ridden(v) ? "dismount" : "mount");
            string hint = Ctx.PickSpell >= 0 ? "<color=#00ff00>Tap to pick this " + (_critters ? "companion" : "mount") + " instead.</color>"
                : Ctx.Vr ? "<color=#00ff00>Tap to select, tap again to " + verb + ". Tap the name, then an action button, to put it on the bar.</color>"
                : "<color=#00ff00>Click to select, click again to " + verb + ". Right-click, then an action button, to put it on the bar.</color>";
            return UiTooltip.Ability(v, hint);
        }

        private string SelectedTip()
        {
            AbilityView v = Selected();
            if (v == null) return null;
            return UiTooltip.Ability(v, "<color=#00ff00>" + (Ctx.Vr ? "Tap" : "Click") + " the name or the icon, then an action button, to put it on the bar.</color>");
        }

        // ------------------------------------------------------------------ Testing (GM), lead decision D13

        private static bool Online(IWowSession s) { return s is SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession; }

        private void SetGmShown(bool on)
        {
            _gmLabel.enabled = on;
            _gmLearn.gameObject.SetActive(on);
            _gmTele.gameObject.SetActive(on);
        }

        private void GmLearn()
        {
            IWowSession s = Ctx.S;
            PlayerState p = Ctx.P;
            if (s == null || p == null || !Online(s) || !WowGame.AutoAccountIsGm) return;
            if (!MountCatalog.Loaded)
            {
                Ctx.Say("The mount list is still being read from the game files - try again in a moment.", UiArt.ErrorRed);   // ours
                return;
            }
            string who = p.Name ?? "";
            if (LearnByName && (who.Length == 0 || who.IndexOf(' ') >= 0))
            {
                Ctx.Say("Testing (GM): the character's name is not known yet - try again in a moment.", UiArt.ErrorRed);   // ours
                return;
            }
            if (!LearnByName && s.TargetGuid != 0) s.SetTarget(0);   // ".learn" teaches the selected player (see LearnByName)
            UiSound.Play(WowUiSound.ButtonClick);
            int riding = 0, mounts = 0, known = 0;
            for (int i = 0; i < RidingSpells.Length; i++)
            {
                bool has = RidingSkillOf[i] < 0 ? p.ColdWeatherFlying : p.RidingSkill >= RidingSkillOf[i];
                if (has) { known++; continue; }
                s.RunGmCommand(LearnCommand(who, RidingSpells[i]));
                riding++;
            }
            List<MountInfo> list = MountCatalog.ForCharacter(p.Faction, p.Class, p.Race);
            for (int i = 0; i < list.Count; i++)
            {
                MountInfo m = list[i];
                if (m == null) continue;
                if (Knows(p, m.SpellId)) { known++; continue; }
                s.RunGmCommand(LearnCommand(who, m.SpellId));
                mounts++;
            }
            _gmBusyUntil = Time.unscaledTime + 5f;
            string line = riding + mounts == 0 ? "Testing (GM): this character already knows every riding skill and mount of its faction and class."
                : "Testing (GM): asked the realm to teach " + riding + " riding spell" + (riding == 1 ? "" : "s") + " and " + mounts + " mount" + (mounts == 1 ? "" : "s") + " (" + known + " already known). The realm answers below.";
            Ctx.Say(line, UiArt.InfoYellow);   // ours
            WowLog.Info("UI: Testing (GM) 'Learn riding + mounts' for " + UiArt.ClassName(p.Class) + " race " + p.Race + " (" + p.Faction + "): " + riding + " riding + " + mounts
                + " mount GM commands handed to the session (" + (LearnByName ? "'.player learn NAME id'" : "'.learn id'") + ", paced by the session), " + known + " skipped as known; "
                + list.Count + " mounts in MountCatalog.ForCharacter.");
        }

        private static string LearnCommand(string who, int spellId)
        {
            return LearnByName ? ".player learn " + who + " " + spellId : ".learn " + spellId;
        }

        private static bool Knows(PlayerState p, int spellId)
        {
            List<AbilityView> m = p.Mounts;
            if (m != null) for (int i = 0; i < m.Count; i++) if (m[i] != null && m[i].SpellId == spellId) return true;
            return false;
        }

        private void GmTele()
        {
            IWowSession s = Ctx.S;
            if (s == null || !Online(s) || !WowGame.AutoAccountIsGm) return;
            UiSound.Play(WowUiSound.ButtonClick);
            s.RunGmCommand(OutlandTele);
            _gmBusyUntil = Time.unscaledTime + 5f;
            Ctx.Say("Testing (GM): teleport to Hellfire Peninsula asked (" + OutlandTele + ").", UiArt.InfoYellow);   // ours
            WowLog.Info("UI: Testing (GM) 'Go to Outland (test flying)' - '" + OutlandTele + "' handed to the session.");
        }

        // ------------------------------------------------------------------ texts

        /// <summary>"Not in this iteration: ..." from MountCatalog's exclusion reasons (PLAN7 s4.1), counted, never typed.</summary>
        private string Exclusions()
        {
            if (_exclusionsDone) return _exclusions;
            if (!MountCatalog.Loaded) return null;
            _exclusionsDone = true;
            int scaled = 0, vehicle = 0, aq = 0, other = 0;
            IReadOnlyList<MountInfo> all = MountCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                string why = all[i] != null ? all[i].Excluded : null;
                if (why == null) continue;
                if (why.StartsWith("riding-scaled", System.StringComparison.Ordinal)) scaled++;
                else if (why.StartsWith("vehicle", System.StringComparison.Ordinal)) vehicle++;
                else if (why.StartsWith("Ahn'Qiraj", System.StringComparison.Ordinal)) aq++;
                else other++;
            }
            if (scaled + vehicle + aq + other == 0) { _exclusions = null; return null; }
            var parts = new List<string>(4);
            if (scaled > 0) parts.Add(scaled + " riding-scaled");
            if (vehicle > 0) parts.Add(vehicle + " vehicle carpet" + (vehicle == 1 ? "" : "s"));
            if (aq > 0) parts.Add(aq + " Qiraji tank" + (aq == 1 ? "" : "s"));
            if (other > 0) parts.Add(other + " other");
            _exclusions = "Not in this iteration: " + string.Join(", ", parts.ToArray()) + ".";
            WowLog.Info("UI: Mounts window footer - " + _exclusions);
            return _exclusions;
        }

        private string FooterText(PlayerState p, bool online, bool gm)
        {
            int n = _sorted.Count;
            if (_critters) return "";   // pets H11: the Companions tab has no footer (ours)
            if (online)
                return "Online: your realm decides which mounts you know (README)." + (n > 0 ? "" : gm ? " None yet - use Testing (GM) below." : " None yet.");
            string first;
            if (!MountCatalog.Loaded) first = "Reading the mounts from the game files...";
            else if (n == 0) first = WowSettings.UnlockAllMounts ? "No mounts for this character." : "No mounts: Settings > Gameplay > Unlock all mounts (testing)";
            else first = WowSettings.UnlockAllMounts ? "Unlock all mounts (testing) is on: every mount of your faction and class." : "";
            string ex = Exclusions();
            if (ex == null) return first;
            return first.Length > 0 ? first + "\n" + ex : ex;
        }

        /// <summary>"Model: server | baked | learned | stand-in (display D)" for the selected mount.</summary>
        private string SourceText(AbilityView v, PlayerState p)
        {
            if (v == null || _critters) return "";
            MountDisplaySource src;
            string disp;
            if (Ridden(v) && p.MountDisplayId > 0)
            {
                src = p.MountDisplaySource;
                disp = "display " + p.MountDisplayId;
            }
            else
            {
                MountInfo mi = MountCatalog.Find(v.SpellId);
                if (mi == null || mi.CreatureEntry <= 0) return MountCatalog.Loaded ? "Model: unknown (not in the game's Mounts list)" : "";
                int[] ids;
                if (MountDisplays.TryGetAll(mi.CreatureEntry, out ids, out src) && ids != null && ids.Length > 0)
                {
                    disp = ids.Length == 1 ? "display " + ids[0] : "displays " + string.Join(", ", System.Array.ConvertAll(ids, i => i.ToString()));
                }
                else
                {
                    src = MountDisplaySource.StandIn;
                    disp = "display " + MountDisplays.StandIn(mi.Kind, p.Faction);
                }
            }
            string label;
            switch (src)
            {
                case MountDisplaySource.Server: label = "server"; break;
                case MountDisplaySource.Learned: label = "learned"; break;
                case MountDisplaySource.Baked: label = "baked"; break;
                case MountDisplaySource.StandIn: label = "stand-in"; break;
                default: label = "unknown"; break;
            }
            return "Model: " + label + " (" + disp + ")";
        }

        // ------------------------------------------------------------------ refresh

        protected override void OnOpen()
        {
            _shownSig = int.MinValue;
            _sourceSig = int.MinValue;
            _footerShown = null;
        }

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            IWowSession s = Ctx.S;
            if (p == null || s == null) return;
            if (!ReferenceEquals(s, _forSession))
            {
                _forSession = s;
                _selected = 0;
                _pageIndex = 0;
                _sig = int.MinValue;
                _shownSig = int.MinValue;
                _gmShownFor = -1;
            }
            if (p.Class != _cls)
            {
                _cls = p.Class;
                Vector4 c = UiArt.ClassCoords(p.Class);
                UiKit.Coords(_portrait, c.x, c.y, c.z, c.w);
            }
            // pets H11: the Companions tab shows only with Companions.Count > 0 (PetPaperDollFrame.lua:199); with it hidden, the Mounts list.
            bool hasCritters = p.Companions != null && p.Companions.Count > 0;
            if (!hasCritters && _critters) SetCritters(false);
            if (!_tabsInit || hasCritters != _tabsShownFor)
            {
                _tabsInit = true;
                _tabsShownFor = hasCritters;
                _tabCompanions.gameObject.SetActive(hasCritters);
                _tabMounts.gameObject.SetActive(hasCritters);
                _tabCompanions.Interactable = !_critters;
                _tabMounts.Interactable = _critters;
            }
            SyncList(p);
            int pages = Mathf.Max(1, (_sorted.Count + PerPage - 1) / PerPage);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, pages - 1);

            // Slots (on a change of the list, the page, the selection or the ridden mount).
            int ridden = _critters ? (p.CompanionGuid != 0 ? p.CompanionSpellId : 0) : p.Mounted ? p.MountSpellId : 0;   // pets H11
            int shown = _sig * 31 + _pageIndex * 7 + _selected * 13 + ridden * 17 + (p.Mounted ? 1 : 0) + (_critters ? 5 : 0);
            for (int k = 0; k < PerPage; k++) { AbilityView v = At(k); if (v != null && v.Active) shown = shown * 3 + k; }
            if (shown != _shownSig)
            {
                _shownSig = shown;
                _page.SetText("Page {0} of {1}", _pageIndex + 1, pages);   // MERCHANT_PAGE_NUMBER "Page %s of %s" (GlobalStrings.lua:4925)
                _prev.Interactable = _pageIndex > 0;
                _next.Interactable = _pageIndex < pages - 1;
                for (int k = 0; k < PerPage; k++)
                {
                    AbilityView v = At(k);
                    bool on = v != null;
                    _slot[k].gameObject.SetActive(on);
                    _empty[k].enabled = !on && _empty[k].texture != null;
                    int id = on ? v.SpellId : 0;
                    if (id != _spellAt[k]) { _spellAt[k] = id; _iconLoaded[k] = false; _slot[k].Icon.enabled = false; }
                    if (!on) continue;
                    _outline[k].enabled = v.SpellId == _selected && _outline[k].texture != null;
                    _ridden[k].enabled = Ridden(v) && _ridden[k].texture != null;
                }
                AbilityView sel = Selected();
                _name.text = sel != null ? sel.Name ?? "" : "";
                _nameBtn.Interactable = sel != null;
                _big.gameObject.SetActive(sel != null);
                string label = _critters ? (sel != null && Ridden(sel) ? UiGlobalStrings.G("PET_DISMISS", "Dismiss") : UiGlobalStrings.G("SUMMON", "Summon"))   // pets H11: PET_DISMISS / SUMMON
                    : sel != null && Ridden(sel) ? "Dismount" : "Mount";   // BINDING_NAME_DISMOUNT (GlobalStrings.lua:802) / MOUNT (:4979)
                if (!ReferenceEquals(label, _summonShown)) { _summonShown = label; _summon.SetLabel(label); }
                _summon.Interactable = sel != null;
            }
            for (int k = 0; k < PerPage; k++)
            {
                if (_spellAt[k] == 0 || _iconLoaded[k]) continue;
                AbilityView v = At(k);
                if (v != null) SetIcon(_slot[k], v.IconPath, ref _iconPath[k], ref _iconLoaded[k]);
            }
            AbilityView selected = Selected();
            if (selected != null && (!_bigLoaded || !ReferenceEquals(selected.IconPath, _bigPath))) SetIcon(_big, selected.IconPath, ref _bigPath, ref _bigLoaded);

            // The model source line (on a change of the selection, the ride or the resolver's cache).
            int srcSig = _selected * 31 + ridden * 7 + p.MountDisplayId * 13 + (int)p.MountDisplaySource * 5 + MountDisplays.Version * 17 + (MountCatalog.Loaded ? 1 : 0);
            if (srcSig != _sourceSig)
            {
                _sourceSig = srcSig;
                string t = SourceText(selected, p);
                if (!string.Equals(t, _sourceShown)) { _sourceShown = t; _source.text = t; }
            }

            // Footer and the Testing (GM) row.
            bool online = Online(s);
            bool gm = online && WowGame.AutoAccountIsGm && !_critters;   // the Testing (GM) row is the Mounts tab's
            int gmNow = gm ? 1 : 0;
            if (gmNow != _gmShownFor)
            {
                _gmShownFor = gmNow;
                SetGmShown(gm);
                UiKit.Size(_footer.rectTransform, 278f, gm ? 22f : 60f);
                if (gm) WowLog.Info("UI: Mounts window - Testing (GM) row shown (online with a GM automatic account; lead decision D13).");
            }
            if (gm)
            {
                bool free = now >= _gmBusyUntil;
                if (_gmLearn.Interactable != free) _gmLearn.Interactable = free;
                if (_gmTele.Interactable != free) _gmTele.Interactable = free;
            }
            string footer = FooterText(p, online, gm);
            if (!string.Equals(footer, _footerShown)) { _footerShown = footer; _footer.text = footer; }
        }
    }
}
