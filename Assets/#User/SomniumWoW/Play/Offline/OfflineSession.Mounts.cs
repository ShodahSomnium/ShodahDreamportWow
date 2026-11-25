using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// Iteration 7 (Rules): mounts in the offline game, AzerothCore's rules (PLAN7 s4.5). Summoning = CastSpell(mountSpellId): WoW's
    /// 1.5 s cast with its checks, then the mount aura (aura 78) on the player with its speed auras (32 / 207 / 58), the model from
    /// MountDisplays, AzerothCore's speed formula (Unit::UpdateSpeed), the pet sent away, and the forced dismounts (death, indoors, water,
    /// flying areas, other spells). The two testing switches build DERIVED sets that are never saved: the record keeps no mounts or auras,
    /// so the character always logs in on foot. Every text is WoW's own (GlobalStrings.lua key and line in a comment) unless marked ours.
    /// </summary>
    public sealed partial class OfflineSession
    {
        /// <summary>Testing switch (user, 2026-09-27: "a way to test them"): every summonable mount of the character's faction and class is
        /// known, riding counts as 300 + Cold Weather Flying. A DERIVED set, never saved. WowGame copies WowSettings.UnlockAllMounts.</summary>
        public static bool UnlockAllMounts = true;

        /// <summary>Testing switch: flying mounts may be summoned and fly in any zone offline (all 8 starting zones are no-fly in 3.3.5a).
        /// Off = AzerothCore's rule. WowGame copies WowSettings.FlyAnywhere.</summary>
        public static bool FlyAnywhere = true;

        /// <summary>Ours: the rider's own collision height in yards (the Somnium avatar has no WoW model). 2.031 = CreatureModelData
        /// CollisionHeight measured on the player models (PLAN7 s4.2), so the horse gives AzerothCore's 1.866 + 2.031 x 0.5 = 2.88 yd.</summary>
        public const float RiderCollisionHeight = 2.031f;

        // AzerothCore SharedDefines.h / SpellDefines.h values used here.
        private const uint Attr0AllowWhileMounted = 0x01000000u;   // SPELL_ATTR0_ALLOW_WHILE_MOUNTED "Can be cast while mounted"
        private const uint Attr0OnlyOutdoors = 0x00008000u;        // SPELL_ATTR0_ONLY_OUTDOORS
        private const uint Attr0NoAuraCancel = 0x80000000u;        // SPELL_ATTR0_NO_AURA_CANCEL "Aura cannot be cancelled"
        private const uint Attr7IgnoresColdWeather = 0x00400000u;  // SPELL_ATTR7_IGNORES_COLD_WEATHER_FLYING_REQUIREMENT (loaner mounts)
        private const int AuraInterruptMount = 0x20000;            // AURA_INTERRUPT_FLAG_MOUNT (Unit::Mount removes these)
        private const int AuraInterruptNotAboveWater = 0x80;       // AURA_INTERRUPT_FLAG_NOT_ABOVEWATER "removed by entering water" (flyers)
        private const int SpellInterruptAbortOnDmg = 0x10;         // SPELL_INTERRUPT_FLAG_ABORT_ON_DMG (Unit.cpp 1313-1317: damage interrupts the cast)
        private const float SwimDepthFactor = 0.75f;               // Unit.cpp 4217-4219 "exact mirror of client logic": level - z > collision height x 0.75
        // Aura types of AC's Unit::UpdateSpeed (SpellAuraDefines.h) not in SpellFields.
        private const int AuraModSpeedAlways = 129, AuraModMountedSpeedAlways = 130, AuraModSpeedNotStack = 171, AuraModMountedSpeedNotStack = 172;
        private const int AuraModIncreaseFlightSpeed = 206, AuraModMountedFlightSpeedAlways = 209, AuraModFlightSpeedNotStacking = 210;
        private const int AuraModFlightSpeedMountedNotStacking = 211, AuraModMinimumSpeed = 305;
        private const int SeaTurtleSpell = 64731;                   // Unit.cpp UpdateSpeed: "sea turtle only, it is not affected by any increasing / decreasing effects"

        private PlayerMotion _motion;
        private PlayerPlace _place;
        private bool _placeUnknownLogged, _placeFirstLogged, _wasOutdoor = true, _flyUnknownLogged;
        private int _placeArea = -1;
        private OfflineAura _mountAura;
        private MountInfo _mount;
        private bool _petAwayForMount;
        private DismountReason _dismountReason = DismountReason.Ended;
        private bool _dismounting;
        private readonly HashSet<int> _unlockedMounts = new HashSet<int>();
        private readonly List<SpellInfo> _mountSpells = new List<SpellInfo>();       // parallel to _player.Mounts
        private readonly List<MountInfo> _mountInfos = new List<MountInfo>();        // parallel to _player.Mounts
        private readonly Dictionary<int, float> _expectedCollision = new Dictionary<int, float>();   // mount spell -> yards (the cast's water check)
        private readonly Dictionary<int, float> _mountCollision = new Dictionary<int, float>();   // display id -> yards
        private bool _mountsBuilt, _mountsBuiltUnlocked, _flyAnywhereApplied;
        private int _mountPick;
        /// <summary>Ours: how often the Mounts window's usable flags are refreshed (seconds of session time).</summary>
        private const double MountViewsInterval = 0.1;
        private double _mountViewsNext;
        private bool _mountViewsMounted;
        private int _mountViewsCast = -1;

        // ------------------------------------------------------------------ setup

        /// <summary>Called by the constructor before InitPlayer: the catalogue and the area tables (cached; the loading worker usually read them).</summary>
        private void LoadMountData()
        {
            if (_files == null) return;
            MountCatalog.Load(_files);
            MountAreaRules.Load(_files);
        }

        /// <summary>Called by the constructor after InitPlayer: the Mounts list and the switches as they are now.</summary>
        private void InitMounts()
        {
            // AC ObjectMgr::ChooseDisplayId picks one of an entry's models at random (by probability); offline a stable hash of the
            // character's id picks, so a character keeps its model (FNV-1a; string.GetHashCode is not stable across runs).
            uint h = 2166136261u;
            string id = _rec.Id ?? _rec.Name ?? "";
            for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
            _mountPick = (int)(h & 0x7fffffffu);
            _flyAnywhereApplied = FlyAnywhere;
            RefreshMounts();
            RecalcSpeeds();
            WowLog.Info("Mounts: catalogue " + MountCatalog.Summary + "; fly anywhere (testing) " + (FlyAnywhere ? "on" : "off") + "; collision height = mount height + "
                + RiderCollisionHeight.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " x 0.5 yd (ours: the rider's height, the avatar has no WoW model)");
        }

        /// <summary>The Mounts list (WoW's Mounts pane, by name): the testing set while UnlockAllMounts is on, plus mount spells the record knows.</summary>
        private void RefreshMounts()
        {
            bool on = UnlockAllMounts;
            var faction = WowRaces.FactionOf(_rec.Race);
            _unlockedMounts.Clear();
            var list = new List<MountInfo>();
            if (on)
                foreach (var m in MountCatalog.ForCharacter(faction, _rec.Class, _rec.Race)) { _unlockedMounts.Add(m.SpellId); list.Add(m); }
            foreach (int sid in _rec.Spells)
            {
                var m = MountCatalog.Find(sid);
                if (m != null && !_unlockedMounts.Contains(sid)) list.Add(m);
            }
            list.Sort((a, b) => { int k = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); return k != 0 ? k : a.SpellId.CompareTo(b.SpellId); });

            _player.Mounts.Clear();
            _mountSpells.Clear();
            _mountInfos.Clear();
            var ctx = TextContext();
            foreach (var m in list)
            {
                var s = _db.Find(m.SpellId);
                if (s == null) continue;
                var v = new AbilityView();
                v.SpellId = s.Id;
                v.Name = s.Name;
                v.Rank = s.Rank;
                v.IconPath = s.IconPath;
                v.Description = SpellText.Resolve(_db, s, s.Description, ctx);
                PowerType pt;
                v.Cost = CostOf(s, out pt);
                v.CostType = pt;
                v.CastTime = s.CastTimeMs / 1000f;
                v.Cooldown = Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs) / 1000f;
                v.LearnedAtLevel = Math.Max(1, s.SpellLevel);
                v.IsMount = true;
                v.Tooltip = BuildTooltip(s, v);
                _player.Mounts.Add(v);
                _mountSpells.Add(s);
                _mountInfos.Add(m);
            }

            // Riding (skill 762): the testing switch counts as Artisan (300) + Cold Weather Flying; otherwise what the record knows.
            int riding = 0;
            if (_rec.Spells.Contains(MountCatalog.ApprenticeRiding)) riding = 75;
            if (_rec.Spells.Contains(MountCatalog.JourneymanRiding)) riding = 150;
            if (_rec.Spells.Contains(MountCatalog.ExpertRiding)) riding = 225;
            if (_rec.Spells.Contains(MountCatalog.ArtisanRiding)) riding = 300;
            bool cwf = _rec.Spells.Contains(MountCatalog.ColdWeatherFlying);
            _player.RidingSkill = on ? 300 : riding;
            _player.ColdWeatherFlying = on || cwf;

            string who = WowRaces.Name(_files, _rec.Race) + " " + ClassName(_rec.Class);
            if (on)
                WowLog.Info("Mounts: " + _unlockedMounts.Count + " of " + MountCatalog.All.Count + " unlocked for a " + who + " (testing switch); riding 300 + Cold Weather Flying (derived, not saved)"
                    + (list.Count > _unlockedMounts.Count ? "; " + (list.Count - _unlockedMounts.Count) + " more known by the character" : ""));
            else
                WowLog.Info("Mounts: testing switch off - " + list.Count + " mount spell" + (list.Count == 1 ? "" : "s") + " known by this " + who + " (riding " + riding
                    + (cwf ? " + Cold Weather Flying" : "") + "); offline there are no riding trainers or mount vendors");
            _mountsBuilt = true;
            _mountViewsNext = 0;
            _mountsBuiltUnlocked = on;
        }

        private static string ClassName(WowClass c) { return c == WowClass.DeathKnight ? "Death Knight" : c.ToString(); }

        /// <summary>The character may summon this mount now (the testing set, or a mount spell the record knows).</summary>
        private bool KnowsMount(int spellId)
        {
            return MountCatalog.IsMount(spellId) && (_unlockedMounts.Contains(spellId) || _rec.Spells.Contains(spellId));
        }

        // ------------------------------------------------------------------ IWowSession (iteration 7)

        public void Dismount()
        {
            if (!_player.Mounted) return;
            Dismount(DismountReason.Chosen, null);
        }

        /// <summary>Right-click on a buff: positive, not passive, not SPELL_ATTR0_NO_AURA_CANCEL (the CMSG_CANCEL_AURA rules); the mount
        /// aura = Dismount; spell 0 = the food / drink "aura".</summary>
        public void CancelAura(int spellId)
        {
            if (_player.Mounted && spellId == _player.MountSpellId) { Dismount(DismountReason.Chosen, null); return; }
            if (spellId == 0) { if (_eating != null) CancelConsumable(null); return; }
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                var au = _me.Auras[a];
                if (au.Spell.Id != spellId || au.Harmful || au.Hidden || au.FromForm || au.Spell.IsPassive) continue;
                if ((au.Spell.Attributes[0] & Attr0NoAuraCancel) != 0) return;
                if (_channel != null && au.Spell == _channel) { EndChannel("Interrupted"); return; }
                RemoveAuraAt(_me, a);
                WowLog.Info("OfflineSession: aura " + spellId + " '" + au.Spell.Name + "' cancelled by the player");
                return;
            }
        }

        public void SetPlayerMotion(PlayerMotion motion)
        {
            _motion = motion;
        }

        public void SetPlayerPlace(PlayerPlace place)
        {
            _place = place;
            _mountViewsNext = 0;                              // the Mounts window follows a new spot at once
            if (!place.Known)
            {
                if (!_placeUnknownLogged) { _placeUnknownLogged = true; WowLog.Info("Mounts: no place sample yet - the spot counts as outdoors and dry until one arrives"); }
            }
            else if (!_placeFirstLogged)
            {
                _placeFirstLogged = true;
                WowLog.Info("Mounts: first place sample - " + (place.Outdoors ? "outdoors" : "indoors") + ", area " + place.AreaId + " '" + MountAreaRules.AreaName(place.AreaId) + "'"
                    + (place.LiquidDepth > 0f ? ", liquid " + place.LiquidDepth.ToString("0.00") + " yd" : "") + (string.IsNullOrEmpty(place.Where) ? "" : " (" + place.Where + ")"));
            }
            if (_player.Dead) return;

            // Player.cpp 5895-5905: `if (sWorld->getBoolConfig(CONFIG_VMAP_INDOOR_CHECK) && _wasOutdoor != isOutdoor)` removes the auras whose
            // spell has SPELL_ATTR0_ONLY_OUTDOORS when going indoors (vmap.enableIndoorCheck = 1 by default, worldserver.conf.dist 1377-1382).
            bool outdoors = !place.Known || place.Outdoors;
            if (outdoors != _wasOutdoor)
            {
                _wasOutdoor = outdoors;
                if (!outdoors) RemoveOutdoorsOnlyAuras();
            }

            // PlayerUpdates.cpp 1894-1906 UpdateAreaDependentAuras: an area change removes every aura whose CheckLocation (non-strict) fails.
            int area = place.Known ? place.AreaId : 0;
            if (area != _placeArea)
            {
                _placeArea = area;
                CheckFlyingArea("area " + area + " '" + MountAreaRules.AreaName(area) + "'");
            }
            CheckWater();
            UpdateCanFly();
        }

        /// <summary>Lead decision D13: offline there is no server to run GM commands - the testing switches do the same job.</summary>
        public void RunGmCommand(string command)
        {
            EmitInfo("Offline: use Settings > Gameplay > Unlock all mounts / Fly anywhere");   // ours (lead decision D13)
            WowLog.Info("Mounts: GM command '" + (command ?? "") + "' ignored offline (the testing switches replace it)");
        }

        // ------------------------------------------------------------------ casting a mount (CastSpell routes mount spells here)

        /// <summary>Mount spells in AzerothCore's order (Spell::CheckCast / prepare, PLAN7 s4.5). Casting any mount while mounted dismounts.</summary>
        private void CastMount(SpellInfo s)
        {
            var m = MountCatalog.Find(s.Id);
            if (m == null) return;
            if (_player.Mounted) { Dismount(DismountReason.Chosen, null); return; }   // the pane's Mount / Dismount rule (AC would answer NOT_MOUNTED)
            if (m.Excluded != null) { CastFailed(s.Id, m.Name + ": " + m.Excluded); return; }   // ours: never offered offline (the window's footer lists them)
            string why = CheckMountCast(m, s, true);
            if (why != null) { CastFailed(s.Id, why); return; }
            CancelConsumable(null);
            if (s.CastTimeMs <= 0) { StartGcd(s); BreakStealthFor(s); Emit(GameEventType.CastSucceeded, PlayerGuid, PlayerGuid, 0, s.Id, false, s.Name); MountUp(m, s); return; }
            _casting = s;
            _castHold = false;
            _castTargetGuid = PlayerGuid;
            _castElapsed = 0f;
            _castTotal = s.CastTimeMs / 1000f / Math.Max(0.1f, _me.AuraMultiplier(SpellAura.MeleeSlow, 0));   // as every cast (Berserking haste)
            _castStartPos = _me.Pos;
            StartGcd(s);
            BreakStealthFor(s);
            Emit(GameEventType.CastStarted, PlayerGuid, PlayerGuid, s.CastTimeMs, s.Id, false, s.Name);
        }

        /// <summary>The cast bar of a mount spell is full (StepCasting): the checks once more (AC Spell::cast re-runs CheckCast), then the mount.</summary>
        private void FinishMountCast(SpellInfo s)
        {
            var m = MountCatalog.Find(s.Id);
            if (m == null) return;
            string why = CheckMountCast(m, s, false);
            if (why != null) { CastFailed(s.Id, why); return; }
            Emit(GameEventType.CastSucceeded, PlayerGuid, PlayerGuid, 0, s.Id, false, s.Name);
            MountUp(m, s);
        }

        /// <summary>Why this mount cannot be summoned now (null = go). checkTimers false = the end of the cast (no busy / GCD / moving checks).</summary>
        private string CheckMountCast(MountInfo m, SpellInfo s, bool checkTimers)
        {
            if (_player.Dead) return "You are dead";                               // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
            if (_player.OnTaxi) return "You are in flight";                         // SPELL_FAILED_NOT_ON_TAXI (7110)
            if (_me.Stunned) return "Can't do that while stunned";                  // as CheckCast
            if (_me.Feared) return "Can't do that while feared";
            if (_me.Confused) return "Can't do that while confused";
            if (checkTimers)
            {
                if (_casting != null || _held != null || (_channel != null && _channel.Id == s.Id)) return "Another action is in progress";
                if ((_gcd > 0f && s.StartRecoveryTimeMs > 0) || CooldownLeft(s) > 0f) return "Spell is not ready yet";   // SPELL_FAILED_NOT_READY (offline keeps its text)
            }
            if (m.OutdoorsOnly && !_place.Known && checkTimers && !_placeUnknownLogged)
            {
                _placeUnknownLogged = true;
                WowLog.Info("Mounts: no place sample yet - the spot counts as outdoors (mounting allowed)");
            }
            if (m.OutdoorsOnly && _place.Known && !_place.Outdoors) return "Can only use outside";    // SPELL_FAILED_ONLY_OUTDOORS (7143)
            if (CheckShapeshift(s) != null) return "You can't mount while shapeshifted.";           // ERR_MOUNT_SHAPESHIFTED (3273)
            if (s.CantUseInCombat && _inCombat) return "You are in combat";                         // SPELL_FAILED_AFFECTING_COMBAT (6941)
            if (_loot != null) return "You can't mount while looting!";                             // ERR_MOUNT_LOOTING (3268)
            if (checkTimers && _moving) return "Can't do that while moving";                        // SPELL_FAILED_MOVING (7083)
            if (m.OnlyFlyingAreas && !FlyAnywhere && !CanFlyHereNow(s, true)) return "You are in the wrong zone.";   // SPELL_FAILED_INCORRECT_AREA (7065)
            if ((m.AuraInterruptFlags & AuraInterruptNotAboveWater) != 0 && _place.Known && _place.LiquidDepth > 0f && InDeepWater(ExpectedCollisionHeight(m)))
                return "Cannot use while swimming";                                                  // SPELL_FAILED_ONLY_ABOVEWATER (7136)
            return null;
        }

        /// <summary>Aura 78 lands (AC HandleAuraMounted -> Unit::Mount): model, speeds, can-fly, collision height, the pet sent away,
        /// AURA_INTERRUPT_FLAG_MOUNT auras removed, the Mounted event.</summary>
        private void MountUp(MountInfo m, SpellInfo s)
        {
            StopAttack();                                   // SPELL_ATTR0_CANCELS_AUTO_ATTACK_COMBAT 0x100000 on the mount spells
            if (_autoShot) SetAutoShot(false);
            _nextSwing = null;
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= _me.Auras.Count) continue;
                var au = _me.Auras[a];
                if ((au.Spell.AuraInterruptFlags & AuraInterruptMount) != 0 && !au.FromForm && !au.Spell.IsPassive) RemoveAuraAt(_me, a);
            }
            _petAwayForMount = _pet != null;
            if (_petAwayForMount) DismissPet(false);       // Unit::Mount: player->UnsummonPetTemporaryIfAny(); back at Dismount (RestorePet)

            var aura = new OfflineAura { Spell = s, Caster = PlayerGuid, Remaining = -1f, Duration = 0f };
            aura.View = new AuraView { SpellId = s.Id, Name = s.Name, IconPath = s.IconPath, CasterGuid = PlayerGuid };
            for (int i = 0; i < 3; i++) { var e = s.Effects[i]; if (e != null && e.IsAura) aura.Amount[i] = e.MaxValue; }   // DieSides 1: BasePoints + 1
            _me.Auras.Add(aura);
            _mountAura = aura;
            _mount = m;
            _aurasChanged = true;
            EmitAuraApplied(aura, _me);

            MountDisplaySource src;
            var faction = WowRaces.FactionOf(_rec.Race);
            int display = MountDisplays.Resolve(m.CreatureEntry, _mountPick, out src);
            if (display == 0)
            {
                display = MountDisplays.StandIn(m.Kind, faction);
                src = MountDisplaySource.StandIn;
                EmitInfo(m.Name + " (stand-in model)");                                      // ours: the realm's model for this mount is not known
            }
            _player.Mounted = true;
            _player.MountSpellId = s.Id;
            _player.MountCreatureEntry = m.CreatureEntry;
            _player.MountKind = m.Kind;
            _player.MountDisplaySource = src;
            _player.MountDisplayId = display;
            _player.MountCounter++;
            _player.CollisionHeight = CollisionHeightOf(display);
            RecalcSpeeds();
            UpdateCanFly();
            Emit(GameEventType.Mounted, PlayerGuid, PlayerGuid, display, s.Id, false, m.Name);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            WowLog.Info("Mounts: cast " + s.Id + " '" + m.Name + "' -> mounted display " + display + " (" + SourceName(src) + "), run " + _player.RunSpeed.ToString("0.00", inv) + " yd/s, flight "
                + (m.FlightPct > 0 ? _player.FlightSpeed.ToString("0.00", inv) + " yd/s" : "-") + (m.SwimPct > 0 ? ", swim " + _player.SwimSpeed.ToString("0.00", inv) + " yd/s" : "")
                + ", collision " + _player.CollisionHeight.ToString("0.00", inv) + " yd, CanFly " + (_player.CanFly ? "yes" : "no")
                + (_petAwayForMount ? ", pet sent away" : "") + ", entry " + m.CreatureEntry);
        }

        /// <summary>Takes the mount aura away (the aura removal calls MountAuraGone) with the reason; text = WoW's message, shown as Info too.</summary>
        private void Dismount(DismountReason reason, string text)
        {
            if (!_player.Mounted) return;
            _dismountReason = reason;
            int index = _mountAura != null ? _me.Auras.IndexOf(_mountAura) : -1;
            if (index >= 0) RemoveAuraAt(_me, index);     // -> MountAuraGone
            else MountAuraGone();
            _dismountReason = DismountReason.Ended;
            if (text != null) EmitInfo(text);
            // Dismounted's Text for the views (the Info above is the on-screen line).
        }

        /// <summary>RemoveAuraAt removed the mount aura (Dismount, or any rule that removes auras): the player is on foot again.</summary>
        private void MountAuraGone()
        {
            if (_dismounting || !_player.Mounted) return;
            _dismounting = true;
            try
            {
                var reason = _dismountReason;
                int spell = _player.MountSpellId;
                _mountAura = null;
                _mount = null;
                _player.Mounted = false;
                _player.MountSpellId = 0;
                _player.MountCreatureEntry = 0;
                _player.MountKind = MountKind.None;
                _player.MountDisplaySource = MountDisplaySource.None;
                if (!_player.OnTaxi) _player.MountDisplayId = 0;
                _player.CanFly = false;
                _player.CollisionHeight = 0f;
                _aurasChanged = true;
                RecalcSpeeds();
                string text = reason == DismountReason.Spell ? "You dismount before continuing." : null;   // ERR_MOUNT_FORCEDDISMOUNT (GlobalStrings.lua:3266)
                Emit(GameEventType.Dismounted, PlayerGuid, PlayerGuid, (int)reason, spell, false, text);
                WowLog.Info("Mounts: dismounted (" + reason + ")" + (_motion.Driven && _motion.Flying ? " in the air" : ""));
                if (_petAwayForMount)
                {
                    _petAwayForMount = false;
                    if (!_player.Dead) RestorePet();             // Unit::Dismount: ResummonPetTemporaryUnSummonedIfAny (a dead player's pet stays away)
                }
            }
            finally { _dismounting = false; }
        }

        /// <summary>Casting another spell while mounted (PLAN7 s4.5): SPELL_ATTR0_ALLOW_WHILE_MOUNTED and passive spells stay mounted; in the
        /// air it is refused (AC Spell.cpp 6209-6216 SPELL_FAILED_NOT_MOUNTED - the client's "Auto Dismount in Flight" is off by default); on
        /// the ground the client dismounts first (ERR_MOUNT_FORCEDDISMOUNT). null = go on.</summary>
        private string MountedCastGate(SpellInfo s)
        {
            if (!_player.Mounted || s == null) return null;
            if (s.IsPassive || (s.Attributes[0] & Attr0AllowWhileMounted) != 0 || MountCatalog.IsMount(s.Id)) return null;
            if (_motion.Driven && _motion.Flying) return "You are mounted.";               // SPELL_FAILED_NOT_MOUNTED (GlobalStrings.lua:7104)
            Dismount(DismountReason.Spell, "You dismount before continuing.");            // ERR_MOUNT_FORCEDDISMOUNT (3266)
            return null;
        }

        /// <summary>Potions, food and drink are spells in WoW: the MountedCastGate rule for UseItem. False = refused (Error shown).</summary>
        private bool MountedItemGate()
        {
            if (!_player.Mounted) return true;
            if (_motion.Driven && _motion.Flying) { EmitError("You are mounted."); return false; }   // SPELL_FAILED_NOT_MOUNTED (GlobalStrings.lua:7104)
            Dismount(DismountReason.Spell, "You dismount before continuing.");                     // ERR_MOUNT_FORCEDDISMOUNT (3266)
            return true;
        }

        // ------------------------------------------------------------------ forced dismounts by place

        private void RemoveOutdoorsOnlyAuras()
        {
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= _me.Auras.Count) continue;
                var au = _me.Auras[a];
                if ((au.Spell.Attributes[0] & Attr0OnlyOutdoors) == 0 || au.FromForm || au.Spell.IsPassive) continue;
                if (au == _mountAura) { Dismount(DismountReason.Indoors, null); continue; }
                RemoveAuraAt(_me, a);
            }
        }

        /// <summary>UpdateAreaDependentAuras for a flyer with SPELL_ATTR4_ONLY_FLYING_AREAS when Fly anywhere is off.</summary>
        private void CheckFlyingArea(string why)
        {
            if (!_player.Mounted || _mount == null || _mountAura == null || FlyAnywhere || !_mount.OnlyFlyingAreas) return;
            if (CanFlyHereNow(_mountAura.Spell, false)) return;
            WowLog.Info("Mounts: " + _mount.Name + " may not fly here (" + why + ", AzerothCore UpdateAreaDependentAuras)");
            Dismount(DismountReason.FlyingArea, null);
        }

        /// <summary>AURA_INTERRUPT_FLAG_NOT_ABOVEWATER auras (the flyers) go in deep liquid: level - z > 0.75 x collision height (Unit.cpp 4217-4219).</summary>
        private void CheckWater()
        {
            if (!_place.Known || _place.LiquidDepth <= 0f) return;
            float ch = _player.Mounted && _player.CollisionHeight > 0f ? _player.CollisionHeight : RiderCollisionHeight;
            if (!InDeepWater(ch)) return;
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= _me.Auras.Count) continue;
                var au = _me.Auras[a];
                if ((au.Spell.AuraInterruptFlags & AuraInterruptNotAboveWater) == 0 || au.FromForm || au.Spell.IsPassive) continue;
                if (au == _mountAura)
                {
                    WowLog.Info("Mounts: in the water (liquid " + _place.LiquidDepth.ToString("0.00") + " yd > 0.75 x " + ch.ToString("0.00") + " yd)");
                    Dismount(DismountReason.Water, null);
                    continue;
                }
                RemoveAuraAt(_me, a);
            }
        }

        private bool InDeepWater(float collisionHeightYards)
        {
            return _place.Known && _place.LiquidDepth > 0f && _place.LiquidDepth > SwimDepthFactor * collisionHeightYards;
        }

        /// <summary>MountAreaRules.CanFlyHere at the current spot, with SPELL_ATTR7_IGNORES_COLD_WEATHER_FLYING_REQUIREMENT folded in.
        /// No place sample yet: "no" (all 8 offline starting zones are no-fly in 3.3.5a - measured - so this is the true answer there; logged once).</summary>
        private bool CanFlyHereNow(SpellInfo s, bool strict)
        {
            if (!_place.Known)
            {
                if (!_flyUnknownLogged) { _flyUnknownLogged = true; WowLog.Info("Mounts: flying-area rule asked before the first place sample - answering 'no' (every offline zone is a no-fly zone)"); }
                return false;
            }
            bool cwf = _player.ColdWeatherFlying || (s != null && (s.Attributes[7] & Attr7IgnoresColdWeather) != 0);
            return MountAreaRules.CanFlyHere(_zone.MapId, _place.AreaId, cwf, strict);
        }

        /// <summary>PlayerState.CanFly: a flight-speed aura turns flying on (AC HandleAuraModIncreaseFlightSpeed -> SetCanFly); flyers with
        /// ONLY_FLYING_AREAS may only fly where CheckLocation (non-strict) allows it, unless Fly anywhere (testing) is on.</summary>
        private void UpdateCanFly()
        {
            bool can = _player.Mounted && _mount != null && _mount.FlightPct > 0
                && (FlyAnywhere || !_mount.OnlyFlyingAreas || (_mountAura != null && CanFlyHereNow(_mountAura.Spell, false)));
            if (can != _player.CanFly && _player.Mounted) WowLog.Info("Mounts: can fly " + (can ? "yes" : "no") + (FlyAnywhere ? " (fly anywhere, testing)" : ""));
            _player.CanFly = can;
        }

        // ------------------------------------------------------------------ per frame (UpdateViews)

        private void UpdateMountViews()
        {
            if (!_mountsBuilt || _mountsBuiltUnlocked != UnlockAllMounts)
            {
                bool was = _mountsBuilt;
                RefreshMounts();
                if (was && _player.Mounted && !KnowsMount(_player.MountSpellId))
                {
                    WowLog.Info("Mounts: 'Unlock all mounts' switched off while riding a mount the character does not know");
                    Dismount(DismountReason.Ended, null);
                }
            }
            if (_flyAnywhereApplied != FlyAnywhere)
            {
                _flyAnywhereApplied = FlyAnywhere;
                WowLog.Info("Mounts: fly anywhere (testing) " + (FlyAnywhere ? "on" : "off - AzerothCore's flying areas apply"));
                CheckFlyingArea("the testing switch was turned off");
                UpdateCanFly();
            }
            RecalcSpeeds();

            // The Mounts window's Usable / UnusableReason (up to ~270 buttons): 10 times a second (ours), and at once when the ride or a cast changes.
            int castId = _casting != null ? _casting.Id : 0;
            if (_time < _mountViewsNext && _player.Mounted == _mountViewsMounted && castId == _mountViewsCast) return;
            _mountViewsNext = _time + MountViewsInterval;
            _mountViewsMounted = _player.Mounted;
            _mountViewsCast = castId;
            for (int i = 0; i < _player.Mounts.Count && i < _mountSpells.Count && i < _mountInfos.Count; i++)
            {
                var v = _player.Mounts[i];
                var s = _mountSpells[i];
                var m = _mountInfos[i];
                string why;
                if (_player.Mounted) why = _player.Dead ? "You are dead" : null;   // any mount button dismounts
                else why = m == null ? "Unknown spell" : m.Excluded ?? CheckMountCast(m, s, true);
                v.Usable = why == null;
                v.UnusableReason = why;
                v.CooldownRemaining = Math.Max(CooldownLeft(s), s.StartRecoveryTimeMs > 0 ? _gcd : 0f);
                v.Active = (_player.Mounted && _player.MountSpellId == s.Id) || (_casting != null && _casting.Id == s.Id);
            }
        }

        // ------------------------------------------------------------------ speeds (AzerothCore Unit::UpdateSpeed, Unit.cpp 10876-11044)

        private void RecalcSpeeds()
        {
            var p = _player;
            bool mounted = p.Mounted;
            float slowPct = MaxNegativeAura(SpellAura.ModDecreaseSpeed);    // "Apply strongest slow aura mod to speed" (all move types)
            int minPct = MaxPositiveAura(AuraModMinimumSpeed);

            // MOVE_RUN
            float run = mounted
                ? Rate(MaxPositiveAura(MountCatalog.AuraMountedSpeed), TotalAuraMultiplier(AuraModMountedSpeedAlways), 1f + MaxPositiveAura(AuraModMountedSpeedNotStack) / 100f)
                : Rate(MaxPositiveAura(SpellAura.ModIncreaseSpeed), TotalAuraMultiplier(AuraModSpeedAlways), 1f + MaxPositiveAura(AuraModSpeedNotStack) / 100f);
            // MOVE_SWIM: the Sea Turtle's aura alone sets it ("not affected by any increasing / decreasing effects"), else the strongest aura 58.
            float swim;
            int turtle;
            if (SeaTurtleSwim(out turtle)) swim = 1f + turtle / 100f;
            else swim = Finish(Rate(StrongestSwimAura(), 1f, 1f), slowPct, minPct);
            // MOVE_FLIGHT
            float flight = mounted
                ? Rate(MaxPositiveAura(MountCatalog.AuraMountedFlightSpeed), TotalAuraMultiplier(AuraModMountedFlightSpeedAlways), 1f + MaxPositiveAura(AuraModFlightSpeedMountedNotStacking) / 100f)
                : Rate(MaxPositiveAura(AuraModIncreaseFlightSpeed), TotalAuraModifier(208), 1f + MaxPositiveAura(AuraModFlightSpeedNotStacking) / 100f);   // 208 SPELL_AURA_MOD_FLIGHT_SPEED_ALWAYS (AC sums it here)
            float back = Finish(1f, slowPct, minPct);   // RUN_BACK / SWIM_BACK / FLIGHT_BACK / WALK: "Only apply debuffs"

            p.RunSpeed = MountCatalog.BaseRun * Finish(run, slowPct, minPct);
            p.WalkSpeed = MountCatalog.BaseWalk * back;
            p.RunBackSpeed = MountCatalog.BaseRunBack * back;
            p.SwimSpeed = MountCatalog.BaseSwim * swim;
            p.SwimBackSpeed = MountCatalog.BaseSwimBack * back;
            p.FlightSpeed = MountCatalog.BaseFlight * Finish(flight, slowPct, minPct);
            p.FlightBackSpeed = MountCatalog.BaseFlightBack * back;
            p.TurnRate = MountCatalog.BaseTurnRate;
        }

        /// <summary>`float speed = std::max(non_stack_bonus, stack_bonus); if (main_speed_mod) AddPct(speed, main_speed_mod);`</summary>
        private static float Rate(int mainPct, float stack, float nonStack)
        {
            float speed = Math.Max(nonStack, stack);
            if (mainPct != 0) speed *= 1f + mainPct / 100f;
            return speed;
        }

        /// <summary>The slow (strongest SPELL_AURA_MOD_DECREASE_SPEED) and SPELL_AURA_MOD_MINIMUM_SPEED; a rate never goes below 0.</summary>
        private static float Finish(float speed, float slowPct, int minPct)
        {
            if (slowPct != 0f) speed *= 1f + slowPct / 100f;
            if (minPct > 0 && speed < minPct / 100f) speed = minPct / 100f;
            return speed < 0f ? 0f : speed;
        }

        private int MaxPositiveAura(int aura)
        {
            int best = 0;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                for (int i = 0; i < 3; i++) { var e = au.Spell.Effects[i]; if (e != null && e.IsAura && e.Aura == aura && au.Active[i] && au.Amount[i] > best) best = au.Amount[i]; }
            }
            return best;
        }

        private float MaxNegativeAura(int aura)
        {
            int worst = 0;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                for (int i = 0; i < 3; i++) { var e = au.Spell.Effects[i]; if (e != null && e.IsAura && e.Aura == aura && au.Active[i] && au.Amount[i] < worst) worst = au.Amount[i]; }
            }
            return worst;
        }

        private float TotalAuraMultiplier(int aura)
        {
            float m = 1f;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                for (int i = 0; i < 3; i++) { var e = au.Spell.Effects[i]; if (e != null && e.IsAura && e.Aura == aura && au.Active[i]) m *= (100f + au.Amount[i]) / 100f; }
            }
            return m;
        }

        private float TotalAuraModifier(int aura)
        {
            float sum = 0f;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                for (int i = 0; i < 3; i++) { var e = au.Spell.Effects[i]; if (e != null && e.IsAura && e.Aura == aura && au.Active[i]) sum += au.Amount[i]; }
            }
            return sum;
        }

        private bool SeaTurtleSwim(out int pct)
        {
            pct = 0;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                if (au.Spell.Id != SeaTurtleSpell) continue;
                for (int i = 0; i < 3; i++) { var e = au.Spell.Effects[i]; if (e != null && e.IsAura && e.Aura == MountCatalog.AuraSwimSpeed) { pct = au.Amount[i]; return true; } }
            }
            return false;
        }

        /// <summary>The swim loop: the largest increase, or the strongest decrease (AC keeps whichever beats main_speed_mod in its direction).</summary>
        private int StrongestSwimAura()
        {
            int main = 0;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e == null || !e.IsAura || e.Aura != MountCatalog.AuraSwimSpeed || !au.Active[i]) continue;
                    int v = au.Amount[i];
                    if ((v > 0 && v > main) || (v < 0 && v < main)) main = v;
                }
            }
            return main;
        }

        // ------------------------------------------------------------------ collision height (AC Unit::GetCollisionHeight while mounted)

        /// <summary>Unit.cpp 16783: `scaleMod * (mountModelData->MountHeight + modelData->CollisionHeight * modelData->Scale * displayInfo->scale * 0.5f)`
        /// with MountHeight = CreatureModelData field 16 of the mount display's model AS STORED and the rider's part ours (RiderCollisionHeight).</summary>
        private float CollisionHeightOf(int displayId)
        {
            float h;
            if (_mountCollision.TryGetValue(displayId, out h)) return h;
            h = 0f;
            var cdi = _files != null ? Dbc.Load(_files, "CreatureDisplayInfo") : null;
            var cmd = _files != null ? Dbc.Load(_files, "CreatureModelData") : null;
            int r = cdi != null ? cdi.FindRow(displayId) : -1;
            int model = r >= 0 ? cdi.GetInt(r, DbcFields.CreatureDisplayInfo.ModelID) : 0;
            int mr = cmd != null && model > 0 ? cmd.FindRow(model) : -1;
            if (mr >= 0) h = cmd.GetFloat(mr, DbcFields.CreatureModelData.MountHeight) + RiderCollisionHeight * 0.5f;
            else WowLog.Warn("Mounts: display " + displayId + " has no CreatureDisplayInfo / CreatureModelData row - collision height unknown (0)");
            _mountCollision[displayId] = h;
            return h;
        }

        /// <summary>The collision height the mount will have (for the cast's water check), from the display it would get.</summary>
        private float ExpectedCollisionHeight(MountInfo m)
        {
            float h;
            if (_expectedCollision.TryGetValue(m.SpellId, out h)) return h;
            int[] ids;
            MountDisplaySource src;
            int display = MountDisplays.TryGetAll(m.CreatureEntry, out ids, out src) && ids.Length > 0 ? ids[_mountPick % ids.Length] : MountDisplays.StandIn(m.Kind, WowRaces.FactionOf(_rec.Race));
            h = CollisionHeightOf(display);
            _expectedCollision[m.SpellId] = h;
            return h;
        }

        private static string SourceName(MountDisplaySource s)
        {
            switch (s)
            {
                case MountDisplaySource.Server: return "server";
                case MountDisplaySource.Learned: return "learned";
                case MountDisplaySource.Baked: return "baked";
                case MountDisplaySource.StandIn: return "stand-in";
            }
            return "none";
        }
    }
}
