# Quick Add (Fullscreen)

Playnite extension to add and organise games without leaving Fullscreen mode.

Menu (Back/View button) -> Extensions -> Quick Add -> "Add a game or app..."

Switch mode with left/right (D-pad, left stick or keyboard):

| Mode | A | X | Start | Y |
|---|---|---|---|---|
| ADD | open / add the highlighted file | mark file | add marked (asks each name) | places |
| MOVE | open | mark file or folder | move marked here (background, progress bar) | places |
| SCAN | open | mark folder | scan marked / this / saved folders | places |
| RENAME | open | mark | rename marked (or highlighted) on disk | places |

Select = options of the current mode. B = back (closes at the top). Scan results: A choose executables
(A = main, X = extra game actions), X select, Y rename, LT/RT select none/all, Start import.

## Build
Actions tab -> "Build and release" -> Run workflow -> type the version (for example `1.2.0`).
It builds, names the file `QuickAdd_<version>.pext` and creates a release named `<version>` (tag `v<version>`).
Keep `icon.png` in the repository root: it is copied next to the DLL and packed into the `.pext`.

## Credits
The idea of a controller-first file browser was inspired by [Kippix](https://github.com/Kipping0/Kippix) by Kipping0.
No Kippix code was copied. Not affiliated with Kippix or Playnite.

## AI disclosure
Written with AI assistance (Claude, by Anthropic). Provided as is.
