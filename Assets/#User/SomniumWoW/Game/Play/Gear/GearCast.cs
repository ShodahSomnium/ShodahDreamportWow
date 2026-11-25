using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Online;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// Stage O2 (owner Sync, PLAN9 s0 decision 2) - another Somnium player's spell casting ON THEIR AVATAR (GearRemote owns one).
    /// - Anchors: the avatar's 12 spell-effect slots (head, chest, base, hands, breath, weapons, specials, world - SpellVisualDb.Slot)
    ///   as this client DRAWS the avatar (their frame + the origin offset + the stage-B ride correction), placed every LateUpdate after
    ///   GearRemote posed it, under "WoW Remote Spell Anchors" (a container child of the WoW Game object, so WowOrigin shifts it; never
    ///   moved in Rebased), inactive while the avatar is hidden. WowSpellVfx finds them through WowGear.TryAvatarSlot for this remote's
    ///   record-driven <see cref="Key"/> (<see cref="KeyOf"/>(slot)) and - online only - for their character guid (the server's events).
    /// - Online, a character our server shows (session.Find(their guid)): the server's CastStarted / CastSucceeded / ChannelStarted /
    ///   AuraApplied of that guid play through WowSpellVfx as for any unit - re-anchored to these slots. The record adds only what the
    ///   server does not know: the hand (for the missile and single-hand effects), the VR held-spell glow, the immersive glow growing in
    ///   the casting hand, and a cast the server has not started (online immersive casting holds locally until the push).
    /// - Otherwise (offline, another realm, beyond the server's visibility): the record drives the caster's look only - the precast or
    ///   channel kit looped while their cast runs, the cast kit when they release a spell, its impact only when it landed on the caster,
    ///   the glows. Never a missile or an impact on a monster: offline every player has their own monsters.
    /// </summary>
    public sealed class GearCast
    {
        /// <summary>Ours: record-driven keys carry 0x5357 ('SW') in the HighGuid bits, a value no 3.3.5a object guid uses (AzerothCore
        /// ObjectGuid.h enum HighGuid: Player 0x0000, Item 0x4000, GameObject 0xF110, Transport 0xF120, Unit 0xF130, Pet 0xF140,
        /// Vehicle 0xF150, DynamicObject 0xF100, Corpse 0xF101, Mo_Transport 0x1FC0, Instance 0x1F40, Group 0x1F50; the offline
        /// session uses 0xF130 too).</summary>
        public const ulong KeyBase = 0x5357000000000000UL;
        private const ulong KeyMask = 0xFFFF000000000000UL;

        // Ours: the glow in the casting hand - VrImmersive's look (Game/Play/VrInput/VrImmersive.cs Tick), so others see what the caster sees.
        public const float GlowCastSize = 0.04f, GlowCastGrow = 0.07f, GlowHeldSize = 0.12f, GlowHeldPulse = 0.015f, GlowHeldRate = 7f;
        public const float GlowCastAlpha = 0.55f, GlowCastAlphaGrow = 0.3f, GlowHeldAlpha = 0.95f, GlowAhead = 0.06f;
        /// <summary>Ours: how long a precast / channel loop drawn from a record may outlive the cast's own end (as WowSpellVfx's +1 s).</summary>
        public const int LoopSlackMs = 1000;
        /// <summary>Ours: a record cast the server does not show after this long (online) is drawn from the record (immersive local phase).</summary>
        public const float ServerGraceSeconds = 0.35f;

        public static ulong KeyOf(int slot) { return KeyBase | (ulong)(uint)(slot + 1); }
        public static bool IsKey(ulong guid) { return guid != 0 && (guid & KeyMask) == KeyBase; }
        public static int SlotOfKey(ulong key) { return IsKey(key) ? (int)(key & 0xFFFFFFFFUL) - 1 : -1; }

        private const string ContainerName = "WoW Remote Spell Anchors";
        private static Transform _container;

        private readonly Transform _parent;
        private Transform _root;
        private readonly Transform[] _slots = new Transform[12];
        private bool _placed;
        private VrGlow _glow;

        private WowNet.Remote _r;
        private string _label = "?";
        private ulong _key;
        private WowCastRecord _c;
        private float _timingAt;              // Time.unscaledTime the cast timing (int 29) last changed
        private int _tSpell, _tCounter, _tTotal;
        private float _tProgress;
        private WowCastState _tState;
        private bool _tHave;

        // The record-driven loop.
        private bool _loopOn, _loopYielded;
        private int _levelSeen;          // lead 2026-10-12: their record's level (0 = not read yet)
        private ulong _levelChar;        // the character it belongs to (another character = read again, no effect)
        private int _levelSaid;
        private ulong _heldKey;          // lead 2026-10-11: the loop key HoldCast kept going (their record key or their character's guid)
        private int _heldSpell, _heldCounter = -1, _heldSaid;
        private int _loopSpell, _loopCounter;
        private WowCastState _loopState;
        private float _castSeenAt;            // when the current record cast was first read (server grace)
        private int _seenSpell, _seenCounter;
        private WowCastState _seenState;
        private bool _seenHave;

        // Releases.
        private bool _relHave;
        private int _relSpell, _relCounter;

        // The glow colour per spell.
        private int _colourFor;
        private Color _colour = Color.white;

        // The log (rate-limited).
        private int _castsSaid, _relSaid, _glowSaid, _glowCounter = -1;
        private int _modeSaid;

        // ServerCopy's name check, cached (NameHash allocates).
        private string _nameFor;
        private int _nameHashFor;
        private bool _nameOk;

        public GearCast(Transform parent) { _parent = parent; }

        /// <summary>The key WowSpellVfx's record-driven loops and anchors use for this avatar (KeyOf(slot); 0 before the first Tick).</summary>
        public ulong Key { get { return _key; } }

        /// <summary>The record's cast hand for this spell (the current cast / held spell, else the last release), NotSaid otherwise.</summary>
        public WowCastHand HandFor(int spellId)
        {
            if (spellId <= 0) return WowCastHand.NotSaid;
            if (_c.Casting && _c.SpellId == spellId) return _c.Hand;
            if (_c.ReleaseSpellId == spellId) return _c.ReleaseHand;
            return WowCastHand.NotSaid;
        }

        /// <summary>A slot anchor while the avatar is drawn here (placed this or last LateUpdate), null otherwise.</summary>
        public Transform Slot(int slot)
        {
            if (slot < 0 || slot >= _slots.Length || !_placed || _root == null) return null;
            return _slots[slot];
        }

        // ------------------------------------------------------------------ Update: the record

        /// <summary>GearRemote.Tick (Update): read their cast record, start / stop the record-driven kits.</summary>
        public void Tick(WowNet.Remote r, string label)
        {
            _r = r;
            _label = label;
            WowGearState g = r.Gear;
            WowCastRecord c = g.Cast;
            _c = c;
            // Always the slot's own key (never the character guid: the offline player is guid 1, and so is the first character of a
            // fresh realm - a remote's record-driven effects must never land on the local player).
            ulong key = KeyOf(r.Slot);
            WowSpellVfx vfx = WowSpellVfx.Instance;
            if (key != _key)
            {
                if (_key != 0 && vfx != null) vfx.AvatarCastStop(_key);
                _key = key;
                _loopOn = false;
                _levelSeen = 0;
            }
            float now = Time.unscaledTime;
            if (!_tHave || c.SpellId != _tSpell || c.Counter != _tCounter || c.State != _tState || c.TotalMs != _tTotal || c.Progress != _tProgress)
            {
                _tHave = true;
                _tSpell = c.SpellId; _tCounter = c.Counter; _tState = c.State; _tTotal = c.TotalMs; _tProgress = c.Progress;
                _timingAt = now;
            }

            EntityState server = ServerCopy(g);
            bool serverSees = server != null;
            int mode = serverSees ? 1 : 2;
            if (mode != _modeSaid)
            {
                _modeSaid = mode;
                WowLog.Info("Gear: " + _label + " - " + (serverSees ? "the server shows their character (guid " + g.CharacterGuid + "): its spell effects are anchored to their avatar; their record adds the hand and the held-spell glow"
                    : "their spell effects are drawn from their record (the caster's look only: " + (g.CharacterGuid != 0 ? "their character is not in our session" : "offline") + ")"));
            }

            bool running = c.Casting && (c.State == WowCastState.Casting || c.State == WowCastState.Channeling) && !g.Dead;
            if (running && (!_seenHave || c.SpellId != _seenSpell || c.Counter != _seenCounter || c.State != _seenState))
            {
                _seenHave = true;
                _seenSpell = c.SpellId; _seenCounter = c.Counter; _seenState = c.State;
                _castSeenAt = now;
            }
            if (!running) _seenHave = false;

            // The record-driven precast / channel loop - only for a cast the server does not show (online: its CastStarted / ChannelStarted
            // of their guid play on the same avatar slots; a record loop running when the server starts showing that cast yields to it).
            if (running && vfx != null)
            {
                bool channel = c.State == WowCastState.Channeling;
                bool same = _loopOn && _loopSpell == c.SpellId && _loopCounter == c.Counter && _loopState == c.State;
                bool serverCasting = serverSees && (server.CastingSpellId == c.SpellId || vfx.HasLoop(channel ? 2 : 1, g.CharacterGuid));
                if (!same)
                {
                    if (_loopOn) { if (!_loopYielded) vfx.AvatarCastStop(_key); _loopOn = false; }
                    if (!serverCasting && !(serverSees && now - _castSeenAt < ServerGraceSeconds))
                    {
                        float left = c.TotalMs > 0 ? c.TotalMs * (1f - WowCastWriter.Predict(c, 0.0)) : 0f;
                        int maxMs = c.TotalMs > 0 ? Mathf.RoundToInt(left) + LoopSlackMs : 0;
                        vfx.AvatarCast(_key, c.SpellId, channel, maxMs);
                        _loopOn = true;
                        _loopYielded = false;
                        _loopSpell = c.SpellId; _loopCounter = c.Counter; _loopState = c.State;
                        Say(ref _castsSaid, "Gear: " + _label + " " + (channel ? "channels " : "casts ") + SpellLabel(c.SpellId) + " - drawn from their record" +
                            (serverSees ? " (the server has not shown this cast)" : "") + ", hand " + c.Hand + (c.TotalMs > 0 ? ", " + (c.TotalMs / 1000f).ToString("0.0") + " s" : "") +
                            (c.Immersive ? ", immersive" : ""));
                    }
                }
                else if (serverCasting && !_loopYielded)
                {
                    vfx.AvatarCastStop(_key);
                    _loopYielded = true;
                    WowLog.Info("Gear: " + _label + " - the server now shows " + SpellLabel(c.SpellId) + ": its effects take over from the record's");
                }
            }
            else if (_loopOn)
            {
                // Held after its own cast: the precast fades out by itself (as the caster's own WowSpellVfx: cast time + 1 s).
                bool heldSame = c.Casting && c.State == WowCastState.Held && c.SpellId == _loopSpell && c.Counter == _loopCounter && !g.Dead;
                if (!heldSame)
                {
                    if (!_loopYielded && vfx != null) vfx.AvatarCastStop(_key);
                    _loopOn = false;
                }
            }

            // Lead 2026-10-12 (the user: "can't see when a remote player levels up, please sync that"): their record's level (int 0 of the
            // character int, WowNetWire) went up since we last read it - WoW's level-up effect on their avatar (not on the first read)
            if (g.CharacterGuid != _levelChar) { _levelChar = g.CharacterGuid; _levelSeen = 0; }
            if (g.Level > 0)
            {
                if (_levelSeen > 0 && g.Level > _levelSeen && vfx != null && !g.Dead)
                {
                    vfx.AvatarLevelUp(_key);
                    Say(ref _levelSaid, "Gear: " + _label + " reached level " + g.Level + " - the level-up effect on their avatar");
                }
                _levelSeen = g.Level;
            }

            // Lead 2026-10-11 (the user: "loop the sound and effects if I have not released it, remember to sync over network too"): their
            // record says Held (immersive casting; WowCastState.Held is on the wire since iteration 4) - the precast on their avatar keeps
            // going, effects and sound: the record's own loop, or the server's (their character's guid) when the server showed the cast.
            bool heldNow = vfx != null && c.Casting && c.State == WowCastState.Held && !g.Dead;
            if (heldNow && (_heldKey == 0 || _heldSpell != c.SpellId || _heldCounter != c.Counter))
            {
                _heldKey = serverSees && (_loopYielded || !_loopOn) ? g.CharacterGuid : _key;
                _heldSpell = c.SpellId;
                _heldCounter = c.Counter;
                vfx.HoldCast(_heldKey, c.SpellId);
                Say(ref _heldSaid, "Gear: " + _label + " holds " + SpellLabel(c.SpellId) + " - its precast effects and sound loop on their avatar until they push it");
            }
            else if (!heldNow && _heldKey != 0)
            {
                // put away / cancelled (no cast in their record now): its loop ends; a release or a new cast ends / replaces it by itself
                if (vfx != null && !c.Casting && c.ReleaseCounter == _relCounter) vfx.AvatarCastStop(_heldKey);
                _heldKey = 0;
                _heldSpell = 0;
                _heldCounter = -1;
            }

            // Releases: the cast kit on the avatar (the server's SPELL_GO does it when it shows their character).
            if (c.ReleaseSpellId > 0 && (!_relHave || c.ReleaseCounter != _relCounter || c.ReleaseSpellId != _relSpell))
            {
                bool first = !_relHave;
                _relHave = true;
                _relSpell = c.ReleaseSpellId;
                _relCounter = c.ReleaseCounter;
                if (!first && !serverSees && !g.Dead && vfx != null)
                {
                    // (the loop state is kept: a record still saying "casting" in this read must not restart the precast)
                    vfx.AvatarRelease(_key, c.ReleaseSpellId, c.ReleaseAtSelf);
                    Say(ref _relSaid, "Gear: " + _label + " released " + SpellLabel(c.ReleaseSpellId) + " (hand " + c.ReleaseHand + (c.ReleaseAtSelf ? ", on themselves" : "") + ") - its cast effect on their avatar");
                }
            }
        }

        /// <summary>The server's copy of their character in our online session (same realm, in our visibility), else null.</summary>
        private EntityState ServerCopy(in WowGearState g)
        {
            if (g.CharacterGuid == 0) return null;
            IWowSession s = WowPlay.Session;
            if (!(s is OnlineSession)) return null;
            EntityState e;
            try { e = s.Find(g.CharacterGuid); }
            catch (Exception) { return null; }
            if (e == null) return null;
            if (g.CharacterNameHash == 0 || string.IsNullOrEmpty(e.Name)) return e;
            if (!ReferenceEquals(e.Name, _nameFor) || g.CharacterNameHash != _nameHashFor)
            {
                _nameFor = e.Name;
                _nameHashFor = g.CharacterNameHash;
                _nameOk = g.CharacterNameHash == WowNetWire.NameHash(e.Name);
            }
            return _nameOk ? e : null;
        }

        // ------------------------------------------------------------------ LateUpdate: anchors and glow

        /// <summary>GearRemote.Place, the avatar drawn: every slot anchor at the posed body + draw, then the glow in the casting hand.</summary>
        public void Place(GearBody b, Vector3 draw, Vector3 eye, bool dead)
        {
            // Placed every frame the avatar is drawn (12 empty transforms): an effect may arrive any time (a mob's spell on them online).
            // The same slot rules as the local player's anchors (WowSpellVfx.PlaceAnchors), at the avatar as drawn here.
            EnsureAnchors();
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            // vfxanchor fix 2026-10-04: the slot rule lives in GearSpellSlots (root-yaw head / feet / above-head points, the crown, the
            // chest's front), shared with the local player's anchors.
            GearSpellSlots.Place(b, draw, _slots);
            _placed = true;
            PlaceGlow(b, draw, eye, dead);
        }

        /// <summary>The avatar is not drawn here: the anchors (and any effect on them) hide, the glow too.</summary>
        public void Hide()
        {
            _placed = false;
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            if (_glow != null) _glow.Show(false);
        }

        private void PlaceGlow(GearBody b, Vector3 draw, Vector3 eye, bool dead)
        {
            WowCastRecord c = _c;
            bool held = c.State == WowCastState.Held;
            bool want = !dead && c.Casting && (held || (c.State == WowCastState.Casting && c.Immersive)) && (c.Hand == WowCastHand.Right || c.Hand == WowCastHand.Left)
                        && WowSettings.SpellEffects;
            if (!want)
            {
                if (_glow != null) _glow.Show(false);
                return;
            }
            bool right = c.Hand == WowCastHand.Right;
            Vector3 hp; Quaternion hr;
            if (!b.HandPose(right, out hp, out hr)) { if (_glow != null) _glow.Show(false); return; }
            if (_glow == null)
            {
                _glow = new VrGlow("WoW remote held spell", 4);
                Transform c0 = Container();
                if (c0 != null) _glow.Go.transform.SetParent(c0, true);
            }
            if (_colourFor != c.SpellId) { _colourFor = c.SpellId; _colour = SpellColour(c.SpellId); }
            float now = Time.unscaledTime;
            float progress = WowCastWriter.Predict(c, now - _timingAt);
            float size = held ? GlowHeldSize + GlowHeldPulse * Mathf.Sin(now * GlowHeldRate) : GlowCastSize + GlowCastGrow * progress;
            float alpha = held ? GlowHeldAlpha : GlowCastAlpha + GlowCastAlphaGrow * progress;
            Vector3 at = hp + draw + (hr * Vector3.forward) * GlowAhead;
            _glow.Set(at, eye, size * b.Scale, new Color(_colour.r, _colour.g, _colour.b, alpha));
            int glowKey = c.Counter * 4 + (held ? 1 : 0);
            if (glowKey != _glowCounter)
            {
                _glowCounter = glowKey;
                Say(ref _glowSaid, "Gear: " + _label + (held ? " holds " : " gathers (immersive) ") + SpellLabel(c.SpellId) + " in the " + (right ? "right" : "left") + " hand - the " + (held ? "held-spell glow, until they push it forward" : "glow grows with the cast"));
            }
        }

        private static Color SpellColour(int spellId)
        {
            WowFiles files = WowPlay.Files;
            SpellInfo sp = null;
            try { sp = files != null ? SpellDb.Get(files).Find(spellId) : null; }
            catch (Exception) { sp = null; }
            return VrArt.SpellColor(sp != null ? sp.IconPath : null, sp == null || sp.IsHarmful);
        }

        private static string SpellLabel(int spellId)
        {
            WowFiles files = WowPlay.Files;
            SpellInfo sp = null;
            try { sp = files != null ? SpellDb.Get(files).Find(spellId) : null; }
            catch (Exception) { sp = null; }
            return (sp != null && !string.IsNullOrEmpty(sp.Name) ? sp.Name + " " : "spell ") + "(" + spellId + ")";
        }


        /// <summary>Iteration 11 (avatar): after Place, while they are drawn as a creature (a form, a polymorph...), every slot the
        /// creature model resolves (its attachments 17-22 / 34, SlotFallback, else its height) replaces the hidden avatar's point.</summary>
        public void OverrideFrom(SomniumSpace.Worlds.SomniumWoW.Game.Forms.FormModel m)
        {
            if (m == null || !_placed) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                Vector3 p; Quaternion r;
                if (_slots[i] != null && m.TrySlot(i, out p, out r)) _slots[i].SetPositionAndRotation(p, r);
            }
        }

        private void EnsureAnchors()
        {
            if (_root != null) return;
            Transform c = Container();
            var go = new GameObject("Spell anchors of player " + (_r != null ? _r.PlayerId : -1));
            _root = go.transform;
            if (c != null) _root.SetParent(c, false);
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new GameObject(((SpellVisualDb.Slot)i).ToString()).transform;
                _slots[i].SetParent(_root, false);
            }
        }

        /// <summary>"WoW Remote Spell Anchors" under the WoW Game object (at (0,0,0): WowOrigin shifts its children).</summary>
        private Transform Container()
        {
            if (_container != null) return _container;
            if (_parent == null) return null;
            Transform found = _parent.Find(ContainerName);
            if (found == null)
            {
                var go = new GameObject(ContainerName);
                found = go.transform;
                found.SetParent(_parent, false);
                found.localPosition = Vector3.zero;
                found.localRotation = Quaternion.identity;
                found.localScale = Vector3.one;
            }
            _container = found;
            return _container;
        }

        private static void Say(ref int n, string line)
        {
            n++;
            if (n <= 10 || n % 50 == 0) WowLog.Info(line + (n == 10 ? " (further lines every 50th)" : ""));
        }

        public void Destroy()
        {
            WowSpellVfx vfx = WowSpellVfx.Instance;
            if (vfx != null && _key != 0) vfx.AvatarCastStop(_key);
            _loopOn = false;
            _placed = false;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            for (int i = 0; i < _slots.Length; i++) _slots[i] = null;
            if (_glow != null) _glow.Destroy();
            _glow = null;
        }
    }
}
