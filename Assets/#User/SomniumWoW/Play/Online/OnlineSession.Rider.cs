using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 7 (owner Online, PLAN7 s4.10 "The rider's movement sender"): while PlayerMotion.Driven the movement packets follow the
    // ride's own state, one opcode per change like the real client (START/STOP forward, backward, strafe, turn, swim, ascend / descend,
    // SET_FACING, SET_PITCH, JUMP, FALL_LAND, walk / run mode, heartbeats), so other 3.3.5a clients dead-reckon us at the server's speed.
    // The position is the MOUNT's feet and the orientation the mount's heading (SetPlayerPosition while riding). Walkers keep the code
    // in OnlineSession.Movement.cs. Flag rules (AC WorldSession::ReadMovementInfo): never FLYING without the server's can-fly, never
    // FALLING with CAN_FLY, never both of an opposing pair.
    public sealed partial class OnlineSession
    {
        const double RiderVerticalHeartbeat = 0.25;   // ours: heartbeat during pure ascend / descend (normal clients see smaller steps)
        const double RiderAngleInterval = 0.1;        // ours: SET_FACING / SET_PITCH at most this often
        const float RiderAngleStep = 0.05f;           // ours: rad of heading / pitch change worth a packet
        const float OthersAscendFactor = 0.6f;        // ours: others with only ASCENDING / DESCENDING move vertically at 0.6 x flight speed
        const double RiderStatsEvery = 30.0;

        double _rAirStart, _lastFacingAt = -10, _lastPitchAt = -10;
        float _rJz, _rJs, _rJc, _rJxy, _sentPitch;
        bool _flyRefusedLogged, _jumpCanFlyLogged, _otherJumpLogged;
        // statistics for the 30 s line
        readonly Dictionary<ushort, int> _riderSent = new Dictionary<ushort, int>();
        int _riderSentTotal;
        double _riderStatsAt = -1, _fwdTime;
        float _fwdDist, _fwdExpected;
        WowPos _riderLastPos;
        double _riderLastAt = -1;

        /// <summary>Probes / diagnostics: the straight-riding totals since the last stats line (yards travelled, yards the server's speed gives, seconds).</summary>
        public void RiderSpeedTotals(out float travelled, out float expected, out double seconds)
        {
            travelled = _fwdDist;
            expected = _fwdExpected;
            seconds = _fwdTime;
        }

        /// <summary>The speed the server dead-reckons us with for these flags (AC Unit::GetSpeed by move type).</summary>
        float SpeedFor(uint f)
        {
            bool back = (f & MoveFlags.Backward) != 0;
            if ((f & MoveFlags.Flying) != 0) return _mySpeeds[back ? MoveType.FlightBack : MoveType.Flight];
            if ((f & MoveFlags.Swimming) != 0) return _mySpeeds[back ? MoveType.SwimBack : MoveType.Swim];
            if ((f & MoveFlags.Walking) != 0) return _mySpeeds[MoveType.Walk];
            return _mySpeeds[back ? MoveType.RunBack : MoveType.Run];
        }

        /// <summary>SetPlayerPosition while Driven: measures the planar speed of pure forward riding against the server's speed.</summary>
        void MeasureRider(WowPos pos)
        {
            double now = _now;
            if (_riderLastAt >= 0)
            {
                double dt = now - _riderLastAt;
                uint f = _sentFlags;
                bool pureForward = (f & MoveFlags.Forward) != 0 && (f & (MoveFlags.StrafeLeft | MoveFlags.StrafeRight | MoveFlags.Falling)) == 0;
                if (dt > 0 && dt < 0.5 && pureForward)
                {
                    float dx = pos.X - _riderLastPos.X, dy = pos.Y - _riderLastPos.Y;
                    _fwdDist += (float)Math.Sqrt(dx * dx + dy * dy);
                    _fwdTime += dt;
                    _fwdExpected += SpeedFor(f) * (float)dt * ((f & (MoveFlags.Flying | MoveFlags.Swimming)) != 0 ? (float)Math.Cos(_sentPitch) : 1f);
                }
            }
            _riderLastPos = pos;
            _riderLastAt = now;
        }

        void RiderSend(ushort opcode, uint flags, WowPos pos, float ori, uint fallTime, float pitch)
        {
            bool falling = (flags & MoveFlags.Falling) != 0;
            SendMove(opcode, flags, pos, ori, fallTime, falling ? _rJz : 0f, falling ? _rJs : 0f, falling ? _rJc : 0f, falling ? _rJxy : 0f, pitch);
            if (opcode == Op.MSG_MOVE_SET_FACING) _lastFacingAt = _now;
            if ((flags & (MoveFlags.Flying | MoveFlags.Swimming)) != 0) { _sentPitch = pitch; if (opcode == Op.MSG_MOVE_SET_PITCH) _lastPitchAt = _now; }
            int c;
            _riderSent.TryGetValue(opcode, out c);
            _riderSent[opcode] = c + 1;
            _riderSentTotal++;
        }

        /// <summary>One tick of the rider's sender (TickOwnMovement while PlayerMotion.Driven). Several changes in one frame = several packets, each with the full new flags.</summary>
        void TickRider()
        {
            var m = _motion;
            var pos = _avatarPos;
            double now = _now;
            float ori = pos.O;
            bool canFly = _player.CanFly;
            bool flying = m.Flying && canFly;
            if (m.Flying && !canFly && !_flyRefusedLogged)
            {
                _flyRefusedLogged = true;
                WowLog.Warn("Online: the ride says Flying but the server has not sent SMSG_MOVE_SET_CAN_FLY - FLYING is not sent (logged once)");
            }
            uint f = 0;
            if (m.Forward > 0) f |= MoveFlags.Forward; else if (m.Forward < 0) f |= MoveFlags.Backward;
            if (m.Strafe > 0) f |= MoveFlags.StrafeRight; else if (m.Strafe < 0) f |= MoveFlags.StrafeLeft;
            if (m.Turn > 0) f |= MoveFlags.Left; else if (m.Turn < 0) f |= MoveFlags.Right;
            if (m.Walking) f |= MoveFlags.Walking;
            if (m.Swimming) f |= MoveFlags.Swimming;
            if (flying) f |= MoveFlags.Flying;
            if ((flying || m.Swimming) && m.Vertical > 0) f |= MoveFlags.Ascending;
            else if ((flying || m.Swimming) && m.Vertical < 0) f |= MoveFlags.Descending;
            bool falling = m.Airborne && !flying && !m.Swimming && !canFly && (_stateFlags & MoveFlags.DisableGravity) == 0;
            if (falling) f |= MoveFlags.Falling;
            float pitch = (flying || m.Swimming) ? m.Pitch : 0f;
            uint prev = _sentFlags;
            if (m.Jumped && !falling && !_jumpCanFlyLogged)
            {
                _jumpCanFlyLogged = true;
                WowLog.Info("Online: a mounted jump while the server lets us fly is not sent as MSG_MOVE_JUMP (AC strips FALLING together with CAN_FLY; logged once)");
            }

            // leaving the ground: MSG_MOVE_JUMP (the launch velocity) or a heartbeat with FALLING (rode off an edge)
            if (falling && (prev & MoveFlags.Falling) == 0)
            {
                _rAirStart = now - Math.Max(0f, m.Jumped ? 0f : m.FallTime);
                if (m.Jumped)
                {
                    _rJz = -m.JumpUpSpeed;                      // MovementInfo jump z speed: negative = up (the walker's -_vz convention)
                    _rJs = (float)Math.Sin(m.JumpDirection);
                    _rJc = (float)Math.Cos(m.JumpDirection);
                    _rJxy = m.JumpPlanarSpeed;
                    RiderSend(Op.MSG_MOVE_JUMP, f, pos, ori, 0, pitch);
                }
                else
                {
                    float dir = ori + ((f & MoveFlags.Backward) != 0 ? (float)Math.PI : 0f);
                    _rJz = 0f;
                    _rJs = (float)Math.Sin(dir);
                    _rJc = (float)Math.Cos(dir);
                    _rJxy = (f & (MoveFlags.Forward | MoveFlags.Backward)) != 0 ? SpeedFor(f) : 0f;
                    RiderSend(Op.MSG_MOVE_HEARTBEAT, f, pos, ori, (uint)((now - _rAirStart) * 1000.0), pitch);
                }
                return;
            }
            // back on the ground after a jump / fall: MSG_MOVE_FALL_LAND (Player::HandleFall). Landing from FLIGHT sends no FALL_LAND.
            if (!falling && (prev & MoveFlags.Falling) != 0)
            {
                uint ms = (uint)Math.Max(0.0, (now - _rAirStart) * 1000.0);
                RiderSend(Op.MSG_MOVE_FALL_LAND, f, pos, ori, ms, pitch);
                return;
            }

            uint changed = (f ^ prev) & ~(MoveFlags.CanFly | MoveFlags.Root | _stateFlags);
            bool sent = false;
            if ((changed & MoveFlags.Swimming) != 0) { RiderSend((f & MoveFlags.Swimming) != 0 ? Op.MSG_MOVE_START_SWIM : Op.MSG_MOVE_STOP_SWIM, f, pos, ori, 0, pitch); sent = true; }
            if ((changed & (MoveFlags.Ascending | MoveFlags.Descending)) != 0)
            {
                // AC's table has no STOP_DESCEND: ending either is MSG_MOVE_STOP_ASCEND
                ushort op = (f & MoveFlags.Ascending) != 0 ? Op.MSG_MOVE_START_ASCEND : (f & MoveFlags.Descending) != 0 ? Op.MSG_MOVE_START_DESCEND : Op.MSG_MOVE_STOP_ASCEND;
                RiderSend(op, f, pos, ori, 0, pitch);
                sent = true;
            }
            else if ((changed & MoveFlags.Flying) != 0) { RiderSend(Op.MSG_MOVE_HEARTBEAT, f, pos, ori, 0, pitch); sent = true; }   // take-off / landing without a climb key
            if ((changed & (MoveFlags.Forward | MoveFlags.Backward)) != 0)
            {
                ushort op = (f & MoveFlags.Forward) != 0 ? Op.MSG_MOVE_START_FORWARD : (f & MoveFlags.Backward) != 0 ? Op.MSG_MOVE_START_BACKWARD : Op.MSG_MOVE_STOP;
                RiderSend(op, f, pos, ori, 0, pitch);
                sent = true;
            }
            if ((changed & (MoveFlags.StrafeLeft | MoveFlags.StrafeRight)) != 0)
            {
                ushort op = (f & MoveFlags.StrafeLeft) != 0 ? Op.MSG_MOVE_START_STRAFE_LEFT : (f & MoveFlags.StrafeRight) != 0 ? Op.MSG_MOVE_START_STRAFE_RIGHT : Op.MSG_MOVE_STOP_STRAFE;
                RiderSend(op, f, pos, ori, 0, pitch);
                sent = true;
            }
            if ((changed & (MoveFlags.Left | MoveFlags.Right)) != 0)
            {
                ushort op = (f & MoveFlags.Left) != 0 ? Op.MSG_MOVE_START_TURN_LEFT : (f & MoveFlags.Right) != 0 ? Op.MSG_MOVE_START_TURN_RIGHT : Op.MSG_MOVE_STOP_TURN;
                RiderSend(op, f, pos, ori, 0, pitch);
                sent = true;
            }
            if ((changed & MoveFlags.Walking) != 0) { RiderSend((f & MoveFlags.Walking) != 0 ? Op.MSG_MOVE_SET_WALK_MODE : Op.MSG_MOVE_SET_RUN_MODE, f, pos, ori, 0, pitch); sent = true; }
            if (!sent && (f & (MoveFlags.Flying | MoveFlags.Swimming)) != 0 && Math.Abs(pitch - _sentPitch) > RiderAngleStep && now - _lastPitchAt >= RiderAngleInterval)
            {
                RiderSend(Op.MSG_MOVE_SET_PITCH, f, pos, ori, 0, pitch);
                sent = true;
            }
            // mouse steering and the client's snap / smooth turn change the heading without a turn key: SET_FACING
            if (!sent && (f & MoveFlags.MaskTurning) == 0 && Math.Abs(AngleDiff(ori, _sentPos.O)) > RiderAngleStep && now - _lastFacingAt >= RiderAngleInterval)
            {
                RiderSend(Op.MSG_MOVE_SET_FACING, f, pos, ori, 0, pitch);
                sent = true;
            }
            bool moving = (f & (MoveFlags.MaskMoving | MoveFlags.MaskTurning)) != 0;
            if (!sent && m.AtSpeed && moving) { RiderSend(Op.MSG_MOVE_HEARTBEAT, f, pos, ori, falling ? (uint)((now - _rAirStart) * 1000.0) : 0, pitch); sent = true; }
            if (!sent && moving)
            {
                bool pureVertical = (f & (MoveFlags.Ascending | MoveFlags.Descending)) != 0 && (f & (MoveFlags.Forward | MoveFlags.Backward | MoveFlags.StrafeLeft | MoveFlags.StrafeRight)) == 0;
                if (now - _lastSendAt >= (pureVertical ? RiderVerticalHeartbeat : HeartbeatSeconds))
                    RiderSend(Op.MSG_MOVE_HEARTBEAT, f, pos, ori, falling ? (uint)((now - _rAirStart) * 1000.0) : 0, pitch);
            }
            else if (!sent)
            {
                float dx = pos.X - _sentPos.X, dy = pos.Y - _sentPos.Y, dz = pos.Z - _sentPos.Z;
                if (dx * dx + dy * dy + dz * dz > 0.5f * 0.5f && now - _lastSendAt > 0.25) RiderSend(Op.MSG_MOVE_HEARTBEAT, f, pos, ori, 0, pitch);
            }
        }

        /// <summary>The ride took the avatar over / gave it back: start each side from a clean state (the walker knows only Forward).</summary>
        void RiderTransition(bool driven)
        {
            var at = _haveAvatar ? _avatarPos : _player.Pos;
            if (driven)
            {
                _airborne = false;
                _riderStatsAt = _now + RiderStatsEvery;
                _riderLastAt = -1;
                WowLog.Info("Online: the ride drives the avatar - movement packets follow the mount (feet " + at + ")");
                return;
            }
            if ((_sentFlags & (MoveFlags.MaskMoving | MoveFlags.MaskTurning | MoveFlags.Flying | MoveFlags.Swimming)) != 0 && _holdUntil < 0 && _world != null && _player.Guid != 0)
                SendMove(Op.MSG_MOVE_STOP, 0u, at, at.O, 0, 0, 0, 0, 0);
            _startWaitSince = -1;
            LogRiderStats("ride ended");
            WowLog.Info("Online: the ride let go - walker movement packets again");
        }

        void TickRiderStats()
        {
            if (!_motion.Driven || _riderStatsAt < 0 || _now < _riderStatsAt) return;
            _riderStatsAt = _now + RiderStatsEvery;
            LogRiderStats("30 s");
        }

        void LogRiderStats(string when)
        {
            if (_riderSentTotal == 0 && _fwdTime <= 0) return;
            var sb = new StringBuilder();
            var keys = new List<ushort>(_riderSent.Keys);
            keys.Sort((a, b) => _riderSent[b].CompareTo(_riderSent[a]));
            for (int i = 0; i < keys.Count; i++) sb.Append(i == 0 ? "" : ", ").Append(Op.Name(keys[i]).Replace("MSG_MOVE_", "")).Append(' ').Append(_riderSent[keys[i]]);
            string speed = _fwdTime > 0.2
                ? "speed measured " + (_fwdDist / _fwdTime).ToString("0.00") + " vs server " + (_fwdExpected / _fwdTime).ToString("0.00") + " yd/s over " + _fwdTime.ToString("0.0") + " s of straight riding"
                : "no straight riding to measure";
            WowLog.Info("Online: rider moves sent " + _riderSentTotal + " (" + when + ": " + (sb.Length > 0 ? sb.ToString() : "none") + "), " + speed);
            _riderSent.Clear();
            _riderSentTotal = 0;
            _fwdDist = 0f; _fwdExpected = 0f; _fwdTime = 0;
        }

        /// <summary>Another unit's movement flags -> EntityState.Flying / Swimming / Pitch; logs the first real client's jump (WoW's jump constant).</summary>
        void ApplyOtherMoveState(OnlineObject o, MovementInfo mi, ushort opcode)
        {
            if (o.State == null || mi == null) return;
            o.State.Flying = (mi.Flags & MoveFlags.Flying) != 0;
            o.State.Swimming = (mi.Flags & MoveFlags.Swimming) != 0;
            o.State.Pitch = (mi.Flags & (MoveFlags.Flying | MoveFlags.Swimming)) != 0 ? mi.Pitch : 0f;
            if (opcode == Op.MSG_MOVE_JUMP && o.TypeId == TypeId.Player && !o.State.IsSomniumPlayer && !_otherJumpLogged)
            {
                _otherJumpLogged = true;
                WowLog.Info("Online: a real client jumped: zspeed " + mi.JumpZSpeed.ToString("0.000") + " (negative = up), xyspeed " + mi.JumpXYSpeed.ToString("0.000") +
                            " yd/s, flags 0x" + mi.Flags.ToString("X8") + " (WoW's own jump take-off; ours is a constant of the ride - logged once)");
            }
        }
    }
}
