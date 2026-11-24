using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Mirage;
using Mirage.Authentication;
using Mirage.SteamworksSocket;
using NuclearOption.Networking;
using NuclearOption.Networking.Authentication;
using Steamworks;

namespace NOTCH.Net
{
    /// <summary>Server side: asks a joining client for its mod list, compares it, then admits or refuses by Policy.</summary>
    internal static class ServerJoin
    {
        /// <summary>Players NOTCH is holding inside login right now. Game code is single-threaded, so plain fields are fine.</summary>
        internal static int Holding;
        private static readonly HashSet<INetworkPlayer> waiting = new HashSet<INetworkPlayer>();
        private static readonly Dictionary<INetworkPlayer, NotchTransponderV1> replies = new Dictionary<INetworkPlayer, NotchTransponderV1>();

        // 1) On server start: listen for answers. allowUnauthenticated, because the player is still logging in.
        [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), nameof(NetworkAuthenticatorNuclearOption.OnStartServer))]
        internal static class OnStartServerPatch
        {
            private static void Postfix(NetworkAuthenticatorNuclearOption __instance)
            {
                waiting.Clear(); replies.Clear(); Holding = 0;
                __instance.server.MessageHandler.RegisterHandler<NotchTransponderV1>(OnTransponder, allowUnauthenticated: true);
                // Requests from an admin's NOTCH menu. Only from players who have finished logging in; the sender is checked again in PanelServer.
                __instance.server.MessageHandler.RegisterHandler<NotchPanelActionV1>(NOTCH.Panel.PanelServer.OnAction, allowUnauthenticated: false);
                // An admin's answer to the Network Access Notice. Checked again in ConsentRemote.
                __instance.server.MessageHandler.RegisterHandler<NotchNoticeAnswerV1>(NOTCH.Consent.ConsentRemote.OnAnswer, allowUnauthenticated: false);
                // Put-back reports from a player's game. Checked in RevertServer against what that player was sent.
                __instance.server.MessageHandler.RegisterHandler<NotchRevertV1>(NOTCH.Sync.RevertServer.OnRevert, allowUnauthenticated: true);
                __instance.server.Disconnected.RemoveListener(NOTCH.Sync.RevertServer.Forget);
                __instance.server.Disconnected.AddListener(NOTCH.Sync.RevertServer.Forget);
                Plugin.Dbg("server started: listening for mod-list answers");
            }
        }

        private static void OnTransponder(INetworkPlayer player, NotchTransponderV1 msg)
        {
            if (waiting.Contains(player)) replies[player] = msg;        // only an answer we asked for counts
            else Plugin.Dbg($"ignored an unrequested mod list from {player?.ConnectionHandle}");
        }

        // 2) Wrap the game's login check (tested in spike 1).
        [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), nameof(NetworkAuthenticatorNuclearOption.AuthenticateAsync))]
        internal static class AuthenticatePatch
        {
            private static void Postfix(NetworkAuthenticatorNuclearOption __instance, INetworkPlayer player,
                NetworkAuthenticatorNuclearOption.AuthMessage message, CancellationToken cancellationToken,
                ref UniTask<AuthenticationResult> __result)
            {
                // Never touch the server's own connection or a hosting player.
                if (player == null || player.IsHost || message.JoinAs == PlayerType.DedicatedServer) return;
                if (Plugin.Policy.Value == JoinPolicy.Off) return;      // Off: no question, no wait

                // Guard 2: remember the game's Steam player entry so it can be put back if the player is refused.
                Action undo = () => { };
                if (player.ConnectionHandle is SteamConnection steam)
                {
                    CSteamID id = steam.SteamID;
                    __instance.steamPlayerLookup.TryGetValue(id, out INetworkPlayer previous);
                    undo = () =>
                    {
                        if (__instance.steamPlayerLookup.TryGetValue(id, out INetworkPlayer current) && ReferenceEquals(current, player))
                        {
                            if (previous != null) __instance.steamPlayerLookup[id] = previous;
                            else __instance.steamPlayerLookup.Remove(id);
                            Plugin.Dbg($"{id}: login did not complete — Steam player list restored");
                        }
                    };
                }
                __result = Exchange(__instance, __result, player, message.SteamName, cancellationToken, undo);
            }
        }

        // A UniTask may only be awaited once: we await the game's task here, the game awaits ours.
        private static async UniTask<AuthenticationResult> Exchange(NetworkAuthenticatorNuclearOption auth, UniTask<AuthenticationResult> gameCheck,
            INetworkPlayer player, string name, CancellationToken token, Action undo)
        {
            AuthenticationResult result = await gameCheck;      // the game's own verdict; errors pass through untouched
            if (!result.Success) return result;                 // the game refused them: nothing for NOTCH to do

            string who = $"'{name}'";
            JoinPolicy policy = Plugin.Policy.Value;
            bool checkUnlisted = Plugin.UnlistedClientMods.Value == UnlistedMode.Report;
            string refusal = null;                              // set = this player is turned away, with this text
            bool admitted = false;
            Holding++;
            waiting.Add(player);
            try
            {
                var ask = new NotchInterrogateV1
                {
                    Protocol = Wire.ProtocolVersion, Policy = (byte)policy, CheckUnlisted = checkUnlisted,
                    KnownSet = Plugin.KnownSet.Select(Wire.ToWire).ToArray(),
                };
                player.Send(ask);
                Plugin.Dbg($"[join] {who}: asked for mod list (sent {ask.KnownSet.Length} known mod(s))");

                long limitMs = Plugin.ReplyTimeoutSeconds.Value * 1000L;
                var clock = Stopwatch.StartNew();
                bool answered = false, sendSettings = false;
                NotchTransponderV1 reply = default;
                while (clock.ElapsedMilliseconds < limitMs)
                {
                    token.ThrowIfCancellationRequested();       // player left, or the game's own login timeout fired
                    if (replies.TryGetValue(player, out reply)) { answered = true; break; }
                    await UniTask.Yield();                      // wait one frame
                }

                if (answered)
                {
                    string list = string.Join(", ", (reply.ClientMods ?? new WireMod[0]).Select(m => string.IsNullOrEmpty(m.Version) ? m.Name : m.Name + " " + m.Version));
                    Plugin.Log.LogInfo($"[join] {who}: answered in {clock.ElapsedMilliseconds} ms with {reply.ClientMods?.Length ?? 0} mod(s): {list}");
                    if (Plugin.Debug.Value)
                        foreach (WireMod m in reply.ClientMods ?? new WireMod[0])
                            Plugin.Dbg($"[join] {who}: {m.Name} {m.Version} ({m.Guid}) kind={m.Kind} sha256={Hex.FromBytes(m.Sha256)}");

                    RuleStore.Reload();                         // pick up hand edits of the yaml files
                    List<Problem> problems = ModCompare.Run(Plugin.KnownSet, reply.ClientMods, checkUnlisted, RuleStore.Rules, RuleStore.Banned.Keys);
                    Commands.NoteClientMods(reply.ClientMods);  // so /notch-ban can offer the mods players actually bring
                    // Which problems keep the player out is decided in one place: ModCompare.Blocks.
                    List<Problem> blocking = problems.Where(p => ModCompare.Blocks(p, policy)).ToList();
                    bool refuse = blocking.Count > 0;
                    if (problems.Count == 0)
                        Plugin.Log.LogInfo($"[join] {who}: all {reply.ClientMods?.Length ?? 0} mod(s) match this server");
                    else
                    {
                        Plugin.Log.LogInfo($"[join] {who}: {problems.Count} problem(s){(blocking.Count > 0 && blocking.Count < problems.Count ? $", {blocking.Count} of them refuse the join" : "")} — {(refuse ? "REFUSED" : "joins, shown a notice")} (policy {policy}):");
                        foreach (Problem p in problems)
                        {
                            Plugin.Log.LogInfo($"[join] {who}:   - {(refuse && blocking.Contains(p) && blocking.Count < problems.Count ? "[refused for] " : "")}{p.Describe()}");
                            if (p.Kind == ProblemKind.HashMismatch) Plugin.Dbg($"[join] {who}:     server sha256={p.ServerHash} client sha256={p.ClientHash}");
                        }
                        string[] unlisted = problems.Where(p => p.Kind == ProblemKind.Unlisted).Select(p => p.Name).ToArray();
                        if (unlisted.Length > 0)
                            Plugin.Log.LogInfo($"[join] To allow {string.Join(", ", unlisted)}: install it on this server, or put its file in BepInEx/config/{Plugin.Name}/{Plugin.NoLoadFolderName}.");
                        if (refuse) refusal = PlayerNote.Refusal(blocking);
                    }

                    // Tell the client what was found. Its own NOTCH shows the notice (or, later, writes the report file).
                    player.Send(new NotchVerdictV1
                    {
                        Protocol = Wire.ProtocolVersion, Policy = (byte)policy, Refused = refuse,
                        ServerName = Plugin.ServerName(), Problems = problems.Select(p => p.ToWire()).ToArray(),
                    });
                    sendSettings = !refuse;
                }
                else if (policy == JoinPolicy.WarnOnly)
                {
                    Plugin.Log.LogInfo($"[join] {who}: no answer within {Plugin.ReplyTimeoutSeconds.Value} s — that client is not running NOTCH. Joins unchecked (policy WarnOnly).");
                }
                else
                {
                    Plugin.Log.LogInfo($"[join] {who}: no answer within {Plugin.ReplyTimeoutSeconds.Value} s — that client is not running NOTCH. REFUSED (policy {policy}).");
                    refusal = PlayerNote.NoNotchRefusal;
                }

                if (refusal != null)
                {
                    // Same route the game uses for "Password incorrect": send the reason, then fail the login.
                    if (!token.IsCancellationRequested) await auth.SendFailReason(player, refusal);
                    return AuthenticationResult.CreateFail("NOTCH: refused by policy " + policy, auth);   // 'finally' below undoes the Steam list entry
                }
                // Config sync: the server's settings follow the verdict, so they are in place before the player loads the map.
                if (sendSettings) NOTCH.Sync.ConfigSend.SendTo(player, who, policy, reply.ClientMods);
                admitted = true;
                return result;                                  // the game's own "yes"
            }
            finally
            {
                Holding--;
                waiting.Remove(player);
                replies.Remove(player);
                if (!admitted) undo();                          // left or timed out while held
            }
        }

        // Guard 1: skip the game's once-a-second Steam check while a player is held (no login data yet).
        [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), nameof(NetworkAuthenticatorNuclearOption.CheckSteamSessionDisconnects))]
        internal static class SessionLoopGuard
        {
            private static bool Prefix() => Holding <= 0;
        }
    }
}
