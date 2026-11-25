using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// The few pictures the VR input draws that WoW has no file for, made in code once (no game assets are shipped): the two-band
    /// half ring behind the radial menu (dark fill, gold trim like WoW's frame borders, a divider per segment), the same bands in
    /// white for the highlighted wedge (an Image Filled Radial360 cuts one segment out of it), and a soft round glow (the held
    /// spell in the hand, the dot at the end of the target ray). Standard Unity orientation (row 0 = bottom), not BLP data.
    /// </summary>
    internal static class VrArt
    {
        /// <summary>Band radii as fractions of the ring's outer radius: outer band (the buttons), inner band (the key labels).</summary>
        public const float OuterIn = 0.56f, OuterOut = 1f, InnerIn = 0.36f, InnerOut = 0.5f;

        private static readonly Color Fill = new Color(0.03f, 0.03f, 0.05f, 0.74f);
        private static readonly Color FillInner = new Color(0.02f, 0.02f, 0.03f, 0.62f);
        private static readonly Color Trim = new Color(0.86f, 0.68f, 0.26f, 1f);
        private static readonly Color Divider = new Color(0.55f, 0.43f, 0.16f, 0.9f);

        private static Texture2D _halfRings, _bands, _glow;
        private static Sprite _bandsSprite;

        /// <summary>512x256: the top half of both bands, the ring centre at the bottom middle; dividers every 180/segments degrees.</summary>
        public static Texture2D HalfRings(int segments)
        {
            if (_halfRings != null) return _halfRings;
            const int w = 512, h = 256;
            var px = new Color32[w * h];
            float step = 180f / Mathf.Max(1, segments);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / h - 1f, v = (y + 0.5f) / h;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float pr = r * h;   // radius in pixels
                    Color c = new Color(0f, 0f, 0f, 0f);
                    bool outer = r >= OuterIn && r <= OuterOut, inner = r >= InnerIn && r <= InnerOut;
                    if (outer || inner)
                    {
                        c = outer ? Fill : FillInner;
                        float a = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                        float k = Mathf.Round(a / step) * step;
                        float dLine = Mathf.Abs(Mathf.Sin((a - k) * Mathf.Deg2Rad)) * pr;
                        if (dLine < 1.3f) c = Color.Lerp(c, Divider, 1f - dLine / 1.3f);
                        float lo = outer ? OuterIn : InnerIn, hi = outer ? OuterOut : InnerOut;
                        float dEdge = Mathf.Min(pr - lo * h, hi * h - pr);
                        dEdge = Mathf.Min(dEdge, y + 0.5f);   // the flat bottom ends
                        if (dEdge < 2.2f) c = Color.Lerp(c, Trim, Mathf.Clamp01(1.4f - dEdge / 2.2f));
                    }
                    px[y * w + x] = c;
                }
            }
            _halfRings = Make(w, h, px, "WoW VR radial rings");
            return _halfRings;
        }

        /// <summary>A sprite of both bands in white over the whole circle (256x256, centre in the middle) for the wedge highlight.</summary>
        public static Sprite Bands
        {
            get
            {
                if (_bandsSprite != null) return _bandsSprite;
                const int n = 256;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float pr = Mathf.Sqrt(u * u + v * v) * n * 0.5f;
                        float half = n * 0.5f;
                        float a = Mathf.Max(Soft(pr, OuterIn * half, OuterOut * half), Soft(pr, InnerIn * half, InnerOut * half));
                        px[y * n + x] = new Color(1f, 1f, 1f, a);
                    }
                }
                _bands = Make(n, n, px, "WoW VR radial bands");
                _bandsSprite = Sprite.Create(_bands, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
                return _bandsSprite;
            }
        }

        /// <summary>64x64 soft round glow (white, alpha falling off to the rim).</summary>
        public static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int n = 64;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float r = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                        float a = (1f - r) * (1f - r);
                        px[y * n + x] = new Color(1f, 1f, 1f, a);
                    }
                }
                _glow = Make(n, n, px, "WoW VR glow");
                return _glow;
            }
        }

        /// <summary>8x32 beam cross-section for the target ray's LineRenderer (u along the line: constant; v across: soft edges).</summary>
        public static Texture2D Beam
        {
            get
            {
                if (_beam != null) return _beam;
                const int w = 8, h = 32;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    float a = Mathf.Clamp01(1f - v * v * v);
                    for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
                _beam = Make(w, h, px, "WoW VR beam");
                return _beam;
            }
        }

        private static Texture2D _beam, _strikeBand;

        /// <summary>
        /// Iteration 11 (vrcast, proposal-vrcast.md 6.5): 512x512 backdrop of the immersive-strike ring - a full band from 0.80 to 1.00 of the
        /// radius in the radial's dark fill at alpha 0.45 with the gold trim at both edges (the radial menu's colours, ours).
        /// </summary>
        public static Texture2D StrikeBand
        {
            get
            {
                if (_strikeBand != null) return _strikeBand;
                const int n = 512;
                const float inner = 0.80f, outer = 1.00f;
                var px = new Color32[n * n];
                var fill = new Color(Fill.r, Fill.g, Fill.b, 0.45f);
                float half = n * 0.5f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / half - 1f, v = (y + 0.5f) / half - 1f;
                        float pr = Mathf.Sqrt(u * u + v * v) * half;   // radius in pixels
                        Color c = new Color(0f, 0f, 0f, 0f);
                        if (pr >= inner * half - 1f && pr <= outer * half)
                        {
                            float dEdge = Mathf.Min(pr - inner * half, outer * half - pr);
                            c = Color.Lerp(fill, Trim, Mathf.Clamp01(1.4f - dEdge / 2.2f));
                            c.a *= Mathf.Clamp01(dEdge + 1f);   // soft outer pixel
                        }
                        px[y * n + x] = c;
                    }
                }
                _strikeBand = Make(n, n, px, "WoW VR strike band");
                return _strikeBand;
            }
        }

        /// <summary>
        /// A glow colour for a spell from its icon's file name - WoW names its spell icons by school (Interface\Icons\Spell_Fire_*,
        /// Spell_Frost_*, Spell_Holy_*, Spell_Nature_*, Spell_Shadow_*, Spell_Arcane_*); anything else by harmful / helpful.
        /// </summary>
        public static Color SpellColor(string iconPath, bool harmful)
        {
            string p = iconPath ?? "";
            if (Has(p, "Spell_Fire")) return new Color(1f, 0.45f, 0.1f);
            if (Has(p, "Spell_Frost")) return new Color(0.45f, 0.75f, 1f);
            if (Has(p, "Spell_Holy")) return new Color(1f, 0.9f, 0.5f);
            if (Has(p, "Spell_Nature")) return new Color(0.45f, 1f, 0.4f);
            if (Has(p, "Spell_Shadow")) return new Color(0.7f, 0.3f, 1f);
            if (Has(p, "Spell_Arcane")) return new Color(0.95f, 0.5f, 1f);
            return harmful ? new Color(1f, 0.55f, 0.2f) : new Color(1f, 0.95f, 0.6f);
        }

        private static bool Has(string path, string part)
        {
            return path.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float Soft(float pr, float lo, float hi)
        {
            return Mathf.Clamp01(Mathf.Min(pr - lo, hi - pr) + 0.5f);
        }

        private static Texture2D Make(int w, int h, Color32[] px, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.name = name;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
