// iteration 11 - owner motion (proposal-motion.md s7): the offline side of forced movement. Charge / Intercept / Intervene / Feral Charge
// - Bear (effect 96), Feral Charge - Cat (42 JUMP_DEST), Blink (29 LEAP), Disengage (138 LEAP_BACK), Shadowstep / Killing Spree's jumps
// (5 TELEPORT_UNITS with TargetB 65) and a creature's knockback (98) move the player as AzerothCore moves an online player; the player's
// own fear / confuse wander; PlayerState Rooted / Stunned / ControlLost / SlowFall / WaterWalking / Hovering every Tick. The calls come from
// spellfx's OfflineSession.Spells.cs / UnitSpells.cs (hooks 17a-c) and OfflineSession.cs Step (17d).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Motion;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        // AzerothCore values
        private const float ChargeSpeed = 42f;                 // SPEED_CHARGE (MotionMaster.h:136) when the spell has no speed
        private const float PlayerBaseRun = 7f;                // playerBaseMoveSpeed[MOVE_RUN] (CalculateJumpSpeeds, SpellEffects.cpp:1215)
        private const float FleeMinQuiet = 28f, FleeMaxQuiet = 43f;   // FleeingMovementGenerator.cpp:26-27
        private const float ConfuseWander = 4f;                // ConfusedMovementGenerator.cpp:28
        private const int ConfuseWaypoints = 24;               // MAX_CONF_WAYPOINTS (ConfusedMovementGenerator.h:24)
        // ours (logged once)
        private const float BlinkDefaultYards = 20f;           // Blink's distance when the spell row has no radius
        private const float BlinkStepUp = 2f;                  // yd: Blink stops before a step higher than this (no vmaps offline)
        private const float BehindExtra = 1f;                  // yd behind the target's reach for DEST_TARGET_BACK

        private int _offForcedId;
        private double _offForcedAt;
        private ulong _offChargeTarget;
        private double _offFleeNextAt, _offConfuseNextAt;
        private float[] _offConfusePoints;
        private int _offControlBits = -1;
        private bool _offMotionLogged;

        // ------------------------------------------------------------------ IWowSession

        /// <summary>The view finished PlayerState.ForcedMove id at pos: it clears; a charge starts the auto-attack at the arrival
        /// (PointMovementGenerator.cpp:264-269). Ignored when id is not the current move.</summary>
        public void ForcedMoveDone(int id, WowPos pos)
        {
            var m = _player.ForcedMove;
            if (m == null || m.Id != id || m.Kind == ForcedMoveKind.Taxi) return;
            _player.ForcedMove = null;
            _me.Pos = pos;
            _player.Pos = pos;
            _events.Enqueue(new GameEvent { Type = GameEventType.ForcedMoveEnded, SourceGuid = PlayerGuid, Amount = (int)m.Kind, SpellId = m.SpellId });
            if (_offChargeTarget != 0)
            {
                ulong t = _offChargeTarget;
                _offChargeTarget = 0;
                var c = CreatureOf(t);
                if (c != null && !c.Dead) StartAttack(t);
            }
            if (m.Kind == ForcedMoveKind.Fleeing) _offFleeNextAt = _time + 0.8 + Rand01() * 0.7;          // FleeingMovementGenerator: + urand(800, 1500)
            if (m.Kind == ForcedMoveKind.Confused) _offConfuseNextAt = _time + 0.6 + Rand01() * 0.6;      // ConfusedMovementGenerator: urand(600, 1200)
        }

        /// <summary>Offline: no transports - logged only.</summary>
        public void SetPlayerTransport(ulong transportGuid)
        {
            if (transportGuid != 0) WowLog.Info("OfflineSession: SetPlayerTransport(" + transportGuid.ToString("X") + ") - there are no transports offline");
        }

        /// <summary>TeleportUnits with TargetB 65 DEST_TARGET_BACK (Shadowstep 36563, Killing Spree 57840): behind the target.</summary>
        private static bool MotionTeleportSupported(SpellEffectInfo e) { return e != null && e.TargetB == 65; }

        // ------------------------------------------------------------------ the spell effects (spellfx's hooks 17b / 17c)

        /// <summary>CHARGE (96, SpellEffects.cpp:5145-5186): to the target's combat reach on the side of the caster, at
        /// min(speed or 42, max(28, run x 4)) yd/s (MoveSplineInit.cpp:106).</summary>
        partial void OfflineChargeMove(OfflineCreature c, SpellInfo s)
        {
            if (c == null) return;
            LogMotionOnce();
            WowPos me = _me.Pos;
            float dx = me.X - c.Pos.X, dy = me.Y - c.Pos.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            float reach = c.CombatReach > 0f ? c.CombatReach : 1.5f;
            float ux = d > 0.01f ? dx / d : 1f, uy = d > 0.01f ? dy / d : 0f;
            float ex = c.Pos.X + ux * reach, ey = c.Pos.Y + uy * reach;
            float ez = Ground(ex, ey, c.Pos.Z);
            float speed = Math.Min(s != null && s.Speed > 0f ? s.Speed : ChargeSpeed, Math.Max(28f, _player.RunSpeed * 4f));
            float len = Dist3(me.X, me.Y, me.Z, ex, ey, ez);
            int ms = Math.Max(1, (int)(len / speed * 1000f));
            _offChargeTarget = c.Guid;
            PublishOffline(new ForcedMoveView
            {
                Kind = ForcedMoveKind.Path, Points = new[] { me.X, me.Y, me.Z, ex, ey, ez }, DurationMs = ms, FacingGuid = c.Guid,
                SpellId = s != null ? s.Id : 0, Source = "offline " + (s != null ? s.Name : "charge")
            });
            WowLog.Info("OfflineSession: " + (s != null ? s.Name : "charge") + " - " + len.ToString("0.0") + " yd to " + CName(c, "?") + " in " + ms + " ms (" + speed.ToString("0") + " yd/s)");
        }

        /// <summary>JUMP_DEST (42, SpellEffects.cpp:1145-1210): Feral Charge - Cat 49376 behind the target (TargetA 65 DEST_TARGET_BACK),
        /// CalculateJumpSpeeds (:1212-1239) with MiscValue / MiscValueB, MoveJump's arc (MotionMaster.cpp:676-694, MoveSpline.cpp:185-188).</summary>
        partial void OfflineJumpDest(OfflineCreature c, SpellInfo s, SpellEffectInfo e)
        {
            if (c == null || e == null) return;
            LogMotionOnce();
            WowPos me = _me.Pos;
            WowPos dest = Behind(c);
            float dist2d = (float)Math.Sqrt((dest.X - me.X) * (dest.X - me.X) + (dest.Y - me.Y) * (dest.Y - me.Y));
            float mult = e.ValueMultiplier > 0f ? e.ValueMultiplier : 1f;
            float speedXY = Math.Min(PlayerBaseRun * 3f * mult, Math.Max(28f, _player.RunSpeed * 4f));
            float duration = dist2d / speedXY, durationSqr = duration * duration;
            float minH = e.MiscValue != 0 ? e.MiscValue / 10f : 0.5f, maxH = e.MiscValueB != 0 ? e.MiscValueB / 10f : 1000f;
            double g = ForcedMoveMath.Gravity;
            double height = durationSqr < minH * 8 / g ? minH : durationSqr > maxH * 8 / g ? maxH : g * durationSqr / 8;
            float speedZ = (float)Math.Sqrt(2 * g * height);
            if (s != null && s.Id == 49376) speedXY = (float)Math.Pow(speedZ * 10, 8);              // AC's override for Feral Charge - Cat
            float velocity = Math.Min(speedXY, Math.Max(28f, _player.RunSpeed * 4f));               // MoveSplineInit.cpp:106
            float len = Dist3(me.X, me.Y, me.Z, dest.X, dest.Y, dest.Z);
            int ms = Math.Max(1, (int)(len / velocity * 1000f));
            double amplitude = -ForcedMoveMath.FallElevation(speedZ / g, false, -speedZ);           // MotionMaster.cpp:683-684
            double secs = ms / 1000.0;
            _offChargeTarget = 0;
            PublishOffline(new ForcedMoveView
            {
                Kind = ForcedMoveKind.Path, Points = new[] { me.X, me.Y, me.Z, dest.X, dest.Y, dest.Z }, DurationMs = ms, FacingGuid = c.Guid,
                ParabolicAccel = (float)(amplitude * 8.0 / (secs * secs)), ParabolicStartMs = 0, SpellId = s != null ? s.Id : 0,
                Source = "offline " + (s != null ? s.Name : "jump")
            });
            WowLog.Info("OfflineSession: " + (s != null ? s.Name : "jump") + " - " + len.ToString("0.0") + " yd behind " + CName(c, "?") + " in " + ms + " ms, arc " + amplitude.ToString("0.00") + " yd");
        }

        /// <summary>LEAP (29, Blink: SpellEffects.cpp:4923-4936, DEST_CASTER_FRONT_LEAP): forward along the facing, stepped every yard on the
        /// ground, stopping before a step higher than 2 yd (ours: no vmaps offline), then the existing teleport.</summary>
        partial void OfflineLeap(SpellInfo s, SpellEffectInfo e)
        {
            LogMotionOnce();
            WowPos me = _me.Pos;
            float dist = e != null && e.Radius > 0f ? e.Radius : BlinkDefaultYards;
            float c = (float)Math.Cos(me.O), sn = (float)Math.Sin(me.O);
            float x = me.X, y = me.Y, z = me.Z;
            for (int i = 1; i <= (int)dist; i++)
            {
                float nx = me.X + c * i, ny = me.Y + sn * i;
                float nz = Ground(nx, ny, z + BlinkStepUp);
                if (nz - z > BlinkStepUp) break;
                x = nx; y = ny; z = nz;
            }
            var dest = new WowPos(x, y, z, me.O);
            TeleportPlayerOffline(dest, s != null ? s.Name : "Blink");
        }

        /// <summary>LEAP_BACK (138, Disengage: SpellEffects.cpp:5291-5316 -> Unit::JumpTo, Unit.cpp:15108-15128): speedXY = MiscValue / 10,
        /// speedZ = value / 10, backwards for SPELLFAMILY_HUNTER (9), else forwards.</summary>
        partial void OfflineLeapBack(SpellInfo s, SpellEffectInfo e)
        {
            if (e == null) return;
            LogMotionOnce();
            float o = _me.Pos.O + (s != null && s.SpellFamilyName == 9 ? (float)Math.PI : 0f);
            PublishOffline(new ForcedMoveView
            {
                Kind = ForcedMoveKind.Knockback, Start = _me.Pos, DirCos = (float)Math.Cos(o), DirSin = (float)Math.Sin(o),
                SpeedXY = e.MiscValue / 10f, SpeedUp = e.MinValue / 10f, SlowFall = _player.SlowFall, SpellId = s != null ? s.Id : 0,
                Source = "offline " + (s != null ? s.Name : "leap back")
            });
            WowLog.Info("OfflineSession: " + (s != null ? s.Name : "leap back") + " - " + (e.MiscValue / 10f).ToString("0.0") + " yd/s out, " + (e.MinValue / 10f).ToString("0.0") + " yd/s up");
        }

        /// <summary>TELEPORT_UNITS with TargetB 65 (Shadowstep, Killing Spree): behind the target, facing it.</summary>
        partial void OfflineTeleportBehind(OfflineCreature c)
        {
            if (c == null) return;
            LogMotionOnce();
            TeleportPlayerOffline(Behind(c), "a teleport behind " + CName(c, "the target"));
        }

        /// <summary>KNOCK_BACK (98) cast by an offline creature on the player (SpellEffects.cpp:5247-5249: speedXY = MiscValue x 0.1,
        /// speedZ = value x 0.1), away from the caster (Unit::KnockbackFrom).</summary>
        partial void OfflineKnockbackFrom(OfflineUnit caster, SpellEffectInfo e)
        {
            if (caster == null || e == null) return;
            LogMotionOnce();
            WowPos me = _me.Pos;
            float dx = me.X - caster.Pos.X, dy = me.Y - caster.Pos.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
            float ang = d > 0.001f ? (float)Math.Atan2(dy, dx) : Rand01() * 2f * (float)Math.PI;
            PublishOffline(new ForcedMoveView
            {
                Kind = ForcedMoveKind.Knockback, Start = me, DirCos = (float)Math.Cos(ang), DirSin = (float)Math.Sin(ang),
                SpeedXY = e.MiscValue * 0.1f, SpeedUp = e.MinValue * 0.1f, SlowFall = _player.SlowFall, Source = "offline knockback"
            });
        }

        // ------------------------------------------------------------------ per Tick (17d)

        partial void TickMotion(float dt)
        {
            var p = _player;
            bool feared = _me.Feared, confused = _me.Confused;
            p.Stunned = _me.Stunned;
            p.Rooted = _me.Rooted || p.Stunned;
            p.ControlLost = feared || confused || p.OnTaxi;
            p.SlowFall = _me.HasAuraType(105);                 // SPELL_AURA_FEATHER_FALL
            p.WaterWalking = _me.HasAuraType(104);             // SPELL_AURA_WATER_WALK
            p.Hovering = _me.HasAuraType(106);                 // SPELL_AURA_HOVER
            int bits = (p.Rooted ? 1 : 0) | (p.Stunned ? 2 : 0) | (p.ControlLost ? 4 : 0) | (p.SlowFall ? 8 : 0) | (p.WaterWalking ? 16 : 0) | (p.Hovering ? 32 : 0);
            if (bits != _offControlBits)
            {
                bool first = _offControlBits < 0;
                _offControlBits = bits;
                if (!first || bits != 0)
                {
                    _events.Enqueue(new GameEvent { Type = GameEventType.ControlChanged, SourceGuid = PlayerGuid, Amount = bits & 7 });
                    WowLog.Info("OfflineSession: control " + (bits == 0 ? "free" : (p.Rooted ? "rooted " : "") + (p.Stunned ? "stunned " : "") + (feared ? "feared " : "") + (confused ? "confused " : "") +
                                (p.SlowFall ? "slow-fall " : "") + (p.WaterWalking ? "water-walking " : "") + (p.Hovering ? "hovering" : "")));
                }
            }
            var m = p.ForcedMove;
            if (m != null)
            {
                // the view reports the end; a move it never finishes ends by itself (2 s past its duration, ours as online)
                double limit = m.Kind == ForcedMoveKind.Knockback ? 10.0 : m.DurationMs / 1000.0 + 2.0;
                if (_time - _offForcedAt > limit) { WowLog.Info("OfflineSession: forced " + m.Kind + " " + m.Id + " timed out"); ForcedMoveDone(m.Id, _me.Pos); }
                return;
            }
            if (p.Dead || p.OnTaxi) return;
            if (feared && !p.Rooted && _time >= _offFleeNextAt) FleeSegment();
            else if (!feared) _offFleeNextAt = 0;
            if (confused && !feared && !p.Rooted)
            {
                if (_offConfusePoints == null) BuildConfusePoints();
                if (_time >= _offConfuseNextAt) ConfuseSegment();
            }
            else if (!confused) _offConfusePoints = null;
        }

        /// <summary>FleeingMovementGenerator::GetPoint (FleeingMovementGenerator.cpp:180-216): away from the fear's caster into the
        /// 28-43 yd quiet range, run speed.</summary>
        private void FleeSegment()
        {
            WowPos me = _me.Pos;
            OfflineUnit caster = null;
            foreach (var a in _me.Auras)
            {
                if (a.Spell == null) continue;
                foreach (var e in a.Spell.Effects) if (e != null && e.IsAura && e.Aura == SpellAura.ModFear) { caster = UnitOf(a.Caster); break; }
                if (caster != null) break;
            }
            float casterDist = 0f, casterAngle = Rand01() * 2f * (float)Math.PI;
            if (caster != null)
            {
                float dx = me.X - caster.Pos.X, dy = me.Y - caster.Pos.Y;
                casterDist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (casterDist > 0.2f) casterAngle = (float)Math.Atan2(dy, dx);
            }
            float dist, angle;
            if (casterDist < FleeMinQuiet) { dist = (0.4f + Rand01() * 0.9f) * (FleeMinQuiet - casterDist); angle = casterAngle + (Rand01() - 0.5f) * (float)Math.PI / 4f; }
            else if (casterDist > FleeMaxQuiet) { dist = (0.4f + Rand01() * 0.6f) * (FleeMaxQuiet - FleeMinQuiet); angle = -casterAngle + (Rand01() - 0.5f) * (float)Math.PI / 2f; }
            else { dist = (0.6f + Rand01() * 0.6f) * (FleeMaxQuiet - FleeMinQuiet); angle = Rand01() * 2f * (float)Math.PI; }
            if (dist < 2f) { _offFleeNextAt = _time + 0.1; return; }                                     // MIN_PATH_LENGTH: recheck in 100 ms
            float x = me.X + (float)Math.Cos(angle) * dist, y = me.Y + (float)Math.Sin(angle) * dist, z = Ground(x, y, me.Z);
            float speed = _player.RunSpeed > 0.1f ? _player.RunSpeed : 7f;
            int ms = Math.Max(1, (int)(Dist3(me.X, me.Y, me.Z, x, y, z) / speed * 1000f));
            PublishOffline(new ForcedMoveView { Kind = ForcedMoveKind.Fleeing, Points = new[] { me.X, me.Y, me.Z, x, y, z }, DurationMs = ms, Source = "offline fear" });
        }

        /// <summary>ConfusedMovementGenerator::DoInitialize (ConfusedMovementGenerator.cpp:28-83): 25 waypoints within a 4 yd box around
        /// the start (a waypoint more than 3 yd up / down keeps the previous one).</summary>
        private void BuildConfusePoints()
        {
            WowPos me = _me.Pos;
            _offConfusePoints = new float[(ConfuseWaypoints + 1) * 3];
            float px = me.X, py = me.Y, pz = me.Z;
            for (int i = 0; i <= ConfuseWaypoints; i++)
            {
                float wx = me.X + ConfuseWander * Rand01() - ConfuseWander / 2f, wy = me.Y + ConfuseWander * Rand01() - ConfuseWander / 2f;
                float wz = Ground(wx, wy, me.Z);
                if (Math.Abs(me.Z - wz) > 3f) { wx = px; wy = py; wz = pz; }
                _offConfusePoints[i * 3] = wx; _offConfusePoints[i * 3 + 1] = wy; _offConfusePoints[i * 3 + 2] = wz;
                px = wx; py = wy; pz = wz;
            }
            _offConfuseNextAt = _time;
        }

        /// <summary>ConfusedMovementGenerator::DoUpdate (:128-150): walk to a random waypoint 1..24, then wait 600-1200 ms.</summary>
        private void ConfuseSegment()
        {
            WowPos me = _me.Pos;
            int i = 1 + (int)(Rand01() * ConfuseWaypoints);
            if (i > ConfuseWaypoints) i = ConfuseWaypoints;
            float x = _offConfusePoints[i * 3], y = _offConfusePoints[i * 3 + 1], z = _offConfusePoints[i * 3 + 2];
            float speed = _player.WalkSpeed > 0.1f ? _player.WalkSpeed : 2.5f;
            float len = Dist3(me.X, me.Y, me.Z, x, y, z);
            if (len < 0.2f) { _offConfuseNextAt = _time + 0.6 + Rand01() * 0.6; return; }
            PublishOffline(new ForcedMoveView { Kind = ForcedMoveKind.Confused, Points = new[] { me.X, me.Y, me.Z, x, y, z }, DurationMs = Math.Max(1, (int)(len / speed * 1000f)), Source = "offline confuse" });
        }

        // ------------------------------------------------------------------ helpers

        private void PublishOffline(ForcedMoveView m)
        {
            var old = _player.ForcedMove;
            if (old != null) _events.Enqueue(new GameEvent { Type = GameEventType.ForcedMoveEnded, SourceGuid = PlayerGuid, Amount = (int)old.Kind, SpellId = old.SpellId, Text = "replaced" });
            m.Id = ++_offForcedId;
            _player.ForcedMove = m;
            _player.ForcedMoveCounter++;
            _offForcedAt = _time;
            WowPos at = m.Kind == ForcedMoveKind.Knockback ? m.Start : new WowPos(m.Points[m.Points.Length - 3], m.Points[m.Points.Length - 2], m.Points[m.Points.Length - 1], 0f);
            _events.Enqueue(new GameEvent { Type = GameEventType.ForcedMoveStarted, SourceGuid = PlayerGuid, Amount = (int)m.Kind, SpellId = m.SpellId, Pos = at, Text = m.Source });
        }

        /// <summary>DEST_TARGET_BACK: behind the target by its reach + 1 yd (ours), facing it.</summary>
        private WowPos Behind(OfflineCreature c)
        {
            float o = c.Pos.O;
            float reach = (c.CombatReach > 0f ? c.CombatReach : 1.5f) + BehindExtra;
            float x = c.Pos.X - (float)Math.Cos(o) * reach, y = c.Pos.Y - (float)Math.Sin(o) * reach;
            return new WowPos(x, y, Ground(x, y, c.Pos.Z), ForcedMoveMath.Normalize(o));
        }

        private void TeleportPlayerOffline(WowPos dest, string what)
        {
            if (_player.ForcedMove != null) { var m = _player.ForcedMove; _player.ForcedMove = null; _events.Enqueue(new GameEvent { Type = GameEventType.ForcedMoveEnded, SourceGuid = PlayerGuid, Amount = (int)m.Kind, Text = "teleport" }); }
            _me.Pos = dest;
            _player.Pos = dest;
            _events.Enqueue(new GameEvent { Type = GameEventType.Teleport, Pos = dest, TargetGuid = PlayerGuid, Text = what });
            WowLog.Info("OfflineSession: " + what + " - teleport to " + dest);
        }

        private static string CName(OfflineCreature c, string none)
        {
            return c != null && c.State != null && !string.IsNullOrEmpty(c.State.Name) ? c.State.Name : none;
        }

        private static float Dist3(float ax, float ay, float az, float bx, float by, float bz)
        {
            float dx = ax - bx, dy = ay - by, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void LogMotionOnce()
        {
            if (_offMotionLogged) return;
            _offMotionLogged = true;
            WowLog.Info("OfflineSession: forced movement - ours: Blink " + BlinkDefaultYards + " yd without a radius, stops before a step over " + BlinkStepUp + " yd; " +
                        "behind a target = its reach + " + BehindExtra + " yd; a move the view never finishes ends 2 s after its duration (10 s for a knockback).");
        }
    }
}
