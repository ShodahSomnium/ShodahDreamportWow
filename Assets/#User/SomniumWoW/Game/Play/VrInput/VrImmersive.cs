using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// Immersive casting (WowSettings.ImmersiveCasting): the radial release starts IWowSession.CastSpellHeld; a glow in the
    /// spell's school colour sits in the casting hand and grows with the cast (WowSpellVfx also plays the spell's own precast
    /// kit on the hands while the cast runs); once the session holds the finished spell (Player.HeldSpellId, CastHeld) the glow
    /// pulses until that hand is PUSHED forward - its velocity (in the avatar root's frame, so walking and snap turns do not
    /// count) along the head's horizontal forward above PushSpeed for two frames - and ReleaseHeldSpell fires it at the
    /// selected target, always (no aim assist since 2026-09-27: it picked far entities in the push direction).
    /// Stage S1 (the user: "immersive casting should start the spell THEN i can release, make it loop if i do not release it ...
    /// Instant cast spells can just trigger normally"): the cast starts at once (online: sent at once, the realm's Dreamport hold
    /// keeps it at its end), at "ready" the casting hand vibrates (ReadyPulse), the glow then pulses in a loop until the push or
    /// the session drops it; an instant is cast at once with no glow and nothing to push.
    /// </summary>
    internal sealed class VrImmersive
    {
        /// <summary>
        /// m/s along the head's forward: the task's figure, NOT yet measured in a headset. Every push logs its speed and a hold
        /// that ends unfired logs the fastest forward speed seen, so the value can be tuned from real logs.
        /// </summary>
        public const float PushSpeed = 1.5f;
        public const float AimCone = 15f;
        /// <summary>Ours (S1): the vibration when the spell is ready in the hand - stronger and longer than a menu tick (0.5 / 0.04 s).</summary>
        public const float ReadyAmplitude = 0.85f, ReadySeconds = 0.18f;
        /// <summary>
        /// Ours (S1): seconds a started cast may show no cast bar before the glow gives up. Online the cast bar starts with the
        /// server's SMSG_SPELL_START (one round trip after the choice), offline at once.
        /// </summary>
        public const float StartGrace = 1.5f;

        private readonly VrGlow _glow = new VrGlow("WoW VR held spell", 4);
        private int _spell;
        private bool _right, _held, _harmful;
        private float _since, _heldAt;
        private Color _color;
        private string _name;
        private readonly Vector3[] _prev = new Vector3[2];
        private readonly bool[] _hasPrev = new bool[2];
        private readonly Vector3[] _vel = new Vector3[2];
        private int _fast;
        private float _peak;
        private int _fired;
        private int _instantNotes;

        public bool Active { get { return _spell != 0; } }
        public bool Right { get { return _right; } }

        public void Begin(IWowSession s, int spellId, bool right, AbilityView v)
        {
            if (s == null || spellId == 0) return;
            int before = _spell;
            s.CastSpellHeld(spellId, 0);
            if (v != null && v.CastTime <= 0f)
            {
                // S1: an instant (or a channel) is cast normally - the session's CastSpellHeld casts it at once; nothing to hold or push
                // (a spell already held stays held and tracked)
                if (_instantNotes++ < 5) WowLog.Info("VR input: " + v.Name + " is instant - cast at once (immersive casting holds only spells with a cast time; logged 5 times)");
                return;
            }
            PlayerState ps = s.Player;
            if (before != 0 && before != spellId && ps != null && (ps.HeldSpellId == before || ps.CastingSpellId == before))
            {
                // S1 (PLAN10 rule 6): the spell in the hand (casting or held) is a cast in progress - the session refused the new one
                // ("Another action is in progress"), so the glow stays with the first
                WowLog.Info("VR input: " + _name + " is still " + (_held ? "held" : "casting") + " in the " + (_right ? "right" : "left") + " hand - push it first (" + (v != null ? v.Name : "spell " + spellId) + " was not cast)");
                return;
            }
            _spell = spellId;
            _right = right;
            _held = false;
            _since = Time.unscaledTime;
            _harmful = v == null || v.Harmful;
            _name = v != null ? v.Name : "spell " + spellId;
            _color = VrArt.SpellColor(v != null ? v.IconPath : null, _harmful);
            _fast = 0;
            _peak = 0f;
        }

        public void Cancel()
        {
            _spell = 0;
            _held = false;
            _glow.Show(false);
        }

        /// <summary>
        /// Per frame in VR. hand = the casting hand's transform (both hands are tracked for velocity). Iteration 11 (vrcast,
        /// proposal-vrcast 1.2 #6): leftMenuOpen / rightMenuOpen - while the casting hand's radial menu is open its pushes are row
        /// changes, never the release of the held spell.
        /// </summary>
        public void Tick(IWowSession s, PlayerState p, Transform left, Transform right, Transform root, Vector3 eye, Vector3 headForward, float dt,
                         bool leftMenuOpen, bool rightMenuOpen)
        {
            Track(0, left, root, dt);
            Track(1, right, root, dt);
            if (_spell == 0 || s == null || p == null) { _glow.Show(false); return; }
            float now = Time.unscaledTime;
            // spellfx hook (iteration 11): a ground spell of this cast waits for its place (the green circle) - no cast yet, nothing in the hand.
            if (p.Targeting != null && p.Targeting.SpellId == _spell)
            {
                _since = now;
                _glow.Show(false);
                return;
            }
            if (p.HeldSpellId == _spell)
            {
                if (!_held)
                {
                    _held = true;
                    _heldAt = now;
                    GearXr.Pulse(_right, ReadyAmplitude, ReadySeconds);   // S1: "when it's finished (controller vibrate) I can push to release it"
                    WowLog.Info("VR input: " + _name + " is ready in the " + (_right ? "right" : "left") + " hand after " + (now - _since).ToString("0.0") +
                                " s (vibration " + ReadyAmplitude.ToString("0.00") + " for " + ReadySeconds.ToString("0.00") + " s) - it waits, glowing, until you push it forward");
                }
            }
            else if (_held || (p.CastingSpellId != _spell && now - _since > StartGrace))
            {
                if (!_held) WowLog.Info("VR input: immersive " + _name + " ended without being held (it went off, failed or was interrupted)");
                else WowLog.Info("VR input: held " + _name + " ended without a push (fastest forward hand speed " + _peak.ToString("0.00") + " m/s, threshold " + PushSpeed.ToString("0.0") + ")");
                Cancel();
                return;
            }

            Transform hand = _right ? right : left;
            if (hand == null) { _glow.Show(false); return; }
            float progress = p.CastTotal > 0.01f ? Mathf.Clamp01(p.CastElapsed / p.CastTotal) : 1f;
            // held: the loop (S1) - the glow breathes (size and brightness, about once a second) until the push
            float loop = Mathf.Sin((now - _heldAt) * 6f);
            float size = _held ? 0.12f + 0.022f * loop : 0.04f + 0.07f * progress;
            float alpha = _held ? 0.85f + 0.12f * loop : 0.55f + 0.3f * progress;
            _glow.Set(hand.position + hand.forward * 0.06f, eye, size, new Color(_color.r, _color.g, _color.b, alpha));

            if (!_held || now - _heldAt < 0.1f) return;
            if (_right ? rightMenuOpen : leftMenuOpen) { _fast = 0; return; }   // iteration 11: a row push is not a throw
            Vector3 v = _vel[_right ? 1 : 0];
            Vector3 f = new Vector3(headForward.x, 0f, headForward.z);
            if (f.sqrMagnitude < 1e-4f) return;
            f.Normalize();
            float along = Vector3.Dot(v, f);
            if (along > _peak) _peak = along;
            _fast = along > PushSpeed ? _fast + 1 : 0;
            if (_fast < 2) return;

            // The user (2026-09-27): "only go to the selected enemy, always" - the aim assist picked far entities in the push
            // direction ("too far away"). Released at the current target; none -> the target it was cast at (0).
            ulong sent = s.TargetGuid;
            _fired++;
            if (_fired <= 20 || _fired % 50 == 0)
            {
                EntityState e = sent != 0 ? s.Find(sent) : null;
                WowLog.Info("VR input: pushed the " + (_right ? "right" : "left") + " hand at " + along.ToString("0.0") + " m/s -> " + _name + " released at "
                    + (e != null ? e.Name : sent == 0 ? "the cast target" : "guid " + sent) + " (selected target)");
            }
            s.ReleaseHeldSpell(sent);
            GearXr.Pulse(_right, 0.8f, 0.08f);
            Cancel();
        }

        private void Track(int h, Transform hand, Transform root, float dt)
        {
            if (hand == null || dt <= 1e-5f) { _hasPrev[h] = false; return; }
            Quaternion inv = root != null ? Quaternion.Inverse(root.rotation) : Quaternion.identity;
            Vector3 local = inv * (hand.position - (root != null ? root.position : Vector3.zero));
            if (_hasPrev[h])
            {
                Vector3 vLocal = (local - _prev[h]) / dt;
                Vector3 vWorld = root != null ? root.rotation * vLocal : vLocal;
                _vel[h] = Vector3.Lerp(_vel[h], vWorld, 0.6f);   // light smoothing: tracking jitter, not the push
            }
            _prev[h] = local;
            _hasPrev[h] = true;
        }

        /// <summary>The attackable (harmful) or friendly (helpful) entity nearest the push direction within AimCone, else 0.</summary>
        public static ulong Aim(IWowSession s, Vector3 from, Vector3 dir, bool harmful)
        {
            if (s == null || s.Entities == null) return 0;
            float best = AimCone, range = WowSettings.VrTargetRange;
            ulong pick = 0;
            var list = s.Entities;
            for (int i = 0; i < list.Count; i++)
            {
                EntityState e = list[i];
                if (e == null || e.Dead || e.Kind == EntityKind.GameObject) continue;
                bool friendly = e.Reaction == Reaction.Friendly;
                if (harmful == friendly) continue;
                Transform t = WowEntityViews.GetView(e.Guid);
                if (t == null) continue;
                float height, footprint;
                if (!WowEntityViews.TryGetSize(e.Guid, out height, out footprint)) continue;
                Vector3 c = t.position + Vector3.up * Mathf.Max(0.5f, height * 0.5f);
                Vector3 d = c - from;
                float dist = d.magnitude;
                if (dist < 0.5f || dist > range) continue;
                // Measure to the body's edge, not its centre: a big creature is easier to hit.
                float ang = Vector3.Angle(dir, d) - Mathf.Atan2(Mathf.Max(0.3f, footprint * 0.5f), dist) * Mathf.Rad2Deg;
                if (ang < best) { best = ang; pick = e.Guid; }
            }
            return pick;
        }

        public void Destroy()
        {
            _glow.Destroy();
        }
    }
}
