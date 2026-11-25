// iteration 11 - owner trainer (proposal-trainer.md 3.2, 5.3 "Trainer window", 6.1): SMSG_TRAINER_LIST and the trainer window's lines. Pure.
using System;
using System.Collections.Generic;
using System.Text;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Spells;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Professions
{
    /// <summary>What the trainer window needs to know about the character (online: the session's fields; offline: the record).</summary>
    public interface ITrainerFacts
    {
        int Level { get; }
        int Money { get; }
        int FreeProfessions { get; }
        bool Knows(int spellId);
        int SkillValue(int skillLine);       // the raw skill value, 0 = the skill is missing
        string TooltipOf(SpellInfo s);       // the spellbook's tooltip text of a spell (AbilityView.Tooltip)
    }

    /// <summary>
    /// SMSG_TRAINER_LIST (0x1B1) as AzerothCore master writes it - Trainer::SendSpells (Trainer.cpp:38-81) + TrainerList::Write (NPCPackets.cpp:25-46):
    /// uint64 TrainerGUID; int32 TrainerType; int32 count; count x 38 bytes {int32 SpellID; uint8 Usable; int32 MoneyCost; int32 PointCost[2];
    /// uint8 ReqLevel; int32 ReqSkillLine; int32 ReqSkillRank; int32 ReqAbility[3]}; cstring Greeting. (PointCost / ReqAbility are appended as
    /// raw int32 arrays, ByteBuffer.h:472-475.)
    /// </summary>
    public struct TrainerListPacket
    {
        public struct Entry
        {
            public int SpellId; public byte State; public int MoneyCost; public int PointCost0, PointCost1;
            public byte ReqLevel; public int ReqSkillLine, ReqSkillRank; public int Ab0, Ab1, Ab2;
        }

        public const int EntrySize = 38;
        public ulong Trainer;
        public int Type;
        public Entry[] Spells;
        public string Greeting;

        /// <summary>Reads the packet; false (why set) when the count cannot fit the bytes (38 per spell + the greeting's 0) or the head is short.</summary>
        public static bool Parse(WowPacket p, out TrainerListPacket t, out string why)
        {
            t = new TrainerListPacket();
            why = null;
            if (p == null || p.Remaining < 16) { why = "shorter than its 16-byte head (" + (p != null ? p.Remaining : 0) + ")"; return false; }
            t.Trainer = p.U64();
            t.Type = p.I32();
            int n = p.I32();
            if (n < 0 || n > 1000 || p.Remaining < n * EntrySize + 1) { why = "count " + n + " does not fit " + p.Remaining + " bytes"; return false; }
            t.Spells = new Entry[n];
            for (int i = 0; i < n; i++)
            {
                var e = new Entry();
                e.SpellId = p.I32(); e.State = p.U8(); e.MoneyCost = p.I32();
                e.PointCost0 = p.I32(); e.PointCost1 = p.I32();
                e.ReqLevel = p.U8(); e.ReqSkillLine = p.I32(); e.ReqSkillRank = p.I32();
                e.Ab0 = p.I32(); e.Ab1 = p.I32(); e.Ab2 = p.I32();
                t.Spells[i] = e;
            }
            t.Greeting = p.CString();
            return true;
        }
    }

    public static class TrainerBook
    {
        // WoW's |cffRRGGBB as TMP rich text (GlobalStrings.lua:7667-7672: white ffffff, red ff2020)
        public const string White = "<color=#ffffff>", Red = "<color=#ff2020>", End = "</color>";
        private static bool _loggedSort;

        /// <summary>The trainer window of a parsed list (display, requirements, CanTrain, NeedsConfirm, our sort).</summary>
        public static TrainerView Build(TrainerListPacket t, string npcName, ITrainerFacts me, SpellDb db, ProfessionDb pdb)
        {
            var v = new TrainerView
            {
                NpcGuid = t.Trainer,
                NpcName = npcName ?? "",
                Greeting = t.Greeting ?? "",
                Kind = t.Type >= 0 && t.Type <= 3 ? (TrainerKind)t.Type : TrainerKind.Class
            };
            if (t.Spells != null)
                foreach (var e in t.Spells)
                {
                    var sv = new TrainerServiceView
                    {
                        SpellId = e.SpellId,
                        State = e.State <= 2 ? (TrainerSpellState)e.State : TrainerSpellState.Unavailable,
                        MoneyCost = e.MoneyCost,
                        ProfessionCost = e.PointCost1,
                        ReqLevel = e.ReqLevel,
                        ReqSkillLine = e.ReqSkillLine,
                        ReqSkillRank = e.ReqSkillRank
                    };
                    sv.ReqAbility[0] = e.Ab0; sv.ReqAbility[1] = e.Ab1; sv.ReqAbility[2] = e.Ab2;
                    Display(sv, me, db, pdb);
                    v.Services.Add(sv);
                }
            Sort(v.Services);
            RefreshLocal(v, me, db, pdb);
            return v;
        }

        /// <summary>Name, rank, icon and tooltip from the spell the service teaches (ShowSpellOf: a LEARN_SPELL wrapper's first TriggerSpell, else itself).</summary>
        public static void Display(TrainerServiceView sv, ITrainerFacts me, SpellDb db, ProfessionDb pdb)
        {
            sv.ShowSpellId = pdb != null ? pdb.ShowSpellOf(sv.SpellId) : sv.SpellId;
            var s = db != null ? db.Find(sv.ShowSpellId) : null;
            if (s == null && sv.ShowSpellId != sv.SpellId && db != null) s = db.Find(sv.SpellId);
            sv.Name = s != null ? s.Name : "Spell " + sv.SpellId;
            sv.Rank = s != null ? s.Rank : "";
            sv.IconPath = s != null ? s.IconPath : null;
            string tip = null;
            if (s != null && me != null) { try { tip = me.TooltipOf(s); } catch (Exception) { tip = null; } }
            sv.Tooltip = tip ?? sv.Name;
        }

        /// <summary>Our order (the client's is C code, the packet's is AzerothCore's unordered DB load): ReqLevel, Name, the number in Rank
        /// ("Rank 2" before "Rank 10"), SpellId.</summary>
        public static void Sort(List<TrainerServiceView> list)
        {
            list.Sort((a, b) =>
            {
                int c = a.ReqLevel.CompareTo(b.ReqLevel);
                if (c != 0) return c;
                c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = RankNumber(a.Rank).CompareTo(RankNumber(b.Rank));
                return c != 0 ? c : a.SpellId.CompareTo(b.SpellId);
            });
            if (!_loggedSort) { _loggedSort = true; WowLog.Info("Trainer: services sorted by level, name, rank (ours)"); }
        }

        /// <summary>The first number in a rank text ("Rank 10" -> 10), 0 when none.</summary>
        public static int RankNumber(string rank)
        {
            if (string.IsNullOrEmpty(rank)) return 0;
            int n = 0; bool any = false;
            for (int i = 0; i < rank.Length; i++)
            {
                char ch = rank[i];
                if (ch >= '0' && ch <= '9') { any = true; n = n * 10 + (ch - '0'); if (n > 100000) break; }
                else if (any) break;
            }
            return n;
        }

        /// <summary>The local parts every frame may change: requirement colours (level, skill, abilities) and CanTrain (money, free professions);
        /// NeedsConfirm. True when anything changed (the caller bumps Version).</summary>
        public static bool RefreshLocal(TrainerView v, ITrainerFacts me, SpellDb db, ProfessionDb pdb)
        {
            if (v == null) return false;
            bool changed = false;
            foreach (var sv in v.Services)
            {
                string req = Requirements(sv, me, db, pdb);
                bool can = sv.State == TrainerSpellState.Available && me != null && me.Money >= sv.MoneyCost
                           && (sv.ProfessionCost == 0 || me.FreeProfessions >= sv.ProfessionCost);   // Blizzard_TrainerUI.lua:341-379
                bool confirm = v.Kind == TrainerKind.Tradeskill && sv.ProfessionCost > 0;            // Blizzard_TrainerUI.lua:396-398
                if (!string.Equals(req, sv.Requirements, StringComparison.Ordinal) || can != sv.CanTrain || confirm != sv.NeedsConfirm) changed = true;
                sv.Requirements = req;
                sv.CanTrain = can;
                sv.NeedsConfirm = confirm;
            }
            return changed;
        }

        /// <summary>
        /// REQUIRES_LABEL "Requires:" (GlobalStrings.lua:6084) + " " + the parts joined ", " (Blizzard_TrainerUI.lua:288-337), null when none:
        /// TRAINER_REQ_LEVEL "Level |cffffffff%d|r" / _RED when ReqLevel &gt; 1; TRAINER_REQ_SKILL_RANK "%s (|cffffffff%d|r)" / _RED; each ability
        /// TRAINER_REQ_ABILITY "|cffffffff%s|r" / _RED (white when known or when the service itself is Known, lua :321). Only the number / the
        /// ability name is coloured.
        /// </summary>
        public static string Requirements(TrainerServiceView sv, ITrainerFacts me, SpellDb db, ProfessionDb pdb)
        {
            var sb = new StringBuilder();
            if (sv.ReqLevel > 1)
            {
                bool ok = me != null && me.Level >= sv.ReqLevel;
                sb.Append("Level ").Append(ok ? White : Red).Append(sv.ReqLevel).Append(End);
            }
            if (sv.ReqSkillLine != 0)
            {
                bool ok = me != null && me.SkillValue(sv.ReqSkillLine) >= sv.ReqSkillRank && me.SkillValue(sv.ReqSkillLine) > 0;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(pdb != null ? pdb.SkillName(sv.ReqSkillLine) : "Skill " + sv.ReqSkillLine).Append(" (").Append(ok ? White : Red).Append(sv.ReqSkillRank).Append(End).Append(')');
            }
            for (int i = 0; i < 3; i++)
            {
                int a = sv.ReqAbility[i];
                if (a == 0) continue;
                var s = db != null ? db.Find(a) : null;
                bool ok = sv.State == TrainerSpellState.Known || (me != null && me.Knows(a));
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(ok ? White : Red).Append(s != null ? s.Name : "Spell " + a).Append(End);
            }
            if (sb.Length == 0) return null;
            return "Requires: " + sb;
        }
    }
}
