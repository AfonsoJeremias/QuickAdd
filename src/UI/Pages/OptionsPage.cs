using QuickAdd.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickAdd.UI
{
    /// <summary>The Select button: a different list of options for every mode.</summary>
    public sealed class OptionsPage : PickerPage
    {
        sealed class Def
        {
            public string Label;
            public Func<string> Value;
            public Action Change;
        }

        readonly ModeKind mode;
        readonly PickerPage returnTo;

        public OptionsPage(ModeKind mode, PickerPage returnTo) { this.mode = mode; this.returnTo = returnTo; }

        static AppSettings S { get { return AppSettings.Current; } }
        static string OnOff(bool v) { return v ? "On" : "Off"; }

        public override bool AllowModeSwitch { get { return false; } }
        public override string Subtitle { get { return "Options  ·  " + mode + " mode"; } }
        public override string Title(string baseTitle) { return baseTitle + "   ·   Options"; }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Change"); yield return H("B", "Close"); yield return H("Select", "Close");
        }

        public override void Enter(object focus)
        {
            foreach (var d in Build())
                Vm.Rows.Add(new RowViewModel { Kind = RowKind.Option, Glyph = "\uE713", Text = d.Label, Sub = d.Value(), Data = d });
            if (Vm.Rows.Count > 0) Vm.Select(focus is int ? Math.Max(0, Math.Min(Vm.Rows.Count - 1, (int)focus)) : 0);
        }

        public override void Handle(PickerCommand command, RowViewModel row)
        {
            switch (command)
            {
                case PickerCommand.Accept:
                    var def = row == null ? null : row.Data as Def;
                    if (def == null) return;
                    int idx = Vm.SelectedIndex;
                    def.Change();
                    S.Save();
                    Vm.Show(this, idx);
                    break;
                case PickerCommand.Back:
                case PickerCommand.Options:
                    Vm.Show(returnTo, null);
                    break;
            }
        }

        List<Def> Build()
        {
            var list = new List<Def>();
            Action<string, Func<string>, Action> add = (l, v, c) => list.Add(new Def { Label = l, Value = v, Change = c });

            switch (mode)
            {
                case ModeKind.Scan:
                    if (ScanService.Snapshot().Running)
                        add("Cancel the running scan / import / metadata", () => "A to cancel", () => { ScanService.Cancel(); Vm.Toast("Cancelling…"); });
                    add("Folder layout", () => S.FolderPerGame ? "One game per subfolder (collections are split)" : "Folder with the executable",
                        () => S.FolderPerGame = !S.FolderPerGame);
                    add("Search depth", () => S.Depth.ToString(), () => S.Depth = S.Depth % 4 + 1);
                    add("Game name from", () => S.NameFromFolder ? "Folder name" : "File name", () => S.NameFromFolder = !S.NameFromFolder);
                    add("Include files (wildcards, ; separated)", () => S.Include, () => Edit("Files to include, e.g. *.exe;*.bat", S.Include, v => S.Include = v));
                    add("Exclude files (wildcards, ; separated)", () => S.Exclude, () => Edit("Files to skip, e.g. unins*;*setup*", S.Exclude, v => S.Exclude = v));
                    add("Hide games already in the library", () => OnOff(S.HideExisting), () => S.HideExisting = !S.HideExisting);
                    add("Install paths", () => S.RelativePaths ? "Relative to the Playnite folder" : "Absolute", () => S.RelativePaths = !S.RelativePaths);
                    add("Ask each game's name before importing", () => OnOff(S.AskNamesScan), () => S.AskNamesScan = !S.AskNamesScan);
                    add("Download metadata after importing", () => OnOff(S.DownloadMetadata), () => S.DownloadMetadata = !S.DownloadMetadata);
                    list.Add(MetadataSource());
                    add("Saved scan folders", () => S.SavedRoots.Count + " saved - A clears them", () => S.SavedRoots.Clear());
                    break;
                case ModeKind.Add:
                    add("Ask each game's name when adding", () => OnOff(S.AskNamesAdd), () => S.AskNamesAdd = !S.AskNamesAdd);
                    add("Download metadata after adding", () => OnOff(S.DownloadMetadata), () => S.DownloadMetadata = !S.DownloadMetadata);
                    list.Add(MetadataSource());
                    break;
                case ModeKind.Move:
                    add("When the name already exists", () => S.MoveConflict == 1 ? "Keep both (add a number)" : "Skip it", () => S.MoveConflict = S.MoveConflict == 1 ? 0 : 1);
                    add("Update library paths after moving", () => OnOff(S.UpdateLibraryPaths), () => S.UpdateLibraryPaths = !S.UpdateLibraryPaths);
                    break;
                default:
                    add("Keep the file extension when renaming", () => OnOff(S.KeepExtension), () => S.KeepExtension = !S.KeepExtension);
                    add("Update library paths after renaming", () => OnOff(S.UpdateLibraryPaths), () => S.UpdateLibraryPaths = !S.UpdateLibraryPaths);
                    break;
            }

            if (mode != ModeKind.Scan)
            {
                add("Sort by", () => S.SortBy == 1 ? "Date modified" : S.SortBy == 2 ? "Size (files)" : "Name", () => S.SortBy = (S.SortBy + 1) % 3);
                add("Order", () => S.SortDescending ? "Descending" : "Ascending", () => S.SortDescending = !S.SortDescending);
                add("Show hidden and system files", () => OnOff(S.ShowHidden), () => S.ShowHidden = !S.ShowHidden);
            }
            add("Open in the last folder", () => OnOff(S.RememberLastFolder), () => S.RememberLastFolder = !S.RememberLastFolder);
            add("Reset all options", () => "A to reset", () => S.ResetOptions());
            return list;
        }

        Def MetadataSource()
        {
            return new Def
            {
                Label = "Metadata source",
                Value = () => string.IsNullOrEmpty(S.MetadataSource) ? "All installed (IGDB first)" : S.MetadataSource,
                Change = () =>
                {
                    var all = new List<string> { "" };
                    if (Vm.Options.MetadataSources != null) all.AddRange(Vm.Options.MetadataSources());
                    int i = all.IndexOf(S.MetadataSource ?? "");
                    S.MetadataSource = all[(i + 1) % all.Count];
                }
            };
        }

        void Edit(string message, string current, Action<string> apply)
        {
            if (Vm.Options.PromptText == null) { Vm.Toast("Text input isn't available here."); return; }
            var v = Vm.Options.PromptText(message, "Option", current);
            if (v != null) apply(v.Trim());
        }
    }
}
