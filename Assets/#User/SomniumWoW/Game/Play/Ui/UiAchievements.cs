// iteration 11 - owner character (proposal-talents.md 6.1 / hook 11): the achievement toast (UiAchievementAlert, built by uicore's
// UiDesktopLayout / UiVrLayout on the HUD) and the AchievementFrame widgets (achievement buttons, category buttons, progress bars).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// AchievementAlertFrameTemplate (AlertFrames.xml:195-410): 300x88 at BOTTOM (0, 128) of the HUD, UI-Achievement-Alert-Background
    /// (0, .605, 0, .703), "Achievement Earned" (ACHIEVEMENT_UNLOCKED, GlobalStrings.lua:30) at TOP (7, -23), the name between x 72 and -60
    /// (36 above the bottom), the icon (50x50 + the 72x72 UI-Achievement-IconFrame) on the left, the points shield 64x64 at TOPRIGHT (-10, -13),
    /// the ADD glow 400x171 flashing in. Alpha: in 0.2 s, held 4.05 s, out 1.5 s (animIn / waitAndAnimOut). The client plays AchievementSound
    /// (SoundEntries, C code - the sound is ours to call); VR: a short pulse on both hands (ours, proposal D5). One toast at a time, the rest wait.
    /// </summary>
    public sealed class UiAchievementAlert
    {
        const float FadeIn = 0.2f, Hold = 4.05f, FadeOut = 1.5f;
        readonly RectTransform _root;
        readonly CanvasGroup _group;
        readonly RawImage _icon, _glow, _shield;
        readonly TextMeshProUGUI _name, _points;
        readonly Queue<GameEvent> _queue = new Queue<GameEvent>();
        readonly UiContext _ctx;
        float _start = -1f;
        int _iconFor;
        static bool _logged;

        public UiAchievementAlert(Transform hudRoot, UiContext ctx)
        {
            _ctx = ctx;
            if (hudRoot == null) return;
            var go = new GameObject("AchievementAlertFrame", typeof(RectTransform));
            go.layer = UiKit.Layer;
            _root = (RectTransform)go.transform;
            _root.SetParent(hudRoot, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);   // BOTTOM (0, 128)
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 128f);
            _root.sizeDelta = new Vector2(300f, 88f);
            _group = go.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            RectTransform c = UiKit.Rect(_root, "Content", 0f, 0f, 300f, 88f);
            _glow = UiKit.Pic(c, "Glow", UiArt.Add(@"Interface\AchievementFrame\UI-Achievement-Alert-Glow.blp"), 0f, 0.78125f, 0f, 0.66796875f, -50f, -41.5f, 400f, 171f);
            UiKit.Pic(c, "Background", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-Alert-Background.blp"), 0f, 0.605f, 0f, 0.703f, 0f, 0f, 300f, 88f);
            TextMeshProUGUI unlocked = UiKit.Text(c, "Unlocked", UiGlobalStrings.G("ACHIEVEMENT_UNLOCKED", "Achievement Earned"), 9f, Color.black, TextAlignmentOptions.Center, 57f, 23f, 200f, 12f, null, false);
            unlocked.textWrappingMode = TextWrappingModes.NoWrap;
            _name = UiKit.Text(c, "Name", "", 12f, Color.white, TextAlignmentOptions.Center, 72f, 36f, 168f, 16f);
            _name.textWrappingMode = TextWrappingModes.NoWrap;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            _icon = UiKit.Pic(c, "IconTexture", null, 11f, 18f, 50f, 50f);
            UiKit.Pic(c, "IconOverlay", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-IconFrame.blp"), 0f, 0.5625f, 0f, 0.5625f, -1f, 8f, 72f, 72f);
            _shield = UiKit.Pic(c, "ShieldIcon", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-Shields.blp"), 0f, 0.5f, 0f, 0.9f, 239f, 19f, 52f, 48f);
            _points = UiKit.Text(c, "ShieldPoints", "", 12f, UiArt.Gold, TextAlignmentOptions.Center, 239f, 30f, 52f, 18f);
            go.AddComponent<UiAchievementAlertDriver>().Alert = this;
            go.SetActive(false);
        }

        /// <summary>Called by the layout's event fan-out for every event; acts on GameEventType.AchievementEarned.</summary>
        public void OnEvent(GameEvent e)
        {
            if (e.Type != GameEventType.AchievementEarned || _root == null) return;
            if (_queue.Count < 6) _queue.Enqueue(e);
            if (!_logged) { _logged = true; WowLog.Info("UI: achievement toast shown (AchievementAlertFrame, AchievementSound" + (_ctx != null && _ctx.Vr ? ", haptic pulse - ours" : "") + ")"); }
            if (_start < 0f) Next();
        }

        void Next()
        {
            if (_queue.Count == 0) { _start = -1f; _root.gameObject.SetActive(false); return; }
            GameEvent e = _queue.Dequeue();
            AchievementDb db = UiArt.Files != null ? AchievementDb.Get(UiArt.Files) : null;
            AchievementInfo a = db != null ? db.Find(e.Amount) : null;
            _name.text = !string.IsNullOrEmpty(e.Text) ? e.Text : a != null ? a.Name : "";
            int pts = a != null ? a.Points : 0;
            _points.text = pts > 0 ? pts.ToString() : "";
            _shield.texture = UiArt.Tex(pts > 0 ? @"Interface\AchievementFrame\UI-Achievement-Shields.blp" : @"Interface\AchievementFrame\UI-Achievement-Shields-NoPoints.blp");
            _iconFor = e.Amount;
            _icon.texture = null;
            _start = Time.unscaledTime;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            UiSound.Named("AchievementSound");
            if (_ctx != null && _ctx.Vr)
            {
                try { GearXr.Pulse(true, 0.45f, 0.12f); GearXr.Pulse(false, 0.45f, 0.12f); }
                catch (Exception ex) { WowLog.Warn("UI: achievement haptics failed: " + ex.Message); }
            }
        }

        internal void Tick()
        {
            if (_start < 0f) return;
            float t = Time.unscaledTime - _start;
            if (_icon.texture == null)
            {
                AchievementDb db = UiArt.Files != null ? AchievementDb.Get(UiArt.Files) : null;
                AchievementInfo a = db != null ? db.Find(_iconFor) : null;
                Texture2D tex = UiArt.Icon(a != null ? a.IconPath : null);
                if (tex != null) { _icon.texture = tex; _icon.color = Color.white; }
            }
            float alpha = t < FadeIn ? t / FadeIn : t < FadeIn + Hold ? 1f : 1f - (t - FadeIn - Hold) / FadeOut;
            _group.alpha = Mathf.Clamp01(alpha);
            // the glow: alpha 0 -> 1 in 0.2 s, back to 0 in 0.5 s (AlertFrames.xml:262-263)
            _glow.color = new Color(1f, 1f, 1f, t < 0.2f ? t / 0.2f : Mathf.Clamp01(1f - (t - 0.2f) / 0.5f));
            if (t >= FadeIn + Hold + FadeOut) Next();
        }
    }

    /// <summary>Drives the toast's fade (the toast object itself is not a MonoBehaviour).</summary>
    public sealed class UiAchievementAlertDriver : MonoBehaviour
    {
        internal UiAchievementAlert Alert;
        void Update() { if (Alert != null) Alert.Tick(); }
    }

    /// <summary>AchievementProgressBarTemplate (Blizzard_AchievementUI.xml:512-600): 212x14, black .4 background, UI-StatusBar in (0, .6, 0),
    /// the UI-Achievement-ProgressBar-Border pieces 6 outside left / right and 5 above / below, the "x/y" text (GameFontHighlightSmall).</summary>
    internal sealed class UiAchBar
    {
        public readonly RectTransform Root;
        readonly UiBar _bar;
        readonly TextMeshProUGUI _text;

        public UiAchBar(Transform parent, string name, float x, float y, float w)
        {
            Root = UiKit.Rect(parent, name, x, y, w, 14f);
            UiKit.Solid(Root, "BG", new Color(0f, 0f, 0f, 0.4f), 0f, 0f, w, 14f);
            _bar = UiBar.Make(Root, "Bar", 0f, 0f, w, 14f, new Color(0f, 0.6f, 0f), false);
            Texture2D border = UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-ProgressBar-Border.blp");
            UiKit.Pic(Root, "BorderLeft", border, 0f, 0.0625f, 0f, 0.75f, -6f, -5f, 16f, 24f);
            UiKit.Pic(Root, "BorderCenter", border, 0.0625f, 0.812f, 0f, 0.75f, 10f, -5f, Mathf.Max(1f, w - 8f), 24f);
            UiKit.Pic(Root, "BorderRight", border, 0.812f, 0.8745f, 0f, 0.75f, w - 10f, -5f, 16f, 24f);
            _text = UiKit.Text(Root, "Text", "", 10f, Color.white, TextAlignmentOptions.Center, 0f, 0f, w, 14f);
            _text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        public void Set(long value, long max, string text = null)
        {
            if (max <= 0) max = 1;
            _bar.Set(Mathf.Clamp01(value / (float)max));
            string s = text ?? (value + "/" + max);
            if (_text.text != s) _text.text = s;
        }
    }

    /// <summary>AchievementCategoryTemplate (Blizzard_AchievementUI.xml:672-700): 24 tall, top categories 165 wide (CATEGORIESWIDTH - 10,
    /// GameFontNormal), children 150 (CATEGORIESWIDTH - 25, GameFontHighlight, background at .6); UI-Achievement-Category-Background
    /// (0, .6640625, 0, 1), the label 16 in from the left, the ADD highlight UI-Achievement-Category-Highlight.</summary>
    internal sealed class UiAchCategoryButton
    {
        public readonly UiButton B;
        readonly RawImage _bg, _sel;
        readonly TextMeshProUGUI _label;
        public int CategoryId;   // 0 = Summary

        public UiAchCategoryButton(Transform parent, int i)
        {
            B = UiKit.Button(parent, "AchievementFrameCategoriesContainerButton" + (i + 1), 0f, 0f, 165f, 24f);
            _bg = UiKit.Pic(B.transform, "Background", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-Category-Background.blp"), 0f, 0.6640625f, 0f, 1f, 0f, -4f, 165f, 32f);
            _sel = UiKit.Pic(B.transform, "Selected", UiArt.Add(@"Interface\AchievementFrame\UI-Achievement-Category-Highlight.blp"), 0f, 0.6640625f, 0f, 1f, 0f, -4f, 165f, 32f);
            _sel.enabled = false;
            B.Hilite = UiKit.Pic(B.transform, "Highlight", UiArt.Add(@"Interface\AchievementFrame\UI-Achievement-Category-Highlight.blp"), 0f, 0.6640625f, 0f, 1f, 0f, -4f, 165f, 32f);
            B.Hilite.enabled = false;
            _label = UiKit.Text(B.transform, "Label", "", 12f, UiArt.Gold, TextAlignmentOptions.BottomLeft, 16f, 4f, 165f - 24f, 16f);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.overflowMode = TextOverflowModes.Ellipsis;
        }

        public void Show(int categoryId, string name, bool child, bool selected, float x, float y)
        {
            CategoryId = categoryId;
            if (!B.gameObject.activeSelf) B.gameObject.SetActive(true);
            float w = child ? 150f : 165f;
            UiKit.Move(B.Rt, x + (child ? 175f - 10f - 150f : 0f), y);
            UiKit.Size(B.Rt, w, 24f);
            UiKit.Size(_bg.rectTransform, w, 32f);
            UiKit.Size(_sel.rectTransform, w, 32f);
            UiKit.Size(B.Hilite.rectTransform, w, 32f);
            UiKit.Size(_label.rectTransform, w - 24f, 16f);
            _bg.color = child ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
            _label.color = child ? Color.white : UiArt.Gold;
            _label.fontSize = 12f;
            if (_label.text != name) _label.text = name ?? "";
            _sel.enabled = selected && _sel.texture != null;
        }

        public void Hide() { if (B.gameObject.activeSelf) B.gameObject.SetActive(false); }
    }

    /// <summary>
    /// AchievementTemplate (Blizzard_AchievementUI.xml:839-1260) at 496 wide (AchievementFrameAchievements 504, .lua:611-613): parchment
    /// background (UI-Achievement-Parchment-Horizontal, "-Desaturated" while not earned), the title bar UI-Achievement-Title 24 tall at (5, -5)
    /// (texcoords 0-.3125 earned / .34375-.65625 not), the name (GameFontHighlightMedium) centered on it, the description 30 down
    /// (black on parchment when earned, white when not), the 60x60 icon at (8, -9) (50x50 icon + the 72x72 UI-Achievement-IconFrame, tinted .55
    /// when not earned), the points shield 64x64 at TOPRIGHT (-6, 0) (UI-Achievement-Shields 0-.5 earned / .5-1 not, -NoPoints for 0), the
    /// date under it (SHORTDATE), a progress bar for progress-bar achievements; a click expands the criteria (15 per line, 2 columns).
    /// Collapsed 84 tall (ACHIEVEMENTBUTTON_COLLAPSEDHEIGHT), at most 232 (MAXHEIGHT, .lua:812-817).
    /// </summary>
    internal sealed class UiAchButton
    {
        public const float W = 496f, CollapsedH = 84f, MaxH = 232f, CriteriaLine = 15f;
        public readonly UiButton B;
        readonly RawImage _bg, _title, _icon, _iconFrame, _shield;
        readonly TextMeshProUGUI _label, _desc, _date, _points, _criteria, _reward;
        readonly UiAchBar _bar;
        public AchievementInfo A;
        public float Height = CollapsedH;
        int _iconLoadedFor = -1;
        /// <summary>The icon is still loading (UiArt.Icon null on a pool thread) - the window re-shows only these buttons.</summary>
        public bool IconPending { get { return A != null && _iconLoadedFor != A.Id; } }

        public UiAchButton(Transform parent, int i)
        {
            B = UiKit.Button(parent, "AchievementFrameAchievementsContainerButton" + (i + 1), 0f, 0f, W, CollapsedH);
            _bg = UiKit.Pic(B.transform, "Background", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-Parchment-Horizontal.blp"), 3f, 3f, W - 6f, CollapsedH - 6f);
            _title = UiKit.Pic(B.transform, "TitleBackground", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-Title.blp"), 0f, 0.9765625f, 0f, 0.3125f, 5f, 5f, W - 10f, 24f);
            _label = UiKit.Text(B.transform, "Label", "", 14f, Color.white, TextAlignmentOptions.Center, (W - 320f) * 0.5f, 7f, 320f, 20f);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.overflowMode = TextOverflowModes.Ellipsis;
            _desc = UiKit.Text(B.transform, "Description", "", 11f, Color.black, TextAlignmentOptions.Top, (W - 330f) * 0.5f, 30f, 330f, 40f, null, false);
            _icon = UiKit.Pic(B.transform, "IconTexture", null, 13f, 11f, 50f, 50f);
            _iconFrame = UiKit.Pic(B.transform, "IconOverlay", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-IconFrame.blp"), 0f, 0.5625f, 0f, 0.5625f, 1f, 1f, 72f, 72f);
            _shield = UiKit.Pic(B.transform, "ShieldIcon", UiArt.Tex(@"Interface\AchievementFrame\UI-Achievement-Shields.blp"), 0f, 0.5f, 0f, 1f, W - 6f - 66f, 6f, 66f, 64f);
            _points = UiKit.Text(B.transform, "ShieldPoints", "", 14f, Color.white, TextAlignmentOptions.Center, W - 6f - 66f + 1f, 20f, 64f, 20f);
            _points.textWrappingMode = TextWrappingModes.NoWrap;
            _date = UiKit.Text(B.transform, "DateCompleted", "", 10f, Color.white, TextAlignmentOptions.Center, W - 6f - 66f - 17f, 66f, 100f, 14f);
            _date.textWrappingMode = TextWrappingModes.NoWrap;
            _bar = new UiAchBar(B.transform, "ProgressBar", (W - 212f) * 0.5f, 58f, 212f);
            _criteria = UiKit.Text(B.transform, "Criteria", "", 10f, Color.white, TextAlignmentOptions.TopLeft, 80f, 72f, W - 160f, MaxH - 84f);
            _criteria.overflowMode = TextOverflowModes.Truncate;
            _reward = UiKit.Text(B.transform, "Reward", "", 10f, UiArt.Gold, TextAlignmentOptions.Center, 20f, CollapsedH - 20f, W - 40f, 16f);
            _reward.textWrappingMode = TextWrappingModes.NoWrap;
            B.Hilite = UiKit.Pic(B.transform, "Highlight", UiArt.Add(@"Interface\PaperDollInfoFrame\UI-Character-ReputationBar-Highlight.blp"), 0f, 1f, 0f, 0.4375f, 0f, 0f, W, CollapsedH);
            B.Hilite.color = new Color(1f, 1f, 1f, 0.35f);
            B.Hilite.enabled = false;
        }

        /// <summary>Lays the button out for achievement a at y; returns its height. expanded = the criteria lines are shown.</summary>
        public float Show(AchievementInfo a, AchievementsView v, AchievementDb db, bool expanded, float y)
        {
            A = a;
            if (!B.gameObject.activeSelf) B.gameObject.SetActive(true);
            long when = 0;
            bool done = v != null && v.Completed.TryGetValue(a.Id, out when);
            if (!done) when = 0;
            // AchievementButton_Saturate / _Desaturate (.lua:876-905)
            _bg.texture = UiArt.Tex(done ? @"Interface\AchievementFrame\UI-Achievement-Parchment-Horizontal.blp" : @"Interface\AchievementFrame\UI-Achievement-Parchment-Horizontal-Desaturated.blp");
            if (done) UiKit.Coords(_title, 0f, 0.9765625f, 0f, 0.3125f); else UiKit.Coords(_title, 0f, 0.9765625f, 0.34375f, 0.65625f);
            _label.color = done ? Color.white : new Color(0.65f, 0.65f, 0.65f);
            _desc.color = done ? Color.black : Color.white;
            _iconFrame.color = done ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            _icon.color = done ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            if (_label.text != a.Name) _label.text = a.Name ?? "";
            if (_desc.text != a.Description) _desc.text = a.Description ?? "";
            if (_iconLoadedFor != a.Id)
            {
                Texture2D t = UiArt.Icon(a.IconPath);
                _icon.texture = t;
                if (t != null) _iconLoadedFor = a.Id;
            }
            _shield.texture = UiArt.Tex(a.Points > 0 ? @"Interface\AchievementFrame\UI-Achievement-Shields.blp" : @"Interface\AchievementFrame\UI-Achievement-Shields-NoPoints.blp");
            if (done) UiKit.Coords(_shield, 0f, 0.5f, 0f, 1f); else UiKit.Coords(_shield, 0.5f, 1f, 0f, 1f);
            _points.text = a.Points > 0 ? a.Points.ToString() : "";
            _points.color = done ? Color.white : new Color(0.65f, 0.65f, 0.65f);
            _date.text = done ? AchievementDb.ShortDate(when) : "";
            // a progress bar: ACHIEVEMENT_FLAGS_HAS_PROGRESS_BAR or a single progress-bar criterion (Blizzard_AchievementUI.lua:1335 / 1496)
            AchievementCriteriaInfo barCrit = null;
            if (!done && (a.HasProgressBar || (a.Criteria.Count == 1 && a.Criteria[0].ProgressBar)) && a.Criteria.Count > 0) barCrit = a.Criteria[0];
            bool bar = barCrit != null;
            if (_bar.Root.gameObject.activeSelf != bar) _bar.Root.gameObject.SetActive(bar);
            if (bar)
            {
                long have = 0;
                if (v != null) v.Criteria.TryGetValue(barCrit.Id, out have);
                long need = Math.Max(1, a.HasProgressBar && a.MinCriteria > 0 ? a.MinCriteria : barCrit.Quantity);
                _bar.Set(Math.Min(have, need), need);
            }
            float h = CollapsedH;
            string reward = a.Reward ?? "";
            if (expanded && a.Criteria.Count > 0 && !bar)
            {
                string text = CriteriaText(a, v, db, done, out int lines);
                _criteria.text = text;
                _criteria.gameObject.SetActive(true);
                h = Mathf.Min(MaxH, CollapsedH - 12f + lines * CriteriaLine + (reward.Length > 0 ? 20f : 4f));
                UiKit.Size(_criteria.rectTransform, W - 160f, h - 72f);
            }
            else _criteria.gameObject.SetActive(false);
            _reward.text = reward;
            UiKit.Move(_reward.rectTransform, 20f, h - 20f);
            UiKit.Move(B.Rt, 0f, y);
            UiKit.Size(B.Rt, W, h);
            UiKit.Size(_bg.rectTransform, W - 6f, h - 6f);
            UiKit.Size(B.Hilite.rectTransform, W, h);
            Height = h;
            return h;
        }

        /// <summary>The criteria lines (AchievementObjectives): met ones in green with the check, the rest grey; meta criteria name the
        /// achievement they need; two columns when there are more than 5 (ours: one text block, 15 units a line).</summary>
        static string CriteriaText(AchievementInfo a, AchievementsView v, AchievementDb db, bool achDone, out int lines)
        {
            var sb = new System.Text.StringBuilder(256);
            int shown = 0;
            List<AchievementCriteriaInfo> list = a.Criteria;
            bool two = list.Count > 5;
            for (int i = 0; i < list.Count; i++)
            {
                AchievementCriteriaInfo c = list[i];
                if (c.Hidden) continue;
                bool met = achDone;
                string text = c.Description;
                if (c.Type == AchievementDb.CriteriaTypeCompleteAchievement)
                {
                    met = met || (v != null && v.Completed.ContainsKey(c.Asset));
                    if (string.IsNullOrEmpty(text) && db != null) text = db.Name(c.Asset);
                }
                else if (!met && v != null)
                {
                    long have;
                    met = v.Criteria.TryGetValue(c.Id, out have) && have >= Math.Max(1, c.Quantity);
                    if (!met && c.Quantity > 1) text = (text ?? "") + " (" + (v.Criteria.TryGetValue(c.Id, out have) ? have : 0) + "/" + c.Quantity + ")";
                }
                if (string.IsNullOrEmpty(text)) continue;
                if (two && shown % 2 == 1) sb.Append("<pos=50%>"); else if (shown > 0) sb.Append('\n');
                sb.Append(met ? "<color=#00ff00>- " : "<color=#a0a0a0>- ").Append(text).Append("</color>");
                shown++;
            }
            lines = two ? (shown + 1) / 2 : shown;
            return sb.ToString();
        }

        public void Hide() { A = null; if (B.gameObject.activeSelf) B.gameObject.SetActive(false); }
    }
}
