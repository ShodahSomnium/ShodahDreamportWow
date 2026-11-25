using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 7 (lead contract stub PLAN7 s3.2, filled by owner Online): mount state from the server (UNIT_FLAG_MOUNT +
    // UNIT_FIELD_MOUNTDISPLAYID, SMSG_DISMOUNT), dismounting (CMSG_CANCEL_MOUNT_AURA / CMSG_CANCEL_AURA), the movement acks a mount needs
    // (SMSG_MOVE_SET_COLLISION_HGT, SMSG_MOVE_SET/UNSET_CAN_FLY, hover / feather fall / water walk / gravity), the speeds copied into
    // PlayerState, and the ride's motion (SetPlayerMotion, sent by OnlineSession.Rider.cs). The server decides every rule; nothing here
    // re-derives a speed, a display or a dismount.
    public sealed partial class OnlineSession
    {
        PlayerMotion _motion;
        bool _wasDriven;
        PlayerPlace _place;
        bool _placeSeen;
        uint _stateFlags;                 // HOVER / WATERWALKING / FALLING_SLOW / DISABLE_GRAVITY the server turned on (acked)
        long _selfDismountPair = -1;      // (flag, display) our values held when SMSG_DISMOUNT came: stay dismounted while they still say so
        bool _vehicleLogged;
        double _dismountAskedAt = -10, _mountLogAt = -1;
        double _collisionAckAt = -100, _runAckAt = -100, _mountUpAt = -100;   // for the mount-up log line (acks within 2 s of the mount-up)

        void RegisterMountHandlers()
        {
            _world.On(Op.SMSG_DISMOUNT, OnDismount, this);
            _world.On(Op.SMSG_MOVE_SET_COLLISION_HGT, OnCollisionHeight, this);
            _world.On(Op.SMSG_MOVE_SET_CAN_FLY, p => OnCanFly(p, true), this);
            _world.On(Op.SMSG_MOVE_UNSET_CAN_FLY, p => OnCanFly(p, false), this);
            // priority 2: the other flag changes of HandleMoveFlagChangeOpcode (same layout: packed guid, uint32 counter)
            _world.On(Op.SMSG_MOVE_SET_HOVER, p => OnFlagChange(p, Op.CMSG_MOVE_HOVER_ACK, MoveFlags.Hover, true, true, "hover"), this);
            _world.On(Op.SMSG_MOVE_UNSET_HOVER, p => OnFlagChange(p, Op.CMSG_MOVE_HOVER_ACK, MoveFlags.Hover, false, true, "hover"), this);
            _world.On(Op.SMSG_MOVE_FEATHER_FALL, p => OnFlagChange(p, Op.CMSG_MOVE_FEATHER_FALL_ACK, MoveFlags.FallingSlow, true, true, "feather fall"), this);
            _world.On(Op.SMSG_MOVE_NORMAL_FALL, p => OnFlagChange(p, Op.CMSG_MOVE_FEATHER_FALL_ACK, MoveFlags.FallingSlow, false, true, "feather fall"), this);
            _world.On(Op.SMSG_MOVE_WATER_WALK, p => OnFlagChange(p, Op.CMSG_MOVE_WATER_WALK_ACK, MoveFlags.WaterWalking, true, true, "water walk"), this);
            _world.On(Op.SMSG_MOVE_LAND_WALK, p => OnFlagChange(p, Op.CMSG_MOVE_WATER_WALK_ACK, MoveFlags.WaterWalking, false, true, "water walk"), this);
            _world.On(Op.SMSG_MOVE_GRAVITY_DISABLE, p => OnFlagChange(p, Op.CMSG_MOVE_GRAVITY_DISABLE_ACK, MoveFlags.DisableGravity, true, false, "gravity off"), this);
            _world.On(Op.SMSG_MOVE_GRAVITY_ENABLE, p => OnFlagChange(p, Op.CMSG_MOVE_GRAVITY_ENABLE_ACK, MoveFlags.DisableGravity, false, false, "gravity off"), this);
        }

        // ------------------------------------------------------------------ IWowSession (iteration 7)

        /// <summary>CMSG_CANCEL_MOUNT_AURA (empty body, MiscHandler HandleCancelMountAuraOpcode: Dismount + remove SPELL_AURA_MOUNTED). No-op on foot.</summary>
        public void Dismount()
        {
            if (_player.Vehicle != null && _player.Vehicle.Mode != VehicleMode.Passenger) { LeaveVehicle(); return; }   // iteration 11 (pets hook): leave a driven vehicle / possessed unit
            if (!_player.Mounted || _world == null) return;
            _world.Send(Op.CMSG_CANCEL_MOUNT_AURA);
            _dismountAskedAt = _now;
            WowLog.Info("Online: dismount asked (CMSG_CANCEL_MOUNT_AURA)");
        }

        /// <summary>CMSG_CANCEL_AURA (uint32 spell; HandleCancelAuraOpcode refuses non-positive / passive / NO_AURA_CANCEL silently); the mount aura = Dismount().</summary>
        public void CancelAura(int spellId)
        {
            if (spellId <= 0 || _world == null) return;
            if (_player.Mounted && (spellId == _player.MountSpellId || spellId == _mountAuraSpell || IsMountSpell(spellId))) { Dismount(); return; }
            _world.Send(Op.CMSG_CANCEL_AURA, new PacketWriter().U32((uint)spellId));
            WowLog.Info("Online: cancel aura " + spellId + " '" + SpellName(spellId) + "' (CMSG_CANCEL_AURA)");
        }

        /// <summary>The ride's motion of this frame (WowGame calls it right before SetPlayerPosition). Driven = the rider's sender runs.</summary>
        public void SetPlayerMotion(PlayerMotion motion)
        {
            _motion = motion;
        }

        /// <summary>Online the server decides indoors / flying areas / water: only log changes (area, outdoors, liquid).</summary>
        public void SetPlayerPlace(PlayerPlace place)
        {
            bool changed = !_placeSeen || place.Known != _place.Known || place.Outdoors != _place.Outdoors || place.AreaId != _place.AreaId
                           || place.InWmo != _place.InWmo || (place.LiquidDepth > 0f) != (_place.LiquidDepth > 0f);
            _place = place;
            _placeSeen = true;
            if (!changed) return;
            WowLog.Info("Online: place " + (!place.Known ? "unknown" : place.Outdoors ? "outdoors" : "indoors") + " (" + (place.Where ?? "?") + ")" +
                        (place.LiquidDepth > 0f ? ", liquid " + place.LiquidDepth.ToString("0.00") + " yd deep" : "") + " - the server applies its own rules");
        }

        /// <summary>Lead decision D13: a GM command for the in-world testing buttons (paced chat, OnlineSession.Chat.cs).</summary>
        public void RunGmCommand(string command)
        {
            QueueGmCommand(command);
        }

        // ------------------------------------------------------------------ mount state from the update fields

        static long MountPair(OnlineObject o)
        {
            return ((long)((o.U32(UF.UNIT_FIELD_FLAGS) & UnitFlagMount) != 0 ? 1 : 0) << 32) | o.U32(UF.UNIT_FIELD_MOUNTDISPLAYID);
        }

        /// <summary>RefreshSelf: UNIT_FLAG_MOUNT + UNIT_FIELD_MOUNTDISPLAYID (Unit::Mount sets both; display 0 + flag = a vehicle mount).</summary>
        void UpdateSelfMount(OnlineObject o)
        {
            if (TaxiOwnsMount(o)) return;                   // iteration 11 (motion hook 3): the taxi's mount is not a ridden mount (OnlineSession.Taxi.cs)
            long pair = MountPair(o);
            if (_selfDismountPair >= 0)
            {
                if (pair == _selfDismountPair) { _player.MountDisplayId = 0; return; }   // the values have not caught up with SMSG_DISMOUNT yet
                _selfDismountPair = -1;
            }
            bool flag = (pair >> 32) != 0;
            int display = (int)(pair & 0xFFFFFFFF);
            if (flag && display == 0 && !_vehicleLogged)
            {
                _vehicleLogged = true;
                WowLog.Warn("Online: UNIT_FLAG_MOUNT with mount display 0 = a vehicle mount (not in iteration 7) - not ridden (logged once)");
            }
            bool mounted = flag && display != 0;
            if (mounted && (!_player.Mounted || display != _player.MountDisplayId)) MountUp(display);
            else if (!mounted && _player.Mounted) DismountNow("values: UNIT_FLAG_MOUNT " + (flag ? "on" : "off") + ", display " + display);
            else if (!mounted) _player.MountDisplayId = display;   // e.g. 0, or a vehicle's 0
        }

        void MountUp(int display)
        {
            _player.Mounted = true;
            _player.MountDisplayId = display;
            _player.MountDisplaySource = MountDisplaySource.Server;
            _player.MountCounter++;
            _mountUpAt = _now;
            ApplyMountSpell();
            if (_player.MountSpellId == 0) { _player.MountCreatureEntry = 0; _player.MountKind = _player.CanFly ? MountKind.Flying : MountKind.Ground; }
            string name = _player.MountSpellId != 0 ? SpellName(_player.MountSpellId) : null;
            _events.Enqueue(new GameEvent { Type = GameEventType.Mounted, SourceGuid = _player.Guid, SpellId = _player.MountSpellId, Amount = display, Text = name });
            _mountLogAt = _now + 1.0;                           // the full line once the speed / collision acks of the mount-up are in
            WowLog.Info("Online: mount-up - display " + display + " (server), spell " + (_player.MountSpellId != 0 ? _player.MountSpellId + " '" + name + "'" : "unknown (no mount aura yet)") +
                        ", creature entry " + _player.MountCreatureEntry + ", counter " + _player.MountCounter);
            LearnRide();
        }

        void DismountNow(string why)
        {
            int spell = _player.MountSpellId;
            var reason = _now - _dismountAskedAt < 5.0 ? DismountReason.Chosen : DismountReason.Server;
            _player.Mounted = false;
            _player.MountDisplayId = 0;
            _player.MountDisplaySource = MountDisplaySource.None;
            _player.MountSpellId = 0;
            _player.MountCreatureEntry = 0;
            _player.MountKind = MountKind.None;
            _dismountAskedAt = -10;
            _mountLogAt = -1;
            _events.Enqueue(new GameEvent { Type = GameEventType.Dismounted, SourceGuid = _player.Guid, SpellId = spell, Amount = (int)reason, Text = null });
            WowLog.Info("Online: dismounted (" + why + ") - " + (reason == DismountReason.Chosen ? "asked by us" : "by the server") + ", run speed " + _player.RunSpeed.ToString("0.00") + " yd/s");
        }

        void TickMountLog()
        {
            if (_mountLogAt < 0 || _now < _mountLogAt) return;
            _mountLogAt = -1;
            if (!_player.Mounted) return;
            WowLog.Info("Online: mounted display " + _player.MountDisplayId + " (server), run " + _player.RunSpeed.ToString("0.00") + " yd/s" + (_runAckAt >= _mountUpAt - 2.0 ? " acked" : " (no speed change received)") +
                        ", flight " + _player.FlightSpeed.ToString("0.00") + " yd/s, collision height " + (_collisionAckAt >= _mountUpAt - 2.0 ? _player.CollisionHeight.ToString("0.00") + " acked" : "not received") +
                        ", can fly " + (_player.CanFly ? "yes" : "no") + ", kind " + _player.MountKind + ", spell " + _player.MountSpellId);
        }

        /// <summary>Refresh (others): EntityState.Mounted from UNIT_FLAG_MOUNT, MountDisplayId; held at 0 after their SMSG_DISMOUNT until the values change.</summary>
        void UpdateOtherMount(OnlineObject o, EntityState s)
        {
            long pair = MountPair(o);
            if (o.DismountPair >= 0)
            {
                if (pair == o.DismountPair) { s.Mounted = false; s.MountDisplayId = 0; return; }
                o.DismountPair = -1;
            }
            s.Mounted = (pair >> 32) != 0;
            s.MountDisplayId = (int)(pair & 0xFFFFFFFF);
        }

        /// <summary>SMSG_DISMOUNT (Unit::Dismount, to everyone near): packed guid. Cleared at once, before the values update arrives.</summary>
        void OnDismount(WowPacket p)
        {
            ulong g = p.PackedGuid();
            if (g == _player.Guid)
            {
                if (TaxiDismount()) return;                 // iteration 11 (motion hook 3): on a taxi = the server ended the flight
                if (_self != null) _selfDismountPair = MountPair(_self);
                if (_player.Mounted) DismountNow("SMSG_DISMOUNT");
                _player.MountDisplayId = 0;
                return;
            }
            OnlineObject o;
            if (!_objects.TryGetValue(g, out o)) return;
            o.DismountPair = MountPair(o);
            if (o.State != null) { o.State.Mounted = false; o.State.MountDisplayId = 0; }
        }

        // ------------------------------------------------------------------ acks

        /// <summary>The flags an ack carries: those of our last movement packet (never FALLING: acks carry no jump data), CAN_FLY while the
        /// server lets us fly, never FLYING without it, plus the hover / water walk / feather fall / gravity state the server set.</summary>
        uint AckFlags()
        {
            return _sentFlags & ~MoveFlags.Falling;
        }

        WowPos AckPos { get { return _haveAvatar ? _avatarPos : _player.Pos; } }

        /// <summary>SMSG_MOVE_SET_COLLISION_HGT (Unit::Mount / Dismount): packed guid; uint32 counter; float height.
        /// Ack CMSG_MOVE_SET_COLLISION_HGT_ACK (HandleForceSpeedChangeAck): packed guid; uint32 counter; MovementInfo; float height.</summary>
        void OnCollisionHeight(WowPacket p)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            float h = p.F32();
            if (g != _player.Guid) return;
            _player.CollisionHeight = h;
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            OwnMovementInfo(AckFlags(), AckPos, _sentPos.O).Write(w);
            w.F32(h);
            _world.Send(Op.CMSG_MOVE_SET_COLLISION_HGT_ACK, w);
            _collisionAckAt = _now;
            WowLog.Info("Online: collision height " + h.ToString("0.00") + " yd acked (counter " + counter + ")");
        }

        /// <summary>SMSG_MOVE_SET_CAN_FLY / UNSET (Unit::SetCanFly): packed guid; uint32 counter. Ack CMSG_MOVE_SET_CAN_FLY_ACK
        /// (HandleMoveFlagChangeOpcode): packed guid; uint32 counter; MovementInfo (CAN_FLY after SET, cleared after UNSET); uint32 applied.
        /// The server sends an UNSET at every login (Player.cpp) - acked like any other.</summary>
        void OnCanFly(WowPacket p, bool set)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (g != _player.Guid) return;
            _player.CanFly = set;
            if (!set) _sentFlags &= ~(MoveFlags.Flying | MoveFlags.Ascending | MoveFlags.Descending);
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            OwnMovementInfo(AckFlags(), AckPos, _sentPos.O).Write(w);
            w.U32(set ? 1u : 0u);
            _world.Send(Op.CMSG_MOVE_SET_CAN_FLY_ACK, w);
            if (_player.Mounted && _player.MountSpellId == 0) _player.MountKind = set ? MountKind.Flying : MountKind.Ground;
            WowLog.Info("Online: can fly " + (set ? "set" : "unset") + " (counter " + counter + ") acked");
        }

        /// <summary>SMSG_MOVE_(UN)SET_HOVER / FEATHER_FALL / NORMAL_FALL / WATER_WALK / LAND_WALK / GRAVITY_DISABLE / ENABLE: packed guid;
        /// uint32 counter -> the matching CMSG_*_ACK (HandleMoveFlagChangeOpcode): packed guid; counter; MovementInfo; [uint32 applied, not for gravity].</summary>
        void OnFlagChange(WowPacket p, ushort ack, uint flag, bool set, bool withApplied, string what)
        {
            ulong g = p.PackedGuid();
            uint counter = p.U32();
            if (g != _player.Guid) return;
            if (set) _stateFlags |= flag; else _stateFlags &= ~flag;
            var w = new PacketWriter().PackedGuid(g).U32(counter);
            OwnMovementInfo(AckFlags(), AckPos, _sentPos.O).Write(w);
            if (withApplied) w.U32(set ? 1u : 0u);
            _world.Send(ack, w);
            WowLog.Info("Online: " + what + " " + (set ? "on" : "off") + " (counter " + counter + ") acked");
            RefreshMoveAuras();                             // iteration 11 (motion hook 3): PlayerState.SlowFall / WaterWalking / Hovering
        }

        /// <summary>A forced speed of ours (acked with the exact float) or our create block's speeds -> PlayerState (the ride's speeds).</summary>
        void SetPlayerSpeed(int type, float speed)
        {
            var p = _player;
            switch (type)
            {
                case MoveType.Walk: p.WalkSpeed = speed; break;
                case MoveType.Run: p.RunSpeed = speed; break;
                case MoveType.RunBack: p.RunBackSpeed = speed; break;
                case MoveType.Swim: p.SwimSpeed = speed; break;
                case MoveType.SwimBack: p.SwimBackSpeed = speed; break;
                case MoveType.TurnRate: p.TurnRate = speed; break;
                case MoveType.Flight: p.FlightSpeed = speed; break;
                case MoveType.FlightBack: p.FlightBackSpeed = speed; break;
            }
        }

        void CopySpeedsToPlayer()
        {
            for (int t = 0; t < MoveType.Count; t++) SetPlayerSpeed(t, _mySpeeds[t]);
        }

        static readonly string[] SpeedNames = { "walk", "run", "run back", "swim", "swim back", "turn rate", "flight", "flight back", "pitch rate" };

        void LogMountConstants()
        {
            WowLog.Info("Online: mounts - ours (not WoW's): cast time-out " + CastAnswerTimeout + " s, walk tolerance during a cast " + CastWalkTolerance + " yd, mount creature queries every " +
                        MountQuerySpacing + " s, GM commands " + GmBurst + " per burst " + GmSpacing + " s apart then " + GmPause + " s, rider heartbeats " + HeartbeatSeconds + " s (" +
                        RiderVerticalHeartbeat + " s pure vertical) + one at speed, SET_FACING / SET_PITCH >= " + RiderAngleInterval + " s and > " + RiderAngleStep +
                        " rad, others' pure ascend / descend drawn at " + OthersAscendFactor + " x their flight speed");
        }
    }
}
