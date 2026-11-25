using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Another Somnium player's weapons and nameplate, drawn from their slot record (WowNet.Remote): sheathed where WoW hangs
    /// them, or in the hand their record says (their Body.RightHand/LeftHand in VR - the client syncs those - else their
    /// avatar's hand bones), a chop on a desktop player's swings, hidden in cat/bear form, and "Name - Level N Class" over
    /// the head. Built from this client's own game files; without them (not playing here) only the nameplate is shown.
    /// Iteration 4: drawn re-mapped by the floating-origin difference (GearRemap: the client's avatar roots are offset for
    /// rendering only), hidden on another map / in the room / beyond 1.5 km, their flight-path mount under them.
    /// </summary>
    /// <summary>What GearRemote.Place needs to know about this client (WowGear fills it once per frame).</summary>
    public struct GearLocalView
    {
        public bool InWorld;            // a game runs here (WowPlay.Session has a player)
        public int Map;                 // WowSpace.OriginMap (-1 = no origin yet)
        public bool HaveFeet;
        public Vector3 Feet;            // the local avatar's feet (our frame)
        public Transform PlayerRoot, AvatarRoot, GameRoot;   // never moved or hidden
        public Vector3 Eye;             // stage O2: where the local player looks from (glows face it)
    }

    /// <summary>
    /// Iteration 7 (owner Remote, PLAN7 s5.5) - their MOUNT (ridden or a flight path), what other Somnium players see of a rider:
    /// - Stage A: the mount is glued to the rider's synced root as Fusion delivers it (+ the origin offset) by the relative pose
    ///   their client placed (record ints 18-20; never the hips bone, never the shoulders' facing), playing exactly their gait at
    ///   their rate (int 17) through M2Animator.CrossFade. Not said (an older build's taxi, or the moment before their ride owns the
    ///   avatar): the hips over the mount's seat (attachment 0 measured once, 2 frames after the build) with the yaw from the ROOT
    ///   smoothed exp 8/s, and RideGait.Pick from the root's smoothed speed. The model is prepared on the entity art worker first
    ///   (WowEntityViews.PrewarmDisplay; at most one build per frame for all riders; the previous mount stays meanwhile). Mount-state
    ///   changes are applied 0.12 s after they are read (Fusion interpolates the avatar later than the ints arrive). Mounts live
    ///   under "WoW Remote Mounts" (a container child of the WoW Game object, so WowOrigin shifts them) and are placed every
    ///   LateUpdate, never in Rebased; hidden with the avatar (NoBody / OtherMap / Room / Far) and when dead.
    /// - Stage B (the ETS smoothness layer): their stamped root poses (ints 21-27, the writer's origin frame) feed a RideTimeline;
    ///   each frame correction = smooth - (Fusion's root + offset), translation only, minus its own 1 s average (the steady offset
    ///   between the timeline's and Fusion's delays - not jitter), clamped at 3 m (beyond: faded out, the client is trusted,
    ///   logged once), faded over 0.3 s at mount-up / dismount, and handed to GearRemap together with the origin offset - so the
    ///   avatar AND its mount and weapons are drawn on one smooth path. The avatar itself is never modified. Kill switch RideSmoothing.
    /// </summary>
    public sealed partial class GearRemote
    {
        private static readonly int[] Slots = { 15, 16, 17 };

        public readonly GearBody Body = new GearBody();
        public readonly GearPoser Poser = new GearPoser();
        /// <summary>Stage O2 (Sync): their spell casting on their avatar (slot anchors, record-driven kits, the held-spell glow).</summary>
        public readonly GearCast Cast;

        /// <summary>WowSettings.ShowPlayerPlates (WowGear copies it on WowSettings.Changed).</summary>
        public static bool ShowPlates = WowSettings.ShowPlayerPlates;

        /// <summary>Kill switch of stage B (a constant in effect - static readonly so the other branch is not "unreachable code";
        /// PLAN7 D10: on, with the jitter/correction log). False = remote riders are drawn exactly where Fusion puts them.</summary>
        public static readonly bool RideSmoothing = true;

        // Ours (PLAN7 s4.8), one place, logged once by ConstantsLine:
        public const float StateDelay = 0.12f;          // s: a mount-state change is applied this long after it is read
        public const float CorrectionClamp = 3f;        // m: a larger stage-B correction is not applied (the client is trusted)
        public const float CorrectionFade = 0.3f;       // s: the correction fades in at mount-up / out at dismount
        public const float FallbackYawRate = 8f;        // 1/s: exp smoothing of the root's yaw for the fallback anchor
        public const float FallbackSpeedRate = 8f;      // 1/s: exp smoothing of the root's speed for the fallback gait
        public const float ArtWaitSeconds = 10f;        // s: a mount the art worker has not prepared by then is built anyway
        public const float PoseLostSeconds = 0.5f;      // s without a stage-B sample -> the correction fades out, the timeline is cleared
        public const float BiasSeconds = 1f;            // s: the slow average of the correction that is left out (the two delays' difference)

        private readonly GearItem[] _items = new GearItem[3];
        private readonly GearNameplate _plate;
        private readonly Transform _parent;
        private WowNet.Remote _r;

        /// <summary>Their record as last ticked (null before the first Tick).</summary>
        public WowNet.Remote Record { get { return _r; } }
        private int _lastCounter = -1;
        private float _chopT = -1f;
        private string _classLabel;
        private string _line, _lineName;
        private int _lineLevel = -1, _lineClass = -1, _lineRace = -1;
        private int _lastMotion = -1;
        private string _raceLabel;
        private int _raceFor = -1;
        private int _classFor = -1;

        public GearRemote(Transform parent)
        {
            _parent = parent;
            _plate = new GearNameplate(parent);
            Cast = new GearCast(parent);
        }

        /// <summary>The ours-constants of the remote rider view (WowGear logs it once at start).</summary>
        public static string ConstantsLine()
        {
            var t = new RideTimeline();
            return "Gear: remote riders (ours) - mount state applied " + StateDelay.ToString("0.00") + " s after it is read, stage-B ride smoothing " + (RideSmoothing ? "on" : "OFF") +
                   " (kill switch GearRemote.RideSmoothing; RideTimeline delay " + t.MinDelay.ToString("0.00") + "-" + t.MaxDelay.ToString("0.00") + " s, extrapolation " +
                   t.MaxExtrapolation.ToString("0.00") + " s), correction clamp " + CorrectionClamp.ToString("0") + " m, fade " + CorrectionFade.ToString("0.0") + " s, fallback yaw/speed exp " +
                   FallbackYawRate.ToString("0") + "/s, delay offset average " + BiasSeconds.ToString("0.0") + " s, art wait " + ArtWaitSeconds.ToString("0") + " s";
        }

        public void Tick(WowNet.Remote r, WowFiles files)
        {
            _r = r;
            Body.Player = r.Player;
            Body.Label = "'" + r.Name + "' (player " + r.PlayerId + ")";
            _files = files;          // iteration 11 (avatar): forms, puppet, armour (GearRemote.Avatar.cs)
            EnsureAvatarParts();
            WowGearState g = r.Gear;
            for (int i = 0; i < 3; i++)
            {
                WowWireItem w = i == 0 ? g.Main : i == 1 ? g.Off : g.Ranged;
                GearItem cur = _items[i];
                if (cur != null && cur.Item.SameAs(w))
                {
                    cur.Ready();
                    // iteration 11 (spellfx hook 7): their weapon-enchant glow (int 44), applied when it changes (GearItem keeps it over a build)
                    int ench = i == 0 ? g.EnchantMain : i == 1 ? g.EnchantOff : g.EnchantRanged;
                    if (ench != _enchantShown[i]) { _enchantShown[i] = ench; cur.SetEnchantVisual(ench); }
                    continue;
                }
                if (cur != null) cur.Destroy();
                _items[i] = w.Empty || files == null ? null : new GearItem(files, w, Slots[i], "player " + r.PlayerId);
                _enchantShown[i] = -1;   // iteration 11: the new model gets its glow on the next Tick
            }
            if (g.AttackCounter != _lastCounter)
            {
                if (_lastCounter >= 0 && !g.Xr && g.Sheath == (int)SheathState.Melee) _chopT = 0f;
                _lastCounter = g.AttackCounter;
            }
            if (_chopT >= 0f) { _chopT += Time.deltaTime; if (_chopT > 0.34f) _chopT = -1f; }
            // Their avatar's motions (desktop players only; a VR avatar is moved by its own IK).
            Poser.Label = Body.Label;
            if (g.MotionCounter != _lastMotion)
            {
                if (_lastMotion >= 0 && g.Motion != WowMotion.None && !g.Xr) Poser.Play(g.Motion, Time.time);
                _lastMotion = g.MotionCounter;
            }
            Poser.SetHold(g.Xr ? WowHold.None : g.Hold);

            if (_classFor != g.Class) { _classFor = g.Class; _classLabel = ClassName(files, g.Class); }
            if (_raceFor != g.Race) { _raceFor = g.Race; _raceLabel = g.Race > 0 ? WowRaces.Name(files, g.Race) : null; }
            if (_line == null || _lineName != r.Name || _lineLevel != g.Level || _lineClass != g.Class || _lineRace != g.Race)
            {
                _lineName = r.Name;
                _lineLevel = g.Level;
                _lineClass = g.Class;
                _lineRace = g.Race;
                _line = r.Name + " - Level " + g.Level + " " + (string.IsNullOrEmpty(_raceLabel) ? "" : _raceLabel + " ") + _classLabel;
            }
            _plate.Set(_line, GearNameplate.ClassColour(g.Class), g.HealthPercent, g.Dead);
            TickMount(g, files);
            Cast.Tick(r, Body.Label);
        }

        /// <summary>Where this player is drawn, filled by Place (WowParty, the log): the offset their frame -> ours, why
        /// (not) shown, metres from the local player after re-mapping (float.MaxValue when not comparable).</summary>
        public Vector3 Offset { get; private set; }
        public GearSeen Seen { get; private set; } = GearSeen.NoBody;
        public float Distance { get; private set; } = float.MaxValue;

        /// <summary>Iteration 7 (WowGear.TryRiderAnchor, the hidden WoW character's click box online): 1 = drawn here this frame at
        /// AnchorFeet (the mount's feet when mounted, else the avatar's feet, our frame), 2 = hidden (another map, the room, far, no
        /// body), 0 = not placed yet.</summary>
        public int AnchorState { get; private set; }
        public Vector3 AnchorFeet { get; private set; }
        private bool _feetMismatchSaid;   // lead fix: the root / avatar-model disagreement is logged once
        public float AnchorSeatHeight { get; private set; }   // m from AnchorFeet up to the rider's root (0 on foot)
        public float AnchorYaw { get; private set; }

        /// <summary>True while a ridden mount (not only a flight path) is drawn under them.</summary>
        public bool Riding { get { return _mount != null && _mDisplay > 0 && _mRide.Kind != 0 && _mRide.Kind != 3; } }

        private readonly GearHider _hider = new GearHider();
        private GearSeen _saidSeen = (GearSeen)(-1);
        private Transform _saidPr, _saidAr;

        /// <summary>
        /// LateUpdate (order 10000, the client's IK done): re-map (GearRemap.Decide + the 1.5 km rule), hide or show the
        /// client's renderers of this avatar, queue its roots for the render-time offset (+ the stage-B ride correction), then pose
        /// it and place the weapons, plate and mount at the re-mapped spot (everything read from the body is in THEIR frame; + draw
        /// = ours).
        /// </summary>
        public void Place(in GearLocalView v)
        {
            Poser.Begin();
            // (Body.OverrideRoot = GearBody's render-lab hook; null in the world, where a body needs its Bridge player.)
            if (_r == null || (_r.Player == null && Body.OverrideRoot == null) || !Body.Update())
            {
                HideOwn();
                Cast.Hide();
                HideAvatarExtras();   // iteration 11
                _hider.Show();
                Poser.Release();
                Seen = GearSeen.NoBody;
                Distance = float.MaxValue;
                Offset = Vector3.zero;
                RideHidden();
                return;
            }
            WowGearState g = _r.Gear;
            Vector3 off;
            GearSeen why;
            bool shown = GearRemap.Decide(v.InWorld, v.Map, g, out off, out why);
            // iteration 11 (world hook): inside a dungeon / raid only the players of our instance are drawn
            string instWhy;
            if (shown && why == GearSeen.Shown && !WowInstanceGate.Allows(v, g, out instWhy)) { shown = false; why = GearSeen.OtherInstance; }
            Vector3 offRecord = off;   // the record's own cell (stage-B samples are in that frame); Stitch may hold an older one
            off = Stitch(g, why, off);
            float dist = float.MaxValue;
            if (shown && v.HaveFeet)
            {
                // Lead fix: measure from the avatar model (Body.Root was seen parked at the login room while the avatar stood in
                // the world); both are logged once when they disagree by more than 50 m, so the next Player.log shows which is real.
                Vector3 remoteFeet = Body.ModelFeet;
                if (!_feetMismatchSaid && (Body.Feet - remoteFeet).sqrMagnitude > 50f * 50f)
                {
                    _feetMismatchSaid = true;
                    WowLog.Warn("Gear: remote body root and avatar model disagree for " + Body.Label + ": root " + Body.Feet.ToString("F0") + ", avatar model "
                        + remoteFeet.ToString("F0") + ", our feet " + v.Feet.ToString("F0") + ", offset " + off.ToString("F0") + " - distance taken from the avatar model");
                }
                dist = (remoteFeet + off - v.Feet).magnitude;
                if (dist > (Seen == GearSeen.Far ? GearRemap.ShowWithin : GearRemap.HideBeyond)) { shown = false; why = GearSeen.Far; }
            }
            Seen = why;
            Distance = dist;
            Offset = shown ? off : Vector3.zero;
            Transform pr = Movable(Body.PlayerRoot, v), ar = Movable(Body.AvatarRoot, v);
            Said(why, dist, pr, ar);
            if (!shown)
            {
                HideAvatarExtras();   // iteration 11: our form hider gives its renderers back first, then GearHider hides them all
                _hider.Hide(pr, ar);
                HideOwn();
                Cast.Hide();
                Poser.Apply(Body, false, Time.time, Time.deltaTime);
                SayWow(false, true);
                RideHidden();
                return;
            }
            _hider.Show();
            // Stage B: only when the whole avatar can be moved with it (its root and its model), so rider and mount stay together.
            bool canCorrect = pr != null && (ar != null || Body.AvatarRoot == null);
            Vector3 draw = off + RideCorrection(off, offRecord, canCorrect);
            GearRemap.Request(pr, draw);
            if (ar != pr) GearRemap.Request(ar, draw);

            // Iteration 11 (avatar): their look (a creature hides the weapons as WoW does; an unsaid look keeps the old cat/bear rule)
            // and the WoW full-body animation of their desktop avatar (fix 2026-10-04: the WEARER's style, int 32 bit 28 - was the viewer's).
            _look = LookOf(g);
            bool hidden = _look.HidesWeapons || (_look.Kind == AvatarLookKind.Own && (g.Form == 1 || g.Form == 5));
            bool desktop = !g.Xr && Body.XrRight == null;
            GearWowPose wp = WowPose(g, desktop);
            if (Poser.Apply(Body, (desktop || wp.LegsOnly) && !g.Dead && !_look.HidesAvatar, Time.time, Time.deltaTime, wp)) Body.Update();   // lead 2026-10-26: VR swim legs
            SayWow(wp.Enabled, false);
            int right = g.Dead ? 0 : SlotOf(g.Right), left = g.Dead ? 0 : SlotOf(g.Left);
            for (int i = 0; i < 3; i++)
            {
                GearItem it = _items[i];
                if (it == null || it.Go == null) continue;
                bool inRight = right == it.Slot, inLeft = left == it.Slot;
                bool show = !hidden && (inRight || inLeft || it.Spot != GearSpot.Hidden);
                if (it.Go.activeSelf != show) it.Go.SetActive(show);
                if (!show) continue;
                Vector3 pos; Quaternion rot;
                if (inRight || inLeft)
                {
                    Vector3 hp; Quaternion hr;
                    Body.HandPose(inRight, out hp, out hr);
                    if (_chopT >= 0f && Body.XrRight == null && !Poser.Posed) hr = hr * Quaternion.AngleAxis(Chop(_chopT / 0.34f), Vector3.right);
                    GearPlace.Held(it, hp, hr, out pos, out rot);
                }
                else GearPlace.Sheathed(Body, it, out pos, out rot);
                it.T.SetPositionAndRotation(pos + draw, rot);
            }
            PlaceMount(draw, off);
            Cast.Place(Body, draw, v.Eye, g.Dead);
            PlaceAvatarExtras(g, draw, v);   // iteration 11: form / shell, armour (after Cast.Place: a creature's own spell points win)
            _plate.Place(PlatePoint(Body.HeadPos + Vector3.up * (0.5f * Body.Scale) + draw), ShowPlates);
        }

        // ------------------------------------------------------------------ their rebases
        // When they rebase, their avatar jumps by -delta in their frame and their record's cell changes - two network objects:
        // the record's ints are read as they arrive, the NetworkTransform position is rendered interpolated (later). Until
        // both happened the previous picture is held (at most StitchSeconds), so nobody sees them flash a cell away.

        private const float StitchJump = 100f, StitchSeconds = 1.5f;
        private enum StitchWait { None, Jump, Cell }
        private StitchWait _stitch;
        private Vector3 _stitchOff, _lastOff, _lastRaw;
        private float _stitchUntil;
        private bool _haveLast;
        private int _lastMap, _lastCx, _lastCy, _lastCz;

        /// <summary>WowSpace.Rebased: OUR origin moved by delta, so every offset to them shrinks by delta (a held one too). The
        /// mount under "WoW Remote Mounts" is shifted by WowOrigin and re-placed from the body next LateUpdate; the drawn-path
        /// histories (jitter, fallback speed) are in our frame and restart.</summary>
        public void OnLocalRebased(Vector3 delta)
        {
            _stitchOff -= delta;
            _lastOff -= delta;
            _jitHave = 0;
        }

        private Vector3 Stitch(in WowGearState g, GearSeen why, Vector3 off)
        {
            Vector3 raw = Body.Feet;
            bool mapped = why == GearSeen.Shown;
            if (!mapped || !_haveLast || g.OriginMap != _lastMap)
            {
                _stitch = StitchWait.None;
            }
            else
            {
                bool cellChanged = g.CellX != _lastCx || g.CellY != _lastCy || g.CellZ != _lastCz;
                Vector3 jump = raw - _lastRaw;
                bool bigJump = jump.sqrMagnitude > StitchJump * StitchJump;
                float now = Time.unscaledTime;
                switch (_stitch)
                {
                    case StitchWait.None:
                        if (cellChanged && !bigJump) { _stitch = StitchWait.Jump; _stitchOff = _lastOff; _stitchUntil = now + StitchSeconds; }
                        else if (bigJump && !cellChanged) { _stitch = StitchWait.Cell; _stitchOff = _lastOff - jump; _stitchUntil = now + StitchSeconds; }
                        break;
                    case StitchWait.Jump:
                        if (bigJump || now > _stitchUntil) _stitch = StitchWait.None;   // the position caught up: the new offset fits
                        break;
                    case StitchWait.Cell:
                        if (cellChanged || now > _stitchUntil) _stitch = StitchWait.None;
                        else if (bigJump) _stitchOff -= jump;
                        break;
                }
                if (_stitch != StitchWait.None) off = _stitchOff;
            }
            _haveLast = mapped;
            _lastRaw = raw;
            _lastOff = off;
            _lastMap = g.OriginMap; _lastCx = g.CellX; _lastCy = g.CellY; _lastCz = g.CellZ;
            return off;
        }

        /// <summary>A root of this player's we may move/hide: never the local player's, never an ancestor of theirs or of the game.</summary>
        private static Transform Movable(Transform t, in GearLocalView v)
        {
            if (t == null) return null;
            if (t == v.PlayerRoot || t == v.AvatarRoot) return null;
            if (v.PlayerRoot != null && v.PlayerRoot.IsChildOf(t)) return null;
            if (v.AvatarRoot != null && v.AvatarRoot.IsChildOf(t)) return null;
            if (v.GameRoot != null && v.GameRoot.IsChildOf(t)) return null;
            return t;
        }

        private void Said(GearSeen why, float dist, Transform pr, Transform ar)
        {
            if (why == _saidSeen && ReferenceEquals(pr, _saidPr) && ReferenceEquals(ar, _saidAr)) return;
            _saidPr = pr;
            _saidSeen = why;
            _saidAr = ar;
            WowGearState g = _r.Gear;
            WowLog.Info("Gear: " + Body.Label + " " + (why == GearSeen.Shown || why == GearSeen.Unmapped ? "drawn" : "hidden") + " (" + why +
                        (dist < float.MaxValue ? ", " + dist.ToString("0") + " m" : "") + "; their origin " + (g.OriginSaid ? "map " + g.OriginMap + " cell (" + g.CellX + ", " + g.CellY + ", " + g.CellZ + ")" : "not said") +
                        ", ours map " + WowSpace.OriginMap + " cell (" + WowSpace.OriginCellX + ", " + WowSpace.OriginCellY + ", " + WowSpace.OriginCellZ + "); roots player '" +
                        (pr != null ? pr.name : "none/not movable") + "', avatar '" + (ar != null ? (ar == pr ? "(same)" : ar.name) : "none/not movable") + "')");
        }

        private void HideOwn()
        {
            for (int i = 0; i < 3; i++) if (_items[i] != null && _items[i].Go != null && _items[i].Go.activeSelf) _items[i].Go.SetActive(false);
            if (_mount != null && _mount.activeSelf) _mount.SetActive(false);
            _plate.Place(Vector3.zero, false);
        }

        // ------------------------------------------------------------------ mount (iteration 7: ridden mounts; flight paths)

        private const string ContainerName = "WoW Remote Mounts";
        private static Transform _container;
        private static int _buildFrame = -1;

        private GameObject _mount;
        private M2Animator _mountAnim;
        private Transform _mountSeat;
        private RideGaitModel _gaitModel;
        private Vector3 _seatLocal;          // attachment 0 in the mount's own space (measured once per gait animation, for the fallback only)
        private int _seatMeasureFrame = -1;
        private bool _seatFixed;
        // Lead fix 2026-10-04 (the user, with a screenshot: "remote players float above griffons when riding"): a taxi says no relative
        // pose (WowFlight writes no MountSnapshot: Player.log "anchor fallback hips"), so the fallback puts attachment 0 under their hips -
        // but it was measured once, 2 frames after the build, in the Stand animation (Gryphon: about the bind pose's 1.43 m), and the
        // griffon then flies in its Fly animation, where the rider's own client measures that seat at 0.41 m (Player.log: "Flight: seat
        // measured on the animated mount at (-0.06, 0.41, 0.04) (bind pose (0.00, 1.43, 0.21))") - the griffon hung a metre under the
        // rider. Now the seat is measured again SeatSettleFrames after every change of the gait animation (kept per animation) and the
        // drawn seat eases to it over SeatEaseSeconds (no pop). Values ours.
        private const int SeatSettleFrames = 2;
        private const float SeatEaseSeconds = 0.25f;
        private int _seatAnim = int.MinValue, _seatLogs;
        private float _seatMeasureAt;
        private Vector3 _seatShown;
        private bool _seatShownHave;
        private readonly System.Collections.Generic.Dictionary<int, Vector3> _seatByAnim = new System.Collections.Generic.Dictionary<int, Vector3>();
        private float _mountTop;
        private int _mountId;                // the display built (or tried) now
        private int _prepId;
        private float _prepAt;
        private int _gaitPlaying = -1;
        private bool _anchorSaid;
        private int _anchorKey, _anchorRelKey;

        // The mount state as read (queued) and as applied StateDelay later.
        private struct MountWant { public float At; public int Display; public RideRecord Ride; public bool Taxi; }
        private readonly MountWant[] _wants = new MountWant[16];
        private int _wantCount;
        private MountWant _wantLast;
        private bool _haveWant;
        private int _mDisplay;
        private RideRecord _mRide;
        private bool _mTaxi;
        private int _mCounterSaid = -1;

        // Stage B.
        private readonly RideTimeline _timeline = new RideTimeline();
        private float _poseSeenAt = -100f;
        private float _corrW;
        private Vector3 _lastCorr;
        private bool _clampSaid;
        private int _snapsSeen;

        // Fallback gait: the root's smoothed velocity (their frame).
        private Vector3 _fbVel, _fbLastRoot;
        private bool _fbHaveRoot, _fbHaveYaw;
        private float _fbYaw;

        /// <summary>The "WoW Remote Mounts" container under the WoW Game object (at (0,0,0): WowOrigin shifts its children).</summary>
        private Transform Container()
        {
            if (_container != null) return _container;
            if (_parent == null) return null;
            Transform found = _parent.Find(ContainerName);
            if (found == null)
            {
                var go = new GameObject(ContainerName);
                found = go.transform;
                found.SetParent(_parent, false);
                found.localPosition = Vector3.zero;
                found.localRotation = Quaternion.identity;
                found.localScale = Vector3.one;
            }
            _container = found;
            return _container;
        }

        /// <summary>Tick (Update): queue the record's mount state, apply what is StateDelay old, feed stage B, prepare/build the model.</summary>
        private void TickMount(in WowGearState g, WowFiles files)
        {
            float now = Time.unscaledTime;
            RideRecord state = g.Ride;
            state.PoseSaid = false; state.PoseStampMs = 0; state.Seq = 0;
            state.Pos = default(RideVec); state.Vel = default(RideVec); state.Yaw = 0f;
            var w = new MountWant { At = now, Display = g.MountDisplayId, Ride = state, Taxi = g.OnTaxi };
            if (!_haveWant || !SameWant(w, _wantLast))
            {
                if (_wantCount == _wants.Length) { ApplyWant(_wants[0]); Shift(1); }
                _wants[_wantCount++] = w;
                _wantLast = w;
                _haveWant = true;
            }
            int k = -1;
            for (int i = 0; i < _wantCount; i++) if (now - _wants[i].At >= StateDelay) k = i;
            if (k >= 0) { ApplyWant(_wants[k]); Shift(k + 1); }

            // Stage B samples go in as they arrive (the timeline puts them on the writer's clock).
            if (g.Ride.Kind != 0 && g.Ride.PoseSaid)
            {
                _poseSeenAt = now;
                var s = new RideSample { Seq = g.Ride.Seq, StampMs = g.Ride.PoseStampMs, Epoch = g.Ride.Epoch, Position = g.Ride.Pos, Velocity = g.Ride.Vel, Yaw = g.Ride.Yaw };
                _timeline.Add(s, Time.realtimeSinceStartupAsDouble);
            }
            else if (now - _poseSeenAt > PoseLostSeconds && _timeline.Count > 0 && _corrW <= 0f) _timeline.Clear();

            int want = files == null ? 0 : _mDisplay;
            if (want == _mountId) return;
            if (want <= 0)
            {
                if (_mount != null) WowLog.Info("Gear: " + Body.Label + " dismounted (display " + _mountId + " removed)");
                DropMount();
                return;
            }
            if (Seen == GearSeen.OtherMap || Seen == GearSeen.Room || Seen == GearSeen.Far || Seen == GearSeen.OtherInstance) return;   // not drawn here: built when they are
            if (_prepId != want) { _prepId = want; _prepAt = now; }
            float prio = Distance < float.MaxValue ? Distance * Distance : 100f;
            bool ready = WowEntityViews.PrewarmDisplay(want, prio);
            bool late = now - _prepAt > ArtWaitSeconds;
            if ((!ready && !late) || _buildFrame == Time.frameCount) return;   // the previous mount (if any) stays meanwhile
            _buildFrame = Time.frameCount;
            BuildMount(want, files, ready);
        }

        private void Shift(int n)
        {
            if (n <= 0) return;
            for (int i = n; i < _wantCount; i++) _wants[i - n] = _wants[i];
            _wantCount -= n;
        }

        private void ApplyWant(in MountWant w)
        {
            _mDisplay = w.Display;
            _mRide = w.Ride;
            _mTaxi = w.Taxi;
            if (w.Display > 0 && w.Ride.Kind != 0 && w.Ride.MountUpCounter != _mCounterSaid)
            {
                if (_mCounterSaid >= 0 && w.Display == _mountId) WowLog.Info("Gear: " + Body.Label + " mounted again (mount-up " + w.Ride.MountUpCounter + ", display " + w.Display + ")");
                _mCounterSaid = w.Ride.MountUpCounter;
            }
        }

        private static bool SameWant(in MountWant a, in MountWant b)
        {
            RideRecord x = a.Ride, y = b.Ride;
            return a.Display == b.Display && a.Taxi == b.Taxi && x.Kind == y.Kind && x.Airborne == y.Airborne && x.Swimming == y.Swimming &&
                   x.GaitAnim == y.GaitAnim && x.GaitRate == y.GaitRate && x.MountUpCounter == y.MountUpCounter && x.Epoch == y.Epoch &&
                   x.RelativeSaid == y.RelativeSaid && x.RelYawDeg == y.RelYawDeg && x.RelPos.X == y.RelPos.X && x.RelPos.Y == y.RelPos.Y && x.RelPos.Z == y.RelPos.Z;
        }

        private void BuildMount(int displayId, WowFiles files, bool prepared)
        {
            GameObject go = null;
            try { go = WowCreatures.Build(files, displayId); }
            catch (System.Exception e) { WowLog.Warn("Gear: mount display " + displayId + " of " + Body.Label + " threw: " + e.Message); }
            DropMount();
            _mountId = displayId;
            if (go == null) { WowLog.Warn("Gear: no model for mount display " + displayId + " of " + Body.Label); return; }
            _mount = go;
            go.name = "Remote mount " + displayId + " of player " + (_r != null ? _r.PlayerId : -1);
            Transform c = Container();
            if (c != null) go.transform.SetParent(c, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _mountAnim = go.GetComponent<M2Animator>();
            _mountSeat = _mountAnim != null ? _mountAnim.Attachment(0) : null;   // WMV wmv07_enums.h: "ATT_LEFT_WRIST = 0, // Mountpoint"
            _gaitModel = GearRideGait.Of(_mountAnim, go.transform);
            _gaitPlaying = -1;
            if (_mountAnim != null) _gaitPlaying = GearRideGait.Apply(_mountAnim, -1, RideGait.Stand, 1f);
            // Without a mount point: the rider's hips at 80% of the model's height (bounds measured once, upright at the origin).
            _mountTop = 1.5f;
            bool any = false;
            Bounds b = default(Bounds);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any && b.max.y - go.transform.position.y > 0.2f) _mountTop = b.max.y - go.transform.position.y;
            _seatLocal = _mountSeat != null ? go.transform.InverseTransformPoint(_mountSeat.position) : new Vector3(0f, 0.8f * _mountTop / Mathf.Max(0.01f, go.transform.localScale.y), 0f);
            _seatFixed = _mountSeat == null;
            _seatMeasureFrame = Time.frameCount + 2;
            _seatByAnim.Clear();   // lead fix 2026-10-04
            _seatAnim = _gaitPlaying;
            _seatMeasureAt = 0f;
            _seatShownHave = false;
            _anchorSaid = false;
            WowLog.Info("Gear: " + Body.Label + " mount display " + displayId + " built" + (prepared ? " (ready: prepared by the entity art worker, or no worker running)" : " (art worker not ready after " + ArtWaitSeconds.ToString("0") + " s: built anyway)") +
                        ", " + (_mountSeat != null ? "mount point 0" : "no mount point: seat at 80% of " + _mountTop.ToString("0.0") + " m") + ", gait model run " + _gaitModel.RunMoveSpeed.ToString("0.00") +
                        " yd/s, scale " + _gaitModel.Scale.ToString("0.00") + (_gaitModel.HasFly ? ", Fly" : "") + (_gaitModel.HasHover ? ", Hover" : ""));
        }

        /// <summary>
        /// Place: the mount under the rider. Relative pose said: root (as delivered, + draw) x the record's relative pose - exactly
        /// where the rider's client drew it. Else the fallback: the mount's seat (measured once) under their hips, the root's yaw
        /// smoothed. Then the gait, the anchor for the online click box, the log line and the jitter measurement.
        /// </summary>
        private void PlaceMount(Vector3 draw, Vector3 off)
        {
            Transform root = Body.Root;
            float rootYaw = YawOf(root);
            Vector3 rootRaw = root != null ? root.position : Body.Feet;
            Measure(rootRaw, off, draw);
            if (_mount == null)
            {
                SetAnchor(Body.Feet + draw, 0f, rootYaw);
                return;
            }
            WowGearState g = _r.Gear;
            bool show = _mDisplay > 0 && !g.Dead;
            if (_mount.activeSelf != show) _mount.SetActive(show);
            if (!show)
            {
                SetAnchor(Body.Feet + draw, 0f, rootYaw);
                return;
            }
            Transform t = _mount.transform;
            float dt = Time.deltaTime;
            Vector3 rootDrawn = rootRaw + draw;
            bool relative = _mRide.Kind != 0 && _mRide.RelativeSaid;
            Vector3 pos;
            Quaternion rot;
            if (relative)
            {
                Quaternion ry = Quaternion.Euler(0f, rootYaw, 0f);
                pos = rootDrawn + ry * new Vector3(_mRide.RelPos.X, _mRide.RelPos.Y, _mRide.RelPos.Z);
                rot = Quaternion.Euler(0f, rootYaw + _mRide.RelYawDeg, 0f);
                _fbHaveYaw = false;
            }
            else
            {
                if (!_fbHaveYaw) { _fbYaw = rootYaw; _fbHaveYaw = true; }
                else _fbYaw = Mathf.LerpAngle(_fbYaw, rootYaw, 1f - Mathf.Exp(-dt * FallbackYawRate));
                rot = Quaternion.Euler(0f, _fbYaw, 0f);
                Vector3 hips = (Body.Humanoid ? Body.Hips : Body.Feet + Vector3.up * (0.95f * Body.Scale)) + draw;
                t.rotation = rot;
                // lead fix 2026-10-04: the seat of the animation playing, eased (see _seatByAnim)
                if (!_seatShownHave) { _seatShown = _seatLocal; _seatShownHave = true; }
                else _seatShown = Vector3.Lerp(_seatShown, _seatLocal, 1f - Mathf.Exp(-dt / SeatEaseSeconds));
                pos = hips - (t.TransformPoint(_seatShown) - t.position);
            }
            t.SetPositionAndRotation(pos, rot);
            MeasureSeatOnce(t);

            int anim;
            float rate;
            if (_mRide.Kind != 0) { anim = _mRide.GaitAnim; rate = _mRide.GaitRate; }
            else
            {
                Vector3 heading = rot * Vector3.forward;
                Vector3 planar = new Vector3(_fbVel.x, 0f, _fbVel.z);
                float forward = planar.magnitude * (Vector3.Dot(planar, heading) < 0f ? -1f : 1f);
                RideMode mode = _mTaxi ? RideMode.Flying : RideMode.Ground;
                // The rider's run speed is not on an older record: WoW's base run speed (AC playerBaseMoveSpeed 7 yd/s) splits walk / run.
                RideGaitPick pick = RideGait.Pick(mode, forward, 0f, _fbVel.y, MountCatalog.BaseRun * WowSpace.MetresPerYard, false, false, _gaitModel, _gaitPlaying);
                anim = pick.AnimId;
                rate = pick.Rate;
            }
            _gaitPlaying = GearRideGait.Apply(_mountAnim, _gaitPlaying, anim, rate);
            if (_gaitPlaying != _seatAnim && _mountSeat != null)
            {
                // lead fix 2026-10-04: the seat of this animation - known, or measured once it has posed the mount
                _seatAnim = _gaitPlaying;
                Vector3 known;
                if (_seatByAnim.TryGetValue(_seatAnim, out known)) { _seatLocal = known; _seatFixed = true; }
                else
                {
                    // after the cross-fade (GearRideGait.Apply -> M2Animator.CrossFade with the sequence's BlendTime, real time)
                    _seatFixed = false;
                    _seatMeasureFrame = Time.frameCount + SeatSettleFrames;
                    _seatMeasureAt = Time.unscaledTime + (_mountAnim != null ? _mountAnim.BlendTimeMs(_seatAnim) : 0) / 1000f + 0.05f;
                }
            }
            SetAnchor(pos, Mathf.Max(0f, rootDrawn.y - pos.y), rot.eulerAngles.y);

            // One line per change of display / kind / anchor mode / relative pose (to the centimetre) - no string work otherwise.
            int kindCode = _mRide.Kind != 0 ? _mRide.Kind : (_mTaxi ? 7 : 0);
            int key = _mountId * 31 + kindCode * 7 + (relative ? 1 : 0);
            int relKey = relative ? Mathf.RoundToInt(_mRide.RelPos.X * 100f) * 73856093 ^ Mathf.RoundToInt(_mRide.RelPos.Y * 100f) * 19349663 ^ Mathf.RoundToInt(_mRide.RelPos.Z * 100f) * 83492791 : 0;
            if (!_anchorSaid || key != _anchorKey || relKey != _anchorRelKey)
            {
                _anchorSaid = true;
                _anchorKey = key;
                _anchorRelKey = relKey;
                string kind = _mRide.Kind == 1 ? "ground" : _mRide.Kind == 2 ? "flying" : _mRide.Kind == 3 || _mTaxi ? "taxi" : "not said";
                WowLog.Info("Gear: " + Body.Label + " mounted display " + _mountId + " (" + kind + "), gait " + anim + " x " + rate.ToString("0.00") + ", anchor " +
                            (relative ? "relative (" + _mRide.RelPos.X.ToString("0.000") + ", " + _mRide.RelPos.Y.ToString("0.000") + ", " + _mRide.RelPos.Z.ToString("0.000") + ", " + _mRide.RelYawDeg.ToString("0.0") + ")" : "fallback hips"));
            }
        }

        /// <summary>The seat (attachment 0, mount space) measured ONCE on the animated model 2 frames after the build (FlightRide's
        /// rule: never follow the bobbing mount point). Only the fallback anchor uses it.</summary>
        private void MeasureSeatOnce(Transform t)
        {
            if (_seatFixed || _mountSeat == null || Time.frameCount < _seatMeasureFrame || Time.unscaledTime < _seatMeasureAt) return;
            _seatFixed = true;
            Vector3 was = _seatLocal;
            _seatLocal = t.InverseTransformPoint(_mountSeat.position);
            _seatByAnim[_seatAnim] = _seatLocal;   // lead fix 2026-10-04
            if (_seatLogs++ < 12)
                WowLog.Info("Gear: " + Body.Label + " mount " + _mountId + " seat measured in animation " + _seatAnim + " at " + _seatLocal.ToString("F2") + " (mount space; was " + was.ToString("F2") + ")");
        }

        private void SetAnchor(Vector3 feet, float seat, float yaw)
        {
            AnchorState = 1;
            AnchorFeet = feet;
            AnchorSeatHeight = seat;
            AnchorYaw = yaw;
        }

        /// <summary>Not drawn this frame: the online click box of their WoW character hides too; stage B fades out.</summary>
        private void RideHidden()
        {
            AnchorState = 2;
            _jitHave = 0;
            _fbHaveRoot = false;
            _corrW = 0f;
            _lastCorr = Vector3.zero;
        }

        /// <summary>The yaw of Body.Root (degrees, heading only).</summary>
        private float YawOf(Transform root)
        {
            Vector3 f = root != null ? root.forward : Body.Forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Body.Forward;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        private void DropMount()
        {
            if (_mount != null) Object.Destroy(_mount);
            _mount = null;
            _mountAnim = null;
            _mountSeat = null;
            _mountId = 0;
            _gaitPlaying = -1;
            _seatFixed = false;
            _seatByAnim.Clear();   // lead fix 2026-10-04
            _seatAnim = int.MinValue;
            _seatShownHave = false;
            _anchorSaid = false;
        }

        // ------------------------------------------------------------------ stage B (ETS timeline) and the measurements

        /// <summary>The stage-B render correction this frame (translation, our frame): smooth - (Fusion's root + offset).</summary>
        private Vector3 RideCorrection(Vector3 off, Vector3 offRecord, bool can)
        {
            float dt = Time.deltaTime;
            Transform root = Body.Root;
            bool live = RideSmoothing && can && root != null && _mDisplay > 0 && _mRide.Kind != 0 && Time.unscaledTime - _poseSeenAt <= PoseLostSeconds && _timeline.Count > 0;
            RideVec sp;
            float sy;
            if (live && _timeline.Sample(Time.realtimeSinceStartupAsDouble, out sp, out sy))
            {
                Vector3 smooth = new Vector3(sp.X, sp.Y, sp.Z) + offRecord;
                Vector3 c = smooth - (root.position + off);
                if (_timeline.Snaps != _snapsSeen) { _statSnaps += _timeline.Snaps - _snapsSeen; _snapsSeen = _timeline.Snaps; }
                // The timeline and Fusion's own interpolation draw the rider a different beat behind: at speed that is a steady
                // offset along the path (0.05 s x 26 m/s = 1.3 m), not jitter. It is followed by a slow average (BiasSeconds) and
                // left out, so stage B removes the jitter while the rider stays where Fusion puts them on average; the average moves
                // slowly, so the drawn path stays smooth, and a 3 m clamp on the rest cannot be reached by a delay difference.
                if (!_biasHave || _timeline.WasSnap) { _bias = c; _biasHave = true; }
                else _bias = Vector3.Lerp(_bias, c, 1f - Mathf.Exp(-dt / BiasSeconds));
                _statBiasSum += _bias.magnitude;
                Vector3 j = c - _bias;
                bool tooBig = j.sqrMagnitude > CorrectionClamp * CorrectionClamp;
                if (tooBig)
                {
                    if (!_clampSaid)
                    {
                        _clampSaid = true;
                        WowLog.Info("Gear: ride smoothing of " + Body.Label + " - correction " + j.magnitude.ToString("0.0") + " m is beyond " + CorrectionClamp.ToString("0") +
                                    " m: faded out, drawn where the client puts them (logged once per player)");
                    }
                    _statClamped++;
                }
                else _lastCorr = j;
                _corrW = Mathf.MoveTowards(_corrW, tooBig ? 0f : 1f, dt / CorrectionFade);
                Vector3 applied = _lastCorr * _corrW;
                float m = applied.magnitude;
                _statLagSum += _timeline.Delay;
                _statCorrSum += m;
                _statN++;
                if (m > _statCorrMax) _statCorrMax = m;
                return applied;
            }
            _biasHave = false;
            _corrW = Mathf.MoveTowards(_corrW, 0f, dt / CorrectionFade);
            return _lastCorr * _corrW;
        }

        private Vector3 _bias;
        private bool _biasHave;

        // Ride smoothing stats over all riders (WowGear logs them every 120 s).
        private static double _statLagSum, _statCorrSum, _statBiasSum;
        private static int _statN, _statSnaps, _statClamped;
        private static float _statCorrMax;

        /// <summary>"Gear: ride smoothing - ..." (and resets); null when no rider was smoothed since the last line.</summary>
        public static string RideStatsLine(int riders)
        {
            if (_statN == 0 && riders == 0) return null;
            string s = "Gear: ride smoothing - " + riders + " rider(s), " + (RideSmoothing ? "" : "OFF (kill switch), ") + "lag avg " + (_statN > 0 ? (_statLagSum / _statN).ToString("0.000") : "-") +
                       " s, correction avg " + (_statN > 0 ? (_statCorrSum / _statN).ToString("0.000") : "-") + " m max " + _statCorrMax.ToString("0.000") + " m (steady delay offset left out avg " +
                       (_statN > 0 ? (_statBiasSum / _statN).ToString("0.000") : "-") + " m), snaps " + _statSnaps +
                       ", clamped frames " + _statClamped + " (" + _statN + " frames)";
            _statLagSum = _statCorrSum = _statBiasSum = 0.0;
            _statN = _statSnaps = _statClamped = 0;
            _statCorrMax = 0f;
            return s;
        }

        // Jitter: the second difference of the drawn root path per frame (the next point predicted from the last two, dt-scaled).
        private Vector3 _jitRaw1, _jitRaw2, _jitDrawn1, _jitDrawn2;
        private float _jitDt1;
        private int _jitHave;
        private double _jitRawSum, _jitDrawnSum, _jitSpeedSum;
        private float _jitRawMax, _jitDrawnMax;
        private int _jitN;

        /// <summary>Per frame while a mount is drawn: the fallback gait's root speed (their frame) and the jitter of the root path as
        /// Fusion delivers it (+ offset) and as drawn (+ the stage-B correction).</summary>
        private void Measure(Vector3 rootRaw, Vector3 off, Vector3 draw)
        {
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;
            // fallback speed (their frame; a jump faster than 60 m/s is a teleport / their rebase, not a speed)
            if (_fbHaveRoot)
            {
                Vector3 v = (rootRaw - _fbLastRoot) / dt;
                if (v.sqrMagnitude < 3600f) _fbVel = Vector3.Lerp(_fbVel, v, 1f - Mathf.Exp(-dt * FallbackSpeedRate));
            }
            _fbLastRoot = rootRaw;
            _fbHaveRoot = true;

            if (_mount == null || !_mount.activeSelf) { _jitHave = 0; return; }
            Vector3 raw = rootRaw + off, drawn = rootRaw + draw;
            if (_jitHave >= 2 && _jitDt1 > 1e-5f)
            {
                float k = dt / _jitDt1;
                Vector3 pr = _jitRaw1 + (_jitRaw1 - _jitRaw2) * k, pd = _jitDrawn1 + (_jitDrawn1 - _jitDrawn2) * k;
                float nr = (raw - pr).magnitude, nd = (drawn - pd).magnitude;
                if (nr < 5f && nd < 5f)   // larger = a snap / rebase, not jitter
                {
                    _jitRawSum += nr; _jitDrawnSum += nd; _jitN++;
                    _jitSpeedSum += (drawn - _jitDrawn1).magnitude / dt;
                    if (nr > _jitRawMax) _jitRawMax = nr;
                    if (nd > _jitDrawnMax) _jitDrawnMax = nd;
                }
            }
            _jitRaw2 = _jitRaw1; _jitRaw1 = raw;
            _jitDrawn2 = _jitDrawn1; _jitDrawn1 = drawn;
            _jitDt1 = dt;
            if (_jitHave < 2) _jitHave++;
        }

        /// <summary>"Gear: ride jitter 'Name': ..." (and resets); null without riding frames since the last line.</summary>
        public string JitterLine()
        {
            if (_jitN == 0) return null;
            string s = "Gear: ride jitter " + Body.Label + ": root step noise avg " + (_jitRawSum / _jitN).ToString("0.000") + " m, max " + _jitRawMax.ToString("0.000") + " m at " +
                       (_jitSpeedSum / _jitN).ToString("0.0") + " m/s as Fusion delivers it; drawn (stage B " + (RideSmoothing ? "on" : "off") + ") avg " + (_jitDrawnSum / _jitN).ToString("0.000") +
                       " m, max " + _jitDrawnMax.ToString("0.000") + " m (" + _jitN + " frames)";
            _jitRawSum = _jitDrawnSum = _jitSpeedSum = 0.0;
            _jitRawMax = _jitDrawnMax = 0f;
            _jitN = 0;
            return s;
        }

        private static int SlotOf(WowHandHolds h)
        {
            return h == WowHandHolds.MainHand ? 15 : h == WowHandHolds.OffHand ? 16 : h == WowHandHolds.Ranged ? 17 : 0;
        }

        private static float Chop(float t)
        {
            if (t < 0.35f) return Mathf.SmoothStep(0f, -50f, t / 0.35f);
            if (t < 0.7f) return Mathf.SmoothStep(-50f, 60f, (t - 0.35f) / 0.35f);
            return Mathf.SmoothStep(60f, 0f, (t - 0.7f) / 0.3f);
        }

        /// <summary>The class name from the player's own ChrClasses.dbc (Name_lang), else the enum name.</summary>
        public static string ClassName(WowFiles files, int cls)
        {
            try
            {
                var t = files != null ? Dbc.Load(files, "ChrClasses") : null;
                int row = t != null ? t.FindRow(cls) : -1;
                if (row >= 0)
                {
                    string n = t.GetLocalizedString(row, DbcFields.ChrClasses.Name_lang);
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            catch (System.Exception) { /* fall back below */ }
            return cls == 6 ? "Death Knight" : ((WowClass)cls).ToString();
        }

        public void Destroy()
        {
            Poser.Release();
            for (int i = 0; i < 3; i++) { if (_items[i] != null) _items[i].Destroy(); _items[i] = null; }
            _plate.Destroy();
            DropMount();
            Cast.Destroy();
            DestroyAvatarExtras();   // iteration 11
            _hider.Show();
            AnchorState = 2;
        }
    }
}
