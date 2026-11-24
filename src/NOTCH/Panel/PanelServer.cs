using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.SteamworksSocket;
using NOTCH.Admin;
using NOTCH.Net;
using NuclearOption.DedicatedServer;
using NuclearOption.ModdingApi;
using NuclearOption.Networking;
using NuclearOption.Networking.Authentication;
using Steamworks;

namespace NOTCH.Panel
{
    /// <summary>Server side of the menu: builds what it shows and carries out its requests. Every request is re-checked for Admin level.</summary>
    internal static class PanelServer
    {
        /// <summary>/notch-menu: send the current state to that admin; their game opens the menu.</summary>
        internal static void Open(PlayerHandle admin)
        {
            INetworkPlayer owner = admin.GetInternalPlayer()?.Owner;
            if (owner == null) return;
            owner.Send(BuildState("", true, open: true));
        }

        internal static void OnAction(INetworkPlayer sender, NotchPanelActionV1 msg)
        {
            try
            {
                if (!IsAdmin(sender, out string who))
                {
                    Plugin.Log.LogWarning($"[menu] ignored a menu request from {who}: not an admin.");
                    return;
                }
                PanelAction[] asked = msg.Actions ?? new PanelAction[0];
                bool wantNotes = asked.Any(a => a.Kind == PanelLogic.ActNotes);
                asking = who;
                if (wantNotes && asked.All(a => a.Kind == PanelLogic.ActNotes)) { sender.Send(BuildNote()); return; }   // nothing else was asked: no new menu state needed
                var done = new List<string>();
                bool ok = true;
                foreach (PanelAction a in msg.Actions ?? new PanelAction[0])
                {
                    if (a.Kind == PanelLogic.ActRefresh || a.Kind == PanelLogic.ActNotes) continue;
                    string result;
                    bool good;
                    try { good = Do(a, out result); }
                    catch (Exception e) { good = false; result = e.Message; }
                    ok &= good;
                    done.Add(result);
                    Plugin.Log.LogInfo($"[menu] {who}: {result}");
                }
                string status = done.Count == 0 ? "" : done.Count == 1 ? done[0] : (ok ? $"Saved {done.Count} changes." : "Some changes failed: " + string.Join(" ", done.Where(d => d.StartsWith("Could not") || d.StartsWith("Not "))));
                sender.Send(BuildState(status, ok, open: false));
                if (wantNotes) sender.Send(BuildNote());            // after the changes, so the table shows the result
            }
            catch (Exception e) { Plugin.Log.LogWarning("[menu] request failed: " + e.Message); }
        }

        /// <summary>The menu's second message: leftover files, the catalogue's state and the config sync table.</summary>
        private static string asking = "";          // the admin whose request is being handled: notices are hidden per admin

        private static NotchPanelNoteV1 BuildNote()
        {
            NOTCH.Catalogue.CatalogueStatus status = NOTCH.Catalogue.CatalogueRunner.Status;
            bool inUse = status != null && status.Version.Length > 0;
            return new NotchPanelNoteV1
            {
                Protocol = Wire.ProtocolVersion,
                Leftovers = Plugin.Leftovers.ToArray(),
                Catalogue = NOTCH.Catalogue.CatalogueRunner.Report().Split('\n')[0],
                Unmatched = inUse ? status.Unmatched.ToArray() : new string[0],
                SizeLimitMB = (ushort)Plugin.CatalogueSizeLimitMB.Value,
                RevertCount = (byte)Plugin.RevertCount.Value, RevertSeconds = (byte)Plugin.RevertSeconds.Value,
                Events = NOTCH.Sync.RevertServer.Events,
                QuietStops = NOTCH.Sync.RevertServer.Seen.StopsQuiet(asking, NOTCH.Sync.RevertServer.Log),
                QuietLeftovers = NOTCH.Sync.RevertServer.Seen.LeftoversQuiet(asking, Plugin.Leftovers),
                CatalogueBytes = inUse ? (uint)Math.Min(status.Bytes, uint.MaxValue) : 0,
                CataloguePublished = inUse && status.PublishedUtc.Year > 1970 ? (uint)(status.PublishedUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds : 0,
                Sync = NOTCH.Sync.ConfigSend.Rows().OrderBy(r => r.Mod.Name, StringComparer.OrdinalIgnoreCase).Select(r => new PanelSync
                {
                    Guid = r.Mod.Guid, Name = r.Mod.Name, Why = r.Verdict.Why, Settings = (ushort)Math.Min(r.Settings, ushort.MaxValue),
                    Choice = (byte)r.Choice, Mode = (byte)r.Verdict.Mode, Locked = r.Verdict.Locked,
                    InCatalogue = !inUse || NOTCH.Catalogue.CatalogueRunner.Hits.ContainsKey(r.Mod.Guid),
                }).ToArray(),
            };
        }

        internal static bool IsAdmin(INetworkPlayer sender, out string who)
        {
            who = "the host";
            if (sender.IsHost) return true;
            who = sender.ConnectionHandle?.ToString() ?? "unknown";
            if (!(sender.ConnectionHandle is SteamConnection steam)) return false;
            who = steam.SteamID.m_SteamID.ToString();
            return PlayerHandle.TryGet(steam.SteamID.m_SteamID, out PlayerHandle h) && h.State == PlayerHandleState.InMatch
                   && (h.IsHost || (byte)h.PermissionLevel >= (byte)PermissionLevel.Admin);
        }

        // ---- carrying out one change ------------------------------------------------------------------------

        private static bool Do(PanelAction a, out string result)
        {
            string key = a.Key ?? "", value = (a.Value ?? "").Trim();
            switch (a.Kind)
            {
                case PanelLogic.ActRequired:
                {
                    ModEntry mod = Known(key);
                    if (mod == null) { result = $"Could not find the mod '{key}'."; return false; }
                    RuleStore.SetRequired(mod.Guid, value == "on" ? true : value == "off" ? false : (bool?)null);
                    result = $"{mod.Name}: required = {value} → {(mod.Required ? "required" : "not required")}."; return true;
                }
                case PanelLogic.ActRule:
                {
                    ModEntry mod = Known(key);
                    if (mod == null) { result = $"Could not find the mod '{key}'."; return false; }
                    RuleStore.SetRule(mod.Guid, value == "kick" ? MismatchRule.Kick : value == "warn" ? MismatchRule.Warn : value == "off" ? MismatchRule.Off : (MismatchRule?)null);
                    result = $"{mod.Name}: mismatch rule = {value}."; return true;
                }
                case PanelLogic.ActModBan:
                {
                    bool ban = value == "1";
                    string name = Known(key)?.Name ?? (Commands.Seen.TryGetValue(RuleOverrides.Key(key), out string seenName) ? seenName
                                  : RuleStore.Banned.TryGetValue(RuleOverrides.Key(key), out string bannedName) ? bannedName : "");
                    RuleStore.SetBanned(key, name, ban);
                    Commands.RefreshBanCommand();
                    result = $"{(name.Length > 0 ? name : key)}: {(ban ? "banned" : "no longer banned")}."; return true;
                }
                case PanelLogic.ActPolicy:
                    if (!Enum.TryParse(value, out JoinPolicy policy) || !Enum.IsDefined(typeof(JoinPolicy), policy)) { result = $"Could not set the policy to '{value}'."; return false; }
                    Plugin.Policy.Value = policy;
                    result = $"Policy = {policy}."; return true;
                case PanelLogic.ActUnlisted:
                    Plugin.UnlistedClientMods.Value = value == "Ignore" ? UnlistedMode.Ignore : UnlistedMode.Report;
                    result = $"Unlisted client mods = {Plugin.UnlistedClientMods.Value}."; return true;
                case PanelLogic.ActTimeout:
                    if (!int.TryParse(value, out int seconds) || seconds < 1 || seconds > 6) { result = $"Could not set the wait to '{value}' (1–6 seconds)."; return false; }
                    Plugin.ReplyTimeoutSeconds.Value = seconds;
                    result = $"Wait for a player's mod list = {seconds} s."; return true;

                case PanelLogic.ActSync:
                {
                    NOTCH.Sync.ConfigSend.Row row = NOTCH.Sync.ConfigSend.Rows().FirstOrDefault(r => string.Equals(r.Mod.Guid, key, StringComparison.OrdinalIgnoreCase));
                    if (row.Mod == null) { result = $"Could not find a mod with settings called '{key}'."; return false; }
                    if (row.Verdict.Locked) { result = $"Could not change {row.Mod.Name}: its settings are never sent ({row.Verdict.Why})."; return false; }
                    if (!NOTCH.Sync.SyncChoices.TryWord(value, out NOTCH.Sync.SyncChoice choice)) { result = $"Could not set config sync to '{value}'."; return false; }
                    RuleStore.SetSync(row.Mod.Guid, choice);
                    result = $"{row.Mod.Name}: config sync = {(choice == NOTCH.Sync.SyncChoice.Auto ? "default" : value)}."; return true;
                }
                case PanelLogic.ActSizeLimit:
                {
                    if (!int.TryParse(value, out int mb) || mb < 1 || mb > 1024) { result = $"Could not set the catalogue size limit to '{value}' (1 to 1024 MB)."; return false; }
                    Plugin.CatalogueSizeLimitMB.Value = mb;
                    result = $"Catalogue size limit = {mb} MB."; return true;
                }
                case PanelLogic.ActStopRemove:
                    return NOTCH.Sync.RevertServer.Remove(value, out result);
                case PanelLogic.ActNoticeQuiet:
                    return NOTCH.Sync.RevertServer.Quiet(asking, key, out result);
                case PanelLogic.ActRevertCount:
                {
                    if (!int.TryParse(value, out int n) || n < 1 || n > 20) { result = $"Could not set the put-back count to '{value}' (1 to 20)."; return false; }
                    Plugin.RevertCount.Value = n;
                    result = $"Stop after {n} put-back(s)."; return true;
                }
                case PanelLogic.ActRevertSeconds:
                {
                    if (!int.TryParse(value, out int n) || n < 5 || n > 60) { result = $"Could not set the put-back window to '{value}' (5 to 60 seconds)."; return false; }
                    Plugin.RevertSeconds.Value = n;
                    result = $"Put-backs are counted over {n} s."; return true;
                }

                case PanelLogic.ActAdminAdd:
                case PanelLogic.ActAdminRemove:
                {
                    if (!AdminManager.Active) { result = "Could not change the admin list: admin management is off."; return false; }
                    if (!AdminList.TryParseSteamId(key, out ulong id)) { result = $"Not a player's Steam ID: '{key}'. Nothing was changed."; return false; }
                    result = a.Kind == PanelLogic.ActAdminAdd ? AdminManager.Add(id, value.Length > 0 ? value : null) : AdminManager.Remove(id);
                    return true;
                }

                case PanelLogic.ActPlayerBan:
                case PanelLogic.ActPlayerUnban:
                case PanelLogic.ActPlayerKick:
                {
                    if (!AdminList.TryParseSteamId(key, out ulong id)) { result = $"Not a player's Steam ID: '{key}'. Nothing was changed."; return false; }
                    var steamId = new CSteamID(id);
                    NetworkAuthenticatorNuclearOption auth = NetworkManagerNuclearOption.i.Authenticator;
                    if (a.Kind == PanelLogic.ActPlayerKick)
                    {
                        result = Kick(steamId) ? $"{id} was kicked." : $"Could not kick {id}: not connected."; return true;
                    }
                    string banFile = BanFile();
                    if (banFile == null) { result = "Could not change the ban list: this server has no ban file (BanListPaths in DedicatedServerConfig.json is empty)."; return false; }
                    if (a.Kind == PanelLogic.ActPlayerBan)
                    {
                        if (auth.BanList.Contains(steamId)) { result = $"{id} is already banned."; return true; }
                        string reason = value.Replace("\r", "").Replace("\n", " ");
                        auth.BanPlayer(steamId, reason, banFile);       // the game's own ban: list + file (+ the owner of a family-shared copy)
                        bool wasOnline = Kick(steamId);
                        result = $"{id} is banned{(wasOnline ? " and was kicked" : "")}."; return true;
                    }
                    if (!auth.BanList.Contains(steamId)) { result = $"{id} is not on the ban list."; return true; }
                    auth.BanList.Remove(steamId);
                    AllowBanList.RemoveId(banFile, steamId, allowMissing: true);
                    result = $"{id} is no longer banned."; return true;
                }
            }
            result = "Could not carry out an unknown request."; return false;
        }

        private static ModEntry Known(string guid) =>
            Plugin.KnownSet.FirstOrDefault(e => string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase));

        /// <summary>The file the game writes bans to, or null when a dedicated server has none configured.</summary>
        private static string BanFile()
        {
            if (!DedicatedServerManager.IsRunning) return NetworkAuthenticatorNuclearOption.UserBanListPath;
            string[] paths = DedicatedServerManager.Instance?.Config?.BanListPaths;
            return paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]) ? paths[0] : null;
        }

        /// <summary>The same steps the game's own "kick-player" remote command takes.</summary>
        private static bool Kick(CSteamID steamId)
        {
            foreach (INetworkPlayer p in NetworkManagerNuclearOption.i.Server.AuthenticatedPlayers)
            {
                if (p.IsHost || p.GetAuthData() == null || p.GetAuthData().SteamID != steamId) continue;
                if (p.TryGetPlayer<NuclearOption.Networking.Player>(out var player)) NetworkManagerNuclearOption.i.KickPlayerAsync(player).Forget();
                else NetworkManagerNuclearOption.i.KickPlayer(p);
                return true;
            }
            return false;
        }

        // ---- what the menu shows ------------------------------------------------------------------------------

        internal static NotchPanelStateV1 BuildState(string status, bool statusOk, bool open)
        {
            RuleStore.Reload();
            AdminManager.Reload();

            var mods = new List<PanelMod>();
            foreach (ModEntry e in Plugin.KnownSet.Where(e => e.Origin != ModOrigin.NotLoaded))
            {
                string key = RuleOverrides.Key(e.Guid);
                bool auto = RequiredRules.DefaultFor(e, out string why);
                mods.Add(new PanelMod
                {
                    Guid = e.Guid, Name = e.Name, Version = e.Version ?? "", Kind = (byte)e.Kind,
                    Where = e.Origin == ModOrigin.Loaded ? PanelLogic.OnServer : e.Origin == ModOrigin.NoLoadFolder ? PanelLogic.NoLoadFolder : PanelLogic.SkippedClientOnly,
                    ScopeTag = e.ScopeTag ?? "", CompatibleFrom = e.CompatibleFrom ?? "",
                    Required = (byte)(RuleStore.Required.TryGetValue(key, out bool req) ? (req ? 1 : 2) : 0),
                    RequiredAuto = auto, RequiredAutoWhy = why,
                    Rule = (byte)(RuleStore.Rules.TryGetValue(key, out MismatchRule rule) ? (rule == MismatchRule.Kick ? 1 : rule == MismatchRule.Warn ? 2 : 3) : 0),
                    Banned = RuleStore.Banned.ContainsKey(key),
                });
            }
            var have = new HashSet<string>(mods.Select(m => RuleOverrides.Key(m.Guid)));
            foreach (KeyValuePair<string, string> s in Commands.Seen.Where(s => have.Add(s.Key)))
                mods.Add(new PanelMod { Guid = s.Key, Name = s.Value, Version = "", Where = PanelLogic.SeenOnPlayer, ScopeTag = "", CompatibleFrom = "", RequiredAutoWhy = "", Banned = RuleStore.Banned.ContainsKey(s.Key) });
            foreach (KeyValuePair<string, string> b in RuleStore.Banned.Where(b => have.Add(b.Key)))
                mods.Add(new PanelMod { Guid = b.Key, Name = b.Value.Length > 0 ? b.Value : b.Key, Version = "", Where = PanelLogic.BannedOnly, ScopeTag = "", CompatibleFrom = "", RequiredAutoWhy = "", Banned = true });

            var online = new List<PlayerHandle>();
            PlayerHandle.GetAll(online);
            var players = online.Where(h => h.State == PlayerHandleState.InMatch).Select(h => new PanelPerson
            {
                Id = h.SteamId, Note = NameOf(h), Online = true, Admin = h.IsHost || (byte)h.PermissionLevel >= (byte)PermissionLevel.Admin,
            }).OrderBy(p => p.Note).ToArray();
            var onlineIds = new HashSet<ulong>(players.Select(p => p.Id));

            PanelPerson[] bans = new PanelPerson[0];
            try
            {
                bans = NetworkManagerNuclearOption.i.Authenticator.BanList.GetItems()
                    .Select(b => new PanelPerson { Id = b.id.m_SteamID, Note = b.reason ?? "", Online = onlineIds.Contains(b.id.m_SteamID) }).OrderBy(b => b.Id).ToArray();
            }
            catch (Exception e) { Plugin.Dbg("[menu] could not read the game's ban list: " + e.Message); }

            return new NotchPanelStateV1
            {
                Protocol = Wire.ProtocolVersion, Open = open, ServerName = Plugin.ServerName(), Version = Plugin.Version,
                Policy = (byte)Plugin.Policy.Value, ReportUnlisted = Plugin.UnlistedClientMods.Value == UnlistedMode.Report,
                ReplyTimeout = (byte)Plugin.ReplyTimeoutSeconds.Value, AdminActive = AdminManager.Active, BanFileOk = BanFile() != null,
                Status = status ?? "", StatusOk = statusOk,
                Mods = mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                Admins = AdminManager.Entries().Select(a => new PanelPerson { Id = a.Key, Note = a.Value, Online = onlineIds.Contains(a.Key) }).ToArray(),
                Bans = bans, Players = players,
            };
        }

        private static string NameOf(PlayerHandle h)
        {
            try { return h.GetInternalPlayer()?.GetPlayerName()?.RawSteamName ?? h.SteamId.ToString(); }
            catch { return h.SteamId.ToString(); }
        }
    }
}
