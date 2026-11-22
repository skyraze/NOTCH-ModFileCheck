// Example: a client-only mod (a HUD, an overlay, anything that draws or reads input).
// [BepInProcess] makes a dedicated server skip it. No reference to NOTCH is needed. See docs/FOR-MOD-DEVS.md.

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
