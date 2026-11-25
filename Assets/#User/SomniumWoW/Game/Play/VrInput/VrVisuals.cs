using SomniumSpace.Worlds.SomniumWoW.Models;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// A camera-facing soft glow (the held spell in the hand, the hovered entity, the end of the target ray): a 4-vertex quad
    /// with vertex colours in the game's own particle material (M2Effects.MaterialFor: the carried SomniumWoW/Particle shader,
    /// blend 4 = WoW "Add", unfogged) so it renders like the spell effects around it.
    /// </summary>
    internal sealed class VrGlow
    {
        public readonly GameObject Go;
        private readonly Mesh _mesh;
        private readonly Color32[] _cols = new Color32[4];
        private Color32 _shown = new Color32(0, 0, 0, 0);

        public VrGlow(string name, int blend)
        {
            Go = new GameObject(name);
            _mesh = new Mesh { name = name };
            _mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            for (int i = 0; i < 4; i++) _cols[i] = new Color32(255, 255, 255, 255);
            _mesh.colors32 = _cols;
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            Go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = Go.AddComponent<MeshRenderer>();
            r.sharedMaterial = M2Effects.MaterialFor(VrArt.Glow, blend, true, 5, false);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Go.SetActive(false);
        }

        public void Show(bool on)
        {
            if (Go != null && Go.activeSelf != on) Go.SetActive(on);
        }

        /// <summary>Shows the glow at pos, facing eye, size metres across, in colour c (alpha = strength).</summary>
        public void Set(Vector3 pos, Vector3 eye, float size, Color c)
        {
            Show(true);
            Vector3 f = pos - eye;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            Go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f, Vector3.up));
            Go.transform.localScale = Vector3.one * Mathf.Max(0.001f, size);
            Color32 k = c;
            if (k.r != _shown.r || k.g != _shown.g || k.b != _shown.b || k.a != _shown.a)
            {
                _shown = k;
                for (int i = 0; i < 4; i++) _cols[i] = k;
                _mesh.colors32 = _cols;
            }
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
            if (_mesh != null) Object.Destroy(_mesh);
        }
    }

    /// <summary>The target ray's thin line: a 2-point LineRenderer with VrArt.Beam in the particle material (alpha blend).</summary>
    internal sealed class VrLine
    {
        public readonly GameObject Go;
        private readonly LineRenderer _line;

        public VrLine(string name)
        {
            Go = new GameObject(name);
            _line = Go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.lightProbeUsage = LightProbeUsage.Off;
            _line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _line.sharedMaterial = M2Effects.MaterialFor(VrArt.Beam, 2, true, 4, false);
            Go.SetActive(false);
        }

        public void Show(bool on)
        {
            if (Go != null && Go.activeSelf != on) Go.SetActive(on);
        }

        public void Set(Vector3 from, Vector3 to, Color near, Color far)
        {
            Show(true);
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            float d = Vector3.Distance(from, to);
            _line.startWidth = 0.004f;
            _line.endWidth = Mathf.Max(0.006f, d * 0.0025f);   // ~0.14 degrees wide at the far end: stays visible at 150 m
            _line.startColor = near;
            _line.endColor = far;
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
        }
    }
}
