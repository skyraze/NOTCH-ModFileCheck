using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using NuclearOption.ModdingApi;
using NuclearOption.Networking;

namespace NOTCH.Admin
{
    /// <summary>Whether NOTCH manages admins itself.</summary>
    public enum AdminMode { Auto, On, Off }

    /// <summary>
    /// NOTCH's basic admin management. The game has permission levels (Player, Moderator, Admin, Host) but no way
    /// to give one to a player; this does the minimum: players listed in admins.txt get the game's Admin level
    /// when they join. Rules:
    ///   - only ever RAISES a level, so it can't fight another mod over who is admin;
    ///   - never touches the Host;
    ///   - steps aside entirely when a known admin mod is loaded (AdminManagement = Auto) or when set to Off.
    /// Uses only the game's official ModdingApi (PlayerHandle).
    /// </summary>
    internal static class AdminManager
    {
        internal static bool Active { get; private set; }
        private static HashSet<ulong> admins = new HashSet<ulong>();
        private static DateTime fileStamp;
        /// <summary>Players NOTCH itself promoted this session (so later it only ever undoes its own grants).</summary>
        internal static readonly HashSet<ulong> Granted = new HashSet<ulong>();

        internal static string FilePath => Path.Combine(Path.Combine(Paths.ConfigPath, Plugin.Name), AdminList.FileName);
        internal const string FilePathShown = "BepInEx/config/NOTCH/" + AdminList.FileName;

        /// <summary>Called once from Plugin.Start, after BepInEx has loaded every plugin.</summary>
        internal static void Init(bool dedicatedServer)
        {
            AdminMode mode = Plugin.AdminManagement.Value;
            string other = KnownAdminMods.Guids.FirstOrDefault(g => Chainloader.PluginInfos.ContainsKey(g));
            if (mode == AdminMode.Off)
            {
                if (dedicatedServer) Plugin.Log.LogInfo("Admin management is off (AdminManagement = Off).");
                return;
            }
            if (mode == AdminMode.Auto && other != null)
            {
                Plugin.Log.LogInfo($"Admin management is off: {Chainloader.PluginInfos[other].Metadata.Name} is handling admins. Set AdminManagement = On to override.");
                return;
            }

            Active = true;
            try
            {
                if (dedicatedServer && !File.Exists(FilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, AdminList.Template.Replace("\n", Environment.NewLine));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not create {FilePathShown}: {e.Message}"); }

            Reload(force: true);
            PlayerHandle.PlayerJoined += OnJoined;
            PlayerHandle.PlayerLeft += h => Granted.Remove(h.SteamId);
            PlayerHandle.PlayersCleared += Granted.Clear;
            if (dedicatedServer)
                Plugin.Log.LogInfo($"Admin management is on: {admins.Count} admin(s) in {FilePathShown}.");
        }

        /// <summary>Re-reads admins.txt if it changed on disk (so an edit applies from the next join, no restart).</summary>
        internal static void Reload(bool force = false)
        {
            try
            {
                if (!File.Exists(FilePath)) { admins = new HashSet<ulong>(); fileStamp = default; return; }
                DateTime stamp = File.GetLastWriteTimeUtc(FilePath);
                if (!force && stamp == fileStamp) return;
                fileStamp = stamp;
                admins = AdminList.Parse(File.ReadAllText(FilePath), out List<string> bad);
                foreach (string line in bad)
                    Plugin.Log.LogWarning($"{FilePathShown}: ignored the line '{line}' — expected a player's Steam ID (SteamID64).");
                if (!force) Plugin.Log.LogInfo($"{FilePathShown} changed: now {admins.Count} admin(s).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not read {FilePathShown}: {e.Message}"); }
        }

        /// <summary>The admin list as it is in the file right now: Steam ID and note.</summary>
        internal static List<KeyValuePair<ulong, string>> Entries()
        {
            try { return File.Exists(FilePath) ? AdminList.Entries(File.ReadAllText(FilePath)) : new List<KeyValuePair<ulong, string>>(); }
            catch { return new List<KeyValuePair<ulong, string>>(); }
        }

        /// <summary>Adds a player to admins.txt and, if they are online, gives them the Admin level now. Returns what happened.</summary>
        internal static string Add(ulong id, string note = null)
        {
            string name = string.IsNullOrWhiteSpace(note) ? NameOf(id) : note.Trim();
            string text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : AdminList.Template;
            bool already = AdminList.Parse(text, out _).Contains(id);
            if (!already)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, AdminList.Add(text, id, name).Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            }
            Reload(force: true);
            string who = name.Length > 0 ? name : id.ToString();
            bool promoted = PlayerHandle.TryGet(id, out PlayerHandle player) && TryPromote(player);
            return already ? $"{who} is already on the admin list{(promoted ? "; given the Admin level now" : "")}."
                           : $"{who} added to the admin list{(promoted ? " and given the Admin level" : "; they get the Admin level when they next join")}.";
        }

        /// <summary>Removes a player from admins.txt. If they are online and NOTCH promoted them, they go back to Player now.</summary>
        internal static string Remove(ulong id)
        {
            string name = NameOf(id);
            string who = name.Length > 0 ? name : id.ToString();
            string text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
            if (!AdminList.Parse(text, out _).Contains(id)) return $"{who} is not on the admin list.";
            File.WriteAllText(FilePath, AdminList.Remove(text, id).Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            Reload(force: true);

            if (!PlayerHandle.TryGet(id, out PlayerHandle player) || player.State != PlayerHandleState.InMatch || player.IsHost)
                return $"{who} removed from the admin list.";
            // Only undo a level NOTCH itself gave, and only if it is still exactly that level.
            if (Granted.Contains(id) && player.PermissionLevel == PermissionLevel.Admin && player.SetPermissionLevel(PermissionLevel.Player))
            {
                Granted.Remove(id);
                return $"{who} removed from the admin list and set back to Player.";
            }
            return $"{who} removed from the admin list. Their current level ({player.PermissionLevel}) was not given by NOTCH, so it is left as it is.";
        }

        /// <summary>Raise-only: gives the Admin level to a player who is in the match, below Admin, and not the host.</summary>
        private static bool TryPromote(PlayerHandle player)
        {
            if (player.State != PlayerHandleState.InMatch || player.IsHost) return false;
            if ((byte)player.PermissionLevel >= (byte)PermissionLevel.Admin) return false;      // never lower, nothing to raise
            if (!player.SetPermissionLevel(PermissionLevel.Admin)) return false;
            Granted.Add(player.SteamId);
            return true;
        }

        /// <summary>An online player's Steam name, for the note in the file; "" when unknown.</summary>
        private static string NameOf(ulong id)
        {
            try { return PlayerHandle.TryGet(id, out PlayerHandle p) ? (p.GetInternalPlayer()?.GetPlayerName()?.RawSteamName ?? "") : ""; }
            catch { return ""; }
        }

        private static void OnJoined(PlayerHandle player)
        {
            try
            {
                // The event also fires on clients; only the server may set levels.
                if (NetworkManagerNuclearOption.i == null || NetworkManagerNuclearOption.i.Server == null || !NetworkManagerNuclearOption.i.Server.Active) return;
                if (player.State != PlayerHandleState.InMatch || player.IsHost) return;
                Reload();
                if (!admins.Contains(player.SteamId)) return;
                if (TryPromote(player)) Plugin.Log.LogInfo($"[admin] {player.SteamId} is in {AdminList.FileName}: given the Admin level.");
                else Plugin.Dbg($"[admin] {player.SteamId} is in {AdminList.FileName} but was not promoted (already Admin or above).");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[admin] join handling failed: " + e.Message); }
        }
    }
}
