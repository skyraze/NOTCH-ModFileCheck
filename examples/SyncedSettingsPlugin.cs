// Example: a mod whose settings NOTCH syncs from the server to every player for the session.
// The [assembly: ...] line asks for that even though the mod is not required. No reference to NOTCH is needed.
// See docs/FOR-MOD-DEVS.md.

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

[assembly: AssemblyMetadata("NotchConfigSync", "Notch")]

namespace NotchExamples
{
    [BepInPlugin("com.example.syncedsettings", "Example Synced Settings Mod", "1.0.0")]
    public class SyncedSettingsPlugin : BaseUnityPlugin
    {
        private ConfigEntry<int> maxSpeed;      // follows the server
        private ConfigEntry<bool> hardMode;     // follows the server
        private ConfigEntry<int> volume;        // the player's own: tagged "NotchClient"
        private bool hardModeInUse;

        private void Awake()
        {
            maxSpeed = Config.Bind("Rules", "MaxSpeed", 300, "Read each time it is used, so the server's value takes effect at once.");
            hardMode = Config.Bind("Rules", "HardMode", false, "The mod keeps its own copy and updates it when the setting changes.");
            volume = Config.Bind("Sound", "Volume", 80,
                new ConfigDescription("How loud the warning tone is. Marked as the player's own.", null, "NotchClient"));

            // A copy taken here would NOT follow the server by itself: values arrive when the player joins.
            // Reacting to the change keeps the copy right.
            hardModeInUse = hardMode.Value;
            hardMode.SettingChanged += (s, e) => hardModeInUse = hardMode.Value;
        }

        // Reading the setting at the point of use always gives the value in force.
        private int SpeedLimit() => hardModeInUse ? maxSpeed.Value / 2 : maxSpeed.Value;
    }
}
