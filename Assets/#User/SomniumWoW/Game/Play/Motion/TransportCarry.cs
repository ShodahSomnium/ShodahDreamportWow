// iteration 11 - owner motion (proposal-motion.md 6.2.7): transports carry the Somnium avatar - boats and zeppelins (GO type 15), elevators
// and the Deeprun Tram (GO type 11). Added by WowGame (hook 7). Online only in practice: offline has no transport entities.
// 2026-10-03 (the user: "the same as the elevators in base reality, we used it in euro truck simulator"): the carry is now the Main Build's
// elevator method as Euro Truck Simulator's riders rebuilt it for world code (no client platform component is reachable from a world: the
// SDK has no MobilePlatform and the Bridge no VRIKAddPlatformMotion / SetInElevator; ETS README "Riders": "None of that is reachable"):
// - Base reality (MB = E:/Somnium Space/Main Build/Assets/_SomniumSpace): an elevator's LateUpdate (Elevators/Scripts/ElevatorsNetworkHandler.cs
//   :32-46 -> ElevatorControl.cs:79-90, 235-240) runs MobilePlatform.ExecuteMobilePlatformLogic (DynamicObjects/MobilePlatform.cs:40-45): the
//   platform's pose delta since last frame (MobilePlatformMover.cs:53-57) moves the player's CharacterController TRANSFORM directly -
//   PhysicBodyPosition += deltaRotation * rel - rel + deltaPosition (:137-143; the setter writes _characterControllerRef.transform.position,
//   Player/.../Common/MotionFeature.cs:25-40), never CharacterController.Move - and never turns the player (:151 Quaternion.identity).
// - ETS (D:/Dreamport/Euro Truck Simulator/Euro Truck Sim/Assets/#User/SomniumEuroTruck): the same rigid carry (Ets/EtsRiderMath.cs:128-150
//   Carry / CarryRoot) written on Body.Root + Physics.SyncTransforms() as the client's own teleport does (EtsRiders.cs:418-438;
//   NonXRMotionFeature.cs DoTeleportToPoint), in Update at order -100 right after the vehicles are drawn (EtsNet.cs:42, 270-285) - before the
//   client's order-0 scripts, because NonXRRootsMovementHandler places a desktop avatar in Update (MB AvatarSystem/.../NonXRRootsMovementHandler.cs:5-15;
//   only the client's own elevators switch it to LateUpdate); VRIK legs and the falling clip off while carried faster than 0.5 m/s, the
//   substitute for VRIKAddPlatformMotion (EtsRiders.cs:589-607, EtsRiderMath.cs:59); a frame that jumps more than 50 m is not carried
//   (EtsRiderMath.cs:43); an airborne walker stays a rider 1.5 s (EtsRiderMath.cs:52); the frame's pose is remembered in LateUpdate for the next
//   carry (EtsRiders.cs:487-499); "carry lost" counted as there (EtsRiderMath.cs:183-206).
// Here, every frame while the avatar stands on a transport:
//   Update (order -100, before WowGame ticks the session and WowEntityViews draws at order 0, before the client's order-0 scripts):
//     1. OnlineSession.AdvanceTransports(Time.deltaTime): the transports' poses at the clock this frame's Tick will reach;
//     2. the ridden transport's view root is put at that pose (the pose WowEntityView.UpdatePose gives a transport, which then writes the same);
//     3. the avatar's CharacterController transform is moved by the deck's rigid motion since last frame - position only, desktop and VR
//        (base reality never turns the player; the VR comfort rule D-M4) - and Physics.SyncTransforms() so the client steps against the deck
//        where it is drawn. VR carries the head's ground point (where the player stands in room-scale, ETS ProbePoint EtsRiders.cs:497-503).
//   LateUpdate: on / off detection (a ray under the feet), the deck pose remembered, the avatar's offset on the deck kept for a rebase.
// - Detection: a ray under the feet on layer 0; the hit collider belongs to a transport when it IsChildOf that transport's VIEW ROOT
//   (WowEntityViews.GetView; TryGetGuid only knows click boxes). A change -> IWowSession.SetPlayerTransport (the session then sends
//   MOVEMENTFLAG_ONTRANSPORT + our offsets, MovementHandler.cpp:437-486). Off: a solid (non-transport) floor under the feet stops the carry at
//   once and unboards after 0.5 s; no floor 0.5 s grounded, 1.5 s airborne (ours / ETS).
// - Floating origin: while riding, WowOrigin.AvatarDriven is set (we write the avatar every frame, as a flight or seat does), so a rebase on
//   board commits at once and OnRebased moves the avatar with the world; a rebase that went the teleport way instead puts the avatar back on
//   its deck spot (the ship sailed on during the teleport's frames).
// - Arrival after a map change on board (the session raises Teleport with PlayerState.TransportGuid set, OnlineSession.Transport.cs):
//   the avatar is held (AvatarHold) at its offsets on the ship until the ship's view has its collider under the feet, max 20 s (ours).
using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Worlds.SomniumWoW.Game.Motion;
using SomniumSpace.Worlds.SomniumWoW.Game.Mounts;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Motion;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    [DefaultExecutionOrder(-100)]
    public sealed class TransportCarry : MonoBehaviour
    {
        // ours (logged once)
        private const float RayUp = 0.5f, RayDown = 1.5f;     // m: the ray from feet + 0.5 m down 1.5 m (6.2.7)
        private const float OffGrace = 0.5f;                  // s: grounded with no deck under the feet before "off the transport"
        private const float RootsRefresh = 1f;                // s between re-reads of the transport view roots
        private const float ArrivalHoldMax = 20f;             // s: the avatar is held on board after a map change at most this long
        private const float ArrivalWaitMax = 60f;             // s: the world (loading screen) is awaited this long before the hold starts
        private const float ReportEvery = 30f;                // s between "riding" lines while on board
        // ETS (Ets/EtsRiderMath.cs)
        private const float JumpMetres = 50f;                 // :43 a frame that moved further was set down somewhere new: not carried
        private const float AirborneGrace = 1.5f;             // :52 an airborne walker stays a rider this long
        private const float LegsOffSpeed = 0.5f;              // :59 carried faster: VRIK legs and the falling clip off
        private const float MaxOwnSpeed = 7.2f;               // :183 the fastest a player moves themselves (CarryLost)
        private const int GoTypeMoTransport = 15;

        /// <summary>The transport the avatar stands on (its guid), 0 = none.</summary>
        public static ulong Current { get; private set; }

        private static bool _logged;
        private readonly List<KeyValuePair<ulong, Transform>> _roots = new List<KeyValuePair<ulong, Transform>>();
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private float _rootsAt = -10f, _offSince = -1f;
        private Transform _carryRoot;
        private Matrix4x4 _prev;
        private bool _prevValid;
        private bool _onSolid;            // a non-transport floor is under the feet: no carry (unboarding after OffGrace)
        private CharacterController _cc;
        private Transform _ccFor;
        private bool _subscribed;
        private bool _wasBusy;
        private bool _driven;             // we set WowOrigin.AvatarDriven
        private bool _legsOff, _legsDirty;
        private Vector3 _offset;          // the carried transform's position in the deck's frame (LateUpdate), for a rebase
        private bool _offsetValid;
        private bool _placeWarned, _jumpWarned;

        // the carry's own numbers (ETS "carry lost")
        private int _carriedFrame = -1;
        private Transform _carryBody;
        private Vector3 _carriedTo, _travel;
        private float _carryDt, _topSpeed, _reportAt;
        private int _frames, _checked, _lost;

        // arrival on board after a map change
        private bool _arrival;
        private ulong _arrivalGuid;
        private float _arrivalAt, _holdAt;
        private WowPos _arrivalOffset;
        private bool _arrivalOffsetKnown;
        private AvatarHold _hold;

        private void OnEnable()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                WowPlay.Event += OnEvent;
                WowPlay.Ended += OnEnded;
                WowSpace.Rebased += OnRebased;
            }
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("Transports: carry = base reality's elevator method as ETS runs it (Update order -100: deck drawn, avatar's controller moved by the deck's motion, " +
                            "Physics.SyncTransforms; position only, desktop and VR). Values - ours: ray " + RayUp + " m up / " + RayDown + " m down on layer 0, off-grace " +
                            OffGrace + " s, arrival hold max " + ArrivalHoldMax + " s; ETS: airborne grace " + AirborneGrace + " s, legs off above " + LegsOffSpeed +
                            " m/s, not carried past " + JumpMetres + " m in a frame.");
            }
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                _subscribed = false;
                WowPlay.Event -= OnEvent;
                WowPlay.Ended -= OnEnded;
                WowSpace.Rebased -= OnRebased;
            }
            ReleaseHold("the component was disabled", false);
            ClearState();
        }

        private void ClearState()
        {
            Current = 0;
            _carryRoot = null;
            _prevValid = false;
            _offsetValid = false;
            _onSolid = false;
            _offSince = -1f;
            _arrival = false;
            _roots.Clear();
            _rootsAt = -10f;
            SetLegs(false);
            Driven(false);
        }

        private void OnEnded()
        {
            ReleaseHold("the game ended", false);
            ClearState();
        }

        private void OnEvent(GameEvent e)
        {
            if (e.Type != GameEventType.Teleport) return;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (p == null || p.TransportGuid == 0)
            {
                // an ordinary teleport: whatever we stood on is gone from under us
                _carryRoot = null; _prevValid = false; _offsetValid = false;
                return;
            }
            // Arrival on board (OnlineSession.Transport.cs NoteSelfOnTransport / its fallback): keep our offsets on the ship.
            Driven(false);   // until the hold takes the avatar, a rebase teleports it as usual (OnRebased skips the arrival)
            _arrival = true;
            _arrivalGuid = p.TransportGuid;
            _arrivalAt = Time.time;
            _arrivalOffsetKnown = false;
            EntityState t = s.Find(p.TransportGuid);
            if (t != null && t.IsTransport)
            {
                _arrivalOffset = ForcedMoveMath.PassengerOffset(e.Pos.X, e.Pos.Y, e.Pos.Z, e.Pos.O, t.Pos);
                _arrivalOffsetKnown = true;
            }
            _carryRoot = null; _prevValid = false; _offsetValid = false;
            SetLegs(false);
            WowLog.Info("Transports: arrival on board " + (t != null ? t.Name : p.TransportGuid.ToString("X")) + " after a map change - holding the avatar until the deck is there" +
                        (_arrivalOffsetKnown ? " (offsets " + _arrivalOffset.X.ToString("0.0") + ", " + _arrivalOffset.Y.ToString("0.0") + ", " + _arrivalOffset.Z.ToString("0.0") + ")" : "") + ".");
        }

        /// <summary>WowOrigin moved the world (the transport views with it) by -delta and synced physics. The stored deck pose moves with it;
        /// the avatar, which we drive while on board, is moved with the world (AvatarDriven: the commit came at once) or, when the rebase
        /// went the teleport way (the teleport was aimed where the avatar stood when it began), put back on its deck spot.</summary>
        private void OnRebased(Vector3 delta)
        {
            if (_prevValid) _prev = Matrix4x4.Translate(-delta) * _prev;
            if (Current == 0 || _arrival || Busy()) return;   // the hold / the owner of a forced move, mount, flight or vehicle moves the avatar itself
            Transform root = WowPlayer.Root;
            if (root == null) return;
            try
            {
                Transform body = Body(root);
                Vector3 a = body.position, shifted = a - delta, to;
                string how;
                if (_offsetValid && _carryRoot != null)
                {
                    Vector3 spot = _carryRoot.TransformPoint(_offset);
                    if ((shifted - spot).sqrMagnitude < (a - spot).sqrMagnitude) { to = shifted; how = "moved with the world"; }
                    else { to = spot; how = "put back on its deck spot, " + Mathf.RoundToInt((a - spot).magnitude * 100f) + " cm from where the rebase teleport left it"; }
                }
                else if (_driven) { to = shifted; how = "moved with the world (no deck spot known)"; }
                else return;
                body.position = to;
                Physics.SyncTransforms();
                WowLog.Info("Transports: rebase on board " + Current.ToString("X16") + " - the avatar " + how + ".");
            }
            catch (Exception e) { WowLog.Warn("Transports: the rebase on board threw " + e.Message); }
        }

        private static bool Busy()
        {
            return WowForcedMove.OwnsAvatar || WowMount.OwnsAvatar || WowFlight.Flying || WowVehicle.OwnsAvatar;
        }

        // ------------------------------------------------------------------ Update: deck drawn, avatar carried (before order 0)

        private void Update()
        {
            _carriedFrame = -1;
            IWowSession s = WowPlay.Session;
            Transform root = WowPlayer.Root;
            if (s == null || s.Player == null || root == null || _arrival) return;   // LateUpdate clears / runs the arrival
            try
            {
                if (Busy())
                {
                    // something else writes the avatar (and may take the legs / falling flags): no carry; on board still
                    _wasBusy = true;
                    return;
                }
                if (_wasBusy)
                {
                    _wasBusy = false;
                    if (_legsOff) _legsDirty = true;   // the owner may have given the legs back: ours again
                }
                if (Current == 0 || _carryRoot == null || _onSolid) { SetLegs(false); return; }
                Carry(s, root);
            }
            catch (Exception ex) { WowLog.Warn("Transports: carry threw " + ex.Message); _prevValid = false; }
        }

        private void Carry(IWowSession s, Transform root)
        {
            // 1-2. This frame's transport poses (the session ticks at order 0, later), and the ridden deck drawn there now.
            OnlineSession online = s as OnlineSession;
            if (online != null) online.AdvanceTransports(Time.deltaTime);
            EntityState t = s.Find(Current);
            if (t != null && t.IsTransport) PlaceView(_carryRoot, t, s.MapId);
            if (!_prevValid) { Physics.SyncTransforms(); SetLegs(false); return; }   // the first frame on it: LateUpdate remembers the deck

            // 3. The rigid motion of the deck since last frame, applied to the controller's transform (position only).
            Matrix4x4 now = _carryRoot.localToWorldMatrix;
            Vector3 jump = (Vector3)now.GetColumn(3) - (Vector3)_prev.GetColumn(3);
            if (jump.sqrMagnitude > JumpMetres * JumpMetres)
            {
                if (!_jumpWarned) { _jumpWarned = true; WowLog.Warn("Transports: the deck of " + Current.ToString("X16") + " moved " + Mathf.RoundToInt(jump.magnitude) + " m in one frame - not carried (ETS JumpMetres)."); }
                _prevValid = false;
                SetLegs(false);
                return;
            }
            Matrix4x4 step = now * _prev.inverse;
            Transform body = Body(root);
            Vector3 point = CarryPoint(body);
            Vector3 delta = step.MultiplyPoint3x4(point) - point;
            body.position += delta;
            Physics.SyncTransforms();

            float dt = Time.deltaTime;
            _carriedFrame = Time.frameCount;
            _carryBody = body;
            _carriedTo = body.position;
            _travel = delta;
            _carryDt = dt;
            _frames++;
            float speed = dt > 1e-5f ? delta.magnitude / dt : 0f;
            if (speed > _topSpeed) _topSpeed = speed;
            SetLegs(speed > LegsOffSpeed);
        }

        /// <summary>The pose WowEntityView.UpdatePose gives a transport (WowEntityView.cs: target = ToUnity(Pos), unsmoothed for transports; an MO
        /// transport turns with its path heading Pos.O, a static one keeps its spawn rotation), so its own write at order 0 is the same.</summary>
        private void PlaceView(Transform view, EntityState t, int mapId)
        {
            if (view == null) return;
            Vector3 pos = WowSpace.ToUnity(mapId, t.Pos.X, t.Pos.Y, t.Pos.Z);
            float yaw = WowSpace.OrientationToYaw(t.Pos.O);
            Quaternion rot;
            if (t.GoType == GoTypeMoTransport) rot = Quaternion.Euler(0f, yaw, 0f);
            else if (t.RotX != 0f || t.RotY != 0f || t.RotZ != 0f || t.RotW != 0f) rot = WowSpace.RotationToUnity(t.RotX, t.RotY, t.RotZ, t.RotW);
            else rot = Quaternion.Euler(0f, yaw, 0f);
            if ((pos - view.position).sqrMagnitude > JumpMetres * JumpMetres)
            {
                // not this frame's step of the deck (a map id or origin mismatch): leave the view to WowEntityViews
                if (!_placeWarned) { _placeWarned = true; WowLog.Warn("Transports: " + (t.Name ?? "the transport") + "'s pose is " + Mathf.RoundToInt((pos - view.position).magnitude) + " m from its view - not drawn early."); }
                return;
            }
            view.SetPositionAndRotation(pos, rot);
        }

        /// <summary>The point the deck carries: the controller's own position on a monitor; in a headset the head's ground point (the player
        /// stands where the head is in room-scale; ETS ProbePoint).</summary>
        private Vector3 CarryPoint(Transform body)
        {
            Vector3 p = body.position;
            if (!WowPlayer.XrPresent) return p;
            Transform head = WowPlayer.HeadBone;
            return head != null ? new Vector3(head.position.x, p.y, head.position.z) : p;
        }

        /// <summary>The transform base reality moves (the CharacterController's, MotionFeature.PhysicBodyPosition): the controller when it is
        /// on Body.Root or above it, else Body.Root.</summary>
        private Transform Body(Transform root)
        {
            CharacterController cc = Controller(root);
            return cc != null && root.IsChildOf(cc.transform) ? cc.transform : root;
        }

        private CharacterController Controller(Transform root)
        {
            if (_ccFor != root)
            {
                _ccFor = root;
                _cc = root.GetComponentInParent<CharacterController>();
                if (_cc == null) _cc = root.GetComponentInChildren<CharacterController>();
            }
            return _cc;
        }

        /// <summary>ETS SetLocalLegs (EtsRiders.cs:589-607): VRIK's procedural steps and the falling clip off while the deck carries the avatar
        /// faster than LegsOffSpeed (the deck can take the controller's ground contact for a frame), back when it slows or the avatar leaves.</summary>
        private void SetLegs(bool off)
        {
            if (_legsOff == off && !_legsDirty) return;
            _legsDirty = false;
            _legsOff = off;
            ISomniumPlayer player = null;
            try { player = SomniumBridge.PlayersContainer?.LocalPlayer; } catch (Exception) { player = null; }
            if (player == null) return;
            try { player.Features?.Avatar?.SetVRIKLegMovementDisabled(off); } catch (Exception) { }
            try { player.Features?.Animation?.SetFallingAnimationDisabled(off); } catch (Exception) { }
        }

        /// <summary>WowOrigin.AvatarDriven while we write the avatar every frame (a rebase then commits at once; OnRebased moves the avatar).
        /// Never cleared while another owner holds the avatar (it set the flag itself).</summary>
        private void Driven(bool want)
        {
            if (want)
            {
                if (!WowOrigin.AvatarDriven) { WowOrigin.AvatarDriven = true; _driven = true; }
            }
            else if (_driven)
            {
                _driven = false;
                if (!Busy() && !_arrival && _hold == null) WowOrigin.AvatarDriven = false;
            }
        }

        // ------------------------------------------------------------------ LateUpdate: detection, the deck remembered

        private void LateUpdate()
        {
            IWowSession s = WowPlay.Session;
            Transform root = WowPlayer.Root;
            if (s == null || s.Player == null || root == null) { if (Current != 0 || _arrival) { ReleaseHold("no session or avatar", false); ClearState(); } return; }
            try
            {
                if (_arrival) { TickArrival(s, root); return; }
                if (Busy())
                {
                    // a held avatar is not "off" the transport yet; the deck is still remembered for the carry after
                    if (_driven) _driven = false;   // the owner set AvatarDriven itself and clears it when done
                    Remember();
                    return;
                }
                CountLost();
                Detect(s, root);
                Remember();
                MeasureOffset(root);
                Driven(Current != 0);
                Report();
            }
            catch (Exception ex) { WowLog.Warn("Transports: carry threw " + ex.Message); _prevValid = false; }
        }

        /// <summary>The pose the next carry starts from: the last one this frame drew (EtsRiders.cs:487-499).</summary>
        private void Remember()
        {
            if (_carryRoot != null) { _prev = _carryRoot.localToWorldMatrix; _prevValid = true; }
            else _prevValid = false;
        }

        private void MeasureOffset(Transform root)
        {
            if (Current == 0 || _carryRoot == null) { _offsetValid = false; return; }
            if (WowOrigin.Pending) return;   // a rebase teleport may land this frame: keep the spot from before it
            _offset = _carryRoot.InverseTransformPoint(Body(root).position);
            _offsetValid = true;
            if (_onDeckNow) { _deckSpot = _offset; _deckSpotValid = true; }   // lead fix 2026-10-05: the last spot actually standing on the deck
        }

        // Lead fix (2026-10-05, the user: "Sometimes falling through the floor of the Zeppelin"): the avatar fell through the deck (a gap in
        // its collider / a fast turn) - more than FallBelowDeck under its last spot standing on the deck, within RescueRadius of it, while still
        // counted on board (the off-grace): put back on that spot, at most RescueMax times per RescueWindow. Values ours.
        private const float FallBelowDeck = 1.5f, RescueRadius = 12f, RescueWindow = 10f;
        private const int RescueMax = 3;
        private Vector3 _deckSpot;
        private bool _deckSpotValid, _onDeckNow;
        private float _rescueSince = -100f;
        private int _rescues;

        private bool TryRescue(Transform root, Vector3 feet)
        {
            if (!_deckSpotValid || _carryRoot == null) return false;
            Vector3 spot = _carryRoot.TransformPoint(_deckSpot);
            Vector3 flat = feet - spot; flat.y = 0f;
            if (feet.y > spot.y - FallBelowDeck || flat.magnitude > RescueRadius) return false;
            if (Time.time - _rescueSince > RescueWindow) { _rescueSince = Time.time; _rescues = 0; }
            if (_rescues >= RescueMax) return false;
            _rescues++;
            Transform body = Body(root);
            body.position += spot + Vector3.up * 0.1f - feet;
            Physics.SyncTransforms();
            _offSince = -1f;
            WowLog.Info("Transports: fell through the deck of " + Current.ToString("X16") + " (" + (spot.y - feet.y).ToString("0.0") + " m under the last deck spot) - put back on it (" + _rescues + " of " + RescueMax + " in " + RescueWindow + " s).");
            return true;
        }

        /// <summary>ETS CarryLost (EtsRiderMath.cs:183-206): after the client's step the avatar is more than half the deck's travel BEHIND where
        /// the carry put it - something dragged it back. Told apart from the player's own step only above twice MaxOwnSpeed.</summary>
        private void CountLost()
        {
            if (_carriedFrame != Time.frameCount || _carryBody == null) return;
            float len = _travel.magnitude, own = MaxOwnSpeed * _carryDt;
            if (len <= 2f * own || len <= 0f) return;
            _checked++;
            if (Vector3.Dot(_carryBody.position - _carriedTo, _travel / len) < -0.5f * len) _lost++;
        }

        private void Report()
        {
            if (Current == 0 || Time.time < _reportAt) return;
            _reportAt = Time.time + ReportEvery;
            if (_frames > 0) WowLog.Info("Transports: riding " + Current.ToString("X16") + " - " + Numbers() + ".");
        }

        private string Numbers()
        {
            return "carried " + _frames + " frames, top " + _topSpeed.ToString("0.0") + " m/s, carry lost " + _lost + " of " + _checked + " checked frames";
        }

        // ------------------------------------------------------------------ detection

        private void RefreshRoots(IWowSession s)
        {
            if (Time.time - _rootsAt < RootsRefresh) return;
            _rootsAt = Time.time;
            _roots.Clear();
            IReadOnlyList<EntityState> list = s.Entities;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                EntityState e = list[i];
                if (e == null || !e.IsTransport) continue;
                Transform r = WowEntityViews.GetView(e.Guid);
                if (r != null) _roots.Add(new KeyValuePair<ulong, Transform>(e.Guid, r));
            }
        }

        /// <summary>The transport whose view owns the nearest floor hit under p (ignoring the avatar's own colliders), 0 = none; solid = the
        /// nearest floor is not a transport's.</summary>
        private ulong Under(Vector3 p, float up, float down, Transform avatar, out Transform viewRoot, out RaycastHit floor, out bool solid)
        {
            viewRoot = null;
            floor = default(RaycastHit);
            solid = false;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * up, Vector3.down, _hits, up + down, 1 << 0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || (avatar != null && c.transform.IsChildOf(avatar))) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; bi = i; }
            }
            if (bi < 0) return 0;
            floor = _hits[bi];
            Transform ht = _hits[bi].collider.transform;
            for (int k = 0; k < _roots.Count; k++)
            {
                Transform r = _roots[k].Value;
                if (r != null && ht.IsChildOf(r)) { viewRoot = r; return _roots[k].Key; }
            }
            solid = true;
            return 0;
        }

        private void Detect(IWowSession s, Transform root)
        {
            RefreshRoots(s);
            Vector3 feet;
            if (!WowPlayer.TryFeet(out feet)) feet = root.position;
            if (WowPlayer.XrPresent && WowPlayer.HeadBone != null) { Vector3 h = WowPlayer.HeadBone.position; feet = new Vector3(h.x, feet.y, h.z); }
            Transform r;
            RaycastHit floor;
            bool solid;
            ulong g = Under(feet, RayUp, RayDown, root, out r, out floor, out solid);
            _onDeckNow = g != 0 && g == Current;   // lead fix 2026-10-05
            if (g != 0)
            {
                _offSince = -1f;
                _onSolid = false;
                if (g != Current) SetTransport(s, g, r, Current != 0 ? "the avatar stepped across onto it" : "the avatar stands on it");
                else if (r != _carryRoot) { _carryRoot = r; _prevValid = false; }
                return;
            }
            if (Current == 0) return;
            if (TryRescue(root, feet)) return;   // lead fix 2026-10-05
            CharacterController cc = Controller(root);
            bool airborne = cc != null && cc.enabled && !cc.isGrounded && !solid;
            _onSolid = solid;   // on a dock or the ground: the deck no longer carries the avatar
            if (_offSince < 0f) _offSince = Time.time;
            float grace = airborne ? AirborneGrace : OffGrace;
            if (Time.time - _offSince >= grace)
                SetTransport(s, 0, null, solid ? "a floor that is not the transport's under the feet for " + OffGrace + " s (" + floor.collider.name + ")"
                                              : (airborne ? "airborne" : "no deck under the feet") + " for " + grace + " s");
        }

        private void SetTransport(IWowSession s, ulong g, Transform r, string why)
        {
            ulong old = Current;
            if (old != 0 && _frames > 0) WowLog.Info("Transports: the ride on " + old.ToString("X16") + " - " + Numbers() + ".");
            Current = g;
            _carryRoot = r;
            _deckSpotValid = false;   // lead fix 2026-10-05
            _prevValid = false;
            _offsetValid = false;
            _onSolid = false;
            _offSince = -1f;
            _frames = 0; _checked = 0; _lost = 0; _topSpeed = 0f;
            _reportAt = Time.time + ReportEvery;
            _jumpWarned = false;
            _placeWarned = false;
            if (g == 0) { SetLegs(false); Driven(false); }
            WowLog.Info("Transports: " + (g != 0 ? "standing on " + g.ToString("X16") : "off " + old.ToString("X16")) + " - " + why + ".");
            try { s.SetPlayerTransport(g); }
            catch (Exception e) { WowLog.Warn("Transports: SetPlayerTransport threw " + e.Message); }
        }

        // ------------------------------------------------------------------ arrival after a map change on board

        private void TickArrival(IWowSession s, Transform root)
        {
            if (_hold == null)
            {
                // the far teleport / loading screen first: the avatar must be out of the login room on a settled origin
                if (!WowFlight.WorldReady())
                {
                    if (Time.time - _arrivalAt > ArrivalWaitMax) { WowLog.Warn("Transports: the world was not ready " + ArrivalWaitMax + " s after the arrival - not holding the avatar."); _arrival = false; }
                    return;
                }
                if (Busy()) { _arrival = false; return; }
                var hold = new AvatarHold { Tag = "Transports" };
                string why;
                if (!hold.TakeOver(root, false, out why)) { WowLog.Warn("Transports: cannot hold the avatar on board (" + why + ")."); _arrival = false; return; }
                _hold = hold;
                _holdAt = Time.time;
                _driven = false;   // the hold owns AvatarDriven now
                WowOrigin.AvatarDriven = true;
            }
            Vector3 target = _hold.Root != null ? _hold.Root.position : root.position;
            EntityState t = s.Find(_arrivalGuid);
            if (_arrivalOffsetKnown && t != null && t.IsTransport)
            {
                WowPos w = ForcedMoveMath.PassengerPosition(_arrivalOffset.X, _arrivalOffset.Y, _arrivalOffset.Z, _arrivalOffset.O, t.Pos);
                target = WowSpace.ToUnity(s.MapId, w.X, w.Y, w.Z);
            }
            float yaw = root.eulerAngles.y;
            _hold.Write(target, yaw, WowPlayer.XrPresent);
            RefreshRoots(s);
            Transform r;
            RaycastHit floor;
            bool solid;
            ulong g = Under(target, 1.5f, 3f, root, out r, out floor, out solid);
            if (g == _arrivalGuid && r != null)
            {
                ReleaseHold("the deck is under the feet", true, floor.point + Vector3.up * 0.05f, yaw);
                SetTransport(s, g, r, "arrived on board");
                return;
            }
            if (Time.time - _holdAt > ArrivalHoldMax) ReleaseHold("no deck under the feet after " + ArrivalHoldMax + " s", true, target, yaw);
        }

        private void ReleaseHold(string why, bool place)
        {
            if (_hold == null) return;
            Vector3 at = _hold.Root != null ? _hold.Root.position : Vector3.zero;
            ReleaseHold(why, place, at, _hold.Root != null ? _hold.Root.eulerAngles.y : 0f);
        }

        private void ReleaseHold(string why, bool place, Vector3 spot, float yaw)
        {
            _arrival = false;
            AvatarHold h = _hold;
            _hold = null;
            if (h == null) return;
            string how = "none";
            try { h.Release(spot, yaw, WowPlayer.XrPresent, out how); }
            catch (Exception e) { WowLog.Warn("Transports: releasing the held avatar threw " + e.Message); }
            WowOrigin.AvatarDriven = false;
            WowLog.Info("Transports: on-board hold released - " + why + " (" + how + (place ? ", at " + spot.ToString("F1") : "") + ").");
        }
    }
}
