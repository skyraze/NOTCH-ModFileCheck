using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Mirage;
using NOTCH.Net;

namespace NOTCH.Sync
{
    /// <summary>Player side of config sync: applies a server's values in memory only, and puts the player's own back on every way out.</summary>
    internal static class ConfigApply
    {
        private sealed class Held
        {
            public ConfigEntryBase Entry; public string Own, Server, Guid, Label;
            public bool Stopped, Told;
            public readonly RevertWindow Window = new RevertWindow();
        }

        private static readonly PartsBin incoming = new PartsBin();
        private static readonly List<Held> held = new List<Held>();                                   // the player's own values, to put back
        private static readonly Dictionary<ConfigFile, bool> files = new Dictionary<ConfigFile, bool>();   // file → its own "save on change" switch
        private static readonly HashSet<ConfigFile> saveWanted = new HashSet<ConfigFile>();           // a save was asked for while held: done on release
        private static readonly Dictionary<ConfigFile, EventHandler<SettingChangedEventArgs>> watching = new Dictionary<ConfigFile, EventHandler<SettingChangedEventArgs>>();
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private static INetworkPlayer link;                  // the server, for put-back reports

        /// <summary>What happened at the last join, in plain lines. For the log now; the message window and the report later.</summary>
        internal static List<string> LastLines { get; private set; } = new List<string>();
        internal static bool Active => held.Count > 0;
        private static string pendingMessage;                // waits until the match screen exists

        // While a server's values are in a mod's settings, that mod's config file must not be written: the values are for this session only.
        [HarmonyPatch(typeof(ConfigFile), nameof(ConfigFile.Save))]
        internal static class SavePatch
        {
            private static bool Prefix(ConfigFile __instance)
            {
                if (!files.ContainsKey(__instance)) return true;
                saveWanted.Add(__instance);
                return false;
            }
        }

        /// <summary>A part of the server's settings arrived. Nothing happens until every part is in.</summary>
        internal static void OnConfig(INetworkPlayer server, NotchConfigV1 msg)
        {
            try
            {
                if (Plugin.IsServerProcess) return;
                byte[] plain = ConfigPack.Unsqueeze(msg.Data ?? new byte[0], msg.Compressed);
                List<SyncMod> mods = incoming.Add(msg.Part, msg.Parts, plain);
                if (mods != null) Take(mods, msg.Apply, server);
            }
            catch (InvalidDataException e) { Plugin.Log.LogWarning("Config sync: the server's settings could not be read and were ignored (" + e.Message + ")."); }
            catch (Exception e) { Plugin.Log.LogWarning("Config sync failed: " + e.Message); }
        }

        /// <summary>Applies (or, for a WarnOnly server, only compares) the server's values.</summary>
        private static void Take(List<SyncMod> mods, bool apply, INetworkPlayer server)
        {
            Restore("new server");                              // never let one server's values carry into another's
            link = server;
            var lines = new List<string>();                     // what needs the player's attention
            var setLines = new List<string>();                  // what was set; listed after, and capped
            int applied = 0, same = 0, differ = 0;
            foreach (SyncMod mod in mods)
            {
                if (!Chainloader.PluginInfos.TryGetValue(mod.Guid, out PluginInfo info) || info.Instance == null) continue;   // not running here: nothing to set
                if (string.Equals(mod.Guid, Plugin.Guid, StringComparison.OrdinalIgnoreCase)) continue;                       // NOTCH's own settings are never touched
                ConfigFile config = info.Instance.Config;
                string name = info.Metadata.Name;
                SyncLabel ownLabel = SyncLabels.Parse(Plugin.KnownSet.FirstOrDefault(e => e.Origin == ModOrigin.Loaded && string.Equals(e.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase))?.ConfigSyncTag);
                if (ownLabel == SyncLabel.Self || ownLabel == SyncLabel.Off)   // the author's label on the player's own copy is honoured here too
                {
                    lines.Add($"{name}: not touched: your copy of the mod is labelled by its author to keep its own settings");
                    continue;
                }
                bool applyMod = apply && mod.Apply;                 // the server may send one mod to apply and another only to compare
                foreach (SyncSetting s in mod.Settings)
                {
                    var def = new ConfigDefinition(s.Section, s.Key);
                    ConfigEntryBase entry = config.ContainsKey(def) ? config[def] : null;
                    ApplyResult rule = ApplyRule.Decide(s, entry?.SettingType.FullName, entry != null && ConfigSend.HasClientTag(entry));
                    string label = $"{name}: [{s.Section}] {s.Key}";
                    if (!applyMod && rule == ApplyResult.NotPlainText) rule = ApplyResult.Apply;   // a mod that is only compared: nothing is applied, so nothing to block
                    if (rule != ApplyResult.Apply) { if (rule != ApplyResult.ClientOnly) lines.Add($"{label} {ApplyRule.Why(rule)}"); continue; }
                    string own = entry.GetSerializedValue();
                    if (own == s.Value) { same++; continue; }
                    if (!applyMod) { differ++; lines.Add($"{label} differs: yours is {own}, the server's is {Shown(s.Value)}"); continue; }

                    if (!files.ContainsKey(config)) { files[config] = config.SaveOnConfigSet; config.SaveOnConfigSet = false; }   // stop saves before the first change
                    entry.SetSerializedValue(s.Value);
                    string now = entry.GetSerializedValue();
                    held.Add(new Held { Entry = entry, Own = own, Server = now, Guid = mod.Guid, Label = label });
                    applied++;
                    setLines.Add(now == s.Value ? $"{label} = {now} (yours: {own})" : $"{label} = {now} (yours: {own}; the server sent {Shown(s.Value)}, your copy of the mod adjusted it)");
                }
            }

            // From here on, a change to a held setting is put back. Listening starts after the values are in, so NOTCH's own changes are not seen.
            foreach (ConfigFile config in files.Keys.ToList())
            {
                ConfigFile file = config;
                EventHandler<SettingChangedEventArgs> handler = (o, e) => Changed(file, e.ChangedSetting);
                file.SettingChanged += handler;
                watching[file] = handler;
            }

            string summary = applied > 0
                ? $"Config sync: this server set {applied} setting(s) for this session. Your own values come back when you leave." +
                  (differ > 0 ? $" {differ} other setting(s) differ and were left alone (the server only warns about those)." : "")
                : differ > 0 ? $"Config sync: {differ} setting(s) differ from this server's. Nothing was changed (the server only warns); you may see differences in play."
                : $"Config sync: your settings match this server's ({same} checked).";
            lines = SyncReport.Cap(lines, setLines);
            lines.Insert(0, summary);
            LastLines = lines;
            Plugin.Log.LogInfo(summary);
            foreach (string line in lines.Skip(1)) Plugin.Log.LogInfo("  - " + line);

            WriteReport(summary, lines.Skip(1).ToList());
            if (applied > 0 || differ > 0)                      // only when there is something to tell
            {
                string told = applied > 0
                    ? $"NOTCH: this server set {applied} mod setting(s) for this session. Your own values return when you leave." + (differ > 0 ? $" {differ} more differ and were left alone." : "")
                    : $"NOTCH: {differ} of your mod setting(s) differ from this server's. Nothing was changed.";
                Popup.Note(told);
                pendingMessage = told + " Details: " + Plugin.ReportPathShown;
            }
        }

        /// <summary>A setting in a held file changed. A held one goes back to the server's value; any other is the player's own and is saved on release.</summary>
        private static void Changed(ConfigFile file, ConfigEntryBase entry)
        {
            try
            {
                Held h = held.FirstOrDefault(x => x.Entry == entry);
                if (h == null) { saveWanted.Add(file); return; }
                if (h.Stopped || entry.GetSerializedValue() == h.Server) return;    // stopped, or this is NOTCH's own put-back arriving
                if (h.Window.Add(clock.Elapsed.TotalSeconds, GuardTracker.SafetySeconds) >= GuardTracker.SafetyCount)
                {
                    h.Stopped = true;                                               // the fixed safety net: something is fighting over it
                    Send(RevertKind.SafetyNet, h.Guid, entry.Definition.Section, entry.Definition.Key);
                    Remark($"{h.Label} was changed {GuardTracker.SafetyCount} times in {GuardTracker.SafetySeconds:0} seconds, so NOTCH stopped putting the server's value back");
                    return;
                }
                Send(RevertKind.PutBack, h.Guid, entry.Definition.Section, entry.Definition.Key);
                if (!h.Told) { h.Told = true; Remark($"{h.Label} was changed during the session and put back to the server's value"); }
                entry.SetSerializedValue(h.Server);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Config sync: could not put a setting back: " + e.Message); }
        }

        /// <summary>The server says: stop putting this setting back. Always answered, even for a setting already stopped or no longer held.</summary>
        internal static void OnStop(INetworkPlayer server, NotchRevertStopV1 msg)
        {
            try
            {
                string key = RevertServer.KeyOf(msg.Guid, msg.Section, msg.Key);
                Held h = held.FirstOrDefault(x => RevertServer.KeyOf(x.Guid, x.Entry.Definition.Section, x.Entry.Definition.Key) == key);
                if (h != null && !h.Stopped) { h.Stopped = true; Remark($"{h.Label} keeps being changed, so the server told NOTCH to stop putting its value back"); }
                server.Send(new NotchRevertV1 { Protocol = Wire.ProtocolVersion, Kind = (byte)RevertKind.Ack, Guid = msg.Guid, Section = msg.Section, Key = msg.Key });
            }
            catch (Exception e) { Plugin.Log.LogWarning("Config sync: could not answer the server's stop: " + e.Message); }
        }

        private static void Send(RevertKind kind, string guid, string section, string key)
        {
            try { link?.Send(new NotchRevertV1 { Protocol = Wire.ProtocolVersion, Kind = (byte)kind, Guid = guid, Section = section, Key = key }); }
            catch (Exception e) { Plugin.Dbg("config sync: report not sent: " + e.Message); }
        }

        /// <summary>A line for the log and the report file. Nothing is shown on screen.</summary>
        private static void Remark(string line)
        {
            Plugin.Log.LogInfo("Config sync: " + line + ".");
            if (LastLines.Count == 0) return;
            LastLines.Add(line);
            WriteReport(LastLines[0], LastLines.Skip(1).ToList());
        }

        /// <summary>A server's value made safe to show: cleaned like every other text from the network, and cut short.</summary>
        private static string Shown(string value)
        {
            string clean = SafeText.Clean(value ?? "");
            return clean.Length > 80 ? clean.Substring(0, 80) + "…" : clean;
        }

        /// <summary>Called from the plugin's OnGUI: writes the waiting line into the game's own message window once the match screen exists. Local only; nothing is sent.</summary>
        internal static void Tell()
        {
            if (pendingMessage == null || GameplayUI.i == null) return;
            string message = pendingMessage;
            pendingMessage = null;
            try { GameplayUI.i.GameMessage(message); }
            catch (Exception e) { Plugin.Log.LogWarning("Config sync: could not write to the game's message window: " + e.Message); }
        }

        /// <summary>Adds the result to the newest check in the player's report file. Never fatal.</summary>
        private static void WriteReport(string summary, List<string> details)
        {
            try
            {
                string path = Plugin.ReportPath;
                if (File.Exists(path)) File.WriteAllText(path, CheckReport.AddSettings(File.ReadAllText(path), summary, details));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not add the settings to {CheckReport.FileName}: {e.Message}"); }
        }

        /// <summary>Puts the player's own values back and lets the files be saved again. Safe to call at any time, any number of times.</summary>
        internal static void Restore(string why)
        {
            pendingMessage = null;
            foreach (KeyValuePair<ConfigFile, EventHandler<SettingChangedEventArgs>> w in watching) w.Key.SettingChanged -= w.Value;   // stop listening before the values go back
            watching.Clear();
            link = null;
            if (held.Count == 0 && files.Count == 0) return;
            int count = held.Count;
            for (int i = held.Count - 1; i >= 0; i--)
            {
                try { held[i].Entry.SetSerializedValue(held[i].Own); }
                catch (Exception e) { Plugin.Log.LogWarning("Config sync: could not put back a setting: " + e.Message); }
            }
            held.Clear();
            var release = files.ToList();
            files.Clear();                                      // from here on, saves go through again
            foreach (KeyValuePair<ConfigFile, bool> f in release)
            {
                f.Key.SaveOnConfigSet = f.Value;
                if (saveWanted.Contains(f.Key)) { try { f.Key.Save(); } catch (Exception e) { Plugin.Log.LogWarning("Config sync: could not save a config file: " + e.Message); } }
            }
            saveWanted.Clear();
            Plugin.Log.LogInfo($"Config sync: put back {count} of your own setting(s) ({why}).");
        }
    }
}
