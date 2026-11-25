// iteration 11 - owner interact (proposal-interact.md 6.3.10): raid target icons (Star .. Skull) floating over units in the 3D world - over
// WoW-client players, NPCs and Somnium avatars alike. Reads IWowInteractions.Raid.TargetIcons (MSG_RAID_TARGET_UPDATE, OnlineSession.Raid.cs).
// Anchors: a Somnium avatar's head slot (WowGear.TryAvatarSlot, SpellVisualDb.Slot.Head) + 0.45 m (ours: above its nameplate, GearRemote puts
// the plate 0.5 x scale above the head bone); a drawn WoW unit's top (WowEntityViews.TryGetTop) + 0.35 m (ours); our own avatar on desktop only
// (WowPlayer.HeadBone + 0.45 m; VR: not drawn - you cannot see above your own head). A Somnium avatar not drawn here shows nothing (never the
// server's copy). Drawn like WowNameplate (a world-space canvas on the UI layer, no raycasts), 64 x 64 units at 0.007 m a unit (0.45 m, ours),
// facing the camera / headset every LateUpdate. Art: Interface\TargetingFrame\UI-RaidTargetingIcons (4 x 4 cells of 64; icon i at column
// (i-1) % 4, row (i-1) / 4 - TargetFrame.lua:692-701).
using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Interact
{
    public sealed class WowRaidMarks : MonoBehaviour
    {
        private const float Units = 64f, MetresPerUnit = 0.007f;          // ours: 0.45 m wide
        private const float AvatarUp = 0.45f, UnitUp = 0.35f;              // ours (see the header)
        private const string Art = @"Interface\TargetingFrame\UI-RaidTargetingIcons.blp";

        private readonly GameObject[] _go = new GameObject[8];
        private readonly ulong[] _shownFor = new ulong[8];
        private bool _logged, _failedLogged;

        private void OnDisable()
        {
            for (int i = 0; i < 8; i++) if (_go[i] != null) _go[i].SetActive(false);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < 8; i++) if (_go[i] != null) Destroy(_go[i]);
        }

        private void LateUpdate()
        {
            try { Place(); }
            catch (Exception e)
            {
                if (!_failedLogged) { _failedLogged = true; WowLog.Warn("Raid marks: placing failed (" + e.Message + ") - logged once."); }
            }
        }

        private void Place()
        {
            IWowSession s = WowPlay.Session;
            IWowInteractions ix = s != null ? s.Interactions : null;
            RaidView r = ix != null ? ix.Raid : null;
            PlayerState p = s != null ? s.Player : null;
            Camera cam = WowPlayer.Head();
            for (int i = 0; i < 8; i++)
            {
                ulong guid = r != null && p != null ? r.TargetIcons[i] : 0;
                Vector3 at = Vector3.zero;
                bool show = guid != 0 && cam != null && Anchor(p, guid, out at);
                if (!show)
                {
                    if (_go[i] != null && _go[i].activeSelf) _go[i].SetActive(false);
                    _shownFor[i] = 0;
                    continue;
                }
                GameObject go = _go[i] ?? (_go[i] = Build(i));
                if (!go.activeSelf) go.SetActive(true);
                if (_shownFor[i] != guid)
                {
                    _shownFor[i] = guid;
                    if (!_logged)
                    {
                        _logged = true;
                        WowLog.Info("Raid marks: world icons drawn (ours: " + Units + " units at " + MetresPerUnit + " m, " + AvatarUp + " m over a Somnium avatar's head, " +
                                    UnitUp + " m over a WoW model; facing the camera).");
                    }
                }
                Transform t = go.transform;
                t.position = at;
                Vector3 away = at - cam.transform.position;
                if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        /// <summary>Where icon's unit stands (see the header); false = not drawn.</summary>
        private static bool Anchor(PlayerState p, ulong guid, out Vector3 at)
        {
            at = Vector3.zero;
            Transform head;
            if (WowGear.TryAvatarSlot(guid, (int)SpellVisualDb.Slot.Head, out head))
            {
                if (head == null) return false;   // a Somnium avatar not drawn here: nothing (never the server's copy)
                at = head.position + Vector3.up * AvatarUp;
                return true;
            }
            Vector3 top;
            if (WowEntityViews.TryGetTop(guid, out top)) { at = top + Vector3.up * UnitUp; return true; }
            if (guid == p.Guid && !WowPlayer.XrPresent)
            {
                Transform hb = WowPlayer.HeadBone;
                if (hb == null) return false;
                at = hb.position + Vector3.up * AvatarUp;
                return true;
            }
            return false;
        }

        private GameObject Build(int i)
        {
            var go = new GameObject("Raid Target Icon " + (i + 1));
            go.layer = UiKit.Layer;
            go.transform.SetParent(transform, false);
            UiSharp.Register(go);   // lead 2026-10-10
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(Units, Units);
            rt.localScale = Vector3.one * MetresPerUnit;
            var img = go.AddComponent<RawImage>();
            img.texture = UiArt.Tex(Art);
            img.raycastTarget = false;
            float u = (i % 4) * 0.25f, v = (i / 4) * 0.25f;
            UiKit.Coords(img, u, u + 0.25f, v, v + 0.25f);   // SetRaidTargetIconTexture (TargetFrame.lua:692-701)
            return go;
        }
    }
}
