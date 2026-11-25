// iteration 11 - quest map: WotLK 3.3's quest POIs online (the world map's quest areas, the tracker's POI numbers).
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Play.Online.Net;

namespace SomniumSpace.Worlds.SomniumWoW.Play.Online
{
    /// <summary>
    /// Quest POIs from the server. CMSG_QUEST_POI_QUERY (AzerothCore QueryHandler.cpp:411-425 HandleQuestPOIQuery): uint32 count ("quest
    /// count, max=25" - a count over MAX_QUEST_LOG_SIZE 25 is dropped unanswered, QuestDef.h:33), count x uint32 quest id.
    /// SMSG_QUEST_POI_QUERY_RESPONSE (:427-478): uint32 count, per quest uint32 quest id, uint32 POI count (0 when the quest is not in the
    /// player's log or has no quest_poi rows), per POI uint32 Id, int32 ObjectiveIndex, uint32 MapId, uint32 AreaId (quest_poi.WorldMapAreaId),
    /// uint32 FloorId, uint32 Unk3 (Priority), uint32 Unk4 (Flags), uint32 point count, per point int32 x, int32 y.
    /// When the client asks is engine code (not readable): ours - every quest of the log once per session, in one query of at most 25 ids
    /// sent after the log refresh; the answer rebuilds the log views (QuestView.Pois).
    /// </summary>
    public sealed partial class OnlineSession
    {
        const int QPoiMaxPerQuery = 25;                                  // MAX_QUEST_LOG_SIZE (QuestDef.h:33): HandleQuestPOIQuery drops a larger count
        readonly Dictionary<int, List<QuestPoiView>> _qPois = new Dictionary<int, List<QuestPoiView>>();
        readonly HashSet<int> _qPoiAsked = new HashSet<int>();
        readonly List<int> _qPoiWant = new List<int>();
        int _qPoiAnswers, _qPoiAreas;
        bool _qPoiLogged;

        void InitQuestPoi()
        {
            QOn(Op.SMSG_QUEST_POI_QUERY_RESPONSE, OnQPoiResponse);
            WowLog.Info("Online quests: POI - CMSG_QUEST_POI_QUERY for every quest of the log (ours: once per quest, at most " + QPoiMaxPerQuery +
                        " ids per query, HandleQuestPOIQuery's limit); the answer feeds the WoW Map's quest areas and the tracker's POI numbers.");
        }

        /// <summary>The log refresh: the view gets the known POIs (null until answered); a quest never asked is queued for QPoiFlush.</summary>
        void QPoiAttach(QuestView v)
        {
            List<QuestPoiView> l;
            if (_qPois.TryGetValue(v.Id, out l)) { v.Pois = l; return; }
            if (!_qPoiAsked.Contains(v.Id) && !_qPoiWant.Contains(v.Id)) _qPoiWant.Add(v.Id);
        }

        /// <summary>Sends the queued quest ids (TickQuests, after the log refresh).</summary>
        void QPoiFlush()
        {
            if (_qPoiWant.Count == 0 || _world == null) return;
            int n = Math.Min(QPoiMaxPerQuery, _qPoiWant.Count);
            var w = new PacketWriter().U32((uint)n);
            for (int i = 0; i < n; i++) { w.U32((uint)_qPoiWant[i]); _qPoiAsked.Add(_qPoiWant[i]); }
            _qPoiWant.RemoveRange(0, n);
            _world.Send(Op.CMSG_QUEST_POI_QUERY, w);
        }

        /// <summary>SMSG_QUEST_POI_QUERY_RESPONSE (see the class summary). A short packet keeps what was read before it ended (QOn logs it once).</summary>
        void OnQPoiResponse(WowPacket p)
        {
            uint count = p.U32();
            int areas = 0;
            for (uint q = 0; q < count; q++)
            {
                int questId = (int)p.U32();
                uint pois = p.U32();
                var list = new List<QuestPoiView>((int)Math.Min(pois, 64u));
                for (uint i = 0; i < pois; i++)
                {
                    var v = new QuestPoiView();
                    v.Id = (int)p.U32();
                    v.ObjectiveIndex = p.I32();
                    v.MapId = (int)p.U32();
                    v.WorldMapAreaId = (int)p.U32();
                    v.Floor = (int)p.U32();
                    v.Priority = (int)p.U32();
                    v.Flags = (int)p.U32();
                    uint points = p.U32();
                    if (points > (uint)(p.Remaining / 8)) throw new PacketTooShortException("SMSG_QUEST_POI_QUERY_RESPONSE: " + points + " points do not fit");
                    v.X = new int[points];
                    v.Y = new int[points];
                    for (uint k = 0; k < points; k++) { v.X[k] = p.I32(); v.Y[k] = p.I32(); }
                    list.Add(v);
                }
                _qPois[questId] = list;
                _qPoiAsked.Add(questId);
                areas += list.Count;
                _qPoiAnswers++;
            }
            _qPoiAreas += areas;
            _qLogDirty = true;
            if (!_qPoiLogged || count > 0 && areas > 0 && _qPoiAnswers <= 25)
            {
                _qPoiLogged = true;
                WowLog.Info("Online quests: POI - the server answered " + count + " quest(s), " + areas + " area(s) (" + _qPoiAnswers + " quests answered this session)");
            }
        }

        /// <summary>For the quest summary line: "POIs n quests / m areas".</summary>
        string QPoiSummary { get { return "POIs " + _qPois.Count + " quests / " + _qPoiAreas + " areas"; } }
    }
}
