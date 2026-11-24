using QuickAdd.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickAdd.UI
{
    // ============================ pick a single folder (PickFolder) ============================
    public sealed class FolderPickPage : BrowsePage
    {
        public override ModeKind Mode { get { return ModeKind.Add; } }
        protected override bool ShowFiles { get { return false; } }
        protected override bool SkipUpOnOpen { get { return false; } }

        protected override void AddLeadingRows(string path)
        {
            Vm.Rows.Add(Row(RowKind.Use, "\uE73E", "Use this folder", path, path));
        }

        protected override void Accept(RowViewModel row)
        {
            if (row.Kind == RowKind.Use) Vm.Finish(new List<string> { row.Path }); else base.Accept(row);
        }

        protected override void Mark(RowViewModel row) { UseCurrent(); }
        protected override void Confirm(RowViewModel row) { UseCurrent(); }

        void UseCurrent() { if (Vm.CurrentPath != null) Vm.Finish(new List<string> { Vm.CurrentPath }); }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Open"); yield return H("X", "Use this folder");
            yield return H("Y", "Places / drives"); yield return H("B", "Back"); yield return H("Start", "Use this folder");
        }
    }

    // ============================ ADD ============================
    public sealed class AddPage : BrowsePage
    {
        const string InLibraryMessage = "This executable is already in your library - it can't be added again.";

        public override ModeKind Mode { get { return ModeKind.Add; } }
        bool Multi { get { return Vm.Options.AllowMultiple; } }
        protected override bool FilesMarkable { get { return Multi; } }
        protected override int MaxFiles { get { return 500; } }

        protected override bool FileAllowed(FileInfo f)
        {
            var exts = Vm.Options.Extensions;
            return exts == null || exts.Length == 0 || exts.Contains(f.Extension.ToLowerInvariant());
        }

        protected override RowViewModel CreateFileRow(FileInfo f)
        {
            var row = base.CreateFileRow(f);
            if (Vm.Options.IsInLibrary != null && Vm.Options.IsInLibrary(f.FullName))
            {
                row.Glyph = "\uE7BA"; row.Dim = true; row.Tag = "inlib"; row.Marked = false;
                row.Sub += "  ·  already in library";
            }
            return row;
        }

        protected override void Accept(RowViewModel row)
        {
            if (row.Kind != RowKind.File) { base.Accept(row); return; }
            if (row.Tag == "inlib") { Vm.Toast(InLibraryMessage); return; }
            var result = new List<string>(Marks);
            if (!result.Contains(row.Path, StringComparer.OrdinalIgnoreCase)) result.Add(row.Path);
            Vm.Finish(result);
        }

        protected override void Mark(RowViewModel row)
        {
            if (row.Kind == RowKind.File && row.Tag == "inlib") { Vm.Toast(InLibraryMessage); return; }
            base.Mark(row);
        }

        protected override void Confirm(RowViewModel row)
        {
            if (!Multi) return;
            if (Marks.Count > 0) { Vm.Finish(new List<string>(Marks)); return; }
            if (row == null || row.Kind != RowKind.File) return;
            if (row.Tag == "inlib") { Vm.Toast(InLibraryMessage); return; }
            Vm.Finish(new List<string> { row.Path });
        }

        public override IEnumerable<HintViewModel> Hints()
        {
            if (!Multi)
            {
                yield return H("A", "Open / select"); yield return H("Y", "Places / drives"); yield return H("B", "Back");
                yield break;
            }
            yield return H("A", "Open / add"); yield return H("X", "Mark"); yield return H("Y", "Places / drives"); yield return H("B", "Back");
            yield return H("Start", "Add marked"); yield return H("Select", "Options");
        }
    }

    // ============================ MOVE ============================
    public sealed class MovePage : BrowsePage
    {
        public override ModeKind Mode { get { return ModeKind.Move; } }
        protected override bool FoldersMarkable { get { return true; } }
        protected override string MarkWord { get { return "to move"; } }

        protected override void Accept(RowViewModel row)
        {
            if (row.Kind != RowKind.File) base.Accept(row); // files are only marked (X)
        }

        protected override void Confirm(RowViewModel row)
        {
            if (Vm.CurrentPath == null) { Vm.Toast("Open the destination folder first, then press Start."); return; }
            if (Marks.Count == 0) { Vm.Toast("Mark files or folders with X first."); return; }

            var dest = Vm.CurrentPath.TrimEnd('\\');
            foreach (var src in Marks)
            {
                var s = src.TrimEnd('\\');
                if (Directory.Exists(s) && (string.Equals(dest, s, StringComparison.OrdinalIgnoreCase) ||
                                            dest.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    Vm.Toast("A folder can't be moved into itself.");
                    return;
                }
            }
            MoveService.Enqueue(Marks.ToList(), Vm.CurrentPath);
            ClearMarks();
        }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Open"); yield return H("X", "Mark to move"); yield return H("Y", "Places / drives"); yield return H("B", "Back");
            yield return H("Start", "Move here"); yield return H("Select", "Options");
        }
    }

    // ============================ RENAME ============================
    public sealed class RenamePage : BrowsePage
    {
        public override ModeKind Mode { get { return ModeKind.Rename; } }
        protected override bool FoldersMarkable { get { return true; } }
        protected override string MarkWord { get { return "to rename"; } }

        protected override void Accept(RowViewModel row)
        {
            if (row.Kind != RowKind.File) base.Accept(row);
        }

        // renames the real files / folders on disk: the marked ones, or the highlighted one when nothing is marked
        protected override void Confirm(RowViewModel row)
        {
            var targets = Marks.ToList();
            string focus = row == null ? null : row.Path;
            if (targets.Count == 0 && row != null && (row.Kind == RowKind.Folder || row.Kind == RowKind.File)) targets.Add(row.Path);
            if (targets.Count == 0) { Vm.Toast("Highlight or mark a file or folder first."); return; }
            if (Vm.Options.PromptText == null) { Vm.Toast("Text input isn't available here."); return; }

            // deepest first: renaming a parent folder must not invalidate the paths inside it
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
                var ext = (!isDir && S.KeepExtension) ? Path.GetExtension(fullName) : "";
                var baseName = ext.Length > 0 ? fullName.Substring(0, fullName.Length - ext.Length) : fullName;

                var message = (targets.Count > 1 ? "New name (" + (i + 1) + " of " + targets.Count + ") for:\n" : "New name for:\n") + fullName;
                var entered = Vm.Options.PromptText(message, "Rename", baseName);
                if (string.IsNullOrWhiteSpace(entered)) continue; // Cancel skips this item

                string newPath, error;
                if (FileService.TryRename(old, entered.Trim() + ext, out newPath, out error))
                {
                    renamed++;
                    moves.Add(new KeyValuePair<string, string>(old, newPath));
                    if (focus != null && string.Equals(focus, old, StringComparison.OrdinalIgnoreCase)) focus = newPath;
                }
                else { failed++; lastError = error; }
            }

            Marks.Clear();
            int lib = 0;
            if (moves.Count > 0 && S.UpdateLibraryPaths && Vm.Options.UpdateLibrary != null)
            {
                try { lib = Vm.Options.UpdateLibrary(moves); } catch { }
            }

            var summary = "Renamed " + renamed + " item(s)";
            if (failed > 0) summary += ", " + failed + " failed (" + lastError + ")";
            if (lib > 0) summary += " - " + lib + " library game(s) updated";

            Navigate(Vm.CurrentPath, focus);
            if (renamed > 0 || failed > 0)
            {
                Vm.Toast(summary);
                if (Vm.Options.Notify != null) Vm.Options.Notify("Quick Add: " + summary + ".");
            }
        }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Open"); yield return H("X", "Mark"); yield return H("Y", "Places / drives"); yield return H("B", "Back");
            yield return H("Start", Marks.Count > 0 ? "Rename marked" : "Rename highlighted"); yield return H("Select", "Options");
        }
    }

    // ============================ SCAN ============================
    public sealed class ScanPage : BrowsePage
    {
        public override ModeKind Mode { get { return ModeKind.Scan; } }
        protected override bool ShowFiles { get { return false; } }
        protected override bool FoldersMarkable { get { return true; } }
        protected override bool FilesMarkable { get { return false; } }
        protected override string MarkWord { get { return "to scan"; } }

        static List<string> SavedRoots() { return (S.SavedRoots ?? new List<string>()).Where(Directory.Exists).ToList(); }

        protected override void FillPlaces()
        {
            var results = ScanService.GetResults();
            var saved = SavedRoots();
            if (results.Count > 0 || saved.Count > 0) Vm.Rows.Add(Row(RowKind.Header, null, "SCAN"));
            if (results.Count > 0)
            {
                var r = Row(RowKind.Action, "\uE8FD", "Review scan results", results.Count + " games waiting");
                r.Tag = "results"; Vm.Rows.Add(r);
            }
            if (saved.Count > 0)
            {
                var all = Row(RowKind.Action, "\uE72C", "Scan all saved folders", saved.Count + " folder(s)");
                all.Tag = "scanall"; Vm.Rows.Add(all);
                Vm.Rows.Add(Row(RowKind.Header, null, "SAVED FOLDERS"));
                foreach (var root in saved)
                {
                    var name = Path.GetFileName(root.TrimEnd('\\'));
                    var row = Row(RowKind.Saved, "\uE8B7", string.IsNullOrEmpty(name) ? root : name, root, root);
                    row.Marked = IsMarked(root);
                    Vm.Rows.Add(row);
                }
            }
            base.FillPlaces();
        }

        protected override void AddLeadingRows(string path)
        {
            var r = Row(RowKind.Action, "\uE721", "Scan this folder", path);
            r.Tag = "scanhere"; Vm.Rows.Add(r);
        }

        protected override void OnAction(RowViewModel row)
        {
            switch (row.Tag)
            {
                case "scanhere": if (Vm.CurrentPath != null) StartScan(new List<string> { Vm.CurrentPath }); break;
                case "scanall": StartScan(SavedRoots()); break;
                case "results": Vm.Show(new ResultsPage(), null); break;
            }
        }

        protected override void Confirm(RowViewModel row)
        {
            if (Marks.Count > 0) StartScan(Marks.ToList());
            else if (Vm.CurrentPath != null) StartScan(new List<string> { Vm.CurrentPath });
            else StartScan(SavedRoots());
        }

        void StartScan(IList<string> roots)
        {
            if (roots == null || roots.Count == 0) { Vm.Toast("Open a folder first, or save one by scanning it once."); return; }
            if (!ScanService.StartScan(roots, S)) { Vm.Toast("A scan, import or metadata download is already running."); return; }

            bool changed = false;
            foreach (var r in roots)
                if (!S.SavedRoots.Contains(r, StringComparer.OrdinalIgnoreCase)) { S.SavedRoots.Add(r); changed = true; }
            if (changed) S.Save();
            ClearMarks();
        }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Open"); yield return H("X", "Mark folder"); yield return H("Y", "Places / drives"); yield return H("B", "Back");
            yield return H("Start", Marks.Count > 0 ? "Scan marked folders" : Vm.CurrentPath == null ? "Scan saved folders" : "Scan this folder");
            yield return H("Select", "Options");
        }
    }
}
