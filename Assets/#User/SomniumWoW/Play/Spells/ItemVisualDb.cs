// iteration 11 - owner spellfx: weapon enchant glows (proposal-spellfx.md s1.3 / s4 / s6.1). Port of ItemVisualDbProto
// (P/harness/Probes/D11/spellfx/SpellFxProtos.cs, checked on the real files by the SfxProtos probe).
// SpellItemEnchantment.dbc (38 fields, ItemVisual = field 31, AC DBCStructure.h SpellItemEnchantmentEntry) -> ItemVisuals.dbc (6 fields: ID + 5
// ItemVisualEffects ids, one per weapon attachment id 0..4) -> ItemVisualEffects.dbc (2 fields: ID, model path). ItemDisplayInfo.dbc field 23 =
// the item's own visual (DbcFields.ItemDisplayInfo.ItemVisual). Pure C#.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Spells
{
    public sealed class ItemVisualDb
    {
        public const int EnchantFields = 38, VisualFields = 6, EffectFields = 2, EnchantVisualField = 31;
        static readonly object CacheLock = new object();
        static WowFiles _cachedFiles;
        static ItemVisualDb _cached;
        static readonly ItemVisualDb Empty = new ItemVisualDb(null);

        readonly WowFiles _files;
        readonly DbcTable _ench, _vis, _eff, _disp;
        readonly object _lock = new object();
        readonly Dictionary<int, string[]> _models = new Dictionary<int, string[]>();
        readonly Dictionary<long, int> _for = new Dictionary<long, int>();
        Dictionary<int, int> _enchantSpell;
        static readonly string[] NoModels = new string[5];

        /// <summary>Why the tables are not used (null = fine): a missing table or an unexpected field count (logged once).</summary>
        public string Problem { get; private set; }

        /// <summary>The cached tables of the player's install (never null; no files = an instance that answers 0 everywhere).</summary>
        public static ItemVisualDb Get(WowFiles files)
        {
            if (files == null) return Empty;
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = new ItemVisualDb(files);
                _cachedFiles = files;
                return _cached;
            }
        }

        ItemVisualDb(WowFiles files)
        {
            _files = files;
            if (files == null) { Problem = "no game files"; return; }
            try
            {
                _ench = Dbc.Load(files, "SpellItemEnchantment"); _vis = Dbc.Load(files, "ItemVisuals"); _eff = Dbc.Load(files, "ItemVisualEffects"); _disp = Dbc.Load(files, "ItemDisplayInfo");
                if (_ench == null || _vis == null || _eff == null || _disp == null) Problem = "a table is missing (SpellItemEnchantment / ItemVisuals / ItemVisualEffects / ItemDisplayInfo)";
                else if (_ench.FieldCount != EnchantFields || _vis.FieldCount != VisualFields || _eff.FieldCount != EffectFields)
                    Problem = "field counts " + _ench.FieldCount + "/" + _vis.FieldCount + "/" + _eff.FieldCount + " (expected 38/6/2)";
            }
            catch (Exception e) { Problem = e.Message; }
            if (Problem != null) WowLog.Warn("ItemVisualDb: weapon enchant glows are off - " + Problem);
            else WowLog.Info("ItemVisualDb: " + _ench.RecordCount + " enchants, " + _vis.RecordCount + " item visuals, " + _eff.RecordCount + " visual models (weapon enchant glows)");
        }

        /// <summary>ItemVisuals id of a SpellItemEnchantment id (field 31), 0 = none.</summary>
        public int VisualOfEnchant(int enchantId)
        {
            if (Problem != null || enchantId <= 0) return 0;
            int r = _ench.FindRow(enchantId);
            return r >= 0 ? _ench.GetInt(r, EnchantVisualField) : 0;
        }

        /// <summary>The item's own glow (ItemDisplayInfo field 23), 0 = none.</summary>
        public int VisualOfDisplay(int displayId)
        {
            if (Problem != null || displayId <= 0) return 0;
            int r = _disp.FindRow(displayId);
            return r >= 0 ? _disp.GetInt(r, DbcFields.ItemDisplayInfo.ItemVisual) : 0;
        }

        /// <summary>packed = permanent | temporary &lt;&lt; 16 (PLAYER_VISIBLE_ITEM_n_ENCHANTMENT, PlayerStorage.cpp:2968-2983): the temporary
        /// enchant's visual wins (poisons / imbues / stones), then the permanent one's, then the item's own (decision D4, ours). Only visuals that
        /// resolve to at least one model count. Cached (called per frame by the views).</summary>
        public int VisualFor(int packed, int displayId)
        {
            if (Problem != null || (packed == 0 && displayId <= 0)) return 0;
            long key = ((long)(uint)packed << 32) | (uint)displayId;
            lock (_lock) { int c; if (_for.TryGetValue(key, out c)) return c; }
            int v = 0;
            int t = VisualOfEnchant((packed >> 16) & 0xFFFF);
            if (t > 0 && HasModels(t)) v = t;
            else
            {
                int p = VisualOfEnchant(packed & 0xFFFF);
                if (p > 0 && HasModels(p)) v = p;
                else
                {
                    int own = VisualOfDisplay(displayId);
                    if (own > 0 && HasModels(own)) v = own;
                }
            }
            lock (_lock) { if (_for.Count > 4096) _for.Clear(); _for[key] = v; }
            return v;
        }

        /// <summary>The 5 model paths by weapon attachment id 0..4 (null = none: 0, or an id that is no ItemVisualEffects row - the measured
        /// garbage values of visuals 28 / 151 / 172 / 173). The array is shared: callers must not modify it.</summary>
        public string[] Models(int visualId)
        {
            if (Problem != null || visualId <= 0) return NoModels;
            lock (_lock) { string[] c; if (_models.TryGetValue(visualId, out c)) return c; }
            var m = new string[5];
            int r = _vis.FindRow(visualId);
            if (r >= 0)
                for (int s = 0; s < 5; s++)
                {
                    int e = _vis.GetInt(r, 1 + s);
                    if (e <= 0) continue;
                    int er = _eff.FindRow(e);
                    if (er < 0) continue;
                    string path = _eff.GetString(er, 1);
                    if (!string.IsNullOrEmpty(path)) m[s] = path;
                }
            lock (_lock) { _models[visualId] = m; }
            return m;
        }

        public bool HasModels(int visualId) { foreach (var p in Models(visualId)) if (p != null) return true; return false; }

        /// <summary>The spell that applies an enchant (the buff row's weapon imbue icon): the first Spell.dbc row with ENCHANT_ITEM 53 /
        /// ENCHANT_ITEM_TEMPORARY 54 whose MiscValue is the enchant (a lazy reverse map, built once). 0 = unknown.</summary>
        public int EnchantSpell(int enchantId)
        {
            if (_files == null || enchantId <= 0) return 0;
            Dictionary<int, int> map;
            lock (_lock) map = _enchantSpell;
            if (map == null)
            {
                map = new Dictionary<int, int>();
                var t = Dbc.Load(_files, "Spell");
                if (t != null && t.FieldCount == SpellFields.Spell.FieldCount)
                    for (int r = 0; r < t.RecordCount; r++)
                        for (int e = 0; e < 3; e++)
                        {
                            int eff = t.GetInt(r, SpellFields.Spell.Effect + e);
                            if (eff != SpellEffect.EnchantItem && eff != SpellEffect.EnchantItemTemporary) continue;
                            int misc = t.GetInt(r, SpellFields.Spell.EffectMiscValue + e);
                            if (misc > 0 && !map.ContainsKey(misc)) map[misc] = t.GetId(r);
                        }
                lock (_lock) _enchantSpell = map;
                WowLog.Info("ItemVisualDb: " + map.Count + " enchants map to the spell that applies them (buff row weapon imbue icon)");
            }
            int id;
            return map.TryGetValue(enchantId, out id) ? id : 0;
        }
    }
}
