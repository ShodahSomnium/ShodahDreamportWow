using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play
{
    // The 3.3.5a item tooltip (GameTooltip:SetBagItem / SetInventoryItem / SetHyperlink...), shared by the online and offline sessions.
    // The client builds it in C++, from GlobalStrings.lua texts; the line order below follows the order those string names sit in
    // Wow.exe's string pool for the item tooltip function (literals are pooled in reverse order of use: "ITEM_HEROIC ... ITEM_CONJURED,
    // ITEM_BIND_*, ITEM_UNIQUE*, ITEM_LIMIT_CATEGORY*, ITEM_STARTS_QUEST, CONTAINER_SLOTS, SPEED / *DAMAGE_TEMPLATE / DPS_TEMPLATE,
    // SHIELD_BLOCK_TEMPLATE, ITEM_ENCHANT_TIME_LEFT, sockets, ITEM_SOCKET_BONUS, ITEM_RANDOM_ENCHANT, DURABILITY_TEMPLATE, ITEM_DURATION,
    // ITEM_RACES/CLASSES_ALLOWED, ITEM_LEVEL_RANGE_CURRENT, ..., <spelldesc> ITEM_COOLDOWN_TOTAL, ITEM_SET_NAME, ITEM_SET_BONUS(_GRAY),
    // "%s" (description), ITEM_CREATED_BY, ITEM_OPENABLE / ITEM_READABLE, ITEM_DISENCHANT_*", offsets 6440300-6442700 of Wow.exe 3.3.5a).
    // Item level is not shown: ITEM_LEVEL is behind the CVar showItemLevel ("Show item level in the tooltip", Wow.exe), off by default.
    // Colours: FrameXML Constants.lua RED_FONT_COLOR_CODE ff2020, GRAY ff808080, NORMAL ffd200, LIGHTYELLOW ffff9a; green 00ff00 as
    // ITEM_CREATED_BY "|cff00ff00<Made by %s>|r" (GlobalStrings.lua:4356).

    /// <summary>The template part: the SMSG_ITEM_QUERY_SINGLE_RESPONSE fields the tooltip draws (AC ItemHandler.cpp:413-536).</summary>
    public sealed class ItemTipData
    {
        public int Entry, Class = -1, SubClass = -1, Quality = 1, InventoryType, AllowableClass = -1, AllowableRace = -1, ItemLevel, RequiredLevel;
        public uint Flags, Flags2;
        public string Name = "", Description = "";
        public int RequiredSkill, RequiredSkillRank, RequiredSpell, RequiredReputationFaction, RequiredReputationRank;
        public int MaxCount, ContainerSlots, SellPrice;
        public int StatCount;
        public readonly int[] StatType = new int[ItemTip.MaxStats], StatValue = new int[ItemTip.MaxStats];
        public int ScalingStatDistribution, ScalingStatValue;
        public readonly float[] DmgMin = new float[2], DmgMax = new float[2];
        public readonly int[] DmgType = new int[2];
        public int Armor, Delay, Block;
        public readonly int[] Res = new int[6];             // Holy, Fire, Nature, Frost, Shadow, Arcane
        public readonly int[] SpellId = new int[5], SpellTrigger = new int[5], SpellCharges = new int[5], SpellCooldown = new int[5], SpellCategoryCooldown = new int[5];
        public int Bonding, PageText, StartQuest, RandomProperty, RandomSuffix, ItemSet, MaxDurability, Duration, ItemLimitCategory, RequiredDisenchantSkill;
        public readonly int[] SocketColor = new int[3];
        public int SocketBonus;
        public string UseText;                               // offline only: the zone file's consumable line (no spell)
    }

    /// <summary>One item's own state: the item object's fields (owned), or what a loot / roll / trade / mail / auction packet carries.</summary>
    public sealed class ItemTipInstance
    {
        public bool Owned;
        public int Durability, MaxDurability;
        public uint FieldFlags;                              // ITEM_FIELD_FLAGS
        public int RandomPropertyId;                         // ITEM_FIELD_RANDOM_PROPERTIES_ID: > 0 ItemRandomProperties, < 0 -ItemRandomSuffix id
        public int SuffixFactor;                             // ITEM_FIELD_PROPERTY_SEED (GenerateEnchSuffixFactor); 0 = computed from RandPropPoints
        public readonly int[] Enchant = new int[12];         // ITEM_FIELD_ENCHANTMENT_1_1 + slot * 3 (AC Item.h EnchantmentSlot 0..11)
        public int TempEnchantMs;                            // ITEM_FIELD_ENCHANTMENT_2_2 (TEMP_ENCHANTMENT_SLOT duration, ms)
        public ulong Creator;                                // ITEM_FIELD_CREATOR
        public readonly int[] SpellCharges = new int[5];     // ITEM_FIELD_SPELL_CHARGES
        public bool HasCharges;
        public int DurationLeft;                             // ITEM_FIELD_DURATION (s), 0 = none

        /// <summary>What the tooltip depends on beyond the template (a changed signature = a new view).</summary>
        public int Signature()
        {
            int h = RandomPropertyId * 31 + SuffixFactor;
            for (int i = 0; i < Enchant.Length; i++) h = h * 31 + Enchant[i];
            h = h * 31 + (int)(Creator ^ (Creator >> 32));
            for (int i = 0; i < SpellCharges.Length; i++) h = h * 31 + SpellCharges[i];
            h = h * 31 + TempEnchantMs / 60000;
            h = h * 31 + DurationLeft / 60;
            return h;
        }
    }

    /// <summary>The player-side facts (red requirement lines, set pieces, names). Unset delegates = unknown = drawn as met.</summary>
    public sealed class ItemTipContext
    {
        public int Level = 1, ClassId, Race;
        public Func<int, int> SkillValue;                    // SkillLine id -> value, -1 = not learned
        public Func<int, int> ReputationRank;                // Faction id -> 0 Hated .. 7 Exalted, -1 = unknown
        public Func<int, bool> KnowsSpell;
        public Func<int, string> ItemName;                   // set pieces: null = not known yet (asked), "" = no such item
        public Func<int, bool> IsEquipped;
        public Func<int, string> SpellDescription;           // resolved Spell.dbc description (null = none)
        public Func<ulong, string> PlayerName;               // <Made by %s>: null = not known yet (asked)
        /// <summary>Set by Build: a set piece or the creator had no name yet - build again when the answers arrive.</summary>
        public bool Incomplete;
    }

    /// <summary>The DBC rows a tooltip reads, loaded once per WowFiles. Layouts measured on the real 3.3.5a files (harness probe ItemTooltipFacts).</summary>
    public sealed class ItemTipDb
    {
        // SpellItemEnchantment.dbc 38 fields: 2-4 type, 5-7 amount, 11-13 arg, 14 name (AC DBCStructure.h:1876-1893)
        const int EnchFields = 38, EnchName = 14;
        // ItemRandomSuffix.dbc 29 fields: 1 Name, 19-23 Enchantment, 24-28 AllocationPct (DBCStructure.h:1254-1261)
        const int SuffixFields = 29, SuffixName = 1, SuffixEnch = 19, SuffixAlloc = 24;
        // ItemRandomProperties.dbc 24 fields: 2-6 Enchantment, 7 Name (DBCStructure.h:1245-1252)
        const int PropFields = 24, PropEnch = 2, PropName = 7;
        // ItemSet.dbc 53 fields: 1 name, 18-27 item ids (28-34 unused), 35-42 spells, 43-50 thresholds (ItemSetEntryfmt "dssssssssssssssssxiiiiiiiiiixxxxxxxiii...")
        const int SetFields = 53, SetName = 1, SetItems = 18, SetItemCount = 10, SetSpells = 35, SetThresholds = 43, SetSpellCount = 8;
        // GemProperties.dbc 5 fields: 1 enchant id, 4 colour mask (GemPropertiesEntry)
        const int GemFields = 5, GemEnchant = 1, GemColor = 4;
        // RandPropPoints.dbc 16 fields: 0 item level, 1-5 epic, 6-10 rare, 11-15 uncommon (RandomPropertiesPointsEntry)
        const int RppFields = 16;
        // ScalingStatDistribution.dbc 22 fields: 1-10 StatMod, 11-20 Modifier, 21 MaxLevel; ScalingStatValues.dbc 24 fields, indexed by field 1 (Level)
        const int SsdFields = 22, SsvFields = 24;
        // ItemLimitCategory.dbc 20 fields: 1 name, 18 quantity, 19 mode (1 = equipped); ItemSubClass.dbc 44 fields: 0 class, 1 sub, 10 name
        const int LimitFields = 20, LimitName = 1, LimitQuantity = 18, LimitMode = 19, SubClassFields = 44, SubClassNameField = 10;
        const int SkillNameField = 3, FactionNameField = 23;          // SkillLine m_displayName_lang 3-18; Faction name 23 ('Stormwind' measured)

        static readonly object CacheLock = new object();
        static WowFiles _cachedFiles;
        static ItemTipDb _cached;

        readonly DbcTable _ench, _suffix, _prop, _set, _rpp, _ssd, _ssv, _limit, _skill, _faction, _classes, _races;
        readonly Dictionary<int, string> _subClassNames = new Dictionary<int, string>();
        readonly Dictionary<int, int> _gemColor = new Dictionary<int, int>();
        public readonly SpellDb Spells;

        public static ItemTipDb Get(WowFiles files)
        {
            if (files == null) return null;
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = new ItemTipDb(files);
                _cachedFiles = files;
                return _cached;
            }
        }

        ItemTipDb(WowFiles files)
        {
            _ench = Load(files, "SpellItemEnchantment", EnchFields);
            _suffix = Load(files, "ItemRandomSuffix", SuffixFields);
            _prop = Load(files, "ItemRandomProperties", PropFields);
            _set = Load(files, "ItemSet", SetFields);
            _rpp = Load(files, "RandPropPoints", RppFields);
            _ssd = Load(files, "ScalingStatDistribution", SsdFields);
            _ssv = Load(files, "ScalingStatValues", SsvFields);
            _limit = Load(files, "ItemLimitCategory", LimitFields);
            _skill = Dbc.Load(files, "SkillLine");
            _faction = Dbc.Load(files, "Faction");
            _classes = Dbc.Load(files, "ChrClasses");
            _races = Dbc.Load(files, "ChrRaces");
            var sc = Load(files, "ItemSubClass", SubClassFields);
            if (sc != null)
                for (int r = 0; r < sc.RecordCount; r++)
                {
                    string n = sc.GetLocalizedString(r, SubClassNameField);
                    if (!string.IsNullOrEmpty(n)) _subClassNames[sc.GetInt(r, 0) << 8 | (sc.GetInt(r, 1) & 0xFF)] = n;
                }
            var gem = Load(files, "GemProperties", GemFields);
            if (gem != null)
                for (int r = 0; r < gem.RecordCount; r++)
                {
                    int e = gem.GetInt(r, GemEnchant);
                    if (e > 0 && !_gemColor.ContainsKey(e)) _gemColor[e] = gem.GetInt(r, GemColor);
                }
            try { Spells = SpellDb.Get(files); } catch (Exception) { Spells = null; }
            WowLog.Info("Item tooltips: SpellItemEnchantment " + Rows(_ench) + ", ItemRandomSuffix " + Rows(_suffix) + ", ItemRandomProperties " + Rows(_prop) + ", ItemSet " + Rows(_set) +
                        ", RandPropPoints " + Rows(_rpp) + ", ScalingStat " + Rows(_ssd) + "/" + Rows(_ssv) + ", GemProperties " + _gemColor.Count + " enchants, ItemSubClass names " + _subClassNames.Count);
        }

        static DbcTable Load(WowFiles files, string name, int fields)
        {
            DbcTable t = null;
            try { t = Dbc.Load(files, name); } catch (Exception) { t = null; }
            if (t != null && t.FieldCount != fields) { WowLog.Warn("Item tooltips: " + name + ".dbc has " + t.FieldCount + " fields, " + fields + " expected - not used"); t = null; }
            return t;
        }

        static string Rows(DbcTable t) { return t != null ? t.RecordCount.ToString() : "missing"; }

        static string Loc(DbcTable t, int id, int field)
        {
            if (t == null || id <= 0) return null;
            int r = t.FindRow(id);
            if (r < 0) return null;
            string s = t.GetLocalizedString(r, field);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        public string EnchantName(int id) { return Loc(_ench, id, EnchName); }
        public string SkillName(int id) { return Loc(_skill, id, SkillNameField); }
        public string FactionName(int id) { return Loc(_faction, id, FactionNameField); }
        public string ClassName(int id) { return Loc(_classes, id, DbcFields.ChrClasses.Name_lang); }
        public string RaceName(int id) { return Loc(_races, id, DbcFields.ChrRaces.Name_lang); }
        public string SubClassName(int cls, int sub) { string n; return _subClassNames.TryGetValue(cls << 8 | (sub & 0xFF), out n) ? n : null; }
        public int GemColorOf(int enchantId) { int c; return _gemColor.TryGetValue(enchantId, out c) ? c : 0; }

        public bool LimitCategory(int id, out string name, out int quantity, out bool equipped)
        {
            name = null; quantity = 0; equipped = false;
            if (_limit == null || id <= 0) return false;
            int r = _limit.FindRow(id);
            if (r < 0) return false;
            name = _limit.GetLocalizedString(r, LimitName);
            quantity = _limit.GetInt(r, LimitQuantity);
            equipped = _limit.GetInt(r, LimitMode) == 1;      // "mode; // 19, 0 = have, 1 = equip" (DBCStructure.h:1240)
            return !string.IsNullOrEmpty(name);
        }

        /// <summary>The random property / suffix name ("of the Bear") and its enchantments (+ allocation for a suffix).</summary>
        public string RandomName(int randomPropertyId)
        {
            if (randomPropertyId > 0) return Loc(_prop, randomPropertyId, PropName);
            if (randomPropertyId < 0) return Loc(_suffix, -randomPropertyId, SuffixName);
            return null;
        }

        public int RandomEnchants(int randomPropertyId, int[] ench, int[] alloc)
        {
            int n = 0;
            if (randomPropertyId > 0 && _prop != null)
            {
                int r = _prop.FindRow(randomPropertyId);
                if (r >= 0) for (int k = 0; k < 5; k++) { ench[n] = _prop.GetInt(r, PropEnch + k); alloc[n] = 0; if (ench[n] > 0) n++; }
            }
            else if (randomPropertyId < 0 && _suffix != null)
            {
                int r = _suffix.FindRow(-randomPropertyId);
                if (r >= 0) for (int k = 0; k < 5; k++) { ench[n] = _suffix.GetInt(r, SuffixEnch + k); alloc[n] = _suffix.GetInt(r, SuffixAlloc + k); if (ench[n] > 0) n++; }
            }
            return n;
        }

        /// <summary>AC ItemEnchantmentMgr.cpp:125-201 GenerateEnchSuffixFactor: RandPropPoints[ItemLevel] by quality and slot group.</summary>
        public int SuffixFactor(int itemLevel, int quality, int invType)
        {
            if (_rpp == null) return 0;
            int r = _rpp.FindRow(itemLevel);
            if (r < 0) return 0;
            int idx;
            switch (invType)
            {
                case 1: case 4: case 5: case 7: case 17: case 20: idx = 0; break;            // head, body, chest, legs, 2h weapon, robe
                case 3: case 6: case 8: case 10: case 12: idx = 1; break;                    // shoulders, waist, feet, hands, trinket
                case 2: case 9: case 11: case 14: case 16: case 23: idx = 2; break;          // neck, wrists, finger, shield, cloak, holdable
                case 13: case 21: case 22: idx = 3; break;                                   // weapon, main hand, off hand
                case 15: case 25: case 26: idx = 4; break;                                   // ranged, thrown, ranged right
                default: return 0;
            }
            switch (quality)
            {
                case 2: return _rpp.GetInt(r, 11 + idx);                                     // UncommonPropertiesPoints
                case 3: return _rpp.GetInt(r, 6 + idx);                                      // RarePropertiesPoints
                case 4: return _rpp.GetInt(r, 1 + idx);                                      // EpicPropertiesPoints
            }
            return 0;
        }

        public bool SetInfo(int setId, out string name, List<int> items, List<int> spells, List<int> thresholds)
        {
            name = null;
            if (_set == null || setId <= 0) return false;
            int r = _set.FindRow(setId);
            if (r < 0) return false;
            name = _set.GetLocalizedString(r, SetName);
            for (int i = 0; i < SetItemCount; i++) { int e = _set.GetInt(r, SetItems + i); if (e > 0 && !items.Contains(e)) items.Add(e); }
            for (int i = 0; i < SetSpellCount; i++)
            {
                int s = _set.GetInt(r, SetSpells + i);
                if (s > 0) { spells.Add(s); thresholds.Add(_set.GetInt(r, SetThresholds + i)); }
            }
            return !string.IsNullOrEmpty(name);
        }

        /// <summary>Heirlooms (AC Player.cpp:6787-6800): the ScalingStatDistribution row and the ScalingStatValues row of min(level, MaxLevel).</summary>
        public bool Scaling(int ssdId, int level, out int ssdRow, out int ssvRow, out int maxLevel)
        {
            ssdRow = ssvRow = -1; maxLevel = 0;
            if (_ssd == null || _ssv == null || ssdId <= 0) return false;
            ssdRow = _ssd.FindRow(ssdId);
            if (ssdRow < 0) return false;
            maxLevel = _ssd.GetInt(ssdRow, 21);
            ssvRow = _ssv.FindRowWhere(1, Math.Max(1, Math.Min(level, maxLevel)));   // ScalingStatValuesfmt "iniii..": the index is field 1, Level (row 80 has ID 200)
            return ssvRow >= 0;
        }

        public int SsdStat(int row, int i) { return _ssd.GetInt(row, 1 + i); }
        public int SsdModifier(int row, int i) { return _ssd.GetInt(row, 11 + i); }
        public int Ssv(int row, int field) { return _ssv.GetInt(row, field); }
    }

    /// <summary>Builds the tooltip text: line 0 = the name (UiTooltip colours it by quality), Unity rich-text colours for the rest.</summary>
    public static class ItemTip
    {
        public const int MaxStats = 10;
        public const string Green = "#00ff00", Red = "#ff2020", Gray = "#808080", Gold = "#ffd200", LightYellow = "#ffff9a";
        const uint FlagConjured = 0x2, FlagHasLoot = 0x4, FlagHeroic = 0x8, FlagUniqueEquippable = 0x80000, FlagAccountBound = 0x08000000;   // ItemTemplate.h:145-178
        const uint FieldFlagSoulbound = 0x1, FieldFlagReadable = 0x200;                                                                     // ItemTemplate.h:107-118
        const int ClassMaskPlayable = 0x5FF;                 // CLASSMASK_ALL_PLAYABLE (SharedDefines.h:142): classes 1-9, 11
        const int RaceMaskPlayable = 0x6FF;                  // the playable races 1-8, 10, 11 (Human .. Troll, Blood Elf, Draenei)
        const int SkillEnchanting = 333;                     // SKILL_ENCHANTING (SharedDefines.h)
        const int CooldownShownMs = 1500;                    // ours: Use: lines show cooldowns longer than the global cooldown (food has a 1 s category cooldown)

        static readonly string[] Standing = { "Hated", "Hostile", "Unfriendly", "Neutral", "Friendly", "Honored", "Revered", "Exalted" };   // FACTION_STANDING_LABEL1..8 (GlobalStrings.lua:3651-3665)

        /// <summary>The name WoW shows: ITEM_SUFFIX_TEMPLATE "%s %s" (GlobalStrings.lua:4529) with the random property / suffix name.</summary>
        public static string Name(ItemTipData d, ItemTipInstance inst, ItemTipDb db)
        {
            string n = d != null ? d.Name ?? "" : "";
            if (inst != null && inst.RandomPropertyId != 0 && db != null)
            {
                string s = db.RandomName(inst.RandomPropertyId);
                if (!string.IsNullOrEmpty(s)) n = n + " " + s;
            }
            return n;
        }

        public static string Build(ItemTipData d, ItemTipInstance inst, ItemTipDb db, ItemTipContext ctx)
        {
            if (ctx == null) ctx = new ItemTipContext();
            ctx.Incomplete = false;
            var sb = new StringBuilder();
            bool owned = inst != null && inst.Owned;
            sb.Append(Name(d, inst, db));
            if ((d.Flags & FlagHeroic) != 0) Line(sb, Green, "Heroic");                                                      // ITEM_HEROIC (4370)
            if ((d.Flags & FlagConjured) != 0) Line(sb, null, "Conjured Item");                                              // ITEM_CONJURED (4345)
            bool account = (d.Flags & FlagAccountBound) != 0;
            if (owned && (inst.FieldFlags & FieldFlagSoulbound) != 0) Line(sb, null, account ? "Account Bound" : "Soulbound");   // ITEM_ACCOUNTBOUND (4337) / ITEM_SOULBOUND (4520)
            else if (account) Line(sb, null, "Binds to account");                                                            // ITEM_BIND_TO_ACCOUNT (4342)
            else if (d.Bonding == 1) Line(sb, null, "Binds when picked up");                                                 // ITEM_BIND_ON_PICKUP (4339)
            else if (d.Bonding == 2) Line(sb, null, "Binds when equipped");                                                  // ITEM_BIND_ON_EQUIP (4338)
            else if (d.Bonding == 3) Line(sb, null, "Binds when used");                                                      // ITEM_BIND_ON_USE (4340)
            else if (d.Bonding == 4 || d.Bonding == 5) Line(sb, null, "Quest Item");                                         // ITEM_BIND_QUEST (4341)
            if (d.MaxCount == 1) Line(sb, null, "Unique");                                                                   // ITEM_UNIQUE (4531)
            else if (d.MaxCount > 1) Line(sb, null, "Unique (" + d.MaxCount + ")");                                         // ITEM_UNIQUE_MULTIPLE (4533)
            else if ((d.Flags & FlagUniqueEquippable) != 0) Line(sb, null, "Unique-Equipped");                               // ITEM_UNIQUE_EQUIPPABLE (4532)
            if (d.ItemLimitCategory > 0 && db != null)
            {
                string ln; int lq; bool leq;
                if (db.LimitCategory(d.ItemLimitCategory, out ln, out lq, out leq))
                    Line(sb, null, (leq ? "Unique-Equipped: " : "Unique: ") + ln + " (" + lq + ")");                        // ITEM_LIMIT_CATEGORY(_MULTIPLE) (4376-4377)
            }
            if (d.StartQuest > 0) Line(sb, null, "This Item Begins a Quest");                                                // ITEM_STARTS_QUEST (4528)

            // slot + type ("Feet   Plate"); the type red without its proficiency skill
            string slot = SlotText(d.InventoryType);
            string sub = (d.Class == 2 || (d.Class == 4 && d.SubClass > 0)) && db != null ? db.SubClassName(d.Class, d.SubClass) : null;
            if (sub != null)
            {
                int prof = ProficiencySkill(d.Class, d.SubClass);
                if (prof > 0 && ctx.SkillValue != null && ctx.SkillValue(prof) < 0) sub = Col(Red, sub);
            }
            if (slot != null || sub != null) Line(sb, null, (slot ?? "") + (slot != null && sub != null ? "   " : "") + (sub ?? ""));
            if (d.ContainerSlots > 0 && d.InventoryType == 18) Line(sb, null, d.ContainerSlots + " Slot Bag");              // CONTAINER_SLOTS "%d Slot %s" (1846), INVTYPE_BAG (4303)

            // heirlooms: ScalingStatDistribution / ScalingStatValues (AC Player.cpp:6787-6880, 6996-7008, 7080-7125)
            int ssdRow = -1, ssvRow = -1, ssdMax = 0;
            bool scaled = d.ScalingStatDistribution > 0 && d.ScalingStatValue > 0 && db != null && db.Scaling(d.ScalingStatDistribution, ctx.Level, out ssdRow, out ssvRow, out ssdMax);
            uint ssvMask = (uint)d.ScalingStatValue;

            // damage / speed / DPS
            float speed = d.Delay / 1000f, dpsSum = 0f;
            for (int i = 0; i < 2; i++)
            {
                float mnF = d.DmgMin[i], mxF = d.DmgMax[i];
                if (i == 0 && scaled)
                {
                    int extraDps = SsvDps(db, ssvRow, ssvMask);
                    if (extraDps > 0)
                    {
                        float avg = extraDps * d.Delay / 1000f, mod = (ssvMask & 0x400) != 0 || (ssvMask & 0x1000) != 0 ? 0.2f : 0.3f;
                        mnF = (1f - mod) * avg; mxF = (1f + mod) * avg;
                    }
                }
                if (mxF <= 0f) continue;
                int mn = (int)mnF, mx = (int)mxF;
                string school = d.DmgType[i] > 0 ? SchoolName(d.DmgType[i]) : null;
                string text;
                if (i == 0) text = school == null ? mn + " - " + mx + " Damage" : mn + " - " + mx + " " + school + " Damage";      // DAMAGE_TEMPLATE(_WITH_SCHOOL) (1920-1921)
                else text = school == null ? "+ " + mn + " - " + mx + " Damage" : "+" + mn + " - " + mx + " " + school + " Damage"; // PLUS_DAMAGE_TEMPLATE(_WITH_SCHOOL) (5697-5698)
                if (i == 0 && speed > 0f) text += "   Speed " + speed.ToString("0.00");                                       // SPEED (6901)
                Line(sb, null, text);
                dpsSum += (mnF + mxF) / 2f;
            }
            if (dpsSum > 0f && speed > 0f) Line(sb, null, "(" + (dpsSum / speed).ToString("0.0") + " damage per second)");   // DPS_TEMPLATE (2005)

            int armor = d.Armor;
            if (scaled)
            {
                int sa = SsvArmor(db, ssvRow, ssvMask);
                if (sa > 0) armor = sa;                                                                                       // "if (proto->ScalingStatValue > 0 || ssvarmor < proto->Armor) armor = ssvarmor"
            }
            if (armor > 0) Line(sb, null, armor + " Armor");                                                                 // ARMOR_TEMPLATE (583)
            if (d.Block > 0) Line(sb, null, d.Block + " Block");                                                             // SHIELD_BLOCK_TEMPLATE (6242)

            // the stats: the template's or the heirloom's (white primaries, green Equip ratings below)
            var statType = new List<int>(); var statValue = new List<int>();
            if (scaled)
            {
                int mult = SsvStatMultiplier(db, ssvRow, ssvMask);
                for (int i = 0; i < MaxStats; i++)
                {
                    int t = db.SsdStat(ssdRow, i);
                    if (t < 0) continue;
                    int v = (int)((long)mult * db.SsdModifier(ssdRow, i) / 10000);                                           // "(ssv->getssdMultiplier(ScalingStatValue) * ssd->Modifier[i]) / 10000"
                    if (v != 0) { statType.Add(t); statValue.Add(v); }
                }
            }
            else for (int i = 0; i < d.StatCount && i < MaxStats; i++) if (d.StatValue[i] != 0) { statType.Add(d.StatType[i]); statValue.Add(d.StatValue[i]); }
            for (int i = 0; i < statType.Count; i++)
            {
                string s = PrimaryStat(statType[i], statValue[i]);
                if (s != null) Line(sb, null, s);
            }

            // the random property / suffix ("of the Bear"): its enchantments' names, "$i" = AllocationPct * suffix factor / 10000
            // (AC PlayerStorage.cpp ApplyEnchantment ITEM_ENCHANTMENT_TYPE_STAT: "uint32((item_rand_suffix->AllocationPct[k] * item->GetItemSuffixFactor()) / 10000)")
            bool randomKnown = inst != null && inst.RandomPropertyId != 0;
            if (randomKnown && db != null)
            {
                var ench = new int[5]; var alloc = new int[5];
                int n = db.RandomEnchants(inst.RandomPropertyId, ench, alloc);
                int factor = inst.SuffixFactor > 0 ? inst.SuffixFactor : db.SuffixFactor(d.ItemLevel, d.Quality, d.InventoryType);
                for (int k = 0; k < n; k++)
                {
                    string en = db.EnchantName(ench[k]);
                    if (string.IsNullOrEmpty(en)) continue;
                    if (en.IndexOf("$i", StringComparison.Ordinal) >= 0) en = en.Replace("$i", ((int)((long)alloc[k] * factor / 10000)).ToString());
                    Line(sb, null, en);
                }
            }

            for (int i = 0; i < 6; i++)
                if (d.Res[i] != 0) Line(sb, null, Sign(d.Res[i]) + Math.Abs(d.Res[i]) + " " + SchoolName(i + 1) + " Resistance");  // ITEM_RESIST_SINGLE "%c%d %s Resistance" (4510)

            // enchantments: permanent, temporary (+ time left), then the sockets and the socket bonus
            if (inst != null && db != null)
            {
                string perm = inst.Enchant[0] > 0 ? db.EnchantName(inst.Enchant[0]) : null;
                if (perm != null) Line(sb, Green, perm);
                string temp = inst.Enchant[1] > 0 ? db.EnchantName(inst.Enchant[1]) : null;
                if (temp != null) Line(sb, Green, inst.TempEnchantMs > 0 ? temp + " (" + TimeLeft(inst.TempEnchantMs / 1000, " hours") + ")" : temp);   // ITEM_ENCHANT_TIME_LEFT_* "%s (%d min)" (4366-4369)
            }
            int sockets = 0;
            bool allFit = true;
            for (int i = 0; i < 3; i++)
            {
                int color = d.SocketColor[i];
                if (color == 0) continue;
                sockets++;
                int gem = inst != null ? inst.Enchant[2 + i] : 0;
                string gn = gem > 0 && db != null ? db.EnchantName(gem) : null;
                if (gn != null) Line(sb, null, gn);
                else Line(sb, Gray, SocketText(color));
                if (gem <= 0 || db == null || (db.GemColorOf(gem) & color) == 0) allFit = false;                           // Item::GemsFitSockets (Item.cpp:971-1006)
            }
            if (inst != null && inst.Enchant[6] > 0 && sockets < 3)                                                          // PRISMATIC_ENCHANTMENT_SLOT: a belt buckle's extra socket
            {
                int gem = inst.Enchant[2 + sockets];
                string gn = gem > 0 && db != null ? db.EnchantName(gem) : null;
                Line(sb, gn != null ? null : Gray, gn ?? "Prismatic Socket");                                                // EMPTY_SOCKET_NO_COLOR (2766)
            }
            if (d.SocketBonus > 0 && db != null)
            {
                string bn = db.EnchantName(d.SocketBonus);
                if (bn != null) Line(sb, sockets > 0 && allFit ? Green : Gray, "Socket Bonus: " + bn);                      // ITEM_SOCKET_BONUS (4518)
            }
            if ((d.RandomProperty != 0 || d.RandomSuffix != 0) && !randomKnown) Line(sb, Green, "<Random enchantment>");    // ITEM_RANDOM_ENCHANT (4500)

            int shownMax = owned && inst.MaxDurability > 0 ? inst.MaxDurability : d.MaxDurability;
            if (shownMax > 0) Line(sb, null, "Durability " + (owned && inst.MaxDurability > 0 ? inst.Durability : shownMax) + " / " + shownMax);   // DURABILITY_TEMPLATE (2098)
            int durLeft = owned && inst.DurationLeft > 0 ? inst.DurationLeft : d.Duration;
            if (durLeft > 0) Line(sb, null, "Duration: " + TimeLeft(durLeft, " hrs"));                                              // ITEM_DURATION_* "Duration: %d min" (4361-4364)

            // requirements, red when not met
            if (d.AllowableClass > 0 && (d.AllowableClass & ClassMaskPlayable) != ClassMaskPlayable && db != null)
            {
                var names = new StringBuilder();
                for (int c = 1; c <= 11; c++)
                {
                    if ((ClassMaskPlayable & (1 << (c - 1))) == 0 || (d.AllowableClass & (1 << (c - 1))) == 0) continue;
                    string cn = db.ClassName(c);
                    if (cn == null) continue;
                    if (names.Length > 0) names.Append(", ");
                    names.Append(cn);
                }
                bool ok = ctx.ClassId <= 0 || (d.AllowableClass & (1 << (ctx.ClassId - 1))) != 0;
                if (names.Length > 0) Line(sb, ok ? null : Red, "Classes: " + names);                                         // ITEM_CLASSES_ALLOWED (4344)
            }
            if (d.AllowableRace > 0 && (d.AllowableRace & RaceMaskPlayable) != RaceMaskPlayable && db != null)
            {
                var names = new StringBuilder();
                for (int r = 1; r <= 11; r++)
                {
                    if ((RaceMaskPlayable & (1 << (r - 1))) == 0 || (d.AllowableRace & (1 << (r - 1))) == 0) continue;
                    string rn = db.RaceName(r);
                    if (rn == null) continue;
                    if (names.Length > 0) names.Append(", ");
                    names.Append(rn);
                }
                bool ok = ctx.Race <= 0 || (d.AllowableRace & (1 << (ctx.Race - 1))) != 0;
                if (names.Length > 0) Line(sb, ok ? null : Red, "Races: " + names);                                           // ITEM_RACES_ALLOWED (4499)
            }
            if (scaled) Line(sb, null, "Requires level " + Math.Max(1, d.RequiredLevel) + " to " + ssdMax + " (" + ctx.Level + ")");   // ITEM_LEVEL_RANGE_CURRENT (4375)
            else if (d.RequiredLevel > 0) Line(sb, ctx.Level < d.RequiredLevel ? Red : null, "Requires Level " + d.RequiredLevel);   // ITEM_MIN_LEVEL (4380)
            if (d.RequiredSkill > 0 && db != null)
            {
                string sn = db.SkillName(d.RequiredSkill);
                if (sn != null)
                {
                    bool ok = ctx.SkillValue == null || ctx.SkillValue(d.RequiredSkill) >= Math.Max(1, d.RequiredSkillRank);
                    Line(sb, ok ? null : Red, d.RequiredSkillRank > 0 ? "Requires " + sn + " (" + d.RequiredSkillRank + ")" : "Requires " + sn);   // ITEM_MIN_SKILL (4381) / ITEM_REQ_SKILL (4508)
                }
            }
            if (d.RequiredSpell > 0 && db != null && db.Spells != null)
            {
                var rs = db.Spells.Find(d.RequiredSpell);
                if (rs != null && !string.IsNullOrEmpty(rs.Name))
                    Line(sb, ctx.KnowsSpell == null || ctx.KnowsSpell(d.RequiredSpell) ? null : Red, "Requires " + rs.Name);   // ITEM_REQ_SKILL "Requires %s" (4508)
            }
            if (d.RequiredReputationFaction > 0 && db != null)
            {
                string fn = db.FactionName(d.RequiredReputationFaction);
                int rank = Math.Max(0, Math.Min(7, d.RequiredReputationRank));
                if (fn != null)
                {
                    int have = ctx.ReputationRank != null ? ctx.ReputationRank(d.RequiredReputationFaction) : -1;
                    Line(sb, have >= 0 && have < rank ? Red : null, "Requires " + fn + " - " + Standing[rank]);           // ITEM_REQ_REPUTATION "Requires %s - %s" (4507)
                }
            }

            // green Equip: lines - the ratings, the heirloom's spell power, then the item's spells
            for (int i = 0; i < statType.Count; i++)
            {
                string s = RatingStat(statType[i], statValue[i]);
                if (s != null) Line(sb, Green, "Equip: " + s);                                                                // ITEM_SPELL_TRIGGER_ONEQUIP (4525)
            }
            if (scaled && (ssvMask & 0x8000) != 0 && db.Ssv(ssvRow, 16) > 0) Line(sb, Green, "Equip: Increases spell power by " + db.Ssv(ssvRow, 16) + ".");   // getSpellBonus -> ITEM_MOD_SPELL_POWER (4474)
            for (int i = 0; i < 5; i++)
            {
                if (d.SpellId[i] <= 0) continue;
                string trigger;
                bool use = false;
                switch (d.SpellTrigger[i])
                {
                    case 0: case 5: trigger = "Use:"; use = true; break;                                                     // ITEM_SPELL_TRIGGER_ONUSE (4527): ON_USE, NO_DELAY_USE
                    case 4: trigger = "Use:"; break;                                                                         // SOULSTONE
                    case 1: trigger = "Equip:"; break;                                                                       // ITEM_SPELL_TRIGGER_ONEQUIP (4525)
                    case 2: trigger = "Chance on hit:"; break;                                                               // ITEM_SPELL_TRIGGER_ONPROC (4526)
                    default: trigger = null; break;                                                                          // 6 = learn: the recipe's own lines
                }
                if (trigger == null) continue;
                string desc = ctx.SpellDescription != null ? ctx.SpellDescription(d.SpellId[i]) : null;
                if (string.IsNullOrEmpty(desc)) continue;
                string line = trigger + " " + desc;
                int cd = d.SpellCooldown[i] > 0 ? d.SpellCooldown[i] : d.SpellCategoryCooldown[i];
                if (use && cd > CooldownShownMs) line += " " + CooldownText(cd / 1000);                                     // ITEM_COOLDOWN_TOTAL_* "(%d Min Cooldown)" (4351-4355)
                Line(sb, Green, line);
                int baseCh = d.SpellCharges[i];
                if (Math.Abs(baseCh) > 1)
                {
                    int ch = Math.Abs(inst != null && inst.HasCharges ? inst.SpellCharges[i] : baseCh);
                    Line(sb, null, ch == 0 ? "No charges" : ch + (ch == 1 ? " Charge" : " Charges"));                       // ITEM_SPELL_CHARGES "%d |4Charge:Charges;" (4521) / _NONE (4522)
                }
            }
            if (!string.IsNullOrEmpty(d.UseText)) Line(sb, Green, d.UseText);

            // the item set: name (n/N), the pieces, the bonuses by threshold
            if (d.ItemSet > 0 && db != null)
            {
                string setName;
                var items = new List<int>(); var spells = new List<int>(); var thr = new List<int>();
                if (db.SetInfo(d.ItemSet, out setName, items, spells, thr))
                {
                    int have = 0;
                    var equipped = new bool[items.Count];
                    for (int i = 0; i < items.Count; i++) { equipped[i] = ctx.IsEquipped != null && ctx.IsEquipped(items[i]); if (equipped[i]) have++; }
                    Line(sb, null, "");
                    Line(sb, Gold, setName + " (" + have + "/" + items.Count + ")");                                         // ITEM_SET_NAME "%s (%d/%d)" (4513)
                    for (int i = 0; i < items.Count; i++)
                    {
                        string iname = ctx.ItemName != null ? ctx.ItemName(items[i]) : null;
                        if (iname == null) { ctx.Incomplete = true; continue; }
                        if (iname.Length == 0) continue;                                                         // the server has no such item
                        Line(sb, equipped[i] ? LightYellow : Gray, "  " + iname);
                    }
                    var order = new List<int>();
                    for (int i = 0; i < spells.Count; i++) order.Add(i);
                    order.Sort((a, b) => thr[a] != thr[b] ? thr[a].CompareTo(thr[b]) : a.CompareTo(b));
                    bool first = true;
                    foreach (int i in order)
                    {
                        string desc = ctx.SpellDescription != null ? ctx.SpellDescription(spells[i]) : null;
                        if (string.IsNullOrEmpty(desc)) continue;
                        if (first) { Line(sb, null, ""); first = false; }
                        if (have >= thr[i]) Line(sb, Green, "Set: " + desc);                                                // ITEM_SET_BONUS (4511)
                        else Line(sb, Gray, "(" + thr[i] + ") Set: " + desc);                                               // ITEM_SET_BONUS_GRAY (4512)
                    }
                }
            }

            if (!string.IsNullOrEmpty(d.Description)) Line(sb, Gold, "\"" + d.Description + "\"");
            if (inst != null && inst.Creator != 0)
            {
                string cn = ctx.PlayerName != null ? ctx.PlayerName(inst.Creator) : null;
                if (cn != null) Line(sb, Green, "<Made by " + cn + ">");                                                     // ITEM_CREATED_BY (4356)
                else ctx.Incomplete = true;
            }
            if ((d.Flags & FlagHasLoot) != 0) Line(sb, Green, "<Right Click to Open>");                                      // ITEM_OPENABLE (4486)
            if (d.PageText > 0 || (owned && (inst.FieldFlags & FieldFlagReadable) != 0)) Line(sb, Green, "<Right Click to Read>");   // ITEM_READABLE (4501)
            if (d.RequiredDisenchantSkill > 0 && ctx.SkillValue != null && db != null)
            {
                int ench = ctx.SkillValue(SkillEnchanting);
                string en = db.SkillName(SkillEnchanting);
                if (ench >= 0 && en != null) Line(sb, ench < d.RequiredDisenchantSkill ? Red : null, "Disenchanting requires " + en + " (" + d.RequiredDisenchantSkill + ")");   // ITEM_DISENCHANT_MIN_SKILL (4359)
            }
            if (d.SellPrice > 0) Line(sb, null, "Sell Price: " + MoneyWords(d.SellPrice));                                   // SELL_PRICE (6207) + GOLD/SILVER/COPPER_AMOUNT
            return sb.ToString();
        }

        static void Line(StringBuilder sb, string color, string s)
        {
            sb.Append('\n');
            if (color == null || s.Length == 0) sb.Append(s);
            else sb.Append("<color=").Append(color).Append('>').Append(s).Append("</color>");
        }

        static string Col(string color, string s) { return "<color=" + color + ">" + s + "</color>"; }
        static string Sign(int v) { return v < 0 ? "-" : "+"; }

        /// <summary>INVTYPE_* (GlobalStrings.lua:4301-4329) by InventoryType; null for 0 (not equippable).</summary>
        public static string SlotText(int invType)
        {
            switch (invType)
            {
                case 1: return "Head"; case 2: return "Neck"; case 3: return "Shoulder"; case 4: return "Shirt"; case 5: return "Chest";
                case 6: return "Waist"; case 7: return "Legs"; case 8: return "Feet"; case 9: return "Wrist"; case 10: return "Hands";
                case 11: return "Finger"; case 12: return "Trinket"; case 13: return "One-Hand"; case 14: return "Off Hand"; case 15: return "Ranged";
                case 16: return "Back"; case 17: return "Two-Hand"; case 18: return "Bag"; case 19: return "Tabard"; case 20: return "Chest";
                case 21: return "Main Hand"; case 22: return "Off Hand"; case 23: return "Held In Off-hand"; case 24: return "Ammo"; case 25: return "Thrown";
                case 26: return "Ranged"; case 27: return "Quiver"; case 28: return "Relic";
            }
            return null;
        }

        /// <summary>ItemTemplate::GetSkill (AC ItemTemplate.h:784-814): item_weapon_skills / item_armor_skills (SharedDefines.h SKILL_*).</summary>
        static int ProficiencySkill(int cls, int sub)
        {
            if (cls == 2)
            {
                int[] w = { 44, 172, 45, 46, 54, 160, 229, 43, 55, 0, 136, 0, 0, 473, 0, 173, 176, 253, 226, 228, 356 };
                return sub >= 0 && sub < w.Length ? w[sub] : 0;
            }
            if (cls == 4)
            {
                int[] a = { 0, 415, 414, 413, 293, 0, 433, 0, 0, 0, 0 };
                return sub >= 0 && sub < a.Length ? a[sub] : 0;
            }
            return 0;
        }

        /// <summary>DAMAGE_SCHOOL2..7 (GlobalStrings.lua:1911-1916) = schools 1 Holy .. 6 Arcane.</summary>
        static string SchoolName(int school)
        {
            switch (school)
            {
                case 1: return "Holy"; case 2: return "Fire"; case 3: return "Nature"; case 4: return "Frost"; case 5: return "Shadow"; case 6: return "Arcane";
            }
            return "Unknown";                                                                                                 // STRING_SCHOOL_UNKNOWN (7418)
        }

        /// <summary>EMPTY_SOCKET_* (GlobalStrings.lua:2764-2768) by SocketColor (ItemTemplate.h:244-250).</summary>
        static string SocketText(int color)
        {
            if ((color & 1) != 0) return "Meta Socket";
            if ((color & 2) != 0) return "Red Socket";
            if ((color & 4) != 0) return "Yellow Socket";
            if ((color & 8) != 0) return "Blue Socket";
            return "Prismatic Socket";
        }

        /// <summary>ITEM_MOD_MANA/HEALTH/AGILITY/STRENGTH/INTELLECT/SPIRIT/STAMINA "%c%d X" (GlobalStrings.lua:4450, 4426, 4383, 4480, 4448, 4476, 4478).</summary>
        public static string PrimaryStat(int type, int value)
        {
            string n;
            switch (type)
            {
                case 0: n = "Mana"; break; case 1: n = "Health"; break; case 3: n = "Agility"; break; case 4: n = "Strength"; break;
                case 5: n = "Intellect"; break; case 6: n = "Spirit"; break; case 7: n = "Stamina"; break;
                default: return null;
            }
            return Sign(value) + Math.Abs(value) + " " + n;
        }

        /// <summary>The green "Equip:" stat lines, ITEM_MOD_* texts (GlobalStrings.lua:4385-4474) by ItemModType (ItemTemplate.h:25-71).</summary>
        public static string RatingStat(int type, int v)
        {
            switch (type)
            {
                case 12: return "Increases defense rating by " + v + ".";
                case 13: return "Increases your dodge rating by " + v + ".";
                case 14: return "Increases your parry rating by " + v + ".";
                case 15: return "Increases your shield block rating by " + v + ".";
                case 16: return "Improves melee hit rating by " + v + ".";
                case 17: return "Improves ranged hit rating by " + v + ".";
                case 18: return "Improves spell hit rating by " + v + ".";
                case 19: return "Improves melee critical strike rating by " + v + ".";
                case 20: return "Improves ranged critical strike rating by " + v + ".";
                case 21: return "Improves spell critical strike rating by " + v + ".";
                case 22: return "Improves melee hit avoidance rating by " + v + ".";
                case 23: return "Improves ranged hit avoidance rating by " + v + ".";
                case 24: return "Improves spell hit avoidance rating by " + v + ".";
                case 25: return "Improves melee critical avoidance rating by " + v + ".";
                case 26: return "Improves ranged critical avoidance rating by " + v + ".";
                case 27: return "Improves spell critical avoidance rating by " + v + ".";
                case 28: return "Improves melee haste rating by " + v + ".";
                case 29: return "Improves ranged haste rating by " + v + ".";
                case 30: return "Improves spell haste rating by " + v + ".";
                case 31: return "Improves hit rating by " + v + ".";
                case 32: return "Improves critical strike rating by " + v + ".";
                case 33: return "Improves hit avoidance rating by " + v + ".";
                case 34: return "Improves critical avoidance rating by " + v + ".";
                case 35: return "Improves your resilience rating by " + v + ".";
                case 36: return "Improves haste rating by " + v + ".";
                case 37: return "Increases your expertise rating by " + v + ".";
                case 38: return "Increases attack power by " + v + ".";
                case 39: return "Increases ranged attack power by " + v + ".";
                case 40: return "Increases attack power by " + v + " in Cat, Bear, Dire Bear, and Moonkin forms only.";      // ITEM_MOD_FERAL_ATTACK_POWER (4416)
                case 41: return "Increases healing done by magical spells and effects by up to " + v + ".";
                case 42: return "Increases damage done by magical spells and effects by up to " + v + ".";
                case 43: return "Restores " + v + " mana per 5 sec.";
                case 44: return "Increases your armor penetration rating by " + v + ".";
                case 45: return "Increases spell power by " + v + ".";
                case 46: return "Restores " + v + " health per 5 sec.";
                case 47: return "Increases spell penetration by " + v + ".";
                case 48: return "Increases the block value of your shield by " + v + ".";
            }
            return null;
        }

        // ScalingStatValuesEntry (AC DBCStructure.h:1516-1595): 2-5 ssdMultiplier, 6-9 armorMod, 10-15 dpsMod, 16 spellPower,
        // 17 ssdMultiplier2, 18 ssdMultiplier3, 19-23 armorMod2
        static int SsvStatMultiplier(ItemTipDb db, int row, uint mask)
        {
            if ((mask & 0x4001F) == 0) return 0;
            if ((mask & 0x1) != 0) return db.Ssv(row, 2);
            if ((mask & 0x2) != 0) return db.Ssv(row, 3);
            if ((mask & 0x4) != 0) return db.Ssv(row, 4);
            if ((mask & 0x8) != 0) return db.Ssv(row, 17);
            if ((mask & 0x10) != 0) return db.Ssv(row, 5);
            if ((mask & 0x40000) != 0) return db.Ssv(row, 18);
            return 0;
        }

        static int SsvArmor(ItemTipDb db, int row, uint mask)
        {
            if ((mask & 0x00F801E0) == 0) return 0;
            if ((mask & 0x20) != 0) return db.Ssv(row, 6);
            if ((mask & 0x40) != 0) return db.Ssv(row, 7);
            if ((mask & 0x80) != 0) return db.Ssv(row, 8);
            if ((mask & 0x100) != 0) return db.Ssv(row, 9);
            if ((mask & 0x80000) != 0) return db.Ssv(row, 19);
            if ((mask & 0x100000) != 0) return db.Ssv(row, 20);
            if ((mask & 0x200000) != 0) return db.Ssv(row, 21);
            if ((mask & 0x400000) != 0) return db.Ssv(row, 22);
            if ((mask & 0x800000) != 0) return db.Ssv(row, 23);
            return 0;
        }

        static int SsvDps(ItemTipDb db, int row, uint mask)
        {
            if ((mask & 0x7E00) == 0) return 0;
            if ((mask & 0x200) != 0) return db.Ssv(row, 10);
            if ((mask & 0x400) != 0) return db.Ssv(row, 11);
            if ((mask & 0x800) != 0) return db.Ssv(row, 12);
            if ((mask & 0x1000) != 0) return db.Ssv(row, 13);
            if ((mask & 0x2000) != 0) return db.Ssv(row, 14);
            if ((mask & 0x4000) != 0) return db.Ssv(row, 15);
            return 0;
        }

        /// <summary>"%d |4day:days;" / "%d |4hour:hrs;" (duration) or "|4hour:hours;" (enchant) / "%d min" / "%d sec" (ITEM_DURATION_* 4361-4364, ITEM_ENCHANT_TIME_LEFT_* 4366-4369): the largest unit.</summary>
        static string TimeLeft(int s, string hoursWord)
        {
            if (s >= 86400) { int n = s / 86400; return n + (n == 1 ? " day" : " days"); }
            if (s >= 3600) { int n = s / 3600; return n + (n == 1 ? " hour" : hoursWord); }
            if (s >= 60) return (s / 60) + " min";
            return Math.Max(0, s) + " sec";
        }

        /// <summary>ITEM_COOLDOWN_TOTAL_DAYS / _HOURS / _MIN / _SEC "(%d Min Cooldown)" (GlobalStrings.lua:4352-4355): the largest unit.</summary>
        static string CooldownText(int s)
        {
            if (s >= 86400) { int n = s / 86400; return "(" + n + (n == 1 ? " Day" : " Days") + " Cooldown)"; }
            if (s >= 3600) { int n = s / 3600; return "(" + n + (n == 1 ? " Hour" : " Hours") + " Cooldown)"; }
            if (s >= 60) return "(" + (s / 60) + " Min Cooldown)";
            return "(" + s + " Sec Cooldown)";
        }

        /// <summary>GOLD_AMOUNT / SILVER_AMOUNT / COPPER_AMOUNT "%d Gold" (GlobalStrings.lua:3843, 6321, 1860), joined like the offline tooltip.</summary>
        public static string MoneyWords(int copper)
        {
            int g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var sb = new StringBuilder();
            if (g > 0) sb.Append(g).Append(" Gold");
            if (s > 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(s).Append(" Silver"); }
            if (c > 0 || sb.Length == 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(c).Append(" Copper"); }
            return sb.ToString();
        }
    }
}
