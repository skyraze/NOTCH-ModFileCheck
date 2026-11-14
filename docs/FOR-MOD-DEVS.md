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

[NotchLabels.cs](../examples/NotchLabels.cs) is a drop-in file with both `[assembly: ...]` labels commented out:
copy it into your project and uncomment the lines that apply.

With no labels at all, nothing breaks. NOTCH assumes the mod is not required and that versions must be identical,
and a server manager can change either for your mod themselves.

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

## Checking your labels

Put your mod on a dedicated server that runs NOTCH and look at the server's BepInEx log after it starts:

- a client-only mod shows `Detected <your mod> — client-only, not loaded on this server`;
- a required mod is listed in `Required on every player's game: ...`;
- an admin in game sees the same with `/notch required` and `/notch rules`, or in the Mods tab of `/notch-menu`
  under "Author says".

## Not built yet

A way for a client mod to ask at run time whether the host allows it, and switch itself off if not.
