using System;
using SomniumSpace.Worlds.SomniumWoW.Data;
using SomniumSpace.Worlds.SomniumWoW.Play;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The hub between the running game (an IWowSession) and the Unity views. The front end starts a session when the
    /// player picks a character and has arrived in the world, and ends it when they go back to the room; the views
    /// (WowEntityViews, WowArmUi, WowGear) listen to these events and read Session every frame.
    /// </summary>
    public static class WowPlay
    {
        /// <summary>The running game, or null (in the room / sightseeing).</summary>
        public static IWowSession Session { get; private set; }

        /// <summary>The game files, for views that build models / icons.</summary>
        public static WowFiles Files { get; private set; }

        /// <summary>
        /// Added by the front end (iteration 2; iteration 3: all nine playable classes): the classes as the class screen read
        /// them from the files (localized name, CLASS_ICON_TCOORDS into WowClassInfo.IconAtlas, RAID_CLASS_COLORS, glue texts),
        /// for nameplates and frames ("Name - Level N Class" in the class colour). Set once the game files are read; null
        /// before, and when they could not be read. WowClassInfo.Find(WowPlay.Classes, cls) picks one.
        /// </summary>
        public static System.Collections.Generic.List<WowClassEntry> Classes { get; internal set; }

        /// <summary>
        /// Added by the front end (iteration 3): the ten races as the race screen read them (localized ChrRaces name, glue
        /// texts, RACE_ICON_TCOORDS into WowRaceInfo.RaceAtlas, classes, racial abilities, starting zone). Set once the game
        /// files are read; null before. WowRaceInfo.Find(WowPlay.Races, raceId) picks one.
        /// </summary>
        public static System.Collections.Generic.List<WowRaceEntry> Races { get; internal set; }

        /// <summary>Raised after Session is set.</summary>
        public static event Action<IWowSession> Started;

        /// <summary>Raised before Session is cleared.</summary>
        public static event Action Ended;

        /// <summary>
        /// Every game event, fanned out. The session's queue can only be drained once, so the front end drains it each
        /// frame (IWowSession.TryDequeueEvent) and calls Publish; views subscribe here and never drain the queue themselves.
        /// </summary>
        public static event Action<GameEvent> Event;

        /// <summary>Fans one event out to every Event listener (the front end calls this; a throwing listener is logged and skipped).</summary>
        public static void Publish(GameEvent e)
        {
            var h = Event;
            if (h == null) return;
            foreach (Action<GameEvent> d in h.GetInvocationList())
            {
                try { d(e); }
                catch (Exception ex) { WowLog.Error("Play: an Event listener threw on " + e.Type + ": " + ex); }
            }
        }

        public static void Start(IWowSession session, WowFiles files)
        {
            if (Session != null) End();
            Session = session;
            Files = files;
            WowLog.Info("Play: session started (" + (session == null ? "null" : session.GetType().Name) + ")");
            var h = Started;
            if (h != null)
            {
                foreach (Action<IWowSession> d in h.GetInvocationList())
                {
                    try { d(session); }
                    catch (Exception e) { WowLog.Error("Play: a Started listener threw: " + e); }
                }
            }
        }

        public static void End()
        {
            if (Session == null) return;
            var h = Ended;
            if (h != null)
            {
                foreach (Action d in h.GetInvocationList())
                {
                    try { d(); }
                    catch (Exception e) { WowLog.Error("Play: an Ended listener threw: " + e); }
                }
            }
            WowLog.Info("Play: session ended");
            Session = null;
        }
    }
}
