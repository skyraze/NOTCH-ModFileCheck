using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking.Authentication;

namespace NOTCH.Net
{
    /// <summary>Client side: answers a NOTCH server's request with this machine's mod list. Never sends unasked.</summary>
    internal static class ClientJoin
    {
        /// <summary>What the server we last joined said it knows (kept for the later "host allows" check).</summary>
        internal static WireMod[] ServerKnownSet = new WireMod[0];

        // The game registers its own login-time handlers in OnClientConnected; we add ours at the same moment.
        [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), nameof(NetworkAuthenticatorNuclearOption.OnClientConnected))]
        internal static class OnClientConnectedPatch
        {
            private static void Prefix(NetworkAuthenticatorNuclearOption __instance)
            {
                ServerKnownSet = new WireMod[0];
                Popup.Clear();
                __instance.client.MessageHandler.RegisterHandler<NotchInterrogateV1>(OnInterrogate, allowUnauthenticated: true);
                __instance.client.MessageHandler.RegisterHandler<NotchVerdictV1>(OnVerdict, allowUnauthenticated: true);
                NOTCH.Panel.PanelWindow.Close();
                __instance.client.MessageHandler.RegisterHandler<NotchPanelStateV1>(NOTCH.Panel.PanelWindow.OnState, allowUnauthenticated: false);
                Plugin.Dbg("connecting: ready to answer a NOTCH server");
            }
        }

        private static void OnInterrogate(INetworkPlayer server, NotchInterrogateV1 ask)
        {
            ServerKnownSet = ask.KnownSet ?? new WireMod[0];
            // Only what is actually present and active here: loaded plugins, patchers and .nobp bundles.
            WireMod[] mine = Plugin.KnownSet.Where(e => e.Origin == ModOrigin.Loaded).Select(Wire.ToWire).ToArray();
            server.Send(new NotchTransponderV1 { Protocol = Wire.ProtocolVersion, ClientMods = mine });
            Plugin.Log.LogInfo($"This server runs NOTCH (protocol {ask.Protocol}, policy {(JoinPolicy)ask.Policy}, knows {ServerKnownSet.Length} mod(s)). Sent it our {mine.Length} mod(s).");
        }

        /// <summary>The server's result. No problems = a log line only. Problems and we got in = the notice.</summary>
        private static void OnVerdict(INetworkPlayer server, NotchVerdictV1 verdict)
        {
            List<Problem> problems = (verdict.Problems ?? new WireProblem[0]).Select(Problem.FromWire).ToList();
            string name = string.IsNullOrEmpty(verdict.ServerName) ? "this server" : "'" + verdict.ServerName + "'";
            WriteReport(verdict, problems);
            if (problems.Count == 0) { Plugin.Log.LogInfo($"NOTCH check on {name}: all mods match."); return; }

            // One summary line here; the per-mod detail is in the report file (and in this log when Debug is on).
            Plugin.Log.LogInfo($"NOTCH check on {name}: {problems.Count} problem(s){(verdict.Refused ? " — the server is refusing the join" : "")} — see {Plugin.ReportPathShown}");
            foreach (Problem p in problems) Plugin.Dbg("  - " + p.Describe());
            // Refused: the game's own disconnect box shows the reason, so no notice of ours.
            if (!verdict.Refused) Popup.Show(verdict.ServerName, PlayerNote.Header(problems.Count), problems.Select(p => p.Describe()));
        }

        /// <summary>Adds this check to the top of notch-checks.yaml and drops the oldest beyond 10. Never fatal.</summary>
        private static void WriteReport(NotchVerdictV1 verdict, List<Problem> problems)
        {
            try
            {
                string path = Plugin.ReportPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string entry = CheckReport.Entry(DateTime.Now, verdict.ServerName, ((JoinPolicy)verdict.Policy).ToString(), verdict.Refused, problems,
                    guid => Plugin.KnownSet.FirstOrDefault(e => e.Origin == ModOrigin.Loaded && string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase))?.FilePath);
                string old = File.Exists(path) ? File.ReadAllText(path) : "";
                File.WriteAllText(path, CheckReport.Merge(old, entry));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not write {CheckReport.FileName}: {e.Message}"); }
        }
    }
}
