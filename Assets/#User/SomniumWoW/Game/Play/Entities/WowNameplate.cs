using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>Colours the entity views use (sources in the Entities report).</summary>
    internal static class WowEntityColors
    {
        // In-world unit colours (the engine's unit selection colours; not in the Lua files - the values nameplate addons
        // test for): hostile red, neutral yellow, friendly green. Used for names, the nameplate bar and the selection circle.
        public static readonly Color Hostile = new Color(1f, 0f, 0f);
        public static readonly Color Neutral = new Color(1f, 1f, 0f);
        public static readonly Color Friendly = new Color(0f, 1f, 0f);
        public static readonly Color Dead = new Color(0.5f, 0.5f, 0.5f);
        // Iteration 6 (online): other WoW players and gameobjects. A friendly player is the engine's blue unit selection colour (the
        // value nameplate addons test for "friendly player": r < .01, g < .01, b > .99); hostile/neutral players as creatures. A
        // gameobject has no reaction: its name is drawn white as the tooltip header draws it.
        public static readonly Color FriendlyPlayer = new Color(0f, 0f, 1f);
        public static readonly Color ObjectName = new Color(1f, 1f, 1f);

        /// <summary>The name / bar / selection-circle colour of an entity (kind and reaction; the Dead grey is applied by the callers).</summary>
        public static Color For(EntityState s)
        {
            if (s.Kind == EntityKind.GameObject) return ObjectName;
            if (s.Kind == EntityKind.Player && s.Reaction == Play.Reaction.Friendly) return FriendlyPlayer;
            return Reaction(s.Reaction);
        }

        // Interface\FrameXML\Constants.lua:404-408 QuestDifficultyColors: impossible, verydifficult, difficult, standard, trivial.
        private static readonly Color[] Con =
        {
            new Color(0.50f, 0.50f, 0.50f), // 0 trivial (gray)
            new Color(0.25f, 0.75f, 0.25f), // 1 standard (green)
            new Color(1.00f, 1.00f, 0.00f), // 2 difficult (yellow)
            new Color(1.00f, 0.50f, 0.25f), // 3 verydifficult (orange)
            new Color(1.00f, 0.10f, 0.10f), // 4 impossible (red)
        };

        public static Color Reaction(Reaction r)
        {
            switch (r)
            {
                case Play.Reaction.Hostile: return Hostile;
                case Play.Reaction.Neutral: return Neutral;
                default: return Friendly;
            }
        }

        public static Color ConColor(int con)
        {
            return Con[Mathf.Clamp(con, 0, Con.Length - 1)];
        }
    }

    /// <summary>
    /// A world-space WoW nameplate over one entity: the name in its reaction colour, then for creatures WoW's own nameplate
    /// border (Interface\Tooltips\Nameplate-Border.blp, 128x32) with the health bar in its measured interior (x 5..105,
    /// y 20..26) and the level in the border's box (x 107..124) coloured by difficulty; for NPCs the &lt;SubName&gt; line.
    /// The glow (Nameplate-Glow.blp) marks the target / the unit under the pointer. Canvas units = texture pixels. Billboarded
    /// to the camera and scaled with distance (near constant angular size), drawn by a world-space canvas (both eyes).
    /// Texts only change when the value changes (no per-frame allocations).
    /// </summary>
    internal sealed partial class WowNameplate
    {
        // Iteration 11 (w0game, wave 0): partial-method hooks the owners implement in their own new partial file of this class (e.g.
        // Game/Play/Entities/WowNameplate.Trainer.cs) as `partial void Name(EntityState s) { ... }`, once each; called from Refresh every
        // frame for every plate (keep them cheap: act on a change). Until implemented the compiler removes the calls (inert).
        /// <summary>trainer U11 (b): the second line of a dead unit that is Skinnable and not Lootable (Skinnable / Herbalism / Mining / Engineering).</summary>
        partial void TrPlateLine(EntityState s);
        /// <summary>socialui H12 (optional): "&lt;GuildName&gt;" under a player's name.</summary>
        partial void SocialPlateLine(EntityState s);

        public const float Width = 128f;
        public const float Height = 46f;

        public readonly GameObject Go;
        private readonly RectTransform _rt;
        private readonly CanvasGroup _group;
        private readonly TextMeshProUGUI _name, _sub, _level;
        private readonly GameObject _bar;
        private readonly RawImage _fill, _glow;
        private readonly RectTransform _fillRt;
        private readonly bool _npc;

        private string _lastName, _lastSub;
        private int _lastHealth = -1, _lastMax = -1, _lastLevel = -1, _lastCon = -1, _lastReaction = -1;
        private int _lastState = -1;   // 0 alive, 1 dead
        private int _lastGlow = -1;

        private static readonly Rect FullUv = new Rect(0f, 1f, 1f, -1f);   // rule 15: BLP rows as stored -> flip v for UI

        public WowNameplate(Transform parent, WowEntityArt art, bool npc)
        {
            _npc = npc;
            Go = new GameObject("Nameplate");
            Go.layer = Ui.UiKit.Layer;
            Go.transform.SetParent(parent, false);
            Ui.UiSharp.Register(Go);   // lead 2026-10-10: after DLSS with the VR interface
            var canvas = Go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = Go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            _group = Go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _rt = (RectTransform)Go.transform;
            _rt.pivot = new Vector2(0.5f, 0f);
            _rt.sizeDelta = new Vector2(Width, Height);
            _rt.localScale = Vector3.one * 0.003f;

            if (npc)
            {
                _name = Text(art, "Name", 0f, 16f, Width, 16f, 11f);
                _sub = Text(art, "SubName", -20f, 30f, Width + 40f, 14f, 9f);
            }
            else
            {
                _name = Text(art, "Name", -20f, 0f, Width + 40f, 16f, 11f);
                _bar = new GameObject("Bar", typeof(RectTransform));
                _bar.layer = Ui.UiKit.Layer;
                var barRt = (RectTransform)_bar.transform;
                barRt.SetParent(Go.transform, false);
                Anchor(barRt, 0f, 14f, Width, 32f);
                // Glow first (a halo behind everything: drawn over the bar it washed the health colour out - lab e1),
                // then the fill (under the border), the border, and the level text in the border's box.
                _glow = Picture(_bar.transform, "Glow", art.PlateGlow, -10f, -7f, Width + 20f, 46f);
                _glow.color = new Color(1f, 0.95f, 0.6f, 0.95f);
                _glow.gameObject.SetActive(false);
                _fill = Picture(_bar.transform, "Health", art.BarFill, 5f, 19.5f, 101f, 8f);
                _fillRt = _fill.rectTransform;
                Picture(_bar.transform, "Border", art.PlateBorder, 0f, 0f, Width, 32f);
                _level = Text(art, "Level", 105f, 17f, 22f, 12f, 8.5f);
                _level.transform.SetParent(_bar.transform, false);
            }
        }

        private TextMeshProUGUI Text(WowEntityArt art, string name, float x, float y, float w, float h, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = Ui.UiKit.Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(Go.transform, false);
            Anchor(r, x, y, w, h);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = WowUi.Font;
            var mat = art.TextMaterial;
            if (mat != null) t.fontSharedMaterial = mat;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = false;
            t.raycastTarget = false;
            t.text = "";
            return t;
        }

        private static RawImage Picture(Transform parent, string name, Texture tex, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = Ui.UiKit.Layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            Anchor(r, x, y, w, h);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.uvRect = FullUv;
            img.raycastTarget = false;
            if (tex == null && name != "Health") img.color = new Color(0f, 0f, 0f, 0f);   // art missing: the fill alone (plain colour) stays readable
            return img;
        }

        /// <summary>Top-left anchored rect at (x, y) units from the parent's top-left.</summary>
        private static void Anchor(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Updates texts/bars from the state (only what changed). glow: target or hovered.</summary>
        public void Refresh(EntityState s, bool glow)
        {
            Refresh(s, glow, s.Name);
        }

        /// <summary>As Refresh, with the name line given (a pet's "&lt;Owner&gt;'s Imp"); pass the same string instance each frame.</summary>
        public void Refresh(EntityState s, bool glow, string name)
        {
            int state = s.Dead ? 1 : 0;
            int reaction = (int)s.Reaction + 4 * (s.Kind == EntityKind.Player ? 1 : s.Kind == EntityKind.GameObject ? 2 : 0);
            if (!ReferenceEquals(name, _lastName)) { _lastName = name; _name.text = name ?? ""; }
            if (state != _lastState || reaction != _lastReaction)
            {
                _lastState = state;
                _lastReaction = reaction;
                Color c = s.Dead ? WowEntityColors.Dead : WowEntityColors.For(s);
                _name.color = c;
                if (_sub != null) _sub.color = c;
                if (_fill != null) _fill.color = c;
                if (_bar != null) _bar.SetActive(!s.Dead);
            }
            if (_sub != null && !ReferenceEquals(s.SubName, _lastSub))
            {
                _lastSub = s.SubName;
                _sub.text = string.IsNullOrEmpty(s.SubName) ? "" : "<" + s.SubName + ">";
            }
            TrPlateLine(s);       // iteration 11 (trainer U11 b; partial, inert until trainer)
            SocialPlateLine(s);   // iteration 11 (socialui H12; partial, inert until socialui)
            if (_bar == null) return;
            if (s.Health != _lastHealth || s.MaxHealth != _lastMax)
            {
                _lastHealth = s.Health;
                _lastMax = s.MaxHealth;
                float f = s.MaxHealth > 0 ? Mathf.Clamp01((float)s.Health / s.MaxHealth) : 0f;
                _fillRt.sizeDelta = new Vector2(101f * f, 8f);
                _fill.uvRect = new Rect(0f, 1f, Mathf.Max(0.001f, f), -1f);
                _fill.enabled = f > 0f;
            }
            if (s.Level != _lastLevel) { _lastLevel = s.Level; _level.SetText("{0}", s.Level); }
            if (s.ConColor != _lastCon) { _lastCon = s.ConColor; _level.color = WowEntityColors.ConColor(s.ConColor); }
            int g = glow ? 1 : 0;
            if (g != _lastGlow && _glow != null) { _lastGlow = g; _glow.gameObject.SetActive(glow && _glow.texture != null); }
        }

        /// <summary>Stands the plate at anchor, facing the camera, scaled for its distance. LateUpdate.</summary>
        public void Place(Vector3 anchor, Vector3 cameraPos, float distance)
        {
            var t = Go.transform;
            t.position = anchor;
            Vector3 away = anchor - cameraPos;
            if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
            float s = ScaleFor(distance);
            t.localScale = new Vector3(s, s, s);
        }


        /// <summary>
        /// Metres per canvas unit at a distance: about 0.07 degrees per unit (the 128-unit plate ~9 degrees wide, an 11-unit
        /// name ~0.75 degrees - WoW's on-screen plate size x1.5 for a headset), the SAME angular size from 1 m to 50 m
        /// (iteration 3: the old 0.003..0.03 clamp made plates big up close and small beyond 25 m).
        /// (Lab run e1 at 0.0008/m looked like WoW on a monitor but too small to read in VR.)
        /// </summary>
        public static float ScaleFor(float distance)
        {
            return Mathf.Clamp(distance * 0.0012f, 0.0012f, 0.06f);
        }

        /// <summary>Canvas units from the plate's bottom edge to the top of what it shows: an NPC plate's name starts 16 units
        /// below the rect's top (name 16..32, subname 30..44), a creature plate's name at the very top.</summary>
        public float VisibleUnits { get { return _npc ? Height - 16f : Height; } }

        /// <summary>Canvas units of air between the top of the head and the plate's bottom edge (constant angle, ~0.4 degrees).</summary>
        public const float GapUnits = 6f;

        /// <summary>Metres from the top of the head to the plate's bottom edge at this distance: 4 cm + GapUnits at the plate's scale.</summary>
        public static float GapFor(float distance)
        {
            return 0.04f + GapUnits * ScaleFor(distance);
        }
        public void SetVisible(bool on)
        {
            if (Go.activeSelf != on) Go.SetActive(on);
        }

        public float Alpha
        {
            set { if (!Mathf.Approximately(_group.alpha, value)) _group.alpha = value; }
        }
    }
}
