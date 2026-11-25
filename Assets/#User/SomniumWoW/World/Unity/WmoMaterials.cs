using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>
    /// Materials for WMO batches on the SomniumWoW/Wmo shader, shared by every WMO that uses the same textures, shader kind,
    /// blend mode and flags (main thread). Carrier: Resources/WowWmo.mat (the integrator puts it into WowGame._shaderCarriers).
    /// EGxBlend (3.3.5a; measured in the WMO materials: 0 23,913x, 1 1,104x, 2 13x, 3 3x, 5 1x):
    /// 0 Opaque, 1 AlphaKey (alpha test), 2 Alpha, 3 Add (SrcAlpha, One), 4 Mod, 5 Mod2x, 6 ModAdd, 7 InvSrcAlphaAdd,
    /// 8 InvSrcAlphaOpaque, 9 SrcAlphaOpaque, 10 NoAlphaAdd; anything else draws opaque.
    /// </summary>
    public static class WmoMaterials
    {
        public const string ShaderName = "SomniumWoW/Wmo";

        /// <summary>
        /// Alpha-test threshold for AlphaKey materials. The client's value (CShaderEffect::SetAlphaRefDefault) is not documented
        /// on wowdev and could not be measured offline: 0.5 is a chosen default (the lab render decides; 1-bit-alpha BLPs, most
        /// cut-outs, are unaffected by the exact value).
        /// </summary>
        public static float AlphaKeyCutoff = 0.5f;

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static Shader _shader;
        static bool _shaderLogged;

        public static int Count { get { return Cache.Count; } }

        /// <summary>The texture options a material's textures load with (F_CLAMP_S / F_CLAMP_T override WoW's default wrap).</summary>
        public static WowTextureOptions TextureOptions(WmoMaterial m)
        {
            var o = WowTextureOptions.World;
            if (m != null && m.ClampS) o.WrapU = TextureWrapMode.Clamp;
            if (m != null && m.ClampT) o.WrapV = TextureWrapMode.Clamp;
            return o;
        }

        /// <summary>True when this material samples texture 2 (TwoLayerDiffuse with a second texture).</summary>
        public static bool UsesTexture2(WmoMaterial m)
        {
            return m != null && m.Shader == 6 && !string.IsNullOrEmpty(m.Texture2);
        }

        static string Key(WmoMaterial m)
        {
            return WowTextures.NormalizePath(m.Texture1) + "|" + (UsesTexture2(m) ? WowTextures.NormalizePath(m.Texture2) : "") + "|b" + m.BlendMode
                + "|f" + (m.Flags & (WmoMaterial.F_UNFOGGED | WmoMaterial.F_UNCULLED | WmoMaterial.F_CLAMP_S | WmoMaterial.F_CLAMP_T | WmoMaterial.F_SIDN)).ToString("X")
                + "|e" + (m.Sidn ? m.SidnColor.ToString("X8") : "0");
        }

        /// <summary>
        /// The shared material for a MOMT entry. getTexture(path, options) returns the Texture2D (e.g. from WowTextures).
        /// Null material data gives the magenta Missing texture on an opaque material. Returns null only when neither the WMO
        /// shader nor a stock URP shader exists.
        /// </summary>
        public static Material Get(WmoMaterial m, Func<string, WowTextureOptions, Texture2D> getTexture, out bool created)
        {
            created = false;
            if (m == null) m = new WmoMaterial { Texture1 = "" };
            string key = Key(m);
            Material mat;
            if (Cache.TryGetValue(key, out mat) && mat != null) return mat;

            var shader = FindShader();
            var useShader = shader != null ? shader : FallbackShader();
            if (useShader == null) return null;
            var opts = TextureOptions(m);
            mat = new Material(useShader) { name = "WMO " + FileName(m.Texture1) + " b" + m.BlendMode };
            mat.enableInstancing = true;
            var t1 = string.IsNullOrEmpty(m.Texture1) ? WowTextures.Missing : getTexture(m.Texture1, opts);
            mat.SetTexture("_BaseMap", t1);
            if (shader == null)
            {
                // Fallback (stock URP shader): texture only, still visible. Logged once in FindShader.
                Cache[key] = mat;
                created = true;
                return mat;
            }
            bool twoLayer = UsesTexture2(m);
            mat.SetFloat("_TwoLayer", twoLayer ? 1f : 0f);
            mat.SetTexture("_Layer2Map", twoLayer ? getTexture(m.Texture2, opts) : Texture2D.blackTexture);
            mat.SetFloat("_Unfogged", m.Unfogged ? 1f : 0f);
            mat.SetFloat("_Cull", m.TwoSided ? (float)CullMode.Off : (float)CullMode.Back);
            mat.SetColor("_Emissive", m.Sidn ? Bgra(m.SidnColor) : Color.clear);
            mat.SetFloat("_Cutoff", AlphaKeyCutoff);

            BlendMode src = BlendMode.One, dst = BlendMode.Zero;
            bool blended = true, alphaOut = true, clip = false;
            switch (m.BlendMode)
            {
                case 0: blended = false; alphaOut = false; break;
                case 1: blended = false; alphaOut = false; clip = true; break;
                case 2: src = BlendMode.SrcAlpha; dst = BlendMode.OneMinusSrcAlpha; break;
                case 3: src = BlendMode.SrcAlpha; dst = BlendMode.One; break;
                case 4: src = BlendMode.DstColor; dst = BlendMode.Zero; break;
                case 5: src = BlendMode.DstColor; dst = BlendMode.SrcColor; break;
                case 6: src = BlendMode.DstColor; dst = BlendMode.One; break;
                case 7: src = BlendMode.OneMinusSrcAlpha; dst = BlendMode.One; break;
                case 8: src = BlendMode.OneMinusSrcAlpha; dst = BlendMode.Zero; break;
                case 9: src = BlendMode.SrcAlpha; dst = BlendMode.Zero; break;
                case 10: src = BlendMode.One; dst = BlendMode.One; alphaOut = false; break;
                default: blended = false; alphaOut = false; break;
            }
            mat.SetFloat("_SrcBlend", (float)src);
            mat.SetFloat("_DstBlend", (float)dst);
            mat.SetFloat("_ZWrite", blended ? 0f : 1f);
            mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
            mat.SetFloat("_AlphaBlend", alphaOut ? 1f : 0f);
            if (blended)
            {
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetShaderPassEnabled("ShadowCaster", false);
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("DepthNormals", false);
            }
            else if (clip)
            {
                mat.renderQueue = (int)RenderQueue.AlphaTest;
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else mat.renderQueue = (int)RenderQueue.Geometry;

            Cache[key] = mat;
            created = true;
            return mat;
        }

        static Shader FindShader()
        {
            if (_shader != null) return _shader;
            _shader = WowShaders.Find(ShaderName);
            if (_shader == null && !_shaderLogged)
            {
                _shaderLogged = true;
                WowLog.Error("WMO: shader '" + ShaderName + "' missing (carrier Resources/WowWmo.mat not in WowGame._shaderCarriers?) - buildings drawn with a stock URP shader, no baked lighting");
            }
            return _shader;
        }

        static Shader FallbackShader()
        {
            var s = WowShaders.Find("Universal Render Pipeline/Simple Lit");
            if (s == null) s = WowShaders.Find("Universal Render Pipeline/Lit");
            return s;
        }

        /// <summary>CImVector (B, G, R, A bytes, as a little-endian uint 0xAARRGGBB) -> Color (gamma values, not converted).</summary>
        public static Color Bgra(uint c)
        {
            return new Color(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, ((c >> 24) & 0xFF) / 255f);
        }

        static string FileName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "(none)";
            int i = Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));
            return i >= 0 ? p.Substring(i + 1) : p;
        }

        /// <summary>Destroys every cached material (call when no WMO instance uses them any more).</summary>
        public static void Clear()
        {
            foreach (var m in Cache.Values) if (m != null) WmoFactory.DestroyAsset(m);
            Cache.Clear();
        }
    }
}
