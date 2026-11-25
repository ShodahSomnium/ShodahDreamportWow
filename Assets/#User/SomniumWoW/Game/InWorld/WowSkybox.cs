// Lead 2026-10-10 (the user: "the sky in burning crusade areas looks wrong"; Player.log: Hellfire Peninsula, map 530): the Light rows'
// LightParams name a skybox model (LightParams.LightSkyboxID -> LightSkybox.Name, e.g. the nebulae over Outland) that the client draws
// round the camera over its gradient sky - we drew only the gradient dome.
using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The skybox model of the light in effect (WowLighting.Current.ParamsId -> LightParams field 2 LightSkyboxID -> LightSkybox field 1
    /// Name, .mdx read as .m2): built once per model (M2Factory, its own materials), centred on the camera every frame, scaled to SkyRadius
    /// (at most FarShare of the camera's far plane), animated by its own M2Animator, drawn right after the gradient dome (render queue
    /// QueueAfterDome, depth write off, fog off, no shadows) so the world draws over it. None when the light names no skybox. Values ours.
    /// </summary>
    public static class WowSkybox
    {
        public const float SkyRadius = 2500f, FarShare = 0.8f;
        public const int QueueAfterDome = 1001;

        private static GameObject _go;
        private static string _path;
        private static int _params = int.MinValue;
        private static float _modelRadius = 1f;
        private static int _logs;

        /// <summary>Every frame in the world, after WowLighting.Tick.</summary>
        public static void Tick(WowFiles files)
        {
            var s = WowLighting.Current;
            int p = s != null ? s.ParamsId : 0;
            if (p != _params)
            {
                _params = p;
                string path = files != null ? PathOf(files, p) : null;
                if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
                {
                    Drop();
                    _path = path;
                    if (path != null) Build(files, path, p);
                }
            }
            if (_go == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            _go.transform.position = cam.transform.position;
            float radius = Mathf.Min(SkyRadius, cam.farClipPlane * FarShare);
            _go.transform.localScale = Vector3.one * (radius / Mathf.Max(0.01f, _modelRadius));
        }

        /// <summary>The skybox M2 path of LightParams row p, null when none.</summary>
        public static string PathOf(WowFiles files, int p)
        {
            if (p <= 0) return null;
            DbcTable lp = Dbc.Load(files, "LightParams"), sb = Dbc.Load(files, "LightSkybox");
            if (lp == null || sb == null) return null;
            int r = lp.FindRow(p);
            int box = r >= 0 ? lp.GetInt(r, DbcFields.LightParams.LightSkyboxID) : 0;
            int br = box > 0 ? sb.FindRow(box) : -1;
            string name = br >= 0 ? sb.GetString(br, DbcFields.LightSkybox.Name) : null;
            if (string.IsNullOrEmpty(name)) return null;
            string path = M2Model.NormalizeModelPath(name);
            return files.Exists(path) ? path : null;
        }

        private static void Build(WowFiles files, string path, int p)
        {
            try
            {
                _go = M2Factory.Build(files, path, new M2BuildOptions { ShareMaterials = false, Name = "WoW skybox" });
            }
            catch (Exception e) { _go = null; if (_logs++ < 4) WowLog.Warn("Lighting: skybox " + path + " threw " + e.Message); return; }
            if (_go == null) { if (_logs++ < 4) WowLog.Warn("Lighting: skybox " + path + " could not be built."); return; }
            if (WowGame.Current != null) _go.transform.SetParent(WowGame.Current.transform, true);
            Bounds b = default(Bounds);
            bool any = false;
            foreach (Renderer r in _go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    m.renderQueue = QueueAfterDome;
                    if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                    if (m.HasProperty("_FogMode")) m.SetFloat("_FogMode", 0f);
                }
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) smr.updateWhenOffscreen = true;   // centred on the camera: never culled
                Bounds rb = smr != null && smr.sharedMesh != null ? smr.sharedMesh.bounds : r.bounds;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            _modelRadius = any ? Mathf.Max(1f, b.extents.magnitude) : 1f;
            if (_logs++ < 8) WowLog.Info("Lighting: skybox " + path + " (LightParams " + p + "), model radius " + _modelRadius.ToString("0.0") + " - drawn round the camera at " + SkyRadius + " m (ours).");
        }

        public static void Drop()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _path = null;
            _params = int.MinValue;
        }
    }
}
