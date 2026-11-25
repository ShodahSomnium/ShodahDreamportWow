// iteration 11 - owner spellfx: other units' stealth drawn translucent (proposal-spellfx.md s5b Rogue / s6.1). Online the server marks a
// stealthed unit with UNIT_FIELD_BYTES_1 byte 2 UNIT_STAND_FLAGS_CREEP 0x02 (SpellAuraEffects.cpp HandleModStealth -> SetStandFlags; EntityState
// .Stealthed) - a rogue / druid in the same party stays visible to us, others are simply not sent. Our own M2 models of OTHER units (WowEntityView:
// other WoW players, stealthed creatures) get instance materials with alpha blending (the M2 shader's _SrcBlend / _DstBlend / _ZWrite / _Color.a);
// Clear restores the shared materials. Alpha 0.3 = the Stealth state kit 312's CharParamZero[1] (measured); reading CharProc 14 as "model alpha" is
// OURS. The Somnium avatars are NOT touched here (proposal-avatar.md's StealthShell).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    public static class WowStealthLook
    {
        sealed class Saved
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Material[]> Shared = new List<Material[]>();
            public readonly List<Material> Made = new List<Material>();
        }

        static readonly Dictionary<GameObject, Saved> _saved = new Dictionary<GameObject, Saved>();
        static bool _logged;

        /// <summary>Draws the model translucent (alpha 0..1). Calling it again on the same model only changes nothing (Clear first to change alpha).</summary>
        public static void Apply(GameObject model, float alpha)
        {
            if (model == null || _saved.ContainsKey(model)) return;
            Sweep();
            var s = new Saved();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                var shared = r.sharedMaterials;
                var mine = new Material[shared.Length];
                bool any = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var m = shared[i];
                    if (m == null || !m.HasProperty("_SrcBlend") || !m.HasProperty("_Color")) { mine[i] = m; continue; }
                    var c = new Material(m) { name = m.name + " (stealth)" };
                    var src = (BlendMode)(int)m.GetFloat("_SrcBlend");
                    if (src == BlendMode.One && (BlendMode)(int)m.GetFloat("_DstBlend") == BlendMode.Zero)
                    {
                        // opaque / alpha-key: alpha blending (WoW draws a stealthed unit see-through)
                        c.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        c.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        c.SetFloat("_ZWrite", 0f);
                        c.renderQueue = (int)RenderQueue.Transparent;
                        c.SetOverrideTag("RenderType", "Transparent");
                        c.SetShaderPassEnabled("ShadowCaster", false);
                    }
                    var col = c.GetColor("_Color");
                    col.a *= Mathf.Clamp01(alpha);
                    c.SetColor("_Color", col);
                    mine[i] = c;
                    s.Made.Add(c);
                    any = true;
                }
                if (!any) continue;
                s.Renderers.Add(r);
                s.Shared.Add(shared);
                r.sharedMaterials = mine;
            }
            _saved[model] = s;
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("Stealth look: other stealthed units are drawn at alpha " + alpha.ToString("0.00") + " (Stealth kit 312 CharParamZero[1], measured; CharProc 14 read as model alpha is ours; logged once)");
            }
        }

        /// <summary>Back to the model's own materials.</summary>
        public static void Clear(GameObject model)
        {
            Saved s;
            if (model == null || !_saved.TryGetValue(model, out s)) return;
            _saved.Remove(model);
            for (int i = 0; i < s.Renderers.Count; i++) if (s.Renderers[i] != null) s.Renderers[i].sharedMaterials = s.Shared[i];
            foreach (var m in s.Made) if (m != null) Object.Destroy(m);
        }

        /// <summary>Drops (and frees the materials of) models destroyed while translucent.</summary>
        static void Sweep()
        {
            List<GameObject> dead = null;
            foreach (var k in _saved.Keys) if (k == null) (dead ?? (dead = new List<GameObject>())).Add(k);
            if (dead == null) return;
            foreach (var k in dead)
            {
                foreach (var m in _saved[k].Made) if (m != null) Object.Destroy(m);
                _saved.Remove(k);
            }
        }
    }
}
