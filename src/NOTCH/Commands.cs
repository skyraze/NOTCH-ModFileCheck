using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using NOTCH.Admin;
using NOTCH.Net;
using NuclearOption.ModdingApi;

namespace NOTCH
{
    /// <summary>
    /// The three small files a server manager's per-mod choices are saved in, next to admins.txt:
    ///   overrides.yaml — what a mismatch costs (kick / warn / off);
    ///   required.yaml  — mods switched to required / not required;
    ///   banned.yaml    — mods nobody may join with.
    /// All are re-read when they change on disk, so a hand edit applies from the next join.
    /// </summary>
    internal static class RuleStore
    {
        internal static Dictionary<string, MismatchRule> Rules { get; private set; } = new Dictionary<string, MismatchRule>();
        internal static Dictionary<string, bool> Required { get; private set; } = new Dictionary<string, bool>();
        internal static Dictionary<string, string> Banned { get; private set; } = new Dictionary<string, string>();   // id → name
        private static DateTime rulesStamp, requiredStamp, bannedStamp;

        private static string Dir => Path.Combine(Paths.ConfigPath, Plugin.Name);
        internal static string RulesPath => Path.Combine(Dir, RuleOverrides.FileName);
        internal static string RequiredPath => Path.Combine(Dir, RequiredRules.FileName);
        internal static string BannedPath => Path.Combine(Dir, BannedList.FileName);
        internal const string RulesShown = "BepInEx/config/NOTCH/" + RuleOverrides.FileName;
        internal const string RequiredShown = "BepInEx/config/NOTCH/" + RequiredRules.FileName;
        internal const string BannedShown = "BepInEx/config/NOTCH/" + BannedList.FileName;

        internal static void Reload(bool force = false)
        {
            if (Changed(RulesPath, ref rulesStamp, force, out string rulesText))
            {
                Rules = RuleOverrides.Parse(rulesText, out List<string> bad);
                Warn(RulesShown, bad, " Use kick, warn or off.");
                if (!force) Plugin.Log.LogInfo($"{RulesShown} changed: now {Rules.Count} per-mod rule(s).");
            }
            if (Changed(RequiredPath, ref requiredStamp, force, out string requiredText))
            {
                Required = RequiredRules.Parse(requiredText, out List<string> bad);
                Warn(RequiredShown, bad, "");
                RequiredRules.Apply(Plugin.KnownSet, Required);
                if (!force) Plugin.Log.LogInfo($"{RequiredShown} changed: {Plugin.KnownSet.Count(e => e.Required)} mod(s) are now required.");
            }
            if (Changed(BannedPath, ref bannedStamp, force, out string bannedText))
            {
                Banned = BannedList.Parse(bannedText, out List<string> bad);
                Warn(BannedShown, bad, "");
                if (!force) { Plugin.Log.LogInfo($"{BannedShown} changed: now {Banned.Count} banned mod(s)."); Commands.RefreshBanCommand(); }
            }
        }

        private static void Warn(string file, List<string> badLines, string hint)
        {
            foreach (string line in badLines) Plugin.Log.LogWarning($"{file}: ignored the line '{line}'.{hint}");
        }

        /// <summary>True when the file should be (re)read; gives its text ("" when it does not exist).</summary>
        private static bool Changed(string path, ref DateTime stamp, bool force, out string text)
        {
            text = "";
            try
            {
                DateTime now = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
                if (!force && now == stamp) return false;
                stamp = now;
                if (now != default) text = File.ReadAllText(path);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not read {path}: {e.Message}"); return false; }
        }

        /// <summary>Sets (or, with null, clears) one mod's mismatch rule and writes overrides.yaml.</summary>
        internal static void SetRule(string guid, MismatchRule? rule)
        {
            Reload();
            string key = RuleOverrides.Key(guid);
            if (rule.HasValue) Rules[key] = rule.Value; else Rules.Remove(key);
            Write(RulesPath, RuleOverrides.Serialize(Rules, NameOf), ref rulesStamp);
        }

        /// <summary>Sets (or, with null, clears) the manager's choice for one mod and writes required.yaml.</summary>
        internal static void SetRequired(string guid, bool? required)
        {
            Reload();
            string key = RuleOverrides.Key(guid);
            if (required.HasValue) Required[key] = required.Value; else Required.Remove(key);
            Write(RequiredPath, RequiredRules.Serialize(Required, NameOf), ref requiredStamp);
            RequiredRules.Apply(Plugin.KnownSet, Required);
        }

        /// <summary>Bans or un-bans one mod and writes banned.yaml.</summary>
        internal static void SetBanned(string guid, string name, bool banned)
        {
            Reload();
            string key = RuleOverrides.Key(guid);
            if (banned) Banned[key] = name ?? ""; else Banned.Remove(key);
            Write(BannedPath, BannedList.Serialize(Banned), ref bannedStamp);
        }

        private static void Write(string path, string text, ref DateTime stamp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text.Replace("\n", Environment.NewLine));
            stamp = File.GetLastWriteTimeUtc(path);
        }

        internal static string NameOf(string key) =>
            Plugin.KnownSet.FirstOrDefault(e => RuleOverrides.Key(e.Guid) == key)?.Name ?? "";
    }

    /// <summary>
    /// NOTCH's admin commands. Each action is written once (the Do… methods) and offered two ways:
    ///
    /// 1. In the game's chat, through the official ModdingApi. All need the Admin level, so the game hides them
    ///    from everyone else. A chat command has ONE fixed list of arguments with fixed suggestions, so there are
    ///    several commands instead of "/notch rule ..." sub-commands — that way the chat box can suggest everything:
    ///        /notch [status|rules|required|banned|admins]
    ///        /notch-rule <mod> <kick|warn|off|reset>      what a version / file mismatch on that mod costs
    ///        /notch-require <mod> <on|off|reset>          whether players must have that mod
    ///        /notch-ban <mod> <ban|allow>                 nobody may join with that mod, any version
    ///        /notch-admin <add|remove> <player>
    ///        /notch-menu                                  opens a window with all of the above (NOTCH.Panel)
    ///
    /// 2. On a dedicated server, through the game's remote-command port (start the server with
    ///    -ServerRemoteCommands; only programs on the server machine can reach it):
    ///        notch-status, notch-rules, notch-required, notch-banned, notch-admins,
    ///        notch-rule <mod> <rule>, notch-require <mod> <on|off|reset>, notch-ban <mod or plugin id> <ban|allow>,
    ///        notch-admin-add <steam id>, notch-admin-remove <steam id>
    /// </summary>
    internal static class Commands
    {
        private static List<KeyValuePair<string, ModEntry>> modChoices = new List<KeyValuePair<string, ModEntry>>();
        /// <summary>Mods players have brought that this server does not know: id (lower case) → name. Since start-up.</summary>
        private static readonly Dictionary<string, string> seen = new Dictionary<string, string>();
        internal static IReadOnlyDictionary<string, string> Seen => seen;
        private static List<KeyValuePair<string, ModEntry>> banChoices = new List<KeyValuePair<string, ModEntry>>();

        /// <summary>Called once from Plugin.Start, after the known set is built.</summary>
        internal static void Register(bool dedicatedServer)
        {
            modChoices = ModChoices.Build(Plugin.KnownSet);
            try
            {
                string[] mods = modChoices.Select(c => c.Key).ToArray();
                ChatCommands.RegisterServerCommand("notch", "NOTCH: show status, per-mod rules, required mods, banned mods, or the admin list",
                    r => Reply(DoInfo(r.Args.Count > 0 ? r.Args[0] : "status")),
                    new[] { ChatCommandArgument.FromChoices("show", new[] { "status", "rules", "required", "banned", "admins" }, optional: true) },
                    PermissionLevel.Admin);

                ChatCommands.RegisterServerCommand("notch-rule", "NOTCH: what happens when a player's copy of a mod does not match",
                    r => Reply(r.Args.Count < 2 ? Bad("Usage: /notch-rule <mod> <kick|warn|off|reset>") : DoRule(r.Args[0], r.Args[1], r.Sender.SteamId.ToString())),
                    new[] { ChatCommandArgument.FromChoices("mod", mods), ChatCommandArgument.FromChoices("rule", new[] { "kick", "warn", "off", "reset" }) },
                    PermissionLevel.Admin);

                ChatCommands.RegisterServerCommand("notch-require", "NOTCH: make a mod required on every player's game, or not",
                    r => Reply(r.Args.Count < 2 ? Bad("Usage: /notch-require <mod> <on|off|reset>") : DoRequire(r.Args[0], r.Args[1], r.Sender.SteamId.ToString())),
                    new[] { ChatCommandArgument.FromChoices("mod", mods), ChatCommandArgument.FromChoices("setting", new[] { "on", "off", "reset" }) },
                    PermissionLevel.Admin);

                if (AdminManager.Active)
                    ChatCommands.RegisterServerCommand("notch-admin", "NOTCH: add a player to, or remove one from, the admin list",
                        r =>
                        {
                            if (r.Args.Count < 2) return Reply(Bad("Usage: /notch-admin <add|remove> <player>"));
                            if (!r.TryGetTargetSteamId("player", out ulong id, out string error)) return Reply(Bad(error));
                            return Reply(DoAdmin(r.Args[0], id, r.Sender.SteamId.ToString()));
                        },
                        new[] { ChatCommandArgument.FromChoices("action", new[] { "add", "remove" }), new ChatCommandArgument("player", ChatArgumentType.Player) },
                        PermissionLevel.Admin);

                ChatCommands.RegisterServerCommand("notch-menu", "NOTCH: open the menu — mods, server settings, admins, player bans",
                    r => { NOTCH.Panel.PanelServer.Open(r.Sender); return ChatCommandResult.Ok(); },
                    null, PermissionLevel.Admin);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not register the /notch chat commands: " + e.Message); }
            RefreshBanCommand();

            if (!dedicatedServer) return;
            try
            {
                Remote("notch-status", a => DoInfo("status"));
                Remote("notch-rules", a => DoInfo("rules"));
                Remote("notch-required", a => DoInfo("required"));
                Remote("notch-banned", a => DoInfo("banned"));
                Remote("notch-admins", a => DoInfo("admins"));
                Remote("notch-rule", a => a.Count < 2 ? Bad("Usage: notch-rule <mod> <kick|warn|off|reset>") : DoRule(a[0], a[1], "remote command"));
                Remote("notch-require", a => a.Count < 2 ? Bad("Usage: notch-require <mod> <on|off|reset>") : DoRequire(a[0], a[1], "remote command"));
                Remote("notch-ban", a => a.Count < 2 ? Bad("Usage: notch-ban <mod or plugin id> <ban|allow>") : DoBan(a[0], a[1], "remote command", allowUnseen: true));
                Remote("notch-admin-add", a => RemoteAdmin("add", a));
                Remote("notch-admin-remove", a => RemoteAdmin("remove", a));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not register NOTCH's remote commands: " + e.Message); }
        }

        /// <summary>
        /// Called for every joining player's mod list: remembers the mods this server does not know, so an admin can
        /// pick one in /notch-ban without typing its name. The command's suggestion list is rebuilt when a new one shows up.
        /// </summary>
        internal static void NoteClientMods(WireMod[] clientMods)
        {
            bool added = false;
            foreach (WireMod c in clientMods ?? new WireMod[0])
            {
                if (string.IsNullOrEmpty(c.Guid)) continue;
                if (Plugin.KnownSet.Any(e => (byte)e.Kind == c.Kind && string.Equals(e.Guid, c.Guid, StringComparison.OrdinalIgnoreCase))) continue;
                string key = RuleOverrides.Key(c.Guid);
                if (seen.ContainsKey(key)) continue;
                seen[key] = string.IsNullOrEmpty(c.Name) ? c.Guid : c.Name;
                added = true;
            }
            if (added) RefreshBanCommand();
        }

        /// <summary>
        /// (Re)registers /notch-ban with the current list:
        ///   - mods the server knows but does not run (the Client-Mods-NoLoad folder, and client-only mods BepInEx skipped),
        ///     so a well-known cheat or broken mod can be banned BEFORE anyone joins with it: drop its file in the folder;
        ///   - mods seen on players that the server does not know;
        ///   - mods already banned.
        /// The game syncs the new list to connected admins. With nothing to offer yet, the command is not shown.
        /// </summary>
        internal static void RefreshBanCommand()
        {
            try
            {
                var candidates = new Dictionary<string, string>();
                foreach (ModEntry e in Plugin.KnownSet.Where(e => e.Origin == ModOrigin.NoLoadFolder || e.Origin == ModOrigin.SkippedByTag))
                    candidates[RuleOverrides.Key(e.Guid)] = e.Name;
                foreach (KeyValuePair<string, string> s in seen) candidates[s.Key] = s.Value;
                foreach (KeyValuePair<string, string> b in RuleStore.Banned) candidates[b.Key] = b.Value.Length > 0 ? b.Value : b.Key;
                banChoices = ModChoices.Build(candidates.Select(c => new ModEntry { Guid = c.Key, Name = c.Value }));
                ChatCommands.UnregisterServerCommand("notch-ban");
                if (banChoices.Count == 0) return;
                ChatCommands.RegisterServerCommand("notch-ban", "NOTCH: stop players joining with a mod (any version), or allow it again",
                    r => Reply(r.Args.Count < 2 ? Bad("Usage: /notch-ban <mod> <ban|allow>") : DoBan(r.Args[0], r.Args[1], r.Sender.SteamId.ToString(), allowUnseen: false)),
                    new[] { ChatCommandArgument.FromChoices("mod", banChoices.Select(c => c.Key).ToArray()), ChatCommandArgument.FromChoices("action", new[] { "ban", "allow" }) },
                    PermissionLevel.Admin);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not update the /notch-ban command: " + e.Message); }
        }

        // ---- plumbing -------------------------------------------------------------------------------------

        private struct Result { public bool Ok; public string Text; }
        private static Result Good(string text) => new Result { Ok = true, Text = text };
        private static Result Bad(string text) => new Result { Ok = false, Text = text };
        private static ChatCommandResult Reply(Result r) => r.Ok ? ChatCommandResult.Ok(r.Text) : ChatCommandResult.Error(0, r.Text);

        /// <summary>Remote commands arrive on a side thread; the work is handed to the game's main thread and waited for.</summary>
        private static void Remote(string name, Func<IReadOnlyList<string>, Result> action)
        {
            DedicatedServerCommands.Register(name, arguments =>
            {
                IReadOnlyList<string> args = arguments ?? new string[0];
                if (!DedicatedServerCommands.TryRunOnMainThreadBlocking(() => Safe(() => action(args)), out Result result))
                    return CommandResponse.Create(StatusCode.InternalServerError, "NOTCH: the command could not be run.");
                return CommandResponse.Create(result.Ok ? StatusCode.Success : StatusCode.BadArguments, result.Text);
            });
        }

        private static Result RemoteAdmin(string action, IReadOnlyList<string> a)
        {
            if (!AdminManager.Active) return Bad("NOTCH's admin management is off on this server.");
            if (a.Count < 1 || !AdminList.TryParseSteamId(a[0], out ulong id)) return Bad($"Usage: notch-admin-{action} <steam id>  (a player's SteamID64)");
            return DoAdmin(action, id, "remote command");
        }

        private static Result Safe(Func<Result> work)
        {
            try { return work(); }
            catch (Exception e) { return Bad("NOTCH: " + e.Message); }
        }

        /// <summary>A mod by its suggestion name (Configuration_Manager), its plugin ID, or its plain name.</summary>
        private static ModEntry Find(List<KeyValuePair<string, ModEntry>> choices, IEnumerable<ModEntry> all, string arg)
        {
            ModEntry byChoice = choices.FirstOrDefault(c => string.Equals(c.Key, arg, StringComparison.OrdinalIgnoreCase)).Value;
            return byChoice
                ?? all.FirstOrDefault(e => string.Equals(e.Guid, arg, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(e => string.Equals(e.Name, arg, StringComparison.OrdinalIgnoreCase));
        }

        // ---- the actions ----------------------------------------------------------------------------------

        private static Result DoInfo(string show)
        {
            show = (show ?? "status").ToLowerInvariant();
            RuleStore.Reload();
            if (show == "rules")
            {
                var lines = RuleStore.Rules.OrderBy(r => r.Key).Select(r => $"- {Shown(r.Key)}: {r.Value.ToString().ToLowerInvariant()} (set by the server manager)").ToList();
                lines.AddRange(Plugin.KnownSet.Where(e => e.CompatibleFrom.Length > 0).OrderBy(e => e.Name)
                    .Select(e => $"- {e.Name}: this server's copy ({e.Version}) accepts versions from {e.CompatibleFrom} (its author's label)"));
                if (lines.Count == 0) return Good("No per-mod rules. A mismatch is handled by the policy. Set a rule with notch-rule.");
                return Good("Per-mod rules:\n" + string.Join("\n", lines));
            }
            if (show == "required")
            {
                var required = Plugin.KnownSet.Where(e => e.Required).OrderBy(e => e.Name).ToList();
                if (required.Count == 0) return Good("No mod is required on players' games. Make one required with notch-require.");
                return Good($"Required on every player's game ({required.Count}):\n" + string.Join("\n", required.Select(e => $"- {e.Display} ({e.RequiredWhy})")));
            }
            if (show == "banned")
            {
                if (RuleStore.Banned.Count == 0)
                    return Good(banChoices.Count == 0 ? $"No mod is banned. notch-ban offers mods in {Plugin.NoLoadFolderName} and mods players bring that this server doesn't know."
                                                      : $"No mod is banned. {banChoices.Count} mod(s) can be banned with notch-ban.");
                return Good($"Banned ({RuleStore.Banned.Count}):\n" + string.Join("\n", RuleStore.Banned.OrderBy(b => b.Key).Select(b => $"- {(b.Value.Length > 0 ? b.Value + " (" + b.Key + ")" : b.Key)}")));
            }
            if (show == "admins")
            {
                if (!AdminManager.Active) return Good("NOTCH's admin management is off on this server.");
                var entries = AdminManager.Entries();
                if (entries.Count == 0) return Good($"No admins in {AdminManager.FilePathShown}.");
                var lines = entries.Select(a => $"- {a.Key}{(a.Value.Length > 0 ? " (" + a.Value + ")" : "")}{(PlayerHandle.TryGet(a.Key, out _) ? " — online" : "")}");
                return Good($"Admins ({entries.Count}):\n" + string.Join("\n", lines));
            }
            return Good(
                $"NOTCH v{Plugin.Version} — policy {Plugin.Policy.Value}\n" +
                $"Known mods: {Plugin.KnownSet.Count}, required: {Plugin.KnownSet.Count(e => e.Required)}, banned: {RuleStore.Banned.Count}. Unlisted client mods: {Plugin.UnlistedClientMods.Value}.\n" +
                $"Per-mod rules: {RuleStore.Rules.Count}.\n" +
                $"Admin management: {(AdminManager.Active ? "on, " + AdminManager.Entries().Count + " admin(s)" : "off")}.");
        }

        private static Result DoRule(string modArg, string word, string who)
        {
            ModEntry mod = Find(modChoices, Plugin.KnownSet, modArg);
            if (mod == null) return Bad($"'{modArg}' is not a mod this server knows.");
            word = (word ?? "").ToLowerInvariant();
            if (word == "reset")
            {
                RuleStore.SetRule(mod.Guid, null);
                Plugin.Log.LogInfo($"[rules] {who} reset the rule for {mod.Name}.");
                return Good($"{mod.Name}: rule removed — a mismatch is handled by the policy again. Applies from the next join.");
            }
            MismatchRule rule;
            if (word == "kick") rule = MismatchRule.Kick; else if (word == "warn") rule = MismatchRule.Warn; else if (word == "off") rule = MismatchRule.Off;
            else return Bad($"'{word}' is not a rule. Use kick, warn, off or reset.");
            RuleStore.SetRule(mod.Guid, rule);
            Plugin.Log.LogInfo($"[rules] {who} set the rule for {mod.Name} to {word}.");
            string meaning = rule == MismatchRule.Kick ? "a player with a different version or file is refused"
                           : rule == MismatchRule.Warn ? "a player with a different version or file joins and is told"
                           : "its version and file are no longer compared";
            return Good($"{mod.Name}: {word} — {meaning}. Applies from the next join.");
        }

        private static Result DoRequire(string modArg, string word, string who)
        {
            ModEntry mod = Find(modChoices, Plugin.KnownSet, modArg);
            if (mod == null) return Bad($"'{modArg}' is not a mod this server knows.");
            word = (word ?? "").ToLowerInvariant();
            if (word != "on" && word != "off" && word != "reset") return Bad($"'{word}' is not a choice. Use on, off or reset.");
            RuleStore.SetRequired(mod.Guid, word == "reset" ? (bool?)null : word == "on");
            Plugin.Log.LogInfo($"[required] {who} set {mod.Name} to '{word}' → {(mod.Required ? "required" : "not required")}.");
            string now = mod.Required ? $"is now REQUIRED on every player's game ({mod.RequiredWhy})" : "is not required on players' games";
            return Good($"{mod.Name} {now}. Applies from the next join.");
        }

        private static Result DoBan(string modArg, string word, string who, bool allowUnseen)
        {
            word = (word ?? "").ToLowerInvariant();
            if (word == "on") word = "ban"; else if (word == "off") word = "allow";      // the first wording; still accepted
            if (word != "ban" && word != "allow") return Bad($"'{word}' is not a choice. Use ban or allow.");
            ModEntry mod = Find(banChoices, banChoices.Select(c => c.Value), modArg);
            if (mod == null && allowUnseen && !string.IsNullOrWhiteSpace(modArg)) mod = new ModEntry { Guid = modArg.Trim(), Name = "" };   // remote: any plugin ID
            if (mod == null) return Bad($"'{modArg}' is not a mod in {Plugin.NoLoadFolderName}, seen on a player, or already banned.");
            ModEntry known = Plugin.KnownSet.FirstOrDefault(e => string.Equals(e.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase));
            RuleStore.SetBanned(mod.Guid, mod.Name, word == "ban");
            RefreshBanCommand();
            string shown = string.IsNullOrEmpty(mod.Name) ? mod.Guid : mod.Name;
            Plugin.Log.LogInfo($"[banned] {who} set {shown} ({mod.Guid}) to '{word}'.");
            if (word == "allow") return Good($"{shown} is no longer banned. Applies from the next join.");
            return Good($"{shown} is now BANNED: a player who has it, in any version, can't join. Applies from the next join." +
                        (known != null && known.Origin == ModOrigin.Loaded ? $" Note: this server runs {known.Name} itself." : ""));
        }

        private static Result DoAdmin(string action, ulong id, string who)
        {
            action = (action ?? "").ToLowerInvariant();
            string message = action == "add" ? AdminManager.Add(id) : action == "remove" ? AdminManager.Remove(id) : null;
            if (message == null) return Bad("Use add or remove.");
            Plugin.Log.LogInfo($"[admin] {who} ran: {action} {id} → {message}");
            return Good(message);
        }

        private static string Shown(string key)
        {
            string name = RuleStore.NameOf(key);
            return name.Length > 0 ? name : key;
        }
    }
}
