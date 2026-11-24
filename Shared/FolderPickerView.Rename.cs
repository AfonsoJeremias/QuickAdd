using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // Rename mode: renames the real files/folders on disk (never just the library name).
        // Start renames the marked items, or the highlighted one when nothing is marked.
        void StartRename()
        {
            List<string> targets = markedRename.ToList();
            var sel = List.SelectedItem as PickItem;
            string focus = sel == null ? null : sel.Path;
            if (targets.Count == 0 && sel != null && (sel.Kind == PickKind.Folder || sel.Kind == PickKind.File))
                targets.Add(sel.Path);
            if (targets.Count == 0) { ShowToast("Highlight or mark a file or folder first."); return; }
            if (opt.PromptText == null) { ShowToast("Text input isn't available here."); return; }

            // deepest first: renaming a parent folder must not invalidate the paths of items inside it
            targets = targets.OrderByDescending(p => p.Count(ch => ch == '\\')).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

            var moves = new List<KeyValuePair<string, string>>();
            int renamed = 0, failed = 0;
            string lastError = null;

            for (int i = 0; i < targets.Count; i++)
            {
                var old = targets[i];
                bool isDir = Directory.Exists(old);
                if (!isDir && !File.Exists(old)) continue;

                var fullName = Path.GetFileName(old.TrimEnd('\\'));
                var ext = (!isDir && Settings.KeepExtension) ? Path.GetExtension(fullName) : "";
                var baseName = ext.Length > 0 ? fullName.Substring(0, fullName.Length - ext.Length) : fullName;

                var message = (targets.Count > 1 ? "New name (" + (i + 1) + " of " + targets.Count + ") for:\n" : "New name for:\n") + fullName;
                var entered = opt.PromptText(message, "Rename", baseName);
                if (string.IsNullOrWhiteSpace(entered)) continue; // Cancel skips this item

                string newPath, error;
                if (FileOps.TryRename(old, entered.Trim() + ext, out newPath, out error))
                {
                    renamed++;
                    moves.Add(new KeyValuePair<string, string>(old, newPath));
                    if (focus != null && string.Equals(focus, old, StringComparison.OrdinalIgnoreCase)) focus = newPath;
                }
                else { failed++; lastError = error; }
            }

            markedRename.Clear();

            int lib = 0;
            if (moves.Count > 0 && Settings.UpdateLibraryPaths && opt.UpdateLibrary != null)
            {
                try { lib = opt.UpdateLibrary(moves); } catch { }
            }

            var summary = "Renamed " + renamed + " item(s)";
            if (failed > 0) summary += ", " + failed + " failed (" + lastError + ")";
            if (lib > 0) summary += " - " + lib + " library game(s) updated";
            if (renamed > 0 || failed > 0)
            {
                ShowToast(summary);
                if (opt.Notify != null) opt.Notify("Quick Add: " + summary + ".");
            }
            Navigate(current, focus);
        }
    }
}
