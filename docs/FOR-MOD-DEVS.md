# N.O.T.C.H — notes for mod developers

This file covers anything that means changing or adding code in a mod. Installing, configuring and how NOTCH
behaves are in the [README](../README.md).

**You never need to reference or depend on NOTCH.** Everything here is a standard BepInEx or .NET attribute. Your
mod behaves exactly the same where NOTCH is not installed.

## At a glance

| You want to say | Add | Example file |
|---|---|---|
| "This mod is for the game client only" | `[BepInProcess("NuclearOption.exe")]` on your plugin class | [ClientOnlyPlugin.cs](../examples/ClientOnlyPlugin.cs) |
| "Every player must have this mod" | `[assembly: AssemblyMetadata("NotchScope", "RequiresAll")]` | [RequiredPlugin.cs](../examples/RequiredPlugin.cs) |
| "Older versions still work with this one" | `[assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")]` | [RequiredPlugin.cs](../examples/RequiredPlugin.cs) |
| "Sync my settings with NOTCH, even if my mod is not required" | `[assembly: AssemblyMetadata("NotchConfigSync", "Notch")]` | [SyncedSettingsPlugin.cs](../examples/SyncedSettingsPlugin.cs) |
| "I sync my own settings; leave them alone" | `[assembly: AssemblyMetadata("NotchConfigSync", "Self")]` | [NotchLabels.cs](../examples/NotchLabels.cs) |
| "Never sync my settings" | `[assembly: AssemblyMetadata("NotchConfigSync", "Off")]` | [NotchLabels.cs](../examples/NotchLabels.cs) |
| "This one setting belongs to the player" | the tag `"NotchClient"` on that setting | [SyncedSettingsPlugin.cs](../examples/SyncedSettingsPlugin.cs) |

[NotchLabels.cs](../examples/NotchLabels.cs) is a drop-in file with the `[assembly: ...]` labels commented out:
copy it into your project and uncomment the lines that apply.

With no labels at all, nothing breaks. NOTCH assumes the mod is not required and that versions must be identical,
and a server manager can change either for your mod themselves. If the mod is required, its settings are synced
(see [Config sync](#config-sync-your-mods-settings)).

## Blueprinter

NOTCH recognises Blueprinter and Blueprinter content, and checks them like any other mod. **NOTCH does not need
Blueprinter**, and Blueprinter content needs nothing from NOTCH: there is nothing you have to add.

Content is recognised in both forms it ships in:

- **A plugin DLL with the `.nobp` packed inside it** (the usual form). NOTCH sees the packed bundle and treats
  the plugin as Blueprinter content. Its ID, name and version come from your `[BepInPlugin(...)]` line, like any
  plugin, and the labels on this page work on it.
- **A loose `.nobp` file.** It is recognised by its file name: `Name_1.2.3.nobp` is read as *Name*, version
  *1.2.3*. Keep the name part the same between releases. It can't carry labels, so a player's copy must be the
  identical version and file.

Either way, Blueprinter content is treated as required on every player's game, unless the server manager says
otherwise.

**Keep the version in `[BepInPlugin(...)]` in step with your release version.** It is the version NOTCH shows.
If your download is 1.2.2 but the plugin line still says 1.0.0, a player who lacks your mod is told to install
"v1.0.0".

## How NOTCH recognises your mod

| What | Where it comes from | Advice |
|---|---|---|
| ID | The first value in `[BepInPlugin(...)]` | Never change it. Rules and bans on servers are stored by ID. |
| Version | The third value in `[BepInPlugin(...)]` | Raise it for **every** build you release. |
| File hash | SHA-256 of your DLL | Nothing to do. |

Two different files with the same version number are reported to players as "file differs", and they are told
to reinstall. Raising the version on every release avoids that.

This table is for BepInEx plugins (DLLs), content packs included. For loose `.nobp` files see
[Blueprinter](#blueprinter).

NOTCH reads your DLL as data. It never loads or runs it to find any of this.

## Marking a mod as client-only

If your mod only makes sense in the game client (a HUD, an overlay, anything that draws or reads input), add one
line under your `[BepInPlugin]` line:

```csharp
[BepInPlugin("com.you.yourmod", "Your Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]          // <- this line
public class YourMod : BaseUnityPlugin
```

This is BepInEx's own attribute. It tells BepInEx to load the plugin only inside `NuclearOption.exe`. The
dedicated server runs as `NuclearOptionServer.exe`, so BepInEx skips your mod there and logs
`Skipping [Your Mod 1.0.0] because of process filters (NuclearOption.exe)`.

- It works on servers that don't run NOTCH at all.
- A player who hosts from inside the game is still running `NuclearOption.exe`, so the mod loads for them.
- A server that runs NOTCH still reads the skipped file and counts it as an allowed client mod, so players who
  have it are not told it is unknown.

If you don't tag it, server managers can still allow your mod themselves (README, "Client mods the server can't
run"). Tagging saves them the step.

BepInEx documents the attribute here:

- Plugin tutorial: https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html
- API reference: https://docs.bepinex.dev/api/BepInEx.BepInProcess.html

## Marking a mod as required on every client

If your mod only works when every player has it (it adds or changes vehicles, weapons or gameplay rules), add
this to any one `.cs` file in your project, outside the namespace:

```csharp
using System.Reflection;

[assembly: AssemblyMetadata("NotchScope", "RequiresAll")]
```

On a server that runs your mod, a player who lacks it is told to install it, or refused, depending on the
server's policy.

- Any other value means "not needed by everyone".
- A server manager can override the label either way.
- Blueprinter itself and Blueprinter content are treated as required without a label.

## Saying which older versions still work

By default NOTCH treats two copies of your mod as matching only when their versions are identical. If a new
release still works with older ones, say from which version on:

```csharp
using System.Reflection;

[assembly: AssemblyMetadata("NotchCompatibleFrom", "1.15")]
```

With this line in version 1.16, a server running 1.16 accepts players on 1.15 up to 1.16.

- **The server's copy decides.** The label on a player's copy is ignored.
- **It only reaches downward.** A server on 1.15 does not accept 1.16, because the 1.15 file was built before
  1.16 existed and cannot vouch for it.
- **Leave it out when in doubt.** Identical versions are the safe default, and a server manager can relax the
  check for your mod themselves.

## Config sync: your mod's settings

A server that runs NOTCH can make its values for a mod's settings hold on every player's game while they are
connected. You don't have to build anything for this, and your mod never talks to NOTCH.

**What NOTCH does.** When a player has passed the mod check, the server sends the values of the settings your
plugin has declared with `Config.Bind`, as they are in the server's memory at that moment. The player's NOTCH
sets them in memory, before the map loads. When the player leaves, their own values are put back. Nothing is
written to the player's config file.

**Which mods.** By default, mods that are [required on every client](#marking-a-mod-as-required-on-every-client),
which includes Blueprinter content. A server manager can change that for any mod. A mod NOMNOM lists as
client-only or server-only is never synced.

### Saying how your settings should be treated

One label, with three values. It goes in any one `.cs` file, outside the namespace, like the other labels:

```csharp
using System.Reflection;

[assembly: AssemblyMetadata("NotchConfigSync", "Notch")]
```

| Value | Meaning | Can a server manager change it? |
|---|---|---|
| `Notch` | Sync my settings with NOTCH, even if my mod is not required. They go to the players who have the mod. | Yes: they can turn it down to warn or off |
| `Self` | My mod syncs its own settings. NOTCH leaves them alone. | No |
| `Off` | Never sync my settings. | No |

With no label, NOTCH also looks for a sync library inside your mod or among the files it refers to (ServerSync,
Jotunn or CSync, by name). If it finds one it leaves your settings alone, as if you had written `Self`. Write
`Notch` if you want NOTCH's sync anyway. The label always wins over what NOTCH finds.

The player's game reads the label on the player's own copy too: a copy labelled `Self` or `Off` is never touched,
whatever a server sends.

### Keeping one setting for the player

Some settings belong to the player even in a synced mod: a key binding the player chose, a window position, a
volume, a colour. Mark each with one extra word where you declare it:

```csharp
volume = Config.Bind("Sound", "Volume", 80,
    new ConfigDescription("How loud the warning tone is.", null, "NotchClient"));
```

`"NotchClient"` is plain text in BepInEx's own list of tags for a setting. BepInEx and Configuration Manager
ignore a tag they don't know, so the setting behaves and displays exactly as before. A setting with this tag is
never sent by a server and never changed on a player's game.

### Making your mod follow the server

NOTCH can set a value. Only your mod can act on it.

- **Read a setting when you use it** (`mySetting.Value` at the point of use). The server's value takes effect at
  once.
- **Or react to the change**: `mySetting.SettingChanged += (s, e) => Apply(mySetting.Value);`
- **A value copied once in `Awake` will not follow the server.** Values arrive when the player joins, long after
  start-up. The setting changes, and your copy doesn't.

### What can and cannot be synced

- Only settings declared on your plugin's own `Config`. A second config file your mod opens itself is not read.
- A setting is applied only if the player's copy of your mod has the same section and key with the same type.
  Anything else is skipped and listed in the player's report.
- **Text settings only take plain characters:** letters, digits, spaces, hyphens, underscores and commas, 64
  characters at most. This protects players from a hostile server; it is why a text setting can never be given a
  link or a file path. A value with a full stop in it, such as `1.5` or a file name, is not applied. Numbers,
  switches, list choices and key bindings are not affected. If a setting has to carry such text, tag it
  `NotchClient` or label the mod `Self`.
- Out-of-range values are adjusted by your own limits (`AcceptableValueRange` and the like), as always.
- One mod's settings must fit in about 64,000 bytes when sent. That is a few thousand ordinary settings.

### If your mod writes to its own settings

- **While a player is connected, a synced setting that changes is put back** to the server's value. If your mod
  keeps rewriting it, NOTCH notices the fight and stops putting that one setting back, and the server manager is
  told which mod and setting it was. If your mod needs to own a setting, tag it `NotchClient`.
- **Saving.** While a server's values are in your settings, NOTCH holds back writes to your config file, so the
  server's values never reach the player's disk. A save your mod asks for is done once when the player leaves,
  with the player's own values.

## Checking your labels

Put your mod on a dedicated server that runs NOTCH and look at the server's BepInEx log after it starts:

- a client-only mod shows `Detected <your mod> — client-only, not loaded on this server`;
- a required mod is listed in `Required on every player's game: ...`;
- an admin in game sees the same with `/notch required` and `/notch rules`, or in the Mods tab of `/notch-menu`
  under "Author says";
- for config sync, `/notch config` and the table at the bottom of the menu's Server tab list your mod with what
  happens to its settings and why: `sync (author asked for it)`, `off (author: syncs itself)`,
  `off (author: never sync)` or `off (has its own sync (...))`. A mod that declares no settings is not listed;
- a player's `BepInEx/NOTCH/notch-checks.yaml` lists each setting a server set, and any it could not, with the
  reason.

## Not built yet

A way for a client mod to ask at run time whether the host allows it, and switch itself off if not.
