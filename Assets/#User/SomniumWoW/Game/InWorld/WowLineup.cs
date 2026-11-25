using System;
using System.Collections;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using TMPro;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The model line-up at each arrival (this object stands at the arrival point, turned to the avatar's facing; local
    /// +z is "in front"). All ids verified with the harness probes FrontLooks / FrontLineup (reports/front.md):
    /// - 5 characters 5 m ahead, 1.6 m apart, facing the avatar: human male (Worn Shortsword + Worn Wooden Shield), human
    ///   female, orc male (Worn Battleaxe, 2H; cycles Stand/Walk/Run/Attack2H every 3 s), night elf female (Bent Staff),
    ///   tauren male. Looks are CharSections rows that exist (skin/face/hair/underwear) with CharHairGeosets and
    ///   CharacterFacialHairStyles rows. Weapons go in through CharacterLook.ItemDisplayIds (slot 15 main hand,
    ///   16 off hand), which WowCharacter attaches (1 right hand, 0 shield).
    /// - 3 creatures 8.5 m ahead: kobold (CreatureDisplayInfo 26, Creature\Kobold\Kobold), timber wolf (165,
    ///   Creature\Wolf\Wolf), murloc (31, Creature\Murloc\Murloc).
    /// - 4 items on display 3 m ahead at 1.1 m, turning slowly: one-hander Worn Shortsword (item 25 -> display 1542),
    ///   two-hander Battleworn Hammer (2361 -> 8690), staff Bent Staff (35 -> 472), shield Worn Wooden Shield (2362 -> 18730).
    ///   Item ids named after AzerothCore item_template; the display ids come from the game's Item.dbc.
    /// Built one model per frame, each in try/catch: a missing piece becomes a label saying so.
    /// </summary>
    public sealed class WowLineup : MonoBehaviour
    {
        private struct CharacterEntry
        {
            public string Label;
            public CharacterLook Look;
            public bool Cycles;
        }

        private const int SlotMainHand = 15, SlotOffHand = 16;

        // Animation ids: AnimationData.dbc rows 0 Stand, 4 Walk, 5 Run, 18 Attack2H (harness DbcRows AnimationData).
        private static readonly int[] CycleIds = { 0, 4, 5, 18 };
        private static readonly string[] CycleNames = { "Stand", "Walk", "Run", "Attack2H" };

        private WowFiles _files;
        private WowGame _game;
        private readonly List<Transform> _spinners = new List<Transform>();
        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        private M2Animator _cycler;
        private TextMeshPro _cycleLabel;
        private int _cycleIndex;
        private float _nextCycle;
        private int _built, _failed;

        private static int[] Equip(int mainHand, int offHand)
        {
            var slots = new int[19];
            slots[SlotMainHand] = mainHand;
            slots[SlotOffHand] = offHand;
            return slots;
        }

        public void Begin(WowFiles files, WowGame game)
        {
            _files = files;
            _game = game;
            StartCoroutine(Build());
        }

        private IEnumerator Build()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var characters = new[]
            {
                new CharacterEntry { Label = "Human male", Look = new CharacterLook { Race = 1, Gender = 0, Skin = 1, Face = 2, HairStyle = 3, HairColor = 2, FacialHair = 1, ItemDisplayIds = Equip(1542, 18730) } },
                new CharacterEntry { Label = "Human female", Look = new CharacterLook { Race = 1, Gender = 1, Skin = 2, Face = 1, HairStyle = 4, HairColor = 1, FacialHair = 0 } },
                new CharacterEntry { Label = "Orc male", Look = new CharacterLook { Race = 2, Gender = 0, Skin = 1, Face = 1, HairStyle = 2, HairColor = 1, FacialHair = 1, ItemDisplayIds = Equip(22291, 0) }, Cycles = true },
                new CharacterEntry { Label = "Night elf female", Look = new CharacterLook { Race = 4, Gender = 1, Skin = 2, Face = 1, HairStyle = 2, HairColor = 3, FacialHair = 0, ItemDisplayIds = Equip(472, 0) } },
                new CharacterEntry { Label = "Tauren male", Look = new CharacterLook { Race = 6, Gender = 0, Skin = 3, Face = 1, HairStyle = 1, HairColor = 1, FacialHair = 1 } },
            };

            for (int i = 0; i < characters.Length; i++)
            {
                CharacterEntry c = characters[i];
                GameObject go = null;
                string error = null;

                try { go = WowCharacter.Build(_files, c.Look); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }

                Vector3 local = new Vector3((i - 2) * 1.6f, 0f, 5f);
                Place(go, local, true);
                string label = c.Label + (go == null ? "\n(" + (error ?? "not built") + ")" : "");
                TextMeshPro text = Label(label, local, go, go == null ? new Color(1f, 0.4f, 0.3f) : WowUi.GlueGold);

                if (go != null && c.Cycles)
                {
                    _cycler = go.GetComponent<M2Animator>();
                    _cycleLabel = text;
                    _nextCycle = Time.time;
                }

                Count(go, "character " + c.Label, error);
                yield return null;
            }

            var creatures = new[] { new KeyValuePair<string, int>("Kobold (display 26)", 26), new KeyValuePair<string, int>("Timber wolf (display 165)", 165), new KeyValuePair<string, int>("Murloc (display 31)", 31) };

            for (int i = 0; i < creatures.Length; i++)
            {
                GameObject go = null;
                string error = null;

                try { go = WowCreatures.Build(_files, creatures[i].Value); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }

                Vector3 local = new Vector3((i - 1) * 3f, 0f, 8.5f);
                Place(go, local, true);
                Label(creatures[i].Key + (go == null ? "\n(" + (error ?? "not built") + ")" : ""), local, go, go == null ? new Color(1f, 0.4f, 0.3f) : Color.white);
                Count(go, "creature " + creatures[i].Key, error);
                yield return null;
            }

            var items = new[]
            {
                new KeyValuePair<string, int>("One-hander: Worn Shortsword", 1542),
                new KeyValuePair<string, int>("Two-hander: Battleworn Hammer", 8690),
                new KeyValuePair<string, int>("Staff: Bent Staff", 472),
                new KeyValuePair<string, int>("Shield: Worn Wooden Shield", 18730),
            };

            for (int i = 0; i < items.Length; i++)
            {
                GameObject go = null;
                string error = null;

                try { go = WowItems.Build(_files, items[i].Value, false); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }

                // Integrator (lab run r2): 2 m apart, not 1.2 m - the long weapons swept into each other and the labels overlapped.
                Vector3 local = new Vector3((i - 1.5f) * 2f, 0f, 3f);

                if (go != null)
                {
                    // A turning stand at 1.1 m so the item is seen from every side (no guess about its resting axis).
                    var stand = new GameObject("Stand " + items[i].Value).transform;
                    stand.SetParent(transform, false);
                    stand.localPosition = Grounded(local) + new Vector3(0f, 1.1f, 0f);
                    go.transform.SetParent(stand, false);
                    go.transform.localPosition = Vector3.zero;
                    Upright(go.transform, stand, stand.localPosition.y - 1.1f);
                    _spinners.Add(stand);
                }

                TextMeshPro t = Label(items[i].Key.Replace(": ", ":\n") + "\n(display " + items[i].Value + ")" + (go == null ? "\n(" + (error ?? "not built") + ")" : ""), local, null, go == null ? new Color(1f, 0.4f, 0.3f) : Color.white);
                t.transform.localPosition = Grounded(local) + new Vector3(0f, 0.45f, 0f);
                t.fontSize = 1.1f;
                Count(go, "item " + items[i].Key, error);
                yield return null;
            }

            WowLog.Info("Line-up: " + _built + " built, " + _failed + " missing, " + watch.ElapsedMilliseconds + " ms (over " + (characters.Length + creatures.Length + items.Length) + " frames); " + WowTextures.StatsLine());
            try { WowLog.Info("Line-up: " + M2Factory.StatsLine()); } catch (Exception) { } // integrator: Models' request
        }

        private void Count(GameObject go, string what, string error)
        {
            if (go != null) _built++;
            else
            {
                _failed++;
                WowLog.Warn("Line-up: " + what + " not built" + (error != null ? ": " + error : " (null)"));
            }
        }

        /// <summary>Stands a model on the ground at a local position, facing the avatar (local -z).</summary>
        private void Place(GameObject go, Vector3 local, bool faceAvatar)
        {
            if (go == null)
            {
                return;
            }

            go.transform.SetParent(transform, false);
            go.transform.localPosition = Grounded(local);
            // M2 models face +x in WoW = +z in Unity (WowSpace.PointToUnity): turned round to face the avatar at local -z.
            go.transform.localRotation = faceAvatar ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        }

        /// <summary>
        /// Integrator (lab run r2): turns an item so its longest extent (measured from its renderers' bounds in the stand's
        /// space) is vertical, centres it on the stand and lifts the stand so the item clears the ground by 0.15 m. The lab
        /// showed the weapons lying flat and sweeping into each other while turning.
        /// </summary>
        private static void Upright(Transform item, Transform stand, float groundY)
        {
            Renderer[] rs = item.GetComponentsInChildren<Renderer>();

            if (rs.Length == 0)
            {
                return;
            }

            // measure with the stand unrotated, so the world boxes line up with the stand's axes
            Quaternion keep = stand.rotation;
            stand.rotation = Quaternion.identity;
            Bounds b = BoundsIn(rs, stand);
            Vector3 s = b.size;

            if (s.x > s.y && s.x >= s.z)
            {
                item.localRotation = Quaternion.Euler(0f, 0f, 90f) * item.localRotation;
            }
            else if (s.z > s.y && s.z > s.x)
            {
                item.localRotation = Quaternion.Euler(90f, 0f, 0f) * item.localRotation;
            }

            b = BoundsIn(rs, stand);
            item.localPosition -= b.center;
            float lift = groundY + b.extents.y + 0.15f - stand.localPosition.y;
            if (lift > 0f) stand.localPosition += new Vector3(0f, lift, 0f);
            stand.rotation = keep;
        }

        /// <summary>The renderers' world bounds as a box in the space of <paramref name="space"/> (8 corners each).</summary>
        private static Bounds BoundsIn(Renderer[] rs, Transform space)
        {
            bool any = false;
            var b = new Bounds();

            foreach (Renderer r in rs)
            {
                Bounds w = r.bounds;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) == 0 ? w.min.x : w.max.x, (c & 2) == 0 ? w.min.y : w.max.y, (c & 4) == 0 ? w.min.z : w.max.z);
                    Vector3 p = space.InverseTransformPoint(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }

            return b;
        }

        /// <summary>Local position with y on the ground there (collider or terrain height), else the arrival height.</summary>
        private Vector3 Grounded(Vector3 local)
        {
            Vector3 world = transform.TransformPoint(new Vector3(local.x, 0f, local.z));
            float y;

            if (_game != null && _game.GroundAt(world, transform.position.y, out y) && Mathf.Abs(y - transform.position.y) < 4f)
            {
                return new Vector3(local.x, y - transform.position.y, local.z);
            }

            return new Vector3(local.x, 0f, local.z);
        }

        private TextMeshPro Label(string text, Vector3 local, GameObject model, Color color)
        {
            var go = new GameObject("Label " + text.Split('\n')[0]);
            go.transform.SetParent(transform, false);
            float top = 2.3f;

            if (model != null)
            {
                bool any = false;
                Bounds b = new Bounds();

                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }

                if (any)
                {
                    top = Mathf.Clamp(b.max.y - transform.position.y + 0.3f, 0.6f, 6f);
                }
            }

            go.transform.localPosition = Grounded(local) + new Vector3(0f, top, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.font = WowUi.Font;
            t.fontSize = 1.6f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = false;
            t.rectTransform.sizeDelta = new Vector2(4f, 1f);
            t.text = text;

            try
            {
                t.outlineWidth = 0.2f;
                t.outlineColor = new Color32(0, 0, 0, 255);
            }
            catch (Exception)
            {
                // no outline support in the fallback shader
            }

            _labels.Add(t);
            return t;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            foreach (Transform s in _spinners)
            {
                if (s != null) s.Rotate(0f, 30f * dt, 0f, Space.World);
            }

            if (_cycler != null && Time.time >= _nextCycle)
            {
                _nextCycle = Time.time + 3f;
                int id = CycleIds[_cycleIndex % CycleIds.Length];
                string name = CycleNames[_cycleIndex % CycleIds.Length];
                _cycleIndex++;

                try
                {
                    _cycler.Play(id, true);
                    if (_cycleLabel != null) _cycleLabel.text = "Orc male\n" + name + " (animation " + id + ")";
                }
                catch (Exception e)
                {
                    WowLog.Warn("Line-up: M2Animator.Play(" + id + ") failed: " + e.Message);
                    _cycler = null;
                }
            }
        }

        private void LateUpdate()
        {
            // Labels turn to the camera (yaw only), readable on a monitor and in a headset.
            Camera head = WowPlayer.Head();

            if (head == null)
            {
                return;
            }

            Vector3 eye = head.transform.position;

            foreach (TextMeshPro t in _labels)
            {
                if (t == null) continue;
                Vector3 d = t.transform.position - eye;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) t.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }
    }
}
