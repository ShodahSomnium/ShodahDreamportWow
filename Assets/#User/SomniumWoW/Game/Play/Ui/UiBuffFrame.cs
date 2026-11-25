// Lead 2026-10-04 (the user, with a WoW screenshot: "move buffs/debuffs to the correct position on desktop to the right").
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// WoW's BuffFrame on the desktop (3.3.5a BuffFrame.xml / .lua, from the install): BuffFrame TOPRIGHT of UIParent (-205, -13);
    /// AuraButtonTemplate 30 x 30 with $parentCount NumberFontNormal BOTTOMRIGHT (-2, 2) and $parentDuration GameFontNormalSmall TOP to the
    /// button's BOTTOM; buffs right to left ("RIGHT", previous, "LEFT", -5, 0), BUFFS_PER_ROW 8, a new row "TOP" to the button 8 before's
    /// "BOTTOM" (0, -BUFF_ROW_SPACING), BUFF_ROW_SPACING 15 with durations shown (BuffFrame_UpdatePositions); up to BUFF_MAX_DISPLAY 32.
    /// Debuffs (DebuffButtonTemplate: UI-Debuff-Overlays border 33 x 32, TexCoords 0.296875-0.5703125 / 0-0.515625) below:
    /// DebuffButton1 TOPRIGHT at -((2 x spacing) + height) under one buff row, -rows x (spacing + height) under more
    /// (DebuffButton_UpdateAnchors), DEBUFF_MAX_DISPLAY 16. Durations SecondsToTimeAbbrev ("%d m" / "%d s" / "%d h"), gold, white under
    /// 60 s (BUFF_DURATION_WARNING_TIME). The border is DebuffTypeColor "none" (0.8, 0, 0): our auras carry no dispel type (ours).
    /// Right-click a buff = CancelAura (the player frame's rule); the tooltip as the player frame's row. Consolidated buffs and the
    /// weapon-enchant buttons are not built (ours).
    /// </summary>
    public sealed class UiBuffFrame
    {
        public const int BuffsPerRow = 8, MaxBuffs = 32, MaxDebuffs = 16;
        public const float Size = 30f, Gap = 5f, RowSpacing = 15f;
        private static readonly Color DebuffNone = new Color(0.8f, 0f, 0f);

        private sealed class Slot
        {
            public UiButton B;
            public TextMeshProUGUI Time, Count;
            public RawImage Border;
            public string Path;
            public int Secs = -2, Stacks = -1;
            public AuraView A;
        }

        public RectTransform Root;
        public System.Action<int> Cancel;
        public int MountSpell;
        private readonly Slot[] _buffs = new Slot[MaxBuffs], _debuffs = new Slot[MaxDebuffs];
        private readonly List<AuraView> _good = new List<AuraView>(MaxBuffs), _bad = new List<AuraView>(MaxDebuffs);
        private int _buffRowsLaid = -1;
        private bool _logged;

        public UiBuffFrame(UiSurface s)
        {
            Root = s.Anchor("BuffFrame", new Vector2(1f, 1f), 400f, 300f, -205f, -13f);
            Texture2D overlay = UiArt.Tex(@"Interface\Buttons\UI-Debuff-Overlays");
            for (int i = 0; i < MaxBuffs; i++) _buffs[i] = Make("BuffButton" + (i + 1), i, false, null);
            for (int i = 0; i < MaxDebuffs; i++) _debuffs[i] = Make("DebuffButton" + (i + 1), i, true, overlay);
            for (int i = 0; i < MaxBuffs; i++) UiKit.Move(_buffs[i].B.Rt, X(i), Y(i, 0f));
        }

        private Slot Make(string name, int index, bool debuff, Texture2D overlay)
        {
            var sl = new Slot();
            UiButton b = UiKit.Button(Root, name, 0f, 0f, Size, Size);
            b.Icon = UiKit.Pic(b.transform, "Icon", (Texture2D)null, 0f, 0f, Size, Size);
            b.Icon.enabled = false;
            if (debuff && overlay != null)
            {
                sl.Border = UiKit.Pic(b.transform, "Border", overlay, 0.296875f, 0.5703125f, 0f, 0.515625f, -1.5f, -1f, 33f, 32f);
                sl.Border.color = DebuffNone;
            }
            sl.Count = UiKit.Text(b.transform, "Count", "", 12f, Color.white, TextAlignmentOptions.BottomRight, 0f, 0f, Size - 2f, Size - 2f, UiArt.Numbers);
            sl.Time = UiKit.Text(b.transform, "Duration", "", 10f, UiArt.Gold, TextAlignmentOptions.Top, -10f, Size, Size + 20f, 14f);
            sl.Time.textWrappingMode = TextWrappingModes.NoWrap;
            b.Tip = () => Tip(sl.A);
            if (!debuff) b.RightClick = () => CancelOf(sl.A);
            b.gameObject.SetActive(false);
            sl.B = b;
            return sl;
        }

        // the buff at index i: right to left in rows of 8 from the frame's top right
        private static float X(int i) { return 400f - Size - (i % BuffsPerRow) * (Size + Gap); }
        private static float Y(int i, float top) { return top + (i / BuffsPerRow) * (Size + RowSpacing); }

        private string Tip(AuraView a)
        {
            if (a == null) return null;
            string name = string.IsNullOrEmpty(a.Name) ? "Aura" : a.Name;
            string tip = "<size=14><color=" + (a.Harmful ? "#ff2020" : "#ffffff") + ">" + name + "</color></size>";
            if (a.Duration > 0f) tip += "\n" + Mathf.CeilToInt(a.Remaining) + " sec remaining";
            if (!string.IsNullOrEmpty(a.Tooltip)) tip += "\n" + a.Tooltip;
            if (Cancel != null && !a.Harmful && a.SpellId != 0 && a.SpellId == MountSpell) tip += "\n<color=#00ff00>Right-click to dismount.</color>";
            return tip;
        }

        private void CancelOf(AuraView a)
        {
            if (Cancel == null || a == null || a.Harmful || a.SpellId <= 0) return;
            WowLog.Info("UI: buff '" + (a.Name ?? "?") + "' right-clicked (BuffFrame) - CancelAura(" + a.SpellId + ")" + (a.SpellId == MountSpell ? " (the mount aura: dismount)" : "") + ".");
            Cancel(a.SpellId);
        }

        public void Refresh(PlayerState p)
        {
            _good.Clear(); _bad.Clear();
            if (p != null && p.Auras != null)
                for (int i = 0; i < p.Auras.Count; i++)
                {
                    AuraView a = p.Auras[i];
                    if (a == null) continue;
                    if (a.Harmful) { if (_bad.Count < MaxDebuffs) _bad.Add(a); }
                    else if (_good.Count < MaxBuffs) _good.Add(a);
                }
            MountSpell = p != null && p.Mounted ? p.MountSpellId : 0;
            for (int i = 0; i < MaxBuffs; i++) Show(_buffs[i], i < _good.Count ? _good[i] : null);
            // debuffs under the buff rows (DebuffButton_UpdateAnchors)
            int rows = Mathf.Max(1, (_good.Count + BuffsPerRow - 1) / BuffsPerRow);
            if (rows != _buffRowsLaid)
            {
                _buffRowsLaid = rows;
                float top = rows < 2 ? 2f * RowSpacing + Size : rows * (RowSpacing + Size);
                for (int i = 0; i < MaxDebuffs; i++) UiKit.Move(_debuffs[i].B.Rt, X(i), Y(i, top));
            }
            for (int i = 0; i < MaxDebuffs; i++) Show(_debuffs[i], i < _bad.Count ? _bad[i] : null);
            if (!_logged && (_good.Count > 0 || _bad.Count > 0))
            {
                _logged = true;
                WowLog.Info("UI: BuffFrame at the top right (BuffFrame.xml TOPRIGHT -205, -13; 30 px, rows of 8, debuffs below) - " + _good.Count + " buff(s), " + _bad.Count + " debuff(s).");
            }
        }

        private static void Show(Slot sl, AuraView a)
        {
            sl.A = a;
            bool show = a != null;
            if (sl.B.gameObject.activeSelf != show) sl.B.gameObject.SetActive(show);
            if (!show) return;
            string path = string.IsNullOrEmpty(a.IconPath) ? (a.SpellId == 0 ? @"Interface\Icons\INV_Misc_Food_11" : null) : a.IconPath;
            if (!ReferenceEquals(path, sl.Path) || !sl.B.Icon.enabled)
            {
                Texture2D t = UiArt.Icon(path);
                if (t != null) { sl.B.Icon.texture = t; sl.B.Icon.color = Color.white; sl.B.Icon.enabled = true; sl.Path = path; }
            }
            int stacks = a.Stacks > 1 ? a.Stacks : 0;
            if (stacks != sl.Stacks) { sl.Stacks = stacks; sl.Count.SetText(stacks > 0 ? stacks.ToString() : ""); }
            int secs = a.Duration > 0f ? Mathf.CeilToInt(a.Remaining) : -1;
            if (secs != sl.Secs)
            {
                sl.Secs = secs;
                // SecondsToTimeAbbrev: "%d h" / "%d m" / "%d s" (GlobalStrings HOUR_ / MINUTE_ / SECOND_ONELETTER_ABBR)
                if (secs < 0) sl.Time.SetText("");
                else if (secs >= 3600) sl.Time.SetText("{0} h", Mathf.CeilToInt(secs / 3600f));
                else if (secs >= 60) sl.Time.SetText("{0} m", Mathf.CeilToInt(secs / 60f));
                else sl.Time.SetText("{0} s", secs);
                sl.Time.color = secs >= 0 && secs < 60 ? Color.white : UiArt.Gold;   // BUFF_DURATION_WARNING_TIME 60
            }
        }
    }
}
