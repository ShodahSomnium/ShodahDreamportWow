// iteration 11 - owner character (proposal-talents.md 3.6-3.10, 5.4, 6.1): reputation list + chat lines + at war / inactive / watched,
// titles, achievements, currencies and the character pane's extended stats (what the server sends to the owner).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;
using SomniumSpace.Worlds.SomniumWoW.Play.Rules;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    public sealed partial class OnlineSession
    {
        const int CpMaxSkills = 128, CpSkillDefense = 95;            // PLAYER_MAX_SKILLS (Player.h), SKILL_DEFENSE (SharedDefines.h)
        const int CpCrDefenseSkill = 1;                                  // CombatRating CR_DEFENSE_SKILL (Unit.h:224-251)

        // ---- reputation (the object manager's SMSG_INITIALIZE_FACTIONS / SET_FACTION_STANDING handlers run first and fill _repFlags /
        // _repStanding - WorldClient dispatches in registration order, Net/WorldClient.cs:190-197; these second handlers add the list + lines)
        readonly int[] _cpRepSeen = new int[128];                       // the standing (server delta) the player last saw, for "increased by"
        readonly bool[] _cpRepSeenOk = new bool[128];
        bool _cpRepInitialized, _cpRepListLogged;
        int _cpRepLines;

        // ---- self panes (titles, currencies, stats): read after every SMSG_UPDATE_OBJECT (this file's handler runs after the object manager's)
        ulong _cpTitles0, _cpTitles1, _cpTitles2;
        int _cpTitlesGender = -1, _cpTitlesUpdates;
        string _cpTitlesName;
        long _cpCurrencySig = long.MinValue;
        readonly Dictionary<int, ItemView> _cpCurrencyViews = new Dictionary<int, ItemView>();
        bool _cpPanesLogged, _cpStatsLogged;
        int _cpCurrencyLogs;

        // ---- achievements
        int _cpAchLogged;

        FactionDb CpFactions { get { return FactionDb.Get(_files); } }
        CharacterPanesDb CpPanes { get { return CharacterPanesDb.Get(_files); } }
        AchievementDb CpAchDb { get { return AchievementDb.Get(_files); } }

        partial void InitReputation()
        {
            if (_world == null) return;
            _world.On(Op.SMSG_INITIALIZE_FACTIONS, CpRepOnInitialize, this);
            _world.On(Op.SMSG_SET_FACTION_STANDING, CpRepOnStanding, this);
            _world.On(Op.SMSG_SET_FACTION_VISIBLE, CpRepOnVisible, this);
            _world.On(Op.SMSG_TITLE_EARNED, CpOnTitleEarned, this);
            _world.On(Op.SMSG_ALL_ACHIEVEMENT_DATA, CpOnAllAchievementData, this);
            _world.On(Op.SMSG_ACHIEVEMENT_EARNED, CpOnAchievementEarned, this);
            _world.On(Op.SMSG_CRITERIA_UPDATE, CpOnCriteriaUpdate, this);
            _world.On(Op.SMSG_CRITERIA_DELETED, CpOnCriteriaDeleted, this);
            _world.On(Op.SMSG_ACHIEVEMENT_DELETED, CpOnAchievementDeleted, this);
            _world.On(Op.SMSG_UPDATE_OBJECT, CpAfterUpdate, this);   // after RegisterObjectHandlers' OnUpdateObject (registered first)
        }

        // ================================================================== reputation (ReputationMgr.cpp)

        /// <summary>SMSG_INITIALIZE_FACTIONS (ReputationMgr.cpp:211-244): the object manager stored flags + standing; this one builds the list.</summary>
        void CpRepOnInitialize(WowPacket p)
        {
            for (int i = 0; i < 128; i++) { _cpRepSeen[i] = _repStanding[i]; _cpRepSeenOk[i] = true; }
            _cpRepInitialized = true;
            CpRepRebuild("initialize");
        }

        /// <summary>SMSG_SET_FACTION_STANDING (ReputationMgr.cpp:178-209): float 0, uint8 showVisual, uint32 count, count x (uint32 repListId,
        /// uint32 standing = the delta over the base). WoW's lines (ours: the client prints them from this packet, C code): FACTION_STANDING_INCREASED
        /// "Reputation with %s increased by %d." (GlobalStrings.lua:3648) / _DECREASED (:3646) for a shown faction, FACTION_STANDING_CHANGED
        /// "You are now %s with %s." (:3645) when the rank changes. A faction without a seen value prints nothing.</summary>
        void CpRepOnStanding(WowPacket p)
        {
            if (p.Remaining < 9) return;
            p.F32(); p.U8();
            uint n = p.U32();
            FactionDb db = CpFactions;
            for (uint i = 0; i < n && p.Remaining >= 8; i++)
            {
                uint id = p.U32();
                int standing = p.I32();
                if (id >= 128) continue;
                bool had = _cpRepSeenOk[id];
                int old = _cpRepSeen[id];
                _cpRepSeen[id] = standing;
                _cpRepSeenOk[id] = true;
                if (!had || old == standing || !FactionDb.Shown(_repFlags[id])) continue;
                string name = db.Name((int)id);
                if (string.IsNullOrEmpty(name)) continue;
                int delta = standing - old;
                CpSystemLine(delta > 0 ? "Reputation with " + name + " increased by " + delta + "." : "Reputation with " + name + " decreased by " + (-delta) + ".");
                int baseRep = db.BaseReputation((int)id, _player.Race, (int)_player.Class);
                int r0 = FactionDb.RankOf(baseRep + old), r1 = FactionDb.RankOf(baseRep + standing);
                if (r0 != r1) CpSystemLine("You are now " + CpRankLabel(r1) + " with " + name + ".");
                if (++_cpRepLines <= 10 || _cpRepLines % 50 == 0)
                    WowLog.Info("Online reputation: " + name + " " + (delta > 0 ? "+" : "") + delta + " -> " + CpRankLabel(r1) + " " + (baseRep + standing) + (_cpRepLines == 10 ? " (further changes every 50th)" : ""));
            }
            CpRepRebuild(null);
        }

        /// <summary>SMSG_SET_FACTION_VISIBLE (ReputationMgr.cpp:252-261): uint32 repListId - the faction joins the list.</summary>
        void CpRepOnVisible(WowPacket p)
        {
            if (p.Remaining < 4) return;
            uint id = p.U32();
            if (id >= 128) return;
            _repFlags[id] |= FactionDb.FlagVisible;
            WowLog.Info("Online reputation: " + (CpFactions.Name((int)id) ?? ("list id " + id)) + " is now listed (SMSG_SET_FACTION_VISIBLE)");
            RefreshAllReactions();
            CpRepRebuild(null);
        }

        /// <summary>FACTION_STANDING_LABEL1..8 (GlobalStrings.lua:3651-3666; the _FEMALE forms are the same words in enUS).</summary>
        internal static string CpRankLabel(int rank)
        {
            switch (rank)
            {
                case 0: return "Hated";
                case 1: return "Hostile";
                case 2: return "Unfriendly";
                case 3: return "Neutral";
                case 4: return "Friendly";
                case 5: return "Honored";
                case 6: return "Revered";
                default: return "Exalted";
            }
        }

        void CpRepRebuild(string why)
        {
            FactionDb db = CpFactions;
            var list = new List<FactionView>();
            db.BuildList(_player.Race, (int)_player.Class, _repStanding, _repFlags, _player.WatchedFaction, list);
            _player.Reputations = list;
            _player.ReputationCounter++;
            if (why != null || !_cpRepListLogged)
            {
                _cpRepListLogged = true;
                int factions = 0;
                for (int i = 0; i < list.Count; i++) if (!list[i].IsHeader || list[i].HasRep) factions++;
                WowLog.Info("Online reputation: list of " + list.Count + " rows (" + factions + " with a standing)" + (why != null ? " after " + why : "") + (db.Available ? "" : " - Faction.dbc missing"));
            }
        }

        /// <summary>CMSG_SET_FACTION_ATWAR (CharacterHandler.cpp:1282-1291): uint32 repListId, uint8 atWar. The server never echoes the flag
        /// (SetAtWar only marks needSend; SendState carries the standing) - our copy follows the server's own rules (ReputationMgr.cpp:504-534).</summary>
        public void SetFactionAtWar(int repListId, bool atWar)
        {
            if (repListId < 0 || repListId >= 128) return;
            int flags = _repFlags[repListId];
            if ((flags & (FactionDb.FlagInvisibleForced | FactionDb.FlagHidden)) != 0 || (atWar && (flags & FactionDb.FlagPeaceForced) != 0))
            {
                WowLog.Info("Online reputation: at war with list id " + repListId + " refused locally (hidden / peace forced - the server would refuse too)");
                return;
            }
            if (((flags & FactionDb.FlagAtWar) != 0) == atWar) return;
            if (_world != null) _world.Send(Op.CMSG_SET_FACTION_ATWAR, new PacketWriter().U32((uint)repListId).U8(atWar ? 1 : 0));
            _repFlags[repListId] = (byte)(atWar ? flags | FactionDb.FlagAtWar : flags & ~FactionDb.FlagAtWar);
            WowLog.Info("Online reputation: " + (CpFactions.Name(repListId) ?? ("list id " + repListId)) + (atWar ? " - at war" : " - at peace") + " (CMSG_SET_FACTION_ATWAR)");
            RefreshAllReactions();
            CpRepRebuild(null);
        }

        /// <summary>CMSG_SET_FACTION_INACTIVE (CharacterHandler.cpp:1335-1342): uint32 repListId, uint8 inactive (refused for hidden / not
        /// visible factions, ReputationMgr.cpp:545-562 - checked here first; our copy is optimistic like the server's).</summary>
        public void SetFactionInactive(int repListId, bool inactive)
        {
            if (repListId < 0 || repListId >= 128) return;
            int flags = _repFlags[repListId];
            if (inactive && ((flags & (FactionDb.FlagInvisibleForced | FactionDb.FlagHidden)) != 0 || (flags & FactionDb.FlagVisible) == 0))
            {
                WowLog.Info("Online reputation: inactive for list id " + repListId + " refused locally (hidden / not listed)");
                return;
            }
            if (((flags & FactionDb.FlagInactive) != 0) == inactive) return;
            if (_world != null) _world.Send(Op.CMSG_SET_FACTION_INACTIVE, new PacketWriter().U32((uint)repListId).U8(inactive ? 1 : 0));
            _repFlags[repListId] = (byte)(inactive ? flags | FactionDb.FlagInactive : flags & ~FactionDb.FlagInactive);
            WowLog.Info("Online reputation: " + (CpFactions.Name(repListId) ?? ("list id " + repListId)) + (inactive ? " moved to Inactive" : " active again") + " (CMSG_SET_FACTION_INACTIVE)");
            CpRepRebuild(null);
        }

        /// <summary>CMSG_SET_WATCHED_FACTION (CharacterHandler.cpp:1328-1333): uint32 repListId, -1 = none -> PLAYER_FIELD_WATCHED_FACTION_INDEX
        /// (read back in CpReadSelf).</summary>
        public void SetWatchedFaction(int repListId)
        {
            if (repListId < -1 || repListId >= 128) return;
            if (_world != null) _world.Send(Op.CMSG_SET_WATCHED_FACTION, new PacketWriter().U32(unchecked((uint)repListId)));
            if (_player.WatchedFaction == repListId) return;
            _player.WatchedFaction = repListId;
            WowLog.Info("Online reputation: watched faction " + (repListId < 0 ? "none" : CpFactions.Name(repListId) ?? ("list id " + repListId)) + " (CMSG_SET_WATCHED_FACTION)");
            CpRepRebuild(null);
        }

        // ================================================================== titles (Player.cpp:13659-13698, MiscHandler.cpp:1236-1252)

        /// <summary>CMSG_SET_TITLE: int32 bit_index (0 / -1 = none; a title the player does not know is ignored by the server).</summary>
        public void SetTitle(int bitIndex)
        {
            if (bitIndex < 0) bitIndex = 0;
            if (bitIndex != 0 && !CpHasTitle(bitIndex))
            {
                WowLog.Info("Online titles: title bit " + bitIndex + " is not known - nothing sent");
                return;
            }
            if (_world != null) _world.Send(Op.CMSG_SET_TITLE, new PacketWriter().U32((uint)bitIndex));
            _player.ChosenTitle = bitIndex;
            WowLog.Info("Online titles: chose " + (bitIndex == 0 ? "no title" : "'" + CpTitleText(bitIndex) + "'") + " (CMSG_SET_TITLE)");
        }

        bool CpHasTitle(int bit)
        {
            if (bit <= 0 || bit >= 192) return false;
            ulong w = bit < 64 ? _cpTitles0 : bit < 128 ? _cpTitles1 : _cpTitles2;
            return (w & (1UL << (bit & 63))) != 0;
        }

        string CpTitleText(int bit)
        {
            return CharacterPanesDb.FillTitle(CpPanes.TitleFormat(bit, _player.Gender), _player.Name);
        }

        /// <summary>SMSG_TITLE_EARNED (Player.cpp:13695-13698): uint32 bit_index, uint32 1 earned / 0 lost -> NEW_TITLE_EARNED "You have earned
        /// the title '%s'." (GlobalStrings.lua:5177) / OLD_TITLE_LOST "You have lost the title '%s'." (:5235) as a system line.</summary>
        void CpOnTitleEarned(WowPacket p)
        {
            if (p.Remaining < 8) return;
            int bit = (int)p.U32();
            bool earned = p.U32() != 0;
            string t = CpTitleText(bit);
            CpSystemLine(earned ? "You have earned the title '" + t + "'." : "You have lost the title '" + t + "'.");
            WowLog.Info("Online titles: " + (earned ? "earned" : "lost") + " title bit " + bit + " '" + t + "'");
        }

        // ================================================================== achievements (AchievementMgr.cpp)

        /// <summary>SMSG_ALL_ACHIEVEMENT_DATA (AchievementMgr.cpp:2400-2448): { uint32 id, uint32 packedTime } until int32 -1, then { uint32
        /// criteriaId, packed counter (a uint64 written like a packed guid), packed guid, uint32 0, uint32 packedTime, uint32 elapsed, uint32
        /// elapsed } until int32 -1.</summary>
        void CpOnAllAchievementData(WowPacket p)
        {
            AchievementsView a = _player.Achievements;
            a.Completed.Clear();
            a.Criteria.Clear();
            while (p.Remaining >= 4)
            {
                int id = p.I32();
                if (id == -1 || p.Remaining < 4) break;
                a.Completed[id] = AchievementDb.UnpackTime(p.U32());
            }
            while (p.Remaining >= 4)
            {
                int id = p.I32();
                if (id == -1) break;
                ulong counter = p.PackedGuid();
                p.PackedGuid();
                if (p.Remaining < 16) break;
                p.U32(); p.U32(); p.U32(); p.U32();
                a.Criteria[id] = (long)counter;
            }
            a.Points = CpAchievementPoints(a);
            a.Known = true;
            a.Counter++;
            WowLog.Info("Online achievements: " + a.Completed.Count + " earned (" + a.Points + " points), " + a.Criteria.Count + " criteria in progress (SMSG_ALL_ACHIEVEMENT_DATA)"
                        + (CpAchDb.Available ? "" : " - Achievement.dbc missing"));
        }

        int CpAchievementPoints(AchievementsView a)
        {
            AchievementDb db = CpAchDb;
            int pts = 0;
            foreach (int id in a.Completed.Keys) pts += db.Points(id);
            return pts;
        }

        /// <summary>SMSG_ACHIEVEMENT_EARNED (AchievementMgr.cpp:765-770): packed guid earner, uint32 id, uint32 packedTime, uint32 0 - to everyone
        /// in say range: only our own earns become the toast (other players' come as the CHAT_MSG_ACHIEVEMENT line, area social).</summary>
        void CpOnAchievementEarned(WowPacket p)
        {
            ulong earner = p.PackedGuid();
            if (p.Remaining < 8 || earner != _player.Guid) return;
            int id = (int)p.U32();
            long when = AchievementDb.UnpackTime(p.U32());
            AchievementsView a = _player.Achievements;
            a.Completed[id] = when;
            a.Points = CpAchievementPoints(a);
            a.Counter++;
            string name = CpAchDb.Name(id) ?? ("Achievement " + id);
            _events.Enqueue(new GameEvent { Type = GameEventType.AchievementEarned, Amount = id, SourceGuid = _player.Guid, TargetGuid = _player.Guid, Text = name });
            WowLog.Info("Online achievements: earned " + id + " '" + name + "' (" + CpAchDb.Points(id) + " points, total " + a.Points + ")");
        }

        /// <summary>SMSG_CRITERIA_UPDATE (AchievementMgr.cpp:773-795): uint32 criteriaId, packed counter, packed guid, uint32 timed, uint32 packedTime,
        /// uint32 elapsed, uint32 elapsed-or-average.</summary>
        void CpOnCriteriaUpdate(WowPacket p)
        {
            if (p.Remaining < 5) return;
            int id = (int)p.U32();
            ulong counter = p.PackedGuid();
            AchievementsView a = _player.Achievements;
            a.Criteria[id] = (long)counter;
            a.Counter++;
            if (++_cpAchLogged <= 10) WowLog.Info("Online achievements: criteria " + id + " = " + counter + (_cpAchLogged == 10 ? " (further criteria updates not logged)" : ""));
        }

        /// <summary>SMSG_CRITERIA_DELETED (AchievementMgr.cpp:2189-2199): uint32 criteriaId.</summary>
        void CpOnCriteriaDeleted(WowPacket p)
        {
            if (p.Remaining < 4) return;
            if (_player.Achievements.Criteria.Remove((int)p.U32())) _player.Achievements.Counter++;
        }

        /// <summary>SMSG_ACHIEVEMENT_DELETED: uint32 achievementId.</summary>
        void CpOnAchievementDeleted(WowPacket p)
        {
            if (p.Remaining < 4) return;
            AchievementsView a = _player.Achievements;
            int id = (int)p.U32();
            if (!a.Completed.Remove(id)) return;
            a.Points = CpAchievementPoints(a);
            a.Counter++;
            WowLog.Info("Online achievements: achievement " + id + " removed by the server");
        }

        /// <summary>A system chat line of ours (no HUD message - WoW shows these in the chat frame only).</summary>
        void CpSystemLine(string text)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.ChatMessage, Amount = (int)ChatKind.System, Text = text });
        }

        // ================================================================== self panes (UpdateFields.cs; proposal 3.7, 3.8, 3.10)

        void CpAfterUpdate(WowPacket p)
        {
            try { CpReadSelf(); }
            catch (Exception e) { if (!_cpPanesLogged) { _cpPanesLogged = true; WowLog.Error("Online: character panes failed: " + e); } }
        }

        /// <summary>Titles, watched faction, currencies, lifetime honorable kills and the extended stats from our own object's fields.</summary>
        internal void CpReadSelf()
        {
            OnlineObject o = _self;
            if (o == null) return;
            PlayerState pl = _player;
            // titles: PLAYER__FIELD_KNOWN_TITLES (3 x uint64, bit i = CharTitles bit_index i, Player::CpHasTitle Player.cpp:13659-13667)
            ulong t0 = o.U64(UF.PLAYER__FIELD_KNOWN_TITLES), t1 = o.U64(UF.PLAYER__FIELD_KNOWN_TITLES1), t2 = o.U64(UF.PLAYER__FIELD_KNOWN_TITLES2);
            if (t0 != _cpTitles0 || t1 != _cpTitles1 || t2 != _cpTitles2 || pl.Gender != _cpTitlesGender || !string.Equals(pl.Name, _cpTitlesName))
            {
                _cpTitles0 = t0; _cpTitles1 = t1; _cpTitles2 = t2; _cpTitlesGender = pl.Gender; _cpTitlesName = pl.Name;
                CpRebuildTitles();
            }
            pl.ChosenTitle = o.I32(UF.PLAYER_CHOSEN_TITLE);
            int watched = o.I32(UF.PLAYER_FIELD_WATCHED_FACTION_INDEX);
            if (watched < -1 || watched >= 128) watched = -1;
            if (watched != pl.WatchedFaction)
            {
                pl.WatchedFaction = watched;
                if (_cpRepInitialized) CpRepRebuild(null);
            }
            pl.LifetimeHonorableKills = o.I32(UF.PLAYER_FIELD_LIFETIME_HONORABLE_KILLS);
            pl.HonorPoints = o.I32(UF.PLAYER_FIELD_HONOR_CURRENCY);     // area npc writes the same two fields (idempotent)
            pl.ArenaPoints = o.I32(UF.PLAYER_FIELD_ARENA_CURRENCY);
            CpReadCurrencies(o, pl);
            CpReadExtendedStats(o, pl);
        }

        void CpRebuildTitles()
        {
            CharacterPanesDb db = CpPanes;
            var list = new List<TitleView>();
            IReadOnlyList<int> bits = db.TitleBits;
            for (int i = 0; i < bits.Count; i++)
                if (CpHasTitle(bits[i])) list.Add(new TitleView { BitIndex = bits[i], Format = db.TitleFormat(bits[i], _player.Gender) });
            string name = _player.Name;
            // PlayerTitleFrame_UpdateTitles (PaperDollFrame.lua:2591-2615) sorts the filled names
            list.Sort((a, b) => string.Compare(CharacterPanesDb.FillTitle(a.Format, name), CharacterPanesDb.FillTitle(b.Format, name), StringComparison.OrdinalIgnoreCase));
            _player.KnownTitles = list;
            if (++_cpTitlesUpdates <= 5) WowLog.Info("Online titles: " + list.Count + " known" + (list.Count > 0 ? " (first '" + CharacterPanesDb.FillTitle(list[0].Format, name) + "')" : ""));
        }

        /// <summary>PLAYER_FIELD_KNOWN_CURRENCIES (uint64, bit BitIndex-1, Player::AddKnownCurrency Player.cpp:14554-14559); honor / arena points
        /// are fields (PLAYER_FIELD_HONOR_CURRENCY / _ARENA_CURRENCY), every other currency is an item in the currency token bag
        /// (PLAYER_FIELD_CURRENCYTOKEN_SLOT_1, 32 x uint64 guids) whose ITEM_FIELD_STACK_COUNT is the count.</summary>
        void CpReadCurrencies(OnlineObject o, PlayerState pl)
        {
            ulong known = o.U64(UF.PLAYER_FIELD_KNOWN_CURRENCIES);
            long sig = (long)known * 31 + pl.HonorPoints * 7919L + pl.ArenaPoints * 104729L;
            for (int i = 0; i < 32; i++)
            {
                ulong g = o.U64(UF.PLAYER_FIELD_CURRENCYTOKEN_SLOT_1 + i * 2);
                if (g == 0) continue;
                OnlineObject it;
                int entry = 0, count = 0;
                if (_objects.TryGetValue(g, out it)) { entry = it.Entry; count = it.I32(UF.ITEM_FIELD_STACK_COUNT); }
                sig = sig * 131 + (long)(g & 0xFFFFFF) * 17 + entry * 3 + count;
            }
            if (sig == _cpCurrencySig) return;
            _cpCurrencySig = sig;
            CharacterPanesDb db = CpPanes;
            var list = new List<CurrencyView>();
            IReadOnlyList<CurrencyTypeInfo> types = db.Currencies;
            for (int k = 0; k < types.Count; k++)
            {
                CurrencyTypeInfo t = types[k];
                if ((known & (1UL << (t.BitIndex - 1))) == 0) continue;
                int count = t.ItemEntry == CharacterPanesDb.HonorPointsItem ? pl.HonorPoints : t.ItemEntry == CharacterPanesDb.ArenaPointsItem ? pl.ArenaPoints : CpTokenCount(o, t.ItemEntry);
                ItemView view;
                if (!_cpCurrencyViews.TryGetValue(t.ItemEntry, out view)) { view = ItemInfoView(t.ItemEntry, 1); _cpCurrencyViews[t.ItemEntry] = view; }
                list.Add(new CurrencyView { ItemEntry = t.ItemEntry, CategoryId = t.CategoryId, CategoryName = db.CategoryName(t.CategoryId), Item = view, Count = count });
            }
            pl.Currencies = list;
            if (++_cpCurrencyLogs <= 5) WowLog.Info("Online currencies: " + list.Count + " known (honor " + pl.HonorPoints + ", arena " + pl.ArenaPoints + ")" + (_cpCurrencyLogs == 5 ? " (further changes not logged)" : ""));
        }

        int CpTokenCount(OnlineObject o, int entry)
        {
            int n = 0;
            for (int i = 0; i < 32; i++)
            {
                ulong g = o.U64(UF.PLAYER_FIELD_CURRENCYTOKEN_SLOT_1 + i * 2);
                OnlineObject it;
                if (g != 0 && _objects.TryGetValue(g, out it) && it.Entry == entry) n += Math.Max(1, it.I32(UF.ITEM_FIELD_STACK_COUNT));
            }
            return n;
        }

        /// <summary>proposal 3.10: the owner-only stat fields of the character pane (base stats themselves are RefreshSelf's).</summary>
        void CpReadExtendedStats(OnlineObject o, PlayerState pl)
        {
            StatsView st = pl.Stats;
            if (st == null) return;
            for (int i = 0; i < 5; i++)
            {
                st.StatPos[i] = o.I32(UF.UNIT_FIELD_POSSTAT0 + i);    // uint32(float) on the wire (Unit.cpp:16447-16453): >= 0
                st.StatNeg[i] = o.I32(UF.UNIT_FIELD_NEGSTAT0 + i);    // <= 0
            }
            for (int i = 0; i < 7; i++) st.Resist[i] = o.I32(UF.UNIT_FIELD_RESISTANCES + i);
            int apMod = o.I32(UF.UNIT_FIELD_ATTACK_POWER_MODS);       // AC writes ONE int32 total modifier (StatSystem.cpp:521) - split by sign (ours)
            st.AttackPowerPos = Math.Max(0, apMod); st.AttackPowerNeg = Math.Min(0, apMod);
            int offMs = o.I32(UF.UNIT_FIELD_BASEATTACKTIME + 1);
            st.OffMinDamage = o.F32(UF.UNIT_FIELD_MINOFFHANDDAMAGE);
            st.OffMaxDamage = o.F32(UF.UNIT_FIELD_MAXOFFHANDDAMAGE);
            st.OffAttackSpeed = st.OffMaxDamage > 0f && offMs > 0 ? offMs / 1000f : 0f;
            st.RangedAttackPower = o.I32(UF.UNIT_FIELD_RANGED_ATTACK_POWER);
            int rapMod = o.I32(UF.UNIT_FIELD_RANGED_ATTACK_POWER_MODS);
            st.RangedAttackPowerPos = Math.Max(0, rapMod); st.RangedAttackPowerNeg = Math.Min(0, rapMod);
            st.RangedMinDamage = o.F32(UF.UNIT_FIELD_MINRANGEDDAMAGE);
            st.RangedMaxDamage = o.F32(UF.UNIT_FIELD_MAXRANGEDDAMAGE);
            st.RangedSpeed = o.I32(UF.UNIT_FIELD_RANGEDATTACKTIME) / 1000f;
            st.RangedCritChance = o.F32(UF.PLAYER_RANGED_CRIT_PERCENTAGE);
            st.OffhandCritChance = o.F32(UF.PLAYER_OFFHAND_CRIT_PERCENTAGE);
            for (int i = 0; i < 7; i++)
            {
                st.SpellCrit[i] = o.F32(UF.PLAYER_SPELL_CRIT_PERCENTAGE1 + i);
                st.SpellDamage[i] = o.I32(UF.PLAYER_FIELD_MOD_DAMAGE_DONE_POS + i) + o.I32(UF.PLAYER_FIELD_MOD_DAMAGE_DONE_NEG + i);   // NEG holds the negative mods
            }
            st.BonusHealing = o.I32(UF.PLAYER_FIELD_MOD_HEALING_DONE_POS);
            st.SpellPenetration = -o.I32(UF.PLAYER_FIELD_MOD_TARGET_RESISTANCE);   // stored negative (StatSystem.cpp:238)
            // per second on the wire (Player::UpdateManaRegen, StatSystem.cpp:939-971); WoW's pane shows floor(x * 5) (PaperDollFrame.lua:1012-1015)
            st.ManaRegen = o.F32(UF.UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER) * 5f;
            st.ManaRegenCasting = o.F32(UF.UNIT_FIELD_POWER_REGEN_INTERRUPTED_FLAT_MODIFIER) * 5f;
            st.ParryChance = o.F32(UF.PLAYER_PARRY_PERCENTAGE);
            st.BlockChance = o.F32(UF.PLAYER_BLOCK_PERCENTAGE);
            st.BlockValue = o.I32(UF.PLAYER_SHIELD_BLOCK);
            st.Expertise = o.I32(UF.PLAYER_EXPERTISE);
            st.OffhandExpertise = o.I32(UF.PLAYER_OFFHAND_EXPERTISE);
            CharacterPanesDb db = CpPanes;
            for (int i = 0; i < 25; i++)
            {
                int r = o.I32(UF.PLAYER_FIELD_COMBAT_RATING_1 + i);
                st.Ratings[i] = r;
                st.RatingBonus[i] = r != 0 ? db.RatingBonus(i, r, pl.Class, pl.Level) : 0f;
            }
            // defense skill: PLAYER_SKILL_INFO_1_1 (3 fields per skill: id | step << 16, value | max << 16, temp bonus | perm bonus << 16)
            int defense = 0;
            for (int i = 0; i < CpMaxSkills; i++)
            {
                int idx = UF.PLAYER_SKILL_INFO_1_1 + i * 3;
                if (o.U16(idx, 0) != CpSkillDefense) continue;
                defense = o.U16(idx + 1, 0) + (short)o.U16(idx + 2, 0) + (short)o.U16(idx + 2, 1);
                break;
            }
            st.Defense = defense;
            st.DefenseRatingBonus = (int)st.RatingBonus[CpCrDefenseSkill];        // Player::GetDefenseSkillValue adds uint32(rating bonus)
            if (!_cpStatsLogged && pl.Level > 0 && st.Resist[0] > 0)
            {
                _cpStatsLogged = true;
                WowLog.Info("Online stats: armor " + st.Resist[0] + ", resistances " + st.Resist[1] + "/" + st.Resist[2] + "/" + st.Resist[3] + "/" + st.Resist[4] + "/" + st.Resist[5] + "/" + st.Resist[6]
                            + ", ranged AP " + st.RangedAttackPower + ", spell crit " + st.SpellCrit[1].ToString("0.00") + "%, defense " + st.Defense + " (+" + st.DefenseRatingBonus + "), mana regen "
                            + (int)st.ManaRegen + "/" + (int)st.ManaRegenCasting + " per 5 s (logged once)");
            }
        }
    }
}
