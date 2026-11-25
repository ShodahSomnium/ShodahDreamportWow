// iteration 11 - owner world (proposal-world.md 7): offline is our own per-zone simulation - no dungeon, raid or battleground map is an
// offline zone, so the 6 instance calls answer one Info line (ours) and PlayerState.MapInstanceType stays 0 (the instance sight rule and
// the SELF menu's instance rows never apply offline).
namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        const string InstOfflineText = "Dungeons and raids are on the online realm";   // ours

        void InstOffline(string what)
        {
            _events.Enqueue(new GameEvent { Type = GameEventType.Info, Text = InstOfflineText });
            WowLog.Info("Offline instances: " + what + " - " + InstOfflineText + " (ours)");
        }

        public void SetDungeonDifficulty(int difficulty) { InstOffline("dungeon difficulty " + difficulty); }
        public void SetRaidDifficulty(int difficulty) { InstOffline("raid difficulty " + difficulty); }
        public void ResetInstances() { InstOffline("reset instances"); }
        public void RequestRaidInfo() { WowLog.Info("Offline instances: raid info - no saved instances offline"); }
        public void AnswerInstanceLock(bool accept) { }
        public void SetSavedInstanceExtend(int mapId, int difficulty, bool extend) { InstOffline("raid lock extension"); }
    }
}
