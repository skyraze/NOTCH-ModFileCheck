# Changelog

## Unreleased (0.13.0, in development)

Not released. Config sync works for required mods; the menu section, per-mod choices and author labels are still to come.

- **Config sync.** A server makes the settings of its required mods hold on every player's game while they are
  connected. The values are applied in memory when the player joins, before the map loads, and the player's own
  values come back on leaving; nothing is written to the player's config files. The player is told in the game's
  message window and in the report file, setting by setting. With Policy `WarnOnly` the differences are listed and
  nothing is changed. Never sent: NOTCH's own settings, Configuration Manager's, Blueprinter's own, any mod NOMNOM
  marks client-only or server-only, and any setting its author marked `NotchClient`.
- **Protection against a hostile server.** A value is applied only to a setting the player's own copy of the mod
  has, of the same kind. A text setting only takes plain characters (letters, digits, spaces, hyphens, underscores,
  commas; 64 at most), so it cannot be given a link or a file path.

- **NOMNOM catalogue check.** A dedicated server, or a game while it hosts for other players, can read NOMNOM's
  public mod catalogue to learn whether a mod is meant for clients, servers or both. It runs in the background, at
  server start and then at intervals of no less than about an hour, and never while a player is only playing.
  Downloads are checked for size and structure before they are used. Status: `/notch catalogue`.
- **Network Access Notice and Consent.** Nothing is contacted until the notice is accepted on that machine. A
  game shows it once in the main menu (Accept, Decline, or Later); a dedicated server prints a reminder in its
  console at every start until it is answered, in the config file or once in game with `/notch-notice`. The
  notice is also written to `BepInEx/config/NOTCH/NETWORK-ACCESS-NOTICE.txt`. If the notice ever changes, NOTCH
  asks again and can show what changed.
- **Leftover copies of a mod.** When an old file is left beside the new one after an update, NOTCH no longer
  counts the copy BepInEx skipped, and warns about it in the server log and at the top of the menu's Mods tab.
- The join notice no longer ends with "You have joined — this is only a notice."
- A mod unpacked into nested folders is now matched to its catalogue entry by any of those folders.

## 0.12.0

First release marked stable. Still a beta: it has not been tested with several players at once.

- **Refusal message rebuilt for the game's disconnect box.** The box holds four lines and does not wrap or scroll,
  so a refusal is now one count line plus the three most serious problems, in short form
  (`Missing Blueprinter v2.1.1`). Order: banned mod, required mod missing, wrong version, different file, unknown
  mod. The full list is always in the player's report file.
- The message for a player without NOTCH is now two short lines.
- The server log no longer prints a mod's ID twice when it is banned by ID.
- README: new section on using NOTCH alongside NOMM and NOSMR.

A server and its players need the same `NOTCH.dll`, so update both together.

## 0.11.3

First public beta.

- Join check with four policies: `RequireNotch` (default), `WarnOnly`, `RequireMatch`, `Off`
- A notice for players listing each mismatch and how to fix it, plus a report file
- Per-mod rules (kick / warn / off), required mods, and a mod ban list
- Blueprinter content packs are recognised and required by default
- A basic admin list, chat commands, remote commands, and an in-game menu (`/notch-menu`)
- Labels for mod authors, with no dependency on NOTCH
