# Quick Add (Fullscreen) - alternative structure

Same features as the main version, built differently:

```
src/
  Plugin/    QuickAddPlugin (thin) + AddSession (what "Add a game or app" does)
  Services/  logic with no UI: JobService, MoveService, MetadataService, LibraryIndex, LibraryPathService,
             FileService, IconService, AppSettings, FolderDialog, Notifier
  UI/
    Input/       PickerCommand + InputRouter (pad / keyboard -> commands, repeat, duplicate filter, stick cooldown)
    ViewModels/  PickerViewModel (rows, tabs, prompts, job bars), RowViewModel, Hint/Tab/Job view models
    Pages/       one class per screen: Add, Move, Rename, FolderPick, Options
    Views/       PickerView.xaml (pure bindings, no code for layout)
    PickerService  opens the Playnite window and routes the controller
```

Adding a mode = one new `BrowsePage` subclass and one line in `PickerViewModel`.

Switch mode with left/right. Select = options. B = back. See the main README for the button table.

## Build
Actions tab -> "Build and release" -> Run workflow -> type the version (for example `1.2.0`).
It creates `QuickAdd_<version>.pext` and a release named `<version>` (tag `v<version>`).
Keep `icon.png` in the repository root.

## Credits
Inspired by [Kippix](https://github.com/Kipping0/Kippix) by Kipping0. No Kippix code was copied.

## AI disclosure
Written with AI assistance (Claude, by Anthropic). Provided as is.
