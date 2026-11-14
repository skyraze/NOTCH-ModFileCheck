namespace NOTCH.Admin
{
    /// <summary>
    /// Mods that manage server admins themselves. When AdminManagement = Auto and one of these is LOADED,
    /// NOTCH's own basic admin management stays off and leaves admins to that mod.
    ///
    /// To add a mod: put its BepInEx plugin ID (the first text in its [BepInPlugin(...)] line; NOTCH prints
    /// it at start-up when Debug is on) on a new line below, with a comment saying which mod it is. Rebuild.
    /// </summary>
    internal static class KnownAdminMods
    {
        internal static readonly string[] Guids =
        {
            "com.skyraze.notch.admin",      // reserved: a possible expanded admin manager of our own
        };
    }
}
