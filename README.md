# Quick Add (Fullscreen)

Playnite extension to add and organise games without leaving Fullscreen mode.

Menu (Back/View button) -> Extensions -> Quick Add -> "Add a game or app..."

Switch mode with left/right (D-pad, left stick or keyboard):

| Mode | A | X | Start | Y |
|---|---|---|---|---|
| ADD | open / add the highlighted file | mark file | add marked (asks each name) | places |
| MOVE | open | mark file or folder | move marked here (background, progress bar) | places |
| RENAME | open | mark | rename marked (or highlighted) on disk | places |

Select = options of the current mode. B = back (closes at the top). While Playnite's on-screen keyboard is open
the extension ignores the pad, so typing never moves its menu.

Metadata: after adding games the extension asks the installed metadata plugins (IGDB first) for everything that is
missing. The Playnite SDK gives extensions no way to start Playnite's own metadata download, so the new games are
also selected in the library: run Playnite's "Download metadata" on them to apply your full metadata settings.

## Build
Actions tab -> "Build and release" -> Run workflow -> type the version (for example `1.2.0`).
It builds, names the file `QuickAdd_<version>.pext` and creates a release named `<version>` (tag `v<version>`).
Keep `icon.png` in the repository root: it is copied next to the DLL and packed into the `.pext`.

## Credits
The idea of a controller-first file browser was inspired by [Kippix](https://github.com/Kipping0/Kippix) by Kipping0.
No Kippix code was copied. Not affiliated with Kippix or Playnite.

## AI disclosure
Written with AI assistance (Claude, by Anthropic). Provided as is.
