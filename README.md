# N.O.T.C.H

**Nuclear Option Transponder Check Handshake**

A BepInEx 5 plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/). A server
that runs NOTCH does two things for each joining player:

- **Mod check.** It asks which mods the player has, compares the answer with its own, and either tells the player
  what doesn't match or refuses the join.
- **Config sync.** It makes the settings of its required mods hold on the player's game while they are connected.
  The player's own settings come back when they leave, and nothing is written to their files.

Server managers get commands and an in-game menu for all of it, a basic admin list, and the game's player bans.

> **Beta, version 0.90.0.** This release has the full feature set planned for 1.0. Everything described here works and
> has been tested in game on a dedicated server, including with Blueprinter content. It has not yet been tested
> with several players at once, or on a game that hosts for others. 1.0.0 will be this same code once it has been
> proven in real use. See *Reporting a problem*.

NOTCH is optional. It does not replace BepInEx or a mod manager, and a server without it works as before.
It is an honor-system tool, not anti-cheat (see *Limits*).

**Coming from 0.12.0?** See [Updating from 0.12.0](#updating-from-0120).

**Works with [Blueprinter](#blueprinter) 2.1.2 and later.** NOTCH recognises Blueprinter and its content files
(`.nobp`) and checks them like any other mod. NOTCH does not need Blueprinter: it runs the same with or without it.

**Writing a mod?** See [docs/FOR-MOD-DEVS.md](docs/FOR-MOD-DEVS.md) and the [examples](examples/) folder.

**Reading the code?** See [docs/CODE-GUIDE.md](docs/CODE-GUIDE.md).

## Contents

- [Installing](#installing)
- [Updating from 0.12.0](#updating-from-0120)
- [The Network Access Notice](#the-network-access-notice)
- [For players](#for-players)
- [For server managers](#for-server-managers)
  - [The join check](#the-join-check)
  - [Config sync](#config-sync)
  - [The NOMNOM catalogue](#the-nomnom-catalogue)
  - [Blueprinter](#blueprinter)
  - [NOMM and NOSMR](#nomm-and-nosmr)
  - [Client mods the server can't run](#client-mods-the-server-cant-run)
  - [Required mods](#required-mods)
  - [Per-mod rules](#per-mod-rules)
  - [Banned mods](#banned-mods)
  - [Leftover files](#leftover-files)
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

1. Download `NOTCH.dll` from the [Releases](https://github.com/skyraze/NOTCH-ModFileCheck/releases) page and put it in
   `BepInEx/plugins/NOTCH/`: in the game folder for players, in the dedicated server folder
   for servers.
2. Start once. The settings file `BepInEx/config/com.skyraze.notch.cfg` is created, and NOTCH shows its
   [Network Access Notice](#the-network-access-notice).

A server and its players need the **same NOTCH.dll**. NOTCH checks its own file like any other mod.

## Updating from 0.12.0

0.90.0 is a much larger release than its predecessor: config sync, the NOMNOM catalogue check, the notice that
goes with it, and a good deal more in the menu. Update the server and the players together.

- **A player still on 0.12.0 can connect** under the default policy. They are shown a notice telling them to
  update, and get none of the new features: no settings are synced to them. Under `RequireMatch` they are refused.
- **A 0.90.0 player on a 0.12.0 server** is checked as before, and nothing else happens.
- **Your settings and rule files carry over.** Nothing needs to be redone.
- **Config sync is on by default** for required mods. Read [Config sync](#config-sync) before you update a server,
  and tell your players, the same as when you add or remove any mod.
- **Each machine is asked once** to accept or decline the Network Access Notice. NOTCH works either way.

## The Network Access Notice

NOTCH can read [NOMNOM](https://github.com/KopterBuzz/NOMNOM)'s public mod catalogue to learn whether a mod is
meant for clients, for servers or for both. That is the only thing NOTCH ever connects to, and it does not do so
until the notice has been accepted **on that machine**. The full text is written to
`BepInEx/config/NOTCH/NETWORK-ACCESS-NOTICE.txt`.

- **On a game** the notice appears once in the main menu, with Accept, Decline and Later. "Don't ask again" stops
  the pop-up; the answer can then be given in the settings file.
- **On a dedicated server** a block in the console points to the notice at every start until it is answered. Answer
  in the settings file (`Answer = Accepted` or `Answer = Declined`), or once, as an admin in the game's chat, with
  `/notch-notice`, which opens the notice on your own game and sends your answer to the server.
- **Declining costs very little.** NOTCH then contacts nothing and decides from what the mods themselves say.
- **The answer can be changed** at any time in the settings file. If the notice's text ever changes, NOTCH asks
  again.

A game only uses the catalogue while it is hosting for other players. It never does while you are simply playing
on someone else's server, or in single player. See [The NOMNOM catalogue](#the-nomnom-catalogue).

## For players

Install NOTCH and play as usual. On a server without NOTCH, nothing changes.

On a server with NOTCH, your mods are checked when you join:

- **Everything matches:** you join as normal.
- **Something doesn't match:** either you join and see a notice listing each problem and how to fix it, or the
  server refuses you and names the mods that caused it.

**Your mod settings on a server.** A server can set some of your mods' settings for as long as you are connected,
so that everyone plays by the same values. When it does:

- a line in the game's message window says how many settings were set;
- your own values come back the moment you leave, are kicked, or lose the connection;
- nothing is written to your config files, so a crash cannot leave a server's values behind;
- only settings your own copy of the mod already has can be set, and a text setting can only be given plain
  characters, never a link or a file path.

If you change one of those settings while connected, NOTCH puts the server's value back. In a settings window
that writes as you type, this can look as if your typing is being ignored. Settings that are not synced are yours
and are left alone.

Your last 10 join checks are saved in `BepInEx/NOTCH/notch-checks.yaml`, with every setting a server set and any it
could not. Send that file to the server manager if you need help.

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
NOTCH (Nuclear Option Transponder Check Handshake) v0.90.0 — dedicated server, 4 mod(s) known, Client-Mods-NoLoad: 1 mod(s), debug off — policy RequireNotch
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

A refused player sees the reason in the game's own disconnect box. That box is small, so it shows a count and the
three most serious problems, in short form:

```
[NOTCH] Can't join server. 4 problems detected, showing 3
BadMod is not allowed here
Missing SomeJet v1.2.0
SomeMod: need v1.1.0, you have v1.0.0
```

A player without NOTCH is told `This server requires the NOTCH mod. Install NOTCH and rejoin.` If there is
nothing to report, the player sees nothing.

**What you see.** Each join is summarised in the server's log, with a hint on how to allow a mod the server
doesn't know. The player's own report file, `BepInEx/NOTCH/notch-checks.yaml`, holds every problem, the full file
hashes and the path of their copy of each mod, so ask for it when someone needs help.

### Config sync

BepInEx has no way of its own to make a player's mod settings match the server's. NOTCH does it for every mod at
once, so mod makers don't each have to build it.

**What is synced.** By default, the settings of every [required mod](#required-mods) the server is running. The
server sends the values it is actually running with, right after the mod check passes and before the map loads.
Nobody picks individual settings.

**What is never sent**, whatever you choose:

- NOTCH's own settings, Configuration Manager's, and Blueprinter's own (its content mods still sync);
- any mod NOMNOM marks as client-only or server-only;
- any mod whose author labelled it `Self` or `Off`, or that carries a sync library of its own;
- any single setting its author marked as the player's (`NotchClient`);
- a mod the server does not load. Mods in `Client-Mods-NoLoad`, and mods only players have, are never touched.

**Your choice, per mod.** For each mod that has settings you can choose, with `/notch-config` or in
[the menu](#the-menu):

| Choice | What happens on a player's game |
|---|---|
| `sync` | The server's values are applied for the session |
| `warn` | Nothing is changed; the player is told which settings differ |
| `off` | That mod's settings are not sent |
| `reset` | Back to the default: required mods sync, others do not |

You can set a mod that is not required to `sync`; its settings then go to the players who have it. `/notch config`
lists every mod with settings, what happens to it, and why.

**How the policy fits in.** `WarnOnly` never changes anything on a player's game: every synced mod is compared and
reported only. `Off` sends nothing. `RequireNotch` and `RequireMatch` apply the values.

**If a synced setting is changed during play.** NOTCH on the player's game puts the server's value back. If the
same setting keeps being changed, something is fighting over it, usually a mod that rewrites its own setting. The
server then tells that game to stop putting it back, and records it. This guard is **experimental**:

- the server stops a setting after `RevertCount` changes within `RevertSeconds` (3 within 10 by default);
- each game also has a fixed limit of its own, 20 changes in 10 seconds, for a fight too fast for the server to
  catch;
- a game that does not acknowledge the server's stop within 30 seconds is dropped from the server.

Once a setting is stopped it keeps whatever value was written last, for the rest of that session. **So the guard
is not an enforcement tool.** A player who changes a synced setting several times in a few seconds will trip it,
and their value then holds. NOTCH gives the player the benefit of the doubt and tells you: every stop is listed in
the menu and by `/notch config` with the player's name and Steam ID, the mod and the setting. Kick or ban as you
see fit. Remove an entry once its cause is dealt with; the server log keeps the full record.

**Size.** A normal server's settings fit in a single network packet. Larger sets are compressed and split
automatically. One mod's settings must fit in about 64,000 bytes, and a whole send in about 512,000; anything
beyond that is left out and named in the server log.

**Good to know.**

- Values are applied when a player joins. If you change a setting on the server mid-mission, players already
  connected keep the old value until they rejoin.
- A mod that copies a setting once at start-up keeps using its copy. NOTCH can set the value, but only the mod's
  author can make the mod react to it.
- The player's list of applied settings shows 200 at most, problems first.

### The NOMNOM catalogue

NOMNOM's catalogue says, for many mods, whether a mod is meant for clients, for servers or for both. NOTCH uses
that for one thing: a mod marked client-only or server-only never has its settings synced. It never makes a mod
required, and nothing from the catalogue is sent to players.

- It is read only after the [Network Access Notice](#the-network-access-notice) is accepted on that machine, and
  only by a dedicated server or a game that is hosting for others.
- It is checked when the server starts, and then when a mission loads once an hour has passed, or after two hours
  at the latest. The work is done in the background at the lowest priority, with a 10-second limit, and never
  holds up a join.
- A download is checked before it is used: it must be within the size limit (8 MB by default; the catalogue is
  under 1 MB), read as the catalogue's structure, and not be drastically smaller or different from the last good
  copy. A failed or rejected check changes nothing; NOTCH carries on with what it had.
- NOTCH connects to NOMNOM's two catalogue addresses and nothing else. The addresses are fixed in the code and
  cannot be changed by a setting, a server, a player or the catalogue itself.

`/notch catalogue` shows the state of the last check and which installed mods were matched.

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

**Blueprinter's own settings are never synced.** They are about each machine, and the one that matters on a
server, `SkipAdditionalAssets`, has to be the opposite on a player's game. Content mods sync by the normal rules.

**Use Blueprinter 2.1.2 or later.** That is the version NOTCH is tested with and supports. Blueprinter 2.1.1 and
earlier had instabilities that needed workarounds. One example: in our testing (2.1.1, game 0.34.6) a dedicated
server with Blueprinter content installed did not finish starting until `SkipAdditionalAssets = true` was set by
hand in `BepInEx/config/com.nikkorap.blueprinter.cfg`, with or without NOTCH. Blueprinter 2.1.2 sets that on
servers by itself. NOTCH does not block an older Blueprinter; it is simply not what we test against.

### NOMM and NOSMR

NOTCH works alongside [NOMM](https://github.com/Combat787/NOMM) and its server plugin NOSMR, and does a different
job. NOSMR tells NOMM which mods a server runs, so players can get them before joining. NOTCH checks what a player
actually has when they join.

If you use both, keep them in step. When a player joins through NOMM, NOMM switches their mods to match the pack
your server broadcasts. A mod that NOTCH requires but your NOMM pack leaves out gets switched off, and NOTCH then
refuses the player for not having it. After changing your server's mods, export a fresh pack.

NOTCH does not read or rely on the files NOMM writes.

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

### Leftover files

When a mod is updated by copying the new file in beside the old one, BepInEx loads one and skips the other.
NOTCH does not count the skipped copy: it is not known, not required and not sent to players. It is named in the
server log at every start and in a notice at the top of the menu's Mods tab, until the file is removed and the
server restarted.

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
| `/notch catalogue` | The state of the last [NOMNOM catalogue](#the-nomnom-catalogue) check |
| `/notch config` | Every mod with settings, what happens to it and why, and the settings that had to be stopped |
| `/notch-rule <mod> <kick\|warn\|off\|reset>` | Sets what happens when a player's copy of that mod doesn't match |
| `/notch-require <mod> <on\|off\|reset>` | Makes that mod required or not; `reset` returns it to NOTCH's own rule |
| `/notch-ban <mod> <ban\|allow>` | Stops players joining with that mod, or allows it again |
| `/notch-config <mod> <sync\|warn\|off\|reset>` | Sets what happens to that mod's settings on players' games |
| `/notch-config-clear` | Empties the list of settings that had to be stopped |
| `/notch-notice` | Opens this server's [Network Access Notice](#the-network-access-notice) on your game, to answer it. Works until it is answered |
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
| **Server** | The policy and the other server-wide settings, then **Config sync and the NOMNOM catalogue**: the catalogue's status and size limit, the guard's two numbers, the list of stopped settings, and the per-mod table. |
| **Admins** | The admin list: remove someone, add an online player, or add a Steam ID by hand. |
| **Player bans** | The game's own ban list: unban, kick or ban an online player, or ban a Steam ID by hand. |

- On *Mods* and *Server* nothing changes until you press **Apply**. Switching tabs with unapplied changes asks
  first: apply, discard or stay.
- The Mods tab shows a notice when there are [leftover files](#leftover-files) or stopped settings. **Dismiss**
  hides it until the tab is opened again. **Don't show again** hides it, for you only, until something new of that
  kind happens.
- In the per-mod config sync table, a greyed row cannot be changed and says why. A mod with no NOMNOM entry is
  shown in red; that is information, not a fault.
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
| `notch-status`, `notch-rules`, `notch-required`, `notch-banned`, `notch-admins`, `notch-catalogue` | `/notch …` |
| `notch-config` | `/notch config`; with `<mod> <sync\|warn\|off\|reset>`, the same as `/notch-config` |
| `notch-config-clear [number]` | `/notch-config-clear`; with a number, removes that one entry |
| `notch-notice [accept\|decline\|unanswered]` | Shows the answer to the Network Access Notice, or sets it |
| `notch-rule <mod> <kick\|warn\|off\|reset>` | `/notch-rule` |
| `notch-require <mod> <on\|off\|reset>` | `/notch-require` |
| `notch-ban <mod or plugin ID> <ban\|allow>` | `/notch-ban`; also accepts a plugin ID directly, with no file needed |
| `notch-admin-add <steam id>`, `notch-admin-remove <steam id>` | `/notch-admin` |

## Configuration

`BepInEx/config/com.skyraze.notch.cfg`. The server's settings can also be changed in [the menu](#the-menu). An
edit made by hand while NOTCH is running is picked up at the next server start, mission load or join; no restart
is needed.

| Setting | Default | What it does |
|---|---|---|
| `General` / `Debug` | `false` | Verbose logging: every file scanned, with full hashes. Turn on when troubleshooting. Needs a restart. |
| `Server` / `Policy` | `RequireNotch` | `RequireNotch`, `WarnOnly`, `RequireMatch` or `Off`. See [the join check](#the-join-check). |
| `Server` / `ReplyTimeoutSeconds` | `3` | How long a joining player is held while NOTCH waits for their mod list (1–6). A player without NOTCH waits this long. |
| `Server` / `UnlistedClientMods` | `Report` | `Report` = a player's mod this server doesn't know counts as a difference. `Ignore` = only mods the server knows are checked. |
| `Server` / `AdminManagement` | `Auto` | `Auto` = on, unless a known admin mod is loaded. `On` = always on. `Off` = NOTCH leaves admins alone. |

| `Catalogue` / `SizeLimitMB` | `8` | The largest NOMNOM catalogue download NOTCH accepts, in megabytes (1–1024). A larger file is dropped while it downloads. |
| `ConfigSync` / `RevertCount` | `3` | Experimental. How many times a synced setting may be put back within `RevertSeconds` before the server stops it (1–20). |
| `ConfigSync` / `RevertSeconds` | `10` | Experimental. The number of seconds `RevertCount` is counted over (5–60). |
| `Network Access Notice` / `Answer` | `NotAnswered` | `Accepted`, `Declined` or `NotAnswered`. See [The Network Access Notice](#the-network-access-notice). |
| `Network Access Notice` / `AskInGame` | `true` | A game only. `false` stops the pop-up in the main menu; "Don't ask again" sets it. |

The same section holds `AgreementVersion` and `PreviousAgreementVersion`, which NOTCH keeps itself.

The `Server`, `Catalogue` and `ConfigSync` settings do nothing in a player's game, unless that game hosts.

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
| `BepInEx/config/NOTCH/config-sync.yaml` | server | Your per-mod config sync choices |
| `BepInEx/config/NOTCH/NETWORK-ACCESS-NOTICE.txt` | both | The notice, written out by NOTCH |
| `BepInEx/NOTCH/catalogue-memory.txt` | server | What the last accepted catalogue looked like: its size and its list of mod IDs. Safe to delete |
| `BepInEx/NOTCH/config-sync-stops.txt` | server | The list of settings that had to be stopped. Holds players' names and Steam IDs |
| `BepInEx/NOTCH/notices-seen.txt` | server | Which menu notices each admin chose not to see again. Safe to delete |

The files in `BepInEx/config/NOTCH/` are plain text and can be edited by hand. Edits apply from the next join.
"Server" here also means a game that hosts.

## Reporting a problem

[Open an issue](https://github.com/skyraze/NOTCH-ModFileCheck/issues/new/choose) on GitHub. Please include:

- what you did and what you expected;
- the NOTCH version, from the first NOTCH line in the log;
- from the player: `BepInEx/NOTCH/notch-checks.yaml` and `BepInEx/LogOutput.log`;
- from the server: `BepInEx/LogOutput.log`, ideally with `Debug = true`.

Be aware that the logs list the mods you have installed and the folders they are in.

## Limits

- **Not anti-cheat.** NOTCH catches outdated and mismatched mods. It will not stop someone determined to fake
  their list.
- **No mod is ever downloaded or run.** NOTCH never installs, loads or runs a mod because a server or a player
  named it. The only thing it downloads is NOMNOM's catalogue, a list, and only with consent.
- **Config sync is not enforcement either.** It keeps honest players on the same settings. See
  [Config sync](#config-sync) for what the guard does and does not do.
- **Settings are applied at join.** A mod that reads a setting only once at start-up will not follow the server.
- **A dropped or kicked player is given no reason by NOTCH.** The game's own kick carries no message; the reason is
  in the server's log and menu.
- **Player bans and kicks use the game's internal code**, not its official modding interface, so a game update
  could break that one tab of the menu.

## Planned

- **1.0.0** is this release once it has been proven: run on real servers, tested with several players and on a
  game that hosts. Fixes and small additions until then are 0.90.x and 0.91.
- **2.0** is planned to include a mod manager, so a player can get the exact mods and versions a server asks for.
  Nothing will ever be downloaded from a game server, only from an approved site.
- A way for a client mod to ask whether the host allows it, and switch itself off if not.

## License

MIT, see [LICENSE](LICENSE). Source is public and unobfuscated. Developed with AI assistance (Claude); every
change is reviewed by the maintainer.
