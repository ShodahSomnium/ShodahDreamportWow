// iteration 11 - owner vehicle (proposal-pets.md s3.6 / 3.7 / 3.11 / 6.2). Vehicles (enter / seat / drive / turret / passenger / exit),
// possession (Eye of Acherus, Eye of Kilrogg, Mind Control, Eyes of the Beast), the mover guid, aimed vehicle spells and other units' seats.
// Every layout is AzerothCore master (copies in P/acsrc11/pets, file:line quoted). Motion's Movement.cs calls the Veh* hooks below
// (proposal-motion s6.2.2 items 1-7, merged pets H3 a-g); the pet / possess / vehicle BAR (SMSG_PET_SPELLS) is area pets' (OnlineSession.Pets.cs),
// which calls VehCast for a Vehicle bar and reads _vehCtl for its classification (s3.8 rule 3).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Pets;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        /// <summary>The unit whose movement we send (a driven vehicle / possessed unit); 0 = our own character.</summary>
        internal ulong _vehMover = 0;
        /// <summary>Our seat when we ride a vehicle as a passenger or driver; null = on our own feet (also null while possessing).</summary>
        internal VehicleView _vehSeat = null;
        /// <summary>The unit of our last SMSG_CLIENT_CONTROL_UPDATE(allow 1) that is not our player (0 = none / control is ours again).
        /// Area pets' SMSG_PET_SPELLS classification rule 3 (proposal-pets s3.8) reads it.</summary>
        internal ulong _vehCtl = 0;

        // ---- ours (named + logged once in InitVehicle)
        /// <summary>A turret's aim packets go out at most every 0.1 s while it turns (+ one when it stops).</summary>
        const double VehAimSendEvery = 0.1;
        /// <summary>A control grant for a vehicle unit waits this long for the boarding path before it counts as a possession.</summary>
        const double VehCtlWaitSeconds = 2.0;
        /// <summary>Unrooted while seated without an exit path for this long: we are off the vehicle.</summary>
        const double VehUnrootExitSeconds = 2.0;
        /// <summary>The vehicle / controlled unit out of sight this long ends the seat.</summary>
        const double VehGoneSeconds = 1.0;
        /// <summary>Hold the reported position after leaving a vehicle / ending a possession (the avatar is moved there by the Teleport event).</summary>
        const double VehExitHold = 3.0, VehPossessHold = 5.0;
        const string VehCantText = "You can't do that right now.";   // ERR_CLIENT_LOCKED_OUT (GlobalStrings.lua:3008), ours to say for a seat that forbids it

        sealed class VehPassenger
        {
            public ulong Vehicle;
            public sbyte Seat;
            public float LX, LY, LZ, LO;          // fixed offset in the vehicle's frame (MovementInfo transport block)
            public SplinePath Local;              // a path in the vehicle's frame (SMSG_MONSTER_MOVE_TRANSPORT), null = the fixed offset
            public bool GoneLogged;
        }

        readonly Dictionary<ulong, VehPassenger> _vehPassengers = new Dictionary<ulong, VehPassenger>();
        readonly List<ulong> _vehPassengerDrop = new List<ulong>();
        WowPos _vehBodyPos;                       // our character's position when a possession started
        double _vehCtlPendingAt = -1;             // a control grant for a vehicle unit that we do not sit on yet
        double _vehSeenAt = -1;                   // the vehicle / controlled unit was last in sight (session clock); -1 = never seen
        double _vehUnrootAt = -1;                 // unrooted while seated (waiting for the exit path)
        bool _vehRooted;                          // the controlled unit is rooted (SMSG_FORCE_MOVE_ROOT with its guid, acked)
        bool _vehCanFlySet;                       // SMSG_MOVE_SET_CAN_FLY for the controlled unit (acked)
        bool _vehDriving;                         // the player's speeds / CanFly / MountDisplayId show the controlled unit's
        bool _vehSavedCanFly;
        int _vehSavedMountDisplay;
        // turret aim
        bool _aimWant;
        float _aimWantO, _aimWantP, _aimO, _aimP, _aimSentO, _aimSentP;
        double _aimSentAt = -1;
        bool _aimDirty;
        int _aimLogs, _vehCastLogs;
        bool _vehLoggedConstants, _vehSpeedLogged, _vehDriveAimLogged, _vehUnknownSeatLogged;

        // ================================================================== init / packets

        partial void InitVehicle()
        {
            VehicleDb.Load(_files);
            _world.On(Op.SMSG_CLIENT_CONTROL_UPDATE, VehOnClientControl, this);   // motion owns our own guid's half (OnlineSession.Forced.cs)
            _world.On(Op.SMSG_ON_CANCEL_EXPECTED_RIDE_VEHICLE_AURA, p => WowLog.Info("Online: SMSG_ON_CANCEL_EXPECTED_RIDE_VEHICLE_AURA (a vehicle seat is coming)"), this);
            _world.On(Op.SMSG_PLAYER_VEHICLE_DATA, VehOnPlayerVehicleData, this);
            _world.On(Op.SMSG_FORCE_MOVE_ROOT, p => VehOnForceRoot(p, true), this);
            _world.On(Op.SMSG_FORCE_MOVE_UNROOT, p => VehOnForceRoot(p, false), this);
            _world.On(Op.SMSG_MOVE_SET_CAN_FLY, p => VehOnCanFly(p, true), this);
            _world.On(Op.SMSG_MOVE_UNSET_CAN_FLY, p => VehOnCanFly(p, false), this);
            _world.On(Op.SMSG_MOVE_GRAVITY_DISABLE, p => VehOnGravity(p, true), this);
            _world.On(Op.SMSG_MOVE_GRAVITY_ENABLE, p => VehOnGravity(p, false), this);
            _world.On(Op.SMSG_TRANSFER_PENDING, p => VehFarTeleport("a far teleport (SMSG_TRANSFER_PENDING)"), this);
            _world.On(Op.SMSG_NEW_WORLD, p => { VehFarTeleport("a new world (SMSG_NEW_WORLD)"); _vehPassengers.Clear(); }, this);
            if (!_vehLoggedConstants)
            {
                _vehLoggedConstants = true;
                WowLog.Info("Online: vehicles - ours: turret aim packets every " + VehAimSendEvery + " s while turning, a vehicle control grant waits " + VehCtlWaitSeconds +
                            " s for its seat, unrooted " + VehUnrootExitSeconds + " s without an exit path = off the vehicle, a seat whose unit is out of sight " +
                            VehGoneSeconds + " s ends, position held " + VehExitHold + " s after an exit / " + VehPossessHold + " s after a possession");
            }
        }

        /// <summary>SMSG_CLIENT_CONTROL_UPDATE (Player::SetClientControl, Player.cpp:13178-13181): packed guid; uint8 allowMove.</summary>
        void VehOnClientControl(WowPacket p)
        {
            ulong g = p.PackedGuid();
            byte allow = p.U8();
            if (g == 0) return;
            if (g == _player.Guid)
            {
                if (allow != 0) VehControlBack();
                return;
            }
            var v = _player.Vehicle;
            if (allow == 0)
            {
                // a fleeing / confused controlled unit (Player.cpp:13180) or the server taking it back: keep the view, stop steering
                if (v != null && v.Guid == g && v.CanSteer)
                {
                    v.CanSteer = false;
                    v.Counter++;
                    WowLog.Info("Online: control of " + VehName(g) + " taken back by the server (SMSG_CLIENT_CONTROL_UPDATE allow 0) - steering paused");
                }
                return;
            }
            _vehCtl = g;
            if (v != null && v.Guid == g) { VehTakeControl(v); return; }
            if (VehIsVehicleUnit(g))
            {
                _vehCtlPendingAt = _now;
                WowLog.Info("Online: control of the vehicle " + VehName(g) + " granted before its seat (AC Vehicle::AddPassenger, Vehicle.cpp:425-468: control, bar, root, then the boarding path) - waiting for the seat");
                return;
            }
            VehStartPossess(g);
        }

        /// <summary>SMSG_PLAYER_VEHICLE_DATA (SpellAuraEffects.cpp:5926-5929): packed player guid; uint32 vehicleId (0 = no longer a vehicle).</summary>
        void VehOnPlayerVehicleData(WowPacket p)
        {
            ulong g = p.PackedGuid();
            uint id = p.U32();
            OnlineObject o;
            if (_objects.TryGetValue(g, out o) && o.State != null) o.State.VehicleId = (int)id;
            WowLog.Info("Online: " + (g == _player.Guid ? "our character" : VehName(g)) + " is " + (id != 0 ? "a vehicle now (Vehicle " + id + ")" : "no longer a vehicle") + " (SMSG_PLAYER_VEHICLE_DATA)");
        }

        /// <summary>SMSG_FORCE_MOVE_(UN)ROOT (Unit::SendMoveRoot, Unit.cpp:14137-14145): packed guid; uint32 counter. Our own guid is motion's
        /// (acked there); the CONTROLLED unit's comes with its guid (a turret is rooted at control, Player.cpp:13196-13200) and is acked here with
        /// its MovementInfo (HandleMoveRootAck requires guid == m_mover, MovementHandler.cpp:997-1012).</summary>
        void VehOnForceRoot(WowPacket p, bool root)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (g == _player.Guid)
            {
                if (!root && _vehSeat != null && _vehMover == 0) _vehUnrootAt = _now;   // the exit path should follow (Unit.cpp:15381 then 15429-15435)
                return;
            }
            if (g == 0 || g != _vehMover) return;
            _vehRooted = root;
            WowPos at = VehUnitPos(g, _player.Pos);
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            VehMoverInfoCore(0u, at, at.O).Write(w);
            _world.Send(root ? Op.CMSG_FORCE_MOVE_ROOT_ACK : Op.CMSG_FORCE_MOVE_UNROOT_ACK, w);
            WowLog.Info("Online: " + VehName(g) + (root ? " rooted" : " unrooted") + " (counter " + counter + ") acked with its guid");
        }

        /// <summary>SMSG_MOVE_SET_CAN_FLY / UNSET for the controlled unit: packed guid; uint32 counter -> CMSG_MOVE_SET_CAN_FLY_ACK: packed guid;
        /// counter; MovementInfo; uint32 applied (HandleMoveFlagChangeOpcode, as our own in OnlineSession.Mounts.cs OnCanFly).</summary>
        void VehOnCanFly(WowPacket p, bool set)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (g == 0 || g == _player.Guid || g != _vehMover) return;
            _vehCanFlySet = set;
            OnlineObject o;
            if (_objects.TryGetValue(g, out o) && o.Move != null) { if (set) o.Move.Flags |= MoveFlags.CanFly; else o.Move.Flags &= ~(MoveFlags.CanFly | MoveFlags.Flying); }
            WowPos at = VehUnitPos(g, _player.Pos);
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            VehMoverInfoCore(0u, at, at.O).Write(w);
            w.U32(set ? 1u : 0u);
            _world.Send(Op.CMSG_MOVE_SET_CAN_FLY_ACK, w);
            WowLog.Info("Online: " + VehName(g) + " can fly " + (set ? "set" : "unset") + " (counter " + counter + ") acked with its guid");
        }

        /// <summary>SMSG_MOVE_GRAVITY_DISABLE / ENABLE for the controlled unit -> the matching ack (no applied field, OnlineSession.Mounts.cs OnFlagChange).</summary>
        void VehOnGravity(WowPacket p, bool off)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (g == 0 || g == _player.Guid || g != _vehMover) return;
            OnlineObject o;
            if (_objects.TryGetValue(g, out o) && o.Move != null) { if (off) o.Move.Flags |= MoveFlags.DisableGravity; else o.Move.Flags &= ~MoveFlags.DisableGravity; }
            WowPos at = VehUnitPos(g, _player.Pos);
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            VehMoverInfoCore(0u, at, at.O).Write(w);
            _world.Send(off ? Op.CMSG_MOVE_GRAVITY_DISABLE_ACK : Op.CMSG_MOVE_GRAVITY_ENABLE_ACK, w);
            WowLog.Info("Online: " + VehName(g) + " gravity " + (off ? "disabled" : "enabled") + " (counter " + counter + ") acked with its guid");
        }

        void VehFarTeleport(string why)
        {
            if (_player.Vehicle != null || _vehSeat != null || _vehMover != 0) VehEnd(why, _player.Pos, false, 0);
        }

        // ================================================================== control

        bool VehIsVehicleUnit(ulong g)
        {
            if (((g >> 48) & 0xFFFF) == 0xF150) return true;                      // HighGuid::Vehicle (ObjectGuid.h)
            OnlineObject o;
            return _objects.TryGetValue(g, out o) && o.State != null && o.State.VehicleId != 0;
        }

        /// <summary>The vehicle we sit on is ours to steer (SetCharmedBy CHARM_TYPE_VEHICLE, Unit.cpp:14372-14377).</summary>
        void VehTakeControl(VehicleView v)
        {
            _vehCtlPendingAt = -1;
            bool wasSteering = v.CanSteer && _vehMover == v.Guid;
            v.CanSteer = true;
            if (v.Mode != VehicleMode.Possess)
            {
                OnlineObject o;
                bool disableMove = _objects.TryGetValue(v.Guid, out o) && (o.U32(UF.UNIT_FIELD_FLAGS) & 0x4u) != 0;   // UNIT_FLAG_DISABLE_MOVE (UnitDefines.h)
                v.Mode = disableMove ? VehicleMode.Turret : VehicleMode.Drive;
            }
            if (_vehMover != v.Guid)
            {
                _vehMover = v.Guid;
                _vehRooted = false;
                _vehCanFlySet = false;
                VehAimInit(v);
                _world.Send(Op.CMSG_SET_ACTIVE_MOVER, new PacketWriter().U64(v.Guid));   // MovementHandler.cpp:780-793 (only logs a mismatch)
            }
            if (v.Mode == VehicleMode.Drive || v.Mode == VehicleMode.Possess) VehDriveBegin(v);
            v.Counter++;
            if (!wasSteering)
                WowLog.Info("Online: we control " + VehName(v.Guid) + " - mode " + v.Mode + (v.Mode == VehicleMode.Turret ? " (UNIT_FLAG_DISABLE_MOVE: turn and aim only)" : "") +
                            "; CMSG_SET_ACTIVE_MOVER sent, its movement goes out with its guid");
        }

        void VehStartPossess(ulong g)
        {
            _vehCtlPendingAt = -1;
            if (_player.Vehicle != null && _player.Vehicle.Mode != VehicleMode.Possess)
                WowLog.Warn("Online: a possession of " + VehName(g) + " while seated on " + VehName(_player.Vehicle.Guid) + " - the seat view is replaced");
            OnlineObject o;
            _objects.TryGetValue(g, out o);
            var e = o != null ? o.State : null;
            var v = new VehicleView
            {
                Guid = g, Mode = VehicleMode.Possess, CanSteer = true, HideAvatar = true, CanLeave = true, HasVehicleUi = false, SeatIndex = -1,
                DisplayId = e != null ? e.DisplayId : 0, Name = e != null ? e.Name : null,
                TurnSpeed = (float)Math.PI, PitchSpeed = (float)Math.PI, PitchMin = -(float)(Math.PI / 2), PitchMax = (float)(Math.PI / 2)
            };
            v.CanFly = o != null && o.Move != null && (o.Move.Flags & (MoveFlags.CanFly | MoveFlags.Flying | MoveFlags.DisableGravity)) != 0;
            _vehBodyPos = _player.Pos;
            _vehSeat = null;
            _player.Vehicle = v;
            _vehSeenAt = o != null && o.HasPos ? _now : -1;
            _vehMover = g;
            _vehRooted = false;
            _vehCanFlySet = false;
            VehAimInit(v);
            _world.Send(Op.CMSG_SET_ACTIVE_MOVER, new PacketWriter().U64(g));
            VehDriveBegin(v);
            _events.Enqueue(new GameEvent { Type = GameEventType.VehicleEntered, SourceGuid = g, Amount = (int)VehicleMode.Possess, SpellId = 0, Text = v.Name });
            WowLog.Info("Online: possessing " + VehName(g) + " (SMSG_CLIENT_CONTROL_UPDATE allow 1, not a vehicle) - our body stays at " + _vehBodyPos +
                        "; its movement goes out with its guid, CMSG_SET_ACTIVE_MOVER sent" + (v.CanFly ? "; it flies" : ""));
        }

        /// <summary>SMSG_CLIENT_CONTROL_UPDATE(our guid, 1): we move our own character again (Unit.cpp:14503 / 14509 / 14566).</summary>
        void VehControlBack()
        {
            var v = _player.Vehicle;
            bool had = _vehMover != 0 || _vehCtl != 0;
            _vehCtl = 0;
            _vehCtlPendingAt = -1;
            if (_vehMover != 0)
            {
                _vehMover = 0;
                _vehRooted = false;
                _vehCanFlySet = false;
                _aimWant = false;
                _world.Send(Op.CMSG_SET_ACTIVE_MOVER, new PacketWriter().U64(_player.Guid));
            }
            if (v == null)
            {
                if (had) WowLog.Info("Online: control of our character is back (no vehicle view)");
                return;
            }
            if (v.Mode == VehicleMode.Possess) { VehEnd("the possession ended (control is ours again)", _vehBodyPos, true, VehPossessHold); return; }
            VehDriveEnd();
            if (v.Mode != VehicleMode.Passenger || v.CanSteer)
            {
                v.Mode = VehicleMode.Passenger;
                v.CanSteer = false;
                v.ServerMoving = false;
                v.Counter++;
                WowLog.Info("Online: control of our character is back while seated on " + VehName(v.Guid) + " - a passenger now (the exit path or a new seat follows)");
            }
        }

        // ================================================================== motion's hooks (Movement.cs)

        /// <summary>Our own SMSG_MONSTER_MOVE(_TRANSPORT) with TransportEnter / TransportExit, or on a unit transport (motion item 1).</summary>
        partial void VehOnSelfSpline(ulong tGuid, sbyte tSeat, SplinePath s)
        {
            if (s == null) return;
            if ((s.Flags & SplineFlags.TransportExit) != 0)
            {
                WowPos end = VehSplineEnd(s, _player.Pos.O);
                if (_player.Vehicle == null && _vehSeat == null)
                {
                    WowLog.Info("Online: an exit path without a seat - placed at its end " + end);
                    HoldAt(end, VehExitHold);
                    TeleportEvent(end, _mapId);
                    return;
                }
                VehEnd("left the vehicle (the exit path, SplineFlags.TransportExit)", end, true, VehExitHold);
                return;
            }
            if (tGuid == 0 || IsGoTransportGuid(tGuid)) return;
            if ((s.Flags & SplineFlags.TransportEnter) != 0 || _vehSeat == null || _vehSeat.Guid != tGuid) { VehBoard(tGuid, tSeat, s); return; }
            // the server moves us inside the vehicle (the seat's local path): the seat offset follows its end
            WowPos local = VehSplineEnd(s, _vehSeat.SeatOffset.O);
            _vehSeat.SeatOffset = local;
            _vehSeat.Counter++;
            WowLog.Info("Online: moved inside " + VehName(tGuid) + " to the seat offset " + local);
        }

        /// <summary>Another unit's SMSG_MONSTER_MOVE_TRANSPORT on a unit transport: a path in the vehicle's frame (MoveSplineInit.cpp:39 / 114-121).</summary>
        partial void VehOnPassengerSpline(OnlineObject o, ulong tGuid, sbyte tSeat, SplinePath s)
        {
            if (o == null || o == _self || tGuid == 0) return;
            VehPassenger vp;
            if (!_vehPassengers.TryGetValue(o.Guid, out vp)) _vehPassengers[o.Guid] = vp = new VehPassenger();
            vp.Vehicle = tGuid;
            vp.Seat = tSeat;
            if (s != null && s.Count > 0)
            {
                s.StartTime = _now;
                vp.Local = s;
                int n = s.Count - 1;
                vp.LX = s.Pts[n * 3]; vp.LY = s.Pts[n * 3 + 1]; vp.LZ = s.Pts[n * 3 + 2];
                if (s.FacingKind == 1) vp.LO = s.FacingAngle;
            }
            o.TransportGuid = tGuid;
            o.Spline = null;
            VehMarkSeated(o, vp);
        }

        /// <summary>Every MovementInfo of a unit whose transport guid is not a game-object transport (0 = not on a vehicle).</summary>
        partial void VehOnPassengerInfo(OnlineObject o, MovementInfo mi)
        {
            if (o == null || mi == null) return;
            bool seated = (mi.Flags & MoveFlags.OnTransport) != 0 && mi.TransportGuid != 0 && !IsGoTransportGuid(mi.TransportGuid);
            if (o == _self)
            {
                // our own create block in a seat (a login / far teleport inside a vehicle)
                if (seated && _vehSeat == null && _player.Vehicle == null)
                {
                    var path = new SplinePath();
                    path.AddPoint(mi.TX, mi.TY, mi.TZ);
                    path.FacingKind = 1;
                    path.FacingAngle = mi.TO;
                    WowLog.Info("Online: our character arrives seated on " + VehName(mi.TransportGuid) + " seat " + mi.TransportSeat + " (its movement block)");
                    VehBoard(mi.TransportGuid, mi.TransportSeat, path);
                }
                return;
            }
            if (seated)
            {
                VehPassenger vp;
                if (!_vehPassengers.TryGetValue(o.Guid, out vp)) _vehPassengers[o.Guid] = vp = new VehPassenger();
                vp.Vehicle = mi.TransportGuid;
                vp.Seat = mi.TransportSeat;
                vp.LX = mi.TX; vp.LY = mi.TY; vp.LZ = mi.TZ; vp.LO = mi.TO;
                vp.Local = null;
                o.TransportGuid = mi.TransportGuid;
                VehMarkSeated(o, vp);
            }
            else if (_vehPassengers.Remove(o.Guid) && o.State != null)
            {
                if (o.State.TransportGuid != 0 && !IsGoTransportGuid(o.State.TransportGuid)) o.State.TransportGuid = 0;
                o.State.SeatIndex = -1;
                o.State.HiddenInSeat = false;
                o.State.SeatAnim = 0;
            }
        }

        /// <summary>pets H3b: a seated passenger's MovementInfo (every ack) keeps its seat (Vehicle.cpp:414-422: transport.pos = the seat
        /// offset, time 0, seat index).</summary>
        partial void VehAddSeat(MovementInfo mi)
        {
            var v = _vehSeat;
            if (v == null || mi == null) return;
            mi.Flags |= MoveFlags.OnTransport;
            mi.TransportGuid = v.Guid;
            mi.TX = v.SeatOffset.X; mi.TY = v.SeatOffset.Y; mi.TZ = v.SeatOffset.Z; mi.TO = v.SeatOffset.O;
            mi.TransportTime = 0;
            mi.TransportSeat = (sbyte)v.SeatIndex;
            mi.Flags2 &= unchecked((ushort)~MoveFlags.F2InterpolatedMovement);
        }

        /// <summary>pets H3d: SMSG_FORCE_*_SPEED_CHANGE for the controlled unit - NOT acked (AC accepts a speed ack only from the player
        /// mover, MovementHandler.cpp:695-699); its speed and, while we drive it, the player's ride speed.</summary>
        partial void VehMoverSpeed(int type, float speed)
        {
            OnlineObject o;
            if (_objects.TryGetValue(_vehMover, out o) && type >= 0 && type < MoveType.Count) o.Speeds[type] = speed;
            if (_vehDriving) SetPlayerSpeed(type, speed);
            if (type == MoveType.Run || type == MoveType.Flight)
                WowLog.Info("Online: " + VehName(_vehMover) + " " + SpeedNames[type] + " speed " + speed.ToString("0.00") + " yd/s (not acked: AC acks only the player mover)");
        }

        /// <summary>pets H3a (Movement.cs OwnMovementInfo): the controlled unit's MovementInfo.</summary>
        MovementInfo VehMoverInfo(uint flags, WowPos pos, float ori)
        {
            if (_vehMover == 0) return null;
            return VehMoverInfoCore(flags, pos, ori);
        }

        /// <summary>The controlled unit's MovementInfo (s6.2 Drive): its flags2 (else Vehicle.dbc's, Vehicle.cpp:640-655), no ROOT unless it is
        /// rooted, none of the player's state flags, CAN_FLY only when the unit can fly, FLYING only with VehicleView.CanFly.</summary>
        MovementInfo VehMoverInfoCore(uint flags, WowPos pos, float ori)
        {
            OnlineObject o;
            _objects.TryGetValue(_vehMover, out o);
            var v = _player.Vehicle;
            uint unitFlags = o != null && o.Move != null ? o.Move.Flags : 0u;
            const uint StateBits = MoveFlags.DisableGravity | MoveFlags.Hover | MoveFlags.WaterWalking | MoveFlags.FallingSlow;
            flags &= ~(MoveFlags.Root | MoveFlags.OnTransport | MoveFlags.CanFly | StateBits | _stateFlags);
            flags |= unitFlags & StateBits;
            bool unitCanFly = (unitFlags & MoveFlags.CanFly) != 0 || _vehCanFlySet;
            if (unitCanFly) flags |= MoveFlags.CanFly;
            if (v == null || !v.CanFly) flags &= ~(MoveFlags.Flying | MoveFlags.Ascending | MoveFlags.Descending);
            if ((flags & (MoveFlags.CanFly | MoveFlags.DisableGravity)) != 0) flags &= ~MoveFlags.Falling;
            if (_vehRooted) flags = (flags & ~MoveFlags.MaskMoving) | MoveFlags.Root;
            ushort f2 = o != null && o.Move != null ? (ushort)(o.Move.Flags2 & ~MoveFlags.F2InterpolatedMovement) : (ushort)0;
            var vi = VehicleDb.Vehicle(o != null && o.State != null ? o.State.VehicleId : (v != null ? v.VehicleId : 0));
            if (vi != null) f2 |= VehicleDb.Flags2(vi.Flags);
            return new MovementInfo
            {
                Flags = flags, Flags2 = f2, Time = _clientTicks(),
                X = pos.X, Y = pos.Y, Z = pos.Z, O = NormalizeO(ori),
                Pitch = v != null ? v.AimPitch : 0f
            };
        }

        /// <summary>The addition to motion's item 4 (TickOwnMovement): only WowMount's ride may move the controlled unit, and not while the
        /// server runs a path on it (AC drops a mover's packets meanwhile, MovementHandler.cpp:543-547).</summary>
        bool VehMaySend()
        {
            var v = _player.Vehicle;
            return v != null && (v.Mode == VehicleMode.Drive || v.Mode == VehicleMode.Possess) && v.CanSteer && !v.ServerMoving && _motion.Driven && _vehMover == v.Guid;
        }

        // ================================================================== boarding / leaving

        void VehBoard(ulong tGuid, sbyte tSeat, SplinePath s)
        {
            OnlineObject o;
            _objects.TryGetValue(tGuid, out o);
            var e = o != null ? o.State : null;
            bool same = _vehSeat != null && _vehSeat.Guid == tGuid && _player.Vehicle == _vehSeat;
            var v = same ? _vehSeat : new VehicleView();
            v.Guid = tGuid;
            v.SeatIndex = tSeat;
            v.VehicleId = e != null ? e.VehicleId : v.VehicleId;
            VehFillSeat(v);
            v.SeatOffset = VehSplineEnd(s, 0f);
            if (s == null || s.Count == 0)
            {
                var si = VehicleDb.SeatOf(v.VehicleId, tSeat);
                if (si != null) v.SeatOffset = new WowPos(si.OffX, si.OffY, si.OffZ, 0f);
            }
            if (e != null) { v.DisplayId = e.DisplayId; v.Name = e.Name; }
            if (!same)
            {
                v.Mode = VehicleMode.Passenger;
                v.CanSteer = false;
                if (_player.Vehicle != null && _player.Vehicle.Mode == VehicleMode.Possess)
                    WowLog.Warn("Online: seated on " + VehName(tGuid) + " while possessing " + VehName(_player.Vehicle.Guid) + " - the possession view is replaced");
            }
            _vehSeat = v;
            _player.Vehicle = v;
            _vehUnrootAt = -1;
            if (o != null && o.HasPos) _vehSeenAt = _now;
            if (_vehCtl == tGuid) VehTakeControl(v);
            else if (same && v.Mode != VehicleMode.Passenger && _vehMover != tGuid) { VehDriveEnd(); v.Mode = VehicleMode.Passenger; v.CanSteer = false; }
            if (!same)
            {
                _events.Enqueue(new GameEvent { Type = GameEventType.VehicleEntered, SourceGuid = tGuid, Amount = (int)v.Mode, SpellId = v.SeatId, Text = v.Name });
                WowLog.Info("Online: in vehicle " + (v.Name ?? "?") + " (entry " + (e != null ? e.Entry : 0) + ", Vehicle " + v.VehicleId + ") seat " + tSeat + " (VehicleSeat " + v.SeatId +
                            ", flags 0x" + v.SeatFlags.ToString("X8") + ", flagsB 0x" + v.SeatFlagsB.ToString("X8") + "), mode " + v.Mode + ", offset " + v.SeatOffset +
                            (v.CanLeave ? "" : ", may not leave") + (v.HideAvatar ? ", the passenger is hidden" : "") + (v.HasVehicleUi ? ", vehicle UI" : "") +
                            (e == null ? " - the vehicle is not in sight yet (filled when it arrives)" : ""));
            }
            else
            {
                v.Counter++;
                WowLog.Info("Online: seat changed on " + VehName(tGuid) + " to seat " + tSeat + " (VehicleSeat " + v.SeatId + "), mode " + v.Mode + ", offset " + v.SeatOffset);
            }
        }

        /// <summary>VehicleView fields from Vehicle.dbc / VehicleSeat.dbc (s4.6).</summary>
        void VehFillSeat(VehicleView v)
        {
            var vi = VehicleDb.Vehicle(v.VehicleId);
            var si = vi != null && v.SeatIndex >= 0 && v.SeatIndex < 8 ? VehicleDb.Seat(vi.Seats[v.SeatIndex]) : null;
            v.SeatId = si != null ? si.Id : 0;
            v.SeatFlags = si != null ? si.Flags : 0u;
            v.SeatFlagsB = si != null ? si.FlagsB : 0u;
            v.CanLeave = si == null || si.CanEnterOrExit;   // unknown seat (vehicle not in sight yet): allowed - ours, the server decides
            v.CanSwitchSeat = si != null && si.CanSwitch;
            v.HideAvatar = si != null && si.HidePassenger;
            v.OwnSpells = si != null && si.CanAttack;
            v.HasVehicleUi = si != null && si.HasVehicleUi;
            v.TurnSpeed = vi != null ? vi.TurnSpeed : (float)Math.PI;
            v.PitchSpeed = vi != null ? vi.PitchSpeed : (float)Math.PI;
            v.PitchMin = vi != null ? vi.PitchMin : -(float)(Math.PI / 2);
            v.PitchMax = vi != null ? vi.PitchMax : (float)(Math.PI / 2);
            v.Pitching = vi != null && vi.AllowPitching;
            v.AimAdjustable = vi != null && vi.AimAdjustable;
            v.SeatCount = vi != null ? vi.SeatCount : 1;
            if (si == null && !_vehUnknownSeatLogged)
            {
                _vehUnknownSeatLogged = true;
                WowLog.Info("Online: vehicle seat unknown (Vehicle " + v.VehicleId + " seat " + v.SeatIndex + ") - leaving allowed, no seat switch (ours; logged once)");
            }
        }

        /// <summary>Leaves the seat / ends the possession without (or after) the server's packets: the view goes, the player's own state comes back.</summary>
        void VehEnd(string why, WowPos at, bool teleport, double hold)
        {
            var v = _player.Vehicle;
            VehDriveEnd();
            _vehSeat = null;
            _player.Vehicle = null;
            _vehMover = 0;
            _vehCtl = 0;
            _vehCtlPendingAt = -1;
            _vehRooted = false;
            _vehCanFlySet = false;
            _vehUnrootAt = -1;
            _vehSeenAt = -1;
            _aimWant = false;
            _aimDirty = false;
            var bar = _player.ControlBar;
            if (bar != null && v != null && bar.UnitGuid == v.Guid && (bar.Kind == ControlBarKind.Vehicle || bar.Kind == ControlBarKind.Possess)) _player.ControlBar = null;
            if (v != null) _events.Enqueue(new GameEvent { Type = GameEventType.VehicleLeft, SourceGuid = v.Guid, Pos = at });
            if (teleport)
            {
                HoldAt(at, hold);
                TeleportEvent(at, _mapId);
            }
            WowLog.Info("Online: " + (v != null ? (v.Mode == VehicleMode.Possess ? "possession of " : "vehicle ") + VehName(v.Guid) + " ended" : "vehicle state cleared") +
                        " (" + why + ")" + (teleport ? ", our character at " + at : ""));
        }

        // ================================================================== driving state

        void VehDriveBegin(VehicleView v)
        {
            if (_vehDriving) return;
            _vehDriving = true;
            _vehSavedCanFly = _player.CanFly;
            _vehSavedMountDisplay = _player.MountDisplayId;
            OnlineObject o;
            _objects.TryGetValue(v.Guid, out o);
            ushort f2 = o != null && o.Move != null ? o.Move.Flags2 : (ushort)0;
            var vi = VehicleDb.Vehicle(v.VehicleId);
            if (vi != null) f2 |= VehicleDb.Flags2(vi.Flags);
            WowLog.Info("Online: driving " + VehName(v.Guid) + " as the mover (speeds run " + (o != null ? o.Speeds[MoveType.Run] : 0f).ToString("0.0") + " / flight " +
                        (o != null ? o.Speeds[MoveType.Flight] : 0f).ToString("0.0") + ", flags2 0x" + f2.ToString("X") + ", display " + v.DisplayId + ") - the ride draws it as the mount");
        }

        void VehDriveEnd()
        {
            if (!_vehDriving) return;
            _vehDriving = false;
            CopySpeedsToPlayer();
            _player.CanFly = _vehSavedCanFly;
            _player.MountDisplayId = _vehSavedMountDisplay;
            RefreshRooted();
            WowLog.Info("Online: the player's own speeds, flight and mount display are back (driving ended)");
        }

        // ================================================================== IWowSession

        /// <summary>Leaves the vehicle or ends a possession (s6.2 Leaving). False when the seat does not allow it / nothing to leave.</summary>
        public bool LeaveVehicle()
        {
            var v = _player.Vehicle;
            if (v == null || _world == null) return false;
            if (v.Mode == VehicleMode.Possess)
            {
                // COMMAND_ABANDON on a charm = StopCastingCharm (PetHandler.cpp:277-281); CMSG_PET_ACTION: uint64 unit; uint32 data; uint64 target
                _world.Send(Op.CMSG_PET_ACTION, new PacketWriter().U64(v.Guid).U32(3u | 0x07u << 24).U64(0));
                WowLog.Info("Online: ending the possession of " + VehName(v.Guid) + " (CMSG_PET_ACTION COMMAND_ABANDON - ours: the aura the client would cancel is not known to us)");
                return true;
            }
            if (!v.CanLeave)
            {
                Error(VehCantText);
                WowLog.Info("Online: leave refused here - VehicleSeat " + v.SeatId + " (flags 0x" + v.SeatFlags.ToString("X8") + ") has no CAN_ENTER_OR_EXIT (DBCStructure.h:2149-2154)");
                return false;
            }
            if ((v.Mode == VehicleMode.Drive || v.Mode == VehicleMode.Turret) && _vehMover == v.Guid)
            {
                // CMSG_DISMISS_CONTROLLED_VEHICLE (VehicleHandler.cpp:26-59): packed vehicle guid + the vehicle's MovementInfo
                WowPos at = VehUnitPos(v.Guid, _player.Pos);
                var w = new PacketWriter().PackedGuid(v.Guid);
                VehMoverInfoCore(0u, at, at.O).Write(w);
                _world.Send(Op.CMSG_DISMISS_CONTROLLED_VEHICLE, w);
                WowLog.Info("Online: leaving " + VehName(v.Guid) + " (CMSG_DISMISS_CONTROLLED_VEHICLE at " + at + ")");
            }
            else
            {
                _world.Send(Op.CMSG_REQUEST_VEHICLE_EXIT);   // empty (VehicleHandler.cpp:229-245)
                WowLog.Info("Online: leaving " + VehName(v.Guid) + " (CMSG_REQUEST_VEHICLE_EXIT)");
            }
            return true;
        }

        /// <summary>seat -1 = previous, -2 = next, else that seat index (VehicleHandler.cpp:61-140; refused unless CAN_SWITCH).</summary>
        public void ChangeSeat(int seat)
        {
            var v = _player.Vehicle;
            if (v == null || v.Mode == VehicleMode.Possess || _world == null || !v.CanSwitchSeat)
            {
                Error(VehCantText);
                WowLog.Info("Online: seat change refused here (" + (v == null ? "not in a vehicle" : v.Mode == VehicleMode.Possess ? "a possession" : "VehicleSeat " + v.SeatId + " has no CAN_SWITCH 0x04000000") + ")");
                return;
            }
            if (seat == -1) _world.Send(Op.CMSG_REQUEST_VEHICLE_PREV_SEAT);
            else if (seat == -2) _world.Send(Op.CMSG_REQUEST_VEHICLE_NEXT_SEAT);
            else if (seat >= 0 && seat < 8) _world.Send(Op.CMSG_REQUEST_VEHICLE_SWITCH_SEAT, new PacketWriter().PackedGuid(v.Guid).U8(seat));
            else return;
            WowLog.Info("Online: seat change requested on " + VehName(v.Guid) + " (" + (seat == -1 ? "previous" : seat == -2 ? "next" : "seat " + seat) + ")");
        }

        /// <summary>Turret / aimed vehicle: the wanted facing and aim pitch; TickVehicle turns at Vehicle.dbc speeds and sends the packets.</summary>
        public void SetVehicleAim(float orientation, float pitch)
        {
            var v = _player.Vehicle;
            if (v == null) return;
            float lo = Math.Min(v.PitchMin, v.PitchMax), hi = Math.Max(v.PitchMin, v.PitchMax);
            float p = float.IsNaN(pitch) ? v.AimPitch : Math.Max(lo, Math.Min(hi, pitch));
            if (v.Mode == VehicleMode.Turret && v.CanSteer && _vehMover == v.Guid)
            {
                _aimWant = true;
                _aimWantO = float.IsNaN(orientation) ? _aimO : NormalizeO(orientation);
                _aimWantP = p;
                return;
            }
            // a driven vehicle / possessed unit / a passenger: the ride owns the facing; the aim pitch is kept for aimed spells (ours)
            v.AimPitch = p;
            if (!_vehDriveAimLogged)
            {
                _vehDriveAimLogged = true;
                WowLog.Info("Online: aim pitch " + p.ToString("0.000") + " kept for aimed spells (mode " + v.Mode + ": the facing is the ride's) - logged once");
            }
        }

        // ================================================================== vehicle spells

        /// <summary>Casts Player.ControlBar slot 0..9 of a Vehicle bar (area pets' UseControlSlot / UseControlSlotAt call it). dest = the
        /// circle's place for a NeedsPlace spell, null otherwise.</summary>
        internal void VehCast(int slot, ulong target, WowPos? dest)
        {
            var bar = _player.ControlBar;
            if (bar == null || slot < 0 || slot >= bar.Slots.Length || bar.Slots[slot] == null || bar.Slots[slot].Kind != ControlSlotKind.Spell) return;
            VehCastSpell(bar.Slots[slot].Action, target, dest);
        }

        /// <summary>CMSG_CAST_SPELL as the vehicle (SpellHandler.cpp:378-552: the server casts with m_mover = the vehicle when it has the spell).
        /// Aimed (TARGET_DEST_TRAJ 89): castFlags 0x02, SOURCE + DEST_LOCATION, then float elevation, float speed, uint8 0
        /// (HandleClientCastFlags, SpellHandler.cpp:35-56).</summary>
        internal bool VehCastSpell(int spellId, ulong target, WowPos? dest)
        {
            var v = _player.Vehicle;
            if (_world == null || v == null || spellId <= 0) return false;
            var s = Db.Find(spellId);
            if (s == null) { WowLog.Info("Online: vehicle spell " + spellId + " is not in Spell.dbc - not cast"); return false; }
            WowPos vpos = VehUnitPos(v.Guid, _player.Pos);
            float facing = v.Mode == VehicleMode.Turret && _vehMover == v.Guid ? _aimO : vpos.O;
            uint mask; ulong obj = 0;
            WowPos src = vpos, dst;
            byte castFlags = 0;
            bool aimed = VehAimed(s);
            if (aimed)
            {
                src = new WowPos(vpos.X, vpos.Y, vpos.Z + VehicleDb.ShotHeight, facing);
                dst = VehicleDb.BallisticDest(src, facing, v.AimPitch, s.Speed, s.RangeMax, _groundZ);
                mask = TfSourceLocation | TfDestLocation;
                castFlags = 0x02;
            }
            else if (dest.HasValue)
            {
                dst = dest.Value;
                mask = TfDestLocation | ((s.Targets & TfSourceLocation) != 0 ? TfSourceLocation : 0u);
            }
            else
            {
                string why = SpTargets(s, target, out mask, out obj, out dst);
                if (why != null) { Error(why); WowLog.Info("Online: vehicle spell " + spellId + " '" + s.Name + "' refused here: " + why); return false; }
            }
            _castCount++;
            if (_castCount == 0) _castCount = 1;            // 0 marks triggered casts in AC
            var w = new PacketWriter().U8(_castCount).U32((uint)spellId).U8(castFlags);
            SpWriteTargets(w, mask, obj, src, dst);
            if (aimed) w.F32(v.AimPitch).F32(s.Speed).U8(0);
            _world.Send(Op.CMSG_CAST_SPELL, w);
            if (_vehCastLogs++ < 20)
                WowLog.Info("Online: vehicle spell " + spellId + " '" + s.Name + "' cast as " + VehName(v.Guid) + " (mask 0x" + mask.ToString("X") +
                            (aimed ? ", aimed: elevation " + v.AimPitch.ToString("0.000") + ", speed " + s.Speed.ToString("0.0") + ", lands at " + dst : "") +
                            (dest.HasValue ? ", at " + dst : "") + ")");
            return true;
        }

        /// <summary>TARGET_DEST_TRAJ (89) in any effect: the shot follows the vehicle's aim.</summary>
        static bool VehAimed(SpellInfo s)
        {
            if (s == null || s.Effects == null) return false;
            for (int i = 0; i < s.Effects.Length; i++)
                if (s.Effects[i] != null && (s.Effects[i].TargetA == 89 || s.Effects[i].TargetB == 89)) return true;
            return false;
        }

        // ================================================================== per frame

        partial void TickVehicle(float dt)
        {
            if (_vehPassengers.Count > 0) VehTickPassengers();
            var v = _player.Vehicle;
            if (v == null)
            {
                if (_vehCtl != 0 && _vehCtlPendingAt >= 0 && _now - _vehCtlPendingAt > VehCtlWaitSeconds)
                {
                    WowLog.Info("Online: no seat arrived " + VehCtlWaitSeconds + " s after the control grant of " + VehName(_vehCtl) + " - treated as a possession");
                    VehStartPossess(_vehCtl);
                }
                return;
            }
            OnlineObject o;
            bool have = _objects.TryGetValue(v.Guid, out o) && o.HasPos;
            if (have)
            {
                _vehSeenAt = _now;
                if (o.State != null)
                {
                    if (v.VehicleId == 0 && o.State.VehicleId != 0 && v.Mode != VehicleMode.Possess)
                    {
                        v.VehicleId = o.State.VehicleId;
                        VehFillSeat(v);
                        if (_vehSeat != null && _vehSeat.SeatOffset.X == 0f && _vehSeat.SeatOffset.Y == 0f && _vehSeat.SeatOffset.Z == 0f)
                        {
                            var si = VehicleDb.SeatOf(v.VehicleId, v.SeatIndex);
                            if (si != null) v.SeatOffset = new WowPos(si.OffX, si.OffY, si.OffZ, v.SeatOffset.O);
                        }
                        v.Counter++;
                        WowLog.Info("Online: " + VehName(v.Guid) + " arrived - Vehicle " + v.VehicleId + " seat " + v.SeatIndex + " (VehicleSeat " + v.SeatId + ") filled in");
                    }
                    if (v.DisplayId == 0 && o.State.DisplayId != 0) { v.DisplayId = o.State.DisplayId; v.Counter++; }
                    if (string.IsNullOrEmpty(v.Name) && !string.IsNullOrEmpty(o.State.Name)) v.Name = o.State.Name;
                }
            }
            else if (_vehSeenAt >= 0 && _now - _vehSeenAt > VehGoneSeconds)
            {
                VehEnd("the " + (v.Mode == VehicleMode.Possess ? "possessed unit" : "vehicle") + " left sight", v.Mode == VehicleMode.Possess ? _vehBodyPos : _player.Pos, true, VehExitHold);
                return;
            }
            if (_player.Dead) { VehEnd("death", _player.Pos, false, 0); return; }
            if (_vehUnrootAt >= 0 && _vehMover == 0 && _now - _vehUnrootAt > VehUnrootExitSeconds)
            {
                VehEnd("unrooted " + VehUnrootExitSeconds + " s ago without an exit path", _player.Pos, true, VehExitHold);
                return;
            }
            // the server's path on the controlled / possessed unit; its flight
            bool serverMoving = have && o.Spline != null && !o.Spline.Finished(_now);
            if (serverMoving != v.ServerMoving)
            {
                v.ServerMoving = serverMoving;
                v.Counter++;
                if (v.CanSteer) WowLog.Info(serverMoving ? "Online: the server moves our controlled unit " + VehName(v.Guid) + " (spline " + o.Spline.Id + ") - steering paused" : "Online: the server's path on " + VehName(v.Guid) + " ended - steering resumes");
            }
            bool canFly = have && o.Move != null && (o.Move.Flags & (MoveFlags.CanFly | MoveFlags.Flying | MoveFlags.DisableGravity)) != 0 || (_vehCanFlySet && _vehMover == v.Guid);
            if (canFly != v.CanFly)
            {
                v.CanFly = canFly;
                v.Counter++;
                WowLog.Info("Online: " + VehName(v.Guid) + (canFly ? " flies" : " does not fly") + " (its movement flags)");
            }
            bool steering = (v.Mode == VehicleMode.Drive || v.Mode == VehicleMode.Possess) && v.CanSteer && _vehMover == v.Guid;
            if (steering)
            {
                // the player's ride state shows the controlled unit's (WowMount drives and Gear syncs it as a mount)
                if (have) for (int t = 0; t < MoveType.Count; t++) SetPlayerSpeed(t, o.Speeds[t]);
                _player.CanFly = v.CanFly;
                _player.MountDisplayId = v.DisplayId;
                _player.Rooted = _vehRooted;
                if (!_vehSpeedLogged && have)
                {
                    _vehSpeedLogged = true;
                    WowLog.Info("Online: the ride uses the controlled unit's speeds (run " + o.Speeds[MoveType.Run].ToString("0.0") + ", flight " + o.Speeds[MoveType.Flight].ToString("0.0") + ") - logged once");
                }
            }
            if (steering && !v.ServerMoving && _motion.Driven)
            {
                // the server does not echo a mover's own packets: the unit is where the ride put it (Player.Pos = its feet). Only once
                // WowMount rides it - before that (WowVehicle puts the avatar on it first) the unit stays where the server has it.
                if (have)
                {
                    WowPos p = _player.Pos;
                    o.X = p.X; o.Y = p.Y; o.Z = p.Z; o.O = p.O;
                    if (o.Move != null)
                    {
                        o.Move.X = p.X; o.Move.Y = p.Y; o.Move.Z = p.Z; o.Move.O = p.O;
                        o.Move.Flags &= ~(MoveFlags.MaskMoving | MoveFlags.MaskTurning);
                    }
                    o.MoveTime = _now;
                    if (o.State != null) o.State.Pos = p;
                }
                return;
            }
            // seated (passenger / turret) or the server moves the controlled unit: our character goes with it
            if (have)
            {
                WowPos up = new WowPos(o.X, o.Y, o.Z, o.O);
                if (v.Mode == VehicleMode.Turret && _vehMover == v.Guid) VehTickAim(v, o, dt);
                if (_vehSeat != null && (v.Mode == VehicleMode.Passenger || v.Mode == VehicleMode.Turret))
                {
                    var sp = _vehSeat.SeatOffset;
                    _player.Pos = VehicleDb.PassengerPosition(sp.X, sp.Y, sp.Z, sp.O, up);
                }
                else _player.Pos = up;   // a driven / possessed unit the ride does not steer now: its feet
            }
        }

        // ---- turret aim (s6.2 Turret)

        void VehAimInit(VehicleView v)
        {
            OnlineObject o;
            _objects.TryGetValue(v.Guid, out o);
            _aimO = o != null ? NormalizeO(o.O) : 0f;
            _aimP = o != null && o.Move != null ? o.Move.Pitch : 0f;
            float lo = Math.Min(v.PitchMin, v.PitchMax), hi = Math.Max(v.PitchMin, v.PitchMax);
            _aimP = Math.Max(lo, Math.Min(hi, _aimP));
            _aimSentO = _aimO; _aimSentP = _aimP;
            _aimWant = false;
            _aimDirty = false;
            _aimSentAt = -1;
            v.AimPitch = _aimP;
        }

        void VehTickAim(VehicleView v, OnlineObject o, float dt)
        {
            if (!_aimWant) return;
            float dO = AngleDelta(_aimO, _aimWantO);
            float maxO = v.TurnSpeed > 0f ? v.TurnSpeed * dt : float.MaxValue;
            float newO = NormalizeO(_aimO + Math.Max(-maxO, Math.Min(maxO, dO)));
            float dP = _aimWantP - _aimP;
            float maxP = v.PitchSpeed > 0f ? v.PitchSpeed * dt : float.MaxValue;
            float newP = _aimP + Math.Max(-maxP, Math.Min(maxP, dP));
            bool changed = Math.Abs(AngleDelta(_aimO, newO)) > 1e-4f || Math.Abs(newP - _aimP) > 1e-4f;
            _aimO = newO; _aimP = newP;
            v.AimPitch = newP;
            // our local view turns at once (the server does not echo the mover's packets)
            o.O = newO;
            if (o.Move != null) { o.Move.O = newO; o.Move.Pitch = newP; }
            if (o.State != null) { var p = o.State.Pos; p.O = newO; o.State.Pos = p; }
            if (changed)
            {
                _aimDirty = true;
                if (_aimSentAt < 0 || _now - _aimSentAt >= VehAimSendEvery) VehSendAim(v, o);
            }
            else if (_aimDirty) VehSendAim(v, o);
        }

        /// <summary>MSG_MOVE_SET_FACING / MSG_MOVE_SET_PITCH with the vehicle's packed guid + its MovementInfo without moving flags (a
        /// DISABLE_MOVE mover may still turn and pitch: MovementHandler.cpp:549-560 drops only MASK_MOVING packets).</summary>
        void VehSendAim(VehicleView v, OnlineObject o)
        {
            bool facing = Math.Abs(AngleDelta(_aimSentO, _aimO)) > 1e-4f;
            ushort op = facing ? Op.MSG_MOVE_SET_FACING : Op.MSG_MOVE_SET_PITCH;
            uint flags = o.Move != null ? o.Move.Flags & ~(MoveFlags.MaskMoving | MoveFlags.MaskTurning) : 0u;
            var mi = VehMoverInfoCore(flags, new WowPos(o.X, o.Y, o.Z, _aimO), _aimO);
            mi.Flags &= ~(MoveFlags.MaskMoving | MoveFlags.MaskTurning);
            mi.Pitch = _aimP;
            var w = new PacketWriter().PackedGuid(v.Guid);
            mi.Write(w);
            _world.Send(op, w);
            _aimSentO = _aimO; _aimSentP = _aimP;
            _aimSentAt = _now;
            bool stillMoving = Math.Abs(AngleDelta(_aimO, _aimWantO)) > 1e-4f || Math.Abs(_aimWantP - _aimP) > 1e-4f;
            _aimDirty = stillMoving;
            if (_aimLogs++ < 5)
                WowLog.Info("Online: turret " + VehName(v.Guid) + " aim facing " + _aimO.ToString("0.000") + " pitch " + _aimP.ToString("0.000") + " sent (" +
                            (facing ? "MSG_MOVE_SET_FACING" : "MSG_MOVE_SET_PITCH") + ", flags2 0x" + mi.Flags2.ToString("X") + ")");
        }

        static float AngleDelta(float from, float to)
        {
            double d = (to - from) % (2 * Math.PI);
            if (d > Math.PI) d -= 2 * Math.PI;
            else if (d < -Math.PI) d += 2 * Math.PI;
            return (float)d;
        }

        // ---- other units' seats (s6.2 Other passengers)

        void VehMarkSeated(OnlineObject o, VehPassenger vp)
        {
            var st = o.State;
            if (st == null) return;
            st.TransportGuid = vp.Vehicle;
            st.SeatIndex = vp.Seat;
            OnlineObject veh;
            int vid = _objects.TryGetValue(vp.Vehicle, out veh) && veh.State != null ? veh.State.VehicleId : 0;
            var si = VehicleDb.SeatOf(vid, vp.Seat);
            st.HiddenInSeat = si != null && si.HidePassenger;
            st.SeatAnim = si != null ? si.RideAnimLoop : 0;
            VehPlacePassenger(o, vp, veh);
        }

        void VehTickPassengers()
        {
            _vehPassengerDrop.Clear();
            foreach (var kv in _vehPassengers)
            {
                OnlineObject o, veh;
                if (!_objects.TryGetValue(kv.Key, out o)) { _vehPassengerDrop.Add(kv.Key); continue; }
                if (!_objects.TryGetValue(kv.Value.Vehicle, out veh) || !veh.HasPos)
                {
                    if (!kv.Value.GoneLogged) { kv.Value.GoneLogged = true; WowLog.Info("Online: the vehicle of " + VehName(kv.Key) + " is out of sight - it keeps its last position (logged once)"); }
                    continue;
                }
                kv.Value.GoneLogged = false;
                if (o.State != null && o.State.HiddenInSeat == false && o.State.SeatIndex >= 0 && veh.State != null && veh.State.VehicleId != 0)
                {
                    var si = VehicleDb.SeatOf(veh.State.VehicleId, kv.Value.Seat);
                    if (si != null) { o.State.HiddenInSeat = si.HidePassenger; o.State.SeatAnim = si.RideAnimLoop; }
                }
                VehPlacePassenger(o, kv.Value, veh);
            }
            foreach (ulong g in _vehPassengerDrop) _vehPassengers.Remove(g);
        }

        /// <summary>CalculatePassengerPosition (VehicleDefines.h:143-152): the vehicle's current pose + the local offset / path point.</summary>
        void VehPlacePassenger(OnlineObject o, VehPassenger vp, OnlineObject veh)
        {
            if (veh == null || !veh.HasPos) return;
            float lx = vp.LX, ly = vp.LY, lz = vp.LZ, lo = vp.LO;
            if (vp.Local != null)
            {
                float x, y, z, dirO;
                vp.Local.Evaluate(_now, out x, out y, out z, out dirO);
                lx = x; ly = y; lz = z;
                if (vp.Local.Finished(_now)) vp.Local = null;
            }
            WowPos w = VehicleDb.PassengerPosition(lx, ly, lz, lo, new WowPos(veh.X, veh.Y, veh.Z, veh.O));
            o.X = w.X; o.Y = w.Y; o.Z = w.Z; o.O = w.O; o.HasPos = true;
            if (o.State != null) o.State.Pos = w;
        }

        // ---- helpers

        WowPos VehUnitPos(ulong g, WowPos fallback)
        {
            OnlineObject o;
            return _objects.TryGetValue(g, out o) && o.HasPos ? new WowPos(o.X, o.Y, o.Z, o.O) : fallback;
        }

        /// <summary>The last point of a path (+ its final angle when it has one, else o).</summary>
        static WowPos VehSplineEnd(SplinePath s, float o)
        {
            if (s == null || s.Count == 0) return new WowPos(0f, 0f, 0f, o);
            int n = s.Count - 1;
            return new WowPos(s.Pts[n * 3], s.Pts[n * 3 + 1], s.Pts[n * 3 + 2], s.FacingKind == 1 ? s.FacingAngle : o);
        }

        string VehName(ulong g)
        {
            OnlineObject o;
            string n = _objects.TryGetValue(g, out o) && o.State != null ? o.State.Name : null;
            return (string.IsNullOrEmpty(n) ? "unit" : n) + " (0x" + g.ToString("X") + ")";
        }
    }
}
