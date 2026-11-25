using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>
    /// Floating combat text from a fixed pool (no allocations after the pool is built: numbers go through TMP's
    /// SetText("{0}", n)). Over an entity: the number rises off it and fades (white melee, yellow crits bigger, green heals,
    /// grey misses - the task's colours). Near the player's view: damage taken / heals / misses in the colours of the game's
    /// own Blizzard_CombatText.lua (DAMAGE 1/0.1/0.1, SPELL_DAMAGE 0.79/0.3/0.85, HEAL 0.1/1/0.1; scroll 1.9 s, fade from 1.3 s),
    /// "-N" / "+N" as that file formats them. Each item is a small world-space canvas (both eyes) billboarded in LateUpdate.
    /// </summary>
    internal sealed class WowCombatText
    {
        public static readonly Color White = new Color(1f, 1f, 1f);
        public static readonly Color Yellow = new Color(1f, 1f, 0f);
        public static readonly Color Green = new Color(0.1f, 1f, 0.1f);
        public static readonly Color Grey = new Color(0.7f, 0.7f, 0.7f);
        public static readonly Color SelfDamage = new Color(1f, 0.1f, 0.1f);      // COMBAT_TEXT_TYPE_INFO["DAMAGE"]
        public static readonly Color SelfSpell = new Color(0.79f, 0.3f, 0.85f);   // ["SPELL_DAMAGE"]
        public static readonly Color SelfHeal = new Color(0.1f, 1f, 0.1f);        // ["HEAL"]

        private const float ScrollTime = 1.9f;   // COMBAT_TEXT_SCROLLSPEED
        private const float FadeFrom = 1.3f;     // COMBAT_TEXT_FADEOUT_TIME
        private const float EntityLife = 1.5f;

        private sealed class Item
        {
            public GameObject Go;
            public TextMeshProUGUI Text;
            public CanvasGroup Group;
            public bool Active, Self, Crit;
            public float T, Life, Rise, Size;
            public Vector3 Start, Side;
            public int Serial;
        }

        private readonly Item[] _items;
        private readonly Transform _root;
        private readonly WowEntityArt _art;
        private int _made;
        private int _serial;

        /// <summary>Floating origin (WowSpace.Rebased): the texts rise from Start, which moves with the world.</summary>
        public void OnRebased(Vector3 d)
        {
            foreach (var it in _items) if (it != null) it.Start -= d;
        }

        public WowCombatText(Transform parent, WowEntityArt art, int poolSize)
        {
            _root = new GameObject("Combat text").transform;
            _root.SetParent(parent, false);
            _items = new Item[poolSize];
            _art = art;   // items are made on first use (spread over the first hits instead of one frame)
        }

        private Item Make(WowEntityArt art, int i)
        {
            var it = new Item();
            it.Go = new GameObject("Combat text " + i);
            it.Go.layer = Ui.UiKit.Layer;
            Ui.UiSharp.Register(it.Go);   // lead 2026-10-10
            it.Go.transform.SetParent(_root, false);
            var canvas = it.Go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            it.Go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            it.Group = it.Go.AddComponent<CanvasGroup>();
            it.Group.interactable = false;
            it.Group.blocksRaycasts = false;
            var rt = (RectTransform)it.Go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200f, 40f);
            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.layer = Ui.UiKit.Layer;
            var trt = (RectTransform)tgo.transform;
            trt.SetParent(it.Go.transform, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            it.Text = tgo.AddComponent<TextMeshProUGUI>();
            it.Text.font = WowUi.Font;
            var mat = art.TextMaterial;
            if (mat != null) it.Text.fontSharedMaterial = mat;
            it.Text.alignment = TextAlignmentOptions.Center;
            it.Text.textWrappingMode = TextWrappingModes.NoWrap;
            it.Text.overflowMode = TextOverflowModes.Overflow;
            it.Text.richText = false;
            it.Text.raycastTarget = false;
            it.Text.fontSize = 24f;
            it.Go.SetActive(false);
            return it;
        }

        private Item Take()
        {
            Item best = null;
            for (int i = 0; i < _made; i++)
            {
                var it = _items[i];
                if (!it.Active) { best = it; break; }
                if (best == null || it.Serial < best.Serial) best = it;   // all busy: reuse the oldest
            }
            if ((best == null || best.Active) && _made < _items.Length)
            {
                best = Make(_art, _made);
                _items[_made++] = best;
            }
            best.Active = true;
            best.Serial = ++_serial;
            best.T = 0f;
            if (!best.Go.activeSelf) best.Go.SetActive(true);
            return best;
        }

        /// <summary>A number rising off an entity (worldPos = above its model). prefix: 0 none, '+' or '-'.</summary>
        public void Number(Vector3 worldPos, int amount, char prefix, Color color, bool crit, bool self)
        {
            var it = Take();
            if (prefix == '+') it.Text.SetText("+{0}", amount);
            else if (prefix == '-') it.Text.SetText("-{0}", amount);
            else it.Text.SetText("{0}", amount);
            Setup(it, worldPos, color, crit, self);
        }

        /// <summary>A word (Miss, Dodge, Parry, Resist, Absorb...) at worldPos.</summary>
        public void Word(Vector3 worldPos, string word, Color color, bool self)
        {
            var it = Take();
            it.Text.text = word ?? "";
            Setup(it, worldPos, color, false, self);
        }

        private void Setup(Item it, Vector3 worldPos, Color color, bool crit, bool self)
        {
            it.Self = self;
            it.Crit = crit;
            it.Text.color = color;
            it.Text.fontSize = crit ? 34f : 24f;
            it.Size = crit ? 1.25f : 1f;
            it.Life = self ? ScrollTime : EntityLife;
            it.Rise = self ? 0.35f : 0.9f;
            // Stagger sideways a little so a burst of hits does not stack into one unreadable spot (isStaggered in the addon).
            it.Side = new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(-0.05f, 0.1f), Random.Range(-0.18f, 0.18f));
            it.Start = worldPos + it.Side;
            it.Group.alpha = 1f;
            it.Go.transform.position = it.Start;
        }

        /// <summary>Moves, fades and billboards the live items. LateUpdate.</summary>
        public void Tick(float dt, Camera cam)
        {
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            for (int i = 0; i < _made; i++)
            {
                var it = _items[i];
                if (!it.Active) continue;
                it.T += dt;
                if (it.T >= it.Life)
                {
                    it.Active = false;
                    it.Go.SetActive(false);
                    continue;
                }
                float u = it.T / it.Life;
                Vector3 p = it.Start + Vector3.up * (it.Rise * u);
                var t = it.Go.transform;
                t.position = p;
                float fadeFrom = it.Self ? FadeFrom : EntityLife * 0.55f;
                it.Group.alpha = it.T <= fadeFrom ? 1f : Mathf.Clamp01(1f - (it.T - fadeFrom) / (it.Life - fadeFrom));
                float dist = cam != null ? Vector3.Distance(camPos, p) : 5f;
                // Crits pop (1.6x -> 1x over 0.15 s) like WoW's crit numbers.
                float pop = it.Crit && it.T < 0.15f ? Mathf.Lerp(1.6f, 1f, it.T / 0.15f) : 1f;
                float s = Mathf.Clamp(dist * 0.0014f, 0.0014f, 0.07f) * pop;   // ~1.9 degrees per 24-unit number, constant 1..50 m (lab e1: 0.0009 read too small)
                t.localScale = new Vector3(s, s, s);
                if (cam != null)
                {
                    Vector3 away = p - camPos;
                    if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, cam.transform.up);
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _made; i++) { _items[i].Active = false; _items[i].Go.SetActive(false); }
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
