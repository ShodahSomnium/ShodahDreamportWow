// iteration 11 - owner spellfx: the Death Knight rune frame (proposal-spellfx.md s4 / s6.5) - RuneFrame.xml 85-160 / RuneFrame.lua, read from
// the player's own FrameXML: frame 130 x 18 anchored TOP to PlayerFrame BOTTOM +54 / +34; six 18 x 18 buttons left to right, 3 px apart, ids
// 1, 2, 5, 6, 3, 4 (RuneFrame_FixRunes) = server runes 0, 1, 4, 5, 2, 3 (blood, blood, frost, frost, unholy, unholy); per button the rune icon
// 24 x 24 (UI-PlayerFrame-Deathknight-Blood / -Unholy / -Frost / -Death by its type, RuneFrame.lua:8-12), the ring 24 x 24 colour .6, a cooldown
// sweep 15 x 15 and the shine (ComboPoint 60 x 35, texcoords .5625-1, ADD) fading in 0.5 s and out 0.5 s (RuneButton_ShineFadeIn / Out): white when
// a rune is ready again, runeColors[type] when it changes type (RuneFrame.lua:21-26). uicore's UiHud / UiVrHud call the Attach methods (hook 12);
// the frame shows only while Player.Runes != null (death knights online).
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public static class UiRunes
    {
        internal const string Ring = @"Interface\PlayerFrame\UI-PlayerFrame-Deathknight-Ring.blp";
        internal static readonly string[] Icons =
        {
            @"Interface\PlayerFrame\UI-PlayerFrame-Deathknight-Blood.blp",    // RUNETYPE_BLOOD (server 0)
            @"Interface\PlayerFrame\UI-PlayerFrame-Deathknight-Unholy.blp",   // RUNETYPE_UNHOLY (server 1)
            @"Interface\PlayerFrame\UI-PlayerFrame-Deathknight-Frost.blp",    // RUNETYPE_FROST (server 2)
            @"Interface\PlayerFrame\UI-PlayerFrame-Deathknight-Death.blp",    // RUNETYPE_DEATH (server 3)
        };
        internal static readonly Color[] Colors =
        {
            new Color(1f, 0f, 0f), new Color(0f, 0.5f, 0f), new Color(0f, 1f, 1f), new Color(0.8f, 0.1f, 1f),
        };
        /// <summary>The server rune shown at each position (RuneFrame.xml button ids 1, 2, 5, 6, 3, 4 minus 1).</summary>
        internal static readonly int[] Order = { 0, 1, 4, 5, 2, 3 };

        public static void AttachDesktop(RectTransform playerFrame) { Attach(playerFrame, false); }
        public static void AttachVr(RectTransform hudPlayerFrame) { Attach(hudPlayerFrame, true); }

        static void Attach(RectTransform pf, bool vr)
        {
            if (pf == null || pf.Find("Rune Frame") != null) return;   // once per player frame
            try
            {
                float w = pf.rect.width > 1f ? pf.rect.width : 232f, h = pf.rect.height > 1f ? pf.rect.height : 100f;
                // TOP to the player frame's BOTTOM, offset (54, 34) - WoW's y grows upwards, UiKit's downwards.
                RectTransform root = UiKit.Rect(pf, "Rune Frame", w * 0.5f + 54f - 65f, h - 34f, 130f, 18f);
                root.gameObject.AddComponent<UiRuneFrame>().Build(root, vr);
                WowLog.Info("UI: Rune frame attached to the " + (vr ? "VR HUD" : "desktop") + " player frame (RuneFrame.xml: TOP to PlayerFrame BOTTOM +54 / +34, 130x18).");
            }
            catch (Exception e) { WowLog.Warn("UI: Rune frame not built: " + e.Message); }
        }
    }

    /// <summary>The six rune buttons, refreshed every frame from WowPlay.Session.Player.Runes.</summary>
    public sealed class UiRuneFrame : MonoBehaviour
    {
        const float Fade = 0.5f;          // RuneButton_ShineFadeIn timeToFade / UIFrameFadeOut(self, 0.5)

        sealed class Btn
        {
            public RawImage Rune, RingImg, Shine;
            public Image Sweep;
            public int Type = -1;
            public bool Ready = true, Known;
            public float ShineAt = -10f;
        }

        GameObject _content;
        readonly Btn[] _b = new Btn[6];
        bool _shownLogged;

        public void Build(RectTransform root, bool vr)
        {
            _content = UiKit.Rect(root, "Runes", 0f, 0f, 130f, 18f).gameObject;
            for (int i = 0; i < 6; i++)
            {
                var b = new Btn();
                RectTransform r = UiKit.Rect(_content.transform, "Rune " + (i + 1), i * 21f, 0f, 18f, 18f);
                // ARTWORK: the rune 24 x 24 at CENTER (0, -1)
                b.Rune = UiKit.Pic(r, "Rune", UiArt.Tex(UiRunes.Icons[UiRunes.Order[i] / 2]), -3f, -2f, 24f, 24f);
                // the cooldown 15 x 15 at CENTER (0, -1), drawEdge
                RectTransform sr = UiKit.Rect(r, "Cooldown", 1.5f, 2.5f, 15f, 15f);
                b.Sweep = sr.gameObject.AddComponent<Image>();
                b.Sweep.sprite = UiArt.WhiteSprite;
                b.Sweep.type = Image.Type.Filled;
                b.Sweep.fillMethod = Image.FillMethod.Radial360;
                b.Sweep.fillOrigin = (int)Image.Origin360.Top;
                b.Sweep.fillClockwise = false;
                b.Sweep.color = new Color(0f, 0f, 0f, 0.7f);
                b.Sweep.raycastTarget = false;
                if (UiKit.Over) b.Sweep.material = UiArt.OverUi;
                b.Sweep.enabled = false;
                // the border frame: the ring 24 x 24 at CENTER (0, -1), colour .6 .6 .6
                b.RingImg = UiKit.Pic(r, "Ring", UiArt.Tex(UiRunes.Ring), -3f, -2f, 24f, 24f);
                if (b.RingImg.texture != null) b.RingImg.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                // the shine: ComboPoint 60 x 35 at CENTER, texcoords (0.5625, 1, 0, 1), ADD, hidden
                b.Shine = UiKit.Pic(r, "Shine", UiArt.Add(UiArt.ComboPoint), 0.5625f, 1f, 0f, 1f, -21f, -8.5f, 60f, 35f);
                b.Shine.enabled = false;
                _b[i] = b;
            }
            _content.SetActive(false);
        }

        void Update()
        {
            var s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            RuneView[] runes = p != null ? p.Runes : null;
            bool show = runes != null && runes.Length >= 6;
            if (_content == null) return;
            if (_content.activeSelf != show) _content.SetActive(show);
            if (!show) { for (int i = 0; i < 6; i++) if (_b[i] != null) _b[i].Known = false; return; }
            if (!_shownLogged) { _shownLogged = true; WowLog.Info("UI: Rune frame shown (6 runes, order blood blood frost frost unholy unholy as RuneFrame.xml)."); }
            float now = Time.unscaledTime;
            for (int i = 0; i < 6; i++)
            {
                var b = _b[i];
                var v = runes[UiRunes.Order[i]];
                if (b == null || v == null) continue;
                int type = v.Type >= 0 && v.Type <= 3 ? v.Type : 0;
                if (type != b.Type)
                {
                    var tex = UiArt.Tex(UiRunes.Icons[type]);
                    if (tex != null) { b.Rune.texture = tex; b.Rune.color = Color.white; }
                    if (b.Known) { b.ShineAt = now; b.Shine.color = UiRunes.Colors[type]; }   // RuneButton_Update: runeColors[runeType]
                    b.Type = type;
                }
                if (b.Known && v.Ready && !b.Ready) { b.ShineAt = now; b.Shine.color = Color.white; }   // RUNE_POWER_UPDATE usable: white
                b.Ready = v.Ready;
                b.Known = true;
                float frac = !v.Ready && v.Cooldown > 0f ? Mathf.Clamp01(v.Remaining / v.Cooldown) : 0f;
                b.Sweep.enabled = frac > 0f;
                if (frac > 0f) b.Sweep.fillAmount = frac;
                float t = now - b.ShineAt;
                if (t >= 0f && t < 2f * Fade && b.Shine.texture != null)
                {
                    float a = t < Fade ? t / Fade : 1f - (t - Fade) / Fade;
                    var c = b.Shine.color;
                    c.a = Mathf.Clamp01(a);
                    b.Shine.color = c;
                    b.Shine.enabled = true;
                }
                else if (b.Shine.enabled) b.Shine.enabled = false;
            }
        }
    }
}
