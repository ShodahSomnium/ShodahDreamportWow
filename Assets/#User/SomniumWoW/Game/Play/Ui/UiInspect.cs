// iteration 11 - owner interact (proposal-interact.md 4.4 / 6.3.4): WoW's InspectFrame (Blizzard_InspectUI / InspectPaperDollFrame), a session
// window of uicore's registry (Wanted while IWowInteractions.Inspection != null): the other player's 19 equipment slots at the paper doll's
// places (UiCharacter's), their WoW tooltips with the enchant / gem lines, "Level N Race Class", tabs Character / PvP (greyed - gap G-I1) /
// Talents (a summary - gap G-I2: points per tree, unspent points, glyphs). Desktop: the mouse; VR: the laser / poke on the dialog surface.
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiInspect : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Inspect, Frame = "InspectFrame", TitleKey = "INSPECT", Title = "Inspect",   // GlobalStrings.lua:4241
                Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,                       // UIPanelWindows InspectFrame left 0 (Blizzard_InspectUI.lua:4)
                Wanted = s => s != null && s.Interactions != null && s.Interactions.Inspection != null,
                NearGuid = s => s != null && s.Interactions != null && s.Interactions.Inspection != null ? s.Interactions.Inspection.Guid : 0,
                Closed = s => { if (s != null && s.Interactions != null) { WowLog.Info("UI: InspectFrame closed by the player."); s.Interactions.CloseInspect(); } },
                OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",                          // Blizzard_InspectUI.lua:74 / :81
                Owner = "interact", Hidden = false,
                Build = (parent, ctx, close) => new UiInspect(parent, ctx, close)
            };
        }

        /// <summary>Set by the unit menu: the inspection was opened for the current target, so it follows the target like WoW
        /// (InspectFrame_OnEvent PLAYER_TARGET_CHANGED, Blizzard_InspectUI.lua:33-42).</summary>
        public static bool OpenedForTarget;

        private const string ActiveTab = @"Interface\PaperDollInfoFrame\UI-Character-ActiveTab.blp", InactiveTab = @"Interface\PaperDollInfoFrame\UI-Character-InActiveTab.blp";
        private static readonly EquipSlot[] Left = { EquipSlot.Head, EquipSlot.Neck, EquipSlot.Shoulders, EquipSlot.Back, EquipSlot.Chest, EquipSlot.Shirt, EquipSlot.Tabard, EquipSlot.Wrists };
        private static readonly EquipSlot[] Right = { EquipSlot.Hands, EquipSlot.Waist, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Finger1, EquipSlot.Finger2, EquipSlot.Trinket1, EquipSlot.Trinket2 };
        private static readonly EquipSlot[] Weapons = { EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Ranged };
        private static readonly string[] SlotNames = { "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands", "Finger", "Finger", "Trinket", "Trinket", "Back", "Main Hand", "Off Hand", "Ranged", "Tabard" };

        private readonly UiButton[] _slot = new UiButton[19];
        private readonly string[] _slotPath = new string[19];
        private readonly bool[] _slotLoaded = new bool[19];
        private readonly ItemView[] _slotItem = new ItemView[19];
        private readonly RectTransform _paperDoll, _talents;
        private readonly TextMeshProUGUI _name, _level, _talentText;
        private readonly RawImage _portrait;
        private readonly UiButton[] _tabs = new UiButton[3];
        private readonly RawImage[][] _tabArt = new RawImage[3][];
        private int _tab, _version = -1;
        private ulong _guid, _lastTarget;
        private InspectView _shown;
        private string _raceFor;
        private int _raceId = -1;

        public UiInspect(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            Root = UiKit.Rect(parent, "InspectFrame", 0f, 0f, W, H);
            UiKit.Frame4(Root, "Art", UiArt.CharacterTab + "L1.blp", UiArt.CharacterTab + "R1.blp", UiArt.CharacterTab + "BottomLeft.blp", UiArt.CharacterTab + "BottomRight.blp");   // InspectPaperDollFrame art = CharacterFrame's
            _portrait = Portrait(UiArt.Tex(UiArt.ClassCircles));                                                                    // InspectFramePortrait 60 x 60 at (7, -6)
            _name = UiKit.Text(Root, "Name", "", 13f, UiArt.Gold, TextAlignmentOptions.Center, 78f, 16f, 240f, 17f);               // InspectNameText (GameFontNormal)
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _level = UiKit.Text(Root, "Level", "", 10f, Color.white, TextAlignmentOptions.Center, 70f, 44f, 250f, 14f);
            UiKit.CloseButton(Root, 384f - 44f - 16f, 25f - 16f, close);                                                            // InspectFrameCloseButton CENTER at TOPRIGHT (-44, -25)
            _paperDoll = UiKit.Rect(Root, "Paper Doll", 0f, 0f, 384f, 512f);
            for (int i = 0; i < 8; i++) MakeSlot(Left[i], 21f, 74f + i * 41f);                                                      // InspectPaperDollFrame.xml (UiCharacter's places)
            for (int i = 0; i < 8; i++) MakeSlot(Right[i], 305f, 74f + i * 41f);
            for (int i = 0; i < 3; i++) MakeSlot(Weapons[i], 122f + i * 42f, 385f);
            _talents = UiKit.Rect(Root, "Talents", 0f, 0f, 384f, 512f);
            _talentText = UiKit.Text(_talents, "Summary", "", 12f, Color.white, TextAlignmentOptions.TopLeft, 30f, 84f, 300f, 330f);
            _talents.gameObject.SetActive(false);
            // tabs CHARACTER / PVP / TALENTS (CharacterFrameTabButtonTemplate): tab 1 CENTER at BOTTOMLEFT (60, 61), the next -16 overlapped
            string[] labels = { "Character", "PvP", "Talents" };                                                                    // CHARACTER (GlobalStrings.lua:1372) / PVP (:5753) / TALENTS (:7446)
            for (int t = 0; t < 3; t++) MakeTab(t, labels[t], 60f - 50f + t * (100f - 16f), 512f - 61f - 16f);
            _tabs[1].Interactable = false;
            _tabs[1].Tip = () => "Not available yet";   // ours: the PvP tab (MSG_INSPECT_HONOR_STATS / _ARENA_TEAMS) is gap G-I1
            ShowTab(0);
            Root.gameObject.SetActive(false);
        }

        private void MakeSlot(EquipSlot s, float x, float y)
        {
            int k = (int)s;
            UiButton b = UiKit.Slot(_paperDoll, "Slot " + s, x, y, 37f, false);
            b.Icon.texture = UiArt.Tex(UiArt.SlotPrefix + UiArt.SlotBackground[k] + ".blp");
            b.Icon.color = b.Icon.texture != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            b.Icon.enabled = true;
            b.Tip = () => SlotTip(k);
            _slot[k] = b;
        }

        private void MakeTab(int t, string label, float x, float y)
        {
            UiButton b = UiKit.Button(Root, "Tab " + (t + 1), x, y, 100f, 32f);
            var art = new RawImage[3];
            art[0] = UiKit.Pic(b.transform, "Left", UiArt.Tex(InactiveTab), 0f, 0.15625f, 0f, 1f, 0f, 0f, 20f, 32f);
            art[1] = UiKit.Pic(b.transform, "Middle", UiArt.Tex(InactiveTab), 0.15625f, 0.84375f, 0f, 1f, 20f, 0f, 60f, 32f);
            art[2] = UiKit.Pic(b.transform, "Right", UiArt.Tex(InactiveTab), 0.84375f, 1f, 0f, 1f, 80f, 0f, 20f, 32f);
            _tabArt[t] = art;
            b.Label = UiKit.Text(b.transform, "Text", label, 10f, UiArt.Gold, TextAlignmentOptions.Center, 10f, 2f, 80f, 26f);
            b.LabelColor = UiArt.Gold;
            int k = t;
            b.Click = () => { WowLog.Info("UI: InspectFrame tab " + (k + 1) + "."); ShowTab(k); };
            _tabs[t] = b;
        }

        private void ShowTab(int t)
        {
            _tab = t;
            _paperDoll.gameObject.SetActive(t == 0);
            _talents.gameObject.SetActive(t == 2);
            for (int i = 0; i < 3; i++)
            {
                bool active = i == t;
                Texture2D tex = UiArt.Tex(active ? ActiveTab : InactiveTab);
                for (int j = 0; j < 3; j++)
                {
                    RawImage img = _tabArt[i][j];
                    img.texture = tex;
                    Vector2 ap = img.rectTransform.anchoredPosition;
                    img.rectTransform.anchoredPosition = new Vector2(ap.x, active ? 5f : 0f);   // the active tab stands 5 higher (PanelTemplates_SelectTab)
                }
            }
            _version = -1;
        }

        private string SlotTip(int k)
        {
            ItemView it = _slotItem[k];
            if (it == null) return "<size=14>" + SlotNames[k] + "</size>";
            InspectView v = Inspection;
            string ench = v != null ? v.Enchants[k] : null;
            return UiTooltip.Item(it, ench != null ? "<color=#1eff00>" + ench + "</color>" : null);
        }

        private InspectView Inspection
        {
            get { return Ctx != null && Ctx.S != null && Ctx.S.Interactions != null ? Ctx.S.Interactions.Inspection : null; }
        }

        protected override void OnOpen()
        {
            _version = -1;
            _shown = null;
            ShowTab(0);
            InspectView v = Inspection;
            _lastTarget = Ctx != null && Ctx.S != null ? Ctx.S.TargetGuid : 0;
            WowLog.Info("UI: InspectFrame opened (" + (v != null ? v.Name ?? "?" : "?") + (Ctx != null && Ctx.Vr ? ", VR" : ", desktop") + ").");
        }

        public override void Refresh(float now)
        {
            IWowSession s = Ctx != null ? Ctx.S : null;
            InspectView v = Inspection;
            if (s == null || v == null) return;
            FollowTarget(s, v);
            if (!ReferenceEquals(v, _shown)) { _shown = v; _version = -1; for (int k = 0; k < 19; k++) _slotItem[k] = null; }
            for (int k = 0; k < 19; k++)
            {
                ItemView it = v.Equipment[k];
                if (!ReferenceEquals(it, _slotItem[k]))
                {
                    _slotItem[k] = it;
                    _slotLoaded[k] = false;
                    _slotPath[k] = null;
                    UiButton b = _slot[k];
                    b.Icon.texture = UiArt.Tex(UiArt.SlotPrefix + UiArt.SlotBackground[k] + ".blp");
                    b.Icon.color = Color.white;
                    b.Icon.enabled = b.Icon.texture != null;
                    UiKit.Coords(b.Icon, 0f, 1f, 0f, 1f);
                }
                if (it != null && !_slotLoaded[k] && !string.IsNullOrEmpty(it.IconPath)) SetIcon(_slot[k], it.IconPath, ref _slotPath[k], ref _slotLoaded[k]);
            }
            if (v.Version == _version && v.Guid == _guid) return;
            _version = v.Version;
            _guid = v.Guid;
            Vector4 c = UiArt.ClassCoords(v.Class);
            UiKit.Coords(_portrait, c.x, c.y, c.z, c.w);
            _name.text = v.Name ?? "";
            string race = RaceName(v.Race);
            _level.text = "Level " + v.Level + (race.Length > 0 ? " " + race : "") + " " + UiArt.ClassName(v.Class);   // PaperDollFrame "Level %d %s %s"
            _talentText.text = TalentSummary(v);
        }

        /// <summary>Ours (gap G-I2, WoW draws the read-only talent grid): the trees with their points, the unspent points, the glyphs.</summary>
        private static string TalentSummary(InspectView v)
        {
            if (!v.Loaded) return "<color=#808080>Retrieving information...</color>";   // ours
            if (v.TalentGroups == 0) return "<color=#808080>Talents hidden by the realm</color>";   // ours (worldserver.conf TalentsInspecting off)
            Sb.Length = 0;
            Sb.Append("<color=#ffd100>Talent Points</color>\n");                       // TALENT_POINTS (GlobalStrings.lua:7451)
            for (int i = 0; i < v.Trees.Count; i++) Sb.Append(v.Trees[i].Name).Append(": <color=#ffffff>").Append(v.Trees[i].Points).Append("</color>\n");
            if (v.UnspentPoints > 0) Sb.Append("\nUnspent Talents: ").Append(v.UnspentPoints).Append('\n');   // UNSPENT_TALENT_POINTS (:7910)
            if (v.Glyphs.Count > 0)
            {
                Sb.Append("\n<color=#ffd100>Glyphs</color>\n");                         // GLYPHS (:3797)
                for (int i = 0; i < v.Glyphs.Count; i++) Sb.Append(v.Glyphs[i]).Append('\n');
            }
            return Sb.ToString();
        }

        /// <summary>InspectFrame_OnEvent PLAYER_TARGET_CHANGED (Blizzard_InspectUI.lua:33-42): opened for the target and the target changes -> an
        /// inspectable player within 28 yd is inspected instead, anything else closes the frame.</summary>
        private void FollowTarget(IWowSession s, InspectView v)
        {
            ulong t = s.TargetGuid;
            if (t == _lastTarget) return;
            _lastTarget = t;
            if (!OpenedForTarget || t == v.Guid) return;
            EntityState e = t != 0 ? s.Find(t) : null;
            PlayerState p = s.Player;
            bool ok = e != null && e.Kind == EntityKind.Player && t != p.Guid && e.Reaction != Reaction.Hostile && Yards(p.Pos, e.Pos) <= 28f;   // INSPECT_DISTANCE (ObjectDefines.h:27)
            WowLog.Info("UI: InspectFrame - the target changed: " + (ok ? "inspecting " + e.Name : "closing") + ".");
            if (ok) s.Interactions.InspectPlayer(t);
            else s.Interactions.CloseInspect();
        }

        private static float Yards(WowPos a, WowPos b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private string RaceName(int race)
        {
            if (race == _raceId && _raceFor != null) return _raceFor;
            _raceId = race;
            string n = null;
            try { n = UiArt.Files != null ? SomniumSpace.Worlds.SomniumWoW.Play.Rules.WowRaces.Name(UiArt.Files, race) : null; }
            catch (Exception e) { WowLog.Warn("UI: race name for " + race + " failed: " + e.Message); }
            _raceFor = string.IsNullOrEmpty(n) ? "" : n;
            return _raceFor;
        }
    }
}
