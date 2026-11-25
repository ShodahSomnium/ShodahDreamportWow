// iteration 11 - quest map: WotLK 3.3's quest POIs (QuestPOI.lua, WorldMapFrame.lua, WatchFrame.lua, QuestLogFrame "Show Map") on the WoW Map,
// the objectives tracker and the quest log.
using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Game.Ui;
using SomniumSpace.Worlds.SomniumWoW.Play;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game.MapApp
{
    /// <summary>
    /// A WorldMapArea.dbc row, measured by the harness probe QuestMapData (108 rows x 11 fields): [0] ID, [1] map, [2] AreaTable id,
    /// [3] the art directory (Interface\WorldMap\&lt;dir&gt;\&lt;dir&gt;1..12), [4..7] locLeft / locRight / locTop / locBottom (yards; left = the
    /// largest y, top = the largest x: Western Plaguelands 416.7 / -3883.3 / 3366.7 / 500.0).
    /// </summary>
    public sealed class WowZoneInfo
    {
        public int Id, MapId, AreaId;
        public string Dir, Name;
        public float Left, Right, Top, Bottom;

        /// <summary>WoW world x / y to the map's 0..1 (GetPlayerMapPosition's space: x across from the left, y down from the top).</summary>
        public void ToUv(float x, float y, out float u, out float v)
        {
            u = Left != Right ? (Left - y) / (Left - Right) : 0f;
            v = Top != Bottom ? (Top - x) / (Top - Bottom) : 0f;
        }

        public bool Contains(int mapId, float x, float y)
        {
            if (mapId != MapId) return false;
            float u, v;
            ToUv(x, y, out u, out v);
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }
    }

    /// <summary>One overlay tile of a zone map, in WorldMapDetailFrame pixels (1002 x 668), as WorldMapFrame_Update places it (WorldMapFrame.lua:303-352).</summary>
    public struct WowZoneOverlay
    {
        public string Path;
        public float X, Y, W, H;      // TOPLEFT offset and size
        public float U, V;            // texcoords right / bottom (left = top = 0) with the Lua's assumed file size (WowMapPanel uses the file's real size)
    }

    /// <summary>One quest on a map: WorldMapFrame_UpdateQuests' quest frame (and the tracker's POI button).</summary>
    public sealed class QuestMapEntry
    {
        public int QuestId;
        public QuestView Quest;
        public bool Complete;
        public int Number;            // QUEST_POI_NUMERIC index (1..); 0 for a complete quest (the turn-in icon)
        public bool HasIcon;
        public float IconX, IconY;    // WoW yards
        public readonly List<QuestPoiView> Blob = new List<QuestPoiView>();   // the areas DrawQuestBlob(questId, true) draws when it is selected
    }

    /// <summary>
    /// WotLK 3.3's quest map state and rules. What FrameXML shows (quoted where used): the world map lists the shown zone's quests with POIs,
    /// numbered (QUEST_POI_NUMERIC, "index - numCompletedQuests", WorldMapFrame.lua:1702-1708), complete ones with the turn-in icon
    /// (QUEST_POI_COMPLETE_SWAP); only the selected, unfinished quest's blob is drawn (WorldMapFrame_SelectQuestFrame :1688-1692, every other
    /// DrawQuestBlob(..., false)); "Show Quest Objectives" (CVar questPOI) switches it all, the tracker's POI buttons and the quest log's
    /// Show Map button (WorldMapQuestShowObjectives_Toggle :1424-1433). Engine functions (QuestMapUpdateAllQuests, QuestPOIGetIconInfo,
    /// DrawQuestBlob, GetQuestWorldMapAreaID) are not readable: their rules here are OURS and say so.
    /// </summary>
    public static class WowQuestMap
    {
        /// <summary>WorldMapQuestShowObjectives (CVar questPOI). Ours: on at start, this session only (no settings entry is ours to add).</summary>
        public static bool ShowObjectives = true;
        public static int SelectedQuestId;
        /// <summary>The WorldMapArea ID of the zone the player stands in (WowTabletMap's area lookup, once a second), 0 = unknown.</summary>
        public static int PlayerZone;
        /// <summary>The zone the World Map view shows; 0 = the player's zone (WorldMap_OpenToQuest sets the quest's zone).</summary>
        public static int ShownZone;
        public static int Serial { get; private set; }

        public const float IconUnits = 28f;   // ours: the 32-unit QuestPOITemplate at WoW's ratio to the 0.691-scaled 1002-wide map (32 / 692 of our 608)
        public const float FillAlpha = 128f / 255f, BorderAlpha = 192f / 255f;   // WorldMapBlobFrame_OnLoad: SetFillAlpha(128), SetBorderAlpha(192) (WorldMapFrame.lua:1911-1917)
        public const string FillTex = @"Interface\WorldMap\UI-QuestBlob-Inside.blp", BorderTex = @"Interface\WorldMap\UI-QuestBlob-Outside.blp";   // :1912-1913

        private static readonly Dictionary<int, WowZoneInfo> Zones = new Dictionary<int, WowZoneInfo>();
        private static readonly Dictionary<int, List<WowZoneOverlay>> Overlays = new Dictionary<int, List<WowZoneOverlay>>();
        private static readonly List<QuestView> Order = new List<QuestView>();
        private static readonly List<QuestMapEntry> PlayerList = new List<QuestMapEntry>(), PlayerPool = new List<QuestMapEntry>();
        private static int _playerListFrame = -1;
        private static bool _logged;

        public static void Select(int questId)
        {
            if (questId == SelectedQuestId) return;
            SelectedQuestId = questId;
            Serial++;
        }

        public static void SetShowObjectives(bool on)
        {
            if (on == ShowObjectives) return;
            ShowObjectives = on;
            Serial++;
            WowLog.Info("UI quest map: Show Quest Objectives " + (on ? "on" : "off") + " (questPOI; ours: this session only).");
        }

        /// <summary>
        /// WorldMap_OpenToQuest (WorldMapFrame.lua:1964-1976): shows the map, SetMapByID(GetQuestWorldMapAreaID(questID)), selects the quest.
        /// GetQuestWorldMapAreaID is engine code - ours: the zone of the POI the quest's icon uses, else the player's zone.
        /// </summary>
        public static void OpenToQuest(PlayerState p, int questId)
        {
            QuestView q = Find(p, questId);
            int zone = q != null ? QuestZone(q) : 0;
            ShownZone = zone;
            SelectedQuestId = questId;
            Serial++;
            WowTabletMap.OpenQuestMap("quest " + questId + (zone > 0 ? " in zone " + zone : " (no POI: the player's zone)"));
        }

        /// <summary>Ours (GetQuestWorldMapAreaID): the WorldMapAreaId of the POI the icon would use.</summary>
        public static int QuestZone(QuestView q)
        {
            if (q == null || q.Pois == null || q.Pois.Count == 0) return 0;
            QuestPoiView icon = IconPoi(q.Pois, IsComplete(q), 0);
            return icon != null ? icon.WorldMapAreaId : 0;
        }

        /// <summary>WatchFrame_DisplayTrackedQuests / WorldMapFrame_UpdateQuests: "isComplete < 0" (failed) is not complete. Their "numObjectives == 0
        /// ... isComplete = true" is not applied (ours): online a view has no objectives until the quest template arrives, so only the log's
        /// complete state counts.</summary>
        public static bool IsComplete(QuestView q)
        {
            return q != null && !q.Failed && q.State == QuestState.Complete;
        }

        /// <summary>
        /// WatchFrame_ReverseQuestObjective / WorldMapFrame_ReverseQuestObjective (WatchFrame.lua:1270-1277, WorldMapFrame.lua:1761-1768):
        /// 'string.find(text, "(.*):%s(.*)")' then 'arg2.." "..arg1' on GetQuestLogLeaderBoard's "%s: %d/%d" (QUEST_ITEMS_NEEDED /
        /// QUEST_OBJECTS_FOUND, GlobalStrings.lua:5891 / :5900) or "%s slain: %d/%d" (QUEST_MONSTERS_KILLED :5897) - "0/1 The Deed to Brill".
        /// </summary>
        public static string ObjectiveLine(QuestObjectiveView o)
        {
            return Mathf.Min(o.Have, o.Need) + "/" + o.Need + " " + (o.Text ?? "");
        }

        public static QuestView Find(PlayerState p, int id)
        {
            if (p == null) return null;
            for (int i = 0; i < p.QuestLog.Count; i++) if (p.QuestLog[i].Id == id) return p.QuestLog[i];
            return null;
        }

        /// <summary>
        /// QuestMapUpdateAllQuests for a zone (engine): the log's quests with a POI in that WorldMapArea. Ours: watched quests first in watch
        /// order, then the others in log order - so the tracker's numbers (its own count of watched quests on the map, WatchFrame.lua:902-916)
        /// equal the map's. Numbers follow WorldMapFrame_DisplayQuestPOI ("index - numCompletedQuests"). Empty while Show Quest Objectives is off.
        /// </summary>
        public static void Build(PlayerState p, int zone, List<QuestMapEntry> into, List<QuestMapEntry> pool)
        {
            into.Clear();
            if (p == null || zone <= 0 || !ShowObjectives) return;
            Order.Clear();
            var watched = QuestWatch.List;
            for (int w = 0; w < watched.Count; w++) { QuestView q = Find(p, watched[w]); if (q != null) Order.Add(q); }
            for (int i = 0; i < p.QuestLog.Count; i++) if (!QuestWatch.IsWatched(p.QuestLog[i].Id)) Order.Add(p.QuestLog[i]);
            int numeric = 0;
            for (int i = 0; i < Order.Count; i++)
            {
                QuestView q = Order[i];
                if (q.Pois == null || q.Pois.Count == 0) continue;
                bool any = false;
                for (int k = 0; k < q.Pois.Count && !any; k++) if (OnZone(q.Pois[k], zone)) any = true;
                if (!any) continue;
                while (pool.Count <= into.Count) pool.Add(new QuestMapEntry());
                QuestMapEntry e = pool[into.Count];
                e.QuestId = q.Id;
                e.Quest = q;
                e.Complete = IsComplete(q);
                e.Number = e.Complete ? 0 : ++numeric;
                e.Blob.Clear();
                QuestPoiView icon = IconPoi(q.Pois, e.Complete, zone);
                e.HasIcon = icon != null && Centre(icon, out e.IconX, out e.IconY);
                if (!e.Complete)
                {
                    // ours (DrawQuestBlob's choice is engine code): the zone's areas of the objectives (ObjectiveIndex >= 0), else all of them
                    for (int k = 0; k < q.Pois.Count; k++) if (OnZone(q.Pois[k], zone) && q.Pois[k].ObjectiveIndex >= 0 && q.Pois[k].X.Length >= 3) e.Blob.Add(q.Pois[k]);
                    if (e.Blob.Count == 0) for (int k = 0; k < q.Pois.Count; k++) if (OnZone(q.Pois[k], zone) && q.Pois[k].X.Length >= 3) e.Blob.Add(q.Pois[k]);
                }
                into.Add(e);
            }
            if (!_logged && into.Count > 0)
            {
                _logged = true;
                WowLog.Info("UI quest map: " + into.Count + " quest(s) with POIs in zone " + zone + " (ours: watched first, then the log; icon = the area's point average; "
                            + "a complete quest's icon on its ObjectiveIndex -1 area, read as the hand-in).");
            }
        }

        /// <summary>The player's zone list, built at most once a frame (the tracker asks every frame).</summary>
        public static List<QuestMapEntry> PlayerZoneQuests(PlayerState p)
        {
            if (_playerListFrame == Time.frameCount) return PlayerList;
            _playerListFrame = Time.frameCount;
            Build(p, PlayerZone, PlayerList, PlayerPool);
            return PlayerList;
        }

        /// <summary>
        /// QuestPOIGetIconInfo (engine) - ours: a complete quest uses its ObjectiveIndex -1 area (read as the hand-in; AzerothCore only passes
        /// the int32 through), an unfinished one its first area with ObjectiveIndex >= 0; else the first area. zone 0 = any zone.
        /// </summary>
        public static QuestPoiView IconPoi(List<QuestPoiView> pois, bool complete, int zone)
        {
            QuestPoiView first = null, want = null;
            for (int k = 0; k < pois.Count; k++)
            {
                QuestPoiView a = pois[k];
                if (zone > 0 && !OnZone(a, zone)) continue;
                if (a.X.Length == 0) continue;
                if (first == null) first = a;
                if (want == null && (complete ? a.ObjectiveIndex == -1 : a.ObjectiveIndex >= 0)) want = a;
            }
            return want ?? first;
        }

        /// <summary>Ours: the average of an area's points.</summary>
        /// <summary>
        /// Lead fix 2026-10-12 (the user: "can't see quests on the quest map view inside cities"): a POI area belongs on a zone's map when
        /// the server tagged it with that WorldMapArea, or when it lies inside the zone's bounds on the same map (its points are world
        /// coordinates: x between LocBottom and LocTop, y between LocRight and LocLeft, WorldMapArea.dbc fields 4-7) - a Stormwind quest the
        /// realm tagged with Elwynn (30) shows on the Stormwind map (301). Ours; WoW's map draws the tagged ones only.
        /// </summary>
        public static bool OnZone(QuestPoiView a, int zone)
        {
            if (a == null) return false;
            if (a.WorldMapAreaId == zone) return true;
            if (a.X.Length == 0) return false;
            WowZoneInfo z = Zone(WowPlay.Files, zone);
            if (z == null || z.MapId != a.MapId) return false;
            float x, y;
            if (!Centre(a, out x, out y)) return false;
            return x >= Math.Min(z.Top, z.Bottom) && x <= Math.Max(z.Top, z.Bottom) && y >= Math.Min(z.Left, z.Right) && y <= Math.Max(z.Left, z.Right);
        }

        public static bool Centre(QuestPoiView a, out float x, out float y)
        {
            x = y = 0f;
            if (a == null || a.X.Length == 0) return false;
            double sx = 0, sy = 0;
            for (int i = 0; i < a.X.Length; i++) { sx += a.X[i]; sy += a.Y[i]; }
            x = (float)(sx / a.X.Length);
            y = (float)(sy / a.X.Length);
            return true;
        }

        // ------------------------------------------------------------------ zones

        public static WowZoneInfo Zone(WowFiles files, int id)
        {
            WowZoneInfo z;
            if (Zones.TryGetValue(id, out z)) return z;
            z = null;
            DbcTable wma = files != null && id > 0 ? Dbc.Load(files, "WorldMapArea") : null;
            int r = wma != null ? wma.FindRow(id) : -1;
            if (r >= 0 && wma.FieldCount >= 8)
            {
                z = new WowZoneInfo
                {
                    Id = id, MapId = wma.GetInt(r, 1), AreaId = wma.GetInt(r, 2), Dir = wma.GetString(r, 3),
                    Left = wma.GetFloat(r, 4), Right = wma.GetFloat(r, 5), Top = wma.GetFloat(r, 6), Bottom = wma.GetFloat(r, 7)
                };
                z.Name = WowAreaLookup.AreaName(files, z.AreaId) ?? z.Dir;
            }
            if (files != null) Zones[id] = z;
            return z;
        }

        /// <summary>
        /// The zone's overlay tiles (WorldMapOverlay.dbc, probe QuestMapData: 17 fields, [1] WorldMapArea ID, [8] texture name, [9] width,
        /// [10] height, [11] offset x, [12] offset y), cut into 256-pixel textures exactly as WorldMapFrame_Update (WorldMapFrame.lua:303-352):
        /// "numTexturesWide = ceil(textureWidth/256)", the last column / row "mod(textureWidth, 256)" wide in a power-of-two file (from 16),
        /// "SetPoint("TOPLEFT", offsetX + (256 * (k-1)), -(offsetY + (256 * (j - 1))))", "SetTexture(textureName..(((j - 1) * numTexturesWide) + k))".
        /// GetNumMapOverlays returns the explored ones only (engine): ours - every overlay (the zone as if explored).
        /// </summary>
        public static List<WowZoneOverlay> ZoneOverlays(WowFiles files, WowZoneInfo z)
        {
            List<WowZoneOverlay> l;
            if (z == null) return null;
            if (Overlays.TryGetValue(z.Id, out l)) return l;
            l = new List<WowZoneOverlay>();
            DbcTable ov = files != null ? Dbc.Load(files, "WorldMapOverlay") : null;
            if (ov != null && ov.FieldCount >= 13)
            {
                for (int r = 0; r < ov.RecordCount; r++)
                {
                    if (ov.GetInt(r, 1) != z.Id) continue;
                    string name = ov.GetString(r, 8);
                    int tw = ov.GetInt(r, 9), th = ov.GetInt(r, 10), ox = ov.GetInt(r, 11), oy = ov.GetInt(r, 12);
                    if (string.IsNullOrEmpty(name) || tw <= 0 || th <= 0) continue;
                    int wide = (tw + 255) / 256, tall = (th + 255) / 256;
                    for (int j = 1; j <= tall; j++)
                    {
                        int ph = j < tall ? 256 : (th % 256 == 0 ? 256 : th % 256), fh = j < tall ? 256 : Pow2From16(ph);
                        for (int k = 1; k <= wide; k++)
                        {
                            int pw = k < wide ? 256 : (tw % 256 == 0 ? 256 : tw % 256), fw = k < wide ? 256 : Pow2From16(pw);
                            l.Add(new WowZoneOverlay
                            {
                                Path = @"Interface\WorldMap\" + z.Dir + @"\" + name + ((j - 1) * wide + k) + ".blp",
                                X = ox + 256 * (k - 1), Y = oy + 256 * (j - 1), W = pw, H = ph, U = pw / (float)fw, V = ph / (float)fh
                            });
                        }
                    }
                }
            }
            Overlays[z.Id] = l;
            return l;
        }

        private static int Pow2From16(int px)
        {
            int f = 16;
            while (f < px) f *= 2;
            return f;
        }

        /// <summary>The zone holding an AreaTable area (its top parent) on a map: a WorldMapArea ID, 0 = none (thread-safe reads of cached DBCs).</summary>
        public static int ZoneOfArea(WowFiles files, int map, int areaId)
        {
            DbcTable area = Dbc.Load(files, "AreaTable");
            DbcTable wma = Dbc.Load(files, "WorldMapArea");
            if (area == null || wma == null || areaId <= 0) return 0;
            int zone = areaId;
            for (int guard = 0; guard < 8; guard++)
            {
                int r = area.FindRow(zone);
                int parent = r >= 0 ? area.GetInt(r, DbcFields.AreaTable.ParentAreaID) : 0;
                if (parent <= 0) break;
                zone = parent;
            }
            for (int r = 0; r < wma.RecordCount; r++)
                if (wma.GetInt(r, 2) == zone && wma.GetInt(r, 1) == map) return wma.GetInt(r, 0);
            return 0;
        }
    }

    /// <summary>
    /// QuestPOITemplate / QuestPOICompletedTemplate (QuestPOI.xml) and QuestPOI_DisplayButton / _SelectButton / _DeselectButton (QuestPOI.lua):
    /// a 32-unit button whose NormalTexture is a cell of UI-QuestPoi-NumberIcons ("0.875, 1, 0.875, 1" deselected, "0.500, 0.625, 0.375, 0.5"
    /// selected), the number cell ("xOffset = mod(buttonIndex, QUEST_POI_ICONS_PER_ROW) * QUEST_POI_ICON_SIZE", y 0.5 + row * 0.125 yellow,
    /// 0 + row * 0.125 black when selected), the 24-unit turn-in (UI-WorldMap-QuestIcon 0..0.5, CENTER -1, 0) and the 50-unit selection glow
    /// (UI-QuestPoi-IconGlow, ADD). Drawn at any size (all parts scale with it).
    /// </summary>
    public sealed class QuestPoiIcon
    {
        public const int Numeric = 1, CompleteIn = 2, CompleteOut = 3, CompleteSwap = 4;   // QUEST_POI_NUMERIC .. QUEST_POI_COMPLETE_SWAP (QuestPOI.lua:9-12)
        public const string NumberIconsTex = @"Interface\WorldMap\UI-QuestPoi-NumberIcons.blp";
        public const string QuestIconTex = @"Interface\WorldMap\UI-WorldMap-QuestIcon.blp";
        public const string GlowTex = @"Interface\WorldMap\UI-QuestPoi-IconGlow.blp";
        private const float IconsPerRow = 8f, IconSize = 0.125f;                          // QUEST_POI_ICONS_PER_ROW, QUEST_POI_ICON_SIZE (:4-5)

        public RectTransform Root;
        private RawImage _glow, _back, _number, _turnin;
        private int _type = -1, _index = -1;
        private bool _selected;

        /// <summary>A POI icon of size units. centred: anchored at the parent's centre (map markers); else top-left at (x, y) (UiKit coordinates).</summary>
        public static QuestPoiIcon Make(Transform parent, string name, float size, bool centred, float x = 0f, float y = 0f)
        {
            var i = new QuestPoiIcon();
            i.Root = UiKit.Rect(parent, name, x, y, size, size);
            if (centred) { i.Root.anchorMin = i.Root.anchorMax = i.Root.pivot = new Vector2(0.5f, 0.5f); i.Root.anchoredPosition = Vector2.zero; }
            float s = size / 32f;
            i._glow = UiKit.Pic(i.Root, "Glow", UiArt.Add(GlowTex), (size - 50f * s) * 0.5f, (size - 50f * s) * 0.5f, 50f * s, 50f * s);
            i._back = UiKit.Pic(i.Root, "Normal", UiArt.Tex(NumberIconsTex), 0f, 0f, size, size);
            i._number = UiKit.Pic(i.Root, "Number", UiArt.Tex(NumberIconsTex), 0f, 0f, size, size);
            i._turnin = UiKit.Pic(i.Root, "Turnin", UiArt.Tex(QuestIconTex), 0f, 0.5f, 0f, 0.5f, (size - 24f * s) * 0.5f - 1f * s, (size - 24f * s) * 0.5f, 24f * s, 24f * s);
            i._glow.enabled = false;
            i._turnin.enabled = false;
            return i;
        }

        public void SetShown(bool on) { if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on); }

        /// <summary>UiKit.Pic makes a picture without a texture (files not open yet) transparent: white again once it has one.</summary>
        private static void Visible(RawImage img)
        {
            if (img == null) return;
            if (img.texture == null)
            {
                Texture2D t = img.name == "Glow" ? UiArt.Add(GlowTex) : img.name == "Turnin" ? UiArt.Tex(QuestIconTex) : UiArt.Tex(NumberIconsTex);
                if (t != null) img.texture = t;
            }
            if (img.texture != null && img.color.a < 0.01f) img.color = Color.white;
        }

        public void Set(int type, int index, bool selected)
        {
            if (type == _type && index == _index && selected == _selected) return;
            _type = type; _index = index; _selected = selected;
            bool glow = false, number = false, turnin = false;
            Texture2D back = UiArt.Tex(NumberIconsTex);
            switch (type)
            {
                case Numeric:
                    if (selected) UiKit.Coords(_back, 0.5f, 0.625f, 0.375f, 0.5f); else UiKit.Coords(_back, 0.875f, 1f, 0.875f, 1f);
                    int i = Mathf.Max(0, index - 1);
                    float xo = (i % (int)IconsPerRow) * IconSize, yo = (selected ? 0f : 0.5f) + Mathf.Floor(i / IconsPerRow) * IconSize;   // QuestPOI_SetTextColor
                    UiKit.Coords(_number, xo, xo + IconSize, yo, yo + IconSize);
                    number = true; glow = selected;
                    break;
                case CompleteIn:
                    if (selected) UiKit.Coords(_back, 0.5f, 0.625f, 0.375f, 0.5f); else UiKit.Coords(_back, 0.875f, 1f, 0.875f, 1f);
                    turnin = true; glow = selected;
                    break;
                case CompleteOut:
                    UiKit.Coords(_back, 0.5f, 0.625f, 0.875f, 1f);                  // "has no selected mode"
                    turnin = true;
                    break;
                default:                                                            // CompleteSwap: QuestPOICompletedTemplate, swapped on selection
                    if (selected) { UiKit.Coords(_back, 0.5f, 0.625f, 0.375f, 0.5f); turnin = true; glow = true; }
                    else { back = UiArt.Tex(QuestIconTex); UiKit.Coords(_back, 0f, 0.5f, 0f, 0.5f); }
                    break;
            }
            if (_back.texture != back) _back.texture = back;
            Visible(_back); Visible(_number); Visible(_turnin); Visible(_glow);
            _glow.enabled = glow;
            _number.enabled = number;
            _turnin.enabled = turnin;
        }
    }
}
