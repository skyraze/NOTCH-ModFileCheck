# Changelog

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
