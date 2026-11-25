using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Flight
{
    /// <summary>
    /// One flight: the avatar seated on the taxi mount, carried along the stitched TaxiPathNode route at TaxiFlightSpeed.
    /// Iteration 7 (Ride, priority 2 / decision D11): the avatar is taken over by the mounts' seat (Mounts/MountSeat, the same ten Bridge
    /// members as before - Q3Player / EtsTruckSeat: motion collision/fly/teleport off, speed/gravity/jump 0, falling animation off, VRIK
    /// legs off, the CharacterController disabled, the sitting pose held every frame), which adds ETS's pose-entry undo (VR: the head and
    /// hand offsets' turn taken out), PlaceHead (VR: the headset on the eye point, lean clamped 0.45 m), the measured hips (the root at
    /// the saddle minus the avatar's own hip height instead of the fixed 0.45 m) and the no-drop release (the root written on the ground
    /// spot while the controller is still off). On a monitor the ride's chase camera (Mounts/MountCamera) follows the gryphon from the
    /// client camera's own pose. The spline, the speeds and the streaming are unchanged; the root is written in LateUpdate - heading
    /// only, never pitch or roll. Everything is given back in Release, then the avatar is put on the ground with the official teleport.
    /// The route is kept in WoW coordinates (doubles) and converted with the CURRENT origin every frame, so a rebase mid-flight
    /// needs no path shift; Rebased re-applies the pose at once so no frame shows the old frame's position.
    /// </summary>
    public sealed class FlightRide
    {
        /// <summary>AzerothCore worldserver.conf.dist "TaxiFlightSpeed = 32.0" (yards per second).</summary>
        public const double TaxiSpeed = 32.0;
        /// <summary>How far ahead of the mount the world is asked to stream (seconds of flight).</summary>
        public const double StreamAheadSeconds = 8.0;
        /// <summary>The sitting pose's hips above its root (the avatar's feet): the root goes this far below the saddle.</summary>
        public const float SeatDrop = 0.45f;

        private const int AnimFly = 135;       // AnimationData.dbc 135 = Fly (the taxi gryphon's 135 has MoveSpeed 30)
        private const float EaseSeconds = 1.5f; // comfort: speed ramps in / out over the first / last seconds

        public enum Phase { None, FadingOut, Flying, WaitingForGround, FadingIn, Done }

        public Phase State { get; private set; }
        public int Map { get; private set; }
        public double Travelled { get { return _s; } }
        public double Length { get { return _spline != null ? _spline.Length : 0; } }
        public Vector3 StreamFocus { get; private set; }
        public string Destination = "";
        /// <summary>Iteration 11 (motion 6.2.8): online = the server spline's duration (DurationMs / 1000); the ride then keeps that
        /// time exactly (ForcedMoveMath.EasedDistance) and ends at it. 0 = offline: 32 yd/s with the ease, unchanged.</summary>
        public double ServerSeconds;
        /// <summary>Seconds of the server's spline already gone when the ride began (publish age + the deferral).</summary>
        public double ServerAge;
        /// <summary>The spline ends at a map edge (a two-map route): fade to black over the last HopFadeSeconds and release in the air.</summary>
        public bool HopAtEnd;
        private const float HopFadeSeconds = 0.4f;   // ours

        private FlightSpline _spline;
        private double _s;
        private float _time, _yaw, _phaseTime;
        private bool _yawKnown, _fast;
        private GameObject _mount;
        private M2Animator _anim;
        private Vector3 _seatLocal;
        private Transform _root;
        private MountSeat _seat;
        /// <summary>The desktop chase camera (WowFlight passes the ride's MountCamera); null = the client's own camera follows.</summary>
        public MountCamera Camera;
        private FlightFade _fade;
        private double _endX, _endY, _endZ;
        private int _logTick;

        /// <summary>Starts the ride. False (and nothing taken over) when there is no avatar or no route.</summary>
        public bool Begin(List<FlightPoint> route, WowPos from, int mountDisplayId, WowFiles files, bool fastTravel, out string why)
        {
            why = null;
            if (route == null || route.Count < 2) { why = "the route has " + (route != null ? route.Count : 0) + " points"; return false; }
            _root = FindAvatar();
            if (_root == null) { why = "no avatar to carry"; return false; }
            Map = route[0].Map;

            var xs = new List<double>(route.Count + 1); var ys = new List<double>(route.Count + 1); var zs = new List<double>(route.Count + 1);
            // As AzerothCore's flight spline: the player's own position is the first point.
            double ddx = from.X - route[0].X, ddy = from.Y - route[0].Y;
            if (ddx * ddx + ddy * ddy > 2.0 * 2.0) { xs.Add(from.X); ys.Add(from.Y); zs.Add(from.Z); }
            for (int i = 0; i < route.Count; i++) { xs.Add(route[i].X); ys.Add(route[i].Y); zs.Add(route[i].Z); }
            _spline = new FlightSpline(xs, ys, zs);
            _spline.At(_spline.Length, out _endX, out _endY, out _endZ);
            _s = 0; _time = 0f; _yawKnown = false;
            _fast = fastTravel;

            TakeOver();
            if (!_fast) BuildMount(mountDisplayId, files);
            State = _fast ? Phase.FadingOut : Phase.Flying;
            _phaseTime = 0f;
            if (_fast) { _fade = new FlightFade(); _fade.Set(0f); }
            bool chase = !_fast && Camera != null && !WowPlayer.XrPresent;
            if (chase) Camera.Begin();
            WowLog.Info("Flight: " + (_fast ? "fast travel" : "flying") + " to " + Destination + ": " + _spline.PointCount + " points, " + _spline.Length.ToString("0") +
                        " yd, " + (_spline.Length / TaxiSpeed).ToString("0") + " s at " + TaxiSpeed + " yd/s; mount display " + mountDisplayId +
                        (_mount != null ? "" : " (none)") + "; avatar '" + _root.name + "', controller " + (_seat.CcFound ? "disabled" : "not found") + "; seat = the mounts' MountSeat" +
                        (WowPlayer.XrPresent ? " (VR: pose-entry undo, PlaceHead)" : chase ? ", desktop chase camera" : "") + ".");
            if (!_fast) ApplyPose();
            return true;
        }

        /// <summary>LateUpdate: moves along the route (or runs the fast-travel fade). Returns true once the ride has ended.</summary>
        public bool Tick(float dt, WowMapView map)
        {
            if (State == Phase.None || State == Phase.Done) return true;
            if (_root == null) { WowLog.Warn("Flight: the avatar disappeared mid-flight."); return true; }
            _time += dt;
            _phaseTime += dt;
            switch (State)
            {
                case Phase.Flying when ServerSeconds > 0:
                {
                    // Online: the server's spline timing (its duration, a comfort ease that keeps it - ForcedMoveMath.EasedDistance).
                    double t = _time + ServerAge;
                    _s = SomniumSpace.Worlds.SomniumWoW.Play.Motion.ForcedMoveMath.EasedDistance(_spline.Length, ServerSeconds, t);
                    ApplyPose();
                    Stream(map);
                    if (HopAtEnd && t >= ServerSeconds - HopFadeSeconds)
                    {
                        if (_fade == null) { _fade = new FlightFade(); WowLog.Info("Flight: fading out at the map edge."); }
                        _fade.Set(Mathf.Clamp01((float)((t - (ServerSeconds - HopFadeSeconds)) / HopFadeSeconds)));
                    }
                    if (++_logTick % 900 == 0) WowLog.Info("Flight: " + (_s / _spline.Length * 100.0).ToString("0") + "% (" + t.ToString("0") + " / " + ServerSeconds.ToString("0") + " s of the server's flight).");
                    return t >= ServerSeconds;
                }
                case Phase.Flying:
                {
                    double remaining = _spline.Length - _s;
                    double v = TaxiSpeed;
                    if (_time < EaseSeconds) v *= 0.25 + 0.75 * (_time / EaseSeconds);
                    double easeDist = TaxiSpeed * EaseSeconds * 0.5;
                    if (remaining < easeDist) v *= Math.Max(0.2, remaining / easeDist);
                    _s = Math.Min(_spline.Length, _s + v * dt);
                    ApplyPose();
                    Stream(map);
                    if (++_logTick % 900 == 0) WowLog.Info("Flight: " + (_s / _spline.Length * 100.0).ToString("0") + "% (" + _s.ToString("0") + " / " + _spline.Length.ToString("0") + " yd).");
                    if (_s >= _spline.Length - 0.01) return true;
                    return false;
                }
                case Phase.FadingOut:
                    _fade.Set(Mathf.Clamp01(_phaseTime / 0.5f));
                    // The avatar stays where it stands (controller off, gravity 0) until the view is black.
                    if (_phaseTime >= 0.5f)
                    {
                        _s = _spline.Length;
                        State = Phase.WaitingForGround;
                        _phaseTime = 0f;
                        WowLog.Info("Flight: fast travel - faded out, waiting for the world at the landing point.");
                    }
                    return false;
                case Phase.WaitingForGround:
                {
                    _fade.Set(1f);
                    ApplyPose();
                    Stream(map);
                    Vector3 end = ToUnity(_endX, _endY, _endZ);
                    bool ready = map != null && map.IsReadyAround(end, 60f);
                    if (ready || _phaseTime > 20f)
                    {
                        if (!ready) WowLog.Warn("Flight: fast travel - the landing point was not ready after 20 s; landing anyway.");
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        /// <summary>After a rebase: the path is in WoW space, so only the objects need rewriting in the new frame (at once).</summary>
        public void OnRebased(Vector3 delta)
        {
            StreamFocus -= delta;
            if (State == Phase.Flying || State == Phase.WaitingForGround) ApplyPose();
        }

        /// <summary>The fast-travel fade (black), handed over after Release so the caller can fade back in; null when none.</summary>
        public FlightFade DetachFade()
        {
            FlightFade f = _fade;
            _fade = null;
            return f;
        }

        /// <summary>The landing point in WoW coordinates.</summary>
        public WowPos End { get { return new WowPos((float)_endX, (float)_endY, (float)_endZ, WowSpace.YawToOrientation(_yaw)); } }

        /// <summary>
        /// Ends the ride: gives the avatar back and puts it on the ground under at (Unity; the route's end normally, the current
        /// point when the flight was cut short). Returns the Unity landing point.
        /// </summary>
        public Vector3 Release(WowMapView map, bool atEnd)
        {
            double x, y, z;
            if (atEnd) { x = _endX; y = _endY; z = _endZ; } else _spline.At(_s, out x, out y, out z);
            Vector3 p = ToUnity(x, y, z);
            float gy;
            string ground = "route height";
            if (atEnd && HopAtEnd) ground = "in the air at the map edge (the server's far teleport follows)";   // iteration 11 (motion 6.2.8): no ground snap
            else if (map != null && map.TryFloorHeight(p, 4f, out gy)) { p.y = gy + 0.05f; ground = "floor"; }
            else if (map != null && map.TryGroundHeight(p, out gy)) { p.y = gy + 0.05f; ground = "terrain"; }

            if (_mount != null) { UnityEngine.Object.Destroy(_mount); _mount = null; _anim = null; }
            if (Camera != null) Camera.End("the flight landed");
            // MountSeat: the root written on the spot while the controller is still off (no drop), the pose left FIRST, motion /
            // VRIK / falling animation / the controller given back in the ETS mirror order, then the official teleport.
            string how = "none";
            bool ccFound = _seat != null && _seat.CcFound;
            if (_seat != null) _seat.Release(p, _yaw, true, out how);
            // The fast-travel fade stays black: WowFlight takes it (DetachFade) and fades back in.
            State = Phase.Done;
            WowLog.Info("Flight: landed at " + p.ToString("F1") + " (" + ground + ", " + how + ", no-drop write) after " + _time.ToString("0") + " s; controller " +
                        (ccFound ? "restored" : "none") + ", pose and motion given back.");
            _seat = null;
            return p;
        }

        // ------------------------------------------------------------------------------------------------ internals

        private static Transform FindAvatar()
        {
            Transform root = WowPlayer.Root;
            if (root != null) return root;
            // Editor / lab without the client: the SDK test harness's (or the lab's) CharacterController, as WowPlayer does.
            if (Application.isEditor && !WowPlayer.HasBridgePlayer)
            {
                CharacterController cc = UnityEngine.Object.FindFirstObjectByType<CharacterController>();
                if (cc != null) return cc.transform;
            }
            return null;
        }

        private void TakeOver()
        {
            _seat = new MountSeat();
            _seat.Tag = "Flight";
            string why;
            if (!_seat.TakeOver(_root, out why)) WowLog.Warn("Flight: the seat could not take the avatar (" + why + ").");
        }

        private void BuildMount(int displayId, WowFiles files)
        {
            if (displayId <= 0 || files == null) return;
            try { _mount = WowCreatures.Build(files, displayId); }
            catch (Exception e) { WowLog.Warn("Flight: WowCreatures.Build(" + displayId + ") threw " + e.Message); _mount = null; }
            if (_mount == null) return;
            _mount.name = "WoW Taxi Mount " + displayId;
            _anim = _mount.GetComponent<M2Animator>();
            if (_anim != null) _anim.Play(AnimFly, true);
            // The rider sits on attachment 0 (Gryphon.m2: bone 53 at 0.22, 0, 1.57 yd; Wyvern.m2: bone 47 at 0.18, 0, 1.19 yd).
            // Until the animator has posed the bones (its first LateUpdate) the attachment transforms are not where the model
            // draws them (lab: 0.32 m instead of ~1.4 m), so the bind-pose position from the file comes first and the animated
            // one is measured once, a frame later, in the mount's own space - fixed from then on, so the seat does not bob
            // with the wing beats (VR comfort).
            _seatLocal = new Vector3(0f, 1.4f, 0f);
            string src = "fallback";
            M2Model model = _anim != null ? _anim.Model : null;
            if (model != null)
            {
                for (int i = 0; i < model.Attachments.Length; i++)
                {
                    if (model.Attachments[i] == null || model.Attachments[i].Id != 0) continue;
                    M2Vec3 a = model.Attachments[i].Position;
                    _seatLocal = WowSpace.PointToUnity(a.X, a.Y, a.Z);
                    src = "attachment 0 (bind pose)";
                    break;
                }
            }
            _seatMeasureFrame = Time.frameCount + 2;
            _seatMeasured = _anim == null || _anim.Attachment(0) == null;
            WowLog.Info("Flight: mount " + displayId + " built, seat " + src + " at " + _seatLocal.ToString("F2") + " (mount space).");
        }

        private int _seatMeasureFrame;
        private bool _seatMeasured;

        private void MeasureSeat()
        {
            if (_seatMeasured || _mount == null || _anim == null || Time.frameCount < _seatMeasureFrame) return;
            _seatMeasured = true;
            Transform att = _anim.Attachment(0);
            if (att == null) return;
            Vector3 was = _seatLocal;
            _seatLocal = _mount.transform.InverseTransformPoint(att.position);
            WowLog.Info("Flight: seat measured on the animated mount at " + _seatLocal.ToString("F2") + " (bind pose " + was.ToString("F2") + ").");
        }

        private void ApplyPose()
        {
            double x, y, z;
            _spline.At(_s, out x, out y, out z);
            Vector3 p = ToUnity(x, y, z);
            double hx, hy;
            _spline.Heading(_s, out hx, out hy);
            Vector3 dir = WowSpace.DirToUnity((float)hx, (float)hy, 0f);
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (!_yawKnown) { _yaw = want; _yawKnown = true; }
            else _yaw = Mathf.LerpAngle(_yaw, want, 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
            Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 seat;
            if (_mount != null)
            {
                _mount.transform.SetPositionAndRotation(p, rot);
                MeasureSeat();
                seat = _mount.transform.TransformPoint(_seatLocal);
                _camSeat = seat;
                // lead fix 2026-10-05 (the user, desktop, a screenshot of the rider floating over the flapping griffon: Gryphon.m2's
                // saddle moves -0.06 .. 0.42 m in Fly 135, harness TaxiSeat, the fixed seat was one sample near the top): desktop riders
                // sit on the live saddle every frame; VR keeps the fixed seat (comfort). The chase camera keeps the fixed one (no bob).
                Transform att = _anim != null ? _anim.Attachment(0) : null;
                if (!WowPlayer.XrPresent && _seatMeasured && att != null) seat = att.position;
            }
            else { seat = p + Vector3.up * 1.4f; _camSeat = seat; }
            // The mounts' seat: SittingStraight held FIRST (VR: the pose entry's re-centring undone), the root at the saddle minus the
            // measured hips (SeatDrop until measured), VR PlaceHead onto the eye point - heading only.
            bool vr = WowPlayer.XrPresent;
            _lastSeat = seat;
            _haveSeat = true;
            // lead fix 2026-10-04: Update (-500) writes the root at the saddle for everything later in the frame; LateSeat holds the pose
            if (_seat != null) _seat.WriteRoot(seat, _yaw, vr);
            else _root.SetPositionAndRotation(seat - Vector3.up * SeatDrop, rot);
        }

        private Vector3 _lastSeat, _camSeat;
        private bool _haveSeat;

        /// <summary>Lead fix 2026-10-04: LateUpdate (-500) - the mounts' seat on this frame's saddle (SittingStraight held first, the root
        /// again, VR PlaceHead) and the desktop chase camera's target.</summary>
        public void LateSeat()
        {
            if (!_haveSeat || _root == null) return;
            bool vr = WowPlayer.XrPresent;
            if (_seat != null) _seat.Seat(_lastSeat, _yaw, vr);
            if (!vr && Camera != null && Camera.On) Camera.SetTarget(_camSeat, _yaw, 0f);   // lead fix 2026-10-05: the steady seat
            WriteSnapshot();
        }

        /// <summary>
        /// Lead fix 2026-10-04 (the user, with a screenshot: "remote players float above griffons when riding"): a flight said nothing in the
        /// ride record (only WowMount wrote a MountSnapshot), so viewers placed the griffon by the hips fallback with its seat measured in
        /// the Stand animation - and Gryphon.m2's saddle is 1.79 .. 2.28 m up in Stand but -0.06 .. 0.42 m in Fly 135 (harness TaxiSeat),
        /// so the rider hung ~1.8 m over the flying griffon. Now the flight says what WowMount says for a ridden mount (WowMount.WriteSnapshot):
        /// Kind 3 = taxi (RideWire int 17), gait Fly at the animator's speed, and the drawn griffon relative to the SYNCED root
        /// (MountSeat.SyncedRoot: Body.Root + the flat controller-centre offset the client's PlayerTransformNetworkDriver adds) - so every
        /// viewer draws it exactly where this client draws it. No stage-B pose (PoseStampMs 0): the flight is the server's spline.
        /// Valid for the frame it was written in and the next (SnapshotFrame) - an ended or dropped flight says nothing.
        /// </summary>
        public static MountSnapshot Snapshot { get; private set; }
        public static int SnapshotFrame { get; private set; } = -10;

        /// <summary>This frame's (or the previous frame's) flight snapshot, false when no flight wrote one.</summary>
        public static bool TryGetSnapshot(out MountSnapshot s)
        {
            s = Snapshot;
            return Time.frameCount - SnapshotFrame <= 1;
        }

        private void WriteSnapshot()
        {
            if (_seat == null || _seat.Root == null || _mount == null) return;
            IWowSession session = WowPlay.Session;
            PlayerState p = session != null ? session.Player : null;
            float rootYaw = _seat.Root.eulerAngles.y;
            MountSnapshot sn = default(MountSnapshot);
            sn.Kind = 3;
            sn.Flying = true;
            sn.GaitAnim = AnimFly;
            sn.GaitRate = _anim != null && _anim.Speed > 0.01f ? _anim.Speed : 1f;
            sn.MountUpCounter = p != null ? (p.MountCounter & 15) : 0;
            sn.RelativeSaid = true;
            sn.MountInRoot = Quaternion.Inverse(Quaternion.Euler(0f, rootYaw, 0f)) * (_mount.transform.position - _seat.SyncedRoot);
            sn.MountYawInRoot = Mathf.DeltaAngle(rootYaw, _mount.transform.eulerAngles.y);
            Snapshot = sn;
            SnapshotFrame = Time.frameCount;
        }

        private void Stream(WowMapView map)
        {
            double x, y, z;
            _spline.At(Math.Min(_spline.Length, _s + TaxiSpeed * StreamAheadSeconds), out x, out y, out z);
            StreamFocus = ToUnity(x, y, z);
            if (map != null)
            {
                try { map.SetFocus(StreamFocus); }
                catch (Exception) { }
            }
        }

        /// <summary>WoW yards (double) -> Unity metres in the current origin frame, without float rounding of the absolute position.</summary>
        public static Vector3 ToUnity(double x, double y, double z)
        {
            WowSpace.Frame f = WowSpace.Current;
            const double K = WowSpace.MetresPerYard;
            return new Vector3((float)(-y * K - f.Unity.x), (float)(z * K - f.Unity.y), (float)(x * K - f.Unity.z));
        }
    }

    /// <summary>A black fade over the view: a screen overlay on a monitor, a quad just in front of the headset's camera in VR.</summary>
    public sealed class FlightFade
    {
        private GameObject _go;
        private UnityEngine.UI.RawImage _img;
        private bool _vr;

        public void Set(float alpha)
        {
            bool vr = WowPlayer.XrPresent;
            if (_go != null && vr != _vr) Destroy();
            if (_go == null) Build(vr);
            if (_vr)
            {
                Camera head = WowPlayer.Head();
                if (head != null && _go.transform.parent != head.transform)
                {
                    _go.transform.SetParent(head.transform, false);
                    _go.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0.05f, head.nearClipPlane * 1.5f));
                    _go.transform.localRotation = Quaternion.identity;
                }
            }
            _img.color = new Color(0f, 0f, 0f, alpha);
        }

        public void Destroy()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _img = null;
        }

        private void Build(bool vr)
        {
            _vr = vr;
            _go = new GameObject("WoW Flight Fade", typeof(RectTransform));
            var canvas = _go.AddComponent<Canvas>();
            if (vr)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                var r = (RectTransform)_go.transform;
                r.sizeDelta = new Vector2(100f, 100f);
                r.localScale = Vector3.one * 0.01f; // 1 x 1 m right in front of the eyes
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            canvas.sortingOrder = 32000;
            var go = new GameObject("Black", typeof(RectTransform));
            go.transform.SetParent(_go.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _img = go.AddComponent<UnityEngine.UI.RawImage>();
            _img.raycastTarget = false;
            if (vr) _img.material = Ui.UiArt.OverUi;
        }
    }
}
