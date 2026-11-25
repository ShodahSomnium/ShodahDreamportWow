// iteration 11 - owner talents (wave-0 stub by w0game; filled by the talents builder - proposal-talents.md 1, 4.2, 5.4, 6.1)
// WoW's PlayerTalentFrame (Blizzard_TalentUI.xml / .lua of the player's install, dumps in P/harness/out_d11talents/): 384x512, art
// UI-Character-General-TopLeft/-TopRight/-BottomLeft/-BottomRight at (2,-1) of their corners, portrait 60x60 at (7,-6), title TOP (0,-18),
// close button CENTER on TOPRIGHT (-44,-25); the tree (UiTalentTree) or the Glyphs tab (UiGlyphFrame); spec tabs PlayerSpecTab1/2/3 32x32 at
// TOPRIGHT (-32,-65), 22 / 39 below each other (SpellBook-SkillLineTab, UI-TalentFrame-DualTalentSpec overlay); Activate button TOP (10,-45)
// or the status frame (75,-46) 264x20; points bar LEFT 16 / RIGHT -36 / BOTTOM 81 (UI-Button-Borders2), the preview bar in its place with
// Learn / Reset 80x22 (the points bar 4 units over it); tabs PlayerTalentFrameTab1..3 + Tab4 "Glyphs" (CharacterFrameTabButtonTemplate) from
// BOTTOMLEFT (15,46), each next -15 right of the previous. Clicks (Blizzard_TalentUI.lua:436-470): left = learn (or +1 preview point), right =
// -1 preview point; Learn -> CONFIRM_LEARN_PREVIEW_TALENTS. A trainer's MSG_TALENT_WIPE_CONFIRM -> CONFIRM_TALENT_WIPE (StaticPopup.lua:2498-2518)
// on the active spec (UIParent.lua:880-891). A glyph used from the bags opens the Glyphs tab (USE_GLYPH -> OpenGlyphFrame, UIParent.lua:1053-1055).
// VR: "Preview Talent Changes" is always on (a mis-tap never spends a point), "+" / "-" beside the selected talent, a tap on a filled glyph
// socket offers to remove it (VR has no Shift + right click) - ours.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Offline;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiTalentsWindow : UiWindow
    {
        public static UiWindowSpec Spec()
        {
            WowSettings.SetLive("preview_talents");   // the "Preview Talent Changes" row works with this window (main thread: the registry's first use)
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Talents, Frame = "PlayerTalentFrame", TitleKey = "TALENTS_BUTTON", Title = "Talents",   // GlobalStrings.lua:7447
                Area = UiPanelArea.Left, Pushable = 6, WhileDead = true, Key = KeyCode.N, KeyShift = true, Binding = "TOGGLETALENTS",   // PLAN11 s2: Shift+N
                Micro = "Talents", MicroOrder = 2, MinLevel = 10, OpenSound = "TalentScreenOpen", CloseSound = "TalentScreenClose",   // Blizzard_TalentUI.lua:291 / :310
                Owner = "talents", Hidden = false,
                OpenRequest = s => s != null && s.Player != null ? s.Player.Talents.OpenRequest : 0,   // a glyph waits / a trainer's wipe question (talents U2)
                Build = (parent, ctx, close) => new UiTalentsWindow(parent, ctx, close)
            };
        }

        const int GlyphTab = 3;                                    // GLYPH_TALENT_TAB 4 (Blizzard_TalentUI.lua:20), 0-based
        const int PetSpec = 2;
        const int InscriptionLevel = 15;                           // SHOW_INSCRIPTION_LEVEL (Constants.lua:147)
        const int ActivatePrimary = 63645, ActivateSecondary = 63644;   // TALENT_ACTIVATION_SPELLS (the Activate button is disabled while casting)
        const float PointsY = 405f, PointsYPreview = 383f;         // the points bar at BOTTOM 81, or 4 units into the preview bar's top
        const string GeneralArt = @"Interface\PaperDollInfoFrame\UI-Character-General-";
        const string SkillLineTab = @"Interface\SpellBook\SpellBook-SkillLineTab.blp", DualSpecArt = @"Interface\TalentFrame\UI-TalentFrame-DualTalentSpec.blp";
        const string InputBorder = @"Interface\Common\Common-Input-Border.blp", Borders2 = @"Interface\Buttons\UI-Button-Borders2.blp";
        static readonly float[] SpecTabY = { 65f, 119f, 190f };

        sealed class SpecTab
        {
            public UiButton B;
            public RawImage Bg, Icon, Checked, Overlay;
            public string IconShown = "\0";   // nothing shown yet (null = the class circle)
            public bool Grey;
        }

        readonly Action _close;
        readonly RawImage _portrait;
        readonly TextMeshProUGUI _title;
        readonly UiTalentTree _tree;
        readonly UiGlyphFrame _glyphs;
        readonly RectTransform _pointsBar, _previewBar, _statusFrame;
        readonly TextMeshProUGUI _spentText, _unspentText;
        readonly UiButton _learn, _reset, _activate, _unlearnOffline, _closeButton;
        readonly UiTalentTab[] _tabs = new UiTalentTab[4];
        readonly SpecTab[] _specTabs = new SpecTab[3];
        readonly UiTalentModel[] _models = { new UiTalentModel(), new UiTalentModel(), new UiTalentModel { Pet = true } };
        readonly List<TalentTabInfo> _trees = new List<TalentTabInfo>(3);
        // the in-window confirm (ours) for when uicore's StaticPopup shows nothing
        readonly RectTransform _confirm;
        readonly TextMeshProUGUI _confirmText;
        readonly UiButton _confirmYes, _confirmNo;
        Action _confirmAccept, _confirmCancel;
        Func<bool> _confirmKeep;
        static bool _fallbackLogged;

        TalentDb _db;
        SpellDb _spells;
        int _spec, _tab, _sig = int.MinValue, _openReq = int.MinValue, _previewVersion, _shownTreeId = -1;
        bool _opened, _wipeShown, _wipeFallback;
        int _wipeHandle;
        float _lastNow = -1f;
        WowClass _portraitClass = (WowClass)255;

        public UiTalentsWindow(Transform parent, UiContext ctx, Action close)
        {
            Ctx = ctx;
            _close = close;
            Root = UiKit.Rect(parent, "PlayerTalentFrame", 0f, 0f, W, H);
            UiKit.Pic(Root, "PlayerTalentFrameTopLeft", UiArt.Tex(GeneralArt + "TopLeft.blp"), 2f, 1f, 256f, 256f);
            UiKit.Pic(Root, "PlayerTalentFrameTopRight", UiArt.Tex(GeneralArt + "TopRight.blp"), 258f, 1f, 128f, 256f);
            UiKit.Pic(Root, "PlayerTalentFrameBottomLeft", UiArt.Tex(GeneralArt + "BottomLeft.blp"), 2f, 257f, 256f, 256f);
            UiKit.Pic(Root, "PlayerTalentFrameBottomRight", UiArt.Tex(GeneralArt + "BottomRight.blp"), 258f, 257f, 128f, 256f);
            _portrait = Portrait(UiArt.Tex(UiArt.ClassCircles));
            _title = Title(UiTalentText.G("TALENTS", "Talents"), 18f);   // GS 7446

            // the status frame (dual spec, the active spec shown) / the Activate button (an inactive spec shown)
            _statusFrame = UiKit.Rect(Root, "PlayerTalentFrameStatusFrame", 75f, 46f, 264f, 20f);
            Texture2D ib = UiArt.Tex(InputBorder);
            UiKit.Pic(_statusFrame, "Left", ib, 0f, 0.0625f, 0f, 0.625f, 0f, 0f, 8f, 20f);
            UiKit.Pic(_statusFrame, "Middle", ib, 0.0625f, 0.9375f, 0f, 0.625f, 8f, 0f, 248f, 20f);
            UiKit.Pic(_statusFrame, "Right", ib, 0.9375f, 1f, 0f, 0.625f, 256f, 0f, 8f, 20f);
            var status = UiKit.Text(_statusFrame, "PlayerTalentFrameStatusText", UiTalentText.G("TALENT_ACTIVE_SPEC_STATUS", "These are your active talents"), 12f, UiArt.Gold, TextAlignmentOptions.Center, 0f, 0f, 264f, 20f);   // GS 7450
            status.textWrappingMode = TextWrappingModes.NoWrap;
            _statusFrame.gameObject.SetActive(false);
            string act = UiTalentText.G("TALENT_SPEC_ACTIVATE", "Activate These Talents");   // GS 7455
            _activate = UiKit.PanelButton(Root, act, 132f, 45f, 140f, 22f, Activate);
            _activate.Tip = null;
            float aw = Mathf.Max(80f, Mathf.Ceil(_activate.Label.GetPreferredValues(act).x) + 40f);   // PlayerTalentFrameActivateButton_OnLoad: text + 40
            ResizePanelButton(_activate, aw, 22f);
            UiKit.Move(_activate.Rt, 202f - aw * 0.5f, 45f);
            _activate.gameObject.SetActive(false);
            _unlearnOffline = UiKit.PanelButton(Root, "Unlearn Talents", 142f, 45f, 120f, 22f, UnlearnOffline);   // ours (offline has no trainer)
            _unlearnOffline.Tip = () => "<size=14>Unlearn Talents</size>\nOffline: unlearn every talent at no cost (ours - online a class trainer does it).";
            _unlearnOffline.gameObject.SetActive(false);

            _tree = new UiTalentTree(Root, ctx);
            _tree.Clicked = TalentClicked;
            _tree.Tip = t => UiTalentTooltip.Build(_models[_spec], t, Ctx.Vr);

            // the points bar (Blizzard_TalentUI.xml:249-327)
            _pointsBar = UiKit.Rect(Root, "PlayerTalentFramePointsBar", 16f, PointsY, 332f, 26f);
            Texture2D b2 = UiArt.Tex(Borders2);
            UiKit.Pic(_pointsBar, "Background", b2, 0f, 0.646484375f, 0.2109375f, 0.4140625f, 0f, 0f, 332f, 26f);
            UiKit.Pic(_pointsBar, "BorderLeft", b2, 0f, 0.01171875f, 0.421875f, 0.5625f, 4f, 5f, 6f, 16f);
            UiKit.Pic(_pointsBar, "BorderMiddle", b2, 0.01171875f, 0.3046875f, 0.421875f, 0.5625f, 10f, 5f, 312f, 16f);
            UiKit.Pic(_pointsBar, "BorderRight", b2, 0.3046875f, 0.31640625f, 0.421875f, 0.5625f, 322f, 5f, 6f, 16f);
            _spentText = UiKit.Text(_pointsBar, "PlayerTalentFrameSpentPointsText", "", 10f, UiArt.Gold, TextAlignmentOptions.Left, 12f, 5f, 200f, 15f);
            _unspentText = UiKit.Text(_pointsBar, "PlayerTalentFrameTalentPointsText", "", 10f, UiArt.Gold, TextAlignmentOptions.Right, 120f, 5f, 200f, 15f);
            _spentText.textWrappingMode = _unspentText.textWrappingMode = TextWrappingModes.NoWrap;

            // the preview bar (:328-415): Reset at RIGHT -4, Learn left of it
            _previewBar = UiKit.Rect(Root, "PlayerTalentFramePreviewBar", 20f, PointsY, 328f, 26f);
            UiKit.Pic(_previewBar, "Filler", b2, 0.484375f, 0.78515625f, 0f, 0.203125f, 0f, 0f, 160f, 26f);
            UiKit.Pic(_previewBar, "ButtonBorder", b2, 0f, 0.646484375f, 0.2109375f, 0.4140625f, 160f, 0f, 168f, 26f);
            _learn = UiKit.PanelButton(_previewBar, UiTalentText.G("LEARN", "Learn"), 164f, 2f, 80f, 22f, Learn);   // GS 4694
            _learn.Tip = () => UiTalentText.G("TALENT_TOOLTIP_LEARNTALENTGROUP", "Click to finalize your preview talent points.");   // GS 7462
            _reset = UiKit.PanelButton(_previewBar, UiTalentText.G("RESET", "Reset"), 244f, 2f, 80f, 22f, ResetPreview);   // GS 6086
            _reset.Tip = () => UiTalentText.G("TALENT_TOOLTIP_RESETTALENTGROUP", "Click to reset your preview talent points.");   // GS 7464
            _previewBar.gameObject.SetActive(false);

            _glyphs = new UiGlyphFrame(Root, ctx);
            _glyphs.Clicked = GlyphClicked;
            _glyphs.Tip = i => { var p = Ctx.P; return p == null ? null : UiGlyphFrame.Tooltip(p.Talents, _db, _spells, _spec, _spec == p.Talents.ActiveSpec, i, Ctx.Vr, p.Level); };

            // spec tabs (Blizzard_TalentUI.xml:700-790)
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var st = new SpecTab();
                st.Bg = UiKit.Pic(Root, "PlayerSpecTab" + (i + 1) + "Background", UiArt.Tex(SkillLineTab), 349f, SpecTabY[i] - 11f, 64f, 64f);
                st.B = UiKit.Button(Root, "PlayerSpecTab" + (i + 1), 352f, SpecTabY[i], 32f, 32f);
                st.Icon = UiKit.Pic(st.B.transform, "Normal", null, 0f, 0f, 32f, 32f);
                st.Overlay = UiKit.Pic(st.B.transform, "OverlayIcon", UiArt.Tex(DualSpecArt), 7f, 0f, 18f, 32f);
                st.Checked = UiKit.Pic(st.B.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 32f, 32f);
                st.B.Hilite = UiKit.Pic(st.B.transform, "Highlight", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, 32f, 32f);
                st.B.Hilite.enabled = false;
                st.B.Click = () => SelectSpec(k);
                st.B.Tip = () => SpecTip(k);
                _specTabs[i] = st;
            }

            // bottom tabs
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                _tabs[i] = new UiTalentTab(Root, "PlayerTalentFrameTab" + (i + 1), 15f, 434f, () => SelectTab(k));
            }
            _tabs[GlyphTab].SetText(UiTalentText.G("GLYPHS", "Glyphs"), 384f);   // GS 3797

            _closeButton = UiKit.CloseButton(Root, 324f, 9f, () => { if (_close != null) _close(); });

            // the in-window confirm (ours)
            _confirm = UiKit.Rect(Root, "TalentConfirm", 32f, 170f, 300f, 118f);
            UiKit.Solid(_confirm, "Background", new Color(0f, 0f, 0f, 0.9f), 0f, 0f, 300f, 118f);
            UiKit.Solid(_confirm, "Edge", new Color(UiArt.Gold.r, UiArt.Gold.g, UiArt.Gold.b, 0.6f), 0f, 0f, 300f, 2f);
            _confirmText = UiKit.Text(_confirm, "Text", "", 12f, Color.white, TextAlignmentOptions.Center, 10f, 10f, 280f, 66f);
            _confirmYes = UiKit.PanelButton(_confirm, "Yes", 40f, 84f, 100f, 22f, () => CloseConfirm(true));
            _confirmNo = UiKit.PanelButton(_confirm, "No", 160f, 84f, 100f, 22f, () => CloseConfirm(false));
            _confirm.gameObject.SetActive(false);

            Root.gameObject.SetActive(false);
        }

        static void ResizePanelButton(UiButton b, float w, float h)
        {
            UiKit.Size(b.Rt, w, h);
            if (b.Slices != null && b.Slices.Length == 3)
            {
                UiKit.Size(b.Slices[1].rectTransform, w - 24f, h);
                UiKit.Move(b.Slices[2].rectTransform, w - 12f, 0f);
            }
            if (b.Hilite != null) UiKit.Size(b.Hilite.rectTransform, w, h);
            if (b.Label != null) UiKit.Size(b.Label.rectTransform, w - 8f, h);
        }

        // ------------------------------------------------------------------ open / refresh

        protected override void OnOpen()
        {
            var p = Ctx.P;
            if (p == null) return;
            var T = p.Talents;
            _openReq = T.OpenRequest;
            PickForRequest(p, !_opened);
            _opened = true;
            _sig = int.MinValue;
        }

        protected override void OnClose()
        {
            var p = Ctx.P;
            // the glyph waiting for a socket is given up with the frame (WoW stops the glyph's spell targeting)
            if (p != null && p.Talents.GlyphPick && Ctx.S != null) Ctx.S.InscribeGlyph(-1);
            if (_confirm.gameObject.activeSelf && !_wipeFallback) CloseConfirm(false);
        }

        /// <summary>The spec and tab to show: a waiting glyph -> the Glyphs tab of the active spec; a trainer's wipe question -> the active
        /// spec (UIParent.lua:880-891); the first open -> the active spec on its tree with the most points (PlayerTalentTab_GetBestDefaultTab).</summary>
        void PickForRequest(PlayerState p, bool first)
        {
            var T = p.Talents;
            EnsureDb();
            if (T.GlyphPick && p.Level >= InscriptionLevel) { _spec = T.ActiveSpec; _tab = GlyphTab; }
            else if (T.WipePending || first) { _spec = T.ActiveSpec; _tab = BestTab(p, _spec); }
            _sig = int.MinValue;
        }

        void EnsureDb()
        {
            if (_db == null && UiArt.Files != null) _db = TalentDb.Get(UiArt.Files);
            if (_spells == null && UiArt.Files != null) _spells = SpellDb.Get(UiArt.Files);
        }

        int BestTab(PlayerState p, int spec)
        {
            if (_db == null || !_db.Available || spec == PetSpec) return 0;
            var tabs = _db.ClassTabs(p.Class);
            var s = p.Talents.Specs[spec >= 0 && spec < 2 ? spec : 0];
            int best = 0, bestPts = 0;
            for (int i = 0; i < tabs.Count; i++) { int n = _db.PointsInTab(s, tabs[i].Id); if (n > bestPts) { bestPts = n; best = i; } }
            return best;
        }

        public override void Refresh(float now)
        {
            float dt = _lastNow >= 0f ? Mathf.Clamp(now - _lastNow, 0f, 0.1f) : 0f;
            _lastNow = now;
            PlayerState p = Ctx.P;
            if (p == null) return;
            var off = Ctx.S as OfflineSession;
            if (off != null) off.TalentsOfflineSync();
            EnsureDb();
            var T = p.Talents;
            if (T.OpenRequest != _openReq) { _openReq = T.OpenRequest; PickForRequest(p, false); }
            bool previewing = Ctx.Vr || WowSettings.PreviewTalents;
            int family = p.PetInfo != null ? p.PetInfo.Family : 0;
            int sig = T.Counter;
            sig = sig * 31 + p.Level; sig = sig * 31 + (int)p.Class; sig = sig * 31 + (previewing ? 1 : 0); sig = sig * 31 + _spec;
            sig = sig * 31 + _tab; sig = sig * 31 + family; sig = sig * 31 + _previewVersion; sig = sig * 31 + p.CastingSpellId;
            sig = sig * 31 + (_db != null ? 1 : 0) + (_spells != null ? 2 : 0);
            if (sig != _sig) { _sig = sig; Rebuild(p, T, previewing, off); }
            if (_tab != GlyphTab)
            {
                _tree.TickIcons();
                if (!Ctx.Vr && _tree.Hovered) { float w = Input.mouseScrollDelta.y; if (w != 0f) _tree.Wheel(w); }
            }
            else _glyphs.Refresh(T, _db, _spec, _spec == T.ActiveSpec, true, dt);
            TickSpecIcons(p);
            TickWipe(p);
            if (_confirm.gameObject.activeSelf && _confirmKeep != null && !_confirmKeep()) { _confirmAccept = _confirmCancel = null; _confirmKeep = null; _confirm.gameObject.SetActive(false); _wipeFallback = false; }
        }

        void Rebuild(PlayerState p, TalentsView T, bool previewing, OfflineSession off)
        {
            bool hasPet = T.PetTalentTabId != 0 && p.PetInfo != null && T.PetKnown && _db != null && _db.Tab(T.PetTalentTabId) != null;
            if (_spec == 1 && T.SpecCount < 2) _spec = T.ActiveSpec;
            if (_spec == PetSpec && !hasPet) _spec = T.ActiveSpec;
            if (_spec < 0 || _spec > PetSpec) _spec = 0;
            var m = _models[_spec];
            m.Db = _db; m.Spells = _spells; m.P = p; m.Previewing = previewing;
            m.Offline = off != null;
            m.SimulatedOffline = off != null ? (Func<int, bool>)off.TalentSimulatedOffline : null;
            if (_spec == PetSpec)
            {
                m.Spec = T.Pet; m.Active = true; m.Free = T.PetFreePoints;
                m.PetType = _db != null ? _db.PetTalentType(p.PetInfo != null ? p.PetInfo.Family : 0) : -1;
            }
            else
            {
                m.Spec = T.Specs[_spec];
                m.Active = _spec == T.ActiveSpec && T.Known;
                int total = T.FreePoints + TalentDb.PointsSpent(T.Specs[T.ActiveSpec]);   // both specs have the level's points (ours: the packet only carries the active spec's)
                m.Free = m.Active ? T.FreePoints : Math.Max(0, total - TalentDb.PointsSpent(m.Spec));
            }
            if (!previewing || !m.Active) m.Preview.Clear(); else m.Sanitize();

            // the trees of the shown spec
            _trees.Clear();
            if (_db != null && _db.Available)
            {
                if (_spec == PetSpec) _trees.Add(_db.PetView(_db.Tab(T.PetTalentTabId), T.PetCategory));
                else foreach (var t in _db.ClassTabs(p.Class)) _trees.Add(t);
            }
            bool glyphTab = _spec != PetSpec && p.Level >= InscriptionLevel && _db != null;   // PlayerTalentFrame_UpdateTabs: meetsGlyphLevel and spec.hasGlyphs
            if (_tab == GlyphTab && !glyphTab) _tab = 0;
            if (_tab != GlyphTab && _tab >= _trees.Count) _tab = 0;
            bool multi = T.SpecCount > 1;

            // title and portrait
            if (_tab == GlyphTab)
            {
                _title.enabled = false;
                _glyphs.SetTitle(multi ? (_spec == 0 ? UiTalentText.G("TALENT_SPEC_PRIMARY_GLYPH", "Primary Glyphs") : UiTalentText.G("TALENT_SPEC_SECONDARY_GLYPH", "Secondary Glyphs"))   // GS 7458 / 7460
                                       : UiTalentText.G("GLYPHS", "Glyphs"));
            }
            else
            {
                _title.enabled = true;
                string title = multi || _spec == PetSpec ? SpecName(_spec) : UiTalentText.G("TALENTS", "Talents");   // PlayerTalentFrame_UpdateActiveSpec
                if (_title.text != title) _title.text = title;
            }
            if (p.Class != _portraitClass)
            {
                _portraitClass = p.Class;
                Vector4 c = UiArt.ClassCoords(p.Class);
                UiKit.Coords(_portrait, c.x, c.y, c.z, c.w);
            }

            // spec tabs (PlayerSpecTab_Update): Primary always, Secondary with dual spec, Pet with a hunter pet that has talents
            for (int i = 0; i < 3; i++)
            {
                var st = _specTabs[i];
                bool show = i == 0 || (i == 1 && multi) || (i == PetSpec && hasPet);
                if (st.B.gameObject.activeSelf != show) { st.B.gameObject.SetActive(show); st.Bg.enabled = show; }
                if (!show) continue;
                st.Checked.enabled = i == _spec;
                st.Overlay.enabled = multi && i != PetSpec;
                st.Grey = i != PetSpec && i != T.ActiveSpec;                // SetDesaturation(normalTexture, not isActiveSpec) - ours: darkened
            }

            // the bottom tabs (PlayerTalentFrame_UpdateTabs)
            int nTrees = Math.Min(3, _trees.Count);
            float total2 = 0f;
            for (int i = 0; i < 3; i++)
            {
                bool show = i < nTrees;
                _tabs[i].Show(show);
                if (show) { _tabs[i].SetText(_trees[i].Name, 384f); total2 += _tabs[i].W; }
            }
            _tabs[GlyphTab].Show(glyphTab);
            if (glyphTab) total2 += _tabs[GlyphTab].W;
            // PlayerTalentFrame_UpdateTabs: shave 10 units off the widest tab while the tabs' widths add up to the frame's width or more
            int guard = 0;
            while (total2 >= W && guard++ < 60)
            {
                int widest = -1;
                for (int i = 0; i < 4; i++) if (_tabs[i].B.gameObject.activeSelf && (widest < 0 || _tabs[i].W > _tabs[widest].W)) widest = i;
                if (widest < 0 || _tabs[widest].W <= 60f) break;
                string text = widest == GlyphTab ? UiTalentText.G("GLYPHS", "Glyphs") : _trees[widest].Name;
                float before = _tabs[widest].W;
                _tabs[widest].SetText(text, before - 10f);
                total2 -= before - _tabs[widest].W;
            }
            float x = 15f;
            for (int i = 0; i < 4; i++)
            {
                if (!_tabs[i].B.gameObject.activeSelf) continue;
                _tabs[i].Place(x, 434f);
                _tabs[i].Select(i == _tab);
                x += _tabs[i].W - 15f;
            }
            for (int i = 0; i < 4; i++) _tabs[i].B.transform.SetAsLastSibling();
            _closeButton.transform.SetAsLastSibling();
            _confirm.SetAsLastSibling();

            // the active-spec controls (PlayerTalentFrame_UpdateControls)
            bool status = multi && _spec != PetSpec && _spec == T.ActiveSpec;
            bool activate = multi && _spec != PetSpec && _spec != T.ActiveSpec;
            _statusFrame.gameObject.SetActive(status && _tab != GlyphTab);
            _activate.gameObject.SetActive(activate);
            _activate.Interactable = p.CastingSpellId != (_spec == 0 ? ActivatePrimary : ActivateSecondary);
            _unlearnOffline.gameObject.SetActive(off != null && _spec == 0 && _tab != GlyphTab && TalentDb.PointsSpent(T.Specs[0]) > 0);

            if (_tab == GlyphTab)
            {
                _tree.Show(false);
                _pointsBar.gameObject.SetActive(false);
                _previewBar.gameObject.SetActive(false);
                _glyphs.Show(true);
                _shownTreeId = -1;
                return;
            }
            _glyphs.Show(false);
            var tree = _tab < _trees.Count ? _trees[_tab] : null;
            bool preview = previewing && m.Active && m.Free > 0;
            _previewBar.gameObject.SetActive(preview);
            if (preview) { _learn.Interactable = m.PreviewSpent > 0; _reset.Interactable = m.PreviewSpent > 0; }
            float py = preview ? PointsYPreview : PointsY;
            UiKit.Move(_pointsBar, 16f, py);
            _pointsBar.gameObject.SetActive(true);
            _tree.SetFrameHeight(py - UiTalentTree.FrameY);
            _tree.Show(true);
            _tree.Draw(tree, m, tree == null || tree.Id != _shownTreeId);
            _shownTreeId = tree != null ? tree.Id : -1;
            string spent = tree != null ? UiTalentText.Fmt(UiTalentText.G("MASTERY_POINTS_SPENT", "%1$s Talents: %2$s"), tree.Name, "<color=#ffffff>" + m.TabPoints(tree.Id) + "</color>") : "";   // GS 4903
            if (_spentText.text != spent) _spentText.text = spent;
            string unspent = UiTalentText.Fmt(UiTalentText.G("UNSPENT_TALENT_POINTS", "Unspent Talents: %s"), "<color=#ffffff>" + Math.Max(0, m.Remaining) + "</color>");   // GS 7910
            if (_unspentText.text != unspent) _unspentText.text = unspent;
            if (_db == null || !_db.Available) _spentText.text = "No talent data in the game files (Talent.dbc).";   // ours
        }

        void TickSpecIcons(PlayerState p)
        {
            var T = p.Talents;
            for (int i = 0; i < 3; i++)
            {
                var st = _specTabs[i];
                if (!st.B.gameObject.activeSelf) continue;
                string path = SpecIconPath(p, i);
                if (path == st.IconShown) continue;
                Texture2D t;
                if (path == null)
                {
                    t = UiArt.Tex(UiArt.ClassCircles);
                    if (t == null) continue;
                    Vector4 c = UiArt.ClassCoords(p.Class);
                    st.Icon.texture = t;
                    UiKit.Coords(st.Icon, c.x, c.y, c.z, c.w);
                }
                else
                {
                    t = UiArt.Icon(path);
                    if (t == null) continue;
                    st.Icon.texture = t;
                    UiKit.Coords(st.Icon, 0f, 1f, 0f, 1f);
                }
                st.IconShown = path;
                st.Icon.color = st.Grey ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
            }
            for (int i = 0; i < 3; i++)
            {
                var st = _specTabs[i];
                Color c = st.Grey ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
                if (st.Icon.texture != null && st.Icon.color != c) st.Icon.color = c;
            }
        }

        /// <summary>A spec tab's icon: with dual spec the icon of the tree with the most points (TalentFrame_UpdateSpecInfoCache primaryTabIndex);
        /// else the character (ours: the class circle - WoW draws the 3D portrait); the pet tab: the pet family's icon.</summary>
        string SpecIconPath(PlayerState p, int i)
        {
            var T = p.Talents;
            if (i == PetSpec)
            {
                if (p.PetInfo != null && !string.IsNullOrEmpty(p.PetInfo.FamilyIcon)) return p.PetInfo.FamilyIcon;
                var pt = _db != null ? _db.Tab(T.PetTalentTabId) : null;
                return pt != null ? pt.IconPath : null;
            }
            if (T.SpecCount < 2 || _db == null || !_db.Available) return null;
            var tabs = _db.ClassTabs(p.Class);
            int best = -1, bestPts = 0;
            for (int k = 0; k < tabs.Count; k++) { int n = _db.PointsInTab(T.Specs[i], tabs[k].Id); if (n > bestPts) { bestPts = n; best = k; } }
            return best >= 0 ? tabs[best].IconPath : null;
        }

        string SpecName(int i)
        {
            return i == PetSpec ? UiTalentText.G("TALENT_SPEC_PET_PRIMARY", "Pet Talents")                // GS 7456
                 : i == 1 ? UiTalentText.G("TALENT_SPEC_SECONDARY", "Secondary Talents")                  // GS 7459
                 : UiTalentText.G("TALENT_SPEC_PRIMARY", "Primary Talents");                              // GS 7457
        }

        /// <summary>PlayerSpecTab_OnEnter: the spec's name, "These are your active talents" and the points per tree (ours: plain text).</summary>
        string SpecTip(int i)
        {
            var p = Ctx.P;
            if (p == null || _db == null) return null;
            var T = p.Talents;
            var sb = new System.Text.StringBuilder();
            sb.Append("<size=14><color=#ffffff>").Append(SpecName(i)).Append("</color></size>");
            if (i != PetSpec && i == T.ActiveSpec && T.SpecCount > 1) sb.Append("\n<color=#00ff00>").Append(UiTalentText.G("TALENT_SPECTAB_TOOLTIP_ACTIVE", "These are your active talents")).Append("</color>");   // GS 7453
            if (i == PetSpec)
            {
                var pt = _db.Tab(T.PetTalentTabId);
                if (pt != null) sb.Append("\n<color=#ffd100>").Append(pt.Name).Append(": </color><color=#ffffff>").Append(TalentDb.PointsSpent(T.Pet)).Append(" points</color>");
            }
            else
                foreach (var t in _db.ClassTabs(p.Class))
                    sb.Append("\n<color=#ffd100>").Append(t.Name).Append(": </color><color=#ffffff>").Append(_db.PointsInTab(T.Specs[i], t.Id)).Append(" points</color>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ actions

        void SelectSpec(int i)
        {
            var p = Ctx.P;
            if (p == null || i == _spec) return;
            UiSound.Named("igCharacterInfoTab");
            _spec = i;
            _tab = BestTab(p, i);
            _sig = int.MinValue;
        }

        void SelectTab(int i)
        {
            if (i == _tab) return;
            UiSound.Named("igCharacterInfoTab");                                // PlayerTalentFrameTab_OnClick
            _tab = i;
            _sig = int.MinValue;
        }

        void Activate()
        {
            var p = Ctx.P;
            if (p == null || Ctx.S == null || _spec == PetSpec) return;
            Ctx.S.ActivateSpec(_spec);                                         // SetActiveTalentGroup -> the 5 s activation spell
        }

        void TalentClicked(TalentInfo t, bool right)
        {
            var p = Ctx.P;
            if (p == null || Ctx.S == null || t == null) return;
            var m = _models[_spec];
            if (!m.Active) return;                                              // only the active spec (or the pet's) takes clicks
            if (m.Previewing)
            {
                if (right) { if (m.CanRemove(t)) { m.Add(t, -1); _previewVersion++; } }
                else if (m.CanAdd(t)) { m.Add(t, 1); _previewVersion++; }
                return;
            }
            if (right || !m.CanAdd(t)) return;
            Ctx.S.LearnTalents(m.Pet, new[] { new KeyValuePair<int, int>(t.Id, m.Rank(t.Id) + 1) });   // LearnTalent: one rank
        }

        void Learn()
        {
            var m = _models[_spec];
            if (m.PreviewSpent <= 0 || Ctx.S == null) return;
            Confirm("CONFIRM_LEARN_PREVIEW_TALENTS", UiTalentText.G("CONFIRM_LEARN_PREVIEW_TALENTS", "Are you sure you want to learn these talents?"),   // GS 1816
                UiTalentText.G("YES", "Yes"), UiTalentText.G("NO", "No"), -1, null, () =>
                {
                    var pairs = m.PreviewPairs();
                    m.Preview.Clear();
                    _previewVersion++;
                    if (pairs.Count > 0 && Ctx.S != null) Ctx.S.LearnTalents(m.Pet, pairs);   // LearnPreviewTalents
                }, null);
        }

        void ResetPreview()
        {
            var m = _models[_spec];
            if (m.Preview.Count == 0) return;
            m.Preview.Clear();                                                  // ResetGroupPreviewTalentPoints
            _previewVersion++;
        }

        void UnlearnOffline()
        {
            var off = Ctx.S as OfflineSession;
            if (off == null) return;
            Confirm("TALENTS_OFFLINE_UNLEARN", "Unlearn all of your talents? (Offline - no cost.)", UiTalentText.G("ACCEPT", "Accept"), UiTalentText.G("CANCEL", "Cancel"),   // ours
                -1, null, () => { off.ResetTalentsOffline(); _previewVersion++; }, null);
        }

        void GlyphClicked(int socket, bool remove)
        {
            var p = Ctx.P;
            if (p == null || Ctx.S == null) return;
            var T = p.Talents;
            if (_spec != T.ActiveSpec || _spec == PetSpec) return;              // only the active spec's glyphs (GlyphFrameGlyph_OnClick)
            int glyph = T.Specs[_spec].Glyphs[socket];
            if (remove) { if (glyph != 0) ConfirmRemoveGlyph(socket, glyph); return; }
            if (T.GlyphPick)
            {
                if (glyph != 0 && UiGlyphFrame.Matches(T, _db, socket))
                    Confirm("CONFIRM_GLYPH_PLACEMENT", UiTalentText.G("CONFIRM_GLYPH_PLACEMENT", "Are you sure you want to inscribe this glyph?  The existing glyph will be lost."),   // GS 1811
                        UiTalentText.G("YES", "Yes"), UiTalentText.G("NO", "No"), -1, null, () => { if (Ctx.S != null) Ctx.S.InscribeGlyph(socket); }, null);
                else Ctx.S.InscribeGlyph(socket);                                // PlaceGlyphInSocket
            }
            else if (Ctx.Vr && glyph != 0) ConfirmRemoveGlyph(socket, glyph);   // VR has no Shift + right click (ours)
        }

        void ConfirmRemoveGlyph(int socket, int glyph)
        {
            var g = _db != null ? _db.Glyph(glyph) : null;
            var s = g != null && _spells != null ? _spells.Find(g.SpellId) : null;
            int spec = _spec;
            Confirm("CONFIRM_REMOVE_GLYPH", UiTalentText.Fmt(UiTalentText.G("CONFIRM_REMOVE_GLYPH", "Are you sure you want to remove %s? This glyph will be permanently destroyed."),   // GS 1827
                s != null ? s.Name : "this glyph"), UiTalentText.G("YES", "Yes"), UiTalentText.G("NO", "No"), -1, null,
                () => { var p = Ctx.P; if (p != null && Ctx.S != null && spec == p.Talents.ActiveSpec) Ctx.S.RemoveGlyph(socket); }, null);
        }

        /// <summary>CONFIRM_TALENT_WIPE while the session says a trainer asks (StaticPopup.lua:2498-2518: Accept / Cancel, the money, hidden when the
        /// trainer is out of reach - the session's TickTalents clears WipePending; Cancel also hides the Talents window).</summary>
        void TickWipe(PlayerState p)
        {
            var T = p.Talents;
            if (T.WipePending && !_wipeShown)
            {
                _wipeShown = true;
                if (_tab == GlyphTab || _spec != T.ActiveSpec) { _spec = T.ActiveSpec; _tab = BestTab(p, _spec); _sig = int.MinValue; }
                _wipeHandle = Confirm("CONFIRM_TALENT_WIPE", UiTalentText.G("CONFIRM_TALENT_WIPE", "Do you want to unlearn all of your talents?  This will unsummon any controlled pet and the cost will increase each time."),   // GS 1832
                    UiTalentText.G("ACCEPT", "Accept"), UiTalentText.G("CANCEL", "Cancel"), T.WipeCost,   // GS 11 / 1338
                    () => Ctx.P != null && Ctx.P.Talents.WipePending,
                    () => { if (Ctx.S != null) Ctx.S.AnswerTalentWipe(true); },
                    () => { if (Ctx.S != null) Ctx.S.AnswerTalentWipe(false); if (_close != null && IsOpen) _close(); });
                _wipeFallback = _wipeHandle == 0;
            }
            else if (!T.WipePending && _wipeShown)
            {
                _wipeShown = false;
                _wipeHandle = 0;
            }
        }

        /// <summary>WoW's StaticPopup through uicore's UiPopup; when it shows nothing (returns 0) the window's own confirm box (ours) asks instead.</summary>
        int Confirm(string which, string text, string yes, string no, int money, Func<bool> keep, Action accept, Action cancel)
        {
            int h = 0;
            try
            {
                h = UiPopup.Show(new UiPopupSpec
                {
                    Which = which, Text = text, Button1 = yes, Button2 = no, OnAccept = accept, OnCancel = cancel, KeepOpen = keep,
                    Money = money, HideOnEscape = true, Timeout = 0f, Exclusive = true, WhileDead = true
                });
            }
            catch (Exception e) { WowLog.Warn("UI talents: popup " + which + " failed: " + e.Message); h = 0; }
            if (h != 0) return h;
            if (!_fallbackLogged) { _fallbackLogged = true; WowLog.Info("UI talents: the StaticPopup (UiPopup) showed nothing - the Talents window asks in its own box (ours) - logged once"); }
            _confirmText.text = money >= 0 ? text + "\n" + UiArt.MoneyText(money) : text;
            _confirmYes.SetLabel(yes);
            _confirmNo.SetLabel(no);
            _confirmAccept = accept;
            _confirmCancel = cancel;
            _confirmKeep = keep;
            _confirm.gameObject.SetActive(true);
            _confirm.SetAsLastSibling();
            return 0;
        }

        void CloseConfirm(bool accept)
        {
            Action a = accept ? _confirmAccept : _confirmCancel;
            _confirmAccept = _confirmCancel = null;
            _confirmKeep = null;
            _confirm.gameObject.SetActive(false);
            _wipeFallback = false;
            if (a == null) return;
            try { a(); }
            catch (Exception e) { WowLog.Error("UI talents: confirm action failed: " + e); }
        }
    }
}
