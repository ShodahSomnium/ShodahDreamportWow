// iteration 11 - owner avatar (proposal-avatar.md 6.F FormShell): the translucent ghost / stealth / shadow look of a Somnium avatar.
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Forms
{
    /// <summary>
    /// For every SkinnedMeshRenderer of the avatar model (and its skinned wearables) a copy under "WoW Avatar Forms" that shares the
    /// source's mesh and BONES (so it moves exactly with the avatar, and is drawn re-mapped with it - GearRemap moves the avatar's
    /// roots for rendering) with one shared translucent SomniumWoW/M2 material per kind: unlit, SrcAlpha / OneMinusSrcAlpha, no depth
    /// write, queue 3000. Colours (ours): ghost (0.62, 0.80, 1.00, 0.45), stealth (0.75, 0.75, 0.85, 0.22), shadow (0.16, 0.02, 0.30,
    /// 0.55) over the visible avatar. Each copy's enabled mirrors its source (the client toggles LODs). Blend shapes are not copied
    /// (a neutral face) and non-skinned accessories get no shell (README known gaps).
    /// </summary>
    public sealed class FormShell
    {
        private static readonly Material[] _mats = new Material[8];
        private static bool _colorsSaid;

        private readonly List<SkinnedMeshRenderer> _src = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> _dst = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> _scan = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> _more = new List<SkinnedMeshRenderer>();
        private GameObject _root;
        private AvatarLookKind _kind;
        private float _nextScan;

        public AvatarLookKind Kind { get { return _kind; } }
        public int Count { get { return _dst.Count; } }

        public static Color ColourOf(AvatarLookKind k)
        {
            switch (k)
            {
                case AvatarLookKind.GhostShell: return new Color(0.62f, 0.80f, 1.00f, 0.45f);
                case AvatarLookKind.StealthShell: return new Color(0.75f, 0.75f, 0.85f, 0.22f);
                case AvatarLookKind.ShadowTint: return new Color(0.16f, 0.02f, 0.30f, 0.55f);
                default: return new Color(1f, 1f, 1f, 0.5f);
            }
        }

        private static Material MaterialOf(AvatarLookKind k)
        {
            int i = (int)k & 7;
            if (_mats[i] != null) return _mats[i];
            Shader sh = WowShaders.Find("SomniumWoW/M2");
            if (sh == null) return null;
            var m = new Material(sh) { name = "WoW avatar shell " + k };
            m.SetTexture("_MainTex", Texture2D.whiteTexture);
            m.SetColor("_Color", ColourOf(k));
            m.SetFloat("_Unlit", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetFloat("_FogMode", 0f);
            m.SetFloat("_Cutoff", -1f);
            m.renderQueue = (int)RenderQueue.Transparent;
            _mats[i] = m;
            if (!_colorsSaid)
            {
                _colorsSaid = true;
                WowLog.Info("Forms: shell colours (ours) - ghost " + ColourOf(AvatarLookKind.GhostShell) + ", stealth " + ColourOf(AvatarLookKind.StealthShell) +
                            ", shadowform " + ColourOf(AvatarLookKind.ShadowTint) + " (SomniumWoW/M2 unlit, alpha blended)");
            }
            return m;
        }

        /// <summary>True when the next Show re-scans the sources (first time, a new kind, or once a second).</summary>
        public bool NeedsScan(AvatarLookKind kind) { return _root == null || kind != _kind || Time.unscaledTime >= _nextScan; }

        /// <summary>Builds (or re-scans, once a second) the copies of this avatar model's skins + the given wearable roots.</summary>
        public void Show(AvatarLookKind kind, Animator model, List<Transform> items, Transform container)
        {
            if (model == null) { Destroy(); return; }
            if (!NeedsScan(kind)) { Mirror(); return; }
            _nextScan = Time.unscaledTime + 1f;
            if (kind != _kind) Destroy();
            _kind = kind;
            Material mat = MaterialOf(kind);
            if (mat == null) return;
            if (_root == null)
            {
                _root = new GameObject("Avatar shell (" + kind + ")");
                if (container != null) _root.transform.SetParent(container, false);
            }
            _scan.Clear();
            model.GetComponentsInChildren(true, _scan);
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null)
                    {
                        _more.Clear();
                        items[i].GetComponentsInChildren(true, _more);
                        _scan.AddRange(_more);
                        _more.Clear();
                    }
            // drop copies whose source is gone; add the new sources
            for (int i = _src.Count - 1; i >= 0; i--)
                if (_src[i] == null || !_scan.Contains(_src[i]))
                {
                    if (_dst[i] != null) Object.Destroy(_dst[i].gameObject);
                    _src.RemoveAt(i);
                    _dst.RemoveAt(i);
                }
            for (int i = 0; i < _scan.Count; i++)
            {
                SkinnedMeshRenderer s = _scan[i];
                if (s == null || s.sharedMesh == null || _src.Contains(s)) continue;
                var go = new GameObject("shell of " + s.name);
                go.transform.SetParent(_root.transform, false);
                var d = go.AddComponent<SkinnedMeshRenderer>();
                d.sharedMesh = s.sharedMesh;
                d.bones = s.bones;
                d.rootBone = s.rootBone;
                d.localBounds = s.localBounds;
                d.updateWhenOffscreen = s.updateWhenOffscreen;
                d.quality = s.quality;
                int n = Mathf.Max(1, s.sharedMesh.subMeshCount);
                var mats = new Material[n];
                for (int k = 0; k < n; k++) mats[k] = mat;
                d.sharedMaterials = mats;
                d.shadowCastingMode = ShadowCastingMode.Off;
                d.receiveShadows = false;
                d.lightProbeUsage = LightProbeUsage.Off;
                d.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _src.Add(s);
                _dst.Add(d);
            }
            _scan.Clear();
            Mirror();
        }

        /// <summary>Each copy drawn exactly while its source would be (enabled and active); the client toggles LODs.</summary>
        private void Mirror()
        {
            for (int i = 0; i < _src.Count; i++)
            {
                SkinnedMeshRenderer s = _src[i], d = _dst[i];
                if (d == null) continue;
                // A part the client draws as a shadow only (a first-person head) is not drawn by the shell either; the layer follows
                // the source too (a layer the first-person camera culls culls the copy).
                bool on = s != null && s.enabled && s.gameObject.activeInHierarchy && s.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
                if (d.enabled != on) d.enabled = on;
                if (s != null && d.gameObject.layer != s.gameObject.layer) d.gameObject.layer = s.gameObject.layer;
            }
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _src.Clear();
            _dst.Clear();
            _nextScan = 0f;
        }
    }
}
