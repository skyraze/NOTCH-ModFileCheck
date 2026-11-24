using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Mirage;
using NOTCH.Catalogue;
using NOTCH.Net;

namespace NOTCH.Sync
{
    /// <summary>Server side of config sync: reads the settings of the mods that qualify and sends them to a player who was just let in.</summary>
    internal static class ConfigSend
    {
        /// <summary>Never sent, whatever else is decided: NOTCH's own settings, Configuration Manager's, and Blueprinter's own (its content mods still sync). BepInEx.cfg belongs to no plugin, so it is never read.</summary>
        private static readonly string[] NeverSend = { Plugin.Guid, "com.bepis.bepinex.configurationmanager", Plugin.BlueprinterGuid };
        private static readonly System.Version FirstWithSync = new System.Version(0, 90, 0);

        /// <summary>One loaded plugin as config sync sees it: how many settings it has declared and what happens to them.</summary>
        internal struct Row { public ModEntry Mod; public int Settings; public SyncChoice Choice; public SyncVerdict Verdict; }

        /// <summary>Every loaded plugin that has settings, with the decision for each. For the command, the menu and Build.</summary>
        internal static List<Row> Rows()
        {
            var rows = new List<Row>();
            foreach (ModEntry e in Plugin.KnownSet)
            {
                if (e.Kind != ModKind.Plugin || e.Origin != ModOrigin.Loaded) continue;
                if (!Chainloader.PluginInfos.TryGetValue(e.Guid, out PluginInfo info) || info.Instance == null) continue;
                int settings = info.Instance.Config.Keys.Count;
                if (settings == 0) continue;                             // nothing declared: nothing to sync, nothing to choose
                bool never = NeverSend.Any(n => string.Equals(n, e.Guid, StringComparison.OrdinalIgnoreCase));
                CatalogueSide side = CatalogueRunner.Hits.TryGetValue(e.Guid, out CatalogueHit hit) ? hit.Side : CatalogueSide.None;
                RuleStore.Sync.TryGetValue(SyncChoices.Key(e.Guid), out SyncChoice choice);
                rows.Add(new Row { Mod = e, Settings = settings, Choice = choice, Verdict = SyncDecision.Decide(never, SyncLabels.Parse(e.ConfigSyncTag), e.OwnSync, side, e.Required, choice) });
            }
            return rows;
        }

        /// <summary>The settings to send, read from each mod's own live config, so what is sent is what the server is running with.</summary>
        internal static List<SyncMod> Build()
        {
            var mods = new List<SyncMod>();
            foreach (Row row in Rows())
            {
                Plugin.Dbg($"[sync] {row.Mod.Name}: {SyncDecision.Word(row.Verdict.Mode)} ({row.Verdict.Why}), {row.Settings} setting(s)");
                if (row.Verdict.Mode == SyncMode.Off) continue;
                ConfigFile config = Chainloader.PluginInfos[row.Mod.Guid].Instance.Config;
                var mod = new SyncMod { Guid = row.Mod.Guid, Version = row.Mod.Version, Apply = row.Verdict.Mode == SyncMode.Sync };
                foreach (ConfigDefinition def in config.Keys.ToList())
                {
                    ConfigEntryBase entry = config[def];
                    if (HasClientTag(entry)) continue;                  // its author marked it as the player's alone
                    mod.Settings.Add(new SyncSetting { Section = def.Section, Key = def.Key, Type = entry.SettingType.FullName, Value = entry.GetSerializedValue() });
                }
                if (mod.Settings.Count > 0) mods.Add(mod);
            }
            return mods;
        }

        internal static bool HasClientTag(ConfigEntryBase entry) =>
            entry.Description?.Tags != null && entry.Description.Tags.Any(t => t as string == ApplyRule.ClientTag);

        /// <summary>Called for a player who passed the mod check. WarnOnly sends the values to be compared, not applied; Off sends nothing.</summary>
        internal static void SendTo(INetworkPlayer player, string who, JoinPolicy policy, WireMod[] clientMods)
        {
            try
            {
                if (policy == JoinPolicy.Off) return;
                if (!KnowsSync(clientMods)) { Plugin.Dbg($"[join] {who}: their NOTCH is older than {FirstWithSync}: no settings sent"); return; }
                List<SyncMod> mods = Build();
                if (mods.Count == 0) return;
                List<byte[]> parts = ConfigPack.Parts(mods, out List<string> leftOut);
                int bytes = 0, packed = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    byte[] data = ConfigPack.Squeeze(parts[i], out bool compressed);
                    player.Send(new NotchConfigV1
                    {
                        Protocol = Wire.ProtocolVersion, Apply = policy != JoinPolicy.WarnOnly,
                        Part = (byte)i, Parts = (byte)parts.Count, Compressed = compressed, Data = data,
                    });
                    bytes += data.Length; if (compressed) packed++;
                }
                int sent = mods.Count - leftOut.Count;
                RevertServer.Note(player, who, policy == JoinPolicy.WarnOnly ? new SyncMod[0] : mods.Where(m => m.Apply && !leftOut.Contains(m.Guid)));
                Plugin.Log.LogInfo($"[join] {who}: sent the settings of {sent} mod(s) ({(policy == JoinPolicy.WarnOnly ? "all to compare, policy WarnOnly" : mods.Count(m => m.Apply) + " to apply, " + mods.Count(m => !m.Apply) + " to compare")}): " +
                                   $"{mods.Where(m => !leftOut.Contains(m.Guid)).Sum(m => m.Settings.Count)} setting(s), {bytes} bytes, {parts.Count} part(s), {ConfigPack.Packets(bytes)} packet(s){(packed > 0 ? ", compressed" : "")}");
                if (leftOut.Count > 0) Plugin.Log.LogWarning($"[join] Settings too large to send, left out: {string.Join(", ", leftOut)}");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[join] {who}: could not send settings: {e.Message}"); }
        }

        /// <summary>True when the player's NOTCH is new enough to know the settings message (older ones are not sent it).</summary>
        private static bool KnowsSync(WireMod[] clientMods)
        {
            WireMod notch = (clientMods ?? new WireMod[0]).FirstOrDefault(m => string.Equals(m.Guid, Plugin.Guid, StringComparison.OrdinalIgnoreCase));
            return notch.Guid != null && System.Version.TryParse(notch.Version ?? "", out System.Version v) && v >= FirstWithSync;
        }
    }
}
