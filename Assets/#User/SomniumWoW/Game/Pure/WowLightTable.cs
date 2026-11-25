using System;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>An sRGB colour 0..1 (pure, so the harness can run the light sampling).</summary>
    public struct WowRgb
    {
        public float R, G, B;

        public WowRgb(float r, float g, float b) { R = r; G = g; B = b; }

        /// <summary>LightIntBand colours are 0xRRGGBB (noggit3 Sky.cpp: red = (c &amp; 0xff0000) &gt;&gt; 16; measured: Elwynn noon band 3 = 3AA2CF, sky blue).</summary>
        public static WowRgb FromInt(int c)
        {
            return new WowRgb(((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f);
        }

        public static WowRgb Lerp(WowRgb a, WowRgb b, float t)
        {
            return new WowRgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        public string Hex { get { return ((int)Math.Round(R * 255)).ToString("X2") + ((int)Math.Round(G * 255)).ToString("X2") + ((int)Math.Round(B * 255)).ToString("X2"); } }
    }

    /// <summary>The zone's light at a place and time: 18 colour bands and 6 float bands.</summary>
    public sealed class WowLightSample
    {
        public readonly WowRgb[] Colors = new WowRgb[18];
        public readonly float[] Floats = new float[6];
        public int LightId = -1, ParamsId = -1, BaseLightId = -1;
        public float Weight; // 1 = fully the local light, 0 = fully the base (global / nearest) light
        public string Source = "";

        // Band meanings (wowdev DB/LightIntBand, noggit3 Sky.h SkyColorNames).
        public const int Diffuse = 0, Ambient = 1, SkyTop = 2, SkyMiddle = 3, SkyBand1 = 4, SkyBand2 = 5, SkyHorizon = 6, Fog = 7, Sun = 9, SunHalo = 10, Cloud = 12,
            OceanLight = 14, OceanDark = 15, RiverLight = 16, RiverDark = 17;

        /// <summary>LightFloatBand 0: fog distance in yards x 36 (wowdev DB/LightFloatBand).</summary>
        public float FogEndYards { get { return Floats[0] / 36f; } }

        /// <summary>LightFloatBand 1: fog start = end x multiplier.</summary>
        public float FogMultiplier { get { return Floats[1]; } }

        public static WowLightSample Lerp(WowLightSample a, WowLightSample b, float t)
        {
            var s = new WowLightSample();
            LerpInto(a, b, t, s);
            return s;
        }

        /// <summary>dst = lerp(a, b, t) for colours and floats (no allocation; dst may be a or b).</summary>
        public static void LerpInto(WowLightSample a, WowLightSample b, float t, WowLightSample dst)
        {
            for (int i = 0; i < 18; i++) dst.Colors[i] = WowRgb.Lerp(a.Colors[i], b.Colors[i], t);
            for (int i = 0; i < 6; i++) dst.Floats[i] = a.Floats[i] + (b.Floats[i] - a.Floats[i]) * t;
            dst.LightId = b.LightId;
            dst.ParamsId = b.ParamsId;
            dst.BaseLightId = b.BaseLightId;
            dst.Weight = b.Weight;
            dst.Source = b.Source;
        }
    }

    /// <summary>
    /// Light.dbc -> LightParams -> LightIntBand / LightFloatBand for WoW 3.3.5a, measured and documented in reports/front.md:
    /// position X = 17066.666 - c[2]/36, Y = 17066.666 - c[0]/36, Z = c[1]/36, radii /36 (wowdev DB/Light; probe FrontLight:
    /// 708 of 1010 taxi/graveyard points fall inside a local light with this rule, 322 with the swapped one); the map's
    /// global light is the row with coordinates 0 and radius 0 (Northrend has none: then light id 1 - wowdev DB/Light: "If there is no (0,0,0) entry for a continent, use m_ID == 1 as fallback"); params
    /// id p -> int bands p*18-17+k, float bands p*6-5+k; time keys in half minutes 0..2880, interpolated linearly, wrapping.
    /// Inside a local light's inner radius it is used fully, between inner and outer it fades into the base light.
    /// Params slot 0 (clear weather) is used.
    /// </summary>
    public static class WowLightTable
    {
        public const float MapCentre = 17066.666f;

        public static void Position(DbcTable light, int row, out float x, out float y, out float z, out float inner, out float outer)
        {
            float c0 = light.GetFloat(row, DbcFields.Light.GameCoords);
            float c1 = light.GetFloat(row, DbcFields.Light.GameCoords + 1);
            float c2 = light.GetFloat(row, DbcFields.Light.GameCoords + 2);
            x = MapCentre - c2 / 36f;
            y = MapCentre - c0 / 36f;
            z = c1 / 36f;
            inner = light.GetFloat(row, DbcFields.Light.GameFalloffStart) / 36f;
            outer = light.GetFloat(row, DbcFields.Light.GameFalloffEnd) / 36f;
        }

        public static bool IsGlobal(DbcTable light, int row)
        {
            return light.GetFloat(row, DbcFields.Light.GameCoords) == 0f && light.GetFloat(row, DbcFields.Light.GameCoords + 2) == 0f &&
                   light.GetFloat(row, DbcFields.Light.GameFalloffEnd) == 0f;
        }

        /// <summary>The light at (x, y, z) yards on mapId at hours (0..24). Null when the DBCs cannot be read.</summary>
        public static WowLightSample Sample(WowFiles files, int mapId, float x, float y, float z, float hours)
        {
            DbcTable light = Dbc.Load(files, "Light");
            DbcTable ib = Dbc.Load(files, "LightIntBand");
            DbcTable fb = Dbc.Load(files, "LightFloatBand");

            if (light == null || ib == null || fb == null)
            {
                return null;
            }

            int halfMinutes = (int)Math.Round(((hours % 24f) + 24f) % 24f * 120f) % 2880;
            int local = -1, global = -1, nearest = -1;
            float localOuter = float.MaxValue, localDist = 0f, localInner = 0f, nearestGap = float.MaxValue;

            for (int r = 0; r < light.RecordCount; r++)
            {
                if (light.GetInt(r, DbcFields.Light.ContinentID) != mapId)
                {
                    continue;
                }

                if (IsGlobal(light, r))
                {
                    if (global < 0) global = r;
                    continue;
                }

                float lx, ly, lz, inner, outer;
                Position(light, r, out lx, out ly, out lz, out inner, out outer);
                float d = (float)Math.Sqrt((lx - x) * (lx - x) + (ly - y) * (ly - y));

                if (d <= outer && outer < localOuter)
                {
                    local = r;
                    localOuter = outer;
                    localInner = inner;
                    localDist = d;
                }

                if (d - outer < nearestGap)
                {
                    nearestGap = d - outer;
                    nearest = r;
                }
            }

            int idOne = light.FindRow(1);
            int baseRow = global >= 0 ? global : (idOne >= 0 ? idOne : nearest);

            if (baseRow < 0 && local < 0)
            {
                return null;
            }

            WowLightSample baseSample = baseRow >= 0 ? FromRow(light, ib, fb, baseRow, halfMinutes) : null;
            WowLightSample result;
            string baseName = baseRow < 0 ? "none" : (baseRow == global ? "global light " : baseRow == idOne ? "fallback light " : "nearest light ") + light.GetId(baseRow);

            if (local >= 0)
            {
                WowLightSample localSample = FromRow(light, ib, fb, local, halfMinutes);
                float w = localDist <= localInner || localOuter <= localInner ? 1f : Math.Max(0f, Math.Min(1f, (localOuter - localDist) / (localOuter - localInner)));
                result = baseSample != null && w < 1f ? WowLightSample.Lerp(baseSample, localSample, w) : localSample;
                result.LightId = light.GetId(local);
                result.ParamsId = localSample.ParamsId;
                result.Weight = w;
                result.BaseLightId = baseRow >= 0 ? light.GetId(baseRow) : -1;
                result.Source = "local light " + result.LightId + " (params " + result.ParamsId + ", " + localDist.ToString("0") + " yd from its centre, inner " + localInner.ToString("0") +
                                " outer " + localOuter.ToString("0") + ", weight " + w.ToString("0.00") + " over " + baseName + ")";
            }
            else
            {
                result = baseSample;
                result.LightId = light.GetId(baseRow);
                result.BaseLightId = result.LightId;
                result.Weight = 0f;
                result.Source = baseName + " (params " + result.ParamsId + (baseRow == nearest ? ", " + nearestGap.ToString("0") + " yd outside its radius" : "") + ")";
            }

            return result;
        }

        private static WowLightSample FromRow(DbcTable light, DbcTable ib, DbcTable fb, int row, int halfMinutes)
        {
            var s = new WowLightSample();
            int p = light.GetInt(row, DbcFields.Light.LightParamsID);
            s.ParamsId = p;

            for (int k = 0; k < 18; k++)
            {
                s.Colors[k] = IntBand(ib, p * 18 - 17 + k, halfMinutes);
            }

            for (int k = 0; k < 6; k++)
            {
                s.Floats[k] = FloatBand(fb, p * 6 - 5 + k, halfMinutes);
            }

            return s;
        }

        /// <summary>One colour band at a time (half minutes), linear between keys, wrapping round midnight. Black when absent/empty.</summary>
        public static WowRgb IntBand(DbcTable ib, int id, int t)
        {
            int r = ib.FindRow(id);

            if (r < 0)
            {
                return new WowRgb();
            }

            int n = Math.Min(16, ib.GetInt(r, DbcFields.LightIntBand.Num));

            if (n <= 0)
            {
                return new WowRgb();
            }

            int a, b;
            float f;
            Segment(ib, r, DbcFields.LightIntBand.Time, n, t, out a, out b, out f);
            return WowRgb.Lerp(WowRgb.FromInt(ib.GetInt(r, DbcFields.LightIntBand.Data + a)), WowRgb.FromInt(ib.GetInt(r, DbcFields.LightIntBand.Data + b)), f);
        }

        public static float FloatBand(DbcTable fb, int id, int t)
        {
            int r = fb.FindRow(id);

            if (r < 0)
            {
                return 0f;
            }

            int n = Math.Min(16, fb.GetInt(r, DbcFields.LightFloatBand.Num));

            if (n <= 0)
            {
                return 0f;
            }

            int a, b;
            float f;
            Segment(fb, r, DbcFields.LightFloatBand.Time, n, t, out a, out b, out f);
            float va = fb.GetFloat(r, DbcFields.LightFloatBand.Data + a), vb = fb.GetFloat(r, DbcFields.LightFloatBand.Data + b);
            return va + (vb - va) * f;
        }

        private static void Segment(DbcTable t, int row, int timeField, int n, int time, out int a, out int b, out float f)
        {
            if (n == 1)
            {
                a = b = 0;
                f = 0f;
                return;
            }

            for (int i = 0; i < n - 1; i++)
            {
                int t0 = t.GetInt(row, timeField + i), t1 = t.GetInt(row, timeField + i + 1);

                if (time >= t0 && time < t1)
                {
                    a = i;
                    b = i + 1;
                    f = t1 > t0 ? (time - t0) / (float)(t1 - t0) : 0f;
                    return;
                }
            }

            // Between the last key and the first key of the next day.
            int last = t.GetInt(row, timeField + n - 1), first = t.GetInt(row, timeField) + 2880;
            int tt = time < t.GetInt(row, timeField) ? time + 2880 : time;
            a = n - 1;
            b = 0;
            f = first > last ? Math.Max(0f, Math.Min(1f, (tt - last) / (float)(first - last))) : 0f;
        }
    }
}
