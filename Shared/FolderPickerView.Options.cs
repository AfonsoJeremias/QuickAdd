using System;
using System.Collections.Generic;
using System.Linq;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // ======================= options (Select): a different list for every mode =======================

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

            switch (mode)
            {
                case PickMode.Add:
                    add("Ask each game's name when adding", () => OnOff(s.AskNamesAdd), () => s.AskNamesAdd = !s.AskNamesAdd);
                    add("Select the new games in the library", () => OnOff(s.SelectAdded), () => s.SelectAdded = !s.SelectAdded);
                    break;
                case PickMode.Move:
                    add("When the name already exists", () => s.MoveConflict == 1 ? "Keep both (add a number)" : "Skip it",
                        () => s.MoveConflict = s.MoveConflict == 1 ? 0 : 1);
                    add("Update library paths after moving", () => OnOff(s.UpdateLibraryPaths), () => s.UpdateLibraryPaths = !s.UpdateLibraryPaths);
                    break;
                case PickMode.Rename:
                    add("Keep the file extension when renaming", () => OnOff(s.KeepExtension), () => s.KeepExtension = !s.KeepExtension);
                    add("Update library paths after renaming", () => OnOff(s.UpdateLibraryPaths), () => s.UpdateLibraryPaths = !s.UpdateLibraryPaths);
                    break;
                default:
                    add("Suggested name for a new folder", () => s.DefaultFolderName,
                        () => EditText("Suggested name for new folders", s.DefaultFolderName, v => s.DefaultFolderName = v));
                    add("Suggested name for a new file", () => s.DefaultFileName,
                        () => EditText("Suggested name for new files, with extension (for example New file.txt)", s.DefaultFileName, v => s.DefaultFileName = v));
                    add("Go into a new folder right after creating it", () => OnOff(s.OpenNewFolder), () => s.OpenNewFolder = !s.OpenNewFolder);
                    break;
            }

            add("Sort by", () => s.SortBy == 1 ? "Date modified" : s.SortBy == 2 ? "Size (files)" : "Name", () => s.SortBy = (s.SortBy + 1) % 3);
            add("Order", () => s.SortDescending ? "Descending" : "Ascending", () => s.SortDescending = !s.SortDescending);
            add("Show hidden and system files", () => OnOff(s.ShowHidden), () => s.ShowHidden = !s.ShowHidden);
            add("Open in the last folder", () => OnOff(s.RememberLastFolder), () => s.RememberLastFolder = !s.RememberLastFolder);
            add("Reset all options", () => "A to reset", () => s.ResetOptions());
            return list;
        }

        void EditText(string message, string currentValue, Action<string> apply)
        {
            if (opt.PromptText == null) { ShowToast("Text input isn't available here."); return; }
            var v = opt.PromptText(message, "Option", currentValue);
            if (!string.IsNullOrWhiteSpace(v)) apply(v.Trim());
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
