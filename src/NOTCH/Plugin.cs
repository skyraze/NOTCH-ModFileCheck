// NOTCH — Nuclear Option Transponder Check Handshake
// Build step 2b.2: find every mod, build the "known set", report; a NOTCH server asks a joining client
// for its mod list, compares it with the known set and tells the client the result.
// What happens to the player depends on Server/Policy (see JoinPolicy).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NOTCH.Admin;
using NOTCH.Net;
using NuclearOption.ModdingApi;
using NuclearOption.Networking;
using UnityEngine;

namespace NOTCH
{
    /// <summary>What to do about a client mod the server does not know at all.</summary>
    public enum UnlistedMode { Report, Ignore }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.skyraze.notch";
        public const string Name = "NOTCH";
        public const string FullName = "Nuclear Option Transponder Check Handshake";
        public const string Version = "0.11.3";
        public const string BlueprinterGuid = "com.nikkorap.blueprinter";
        public const string NoLoadFolderName = "Client-Mods-NoLoad";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Debug;
        internal static ConfigEntry<JoinPolicy> Policy;
        internal static ConfigEntry<int> ReplyTimeoutSeconds;
        internal static ConfigEntry<UnlistedMode> UnlistedClientMods;
        internal static ConfigEntry<AdminMode> AdminManagement;
        private static bool joinExchangeOn;

        /// <summary>Debug-only log line.</summary>
        internal static void Dbg(string msg) { if (Debug != null && Debug.Value) Log.LogInfo("[debug] " + msg); }

        /// <summary>Every mod this machine knows about. Built once, after BepInEx has finished loading plugins.</summary>
        public static IReadOnlyList<ModEntry> KnownSet { get; private set; } = new List<ModEntry>();

        /// <summary>True on a dedicated server (its program is NuclearOptionServer, or the game runs without graphics).</summary>
        public static bool IsServerProcess =>
            Paths.ProcessName.StartsWith("NuclearOptionServer", System.StringComparison.OrdinalIgnoreCase) || Application.isBatchMode;

        /// <summary>The player's report file with the last 10 join checks.</summary>
        public static string ReportPath => Path.Combine(Path.Combine(Paths.BepInExRootPath, Name), CheckReport.FileName);
        /// <summary>The same path the way it is shown to the player.</summary>
        public const string ReportPathShown = "BepInEx/NOTCH/" + CheckReport.FileName;

        public static string NoLoadFolder => Path.Combine(Path.Combine(Paths.ConfigPath, Name), NoLoadFolderName);

        private void Awake()
        {
            Log = Logger;
            Debug = Config.Bind("General", "Debug", false,
                "Verbose logging for troubleshooting: every file scanned with full hashes, every step of a join exchange. Off by default.");
            Policy = Config.Bind("Server", "Policy", JoinPolicy.RequireNotch,
                "Server only. RequireNotch (default) = players must have NOTCH, must have every REQUIRED mod in a version the server accepts, " +
                "and must not have a banned mod, or they are refused; any other difference only shows them a notice. " +
                "WarnOnly = nobody is refused: players are shown a notice listing every difference, missing required mods included; players without NOTCH join unchecked. " +
                "RequireMatch = players without NOTCH, or with any difference at all, are refused with the reason. " +
                "Per-mod choices made with /notch-rule (kick, warn, off) win over the policy, except that WarnOnly never refuses. " +
                "Off = players are not asked and nothing is checked.");
            ReplyTimeoutSeconds = Config.Bind("Server", "ReplyTimeoutSeconds", 3,
                new ConfigDescription("Server only. How long to wait for a joining client's mod list before treating it as a client without NOTCH. " +
                                      "The game allows about 10 seconds for the whole login, so keep this small.",
                                      new AcceptableValueRange<int>(1, 6)));

            UnlistedClientMods = Config.Bind("Server", "UnlistedClientMods", UnlistedMode.Report,
                "Server only. Report = a client mod this server does not know at all counts as a problem. " +
                "Ignore = only mods the server knows are checked (version and file). " +
                "Mods in the Client-Mods-NoLoad folder always count as known, whatever this is set to.");

            AdminManagement = Config.Bind("Server", "AdminManagement", AdminMode.Auto,
                "Server only. NOTCH's basic admin management: players listed in BepInEx/config/NOTCH/admins.txt (one Steam ID per line) " +
                "get the game's Admin level when they join. It only ever raises a level. " +
                "Auto = on, unless a known admin mod is loaded. On = always on. Off = NOTCH leaves admins alone.");

            // Any game that runs NOTCH and hosts is a modded server: this lights the game's own "modded" marker
            // in the server browser. NOTCH only ever sets it to true.
            NetworkManagerNuclearOption.ModdedServer = true;

            // Debug only: one line whenever the game reports a player joining or leaving, with their permission level.
            PlayerHandle.PlayerJoined += h => Probe("joined", h);
            PlayerHandle.PlayerLeft += h => Probe("left", h);

            joinExchangeOn = Wire.Register(Logger);
            if (joinExchangeOn)
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(ServerJoin.OnStartServerPatch));
                harmony.PatchAll(typeof(ServerJoin.AuthenticatePatch));
                harmony.PatchAll(typeof(ServerJoin.SessionLoopGuard));
                harmony.PatchAll(typeof(ClientJoin.OnClientConnectedPatch));
            }
        }

        private static void Probe(string what, PlayerHandle h)
        {
            if (!Debug.Value) return;
            try
            {
                bool serverActive = NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server != null && NetworkManagerNuclearOption.i.Server.Active;
                string level = h.State == PlayerHandleState.InMatch ? $"{h.PermissionLevel} ({(byte)h.PermissionLevel})" : "n/a";
                Log.LogInfo($"[debug] [probe] player {what}: steamId={h.SteamId} state={h.State} level={level} host={(h.State == PlayerHandleState.InMatch ? h.IsHost.ToString() : "n/a")} serverActive={serverActive} players={PlayerHandle.PlayerCount}");
            }
            catch (System.Exception e) { Log.LogInfo("[debug] [probe] failed: " + e.Message); }
        }

        private static string AllowedOrBanned(ModEntry e) =>
            RuleStore.Banned.ContainsKey(RuleOverrides.Key(e.Guid)) ? "BANNED: players can't join with it (see /notch banned)." : "Counted as an allowed client mod.";

        /// <summary>Unity calls this to draw immediate-mode UI. Only the join notice lives here.</summary>
        private void OnGUI() { Popup.Draw(); NOTCH.Panel.PanelWindow.Draw(); }

        /// <summary>This server's name as players know it, or "" when it can't be told.</summary>
        internal static string ServerName()
        {
            try { return NetworkManagerNuclearOption.i?.DedicatedServerManager?.Config?.ServerName ?? ""; }
            catch { return ""; }
        }

        // Start (not Awake) so that BepInEx has finished loading every plugin and "loaded or not" is final.
        private void Start()
        {
            System.Action<string> dbg = null;
            if (Debug.Value) dbg = msg => Logger.LogInfo("[debug] " + msg);

            bool server = IsServerProcess;
            var all = new List<ModEntry>();
            all.AddRange(ModScanner.ScanPlugins(Paths.PluginPath, false, dbg));
            all.AddRange(ModScanner.ScanNobp(Paths.PluginPath, false, dbg));
            all.AddRange(ModScanner.ScanPatchers(dbg));

            var noLoad = new List<ModEntry>();
            if (server)
            {
                Directory.CreateDirectory(NoLoadFolder);            // so server owners can find it
                noLoad.AddRange(ModScanner.ScanPlugins(NoLoadFolder, true, dbg));
                noLoad.AddRange(ModScanner.ScanNobp(NoLoadFolder, true, dbg));
                foreach (ModEntry e in noLoad)
                {
                    // The same mod installed AND in the folder: the installed copy is the one that counts.
                    ModEntry installed = all.FirstOrDefault(x => x.Kind == e.Kind && x.Guid == e.Guid);
                    if (installed == null) all.Add(e);
                    else Logger.LogWarning($"{e.Display} is in {NoLoadFolderName} but is also installed ({installed.Origin}). Using the installed copy; remove one of them.");
                }
            }
            KnownSet = all;

            Logger.LogMessage($"{Name} ({FullName}) v{Version} — {(server ? "dedicated server" : "game client")}, " +
                              $"{all.Count} mod(s) known, {NoLoadFolderName}: {(server ? noLoad.Count + " mod(s)" : "n/a")}, " +
                              $"debug {(Debug.Value ? "on" : "off")} — {(joinExchangeOn ? (server ? "policy " + Policy.Value : "join checks on") : "join checks OFF (message id clash)")}");

            if (server && joinExchangeOn && (Policy.Value == JoinPolicy.RequireNotch || Policy.Value == JoinPolicy.RequireMatch))
                Logger.LogMessage($"Policy {Policy.Value}: players WITHOUT NOTCH are refused. To let them in, set Policy = WarnOnly in BepInEx/config/{Guid}.cfg.");

            AdminManager.Init(server);
            RuleStore.Reload(force: true);                  // also decides which mods are required
            Commands.Register(server);
            if (server)
            {
                var required = all.Where(x => x.Required).Select(x => $"{x.Display} ({x.RequiredWhy})").ToArray();
                if (required.Length > 0) Logger.LogInfo($"Required on every player's game: {string.Join(", ", required)}. Change with /notch-require.");
            }

            // Mods NOTCH found but this machine does not run.
            foreach (ModEntry e in all.Where(x => x.Origin == ModOrigin.SkippedByTag))
                Logger.LogInfo($"Detected {e.Display} — {(e.ClientOnlyTag ? "client-only" : "tagged for " + string.Join(", ", e.ProcessTags))}, not loaded on this {(server ? "server" : "program")}. {AllowedOrBanned(e)}");
            foreach (ModEntry e in all.Where(x => x.Origin == ModOrigin.NoLoadFolder))
                Logger.LogInfo($"Detected {e.Display} in {NoLoadFolderName} — not loaded on this server. {AllowedOrBanned(e)}");
            foreach (ModEntry e in all.Where(x => x.Origin == ModOrigin.NotLoaded))
                Logger.LogWarning($"{e.Display} is installed but BepInEx did not load it (missing dependency or an error — see the lines above in this log). It is not counted.");

            if (server)
            {
                // Plugins running on the server that their author did not mark as client-only.
                var untagged = all.Where(x => x.Kind == ModKind.Plugin && x.Origin == ModOrigin.Loaded && !x.ClientOnlyTag && x.Guid != Guid)
                                  .Select(x => x.Display).ToArray();
                if (untagged.Length > 0)
                    Logger.LogInfo($"Running on this server without a client-only tag: {string.Join(", ", untagged)}. " +
                                   $"If one of them is a client mod and misbehaves here, move it to BepInEx/config/{Name}/{NoLoadFolderName}.");
            }

            // Blueprinter: not required by NOTCH, but content mods need it.
            // Content packs rarely declare the dependency: Blueprinter finds the .nobp packed inside them, they never call it.
            bool needsBlueprinter = all.Any(x => x.Kind == ModKind.Nobp || x.BlueprinterContent || x.Dependencies.Contains(BlueprinterGuid));
            if (needsBlueprinter && !Chainloader.PluginInfos.ContainsKey(BlueprinterGuid))
            {
                var who = all.Where(x => x.Kind == ModKind.Nobp || x.BlueprinterContent || x.Dependencies.Contains(BlueprinterGuid)).Select(x => x.Display).ToArray();
                Logger.LogWarning($"Blueprinter is needed by {string.Join(", ", who)} but is not loaded. Install or enable Blueprinter ({BlueprinterGuid}).");
            }
        }
    }
}
