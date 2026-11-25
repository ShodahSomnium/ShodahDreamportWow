using System;

namespace SomniumSpace.Worlds.SomniumWoW
{
    /// <summary>
    /// Logging for code that must also run outside Unity (the offline harness). Game code sets Sink to Debug.Log;
    /// the harness sets it to Console.WriteLine. Every line carries the [SomniumWoW] tag so Player.log greps work.
    /// </summary>
    public static class WowLog
    {
        public const string Tag = "[SomniumWoW] ";
        public static Action<string> Sink;
        public static Action<string> WarnSink;
        public static Action<string> ErrorSink;

        public static void Info(string message) { var s = Sink; if (s != null) s(Tag + message); }
        public static void Warn(string message) { var s = WarnSink ?? Sink; if (s != null) s(Tag + message); }
        public static void Error(string message) { var s = ErrorSink ?? WarnSink ?? Sink; if (s != null) s(Tag + message); }
    }
}
