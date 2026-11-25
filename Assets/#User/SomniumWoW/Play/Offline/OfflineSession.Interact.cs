// iteration 11 - owner interact (filled: the designer's compiled draft is the final offline behaviour - proposal-interact.md s7)
// w0play: IWowSession.Interactions offline = OfflineInteractions (one Info line per call; a new partial, no edit to OfflineSession.cs).
namespace SomniumSpace.Worlds.SomniumWoW.Play.Offline
{
    public sealed partial class OfflineSession
    {
        private OfflineInteractions _interactions;

        public IWowInteractions Interactions { get { return _interactions ?? (_interactions = new OfflineInteractions(EmitInfo)); } }
    }
}
