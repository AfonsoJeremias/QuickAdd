using QuickAdd.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickAdd.UI
{
    /// <summary>Folder browsing shared by every mode. A mode overrides only what differs.</summary>
    public abstract class BrowsePage : PickerPage
    {
        const int MaxFolders = 1000;

        protected readonly List<string> Marks = new List<string>();

        public abstract ModeKind Mode { get; }

        protected static AppSettings S { get { return AppSettings.Current; } }

        protected virtual bool ShowFiles { get { return true; } }
        protected virtual bool FoldersMarkable { get { return false; } }
        protected virtual bool FilesMarkable { get { return true; } }
        protected virtual int MaxFiles { get { return 3000; } }
        protected virtual string MarkWord { get { return "marked"; } }
        protected virtual bool SkipUpOnOpen { get { return true; } }
        protected virtual bool FileAllowed(FileInfo file) { return true; }
        protected virtual void AddLeadingRows(string path) { }
        protected virtual void OnAction(RowViewModel row) { }
        protected virtual void Confirm(RowViewModel row) { }

        public override string Subtitle { get { return Vm.CurrentPath ?? "This PC"; } }

        public override string Title(string baseTitle)
        {
            return Marks.Count > 0 ? baseTitle + "   ·   " + Marks.Count + " " + MarkWord : baseTitle;
        }

        public override void Enter(object focus) { Navigate(Vm.CurrentPath, focus as string); }

        // ---------- commands ----------

        public override void Handle(PickerCommand command, RowViewModel row)
        {
            switch (command)
            {
                case PickerCommand.Accept: if (row != null && row.IsSelectable) Accept(row); break;
                case PickerCommand.Back: GoBack(); break;
                case PickerCommand.Mark: if (row != null) Mark(row); break;
                case PickerCommand.Alt: Navigate(null, Vm.CurrentPath); break;
                case PickerCommand.Confirm: Confirm(row); break;
                case PickerCommand.Options:
                    if (Vm.Options.AllowMultiple) Vm.Show(new OptionsPage(Mode, this), null);
                    break;
            }
        }

        protected virtual void Accept(RowViewModel row)
        {
            switch (row.Kind)
            {
                case RowKind.Up: GoBack(); break;
                case RowKind.Action: OnAction(row); break;
                case RowKind.Place:
                case RowKind.Drive:
                case RowKind.Folder:
                case RowKind.Saved: Navigate(row.Path, null); break;
            }
        }

        protected virtual void Mark(RowViewModel row)
        {
            bool ok = ((row.Kind == RowKind.Folder || row.Kind == RowKind.Saved) && FoldersMarkable) ||
                      (row.Kind == RowKind.File && FilesMarkable);
            if (ok) ToggleMark(row);
        }

        protected void ToggleMark(RowViewModel row)
        {
            int i = Marks.FindIndex(p => string.Equals(p, row.Path, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { Marks.RemoveAt(i); row.Marked = false; }
            else { Marks.Add(row.Path); row.Marked = true; }
            Vm.RefreshChrome();
        }

        protected void ClearMarks()
        {
            Marks.Clear();
            foreach (var r in Vm.Rows) r.Marked = false;
            Vm.RefreshChrome();
        }

        protected bool IsMarked(string path) { return Marks.Contains(path, StringComparer.OrdinalIgnoreCase); }

        protected void GoBack()
        {
            if (Vm.CurrentPath == null) { Vm.Finish(null); return; }
            string parent = null;
            try { parent = Directory.GetParent(Vm.CurrentPath)?.FullName; } catch { }
            Navigate(parent, Vm.CurrentPath);
        }

        // ---------- listing ----------

        protected void Navigate(string path, string focusPath)
        {
            Vm.Rows.Clear();
            try { if (path == null) FillPlaces(); else FillFolder(path); }
            catch (Exception ex) { Vm.Rows.Add(Row(RowKind.Up, "\uE74A", "Can't open this folder", ex.Message)); }

            Vm.CurrentPath = path;
            if (path != null) S.LastFolder = path;

            var rows = Vm.Rows.ToList();
            int idx = focusPath == null ? -1 : rows.FindIndex(r => string.Equals(r.Path, focusPath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = rows.FindIndex(r => r.IsSelectable && r.Kind != RowKind.Action && (!SkipUpOnOpen || r.Kind != RowKind.Up));
            if (idx < 0) idx = rows.FindIndex(r => r.IsSelectable);
            if (idx >= 0) Vm.Select(idx);
            Vm.RefreshChrome();
        }

        protected virtual void FillPlaces()
        {
            Vm.Rows.Add(Row(RowKind.Header, null, "PLACES"));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var places = new[]
            {
                Tuple.Create("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                Tuple.Create("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                Tuple.Create("Downloads", Path.Combine(profile, "Downloads")),
                Tuple.Create("Start menu", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")),
                Tuple.Create("Start menu (all users)", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"))
            };
            foreach (var p in places)
            {
                if (string.IsNullOrEmpty(p.Item2) || !Directory.Exists(p.Item2)) continue;
                Vm.Rows.Add(Row(RowKind.Place, "\uE80F", p.Item1, p.Item2, p.Item2));
            }

            Vm.Rows.Add(Row(RowKind.Header, null, "DRIVES"));
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    var root = d.RootDirectory.FullName;
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? root : d.VolumeLabel + " (" + root.TrimEnd('\\') + ")";
                    Vm.Rows.Add(Row(RowKind.Drive, "\uEDA2", label, FormatSize(d.AvailableFreeSpace) + " free", root));
                }
                catch { }
            }
        }

        protected virtual RowViewModel CreateFileRow(FileInfo f)
        {
            var row = Row(RowKind.File, "\uE7FC", f.Name,
                f.LastWriteTime.ToString("d MMM yyyy, HH:mm") + "  ·  " + FormatSize(f.Length), f.FullName);
            row.Marked = FilesMarkable && IsMarked(f.FullName);
            return row;
        }

        protected virtual void FillFolder(string path)
        {
            AddLeadingRows(path);

            string parent = null;
            try { parent = Directory.GetParent(path)?.FullName; } catch { }
            Vm.Rows.Add(Row(RowKind.Up, "\uE74A", "Up a folder", parent ?? "Places and drives", parent));

            bool hidden = S.ShowHidden;
            var dir = new DirectoryInfo(path);
            var dirs = SortDirs(dir.GetDirectories().Where(d => hidden || (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).ToList())
                .Take(MaxFolders).ToList();

            if (ShowFiles && dirs.Count > 0) Vm.Rows.Add(Row(RowKind.Header, null, "FOLDERS"));
            foreach (var d in dirs)
            {
                var row = Row(RowKind.Folder, "\uE8B7", d.Name, null, d.FullName);
                row.Marked = FoldersMarkable && IsMarked(d.FullName);
                Vm.Rows.Add(row);
            }
            if (!ShowFiles) return;

            var files = SortFiles(dir.GetFiles()
                    .Where(f => (hidden || (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) && FileAllowed(f)).ToList())
                .Take(MaxFiles).ToList();
            if (files.Count > 0) Vm.Rows.Add(Row(RowKind.Header, null, "FILES"));
            foreach (var f in files) Vm.Rows.Add(CreateFileRow(f));
        }

        static IEnumerable<DirectoryInfo> SortDirs(List<DirectoryInfo> dirs)
        {
            IEnumerable<DirectoryInfo> sorted = S.SortBy == 1
                ? dirs.OrderBy(d => d.LastWriteTimeUtc)
                : dirs.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase); // folders have no size: name
            return S.SortDescending ? sorted.Reverse() : sorted;
        }

        static IEnumerable<FileInfo> SortFiles(List<FileInfo> files)
        {
            IEnumerable<FileInfo> sorted;
            switch (S.SortBy)
            {
                case 1: sorted = files.OrderBy(f => f.LastWriteTimeUtc); break;
                case 2: sorted = files.OrderBy(f => f.Length); break;
                default: sorted = files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase); break;
            }
            return S.SortDescending ? sorted.Reverse() : sorted;
        }
    }
}
