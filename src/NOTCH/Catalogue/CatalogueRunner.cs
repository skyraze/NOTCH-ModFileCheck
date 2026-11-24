using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NOTCH.Consent;
using NuclearOption.Networking;
using NuclearOption.Networking.Authentication;
using NuclearOption.SavedMission;

namespace NOTCH.Catalogue
{
    /// <summary>How the last catalogue check ended. For the log now; the menu and the status command later.</summary>
    internal sealed class CatalogueStatus
    {
        public DateTime WhenUtc;
        public CatalogueVerdict Verdict;
        public string Detail = "";
        public string Version = "";                 // the catalogue version in use, "" when none
        public bool Unchanged;
        public int Matched;
        public long Bytes;                          // size of the catalogue file in use; kept in memory only
        public DateTime PublishedUtc;               // NOMNOM's Last-Modified for that file; MinValue = not given
        public List<string> Unmatched = new List<string>();

        public string Line()
        {
            string s = "NOMNOM catalogue: " + CatalogueCheck.Reason(Verdict) + (Detail.Length > 0 ? " (" + Detail + ")" : "");
            if (Version.Length == 0) return s + ". No catalogue is in use.";
            s += Verdict == CatalogueVerdict.Accepted ? (Unchanged ? ", unchanged" : "") + $", version {Version}" : $". Still using version {Version}";
            s += $"; {Matched} installed mod(s) matched";
            return s + (Unmatched.Count > 0 ? "; not in the catalogue: " + string.Join(", ", Unmatched) + "." : ".");
        }
    }

    /// <summary>Decides when to check NOMNOM's catalogue and runs the check on a background worker. Server or hosting game only.</summary>
    internal static class CatalogueRunner
    {
        private const int TimeLimitMs = 10000;      // for a whole check, both files together
        private const int VersionMaxBytes = 256;

        private static DateTime lastCheckUtc = DateTime.MinValue;
        private static int running;
        private static Timer latest;
        private static string versionInUse = "";
        private static bool blockShown;

        internal static CatalogueStatus Status { get; private set; }
        /// <summary>What the catalogue says about this machine's mods, by plugin ID. Empty until a check is accepted.</summary>
        internal static IReadOnlyDictionary<string, CatalogueHit> Hits { get; private set; } = new Dictionary<string, CatalogueHit>();

        private static string MemoryPath => Path.Combine(Path.Combine(Paths.BepInExRootPath, Plugin.Name), "catalogue-memory.txt");

        // The server has started (a dedicated server, or a player's game beginning to host).
        [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), nameof(NetworkAuthenticatorNuclearOption.OnStartServer))]
        internal static class ServerStartPatch
        {
            private static void Postfix() => Trigger(missionLoading: false, serverStarting: true);
        }

        // A mission is being loaded: the quiet moment a check waits for.
        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.SetMission))]
        internal static class MissionLoadPatch
        {
            private static void Postfix(Mission mission)
            {
                if (mission != null && mission != Mission.NullMission) Trigger(missionLoading: true, serverStarting: false);
            }
        }

        /// <summary>The notice was just answered on this machine: check now if that is allowed.</summary>
        internal static void AnswerChanged() => Trigger(missionLoading: false, serverStarting: false, why: "the notice was answered");

        /// <summary>Runs on the game's main loop and returns at once: it only decides, the worker does the waiting.</summary>
        private static void Trigger(bool missionLoading, bool serverStarting, string why = null)
        {
            try
            {
                if (!HostingNow(serverStarting)) return;
                ConsentStore.Refresh();
                if (Plugin.IsServerProcess && ConsentStore.Answer == ConsentAnswer.NotAnswered && !blockShown)
                {
                    blockShown = true;
                    ConsentStore.PrintBlock(server: true);
                }
                if (!ConsentStore.MayConnect || Plugin.Policy.Value == JoinPolicy.Off) return;

                DateTime now = DateTime.UtcNow;
                if (!CatalogueSchedule.Due(now, lastCheckUtc, missionLoading))
                {
                    Plugin.Dbg($"{why ?? (serverStarting ? "server start" : missionLoading ? "mission load" : "two hours passed")}: next catalogue check not due ({(int)(now - lastCheckUtc).TotalMinutes} min since the last)");
                    return;
                }
                if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return;     // a check is already running
                lastCheckUtc = now;                                                   // a failed check counts too: no rapid retries
                ArmLatest();

                var job = new Job
                {
                    When = now,
                    Mods = Plugin.KnownSet.Where(e => e.Origin == ModOrigin.Loaded).ToList(),
                    Ceiling = CatalogueCheck.ClampCeiling(Plugin.CatalogueSizeLimitMB.Value * CatalogueCheck.MB),
                    VersionInUse = versionInUse,
                    Before = Status,
                };
                Plugin.Dbg($"catalogue check started ({why ?? (serverStarting ? "server start" : missionLoading ? "mission load" : "two hours passed")})");
                new Thread(() => Work(job)) { IsBackground = true, Priority = System.Threading.ThreadPriority.Lowest, Name = "NOTCH catalogue" }.Start();
            }
            catch (Exception e) { Plugin.Log.LogWarning("NOMNOM catalogue check could not start: " + e.Message); running = 0; }
        }

        /// <summary>True on a dedicated server, or on a game hosting for other players. Single player and the main menu's background scene do not count.</summary>
        private static bool HostingNow(bool serverStarting)
        {
            if (Plugin.IsServerProcess) return true;
            var manager = NetworkManagerNuclearOption.i;
            if (manager == null || manager.Server == null || !manager.Server.Listening) return false;      // not open to other players
            return serverStarting || manager.Server.Active;
        }

        /// <summary>One timer for "no later than two hours": it fires once, with no polling in between.</summary>
        private static void ArmLatest()
        {
            latest?.Dispose();
            latest = new Timer(_ => PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, () => Trigger(false, false)),
                               null, CatalogueSchedule.Latest + TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }

        private sealed class Job
        {
            public DateTime When;
            public List<ModEntry> Mods;
            public long Ceiling;
            public string VersionInUse;
            public CatalogueStatus Before;
        }

        /// <summary>The background worker. Touches no game object; hands one finished result back to the main loop.</summary>
        private static void Work(Job job)
        {
            var status = new CatalogueStatus { WhenUtc = job.When, Version = job.VersionInUse };
            if (job.Before != null) { status.Matched = job.Before.Matched; status.Unmatched = job.Before.Unmatched; status.Bytes = job.Before.Bytes; status.PublishedUtc = job.Before.PublishedUtc; }
            Dictionary<string, CatalogueHit> hits = null;
            var clock = Stopwatch.StartNew();
            try
            {
                byte[] versionBytes = Fetch(CatalogueGate.VersionUrl, VersionMaxBytes, clock, out status.Verdict, out _);
                string version = versionBytes == null ? "" : CatalogueCheck.CleanVersion(Encoding.UTF8.GetString(versionBytes));
                if (versionBytes != null && version.Length == 0) { status.Verdict = CatalogueVerdict.NotCatalogue; status.Detail = "its version file is not a version"; }
                else if (versionBytes != null && job.VersionInUse.Length > 0 && version == job.VersionInUse) status.Unchanged = true;
                else if (versionBytes != null)
                {
                    byte[] file = Fetch(CatalogueGate.ManifestUrl, job.Ceiling, clock, out status.Verdict, out DateTime published);
                    if (file != null)
                    {
                        CatalogueRead read;
                        using (var text = new StreamReader(new MemoryStream(file), Encoding.UTF8)) read = CatalogueReader.Read(text);
                        if (!read.Ok) { status.Verdict = CatalogueVerdict.NotCatalogue; status.Detail = read.Why; }
                        else
                        {
                            status.Verdict = CatalogueCheck.Judge(file.Length, read.Entries.Select(e => e.Id).ToList(), LoadMemory(), out CatalogueMemory next);
                            SaveMemory(next);
                            if (status.Verdict == CatalogueVerdict.Accepted)
                            {
                                CatalogueMatched matched = CatalogueMatch.Run(job.Mods, read.Entries);
                                hits = matched.ByGuid;
                                status.Version = version; status.Matched = hits.Count; status.Unmatched = matched.Unmatched;
                                status.Bytes = file.Length; status.PublishedUtc = published;
                            }
                        }
                    }
                }
            }
            catch (Exception e) { status.Verdict = CatalogueVerdict.CouldNotConnect; status.Detail = e.GetType().Name; }
            PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, () => Publish(status, hits));
        }

        /// <summary>Back on the main loop: one quick step that swaps in the result.</summary>
        private static void Publish(CatalogueStatus status, Dictionary<string, CatalogueHit> hits)
        {
            running = 0;
            Status = status;
            if (hits != null) { Hits = hits; versionInUse = status.Version; }
            if (status.Verdict == CatalogueVerdict.Accepted) Plugin.Log.LogInfo(status.Line()); else Plugin.Log.LogWarning(status.Line());
        }

        /// <summary>Downloads one of the two NOMNOM files. Null = it was not kept; the verdict says why.</summary>
        private static byte[] Fetch(string url, long ceiling, Stopwatch clock, out CatalogueVerdict verdict, out DateTime modified)
        {
            modified = DateTime.MinValue;
            CatalogueGate.Require(url);                                 // the only-NOMNOM rule, checked at the point of connecting
            verdict = CatalogueVerdict.CouldNotConnect;
            int left = TimeLimitMs - (int)clock.ElapsedMilliseconds;
            if (left <= 0) { verdict = CatalogueVerdict.TookTooLong; return null; }
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.AllowAutoRedirect = false;                      // a redirect would lead to another address
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.Timeout = left;
                request.ReadWriteTimeout = left;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) return null;
                    if (response.ContentLength > ceiling) { verdict = CatalogueVerdict.TooLarge; return null; }
                    try { if (response.Headers["Last-Modified"] != null) modified = response.LastModified.ToUniversalTime(); } catch { }   // a header of the same answer; no extra request
                    using (Stream body = response.GetResponseStream())
                        return CatalogueCheck.ReadCapped(body, ceiling, () => clock.ElapsedMilliseconds > TimeLimitMs, out verdict);
                }
            }
            catch (WebException e)
            {
                verdict = e.Status == WebExceptionStatus.Timeout ? CatalogueVerdict.TookTooLong : CatalogueVerdict.CouldNotConnect;
                return null;
            }
            catch (IOException) { return null; }
        }

        /// <summary>The catalogue's state in plain lines, for /notch catalogue and its server-side twin.</summary>
        internal static string Report()
        {
            if (ConsentStore.Answer == ConsentAnswer.Declined) return "NOMNOM catalogue: not used. The Network Access Notice was declined on this server.";
            if (!ConsentStore.MayConnect) return "NOMNOM catalogue: not used. The Network Access Notice has not been answered on this server (" + ConsentStore.NoticeShown + ").";
            if (Plugin.Policy.Value == JoinPolicy.Off) return "NOMNOM catalogue: not used while Policy is Off.";
            if (Status == null) return running != 0 ? "NOMNOM catalogue: the first check is running." : "NOMNOM catalogue: no check has run yet since the server started.";

            var sb = new StringBuilder(Status.Line());
            int minutes = (int)Math.Max(0, (DateTime.UtcNow - Status.WhenUtc).TotalMinutes);
            sb.Append($"\nLast check: {minutes} minute(s) ago. Next: at a mission load once an hour has passed, or after two hours at the latest.");
            sb.Append($"\nSize limit: {Plugin.CatalogueSizeLimitMB.Value} MB.");
            foreach (KeyValuePair<string, CatalogueHit> hit in Hits.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                string name = Plugin.KnownSet.FirstOrDefault(e => string.Equals(e.Guid, hit.Key, StringComparison.OrdinalIgnoreCase))?.Name ?? hit.Key;
                string side = hit.Value.Side == CatalogueSide.None ? "no client/server value" : hit.Value.Side.ToString();
                sb.Append($"\n- {name}: {side} (catalogue entry {hit.Value.Id}, matched by {hit.Value.How})");
            }
            return sb.ToString();
        }

        private static CatalogueMemory LoadMemory()
        {
            try { return File.Exists(MemoryPath) ? CatalogueMemory.Parse(File.ReadAllText(MemoryPath)) : new CatalogueMemory(); }
            catch { return new CatalogueMemory(); }
        }

        private static void SaveMemory(CatalogueMemory memory)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MemoryPath));
                File.WriteAllText(MemoryPath, memory.Serialize().Replace("\n", Environment.NewLine));
            }
            catch { }       // not being able to remember only means the next download is judged as a first one
        }
    }
}
