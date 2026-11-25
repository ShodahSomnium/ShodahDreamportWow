using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    // Iteration 7 (owner Online, PLAN7 s5.2): the server's spellbook (SMSG_INITIAL_SPELLS / LEARNED / REMOVED / SUPERCEDED), the riding
    // skill, mount casts, auras (SMSG_AURA_UPDATE / _ALL) and the creature queries that teach MountDisplays the realm's mount models.
    // Stage O2 (owner spells, PLAN9 s2): EVERY spell of the spellbook is cast on the server (CMSG_CAST_SPELL with the SpellCastTargets the
    // spell's implicit targets ask for), SMSG_SPELL_START / GO / failures of EVERY caster feed EntityState + the CastStarted / CastSucceeded /
    // CastFailed / ChannelStarted / ChannelEnded events the offline game emits (so the existing VFX, audio and animations play), the combat
    // log packets (SMSG_SPELLNONMELEEDAMAGELOG, SPELLHEALLOG, PERIODICAURALOG, SPELLLOGMISS, SPELLDAMAGESHIELD, ENVIRONMENTAL_DAMAGE_LOG,
    // PROCRESIST, SPELLORDAMAGE_IMMUNE) become SpellDamage / SpellHeal / Periodic* / SpellMiss / Absorb events, channels (MSG_CHANNEL_START /
    // UPDATE), pushback (SMSG_SPELL_DELAYED), cooldowns (client-side from Spell.dbc like the real client + SMSG_SPELL_COOLDOWN /
    // COOLDOWN_EVENT / CLEAR_COOLDOWN / MODIFY_COOLDOWN + the GCD), the action bar (SMSG_ACTION_BUTTONS / CMSG_SET_ACTION_BUTTON) and immersive
    // held casting (held here, sent at the push). The server stays the only truth: nothing here computes damage, auras or results.
    // Every layout is AzerothCore's writer / reader (quoted per method; copies in P/harness/Probes/O2/spells/acsrc and P/acsrc).
    public sealed partial class OnlineSession
    {
        // ours (PLAN7 s4.8), logged once at start (LogMountConstants)
        const float CastAnswerTimeout = 3f;             // no SPELL_START / CAST_FAILED this long after CMSG_CAST_SPELL = "No answer from the server"
        const float CastWalkTolerance = 0.55f;          // yd: the walker holds its START during a cast until the avatar really walks (offline CastMoveTolerance)
        const double MountQuerySpacing = 0.25;          // s between CMSG_CREATURE_QUERY for mount entries (at most 4 a second)

        // ours (stage O2, not WoW's / AzerothCore's), logged once by InitSpellsO2
        const float SpHeldTimeout = 30f;                // s an immersive held spell waits for its push (= offline OfflineSession.HeldTimeout, = the hold module; S1: was 10), then "Interrupted"
        const float SpHeldRangeSlack = 5f;              // yd beyond the spell's range before a held spell is dropped (= offline OfflineSession.HeldRangeSlack)
        const double SpMissDedup = 0.3;                 // s: the same miss seen in SMSG_SPELL_GO and SMSG_SPELLLOGMISS is shown once
        const float SpChannelSlack = 3f;                // s after a channel's end without MSG_CHANNEL_UPDATE 0: the bar is ended quietly
        const double SpChannelEndWait = 0.12;           // s a channel end waits for the SMSG_SPELL_FAILURE that follows an interrupt (Spell::cancel sends the update first)
        const int SpLogCasts = 30;                      // the first casts are logged one by one, then every 50th
        const float SpReleaseWaitMax = 2f;              // s: an immersive push during the GCD / a short cooldown is sent when the server would take it, if within this

        // AzerothCore values
        const uint UnitFlagMount = 0x08000000;           // UNIT_FLAG_MOUNT (UnitDefines.h)
        const byte AflagCaster = 0x08, AflagDuration = 0x20, AflagNegative = 0x80;   // SpellAuraDefines.h AuraFlags
        const uint CastFlagPending = 0x1, CastFlagProjectile = 0x20, CastFlagPowerLeftSelf = 0x800, CastFlagAdjustMissile = 0x20000,
                   CastFlagVisualChain = 0x80000, CastFlagRuneList = 0x200000, CastFlagUnknown23 = 0x400000;   // Spell.h CAST_FLAG_*
        const uint TfUnit = 0x2, TfUnitRaid = 0x4, TfUnitParty = 0x8, TfItem = 0x10, TfSourceLocation = 0x20, TfDestLocation = 0x40,
                   TfUnitEnemy = 0x80, TfUnitAlly = 0x100, TfCorpseEnemy = 0x200, TfUnitDead = 0x400, TfGameObject = 0x800,
                   TfTradeItem = 0x1000, TfString = 0x2000, TfGameObjectItem = 0x4000, TfCorpseAlly = 0x8000, TfUnitMinipet = 0x10000,
                   TfUnitPassenger = 0x100000;                                          // SpellInfo.h 46-67 TARGET_FLAG_*
        const uint TfUnitMask = TfUnit | TfUnitRaid | TfUnitParty | TfUnitEnemy | TfUnitAlly | TfUnitDead | TfUnitMinipet | TfUnitPassenger;   // SpellInfo.h 69
        const int SpellMissReflect = 11;                // SharedDefines.h SPELL_MISS_REFLECT
        const int MaxRunes = 6;                         // MAX_RUNES
        const int PlayerMaxSkills = 128;                // PLAYER_MAX_SKILLS (PLAYER_SKILL_INFO_1_1 = 128 x 3 ints)
        const int ResultInterrupted = 40;               // SPELL_FAILED_INTERRUPTED
        const int ResultNoPower = 85;                   // SPELL_FAILED_NO_POWER (no GlobalStrings key: the client shows ERR_OUT_OF_<POWER>)
        const float SpQueueWindow = 0.4f;               // worldserver.conf.dist 2280 / 2287: SpellQueue.Enabled = 1, SpellQueue.Window = 400 (ms) - AC queues a
                                                        // cast asked this close to the end of the GCD / cooldown / current cast (PlayerUpdates.cpp 2396-2419)
        const int SpMinGcdMs = 1000, SpMaxGcdMs = 1500; // Spell.cpp 9209-9210 MIN_GCD / MAX_GCD
        const float SpNominalMelee = 5f, SpMinMeleeReach = 2f, SpDefaultReach = 1.5f;   // ObjectDefines.h 45-47
        const uint Attr0UsesRangedSlot = 0x2, Attr0IsAbility = 0x10, Attr0AllowWhileMountedO2 = 0x01000000, Attr0CooldownOnEvent = 0x02000000;   // SharedDefines.h 371-395
        const int SpAttackSpell = 6603;                 // "Attack" (SPELL_EFFECT_ATTACK), the auto-attack toggle of the spellbook (offline AutoAttackSpell)
        const int SpAutoShot = 75;                      // "Auto Shot" - offline reports its shots as Melee* with this SpellId (OfflineSession.Combat StepRanged)
        const byte CooldownFlagIncludeGcd = 0x1;        // Unit.h 634 SPELL_COOLDOWN_FLAG_INCLUDE_GCD
        const int ActionButtonsMax = 144;               // Player.h 261 MAX_ACTION_BUTTONS
        const int AuraPeriodicDamage = 3, AuraPeriodicHeal = 8, AuraObsModHealth = 20, AuraObsModPower = 21, AuraPeriodicEnergize = 24,
                  AuraPeriodicManaLeech = 64, AuraPeriodicDamagePercent = 89;          // SpellAuraDefines.h 66-152
        const uint HitTypeCritDebug = 0x1, HitTypeCrit = 0x2, HitTypeHitDebug = 0x4, HitTypeAttackTableDebug = 0x20;   // SharedDefines.h 1539-1544
        const int DmgClassMelee = 2, DmgClassRanged = 3;// SPELL_DAMAGE_CLASS_MELEE / _RANGED

        /// <summary>
        /// AzerothCore SpellImplicitTargetInfo::_data (SpellInfo.cpp 211-323), GENERATED by a perl script from that table (never typed by hand):
        /// three characters per implicit target id 0..110 = reference type (- none, c caster, t target, l last, s src, d dest), object type
        /// (- none, s src, d dest, u unit, b unit_and_dest, g gobj, i gobj_item) and check type (d default, n entry, e enemy, a ally, p party,
        /// r raid, R raid_class, P passenger).
        /// </summary>
        const string SpImplicitTargets =
            "--dcudcuecuacupcudtuesunduncdd--dsud--d--d--dsueduecddcdd--d" +
            "cuptuacsdtgdcuetudtidcudddeddasuaduacddsupduptupcddlupcuncdd" +
            "cgncddcddcddcddtuacdncddcddcddcddsgddgdtdecuecddcurturcurcua" +
            "cuntuR--dtddtddtddtddtddtddtddtddtddcddcddtddtddcddcuddddddd" +
            "ddddddddddddddddddddddddddddddtuddddcudcuecudtuPcudcudcudcud" +
            "cudcudcudcudcue-udcddd-dcgd--d-dd";
        const int TargetDestTraj = 89;                  // TARGET_DEST_TRAJ (the one special case of GetExplicitTargetMask)

        SpellDb _spellDb;
        readonly HashSet<int> _known = new HashSet<int>();
        bool _haveSpellbook, _catalogFallbackLogged;
        int _skillRiding;

        // our cast
        int _castId;                    // a cast-time spell or channel of ours (asked or running), 0 = none; the walker holds its START while set
        byte _castCount, _cancelCounter;
        double _castSentAt = -1, _castStartedAt = -1;
        int _castStartMs;
        WowPos _castPos;
        int _lastFailSpell; string _lastFailText; double _lastFailAt = -10;
        int _spellParseProblems, _spellPackets;

        // stage O2
        int _spPendingId; double _spPendingSentAt = -1;         // the last CMSG_CAST_SPELL of an instant still without SPELL_START / GO / CAST_FAILED
        int _spChannelId; double _spChannelStart; int _spChannelMs; ulong _spChannelTarget; ulong _spLastGoTarget; int _spLastGoSpell;
        int _spNextSwing;               // an on-next-swing ability waiting for the swing (Heroic Strike), 0 = none
        int _spAutoRepeat;              // our auto-repeat spell while it is on (Auto Shot 75, Shoot, Throw), 0 = off
        int _spHeldId; ulong _spHeldTarget; double _spHeldAt;   // immersive casting: the spell held in the hand (not sent yet)
        int _spReleaseId; ulong _spReleaseTarget; double _spReleaseAt;   // a pushed held spell waiting for the end of the GCD

        // Stage S1 (owner held, PLAN10 s0.8 / rule 6): the realm's Dreamport hold (ServerKit dreamport-hold, AzerothCore module
        // mod-dreamport-hold). With it an immersive cast is SENT when chosen, the realm holds it at the end of its cast time (DPHOLD
        // READY = Player.HeldSpellId + CastHeld: the vibration and the loop glow), the push releases it (DPHOLD GO). Without it (a
        // stock realm echoes our HELLO back, or no answer) the stage-O2 local hold above stays in use. The messages are LANG_ADDON
        // whispers to ourselves, prefix "DPHOLD\t" (the module's Player::Whisper hook returns false: nobody else ever gets them).
        const string DpPrefix = "DPHOLD\t";
        const int DpProtocol = 1;
        const float DpHelloTimeout = 5f;                // ours: s without an answer to DPHOLD HELLO = this realm has no Dreamport hold
        const float DpServerHoldTimeout = 30f;          // the module's DreamportHoldTimeoutMs (dreamport_hold.cpp); our safety net adds CastAnswerTimeout
        const uint DpLangAddon = 0xFFFFFFFF;            // SharedDefines.h 741 LANG_ADDON
        const int DpChatWhisper = 0x07;                 // SharedDefines.h 3392 CHAT_MSG_WHISPER
        const int DpAsked = 1, DpArmed = 2, DpReady = 3, DpReleased = 4;   // _dpState of the cast the realm was asked to hold
        int _dpMode;                                    // 0 = not known yet, 1 = the realm holds (DPHOLD HI), -1 = no hold on this realm
        bool _dpHelloSent; double _dpHelloAt;
        int _dpAsk;                                     // CastSpellHeld: the next CMSG_CAST_SPELL of this spell goes with a DPHOLD HOLD
        int _dpSpell, _dpState; byte _dpCount; double _dpReadyAt, _dpReleasedAt, _dpStartedAt;
        int _dpHolds, _dpInstantLogged; bool _dpOddLogged;
        // Stage S1 (integrator, PLAN10 s5 - a Dreamport house rule): a cast released with Immersive casting WITHOUT a hold (an instant, a
        // channel, or one held here before the realm answered) goes with DPHOLD BONUS right before its CMSG_CAST_SPELL - the realm's module
        // then adds ImmersiveMode.ImmersiveDamageBonus to its damage (a held cast is flagged by its HOLD). Only VrImmersive reaches this.
        int _dpBonusAsk;                                // CastSpellHeld / the push: the next CMSG_CAST_SPELL of this spell goes with a DPHOLD BONUS
        int _dpBonuses, _dpBonusPct = -1;
        bool _spImmersiveShown;                         // the "Immersive Mode" buff is in _player.Auras (ImmersiveMode.Active)
        readonly AuraView _spImmersiveView = ImmersiveMode.NewBuff();
        double _spGcdEnd = -1; int _spGcdCategory, _spGcdSpell;
        readonly Dictionary<int, double> _spCdEnd = new Dictionary<int, double>();   // spell -> session time its cooldown ends (MaxValue = until SMSG_COOLDOWN_EVENT)
        readonly List<int> _spCdKeys = new List<int>();
        double _spCdSweepAt;
        readonly Dictionary<int, uint> _spMaskCache = new Dictionary<int, uint>();
        readonly HashSet<int> _spMyAuras = new HashSet<int>();
        readonly List<SpChannelEnd> _spChannelEnds = new List<SpChannelEnd>();
        readonly SpRecentMiss[] _spMisses = new SpRecentMiss[16];
        int _spMissNext;
        readonly AuraView[] _spAuraScratch = new AuraView[256];
        int _spCastsSent, _spLogPackets, _spOtherCasts, _spPassiveLogged, _spDroppedButtons;
        readonly HashSet<ushort> _spOddLogged = new HashSet<ushort>();
        bool _spSelfCastLogged, _spDestLogged;

        readonly Dictionary<ulong, SpLastGo> _spOtherGo = new Dictionary<ulong, SpLastGo>();   // another caster -> its last SPELL_GO (the channel target)
        int _spOtherGoLogged;

        struct SpLastGo { public int Spell; public ulong Target; public double At; }
        struct SpChannelEnd { public ulong Caster, Target; public int Spell; public double At; }
        struct SpRecentMiss { public ulong Caster, Target; public int Spell, Cond; public double At; }

        // auras
        readonly AuraView[] _mySlots = new AuraView[256];
        readonly HashSet<OnlineObject> _auraOwners = new HashSet<OnlineObject>();
        readonly List<OnlineObject> _castingOthers = new List<OnlineObject>();
        int _mountAuraSpell;

        // mount display learning (CMSG_CREATURE_QUERY of the Mounts entries)
        readonly Queue<int> _mountQueryQueue = new Queue<int>();
        readonly HashSet<int> _mountAsked = new HashSet<int>();
        double _nextMountQueryAt;
        int _mountQueriesSent;
        HashSet<int> _catalogEntries;

        SpellDb Db
        {
            get
            {
                if (_spellDb == null) _spellDb = SpellDb.Get(_files);
                return _spellDb;
            }
        }

        void RegisterSpellHandlers()
        {
            _world.On(Op.SMSG_INITIAL_SPELLS, OnInitialSpells, this);
            _world.On(Op.SMSG_LEARNED_SPELL, OnLearnedSpell, this);
            _world.On(Op.SMSG_REMOVED_SPELL, OnRemovedSpell, this);
            _world.On(Op.SMSG_SUPERCEDED_SPELL, OnSupercededSpell, this);
            _world.On(Op.SMSG_SPELL_START, OnSpellStart, this);
            _world.On(Op.SMSG_SPELL_GO, OnSpellGo, this);
            _world.On(Op.SMSG_CAST_FAILED, OnCastFailed, this);
            _world.On(Op.SMSG_SPELL_FAILURE, p => OnSpellFailure(p, false), this);
            _world.On(Op.SMSG_SPELL_FAILED_OTHER, p => OnSpellFailure(p, true), this);
            _world.On(Op.SMSG_AURA_UPDATE, p => OnAuraUpdate(p, false), this);
            _world.On(Op.SMSG_AURA_UPDATE_ALL, p => OnAuraUpdate(p, true), this);
        }

        /// <summary>Stage O2 (PLAN9 s3): the channel, cooldown, action bar and combat log handlers (the constructor calls it after RegisterSpellHandlers).</summary>
        void InitSpellsO2()
        {
            if (_world != null)
            {
                _world.On(Op.MSG_CHANNEL_START, SpOnChannelStart, this);
                _world.On(Op.MSG_CHANNEL_UPDATE, SpOnChannelUpdate, this);
                _world.On(Op.SMSG_SPELL_DELAYED, SpOnSpellDelayed, this);
                _world.On(Op.SMSG_SPELL_COOLDOWN, SpOnSpellCooldown, this);
                _world.On(Op.SMSG_COOLDOWN_EVENT, SpOnCooldownEvent, this);
                _world.On(Op.SMSG_CLEAR_COOLDOWN, SpOnClearCooldown, this);
                _world.On(Op.SMSG_MODIFY_COOLDOWN, SpOnModifyCooldown, this);
                _world.On(Op.SMSG_ACTION_BUTTONS, SpOnActionButtons, this);
                _world.On(Op.SMSG_SPELLNONMELEEDAMAGELOG, SpOnNonMeleeDamage, this);
                _world.On(Op.SMSG_SPELLHEALLOG, SpOnHealLog, this);
                _world.On(Op.SMSG_PERIODICAURALOG, SpOnPeriodicLog, this);
                _world.On(Op.SMSG_SPELLLOGMISS, SpOnLogMiss, this);
                _world.On(Op.SMSG_SPELLDAMAGESHIELD, SpOnDamageShield, this);
                _world.On(Op.SMSG_ENVIRONMENTAL_DAMAGE_LOG, SpOnEnvironmental, this);
                _world.On(Op.SMSG_PROCRESIST, p => SpOnResistOrImmune(p, "Resist"), this);          // RESIST (GlobalStrings.lua:6097)
                _world.On(Op.SMSG_SPELLORDAMAGE_IMMUNE, p => SpOnResistOrImmune(p, "Immune"), this); // IMMUNE (GlobalStrings.lua:4227)
                _world.On(Op.SMSG_SPELLENERGIZELOG, SpOnEnergizeLog, this);
                _world.On(Op.SMSG_CANCEL_AUTO_REPEAT, SpOnCancelAutoRepeat, this);
                _world.On(Op.SMSG_SPELLINSTAKILLLOG, SpOnInstakill, this);
                _world.On(Op.SMSG_SPELLDISPELLOG, SpOnDispelLog, this);
                _world.On(Op.SMSG_MESSAGECHAT, p => DpOnChat(p, false), this);      // S1: the realm's DPHOLD answers (LANG_ADDON whispers to ourselves)
                _world.On(Op.SMSG_GM_MESSAGECHAT, p => DpOnChat(p, true), this);    // the same for a GM account (BuildChatPacket gmMessage: + sender name)
                // iteration 11 (spellfx, proposal-spellfx.md s3.2): combo points, runes, enchant time / log, scripted spell kits
                _world.On(Op.SMSG_UPDATE_COMBO_POINTS, OnUpdateComboPoints, this);          // Combat.cs
                _world.On(Op.SMSG_PET_UPDATE_COMBO_POINTS, p => { if (!_petComboLogged) { _petComboLogged = true; WowLog.Info("Online: SMSG_PET_UPDATE_COMBO_POINTS ignored (vehicle / possessed combo points; logged once)"); } }, this);
                _world.On(Op.SMSG_CONVERT_RUNE, OnConvertRune, this);                       // Targeting.cs
                _world.On(Op.SMSG_RESYNC_RUNES, OnResyncRunes, this);
                _world.On(Op.SMSG_ADD_RUNE_POWER, OnAddRunePower, this);
                _world.On(Op.SMSG_ITEM_ENCHANT_TIME_UPDATE, OnItemEnchantTimeUpdate, this);
                _world.On(Op.SMSG_ENCHANTMENTLOG, OnEnchantmentLog, this);
                _world.On(Op.SMSG_PLAY_SPELL_VISUAL, p => OnPlaySpellKit(p, false), this);   // DynObjects.cs
                _world.On(Op.SMSG_PLAY_SPELL_IMPACT, p => OnPlaySpellKit(p, true), this);
            }
            WowLog.Info("Online: spells - ours (not WoW's): held spell time-out " + SpHeldTimeout + " s, held range slack " + SpHeldRangeSlack + " yd, miss de-duplication " +
                        SpMissDedup + " s, channel end slack " + SpChannelSlack + " s, channel end waits " + SpChannelEndWait + " s for its failure, push waits up to " + SpReleaseWaitMax + " s for the GCD; AzerothCore's: spell queue window " + (SpQueueWindow * 1000f) + " ms (SpellQueue.Window), GCD " +
                        SpMinGcdMs + ".." + SpMaxGcdMs + " ms; immersive casting online (S1): with the realm's Dreamport hold the cast starts at once and the realm holds it at its end (" + DpServerHoldTimeout +
                        " s), without it the spell is held here BEFORE the cast and sent at the push (the time-out above); the realm is asked once (DPHOLD HELLO, answer within " + DpHelloTimeout + " s)");
        }

        /// <summary>Spell packets parsed / parsed with bytes left over or missing (a layout problem; logged once per opcode).</summary>
        public int SpellPacketsParsed { get { return _spellPackets; } }
        public int SpellParseProblems { get { return _spellParseProblems; } }
        /// <summary>Spells the server says we know (INITIAL_SPELLS + LEARNED - REMOVED), for probes and diagnostics.</summary>
        public bool KnowsSpell(int spellId) { return _known.Contains(spellId); }
        /// <summary>SMSG_INITIAL_SPELLS has arrived (the Mounts window can say "no mounts" only after it).</summary>
        public bool HaveSpellbook { get { return _haveSpellbook; } }
        /// <summary>Seconds left of our cooldown of a spell (float.MaxValue = until its effect ends), for probes and diagnostics.</summary>
        public float SpellCooldownLeft(int spellId) { return SpCooldownLeft(spellId); }

        // ------------------------------------------------------------------ what a mount spell is

        /// <summary>A Mounts-line spell (MountCatalog, the Rules owner's table); before the catalog is loaded: any spell with aura 78.</summary>
        bool IsMountSpell(int spellId)
        {
            if (spellId <= 0) return false;
            if (MountCatalog.Loaded) return MountCatalog.IsMount(spellId);
            if (!_catalogFallbackLogged)
            {
                _catalogFallbackLogged = true;
                WowLog.Warn("Online: MountCatalog is not loaded - mount spells are recognised by their aura 78 (SPELL_AURA_MOUNTED) in Spell.dbc until it is (logged once).");
            }
            var s = Db.Find(spellId);
            return s != null && s.HasAura(MountCatalog.AuraMounted);
        }

        /// <summary>Spell.dbc facts of a mount spell: the aura-78 creature entry and ground / flying (aura 207).</summary>
        void MountFacts(int spellId, out int entry, out MountKind kind)
        {
            entry = 0;
            kind = MountKind.None;
            if (spellId <= 0) return;
            var info = MountCatalog.Find(spellId);
            if (info != null) { entry = info.CreatureEntry; kind = info.Kind; return; }
            var s = Db.Find(spellId);
            if (s == null) return;
            var mounted = s.AuraOf(MountCatalog.AuraMounted);
            if (mounted == null) return;
            entry = mounted.MiscValue;
            kind = s.HasAura(MountCatalog.AuraMountedFlightSpeed) ? MountKind.Flying : MountKind.Ground;
        }

        string SpellName(int spellId)
        {
            var s = Db.Find(spellId);
            return s != null && s.Name.Length > 0 ? s.Name : "spell " + spellId;
        }

        // ------------------------------------------------------------------ spellbook (Player::SendInitialSpells, learnSpell, removeSpell)

        /// <summary>
        /// SMSG_INITIAL_SPELLS (Player::SendInitialSpells, Player.cpp 2789-2883): uint8 0; uint16 n; n x (uint32 spell, uint16 0); uint16 count;
        /// count x (uint32 spell, uint16 item, uint16 category, uint32 cooldown, uint32 categoryCooldown). The count is m_spellCooldowns.size()
        /// but entries with !needSendToClient are skipped AFTER it was written (2855-2858) - so entries are read while 16 bytes remain.
        /// An infinite cooldown is sent as (1, 0x80000000) (2869-2874).
        /// </summary>
        void OnInitialSpells(WowPacket p)
        {
            p.U8();
            int n = p.U16();
            _known.Clear();
            for (int i = 0; i < n; i++)
            {
                _known.Add((int)p.U32());
                p.U16();
            }
            int cdCount = p.Remaining >= 2 ? p.U16() : 0, cdRead = 0;
            _spCdEnd.Clear();
            for (int i = 0; i < cdCount && p.Remaining >= 16; i++)
            {
                int spell = (int)p.U32();
                p.U16();                                        // cast item id
                p.U16();                                        // category
                uint cd = p.U32(), catCd = p.U32();
                cdRead++;
                if (cd == 1 && catCd == 0x80000000u) { _spCdEnd[spell] = double.MaxValue; continue; }
                uint ms = Math.Max(cd, catCd);
                if (ms > 0) _spCdEnd[spell] = _now + ms / 1000.0;
            }
            _haveSpellbook = true;
            RebuildSpells();
            var names = new StringBuilder();
            for (int i = 0; i < _player.Mounts.Count && i < 8; i++) names.Append(i == 0 ? "" : ", ").Append(_player.Mounts[i].Name).Append(' ').Append(_player.Mounts[i].SpellId);
            if (_player.Mounts.Count > 8) names.Append(", ...");
            WowLog.Info("Online: spellbook from the server - " + n + " spells: " + _player.Spellbook.Count + " in the book (cast on the server), " + _player.Mounts.Count +
                        " mounts" + (_player.Mounts.Count > 0 ? " (" + names + ")" : "") + ", riding " + _player.RidingSkill + (_player.ColdWeatherFlying ? " + Cold Weather Flying" : "") +
                        ", cooldowns " + cdRead + (cdRead != cdCount ? " (count said " + cdCount + ")" : "") + "; MountCatalog " + MountCatalog.Summary);
            QueueMountQueries();
        }

        /// <summary>SMSG_LEARNED_SPELL (Player::learnSpell): uint32 spell; uint16 0. A console `player learn` arrives here at once.</summary>
        void OnLearnedSpell(WowPacket p)
        {
            int id = (int)p.U32();
            if (!_known.Add(id)) return;
            SpRankUp(id);   // lead fix 2026-10-03 (the user: "spells should auto upgrade when you learn the next rank")
            RebuildSpells();
            bool mount = IsMountSpell(id);
            string name = SpellName(id);
            WowLog.Info("Online: learned spell " + id + " '" + name + "'" + (mount ? " (mount)" : "") + (id == MountCatalog.ColdWeatherFlying || RidingOf(id) > 0 ? " - riding now " + _player.RidingSkill : ""));
            _events.Enqueue(new GameEvent { Type = GameEventType.SpellLearned, SourceGuid = _player.Guid, SpellId = id,
                                            Text = (PfIsRecipeId(id) ? "You have learned how to create a new item: " : "You have learned a new spell: ") + name + "." });   // ERR_LEARN_RECIPE_S (GS:3177) / ERR_LEARN_SPELL_S (GS:3178) - iteration 11 (trainer S3)
            if (mount) QueueMountQueries();
        }

        /// <summary>
        /// Lead fix (2026-10-03, the user: "spells should auto upgrade when you learn the next rank of it"). A trainer's new rank reached us
        /// only as SMSG_LEARNED_SPELL (Player.log: "learned spell 695 'Shadow Bolt'", no SMSG_SUPERCEDED_SPELL - Player::addSpell sends that
        /// only from its active-rank walk, Player.cpp:3165-3187), so the action buttons kept Rank 1. Like the real client's action bar, every
        /// button holding a LOWER rank of the same spell (Spell.dbc name + "Rank N") now takes the new one (AbReplaceSpell also tells the
        /// server, CMSG_SET_ACTION_BUTTON). A SMSG_SUPERCEDED_SPELL that does come is then a no-op on the buttons.
        /// </summary>
        void SpRankUp(int id)
        {
            var db = Db;
            var s = db != null ? db.Find(id) : null;
            int rank = s != null ? SpRankNumber(s.Rank) : 0;
            if (rank <= 1 || string.IsNullOrEmpty(s.Name)) return;
            var lower = new List<int>();
            foreach (int k in _known)
            {
                if (k == id) continue;
                var o = db.Find(k);
                if (o == null || o.Name != s.Name) continue;
                int r = SpRankNumber(o.Rank);
                if (r > 0 && r < rank) lower.Add(k);
            }
            for (int i = 0; i < lower.Count; i++) AbReplaceSpell(lower[i], id);
        }

        /// <summary>"Rank 3" -> 3; anything else (no rank, "Racial", "Apprentice") -> 0.</summary>
        static int SpRankNumber(string rank)
        {
            if (string.IsNullOrEmpty(rank) || !rank.StartsWith("Rank ", StringComparison.Ordinal)) return 0;
            int n;
            return int.TryParse(rank.Substring(5).Trim(), out n) ? n : 0;
        }

        /// <summary>SMSG_REMOVED_SPELL (Player::removeSpell): uint32 spell. `player unlearn` from the console.</summary>
        void OnRemovedSpell(WowPacket p)
        {
            int id = (int)p.U32();
            if (!_known.Remove(id)) return;
            RebuildSpells();
            string name = SpellName(id);
            WowLog.Info("Online: unlearned spell " + id + " '" + name + "'");
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, SpellId = id, Text = "You have unlearned " + name + "." });   // ERR_SPELL_UNLEARNED_S (GlobalStrings.lua:3506)
        }

        /// <summary>SMSG_SUPERCEDED_SPELL (Player::addSpell, a higher rank): uint32 old (the lower rank now inactive); uint32 new.</summary>
        void OnSupercededSpell(WowPacket p)
        {
            int old = (int)p.U32(), now = (int)p.U32();
            _known.Remove(old);
            _known.Add(now);
            AbReplaceSpell(old, now);   // iteration 11 (vrcast's hook, applied in wave 0 by w0play): OnlineSession.ActionBars.cs
            RebuildSpells();
            WowLog.Info("Online: spell " + old + " superseded by " + now + " '" + SpellName(now) + "'");
        }

        /// <summary>Riding skill value a riding spell gives (SpellMgr.cpp: SKILL_RIDING value = step x 75), 0 for others.</summary>
        static int RidingOf(int spellId)
        {
            switch (spellId)
            {
                case MountCatalog.ApprenticeRiding: return 75;
                case MountCatalog.JourneymanRiding: return 150;
                case MountCatalog.ExpertRiding: return 225;
                case MountCatalog.ArtisanRiding: return 300;
                default: return 0;
            }
        }

        void UpdateRiding()
        {
            int fromSpells = 0;
            foreach (int id in _known) fromSpells = Math.Max(fromSpells, RidingOf(id));
            _player.RidingSkill = Math.Max(_skillRiding, fromSpells);
            _player.ColdWeatherFlying = _known.Contains(MountCatalog.ColdWeatherFlying);
        }

        /// <summary>PLAYER_SKILL_INFO_1_1: 128 x (SkillIndex = id | step &lt;&lt; 16, SkillValue = value | max &lt;&lt; 16, bonus) - riding (762).</summary>
        void ReadSkills(OnlineObject o)
        {
            int riding = 0;
            for (int i = 0; i < PlayerMaxSkills; i++)
            {
                int idx = UF.PLAYER_SKILL_INFO_1_1 + i * 3;
                if (o.U16(idx, 0) != MountCatalog.SkillRiding) continue;
                riding = o.U16(idx + 1, 0);
                break;
            }
            if (riding == _skillRiding) return;
            _skillRiding = riding;
            UpdateRiding();
            WowLog.Info("Online: riding skill " + riding + " (skill 762 from the server)");
        }

        /// <summary>Player.Spellbook = the known spells a player sees (not hidden client-side, not mounts); Player.Mounts = the mount spells.</summary>
        void RebuildSpells()
        {
            var db = Db;
            var book = new List<SpellInfo>();
            var mounts = new List<SpellInfo>();
            var recipes = new List<SpellInfo>();               // iteration 11 (trainer S1)
            var companions = new List<SpellInfo>();            // iteration 11 (pets H5)
            foreach (int id in _known)
            {
                var s = db.Find(id);
                if (s == null) continue;
                if (IsMountSpell(id)) mounts.Add(s);
                else if (SomniumSpace.Worlds.SomniumWoW.Play.Pets.VehicleDb.IsCompanion(db, s)) companions.Add(s);   // iteration 11 (pets H5): WoW's Companions tab, not the spellbook
                else if (PfIsRecipe(s)) recipes.Add(s);                     // iteration 11 (trainer S1): SPELL_ATTR0_IS_TRADESKILL 0x20 -> the profession window, not the spellbook (SharedDefines.h:375)
                else if (!s.IsHiddenClientSide && !TalentHidden(s.Id)) book.Add(s);   // iteration 11 (talents): the leaking class talent spells stay out
            }
            book.Sort((a, b) => a.SpellLevel != b.SpellLevel ? a.SpellLevel.CompareTo(b.SpellLevel) : a.Id.CompareTo(b.Id));
            mounts.Sort((a, b) => { int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            companions.Sort((a, b) => { int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            _player.Spellbook.Clear();
            _player.Mounts.Clear();
            foreach (var s in book) _player.Spellbook.Add(Ability(s, false));
            foreach (var s in mounts) _player.Mounts.Add(Ability(s, true));
            _player.Companions.Clear();
            foreach (var s in companions) _player.Companions.Add(Ability(s, false));
            recipes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _player.Recipes.Clear();
            foreach (var r in recipes) _player.Recipes.Add(Ability(r, false));
            PfRecipesChanged();
            UpdateRiding();
            RefreshAbilityStates();
        }

        AbilityView Ability(SpellInfo s, bool mount)
        {
            var v = new AbilityView();
            v.SpellId = s.Id;
            v.Name = s.Name;
            v.Rank = s.Rank;
            v.IconPath = s.IconPath;
            try { v.Description = SpellText.Resolve(Db, s, s.Description, new SpellTextContext { CasterLevel = _player.Level }); }
            catch (Exception) { v.Description = s.Description; }
            PowerType pt;
            v.Cost = SpCost(s, out pt);
            v.CostType = pt;
            v.CastTime = s.CastTimeMs / 1000f;
            v.Cooldown = Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs) / 1000f;
            v.RangeMin = s.RangeMin;
            v.RangeMax = s.RangeMax;
            uint need = SpExplicitMask(s);
            v.Harmful = s.IsHarmful || (need & TfUnitEnemy) != 0;
            v.NeedsTarget = (need & TfUnitEnemy) != 0 || ((need & TfUnit) != 0 && v.Harmful);
            v.Passive = s.IsPassive;
            v.LearnedAtLevel = Math.Max(1, s.SpellLevel);
            v.IsMount = mount;
            v.Tooltip = SpTooltip(s, v);
            return v;
        }

        /// <summary>Name (Rank) / cost + range / cast + cooldown / description - the offline tooltip layout (OfflineSession.BuildTooltip).</summary>
        static string SpTooltip(SpellInfo s, AbilityView v)
        {
            var sb = new StringBuilder();
            sb.Append(s.Name);
            if (s.Rank.Length > 0) sb.Append(" (").Append(s.Rank).Append(')');
            sb.Append('\n');
            if (v.Cost > 0)
            {
                sb.Append(v.Cost).Append(' ').Append(v.CostType == PowerType.Rage ? "Rage" : v.CostType == PowerType.Energy ? "Energy" : v.CostType == PowerType.Mana ? "Mana"
                                                  : v.CostType == PowerType.RunicPower ? "Runic Power" : v.CostType == PowerType.Focus ? "Focus" : "Health");
                sb.Append("   ");
            }
            if (s.RangeMax > 5f) sb.Append(s.RangeMin > 0 ? s.RangeMin.ToString("0") + "-" : "").Append(s.RangeMax.ToString("0")).Append(" yd range");
            else if (s.RangeMax > 0f) sb.Append("Melee Range");
            sb.Append('\n');
            sb.Append(s.AutoRepeat ? "Auto-repeat" : s.IsChanneled ? "Channeled" : s.CastTimeMs > 0 ? (s.CastTimeMs / 1000f).ToString("0.#") + " sec cast" : s.OnNextSwing ? "Next melee" : "Instant");
            if (v.Cooldown > 0f) sb.Append("   ").Append(SpellText.DurationText((int)(v.Cooldown * 1000))).Append(" cooldown");
            sb.Append('\n');
            sb.Append(v.Description);
            return sb.ToString();
        }

        /// <summary>
        /// The cost shown and checked for display (Spell::CalcPowerCost basics): Spell.dbc ManaCost, or ManaCostPercentage of the base mana
        /// (UNIT_FIELD_BASE_MANA) / base health; rage and runic power are stored x10. Talent cost mods are not known here - the server decides.
        /// </summary>
        int SpCost(SpellInfo s, out PowerType pt)
        {
            int baseMana = _self != null ? _self.I32(UF.UNIT_FIELD_BASE_MANA) : 0, baseHealth = _self != null ? _self.I32(UF.UNIT_FIELD_BASE_HEALTH) : 0;
            switch (s.PowerType)
            {
                case -2: pt = PowerType.None; return s.ManaCostPercentage > 0 ? baseHealth * s.ManaCostPercentage / 100 : s.ManaCost;
                case 0: pt = PowerType.Mana; return s.ManaCostPercentage > 0 ? baseMana * s.ManaCostPercentage / 100 : s.ManaCost;
                case 1: pt = PowerType.Rage; return s.ManaCost / 10;
                case 2: pt = PowerType.Focus; return s.ManaCost;
                case 3: pt = PowerType.Energy; return s.ManaCost;
                case 6: pt = PowerType.RunicPower; return s.ManaCost / 10;
                default: pt = PowerType.None; return 0;
            }
        }

        /// <summary>Our current power of a Spell.dbc power type from our values (rage / runic power / 10), -1 when not known.</summary>
        int SpPowerNow(int powerType)
        {
            if (_self == null) return -1;
            if (powerType == -2) return _player.Health;
            if (powerType < 0 || powerType > 6) return -1;
            int v = _self.I32(UF.UNIT_FIELD_POWER1 + powerType);
            return powerType == 1 || powerType == 6 ? v / 10 : v;
        }

        /// <summary>Per tick: every spellbook entry's cooldown (the GCD when longer), Usable + UnusableReason and Active; mount buttons as iteration 7.</summary>
        void RefreshAbilityStates()
        {
            string why = _player.Dead ? "You are dead"              // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
                       : _player.InCombat ? "You are in combat"     // SPELL_FAILED_AFFECTING_COMBAT (GlobalStrings.lua:6941)
                       : null;
            for (int i = 0; i < _player.Mounts.Count; i++)
            {
                var v = _player.Mounts[i];
                bool active = _player.Mounted && _player.MountSpellId == v.SpellId;
                v.Active = active;
                v.Usable = why == null || active;               // pressing the ridden mount dismounts, even in combat
                v.UnusableReason = v.Usable ? null : why;
                v.CooldownRemaining = 0f;
            }
            if (_player.Spellbook.Count == 0) return;
            var db = Db;
            EntityState target = _target != 0 && _target != _player.Guid ? Find(_target) : null;
            float gcdLeft = _spGcdEnd > _now ? (float)(_spGcdEnd - _now) : 0f;
            for (int i = 0; i < _player.Spellbook.Count; i++)
            {
                var v = _player.Spellbook[i];
                var s = db.Find(v.SpellId);
                if (s == null) continue;
                float cd = SpCooldownLeft(s.Id);
                bool infinite = cd == float.MaxValue;
                if (infinite) cd = 0f;
                float gcd = s.StartRecoveryTimeMs > 0 && s.StartRecoveryCategory == _spGcdCategory ? gcdLeft : 0f;
                v.CooldownRemaining = Math.Max(cd, gcd);
                string reason = SpUsableReason(s, target);
                if (reason == null && (infinite || v.CooldownRemaining > 0.02f)) reason = "Spell is not ready yet";   // ERR_SPELL_COOLDOWN (GlobalStrings.lua:3493) without the period, as offline + the UI
                v.Usable = reason == null;
                v.UnusableReason = reason;
                v.Targeting = SpellTargeting.KindOf(s, false);   // iteration 11 (spellfx): the circle / the item cursor before the cast
                var shape = s.AuraOf(SpellAura.ModShapeshift);
                v.Active = (shape != null && shape.MiscValue != 0 && shape.MiscValue == _player.ShapeshiftForm)
                        || (s.Id == SpAttackSpell && _player.AutoAttacking) || (s.AutoRepeat && _spAutoRepeat == s.Id)
                        || _spNextSwing == s.Id || (_player.CastingSpellId == s.Id) || _spHeldId == s.Id
                        || (_spMyAuras.Contains(s.Id) && s.DurationMs < 0);
            }
        }

        /// <summary>Why the button is greyed right now (display only - the server decides every cast): passive, dead, form, target, range, power.</summary>
        string SpUsableReason(SpellInfo s, EntityState target)
        {
            if (s.IsPassive) return "Passive";                  // SPELL_PASSIVE (GlobalStrings.lua:7224)
            if (_player.Dead) return "You are dead";            // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
            string form = SpCheckShapeshift(s);
            if (form != null) return form;
            if (s.Id == SpAttackSpell) return target != null && !target.Dead && target.Reaction != Reaction.Friendly ? null : "You have no target";
            // iteration 11 (spellfx): what the server's CheckCast refuses first (Spell.cpp:5973-5982; SpellInfo.cpp:1850-1861) - display only
            if (s.CasterAuraState != 0 && !SpHasAuraState(_player.AuraStateMask, s.CasterAuraState, s)) return "You can't do that yet";        // SPELL_FAILED_CASTER_AURASTATE (GlobalStrings.lua:6961)
            if (s.CasterAuraStateNot != 0 && SpHasAuraState(_player.AuraStateMask, s.CasterAuraStateNot, null)) return "You can't do that yet";
            if (s.CasterAuraSpell != 0 && !_spMyAuras.Contains(s.CasterAuraSpell)) return "You can't do that yet";                              // Rune Strike needs 56817
            if (s.ExcludeCasterAuraSpell != 0 && _spMyAuras.Contains(s.ExcludeCasterAuraSpell)) return "You can't do that yet";                 // e.g. Forbearance
            if (s.NeedsComboPoints && (_player.ComboPoints <= 0 || target == null || _player.ComboTarget != target.Guid))
                return "That ability requires combo points";                                                                                     // SPELL_FAILED_NO_COMBO_POINTS (GlobalStrings.lua:7125)
            if (s.PowerType == 5 && s.RuneCostId > 0 && _player.Runes != null)                                                                   // POWER_RUNE: CheckRuneCost runs only then (Spell.cpp:5590)
            {
                var rc = Db.RuneCost(s.RuneCostId);
                if (rc != null && !_runes.CanPay(rc[0], rc[1], rc[2])) return "Not enough runes";                                                // ERR_OUT_OF_RUNES (GlobalStrings.lua:3342)
            }
            uint need = SpExplicitMask(s);
            bool enemy = (need & TfUnitEnemy) != 0 || ((need & TfUnit) != 0 && (need & (TfUnitAlly | TfUnitParty | TfUnitRaid)) == 0 && s.IsHarmful);
            if (enemy)
            {
                if (target == null || target.Kind == EntityKind.GameObject) return "You have no target";   // ERR_GENERIC_NO_TARGET (GlobalStrings.lua:3064), offline's wording
                if (target.Dead && (need & TfUnitDead) == 0) return "Your target is dead";                   // SPELL_FAILED_TARGETS_DEAD (GlobalStrings.lua:7166)
                if (!SpInRange(s, target)) return "Out of range";                                             // SPELL_FAILED_OUT_OF_RANGE (GlobalStrings.lua:7147)
            }
            else if (target != null && target.Reaction == Reaction.Friendly && (need & (TfUnitAlly | TfUnitParty | TfUnitRaid | TfUnit)) != 0 && !SpInRange(s, target))
                return "Out of range";
            else if (target != null && target.Dead && target.Kind == EntityKind.Player && (need & (TfCorpseAlly | TfCorpseEnemy)) != 0 && !SpInRange(s, target))
                return "Out of range";                                                                    // rescorpse: a corpse / a player lying dead beyond a resurrection's reach
            // iteration 11 (spellfx): the target's aura states / auras (an enemy for Execute / Hammer of Wrath / Kill Shot / Conflagrate / Envenom,
            // a friend for Swiftmend) - SPELL_FAILED_TARGET_AURASTATE (GlobalStrings.lua:7168)
            bool unitSpell = enemy || (need & (TfUnitAlly | TfUnitParty | TfUnitRaid | TfUnit)) != 0;
            if (unitSpell && target != null && target.Kind != EntityKind.GameObject)
            {
                if (s.TargetAuraState != 0 && (target.AuraStateMask & (1u << (s.TargetAuraState - 1))) == 0 && !SpIgnoresAuraState(s)) return "You can't do that yet";
                if (s.TargetAuraStateNot != 0 && (target.AuraStateMask & (1u << (s.TargetAuraStateNot - 1))) != 0) return "You can't do that yet";
                if (s.TargetAuraSpell != 0 && !target.Auras.Exists(a => a.SpellId == s.TargetAuraSpell)) return "You can't do that yet";
                if (s.ExcludeTargetAuraSpell != 0 && target.Auras.Exists(a => a.SpellId == s.ExcludeTargetAuraSpell)) return "You can't do that yet";
            }
            PowerType pt;
            int cost = SpCost(s, out pt);
            if (cost > 0)
            {
                int have = SpPowerNow(s.PowerType);
                if (have >= 0 && have < cost) return SpOutOfPower(s.PowerType);
            }
            return null;
        }

        /// <summary>Unit::HasAuraState (Unit.cpp:7434-7454) as far as the client can know it: an own SPELL_AURA_ABILITY_IGNORE_AURASTATE 262 aura lets the
        /// spell through (Sudden Death -> Execute, Taste for Blood -> Overpower ...). Ours: any 262 aura of the same SpellFamilyName (the affected-spell
        /// mask is not read). Iteration 11 (spellfx).</summary>
        bool SpIgnoresAuraState(SpellInfo s)
        {
            foreach (int id in _spMyAuras) { var a = Db.Find(id); if (a != null && a.SpellFamilyName == s.SpellFamilyName && a.HasAura(SpAuraAbilityIgnoreAuraState)) return true; }
            return false;
        }
        const int SpAuraAbilityIgnoreAuraState = 262;   // SPELL_AURA_ABILITY_IGNORE_AURASTATE (SpellAuraDefines.h)
        bool SpHasAuraState(uint mask, int state, SpellInfo s) { return (state > 0 && state <= 32 && (mask & (1u << (state - 1))) != 0) || (s != null && SpIgnoresAuraState(s)); }

        /// <summary>ERR_OUT_OF_&lt;POWER&gt; (GlobalStrings.lua:3335-3343) for a Spell.dbc power type - also what the client shows for SPELL_FAILED_NO_POWER.</summary>
        static string SpOutOfPower(int powerType)
        {
            switch (powerType)
            {
                case 0: return "Not enough mana";               // ERR_OUT_OF_MANA (3338)
                case 1: return "Not enough rage";               // ERR_OUT_OF_RAGE (3340)
                case 2: return "Not enough focus";              // ERR_OUT_OF_FOCUS (3336)
                case 3: return "Not enough energy";             // ERR_OUT_OF_ENERGY (3335)
                case 5: return "Not enough runes";              // ERR_OUT_OF_RUNES (3342)
                case 6: return "Not enough runic power";        // ERR_OUT_OF_RUNIC_POWER (3343)
                case -2: return "Not enough health";            // ERR_OUT_OF_HEALTH (3337)
                default: return null;
            }
        }

        /// <summary>
        /// Spell::CheckRange for display (Spell.cpp 7331-7423, strict): melee spells reach max(RangeMax - 2 x MIN_MELEE_REACH, 0) + GetMeleeRange
        /// (max(5, reach + reach + 4/3), Unit.cpp 807-811); others RangeMax + both combat reaches (IsWithinCombatRange, Unit.cpp 774-788);
        /// the friendly range for friends.
        /// </summary>
        bool SpInRange(SpellInfo s, EntityState t)
        {
            if (t == null) return true;
            float max = t.Reaction == Reaction.Friendly && s.RangeMaxFriend > 0f ? s.RangeMaxFriend : s.RangeMax;
            if (max <= 0f) return true;
            float myReach = SpReach(_self), theirReach = SpReach(t.Guid);
            float dx = t.Pos.X - _player.Pos.X, dy = t.Pos.Y - _player.Pos.Y, dz = t.Pos.Z - _player.Pos.Z;
            float d2 = dx * dx + dy * dy + dz * dz;
            float reach;
            if ((s.RangeFlags & 1) != 0) reach = Math.Max(max - 2f * SpMinMeleeReach, 0f) + Math.Max(SpNominalMelee, myReach + theirReach + 4f / 3f);
            else reach = max + myReach + theirReach;
            return d2 < reach * reach;
        }

        float SpReach(ulong guid)
        {
            OnlineObject o;
            return _objects.TryGetValue(guid, out o) ? SpReach(o) : SpDefaultReach;
        }

        static float SpReach(OnlineObject o)
        {
            if (o == null) return SpDefaultReach;
            float r = o.F32(UF.UNIT_FIELD_COMBATREACH);
            return r > 0.01f && r < 100f ? r : SpDefaultReach;
        }

        /// <summary>SpellInfo::CheckShapeshift (the offline port, OfflineSession.CheckShapeshift) with the server's form (UNIT_FIELD_BYTES_2 byte 3).</summary>
        string SpCheckShapeshift(SpellInfo s)
        {
            int formNow = _player.ShapeshiftForm;
            uint mask = formNow > 0 && formNow <= 32 ? 1u << (formNow - 1) : 0u;
            if ((mask & s.StancesNot) != 0) return "Can't do that in this form";
            if ((mask & s.Stances) != 0) return null;
            bool actAsShifted = false;
            if (formNow != 0) { var f = Db.Form(formNow); actAsShifted = f != null && f.ActsAsShifted; }
            if (actAsShifted)
            {
                if (s.NotShapeshift) return "Can't do that while shapeshifted";
                if (s.Stances != 0) return "Must be in the right form";
            }
            else if (!s.NotNeedShapeshift && s.Stances != 0)
            {
                if ((s.Stances & (1u << 29)) != 0) return "You must be in Stealth";
                if ((s.Stances & ((1u << 16) | (1u << 17) | (1u << 18))) != 0) return "Wrong stance";
                return "Must be in the right form";
            }
            return null;
        }

        // ------------------------------------------------------------------ what a spell must be aimed at (SpellInfo::_InitializeExplicitTargetMask)

        /// <summary>
        /// The explicit target mask of a spell = AC SpellInfo::_InitializeExplicitTargetMask (SpellInfo.cpp 3029-3054): it STARTS with the
        /// Spell.dbc Targets column ("uint32 targetMask = Targets;", :3033) and adds the effects' TargetA / TargetB through
        /// SpellImplicitTargetInfo::GetExplicitTargetMask (127-209). Not included: the EFFECT_IMPLICIT_TARGET_EXPLICIT extension (the
        /// server's InitExplicitTargets, Spell.cpp 766-812, fills what the client leaves out).
        /// rescorpse fix 2026-10-04 (the user: "I cannot resurect people, it says 'I cannot attack that'"): the Targets column was left out,
        /// so every resurrection (Redemption 7328, Resurrection 2006, Ancestral Spirit 2008, Revive 50769, Rebirth 20484: Targets 0x8000
        /// TARGET_FLAG_CORPSE_ALLY, effect 113 with TargetA/B 0) was sent with mask 0 and no object, and AC's SpellInfo::CheckExplicitTarget
        /// (SpellInfo.cpp:1870-1876) answered SPELL_FAILED_BAD_TARGETS = "Invalid target" (GlobalStrings.lua:6949) = the voice
        /// HumanMale_InvalidTarget (VocalUISounds enum 11, HumanMale_err_invalidattacktarget01/02.wav). Measured over all 9751
        /// SkillLineAbility spells (P/harness/Probes/O2/rescorpse/ResCorpseFactsProbe.cs): 424 gain bits - ITEM 0x10 (349, enchants / imbues),
        /// DEST 0x40 (2), GAMEOBJECT_ITEM 0x4000 (27) and UNIT_DEAD 0x400 (9, gathering) change nothing here (SpTargets reads s.Targets for
        /// those already, or the unit / gameobject bit was set); CORPSE_ALLY 0x8000 (36 resurrections) and CORPSE_ENEMY 0x200 (Remove
        /// Insignia) now take SpCorpseTarget.
        /// </summary>
        uint SpExplicitMask(SpellInfo s)
        {
            uint m;
            if (_spMaskCache.TryGetValue(s.Id, out m)) return m;
            bool srcSet = false, dstSet = false;
            m = s.Targets;                                                       // SpellInfo.cpp:3033
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || e.Effect == 0) continue;
                m |= SpTargetMask(e.TargetA, ref srcSet, ref dstSet);
                m |= SpTargetMask(e.TargetB, ref srcSet, ref dstSet);
            }
            _spMaskCache[s.Id] = m;
            return m;
        }

        static uint SpTargetMask(int target, ref bool srcSet, ref bool dstSet)
        {
            if (target <= 0 || target * 3 + 2 >= SpImplicitTargets.Length) return 0;
            char rf = SpImplicitTargets[target * 3], ob = SpImplicitTargets[target * 3 + 1], ck = SpImplicitTargets[target * 3 + 2];
            uint m = 0;
            if (target == TargetDestTraj)
            {
                if (!srcSet) m = TfSourceLocation;
                if (!dstSet) m |= TfDestLocation;
            }
            else if (rf == 's') { if (!srcSet) m = TfSourceLocation; }
            else if (rf == 'd') { if (!dstSet) m = TfDestLocation; }
            else if (rf == 't')
            {
                if (ob == 'g') m = TfGameObject;
                else if (ob == 'i') m = TfGameObjectItem;
                else if (ob == 'u' || ob == 'd' || ob == 'b')
                    m = ck == 'e' ? TfUnitEnemy : ck == 'a' ? TfUnitAlly : ck == 'p' ? TfUnitParty : ck == 'r' ? TfUnitRaid : ck == 'P' ? TfUnitPassenger : TfUnit;
            }
            if (ob == 's') srcSet = true;
            else if (ob == 'd' || ob == 'b') dstSet = true;
            return m;
        }

        /// <summary>
        /// The SpellCastTargets the real client would send (SpellCastTargets::Read, Spell.cpp 163-214): a unit spell = TARGET_FLAG_UNIT + the
        /// packed guid (enemy: the target; ally / party / raid: a friendly target, else ourselves - AC's own fallback, Spell.cpp 797-801; any:
        /// the target, a helpful one ourselves), a gameobject spell = TARGET_FLAG_GAMEOBJECT; a spell that needs a place = TARGET_FLAG_DEST_LOCATION
        /// + packed transport guid 0 + x y z (the target's feet, else our own - there is no ground-target circle). Null = sendable, else the
        /// refusal text.
        /// </summary>
        string SpTargets(SpellInfo s, ulong targetGuid, out uint mask, out ulong obj, out WowPos dest)
        {
            ulong item;
            return SpTargets(s, targetGuid, out mask, out obj, out dest, out item);
        }

        /// <summary>Iteration 11 (spellfx): the same + the item target - a confirmed place (the green circle) / item / trade slot of WoW's
        /// "spell on the cursor", and the main hand of a SPELL_ATTR0_HELD_ITEM_ONLY spell (shaman imbues).</summary>
        string SpTargets(SpellInfo s, ulong targetGuid, out uint mask, out ulong obj, out WowPos dest, out ulong item)
        {
            mask = 0; obj = 0; dest = _player.Pos; item = 0;
            uint need = SpExplicitMask(s);
            ulong pick = targetGuid != 0 ? targetGuid : _target;
            bool pickSelf = pick != 0 && pick == _player.Guid;
            EntityState te = pick != 0 && !pickSelf ? Find(pick) : null;
            bool unitTarget = te != null && te.Kind != EntityKind.GameObject;
            // rescorpse (2026-10-04): a corpse spell (Targets CORPSE_ALLY / CORPSE_ENEMY - every resurrection, Remove Insignia) aims at a corpse
            // or at a player lying dead; a spell with unit bits too takes this way only for a dead target
            if ((need & (TfCorpseAlly | TfCorpseEnemy)) != 0 && ((need & TfUnitMask) == 0 || (te != null && te.Dead)))
            {
                // states fix 2026-10-04 (ours): the selection is a released player out of our view (a ghost is invisible to the living; a party
                // frame still selects them) - their corpse in view is what WoW aims a resurrection at (AC does not turn a ghost into its corpse:
                // SelectEffectTypeImplicitTargets takes the unit, else the corpse's owner, Spell.cpp:2090-2103)
                if (te == null && pick != 0 && !pickSelf) te = DthCorpseOf(pick);
                uint cm;
                string cw = SpCorpseTarget(s, need, pickSelf, te, out obj, out cm);
                if (cw != null) return cw;
                mask |= cm;
            }
            else if ((need & TfUnitMask) != 0)
            {
                if ((need & TfUnitEnemy) != 0)
                {
                    if (pickSelf) return "Invalid target";                                         // SPELL_FAILED_BAD_TARGETS (GlobalStrings.lua:6949)
                    if (!unitTarget) return "You have no target.";                                 // ERR_GENERIC_NO_TARGET (GlobalStrings.lua:3064)
                    if (te.Dead && (need & TfUnitDead) == 0) return "Your target is dead";         // SPELL_FAILED_TARGETS_DEAD (GlobalStrings.lua:7166)
                    obj = te.Guid;
                }
                else if ((need & (TfUnitAlly | TfUnitParty | TfUnitRaid)) != 0)
                {
                    obj = unitTarget && te.Reaction == Reaction.Friendly ? te.Guid : _player.Guid;
                    if (obj == _player.Guid && !_spSelfCastLogged && te != null)
                    {
                        _spSelfCastLogged = true;
                        WowLog.Info("Online: a helpful spell with a non-friendly target goes to ourselves (the client's auto self-cast; AC InitExplicitTargets does the same) - logged once");
                    }
                }
                else
                {
                    if (unitTarget) obj = te.Guid;
                    else if (pickSelf || !s.IsHarmful) obj = _player.Guid;
                    else return "You have no target.";                                             // ERR_GENERIC_NO_TARGET (GlobalStrings.lua:3064)
                }
                mask |= TfUnit;
            }
            else if ((need & (TfGameObject | TfGameObjectItem)) != 0 && te != null && te.Kind == EntityKind.GameObject)   // iteration 11 (spellfx): Pick Lock (26 GAMEOBJECT_ITEM) on a selected chest = TARGET_FLAG_GAMEOBJECT (SpellInfo.h TARGET_FLAG_GAMEOBJECT_MASK)
            {
                mask |= TfGameObject;
                obj = te.Guid;
            }
            if ((need & TfSourceLocation) != 0) mask |= TfSourceLocation;
            bool confirmed = _spConfirmedFor == s.Id;
            if ((need & TfDestLocation) != 0 || (s.Targets & TfDestLocation) != 0)
            {
                mask |= TfDestLocation;
                if (confirmed && _spConfirmedHasDest) dest = _spConfirmedDest;      // iteration 11 (spellfx): the green circle's place
                else
                {
                    dest = te != null ? te.Pos : _player.Pos;                       // derived dest spells (Tranquility ...) as before; AC fills a missing one anyway (Spell.cpp:810-823)
                    if (!_spDestLogged && (s.Targets & TfDestLocation) == 0)
                    {
                        _spDestLogged = true;
                        WowLog.Info("Online: " + s.Name + " needs a place by its effects only (TARGET_FLAG_DEST_LOCATION derived, Spell.dbc Targets without 0x40): sent at " +
                                    (te != null ? "the target's feet" : "our own feet") + " as the real client (no circle; logged once)");
                    }
                }
            }
            if (confirmed && _spConfirmedItem != 0) { mask |= TfItem; item = _spConfirmedItem; }
            else if (confirmed && _spConfirmedTrade) { mask |= TfTradeItem; item = TradeSlotNonTraded; }   // the trade partner's non-traded slot (Spell.cpp:526-529)
            else if (s.HeldItemOnly && (s.Targets & TfItem) != 0)                  // SPELL_ATTR0_HELD_ITEM_ONLY: the client picks the main hand (SharedDefines.h:379)
            {
                item = _self != null ? _self.U64(UF.PLAYER_FIELD_INV_SLOT_HEAD + 15 * 2) : 0;
                if (item == 0) return "Must have the proper item equipped";       // SPELL_FAILED_EQUIPPED_ITEM (GlobalStrings.lua:7052)
                mask |= TfItem;
            }
            return null;
        }

        /// <summary>
        /// rescorpse (2026-10-04): what the real client sends for a corpse spell. The selected CORPSE object (EntityState.CorpseOwnerGuid,
        /// OnlineSession.Death.cs) = TARGET_FLAG_CORPSE_ALLY / _ENEMY + the corpse guid (AC SelectEffectTypeImplicitTargets
        /// EFFECT_IMPLICIT_TARGET_EXPLICIT then hits the corpse's owner, Spell.cpp:2090-2103); a player lying dead who has not released = the
        /// same flag + the PLAYER's guid ("player which not released his spirit is Unit, but target flag for it is TARGET_FLAG_CORPSE_MASK",
        /// Spell.cpp:2091; InitExplicitTargets keeps a unit for a corpse mask, :778-781). SpellCastTargets::Read reads the object for both
        /// flags (Spell.cpp:170). Ally = not hostile (EntityState.Reaction). The refusals are ours - AC would cast and
        /// Spell::EffectResurrectNew would do nothing for a living / non-player unit (acsrc_lootdeath/SpellEffects.cpp:258-262): nothing
        /// selected "You have no target." (ERR_GENERIC_NO_TARGET, GlobalStrings.lua:3064 - WoW itself puts the spell on the cursor),
        /// ourselves / a living unit "Target is alive" (SPELL_FAILED_TARGET_NOT_DEAD, :7180), a gameobject / dead creature / our own corpse /
        /// a corpse of the other side "Invalid target" (SPELL_FAILED_BAD_TARGETS, :6949).
        /// </summary>
        string SpCorpseTarget(SpellInfo s, uint need, bool pickSelf, EntityState te, out ulong obj, out uint mask)
        {
            obj = 0; mask = 0;
            if (pickSelf) return "Target is alive";
            if (te == null) return "You have no target.";
            if (te.Kind == EntityKind.GameObject) return "Invalid target";
            if (!te.Dead) return "Target is alive";
            bool corpse = te.CorpseOwnerGuid != 0;
            if (te.Kind != EntityKind.Player || (corpse && te.CorpseOwnerGuid == _player.Guid)) return "Invalid target";
            uint flag = te.Reaction != Reaction.Hostile ? TfCorpseAlly : TfCorpseEnemy;
            if ((need & flag) == 0) return "Invalid target";
            mask = flag;
            obj = te.Guid;
            _spCorpseCasts++;
            if (_spCorpseCasts <= 5 || _spCorpseCasts % 50 == 0)
                WowLog.Info("Online: " + s.Name + " at " + (corpse ? "the corpse " + te.Guid.ToString("X") + " (" + te.Name + ", owner " + te.CorpseOwnerGuid.ToString("X") + ")" : te.Name + " lying dead (not released)") +
                            " - TARGET_FLAG_CORPSE_" + (flag == TfCorpseAlly ? "ALLY" : "ENEMY") + " + the " + (corpse ? "corpse" : "player") + "'s guid (AC Spell.cpp:2091) #" + _spCorpseCasts);
            return null;
        }
        int _spCorpseCasts;

        static void SpWriteTargets(PacketWriter w, uint mask, ulong obj, WowPos src, WowPos dest) { SpWriteTargets(w, mask, obj, src, dest, 0); }

        /// <summary>SpellCastTargets::Read order (Spell.cpp:163-214): object, ITEM | TRADE_ITEM, source, dest (string never sent).</summary>
        static void SpWriteTargets(PacketWriter w, uint mask, ulong obj, WowPos src, WowPos dest, ulong item)
        {
            w.U32(mask);
            if ((mask & (TfUnit | TfUnitMinipet | TfGameObject | TfCorpseEnemy | TfCorpseAlly)) != 0) w.PackedGuid(obj);
            if ((mask & (TfItem | TfTradeItem)) != 0) w.PackedGuid(item);
            if ((mask & TfSourceLocation) != 0) w.PackedGuid(0).F32(src.X).F32(src.Y).F32(src.Z);
            if ((mask & TfDestLocation) != 0) w.PackedGuid(0).F32(dest.X).F32(dest.Y).F32(dest.Z);
        }

        // ------------------------------------------------------------------ casting a mount (WorldSession::HandleCastSpellOpcode)

        void CastMount(int id)
        {
            if (_player.Mounted)
            {
                WowLog.Info("Online: mount " + id + " pressed while mounted - dismounting (CMSG_CANCEL_MOUNT_AURA; AC would answer NOT_MOUNTED to a second mount cast)");
                Dismount();
                return;
            }
            if (_world == null) return;
            string name = SpellName(id);
            // local refusals where AzerothCore would refuse the same way (or not answer at all)
            if (!_known.Contains(id)) { FailLocal(id, "Spell not learned", "not in the server's spellbook (AC ignores unknown spells silently)"); return; }   // SPELL_FAILED_NOT_KNOWN (GlobalStrings.lua:7103)
            if (_castId != 0) { FailLocal(id, "Another action is in progress", "cast " + _castId + " still running"); return; }                               // SPELL_FAILED_SPELL_IN_PROGRESS (GlobalStrings.lua:7160)
            if (_player.Dead) { FailLocal(id, "You are dead", "dead"); return; }                                                                                  // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
            if (SpMovingNow()) { FailLocal(id, "Can't do that while moving", "the avatar is moving (AC Spell::prepare refuses a cast-time spell while isMoving())"); return; }   // SPELL_FAILED_MOVING (GlobalStrings.lua:7083)

            WowPos at = _haveAvatar ? _avatarPos : _player.Pos;
            SpStopBeforeCast(at);
            _castCount++;
            if (_castCount == 0) _castCount = 1;                // 0 marks triggered casts in AC (m_cast_count == 0)
            // CMSG_CAST_SPELL: uint8 castCount; uint32 spellId; uint8 castFlags (0: no trajectory data); SpellCastTargets::Read: uint32 targetMask 0 = self
            _world.Send(Op.CMSG_CAST_SPELL, new PacketWriter().U8(_castCount).U32((uint)id).U8(0).U32(0));
            _castId = id;
            _castSentAt = _now;
            _castStartedAt = -1;
            _castPos = at;
            var s = Db.Find(id);
            if (s != null) SpStartGcd(s);
            WowLog.Info("Online: cast " + id + " '" + name + "' sent (castCount " + _castCount + ")");
        }

        bool SpMovingNow()
        {
            // lead fix 2026-11-05 (user: under water "Can't do while moving" though we were not moving): AC's isMoving() is the
            // MOVEMENTFLAG_MASK_MOVING flags (forward, backward, strafes, pitch, falling, ascending / descending) - SWIMMING or FLYING
            // alone is floating still, not moving (was: any swimming / flying counted)
            return _motion.Driven
                ? _motion.Forward != 0 || _motion.Strafe != 0 || _motion.Vertical != 0 || _motion.Airborne
                : _moving;
        }

        /// <summary>AC Spell::prepare / update cancel a movement-interruptible cast while isMoving() = the flags of our LAST movement packet: stop first.</summary>
        void SpStopBeforeCast(WowPos at)
        {
            if ((_sentFlags & (MoveFlags.MaskMoving | MoveFlags.MaskTurning)) != 0 && _holdUntil < 0 && _player.Guid != 0)
            {
                SendMove(Op.MSG_MOVE_STOP, _sentFlags & ~(MoveFlags.MaskMoving | MoveFlags.MaskTurning), at, at.O, 0, 0, 0, 0, 0);
                WowLog.Info("Online: MSG_MOVE_STOP sent before the cast (the last movement packet had moving flags)");
            }
        }

        void FailLocal(int id, string text, string why)
        {
            WowLog.Info("Online: cast " + id + " refused here: \"" + text + "\" (" + why + ")");
            EmitCastFailed(id, text);
        }

        void EmitCastFailed(int id, string text)
        {
            if (id == _lastFailSpell && text == _lastFailText && _now - _lastFailAt < 1.0) return;   // SPELL_FAILURE + CAST_FAILED of one cancel
            _lastFailSpell = id; _lastFailText = text; _lastFailAt = _now;
            _events.Enqueue(new GameEvent { Type = GameEventType.CastFailed, SourceGuid = _player.Guid, SpellId = id, Text = text });
            if (text != null) Error(text);
        }

        void ClearOwnCast()
        {
            _castId = 0;
            _castSentAt = _castStartedAt = -1;
            _player.CastingSpellId = 0;
            _player.CastElapsed = _player.CastTotal = 0f;
            _player.Channeling = false;
            _player.CastTargetGuid = 0;
            _spChannelId = 0;
            DpClear();                                          // S1: our cast ended (GO, failure, cancel, no answer) - so did its hold
        }

        void CancelOwnCast()
        {
            int id = _castId != 0 ? _castId : _player.CastingSpellId;
            if (id == 0) return;
            if (_spChannelId != 0 && id == _spChannelId)
            {
                // CMSG_CANCEL_CHANNELLING: uint32 spellId (WorldSession::HandleCancelChanneling, SpellHandler.cpp 653-684)
                if (_world != null) _world.Send(Op.CMSG_CANCEL_CHANNELLING, new PacketWriter().U32((uint)id));
                WowLog.Info("Online: channel " + id + " cancelled (CMSG_CANCEL_CHANNELLING)");
                ulong t = _spChannelTarget;
                ClearOwnCast();
                SpDropChannelEnd(_player.Guid, id);
                _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = _player.Guid, TargetGuid = t, SpellId = id, Text = "Interrupted" });   // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066)
                return;
            }
            if (_world != null) _world.Send(Op.CMSG_CANCEL_CAST, new PacketWriter().U8(++_cancelCounter).U32((uint)id));   // HandleCancelCastOpcode: uint8 counter; uint32 spellId
            WowLog.Info("Online: cast " + id + " cancelled (CMSG_CANCEL_CAST)");
            ClearOwnCast();
            SpCancelGcd(id);
            EmitCastFailed(id, "Interrupted");                  // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066); the server's own answer is then a duplicate
        }

        // ------------------------------------------------------------------ casting every other spell (stage O2)

        /// <summary>
        /// A spellbook spell to the server like the real client: the local refusals are the ones the client makes itself (not learned, dead,
        /// no target for an enemy spell, moving for a cast-time spell, a cooldown / GCD / cast with more than SpellQueue.Window left - AC
        /// queues the rest), toggles (Attack, Auto Shot, forms, Stealth, a queued next-swing ability) become their own packets, a mounted
        /// cast on the ground dismounts first (offline MountedCastGate), then CMSG_CAST_SPELL with the spell's SpellCastTargets. Power, range,
        /// line of sight and every other rule are the server's (SMSG_CAST_FAILED).
        /// </summary>
        void SpCast(int id, ulong targetGuid)
        {
            if (_world == null) return;
            var s = Db.Find(id);
            if (s == null)
            {
                // without the client's Spell.dbc (no game files - never in play) the spell cannot be aimed: the pre-O2 answer (ours)
                WowLog.Info("Online: cast " + id + " not sent - not in this client's Spell.dbc (\"" + NotYet + "\")");
                Error(NotYet);
                return;
            }
            if (!_known.Contains(id)) { FailLocal(id, "Spell not learned", "not in the server's spellbook (AC ignores unknown spells silently)"); return; }   // SPELL_FAILED_NOT_KNOWN (GlobalStrings.lua:7103)
            if (_player.Dead) { FailLocal(id, "You are dead", "dead"); return; }                                                                                  // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
            if (s.IsPassive)
            {
                if (_spPassiveLogged++ < 3) WowLog.Info("Online: " + s.Name + " is passive - nothing is sent (the real client does nothing either)");
                return;
            }
            if (id == SpAttackSpell || (s.Effects[0] != null && s.Effects[0].Effect == SpellEffect.Attack))
            {
                if (_player.AutoAttacking) StopAttack();
                else StartAttack(targetGuid != 0 ? targetGuid : _target);
                return;
            }
            if (s.AutoRepeat && _spAutoRepeat == id) { SpStopAutoRepeat(true); return; }
            var form = s.AuraOf(SpellAura.ModShapeshift);
            if (form != null && form.MiscValue != 0 && form.MiscValue == _player.ShapeshiftForm)
            {
                if (s.SpellFamilyName != 4) CancelAura(id);     // druid forms leave by the button; warrior stances stay (offline rule)
                return;
            }
            if (s.HasAura(SpellAura.ModStealth) && _spMyAuras.Contains(id)) { CancelAura(id); return; }
            if (s.OnNextSwing && _spNextSwing == id)
            {
                // HandleCancelCastOpcode interrupts CURRENT_MELEE_SPELL (SpellHandler.cpp 554-566)
                _world.Send(Op.CMSG_CANCEL_CAST, new PacketWriter().U8(++_cancelCounter).U32((uint)id));
                WowLog.Info("Online: next-swing " + id + " '" + s.Name + "' dequeued (CMSG_CANCEL_CAST)");
                _spNextSwing = 0;
                return;
            }
            bool timed = s.CastTimeMs > 0 || s.IsChanneled;
            float cd = SpCooldownLeft(id);
            if (cd != float.MaxValue && cd > SpQueueWindow) { FailLocal(id, "Spell is not ready yet", "cooldown " + cd.ToString("0.0") + " s"); return; }          // ERR_SPELL_COOLDOWN (GlobalStrings.lua:3493)
            // iteration 11 (spellfx): the circle / the item cursor first - the GCD, moving and mounted checks run at the confirm (one may aim during the GCD)
            if (SpNeedsAim(s, targetGuid, _dpAsk == id || _dpBonusAsk == id)) return;
            if (_player.Targeting != null && _player.Targeting.SpellId != id)
            {
                WowLog.Info("Online: " + _player.Targeting.Name + " no longer waits (" + s.Name + " pressed)");   // WoW: another spell clears the one on the cursor
                _player.Targeting = null;
            }
            float gcd = s.StartRecoveryTimeMs > 0 && s.StartRecoveryCategory == _spGcdCategory && _spGcdEnd > _now ? (float)(_spGcdEnd - _now) : 0f;
            if (gcd > SpQueueWindow) { FailLocal(id, "Spell is not ready yet", "global cooldown " + gcd.ToString("0.00") + " s"); return; }                           // ERR_SPELL_COOLDOWN (GlobalStrings.lua:3493)
            if (_castId != 0 && _spChannelId == 0)
            {
                float left = _castStartedAt >= 0 ? _player.CastTotal - _player.CastElapsed : CastAnswerTimeout;
                // S1 (PLAN10 rule 6): a cast the realm holds (or will hold) is a cast in progress until the push - the realm refuses
                // another one (Spell::prepare, SPELL_FAILED_SPELL_IN_PROGRESS, Spell.cpp 3618-3622), so it is refused here like the real client
                if (_dpState >= DpAsked && _dpState <= DpReady) left = float.MaxValue;
                if (left > SpQueueWindow) { FailLocal(id, "Another action is in progress", "cast " + _castId + (_dpState >= DpAsked && _dpState <= DpReady ? " held by the realm until the push" : " still running")); return; }   // SPELL_FAILED_SPELL_IN_PROGRESS (GlobalStrings.lua:7160)
            }
            if (timed && SpMovingNow()) { FailLocal(id, "Can't do that while moving", "the avatar is moving (AC Spell::prepare, Spell.cpp 3666-3677)"); return; }  // SPELL_FAILED_MOVING (GlobalStrings.lua:7083)
            if (_player.Mounted && (s.Attributes[0] & Attr0AllowWhileMountedO2) == 0)
            {
                if (_motion.Driven && _motion.Flying) { FailLocal(id, "You are mounted.", "flying"); return; }                                                         // SPELL_FAILED_NOT_MOUNTED (GlobalStrings.lua:7104)
                WowLog.Info("Online: " + s.Name + " while mounted on the ground - dismounting first (the client's auto-dismount, offline MountedCastGate)");
                Dismount();
            }
            uint mask; ulong obj; WowPos dest; ulong item;
            string why = SpTargets(s, targetGuid, out mask, out obj, out dest, out item);
            if (why != null) { FailLocal(id, why, "targets"); return; }

            WowPos at = _haveAvatar ? _avatarPos : _player.Pos;
            if (timed) SpStopBeforeCast(at);
            if (targetGuid != 0 && targetGuid != _player.Guid && targetGuid != _target) SetTarget(targetGuid);   // like offline: the explicit target becomes the selection
            _castCount++;
            if (_castCount == 0) _castCount = 1;
            // CMSG_CAST_SPELL (HandleCastSpellOpcode, SpellHandler.cpp 378-552): uint8 castCount; uint32 spellId; uint8 castFlags (0 = no
            // trajectory, HandleClientCastFlags reads nothing); SpellCastTargets::Read (Spell.cpp 163-214)
            var w = new PacketWriter().U8(_castCount).U32((uint)id).U8(0);
            SpWriteTargets(w, mask, obj, at, dest, item);
            // (only a cast that starts now: one queued behind a cast still running is not held - its SPELL_START comes later)
            bool hold = _dpAsk == id && timed && !s.IsChanneled && _dpMode == 1 && (_castId == 0 || _spChannelId != 0);
            if (_dpAsk == id && !hold && _dpMode == 1) WowLog.Info("Online: " + s.Name + " queued behind the cast still running - sent without a hold (it fires at its end)");
            _dpAsk = 0;
            // S1: the hold request goes out right BEFORE the cast - AzerothCore handles one session's packets strictly in order
            // (LockedQueue::next(result, check), LockedQueue.h 98-113), so the module knows about it when Spell::prepare runs
            if (hold) DpSend("HOLD\t" + id + "\t" + _castCount);
            // S1 (PLAN10 s5): a cast released with Immersive casting WITHOUT a hold - flagged for the realm's damage bonus, right before it
            bool bonus = !hold && _dpBonusAsk == id && _dpMode == 1;
            _dpBonusAsk = 0;
            if (bonus)
            {
                DpSend("BONUS\t" + id + "\t" + _castCount);
                _dpBonuses++;
                if (_dpBonuses <= SpLogCasts || _dpBonuses % 50 == 0)
                    WowLog.Info("Online: " + s.Name + " (" + id + ") released with Immersive casting - the realm adds +" + (_dpBonusPct >= 0 ? _dpBonusPct : 10) +
                                "% damage (DPHOLD BONUS, castCount " + _castCount + ") #" + _dpBonuses);
            }
            FlushFacing(obj != 0 ? obj : targetGuid);       // iteration 11 (motion): the avatar's facing reaches the server before the cast
            _world.Send(Op.CMSG_CAST_SPELL, w);
            NoteOwnCast(id);                                // iteration 11 (motion)
            if (hold)
            {
                _dpSpell = id;
                _dpCount = _castCount;
                _dpState = DpAsked;
                _dpStartedAt = _now;
                _dpHolds++;
                if (_dpHolds <= SpLogCasts || _dpHolds % 50 == 0)
                    WowLog.Info("Online: " + s.Name + " (" + id + ") cast at once, the realm is asked to hold it at the end of its cast (DPHOLD HOLD, castCount " + _castCount + ") #" + _dpHolds);
            }
            _spCastsSent++;
            if (_spCastsSent <= SpLogCasts || _spCastsSent % 50 == 0)
                WowLog.Info("Online: cast " + id + " '" + s.Name + "' sent (castCount " + _castCount + ", targets 0x" + mask.ToString("X") +
                            (obj != 0 ? (obj == _player.Guid ? " self" : " unit 0x" + obj.ToString("X")) : "") + ((mask & TfDestLocation) != 0 ? " at " + dest : "") +
                            ((mask & (TfItem | TfTradeItem)) != 0 ? " item 0x" + item.ToString("X") : "") +
                            (timed ? ", " + (s.IsChanneled ? "channel" : s.CastTimeMs + " ms cast") : ", instant") + ") #" + _spCastsSent);
            SpStartGcd(s);
            if (timed && (_castId == 0 || _spChannelId != 0))
            {
                _castId = id;
                _castSentAt = _now;
                _castStartedAt = -1;
                _castPos = at;
                _spPendingId = 0;
            }
            else if (timed)
            {
                _spPendingId = id;                              // queued by the server behind the cast still running (its SPELL_START takes over)
                _spPendingSentAt = _now;
            }
            else if (s.OnNextSwing)
            {
                _spNextSwing = id;
                if (!_player.AutoAttacking && obj != 0 && obj != _player.Guid) StartAttack(obj);   // the real client swings too (offline: StartAttack)
            }
            else if (s.AutoRepeat)
            {
                _spAutoRepeat = id;
                _player.AutoShooting = true;
                SetSheath(SheathState.Ranged);
                _events.Enqueue(new GameEvent { Type = GameEventType.AttackStarted, SourceGuid = _player.Guid, TargetGuid = obj, SpellId = 75 });   // offline SetAutoShot
            }
            else
            {
                _spPendingId = id;
                _spPendingSentAt = _now;
            }
        }

        /// <summary>CMSG_CANCEL_AUTO_REPEAT_SPELL (empty; HandleCancelAutoRepeatSpellOpcode, SpellHandler.cpp 646-651) or the server's SMSG_CANCEL_AUTO_REPEAT.</summary>
        void SpStopAutoRepeat(bool send)
        {
            if (_spAutoRepeat == 0) return;
            if (send && _world != null) _world.Send(Op.CMSG_CANCEL_AUTO_REPEAT_SPELL);
            WowLog.Info("Online: auto-repeat " + _spAutoRepeat + " '" + SpellName(_spAutoRepeat) + "' off" + (send ? " (CMSG_CANCEL_AUTO_REPEAT_SPELL)" : " (the server stopped it)"));
            _spAutoRepeat = 0;
            _player.AutoShooting = false;
            _events.Enqueue(new GameEvent { Type = GameEventType.AttackStopped, SourceGuid = _player.Guid, TargetGuid = _target, SpellId = 75 });   // offline SetAutoShot(false)
        }

        // ------------------------------------------------------------------ SMSG_SPELL_START / GO / failures for every caster

        /// <summary>SpellCastTargets::Write: uint32 mask; [unit|corpse|gameobject|minipet: packed guid]; [item: packed guid]; [source: packed guid + 3 floats]; [dest: same]; [string].</summary>
        static uint ReadTargets(WowPacket p, out ulong unitTarget) { bool d; WowPos x; return ReadTargets(p, out unitTarget, out d, out x); }

        /// <summary>Iteration 11 (spellfx): the same + the place (SpellCastTargets::Write, Spell.cpp:216-251); a place on a transport (an offset) is no world place.</summary>
        static uint ReadTargets(WowPacket p, out ulong unitTarget, out bool hasDest, out WowPos dest)
        {
            unitTarget = 0; hasDest = false; dest = default(WowPos);
            uint mask = p.U32();
            if ((mask & (TfUnit | TfCorpseAlly | TfGameObject | TfCorpseEnemy | TfUnitMinipet)) != 0) unitTarget = p.PackedGuid();
            if ((mask & (TfItem | TfTradeItem)) != 0) p.PackedGuid();
            if ((mask & TfSourceLocation) != 0) { p.PackedGuid(); p.F32(); p.F32(); p.F32(); }
            if ((mask & TfDestLocation) != 0) { ulong t = p.PackedGuid(); float x = p.F32(), y = p.F32(), z = p.F32(); hasDest = t == 0; dest = new WowPos(x, y, z, 0f); }
            if ((mask & TfString) != 0) p.CString();
            return mask;
        }

        void CheckRead(WowPacket p)
        {
            _spellPackets++;
            if (p.Remaining == 0) return;
            if (++_spellParseProblems <= 5)
                WowLog.Warn("Online: " + p.Name + " (" + p.Length + " bytes) left " + p.Remaining + " bytes unread - layout differs from AzerothCore's writer (logged up to 5 times)");
        }

        /// <summary>Leftover bytes of SMSG_CAST_FAILED / SPELL_FAILURE(_OTHER): a layout problem like CheckRead, but not counted in SpellPacketsParsed (iteration 7 counts START / GO there).</summary>
        void SpCheckLeft(WowPacket p)
        {
            if (p.Remaining == 0) return;
            if (++_spellParseProblems <= 5)
                WowLog.Warn("Online: " + p.Name + " (" + p.Length + " bytes) left " + p.Remaining + " bytes unread - layout differs from AzerothCore's writer (logged up to 5 times)");
        }

        /// <summary>Odd data in a combat log packet: logged once per opcode, never thrown into the frame (PLAN9 rule 2).</summary>
        void SpOdd(WowPacket p, string what)
        {
            _spellParseProblems++;
            if (_spOddLogged.Add(p.Opcode)) WowLog.Warn("Online: " + p.Name + " (" + p.Length + " bytes): " + what + " - skipped (logged once per opcode)");
        }

        /// <summary>
        /// SMSG_SPELL_START (Spell::SendSpellStart, Spell.cpp 4865-4944): packed item-or-caster; packed caster; uint8 castCount; uint32 spell;
        /// uint32 castFlags; int32 castTime; SpellCastTargets; [POWER_LEFT_SELF 0x800: uint32 power]; [PROJECTILE 0x20: uint32 x2]; [UNKNOWN_23 0x400000: uint32 x2].
        /// </summary>
        void OnSpellStart(WowPacket p)
        {
            p.PackedGuid();
            ulong caster = p.PackedGuid();
            p.U8();
            int spell = (int)p.U32();
            uint flags = p.U32();
            int timer = p.I32();
            ulong target;
            ReadTargets(p, out target);
            if ((flags & CastFlagPowerLeftSelf) != 0) p.U32();
            if ((flags & CastFlagProjectile) != 0) { p.U32(); p.U32(); }
            if ((flags & CastFlagUnknown23) != 0) { p.U32(); p.U32(); }
            CheckRead(p);
            if (caster == _player.Guid)
            {
                if (spell == _spPendingId) _spPendingId = 0;   // answered
                if (timer <= 0) return;                         // an instant / next-swing / auto-repeat start: no bar (the GO follows)
                _castId = spell;
                _castStartedAt = _now;
                if (_castSentAt < 0) _castSentAt = _now;
                _castStartMs = timer;
                _player.CastingSpellId = spell;
                _player.CastElapsed = 0f;
                _player.CastTotal = Math.Max(0, timer) / 1000f;
                _player.Channeling = false;
                _player.CastTargetGuid = target != 0 ? target : _player.Guid;
                _events.Enqueue(new GameEvent { Type = GameEventType.CastStarted, SourceGuid = _player.Guid, TargetGuid = _player.CastTargetGuid, Amount = timer, SpellId = spell, Text = SpellName(spell) });
                return;
            }
            if (timer <= 0) return;
            OnlineObject o;
            if (!_objects.TryGetValue(caster, out o) || o.State == null) return;
            o.CastSpellId = spell;
            o.CastStart = _now;
            o.CastMs = Math.Max(0, timer);
            o.State.CastingSpellId = spell;
            o.State.CastProgress = 0f;
            o.State.Channeling = false;
            o.State.CastTargetGuid = target;
            if (!_castingOthers.Contains(o)) _castingOthers.Add(o);
            _spOtherCasts++;
            _events.Enqueue(new GameEvent { Type = GameEventType.CastStarted, SourceGuid = caster, TargetGuid = target, Amount = timer, SpellId = spell, Text = SpellName(spell) });
        }

        /// <summary>
        /// SMSG_SPELL_GO (Spell::SendSpellGo, Spell.cpp 4946-5070 + WriteSpellGoTargets 5164-5222): packed item-or-caster; packed caster;
        /// uint8 castCount; uint32 spell; uint32 castFlags; uint32 time; uint8 hits, hits x uint64; uint8 misses, misses x (uint64, uint8 cond
        /// [REFLECT: uint8]); SpellCastTargets; [0x800: uint32 power]; [RUNE_LIST 0x200000: uint8 before, uint8 after, one uint8 per rune used];
        /// [ADJUST_MISSILE 0x20000: float, uint32]; [PROJECTILE 0x20: uint32 x2]; [VISUAL_CHAIN 0x80000: uint32 x2]; [target mask DEST_LOCATION: uint8 0].
        /// </summary>
        void OnSpellGo(WowPacket p)
        {
            p.PackedGuid();
            ulong caster = p.PackedGuid();
            int count = p.U8();
            int spell = (int)p.U32();
            uint flags = p.U32();
            p.U32();
            int hits = p.U8();
            ulong firstHit = 0;
            ulong[] hitList = hits > 0 ? new ulong[hits] : null;   // iteration 11 (spellfx): EVERY hit (Spell.cpp:5188), not only the first
            for (int i = 0; i < hits; i++) { ulong g = p.U64(); hitList[i] = g; if (i == 0) firstHit = g; }
            int misses = p.U8();
            ulong[] missGuid = misses > 0 ? new ulong[misses] : null;
            int[] missCond = misses > 0 ? new int[misses] : null;
            for (int i = 0; i < misses; i++)
            {
                missGuid[i] = p.U64();
                missCond[i] = p.U8();
                if (missCond[i] == SpellMissReflect) p.U8();
            }
            ulong target; bool atDest; WowPos destPos;
            uint mask = ReadTargets(p, out target, out atDest, out destPos);
            if ((flags & CastFlagPowerLeftSelf) != 0) p.U32();
            if ((flags & CastFlagRuneList) != 0)
            {
                int before = p.U8(), after = p.U8();
                var passed = new List<byte>(MaxRunes);
                for (int i = 0; i < MaxRunes; i++) if ((before & (1 << i)) != 0 && (after & (1 << i)) == 0) passed.Add(p.U8());
                if (caster == _player.Guid) _runes.OnSpellGo(before, after, passed.ToArray());   // iteration 11 (spellfx): Spell.cpp:5033-5052
            }
            if ((flags & CastFlagAdjustMissile) != 0) { p.F32(); p.U32(); }
            if ((flags & CastFlagProjectile) != 0) { p.U32(); p.U32(); }
            if ((flags & CastFlagVisualChain) != 0) { p.U32(); p.U32(); }
            if ((mask & TfDestLocation) != 0) p.U8();
            CheckRead(p);
            ulong shownTarget = target != 0 ? target : firstHit;
            if (caster == _player.Guid)
            {
                bool current = spell == _castId || spell == _player.CastingSpellId;
                bool triggered = count == 0 && (flags & CastFlagPending) != 0;
                if (current)
                {
                    WowLog.Info("Online: cast " + spell + " -> SPELL_START " + (_castStartedAt >= 0 ? _castStartMs + " ms" : "(none)") + " -> SPELL_GO (" + hits + " hit" + (hits == 1 ? (firstHit == _player.Guid ? ": us" : "") : "s") +
                                (misses > 0 ? ", " + misses + " missed" : "") + ")");
                    if (_dpState == DpReleased)
                        WowLog.Info("Online: " + SpellName(spell) + " released by the realm at the push (held " + (_dpReleasedAt - _dpReadyAt).ToString("0.0") + " s, SPELL_GO " + ((_now - _dpReleasedAt) * 1000.0).ToString("0") + " ms after DPHOLD GO)");
                    else if (_dpState == DpAsked || _dpState == DpArmed)
                        WowLog.Info("Online: " + SpellName(spell) + " went off at the end of its cast without DPHOLD READY - the realm did not hold it" + (_dpState == DpAsked ? " (no DPHOLD ARMED either)" : ""));
                    ClearOwnCast();
                }
                else if (spell == _spPendingId)
                {
                    _spPendingId = 0;
                    if (_spCastsSent <= SpLogCasts) WowLog.Info("Online: cast " + spell + " -> SPELL_GO (" + hits + " hit" + (hits == 1 ? "" : "s") + (misses > 0 ? ", " + misses + " missed" : "") + ")");
                }
                if (spell == _spNextSwing) _spNextSwing = 0;
                if (!triggered && _known.Contains(spell))
                {
                    var s = Db.Find(spell);
                    if (s != null)
                    {
                        SpStartCooldown(s);
                        if (s.AutoRepeat) _player.AttackCounter++;   // the shot of Auto Shot / Shoot (the desktop weapon animation, as offline)
                        else if (s.OnNextSwing) CombatMainHandSwing(_player.Guid);   // Heroic Strike replaced the white swing: its animation + swing timer (combat's hook, offline StepMelee)
                    }
                }
                _spLastGoSpell = spell;
                _spLastGoTarget = shownTarget;
                _events.Enqueue(new GameEvent { Type = GameEventType.CastSucceeded, SourceGuid = _player.Guid, TargetGuid = shownTarget, SpellId = spell, Text = SpellName(spell),
                                                Hits = hitList, AtDest = atDest, Pos = destPos });   // iteration 11 (spellfx): every hit + the place
            }
            else
            {
                // PLAN9 s5: every other caster (creatures, normal WoW players, Somnium players' characters) gets the offline creature
                // semantics (OfflineSession.UnitSpells FinishUnitCast): CastSucceeded with the caster's guid, then the release animation
                OnlineObject o;
                bool seen = _objects.TryGetValue(caster, out o) && o.State != null;
                if (seen && o.CastSpellId == spell && !o.State.Channeling) EndOtherCast(o);
                _events.Enqueue(new GameEvent { Type = GameEventType.CastSucceeded, SourceGuid = caster, TargetGuid = shownTarget, SpellId = spell, Text = SpellName(spell),
                                                Hits = hitList, AtDest = atDest, Pos = destPos });   // iteration 11 (spellfx): every hit + the place
                if (seen && (flags & CastFlagPending) == 0)
                {
                    var si = Db.Find(spell);
                    if (si != null && si.OnNextSwing) CombatMainHandSwing(caster);   // the white swing it replaced (combat's hook; AC Unit.cpp 2796-2820)
                    else if (SpReleaseAnimates(o, si)) o.State.AttackCounter++;
                }
                SpRememberGo(caster, spell, shownTarget);
                if (_spOtherGoLogged < 5 && o != null && o.TypeId == TypeId.Player)
                {
                    _spOtherGoLogged++;
                    WowLog.Info("Online: another player's cast 0x" + caster.ToString("X") + (o.State != null && o.State.IsSomniumPlayer ? " (a Somnium player's character)" : "") + " '" + SpellName(spell) + "' " + spell +
                                " -> CastSucceeded at 0x" + shownTarget.ToString("X") + " (" + hits + " hit" + (hits == 1 ? "" : "s") + (misses > 0 ? ", " + misses + " missed" : "") + "; the spell's visual kits and sounds play on it - logged 5 times)");
                }
            }
            for (int i = 0; i < misses; i++) SpMiss(caster, missGuid[i], spell, missCond[i]);
        }

        /// <summary>
        /// A cast release that steps the caster's AttackCounter (the view plays its attack sequence + attack sound): every creature cast,
        /// as offline (OfflineSession.UnitSpells FinishUnitCast: "cast release: views may play the spell-cast animation"); another player's
        /// only for weapon spells (DmgClass melee / ranged - GearAnimLocal.OnCast's rule for our own) - their spell casts animate from
        /// CastingSpellId (ReadySpell, then SpellCast). Triggered instants (CAST_FLAG_PENDING, Spell.cpp 4958) never reach here.
        /// </summary>
        static bool SpReleaseAnimates(OnlineObject o, SpellInfo s)
        {
            if (o.TypeId == TypeId.Unit) return true;
            if (o.TypeId != TypeId.Player) return false;
            return s != null && !s.IsPassive && (s.DmgClass == DmgClassMelee || s.DmgClass == DmgClassRanged);
        }

        /// <summary>The last SPELL_GO of another caster: its channel's MSG_CHANNEL_START follows it (Spell::cast -> SendSpellGo, then handle_immediate -> SendChannelStart).</summary>
        void SpRememberGo(ulong caster, int spell, ulong target)
        {
            if (_spOtherGo.Count >= 512) _spOtherGo.Clear();
            _spOtherGo[caster] = new SpLastGo { Spell = spell, Target = target, At = _now };
        }

        void EndOtherCast(OnlineObject o)
        {
            o.CastSpellId = 0;
            if (o.State != null) { o.State.CastingSpellId = 0; o.State.CastProgress = 0f; o.State.Channeling = false; }
            _castingOthers.Remove(o);
        }

        /// <summary>
        /// SMSG_CAST_FAILED (Spell::WriteCastResultInfo, Spell.cpp 4704-4807): uint8 castCount; uint32 spell; uint8 result; + the per-result
        /// extras (SpellCastResults.ReadExtras). SPELL_FAILED_NO_POWER shows ERR_OUT_OF_&lt;POWER&gt; of the spell's power type (it has no text of its own).
        /// </summary>
        void OnCastFailed(WowPacket p)
        {
            p.U8();
            int spell = (int)p.U32();
            int result = p.U8();
            uint a, b;
            int extras = SpellCastResults.ReadExtras(p, result, out a, out b);
            SpCheckLeft(p);
            string text = SpellCastResults.Text(result, a, b, extras, _files);
            if (text == null && result == ResultNoPower) { var s = Db.Find(spell); if (s != null) text = SpOutOfPower(s.PowerType); }
            WowLog.Info("Online: CAST_FAILED " + spell + " result " + result + " " + SpellCastResults.Name(result) + (extras > 0 ? " (" + a + (extras > 1 ? ", " + b : "") + ")" : "") +
                        (text != null ? " \"" + text + "\"" : " (no WoW text for this code - nothing shown)"));
            if (spell == _castId || spell == _player.CastingSpellId) ClearOwnCast();
            if (spell == _spPendingId) _spPendingId = 0;
            if (spell == _spNextSwing) _spNextSwing = 0;
            // an auto-repeat spell stays on after a failed shot (Spell::prepare casts it anyway, Spell.cpp 3635) until SMSG_CANCEL_AUTO_REPEAT
            SpCancelGcd(spell);                                 // Spell::prepare fails before TriggerGlobalCooldown (Spell.cpp 3634-3652)
            EmitCastFailed(spell, text);
        }

        /// <summary>SMSG_SPELL_FAILURE / SMSG_SPELL_FAILED_OTHER (Spell::SendInterrupted, Spell.cpp 5325-5340, both sent to everyone near): packed caster; uint8 castCount; uint32 spell; uint8 result.</summary>
        void OnSpellFailure(WowPacket p, bool other)
        {
            ulong caster = p.PackedGuid();
            p.U8();
            int spell = (int)p.U32();
            p.U8();
            SpCheckLeft(p);
            if (caster == _player.Guid)
            {
                if (other) return;                              // the FAILED_OTHER copy of our own interrupt
                if (SpInterruptChannelEnd(caster, spell)) return;
                if (_spChannelId != 0 && spell == _spChannelId)
                {
                    ulong t = _spChannelTarget;
                    ClearOwnCast();
                    _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = _player.Guid, TargetGuid = t, SpellId = spell, Text = "Interrupted" });   // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066)
                    return;
                }
                if (spell == _spNextSwing) _spNextSwing = 0;
                if (spell != _castId && spell != _player.CastingSpellId) return;
                WowLog.Info("Online: cast " + spell + " interrupted (SMSG_SPELL_FAILURE)");
                ClearOwnCast();
                SpCancelGcd(spell);                             // Spell::cancel PREPARING -> CancelGlobalCooldown (Spell.cpp 3821-3823)
                EmitCastFailed(spell, "Interrupted");           // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066)
                return;
            }
            if (SpInterruptChannelEnd(caster, spell)) return;
            OnlineObject o;
            if (!_objects.TryGetValue(caster, out o) || o.State == null || (o.CastSpellId != spell && o.State.CastingSpellId != spell)) return;
            bool channel = o.State.Channeling;
            ulong ct = o.State.CastTargetGuid;
            EndOtherCast(o);
            _events.Enqueue(new GameEvent { Type = channel ? GameEventType.ChannelEnded : GameEventType.CastFailed, SourceGuid = caster, TargetGuid = ct, SpellId = spell, Text = "Interrupted" });
        }

        /// <summary>
        /// S1 (PLAN10 s5): the Dreamport's own "Immersive Mode" buff in PlayerState.Auras while ImmersiveMode.Active (VR + Immersive
        /// casting). Never sent to the server (CancelAura ignores ids &lt;= 0) and kept through the server's aura updates (OnAuraUpdate).
        /// </summary>
        void SpTickImmersiveBuff()
        {
            // Online the REALM adds the damage: no buff on a realm that said it has no Dreamport hold module (or a module without the
            // bonus), so the buff never promises damage the realm does not add. Until the realm answers (a few seconds) it shows.
            bool realmAdds = _dpMode == 0 || (_dpMode == 1 && _dpBonusPct > 0);
            bool want = ImmersiveMode.Active && realmAdds;
            if (want == _spImmersiveShown) return;
            _spImmersiveShown = want;
            _player.Auras.Remove(_spImmersiveView);
            if (want) _player.Auras.Add(_spImmersiveView);
            WowLog.Info("Online: buff '" + ImmersiveMode.BuffName + "' " + (want ? "shown (Immersive casting is on in VR: +" + (_dpBonusPct > 0 ? _dpBonusPct : 10) + "% damage on spells you push, added by the realm's Dreamport hold module; ours, never sent to the realm)"
                        : "removed (" + (!ImmersiveMode.Active ? "Immersive casting off / not in VR" : _dpMode == -1 ? "this realm has no Dreamport hold module - it adds no immersive damage" : "this realm's hold module has no damage bonus") + ")"));
        }

        void TickCasts()
        {
            DpTick();
            SpTickImmersiveBuff();
            if (_castId != 0 && _spChannelId == 0)
            {
                bool noAnswer = _castStartedAt < 0 ? _now - _castSentAt > CastAnswerTimeout : _now - _castStartedAt > _player.CastTotal + CastAnswerTimeout;
                // S1: held by the realm - it waits up to its own time-out; after the push the SPELL_GO is due within CastAnswerTimeout
                if (_dpState == DpReady) noAnswer = _now - _dpReadyAt > DpServerHoldTimeout + CastAnswerTimeout;
                else if (_dpState == DpReleased) noAnswer = _now - _dpReleasedAt > CastAnswerTimeout;
                if (noAnswer)
                {
                    int id = _castId;
                    WowLog.Warn("Online: cast " + id + " got no " + (_castStartedAt < 0 ? "SPELL_START / CAST_FAILED" : "SPELL_GO / failure") + " within " + CastAnswerTimeout +
                                " s - \"No answer from the server\" (not in the server's spellbook, or dropped)");
                    ClearOwnCast();
                    EmitCastFailed(id, "No answer from the server");   // ours (PLAN7 rule 6)
                }
                else if (_castStartedAt >= 0) _player.CastElapsed = (float)Math.Min(Math.Max(0.0, _now - _castStartedAt), _player.CastTotal);
            }
            if (_spChannelId != 0)
            {
                double t = _now - _spChannelStart;
                _player.CastElapsed = (float)Math.Min(Math.Max(0.0, t), _player.CastTotal);
                if (t > _player.CastTotal + SpChannelSlack)
                {
                    WowLog.Info("Online: channel " + _spChannelId + " ran out without MSG_CHANNEL_UPDATE 0 - ended here");
                    int id = _spChannelId; ulong ct = _spChannelTarget;
                    ClearOwnCast();
                    _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = _player.Guid, TargetGuid = ct, SpellId = id });
                }
            }
            if (_spPendingId != 0 && _now - _spPendingSentAt > CastAnswerTimeout)
            {
                int id = _spPendingId;
                _spPendingId = 0;
                WowLog.Warn("Online: cast " + id + " got no SPELL_START / SPELL_GO / CAST_FAILED within " + CastAnswerTimeout + " s - \"No answer from the server\"");
                SpCancelGcd(id);
                EmitCastFailed(id, "No answer from the server");   // ours (PLAN7 rule 6)
            }
            for (int i = _castingOthers.Count - 1; i >= 0; i--)
            {
                var o = _castingOthers[i];
                if (o.State == null) { _castingOthers.RemoveAt(i); continue; }
                double t = _now - o.CastStart;
                o.State.CastProgress = o.CastMs > 0 ? (float)Math.Min(1.0, t * 1000.0 / o.CastMs) : 1f;
                if (o.State.Channeling)
                {
                    ulong ch = o.U64(UF.UNIT_FIELD_CHANNEL_OBJECT);   // Spell::SendChannelStart sets it after the packet (Spell.cpp 5382-5383)
                    if (ch != 0) o.State.CastTargetGuid = ch;
                }
                if (t * 1000.0 > o.CastMs + 2000.0)
                {
                    bool channel = o.State.Channeling;
                    int id = o.CastSpellId; ulong ct = o.State.CastTargetGuid;
                    EndOtherCast(o);                              // no SPELL_GO / channel end seen (out of range): stop drawing the bar
                    if (channel) _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = o.Guid, TargetGuid = ct, SpellId = id });
                }
            }
            SpFlushChannelEnds();
            SpTickHeld();
            SpTickRelease();
            _player.GcdRemaining = _spGcdEnd > _now ? (float)(_spGcdEnd - _now) : 0f;
            if (_now >= _spCdSweepAt && _spCdEnd.Count > 0)
            {
                _spCdSweepAt = _now + 1.0;
                _spCdKeys.Clear();
                foreach (var kv in _spCdEnd) if (kv.Value != double.MaxValue && kv.Value <= _now) _spCdKeys.Add(kv.Key);
                for (int i = 0; i < _spCdKeys.Count; i++) _spCdEnd.Remove(_spCdKeys[i]);
            }
        }

        // ------------------------------------------------------------------ channels (MSG_CHANNEL_START / UPDATE) and pushback (SMSG_SPELL_DELAYED)

        /// <summary>MSG_CHANNEL_START (Spell::SendChannelStart, Spell.cpp 5361-5387): packed caster; uint32 spell; uint32 duration ms. After the CastSucceeded of the same spell: ChannelStarted.</summary>
        void SpOnChannelStart(WowPacket p)
        {
            ulong caster = p.PackedGuid();
            int spell = (int)p.U32();
            int ms = p.I32();
            CheckRead(p);
            if (caster == _player.Guid)
            {
                SpDropChannelEnd(caster, 0);
                _spChannelId = spell;
                _spChannelStart = _now;
                _spChannelMs = Math.Max(0, ms);
                _spChannelTarget = _spLastGoSpell == spell && _spLastGoTarget != 0 ? _spLastGoTarget : (_target != 0 ? _target : _player.Guid);
                _castId = spell;                                // the walker holds its START during the channel too (AC interrupts it on movement)
                _castPos = _haveAvatar ? _avatarPos : _player.Pos;
                _castSentAt = _castStartedAt = _now;
                _player.CastingSpellId = spell;
                _player.Channeling = true;
                _player.CastTotal = _spChannelMs / 1000f;
                _player.CastElapsed = 0f;
                _player.CastTargetGuid = _spChannelTarget;
                WowLog.Info("Online: channel " + spell + " '" + SpellName(spell) + "' " + ms + " ms (MSG_CHANNEL_START)");
                _events.Enqueue(new GameEvent { Type = GameEventType.ChannelStarted, SourceGuid = caster, TargetGuid = _spChannelTarget, Amount = ms, SpellId = spell, Text = SpellName(spell) });
                return;
            }
            OnlineObject o;
            if (!_objects.TryGetValue(caster, out o) || o.State == null) return;
            SpDropChannelEnd(caster, 0);
            o.CastSpellId = spell;
            o.CastStart = _now;
            o.CastMs = Math.Max(0, ms);
            o.State.CastingSpellId = spell;
            o.State.CastProgress = 0f;
            o.State.Channeling = true;
            // the channel's target: its SPELL_GO just before (UNIT_FIELD_CHANNEL_OBJECT is set after this packet, Spell.cpp 5382-5383,
            // and arrives with the next values update - TickCasts takes it then)
            SpLastGo go;
            ulong ch = o.U64(UF.UNIT_FIELD_CHANNEL_OBJECT);
            o.State.CastTargetGuid = _spOtherGo.TryGetValue(caster, out go) && go.Spell == spell && _now - go.At < 1.0 && go.Target != 0 ? go.Target : ch;
            if (!_castingOthers.Contains(o)) _castingOthers.Add(o);
            _spOtherCasts++;
            _events.Enqueue(new GameEvent { Type = GameEventType.ChannelStarted, SourceGuid = caster, TargetGuid = o.State.CastTargetGuid, Amount = ms, SpellId = spell, Text = SpellName(spell) });
        }

        /// <summary>
        /// MSG_CHANNEL_UPDATE (Spell::SendChannelUpdate, Spell.cpp 5342-5359): packed caster; uint32 time left ms. 0 = the channel is over:
        /// ChannelEnded at the end of this frame - with "Interrupted" when SMSG_SPELL_FAILURE follows (Spell::cancel sends the update first, 3845-3846).
        /// </summary>
        void SpOnChannelUpdate(WowPacket p)
        {
            ulong caster = p.PackedGuid();
            int left = p.I32();
            CheckRead(p);
            if (caster == _player.Guid)
            {
                if (_spChannelId == 0) return;
                if (left > 0)
                {
                    _spChannelStart = _now - (_spChannelMs - left) / 1000.0;   // DelayedChannel: the bar jumps to the time the server says
                    return;
                }
                _spChannelEnds.Add(new SpChannelEnd { Caster = caster, Spell = _spChannelId, Target = _spChannelTarget, At = _now });
                ClearOwnCast();
                return;
            }
            OnlineObject o;
            if (!_objects.TryGetValue(caster, out o) || o.State == null || !o.State.Channeling) return;
            if (left > 0) { o.CastStart = _now - (o.CastMs - left) / 1000.0; return; }
            _spChannelEnds.Add(new SpChannelEnd { Caster = caster, Spell = o.CastSpellId, Target = o.State.CastTargetGuid, At = _now });
            EndOtherCast(o);
        }

        /// <summary>A failure right after the channel's update 0 turns its pending ChannelEnded into "Interrupted". True when it did.</summary>
        bool SpInterruptChannelEnd(ulong caster, int spell)
        {
            for (int i = 0; i < _spChannelEnds.Count; i++)
            {
                var c = _spChannelEnds[i];
                if (c.Caster != caster || c.Spell != spell) continue;
                _spChannelEnds.RemoveAt(i);
                _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = caster, TargetGuid = c.Target, SpellId = spell, Text = "Interrupted" });   // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066)
                return true;
            }
            return false;
        }

        void SpDropChannelEnd(ulong caster, int spell)
        {
            for (int i = _spChannelEnds.Count - 1; i >= 0; i--)
                if (_spChannelEnds[i].Caster == caster && (spell == 0 || _spChannelEnds[i].Spell == spell))
                {
                    var c = _spChannelEnds[i];
                    _spChannelEnds.RemoveAt(i);
                    if (spell == 0) _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = caster, TargetGuid = c.Target, SpellId = c.Spell });
                }
        }

        /// <summary>Channels that ended without a failure within SpChannelEndWait ran their course: ChannelEnded with Text null (the offline semantics).</summary>
        void SpFlushChannelEnds()
        {
            for (int i = _spChannelEnds.Count - 1; i >= 0; i--)
            {
                var c = _spChannelEnds[i];
                if (_now - c.At < SpChannelEndWait) continue;
                _spChannelEnds.RemoveAt(i);
                _events.Enqueue(new GameEvent { Type = GameEventType.ChannelEnded, SourceGuid = c.Caster, TargetGuid = c.Target, SpellId = c.Spell });
            }
        }

        /// <summary>SMSG_SPELL_DELAYED (Spell::Delayed, Spell.cpp 8104-8148): packed caster; uint32 delay ms - the cast bar moves back (pushback).</summary>
        void SpOnSpellDelayed(WowPacket p)
        {
            ulong caster = p.PackedGuid();
            int delay = p.I32();
            CheckRead(p);
            if (delay <= 0) return;
            if (caster == _player.Guid)
            {
                if (_castStartedAt < 0 || _spChannelId != 0) return;
                if (_dpState == DpReady || _dpState == DpReleased) return;   // S1: held by the realm - its bar stays full (Spell::Delayed caps m_timer at the cast time)
                _castStartedAt = Math.Min(_now, _castStartedAt + delay / 1000.0);
                _player.CastElapsed = (float)Math.Max(0.0, _now - _castStartedAt);
                return;
            }
            OnlineObject o;
            if (_objects.TryGetValue(caster, out o) && o.State != null && o.CastSpellId != 0 && !o.State.Channeling) o.CastStart = Math.Min(_now, o.CastStart + delay / 1000.0);
        }

        // ------------------------------------------------------------------ cooldowns and the GCD (client-side like the real client + the server's packets)

        float SpCooldownLeft(int id)
        {
            double end;
            if (!_spCdEnd.TryGetValue(id, out end)) return 0f;
            if (end == double.MaxValue) return float.MaxValue;
            return end > _now ? (float)(end - _now) : 0f;
        }

        void SpSetCooldown(int id, double seconds)
        {
            if (seconds <= 0) _spCdEnd.Remove(id);
            else _spCdEnd[id] = _now + seconds;
        }

        /// <summary>
        /// Our cooldown at our SPELL_GO, as the real client starts it itself (AC sends SMSG_SPELL_COOLDOWN only for modded / forced category
        /// cooldowns - Player::AddSpellAndCategoryCooldowns, Player.cpp 11089-11225): RecoveryTime for the spell (else CategoryRecoveryTime),
        /// CategoryRecoveryTime for every known spell of the same category and spell family. A COOLDOWN_ON_EVENT spell stays unusable until
        /// SMSG_COOLDOWN_EVENT (SharedDefines.h 395). Talent cooldown mods are not known here (the server refuses an early press).
        /// </summary>
        void SpStartCooldown(SpellInfo s)
        {
            if ((s.Attributes[0] & Attr0CooldownOnEvent) != 0) { _spCdEnd[s.Id] = double.MaxValue; return; }
            SpStartCooldownNow(s);
        }

        void SpStartCooldownNow(SpellInfo s)
        {
            int rec = s.RecoveryTimeMs, catrec = s.CategoryRecoveryTimeMs;
            if (rec <= 0 && catrec <= 0) { _spCdEnd.Remove(s.Id); return; }
            SpSetCooldown(s.Id, (rec > 0 ? rec : catrec) / 1000.0);
            if (s.Category <= 0 || catrec <= 0) return;
            var db = Db;
            foreach (int k in _known)
            {
                if (k == s.Id) continue;
                var o = db.Find(k);
                if (o != null && o.Category == s.Category && o.SpellFamilyName == s.SpellFamilyName) SpSetCooldown(k, catrec / 1000.0);
            }
        }

        /// <summary>
        /// The GCD as Spell::TriggerGlobalCooldown (Spell.cpp 9228-9272) computes it: StartRecoveryTime; for 1000..1500 ms the haste of
        /// UNIT_MOD_CAST_SPEED on category-133 1500 ms spells that are not melee / ranged / ranged-slot / abilities, clamped to MIN_GCD .. MAX_GCD.
        /// Started when the cast is SENT (like the real client), cancelled when the server refuses it.
        /// </summary>
        void SpStartGcd(SpellInfo s)
        {
            if (s == null || s.StartRecoveryTimeMs <= 0) return;
            int gcd = s.StartRecoveryTimeMs;
            if (gcd >= SpMinGcdMs && gcd <= SpMaxGcdMs)
            {
                if (s.StartRecoveryCategory == 133 && gcd == 1500 && s.DmgClass != DmgClassMelee && s.DmgClass != DmgClassRanged && (s.Attributes[0] & (Attr0UsesRangedSlot | Attr0IsAbility)) == 0)
                {
                    float haste = _self != null ? _self.F32(UF.UNIT_MOD_CAST_SPEED) : 1f;
                    if (haste > 0.05f && haste < 20f) gcd = (int)(gcd * haste);
                }
                gcd = Math.Max(SpMinGcdMs, Math.Min(SpMaxGcdMs, gcd));
            }
            _spGcdEnd = _now + gcd / 1000.0;
            _spGcdCategory = s.StartRecoveryCategory;
            _spGcdSpell = s.Id;
            _player.GcdTotal = gcd / 1000f;
            _player.GcdRemaining = _player.GcdTotal;
        }

        void SpCancelGcd(int spellId)
        {
            if (spellId == 0 || spellId != _spGcdSpell || _spGcdEnd <= _now) return;
            _spGcdEnd = _now;
            _player.GcdRemaining = 0f;
        }

        /// <summary>SMSG_SPELL_COOLDOWN (Unit::BuildCooldownPacket, Unit.cpp 16651-16669): uint64 guid; uint8 flags; then (uint32 spell, uint32 ms) to the end.</summary>
        void SpOnSpellCooldown(WowPacket p)
        {
            ulong guid = p.U64();
            int flags = p.U8();
            int n = 0;
            bool mine = guid == _player.Guid;
            while (p.Remaining >= 8)
            {
                int spell = (int)p.U32();
                uint ms = p.U32();
                n++;
                if (!mine) continue;
                SpSetCooldown(spell, ms / 1000.0);
                if ((flags & CooldownFlagIncludeGcd) != 0) { var s = Db.Find(spell); if (s != null) SpStartGcd(s); }
            }
            if (p.Remaining != 0) SpOdd(p, p.Remaining + " bytes after " + n + " cooldown pairs");
            else _spellPackets++;
            if (mine && n > 0 && _spLogPackets++ < 20) WowLog.Info("Online: SMSG_SPELL_COOLDOWN - " + n + " cooldown" + (n == 1 ? "" : "s") + " from the server (flags 0x" + flags.ToString("X") + ")");
        }

        /// <summary>SMSG_COOLDOWN_EVENT (Player::SendCooldownEvent, Player.cpp 11287-11297): uint32 spell; uint64 guid - an on-event cooldown starts now.</summary>
        void SpOnCooldownEvent(WowPacket p)
        {
            int spell = (int)p.U32();
            ulong guid = p.U64();
            CheckRead(p);
            if (guid != _player.Guid) return;
            var s = Db.Find(spell);
            if (s != null) SpStartCooldownNow(s);
            else _spCdEnd.Remove(spell);
            WowLog.Info("Online: cooldown of " + spell + " '" + SpellName(spell) + "' starts now (SMSG_COOLDOWN_EVENT), " + SpCooldownLeft(spell).ToString("0.0") + " s");
        }

        /// <summary>SMSG_CLEAR_COOLDOWN (Player::SendClearCooldown, Player.cpp 15088-15097): uint32 spell; uint64 guid.</summary>
        void SpOnClearCooldown(WowPacket p)
        {
            int spell = (int)p.U32();
            ulong guid = p.U64();
            CheckRead(p);
            if (guid != _player.Guid) return;
            _spCdEnd.Remove(spell);
            WowLog.Info("Online: cooldown of " + spell + " '" + SpellName(spell) + "' cleared by the server (SMSG_CLEAR_COOLDOWN)");
        }

        /// <summary>SMSG_MODIFY_COOLDOWN (Player::ModifySpellCooldown, Player.cpp 11272-11285): uint32 spell; uint64 guid; int32 delta ms.</summary>
        void SpOnModifyCooldown(WowPacket p)
        {
            int spell = (int)p.U32();
            ulong guid = p.U64();
            int delta = p.I32();
            CheckRead(p);
            double end;
            if (guid != _player.Guid || !_spCdEnd.TryGetValue(spell, out end) || end == double.MaxValue) return;
            _spCdEnd[spell] = end + delta / 1000.0;
        }

        // ------------------------------------------------------------------ the combat log (Unit.cpp / Object.cpp / Player.cpp writers)

        void SpWound(ulong guid)
        {
            if (guid == _player.Guid) { _player.WoundCounter++; return; }
            var e = Find(guid);
            if (e != null) e.WoundCounter++;
        }

        void SpDamage(GameEventType type, ulong source, ulong target, int amount, int absorb, int spell, bool crit)
        {
            if (amount > 0)
            {
                SpWound(target);
                _events.Enqueue(new GameEvent { Type = type, SourceGuid = source, TargetGuid = target, Amount = amount, SpellId = spell, Crit = crit });
                if (source != 0 && type != GameEventType.SpellHeal) CombatNoteDamage(source, target);   // the killer EntityDied / PlayerDied name (combat's hook)
            }
            if (absorb > 0) _events.Enqueue(new GameEvent { Type = GameEventType.Absorb, SourceGuid = source, TargetGuid = target, Amount = absorb, SpellId = spell, Text = "Absorb" });   // ABSORB (GlobalStrings.lua:9)
        }

        /// <summary>SpellMissInfo (SharedDefines.h 1521-1535) -> the combat text word (GlobalStrings.lua MISS 4956, RESIST 6097, DODGE 2000, PARRY 5569, BLOCK 1060, EVADE 3606, IMMUNE 4227, DEFLECT 1967, ABSORB 9, REFLECT 6038).</summary>
        static string SpMissText(int cond)
        {
            switch (cond)
            {
                case 1: return "Miss";
                case 2: return "Resist";
                case 3: return "Dodge";
                case 4: return "Parry";
                case 5: return "Block";
                case 6: return "Evade";
                case 7: case 8: return "Immune";
                case 9: return "Deflect";
                case 10: return "Absorb";
                case 11: return "Reflect";
                default: return null;
            }
        }

        void SpMiss(ulong caster, ulong target, int spell, int cond)
        {
            string text = SpMissText(cond);
            if (text == null) return;
            for (int i = 0; i < _spMisses.Length; i++)
            {
                var m = _spMisses[i];
                if (m.Spell == spell && m.Cond == cond && m.Caster == caster && m.Target == target && _now - m.At < SpMissDedup) return;
            }
            _spMisses[_spMissNext] = new SpRecentMiss { Caster = caster, Target = target, Spell = spell, Cond = cond, At = _now };
            _spMissNext = (_spMissNext + 1) % _spMisses.Length;
            var type = GameEventType.SpellMiss;
            if (spell == SpAutoShot)                            // offline reports Auto Shot as Melee* with SpellId 75 (OfflineSession.Combat StepRanged)
                type = cond == 1 ? GameEventType.MeleeMiss : cond == 3 ? GameEventType.MeleeDodge : cond == 4 ? GameEventType.MeleeParry : cond == 5 ? GameEventType.MeleeBlock : GameEventType.SpellMiss;
            _events.Enqueue(new GameEvent { Type = type, SourceGuid = caster, TargetGuid = target, SpellId = spell, Text = text });
        }

        /// <summary>
        /// SMSG_SPELLNONMELEEDAMAGELOG (Unit::SendSpellNonMeleeDamageLog, Unit.cpp 6487-6532): packed target; packed attacker; uint32 spell;
        /// uint32 damage; uint32 overkill; uint8 school; uint32 absorb; uint32 resist; uint8 physicalLog; uint8 unused; uint32 blocked;
        /// uint32 hitInfo (CRIT 0x2); uint8 debug = hitInfo &amp; (0x1|0x4|0x20) [then per AC's commented layout: CRIT_DEBUG 2 floats, HIT_DEBUG 2, ATTACK_TABLE_DEBUG 6].
        /// </summary>
        void SpOnNonMeleeDamage(WowPacket p)
        {
            ulong target = p.PackedGuid(), attacker = p.PackedGuid();
            int spell = (int)p.U32();
            int damage = p.I32();
            p.U32();                                            // overkill
            p.U8();                                             // school
            int absorb = p.I32(), resist = p.I32();
            p.U8(); p.U8();                                     // physicalLog, unused
            p.U32();                                            // blocked
            uint hitInfo = p.U32();
            int debug = p.U8();
            if ((debug & HitTypeCritDebug) != 0) { p.F32(); p.F32(); }
            if ((debug & HitTypeHitDebug) != 0) { p.F32(); p.F32(); }
            if ((debug & HitTypeAttackTableDebug) != 0) for (int i = 0; i < 6; i++) p.F32();
            CheckRead(p);
            // Auto Shot's hits come here (Spell::DoAllEffectOnTarget -> SendSpellNonMeleeDamageLog, Spell.cpp 2949); offline reports them as
            // MeleeHit with SpellId 75 (the white number; the audio's weapon sounds skip 75) - the same here
            SpDamage(spell == SpAutoShot ? GameEventType.MeleeHit : GameEventType.SpellDamage, attacker, target, Math.Max(0, damage), Math.Max(0, absorb), spell, (hitInfo & HitTypeCrit) != 0);
            if (damage <= 0 && absorb <= 0 && resist > 0) SpMiss(attacker, target, spell, 2);
        }

        /// <summary>SMSG_SPELLHEALLOG (Unit::SendHealSpellLog, Unit.cpp 8123-8138): packed target; packed caster; uint32 spell; uint32 heal; uint32 overheal; uint32 absorb; uint8 crit; uint8 unused.</summary>
        void SpOnHealLog(WowPacket p)
        {
            ulong target = p.PackedGuid(), caster = p.PackedGuid();
            int spell = (int)p.U32();
            int heal = p.I32();
            p.U32(); p.U32();                                   // overheal, absorb
            bool crit = p.U8() != 0;
            p.U8();
            CheckRead(p);
            if (heal > 0) _events.Enqueue(new GameEvent { Type = GameEventType.SpellHeal, SourceGuid = caster, TargetGuid = target, Amount = heal, SpellId = spell, Crit = crit });
        }

        /// <summary>
        /// SMSG_PERIODICAURALOG (Unit::SendPeriodicAuraLog, Unit.cpp 6588-6642): packed target; packed caster; uint32 spell; uint32 count (1);
        /// per entry uint32 auraType + PERIODIC_DAMAGE(_PERCENT) 3/89: uint32 dmg, overkill, school, absorb, resist, uint8 crit | PERIODIC_HEAL /
        /// OBS_MOD_HEALTH 8/20: uint32 heal, overheal, absorb, uint8 crit | OBS_MOD_POWER / PERIODIC_ENERGIZE 21/24: uint32 power, amount |
        /// PERIODIC_MANA_LEECH 64: uint32 power, amount, float multiplier. Another aura type is odd data (AC writes none).
        /// </summary>
        void SpOnPeriodicLog(WowPacket p)
        {
            ulong target = p.PackedGuid(), caster = p.PackedGuid();
            int spell = (int)p.U32();
            int count = p.I32();
            for (int i = 0; i < count; i++)
            {
                int aura = p.I32();
                switch (aura)
                {
                    case AuraPeriodicDamage:
                    case AuraPeriodicDamagePercent:
                        {
                            int dmg = p.I32(); p.U32(); p.U32();
                            int absorb = p.I32(); p.U32();
                            bool crit = p.U8() != 0;
                            SpDamage(GameEventType.PeriodicDamage, caster, target, Math.Max(0, dmg), Math.Max(0, absorb), spell, crit);
                            break;
                        }
                    case AuraPeriodicHeal:
                    case AuraObsModHealth:
                        {
                            int heal = p.I32(); p.U32(); p.U32();
                            bool crit = p.U8() != 0;
                            if (heal > 0) _events.Enqueue(new GameEvent { Type = GameEventType.PeriodicHeal, SourceGuid = caster, TargetGuid = target, Amount = heal, SpellId = spell, Crit = crit });
                            break;
                        }
                    case AuraObsModPower:
                    case AuraPeriodicEnergize:
                        p.U32(); p.U32();
                        break;
                    case AuraPeriodicManaLeech:
                        p.U32(); p.U32(); p.F32();
                        break;
                    default:
                        SpOdd(p, "aura type " + aura + " (AC's writer has no layout for it)");
                        return;
                }
            }
            CheckRead(p);
        }

        /// <summary>
        /// SMSG_SPELLLOGMISS (WorldObject::SendSpellMiss, Object.cpp 3830-3842): uint32 spell; uint64 caster; uint8 0; uint32 count (1);
        /// count x (uint64 target, uint8 missInfo). AC always writes the byte as 0; a 1 is taken only when the length proves 8 more bytes per target.
        /// </summary>
        void SpOnLogMiss(WowPacket p)
        {
            int spell = (int)p.U32();
            ulong caster = p.U64();
            int flag = p.U8();
            int count = p.I32();
            if (count < 0 || count > 255) { SpOdd(p, "target count " + count); return; }
            int per = 9;
            if (flag != 0)
            {
                if (p.Remaining == count * 17) per = 17;
                else if (p.Remaining != count * 9) { SpOdd(p, "flag " + flag + " with " + p.Remaining + " bytes for " + count + " targets"); return; }
            }
            for (int i = 0; i < count; i++)
            {
                ulong target = p.U64();
                int cond = p.U8();
                if (per == 17) { p.F32(); p.F32(); }
                SpMiss(caster, target, spell, cond);
            }
            CheckRead(p);
        }

        /// <summary>
        /// SMSG_SPELLDAMAGESHIELD (Unit::DealDamageShieldDamage, Unit.cpp 2184-2192): uint64 the shield's owner (dealing the damage); uint64 the
        /// attacker (taking it); uint32 spell; uint32 damage; uint32 overkill; uint32 school.
        /// </summary>
        void SpOnDamageShield(WowPacket p)
        {
            ulong owner = p.U64(), hurt = p.U64();
            int spell = (int)p.U32();
            int damage = p.I32();
            p.U32(); p.U32();
            CheckRead(p);
            SpDamage(GameEventType.SpellDamage, owner, hurt, Math.Max(0, damage), 0, spell, false);
        }

        /// <summary>
        /// SMSG_ENVIRONMENTAL_DAMAGE_LOG (Player::EnvironmentalDamage, Player.cpp 817-866 + CombatLogPackets.cpp 19-28): uint64 victim;
        /// uint8 type (Player.h 828-834: 0 exhausted, 1 drowning, 2 fall, 3 lava, 4 slime, 5 fire); uint32 amount; uint32 resisted; uint32 absorbed.
        /// </summary>
        void SpOnEnvironmental(WowPacket p)
        {
            ulong victim = p.U64();
            int type = p.U8();
            int amount = p.I32();
            p.U32();
            int absorbed = p.I32();
            CheckRead(p);
            SpDamage(GameEventType.SpellDamage, 0, victim, Math.Max(0, amount), Math.Max(0, absorbed), 0, false);
            if (victim == _player.Guid && _spLogPackets++ < 40)
                WowLog.Info("Online: environmental damage " + amount + " (" + (type == 0 ? "fatigue" : type == 1 ? "drowning" : type == 2 ? "falling" : type == 3 ? "lava" : type == 4 ? "slime" : type == 5 ? "fire" : "type " + type) + ")");
        }

        /// <summary>SMSG_PROCRESIST / SMSG_SPELLORDAMAGE_IMMUNE (Unit::SendSpellDamageResist / Immune, Unit.cpp 6644-6662): uint64 caster; uint64 target; uint32 spell; uint8 debug.</summary>
        void SpOnResistOrImmune(WowPacket p, string word)
        {
            ulong caster = p.U64(), target = p.U64();
            int spell = (int)p.U32();
            p.U8();
            CheckRead(p);
            SpMiss(caster, target, spell, word == "Resist" ? 2 : 7);
        }

        /// <summary>SMSG_SPELLENERGIZELOG (Unit::SendEnergizeSpellLog, Unit.cpp 8156-8165): packed victim; packed caster; uint32 spell; uint32 power; uint32 amount (no view event - parsed and counted).</summary>
        void SpOnEnergizeLog(WowPacket p)
        {
            p.PackedGuid(); p.PackedGuid();
            p.U32(); p.U32(); p.U32();
            CheckRead(p);
        }

        /// <summary>SMSG_CANCEL_AUTO_REPEAT (Player::SendAutoRepeatCancel, PlayerMisc.cpp 154-159): packed guid.</summary>
        void SpOnCancelAutoRepeat(WowPacket p)
        {
            ulong g = p.PackedGuid();
            CheckRead(p);
            if (g == _player.Guid) SpStopAutoRepeat(false);
        }

        /// <summary>SMSG_SPELLINSTAKILLLOG (Spell::EffectInstaKill, SpellEffects.cpp 294-298): uint64 caster; uint64 target; uint32 spell (the death itself comes with health 0).</summary>
        void SpOnInstakill(WowPacket p)
        {
            ulong caster = p.U64(), target = p.U64();
            int spell = (int)p.U32();
            CheckRead(p);
            WowLog.Info("Online: " + SpellName(spell) + " killed 0x" + target.ToString("X") + " outright (SMSG_SPELLINSTAKILLLOG, caster 0x" + caster.ToString("X") + ")");
        }

        /// <summary>SMSG_SPELLDISPELLOG (Spell::EffectDispel, SpellEffects.cpp 2802-2814): packed victim; packed caster; uint32 spell; uint8 0; uint32 n; n x (uint32 spell, uint8). The aura updates carry the removal.</summary>
        void SpOnDispelLog(WowPacket p)
        {
            p.PackedGuid(); p.PackedGuid();
            p.U32(); p.U8();
            int n = p.I32();
            if (n < 0 || n * 5 != p.Remaining) { SpOdd(p, n + " dispelled with " + p.Remaining + " bytes"); return; }
            for (int i = 0; i < n; i++) { p.U32(); p.U8(); }
            CheckRead(p);
        }

        // ------------------------------------------------------------------ auras (AuraApplication::BuildUpdatePacket)

        /// <summary>
        /// SMSG_AURA_UPDATE (one aura) / SMSG_AURA_UPDATE_ALL (every visible aura of the unit, Player.cpp 12342-12352): packed target, then per aura
        /// (AuraApplication::BuildUpdatePacket, SpellAuras.cpp 187-223) uint8 slot; uint32 spell (0 = removed); uint8 flags; uint8 casterLevel;
        /// uint8 stack/charges; [!(flags &amp; CASTER 0x08): packed caster]; [flags &amp; DURATION 0x20: uint32 max, uint32 remaining].
        /// _ALL replaces the unit's list: what it adds is AuraApplied, what it drops AuraRemoved (so the aura kits of a unit that comes into view play).
        /// </summary>
        void OnAuraUpdate(WowPacket p, bool all)
        {
            ulong target = p.PackedGuid();
            bool self = target == _player.Guid;
            OnlineObject o = null;
            AuraView[] slots = null;
            if (self) slots = _mySlots;
            else if (_objects.TryGetValue(target, out o) && o.State != null)
            {
                if (o.AuraSlots == null) o.AuraSlots = new AuraView[256];
                slots = o.AuraSlots;
            }
            if (all && slots != null)
            {
                Array.Copy(slots, _spAuraScratch, slots.Length);
                Array.Clear(slots, 0, slots.Length);
            }
            while (p.Remaining > 0)
            {
                int slot = p.U8();
                int spell = (int)p.U32();
                if (spell == 0)
                {
                    if (slots != null && slots[slot] != null)
                    {
                        var gone = slots[slot];
                        slots[slot] = null;
                        if (!all) _events.Enqueue(new GameEvent { Type = GameEventType.AuraRemoved, SourceGuid = gone.CasterGuid, TargetGuid = target, SpellId = gone.SpellId, Text = gone.Name });
                    }
                    continue;
                }
                int flags = p.U8();
                p.U8();                                         // caster level
                int stack = p.U8();
                ulong caster = (flags & AflagCaster) != 0 ? target : p.PackedGuid();
                int maxMs = 0, leftMs = 0;
                if ((flags & AflagDuration) != 0) { maxMs = p.I32(); leftMs = p.I32(); }
                if (slots == null) continue;
                var v = slots[slot];
                if (all && v == null)
                {
                    var was = _spAuraScratch[slot];
                    if (was != null && was.SpellId == spell) { v = was; slots[slot] = v; }
                }
                if (v != null && v.SpellId != spell && !all)
                    _events.Enqueue(new GameEvent { Type = GameEventType.AuraRemoved, SourceGuid = v.CasterGuid, TargetGuid = target, SpellId = v.SpellId, Text = v.Name });
                bool isNew = v == null || v.SpellId != spell;
                if (isNew) { v = new AuraView(); slots[slot] = v; }
                var info = Db.Find(spell);
                v.SpellId = spell;
                v.Name = info != null ? info.Name : "Spell " + spell;
                v.IconPath = info != null ? info.IconPath : null;
                v.Duration = maxMs > 0 ? maxMs / 1000f : 0f;
                v.Remaining = maxMs > 0 ? Math.Max(0, leftMs) / 1000f : 0f;
                v.Stacks = Math.Max(1, stack);
                v.Harmful = (flags & AflagNegative) != 0;
                v.CasterGuid = caster;
                if (!all || isNew) _events.Enqueue(new GameEvent { Type = GameEventType.AuraApplied, SourceGuid = caster, TargetGuid = target, Amount = maxMs, SpellId = spell, Text = v.Name });
            }
            if (slots == null) return;
            if (all)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    var was = _spAuraScratch[i];
                    if (was == null) continue;
                    if (slots[i] == null || slots[i] != was)
                        _events.Enqueue(new GameEvent { Type = GameEventType.AuraRemoved, SourceGuid = was.CasterGuid, TargetGuid = target, SpellId = was.SpellId, Text = was.Name });
                    _spAuraScratch[i] = null;
                }
            }
            if (self)
            {
                FillAuras(_player.Auras, slots);
                _spMyAuras.Clear();
                for (int i = 0; i < _player.Auras.Count; i++) _spMyAuras.Add(_player.Auras[i].SpellId);
                if (_spImmersiveShown) _player.Auras.Add(_spImmersiveView);   // S1 (PLAN10 s5): our own buff stays through every aura update
                UpdateMountAura();
            }
            else
            {
                FillAuras(o.State.Auras, slots);
                _auraOwners.Add(o);
            }
        }

        static void FillAuras(List<AuraView> list, AuraView[] slots)
        {
            list.Clear();
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) list.Add(slots[i]);
        }

        /// <summary>The mount aura among our auras -> MountSpellId / MountCreatureEntry / MountKind while mounted.</summary>
        void UpdateMountAura()
        {
            int found = 0;
            for (int i = 0; i < _mySlots.Length && found == 0; i++)
                if (_mySlots[i] != null && IsMountSpell(_mySlots[i].SpellId)) found = _mySlots[i].SpellId;
            if (found == _mountAuraSpell) return;
            _mountAuraSpell = found;
            if (found != 0) WowLog.Info("Online: mount aura " + found + " '" + SpellName(found) + "' on us");
            ApplyMountSpell();
        }

        void ApplyMountSpell()
        {
            if (!_player.Mounted) return;
            int spell = _mountAuraSpell;
            int entry; MountKind kind;
            MountFacts(spell, out entry, out kind);
            bool changed = spell != _player.MountSpellId;
            _player.MountSpellId = spell;
            _player.MountCreatureEntry = entry;
            _player.MountKind = kind != MountKind.None ? kind : (_player.CanFly ? MountKind.Flying : MountKind.Ground);
            if (changed && spell != 0) LearnRide();
        }

        void TickAuras(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < _player.Auras.Count; i++) { var a = _player.Auras[i]; if (a.Duration > 0f) a.Remaining = Math.Max(0f, a.Remaining - dt); }
            foreach (var o in _auraOwners)
            {
                if (o.State == null) continue;
                var list = o.State.Auras;
                for (int i = 0; i < list.Count; i++) { var a = list[i]; if (a.Duration > 0f) a.Remaining = Math.Max(0f, a.Remaining - dt); }
            }
            SpTickAim();                                        // iteration 11 (spellfx): Targeting.cs
            DynTick(dt);                                        // DynObjects.cs
            SpRefreshSelfExtras(dt);
        }

        /// <summary>Iteration 11 (spellfx): our own weapon enchants (PLAYER_VISIBLE_ITEM_n_ENCHANTMENT, PUBLIC - our own too, PlayerStorage.cpp:2968-2983),
        /// aura states (UNIT_FIELD_AURASTATE 0x003D), the death knight's runes (client-side regeneration from PLAYER_RUNE_REGEN_1..4, PRIVATE) and the
        /// temporary enchant of the main hand for the buff row (TempEnchant1, BuffFrame.xml:202-217).</summary>
        void SpRefreshSelfExtras(float dt)
        {
            if (_self == null) return;
            for (int k = 0; k < 19; k++) _player.EquipEnchants[k] = (int)_self.U32(UF.PLAYER_VISIBLE_ITEM_1_ENCHANTMENT + k * 2);
            _player.AuraStateMask = _self.U32(UF.UNIT_FIELD_AURASTATE);
            if (_player.Class == WowClass.DeathKnight)
            {
                if (_player.Runes == null)
                {
                    _player.Runes = new RuneView[RuneTracker.Count];
                    WowLog.Info("Online: death knight - six runes tracked client-side (RUNE_LIST of SMSG_SPELL_GO, SMSG_CONVERT_RUNE; regeneration from PLAYER_RUNE_REGEN, " + RuneTracker.BaseCooldown + " s base)");
                }
                for (int t = 0; t < 4; t++) _runeRegen[t] = _self.F32(UF.PLAYER_RUNE_REGEN_1 + t);
                _runes.Tick(dt, _runeRegen);
                _runes.Fill(_player.Runes);
            }
            for (int i = 0; i < 3; i++) if (_spTempEnchantLeft[i] > 0f) _spTempEnchantLeft[i] = Math.Max(0f, _spTempEnchantLeft[i] - dt);
            int tmp = (_player.EquipEnchants[15] >> 16) & 0xFFFF;
            if (tmp != 0)
            {
                _player.WeaponImbueSpellId = ItemVisualDb.Get(_files).EnchantSpell(tmp);
                _player.WeaponImbueRemaining = _spTempEnchantLeft[0];
            }
            else if (_spImbueShown)
            {
                _player.WeaponImbueSpellId = 0;
                _player.WeaponImbueRemaining = 0f;
            }
            _spImbueShown = tmp != 0;
        }
        bool _spImbueShown;

        // ------------------------------------------------------------------ immersive (held) casting online

        /// <summary>A held spell ends without being sent (dead, time-out, target gone / out of range): CastFailed as offline DropHeld.</summary>
        void SpDropHeld(string reason)
        {
            if (_spHeldId == 0) return;
            int id = _spHeldId; ulong t = _spHeldTarget;
            _spHeldId = 0;
            _spHeldTarget = 0;
            _player.HeldSpellId = 0;
            _player.HeldTargetGuid = 0;
            WowLog.Info("Online: held " + id + " '" + SpellName(id) + "' dropped: " + reason);
            _events.Enqueue(new GameEvent { Type = GameEventType.CastFailed, SourceGuid = _player.Guid, TargetGuid = t, SpellId = id, Text = reason });
        }

        void SpTickHeld()
        {
            if (_spHeldId == 0) return;
            if (_player.Dead) { SpDropHeld("You are dead"); return; }                     // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
            if (_now - _spHeldAt > SpHeldTimeout) { SpDropHeld("Interrupted"); return; }  // SPELL_FAILED_INTERRUPTED (GlobalStrings.lua:7066)
            if (_spHeldTarget == 0 || _spHeldTarget == _player.Guid) return;
            var t = Find(_spHeldTarget);
            var s = Db.Find(_spHeldId);
            // rescorpse: a resurrection held at a corpse / a player lying dead keeps it (only a target that came back to life ends it)
            bool corpseSpell = s != null && (SpExplicitMask(s) & (TfCorpseAlly | TfCorpseEnemy)) != 0;
            if (t == null || (corpseSpell ? !t.Dead : t.Dead)) { SpDropHeld("Invalid target"); return; }   // SPELL_FAILED_BAD_TARGETS (GlobalStrings.lua:6949)
            if (s == null) return;
            float max = (t.Reaction == Reaction.Friendly && s.RangeMaxFriend > 0f ? s.RangeMaxFriend : s.RangeMax) + SpHeldRangeSlack + SpReach(_self) + SpReach(t.Guid);
            float dx = t.Pos.X - _player.Pos.X, dy = t.Pos.Y - _player.Pos.Y, dz = t.Pos.Z - _player.Pos.Z;
            if (s.RangeMax > 0f && dx * dx + dy * dy + dz * dz > max * max) { SpDropHeld("Out of range"); Error("Out of range"); }   // SPELL_FAILED_OUT_OF_RANGE (GlobalStrings.lua:7147)
        }

        // ------------------------------------------------------------------ the realm's mount models (MountDisplays, option A and B alike)

        /// <summary>Every Mounts entry MountDisplays does not know as Learned gets one CMSG_CREATURE_QUERY (at most 4 a second).</summary>
        void QueueMountQueries()
        {
            int added = 0;
            for (int i = 0; i < _player.Mounts.Count; i++)
            {
                int entry; MountKind kind;
                MountFacts(_player.Mounts[i].SpellId, out entry, out kind);
                if (entry <= 0 || _mountAsked.Contains(entry)) continue;
                int[] ids; MountDisplaySource src;
                if (MountDisplays.TryGetAll(entry, out ids, out src) && src == MountDisplaySource.Learned) continue;
                _mountAsked.Add(entry);
                _mountQueryQueue.Enqueue(entry);
                added++;
            }
            if (added > 0) WowLog.Info("Online: asking the server for the models of " + added + " mount creature entr" + (added == 1 ? "y" : "ies") + " (CMSG_CREATURE_QUERY, 4 a second)");
        }

        void TickMountQueries()
        {
            if (_mountQueryQueue.Count == 0 || _now < _nextMountQueryAt || _world == null) return;
            int entry = _mountQueryQueue.Dequeue();
            _world.Send(Op.CMSG_CREATURE_QUERY, new PacketWriter().U32((uint)entry).U64(0));   // uint32 entry, ObjectGuid (AC answers from creature_template only)
            _queriesSent++;
            _mountQueriesSent++;
            _nextMountQueryAt = _now + MountQuerySpacing;
        }

        bool IsMountEntry(int entry)
        {
            if (_mountAsked.Contains(entry)) return true;
            if (_catalogEntries == null && MountCatalog.Loaded)
            {
                _catalogEntries = new HashSet<int>();
                foreach (var m in MountCatalog.All) if (m.CreatureEntry > 0) _catalogEntries.Add(m.CreatureEntry);
            }
            return _catalogEntries != null && _catalogEntries.Contains(entry);
        }

        /// <summary>Modelid1..4 of a SMSG_CREATURE_QUERY_RESPONSE (read by OnCreatureQueryResponse) for a mount entry -> MountDisplays.Learn.</summary>
        void LearnCreatureModels(int entry, int[] models)
        {
            if (!IsMountEntry(entry)) return;
            var ids = new List<int>();
            for (int i = 0; i < models.Length; i++) if (models[i] > 0 && !ids.Contains(models[i])) ids.Add(models[i]);
            if (ids.Count == 0) { WowLog.Info("Online: creature " + entry + " has no model on the server (vehicle / scripted mount?)"); return; }
            MountDisplays.Learn(entry, ids.ToArray());
            WowLog.Info("Online: creature " + entry + " -> displays " + string.Join(",", ids.ConvertAll(x => x.ToString()).ToArray()) + " (learned from the server)");
        }

        /// <summary>"Entry E is ridden with UNIT_FIELD_MOUNTDISPLAYID D": D joins the learned list of E (never replaces the server's full list).</summary>
        void LearnRide()
        {
            int entry = _player.MountCreatureEntry, display = _player.MountDisplayId;
            if (!_player.Mounted || entry <= 0 || display <= 0) return;
            int[] ids; MountDisplaySource src;
            bool have = MountDisplays.TryGetAll(entry, out ids, out src) && src == MountDisplaySource.Learned && ids != null;
            if (have && Array.IndexOf(ids, display) >= 0) return;
            var list = new List<int>();
            if (have) list.AddRange(ids);
            list.Add(display);
            MountDisplays.Learn(entry, list.ToArray());
            WowLog.Info("Online: creature " + entry + " ridden with display " + display + " (learned from the server)");
        }

        // ------------------------------------------------------------------ IWowSession (stage O2, PLAN9 s3: signatures frozen)

        /// <summary>Mount spells as iteration 7 (a mount never needs a target); every other spell of the spellbook goes to the server (SpCast).</summary>
        public void CastSpell(int spellId, ulong targetGuid)
        {
            _spReleaseId = 0;                                   // a queued push is replaced by the new press
            if (_spHeldId != 0) SpDropHeld("Interrupted");      // pressing another spell ends a held one (offline: a new cast replaces it)
            if (PfIsTradeSkillSpell(spellId)) { OpenTradeSkill(PfTradeSkillOf(spellId)); return; }   // iteration 11 (trainer S2): a profession spell opens its window (EffectTradeSkill does nothing server-side, SpellEffects.cpp:2976-2990)
            if (IsMountSpell(spellId)) { CastMount(spellId); return; }
            SpCast(spellId, targetGuid);
        }

        /// <summary>Offline order (OfflineSession.CancelCast): the cast / channel, else the held spell, else a queued next-swing ability.</summary>
        public void CancelCast()
        {
            PfStopQueue("cancelled");                           // iteration 11 (trainer S4): Create N stops
            if (_spReleaseId != 0) { WowLog.Info("Online: queued push of " + _spReleaseId + " cancelled"); _spReleaseId = 0; return; }
            if (_castId != 0 || _player.CastingSpellId != 0) { CancelOwnCast(); return; }
            if (_spHeldId != 0) { SpDropHeld("Interrupted"); return; }
            if (_spNextSwing != 0 && _world != null)
            {
                _world.Send(Op.CMSG_CANCEL_CAST, new PacketWriter().U8(++_cancelCounter).U32((uint)_spNextSwing));   // HandleCancelCastOpcode: InterruptSpell(CURRENT_MELEE_SPELL)
                WowLog.Info("Online: next-swing " + _spNextSwing + " dequeued (CMSG_CANCEL_CAST)");
                _spNextSwing = 0;
            }
        }

        /// <summary>
        /// Immersive casting online (only VrImmersive calls this: a VR player with Immersive casting on). Stage S1 (the user: "start the
        /// spell THEN I can release ... Instant cast spells can just trigger normally"): instants and channels are cast normally; a spell
        /// with a cast time, on a realm with the Dreamport hold (_dpMode 1, DPHOLD HI), is SENT now with a hold request - the cast bar runs,
        /// at its end the realm holds it (DPHOLD READY = Player.HeldSpellId + CastHeld) until ReleaseHeldSpell (the push, DPHOLD GO).
        /// On a realm without it (or before its answer) the stage-O2 way: held HERE BEFORE the cast - checked like CastSpell, then
        /// Player.HeldSpellId + CastHeld at once; ReleaseHeldSpell sends it and the cast bar then runs on the server.
        /// Mounts, Attack, auto-repeat, next-swing abilities and form toggles behave as CastSpell (offline rule).
        /// </summary>
        public void CastSpellHeld(int spellId, ulong targetGuid)
        {
            if (_spHeldId != 0) SpDropHeld("Interrupted");
            if (PfIsTradeSkillSpell(spellId)) { OpenTradeSkill(PfTradeSkillOf(spellId)); return; }   // iteration 11 (trainer S2b): a profession spell on a VR ring opens its window
            var s = IsMountSpell(spellId) ? null : Db.Find(spellId);
            if (s == null || s.IsPassive || s.AutoRepeat || s.OnNextSwing || spellId == SpAttackSpell || (s.Effects[0] != null && s.Effects[0].Effect == SpellEffect.Attack)
                || s.HasAura(SpellAura.ModShapeshift) || (s.HasAura(SpellAura.ModStealth) && _spMyAuras.Contains(spellId)))
            {
                CastSpell(spellId, targetGuid);
                return;
            }
            if (SpNeedsAim(s, targetGuid, true)) return;       // iteration 11 (spellfx): the circle / the item cursor first; the confirm calls this again
            if (s.CastTimeMs <= 0 || s.IsChanneled)
            {
                if (_dpInstantLogged++ < 5) WowLog.Info("Online: " + s.Name + " is " + (s.IsChanneled ? "a channel" : "instant") + " - cast at once (immersive casting holds only spells with a cast time; logged 5 times)");
                _dpBonusAsk = spellId;                          // S1 (PLAN10 s5): cast through Immersive casting - SpCast sends DPHOLD BONUS before it
                try { CastSpell(spellId, targetGuid); }
                finally { _dpBonusAsk = 0; }
                return;
            }
            if (_dpMode == 1)
            {
                _dpAsk = spellId;                               // SpCast sends DPHOLD HOLD right before its CMSG_CAST_SPELL
                CastSpell(spellId, targetGuid);
                _dpAsk = 0;                                     // refused here (range, cooldown, moving ...): nothing was asked
                return;
            }
            if (!_known.Contains(spellId)) { FailLocal(spellId, "Spell not learned", "not in the server's spellbook"); return; }   // SPELL_FAILED_NOT_KNOWN (GlobalStrings.lua:7103)
            if (_player.Dead) { FailLocal(spellId, "You are dead", "dead"); return; }                                               // SPELL_FAILED_CASTER_DEAD (GlobalStrings.lua:6962)
            float hcd = SpCooldownLeft(spellId);
            if (hcd != float.MaxValue && hcd > SpReleaseWaitMax) { FailLocal(spellId, "Spell is not ready yet", "cooldown " + hcd.ToString("0.0") + " s (held)"); return; }   // ERR_SPELL_COOLDOWN (GlobalStrings.lua:3493)
            uint mask; ulong obj; WowPos dest;
            string why = SpTargets(s, targetGuid, out mask, out obj, out dest);
            if (why != null) { FailLocal(spellId, why, "targets (held)"); return; }
            _spHeldId = spellId;
            // iteration 11 (spellfx): a confirmed place / item stays with the spell held here until the push (SpCastPushed restores it)
            _spHeldAimFor = _spConfirmedFor == spellId ? spellId : 0; _spHeldHasDest = _spConfirmedHasDest; _spHeldDest = _spConfirmedDest; _spHeldItem = _spConfirmedItem;
            _spHeldTarget = obj != 0 ? obj : (targetGuid != 0 ? targetGuid : _player.Guid);
            _spHeldAt = _now;
            _player.HeldSpellId = spellId;
            _player.HeldTargetGuid = _spHeldTarget;
            WowLog.Info("Online: " + s.Name + " held in the hand (immersive) - sent to the server at the push");
            _events.Enqueue(new GameEvent { Type = GameEventType.CastHeld, SourceGuid = _player.Guid, TargetGuid = _spHeldTarget, SpellId = spellId, Text = s.Name });
        }

        /// <summary>The push: the held spell goes to the server at targetGuid when that is a valid target for it (else at the target it was held for).</summary>
        public void ReleaseHeldSpell(ulong targetGuid)
        {
            if (_dpState >= DpAsked && _dpState <= DpReady) { DpRelease(targetGuid); return; }   // S1: the realm holds it
            if (_spHeldId == 0) return;
            int id = _spHeldId;
            ulong t = _spHeldTarget;
            if (targetGuid != 0 && targetGuid != t)
            {
                var s = Db.Find(id);
                var alt = targetGuid == _player.Guid ? null : Find(targetGuid);
                bool harmful = s != null && (SpExplicitMask(s) & TfUnitEnemy) != 0;
                bool corpseSpell = s != null && (SpExplicitMask(s) & (TfCorpseAlly | TfCorpseEnemy)) != 0;   // rescorpse: pushed at another corpse / player lying dead
                if (targetGuid == _player.Guid ? !harmful && !corpseSpell
                    : (alt != null && (corpseSpell ? alt.Dead && alt.Kind == EntityKind.Player : !alt.Dead) && (harmful ? alt.Reaction != Reaction.Friendly : alt.Reaction == Reaction.Friendly))) t = targetGuid;
            }
            _spHeldId = 0;
            _spHeldTarget = 0;
            _player.HeldSpellId = 0;
            _player.HeldTargetGuid = 0;
            var sp = Db.Find(id);
            float wait = sp != null ? SpTimerLeft(sp) : 0f;
            if (wait > SpQueueWindow && wait <= SpReleaseWaitMax)
            {
                // a push during the GCD / a short cooldown: sent when the server would queue it (ours: waits at most SpReleaseWaitMax)
                _spReleaseId = id;
                _spReleaseTarget = t;
                _spReleaseAt = _now + wait - SpQueueWindow;
                WowLog.Info("Online: held " + id + " '" + SpellName(id) + "' pushed during the global cooldown - sent in " + (wait - SpQueueWindow).ToString("0.00") + " s");
                return;
            }
            SpCastPushed(id, t);
        }

        /// <summary>S1 (PLAN10 s5): a spell held HERE is sent at the push - released with Immersive casting (DPHOLD BONUS when the realm has the module).</summary>
        void SpCastPushed(int id, ulong t)
        {
            _dpBonusAsk = id;
            bool aimed = _spHeldAimFor == id;                   // iteration 11 (spellfx): the place / item confirmed before the spell was held
            if (aimed) { _spConfirmedFor = id; _spConfirmedHasDest = _spHeldHasDest; _spConfirmedDest = _spHeldDest; _spConfirmedItem = _spHeldItem; }
            try { SpCast(id, t); }
            finally
            {
                _dpBonusAsk = 0;
                if (aimed) { _spConfirmedFor = 0; _spConfirmedHasDest = false; _spConfirmedItem = 0; }
                _spHeldAimFor = 0;
            }
        }

        /// <summary>Seconds until the spell could be sent without a "not ready" refusal: its finite cooldown or the GCD of its category.</summary>
        float SpTimerLeft(SpellInfo s)
        {
            float cd = SpCooldownLeft(s.Id);
            if (cd == float.MaxValue) cd = 0f;
            float gcd = s.StartRecoveryTimeMs > 0 && s.StartRecoveryCategory == _spGcdCategory && _spGcdEnd > _now ? (float)(_spGcdEnd - _now) : 0f;
            return Math.Max(cd, gcd);
        }

        void SpTickRelease()
        {
            if (_spReleaseId == 0 || _now < _spReleaseAt) return;
            int id = _spReleaseId;
            ulong t = _spReleaseTarget;
            _spReleaseId = 0;
            _spReleaseTarget = 0;
            SpCastPushed(id, t);                                // S1: the push that waited for the GCD
        }

        // ------------------------------------------------------------------ S1: the realm's Dreamport hold (DPHOLD, ServerKit dreamport-hold)

        /// <summary>
        /// CMSG_MESSAGECHAT (HandleMessagechatOpcode, ChatHandler.cpp): uint32 type CHAT_MSG_WHISPER; uint32 lang LANG_ADDON; CString to (our own
        /// name - normalizePlayerName + FindPlayerByName find us); CString message ("DPHOLD\t..."). LANG_ADDON has no skill (ObjectMgr.cpp 233)
        /// and is valid for whispers when AddonChannel = 1 (the default); a whisper to oneself skips the level and trial checks (`receiver != sender`).
        /// The module's OnPlayerCanUseChat (Player::Whisper, Player.cpp 9675-9690) returns false: nothing is sent to anyone; a stock realm
        /// delivers it back to us only (an addon message: the real client never shows those).
        /// </summary>
        void DpSend(string text)
        {
            if (_world == null || string.IsNullOrEmpty(_player.Name)) return;
            _world.Send(Op.CMSG_MESSAGECHAT, new PacketWriter().U32(DpChatWhisper).U32(DpLangAddon).CString(_player.Name).CString(DpPrefix + text));
        }

        /// <summary>Per tick: the one DPHOLD HELLO after entering the world, and "no answer" = no Dreamport hold on this realm.</summary>
        void DpTick()
        {
            if (_world == null) return;
            if (!_dpHelloSent)
            {
                if (string.IsNullOrEmpty(_player.Name) || _player.Guid == 0) return;
                _dpHelloSent = true;
                _dpHelloAt = _now;
                DpSend("HELLO\t" + DpProtocol);
                WowLog.Info("Online: asking the realm once whether it holds immersive casts (DPHOLD HELLO, an addon message to ourselves - nobody sees it, nothing changes)");
                return;
            }
            if (_dpMode == 0 && _now - _dpHelloAt > DpHelloTimeout)
            {
                _dpMode = -1;
                WowLog.Info("Online: this realm has no Dreamport hold (no answer to DPHOLD HELLO within " + DpHelloTimeout + " s; ServerKit setup-hold.sh installs it) - immersive casts are held here before they are sent (stage O2)");
            }
        }

        /// <summary>The realm's hold of our cast is over (ClearOwnCast: GO, failure, cancel, no answer): the held look goes with it.</summary>
        void DpClear()
        {
            if (_dpState == 0) return;
            if (_spHeldId == 0 && _player.HeldSpellId == _dpSpell) { _player.HeldSpellId = 0; _player.HeldTargetGuid = 0; }
            _dpState = 0;
            _dpSpell = 0;
        }

        /// <summary>
        /// The push with the realm holding the cast: DPHOLD GO. The spell goes to the target it was started on (a WoW cast in progress keeps
        /// its target); the held look and our bar end now, the SPELL_GO follows (TickCasts waits CastAnswerTimeout for it).
        /// </summary>
        void DpRelease(ulong targetGuid)
        {
            int id = _dpSpell;
            DpSend("GO\t" + id + "\t" + _dpCount);
            if (_dpState != DpReady)
            {
                // before its end (not held yet): the realm simply does not hold it - it goes off when its cast bar is over
                WowLog.Info("Online: " + SpellName(id) + " pushed before its cast was over - not held, it goes off at the end (DPHOLD GO)");
                _dpState = 0;
                return;
            }
            _dpState = DpReleased;
            _dpReleasedAt = _now;
            if (_spHeldId == 0) { _player.HeldSpellId = 0; _player.HeldTargetGuid = 0; }
            ulong castAt = _player.CastTargetGuid;
            _player.CastingSpellId = 0;                         // the bar is over (the Gear record then sends the release, not a new cast)
            _player.CastElapsed = _player.CastTotal = 0f;
            if (_dpHolds <= SpLogCasts || _dpHolds % 50 == 0)
                WowLog.Info("Online: " + SpellName(id) + " pushed after " + (_now - _dpReadyAt).ToString("0.0") + " s held - released on the realm (DPHOLD GO, castCount " + _dpCount + ")" +
                            (targetGuid != 0 && targetGuid != castAt ? "; it goes to the target it was started on, not the one selected now" : ""));
        }

        /// <summary>
        /// SMSG_MESSAGECHAT / SMSG_GM_MESSAGECHAT (ChatHandler::BuildChatPacket, Chat.cpp 273-349): uint8 type; int32 lang; uint64 sender; uint32 0;
        /// [GM message: uint32 len + 1; sender name]; [CHAT_MSG_CHANNEL: channel]; uint64 receiver; uint32 len + 1; message; uint8 tag. Only
        /// LANG_ADDON whispers from ourselves with the DPHOLD prefix are read here (the chat owner never shows addon messages).
        /// </summary>
        void DpOnChat(WowPacket p, bool gm)
        {
            if (p.Remaining < 17) return;
            int type = p.U8();
            uint lang = p.U32();
            if (type != DpChatWhisper || lang != DpLangAddon) return;
            ulong sender = p.U64();
            p.U32();
            if (gm) { p.U32(); p.CString(); }
            if (p.Remaining < 12) return;
            p.U64();                                            // receiver
            p.U32();                                            // length + 1
            string text = p.CString();
            if (sender != _player.Guid || text == null || !text.StartsWith(DpPrefix, StringComparison.Ordinal)) return;
            DpHandle(text.Substring(DpPrefix.Length).Split('\t'));
        }

        void DpHandle(string[] f)
        {
            string verb = f[0];
            int spell = f.Length > 1 ? DpNumber(f[1]) : 0;
            int count = f.Length > 2 ? DpNumber(f[2]) : -1;
            switch (verb)
            {
                case "HI":
                    // S1 (PLAN10 s5): a module with the damage bonus adds its percent as a 4th field (an older one sends 3 fields)
                    _dpBonusPct = f.Length > 3 ? DpNumber(f[3]) : -1;
                    if (_dpMode != 1)
                        WowLog.Info("Online: this realm holds immersive casts (Dreamport hold module, protocol " + spell + ", time-out " + (count > 0 ? (count / 1000).ToString() : "?") +
                                    " s) - a VR immersive cast starts at once and waits at its end for the push; spells released with Immersive casting deal " +
                                    (_dpBonusPct >= 0 ? "+" + _dpBonusPct + "% damage (the module's house rule)" : "normal damage (this module has no damage bonus yet)"));
                    _dpMode = 1;
                    return;
                case "HELLO": case "HOLD": case "GO": case "BONUS":
                    // our own message came back: a stock realm delivered the whisper (Player::Whisper -> target->SendDirectMessage)
                    if (_dpMode != -1)
                        WowLog.Info("Online: this realm has no Dreamport hold (it echoed DPHOLD " + verb + " back; ServerKit setup-hold.sh installs it) - immersive casts are held here before they are sent (stage O2)");
                    _dpMode = -1;
                    if (verb == "HOLD" && (_dpState == DpAsked || _dpState == DpArmed)) _dpState = 0;   // that cast is a normal one now
                    return;
            }
            bool ours = _dpState != 0 && count == _dpCount;
            switch (verb)
            {
                case "ARMED":
                    if (ours && _dpState == DpAsked)
                    {
                        _dpState = DpArmed;
                        if (_dpHolds <= SpLogCasts) WowLog.Info("Online: " + SpellName(_dpSpell) + " armed on the realm (DPHOLD ARMED, " + (f.Length > 3 ? f[3] : "?") + " ms cast) - it will wait at its end");
                    }
                    return;
                case "READY":
                    if (ours && (_dpState == DpAsked || _dpState == DpArmed) && _castId == _dpSpell)
                    {
                        _dpState = DpReady;
                        _dpReadyAt = _now;
                        _player.HeldSpellId = _dpSpell;
                        _player.HeldTargetGuid = _player.CastTargetGuid;
                        if (_player.CastTotal > 0f) _player.CastElapsed = _player.CastTotal;
                        if (_dpHolds <= SpLogCasts || _dpHolds % 50 == 0)
                            WowLog.Info("Online: " + SpellName(_dpSpell) + " held by the realm at the end of its cast (DPHOLD READY after " + (_now - _dpStartedAt).ToString("0.0") + " s) - push to release it");
                        _events.Enqueue(new GameEvent { Type = GameEventType.CastHeld, SourceGuid = _player.Guid, TargetGuid = _player.HeldTargetGuid, SpellId = _dpSpell, Text = SpellName(_dpSpell) });
                        return;
                    }
                    // a hold we do not show (cancelled here, or never asked): released at once - the realm never keeps a cast we cannot see
                    WowLog.Info("Online: DPHOLD READY for " + spell + " (castCount " + count + ") that is not held here - released at once (DPHOLD GO)");
                    DpSend("GO\t" + spell + "\t" + count);
                    return;
                case "EXPIRED":
                    WowLog.Info("Online: " + SpellName(spell) + " held " + DpServerHoldTimeout + " s without a push - the realm cancelled it (DPHOLD EXPIRED; its SPELL_FAILURE follows)");
                    return;
                case "NOHOLD":
                    if (ours && _dpState <= DpArmed) _dpState = 0;  // a normal cast now (instant on the realm: a proc or a talent)
                    WowLog.Info("Online: the realm does not hold " + SpellName(spell) + " (DPHOLD NOHOLD: " + (f.Length > 3 ? f[3] : "?") + ") - it goes off as a normal cast");
                    return;
            }
            if (!_dpOddLogged) { _dpOddLogged = true; WowLog.Warn("Online: unknown DPHOLD message '" + verb + "' (a newer module?) - ignored (logged once)"); }
        }

        static int DpNumber(string s)
        {
            int v;
            return int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : -1;
        }
    }
}
