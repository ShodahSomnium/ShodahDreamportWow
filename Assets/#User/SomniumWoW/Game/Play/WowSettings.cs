using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// CONTRACT (shared). The player's settings, shown in the settings window (UI) and read by every module. Each module
    /// reads the values it cares about when it needs them (or on Changed). The front end loads the saved text at boot
    /// (Load) and saves it when Changed fires (Serialize). Owners may ADD settings (a field, a default, a line in
    /// Serialize/Load); do not rename or remove.
    /// </summary>
    public static class WowSettings
    {
        // ---------------------------------------------------------------- display
        public static bool ShowNameplates = true;          // creature/NPC nameplates
        public static bool ShowFriendlyNames = true;       // NPC names even when not targeted
        public static bool ShowCombatText = true;          // floating damage/heal numbers
        public static bool ShowPlayerPlates = true;        // "Name - Level N Class" over other Somnium players
        public static bool ShowQuestMarkers = true;
        public static float UiScale = 1f;                  // desktop HUD scale (0.75 .. 1.5)
        public static float ArmUiScale = 1f;               // VR arm panel scale (0.75 .. 1.5)

        // ---------------------------------------------------------------- effects
        public static bool SpellEffects = true;            // spell visuals (cast, missile, impact, auras)
        public static bool WorldParticles = true;          // torches, fires, smoke, fireflies on world props
        public static int EffectsQuality = 2;              // 0 low, 1 medium, 2 high (particle counts/distances)

        // ---------------------------------------------------------------- audio (0..1)
        public static float MasterVolume = 1f;
        public static float MusicVolume = 0.45f;
        public static float EffectsVolume = 0.8f;          // spells, weapons, creatures, UI
        public static float AmbienceVolume = 0.6f;
        public static float FootstepVolume = 0.6f;
        public static float VoiceVolume = 0.8f;            // NPC greetings, error voice lines
        public static bool Music = true;
        public static bool Footsteps = true;
        public static int VoiceGender = 0;                 // race voice lines (errors, emotes): 0 male, 1 female

        // ---------------------------------------------------------------- gameplay
        public static int Difficulty = 1;                  // 0 easy, 1 normal, 2 hard (creature damage/health tuning)
        public static bool AutoLoot = false;
        public static float SwingSpeed = 1.6f;             // VR: blade speed (m/s) that counts as a hit
        public static bool Haptics = true;                 // VR controller pulses
        public static bool AvatarCombatAnimations = true;  // desktop: attack/cast animations on the Somnium avatar

        // ---------------------------------------------------------------- iteration 4 (lead contract)
        public static bool ImmersiveCasting = false;       // VR: a finished cast waits until you push the casting hand forward
        public static bool VrHud = true;                   // VR: the small floating HUD top-left of your view
        public static bool VrForearmBar = false;           // VR: the old action bar along the forearm (radial menus replace it)
        public static bool LowHealthVignette = true;       // red view edges below 20% health
        public static float VrTargetRange = 150f;          // VR: target ray reach in metres (20 .. 300)
        public static bool FastTravel = false;             // flights: fade and jump to the landing point instead of flying
        public static bool UnlockAllFlightPaths = true;    // testing (user, 2026-09-27): every flight path of your faction counts as discovered
        public static bool UnlockAllMounts = true;          // testing (user, 2026-09-27): every mount of your faction/class, riding 300 + Cold Weather Flying (offline)
        public static bool FlyAnywhere = true;              // testing: flying mounts fly in any zone offline (all offline starting zones are no-fly in 3.3.5a)
        /// <summary>Lead 2026-10-12: our macros (UiMacros' encoded list); DATA, NOT touched by ResetDefaults.</summary>
        public static string MacroData = "";
        public static string MountDisplayCache = "";        // DATA, not a setting: MountDisplays.SaveCache() (numbers only, <= 4 KB); NOT touched by ResetDefaults
        public static string AutoAccountGm = "";            // DATA (lead, D13): "host|NAME;..." automatic accounts the account service reported as GM; NOT touched by ResetDefaults
        public static bool GroundClutter = true;           // grass, flowers, pebbles
        public static float ClutterDensity = 1f;           // 0.25 .. 1.5
        public static bool AmbientLife = true;             // birds and critters
        public static float WaterVisibility = 1.25f;      // 0.5 .. 2.5, Water.shader sky reflection + opacity (lead 2026-10-26; ours: x1 = the shader's own amount)
        public static float TerrainShine = 2.5f;           // 0.25 .. 4, x the client's terrain specular (ours: the user found x1 too faint)
        public static bool TerrainSpecular = true;        // the client's terrain shine (CVar "specular": _s.blp alpha as the specular mask)

        // ---------------------------------------------------------------- iteration 6 (front): online (AzerothCore)
        public static bool OnlineMode = false;             // Play tab: false = offline characters, true = log in to a server
        public const string DefaultServerHost = "145.241.232.229";   // stage A1: the user's own realm (a saved non-empty value wins)
        public static string ServerHost = DefaultServerHost;  // the login server (host name or IP)
        public static int ServerPort = 3724;               // the auth port
        public static bool RememberAccount = true;         // keep the account NAME (never the password) for the next visit
        public static string OnlineAccount = "";           // the remembered account name (upper case), "" when RememberAccount is off

        // ---------------------------------------------------------------- stage A1 (front): automatic accounts
        public static bool OnlineManual = false;           // Online tab: false = "Play online as <nick>" (automatic account), true = the manual form

        // ---------------------------------------------------------------- stage S1 (owner hud): the top-left HUD group
        // (player, target, party and focus frames, pet and totems) - the user: "the ability to resize and position in the settings".
        // Desktop and VR keep their own values (a fraction of the screen and metres are different units). 1 / 0 / 0 = today's layout.
        public const float HudScaleMin = 0.6f, HudScaleMax = 1.6f;          // ours
        public const float HudOffsetMaxX = 0.8f, HudOffsetMaxY = 0.8f;      // desktop: fractions of the screen (ours)
        public const float VrHudOffsetMax = 0.3f;                           // VR: metres either way (ours)
        public static float HudScale = 1f;                 // desktop: size of the top-left group (on top of UiScale), 0.6 .. 1.6
        public static float HudOffsetX = 0f;               // desktop: moved right by this fraction of the screen width (0 .. 0.8)
        public static float HudOffsetY = 0f;               // desktop: moved down by this fraction of the screen height (0 .. 0.8)
        public static float VrHudScale = 1f;               // VR: size of the floating HUD, 0.6 .. 1.6
        public static float VrHudOffsetX = 0f;             // VR: metres to the right in the HUD's own plane (-0.3 .. 0.3)
        public static float VrHudOffsetY = 0f;             // VR: metres up in the HUD's own plane (-0.3 .. 0.3)
        // Lead 2026-10-03 (the user: "put the mini map on the top right"): the desktop minimap (UiMinimap) and its zoom step 0..5 (WoW's minimapZoom CVar)
        public static bool ShowMinimap = true;             // show_minimap
        public static int MinimapZoom = 0;                 // minimap_zoom

        // ---------------------------------------------------------------- iteration 11 (w0game, wave 0): every new setting of the
        // proposals, defaults per PLAN11 s2. Their Settings rows stay hidden until the owner calls SetLive(key) (see below).
        // uicore (hook H1): WoW's cursor art; the cursor in first person while Alt / a window / a held item
        public static bool WowCursorArt = true;            // wow_cursor_art
        public static bool WowCursorFirstPerson = true;    // wow_cursor_first_person
        // vrcast (hook 9): the ActionBars page (PLAN11 s2: 4 extra bars ON + Always Show ON; WoW's own defaults are off - ours)
        public static bool ShowBottomLeftBar = true, ShowBottomRightBar = true, ShowRightBar = true, ShowRightBar2 = true;   // desktop
        public static bool LockActionBars = true;          // ours: a plain drag does not pick a button up, Shift+drag does
        public static bool AlwaysShowActionBars = true;    // ours: empty multi-bar buttons stay visible (ALWAYS_SHOW_MULTIBARS)
        public static bool VrRows = true;                  // VR: pull / push switches the radial menu's row
        public static bool VrRowsRemember = false;         // VR: each hand reopens on its last row (PLAN11 s2: off = the Action Bar row each grip)
        public static bool ImmersiveStrike = false;        // VR: the strike ring (PLAN11 s2: off)
        public static bool ImmersiveEating = false;
        public static bool SharpVrUi = true;
        public static bool ImmersiveBow = true;
        public static bool ShowMainHand = true, ShowOffHand = true, ShowRanged = true;   // lead 2026-10-12: weapons on the avatar (everyone sees it)            // lead 2026-10-10: VR bow, arrow and the column of shots (GearLocal.Bow)               // lead 2026-10-10: VR interface after DLSS (UiSharp)
        public static float VrQuestSize = 1f;              // lead 2026-10-10: VR quest log / dialog / objectives size (0.75 .. 2)
        public static int VrMenuStyle = 1;                 // lead 2026-10-10: VR spell menus - 0 Twist wrist, 1 Gear Select (grip and move); 2026-11-04: Gear Select is the default
        public static float WristSensitivity = 0.5f;       // lead 2026-10-05: VR spell menus' twist, 0 .. 1 (0.5 = as before; higher = less twist)        // lead 2026-10-04 (the user's toggle): VR - eat and drink only while sitting in real life
        public static int StrikeRingBar = 0;               // 0 Action Bar, 1 Bottom Left, 2 Bottom Right, 3 Right, 4 Right 2
        public static float StrikeRingDistance = 0.55f;    // m from the chest, 0.25 .. 1.20 (lead 2026-10-03: was 0.40 .. 0.75)
        public static float StrikeRingSize = 1f;           // lead 2026-10-03 (the user): icon size x 0.5 .. 2 (the hit sphere grows with it)
        public static float StrikeRingSpread = 1f;         // lead 2026-10-03 (the user): ring radius x 0.5 .. 2
        // Lead (2026-10-03, the user: "Armour Adjustment ... select the armour, move it xyz and also scale it, then save ... show/hide them
        // individually ... it will remember where the placement should be"): per armour PIECE SLOT (0 helm, 1 left shoulder, 2 right
        // shoulder), not per item, so a new helm sits where the last one was put. Offsets in metres in the piece's own frame (x right, y up,
        // z forward of the head / the torso), size x 0.5 .. 2. Everyone sees them (record ints 45-47). HelmInPhotos: a helm hidden from your
        // own eyes (VR, desktop first person) still shows in the Somnium camera's photos and in mirrors.
        // 2026-10-04 (the user: "expand the armour fitting tool to include all the pieces"): 10 slots - 0 helm, 1 left shoulder,
        // 2 right shoulder, then the full shell's 3 chest, 4 waist, 5 arms, 6 hands, 7 legs, 8 feet, 9 cloak (ArmourAdjust.Names).
        public const int ArmourSlots = 10;
        public static readonly bool[] ArmourShow = { true, true, true, true, true, true, true, true, true, true };
        public static readonly float[] ArmourX = new float[ArmourSlots], ArmourY = new float[ArmourSlots], ArmourZ = new float[ArmourSlots];
        public static readonly float[] ArmourSize = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        /// <summary>Lead (2026-10-04, the user: "add a tilt for the armour, as chest and waist are very hard to line up"): degrees, + = the
        /// top leans forward (the cloak: its hem swings back), -45 .. +45.</summary>
        public static readonly float[] ArmourTilt = new float[ArmourSlots];
        /// <summary>Lead (2026-10-04, the user: "add taper size for top and bottom"): the full shell's pieces (3 chest .. 9 cloak) x0.5 ..
        /// 1.5 at their top and their bottom (the top of a limb is its upper joint; the cloak: its shoulders and its hem).</summary>
        public static readonly float[] ArmourTaperTop = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        public static readonly float[] ArmourTaperBottom = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        // lead 2026-10-04 (the user: "add some finer controls ... something like top start and bottom end"): the full shell's pieces (3 chest
        // .. 8 feet) - their top and bottom edges moved forward / back (Z) and up / down (Y), m, -0.15 .. 0.15; the piece shears and stretches
        // between them (ArmourShell.EndPieces)
        public static readonly float[] ArmourTopZ = new float[10], ArmourTopY = new float[10], ArmourBottomZ = new float[10], ArmourBottomY = new float[10];
        public const float ArmourEndMax = 0.15f;
        /// <summary>Lead (2026-10-04, the user: "Hide Avatar Part"): the avatar's body under the full-armour piece is hidden (slots 3 chest ..
        /// 8 feet; ArmourBodyHide). The helm's is HelmHideHair ("Face in helm"). Synced as the piece's flag bit.</summary>
        public static readonly bool[] ArmourHideAvatar = new bool[ArmourSlots];
        /// <summary>Lead (2026-10-04): "Face in helm" - with the helm on, the head is hidden and a snapshot of the face is drawn inside the
        /// helm (ArmourFaceCard); others see it too (the helm adjust's flag bit).</summary>
        public static bool HelmHideHair = true;
        public static bool ArmourMirrorShoulders = true;
        public static bool HelmInPhotos = true;
        public static int ArmourPiece = 0;                 // the Armour page's selection (not saved)
        public static float StrikeRingHeight = 0f;
        /// <summary>Lead (2026-10-04): metres added to the rider's seat on a mount (MountSeat.Raised), -0.30 .. +0.50.</summary>
        public static float MountSeatHeight = 0f;         // lead 2026-10-03 (the user): m up (+) / down (-) from the default (0.30 m below the eye), -0.50 .. 0.30
        // avatar (proposal-avatar.md 5.7): forms on the avatar, the desktop combat animation style, immersive armour
        public static bool ShowForms = true;               // show_forms (PLAN11 s2: on)
        public static int AvatarCombatStyle = 1;           // avatar_combat_style: 1 WoW (full body) - PLAN11 s2 default, 0 simple (arms)
        public static int ImmersiveArmour = 2;             // immersive_armour: 0 off, 1 helm + shoulders, 2 full (experimental, A2) - lead 2026-10-04: the user: "immersive armour on by default, full" (was 0, PLAN11 s2)
        public static float ArmourFit = 1f;                // armour_fit: 0.5 .. 1.4 (lead 2026-10-04: was 0.8, the user hit the floor)
        // talents (hook 8): PREVIEW_TALENT_CHANGES (forced on in VR by the talents window)
        public static bool PreviewTalents = true;          // preview_talents (PLAN11 s2: on)
        // spellfx (hook 13): weapon enchant glows; ground spells at the screen centre instead of the mouse (desktop)
        public static bool WeaponEnchantGlows = true;      // weapon_enchant_glows (key ours)
        public static bool GroundTargetAtScreenCentre = false;   // ground_target_screen_centre (key ours; PLAN11 s2: off = at the mouse)
        // motion (hook 11): VR comfort for forced moves, the flight map after a discovery, VR facing assist
        public static int VrForcedMove = 0;                // vr_forced_move: 0 Dim (PLAN11 s2), 1 Blink, 2 Smooth (VR only)
        public static bool TaxiMapAfterDiscovery = true;   // taxi_map_after_discovery (PLAN11 s2: on)
        public static bool VrFacingAssist = false;         // vr_facing_assist (VR only; PLAN11 s2: off)

        // Iteration 11 (w0game): which Settings rows are shown. A row of a feature that is not built yet stays hidden (PLAN11 s1.8: no
        // control that does nothing); the feature's owner calls SetLive("<its save key>") once its feature works (main thread).
        private static readonly HashSet<string> _live = new HashSet<string>();
        /// <summary>+1 whenever a row is shown or hidden (the Settings window re-lays out its pages).</summary>
        public static int LiveVersion { get; private set; }

        /// <summary>The owner of a setting says its feature works (true) or not (false); the Settings window shows the row while true.</summary>
        public static void SetLive(string key, bool live = true, bool say = true)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (live ? _live.Add(key) : _live.Remove(key))
            {
                LiveVersion++;
                if (say) WowLog.Info("Settings: the row '" + key + "' is " + (live ? "shown (its feature is built)" : "hidden") + ".");
            }
        }

        /// <summary>True when the row of this save key is shown (SetLive).</summary>
        public static bool IsLive(string key) { return key != null && _live.Contains(key); }

        /// <summary>Stage S1: the Settings window's "Reset HUD" - the HUD group back where WoW puts it (desktop and VR), nothing else.</summary>
        public static void ResetHud()
        {
            HudScale = 1f; HudOffsetX = 0f; HudOffsetY = 0f;
            VrHudScale = 1f; VrHudOffsetX = 0f; VrHudOffsetY = 0f;
            NotifyChanged();
        }

        /// <summary>Raised after any change made through Set/Load/ResetDefaults (the front end saves on it).</summary>
        public static event Action Changed;

        /// <summary>Call after changing fields directly (the settings window does); fans Changed out safely.</summary>
        public static void NotifyChanged()
        {
            var h = Changed;
            if (h == null) return;
            foreach (Action d in h.GetInvocationList())
            {
                try { d(); }
                catch (Exception e) { WowLog.Error("Settings: a Changed listener threw: " + e); }
            }
        }

        public static void ResetDefaults()
        {
            ShowNameplates = true; ShowFriendlyNames = true; ShowCombatText = true; ShowPlayerPlates = true; ShowQuestMarkers = true;
            UiScale = 1f; ArmUiScale = 1f;
            SpellEffects = true; WorldParticles = true; EffectsQuality = 2;
            MasterVolume = 1f; MusicVolume = 0.45f; EffectsVolume = 0.8f; AmbienceVolume = 0.6f; FootstepVolume = 0.6f; VoiceVolume = 0.8f;
            Music = true; Footsteps = true; VoiceGender = 0;
            Difficulty = 1; AutoLoot = false; SwingSpeed = 1.6f; Haptics = true; AvatarCombatAnimations = true;
            ImmersiveCasting = false; VrHud = true; VrForearmBar = false; LowHealthVignette = true; VrTargetRange = 150f;
            FastTravel = false; GroundClutter = true; ClutterDensity = 1f; AmbientLife = true; TerrainSpecular = true; TerrainShine = 2.5f; WaterVisibility = 1.25f; UnlockAllFlightPaths = true; UnlockAllMounts = true; FlyAnywhere = true;
            HudScale = 1f; HudOffsetX = 0f; HudOffsetY = 0f; VrHudScale = 1f; VrHudOffsetX = 0f; VrHudOffsetY = 0f;   // stage S1
            ShowMinimap = true; MinimapZoom = 0;   // lead 2026-10-03: the minimap
            // iteration 11 (w0game): PLAN11 s2 defaults
            WowCursorArt = true; WowCursorFirstPerson = true;
            ShowBottomLeftBar = true; ShowBottomRightBar = true; ShowRightBar = true; ShowRightBar2 = true; LockActionBars = true; AlwaysShowActionBars = true;
            VrRows = true; VrRowsRemember = false; ImmersiveStrike = false; ImmersiveEating = false; SharpVrUi = true; VrQuestSize = 1f; ImmersiveBow = true; ShowMainHand = ShowOffHand = ShowRanged = true; WristSensitivity = 0.5f; VrMenuStyle = 1; StrikeRingBar = 0; StrikeRingDistance = 0.55f; StrikeRingSize = 1f; StrikeRingSpread = 1f; StrikeRingHeight = 0f;
            // lead 2026-10-04 (the user: "the Defaults completely break the armour, my friend pressed it and now he can't get his armour back
            // on"): Defaults wiped the whole Armour Adjustment fit, the Armour fit and the seat height (a player's own calibration, saved at
            // once) and turned immersive armour off. Those are now kept - the Armour page's "Reset this piece" resets a piece - and the
            // armour stays on (its default, Full).
            ShowForms = true; AvatarCombatStyle = 1; ImmersiveArmour = 2;
            PreviewTalents = true; WeaponEnchantGlows = true; GroundTargetAtScreenCentre = false;
            VrForcedMove = 0; TaxiMapAfterDiscovery = true; VrFacingAssist = false;
            NotifyChanged();
        }

        /// <summary>"key=value" lines, versioned. Unknown keys are ignored by Load, so older/newer saves still load.</summary>
        public static string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("wow_settings=1\n");
            B(sb, "nameplates", ShowNameplates); B(sb, "friendly_names", ShowFriendlyNames); B(sb, "combat_text", ShowCombatText);
            B(sb, "player_plates", ShowPlayerPlates); B(sb, "quest_markers", ShowQuestMarkers);
            F(sb, "ui_scale", UiScale); F(sb, "arm_ui_scale", ArmUiScale);
            B(sb, "spell_effects", SpellEffects); B(sb, "world_particles", WorldParticles); I(sb, "effects_quality", EffectsQuality);
            F(sb, "master_volume", MasterVolume); F(sb, "music_volume", MusicVolume); F(sb, "effects_volume", EffectsVolume);
            F(sb, "ambience_volume", AmbienceVolume); F(sb, "footstep_volume", FootstepVolume); F(sb, "voice_volume", VoiceVolume);
            B(sb, "music", Music); B(sb, "footsteps", Footsteps); I(sb, "voice_gender", VoiceGender);
            I(sb, "difficulty", Difficulty); B(sb, "auto_loot", AutoLoot); F(sb, "swing_speed", SwingSpeed); B(sb, "haptics", Haptics);
            B(sb, "avatar_combat_animations", AvatarCombatAnimations);
            B(sb, "immersive_casting", ImmersiveCasting); B(sb, "vr_hud", VrHud); B(sb, "vr_forearm_bar", VrForearmBar);
            B(sb, "low_health_vignette", LowHealthVignette); F(sb, "vr_target_range", VrTargetRange); B(sb, "fast_travel", FastTravel); B(sb, "unlock_all_flight_paths", UnlockAllFlightPaths); B(sb, "unlock_all_mounts", UnlockAllMounts); B(sb, "fly_anywhere", FlyAnywhere); SD(sb, "mount_display_cache", MountDisplayCache, 4096); SD(sb, "macros", MacroData, 12000); SD(sb, "auto_account_gm", AutoAccountGm, 1024);
            B(sb, "ground_clutter", GroundClutter); F(sb, "clutter_density", ClutterDensity); B(sb, "ambient_life", AmbientLife); B(sb, "terrain_specular", TerrainSpecular); F(sb, "terrain_shine", TerrainShine); F(sb, "water_visibility", WaterVisibility);
            B(sb, "online_mode", OnlineMode); S(sb, "server_host", ServerHost); I(sb, "server_port", ServerPort);
            B(sb, "remember_account", RememberAccount); S(sb, "online_account", RememberAccount ? OnlineAccount : "");
            B(sb, "online_manual", OnlineManual);
            F(sb, "hud_scale", HudScale); F(sb, "hud_offset_x", HudOffsetX); F(sb, "hud_offset_y", HudOffsetY);
            F(sb, "vr_hud_scale", VrHudScale); F(sb, "vr_hud_offset_x", VrHudOffsetX); F(sb, "vr_hud_offset_y", VrHudOffsetY);
            B(sb, "show_minimap", ShowMinimap); I(sb, "minimap_zoom", MinimapZoom);   // lead 2026-10-03
            // iteration 11 (w0game)
            B(sb, "wow_cursor_art", WowCursorArt); B(sb, "wow_cursor_first_person", WowCursorFirstPerson);
            B(sb, "show_bottom_left_bar", ShowBottomLeftBar); B(sb, "show_bottom_right_bar", ShowBottomRightBar); B(sb, "show_right_bar", ShowRightBar);
            B(sb, "show_right_bar_2", ShowRightBar2); B(sb, "lock_action_bars", LockActionBars); B(sb, "always_show_action_bars", AlwaysShowActionBars);
            B(sb, "vr_rows", VrRows); B(sb, "vr_rows_remember", VrRowsRemember); B(sb, "immersive_strike", ImmersiveStrike); B(sb, "immersive_eating", ImmersiveEating); B(sb, "sharp_vr_ui", SharpVrUi); B(sb, "immersive_bow", ImmersiveBow); B(sb, "show_main_hand", ShowMainHand); B(sb, "show_off_hand", ShowOffHand); B(sb, "show_ranged", ShowRanged); F(sb, "vr_quest_size", VrQuestSize); F(sb, "wrist_sensitivity", WristSensitivity); F(sb, "vr_menu_style", VrMenuStyle);
            I(sb, "strike_ring_bar", StrikeRingBar); F(sb, "strike_ring_distance", StrikeRingDistance); F(sb, "strike_ring_size", StrikeRingSize); F(sb, "strike_ring_spread", StrikeRingSpread); F(sb, "strike_ring_height", StrikeRingHeight);
            for (int ai = 0; ai < ArmourSlots; ai++)
            {
                B(sb, "armour_show_" + ai, ArmourShow[ai]); F(sb, "armour_x_" + ai, ArmourX[ai]); F(sb, "armour_y_" + ai, ArmourY[ai]);
                F(sb, "armour_z_" + ai, ArmourZ[ai]); F(sb, "armour_size_" + ai, ArmourSize[ai]); F(sb, "armour_tilt_" + ai, ArmourTilt[ai]);
                F(sb, "armour_ttop_" + ai, ArmourTaperTop[ai]); F(sb, "armour_tbot_" + ai, ArmourTaperBottom[ai]);
                F(sb, "armour_etz_" + ai, ArmourTopZ[ai]); F(sb, "armour_ety_" + ai, ArmourTopY[ai]); F(sb, "armour_ebz_" + ai, ArmourBottomZ[ai]); F(sb, "armour_eby_" + ai, ArmourBottomY[ai]); B(sb, "armour_hide_" + ai, ArmourHideAvatar[ai]);
            }
            B(sb, "armour_mirror", ArmourMirrorShoulders); B(sb, "helm_in_photos", HelmInPhotos); B(sb, "helm_hide_hair", HelmHideHair);
            F(sb, "mount_seat_height", MountSeatHeight);
            B(sb, "show_forms", ShowForms); I(sb, "avatar_combat_style", AvatarCombatStyle); I(sb, "immersive_armour", ImmersiveArmour); F(sb, "armour_fit", ArmourFit);
            B(sb, "avatar_defaults_115", true);   // lead 2026-10-04: the one-time move to the 11.5 defaults is done
            B(sb, "gear_select_default_144", true);   // lead 2026-11-04: the one-time move to Gear Select is done
            B(sb, "preview_talents", PreviewTalents); B(sb, "weapon_enchant_glows", WeaponEnchantGlows); B(sb, "ground_target_screen_centre", GroundTargetAtScreenCentre);
            I(sb, "vr_forced_move", VrForcedMove); B(sb, "taxi_map_after_discovery", TaxiMapAfterDiscovery); B(sb, "vr_facing_assist", VrFacingAssist);
            return sb.ToString();
        }

        /// <summary>Applies saved text (tolerant: bad lines are skipped). Returns the number of values read.</summary>
        public static int Load(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var map = new Dictionary<string, string>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            int n = 0;
            n += GB(map, "nameplates", ref ShowNameplates); n += GB(map, "friendly_names", ref ShowFriendlyNames);
            n += GB(map, "combat_text", ref ShowCombatText); n += GB(map, "player_plates", ref ShowPlayerPlates);
            n += GB(map, "quest_markers", ref ShowQuestMarkers);
            n += GF(map, "ui_scale", ref UiScale, 0.75f, 1.5f); n += GF(map, "arm_ui_scale", ref ArmUiScale, 0.75f, 1.5f);
            n += GB(map, "spell_effects", ref SpellEffects); n += GB(map, "world_particles", ref WorldParticles);
            n += GI(map, "effects_quality", ref EffectsQuality, 0, 2);
            n += GF(map, "master_volume", ref MasterVolume, 0f, 1f); n += GF(map, "music_volume", ref MusicVolume, 0f, 1f);
            n += GF(map, "effects_volume", ref EffectsVolume, 0f, 1f); n += GF(map, "ambience_volume", ref AmbienceVolume, 0f, 1f);
            n += GF(map, "footstep_volume", ref FootstepVolume, 0f, 1f); n += GF(map, "voice_volume", ref VoiceVolume, 0f, 1f);
            n += GB(map, "music", ref Music); n += GB(map, "footsteps", ref Footsteps); n += GI(map, "voice_gender", ref VoiceGender, 0, 1);
            n += GI(map, "difficulty", ref Difficulty, 0, 2); n += GB(map, "auto_loot", ref AutoLoot);
            n += GF(map, "swing_speed", ref SwingSpeed, 0.5f, 4f); n += GB(map, "haptics", ref Haptics);
            n += GB(map, "avatar_combat_animations", ref AvatarCombatAnimations);
            n += GB(map, "immersive_casting", ref ImmersiveCasting); n += GB(map, "vr_hud", ref VrHud); n += GB(map, "vr_forearm_bar", ref VrForearmBar);
            n += GB(map, "low_health_vignette", ref LowHealthVignette); n += GF(map, "vr_target_range", ref VrTargetRange, 20f, 300f);
            n += GB(map, "fast_travel", ref FastTravel); n += GB(map, "unlock_all_flight_paths", ref UnlockAllFlightPaths); n += GB(map, "unlock_all_mounts", ref UnlockAllMounts); n += GB(map, "fly_anywhere", ref FlyAnywhere); n += GSD(map, "mount_display_cache", ref MountDisplayCache, 4096); n += GSD(map, "macros", ref MacroData, 12000); n += GSD(map, "auto_account_gm", ref AutoAccountGm, 1024); n += GB(map, "ground_clutter", ref GroundClutter); n += GB(map, "terrain_specular", ref TerrainSpecular);
            n += GF(map, "clutter_density", ref ClutterDensity, 0.25f, 1.5f); n += GB(map, "ambient_life", ref AmbientLife); n += GF(map, "terrain_shine", ref TerrainShine, 0.25f, 4f); n += GF(map, "water_visibility", ref WaterVisibility, 0.5f, 2.5f);
            n += GB(map, "online_mode", ref OnlineMode); n += GS(map, "server_host", ref ServerHost); n += GI(map, "server_port", ref ServerPort, 1, 65535);
            n += GB(map, "remember_account", ref RememberAccount); n += GS(map, "online_account", ref OnlineAccount);
            bool savedManual = false; n += GB(map, "online_manual", ref savedManual);   // lead 2026-11-03: read but not used - the menu always opens on Log In with Somnium Account
            n += GF(map, "hud_scale", ref HudScale, HudScaleMin, HudScaleMax);
            n += GF(map, "hud_offset_x", ref HudOffsetX, 0f, HudOffsetMaxX); n += GF(map, "hud_offset_y", ref HudOffsetY, 0f, HudOffsetMaxY);
            n += GF(map, "vr_hud_scale", ref VrHudScale, HudScaleMin, HudScaleMax);
            n += GF(map, "vr_hud_offset_x", ref VrHudOffsetX, -VrHudOffsetMax, VrHudOffsetMax); n += GF(map, "vr_hud_offset_y", ref VrHudOffsetY, -VrHudOffsetMax, VrHudOffsetMax);
            n += GB(map, "show_minimap", ref ShowMinimap); n += GI(map, "minimap_zoom", ref MinimapZoom, 0, 5);   // lead 2026-10-03
            // iteration 11 (w0game)
            n += GB(map, "wow_cursor_art", ref WowCursorArt); n += GB(map, "wow_cursor_first_person", ref WowCursorFirstPerson);
            n += GB(map, "show_bottom_left_bar", ref ShowBottomLeftBar); n += GB(map, "show_bottom_right_bar", ref ShowBottomRightBar); n += GB(map, "show_right_bar", ref ShowRightBar);
            n += GB(map, "show_right_bar_2", ref ShowRightBar2); n += GB(map, "lock_action_bars", ref LockActionBars); n += GB(map, "always_show_action_bars", ref AlwaysShowActionBars);
            n += GB(map, "vr_rows", ref VrRows); n += GB(map, "vr_rows_remember", ref VrRowsRemember); n += GB(map, "immersive_strike", ref ImmersiveStrike); n += GB(map, "immersive_eating", ref ImmersiveEating); n += GB(map, "sharp_vr_ui", ref SharpVrUi); n += GB(map, "immersive_bow", ref ImmersiveBow); n += GB(map, "show_main_hand", ref ShowMainHand); n += GB(map, "show_off_hand", ref ShowOffHand); n += GB(map, "show_ranged", ref ShowRanged); n += GF(map, "vr_quest_size", ref VrQuestSize, 0.75f, 2f); n += GF(map, "wrist_sensitivity", ref WristSensitivity, 0f, 1f);
            { float ms = VrMenuStyle; n += GF(map, "vr_menu_style", ref ms, 0f, 1f); VrMenuStyle = (int)Math.Round(ms); }
            // lead 2026-11-04 (the user: "make the gear shift spells mode the default"): settings saved before 14.5 move ONCE to Gear Select;
            // a later choice of Twist wrist is kept
            bool gearMoved = false;
            if (GB(map, "gear_select_default_144", ref gearMoved) == 0 || !gearMoved)
            {
                if (VrMenuStyle != 1) WowLog.Info("Settings: VR spell menus moved to Gear Select once (the new default; Twist wrist can be chosen again in Settings).");
                VrMenuStyle = 1;
            }
            n += GI(map, "strike_ring_bar", ref StrikeRingBar, 0, 4); n += GF(map, "strike_ring_distance", ref StrikeRingDistance, 0.25f, 1.20f);
            n += GF(map, "strike_ring_size", ref StrikeRingSize, 0.5f, 2f); n += GF(map, "strike_ring_spread", ref StrikeRingSpread, 0.5f, 2f);
            n += GF(map, "strike_ring_height", ref StrikeRingHeight, -0.50f, 0.30f);
            for (int ai = 0; ai < ArmourSlots; ai++)
            {
                n += GB(map, "armour_show_" + ai, ref ArmourShow[ai]); n += GF(map, "armour_x_" + ai, ref ArmourX[ai], -0.30f, 0.30f);
                n += GF(map, "armour_y_" + ai, ref ArmourY[ai], -0.30f, 0.30f); n += GF(map, "armour_z_" + ai, ref ArmourZ[ai], -0.30f, 0.30f);
                n += GF(map, "armour_size_" + ai, ref ArmourSize[ai], 0.5f, 2f);
                n += GF(map, "armour_tilt_" + ai, ref ArmourTilt[ai], -45f, 45f);
                n += GF(map, "armour_ttop_" + ai, ref ArmourTaperTop[ai], 0.5f, 1.5f); n += GF(map, "armour_tbot_" + ai, ref ArmourTaperBottom[ai], 0.5f, 1.5f);
                n += GF(map, "armour_etz_" + ai, ref ArmourTopZ[ai], -ArmourEndMax, ArmourEndMax); n += GF(map, "armour_ety_" + ai, ref ArmourTopY[ai], -ArmourEndMax, ArmourEndMax);
                n += GF(map, "armour_ebz_" + ai, ref ArmourBottomZ[ai], -ArmourEndMax, ArmourEndMax); n += GF(map, "armour_eby_" + ai, ref ArmourBottomY[ai], -ArmourEndMax, ArmourEndMax);
                n += GB(map, "armour_hide_" + ai, ref ArmourHideAvatar[ai]);
            }
            n += GB(map, "armour_mirror", ref ArmourMirrorShoulders); n += GB(map, "helm_in_photos", ref HelmInPhotos); n += GB(map, "helm_hide_hair", ref HelmHideHair);
            n += GF(map, "mount_seat_height", ref MountSeatHeight, -0.30f, 0.50f);
            n += GB(map, "show_forms", ref ShowForms); n += GI(map, "avatar_combat_style", ref AvatarCombatStyle, 0, 1); n += GI(map, "immersive_armour", ref ImmersiveArmour, 0, 2);
            // lead 2026-10-04 (the user: "have Combat animation style full body by default and immersive armour on by default, full"):
            // settings saved before 11.5 move ONCE to the new defaults (WoW full body, Full armour); a later choice is kept.
            bool moved = false;
            if (GB(map, "avatar_defaults_115", ref moved) == 0 || !moved)
            {
                if (AvatarCombatStyle != 1 || ImmersiveArmour != 2)
                    WowLog.Info("Settings: moved to the 11.5 avatar defaults once - combat animation WoW (full body), immersive armour Full (were style " + AvatarCombatStyle + ", armour " + ImmersiveArmour + ")");
                AvatarCombatStyle = 1;
                ImmersiveArmour = 2;
            }
            n += GF(map, "armour_fit", ref ArmourFit, 0.5f, 1.4f);
            n += GB(map, "preview_talents", ref PreviewTalents); n += GB(map, "weapon_enchant_glows", ref WeaponEnchantGlows); n += GB(map, "ground_target_screen_centre", ref GroundTargetAtScreenCentre);
            n += GI(map, "vr_forced_move", ref VrForcedMove, 0, 2); n += GB(map, "taxi_map_after_discovery", ref TaxiMapAfterDiscovery); n += GB(map, "vr_facing_assist", ref VrFacingAssist);
            if (!RememberAccount) OnlineAccount = "";
            if (string.IsNullOrEmpty(ServerHost)) ServerHost = DefaultServerHost;   // an empty saved host (the old default) = not set
            NotifyChanged();
            return n;
        }

        static void B(StringBuilder sb, string k, bool v) { sb.Append(k).Append('=').Append(v ? "1" : "0").Append('\n'); }
        static void I(StringBuilder sb, string k, int v) { sb.Append(k).Append('=').Append(v.ToString(CultureInfo.InvariantCulture)).Append('\n'); }
        static void F(StringBuilder sb, string k, float v) { sb.Append(k).Append('=').Append(v.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n'); }

        /// <summary>A one-line text value for the settings text: control characters and '=' are dropped, at most 128 characters.</summary>
        public static string CleanText(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var b = new StringBuilder(v.Length);
            foreach (char c in v)
            {
                if (c < ' ' || c == '=' || c == (char)127) continue;
                b.Append(c);
                if (b.Length >= 128) break;
            }
            return b.ToString().Trim();
        }

        static void S(StringBuilder sb, string k, string v) { sb.Append(k).Append('=').Append(CleanText(v)).Append('\n'); }

        // Iteration 7 (lead): DATA values (the mount display cache, the GM flags) - printable characters only, a larger cap than CleanText.
        static void SD(StringBuilder sb, string k, string v, int max) { sb.Append(k).Append('=').Append(CleanData(v, max)).Append('\n'); }

        static int GSD(Dictionary<string, string> m, string k, ref string v, int max)
        {
            string s;
            if (!m.TryGetValue(k, out s)) return 0;
            v = CleanData(s, max);
            return 1;
        }

        public static string CleanData(string v, int max)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var b = new StringBuilder(Math.Min(v.Length, max));
            foreach (char c in v)
            {
                if (c < ' ' || c == '=' || c >= (char)127) continue;
                if (b.Length >= max) break;
                b.Append(c);
            }
            return b.ToString();
        }

        static int GS(Dictionary<string, string> m, string k, ref string v)
        {
            string s;
            if (!m.TryGetValue(k, out s)) return 0;
            v = CleanText(s);
            return 1;
        }

        static int GB(Dictionary<string, string> m, string k, ref bool v)
        {
            string s;
            if (!m.TryGetValue(k, out s)) return 0;
            if (s == "1") { v = true; return 1; }
            if (s == "0") { v = false; return 1; }
            return 0;
        }

        static int GI(Dictionary<string, string> m, string k, ref int v, int min, int max)
        {
            string s; int x;
            if (!m.TryGetValue(k, out s) || !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return 0;
            v = Math.Max(min, Math.Min(max, x));
            return 1;
        }

        static int GF(Dictionary<string, string> m, string k, ref float v, float min, float max)
        {
            string s; float x;
            if (!m.TryGetValue(k, out s) || !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return 0;
            if (float.IsNaN(x)) return 0;
            v = Math.Max(min, Math.Min(max, x));
            return 1;
        }
    }
}
