using System;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The in-world info (where you are) - shown while you are in the WoW world.
    /// Headset: a small world-space panel on the left wrist (AoM's arm bar placement: hand.position + hand.up x 3.5 cm, lying
    /// along the forearm, face towards whoever turns the wrist up) with the place's name and the zone under you.
    /// Monitor: no label (the user, 2026-09-27: "remove this text above me in non-xr"); only the one-time hint below floats there.
    /// Stage S1 (the user: "Please move the return to room into settings tab"): the wrist's "Back to room" button and the label's
    /// "Press K to return to the room" are gone - the way back is the Settings page's "Return to the room" (owner hud) and, on
    /// the desktop, still the K key (WowPlayer.BackKey, WowGame). The panel has no button left, so it no longer catches the
    /// client's ray (its pointer catcher is off).
    /// Stage S1 (PLAN10 s0.7): the one-time "alone in this instance" hint (Networking/WowNetAlone, due after 60 s alone in the
    /// Fusion session while the whitelist names others) is shown here once you are in the world: for 30 s under the wrist panel
    /// (VR) / above the info label (desktop), plus two lines in the game's yellow message line when a game runs.
    /// Positions are copied every frame; nothing is parented to the avatar (PLAN rule 10).
    /// </summary>
    public sealed class WowWrist : MonoBehaviour
    {
        public const float Width = 320f, Height = 100f, MetresPerUnit = 0.0005f;

        /// <summary>The hint box under the wrist panel (canvas units, from the panel's top).</summary>
        public const float HintTop = 106f, HintHeight = 150f;

        /// <summary>
        /// No longer raised (stage S1: the wrist button is gone; the way back is the Settings page's "Return to the room" and the K
        /// key). Kept so WowGame's subscription compiles.
        /// </summary>
#pragma warning disable 0067
        public event Action BackPressed;
#pragma warning restore 0067

        private Canvas _canvas;
        private TextMeshProUGUI _info;
        private RectTransform _hintBox;
        private TextMeshProUGUI _hintText;
        private TextMeshPro _desktopLabel;
        private TextMeshPro _desktopHint;
        private bool _shown;
        private bool _loggedPlacement;
        private string _place = "", _detail = "";
        private float _hintUntil = -1f;

        public static WowWrist Create(Transform parent)
        {
            var go = new GameObject("Wow Wrist");
            go.transform.SetParent(parent, false);
            return go.AddComponent<WowWrist>();
        }

        public void Show(string place, string detail)
        {
            _place = place ?? "";
            _detail = detail ?? "";
            _shown = true;
            _loggedPlacement = false;
            Build();
            _area = "";
            Refresh();
        }

        private string _area = "";

        /// <summary>The zone under the player (AreaTable name through the ADT chunk), shown under the place's name.</summary>
        public void SetArea(string area)
        {
            _area = area ?? "";
            if (_canvas != null) Refresh();
        }

        private void Refresh()
        {
            string where = _area.Length > 0 && _area != _place ? _place + "\n" + _area : _place;
            _info.text = where;
            _desktopLabel.text = where;
        }

        public void Hide()
        {
            _shown = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (_desktopLabel != null) _desktopLabel.gameObject.SetActive(false);
            if (_desktopHint != null) _desktopHint.gameObject.SetActive(false);
        }

        private void Build()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = WowUi.MakeCanvas("Wow Wrist Panel", transform, Width, Height, MetresPerUnit);
            Transform root = _canvas.transform;

            // Nothing on the panel is pressable any more: let the client's ray pass (the catcher WowUi.MakeCanvas adds).
            Transform catcher = root.Find("Pointer catcher");
            Image catcherImage = catcher != null ? catcher.GetComponent<Image>() : null;
            if (catcherImage != null) catcherImage.raycastTarget = false;

            WowUi.Backdrop(root, 0f, 0f, Width, Height, 24f);
            _info = WowUi.Text(root, "Place", "", 21f, WowUi.GlueGold, TextAlignmentOptions.Center, 18f, 12f, Width - 36f, Height - 24f);

            _hintBox = WowUi.Place(root, "Hint", 0f, HintTop, Width, HintHeight);
            WowUi.Backdrop(_hintBox, 0f, 0f, Width, HintHeight, 24f);
            _hintText = WowUi.Text(_hintBox, "Hint text", WowNetAlone.HintText, 14f, Color.white, TextAlignmentOptions.Center, 18f, 12f, Width - 36f, HintHeight - 24f);
            _hintBox.gameObject.SetActive(false);

            _desktopLabel = Label("Wow Info Label", 2f, WowUi.GlueGold);
            _desktopHint = Label("Wow Hint Label", 1.1f, Color.white);
            _desktopHint.text = WowNetAlone.HintText;
            _desktopHint.gameObject.SetActive(false);
        }

        private TextMeshPro Label(string name, float size, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = WowUi.Font;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = colour;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = false;
            t.rectTransform.sizeDelta = new Vector2(6f, 1f);

            try
            {
                t.outlineWidth = 0.2f;
                t.outlineColor = new Color32(0, 0, 0, 255);
            }
            catch (Exception)
            {
                // fallback shader without outline
            }

            return t;
        }

        private void LateUpdate()
        {
            if (!_shown || _canvas == null)
            {
                return;
            }

            StepHint();
            bool hint = Time.unscaledTime < _hintUntil;

            bool vr = WowPlayer.XrPresent;
            Camera head = WowPlayer.Head();
            Transform hand = vr ? WowPlayer.LeftHand : null;
            bool wrist = vr && hand != null;
            _canvas.gameObject.SetActive(wrist);
            _desktopLabel.gameObject.SetActive(false);   // the user (2026-09-27): no place label over the head on the desktop (the hint stays)
            _desktopHint.gameObject.SetActive(!vr && hint);
            if (_hintBox.gameObject.activeSelf != hint) _hintBox.gameObject.SetActive(hint);

            if (!vr)
            {
                Vector3 feet;

                if (WowPlayer.TryFeet(out feet))
                {
                    _desktopLabel.transform.position = feet + new Vector3(0f, 2.35f, 0f);
                    _desktopHint.transform.position = feet + new Vector3(0f, 2.95f, 0f);

                    if (head != null)
                    {
                        Vector3 d = _desktopLabel.transform.position - head.transform.position;
                        d.y = 0f;
                        if (d.sqrMagnitude > 1e-4f)
                        {
                            Quaternion face = Quaternion.LookRotation(d, Vector3.up);
                            _desktopLabel.transform.rotation = face;
                            _desktopHint.transform.rotation = face;
                        }
                    }
                }

                return;
            }

            if (!wrist)
            {
                return;
            }

            _canvas.worldCamera = head;
            Transform t = _canvas.transform;
            t.position = hand.position + hand.up * 0.035f;
            // A canvas is read from the side its forward points away from: forward into the arm (AomVr.cs:635-643).
            t.rotation = Quaternion.LookRotation(-hand.up, hand.forward);

            if (!_loggedPlacement)
            {
                _loggedPlacement = true;
                WowLog.Info("Wrist: panel on the left hand '" + hand.name + "' (VR).");
            }
        }

        /// <summary>The one-time "alone in this instance" hint: WowNet's rule says it is due, and we are in the world (shown).</summary>
        private void StepHint()
        {
            WowNetAlone alone = WowNet.Alone;
            if (alone == null || !alone.Due)
            {
                return;
            }

            alone.MarkShown();
            float now = Time.unscaledTime;
            _hintUntil = now + WowNetAlone.HintSeconds;
            bool game = WowPlay.Session != null;
            if (game)
            {
                // The game's message line puts the newest on top: the lower line first.
                WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = WowNetAlone.MessageBelow });
                WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = WowNetAlone.MessageTop });
            }

            WowLog.Info("Wrist: the one-time hint 'alone in this Somnium instance' is shown for " + WowNetAlone.HintSeconds.ToString("0") + " s ("
                        + (WowPlayer.XrPresent ? "under the wrist panel" : "above the info label") + (game ? " + the game's message line" : "") + ").");
        }
    }
}
