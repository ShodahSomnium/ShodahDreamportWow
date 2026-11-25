// Lead 2026-10-04 ("Face in helm"). ArmourHeadCut (head triangles outside the helm removed from mesh copies) failed in the client - the
// user: "my hair is still visible and I dont see my face, in-fact, I look like a ghost!". The user's plan: "Is there a way we can take some
// kind of snapshot of the players face, and then simply have the face shown as a simple model inside where the helmet is? ... Then you
// hide the head, completely. That should do it."
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Armour
{
    /// <summary>
    /// "Face in helm" for one avatar (local or remote; every client does it for every avatar from its own view, nothing goes over the
    /// network beyond the wearer's existing flag, ArmourPieces.Adjust[0].Flag), while its immersive helm is shown:
    /// 1. The HEAD is hidden the client's own way - MainRoot.SetHeadVisibilityState (CS AvatarSystem/Scripts/Roots/MainRoot.cs:99-112):
    ///    "headBone.localScale = state ? _headScale : _headScale * MINIMAL_SCALE_TO_WORK_WITH_MAGICA_CLOTH_PHYSICS" (1e-10f, :18) - on
    ///    every visual copy (GearAvatarRoots.VisualRoots: the main / desktop copy and the mirror copy the Somnium camera and mirrors draw;
    ///    the teleport preview has no helm and is left whole). Nothing in the client resets it per frame: RootsMirroringHandler.CopyBonesTransforms
    ///    copies only "toBone.localPosition" / "toBone.localRotation" (:102-103), and MainRoot.SetHeadVisibilityState runs at set-up and
    ///    on camera-mode changes (FirstPersonCameraState -> NonXRCameraFeature.SetAvatarHeadActive -> SetHeadBoneState(true)), which
    ///    Tick puts back every frame. The local VR main copy ("MainRoot", IsLocal) is the client's: it already hides that head
    ///    (MainRoot.SetupHead -> SetHeadVisibilityState(false)), so its scale is never touched; nor is any copy whose head is already
    ///    collapsed when found. Restored (only where it still holds our value) when the helm goes, the flag goes off, or the avatar /
    ///    armour is dropped.
    /// 2. A SNAPSHOT of the face (the avatar's own renderers, from the front, transparent background) into a small RenderTexture - the
    ///    per-camera technique of UiUnitPortrait / UiArmourFitView: our own camera, enabled for one render; in beginCameraRendering, for
    ///    that camera only, the copy's renderers go to UiUnitPortrait.PortraitLayer (30) and back in endCameraRendering; a key light /
    ///    flat ambient / no fog for that render only. Orthographic, so the picture maps 1:1 onto the card. Drawn from the mirror copy
    ///    (its head is never hidden by the client) else the followed copy. Skinned meshes are skinned before the cameras render
    ///    (render lab face test, Unity 6000.3.5 D3D11, a 128 px face picture: head whole 7148 px drawn, collapsed 1564, collapsed but
    ///    made whole only inside that camera's beginCameraRendering 1564), so the copy's head is made whole on the frame BEFORE the render and stays whole through it (only the copy the
    ///    snapshot is taken from; updateWhenOffscreen on for those two frames, so a copy no camera draws is skinned too). Taken before the
    ///    head is hidden (on helm-up), and again when the avatar changes (another Animator, its copies, renderers or meshes); never per
    ///    frame. An AsyncGPUReadback checks the picture once: an empty one never hides the head.
    /// 3. The CARD: an oval (ours: cheek to cheek, chin to mid-forehead, from the eye bones) that follows the measured face surface 1 cm under it, textured with the snapshot, SomniumWoW/M2 unlit + alpha test (as M2Materials builds
    ///    blend mode 1 AlphaKey with render flag 0x01 unlit; FormShell uses the same shader unlit), placed every frame after the helm in
    ///    ArmourPieces.Place on the followed head bone where the face was measured, kept behind the helm's front. Same layer as the helm
    ///    (the photo layer 8 when the helm is hidden from the wearer's own eyes but kept for the Somnium camera and mirrors) and hidden
    ///    whenever the helm is.
    /// Values are ours unless quoted. One log line per avatar ("Armour: face card for ...").
    /// Fix 2026-10-04 (the user: "Apart from my head is still not there when I am wearing a helmet" - others saw the head hidden and no
    /// face): the body-scale face (an avatar whose head vertices cannot be used - the user's own: Player.log "body-scale face (s 0.82,
    /// RPM Men in the render lab; no head vertices)") took its size s from the head bone's height above the copy's ANIMATOR OBJECT. For a
    /// remote avatar that object is not at the feet: on the user's client one remote avatar logged "s 3.00" and, a minute later, "s 0.54"
    /// (Player.log "face card for 'ꨄ Rage Quit ꨄ' ...", the same avatar, its face measured 0.126 x 0.156 m both times); the copy's Animator
    /// is then the client's custom Animator on the NonXRMainRoot object itself (CS RootHandlers/Common/CustomAnimationsHandler.cs:44
    /// "root?.AddComponentToObject&lt;Animator&gt;()", Player.log "humanoid Animator 'NonXRMainRoot'"), placed by NonXRRootsMovementHandler from
    /// the network targets. Render lab (P/face_lab_remote, a remote whose client root stands 0.75 m above its feet): s 0.56, the snapshot
    /// camera inside the face (75% of the oval drawn: the inside of the mouth), a 0.076 x 0.097 m card 6 cm inside the head - the head gone
    /// and no face. Now s comes from the avatar's own skeleton: the head bone's height in the humanoid Avatar's T-pose
    /// (Avatar.humanDescription.skeleton, as ArmourPieces.TPoseTurn reads it) x the model's world scale - where the client root stands, the
    /// pose, IK and our poser do not enter (the old way only when the skeleton cannot tell). And the camera of a body-scale snapshot stands
    /// FallSlack x the estimated front further out (the picture is orthographic: only what it can see changes), so a face larger than the
    /// estimate is still photographed from outside.
    /// </summary>
    public sealed class ArmourFaceCard
    {
        // ---- the client's own
        public const float HiddenScale = 1e-10f;          // MainRoot.cs:18 MINIMAL_SCALE_TO_WORK_WITH_MAGICA_CLOTH_PHYSICS
        // ---- ours: the snapshot
        public const int TexSize = 128;                   // px, square (64 KB colour per avatar)
        public const float CameraDepth = -62f;            // before the main camera (UiUnitPortrait -60, UiArmourFitView -61)
        public const float CamGap = 0.05f;                // m: the camera stands this far in front of the frontmost head point
        public const float FarShare = 0.6f;               // the camera sees this share of the head's depth (the face, not the hair behind)
        public const float KeyPitch = 20f, KeyYaw = 25f;  // the key light, relative to the camera: from the upper left
        public const float KeyIntensity = 0.75f;
        public const float Ambient = 0.6f;                // flat grey ambient during the snapshot
        public const float MinContent = 0.05f;            // share of the oval with any colour under which the snapshot is empty
        public const float MinCoverage = 0.05f;           // share of the oval with alpha >= 0.5 under which its alpha is not trusted
        // ---- ours: when
        public const float CheckSeconds = 1f;             // copies / renderers / meshes are checked this often while shown
        public const float MinGap = 0.5f;                 // s between two snapshots of one avatar
        public const float RetrySeconds = 5f;             // an empty snapshot is tried again after this x tries, at most MaxTries
        public const int MaxTries = 3;
        public const int StuckFrames = 5;                 // a camera that has not rendered after this many frames is given up
        public const int ReadbackFrames = 30;             // a readback not back after this many frames: the picture is used unchecked
        public const int MaxLogs = 4;
        // ---- ours: the face, measured from the avatar's own head vertices in the head's facing frame (x right, y up, z forward).
        // The card covers cheek to cheek and chin to mid-forehead - not the hair (render lab f1: a frame of the whole front of the head put
        // the woman's hair round the helm and the man's hair above it).
        public const float SlabShare = 0.4f;              // the face's front = head vertices within this share of the head's width behind its front
        public const float MinSlab = 0.04f, MaxSlab = 0.1f;   // m: that depth clamped
        public const float WidthOverEyes = 2.3f;          // the card's width = this x the distance between the eye bones
        public const float TopOverEyes = 0.9f;            // its top this x that distance above the eyes (mid-forehead)
        public const float ChinOverEyes = 2f;             // the chin this x that distance under the eyes when it cannot be measured
        public const float MinEyes = 0.035f, MaxEyes = 0.12f;  // m: an eye distance outside these is not used
        public const float FallWidthShare = 0.75f, FallTopShare = 0.7f;   // no eye bones: of the face front's width / from the chin up
        public const float CardInset = 0.01f;             // m: the card sits this far behind the face's surface
        public const int ReliefX = 12, ReliefY = 16;      // the face's surface sampled on this grid (the card follows it)
        public const int Rings = 6, Segs = 24;            // the oval card's mesh
        public const float Curve = 0.2f;                  // no surface (body-scale face): the card's sides sit this x its width back
        public const float HelmClear = 0.01f;             // m: the card's front stays this far behind the helm's front
        public const float Shade = 0.9f;                  // the card's tint (rgb): the face in the helm's shade
        public const float Cutoff = 0.5f;                 // alpha test
        public const float MinFace = 0.06f, MaxFace = 0.4f;   // m: a measured face width / height outside these is not used
        public const float HeadJoint = 1.6f;              // as UiArmourFitView: s = the head bone's height above the feet / this
        // fallback when the head's vertices cannot be read, x s: the RPM Men card measured in the render lab (face 0.139 x 0.178 m,
        // centre 0.053 m up, front 0.131 m forward of the head bone, s 1.02 - face lab f3); FallDepth ours
        public const float FallW = 0.136f, FallH = 0.175f, FallY = 0.052f, FallZ = 0.128f, FallDepth = 0.22f;
        private const string FallNote = "RPM Men in the render lab";
        public const float FallSlack = 0.5f;              // a body-scale snapshot's camera this share of the estimated front further out (ours)
        public const float MinHeadHeight = 0.3f, MaxHeadHeight = 6f;   // m: a T-pose head height outside these is not used (ours)
        public const string CameraName = "WoW face card camera";

        private enum Snap { Idle, Prime, Camera, Rendered, Reading }

        private struct Copy
        {
            public Transform Root, Head;
            public Animator Anim;
            public string Kind;
            public Vector3 Orig;
            public bool Hidden, Skip, Seen;
        }

        private readonly string _label;
        private readonly List<Copy> _copies = new List<Copy>(4);
        private readonly List<Transform> _roots = new List<Transform>(8);
        private readonly List<Renderer> _rends = new List<Renderer>(32);
        private readonly List<SkinnedMeshRenderer> _skins = new List<SkinnedMeshRenderer>(8);
        private readonly List<bool> _offscreen = new List<bool>(8);
        private readonly List<GameObject> _moved = new List<GameObject>(32);
        private readonly List<int> _layers = new List<int>(32);
        private readonly List<Vector3> _pts = new List<Vector3>(1024);

        private Animator _anim;
        private bool _local, _want, _ready, _alphaOk = true;
        private Quaternion _qHead = Quaternion.identity;
        private bool _qHeadSet;
        private float _nextCheck, _snapAt;
        private int _sig, _tries;
        private bool _needSnap, _avoidMirror;
        private Snap _state = Snap.Idle;
        private int _stateFrame, _snapCopy = -1;
        private string _snapKind = "";
        private int _snapRenderers;
        // the face (head facing frame of the copy, metres)
        private float _cx, _cy, _front, _w, _h, _depth, _cardFront;
        private float _gap = CamGap;                      // the snapshot camera's distance in front of _front
        private string _measure = "";
        private static readonly List<int> Chain = new List<int>(32);
        // the card
        private GameObject _card;
        private MeshRenderer _cardMr;
        private Mesh _cardMesh;
        private Material _mat;
        private RenderTexture _rt;
        private int _helmBoxFor;
        private Vector3 _helmFront;
        private bool _helmFrontOk;
        private int _logs;
        private bool _saidFor;
        private Action<AsyncGPUReadbackRequest> _onRead;
        private bool _destroyed;

        // the shared camera (one snapshot at a time over all avatars)
        private static Camera _cam;
        private static GameObject _camGo;
        private static RenderTexture _idleRt;
        private static ArmourFaceCard _owner;
        private static int _ownedAt;
        private static bool _hooked;
        private static int _alive;
        private static Light _sun;
        private static float _nextSunSearch;
        // the render's saved state
        private static bool _lit, _savedFog;
        private static SphericalHarmonicsL2 _savedAmbient;
        private static Light _litSun;
        private static Quaternion _savedSunRot;
        private static float _savedSunIntensity;
        private static Color _savedSunColor;

        public ArmourFaceCard(string label)
        {
            _label = label;
            _alive++;
        }

        /// <summary>The card's renderer while it is shown (the fitting view draws it with the helm), else null.</summary>
        public Renderer Shown { get { return _card != null && _card.activeSelf && _cardMr != null ? _cardMr : null; } }

        /// <summary>
        /// Every frame from ArmourPieces.Place, after the helm is placed. want = the helm is shown and the wearer's flag is on; local = our
        /// own avatar; qHead / qHeadSet = the head bone's T-pose turn (head.rotation x qHead = the facing frame); draw = our-frame offset.
        /// </summary>
        public void Tick(Animator avatar, GameObject helm, bool want, bool local, Quaternion qHead, bool qHeadSet, Vector3 draw)
        {
            if (_destroyed) return;
            Transform head = avatar != null && avatar.isHuman ? avatar.GetBoneTransform(HumanBodyBones.Head) : null;
            if (!want || head == null || helm == null || !helm.activeInHierarchy) { Release(); return; }
            float now = Time.unscaledTime;
            _qHead = qHead;
            _qHeadSet = qHeadSet;
            if (avatar != _anim || local != _local)
            {
                Release();
                _anim = avatar;
                _local = local;
                _saidFor = false;
                _tries = 0;
                _avoidMirror = false;
                _sig = 0;
            }
            if (!_want)
            {
                // helm-up: a new snapshot before the head is hidden
                _want = true;
                _needSnap = true;
                _tries = 0;
                _nextCheck = 0f;
            }
            if (now >= _nextCheck)
            {
                _nextCheck = now + CheckSeconds;
                FindCopies();
                int sig = Signature();
                if (sig != _sig) { if (_sig != 0) _needSnap = true; _sig = sig; }
            }
            Step(now);
            ApplyHeads();
            PlaceCard(head, helm, draw);
        }

        // ------------------------------------------------------------------ the copies and their heads

        private void FindCopies()
        {
            for (int i = 0; i < _copies.Count; i++) { Copy c = _copies[i]; c.Seen = false; _copies[i] = c; }
            GearAvatarRoots.VisualRoots(_anim, _roots);
            bool changed = false;
            for (int r = 0; r < _roots.Count; r++)
            {
                Transform t = _roots[r];
                if (t == null) continue;
                string kind = GearAvatarRoots.KindOf(t);
                if (kind == GearAvatarRoots.Teleport) continue;   // the teleport preview: no helm there, left whole
                Animator a = t.GetComponentInChildren<Animator>(true);
                if (a == null || !a.isHuman) continue;
                Transform h = a.GetBoneTransform(HumanBodyBones.Head);
                if (h == null) continue;
                int at = -1;
                for (int i = 0; i < _copies.Count; i++) if (_copies[i].Head == h) { at = i; break; }
                if (at >= 0)
                {
                    Copy c = _copies[at];
                    c.Seen = true;
                    if (c.Root != t && c.Root != null && c.Root.IsChildOf(t)) c.Root = t;   // the client root above the Animator
                    if (kind != null) c.Kind = kind;
                    _copies[at] = c;
                    continue;
                }
                var n = new Copy { Root = t, Head = h, Anim = a, Kind = kind ?? "model", Orig = h.localScale, Seen = true };
                // the local VR main copy is the client's (it hides that head itself); so is any head already collapsed
                n.Skip = (_local && kind == GearAvatarRoots.Main) || Tiny(h.localScale);
                _copies.Add(n);
                changed = true;
            }
            _roots.Clear();
            for (int i = _copies.Count - 1; i >= 0; i--)
            {
                if (_copies[i].Seen && _copies[i].Head != null) continue;
                RestoreHead(_copies[i]);
                _copies.RemoveAt(i);
                if (_snapCopy == i) _snapCopy = -1;
                else if (_snapCopy > i) _snapCopy--;
                changed = true;
            }
            if (changed && _ready) _needSnap = true;
        }

        private static bool Tiny(Vector3 s) { return s.sqrMagnitude < 1e-8f; }

        /// <summary>Each managed copy's head hidden while the face is ready, except the copy a snapshot is being taken from.</summary>
        private void ApplyHeads()
        {
            bool snapping = _state == Snap.Prime || _state == Snap.Camera;
            for (int i = 0; i < _copies.Count; i++)
            {
                Copy c = _copies[i];
                if (c.Skip || c.Head == null) continue;
                bool hide = _ready && !(snapping && i == _snapCopy);
                Vector3 s = c.Head.localScale;
                if (hide)
                {
                    if (!Tiny(s)) { c.Orig = s; c.Head.localScale = s * HiddenScale; }   // also after the client put it back (first person)
                    c.Hidden = true;
                }
                else if (c.Hidden)
                {
                    if (Tiny(s)) c.Head.localScale = c.Orig;
                    c.Hidden = false;
                }
                _copies[i] = c;
            }
        }

        private static void RestoreHead(Copy c)
        {
            if (c.Hidden && c.Head != null && Tiny(c.Head.localScale)) c.Head.localScale = c.Orig;
        }

        /// <summary>A hash of the snapshot copy's renderers and meshes (the avatar changed = a new snapshot).</summary>
        private int Signature()
        {
            int k = PickCopy();
            if (k < 0) return 1;
            Transform root = _copies[k].Root;
            if (root == null) return 1;
            root.GetComponentsInChildren(true, _rends);
            int sig = 17 + _copies.Count;
            for (int i = 0; i < _rends.Count; i++)
            {
                Renderer r = _rends[i];
                if (r == null) continue;
                sig = sig * 31 + r.GetInstanceID();
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) sig = sig * 31 + (smr.sharedMesh != null ? smr.sharedMesh.GetInstanceID() : 0);
            }
            _rends.Clear();
            return sig == 0 ? 2 : sig;
        }

        /// <summary>The copy to take the face from: the mirror copy (its head is never hidden by the client), else the followed one, else
        /// any other managed copy. -1 = none (every head is the client's).</summary>
        private int PickCopy()
        {
            int followed = -1, other = -1;
            for (int i = 0; i < _copies.Count; i++)
            {
                Copy c = _copies[i];
                if (c.Skip || c.Head == null || c.Root == null || !c.Root.gameObject.activeInHierarchy) continue;
                if (c.Kind == GearAvatarRoots.Mirror && !_avoidMirror) return i;
                if (c.Anim == _anim) followed = i;
                else if (other < 0) other = i;
            }
            return followed >= 0 ? followed : other;
        }

        // ------------------------------------------------------------------ the snapshot

        private void Step(float now)
        {
            int frame = Time.frameCount;
            switch (_state)
            {
                case Snap.Idle:
                    if (_needSnap && now >= _snapAt && _tries < MaxTries && CameraFree()) StartSnapshot(now);
                    break;
                case Snap.Prime:
                    // the copy's head was made whole and its skins set to update on the previous frame: render now
                    if (_owner != this) { CancelRender(); _state = Snap.Idle; _needSnap = true; break; }   // taken over (we stalled)
                    if (frame <= _stateFrame) break;
                    if (_snapCopy < 0 || !EnsureCamera()) { Fail("no snapshot camera"); break; }
                    _cam.targetTexture = _rt;
                    _cam.enabled = true;
                    _state = Snap.Camera;
                    _stateFrame = frame;
                    break;
                case Snap.Camera:
                    if (_owner != this) { CancelRender(); _state = Snap.Idle; _needSnap = true; break; }
                    if (frame - _stateFrame > StuckFrames) Fail("the snapshot camera did not render");
                    break;
                case Snap.Rendered:
                    if (SystemInfo.supportsAsyncGPUReadback && _rt != null)
                    {
                        if (_onRead == null) _onRead = OnRead;
                        _state = Snap.Reading;
                        _stateFrame = frame;
                        AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, _onRead);
                    }
                    else Finish(-1f, -1f);
                    break;
                case Snap.Reading:
                    if (frame - _stateFrame > ReadbackFrames) Finish(-1f, -1f);
                    break;
            }
        }

        private void StartSnapshot(float now)
        {
            _snapAt = now + MinGap;
            _snapCopy = PickCopy();
            if (_snapCopy < 0)
            {
                _tries = MaxTries;
                Say("Armour: face card for " + _label + ": no copy of the avatar with a whole head to take the face from (" + Kinds(false) + ") - the head stays drawn.", true);
                return;
            }
            _needSnap = false;
            EnsureCard();
            Copy c = _copies[_snapCopy];
            _snapKind = c.Kind;
            // the copy's head whole now (ApplyHeads skips it while snapping), so the bake below and next frame's skinning see it
            if (c.Hidden && Tiny(c.Head.localScale)) c.Head.localScale = c.Orig;
            c.Hidden = false;
            _copies[_snapCopy] = c;
            Measure(c);
            c.Root.GetComponentsInChildren(true, _skins);
            _offscreen.Clear();
            for (int i = 0; i < _skins.Count; i++) { _offscreen.Add(_skins[i].updateWhenOffscreen); _skins[i].updateWhenOffscreen = true; }
            _owner = this;
            _ownedAt = Time.frameCount;
            _state = Snap.Prime;
            _stateFrame = Time.frameCount;
        }

        /// <summary>The shared camera is free for us (or held by a card that stopped ticking: taken over).</summary>
        private bool CameraFree()
        {
            if (_owner == null || _owner == this) return true;
            if (!_owner._destroyed && Time.frameCount - _ownedAt <= 4 * StuckFrames) return false;
            ReleaseCamera(null);
            return true;
        }

        /// <summary>The face from the copy's head vertices (strongest bone = the head bone or one below it), in its facing frame.</summary>
        private void Measure(Copy c)
        {
            Transform head = c.Head;
            Quaternion inv = Quaternion.Inverse(Frame(c));
            Vector3 hp = head.position;
            _pts.Clear();
            Mesh baked = null;
            int meshes = 0, unread = 0;
            try
            {
                c.Root.GetComponentsInChildren(true, _skins);
                for (int k = 0; k < _skins.Count; k++)
                {
                    SkinnedMeshRenderer smr = _skins[k];
                    Mesh src = smr != null ? smr.sharedMesh : null;
                    if (src == null) continue;
                    if (!src.isReadable) { unread++; continue; }
                    Transform[] bones = smr.bones;
                    if (bones == null || bones.Length == 0) continue;
                    var isHead = new bool[bones.Length];
                    bool any = false;
                    for (int b = 0; b < bones.Length; b++) if (bones[b] != null && bones[b].IsChildOf(head)) { isHead[b] = true; any = true; }
                    if (!any) continue;
                    BoneWeight[] w = src.boneWeights;
                    if (baked == null) baked = new Mesh { name = "WoW face card bake" };
                    smr.BakeMesh(baked);
                    Vector3[] v = baked.vertices;
                    if (w.Length != v.Length) continue;
                    meshes++;
                    Matrix4x4 m = smr.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length; i++)
                    {
                        BoneWeight bw = w[i];
                        int bi = bw.boneIndex0; float best = bw.weight0;
                        if (bw.weight1 > best) { best = bw.weight1; bi = bw.boneIndex1; }
                        if (bw.weight2 > best) { best = bw.weight2; bi = bw.boneIndex2; }
                        if (bw.weight3 > best) { bi = bw.boneIndex3; }
                        if (bi < 0 || bi >= isHead.Length || !isHead[bi]) continue;
                        _pts.Add(inv * (m.MultiplyPoint3x4(v[i]) - hp));
                    }
                }
            }
            catch (Exception e)
            {
                _pts.Clear();
                if (_logs < MaxLogs) { _logs++; WowLog.Warn("Armour: face card for " + _label + ": the head could not be measured (" + e.GetType().Name + ": " + e.Message + ") - body-scale face used."); }
            }
            if (baked != null) UnityEngine.Object.Destroy(baked);
            _skins.Clear();
            string basis;
            float s = BodyScale(c, out basis);   // fix 2026-10-04: from the skeleton, not from where the copy's Animator object stands
            string how;
            if (FaceFromPoints(c, inv, hp, out how))
            {
                _measure = "measured from " + _pts.Count + " head vertices of " + meshes + " mesh(es), " + how + ", s " + s.ToString("0.00") + " (" + basis + ")";
                _gap = CamGap;
                BuildCard(true);
                _pts.Clear();
                return;
            }
            _pts.Clear();
            // body scale fallback (ours, see FallNote)
            _w = FallW * s; _h = FallH * s; _cx = 0f; _cy = FallY * s; _front = FallZ * s; _depth = FallDepth * s;
            _gap = CamGap + FallSlack * _front;   // fix 2026-10-04: the camera outside a face larger than the estimate
            BuildCard(false);
            _measure = "body-scale face (s " + s.ToString("0.00") + " from " + basis + ", " + FallNote + (unread > 0 ? "; " + unread + " unreadable mesh(es)" : "; no head vertices") + ")";
        }

        /// <summary>Fix 2026-10-04: the body-scale face's s = the head bone's T-pose height / HeadJoint (as UiArmourFitView: s = the head
        /// bone's height above the feet / 1.6), from the copy's humanoid Avatar (the copies share it) - else, as before, from the head's height
        /// above the copy's Animator object (wrong for a remote avatar, see the summary). Clamped to 0.3 .. 3 as before.</summary>
        private float BodyScale(Copy c, out string basis)
        {
            Avatar av = c.Anim != null && c.Anim.avatar != null ? c.Anim.avatar : (_anim != null ? _anim.avatar : null);
            float height;
            if (TPoseHeadHeight(av, c.Head, out height))
            {
                basis = "the T-pose head height " + height.ToString("0.00") + " m";
                return Mathf.Clamp(height / HeadJoint, 0.3f, 3f);
            }
            float above = c.Head.position.y - (c.Anim != null ? c.Anim.transform.position.y : c.Root.position.y);
            basis = "the head " + above.ToString("0.00") + " m over the Animator object (no usable skeleton)";
            return Mathf.Clamp(above / HeadJoint, 0.3f, 3f);
        }

        /// <summary>
        /// Fix 2026-10-04: the head bone's height above the avatar model's origin (its feet: Mecanim humanoid models stand on their origin)
        /// in the humanoid Avatar's T-pose, in world metres - Avatar.humanDescription.skeleton (each bone's T-pose local position, rotation
        /// and scale, by NAME) composed from the topmost skeleton bone above the head down to the head, times the world scale of the object
        /// above that bone (the model, which the client renames "Root": CS Roots/Base/BaseAvatarRoot.cs:140 - so the walk stops under it;
        /// its parents' scale, e.g. the client's avatar scale on its root, is included). Pose, IK, our poser and where the client places its
        /// roots do not enter. False when the skeleton cannot tell (not humanoid, a bone name not found, a height outside MinHeadHeight ..
        /// MaxHeadHeight). Public for ArmourPieces' helm size (the same question).
        /// </summary>
        public static bool TPoseHeadHeight(Avatar avatar, Transform head, out float height)
        {
            height = 0f;
            if (avatar == null || head == null) return false;
            try
            {
                if (!avatar.isHuman) return false;
                SkeletonBone[] sk = avatar.humanDescription.skeleton;
                if (sk == null || sk.Length == 0) return false;
                Chain.Clear();
                Transform top = null;
                for (Transform t = head; t != null && Chain.Count < 64; t = t.parent)
                {
                    int i = SkeletonIndex(sk, t.name);
                    if (i < 0) break;
                    Chain.Add(i);
                    top = t;
                }
                if (Chain.Count < 2 || top == null) { Chain.Clear(); return false; }   // the head and at least one bone above it
                Matrix4x4 m = Matrix4x4.identity;
                for (int k = Chain.Count - 1; k >= 0; k--)
                {
                    SkeletonBone b = sk[Chain[k]];
                    m = m * Matrix4x4.TRS(b.position, b.rotation, b.scale);
                }
                Chain.Clear();
                float scale = top.parent != null ? top.parent.lossyScale.y : 1f;
                height = m.m13 * scale;   // the head's T-pose position, y
                return height >= MinHeadHeight && height <= MaxHeadHeight;
            }
            catch (Exception) { Chain.Clear(); height = 0f; return false; }
        }

        private static int SkeletonIndex(SkeletonBone[] sk, string name)
        {
            for (int i = 0; i < sk.Length; i++) if (sk[i].name == name) return i;
            return -1;
        }

        /// <summary>The card's rectangle from the head points: cheek to cheek and chin to mid-forehead, from the eye bones when the
        /// avatar has them (else from the front of the head); false = nothing usable.</summary>
        private bool FaceFromPoints(Copy c, Quaternion inv, Vector3 hp, out string how)
        {
            how = "";
            if (_pts.Count < 50) return false;
            float zMax = float.MinValue, zMin = float.MaxValue, xMin = float.MaxValue, xMax = float.MinValue;
            for (int i = 0; i < _pts.Count; i++)
            {
                Vector3 p = _pts[i];
                if (p.z > zMax) zMax = p.z;
                if (p.z < zMin) zMin = p.z;
                if (p.x < xMin) xMin = p.x;
                if (p.x > xMax) xMax = p.x;
            }
            float slab = Mathf.Clamp((xMax - xMin) * SlabShare, MinSlab, MaxSlab);
            float fx0 = float.MaxValue, fx1 = float.MinValue, fy0 = float.MaxValue, fy1 = float.MinValue;
            int n = 0;
            for (int i = 0; i < _pts.Count; i++)
            {
                Vector3 p = _pts[i];
                if (p.z < zMax - slab) continue;
                n++;
                if (p.x < fx0) fx0 = p.x;
                if (p.x > fx1) fx1 = p.x;
                if (p.y < fy0) fy0 = p.y;
                if (p.y > fy1) fy1 = p.y;
            }
            if (n < 20) return false;
            float top, chin, w, cx;
            Transform le = c.Anim != null ? c.Anim.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            Transform re = c.Anim != null ? c.Anim.GetBoneTransform(HumanBodyBones.RightEye) : null;
            Vector3 l = le != null ? inv * (le.position - hp) : Vector3.zero, r = re != null ? inv * (re.position - hp) : Vector3.zero;
            float eyes = Mathf.Abs(r.x - l.x);
            if (le != null && re != null && eyes >= MinEyes && eyes <= MaxEyes)
            {
                float ex = (l.x + r.x) * 0.5f, ey = (l.y + r.y) * 0.5f;
                chin = float.MaxValue;
                for (int i = 0; i < _pts.Count; i++)
                {
                    Vector3 p = _pts[i];
                    if (p.z >= zMax - slab && p.y < ey && p.y < chin && Mathf.Abs(p.x - ex) < 0.5f * eyes) chin = p.y;
                }
                bool measured = chin < float.MaxValue && ey - chin <= 3f * eyes;
                if (!measured) chin = ey - ChinOverEyes * eyes;
                top = ey + TopOverEyes * eyes;
                w = WidthOverEyes * eyes;
                cx = ex;
                how = "eye bones " + eyes.ToString("0.000") + " m apart, chin " + (measured ? "measured" : "from the eyes");
            }
            else
            {
                chin = fy0;
                top = fy0 + (fy1 - fy0) * FallTopShare;
                w = (fx1 - fx0) * FallWidthShare;
                cx = (fx0 + fx1) * 0.5f;
                how = "no eye bones (front of the head " + (fx1 - fx0).ToString("0.000") + " x " + (fy1 - fy0).ToString("0.000") + " m)";
            }
            float h = top - chin;
            if (w < MinFace || w > MaxFace || h < MinFace || h > MaxFace) return false;
            _w = w; _h = h;
            _cx = cx;
            _cy = (top + chin) * 0.5f;
            _front = zMax;
            _depth = Mathf.Max(0.05f, zMax - zMin);
            return true;
        }

        /// <summary>The card's mesh: an oval over the face's rectangle, each vertex CardInset behind the face's surface there (the head
        /// points' frontmost depth on a ReliefX x ReliefY grid, smoothed) - or, without points, a gently curved oval. UVs as the snapshot
        /// sees it from the front (u = 0.5 - x: the avatar's right is the picture's left). Built on each snapshot only.</summary>
        private void BuildCard(bool relief)
        {
            if (_cardMesh == null) _cardMesh = new Mesh { name = "WoW face card " + _label };
            float x0 = _cx - _w * 0.5f, y0 = _cy - _h * 0.5f;
            float[] d = null;
            if (relief)
            {
                var raw = new float[ReliefX * ReliefY];
                for (int i = 0; i < raw.Length; i++) raw[i] = float.MinValue;
                float lo = float.MaxValue;
                for (int i = 0; i < _pts.Count; i++)
                {
                    Vector3 p = _pts[i];
                    float u = (p.x - x0) / _w, v = (p.y - y0) / _h;
                    if (u < 0f || u >= 1f || v < 0f || v >= 1f) continue;
                    int k = (int)(v * ReliefY) * ReliefX + (int)(u * ReliefX);
                    if (p.z > raw[k]) raw[k] = p.z;
                }
                for (int i = 0; i < raw.Length; i++) if (raw[i] > float.MinValue && raw[i] < lo) lo = raw[i];
                if (lo < float.MaxValue)
                {
                    for (int i = 0; i < raw.Length; i++) if (raw[i] == float.MinValue) raw[i] = lo;   // no surface there: as deep as the deepest
                    d = new float[raw.Length];
                    for (int y = 0; y < ReliefY; y++)
                        for (int x = 0; x < ReliefX; x++)
                        {
                            float sum = 0f; int cnt = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx >= ReliefX || yy >= ReliefY) continue;
                                    sum += raw[yy * ReliefX + xx]; cnt++;
                                }
                            d[y * ReliefX + x] = sum / cnt;
                        }
                }
            }
            int n = 1 + Rings * Segs;
            var vs = new Vector3[n];
            var uv = new Vector2[n];
            var nr = new Vector3[n];
            float front = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float x = 0f, y = 0f;
                if (i > 0)
                {
                    int r = 1 + (i - 1) / Segs, s = (i - 1) % Segs;
                    float f = (float)r / Rings, a = s * Mathf.PI * 2f / Segs;
                    x = 0.5f * f * Mathf.Cos(a);
                    y = 0.5f * f * Mathf.Sin(a);
                }
                float z = d != null ? Depth(d, x + 0.5f, y + 0.5f) - CardInset : _front - CardInset - Curve * 4f * x * x * _w;
                vs[i] = new Vector3(x * _w, y * _h, z);
                uv[i] = new Vector2(0.5f - x, 0.5f + y);
                nr[i] = Vector3.forward;
                if (z > front) front = z;
            }
            var t = new int[Segs * 3 + (Rings - 1) * Segs * 6];
            int ti = 0;
            for (int s = 0; s < Segs; s++) { t[ti++] = 0; t[ti++] = 1 + s; t[ti++] = 1 + (s + 1) % Segs; }
            for (int r = 1; r < Rings; r++)
                for (int s = 0; s < Segs; s++)
                {
                    int a0 = 1 + (r - 1) * Segs + s, a1 = 1 + (r - 1) * Segs + (s + 1) % Segs, b0 = a0 + Segs, b1 = a1 + Segs;
                    t[ti++] = a0; t[ti++] = b0; t[ti++] = b1;
                    t[ti++] = a0; t[ti++] = b1; t[ti++] = a1;
                }
            _cardMesh.Clear();
            _cardMesh.vertices = vs;
            _cardMesh.uv = uv;
            _cardMesh.normals = nr;
            _cardMesh.triangles = t;
            _cardMesh.RecalculateBounds();
            _cardFront = front;
        }

        /// <summary>The smoothed surface depth at (u, v) in 0..1 over the face's rectangle (bilinear between grid cell centres).</summary>
        private static float Depth(float[] d, float u, float v)
        {
            float fx = Mathf.Clamp(u * ReliefX - 0.5f, 0f, ReliefX - 1), fy = Mathf.Clamp(v * ReliefY - 0.5f, 0f, ReliefY - 1);
            int x0 = (int)fx, y0 = (int)fy, x1 = Mathf.Min(x0 + 1, ReliefX - 1), y1 = Mathf.Min(y0 + 1, ReliefY - 1);
            float tx = fx - x0, ty = fy - y0;
            float a = Mathf.Lerp(d[y0 * ReliefX + x0], d[y0 * ReliefX + x1], tx), b = Mathf.Lerp(d[y1 * ReliefX + x0], d[y1 * ReliefX + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>The copy's head facing frame now (the followed Animator's T-pose turn; the copies share the skeleton).</summary>
        private Quaternion Frame(Copy c)
        {
            return _qHeadSet ? c.Head.rotation * _qHead : (c.Anim != null ? c.Anim.transform.rotation : c.Head.rotation);
        }

        private void OnRead(AsyncGPUReadbackRequest req)
        {
            if (_destroyed || _state != Snap.Reading) return;
            if (req.hasError) { Finish(-1f, -1f); return; }
            NativeArray<Color32> px = req.GetData<Color32>();
            int w = req.width, h = req.height, inside = 0, content = 0, solid = 0;
            for (int y = 0; y < h; y++)
            {
                float dy = (y + 0.5f) / h - 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w - 0.5f;
                    if (dx * dx + dy * dy > 0.25f) continue;   // the oval (the card's shape)
                    inside++;
                    Color32 c = px[y * w + x];
                    if (c.a >= 128) solid++;
                    if (c.a > 2 || c.r > 2 || c.g > 2 || c.b > 2) content++;
                }
            }
            Finish(inside > 0 ? (float)content / inside : -1f, inside > 0 ? (float)solid / inside : -1f);
        }

        /// <summary>The snapshot is done: content / coverage = shares of the oval (-1 = not checked).</summary>
        private void Finish(float content, float coverage)
        {
            _state = Snap.Idle;
            if (content >= 0f && content < MinContent)
            {
                _tries++;
                _snapAt = Time.unscaledTime + RetrySeconds * _tries;
                _needSnap = true;
                bool wasMirror = _snapKind == GearAvatarRoots.Mirror;
                if (wasMirror) _avoidMirror = true;
                _ready = false;
                Say("Armour: face card for " + _label + ": the snapshot of the " + _snapKind + " copy came out empty (" + Pct(content) + " of the oval drawn) - the head stays drawn" +
                    (_tries < MaxTries ? "; trying again in " + (RetrySeconds * _tries).ToString("0") + " s" + (wasMirror ? " from the followed copy" : "") : "; giving up for this avatar") + ".", true);
                return;
            }
            _tries = 0;
            _alphaOk = coverage < 0f || coverage >= MinCoverage;
            if (_mat != null) _mat.SetFloat("_Cutoff", _alphaOk ? Cutoff : -1f);
            _ready = true;
            if (_saidFor) return;
            _saidFor = true;
            Say("Armour: face card for " + _label + ": snapshot of the " + _snapKind + " copy (" + _snapRenderers + " renderer(s), " + TexSize + " px, layer " + Ui.UiUnitPortrait.PortraitLayer +
                "), face " + _w.ToString("0.000") + " x " + _h.ToString("0.000") + " m " + _measure + ", card front " + _cardFront.ToString("0.000") + " m in front of the head bone, centre " +
                _cy.ToString("0.000") + " m up; " + (content < 0f ? "picture not checked" : Pct(content) + " of the oval drawn, " + Pct(coverage) + " opaque" + (_alphaOk ? "" : " (alpha not written: the card is drawn opaque)")) +
                "; head hidden on " + Kinds(true) + ".", false);
        }

        private void Fail(string why)
        {
            CancelRender();
            _state = Snap.Idle;
            _ready = false;   // never a hidden head without a face
            _tries++;
            _snapAt = Time.unscaledTime + RetrySeconds * _tries;
            _needSnap = true;
            Say("Armour: face card for " + _label + ": " + why + " - the head stays drawn" + (_tries < MaxTries ? "; trying again." : "; giving up for this avatar."), true);
        }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }

        private string Kinds(bool managed)
        {
            string s = "";
            for (int i = 0; i < _copies.Count; i++)
            {
                if (managed && _copies[i].Skip) continue;
                s += (s.Length > 0 ? ", " : "") + _copies[i].Kind + (_copies[i].Skip ? " (the client's)" : "");
            }
            if (managed)
            {
                string left = "";
                for (int i = 0; i < _copies.Count; i++) if (_copies[i].Skip) left += (left.Length > 0 ? ", " : "") + _copies[i].Kind;
                if (left.Length > 0) s += "; left to the client: " + left;
            }
            return s.Length > 0 ? s : "no copies";
        }

        private void Say(string line, bool warn)
        {
            if (_logs >= MaxLogs) return;
            _logs++;
            if (warn) WowLog.Warn(line);
            else WowLog.Info(line);
        }

        // ------------------------------------------------------------------ the shared camera

        private static bool EnsureCamera()
        {
            if (_cam != null) return true;
            if (_camGo != null) return false;   // a failed set-up is not retried
            try
            {
                _idleRt = new RenderTexture(4, 4, 16, RenderTextureFormat.ARGB32) { name = "WoW face card idle" };
                _idleRt.Create();
                _camGo = new GameObject(CameraName);
                _cam = _camGo.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.orthographic = true;
                _cam.targetTexture = _idleRt;   // never the screen, also when idle
                _cam.cullingMask = 1 << Ui.UiUnitPortrait.PortraitLayer;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent around the face
                _cam.depth = CameraDepth;
                _cam.allowHDR = false;
                _cam.allowMSAA = false;
                _cam.useOcclusionCulling = false;
                _cam.stereoTargetEye = StereoTargetEyeMask.None;   // never an XR eye
                UniversalAdditionalCameraData data = _cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderShadows = false;
                    data.requiresDepthOption = CameraOverrideOption.Off;
                    data.requiresColorOption = CameraOverrideOption.Off;
                    data.renderPostProcessing = false;
                    data.antialiasing = AntialiasingMode.None;
                    data.volumeLayerMask = 0;
                }
                if (!_hooked)
                {
                    _hooked = true;
                    RenderPipelineManager.beginCameraRendering += OnBegin;
                    RenderPipelineManager.endCameraRendering += OnEnd;
                }
                return true;
            }
            catch (Exception e)
            {
                _cam = null;
                WowLog.Warn("Armour: face card camera set-up failed (" + e.Message + ") - heads stay drawn under helms.");
                return false;
            }
        }

        private static void OnBegin(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam) return;
            ArmourFaceCard o = _owner;
            if (o == null || o._destroyed || o._state != Snap.Camera) return;
            try { o.BeginDraw(); }
            catch (Exception e) { o.EndDraw(); if (o._logs < MaxLogs) { o._logs++; WowLog.Warn("Armour: face card snapshot set-up failed: " + e.Message); } }
        }

        private static void OnEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _cam) return;
            ArmourFaceCard o = _owner;
            if (o != null && !o._destroyed && o._state == Snap.Camera)
            {
                bool drew = o._moved.Count > 0;
                o.EndDraw();
                if (drew) o._state = Snap.Rendered;
                else o.Fail("nothing of the avatar to draw");
            }
            else ReleaseCamera(null);
        }

        private void BeginDraw()
        {
            _moved.Clear();
            _layers.Clear();
            if (_snapCopy < 0 || _snapCopy >= _copies.Count) return;
            Copy c = _copies[_snapCopy];
            if (c.Root == null || c.Head == null) return;
            c.Root.GetComponentsInChildren(false, _rends);
            for (int i = 0; i < _rends.Count; i++)
            {
                Renderer r = _rends[i];
                if (r == null || !r.enabled || Ui.UiKit.IsUiLayer(r.gameObject.layer)) continue;
                GameObject g = r.gameObject;
                _moved.Add(g);
                _layers.Add(g.layer);
                g.layer = Ui.UiUnitPortrait.PortraitLayer;
            }
            _rends.Clear();
            _snapRenderers = _moved.Count;
            if (_moved.Count == 0) return;
            // orthographic, from the front of the face: the picture is the card's rectangle (the oval inside it)
            Quaternion f = Frame(c);
            Vector3 fwd = f * Vector3.forward, up = f * Vector3.up;
            Vector3 at = c.Head.position + f * new Vector3(_cx, _cy, _front + _gap);
            _camGo.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-fwd, up));
            _cam.orthographicSize = _h * 0.5f;
            _cam.aspect = _w / Mathf.Max(1e-4f, _h);
            _cam.nearClipPlane = 0.005f;
            _cam.farClipPlane = _gap + _depth * FarShare;   // the far plane where it was (_gap: CamGap, or more for a body-scale face)
            KeyLight();
        }

        private void EndDraw()
        {
            for (int i = _moved.Count - 1; i >= 0; i--)   // backwards: an object met twice gets its first layer back
            {
                GameObject g = _moved[i];
                if (g != null) g.layer = _layers[i];
            }
            _moved.Clear();
            _layers.Clear();
            Unlight();
            RestoreSkins();
            ReleaseCamera(this);
        }

        private void RestoreSkins()
        {
            for (int i = 0; i < _skins.Count && i < _offscreen.Count; i++) if (_skins[i] != null) _skins[i].updateWhenOffscreen = _offscreen[i];
            _skins.Clear();
            _offscreen.Clear();
        }

        /// <summary>A snapshot in progress stopped (released, dropped, failed): the skins, the camera and the head back.</summary>
        private void CancelRender()
        {
            if (_moved.Count > 0)
            {
                for (int i = _moved.Count - 1; i >= 0; i--) if (_moved[i] != null) _moved[i].layer = _layers[i];
                _moved.Clear();
                _layers.Clear();
                Unlight();
            }
            RestoreSkins();
            if (_owner == this) ReleaseCamera(this);
        }

        private static void ReleaseCamera(ArmourFaceCard who)
        {
            if (who != null && _owner != who) return;
            _owner = null;
            if (_cam != null)
            {
                _cam.enabled = false;
                _cam.targetTexture = _idleRt;
            }
        }

        private static void KeyLight()
        {
            _savedFog = RenderSettings.fog;
            RenderSettings.fog = false;
            _savedAmbient = RenderSettings.ambientProbe;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(Ambient, Ambient, Ambient));
            RenderSettings.ambientProbe = sh;
            _litSun = Sun();
            if (_litSun != null)
            {
                Transform t = _litSun.transform;
                _savedSunRot = t.rotation;
                _savedSunIntensity = _litSun.intensity;
                _savedSunColor = _litSun.color;
                t.rotation = _camGo.transform.rotation * Quaternion.Euler(KeyPitch, KeyYaw, 0f);
                _litSun.intensity = KeyIntensity;
                _litSun.color = Color.white;
            }
            _lit = true;
        }

        private static void Unlight()
        {
            if (!_lit) return;
            _lit = false;
            RenderSettings.fog = _savedFog;
            RenderSettings.ambientProbe = _savedAmbient;
            if (_litSun != null)
            {
                _litSun.transform.rotation = _savedSunRot;
                _litSun.intensity = _savedSunIntensity;
                _litSun.color = _savedSunColor;
            }
            _litSun = null;
        }

        /// <summary>The directional light WowLighting drives (as UiUnitPortrait.Sun: RenderSettings.sun, else the first directional light,
        /// searched at most every 5 s).</summary>
        private static Light Sun()
        {
            if (_sun != null && _sun.isActiveAndEnabled) return _sun;
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional) return RenderSettings.sun;
            if (Time.unscaledTime < _nextSunSearch) return null;
            _nextSunSearch = Time.unscaledTime + 5f;
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != null && l.type == LightType.Directional && l.isActiveAndEnabled) { _sun = l; return l; }
            return null;
        }

        // ------------------------------------------------------------------ the card

        private void EnsureCard()
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(TexSize, TexSize, 24, RenderTextureFormat.ARGB32)
                {
                    name = "WoW face card " + _label,
                    useMipMap = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                };
                _rt.Create();
            }
            if (_mat == null)
            {
                Shader sh = WowShaders.Find(Models.M2Materials.ShaderName);
                if (sh == null) sh = WowShaders.Find("Universal Render Pipeline/Unlit");
                if (sh != null)
                {
                    _mat = new Material(sh) { name = "WoW face card " + _label };
                    if (sh.name == Models.M2Materials.ShaderName)
                    {
                        // M2Materials blend mode 1 (AlphaKey) + render flag 0x01 (unlit), two-sided
                        _mat.SetTexture("_MainTex", _rt);
                        _mat.SetColor("_Color", new Color(Shade, Shade, Shade, 1f));
                        _mat.SetFloat("_Unlit", 1f);
                        _mat.SetFloat("_UvSet", 0f);
                        _mat.SetFloat("_Cull", (float)CullMode.Off);
                        _mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                        _mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                        _mat.SetFloat("_ZWrite", 1f);
                        _mat.SetFloat("_FogMode", 0f);
                        _mat.SetFloat("_Cutoff", Cutoff);
                        _mat.renderQueue = (int)RenderQueue.AlphaTest;
                        _mat.SetOverrideTag("RenderType", "TransparentCutout");
                    }
                    else if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _rt);   // URP Unlit fallback: opaque
                }
            }
            if (_card == null)
            {
                _card = new GameObject("WoW face card " + _label);
                _card.SetActive(false);
                if (_cardMesh == null) _cardMesh = new Mesh { name = "WoW face card " + _label };   // filled by BuildCard
                _card.AddComponent<MeshFilter>().sharedMesh = _cardMesh;
                _cardMr = _card.AddComponent<MeshRenderer>();
                _cardMr.sharedMaterial = _mat;
                _cardMr.shadowCastingMode = ShadowCastingMode.Off;
                _cardMr.receiveShadows = false;
                _cardMr.lightProbeUsage = LightProbeUsage.Off;
                _cardMr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        private void PlaceCard(Transform head, GameObject helm, Vector3 draw)
        {
            bool show = _ready && _card != null && _mat != null;
            if (show)
            {
                Transform ht = helm.transform;
                if (_card.transform.parent != ht.parent) _card.transform.SetParent(ht.parent, false);
                if (_card.layer != helm.layer) _card.layer = helm.layer;   // the photo layer with the helm
                Quaternion rot = _qHeadSet ? head.rotation * _qHead : (_anim != null ? _anim.transform.rotation : head.rotation);
                Vector3 hp = head.position + draw;
                float back = 0f;
                if (HelmFront(helm))
                {
                    // never in front of the helm's front (the wearer may have moved the helm back)
                    float zf = Vector3.Dot(ht.TransformPoint(_helmFront) - hp, rot * Vector3.forward);
                    if (_cardFront > zf - HelmClear) back = zf - HelmClear - _cardFront;
                }
                _card.transform.SetPositionAndRotation(hp + rot * new Vector3(_cx, _cy, back), rot);
                if (_card.transform.localScale != Vector3.one) _card.transform.localScale = Vector3.one;
            }
            if (_card != null && _card.activeSelf != show) _card.SetActive(show);
        }

        /// <summary>The helm's front centre in its own frame (+z forward), from its meshes' bounds (once per helm object).</summary>
        private bool HelmFront(GameObject helm)
        {
            int id = helm.GetInstanceID();
            if (id == _helmBoxFor) return _helmFrontOk;
            _helmBoxFor = id;
            _helmFrontOk = false;
            Matrix4x4 inv = helm.transform.worldToLocalMatrix;
            bool have = false;
            var box = new Bounds();
            foreach (Renderer r in helm.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                var sk = r as SkinnedMeshRenderer;
                if (sk != null) mesh = sk.sharedMesh;
                if (mesh == null) continue;
                Bounds b = mesh.bounds;
                Matrix4x4 toHelm = inv * r.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) != 0 ? 1f : -1f, (k & 2) != 0 ? 1f : -1f, (k & 4) != 0 ? 1f : -1f));
                    Vector3 p = toHelm.MultiplyPoint3x4(corner);
                    if (!have) { box = new Bounds(p, Vector3.zero); have = true; }
                    else box.Encapsulate(p);
                }
            }
            _helmFrontOk = have && box.size.z > 1e-4f;
            _helmFront = new Vector3(box.center.x, box.center.y, box.max.z);
            return _helmFrontOk;
        }

        // ------------------------------------------------------------------ release / destroy

        /// <summary>The helm went, the flag went off, or the avatar is not drawn: every head back, the card hidden, no snapshot running.
        /// The next show takes a new snapshot first.</summary>
        public void Release()
        {
            if (_state == Snap.Prime || _state == Snap.Camera) CancelRender();
            _state = Snap.Idle;
            _ready = false;
            _want = false;
            for (int i = 0; i < _copies.Count; i++) RestoreHead(_copies[i]);
            _copies.Clear();
            _snapCopy = -1;
            if (_card != null && _card.activeSelf) _card.SetActive(false);
        }

        public void Destroy()
        {
            if (_destroyed) return;
            Release();
            _destroyed = true;
            _anim = null;
            if (_card != null) UnityEngine.Object.Destroy(_card);
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            if (_cardMesh != null) UnityEngine.Object.Destroy(_cardMesh);
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
            _card = null; _cardMr = null; _mat = null; _rt = null; _cardMesh = null;
            if (--_alive > 0) return;
            // the last card: the shared camera goes too
            if (_hooked)
            {
                _hooked = false;
                RenderPipelineManager.beginCameraRendering -= OnBegin;
                RenderPipelineManager.endCameraRendering -= OnEnd;
            }
            _owner = null;
            if (_camGo != null) UnityEngine.Object.Destroy(_camGo);
            if (_idleRt != null) { _idleRt.Release(); UnityEngine.Object.Destroy(_idleRt); }
            _camGo = null; _cam = null; _idleRt = null;
        }
    }
}
