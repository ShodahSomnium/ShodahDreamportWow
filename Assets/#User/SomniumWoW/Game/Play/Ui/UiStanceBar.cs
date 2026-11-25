// iteration 11 - owner vrcast (proposal-vrcast.md 4.2 / 6.4): ShapeshiftBarFrame on the desktop - one 30x30 button per known stance / form
// (BonusActionBarFrame.xml:201-330, BonusActionBarFrame.lua:118-200), the active one checked, click (or Ctrl+F1..F10, DefaultBindings.wtf:43-52)
// = cast it; the active form's button leaves the form (warrior stances excepted: recasting a stance does nothing - ours).
// spellui (2026-10-04, the user: "I can't see my special abilities like Auras for paladin, forms for druids"): the list is now WoW's own rule,
// read out of Wow.exe - UiSpellFacts.Stances (Game/Play/Ui/Pure/UiSpellRules.cs, header (1)): aura 36 OR SPELL_ATTR2_USE_SHAPESHIFT_BAR 0x10
// (paladin auras, death knight presences), never SPELL_ATTR2_NO_SHAPESHIFT_UI 0x2 (Ghost Wolf), one button per rank chain, StanceBarOrder
// order. An aura / presence is checked while the player has its aura (any rank, cast by the player); clicking it casts it like
// CastShapeshiftForm (Wow.exe 0x53C0F0 only calls the cast function with the button's spell): an active aura is cast again (it stays on -
// the realm / the offline session refresh it); an aura goes off by right-clicking its buff. Hunter aspects: no flag, no aura 36 -> not listed.
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public sealed class UiStanceBar
    {
        public const int Max = 10;   // NUM_SHAPESHIFT_SLOTS (BonusActionBarFrame.lua:5)
        public const string BarArt = @"Interface\ShapeshiftBar\ShapeshiftBar.blp";
        public const string MiddleArt = @"Interface\ShapeshiftBar\ShapeshiftBarMiddle.blp";
        /// <summary>Ours: CTRL-F1..F10 (SHAPESHIFTBUTTON1..10) abbreviated for the hot key corner.</summary>
        public static readonly string[] Keys = { "c-F1", "c-F2", "c-F3", "c-F4", "c-F5", "c-F6", "c-F7", "c-F8", "c-F9", "c-F10" };
        private const float AnchorH = 140f;   // the anchor rect: 1024 x 140 at the bottom centre (the main bar is the bottom 53)

        private readonly UiContext _ctx;
        private readonly RectTransform _root;
        private readonly RawImage _left, _right;
        private readonly RawImage[] _middle = new RawImage[Max - 2];
        private readonly UiButton[] _btn = new UiButton[Max];
        private readonly RawImage[] _normal = new RawImage[Max];
        private readonly int[] _spell = new int[Max], _form = new int[Max];
        private readonly StanceEntry[] _entry = new StanceEntry[Max];
        private readonly string[] _iconPath = new string[Max];
        private readonly bool[] _iconLoaded = new bool[Max], _checked = new bool[Max];
        private readonly int[] _greyShown = new int[Max];   // -1 unknown, 0 white, 1 grey
        private readonly float[] _sweep = new float[Max];
        private int _count, _sig = int.MinValue, _laidCount = -1;
        private bool _laidBl;

        public UiStanceBar(UiSurface s, UiContext ctx)
        {
            _ctx = ctx;
            RectTransform anchor = s.Anchor("Stance bar", new Vector2(0.5f, 0f), 1024f, AnchorH, 0f, 0f);
            _root = UiKit.Rect(anchor, "ShapeshiftBarFrame", 0f, 0f, 1024f, AnchorH);
            _root.gameObject.AddComponent<Canvas>();
            if (UiKit.Pointable) _root.gameObject.AddComponent<GraphicRaycaster>();   // a nested canvas needs its own (WowMapTabletLink.cs:864)
            Texture2D bar = UiArt.Tex(BarArt), mid = UiArt.Tex(MiddleArt);
            _left = UiKit.Pic(_root, "ShapeshiftBarLeft", bar, 0f, 0.734375f, 0f, 0.296875f, 30f, 0f, 47f, 38f);
            for (int k = 0; k < _middle.Length; k++) _middle[k] = UiKit.Pic(_root, "ShapeshiftBarMiddle " + k, mid, 0f, 0f, 37f, 38f);
            _right = UiKit.Pic(_root, "ShapeshiftBarRight", bar, 0.328125f, 1f, 0.3125f, 0.6015625f, 0f, 0f, 43f, 38f);
            for (int i = 0; i < Max; i++)
            {
                int k = i;
                UiButton b = UiKit.Slot(_root, "ShapeshiftButton" + (i + 1), 0f, 0f, 30f, false);
                _normal[i] = UiKit.Pic(b.transform, "Normal", UiArt.Tex(UiArt.Quickslot), -17f, -16f, 64f, 64f);
                _normal[i].transform.SetSiblingIndex(1);   // over the icon, under the highlight (ActionButtonTemplate layers)
                b.Checked = UiKit.Pic(b.transform, "Checked", UiArt.Add(UiArt.CheckButtonHilight), 0f, 0f, 30f, 30f);
                b.Checked.enabled = false;
                var sweepRt = UiKit.Rect(b.transform, "Cooldown", 0f, 0f, 30f, 30f);
                b.Sweep = sweepRt.gameObject.AddComponent<Image>();
                b.Sweep.sprite = UiArt.WhiteSprite;
                b.Sweep.type = Image.Type.Filled;
                b.Sweep.fillMethod = Image.FillMethod.Radial360;
                b.Sweep.fillOrigin = (int)Image.Origin360.Top;
                b.Sweep.fillClockwise = false;
                b.Sweep.color = new Color(0f, 0f, 0f, 0.7f);
                b.Sweep.raycastTarget = false;
                if (UiKit.Over) b.Sweep.material = UiArt.OverUi;
                b.Sweep.enabled = false;
                b.HotKey = UiKit.Text(b.transform, "HotKey", Keys[i], 10f, new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.TopRight, -2f, 1f, 30f, 11f, UiArt.Numbers);
                b.Click = () => Press(k);
                b.RightClick = () => Press(k);
                b.Tip = () => Tip(k);
                b.gameObject.SetActive(false);
                _btn[i] = b;
                _sweep[i] = -1f;
            }
            _root.gameObject.SetActive(false);
        }

        private AbilityView AbilityAt(int i)
        {
            return i >= 0 && i < _count ? UiContext.Find(_ctx.P, _spell[i]) : null;
        }

        private string Tip(int i)
        {
            AbilityView v = AbilityAt(i);
            return v != null ? UiTooltip.Ability(v, null) : null;
        }

        /// <summary>Ctrl+F1..F10 / a click: the button i (0-based). WoW's CastShapeshiftForm: the active form's button leaves it.</summary>
        public void Press(int i)
        {
            IWowSession s = _ctx.S;
            if (s == null || s.Player == null || i < 0 || i >= _count) return;
            int spell = _spell[i], form = _form[i];
            bool stance = form == 17 || form == 18 || form == 19;   // Battle / Defensive / Berserker Stance (SpellShapeshiftForm ids)
            if (form != 0 && s.Player.ShapeshiftForm == form && !stance) { s.CancelAura(spell); return; }
            s.CastSpell(spell, 0);   // a form, or an aura / presence (active or not: CastShapeshiftForm casts, Wow.exe 0x53C0F0)
        }

        /// <summary>The known stances / forms / auras / presences (UiSpellFacts.Stances, the file header), at most 10.</summary>
        private void BuildList(PlayerState p)
        {
            var book = p.Spellbook;
            int sig = book.Count * 7919 + p.Level;
            for (int i = 0; i < book.Count; i++) if (book[i] != null) sig = sig * 31 + book[i].SpellId;
            if (sig == _sig) return;
            SpellDb db = WowPlay.Files != null ? SpellDb.Get(WowPlay.Files) : null;
            UiSpellFacts facts = WowPlay.Files != null ? UiSpellFacts.Get(WowPlay.Files) : null;
            if (db == null || facts == null) return;
            _sig = sig;
            var list = facts.Stances(book, db);
            int n = System.Math.Min(Max, list.Count);
            bool changed = n != _count;
            for (int i = 0; i < n; i++)
            {
                if (_spell[i] != list[i].SpellId) changed = true;
                _entry[i] = list[i];
                _spell[i] = list[i].SpellId;
                _form[i] = list[i].Form;
            }
            for (int i = n; i < Max; i++) { _entry[i] = null; _spell[i] = 0; _form[i] = 0; }
            if (changed)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++) sb.Append(i > 0 ? ", " : "").Append(list[i].Name).Append(' ').Append(list[i].SpellId).Append(list[i].Form != 0 ? " (form " + list[i].Form + ")" : " (aura)");
                WowLog.Info("UI stance bar: " + n + " forms for " + p.Class + (n > 0 ? " (" + sb + ")" : "") + " - Wow.exe's rule: aura 36 or SPELL_ATTR2_USE_SHAPESHIFT_BAR, StanceBarOrder order.");
            }
            _count = n;
            for (int i = 0; i < Max; i++) { _iconPath[i] = null; _iconLoaded[i] = false; _btn[i].Icon.enabled = false; _checked[i] = false; _btn[i].Checked.enabled = false; }
        }

        /// <summary>ShapeshiftBar_Update (BonusActionBarFrame.lua:125-149) + UIParent.lua:1813-1844 (Bottom Left shown: no art, 50x50 frames).</summary>
        private void Layout(int n, bool bottomLeft)
        {
            _laidCount = n;
            _laidBl = bottomLeft;
            float frameBottom = AnchorH - 53f - (bottomLeft ? UiBars.ActionBarOffset : 0f);   // MainMenuBar TOPLEFT (+45 with Bottom Left, UIParent.lua:1189)
            float artTop = frameBottom - 38f;
            bool art = !bottomLeft && n > 0;
            _left.enabled = art;
            UiKit.Move(_left.rectTransform, 30f, artTop);
            int mids = n > 2 ? n - 2 : 0;
            for (int k = 0; k < _middle.Length; k++)
            {
                _middle[k].enabled = art && k < mids;
                UiKit.Move(_middle[k].rectTransform, 30f + 47f + 37f * k, artTop);
            }
            _right.enabled = art;
            UiKit.Move(_right.rectTransform, n == 1 ? 30f + 12f : 30f + 47f + 37f * mids, artTop);
            float x = 30f + (n == 1 ? 12f : 10f);
            float f = bottomLeft ? 50f : 64f;
            for (int i = 0; i < Max; i++)
            {
                if (i == 1) x += 30f + 8f;
                else if (i > 1) x += 30f + 7f;
                UiKit.Move(_btn[i].Rt, x, frameBottom - 3f - 30f);
                _btn[i].gameObject.SetActive(i < n);
                UiKit.Size(_normal[i].rectTransform, f, f);
                UiKit.Move(_normal[i].rectTransform, (30f - f) * 0.5f, (30f - f) * 0.5f + 1f);
            }
        }

        public void Refresh(PlayerState p, float now)
        {
            if (p == null || _ctx.S == null) return;
            BuildList(p);
            bool bl = UiBars.ShowBottomLeft;
            if (_count != _laidCount || bl != _laidBl) Layout(_count, bl);
            bool on = _count > 0;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) return;
            UiBars.NoteStance();
            for (int i = 0; i < _count; i++)
            {
                UiButton b = _btn[i];
                AbilityView v = UiContext.Find(p, _spell[i]);
                string path = v != null ? v.IconPath : null;
                if (path != _iconPath[i]) { _iconPath[i] = path; _iconLoaded[i] = false; b.Icon.enabled = false; }
                if (!_iconLoaded[i] && v != null)
                {
                    Texture2D t = UiArt.Icon(path);
                    if (t != null) { b.Icon.texture = t; b.Icon.enabled = true; _iconLoaded[i] = true; _greyShown[i] = -1; }
                }
                bool isChecked = UiSpellFacts.IsActive(_entry[i], p);   // a form: ShapeshiftForm; an aura / presence: the player's own aura of it
                if (isChecked != _checked[i]) { _checked[i] = isChecked; b.Checked.enabled = isChecked; }
                bool grey = v == null || UiActions.TintOf(v) != 1;   // ShapeshiftBar_UpdateState: not castable -> 0.4 (BonusActionBarFrame.lua:186-190)
                if ((grey ? 1 : 0) != _greyShown[i] && _iconLoaded[i]) { _greyShown[i] = grey ? 1 : 0; b.Icon.color = grey ? new Color(0.4f, 0.4f, 0.4f) : Color.white; }
                float frac = UiActions.CooldownOf(p, v);
                if (Mathf.Abs(frac - _sweep[i]) > 0.004f) { _sweep[i] = frac; b.Sweep.enabled = frac > 0f; b.Sweep.fillAmount = frac; }
            }
        }
    }
}
