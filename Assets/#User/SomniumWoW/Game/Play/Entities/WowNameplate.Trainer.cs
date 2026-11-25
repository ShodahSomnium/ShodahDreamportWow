// iteration 11 - owner trainer (proposal-trainer.md 6.2 U11 b): the second plate line of a looted corpse that can be skinned / gathered - WoW
// shows it by the mouse cursor (C code) and the corpse tooltip; VR has no cursor, so (ours) the plate says it with WoW's own tooltip texts
// UNIT_SKINNABLE_LEATHER "Skinnable", _HERB "Requires Herbalism", _ROCK "Requires Mining", _BOLTS "Requires Engineering" (GlobalStrings.lua:7887-7890):
// grey when the character has that skill, red (#ff2020) when not.
using SomniumSpace.Worlds.SomniumWoW.Game.Ui.Pure;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    internal sealed partial class WowNameplate
    {
        private TextMeshProUGUI _trLine;
        private int _trShown = -1;
        private static readonly Color TrHas = new Color(0.5f, 0.5f, 0.5f), TrLacks = new Color(1f, 0.125f, 0.125f);

        partial void TrPlateLine(EntityState s)
        {
            int want = 0;
            bool has = false;
            if (s.Dead && s.Skinnable && !s.Lootable && s.SkinSkill > 0)
            {
                has = TrHasSkill(s.SkinSkill);
                want = s.SkinSkill * 2 + (has ? 1 : 0);
            }
            if (want == _trShown) return;
            _trShown = want;
            if (want == 0) { if (_trLine != null) _trLine.gameObject.SetActive(false); return; }
            if (_trLine == null) _trLine = TrMakeLine();
            _trLine.text = TrText(s.SkinSkill);
            _trLine.color = has ? TrHas : TrLacks;
            _trLine.gameObject.SetActive(true);
        }

        private static string TrText(int skill)
        {
            switch (skill)
            {
                case 182: return UiGlobalStrings.G("UNIT_SKINNABLE_HERB", "Requires Herbalism");
                case 186: return UiGlobalStrings.G("UNIT_SKINNABLE_ROCK", "Requires Mining");
                case 202: return UiGlobalStrings.G("UNIT_SKINNABLE_BOLTS", "Requires Engineering");
                default: return UiGlobalStrings.G("UNIT_SKINNABLE_LEATHER", "Skinnable");
            }
        }

        private static bool TrHasSkill(int skill)
        {
            var p = WowPlay.Session != null ? WowPlay.Session.Player : null;
            if (p == null || p.Skills == null) return false;
            for (int i = 0; i < p.Skills.Count; i++) if (p.Skills[i] != null && p.Skills[i].SkillId == skill) return true;
            return false;
        }

        /// <summary>A line where a living creature's health bar is (the bar is hidden on a corpse).</summary>
        private TextMeshProUGUI TrMakeLine()
        {
            var go = new GameObject("Skinnable", typeof(RectTransform));
            go.layer = WowUi.UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(Go.transform, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(-20f, -16f);
            r.sizeDelta = new Vector2(Width + 40f, 14f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = WowUi.Font;
            if (_name != null && _name.fontSharedMaterial != null) t.fontSharedMaterial = _name.fontSharedMaterial;
            t.fontSize = 9f;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            return t;
        }
    }
}
