using System;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Where a Somnium player's body is, for hanging weapons on it (the local player and every remote one). Read only:
    /// the avatar is never modified and nothing is parented under it.
    ///
    /// What the Bridge gives (client source, see the Gear report): Body.Root and Body.Head always; Body.RightHand /
    /// LeftHand / Neck only in VR (RightHandAnchor with the controller's grip pose, NeckAnchor 0.2 m under the head) -
    /// in desktop mode they are null (Player.NonXR.Body.prefab: "RightHand: {fileID: 0}"). So the torso comes from the
    /// avatar's own humanoid Animator when one is found (its bones' POSITIONS only: hips, chest, shoulders, hands and
    /// fingers - rig-independent), matched to the player by its head bone lying within 0.6 m of Body.Head (the avatar
    /// object is re-parented to the scene root by the client, so it is not under Body.Root). Without it: a frame from
    /// Root/Neck/Head (feet at Root, the neck under the head, facing = Root's yaw on desktop, the head's yaw smoothed in VR).
    /// </summary>
    public sealed class GearBody
    {
        public ISomniumPlayer Player;
        public string Label = "player";

        // Torso frame (world), refreshed by Update.
        public Vector3 Up = Vector3.up, Right = Vector3.right, Forward = Vector3.forward;
        public Vector3 Chest, Hips, ShoulderL, ShoulderR, Feet, HeadPos;
        public float Scale = 1f;
        public bool Humanoid { get { return _anim != null; } }

        /// <summary>The avatar's humanoid Animator matched to this player (null when none was found) - GearPoser poses its bones.</summary>
        public Animator Animator { get { return _anim; } }

        /// <summary>Fix 2026-10-04: the followed Animator is the client's culling / loading stand-in (GearAvatarRoots.IsStandIn), not the
        /// player's own avatar.</summary>
        public bool OnStandIn { get; private set; }

        /// <summary>Lead fix 2026-10-04 (the user: armour "removed and placed back" near other players): when the followed Animator was
        /// last lost (Drop / Rebound), -100 = never. For RefindSeconds after it, Find runs every RefindEvery s with a fresh candidate list
        /// (was every 2 s: Player.log "desktop, no humanoid avatar found" between the client's swaps of a player at its 15 m near distance -
        /// the armour hidden meanwhile). Values ours.</summary>
        public float LostAt { get; private set; } = -100f;
        public const float RefindSeconds = 4f, RefindEvery = 0.2f;

        /// <summary>The top of the player object's hierarchy (Body.Root.root) as of the last Update, null before one found it.</summary>
        public Transform PlayerRoot { get; private set; }

        /// <summary>Iteration 7: Body.Root itself as of the last Update (a remote one = the synced pose as Fusion delivers it: the
        /// rider's root + the flat CharacterController-centre offset, client PlayerTransformNetworkDriver). Null before one was found.</summary>
        public Transform Root { get; private set; }

        /// <summary>The top of the avatar model's hierarchy (the client spawns remote avatars at the scene root), null without a matched Animator.</summary>
        /// Kept after the Animator is dropped (AvatarHoldersHandler.SetMainHolderCulling deactivates the main model's visual
        /// roots while the client shows its culling stand-in; whether that stand-in is under the same object is not verified).
        public Transform AvatarRoot { get { if (_anim != null) _avatarRootKept = _anim.transform.root; return _avatarRootKept; } }
        private Transform _avatarRootKept;
        public bool Valid { get; private set; }

        /// <summary>Lead fix (2026-09-27, the user: "I still don't see Somnium player avatars"): the avatar MODEL's own feet (its
        /// humanoid Animator's transform) when one is found, else Feet (Body.Root). A remote Body.Root was seen at the login room
        /// (~3 km up) while the avatar stood in the world, so the far rule hid it (Player.log "hidden (Far, 2927 m)").</summary>
        public Vector3 ModelFeet { get { return _anim != null && _anim.gameObject.activeInHierarchy ? _anim.transform.position : Feet; } }

        /// <summary>Body.RightHand / LeftHand (VR only; null on desktop).</summary>
        public Transform XrRight, XrLeft;

        /// <summary>
        /// Render-lab hook (null in the world): when OverrideRoot is set these stand in for the Bridge player's
        /// Body.Root/Head/Neck/RightHand/LeftHand, so the placement can be rendered on a humanoid without the client.
        /// </summary>
        public Transform OverrideRoot, OverrideHead, OverrideNeck, OverrideRight, OverrideLeft;

        private Animator _anim;
        private bool _animSecondary;   // lead fix 2026-10-03: _anim is a mirror / teleport copy (no main copy found yet); fix 2026-10-04: or not a main copy at all
        private Avatar _animAvatar;    // fix 2026-10-04 (avatar change): the Animator.avatar the bones were read from
        private float _rebindSaidAt = -100f;
        private int _rebindQuiet;
        private const float RebindSayEvery = 5f;   // ours: the "avatar changed ... re-bound" line at most this often per body
        private Transform _hips, _spine, _chest, _upperChest, _neck, _head, _lUpper, _rUpper, _lHand, _rHand;
        private Transform _rIndex, _rLittle, _rMiddle, _lIndex, _lLittle, _lMiddle;
        private float _nextFind, _driftSince = -1f;
        private float _yaw;
        private int _yawFrame = -1;
        private bool _haveYaw;
        private string _mode = "";

        private static Animator[] _candidates = new Animator[0];
        private static float _candidatesAt = -100f;

        /// <summary>Recomputes the frame. False when the player has no body (not spawned yet, left).</summary>
        public bool Update()
        {
            Valid = false;
            Transform root, head, neckAnchor;
            if (OverrideRoot != null)
            {
                root = OverrideRoot;
                head = OverrideHead;
                neckAnchor = OverrideNeck;
                XrRight = OverrideRight;
                XrLeft = OverrideLeft;
            }
            else
            {
                ISomniumPlayerBody body = null;
                try { body = Player?.References?.Body; }
                catch (Exception) { body = null; }
                root = body?.Root;
                head = body?.Head;
                if (root == null) return false;
                XrRight = body.RightHand;
                XrLeft = body.LeftHand;
                neckAnchor = body.Neck;
            }
            Vector3 headPos = head != null ? head.position : root.position + Vector3.up * 1.65f;
            HeadPos = headPos;
            Feet = root.position;
            PlayerRoot = root.root;
            Root = root;

            // Fix 2026-10-04 (avatar change): a new avatar on the same Animator (another Animator.avatar) or a bone of ours destroyed
            // with the old model - read the bones again at once (else the poser and the weapons keep the old skeleton).
            string changed = Gone(_anim) ? "its Animator was destroyed with the old model" : _anim != null ? AvatarChanged() : null;
            if (changed != null) { Rebound(changed); Find(headPos); }
            else if (_anim == null || !_anim.gameObject.activeInHierarchy) { if (_anim != null) Drop("the avatar's Animator went away"); Find(headPos); }
            else if (_animSecondary) Find(headPos);   // lead fix 2026-10-03: holding a mirror copy - keep looking (every 2 s) for the main one
            if (_anim != null && _head != null)
            {
                float drift = (_head.position - headPos).magnitude;
                if (drift > 1.0f)
                {
                    if (_driftSince < 0f) _driftSince = Time.unscaledTime;
                    else if (Time.unscaledTime - _driftSince > 2f) Drop("its head bone is " + drift.ToString("0.0") + " m from Body.Head");
                }
                else _driftSince = -1f;
            }

            if (_anim != null && _hips != null && _lUpper != null && _rUpper != null)
            {
                Vector3 neck = _neck != null ? _neck.position : (_head != null ? _head.position : headPos);
                Hips = _hips.position;
                Vector3 up = neck - Hips;
                if (up.sqrMagnitude < 1e-4f) up = Vector3.up;
                Up = up.normalized;
                Vector3 across = _rUpper.position - _lUpper.position;
                Vector3 right = across - Vector3.Dot(across, Up) * Up;
                if (right.sqrMagnitude < 1e-5f) right = root.right;
                Right = right.normalized;
                Forward = Vector3.Cross(Right, Up);
                Transform c = _upperChest != null ? _upperChest : (_chest != null ? _chest : _spine);
                Chest = c != null ? c.position : Vector3.Lerp(Hips, neck, 0.7f);
                ShoulderL = _lUpper.position;
                ShoulderR = _rUpper.position;
                Scale = Mathf.Clamp(across.magnitude / 0.34f, 0.6f, 1.6f);
                SetMode("humanoid avatar '" + _anim.name + "'");
            }
            else
            {
                Up = Vector3.up;
                Vector3 f;
                if (XrRight != null && head != null)
                {
                    // VR without the avatar: the torso follows the head's yaw, lagging like a body does (turns once the
                    // head is more than 25 degrees away, at up to 120 degrees a second).
                    Vector3 hf = head.forward; hf.y = 0f;
                    if (hf.sqrMagnitude < 1e-4f) { hf = head.up * (head.forward.y < 0f ? 1f : -1f); hf.y = 0f; }
                    float hy = Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg;
                    if (!_haveYaw) { _yaw = hy; _haveYaw = true; }
                    float d = Mathf.DeltaAngle(_yaw, hy);
                    if (Mathf.Abs(d) > 25f && _yawFrame != Time.frameCount) _yaw += Mathf.Clamp(d - Mathf.Sign(d) * 25f, -120f * Time.deltaTime, 120f * Time.deltaTime);
                    _yawFrame = Time.frameCount;
                    f = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
                }
                else
                {
                    f = root.forward; f.y = 0f;
                    if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                }
                Forward = f.normalized;
                Right = Vector3.Cross(Up, Forward);
                float height = Mathf.Max(0.8f, headPos.y - root.position.y);
                Scale = Mathf.Clamp(height / 1.62f, 0.6f, 1.5f);
                Vector3 neck = neckAnchor != null && XrRight != null ? new Vector3(headPos.x, neckAnchor.position.y, headPos.z) : headPos - Up * (0.2f * Scale);
                Vector3 spine = neck - Forward * (0.06f * Scale);
                Chest = spine - Up * (0.22f * Scale);
                Hips = new Vector3(spine.x, root.position.y + height * 0.56f, spine.z);
                ShoulderL = Chest + Up * (0.1f * Scale) - Right * (0.18f * Scale);
                ShoulderR = Chest + Up * (0.1f * Scale) + Right * (0.18f * Scale);
                SetMode(XrRight != null ? "VR, no humanoid avatar found (torso from the head)" : "desktop, no humanoid avatar found (torso from Root/Head)");
            }
            Valid = true;
            return true;
        }

        /// <summary>
        /// A hand's grip frame: VR = the controller anchor (the OpenXR grip pose: forward out of the top of the fist, right =
        /// palm normal to the right, up toward the player). Desktop = the same frame built from the avatar's hand and finger
        /// bones (forward = little -> index knuckle, up = against wrist -> middle knuckle). Else a pose in front of the hip.
        /// </summary>
        public bool HandPose(bool right, out Vector3 pos, out Quaternion rot)
        {
            Transform xr = right ? XrRight : XrLeft;
            if (xr != null)
            {
                pos = xr.position;
                rot = xr.rotation;
                return true;
            }
            Transform hand = right ? _rHand : _lHand;
            if (_anim != null && hand != null)
            {
                Transform index = right ? _rIndex : _lIndex, little = right ? _rLittle : _lLittle, middle = right ? _rMiddle : _lMiddle;
                Vector3 k = (middle != null ? middle.position : hand.position + Forward * 0.08f) - hand.position;
                if (k.sqrMagnitude < 1e-6f) k = -Up;
                k.Normalize();
                Vector3 f;
                if (index != null && little != null) f = index.position - little.position;
                else f = Forward;
                f -= Vector3.Dot(f, k) * k;
                if (f.sqrMagnitude < 1e-6f) f = Forward;
                f.Normalize();
                pos = hand.position + k * (0.06f * Scale);
                rot = Quaternion.LookRotation(f, -k);
                return true;
            }
            if (!Valid) { pos = Vector3.zero; rot = Quaternion.identity; return false; }
            float side = right ? 1f : -1f;
            pos = Hips + Right * (0.24f * side * Scale) + Forward * (0.28f * Scale) + Up * (0.12f * Scale);
            Vector3 blade = (Forward + Up * 0.6f).normalized;
            rot = Quaternion.LookRotation(blade, Vector3.Cross(Right, blade) * -1f);
            return true;
        }

        private void SetMode(string mode)
        {
            if (mode == _mode) return;
            _mode = mode;
            WowLog.Info("Gear: body of " + Label + ": " + mode);
        }

        private void Drop(string why)
        {
            WowLog.Info("Gear: body of " + Label + ": dropped the avatar Animator (" + why + ")");
            _anim = null;
            _animSecondary = false;
            OnStandIn = false;
            LostAt = Time.unscaledTime;
            _driftSince = -1f;
            // fix 2026-10-04 (remote armour): the client swaps the main copy and its culling stand-in in one call
            // (AvatarHoldersHandler.SetMainHolderCulling) - look now, in a fresh list (FindObjectsByType skips inactive objects, so the
            // copy that just came back was not in the last one)
            _nextFind = 0f;
            _candidatesAt = -100f;
        }

        /// <summary>Fix 2026-10-04 (avatar change): why the bound Animator no longer shows the skeleton we read (another
        /// Animator.avatar, a bone we hold destroyed with the old model), null while it does.</summary>
        private string AvatarChanged()
        {
            if (_anim.avatar != _animAvatar) return "its humanoid avatar is now '" + (_anim.avatar != null ? _anim.avatar.name : "none") + "'";
            if (Gone(_hips) || Gone(_head) || Gone(_lUpper) || Gone(_rUpper) || Gone(_lHand) || Gone(_rHand)) return "a bone of the old skeleton was destroyed";
            return null;
        }

        /// <summary>An object we held that Unity destroyed (a C# reference, Unity-null).</summary>
        private static bool Gone(UnityEngine.Object o) { return !ReferenceEquals(o, null) && o == null; }

        /// <summary>Fix 2026-10-04: forget the copy at once and look for the avatar now (the candidate list read again); one line per
        /// RebindSayEvery s ("... avatar changed ... re-bound"; the next one says how many it covered).</summary>
        private void Rebound(string why)
        {
            float now = Time.unscaledTime;
            if (now - _rebindSaidAt >= RebindSayEvery)
            {
                WowLog.Info("Gear: body of " + Label + ": avatar changed (" + why + ") - re-bound" + (_rebindQuiet > 0 ? " (" + _rebindQuiet + " more since the last line)" : ""));
                _rebindSaidAt = now;
                _rebindQuiet = 0;
            }
            else _rebindQuiet++;
            _anim = null;
            _animAvatar = null;
            _animSecondary = false;
            OnStandIn = false;
            LostAt = Time.unscaledTime;
            _driftSince = -1f;
            _nextFind = 0f;
            _candidatesAt = -100f;
        }

        private void Find(Vector3 headPos)
        {
            if (Time.unscaledTime < _nextFind) return;
            bool soon = Time.unscaledTime - LostAt < RefindSeconds;   // lead fix 2026-10-04: just lost - look again quickly
            _nextFind = Time.unscaledTime + (soon ? RefindEvery : 2f);
            if (Time.unscaledTime - _candidatesAt > (soon ? RefindEvery * 0.9f : 1.5f))
            {
                _candidatesAt = Time.unscaledTime;
                _candidates = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            }
            // Lead fix (2026-10-03): the client's MAIN avatar copy wins over its mirror / teleport copies (GearAvatarRoots): they are posed
            // alike, so the nearest head alone picked the mirror copy ('MirrorRoot' in Player.log) - forms hid an avatar nobody sees.
            // activeInHierarchy, not enabled: the client disables the main Animator and updates it by hand for far avatars.
            // Fix 2026-10-04 (remote animation, avatar change): three tiers - a MAIN copy (MainRoot / NonXRMainRoot), else a humanoid of
            // no client root (the render lab, a world's own), else a copy nobody sees whole (mirror / teleport / WEARABLES). The wearables
            // copy carries the main copy's bones exactly (RootsMirroringHandler), so with two equal heads the old "nearest wins" left
            // the choice to FindObjectsByType's unsorted order - and the poser then posed an invisible copy (GearAvatarRoots).
            // Fix 2026-10-04 (the user: remote players saw the armour "all messed up" after "I went behind them"): a main copy of the
            // client's culling / loading STAND-IN (GearAvatarRoots.IsStandIn) ranks under every copy of the player's own avatar and is
            // only taken when none is active - and then the main one is looked for again every 2 s.
            Animator best = null, other = null, second = null, stand = null;
            float bestD = 0.6f, otherD = 0.6f, secondD = 0.6f, standD = 0.6f;
            for (int i = 0; i < _candidates.Length; i++)
            {
                Animator a = _candidates[i];
                if (a == null || !a.gameObject.activeInHierarchy || !a.isHuman || a.avatar == null) continue;
                Transform h = a.GetBoneTransform(HumanBodyBones.Head);
                if (h == null) continue;
                float d = (h.position - headPos).magnitude;
                if (d >= 0.6f) continue;
                string kind = GearAvatarRoots.KindOf(a);
                if ((kind == GearAvatarRoots.Main || kind == GearAvatarRoots.NonXrMain) && GearAvatarRoots.IsStandIn(a)) { if (d < standD) { standD = d; stand = a; } }
                else if (kind == GearAvatarRoots.Main || kind == GearAvatarRoots.NonXrMain) { if (d < bestD) { bestD = d; best = a; } }
                else if (kind == null) { if (d < otherD) { otherD = d; other = a; } }
                else if (d < secondD) { secondD = d; second = a; }
            }
            _animSecondary = best == null && (other != null || stand != null || second != null);   // not a main copy: keep looking (every 2 s) for one
            if (best == null) { best = other ?? stand ?? second; bestD = other != null ? otherD : stand != null ? standD : secondD; }
            if (best == null) return;
            OnStandIn = best == stand;
            if (best == _anim) return;   // the same copy as before (a secondary one: still no main copy) - nothing to log
            _anim = best;
            _hips = best.GetBoneTransform(HumanBodyBones.Hips);
            _spine = best.GetBoneTransform(HumanBodyBones.Spine);
            _chest = best.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = best.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = best.GetBoneTransform(HumanBodyBones.Neck);
            _head = best.GetBoneTransform(HumanBodyBones.Head);
            _lUpper = best.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rUpper = best.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lHand = best.GetBoneTransform(HumanBodyBones.LeftHand);
            _rHand = best.GetBoneTransform(HumanBodyBones.RightHand);
            _rIndex = best.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _rLittle = best.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            _rMiddle = best.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _lIndex = best.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _lLittle = best.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            _lMiddle = best.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _animAvatar = best.avatar;
            _driftSince = -1f;
            WowLog.Info("Gear: body of " + Label + ": humanoid Animator '" + best.name + "' (" + (GearAvatarRoots.KindOf(best) ?? "no client root") + (OnStandIn ? ", the client's culling / loading stand-in - looking for the avatar itself" : _animSecondary ? ", not the main copy - looking for the main one" : "") +
                        "; head bone " + bestD.ToString("0.00") + " m from Body.Head; hips " + (_hips != null) +
                        ", chest " + (_chest != null || _upperChest != null) + ", hands " + (_rHand != null && _lHand != null) + ", fingers " + (_rIndex != null && _rLittle != null && _rMiddle != null) + ")");
        }
    }
}
