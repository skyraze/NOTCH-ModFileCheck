using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;

namespace NOTCH
{
    /// <summary>Finds mods by reading files as data (Mono.Cecil). Never loads or runs them.</summary>
    internal static class ModScanner
    {
        public const string ClientProcess = "NuclearOption";            // the game client, without ".exe"
        private static readonly Regex NobpName = new Regex(@"^(?<name>.+)_(?<ver>\d+(\.\d+)+)$");

        /// <summary>Plugin DLLs under a folder. fromNoLoadFolder = the Client-Mods-NoLoad folder.</summary>
        public static List<ModEntry> ScanPlugins(string folder, bool fromNoLoadFolder, Action<string> debug)
        {
            var found = new List<ModEntry>();
            if (!Directory.Exists(folder)) return found;
            foreach (string dll in Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(dll))
                    {
                        string hash = null;
                        // The author's own label, if any: AssemblyMetadata("NotchScope", ...).
                        string scope = asm.CustomAttributes
                            .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2
                                        && string.Equals(a.ConstructorArguments[0].Value as string, "NotchScope", StringComparison.OrdinalIgnoreCase))
                            .Select(a => a.ConstructorArguments[1].Value as string).FirstOrDefault() ?? "";
                        // …and the oldest version this copy still works with: [assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")].
                        string compatibleFrom = asm.CustomAttributes
                            .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2
                                        && string.Equals(a.ConstructorArguments[0].Value as string, "NotchCompatibleFrom", StringComparison.OrdinalIgnoreCase))
                            .Select(a => a.ConstructorArguments[1].Value as string).FirstOrDefault() ?? "";
                        // Blueprinter content: a plugin DLL with the .nobp packed inside as a resource. Only its name is read.
                        bool embedsNobp = asm.MainModule.Resources.Any(r => r.Name != null && r.Name.EndsWith(".nobp", StringComparison.OrdinalIgnoreCase));
                        foreach (TypeDefinition type in asm.MainModule.Types)
                        {
                            CustomAttribute plugin = Attr(type, "BepInEx.BepInPlugin").FirstOrDefault();
                            if (plugin == null || plugin.ConstructorArguments.Count < 3) continue;
                            hash = hash ?? Sha256(dll);
                            var entry = new ModEntry
                            {
                                Guid = (string)plugin.ConstructorArguments[0].Value,
                                Name = Net.SafeText.Clean((string)plugin.ConstructorArguments[1].Value),       // a mod's own name and version are untrusted text too
                                Version = Net.SafeText.Clean((string)plugin.ConstructorArguments[2].Value),
                                Kind = ModKind.Plugin,
                                ProcessTags = Attr(type, "BepInEx.BepInProcess").Select(FirstString).Where(s => s != null).ToArray(),
                                Dependencies = Attr(type, "BepInEx.BepInDependency").Select(FirstString).Where(s => s != null).ToArray(),
                                BlueprinterContent = embedsNobp,
                                Sha256 = hash,
                                FilePath = dll,
                                ScopeTag = scope.Trim(),
                                CompatibleFrom = compatibleFrom.Trim(),
                            };
                            entry.ClientOnlyTag = entry.ProcessTags.Length > 0 && entry.ProcessTags.All(t => SameProcess(t, ClientProcess));
                            if (fromNoLoadFolder) entry.Origin = ModOrigin.NoLoadFolder;
                            else if (Chainloader.PluginInfos.ContainsKey(entry.Guid)) entry.Origin = ModOrigin.Loaded;
                            else if (entry.ProcessTags.Length > 0 && !entry.ProcessTags.Any(t => SameProcess(t, Paths.ProcessName))) entry.Origin = ModOrigin.SkippedByTag;
                            else entry.Origin = ModOrigin.NotLoaded;
                            found.Add(entry);
                            debug?.Invoke($"scan plugin: {entry.Display} ({entry.Guid}) {entry.Origin} tags=[{string.Join(",", entry.ProcessTags)}]{(entry.ScopeTag.Length > 0 ? " scope=" + entry.ScopeTag : "")}{(entry.CompatibleFrom.Length > 0 ? " compatibleFrom=" + entry.CompatibleFrom : "")}{(entry.BlueprinterContent ? " blueprinter-content" : "")} sha256={hash} file={dll}");
                        }
                        if (hash == null) debug?.Invoke($"scan: {dll} has no BepInEx plugin in it (a library) — ignored");
                    }
                }
                catch (Exception e)
                {
                    debug?.Invoke($"scan: could not read {dll} ({e.GetType().Name}: {e.Message}) — ignored");
                }
            }
            return found;
        }

        /// <summary>Blueprinter content bundles (.nobp). Version comes from the file name (Name_1.2.3.nobp) when it has one.</summary>
        public static List<ModEntry> ScanNobp(string folder, bool fromNoLoadFolder, Action<string> debug)
        {
            var found = new List<ModEntry>();
            if (!Directory.Exists(folder)) return found;
            foreach (string file in Directory.GetFiles(folder, "*.nobp", SearchOption.AllDirectories))
            {
                try
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    Match m = NobpName.Match(stem);
                    var entry = new ModEntry
                    {
                        Name = Net.SafeText.Clean(m.Success ? m.Groups["name"].Value : stem),
                        Version = m.Success ? m.Groups["ver"].Value : "",
                        Kind = ModKind.Nobp,
                        Origin = fromNoLoadFolder ? ModOrigin.NoLoadFolder : ModOrigin.Loaded,   // "present"; Blueprinter decides what it loads
                        Sha256 = Sha256(file),
                        FilePath = file,
                    };
                    entry.Guid = "nobp:" + entry.Name;
                    found.Add(entry);
                    debug?.Invoke($"scan nobp: {entry.Display} sha256={entry.Sha256} file={file}");
                }
                catch (Exception e)
                {
                    debug?.Invoke($"scan: could not read {file} ({e.GetType().Name}: {e.Message}) — ignored");
                }
            }
            return found;
        }

        /// <summary>BepInEx patchers (BepInEx/patchers). They have no plugin GUID, so the assembly name stands in.</summary>
        public static List<ModEntry> ScanPatchers(Action<string> debug)
        {
            var found = new List<ModEntry>();
            string folder = Paths.PatcherPluginPath;
            if (!Directory.Exists(folder)) return found;
            foreach (string dll in Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(dll))
                    {
                        var entry = new ModEntry
                        {
                            Guid = "patcher:" + asm.Name.Name,
                            Name = asm.Name.Name,
                            Version = asm.Name.Version?.ToString() ?? "",
                            Kind = ModKind.Patcher,
                            Origin = ModOrigin.Loaded,
                            Sha256 = Sha256(dll),
                            FilePath = dll,
                        };
                        found.Add(entry);
                        debug?.Invoke($"scan patcher: {entry.Display} sha256={entry.Sha256} file={dll}");
                    }
                }
                catch (Exception e)
                {
                    debug?.Invoke($"scan: could not read {dll} ({e.GetType().Name}: {e.Message}) — ignored");
                }
            }
            return found;
        }

        /// <summary>"NuclearOption.exe" and "nuclearoption" are the same program. BepInEx compares the same way.</summary>
        public static bool SameProcess(string tag, string processName)
        {
            string a = tag ?? "";
            if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 4);
            return string.Equals(a, processName, StringComparison.OrdinalIgnoreCase);
        }

        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        private static IEnumerable<CustomAttribute> Attr(TypeDefinition type, string fullName) =>
            type.CustomAttributes.Where(a => a.AttributeType.FullName == fullName);

        private static string FirstString(CustomAttribute a) =>
            a.ConstructorArguments.Count > 0 ? a.ConstructorArguments[0].Value as string : null;
    }
}
