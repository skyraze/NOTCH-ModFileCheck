// Example: a mod every player must have, which also accepts older versions from 1.15.
// The two [assembly: ...] lines are the NOTCH labels. See docs/FOR-MOD-DEVS.md.

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
