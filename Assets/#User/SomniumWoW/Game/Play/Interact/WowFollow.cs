// iteration 11 - owner interact (proposal-interact.md 6.3.9): WoW's auto-follow (UnitPopup FOLLOW -> FollowUnit, /f /follow /fol) for a
// Somnium avatar. In WoW following is CLIENT-side (the client walks its own character and sends normal movement); here the Somnium avatar IS
// the WoW character, so this component walks the avatar's CharacterController after the leader while the pure Play/Interact/FollowRules say
// walk / hold / stop. Our position then goes to the realm as usual (the session's SetPlayerPosition from the avatar). Added by WowGame after
// UiGroup; runs after WowForcedMove (-450) and before WowGame's pump.
// Desktop: the avatar turns the way it walks (12 /s smoothing like WowEntityView - ours). VR: position only - the view never turns (comfort
// rule); any own movement (keys, thumbstick, teleport) ends the follow at once (FollowRules.OwnMoveMetres).
using System;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Interact;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Interact
{
    [DefaultExecutionOrder(-440)]
    public sealed class WowFollow : MonoBehaviour
    {
        private const float TurnRate = 12f;   // ours: desktop yaw smoothing per second (WowEntityView's)

        private static ulong _guid;
        private static string _name;
        private static bool _constantsLogged;
        private static int _startLogs;

        private CharacterController _cc;
        private Transform _ccFor;
        private Vector3 _lastPos;
        private bool _haveLast, _walking;
        private bool _ccMissingLogged;

        /// <summary>True while following someone.</summary>
        public static bool Active { get { return _guid != 0; } }

        /// <summary>The unit being followed, 0 = none.</summary>
        public static ulong Leader { get { return _guid; } }

        /// <summary>FollowUnit: starts following a friendly player within 28 yd (FollowRules.CanStart); its error goes out as an Error event.
        /// Following another unit replaces the old follow without the stop text (WoW prints only the new AUTOFOLLOWSTART).</summary>
        public static bool Begin(ulong guid)
        {
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (p == null) return false;
            EntityState e = guid != 0 ? s.Find(guid) : null;
            float yards = e != null ? Yards(p.Pos, e.Pos) : float.MaxValue;
            string err = FollowRules.CanStart(e != null && e.Kind == EntityKind.Player, e != null && e.Reaction == Reaction.Friendly, guid == p.Guid,
                                              yards, p.Dead || p.Ghost, Busy(p));
            if (err != null)
            {
                WowLog.Info("Follow: refused (" + err + ") for " + (e != null ? e.Name : "0x" + guid.ToString("X")) + ".");
                WowPlay.Publish(new GameEvent { Type = GameEventType.Error, Text = err });
                return false;
            }
            if (!_constantsLogged)
            {
                _constantsLogged = true;
                WowLog.Info("Follow: ours - start within " + FollowRules.StartRange + " yd, ends beyond " + FollowRules.BreakRange + " yd, holds " +
                            FollowRules.HoldDistance + " yd behind, walks again beyond " + FollowRules.ResumeDistance + " yd, own movement over " +
                            FollowRules.OwnMoveMetres + " m a frame ends it, desktop turn " + TurnRate + " /s, VR position only.");
            }
            _guid = guid;
            _name = e.Name;
            if (++_startLogs <= 20 || _startLogs % 50 == 0) WowLog.Info("Follow: following " + _name + " (" + yards.ToString("0.0") + " yd).");
            WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = "Following " + _name + "." });   // AUTOFOLLOWSTART (GlobalStrings.lua:642)
            return true;
        }

        /// <summary>Ends the follow: AUTOFOLLOWSTOP "You stop following %s." (GlobalStrings.lua:643) + the reason in the log.</summary>
        public static void Stop(string why)
        {
            if (_guid == 0) return;
            string n = _name ?? "?";
            _guid = 0;
            _name = null;
            WowLog.Info("Follow: stopped following " + n + " (" + why + ").");
            WowPlay.Publish(new GameEvent { Type = GameEventType.Info, Text = "You stop following " + n + "." });
        }

        private static bool Busy(PlayerState p)
        {
            // ERR_TOOBUSYTOFOLLOW: on a taxi, or a ride / forced move / vehicle owns the avatar root (ours: those write the root themselves)
            return p.OnTaxi || WowMount.OwnsAvatar || WowForcedMove.OwnsAvatar || WowVehicle.OwnsAvatar;
        }

        private static float Yards(WowPos a, WowPos b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void OnDisable()
        {
            if (_guid != 0) Stop("the game ended");
            _haveLast = false;
            _walking = false;
        }

        private void Update()
        {
            if (_guid == 0) { _haveLast = false; _walking = false; return; }
            try { Step(); }
            catch (Exception e)
            {
                WowLog.Warn("Follow: step failed (" + e.Message + ") - follow ended.");
                Stop("error");
            }
        }

        private void Step()
        {
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (p == null) { Stop("no game"); return; }
            CharacterController cc = Controller();
            if (cc == null) { Stop("the avatar has no CharacterController"); return; }
            Transform t = cc.transform;
            Vector3 pos = t.position;

            // own movement since our last step (keys, thumbstick, teleport, a server teleport): the player took control back
            bool selfMoved = false;
            if (_haveLast)
            {
                Vector3 d = pos - _lastPos;
                d.y = 0f;
                selfMoved = d.magnitude > FollowRules.OwnMoveMetres;
            }

            EntityState e = s.Find(_guid);
            bool visible = e != null && !e.Dead;
            Vector3 leader;
            if (!LeaderPosition(s, e, out leader)) visible = false;
            Vector3 to = leader - pos;
            to.y = 0f;
            float yards = to.magnitude / WowSpace.MetresPerYard;

            FollowRules.Verdict v = FollowRules.Next(yards, _walking, visible, true, selfMoved, p.Dead || p.Ghost, Busy(p));
            if (v == FollowRules.Verdict.Stop)
            {
                string why = !visible ? "the leader is gone or out of sight" : selfMoved ? "you moved" : (p.Dead || p.Ghost) ? "you died"
                           : Busy(p) ? "a taxi / ride / forced move took the avatar" : "the leader is " + yards.ToString("0") + " yd away";
                if (yards > FollowRules.BreakRange && visible && !selfMoved)
                    WowPlay.Publish(new GameEvent { Type = GameEventType.Error, Text = "Target is too far away." });   // ERR_AUTOFOLLOW_TOO_FAR (GlobalStrings.lua:2943)
                Stop(why);
                _haveLast = false;
                _walking = false;
                return;
            }
            _walking = v == FollowRules.Verdict.Walk;
            if (_walking)
            {
                float stepYd = FollowRules.StepYards(yards, p.RunSpeed, Time.deltaTime);
                if (stepYd > 0f && to.sqrMagnitude > 1e-6f)
                {
                    Vector3 dir = to.normalized;
                    cc.Move(dir * stepYd * WowSpace.MetresPerYard);   // horizontal only: the client's own provider keeps gravity and steps
                    if (!WowPlayer.XrPresent)
                    {
                        Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
                        t.rotation = Quaternion.Slerp(t.rotation, want, Mathf.Clamp01(TurnRate * Time.deltaTime));
                    }
                }
            }
            _lastPos = t.position;
            _haveLast = true;
        }

        /// <summary>The leader's feet in Unity: a Somnium avatar (WowGear), else the drawn WoW character, else the session's position.</summary>
        private static bool LeaderPosition(IWowSession s, EntityState e, out Vector3 at)
        {
            float seat, yaw;
            if (WowGear.TryRiderAnchor(_guid, _name, out at, out seat, out yaw)) return true;
            Transform v = WowEntityViews.GetView(_guid);
            if (v != null) { at = v.position; return true; }
            if (e != null) { at = WowSpace.ToUnity(0, e.Pos.X, e.Pos.Y, e.Pos.Z); return true; }
            at = Vector3.zero;
            return false;
        }

        private CharacterController Controller()
        {
            Transform root = WowPlayer.Root;
            if (root == null) return null;
            if (_cc != null && _ccFor == root) return _cc;
            _ccFor = root;
            _cc = root.GetComponentInParent<CharacterController>();   // found like MountSeat.cs:131-132
            if (_cc == null) _cc = root.GetComponentInChildren<CharacterController>();
            if (_cc == null && !_ccMissingLogged) { _ccMissingLogged = true; WowLog.Warn("Follow: the avatar has no CharacterController - following cannot walk it (logged once)."); }
            return _cc;
        }
    }
}
