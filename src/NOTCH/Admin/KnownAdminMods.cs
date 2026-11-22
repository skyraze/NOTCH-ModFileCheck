namespace NOTCH.Admin
{
    /// <summary>Mods that manage admins themselves. If one is loaded, NOTCH leaves admins to it (Auto).</summary>
    // To add one: its BepInEx plugin ID on a new line below, with a comment naming the mod.
    internal static class KnownAdminMods
    {
        internal static readonly string[] Guids =
        {
            "com.skyraze.notch.admin",      // reserved: a possible expanded admin manager of our own
        };
    }
}
