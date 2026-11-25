using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Content;
using SomniumSpace.Worlds.SomniumWoW.Play.Riding;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        /// <summary>
        /// Offline tuning of creature melee damage on top of AzerothCore's creature_classlevelstats x the template multiplier
        /// (see the Rules report: the real level-1 numbers make fights trivial; tuned with the RulesSim probe).
        /// </summary>
        public static float CreatureDamageTuning = 1.8f;

        /// <summary>Offline tuning of creature health on top of creature_classlevelstats x the template multiplier (longer, readable VR fights).</summary>
        public static float CreatureHealthTuning = 1.2f;

        /// <summary>Spell id of the melee "Auto Attack" button (Spell.dbc 6603, effect ATTACK).</summary>
        public const int AutoAttackSpell = 6603;

        // player stats (recomputed by RecalcStats)
        private float _str, _agi, _sta, _int, _spi;
        private float _ap, _rap, _crit, _spellCrit, _dodge, _parry, _armor;
        private float _mhMin = 1, _mhMax = 2, _mhSpeed = 2f;
        private int _mhSubclass = -1;
        private bool _hasMelee, _hasRanged, _hasOffhand, _dualWield;
        private float _ohMin, _ohMax, _ohSpeed = 2f, _offTimer;
        private int _ohSubclass = -1;
        private float _rgMin, _rgMax, _rgSpeed = 2f;
        private int _rgSubclass = -1;
        private int _baseHealth, _baseMana, _maxMana;
        private bool _canParry;

        // powers
        private int _mana;
        private float _rage, _energy, _manaFrac;
        private double _lastManaSpend = -100;
        private float _regenTimer = 2f;
        private bool _moving;
        private int _form;                       // SpellShapeshiftForm id
        private double _victoriousUntil = -1;    // Victory Rush window (aura state 10)
        private bool _inCombat;

        private void InitPlayer()
        {
            _me.Guid = PlayerGuid;
            _player.Guid = PlayerGuid;
            _player.Name = _rec.Name;
            _player.Class = _rec.Class;
            _player.Race = _rec.Race;
            _player.Level = Math.Max(1, Math.Min(WowFormulas.MaxLevel, _rec.Level));
            _me.Level = _player.Level;
            _player.Xp = _player.Level >= WowFormulas.MaxLevel ? 0 : Math.Max(0, _rec.Xp);
            _player.XpToNextLevel = WowFormulas.XpToNextLevel(_player.Level);
            _player.Money = _rec.Money;
            _me.Pos = _rec.HasPos && _rec.MapId == _zone.MapId ? _rec.Pos : _zone.PlayerStart;
            _player.Pos = _me.Pos;
            _player.Sheath = SheathState.Sheathed;
            Array.Copy(_rec.ActionButtons, _player.ActionButtons, WowActionSlots.Count); AbSyncBar();   // iteration 11 (vrcast s6.2 #5): all 144 buttons, ActionBar = their 12 spells

            // Spells: what the record knows + the class kit up to this level (from the game files) + melee Auto Attack.
            bool emptyBar = true;
            for (int i = 0; i < 12; i++) if (_player.ActionButtons[i] != 0) emptyBar = false;
            if (_db.Available)
            {
                if (_db.Find(AutoAttackSpell) != null && !_rec.Spells.Contains(AutoAttackSpell)) _rec.Spells.Insert(0, AutoAttackSpell);
                foreach (var k in _db.KnownAt(_rec.Class, _rec.Race, _player.Level)) LearnSpell(k.SpellId, false);
                LearnRacials();
                if (emptyBar)
                {
                    int slot = 0;
                    foreach (int s in _rec.Spells) { var si = _db.Find(s); if (si != null && !si.IsPassive && slot < 12) AbSet(slot++, WowActionSlots.Spell(s)); }
                }
                else if (Array.IndexOf(_player.ActionBar, AutoAttackSpell) < 0) PlaceOnBar(AutoAttackSpell);
            }
            _canParry = ClassCanParry();

            // Warriors stand in Battle Stance (the server applies it on login when known).
            if (_rec.Class == WowClass.Warrior && _rec.Spells.Contains(2457)) { var bs = _db.Find(2457); if (bs != null) ApplySpellAuras(bs, _me, _me, 0); }

            RecalcStats();
            _me.Health = _rec.Health > 0 ? Math.Min(_rec.Health, _me.MaxHealth) : _me.MaxHealth;
            _mana = _maxMana;
            _rage = 0f;
            _energy = 100f;
            if (_rec.Power >= 0)
            {
                if (_rec.Class == WowClass.Warrior) _rage = 0f;       // rage does not survive a logout
                else if (_rec.Class == WowClass.Rogue) _energy = 100f;
                else _mana = Math.Min(_rec.Power, _maxMana);
            }
            RebuildSpellbook();
            RefreshBagsAndEquipment();
            UpdatePlayerView();
        }

        /// <summary>Parry needs a passive spell with the PARRY effect whose SkillLineAbility row has the class (warrior, hunter, rogue) - level 6+ (APPROXIMATED trainer level).</summary>
        private bool ClassCanParry()
        {
            var parry = _db.Find(3127);
            if (parry == null || !parry.HasEffect(SpellEffect.Parry)) return false;
            bool cls = _rec.Class == WowClass.Warrior || _rec.Class == WowClass.Hunter || _rec.Class == WowClass.Rogue;   // SLA 3127 ClassMask 15 (warrior|paladin|hunter|rogue), measured
            return cls && _player.Level >= 6;
        }

        private int _dualWieldMinLevel = -2;

        /// <summary>
        /// Dual Wield from the files: SkillRaceClassInfo.dbc rows of skill 118 "Dual Wield" (measured: ClassMask 8 rogue MinLevel 1;
        /// ClassMask 5 warrior|hunter MinLevel 20).
        /// </summary>
        private bool CanDualWield()
        {
            if (_dualWieldMinLevel == -2)
            {
                _dualWieldMinLevel = -1;
                var t = _files != null ? Data.Dbc.Load(_files, "SkillRaceClassInfo") : null;
                if (t != null)
                {
                    int bit = 1 << ((int)_rec.Class - 1);
                    for (int r = 0; r < t.RecordCount; r++)
                        if (t.GetInt(r, SpellFields.SkillRaceClassInfo.SkillId) == 118 && (t.GetInt(r, SpellFields.SkillRaceClassInfo.ClassMask) & bit) != 0)
                        { _dualWieldMinLevel = Math.Max(1, t.GetInt(r, 5)); break; }   // field 5 = MinLevel
                }
            }
            return _dualWieldMinLevel > 0 && _player.Level >= _dualWieldMinLevel;
        }

        /// <summary>Recomputes stats, max health/power, attack power, crit/dodge/parry, armor and weapon damage from level, items and auras.</summary>
        private void RecalcStats()
        {
            var cls = _rec.Class;
            int level = _player.Level;
            var b = WowFormulas.PlayerBase(cls, level, _rec.Race);
            _baseHealth = b.BaseHealth;
            _baseMana = b.BaseMana;
            float[] stat = { b.Strength, b.Agility, b.Stamina, b.Intellect, b.Spirit };
            int itemArmor = 0;
            for (int s = 0; s < 19; s++)
            {
                int entry = _rec.Equipment[s];
                if (entry == 0) continue;
                var d = FindItemDef(entry);
                if (d == null) continue;
                itemArmor += d.Armor;
                stat[0] += d.Strength; stat[1] += d.Agility; stat[2] += d.Stamina; stat[3] += d.Intellect; stat[4] += d.Spirit;
            }
            for (int i = 0; i < 5; i++)
            {
                stat[i] += _me.AuraSumMisc(SpellAura.ModStat, i);
                float pct = 1f + (_me.AuraSumMisc(SpellAura.ModPercentStat, i) + _me.AuraSumMisc(SpellAura.ModTotalStatPercentage, i)) / 100f;
                stat[i] = (float)Math.Floor(stat[i] * pct);
                if (stat[i] < 0) stat[i] = 0;
            }
            _str = stat[0]; _agi = stat[1]; _sta = stat[2]; _int = stat[3]; _spi = stat[4];

            int formForAp = _form == 1 || _form == 5 || _form == 8 ? _form : 0;
            float maxHp = (_baseHealth * (1f + _me.AuraSum(SpellAura.ModBaseHealthPct, 0) / 100f) + WowFormulas.HealthFromStamina(_sta))   // Tauren Endurance: base health +5% (iteration 3)
                          * (1f + _me.AuraSum(SpellAura.ModIncreaseHealthPercent, 0) / 100f) + _me.AuraSum(SpellAura.ModIncreaseHealth, 0);
            int oldMax = _me.MaxHealth;
            _me.MaxHealth = Math.Max(1, (int)maxHp);
            if (oldMax > 1 && _me.MaxHealth != oldMax && !_player.Dead) _me.Health = Math.Max(1, (int)Math.Round((double)_me.Health * _me.MaxHealth / oldMax));
            if (_me.Health > _me.MaxHealth) _me.Health = _me.MaxHealth;
            _maxMana = _baseMana > 0 ? (int)(_baseMana + WowFormulas.ManaFromIntellect(_int)) : 0;
            if (_mana > _maxMana) _mana = _maxMana;

            // Armor: items (x form bonus) + 2 per agility + flat auras, then percent auras.
            float armor = itemArmor * (1f + _me.AuraSum(SpellAura.ModBaseResistancePct, 1) / 100f) + _agi * 2f + _me.AuraSum(SpellAura.ModResistance, 1) + _me.AuraSum(SpellAura.ModBaseResistance, 1);
            armor *= 1f + _me.AuraSum(SpellAura.ModResistancePct, 1) / 100f;
            _armor = Math.Max(0f, armor);

            _ap = Math.Max(0f, WowFormulas.MeleeAttackPower(cls, level, _str, _agi, formForAp) + _me.AuraSum(SpellAura.ModAttackPower, 0));
            _rap = Math.Max(0f, WowFormulas.RangedAttackPower(cls, level, _agi, formForAp) + _me.AuraSum(SpellAura.ModRangedAttackPower, 0));
            _crit = _gt.MeleeCritPercent(cls, level, _agi) + _me.AuraSum(SpellAura.ModWeaponCritPercent, 0);
            _spellCrit = _gt.SpellCritPercent(cls, level, _int);
            _dodge = _gt.DodgePercent(cls, level, _agi) + _me.AuraSum(SpellAura.ModDodgePercent, 0);
            _canParry = ClassCanParry();

            // Weapons
            _hasMelee = WeaponOf(_rec.Equipment[(int)EquipSlot.MainHand], false, out _mhMin, out _mhMax, out _mhSpeed, out _mhSubclass);
            if (!_hasMelee) { _mhMin = 1f; _mhMax = 2f; _mhSpeed = 2f; _mhSubclass = -1; }   // BASE_MINDAMAGE / BASE_MAXDAMAGE unarmed
            _hasRanged = WeaponOf(_rec.Equipment[(int)EquipSlot.Ranged], true, out _rgMin, out _rgMax, out _rgSpeed, out _rgSubclass);
            _dualWield = CanDualWield();
            int offInv = InventoryTypeOf(_rec.Equipment[(int)EquipSlot.OffHand]);
            _hasOffhand = _dualWield && _hasMelee && (offInv == 13 || offInv == 22) && WeaponOf(_rec.Equipment[(int)EquipSlot.OffHand], false, out _ohMin, out _ohMax, out _ohSpeed, out _ohSubclass);
            _parry = (_canParry && _hasMelee ? 5f : 0f) + _me.AuraSum(SpellAura.ModParryPercent, 0);

            var st = _player.Stats;
            st.Strength = (int)_str; st.Agility = (int)_agi; st.Stamina = (int)_sta; st.Intellect = (int)_int; st.Spirit = (int)_spi;
            st.Armor = (int)_armor;
            st.AttackPower = (int)_ap;
            float mn, mx, spd;
            MeleeDamageRange(false, out mn, out mx, out spd);
            st.MinDamage = mn; st.MaxDamage = mx; st.AttackSpeed = spd;
            st.CritChance = _crit;
            st.DodgeChance = _dodge;
            // integrator (character request): the character pane's offline extras (the rest stays 0 offline - known gap)
            st.RangedAttackPower = (int)_rap;
            st.ParryChance = _parry;
            if (st.Resist != null && st.Resist.Length > 0) st.Resist[0] = st.Armor;
        }

        /// <summary>Weapon damage/speed of an equipped item: the zone's ItemDef numbers, else a default by Item.dbc weapon subclass.</summary>
        private bool WeaponOf(int entry, bool ranged, out float min, out float max, out float speed, out int subclass)
        {
            min = max = 0; speed = 2f; subclass = -1;
            if (entry == 0) return false;
            var d = FindItemDef(entry);
            int itemClass = -1;
            if (_itemDbc != null)
            {
                int r = _itemDbc.FindRow(entry);
                if (r >= 0) { itemClass = _itemDbc.GetInt(r, Data.DbcFields.Item.ClassID); subclass = _itemDbc.GetInt(r, Data.DbcFields.Item.SubclassID); }
            }
            if (d != null && d.MaxDamage > 0 && d.Speed > 0)
            {
                min = d.MinDamage; max = d.MaxDamage; speed = d.Speed;
                if (subclass < 0) subclass = ranged ? 2 : 7;
                return true;
            }
            if (itemClass == 2)
            {
                WowFormulas.DefaultWeapon(subclass, out min, out max, out speed);
                return true;
            }
            return false;
        }

        /// <summary>Player melee (or ranged) damage per hit with attack power (Player::CalculateMinMaxDamage) and the current swing time.</summary>
        private void MeleeDamageRange(bool normalized, out float min, out float max, out float attackTime)
        {
            var form = _form != 0 ? _db.Form(_form) : null;
            float speed = _mhSpeed;
            float wMin = _mhMin, wMax = _mhMax;
            if (form != null && form.AttackSpeedMs > 0)
            {
                speed = form.AttackSpeedMs / 1000f;
                int lvl = Math.Min(60, _player.Level);
                wMin = lvl * 0.85f * speed;
                wMax = lvl * 1.25f * speed;
            }
            float apSpeed = normalized ? (form != null && form.AttackSpeedMs > 0 ? (form.Id == 1 ? 1.0f : 2.5f) : WowFormulas.NormalizedSpeed(_mhSubclass, false)) : speed;
            float bonus = _ap / 14f * apSpeed + (form == null ? ImbueFlatBonus(speed) : 0f);   // + Rockbiter (iteration 3)
            float pct = _me.AuraMultiplier(SpellAura.ModDamagePercentDone, 1);
            min = (wMin + bonus) * pct;
            max = (wMax + bonus) * pct;
            attackTime = speed / (_me.AuraMultiplier(SpellAura.ModMeleeHaste, 0) * _me.AuraMultiplier(SpellAura.MeleeSlow, 0));   // MELEE_SLOW: Berserking haste (iteration 3)
        }

        private void RangedDamageRange(bool normalized, OfflineUnit target, out float min, out float max, out float attackTime)
        {
            float rap = _rap;
            if (target != null) rap += target.AuraSum(SpellAura.RangedAttackPowerAttackerBonus, 0);   // Hunter's Mark
            float apSpeed = normalized ? 2.8f : _rgSpeed;
            float bonus = rap / 14f * apSpeed;
            float pct = _me.AuraMultiplier(SpellAura.ModDamagePercentDone, 1);
            min = (_rgMin + bonus) * pct;
            max = (_rgMax + bonus) * pct;
            // Auto Shot: "Ranged attacks fired by a Hunter all have 15% increased attack speed" (spell 75 description -> 34082 s2).
            float haste = _me.AuraMultiplier(SpellAura.ModRangedHaste, 0);
            if (_rec.Class == WowClass.Hunter) haste *= 1.15f;
            attackTime = _rgSpeed / haste;
        }

        // ------------------------------------------------------------------ powers

        private PowerType CurrentPower()
        {
            if (_form == 5 || _form == 8) return PowerType.Rage;
            if (_form == 1) return PowerType.Energy;
            return WowFormulas.ClassPower(_rec.Class);
        }

        private int PowerOf(PowerType p)
        {
            switch (p)
            {
                case PowerType.Rage: return (int)_rage;
                case PowerType.Energy: return (int)_energy;
                case PowerType.Mana: return _mana;
            }
            return 0;
        }

        private void AddPower(PowerType p, float amount)
        {
            switch (p)
            {
                case PowerType.Rage: _rage = Math.Max(0f, Math.Min(100f, _rage + amount)); break;
                case PowerType.Energy: _energy = Math.Max(0f, Math.Min(100f, _energy + amount)); break;
                case PowerType.Mana: _mana = Math.Max(0, Math.Min(_maxMana, _mana + (int)amount)); break;
            }
        }

        // ------------------------------------------------------------------ the player's per-step update

        private void StepPlayer(float dt)
        {
            if (_statsDirty) { _statsDirty = false; RecalcStats(); }
            if (_player.Dead) { _inCombat = false; StepDeath(dt); return; }   // stage O2: WoW's death flow (OfflineSession.Ghost.cs)

            // In combat while a creature is engaged with the player.
            bool engaged = false;
            for (int i = 0; i < _creatures.Count; i++) { var c = _creatures[i]; if (c.Present && !c.Dead && c.Engaged) { engaged = true; break; } }
            if (engaged != _inCombat)
            {
                _inCombat = engaged;
                if (engaged) CancelConsumable("You are in combat");
            }

            // Regeneration (Player::RegenerateAll): energy and mana continuously, health and rage decay every 2 s.
            _energy = Math.Min(100f, _energy + 10f * dt);
            if (_maxMana > 0 && _mana < _maxMana && _time - _lastManaSpend >= 5.0)
            {
                _manaFrac += _gt.ManaRegenPerSecond(_rec.Class, _player.Level, _int, _spi) * dt;
                if (_manaFrac >= 1f) { int add = (int)_manaFrac; _manaFrac -= add; _mana = Math.Min(_maxMana, _mana + add); }
            }
            _regenTimer -= dt;
            if (_regenTimer <= 0f)
            {
                _regenTimer += 2f;
                if (!_inCombat)
                {
                    if (_me.Health < _me.MaxHealth)
                    {
                        float hp = _gt.HealthRegenPerTick(_rec.Class, _player.Level, _spi);
                        _me.Health = Math.Min(_me.MaxHealth, _me.Health + Math.Max(1, (int)hp));
                    }
                    if (_rage > 0f) _rage = Math.Max(0f, _rage - 2f);
                }
            }

            // Walking away closes NPC dialogs and loot windows (10 yd, like the client).
            if (_dialogNpc != null && (Dist(_me.Pos, _dialogNpc.Pos) > 10f || !_dialogNpc.Present)) CloseDialog();
            if (_lootCorpse != null && (Dist(_me.Pos, _lootCorpse.Pos) > 10f || !_lootCorpse.Present)) CloseLoot();
            TrOfflineTick(dt);                                  // iteration 11 (trainer F2): the trainer window closes on walking away

            StepConsumable(dt);
            StepAuras(_me, dt);
            if (_player.Dead) return;
            StepCooldowns(dt);
            StepCasting(dt);
            StepMelee(dt);
            StepRanged(dt);
        }

        // ------------------------------------------------------------------ experience and levels

        private void GiveXp(int amount, ulong source, int questId)
        {
            if (amount <= 0 || _player.Level >= WowFormulas.MaxLevel) return;
            var e = new GameEvent { Type = GameEventType.XpGain, Amount = amount, TargetGuid = source, QuestId = questId };
            _events.Enqueue(e);
            _player.Xp += amount;
            while (_player.Level < WowFormulas.MaxLevel && _player.Xp >= WowFormulas.XpToNextLevel(_player.Level))
            {
                _player.Xp -= WowFormulas.XpToNextLevel(_player.Level);
                LevelUp();
            }
            if (_player.Level >= WowFormulas.MaxLevel) _player.Xp = 0;
            _player.XpToNextLevel = WowFormulas.XpToNextLevel(_player.Level);
        }

        private void LevelUp()
        {
            _player.Level++;
            _me.Level = _player.Level;
            RecalcStats();
            _me.Health = _me.MaxHealth;
            _mana = _maxMana;
            Emit(GameEventType.LevelUp, PlayerGuid, 0, _player.Level, 0, false, "You have reached level " + _player.Level + "!");
            WowLog.Info("OfflineSession: level " + _player.Level + " (" + _me.MaxHealth + " health, " + _maxMana + " mana, AP " + (int)_ap + ")");
            int learned = 0;
            if (_db.Available)
                foreach (var k in _db.KnownAt(_rec.Class, _rec.Race, _player.Level))
                    if (!_rec.Spells.Contains(k.SpellId)) { LearnSpell(k.SpellId, true); learned++; }
            if (learned > 0) RebuildSpellbook();
            OnPlayerLevelUp3();
            RefreshQuestLog();
            RefreshMarkers();
            TrOfflineLevelChanged();                            // iteration 11 (trainer F4): the open trainer window re-lists
        }

        /// <summary>Adds a spell (replacing a lower rank of the same name in the spellbook and on the action bar).</summary>
        private void LearnSpell(int spellId, bool announce)
        {
            var s = _db.Find(spellId);
            if (s == null || _rec.Spells.Contains(spellId)) return;
            int replaced = 0;
            for (int i = _rec.Spells.Count - 1; i >= 0; i--)
            {
                var old = _db.Find(_rec.Spells[i]);
                if (old == null || old.Id == spellId || old.Name != s.Name || old.Rank == s.Rank) continue;
                if (old.SpellLevel > s.SpellLevel) return;   // already know a higher rank
                replaced = old.Id;
                _rec.Spells.RemoveAt(i);
                AbReplaceSpell(old.Id, spellId);   // iteration 11 (vrcast s6.2 #5): every button (OfflineSession.ActionBars.cs)
            }
            _rec.Spells.Add(spellId);
            if (announce)
            {
                Emit(GameEventType.SpellLearned, PlayerGuid, 0, 0, spellId, false, "You have learned a new " + (s.IsPassive ? "passive " : "") + "ability: " + s.Name + (s.Rank.Length > 0 ? " (" + s.Rank + ")" : "") + ".");
                if (replaced == 0 && !s.IsPassive) PlaceOnBar(spellId);
            }
        }

        // Iteration 11 (vrcast s6.2 #5, applied in wave 0): PlaceOnBar and SetActionBarSlot moved to OfflineSession.ActionBars.cs (same names).
    }
}
