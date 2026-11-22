using System;
using System.Threading;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Cysharp.Threading.Tasks;

namespace NOTCH.Consent
{
    /// <summary>Holds a game's console reminder back until Blueprinter has finished its start-up messages, so it is not buried.</summary>
    internal sealed class ConsentReminder : ILogListener
    {
        private const string Source = "Blueprinter", DoneLine = "[Plugin] Done in";
        private const int LongestWaitMs = 20000;
        private static ConsentReminder waiting;
        private static Timer longestWait;
        private static int fired;

        /// <summary>Called once, on the main loop, when the main menu has finished loading.</summary>
        internal static void ShowAfterStartup()
        {
            if (!Chainloader.PluginInfos.ContainsKey(Plugin.BlueprinterGuid)) { ConsentStore.PrintBlock(server: false); return; }
            waiting = new ConsentReminder();
            Logger.Listeners.Add(waiting);                                  // told of each log line as it is written; nothing is polled
            longestWait = new Timer(_ => Fire(), null, LongestWaitMs, Timeout.Infinite);   // in case that line never comes
        }

        public void LogEvent(object sender, LogEventArgs e)
        {
            if (e.Source.SourceName == Source && (e.Data as string ?? "").StartsWith(DoneLine, StringComparison.Ordinal)) Fire();
        }

        public void Dispose() { }

        /// <summary>Runs once. The printing is handed to the main loop: a log line must not be written from inside the log itself.</summary>
        private static void Fire()
        {
            if (Interlocked.Exchange(ref fired, 1) != 0) return;
            PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, () =>
            {
                Logger.Listeners.Remove(waiting);
                longestWait?.Dispose();
                waiting = null; longestWait = null;
                if (ConsentStore.Answer == ConsentAnswer.NotAnswered) ConsentStore.PrintBlock(server: false);
            });
        }
    }
}
