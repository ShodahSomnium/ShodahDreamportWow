// iteration 11 - owner motion (proposal-motion.md 6.2.7): drives the Somnium avatar for a ForcedMoveView (charge, intercept, feral charge,
// death grip and other pulls, knockbacks, Disengage, fear / confuse wander, a charmer's moves), the VR comfort overlay (decision D-M1),
// the locomotion locks (FeetLocked for WowRunSpeed; no jump while locked), the slow-fall governor, the water-walk floor, the haptic
// ticks on lost / regained control, and the desktop Gait the avatar area poses the legs with. Added by WowGame (hook 7).
using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using SomniumSpace.Worlds.SomniumWoW.Game.Flight;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.Motion;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Motion;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    [DefaultExecutionOrder(-450)]
    public sealed class WowForcedMove : MonoBehaviour
    {
        /// <summary>A forced move (or a held transport arrival) writes the avatar root: no fall rescue, no ride.</summary>
        public static bool OwnsAvatar { get; private set; }
        /// <summary>Rooted / stunned / control lost / a forced move: the avatar does not walk (WowRunSpeed freezes the on-foot speed).</summary>
        public static bool FeetLocked { get; private set; }
        /// <summary>The desktop legs' AnimationData id during a forced move (5 Run, 4 Walk, 37-40 jump, 14 Stun), 0 = none.</summary>
        public static int Gait { get; private set; }

        // AnimationData.dbc ids (measured with DbcRows, proposal 6.2.7)
        private const int AnimWalk = 4, AnimRun = 5, AnimStun = 14, AnimJumpStart = 37, AnimJump = 38, AnimJumpEnd = 39, AnimFall = 40;
        private const float MetresPerYard = 0.9144f;
        // ours (logged once)
        private const float FloorSnap = 1.5f;              // m: the path's height snaps to a floor this close (hides height noise)
        private const float YawRate = 12f;                 // desktop heading smoothing (/s, as WowEntityView)
        private const float DimAlpha = 0.75f, WanderAlpha = 0.5f, FadeOutSeconds = 0.25f, BlinkInSeconds = 0.1f;   // D-M1
        private const float HapticStrength = 0.3f, HapticSeconds = 0.08f;
        private const float KnockbackHardStop = 10f;       // s
        private const float SlowFallCap = 7f * MetresPerYard;   // m/s: terminalSafefallVelocity (MovementUtil.cpp:29)
        private const float SlowFallResume = 5.5f;         // m/s: gravity comes back below this descent (hysteresis)
        private const float WaterFloorAbove = 0.5f;        // m: the water-walk floor exists while the feet are this close above the surface
        private const float JumpStartSeconds = 0.15f, JumpEndSeconds = 0.2f;

        private AvatarHold _hold;
        private ForcedMoveView _move;
        private int _moveId = -1;
        private float _t0, _yaw;
        private bool _active, _vr, _subscribed, _logged, _settingsLive;
        private Vector3 _kbLast;
        private bool _kbWallHit;
        private double _kbWallX, _kbWallY;
        private float _landedAt = -10f;
        private FlightFade _fade;
        private float _fadeAlpha, _fadeFrom, _fadeTime = -1f;
        private bool _jumpLocked, _gravityOff;
        private float _lastRootY, _lastRootT;
        private int _controlBits = -1;
        private GameObject _waterFloor;
        private readonly MountPlace _place = new MountPlace();
        private int _ridingLoggedId = -1;

        private void OnEnable()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                WowSpace.Rebased += OnRebased;
                WowPlay.Ended += OnEnded;
            }
            if (!_settingsLive)
            {
                _settingsLive = true;
                WowSettings.SetLive("vr_forced_move");
                WowSettings.SetLive("taxi_map_after_discovery");
                WowSettings.SetLive("vr_facing_assist");
            }
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                _subscribed = false;
                WowSpace.Rebased -= OnRebased;
                WowPlay.Ended -= OnEnded;
            }
            if (_active) Abort("the component stopped");
            UnlockAll();
        }

        private void LogOnce()
        {
            if (_logged) return;
            _logged = true;
            WowLog.Info("Forced move: ours - floor snap " + FloorSnap + " m, desktop heading " + YawRate + "/s, VR comfort " + new[] { "Dim", "Blink", "Smooth" }[Mathf.Clamp(WowSettings.VrForcedMove, 0, 2)] +
                        " (dim " + DimAlpha + ", fear / confuse " + WanderAlpha + ", fade out " + FadeOutSeconds + " s), haptic " + HapticStrength + " x " + HapticSeconds +
                        " s, knockback hard stop " + KnockbackHardStop + " s, slow fall capped at " + SlowFallCap.ToString("0.00") + " m/s (gravity back below " + SlowFallResume +
                        " m/s), water-walk floor within " + WaterFloorAbove + " m of the surface.");
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (p == null)
            {
                if (_active) Abort("no game");
                UnlockAll();
                return;
            }
            _vr = WowPlayer.XrPresent;
            ForcedMoveView m = p.ForcedMove;
            if (m != null && m.Id != _moveId) StartMove(s, p, m);
            else if (m == null && _active) ReleaseHere(s, "the session ended it");

            // locks: rooted, stunned, lost control or a running move (WowRunSpeed freezes the speed; no jump while locked)
            bool ride = WowMount.OwnsAvatar || WowFlight.Flying;
            bool locked = (p.Rooted || p.ControlLost || _active) && !p.OnTaxi;
            FeetLocked = locked && !ride;
            bool wantJumpLock = FeetLocked && !_active;
            if (wantJumpLock != _jumpLocked && !ride)
            {
                _jumpLocked = wantJumpLock;
                SetJump(wantJumpLock ? 0f : 1f);
            }
            else if (ride) _jumpLocked = false;

            // haptic tick when control is lost / given back, or a stun starts / ends (VR)
            int bits = (p.ControlLost && !p.OnTaxi ? 4 : 0) | (p.Stunned ? 2 : 0);
            if (_controlBits >= 0 && bits != _controlBits && _vr)
            {
                GearXr.Pulse(false, HapticStrength, HapticSeconds);
                GearXr.Pulse(true, HapticStrength, HapticSeconds);
            }
            _controlBits = bits;

            Gait = ComputeGait(p);
            SlowFallGovernor(p, ride);
            WaterFloor(s, p, ride);
        }

        private void LateUpdate()
        {
            TickFade();
            if (!_active || _move == null) return;
            IWowSession s = WowPlay.Session;
            if (s == null) { Abort("no session"); return; }
            if (_hold == null || _hold.Root == null) { Abort("the avatar disappeared"); return; }
            Drive(s);
        }

        // ------------------------------------------------------------------ a move

        private void StartMove(IWowSession s, PlayerState p, ForcedMoveView m)
        {
            _moveId = m.Id;
            if (m.Kind == ForcedMoveKind.Taxi || m.Kind == ForcedMoveKind.None) return;   // WowFlight rides the taxi
            LogOnce();
            if (WowFlight.Flying) { WowLog.Info("Forced move: " + m.Kind + " " + m.Id + " during a flight - left to the flight"); return; }
            if (WowMount.OwnsAvatar)
            {
                bool applied = false;
                try { applied = WowMount.TryApplyForcedMove(m); } catch (Exception e) { WowLog.Warn("Forced move: WowMount.TryApplyForcedMove threw " + e.Message); }
                if (!applied)
                {
                    if (_ridingLoggedId != m.Id) { _ridingLoggedId = m.Id; WowLog.Info("Forced move: forced " + m.Kind + " while riding: not applied (known gap) - the server position is right"); }
                    try { s.ForcedMoveDone(m.Id, p.Pos); } catch (Exception) { }
                }
                return;
            }
            if (_active) ReleaseForNext();
            Transform root = WowPlayer.Root;
            if (root == null && Application.isEditor)
            {
                CharacterController cc = FindFirstObjectByType<CharacterController>();
                if (cc != null) root = cc.transform;
            }
            if (root == null) { WowLog.Warn("Forced move: no avatar to move - " + m.Kind + " " + m.Id + " reported done at once"); try { s.ForcedMoveDone(m.Id, p.Pos); } catch (Exception) { } return; }
            if (_hold == null || !_hold.Taken)
            {
                _hold = new AvatarHold();
                string why;
                if (!_hold.TakeOver(root, m.Kind == ForcedMoveKind.Fleeing || m.Kind == ForcedMoveKind.Confused, out why))
                {
                    WowLog.Warn("Forced move: the avatar could not be taken (" + why + ") - reported done");
                    try { s.ForcedMoveDone(m.Id, p.Pos); } catch (Exception) { }
                    return;
                }
            }
            _move = m;
            _active = true;
            OwnsAvatar = true;
            WowOrigin.AvatarDriven = true;
            _t0 = Time.time - m.AgeAtPublish;
            _yaw = root.eulerAngles.y;
            _kbLast = root.position;
            _kbWallHit = false;
            BeginOverlay(m);
            WowLog.Info("Forced move: " + m.Kind + " " + m.Id + " (" + (m.Source ?? "?") + ", spell " + m.SpellId + ") takes the avatar" +
                        (m.Kind == ForcedMoveKind.Knockback ? " - launch " + m.SpeedXY.ToString("0.0") + " yd/s out, " + m.SpeedUp.ToString("0.0") + " yd/s up"
                         : " - " + ForcedMoveMath.PathLength(m).ToString("0.0") + " yd in " + m.DurationMs + " ms") + (_vr ? " (VR)" : "") + ".");
        }

        private void Drive(IWowSession s)
        {
            ForcedMoveView m = _move;
            float t = Time.time - _t0;
            WowMapView map = MapView();
            if (m.Kind == ForcedMoveKind.Knockback) { DriveKnockback(s, m, t, map); return; }
            float travel;
            WowPos w = ForcedMoveMath.PathAt(m, t, out travel);
            Vector3 feet = FlightRide.ToUnity(w.X, w.Y, w.Z);
            float gy;
            if (map != null && map.TryFloorHeight(feet, 2f, out gy) && Mathf.Abs(gy - feet.y) < FloorSnap) feet.y = gy;
            bool done = ForcedMoveMath.PathDone(m, t);
            float want = WowSpace.OrientationToYaw(travel);
            if (done || m.Kind == ForcedMoveKind.Path && t * 1000f > m.DurationMs * 0.9f) want = EndYaw(s, m, w, want);
            _yaw = Mathf.LerpAngle(_yaw, want, 1f - Mathf.Exp(-Time.deltaTime * YawRate));
            if (done)
            {
                Finish(s, feet, want, w);
                return;
            }
            _hold.Write(feet, _yaw, _vr);
        }

        private float EndYaw(IWowSession s, ForcedMoveView m, WowPos at, float fallback)
        {
            if (m.FacingGuid != 0)
            {
                EntityState e = s.Find(m.FacingGuid);
                if (e != null) return WowSpace.OrientationToYaw((float)Math.Atan2(e.Pos.Y - at.Y, e.Pos.X - at.X));
            }
            if (!float.IsNaN(m.EndFacing)) return WowSpace.OrientationToYaw(m.EndFacing);
            return fallback;
        }

        /// <summary>The ballistic flight (ForcedMoveMath.KnockbackAt), swept against layer 0 each frame: a wall stops the horizontal
        /// motion (ours - WoW slides), the ground or a walkable water surface lands it.</summary>
        private void DriveKnockback(IWowSession s, ForcedMoveView m, float t, WowMapView map)
        {
            WowPos w = ForcedMoveMath.KnockbackAt(m, t);
            double wx = _kbWallHit ? _kbWallX : w.X, wy = _kbWallHit ? _kbWallY : w.Y;
            Vector3 next = FlightRide.ToUnity(wx, wy, w.Z);
            float radius = _hold != null && _hold.CcRadius > 0.05f ? _hold.CcRadius : 0.25f;
            double vz = ForcedMoveMath.KnockbackVerticalSpeed(m, t);
            Vector3 from = _kbLast + Vector3.up * (radius + 0.05f);
            Vector3 d = next - _kbLast;
            float dist = d.magnitude;
            RaycastHit hit;
            if (dist > 1e-4f && Physics.SphereCast(from, radius, d / dist, out hit, dist, 1 << 0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > 0.5f && vz <= 0)
                {
                    Vector3 land = hit.point;
                    Land(s, m, land, "the ground");
                    return;
                }
                if (!_kbWallHit)
                {
                    _kbWallHit = true;
                    float hx, hy, hz;
                    Vector3 stop = _kbLast;
                    WowSpace.ToWow(s.MapId, stop, out hx, out hy, out hz);
                    _kbWallX = hx; _kbWallY = hy;
                    WowLog.Info("Forced move: the knockback hit a wall - falling straight down from here (WoW slides; ours)");
                    next = new Vector3(stop.x, next.y, stop.z);
                }
            }
            float gy;
            if (vz <= 0 && map != null && map.TryFloorHeight(next + Vector3.up * 0.5f, 1.0f, out gy) && next.y <= gy + 0.02f)
            {
                Land(s, m, new Vector3(next.x, gy, next.z), "the floor");
                return;
            }
            float surface;
            PlayerState p = s.Player;
            if (vz <= 0 && _place.TryLiquid(s.MapId, next, out surface) && next.y <= surface)
            {
                Land(s, m, new Vector3(next.x, surface, next.z), p != null && (p.WaterWalking || p.Ghost) ? "a water surface (water walking)" : "the water");
                return;
            }
            if (t > KnockbackHardStop)
            {
                WowLog.Warn("Forced move: the knockback did not land within " + KnockbackHardStop + " s - put down here");
                float ry;
                Vector3 at = next;
                if (map != null && map.TryGroundHeight(next, out ry)) at.y = ry;
                Land(s, m, at, "the hard stop");
                return;
            }
            _kbLast = next;
            _hold.Write(next, _yaw, _vr);
        }

        private void Land(IWowSession s, ForcedMoveView m, Vector3 spot, string what)
        {
            float x, y, z;
            WowSpace.ToWow(s.MapId, spot, out x, out y, out z);
            var w = new WowPos(x, y, z, WowSpace.YawToOrientation(_yaw));
            _landedAt = Time.time;
            WowLog.Info("Forced move: knockback " + m.Id + " landed on " + what + " after " + (Time.time - _t0).ToString("0.00") + " s at " + w + ".");
            Finish(s, spot, _yaw, w);
        }

        private void Finish(IWowSession s, Vector3 spot, float yaw, WowPos w)
        {
            ForcedMoveView m = _move;
            string how = "none";
            if (_hold != null) _hold.Release(spot, yaw, _vr, out how);
            _active = false;
            _move = null;
            OwnsAvatar = false;
            WowOrigin.AvatarDriven = false;
            EndOverlay();
            try { s.ForcedMoveDone(m.Id, w); } catch (Exception e) { WowLog.Warn("Forced move: ForcedMoveDone threw " + e.Message); }
            if (m.Kind != ForcedMoveKind.Fleeing && m.Kind != ForcedMoveKind.Confused)
                WowLog.Info("Forced move: " + m.Kind + " " + m.Id + " done at " + w + " (" + how + ").");
        }

        /// <summary>The session ended the move itself (teleport, replaced, timeout): give the avatar back where it is.</summary>
        private void ReleaseHere(IWowSession s, string why)
        {
            Vector3 at = _hold != null && _hold.Root != null ? _hold.Root.position : Vector3.zero;
            string how = "none";
            if (_hold != null) _hold.Release(at, _yaw, _vr, out how);
            WowLog.Info("Forced move: " + (_move != null ? _move.Kind + " " + _move.Id : "move") + " ended by the session (" + why + ") - the avatar stays where it is.");
            _active = false;
            _move = null;
            OwnsAvatar = false;
            WowOrigin.AvatarDriven = false;
            EndOverlay();
        }

        /// <summary>A new move replaces a running one: keep the hold (no release / re-take between fear segments).</summary>
        private void ReleaseForNext()
        {
            _active = false;
            _move = null;
        }

        private void Abort(string why)
        {
            if (_hold != null && _hold.Taken)
            {
                string how;
                Vector3 at = _hold.Root != null ? _hold.Root.position : Vector3.zero;
                _hold.Release(at, _yaw, _vr, out how);
            }
            WowLog.Info("Forced move: stopped (" + why + ").");
            _active = false;
            _move = null;
            OwnsAvatar = false;
            WowOrigin.AvatarDriven = false;
            EndOverlay();
        }

        private void OnEnded()
        {
            if (_active) Abort("the game ended");
            _moveId = -1;
            _controlBits = -1;
            UnlockAll();
        }

        private void OnRebased(Vector3 delta)
        {
            _kbLast -= delta;
            if (_waterFloor != null) _waterFloor.transform.position -= delta;
            if (!_active || _move == null) return;
            IWowSession s = WowPlay.Session;
            if (s == null || _hold == null || _hold.Root == null) return;
            if (_move.Kind == ForcedMoveKind.Knockback) { _hold.Write(_kbLast, _yaw, _vr); return; }
            try { Drive(s); } catch (Exception e) { WowLog.Warn("Forced move: rebase rewrite threw " + e.Message); }
        }

        // ------------------------------------------------------------------ gait, locks, slow fall, water

        private int ComputeGait(PlayerState p)
        {
            if (_active && _move != null)
            {
                switch (_move.Kind)
                {
                    case ForcedMoveKind.Path: return AnimRun;
                    case ForcedMoveKind.Fleeing: return AnimRun;
                    case ForcedMoveKind.Confused: return AnimWalk;
                    case ForcedMoveKind.Knockback:
                    {
                        float t = Time.time - _t0;
                        if (t < JumpStartSeconds) return AnimJumpStart;
                        return ForcedMoveMath.KnockbackVerticalSpeed(_move, t) > 0 ? AnimJump : AnimFall;
                    }
                }
            }
            if (Time.time - _landedAt < JumpEndSeconds) return AnimJumpEnd;
            if (p.Stunned && !p.OnTaxi) return AnimStun;
            return 0;
        }

        private static ISomniumPlayerMotion Motion()
        {
            try { return SomniumBridge.PlayersContainer?.LocalPlayer?.Features?.Motion; }
            catch (Exception) { return null; }
        }

        private static void SetJump(float v)
        {
            ISomniumPlayerMotion m = Motion();
            if (m == null) return;
            try { m.SetJump(v, v); } catch (Exception e) { WowLog.Warn("Forced move: SetJump threw " + e.Message); }
        }

        private void UnlockAll()
        {
            FeetLocked = false;
            Gait = 0;
            if (_jumpLocked) { _jumpLocked = false; SetJump(1f); }
            if (_gravityOff) { _gravityOff = false; SetGravity(1f); }
            if (_waterFloor != null) { Destroy(_waterFloor); _waterFloor = null; }
        }

        private static void SetGravity(float g)
        {
            ISomniumPlayerMotion m = Motion();
            if (m == null) return;
            try { m.SetGravity(g, g); } catch (Exception e) { WowLog.Warn("Forced move: SetGravity threw " + e.Message); }
        }

        /// <summary>Slow Fall / Levitate (p.SlowFall): descending faster than 7 yd/s turns the client's gravity off (its providers keep the
        /// descent speed constant: NonXRMotionFeature.cs:94-98, CustomContinuousMoveProvider.cs:248-251, :487); back on when slower or landed.</summary>
        private void SlowFallGovernor(PlayerState p, bool ride)
        {
            Transform root = WowPlayer.Root;
            float now = Time.time;
            float vy = 0f;
            if (root != null)
            {
                float dt = now - _lastRootT;
                if (dt > 1e-4f && dt < 0.5f) vy = (root.position.y - _lastRootY) / dt;
                _lastRootY = root.position.y;
                _lastRootT = now;
            }
            bool want = _gravityOff;
            if (!p.SlowFall || ride || _active || root == null) want = false;
            else if (!_gravityOff && vy < -SlowFallCap) want = true;
            else if (_gravityOff && vy > -SlowFallResume) want = false;
            if (want == _gravityOff) return;
            _gravityOff = want;
            SetGravity(want ? 0f : 1f);
            WowLog.Info("Forced move: slow fall " + (want ? "caps the descent (gravity off at " + (-vy).ToString("0.0") + " m/s)" : "- gravity back") + ".");
        }

        /// <summary>Water Walking / Levitate / a ghost: a hidden 3 x 3 m floor on layer 0 at the liquid surface under the feet, only while the
        /// feet are within 0.5 m above it (diving from a cliff lands on the water, as in WoW).</summary>
        private void WaterFloor(IWowSession s, PlayerState p, bool ride)
        {
            bool want = (p.WaterWalking || p.Ghost) && !ride && !_active;
            Vector3 feet;
            float surface = 0f;
            if (want && WowPlayer.TryFeet(out feet) && _place.TryLiquid(s.MapId, feet, out surface) && feet.y - surface < WaterFloorAbove && feet.y - surface > -0.3f)
            {
                if (_waterFloor == null)
                {
                    _waterFloor = new GameObject("WoW Water Walk Floor");
                    _waterFloor.layer = 0;
                    var box = _waterFloor.AddComponent<BoxCollider>();
                    box.size = new Vector3(3f, 0.1f, 3f);
                    WowLog.Info("Forced move: water walking - a floor at the water surface under the feet.");
                }
                _waterFloor.transform.position = new Vector3(feet.x, surface - 0.05f, feet.z);
                return;
            }
            if (_waterFloor != null && (!want || !WowPlayer.TryFeet(out feet) || !_place.TryLiquid(s.MapId, feet, out surface) || feet.y - surface >= WaterFloorAbove + 0.5f))
            {
                Destroy(_waterFloor);
                _waterFloor = null;
            }
        }

        // ------------------------------------------------------------------ the VR comfort overlay (D-M1)

        private void BeginOverlay(ForcedMoveView m)
        {
            if (!_vr) return;
            bool wander = m.Kind == ForcedMoveKind.Fleeing || m.Kind == ForcedMoveKind.Confused;
            int mode = Mathf.Clamp(WowSettings.VrForcedMove, 0, 2);
            float target = wander ? WanderAlpha : mode == 0 ? DimAlpha : mode == 1 ? 1f : 0f;
            if (target <= 0f) return;
            if (_fade == null) _fade = new FlightFade();
            _fadeFrom = _fadeAlpha;
            _fadeTarget = target;
            _fadeTime = 0f;
            _fadeIn = true;
        }

        private float _fadeTarget;
        private bool _fadeIn;

        private void EndOverlay()
        {
            if (_fade == null) return;
            _fadeFrom = _fadeAlpha;
            _fadeTarget = 0f;
            _fadeTime = 0f;
            _fadeIn = false;
        }

        private void TickFade()
        {
            if (_fade == null || _fadeTime < 0f) return;
            _fadeTime += Time.deltaTime;
            float span = _fadeIn ? (_fadeTarget >= 1f ? BlinkInSeconds : 0.1f) : FadeOutSeconds;
            float k = Mathf.Clamp01(_fadeTime / span);
            _fadeAlpha = Mathf.Lerp(_fadeFrom, _fadeTarget, k);
            _fade.Set(_fadeAlpha);
            if (k >= 1f && !_fadeIn)
            {
                _fade.Destroy();
                _fade = null;
                _fadeAlpha = 0f;
                _fadeTime = -1f;
            }
        }

        private static WowMapView MapView()
        {
            WowGame g = WowGame.Current;
            return g != null ? g.Map : null;
        }
    }
}
