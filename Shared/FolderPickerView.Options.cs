using System;
using System.Collections.Generic;
using System.Linq;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // ======================= options (Select): different list for every mode =======================

        sealed class OptionDef
        {
            public string Label;
            public Func<string> Value;
            public Action Change;
        }

        static string OnOff(bool v) { return v ? "On" : "Off"; }

        List<OptionDef> BuildOptions()
        {
            var s = Settings;
            var list = new List<OptionDef>();
            Action<string, Func<string>, Action> add = (l, v, c) => list.Add(new OptionDef { Label = l, Value = v, Change = c });

            if (mode == PickMode.Scan)
            {
                if (ScanManager.Snapshot().Running)
                    add("Cancel the running scan / import / metadata", () => "A to cancel", () => { ScanManager.Cancel(); ShowToast("Cancelling…"); });

                add("Folder layout", () => s.FolderPerGame ? "One game per subfolder (collections are split)" : "Folder with the executable",
                    () => s.FolderPerGame = !s.FolderPerGame);
                add("Search depth", () => s.Depth.ToString(), () => s.Depth = s.Depth % 4 + 1);
                add("Game name from", () => s.NameFromFolder ? "Folder name" : "File name", () => s.NameFromFolder = !s.NameFromFolder);
                add("Include files (wildcards, ; separated)", () => s.Include,
                    () => EditText("Files to include, e.g. *.exe;*.bat", s.Include, v => s.Include = v));
                add("Exclude files (wildcards, ; separated)", () => s.Exclude,
                    () => EditText("Files to skip, e.g. unins*;*setup*", s.Exclude, v => s.Exclude = v));
                add("Hide games already in the library", () => OnOff(s.HideExisting), () => s.HideExisting = !s.HideExisting);
                add("Install paths", () => s.RelativePaths ? "Relative to the Playnite folder" : "Absolute", () => s.RelativePaths = !s.RelativePaths);
                add("Ask each game's name before importing", () => OnOff(s.AskNamesScan), () => s.AskNamesScan = !s.AskNamesScan);
                add("Download metadata after importing", () => OnOff(s.DownloadMetadata), () => s.DownloadMetadata = !s.DownloadMetadata);
                list.Add(MetadataSourceOption());
                add("Saved scan folders", () => s.SavedRoots.Count + " saved - A clears them", () => s.SavedRoots.Clear());
            }
            else if (mode == PickMode.Add)
            {
                add("Ask each game's name when adding", () => OnOff(s.AskNamesAdd), () => s.AskNamesAdd = !s.AskNamesAdd);
                add("Download metadata after adding", () => OnOff(s.DownloadMetadata), () => s.DownloadMetadata = !s.DownloadMetadata);
                list.Add(MetadataSourceOption());
            }
            else if (mode == PickMode.Move)
            {
                add("When the name already exists", () => s.MoveConflict == 1 ? "Keep both (add a number)" : "Skip it",
                    () => s.MoveConflict = s.MoveConflict == 1 ? 0 : 1);
                add("Update library paths after moving", () => OnOff(s.UpdateLibraryPaths), () => s.UpdateLibraryPaths = !s.UpdateLibraryPaths);
            }
            else
            {
                add("Keep the file extension when renaming", () => OnOff(s.KeepExtension), () => s.KeepExtension = !s.KeepExtension);
                add("Update library paths after renaming", () => OnOff(s.UpdateLibraryPaths), () => s.UpdateLibraryPaths = !s.UpdateLibraryPaths);
            }

            if (mode != PickMode.Scan)
            {
                add("Sort by", () => s.SortBy == 1 ? "Date modified" : s.SortBy == 2 ? "Size (files)" : "Name", () => s.SortBy = (s.SortBy + 1) % 3);
                add("Order", () => s.SortDescending ? "Descending" : "Ascending", () => s.SortDescending = !s.SortDescending);
                add("Show hidden and system files", () => OnOff(s.ShowHidden), () => s.ShowHidden = !s.ShowHidden);
            }
            add("Open in the last folder", () => OnOff(s.RememberLastFolder), () => s.RememberLastFolder = !s.RememberLastFolder);
            add("Reset all options", () => "A to reset", () => s.ResetOptions());
            return list;
        }

        OptionDef MetadataSourceOption()
        {
            var s = Settings;
            return new OptionDef
            {
                Label = "Metadata source",
                Value = () => string.IsNullOrEmpty(s.MetadataSource) ? "All installed (IGDB first)" : s.MetadataSource,
                Change = () =>
                {
                    var all = new List<string> { "" };
                    if (opt.MetadataSources != null) all.AddRange(opt.MetadataSources());
                    int i = all.IndexOf(s.MetadataSource ?? "");
                    s.MetadataSource = all[(i + 1) % all.Count];
                }
            };
        }

        void EditText(string message, string currentValue, Action<string> apply)
        {
            if (opt.PromptText == null) { ShowToast("Text input isn't available here."); return; }
            var v = opt.PromptText(message, "Option", currentValue);
            if (v != null) apply(v.Trim());
        }

        void ToggleOptions()
        {
            if (page == PickPage.Options) { CloseOptions(); return; }
            if (page != PickPage.Browse && page != PickPage.Results) return;
            if (!opt.AllowMultiple) return;
            optionsReturn = page;
            RenderOptions(0);
        }

        void CloseOptions()
        {
            if (optionsReturn == PickPage.Results) ShowResults(null); else Navigate(current, null);
            UpdateModeUi();
        }

        void RenderOptions(int focusIndex)
        {
            page = PickPage.Options;
            items.Clear();
            foreach (var def in BuildOptions())
                items.Add(new PickItem { Kind = PickKind.Option, Glyph = "\uE713", Text = def.Label, Sub = def.Value(), Data = def });

            PathText.Text = "Options  ·  " + mode.ToString() + " mode";
            if (items.Count > 0) Select(Math.Max(0, Math.Min(items.Count - 1, focusIndex)));
            UpdateModeUi();
        }

        void ChangeOption(PickItem it)
        {
            var def = it.Data as OptionDef;
            if (def == null) return;
            int idx = items.IndexOf(it);
            def.Change();
            Settings.Save();
            RenderOptions(idx);
        }
    }
}
