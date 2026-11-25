using System;
using SomniumSpace.Worlds.SomniumWoW.Game.Gear;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.VrInput
{
    /// <summary>
    /// Iteration 11 (vrcast, proposal-vrcast.md 1.3 / 6.6) - IMMERSIVE STRIKE (VR only, Settings > ActionBars > "Immersive strike", off by
    /// default): with an attackable living enemy targeted, a full circle of the chosen bar's harmful spells (and Auto Attack, and items)
    /// stands upright in front of the chest, in front of you (it follows your facing - FollowStart; it no longer turns toward the target); HIT an icon with the weapon (any part of the blade capsule the
    /// swing-to-attack uses, WowGear.TryHeldBlade) or, with nothing drawn, the fist, faster than Settings > Swing speed, and that button is
    /// used on the target (IWowSession.UseActionButton). The hand must have slowed below half the swing speed since its last strike (the
    /// swing's own "armed" rule, GearLocal), the whole blade path between two frames is tested (SubSteps), one icon per swing, the same icon
    /// again only after SameIconGap, and after any strike no icon fires for SwingCooldown (the user's 0.3 s). A strike in the last QueueWindow of a cooldown / the GCD waits and fires when it ends (AzerothCore's
    /// spell queue window, worldserver.conf.dist:2280-2287 SpellQueue.Window = 400; the ring queues itself so offline feels the same).
    /// The hand whose radial menu is open never strikes (WowVrInput.MenuOpen). Every value below is ours unless named; logged once.
    /// Added by WowVrInput.Awake; runs after WowGear's LateUpdate (10000) placed the weapons.
    /// </summary>
    [DefaultExecutionOrder(10020)]
    public sealed class VrStrikeRing : MonoBehaviour
    {
        // ---- ours (proposal-vrcast 6.6), logged once at the first build
        public const float Down = 0.30f;            // m below the eye
        public const float IconSize = 0.10f;        // m square
        public const float RadiusSmall = 0.24f;     // m, up to 8 icons
        public const float RadiusLarge = 0.28f;     // m, 9-12 icons
        public const float HitRadius = 0.055f;      // m, the icon's hit sphere
        public const float FistRadius = 0.06f;      // m, the fist sphere with nothing drawn
        public const int SubSteps = 4;              // the blade path between two frames is tested at 1/4, 2/4, 3/4, 4/4
        public const float SameIconGap = 0.30f;     // s before the same icon can be struck again
        /// <summary>The user's value (2026-10-03: "I kept hitting the next spell when doing a full swing ... add a 0.3 second swing
        /// cooldown on the spells on that immersive strike wheel, so I can swing without triggering the spell opposite it"): after a
        /// strike fires, no icon on the wheel fires for this long (either hand).</summary>
        public const float SwingCooldown = 0.30f;   // s
        public const float QueueWindow = 0.40f;     // s (= AzerothCore SpellQueue.Window 400 ms)
        public const float QueueGiveUp = 0.60f;     // s a queued strike waits at most
        // Lead (2026-10-03, the user: "make the ring follow the player ... so it floats, if I move it stays in place same distance away
        // from me"): the ring floats in front of YOU at the set distance and height from your eyes every frame (walking carries it), no
        // longer turned toward the target. Turning with the thumbstick (snap or smooth: the rig turns) turns it with you at once; turning
        // your head or body in the room is a lazy follow - it starts turning when you face more than FollowStart away from it and turns
        // until it is within FollowStop again, so a glance aside while swinging does not move it. When it appears it starts straight in
        // front of you. Ours.
        public const float FollowStart = 25f;       // degrees
        public const float FollowStop = 3f;         // degrees
        public const float YawFollow = 180f;        // degrees / s the ring turns after you
        public const float FadeSeconds = 0.15f;
        public const float StrikeAmplitude = 0.8f, StrikeSeconds = 0.06f;
        public const float RefuseAmplitude = 0.15f, RefuseSeconds = 0.03f;
        public const float FlashSeconds = 0.15f, FlashScale = 1.25f, ShakeSeconds = 0.2f, ShakeMetres = 0.01f;
        public const float TeleportStep = 1.5f;     // m in one frame = a teleport, not a swing (GearLocal.cs TestBlade)

        private const int MaxIcons = 12;
        private const float UnitsPerMetre = 1000f;  // the canvas: 1 unit = 1 mm
        private const int AutoAttack = 6603;        // Spell.dbc "Auto Attack"

        private sealed class Icon
        {
            public RectTransform Rt;
            public RawImage Pic, Frame, Flash, Empty;
            public Image Sweep;
            public TextMeshProUGUI Cost;
            public readonly UiActionView View = new UiActionView();
            public int Slot = -1, Shown = int.MinValue, Tint = -1, CostShown = -1;
            public string Path;
            public bool Loaded;
            public float SweepShown = -1f, FlashUntil, ShakeUntil, LastHit = -10f;
            public Vector2 Home;
            public Vector3 World;
        }

        private sealed class HandTrack
        {
            public bool Has, Armed, Blade;
            public Vector3 PrevA, PrevB;   // root-local (walking and snap turns are not swings)
        }

        private GameObject _go;
        private RectTransform _root;
        private RectTransform _bandRt;
        private CanvasGroup _group;
        private readonly Icon[] _icons = new Icon[MaxIcons];
        private readonly int[] _slots = new int[MaxIcons];
        private readonly HandTrack[] _hands = { new HandTrack(), new HandTrack() };
        private float _swingLockUntil = -10f;   // SwingCooldown: no strike before this time
        private int _swingLockLogs;
        private int _n, _layoutSig = int.MinValue, _shownBar = -1;
        private float _radius;
        private Vector3 _dir;
        private bool _hasDir, _built, _broken, _visible, _loggedConsts, _following;
        private int _placedFrame = -10;   // the frame Place last ran (a gap = the ring was hidden: it starts in front of you again)
        private float _rigYaw;            // the camera rig's yaw last frame (the thumbstick turn carries the ring)
        private float _alpha;
        private int _errors, _hits, _ringLogs;
        // one pending (queued) strike
        private int _pendIcon = -1;
        private bool _pendRight;
        private float _pendAt, _pendSpeed;
        private ulong _pendTarget;

        private void LateUpdate()
        {
            if (_broken) return;
            try
            {
                Step();
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 5) WowLog.Warn("VR strike: " + e.GetType().Name + ": " + e.Message + (_errors == 5 ? " (strike ring off after 5 errors)" : ""));
                if (_errors >= 5) { _broken = true; Hide(true); }
            }
        }

        private void Step()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            IWowSession s = WowPlay.Session;
            PlayerState p = s != null ? s.Player : null;
            EntityState t = Wanted(s, p);
            Camera cam = t != null ? WowPlayer.Head() : null;
            Transform root = WowPlayer.Root;
            if (t == null || cam == null)
            {
                _pendIcon = -1;
                ResetHands();
                Fade(false, dt);
                return;
            }
            Build();
            int row = BarRow(WowSettings.StrikeRingBar);
            int page = WowActionSlots.RowPage(row, UiBars.MainPage);
            int n = 0, sig = page * 7919 + row;
            for (int i = 0; i < WowActionSlots.PerBar && n < MaxIcons; i++)
            {
                int slot = WowActionSlots.Slot(page, i);
                if (!Qualifies(p, p.ActionButtons[slot])) continue;
                _slots[n++] = slot;
                sig = sig * 31 + slot * 1031 + p.ActionButtons[slot];
            }
            if (n == 0)
            {
                _pendIcon = -1;
                ResetHands();
                Fade(false, dt);
                return;
            }
            if (sig != _layoutSig || n != _n || Spread() != _layoutSpread || IconScale() != _layoutScale) Layout(n, sig);
            if (!_visible || _shownBar != WowSettings.StrikeRingBar)
            {
                _shownBar = WowSettings.StrikeRingBar;
                _ringLogs++;
                if (_ringLogs <= 20 || _ringLogs % 50 == 0)
                    WowLog.Info("VR strike: ring on (" + _n + " icons from " + WowActionSlots.RowName(row) + ", distance " + Distance().ToString("0.00") + " m, radius "
                                + _radius.ToString("0.00") + " m) - target " + t.Name);
            }
            Fade(true, dt);
            Place(cam, t, dt);
            for (int i = 0; i < _n; i++) RefreshIcon(_icons[i], p, now);
            UiArt.PumpIcons(2);

            StepPending(s, p, t, now);
            Transform rootT = root;
            StepHand(s, p, t, false, WowPlayer.LeftHand, rootT, dt, now);
            StepHand(s, p, t, true, WowPlayer.RightHand, rootT, dt, now);
        }

        /// <summary>The target the ring is shown for, or null (every condition of proposal 6.6 "Shown when").</summary>
        private static EntityState Wanted(IWowSession s, PlayerState p)
        {
            if (!WowPlayer.XrPresent || !WowSettings.ImmersiveStrike || s == null || p == null) return null;
            if (p.Dead || p.Ghost || p.OnTaxi || p.Mounted || UiActionCursor.Active) return null;
            if (s.TargetGuid == 0) return null;
            EntityState t = s.Find(s.TargetGuid);
            if (t == null) return null;
            bool ok = WowEntityViews.AttackOnClick(s, t) || (t.Kind == EntityKind.Player && t.Reaction == Reaction.Hostile && !t.Dead);
            return ok ? t : null;
        }

        /// <summary>Settings StrikeRingBar (0 Action Bar, 1 Bottom Left, 2 Bottom Right, 3 Right, 4 Right 2) -> the VR row of that bar.</summary>
        public static int BarRow(int bar)
        {
            switch (bar)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return -1;
                case 4: return -2;
                default: return 0;
            }
        }

        /// <summary>
        /// A button the ring shows (proposal 1.3, U7): a harmful spell (AbilityView.Harmful, not passive, not a ground / item targeting spell -
        /// those want a place or an item, ours), Auto Attack, or an item.
        /// </summary>
        private static bool Qualifies(PlayerState p, int packed)
        {
            switch (WowActionSlots.KindOf(packed))
            {
                case ActionKind.Item: return true;
                case ActionKind.Spell:
                {
                    int id = WowActionSlots.SpellOf(packed);
                    if (id == AutoAttack) return true;
                    AbilityView v = UiActions.Ability(p, id);
                    return v != null && v.Harmful && !v.Passive && v.Targeting == SpellTargetingKind.None;
                }
                default: return false;
            }
        }

        private static float Distance()
        {
            return Mathf.Clamp(WowSettings.StrikeRingDistance, 0.25f, 1.20f);
        }

        // ------------------------------------------------------------------ build / layout / placement

        private void Build()
        {
            if (_built) return;
            _built = true;
            if (!_loggedConsts)
            {
                _loggedConsts = true;
                WowLog.Info("VR strike: ring constants (ours) - " + Down.ToString("0.00") + " m below the eye, icon " + IconSize.ToString("0.00") + " m, radius "
                            + RadiusSmall.ToString("0.00") + " / " + RadiusLarge.ToString("0.00") + " m, hit sphere " + HitRadius.ToString("0.000") + " m, fist "
                            + FistRadius.ToString("0.00") + " m, " + SubSteps + " sub-steps, same icon gap " + SameIconGap.ToString("0.00") + " s, swing cooldown " + SwingCooldown.ToString("0.00") + " s, queue "
                            + QueueWindow.ToString("0.00") + " s (AzerothCore SpellQueue.Window) / give up " + QueueGiveUp.ToString("0.00") + " s, follows your facing past "
                            + FollowStart.ToString("0") + " deg at " + YawFollow.ToString("0") + " deg/s, strike pulse " + StrikeAmplitude.ToString("0.00") + " / "
                            + StrikeSeconds.ToString("0.00") + " s, refuse " + RefuseAmplitude.ToString("0.00") + " / " + RefuseSeconds.ToString("0.00") + " s");
            }
            bool over = UiKit.Over;
            UiKit.Over = false;   // depth-tested: the weapon passing in front of an icon hides it
            try
            {
                float size = 2f * (RadiusLarge + IconSize) * UnitsPerMetre;
                _go = new GameObject("WoW VR strike ring", typeof(RectTransform));
                _go.layer = UiKit.Layer;
                var canvas = _go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 390;
                UiSharp.Register(_go);   // lead 2026-10-10
                var scaler = _go.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 3f;
                _group = _go.AddComponent<CanvasGroup>();
                _group.interactable = false;
                _group.blocksRaycasts = false;
                _group.alpha = 0f;
                _root = (RectTransform)_go.transform;
                _root.sizeDelta = new Vector2(size, size);
                _root.pivot = new Vector2(0.5f, 0.5f);
                _root.localScale = Vector3.one / UnitsPerMetre;

                RawImage band = UiKit.Pic(_root, "Band", VrArt.StrikeBand, 0f, 0f, size, size);
                band.uvRect = new Rect(0f, 0f, 1f, 1f);
                _bandRt = band.rectTransform;
                _bandRt.pivot = new Vector2(0.5f, 0.5f);
                _bandRt.anchoredPosition = new Vector2(size * 0.5f, -size * 0.5f);

                float px = IconSize * UnitsPerMetre;
                for (int i = 0; i < MaxIcons; i++)
                {
                    var ic = new Icon();
                    ic.Rt = UiKit.Rect(_root, "Strike " + (i + 1), 0f, 0f, px, px);
                    ic.Rt.pivot = new Vector2(0.5f, 0.5f);
                    ic.Empty = UiKit.Solid(ic.Rt, "Back", new Color(0f, 0f, 0f, 0.45f), 0f, 0f, px, px);
                    ic.Pic = UiKit.Pic(ic.Rt, "Icon", null, 0f, 0f, px, px);
                    ic.Pic.enabled = false;
                    var sweepRt = UiKit.Rect(ic.Rt, "Cooldown", 0f, 0f, px, px);
                    ic.Sweep = sweepRt.gameObject.AddComponent<Image>();
                    ic.Sweep.sprite = UiArt.WhiteSprite;
                    ic.Sweep.type = Image.Type.Filled;
                    ic.Sweep.fillMethod = Image.FillMethod.Radial360;
                    ic.Sweep.fillOrigin = (int)Image.Origin360.Top;
                    ic.Sweep.fillClockwise = false;
                    ic.Sweep.color = new Color(0f, 0f, 0f, 0.7f);
                    ic.Sweep.raycastTarget = false;
                    ic.Sweep.enabled = false;
                    float f = px * 66f / 36f;   // UI-Quickslot2 around a 36 px button (ActionButtonTemplate)
                    ic.Frame = UiKit.Pic(ic.Rt, "Normal", UiArt.Tex(UiArt.Quickslot), (px - f) * 0.5f, (px - f) * 0.5f + 1f, f, f);
                    ic.Flash = UiKit.Pic(ic.Rt, "Flash", UiArt.Add(UiArt.ButtonHilightSquare), 0f, 0f, px, px);
                    ic.Flash.enabled = false;
                    ic.Cost = UiKit.Text(ic.Rt, "Cost", "", 30f, Color.white, TextAlignmentOptions.BottomRight, 0f, px - 34f, px - 4f, 32f, UiArt.Numbers);
                    ic.Rt.gameObject.SetActive(false);
                    _icons[i] = ic;
                }
            }
            finally
            {
                UiKit.Over = over;
            }
            _go.SetActive(false);
        }

        /// <summary>n icons in bar order, clockwise from 12 o'clock, angle step 360 / n.</summary>
        // Lead (2026-10-03, the user: "add the ability to change size and spread of the immersive strike mode"): Settings > Bars
        // "Strike ring icon size" (x0.5 .. 2, the hit sphere scales with it) and "Strike ring spread" (the ring radius x0.5 .. 2).
        private float _layoutSpread = -1f, _layoutScale = 1f;   // spread -1 forces the first layout; scale 1 until then
        private static float Spread() { return Mathf.Clamp(WowSettings.StrikeRingSpread, 0.5f, 2f); }
        private static float IconScale() { return Mathf.Clamp(WowSettings.StrikeRingSize, 0.5f, 2f); }

        private void Layout(int n, int sig)
        {
            _n = n;
            _layoutSig = sig;
            _pendIcon = -1;
            _layoutSpread = Spread();
            _layoutScale = IconScale();
            _radius = (n <= 8 ? RadiusSmall : RadiusLarge) * _layoutSpread;
            float size = _root.sizeDelta.x, r = _radius * UnitsPerMetre;
            float bandSize = 2f * r / 0.90f;   // the band's middle (0.9 of its radius) runs under the icon centres
            _bandRt.sizeDelta = new Vector2(bandSize, bandSize);
            for (int i = 0; i < MaxIcons; i++)
            {
                Icon ic = _icons[i];
                bool on = i < n;
                if (ic.Rt.gameObject.activeSelf != on) ic.Rt.gameObject.SetActive(on);
                if (!on) { ic.Slot = -1; continue; }
                if (ic.Slot != _slots[i]) { ic.Slot = _slots[i]; ic.LastHit = -10f; }
                float a = 2f * Mathf.PI * i / n;
                ic.Home = new Vector2(size * 0.5f + Mathf.Sin(a) * r, -(size * 0.5f - Mathf.Cos(a) * r));
                ic.Rt.anchoredPosition = ic.Home;
            }
        }

        private void Place(Camera cam, EntityState t, float dt)
        {
            Transform ct = cam.transform;
            Vector3 eye = ct.position;
            // your level facing: the head's forward, and while looking steeply down (or up) the top of the head, which then points ahead
            Vector3 f = ct.forward, u = ct.up;
            Vector3 want = new Vector3(f.x, 0f, f.z) + new Vector3(u.x, 0f, u.z) * -f.y;
            if (want.sqrMagnitude < 1e-6f) want = _hasDir ? _dir : Vector3.forward;
            want.Normalize();
            Transform rig = ct.parent;   // the headset's rig: turns with the thumbstick, not with your head
            float rigYaw = _rigYaw;
            if (rig != null)
            {
                Vector3 rf = rig.forward;
                rf.y = 0f;
                if (rf.sqrMagnitude > 1e-6f) rigYaw = Mathf.Atan2(rf.x, rf.z) * Mathf.Rad2Deg;
            }
            bool fresh = !_hasDir || Time.frameCount - _placedFrame > 1;
            _placedFrame = Time.frameCount;
            if (fresh) { _dir = want; _hasDir = true; _following = false; }
            else
            {
                float turned = Mathf.DeltaAngle(_rigYaw, rigYaw);
                if (Mathf.Abs(turned) > 0.01f) _dir = Quaternion.AngleAxis(turned, Vector3.up) * _dir;   // the thumbstick turn carries the ring
                if (Vector3.Angle(_dir, want) > FollowStart) _following = true;
                if (_following)
                {
                    _dir = Vector3.RotateTowards(_dir, want, YawFollow * Mathf.Deg2Rad * dt, 0f);
                    if (Vector3.Angle(_dir, want) < FollowStop) _following = false;
                }
            }
            _rigYaw = rigYaw;
            Vector3 centre = eye - Vector3.up * (Down - Mathf.Clamp(WowSettings.StrikeRingHeight, -0.50f, 0.30f)) + _dir * Distance();   // lead 2026-10-03: the height setting
            Vector3 face = centre - eye;
            face.y = 0f;
            if (face.sqrMagnitude < 1e-6f) face = _dir;
            Quaternion rot = Quaternion.LookRotation(face.normalized, Vector3.up);
            _root.SetPositionAndRotation(centre, rot);
            for (int i = 0; i < _n; i++)
            {
                float a = 2f * Mathf.PI * i / _n;
                _icons[i].World = centre + rot * new Vector3(Mathf.Sin(a) * _radius, Mathf.Cos(a) * _radius, 0f);
            }
        }

        private void RefreshIcon(Icon ic, PlayerState p, float now)
        {
            int packed = WowActionSlots.Valid(ic.Slot) ? p.ActionButtons[ic.Slot] : 0;
            UiActionView v = ic.View;
            UiActions.Resolve(p, packed, v);
            if (packed != ic.Shown) { ic.Shown = packed; ic.Path = null; ic.Loaded = false; ic.Pic.enabled = false; ic.CostShown = -1; }
            string path = v.Has ? v.IconPath : null;
            if (path != ic.Path) { ic.Path = path; ic.Loaded = false; ic.Pic.enabled = false; }
            if (!ic.Loaded && v.Has)
            {
                Texture2D tex = UiArt.Icon(path);
                if (tex != null) { ic.Pic.texture = tex; ic.Pic.enabled = true; ic.Loaded = true; ic.Tint = -1; }
            }
            if (v.Tint != ic.Tint && ic.Loaded) { ic.Tint = v.Tint; ic.Pic.color = UiActions.TintColor(v.Tint); }
            float frac = v.Cooldown;
            if (Mathf.Abs(frac - ic.SweepShown) > 0.004f) { ic.SweepShown = frac; ic.Sweep.enabled = frac > 0f; ic.Sweep.fillAmount = frac; }
            int cost = v.Kind == ActionKind.Item ? (v.ShowCount ? v.Count : 0)
                     : v.Ability != null && v.Ability.CostType != PowerType.None ? v.Ability.Cost : 0;
            if (cost != ic.CostShown)
            {
                ic.CostShown = cost;
                if (cost > 0) ic.Cost.SetText("{0}", cost); else ic.Cost.SetText("");
                ic.Cost.color = v.Kind == ActionKind.Item || v.Ability == null ? Color.white : CostColor(v.Ability.CostType);
            }
            bool flash = now < ic.FlashUntil;
            if (ic.Flash.enabled != flash) ic.Flash.enabled = flash;
            float scale = (flash ? FlashScale : 1f) * _layoutScale;   // lead 2026-10-03: the icon size setting
            if (Mathf.Abs(ic.Rt.localScale.x - scale) > 1e-3f) ic.Rt.localScale = Vector3.one * scale;
            if (now < ic.ShakeUntil)
            {
                float k = ShakeMetres * UnitsPerMetre;
                ic.Rt.anchoredPosition = ic.Home + new Vector2(UnityEngine.Random.Range(-k, k), UnityEngine.Random.Range(-k, k));
            }
            else if (ic.Rt.anchoredPosition != ic.Home) ic.Rt.anchoredPosition = ic.Home;
        }

        private void Fade(bool on, float dt)
        {
            if (_go == null) { _visible = false; return; }
            float step = FadeSeconds > 0f ? dt / FadeSeconds : 1f;
            _alpha = Mathf.MoveTowards(_alpha, on ? 1f : 0f, step);
            if (on && !_go.activeSelf) _go.SetActive(true);
            _group.alpha = _alpha;
            _visible = on;
            if (!on && _alpha <= 0f && _go.activeSelf) _go.SetActive(false);
        }

        private void Hide(bool now)
        {
            _visible = false;
            _pendIcon = -1;
            ResetHands();
            if (_go == null) return;
            if (now) { _alpha = 0f; _group.alpha = 0f; _go.SetActive(false); }
        }

        private void ResetHands()
        {
            _hands[0].Has = false;
            _hands[1].Has = false;
        }

        // ------------------------------------------------------------------ strikes

        private void StepHand(IWowSession s, PlayerState p, EntityState t, bool right, Transform hand, Transform root, float dt, float now)
        {
            HandTrack h = _hands[right ? 1 : 0];
            if (hand == null || dt <= 1e-5f || WowVrInput.MenuOpen(right) || WowGear.HandOnBow(right)) { h.Has = false; return; }   // lead 2026-10-10: not the bow / arrow hand
            Vector3 a, b;
            float r;
            bool blade = WowGear.TryHeldBlade(right, out a, out b, out r);
            if (!blade) { a = b = hand.position; r = FistRadius; }
            Vector3 aL = ToLocal(root, a), bL = ToLocal(root, b);
            if (!h.Has || h.Blade != blade)
            {
                // first frame, or the weapon was drawn / sheathed: no path to sweep yet
                h.Has = true;
                h.Blade = blade;
                h.PrevA = aL;
                h.PrevB = bL;
                h.Armed = false;
                return;
            }
            float step = (bL - h.PrevB).magnitude;
            float speed = step > TeleportStep ? 0f : step / dt;
            float swing = Mathf.Max(0.1f, WowSettings.SwingSpeed);
            if (speed < swing * 0.5f) h.Armed = true;
            int hit = -1;
            if (h.Armed && speed >= swing) hit = Sweep(root, h.PrevA, h.PrevB, aL, bL, r);
            h.PrevA = aL;
            h.PrevB = bL;
            if (hit < 0) return;
            h.Armed = false;
            Icon ic = _icons[hit];
            if (now < _swingLockUntil)
            {
                // the same swing running on through the wheel (e.g. into the icon opposite): ignored (SwingCooldown)
                if (_swingLockLogs++ < 10)
                    WowLog.Info("VR strike: " + (right ? "right" : "left") + " hand passed " + (ic.View.Has ? ic.View.Name : "a button") + " " + (SwingCooldown - (_swingLockUntil - now)).ToString("0.00")
                                + " s after a strike - ignored (swing cooldown " + SwingCooldown.ToString("0.00") + " s).");
                return;
            }
            if (now - ic.LastHit < SameIconGap) return;
            ic.LastHit = now;
            _swingLockUntil = now + SwingCooldown;
            Strike(s, p, t, hit, right, speed, blade, now);
        }

        /// <summary>The icon (index) the blade's path between two frames meets first: SubSteps segments lerped from the previous to the
        /// current blade (root-local), each tested against every icon's sphere (HitRadius + the blade's radius); nearest icon of the first
        /// sub-step that meets any. -1 = none.</summary>
        private int Sweep(Transform root, Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float r)
        {
            for (int k = 1; k <= SubSteps; k++)
            {
                float u = (float)k / SubSteps;
                Vector3 a = Vector3.Lerp(a0, a1, u), b = Vector3.Lerp(b0, b1, u);
                int best = -1;
                float bestD = HitRadius * _layoutScale + r;   // lead 2026-10-03: the hit sphere grows with the icon size setting
                for (int i = 0; i < _n; i++)
                {
                    float d = PointSegment(ToLocal(root, _icons[i].World), a, b);
                    if (d <= bestD) { bestD = d; best = i; }
                }
                if (best >= 0) return best;
            }
            return -1;
        }

        private void Strike(IWowSession s, PlayerState p, EntityState t, int index, bool right, float speed, bool blade, float now)
        {
            Icon ic = _icons[index];
            UiActionView v = ic.View;
            AbilityView ab = v.Kind == ActionKind.Spell ? v.Ability : null;
            float cd = ab != null ? ab.CooldownRemaining : 0f;
            ic.FlashUntil = now + FlashSeconds;
            WowGear.NoteCastHand(right);
            string what = v.Has ? v.Name : "button";
            bool queued = cd > 0.02f && cd <= QueueWindow;
            bool usable = queued || (ab != null ? ab.Usable : v.Tint == 1 || v.Tint == 0);
            _hits++;
            if (_hits <= 20 || _hits % 50 == 0)
                WowLog.Info("VR strike: " + (right ? "right " : "left ") + (blade ? "blade " : "fist ") + speed.ToString("0.0") + " m/s hit " + what + " ("
                            + WowActionSlots.BarName(ic.Slot) + " button " + WowActionSlots.ButtonOf(ic.Slot) + ") on " + t.Name
                            + (queued ? " - queued, " + cd.ToString("0.00") + " s of cooldown left" : usable ? "" : " - " + (ab != null && ab.UnusableReason != null ? ab.UnusableReason : "not usable now")));
            if (queued)
            {
                _pendIcon = index;
                _pendRight = right;
                _pendAt = now;
                _pendSpeed = speed;
                _pendTarget = s.TargetGuid;
            }
            else
            {
                _pendIcon = -1;
                s.UseActionButton(ic.Slot, s.TargetGuid);   // the session gives WoW's own red text when it cannot
            }
            if (usable) GearXr.Pulse(right, StrikeAmplitude, StrikeSeconds);
            else
            {
                GearXr.Pulse(right, RefuseAmplitude, RefuseSeconds);
                ic.ShakeUntil = now + ShakeSeconds;
            }
        }

        /// <summary>A queued strike fires when its cooldown / the GCD ends (<= 0.02 s), or is dropped after QueueGiveUp / a target change.</summary>
        private void StepPending(IWowSession s, PlayerState p, EntityState t, float now)
        {
            if (_pendIcon < 0) return;
            if (_pendIcon >= _n || s.TargetGuid != _pendTarget)
            {
                _pendIcon = -1;
                return;
            }
            Icon ic = _icons[_pendIcon];
            AbilityView ab = ic.View.Kind == ActionKind.Spell ? ic.View.Ability : null;
            float cd = ab != null ? ab.CooldownRemaining : 0f;
            if (cd <= 0.02f)
            {
                _pendIcon = -1;
                if (_hits <= 20) WowLog.Info("VR strike: queued " + (ab != null ? ab.Name : "action") + " fired after " + (now - _pendAt).ToString("0.00") + " s");
                s.UseActionButton(ic.Slot, s.TargetGuid);
                return;
            }
            if (now - _pendAt > QueueGiveUp)
            {
                _pendIcon = -1;
                if (_hits <= 20) WowLog.Info("VR strike: queued " + (ab != null ? ab.Name : "action") + " dropped - still " + cd.ToString("0.00") + " s of cooldown after "
                                             + QueueGiveUp.ToString("0.00") + " s (" + (_pendRight ? "right" : "left") + " hand, " + _pendSpeed.ToString("0.0") + " m/s)");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 ToLocal(Transform root, Vector3 world)
        {
            return root != null ? Quaternion.Inverse(root.rotation) * (world - root.position) : world;
        }

        private static float PointSegment(Vector3 c, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float u = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2) : 0f;
            return (c - (a + ab * u)).magnitude;
        }

        private static Color CostColor(PowerType t)
        {
            switch (t)
            {
                case PowerType.Mana: return new Color(0.45f, 0.65f, 1f);
                case PowerType.Rage: return new Color(1f, 0.35f, 0.35f);
                case PowerType.Energy: return new Color(1f, 1f, 0.3f);
                case PowerType.Focus: return new Color(1f, 0.6f, 0.35f);
                default: return Color.white;
            }
        }

        private void OnDisable()
        {
            Hide(true);
        }

        private void OnDestroy()
        {
            if (_go != null) Destroy(_go);
        }
    }
}
