using QuickAdd.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickAdd.UI
{
    // ============================ scan results: review before importing ============================
    public sealed class ResultsPage : PickerPage
    {
        static AppSettings S { get { return AppSettings.Current; } }

        public override string Subtitle { get { return "Scan results"; } }

        public override string Title(string baseTitle)
        {
            var r = ScanService.GetResults();
            return baseTitle + "   ·   " + r.Count + " found, " + r.Count(c => c.Selected) + " selected";
        }

        public override void Enter(object focus)
        {
            var list = ScanService.GetResults();
            foreach (var c in list)
            {
                Vm.Rows.Add(new RowViewModel
                {
                    Kind = RowKind.Candidate, Glyph = "\uE7FC", Text = c.Name, Path = c.Folder, Data = c,
                    Sub = Describe(c), Marked = c.Selected
                });
            }
            if (list.Count == 0) Vm.Rows.Add(Row(RowKind.Up, "\uE74A", "Nothing to review", "Back to folders"));

            int idx = focus is ScanCandidate ? Vm.Rows.ToList().FindIndex(r => r.Data == focus) : -1;
            Vm.Select(idx >= 0 ? idx : 0);
        }

        public static string Relative(ScanCandidate c, string exe)
        {
            try { return exe.Substring(c.Folder.Length).TrimStart('\\'); } catch { return Path.GetFileName(exe); }
        }

        static string Describe(ScanCandidate c)
        {
            var sub = c.Folder + "  ›  " + Relative(c, c.Exe);
            if (c.ActionCount > 1) sub += "  ·  " + c.ActionCount + " actions";
            else if (c.Exes.Count > 1) sub += "  ·  " + c.Exes.Count + " executables found";
            if (c.Exists) sub += "  ·  already in library";
            return sub;
        }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Choose executables"); yield return H("X", "Select / unselect"); yield return H("Y", "Rename"); yield return H("B", "Back to folders");
            yield return H("Start", "Import selected"); yield return H("LT", "Select none"); yield return H("RT", "Select all"); yield return H("Select", "Options");
        }

        public override void Handle(PickerCommand command, RowViewModel row)
        {
            var c = row == null ? null : row.Data as ScanCandidate;
            switch (command)
            {
                case PickerCommand.Accept:
                    if (row == null) return;
                    if (row.Kind == RowKind.Up) Vm.ShowBrowse();
                    else if (c != null && c.Exes.Count < 2) Vm.Toast("Only one executable was found in this folder.");
                    else if (c != null) Vm.Show(new ExecutablesPage(c), null);
                    break;
                case PickerCommand.Back: Vm.ShowBrowse(); break;
                case PickerCommand.Mark:
                    if (c == null) return;
                    c.Selected = !c.Selected; row.Marked = c.Selected; Vm.RefreshChrome();
                    break;
                case PickerCommand.Alt: Rename(c); break;
                case PickerCommand.SelectAll: SetAll(true, c); break;
                case PickerCommand.SelectNone: SetAll(false, c); break;
                case PickerCommand.Confirm: Import(); break;
                case PickerCommand.Options: Vm.Show(new OptionsPage(ModeKind.Scan, this), null); break;
            }
        }

        void Rename(ScanCandidate c)
        {
            if (c == null) return;
            if (Vm.Options.PromptText == null) { Vm.Toast("Text input isn't available here."); return; }
            var name = Vm.Options.PromptText("Game name:", "Rename", c.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            c.Name = name.Trim();
            Vm.Show(this, c);
        }

        void SetAll(bool value, ScanCandidate focus)
        {
            foreach (var c in ScanService.GetResults()) c.Selected = value;
            Vm.Show(this, focus);
        }

        void Import()
        {
            var chosen = ScanService.GetResults().Where(c => c.Selected).ToList();
            if (chosen.Count == 0) { Vm.Toast("Select at least one game with X."); return; }

            // optional: ask for each game's name first (Cancel skips that game)
            if (S.AskNamesScan && Vm.Options.PromptText != null)
            {
                var keep = new List<ScanCandidate>();
                for (int i = 0; i < chosen.Count; i++)
                {
                    var name = Vm.Options.PromptText("Name of the game (" + (i + 1) + " of " + chosen.Count + "):", "Import", chosen[i].Name);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    chosen[i].Name = name.Trim();
                    keep.Add(chosen[i]);
                }
                chosen = keep;
                if (chosen.Count == 0) { Vm.Show(this, null); return; }
            }

            if (!ScanService.StartImport(chosen, S)) { Vm.Toast("A scan, import or metadata download is already running."); return; }
            Vm.ShowBrowse();
        }
    }

    // ============================ executables of one game: main + extra actions ============================
    public sealed class ExecutablesPage : PickerPage
    {
        readonly ScanCandidate game;
        public ExecutablesPage(ScanCandidate game) { this.game = game; }

        public override bool AllowModeSwitch { get { return false; } }
        public override string Subtitle { get { return game.Name + "  ·  " + game.Folder; } }
        public override string Title(string baseTitle) { return baseTitle + "   ·   Executables"; }

        public override void Enter(object focus)
        {
            for (int i = 0; i < game.Exes.Count; i++)
            {
                long size = 0;
                try { size = new FileInfo(game.Exes[i]).Length; } catch { }
                Vm.Rows.Add(new RowViewModel
                {
                    Kind = RowKind.Executable, Glyph = i == game.Main ? "\uE768" : "\uE7FC",
                    Text = ResultsPage.Relative(game, game.Exes[i]), Data = i, Marked = game.IsIncluded(i),
                    Sub = FormatSize(size) + (i == game.Main ? "  ·  main (Play button)" : game.Extra.Contains(i) ? "  ·  extra action" : "")
                });
            }
            Vm.Select(focus is int ? (int)focus : game.Main);
        }

        public override IEnumerable<HintViewModel> Hints()
        {
            yield return H("A", "Set as main (Play)"); yield return H("X", "Include / exclude"); yield return H("B", "Done");
        }

        public override void Handle(PickerCommand command, RowViewModel row)
        {
            int i = row != null && row.Data is int ? (int)row.Data : -1;
            switch (command)
            {
                case PickerCommand.Accept:
                    if (i < 0) return;
                    game.Extra.Remove(i); game.Main = i;
                    Vm.Show(this, i);
                    break;
                case PickerCommand.Mark:
                    if (i < 0) return;
                    if (i == game.Main) { Vm.Toast("The main executable can't be excluded - set another one as main first."); return; }
                    if (!game.Extra.Remove(i)) game.Extra.Add(i);
                    Vm.Show(this, i);
                    break;
                case PickerCommand.Back: Vm.Show(new ResultsPage(), game); break;
            }
        }
    }
}
