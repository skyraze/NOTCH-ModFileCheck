// NOTCH labels — drop-in file for mod authors.
//
// Copy this file into your mod's project and remove the "//" in front of the lines that apply.
// Both labels are the standard .NET attribute AssemblyMetadata:
//   - your mod does NOT reference or depend on NOTCH;
//   - they do nothing where NOTCH is not installed;
//   - NOTCH reads them from your DLL file without loading or running it.
//
// Full explanation: docs/FOR-MOD-DEVS.md

using System.Reflection;

// Every player must have this mod (it adds or changes vehicles, weapons or gameplay rules).
// On a server that runs your mod, a player without it is told to install it, or refused, depending on the server.
// [assembly: AssemblyMetadata("NotchScope", "RequiresAll")]

// This release still works with older ones, from the version given here.
// With this line in version 1.16, a server on 1.16 accepts players on 1.15 and 1.16.
// Leave it out and only the identical version is accepted (the safe default).
// [assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")]
