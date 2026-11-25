using System;
using SomniumSpace.Bridge;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Forms;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Networking;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Gear
{
    /// <summary>
    /// The local player's weapons (Session.Player.Equipment MainHand / OffHand / Ranged) on their Somnium avatar.
    ///
    /// VR - grab and put back (sticky, press-to-toggle): squeeze the grip with a hand at a sheathed weapon it may take
    /// (lead 2026-10-10: either hand takes any weapon - a back weapon from anywhere behind the back, a hip weapon within HipReach;
    /// a shield stays the left hand's; GrabFor) and the weapon jumps into that
    /// hand at WoW's grip and STAYS there when the grip is let go (fights are long; holding the grip for a minute tires the
    /// hand, and the client's own grip does nothing here). To put it back, bring that hand to the weapon's sheath and
    /// squeeze again. Drawing calls Session.SetSheath(Melee) (a bow alone: Ranged); both hands empty again -> Sheathed.
    /// The session sheathing by itself (death, the desktop key) empties the hands too.
    /// Swing -> auto-attack: each frame the held blade (a capsule along the model's long axis) is tested against entity
    /// colliders (Physics.OverlapCapsule on layer 16 + WowEntityViews.TryGetGuid); a hit whose tip moves faster than
    /// SwingSpeed relative to the body, on a living hostile/neutral creature, calls Session.StartAttack(guid) (at most every
    /// 0.4 s, and not again while already auto-attacking that target) and pulses that hand. The damage runs on the
    /// simulation's swing timer; a light tick follows each MeleeHit the player lands.
    ///
    /// Desktop: Z (WoW's own sheathe key; free in the Somnium client) toggles Sheathed / Melee through the session, and the
    /// weapons follow Session.Player.Sheath: drawn = in the avatar's hands (the humanoid avatar's hand bones - the Bridge has
    /// no desktop hands). Iteration 3: the avatar itself swings, stabs, punches, shoots and casts (GearAnimLocal picks the
    /// motion, GearPoser poses the humanoid bones after the client animated them; WowSettings.AvatarCombatAnimations); with
    /// the setting off or no humanoid avatar the held weapon only chops on each swing (Player.AttackCounter), as before.
    /// VR: the local avatar is never posed (the player's own hands move it).
    /// </summary>
    public sealed partial class GearLocal
    {
        /// <summary>Blade tip speed (m/s, relative to the avatar) a hit needs to start the attack: WowSettings.SwingSpeed (WowGear copies it on Changed).</summary>
        public static float SwingSpeed = WowSettings.SwingSpeed;

        /// <summary>WowSettings.AvatarCombatAnimations (WowGear copies it on WowSettings.Changed): desktop avatar motions on/off.</summary>
        public static bool AnimationsOn = WowSettings.AvatarCombatAnimations;

        /// <summary>How near (m, x body scale) a hand must be to a sheathed weapon's handle to take it or put it back.</summary>
        public static float Reach = 0.2f;

        /// <summary>Lead 2026-10-10 (the user: "for grabbing weapons, can you make it a bit easier ... a big 2 hander on my back, just let me
        /// reach over behind my head anywhere to grab it. Same with dual wielding or 1 handers, or wands etc, make it so I just need to be
        /// near it, I can pick up with any hand"): a weapon on the hips within HipReach (m x body scale; the Skyrim world's GrabRadius
        /// 0.28) of its handle; a weapon on the back from anywhere behind the head's plane (BackBehind) and above the chest less BackBelow
        /// (the Skyrim world's quiver test is behind the head's plane and above the hips); either hand takes any weapon, a shield stays
        /// the left hand's. Values ours.</summary>
        public const float HipReach = 0.28f, BackBehind = 0.02f, BackBelow = 0.15f;

        private const int EntityLayerMask = 1 << 16;
        private static readonly int[] Slots = { 15, 16, 17 };

        public readonly GearBody Body = new GearBody { Label = "the local player" };
        public readonly GearPoser Poser = new GearPoser { Label = "the local player" };
        public readonly GearAnimLocal Anim = new GearAnimLocal();
        /// <summary>States fix 2026-10-04: lying dead / sitting / eating / kneeling / sleeping as the Somnium client's own pose (GearStatePose).</summary>
        public readonly GearStatePose StatePose = new GearStatePose("local player");

        /// <summary>Iteration 11 (avatar): the creature / ghost / stealth look on our own avatar (Game/Play/Forms).</summary>
        public readonly FormView Form = new FormView("the local player", true);
        /// <summary>Iteration 11: the look WoW gives us now (WowSettings.ShowForms off = Own, except a vehicle's Hidden).</summary>
        public AvatarLook Look { get; private set; } = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f };
        /// <summary>Iteration 11: the WoW Game object (WowGear sets it): the form / armour containers live under it, nothing of it is hidden.</summary>
        public Transform GameRoot;
        // iteration 11: our avatar's locomotion for the form model's gait and the record's flags (int 31 bits 28-30)
        private Vector3 _fLast, _fVel;
        private bool _fHave, _airborne, _wasAirborne;
        private float _airSince = -1f;
        private int _formShotCounter = -1;
        private readonly int[] _enchantShown = { -1, -1, -1 };
        private readonly int[] _enchantWant = new int[3];
        private WowFiles _files;
        private readonly GearItem[] _items = new GearItem[3];
        private readonly WowWireItem[] _wanted = new WowWireItem[3];
        private readonly Collider[] _hits = new Collider[16];
        private int _right, _left;              // held slot (15/16/17) per hand, 0 = empty
        private bool _xr, _haveMode;
        private SheathState _lastSheath;
        private int _lastAttackCounter = -1;
        private float _chopT = -1f;
        private int _attackCounter;
        private bool _hidden;
        private int _logHits;

        private struct Swing { public Vector3 PrevTip; public bool Have; public bool Armed; public float Peak; }
        private Swing _swingR, _swingL;
        private float _lastStartAttack = -10f;

        public bool Xr { get { return _xr; } }

        private GearItem ItemOf(int slot) { return slot >= 15 && slot <= 17 ? _items[slot - 15] : null; }

        public void Clear()
        {
            for (int i = 0; i < _items.Length; i++) { if (_items[i] != null) _items[i].Destroy(); _items[i] = null; }
            for (int i = 0; i < _wanted.Length; i++) _wanted[i] = default(WowWireItem);
            _right = _left = 0;
            _lastAttackCounter = -1;
            ClearBow();   // lead 2026-10-10
            Poser.Release();
            Anim.Clear();
            StatePose.Release();   // states fix 2026-10-04: a pose we set is left when the game ends
            // iteration 11: the avatar's own look back at once; the form / puppet / armour of this game gone
            Form.Clear();
            Look = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f };
            _formShotCounter = -1;
            _fHave = false;
            _airborne = _wasAirborne = false;
            for (int i = 0; i < 3; i++) { _enchantShown[i] = -1; _enchantWant[i] = 0; }
            ClearExtras();
            // Stage O2: no cast and no release is said once the game ends (a new game starts with a clean record).
            _castState = WowCastState.None;
            _castSpell = 0;
            _handPending = false;
            _relSpell = 0;
            _castWriter.Reset();
            ClearSheathe();   // iteration 11 (combat feel)
        }

        // ------------------------------------------------------------------------------------------------ Update

        public void Tick(IWowSession s, WowFiles files)
        {
            if (s == null || s.Player == null) { Clear(); return; }
            PlayerState p = s.Player;
            try { Body.Player = SomniumBridge.PlayersContainer?.LocalPlayer; }
            catch (Exception) { Body.Player = null; }
            Body.Update();

            _files = files;
            SyncItems(p, files);

            bool xr = GearXr.Present && Body.XrRight != null && Body.XrLeft != null;
            if (xr != _xr || !_haveMode)
            {
                _xr = xr;
                _haveMode = true;
                _right = _left = 0;
                WowLog.Info("Gear: local player in " + (xr ? "VR - grab a weapon from its sheath with the grip; squeeze again at the sheath to put it back" : "desktop mode - the Z key draws/sheathes"));
            }

            // Iteration 11 (avatar): the look (forms, transforms, ghost, stealth, a vehicle's hidden passenger); creature looks hide the
            // weapons (WoW does). The kill switch "Show forms on my avatar" off = the old rule (cat and bear hide the weapons).
            AvatarLook look = AvatarLooks.Decide(p, null);
            if (!WowSettings.ShowForms && look.Kind != AvatarLookKind.Hidden) look = new AvatarLook { Kind = AvatarLookKind.Own, Scale = 1f, Form = p.ShapeshiftForm };
            if (!look.Same(Look) && look.Kind != AvatarLookKind.Own && files != null)
            {
                SpellDb db = null;
                try { db = SpellDb.Get(files); } catch (Exception) { db = null; }
                look.SpellId = AvatarLooks.LookSpell(p, db);
            }
            else if (look.Same(Look)) look.SpellId = Look.SpellId;
            Look = look;
            _hidden = WowSettings.ShowForms ? look.HidesWeapons : (p.ShapeshiftForm == 1 || p.ShapeshiftForm == 5 || look.Kind == AvatarLookKind.Hidden);

            // The session sheathed by itself (death, a key, SetSheath from elsewhere): the hands let go.
            if (p.Sheath != _lastSheath)
            {
                if (p.Sheath == SheathState.Sheathed && (_right != 0 || _left != 0))
                {
                    WowLog.Info("Gear: the session sheathed the weapons; hands emptied");
                    _right = _left = 0;
                }
                _lastSheath = p.Sheath;
            }
            if (p.Dead) _right = _left = 0;
            // lead 2026-10-12: a weapon hidden in Settings leaves the hand
            if (_right != 0 && !SlotShown(_right)) { _right = 0; if (_xr) ApplySheath(s, p); }
            if (_left != 0 && !SlotShown(_left)) { _left = 0; if (_xr) ApplySheath(s, p); }

            StepSheathe(s, p);   // iteration 11 (combat feel): a ranged cast / the mount put the weapons away (GearLocal.Sheathe.cs)
            if (_xr) StepVr(s, p);
            else StepDesktop(s, p);
            StepBow(s, p);   // lead 2026-10-10: immersive bow (GearLocal.Bow.cs)

            if (p.AttackCounter != _lastAttackCounter)
            {
                if (_lastAttackCounter >= 0 && !_xr && p.Sheath == SheathState.Melee) _chopT = 0f;
                if (_lastAttackCounter >= 0) _attackCounter++;
                _lastAttackCounter = p.AttackCounter;
            }
            if (_chopT >= 0f) { _chopT += Time.deltaTime; if (_chopT > 0.34f) _chopT = -1f; }

            bool creature = Look.Kind == AvatarLookKind.Creature;
            bool desktopAnim = AnimationsOn && !_xr;
            Anim.WeaponsAway = !_xr && (_putAway || p.Mounted);   // iteration 11 (combat feel): no weapon stance with the weapons away
            // states fix 2026-10-04: the body state + its client pose first (a ride / taxi / vehicle / forced move owns the avatar: no pose of ours)
            bool owned = WowMount.OwnsAvatar || WowFlight.Flying || WowVehicle.OwnsAvatar || WowForcedMove.OwnsAvatar || p.OnTaxi;
            StatePose.Tick(p, files, owned, Body.ModelFeet, _xr);
            Anim.Body = StatePose.State;
            StepEating(s, p);   // lead 2026-10-04: Immersive eating (VR; GearLocal.Eating.cs)
            Anim.Tick(s, files, Poser, desktopAnim, Time.time, desktopAnim || creature, creature, new Vector2(_fVel.x, _fVel.z).magnitude);
            StepCast(p);
            StepEnchants(p, files);
        }

        /// <summary>Iteration 11 (spellfx hook 7): the ItemVisuals glow of each held weapon's enchant (main, off, ranged) - into our
        /// record (int 44) and onto our own weapon models (GearItem.SetEnchantVisual, again after a rebuild).</summary>
        private void StepEnchants(PlayerState p, WowFiles files)
        {
            ItemVisualDb db = ItemVisualDb.Get(files);
            for (int i = 0; i < 3; i++)
            {
                int slot = Slots[i];
                ItemView v = p.Equipment != null && p.Equipment.Length > slot ? p.Equipment[slot] : null;
                int packed = p.EquipEnchants != null && p.EquipEnchants.Length > slot ? p.EquipEnchants[slot] : 0;
                int want = v == null || v.DisplayId <= 0 ? 0 : db.VisualFor(packed, v.DisplayId);
                _enchantWant[i] = want < 0 || want > 0xFF ? 0 : want;
                GearItem it = _items[i];
                if (it == null) { _enchantShown[i] = -1; continue; }
                if (_enchantShown[i] == _enchantWant[i]) continue;
                _enchantShown[i] = _enchantWant[i];
                it.SetEnchantVisual(_enchantWant[i]);
            }
        }

        private void SyncItems(PlayerState p, WowFiles files)
        {
            for (int i = 0; i < 3; i++)
            {
                ItemView v = p.Equipment != null && p.Equipment.Length > Slots[i] ? p.Equipment[Slots[i]] : null;
                var w = v == null || v.DisplayId <= 0 ? default(WowWireItem) : new WowWireItem { DisplayId = v.DisplayId, SheatheType = v.SheatheType, InventoryType = v.InventoryType };
                GearItem cur = _items[i];
                if (cur != null && cur.Item.SameAs(w)) { cur.Ready(); continue; }
                if (cur == null && _wanted[i].SameAs(w)) continue; // nothing to build (empty, or no game files)
                _wanted[i] = w;
                if (cur != null)
                {
                    cur.Destroy();
                    if (_right == Slots[i]) _right = 0;
                    if (_left == Slots[i]) _left = 0;
                }
                _items[i] = w.Empty || files == null ? null : new GearItem(files, w, Slots[i], "local player");
                _enchantShown[i] = -1;   // iteration 11: the new model gets its enchant glow again (StepEnchants)
                if (!w.Empty) WowLog.Info("Gear: local " + GearPlace.SlotName(Slots[i]) + " is now display " + w.DisplayId + " (" + (v.Name ?? "?") + ")");
            }
        }

        private bool Usable(GearItem it) { return it != null && it.Go != null && it.Spot != GearSpot.Hidden && SlotShown(it.Slot); }

        /// <summary>Lead 2026-10-12 (the user: "add the ability to hide weapons, like you can armour (I was wearing an off-hand, and it was
        /// constantly in my vision)"): Settings > Avatar "Show main hand / off hand / ranged weapon" - a hidden one is not drawn on our avatar,
        /// cannot be drawn into a hand, and our record sends it as empty, so the others do not see it either.</summary>
        private static bool SlotShown(int slot)
        {
            return slot == 15 ? WowSettings.ShowMainHand : slot == 16 ? WowSettings.ShowOffHand : slot == 17 ? WowSettings.ShowRanged : true;
        }

        /// <summary>The slots a hand may take: lead 2026-10-10 - any weapon with either hand (no more cross-drawing), a shield with the left.</summary>
        private bool MayTake(bool right, GearItem it)
        {
            if (!Usable(it)) return false;
            if (it.IsShield) return !right;
            return it.Slot >= 15 && it.Slot <= 17;
        }

        private static bool OnBack(GearSpot s) { return s == GearSpot.BackTwoHand || s == GearSpot.BackStaff || s == GearSpot.BackRanged || s == GearSpot.BackShield; }

        /// <summary>Lead 2026-10-10: a hand behind the head's plane and no lower than BackBelow under the chest - over the shoulder,
        /// behind the head or the upper back.</summary>
        private bool BehindBack(Vector3 hp)
        {
            Vector3 f = Body.Forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) return false;
            float s = Body.Scale;
            return Vector3.Dot(hp - Body.HeadPos, f.normalized) < BackBehind * s && Vector3.Dot(hp - Body.Chest, Body.Up) > -BackBelow * s;
        }

        /// <summary>
        /// Lead 2026-10-10: what a grip press of this hand at hp does - put the held weapon back (-1: a back weapon anywhere behind the
        /// back or at its handle, a hip weapon within HipReach x 1.1), take a weapon (its slot: the nearest back weapon when behind the
        /// back, else the nearest within reach of its handle - back weapons Reach, hip weapons HipReach), or nothing (0). Behind the back
        /// with the bow in the other hand is the quiver (GearLocal.Bow), never a weapon. StepVr acts on it, NearSheath reports it.
        /// </summary>
        private int GrabFor(bool right, Vector3 hp, out float dist)
        {
            dist = 0f;
            float reach = Reach * Body.Scale, hip = HipReach * Body.Scale;
            bool back = BehindBack(hp);
            int held = right ? _right : _left;
            if (held != 0)
            {
                GearItem h = ItemOf(held);
                if (h == null || h.Go == null) return -1;
                dist = SheathDistance(h, hp);
                return (OnBack(h.Spot) ? back || dist <= reach * 1.25f : dist <= hip * 1.1f) ? -1 : 0;
            }
            if (back && ArrowHand(right)) return 0;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                GearItem it = _items[i];
                if (!MayTake(right, it) || _right == it.Slot || _left == it.Slot) continue;
                float d = SheathDistance(it, hp);
                bool ok = OnBack(it.Spot) ? back || d < reach : d < hip;
                if (ok && d < bestD) { bestD = d; best = it.Slot; }
            }
            dist = best != 0 ? bestD : 0f;
            return best;
        }

        private void StepVr(IWowSession s, PlayerState p)
        {
            if (p.Dead || _hidden || p.Mounted) return;   // iteration 11 (combat feel): no weapon is drawn while mounted
            bool changed = false;
            for (int h = 0; h < 2; h++)
            {
                bool right = h == 0;
                if (!GearXr.GripPressed(right)) continue;
                Vector3 hp; Quaternion hr;
                if (!Body.HandPose(right, out hp, out hr)) continue;
                int held = right ? _right : _left;
                float d;
                int grab = GrabFor(right, hp, out d);   // lead 2026-10-10: easier grabs (behind the back / near the hips, either hand)
                if (grab < 0)
                {
                    SetHand(right, 0);
                    changed = true;
                    GearXr.Pulse(right, 0.35f, 0.05f);
                    WowLog.Info("Gear: VR " + (right ? "right" : "left") + " hand put the " + GearPlace.SlotName(held) + " back (" + d.ToString("0.00") + " m from its sheath" + (BehindBack(hp) ? ", behind the back" : "") + ")");
                }
                else if (grab > 0)
                {
                    SetHand(right, grab);
                    changed = true;
                    GearXr.Pulse(right, 0.5f, 0.06f);
                    WowLog.Info("Gear: VR " + (right ? "right" : "left") + " hand drew the " + GearPlace.SlotName(grab) + " (" + d.ToString("0.00") + " m from the handle" + (BehindBack(hp) ? ", behind the back" : "") + ")");
                }
            }
            if (changed) ApplySheath(s, p);
        }

        /// <summary>
        /// For WowGear.HandNearSheath (the VR radial menu opens only when false): VR, alive, weapons shown, and the hand within
        /// the grab reach of a sheathed weapon it may take, or holding a weapon within the put-back reach of its sheath -
        /// the same distances StepVr acts on, so a grip there always belongs to the weapon.
        /// </summary>
        public bool NearSheath(bool right)
        {
            if (!_xr || _hidden) return false;
            IWowSession s = WowPlay.Session;
            if (s == null || s.Player == null || s.Player.Dead || s.Player.Mounted) return false;   // iteration 11: mounted = no grabs (StepVr)
            Vector3 hp; Quaternion hr;
            if (!Body.Valid || !Body.HandPose(right, out hp, out hr)) return false;
            float d;
            if (GrabFor(right, hp, out d) != 0) return true;   // lead 2026-10-10: the same rule StepVr acts on
            return BowBusy(right, hp, true);                   // the bow hand, the arrow hand, the quiver (GearLocal.Bow)
        }

        private float SheathDistance(GearItem it, Vector3 hand)
        {
            Vector3 sp, a, b; Quaternion sr;
            GearPlace.Sheathed(Body, it, out sp, out sr);
            GearPlace.GrabSegment(it, sp, sr, out a, out b);
            return GearPlace.DistanceToSegment(hand, a, b);
        }

        /// <summary>WowSpace.Rebased (our origin moved by delta, the avatar by -delta): the swing trackers follow, so the jump is no swing.</summary>
        public void OnRebased(Vector3 delta)
        {
            _swingR.PrevTip -= delta;
            _swingL.PrevTip -= delta;
        }

        private void SetHand(bool right, int slot)
        {
            if (right) { _right = slot; _swingR = default(Swing); }
            else { _left = slot; _swingL = default(Swing); }
        }

        private void ApplySheath(IWowSession s, PlayerState p)
        {
            bool melee = (_right == 15 || _right == 16) || (_left == 15 || _left == 16) || ((_right == 17 || _left == 17) && !IsBowSlot(17));   // lead 2026-10-10: either hand
            SheathState want = melee ? SheathState.Melee : (_right == 17 || _left == 17) ? SheathState.Ranged : SheathState.Sheathed;
            if (want == p.Sheath) return;
            try { s.SetSheath(want); }
            catch (Exception e) { WowLog.Warn("Gear: SetSheath(" + want + ") threw: " + e.Message); }
            _lastSheath = p.Sheath;
            WowLog.Info("Gear: SetSheath(" + want + ") -> session says " + p.Sheath);
        }

        private bool IsBowSlot(int slot) { GearItem it = ItemOf(slot); return it != null && it.IsBow; }

        private void StepDesktop(IWowSession s, PlayerState p)
        {
            if (Input.GetKeyDown(KeyCode.Z) && !TypingInField() && !SheatheKey(p))   // iteration 11: mounted / put away -> GearLocal.Sheathe.cs
            {
                bool anyMelee = Usable(_items[0]) || Usable(_items[1]);
                SheathState want = p.Sheath != SheathState.Sheathed ? SheathState.Sheathed : (anyMelee ? SheathState.Melee : (Usable(_items[2]) ? SheathState.Ranged : SheathState.Melee));
                try { s.SetSheath(want); }
                catch (Exception e) { WowLog.Warn("Gear: SetSheath threw: " + e.Message); }
                WowLog.Info("Gear: the Z key -> SetSheath(" + want + "), session says " + p.Sheath);
                _lastSheath = p.Sheath;
            }
            // The hands follow the session's sheath state.
            _right = _left = 0;
            if (p.Dead || _putAway || p.Mounted) return;   // iteration 11 (combat feel): put away by a ranged cast / the mount
            if (p.Sheath == SheathState.Melee)
            {
                if (Usable(_items[0])) _right = 15;
                if (Usable(_items[1])) _left = 16;
            }
            else if (p.Sheath == SheathState.Ranged && Usable(_items[2]))
            {
                if (_items[2].IsBow) _left = 17; else _right = 17;
            }
        }

        private static bool TypingInField()
        {
            EventSystem es = EventSystem.current;
            GameObject sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return false;
            var tmp = sel.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var ui = sel.GetComponent<UnityEngine.UI.InputField>();
            return ui != null && ui.isFocused;
        }

        // ------------------------------------------------------------------------------------------------ LateUpdate

        /// <summary>Places every weapon (after the avatar's IK), then tests the held blades (VR).</summary>
        public void Place(IWowSession s)
        {
            if (s == null || !Body.Valid)
            {
                for (int i = 0; i < 3; i++) if (_items[i] != null && _items[i].Go != null && _items[i].Go.activeSelf) _items[i].Go.SetActive(false);
                PlaceBow(s, true);   // lead 2026-10-10: the string / arrow / column hide with the bow
                Poser.Release();
                Form.Hide();
                HideExtras();
                return;
            }
            Poser.Begin();
            Body.Update();
            // Desktop: the avatar's combat motion first (after the client animated it), then the weapons on the posed body.
            bool dead = s.Player == null || s.Player.Dead;
            StepLocomotion();
            GearWowPose wp = WowBody(s.Player, dead);
            if (Poser.Apply(Body, AnimationsOn && (!_xr || wp.LegsOnly) && !dead, Time.time, Time.deltaTime, wp)) Body.Update();   // lead 2026-10-26: VR swim legs
            for (int i = 0; i < 3; i++)
            {
                GearItem it = _items[i];
                if (it == null || it.Go == null) continue;
                bool inRight = _right == it.Slot, inLeft = _left == it.Slot;
                bool show = !_hidden && SlotShown(it.Slot) && (inRight || inLeft || it.Spot != GearSpot.Hidden);   // lead 2026-10-12: Settings > Avatar
                if (it.Go.activeSelf != show) it.Go.SetActive(show);
                if (!show) continue;
                Vector3 pos; Quaternion rot;
                if (inRight || inLeft)
                {
                    Vector3 hp; Quaternion hr;
                    Body.HandPose(inRight, out hp, out hr);
                    if (!_xr && _chopT >= 0f && !Poser.Posed) hr = hr * Quaternion.AngleAxis(Chop(_chopT / 0.34f), Vector3.right);
                    GearPlace.Held(it, hp, hr, out pos, out rot);
                }
                else GearPlace.Sheathed(Body, it, out pos, out rot);
                it.T.SetPositionAndRotation(pos, rot);
            }
            if (_xr) { TestBlade(s, true, ref _swingR); TestBlade(s, false, ref _swingL); }
            PlaceBow(s, true);   // lead 2026-10-10: the bow's string, the arrow, the column (GearLocal.Bow.cs)
            PlaceForm(s.Player);
            PlaceExtras(s.Player);
        }

        // ------------------------------------------------------------------ iteration 11 (avatar): the form on our avatar

        /// <summary>Our avatar's smoothed velocity (exp 8/s, as GearRemote's fallback) and the airborne rule (|vy| > 1.5 m/s for > 0.15 s; ours).</summary>
        private void StepLocomotion()
        {
            float dt = Time.deltaTime;
            Vector3 feet = Body.ModelFeet;
            if (_fHave && dt > 1e-5f)
            {
                Vector3 v = (feet - _fLast) / dt;
                if (v.sqrMagnitude < 3600f) _fVel = Vector3.Lerp(_fVel, v, 1f - Mathf.Exp(-dt * 8f));
            }
            _fLast = feet;
            _fHave = true;
            bool up = Mathf.Abs(_fVel.y) > 1.5f;
            if (up) { if (_airSince < 0f) _airSince = Time.time; }
            else _airSince = -1f;
            _wasAirborne = _airborne;
            _airborne = up && Time.time - _airSince > 0.15f;
        }

        /// <summary>The local eye (camera) and whether we look from the avatar's head (VR always; Somnium's desktop first person).</summary>
        private bool FirstPerson(out Vector3 eye)
        {
            Camera cam = M2Animator.FindCamera();
            eye = cam != null ? cam.transform.position : Body.HeadPos;
            if (_xr) return true;
            return cam != null && (eye - Body.HeadPos).magnitude < FormModel.FirstPersonDistance;
        }

        private void PlaceForm(PlayerState p)
        {
            if (p == null) return;
            Vector3 eye;
            bool fp = FirstPerson(out eye);
            var f = new FormFrame
            {
                Feet = Body.ModelFeet,
                Forward = Body.Forward,
                FirstPerson = fp,
                Camera = eye,
                Dt = Time.deltaTime,
                XrRight = _xr ? Body.XrRight : null,
                XrLeft = _xr ? Body.XrLeft : null,
                HandColour = GearNameplate.ClassColour((int)p.Class),
            };
            Form.Apply(Look, Body.Animator, f, GameRoot, _files, 0f);
            FormModel m = Form.Model;
            if (m == null) { _formShotCounter = Anim.Wow.Counter; return; }
            float now = Time.time;
            if (_formShotCounter >= 0 && Anim.Wow.Counter != _formShotCounter) PlayFormShot(m, Anim.Wow.OneShot, now);
            _formShotCounter = Anim.Wow.Counter;
            RideMode mode = Mode(p);
            Vector3 fwd = Body.Forward; fwd.y = 0f;
            Vector3 planar = new Vector3(_fVel.x, 0f, _fVel.z);
            float forward = planar.magnitude * (fwd.sqrMagnitude > 1e-6f && Vector3.Dot(planar, fwd) < 0f ? -1f : 1f);
            int castLoop = p.CastingSpellId != 0 && !p.Dead ? Anim.Wow.Loop : 0;
            m.Animate(mode, forward, _fVel.y, Mathf.Max(0.5f, p.RunSpeed) * WowSpace.MetresPerYard, _airborne && !_wasAirborne, !_airborne && _wasAirborne,
                      p.Dead && !p.Ghost, castLoop, now, p.InCombat || p.AutoAttacking);
        }

        /// <summary>The gait mode of our form: swimming (liquid >= 1 yd at our feet, WowMount.Place; ours), a flight form in the air
        /// (CanFly + airborne), airborne, else on the ground.</summary>
        private RideMode Mode(PlayerState p)
        {
            if (WowMount.Place.LiquidDepth >= 1.0f) return RideMode.Swimming;
            if (_airborne) return p.CanFly ? RideMode.Flying : RideMode.Airborne;
            return RideMode.Ground;
        }

        /// <summary>A WoW one-shot on a creature model, with the creature fallbacks (an attack it lacks -> 16, then 17; a release -> the other release).</summary>
        public static void PlayFormShot(FormModel m, int id, float now)
        {
            if (m == null || id <= 0) return;
            if (m.PlayOnce(id, now)) return;
            if (GearWowAnim.IsAttack(id)) { if (!m.PlayOnce(GearWowAnim.Unarmed, now)) m.PlayOnce(GearWowAnim.Attack1H, now); return; }
            if (id == GearWowAnim.CastDirected) m.PlayOnce(GearWowAnim.CastOmni, now);
            else if (id == GearWowAnim.CastOmni) m.PlayOnce(GearWowAnim.CastDirected, now);
        }

        /// <summary>For WowGear.TryLocalFormSlot: a spell visual slot on our creature model (false = not a creature look / not built).</summary>
        public bool TryFormSlot(int slot, out Vector3 pos, out Quaternion rot)
        {
            FormModel m = Form.Model;
            if (m == null) { pos = Vector3.zero; rot = Quaternion.identity; return false; }
            return m.TrySlot(slot, out pos, out rot);
        }

        /// <summary>VR: the held weapons again right before rendering, from the controller poses the client updated for this frame.</summary>
        public void BeforeRender()
        {
            if (!_xr || _hidden) return;
            for (int h = 0; h < 2; h++)
            {
                bool right = h == 0;
                GearItem it = ItemOf(right ? _right : _left);
                if (it == null || it.Go == null || !it.Go.activeSelf) continue;
                Vector3 hp, pos; Quaternion hr, rot;
                if (!Body.HandPose(right, out hp, out hr)) continue;
                GearPlace.Held(it, hp, hr, out pos, out rot);
                it.T.SetPositionAndRotation(pos, rot);
            }
            PlaceBow(WowPlay.Session, false);   // lead 2026-10-10: the string and arrow on this frame's hands
        }

        /// <summary>Desktop chop, degrees about the hand's right axis over 0..1: up to -50, down to +60, back to 0.</summary>
        private static float Chop(float t)
        {
            if (t < 0.35f) return Mathf.SmoothStep(0f, -50f, t / 0.35f);
            if (t < 0.7f) return Mathf.SmoothStep(-50f, 60f, (t - 0.35f) / 0.35f);
            return Mathf.SmoothStep(60f, 0f, (t - 0.7f) / 0.3f);
        }

        private void TestBlade(IWowSession s, bool right, ref Swing sw)
        {
            int slot = right ? _right : _left;
            GearItem it = ItemOf(slot);
            if (it == null || it.Go == null || it.IsShield || it.IsBow || slot == 17) { sw.Have = false; return; }
            Vector3 a, b; float r;
            GearPlace.BladeCapsule(it, out a, out b, out r);
            Vector3 rel = b - Body.Feet;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = 0f;
            if (sw.Have)
            {
                float step = (rel - sw.PrevTip).magnitude;
                speed = step > 1.5f ? 0f : step / dt; // a teleport is not a swing
            }
            sw.PrevTip = rel;
            sw.Have = true;
            if (speed < SwingSpeed * 0.5f) sw.Armed = true;
            if (speed > sw.Peak) sw.Peak = speed;
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, _hits, EntityLayerMask, QueryTriggerInteraction.Collide);
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                ulong guid;
                if (!WowEntityViews.TryGetGuid(_hits[i], out guid) || guid == 0) continue;
                EntityState e = s.Find(guid);
                if (e == null || e.Dead || e.Reaction == Reaction.Friendly || e.Kind != EntityKind.Creature) continue;
                if (!sw.Armed || speed < SwingSpeed) return; // a slow touch, or the same swing still going
                sw.Armed = false;
                GearXr.Pulse(right, 0.7f, 0.07f);
                bool already = s.TargetGuid == guid && s.Player.AutoAttacking;
                string what;
                if (already) what = "keeps attacking";
                else if (Time.unscaledTime - _lastStartAttack < 0.4f) what = "rate-limited";
                else
                {
                    _lastStartAttack = Time.unscaledTime;
                    try { s.StartAttack(guid); what = "StartAttack"; }
                    catch (Exception ex) { what = "StartAttack threw " + ex.Message; }
                }
                _logHits++;
                if (_logHits <= 20 || _logHits % 50 == 0)
                    WowLog.Info("Gear: VR swing (" + (right ? "right" : "left") + ", tip " + speed.ToString("0.0") + " m/s, peak " + sw.Peak.ToString("0.0") + ") hit " + e.Name + " level " + e.Level + " -> " + what + " (hit " + _logHits + ")");
                sw.Peak = 0f;
                return;
            }
        }

        /// <summary>The event feeds the avatar motions (GearAnimLocal); in VR also a light tick on the hand that holds the weapon for each hit the player lands.</summary>
        public void OnEvent(IWowSession s, GameEvent e)
        {
            if (s == null || s.Player == null) return;
            Anim.OnEvent(s, e, _files, Poser, AnimationsOn && !_xr);
            try { CastEvent(s.Player, e); }
            catch (Exception ex) { WowLog.Warn("Gear: our cast record threw on " + e.Type + ": " + ex.Message); }
            try { SheatheEvent(s.Player, e); }   // iteration 11 (combat feel): instants put away / melee draws (GearLocal.Sheathe.cs)
            catch (Exception ex) { WowLog.Warn("Gear: the sheathe rule threw on " + e.Type + ": " + ex.Message); }
            if (!_xr) return;
            if (e.Type != GameEventType.MeleeHit || e.SourceGuid != s.Player.Guid) return;
            int want = e.SpellId == 75 ? 17 : 15;
            bool? hand = _right == want ? true : _left == want ? false : (bool?)null;
            if (hand == null && want == 15) hand = _right == 16 ? true : _left == 16 ? false : (bool?)null;
            if (hand == null) return;
            GearXr.Pulse(hand.Value, e.Crit ? 0.45f : 0.25f, 0.04f);
        }

        /// <summary>What the other players are told (WowNet writes it into our slot).</summary>
        public WowGearState State(IWowSession s, bool roomMaster)
        {
            var g = new WowGearState { RoomMaster = roomMaster };
            // In the room: said, no map (others hide our avatar while they are in the world).
            if (s == null || s.Player == null) { g.Status = WowNetStatus.Present; g.Xr = GearXr.Present; g.OriginSaid = true; g.OriginMap = -1; return g; }
            PlayerState p = s.Player;
            g.Status = WowNetStatus.Playing;
            // Our floating origin (Core/WowSpace): others re-map our avatar by the difference. Before the first SetOrigin
            // (the Origin module not running) it is not said, so others draw us unmapped as older builds did.
            int map = WowSpace.OriginMap;
            g.OriginSaid = map >= 0;
            g.OriginMap = map;
            g.CellX = WowSpace.OriginCellX;
            g.CellY = WowSpace.OriginCellY;
            g.CellZ = WowSpace.OriginCellZ;
            g.OnTaxi = p.OnTaxi;
            g.MountDisplayId = p.MountDisplayId;
            // Iteration 6: logged in to a realm, our WoW character is drawn by nobody else (our avatar is there) - WowGear.IsSomniumCharacter.
            if (s is SomniumSpace.Worlds.SomniumWoW.Play.Online.OnlineSession && p.Guid != 0)
            {
                g.CharacterGuid = p.Guid;
                g.CharacterNameHash = WowNetWire.NameHash(p.Name);
            }
            g.Xr = _xr;
            g.Dead = p.Dead;
            g.InCombat = p.InCombat;
            g.AutoAttacking = p.AutoAttacking;
            g.Class = (int)p.Class;
            g.Level = p.Level;
            g.HealthPercent = p.MaxHealth > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * p.Health / p.MaxHealth), 0, 100) : 0;
            g.Sheath = (int)p.Sheath;
            g.Form = p.ShapeshiftForm;
            g.AttackCounter = _attackCounter;
            g.Race = p.Race;
            g.Motion = Anim.Motion;
            g.MotionCounter = Anim.Counter;
            g.Hold = Anim.Hold;
            g.Right = Holds(_right);
            g.Left = Holds(_left);
            g.Main = _items[0] != null && WowSettings.ShowMainHand ? _items[0].Item : default(WowWireItem);   // lead 2026-10-12: hidden = sent empty
            g.Off = _items[1] != null && WowSettings.ShowOffHand ? _items[1].Item : default(WowWireItem);
            g.Ranged = _items[2] != null && WowSettings.ShowRanged ? _items[2].Item : default(WowWireItem);
            g.Ride = Ride(p);
            g.Cast = CastRecord(p);
            // Iteration 11 (avatar): ints 31-44 - our look (everyone draws the same creature / shell), the WoW animation ids, the
            // immersive armour (the wearer decides; the pieces change only with the gear) and the weapon-enchant glows.
            AvatarLook l = Look;
            g.Look = new WowLookRecord
            {
                Kind = (int)l.Kind,
                DisplayId = l.Kind == AvatarLookKind.Creature ? l.DisplayId : 0,
                Scale = l.Scale > 0.01f ? l.Scale : 1f,
                Swimming = l.Kind == AvatarLookKind.Creature && WowMount.Place.LiquidDepth >= 1.0f,
                Airborne = l.Kind == AvatarLookKind.Creature && _airborne,
                Flying = l.Kind == AvatarLookKind.Creature && _airborne && p.CanFly,
            };
            // fix 2026-10-04 (remote animation): + our combat style (int 32 bit 28) - every viewer draws OUR choice, not its own setting
            g.Anim = new WowAnimRecord { OneShot = Anim.Wow.OneShot, Counter = Anim.Wow.Counter, Loop = Anim.Wow.Loop, SimpleArms = WowSettings.AvatarCombatStyle != 1 };
            g.Armour = ArmourRecord(p);
            g.EnchantMain = _enchantWant[0];
            g.EnchantOff = _enchantWant[1];
            g.EnchantRanged = _enchantWant[2];
            return g;
        }

        // ------------------------------------------------------------------ stage O2 (Sync): our cast on the wire (ints 28-30)

        /// <summary>Ours: a grip let go this recently (s) chose the spell of the cast that starts (the radial menu casts on release;
        /// online the cast state may follow a round trip later).</summary>
        public const float CastHandWindow = 0.6f;
        /// <summary>Ours: how long after a cast ended its hand is still told to WowSpellVfx (the CastSucceeded may come a frame later).</summary>
        public const float CastHandKeep = 1.5f;

        private readonly WowCastWriter _castWriter = new WowCastWriter();
        private readonly float[] _gripUpAt = { -100f, -100f };   // [0] left, [1] right: Time.unscaledTime of the last grip release
        private readonly bool[] _gripWas = new bool[2];
        private int _gripFrame = -1;
        private int _castSpell, _castCounter;
        private WowCastState _castState;
        private WowCastHand _castHand;
        private bool _castImmersive, _handPending, _forceFresh;
        private float _castAt, _castEndedAt = -100f;
        private int _lastCastSpell;
        private WowCastHand _lastCastHand;
        private int _relSpell, _relCounter;
        private WowCastHand _relHand;
        private bool _relSelf;
        private float _relAt = -100f;
        private int _castSaid, _relSaid;

        /// <summary>WowGear.LocalCastHand: the hand of our cast of this spell (current, just ended, or just released), else NotSaid.</summary>
        public WowCastHand CastHandFor(int spellId)
        {
            if (spellId <= 0) return WowCastHand.NotSaid;
            if (_castSpell == spellId && _castState != WowCastState.None) return _castHand;
            float now = Time.unscaledTime;
            if (_lastCastSpell == spellId && now - _castEndedAt <= CastHandKeep) return _lastCastHand;
            if (_relSpell == spellId && now - _relAt <= CastHandKeep) return _relHand;
            // A cast starting this very frame (its CastStarted may reach WowSpellVfx before StepCast saw it): the same rule StepCast uses.
            return _xr ? PickHand(false) : WowCastHand.NotSaid;
        }

        /// <summary>The grips' releases (VR), once per frame: the radial menu casts the moment its grip is let go.</summary>
        private void PollGrips()
        {
            if (_gripFrame == Time.frameCount) return;
            _gripFrame = Time.frameCount;
            if (!_xr) { _gripWas[0] = _gripWas[1] = false; return; }
            for (int h = 0; h < 2; h++)
            {
                bool down = GearXr.GripHeld(h == 1);
                if (_gripWas[h] && !down)
                {
                    _gripUpAt[h] = Time.unscaledTime;
                    if (_handPending && _castState != WowCastState.None && Time.unscaledTime - _castAt <= CastHandWindow)
                    {
                        _handPending = false;
                        _castHand = h == 1 ? WowCastHand.Right : WowCastHand.Left;
                        SayCast("Gear: our cast record - " + SpellName(_castSpell) + " hand " + _castHand + " (the grip let go just after the cast began)");
                    }
                }
                _gripWas[h] = down;
            }
        }

        /// <summary>The hand the next cast / release comes from: VR = the grip let go within CastHandWindow (the latest), else not said;
        /// desktop = both hands (the Cast hold gathers both), a single instant at a target the right hand (the CastInstant motion).</summary>
        private WowCastHand PickHand(bool instantAtOther)
        {
            if (!_xr) return instantAtOther ? WowCastHand.Right : WowCastHand.Both;
            PollGrips();
            float now = Time.unscaledTime;
            float r = now - _gripUpAt[1], l = now - _gripUpAt[0];
            bool rOk = r <= CastHandWindow, lOk = l <= CastHandWindow;
            if (rOk && (!lOk || r <= l)) return WowCastHand.Right;
            if (lOk) return WowCastHand.Left;
            return WowCastHand.NotSaid;
        }

        /// <summary>Each frame (Tick): what our PlayerState says we cast - a new cast steps the counter and picks the hand.</summary>
        private void StepCast(PlayerState p)
        {
            PollGrips();
            WowCastState state = WowCastState.None;
            int spell = 0;
            if (!p.Dead)
            {
                if (p.HeldSpellId != 0) { state = WowCastState.Held; spell = p.HeldSpellId; }
                else if (p.CastingSpellId != 0) { state = p.Channeling ? WowCastState.Channeling : WowCastState.Casting; spell = p.CastingSpellId; }
            }
            bool fresh = state != WowCastState.None && (spell != _castSpell || _castState == WowCastState.None || (_castState == WowCastState.Held && state != WowCastState.Held) || _forceFresh);
            _forceFresh = false;
            if (state == WowCastState.None && _castState != WowCastState.None)
            {
                _lastCastSpell = _castSpell;
                _lastCastHand = _castHand;
                _castEndedAt = Time.unscaledTime;
                _handPending = false;
            }
            if (fresh)
            {
                _castCounter = (_castCounter + 1) & 0xF;
                _castAt = Time.unscaledTime;
                _castHand = PickHand(false);
                _handPending = _xr && _castHand == WowCastHand.NotSaid;
                _castImmersive = _xr && WowSettings.ImmersiveCasting && state == WowCastState.Casting && (_castHand == WowCastHand.Right || _castHand == WowCastHand.Left || _handPending)
                                 && !MountCatalog.IsMount(spell);
                SayCast("Gear: our cast record - " + SpellName(spell) + " " + (state == WowCastState.Channeling ? "channelling" : state == WowCastState.Held ? "held" : "casting") +
                        ", hand " + (_handPending ? "not known yet" : _castHand.ToString()) + (p.CastTotal > 0.01f ? ", " + p.CastTotal.ToString("0.0") + " s" : "") +
                        (_castImmersive ? ", immersive (others see the glow grow in that hand)" : "") + " - other Somnium players see it on your avatar");
            }
            if (_handPending && Time.unscaledTime - _castAt > CastHandWindow) _handPending = false;   // stays not said: viewers use both hands
            if (state == WowCastState.Held && _castState == WowCastState.Casting && spell == _castSpell)
                SayCast("Gear: our cast record - " + SpellName(spell) + " held in the " + _castHand + " hand (others see the held-spell glow until you push it)");
            _castSpell = state != WowCastState.None ? spell : 0;
            _castState = state;
        }

        /// <summary>A CastSucceeded of ours: the release record (its cast kit plays on our avatar for the others).</summary>
        private void CastEvent(PlayerState p, GameEvent e)
        {
            // A new cast of the spell we are already casting (no frame without a cast between them): a new cast all the same.
            if (e.Type == GameEventType.CastStarted && e.SourceGuid == p.Guid && _castState != WowCastState.None && e.SpellId == _castSpell && Time.unscaledTime - _castAt > 0.1f)
                _forceFresh = true;
            if (e.Type != GameEventType.CastSucceeded || e.SourceGuid != p.Guid || e.SpellId <= 0) return;
            if (e.SpellId == 75) return;                                        // Auto Shot: the swing counter shows it
            if (p.Channeling && e.SpellId != p.CastingSpellId) return;          // a channel's ticks (Arcane Missiles 7268)
            bool ours = e.SpellId == _castSpell || e.SpellId == _lastCastSpell || InSpellbook(p, e.SpellId) || e.SpellId == p.HeldSpellId
                        || e.AtDest;   // lead fix 2026-10-12: an item's spell aimed at a place (dynamite) is ours - others see its throw
            if (!ours) return;                                                  // triggered spells: the caster's own look is enough
            bool atSelf = e.TargetGuid == 0 || e.TargetGuid == p.Guid;   // (offline p.Guid is 1; online guid 1 can be another character)
            WowCastHand hand;
            if (e.SpellId == _castSpell && _castState != WowCastState.None) hand = _castHand;
            else if (e.SpellId == _lastCastSpell && Time.unscaledTime - _castEndedAt <= CastHandKeep) hand = _lastCastHand;
            else hand = PickHand(!atSelf);
            _relSpell = e.SpellId;
            _relCounter = (_relCounter + 1) & 0xF;
            _relHand = hand;
            _relSelf = atSelf;
            _relAt = Time.unscaledTime;
            _relSaid++;
            if (_relSaid <= 10 || _relSaid % 50 == 0)
                WowLog.Info("Gear: our release record - " + SpellName(e.SpellId) + ", hand " + hand + (atSelf ? ", on ourselves" : ", at the target") + " (release " + _relSaid + ")");
        }

        private static bool InSpellbook(PlayerState p, int spellId)
        {
            if (p.Spellbook == null) return false;
            for (int i = 0; i < p.Spellbook.Count; i++) if (p.Spellbook[i] != null && p.Spellbook[i].SpellId == spellId) return true;
            return false;
        }

        /// <summary>Ints 28-30 as the others read them: the cast (timing held steady by WowCastWriter), the last release.</summary>
        private WowCastRecord CastRecord(PlayerState p)
        {
            var c = new WowCastRecord();
            if (_castState != WowCastState.None && _castSpell > 0)
            {
                c.SpellId = _castSpell;
                c.State = _castState;
                c.Hand = _castHand;
                c.Counter = _castCounter;
                c.Immersive = _castImmersive;
                c.TotalMs = p.CastTotal > 0.001f ? Mathf.RoundToInt(p.CastTotal * 1000f) : 0;
                c.Progress = _castState == WowCastState.Held ? 1f : (p.CastTotal > 0.001f ? Mathf.Clamp01(p.CastElapsed / p.CastTotal) : 0f);
            }
            if (_relSpell > 0)
            {
                c.ReleaseSpellId = _relSpell;
                c.ReleaseCounter = _relCounter;
                c.ReleaseHand = _relHand;
                c.ReleaseAtSelf = _relSelf;
            }
            return _castWriter.Step(c, Time.realtimeSinceStartupAsDouble);
        }

        private string SpellName(int spellId)
        {
            SpellInfo sp = null;
            try { sp = _files != null ? SpellDb.Get(_files).Find(spellId) : null; }
            catch (Exception) { sp = null; }
            return (sp != null && !string.IsNullOrEmpty(sp.Name) ? sp.Name + " " : "spell ") + "(" + spellId + ")";
        }

        private void SayCast(string line)
        {
            _castSaid++;
            if (_castSaid <= 12 || _castSaid % 50 == 0) WowLog.Info(line);
        }

        // ------------------------------------------------------------------ iteration 7: the local ride on the wire (ints 17-27)

        private RideRecord _rideSent;
        private int _rideSeq, _rideStamp;
        private bool _rideStampHave;
        private string _rideSaid;

        /// <summary>
        /// WowMount.Snapshot (what the local ride drew in its LateUpdate) as the other players read it: kind, gait and rate (rate
        /// rewritten in 0.05 steps), mount-up counter, epoch, the mount's pose relative to the synced root (rewritten only past 5 mm /
        /// 0.2 deg - constant on desktop), and the stage-B stamped root pose with a sample seq that steps with each new stamp.
        /// Kind 0 (nothing said) on foot, dead, or before the ride owns the avatar: viewers then draw int 13 by the fallback rule.
        /// </summary>
        private RideRecord Ride(PlayerState p)
        {
            MountSnapshot m = WowMount.Snapshot;
            MountSnapshot flight;
            if (!m.Riding && m.Kind == 0 && Game.Flight.FlightRide.TryGetSnapshot(out flight)) m = flight;   // lead fix 2026-10-04: on a taxi
            RideRecord r = default(RideRecord);
            if ((!m.Riding && m.Kind == 0) || p.Dead)
            {
                if (_rideSent.Kind != 0) SayRide("Gear: our ride record cleared (" + (p.Dead ? "dead" : "not riding") + ")");
                _rideSent = r;
                return r;
            }
            r.Kind = m.Kind != 0 ? m.Kind : (byte)(m.Flying || p.MountKind == MountKind.Flying ? 2 : 1);
            r.Airborne = m.Airborne;
            r.Swimming = m.Swimming;
            r.GaitAnim = m.GaitAnim;
            r.GaitRate = m.GaitRate;
            r.MountUpCounter = m.MountUpCounter & 0xF;
            r.Epoch = m.Epoch & 0x1F;
            if (m.RelativeSaid)
            {
                r.RelativeSaid = true;
                r.RelPos = new RideVec(m.MountInRoot.x, m.MountInRoot.y, m.MountInRoot.z);
                r.RelYawDeg = m.MountYawInRoot;
            }
            r = WowRideWriter.Steady(_rideSent, r);   // rate in 0.05 steps, the relative pose past 5 mm / 0.2 deg only
            if (m.PoseStampMs != 0)
            {
                if (!_rideStampHave || m.PoseStampMs != _rideStamp)
                {
                    _rideStampHave = true;
                    _rideStamp = m.PoseStampMs;
                    _rideSeq = (_rideSeq + 1) & 0xFFFF;
                }
                r.PoseSaid = true;
                r.PoseStampMs = m.PoseStampMs;
                r.Seq = _rideSeq;
                r.Pos = new RideVec(m.RootPos.x, m.RootPos.y, m.RootPos.z);
                r.Vel = new RideVec(m.RootVel.x, m.RootVel.y, m.RootVel.z);
                r.Yaw = m.RootYaw;
            }
            if (_rideSent.Kind == 0 || _rideSent.MountUpCounter != r.MountUpCounter || _rideSent.RelativeSaid != r.RelativeSaid || _rideSent.PoseSaid != r.PoseSaid)
                SayRide("Gear: our ride record - kind " + r.Kind + ", mount display " + p.MountDisplayId + ", mount-up " + r.MountUpCounter + ", epoch " + r.Epoch +
                        (r.RelativeSaid ? ", mount relative to the synced root (" + r.RelPos.X.ToString("0.000") + ", " + r.RelPos.Y.ToString("0.000") + ", " + r.RelPos.Z.ToString("0.000") + ") m, yaw " + r.RelYawDeg.ToString("0.0") : ", relative pose not said (viewers use the hips fallback)") +
                        (r.PoseSaid ? ", stage-B pose said" : ", no stage-B pose"));
            _rideSent = r;
            return r;
        }

        private void SayRide(string line)
        {
            if (line == _rideSaid) return;
            _rideSaid = line;
            WowLog.Info(line);
        }

        private static WowHandHolds Holds(int slot)
        {
            return slot == 15 ? WowHandHolds.MainHand : slot == 16 ? WowHandHolds.OffHand : slot == 17 ? WowHandHolds.Ranged : WowHandHolds.None;
        }
    }
}
