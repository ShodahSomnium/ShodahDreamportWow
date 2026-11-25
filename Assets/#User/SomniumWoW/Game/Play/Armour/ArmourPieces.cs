// iteration 11 - owner avatar (proposal-avatar.md 6.A, phase A1): "immersive armour" - the equipped helm and shoulder pads (WoW item
// M2s from the player's own install) on a Somnium avatar. Setting OFF by default (the user's wish); the WEARER decides (record int 32).
using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Models;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Armour
{
    /// <summary>Lead (2026-10-03): "Full (experimental)" (the A2 armour shell) is offered and drawn only once its build is finished.
    /// A2 finished 2026-10-03 (rendered in the armour lab: plate, robe, tabard, cape, fit 1.3, RPM Men / Women, an unreadable avatar
    /// mesh, first person): FullReady true. False again = mode 2 acts as Helm and shoulders, here and for others.</summary>
    public static class ArmourGate
    {
        public const bool FullReady = true;
        public static int Mode(int wanted) { int m = wanted < 0 ? 0 : wanted > 2 ? 2 : wanted; return m == 2 && !FullReady ? 1 : m; }
    }

    /// <summary>
    /// Per avatar (local or remote) while the wearer's mode is >= 1 and the avatar has a humanoid Animator:
    /// - Helm: Item\ObjectComponents\Head\&lt;ModelName[0] base&gt;_Hu&lt;M|F&gt;.m2 (Human models of the wearer's gender - user decision
    ///   U2; falls back to the wearer's race prefix, then HuM) with ReplaceablePaths[2] = Item\ObjectComponents\Head\&lt;ModelTexture[0]&gt;.blp
    ///   (the helm M2's only texture is type 2, measured AvArmModel). Placed where WoW's helm attachment (11) sits over the head pivot:
    ///   +0.184 yd up / +0.063 yd forward (HumanMale), +0.185 / +0.085 (HumanFemale) (measured AvArmAttach), scaled by k = the avatar's
    ///   head height / WoW's head pivot height (1.843 / 1.712 yd) x Armour fit; turned with the head bone (its T-pose turn taken out).
    /// - Shoulders: Item\ObjectComponents\Shoulder\&lt;ModelName[0]&gt;.m2 = LEFT, [1] = RIGHT, textures ModelTexture[0] / [1]; at each upper
    ///   arm joint +0.094 yd up and 0.047 yd inward (HumanMale att 5 / 6 vs the shoulder joints; female +0.087 / 0.031), scaled by the
    ///   avatar's shoulder span / WoW's (0.516 / 0.34 yd) x fit, turned with the torso (ours).
    /// - The local helm is not drawn while the camera is inside it (desktop first person, &lt; 0.3 m; VR always); HideHelm hides it.
    /// One build per frame over all avatars; one log line per change.
    /// </summary>
    /// <summary>Lead (2026-10-03): the wearer's placement of one armour piece (the Armour page of Settings; for others from record
    /// ints 45-47, ArmourAdjust). Offset in metres in the piece's own frame (x right, y up, z forward), Size x 0.5 .. 2.</summary>
    public struct PieceAdjust
    {
        public bool Show;
        public Vector3 Offset;
        public float Size;
        public bool Flag;   // the helm: "Face in helm" - the head hidden, a snapshot of the face inside the helm (ArmourFaceCard)
        public float Tilt;  // degrees, + = the top leans forward (lead 2026-10-04)
        public float TaperTop, TaperBottom;   // x at the piece's top / bottom (lead 2026-10-04); 0 (not set) reads as 1 - TopK / BottomK
        public float TopZ, TopY, BottomZ, BottomY;   // lead 2026-10-04: m, the full shell piece's top / bottom edge forward (+) / up (+)
        public bool HasEnds { get { return Mathf.Abs(TopZ) > 1e-4f || Mathf.Abs(TopY) > 1e-4f || Mathf.Abs(BottomZ) > 1e-4f || Mathf.Abs(BottomY) > 1e-4f; } }

        public float TopK { get { return TaperTop > 0.01f ? Mathf.Clamp(TaperTop, 0.5f, 1.5f) : 1f; } }
        public float BottomK { get { return TaperBottom > 0.01f ? Mathf.Clamp(TaperBottom, 0.5f, 1.5f) : 1f; } }
        public static readonly PieceAdjust Default = new PieceAdjust { Show = true, Size = 1f };
    }

    /// <summary>Lead (2026-10-03): one piece's placement in one record int - bit 30 said, bit 29 hidden, 7 bits each for x / y / z in cm
    /// (+64, -64 .. 63 cm), 7 bits size ((size - 0.5) / 0.02). 0 (an older record) = not said = the default placement.</summary>
    public static class ArmourAdjust
    {
        public const int Said = 1 << 30, Hidden = 1 << 29, FlagBit = 1 << 28;
        /// <summary>The slots (WowSettings.ArmourSlots): 0-2 the objects, 3-9 the full shell's pieces (lead 2026-10-04).</summary>
        public const int Slots = 10;
        public static readonly string[] Names = { "Helm", "Left shoulder", "Right shoulder", "Chest", "Waist", "Arms", "Hands", "Legs", "Feet", "Cloak" };
        public const int Helm = 0, LShoulder = 1, RShoulder = 2, Chest = 3, Waist = 4, Arms = 5, Hands = 6, Legs = 7, Feet = 8, Cloak = 9;

        public static int Pack(bool show, float x, float y, float z, float size, bool flag = false)
        {
            int s = Mathf.Clamp(Mathf.RoundToInt((size - 0.5f) / 0.02f), 0, 127);
            return Said | (show ? 0 : Hidden) | (flag ? FlagBit : 0) | Q(x) | (Q(y) << 7) | (Q(z) << 14) | (s << 21);
        }

        private static int Q(float m) { return Mathf.Clamp(Mathf.RoundToInt(m * 100f), -64, 63) + 64; }

        public static PieceAdjust Unpack(int v)
        {
            if ((v & Said) == 0) return PieceAdjust.Default;
            return new PieceAdjust
            {
                Show = (v & Hidden) == 0,
                Flag = (v & FlagBit) != 0,
                Offset = new Vector3(((v & 127) - 64) / 100f, (((v >> 7) & 127) - 64) / 100f, (((v >> 14) & 127) - 64) / 100f),
                Size = 0.5f + ((v >> 21) & 127) * 0.02f
            };
        }

        /// <summary>The local wearer's piece i from Settings.</summary>
        public static PieceAdjust Local(int i)
        {
            return new PieceAdjust { Show = WowSettings.ArmourShow[i], Offset = new Vector3(WowSettings.ArmourX[i], WowSettings.ArmourY[i], WowSettings.ArmourZ[i]), Size = WowSettings.ArmourSize[i],
                                     Flag = HidesAvatar(i), Tilt = WowSettings.ArmourTilt[i],
                                     TaperTop = WowSettings.ArmourTaperTop[i], TaperBottom = WowSettings.ArmourTaperBottom[i],
                                     TopZ = WowSettings.ArmourTopZ[i], TopY = WowSettings.ArmourTopY[i], BottomZ = WowSettings.ArmourBottomZ[i], BottomY = WowSettings.ArmourBottomY[i] };
        }

        /// <summary>Lead (2026-10-04): the tilts of slots first .. first + 4 in one record int - bit 30 said, 6 bits each (1.5 degree steps,
        /// -46.5 .. +46.5, +32). Record ints 55 (slots 0-4) and 56 (5-9); 0 = not said = no tilt.</summary>
        public const float TiltStep = 1.5f;

        public static int PackTilts(int first)
        {
            int v = Said;
            for (int k = 0; k < 5 && first + k < Slots; k++)
                v |= (Mathf.Clamp(Mathf.RoundToInt(WowSettings.ArmourTilt[first + k] / TiltStep), -31, 31) + 32) << (k * 6);
            return v;
        }

        /// <summary>Lead (2026-10-04): the tapers of slots 3-9 (top, bottom, top, bottom ...: 14 values) in 3 record ints (57, 58, 59) -
        /// bit 30 said, 6 bits each: 0 = not said (x1), else 1 + (taper - 0.5) / 0.02.</summary>
        public static int PackTapers(int part)
        {
            int v = Said;
            for (int k = 0; k < 5; k++)
            {
                int n = part * 5 + k;   // 0 .. 13
                if (n >= 14) break;
                int slot = 3 + n / 2;
                float t = n % 2 == 0 ? WowSettings.ArmourTaperTop[slot] : WowSettings.ArmourTaperBottom[slot];
                v |= (Mathf.Clamp(Mathf.RoundToInt((t - 0.5f) / 0.02f), 0, 50) + 1) << (k * 6);
            }
            return v;
        }

        /// <summary>Lead (2026-10-04): the end offsets of slots 3-8 (chest .. feet; 4 each: top Z, top Y, bottom Z, bottom Y = 24 values) in the
        /// record's last 4 ints (60-63) - bit 30 said, 5 bits each (6 a int): 0 = not said (0), else 16 + cm (-15 .. +15 cm, 1 cm steps).</summary>
        public const int EndSlots = 6, EndFirst = 3;

        public static int PackEnds(int part)
        {
            int v = Said;
            for (int k = 0; k < 6; k++)
            {
                int n = part * 6 + k;   // 0 .. 23
                if (n >= EndSlots * 4) break;
                int slot = EndFirst + n / 4, which = n % 4;
                float m = which == 0 ? WowSettings.ArmourTopZ[slot] : which == 1 ? WowSettings.ArmourTopY[slot] : which == 2 ? WowSettings.ArmourBottomZ[slot] : WowSettings.ArmourBottomY[slot];
                v |= (Mathf.Clamp(Mathf.RoundToInt(m * 100f), -15, 15) + 16) << (k * 5);
            }
            return v;
        }

        /// <summary>which 0 top Z, 1 top Y, 2 bottom Z, 3 bottom Y; metres (0 when not said or another slot).</summary>
        public static float UnpackEnd(int e0, int e1, int e2, int e3, int slot, int which)
        {
            if (slot < EndFirst || slot >= EndFirst + EndSlots) return 0f;
            int n = (slot - EndFirst) * 4 + which;
            int v = n < 6 ? e0 : n < 12 ? e1 : n < 18 ? e2 : e3;
            if ((v & Said) == 0) return 0f;
            int q = (v >> ((n % 6) * 5)) & 31;
            return q == 0 ? 0f : (q - 16) / 100f;
        }

        public static float UnpackTaper(int t0, int t1, int t2, int slot, bool top)
        {
            if (slot < 3) return 1f;
            int n = (slot - 3) * 2 + (top ? 0 : 1);
            int v = n < 5 ? t0 : n < 10 ? t1 : t2;
            if ((v & Said) == 0) return 1f;
            int q = (v >> ((n % 5) * 6)) & 63;
            return q == 0 ? 1f : 0.5f + (q - 1) * 0.02f;
        }

        public static float UnpackTilt(int v, int k)
        {
            if ((v & Said) == 0) return 0f;
            return (((v >> (k * 6)) & 63) - 32) * TiltStep;
        }

        public static int PackLocal(int i)
        {
            return Pack(WowSettings.ArmourShow[i], WowSettings.ArmourX[i], WowSettings.ArmourY[i], WowSettings.ArmourZ[i], WowSettings.ArmourSize[i], HidesAvatar(i));
        }

        /// <summary>The piece's flag bit: the helm's "Face in helm" (head hidden, ArmourFaceCard), the full shell's chest .. feet "Hide avatar
        /// part" (ArmourBodyHide); the shoulders and the cloak have none (lead 2026-10-04).</summary>
        public static bool HidesAvatar(int i)
        {
            if (i == Helm) return WowSettings.HelmHideHair;
            return i >= Chest && i <= Feet && WowSettings.ArmourHideAvatar[i];
        }
    }

    public sealed class ArmourPieces
    {
        public const string ContainerName = "WoW Avatar Armour";
        public const float HelmCameraClear = 0.3f;   // ours: m
        /// <summary>The client's NON_RENDERER_BY_MAIN_CAMERA_LAYER (CS AvatarSystem/Scripts/Roots/Base/BaseAvatarRoot.cs:124): the layer of the
        /// avatar's mirror copy ("Root for the tablet camera and mirrors", MirrorRoot.cs) - the Somnium camera and mirrors draw it, the
        /// main camera does not. A helm hidden from our own eyes goes there (HelmInPhotos).</summary>
        public const int PhotoLayer = 8;

        /// <summary>Lead (2026-10-03): the wearer's placement per piece (0 helm, 1 left shoulder, 2 right shoulder), set before Place.</summary>
        public readonly PieceAdjust[] Adjust = { PieceAdjust.Default, PieceAdjust.Default, PieceAdjust.Default };   // 0-2 (the shell has 3-9)
        /// <summary>Local wearer only: a helm hidden from our own eyes stays drawn for the Somnium camera and mirrors (PhotoLayer).</summary>
        public bool HelmInPhotos;
        private bool _helmPhoto;
        private int _helmLayer = -1;
        private Avatar _calAvatar;   // lead 2026-10-04: the humanoid avatar the head turn was read from
        private ArmourFaceCard _face;   // lead 2026-10-04: "Face in helm"

        private static Transform _container;
        private static int _buildFrame = -1;

        private readonly string _label;
        private int _helmId = -1, _shoulderId = -1, _gender = -1, _race = -1;
        private GameObject _helm, _lShoulder, _rShoulder;
        private bool _helmTried, _shoulderTried;
        private Animator _calFor;
        private Quaternion _qHead = Quaternion.identity;
        private bool _qHeadSet;
        private string _helmPath, _shoulderPaths;
        private float _kHelm, _kShoulder;
        private string _said;

        public ArmourPieces(string label) { _label = label; }

        private static Transform Container(Transform gameRoot)
        {
            if (_container != null) return _container;
            if (gameRoot == null) return null;
            Transform found = gameRoot.Find(ContainerName);
            if (found == null)
            {
                var go = new GameObject(ContainerName);
                found = go.transform;
                found.SetParent(gameRoot, false);
            }
            _container = found;
            return _container;
        }

        /// <summary>The pieces wanted now (0 = none). Builds what changed (one build per frame over all avatars).</summary>
        public void Want(WowFiles files, int helmDisplay, int shoulderDisplay, int race, int gender, Transform gameRoot)
        {
            gender = gender == 1 ? 1 : 0;
            if (helmDisplay != _helmId || gender != _gender || race != _race) { Drop(ref _helm); _helmId = helmDisplay; _helmTried = false; _helmPhoto = false; _helmLayer = -1; }
            if (shoulderDisplay != _shoulderId) { Drop(ref _lShoulder); Drop(ref _rShoulder); _shoulderId = shoulderDisplay; _shoulderTried = false; }
            _gender = gender;
            _race = race;
            if (files == null || _buildFrame == Time.frameCount) return;
            Transform c = Container(gameRoot);
            if (!_helmTried && _helmId > 0)
            {
                _buildFrame = Time.frameCount;
                _helmTried = true;
                _helm = BuildHelm(files, _helmId, race, gender, c, out _helmPath);
                Say();
                return;
            }
            if (!_shoulderTried && _shoulderId > 0)
            {
                _buildFrame = Time.frameCount;
                _shoulderTried = true;
                string l, r;
                _lShoulder = BuildShoulder(files, _shoulderId, 0, c, out l);
                _rShoulder = BuildShoulder(files, _shoulderId, 1, c, out r);
                _shoulderPaths = (l ?? "-") + " / " + (r ?? "-");
                Say();
            }
        }

        private static GameObject BuildHelm(WowFiles files, int display, int race, int gender, Transform parent, out string path)
        {
            path = null;
            DbcTable t = Dbc.Load(files, "ItemDisplayInfo");
            int row = t != null ? t.FindRow(display) : -1;
            if (row < 0) return null;
            string model = t.GetString(row, DbcFields.ItemDisplayInfo.ModelName);
            string tex = t.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture);
            if (string.IsNullOrEmpty(model)) return null;
            string bas = model;
            int dot = bas.LastIndexOf('.');
            if (dot > 0) bas = bas.Substring(0, dot);
            string g = gender == 1 ? "F" : "M";
            string racePrefix = RacePrefix(files, race);
            string[] tries = { "Hu" + g, racePrefix != null ? racePrefix + g : null, "HuM" };
            foreach (string suffix in tries)
            {
                if (suffix == null) continue;
                string p = @"Item\ObjectComponents\Head\" + bas + "_" + suffix + ".m2";
                if (!files.Exists(p)) continue;
                path = p;
                var o = new M2BuildOptions { Name = "Immersive helm " + display };
                if (!string.IsNullOrEmpty(tex)) o.ReplaceablePaths[2] = @"Item\ObjectComponents\Head\" + tex + ".blp";
                GameObject go = M2Factory.Build(files, p, o);
                if (go != null && parent != null) go.transform.SetParent(parent, false);
                return go;
            }
            return null;
        }

        private static GameObject BuildShoulder(WowFiles files, int display, int side, Transform parent, out string path)
        {
            path = null;
            DbcTable t = Dbc.Load(files, "ItemDisplayInfo");
            int row = t != null ? t.FindRow(display) : -1;
            if (row < 0) return null;
            string model = t.GetString(row, DbcFields.ItemDisplayInfo.ModelName + side);
            // An empty right name: the left name with RShoulder_ (ours - harness AvArmourFiles: 8 of 300 random shoulder rows have no
            // ModelName[1], and the RShoulder_ twin of the left file exists for all 8)
            if (side == 1 && string.IsNullOrEmpty(model))
            {
                string left = t.GetString(row, DbcFields.ItemDisplayInfo.ModelName);
                if (!string.IsNullOrEmpty(left) && left.StartsWith("LShoulder_", StringComparison.OrdinalIgnoreCase)) model = "RShoulder_" + left.Substring(10);
            }
            string tex = t.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture + side);
            if (string.IsNullOrEmpty(tex)) tex = t.GetString(row, DbcFields.ItemDisplayInfo.ModelTexture);
            if (string.IsNullOrEmpty(model)) return null;
            string p = @"Item\ObjectComponents\Shoulder\" + M2Model.NormalizeModelPath(model);
            if (!files.Exists(p)) return null;
            path = p;
            var o = new M2BuildOptions { Name = "Immersive shoulder " + (side == 0 ? "L " : "R ") + display };
            if (!string.IsNullOrEmpty(tex)) o.ReplaceablePaths[2] = @"Item\ObjectComponents\Shoulder\" + tex + ".blp";
            GameObject go = M2Factory.Build(files, p, o);
            if (go != null && parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>ChrRaces.ClientPrefix (field 6: Hu, Or, Dw, Ni, Sc, Ta, Gn, Tr, Be, Dr), null when unknown.</summary>
        private static string RacePrefix(WowFiles files, int race)
        {
            DbcTable t = race > 0 ? Dbc.Load(files, "ChrRaces") : null;
            int r = t != null ? t.FindRow(race) : -1;
            return r >= 0 ? t.GetString(r, DbcFields.ChrRaces.ClientPrefix) : null;
        }

        /// <summary>
        /// Every LateUpdate after the poser (the avatar as drawn): the helm on the head, the pads on the shoulders. draw = our-frame
        /// offset (remote); localEye: the local player's eye (the helm hides while the camera is inside it), null for others.
        /// </summary>
        public void Place(Animator avatar, GearBody body, Vector3 draw, float fit, bool hideHelm, bool xrLocal, Vector3? localEye)
        {
            if (avatar == null || body == null || !body.Valid) { Hide(); return; }
            Transform head = avatar.GetBoneTransform(HumanBodyBones.Head);
            Transform lu = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm), ru = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
            // lead 2026-10-04: also a new humanoid avatar under the same Animator (it finished loading) - the T-pose head turn again
            if (avatar != _calFor || avatar.avatar != _calAvatar) { _calFor = avatar; _calAvatar = avatar.avatar; _qHeadSet = false; }
            if (!_qHeadSet && head != null) _qHeadSet = TPoseTurn(avatar, head, out _qHead);
            fit = Mathf.Clamp(fit > 0f ? fit : 1f, 0.5f, 2f);
            bool female = _gender == 1;
            float yd = WowSpace.MetresPerYard;
            Vector3 up = body.Up, right = body.Right, fwd = body.Forward;
            Quaternion torso = Quaternion.LookRotation(fwd, up);
            // helm
            if (_helm != null)
            {
                PieceAdjust ah = Adjust[0];
                bool show = head != null && !hideHelm && ah.Show;
                bool photoOnly = false;
                if (show)
                {
                    // lead fix 2026-10-04 (face-card lab r3/r4: a remote's followed Animator object - the client's own on NonXRMainRoot,
                    // CustomAnimationsHandler.cs:44 - is not at its feet, so the helm came out x0.53 there): the head's height from the
                    // avatar's own T-pose skeleton, the Animator object's height only when the skeleton cannot tell
                    float h;
                    if (!ArmourFaceCard.TPoseHeadHeight(avatar.avatar, head, out h)) h = head.position.y - body.ModelFeet.y;
                    h = Mathf.Max(0.5f, h);
                    _kHelm = h / ((female ? 1.712f : 1.843f) * yd) * fit;
                    Quaternion rot = _qHeadSet ? head.rotation * _qHead : torso;
                    Vector3 hu = rot * Vector3.up, hf = rot * Vector3.forward;
                    Vector3 pos = head.position + hu * ((female ? 0.185f : 0.184f) * yd * _kHelm) + hf * ((female ? 0.085f : 0.063f) * yd * _kHelm);
                    bool eyesInside = xrLocal || (localEye.HasValue && (localEye.Value - pos).magnitude < HelmCameraClear + 0.1f * _kHelm);
                    pos += rot * ah.Offset;   // lead 2026-10-03: the wearer's placement (head frame)
                    if (eyesInside) { if (HelmInPhotos) photoOnly = true; else show = false; }
                    if (show)
                    {
                        _helm.transform.SetPositionAndRotation(pos + draw, rot * Quaternion.AngleAxis(Mathf.Clamp(ah.Tilt, -60f, 60f), Vector3.right));   // lead 2026-10-04: tilt
                        _helm.transform.localScale = Vector3.one * (_kHelm * Mathf.Clamp(ah.Size, 0.5f, 2f));
                    }
                }
                if (_helm.activeSelf != show) _helm.SetActive(show);
                if (show && photoOnly != _helmPhoto) { _helmPhoto = photoOnly; SetHelmLayer(photoOnly); }
            }
            // lead 2026-10-04: "Face in helm" (the wearer's choice: Adjust[0].Flag) - the head hidden on every copy, a snapshot of the face
            // on a card inside the helm (after the helm: same layer, hidden with it)
            {
                // lead fix 2026-10-04: not on the client's culling / loading stand-in (GearBody.OnStandIn) - its own head stays under the helm
                bool faceWant = _helm != null && _helm.activeSelf && Adjust[0].Flag && head != null && !body.OnStandIn;
                if (_face == null && faceWant) _face = new ArmourFaceCard(_label);
                if (_face != null) _face.Tick(avatar, _helm, faceWant, localEye.HasValue || xrLocal, _qHead, _qHeadSet, draw);
            }
            // shoulders
            if (_lShoulder != null || _rShoulder != null)
            {
                bool show = lu != null && ru != null;
                if (show)
                {
                    float span = (ru.position - lu.position).magnitude;
                    _kShoulder = span / ((female ? 0.34f : 0.516f) * yd) * fit;
                    float upOff = (female ? 0.087f : 0.094f) * yd * _kShoulder, inOff = (female ? 0.031f : 0.047f) * yd * _kShoulder;
                    PieceAdjust al = Adjust[1], ar = Adjust[2];   // lead 2026-10-03: the wearer's placement (torso frame)
                    if (_lShoulder != null) { _lShoulder.transform.SetPositionAndRotation(lu.position + up * upOff + right * inOff + torso * al.Offset + draw, torso * Quaternion.AngleAxis(al.Tilt, Vector3.right)); _lShoulder.transform.localScale = Vector3.one * (_kShoulder * Mathf.Clamp(al.Size, 0.5f, 2f)); }
                    if (_rShoulder != null) { _rShoulder.transform.SetPositionAndRotation(ru.position + up * upOff - right * inOff + torso * ar.Offset + draw, torso * Quaternion.AngleAxis(ar.Tilt, Vector3.right)); _rShoulder.transform.localScale = Vector3.one * (_kShoulder * Mathf.Clamp(ar.Size, 0.5f, 2f)); }
                }
                bool showL = show && Adjust[1].Show, showR = show && Adjust[2].Show;
                if (_lShoulder != null && _lShoulder.activeSelf != showL) _lShoulder.SetActive(showL);
                if (_rShoulder != null && _rShoulder.activeSelf != showR) _rShoulder.SetActive(showR);
            }
        }

        /// <summary>Lead (2026-10-04, the fitting view): the renderers of the pieces shown now (helm, shoulders), appended.</summary>
        public void CollectRenderers(List<Renderer> into)
        {
            Add(_helm, into); Add(_lShoulder, into); Add(_rShoulder, into);
            Renderer face = _face != null ? _face.Shown : null;   // "Face in helm": the face card with the helm
            if (face != null) into.Add(face);
        }

        private static void Add(GameObject go, List<Renderer> into)
        {
            if (go == null || !go.activeInHierarchy) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(false)) if (r.enabled) into.Add(r);
        }

        /// <summary>The helm's renderers on the photo / mirror layer (only the Somnium camera and mirrors draw it) or back on their own.</summary>
        private void SetHelmLayer(bool photo)
        {
            if (_helm == null) return;
            if (_helmLayer < 0) _helmLayer = _helm.layer;
            int layer = photo ? PhotoLayer : _helmLayer;
            foreach (Transform t in _helm.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            if (_layerLogs++ < 3) WowLog.Info("Armour: " + _label + " helm " + (photo ? "hidden from your own eyes, kept for the Somnium camera and mirrors (layer " + PhotoLayer + ")" : "drawn normally again") + ".");
        }

        private int _layerLogs;

        /// <summary>The head bone's turn in the avatar's T-pose relative to its root (Avatar.humanDescription.skeleton - Mecanim
        /// avatars face +Z in the T-pose), inverted: head.rotation x this = the facing frame. False when the skeleton cannot tell.</summary>
        private static bool TPoseTurn(Animator avatar, Transform head, out Quaternion q)
        {
            q = Quaternion.identity;
            try
            {
                Avatar av = avatar.avatar;
                if (av == null || !av.isHuman) return false;
                SkeletonBone[] sk = av.humanDescription.skeleton;
                if (sk == null || sk.Length == 0) return false;
                Quaternion r = Quaternion.identity;
                Transform root = avatar.transform;
                for (Transform t = head; t != null && t != root; t = t.parent)
                {
                    int i = Find(sk, t.name);
                    if (i < 0) return false;
                    r = sk[i].rotation * r;
                }
                q = Quaternion.Inverse(r);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static int Find(SkeletonBone[] sk, string name)
        {
            for (int i = 0; i < sk.Length; i++) if (sk[i].name == name) return i;
            return -1;
        }

        private void Say()
        {
            string s = "Armour: " + _label + " helm " + (_helmId > 0 ? _helmId + " -> " + (_helmPath ?? "no model") : "none") + ", shoulders " +
                       (_shoulderId > 0 ? _shoulderId + " -> " + (_shoulderPaths ?? "not built yet") : "none");
            if (s == _said) return;
            _said = s;
            WowLog.Info(s);
        }

        public void Hide()
        {
            if (_face != null) _face.Release();   // the head whole again, the card hidden
            if (_helm != null && _helm.activeSelf) _helm.SetActive(false);
            if (_lShoulder != null && _lShoulder.activeSelf) _lShoulder.SetActive(false);
            if (_rShoulder != null && _rShoulder.activeSelf) _rShoulder.SetActive(false);
        }

        private static void Drop(ref GameObject go)
        {
            // (a rebuilt helm starts on its own layer: SetHelmLayer reads it again)
            if (go != null) UnityEngine.Object.Destroy(go);
            go = null;
        }

        public void Destroy()
        {
            if (_face != null) { _face.Destroy(); _face = null; }
            _helmPhoto = false; _helmLayer = -1;
            Drop(ref _helm);
            Drop(ref _lShoulder);
            Drop(ref _rShoulder);
            _helmId = _shoulderId = -1;
            _helmTried = _shoulderTried = false;
            _calFor = null;
            _qHeadSet = false;
            _said = null;
        }
    }
}
