using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mirage;
using NOTCH.Net;
using NuclearOption.Networking;

namespace NOTCH.Sync
{
    /// <summary>Server side of the put-back guard: counts each player's reports, sends the stop, waits for the acknowledgement, keeps the list of events.</summary>
    internal static class RevertServer
    {
        private sealed class Sent
        {
            public string Name;
            public readonly Dictionary<string, string> Shown = new Dictionary<string, string>();   // setting key → "Mod / Section.Key", for settings sent to be applied
            public readonly GuardTracker Tracker = new GuardTracker();
            public readonly List<Timer> Timers = new List<Timer>();
            public bool Dropped;
        }

        private static readonly Dictionary<INetworkPlayer, Sent> players = new Dictionary<INetworkPlayer, Sent>();
        private static StopLog log;                 // the list of stops, loaded from its file when first needed
        private static NoticeSeen seen;             // per admin: notices they asked not to see again
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        private static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, Plugin.Name);

        internal static StopLog Log => log ?? (log = StopLog.Parse(ReadOrEmpty(StopLog.FileName)));
        internal static NoticeSeen Seen => seen ?? (seen = NoticeSeen.Parse(ReadOrEmpty(NoticeSeen.FileName)));

        /// <summary>The list for the menu, oldest first, each line as "number|text".</summary>
        internal static string[] Events => Log.Entries.Select(e => e.Key + "|" + e.Value).ToArray();

        private static string ReadOrEmpty(string file)
        {
            try { string path = Path.Combine(Folder, file); return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not read {file}: {e.Message}"); return ""; }
        }

        private static void Write(string file, string text)
        {
            try { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, file), text.Replace("\n", Environment.NewLine)); }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not write {file}: {e.Message}"); }
        }

        /// <summary>An admin removes one entry (by its number) or all of them. The server log keeps the original events.</summary>
        internal static bool Remove(string which, out string result)
        {
            if (string.Equals(which, "all", StringComparison.OrdinalIgnoreCase))
            {
                int n = Log.Clear();
                Write(StopLog.FileName, Log.Serialize());
                result = n == 0 ? "The list of stopped settings is already empty." : $"Removed all {n} entries from the list of stopped settings.";
                return true;
            }
            if (!uint.TryParse(which, out uint id) || !Log.Remove(id)) { result = $"Could not find entry '{which}' in the list of stopped settings."; return false; }
            Write(StopLog.FileName, Log.Serialize());
            result = $"Removed entry {id} from the list of stopped settings.";
            return true;
        }

        /// <summary>An admin asks not to see a notice again until something new of that kind happens.</summary>
        internal static bool Quiet(string admin, string which, out string result)
        {
            if (which == "stops") Seen.QuietStops(admin, Log);
            else if (which == "leftovers") Seen.QuietLeftovers(admin, Plugin.Leftovers);
            else { result = $"Could not hide the notice '{which}'."; return false; }
            Write(NoticeSeen.FileName, Seen.Serialize());
            result = which == "stops" ? "The stopped-settings notice is hidden until another setting is stopped." : "The leftover-file notice is hidden until the leftover files change.";
            return true;
        }

        /// <summary>How a setting is named in both directions: cleaned like all network text, so both sides compare the same thing.</summary>
        internal static string KeyOf(string guid, string section, string key) => SafeText.Clean(guid ?? "").ToLowerInvariant() + "\n" + SafeText.Clean(section ?? "") + "\n" + SafeText.Clean(key ?? "");

        /// <summary>Remembers which settings a player was sent to apply. Reports about anything else are ignored.</summary>
        internal static void Note(INetworkPlayer player, string name, IEnumerable<SyncMod> applied)
        {
            Forget(player);
            var sent = new Sent { Name = (name ?? "").Trim('\'') };
            foreach (SyncMod mod in applied)
            {
                string modName = Plugin.KnownSet.FirstOrDefault(e => string.Equals(e.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase))?.Name ?? mod.Guid;
                foreach (SyncSetting s in mod.Settings) sent.Shown[KeyOf(mod.Guid, s.Section, s.Key)] = $"{modName} / {s.Section}.{s.Key}";
            }
            if (sent.Shown.Count > 0) players[player] = sent;
        }

        /// <summary>The player left: their record and any waiting timers go.</summary>
        internal static void Forget(INetworkPlayer player)
        {
            if (player == null || !players.TryGetValue(player, out Sent sent)) return;
            foreach (Timer t in sent.Timers) t.Dispose();
            players.Remove(player);
        }

        internal static void OnRevert(INetworkPlayer player, NotchRevertV1 msg)
        {
            try
            {
                if (!players.TryGetValue(player, out Sent sent) || sent.Dropped) return;
                string key = KeyOf(msg.Guid, msg.Section, msg.Key);
                if (!sent.Shown.TryGetValue(key, out string shown) || msg.Kind > (byte)RevertKind.Ack) return;   // not a setting this player was sent: ignored
                GuardAction action = sent.Tracker.Report(key, (RevertKind)msg.Kind, clock.Elapsed.TotalSeconds, Plugin.RevertCount.Value, Plugin.RevertSeconds.Value, out string result);
                if (msg.Kind == (byte)RevertKind.PutBack) Plugin.Dbg($"[sync] {sent.Name}: put back {shown}");
                if (action == GuardAction.Settled) Record(player, sent, shown, result);
                if (action != GuardAction.SendStop) return;

                player.Send(new NotchRevertStopV1 { Protocol = Wire.ProtocolVersion, Guid = msg.Guid, Section = msg.Section, Key = msg.Key });
                Plugin.Dbg($"[sync] {sent.Name}: stop sent for {shown}");
                // One timer per stop, fired once; no polling. The result is handled back on the main loop.
                Timer timer = null;
                timer = new Timer(_ => PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, () => Overdue(player, sent, timer)), null, (int)(GuardTracker.AckWaitSeconds * 1000) + 250, Timeout.Infinite);
                sent.Timers.Add(timer);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[sync] a put-back report could not be handled: " + e.Message); }
        }

        private static void Overdue(INetworkPlayer player, Sent sent, Timer timer)
        {
            try
            {
                timer?.Dispose(); sent.Timers.Remove(timer);
                if (!players.TryGetValue(player, out Sent now) || now != sent || sent.Dropped) return;   // already gone
                List<string> late = sent.Tracker.Overdue(clock.Elapsed.TotalSeconds);
                if (late.Count == 0) return;
                foreach (string key in late) Record(player, sent, sent.Shown[key], GuardTracker.Dropped);
                sent.Dropped = true;
                if (player.TryGetPlayer<Player>(out Player inGame)) NetworkManagerNuclearOption.i.KickPlayerAsync(inGame).Forget();
                else NetworkManagerNuclearOption.i.KickPlayer(player);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[sync] could not drop a player whose NOTCH did not answer: " + e.Message); }
        }

        /// <summary>One line per event: time, player and Steam ID, mod and setting, result. To the log and the menu's list.</summary>
        private static void Record(INetworkPlayer player, Sent sent, string shown, string result)
        {
            string id = "unknown";
            try { id = player.GetAuthData()?.SteamID.m_SteamID.ToString() ?? "unknown"; } catch { }
            string line = $"{sent.Name} ({id})  {shown}  {result}";
            Plugin.Log.LogWarning("[sync] " + line);
            Log.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + line);
            Write(StopLog.FileName, Log.Serialize());
        }
    }
}
