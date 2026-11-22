# N.O.T.C.H code guide

What each part of the source does, for anyone reading or reviewing the code. The comments in the code are kept to one line; the longer explanations live here.

For using NOTCH, see the [README](../README.md). For labelling your own mod, see [FOR-MOD-DEVS.md](FOR-MOD-DEVS.md).

## How it fits together

1. **Start-up** (`Plugin.cs`): NOTCH scans the mods on this machine (`ModScanner.cs`) and builds the "known set".
2. **A player joins** (`Net/ServerJoin.cs`): after the game accepts the login and before it lets the player in, the server asks the client for its mod list and waits briefly.
3. **The client answers** (`Net/ClientJoin.cs`) with its own list. A client never sends anything unasked, so a server without NOTCH never hears from it.
4. **The server compares** (`ModCompare.cs`) and, depending on the Policy setting, admits the player with a notice or refuses with a reason.
5. **The player is told** (`PlayerNote.cs`, `Popup.cs`, `CheckReport.cs`): a notice in game, the text in the game's disconnect box, and a report file.

Classes described as "pure logic" or "pure text" touch no game, network or file code. That is deliberate: they are the ones covered by the unit tests in `tests/NOTCH.Tests`.

## Finding mods

**`ModScanner.cs`** reads files as data. DLLs are opened with Mono.Cecil, a library reader; everything else is only hashed. Nothing here loads or runs another mod's code.

- **Author labels** are read from the DLL: `[assembly: AssemblyMetadata("NotchScope", "RequiresAll")]` and `NotchCompatibleFrom`. They are a standard .NET attribute, so a mod carries them without depending on NOTCH.
- **Blueprinter content** is usually one plugin DLL with the `.nobp` bundle packed inside as a resource, with no loose `.nobp` file and no declared dependency on Blueprinter. NOTCH reads only the resource's name.

**`ModEntry.cs`** is one mod as the scanner found it. **`WireMod.cs`** is the smaller form sent over the network.

## The comparison

**`ModCompare.cs`** takes the server's known set and the client's list and returns a list of problems. Three layers decide the outcome for a mod:

1. **The mod's author:** who needs the mod (`ModEntry.Required`, through `RequiredRules`) and which older versions the server's copy accepts (`ModEntry.CompatibleFrom`).
2. **The server's policy:** what a mismatch costs when nothing else says (`Blocks`).
3. **The server manager, per mod:** `/notch-require`, `/notch-rule` and `/notch-ban`. These win.

Two rules worth knowing:

- **Older versions:** the server's copy decides which other versions are fine, and only downward. A copy labelled "compatible from 1.15" accepts 1.15 up to its own version. Nothing newer than the server's copy is accepted by a label, because that copy cannot know about versions made after it.
- **Required mods:** a mismatch rule of "off" stops the comparison for that mod, but not the need to have it.

`Problem.Describe()` gives the full wording for the notice and the report. `Problem.Short()` gives one short line for the disconnect box.

**`RuleOverrides.cs`** holds four small classes:

| Class | What it does |
|---|---|
| `RuleOverrides` | Text of `overrides.yaml`: what a mismatch on one mod costs. One line per mod: `"plugin.id": kick   # Mod Name` |
| `BannedList` | Text of `banned.yaml`: mods nobody may join with, in any version |
| `RequiredRules` | Decides which mods every player must have |
| `ModChoices` | Mod names as chat suggestions. A suggestion can't contain a space, so "Configuration Manager" becomes "Configuration_Manager"; two mods with the same name get _2, _3 |

`RequiredRules` decides per mod, first match wins:

1. The server manager's choice (`required.yaml`, set with `/notch-require`).
2. The mod author's label: `RequiresAll` means required; any other label means not required.
3. Blueprinter content, Blueprinter itself and plugins that depend on Blueprinter are required, because they almost always add or change vehicles.
4. Everything else is not required.

Steps 2 to 4 only apply to mods the server actually runs.

## What the player sees

**`PlayerNote.cs`** builds the text. The game's disconnect box is small: it does not wrap text (about 80 characters fit on a line), cannot scroll, and has room for four lines. A fifth line runs out of the box and over its OK button. So a refusal is the header plus at most three problems (`MaxListed`), most serious first. With more, the header says how many are shown, and the full list is in the report file.

**`Popup.cs`** draws the notice after a join where some mods don't match. It uses Unity's built-in immediate-mode UI, with no art assets and nothing taken from the game's own UI. It appears once the match screen exists, stays until Close is clicked, and goes away if the player leaves.

- **Holding input:** while the notice is open the game must not react to keys or the mouse wheel. NOTCH does what the game's chat box does while you type: it switches off the keyboard and mouse control maps, keeps the pointer visible and blocks the pause key. This runs every frame, because the chat box switches every control map back on a moment after it closes.
- **The click blocker:** the notice is drawn on top of the game's menus, but that alone does not stop a click from reaching the button underneath. An invisible full-screen sheet sits above the game's menus and swallows those clicks. It also dims the screen slightly, and is removed when the notice closes.

**`CheckReport.cs`** builds the text of `BepInEx/NOTCH/notch-checks.yaml`: the last 10 join checks, newest first. It is YAML so an editor colours and folds it and a script can read it, but it is meant to be read as text. Names and versions from outside are double-quoted, because an unquoted version "1.10" would be read as the number 1.1, and line breaks and control characters become a space so a strange name can't break the layout or fake an entry. `Plugin.cs` does the actual reading and writing.

## Network

**`Net/Messages.cs`** defines the messages: three for the join check and two for the admin menu.

- **The type names are part of the protocol.** Mirage turns the full name (for example `NOTCH.Net.NotchInterrogateV1`) into the 16-bit message id, so renaming a struct breaks compatibility. A changed layout gets a new struct (V2), never an edit to an existing one.
- **Registering** tells Mirage how to write and read the messages and checks that their ids don't clash with a game message. If they do, NOTCH stays passive.
- **Reading:** every string goes through `SafeText.Clean` and every list has a size limit. Nothing that is read is ever run, loaded or used as a file path.

**`SafeText`** (in `WireMod.cs`) cleans all text that arrives over the network. NOTCH only compares, shows and writes that text, but text can still be used to mislead, so on arrival:

- Control characters and line breaks become a space, so there are no fake lines in a log, a file or the notice.
- Characters that reorder text on screen are dropped, so a name can't be made to read backwards.
- Angle brackets become look-alikes, because the game's text boxes treat tags as formatting.
- Anything longer than `MaxLength` is cut.

**`Net/ServerJoin.cs`** is the server side of the join. The client is always told the result. Which problems refuse the join is decided by `ModCompare.Blocks` from the policy and the manager's per-mod choices. It has three patches:

1. **On server start** it listens for clients' answers. This uses `allowUnauthenticated`, because the player has not finished logging in when they answer. The game's password check does the same.
2. **Guard 1:** the game checks every Steam player once a second and assumes each has login data. A held player has none yet, so that tick is skipped while anyone is held and runs again a second later.
3. **Guard 2:** NOTCH remembers what the game's Steam player list held for the account before the game touches it, so it can be put back if the player ends up not getting in.

## Admins and commands

**`Admin/AdminList.cs`** reads and edits the text of `admins.txt`: one Steam ID per line, `#` starts a comment, blank lines ignored, the same shape as the game's ban list. A SteamID64 has fixed parts (which Steam, what kind of account, the account's own number), and a player's account always falls in one range. NOTCH checks the range rather than the leading digits, which keeps newer accounts valid as their IDs run past 7656119… into 7656120…. Game-server and group IDs fall outside it.

**`Admin/AdminManager.cs`** is basic admin management. The game has permission levels (Player, Moderator, Admin, Host) but no way to give one to a player, so NOTCH does the minimum: players listed in `admins.txt` get the game's Admin level when they join. It:

- only ever raises a level, so it can't fight another mod over who is admin;
- never touches the Host;
- steps aside entirely when a known admin mod is loaded (`AdminManagement = Auto`) or when set to Off;
- uses only the game's official ModdingApi.

**`Admin/KnownAdminMods.cs`** lists mods that manage admins themselves. To add one, put its BepInEx plugin ID on a new line with a comment naming the mod, then rebuild. The ID is the first text in the mod's `[BepInPlugin(...)]` line, and NOTCH prints it at start-up when Debug is on.

**`Commands.cs`** has two parts:

- **`RuleStore`** owns the three per-mod files (`overrides.yaml`, `required.yaml`, `banned.yaml`), kept next to `admins.txt`. All are re-read when they change on disk, so a hand edit applies from the next join.
- **`Commands`** writes each action once (the `Do…` methods) and offers it two ways: in the game's chat through the official ModdingApi, and on a dedicated server through the game's remote-command port. The full list of both is in the README.

Chat commands all need the Admin level, so the game hides them from everyone else. A chat command has one fixed list of arguments with fixed suggestions, which is why there are several commands (`/notch-rule`, `/notch-require`, `/notch-ban`) rather than sub-commands of `/notch`: that way the chat box can suggest everything.

`/notch-ban`'s suggestion list is rebuilt as mods are seen. It offers mods the server knows but does not run (the `Client-Mods-NoLoad` folder and client-only mods BepInEx skipped), mods seen on players that the server does not know, and mods already banned. A known cheat or broken mod can therefore be banned before anyone joins with it, by dropping its file in the folder. With nothing to offer, the command is not shown.

## The admin menu

**`Panel/PanelWindow.cs`** is the window an admin opens with `/notch-menu`, drawn with Unity's immediate-mode UI like the join notice. It has four tabs: Mods, Server, Admins and Player bans. On Mods and Server nothing changes until Apply. On Admins and Player bans each button acts at once, after a second click to confirm. The column titles sit in their own strip that follows the list's sideways scroll, so they stay over their columns.

**`Panel/PanelServer.cs`** builds what the menu shows and carries out the changes it sends back. The window only asks. Every request is checked again on the server (the sender must have the game's Admin level or be the host), because a message can be sent by any client, whatever its own menu shows. Mods, Server and Admins use NOTCH's own files and settings. Player bans use the game's own ban list and kick code, so a ban made there is an ordinary game ban that works without NOTCH.

**`Panel/PanelData.cs`** is what travels between the two. Its `Result` method produces the "what happens to a player now" text for a row, and is built on `ModCompare.Blocks`, so the menu can never say something different from what the join check does.
