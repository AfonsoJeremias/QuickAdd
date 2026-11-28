# Quick Add (Fullscreen)

Playnite extension to add games and manage files **without leaving Fullscreen mode**, using only a controller.

Playnite menu (Back/View button) -> Extensions -> Quick Add -> "Add a game or app..."

## Controls

Navigate with the D-pad or the left stick (hold up/down to scroll fast). Switch mode with **left / right** (D-pad, left stick or keyboard). The current mode is highlighted at the top and the button prompts at the bottom change with it.

| Button | What it does |
|---|---|
| A | Open the folder / act on the highlighted item |
| B | Go back (closes the browser at the top level) |
| X | Mark / unmark the highlighted item (New folder in CREATE mode) |
| Y | Places and drives |
| Start | Confirm the action of the current mode |
| Select | Options of the current mode |
| LB / RB | Jump a page up / down |
| left / right | Switch mode |

| Mode | A | X | Start |
|---|---|---|---|
| **ADD** | open folder / add the highlighted file (plus the marked ones) | mark file | add the marked files |
| **MOVE** | open folder | mark file or folder | move the marked items into the folder you are in |
| **RENAME** | open folder | mark file or folder | rename the marked items (or the highlighted one) on disk |
| **CREATE** | open folder | new folder | new file |

Keyboard: arrows, Enter, Esc/Backspace, Space (mark / new folder), Y, F2 (options), Ctrl+Enter (Start), Tab (next mode).

## ADD
* Mark several games with **X** (even in different folders) and press **Start** to add them all at once.
* Works with `.exe`, `.lnk` (shortcuts are resolved), `.bat` and `.cmd`.
* An executable that is already in your library can't be added again: it is flagged *already in library* and refused with a warning.
* Optionally asks for each game's name, one after the other (Cancel skips that game).
* The game's icon is extracted automatically.
* The new games are selected in your library afterwards, so they are easy to find.

> **About metadata:** Quick Add **only adds the games** (name, install folder, launch action and icon). It does not download metadata.
> To get covers, descriptions, genres and so on, **update your Playnite library** (or use Playnite's own *Download metadata*):
> Playnite then applies your own metadata settings and sources.

## MOVE
* Lists **every kind of file**, and folders too.
* Mark items with **X**, open the destination folder and press **Start**.
* Runs in the background with a progress bar and a notification when it finishes. You can close the browser meanwhile; the bar is back when you reopen it. Several moves are queued.
* Same drive: instant. Other drive: copied with progress, and the original is deleted only after everything was copied.
* If the name already exists at the destination: skip it, or keep both (adds a number).
* Updates the paths of library games that pointed to what you moved (optional).

## RENAME
* Renames the **real files and folders on disk**, not just a name in Playnite.
* **Start** renames the marked items, or the highlighted one if nothing is marked. Names are typed with Playnite's on-screen keyboard (Cancel skips that item).
* Checks for invalid and duplicate names, keeps the file extension (optional) and updates library paths (optional).

## CREATE
* Creates **folders and empty files** inside the folder you are browsing: **X** = new folder, **Start** = new file.
* The name is typed with Playnite's on-screen keyboard, with a suggestion that is already free ("New folder", "New folder (2)", "New file.txt"...). Cancel does nothing.
* An existing name is never overwritten, and invalid names are refused.
* Right after creating a folder you go into it (optional); a new file is highlighted in the list.
* You must be inside a folder: at the "Places and drives" screen there is nowhere to create.

## Options (Select)
Each mode has its own list:

| Mode | Options |
|---|---|
| ADD | ask each name, select the new games |
| MOVE | name conflict policy (skip / keep both), update library paths |
| RENAME | keep file extension, update library paths |
| CREATE | suggested folder name, suggested file name, go into a new folder after creating it |
| All | sort by name / date / size, order, show hidden files, open in the last folder, reset all |

Options are saved in `settings.json` in the extension's data folder.

## Notes
* While Playnite's on-screen keyboard is open, the extension ignores the controller, so typing never moves its menu.
* **Desktop mode** falls back to standard Windows dialogs (Explorer-style folder picker, multi-select file picker). Move, Rename and Create are Fullscreen only.
* Requires a Playnite version with API 6.12 or newer.

### Known limitations
* **Library paths:** Move and Rename fix absolute paths and install folders. If you move only a subfolder inside a game's install folder, actions that are relative to it can still break.
* If Playnite is closed in the middle of a copy between drives, the originals are untouched, but a partial copy may remain at the destination.

## Build and release
Actions tab -> **Build and release** -> **Run workflow** -> type the version (for example `1.2.0`).
It builds the extension, names the file `QuickAdd_<version>.pext` and creates a release named `<version>` (tag `v<version>`) with that file attached.
Pushes and pull requests only run a compile check (version `0.0.0`) and don't create releases.

Keep `icon.png` in the repository root: it is copied next to the DLL and packed into the `.pext`.
Keep `installer.yaml` in the repository root, and add a new entry to it for each release (same version, same `.pext` name).

## Credits
The idea of a controller-first file browser was inspired by [Kippix](https://github.com/Kipping0/Kippix) by Kipping0.
No Kippix code was copied. Not affiliated with Kippix or Playnite.

## AI disclosure
Written with AI assistance (Claude, by Anthropic). Provided as is.
