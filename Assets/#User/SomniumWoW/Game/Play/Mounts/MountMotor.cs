using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.World;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Mounts
{
    /// <summary>
    /// The ride's physics body (iteration 7, owner Ride; PLAN7 s5.3 "Takeover", "Motion", "Floating origin"; s4.9 "Collision layers").
    /// A hidden proxy CharacterController "WoW Mount Proxy" - a child of the WoW Game object (WowOrigin shifts it and syncs physics;
    /// it is never moved in a Rebased handler except the logged re-seat below), on layer 2 (no client pointer ray hits it) with
    /// excludeLayers = everything but layer 0, so it touches only terrain tiles and WMO groups (the only colliders, layer 0) and never
    /// players, hands, click boxes or UI. Its radius / stepOffset / slopeLimit are the avatar controller's, its height the mounted
    /// collision height (AzerothCore Unit::GetCollisionHeight: the rider's cylinder made taller). Stepped once per rendered frame:
    /// RideMotion.Step -> Move -> isGrounded; the visible height through RideMotion.FilterY (stairs). Unloaded ground: the altitude is
    /// held (RideMotion); a proxy more than 2 m under the ADT terrain with nothing below it is put back on top (logged).
    /// </summary>
    public sealed class MountMotor
    {
        /// <summary>Ours (PLAN7 s4.8): a layer-0 collider within this many metres under the proxy counts as "ground built".</summary>
        public const float ColliderSearch = 60f;
        /// <summary>Ours (PLAN7 s4.8): the fall-through rescue - more than this many metres under the ADT terrain height, nothing below.</summary>
        public const float RescueBelow = 2f;
        /// <summary>Ours: a rebase that left the proxy this far from where it should be re-seats it (logged).</summary>
        public const float ReseatResidual = 0.01f;
        private const int LayerProxy = 2;       // Ignore Raycast: the avatar's own capsule layer (PLAN7 s4.9)
        private const int WorldMask = 1 << 0;   // terrain tiles + WMO groups (WowMapView.cs 552-558, WmoFactory.cs 14 / 266)

        public CharacterController Proxy { get; private set; }
        public RideState State;
        public RideGround Ground;
        public float VisibleY { get; private set; }
        public float SkinWidth { get; private set; }
        /// <summary>Planar metres moved (after collision) since the last TakeStats.</summary>
        public float Distance { get; private set; }
        /// <summary>The largest |proxy y - visible y| since the last TakeStats.</summary>
        public float MaxGap { get; private set; }
        public int Rescues { get; private set; }
        public int Reseats { get; private set; }
        /// <summary>The actual planar speed of the last Move (m/s, after collision).</summary>
        public float ActualSpeed { get; private set; }

        private Vector3 _afterMove;
        private bool _moveBroken;

        /// <summary>The MOUNT's feet (the capsule's bottom on the ground), Unity, current origin frame.</summary>
        public Vector3 Feet { get { return Proxy != null ? Proxy.transform.position - Vector3.up * SkinWidth : Vector3.zero; } }

        /// <summary>The feet with the step-filtered height (where the mount model is drawn).</summary>
        public Vector3 VisibleFeet { get { Vector3 f = Feet; f.y = VisibleY; return f; } }

        public bool Create(Transform parent, Vector3 feet, float heading, float radius, float height, float stepOffset, float slopeLimit)
        {
            Destroy();
            var go = new GameObject("WoW Mount Proxy");
            go.layer = LayerProxy;
            if (parent != null) go.transform.SetParent(parent, false);
            radius = Mathf.Clamp(radius > 0.01f ? radius : 0.25f, 0.05f, 1f);
            SkinWidth = Mathf.Max(0.01f, radius * 0.1f);   // Unity's advice: 10 % of the radius
            go.transform.SetPositionAndRotation(feet + Vector3.up * SkinWidth, Quaternion.identity);
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.radius = radius;
            cc.skinWidth = SkinWidth;
            cc.minMoveDistance = 0f;
            cc.stepOffset = Mathf.Clamp(stepOffset, 0f, 1f);
            cc.slopeLimit = Mathf.Clamp(slopeLimit, 1f, 89f);
            cc.excludeLayers = ~WorldMask;
            Proxy = cc;
            SetHeight(height);
            State = default(RideState);
            State.Mode = RideMode.Ground;
            State.Heading = heading;
            Ground = default(RideGround);
            Ground.TerrainY = float.NaN;
            Ground.CollisionHeight = cc.height;
            Distance = 0f;
            MaxGap = 0f;
            Rescues = 0;
            Reseats = 0;
            _moveBroken = false;
            // Settle onto the ground (grounded before the first ride frame).
            try
            {
                CollisionFlags flags = cc.Move(Vector3.down * Mathf.Max(0.3f, cc.stepOffset));
                Ground.Grounded = (flags & CollisionFlags.Below) != 0;
            }
            catch (Exception e) { WowLog.Warn("Ride: the proxy's first Move threw " + e.Message); }
            Ground.HaveCollider = Physics.Raycast(Feet + Vector3.up * 0.5f, Vector3.down, ColliderSearch + 0.5f, WorldMask, QueryTriggerInteraction.Ignore);
            _afterMove = go.transform.position;
            VisibleY = Feet.y;
            return true;
        }

        /// <summary>The proxy's height = the mounted collision height (>= 2 x radius), bottom at the transform.</summary>
        public void SetHeight(float h)
        {
            if (Proxy == null) return;
            h = Mathf.Max(h, Proxy.radius * 2f + 0.02f);
            if (Mathf.Abs(Proxy.height - h) < 0.02f && Mathf.Abs(Proxy.center.y - h * 0.5f) < 0.01f) return;
            Proxy.height = h;
            Proxy.center = new Vector3(0f, h * 0.5f, 0f);
            Ground.CollisionHeight = h;
        }

        /// <summary>Update (-500): ground info, RideMotion.Step, Move, grounded, the step filter.</summary>
        public void Step(in RideIntent i, in RideLimits l, float dt, MountPlace place, int mapId, WowMapView map)
        {
            if (Proxy == null) return;
            Vector3 pos = Feet;
            Ground.CollisionHeight = Proxy.height;
            bool hit = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, ColliderSearch + 0.5f, WorldMask, QueryTriggerInteraction.Ignore);
            float ty;
            Ground.TerrainY = map != null && map.TryGroundHeight(pos, out ty) ? ty : float.NaN;
            // "Ground built": a collider within 60 m below, or more than 60 m above the known terrain (a fall from high up - a cliff, an air
            // dismount - is real; only unbuilt ground near the proxy holds the altitude).
            Ground.HaveCollider = hit || (!float.IsNaN(Ground.TerrainY) && pos.y - Ground.TerrainY > ColliderSearch);
            float sy = 0f;
            bool liquid = false;
            if (place != null)
            {
                try { liquid = place.TryLiquid(mapId, pos, out sy); }
                catch (Exception) { liquid = false; }
            }
            if (liquid && sy > pos.y) { Ground.LiquidDepth = sy - pos.y; Ground.LiquidSurfaceY = sy; }
            else { Ground.LiquidDepth = 0f; Ground.LiquidSurfaceY = liquid ? sy : 0f; }

            // Fell through unbuilt ground (nothing below within 60 m, under the terrain height): put it back on top.
            if (!Ground.Grounded && !Ground.HaveCollider && !float.IsNaN(Ground.TerrainY) && pos.y < Ground.TerrainY - RescueBelow)
            {
                Vector3 top = new Vector3(pos.x, Ground.TerrainY + 0.05f, pos.z);
                Rescues++;
                WowLog.Warn("Ride: the proxy was " + (Ground.TerrainY - pos.y).ToString("0.0") + " m under the terrain with no collider below - put back on top at " + top.ToString("F1") + " (fall-through rescue).");
                SetFeet(top);
                State.Velocity.Y = 0f;
                if (State.Mode == RideMode.Airborne) State.Mode = RideMode.Ground;
                pos = Feet;
            }

            RideVec d = RideMotion.Step(ref State, i, l, Ground, dt);
            Vector3 before = Proxy.transform.position;
            if (!_moveBroken)
            {
                try
                {
                    CollisionFlags flags = Proxy.Move(new Vector3(d.X, d.Y, d.Z));
                    Ground.Grounded = (flags & CollisionFlags.Below) != 0;
                }
                catch (Exception e)
                {
                    _moveBroken = true;
                    WowLog.Error("Ride: the proxy's Move threw " + e + " - the mount stands still.");
                }
            }
            Vector3 after = Proxy.transform.position;
            _afterMove = after;
            Vector3 moved = after - before;
            moved.y = 0f;
            Distance += moved.magnitude;
            ActualSpeed = dt > 1e-5f ? moved.magnitude / dt : 0f;
            float proxyY = after.y - SkinWidth;
            VisibleY = RideMotion.FilterY(VisibleY, proxyY, State.Velocity.PlanarLength, State.Mode != RideMode.Ground, dt);
            float gap = Mathf.Abs(proxyY - VisibleY);
            if (gap > MaxGap) MaxGap = gap;
        }

        /// <summary>Moves the proxy (off, write, on - a CharacterController ignores plain transform writes) and snaps the visible height.</summary>
        public void SetFeet(Vector3 feet)
        {
            if (Proxy == null) return;
            Proxy.enabled = false;
            Proxy.transform.position = feet + Vector3.up * SkinWidth;
            Proxy.enabled = true;
            _afterMove = Proxy.transform.position;
            VisibleY = feet.y;
        }

        /// <summary>
        /// WowSpace.Rebased: WowOrigin has shifted the proxy (a child of the WoW Game object) and synced physics. Only our own
        /// Unity-space values move here; the proxy is checked (residual > 1 cm = it was not shifted: re-seated, logged).
        /// </summary>
        public void OnRebased(Vector3 delta)
        {
            if (Proxy == null) return;
            Vector3 expected = _afterMove - delta;
            float residual = (Proxy.transform.position - expected).magnitude;
            if (residual > ReseatResidual)
            {
                Reseats++;
                WowLog.Warn("Ride: after the rebase the proxy was " + residual.ToString("0.000") + " m from where it should be - re-seated (enabled off/on).");
                Proxy.enabled = false;
                Proxy.transform.position = expected;
                Proxy.enabled = true;
            }
            _afterMove = Proxy.transform.position;
            VisibleY -= delta.y;
        }

        /// <summary>The distance / gap counters for the 30 s log, reset.</summary>
        public void TakeStats(out float distance, out float maxGap)
        {
            distance = Distance;
            maxGap = MaxGap;
            Distance = 0f;
            MaxGap = 0f;
        }

        public void Destroy()
        {
            if (Proxy != null) UnityEngine.Object.Destroy(Proxy.gameObject);
            Proxy = null;
        }
    }
}
