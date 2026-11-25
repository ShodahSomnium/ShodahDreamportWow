using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// The low-health vignette (WowSettings.LowHealthVignette): at 20% health and below the edges of the view turn red and pulse like
    /// a heartbeat, stronger and faster towards 0 (off while dead). The picture is the install's own
    /// Interface\FullScreenTextures\LowHealth.blp (256x256 DXT1, red edges on black, meant to be added): its brightness becomes the
    /// alpha (normalised to the texture's brightest pixel) and its colour is kept, so uGUI's alpha blending shows it.
    /// - VR: a world canvas locked to the head camera (placed in LateUpdate and again just before rendering), Distance in front of
    ///   the eyes, drawn over everything with uGUI's default shader (UiArt.OverUi: ZTest Always, Overlay queue; the default UI shader
    ///   is stereo-instancing aware and transparent, so it needs no depth or DepthNormals) - both eyes see it. The quad covers
    ///   +-CoverH / +-CoverV degrees; the texture spans +-TexH / +-TexV and its clamped edge colour fills the rest, so there is no
    ///   visible border inside any headset's field of view.
    /// - Desktop: a full-screen image on its own overlay canvas under the HUD.
    /// </summary>
    public sealed class UiVignette
    {
        public const string LowHealth = @"Interface\FullScreenTextures\LowHealth.blp";
        public const float Threshold = 0.2f;
        public float Distance = 0.3f;
        public const float MetresPerUnit = 0.001f;
        public const float CoverH = 72f, CoverV = 64f, TexH = 46f, TexV = 42f;

        private GameObject _vrGo, _deskGo;
        private RectTransform _vrRoot;
        private RawImage _vrImg, _deskImg;
        private Texture2D _tex;
        private bool _texTried;
        private float _alpha, _phase;
        private bool _logged;
        private int _state = -1;

        /// <summary>Current strength 0..1 (for the lab / logs).</summary>
        public float Alpha { get { return _alpha; } }

        public void Refresh(Transform parent, PlayerState p, bool vr, float dt)
        {
            float target = 0f;
            float frac = 1f;
            if (WowSettings.LowHealthVignette && p != null && !p.Dead && p.MaxHealth > 0)
            {
                frac = (float)p.Health / p.MaxHealth;
                if (frac <= Threshold)
                {
                    float k = 1f - Mathf.Clamp01(frac / Threshold);   // 0 at 20%, 1 at 0%
                    float strength = 0.25f + 0.45f * k;   // lead, iteration 4: gentler (the lab's 15% view washed the whole screen)
                    float hz = Mathf.Lerp(0.9f, 2.2f, k);
                    float amp = 0.3f + 0.15f * k;
                    _phase += Mathf.Max(0f, dt) * hz * Mathf.PI * 2f;
                    if (_phase > 1000f) _phase -= Mathf.PI * 2f * 150f;
                    float beat = 0.5f + 0.5f * Mathf.Sin(_phase);
                    target = strength * (1f - amp + amp * beat);
                }
            }
            _alpha = Mathf.MoveTowards(_alpha, target, Mathf.Max(0f, dt) * 3f);
            int state = _alpha > 0.001f ? (vr ? 2 : 1) : 0;
            if (state != _state)
            {
                if (state != 0 && (_state <= 0))
                    WowLog.Info("UI: low-health vignette on (" + Mathf.RoundToInt(frac * 100f) + "% health, " + (vr ? "VR head-locked overlay" : "desktop overlay") + (Texture() != null ? "" : ", texture missing") + ").");
                _state = state;
            }
            if (state == 0)
            {
                Hide();
                return;
            }
            if (Texture() == null) return;
            if (vr)
            {
                if (_vrGo == null) BuildVr(parent);
                if (_deskGo != null && _deskGo.activeSelf) _deskGo.SetActive(false);
                if (!_vrGo.activeSelf) _vrGo.SetActive(true);
                _vrImg.color = new Color(1f, 1f, 1f, _alpha);
            }
            else
            {
                if (_deskGo == null) BuildDesktop(parent);
                if (_vrGo != null && _vrGo.activeSelf) _vrGo.SetActive(false);
                if (!_deskGo.activeSelf) _deskGo.SetActive(true);
                _deskImg.color = new Color(1f, 1f, 1f, _alpha);
            }
        }

        /// <summary>VR: lock to the head camera (LateUpdate and Application.onBeforeRender).</summary>
        public void Place(Camera head)
        {
            if (_vrGo == null || !_vrGo.activeSelf || head == null) return;
            Transform t = head.transform;
            _vrRoot.SetPositionAndRotation(t.position + t.forward * Distance, t.rotation);
        }

        public void Hide()
        {
            if (_vrGo != null && _vrGo.activeSelf) _vrGo.SetActive(false);
            if (_deskGo != null && _deskGo.activeSelf) _deskGo.SetActive(false);
        }

        public void Reset()
        {
            _alpha = 0f;
            _state = -1;
            Hide();
        }

        private Texture2D Texture()
        {
            if (_tex != null || _texTried) return _tex;
            WowFiles files = UiArt.Files;
            if (files == null) return null;
            _texTried = true;
            try
            {
                byte[] blp = files.Read(LowHealth);
                if (blp == null) { WowLog.Warn("UI: " + LowHealth + " is not in the install - no low-health vignette."); return null; }
                BlpImage img = BlpImage.Decode(blp);
                byte[] px = img.GetRgba32(0);
                int max = 1;
                for (int i = 0; i < px.Length; i += 4) max = Math.Max(max, Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
                for (int i = 0; i < px.Length; i += 4)
                {
                    int m = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                    if (m == 0) { px[i] = 255; px[i + 1] = 0; px[i + 2] = 0; px[i + 3] = 0; continue; }   // red, so filtering fades to red, not black
                    px[i] = (byte)Math.Min(255, px[i] * 255 / m);
                    px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / m);
                    px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / m);
                    px[i + 3] = (byte)Math.Min(255, m * 255 / max);
                }
                EdgeRing(px, img.Width, img.Height);
                _tex = WowTextures.FromRgba32(img.Width, img.Height, px, WowTextureOptions.Ui, "LowHealth (vignette)", true);
                if (_tex != null) _tex.wrapMode = TextureWrapMode.Clamp;
                if (!_logged)
                {
                    _logged = true;
                    WowLog.Info("UI: low-health vignette texture " + LowHealth + " " + img.Width + "x" + img.Height + " (brightest channel " + max + ").");
                }
            }
            catch (Exception e)
            {
                WowLog.Warn("UI: low-health vignette texture failed: " + e.Message);
                _tex = null;
            }
            return _tex;
        }

        /// <summary>
        /// The texture's outermost pixels vary (radial streaks), so clamping them outward (VR: the quad is larger than the texture)
        /// draws bands at the texture's border (seen in the lab, u4_ui4_vr_hud). The outer 10 % ring is blended (smoothstep) into
        /// the ring's own average colour at full alpha: the clamped colour is then uniform and the border invisible.
        /// </summary>
        private static void EdgeRing(byte[] px, int w, int h)
        {
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y)) > 1) continue;
                    int i = (y * w + x) * 4;
                    if (px[i + 3] == 0) continue;
                    r += px[i]; g += px[i + 1]; b += px[i + 2]; n++;
                }
            byte er = n > 0 ? (byte)(r / n) : (byte)255, eg = n > 0 ? (byte)(g / n) : (byte)0, eb = n > 0 ? (byte)(b / n) : (byte)0;
            float ring = Math.Max(2f, 0.1f * Math.Min(w, h));
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float e = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
                    float t = 1f - Mathf.Clamp01(e / ring);
                    if (t <= 0f) continue;
                    t = t * t * (3f - 2f * t);
                    int i = (y * w + x) * 4;
                    px[i] = (byte)(px[i] + (er - px[i]) * t);
                    px[i + 1] = (byte)(px[i + 1] + (eg - px[i + 1]) * t);
                    px[i + 2] = (byte)(px[i + 2] + (eb - px[i + 2]) * t);
                    px[i + 3] = (byte)(px[i + 3] + (255 - px[i + 3]) * t);
                }
        }

        private void BuildVr(Transform parent)
        {
            _vrGo = new GameObject("WoW Low Health (VR)", typeof(RectTransform));
            _vrGo.layer = UiKit.Layer;
            _vrGo.transform.SetParent(parent, false);
            Canvas c = _vrGo.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.overrideSorting = true;
            c.sortingOrder = 150;   // under the HUD / arm / palm canvases (200+), over the world
            UiSharp.Register(_vrGo);   // lead 2026-10-10
            _vrRoot = (RectTransform)_vrGo.transform;
            _vrRoot.pivot = new Vector2(0.5f, 0.5f);
            float hw = Distance * Mathf.Tan(CoverH * Mathf.Deg2Rad), hh = Distance * Mathf.Tan(CoverV * Mathf.Deg2Rad);
            _vrRoot.sizeDelta = new Vector2(2f * hw / MetresPerUnit, 2f * hh / MetresPerUnit);
            _vrRoot.localScale = Vector3.one * MetresPerUnit;
            _vrImg = Stretched(_vrRoot, "Vignette");
            _vrImg.material = UiArt.OverUi;
            float sx = Mathf.Tan(CoverH * Mathf.Deg2Rad) / Mathf.Tan(TexH * Mathf.Deg2Rad);
            float sy = Mathf.Tan(CoverV * Mathf.Deg2Rad) / Mathf.Tan(TexV * Mathf.Deg2Rad);
            _vrImg.uvRect = new Rect(0.5f - sx * 0.5f, 0.5f - sy * 0.5f, sx, sy);
            _vrGo.SetActive(false);
        }

        private void BuildDesktop(Transform parent)
        {
            _deskGo = new GameObject("WoW Low Health (desktop)", typeof(RectTransform));
            _deskGo.layer = UiKit.Layer;
            _deskGo.transform.SetParent(parent, false);
            Canvas c = _deskGo.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 4;     // the desktop HUD is 5
            _deskImg = Stretched((RectTransform)_deskGo.transform, "Vignette");
            _deskGo.SetActive(false);
        }

        private RawImage Stretched(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiKit.Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            RawImage img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = _tex;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }
    }
}
