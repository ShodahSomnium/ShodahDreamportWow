using SomniumSpace.Worlds.SomniumWoW.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// WoW's loading screen on a big world-space panel in the room: the map's picture (Map.dbc -> LoadingScreens.dbc,
    /// the "...Wide.blp" file where the row has HasWideScreen) with the game's loading bar art over its lower part,
    /// the fill driven by WowMapView.Progress01.
    /// Aspect: the Wide picture is shown at 16:10. Measured on LoadScreenEasternKingdom(Wide).blp: the WoW logo is
    /// ~355 x 190 px in the 4:3 picture and ~300 x 195 px in the Wide one; the same logo shape needs 300 A / 195 =
    /// 355 x (4/3) / 190, A = 1.62. The normal picture is shown at 4:3.
    /// Bar: Loading-BarBorder.blp 512 x 64, its transparent window measured (probe FrontAlpha) at x 37..474, rows 24..38.
    /// </summary>
    public sealed class WowLoadingScreen : MonoBehaviour
    {
        public const float Height = 1000f, MetresPerUnit = 0.0025f;
        public static readonly Vector3 Centre = new Vector3(0f, 1.85f, 4.3f);

        private Canvas _canvas;
        private RectTransform _fill;
        private RawImage _spark;
        private TextMeshProUGUI _caption;
        private float _fillX, _fillY, _fillW, _fillH;
        private string _title = "";

        public static WowLoadingScreen Create(Transform room)
        {
            var go = new GameObject("Wow Loading Screen");
            go.transform.SetParent(room, false);
            return go.AddComponent<WowLoadingScreen>();
        }

        /// <summary>Shows the loading picture of mapId with the title (e.g. "Goldshire - Eastern Kingdoms").</summary>
        public void Show(WowFiles files, int mapId, string title)
        {
            Hide();
            _title = title ?? "";
            string why = null;
            string path = files != null ? WowLocations.LoadingScreenOf(files, mapId, true, out why) : null;
            if (files == null) why = "no game files";
            Texture2D pic = null;

            if (path != null)
            {
                try { pic = WowTextures.Get(files, path, WowTextureOptions.Ui); }
                catch (System.Exception e) { why += ", " + e.Message; }
            }

            bool wide = path != null && path.EndsWith("Wide.blp", System.StringComparison.OrdinalIgnoreCase);
            float width = wide ? Height * 1.6f : Height * 4f / 3f;
            WowLog.Info("Loading screen: map " + mapId + " -> " + (path ?? "none") + " (" + why + "), shown " + (wide ? "16:10" : "4:3") + " " + (width * MetresPerUnit).ToString("0.0") + " x " + (Height * MetresPerUnit).ToString("0.0") + " m.");

            _canvas = WowUi.MakeCanvas("Wow Loading Panel", transform, width, Height, MetresPerUnit);
            _canvas.transform.localPosition = Centre;
            _canvas.transform.localRotation = Quaternion.identity;
            var placer = _canvas.gameObject.AddComponent<WowPanelPlacer>();
            placer.HeightMetres = Height * MetresPerUnit;
            placer.VrDistance = Centre.z;
            placer.What = "loading screen";
            Transform root = _canvas.transform;

            RawImage p = WowUi.Picture(root, "Picture", pic, new Rect(0f, 0f, 1f, 1f), 0f, 0f, width, Height);

            if (pic == null)
            {
                p.color = new Color(0.05f, 0.04f, 0.03f, 1f);
            }

            // The bar: 512 x 64 art at 1.6x, centred, its bottom 60 units above the picture's bottom.
            float bw = 512f * 1.6f, bh = 64f * 1.6f, bx = (width - bw) / 2f, by = Height - 60f - bh;
            _fillX = bx + 37f * 1.6f;
            _fillY = by + 24f * 1.6f;
            _fillW = (475f - 37f) * 1.6f;
            _fillH = 15f * 1.6f;
            Texture2D fillTex = WowUi.Tex(WowUi.BarFill);
            RawImage fill = WowUi.Picture(root, "Bar Fill", fillTex, new Rect(0f, 0f, 1f, 1f), _fillX, _fillY, 0f, _fillH);

            if (fillTex == null)
            {
                fill.color = new Color(0.1f, 0.35f, 0.8f, 1f);
            }

            _fill = fill.rectTransform;
            _spark = WowUi.Picture(root, "Bar Spark", WowUi.Tex(WowUi.BarSpark), new Rect(0f, 0f, 1f, 1f), _fillX - 16f, _fillY - 16f, 32f, _fillH + 32f);
            WowUi.Picture(root, "Bar Border", WowUi.Tex(WowUi.BarBorder), new Rect(0f, 0f, 1f, 1f), bx, by, bw, bh);
            _caption = WowUi.Text(root, "Caption", "", 34f, WowUi.GlueGold, TextAlignmentOptions.Center, bx - 200f, by - 58f, bw + 400f, 50f);
            SetProgress(0f, null);
        }

        public void SetProgress(float progress01, string extra)
        {
            if (_canvas == null)
            {
                return;
            }

            float p = Mathf.Clamp01(progress01);
            _fill.sizeDelta = new Vector2(_fillW * p, _fillH);
            _spark.rectTransform.anchoredPosition = new Vector2(_fillX + _fillW * p - 16f, -(_fillY - 16f));
            _spark.gameObject.SetActive(p > 0.001f && p < 0.999f && _spark.texture != null);
            _caption.text = "Loading " + _title + "   " + Mathf.RoundToInt(p * 100f) + " %" + (string.IsNullOrEmpty(extra) ? "" : "   " + extra);
        }

        public void Hide()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
        }

        private void LateUpdate()
        {
            if (_canvas != null)
            {
                _canvas.worldCamera = WowPlayer.Head();
            }
        }
    }
}
