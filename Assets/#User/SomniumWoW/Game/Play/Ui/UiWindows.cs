using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>A window of the game UI (spellbook, quest log, character, bags, the NPC quest frame, loot, the death popup).</summary>
    public abstract class UiWindow
    {
        public RectTransform Root;
        protected UiContext Ctx;
        public float W = 384f, H = 512f;
        protected static readonly StringBuilder Sb = new StringBuilder(1024);

        public bool IsOpen { get { return Root != null && Root.gameObject.activeSelf; } }

        public void SetOpen(bool on)
        {
            if (Root == null || Root.gameObject.activeSelf == on) return;
            Root.gameObject.SetActive(on);
            if (on) OnOpen();
            else OnClose();
        }

        protected virtual void OnOpen() { }
        protected virtual void OnClose() { }
        public abstract void Refresh(float now);

        /// <summary>The standard portrait circle of the 384x512 panels (QuestFramePortrait: 60x60 at (7, 6)).</summary>
        protected RawImage Portrait(Texture2D tex)
        {
            return UiKit.Pic(Root, "Portrait", tex, 7f, 6f, 60f, 60f);
        }

        protected TextMeshProUGUI Title(string text, float y)
        {
            TextMeshProUGUI t = UiKit.Text(Root, "Title", text, 13f, UiArt.Gold, TextAlignmentOptions.Center, 78f, y, 240f, 17f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>A slot button's icon: loads (possibly over a few frames) and shows; returns true once shown.</summary>
        protected static bool SetIcon(UiButton b, string path, ref string shownPath, ref bool loaded)
        {
            if (!ReferenceEquals(path, shownPath)) { shownPath = path; loaded = false; }
            if (loaded) return true;
            Texture2D t = UiArt.Icon(path);
            if (t == null) return false;
            b.Icon.texture = t;
            b.Icon.color = Color.white;
            b.Icon.enabled = true;
            UiKit.Coords(b.Icon, 0f, 1f, 0f, 1f);
            loaded = true;
            return true;
        }
    }

    /// <summary>Money as WoW's MoneyFrame draws it: numbers with the UI-MoneyIcons coins (gold 0-0.25, silver 0.25-0.5, copper 0.5-0.75: MoneyFrame.xml).</summary>
    public sealed class UiMoney
    {
        private readonly TextMeshProUGUI[] _n = new TextMeshProUGUI[3];
        private readonly RawImage[] _i = new RawImage[3];
        private int _shown = -1;

        public UiMoney(Transform parent, float right, float y, Color textColor)
        {
            Texture2D coins = UiArt.Tex(UiArt.MoneyIcons);
            for (int k = 0; k < 3; k++)
            {
                float x = right - 13f - (2 - k) * 44f;
                _i[k] = UiKit.Pic(parent, "Coin " + k, coins, k * 0.25f, k * 0.25f + 0.25f, 0f, 1f, x, y + 1f, 13f, 13f);
                _n[k] = UiKit.Text(parent, "Coins " + k, "", 11f, textColor, TextAlignmentOptions.Right, x - 31f, y, 30f, 15f, UiArt.Numbers);
                _n[k].textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        public void Set(int copper)
        {
            if (copper == _shown) return;
            _shown = copper;
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            _n[0].SetText("{0}", g); _n[1].SetText("{0}", s); _n[2].SetText("{0}", c);
            _n[0].enabled = _i[0].enabled = g > 0;
            _n[1].enabled = _i[1].enabled = g > 0 || s > 0;
        }
    }
}
