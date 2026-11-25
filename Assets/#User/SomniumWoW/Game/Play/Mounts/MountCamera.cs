using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Mounts
{
    /// <summary>
    /// The desktop chase camera while riding (iteration 7, owner Ride; PLAN7 s5.3 "Desktop camera", s4.8 numbers), the Euro Truck Sim
    /// way (EtsTruckCamera.cs): it owns no camera - each frame, after everything else (LateUpdate order 10000: after the client's
    /// CinemachineBrain slot at 100 and after GearPoser / VRIK), it places whichever camera renders (Camera.main, else the first
    /// enabled camera without a targetTexture - never the WoW Map's RenderTexture camera). The client's CinemachineBrain is paused by
    /// name while it has the camera (ETS HoldBrain 262-275, so nothing needs the Cinemachine assembly) and handed back on release
    /// (ETS GiveBrainBack; the client's orbit then jumps to its own pose - one accepted cut, logged).
    /// It starts from the client camera's own pose (orbit yaw / pitch / distance computed from it, its rotation offset eased out), so
    /// the first frame is identical - no cut. Then: target = the saddle + 0.9 m; a critically damped yaw spring (omega 6/s) behind the
    /// mount's heading; right mouse: the mouse X turn of the mount turns the camera with it, mouse Y = pitch -30..70 (ETS
    /// MouseSensitivity 3); wheel = distance 2-25 m (smoothed exp 10/s); a SphereCast (r 0.25, layer 0) pulls in at once and lets out
    /// at exp 4/s; kept above the ground under it. Heading only: it never rolls or pitches with the mount; the field of view is
    /// untouched. Nothing at all in VR ("in a headset the camera is the headset's").
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class MountCamera : MonoBehaviour
    {
        // ---- ours (PLAN7 s4.8; WowMount logs them once) ----------------------------------------------------------------------------
        public const float TargetAboveSaddle = 0.9f;
        public const float YawOmega = 6f;                 // 1/s, critically damped (SmoothDampAngle smoothTime = 2 / omega)
        public const float MouseDegPerUnit = 3f;          // ETS EtsTruckCamera.MouseSensitivity = 3f
        public const float PitchMin = -30f, PitchMax = 70f;
        public const float DistMin = 2f, DistMax = 25f;
        public const float WheelStep = 0.85f;             // distance x 0.85 per wheel notch in, / 0.85 out
        public const float DistSmooth = 10f, LetOut = 4f, CastRadius = 0.25f, WallGap = 0.05f;
        public const float GroundClearance = 0.5f;
        public const float StartSettle = 4f;              // 1/s: the client camera's own rotation offset eases out
        private const int WorldMask = 1 << 0;

        public bool On { get; private set; }
        /// <summary>The camera's pitch (+ = above, looking down), degrees - the climb angle while the right mouse steers a flyer.</summary>
        public float Pitch { get; private set; } = 15f;

        private Camera _cam;
        private Behaviour _brain;
        private bool _initialised;
        private float _yaw, _yawVel, _dist, _wantDist, _cur;
        private Quaternion _settle = Quaternion.identity;
        private Vector3 _target;
        private float _heading, _mouseTurn;
        private bool _haveTarget;
        private bool _loggedStart;
        private bool _broken;

        /// <summary>WowMount, LateUpdate -500: the saddle, the heading, and the mount's mouse turn this frame (the camera turns with it).</summary>
        public void SetTarget(Vector3 saddle, float headingDeg, float mouseTurnDeg)
        {
            _target = saddle + Vector3.up * TargetAboveSaddle;
            _heading = headingDeg;
            _mouseTurn += mouseTurnDeg;
            _haveTarget = true;
        }

        /// <summary>Takes the view (desktop): the brain is paused at once; the orbit starts from the camera's pose on the first LateUpdate.</summary>
        public void Begin()
        {
            if (WowPlayer.XrPresent || _broken) return;
            On = true;
            _initialised = false;
            _loggedStart = false;
            _haveTarget = false;
            _mouseTurn = 0f;
            FindCamera();
            HoldBrain();
        }

        /// <summary>Hands the camera back (the brain on again).</summary>
        public void End(string why)
        {
            if (!On) return;
            On = false;
            bool had = _brain != null;
            GiveBrainBack();
            _haveTarget = false;
            WowLog.Info("Ride: chase camera off (" + why + ") - " + (had ? "the client's CinemachineBrain is back (its orbit jumps to its own pose: one accepted cut)" : "no brain was paused") + ".");
        }

        private void OnDisable()
        {
            if (On) End("the camera component was disabled");
        }

        private void LateUpdate()
        {
            if (!On || _broken) return;
            if (WowPlayer.XrPresent) { End("a headset came up"); return; }
            if (!_haveTarget) return;
            try { Place(Time.deltaTime); }
            catch (Exception e)
            {
                _broken = true;
                WowLog.Error("Ride: the chase camera threw " + e + " - handing the camera back.");
                End("error");
            }
        }

        private void Place(float dt)
        {
            if (_cam == null || !_cam.isActiveAndEnabled || _cam.targetTexture != null)
            {
                FindCamera();
                if (_cam == null) return;
                _initialised = false;
            }
            HoldBrain();
            Transform ct = _cam.transform;
            if (!_initialised)
            {
                // The client camera's own pose (the brain placed it last frame and is paused now): the orbit that reproduces it.
                Vector3 off = ct.position - _target;
                float d = off.magnitude;
                if (d < 0.1f) { off = Quaternion.Euler(0f, _heading, 0f) * new Vector3(0f, 2f, -6f); d = off.magnitude; }
                Vector3 look = -off / d;
                _yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(-look.y, -1f, 1f)) * Mathf.Rad2Deg, PitchMin, PitchMax);
                _yawVel = 0f;
                _dist = d;
                _cur = d;
                _wantDist = Mathf.Clamp(d, DistMin, DistMax);
                _settle = ct.rotation * Quaternion.Inverse(Quaternion.LookRotation(look, Vector3.up));
                _initialised = true;
                _mouseTurn = 0f;
                if (!_loggedStart)
                {
                    _loggedStart = true;
                    WowLog.Info("Ride: desktop chase camera on (brain paused: " + (_brain != null ? "yes" : "not found") + ", camera '" + _cam.name + "'), start distance " + d.ToString("0.0") +
                                " m, pitch " + Pitch.ToString("0") + " deg, from the client camera's own pose.");
                }
            }

            if (Input.GetMouseButton(1))
            {
                float my = Input.GetAxisRaw("Mouse Y");
                if (!float.IsNaN(my)) Pitch = Mathf.Clamp(Pitch - my * MouseDegPerUnit, PitchMin, PitchMax);
            }
            _yaw += _mouseTurn;   // the right-mouse turn of the mount turns the camera with it (WoW's right-drag)
            _mouseTurn = 0f;
            float wheel = Ui.UiMinimap.OwnsWheel() ? 0f : Input.mouseScrollDelta.y;   // lead 2026-10-03: the wheel over the minimap zooms it
            if (Mathf.Abs(wheel) > 0.01f) _wantDist = Mathf.Clamp(_wantDist * Mathf.Pow(WheelStep, wheel), DistMin, DistMax);
            _dist += (_wantDist - _dist) * (1f - Mathf.Exp(-DistSmooth * dt));
            _yaw = Mathf.SmoothDampAngle(_yaw, _heading, ref _yawVel, 2f / YawOmega, Mathf.Infinity, Mathf.Max(dt, 1e-5f));

            Quaternion orbit = Quaternion.Euler(Pitch, _yaw, 0f);
            Vector3 back = orbit * Vector3.back;
            float allowed = _dist;
            RaycastHit hit;
            if (Physics.SphereCast(_target, CastRadius, back, out hit, _dist, WorldMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.3f, hit.distance - WallGap);
            if (allowed < _cur) _cur = allowed;                                   // a wall between: pull in at once
            else _cur += (allowed - _cur) * (1f - Mathf.Exp(-LetOut * dt));       // let out gently
            Vector3 pos = _target + back * _cur;
            // Keep above the ground under the camera (ETS 378); cast from the target's height so a roof over the rider is not "ground".
            Vector3 from = new Vector3(pos.x, Mathf.Max(pos.y, _target.y) + 0.5f, pos.z);
            if (Physics.Raycast(from, Vector3.down, out hit, 60f, WorldMask, QueryTriggerInteraction.Ignore) && hit.point.y + GroundClearance > pos.y)
                pos.y = hit.point.y + GroundClearance;
            _settle = Quaternion.Slerp(_settle, Quaternion.identity, 1f - Mathf.Exp(-StartSettle * dt));
            Vector3 toTarget = _target - pos;
            Quaternion rot = toTarget.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toTarget, Vector3.up) : ct.rotation;
            ct.SetPositionAndRotation(pos, _settle * rot);
        }

        private void FindCamera()
        {
            Camera c = Camera.main;
            if (c == null || !c.isActiveAndEnabled || c.targetTexture != null)
            {
                c = null;
                foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (candidate.isActiveAndEnabled && candidate.targetTexture == null) { c = candidate; break; }
                }
            }
            if (c != _cam) GiveBrainBack();
            _cam = c;
        }

        private void HoldBrain()
        {
            if (_cam == null) return;
            if (_brain == null || _brain.gameObject != _cam.gameObject)
            {
                GiveBrainBack();
                _brain = _cam.GetComponent("CinemachineBrain") as Behaviour;
            }
            if (_brain != null && _brain.enabled) _brain.enabled = false;
        }

        private void GiveBrainBack()
        {
            if (_brain != null && !_brain.enabled) _brain.enabled = true;
            _brain = null;
        }
    }
}
