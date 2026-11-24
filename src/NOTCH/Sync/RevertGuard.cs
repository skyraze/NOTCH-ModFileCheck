using System.Collections.Generic;
using System.Linq;

namespace NOTCH.Sync
{
    /// <summary>What a player's game tells the server about a synced setting it had to put back.</summary>
    public enum RevertKind : byte { PutBack = 0, SafetyNet = 1, Ack = 2 }

    /// <summary>What the server does next for one report.</summary>
    public enum GuardAction { None, SendStop, Settled }

    /// <summary>Counts events inside a sliding window of seconds. Pure logic.</summary>
    public sealed class RevertWindow
    {
        private readonly Queue<double> times = new Queue<double>();

        /// <summary>Adds one event at 'now' and returns how many fall within the last 'seconds'.</summary>
        public int Add(double now, double seconds)
        {
            times.Enqueue(now);
            while (times.Count > 0 && now - times.Peek() > seconds) times.Dequeue();
            return times.Count;
        }
    }

    /// <summary>The server's record for one player: per setting, counting, waiting for the acknowledgement, or settled. Pure logic.</summary>
    public sealed class GuardTracker
    {
        public const int SafetyCount = 20;          // the player's own fixed limit: this many put-backs…
        public const double SafetySeconds = 10;     // …within this many seconds
        public const double AckWaitSeconds = 30;    // how long the server waits for the acknowledgement of a stop

        public const string ByServer = "Stopped by server; acknowledged.";
        public const string BySafetyNet = "Stopped by client safety net.";
        public const string Dropped = "No acknowledgement; player dropped.";

        private enum State { Counting, Waiting, Settled }
        private sealed class Item { public State State; public RevertWindow Window = new RevertWindow(); public double StopAt; }
        private readonly Dictionary<string, Item> items = new Dictionary<string, Item>();

        private Item Get(string key) { if (!items.TryGetValue(key, out Item i)) items[key] = i = new Item(); return i; }

        /// <summary>One report from the player's game. result is set when the setting is settled by it.</summary>
        public GuardAction Report(string key, RevertKind kind, double now, int count, double seconds, out string result)
        {
            result = null;
            Item item = Get(key);
            if (item.State == State.Settled) return GuardAction.None;              // the first event to settle a setting wins; later ones are ignored
            switch (kind)
            {
                case RevertKind.SafetyNet:
                    item.State = State.Settled; result = BySafetyNet; return GuardAction.Settled;   // proof the game stopped, whether or not a stop was on its way
                case RevertKind.Ack:
                    if (item.State != State.Waiting) return GuardAction.None;      // an acknowledgement nobody asked for
                    item.State = State.Settled; result = ByServer; return GuardAction.Settled;
                default:
                    if (item.State == State.Waiting) return GuardAction.None;      // put-backs still in flight after the stop was sent
                    if (item.Window.Add(now, seconds) < count) return GuardAction.None;
                    item.State = State.Waiting; item.StopAt = now; return GuardAction.SendStop;
            }
        }

        /// <summary>Settings whose stop has gone unanswered for the whole wait. They are settled as dropped.</summary>
        public List<string> Overdue(double now)
        {
            List<string> late = items.Where(i => i.Value.State == State.Waiting && now - i.Value.StopAt >= AckWaitSeconds).Select(i => i.Key).ToList();
            foreach (string key in late) items[key].State = State.Settled;
            return late;
        }
    }
}
