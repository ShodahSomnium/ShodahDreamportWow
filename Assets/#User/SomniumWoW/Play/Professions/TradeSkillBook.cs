// iteration 11 - owner trainer (proposal-trainer.md 5.3 "Profession window", 6.1): the profession window's recipes. Pure.
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Professions
{
    /// <summary>What the profession window needs to know (online: bags, equipment, item answers, cooldowns, nearby spell focus objects).</summary>
    public interface ITradeFacts
    {
        IEnumerable<int> KnownSpells { get; }
        int Race { get; }
        WowClass Class { get; }
        int SkillValue(int skillLine);
        int SkillMax(int skillLine);
        int Have(int itemEntry);                 // count in the backpack + bags
        bool HasItem(int itemEntry);             // in the bags or the equipment (Spell.Totem)
        bool HasTotemCategory(int category);     // an item of the bags / equipment whose TotemCategory is compatible
        bool FocusNear(int focusId);             // a spawned GAMEOBJECT_TYPE_SPELL_FOCUS (8) of that focus within its distance
        float Cooldown(int spellId);             // seconds left, 0 = ready
        string ItemName(int itemEntry);          // null until known (online: the item answer is asked)
        string ItemIcon(int itemEntry);
        string ItemTooltip(int itemEntry);       // lead fix 2026-10-11: the made item's whole tooltip, null until known
        string TooltipOf(SpellInfo s);
        string DescriptionOf(SpellInfo s);
    }

    public static class TradeSkillBook
    {
        private static bool _logged;
        public const string Red = "<color=#ff2020>", End = "</color>";   // RED_FONT_COLOR_CODE "|cffff2020" .. FONT_COLOR_CODE_CLOSE (Constants.lua:12 / :18)

        /// <summary>
        /// The window of one skill line: the known recipes (attr0 0x20) with a SkillLineAbility row on it for this race / class; reagents counted in
        /// the bags, Craftable = min(Have / Need), tools ("Requires:" = WoW's BuildColoredListString of GetTradeSkillTools: focus, totems, totem
        /// categories - met plain, missing red), difficulty from the row, groups by the made item's subclass (ours), sorted Group ("" last),
        /// Difficulty, Name (ours, logged once).
        /// </summary>
        public static TradeSkillView Build(int skill, ITradeFacts me, ProfessionDb pdb, SpellDb db)
        {
            var line = pdb != null ? pdb.Skill(skill) : null;
            var v = new TradeSkillView
            {
                SkillId = skill,
                Name = line != null ? line.Name : "Skill " + skill,
                Verb = line != null ? line.Verb ?? "" : "",
                Rank = me != null ? me.SkillValue(skill) : 0,
                MaxRank = me != null ? me.SkillMax(skill) : 0
            };
            if (me == null || pdb == null || db == null) return v;
            foreach (int id in me.KnownSpells)
            {
                var s = db.Find(id);
                if (!ProfessionDb.IsRecipe(s)) continue;
                if (!pdb.OnLine(id, skill, me.Race, me.Class)) continue;
                v.Recipes.Add(Recipe(s, skill, v.Rank, line, me, pdb));
            }
            v.Recipes.Sort((a, b) =>
            {
                bool ea = string.IsNullOrEmpty(a.Group), eb = string.IsNullOrEmpty(b.Group);
                if (ea != eb) return ea ? 1 : -1;
                int c = string.Compare(a.Group ?? "", b.Group ?? "", StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = ((int)a.Difficulty).CompareTo((int)b.Difficulty);
                if (c != 0) return c;
                c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.SpellId.CompareTo(b.SpellId);
            });
            if (!_logged)
            {
                _logged = true;
                WowLog.Info("Profession window: recipes grouped by the made item's ItemSubClass, then difficulty (orange first), then name; the Create verb of a recipe that makes no item = its skill line's AlternateVerb (ours - Blizzard_TradeSkillUI.lua:386-411 decides it in C code)");
            }
            return v;
        }

        public static RecipeView Recipe(SpellInfo s, int skill, int rank, SkillLineInfo line, ITradeFacts me, ProfessionDb pdb)
        {
            var f = pdb.Recipe(s.Id) ?? new RecipeFacts { SpellId = s.Id };
            var r = new RecipeView
            {
                SpellId = s.Id,
                Name = s.Name,
                IconPath = s.IconPath,
                Difficulty = ProfessionDb.Difficulty(rank, pdb.Ability(s.Id, skill, me.Race, me.Class)),
                CreatesItem = f.CreatesItem,
                MinMade = f.CreatesItem != 0 ? f.MinMade : 0,
                MaxMade = f.CreatesItem != 0 ? f.MaxMade : 0,
                ItemTarget = f.ItemTarget,
                CooldownRemaining = Math.Max(0f, me.Cooldown(s.Id))
            };
            if (r.CooldownRemaining == float.MaxValue) r.CooldownRemaining = 0f;
            try { r.Tooltip = me.TooltipOf(s); } catch (Exception) { r.Tooltip = null; }
            try { r.Description = me.DescriptionOf(s); } catch (Exception) { r.Description = s.Description; }
            if (r.Tooltip == null) r.Tooltip = s.Name;
            r.Group = f.CreatesItem != 0 ? pdb.GroupOfItem(f.CreatesItem) : "";
            // lead fix 2026-10-11 (the user: "you can't see what items you can make on your professions"): WoW's TradeSkillSkillIcon shows the
            // made item (GetTradeSkillIcon) and its tooltip is that item's (GameTooltip:SetTradeSkillItem) - not the spell's
            if (f.CreatesItem != 0)
            {
                try { r.MadeIconPath = me.ItemIcon(f.CreatesItem); r.MadeTooltip = me.ItemTooltip(f.CreatesItem); r.MadeName = me.ItemName(f.CreatesItem); }
                catch (Exception) { }
            }
            r.Verb = f.CreatesItem != 0 ? null : (line != null && !string.IsNullOrEmpty(line.Verb) ? line.Verb : null);
            // reagents
            int craftable = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                int item = f.Reagent[i], need = f.ReagentCount[i];
                if (item <= 0 || need <= 0) continue;
                int have = Math.Max(0, me.Have(item));
                r.Reagents.Add(new ReagentView { ItemEntry = item, Need = need, Have = have, Name = me.ItemName(item), IconPath = me.ItemIcon(item) });
                craftable = Math.Min(craftable, have / need);
            }
            // tools: focus, totems, totem categories (ours: that order)
            var sb = new StringBuilder();
            bool met = true;
            if (f.Focus > 0) met &= Part(sb, pdb.FocusName(f.Focus) ?? ("Focus " + f.Focus), me.FocusNear(f.Focus));
            for (int i = 0; i < 2; i++)
                if (f.Totem[i] > 0) met &= Part(sb, me.ItemName(f.Totem[i]) ?? ("Item " + f.Totem[i]), me.HasItem(f.Totem[i]));
            for (int i = 0; i < 2; i++)
                if (f.TotemCategory[i] > 0) met &= Part(sb, pdb.TotemCategoryName(f.TotemCategory[i]) ?? ("Tool " + f.TotemCategory[i]), me.HasTotemCategory(f.TotemCategory[i]));
            r.Tools = sb.Length > 0 ? sb.ToString() : null;
            r.ToolsMet = met;
            if (craftable == int.MaxValue) craftable = met ? 1 : 0;   // no reagents (ours)
            r.Craftable = Math.Max(0, craftable);
            return r;
        }

        private static bool Part(StringBuilder sb, string name, bool ok)
        {
            if (sb.Length > 0) sb.Append(", ");
            if (ok) sb.Append(name);
            else sb.Append(Red).Append(name).Append(End);
            return ok;
        }
    }
}
