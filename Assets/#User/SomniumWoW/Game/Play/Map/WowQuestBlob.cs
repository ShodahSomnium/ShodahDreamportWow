// iteration 11 - quest map: the world map's quest blob (WorldMapBlobFrame, a QuestPOIFrame drawn by the engine's DrawQuestBlob).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.MapApp
{
    /// <summary>
    /// The quest blob of WorldMapBlobFrame_OnLoad (WorldMapFrame.lua:1911-1917): fill "UI-QuestBlob-Inside" (64 x 64 DXT1, one blue) at
    /// SetFillAlpha(128), border "UI-QuestBlob-Outside" (16 x 16, alpha 220 on row 0 fading to 0 by row 9 - probe QuestMapData) at
    /// SetBorderAlpha(192), SetBorderScalar(1.0). How DrawQuestBlob shapes the area is engine code - ours: each POI area is the polygon of
    /// its points in the server's order, filled as a fan from its point average; the border is a band along every edge, row 0 of the texture
    /// on the edge, fading outwards over BandWidth units (the texture's 16 pixels at the map's scale x the 1.0 scalar).
    /// Points are in the parent's units with the centre as origin and y up (the map panel's marker space).
    /// </summary>
    public sealed class WowQuestBlob : MaskableGraphic
    {
        public Texture2D Tex;
        public bool Border;
        public float BandWidth = 10f;
        private readonly List<Vector2> _pts = new List<Vector2>();
        private readonly List<int> _starts = new List<int>();

        public override Texture mainTexture { get { return Tex != null ? (Texture)Tex : s_WhiteTexture; } }

        public static WowQuestBlob Make(Transform parent, string name, Texture2D tex, bool border, float alpha, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = Vector2.zero;
            WowQuestBlob b = go.AddComponent<WowQuestBlob>();
            b.Tex = tex;
            b.Border = border;
            b.raycastTarget = false;
            b.color = new Color(1f, 1f, 1f, alpha);
            return b;
        }

        public void Clear() { _pts.Clear(); _starts.Clear(); }
        public void Begin() { _starts.Add(_pts.Count); }
        public void Add(Vector2 p) { _pts.Add(p); }
        public int Polygons { get { return _starts.Count; } }
        public void Commit() { SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 c = color;
            for (int s = 0; s < _starts.Count; s++)
            {
                int a = _starts[s], n = (s + 1 < _starts.Count ? _starts[s + 1] : _pts.Count) - a;
                if (n < 3) continue;
                if (!Border)
                {
                    Vector2 m = Vector2.zero;
                    for (int i = 0; i < n; i++) m += _pts[a + i];
                    m /= n;
                    int b0 = vh.currentVertCount;
                    vh.AddVert(m, c, new Vector2(0.5f, 0.5f));
                    for (int i = 0; i < n; i++) vh.AddVert(_pts[a + i], c, new Vector2(0.5f, 0.5f));
                    for (int i = 0; i < n; i++) vh.AddTriangle(b0, b0 + 1 + i, b0 + 1 + (i + 1) % n);
                }
                else
                {
                    float area = 0f;                                                // signed (y up): > 0 = counter-clockwise
                    for (int i = 0; i < n; i++) { Vector2 p = _pts[a + i], q = _pts[a + (i + 1) % n]; area += p.x * q.y - q.x * p.y; }
                    float side = area >= 0f ? 1f : -1f;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 p = _pts[a + i], q = _pts[a + (i + 1) % n], d = q - p;
                        float len = d.magnitude;
                        if (len < 0.01f) continue;
                        Vector2 o = new Vector2(d.y, -d.x) / len * side * BandWidth;  // outward
                        int b0 = vh.currentVertCount;
                        float u = len / Mathf.Max(1f, BandWidth);
                        vh.AddVert(p, c, new Vector2(0f, 0f));
                        vh.AddVert(q, c, new Vector2(u, 0f));
                        vh.AddVert(q + o, c, new Vector2(u, 1f));
                        vh.AddVert(p + o, c, new Vector2(0f, 1f));
                        vh.AddTriangle(b0, b0 + 1, b0 + 2);
                        vh.AddTriangle(b0, b0 + 2, b0 + 3);
                    }
                }
            }
        }
    }
}
