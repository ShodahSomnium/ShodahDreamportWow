using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Game.Entities;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// Far target selection in VR ("if you can see it you can select it", as on the desktop) along the CLIENT's own laser
    /// (stage S1, the user: "use the same laser for selecting, at the moment there is a separate yellow dot and it is very
    /// confusing"). Nothing of ours is drawn along the ray any more - no line, no end dot: the aim is VrClientLaser's ray (the
    /// Somnium pointer's far caster, from the same stabilized origin the client's own raycast uses). Along it a thin ray (the
    /// world on layers 0 and 14 stops it, as the desktop click's mask) and, up to that point, sphere casts against the entity
    /// boxes (WowEntityClick.Layer 16, WowEntityViews.TryGetGuid) whose radius grows with distance in steps (about 1.1 degrees:
    /// 0.05 m at the hand, 0.85 m at 40 m, 2.8 m at 140 m) up to WowSettings.VrTargetRange.
    /// The entity pointed at is highlighted, in the spirit of WoW's mouse-over: its nameplate shows with WoW's own
    /// Nameplate-Glow (WowEntityViews.OnEntityHover -> WowNameplate) and a soft white glow lies on the model (ours, no
    /// outline, no colour). When the client's laser itself is on an entity (its 5 m reach) that entity is the one
    /// highlighted - the client draws its line to it and gives its hover tick; beyond its reach (where the client draws no
    /// line) a hover tick of the client's own strength marks each new entity.
    /// </summary>
    internal sealed class VrTargetRay
    {
        public const int EntityMask = 1 << WowEntityClick.Layer;
        public const int WorldMask = (1 << 0) | (1 << 14);
        private static readonly float[] Steps = { 0f, 4f, 12f, 30f, 70f, 140f, 220f };
        private const float Skip = 0.35f;   // the occluder ray starts this far out: never the player's own hand/body

        /// <summary>Ours: strength (additive alpha) of the soft glow on the highlighted unit; 0 = the nameplate glow only.</summary>
        public const float GlowAlpha = 0.2f;
        /// <summary>Ours: the glow's size as a multiple of the model's larger extent (height or footprint).</summary>
        public const float GlowSize = 1.25f;
        /// <summary>The client's own hover tick (Near-Far Interactor Right.prefab m_HoverEnteredData: amplitude 0.1, duration 0.02),
        /// played by us only for hovers beyond the client's reach (it plays its own within it).</summary>
        public const float HoverTickAmplitude = 0.1f, HoverTickSeconds = 0.02f;
        /// <summary>Ours: at most one far hover tick per this many seconds (a hand wavering between two close units does not buzz).</summary>
        public const float HoverTickGap = 0.1f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly VrGlow _glow;
        private float _lastTick = -10f;

        public ulong Hovered { get; private set; }
        public float HoveredDistance { get; private set; }

        public VrTargetRay()
        {
            _glow = new VrGlow("WoW VR laser hover", 4);
            _line = new VrLine("WoW VR target laser");
        }

        // Lead (2026-10-03, the user: "add a 50% transparent laser when targeting, it is very hard to select mobs"): beyond its own 5 m the
        // client draws no line, so the far targeting draws ours - the proven iteration-4 VrLine (VrArt.Beam, alpha blend) - from the
        // client's laser origin to what it meets (a unit, the ground) or to the targeting range. White at 50 % (ours); a little warmer
        // while it is on a unit, so the hover reads at a glance.
        public const float LaserAlpha = 0.5f;
        private readonly VrLine _line;

        public void Laser(Vector3 from, Vector3 to, bool onUnit)
        {
            Color near = onUnit ? new Color(1f, 0.92f, 0.6f, LaserAlpha) : new Color(1f, 1f, 1f, LaserAlpha);
            Color far = new Color(near.r, near.g, near.b, LaserAlpha * 0.8f);
            _line.Set(from, to, near, far);
        }

        /// <summary>The entity the ray meets (0 none), the point on it and its distance; blocked = the world stopped the ray first.</summary>
        public ulong Cast(Ray ray, float range, out Vector3 end, out float distance, out bool blocked)
        {
            float limit = range;
            blocked = false;
            RaycastHit h;
            if (Physics.Raycast(ray.origin + ray.direction * Skip, ray.direction, out h, Mathf.Max(0.01f, range - Skip), WorldMask, QueryTriggerInteraction.Ignore))
            {
                limit = h.distance + Skip;
                blocked = true;
            }
            end = ray.origin + ray.direction * limit;
            distance = limit;
            for (int i = 0; i < Steps.Length; i++)
            {
                float s = Steps[i];
                if (s >= limit) break;
                float e = i + 1 < Steps.Length ? Mathf.Min(Steps[i + 1], limit) : limit;
                float radius = 0.05f + s * 0.02f;
                int n = Physics.SphereCastNonAlloc(ray.origin + ray.direction * s, radius, ray.direction, _hits, e - s, EntityMask, QueryTriggerInteraction.Ignore);
                ulong best = 0;
                float bestD = float.MaxValue;
                Vector3 bestP = Vector3.zero;
                for (int k = 0; k < n; k++)
                {
                    ulong g;
                    if (!WowEntityViews.TryGetGuid(_hits[k].collider, out g)) continue;
                    // A box the sphere already overlapped at its start reports distance 0 and no point: use its centre.
                    float d = s + _hits[k].distance;
                    Vector3 p = _hits[k].distance > 0f ? _hits[k].point : _hits[k].collider.bounds.center;
                    if (d < bestD) { bestD = d; best = g; bestP = p; }
                }
                if (best != 0)
                {
                    end = bestP;
                    distance = bestD;
                    return best;
                }
            }
            return 0;
        }

        /// <summary>
        /// Highlights guid (0 = none) and remembers it as hovered: the hover flag (its nameplate with WoW's glow) and the soft
        /// glow on its model. clientGuid = the entity the client's own laser hovers this frame (0 none): the client already
        /// flags it and ticks for it, so we never clear its flag and never tick for it.
        /// </summary>
        public void Highlight(IWowSession s, ulong guid, float distance, ulong clientGuid, Vector3 eye, WowEntityViews views)
        {
            bool changed = guid != Hovered;
            SetHover(guid, views, clientGuid);
            HoveredDistance = guid != 0 ? distance : 0f;
            if (changed && guid != 0 && guid != clientGuid && Time.unscaledTime - _lastTick > HoverTickGap)
            {
                _lastTick = Time.unscaledTime;
                GearXr.Pulse(true, HoverTickAmplitude, HoverTickSeconds);
            }

            EntityState e = guid != 0 && s != null ? s.Find(guid) : null;
            if (e == null || GlowAlpha <= 0f) { _glow.Show(false); return; }
            Transform root = WowEntityViews.GetView(guid);
            float height, footprint;
            if (root == null || !WowEntityViews.TryGetSize(guid, out height, out footprint)) { _glow.Show(false); return; }
            if (e.Dead) height *= 0.5f;   // a corpse lies down (WowEntityViews places its plate at half the standing top too)
            height = Mathf.Max(0.8f, height);
            Vector3 c = root.position + Vector3.up * (height * 0.5f);
            float a = e.Dead ? GlowAlpha * 0.7f : GlowAlpha;
            _glow.Set(c, eye, Mathf.Max(height, footprint) * GlowSize, new Color(1f, 1f, 1f, a));
        }

        /// <summary>Nothing highlighted (the ray is off this frame). The client's own hover flag stays.</summary>
        public void Hide(WowEntityViews views)
        {
            SetHover(0, views, VrClientLaser.HoveredEntity);
            _glow.Show(false);
            _line.Show(false);
        }

        /// <summary>
        /// The hover flag on the entity (its nameplate lights up). The client's own XRI hover writes the same flag
        /// (WowEntityClick hoverEntered / hoverExited -> WowEntityViews.OnEntityHover): ours is re-asserted every frame (an
        /// idempotent flag write) so the client's hover-exit on an entity our ray still points at does not blank it, and the
        /// entity the client still hovers is never un-flagged by us.
        /// </summary>
        private void SetHover(ulong guid, WowEntityViews views, ulong clientGuid)
        {
            if (views == null) { Hovered = guid; return; }
            if (guid != Hovered)
            {
                if (Hovered != 0 && Hovered != clientGuid) views.OnEntityHover(Hovered, false);
                Hovered = guid;
            }
            if (guid != 0) views.OnEntityHover(guid, true);
        }

        public void Destroy()
        {
            _glow.Destroy();
            _line.Destroy();
        }
    }

    /// <summary>
    /// The client's own laser on the right hand (stage S1): which XRI interactor it is, its ray, and what it is on this frame.
    /// Facts (client source, E:/Somnium Space/Main Build; XRI 3.3.2):
    /// - the laser is the Near-Far interactor XRAdditionalHolder.GetRightPointerRay ('Near-Far Interactor Right.prefab'): far
    ///   caster m_CastDistance 5, raycast, m_EnableStabilization 1, m_RaycastMask 8471649 = layers 0,5,6,10,14,16,23;
    ///   m_InteractionLayers 144 = 'XRInteractable' (4) + 'XRRay' (7); UI interaction on. Its CurveVisualController has
    ///   m_RestingVisualLineLength 0 and m_LineDynamicsMode 1 (RetractOnHitLoss): the client draws its line only while it
    ///   hovers an interactable (our entity boxes are XRSimpleInteractables on layer 4) or UI within those 5 m.
    /// - a second right-hand Near-Far interactor, 'Grab Right' (far m_CastDistance 1, mask 8388737 = layers 0,7,23,
    ///   interaction layers 'Grabbable' (3)), sits at the same spot: O2 picked it (the user's Player.log: "target ray aims along
    ///   the client's Near-Far interactor 'Grab Right'"). The laser is now told apart by its far caster: the one whose raycast
    ///   reaches our entity layer 16 with the 'XRInteractable' layer, then the longest reach.
    /// - the ray: NearFarInteractor.curveOrigin = farInteractionCaster.effectiveCastOrigin (the stabilized anchor the caster
    ///   raycasts from), so our far cast leaves the hand exactly where the client's does.
    /// - inspector mode (XRInspectorFeature.InspectorEnabled, the client's X-hold) sets the pointers' interactionLayers to 400
    ///   (XRInspector.prefab) = bits 4, 7 and 8 'Inspectable' (XRI/Settings/Resources/InteractionLayerSettings.asset) and
    ///   back to the normal 144 when it ends: bit 8 on the laser = the client's inspector owns the laser and the trigger.
    /// Not a single client setting is changed (PLAN s2.10: never modify the avatar); everything here only reads.
    /// </summary>
    internal static class VrClientLaser
    {
        /// <summary>The client's interaction layer 'Inspectable' (bit 8) - on the laser only in the client's inspector mode.</summary>
        public const int InspectableLayerBit = 1 << 8;
        /// <summary>The client's interaction layer 'XRInteractable' (bit 4) - the one our entity interactables carry (WowEntityClick).</summary>
        public const int XrInteractableLayerBit = 1 << 4;
        private const float NearHandSq = 0.25f;   // squared metres: an interactor never sits half a metre from its own hand
        private const float SearchEvery = 2f;     // ours: seconds between searches while the laser is not known or not active

        private static NearFarInteractor _nearFar;
        private static bool _nearFarIsLaser;      // its far caster reaches our entity layer (the Somnium pointer, not the grab)
        private static XRRayInteractor _rayInteractor;
        private static bool _everHadLaser;        // the laser was seen this session: when it goes inactive, the client turned it off
        private static float _nextSearch;
        private static string _choice = "";
        private static int _logs, _stateLogs, _warns;   // log caps (ours): 20 aim / on-off lines, 30 laser-state lines, 5 warnings

        private static int _sensedFrame = -1;
        private static ulong _entity;
        private static bool _busy, _inspector, _hasEnd;
        private static Vector3 _end;
        private static string _busyWhy = "";
        private static string _saidState = "";

        /// <summary>True when the client's laser was seen and no laser is active now (the client switched its pointer off).</summary>
        public static bool LaserOff { get; private set; }

        /// <summary>What the aim is, for log lines ("the client's laser 'Near-Far Interactor Right' ...").</summary>
        public static string Describe { get { return _choice; } }

        /// <summary>
        /// The ray of the client's interactor on the right hand: its laser (preferred), else another Near-Far interactor or an
        /// XRRayInteractor there (O2's fallbacks, only while no laser was ever found). False = none active (callers fall back to
        /// the hand's forward unless LaserOff).
        /// </summary>
        public static bool TryGetLaser(Transform right, out Ray ray)
        {
            ray = default(Ray);
            if (right == null) return false;
            Validate(right);
            if (_nearFar == null && _rayInteractor == null) Search(right);
            if (_nearFar != null)
            {
                Transform o = null;
                try
                {
                    ICurveInteractionCaster far = _nearFar.farInteractionCaster;
                    if (far != null) o = far.effectiveCastOrigin != null ? far.effectiveCastOrigin : far.castOrigin;
                }
                catch (Exception) { o = null; }
                if (o == null) o = _nearFar.transform;
                ray = new Ray(o.position, o.forward);
                return true;
            }
            if (_rayInteractor != null)
            {
                Transform o = _rayInteractor.rayOriginTransform != null ? _rayInteractor.rayOriginTransform : _rayInteractor.transform;
                ray = new Ray(o.position, o.forward);
                return true;
            }
            return false;
        }

        /// <summary>The entity (guid) the client's laser hovers this frame, 0 none.</summary>
        public static ulong HoveredEntity { get { Sense(); return _entity; } }

        /// <summary>The laser is on something of the client's own (its UI, one of its interactables, or it holds something):
        /// that trigger press is the client's.</summary>
        public static bool Busy { get { Sense(); return _busy; } }

        /// <summary>Lead fix 2026-10-10 (the user: "if I select distract, I cannot press trigger on the floor, nothing happens"; Player.log: "the
        /// client's laser is on a Somnium interactable" - the floor counts): busy only because it holds something or is on a Somnium UI panel -
        /// what a ground aim must give way to; a far interactable under the laser (the floor) does not stop placing a spell.</summary>
        public static bool BusyHoldOrUi { get { Sense(); return _busy && _busyWhy != "is on a Somnium interactable"; } }

        /// <summary>The client's inspector mode is on (the laser carries the 'Inspectable' layer): the laser and the trigger are its.</summary>
        public static bool Inspector { get { Sense(); return _inspector; } }

        /// <summary>Where the client's laser ends on the hovered entity (its raycast hit), when it has one.</summary>
        public static bool TryGetEnd(out Vector3 end)
        {
            Sense();
            end = _end;
            return _hasEnd;
        }

        /// <summary>Reads the laser's state once per frame (after the XRI interaction manager has updated it).</summary>
        private static void Sense()
        {
            if (_sensedFrame == Time.frameCount) return;
            _sensedFrame = Time.frameCount;
            _entity = 0;
            _busy = false;
            _inspector = false;
            _hasEnd = false;
            _busyWhy = "";
            try
            {
                if (_nearFar != null && _nearFar.isActiveAndEnabled)
                {
                    _inspector = (_nearFar.interactionLayers.value & InspectableLayerBit) != 0;
                    bool other = ReadHovered(_nearFar.interactablesHovered);
                    Vector3 end;
                    EndPointType t = _nearFar.TryGetCurveEndPoint(out end);
                    if (_entity != 0 && t == EndPointType.ValidCastHit) { _end = end; _hasEnd = true; }
                    if (_nearFar.hasSelection) { _busy = true; _busyWhy = "holds something"; }
                    else if (t == EndPointType.UI) { _busy = true; _busyWhy = "is on a Somnium UI panel"; }
                    // Only a FAR valid hit counts (the client draws its line to that interactable); a near hover at the hand does not.
                    else if (other && _entity == 0 && t == EndPointType.ValidCastHit) { _busy = true; _busyWhy = "is on a Somnium interactable"; }
                }
                else if (_rayInteractor != null && _rayInteractor.isActiveAndEnabled)
                {
                    bool other = ReadHovered(_rayInteractor.interactablesHovered);
                    if (_rayInteractor.hasSelection) { _busy = true; _busyWhy = "holds something"; }
                    else if (other && _entity == 0) { _busy = true; _busyWhy = "is on a Somnium interactable"; }
                }
            }
            catch (Exception e)
            {
                _entity = 0;
                _busy = false;
                _inspector = false;
                _hasEnd = false;
                if (_warns < 5) { _warns++; WowLog.Warn("VR input: reading the client's laser failed: " + e.GetType().Name + ": " + e.Message); }
            }
            string state = _inspector ? "inspector" : _busy ? _busyWhy : "";
            if (state != _saidState)
            {
                _saidState = state;
                if (_stateLogs < 30)
                {
                    _stateLogs++;
                    if (_inspector) WowLog.Info("VR input: the client's inspector mode is on (its laser carries the 'Inspectable' layer) - far targeting paused.");
                    else if (_busy) WowLog.Info("VR input: the client's laser " + _busyWhy + " - that trigger press is the client's.");
                    else WowLog.Info("VR input: the client's laser is free again - far targeting on.");
                }
            }
        }

        /// <summary>Our entity among the interactables the client hovers (-> _entity); true when it also hovers something else.</summary>
        private static bool ReadHovered(List<IXRHoverInteractable> hovered)
        {
            bool other = false;
            if (hovered == null) return false;
            for (int i = 0; i < hovered.Count; i++)
            {
                Component c = hovered[i] as Component;
                if (c == null) continue;
                WowEntityClick click;
                ulong g;
                if (c.TryGetComponent(out click) && click != null && click.Box != null && WowEntityViews.TryGetGuid(click.Box, out g) && g != 0)
                {
                    if (_entity == 0) _entity = g;
                }
                else other = true;
            }
            return other;
        }

        /// <summary>Drops a cached interactor that is gone, off or away from the hand; LaserOff when the laser itself went off.</summary>
        private static void Validate(Transform right)
        {
            if (_nearFar != null && (!_nearFar.isActiveAndEnabled || (_nearFar.transform.position - right.position).sqrMagnitude > NearHandSq))
            {
                _nearFar = null;
                _nearFarIsLaser = false;
            }
            if (_rayInteractor != null && (!_rayInteractor.isActiveAndEnabled || (_rayInteractor.transform.position - right.position).sqrMagnitude > NearHandSq)) _rayInteractor = null;
            // A fallback (the grab interactor, a ray interactor) is re-checked every SearchEvery: the laser may come up later.
            if (_nearFar != null && !_nearFarIsLaser && Time.unscaledTime > _nextSearch) Search(right);
            bool off = _everHadLaser && !_nearFarIsLaser;
            if (off)
            {
                // The client's laser was here and is not now: aim along nothing of our own choosing (the user wants THE laser).
                _nearFar = null;
                _rayInteractor = null;
            }
            if (off != LaserOff)
            {
                LaserOff = off;
                if (_logs < 20)
                {
                    _logs++;
                    WowLog.Info(off ? "VR input: the client's laser is off (its pointer interactor is disabled or gone) - no far targeting until it is back."
                                    : "VR input: the client's laser is back.");
                }
            }
        }

        /// <summary>Finds the laser among the Near-Far interactors near the right hand (else a ray interactor), every SearchEvery.</summary>
        private static void Search(Transform right)
        {
            if (Time.unscaledTime < _nextSearch) return;
            _nextSearch = Time.unscaledTime + SearchEvery;
            NearFarInteractor best = null;
            bool bestIsLaser = false;
            float bestReach = -1f, bestD = NearHandSq;
            string others = "";
            try
            {
                NearFarInteractor[] all = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    NearFarInteractor c = all[i];
                    if (c == null || !c.isActiveAndEnabled) continue;
                    float d = (c.transform.position - right.position).sqrMagnitude;
                    if (d >= NearHandSq) continue;
                    float reach;
                    bool laser = IsLaser(c, out reach);
                    others += (others.Length > 0 ? ", " : "") + "'" + c.name + "' (far reach " + reach.ToString("0.#") + " m, " + (laser ? "reaches the entity layer" : "not the entity layer") + ")";
                    bool better = best == null
                        || (laser && !bestIsLaser)
                        || (laser == bestIsLaser && (reach > bestReach + 0.01f || (Mathf.Abs(reach - bestReach) <= 0.01f && d < bestD)));
                    if (!better) continue;
                    best = c;
                    bestIsLaser = laser;
                    bestReach = reach;
                    bestD = d;
                }
            }
            catch (Exception e)
            {
                if (_warns < 5) { _warns++; WowLog.Warn("VR input: searching the client's laser failed: " + e.Message); }
            }
            if (bestIsLaser) _everHadLaser = true;
            // After the laser was seen, only the laser counts (no aiming along the grab interactor again).
            if (best != null && (bestIsLaser || !_everHadLaser))
            {
                _nearFar = best;
                _nearFarIsLaser = bestIsLaser;
                _rayInteractor = null;
            }
            else if (!_everHadLaser && _nearFar == null)
            {
                try
                {
                    XRRayInteractor[] rays = UnityEngine.Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    float rd = NearHandSq;
                    for (int i = 0; rays != null && i < rays.Length; i++)
                    {
                        XRRayInteractor c = rays[i];
                        if (c == null || !c.isActiveAndEnabled) continue;
                        float d = (c.transform.position - right.position).sqrMagnitude;
                        if (d < rd) { rd = d; _rayInteractor = c; }
                    }
                }
                catch (Exception) { _rayInteractor = null; }
            }
            string choice = _nearFar != null
                ? (_nearFarIsLaser ? "the client's laser, Near-Far interactor '" : "the client's Near-Far interactor '") + _nearFar.name + "' (far caster, stabilized origin"
                  + (_nearFarIsLaser ? "" : "; it does not reach the entity layer - no laser found yet") + ")"
                : _rayInteractor != null ? "the client's ray interactor '" + _rayInteractor.name + "' (no Near-Far interactor near the right hand)"
                : "";
            if (choice.Length > 0 && choice != _choice)
            {
                _choice = choice;
                if (_logs < 20) { _logs++; WowLog.Info("VR input: target ray aims along " + choice + (others.Length > 0 ? "; near the right hand: " + others : "") + "."); }
            }
        }

        /// <summary>The Somnium pointer: far casting on, a CurveInteractionCaster whose raycast mask holds our entity layer, and the
        /// 'XRInteractable' interaction layer our entity interactables carry. reach = its far cast distance (0 unknown).</summary>
        private static bool IsLaser(NearFarInteractor c, out float reach)
        {
            reach = 0f;
            try
            {
                CurveInteractionCaster far = c.farInteractionCaster as CurveInteractionCaster;
                if (far == null) return false;
                reach = far.castDistance;
                return c.enableFarCasting
                       && (far.raycastMask.value & (1 << WowEntityClick.Layer)) != 0
                       && (c.interactionLayers.value & XrInteractableLayerBit) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
