using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Materials of the SomniumWoW/M2 shader for M2 batches (main thread). Shared materials are cached by texture + state
    /// so every instance of a doodad shares them (GPU instancing on). Blend factors / alpha test / fog per WoW blend mode as
    /// noggit3 (the 3.3.5 editor) sets them; AlphaKey's alphaRef is (224/255) x alpha (wowdev M2/Rendering, &lt;= WotLK).
    /// </summary>
    public static class M2Materials
    {
        public const string ShaderName = "SomniumWoW/M2";
        public const float AlphaKeyRef = 224f / 255f;

        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        static Shader _shader, _fallback;
        static bool _fallbackWarned;

        public static int CachedCount { get { return _cache.Count; } }

        static Shader Shader
        {
            get
            {
                if (_shader != null) return _shader;
                _shader = WowShaders.Find(ShaderName);
                if (_shader != null) return _shader;
                if (_fallback == null) _fallback = WowShaders.Find("Universal Render Pipeline/Unlit");
                if (!_fallbackWarned) { _fallbackWarned = true; WowLog.Warn("M2Materials: shader " + ShaderName + " missing (carrier WowM2.mat not carried?) - using URP Unlit, no blend modes"); }
                return _fallback;
            }
        }

        /// <summary>True when the real SomniumWoW/M2 shader is available (false = URP fallback).</summary>
        public static bool HasShader { get { var s = Shader; return s != null && s.name == ShaderName; } }

        /// <summary>A material for a batch with this texture. shared = cached and shared (do not destroy); else the caller owns it.</summary>
        public static Material Get(M2BatchInfo b, Texture tex, bool shared)
        {
            string key = null;
            if (shared)
            {
                key = (tex != null ? tex.GetInstanceID() : 0) + "|" + b.BlendMode + "|" + (b.RenderFlags & 0x17) + "|" + b.UvSet + "|"
                    + Mathf.RoundToInt(b.Alpha * 255f) + "|" + Mathf.RoundToInt(b.R * 255f) + "," + Mathf.RoundToInt(b.G * 255f) + "," + Mathf.RoundToInt(b.B * 255f)
                    + "|" + b.ScrollU.ToString("0.####") + "," + b.ScrollV.ToString("0.####") + "|" + Mathf.Clamp(b.PriorityPlane, -20, 20);
                Material cached;
                if (_cache.TryGetValue(key, out cached) && cached != null) return cached;
            }
            var m = Create(b, tex);
            if (shared) _cache[key] = m;
            return m;
        }

        static Material Create(M2BatchInfo b, Texture tex)
        {
            var sh = Shader;
            var m = new Material(sh);
            m.name = "M2 " + (tex != null ? tex.name : "untextured") + " b" + b.BlendMode;
            m.enableInstancing = true;
            if (!HasShader)
            {
                // URP Unlit fallback: texture only.
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                return m;
            }
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", new Color(b.R, b.G, b.B, b.Alpha));
            m.SetFloat("_Unlit", (b.RenderFlags & 0x01) != 0 ? 1f : 0f);
            m.SetFloat("_UvSet", b.UvSet == 1 ? 1f : 0f);
            m.SetVector("_UvScroll", new Vector4(b.ScrollU, b.ScrollV, 0f, 0f));
            m.SetFloat("_Cull", (b.RenderFlags & 0x04) != 0 ? (float)CullMode.Off : (float)CullMode.Back);

            BlendMode src = BlendMode.One, dst = BlendMode.Zero;
            float cutoff = -1f, fog = 0f;
            bool blended = true;
            switch (b.BlendMode)
            {
                case 0: blended = false; break;                                                        // Opaque
                case 1: blended = false; cutoff = AlphaKeyRef * b.Alpha; break;                        // AlphaKey
                case 2: src = BlendMode.SrcAlpha; dst = BlendMode.OneMinusSrcAlpha; cutoff = 1f / 255f; break;  // Alpha
                case 3: src = BlendMode.One; dst = BlendMode.One; cutoff = 1f / 255f; fog = 1f; break;          // NoAlphaAdd
                case 4: src = BlendMode.SrcAlpha; dst = BlendMode.One; cutoff = 1f / 255f; fog = 1f; break;     // Add
                case 5: src = BlendMode.DstColor; dst = BlendMode.Zero; cutoff = 1f / 255f; fog = 2f; break;    // Mod
                case 6: src = BlendMode.DstColor; dst = BlendMode.SrcColor; cutoff = 1f / 255f; fog = 3f; break; // Mod2x
                default: blended = false; break;
            }
            if ((b.RenderFlags & 0x02) != 0) fog = 4f;
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_FogMode", fog);
            m.SetFloat("_ZWrite", blended || (b.RenderFlags & 0x10) != 0 ? 0f : 1f);
            if (blended)
            {
                m.renderQueue = (int)RenderQueue.Transparent + Mathf.Clamp(b.PriorityPlane, -20, 20);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("ShadowCaster", false);
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("DepthNormals", false);
            }
            else if (b.BlendMode == 1)
            {
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else m.renderQueue = (int)RenderQueue.Geometry;
            return m;
        }

        /// <summary>Destroys every shared material (only when nothing uses them any more).</summary>
        public static void Clear()
        {
            foreach (var m in _cache.Values) if (m != null) Object.Destroy(m);
            _cache.Clear();
        }
    }
}
