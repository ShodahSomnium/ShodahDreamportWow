using System;
using SomniumSpace.Worlds.SomniumWoW.Data;

namespace SomniumSpace.Worlds.SomniumWoW.World
{
    /// <summary>Map.dbc lookups and file loading helpers for the terrain code. Pure C#, thread-safe.</summary>
    public static class WowMapData
    {
        /// <summary>The map's directory name (Map.dbc Directory, e.g. "Azeroth"), or null when the id is unknown.</summary>
        public static string Directory(WowFiles files, int mapId)
        {
            var t = Dbc.Load(files, "Map");
            if (t == null) return null;
            int r = t.FindRow(mapId);
            if (r < 0) return null;
            string d = t.GetString(r, DbcFields.Map.Directory);
            return string.IsNullOrEmpty(d) ? null : d;
        }

        /// <summary>The map's WDT or null (logged) when missing/broken.</summary>
        public static WdtFile LoadWdt(WowFiles files, string mapDir)
        {
            var bytes = files.Read(WdtFile.PathOf(mapDir));
            if (bytes == null) { WowLog.Warn("Terrain: WDT missing: " + WdtFile.PathOf(mapDir)); return null; }
            try { return WdtFile.Parse(bytes); }
            catch (Exception e) { WowLog.Warn("Terrain: WDT unreadable " + mapDir + ": " + e.Message); return null; }
        }

        /// <summary>A tile or null (logged) when missing/broken.</summary>
        public static AdtFile LoadAdt(WowFiles files, string mapDir, int tileX, int tileY, bool bigAlpha, bool placementsOnly = false)
        {
            string path = WdtFile.AdtPath(mapDir, tileX, tileY);
            var bytes = files.Read(path);
            if (bytes == null) { WowLog.Warn("Terrain: ADT missing: " + path); return null; }
            try { return AdtFile.Parse(bytes, tileX, tileY, bigAlpha, placementsOnly); }
            catch (Exception e) { WowLog.Warn("Terrain: ADT unreadable " + path + ": " + e.Message); return null; }
        }

        /// <summary>The map's WDL or null when missing/broken (not logged as an error: some maps have none).</summary>
        public static WdlFile LoadWdl(WowFiles files, string mapDir)
        {
            var bytes = files.Read(WdtFile.WdlPath(mapDir));
            if (bytes == null) return null;
            try { return WdlFile.Parse(bytes); }
            catch (Exception e) { WowLog.Warn("Terrain: WDL unreadable " + mapDir + ": " + e.Message); return null; }
        }
    }
}
