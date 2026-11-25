using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    /// <summary>
    /// Iteration 3: difficulty and auto loot settings, racial abilities, channels, seals and Judgement, weapon imbues, procs on being
    /// struck (Lightning Shield, Frost Armor), conjured items, dispels, crowd control movement (fear, polymorph), aura events.
    /// </summary>
    public sealed partial class OfflineSession
    {
        // ------------------------------------------------------------------ settings (WowSettings -> Front -> session)

        /// <summary>Creature melee/spell damage per difficulty (0 easy, 1 normal = today's numbers, 2 hard).</summary>
        public static readonly float[] DifficultyDamage = { 0.7f, 1f, 1.3f };
        /// <summary>Creature health per difficulty (0 easy, 1 normal = today's numbers, 2 hard).</summary>
        public static readonly float[] DifficultyHealth = { 0.85f, 1f, 1.2f };

        private int _difficulty = 1;
        private bool _autoLoot;
        private bool _inAutoLoot;

        /// <summary>0 easy, 1 normal, 2 hard (PlayerState.Difficulty mirrors it).</summary>
        public int Difficulty { get { return _difficulty; } }

        /// <summary>
        /// 0 easy (~0.7x creature damage, ~0.85x health), 1 normal (today's tuning), 2 hard (~1.3x / 1.2x). Living creatures are rescaled at
        /// once (keeping their health fraction); respawns use the new values.
        /// </summary>
        public void SetDifficulty(int difficulty)
        {
            int d = Math.Max(0, Math.Min(2, difficulty));
            _player.Difficulty = d;
            if (d == _difficulty) return;
            float hpRatio = DifficultyHealth[d] / DifficultyHealth[_difficulty];
            float dmgRatio = DifficultyDamage[d] / DifficultyDamage[_difficulty];
            _difficulty = d;
            foreach (var c in _creatures)
            {
                if (c.Summon != SummonKind.None || c.Tpl.IsNpc) continue;
                double frac = c.MaxHealth > 0 ? (double)c.Health / c.MaxHealth : 1.0;
                c.MaxHealth = Math.Max(1, (int)Math.Round(c.MaxHealth * hpRatio));
                if (!c.Dead) c.Health = Math.Max(1, Math.Min(c.MaxHealth, (int)Math.Round(c.MaxHealth * frac)));
                c.DamageMod *= dmgRatio;
                c.MinDamage *= dmgRatio;
                c.MaxDamage *= dmgRatio;
                c.State.MaxHealth = c.MaxHealth;
                c.State.Health = c.Health;
            }
            WowLog.Info("OfflineSession: difficulty " + (d == 0 ? "easy" : d == 1 ? "normal" : "hard") + " (creature damage x" + DifficultyDamage[d] + ", health x" + DifficultyHealth[d] + ")");
        }

        /// <summary>Loot everything as soon as a corpse is opened (PlayerState.AutoLoot mirrors it).</summary>
        public bool AutoLoot
        {
            get { return _autoLoot; }
            set { _autoLoot = value; _player.AutoLoot = value; }
        }

        // ------------------------------------------------------------------ per step extras

        private bool _petRestored;
        private readonly HashSet<int> _racialIds = new HashSet<int>();

        private void StepPlayerExtras(float dt)
        {
            GsTick(dt);                                         // iteration 11 (spellfx): ground areas outlive their caster (before the dead return); aim end rules; aura states
            if (!_petRestored) { _petRestored = true; RestorePet(); }
            if (_player.Dead) return;
            // AURA_INTERRUPT_FLAG_MOVE (0x8): Shadowmeld, Cannibalize ... end when the avatar moves.
            if (_moving)
            {
                for (int a = _me.Auras.Count - 1; a >= 0; a--)
                {
                    if (a >= _me.Auras.Count) continue;
                    var au = _me.Auras[a];
                    if ((au.Spell.AuraInterruptFlags & 0x8) != 0 && !au.FromForm && !au.Spell.IsPassive && (_channel == null || au.Spell != _channel)) RemoveAuraAt(_me, a);
                }
            }
            StepChannel(dt);
            if (_imbueRemaining > 0f)
            {
                _imbueRemaining -= dt;
                if (_imbueRemaining <= 0f || _rec.Equipment[(int)EquipSlot.MainHand] != _imbueItem) EndImbue();
            }
            if (_seaIcd > 0f) _seaIcd -= dt;
        }

        /// <summary>Sets the iteration-3 PlayerState fields (called from UpdatePlayerView).</summary>
        private void UpdatePlayerView3(PlayerState p)
        {
            p.Race = _rec.Race;
            p.Faction = WowRaces.FactionOf(_rec.Race);
            p.Difficulty = _difficulty;
            p.AutoLoot = _autoLoot;
            p.PetGuid = _pet != null && _pet.Present ? _pet.Guid : 0;
            p.WeaponImbueSpellId = _imbueSpell != null ? _imbueSpell.Id : 0;
            p.WeaponImbueRemaining = _imbueSpell != null ? Math.Max(0f, _imbueRemaining) : 0f;
            // iteration 11 (spellfx): the imbue as the main hand's temporary enchant (PLAYER_VISIBLE_ITEM_n_ENCHANTMENT layout: perm | temp << 16)
            for (int k = 0; k < p.EquipEnchants.Length; k++) p.EquipEnchants[k] = 0;
            if (_imbueSpell != null && _imbueEnchant > 0) p.EquipEnchants[(int)EquipSlot.MainHand] = _imbueEnchant << 16;
            if (_channel != null)
            {
                p.CastingSpellId = _channel.Id;
                p.CastElapsed = Math.Min(_channelElapsed, _channelTotal);
                p.CastTotal = _channelTotal;
                p.Channeling = true;
                p.CastTargetGuid = _channelTarget;
            }
            else
            {
                p.Channeling = false;
                p.CastTargetGuid = _casting != null ? _castTargetGuid : 0;
            }
        }

        // ------------------------------------------------------------------ races

        /// <summary>Learns the race's racial abilities and applies its passives (hidden permanent auras). Called from InitPlayer.</summary>
        private void LearnRacials()
        {
            _racialIds.Clear();
            if (!_db.Available) return;
            foreach (int id in _db.RacialSpells(_rec.Race, _rec.Class))
            {
                var s = _db.Find(id);
                if (s == null) continue;
                _racialIds.Add(id);
                if (!_rec.Spells.Contains(id)) _rec.Spells.Add(id);
                if (s.IsPassive) ApplyPassive(s);
            }
        }

        /// <summary>A passive spell as a permanent hidden aura on the player (its stat/regen/resistance/dodge modifiers feed RecalcStats).</summary>
        private void ApplyPassive(SpellInfo s)
        {
            if (_me.FindAura(s.Id) != null) return;
            var au = new OfflineAura { Spell = s, Caster = PlayerGuid, Hidden = true, Remaining = -1f };
            au.View = new AuraView { SpellId = s.Id, Name = s.Name, IconPath = s.IconPath, CasterGuid = PlayerGuid };
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || !e.IsAura) { au.Active[i] = false; continue; }
                au.Amount[i] = CalcValue(s, e, _player.Level);
            }
            _me.Auras.Add(au);
            _statsDirty = true;
        }

        /// <summary>True for racial abilities (read from SkillLineAbility of the race's racial skill line).</summary>
        public bool IsRacial(int spellId) { return _racialIds.Contains(spellId); }

        // ------------------------------------------------------------------ pets on login

        /// <summary>A Warlock logs in with the pet that was out when the character was saved (like the server's pet on login).</summary>
        private void RestorePet()
        {
            if (_rec.PetEntry <= 0 || _player.Dead || _pet != null) return;
            foreach (int id in _rec.Spells)
            {
                var s = _db.Find(id);
                var e = s != null ? s.EffectOf(SpellEffect.SummonPet) : null;
                if (e != null && e.MiscValue == _rec.PetEntry) { SummonPet(s, e.MiscValue); return; }
            }
            _rec.PetEntry = 0;
        }

        /// <summary>The pet grows with its owner: on level up it is summoned again at the new level (same entry, health fraction kept).</summary>
        private void OnPlayerLevelUp3()
        {
            if (_pet == null || !_pet.Present || _pet.Dead) return;
            var spell = _pet.SummonSpell;
            int entry = _pet.Tpl.Entry;
            float frac = _pet.MaxHealth > 0 ? (float)_pet.Health / _pet.MaxHealth : 1f;
            var pos = _pet.Pos;
            if (spell == null || !SummonPet(spell, entry) || _pet == null) return;
            _pet.Pos = pos;
            _pet.Health = Math.Max(1, (int)(_pet.MaxHealth * frac));
        }

        // ------------------------------------------------------------------ channels

        private SpellInfo _channel;
        private ulong _channelTarget;
        private float _channelElapsed, _channelTotal;
        private WowPos _channelStartPos;

        /// <summary>A channeled spell (Arcane Missiles, Cannibalize): runs for its duration, ticks through its periodic aura, ends on moving.</summary>
        private void StartChannel(SpellInfo s, OfflineUnit target)
        {
            int ms = s.DurationAt(_player.Level);
            if (ms <= 0) return;
            _channel = s;
            _channelTarget = target != null ? target.Guid : PlayerGuid;
            _channelElapsed = 0f;
            _channelTotal = ms / 1000f;
            _channelStartPos = _me.Pos;
            _channelHasDest = _gsDestSpell == s.Id;              // iteration 11 (spellfx): a channel aimed at a place ticks there
            _channelDest = _gsDestPos;
            Emit(GameEventType.ChannelStarted, PlayerGuid, _channelTarget, ms, s.Id, false, s.Name);
        }

        private void StepChannel(float dt)
        {
            if (_channel == null) return;
            if (_me.Incapacitated) { EndChannel("Interrupted"); return; }
            if (Dist(_me.Pos, _channelStartPos) > CastMoveTolerance) { EndChannel("Interrupted"); EmitError("Interrupted"); return; }
            var t = UnitOf(_channelTarget);
            if (t == null || t.Dead) { EndChannel("Invalid target"); return; }
            _channelElapsed += dt;
            if (_channelElapsed >= _channelTotal + 0.05f) EndChannel(null);
        }

        /// <summary>Ends the current channel (reason null = ran its course): its auras on the caster and the target end with it.</summary>
        private void EndChannel(string reason)
        {
            var s = _channel;
            if (s == null) return;
            ulong target = _channelTarget;
            _channel = null;
            _channelElapsed = _channelTotal = 0f;
            for (int a = _me.Auras.Count - 1; a >= 0; a--) if (a < _me.Auras.Count && _me.Auras[a].Spell == s) RemoveAuraAt(_me, a);
            var t = UnitOf(target);
            if (t != null && t != _me) for (int a = t.Auras.Count - 1; a >= 0; a--) if (a < t.Auras.Count && t.Auras[a].Spell == s && t.Auras[a].Caster == PlayerGuid) RemoveAuraAt(t, a);
            GsEndChannelAreas(s);                               // iteration 11 (spellfx): its ground areas end with it
            Emit(GameEventType.ChannelEnded, PlayerGuid, target, 0, s.Id, false, reason);
        }

        /// <summary>PERIODIC_TRIGGER_SPELL tick: the triggered spell (Arcane Missile 7268) flies from the aura's caster to the channel target.</summary>
        private void TickTriggerSpell(OfflineAura au, SpellEffectInfo e, OfflineUnit u)
        {
            var trig = _db.Find(e.TriggerSpell);
            if (trig == null) return;
            var caster = UnitOf(au.Caster);
            if (caster == null || caster.Dead) return;
            if (caster == _me && _channel != null && au.Spell == _channel && GsTickAtPlace(trig)) return;   // iteration 11 (spellfx): Blizzard / Rain of Fire / Volley / Hurricane ticks at the place
            bool harmful = trig.IsHarmful || HasEnemyTargetEffect(trig) || (trig.Effects[0] != null && trig.Effects[0].TargetA == 77);
            OfflineUnit target = u;
            if (caster == _me && u == _me && _channel != null && au.Spell == _channel) { target = UnitOf(_channelTarget); if (target == null || target.Dead || target == _me) return; }
            else if (harmful && u == caster)
            {
                // A channel on the caster aims at the channel target (or the caster's current target).
                target = caster == _me ? (UnitOf(_channel != null ? _channelTarget : _target)) : UnitOf(caster.CastTargetGuid);
                if (target == null || target.Dead || target == caster) return;
            }
            if (caster == _me)
            {
                Emit(GameEventType.CastSucceeded, PlayerGuid, target.Guid, 0, trig.Id, false, trig.Name);
                LaunchOrHit(trig, _me, target, 0);
            }
            else ExecuteUnitSpell(caster, trig, target);
        }

        // ------------------------------------------------------------------ Paladin: seals and Judgement

        /// <summary>
        /// Seal of Righteousness (21084, learned at 1; 20154 is the same seal's older id): its SPELL_AURA_DUMMY is scripted on the server.
        /// Per melee hit the seal adds MWS * (0.022 * AP + 0.044 * holy spell power) Holy damage (the formula in its own Spell.dbc tooltip).
        /// </summary>
        private static readonly int[] SealOfRighteousness = { 21084, 20154 };
        private const int SealOfRighteousnessProc = 25742;    // the damage spell the server casts per hit (measured in Spell.dbc)
        private const int JudgementOfRighteousness = 20187;   // "${1+0.2*$AP+0.32*$SPH}" Holy damage when Judgement unleashes the seal
        private const int JudgementOfLightDebuff = 20185;

        private OfflineAura ActiveSeal()
        {
            foreach (int id in SealOfRighteousness) { var a = _me.FindAura(id); if (a != null) return a; }
            return null;
        }

        /// <summary>Judgement of Light (20271) and the other judgements: SCRIPT_EFFECT on the target (family 10).</summary>
        private static bool IsJudgement(SpellInfo s) { return s.SpellFamilyName == 10 && s.HasEffect(SpellEffect.ScriptEffect) && (s.Id == 20271 || s.Id == 53407 || s.Id == 53408); }

        /// <summary>Judgement: unleashes the active seal (Judgement of Righteousness damage) and puts the judgement's debuff on the target.</summary>
        private void DoJudgement(SpellInfo s, OfflineCreature t)
        {
            if (t == null || t.Dead) return;
            var seal = ActiveSeal();
            if (seal != null)
            {
                int dmg = Math.Max(1, (int)(1f + 0.2f * _ap));
                var jor = _db.Find(JudgementOfRighteousness);
                bool crit = Rand01() * 100f < _crit;
                if (crit) dmg *= 2;
                DamageCreature(t, dmg, jor != null ? jor.Id : s.Id, crit, GameEventType.SpellDamage, true);
            }
            var debuff = s.Id == 20271 ? _db.Find(JudgementOfLightDebuff) : null;
            if (debuff != null && !t.Dead) ApplySpellAuras(debuff, _me, t, 0);
        }

        /// <summary>Procs of the player's own melee hit: the seal's Holy damage, the weapon imbue, Judgement of Light on the target.</summary>
        private void OnPlayerMeleeHit(OfflineCreature t, bool offHand)
        {
            if (t == null || t.Dead) return;
            if (!offHand && ActiveSeal() != null)
            {
                float mws = _hasMelee ? _mhSpeed : 2f;
                int dmg = Math.Max(1, (int)(mws * (0.022f * _ap)));
                int id = _db.Find(SealOfRighteousnessProc) != null ? SealOfRighteousnessProc : ActiveSeal().Spell.Id;
                DamageCreature(t, dmg, id, false, GameEventType.SpellDamage, true);
                if (t.Dead) return;
            }
            if (!offHand && _imbueSpell != null && _imbueProcSpell != null)
            {
                // Flametongue: its passive 10400 holds the fire value in a DUMMY aura (bp 325 + 19/level); per hit the tooltip gives
                // "$/77;8026m1 to $/25;8026M1" by weapon speed = value * speed / 100 Fire damage, shown as 8026 "Flametongue Weapon Proc".
                if (_imbueProcSpell.HasEffect(SpellEffect.SchoolDamage)) ExecuteTriggered(_imbueProcSpell, t);
                else
                {
                    var de = _imbueProcSpell.AuraOf(SpellAura.Dummy);
                    if (de != null)
                    {
                        int fire = Math.Max(1, (int)(CalcValue(_imbueProcSpell, de, _player.Level) * (_hasMelee ? _mhSpeed : 2f) / 100f));
                        DamageCreature(t, fire, _db.Find(FlametongueProc) != null ? FlametongueProc : _imbueSpell.Id, false, GameEventType.SpellDamage, true);
                    }
                }
                if (t.Dead) return;
            }
            // Judgement of Light (20185, a DUMMY scripted on the server): hits on the judged target heal the attacker for s1% of max health (spell 20267).
            var jol = t.FindAura(JudgementOfLightDebuff);
            if (jol != null && jol.Caster == PlayerGuid && _me.Health < _me.MaxHealth)
                Heal(_me, Math.Max(1, _me.MaxHealth * Math.Max(1, jol.Amount[0]) / 100), _db.Find(20267) != null ? 20267 : JudgementOfLightDebuff, false, GameEventType.SpellHeal);
            // Debuffs on the target that proc for the attacker (Judgement of Light: heals the attacker).
            for (int a = 0; a < t.Auras.Count; a++)
            {
                var au = t.Auras[a];
                if (au.Caster != PlayerGuid) continue;
                SpellEffectInfo pe;
                if (!au.Has(SpellAura.ProcTriggerSpell, out pe) || pe.TriggerSpell <= 0) continue;
                int chance = au.Spell.ProcChance > 0 && au.Spell.ProcChance <= 100 ? au.Spell.ProcChance : 100;
                if (Rand01() * 100f >= chance) continue;
                var trig = _db.Find(pe.TriggerSpell);
                if (trig != null) ExecuteTriggered(trig, _me);
            }
        }

        // ------------------------------------------------------------------ procs when the player is struck

        private float _seaIcd;   // Lightning Shield: one ball every few seconds (its tooltip) - 3 s, APPROXIMATED

        /// <summary>
        /// The player was hit in melee by a creature: PROC_TRIGGER_SPELL auras whose ProcFlags include "taken melee" (0x8 auto attack,
        /// 0x20 melee ability) fire their trigger spell at the attacker (Lightning Shield 324 -> 26545, Frost Armor 168 -> 6136 Chilled),
        /// spending a charge when the spell has charges.
        /// </summary>
        private void OnPlayerStruck(OfflineCreature attacker)
        {
            if (attacker == null || attacker.Dead) return;
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= _me.Auras.Count) continue;
                var au = _me.Auras[a];
                SpellEffectInfo pe;
                if (!au.Has(SpellAura.ProcTriggerSpell, out pe) || pe.TriggerSpell <= 0) continue;
                if (au.Spell.ProcFlags != 0 && (au.Spell.ProcFlags & (0x8 | 0x20)) == 0) continue;
                bool charged = au.Spell.ProcCharges > 0;
                if (charged && _seaIcd > 0f) continue;
                int chance = au.Spell.ProcChance > 0 && au.Spell.ProcChance <= 100 ? au.Spell.ProcChance : 100;
                if (Rand01() * 100f >= chance) continue;
                var trig = _db.Find(pe.TriggerSpell);
                if (trig == null && au.Spell.SpellFamilyName == 11) trig = _db.Find(LightningShieldDamage);   // 324 names 26545, absent from the 3.3.5 client; the server casts 26364
                if (trig == null) continue;
                if (trig.HasEffect(SpellEffect.SchoolDamage) && trig.EffectOf(SpellEffect.SchoolDamage).BasePoints <= 0 && au.Amount[pe.Index] > 0)
                    DamageCreature(attacker, au.Amount[pe.Index], trig.Id, false, GameEventType.SpellDamage, false);   // Lightning Shield: the aura holds the damage
                else ExecuteTriggered(trig, attacker);
                if (charged)
                {
                    _seaIcd = 3f;
                    if (au.Charges < 0) au.Charges = au.Spell.ProcCharges;
                    au.Charges--;
                    au.View.Stacks = Math.Max(0, au.Charges);
                    if (au.Charges <= 0 && a < _me.Auras.Count && _me.Auras[a] == au) RemoveAuraAt(_me, a);
                }
                if (attacker.Dead) return;
            }
        }

        // ------------------------------------------------------------------ Shaman weapon imbues

        private SpellInfo _imbueSpell, _imbueProcSpell;
        private float _imbueRemaining, _imbueDps;
        private int _imbueItem;
        private int _imbueEnchant;                              // iteration 11 (spellfx): SpellItemEnchantment id of the imbue (GsImbueEnchant)

        /// <summary>
        /// ENCHANT_ITEM_TEMPORARY on the main-hand weapon (Rockbiter, Flametongue): SpellItemEnchantment.dbc gives the enchantment's effect
        /// types (6 = TOTEM: the shaman's weapon DPS + amount, applied as amount * weapon speed per swing like Player::ApplyEnchantment;
        /// 1 = COMBAT_SPELL: a spell per hit) and amounts. Lasts 30 minutes (the tooltips).
        /// </summary>
        private bool ApplyImbue(SpellInfo s, SpellEffectInfo e)
        {
            int weapon = _rec.Equipment[(int)EquipSlot.MainHand];
            if (weapon == 0) { EmitError("You must equip a weapon first"); return false; }
            EndImbue();
            _imbueSpell = s;
            _imbueItem = weapon;
            _imbueRemaining = 1800f;
            _imbueDps = 0f;
            _imbueProcSpell = null;
            _imbueEnchant = GsImbueEnchant(s, e);               // iteration 11 (spellfx): the enchant shown on the weapon (EquipEnchants[15])
            var ench = _files != null ? Dbc.Load(_files, "SpellItemEnchantment") : null;
            int r = ench != null ? ench.FindRow(e.MiscValue) : -1;
            if (r >= 0)
            {
                for (int k = 0; k < 3; k++)
                {
                    int type = ench.GetInt(r, 2 + k), amount = ench.GetInt(r, 5 + k), arg = ench.GetInt(r, 11 + k);
                    if (type == 6 || type == 2) _imbueDps += amount;                          // TOTEM / DAMAGE
                    else if ((type == 1 || type == 3) && arg > 0) _imbueProcSpell = _db.Find(arg);   // COMBAT_SPELL / EQUIP_SPELL (Flametongue 5 -> 10400, measured)
                }
            }
            else _imbueDps = Math.Max(1, CalcValue(s, e, _player.Level));
            _statsDirty = true;
            var au = new OfflineAura { Spell = s, Caster = PlayerGuid, Remaining = 1800f, Duration = 1800f };
            au.View = new AuraView { SpellId = s.Id, Name = s.Name, IconPath = s.IconPath, CasterGuid = PlayerGuid, Duration = 1800f, Remaining = 1800f };
            for (int i = 0; i < 3; i++) au.Active[i] = false;   // display only: the numbers live in the imbue fields
            RemoveImbueAuras();
            _me.Auras.Add(au);
            _aurasChanged = true;
            Emit(GameEventType.AuraApplied, PlayerGuid, PlayerGuid, 1800000, s.Id, false, s.Name);
            return true;
        }

        private void RemoveImbueAuras()
        {
            for (int a = _me.Auras.Count - 1; a >= 0; a--) if (_me.Auras[a].Spell.HasEffect(SpellEffect.EnchantItemTemporary)) RemoveAuraAt(_me, a);
        }

        private void EndImbue()
        {
            if (_imbueSpell == null) return;
            _imbueSpell = null;
            _imbueProcSpell = null;
            _imbueEnchant = 0;                                  // iteration 11 (spellfx)
            _imbueDps = 0f;
            _imbueRemaining = 0f;
            RemoveImbueAuras();
            _statsDirty = true;
        }

        /// <summary>Flat weapon damage the imbue adds to a main-hand swing (DPS amount * weapon speed).</summary>
        private float ImbueFlatBonus(float weaponSpeed) { return _imbueSpell != null ? _imbueDps * weaponSpeed : 0f; }

        // ------------------------------------------------------------------ conjured items

        /// <summary>
        /// CREATE_ITEM: the item id is in Spell.dbc (Conjure Water 5504 -> 5350, Conjure Food 587 -> 5349 ...). Item names and the
        /// food/drink amounts are server data (item_template + the item's Food/Drink spell): the conjured ranks of levels 1-10 are listed here
        /// (APPROXIMATED from the 3.3.5 item data as we know it); display ids come from Item.dbc.
        /// </summary>
        private static readonly Dictionary<int, ItemDef> ConjuredItems = new Dictionary<int, ItemDef>
        {
            { 5349, new ItemDef { Entry = 5349, Name = "Conjured Muffin", HealAmount = 61, UseSeconds = 18f } },
            { 1113, new ItemDef { Entry = 1113, Name = "Conjured Bread", HealAmount = 243, UseSeconds = 21f } },
            { 5350, new ItemDef { Entry = 5350, Name = "Conjured Water", ManaAmount = 151, UseSeconds = 18f } },
            { 2288, new ItemDef { Entry = 2288, Name = "Conjured Fresh Water", ManaAmount = 437, UseSeconds = 21f } },
        };

        private void CreateItem(SpellInfo s, SpellEffectInfo e)
        {
            int entry = e.ItemType;
            if (entry <= 0) return;
            if (FindItemDef(entry) == null)
            {
                ItemDef known;
                var d = ConjuredItems.TryGetValue(entry, out known)
                    ? new ItemDef { Entry = entry, Name = known.Name, HealAmount = known.HealAmount, ManaAmount = known.ManaAmount, UseSeconds = known.UseSeconds }
                    : new ItemDef { Entry = entry, Name = s.Name };
                if (_itemDbc != null) { int r = _itemDbc.FindRow(entry); if (r >= 0) d.DisplayId = _itemDbc.GetInt(r, DbcFields.Item.DisplayInfoID); }
                d.IconPath = "";
                if (_itemDisplay != null && d.DisplayId > 0)
                {
                    int r = _itemDisplay.FindRow(d.DisplayId);
                    if (r >= 0) { string icon = _itemDisplay.GetString(r, DbcFields.ItemDisplayInfo.InventoryIcon); if (!string.IsNullOrEmpty(icon)) d.IconPath = @"Interface\Icons\" + icon; }
                }
                _items[entry] = d;
            }
            int count = Math.Max(1, CalcValue(s, e, _player.Level));
            int left = GiveItem(entry, count);
            if (left > 0) EmitError("Inventory is full");
        }

        // ------------------------------------------------------------------ dispels

        /// <summary>DISPEL (Purify, Cure Poison...): removes harmful auras of the dispel type (MiscValue, SpellDispelType.dbc) from the target.</summary>
        private void DispelType(OfflineUnit u, int dispelType, bool harmfulOnes)
        {
            for (int a = u.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= u.Auras.Count) continue;
                var au = u.Auras[a];
                if (au.Harmful == harmfulOnes && au.Spell.Dispel == dispelType && !au.Spell.IsPassive) { RemoveAuraAt(u, a); return; }
            }
        }

        /// <summary>DISPEL_MECHANIC (Will of the Forsaken, Escape Artist...): removes auras whose spell or effect mechanic is MiscValue.</summary>
        private void DispelMechanic(OfflineUnit u, int mechanic)
        {
            for (int a = u.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= u.Auras.Count) continue;
                var au = u.Auras[a];
                if (!au.Harmful) continue;
                bool hit = au.Spell.Mechanic == mechanic;
                for (int i = 0; i < 3 && !hit; i++) { var e = au.Spell.Effects[i]; if (e != null && e.Mechanic == mechanic) hit = true; }
                if (hit) RemoveAuraAt(u, a);
            }
        }

        // ------------------------------------------------------------------ crowd control movement

        /// <summary>Feared: runs away from the caster in a wobbling line at the run speed x the fear's speed aura.</summary>
        private void StepFlee(OfflineCreature c, float dt)
        {
            c.Ai = AiState.Chase;
            OfflineAura fear = null;
            for (int a = 0; a < c.Auras.Count; a++) if (c.Auras[a].Has(SpellAura.ModFear, out _)) { fear = c.Auras[a]; break; }
            var from = fear != null ? UnitOf(fear.Caster) : null;
            c.FleeTimer -= dt;
            if (c.FleeTimer <= 0f || !c.HasMoveTarget)
            {
                c.FleeTimer = 1.5f;
                float ang = from != null ? Angle(from.Pos, c.Pos) : (float)(_rng.NextDouble() * Math.PI * 2);
                ang += RandRange(-0.6f, 0.6f);
                c.MoveTarget = new WowPos(c.Pos.X + (float)Math.Cos(ang) * 8f, c.Pos.Y + (float)Math.Sin(ang) * 8f, c.Pos.Z, 0f);
                c.HasMoveTarget = true;
            }
            if (c.Rooted) { c.State.Moving = false; return; }
            float speed = (c.Tpl.RunSpeed > 0 ? c.Tpl.RunSpeed : 7f) * 0.6f * SpeedMultiplier(c);
            if (MoveToward(c, c.MoveTarget, speed, dt, true)) c.HasMoveTarget = false;
        }

        /// <summary>Polymorphed (MOD_CONFUSE): ambles about slowly.</summary>
        private void StepConfused(OfflineCreature c, float dt)
        {
            c.FleeTimer -= dt;
            if (c.FleeTimer <= 0f || !c.HasMoveTarget)
            {
                c.FleeTimer = 2.5f;
                double ang = _rng.NextDouble() * Math.PI * 2;
                c.MoveTarget = new WowPos(c.Pos.X + (float)Math.Cos(ang) * 2f, c.Pos.Y + (float)Math.Sin(ang) * 2f, c.Pos.Z, 0f);
                c.HasMoveTarget = true;
            }
            if (c.Rooted) { c.State.Moving = false; return; }
            if (MoveToward(c, c.MoveTarget, 1.2f, dt, false)) c.HasMoveTarget = false;
        }

        /// <summary>Movement speed factor from MOD_INCREASE_SPEED / MOD_DECREASE_SPEED auras (Earthbind, Frostbolt, Chilled).</summary>
        private static float SpeedMultiplier(OfflineUnit u)
        {
            float m = u.AuraMultiplier(SpellAura.ModDecreaseSpeed, 0) * u.AuraMultiplier(SpellAura.ModIncreaseSpeed, 0);
            return Math.Max(0.1f, m);
        }

        /// <summary>
        /// Fear and root on a creature end once it took enough damage after they landed (3.3.5 "damage caused may interrupt the effect";
        /// the server rolls per hit - here a threshold of 20% of the creature's max health, APPROXIMATED).
        /// </summary>
        private void CheckDamageBreak(OfflineCreature c)
        {
            for (int a = c.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= c.Auras.Count) continue;
                var au = c.Auras[a];
                if (!au.Harmful || au.DamageTaken < Math.Max(1, c.MaxHealth / 5)) continue;
                if (au.Has(SpellAura.ModFear, out _) || au.Has(SpellAura.ModRoot, out _)) RemoveAuraAt(c, a);
            }
        }

        // ------------------------------------------------------------------ creature melee against the pet / totems

        /// <summary>A creature's swing at the player's pet or totem (5% miss, 5% crit, the target's armor).</summary>
        private void CreatureSwingAt(OfflineCreature c, OfflineUnit victim)
        {
            c.State.AttackCounter++;
            float r = Rand01() * 100f;
            if (r < 5f) { Emit(GameEventType.MeleeMiss, c.Guid, victim.Guid, 0, 0, false, "Miss"); return; }
            float ap = c.BaseAttackPower + c.AuraSum(SpellAura.ModAttackPower, 0);
            float mn = (c.DamageBase + ap / 14f) * c.AttackSpeed * c.DamageMod;
            float mx = (c.DamageBase * 1.5f + ap / 14f) * c.AttackSpeed * c.DamageMod;
            float dmg = RandRange(Math.Max(1f, mn), Math.Max(1f, mx));
            var vc = victim as OfflineCreature;
            if (vc != null) dmg *= WowFormulas.ArmorMultiplier(vc.Armor, c.Level);
            dmg *= victim.AuraMultiplier(SpellAura.ModDamagePercentTaken, 1) * c.AuraMultiplier(SpellAura.ModDamagePercentDone, 1);
            bool crit = r > 95f;
            if (crit) dmg *= 2f;
            DealDamage(c, victim, Math.Max(1, (int)dmg), 0, crit, GameEventType.MeleeHit);
        }

        /// <summary>Threat per point of damage: MOD_THREAT / MOD_TOTAL_THREAT auras (stances, forms, Shadowmeld's wipe is handled apart).</summary>
        private float ThreatMultiplier(OfflineUnit src)
        {
            if (src != _me) return 1f;
            float m = _me.AuraMultiplier(SpellAura.ModThreat, 0);
            for (int a = 0; a < _me.Auras.Count; a++)
            {
                var au = _me.Auras[a];
                for (int i = 0; i < 3; i++)
                {
                    var e = au.Spell.Effects[i];
                    if (e != null && e.IsAura && e.Aura == SpellAura.ModTotalThreat && au.Active[i] && au.Amount[i] > -100) m *= (100f + au.Amount[i]) / 100f;
                }
            }
            return Math.Max(0.01f, m);
        }

        /// <summary>Shadowmeld (MOD_TOTAL_THREAT -90000001): creatures forget the player; those with nobody else to fight go home.</summary>
        private void WipePlayerThreat()
        {
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (c.Summon != SummonKind.None || !c.Threat.Remove(PlayerGuid)) continue;
                if (c.Engaged && c.Threat.Count == 0) StartEvade(c);
            }
            if (_autoAttack) StopAttack();
            if (_autoShot) SetAutoShot(false);
        }

        // ------------------------------------------------------------------ aura events

        private void EmitAuraApplied(OfflineAura au, OfflineUnit target)
        {
            if (au.Hidden || au.Spell.IsPassive) return;
            Emit(GameEventType.AuraApplied, au.Caster, target.Guid, au.Duration > 0 ? (int)(au.Duration * 1000f) : 0, au.Spell.Id, false, au.Spell.Name);
        }

        private void EmitAuraRemoved(OfflineAura au, OfflineUnit target)
        {
            if (au.Hidden || au.Spell.IsPassive) return;
            Emit(GameEventType.AuraRemoved, au.Caster, target.Guid, 0, au.Spell.Id, false, au.Spell.Name);
        }

        /// <summary>Removes every aura of a unit (death, evade reset) with AuraRemoved events.</summary>
        private void ClearAuras(OfflineUnit u)
        {
            for (int a = u.Auras.Count - 1; a >= 0; a--) if (a < u.Auras.Count) RemoveAuraAt(u, a);
            u.Auras.Clear();
        }

        /// <summary>Polymorph's TRANSFORM aura: the creature shows the Sheep model while it lasts (CreatureDisplayInfo of the transform's creature).</summary>
        private void RefreshTransform(OfflineCreature c)
        {
            int display = c.BaseDisplayId;
            for (int a = 0; a < c.Auras.Count; a++)
            {
                SpellEffectInfo te;
                if (c.Auras[a].Has(SpellAura.Transform, out te)) { int d = TransformDisplay(te.MiscValue); if (d > 0) display = d; }
            }
            if (display != 0 && c.State.DisplayId != display) c.State.DisplayId = display;
        }

        // ------------------------------------------------------------------ racial abilities scripted on the server

        private const int LightningShieldDamage = 26364, FlametongueProc = 8026;
        private const int EscapeArtist = 20589, EveryManForHimself = 59752, CannibalizeSpell = 20577, CannibalizeChannel = 20578;
        private const int StoneformSpell = 20594, StoneformArmor = 65116;

        /// <summary>True when the spell makes the caster immune to the mechanic (Will of the Forsaken: MECHANIC_IMMUNITY 5 fear, 10 sleep, 1 charm).</summary>
        private static bool GrantsImmunity(SpellInfo s, int mechanic)
        {
            for (int i = 0; i < 3; i++) { var e = s.Effects[i]; if (e != null && e.IsAura && e.Aura == SpellAura.MechanicImmunity && e.MiscValue == mechanic) return true; }
            return false;
        }

        /// <summary>Cannibalize (20577: DUMMY, radius 5, TargetCreatureType 96 = humanoid | undead): a fresh corpse of those types within 5 yd.</summary>
        private OfflineCreature FindCannibalCorpse()
        {
            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (!c.Present || !c.Dead || c.Summon != SummonKind.None) continue;
                if (c.CreatureType != 6 && c.CreatureType != 7 && c.CreatureType != 0) continue;
                if (Dist(_me.Pos, c.Pos) <= 5f + c.CombatReach) return c;
            }
            return null;
        }

        /// <summary>The server then casts 20578 on the Undead: a 10 s channel healing 7% of max health every 2 s (OBS_MOD_HEALTH), broken by moving.</summary>
        private void StartCannibalize()
        {
            var ch = _db.Find(CannibalizeChannel);
            if (ch == null) return;
            ApplySpellAuras(ch, _me, _me, 0);
            StartChannel(ch, _me);
        }

        /// <summary>
        /// Escape Artist (roots and snares) and Every Man for Himself (also stuns, fears, polymorph): the server scripts remove these
        /// effects from the caster.
        /// </summary>
        private void RemoveLossOfControl(bool all)
        {
            for (int a = _me.Auras.Count - 1; a >= 0; a--)
            {
                if (a >= _me.Auras.Count) continue;
                var au = _me.Auras[a];
                if (!au.Harmful) continue;
                bool hit = au.Spell.HasAura(SpellAura.ModRoot) || au.Spell.HasAura(SpellAura.ModDecreaseSpeed);
                if (all) hit |= au.Spell.HasAura(SpellAura.ModStun) || au.Spell.HasAura(SpellAura.ModFear) || au.Spell.HasAura(SpellAura.ModConfuse);
                if (hit) RemoveAuraAt(_me, a);
            }
        }

        /// <summary>
        /// After an aura landed: immunities remove what they protect from (DISPEL_IMMUNITY by dispel type, MECHANIC_IMMUNITY by mechanic -
        /// Stoneform, Will of the Forsaken), Every Man for Himself breaks loss of control, Stoneform adds its armor aura (65116).
        /// </summary>
        private void AfterAuraApplied(SpellInfo s, OfflineUnit caster, OfflineUnit target)
        {
            if (target != _me) return;
            for (int i = 0; i < 3; i++)
            {
                var e = s.Effects[i];
                if (e == null || !e.IsAura) continue;
                if (e.Aura == SpellAura.DispelImmunity) { for (int n = 0; n < 8; n++) DispelType(_me, e.MiscValue, true); }
                else if (e.Aura == SpellAura.MechanicImmunity && e.MiscValue > 0) DispelMechanic(_me, e.MiscValue);
            }
            if (s.Id == EveryManForHimself) RemoveLossOfControl(true);
            if (s.Id == StoneformSpell) { var st = _db.Find(StoneformArmor); if (st != null) ApplySpellAuras(st, _me, _me, 0); }
        }

        /// <summary>creature_template display ids of transform creatures (server data): 16372 "Sheep" (Polymorph) -> 856 Creature\Sheep\Sheep.mdx.</summary>
        private static int TransformDisplay(int creatureEntry)
        {
            switch (creatureEntry)
            {
                case 16372: return 856;
            }
            return 0;
        }
    }
}
