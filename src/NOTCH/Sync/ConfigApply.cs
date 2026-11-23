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
        private struct Held { public ConfigEntryBase Entry; public string Own; }

        private static readonly PartsBin incoming = new PartsBin();
        private static readonly List<Held> held = new List<Held>();                                   // the player's own values, to put back
        private static readonly Dictionary<ConfigFile, bool> files = new Dictionary<ConfigFile, bool>();   // file → its own "save on change" switch
        private static readonly HashSet<ConfigFile> saveWanted = new HashSet<ConfigFile>();           // a save was asked for while held: done on release

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
                if (mods != null) Take(mods, msg.Apply);
            }
            catch (InvalidDataException e) { Plugin.Log.LogWarning("Config sync: the server's settings could not be read and were ignored (" + e.Message + ")."); }
            catch (Exception e) { Plugin.Log.LogWarning("Config sync failed: " + e.Message); }
        }

        /// <summary>Applies (or, for a WarnOnly server, only compares) the server's values.</summary>
        private static void Take(List<SyncMod> mods, bool apply)
        {
            Restore("new server");                              // never let one server's values carry into another's
            var lines = new List<string>();
            int applied = 0, same = 0, differ = 0;
            foreach (SyncMod mod in mods)
            {
                if (!Chainloader.PluginInfos.TryGetValue(mod.Guid, out PluginInfo info) || info.Instance == null) continue;   // not running here: nothing to set
                if (string.Equals(mod.Guid, Plugin.Guid, StringComparison.OrdinalIgnoreCase)) continue;                       // NOTCH's own settings are never touched
                ConfigFile config = info.Instance.Config;
                string name = info.Metadata.Name;
                foreach (SyncSetting s in mod.Settings)
                {
                    var def = new ConfigDefinition(s.Section, s.Key);
                    ConfigEntryBase entry = config.ContainsKey(def) ? config[def] : null;
                    ApplyResult rule = ApplyRule.Decide(s, entry?.SettingType.FullName, entry != null && ConfigSend.HasClientTag(entry));
                    string label = $"{name}: [{s.Section}] {s.Key}";
                    if (!apply && rule == ApplyResult.NotPlainText) rule = ApplyResult.Apply;      // nothing is applied on a WarnOnly server, so it is only compared
                    if (rule != ApplyResult.Apply) { if (rule != ApplyResult.ClientOnly) lines.Add($"{label} {ApplyRule.Why(rule)}"); continue; }
                    string own = entry.GetSerializedValue();
                    if (own == s.Value) { same++; continue; }
                    if (!apply) { differ++; lines.Add($"{label} differs: yours is {own}, the server's is {Shown(s.Value)}"); continue; }

                    if (!files.ContainsKey(config)) { files[config] = config.SaveOnConfigSet; config.SaveOnConfigSet = false; }   // stop saves before the first change
                    held.Add(new Held { Entry = entry, Own = own });
                    entry.SetSerializedValue(s.Value);
                    string now = entry.GetSerializedValue();
                    applied++;
                    lines.Add(now == s.Value ? $"{label} = {now} (yours: {own})" : $"{label} = {now} (yours: {own}; the server sent {Shown(s.Value)}, your copy of the mod adjusted it)");
                }
            }

            string summary = apply
                ? (applied == 0 ? $"Config sync: your settings already match this server's ({same} checked)."
                                : $"Config sync: this server set {applied} setting(s) for this session. Your own values come back when you leave.")
                : (differ == 0 ? $"Config sync: your settings match this server's ({same} checked)."
                               : $"Config sync: {differ} setting(s) differ from this server's. Nothing was changed (the server only warns); you may see differences in play.");
            lines.Insert(0, summary);
            LastLines = lines;
            Plugin.Log.LogInfo(summary);
            foreach (string line in lines.Skip(1)) Plugin.Log.LogInfo("  - " + line);

            WriteReport(summary, lines.Skip(1).ToList());
            if (applied > 0 || differ > 0)                      // only when there is something to tell
            {
                string told = apply ? $"NOTCH: this server set {applied} mod setting(s) for this session. Your own values return when you leave."
                                    : $"NOTCH: {differ} of your mod setting(s) differ from this server's. Nothing was changed.";
                Popup.Note(told);
                pendingMessage = told + " Details: " + Plugin.ReportPathShown;
            }
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
