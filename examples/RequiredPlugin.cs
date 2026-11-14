// Example: a mod every player must have, which also says which older versions still work.
//
// The two [assembly: ...] lines are the NOTCH labels. They go outside the namespace, in any one .cs file.
// They are a standard .NET attribute, so this mod does not reference NOTCH.
//
//   NotchScope = RequiresAll       a player without this mod is told to install it (or refused).
//   NotchCompatibleFrom = 1.15     a server running this copy (1.16.0) accepts players on 1.15.x and 1.16.0.
//                                  The SERVER's copy decides, and it only reaches downward.

using System.Reflection;
using BepInEx;

[assembly: AssemblyMetadata("NotchScope", "RequiresAll")]
[assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")]

namespace NotchExamples
{
    [BepInPlugin("com.example.required", "Example Required Mod", "1.16.0")]
    public class RequiredPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("Loaded on the server and on every player's game.");
        }
    }
}
