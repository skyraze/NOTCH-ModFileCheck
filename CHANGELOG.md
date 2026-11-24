# Changelog

## Unreleased (0.90.0, in development)

Not released. Config sync works for required mods, with per-mod choices in the menu and by command, and follows the
author's label. A synced setting changed mid-session is put back.

- **A synced setting stays synced.** If a synced setting is changed on a player's game while they are connected, NOTCH
  sets it back to the server's value. It reacts only when a change happens; nothing is polled. Settings that are not
  synced, or that the mod's author marked `NotchClient`, are the player's own and are left alone.
- **Guard against a mod fighting over a setting (experimental).** If the same setting has to be put back 3 times
  within 10 seconds, the server tells that player's game to stop putting it back and records it. The two numbers are
  server settings (`RevertCount`, `RevertSeconds`), also in the menu. A game that does not acknowledge the stop within
  30 seconds is dropped from the server. Each player's game also has a fixed limit of its own (20 in 10 seconds).
  The menu's Server tab and `/notch config` list what was stopped, for whom, and why. The list is kept across
  restarts until an admin removes the entries (in the menu, or `/notch-config-clear` for all of them).
- **Menu notices.** The Mods tab shows a notice when settings have been stopped, beside the one for leftover files.
  Each can be dismissed for now, or hidden with "Don't show again" until something new of that kind happens.
- The player's log and report list at most 200 settings per join, problems first, then "and N more".
- **Fixed:** a setting the player changed on a synced mod during a session (one that is not synced) was not written
  to their config file on leaving. It is now.

- **Author's label for config sync.** A mod maker can add `[assembly: AssemblyMetadata("NotchConfigSync", "...")]`:
  `Self` (the mod syncs itself; NOTCH leaves it alone), `Notch` (use NOTCH's sync, even if the mod is not required)
  or `Off` (never sync). `Self` and `Off` cannot be overridden by a server manager; a `Notch` mod can still be set to
  warn or off. A mod with no label that carries a known sync library (ServerSync, Jotunn, CSync) is left alone.

- **Per-mod config sync choice.** For each mod that has settings, a server manager can choose sync, warn or off, with
  `/notch-config` or in the menu's Server tab. Kept in `BepInEx/config/NOTCH/config-sync.yaml`. `/notch config` lists
  every mod with settings and what happens to it. Mods on the never-send list or marked client-only or server-only
  in NOMNOM cannot be changed.
- **Menu.** The Server tab has a new section with the catalogue's status, the size limit for a catalogue download, and
  the per-mod table (closed until opened). It also shows the catalogue's size, when NOMNOM last changed it, and the
  installed mods that have no catalogue entry. **Switching tabs with unapplied changes now asks first:** apply, discard or
  stay.

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
