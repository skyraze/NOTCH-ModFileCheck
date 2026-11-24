namespace NOTCH
{
    /// <summary>What kind of file a mod is. The numbers are part of the future network format — don't reorder.</summary>
    public enum ModKind : byte { Plugin = 0, Patcher = 1, Nobp = 2 }

    /// <summary>Why NOTCH knows about a mod on this machine.</summary>
    public enum ModOrigin
    {
        Loaded,         // BepInEx loaded it here
        SkippedByTag,   // has a [BepInProcess] tag for another program, so BepInEx skipped it here
        NoLoadFolder,   // sits in Client-Mods-NoLoad: checked, never loaded
        NotLoaded,      // in the plugins folder but BepInEx did not load it, for some other reason
        Leftover        // another file with the same plugin ID is the one BepInEx loaded; this copy is not counted
    }

    /// <summary>One mod as NOTCH sees it: identity, version, file hash, and how it got on the list.</summary>
    public sealed class ModEntry
    {
        public string Guid;
        public string Name;
        public string Version;          // "" when unknown
        public ModKind Kind;
        public ModOrigin Origin;
        public bool ClientOnlyTag;      // tagged [BepInProcess("NuclearOption.exe")] and nothing else
        public string[] ProcessTags = new string[0];
        public string[] Dependencies = new string[0];
        public bool BlueprinterContent; // a plugin DLL with a Blueprinter bundle (.nobp) packed inside it — how content packs are usually shipped
        public string Sha256;           // full, lower-case hex
        public string FilePath;
        public string ScopeTag = "";    // the author's own label, from [assembly: AssemblyMetadata("NotchScope", "...")]; "" when there is none
        public string CompatibleFrom = "";  // the author's label on THIS copy: the oldest version it still works with, from [assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")]
        public string ConfigSyncTag = "";   // the author's label for config sync: Self, Notch or Off; "" when there is none
        public string OwnSync = "";     // a sync library found in the file (ServerSync, Jotunn, CSync), or ""
        public bool Required;           // server side: every joining player must have this mod (set by RequiredRules.Apply)
        public string RequiredWhy = ""; // server side: the reason, in a few words

        public string Display => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";
        public string ShortHash => Sha256 == null ? "" : Sha256.Substring(0, 12) + "…";
    }
}
