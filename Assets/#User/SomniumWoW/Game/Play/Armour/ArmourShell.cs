// iteration 11 - owner avatar (proposal-avatar.md 6.A, phase A2): "Immersive armour: Full (experimental)" - the rest of the armour
// (chest, legs, gloves, boots, belt, bracers, tabard, cloak, robe skirt) drawn on the Somnium avatar as an armour-only WoW body
// shell, skinned to a WoW skeleton that is posed FROM the avatar every LateUpdate (the inverse of GearPuppet / GearPoser's retarget).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Models;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Armour
{
    /// <summary>
    /// One per avatar (local or remote) while the wearer's mode is 2:
    /// - Look: ArmourShellLook (worker thread) - the Human model of the wearer's gender (the helm's rule, user decision U2), its body
    ///   texture with ONLY the armour layers (skin fully transparent) and the armour geosets; built (M2Factory, one SkinnedMeshRenderer)
    ///   when the gear, the gender or Hide Cloak change - at most one shell build per frame for all avatars.
    /// - Material: the body-texture batches get alpha test 0.5 (ours) so the unpainted skin areas vanish; the cape keeps its own.
    /// - Rig: the WoW bones (M2Humanoid.Find - the map GearPuppet uses) are grouped by their nearest mapped joint into 17 segments
    ///   (hips, chest, neck, clavicles, upper arms, forearms, hands, thighs, shins, feet). Each segment is one transform posed from the
    ///   avatar: origin = the avatar joint, axis = the avatar's segment (hips -> chest -> neck -> head along its spine; upper arm ->
    ///   lower arm -> hand -> knuckles; upper leg -> lower leg -> foot -> toes), twist from the avatar bone (its T-pose turn taken out,
    ///   as the helm) or from the body frame, scale = avatar length / WoW bind length along the axis and a thickness across it (avatar
    ///   shoulder or hip span / WoW's, x Armour fit). The WoW bones hang under their segment with the bind frame taken out, so every
    ///   vertex goes to joint + segment (WoW bind offset) - wrists, ankles and neck land on the avatar's. No per-frame allocation.
    ///   The torso (hips, chest) is matched per height instead (RemapTorso): WoW's torso hangs in front of its spine pivots and is
    ///   wedge-shaped (deep chest, thin neck base), an avatar's sits around its spine bones - so the shell's own torso vertices are
    ///   moved once per avatar onto the avatar's measured cross-sections and those two segments scale only by the Armour fit.
    /// - First person (VR, desktop first person; the local player only): the neck segment collapses, so nothing of the shell is in
    ///   front of the eyes; the body batches cull back faces, so from inside the chest the shell is not seen.
    /// - Cape (cape agent 2026-10-04): ArmourCape re-skins the cape to its own physics rows (created in Rig, re-bound by FitCape,
    ///   stepped after Place, dropped with the shell).
    /// </summary>
    public sealed class ArmourShell
    {
        public const float AlphaCutoff = 0.5f;          // ours: the body texture's alpha test
        public const int ShellRace = 1;                 // the Human model of the wearer's gender (user decision U2, as the helm)
        public const float ThicknessBase = 1.0f;        // ours: x Armour fit
        public const float Margin = 1.10f;              // ours: the shell sits this much outside the avatar's measured surface
        public const float ThicknessFallback = 1.65f;   // ours: x the joint-span ratio when the avatar's meshes cannot be read (the median
                                                        // measured thickness / joint-span ratio over the lab's RPM Men and RPM Women: 1.64 / 1.66)
        public const float ExtentPercentile = 0.98f;    // ours: the avatar / WoW surface distance taken (robust to a stray vertex)
        public const int Bins = 4;                      // ours: height bins along a segment for the thickness match
        public const float BinBoost = 1.2f;             // ours: a bin may thicken its segment up to this x the whole-segment ratio
        public const float TorsoBinDepth = 0.35f;       // ours: a WoW torso bin counts when at least this x the deepest bin's depth
        public const float TorsoClearance = 0.025f;     // ours: m the torso shell stays outside the avatar's front, back and sides (lab: 1 cm left the RPM sweater showing at the waist)
        public const float TorsoRatioMin = 0.5f, TorsoRatioMax = 2.5f;   // ours: the torso remap's depth ratio limits
        public const float FingerTip = 0.8f;            // ours: a glove finger reaches past the avatar's last finger joint by this x its last bone
        // Lead (2026-10-04, the user's Charger screenshot: boots like a bell, "too large and wrong scale"; Player.log: "L hand 3.00/3.00 ...
        // L foot 3.00/3.00", the 3x clamp): an avatar's posed fingers and its shoe's height above the ankle count as the hand's / foot's
        // thickness against WoW's thin bare hand / foot. Hands and feet are now at most ExtremityOverLimb x the larger ratio of the limb
        // they sit on (forearm / shin); the lab's RPM avatars (hands 1.24/0.96 and 0.99/1.42 against forearms 1.11/0.93 and 1.60/1.44,
        // feet 0.82/1.47 against shins 1.00/1.18) stay as they were, the women's trainers (foot z 3.00, shin 1.58) are capped at 2.37. Ours.
        public const float ExtremityOverLimb = 1.5f;
        public const float ExtremityTipMax = 1.6f;
        // Lead (2026-10-04, the user: "remote players do not see my armour correctly ... make sure they are synced when in range"): the
        // avatar was measured ONCE, at the first frame its shell was placed - for another player that is often the client's culling
        // stand-in (far away / still loading), not the avatar that later comes into range. The avatar's skinned meshes are now checked
        // every MeshCheckSeconds (renderer, vertex count, shown) and measured again when they change. Ours.
        public const float MeshCheckSeconds = 1f;
        // Lead (2026-10-04, the user: after a relog "my boots are now huge"; Player.log: logged in DEAD, "L shin 3.00/3.00 ... L foot ->
        // <=4.50" - the avatar was measured lying in the death pose, so its shins read 3x thick and the boots' cap followed): measure
        // only an UPRIGHT avatar - hips at least UprightHips x the leg length above the feet and the spine within UprightTilt of
        // vertical. A VR player who never stands (seated play) is measured anyway after UprightWaitSeconds. A hand / foot also never
        // above ExtremityAbsMax, whatever its limb measures. Ours.
        public const float UprightHips = 0.75f, UprightTilt = 35f, UprightWaitSeconds = 20f, ExtremityAbsMax = 2f;      // ours: a boot / glove at most this x WoW's own length to the shoe's / finger's tip
        // Lead fix (2026-10-04, the user: "you're not syncing the armour correctly for remote players again ... I went behind them, must
        // have triggered frustrum and now I look all messed up"). Player.log (another player walking off and back): "humanoid Animator
        // 'Root' (NonXRMainRoot ...)", "measured the avatar: 0 meshes, 0 vertices, 2 not readable, 0 of 14 segments" - past the client's
        // near distance (AvatarsCullingSystem: 15 m, nearest 8) the client swaps the avatar for its CULLING stand-in (a generic model,
        // unreadable meshes, its own Animator), and the shell measured THAT, so it fell back to WoW's own body thickness ("huge and
        // blobby"). Now: the stand-in (GearAvatarRoots.IsStandIn) is never measured - it wears the avatar's last good measurement, or
        // nothing until the avatar itself was measured; a measurement with no readable mesh or under GoodSegments of the 14 segments never
        // replaces a good one; and good ones are kept per avatar (its humanoid Avatar, its meshes, the shell's gender, the last
        // MeasureCacheSize), so the avatar coming back into range is fitted again at once, without waiting to be seen standing. Ours.
        public const int GoodSegments = 10, MeasureCacheSize = 4;
        // Lead fix (2026-10-04, the user: "Also the feet are HUGE" - "I have to lower it to pretty much the lowest it can go", Size 0.5 of
        // the old default; Player.log: "L foot 3.00/3.00 -> <=1.65" - the avatar's shoe against WoW's BARE foot always reads at the 3.0
        // clamp, and WoW's boot geosets already wrap that foot): a foot at most FootOverShin x its shin's larger ratio and FootAbsMax, its
        // toe at most FootTipMax x WoW's own length. Ours (about the user's chosen size).
        public const float FootOverShin = 0.9f, FootAbsMax = 1.4f, FootTipMax = 1.0f;
        // Lead fix (2026-10-04, the user, with a screenshot: "Even reset to defaults, my ASS sticks out far too much"; Player.log: robe
        // "HumanMale geosets 0+802+1302", "torso remap: ... depth x0.92..1.50, centre moved -10..-6 cm"): the robe skirt's vertices below
        // the pelvis took the pelvis bin's remap ("the end bins held") all the way to the hem - moved back and deepened like the avatar's
        // seat. Below the lowest hips bin's middle the remap now fades out over SkirtFade to WoW's own shape (the skirt hangs from the
        // legs, which are fitted on their own). Ours.
        public const float SkirtFade = 0.15f;
        // Lead fix (2026-10-04, the user, in full body tracking: "the legs ARE HUGE"; Player.log: "L shin 3.00/3.00" - 0.76/1.10 without the
        // trackers): a measurement taken while the trackers pull a limb's bones away from its mesh (calibration, startup) reads at the 3.0
        // clamp. A limb segment (upper arm, forearm, thigh, shin) at or over LimbOutlier is an OUTLIER: it takes its parent limb's ratio
        // (forearm <- upper arm, shin <- thigh) or WoW's own (1), the measurement does not count as good (never cached, the last good one
        // wins), and it is taken again RetrySeconds later, at most MaxRetries times. Ours.
        public const float LimbOutlier = 2.5f, RetrySeconds = 5f;
        public const int MaxRetries = 12;
        private float _retryAt;
        private int _retries;

        private const int SHips = 0, SChest = 1, SNeck = 2, SLClav = 3, SRClav = 4, SLUpper = 5, SRUpper = 6, SLFore = 7, SRFore = 8,
                          SLHand = 9, SRHand = 10, SLThigh = 11, SRThigh = 12, SLShin = 13, SRShin = 14, SLFoot = 15, SRFoot = 16,
                          SFingers = 17, SegCount = 27;   // fingers: 17 + side x 5 + (0 thumb, 1 index, 2 middle, 3 ring, 4 little), L then R
        private static readonly string[] SegNames = { "hips", "chest", "neck", "L clavicle", "R clavicle", "L upper arm", "R upper arm", "L forearm",
                                                      "R forearm", "L hand", "R hand", "L thigh", "R thigh", "L shin", "R shin", "L foot", "R foot",
                                                      "L thumb", "L index", "L middle", "L ring", "L little", "R thumb", "R index", "R middle", "R ring", "R little" };

        private sealed class Job { public volatile bool Done; public ArmourShellLook Look; public long Ms; public string Error; }

        private static Transform _container;
        private static int _buildFrame = -1;

        private readonly string _label;
        /// <summary>The wanted armour: ItemDisplayInfo ids by equipment slot 0..18 (the caller fills it before Want).</summary>
        public readonly int[] Items = new int[19];
        private readonly int[] _built = new int[19], _jobItems = new int[19];
        private int _builtGender = -1, _jobGender = -1;
        private bool _builtHideCloak, _jobHideCloak, _haveBuilt;
        private Job _job;
        private string _said;

        // the shell
        private GameObject _go;
        private SkinnedMeshRenderer _smr;
        private Material[] _matsAll, _matsNoCape;   // the cape off for our own eye (first person)
        private bool _capeOffNow;
        private Transform _root;
        private readonly Transform[] _seg = new Transform[SegCount];
        private readonly Vector3[] _bStart = new Vector3[SegCount];
        private readonly float[] _bLen = new float[SegCount];
        private readonly Quaternion[] _bRot = new Quaternion[SegCount];
        private readonly Vector3[] _bClav = new Vector3[2], _bClavR = new Vector3[2];   // the clavicle pivots in the chest segment's bind frame (R: torso remap applied)
        private float _bHipSpan, _bShoulderSpan, _bChestFrac;

        // the avatar (calibrated once per Animator)
        private Animator _calFor;
        private bool _calOk;
        private Transform _hips, _spine, _chest, _upperChest, _neck, _head;
        private readonly Transform[] _ua = new Transform[2], _la = new Transform[2], _hand = new Transform[2], _ul = new Transform[2],
                                     _ll = new Transform[2], _foot = new Transform[2], _toes = new Transform[2];
        private readonly Transform[] _fing = new Transform[8];   // L index, middle, ring, little; R the same (proximal)
        private readonly Transform[] _af = new Transform[30];    // the avatar's fingers: proximal, intermediate, distal (L thumb .. little, R)
        private readonly int[] _fingBase = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };   // the WoW finger base bones (same order)
        private readonly bool[] _fingOk = new bool[10];          // the avatar has this finger: the glove's finger follows it, else the hand
        private Transform[] _wBones;                             // the WoW bones, their bind pivots and segments (to re-hang the fingers)
        private Vector3[] _wPivot;
        private int[] _wSeg;
        private readonly Quaternion[] _tOff = new Quaternion[SegCount];
        private readonly bool[] _tOffOk = new bool[SegCount];
        private readonly Vector3[] _spinePts = new Vector3[5];
        // this frame's avatar segments (origin, rotation: up = the axis, length) and the measured thickness
        private readonly Vector3[] _aPos = new Vector3[SegCount];
        private readonly Quaternion[] _aRot = new Quaternion[SegCount];
        private readonly float[] _aLen = new float[SegCount];
        private readonly float[] _wExt = new float[SegCount * Bins * 5];   // WoW bare body per height bin: percentile +x, -x, y, +z, -z (bind frame)
        private readonly Vector3[] _thick = new Vector3[SegCount];    // avatar / WoW: x, shoe tip (feet), z; 0 = not measured
        // the torso remap (hips, chest; bins hips 0..3 then chest 0..3): WoW's bare body per height bin, signed front-back z (bind
        // frame); the profile per bin: WoW's centre and half depth, the avatar's centre and the wanted half depth
        private readonly float[] _wLoZ = new float[2 * Bins], _wHiZ = new float[2 * Bins];
        private readonly int[] _wNZ = new int[2 * Bins];
        private readonly bool[] _pOk = new bool[2 * Bins];
        private readonly float[] _pCW = new float[2 * Bins], _pHW = new float[2 * Bins],
                                 _pCA = new float[2 * Bins], _pTZ = new float[2 * Bins];
        private Mesh _mesh;            // the shell's own skinned mesh (built per shell, readable) and its original vertices
        private Vector3[] _v0;
        private int[] _vSeg;           // each vertex's strongest bone's segment
        private float _kHip, _kSpan;
        private bool _measured;
        private int _meshSig, _remeasures, _recalibrations;
        private bool _waitSaid, _uprightSaid;
        /// <summary>Set by the caller each frame: the "measure anyway after UprightWaitSeconds" fallback may be used - only for the local
        /// player while not dead, sitting, mounted or on a taxi (a seated VR player); never for another player's avatar.</summary>
        public bool AllowUprightTimeout;
        private float _uprightSince = -1f;
        private Avatar _calAvatar;
        private float _meshCheckAt;
        // lead fix 2026-10-04: good measurements (see GoodSegments) and the state of the followed Animator
        private sealed class Measured
        {
            public Avatar Avatar;
            public int Sig, Gender;
            public string Name;
            public readonly Vector3[] Thick = new Vector3[SegCount];
            public readonly bool[] POk = new bool[2 * Bins];
            public readonly float[] PCW = new float[2 * Bins], PHW = new float[2 * Bins], PCA = new float[2 * Bins], PTZ = new float[2 * Bins];
            public readonly float[] CentreZ = new float[2];
        }
        private readonly List<Measured> _known = new List<Measured>(MeasureCacheSize);
        private Measured _lastGood;
        private bool _standIn, _needMeasure, _collarOff;
        private readonly List<SkinnedMeshRenderer> _sigList = new List<SkinnedMeshRenderer>();

        /// <summary>Lead (2026-10-04): the wearer's fit per piece - ArmourAdjust slots 3 chest .. 9 cloak (0-2 are ArmourPieces'); set before Place.</summary>
        public readonly PieceAdjust[] Adjust = new PieceAdjust[ArmourAdjust.Slots];
        private bool[] _capeMat;          // the cape's materials (= submeshes)
        private int[] _capeV;             // the cape's vertices
        private Vector3 _capeTop;         // the cape's top middle (bind space): its size and offset pivot
        private Vector3[] _vBase, _vWork; // the shell's vertices after the torso remap, and the work copy with the cloak's fit
        private int _capeKey = int.MinValue;
        private ArmourCape _cape;                 // cape agent 2026-10-04: the cape's own physics (ArmourCape), null = none
        private ArmourCape.Pose _capePose;

        public ArmourShell(string label)
        {
            _label = label;
            for (int i = 0; i < Adjust.Length; i++) Adjust[i] = PieceAdjust.Default;
        }

        public bool Built { get { return _go != null; } }

        /// <summary>Lead (2026-10-04, the fitting view): the shell's renderers while it is shown, appended.</summary>
        public void CollectRenderers(List<Renderer> into)
        {
            if (_go == null || !_go.activeInHierarchy) return;
            foreach (Renderer r in _go.GetComponentsInChildren<Renderer>(false)) if (r.enabled) into.Add(r);
        }

        private static Transform Container(Transform gameRoot)
        {
            if (_container != null) return _container;
            if (gameRoot == null) return null;
            Transform found = gameRoot.Find(ArmourPieces.ContainerName);
            if (found == null)
            {
                var go = new GameObject(ArmourPieces.ContainerName);
                found = go.transform;
                found.SetParent(gameRoot, false);
            }
            _container = found;
            return _container;
        }

        // ------------------------------------------------------------------------------------------------ build (on change only)

        /// <summary>Items (filled by the caller), the wearer's gender and Hide Cloak: composes on a worker when they change, builds when ready.</summary>
        public void Want(WowFiles files, int gender, bool hideCloak, Transform gameRoot)
        {
            gender = gender == 1 ? 1 : 0;
            if (files == null) return;
            if (_haveBuilt && gender == _builtGender && hideCloak == _builtHideCloak && Same(Items, _built)) return;
            if (_job != null)
            {
                bool forNow = gender == _jobGender && hideCloak == _jobHideCloak && Same(Items, _jobItems);
                if (!_job.Done) return;                 // one composition at a time; a stale one is dropped when it ends
                if (!forNow) { _job = null; }
                else
                {
                    if (_buildFrame == Time.frameCount) return;
                    _buildFrame = Time.frameCount;
                    Job j = _job;
                    _job = null;
                    Finish(files, j, gameRoot);
                    return;
                }
            }
            Start(files, gender, hideCloak);
        }

        private static bool Same(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private void Start(WowFiles files, int gender, bool hideCloak)
        {
            Array.Copy(Items, _jobItems, Items.Length);
            _jobGender = gender;
            _jobHideCloak = hideCloak;
            var job = new Job();
            _job = job;
            var items = (int[])Items.Clone();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var sw = Stopwatch.StartNew();
                try { job.Look = ArmourShellLook.Compose(files, ShellRace, gender, items, hideCloak); }
                catch (Exception e) { job.Error = e.Message; }
                job.Ms = sw.ElapsedMilliseconds;
                job.Done = true;
            });
        }

        private void Finish(WowFiles files, Job j, Transform gameRoot)
        {
            Drop();
            Array.Copy(_jobItems, _built, _built.Length);
            _builtGender = _jobGender;
            _builtHideCloak = _jobHideCloak;
            _haveBuilt = true;
            _shapeDirty = true;   // lead 2026-11-04: a new shell mesh takes the wearer's synced shape again
            ArmourShellLook look = j.Look;
            _builtRobe = look != null && look.Group[13] >= 2;   // lead 2026-11-03
            _builtGloves = look != null && look.Group[4] > 1;
            _builtBoots = look != null && look.Group[5] > 1;
            for (int ci = 0; ci < _builtCover.Length; ci++) _builtCover[ci] = look != null ? look.Cover[ci] : 0f;   // lead 2026-11-04
            if (look == null) { Say("Armour: full shell for " + _label + " not built (composition threw: " + j.Error + ")"); return; }
            if (!look.Any) { Say("Armour: full shell for " + _label + ": no armour that paints the body (" + look.Describe() + ")"); return; }
            var sw = Stopwatch.StartNew();
            string name = "Immersive armour shell of " + _label;
            Texture2D tex = null;
            GameObject go = null;
            string why = null;
            try
            {
                M2Model pm; M2Skin ps; string err;
                int bodyFlags = 0;
                if (M2Factory.TryGetParsed(files, look.ModelPath, out pm, out ps, out err))
                    foreach (var t in pm.Textures) if (t.Type == 1) { bodyFlags = (int)t.Flags; break; }
                tex = WowTextures.FromRgba32(ArmourShellLook.Size, ArmourShellLook.Size, look.Rgba, WowTextureOptions.ForM2Flags(bodyFlags), name + " body", true);
                var o = new M2BuildOptions { ShowGeoset = look.ShowGeoset, ShareMaterials = false, Name = name };
                o.ReplaceableTextures[1] = tex;
                if (look.CapeTexture != null) o.ReplaceablePaths[2] = look.CapeTexture;
                go = M2Factory.Build(files, look.ModelPath, o);
                if (go == null) why = "the model " + look.ModelPath + " was not built";
                else
                {
                    var owned = go.GetComponent<M2OwnedAssets>();
                    if (owned != null) { owned.Owned.Add(tex); tex = null; }
                    why = ps != null ? Rig(go, ps) : "the model's skin was not parsed";
                }
            }
            catch (Exception e) { why = "threw " + e.Message; }
            if (why != null)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _go = null;
                Say("Armour: full shell for " + _label + " not built (" + why + ")");
                return;
            }
            if (tex != null) UnityEngine.Object.Destroy(tex);
            Transform c = Container(gameRoot);
            if (c != null) go.transform.SetParent(c, false);
            go.SetActive(false);
            _go = go;
            _root = go.transform;
            Say("Armour: full shell for " + _label + ": " + Short(look.ModelPath) + " " + look.Describe() + ", " + SegCount + " segments posed from the avatar, composed in " +
                j.Ms + " ms, built in " + sw.ElapsedMilliseconds + " ms");
        }

        private static string Short(string p)
        {
            int s = p.LastIndexOfAny(new[] { '\\', '/' });
            p = s >= 0 ? p.Substring(s + 1) : p;
            return p.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 3) : p;
        }

        /// <summary>The armour material (alpha test), the segments and the WoW bones re-hung under them. Null = ok, else why not.</summary>
        private string Rig(GameObject go, M2Skin skin)
        {
            var anim = go.GetComponent<M2Animator>();
            if (anim == null || anim.Model == null) return "no M2Animator";
            M2Model m = anim.Model;
            M2HumanoidMap map = M2Humanoid.Find(m);
            if (!map.Valid || !map.Legs || map.LClavicle < 0 || map.RClavicle < 0) return "the skeleton is not a humanoid (" + map.Describe() + ")";
            anim.enabled = false;   // we pose the bones
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) return "no skinned mesh";
            Material[] mats = smr.sharedMaterials;   // unshared (ShareMaterials false): ours to change
            Material[] noCape = (Material[])mats.Clone();
            bool cape = false;
            _capeMat = new bool[mats.Length];
            var owned = go.GetComponent<M2OwnedAssets>();
            for (int i = 0; i < mats.Length; i++)
            {
                Material mt = mats[i];
                if (mt == null || !mt.HasProperty("_MainTex") || !mt.HasProperty("_Cutoff")) continue;
                Texture body = mt.GetTexture("_MainTex");
                if (body == null || !body.name.EndsWith(" body", StringComparison.Ordinal))
                {
                    // the only other batches the shell draws are the cape's (ArmShellRig: the race model's batches are the body (type 1),
                    // hair (6, not shown), cape (2) and the 1703 eye glow (not shown)) - a copy that alpha-tests everything away for our own eye
                    var off = new Material(mt) { name = mt.name + " (off for our own eye)" };
                    off.SetFloat("_Cutoff", 2f);
                    if (owned != null) owned.Owned.Add(off);
                    noCape[i] = off;
                    cape = true;
                    _capeMat[i] = true;
                    continue;
                }
                mt.SetFloat("_Cutoff", AlphaCutoff);
                mt.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                mt.SetOverrideTag("RenderType", "TransparentCutout");
            }
            _smr = smr;
            _matsAll = mats;
            _matsNoCape = cape ? noCape : null;
            _capeOffNow = false;
            smr.localBounds = new Bounds(new Vector3(0f, 0.2f, 0f), new Vector3(3.4f, 4f, 3.4f));   // ours: the root stands at the avatar's hips (lead fix 2026-10-04)
            smr.updateWhenOffscreen = false;

            Transform[] bones = anim.Bones;
            Vector3 legMid = 0.5f * (P(m, map.LHip) + P(m, map.RHip));
            Vector3 chest = P(m, map.Chest), neck = P(m, map.Neck), head = P(m, map.Head);
            _bChestFrac = (chest - legMid).magnitude / Mathf.Max(1e-4f, (chest - legMid).magnitude + (neck - chest).magnitude);
            _bHipSpan = Mathf.Max(0.05f, (P(m, map.RHip) - P(m, map.LHip)).magnitude);
            _bShoulderSpan = Mathf.Max(0.05f, (P(m, map.RShoulder) - P(m, map.LShoulder)).magnitude);
            Bind(SHips, legMid, chest, Vector3.forward);
            Bind(SChest, chest, neck, Vector3.forward);
            Bind(SNeck, neck, head, Vector3.forward);
            Bind(SLClav, P(m, map.LClavicle), P(m, map.LShoulder), Vector3.forward);
            Bind(SRClav, P(m, map.RClavicle), P(m, map.RShoulder), Vector3.forward);
            Bind(SLUpper, P(m, map.LShoulder), P(m, map.LElbow), Vector3.forward);
            Bind(SRUpper, P(m, map.RShoulder), P(m, map.RElbow), Vector3.forward);
            Bind(SLFore, P(m, map.LElbow), P(m, map.LHand), Vector3.forward);
            Bind(SRFore, P(m, map.RElbow), P(m, map.RHand), Vector3.forward);
            Bind(SLHand, P(m, map.LHand), Knuckles(m, map.LHand, 13), Vector3.forward);
            Bind(SRHand, P(m, map.RHand), Knuckles(m, map.RHand, 8), Vector3.forward);
            BindFingers(m, map.LHand, 13, 0);
            BindFingers(m, map.RHand, 8, 1);
            Bind(SLThigh, P(m, map.LHip), P(m, map.LKnee), Vector3.forward);
            Bind(SRThigh, P(m, map.RHip), P(m, map.RKnee), Vector3.forward);
            Bind(SLShin, P(m, map.LKnee), P(m, map.LAnkle), Vector3.forward);
            Bind(SRShin, P(m, map.RKnee), P(m, map.RAnkle), Vector3.forward);
            // feet: the axis ankle -> toe, the reference toward the knee (WoW's foot has a toe joint; hooves: ankle + the bind's forward)
            Vector3 lAnk = P(m, map.LAnkle), rAnk = P(m, map.RAnkle);
            Bind(SLFoot, lAnk, map.LToe >= 0 ? P(m, map.LToe) : lAnk + new Vector3(0f, -0.08f, 0.2f), P(m, map.LKnee) - lAnk);
            Bind(SRFoot, rAnk, map.RToe >= 0 ? P(m, map.RToe) : rAnk + new Vector3(0f, -0.08f, 0.2f), P(m, map.RKnee) - rAnk);
            Quaternion chestInv = Quaternion.Inverse(_bRot[SChest]);
            _bClav[0] = chestInv * (P(m, map.LClavicle) - _bStart[SChest]);
            _bClav[1] = chestInv * (P(m, map.RClavicle) - _bStart[SChest]);

            for (int s = 0; s < SegCount; s++)
            {
                var t = new GameObject("segment " + SegNames[s]).transform;
                t.SetParent(go.transform, false);
                _seg[s] = t;
            }
            // each bone -> the segment of its nearest mapped ancestor (itself when mapped); unmapped roots ride with the hips
            int n = Mathf.Min(bones.Length, m.Bones.Length);
            var segOfBone = new int[m.Bones.Length];
            _wPivot = new Vector3[m.Bones.Length];
            _wBones = bones;
            _wSeg = segOfBone;
            for (int b = 0; b < n; b++)
            {
                if (bones[b] == null) continue;
                int s = SHips;
                for (int a = b; a >= 0; a = m.Bones[a].Parent)
                {
                    int found = SegOf(map, a);
                    if (found >= 0) { s = found; break; }
                }
                Quaternion inv = Quaternion.Inverse(_bRot[s]);
                bones[b].SetParent(_seg[s], false);
                bones[b].localPosition = inv * (P(m, b) - _bStart[s]);
                bones[b].localRotation = inv;
                bones[b].localScale = Vector3.one;
                segOfBone[b] = s;
                _wPivot[b] = P(m, b);
            }
            MeasureWow(m, skin, segOfBone);
            // the torso remap's inputs: the shell's own mesh (M2Factory builds a skinned mesh per instance, readable, model space =
            // bind space: bindposes are Translate(-pivot)) and each vertex's strongest bone's segment
            _mesh = smr.sharedMesh;
            if (_mesh != null && _mesh.isReadable)
            {
                _v0 = _mesh.vertices;
                BoneWeight[] bw = _mesh.boneWeights;
                _vSeg = new int[_v0.Length];
                for (int i = 0; i < _vSeg.Length; i++)
                {
                    int bi = -1;
                    if (i < bw.Length)
                    {
                        BoneWeight w = bw[i];
                        bi = w.boneIndex0; float best = w.weight0;
                        if (w.weight1 > best) { best = w.weight1; bi = w.boneIndex1; }
                        if (w.weight2 > best) { best = w.weight2; bi = w.boneIndex2; }
                        if (w.weight3 > best) { bi = w.boneIndex3; }
                    }
                    _vSeg[i] = bi >= 0 && bi < segOfBone.Length ? segOfBone[bi] : -1;
                }
                // lead 2026-10-04: the cape's vertices (its submeshes = its materials) and its top middle, for the cloak's own fit
                var capeSet = new HashSet<int>();
                for (int sm = 0; sm < _mesh.subMeshCount && sm < _capeMat.Length; sm++)
                    if (_capeMat[sm]) foreach (int ix in _mesh.GetTriangles(sm)) capeSet.Add(ix);
                _capeV = new int[capeSet.Count];
                capeSet.CopyTo(_capeV);
                _isCape = new bool[_v0.Length];
                foreach (int ix in _capeV) _isCape[ix] = true;
                if (_capeV.Length > 0)
                {
                    float top = float.MinValue; Vector3 sum = Vector3.zero;
                    foreach (int ix in _capeV) { sum += _v0[ix]; top = Mathf.Max(top, _v0[ix].y); }
                    _capeTop = new Vector3(sum.x / _capeV.Length, top, sum.z / _capeV.Length);
                }
                _vBase = _v0;
                _vWork = new Vector3[_v0.Length];
                _capeKey = int.MinValue;
                // cape agent 2026-10-04: our rows on the cape (placed by FitCape's Rebind), the hips / chest bind frames, WoW's hip joints' height
                _cape = ArmourCape.Create(_label, smr, _mesh, go.transform, _capeV, _bStart[SChest], _bRot[SChest], _bStart[SHips], _bRot[SHips],
                                          0.5f * (_bStart[SLThigh].y + _bStart[SRThigh].y));
            }
            else { _v0 = null; _vSeg = null; _capeV = null; _vBase = null; _vWork = null; _cape = null; }
            _calFor = null;   // re-read the avatar for this rig
            return null;
        }

        /// <summary>
        /// The glove's fingers (ours, geometric): the hand's five finger key bones (firstKey .. +4, the keys M2Humanoid.Find uses); the
        /// thumb = the one farthest from the others' mean (HumanMale L: b41, 0.08 m off), the other four by bind forward (WoW's bind
        /// has the index in front of the little finger: L b42 z -0.006 .. b44 z -0.111). Each finger: base bone -> the end of its
        /// chain (first child down); reference = across the knuckles (index - little), the thumb's = toward the wrist.
        /// </summary>
        private void BindFingers(M2Model m, int hand, int firstKey, int side)
        {
            var fb = new List<int>();
            for (int k = firstKey; k < firstKey + 5; k++)
            {
                int b = k < m.KeyBoneLookup.Length ? m.KeyBoneLookup[k] : -1;
                if (b >= 0 && b < m.Bones.Length && !fb.Contains(b)) fb.Add(b);
            }
            for (int i = 0; i < 5; i++) _fingBase[side * 5 + i] = -1;
            if (fb.Count < 5) return;   // the fingers ride on the hand
            Vector3 mean = Vector3.zero;
            foreach (int b in fb) mean += P(m, b);
            mean /= fb.Count;
            int thumb = fb[0];
            foreach (int b in fb) if ((P(m, b) - mean).sqrMagnitude > (P(m, thumb) - mean).sqrMagnitude) thumb = b;
            fb.Remove(thumb);
            fb.Sort((x, y) => P(m, y).z.CompareTo(P(m, x).z));   // index (front) first
            Vector3 across = P(m, fb[0]) - P(m, fb[3]);
            Vector3 toWrist = P(m, hand) - mean;
            for (int i = 0; i < 5; i++)
            {
                int b = i == 0 ? thumb : fb[i - 1];
                int end = b;
                for (int step = 0; step < 6; step++)
                {
                    int child = -1;
                    for (int c = 0; c < m.Bones.Length; c++) if (m.Bones[c].Parent == end) { child = c; break; }
                    if (child < 0) break;
                    end = child;
                }
                Vector3 e = end != b ? P(m, end) : P(m, b) - toWrist.normalized * 0.05f;   // ours: a finger with no joint below
                _fingBase[side * 5 + i] = b;
                Bind(SFingers + side * 5 + i, P(m, b), e, i == 0 ? toWrist : across);
            }
        }

        /// <summary>The glove's finger bones under their finger segment when the avatar has that finger, else under the hand (once per avatar).</summary>
        private void HangFingers()
        {
            if (_wBones == null || _wSeg == null) return;
            for (int b = 0; b < _wBones.Length && b < _wSeg.Length; b++)
            {
                int s = _wSeg[b];
                if (s < SFingers || _wBones[b] == null) continue;
                int f = s - SFingers;
                if (!_fingOk[f]) s = f < 5 ? SLHand : SRHand;
                Quaternion inv = Quaternion.Inverse(_bRot[s]);
                _wBones[b].SetParent(_seg[s], false);
                _wBones[b].localPosition = inv * (_wPivot[b] - _bStart[s]);
                _wBones[b].localRotation = inv;
            }
        }

        private static Vector3 P(M2Model m, int bone)
        {
            M2Vec3 v = m.Bones[bone].Pivot;
            return WowSpace.PointToUnity(v.X, v.Y, v.Z);
        }

        /// <summary>The knuckle line's middle: the mean of the hand's finger key bones (firstKey .. +4) without the one farthest
        /// from the others' mean (the thumb - ours, geometric: HumanMale's L fingers b40-b44, the thumb b41 sits 0.08 m off).</summary>
        private static Vector3 Knuckles(M2Model m, int hand, int firstKey)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int k = firstKey; k < firstKey + 5; k++)
            {
                int b = k < m.KeyBoneLookup.Length ? m.KeyBoneLookup[k] : -1;
                if (b < 0 || b >= m.Bones.Length) continue;
                sum += P(m, b);
                count++;
            }
            Vector3 wrist = P(m, hand);
            if (count == 0) return wrist + new Vector3(0f, -0.1f, 0f);
            Vector3 mean = sum / count;
            if (count < 3) return mean;
            float worst = -1f;
            Vector3 far = mean;
            for (int k = firstKey; k < firstKey + 5; k++)
            {
                int b = k < m.KeyBoneLookup.Length ? m.KeyBoneLookup[k] : -1;
                if (b < 0 || b >= m.Bones.Length) continue;
                float d = (P(m, b) - mean).sqrMagnitude;
                if (d > worst) { worst = d; far = P(m, b); }
            }
            return (sum - far) / (count - 1);
        }

        private void Bind(int s, Vector3 start, Vector3 end, Vector3 reference)
        {
            _bStart[s] = start;
            _bLen[s] = Mathf.Max(1e-3f, (end - start).magnitude);
            _bRot[s] = Frame(end - start, reference, Vector3.up);
        }

        private int SegOf(M2HumanoidMap map, int b)
        {
            for (int f = 0; f < 10; f++) if (_fingBase[f] == b) return SFingers + f;
            if (b == map.Pelvis || b == map.Waist) return SHips;
            if (b == map.Chest) return SChest;
            if (b == map.Neck || b == map.Head) return SNeck;
            if (b == map.LClavicle) return SLClav;
            if (b == map.RClavicle) return SRClav;
            if (b == map.LShoulder) return SLUpper;
            if (b == map.RShoulder) return SRUpper;
            if (b == map.LElbow) return SLFore;
            if (b == map.RElbow) return SRFore;
            if (b == map.LHand) return SLHand;
            if (b == map.RHand) return SRHand;
            if (b == map.LHip) return SLThigh;
            if (b == map.RHip) return SRThigh;
            if (b == map.LKnee) return SLShin;
            if (b == map.RKnee) return SRShin;
            if (b == map.LAnkle || b == map.LToe) return SLFoot;
            if (b == map.RAnkle || b == map.RToe) return SRFoot;
            return -1;
        }

        /// <summary>The segment frame: up = the axis, forward = the reference made perpendicular (fallback when parallel).</summary>
        private static Quaternion Frame(Vector3 axis, Vector3 reference, Vector3 fallback)
        {
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.up;
            axis.Normalize();
            Vector3 f = reference - Vector3.Dot(reference, axis) * axis;
            if (f.sqrMagnitude < 1e-6f * Mathf.Max(1e-6f, reference.sqrMagnitude)) f = fallback - Vector3.Dot(fallback, axis) * axis;
            if (f.sqrMagnitude < 1e-10f) f = Vector3.Cross(axis, Vector3.right);
            return Quaternion.LookRotation(f.normalized, axis);
        }

        // ------------------------------------------------------------------------------------------------ the avatar

        private void Calibrate(Animator a)
        {
            _calFor = a;
            _calOk = false;
            if (a == null || !a.isHuman) return;
            _hips = a.GetBoneTransform(HumanBodyBones.Hips);
            _spine = a.GetBoneTransform(HumanBodyBones.Spine);
            _chest = a.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = a.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = a.GetBoneTransform(HumanBodyBones.Neck);
            _head = a.GetBoneTransform(HumanBodyBones.Head);
            for (int side = 0; side < 2; side++)
            {
                bool l = side == 0;
                _ua[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                _la[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                _hand[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                _ul[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                _ll[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                _foot[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                _toes[side] = a.GetBoneTransform(l ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                _fing[side * 4] = a.GetBoneTransform(l ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                _fing[side * 4 + 1] = a.GetBoneTransform(l ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                _fing[side * 4 + 2] = a.GetBoneTransform(l ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal);
                _fing[side * 4 + 3] = a.GetBoneTransform(l ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            }
            for (int s = 0; s < SegCount; s++) _tOffOk[s] = false;
            HumanBodyBones[] fingerBones =
            {
                HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
                HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
            };
            for (int i = 0; i < _af.Length; i++) _af[i] = a.GetBoneTransform(fingerBones[i]);
            for (int f = 0; f < 10; f++) _fingOk[f] = _fingBase[f] >= 0 && _af[f * 3] != null && (_af[f * 3 + 1] != null || _af[f * 3 + 2] != null);
            HangFingers();
            _measured = false;
            _calOk = _hips != null && _head != null && _ua[0] != null && _ua[1] != null && _la[0] != null && _la[1] != null && _hand[0] != null && _hand[1] != null
                     && _ul[0] != null && _ul[1] != null && _ll[0] != null && _ll[1] != null && _foot[0] != null && _foot[1] != null;
            if (!_calOk) { Say("Armour: full shell for " + _label + " not shown (the avatar lacks arm / leg / head bones)"); return; }
            // the limbs' twist: the avatar's T-pose (Avatar.humanDescription.skeleton, as the helm's TPoseTurn) gives each limb bone's
            // frame (axis to the child joint, forward = the avatar's +Z: thumbs / knees forward, as WoW's bind) in the bone's own space
            try
            {
                Avatar av = a.avatar;
                SkeletonBone[] sk = av != null && av.isHuman ? av.humanDescription.skeleton : null;
                if (sk != null && sk.Length > 0)
                {
                    int ok = 0;
                    for (int side = 0; side < 2; side++)
                    {
                        ok += TOffset(a, sk, side == 0 ? SLUpper : SRUpper, _ua[side], _la[side]) ? 1 : 0;
                        ok += TOffset(a, sk, side == 0 ? SLFore : SRFore, _la[side], _hand[side]) ? 1 : 0;
                        ok += TOffset(a, sk, side == 0 ? SLThigh : SRThigh, _ul[side], _ll[side]) ? 1 : 0;
                        ok += TOffset(a, sk, side == 0 ? SLShin : SRShin, _ll[side], _foot[side]) ? 1 : 0;
                    }
                    Say("Armour: full shell for " + _label + " follows avatar '" + a.name + "' (limb twist from its T-pose: " + ok + " of 8 limbs)");
                }
                else Say("Armour: full shell for " + _label + " follows avatar '" + a.name + "' (no T-pose skeleton: limb twist from the body frame)");
            }
            catch (Exception e) { Say("Armour: full shell for " + _label + ": T-pose not readable (" + e.Message + ") - limb twist from the body frame"); }
        }

        private bool TOffset(Animator a, SkeletonBone[] sk, int s, Transform bone, Transform child)
        {
            Vector3 bp, cp;
            Quaternion br, cr;
            if (bone == null || child == null || !TPose(a.transform, sk, bone, out bp, out br) || !TPose(a.transform, sk, child, out cp, out cr)) return false;
            _tOff[s] = Quaternion.Inverse(br) * Frame(cp - bp, Vector3.forward, Vector3.up);
            _tOffOk[s] = true;
            return true;
        }

        /// <summary>A bone's T-pose position / rotation relative to the Animator (the skeleton's local values chained up to it).</summary>
        private static bool TPose(Transform root, SkeletonBone[] sk, Transform t, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (t == null) return false;
            if (t == root) return true;
            Vector3 pp; Quaternion pr;
            if (!TPose(root, sk, t.parent, out pp, out pr)) return false;
            int i = -1;
            for (int k = 0; k < sk.Length; k++) if (sk[k].name == t.name) { i = k; break; }
            if (i < 0) return false;
            pos = pp + pr * sk[i].position;
            rot = pr * sk[i].rotation;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ every LateUpdate

        /// <summary>
        /// After the avatar is posed (and after the poser): every segment on the avatar's joints. feet = the avatar's ground point
        /// (GearBody.ModelFeet), draw = our-frame offset (remote), fit = the wearer's Armour fit, firstPerson = our own eye is in the head.
        /// </summary>
        public void Place(Animator avatar, Vector3 feet, Vector3 forward, Vector3 draw, float fit, bool firstPerson)
        {
            if (_go == null) return;
            if (avatar == null) { Hide(); return; }
            // lead 2026-10-04 (the user: "if I leave and come back everything is correct on both ends, so we just need to sync it up"): the
            // calibration was kept per Animator object - an avatar that finishes loading under the same Animator (a new humanoid avatar,
            // new bones) kept the loading one's skeleton and measures until a rejoin. Now also a new Animator.avatar or a destroyed bone
            // calibrates again, and so does a change of its meshes (below).
            bool stale = avatar != _calFor || avatar.avatar != _calAvatar || (_calOk && (_hips == null || _head == null || _ua[0] == null || _ul[0] == null));
            if (stale)
            {
                if (_calFor != null && _recalibrations++ < 10) Say("Armour: full shell for " + _label + ": the avatar changed (" + (avatar != _calFor ? "a new Animator" : avatar.avatar != _calAvatar ? "a new humanoid avatar" : "its bones were replaced") + ") - calibrated and measured again");
                Calibrate(avatar);
                _calAvatar = avatar.avatar;
                if (_calOk) Adopt(avatar);   // lead fix 2026-10-04: a stand-in, an avatar measured before, or one to measure
            }
            if (!_calOk || !Frames()) { Hide(); return; }
            // lead 2026-11-04: a remote wearer's synced measurement replaces measuring their copy here (no waits, no stand-in rule)
            if (WearerShapeSaid)
            {
                bool changed = _shapeDirty;
                for (int i = 0; i < ShapeInts && !changed; i++) changed = _shapeIn[i] != _shapeApplied[i];
                if (changed) { ApplyWearerShape(); _shapeDirty = false; }
                _measured = true;
                _needMeasure = false;
            }
            if (_needMeasure && Time.unscaledTime >= _retryAt)
            {
                // lead 2026-10-04 (the user: "Frustum culling might be doing it too"): an avatar no camera draws may not be animated (its
                // Animator culled) - its bones lag its meshes, and a measurement then is wrong. Measure only once one of its renderers is
                // drawn (Renderer.isVisible: seen by any camera last frame) and it stands upright. Until then: hidden - or, lead fix
                // 2026-10-04, drawn with the last good measurement when there is one.
                bool visible = AnyVisible(avatar), upright = false;
                string pose = "";
                if (visible)
                {
                    if (_uprightSince < 0f) _uprightSince = Time.unscaledTime;
                    upright = Upright(out pose) || (AllowUprightTimeout && Time.unscaledTime - _uprightSince >= UprightWaitSeconds);
                }
                if (visible && upright)
                {
                    _uprightSince = -1f;
                    _needMeasure = false;
                    _meshSig = MeshSig(avatar); _meshCheckAt = Time.unscaledTime + MeshCheckSeconds;
                    if (_bodyHide != null) _bodyHide.Suspend();   // measure the avatar's real shape, not the hidden parts
                    string bad;
                    Measured last = LastGood();
                    bool good = Measure(avatar, out bad);
                    if (_bodyHide != null) _bodyHide.Resume();
                    if (good) { Remember(avatar); _retries = 0; }
                    else if (last != null) { UseMeasured(last); Say("Armour: full shell for " + _label + ": that measurement is not usable (" + bad + ") - the last good one (of '" + last.Name + "') kept"); }
                    else if (_retries < MaxRetries)
                    {
                        // lead fix 2026-10-04 (FBT): drawn with the repaired values, measured again soon
                        _retries++;
                        _needMeasure = true;
                        _retryAt = Time.unscaledTime + RetrySeconds;
                        Say("Armour: full shell for " + _label + ": that measurement is doubtful (" + bad + ") - drawn repaired, measured again in " + RetrySeconds.ToString("0") + " s (try " + _retries + " of " + MaxRetries + ")");
                    }
                    _measured = true;
                }
                else if (!_measured)
                {
                    if (!visible) { if (!_waitSaid) { _waitSaid = true; Say("Armour: full shell for " + _label + ": waiting for the avatar to be drawn (on screen) before measuring it"); } }
                    else if (!_uprightSaid) { _uprightSaid = true; Say("Armour: full shell for " + _label + ": waiting for the avatar to stand upright before measuring it (" + pose + ")"); }
                    Hide();
                    return;
                }
            }
            else if (!_measured) { Hide(); return; }   // lead fix 2026-10-04: the client's stand-in before the avatar itself was ever measured
            else if (!_standIn && !WearerShapeSaid && Time.unscaledTime >= _meshCheckAt)
            {
                _meshCheckAt = Time.unscaledTime + MeshCheckSeconds;
                int sig = MeshSig(avatar);
                if (sig != _meshSig)
                {
                    _meshSig = sig;
                    _remeasures++;
                    if (_remeasures <= 10) Say("Armour: full shell for " + _label + ": the avatar's meshes changed (e.g. the client's stand-in replaced by the full avatar) - calibrated again, measured again unless measured before (" + _remeasures + ")");
                    _calFor = null;   // next frame: Calibrate (its bones may be new too), Frames, then Adopt (drawn with the last good measurement meanwhile)
                    return;
                }
            }
            if (!_go.activeSelf) _go.SetActive(true);
            float k = Mathf.Clamp(fit > 0f ? fit : 1f, 0.5f, 2f) * ThicknessBase;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = avatar.transform.forward;
            forward.y = 0f;
            // Lead fix 2026-10-04 (the user: "it is immersion breaking when the frustrom culling removez and places it back onvthem"): the
            // root - only the culling box of the shell's SkinnedMeshRenderer (rootBone) hangs on it, every segment is placed in world space -
            // stood on `feet` = GearBody.ModelFeet, the followed Animator OBJECT's position; for another player that is the client's own
            // Animator on NonXRMainRoot, not at the avatar (Player.log: "remote body root and avatar model disagree ... avatar model (0, 0,
            // 0)"), so Unity culled the whole shell whenever that spot was off screen. Now it stands on the avatar's own hips joint.
            _root.SetPositionAndRotation(_aPos[SHips] + draw, Quaternion.LookRotation(forward.sqrMagnitude > 1e-6f ? forward : Vector3.forward, Vector3.up));
            for (int s = 0; s < SegCount; s++)
            {
                if (s == SNeck || s == SLClav || s == SRClav || (s >= SFingers && !_fingOk[s - SFingers])) continue;
                float ly = _aLen[s] / _bLen[s];
                if ((s == SLFoot || s == SRFoot || ((s == SLHand || s == SRHand) && !_fingOk[s == SLHand ? 1 : 6])) && _thick[s].y > ly) ly = _thick[s].y;   // shoe / fingers on the hand: to their tips
                bool remapped = s <= SChest && _v0 != null;   // the torso's depth is in its vertices (RemapTorso)
                PutFit(s, _aPos[s], _aRot[s], Thick(s, 0) * k, ly, (remapped ? 1f : Thick(s, 1)) * k, draw);
            }
            // our own eye: the cape (its collar wraps the neck) off; the neck (and the head's bones) collapsed. Lead 2026-10-04: the
            // cloak's own Show off too, and its offset / size on its vertices.
            // Lead fix 2026-10-04 (the user: "also cloak is not visible in VR"): first person no longer takes the cape off - only its collar
            // (above WoW's shoulder joints, where it wraps the front of the neck) is folded flat behind the neck (FitCape), so the cloak is
            // seen looking down or back, and nothing of it is at the eyes.
            bool capeOff = !Adjust[ArmourAdjust.Cloak].Show;
            if (_smr != null && _matsNoCape != null && capeOff != _capeOffNow) { _capeOffNow = capeOff; _smr.sharedMaterials = capeOff ? _matsNoCape : _matsAll; }
            _collarOff = firstPerson;
            FitCape();
            // lead 2026-10-04: "Hide avatar part" - the avatar's body under the shown pieces with the wearer's flag
            int hideMask = 0;
            for (int slot = ArmourAdjust.Chest; slot <= ArmourAdjust.Feet; slot++)
                if (Adjust[slot].Flag && Adjust[slot].Show && Worn(slot)) hideMask |= 1 << slot;   // lead 2026-11-03: never with nothing worn there
            if (hideMask != 0 && _bodyHide == null) _bodyHide = new ArmourBodyHide(_label);
            if (_bodyHide != null) _bodyHide.Apply(avatar, hideMask, _meshSig * 31 + avatar.GetInstanceID(), Time.unscaledTime);
            if (_cape != null) StepCape(capeOff, k, draw);   // cape agent 2026-10-04
            if (firstPerson) Put(SNeck, _aPos[SNeck], _aRot[SNeck], 1e-4f, 1e-4f, 1e-4f, draw);
            else PutFit(SNeck, _aPos[SNeck], _aRot[SNeck], Thick(SNeck, 0) * k, _aLen[SNeck] / _bLen[SNeck], Thick(SNeck, 1) * k, draw);
            // clavicles: from where the chest (torso remap included) puts WoW's clavicle joint to the avatar's upper arm (no seam at the chest)
            PieceAdjust aChest = Adjust[ArmourAdjust.Chest];
            float tClav = Mathf.Max(Thick(SChest, 0), Thick(SChest, 1)) * k * Mathf.Clamp(aChest.Size, 0.5f, 2f);
            Vector3 chestFwd = _aRot[SChest] * Vector3.forward, chestUp = _aRot[SChest] * Vector3.up;
            Transform chestSeg = _seg[SChest];
            for (int side = 0; side < 2; side++)
            {
                Vector3 start = chestSeg.TransformPoint(_v0 != null ? _bClavR[side] : _bClav[side]) - draw;
                Vector3 end = _ua[side].position;
                int s = side == 0 ? SLClav : SRClav;
                if (!aChest.Show) Put(s, start, Frame(end - start, chestFwd, chestUp), 1e-4f, 1e-4f, 1e-4f, draw);   // lead 2026-10-04: the chest hidden
                else Put(s, start, Frame(end - start, chestFwd, chestUp), tClav, (end - start).magnitude / _bLen[s], tClav, draw);
            }
        }

        /// <summary>The avatar's segment frames now (all but the clavicles): origin, rotation (up = the axis), length. False = no pose.</summary>
        private bool Frames()
        {
            Vector3 lUl = _ul[0].position, rUl = _ul[1].position, legMid = 0.5f * (lUl + rUl);
            Vector3 lUa = _ua[0].position, rUa = _ua[1].position;
            Vector3 head = _head.position, neck = _neck != null ? _neck.position : Vector3.Lerp(_hips.position, head, 0.85f);
            _kHip = (rUl - lUl).magnitude / _bHipSpan;
            _kSpan = (rUa - lUa).magnitude / _bShoulderSpan;
            if (_kHip < 1e-3f || _kSpan < 1e-3f) return false;
            // the spine: legMid -> spine -> chest -> upper chest -> neck; WoW's chest pivot at the same share of its length
            int np = 0;
            _spinePts[np++] = legMid;
            if (_spine != null) _spinePts[np++] = _spine.position;
            if (_chest != null) _spinePts[np++] = _chest.position;
            if (_upperChest != null) _spinePts[np++] = _upperChest.position;
            _spinePts[np++] = neck;
            Vector3 chestPt = Along(_spinePts, np, _bChestFrac);
            // hips: up along the spine's first part, right = the hip joints; chest: chest point -> neck, right = the shoulder joints
            Set(SHips, legMid, chestPt, FrameRight(chestPt - legMid, rUl - lUl));
            Set(SChest, chestPt, neck, FrameRight(neck - chestPt, rUa - lUa));
            Quaternion aChest = _aRot[SChest];
            Vector3 chestFwd = aChest * Vector3.forward, hipsFwd = _aRot[SHips] * Vector3.forward;
            Set(SNeck, neck, head, FrameRight(head - neck, aChest * Vector3.right));
            for (int side = 0; side < 2; side++)
            {
                bool l = side == 0;
                Vector3 ua = _ua[side].position, la = _la[side].position, hand = _hand[side].position;
                Limb(l ? SLUpper : SRUpper, _ua[side], ua, la, chestFwd);
                Limb(l ? SLFore : SRFore, _la[side], la, hand, chestFwd);
                // hand: wrist -> knuckles, forward = the index side (WoW's bind: index forward of the little finger)
                Vector3 knuckles, across;
                Knuckles(side, hand, la, out knuckles, out across);
                Set(l ? SLHand : SRHand, hand, knuckles, Frame(knuckles - hand, across.sqrMagnitude > 1e-8f ? across : chestFwd, chestFwd));
                for (int i = 0; i < 5; i++)
                {
                    int f = side * 5 + i;
                    if (!_fingOk[f]) continue;
                    Transform fe = _af[f * 3 + 2] != null ? _af[f * 3 + 2] : _af[f * 3 + 1];
                    Transform fp = _af[f * 3 + 2] != null && _af[f * 3 + 1] != null ? _af[f * 3 + 1] : _af[f * 3];
                    Vector3 fs = _af[f * 3].position, fend = fe.position;
                    fend += (fend - fp.position) * FingerTip;   // the last joint is not the tip (lab: RPM fingertips showed past the glove)
                    Set(SFingers + f, fs, fend, Frame(fend - fs, i == 0 ? hand - knuckles : (across.sqrMagnitude > 1e-8f ? across : chestFwd), chestFwd));
                }
                Vector3 ul = _ul[side].position, ll = _ll[side].position, ft = _foot[side].position;
                Limb(l ? SLThigh : SRThigh, _ul[side], ul, ll, hipsFwd);
                Limb(l ? SLShin : SRShin, _ll[side], ll, ft, hipsFwd);
                // foot: ankle -> toes, forward = toward the knee (as the WoW bind)
                Vector3 toes;
                if (_toes[side] != null) toes = _toes[side].position;
                else
                {
                    Vector3 f = hipsFwd; f.y = 0f;
                    toes = ft + (f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward) * ((ll - ft).magnitude * 0.45f) - Vector3.up * ((ll - ft).magnitude * 0.15f);   // ours
                }
                Set(l ? SLFoot : SRFoot, ft, toes, Frame(toes - ft, ll - ft, Vector3.up));
            }
            return true;
        }

        private void Set(int s, Vector3 start, Vector3 end, Quaternion rot)
        {
            _aPos[s] = start;
            _aRot[s] = rot;
            _aLen[s] = (end - start).magnitude;
        }

        /// <summary>A limb segment: axis = the avatar's (start -> end), twist from the bone's T-pose frame when known, else the body's forward.</summary>
        private void Limb(int s, Transform bone, Vector3 start, Vector3 end, Vector3 bodyFwd)
        {
            Vector3 axis = end - start;
            Quaternion a;
            if (_tOffOk[s])
            {
                a = bone.rotation * _tOff[s];
                a = Quaternion.FromToRotation(a * Vector3.up, axis) * a;
            }
            else a = Frame(axis, bodyFwd, Vector3.up);
            Set(s, start, end, a);
        }

        /// <summary>The thickness of a segment across its axis (0 = its x, 1 = its z): the measured avatar / WoW ratio x the margin,
        /// else (an avatar mesh we cannot read) the joint-span ratio x ThicknessFallback.</summary>
        private float Thick(int s, int axis)
        {
            if (s >= SFingers) s = s - SFingers < 5 ? SLHand : SRHand;   // fingers: their hand's thickness
            float m = axis == 0 ? _thick[s].x : _thick[s].z;
            if (m > 0f) return m * Margin;
            bool leg = s == SHips || s == SLThigh || s == SRThigh || s == SLShin || s == SRShin;
            if (s == SLHand || s == SRHand || s == SLFoot || s == SRFoot) return _aLen[s] / _bLen[s] * ThicknessFallback;
            return (leg ? _kHip : _kSpan) * ThicknessFallback;
        }

        /// <summary>
        /// Once per avatar body (ours): how thick the avatar is around each segment - its skinned meshes baked in this pose, each vertex
        /// to the segment of its strongest bone (torso vertices to hips or chest by height along the spine), the 95th percentile of
        /// |x| and |z| in the segment frame (and of y for the feet: the shoe tip) against the same of WoW's bare body (Rig). Meshes that
        /// cannot be read are skipped (then ThicknessFallback). Allocates, but only here.
        /// </summary>
        private bool Measure(Animator a, out string bad)
        {
            for (int s = 0; s < SegCount; s++) _thick[s] = Vector3.zero;
            var lists = new List<float>[SegCount * Bins * 5];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<float>();
            var boneSeg = new Dictionary<Transform, int>();
            Action<HumanBodyBones, int> map = (hb, s) => { Transform t = a.GetBoneTransform(hb); if (t != null && !boneSeg.ContainsKey(t)) boneSeg[t] = s; };
            map(HumanBodyBones.Hips, -2); map(HumanBodyBones.Spine, -2); map(HumanBodyBones.Chest, -2); map(HumanBodyBones.UpperChest, -2);
            map(HumanBodyBones.LeftShoulder, -2); map(HumanBodyBones.RightShoulder, -2); map(HumanBodyBones.Neck, -1); map(HumanBodyBones.Head, -1);
            map(HumanBodyBones.LeftUpperArm, SLUpper); map(HumanBodyBones.RightUpperArm, SRUpper); map(HumanBodyBones.LeftLowerArm, SLFore); map(HumanBodyBones.RightLowerArm, SRFore);
            map(HumanBodyBones.LeftHand, SLHand); map(HumanBodyBones.RightHand, SRHand); map(HumanBodyBones.LeftUpperLeg, SLThigh); map(HumanBodyBones.RightUpperLeg, SRThigh);
            map(HumanBodyBones.LeftLowerLeg, SLShin); map(HumanBodyBones.RightLowerLeg, SRShin); map(HumanBodyBones.LeftFoot, SLFoot); map(HumanBodyBones.RightFoot, SRFoot);
            map(HumanBodyBones.LeftToes, SLFoot); map(HumanBodyBones.RightToes, SRFoot);
            int meshes = 0, unread = 0, verts = 0, farMeshes = 0, farVerts = 0;
            var torsoZ = new List<float>[2 * Bins];   // the avatar's signed front-back z per torso bin (the centre fit)
            for (int i = 0; i < torsoZ.Length; i++) torsoZ[i] = new List<float>();
            Mesh baked = null;
            try
            {
                baked = new Mesh();
                foreach (SkinnedMeshRenderer smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    Mesh src = smr.sharedMesh;
                    if (src == null || smr.transform.IsChildOf(_root)) continue;
                    if (!src.isReadable) { unread++; continue; }
                    // lead fix 2026-11-04 (Player (35).log: a remote's torso "centre moved -3448 cm", chest 3.00/3.00): a skinned mesh under the
                    // Animator that is drawn somewhere else (another copy of the avatar, a mesh left at the floating origin's old place) is not
                    // the body - skipped when its bounds' centre is more than FarMeshMetres from the hips
                    if ((smr.bounds.center - _aPos[SHips]).magnitude > FarMeshMetres) { farMeshes++; continue; }
                    Transform[] bones = smr.bones;
                    var segOf = new int[bones.Length];
                    for (int b = 0; b < bones.Length; b++)
                    {
                        segOf[b] = -1;
                        for (Transform t = bones[b]; t != null && t != a.transform; t = t.parent)
                        {
                            int s;
                            if (boneSeg.TryGetValue(t, out s)) { segOf[b] = s; break; }
                        }
                    }
                    BoneWeight[] w = src.boneWeights;
                    smr.BakeMesh(baked);
                    Vector3[] v = baked.vertices;
                    if (w.Length != v.Length) continue;
                    meshes++;
                    Matrix4x4 m = smr.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length; i++)
                    {
                        int b = w[i].boneIndex0;
                        int s = b >= 0 && b < segOf.Length ? segOf[b] : -1;
                        if (s == -1) continue;
                        Vector3 p = m.MultiplyPoint3x4(v[i]);
                        if (s == -2) s = Vector3.Dot(p - _aPos[SHips], _aRot[SHips] * Vector3.up) <= _aLen[SHips] ? SHips : SChest;
                        Vector3 local = Quaternion.Inverse(_aRot[s]) * (p - _aPos[s]);
                        if (local.x * local.x + local.z * local.z > Sq(MaxRadial(s, _aLen[s]))) { farVerts++; continue; }   // lead fix 2026-11-04: not near its bone
                        if (!AddExtent(lists, s, local, _aLen[s])) continue;
                        verts++;
                        if (s <= SChest) torsoZ[s * Bins + Mathf.Clamp((int)(local.y / Mathf.Max(1e-4f, _aLen[s]) * Bins), 0, Bins - 1)].Add(local.z);
                    }
                }
            }
            catch (Exception e) { WowLog.Warn("Armour: full shell for " + _label + ": the avatar's meshes could not be measured (" + e.Message + ")"); }
            if (baked != null) UnityEngine.Object.Destroy(baked);
            int measured = 0, capped = 0, outliers = 0;
            var outlierSb = new System.Text.StringBuilder();
            var sb = new System.Text.StringBuilder();
            var cappedSb = new System.Text.StringBuilder();
            for (int s = 0; s < SegCount; s++)
            {
                if (s == SNeck || s == SLClav || s == SRClav || s >= SFingers) continue;
                // each axis: the larger of its two sides' ratios, so both sides cover (a belly, a back, a shoe's toe)
                var t = new Vector3(Mathf.Max(Ratio(lists, s, 0), Ratio(lists, s, 1)), Ratio(lists, s, 2), Mathf.Max(Ratio(lists, s, 3), Ratio(lists, s, 4)));
                if (t.x <= 0f || t.z <= 0f) continue;
                t.x = Mathf.Clamp(t.x, 0.3f, 3f);
                t.y = Mathf.Clamp(t.y, 0f, 3f);
                t.z = Mathf.Clamp(t.z, 0.3f, 3f);
                // lead 2026-10-04: a hand / foot at most ExtremityOverLimb x its limb (forearm / shin, measured before it in this loop)
                int limb = s == SLHand ? SLFore : s == SRHand ? SRFore : s == SLFoot ? SLShin : s == SRFoot ? SRShin : -1;
                if (limb >= 0)
                {
                    Vector3 lt = _thick[limb];
                    bool footSeg = s == SLFoot || s == SRFoot;   // lead fix 2026-10-04: the boots follow the shin (see FootOverShin)
                    float cap = Mathf.Min((lt.x > 0f || lt.z > 0f ? Mathf.Max(lt.x, lt.z) : 1f) * (footSeg ? FootOverShin : ExtremityOverLimb), footSeg ? FootAbsMax : ExtremityAbsMax);
                    float tipMax = footSeg ? FootTipMax : ExtremityTipMax;
                    if (t.x > cap || t.z > cap || t.y > tipMax) { capped++; cappedSb.Append(" " + SegNames[s] + " " + t.x.ToString("0.00") + "/" + t.z.ToString("0.00") + " -> <=" + cap.ToString("0.00")); }
                    t.x = Mathf.Min(t.x, cap); t.z = Mathf.Min(t.z, cap); t.y = Mathf.Min(t.y, tipMax);
                }
                bool limbSeg = s == SLUpper || s == SRUpper || s == SLFore || s == SRFore || s == SLThigh || s == SRThigh || s == SLShin || s == SRShin;
                if (limbSeg && (t.x >= LimbOutlier || t.z >= LimbOutlier))
                {
                    // lead fix 2026-10-04 (FBT): an outlier limb takes its parent's ratio, else WoW's own
                    int parent = s == SLFore ? SLUpper : s == SRFore ? SRUpper : s == SLShin ? SLThigh : s == SRShin ? SRThigh : -1;
                    Vector3 pt = parent >= 0 ? _thick[parent] : Vector3.zero;
                    bool sane = parent >= 0 && pt.x > 0f && pt.x < LimbOutlier && pt.z < LimbOutlier;
                    outliers++;
                    outlierSb.Append(" " + SegNames[s] + " " + t.x.ToString("0.00") + "/" + t.z.ToString("0.00") + " -> " + (sane ? SegNames[parent] + "'s" : "WoW's"));
                    t.x = sane ? pt.x : 1f; t.z = sane ? pt.z : 1f;
                }
                _thick[s] = t;
                measured++;
                if (s <= SLHand || s == SLThigh || s == SLShin || s == SLFoot) sb.Append(" " + SegNames[s] + " " + t.x.ToString("0.00") + "/" + t.z.ToString("0.00"));
            }
            if (capped > 0) Say("Armour: full shell for " + _label + ": " + capped + " hand / foot thickness capped (hands " + ExtremityOverLimb.ToString("0.0") + "x the forearm, feet " + FootOverShin.ToString("0.0") + "x the shin, toe " + FootTipMax.ToString("0.0") + "x WoW's; ours):" + cappedSb);
            string torso = ProfileTorso(torsoZ, measured > 0);
            for (int seg = 0; seg < 2; seg++)
            {
                float sum = 0f; int cnt = 0;
                for (int b = 0; b < Bins; b++) { int g = seg * Bins + b; if (_pOk[g]) { sum += _pCA[g]; cnt++; } }
                _centreZ[seg] = cnt > 0 ? sum / cnt : 0f;
            }
            RemapTorso();
            Say("Armour: full shell for " + _label + " measured the avatar: " + meshes + " meshes, " + verts + " vertices" + (unread > 0 ? ", " + unread + " not readable" : "") +
                (farMeshes > 0 || farVerts > 0 ? " (" + farMeshes + " mesh(es) away from the body and " + farVerts + " vertices too far from their bone left out)" : "") +
                ", " + measured + " of 14 segments (thickness avatar / WoW x / z:" + sb + "; joint spans " + _kSpan.ToString("0.00") + " / " + _kHip.ToString("0.00") + "); torso remap:" + torso);
            // lead fix 2026-10-04: good = a readable mesh and at least GoodSegments of the 14 segments
            bad = meshes == 0 ? (unread > 0 ? unread + " mesh(es), none readable" : "no meshes") : measured < GoodSegments ? "only " + measured + " of 14 segments"
                : outliers > 0 ? outliers + " limb segment(s) at the limit, replaced (" + outlierSb.ToString().Trim() + "; trackers mid-calibration?)" : null;
            return bad == null;
        }

        // ------------------------------------------------------------------------------------------------ lead fix 2026-10-04: measurements kept

        /// <summary>Right after Calibrate: the client's stand-in wears the last good measurement (never measured itself); an avatar
        /// measured before (same humanoid Avatar, same meshes, same shell gender) gets that measurement at once; any other avatar is
        /// measured when seen standing - drawn meanwhile with the last good one, if any.</summary>
        private void Adopt(Animator avatar)
        {
            _retries = 0; _retryAt = 0f;   // lead fix 2026-10-04
            _shapeDirty = true;            // lead 2026-11-04: a synced shape is applied again on the new calibration
            _standIn = GearAvatarRoots.IsStandIn(avatar);
            _meshSig = MeshSig(avatar);
            _meshCheckAt = Time.unscaledTime + MeshCheckSeconds;
            Measured last = LastGood();
            Measured known = _standIn ? null : Known(avatar.avatar, _meshSig);
            if (known != null)
            {
                UseMeasured(known);
                _measured = true;
                _needMeasure = false;
                _lastGood = known;
                Say("Armour: full shell for " + _label + ": avatar '" + avatar.name + "' measured before - that measurement used again (no wait)");
                return;
            }
            if (_standIn)
            {
                _needMeasure = false;
                _measured = last != null;
                if (_measured) UseMeasured(last);
                Say("Armour: full shell for " + _label + ": '" + avatar.name + "' is the client's stand-in (the avatar culled past its near distance, or still loading) - not measured; " +
                    (_measured ? "drawn with the measurement of '" + last.Name + "'" : "hidden until the avatar itself is measured"));
                return;
            }
            _needMeasure = true;
            _measured = last != null;
            if (_measured) UseMeasured(last);
        }

        private Measured LastGood() { return _lastGood != null && _lastGood.Gender == _builtGender ? _lastGood : null; }

        private Measured Known(Avatar av, int sig)
        {
            for (int i = _known.Count - 1; i >= 0; i--)
            {
                Measured m = _known[i];
                if (m.Avatar == av && m.Sig == sig && m.Gender == _builtGender) return m;
            }
            return null;
        }

        private void Remember(Animator avatar)
        {
            Measured m = Known(avatar.avatar, _meshSig);
            if (m == null)
            {
                if (_known.Count >= MeasureCacheSize) _known.RemoveAt(0);
                m = new Measured();
                _known.Add(m);
            }
            m.Avatar = avatar.avatar;
            m.Sig = _meshSig;
            m.Gender = _builtGender;
            m.Name = avatar.name;
            Array.Copy(_thick, m.Thick, SegCount);
            Array.Copy(_pOk, m.POk, m.POk.Length);
            Array.Copy(_pCW, m.PCW, m.PCW.Length); Array.Copy(_pHW, m.PHW, m.PHW.Length);
            Array.Copy(_pCA, m.PCA, m.PCA.Length); Array.Copy(_pTZ, m.PTZ, m.PTZ.Length);
            Array.Copy(_centreZ, m.CentreZ, 2);
            _lastGood = m;
        }

        private void UseMeasured(Measured m)
        {
            Array.Copy(m.Thick, _thick, SegCount);
            Array.Copy(m.POk, _pOk, m.POk.Length);
            Array.Copy(m.PCW, _pCW, m.PCW.Length); Array.Copy(m.PHW, _pHW, m.PHW.Length);
            Array.Copy(m.PCA, _pCA, m.PCA.Length); Array.Copy(m.PTZ, _pTZ, m.PTZ.Length);
            Array.Copy(m.CentreZ, _centreZ, 2);
            RemapTorso();
        }

        /// <summary>
        /// The torso's front-back profile (ours, measured in the lab: HumanMale's chest pivot sits 11 cm in front of its back and 23 cm
        /// behind its front, its torso is deep at the chest and thin at the neck base, while an RPM avatar's spine bones run mid-body -
        /// so a shell scaled about the joints left the avatar's back poking out). Per height bin of the hips and the chest (both
        /// measured by height along the hips, as the avatar's): WoW's centre and half depth (signed z, TorsoBin); the avatar's centre
        /// (2nd .. 98th percentile) and wanted half depth = its own x Margin + TorsoClearance. A bin the avatar does not fill (or an avatar
        /// that cannot be measured): WoW's own centred on the spine x the segment's thickness. The lowest hips bin (the pelvis, where
        /// the tabard's flap hangs) at least as deep as the thighs scale (lab: a thinner pelvis hid the flap behind the leg armour).
        /// Ratios TorsoRatioMin .. Max. The width is not remapped: the avatar's sides near the arms belong to its arm bones.
        /// </summary>
        private string ProfileTorso(List<float>[] torsoZ, bool measured)
        {
            float rzLo = 9f, rzHi = 0f, cLo = 9f, cHi = -9f;
            float thighs = Mathf.Max(Thick(SLThigh, 1), Thick(SRThigh, 1));
            int own = 0;
            for (int s = SHips; s <= SChest; s++)
                for (int b = 0; b < Bins; b++)
                {
                    int g = s * Bins + b;
                    _pOk[g] = TorsoBin(s, b);
                    if (!_pOk[g]) continue;
                    float cw = 0.5f * (_wLoZ[g] + _wHiZ[g]), hw = Mathf.Max(0.01f, 0.5f * (_wHiZ[g] - _wLoZ[g]));
                    _pCW[g] = cw; _pHW[g] = hw;
                    List<float> z = torsoZ[g];
                    float tz, ca;
                    if (measured && z.Count >= 8)
                    {
                        z.Sort();
                        float lo = z[Mathf.Clamp((int)(z.Count * (1f - ExtentPercentile)), 0, z.Count - 1)], hi = z[Mathf.Clamp((int)(z.Count * ExtentPercentile), 0, z.Count - 1)];
                        ca = 0.5f * (lo + hi);
                        tz = 0.5f * (hi - lo) * Margin + TorsoClearance;
                        own++;
                    }
                    else { ca = 0f; tz = hw * Thick(s, 1); }
                    if (g == 0) tz = Mathf.Max(tz, hw * thighs);
                    _pCA[g] = ca;
                    _pTZ[g] = Mathf.Clamp(tz, hw * TorsoRatioMin, hw * TorsoRatioMax);
                    rzLo = Mathf.Min(rzLo, _pTZ[g] / hw); rzHi = Mathf.Max(rzHi, _pTZ[g] / hw);
                    cLo = Mathf.Min(cLo, ca - cw); cHi = Mathf.Max(cHi, ca - cw);
                }
            if (rzHi <= 0f) return " no WoW torso bins";
            return " " + own + " of " + (2 * Bins) + " bins from the avatar, depth x" + rzLo.ToString("0.00") + ".." + rzHi.ToString("0.00") +
                   ", centre moved " + (cLo * 100f).ToString("0") + ".." + (cHi * 100f).ToString("0") + " cm";
        }

        /// <summary>
        /// The shell's own torso vertices (strongest bone in the hips or chest segment: body, tabard, cape top, belt) onto the avatar's
        /// front-back cross-sections, in bind space, once per avatar (from the original vertices): each to the bin it is in by height
        /// (the profile interpolated between bin middles, the end bins held), z' = avatar centre + (z - WoW centre) x wanted / WoW half
        /// depth. Then the two segments scale their depth only by the Armour fit. Allocates (calibration only).
        /// </summary>
        private void RemapTorso()
        {
            if (_mesh == null || _v0 == null || _vSeg == null) return;
            var v = new Vector3[_v0.Length];
            Quaternion hipsRot = _bRot[SHips];
            // lead 2026-10-04 (the cape agent's finding: scaling the cape's depth by the avatar / WoW ratio, x1.04-1.71 RPM man, x1.38-2.50
            // woman, doubled its flare - WoW z -0.45 -> -0.87 at y 0.20): the cape's vertices MOVE with the back surface instead
            bool[] cape = null;
            if (_capeV != null && _capeV.Length > 0) { cape = new bool[_v0.Length]; foreach (int ix in _capeV) cape[ix] = true; }
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = _v0[i];
                v[i] = p;
                int s = i < _vSeg.Length ? _vSeg[i] : -1;
                if (s != SHips && s != SChest) continue;
                int sh = Vector3.Dot(p - _bStart[SHips], hipsRot * Vector3.up) <= _bLen[SHips] ? SHips : SChest;
                Vector3 local = Quaternion.Inverse(_bRot[sh]) * (p - _bStart[sh]);
                float cw, hw, ca, tz;
                float uh = sh + local.y / _bLen[sh];
                if (!Profile(uh, out cw, out hw, out ca, out tz)) continue;
                float u0 = 0.5f / Bins;   // lead fix 2026-10-04: below the lowest hips bin's middle the remap fades to WoW's own (SkirtFade)
                if (sh == SHips && uh < u0 && !(cape != null && cape[i]))
                {
                    float wgt = Mathf.Clamp01(1f - (u0 - uh) * _bLen[SHips] / SkirtFade);
                    ca = Mathf.Lerp(cw, ca, wgt);
                    tz = Mathf.Lerp(hw, tz, wgt);
                }
                if (cape != null && cape[i]) local.z += (ca - tz) - (cw - hw);   // the back surface's move (WoW back cw - hw -> ours ca - tz)
                else local.z = ca + (local.z - cw) * tz / hw;
                v[i] = _bStart[sh] + _bRot[sh] * local;
            }
            _mesh.vertices = v;
            _vBase = v;                 // lead 2026-10-04: the cloak's fit goes on top of the remap
            _capeKey = int.MinValue;
            // the clavicle joints (bones, not vertices) through the same profile, so the clavicle segments start on the remapped chest
            for (int side = 0; side < 2; side++)
            {
                Vector3 c = _bClav[side];
                float cw, hw, ca, tz;
                if (Profile(SChest + c.y / _bLen[SChest], out cw, out hw, out ca, out tz)) c.z = ca + (c.z - cw) * tz / hw;
                _bClavR[side] = c;
            }
        }

        /// <summary>The torso profile at height u (hips 0..1, chest 1..2): linear between the bin middles that have one, the end ones held.</summary>
        private bool Profile(float u, out float cw, out float hw, out float ca, out float tz)
        {
            int lo = -1, hi = -1;
            for (int g = 0; g < 2 * Bins; g++)
            {
                if (!_pOk[g]) continue;
                float ug = g / Bins + (g % Bins + 0.5f) / Bins;
                if (ug <= u) lo = g;
                if (ug >= u && hi < 0) hi = g;
            }
            if (lo < 0) lo = hi;
            if (hi < 0) hi = lo;
            cw = hw = ca = tz = 0f;
            if (lo < 0) return false;
            float ulo = lo / Bins + (lo % Bins + 0.5f) / Bins, uhi = hi / Bins + (hi % Bins + 0.5f) / Bins;
            float f = uhi > ulo ? Mathf.Clamp01((u - ulo) / (uhi - ulo)) : 0f;
            cw = Mathf.Lerp(_pCW[lo], _pCW[hi], f); hw = Mathf.Lerp(_pHW[lo], _pHW[hi], f);
            ca = Mathf.Lerp(_pCA[lo], _pCA[hi], f); tz = Mathf.Lerp(_pTZ[lo], _pTZ[hi], f);
            return hw > 1e-3f;
        }

        /// <summary>The avatar / WoW ratio of one list kind (0 +x, 1 -x, 2 y, 3 +z, 4 -z) of a segment: for the sides the largest over
        /// the height bins (the avatar's widest part against WoW's at the same height - a sweater's hem at WoW's narrow waist), for y
        /// the tip; 0 = too few vertices on either. Allocates (calibration only).</summary>
        private float Ratio(List<float>[] lists, int s, int i)
        {
            if (i == 2)
            {
                int y = s * Bins * 5 + 2;
                return lists[y].Count >= 8 && _wExt[y] > 1e-3f ? Pct(lists[y]) / _wExt[y] : 0f;
            }
            // the whole segment (all bins): the avatar's percentile against WoW's largest
            var all = new List<float>();
            float wAll = 0f;
            for (int b = 0; b < Bins; b++) { int at = (s * Bins + b) * 5 + i; all.AddRange(lists[at]); wAll = Mathf.Max(wAll, _wExt[at]); }
            if (all.Count < 8 || wAll <= 1e-3f) return 0f;
            float whole = Pct(all) / wAll, best = whole;
            // per height bin, at most BinBoost x the whole: a wide part where WoW is narrow (a sweater's hem at WoW's waist)
            for (int b = 0; b < Bins; b++)
            {
                int at = (s * Bins + b) * 5 + i;
                List<float> l = lists[at];
                float w = _wExt[at];
                if (l.Count >= 8 && w > 1e-3f) best = Mathf.Max(best, Pct(l) / w);
            }
            return Mathf.Min(best, whole * BinBoost);
        }

        /// <summary>A point in a segment frame into the lists (its height bin: +x / -x, +z / -z; bin 0 also every y); torso and limbs
        /// only between their joints (+-10 %).</summary>
        /// <summary>Lead fix 2026-11-04: ours - a skinned mesh whose bounds' centre is farther than this from the hips is not on the body.</summary>
        public const float FarMeshMetres = 3f;

        private static float Sq(float v) { return v * v; }

        /// <summary>Lead fix 2026-11-04: ours - the farthest a vertex may be from its segment's axis to count as that segment's surface
        /// (torso 0.45 m or 2 x its length, limbs 0.30 m or 1 x, hands / feet 0.30 m or 2 x): generous for any body, but a vertex of
        /// another copy or a far mesh never pushes the thickness to its 3.00 limit.</summary>
        private static float MaxRadial(int s, float len)
        {
            if (s == SHips || s == SChest) return Mathf.Max(0.45f, 2f * len);
            if (s == SLHand || s == SRHand || s == SLFoot || s == SRFoot) return Mathf.Max(0.30f, 2f * len);
            return Mathf.Max(0.30f, len);
        }

        private static bool AddExtent(List<float>[] lists, int s, Vector3 local, float len)
        {
            bool end = s == SLHand || s == SRHand || s == SLFoot || s == SRFoot;
            if (!end && (local.y < -0.1f * len || local.y > 1.1f * len)) return false;
            int bin = Mathf.Clamp((int)(local.y / Mathf.Max(1e-4f, len) * Bins), 0, Bins - 1);
            int at = (s * Bins + bin) * 5;
            lists[at + (local.x >= 0f ? 0 : 1)].Add(Mathf.Abs(local.x));
            lists[s * Bins * 5 + 2].Add(local.y);
            lists[at + (local.z >= 0f ? 3 : 4)].Add(Mathf.Abs(local.z));
            return true;
        }

        private static float Pct(List<float> l)
        {
            if (l.Count == 0) return 0f;
            l.Sort();
            return l[Mathf.Clamp((int)(l.Count * ExtentPercentile), 0, l.Count - 1)];
        }

        /// <summary>WoW's bare body (geosets 0, 401, 501, 1301 - measured ArmShellRig: the body, bare wrists, bare shins, upper legs) per
        /// segment and height bin in its bind frame: its vertices plus each triangle's edge middles and centre (the low-poly faces lie
        /// inside their corners), the largest distance per side (a clean mesh: no stray vertices).</summary>
        private void MeasureWow(M2Model m, M2Skin skin, int[] segOfBone)
        {
            for (int i = 0; i < _wExt.Length; i++) _wExt[i] = 0f;
            for (int i = 0; i < _wNZ.Length; i++) { _wNZ[i] = 0; _wLoZ[i] = float.MaxValue; _wHiZ[i] = float.MinValue; }
            M2MeshData d = M2MeshData.Build(m, skin, id => id == 0 || id == 401 || id == 501 || id == 1301, null, null);
            var lists = new List<float>[SegCount * Bins * 5];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<float>();
            int nv = d.VertexCount;
            var seg = new int[nv];
            var pos = new Vector3[nv];
            for (int vi = 0; vi < nv; vi++)
            {
                int best = -1, bw = -1;
                for (int k = 0; k < 4; k++) { int wt = d.BoneWeights[vi * 4 + k]; if (wt > bw) { bw = wt; best = d.BoneIndices[vi * 4 + k]; } }
                seg[vi] = best >= 0 && best < segOfBone.Length ? segOfBone[best] : -1;
                pos[vi] = WowSpace.PointToUnity(d.Positions[vi * 3], d.Positions[vi * 3 + 1], d.Positions[vi * 3 + 2]);
            }
            foreach (var bt in d.Batches)
            {
                int[] ix = bt.Indices;
                for (int t = 0; t + 2 < ix.Length; t += 3)
                {
                    int a = ix[t], b = ix[t + 1], c = ix[t + 2];
                    WowPoint(lists, seg[a], pos[a]);
                    if (seg[a] == seg[b]) WowPoint(lists, seg[a], 0.5f * (pos[a] + pos[b]));
                    if (seg[b] == seg[c]) WowPoint(lists, seg[b], 0.5f * (pos[b] + pos[c]));
                    if (seg[a] == seg[b] && seg[b] == seg[c]) WowPoint(lists, seg[a], (pos[a] + pos[b] + pos[c]) / 3f);
                }
            }
            for (int i = 0; i < lists.Length; i++)
            {
                List<float> l = lists[i];
                float mx = 0f;
                for (int k = 0; k < l.Count; k++) if (l[k] > mx) mx = l[k];
                _wExt[i] = l.Count >= 3 ? mx : 0f;
            }
        }

        /// <summary>A WoW torso bin with a whole cross-section: 3 points or more and at least TorsoBinDepth of the segment's deepest bin
        /// (ours: the top of the chest is the neck's base, 0.39 of the chest's depth in HumanMale).</summary>
        private bool TorsoBin(int s, int b)
        {
            int at = s * Bins + b;
            if (_wNZ[at] < 3) return false;
            float most = 0f;
            for (int k = 0; k < Bins; k++) { int a2 = s * Bins + k; if (_wNZ[a2] >= 3) most = Mathf.Max(most, _wHiZ[a2] - _wLoZ[a2]); }
            return _wHiZ[at] - _wLoZ[at] >= TorsoBinDepth * most;
        }

        private void WowPoint(List<float>[] lists, int s, Vector3 p)
        {
            if (s < 0) return;
            if (s >= SFingers) s = s - SFingers < 5 ? SLHand : SRHand;   // the glove's fingers measure with its hand (the avatar's do)
            // the torso by height along the hips, as the avatar's (WoW's waist weights split diagonally: belly to the hips bone, the
            // back at that height to the chest's - a segment's own bones would give half cross-sections)
            if (s == SHips || s == SChest) s = Vector3.Dot(p - _bStart[SHips], _bRot[SHips] * Vector3.up) <= _bLen[SHips] ? SHips : SChest;
            Vector3 local = Quaternion.Inverse(_bRot[s]) * (p - _bStart[s]);
            if (AddExtent(lists, s, local, _bLen[s]) && s <= SChest)
            {
                int at = s * Bins + Mathf.Clamp((int)(local.y / Mathf.Max(1e-4f, _bLen[s]) * Bins), 0, Bins - 1);
                _wNZ[at]++;
                if (local.z < _wLoZ[at]) _wLoZ[at] = local.z;
                if (local.z > _wHiZ[at]) _wHiZ[at] = local.z;
            }
        }

        private void Knuckles(int side, Vector3 hand, Vector3 elbow, out Vector3 knuckles, out Vector3 across)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                Transform t = _fing[side * 4 + i];
                if (t == null) continue;
                sum += t.position;
                n++;
            }
            Transform index = _fing[side * 4], little = _fing[side * 4 + 3];
            across = index != null && little != null ? index.position - little.position : Vector3.zero;
            if (n > 0) knuckles = sum / n;
            else knuckles = hand + (hand - elbow) * 0.4f;   // ours: no finger bones
        }

        /// <summary>The avatar stands (see UprightHips / UprightTilt): its pose is the one a measurement can trust.</summary>
        private bool Upright(out string why)
        {
            why = "no bones";
            if (_hips == null || _head == null || _ul[0] == null || _ll[0] == null || _foot[0] == null) return true;   // cannot tell: measure
            float leg = (_ul[0].position - _ll[0].position).magnitude + (_ll[0].position - _foot[0].position).magnitude;
            float feet = Mathf.Min(_foot[0].position.y, _foot[1] != null ? _foot[1].position.y : _foot[0].position.y);
            float hips = _hips.position.y - feet;
            float tilt = Vector3.Angle(_head.position - _hips.position, Vector3.up);
            bool ok = leg > 1e-3f && hips >= UprightHips * leg && tilt <= UprightTilt;
            why = "hips " + hips.ToString("0.00") + " m over the feet for a " + leg.ToString("0.00") + " m leg, spine " + tilt.ToString("0") + " deg from vertical";
            return ok;
        }

        /// <summary>One of the avatar's skinned meshes was drawn by a camera last frame (no allocation).</summary>
        private bool AnyVisible(Animator a)
        {
            _sigList.Clear();
            a.GetComponentsInChildren(false, _sigList);
            bool any = false;
            for (int i = 0; i < _sigList.Count && !any; i++)
            {
                SkinnedMeshRenderer r = _sigList[i];
                any = r != null && r.enabled && r.isVisible && (_root == null || !r.transform.IsChildOf(_root));
            }
            _sigList.Clear();
            return any;
        }

        /// <summary>The avatar's skinned meshes as one number (renderer, vertex count) - no allocation. Lead fix 2026-10-04: no longer
        /// Renderer.enabled (the client's LODs and our isolation toggle it; each toggle measured the avatar again).</summary>
        private int MeshSig(Animator a)
        {
            _sigList.Clear();
            a.GetComponentsInChildren(false, _sigList);
            int h = 17;
            for (int i = 0; i < _sigList.Count; i++)
            {
                SkinnedMeshRenderer r = _sigList[i];
                if (r == null || (_root != null && r.transform.IsChildOf(_root))) continue;
                Mesh m = r.sharedMesh;
                h = h * 31 + r.GetInstanceID();
                h = h * 31 + (m != null ? m.vertexCount : 0);
            }
            _sigList.Clear();
            return h;
        }

        /// <summary>Lead (2026-10-04, the user: "add taper size for top and bottom"): each shell vertex of a piece with a taper, in its own
        /// segment's bind frame, scaled across the segment by Lerp(bottom, top) at its height - the torso about its measured centre line
        /// (RemapTorso's ca), a limb about its axis; the top of the chest / waist / neck is their upper end, of a limb its upper joint.
        /// Works on _vWork (after the remap), the cape's vertices excluded (the cloak has its own).</summary>
        private void TaperPieces()
        {
            if (_vSeg == null) return;
            bool any = false;
            for (int slot = ArmourAdjust.Chest; slot < ArmourAdjust.Cloak && !any; slot++)
                any = Mathf.Abs(Adjust[slot].TopK - 1f) > 1e-3f || Mathf.Abs(Adjust[slot].BottomK - 1f) > 1e-3f;
            if (!any) return;
            for (int i = 0; i < _vWork.Length && i < _vSeg.Length; i++)
            {
                if (_isCape != null && _isCape[i]) continue;
                int s = _vSeg[i];
                if (s < 0 || s >= SegCount) continue;
                PieceAdjust pa = Adjust[SlotOf(s)];
                float top = pa.TopK, bottom = pa.BottomK;
                if (Mathf.Abs(top - 1f) < 1e-3f && Mathf.Abs(bottom - 1f) < 1e-3f) continue;
                Vector3 local = Quaternion.Inverse(_bRot[s]) * (_vWork[i] - _bStart[s]);
                float u = Mathf.Clamp01(local.y / Mathf.Max(1e-4f, _bLen[s]));
                bool torso = s == SHips || s == SChest || s == SNeck;
                float k = Mathf.Lerp(bottom, top, torso ? u : 1f - u);
                float cz = 0f;
                if (s == SHips || s == SChest)
                {
                    float cw, hw, ca, tz;
                    if (Profile(s + u, out cw, out hw, out ca, out tz)) cz = ca;
                }
                local.x *= k;
                local.z = cz + (local.z - cz) * k;
                _vWork[i] = _bStart[s] + _bRot[s] * local;
            }
        }

        /// <summary>
        /// Lead (2026-10-04, the user: "add some finer controls to the armour placement ... something like top start and bottom end"): each
        /// full shell piece (chest .. feet) with a top / bottom edge offset - every vertex of the piece (the cape excluded) moved in the
        /// body's bind frame (x right, y up, z forward) by Lerp(bottom, top, u), u = its height between the piece's lowest (0) and highest
        /// (1) vertex: forward / back shears the piece (a bent robe straightened), up / down stretches it (where it starts and ends). Both
        /// legs / arms alike (no mirroring needed). Works on _vWork after the taper.
        /// </summary>
        private void EndPieces()
        {
            if (_vSeg == null) return;
            bool any = false;
            for (int slot = ArmourAdjust.Chest; slot <= ArmourAdjust.Feet && !any; slot++) any = Adjust[slot].HasEnds;
            if (!any) return;
            for (int s = 0; s < ArmourAdjust.Slots; s++) { _endLo[s] = float.MaxValue; _endHi[s] = float.MinValue; }
            for (int i = 0; i < _vBase.Length && i < _vSeg.Length; i++)
            {
                if (_isCape != null && _isCape[i]) continue;
                int sg = _vSeg[i];
                if (sg < 0 || sg >= SegCount) continue;
                int slot = SlotOf(sg);
                float y = _vBase[i].y;
                if (y < _endLo[slot]) _endLo[slot] = y;
                if (y > _endHi[slot]) _endHi[slot] = y;
            }
            for (int i = 0; i < _vWork.Length && i < _vSeg.Length; i++)
            {
                if (_isCape != null && _isCape[i]) continue;
                int sg = _vSeg[i];
                if (sg < 0 || sg >= SegCount) continue;
                int slot = SlotOf(sg);
                if (slot < ArmourAdjust.Chest || slot > ArmourAdjust.Feet) continue;
                PieceAdjust pa = Adjust[slot];
                if (!pa.HasEnds) continue;
                float span = _endHi[slot] - _endLo[slot];
                float u = span > 1e-4f ? Mathf.Clamp01((_vBase[i].y - _endLo[slot]) / span) : 0.5f;
                _vWork[i] += new Vector3(0f, Mathf.Lerp(pa.BottomY, pa.TopY, u), Mathf.Lerp(pa.BottomZ, pa.TopZ, u));
            }
        }

        private readonly float[] _endLo = new float[ArmourAdjust.Slots], _endHi = new float[ArmourAdjust.Slots];

        /// <summary>Lead (2026-10-04): the slot a segment belongs to - 3 chest (chest, neck, clavicles), 4 waist (hips), 5 arms, 6 hands
        /// (with the glove's fingers), 7 legs, 8 feet.</summary>
        private static int SlotOf(int s)
        {
            if (s >= SFingers) return ArmourAdjust.Hands;
            switch (s)
            {
                case SHips: return ArmourAdjust.Waist;
                case SLUpper: case SRUpper: case SLFore: case SRFore: return ArmourAdjust.Arms;
                case SLHand: case SRHand: return ArmourAdjust.Hands;
                case SLThigh: case SRThigh: case SLShin: case SRShin: return ArmourAdjust.Legs;
                case SLFoot: case SRFoot: return ArmourAdjust.Feet;
                default: return ArmourAdjust.Chest;
            }
        }

        private static bool LeftSide(int s)
        {
            return s == SLClav || s == SLUpper || s == SLFore || s == SLHand || s == SLThigh || s == SLShin || s == SLFoot || (s >= SFingers && s < SFingers + 5);
        }

        /// <summary>Lead (2026-10-04, the Armour page): Put with the wearer's fit for the segment's slot - hidden = collapsed onto its
        /// joint; the offset in the body's frame (x right, y up, z forward; on a left limb x mirrored, so + is outward on both sides);
        /// the size around the segment's axis (the feet also along it: the boot's toe).</summary>
        private void PutFit(int s, Vector3 pos, Quaternion rot, float sx, float sy, float sz, Vector3 draw)
        {
            PieceAdjust a = Adjust[SlotOf(s)];
            if (!a.Show) { Put(s, pos, rot, 1e-4f, 1e-4f, 1e-4f, draw); return; }
            Vector3 off = a.Offset;
            if (LeftSide(s)) off.x = -off.x;
            float f = Mathf.Clamp(a.Size, 0.5f, 2f);
            bool foot = s == SLFoot || s == SRFoot;
            if (Mathf.Abs(a.Tilt) > 0.01f)
            {
                // lead 2026-10-04: the tilt about the body's right axis - the torso pieces (chest, neck, waist) about their middle, a limb
                // about its joint (so it stays on it)
                Quaternion q = Quaternion.AngleAxis(Mathf.Clamp(a.Tilt, -60f, 60f), _root.rotation * Vector3.right);
                if (s == SHips || s == SChest || s == SNeck)
                {
                    Vector3 mid = pos + rot * Vector3.up * (_aLen[s] * 0.5f);
                    pos = mid + q * (pos - mid);
                }
                rot = q * rot;
            }
            if ((s == SHips || s == SChest) && _v0 != null && Mathf.Abs(f - 1f) > 1e-4f)
            {
                // lead 2026-10-04 (the user: "The waist, when increasing size, makes it stick out the rear A LOT"): the torso's depth is in its
                // remapped vertices around the avatar's measured centre (RemapTorso: z' = ca + ...), which sits behind the joint line - so
                // the size scales about that centre, not the joints (pos + rot * (0, 0, c * sz * (1 - f))).
                float c = s == SHips ? _centreZ[0] : _centreZ[1];
                pos += rot * new Vector3(0f, 0f, c * sz * (1f - f));
            }
            Put(s, pos + _root.rotation * off, rot, sx * f, foot ? sy * f : sy, sz * f, draw);
        }

        private ArmourBodyHide _bodyHide;   // lead 2026-10-04: "Hide avatar part" per piece

        // ---- lead 2026-11-04 (user: "Sometimes I do not see remotes armour AT ALL / sometimes their armour is HUGE ... NOTHING like what they
        // have synced / even if they toggle the armour off and on, I don't see it updating"; Player (34/35).log: viewers measured remote copies -
        // the client's stand-in, an avatar sitting, meshes 34 m away - and hid the shell until a measurement that never came). The WEARER's own
        // good measurement is synced (WowArmourRecord.Shape0-4, 150 bits) and a viewer draws with it: no measuring of remote avatars, shown at
        // once, the same fit everyone sees. Layout (bit stream, 30 bits per int): said (1); thickness x / z of hips, chest, upper arms,
        // forearms, hands, thighs, shins, feet (left / right averaged) 16 x 5 bits, log scale 0.3 .. 3.0 (0 = not measured); feet tip 5 bits
        // (x 0.1); torso bins (2 x Bins) x (centre 4 bits: 0 = WoW's own, else -0.35 + (q - 1) x 0.05 m; depth ratio 4 bits 0.5 + q x 2/15).
        public const int ShapeInts = 5;
        private static readonly int[,] ShapePairs = { { SHips, SHips }, { SChest, SChest }, { SLUpper, SRUpper }, { SLFore, SRFore }, { SLHand, SRHand }, { SLThigh, SRThigh }, { SLShin, SRShin }, { SLFoot, SRFoot } };
        private readonly int[] _shapeIn = new int[ShapeInts], _shapeApplied = new int[ShapeInts];
        private bool _shapeDirty = true, _shapeSaidLogged;

        /// <summary>Remote: the wearer's synced measurement for this frame (all 0 = not said: the viewer measures as before).</summary>
        public void SetWearerShape(int a, int b, int c, int d, int e) { _shapeIn[0] = a; _shapeIn[1] = b; _shapeIn[2] = c; _shapeIn[3] = d; _shapeIn[4] = e; }

        private bool WearerShapeSaid { get { return (_shapeIn[0] & 1) != 0; } }

        private static int QThick(float v) { return v <= 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(Mathf.Log10(Mathf.Max(v, 0.3f) / 0.3f) * 30f) + 1, 1, 31); }
        private static float DThick(int q) { return q <= 0 ? 0f : 0.3f * Mathf.Pow(10f, (q - 1) / 30f); }

        private sealed class Bits
        {
            public readonly int[] Ints;
            private int _pos;
            public Bits(int[] ints) { Ints = ints; }
            public void Put(int v, int n) { for (int i = 0; i < n; i++, _pos++) if (((v >> i) & 1) != 0) Ints[_pos / 30] |= 1 << (_pos % 30); }
            public int Get(int n) { int v = 0; for (int i = 0; i < n; i++, _pos++) if (((Ints[_pos / 30] >> (_pos % 30)) & 1) != 0) v |= 1 << i; return v; }
        }

        /// <summary>Local: our last GOOD measurement packed for the record (all 0 while there is none).</summary>
        public void ExportShape(int[] into)
        {
            for (int i = 0; i < into.Length; i++) into[i] = 0;
            Measured m = LastGood();
            if (m == null || into.Length < ShapeInts) return;
            var w = new Bits(into);
            w.Put(1, 1);
            for (int p = 0; p < ShapePairs.GetLength(0); p++)
            {
                Vector3 a = m.Thick[ShapePairs[p, 0]], b = m.Thick[ShapePairs[p, 1]];
                w.Put(QThick(Avg(a.x, b.x)), 5);
                w.Put(QThick(Avg(a.z, b.z)), 5);
            }
            w.Put(Mathf.Clamp(Mathf.RoundToInt(Avg(m.Thick[SLFoot].y, m.Thick[SRFoot].y) / 0.1f), 0, 31), 5);
            for (int g = 0; g < 2 * Bins; g++)
            {
                bool own = m.POk[g] && Mathf.Abs(m.PCA[g]) > 1e-6f;
                w.Put(own ? Mathf.Clamp(Mathf.RoundToInt((m.PCA[g] + 0.35f) / 0.05f) + 1, 1, 15) : 0, 4);
                float r = m.POk[g] && m.PHW[g] > 1e-4f ? m.PTZ[g] / m.PHW[g] : 1f;
                w.Put(Mathf.Clamp(Mathf.RoundToInt((r - 0.5f) / (2f / 15f)), 0, 15), 4);
            }
        }

        private static float Avg(float a, float b) { return a > 0f && b > 0f ? 0.5f * (a + b) : Mathf.Max(a, b); }

        /// <summary>Remote: the wearer's shape into the segments and the torso profile (as UseMeasured does for our own).</summary>
        private void ApplyWearerShape()
        {
            Array.Copy(_shapeIn, _shapeApplied, ShapeInts);
            var r = new Bits((int[])_shapeIn.Clone());
            r.Get(1);
            for (int s = 0; s < SegCount; s++) _thick[s] = Vector3.zero;
            float[] xs = new float[ShapePairs.GetLength(0)], zs = new float[ShapePairs.GetLength(0)];
            for (int p = 0; p < xs.Length; p++) { xs[p] = DThick(r.Get(5)); zs[p] = DThick(r.Get(5)); }
            float footY = r.Get(5) * 0.1f;
            for (int p = 0; p < xs.Length; p++)
            {
                if (xs[p] <= 0f || zs[p] <= 0f) continue;
                for (int side = 0; side < 2; side++)
                {
                    int s = ShapePairs[p, side];
                    _thick[s] = new Vector3(xs[p], s == SLFoot || s == SRFoot ? footY : 0f, zs[p]);
                }
            }
            var none = new List<float>[2 * Bins];
            for (int i = 0; i < none.Length; i++) none[i] = new List<float>();
            ProfileTorso(none, false);   // WoW's bins (_pOk, _pCW, _pHW) and the fallback profile
            for (int g = 0; g < 2 * Bins; g++)
            {
                int qc = r.Get(4), qr = r.Get(4);
                if (!_pOk[g]) continue;
                if (qc > 0) _pCA[g] = -0.35f + (qc - 1) * 0.05f;
                _pTZ[g] = _pHW[g] * Mathf.Clamp(0.5f + qr * (2f / 15f), TorsoRatioMin, TorsoRatioMax);
            }
            for (int seg = 0; seg < 2; seg++)
            {
                float sum = 0f; int cnt = 0;
                for (int b = 0; b < Bins; b++) { int g = seg * Bins + b; if (_pOk[g]) { sum += _pCA[g]; cnt++; } }
                _centreZ[seg] = cnt > 0 ? sum / cnt : 0f;
            }
            RemapTorso();
            if (!_shapeSaidLogged)
            {
                _shapeSaidLogged = true;
                Say("Armour: full shell for " + _label + " uses the wearer's own measurement (synced): hips " + xs[0].ToString("0.00") + "/" + zs[0].ToString("0.00") +
                    " chest " + xs[1].ToString("0.00") + "/" + zs[1].ToString("0.00") + " upper arms " + xs[2].ToString("0.00") + "/" + zs[2].ToString("0.00") +
                    " thighs " + xs[5].ToString("0.00") + "/" + zs[5].ToString("0.00") + " - not measured here");
            }
        }
        private bool _builtRobe;            // lead 2026-11-03: the built look's chest is a robe (geoset group 13 >= 2, ArmourShellLook) - it covers the legs
        private readonly float[] _builtCover = new float[11];   // lead 2026-11-04: ArmourShellLook.Cover of the built look
        private bool _builtGloves, _builtBoots;   // geoset groups 4 (gloves) / 5 (boots) > 1: 3D pieces over the hands / feet
        public const float CoverEnough = 0.6f;   // ours (2026-11-04): a body area counts as covered from 60 % painted (full sleeves / shirts measured 74-100 %)

        /// <summary>
        /// Lead 2026-11-03/04 (user: "if I don't have an armour piece in a slot, it should override 'hide avatar' ... It needs to check"):
        /// an armour piece hides the avatar's body part only where the BUILT armour really covers it - every body-texture area of that part
        /// painted CoverEnough or more, or a 3D piece over it: chest = torso upper; waist = torso lower; arms = arm upper AND arm lower; hands =
        /// hand or gloves; legs = (leg upper AND leg lower) or a robe; feet = foot or boots. 14.1 used the item slots and hid the forearms
        /// under a robe's short sleeves (Player.log: "hidden under the armour (chest, arms, legs)", robe arm lower 0 %).
        /// </summary>
        public bool Worn(int armourSlot)
        {
            if (!_haveBuilt) return false;
            float[] c = _builtCover;
            const int AU = (int)CharacterComposer.Region.ArmUpper, AL = (int)CharacterComposer.Region.ArmLower, HA = (int)CharacterComposer.Region.Hand;
            const int TU = (int)CharacterComposer.Region.TorsoUpper, TL = (int)CharacterComposer.Region.TorsoLower;
            const int LU = (int)CharacterComposer.Region.LegUpper, LL = (int)CharacterComposer.Region.LegLower, FO = (int)CharacterComposer.Region.Foot;
            switch (armourSlot)
            {
                case ArmourAdjust.Chest: return c[TU] >= CoverEnough;
                case ArmourAdjust.Waist: return c[TL] >= CoverEnough;
                case ArmourAdjust.Arms: return c[AU] >= CoverEnough && c[AL] >= CoverEnough;
                case ArmourAdjust.Hands: return c[HA] >= CoverEnough || _builtGloves;
                case ArmourAdjust.Legs: return (c[LU] >= CoverEnough && c[LL] >= CoverEnough) || _builtRobe;
                case ArmourAdjust.Feet: return c[FO] >= CoverEnough || _builtBoots;
                default: return true;
            }
        }
        private readonly float[] _centreZ = new float[2];   // the hips' / chest's mean measured centre (bind z, RemapTorso's ca)
        private bool[] _isCape;

        /// <summary>Lead (2026-10-04): the cloak's offset (body frame = the shell's bind frame: x right, y up, z forward) and size
        /// around its top middle, on its own vertices over the torso remap - only when they change.</summary>
        private void FitCape()
        {
            if (_mesh == null || _vBase == null || _vWork == null) return;
            bool hasCape = _capeV != null && _capeV.Length > 0;
            PieceAdjust a = Adjust[ArmourAdjust.Cloak];
            int key = 17;
            for (int slot = ArmourAdjust.Chest; slot <= ArmourAdjust.Cloak; slot++)
            {
                key = key * 31 + Mathf.RoundToInt(Adjust[slot].TopK * 100f);
                key = key * 31 + Mathf.RoundToInt(Adjust[slot].BottomK * 100f);
            }
            key = key * 31 + Mathf.RoundToInt(a.Offset.x * 1000f);
            key = key * 31 + Mathf.RoundToInt(a.Offset.y * 1000f);
            key = key * 31 + Mathf.RoundToInt(a.Offset.z * 1000f);
            key = key * 31 + Mathf.RoundToInt(a.Size * 100f);
            key = key * 31 + Mathf.RoundToInt(a.Tilt * 10f);
            key = key * 31 + (_collarOff ? 1 : 0);   // lead fix 2026-10-04
            for (int slot = ArmourAdjust.Chest; slot <= ArmourAdjust.Feet; slot++)   // lead 2026-10-04: the top / bottom edges
            {
                PieceAdjust e = Adjust[slot];
                key = key * 31 + Mathf.RoundToInt(e.TopZ * 1000f); key = key * 31 + Mathf.RoundToInt(e.TopY * 1000f);
                key = key * 31 + Mathf.RoundToInt(e.BottomZ * 1000f); key = key * 31 + Mathf.RoundToInt(e.BottomY * 1000f);
            }
            key = key * 31 + _vBase.GetHashCode();
            if (key == _capeKey) return;
            _capeKey = key;
            Array.Copy(_vBase, _vWork, _vWork.Length);
            TaperPieces();
            EndPieces();   // lead 2026-10-04
            if (!hasCape) { _mesh.vertices = _vWork; return; }
            float f = Mathf.Clamp(a.Size, 0.5f, 2f);
            Quaternion tilt = Quaternion.AngleAxis(Mathf.Clamp(a.Tilt, -60f, 60f), Vector3.right);   // lead 2026-10-04: + swings the hem back
            // the cloak's taper (lead 2026-10-04): its width at the shoulders (top) and at the hem (bottom), about its middle line
            float ct = a.TopK, cb = a.BottomK, low = float.MaxValue;
            foreach (int i in _capeV) low = Mathf.Min(low, _vBase[i].y);
            float span = Mathf.Max(1e-3f, _capeTop.y - low);
            foreach (int i in _capeV)
            {
                Vector3 d = _vBase[i] - _capeTop;
                float u = Mathf.Clamp01((_vBase[i].y - low) / span);   // 0 hem .. 1 shoulders
                d.x *= Mathf.Lerp(cb, ct, u);
                _vWork[i] = _capeTop + tilt * (d * f) + a.Offset;
            }
            float shoulderY = 0.5f * (_bStart[SLUpper].y + _bStart[SRUpper].y), pin = _capeTop.y + (shoulderY - _capeTop.y) * f + a.Offset.y;
            if (_collarOff) FoldCollar(shoulderY, pin);
            _mesh.vertices = _vWork;
            // cape agent 2026-10-04: the physics rows on the fitted cape; pinned on WoW's shoulder joints' height through the same fit
            if (_cape != null) _cape.Rebind(_vWork, _v0, pin);
        }

        /// <summary>Lead fix 2026-10-04 (first person, "cloak is not visible in VR"): the cape's collar - its vertices above WoW's shoulder
        /// joints (bind), which wrap the neck - laid on the shoulder line (pin = that line through the Cloak's fit, ArmourCape's row 0) and
        /// no farther forward than the cape's back centre there, so its triangles fold flat behind the neck. Works on _vWork.</summary>
        private void FoldCollar(float shoulderY, float pin)
        {
            int back = -1;
            foreach (int i in _capeV)
            {
                Vector3 b = _vBase[i];
                if (b.y > shoulderY || Mathf.Abs(b.x - _capeTop.x) > 0.08f) continue;   // ours: the centre strip as ArmourCape.StripHalf
                if (back < 0 || b.y > _vBase[back].y) back = i;
            }
            float zBack = float.MaxValue;
            if (back >= 0) zBack = _vWork[back].z;
            else foreach (int i in _capeV) if (_vBase[i].y <= shoulderY) zBack = Mathf.Min(zBack, _vWork[i].z);
            if (zBack == float.MaxValue) return;
            int folded = 0;
            foreach (int i in _capeV)
            {
                if (_vBase[i].y <= shoulderY) continue;
                Vector3 w = _vWork[i];
                _vWork[i] = new Vector3(w.x, pin, Mathf.Min(w.z, zBack));
                folded++;
            }
            if (!_collarSaid) { _collarSaid = true; Say("Armour: full shell for " + _label + ": first person - the cape stays on, its collar (" + folded + " vertices above the shoulder line) folded behind the neck"); }
        }

        private bool _collarSaid;

        /// <summary>Cape agent 2026-10-04: the avatar for the cape's physics (ArmourCape) - the hips / chest segments as Place poses them
        /// (draw included) but without the pieces' own fit, the leg joints, and the leg armour's back radius (WoW's bare thigh / shin
        /// back extent, MeasureWow, x the segment's depth scale).</summary>
        private void StepCape(bool hidden, float k, Vector3 draw)
        {
            bool remapped = _v0 != null;
            _capePose.ChestPos = _aPos[SChest] + draw;
            _capePose.ChestRot = _aRot[SChest];
            _capePose.ChestScale = new Vector3(Thick(SChest, 0) * k, _aLen[SChest] / _bLen[SChest], (remapped ? 1f : Thick(SChest, 1)) * k);
            _capePose.HipsPos = _aPos[SHips] + draw;
            _capePose.HipsRot = _aRot[SHips];
            _capePose.HipsScale = new Vector3(Thick(SHips, 0) * k, _aLen[SHips] / _bLen[SHips], (remapped ? 1f : Thick(SHips, 1)) * k);
            _capePose.Facing = _root.forward;
            _capePose.LHip = _ul[0].position + draw; _capePose.RHip = _ul[1].position + draw;
            _capePose.LKnee = _ll[0].position + draw; _capePose.RKnee = _ll[1].position + draw;
            _capePose.LAnkle = _foot[0].position + draw; _capePose.RAnkle = _foot[1].position + draw;
            _capePose.ThighRadius = Mathf.Max(BackRadius(SLThigh), BackRadius(SRThigh)) * k;
            _capePose.ShinRadius = Mathf.Max(BackRadius(SLShin), BackRadius(SRShin)) * k;
            _cape.Step(hidden, in _capePose);
        }

        private float BackRadius(int s)
        {
            float r = 0f;
            for (int b = 0; b < Bins; b++) r = Mathf.Max(r, _wExt[(s * Bins + b) * 5 + 4]);
            return r * Thick(s, 1);
        }

        private void Put(int s, Vector3 pos, Quaternion rot, float sx, float sy, float sz, Vector3 draw)
        {
            Transform t = _seg[s];
            t.SetPositionAndRotation(pos + draw, rot);
            t.localScale = new Vector3(sx, Mathf.Max(1e-4f, sy), sz);
        }

        private static Quaternion FrameRight(Vector3 up, Vector3 right)
        {
            if (up.sqrMagnitude < 1e-10f) up = Vector3.up;
            up.Normalize();
            Vector3 r = right - Vector3.Dot(right, up) * up;
            if (r.sqrMagnitude < 1e-10f) r = Vector3.Cross(up, Vector3.forward);
            return Quaternion.LookRotation(Vector3.Cross(r.normalized, up), up);
        }

        private static Vector3 Along(Vector3[] pts, int n, float frac)
        {
            float total = 0f;
            for (int i = 1; i < n; i++) total += (pts[i] - pts[i - 1]).magnitude;
            float want = total * Mathf.Clamp01(frac);
            for (int i = 1; i < n; i++)
            {
                float d = (pts[i] - pts[i - 1]).magnitude;
                if (want <= d || i == n - 1) return Vector3.Lerp(pts[i - 1], pts[i], d > 1e-6f ? Mathf.Clamp01(want / d) : 0f);
                want -= d;
            }
            return pts[n - 1];
        }

        // ------------------------------------------------------------------------------------------------ housekeeping

        private void Say(string s)
        {
            if (s == _said) return;
            _said = s;
            WowLog.Info(s);
        }

        public void Hide()
        {
            if (_bodyHide != null) _bodyHide.Apply(null, 0, 0, 0f);   // lead 2026-10-04: the avatar whole again
            if (_go != null && _go.activeSelf) _go.SetActive(false);
        }

        private void Drop()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _root = null;
            for (int s = 0; s < SegCount; s++) _seg[s] = null;
            _mesh = null;
            _cape = null;   // cape agent 2026-10-04: its rows were children of the shell
            _v0 = null;
            _vSeg = null;
            _wBones = null;
            _smr = null;
            _matsAll = _matsNoCape = null;
            _wSeg = null;
            for (int f = 0; f < 10; f++) { _fingBase[f] = -1; _fingOk[f] = false; }
            _calFor = null;
        }

        public void Destroy()
        {
            if (_bodyHide != null) { _bodyHide.Destroy(); _bodyHide = null; }   // lead 2026-10-04
            _known.Clear(); _lastGood = null; _standIn = _needMeasure = false;   // lead fix 2026-10-04
            Drop();
            _job = null;
            _haveBuilt = false;
            _builtGender = -1;
            _said = null;
        }
    }
}
