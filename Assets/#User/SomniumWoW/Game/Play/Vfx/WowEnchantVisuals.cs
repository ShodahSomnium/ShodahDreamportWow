// iteration 11 - owner spellfx: weapon enchant glows (proposal-spellfx.md s1.3 / s6.5) - Crusader's white glow, Fiery's flame, poisons' drip,
// shaman imbues, death knight runes ... drawn as in WoW from the player's own files: SpellItemEnchantment.dbc field 31 -> ItemVisuals.dbc (5 slots)
// -> ItemVisualEffects.dbc model (Play/Spells/ItemVisualDb.cs). Slot i goes on the weapon model's M2 attachment id i (measured with SfxAttach:
// Sword_2H_Ashbringer / Knife_1H_Blood_B_01 / Sword_1H_Short_A_01 carry ids 0..4 from grip to tip, some maces only 2..4, bows none -> nothing,
// as in WoW). The slot -> attachment reading is OURS (the 5 ItemVisuals slots match the 5 attachment ids; logged once per visual).
// Called by WowEntityView (NPC / WoW-player weapons, hook 6) and GearItem.SetEnchantVisual (the Somnium avatars' held weapons).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    public static class WowEnchantVisuals
    {
        const string FxName = "Enchant fx";
        static readonly HashSet<int> _logged = new HashSet<int>();
        static bool _hooked, _rulesLogged;
        static WowFiles _lastFiles;

        /// <summary>What a weapon shows (no rebuild for the same id) and the id it was asked for (a settings change re-applies it).
        /// Kept in a table by weapon object (a destroyed weapon drops out at the next sweep) - no component is added to the weapon.</summary>
        sealed class EnchantMark
        {
            public int Shown;      // the visual drawn now (0 = none)
            public int Wanted;     // the visual asked for (drawn when the settings allow)
        }
        static readonly Dictionary<GameObject, EnchantMark> _marks = new Dictionary<GameObject, EnchantMark>();
        static int _sweepAt;

        /// <summary>Shows ItemVisuals id itemVisualId on the weapon (0 = remove the glow); the same id twice does nothing. Honours
        /// WowSettings.WeaponEnchantGlows and WowSettings.SpellEffects.</summary>
        public static void Apply(WowFiles files, GameObject weapon, int itemVisualId)
        {
            if (weapon == null) return;
            Hook();
            if (files != null) _lastFiles = files;
            EnchantMark mark;
            if (!_marks.TryGetValue(weapon, out mark))
            {
                if (itemVisualId <= 0) return;                 // nothing drawn, nothing wanted
                if (_marks.Count >= _sweepAt) Sweep();
                mark = new EnchantMark();
                _marks[weapon] = mark;
            }
            mark.Wanted = itemVisualId;
            int show = itemVisualId > 0 && WowSettings.WeaponEnchantGlows && WowSettings.SpellEffects ? itemVisualId : 0;
            if (mark.Shown == show) return;
            Remove(weapon);
            mark.Shown = 0;
            if (show <= 0 || files == null) return;
            var db = ItemVisualDb.Get(files);
            var models = db.Models(show);
            var anim = weapon.GetComponentInChildren<M2Animator>();
            int built = 0;
            string names = "";
            for (int s = 0; s < 5; s++)
            {
                if (models[s] == null) continue;
                var at = anim != null ? anim.Attachment(s) : null;
                if (at == null) continue;                      // the weapon has no attachment s (bows, some maces) - as in WoW
                GameObject go = null;
                try { go = M2Factory.Build(files, M2Model.NormalizeModelPath(models[s])); }
                catch (Exception e) { WowLog.Warn("Enchant visual " + show + ": " + models[s] + " failed: " + e.Message); }
                if (go == null) continue;
                go.name = FxName;
                go.transform.SetParent(at, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                built++;
                names += (names.Length > 0 ? ", " : "") + System.IO.Path.GetFileNameWithoutExtension(models[s]) + "@" + s;
            }
            mark.Shown = show;
            if (!_rulesLogged)
            {
                _rulesLogged = true;
                WowLog.Info("Enchant visuals: ItemVisuals slot i is drawn at the weapon's M2 attachment i (ours, measured fits); the temporary enchant wins over the permanent one, then the item's own glow (decision D4)");
            }
            if (_logged.Count < 128 && _logged.Add(show))
                WowLog.Info("Enchant visual " + show + ": " + (built > 0 ? names : "no attachment on " + weapon.name + " - nothing drawn") + " (first per visual)");
        }

        static void Remove(GameObject weapon)
        {
            var list = new List<GameObject>();
            foreach (var t in weapon.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == FxName) list.Add(t.gameObject);
            foreach (var g in list) UnityEngine.Object.Destroy(g);
        }

        /// <summary>A settings change (WeaponEnchantGlows / SpellEffects): every marked weapon shows or drops its glow.</summary>
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            WowSettings.Changed += OnSettings;
        }

        static void OnSettings()
        {
            bool on = WowSettings.WeaponEnchantGlows && WowSettings.SpellEffects;
            Sweep();
            foreach (var kv in new List<KeyValuePair<GameObject, EnchantMark>>(_marks))
            {
                var m = kv.Value;
                int want = on ? m.Wanted : 0;
                if (want == m.Shown) continue;
                int keep = m.Wanted;
                Apply(_lastFiles, kv.Key, on ? keep : 0);
                m.Wanted = keep;
            }
        }

        /// <summary>Drops the entries of destroyed weapons (Unity's == null).</summary>
        static void Sweep()
        {
            var dead = new List<GameObject>();
            foreach (var k in _marks.Keys) if (k == null) dead.Add(k);
            foreach (var k in dead) _marks.Remove(k);
            _sweepAt = _marks.Count + 64;
        }
    }
}
