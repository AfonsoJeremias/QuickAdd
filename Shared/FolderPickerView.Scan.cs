using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // ======================= scan: start, results, executables =======================

        void StartScan(IList<string> roots)
        {
            if (roots == null || roots.Count == 0)
            {
                ShowToast("Open a folder first, or save one by scanning it once.");
                return;
            }
            if (!ScanManager.StartScan(roots, Settings))
            {
                ShowToast("A scan, import or metadata download is already running.");
                return;
            }

            bool changed = false;
            foreach (var r in roots)
                if (!Settings.SavedRoots.Contains(r, StringComparer.OrdinalIgnoreCase)) { Settings.SavedRoots.Add(r); changed = true; }
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
                items.Add(new PickItem
                {
                    Kind = PickKind.Candidate, Glyph = "\uE7FC", Text = c.Name, Path = c.Folder, Data = c,
                    Sub = CandidateSub(c), Marked = c.Selected
                });
            if (list.Count == 0)
                items.Add(new PickItem { Kind = PickKind.Up, Glyph = "\uE74A", Text = "Nothing to review", Sub = "Back to folders" });

            PathText.Text = "Scan results";
            int idx = focus == null ? -1 : items.ToList().FindIndex(i => i.Data == focus);
            Select(idx >= 0 ? idx : 0);
            UpdateModeUi();
        }

        static string Rel(ScanCandidate c, string exe)
        {
            try { return exe.Substring(c.Folder.Length).TrimStart('\\'); } catch { return Path.GetFileName(exe); }
        }

        static string CandidateSub(ScanCandidate c)
        {
            var sub = c.Folder + "  ›  " + Rel(c, c.Exe);
            if (c.ActionCount > 1) sub += "  ·  " + c.ActionCount + " actions";
            else if (c.Exes.Count > 1) sub += "  ·  " + c.Exes.Count + " executables found";
            if (c.Exists) sub += "  ·  already in library";
            return sub;
        }

        // all executables found for the game: A = main (Play) one, X = include as an extra game action
        void ShowAlternatives(ScanCandidate c, int focusIndex)
        {
            if (c == null) return;
            if (c.Exes.Count < 2) { ShowToast("Only one executable was found in this folder."); return; }

            page = PickPage.Alternatives;
            altFor = c;
            items.Clear();
            for (int i = 0; i < c.Exes.Count; i++)
            {
                long size = 0;
                try { size = new FileInfo(c.Exes[i]).Length; } catch { }
                items.Add(new PickItem
                {
                    Kind = PickKind.Alt, Glyph = i == c.Main ? "\uE768" : "\uE7FC", Text = Rel(c, c.Exes[i]),
                    Sub = FormatSize(size) + (i == c.Main ? "  ·  main (Play button)" : c.Extra.Contains(i) ? "  ·  extra action" : ""),
                    Data = i, Marked = c.IsIncluded(i)
                });
            }
            PathText.Text = c.Name + "  ·  " + c.Folder;
            Select(focusIndex >= 0 ? focusIndex : c.Main);
            UpdateModeUi();
        }

        void ChooseMain(PickItem it)
        {
            if (altFor == null || !(it.Data is int)) return;
            int i = (int)it.Data;
            altFor.Extra.Remove(i);
            altFor.Main = i;
            ShowAlternatives(altFor, i);
        }

        void ToggleExtra(PickItem it)
        {
            if (altFor == null || it == null || !(it.Data is int)) return;
            int i = (int)it.Data;
            if (i == altFor.Main) { ShowToast("The main executable can't be excluded - set another one as main first."); return; }
            if (!altFor.Extra.Remove(i)) altFor.Extra.Add(i);
            ShowAlternatives(altFor, i);
        }

        void Rename()
        {
            var it = List.SelectedItem as PickItem;
            var c = it == null ? null : it.Data as ScanCandidate;
            if (c == null) return;
            if (opt.PromptText == null) { ShowToast("Text input isn't available here."); return; }

            var name = opt.PromptText("Game name:", "Rename", c.Name);
            if (!string.IsNullOrWhiteSpace(name)) { c.Name = name.Trim(); ShowResults(c); }
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

            // optional: ask for each game's name first (Cancel skips that game)
            if (Settings.AskNamesScan && opt.PromptText != null)
            {
                var keep = new List<ScanCandidate>();
                for (int i = 0; i < chosen.Count; i++)
                {
                    var name = opt.PromptText("Name of the game (" + (i + 1) + " of " + chosen.Count + "):", "Import", chosen[i].Name);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    chosen[i].Name = name.Trim();
                    keep.Add(chosen[i]);
                }
                chosen = keep;
                if (chosen.Count == 0) { ShowResults(null); return; }
            }

            if (!ScanManager.StartImport(chosen, Settings))
            {
                ShowToast("A scan, import or metadata download is already running.");
                return;
            }
            Navigate(current, null);
            UpdateModeUi();
        }
    }
}
