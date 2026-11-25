using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Game.VrInput;
using SomniumSpace.Worlds.SomniumWoW.Play;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using UnityEngine;
using UnityEngine.XR;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// VR input (iteration 4; nothing at all on a monitor):
    /// - Half-radial spell menus: GRIP on a hand (not at a sheath: WowGear.HandNearSheath) opens that hand's menu where the hand
    ///   is (right = buttons 1-6 of the row's bar, left = 7-12); TWIST the hand around the forearm (+-75 degrees, dead zone and a
    ///   haptic tick per segment: VrMath) to move the highlight; RELEASE uses the button (IWowSession.UseActionButton / CastSpell, or
    ///   CastSpellHeld in Immersive Mode: VrImmersive). Released in the dead zone or on an empty button = cancel.
    /// - Iteration 11 (vrcast, proposal-vrcast.md 1.2): ROWS - while the grip is held, PUSH the hand away (VrMath.RowStep 7 cm per row
    ///   along the level eye-to-hand direction of the grip) for the Bottom Left Bar, then the Bottom Right Bar; PULL it toward you for
    ///   the Right Bar, then Right Bar 2; a firm row click (RowPulse) on every change, the ring moves to the new depth and shows only that
    ///   row; ghost rings and a pip ladder show that a push / pull reveals another row. The old "pull back 12 cm = cancel" is gone (the
    ///   pull is the Right Bar row now): release in the dead zone to cancel. Each grip starts on the Action Bar row (Settings: "Rows
    ///   remember the last one"). The pets owner's pet bar is row +3 while there is one; a vehicle / possess bar replaces row 0.
    /// - Iteration 11: SLOTTING (proposal-vrcast.md 1.4) - while something is on WoW's action cursor (a spell tapped in the spellbook,
    ///   "Clear a button", a mount, an item) both wrist menus follow the hands (60 % opaque); grip either hand, twist, push / pull,
    ///   release = it is placed on that button (UiActionCursor.PlaceOn); release in the dead zone, or 30 s without a grip, cancels.
    /// - Far targeting along the client's own laser (stage S1: VrClientLaser finds it, VrTargetRay casts along it and draws
    ///   no line or dot of its own - the entity pointed at is highlighted instead); the trigger selects / interacts through
    ///   WowEntityViews.OnEntityClicked - the same path, and the same 0.35 s same-guid guard, as the client's 5 m XRI click
    ///   (WowEntityClick). When the client's laser itself hovers one of our entities that entity is the one pointed at (one
    ///   press never selects two things); while the laser is on something of the client's own (its UI, its interactables,
    ///   holding), in the client's inspector mode, or switched off by the client, the far targeting stands down and the
    ///   trigger is the client's. Not while the pointer is on our UI (WowArmUi.PointerOnUi).
    /// Added by WowGame.Awake on the "WoW Game" object (next to WowEntityViews); it adds VrStrikeRing (immersive strike).
    /// </summary>
    public sealed class WowVrInput : MonoBehaviour
    {
        /// <summary>Ours (proposal-vrcast 1.2): the row click - firmer than the twist tick (0.25 / 0.02 s).</summary>
        public const float RowPulseAmplitude = 0.45f, RowPulseSeconds = 0.05f;
        /// <summary>Ours (proposal-vrcast 1.4): seconds of slotting without any grip before the cursor is dropped.</summary>
        public const float SlottingTimeout = 30f;
        /// <summary>Ours: below this horizontal eye-to-hand distance at the grip the push axis is the head's level forward.</summary>
        public const float MinAxisReach = 0.10f;

        private sealed class Hand
        {
            public bool Right;
            public bool Open;
            public bool Preview;          // slotting: the menu follows the hand before a grip
            public Quaternion StartRot;   // hand rotation in the avatar root's frame when the grip went down
            public Vector3 Anchor;        // ring centre in the avatar root's frame at the grip
            public Vector3 StartLocal;    // the hand in the root's frame at the grip
            public Vector3 AxisLocal;     // the push direction (level eye -> hand at the grip) in the root's frame
            public float RowBase;         // the depth the grip started at (the remembered row)
            public int Row, LastRow;
            public int Highlight = -1;
            public VrRadialMenu Menu;
        }

        private static WowVrInput _current;
        private readonly Hand[] _hands = { new Hand { Right = false }, new Hand { Right = true } };
        private readonly VrRows _rows = new VrRows();
        private VrTargetRay _ray;
        private VrImmersive _imm;
        private WowEntityViews _views;
        private bool _built, _broken, _trigger, _shown, _loggedStart, _loggedAim, _loggedTyping, _petRowsFailed;
        private int _casts, _selects, _errors, _rowLogs, _slotVersion = int.MinValue;
        private float _slotSince;

        /// <summary>Iteration 11: the hand's radial menu is open (VrStrikeRing: that hand never strikes).</summary>
        public static bool MenuOpen(bool right)
        {
            WowVrInput c = _current;
            return c != null && c._hands[right ? 1 : 0].Open;
        }

        /// <summary>Lead 2026-10-05: the twist gain from WowSettings.WristSensitivity - 0 = x0.6, 0.5 = x1 (as before), 1 = x2 (ours).</summary>
        /// <summary>Lead 2026-10-10: Gear Select (WowSettings.VrMenuStyle 1) - metres sideways to the outermost spell, metres up / down a bar (ours).</summary>
        public const float GearReach = 0.18f, GearRowMetres = 0.09f;
        private static bool _gearSaid;

        private static float TwistGain()
        {
            float s = Mathf.Clamp01(WowSettings.WristSensitivity);
            return s < 0.5f ? Mathf.Lerp(0.6f, 1f, s / 0.5f) : Mathf.Lerp(1f, 2f, (s - 0.5f) / 0.5f);
        }

        private void Awake()
        {
            _current = this;
            // Iteration 11 (vrcast): the ActionBars page's VR rows are built (WowSettings.SetLive shows their Settings rows).
            WowSettings.SetLive("vr_rows");
            WowSettings.SetLive("vr_rows_remember");
            WowSettings.SetLive("immersive_strike");
            WowSettings.SetLive("wrist_sensitivity");   // lead 2026-10-05
            WowSettings.SetLive("vr_menu_style");       // lead 2026-10-10
            WowSettings.SetLive("strike_ring_bar");
            WowSettings.SetLive("strike_ring_distance");
            WowSettings.SetLive("strike_ring_size");     // lead 2026-10-03
            WowSettings.SetLive("strike_ring_spread");
            WowSettings.SetLive("strike_ring_height");
            if (GetComponent<VrStrikeRing>() == null) gameObject.AddComponent<VrStrikeRing>();
        }

        private void LateUpdate()
        {
            // Stage S1 (integrator, PLAN10 s5): the sessions show the "Immersive Mode" buff (+10 % damage on spells you push) while this
            // is true - VR with Settings > Immersive casting on; false on desktop.
            ImmersiveMode.Active = WowPlayer.XrPresent && WowSettings.ImmersiveCasting;
            if (_broken) return;
            IWowSession s = WowPlay.Session;
            if (s == null || !WowPlayer.XrPresent) { HideAll(); return; }
            try
            {
                Step(s);
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 5) WowLog.Warn("VR input: " + e.GetType().Name + ": " + e.Message + (_errors == 5 ? " (VR input off after 5 errors)" : ""));
                if (_errors >= 5) { _broken = true; HideAll(); }
            }
        }

        private void Build()
        {
            if (_built) return;
            _built = true;
            _hands[0].Menu = new VrRadialMenu(false, WowSettings.VrMenuStyle == 1);
            _hands[1].Menu = new VrRadialMenu(true, WowSettings.VrMenuStyle == 1);
            _ray = new VrTargetRay();
            _imm = new VrImmersive();
            _views = GetComponent<WowEntityViews>();
            if (_views == null) _views = FindFirstObjectByType<WowEntityViews>();
            WowLog.Info("VR input: radial menus, target ray and immersive casting ready (entity views " + (_views != null ? "found" : "MISSING - ray selects through the session") + ")");
            WowLog.Info("VR input: far targeting along the client's laser, nothing of ours drawn on the ray; hover = nameplate glow + a soft glow (ours: alpha "
                        + VrTargetRay.GlowAlpha.ToString("0.00") + ", x" + VrTargetRay.GlowSize.ToString("0.00") + " the model), far hover tick "
                        + VrTargetRay.HoverTickAmplitude.ToString("0.00") + " for " + VrTargetRay.HoverTickSeconds.ToString("0.00") + " s (the client's own), at most every "
                        + VrTargetRay.HoverTickGap.ToString("0.00") + " s (ours)");
            WowLog.Info("VR input: rows " + (WowSettings.VrRows ? "on" : "off") + " (step " + VrMath.RowStep.ToString("0.00") + " m, hold " + VrMath.RowHold.ToString("0.000")
                        + " m, row pulse " + RowPulseAmplitude.ToString("0.00") + " / " + RowPulseSeconds.ToString("0.00") + " s; each grip starts on "
                        + (WowSettings.VrRowsRemember ? "the hand's last row" : "the Action Bar row") + "); slotting timeout " + SlottingTimeout.ToString("0") + " s (ours)");
        }

        private void Step(IWowSession s)
        {
            Build();
            _shown = true;
            PlayerState p = s.Player;
            Camera cam = WowPlayer.Head();
            Transform root = WowPlayer.Root;
            Transform left = WowPlayer.LeftHand, right = WowPlayer.RightHand;
            if (cam == null || p == null) { HideAll(); return; }
            Vector3 eye = cam.transform.position;
            Vector3 headFwd = cam.transform.forward;
            if (!_loggedStart) { _loggedStart = true; WowLog.Info("VR input: running (hands " + (left != null ? "L" : "-") + (right != null ? "R" : "-") + ", root " + (root != null ? "yes" : "no") + ")"); }

            UiBars.Tick(p);
            float now = Time.unscaledTime;
            bool canCast = !p.Dead && !p.Ghost && !p.OnTaxi;
            // Slotting: something an action button takes is on WoW's action cursor (not while a VR trigger-drag still holds it).
            bool slotting = UiActionCursor.Active && !UiPickup.Dragging;
            if (UiActionCursor.Version != _slotVersion) { _slotVersion = UiActionCursor.Version; _slotSince = now; if (slotting) WowLog.Info("VR input: slotting " + Label(UiActionCursor.Packed, UiActionCursor.Name) + " - wrist menus up"); }
            if (_hands[0].Open || _hands[1].Open) _slotSince = now;
            if (slotting && now - _slotSince > SlottingTimeout)
            {
                WowLog.Info("VR input: slotting " + Label(UiActionCursor.Packed, UiActionCursor.Name) + " cancelled - " + SlottingTimeout.ToString("0") + " s without a grip");
                UiActionCursor.Clear();
                Say("Cancelled.");
                slotting = false;
            }
            FillRows(s, slotting);

            StepHand(s, p, _hands[0], left, root, eye, headFwd, canCast, slotting);
            StepHand(s, p, _hands[1], right, root, eye, headFwd, canCast, slotting);
            _imm.Tick(s, p, left, right, root, eye, headFwd, Time.unscaledDeltaTime, _hands[0].Open, _hands[1].Open);

            // Far targeting along the client's laser (right hand), off while the right menu is open, on a flight, or on our UI.
            bool trig = Trigger();
            bool pressed = trig && !_trigger;
            _trigger = trig;
            // spellfx hook: while a ground spell waits for its place, the trigger is the ground circle's (UiGroundTarget).
            if (UiGroundTarget.Active) { _ray.Hide(_views); return; }
            if (right == null || _hands[1].Open || p.OnTaxi || WowArmUi.PointerOnUi || WowFlight.PointerOnMap)
            {
                _ray.Hide(_views);
                return;
            }
            // Stage S1 (integrator, reports10/chat.md request 3): while the chat's headset keyboard is open the trigger types (the
            // keyboard is a uGUI canvas the client's laser presses, not one of our UiSurfaces, so PointerOnUi stays false over it).
            if (UiChat.VrTyping)
            {
                if (!_loggedTyping) { _loggedTyping = true; WowLog.Info("VR input: the chat keyboard is open - far targeting paused while typing (logged once)."); }
                _ray.Hide(_views);
                return;
            }
            Ray aim;
            if (!AimRay(right, out aim)) { _ray.Hide(_views); return; }   // the client switched its laser off
            // The client's inspector mode, or its laser on its own UI / interactable / holding: the laser and this press are its.
            ulong clientGuid = VrClientLaser.HoveredEntity;
            if (VrClientLaser.Inspector || (clientGuid == 0 && VrClientLaser.Busy))
            {
                _ray.Hide(_views);
                return;
            }
            float range = Mathf.Clamp(WowSettings.VrTargetRange, 20f, 300f);
            float dist;
            ulong guid;
            Vector3 laserEnd;
            if (clientGuid != 0)
            {
                // The client's laser is on one of our entities (its 5 m reach): that one, where the client's own raycast met it.
                guid = clientGuid;
                Vector3 hit;
                Transform view = WowEntityViews.GetView(guid);
                dist = VrClientLaser.TryGetEnd(out hit) ? Vector3.Distance(aim.origin, hit) : view != null ? Vector3.Distance(aim.origin, view.position) : 0f;
                laserEnd = aim.GetPoint(dist);
            }
            else
            {
                Vector3 end;
                bool blocked;
                guid = _ray.Cast(aim, range, out end, out dist, out blocked);
                laserEnd = end;
            }
            _ray.Highlight(s, guid, dist, clientGuid, eye, _views);
            _ray.Laser(aim.origin, laserEnd, guid != 0);   // lead 2026-10-03: the 50 % laser
            if (!pressed) return;
            if (!VrTabletGuard.RayAllowed(aim, guid != 0 ? dist : range)) return;   // lead: a press on the Somnium tablet is the tablet's
            if (guid == 0)
            {
                // Lead, 2026-09-27: the trigger with the ray on the ground (or the sky) clears the target, as a click does on a
                // monitor - unless a pointer is on something (that press is its).
                if (WowEntityViews.HoveredGuid == 0 && _views != null) _views.ClearTarget("VR ray on the ground");
                return;
            }
            // The client's XRI click fires too when its laser hovers this entity (WowEntityClick.activated): the 0.35 s same-guid
            // guard in OnEntityClicked keeps one of the two, as since iteration 4.
            Select(s, guid, dist, clientGuid == guid);
        }

        /// <summary>The rows this frame (pets H9b: the pet row and the vehicle row come from UiPetRows; none while slotting).</summary>
        private void FillRows(IWowSession s, bool slotting)
        {
            _rows.Slotting = slotting;
            _rows.Pet = null;
            _rows.Vehicle = null;
            if (!slotting && !_petRowsFailed)
            {
                try
                {
                    UiControlRow r;
                    if (UiPetRows.PetRow(s, out r)) _rows.Pet = r;
                    if (UiPetRows.VehicleRow(s, out r)) _rows.Vehicle = r;
                }
                catch (Exception e)
                {
                    _petRowsFailed = true;
                    WowLog.Warn("VR input: the pet / vehicle rows failed (" + e.GetType().Name + ": " + e.Message + ") - the five action bar rows stay");
                }
            }
            _rows.MaxRow = WowActionSlots.MaxRow + (_rows.Pet != null ? 1 : 0);
        }

        private void Select(IWowSession s, ulong guid, float dist, bool clientLaserOnIt)
        {
            _selects++;
            if (_selects <= 20 || _selects % 50 == 0)
            {
                EntityState e = s.Find(guid);
                WowLog.Info("VR input: target ray trigger on " + (e != null ? e.Name : "guid " + guid) + " at " + dist.ToString("0.0") + " m"
                            + (clientLaserOnIt ? " (the client's laser is on it)" : ""));
            }
            if (_views != null) { _views.OnEntityClicked(guid, "VR ray", WowEntityViews.ClickKind.Vr); return; }
            EntityState t = s.Find(guid);
            if (t == null) return;
            if (s.TargetGuid != guid) s.SetTarget(guid);
            else
            {
                if (t.Kind == EntityKind.Npc || (t.Dead && t.Lootable) || WowEntityViews.AttackOnClick(s, t)) s.Interact(guid);   // stage O2: a targeted enemy -> attack
                Ui.UiDeath.ReopenHealer(s, t);   // stage O2 fix: the spirit healer's offer again after a Cancel
            }
        }

        private void StepHand(IWowSession s, PlayerState p, Hand h, Transform hand, Transform root, Vector3 eye, Vector3 headFwd, bool canCast, bool slotting)
        {
            bool held = GearXr.GripHeld(h.Right);
            // slotting needs no living character (the dead may sort their bars too) - not on a flight
            bool allowed = canCast || (slotting && !p.OnTaxi);
            if (!h.Open)
            {
                bool preview = slotting && hand != null && !p.OnTaxi;
                if (hand == null || !allowed || !GearXr.GripPressed(h.Right) || WowGear.HandNearSheath(h.Right) || !VrTabletGuard.MenuAllowed(h.Right, hand.position))
                {
                    if (preview) ShowPreview(s, p, h, hand, root, eye);
                    else if (h.Preview) { h.Preview = false; h.Menu.Show(false); }
                    return;
                }
                OpenMenu(h, hand, root, eye, headFwd);
            }
            if (hand == null || !allowed)
            {
                Close(h, false);
                return;
            }

            // Twist since the grip went down (in the root's frame: snap turns and walking do not twist).
            Quaternion rel = Quaternion.Inverse(h.StartRot) * Local(root, hand.rotation);
            float twist = VrMath.TwistDegrees(rel.z, rel.w) * TwistGain();   // lead 2026-10-05: Settings "Spell menu twist sensitivity"
            bool gear = h.Menu.Bar;   // the style the menu was opened in (EnsureStyle)
            Vector3 moved = LocalPoint(root, hand.position) - h.StartLocal;
            if (gear)
            {
                // lead 2026-10-10 (the user: "Gear Select" - "instead of grip and twist, you grip and MOVE, left or right, to select a spell and
                // release on the spell to cast ... GRIP, physically move UP or DOWN to be on a different bar"): the hand's sideways move since
                // the grip (across the level eye -> hand axis, root frame) read as the twist - GearReach to the outermost spell; the dead zone
                // and the sticky segment edges of VrMath.Segment are the "auto aim" (a near miss stays on the spell)
                Vector3 across = Vector3.Cross(Vector3.up, h.AxisLocal);
                float dx = across.sqrMagnitude > 1e-6f ? Vector3.Dot(moved, across.normalized) : moved.x;
                twist = -dx / GearReach * VrMath.MaxRoll * TwistGain();
                if (!_gearSaid) { _gearSaid = true; WowLog.Info("VR input: spell menus in Gear Select - move the gripped hand left / right (" + GearReach + " m to the outer spell), up / down " + GearRowMetres + " m a bar, release to cast (ours)."); }
            }
            int seg = VrMath.Segment(twist, VrRadialMenu.Count, h.Highlight);
            if (seg != h.Highlight)
            {
                h.Highlight = seg;
                GearXr.Pulse(h.Right, seg >= 0 ? 0.25f : 0.12f, 0.02f);
            }
            // Push / pull since the grip went down, along the level eye -> hand direction of the grip (root frame: walking and turning do not count).
            float depth = gear ? h.RowBase + moved.y * (VrMath.RowStep / GearRowMetres)   // lead 2026-10-10: Gear Select - up / down a bar
                               : h.RowBase + Vector3.Dot(LocalPoint(root, hand.position) - h.StartLocal, h.AxisLocal);
            int row = WowSettings.VrRows ? VrMath.Row(depth, h.Row, WowActionSlots.MinRow, _rows.MaxRow) : 0;
            if (row != h.Row)
            {
                h.Row = row;
                GearXr.Pulse(h.Right, RowPulseAmplitude, RowPulseSeconds);
                _rowLogs++;
                if (_rowLogs <= 30 || _rowLogs % 50 == 0)
                    WowLog.Info("VR input: " + (h.Right ? "right" : "left") + " hand row -> " + RowLabel(row) + " (depth " + depth.ToString("0.00") + " m)");
            }
            Vector3 axisWorld = root != null ? root.rotation * h.AxisLocal : h.AxisLocal;
            Vector3 centre = gear ? WorldPoint(root, h.Anchor + Vector3.up * ((h.Row * VrMath.RowStep - h.RowBase) / VrMath.RowStep * GearRowMetres))   // the menu rides with the bar
                                  : WorldPoint(root, h.Anchor + h.AxisLocal * (h.Row * VrMath.RowStep - h.RowBase));
            bool rows = WowSettings.VrRows;
            h.Menu.Place(centre, eye, axisWorld, rows && _rows.Exists(h.Row - 1), rows && _rows.Exists(h.Row + 1));
            h.Menu.Refresh(s, p, _rows, h.Row, h.Highlight, false, Time.unscaledTime);
            UiArtPump();

            if (!held) Close(h, true);
        }

        /// <summary>Lead 2026-10-10: the hand's menu in the style Settings > Controls "Spell menu style" names now - the half ring (Twist
        /// wrist) or the bar (Gear Select) - rebuilt when it changed, only while closed.</summary>
        private static void EnsureStyle(Hand h)
        {
            bool bar = WowSettings.VrMenuStyle == 1;
            if (h.Menu != null && h.Menu.Bar == bar) return;
            if (h.Menu != null) h.Menu.Destroy();
            h.Menu = new VrRadialMenu(h.Right, bar);
            WowLog.Info("VR input: " + (h.Right ? "right" : "left") + " spell menu rebuilt as " + (bar ? "the Gear Select bar (six tiles in a row)" : "the half ring (Twist wrist)"));
        }

        private void OpenMenu(Hand h, Transform hand, Transform root, Vector3 eye, Vector3 headFwd)
        {
            EnsureStyle(h);
            h.Open = true;
            h.Preview = false;
            h.StartRot = Local(root, hand.rotation);
            Vector3 toEye = eye - hand.position;
            toEye.y = 0f;
            Vector3 centre = hand.position + Vector3.up * 0.04f + (toEye.sqrMagnitude > 1e-4f ? toEye.normalized * 0.05f : Vector3.zero);
            h.Anchor = LocalPoint(root, centre);
            h.StartLocal = LocalPoint(root, hand.position);
            Vector3 toHand = hand.position - eye;
            toHand.y = 0f;
            if (toHand.magnitude < MinAxisReach) { toHand = headFwd; toHand.y = 0f; }
            if (toHand.sqrMagnitude < 1e-6f) toHand = Vector3.forward;
            h.AxisLocal = (root != null ? Quaternion.Inverse(root.rotation) * toHand.normalized : toHand.normalized);
            int start = WowSettings.VrRowsRemember && WowSettings.VrRows ? h.LastRow : 0;
            h.Row = Mathf.Clamp(start, WowActionSlots.MinRow, _rows.MaxRow);
            h.RowBase = h.Row * VrMath.RowStep;
            h.Highlight = -1;
            h.Menu.Show(true);
            GearXr.Pulse(h.Right, 0.2f, 0.03f);
        }

        /// <summary>Slotting before a grip: the hand's menu follows the hand (as at a grip: 4 cm up, 5 cm toward the eye), 60 % opaque.</summary>
        private void ShowPreview(IWowSession s, PlayerState p, Hand h, Transform hand, Transform root, Vector3 eye)
        {
            if (!h.Preview) { EnsureStyle(h); h.Preview = true; h.Menu.Show(true); }
            Vector3 toEye = eye - hand.position;
            toEye.y = 0f;
            Vector3 centre = hand.position + Vector3.up * 0.04f + (toEye.sqrMagnitude > 1e-4f ? toEye.normalized * 0.05f : Vector3.zero);
            Vector3 toHand = -toEye;
            if (toHand.sqrMagnitude < 1e-6f) toHand = Vector3.forward;
            int row = Mathf.Clamp(WowSettings.VrRowsRemember && WowSettings.VrRows ? h.LastRow : 0, WowActionSlots.MinRow, _rows.MaxRow);
            bool rows = WowSettings.VrRows;
            h.Menu.Place(centre, eye, toHand.normalized, rows && _rows.Exists(row - 1), rows && _rows.Exists(row + 1));
            h.Menu.Refresh(s, p, _rows, row, -1, true, Time.unscaledTime);
            UiArtPump();
        }

        private void Close(Hand h, bool release)
        {
            if (!h.Open) return;
            h.Open = false;
            h.Menu.Show(false);
            h.Preview = false;
            if (!release) return;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            if (s == null || p == null) return;
            int seg = h.Highlight, row = h.Row;
            h.LastRow = row;
            string hand = h.Right ? "right" : "left";
            bool slotting = UiActionCursor.Active;
            if (seg < 0)
            {
                if (slotting)
                {
                    UiActionCursor.Clear();
                    Say("Cancelled.");
                    Log("VR input: " + hand + " radial menu released in the middle - slotting cancelled");
                    return;
                }
                // spellfx hook: a ground spell waiting for its place is cancelled by an empty release too.
                if (p.Targeting != null) { s.CancelTargeting(); GearXr.Pulse(h.Right, 0.15f, 0.02f); }
                Log("VR input: " + hand + " radial menu released - no segment, cancelled");
                return;
            }
            UiControlRow control = _rows.ControlOf(row);
            if (control != null && !slotting)
            {
                int code = h.Menu.ControlAt(control, seg);
                if (code == -1) { Log("VR input: " + hand + " radial menu released - empty " + (control.Name ?? "control") + " button"); return; }
                Log("VR input: " + hand + " radial menu -> " + (control.Name ?? "control bar") + " button code " + code);
                UiPetRows.Use(s, code, 0);
                GearXr.Pulse(h.Right, 0.5f, 0.04f);
                return;
            }
            int slot = h.Menu.SlotAt(row, seg);
            if (!WowActionSlots.Valid(slot)) return;
            string where = WowActionSlots.BarName(slot) + " button " + WowActionSlots.ButtonOf(slot);
            if (slotting)
            {
                string what = Label(UiActionCursor.Packed, UiActionCursor.Name);
                int before = p.ActionButtons[slot];
                UiActionCursor.PlaceOn(s, slot, true);
                bool placed = !UiActionCursor.Active || p.ActionButtons[slot] != before;
                if (placed) GearXr.Pulse(h.Right, 0.5f, 0.04f);
                Log("VR input: " + what + (placed ? " placed on " : " refused on ") + where + " (slot " + slot + ")");
                return;
            }
            int packed = p.ActionButtons[slot];
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Empty:
                    Log("VR input: " + hand + " radial menu released - empty " + where);
                    return;
                case ActionKind.Spell:
                {
                    int spell = WowActionSlots.SpellOf(packed);
                    AbilityView v = VrRadialMenu.Find(p, spell);
                    // Iteration 7 (PLAN7 s5.6): immersive casting never holds a mount spell (summoning is not thrown) - a plain cast.
                    bool immersive = WowSettings.ImmersiveCasting && !(v != null && v.IsMount) && !MountCatalog.IsMount(spell);
                    Log("VR input: " + hand + " radial menu -> " + (v != null ? v.Name : "spell " + spell) + " (" + where + (immersive ? ", immersive" : "") + ")");
                    if (immersive) _imm.Begin(s, spell, h.Right, v);
                    else s.CastSpell(spell, 0);
                    GearXr.Pulse(h.Right, 0.5f, 0.04f);
                    return;
                }
                default:
                    Log("VR input: " + hand + " radial menu -> " + UiActions.NameOf(p, packed) + " (" + where + ")");
                    s.UseActionButton(slot, 0);
                    GearXr.Pulse(h.Right, 0.5f, 0.04f);
                    return;
            }
        }

        private string RowLabel(int row)
        {
            string n = _rows.NameOf(row);
            return n.Length > 0 ? n : "row " + row;
        }

        private static string Label(int packed, string name)
        {
            return packed == 0 ? "Clear a button" : string.IsNullOrEmpty(name) ? "an action" : name;
        }

        private static void Say(string text)
        {
            if (UiActionCursor.Say != null) UiActionCursor.Say(text, UiArt.InfoYellow);
        }

        private void Log(string line)
        {
            _casts++;
            if (_casts <= 30 || _casts % 50 == 0) WowLog.Info(line);
        }

        private static void UiArtPump()
        {
            Ui.UiArt.PumpIcons(2);
        }

        private void HideAll()
        {
            if (!_shown) return;
            _shown = false;
            for (int i = 0; i < 2; i++) { _hands[i].Open = false; _hands[i].Preview = false; if (_hands[i].Menu != null) _hands[i].Menu.Show(false); }
            if (_ray != null) _ray.Hide(_views);
            if (_imm != null) _imm.Cancel();
        }

        private void OnDisable()
        {
            HideAll();
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
            for (int i = 0; i < 2; i++) if (_hands[i].Menu != null) _hands[i].Menu.Destroy();
            if (_ray != null) _ray.Destroy();
            if (_imm != null) _imm.Destroy();
        }

        // ------------------------------------------------------------------ helpers

        private static Quaternion Local(Transform root, Quaternion world)
        {
            return root != null ? Quaternion.Inverse(root.rotation) * world : world;
        }

        private static Vector3 LocalPoint(Transform root, Vector3 world)
        {
            return root != null ? Quaternion.Inverse(root.rotation) * (world - root.position) : world;
        }

        private static Vector3 WorldPoint(Transform root, Vector3 local)
        {
            return root != null ? root.position + root.rotation * local : local;
        }

        /// <summary>
        /// The line the player aims along: the client's own laser (VrClientLaser: the far caster of the Somnium pointer on the
        /// right hand, from its stabilized origin). Until a client interactor is found: the grip's forward (said once after
        /// 20 s). False = the client's laser was there and is switched off now (no far targeting of our own choosing).
        /// </summary>
        private bool AimRay(Transform right, out Ray aim)
        {
            if (VrClientLaser.TryGetLaser(right, out aim)) return true;
            if (VrClientLaser.LaserOff) return false;
            if (!_loggedAim && Time.unscaledTime > 20f) { _loggedAim = true; WowLog.Info("VR input: target ray aims along the right hand's forward (no client interactor near the right hand yet)"); }
            aim = new Ray(right.position, right.forward);
            return true;
        }

        /// <summary>Right trigger as UiPointer reads it (analog > 0.5 OR triggerButton).</summary>
        private static bool Trigger()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!device.isValid) return false;
            float v;
            bool down;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out v) && v > 0.5f) return true;
            return device.TryGetFeatureValue(CommonUsages.triggerButton, out down) && down;
        }
    }
}
