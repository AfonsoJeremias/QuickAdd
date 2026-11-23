using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // ======================= scan: start, results, alternatives =======================

        void StartScan(IList<string> roots)
        {
            if (roots == null || roots.Count == 0)
            {
                ShowToast("Open a folder first, or save one by scanning it once.");
                return;
            }
            if (!ScanManager.StartScan(roots, Settings))
            {
                ShowToast("A scan or import is already running.");
                return;
            }

            bool changed = false;
            foreach (var r in roots)
            {
                if (!Settings.SavedRoots.Contains(r, StringComparer.OrdinalIgnoreCase)) { Settings.SavedRoots.Add(r); changed = true; }
            }
            if (changed) Settings.Save();

            markedScan.Clear();
            foreach (var i in items) i.Marked = false;
            RefreshTitle();
        }

        void ShowResults(ScanCandidate focus)
        {
            page = PickPage.Results;
            altFor = null;
            items.Clear();

            var list = ScanManager.GetResults();
            foreach (var c in list)
            {
                items.Add(new PickItem
                {
                    Kind = PickKind.Candidate, Glyph = "\uE7FC", Text = c.Name, Path = c.Folder, Data = c,
                    Sub = CandidateSub(c), Marked = c.Selected
                });
            }
            if (list.Count == 0)
            {
                items.Add(new PickItem { Kind = PickKind.Up, Glyph = "\uE74A", Text = "Nothing to review", Sub = "Back to folders" });
            }

            PathText.Text = "Scan results";
            int idx = focus == null ? -1 : items.ToList().FindIndex(i => i.Data == focus);
            Select(idx >= 0 ? idx : 0);
            UpdateModeUi();
        }

        static string CandidateSub(ScanCandidate c)
        {
            string rel;
            try { rel = c.Exe.Substring(c.Folder.Length).TrimStart('\\'); } catch { rel = Path.GetFileName(c.Exe); }
            var sub = rel;
            if (c.Exes.Count > 1) sub += "  ·  " + c.Exes.Count + " executables";
            if (c.Exists) sub += "  ·  already in library";
            return sub;
        }

        void ShowAlternatives(ScanCandidate c)
        {
            if (c == null) return;
            if (c.Exes.Count < 2) { ShowToast("Only one executable was found in this folder."); return; }

            page = PickPage.Alternatives;
            altFor = c;
            items.Clear();
            for (int i = 0; i < c.Exes.Count; i++)
            {
                string rel;
                try { rel = c.Exes[i].Substring(c.Folder.Length).TrimStart('\\'); } catch { rel = c.Exes[i]; }
                long size = 0;
                try { size = new FileInfo(c.Exes[i]).Length; } catch { }
                items.Add(new PickItem
                {
                    Kind = PickKind.Alt, Glyph = "\uE7FC", Text = rel, Sub = FormatSize(size), Data = i, Marked = i == c.ExeIndex
                });
            }
            PathText.Text = c.Name + "  ·  " + c.Folder;
            Select(c.ExeIndex);
            UpdateModeUi();
        }

        void ChooseAlternative(PickItem it)
        {
            if (altFor == null || !(it.Data is int)) return;
            altFor.ExeIndex = (int)it.Data;
            ShowResults(altFor);
        }

        void Rename()
        {
            var it = List.SelectedItem as PickItem;
            var c = it == null ? null : it.Data as ScanCandidate;
            if (c == null) return;
            if (opt.PromptText == null) { ShowToast("Text input isn't available here."); return; }

            var name = opt.PromptText("Game name:", "Rename", c.Name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                c.Name = name.Trim();
                ShowResults(c);
            }
        }

        void SelectAllResults(bool value)
        {
            if (page != PickPage.Results) return;
            var focus = (List.SelectedItem as PickItem)?.Data as ScanCandidate;
            foreach (var c in ScanManager.GetResults()) c.Selected = value;
            ShowResults(focus);
        }

        void ImportSelected()
        {
            var chosen = ScanManager.GetResults().Where(c => c.Selected).ToList();
            if (chosen.Count == 0) { ShowToast("Select at least one game with X."); return; }
            if (!ScanManager.StartImport(chosen, Settings))
            {
                ShowToast("A scan or import is already running.");
                return;
            }
            Navigate(current, null); // back to the folders; progress shows at the bottom
            UpdateModeUi();
        }

        // ======================= options (Select) =======================

        sealed class OptionDef
        {
            public string Label;
            public Func<string> Value;
            public Action Change;
        }

        List<OptionDef> BuildOptions()
        {
            var s = Settings;
            var list = new List<OptionDef>();

            if (mode == PickMode.Scan)
            {
                if (ScanManager.Snapshot().Running)
                    list.Add(new OptionDef
                    {
                        Label = "Cancel the running scan / import", Value = () => "A to cancel",
                        Change = () => { ScanManager.Cancel(); ShowToast("Cancelling…"); }
                    });

                list.Add(new OptionDef
                {
                    Label = "Folder layout",
                    Value = () => s.FolderPerGame ? "One game per subfolder" : "Search folders recursively",
                    Change = () => s.FolderPerGame = !s.FolderPerGame
                });
                list.Add(new OptionDef
                {
                    Label = "Search depth", Value = () => s.Depth.ToString(),
                    Change = () => s.Depth = s.Depth % 4 + 1
                });
                list.Add(new OptionDef
                {
                    Label = "Game name from", Value = () => s.NameFromFolder ? "Folder name" : "File name",
                    Change = () => s.NameFromFolder = !s.NameFromFolder
                });
                list.Add(new OptionDef
                {
                    Label = "Include files (wildcards, ; separated)", Value = () => s.Include,
                    Change = () => EditText("Files to include, e.g. *.exe;*.bat", s.Include, v => s.Include = v)
                });
                list.Add(new OptionDef
                {
                    Label = "Exclude files (wildcards, ; separated)", Value = () => s.Exclude,
                    Change = () => EditText("Files to skip, e.g. unins*;*setup*", s.Exclude, v => s.Exclude = v)
                });
                list.Add(new OptionDef
                {
                    Label = "Hide games already in the library", Value = () => s.HideExisting ? "On" : "Off",
                    Change = () => s.HideExisting = !s.HideExisting
                });
                list.Add(new OptionDef
                {
                    Label = "Install paths", Value = () => s.RelativePaths ? "Relative to the Playnite folder" : "Absolute",
                    Change = () => s.RelativePaths = !s.RelativePaths
                });
                list.Add(new OptionDef
                {
                    Label = "Saved scan folders", Value = () => s.SavedRoots.Count + " saved - A clears them",
                    Change = () => s.SavedRoots.Clear()
                });
                list.Add(new OptionDef
                {
                    Label = "Reset scan options", Value = () => "A to reset",
                    Change = () =>
                    {
                        var keep = s.SavedRoots.ToList();
                        s.SetDefaults();
                        s.SavedRoots = keep;
                    }
                });
            }
            else
            {
                list.Add(new OptionDef
                {
                    Label = "Sort by",
                    Value = () => s.SortBy == 1 ? "Date modified" : s.SortBy == 2 ? "Size (files)" : "Name",
                    Change = () => s.SortBy = (s.SortBy + 1) % 3
                });
                list.Add(new OptionDef
                {
                    Label = "Order", Value = () => s.SortDescending ? "Descending" : "Ascending",
                    Change = () => s.SortDescending = !s.SortDescending
                });
            }
            return list;
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
            if (optionsReturn == PickPage.Results) ShowResults(null);
            else Navigate(current, null);
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
