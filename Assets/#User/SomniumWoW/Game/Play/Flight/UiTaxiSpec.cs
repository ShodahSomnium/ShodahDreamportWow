// iteration 11 - owner motion (wave-0 stub by w0game, filled by motion)
// The Taxi (flight map) External spec for uicore's registry (proposal-uicore.md 5.4.8 Kind 42, H17): FlightMap keeps its own surfaces
// (WowFlight opens it while the session has a flight master's TaxiView, online and offline); the registry polls ExtIsOpen and closes it
// with ExtSetOpen(false) (Escape / WoW's panel rules). The map cannot be opened from the UI: only a flight master opens it, as in WoW.
using SomniumSpace.Worlds.SomniumWoW.Game.Flight;

namespace SomniumSpace.Worlds.SomniumWoW.Game.Ui
{
    public static class UiTaxiSpec
    {
        public static UiWindowSpec Spec()
        {
            return new UiWindowSpec
            {
                Kind = UiWindowKind.Taxi, Frame = "TaxiFrame", Area = UiPanelArea.Left, Pushable = 0, SessionWindow = true, Palm = false,
                ExtIsOpen = () => FlightMap.IsOpen,
                ExtSetOpen = open => { if (!open) FlightMap.Close(); },
                Owner = "motion", Hidden = false
            };
        }
    }
}
