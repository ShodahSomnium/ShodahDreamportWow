using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Models;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>
    /// What the online world reads from the player's own client DBCs: reactions (FactionTemplate.dbc + Faction.dbc, exactly as
    /// AzerothCore Unit::GetFactionReactionTo / FactionTemplateEntry / ReputationMgr compute them), item entry -> ItemDisplayInfo id
    /// (Item.dbc), gameobject display -> model path (GameObjectDisplayInfo.dbc). Every lookup answers 0/null without the files.
    /// </summary>
    public sealed class OnlineDb
    {
        // FactionTemplate.dbc (AC DBCStructure.h FactionTemplateEntry, 14 fields): ID, faction, factionFlags, ourMask, friendlyMask,
        // hostileMask, enemyFaction[4], friendFaction[4].
        public const int FtFieldCount = 14, FtFaction = 1, FtFlags = 2, FtOurMask = 3, FtFriendlyMask = 4, FtHostileMask = 5, FtEnemies = 6, FtFriends = 10;
        // Faction.dbc (AC FactionEntry, 57 fields): ID, reputationListID, BaseRepRaceMask[4], BaseRepClassMask[4], BaseRepValue[4],
        // ReputationFlags[4], team, ... name[16] + flags, description[16] + flags.
        public const int FFieldCount = 57, FRepListId = 1, FRaceMask = 2, FClassMask = 6, FRepBase = 10, FRepFlags = 14;
        /// <summary>DBCEnums.h FACTION_TEMPLATE_FLAG_ATTACK_PVP_ACTIVE_PLAYERS / HATES_ALL_EXCEPT_FRIENDS, FACTION_FLAG_AT_WAR.</summary>
        public const int FlagAttackPvpActivePlayers = 0x1000, FlagHatesAllExceptFriends = 0x2000, RepFlagAtWar = 0x02;
        /// <summary>ReputationRank: REP_HATED 0 .. REP_HOSTILE 1, UNFRIENDLY 2, NEUTRAL 3, FRIENDLY 4 .. EXALTED 7.</summary>
        public const int RepHostile = 1, RepUnfriendly = 2, RepNeutral = 3, RepFriendly = 4;

        readonly DbcTable _ft, _faction, _item, _goDisplay;
        public string Problem;           // why reactions are unavailable (null = fine)

        public OnlineDb(WowFiles files)
        {
            if (files == null) { Problem = "no game files"; return; }
            _ft = Dbc.Load(files, "FactionTemplate");
            _faction = Dbc.Load(files, "Faction");
            _item = Dbc.Load(files, "Item");
            _goDisplay = Dbc.Load(files, "GameObjectDisplayInfo");
            if (_ft != null && _ft.FieldCount != FtFieldCount) { Problem = "FactionTemplate.dbc has " + _ft.FieldCount + " fields (expected 14)"; _ft = null; }
            if (_faction != null && _faction.FieldCount != FFieldCount) { Problem = "Faction.dbc has " + _faction.FieldCount + " fields (expected 57)"; _faction = null; }
            if (_item != null && _item.FieldCount != DbcFields.Item.FieldCount) _item = null;
            if (_goDisplay != null && _goDisplay.FieldCount != DbcFields.GameObjectDisplayInfo.FieldCount) _goDisplay = null;
            if (_ft == null && Problem == null) Problem = "FactionTemplate.dbc missing";
        }

        public int RowCount(string which)
        {
            DbcTable t = which == "FactionTemplate" ? _ft : which == "Faction" ? _faction : which == "Item" ? _item : which == "GameObjectDisplayInfo" ? _goDisplay : null;
            return t == null ? -1 : t.RecordCount;
        }

        /// <summary>Item.dbc DisplayInfoID of an item entry (0 = unknown item or no files).</summary>
        public int ItemDisplayId(int itemEntry)
        {
            if (_item == null || itemEntry <= 0) return 0;
            int r = _item.FindRow(itemEntry);
            return r < 0 ? 0 : _item.GetInt(r, DbcFields.Item.DisplayInfoID);
        }

        /// <summary>GameObjectDisplayInfo ModelName (legacy .mdx/.mdl -> .m2, .wmo kept), or null.</summary>
        public string GameObjectModel(int displayId)
        {
            if (_goDisplay == null || displayId <= 0) return null;
            int r = _goDisplay.FindRow(displayId);
            if (r < 0) return null;
            string s = _goDisplay.GetString(r, DbcFields.GameObjectDisplayInfo.ModelName);
            return string.IsNullOrEmpty(s) ? null : M2Model.NormalizeModelPath(s);
        }

        /// <summary>Faction.dbc reputationListID of a faction template's faction, or -1.</summary>
        public int RepListIdOfTemplate(int factionTemplate)
        {
            int ft = _ft == null ? -1 : _ft.FindRow(factionTemplate);
            if (ft < 0 || _faction == null) return -1;
            int f = _faction.FindRow(_ft.GetInt(ft, FtFaction));
            return f < 0 ? -1 : _faction.GetInt(f, FRepListId);
        }

        bool IsHostileTo(int a, int b)
        {
            int bf = _ft.GetInt(b, FtFaction);
            if (bf != 0)
            {
                for (int i = 0; i < 4; i++) if (_ft.GetInt(a, FtEnemies + i) == bf) return true;
                for (int i = 0; i < 4; i++) if (_ft.GetInt(a, FtFriends + i) == bf) return false;
            }
            return (_ft.GetInt(a, FtHostileMask) & _ft.GetInt(b, FtOurMask)) != 0;
        }

        bool IsFriendlyTo(int a, int b)
        {
            int af = _ft.GetInt(a, FtFaction), bf = _ft.GetInt(b, FtFaction);
            if (af == bf) return true;                                            // "Always friendly to self faction"
            if (bf != 0)
            {
                for (int i = 0; i < 4; i++) if (_ft.GetInt(a, FtEnemies + i) == bf) return false;
                for (int i = 0; i < 4; i++) if (_ft.GetInt(a, FtFriends + i) == bf) return true;
            }
            return (_ft.GetInt(a, FtFriendlyMask) & _ft.GetInt(b, FtOurMask)) != 0 || (_ft.GetInt(a, FtOurMask) & _ft.GetInt(b, FtFriendlyMask)) != 0;
        }

        /// <summary>Unit::GetFactionReactionTo(FactionTemplateEntry, FactionTemplateEntry): template against template.</summary>
        public int TemplateRank(int templateA, int templateB)
        {
            if (_ft == null) return RepNeutral;
            int a = _ft.FindRow(templateA), b = _ft.FindRow(templateB);
            if (a < 0 || b < 0) return RepNeutral;
            if (IsHostileTo(a, b)) return RepHostile;
            if (IsFriendlyTo(a, b)) return RepFriendly;
            if (IsFriendlyTo(b, a)) return RepFriendly;
            if ((_ft.GetInt(a, FtFlags) & FlagHatesAllExceptFriends) != 0) return RepHostile;
            return RepNeutral;
        }

        /// <summary>
        /// Unit::GetFactionReactionTo(creature template, player): the contested-guard check, then - when the creature's faction can
        /// have reputation (Faction.dbc reputationListID &gt;= 0) - the player's rank there (ReputationMgr: base for race/class +
        /// standing from SMSG_INITIALIZE_FACTIONS; at war caps it at neutral), else template against template.
        /// </summary>
        public int RankToPlayer(int unitTemplate, int playerTemplate, int race, int cls, int[] standing, byte[] repFlags, bool contestedPvp)
        {
            if (_ft == null) return RepNeutral;
            int a = _ft.FindRow(unitTemplate);
            if (a < 0 || _ft.FindRow(playerTemplate) < 0) return RepNeutral;
            if ((_ft.GetInt(a, FtFlags) & FlagAttackPvpActivePlayers) != 0 && contestedPvp) return RepHostile;
            if (_faction != null)
            {
                int f = _faction.FindRow(_ft.GetInt(a, FtFaction));
                int rep = f < 0 ? -1 : _faction.GetInt(f, FRepListId);
                if (rep >= 0)
                {
                    int value = BaseReputation(f, race, cls) + (standing != null && rep < standing.Length ? standing[rep] : 0);
                    int rank = ReputationToRank(value);
                    if (repFlags != null && rep < repFlags.Length && (repFlags[rep] & RepFlagAtWar) != 0) rank = Math.Min(RepNeutral, rank);
                    return rank;
                }
            }
            return TemplateRank(unitTemplate, playerTemplate);
        }

        /// <summary>ReputationMgr::GetBaseReputation.</summary>
        int BaseReputation(int factionRow, int race, int cls)
        {
            uint raceMask = race > 0 && race <= 32 ? 1u << (race - 1) : 0u, classMask = cls > 0 && cls <= 32 ? 1u << (cls - 1) : 0u;
            for (int i = 0; i < 4; i++)
            {
                uint rm = _faction.GetUInt(factionRow, FRaceMask + i), cm = _faction.GetUInt(factionRow, FClassMask + i);
                if (((rm & raceMask) != 0 || (rm == 0 && cm != 0)) && ((cm & classMask) != 0 || cm == 0))
                    return _faction.GetInt(factionRow, FRepBase + i);
            }
            return 0;
        }

        static readonly int[] PointsInRank = { 36000, 3000, 3000, 3000, 6000, 12000, 21000, 1000 };

        /// <summary>ReputationMgr::ReputationToRank (Reputation_Cap 42999).</summary>
        public static int ReputationToRank(int standing)
        {
            int limit = 42999 + 1;
            for (int i = 7; i >= 0; --i)
            {
                limit -= PointsInRank[i];
                if (standing >= limit) return i;
            }
            return 0;
        }

        /// <summary>Hated/Hostile -> Hostile (AC IsHostileTo: rank &lt;= REP_HOSTILE), Unfriendly/Neutral -> Neutral, Friendly+ -> Friendly.</summary>
        public static Reaction ToReaction(int rank)
        {
            return rank <= RepHostile ? Reaction.Hostile : rank >= RepFriendly ? Reaction.Friendly : Reaction.Neutral;
        }

        /// <summary>ChrRaces "Alliance" field of the playable races (see WowFaction).</summary>
        public static WowFaction FactionOfRace(int race)
        {
            return race == 2 || race == 5 || race == 6 || race == 8 || race == 10 ? WowFaction.Horde : WowFaction.Alliance;
        }
    }
}
