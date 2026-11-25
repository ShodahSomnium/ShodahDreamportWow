using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Pets;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// Iteration 3: the player's pets (Warlock Imp / Voidwalker, EntityKind.Pet) and Shaman totems (EntityKind.Totem). Both live in the
    /// creature list (Reaction Friendly, OwnerGuid = the player) so views see them in Entities; creatures pick them as victims by threat.
    /// </summary>
    public sealed partial class OfflineSession
    {
        private OfflineCreature _pet;
        private readonly OfflineCreature[] _totems = new OfflineCreature[5];   // by SummonProperties slot 1..4
        private uint _summonCounter;

        // Iteration 11 (pets, proposal-pets.md s7): the pet obeys the pet bar. Nothing of it is saved (each summon starts at AzerothCore's
        // new-pet defaults: Follow, Defensive - CharmInfo.cpp:52-65, PetHandler.cpp:189-207; autocast ON - ours, decision U6).
        private PetCommand _petCmd = PetCommand.Follow;
        private PetReact _petReact = PetReact.Defensive;
        private WowPos _petStayAt;
        private ulong _petAttackGuid;
        private readonly HashSet<int> _petAutoOff = new HashSet<int>();
        private ControlBarView _offBar;
        private PetInfoView _offPet;
        private int _petBoltId, _petTauntId;
        private SpellInfo _petQueued;
        private ulong _petQueuedTarget;
        private bool _petRulesLogged;

        /// <summary>The player's pet state (null = none).</summary>
        public EntityState Pet { get { return _pet != null && _pet.Present ? _pet.State : null; } }

        // ------------------------------------------------------------------ server data (ids and numbers only)

        /// <summary>
        /// Warlock pets: creature entry (the SUMMON_PET effect's MiscValue: Summon Imp 688 -> 416, Summon Voidwalker 697 -> 1860, measured),
        /// CreatureFamily (23 "Imp" skill line 188, 16 "Voidwalker" skill line 204, read from CreatureFamily.dbc) and the display id of
        /// creature_template (4449 = Creature\Imp\Imp.mdx, 1132 = Creature\VoidWalker\VoidWalker.mdx, verified in CreatureDisplayInfo).
        /// </summary>
        private static bool PetInfo(int entry, out int family, out int display, out float attackSpeed)
        {
            switch (entry)
            {
                case 416: family = 23; display = 4449; attackSpeed = 2f; return true;
                case 1860: family = 16; display = 1132; attackSpeed = 2f; return true;
            }
            family = 0; display = 0; attackSpeed = 2f;
            return false;
        }

        /// <summary>
        /// pet_levelstats.sql (AzerothCore master, quoted): hp, mana, armor, str, agi, sta, int, spi, min_dmg, max_dmg for levels 1-10.
        /// The voidwalker only has a real row at 10 (it is learned at 10); lower levels use that row.
        /// </summary>
        private static readonly int[,] ImpStats =
        {
            { 34, 10, 22, 20, 20, 20, 25, 23, 0, 1 }, { 43, 48, 21, 21, 20, 20, 26, 24, 1, 2 }, { 56, 57, 32, 22, 20, 21, 27, 25, 2, 4 },
            { 68, 66, 57, 23, 21, 21, 29, 26, 3, 6 }, { 80, 76, 87, 23, 21, 22, 30, 27, 4, 8 }, { 92, 85, 122, 24, 21, 22, 31, 28, 6, 10 },
            { 105, 95, 166, 25, 21, 23, 32, 29, 7, 12 }, { 119, 105, 216, 26, 21, 23, 33, 30, 8, 13 }, { 134, 115, 275, 27, 21, 23, 35, 32, 9, 14 },
            { 149, 126, 342, 27, 22, 24, 36, 33, 9, 15 }
        };
        private static readonly int[] VoidwalkerStats10 = { 205, 147, 831, 29, 10, 29, 25, 27, 7, 13 };

        /// <summary>
        /// player_totem_model.sql (AzerothCore master, quoted): TotemID (= SummonProperties.Slot: 1 fire, 2 earth, 3 water, 4 air) x race ->
        /// CreatureDisplayInfo id. Verified in the files: 30757 OrcTotem_Earth.mdx, 30758 OrcTotem_Fire.mdx, 19073/19074 DraeneiTotem_*,
        /// 4588/4589 Earth/FireElementalTotem.mdx (Tauren).
        /// </summary>
        private static int TotemDisplay(int slot, int race)
        {
            int[] orc = { 30758, 30757, 30759, 30756 }, dwarf = { 30754, 30753, 30755, 30736 }, tauren = { 4589, 4588, 4587, 4590 };
            int[] troll = { 30762, 30761, 30763, 30760 }, draenei = { 19074, 19073, 19075, 19071 };
            int i = Math.Max(1, Math.Min(4, slot)) - 1;
            switch (race)
            {
                case WowRaces.Dwarf: return dwarf[i];
                case WowRaces.Tauren: return tauren[i];
                case WowRaces.Troll: return troll[i];
                case WowRaces.Draenei: return draenei[i];
                default: return orc[i];
            }
        }

        /// <summary>
        /// The spell a totem creature runs (creature_template spell1 on the server; ids checked by name in Spell.dbc): Stoneskin Totem 8071 ->
        /// 8072 "Stoneskin" (area armor), Earthbind Totem 2484 -> 6474 "Earthbind Totem Passive" (pulses 3600 every 3 s), Stoneclaw Totem 5730
        /// -> 5728 "Stoneclaw Totem Passive" (pulses 5729 every 2 s), Searing Totem 3599 -> 3606 "Attack" (2.2 s cast), Strength of Earth
        /// Totem 8075 -> 8076 "Strength of Earth". Other totems: the first spell their tooltip names ($8072a1 ...).
        /// </summary>
        private SpellInfo TotemSpellFor(SpellInfo summon)
        {
            int id = 0;
            switch (summon.Id)
            {
                case 8071: id = 8072; break;
                case 2484: id = 6474; break;
                case 5730: id = 5728; break;
                case 3599: id = 3606; break;
                case 8075: id = 8076; break;
            }
            if (id == 0)
            {
                string d = summon.Description ?? "";
                for (int i = 0; i < d.Length - 1 && id == 0; i++)
                {
                    if (d[i] != '$' || !char.IsDigit(d[i + 1])) continue;
                    int j = i + 1, v = 0;
                    while (j < d.Length && char.IsDigit(d[j]) && v < 10000000) { v = v * 10 + (d[j] - '0'); j++; }
                    if (v != summon.Id && _db.Find(v) != null) id = v;
                }
            }
            return id > 0 ? _db.Find(id) : null;
        }

        private DbcTable _summonProps;

        /// <summary>SummonProperties.dbc Slot (field 4; measured 63 -> 1 fire, 81 -> 2 earth).</summary>
        private int SummonSlot(int propsId)
        {
            if (_summonProps == null && _files != null) _summonProps = Dbc.Load(_files, "SummonProperties");
            if (_summonProps == null) return 0;
            int r = _summonProps.FindRow(propsId);
            return r >= 0 ? _summonProps.GetInt(r, 4) : 0;
        }

        private DbcTable _creatureFamily;

        private string FamilyName(int family, string fallback)
        {
            if (_creatureFamily == null && _files != null) _creatureFamily = Dbc.Load(_files, "CreatureFamily");
            if (_creatureFamily != null)
            {
                int r = _creatureFamily.FindRow(family);
                if (r >= 0) { string n = _creatureFamily.GetLocalizedString(r, 10); if (!string.IsNullOrEmpty(n)) return n; }
            }
            return fallback;
        }

        private int FamilySkillLine(int family)
        {
            if (_creatureFamily == null && _files != null) _creatureFamily = Dbc.Load(_files, "CreatureFamily");
            if (_creatureFamily == null) return 0;
            int r = _creatureFamily.FindRow(family);
            return r >= 0 ? _creatureFamily.GetInt(r, 5) : 0;
        }

        // ------------------------------------------------------------------ creating summons

        private OfflineCreature NewSummon(SummonKind kind, int entry, string name, int display, int level, int health, WowPos pos, SpellInfo spell)
        {
            _summonCounter++;
            var tpl = new CreatureTemplate { Entry = entry, Name = name, SubName = "", DisplayIds = new[] { display }, MinLevel = level, MaxLevel = level, Reaction = Reaction.Friendly, RunSpeed = 7f, WalkSpeed = 2.5f };
            var c = new OfflineCreature();
            c.Tpl = tpl;
            c.Spawn = new SpawnDef { Entry = entry, Pos = pos, RespawnSeconds = 0f };
            c.Summon = kind;
            c.Owner = PlayerGuid;
            c.SummonSpell = spell;
            c.Guid = kind == SummonKind.Pet ? (0xF140UL << 48) | _summonCounter : (0xF130UL << 48) | ((ulong)(uint)entry << 24) | (0xE00000UL + (_summonCounter & 0xFFFFF));
            c.Level = level;
            c.MaxHealth = Math.Max(1, health);
            c.Health = c.MaxHealth;
            c.Pos = pos;
            c.Home = pos;
            c.Present = true;
            c.Ai = AiState.Idle;
            c.CombatReach = kind == SummonKind.Totem ? 0.5f : 1f;
            c.CreatureType = kind == SummonKind.Totem ? 11 : 3;   // CreatureType.dbc 11 Totem, 3 Demon (warlock pets)
            c.BaseDisplayId = display;
            var s = new EntityState();
            s.Guid = c.Guid;
            s.Entry = entry;
            s.Kind = kind == SummonKind.Pet ? EntityKind.Pet : EntityKind.Totem;
            s.Name = name;
            s.SubName = kind == SummonKind.Pet ? (_player.Name ?? "") : "";
            s.Reaction = Reaction.Friendly;
            s.DisplayId = display;
            s.Level = level;
            s.Health = c.Health;
            s.MaxHealth = c.MaxHealth;
            s.Pos = pos;
            s.OwnerGuid = PlayerGuid;
            s.CreatureType = c.CreatureType;
            s.ConColor = 2;
            c.State = s;
            _creatures.Add(c);
            _byGuid[c.Guid] = c;
            _entitiesDirty = true;
            return c;
        }

        /// <summary>SUMMON_PET (Summon Imp / Summon Voidwalker): replaces the current pet.</summary>
        private bool SummonPet(SpellInfo spell, int entry)
        {
            int family, display; float speed;
            if (!PetInfo(entry, out family, out display, out speed)) return false;
            DismissPet(false);
            int lvl = _player.Level;
            int[] row = new int[10];
            if (entry == 416) for (int k = 0; k < 10; k++) row[k] = ImpStats[Math.Max(1, Math.Min(10, lvl)) - 1, k];
            else Array.Copy(VoidwalkerStats10, row, 10);
            // Guardian::UpdateMaxHealth/UpdateMaxPower shape (TrinityCore 3.3.5, recalled - APPROXIMATED): the pet gets 75% of the owner's
            // stamina and 30% of the owner's intellect; health per bonus stamina 8.4 (imp) / 11 (voidwalker), mana per bonus intellect 4.95 / 11.5.
            float bonusSta = _sta * 0.75f, bonusInt = _int * 0.3f;
            float hpMul = entry == 416 ? 8.4f : 11f, manaMul = entry == 416 ? 4.95f : 11.5f;
            int health = (int)(row[0] + bonusSta * hpMul);
            float ang = _me.Pos.O + 2.3f;
            var pos = new WowPos(_me.Pos.X + (float)Math.Cos(ang) * 2.5f, _me.Pos.Y + (float)Math.Sin(ang) * 2.5f, _me.Pos.Z, _me.Pos.O);
            pos.Z = Ground(pos.X, pos.Y, pos.Z);
            var c = NewSummon(SummonKind.Pet, entry, FamilyName(family, entry == 416 ? "Imp" : "Voidwalker"), display, lvl, health, pos, spell);
            c.MaxMana = (int)(row[1] + bonusInt * manaMul);
            c.Mana = c.MaxMana;
            c.Armor = row[2];
            c.AttackSpeed = speed;
            c.MinDamage = Math.Max(1, row[8]);
            c.MaxDamage = Math.Max(c.MinDamage + 1, row[9]);
            c.DamageMod = 1f;
            c.SummonRemaining = -1f;
            c.State.Power = PowerType.Mana;
            // Pet abilities: the SkillLineAbility rows of the family's skill line (CreatureFamily.dbc skillLine[0]) up to the pet's level,
            // highest rank per name, passives ("Tamed Pet Passive", scaling auras) left out.
            int line = FamilySkillLine(family);
            var sla = line != 0 && _files != null ? Dbc.Load(_files, "SkillLineAbility") : null;
            if (sla != null)
            {
                var best = new Dictionary<string, SpellInfo>();
                for (int r = 0; r < sla.RecordCount; r++)
                {
                    if (sla.GetInt(r, SpellFields.SkillLineAbility.SkillLine) != line) continue;
                    var ps = _db.Find(sla.GetInt(r, SpellFields.SkillLineAbility.Spell));
                    if (ps == null || ps.IsPassive || ps.SpellLevel < 1 || ps.SpellLevel > lvl) continue;
                    SpellInfo cur;
                    if (!best.TryGetValue(ps.Name, out cur) || ps.SpellLevel > cur.SpellLevel) best[ps.Name] = ps;
                }
                c.PetSpells.AddRange(best.Values);
            }
            _player.PetGuid = c.Guid;
            _pet = c;
            _rec.PetEntry = entry;
            // Area auras the pet keeps on its owner (Blood Pact).
            foreach (var ps in c.PetSpells)
                if (ps.HasEffect(SpellEffect.ApplyAreaAuraRaid) || ps.HasEffect(SpellEffect.ApplyAreaAuraParty)) ApplySpellAuras(ps, c, _me, 0);
            OffBuildPetBar(c, family);
            WowLog.Info("OfflineSession: summoned " + c.State.Name + " (level " + lvl + ", " + c.MaxHealth + " health, " + c.MaxMana + " mana, " + c.PetSpells.Count + " abilities)");
            return true;
        }

        private void DismissPet(bool forget)
        {
            if (_pet == null) return;
            RemoveSummon(_pet, false);
            _pet = null;
            _player.PetGuid = 0;
            OffClearPetBar();
            if (forget) _rec.PetEntry = 0;
        }

        /// <summary>SUMMON with a totem SummonProperties slot: one totem per element; replaces the old one.</summary>
        private bool SummonTotem(SpellInfo spell, SpellEffectInfo e)
        {
            int slot = SummonSlot(e.MiscValueB);
            if (slot < 1 || slot > 4) return false;
            if (_totems[slot] != null) RemoveSummon(_totems[slot], false);
            int health = Math.Max(1, CalcValue(spell, e, _player.Level));
            // Fire totems stand in front-left, earth front-right, water back-left, air back-right of the caster (about 2 yd).
            float[] angles = { 0f, 0.8f, -0.8f, 2.4f, -2.4f };
            float ang = _me.Pos.O + angles[slot];
            var pos = new WowPos(_me.Pos.X + (float)Math.Cos(ang) * 2f, _me.Pos.Y + (float)Math.Sin(ang) * 2f, _me.Pos.Z, _me.Pos.O);
            pos.Z = Ground(pos.X, pos.Y, pos.Z);
            var c = NewSummon(SummonKind.Totem, e.MiscValue, spell.Name, TotemDisplay(slot, _rec.Race), _player.Level, health, pos, spell);
            c.TotemSlot = slot;
            int dur = spell.DurationAt(_player.Level);
            c.SummonRemaining = dur > 0 ? dur / 1000f : 120f;
            c.TotemSpell = TotemSpellFor(spell);
            c.TotemTimer = 0.5f;
            c.Armor = 0;
            _totems[slot] = c;
            // iteration 11 (pets): the totem bar's one data path for both sessions (Player.Totems by element 0 fire .. 3 air = slot - 1)
            _player.Totems[slot - 1] = new TotemView { Slot = slot - 1, Guid = c.Guid, SpellId = spell.Id, Name = spell.Name, IconPath = spell.IconPath, Duration = c.SummonRemaining, Remaining = c.SummonRemaining };
            return true;
        }

        /// <summary>Removes a pet/totem: its auras on others end, creatures forget it, the entity leaves the list (killed: EntityDied first).</summary>
        private void RemoveSummon(OfflineCreature s, bool killed)
        {
            if (s == null || !s.Present) return;
            RemoveAurasFromCaster(s.Guid);
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                c.Threat.Remove(s.Guid);
                if (c.ForcedTarget == s.Guid) { c.ForcedTarget = 0; c.ForcedTimer = 0f; }
            }
            if (killed) Emit(GameEventType.EntityDied, 0, s.Guid, 0, 0, false, s.State.Name);
            s.Present = false;
            s.Dead = true;
            _creatures.Remove(s);
            _byGuid.Remove(s.Guid);
            _entitiesDirty = true;
            if (s == _pet) { _pet = null; _player.PetGuid = 0; OffClearPetBar(); if (killed) _rec.PetEntry = 0; }
            if (s.Summon == SummonKind.Totem && s.TotemSlot > 0 && s.TotemSlot < _totems.Length && _totems[s.TotemSlot] == s)
            {
                _totems[s.TotemSlot] = null;
                if (s.TotemSlot <= 4 && _player.Totems[s.TotemSlot - 1] != null && _player.Totems[s.TotemSlot - 1].Guid == s.Guid) _player.Totems[s.TotemSlot - 1] = null;   // iteration 11 (pets)
            }
            if (_target == s.Guid) _target = 0;
        }

        private void DismissAllSummons()
        {
            DismissPet(true);
            for (int i = 1; i < _totems.Length; i++) if (_totems[i] != null) RemoveSummon(_totems[i], false);
        }

        /// <summary>Auras a pet/totem put on the player or creatures end with it (Blood Pact, Stoneskin, Earthbind).</summary>
        private void RemoveAurasFromCaster(ulong caster)
        {
            for (int a = _me.Auras.Count - 1; a >= 0; a--) if (a < _me.Auras.Count && _me.Auras[a].Caster == caster) RemoveAuraAt(_me, a);
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                for (int a = c.Auras.Count - 1; a >= 0; a--) if (a < c.Auras.Count && c.Auras[a].Caster == caster) RemoveAuraAt(c, a);
            }
        }

        /// <summary>Damage to the player's pet or totem (creature swings and spells).</summary>
        private void DamageSummon(OfflineCreature s, int amount, OfflineUnit source, int spellId, bool crit, GameEventType type)
        {
            if (s == null || !s.Present || s.Dead || amount <= 0) return;
            amount = Absorb(s, amount);
            if (amount <= 0) return;
            s.Health -= amount;
            s.State.WoundCounter++;
            Emit(type, source != null ? source.Guid : 0, s.Guid, amount, spellId, crit, null);
            if (s.Casting != null && s.Summon == SummonKind.Pet) s.CastElapsed = Math.Max(0f, s.CastElapsed - 0.5f);
            if (s.Health <= 0)
            {
                s.Health = 0;
                s.State.Health = 0;
                WowLog.Info("OfflineSession: " + (s.Summon == SummonKind.Pet ? "the pet " : "") + s.State.Name + " was killed" + (source is OfflineCreature k ? " by " + k.State.Name : ""));
                RemoveSummon(s, true);
                return;
            }
            // A pet defends itself.
            if (s.Summon == SummonKind.Pet && source is OfflineCreature attacker && s.Casting == null && (s.CastTargetGuid == 0 || CreatureOf(s.CastTargetGuid) == null)) s.CastTargetGuid = attacker.Guid;
        }

        // ------------------------------------------------------------------ per step

        private void StepSummon(OfflineCreature s, float dt)
        {
            if (!s.Present) return;
            if (_player.Dead) { RemoveSummon(s, false); return; }
            StepAuras(s, dt);
            if (!s.Present || s.Dead) return;
            if (s.Summon == SummonKind.Totem) StepTotem(s, dt);
            else StepPet(s, dt);
            if (!s.Present) return;
            var st = s.State;
            st.Pos = s.Pos;
            st.Health = s.Health;
            st.MaxHealth = s.MaxHealth;
            st.Level = s.Level;
            st.PowerValue = s.Mana;
            st.PowerMax = s.MaxMana;
            st.CastingSpellId = s.Casting != null ? s.Casting.Id : 0;
            st.CastProgress = s.Casting != null && s.CastTotal > 0 ? Math.Min(1f, s.CastElapsed / s.CastTotal) : 0f;
            st.CastTargetGuid = s.Casting != null ? s.CastTargetGuid : 0;
        }

        private void StepTotem(OfflineCreature t, float dt)
        {
            if (t.SummonRemaining > 0f)
            {
                t.SummonRemaining -= dt;
                if (t.SummonRemaining <= 0f) { RemoveSummon(t, false); return; }
                var tv = t.TotemSlot >= 1 && t.TotemSlot <= 4 ? _player.Totems[t.TotemSlot - 1] : null;
                if (tv != null && tv.Guid == t.Guid) tv.Remaining = t.SummonRemaining;
            }
            if (Dist(t.Pos, _me.Pos) > 60f) { RemoveSummon(t, false); return; }   // left behind (release spirit, running far away)
            var sp = t.TotemSpell;
            if (sp == null) return;
            t.TotemTimer -= dt;
            if (t.TotemTimer > 0f && t.Casting == null) return;

            // Active totem (Searing Totem "Attack" 3606, a cast-time spell): attacks the nearest hostile creature in range.
            if (sp.CastTimeMs > 0 && sp.HasEffect(SpellEffect.SchoolDamage))
            {
                float range = sp.RangeMax > 0 ? sp.RangeMax : 20f;
                if (t.Casting == null)
                {
                    var victim = CreatureOf(t.CastTargetGuid);
                    if (!IsAttackable(victim) || Dist(t.Pos, victim.Pos) > range + victim.CombatReach) victim = NearestAttackable(t.Pos, range);
                    if (victim == null) { t.TotemTimer = 0.5f; t.CastTargetGuid = 0; return; }
                    StartUnitCast(t, sp, victim);
                    return;
                }
                StepUnitCast(t, dt);
                if (t.Casting == null) t.TotemTimer = 0f;
                return;
            }

            // Periodic trigger passive (Earthbind 6474 -> 3600 every 3 s, Stoneclaw 5728 -> 5729 every 2 s).
            var trigEff = sp.AuraOf(SpellAura.PeriodicTriggerSpell);
            if (trigEff != null && trigEff.TriggerSpell > 0)
            {
                t.TotemTimer = Math.Max(0.5f, trigEff.Amplitude / 1000f);
                var trig = _db.Find(trigEff.TriggerSpell);
                if (trig != null) PulseAround(t, trig);
                return;
            }

            // Passive area aura (Stoneskin 8072, Strength of Earth 8076): on the owner while in range.
            t.TotemTimer = 1f;
            float radius = 30f;
            for (int i = 0; i < 3; i++) if (sp.Effects[i] != null && sp.Effects[i].Radius > 0) { radius = sp.Effects[i].Radius; break; }
            bool inRange = !_player.Dead && Dist(t.Pos, _me.Pos) <= radius;
            OfflineAura have = null;
            for (int a = 0; a < _me.Auras.Count; a++) if (_me.Auras[a].Caster == t.Guid && _me.Auras[a].Spell.Id == sp.Id) { have = _me.Auras[a]; break; }
            if (inRange && have == null) ApplySpellAuras(sp, t, _me, 0);
            else if (!inRange && have != null) RemoveAuraAt(_me, _me.Auras.IndexOf(have));
        }

        /// <summary>A totem pulse: the triggered spell's area effects around the totem (slow, taunt/threat).</summary>
        private void PulseAround(OfflineCreature t, SpellInfo trig)
        {
            for (int i = 0; i < 3; i++)
            {
                var e = trig.Effects[i];
                if (e == null) continue;
                float radius = e.Radius > 0 ? e.Radius : 8f;
                for (int k = 0; k < _creatures.Count; k++)
                {
                    var c = _creatures[k];
                    if (!IsAttackable(c) || Dist(t.Pos, c.Pos) > radius + c.CombatReach) continue;
                    if (e.Effect == SpellEffect.Threat || e.Effect == SpellEffect.AttackMe)
                    {
                        // Stoneclaw: "taunts creatures within 8 yards to attack it" - only creatures already fighting.
                        if (!c.Engaged) continue;
                        c.ForcedTarget = t.Guid;
                        c.ForcedTimer = 2.5f;
                        AddThreat(c, t, CalcValue(trig, e, t.Level));
                    }
                    else if (e.IsAura && i == FirstAuraIndex(trig))
                    {
                        ApplySpellAuras(trig, t, c, 0);
                    }
                }
            }
        }

        private static int FirstAuraIndex(SpellInfo s) { for (int i = 0; i < 3; i++) if (s.Effects[i] != null && s.Effects[i].IsAura) return i; return -1; }

        private OfflineCreature NearestAttackable(WowPos from, float range)
        {
            OfflineCreature best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!IsAttackable(c)) continue;
                float d = Dist(from, c.Pos) - c.CombatReach;
                if (d <= range && d < bd) { bd = d; best = c; }
            }
            return best;
        }

        private void StepPet(OfflineCreature p, float dt)
        {
            // Mana regeneration (APPROXIMATED: 5% of max mana per second while not casting).
            if (p.MaxMana > 0 && p.Casting == null && p.Mana < p.MaxMana)
            {
                p.ManaFrac += p.MaxMana * 0.05f * dt;
                if (p.ManaFrac >= 1f) { int add = (int)p.ManaFrac; p.ManaFrac -= add; p.Mana = Math.Min(p.MaxMana, p.Mana + add); }
            }
            // Out-of-combat health regeneration like creatures.
            if (!_inCombat && p.Health < p.MaxHealth) p.Health = Math.Min(p.MaxHealth, p.Health + Math.Max(1, (int)(p.MaxHealth / 20f * dt + 0.5f)));
            if (Dist(p.Pos, _me.Pos) > 60f)
            {
                // Left far behind (graveyard, long run): the pet catches up at the owner's side like the client's pet follow.
                p.Pos = new WowPos(_me.Pos.X - 2f, _me.Pos.Y, Ground(_me.Pos.X - 2f, _me.Pos.Y, _me.Pos.Z), _me.Pos.O);
            }
            if (p.Incapacitated) { p.State.Moving = false; OffUpdatePetBar(p); return; }
            if (p.Casting != null) { StepUnitCast(p, dt); p.State.Moving = false; OffUpdatePetBar(p); return; }
            if (_petQueued != null && OffStepQueued(p, dt)) { OffUpdatePetBar(p); return; }   // iteration 11 (pets): a spell pressed on the bar

            var target = PetPickTarget(p);
            p.CastTargetGuid = target != null ? target.Guid : 0;
            p.State.TargetGuid = p.CastTargetGuid;
            p.State.InCombat = target != null;
            if (p.SwingTimer > 0f) p.SwingTimer -= dt;
            OffUpdatePetBar(p);
            if (target == null) { if (_petCmd == PetCommand.Stay) PetStay(p, dt); else PetFollow(p, dt); return; }

            // Casters (imp): the pet's damage spell from range; melee pets (voidwalker): chase and swing, Torment when not tanking.
            // Iteration 11 (pets): only while that spell's autocast is on (the pet bar's right click).
            SpellInfo bolt = null, taunt = null;
            foreach (var ps in p.PetSpells)
            {
                if (ps.HasEffect(SpellEffect.SchoolDamage) && ps.RangeMax > 5f && (bolt == null || ps.SpellLevel > bolt.SpellLevel)) bolt = ps;
                if (ps.HasEffect(SpellEffect.Threat) && (taunt == null || ps.SpellLevel > taunt.SpellLevel)) taunt = ps;
            }
            if (bolt != null && _petAutoOff.Contains(bolt.Id)) bolt = null;
            if (taunt != null && _petAutoOff.Contains(taunt.Id)) taunt = null;
            float d = Dist(p.Pos, target.Pos);
            FaceToward(p, target.Pos);
            if (bolt != null)
            {
                float range = bolt.RangeMax + target.CombatReach - 2f;
                if (d > range) { MoveToward(p, target.Pos, 7f, dt, true); return; }
                p.State.Moving = false;
                if (p.SwingTimer <= 0f && p.Mana >= CreatureCost(p, bolt)) { StartUnitCast(p, bolt, target); p.SwingTimer = Math.Max(1f, bolt.StartRecoveryTimeMs / 1000f); }
                return;
            }
            float melee = MeleeRange(p, target);
            if (d > melee) { var goal = target.Pos; MoveToward(p, goal, 7f, dt, true); return; }
            p.State.Moving = false;
            if (taunt != null && target.ForcedTarget != p.Guid && CooldownOk(p, taunt) && p.Mana >= CreatureCost(p, taunt))
            {
                StartUnitCast(p, taunt, target);
                return;
            }
            if (p.SwingTimer <= 0f)
            {
                p.SwingTimer = p.AttackSpeed;
                p.State.AttackCounter++;
                SummonSwing(p, target);
            }
        }

        private readonly Dictionary<ulong, double> _unitCooldowns = new Dictionary<ulong, double>();

        private bool CooldownOk(OfflineUnit u, SpellInfo s)
        {
            double until;
            ulong key = u.Guid ^ ((ulong)(uint)s.Id << 20);
            return !_unitCooldowns.TryGetValue(key, out until) || _time >= until;
        }

        private void SetUnitCooldown(OfflineUnit u, SpellInfo s)
        {
            float cd = Math.Max(s.RecoveryTimeMs, s.CategoryRecoveryTimeMs) / 1000f;
            if (cd <= 0f) return;
            _unitCooldowns[u.Guid ^ ((ulong)(uint)s.Id << 20)] = _time + cd;
        }

        /// <summary>Who the pet fights: its current target while it is still a valid fight, else the owner's target when the owner is fighting it, else whatever attacks the owner or the pet.</summary>
        private OfflineCreature PetPickTarget(OfflineCreature p)
        {
            // iteration 11 (pets): the Attack command first; Passive = only that; Aggressive adds hostile creatures within 20 yd (ours, APPROXIMATED)
            var ordered = CreatureOf(_petAttackGuid);
            if (_petAttackGuid != 0 && !IsAttackable(ordered)) { _petAttackGuid = 0; ordered = null; }
            if (ordered != null) return ordered;
            if (_petReact == PetReact.Passive) return null;
            var pick = PetPickDefensive(p);
            if (pick != null || _petReact != PetReact.Aggressive) return pick;
            OfflineCreature near = null;
            float nd = 20f;
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!IsAttackable(c) || c.State == null || c.State.Reaction != Reaction.Hostile) continue;
                float d = Dist(p.Pos, c.Pos);
                if (d < nd) { nd = d; near = c; }
            }
            return near;
        }

        /// <summary>Defensive (the iteration-3 rule): the current fight, the owner's engaged target, anything engaged within 40 yd.</summary>
        private OfflineCreature PetPickDefensive(OfflineCreature p)
        {
            var cur = CreatureOf(p.CastTargetGuid);
            if (IsAttackable(cur) && (cur.Engaged || cur == CreatureOf(_target) && (_autoAttack || _autoShot))) return cur;
            var t = CreatureOf(_target);
            if (IsAttackable(t) && (t.Engaged || _autoAttack || _autoShot || t.DamagedByPlayer)) return t;
            OfflineCreature best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!IsAttackable(c) || !c.Engaged) continue;
                float d = Dist(p.Pos, c.Pos);
                if (d < bd && d < 40f) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Command Stay without a fight: back to where it was told to stay (7 yd/s, ours) and stand.</summary>
        private void PetStay(OfflineCreature p, float dt)
        {
            float d = Dist2D(p.Pos, _petStayAt);
            if (d > 1.2f) MoveToward(p, _petStayAt, 7f, dt, true);
            else { p.State.Moving = false; p.State.Running = false; }
        }

        private void PetFollow(OfflineCreature p, float dt)
        {
            float ang = _me.Pos.O + 2.3f;
            var goal = new WowPos(_me.Pos.X + (float)Math.Cos(ang) * 2.5f, _me.Pos.Y + (float)Math.Sin(ang) * 2.5f, _me.Pos.Z, 0f);
            float d = Dist2D(p.Pos, goal);
            if (d > 1.2f) MoveToward(p, goal, d > 6f ? 7f : 3.5f, dt, d > 6f);
            else { p.State.Moving = false; p.State.Running = false; p.Pos.O = _me.Pos.O; }
        }

        /// <summary>A melee pet's swing (pet_levelstats min/max damage, armor of the target, 5% miss, 5% crit, APPROXIMATED table).</summary>
        private void SummonSwing(OfflineCreature p, OfflineCreature target)
        {
            float r = Rand01() * 100f;
            if (target.Ai == AiState.Evade) { Emit(GameEventType.SpellMiss, p.Guid, target.Guid, 0, 0, false, "Evade"); return; }
            if (r < 5f) { Emit(GameEventType.MeleeMiss, p.Guid, target.Guid, 0, 0, false, "Miss"); Engage(target, p); return; }
            if (r < 10f && !target.Incapacitated) { Emit(GameEventType.MeleeDodge, p.Guid, target.Guid, 0, 0, false, "Dodge"); Engage(target, p); return; }
            bool crit = r > 95f;
            float dmg = RandRange(p.MinDamage, p.MaxDamage) * WowFormulas.ArmorMultiplier(target.Armor, p.Level) * CommandBonus();
            if (crit) dmg *= 2f;
            DamageCreature(target, Math.Max(1, (int)dmg), 0, crit, GameEventType.MeleeHit, true, p);
        }

        /// <summary>Orc "Command" (20575/54562: ADD_PCT_MODIFIER misc 0 = damage, +5%): applied to all pet damage offline.</summary>
        private float CommandBonus()
        {
            float m = 1f;
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                if (!au.Spell.IsPassive || !_racialIds.Contains(au.Spell.Id)) continue;
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e != null && e.IsAura && e.Aura == SpellAura.AddPctModifier && e.MiscValue == 0) m *= 1f + (e.BasePoints + 1) / 100f;   // SPELLMOD_DAMAGE
                }
            }
            return m;
        }

        private static int CreatureCost(OfflineUnit u, SpellInfo s)
        {
            if (s.PowerType != 0) return 0;
            if (s.ManaCostPercentage > 0) return Math.Max(1, u.MaxMana * s.ManaCostPercentage / 100);
            return s.ManaCost;
        }
    }
}

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    // ---- iteration 11 - owner pets (proposal-pets.md s7): the pet bar, pet commands, totems offline. Offline content has warlock pets and
    // shaman totems only - hunter pets, stables, companions, vehicles, possession and charm answer "Not available offline." (ours).
    public sealed partial class OfflineSession
    {
        private void OffUnavailable(string what)
        {
            EmitInfo("Not available offline.");   // ours
            if (_offlineNotYetLogged.Add("pets:" + what)) WowLog.Info("Offline: " + what + " - not available offline (offline content has no hunter pets, stables, companions or vehicles).");
        }

        /// <summary>The pet bar of a new pet: CharmInfo.cpp:52-65 InitPetActionBar - 0 Attack, 1 Follow, 2 Stay, 3..6 the pet's spells (SpellLevel
        /// order; a 5th+ only in Spells - ours), 7 Aggressive, 8 Defensive, 9 Passive.</summary>
        private void OffBuildPetBar(OfflineCreature c, int family)
        {
            _petCmd = PetCommand.Follow;
            _petReact = PetReact.Defensive;
            _petAttackGuid = 0;
            _petAutoOff.Clear();
            _petQueued = null;
            SpellInfo bolt = null, taunt = null;
            foreach (var ps in c.PetSpells)
            {
                if (ps.HasEffect(SpellEffect.SchoolDamage) && ps.RangeMax > 5f && (bolt == null || ps.SpellLevel > bolt.SpellLevel)) bolt = ps;
                if (ps.HasEffect(SpellEffect.Threat) && (taunt == null || ps.SpellLevel > taunt.SpellLevel)) taunt = ps;
            }
            _petBoltId = bolt != null ? bolt.Id : 0;
            _petTauntId = taunt != null ? taunt.Id : 0;
            var spells = new List<SpellInfo>(c.PetSpells);
            spells.Sort((a, b) => a.SpellLevel != b.SpellLevel ? a.SpellLevel.CompareTo(b.SpellLevel) : a.Id.CompareTo(b.Id));
            var v = new ControlBarView { Kind = ControlBarKind.Pet, UnitGuid = c.Guid, Family = family, Command = PetCommand.Follow, React = PetReact.Defensive };
            v.Slots[0] = ControlBarBuilder.CommandSlot(PetCommand.Attack);
            v.Slots[1] = ControlBarBuilder.CommandSlot(PetCommand.Follow);
            v.Slots[2] = ControlBarBuilder.CommandSlot(PetCommand.Stay);
            for (int i = 3; i <= 6; i++) v.Slots[i] = i - 3 < spells.Count ? OffPetSlot(spells[i - 3], c.Level) : ControlBarBuilder.Empty();
            v.Slots[7] = ControlBarBuilder.ReactSlot(PetReact.Aggressive);
            v.Slots[8] = ControlBarBuilder.ReactSlot(PetReact.Defensive);
            v.Slots[9] = ControlBarBuilder.ReactSlot(PetReact.Passive);
            foreach (var s in spells) v.Spells.Add(OffPetSlot(s, c.Level));
            v.Counter = (_offBar != null ? _offBar.Counter : 0) + 1;
            ControlBarBuilder.MarkActive(v);
            _offBar = v;
            _player.ControlBar = v;
            _offPet = new PetInfoView { Guid = c.Guid, Name = c.State.Name, Family = family, FamilyName = FamilyName(family, c.State.Name), FamilyIcon = PetFamilyDb.Icon(_files, family) };
            _player.PetInfo = _offPet;
            if (!_petRulesLogged)
            {
                _petRulesLogged = true;
                WowLog.Info("Offline pets: ours - autocast starts ON (AzerothCore starts new pet spells off, Pet.cpp:1810-1816 - decision U6); Stay returns at 7 yd/s; " +
                            "Aggressive = Defensive + hostile creatures within 20 yd (APPROXIMATED); a bar spell out of range makes the pet run into range first");
            }
        }

        private ControlSlotView OffPetSlot(SpellInfo s, int level)
        {
            bool auto = s.Id == _petBoltId || s.Id == _petTauntId;
            var v = ControlBarBuilder.SpellSlot(_db, s.Id, auto ? ControlBarBuilder.ActEnabled : ControlBarBuilder.ActPassive, level);
            if (auto) v.Autocast = !_petAutoOff.Contains(s.Id);
            return v;
        }

        private void OffClearPetBar()
        {
            if (_offBar != null && ReferenceEquals(_player.ControlBar, _offBar)) _player.ControlBar = null;
            if (_offPet != null && ReferenceEquals(_player.PetInfo, _offPet)) _player.PetInfo = null;
            _offBar = null;
            _offPet = null;
            _petQueued = null;
            _petAttackGuid = 0;
        }

        /// <summary>Each pet step: the lit command / reaction / casting spell, cooldowns, mana.</summary>
        private void OffUpdatePetBar(OfflineCreature p)
        {
            var v = _offBar;
            if (v == null) return;
            v.Command = _petCmd;
            v.React = _petReact;
            ControlBarBuilder.MarkActive(v);
            for (int i = 0; i < 10; i++)
            {
                var s = v.Slots[i];
                if (s.Kind == ControlSlotKind.Command && s.Action == (int)PetCommand.Attack) s.Active = p.CastTargetGuid != 0;
                if (s.Kind == ControlSlotKind.Spell) OffSlotState(p, s);
            }
            for (int i = 0; i < v.Spells.Count; i++) OffSlotState(p, v.Spells[i]);
        }

        private void OffSlotState(OfflineCreature p, ControlSlotView s)
        {
            var sp = _db.Find(s.Action);
            s.Active = p.Casting != null && p.Casting.Id == s.Action;
            double until;
            ulong key = p.Guid ^ ((ulong)(uint)s.Action << 20);
            s.CooldownRemaining = _unitCooldowns.TryGetValue(key, out until) && until > _time ? (float)(until - _time) : 0f;
            s.CooldownTotal = sp != null ? Math.Max(sp.RecoveryTimeMs, sp.CategoryRecoveryTimeMs) / 1000f : 0f;
            bool mana = sp == null || p.Mana >= CreatureCost(p, sp);
            s.Usable = mana && !s.Passive;
            s.UnusableReason = mana ? null : "Not enough mana";                       // ERR_OUT_OF_MANA (GlobalStrings.lua:3338)
        }

        /// <summary>A bar spell waiting for range: the pet runs to the target, then casts. True while it handles the step.</summary>
        private bool OffStepQueued(OfflineCreature p, float dt)
        {
            var s = _petQueued;
            OfflineUnit t = _petQueuedTarget == p.Guid ? (OfflineUnit)p : _petQueuedTarget == PlayerGuid ? (OfflineUnit)_me : CreatureOf(_petQueuedTarget);
            var tc = t as OfflineCreature;
            if (t == null || (tc != null && tc != p && (tc.Dead || !tc.Present))) { _petQueued = null; return false; }
            float range = s.RangeMax > 0f ? s.RangeMax : (tc != null ? MeleeRange(p, tc) : 5f);
            if (t != p && t != _me && Dist(p.Pos, t.Pos) > range - 1f)
            {
                FaceToward(p, t.Pos);
                MoveToward(p, t.Pos, 7f, dt, true);
                return true;
            }
            _petQueued = null;
            p.State.Moving = false;
            if (t != p) FaceToward(p, t.Pos);
            StartUnitCast(p, s, t);
            return true;
        }

        public void UseControlSlot(int slot, ulong targetGuid)
        {
            var v = _offBar;
            if (v == null || _pet == null || slot < 0 || slot > 9) { EmitError("You don't have a pet!"); return; }   // ERR_NO_PET (GlobalStrings.lua:3327)
            var s = v.Slots[slot];
            if (s.Kind == ControlSlotKind.Command) { CommandPet((PetCommand)s.Action, targetGuid); return; }
            if (s.Kind == ControlSlotKind.Reaction) { SetPetReact((PetReact)s.Action); return; }
            if (s.Kind == ControlSlotKind.Spell) OffPetCast(s.Action, targetGuid);
        }

        public void UseControlSlotAt(int slot, WowPos dest) { OffUnavailable("pet place spells"); }   // offline pets have no place spells

        public void UsePetSpell(int spellId, ulong targetGuid)
        {
            if (_offBar == null || _pet == null) { EmitError("You don't have a pet!"); return; }
            if (ControlBarBuilder.SlotOf(_offBar, spellId) < 0 && ControlBarBuilder.BookEntry(_offBar, spellId) == null) { EmitError("Spell not learned"); return; }   // SPELL_FAILED_NOT_KNOWN (7103)
            OffPetCast(spellId, targetGuid);
        }

        private void OffPetCast(int spellId, ulong targetGuid)
        {
            var p = _pet;
            var s = _db.Find(spellId);
            if (p == null || s == null || s.IsPassive) return;
            if (p.Dead || !p.Present) { EmitError("Your pet is dead."); return; }                                  // ERR_PET_SPELL_DEAD (3376)
            if (!CooldownOk(p, s)) { EmitError("Spell is not ready yet."); return; }                                 // ERR_SPELL_COOLDOWN (3493)
            if (p.Mana < CreatureCost(p, s)) { EmitError("Not enough mana"); return; }                               // ERR_OUT_OF_MANA (3338)
            ulong t;
            bool harmful = s.IsHarmful;
            if (harmful)
            {
                t = targetGuid != 0 ? targetGuid : _target;
                var c = CreatureOf(t);
                if (t == 0 || c == null) { EmitError("You have no target."); return; }                               // ERR_GENERIC_NO_TARGET (3064)
                if (!IsAttackable(c)) { EmitError("You cannot attack that target."); return; }                        // ERR_INVALID_ATTACK_TARGET (3136)
                p.CastTargetGuid = c.Guid;
            }
            else t = p.Guid;
            _petQueued = s;
            _petQueuedTarget = t;
            WowLog.Info("Offline: pet spell " + s.Id + " '" + s.Name + "' ordered" + (harmful ? " at 0x" + t.ToString("X") : ""));
        }

        public void CommandPet(PetCommand command, ulong targetGuid)
        {
            var p = _pet;
            if (p == null || !p.Present) { EmitError("You don't have a pet!"); return; }
            switch (command)
            {
                case PetCommand.Stay:
                    _petCmd = PetCommand.Stay; _petStayAt = p.Pos; _petAttackGuid = 0; _petQueued = null;
                    break;
                case PetCommand.Follow:
                    _petCmd = PetCommand.Follow; _petAttackGuid = 0; _petQueued = null; p.CastTargetGuid = 0;
                    break;
                case PetCommand.Attack:
                    ulong t = targetGuid != 0 ? targetGuid : _target;
                    var c = CreatureOf(t);
                    if (t == 0) { EmitError("You have no target."); return; }
                    if (!IsAttackable(c)) { EmitError("You cannot attack that target."); return; }
                    _petAttackGuid = c.Guid;
                    p.CastTargetGuid = c.Guid;
                    break;
                case PetCommand.Dismiss:
                    DismissPet(true);
                    return;
            }
            if (_offBar != null) { _offBar.Command = _petCmd; ControlBarBuilder.MarkActive(_offBar); }
            WowLog.Info("Offline: pet command " + command);
        }

        public void SetPetReact(PetReact react)
        {
            if (_pet == null) { EmitError("You don't have a pet!"); return; }
            _petReact = react;
            if (react == PetReact.Passive && _petAttackGuid == 0) _pet.CastTargetGuid = 0;
            if (_offBar != null) { _offBar.React = react; ControlBarBuilder.MarkActive(_offBar); }
            WowLog.Info("Offline: pet reaction " + react);
        }

        public void SetPetAutocast(int spellId, bool on)
        {
            if (_offBar == null) return;
            int slot = ControlBarBuilder.SlotOf(_offBar, spellId);
            var book = ControlBarBuilder.BookEntry(_offBar, spellId);
            var s = slot >= 0 ? _offBar.Slots[slot] : book;
            if (s == null || !s.Autocastable) return;
            if (on) _petAutoOff.Remove(spellId); else _petAutoOff.Add(spellId);
            if (slot >= 0) _offBar.Slots[slot].Autocast = on;
            if (book != null) book.Autocast = on;
            WowLog.Info("Offline: pet autocast " + spellId + " " + (on ? "on" : "off"));
        }

        public void SetPetBarSlot(int slot, int spellId)
        {
            var v = _offBar;
            if (v == null || slot < 3 || slot > 6) return;
            if (spellId == 0) { v.Slots[slot] = ControlBarBuilder.Empty(); return; }
            var book = ControlBarBuilder.BookEntry(v, spellId);
            if (book == null) { EmitError("Spell not learned"); return; }
            int from = ControlBarBuilder.SlotOf(v, spellId);
            var old = v.Slots[slot];
            var sp = _db.Find(spellId);
            v.Slots[slot] = sp != null && _pet != null ? OffPetSlot(sp, _pet.Level) : book;
            if (from >= 0 && from != slot) v.Slots[from] = old ?? ControlBarBuilder.Empty();   // the slot's old spell goes where the moved one was
        }

        public void RenamePet(string name) { OffUnavailable("pet rename"); }
        public void AbandonPet() { OffUnavailable("abandon pet"); }
        public void DismissPet() { if (_pet == null) { EmitError("You don't have a pet!"); return; } DismissPet(true); }

        public void DestroyTotem(int slot)
        {
            if (slot < 0 || slot > 3) return;
            var t = _totems[slot + 1];                                                  // SummonProperties slot = element + 1 (SummonSlot)
            if (t != null) RemoveSummon(t, false);
        }

        public void SetTotemBarPage(int page) { if (page >= 1 && page <= 3) _player.TotemBarPage = page; }
        public void DismissCompanion() { OffUnavailable("companions"); }
        public bool LeaveVehicle() { OffUnavailable("vehicles"); return false; }
        public void ChangeSeat(int seat) { OffUnavailable("vehicles"); }
        public void SetVehicleAim(float orientation, float pitch) { }
        public void StablePet() { OffUnavailable("stable"); }
        public void UnstablePet(int petNumber) { OffUnavailable("stable"); }
        public void BuyStableSlot() { OffUnavailable("stable"); }
        public void CloseStable() { _player.Stable = null; }
    }
}
