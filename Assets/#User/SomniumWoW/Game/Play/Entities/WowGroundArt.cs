using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>
    /// WoW's selection circle under the current target: Textures\UnitSelectTexture.blp (the texture Wow.exe names; white,
    /// its alpha is a ring bright at the picture's bottom edge fading to the back), tinted by the unit's reaction colour
    /// like the client's selection colour. Laid on the ground under the target (tilted to the ground normal from a ray
    /// against terrain/WMO layers 0 and 14), sized by the model's footprint, the bright edge turned toward the viewer.
    /// A world-space canvas (drawn in both eyes).
    /// </summary>
    internal sealed class WowSelectionCircle
    {
        private readonly GameObject _go;
        private readonly RawImage _img;
        private readonly RectTransform _rt;
        private Color _color = new Color(0f, 0f, 0f, 0f);
        private const int GroundMask = (1 << 0) | (1 << 14);

        public WowSelectionCircle(Transform parent, Texture tex)
        {
            _go = new GameObject("Selection circle");
            _go.layer = WowUi.UiLayer;
            _go.transform.SetParent(parent, false);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            _rt = (RectTransform)_go.transform;
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.sizeDelta = new Vector2(100f, 100f);
            var ig = new GameObject("Circle", typeof(RectTransform));
            ig.layer = WowUi.UiLayer;
            var irt = (RectTransform)ig.transform;
            irt.SetParent(_go.transform, false);
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            _img = ig.AddComponent<RawImage>();
            _img.texture = tex;
            _img.uvRect = new Rect(0f, 1f, 1f, -1f);
            _img.raycastTarget = false;
            _go.SetActive(false);
        }

        public void SetTexture(Texture tex)
        {
            if (_img.texture != tex) _img.texture = tex;
        }

        /// <summary>Shows the circle at a unit's feet. diameter in metres. LateUpdate.</summary>
        public void Show(Vector3 feet, float diameter, Color color, Vector3 cameraPos)
        {
            if (_img.texture == null) { Hide(); return; }
            if (!_go.activeSelf) _go.SetActive(true);
            if (color != _color) { _color = color; _img.color = color; }
            Vector3 normal = Vector3.up;
            Vector3 pos = feet;
            RaycastHit hit;
            if (Physics.Raycast(feet + Vector3.up * 1.5f, Vector3.down, out hit, 4f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                pos = hit.point;
            }
            // Integrator (render lab r1: on a curved slope the flat disc went under the ground and showed a straight cut):
            // fit the disc to the ground under four points near its rim and lift it above the highest of them.
            float r = Mathf.Max(0.6f, diameter) * 0.45f;
            Vector3 t1 = Vector3.Cross(normal, Vector3.forward);
            if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(normal, Vector3.right);
            t1.Normalize();
            Vector3 t2 = Vector3.Cross(normal, t1);
            Vector3 a, b, c, d;
            if (GroundAt(pos + t1 * r, out a) && GroundAt(pos - t1 * r, out b) && GroundAt(pos + t2 * r, out c) && GroundAt(pos - t2 * r, out d))
            {
                Vector3 n = Vector3.Cross(a - b, c - d);
                if (n.y < 0f) n = -n;
                if (n.sqrMagnitude > 1e-8f && Vector3.Angle(n, Vector3.up) < 50f)
                {
                    normal = n.normalized;
                    float lift = Mathf.Max(0f, Mathf.Max(Mathf.Max(Vector3.Dot(a - pos, normal), Vector3.Dot(b - pos, normal)), Mathf.Max(Vector3.Dot(c - pos, normal), Vector3.Dot(d - pos, normal))));
                    pos += normal * lift;
                }
            }
            // Canvas forward = into the ground (visible from above); its local up points away from the camera, so the
            // picture's bottom edge (the bright rim) faces the viewer.
            Vector3 toCam = Vector3.ProjectOnPlane(cameraPos - pos, normal);
            if (toCam.sqrMagnitude < 1e-6f) toCam = Vector3.ProjectOnPlane(Vector3.forward, normal);
            _go.transform.rotation = Quaternion.LookRotation(-normal, -toCam.normalized);
            _go.transform.position = pos + normal * 0.06f;
            float s = Mathf.Max(0.6f, diameter) / 100f;
            _go.transform.localScale = new Vector3(s, s, s);
        }

        public void Hide()
        {
            if (_go.activeSelf) _go.SetActive(false);
        }

        private static bool GroundAt(Vector3 p, out Vector3 at)
        {
            RaycastHit h;
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out h, 5f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                at = h.point;
                return true;
            }
            at = p;
            return false;
        }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }
    }

    /// <summary>
    /// The loot sparkle over a lootable corpse. The game's own effect (PARTICLES\LootFX.m2) is particles only (0 vertices) and
    /// Models draws no particles, so this is four twinkling billboards with that effect's particle texture SPELLS\STAR5A.BLP
    /// (alpha from brightness). A world-space canvas, animated in LateUpdate without allocations.
    /// </summary>
    internal sealed class WowSparkle
    {
        private readonly GameObject _go;
        private readonly RectTransform[] _stars = new RectTransform[4];
        private readonly RawImage[] _imgs = new RawImage[4];
        private readonly float[] _phase = { 0f, 1.7f, 3.1f, 4.4f };
        private static readonly Vector2[] Spots = { new Vector2(-28f, 10f), new Vector2(22f, 26f), new Vector2(6f, -18f), new Vector2(-10f, 36f) };

        public WowSparkle(Transform parent, Texture tex)
        {
            _go = new GameObject("Loot sparkle");
            _go.layer = WowUi.UiLayer;
            _go.transform.SetParent(parent, false);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            var rt = (RectTransform)_go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 100f);
            rt.localScale = Vector3.one * 0.008f;
            for (int i = 0; i < _stars.Length; i++)
            {
                var g = new GameObject("Star " + i, typeof(RectTransform));
                g.layer = WowUi.UiLayer;
                var r = (RectTransform)g.transform;
                r.SetParent(_go.transform, false);
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.anchoredPosition = Spots[i];
                r.sizeDelta = new Vector2(40f, 40f);
                var img = g.AddComponent<RawImage>();
                img.texture = tex;
                img.uvRect = new Rect(0f, 1f, 1f, -1f);
                img.raycastTarget = false;
                img.color = new Color(1f, 0.93f, 0.55f, 1f);
                _stars[i] = r;
                _imgs[i] = img;
            }
            _go.SetActive(false);
        }

        public bool Visible { get { return _go.activeSelf; } }

        public void SetVisible(bool on)
        {
            if (_go.activeSelf != on) _go.SetActive(on && _imgs[0].texture != null);
        }

        public void Tick(Vector3 pos, Vector3 cameraPos, float time)
        {
            var t = _go.transform;
            t.position = pos;
            Vector3 away = pos - cameraPos;
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
            for (int i = 0; i < _stars.Length; i++)
            {
                float w = Mathf.Sin(time * 3.2f + _phase[i]);
                float k = Mathf.Clamp01(w * 0.5f + 0.5f);
                float size = 18f + 34f * k;
                _stars[i].sizeDelta = new Vector2(size, size);
                _stars[i].localRotation = Quaternion.Euler(0f, 0f, time * 40f + _phase[i] * 30f);
                var c = _imgs[i].color;
                c.a = 0.25f + 0.75f * k;
                _imgs[i].color = c;
            }
        }
    }
}
