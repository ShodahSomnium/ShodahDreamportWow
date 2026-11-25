// iteration 11 - owner avatar (proposal-avatar.md 6.F FormView): what one Somnium avatar (the local one or another player's) shows.
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Forms
{
    /// <summary>Where and how a form view draws this frame (filled by GearLocal / GearRemote after their poser ran).</summary>
    public struct FormFrame
    {
        public Vector3 Feet;              // the avatar model's feet, our frame (remote: + the re-map offset)
        public Vector3 Forward;           // the avatar's facing (flattened by the model)
        public bool FirstPerson;          // local only: the camera is at the avatar's head (VR, Somnium's desktop first person)
        public Vector3 Camera;            // local: the eye position
        public float Dt;
        public Transform XrRight, XrLeft; // local VR: the controllers (the hand glows), null on desktop
        public Color HandColour;          // class colour of the glows
    }

    /// <summary>
    /// Per avatar: the look -> FormAvatarHider + FormModel (creature looks) or FormShell (ghost / stealth / shadowform), and in VR two
    /// soft hand glows while the local avatar is hidden (your own hands are part of it; the radial menus and the laser work as before).
    /// A new creature model is built before the old look goes (no flash of the avatar between bear and cat); a model that cannot be
    /// built leaves the avatar as it is (logged once per display). One "Forms:" line per change.
    /// </summary>
    public sealed class FormView
    {
        public const string ContainerName = "WoW Avatar Forms";
        public const float GlowSize = 0.04f;   // ours: the VR hand glows, metres across

        private static Transform _container;

        private readonly string _label;
        private readonly bool _local;
        private readonly FormAvatarHider _hider = new FormAvatarHider();
        private readonly FormShell _shell = new FormShell();
        private FormModel _model, _pending;
        private AvatarLook _shown = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f };
        private int _failedDisplay;
        private VrGlow _glowR, _glowL;
        private string _said;

        public FormView(string label, bool local)
        {
            _label = label;
            _local = local;
        }

        /// <summary>The look drawn now (a creature look only once its model exists).</summary>
        public AvatarLook Shown { get { return _shown; } }
        /// <summary>The creature model drawn now, null otherwise.</summary>
        public FormModel Model { get { return _shown.Kind == AvatarLookKind.Creature && _model != null && _model.Ready ? _model : null; } }

        /// <summary>"WoW Avatar Forms" under the WoW Game object (at its origin; WowOrigin shifts its children - we place every frame).</summary>
        public static Transform Container(Transform gameRoot)
        {
            if (_container != null) return _container;
            if (gameRoot == null) return null;
            Transform found = gameRoot.Find(ContainerName);
            if (found == null)
            {
                var go = new GameObject(ContainerName);
                found = go.transform;
                found.SetParent(gameRoot, false);
                found.localPosition = Vector3.zero;
                found.localRotation = Quaternion.identity;
                found.localScale = Vector3.one;
            }
            _container = found;
            return _container;
        }

        /// <summary>Each LateUpdate while the avatar is drawn here: show want (or the previous look while its model is built).</summary>
        public void Apply(in AvatarLook want, Animator avatar, in FormFrame f, Transform gameRoot, WowFiles files, float priority)
        {
            Transform c = Container(gameRoot);
            AvatarLook next = want;
            if (next.Kind == AvatarLookKind.Creature && (next.DisplayId <= 0 || next.DisplayId == _failedDisplay)) next = Own();
            if (next.Kind != AvatarLookKind.Creature && next.DisplayId != 0) next.DisplayId = 0;
            if (next.Kind == AvatarLookKind.Creature)
            {
                bool showing = _shown.Kind == AvatarLookKind.Creature && _model != null && _model.DisplayId == next.DisplayId && Mathf.Abs(_model.LookScale - Scale(next)) < 0.01f;
                if (!showing)
                {
                    if (_pending == null || _pending.DisplayId != next.DisplayId || Mathf.Abs(_pending.LookScale - Scale(next)) >= 0.01f)
                    {
                        if (_pending != null) _pending.Destroy();
                        _pending = new FormModel(next.DisplayId, Scale(next), _label);
                    }
                    _pending.TryBuild(files, c, priority);
                    if (_pending.Ready)
                    {
                        if (_model != null) _model.Destroy();
                        _model = _pending;
                        _pending = null;
                        _shown = next;
                    }
                    else if (_pending.Failed)
                    {
                        _failedDisplay = next.DisplayId;
                        _pending = null;
                        if (_shown.Kind == AvatarLookKind.Creature) { DropModel(); _shown = Own(); }
                    }
                }
                else _shown = next;   // the same model: SpellId / Form updates only
            }
            else
            {
                if (_pending != null) { _pending.Destroy(); _pending = null; }
                if (_model != null) DropModel();
                _shown = next;
            }

            // What the shown look does to the avatar.
            switch (_shown.Kind)
            {
                case AvatarLookKind.Creature:
                    _hider.Hide(avatar, gameRoot);
                    _shell.Destroy();
                    if (_model != null) _model.Place(f.Feet, f.Forward, _local && f.FirstPerson, f.Camera, f.Dt);
                    break;
                case AvatarLookKind.GhostShell:
                case AvatarLookKind.StealthShell:
                    _hider.Hide(avatar, gameRoot);
                    _shell.Show(_shown.Kind, avatar, _hider.Items, c);
                    break;
                case AvatarLookKind.ShadowTint:
                    _hider.Show();
                    if (avatar != null && _shell.NeedsScan(_shown.Kind)) _hider.FindItems(avatar.transform);
                    _shell.Show(_shown.Kind, avatar, _hider.Items, c);
                    break;
                case AvatarLookKind.Hidden:
                    _hider.Hide(avatar, gameRoot);
                    _shell.Destroy();
                    break;
                default:
                    _hider.Show();
                    _shell.Destroy();
                    break;
            }
            Glows(f);
            Say(f);
        }

        /// <summary>The avatar is not drawn here (another map, the room, far, no body): our parts hide; GearHider owns the avatar.</summary>
        public void Hide()
        {
            _hider.Show();
            _shell.Destroy();
            if (_model != null) _model.Hide();
            ShowGlows(false);
        }

        /// <summary>Back to the avatar's own look at once, everything of ours destroyed (game end, the setting off, the remote gone).</summary>
        public void Clear()
        {
            if (_pending != null) { _pending.Destroy(); _pending = null; }
            DropModel();
            _shell.Destroy();
            _hider.Show();
            ShowGlows(false);
            bool was = _shown.Kind != AvatarLookKind.Own;
            _shown = Own();
            _failedDisplay = 0;
            if (was) { _said = null; WowLog.Info("Forms: " + _label + " back to the own avatar (cleared)"); }
        }

        private void DropModel()
        {
            if (_model != null) _model.Destroy();
            _model = null;
        }

        private static AvatarLook Own() { return new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f }; }
        private static float Scale(in AvatarLook l) { return l.Scale > 0.01f ? l.Scale : 1f; }

        private void Glows(in FormFrame f)
        {
            bool want = _local && _shown.HidesAvatar && f.XrRight != null && f.XrLeft != null;
            if (!want) { ShowGlows(false); return; }
            if (_glowR == null) { _glowR = new VrGlow("WoW form hand glow (right)", 4); _glowL = new VrGlow("WoW form hand glow (left)", 4); Parent(); }
            Color col = new Color(f.HandColour.r, f.HandColour.g, f.HandColour.b, 0.8f);
            _glowR.Set(f.XrRight.position, f.Camera, GlowSize, col);
            _glowL.Set(f.XrLeft.position, f.Camera, GlowSize, col);
        }

        private void Parent()
        {
            Transform c = _container;
            if (c == null) return;
            if (_glowR != null) _glowR.Go.transform.SetParent(c, true);
            if (_glowL != null) _glowL.Go.transform.SetParent(c, true);
        }

        private void ShowGlows(bool on)
        {
            if (_glowR != null) _glowR.Show(on);
            if (_glowL != null) _glowL.Show(on);
        }

        private void Say(in FormFrame f)
        {
            string view = !_local ? "" : (_shown.Kind == AvatarLookKind.Creature ? (f.FirstPerson ? ", view first person (head hidden)" : ", view third person") : "");
            string s = "Forms: " + (_local ? "local look " : _label + " look ") + AvatarLooks.Describe(_shown) +
                       (_shown.HidesAvatar ? " - avatar hidden (" + _hider.Count + " renderers)" : _shown.Kind == AvatarLookKind.ShadowTint ? " - avatar tinted" : " - own avatar") +
                       (_shown.Kind == AvatarLookKind.Creature && _model != null ? ", model display " + _model.DisplayId : "") +
                       (_shown.Kind == AvatarLookKind.GhostShell || _shown.Kind == AvatarLookKind.StealthShell || _shown.Kind == AvatarLookKind.ShadowTint ? ", shell of " + _shell.Count + " skinned meshes" : "") +
                       view + (_local && f.XrRight != null && _shown.HidesAvatar ? ", VR hand glows on" : "");
            // the renderer / shell counts settle within a second: one line per look + view change, not per count
            string key = _shown.Kind + "|" + _shown.DisplayId + "|" + _shown.SpellId + "|" + view;
            if (key == _said) return;
            _said = key;
            WowLog.Info(s);
        }
    }
}
