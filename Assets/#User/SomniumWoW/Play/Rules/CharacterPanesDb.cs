// iteration 11 - owner character (proposal-talents.md 5.3: titles, currencies, combat rating tables)
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Rules
{
    /// <summary>One CurrencyTypes.dbc row (fields 1 ItemId, 3 BitIndex, 2 CategoryID - measured, proposal 4.1).</summary>
    public sealed class CurrencyTypeInfo { public int ItemEntry, BitIndex, CategoryId; }

    /// <summary>
    /// The character panes' game data, read at runtime from the player's install (pure C#): CharTitles.dbc (0 id, 2 male name, 19 female
    /// name - locstrings with "%s" for the character's name -, 36 bit_index), CurrencyTypes.dbc + CurrencyCategory.dbc (0 id, 2 Name_lang),
    /// gtCombatRatings.dbc (1 float per row, row = rating * 100 + level - 1) and gtOCTClassCombatRatingScalar.dbc (id, ratio; id =
    /// (class - 1) * 32 + rating + 1) - AzerothCore Player::GetRatingMultiplier (Player.cpp:5346-5365).
    /// </summary>
    public sealed class CharacterPanesDb
    {
        public const int HonorPointsItem = 43308, ArenaPointsItem = 43307;   // CurrencyTypes.dbc rows (bits 13 / 12, measured)
        const int GtMaxLevel = 100, GtMaxRating = 32;                        // DBCStructure.h GT_MAX_LEVEL, GT_MAX_RATING

        static readonly object CacheLock = new object();
        static CharacterPanesDb _cached;
        static WowFiles _cachedFiles;

        readonly Dictionary<int, string> _titleMale = new Dictionary<int, string>(), _titleFemale = new Dictionary<int, string>();
        readonly List<int> _titleBits = new List<int>();
        readonly List<CurrencyTypeInfo> _currencies = new List<CurrencyTypeInfo>();
        readonly Dictionary<int, string> _categories = new Dictionary<int, string>();
        readonly DbcTable _gtRatings, _gtScalar;
        int _maxTitleBit;

        public static CharacterPanesDb Get(WowFiles files)
        {
            lock (CacheLock) { if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached; }
            var db = new CharacterPanesDb(files);
            lock (CacheLock)
            {
                if (_cached != null && ReferenceEquals(_cachedFiles, files)) return _cached;
                _cached = db;
                _cachedFiles = files;
            }
            return db;
        }

        CharacterPanesDb(WowFiles files)
        {
            DbcTable titles = files != null ? Dbc.Load(files, "CharTitles") : null;
            if (titles != null && titles.FieldCount >= 37)
            {
                for (int r = 0; r < titles.RecordCount; r++)
                {
                    int bit = titles.GetInt(r, 36);
                    if (bit <= 0) continue;
                    string male = titles.GetLocalizedString(r, 2), female = titles.GetLocalizedString(r, 19);
                    if (string.IsNullOrEmpty(male)) continue;
                    _titleMale[bit] = male;
                    _titleFemale[bit] = string.IsNullOrEmpty(female) ? male : female;
                    _titleBits.Add(bit);
                    _maxTitleBit = Math.Max(_maxTitleBit, bit);
                }
                _titleBits.Sort();
            }
            else WowLog.Warn("CharacterPanesDb: CharTitles.dbc missing or unexpected - no titles");
            DbcTable cats = files != null ? Dbc.Load(files, "CurrencyCategory") : null;
            if (cats != null && cats.FieldCount >= 3)
                for (int r = 0; r < cats.RecordCount; r++) _categories[cats.GetId(r)] = cats.GetLocalizedString(r, 2) ?? "";
            DbcTable cur = files != null ? Dbc.Load(files, "CurrencyTypes") : null;
            int dropped = 0;
            if (cur != null && cur.FieldCount >= 4)
            {
                for (int r = 0; r < cur.RecordCount; r++)
                {
                    var c = new CurrencyTypeInfo { ItemEntry = cur.GetInt(r, 1), CategoryId = cur.GetInt(r, 2), BitIndex = cur.GetInt(r, 3) };
                    // id 141 (item 43949) carries a garbage category (measured) - only rows whose category exists are WoW's currencies
                    if (c.ItemEntry <= 0 || c.BitIndex <= 0 || c.BitIndex > 64 || !_categories.ContainsKey(c.CategoryId)) { dropped++; continue; }
                    _currencies.Add(c);
                }
            }
            else WowLog.Warn("CharacterPanesDb: CurrencyTypes.dbc missing or unexpected - no currencies");
            _gtRatings = files != null ? Dbc.Load(files, "gtCombatRatings") : null;
            _gtScalar = files != null ? Dbc.Load(files, "gtOCTClassCombatRatingScalar") : null;
            WowLog.Info("CharacterPanesDb: " + _titleBits.Count + " titles (max bit " + _maxTitleBit + "), " + _currencies.Count + " currencies in " + _categories.Count
                        + " categories (" + dropped + " rows without a category dropped), rating tables " + (_gtRatings != null && _gtScalar != null ? "ok" : "MISSING"));
        }

        /// <summary>Every CharTitles bit_index, ascending.</summary>
        public IReadOnlyList<int> TitleBits { get { return _titleBits; } }

        /// <summary>The gender's title with "%s" for the character name ("Private %s"); gender 0 male, 1 female. Null when unknown.</summary>
        public string TitleFormat(int bitIndex, int gender)
        {
            string s;
            if (gender == 1 && _titleFemale.TryGetValue(bitIndex, out s)) return s;
            return _titleMale.TryGetValue(bitIndex, out s) ? s : null;
        }

        /// <summary>A title filled with the character's name (ours: the client formats it in C code). "%s" may sit before or after.</summary>
        public static string FillTitle(string format, string name)
        {
            if (string.IsNullOrEmpty(format)) return name ?? "";
            int i = format.IndexOf("%s", StringComparison.Ordinal);
            if (i < 0) return format;
            return (format.Substring(0, i) + (name ?? "") + format.Substring(i + 2)).Trim();
        }

        /// <summary>CurrencyTypes rows whose category exists in CurrencyCategory.dbc.</summary>
        public IReadOnlyList<CurrencyTypeInfo> Currencies { get { return _currencies; } }

        public string CategoryName(int categoryId) { string s; return _categories.TryGetValue(categoryId, out s) ? s : ""; }

        /// <summary>The currency row of an item entry, null when the item is not a currency.</summary>
        public CurrencyTypeInfo CurrencyOfItem(int itemEntry)
        {
            for (int i = 0; i < _currencies.Count; i++) if (_currencies[i].ItemEntry == itemEntry) return _currencies[i];
            return null;
        }

        /// <summary>
        /// A combat rating as percent (or skill points for the weapon / defense skill ratings): rating * scalar(class, cr) / gt(cr, level),
        /// AzerothCore Player::GetRatingMultiplier (Player.cpp:5346-5365): level clamped 1..100 (GT_MAX_LEVEL), 0 when a table is missing.
        /// </summary>
        public float RatingBonus(int combatRating, int rating, WowClass cls, int level)
        {
            if (_gtRatings == null || _gtScalar == null || combatRating < 0 || combatRating >= GtMaxRating || rating == 0) return 0f;
            level = Math.Max(1, Math.Min(GtMaxLevel, level));
            int row = combatRating * GtMaxLevel + level - 1;
            if (row >= _gtRatings.RecordCount) return 0f;
            float gt = _gtRatings.GetFloat(row, _gtRatings.FieldCount - 1);
            int scalarRow = _gtScalar.FieldCount > 1 ? _gtScalar.FindRow(((int)cls - 1) * GtMaxRating + combatRating + 1) : ((int)cls - 1) * GtMaxRating + combatRating + 1;
            if (scalarRow < 0 || scalarRow >= _gtScalar.RecordCount || gt <= 0f) return 0f;
            float ratio = _gtScalar.GetFloat(scalarRow, _gtScalar.FieldCount - 1);
            return rating * ratio / gt;
        }
    }
}
