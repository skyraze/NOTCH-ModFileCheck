using System;
using System.Collections.Generic;
using System.Linq;

namespace NOTCH.Catalogue
{
    /// <summary>The catalogue entry found for one installed mod, and how it was found.</summary>
    public sealed class CatalogueHit
    {
        public string Id = "";
        public CatalogueSide Side;
        public string How = "";
    }

    /// <summary>All that NOTCH keeps from a catalogue: one small record per matched mod, and the names of the rest.</summary>
    public sealed class CatalogueMatched
    {
        public Dictionary<string, CatalogueHit> ByGuid = new Dictionary<string, CatalogueHit>(StringComparer.OrdinalIgnoreCase);
        public List<string> Unmatched = new List<string>();
    }

    /// <summary>Pairs the mods on this machine with catalogue entries, without any network call. Pure logic.</summary>
    public static class CatalogueMatch
    {
        private const int MaxExtendsDepth = 8;

        public static CatalogueMatched Run(IEnumerable<ModEntry> mods, IList<CatalogueEntry> entries)
        {
            var byId = new Dictionary<string, CatalogueEntry>(StringComparer.OrdinalIgnoreCase);
            var byFile = new Dictionary<string, List<CatalogueEntry>>(StringComparer.OrdinalIgnoreCase);
            var byHash = new Dictionary<string, List<CatalogueEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogueEntry e in entries ?? new CatalogueEntry[0])
            {
                if (!byId.ContainsKey(e.Id)) byId[e.Id] = e;
                foreach (string f in e.FileNames) Add(byFile, f, e);
                foreach (string h in e.Hashes) Add(byHash, h, e);
            }

            var result = new CatalogueMatched();
            foreach (ModEntry mod in mods ?? new ModEntry[0])
            {
                if (string.IsNullOrEmpty(mod.Guid) || result.ByGuid.ContainsKey(mod.Guid)) continue;
                string file = FileOf(mod.FilePath);
                CatalogueEntry entry = null; string how = null;
                foreach (string folder in FoldersOf(mod.FilePath))
                    if (byId.TryGetValue(folder, out entry)) { how = "folder name"; break; }
                if (how != null) { }
                else if (byId.TryGetValue(mod.Guid, out entry)) how = "plugin ID";
                else if (Single(byFile, file, out entry)) how = "file name";
                else if (Single(byHash, mod.Sha256 ?? "", out entry)) how = "file hash";
                else { result.Unmatched.Add(mod.Display); continue; }
                result.ByGuid[mod.Guid] = new CatalogueHit { Id = entry.Id, Side = SideOf(entry, byId), How = how };
            }
            return result;
        }

        /// <summary>An entry's own value, or for an add-on with none, the value of the mod it extends.</summary>
        public static CatalogueSide SideOf(CatalogueEntry entry, IDictionary<string, CatalogueEntry> byId)
        {
            for (int depth = 0; entry != null && depth < MaxExtendsDepth; depth++)
            {
                if (entry.Side != CatalogueSide.None) return entry.Side;
                if (entry.Extends.Length == 0 || !byId.TryGetValue(entry.Extends, out CatalogueEntry parent) || ReferenceEquals(parent, entry)) break;
                entry = parent;
            }
            return CatalogueSide.None;
        }

        private static void Add(Dictionary<string, List<CatalogueEntry>> index, string key, CatalogueEntry e)
        {
            if (!index.TryGetValue(key, out List<CatalogueEntry> list)) index[key] = list = new List<CatalogueEntry>();
            if (!list.Contains(e)) list.Add(e);
        }

        /// <summary>A match by file name or hash counts only when exactly one entry has it.</summary>
        private static bool Single(Dictionary<string, List<CatalogueEntry>> index, string key, out CatalogueEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(key) || !index.TryGetValue(key, out List<CatalogueEntry> list) || list.Count != 1) return false;
            entry = list[0];
            return true;
        }

        private static string[] Parts(string path) => (path ?? "").Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        public static string FileOf(string path) { string[] p = Parts(path); return p.Length == 0 ? "" : p[p.Length - 1]; }

        private static readonly string[] OwnFolders = { "plugins", "patchers", "addons", "BepInEx", "Client-Mods-NoLoad" };

        /// <summary>The folders a mod's file sits in, nearest first, up to BepInEx's own. BepInEx's own folder names are left out: they name no mod.</summary>
        public static List<string> FoldersOf(string path)
        {
            string[] p = Parts(path);
            int root = Array.FindIndex(p, s => string.Equals(s, "BepInEx", StringComparison.OrdinalIgnoreCase));
            int stop = root >= 0 ? root : Math.Max(-1, p.Length - 3);       // no BepInEx folder in the path: only the folder the file is in
            var folders = new List<string>();
            for (int i = p.Length - 2; i > stop; i--)
                if (!OwnFolders.Any(own => string.Equals(p[i], own, StringComparison.OrdinalIgnoreCase))) folders.Add(p[i]);
            return folders;
        }

        /// <summary>The nearest of those folders, or "".</summary>
        public static string FolderOf(string path) => FoldersOf(path).FirstOrDefault() ?? "";
    }
}
