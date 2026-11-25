// iteration 11 - owner character (proposal-talents.md 4.2 / 6.1): WoW's ReputationFrame (CharacterFrame's Reputation tab, key U).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// ReputationFrame (ReputationFrame.xml / .lua, dumps in P/harness/out_d11talents): the CharacterFrame art (UI-Character-General-*), the
    /// "Faction" / "Standing" column labels, 15 rows 23 apart from y 83 (NUM_FACTIONS_DISPLAYED, REPUTATIONFRAME_ROWSPACING) over the
    /// session's PlayerState.Reputations, headers that collapse with their +/- button, and ReputationDetailFrame (212x203 beside the panel on
    /// the desktop; inside it in VR - ours, the arm lid is exactly the panel): name, description, At War / Move to Inactive / Show as
    /// Experience Bar. Header collapse is a session-only set (ours: WoW keeps it in the client). The tab strip is UiCharacterTabs.
    /// </summary>
    public sealed class UiReputationWindow : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Reputation, Frame = "ReputationFrame", TitleKey = "REPUTATION", Title = "Reputation",   // GlobalStrings.lua:6070
                Area = UiPanelArea.Left, Pushable = 3, WhileDead = true, Key = KeyCode.U, Binding = "TOGGLECHARACTER2", Palm = false,
                OpenSound = "igCharacterInfoOpen", CloseSound = "igCharacterInfoClose",
                Owner = "character", Hidden = false,   // built (iteration 11, character)
                Build = (parent, ctx, close) => new UiReputationWindow(parent, ctx, close)
            };
        }

        const int Rows = 15;                                   // NUM_FACTIONS_DISPLAYED (ReputationFrame.lua:1)
        const float FirstRowY = 83f, RowStep = 23f;            // ReputationBar1 TOPRIGHT (-68, -83); REPUTATIONFRAME_ROWSPACING 23
        const float DetailW = 212f, DetailH = 203f;            // ReputationDetailFrame (ReputationFrame.xml:544-556)

        readonly UiCharacterShell _shell;
        readonly UiRepRow[] _rows = new UiRepRow[Rows];
        readonly bool[] _rowHover = new bool[Rows];
        readonly RectTransform _list, _detail;
        readonly UiButton _up, _down;
        readonly TextMeshProUGUI _empty, _detailName, _detailDesc;
        readonly UiRepCheck _atWar, _inactive, _mainScreen;
        readonly List<FactionView> _visible = new List<FactionView>();
        readonly HashSet<int> _collapsed = new HashSet<int>();
        int _top, _shownCounter = -1, _shownTop = -1, _selected = -1;
        bool _dirty = true;
        readonly float _detailX, _detailY;

        public UiReputationWindow(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            W = 384f; H = 512f;
            Root = UiKit.Rect(parent, "ReputationFrame", 0f, 0f, W, H);
            _shell = new UiCharacterShell(Root, @"Interface\PaperDollInfoFrame\UI-Character-General-TopLeft.blp", @"Interface\PaperDollInfoFrame\UI-Character-General-TopRight.blp",
                                          @"Interface\PaperDollInfoFrame\UI-Character-General-BottomLeft.blp", @"Interface\PaperDollInfoFrame\UI-Character-General-BottomRight.blp", close);
            // ReputationFrameFactionLabel (70, -57) / ReputationFrameStandingLabel (215, -59), GameFontHighlight; FACTION / STANDING (GlobalStrings.lua:3639 / 7341)
            UiKit.Text(Root, "FactionLabel", UiGlobalStrings.G("FACTION", "Faction"), 12f, Color.white, TextAlignmentOptions.TopLeft, 70f, 57f, 120f, 16f).textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Text(Root, "StandingLabel", UiGlobalStrings.G("STANDING", "Standing"), 12f, Color.white, TextAlignmentOptions.TopLeft, 215f, 59f, 100f, 16f).textWrappingMode = TextWrappingModes.NoWrap;
            _list = UiKit.Rect(Root, "ReputationListScrollFrame", 22f, 76f, 296f, 354f);   // 296x354 at TOPRIGHT (-66, -76)
            for (int i = 0; i < Rows; i++)
            {
                var row = new UiRepRow(Root, i, FirstRowY + i * RowStep);
                int k = i;
                row.B.Click = () => RowClick(k);
                row.Toggle.Click = () => ToggleHeader(k);
                row.B.Tip = () => RowTip(k);
                row.Hide();
                _rows[i] = row;
            }
            _up = UiKit.ArtButton(Root, "ScrollUp", UiArt.ScrollUpUp, null, UiArt.ScrollUpDisabled, UiArt.ScrollHighlight, 322f, 78f, 20f, () => Scroll(-3));
            _down = UiKit.ArtButton(Root, "ScrollDown", UiArt.ScrollDownUp, null, UiArt.ScrollDownDisabled, UiArt.ScrollHighlight, 322f, 408f, 20f, () => Scroll(3));
            _empty = UiKit.Text(Root, "Empty", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 40f, 200f, 270f, 60f);
            // ReputationDetailFrame: TOPLEFT on the frame's TOPRIGHT (-33, -28) on the desktop; VR: inside, bottom right (ours)
            _detailX = ctx != null && ctx.Vr ? 384f - DetailW - 14f : 384f - 33f;
            _detailY = ctx != null && ctx.Vr ? 512f - DetailH - 52f : 28f;
            _detail = UiKit.Rect(Root, "ReputationDetailFrame", _detailX, _detailY, DetailW, DetailH);
            UiKit.Button(_detail, "Blocker", 0f, 0f, DetailW, DetailH);   // the frame takes the pointer (enableMouse="true")
            UiBackdrop back = UiBackdrop.Make(_detail, "Backdrop", UiArt.DialogBackground, UiArt.DialogBorder, 32f, 11f, 32f, Color.white);
            back.Place(0f, 0f, DetailW, DetailH);
            // the 256x128 detail background at (11, -11) and the 256-wide divider at (9, -131), cut to the 212-wide frame (ours: WoW clips them)
            UiKit.Pic(_detail, "Background", UiArt.Tex(@"Interface\PaperDollInfoFrame\UI-Character-Reputation-DetailBackground.blp"), 0f, 190f / 256f, 0f, 1f, 11f, 11f, 190f, 128f);
            UiKit.Pic(_detail, "Corner", UiArt.Tex(@"Interface\DialogFrame\UI-DialogBox-Corner.blp"), DetailW - 6f - 32f, 7f, 32f, 32f);
            UiKit.Pic(_detail, "Divider", UiArt.Tex(@"Interface\DialogFrame\UI-DialogBox-Divider.blp"), 0f, (DetailW - 18f) / 256f, 0f, 1f, 9f, 131f, DetailW - 18f, 32f);
            _detailName = UiKit.Text(_detail, "FactionName", "", 12f, UiArt.Gold, TextAlignmentOptions.TopLeft, 20f, 21f, 160f, 16f);
            _detailName.textWrappingMode = TextWrappingModes.NoWrap;
            _detailDesc = UiKit.Text(_detail, "FactionDescription", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 20f, 39f, 170f, 92f);
            _detailDesc.overflowMode = TextOverflowModes.Ellipsis;
            UiKit.CloseButton(_detail, DetailW - 3f - 32f, 3f, () => { _selected = -1; _dirty = true; });
            // AT_WAR (GlobalStrings.lua:598) BOTTOMLEFT (14, 34), the sword check 32x32 at (3, -5); MOVE_TO_INACTIVE (:4988) right of its text;
            // SHOW_FACTION_ON_MAINSCREEN (:6271) under the first box (3 units into it)
            _atWar = new UiRepCheck(_detail, "AtWarCheckBox", UiGlobalStrings.G("AT_WAR", "At War"), @"Interface\Buttons\UI-CheckBox-SwordCheck.blp", 32f, 3f, -5f, 14f, DetailH - 34f - 26f, UiArt.ErrorRed, ClickAtWar);
            _inactive = new UiRepCheck(_detail, "InactiveCheckBox", UiGlobalStrings.G("MOVE_TO_INACTIVE", "Move to Inactive"), @"Interface\Buttons\UI-CheckBox-Check.blp", 26f, 0f, 0f, 14f + _atWar.LabelRight + 3f, DetailH - 34f - 26f, Color.white, ClickInactive);
            _mainScreen = new UiRepCheck(_detail, "MainScreenCheckBox", UiGlobalStrings.G("SHOW_FACTION_ON_MAINSCREEN", "Show as Experience Bar"), @"Interface\Buttons\UI-CheckBox-Check.blp", 26f, 0f, 0f, 14f, DetailH - 34f - 3f, Color.white, ClickMainScreen);
            _atWar.B.Tip = () => "<size=14>" + UiGlobalStrings.G("AT_WAR", "At War") + "</size>\n" + UiGlobalStrings.G("REPUTATION_AT_WAR_DESCRIPTION", "While at war with a faction, you may attack its members.");   // ours when the key is missing
            _detail.gameObject.SetActive(false);
            UiCharacterTabs.Build(Root, ctx, UiWindowKind.Reputation);
            Root.gameObject.SetActive(false);
        }

        static int HeaderKey(FactionView v)
        {
            if (v.FactionId != 0) return v.FactionId;
            return v.Name == "Inactive" ? -2 : -1;   // our two headers without a Faction.dbc row
        }

        void BuildVisible()
        {
            _visible.Clear();
            PlayerState p = Ctx.P;
            List<FactionView> all = p != null ? p.Reputations : null;
            if (all == null) return;
            bool topCollapsed = false, subCollapsed = false;
            for (int i = 0; i < all.Count; i++)
            {
                FactionView v = all[i];
                if (v == null) continue;
                if (v.IsHeader && !v.IsChild)
                {
                    _visible.Add(v);
                    topCollapsed = _collapsed.Contains(HeaderKey(v));
                    subCollapsed = false;
                }
                else if (v.IsHeader)
                {
                    if (!topCollapsed) _visible.Add(v);
                    subCollapsed = _collapsed.Contains(HeaderKey(v));
                }
                else if (v.IsChild) { if (!topCollapsed && !subCollapsed) _visible.Add(v); }
                else if (!topCollapsed) _visible.Add(v);
            }
        }

        FactionView Selected()
        {
            if (_selected < 0) return null;
            PlayerState p = Ctx.P;
            if (p == null || p.Reputations == null) return null;
            for (int i = 0; i < p.Reputations.Count; i++) if (p.Reputations[i].RepListId == _selected) return p.Reputations[i];
            return null;
        }

        void RowClick(int k)
        {
            FactionView v = _rows[k].View;
            if (v == null) return;
            bool hasRep = (v.HasRep || !v.IsHeader) && v.RepListId >= 0;
            if (!hasRep)
            {
                if (v.IsHeader) ToggleHeader(k);   // ours: a header without its own standing also collapses on a row click (VR: bigger target)
                return;
            }
            // ReputationBar_OnClick: the selected row closes the detail, any other faction with a standing opens it
            _selected = _selected == v.RepListId && _detail.gameObject.activeSelf ? -1 : v.RepListId;
            UiSound.Named(_selected >= 0 ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            _dirty = true;
        }

        void ToggleHeader(int k)
        {
            FactionView v = _rows[k].View;
            if (v == null || !v.IsHeader) return;
            int key = HeaderKey(v);
            if (!_collapsed.Remove(key)) _collapsed.Add(key);
            _dirty = true;
        }

        string RowTip(int k)
        {
            FactionView v = _rows[k].View;
            if (v == null || v.RepListId < 0 || !(v.HasRep || !v.IsHeader)) return null;
            return "<size=14>" + v.Name + "</size>\n" + UiCharacterText.Standing(v.Rank) + "  " + Mathf.Clamp(v.Standing - v.RankMin, 0, v.RankMax - v.RankMin) + " / " + (v.RankMax - v.RankMin)
                   + (v.AtWar ? "\n<color=#ff2020>" + UiGlobalStrings.G("AT_WAR", "At War") + "</color>" : "");
        }

        void ClickAtWar()
        {
            FactionView v = Selected();
            if (v == null || Ctx.S == null) return;
            UiSound.Named(!v.AtWar ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            Ctx.S.SetFactionAtWar(v.RepListId, !v.AtWar);
        }

        void ClickInactive()
        {
            FactionView v = Selected();
            if (v == null || Ctx.S == null) return;
            UiSound.Named(!v.Inactive ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            Ctx.S.SetFactionInactive(v.RepListId, !v.Inactive);
        }

        void ClickMainScreen()
        {
            FactionView v = Selected();
            if (v == null || Ctx.S == null) return;
            UiSound.Named(!v.Watched ? "igMainMenuOptionCheckBoxOn" : "igMainMenuOptionCheckBoxOff");
            Ctx.S.SetWatchedFaction(v.Watched ? -1 : v.RepListId);
        }

        void Scroll(int by)
        {
            int max = Mathf.Max(0, _visible.Count - Rows);
            int t = Mathf.Clamp(_top + by, 0, max);
            if (t == _top) return;
            _top = t;
            _dirty = true;
        }

        public override void Refresh(float now)
        {
            PlayerState p = Ctx.P;
            if (p == null) return;
            _shell.Refresh(p);
            float wheel = UiCharacterText.Wheel(_list, Ctx.Vr);
            if (wheel > 0.01f) Scroll(-1); else if (wheel < -0.01f) Scroll(1);
            if (p.ReputationCounter != _shownCounter) { _shownCounter = p.ReputationCounter; _dirty = true; }
            for (int i = 0; i < Rows; i++)
                if (_rows[i].View != null && _rows[i].B.Hovered != _rowHover[i]) { _rowHover[i] = _rows[i].B.Hovered; RedrawRow(i); }
            if (!_dirty && _shownTop == _top) return;
            _dirty = false;
            BuildVisible();
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _visible.Count - Rows));
            _shownTop = _top;
            FactionView sel = Selected();
            if (sel == null) _selected = -1;
            bool detail = sel != null;
            if (_detail.gameObject.activeSelf != detail) _detail.gameObject.SetActive(detail);
            for (int i = 0; i < Rows; i++)
            {
                int idx = _top + i;
                bool hiddenByDetail = detail && Ctx.Vr && FirstRowY + i * RowStep + UiRepRow.Height > _detailY;   // VR: no row under the detail frame
                if (idx < _visible.Count && !hiddenByDetail) RedrawRow(i);
                else _rows[i].Hide();
            }
            bool scrolls = _visible.Count > Rows;
            _up.gameObject.SetActive(scrolls); _down.gameObject.SetActive(scrolls);
            _up.Interactable = _top > 0; _down.Interactable = _top < _visible.Count - Rows;
            bool online = Ctx.S is OnlineSession;
            _empty.text = !online ? UiCharacterText.OnlineOnly : "";
            if (detail)
            {
                _detailName.text = sel.Name ?? "";
                _detailDesc.text = sel.Description ?? "";
                // ReputationFrame_Update (.lua:225-258): At War enabled for canToggleAtWar non-headers (text red, else grey); Inactive enabled for non-headers
                bool canWar = sel.CanToggleAtWar && !sel.IsHeader;
                _atWar.Set(sel.AtWar, canWar, canWar ? UiArt.ErrorRed : UiArt.Grey);
                _inactive.Set(sel.Inactive, !sel.IsHeader, !sel.IsHeader ? Color.white : UiArt.Grey);
                _mainScreen.Set(sel.Watched, true, Color.white);
            }
        }

        void RedrawRow(int i)
        {
            int idx = _top + i;
            if (idx >= _visible.Count) { _rows[i].Hide(); return; }
            FactionView v = _visible[idx];
            bool collapsed = v.IsHeader && _collapsed.Contains(HeaderKey(v));
            bool selected = _detail.gameObject.activeSelf && v.RepListId >= 0 && v.RepListId == _selected;
            _rows[i].Show(v, collapsed, selected, _rows[i].B.Hovered);
        }

        protected override void OnOpen()
        {
            _dirty = true;
        }
    }
}
