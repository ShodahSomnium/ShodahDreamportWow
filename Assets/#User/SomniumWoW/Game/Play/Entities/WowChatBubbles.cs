using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Entities
{
    /// <summary>
    /// Stage S1 (owner chat, PLAN10 s2): WoW-style chat bubbles over whoever speaks - the session's ChatMessage events of the kinds
    /// Say / Yell / Party / Emote / MonsterSay / MonsterYell / MonsterEmote. The bubble stands over the speaker's entity view (above its
    /// nameplate: WowEntityViews.TryGetTop + WowNameplate's gap and height at that distance) or, for another Somnium player's WoW
    /// character, over that player's AVATAR (WowGear.TryAvatarSlot Head - their character is not drawn, the avatar stands there; nothing
    /// while the avatar is not drawn here). It shows the words in the chat type's colour on WoW's own bubble art
    /// (Interface\Tooltips\ChatBubble-Background / -Backdrop (256 x 32: the eight 32 px backdrop pieces) / -Tail), faces the camera and keeps
    /// the nameplates' angular size (WowNameplate.ScaleFor) - world-anchored, never head-locked. One bubble per speaker (a new line
    /// replaces it), Seconds long. No bubble for the local player (in a headset it would float over the own head) and none for visitors:
    /// only a whitelisted player's online session produces chat events, and a visitor is never a WoW speaker.
    /// Ours: Seconds (PLAN10), FadeSeconds, MaxDistance, MaxBubbles, the wrap width, the edge / inset / tail sizes; emote bubbles (PLAN10 -
    /// the real client bubbles say, yell, party and creature lines only).
    /// Order 30600: after GearShift (30000) has moved the remote avatars into our frame (and after WowTabletMap 30500).
    /// </summary>
    [DefaultExecutionOrder(30600)]
    public sealed class WowChatBubbles : MonoBehaviour
    {
        public const float Seconds = 8f;            // PLAN10 s2 (ours)
        public const float FadeSeconds = 1f;        // ours: the last second fades
        public const float MaxDistance = 100f;      // ours, metres (the server delivers say within 25 yd = 22.9 m, CONFIG_LISTEN_RANGE_SAY; yell 300 yd)
        public const int MaxBubbles = 16;           // ours
        public const bool Emotes = true;            // PLAN10 s2 (ours - WoW's client does not bubble emotes)
        const float TextSize = 11f;                 // ours: the nameplate name's size (WowNameplate, 11 units)
        const float MaxTextWidth = 170f;            // ours: canvas units before a line wraps
        const float Edge = 12f, Inset = 4f, Tile = 16f, Pad = 9f, TailSize = 16f, TailX = 10f;   // ours: the art at ~0.4 of its pixels
        const float AvatarAbove = 0.3f;             // ours: metres above a Somnium avatar's head anchor (Head slot = head + 0.1 m)

        const string BubbleBg = @"Interface\Tooltips\ChatBubble-Background.blp";
        const string BubbleEdge = @"Interface\Tooltips\ChatBubble-Backdrop.blp";
        const string BubbleTail = @"Interface\Tooltips\ChatBubble-Tail.blp";

        sealed class Bubble
        {
            public ulong Guid;
            public GameObject Go;
            public RectTransform Rt;
            public TextMeshProUGUI Text;
            public UiBackdrop Back;
            public RawImage Tail;
            public CanvasGroup Group;
            public float Born;
            public float W, H;
        }

        private readonly Dictionary<ulong, Bubble> _active = new Dictionary<ulong, Bubble>();
        private readonly List<Bubble> _free = new List<Bubble>();
        private readonly List<ulong> _drop = new List<ulong>();
        private bool _subscribed, _loggedFirst, _loggedAvatar;
        private int _shown, _skippedNoView;

        private void OnEnable()
        {
            if (_subscribed) return;
            WowPlay.Event += OnGameEvent;
            WowPlay.Ended += Clear;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                WowPlay.Event -= OnGameEvent;
                WowPlay.Ended -= Clear;
                _subscribed = false;
            }
            Clear();
        }

        private void Clear()
        {
            foreach (Bubble b in _active.Values) Recycle(b);
            _active.Clear();
        }

        private static bool Bubbles(ChatKind k)
        {
            switch (k)
            {
                case ChatKind.Say:
                case ChatKind.Yell:
                case ChatKind.Party:
                // iteration 11 (socialui, proposal-social.md 6.5.4): WoW's chatBubblesParty (CHAT_BUBBLES_PARTY) covers the party and raid
                // lines; a raid boss emote bubbles like a creature emote.
                case ChatKind.PartyLeader:
                case ChatKind.Raid:
                case ChatKind.RaidLeader:
                case ChatKind.RaidBossEmote:
                case ChatKind.MonsterSay:
                case ChatKind.MonsterYell:
                case ChatKind.MonsterEmote: return true;
                case ChatKind.Emote: return Emotes;
                default: return false;   // whispers are private, system lines have no speaker
            }
        }

        private void OnGameEvent(GameEvent e)
        {
            if (e.Type != GameEventType.ChatMessage || e.SourceGuid == 0) return;
            var kind = (ChatKind)e.Amount;
            if (!Bubbles(kind)) return;
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null || e.SourceGuid == s.Player.Guid) return;
            if (UiArt.Files == null) return;
            string name = null;
            try { name = s.ChatSender(e.SourceGuid); }
            catch (Exception) { name = null; }
            if (string.IsNullOrEmpty(name)) { EntityState en = s.Find(e.SourceGuid); if (en != null) name = en.Name; }
            string text = UiChat.BubbleText(kind, name, e.Text);
            if (string.IsNullOrEmpty(text)) return;
            try { Show(e.SourceGuid, text, UiChat.ColorOf(kind, e.SourceGuid)); }
            catch (Exception ex) { WowLog.Warn("Chat bubbles: could not show a bubble (" + ex.Message + ")"); }
        }

        private void Show(ulong guid, string text, Color color)
        {
            Bubble b;
            if (!_active.TryGetValue(guid, out b))
            {
                if (_active.Count >= MaxBubbles) DropOldest();
                b = Take();
                b.Guid = guid;
                _active[guid] = b;
            }
            b.Born = Time.unscaledTime;
            b.Text.color = color;
            b.Text.text = text;
            // Wrap at MaxTextWidth; the bubble fits the text (TMP's preferred size).
            float w = Mathf.Min(b.Text.GetPreferredValues(text).x, MaxTextWidth);
            w = Mathf.Max(w, 12f);
            float h = b.Text.GetPreferredValues(text, w, 0f).y;
            b.W = w + 2f * Pad;
            b.H = h + 2f * Pad;
            b.Rt.sizeDelta = new Vector2(b.W, b.H + TailSize - 4f);
            // The anchor point is the tail's tip (bottom left).
            b.Rt.pivot = new Vector2((TailX + 2f) / b.W, 0f);
            b.Back.Place(0f, 0f, b.W, b.H);
            UiKit.Move(b.Text.rectTransform, Pad, Pad);
            UiKit.Size(b.Text.rectTransform, w + 2f, h + 2f);
            UiKit.Move(b.Tail.rectTransform, TailX, b.H - 4f);
            _shown++;
            if (!_loggedFirst)
            {
                _loggedFirst = true;
                WowLog.Info("Chat bubbles: first bubble (" + Seconds + " s, one per speaker, at most " + MaxBubbles + ", up to " + MaxDistance + " m; WoW's ChatBubble art; "
                            + "over the speaker's nameplate or a Somnium player's avatar).");
            }
        }

        private void DropOldest()
        {
            ulong oldest = 0;
            float t = float.MaxValue;
            foreach (KeyValuePair<ulong, Bubble> kv in _active)
                if (kv.Value.Born < t) { t = kv.Value.Born; oldest = kv.Key; }
            Bubble b;
            if (_active.TryGetValue(oldest, out b)) { _active.Remove(oldest); Recycle(b); }
        }

        private Bubble Take()
        {
            if (_free.Count > 0)
            {
                Bubble f = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
                f.Go.SetActive(true);
                return f;
            }
            return Build();
        }

        private void Recycle(Bubble b)
        {
            if (b == null || b.Go == null) return;
            b.Go.SetActive(false);
            b.Guid = 0;
            _free.Add(b);
        }

        private Bubble Build()
        {
            var b = new Bubble();
            b.Go = new GameObject("Chat bubble", typeof(RectTransform));
            b.Go.layer = UiKit.Layer;
            UiSharp.Register(b.Go);   // lead 2026-10-10
            b.Go.transform.SetParent(transform, false);
            var canvas = b.Go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = b.Go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            b.Group = b.Go.AddComponent<CanvasGroup>();
            b.Group.interactable = false;
            b.Group.blocksRaycasts = false;
            b.Rt = (RectTransform)b.Go.transform;
            b.Rt.sizeDelta = new Vector2(100f, 40f);
            b.Rt.localScale = Vector3.one * 0.003f;
            bool over = UiKit.Over;
            UiKit.Over = false;
            try
            {
                b.Back = UiBackdrop.Make(b.Rt, "Backdrop", BubbleBg, BubbleEdge, Edge, Inset, Tile, Color.white);
                b.Tail = UiKit.Pic(b.Rt, "Tail", UiArt.Tex(BubbleTail), TailX, 30f, TailSize, TailSize);
                b.Text = UiKit.Text(b.Rt, "Text", "", TextSize, Color.white, TextAlignmentOptions.TopLeft, Pad, Pad, 60f, 20f, null, false);
                b.Text.richText = false;   // what a player typed stays text
                b.Text.textWrappingMode = TextWrappingModes.Normal;
                b.Text.overflowMode = TextOverflowModes.Overflow;
            }
            finally
            {
                UiKit.Over = over;
            }
            return b;
        }

        private void LateUpdate()
        {
            if (_active.Count == 0) return;
            IWowSession s = WowPlay.Session;
            Camera cam = WowPlayer.Head();
            if (s == null || cam == null)
            {
                foreach (Bubble b in _active.Values) if (b.Go.activeSelf) b.Go.SetActive(false);
                if (s == null) Clear();
                return;
            }
            Vector3 eye = cam.transform.position;
            float now = Time.unscaledTime;
            _drop.Clear();
            foreach (KeyValuePair<ulong, Bubble> kv in _active)
            {
                Bubble b = kv.Value;
                float age = now - b.Born;
                if (age > Seconds) { _drop.Add(kv.Key); continue; }
                Vector3 at;
                if (!Anchor(kv.Key, eye, out at))
                {
                    if (b.Go.activeSelf) b.Go.SetActive(false);
                    continue;
                }
                float d = Vector3.Distance(eye, at);
                bool show = d <= MaxDistance;
                if (b.Go.activeSelf != show) b.Go.SetActive(show);
                if (!show) continue;
                Transform t = b.Rt;
                t.position = at;
                Vector3 away = at - eye;
                if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
                float sc = WowNameplate.ScaleFor(d);
                t.localScale = new Vector3(sc, sc, sc);
                float a = age > Seconds - FadeSeconds ? Mathf.Clamp01((Seconds - age) / FadeSeconds) : 1f;
                if (!Mathf.Approximately(b.Group.alpha, a)) b.Group.alpha = a;
            }
            for (int i = 0; i < _drop.Count; i++)
            {
                Bubble b;
                if (_active.TryGetValue(_drop[i], out b)) { _active.Remove(_drop[i]); Recycle(b); }
            }
        }

        /// <summary>Where the bubble's tail points: over a Somnium player's avatar, else over the entity's nameplate. False = not drawn here.</summary>
        private bool Anchor(ulong guid, Vector3 eye, out Vector3 at)
        {
            at = Vector3.zero;
            Transform head;
            if (WowGear.TryAvatarSlot(guid, (int)SpellVisualDb.Slot.Head, out head))
            {
                // Another Somnium player's online character: their avatar stands there (their WoW model is not drawn).
                if (head == null) return false;   // the avatar is not drawn here (another map, the room, far)
                at = head.position + Vector3.up * AvatarAbove;
                if (!_loggedAvatar) { _loggedAvatar = true; WowLog.Info("Chat bubbles: a Somnium player's line shows over their avatar."); }
                return true;
            }
            Vector3 top;
            if (!WowEntityViews.TryGetTop(guid, out top))
            {
                _skippedNoView++;
                if (_skippedNoView == 1) WowLog.Info("Chat bubbles: a speaker without a view here (out of sight) - no bubble until it is drawn.");
                return false;
            }
            float d = Vector3.Distance(eye, top);
            // Above the nameplate: its bottom edge is GapFor(d) over the head, the plate 46 units tall at ScaleFor(d) metres per unit.
            at = top + Vector3.up * (WowNameplate.GapFor(d) + WowNameplate.Height * WowNameplate.ScaleFor(d) + 0.02f);
            return true;
        }
    }
}
