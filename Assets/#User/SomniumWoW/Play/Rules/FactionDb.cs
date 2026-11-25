// iteration 11 - owner character (proposal-talents.md 5.3 / 12.3; the prototype P/harness/Probes/D11/talents/FactionProto.cs lifted)
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>
    /// Faction.dbc read at runtime from the player's install (pure C#): the reputation tree of WoW's ReputationFrame, the base reputation and
    /// default flags of a race / class (AzerothCore ReputationMgr::GetBaseReputation / GetDefaultStateFlags, ReputationMgr.cpp:91-111 /
    /// 145-162) and the rank bounds (ReputationMgr.cpp:28-42). Fields (measured, proposal 4.1): 0 id, 1 reputationListID, 2-5 race masks,
    /// 6-9 class masks, 10-13 base values, 14-17 base flags, 18 parent faction, 23 Name_lang, 40 Description_lang.
    /// </summary>
    public sealed class FactionDb
    {
        // AzerothCore DBCEnums.h:303-313 FactionFlags
        public const int FlagVisible = 0x01, FlagAtWar = 0x02, FlagHidden = 0x04, FlagInvisibleForced = 0x08, FlagPeaceForced = 0x10, FlagInactive = 0x20;
        public const int MinStanding = -42000, MaxStanding = 42999;                                   // ReputationMgr.h Reputation_Bottom / Reputation_Cap
        static readonly int[] PointsInRank = { 36000, 3000, 3000, 3000, 6000, 12000, 21000, 1000 };   // ReputationMgr.cpp:28-42

        sealed class F
        {
            public int Id, RepList = -1, Parent;
            public readonly int[] RaceMask = new int[4], ClassMask = new int[4], Base = new int[4], Flags = new int[4];
            public string Name, Desc;
        }

        static readonly object CacheLock = new object();
        static FactionDb _cached;
        static WowFiles _cachedFiles;

        readonly Dictionary<int, F> _byId = new Dictionary<int, F>();
        readonly Dictionary<int, F> _byRepList = new Dictionary<int, F>();
        readonly Dictionary<int, List<F>> _children = new Dictionary<int, List<F>>();
        static bool _orderLogged;

        public bool Available { get; private set; }

        /// <summary>The faction data of these files (read once, cached; never null - Available false when Faction.dbc is missing).</summary>
        public static FactionDb Get(WowFiles files)
        {
            lock (CacheLock) { if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached; }
            var db = new FactionDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            return db;
        }

        FactionDb(WowFiles files)
        {
            DbcTable t = files != null ? Dbc.Load(files, "Faction") : null;
            if (t == null || t.FieldCount < 41)
            {
                WowLog.Warn("FactionDb: Faction.dbc missing or unexpected - no reputation list");
                return;
            }
            for (int r = 0; r < t.RecordCount; r++)
            {
                var f = new F { Id = t.GetInt(r, 0), RepList = t.GetInt(r, 1), Parent = t.GetInt(r, 18), Name = t.GetLocalizedString(r, 23), Desc = t.GetLocalizedString(r, 40) };
                for (int i = 0; i < 4; i++)
                {
                    f.RaceMask[i] = t.GetInt(r, 2 + i); f.ClassMask[i] = t.GetInt(r, 6 + i);
                    f.Base[i] = t.GetInt(r, 10 + i); f.Flags[i] = t.GetInt(r, 14 + i);
                }
                _byId[f.Id] = f;
                if (f.RepList >= 0) _byRepList[f.RepList] = f;
            }
            foreach (F f in _byId.Values)
            {
                if (f.Parent <= 0) continue;
                List<F> l;
                if (!_children.TryGetValue(f.Parent, out l)) _children[f.Parent] = l = new List<F>();
                l.Add(f);
            }
            foreach (List<F> l in _children.Values) l.Sort(ByName);
            Available = true;
            WowLog.Info("FactionDb: " + _byId.Count + " factions, " + _byRepList.Count + " with a reputation list id");
        }

        static int ByName(F a, F b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); }

        /// <summary>ReputationMgr::GetBaseReputation / GetDefaultStateFlags: the first of the 4 race / class entries that fits.</summary>
        static int Pick(F f, int race, int cls, bool flags)
        {
            int raceMask = race > 0 && race <= 32 ? 1 << (race - 1) : 0, classMask = cls > 0 && cls <= 32 ? 1 << (cls - 1) : 0;
            for (int i = 0; i < 4; i++)
                if (((f.RaceMask[i] & raceMask) != 0 || (f.RaceMask[i] == 0 && f.ClassMask[i] != 0)) && ((f.ClassMask[i] & classMask) != 0 || f.ClassMask[i] == 0))
                    return flags ? f.Flags[i] : f.Base[i];
            return 0;
        }

        public int BaseReputation(int repListId, int race, int cls) { F f; return _byRepList.TryGetValue(repListId, out f) ? Pick(f, race, cls, false) : 0; }
        public int DefaultFlags(int repListId, int race, int cls) { F f; return _byRepList.TryGetValue(repListId, out f) ? Pick(f, race, cls, true) : 0; }

        /// <summary>Faction.dbc Name_lang of a reputation list id (null unknown).</summary>
        public string Name(int repListId) { F f; return _byRepList.TryGetValue(repListId, out f) ? f.Name : null; }

        /// <summary>The reputation list id of a Faction.dbc name (probes), -1 unknown.</summary>
        public int RepListOf(string name)
        {
            foreach (F f in _byRepList.Values) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f.RepList;
            return -1;
        }

        /// <summary>ReputationMgr::ReputationToRank: 0 Hated .. 7 Exalted.</summary>
        public static int RankOf(int standing)
        {
            int limit = MaxStanding + 1;
            for (int i = 7; i >= 0; --i)
            {
                limit -= PointsInRank[i];
                if (standing >= limit) return i;
            }
            return 0;
        }

        /// <summary>The lowest standing of rank 0..7 (inclusive): Friendly 3000.</summary>
        public static int RankMin(int rank)
        {
            rank = Math.Max(0, Math.Min(7, rank));
            int v = MaxStanding + 1;
            for (int i = 7; i >= rank; --i) v -= PointsInRank[i];
            return v;
        }

        /// <summary>The first standing above rank 0..7 (exclusive): Friendly 9000.</summary>
        public static int RankMax(int rank) { rank = Math.Max(0, Math.Min(7, rank)); return RankMin(rank) + PointsInRank[rank]; }

        /// <summary>A faction is listed when VISIBLE and neither HIDDEN nor INVISIBLE_FORCED (ReputationMgr's own visibility rules).</summary>
        public static bool Shown(int flags) { return (flags & FlagVisible) != 0 && (flags & (FlagHidden | FlagInvisibleForced)) == 0; }

        /// <summary>
        /// WoW's reputation list in display order (GetNumFactions / GetFactionInfo are C code in Wow.exe - this order is OURS, logged once):
        /// shown factions under their Faction.dbc parents (headers = the ancestors of a shown faction, a header "has rep" when it is shown
        /// itself, e.g. Alliance Vanguard), parent-0 factions without shown children under "Other" (FACTION_OTHER, GlobalStrings.lua:3644),
        /// inactive (0x20) ones under "Inactive" (FACTION_INACTIVE, GlobalStrings.lua:3643) at the end, names sorted.
        /// </summary>
        public void BuildList(int race, int cls, int[] standing128, byte[] flags128, int watched, List<FactionView> into)
        {
            into.Clear();
            if (!Available || standing128 == null || flags128 == null) return;
            if (!_orderLogged)
            {
                _orderLogged = true;
                WowLog.Info("FactionDb: the reputation list order is ours (Faction.dbc parents, names sorted, 'Other' and 'Inactive' last) - WoW's GetFactionInfo is C code");
            }
            var shown = new HashSet<int>();
            var headers = new HashSet<int>();
            var others = new List<F>();
            var inactive = new List<F>();
            foreach (F f in _byRepList.Values)
            {
                if (f.RepList > 127 || f.RepList >= flags128.Length || !Shown(flags128[f.RepList])) continue;
                if ((flags128[f.RepList] & FlagInactive) != 0) { inactive.Add(f); continue; }
                shown.Add(f.Id);
            }
            foreach (int id in shown)
            {
                F f = _byId[id];
                int p = f.Parent, guard = 0;
                while (p > 0 && guard++ < 8)
                {
                    headers.Add(p);
                    F pf;
                    p = _byId.TryGetValue(p, out pf) ? pf.Parent : 0;
                }
            }
            var tops = new List<F>();
            foreach (int id in headers) { F h; if (_byId.TryGetValue(id, out h) && h.Parent <= 0) tops.Add(h); }
            tops.Sort(ByName);
            foreach (int id in shown) { F f = _byId[id]; if (f.Parent <= 0 && !headers.Contains(f.Id)) others.Add(f); }
            others.Sort(ByName);
            inactive.Sort(ByName);
            for (int i = 0; i < tops.Count; i++) Emit(tops[i], 0, race, cls, standing128, flags128, watched, headers, shown, into);
            if (others.Count > 0)
            {
                into.Add(new FactionView { Name = "Other", IsHeader = true });          // FACTION_OTHER (GlobalStrings.lua:3644)
                for (int i = 0; i < others.Count; i++) into.Add(Row(others[i], false, false, race, cls, standing128, flags128, watched));
            }
            if (inactive.Count > 0)
            {
                into.Add(new FactionView { Name = "Inactive", IsHeader = true });       // FACTION_INACTIVE (GlobalStrings.lua:3643)
                for (int i = 0; i < inactive.Count; i++) into.Add(Row(inactive[i], false, false, race, cls, standing128, flags128, watched));
            }
        }

        void Emit(F h, int depth, int race, int cls, int[] st, byte[] fl, int watched, HashSet<int> headers, HashSet<int> shown, List<FactionView> into)
        {
            into.Add(Row(h, true, depth >= 1, race, cls, st, fl, watched));
            List<F> kids;
            if (depth > 4 || !_children.TryGetValue(h.Id, out kids)) return;
            for (int i = 0; i < kids.Count; i++)
            {
                F k = kids[i];
                if (headers.Contains(k.Id)) Emit(k, depth + 1, race, cls, st, fl, watched, headers, shown, into);
                else if (shown.Contains(k.Id)) into.Add(Row(k, false, depth + 1 >= 2, race, cls, st, fl, watched));
            }
        }

        FactionView Row(F f, bool header, bool child, int race, int cls, int[] st, byte[] fl, int watched)
        {
            var row = new FactionView { FactionId = f.Id, RepListId = f.RepList, Name = f.Name ?? "", Description = f.Desc ?? "", IsHeader = header, IsChild = child };
            if (f.RepList >= 0 && f.RepList < 128 && f.RepList < fl.Length && f.RepList < st.Length)
            {
                int flags = fl[f.RepList];
                row.HasRep = Shown(flags);
                row.Standing = Math.Max(MinStanding, Math.Min(MaxStanding, Pick(f, race, cls, false) + st[f.RepList]));   // ReputationMgr::GetReputation
                row.Rank = RankOf(row.Standing);
                row.RankMin = RankMin(row.Rank);
                row.RankMax = RankMax(row.Rank);
                row.AtWar = (flags & FlagAtWar) != 0;
                // ReputationMgr::SetAtWar (ReputationMgr.cpp:504-534): refused for hidden / invisible-forced, turning ON refused for peace-forced
                row.CanToggleAtWar = (flags & (FlagPeaceForced | FlagHidden | FlagInvisibleForced)) == 0;
                row.Inactive = (flags & FlagInactive) != 0;
                row.Watched = f.RepList == watched;
            }
            else row.RepListId = -1;
            return row;
        }
    }
}
