// NOTCH labels, drop-in file: copy it into your project and remove the "//" in front of the lines that apply.
// Your mod does not reference NOTCH. See docs/FOR-MOD-DEVS.md.

using System.Reflection;

// Every player must have this mod.
// [assembly: AssemblyMetadata("NotchScope", "RequiresAll")]

// This release also accepts players on older versions, from the one given here.
// [assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")]

// Config sync. Use ONE of these, or none. With none, a required mod's settings are synced by NOTCH.
// "Notch" = sync my settings with NOTCH, even if my mod is not required.
// "Self"  = my mod syncs its own settings; NOTCH leaves them alone.
// "Off"   = never sync my settings.
// [assembly: AssemblyMetadata("NotchConfigSync", "Notch")]
