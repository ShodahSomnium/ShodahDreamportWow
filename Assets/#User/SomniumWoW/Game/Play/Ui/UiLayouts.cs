using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    /// <summary>
    /// The same widgets arranged for one way of playing: UiDesktopLayout (one screen-space overlay canvas, WoW's own positions) or
    /// UiVrLayout (world canvases: the left forearm, a window standing up from it, the NPC dialog in the world, the death popup).
    /// Only the layout in use is built and refreshed.
    /// Iteration 11 (uicore, proposal-uicore.md 5.4.4): the registry's windows (UiRegistry, Build specs) are built here - the desktop
    /// gives each window its own holder that WowArmUi's panel manager (WoW's UIPanelWindows rules) moves; VR puts toggled windows on the
    /// lid and session windows on the dialog surface next to the NPC. Every window root gets a click blocker (UiKit.Blocker). Hosted
    /// here: vrcast's four extra bars + stance bar (desktop) and their managed offsets (UiBars), the micro menu (UiMicroMenu), quests'
    /// objective tracker, the achievement toast.
    /// </summary>
    public abstract class UiLayout
    {
        public UiContext Ctx;
        public GameObject Go;
        public readonly List<UiSurface> Surfaces = new List<UiSurface>();
        public UiPlayerFrame Player;
        public UiTargetFrame Target;
        public UiCastBar Cast;
        public UiMirrorTimers Mirror;        // lead 2026-10-26: breath / fatigue (MirrorTimer.xml)
        public UiXpBar Xp;
        public UiActionBar Bar;
        public UiMicroBar Micro;             // the VR forearm panel's micro buttons (null on the desktop since iteration 11)
        public UiMicroMenu MicroMenu;        // iteration 11 (uicore 5.4.5): WoW's micro buttons on the desktop
        public UiMessages Messages;
        public UiBanner Banner;
        public UiSpellbook Spellbook;
        public UiQuestLog QuestLog;
        public UiCharacter Character;
        public UiBags Bags;
        public UiQuestDialog Quest;
        public UiLoot Loot;
        public UiDeath Death;
        public UiSettingsWindow Settings;
        public UiMounts Mounts;              // iteration 7 (PLAN7 s5.6): WoW's Mounts pane
        public UiPetFrame Pet;
        public UiPetBar PetBar;
        public UiTotemBar Totems;
        public UiTargetCastBar TargetCast;   // iteration 4 (null where the layout has none)
        public UiParty Party;                // iteration 4: WowParty.Members (null where the layout has none)
        public UiMultiBars MultiBars;        // iteration 11 (vrcast hook 8): desktop only (null in VR)
        public UiStanceBar Stance;           // iteration 11 (vrcast hook 8): desktop only (null in VR)
        public UiQuestTracker Tracker;       // iteration 11 (quests H-U1)
        public UiAchievementAlert AchievementAlert;   // iteration 11 (talents / character): the achievement toast
        /// <summary>Iteration 11: the registry's Build windows of this layout (built when the layout is, or when first opened).</summary>
        public readonly Dictionary<UiWindowKind, UiWindow> Registered = new Dictionary<UiWindowKind, UiWindow>();
        private readonly HashSet<UiWindowKind> _buildFailed = new HashSet<UiWindowKind>();
        private readonly HashSet<UiWindowKind> _refreshFailed = new HashSet<UiWindowKind>();
        protected bool PetShown, TotemsShown;

        public abstract void Build(Transform parent);

        public void SetShown(bool on)
        {
            if (Go != null && Go.activeSelf != on) Go.SetActive(on);
            if (!on) for (int i = 0; i < Surfaces.Count; i++) if (Surfaces[i] != null) Surfaces[i].ClearHover();
        }

        public UiWindow WindowOf(UiWindowKind k)
        {
            switch (k)
            {
                case UiWindowKind.Spellbook: return Spellbook;
                case UiWindowKind.QuestLog: return QuestLog;
                case UiWindowKind.Character: return Character;
                case UiWindowKind.Bags: return Bags;
                case UiWindowKind.Settings: return Settings;
                case UiWindowKind.Mounts: return Mounts;
                case UiWindowKind.QuestDialog: case UiWindowKind.Loot: return null;   // driven by the session in Refresh
                default:
                    {
                        UiWindow w;
                        return Registered.TryGetValue(k, out w) ? w : null;
                    }
            }
        }

        /// <summary>Opens exactly the windows the controller says are open (a registered window is built the first time it opens).</summary>
        public virtual void ApplyWindows(bool[] open)
        {
            for (int k = 1; k < open.Length; k++)
            {
                UiWindowKind kind = (UiWindowKind)k;
                UiWindow w = WindowOf(kind);
                if (w == null && open[k]) w = EnsureBuilt(kind);
                if (w != null) w.SetOpen(open[k]);
            }
        }

        // ---- the registry's Build windows

        /// <summary>The layout's parent for a registered window (desktop: a holder; VR: the lid or the dialog surface), with UiKit's build
        /// state set for that surface; Restore puts it back.</summary>
        protected abstract Transform RegisteredParent(UiWindowSpec spec, out UiSurface surface);
        protected virtual void AfterRegisteredBuild(UiWindowSpec spec, UiWindow w, UiSurface surface) { }

        /// <summary>Builds every registered window that is not Hidden (at layout build time).</summary>
        protected void BuildRegistered()
        {
            List<UiWindowSpec> all = UiRegistry.All;
            int built = 0;
            for (int i = 0; i < all.Count; i++)
            {
                UiWindowSpec s = all[i];
                if (s.Build == null || s.Hidden || Registered.ContainsKey(s.Kind)) continue;
                if (BuildOne(s, false) != null) built++;
            }
            WowLog.Info("UI: " + built + " registered windows built for the " + (this is UiVrLayout ? "VR" : "desktop") + " layout; " + _buildFailed.Count + " failed.");
        }

        protected UiWindow EnsureBuilt(UiWindowKind k)
        {
            UiWindow w;
            if (Registered.TryGetValue(k, out w)) return w;
            if (_buildFailed.Contains(k)) return null;
            UiWindowSpec s = UiRegistry.Of(k);
            if (s == null || s.Build == null || s.Hidden) return null;
            return BuildOne(s, true);
        }

        private UiWindow BuildOne(UiWindowSpec s, bool late)
        {
            bool over = UiKit.Over, pointable = UiKit.Pointable;
            UiSurface surfaceBefore = UiKit.Surface;
            try
            {
                UiSurface surface;
                Transform parent = RegisteredParent(s, out surface);
                UiWindowKind k = s.Kind;
                UiWindow w = s.Build(parent, Ctx, () => { if (Ctx.Toggle != null) Ctx.Toggle(k); });
                if (w == null || w.Root == null) throw new InvalidOperationException("Build returned no window");
                if (w.Root.Find("Blocker") == null) UiKit.Blocker(w.Root);
                w.Root.gameObject.SetActive(false);
                Registered[k] = w;
                AfterRegisteredBuild(s, w, surface);
                if (late && surface != null) surface.KeepOnTop();
                return w;
            }
            catch (Exception e)
            {
                _buildFailed.Add(s.Kind);
                WowLog.Error("UI: window " + s.Kind + " (" + s.Frame + ", owner " + s.Owner + ") failed to build: " + e);
                return null;
            }
            finally
            {
                UiKit.Over = over;
                UiKit.Pointable = pointable;
                UiKit.Surface = surfaceBefore;
            }
        }

        /// <summary>Desktop: WoW's panel manager put window k's TOPLEFT at (x, y) (UIParent units, y down negative). VR: nothing.</summary>
        public virtual void PlaceWindow(UiWindowKind k, float x, float y) { }

        /// <summary>Desktop: the canvas width in UIParent units (UIParent:GetRight()); 0 in VR.</summary>
        public virtual float ScreenWidth { get { return 0f; } }

        protected bool IsOpenK(UiWindowKind k) { return Ctx.IsOpen == null || Ctx.IsOpen(k); }

        public virtual void Refresh(float now)
        {
            IWowSession s = Ctx.S;
            PlayerState p = s.Player;
            Player.Refresh(p);
            Target.Refresh(s, p.Class == WowClass.Rogue || p.Class == WowClass.Druid);
            Cast.Refresh(Ctx, now);
            if (Mirror != null) Mirror.Refresh(p);
            Xp.Refresh(p);
            Bar.Refresh(p, now);
            if (Micro != null) Micro.Refresh();
            if (MicroMenu != null) MicroMenu.Refresh();
            Messages.Refresh(now);
            Banner.Refresh(now);
            if (Spellbook.IsOpen) Spellbook.Refresh(now);
            if (QuestLog.IsOpen) QuestLog.Refresh(now);
            if (Character.IsOpen) Character.Refresh(now);
            if (Bags.IsOpen) Bags.Refresh(now);
            if (Settings.IsOpen) Settings.Refresh(now);
            if (Mounts != null && Mounts.IsOpen) Mounts.Refresh(now);
            foreach (KeyValuePair<UiWindowKind, UiWindow> kv in Registered)
            {
                if (!kv.Value.IsOpen) continue;
                try { kv.Value.Refresh(now); }
                catch (Exception e)
                {
                    if (_refreshFailed.Add(kv.Key)) WowLog.Error("UI: window " + kv.Key + " failed to refresh (logged once): " + e);
                }
            }
            PetShown = Pet.Refresh(s);
            PetBar.Refresh(s);
            TotemsShown = Totems.Refresh(s);
            if (TargetCast != null) TargetCast.Refresh(s, now);
            if (Party != null) Party.Refresh(s);

            // The quest / gossip frame and the loot window are session windows of the registry (QuestDialog 52 / Loot 53): the session
            // wants them, WowArmUi's manager let them open (iteration 11).
            bool dialog = s.CurrentDialog != null && IsOpenK(UiWindowKind.QuestDialog);
            Quest.SetOpen(dialog);
            if (dialog) Quest.Refresh(now);
            bool loot = s.CurrentLoot != null && s.CurrentDialog == null && IsOpenK(UiWindowKind.Loot);
            Loot.SetOpen(loot);
            if (loot) Loot.Refresh(now);
            Death.SetOpen(UiDeath.WantsOpen(s));   // stage O2: open for a real prompt only (lying dead, Resurrect at the corpse, the spirit healer)
            if (Tracker != null)
            {
                try { Tracker.Refresh(now); }
                catch (Exception e) { if (_refreshFailed.Add(UiWindowKind.None)) WowLog.Error("UI: the quest tracker failed to refresh (logged once): " + e); }
            }
            for (int i = 0; i < Surfaces.Count; i++) if (Surfaces[i] != null) Surfaces[i].Tick();
        }

        public bool AnyWindowOpen
        {
            get
            {
                if (Spellbook.IsOpen || QuestLog.IsOpen || Character.IsOpen || Bags.IsOpen || Settings.IsOpen || (Mounts != null && Mounts.IsOpen)
                    || Quest.IsOpen || Loot.IsOpen || Death.IsOpen) return true;
                foreach (KeyValuePair<UiWindowKind, UiWindow> kv in Registered) if (kv.Value.IsOpen) return true;
                if (Ctx.IsOpen != null)
                {
                    // External windows (their own surfaces) count as open windows too.
                    List<UiWindowSpec> all = UiRegistry.All;
                    for (int i = 0; i < all.Count; i++) if (all[i].ExtIsOpen != null && Ctx.IsOpen(all[i].Kind)) return true;
                }
                return UiPopup.Count > 0 || UiPickup.Holding || UiStackSplit.IsOpen;
            }
        }

        public virtual void OnEvent(GameEvent e, float now)
        {
            Cast.OnEvent(e, now);
            if (TargetCast != null) TargetCast.OnEvent(e, now);
            if (Tracker != null) Tracker.OnEvent(e);
            if (AchievementAlert != null && e.Type == GameEventType.AchievementEarned) AchievementAlert.OnEvent(e);
        }

        /// <summary>VR: follow the arm / place world windows (called in LateUpdate and just before rendering). Desktop: nothing.</summary>
        public virtual void Place(bool beforeRender) { }

        /// <summary>A sub-canvas for often-changing parts (cooldown sweeps, cast bar), so their redraw does not rebuild the whole panel.</summary>
        protected static RectTransform SubCanvas(Transform parent, string name, float x, float y, float w, float h)
        {
            RectTransform r = UiKit.Rect(parent, name, x, y, w, h);
            r.gameObject.AddComponent<Canvas>();
            return r;
        }

        /// <summary>A window root's click blocker (proposal 5.1.2), unless the window made its own.</summary>
        protected static void Block(UiWindow w)
        {
            if (w != null && w.Root != null && w.Root.Find("Blocker") == null) UiKit.Blocker(w.Root);
        }

        /// <summary>Builds an optional hosted widget of another owner: a failure is logged once and the rest of the UI lives.</summary>
        protected static T Host<T>(string what, Func<T> make) where T : class
        {
            try { return make(); }
            catch (Exception e)
            {
                WowLog.Error("UI: " + what + " failed to build: " + e);
                return null;
            }
        }
    }

    // =====================================================================================================================
    /// <summary>
    /// Desktop: one ScreenSpaceOverlay canvas scaled to WoW's 768-unit-tall UIParent, everything where FrameXML puts it: player frame
    /// TOPLEFT (-19, -4), target frame (250, -4), UIErrorsFrame TOP (0, -122), MainMenuBar at the bottom (1024x53, XP bar, the four
    /// art slices, the gryphon end caps at +-544, action buttons from (8, 4), micro buttons from (552, 2), the bag bar BOTTOMRIGHT (-6, 2)),
    /// panels at TOPLEFT (x, -104) where WoW's panel manager puts them (iteration 11), the backpack above the bar at the right
    /// (CONTAINER_OFFSET_Y 70 + the bars' offsets), StaticPopup at TOP.
    /// </summary>
    public sealed class UiDesktopLayout : UiLayout
    {
        private UiSurface _s;
        private RectTransform _castAnchor, _bagsAnchor, _petBarAnchor, _objectivesAnchor;
        private readonly Dictionary<UiWindowKind, RectTransform> _holders = new Dictionary<UiWindowKind, RectTransform>();
        private bool _loggedOffsets;

        /// <summary>A window's holder: TOPLEFT (0, -104) for UIParent panels (the manager moves it), CENTER for centre windows.</summary>
        private RectTransform Holder(UiWindowKind k, UiPanelArea area, float w, float h)
        {
            RectTransform r = area == UiPanelArea.Center
                ? _s.Anchor(k + " (centre)", new Vector2(0.5f, 0.5f), w, h, 0f, 0f)
                : _s.Anchor(k.ToString(), new Vector2(0f, 1f), w, h, 0f, -104f);
            _holders[k] = r;
            return r;
        }

        public override void PlaceWindow(UiWindowKind k, float x, float y)
        {
            RectTransform r;
            if (_holders.TryGetValue(k, out r) && r != null && r.anchorMin.x == 0f) r.anchoredPosition = new Vector2(x, y);
        }

        public override float ScreenWidth
        {
            get { return _s != null && _s.Root != null ? _s.Root.rect.width : 0f; }
        }

        protected override Transform RegisteredParent(UiWindowSpec spec, out UiSurface surface)
        {
            surface = _s;
            UiKit.Over = false;
            UiKit.Pointable = true;
            UiKit.Surface = _s;
            return Holder(spec.Kind, spec.Area, spec.Width, spec.Height);
        }

        public override void Build(Transform parent)
        {
            UiSurface s = UiSurface.MakeScreen("WoW HUD (desktop)", parent, 5);
            _s = s;
            Go = s.Canvas.gameObject;
            Surfaces.Add(s);
            UiKit.Over = false;
            UiKit.Pointable = true;
            UiKit.Surface = s;
            try
            {
                RectTransform topLeft = s.Anchor("Top left", new Vector2(0f, 1f), 520f, 140f, 0f, 0f);
                Player = new UiPlayerFrame(topLeft, -19f, 4f, true);
                // Iteration 7: BuffFrame's right-click cancels a buff (the mount aura = dismount).
                Player.SetBuffCancel(id => { if (Ctx.S != null) Ctx.S.CancelAura(id); });
                Target = new UiTargetFrame(topLeft, 250f, 4f);
                TargetCast = new UiTargetCastBar(Target.Root);
                // PartyMemberFrame1 TOPLEFT (10, -160) of UIParent; each next one at the previous one's pet frame BOTTOMLEFT (-23, -10)
                // (PartyFrame.xml), the pet frame being at (20, -47) and 26 tall (PartyFrameTemplates.xml): 83 lower, 3 further left.
                Party = new UiParty(topLeft, 10f, 160f, -3f, 83f);
                try { UiRunes.AttachDesktop(Player.Root); }   // spellfx: the death knight's runes under the player frame
                catch (Exception e) { WowLog.Error("UI: rune frame failed to attach: " + e); }

                RectTransform bottom = s.Anchor("Main menu bar", new Vector2(0.5f, 0f), 1024f, 53f, 0f, 0f);
                UiKit.Blocker(bottom);   // iteration 11 (uicore 5.1.2): a click on the bar's art does not reach the world
                Texture2D art = UiArt.Tex(UiArt.MainMenuBar);
                // MainMenuBarTexture0..3 TexCoords (MainMenuBar.xml:248, 261, 274, 287).
                float[] top = { 0.83203125f, 0.58203125f, 0.33203125f, 0.08203125f };
                for (int i = 0; i < 4; i++) UiKit.Pic(bottom, "Bar art " + i, art, 0f, 1f, top[i], top[i] + 0.16796875f, i * 256f, 10f, 256f, 43f);
                Xp = new UiXpBar(bottom, 0f, 0f, 1024f);
                Texture2D cap = UiArt.Tex(UiArt.EndCap);
                UiKit.Pic(bottom, "End cap left", cap, 0f, 1f, 0f, 1f, 512f - 544f - 64f, 53f - 128f, 128f, 128f);
                UiKit.Pic(bottom, "End cap right", cap, 1f, 0f, 0f, 1f, 512f + 544f - 64f, 53f - 128f, 128f, 128f);
                RectTransform buttons = SubCanvas(bottom, "Action buttons", 0f, 0f, 1024f, 53f);
                Bar = new UiActionBar(buttons, Ctx, 8f, 53f - 4f - 36f, true);
                _mainButtons = buttons;   // integrator (pets H7d): hidden while the vehicle bar shows
                // Iteration 11 (uicore 5.4.5): WoW's 10 micro buttons + the World Map button + npc's bag bar, from the registry.
                MicroMenu = new UiMicroMenu(bottom, Ctx);
                // Iteration 11 (vrcast hook 8): the four extra bars and the stance bar (their own anchors).
                MultiBars = Host("the extra action bars (vrcast)", () => new UiMultiBars(s, Ctx, buttons));
                Stance = Host("the stance bar (vrcast)", () => new UiStanceBar(s, Ctx));

                _castAnchor = s.Anchor("Cast bar", new Vector2(0.5f, 0f), 195f, 13f, 0f, 150f);
                Cast = new UiCastBar(_castAnchor, 0f, 0f);
                Mirror = new UiMirrorTimers(s.Anchor("Mirror timers", new Vector2(0.5f, 1f), 206f, 78f, 0f, -96f), 0f, 0f);   // MirrorTimer1 TOP (0, -96)

                RectTransform errors = s.Anchor("Errors", new Vector2(0.5f, 1f), 512f, 80f, 0f, -122f);
                Messages = new UiMessages(errors, 0f, 0f, 512f, 4, 15f);
                RectTransform banner = s.Anchor("Banner", new Vector2(0.5f, 1f), 700f, 80f, 0f, -200f);
                Banner = new UiBanner(banner, 0f, 0f, 700f);

                // Iteration 11 (quests H-U1): the objective tracker at the top right.
                RectTransform objectives = s.Anchor("Objectives", new Vector2(1f, 1f), UiQuestTracker.Width, 768f - 172f - 70f, 0f, -172f);
                Tracker = Host("the quest tracker (quests)", () => new UiQuestTracker(objectives, Ctx, false));
                _objectivesAnchor = objectives;
                // Lead 2026-10-03 (the user: "put the mini map on the top right"): MinimapCluster TOPRIGHT (Minimap.xml) - the tracker
                // above sits below it (y 172), as WoW puts the WatchFrame under the minimap.
                Minimap = Host("the minimap", () => new UiMinimap(s, Ctx));
                Buffs = Host("the BuffFrame", () => new UiBuffFrame(s));   // lead 2026-10-04
                if (Buffs != null)
                {
                    Buffs.Cancel = id => { if (Ctx.S != null) Ctx.S.CancelAura(id); };
                    Player.BuffRowShown = false;
                }

                // Iteration 11: one holder per panel, moved by WoW's panel manager (WowArmUi); the quest / loot frames are panels too.
                Spellbook = new UiSpellbook(Holder(UiWindowKind.Spellbook, UiPanelArea.Left, 384f, 512f), Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Spellbook));
                QuestLog = new UiQuestLog(Holder(UiWindowKind.QuestLog, UiPanelArea.Left, 384f, 512f), Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.QuestLog));
                Character = new UiCharacter(Holder(UiWindowKind.Character, UiPanelArea.Left, 384f, 512f), Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Character));
                Mounts = new UiMounts(Holder(UiWindowKind.Mounts, UiPanelArea.Left, 384f, 512f), Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Mounts));
                Quest = new UiQuestDialog(Holder(UiWindowKind.QuestDialog, UiPanelArea.Left, 384f, 512f), Ctx, 0f, 0f);
                Loot = new UiLoot(Holder(UiWindowKind.Loot, UiPanelArea.Left, 256f, 512f), Ctx, 0f, 0f);
                _bagsAnchor = s.Anchor("Bags", new Vector2(1f, 0f), 192f, 240f, -6f, 70f);
                Bags = new UiBags(_bagsAnchor, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Bags));
                RectTransform popup = s.Anchor("Popup", new Vector2(0.5f, 1f), 320f, 100f, 0f, -135f);
                Death = new UiDeath(popup, Ctx, 0f, 0f);

                // PetFrame at PlayerFrame TOPLEFT (80, -60), TotemFrame at PlayerFrame BOTTOMLEFT (99, 38) (PetFrame.xml, TotemFrame.xml);
                // PetActionBarFrame TOPLEFT at MainMenuBar BOTTOMLEFT + (40, 112) (PetActionBarFrame.xml).
                Pet = new UiPetFrame(topLeft, Ctx, -19f + 80f, 4f + 60f);
                Totems = new UiTotemBar(topLeft, Ctx, -19f + 99f, 4f + 100f - 38f);
                _petBarAnchor = s.Anchor("Pet bar", new Vector2(0.5f, 0f), 1024f, 112f, 0f, 0f);
                PetBar = new UiPetBar(_petBarAnchor, Ctx, 40f, 0f, 30f, 38f, true);

                // The options frame sits in the middle of the screen (InterfaceOptionsFrame: Anchor CENTER).
                Settings = new UiSettingsWindow(Holder(UiWindowKind.Settings, UiPanelArea.Center, 384f, 512f), Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Settings));

                Block(Spellbook); Block(QuestLog); Block(Character); Block(Mounts); Block(Quest); Block(Loot); Block(Death); Block(Settings);
                BuildRegistered();
                UiKit.Over = false;
                UiKit.Pointable = true;
                UiKit.Surface = s;

                // talents / character: the achievement toast on the HUD canvas.
                AchievementAlert = Host("the achievement toast (character)", () => new UiAchievementAlert(s.Content, Ctx));
                _scaler = s.Canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                s.Finish();
            }
            finally
            {
                UiKit.Pointable = false;
                UiKit.Surface = null;
            }
        }

        public override void Refresh(float now)
        {
            base.Refresh(now);
            if (Minimap != null && !_minimapFailed)
            {
                try { Minimap.Refresh(now); }
                catch (Exception e) { _minimapFailed = true; WowLog.Error("UI: the minimap failed to refresh (stopped, logged once): " + e); }
            }
            // Iteration 11 (vrcast hook 8): the extra bars, then WoW's managed offsets (UIParent.lua:1162-1203, vrcast's UiBars).
            PlayerState p = Ctx.P;
            if (Buffs != null) Buffs.Refresh(p);   // lead 2026-10-04
            if (MultiBars != null) MultiBars.Refresh(p, now);
            if (Stance != null) Stance.Refresh(p, now);
            // integrator (pets H7d): WoW hides MainMenuBar's action buttons under VehicleMenuBar / the possess bar (UiVehicleBar sits on them).
            bool vehicleBar = UiVehicleBar.ShownNow;
            if (_mainButtons != null && _mainButtons.gameObject.activeSelf == vehicleBar)
            {
                _mainButtons.gameObject.SetActive(!vehicleBar);
                WowLog.Info("UI: main action bar " + (vehicleBar ? "hidden under the vehicle bar" : "shown again") + " (VehicleMenuBar.lua).");
            }
            _castAnchor.anchoredPosition = new Vector2(0f, 150f + UiBars.CastBarLift);
            _bagsAnchor.anchoredPosition = new Vector2(-6f - UiBars.ContainerShiftX, 70f + UiBars.ContainerLiftY);
            _petBarAnchor.anchoredPosition = new Vector2(0f, UiBars.PetBarLift);
            // lead fix 2026-10-10 (the user, desktop: "the quests on the right, overlap the UI for casting spells"): WatchFrame TOPRIGHT at
            // MinimapCluster BOTTOMRIGHT + (-CONTAINER_OFFSET_X, anchorY), BOTTOMRIGHT at UIParent BOTTOMRIGHT + (-CONTAINER_OFFSET_X,
            // CONTAINER_OFFSET_Y) (UIParent.lua:1937, :1941) - left of the Right Bar(s), above the bottom bars
            if (_objectivesAnchor != null)
            {
                _objectivesAnchor.anchoredPosition = new Vector2(-UiBars.ContainerShiftX, -172f);
                _objectivesAnchor.sizeDelta = new Vector2(UiQuestTracker.Width, 768f - 172f - 70f - UiBars.ContainerLiftY);
            }
            if (!_loggedOffsets && (UiBars.CastBarLift != 0f || UiBars.ContainerShiftX != 0f))
            {
                _loggedOffsets = true;
                WowLog.Info("UI: managed frame offsets (UIParent.lua:1162-1203) - cast bar +" + UiBars.CastBarLift + ", bags x -" + UiBars.ContainerShiftX
                    + " y +" + UiBars.ContainerLiftY + ", pet bar +" + UiBars.PetBarLift + ".");
            }
            // The pet frame / totems sit where the player's buff row is (below the player frame): the buffs move under them.
            Player.ShiftBuffs(PetShown || TotemsShown ? 20f : 0f);
            // WowSettings.UiScale: UIParent's scale - the canvas is referenced to 768 / scale units tall.
            float scale = Mathf.Clamp(WowSettings.UiScale, 0.75f, 1.5f);
            if (_scaler != null && scale != _scale)
            {
                _scale = scale;
                _scaler.referenceResolution = new Vector2(1024f / scale, 768f / scale);
                WowLog.Info("UI: desktop interface scale " + scale.ToString("0.00") + ".");
            }
        }

        private UnityEngine.UI.CanvasScaler _scaler;
        private float _scale = 1f;
        private RectTransform _mainButtons;

        // Lead 2026-10-03: the minimap (UiMinimap) and what WoW's panel manager reads of it (UIParent.lua:2005-2014 GetMaxUIPanelsWidth:
        // "Minimap:IsShown() and not MinimapCluster:IsUserPlaced()" -> min(MinimapCluster:GetLeft(), ...); ours is never moved).
        public UiMinimap Minimap;
        public UiBuffFrame Buffs;   // lead 2026-10-04: WoW's BuffFrame at the top right (the player frame's row is VR only now)
        private bool _minimapFailed;
        public bool MinimapShown { get { return Minimap != null && !_minimapFailed && Minimap.Shown; } }
        public float MinimapLeft { get { return ScreenWidth - UiMinimap.ClusterW; } }
    }

    // =====================================================================================================================
    /// <summary>
    /// VR, the Age of Mythology way: the panel lies along the LEFT forearm, drawn over everything (AomUiPanel.OverEverything), placed
    /// every frame from the Bridge's Body.LeftHand exactly as AomVr.ArmBar does (hand.position + hand.up x lift, rotation
    /// LookRotation(-hand.up, hand.forward): the face looks back at whoever turns the wrist up) and shifted towards the elbow.
    /// A window (spellbook, quest log, character, bags) stands up from the panel's far edge like an opened lid. The NPC quest frame
    /// and the loot window hang in the world between the player and the NPC / corpse, facing the player; the death popup and the
    /// banners appear in front of the head. All of it is pointed at with the right-hand ray + trigger, or poked (UiPointer).
    /// Iteration 4: the forearm panel (and its pet strip) shows only with WowSettings.VrForearmBar (the grip radial menus replace its
    /// action bar); the floating HUD (UiVrHud, WowSettings.VrHud) carries the frames and the messages; the palm menu (UiPalmMenu)
    /// carries the micro buttons, and without the forearm panel the window opens in the world above the palm (PlaceLidFree).
    /// Iteration 11: the registry's toggled windows open on the lid (resized to the window), its session windows (merchant, trainer,
    /// bank ...) on the dialog surface next to their NPC (UiWindowSpec.NearGuid); lid and dialog can be open together.
    /// </summary>
    public sealed class UiVrLayout : UiLayout
    {
        public float ArmMetresPerUnit = 0.0006f;
        public float ArmLift = 0.03f;
        public float ArmBack = 0.13f;
        public float ArmSide = 0f;
        public float LidMetresPerUnit = 0.00055f;
        public float LidAngle = 55f;
        public float DialogMetresPerUnit = 0.0012f;

        public const float ArmW = 520f, ArmH = 250f;
        private UiSurface _arm, _lid, _dialog, _popup, _banner, _strip;
        public const float StripH = 64f;
        private float _scale = 1f;
        private float _lidW = 384f, _lidH = 512f, _lidScale;
        private bool _bagBarFailed;
        /// <summary>Lead fix 2026-10-10: units between the lid window's right edge and the bags beside it (ours).</summary>
        public const float BesideGap = 12f;
        private RectTransform _bagsHolder, _bagsArea, _bagBarHolder;
        private UiBagBar _vrBagBar;   // lead 2026-10-11: the backpack + bag slots under the open bags (VR has no main menu bar)
        private ulong _dialogFor;
        private UiWindowKind _dialogKind;
        private bool _dialogPlaced, _popupPlaced, _loggedArm;
        private bool _bannerWasActive, _lidWanted;

        // Iteration 4: the floating HUD, the palm menu; the forearm panel only with WowSettings.VrForearmBar; without it the window
        // opens in the world above the palm (facing the player) instead of standing up from the arm.
        public readonly UiVrHud Hud = new UiVrHud();
        public readonly UiPalmMenu Palm = new UiPalmMenu();
        public UiPointer Pointer;
        private UiMessages _armMessages;
        private bool _lidPlaced, _lidHinged, _loggedLid;
        private int _lidPlacements;

        public UiSurface Arm { get { return _arm; } }

        /// <summary>
        /// World windows owned by other modules (iteration 5: the WoW Map window, Game/Play/Map) that the ray / poke must reach:
        /// kept in <see cref="UiLayout.Surfaces"/> while listed here (synced every LateUpdate before the pointer runs).
        /// </summary>
        public static readonly List<UiSurface> ExtraSurfaces = new List<UiSurface>();
        private readonly List<UiSurface> _extraAdded = new List<UiSurface>();

        private void SyncExtraSurfaces()
        {
            for (int i = _extraAdded.Count - 1; i >= 0; i--)
            {
                UiSurface s = _extraAdded[i];
                if (s != null && s.Canvas != null && ExtraSurfaces.Contains(s)) continue;
                Surfaces.Remove(s);
                _extraAdded.RemoveAt(i);
            }
            for (int i = 0; i < ExtraSurfaces.Count; i++)
            {
                UiSurface s = ExtraSurfaces[i];
                if (s == null || s.Canvas == null || _extraAdded.Contains(s)) continue;
                Surfaces.Add(s);
                _extraAdded.Add(s);
            }
        }

        protected override Transform RegisteredParent(UiWindowSpec spec, out UiSurface surface)
        {
            bool session = spec.SessionWindow;
            surface = session ? _dialog : _lid;
            UiKit.Over = !session;   // the lid draws over everything (arm UI), the dialog stands in the world
            UiKit.Pointable = false;
            UiKit.Surface = surface;
            return surface.Content;
        }

        public override void Build(Transform parent)
        {
            Go = new GameObject("WoW Arm UI (VR)");
            Go.transform.SetParent(parent, false);
            try
            {
                // --- the forearm panel
                UiKit.Over = true;
                _arm = UiSurface.MakeWorld("WoW Arm", Go.transform, ArmW, ArmH, ArmMetresPerUnit, true, 200);
                UiKit.Surface = _arm;
                Transform c = _arm.Content;
                Messages = new UiMessages(c, 0f, -58f, ArmW, 3, 13f);
                Player = new UiPlayerFrame(c, -8f, 0f, true);
                Target = new UiTargetFrame(c, 262f, 0f);
                RectTransform castHolder = SubCanvas(c, "Cast holder", 0f, 0f, ArmW, ArmH);
                Cast = new UiCastBar(castHolder, (ArmW - 195f) * 0.5f, 118f);
                Mirror = new UiMirrorTimers(castHolder, (ArmW - 206f) * 0.5f, -88f);   // lead 2026-10-26: above the arm's messages
                Xp = new UiXpBar(c, 11f, 134f, 498f);
                RectTransform buttons = SubCanvas(c, "Action buttons", 0f, 0f, ArmW, ArmH);
                Bar = new UiActionBar(buttons, Ctx, 11f, 152f, false);
                Micro = new UiMicroBar(c, Ctx, 172f, 186f, 30f, 302f, 200f, null);
                _arm.Finish();
                Surfaces.Add(_arm);
                _armMessages = Messages;

                // --- the window standing up from the arm
                _lid = UiSurface.MakeWorld("WoW Arm Window", Go.transform, 384f, 512f, LidMetresPerUnit, true, 201);
                UiKit.Surface = _lid;
                Spellbook = new UiSpellbook(_lid.Content, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Spellbook));
                QuestLog = new UiQuestLog(_lid.Content, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.QuestLog));
                Character = new UiCharacter(_lid.Content, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Character));
                Mounts = new UiMounts(_lid.Content, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Mounts));
                _bagsHolder = UiKit.Rect(_lid.Content, "Bags holder", 96f, 200f, 192f, 240f);
                Bags = new UiBags(_bagsHolder, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Bags));
                _bagsArea = UiKit.Rect(_lid.Content, "Bags area (beside)", 396f, 0f, UiContainerFrame.Width, 512f);   // lead fix 2026-10-10
                // lead 2026-10-11 (the user: "in VR mode, there is no way to unequip bags to replace them ... seeing the bag slots will be ok, so
                // then you can drag and drop"): WoW's bag bar (UiBagBar = MainMenuBarBagButtons) at the bottom right of where the bags stack
                _bagBarHolder = UiKit.Rect(_lid.Content, "Bag bar holder (VR)", 0f, 0f, 384f, 512f);
                try { _vrBagBar = new UiBagBar(_bagBarHolder, Ctx); }
                catch (Exception e) { _vrBagBar = null; WowLog.Warn("UI: the VR bag bar failed to build: " + e.Message); }
                _bagBarHolder.gameObject.SetActive(false);
                Settings = new UiSettingsWindow(_lid.Content, Ctx, 0f, 0f, () => Ctx.Toggle(UiWindowKind.Settings));
                Block(Spellbook); Block(QuestLog); Block(Character); Block(Mounts); Block(Settings);
                _lid.Finish();
                Surfaces.Add(_lid);
                _lid.SetShown(false);

                // --- the pet / totem strip: joins the arm panel's elbow edge while the player has a pet or totems
                _strip = UiSurface.MakeWorld("WoW Arm Pet", Go.transform, ArmW, StripH, ArmMetresPerUnit, true, 200);
                UiKit.Surface = _strip;
                Pet = new UiPetFrame(_strip.Content, Ctx, 6f, 6f);
                PetBar = new UiPetBar(_strip.Content, Ctx, 140f, 14f, 30f, 36f, false);
                Totems = new UiTotemBar(_strip.Content, Ctx, 380f, 4f);
                _strip.Finish();
                Surfaces.Add(_strip);
                _strip.SetShown(false);

                // --- the floating HUD (not pointable: it is not in Surfaces, so the ray passes through it) and the palm menu
                Hud.Build(Go.transform, Ctx);
                Palm.Build(Go.transform, Ctx);
                Surfaces.Add(Palm.Surface);
                // Iteration 11 (quests H-U1): the objective tracker (its own world surface).
                Tracker = Host("the quest tracker (quests)", () => new UiQuestTracker(Go.transform, Ctx, true));
                if (Tracker != null && Tracker.Surface != null) Surfaces.Add(Tracker.Surface);
                // talents / character: the achievement toast on the floating HUD.
                AchievementAlert = Host("the achievement toast (character)", () => new UiAchievementAlert(Hud.Surface != null ? Hud.Surface.Content : null, Ctx));

                // --- world windows (normal depth: they stand in the world)
                UiKit.Over = false;
                _dialog = UiSurface.MakeWorld("WoW Dialog", Go.transform, 384f, 512f, DialogMetresPerUnit, false, 100);
                UiKit.Surface = _dialog;
                Quest = new UiQuestDialog(_dialog.Content, Ctx, 0f, 0f);
                Loot = new UiLoot(_dialog.Content, Ctx, 64f, 128f);
                Block(Quest); Block(Loot);
                _dialog.Finish();
                Surfaces.Add(_dialog);
                _dialog.SetShown(false);
                UiCarry.Add(_dialog);   // 13.7: world windows move with the player (UiCarry)

                _popup = UiSurface.MakeWorld("WoW Popup", Go.transform, 320f, 100f, 0.0014f, false, 101);
                UiKit.Surface = _popup;
                Death = new UiDeath(_popup.Content, Ctx, 0f, 0f);
                Block(Death);
                _popup.Finish();
                Surfaces.Add(_popup);
                _popup.SetShown(false);
                UiCarry.Add(_popup);

                _banner = UiSurface.MakeWorld("WoW Banner", Go.transform, 700f, 80f, 0.0024f, false, 99);
                UiKit.Surface = _banner;
                Banner = new UiBanner(_banner.Content, 0f, 0f, 700f);
                _banner.SetShown(false);
                UiCarry.Add(_banner);

                // Iteration 11: the registry's windows (lid / dialog); the tooltips stay on top of them.
                BuildRegistered();
                _lid.KeepOnTop();
                _dialog.KeepOnTop();
            }
            finally
            {
                UiKit.Over = false;
                UiKit.Surface = null;
            }
        }

        private static bool IsSessionKind(UiWindowKind k)
        {
            UiWindowSpec s = UiRegistry.Of(k);
            return s != null && s.SessionWindow;
        }

        public override void ApplyWindows(bool[] open)
        {
            bool was = _lidWanted;
            base.ApplyWindows(open);
            // The lid carries the open toggled window: the six iteration-10 ones (384 x 512) or a registered one (its own size).
            float w = 384f, h = 512f;
            bool lid = open[1] || open[2] || open[3] || open[4] || open[5] || open[6];
            foreach (KeyValuePair<UiWindowKind, UiWindow> kv in Registered)
            {
                int k = (int)kv.Key;
                if (k >= open.Length || !open[k] || IsSessionKind(kv.Key)) continue;
                lid = true;
                w = kv.Value.W;
                h = kv.Value.H;
            }
            _lidWanted = lid;
            if (lid && (w != _lidW || h != _lidH))
            {
                _lidW = w;
                _lidH = h;
                _lid.Resize(w, h);
                _lidPlaced = false;
            }
            if (_lidWanted && !was) _lidPlaced = false;
            _lid.SetShown(_lidWanted);
            // lead 2026-10-10: the bags beside the lid (right of it, a registered hit area) while another lid window is open, so items drag
            // between the bags and the Character window; alone they keep their place on the lid. Lead fix 2026-10-10 (the user, a screenshot:
            // the backpack still over the Character window - in VR UiBags stacks in the arm window's bottom-right corner wherever the holder
            // is): the stacking area itself (UiBags.VrArea) is a column right of the lid, BesideGap units out, the lid's height
            int bi = (int)UiWindowKind.Bags;
            bool bags = bi < open.Length && open[bi], other = false;
            for (int j = 1; j < open.Length && !other; j++) if (j != bi && open[j] && !IsSessionKind((UiWindowKind)j)) other = true;
            if (_bagsHolder != null)
            {
                bool beside = bags && other;
                UiKit.Move(_bagsHolder, beside ? _lidW + BesideGap : 96f, beside ? 60f : 200f);
                if (_bagsArea != null)
                {
                    UiKit.Move(_bagsArea, _lidW + BesideGap, 0f);
                    UiKit.Size(_bagsArea, UiContainerFrame.Width, _lidH);
                    if (Bags != null) { Bags.VrArea = beside ? _bagsArea : null; Bags.VrBottomInset = _vrBagBar != null ? UiBagBar.H + 4f : 0f; }
                    if (_bagBarHolder != null)
                    {
                        UiKit.Move(_bagBarHolder, beside ? _lidW + BesideGap : 0f, 0f);
                        UiKit.Size(_bagBarHolder, beside ? UiContainerFrame.Width : _lidW, _lidH);
                        if (_bagBarHolder.gameObject.activeSelf != (bags && _vrBagBar != null)) _bagBarHolder.gameObject.SetActive(bags && _vrBagBar != null);
                    }
                    bool listed = _lid.HitAreas.Contains(_bagsArea);
                    if (beside && !listed) _lid.HitAreas.Add(_bagsArea);
                    else if (!beside && listed) _lid.HitAreas.Remove(_bagsArea);
                }
            }
        }

        public override void OnEvent(GameEvent e, float now)
        {
            base.OnEvent(e, now);
            Hud.OnEvent(e, now);
        }

        /// <summary>WowSpace.Rebased: the windows standing in the world move with it (the hand / head ones are placed every frame).</summary>
        public void Shift(Vector3 deltaUnity)
        {
            if (_dialog.Shown) _dialog.Root.position -= deltaUnity;
            if (_popup.Shown) _popup.Root.position -= deltaUnity;
            if (_banner.Shown) _banner.Root.position -= deltaUnity;
            if (_lid.Shown && !_lidHinged) _lid.Root.position -= deltaUnity;
        }

        public override void Refresh(float now)
        {
            // Errors and info go to the HUD unless only the forearm panel is on (switched before the base refreshes Messages).
            UiMessages want = WowSettings.VrHud || !WowSettings.VrForearmBar ? Hud.Messages : _armMessages;
            Messages = want;
            base.Refresh(now);
            if (want != _armMessages) _armMessages.Refresh(now);
            if (WowSettings.VrHud) Hud.Refresh(Ctx, now);
            if (want != Hud.Messages) Hud.Messages.Refresh(now);
            Palm.Micro.Refresh();
            if (_vrBagBar != null && _bagBarHolder != null && _bagBarHolder.gameObject.activeInHierarchy && Ctx.S != null && Ctx.S.Player != null)
            {
                try { _vrBagBar.Refresh(Ctx.S.Player, now); }   // lead 2026-10-11
                catch (Exception e) { if (!_bagBarFailed) { _bagBarFailed = true; WowLog.Warn("UI: the VR bag bar refresh threw " + e.Message); } }
            }
            Palm.RefreshExtra();   // iteration 7: the Mounts button
            if (Palm.Xp != null && Ctx.S != null && Ctx.S.Player != null) Palm.Xp.Refresh(Ctx.S.Player);   // lead 2026-10-04: the palm's XP bar

            // The dialog surface: the quest frame / loot (iteration 10) or the open registered session window (iteration 11).
            UiWindowKind kind = Quest.IsOpen ? UiWindowKind.QuestDialog : Loot.IsOpen ? UiWindowKind.Loot : UiWindowKind.None;
            UiWindow shown = null;
            if (kind == UiWindowKind.None)
                foreach (KeyValuePair<UiWindowKind, UiWindow> kv in Registered)
                    if (kv.Value.IsOpen && IsSessionKind(kv.Key)) { kind = kv.Key; shown = kv.Value; break; }
            bool dialog = kind != UiWindowKind.None;
            if (dialog != _dialog.Shown) { _dialog.SetShown(dialog); _dialogPlaced = false; }
            if (dialog && kind != _dialogKind)
            {
                _dialogKind = kind;
                float w = shown != null ? shown.W : 384f, h = shown != null ? shown.H : 512f;
                _dialog.Resize(w, h);
                _dialogPlaced = false;
            }
            // lead 2026-10-10: Settings "Quest text size (VR)" - the quest dialog grows with it
            float dialogScale = DialogMetresPerUnit * (kind == UiWindowKind.QuestDialog ? Mathf.Clamp(WowSettings.VrQuestSize, 0.75f, 2f) : 1f);
            if (!Mathf.Approximately(_dialog.Root.localScale.x, dialogScale)) _dialog.Root.localScale = Vector3.one * dialogScale;
            ulong forGuid = 0;
            if (kind == UiWindowKind.QuestDialog || kind == UiWindowKind.Loot)
                forGuid = Ctx.S.CurrentDialog != null ? Ctx.S.CurrentDialog.NpcGuid : Ctx.S.CurrentLoot != null ? Ctx.S.CurrentLoot.CorpseGuid : 0;
            else if (dialog)
            {
                UiWindowSpec sp = UiRegistry.Of(kind);
                try { forGuid = sp != null && sp.NearGuid != null ? sp.NearGuid(Ctx.S) : 0UL; }
                catch (Exception) { forGuid = 0; }
            }
            if (forGuid != _dialogFor) { _dialogFor = forGuid; _dialogPlaced = false; }
            bool dead = Death.IsOpen;
            if (dead != _popup.Shown) { _popup.SetShown(dead); _popupPlaced = false; }
            bool banner = Banner.Active;
            if (banner && !_bannerWasActive) PlaceInFront(_banner, 2.2f, 0.35f);
            if (banner != _banner.Shown) _banner.SetShown(banner);
            _bannerWasActive = banner;
        }

        public override void Place(bool beforeRender)
        {
            if (!beforeRender) SyncExtraSurfaces();
            Transform hand = WowPlayer.LeftHand;
            Camera headCam = WowPlayer.Head();
            Transform headT = headCam != null ? headCam.transform : null;
            bool arm = hand != null;
            bool armBar = arm && WowSettings.VrForearmBar;
            _arm.SetShown(armBar);
            _lid.SetShown(_lidWanted && (armBar || headT != null));
            _strip.SetShown(armBar && (PetShown || TotemsShown));
            if (armBar != _lidHinged) { _lidHinged = armBar; _lidPlaced = false; }
            if (_lid.Carried == _lidHinged) { if (_lidHinged) _lid.Carried = false; else UiCarry.Add(_lid); }   // 13.7: the free lid stands in the world
            UiCarry.Step();
            // WowSettings.ArmUiScale: the arm panel, its window lid, the pet strip and the palm menu grow together (read every frame, applied on change).
            float scale = Mathf.Clamp(WowSettings.ArmUiScale, 0.75f, 1.5f);
            if (scale != _scale)
            {
                _scale = scale;
                _arm.Root.localScale = Vector3.one * (ArmMetresPerUnit * scale);
                _strip.Root.localScale = Vector3.one * (ArmMetresPerUnit * scale);
                Palm.Surface.Root.localScale = Vector3.one * (Palm.MetresPerUnit * scale);
                if (!beforeRender) WowLog.Info("UI: arm panel scale " + scale.ToString("0.00") + ".");
            }
            // lead 2026-10-10 (the user: "add a 'Quest Size' slider for VR as it is hard to read Quests"): Settings > Display "Quest text
            // size (VR)" - the lid grows with it while the quest log is open (the quest dialog and the objectives too)
            float lidScale = scale * (QuestLog.IsOpen ? Mathf.Clamp(WowSettings.VrQuestSize, 0.75f, 2f) : 1f);
            if (lidScale != _lidScale) { _lidScale = lidScale; _lid.Root.localScale = Vector3.one * (LidMetresPerUnit * lidScale); }
            // The floating HUD (head-locked, lazy follow).
            bool hud = WowSettings.VrHud && headT != null;
            if (hud != Hud.Surface.Shown) { Hud.Surface.SetShown(hud); if (hud) Hud.ResetFollow(); }
            if (hud) Hud.Place(headT, Time.unscaledDeltaTime, beforeRender);
            if (Tracker != null) Tracker.PlaceVr(hud ? headT : null, beforeRender ? 0f : Time.unscaledDeltaTime);   // quests H-U1
            // The palm menu: the gesture is judged once per frame (LateUpdate), the placement follows the hand before rendering too.
            if (!beforeRender)
            {
                bool keep = Pointer != null && Pointer.Hovered != null && Pointer.Hovered.Surface == Palm.Surface;
                // lead 2026-10-05: never while the Somnium tablet is grabbed / spawned (VrTabletGuard.PalmAllowed)
                Transform rightHand = WowPlayer.RightHand;
                bool palmOk = hand == null || VrInput.VrTabletGuard.PalmAllowed(hand.position, rightHand != null ? rightHand.position : hand.position);
                Palm.Step(palmOk ? hand : null, headT, Time.unscaledDeltaTime, keep && palmOk);
            }
            Palm.Place(hand, headT);
            if (!armBar && _lid.Shown && !beforeRender) PlaceLidFree(headT);
            if (armBar)
            {
                Quaternion rot = Quaternion.LookRotation(-hand.up, hand.forward);
                Vector3 pos = hand.position + hand.up * ArmLift - hand.forward * ArmBack + hand.right * ArmSide;
                Transform t = _arm.Root;
                t.SetPositionAndRotation(pos, rot);
                if (_strip.Shown)
                {
                    // Below the panel's elbow edge (its -y), same plane.
                    Vector3 stripPos = pos - rot * Vector3.up * ((ArmH * 0.5f + StripH * 0.5f + 4f) * ArmMetresPerUnit * _scale);
                    _strip.Root.SetPositionAndRotation(stripPos, rot);
                }
                if (!_loggedArm)
                {
                    _loggedArm = true;
                    WowLog.Info("UI: arm panel on the left hand '" + hand.name + "' (" + (ArmW * ArmMetresPerUnit).ToString("0.00") + " x " + (ArmH * ArmMetresPerUnit).ToString("0.00")
                        + " m, lift " + ArmLift + " m, back " + ArmBack + " m; window lid " + LidAngle + " degrees).");
                }
                if (_lid.Shown)
                {
                    // Hinged at the arm panel's far edge (its +y, towards the fingers), tilted up towards the face.
                    Quaternion lidRot = rot * Quaternion.Euler(-LidAngle, 0f, 0f);
                    Vector3 edge = pos + rot * Vector3.up * (ArmH * ArmMetresPerUnit * _scale * 0.5f);
                    Vector3 lidPos = edge + lidRot * Vector3.up * (_lidH * LidMetresPerUnit * _lidScale * 0.5f + 0.01f);
                    _lid.Root.SetPositionAndRotation(lidPos, lidRot);
                }
            }
            if (beforeRender) return;
            Camera head = WowPlayer.Head();
            if (head == null) return;
            if (_dialog.Shown && !_dialogPlaced) { _dialogPlaced = true; PlaceDialog(head.transform); }
            if (_popup.Shown && !_popupPlaced) { _popupPlaced = true; PlaceInFront(_popup, 1.1f, -0.1f); }
        }

        /// <summary>
        /// Without the forearm panel the window (spellbook, quest log, character, bags, settings) opens in the world: above the palm menu
        /// when it is up (it was opened from there), else in front of the head a little to the left and down; facing the player. It
        /// stays where it was put and comes back in front only when the player has walked more than 1.3 m away from it.
        /// </summary>
        private void PlaceLidFree(Transform head)
        {
            if (head == null) return;
            if (_lidPlaced && (_lid.Root.position - head.position).sqrMagnitude < 1.3f * 1.3f) return;
            bool again = _lidPlaced;
            _lidPlaced = true;
            float h = _lidH * LidMetresPerUnit * _lidScale;
            Vector3 pos;
            if (Palm.Open && !again) pos = Palm.Surface.Root.position + Vector3.up * (h * 0.5f + 0.05f);
            else
            {
                Vector3 f = head.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
                pos = head.position + f * 0.55f - right * 0.08f - Vector3.up * 0.06f;
            }
            Vector3 look = pos - head.position;
            if (look.sqrMagnitude < 1e-6f) look = head.forward;
            // Not nearer than 0.6 m (the lab put it 0.35 m from the eyes above a palm held 0.4 m ahead - too close to read comfortably).
            if (look.magnitude < 0.6f) { look = look.normalized * 0.6f; pos = head.position + look; }
            _lid.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            if (!_loggedLid || _lidPlacements < 3)
            {
                _loggedLid = true;
                WowLog.Info("UI: window placed in the world " + (again ? "in front of the player (walked away from it)" : Palm.Open ? "above the palm menu" : "in front of the player")
                    + ", " + (pos - head.position).magnitude.ToString("0.00") + " m from the head.");
            }
            _lidPlacements++;
        }

        /// <summary>Between the player and the NPC / corpse, a little to the right, facing the player (placed once per conversation).</summary>
        private void PlaceDialog(Transform head)
        {
            Vector3 eye = head.position;
            Vector3 dir = head.forward;
            EntityState e = _dialogFor != 0 && Ctx.S != null ? Ctx.S.Find(_dialogFor) : null;
            if (e != null)
            {
                Vector3 at = WowSpace.ToUnity(Ctx.S.MapId, e.Pos.X, e.Pos.Y, e.Pos.Z);
                Vector3 d = at - eye;
                d.y = 0f;
                if (d.sqrMagnitude > 0.04f) dir = d;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 pos = eye + dir * 0.95f + right * 0.32f - Vector3.up * 0.12f;
            Vector3 look = pos - eye;
            _dialog.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(look.normalized, Vector3.up));
            string what = _dialogKind == UiWindowKind.QuestDialog ? "quest dialog" : _dialogKind == UiWindowKind.Loot ? "loot window" : _dialogKind + " window";
            WowLog.Info("UI: " + what + " placed in the world (" + (e != null ? "towards " + e.Name : "in front of the player") + ").");
        }

        private static void PlaceInFront(UiSurface s, float distance, float up)
        {
            Camera head = WowPlayer.Head();
            if (head == null) return;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = head.transform.position + fwd * distance + Vector3.up * up;
            s.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        }
    }
}
