// iteration 11 - owner motion (proposal-motion.md s6.2.10 / hook 16): the one rule w0game's WowMount hook calls. Pure (harness-tested).
namespace SomniumSpace.Worlds.SomniumWoW.Play.Motion
{
    public static class FormRide
    {
        /// <summary>SpellShapeshiftForm 27 Swift Flight Form / 29 Flight Form (Spell.dbc 40120 / 33943: aura 36 misc 27 / 29 + aura 201
        /// FLY, probe MotionSpells). Online CanFly = SMSG_MOVE_SET_CAN_FLY acked (OnlineSession.Mounts.cs OnCanFly); offline CanFly needs a
        /// flying mount in a flyable zone, so a form never flies offline.</summary>
        public const int SwiftFlightForm = 27, FlightForm = 29;

        /// <summary>Druid Flight Form / Swift Flight Form flying without a mount model (WowMount rides it).</summary>
        public static bool Flies(PlayerState p)
        {
            return p != null && !p.Mounted && p.CanFly && !p.OnTaxi && !p.Dead && (p.ShapeshiftForm == SwiftFlightForm || p.ShapeshiftForm == FlightForm);
        }
    }
}
