using System;
using System.Collections.Generic;
using System.Linq;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // ======================= options (Select): a different list for every mode =======================

        PickPage optionsReturn = PickPage.Browse;

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

            if (MetadataManager.Snapshot().Running)
                add("Cancel the running metadata download", () => "A to cancel", () => { MetadataManager.Cancel(); ShowToast("Cancelling…"); });

            switch (mode)
            {
                case PickMode.Add:
                    add("Ask each game's name when adding", () => OnOff(s.AskNamesAdd), () => s.AskNamesAdd = !s.AskNamesAdd);
                    add("Download metadata after adding", () => OnOff(s.DownloadMetadata), () => s.DownloadMetadata = !s.DownloadMetadata);
                    list.Add(MetadataSourceOption());
                    add("Select the new games in the library", () => OnOff(s.SelectAdded), () => s.SelectAdded = !s.SelectAdded);
                    break;
                case PickMode.Move:
                    add("When the name already exists", () => s.MoveConflict == 1 ? "Keep both (add a number)" : "Skip it",
                        () => s.MoveConflict = s.MoveConflict == 1 ? 0 : 1);
                    add("Update library paths after moving", () => OnOff(s.UpdateLibraryPaths), () => s.UpdateLibraryPaths = !s.UpdateLibraryPaths);
                    break;
                default:
                    add("Keep the file extension when renaming", () => OnOff(s.KeepExtension), () => s.KeepExtension = !s.KeepExtension);
                    add("Update library paths after renaming", () => OnOff(s.UpdateLibraryPaths), () => s.UpdateLibraryPaths = !s.UpdateLibraryPaths);
                    break;
            }

            add("Sort by", () => s.SortBy == 1 ? "Date modified" : s.SortBy == 2 ? "Size (files)" : "Name", () => s.SortBy = (s.SortBy + 1) % 3);
            add("Order", () => s.SortDescending ? "Descending" : "Ascending", () => s.SortDescending = !s.SortDescending);
            add("Show hidden and system files", () => OnOff(s.ShowHidden), () => s.ShowHidden = !s.ShowHidden);
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
                Value = () => string.IsNullOrEmpty(s.MetadataSource) ? "All installed plugins (IGDB first)" : s.MetadataSource,
                Change = () =>
                {
                    var all = new List<string> { "" };
                    if (opt.MetadataSources != null) all.AddRange(opt.MetadataSources());
                    int i = all.IndexOf(s.MetadataSource ?? "");
                    s.MetadataSource = all[(i + 1) % all.Count];
                }
            };
        }

        void ToggleOptions()
        {
            if (page == PickPage.Options) { CloseOptions(); return; }
            if (page != PickPage.Browse || !opt.AllowMultiple) return;
            RenderOptions(0);
        }

        void CloseOptions()
        {
            Navigate(current, null);
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
