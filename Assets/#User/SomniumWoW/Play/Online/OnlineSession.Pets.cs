// iteration 11 - owner pets (proposal-pets.md s6.1): the pet / charm / possess bar (SMSG_PET_SPELLS), pet commands, autocast, rename /
// abandon / dismiss, the pet name query, happiness / XP, pet cooldowns, feedback texts, tame failures, totems (SMSG_TOTEM_CREATED /
// CMSG_TOTEM_DESTROYED), companions, the stable and the spell-click routing target. Every layout is AzerothCore master's
// (P/acsrc11/pets/<file>:<line>). Vehicles (seats, driving, aimed casts) are OnlineSession.Vehicle.cs (owner vehicle).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Pets;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        /// <summary>Pet guid -> its chosen name (Objects.cs Refresh shows it instead of the creature's), from SMSG_PET_NAME_QUERY_RESPONSE.</summary>
        readonly Dictionary<ulong, string> _ptNames = new Dictionary<ulong, string>();

        /// <summary>pets H2(d): true once PetsRefreshSelf has set Player.PetGuid from our UNIT_FIELD_SUMMON - Objects.cs then no longer sets it
        /// from the pet's own object (the iteration-3 rule).</summary>
        bool PetsOwnPetGuid { get { return _ptOwnGuid; } }

        const int PetDismissSpell = 2641;               // Dismiss Pet (hunter, 5 s cast) - proposal-pets.md s4.7
        const uint NpcFlagSpellClick = 0x01000000, NpcFlagStable = 0x00400000;   // UnitDefines.h:346 / NPCHandler CheckStableMaster
        const float PtStableReach = 10f;                // ours: the stable window closes beyond this distance from the stable master
        const double PtInfoMinGap = 3.0;                // ours: CMSG_REQUEST_PET_INFO at most every 3 s
        const float PtCooldownSlack = 0.4f;             // ours: a press with less than this left is sent (the server judges)

        bool _ptOwnGuid, _ptInfoSentOnce;
        ulong _ptSummon, _ptCharm, _ptCritter, _ptStableNpc;
        double _ptInfoAskAt = -1, _ptInfoSentAt = -100, _ptNameScanAt;
        readonly HashSet<long> _ptNameAsked = new HashSet<long>();
        readonly Dictionary<int, ulong> _ptNumberGuid = new Dictionary<int, ulong>();
        readonly Dictionary<int, double> _ptCdEnd = new Dictionary<int, double>();
        readonly Dictionary<int, float> _ptCdTotal = new Dictionary<int, float>();
        readonly double[] _ptTotemBorn = new double[4];
        readonly HashSet<string> _ptLogged = new HashSet<string>();

        void PtInfo(string text) { _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = text }); }

        void PtLogOnce(string key, string line) { if (_ptLogged.Add(key)) WowLog.Info(line); }

        partial void InitPets()
        {
            if (_world == null) return;
            _world.On(Op.SMSG_PET_SPELLS, PtOnSpells, this);
            _world.On(Op.SMSG_PET_CAST_FAILED, PtOnCastFailed, this);
            _world.On(Op.SMSG_PET_ACTION_FEEDBACK, PtOnFeedback, this);
            _world.On(Op.SMSG_PET_NAME_QUERY_RESPONSE, PtOnNameResponse, this);
            _world.On(Op.SMSG_PET_NAME_INVALID, PtOnNameInvalid, this);
            _world.On(Op.SMSG_PET_TAME_FAILURE, PtOnTameFailure, this);
            _world.On(Op.SMSG_PET_LEARNED_SPELL, p => PtOnLearned(p, true), this);
            _world.On(Op.SMSG_PET_UNLEARNED_SPELL, p => PtOnLearned(p, false), this);
            _world.On(Op.SMSG_PET_ACTION_SOUND, p => PtLogOnce("sound", "Online: SMSG_PET_ACTION_SOUND / DISMISS_SOUND are read and not played (no CreatureSoundData player - proposal-pets.md gap 9)"), this);
            _world.On(Op.SMSG_PET_DISMISS_SOUND, p => PtLogOnce("sound", "Online: SMSG_PET_ACTION_SOUND / DISMISS_SOUND are read and not played (no CreatureSoundData player - proposal-pets.md gap 9)"), this);
            _world.On(Op.SMSG_PET_UPDATE_COMBO_POINTS, p => PtLogOnce("combo", "Online: SMSG_PET_UPDATE_COMBO_POINTS (a controlled unit's combo points) - not shown (ours)"), this);
            _world.On(Op.SMSG_TOTEM_CREATED, PtOnTotemCreated, this);
            _world.On(Op.MSG_LIST_STABLED_PETS, PtOnStableList, this);
            _world.On(Op.SMSG_STABLE_RESULT, PtOnStableResult, this);
            // second handlers (WorldClient.On allows several per opcode): the controlled unit's own cooldowns (OnlineSession.Spells.cs keeps ours)
            _world.On(Op.SMSG_SPELL_COOLDOWN, PtOnSpellCooldown, this);
            _world.On(Op.SMSG_COOLDOWN_EVENT, p => PtOnCooldownChange(p, 0), this);
            _world.On(Op.SMSG_CLEAR_COOLDOWN, p => PtOnCooldownChange(p, 1), this);
            _world.On(Op.SMSG_MODIFY_COOLDOWN, p => PtOnCooldownChange(p, 2), this);
            _world.On(Op.SMSG_NEW_WORLD, p => { _ptInfoAskAt = _now + 2.0; }, this);   // ours: ask for the bar 2 s after a far teleport
            RegisterNpcService(NpcFlagSpellClick, PetsSpellClick, "spell click (vehicle / quest)", true);
            RegisterNpcService(NpcFlagStable, PetsStableHello, "stable master");
            WowLog.Info("Online pets: ours - CMSG_REQUEST_PET_INFO after login / a far teleport (+2 s) and at most every " + PtInfoMinGap + " s; the stable closes beyond " +
                        PtStableReach + " yd; a pet spell with less than " + PtCooldownSlack + " s of cooldown left is sent");
        }

        // ------------------------------------------------------------------ self fields (Objects.cs RefreshSelf, hook H2e)

        partial void PetsRefreshSelf(OnlineObject o)
        {
            ulong summon = o.U64(UF.UNIT_FIELD_SUMMON), charm = o.U64(UF.UNIT_FIELD_CHARM), critter = o.U64(UF.UNIT_FIELD_CRITTER);
            if (summon != _ptSummon) WowLog.Info("Online: our pet (UNIT_FIELD_SUMMON) " + (summon != 0 ? "is 0x" + summon.ToString("X") : "is gone"));
            if (charm != _ptCharm) WowLog.Info("Online: our charm / controlled unit (UNIT_FIELD_CHARM) " + (charm != 0 ? "is 0x" + charm.ToString("X") : "ended"));
            _ptSummon = summon;
            _ptCharm = charm;
            if (critter != _ptCritter) { _ptCritter = critter; _player.CompanionSpellId = 0; }
            _player.PetGuid = summon;
            _player.CompanionGuid = critter;
            _ptOwnGuid = true;
            if (!_ptInfoSentOnce) { _ptInfoSentOnce = true; _ptInfoAskAt = _now; }
            if (summon == 0) _player.PetInfo = null;
        }

        partial void PetsEnterPlayerVehicle(ulong guid)
        {
            if (_world == null || guid == 0) return;
            _world.Send(Op.CMSG_PLAYER_VEHICLE_ENTER, new PacketWriter().U64(guid));      // VehicleHandler.cpp:143-161 (raid members only)
            WowLog.Info("Online: CMSG_PLAYER_VEHICLE_ENTER 0x" + guid.ToString("X"));
        }

        void PtRequestInfo(string why)
        {
            if (_world == null || _now - _ptInfoSentAt < PtInfoMinGap) return;
            _ptInfoSentAt = _now;
            _world.Send(Op.CMSG_REQUEST_PET_INFO);                                           // MiscHandler.cpp:1560-1580 (empty)
            WowLog.Info("Online: CMSG_REQUEST_PET_INFO (" + why + ")");
        }

        // ------------------------------------------------------------------ SMSG_PET_SPELLS (s3.1 / s3.8)

        void PtOnSpells(WowPacket p)
        {
            ControlBarPacket b = ControlBarBuilder.Read(p);
            CheckRead(p);
            if (b.Guid == 0)
            {
                bool had = _player.ControlBar != null;
                _player.ControlBar = null;
                if (had) WowLog.Info("Online: pet bar removed");
                if (_player.PetGuid != 0) { _ptInfoSentAt = -100; PtRequestInfo("a charm / possession / vehicle ended while our pet lives"); }
                return;
            }
            ulong controlled = _vehCtl != 0 ? _vehCtl : _vehMover;                  // the vehicle owner's last SMSG_CLIENT_CONTROL_UPDATE(unit != us, allow 1)
            ControlBarKind kind = ControlBarBuilder.Classify(b, controlled, _ptSummon);
            int level = _player.Level;
            var e = Find(b.Guid);
            if (e != null && e.Level > 0) level = e.Level;
            ControlBarView v = ControlBarBuilder.Build(b, kind, Db, level);
            v.Counter = (_player.ControlBar != null ? _player.ControlBar.Counter : 0) + 1;
            foreach (var cd in b.Cooldowns)
            {
                uint ms = cd[1] != 0 ? cd[3] : cd[2];                                            // category cooldown in the 2nd field (Player.cpp:9800-9815)
                if (ms == 0 || ms >= 0x80000000u) continue;                                      // "infinite" = 1 / 0x80000000: until the server clears it
                _ptCdEnd[(int)cd[0]] = _now + ms / 1000.0;
                _ptCdTotal[(int)cd[0]] = ms / 1000f;
            }
            _player.ControlBar = v;
            PtUpdateBar();
            WowLog.Info("Online: " + kind + " bar for 0x" + b.Guid.ToString("X") + (e != null ? " '" + e.Name + "'" : "") + " family " + b.Family + ", duration " +
                        b.DurationMs + " ms, react " + b.React + ", command " + b.Command + ", flags 0x" + b.Flags.ToString("X") + ": " + PtDescribe(v) +
                        (v.Spells.Count > 0 ? "; spellbook " + v.Spells.Count : "") + (b.Cooldowns.Count > 0 ? "; " + b.Cooldowns.Count + " cooldowns" : ""));
        }

        static string PtDescribe(ControlBarView v)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                var s = v.Slots[i];
                if (i > 0) sb.Append(' ');
                sb.Append(s.Kind == ControlSlotKind.Empty ? "-" : s.Kind == ControlSlotKind.Spell ? s.Action + (s.Autocast ? "+" : s.Autocastable ? "~" : "") : s.Name);
            }
            return sb.ToString();
        }

        /// <summary>Per frame: cooldowns, the lit command / reaction / casting spell, usability (dead unit).</summary>
        void PtUpdateBar()
        {
            var v = _player.ControlBar;
            if (v == null) return;
            var u = Find(v.UnitGuid);
            bool dead = u != null && u.Dead;
            ControlBarBuilder.MarkActive(v);
            for (int i = 0; i < 10; i++)
            {
                var s = v.Slots[i];
                if (s == null) { v.Slots[i] = ControlBarBuilder.Empty(); continue; }
                if (s.Kind == ControlSlotKind.Command && s.Action == (int)PetCommand.Attack) s.Active = u != null && u.TargetGuid != 0 && u.InCombat;
                if (s.Kind != ControlSlotKind.Spell) { s.Usable = !dead || s.Action == (int)PetCommand.Dismiss; s.UnusableReason = s.Usable ? null : "Your pet is dead."; continue; }
                PtCooldown(s);
                s.Active = u != null && u.CastingSpellId == s.Action;
                s.Usable = !dead && !s.Passive;
                s.UnusableReason = dead ? "Your pet is dead." : null;                               // ERR_PET_SPELL_DEAD (GlobalStrings.lua:3376)
            }
            for (int i = 0; i < v.Spells.Count; i++) PtCooldown(v.Spells[i]);
        }

        void PtCooldown(ControlSlotView s)
        {
            double end;
            if (_ptCdEnd.TryGetValue(s.Action, out end) && end > _now)
            {
                s.CooldownRemaining = (float)(end - _now);
                float total;
                s.CooldownTotal = _ptCdTotal.TryGetValue(s.Action, out total) ? total : s.CooldownRemaining;
            }
            else { s.CooldownRemaining = 0f; s.CooldownTotal = 0f; }
        }

        // ------------------------------------------------------------------ the controlled unit's cooldowns (second handlers)

        /// <summary>SMSG_SPELL_COOLDOWN (Unit.cpp 16651-16669): uint64 guid; uint8 flags; (uint32 spell, uint32 ms) to the end - only the bar unit's.</summary>
        void PtOnSpellCooldown(WowPacket p)
        {
            ulong guid = p.U64();
            p.U8();
            var v = _player.ControlBar;
            if (v == null || guid == 0 || guid != v.UnitGuid) return;
            while (p.Remaining >= 8)
            {
                int spell = (int)p.U32();
                uint ms = p.U32();
                if (ms == 0 || ms >= 0x80000000u) { _ptCdEnd.Remove(spell); continue; }
                _ptCdEnd[spell] = _now + ms / 1000.0;
                _ptCdTotal[spell] = ms / 1000f;
            }
        }

        /// <summary>SMSG_COOLDOWN_EVENT (uint32 spell, uint64 guid) / SMSG_CLEAR_COOLDOWN (uint32, uint64) / SMSG_MODIFY_COOLDOWN (uint32, uint64, int32 ms).</summary>
        void PtOnCooldownChange(WowPacket p, int what)
        {
            int spell = (int)p.U32();
            ulong guid = p.U64();
            var v = _player.ControlBar;
            if (v == null || guid == 0 || guid != v.UnitGuid) return;
            if (what == 0)
            {
                var s = Db.Find(spell);
                float cd = s != null ? Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs) / 1000f : 0f;
                if (cd > 0f) { _ptCdEnd[spell] = _now + cd; _ptCdTotal[spell] = cd; }
                else _ptCdEnd.Remove(spell);
            }
            else if (what == 1) _ptCdEnd.Remove(spell);
            else
            {
                int delta = p.I32();
                double end;
                if (_ptCdEnd.TryGetValue(spell, out end)) _ptCdEnd[spell] = end + delta / 1000.0;
            }
        }

        // ------------------------------------------------------------------ feedback / failures (s3.3)

        /// <summary>SMSG_PET_CAST_FAILED (Spell.cpp:4859-4861 = WriteCastResultInfo): uint8 castCount; uint32 spell; uint8 result; + extras.</summary>
        void PtOnCastFailed(WowPacket p)
        {
            p.U8();
            int spell = (int)p.U32();
            int result = p.U8();
            uint a, b;
            int extras = SpellCastResults.ReadExtras(p, result, out a, out b);
            SpCheckLeft(p);
            string text = SpellCastResults.Text(result, a, b, extras, _files);
            WowLog.Info("Online: PET_CAST_FAILED " + spell + " result " + result + " " + SpellCastResults.Name(result) + (text != null ? " \"" + text + "\"" : " (no WoW text - nothing shown)"));
            if (text != null) Error(text);
        }

        /// <summary>SMSG_PET_ACTION_FEEDBACK (Unit.cpp:12603-12611, PetDefines.h:71-77): uint8 msg.</summary>
        void PtOnFeedback(WowPacket p)
        {
            int msg = p.U8();
            CheckRead(p);
            switch (msg)
            {
                case 1: Error("Your pet is dead."); break;                     // FEEDBACK_PET_DEAD -> ERR_PET_SPELL_DEAD (GlobalStrings.lua:3376)
                case 2: Error("You have no target."); break;                   // FEEDBACK_NOTHING_TO_ATT -> ours: ERR_GENERIC_NO_TARGET (3064), the client's own text is not in GlobalStrings
                case 3: Error("You cannot attack that target."); break;        // FEEDBACK_CANT_ATT_TARGET -> ERR_INVALID_ATTACK_TARGET (3136)
            }
            WowLog.Info("Online: SMSG_PET_ACTION_FEEDBACK " + msg);
        }

        // SharedDefines.h:3931-3946 PetTameFailureReason -> PETTAME_* (GlobalStrings.lua:5601-5613)
        static readonly string[] PtTameTexts = { null, "Creature not found", "You have too many pets already", "Creature is already controlled", "Creature not tameable",
            "You have an active summon already", "You cannot tame creatures", "You do not have a pet to summon", "Internal pet error", "Creature is too high level for you to tame",
            "Your pet is dead", "Your pet is not dead", "You are unable to control exotic creatures", "Unknown taming error" };

        void PtOnTameFailure(WowPacket p)
        {
            int r = p.U8();
            CheckRead(p);
            string text = r > 0 && r < PtTameTexts.Length ? PtTameTexts[r] : PtTameTexts[13];
            WowLog.Info("Online: SMSG_PET_TAME_FAILURE " + r + " \"" + text + "\"");
            Error(text);
        }

        /// <summary>SMSG_PET_LEARNED_SPELL / SMSG_PET_UNLEARNED_SPELL (PetPackets.cpp:42-52): uint32 spell. The bar follows in a new SMSG_PET_SPELLS.</summary>
        void PtOnLearned(WowPacket p, bool learned)
        {
            int spell = (int)p.U32();
            CheckRead(p);
            string name = SpellName(spell);
            PtInfo(learned ? "Your pet has learned a new spell: " + name + "." : "Your pet has unlearned " + name + ".");   // ERR_PET_LEARN_SPELL_S (3372) / ERR_PET_SPELL_UNLEARNED_S (3381)
        }

        // SharedDefines.h:3914-3928 PetNameInvalidReason -> ERR_NAME_* (GlobalStrings.lua:3278-3291)
        static string PtNameText(int reason)
        {
            switch (reason)
            {
                case 1: return "That name contains invalid characters,  Enter a new name.";
                case 2: return "Please enter a name.";
                case 3: return "That name is too short.  Enter a new name.";
                case 4: return "That name is too long.  Enter a new name.";
                case 6: return "Names must use one language.  Enter a new name.";
                case 7: return "That name contains mature language.  Enter a new name.";
                case 8: return "That name is reserved.  Enter a new name.";
                case 11: return "You cannot use the same letter three times consecutively.  Enter a new name.";
                case 12: return "Names cannot start or end with a space.  Enter a new name.";
                case 13: return "Consecutive spaces are not allowed.  Enter a new name.";
                case 14: return "Consecutive silent characters are not allowed. Create a new name.";
                case 15: return "Silent characters are now allowed at the beginning or end of a name. Create a new name.";
                case 16: return "Your declensions must match your original name.  Enter a new name.";
            }
            return "That name contains invalid characters,  Enter a new name.";
        }

        /// <summary>SMSG_PET_NAME_INVALID (PetHandler.cpp:1112-1126): uint32 reason; CString name; uint8 hasDeclined (+5 CStrings).</summary>
        void PtOnNameInvalid(WowPacket p)
        {
            int reason = (int)p.U32();
            string name = p.CString();
            if (p.U8() != 0) for (int i = 0; i < 5; i++) p.CString();
            CheckRead(p);
            WowLog.Info("Online: SMSG_PET_NAME_INVALID reason " + reason + " for '" + name + "'");
            Error(PtNameText(reason));
        }

        /// <summary>SMSG_PET_NAME_QUERY_RESPONSE (PetHandler.cpp:629-669): uint32 number; CString name; uint32 timestamp; uint8 declined (+5 CStrings).</summary>
        void PtOnNameResponse(WowPacket p)
        {
            int number = (int)p.U32();
            string name = p.CString();
            p.U32();
            if (p.U8() != 0) for (int i = 0; i < 5; i++) p.CString();
            CheckRead(p);
            ulong guid;
            if (!_ptNumberGuid.TryGetValue(number, out guid) || string.IsNullOrEmpty(name)) return;
            _ptNames[guid] = name;
            var e = Find(guid);
            if (e != null) e.Name = name;
            if (_player.PetInfo != null && _player.PetInfo.Guid == guid) _player.PetInfo.Name = name;
            WowLog.Info("Online: pet #" + number + " is called '" + name + "'");
        }

        // ------------------------------------------------------------------ per frame

        partial void TickPets(float dt)
        {
            if (_world == null) return;
            if (_ptInfoAskAt >= 0 && _now >= _ptInfoAskAt) { _ptInfoAskAt = -1; PtRequestInfo("after login / a far teleport"); }
            var v = _player.ControlBar;
            if (v != null && v.DurationTotal > 0f) v.DurationLeft = Math.Max(0f, v.DurationLeft - dt);
            PtUpdateBar();
            PtUpdatePetInfo();
            if (_now >= _ptNameScanAt) { _ptNameScanAt = _now + 1.0; PtAskNames(); }
            PtTickTotems(dt);
            PtStableFillFamilies();
            if (_ptCritter != 0 && _player.CompanionSpellId == 0)
            {
                OnlineObject c;
                if (_objects.TryGetValue(_ptCritter, out c)) _player.CompanionSpellId = c.I32(UF.UNIT_CREATED_BY_SPELL);
            }
            if (_player.Stable != null)
            {
                string why = null;
                if (_player.Dead) why = "we died";
                else if (_ptStableNpc != _player.Guid)
                {
                    var n = Find(_ptStableNpc);
                    if (n == null) why = "the stable master left sight";
                    else if (PtDist(n.Pos, _player.Pos) > PtStableReach) why = "walked away";
                }
                if (why != null) { _player.Stable = null; WowLog.Info("Online: stable closed (" + why + ")"); }
            }
        }

        static float PtDist(WowPos a, WowPos b) { float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); }

        /// <summary>PetInfo from our pet's object (s6.1): number, rename / abandon flags (UNIT_FIELD_BYTES_2 byte 2), happiness (POWER5), XP.</summary>
        void PtUpdatePetInfo()
        {
            OnlineObject o;
            if (_ptSummon == 0 || !_objects.TryGetValue(_ptSummon, out o)) { if (_ptSummon == 0) _player.PetInfo = null; return; }
            var pi = _player.PetInfo;
            if (pi == null || pi.Guid != _ptSummon) { pi = new PetInfoView { Guid = _ptSummon }; _player.PetInfo = pi; }
            pi.Number = o.I32(UF.UNIT_FIELD_PETNUMBER);
            int flags = o.Byte(UF.UNIT_FIELD_BYTES_2, 2);
            pi.CanRename = (flags & 0x01) != 0;                                                  // UNIT_CAN_BE_RENAMED (UnitDefines.h:152)
            pi.IsHunterPet = (flags & 0x02) != 0;                                                // UNIT_CAN_BE_ABANDONED (UnitDefines.h:153)
            pi.HappinessValue = pi.IsHunterPet ? o.I32(UF.UNIT_FIELD_POWER1 + 4) : 0;            // POWER_HAPPINESS
            pi.Happiness = !pi.IsHunterPet ? 0 : pi.HappinessValue < 333000 ? 1 : pi.HappinessValue < 666000 ? 2 : 3;   // Pet.cpp:894-902
            pi.Xp = o.I32(UF.UNIT_FIELD_PETEXPERIENCE);
            pi.XpNext = o.I32(UF.UNIT_FIELD_PETNEXTLEVELEXP);
            int fam = _player.ControlBar != null && _player.ControlBar.UnitGuid == _ptSummon ? _player.ControlBar.Family : 0;
            if (fam == 0) { CreatureInfo ci; if (_creatureInfo.TryGetValue(o.Entry, out ci) && ci != null) fam = ci.Family; }
            if (fam != pi.Family || (fam != 0 && pi.FamilyName == null))
            {
                pi.Family = fam;
                pi.FamilyName = PetFamilyDb.Name(_files, fam);
                pi.FamilyIcon = PetFamilyDb.Icon(_files, fam);
            }
            string n;
            pi.Name = _ptNames.TryGetValue(_ptSummon, out n) ? n : (o.State != null ? o.State.Name : null);
        }

        /// <summary>CMSG_PET_NAME_QUERY (PetHandler.cpp:616-627: uint32 number, uint64 guid) for every pet in sight whose (number, name stamp) is new -
        /// the chosen name ("Fang") is not in the creature query ("Wolf").</summary>
        void PtAskNames()
        {
            foreach (var kv in _objects)
            {
                OnlineObject o = kv.Value;
                if (o == null || o.TypeId != TypeId.Unit) continue;
                int number = o.I32(UF.UNIT_FIELD_PETNUMBER);
                if (number == 0) continue;
                long key = (long)number << 32 | (uint)o.I32(UF.UNIT_FIELD_PET_NAME_TIMESTAMP);
                if (!_ptNameAsked.Add(key)) continue;
                _ptNumberGuid[number] = o.Guid;
                _world.Send(Op.CMSG_PET_NAME_QUERY, new PacketWriter().U32((uint)number).U64(o.Guid));
            }
        }

        // ------------------------------------------------------------------ commands (s3.2)

        /// <summary>The unit pet commands go to: the bar's unit (pet / charm / possess), else our pet.</summary>
        ulong PtCommandUnit()
        {
            var v = _player.ControlBar;
            if (v != null && v.Kind != ControlBarKind.Vehicle && v.UnitGuid != 0) return v.UnitGuid;
            return _player.PetGuid;
        }

        void PtSendAction(ulong unit, uint data, ulong target)
        {
            _world.Send(Op.CMSG_PET_ACTION, new PacketWriter().U64(unit).U32(data).U64(target));   // PetHandler.cpp:57-125
        }

        public void CommandPet(PetCommand command, ulong targetGuid)
        {
            if (_world == null) return;
            ulong unit = PtCommandUnit();
            if (unit == 0) { Error("You don't have a pet!"); return; }                              // ERR_NO_PET (GlobalStrings.lua:3327)
            ulong t = 0;
            if (command == PetCommand.Attack)
            {
                t = targetGuid != 0 ? targetGuid : _target;
                if (t == 0) { Error("You have no target."); return; }                               // ERR_GENERIC_NO_TARGET (3064)
                PtLogOnce("attackLit", "Online: the pet bar's Attack button is lit while the pet has a target and is in combat (ours - AC keeps the Stay / Follow command state, PetHandler.cpp:209-275)");
            }
            PtSendAction(unit, (uint)command | ControlBarBuilder.ActCommand << 24, t);
            var v = _player.ControlBar;
            if (v != null && (command == PetCommand.Stay || command == PetCommand.Follow)) { v.Command = command; ControlBarBuilder.MarkActive(v); }   // PetHandler.cpp:175 / 197
            WowLog.Info("Online: pet command " + command + " -> 0x" + unit.ToString("X") + (t != 0 ? " at 0x" + t.ToString("X") : ""));
        }

        public void SetPetReact(PetReact react)
        {
            if (_world == null) return;
            ulong unit = PtCommandUnit();
            if (unit == 0) { Error("You don't have a pet!"); return; }
            PtSendAction(unit, (uint)react | ControlBarBuilder.ActReaction << 24, 0);               // ACT_REACTION (PetHandler.cpp:306-323)
            var v = _player.ControlBar;
            if (v != null) { v.React = react; ControlBarBuilder.MarkActive(v); }
            WowLog.Info("Online: pet reaction " + react);
        }

        public void UseControlSlot(int slot, ulong targetGuid)
        {
            var v = _player.ControlBar;
            if (v == null || slot < 0 || slot > 9) { Error("You don't have a pet!"); return; }
            var s = v.Slots[slot];
            if (s == null || s.Kind == ControlSlotKind.Empty) return;
            if (s.Kind == ControlSlotKind.Command) { CommandPet((PetCommand)s.Action, targetGuid); return; }
            if (s.Kind == ControlSlotKind.Reaction) { SetPetReact((PetReact)s.Action); return; }
            if (s.NeedsPlace)
            {
                // reached without the circle (a probe, a key without the UI): at the current target's feet, else the controlled unit's own
                var t = Find(targetGuid != 0 ? targetGuid : _target);
                var u = Find(v.UnitGuid);
                WowPos at = t != null ? t.Pos : u != null ? u.Pos : _player.Pos;
                PtCast(v, slot, s, targetGuid, true, at);
                return;
            }
            PtCast(v, slot, s, targetGuid, false, default(WowPos));
        }

        public void UseControlSlotAt(int slot, WowPos dest)
        {
            var v = _player.ControlBar;
            if (v == null || slot < 0 || slot > 9) { Error("You don't have a pet!"); return; }
            var s = v.Slots[slot];
            if (s == null || s.Kind != ControlSlotKind.Spell) return;
            var u = Find(v.UnitGuid);
            if (u != null && s.RangeMax > 0f && PtDist(u.Pos, dest) > s.RangeMax + 1f) { Error("Out of range"); return; }   // SPELL_FAILED_OUT_OF_RANGE (GS 7147); +1 yd ours
            PtCast(v, slot, s, 0, true, dest);
        }

        public void UsePetSpell(int spellId, ulong targetGuid)
        {
            var v = _player.ControlBar;
            if (v == null) { Error("You don't have a pet!"); return; }
            int slot = ControlBarBuilder.SlotOf(v, spellId);
            var s = slot >= 0 ? v.Slots[slot] : ControlBarBuilder.BookEntry(v, spellId);
            if (s == null) { Error("Spell not learned"); return; }                                   // SPELL_FAILED_NOT_KNOWN (GS 7103)
            if (slot >= 0) { UseControlSlot(slot, targetGuid); return; }
            if (s.NeedsPlace)
            {
                var t = Find(targetGuid != 0 ? targetGuid : _target);
                var u = Find(v.UnitGuid);
                PtCast(v, -1, s, targetGuid, true, t != null ? t.Pos : u != null ? u.Pos : _player.Pos);
                return;
            }
            PtCast(v, -1, s, targetGuid, false, default(WowPos));
        }

        /// <summary>A bar / book spell of the controlled unit (s6.1 PetsCast), by ControlBar.Kind.</summary>
        void PtCast(ControlBarView v, int slot, ControlSlotView s, ulong targetGuid, bool haveDest, WowPos dest)
        {
            if (_world == null) return;
            if (s.Passive) return;
            var u = Find(v.UnitGuid);
            if (u != null && u.Dead) { Error("Your pet is dead."); return; }                          // ERR_PET_SPELL_DEAD (3376)
            if (s.CooldownRemaining > PtCooldownSlack) { Error("Spell is not ready yet."); return; }  // ERR_SPELL_COOLDOWN (3493)
            SpellInfo info = Db.Find(s.Action);
            if (v.Kind == ControlBarKind.Vehicle)
            {
                // the vehicle owner's VehCast (OnlineSession.Vehicle.cs, s6.2): CMSG_CAST_SPELL as the vehicle; aimed = castFlags 0x02 + the ballistic dest
                WowPos? at = haveDest ? dest : (WowPos?)null;
                if (slot >= 0) VehCast(slot, targetGuid, at);
                else VehCastSpell(s.Action, targetGuid, at);
                return;
            }
            ulong t = PtTargetFor(s, targetGuid, false);
            if (t == ulong.MaxValue) return;
            bool viaCast = v.Kind == ControlBarKind.Possess || haveDest || PtAreaEnemy(info);       // CMSG_CAST_SPELL refused while possessing (SpellHandler.cpp:522-525); area / dest spells refused by CMSG_PET_ACTION (PetHandler.cpp:344-348)
            if (!viaCast)
            {
                PtSendAction(v.UnitGuid, (uint)s.Action | ControlBarBuilder.RawType(s) << 24, t);
                WowLog.Info("Online: pet spell " + s.Action + " '" + s.Name + "' (CMSG_PET_ACTION)" + (t != 0 ? " at 0x" + t.ToString("X") : ""));
                return;
            }
            _castCount++;
            if (_castCount == 0) _castCount = 1;
            var w = new PacketWriter().U64(v.UnitGuid).U8(_castCount).U32((uint)s.Action).U8(0);    // CMSG_PET_CAST_SPELL (PetHandler.cpp:1011-1110)
            PtWriteTargets(w, info, t, haveDest, dest, u);
            _world.Send(Op.CMSG_PET_CAST_SPELL, w);
            WowLog.Info("Online: pet spell " + s.Action + " '" + s.Name + "' (CMSG_PET_CAST_SPELL, castCount " + _castCount + ")" + (haveDest ? " at " + dest : t != 0 ? " at 0x" + t.ToString("X") : ""));
        }

        /// <summary>The unit target of a control-bar spell: harmful -> the given / current target (none = "You have no target.", MaxValue);
        /// helpful -> the given target, else the current target when friendly, else 0 (the unit itself through CheckPetCast, PetHandler.cpp:339-342).</summary>
        ulong PtTargetFor(ControlSlotView s, ulong targetGuid, bool vehicle)
        {
            if (s.NeedsPlace) return 0;
            if (s.Harmful || s.NeedsUnit)
            {
                ulong t = targetGuid != 0 ? targetGuid : _target;
                if (t == 0 && (s.NeedsUnit || !vehicle)) { Error("You have no target."); return ulong.MaxValue; }   // ERR_GENERIC_NO_TARGET (3064)
                return t;
            }
            if (targetGuid != 0) return targetGuid;
            var cur = Find(_target);
            return cur != null && cur.Reaction == Reaction.Friendly ? _target : 0;
        }

        static bool PtAreaEnemy(SpellInfo s)
        {
            if (s == null) return false;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e != null && (e.TargetA == 15 || e.TargetA == 16 || e.TargetA == 28)) return true;   // SRC_AREA_ENEMY / DEST_AREA_ENEMY / DEST_DYNOBJ_ENEMY
            }
            return false;
        }

        void PtWriteTargets(PacketWriter w, SpellInfo info, ulong unit, bool haveDest, WowPos dest, EntityState from)
        {
            uint mask = 0;
            if (unit != 0) mask |= TfUnit;
            WowPos src = from != null ? from.Pos : _player.Pos;
            if (haveDest)
            {
                mask |= TfDestLocation;
                if (info != null && (info.Targets & TfSourceLocation) != 0) mask |= TfSourceLocation;
            }
            SpWriteTargets(w, mask, unit, src, dest);
        }

        public void SetPetAutocast(int spellId, bool on)
        {
            if (_world == null) return;
            var v = _player.ControlBar;
            int slot = ControlBarBuilder.SlotOf(v, spellId);
            var book = ControlBarBuilder.BookEntry(v, spellId);
            var s = slot >= 0 ? v.Slots[slot] : book;
            if (v == null || s == null || !s.Autocastable) return;
            _world.Send(Op.CMSG_PET_SPELL_AUTOCAST, new PacketWriter().U64(v.UnitGuid).U32((uint)spellId).U8(on ? 1 : 0));   // PetPackets.cpp:35-40; no answer
            if (slot >= 0) v.Slots[slot].Autocast = on;
            if (book != null) book.Autocast = on;
            WowLog.Info("Online: pet autocast " + spellId + " " + (on ? "on" : "off"));
        }

        public void SetPetBarSlot(int slot, int spellId)
        {
            if (_world == null) return;
            var v = _player.ControlBar;
            if (v == null || slot < 3 || slot > 6 || (v.Kind != ControlBarKind.Pet && v.Kind != ControlBarKind.Charm)) return;
            var book = spellId != 0 ? ControlBarBuilder.BookEntry(v, spellId) : null;
            if (spellId != 0 && book == null) { Error("Spell not learned"); return; }
            uint data = spellId == 0 ? 0x01000000u : (uint)spellId | (book.Autocast ? ControlBarBuilder.ActEnabled : ControlBarBuilder.ActDisabled) << 24;
            _world.Send(Op.CMSG_PET_SET_ACTION, new PacketWriter().U64(v.UnitGuid).U32((uint)slot).U32(data));   // PetHandler.cpp:696-838 (one pair = place / remove)
            int level = Find(v.UnitGuid) != null ? Find(v.UnitGuid).Level : _player.Level;
            v.Slots[slot] = spellId == 0 ? ControlBarBuilder.Empty() : ControlBarBuilder.SpellSlot(Db, spellId, ControlBarBuilder.TypeOf(data), level);
            WowLog.Info("Online: pet bar slot " + slot + " = " + (spellId == 0 ? "empty" : spellId.ToString()));
        }

        public void RenamePet(string name)
        {
            if (_world == null) return;
            var pi = _player.PetInfo;
            if (pi == null || !pi.CanRename) { Error("Your pet can't be renamed."); return; }           // ERR_PET_NOT_RENAMEABLE (3373)
            _world.Send(Op.CMSG_PET_RENAME, new PacketWriter().U64(pi.Guid).CString(name ?? "").U8(0));   // PetHandler.cpp:840-929
            WowLog.Info("Online: CMSG_PET_RENAME sent");
        }

        public void AbandonPet()
        {
            if (_world == null) return;
            var pi = _player.PetInfo;
            if (pi == null || !pi.IsHunterPet) { Error("You don't have a pet!"); return; }
            _world.Send(Op.CMSG_PET_ABANDON, new PacketWriter().U64(pi.Guid));                     // PetHandler.cpp:931-953 (permanent)
            WowLog.Info("Online: CMSG_PET_ABANDON 0x" + pi.Guid.ToString("X"));
        }

        public void DismissPet()
        {
            var pi = _player.PetInfo;
            if (pi != null && pi.IsHunterPet && (_player.ControlBar == null || _player.ControlBar.UnitGuid == pi.Guid)) { CastSpell(PetDismissSpell, 0); return; }   // Dismiss Pet, never COMMAND_ABANDON (= abandon for a hunter pet)
            CommandPet(PetCommand.Dismiss, 0);
        }

        // ------------------------------------------------------------------ totems (s3.5)

        /// <summary>SMSG_TOTEM_CREATED (TotemPackets.cpp:25-31): uint8 slot; uint64 totem; uint32 duration ms; uint32 spell - before its create block.</summary>
        void PtOnTotemCreated(WowPacket p)
        {
            int slot = p.U8();
            ulong guid = p.U64();
            uint ms = p.U32();
            int spell = (int)p.U32();
            CheckRead(p);
            if (slot > 3) return;
            var s = Db.Find(spell);
            _player.Totems[slot] = new TotemView { Slot = slot, Guid = guid, SpellId = spell, Name = s != null ? s.Name : "Totem", IconPath = s != null ? s.IconPath : null, Duration = ms / 1000f, Remaining = ms / 1000f };
            _ptTotemBorn[slot] = _now;
            WowLog.Info("Online: totem " + slot + " '" + (s != null ? s.Name : spell.ToString()) + "' for " + ms / 1000 + " s");
        }

        void PtTickTotems(float dt)
        {
            for (int i = 0; i < 4; i++)
            {
                var t = _player.Totems[i];
                if (t == null) continue;
                t.Remaining -= dt;
                bool gone = t.Remaining < -1f || (_now - _ptTotemBorn[i] > 1.0 && t.Guid != 0 && Find(t.Guid) == null);
                if (gone) _player.Totems[i] = null;
            }
        }

        public void DestroyTotem(int slot)
        {
            if (_world == null || slot < 0 || slot > 3) return;
            if (_player.Vehicle != null) { Error("You can't do that right now."); return; }            // AC ignores it while m_mover != us (SpellHandler.cpp:688-690); ERR_CLIENT_LOCKED_OUT (3008)
            _world.Send(Op.CMSG_TOTEM_DESTROYED, new PacketWriter().U8(slot));                      // SpellHandler.cpp:686-704
            WowLog.Info("Online: CMSG_TOTEM_DESTROYED " + slot);
        }

        public void SetTotemBarPage(int page) { if (page >= 1 && page <= 3) _player.TotemBarPage = page; }

        // ------------------------------------------------------------------ companions (s3.9)

        public void DismissCompanion()
        {
            if (_world == null) return;
            if (_ptCritter == 0) return;
            _world.Send(Op.CMSG_DISMISS_CRITTER, new PacketWriter().U64(_ptCritter));              // PetPackets.cpp:20-23, PetHandler.cpp:39-55
            WowLog.Info("Online: CMSG_DISMISS_CRITTER 0x" + _ptCritter.ToString("X"));
        }

        // ------------------------------------------------------------------ spell click / stable (npc's service registry)

        /// <summary>npc rule 0 (registered evenWithGossip): a unit with UNIT_NPC_FLAG_SPELLCLICK -> CMSG_SPELLCLICK (SpellHandler.cpp:723-739).</summary>
        bool PetsSpellClick(ulong guid)
        {
            if (_world == null) return false;
            if (_player.Vehicle != null && _player.Vehicle.Guid == guid) return true;              // ours: the real client does not re-board
            _world.Send(Op.CMSG_SPELLCLICK, new PacketWriter().U64(guid));
            var e = Find(guid);
            WowLog.Info("Online: spell click on " + (e != null ? e.Name + " (entry " + e.Entry + ", vehicle " + e.VehicleId + ")" : "0x" + guid.ToString("X")));
            return true;
        }

        /// <summary>npc rule 6: a stable master without gossip -> MSG_LIST_STABLED_PETS (NPCHandler.cpp:334-353); a non-hunter -> false (gossip hello).</summary>
        bool PetsStableHello(ulong npc)
        {
            if (_world == null || _player.Class != WowClass.Hunter) return false;
            _ptStableNpc = npc;
            _world.Send(Op.MSG_LIST_STABLED_PETS, new PacketWriter().U64(npc));
            return true;
        }

        /// <summary>MSG_LIST_STABLED_PETS (NPCHandler.cpp:355-416): uint64 npc; uint8 count; uint8 slotsBought; per pet {uint32 number; uint32 entry;
        /// uint32 level; CString name; uint8 flags 1 current / 2 stabled}.</summary>
        void PtOnStableList(WowPacket p)
        {
            ulong npc = p.U64();
            int n = p.U8();
            int bought = p.U8();
            var v = new StableView { NpcGuid = npc, SlotsBought = bought, Counter = (_player.Stable != null ? _player.Stable.Counter : 0) + 1 };
            v.NextSlotPrice = bought < 4 ? PetFamilyDb.SlotPrice(_files, bought + 1) : 0;
            int stabled = 0;
            for (int i = 0; i < n; i++)
            {
                var sp = new StablePetView { Number = (int)p.U32(), Entry = (int)p.U32(), Level = (int)p.U32(), Name = p.CString() };
                int flags = p.U8();
                sp.Current = (flags & 1) != 0;
                sp.Slot = sp.Current ? 0 : ++stabled;
                CreatureInfo ci;
                if (_creatureInfo.TryGetValue(sp.Entry, out ci) && ci != null && !ci.Missing) PtStableFamily(sp, ci.Family);
                else if (_creatureAsked.Add(sp.Entry)) _world.Send(Op.CMSG_CREATURE_QUERY, new PacketWriter().U32((uint)sp.Entry).U64(0));
                v.Pets.Add(sp);
            }
            CheckRead(p);
            _ptStableNpc = npc;
            NpcServiceOpened(npc, "stable");
            _player.Stable = v;
            WowLog.Info("Online: stable of 0x" + npc.ToString("X") + " - " + n + " pets, " + bought + " slots bought, next slot " + v.NextSlotPrice + " copper");
        }

        void PtStableFamily(StablePetView sp, int family)
        {
            sp.Family = family;
            sp.FamilyName = PetFamilyDb.Name(_files, family);
            sp.IconPath = PetFamilyDb.Icon(_files, family);
        }

        /// <summary>SMSG_STABLE_RESULT (NPCHandler.cpp:38-46, 418-423): uint8.</summary>
        void PtOnStableResult(WowPacket p)
        {
            int r = p.U8();
            CheckRead(p);
            WowLog.Info("Online: SMSG_STABLE_RESULT 0x" + r.ToString("X"));
            switch (r)
            {
                case 0x01: Error("You don't have enough money."); return;                         // STABLE_ERR_MONEY -> ERR_NOT_ENOUGH_MONEY (3307)
                case 0x06: Error("The stable master can't do that now."); return;                 // STABLE_ERR_STABLE: ours (no client text)
                case 0x0C: Error("You are unable to control exotic creatures"); return;          // STABLE_ERR_EXOTIC -> PETTAME_CANTCONTROLEXOTIC (5602)
                case 0x08: PtInfo("Pet stabled."); break;                                         // ours
                case 0x09: PtInfo("Pet retrieved."); break;                                       // ours
                case 0x0A: PtInfo("Stable slot purchased."); break;                               // ours
                default: return;
            }
            if (_ptStableNpc != 0 && _world != null) _world.Send(Op.MSG_LIST_STABLED_PETS, new PacketWriter().U64(_ptStableNpc));   // the list is not re-sent by AC
        }

        public void StablePet()
        {
            if (_world == null || _player.Stable == null) return;
            _world.Send(Op.CMSG_STABLE_PET, new PacketWriter().U64(_player.Stable.NpcGuid));       // NPCHandler.cpp:425-491
        }

        public void UnstablePet(int petNumber)
        {
            if (_world == null || _player.Stable == null) return;
            bool current = false;
            foreach (var sp in _player.Stable.Pets) if (sp.Current) current = true;
            _world.Send(current ? Op.CMSG_STABLE_SWAP_PET : Op.CMSG_UNSTABLE_PET, new PacketWriter().U64(_player.Stable.NpcGuid).U32((uint)petNumber));   // 640-760 / 493-596
        }

        public void BuyStableSlot()
        {
            if (_world == null || _player.Stable == null) return;
            _world.Send(Op.CMSG_BUY_STABLE_SLOT, new PacketWriter().U64(_player.Stable.NpcGuid));  // NPCHandler.cpp:598-634
        }

        public void CloseStable()
        {
            if (_player.Stable != null) WowLog.Info("Online: stable closed");
            _player.Stable = null;
        }

        /// <summary>Stable pets whose creature query answered after the list came: family / icon filled.</summary>
        void PtStableFillFamilies()
        {
            var st = _player.Stable;
            if (st == null) return;
            foreach (var sp in st.Pets)
            {
                if (sp.Family != 0) continue;
                CreatureInfo ci;
                if (_creatureInfo.TryGetValue(sp.Entry, out ci) && ci != null && !ci.Missing && ci.Family != 0) PtStableFamily(sp, ci.Family);
            }
        }
    }
}
