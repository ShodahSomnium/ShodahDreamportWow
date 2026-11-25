// Lead 2026-10-10 (the user: "are you able to render ALL Ui AFTER DLSS? As it can sometimes cause it to be a blurry mess"): the Somnium
// client's URP renderers draw one layer, "Overlay", in a pass of their own after post-processing - outside DLSS
// (Assets/_SomniumSpace/Graphics/Scripts/OverlayLayerRenderFeature.cs: AfterRenderingPostProcessing, the jitter cancelled, the scene depth
// copied in conservatively; Assets/URP/URP_Renderer.asset and URP_Renderer_CameraApp.asset: _layerMask 512 = layer 9, the renderers' own
// opaque / transparent masks 1475593719 without bit 9). The desktop interface is a screen overlay canvas, drawn after every camera
// already; the VR panels, menus, nameplates and texts are world-space canvases in the world pass, so DLSS upscales them. The client keeps
// its own tablet in the world pass in XR (VRTablet/Tablet/TabletOverlayLayerAssigner.cs: "in XR, anything drawn after post-processing
// shakes relative to the world"), so this is a Settings choice (Effects > "Sharp VR interface").
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// Moves our world-space UI canvases (Register) onto the client's "Overlay" layer while Settings > Effects "Sharp VR interface" is on,
    /// in VR, when every renderer of the pipeline either draws that layer itself or has the client's OverlayLayerRenderFeature (active),
    /// and the head camera renders the layer - checked every SweepSeconds, back to UiKit.UnityUiLayer otherwise. Their sorting orders drop
    /// by OrderShift there, so what the client draws on that layer (its laser, HUD; orders 0..3 on the tablet's canvases) still draws over
    /// our panels, as when they were drawn before it. UiKit.Layer follows, so new UI objects are made on the layer in use. Values ours.
    /// </summary>
    public static class UiSharp
    {
        public const string LayerName = "Overlay", FeatureName = "OverlayLayerRenderFeature", SettingKey = "sharp_vr_ui";
        public const int OrderShift = -1000;
        public const float SweepSeconds = 1f;

        private static readonly List<GameObject> Roots = new List<GameObject>();
        private static readonly Dictionary<Canvas, int> BaseOrder = new Dictionary<Canvas, int>();
        private static readonly List<Canvas> CanvasList = new List<Canvas>();
        private static readonly List<CanvasRenderer> GraphicList = new List<CanvasRenderer>();
        private static readonly List<Canvas> Gone = new List<Canvas>();
        private static int _overlay = -2;     // -2 not looked up yet, -1 the client has none
        private static int _pipeline = -1;    // -1 not checked, 0 no, 1 yes
        private static string _pipeWhy;
        private static bool _on, _offered;
        private static float _next;
        private static string _lastWhy;

        /// <summary>The VR interface is on the client's after-post layer now.</summary>
        public static bool On { get { return _on; } }

        /// <summary>The layer UI objects are made on now (UiKit.Layer).</summary>
        public static int Layer { get { return _on ? _overlay : UiKit.UnityUiLayer; } }

        /// <summary>Unity's UI layer or the after-post layer (the avatar measurements skip both).</summary>
        public static bool IsUiLayer(int layer) { return layer == UiKit.UnityUiLayer || (_overlay >= 0 && layer == _overlay); }

        /// <summary>A world-space canvas of ours (its root object): follows the layer in use, its sorting shifted with it.</summary>
        public static void Register(GameObject root)
        {
            if (root == null || Roots.Contains(root)) return;
            Roots.Add(root);
            if (_on) Apply(root, true, true);
        }

        /// <summary>Every frame in the world (WowGame); works every SweepSeconds.</summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + SweepSeconds;
            string why;
            bool want = Wanted(out why);
            if (want != _on)
            {
                Roots.RemoveAll(r => r == null);
                _on = want;
                for (int i = 0; i < Roots.Count; i++) Apply(Roots[i], want, true);
                WowLog.Info("UI: " + (want
                    ? "the VR interface is drawn after DLSS - " + Roots.Count + " canvases on the client's '" + LayerName + "' layer (" + _overlay + "), sorting " + OrderShift
                    : "the VR interface is drawn in the world pass (layer " + UiKit.UnityUiLayer + ")") + " - " + why + ".");
            }
            else
            {
                if (!want && why != _lastWhy) WowLog.Info("UI: the VR interface stays in the world pass - " + why + ".");
                // canvases made since the last sweep (their graphics are made on UiKit.Layer already)
                if (_on) for (int i = 0; i < Roots.Count; i++) Apply(Roots[i], true, false);
            }
            _lastWhy = why;
            Gone.Clear();
            foreach (KeyValuePair<Canvas, int> kv in BaseOrder) if (kv.Key == null) Gone.Add(kv.Key);
            for (int i = 0; i < Gone.Count; i++) BaseOrder.Remove(Gone[i]);
        }

        private static bool Wanted(out string why)
        {
            if (!WowPlayer.XrPresent) { why = "not in VR (the desktop interface is a screen overlay, drawn after DLSS already)"; return false; }
            if (_overlay == -2) _overlay = LayerMask.NameToLayer(LayerName);
            if (_overlay < 0) { why = "the client has no '" + LayerName + "' layer"; return false; }
            if (_pipeline < 0)
            {
                _pipeline = PipelineDraws(_overlay, out _pipeWhy) ? 1 : 0;
                WowLog.Info("UI: the client's '" + LayerName + "' layer (" + _overlay + ") - " + _pipeWhy + ".");
            }
            if (_pipeline == 0) { why = _pipeWhy; return false; }
            if (!_offered) { _offered = true; WowSettings.SetLive(SettingKey); }   // the Settings row: the client can do it
            if (!WowSettings.SharpVrUi) { why = "Settings > Effects 'Sharp VR interface' is off"; return false; }
            Camera cam = WowPlayer.Head();
            if (cam == null) cam = Camera.main;
            if (cam == null || (cam.cullingMask & (1 << _overlay)) == 0) { why = "the head camera does not render the '" + LayerName + "' layer"; return false; }
            why = _pipeWhy;
            return true;
        }

        /// <summary>Every loaded URP renderer draws the layer: itself (its masks have it) or through the client's after-post pass.
        /// Lead fix 2026-10-10 (the user's upload log: "CS7069: Reference to type 'ReadOnlySpan&lt;&gt;' claims it is defined in
        /// 'mscorlib', but it could not be found"): Somnium's server build has no ReadOnlySpan, so not UniversalRenderPipelineAsset.
        /// rendererDataList - the renderer assets the pipeline loaded (Resources.FindObjectsOfTypeAll) instead.</summary>
        private static bool PipelineDraws(int layer, out string why)
        {
            try
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset == null) { why = "the render pipeline is not URP"; return false; }
                int bit = 1 << layer, with = 0, n = 0;
                UniversalRendererData[] list = Resources.FindObjectsOfTypeAll<UniversalRendererData>();
                for (int i = 0; list != null && i < list.Length; i++)
                {
                    ScriptableRendererData d = list[i];
                    if (d == null) continue;
                    n++;
                    bool feature = false;
                    List<ScriptableRendererFeature> fs = d.rendererFeatures;
                    for (int j = 0; fs != null && j < fs.Count; j++)
                        if (fs[j] != null && fs[j].isActive && fs[j].GetType().Name == FeatureName) feature = true;
                    if (feature) { with++; continue; }
                    var u = d as UniversalRendererData;
                    if (u == null || (u.opaqueLayerMask.value & bit) == 0 || (u.transparentLayerMask.value & bit) == 0)
                    {
                        why = "renderer '" + d.name + "' neither draws it nor has the client's after-post pass";
                        return false;
                    }
                }
                if (with == 0) { why = "no renderer has the client's after-post pass (" + FeatureName + ")"; return false; }
                why = with + " of " + n + " renderers draw it after post-processing (" + FeatureName + ")";
                return true;
            }
            catch (Exception e)
            {
                why = "the renderer check failed (" + e.GetType().Name + ": " + e.Message + ")";
                return false;
            }
        }

        /// <summary>The root's canvases (and with graphics their CanvasRenderers) from Unity's UI layer to the after-post layer or back;
        /// root / own-sorting canvases at their own order + OrderShift there.</summary>
        private static void Apply(GameObject root, bool on, bool graphics)
        {
            if (root == null || _overlay < 0) return;
            int to = on ? _overlay : UiKit.UnityUiLayer, from = on ? UiKit.UnityUiLayer : _overlay;
            root.GetComponentsInChildren(true, CanvasList);
            for (int i = 0; i < CanvasList.Count; i++)
            {
                Canvas c = CanvasList[i];
                if (c.gameObject.layer == from) c.gameObject.layer = to;
                if (!c.isRootCanvas && !c.overrideSorting) continue;
                int b;
                if (!BaseOrder.TryGetValue(c, out b)) { b = c.sortingOrder; BaseOrder[c] = b; }
                int want = on ? b + OrderShift : b;
                if (c.sortingOrder != want) c.sortingOrder = want;
            }
            CanvasList.Clear();
            if (!graphics) return;
            root.GetComponentsInChildren(true, GraphicList);
            for (int i = 0; i < GraphicList.Count; i++)
            {
                GameObject g = GraphicList[i].gameObject;
                if (g.layer == from) g.layer = to;
            }
            GraphicList.Clear();
        }
    }
}
