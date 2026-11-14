// Example: a client-only mod (a HUD, an overlay, anything that draws or reads input).
//
// The [BepInProcess] line is BepInEx's own attribute. It makes BepInEx load this plugin only inside
// NuclearOption.exe, so a dedicated server (NuclearOptionServer.exe) skips it by itself.
// No reference to NOTCH is needed. A server that runs NOTCH still reads the file and counts the mod
// as an allowed client mod, so players who use it are not told it is unknown.

using BepInEx;

namespace NotchExamples
{
    [BepInPlugin("com.example.clientonly", "Example Client-Only Mod", "1.0.0")]
    [BepInProcess("NuclearOption.exe")]
    public class ClientOnlyPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("Loaded in the game client only.");
        }
    }
}
