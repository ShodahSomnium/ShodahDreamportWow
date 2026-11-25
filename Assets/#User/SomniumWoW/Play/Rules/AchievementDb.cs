// iteration 11 - owner character (proposal-talents.md 5.3 / 4.1: Achievement, Achievement_Category, Achievement_Criteria)
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>One Achievement_Criteria.dbc row (measured fields: 1 achievement, 2 type, 3 asset, 4 quantity, 9 description, 26 flags, 30 order).</summary>
    public sealed class AchievementCriteriaInfo
    {
        public int Id, Achievement, Type, Asset, Quantity, Flags, Order;
        public string Description;
        public bool ProgressBar { get { return (Flags & 0x1) != 0; } }   // ACHIEVEMENT_CRITERIA_PROGRESS_BAR (Blizzard_AchievementUI.lua:1496)
        public bool Hidden { get { return (Flags & 0x2) != 0; } }
    }

    /// <summary>One Achievement.dbc row (measured fields: 1 faction, 2 map, 3 previous, 4 name, 21 description, 38 category, 39 points, 40 order,
    /// 41 flags, 42 SpellIconID, 43 reward text, 60 minimum criteria count, 61 shares progress with).</summary>
    public sealed class AchievementInfo
    {
        public int Id, Faction = -1, Map, Previous, Category, Points, Order, Flags, IconId, MinCriteria, SharesWith;
        public string Name, Description, Reward, IconPath;
        public readonly List<AchievementCriteriaInfo> Criteria = new List<AchievementCriteriaInfo>();
        public int Next;   // the achievement whose Previous is this one (a series), 0 none
        public bool Counter { get { return (Flags & 0x1) != 0; } }        // ACHIEVEMENT_FLAG_COUNTER = a statistic
        public bool Hidden { get { return (Flags & 0x2) != 0; } }         // ACHIEVEMENT_FLAG_HIDDEN
        public bool HasProgressBar { get { return (Flags & 0x80) != 0; } } // ACHIEVEMENT_FLAGS_HAS_PROGRESS_BAR (Blizzard_AchievementUI.lua:1335)
    }

    /// <summary>One Achievement_Category.dbc row (0 id, 1 parent (-1 top), 2 name, 19 sort).</summary>
    public sealed class AchievementCategoryInfo
    {
        public int Id, Parent = -1, Sort;
        public string Name;
        public readonly List<AchievementCategoryInfo> Children = new List<AchievementCategoryInfo>();
        public readonly List<AchievementInfo> Achievements = new List<AchievementInfo>();   // sorted by Order
    }

    /// <summary>
    /// The achievement data of the player's install (pure C#): names, descriptions, icons, points, the category tree, the criteria of each
    /// achievement. Statistics (flag COUNTER, category 1 "Statistics" and its children) are kept out of the tree (known gap: no Statistics tab).
    /// </summary>
    public sealed class AchievementDb
    {
        public const int StatisticsCategory = 1, FeatsOfStrengthCategory = 81;   // Achievement_Category.dbc (measured)
        public const int CriteriaTypeCompleteAchievement = 8;                    // CRITERIA_TYPE_ACHIEVEMENT (Blizzard_AchievementUI.lua:1446)

        static readonly object CacheLock = new object();
        static AchievementDb _cached;
        static WowFiles _cachedFiles;

        readonly Dictionary<int, AchievementInfo> _byId = new Dictionary<int, AchievementInfo>();
        readonly Dictionary<int, AchievementCriteriaInfo> _criteria = new Dictionary<int, AchievementCriteriaInfo>();
        readonly Dictionary<int, AchievementCategoryInfo> _categories = new Dictionary<int, AchievementCategoryInfo>();
        readonly List<AchievementCategoryInfo> _top = new List<AchievementCategoryInfo>();

        public bool Available { get; private set; }

        public static AchievementDb Get(WowFiles files)
        {
            lock (CacheLock) { if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached; }
            var db = new AchievementDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            return db;
        }

        AchievementDb(WowFiles files)
        {
            DbcTable a = files != null ? Dbc.Load(files, "Achievement") : null;
            DbcTable c = files != null ? Dbc.Load(files, "Achievement_Category") : null;
            DbcTable cr = files != null ? Dbc.Load(files, "Achievement_Criteria") : null;
            if (a == null || c == null || a.FieldCount < 62 || c.FieldCount < 20)
            {
                WowLog.Warn("AchievementDb: Achievement.dbc / Achievement_Category.dbc missing or unexpected - no achievements");
                return;
            }
            DbcTable icons = Dbc.Load(files, "SpellIcon");
            for (int r = 0; r < c.RecordCount; r++)
            {
                var cat = new AchievementCategoryInfo { Id = c.GetId(r), Parent = c.GetInt(r, 1), Name = c.GetLocalizedString(r, 2) ?? "", Sort = c.GetInt(r, 19) };
                _categories[cat.Id] = cat;
            }
            for (int r = 0; r < a.RecordCount; r++)
            {
                var ai = new AchievementInfo
                {
                    Id = a.GetId(r), Faction = a.GetInt(r, 1), Map = a.GetInt(r, 2), Previous = a.GetInt(r, 3), Name = a.GetLocalizedString(r, 4) ?? "",
                    Description = a.GetLocalizedString(r, 21) ?? "", Category = a.GetInt(r, 38), Points = a.GetInt(r, 39), Order = a.GetInt(r, 40),
                    Flags = a.GetInt(r, 41), IconId = a.GetInt(r, 42), Reward = a.GetLocalizedString(r, 43) ?? "", MinCriteria = a.GetInt(r, 60), SharesWith = a.GetInt(r, 61)
                };
                if (icons != null && ai.IconId > 0)
                {
                    int ir = icons.FindRow(ai.IconId);
                    if (ir >= 0) ai.IconPath = icons.GetString(ir, 1);
                }
                _byId[ai.Id] = ai;
            }
            if (cr != null && cr.FieldCount >= 31)
            {
                for (int r = 0; r < cr.RecordCount; r++)
                {
                    var ci = new AchievementCriteriaInfo
                    {
                        Id = cr.GetId(r), Achievement = cr.GetInt(r, 1), Type = cr.GetInt(r, 2), Asset = cr.GetInt(r, 3), Quantity = cr.GetInt(r, 4),
                        Description = cr.GetLocalizedString(r, 9) ?? "", Flags = cr.GetInt(r, 26), Order = cr.GetInt(r, 30)
                    };
                    _criteria[ci.Id] = ci;
                    AchievementInfo owner;
                    if (_byId.TryGetValue(ci.Achievement, out owner)) owner.Criteria.Add(ci);
                }
            }
            else WowLog.Warn("AchievementDb: Achievement_Criteria.dbc missing or unexpected - achievements show without criteria");
            int listed = 0, points = 0;
            foreach (AchievementInfo ai in _byId.Values)
            {
                if (ai.Criteria.Count > 1) ai.Criteria.Sort((x, y) => x.Order != y.Order ? x.Order.CompareTo(y.Order) : x.Id.CompareTo(y.Id));
                AchievementInfo prev;
                if (ai.Previous > 0 && _byId.TryGetValue(ai.Previous, out prev) && prev.Next == 0) prev.Next = ai.Id;
                if (ai.Counter) continue;
                AchievementCategoryInfo cat;
                if (!_categories.TryGetValue(ai.Category, out cat) || InStatistics(cat)) continue;
                cat.Achievements.Add(ai);
                listed++;
                points += ai.Points;
            }
            foreach (AchievementCategoryInfo cat in _categories.Values)
            {
                if (cat.Achievements.Count > 1) cat.Achievements.Sort((x, y) => x.Order != y.Order ? x.Order.CompareTo(y.Order) : x.Id.CompareTo(y.Id));
                if (InStatistics(cat)) continue;
                AchievementCategoryInfo parent;
                if (cat.Parent > 0 && _categories.TryGetValue(cat.Parent, out parent)) parent.Children.Add(cat);
                else if (cat.Parent <= 0) _top.Add(cat);
            }
            Comparison<AchievementCategoryInfo> bySort = (x, y) => x.Sort != y.Sort ? x.Sort.CompareTo(y.Sort) : x.Id.CompareTo(y.Id);
            _top.Sort(bySort);
            foreach (AchievementCategoryInfo cat in _categories.Values) if (cat.Children.Count > 1) cat.Children.Sort(bySort);
            Available = true;
            WowLog.Info("AchievementDb: " + _byId.Count + " achievements (" + listed + " listed, " + points + " points), " + _categories.Count + " categories (" + _top.Count
                        + " top-level without Statistics), " + _criteria.Count + " criteria");
        }

        bool InStatistics(AchievementCategoryInfo cat)
        {
            int guard = 0;
            while (cat != null && guard++ < 8)
            {
                if (cat.Id == StatisticsCategory) return true;
                AchievementCategoryInfo p;
                if (cat.Parent <= 0 || !_categories.TryGetValue(cat.Parent, out p)) return false;
                cat = p;
            }
            return false;
        }

        /// <summary>The top-level categories (General, Quests, Exploration ...) in Achievement_Category sort order, Statistics left out.</summary>
        public IReadOnlyList<AchievementCategoryInfo> TopCategories { get { return _top; } }

        public AchievementCategoryInfo Category(int id) { AchievementCategoryInfo c; return _categories.TryGetValue(id, out c) ? c : null; }
        public AchievementInfo Find(int id) { AchievementInfo a; return _byId.TryGetValue(id, out a) ? a : null; }
        public AchievementCriteriaInfo Criteria(int id) { AchievementCriteriaInfo c; return _criteria.TryGetValue(id, out c) ? c : null; }

        /// <summary>The achievement's name (Achievement.dbc field 4), null unknown - also for area social's chat lines.</summary>
        public string Name(int id) { AchievementInfo a; return _byId.TryGetValue(id, out a) ? a.Name : null; }

        public int Points(int id) { AchievementInfo a; return _byId.TryGetValue(id, out a) ? a.Points : 0; }

        /// <summary>Achievement.dbc faction: -1 both, 0 Horde, 1 Alliance (measured).</summary>
        public static bool ForFaction(AchievementInfo a, WowFaction f)
        {
            return a.Faction < 0 || (a.Faction == 0 && f == WowFaction.Horde) || (a.Faction == 1 && f == WowFaction.Alliance);
        }

        /// <summary>Unpacks a server time (ByteBuffer::AppendPackedTime, ByteBuffer.cpp:137-141: (year-2000) &lt;&lt; 24 | month0 &lt;&lt; 20 |
        /// (day-1) &lt;&lt; 14 | weekday &lt;&lt; 11 | hour &lt;&lt; 6 | minute) to unix seconds. The server packs its LOCAL time: read as UTC (ours).</summary>
        public static long UnpackTime(uint packed)
        {
            int minute = (int)(packed & 0x3F), hour = (int)((packed >> 6) & 0x1F), day = (int)((packed >> 14) & 0x3F) + 1;
            int month = (int)((packed >> 20) & 0xF) + 1, year = (int)((packed >> 24) & 0x1F) + 2000;
            try { return (long)(new DateTime(year, Math.Min(12, month), Math.Min(DateTime.DaysInMonth(year, Math.Min(12, month)), day), Math.Min(23, hour), Math.Min(59, minute), 0, DateTimeKind.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
            catch (ArgumentException) { return 0; }
        }

        /// <summary>WoW's SHORTDATE "%2$d/%1$02d/%3$02d" (GlobalStrings.lua:6246) with (day, month, year): "5/16/08".</summary>
        public static string ShortDate(long unixSeconds)
        {
            if (unixSeconds <= 0) return "";
            DateTime d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
            return d.Month + "/" + d.Day.ToString("00") + "/" + (d.Year % 100).ToString("00");
        }
    }
}
