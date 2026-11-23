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
        private static readonly System.Version FirstWithSync = new System.Version(0, 13, 0);

        /// <summary>The settings to send, read from each mod's own live config, so what is sent is what the server is running with.</summary>
        internal static List<SyncMod> Build()
        {
            var mods = new List<SyncMod>();
            foreach (ModEntry e in Plugin.KnownSet)
            {
                if (e.Kind != ModKind.Plugin || e.Origin != ModOrigin.Loaded || !e.Required) continue;       // required mods only, for now
                if (NeverSend.Any(n => string.Equals(n, e.Guid, StringComparison.OrdinalIgnoreCase))) { Plugin.Dbg($"[sync] {e.Name}: not sent (on the never-send list)"); continue; }
                if (CatalogueRunner.Hits.TryGetValue(e.Guid, out CatalogueHit hit) && (hit.Side == CatalogueSide.Client || hit.Side == CatalogueSide.Server))
                { Plugin.Dbg($"[sync] {e.Name}: not sent (the catalogue marks it {hit.Side})"); continue; }
                if (!Chainloader.PluginInfos.TryGetValue(e.Guid, out PluginInfo info) || info.Instance == null) { Plugin.Dbg($"[sync] {e.Name}: not sent (no running plugin to read)"); continue; }
                ConfigFile config = info.Instance.Config;
                Plugin.Dbg($"[sync] {e.Name}: {config.Keys.Count} setting(s) declared by the mod, {Unclaimed(config)} more in its file that this version of the mod does not use");
                var mod = new SyncMod { Guid = e.Guid, Version = e.Version };
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

        /// <summary>How many values sit in the mod's config file that the running mod has not declared (leftovers of an older version, usually). Debug only.</summary>
        private static int Unclaimed(ConfigFile config)
        {
            try { return (HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config) as System.Collections.ICollection)?.Count ?? -1; }
            catch { return -1; }
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
                Plugin.Log.LogInfo($"[join] {who}: sent the settings of {sent} mod(s) to {(policy == JoinPolicy.WarnOnly ? "compare (policy WarnOnly)" : "apply")}: " +
                                   $"{mods.Where(m => !leftOut.Contains(m.Guid)).Sum(m => m.Settings.Count)} setting(s), {bytes} bytes, {ConfigPack.Packets(bytes)} packet(s){(packed > 0 ? ", compressed" : "")}");
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
