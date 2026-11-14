# N.O.T.C.H

**Nuclear Option Transponder Check Handshake**

A BepInEx 5 plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/). A server
that runs NOTCH asks each joining player which mods they have, compares the answer with its own, and either tells
the player what doesn't match or refuses the join.

> **Beta.** Everything described here is built and has been tested on a local dedicated server with one player.
> It has not yet been tested with several players at once or on a remote server. See *Reporting a problem*.

NOTCH is optional. It does not replace BepInEx or a mod manager, and a server without it works as before.
It is an honor-system tool, not anti-cheat (see *Limits*).

**Works with [Blueprinter](#blueprinter).** NOTCH recognises Blueprinter and its content files (`.nobp`) and
checks them like any other mod. NOTCH does not need Blueprinter: it runs the same with or without it.

**Writing a mod?** See [docs/FOR-MOD-DEVS.md](docs/FOR-MOD-DEVS.md) and the [examples](examples/) folder.

## Contents

- [Installing](#installing)
- [For players](#for-players)
- [For server managers](#for-server-managers)
  - [The join check](#the-join-check)
  - [Blueprinter](#blueprinter)
  - [Client mods the server can't run](#client-mods-the-server-cant-run)
  - [Required mods](#required-mods)
  - [Per-mod rules](#per-mod-rules)
  - [Banned mods](#banned-mods)
  - [Admins](#admins)
  - [Commands](#commands)
  - [The menu](#the-menu)
  - [Remote commands](#remote-commands)
- [Configuration](#configuration)
- [Files NOTCH uses](#files-notch-uses)
- [Reporting a problem](#reporting-a-problem)
- [Limits](#limits)
- [Planned](#planned)
- [License](#license)

## Installing

Requires BepInEx 5. Tested with BepInEx 5.4.23.6 and Nuclear Option 0.34.6.

1. Put `NOTCH.dll` in `BepInEx/plugins/NOTCH/`: in the game folder for players, in the dedicated server folder
   for servers.
2. Start once. The settings file `BepInEx/config/com.skyraze.notch.cfg` is created.

A server and its players need the **same NOTCH.dll**. NOTCH checks its own file like any other mod.

## For players

Install NOTCH and play as usual. On a server without NOTCH, nothing changes.

On a server with NOTCH, your mods are checked when you join:

- **Everything matches:** you join as normal.
- **Something doesn't match:** either you join and see a notice listing each problem and how to fix it, or the
  server refuses you and names the mods that caused it.

Your last 10 join checks are saved in `BepInEx/NOTCH/notch-checks.yaml`. Send that file to the server manager if
you need help.

## For server managers

**By default NOTCH refuses players who don't have NOTCH.** Tell your players before you add it, the same as when
you add or remove any mod. To let players without NOTCH in, set `Policy = WarnOnly`.

When the server starts, NOTCH reads every mod it can find and builds its **known set**:

- BepInEx plugins in `BepInEx/plugins`
- Blueprinter content bundles (`.nobp`)
- BepInEx patchers in `BepInEx/patchers`
- anything in the `Client-Mods-NoLoad` folder ([below](#client-mods-the-server-cant-run))

For each one it records the name, version and a SHA-256 hash of the file. Files are only read; NOTCH never loads
or runs another mod's code to do this. It then reports in the BepInEx log and console:

```
NOTCH (Nuclear Option Transponder Check Handshake) v0.11.1 — dedicated server, 4 mod(s) known, Client-Mods-NoLoad: 1 mod(s), debug off — policy RequireNotch
Detected SomeHud 1.2.3 — client-only, not loaded on this server. Counted as an allowed client mod.
Detected FrontlineMap 1.3.1 in Client-Mods-NoLoad — not loaded on this server. Counted as an allowed client mod.
```


### The join check

1. The server asks the joining player's game for its mod list. A game with NOTCH answers in a fraction of a
   second. A game without NOTCH doesn't answer, and the server stops waiting after `ReplyTimeoutSeconds`.
2. The server compares the list with its known set: is each mod known, does the version match, and, when the
   version matches, is the file identical.
3. The server's `Policy` decides what happens:

| Policy | Player without NOTCH | Banned mod, or a problem with a required mod | Any other difference |
|---|---|---|---|
| `RequireNotch` (default) | refused | refused | joins, shown a notice |
| `WarnOnly` | joins, not checked | joins, shown a notice | joins, shown a notice |
| `RequireMatch` | refused | refused | refused |
| `Off` | joins, not asked | joins, not asked | joins, not asked |

A "problem with a required mod" means it is missing, or the player's copy doesn't match.

A copy **doesn't match** when it is a version the server's copy doesn't accept, or the same version with a
different file. Versions must be identical unless the mod's author says otherwise (see
[docs/FOR-MOD-DEVS.md](docs/FOR-MOD-DEVS.md)).

You can change the outcome for any single mod with [per-mod rules](#per-mod-rules),
[required mods](#required-mods) and [banned mods](#banned-mods).

**What the player sees.** A player who is let in with differences gets a notice as soon as they are in the match.
It lists each problem with the server's side next to theirs, and what to do about it:

```
SomeMod: server has 1.1.0, you have 1.0.0. Update to v1.1.0.
OtherMod 2.0.0: file differs (server 7e4d2ff5fcef…, you 27522f655566…). Verify / reinstall your mod files.
ThirdMod 1.2.5: not on this server.
```

A refused player gets the same lines as the reason for the disconnect. If there is nothing to report, the player
sees nothing.

**What you see.** Each join is summarised in the server's log, with a hint on how to allow a mod the server
doesn't know. The player's own report file, `BepInEx/NOTCH/notch-checks.yaml`, holds the full file hashes and the
path of their copy of each mod, so ask for it when someone needs help.

### Blueprinter

NOTCH is built to work alongside Blueprinter, and does not depend on it. Without Blueprinter installed, NOTCH
does everything else exactly the same.

- **Content is recognised in both forms.** Most content packs are a plugin DLL with the `.nobp` bundle packed
  inside it; some are a loose `.nobp` file. NOTCH recognises both, wherever the file sits under
  `BepInEx/plugins`, and compares players' copies against the server's by version and file hash.
- **Content is required by default.** Blueprinter itself and Blueprinter content count as
  [required](#required-mods), because a player without the same vehicles can't play properly. You can switch
  that off for any of them.
- **A missing Blueprinter is pointed out.** If the server has Blueprinter content and Blueprinter isn't loaded,
  NOTCH says so in the log.

NOTCH shows the name and version a content pack declares inside its file, which can differ from the name and
version on its download page. A loose `.nobp` file's name and version are read from its file name
(`Name_1.2.3.nobp`).

**Dedicated servers.** In our testing (Blueprinter 2.1.1, game 0.34.6), a dedicated server with Blueprinter
content installed did not finish starting until `SkipAdditionalAssets = true` was set in
`BepInEx/config/com.nikkorap.blueprinter.cfg` on the server. This happens with or without NOTCH.

### Client mods the server can't run

Many mods only make sense in the game client: HUDs, overlays, map tools. A dedicated server has no screen, and
some of these misbehave there. The server still needs to *know* them, so that players who use them aren't told
their mod is unknown. There are two ways:

1. **The mod is tagged client-only by its author.** Install it on the server like any other mod. BepInEx skips it
   there (a yellow `Skipping [...] because of process filters` line is expected), and NOTCH prints
   `Detected ... client-only, not loaded on this server`.
2. **The mod is not tagged.** Put its DLL (or `.nobp`) in:

   ```
   BepInEx/config/NOTCH/Client-Mods-NoLoad/
   ```

   Mods in this folder are **not loaded** on the server. They are only made visible to it, so the server can
   manage them: allow them, check players' copies against them, or ban them. The folder is created the first time
   NOTCH runs on a server. It has no on/off setting: if a mod is in it, it counts.

Keep the copy in the folder at the version you want players to have, since the version and file hash are what
players are checked against. If the same mod is both installed and in the folder, the installed copy is used and
NOTCH asks you to remove one.

### Required mods

Some mods only work if every player has them, such as anything that adds or changes vehicles. NOTCH treats a mod
as **required on every player's game** when:

- it is Blueprinter itself or [Blueprinter content](#blueprinter); or
- its author labelled it as required; or
- you switched it on with `/notch-require` or in the menu.

Your own choice always wins, in both directions. A player who lacks a required mod gets a line such as
`SomeJet: required by this server, and you don't have it. Install SomeJet v1.2.0.`

### Per-mod rules

For any mod the server knows, you can decide what happens when a player's copy doesn't match, whatever the policy
says:

| Rule | A player whose copy doesn't match |
|---|---|
| `kick` | is refused |
| `warn` | joins and is told, even under `RequireMatch` |
| `off` | isn't checked: that mod's version and file are not compared |
| `reset` | is handled by the policy again |

- `kick` is useful for a client mod where one particular build is a problem, or to make sure nobody joins with a
  modified copy. The server holds the real file and compares against it.
- `off` does not remove the need to *have* a required mod.
- Under `WarnOnly` nobody is refused, whatever the rule.

### Banned mods

A player can't join with a banned mod, in **any version**. A ban overrides every other setting, except that
under `WarnOnly` it only shows in the notice. You can ban:

- **mods in `Client-Mods-NoLoad`**, so a well-known cheat or broken mod can be banned *before* anyone joins with
  it. Put its file in the folder, restart, and ban it;
- **mods players have brought** that your server doesn't know, since it last started.

A mod is known by the ID inside its file, so renaming the file doesn't get around a ban.

### Admins

The game has permission levels (Player, Moderator, Admin, Host) but no way of its own to make a player an admin
on a dedicated server. NOTCH includes a basic way to do it:

```
BepInEx/config/NOTCH/admins.txt
```

One Steam ID (the long number, SteamID64) per line; text after a `#` is a note. The file is created the first
time NOTCH runs on a server. A player listed there gets the game's **Admin** level when they join, which is what
`/adminkick` and other mods' Admin commands check. Edits apply from the next join; no restart needed.

To make the first admin, edit the file, or use `notch-admin-add` from the server machine
([remote commands](#remote-commands)). After that, admins can be managed in game.

NOTCH only ever raises a level and never changes the host's. If you run another mod that manages admins, set
`AdminManagement = Off`.

### Commands

Typed in the game's chat by a player with the Admin level, or the host. The chat box suggests the commands and
their arguments as you type, so they can be picked with the arrow keys. Players without the level don't see them.

| Command | Does |
|---|---|
| `/notch-menu` | Opens [the menu](#the-menu), which does everything below in one window |
| `/notch` or `/notch status` | Policy, number of known and required mods, whether admin management is on |
| `/notch rules` | The per-mod rules in effect, and any version labels from mod authors |
| `/notch required` | The mods required on every player's game, and why |
| `/notch banned` | The banned mods |
| `/notch admins` | The admin list, and who on it is online |
| `/notch-rule <mod> <kick\|warn\|off\|reset>` | Sets what happens when a player's copy of that mod doesn't match |
| `/notch-require <mod> <on\|off\|reset>` | Makes that mod required or not; `reset` returns it to NOTCH's own rule |
| `/notch-ban <mod> <ban\|allow>` | Stops players joining with that mod, or allows it again |
| `/notch-admin <add\|remove> <player>` | Adds a player to, or removes one from, `admins.txt` |

- `<mod>` is picked from a list. A name with spaces is shown with underscores (`Configuration_Manager`).
- Choices are saved in `BepInEx/config/NOTCH/` and apply from the next join.
- `/notch-admin add` gives an online player the Admin level straight away. `remove` sets them back to Player
  straight away, but only if NOTCH gave them the level.
- `/notch-admin` is not available when admin management is off.

### The menu

`/notch-menu` opens a window with four tabs:

| Tab | What it holds |
|---|---|
| **Mods** | One row per mod, with buttons for *Required*, *If it doesn't match* and *Ban*, and a column saying what a player would get with the current settings. Changed rows turn yellow. |
| **Server** | The policy and the other server-wide settings. |
| **Admins** | The admin list: remove someone, add an online player, or add a Steam ID by hand. |
| **Player bans** | The game's own ban list: unban, kick or ban an online player, or ban a Steam ID by hand. |

- On *Mods* and *Server* nothing changes until you press **Apply**.
- On *Admins* and *Player bans* each button acts straight away, after a second click to confirm.
- A Steam ID that isn't a player's is pointed out and can't be submitted.
- Player bans are ordinary game bans. They are written to the game's ban file and keep working if NOTCH is
  removed. A dedicated server needs a ban file for this (`BanListPaths` in `DedicatedServerConfig.json`).

### Remote commands

A dedicated server started with `-ServerRemoteCommands` accepts the same actions on the game's remote-command
port (7779 by default), from Shockfront's control panel or any tool that speaks its format. This is not standard
RCON. The port only accepts connections from the server machine itself and has no password.

| Remote command | Same as |
|---|---|
| `notch-status`, `notch-rules`, `notch-required`, `notch-banned`, `notch-admins` | `/notch …` |
| `notch-rule <mod> <kick\|warn\|off\|reset>` | `/notch-rule` |
| `notch-require <mod> <on\|off\|reset>` | `/notch-require` |
| `notch-ban <mod or plugin ID> <ban\|allow>` | `/notch-ban`; also accepts a plugin ID directly, with no file needed |
| `notch-admin-add <steam id>`, `notch-admin-remove <steam id>` | `/notch-admin` |

## Configuration

`BepInEx/config/com.skyraze.notch.cfg`. The `Server` settings can also be changed in [the menu](#the-menu).

| Setting | Default | What it does |
|---|---|---|
| `General` / `Debug` | `false` | Verbose logging: every file scanned, with full hashes. Turn on when troubleshooting. Needs a restart. |
| `Server` / `Policy` | `RequireNotch` | `RequireNotch`, `WarnOnly`, `RequireMatch` or `Off`. See [the join check](#the-join-check). |
| `Server` / `ReplyTimeoutSeconds` | `3` | How long a joining player is held while NOTCH waits for their mod list (1–6). A player without NOTCH waits this long. |
| `Server` / `UnlistedClientMods` | `Report` | `Report` = a player's mod this server doesn't know counts as a difference. `Ignore` = only mods the server knows are checked. |
| `Server` / `AdminManagement` | `Auto` | `Auto` = on, unless a known admin mod is loaded. `On` = always on. `Off` = NOTCH leaves admins alone. |

The `Server` settings do nothing in a player's game.

## Files NOTCH uses

| File or folder | Where | What it is |
|---|---|---|
| `BepInEx/config/com.skyraze.notch.cfg` | both | Settings |
| `BepInEx/NOTCH/notch-checks.yaml` | player | The last 10 join checks |
| `BepInEx/config/NOTCH/Client-Mods-NoLoad/` | server | Client mods the server should know but not run |
| `BepInEx/config/NOTCH/admins.txt` | server | The admin list |
| `BepInEx/config/NOTCH/overrides.yaml` | server | Per-mod rules |
| `BepInEx/config/NOTCH/required.yaml` | server | Your required / not required choices |
| `BepInEx/config/NOTCH/banned.yaml` | server | Banned mods |

The server files are plain text and can be edited by hand. Edits apply from the next join.

## Reporting a problem

Open an issue in the **Issues** tab of this repository on GitHub. Please include:

- what you did and what you expected;
- the NOTCH version, from the first NOTCH line in the log;
- from the player: `BepInEx/NOTCH/notch-checks.yaml` and `BepInEx/LogOutput.log`;
- from the server: `BepInEx/LogOutput.log`, ideally with `Debug = true`.

Be aware that the logs list the mods you have installed and the folders they are in.

## Limits

- **Not anti-cheat.** NOTCH catches outdated and mismatched mods. It will not stop someone determined to fake
  their list.
- **Nothing is downloaded or run.** NOTCH never installs, loads or runs a mod because a server or a player named
  it. It only compares and reports.
- **Player bans and kicks use the game's internal code**, not its official modding interface, so a game update
  could break that one tab of the menu.

## Planned

- A way for a client mod to ask whether the host allows it, and switch itself off if not.
- Integration with NOMM or Hexium, so a player can get the exact mods and versions a server asks for.

## License

MIT, see [LICENSE](LICENSE). Source is public and unobfuscated. Developed with AI assistance (Claude); every
change is reviewed by the maintainer.
