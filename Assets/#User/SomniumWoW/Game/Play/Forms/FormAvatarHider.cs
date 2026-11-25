// iteration 11 - owner avatar (proposal-avatar.md 6.F): hides exactly one Somnium avatar while it wears a creature / ghost / stealth look.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Forms
{
    /// <summary>
    /// Hides one avatar with Renderer.forceRenderingOff (the switch GearHider and the isolation use; the client never writes it,
    /// WowIsolation.cs): (a) every Renderer under the humanoid Animator's own transform (the avatar MODEL - never its .root, which
    /// for the local player can be the whole rig with the camera and UI); (b) every renderer of a scene root "Simulator Item: ..."
    /// whose SkinnedMeshRenderer is bound to that model's bones (cloth wearables, WowIsolation.cs:55); never anything under the WoW
    /// Game object. Re-scans once a second while hidden (avatar LOD switches, a new model); Show() restores exactly the renderers it
    /// flipped. Independent of GearRemote's far/other-map GearHider: each restores only its own list. While hidden the avatar's
    /// Animator is kept animating (AlwaysAnimate, restored on Show) so a shell driven by its bones never freezes.
    /// </summary>
    public sealed class FormAvatarHider
    {
        public const string ItemPrefix = "Simulator Item:";   // InventoryItemController.cs:73 (WowIsolation.ItemPrefix)

        private readonly List<Renderer> _off = new List<Renderer>();
        private readonly List<Renderer> _scan = new List<Renderer>();
        private readonly List<SkinnedMeshRenderer> _skins = new List<SkinnedMeshRenderer>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<Transform> _items = new List<Transform>();
        private readonly List<Transform> _visual = new List<Transform>();   // lead fix 2026-10-03: every visual copy of this avatar
        private Animator _anim;
        private AnimatorCullingMode _cullWas;
        private bool _cullSet;
        private float _nextScan;

        public bool Hidden { get; private set; }
        public int Count { get { return _off.Count; } }

        /// <summary>The wearable roots ("Simulator Item:") bound to the hidden model, as of the last scan (FormShell copies their skins).</summary>
        public List<Transform> Items { get { return _items; } }

        /// <summary>Hides the avatar of this Animator (exclude = the WoW Game object: never hidden). Cheap between the 1 s re-scans.</summary>
        public void Hide(Animator model, Transform exclude)
        {
            if (model == null) { Show(); return; }
            if (model != _anim) { Show(); _anim = model; }
            if (Hidden && Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;
            Hidden = true;
            if (!_cullSet)
            {
                _cullSet = true;
                _cullWas = model.cullingMode;
                if (_cullWas != AnimatorCullingMode.AlwaysAnimate) model.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            Transform t = model.transform;
            // Lead fix (2026-10-03, the user: "polymorph, all of them, hides the player avatar for all"): every visual copy of the
            // avatar (the main copy the cameras draw, the mirror / tablet-camera copy, the teleport copy - GearAvatarRoots), not only
            // the one GearBody matched: the copy hidden before was the mirror one, so the avatar stayed in view.
            GearAvatarRoots.VisualRoots(model, _visual);
            if (_visual.Count == 0) _visual.Add(t);
            for (int i = 0; i < _visual.Count; i++) Collect(_visual[i], exclude);
            if (!_loggedRoots || _visual.Count != _loggedCount)
            {
                _loggedRoots = true;
                _loggedCount = _visual.Count;
                WowLog.Info("Forms: hiding " + _visual.Count + " visual cop" + (_visual.Count == 1 ? "y" : "ies") + " of the avatar '" + model.name + "' (" + Names() + "), " + _off.Count + " renderers");
            }
            FindItems(t);
            for (int i = 0; i < _items.Count; i++) Collect(_items[i], exclude);
        }

        private bool _loggedRoots;
        private int _loggedCount;

        private string Names()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _visual.Count; i++)
            {
                if (_visual[i] == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(GearAvatarRoots.KindOf(_visual[i]) ?? _visual[i].name);
            }
            return sb.ToString();
        }

        /// <summary>Puts back every renderer this hider switched off (and the Animator's culling mode).</summary>
        public void Show()
        {
            for (int i = 0; i < _off.Count; i++) if (_off[i] != null) _off[i].forceRenderingOff = false;
            _off.Clear();
            if (_cullSet && _anim != null && _anim.cullingMode == AnimatorCullingMode.AlwaysAnimate) _anim.cullingMode = _cullWas;
            _cullSet = false;
            Hidden = false;
            _nextScan = 0f;
            _loggedRoots = false;
        }

        private void Collect(Transform t, Transform exclude)
        {
            if (t == null) return;
            _scan.Clear();
            t.GetComponentsInChildren(true, _scan);
            for (int i = 0; i < _scan.Count; i++)
            {
                Renderer r = _scan[i];
                if (r == null || r.forceRenderingOff || r is LineRenderer || r is TrailRenderer) continue;
                if (exclude != null && r.transform.IsChildOf(exclude)) continue;
                r.forceRenderingOff = true;
                _off.Add(r);
            }
            _scan.Clear();
        }

        /// <summary>The "Simulator Item:" scene roots skinned to this model's bones (the model's own scene; one pass a second).</summary>
        public void FindItems(Transform model)
        {
            _items.Clear();
            if (model == null) return;
            Scene scene = model.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return;
            _roots.Clear();
            scene.GetRootGameObjects(_roots);
            for (int i = 0; i < _roots.Count; i++)
            {
                GameObject go = _roots[i];
                if (go == null || !go.name.StartsWith(ItemPrefix, System.StringComparison.Ordinal)) continue;
                _skins.Clear();
                go.GetComponentsInChildren(true, _skins);
                for (int k = 0; k < _skins.Count; k++)
                {
                    SkinnedMeshRenderer s = _skins[k];
                    if (s == null) continue;
                    Transform bone = s.rootBone;
                    if (bone == null) { Transform[] b = s.bones; bone = b != null && b.Length > 0 ? b[0] : null; }
                    if (bone != null && bone.IsChildOf(model)) { _items.Add(go.transform); break; }
                }
                _skins.Clear();
            }
            _roots.Clear();
        }
    }
}
