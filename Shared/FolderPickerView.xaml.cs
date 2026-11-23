using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FullscreenKit
{
    public class PickerOptions
    {
        public string Title { get; set; } = "Select";
        public bool FoldersOnly { get; set; }
        /// <summary>File mode: X marks files, Start confirms all marked ones (also enables Select = options).</summary>
        public bool AllowMultiple { get; set; }
        /// <summary>Adds the Move mode (move files and folders in the background).</summary>
        public bool AllowMove { get; set; }
        /// <summary>Adds the Scan mode (find games in folders, review, import).</summary>
        public bool AllowScan { get; set; }
        /// <summary>Add mode only. Lower-case with dot, e.g. ".exe". Empty = all files.</summary>
        public string[] Extensions { get; set; } = new string[0];
        public string StartPath { get; set; }
        /// <summary>(message, caption, default) -> text, or null if cancelled. Used for rename / pattern editing.</summary>
        public Func<string, string, string, string> PromptText { get; set; }
    }

    public enum PickKind { Header, Place, Drive, Up, Use, Folder, File, Action, Saved, Candidate, Alt, Option }
    public enum PickMode { Add, Move, Scan }
    public enum PickPage { Browse, Results, Alternatives, Options }

    public class PickItem : INotifyPropertyChanged
    {
        bool marked;
        public event PropertyChangedEventHandler PropertyChanged;

        public PickKind Kind { get; set; }
        public string Glyph { get; set; }
        public string Text { get; set; }
        public string Sub { get; set; }
        public string Path { get; set; }
        public string Tag { get; set; }
        public object Data { get; set; }
        public bool Selectable { get { return Kind != PickKind.Header; } }
        public double TextSize { get { return Kind == PickKind.Header ? 14 : 22; } }
        public Brush TextBrush { get { return Kind == PickKind.Header ? Brushes.Gray : Brushes.White; } }
        public Visibility SubVisibility { get { return string.IsNullOrEmpty(Sub) ? Visibility.Collapsed : Visibility.Visible; } }

        public bool Marked
        {
            get { return marked; }
            set
            {
                marked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkVisibility)));
            }
        }
        public Visibility MarkVisibility { get { return marked ? Visibility.Visible : Visibility.Collapsed; } }
    }

    /// <summary>
    /// Controller-driven browser with three modes (switch with left/right):
    /// Add (pick games), Move (move files/folders) and Scan (find games in folders). Select = options.
    /// </summary>
    public partial class FolderPickerView : UserControl
    {
        const int MaxFolders = 1000, MaxFiles = 500;
        const int DuplicateWindowMs = 200, StickCooldownMs = 300;

        static readonly Brush AddColor = new SolidColorBrush(Color.FromRgb(0x2F, 0x7D, 0xD1));
        static readonly Brush MoveColor = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E));
        static readonly Brush ScanColor = new SolidColorBrush(Color.FromRgb(0x2F, 0xA3, 0x6B));
        static readonly Brush KeyCapColor = new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x38));
        static readonly Brush HintColor = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB0));

        /// <summary>Raised with the chosen paths (one entry for single picks); null = cancelled.</summary>
        public event Action<IList<string>> Done;

        readonly PickerOptions opt;
        readonly ObservableCollection<PickItem> items = new ObservableCollection<PickItem>();
        readonly List<string> markedAdd = new List<string>();
        readonly List<string> markedMove = new List<string>();
        readonly List<string> markedScan = new List<string>();
        readonly List<PickMode> modes = new List<PickMode>();
        readonly DispatcherTimer repeat = new DispatcherTimer();
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        int repeatDir;
        PickMode mode = PickMode.Add;
        PickPage page = PickPage.Browse;
        PickPage optionsReturn = PickPage.Browse;
        ScanCandidate altFor;
        string current; // null = places + drives
        int seenResultsVersion;
        int lastVerticalTick;

        string lastAction;
        int lastActionTick;
        bool lastFromPad;

        List<string> Marks
        {
            get
            {
                switch (mode)
                {
                    case PickMode.Move: return markedMove;
                    case PickMode.Scan: return markedScan;
                    default: return markedAdd;
                }
            }
        }

        static ScanSettings Settings { get { return ScanSettings.Current; } }

        public FolderPickerView(PickerOptions options)
        {
            InitializeComponent();
            opt = options ?? new PickerOptions();
            if (opt.FoldersOnly) { opt.AllowMultiple = false; opt.AllowMove = false; opt.AllowScan = false; }
            if (!opt.AllowMultiple) { opt.AllowMove = false; opt.AllowScan = false; }

            modes.Add(PickMode.Add);
            if (opt.AllowMove) modes.Add(PickMode.Move);
            if (opt.AllowScan) modes.Add(PickMode.Scan);
            seenResultsVersion = ScanManager.ResultsVersion;

            List.ItemsSource = items;
            UpdateModeUi();

            repeat.Tick += (s, e) =>
            {
                repeat.Interval = TimeSpan.FromMilliseconds(70);
                lastVerticalTick = Environment.TickCount;
                IsDuplicate("move" + repeatDir, true);
                Move(repeatDir);
            };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); UpdateProgress(); };

            PreviewKeyDown += OnKey;
            List.MouseDoubleClick += (s, e) => Activate();
            Loaded += (s, e) =>
            {
                Focus();
                MoveManager.Changed -= OnJobsChanged;
                MoveManager.Changed += OnJobsChanged;
                ScanManager.Changed -= OnJobsChanged;
                ScanManager.Changed += OnJobsChanged;
                UpdateProgress(); // reopening while something runs shows its bar again
            };
            Unloaded += (s, e) => { repeat.Stop(); Unsubscribe(); };

            var start = opt.StartPath;
            Navigate(!string.IsNullOrEmpty(start) && Directory.Exists(start) ? start : null, null);
        }

        void Unsubscribe()
        {
            MoveManager.Changed -= OnJobsChanged;
            ScanManager.Changed -= OnJobsChanged;
        }

        // ---------- input ----------

        // Playnite may turn pad presses into key presses as well: the same action from the other source within a
        // short window is the same press and is ignored.
        bool IsDuplicate(string action, bool fromPad)
        {
            int now = Environment.TickCount;
            bool dup = lastAction == action && lastFromPad != fromPad &&
                       unchecked(now - lastActionTick) < DuplicateWindowMs;
            if (!dup) { lastAction = action; lastActionTick = now; lastFromPad = fromPad; }
            return dup;
        }

        bool StickCooldown() { return unchecked(Environment.TickCount - lastVerticalTick) < StickCooldownMs; }

        /// <summary>Called by PickerHost from Playnite's OnControllerButtonStateChanged.</summary>
        public void HandleController(ControllerInput button, bool pressed)
        {
            switch (button)
            {
                case ControllerInput.DPadUp: Hold(-1, pressed); break;
                case ControllerInput.DPadDown: Hold(1, pressed); break;
                case ControllerInput.LeftStickUp: lastVerticalTick = Environment.TickCount; Hold(-1, pressed); break;
                case ControllerInput.LeftStickDown: lastVerticalTick = Environment.TickCount; Hold(1, pressed); break;

                case ControllerInput.DPadLeft: if (pressed && !IsDuplicate("modeprev", true)) CycleMode(-1); break;
                case ControllerInput.DPadRight: if (pressed && !IsDuplicate("modenext", true)) CycleMode(1); break;
                case ControllerInput.LeftStickLeft:
                    if (pressed && !StickCooldown() && !IsDuplicate("modeprev", true)) CycleMode(-1); break;
                case ControllerInput.LeftStickRight:
                    if (pressed && !StickCooldown() && !IsDuplicate("modenext", true)) CycleMode(1); break;

                case ControllerInput.A: if (pressed && !IsDuplicate("activate", true)) Activate(); break;
                case ControllerInput.B: if (pressed && !IsDuplicate("back", true)) GoBack(); break;
                case ControllerInput.X: if (pressed && !IsDuplicate("mark", true)) MarkOrUse(); break;
                case ControllerInput.Y: if (pressed && !IsDuplicate("y", true)) YAction(); break;
                case ControllerInput.Start: if (pressed && !IsDuplicate("confirm", true)) Confirm(); break;
                case ControllerInput.Back: if (pressed && !IsDuplicate("options", true)) ToggleOptions(); break; // Select / View
                case ControllerInput.TriggerLeft: if (pressed && !IsDuplicate("none", true)) SelectAllResults(false); break;
                case ControllerInput.TriggerRight: if (pressed && !IsDuplicate("all", true)) SelectAllResults(true); break;
                case ControllerInput.LeftShoulder: if (pressed && !IsDuplicate("move-8", true)) Move(-8); break;
                case ControllerInput.RightShoulder: if (pressed && !IsDuplicate("move8", true)) Move(8); break;
            }
        }

        void Hold(int dir, bool pressed)
        {
            if (pressed)
            {
                repeatDir = dir;
                if (!IsDuplicate("move" + dir, true)) Move(dir);
                repeat.Interval = TimeSpan.FromMilliseconds(400);
                repeat.Start();
            }
            else if (repeatDir == dir)
            {
                repeat.Stop();
            }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            string action;
            switch (e.Key)
            {
                case Key.Up: action = "move-1"; break;
                case Key.Down: action = "move1"; break;
                case Key.PageUp: action = "move-8"; break;
                case Key.PageDown: action = "move8"; break;
                case Key.Left: action = "modeprev"; break;
                case Key.Right:
                case Key.Tab: action = "modenext"; break;
                case Key.Enter: action = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? "confirm" : "activate"; break;
                case Key.Escape:
                case Key.Back: action = "back"; break;
                case Key.Space: action = "mark"; break;
                case Key.Y: action = "y"; break;
                case Key.F2: action = "options"; break;
                case Key.Insert: action = "all"; break;
                case Key.Delete: action = "none"; break;
                default: return;
            }
            e.Handled = true;
            if (IsDuplicate(action, false)) return;

            switch (action)
            {
                case "move-1": Move(-1); break;
                case "move1": Move(1); break;
                case "move-8": Move(-8); break;
                case "move8": Move(8); break;
                case "modeprev": CycleMode(-1); break;
                case "modenext": CycleMode(1); break;
                case "activate": Activate(); break;
                case "confirm": Confirm(); break;
                case "back": GoBack(); break;
                case "mark": MarkOrUse(); break;
                case "y": YAction(); break;
                case "options": ToggleOptions(); break;
                case "all": SelectAllResults(true); break;
                case "none": SelectAllResults(false); break;
            }
        }

        // ---------- actions ----------

        void Activate()
        {
            var it = List.SelectedItem as PickItem;
            if (it == null || !it.Selectable) return;

            switch (page)
            {
                case PickPage.Results:
                    if (it.Kind == PickKind.Up) GoBack(); else ShowAlternatives(it.Data as ScanCandidate);
                    return;
                case PickPage.Alternatives: ChooseAlternative(it); return;
                case PickPage.Options: ChangeOption(it); return;
            }

            switch (it.Kind)
            {
                case PickKind.Use: Finish(new List<string> { it.Path }); break;
                case PickKind.File:
                    if (mode != PickMode.Add) return; // files are only marked (X) in Move mode, hidden in Scan mode
                    var result = new List<string>(markedAdd);
                    if (!result.Contains(it.Path, StringComparer.OrdinalIgnoreCase)) result.Add(it.Path);
                    Finish(result);
                    break;
                case PickKind.Up: GoBack(); break;
                case PickKind.Action: RunAction(it); break;
                default: Navigate(it.Path, null); break; // Place, Drive, Folder, Saved
            }
        }

        void RunAction(PickItem it)
        {
            switch (it.Tag)
            {
                case "scanhere": if (current != null) StartScan(new List<string> { current }); break;
                case "scanall": StartScan(SavedRoots()); break;
                case "results": ShowResults(null); break;
            }
        }

        // X: folder mode = use folder; Results = select/unselect; otherwise mark for the current mode
        void MarkOrUse()
        {
            var it = List.SelectedItem as PickItem;

            if (page == PickPage.Results)
            {
                var c = it == null ? null : it.Data as ScanCandidate;
                if (c == null) return;
                c.Selected = !c.Selected;
                it.Marked = c.Selected;
                RefreshTitle();
                return;
            }
            if (page != PickPage.Browse) return;

            if (opt.FoldersOnly)
            {
                if (current != null) Finish(new List<string> { current });
                return;
            }
            if (it == null) return;

            switch (mode)
            {
                case PickMode.Add: if (!opt.AllowMultiple || it.Kind != PickKind.File) return; break;
                case PickMode.Move: if (it.Kind != PickKind.Folder && it.Kind != PickKind.File) return; break;
                case PickMode.Scan: if (it.Kind != PickKind.Folder && it.Kind != PickKind.Saved) return; break;
            }

            var list = Marks;
            int idx = list.FindIndex(p => string.Equals(p, it.Path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) { list.RemoveAt(idx); it.Marked = false; }
            else { list.Add(it.Path); it.Marked = true; }
            RefreshTitle();
        }

        // Start: Results = import; folder mode = use folder; Add = add marked; Move = move here; Scan = scan
        void Confirm()
        {
            if (page == PickPage.Results) { ImportSelected(); return; }
            if (page != PickPage.Browse) return;

            if (opt.FoldersOnly)
            {
                if (current != null) Finish(new List<string> { current });
                return;
            }
            if (mode == PickMode.Move) { StartMove(); return; }
            if (mode == PickMode.Scan)
            {
                if (markedScan.Count > 0) StartScan(markedScan.ToList());
                else if (current != null) StartScan(new List<string> { current });
                else StartScan(SavedRoots());
                return;
            }
            if (!opt.AllowMultiple) return;

            if (markedAdd.Count > 0) { Finish(new List<string>(markedAdd)); return; }
            var it = List.SelectedItem as PickItem;
            if (it != null && it.Kind == PickKind.File) Finish(new List<string> { it.Path });
        }

        void YAction()
        {
            if (page == PickPage.Results) Rename();
            else if (page == PickPage.Browse) Navigate(null, current);
        }

        void StartMove()
        {
            if (current == null) { ShowToast("Open the destination folder first, then press Start."); return; }
            if (markedMove.Count == 0) { ShowToast("Mark files or folders with X first."); return; }

            var dest = current.TrimEnd('\\');
            foreach (var src in markedMove)
            {
                var s = src.TrimEnd('\\');
                if (Directory.Exists(s) &&
                    (string.Equals(dest, s, StringComparison.OrdinalIgnoreCase) ||
                     dest.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    ShowToast("A folder can't be moved into itself.");
                    return;
                }
            }

            MoveManager.Enqueue(markedMove.ToList(), current);
            markedMove.Clear();
            foreach (var i in items) i.Marked = false;
            RefreshTitle();
        }

        void CycleMode(int dir)
        {
            if (modes.Count < 2 || page == PickPage.Alternatives || page == PickPage.Options) return;
            int i = modes.IndexOf(mode);
            mode = modes[(i + dir + modes.Count) % modes.Count];
            var focus = (List.SelectedItem as PickItem)?.Path;
            UpdateModeUi();
            Navigate(current, focus); // also returns to the Browse page
        }

        void GoBack()
        {
            switch (page)
            {
                case PickPage.Results: Navigate(current, null); UpdateModeUi(); return;
                case PickPage.Alternatives: ShowResults(altFor); return;
                case PickPage.Options: CloseOptions(); return;
            }

            if (current == null) { Finish(null); return; }
            string parent = null;
            try { parent = Directory.GetParent(current)?.FullName; } catch { }
            Navigate(parent, current);
        }

        void Finish(IList<string> result)
        {
            repeat.Stop();
            Unsubscribe();
            Done?.Invoke(result);
        }

        // ---------- tabs, title, button prompts ----------

        void UpdateModeUi()
        {
            RenderTabs();
            RefreshTitle();
            RenderHints();
        }

        void RenderTabs()
        {
            TabsPanel.Children.Clear();
            if (modes.Count < 2) { TabsPanel.Visibility = Visibility.Collapsed; return; }
            TabsPanel.Visibility = Visibility.Visible;

            TabsPanel.Children.Add(Arrow("◀"));
            foreach (var m in modes)
            {
                bool on = m == mode;
                Brush color = m == PickMode.Move ? MoveColor : m == PickMode.Scan ? ScanColor : AddColor;
                TabsPanel.Children.Add(new Border
                {
                    Background = on ? color : KeyCapColor,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(18, 6, 18, 6),
                    Margin = new Thickness(4, 0, 4, 0),
                    Child = new TextBlock
                    {
                        Text = m.ToString().ToUpperInvariant(),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = on ? Brushes.White : HintColor
                    }
                });
            }
            TabsPanel.Children.Add(Arrow("▶"));
        }

        static TextBlock Arrow(string glyph)
        {
            return new TextBlock { Text = glyph, FontSize = 16, Foreground = HintColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        }

        void RefreshTitle()
        {
            string t = opt.Title;
            if (page == PickPage.Results)
            {
                var r = ScanManager.GetResults();
                t += "   ·   " + r.Count + " found, " + r.Count(c => c.Selected) + " selected";
            }
            else if (page == PickPage.Options)
            {
                t += "   ·   Options";
            }
            else
            {
                int n = Marks.Count;
                if (n > 0)
                    t += "   ·   " + n + (mode == PickMode.Move ? " to move" : mode == PickMode.Scan ? " to scan" : " marked");
            }
            TitleText.Text = t;
            if (mode == PickMode.Scan && page == PickPage.Browse) RenderHints(); // Start's label depends on the marks
        }

        void RenderHints()
        {
            var h = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, l) => h.Add(new KeyValuePair<string, string>(k, l));
            bool tabs = modes.Count > 1;

            if (opt.FoldersOnly)
            {
                add("A", "Open"); add("X", "Use this folder"); add("Start", "Use this folder");
                add("B", "Back"); add("Y", "Places / drives");
            }
            else if (page == PickPage.Options)
            {
                add("A", "Change"); add("B", "Close"); add("Select", "Close");
            }
            else if (page == PickPage.Alternatives)
            {
                add("A", "Use this executable"); add("B", "Cancel");
            }
            else if (page == PickPage.Results)
            {
                add("A", "Choose executable"); add("X", "Select / unselect"); add("Start", "Import selected");
                add("Y", "Rename"); add("LT", "Select none"); add("RT", "Select all");
                add("Select", "Options"); if (tabs) add("◀ ▶", "Mode"); add("B", "Back to folders");
            }
            else if (!opt.AllowMultiple)
            {
                add("A", "Open / select"); add("B", "Back"); add("Y", "Places / drives");
            }
            else if (mode == PickMode.Move)
            {
                add("A", "Open"); add("X", "Mark to move"); add("Start", "Move here");
                add("Select", "Options"); if (tabs) add("◀ ▶", "Mode"); add("B", "Back"); add("Y", "Places / drives");
            }
            else if (mode == PickMode.Scan)
            {
                add("A", "Open"); add("X", "Mark folder");
                add("Start", markedScan.Count > 0 ? "Scan marked folders" : current == null ? "Scan saved folders" : "Scan this folder");
                add("Select", "Options"); if (tabs) add("◀ ▶", "Mode"); add("B", "Back"); add("Y", "Places / drives");
            }
            else
            {
                add("A", "Open / add"); add("X", "Mark"); add("Start", "Add marked");
                add("Select", "Options"); if (tabs) add("◀ ▶", "Mode"); add("B", "Back"); add("Y", "Places / drives");
            }

            HintPanel.Children.Clear();
            foreach (var p in h)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new Border
                {
                    Background = KeyCapColor,
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(9, 2, 9, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = p.Key, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14 }
                });
                row.Children.Add(new TextBlock
                {
                    Text = p.Value, Foreground = HintColor, FontSize = 16,
                    Margin = new Thickness(8, 0, 26, 0), VerticalAlignment = VerticalAlignment.Center
                });
                HintPanel.Children.Add(row);
            }
        }

        // ---------- background jobs: progress bars ----------

        void OnJobsChanged() { Dispatcher.BeginInvoke(new Action(UpdateProgress)); }

        void UpdateProgress()
        {
            var m = MoveManager.Snapshot();
            if (m.Running)
            {
                toastTimer.Stop();
                ProgressPanel.Visibility = Visibility.Visible;
                MoveBar.Visibility = Visibility.Visible;
                MoveBar.Value = m.Percent;
                var text = string.IsNullOrEmpty(m.Name) ? "Moving…" : "Moving \"" + m.Name + "\"   " + m.Percent + "%";
                if (m.Count > 1 && m.Index > 0) text += "   (" + m.Index + " of " + m.Count + ")";
                if (m.Queued > 0) text += "   +" + m.Queued + " queued";
                ProgressText.Text = text;
            }
            else if (!toastTimer.IsEnabled)
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
            }

            var s = ScanManager.Snapshot();
            if (s.Running)
            {
                ScanPanel.Visibility = Visibility.Visible;
                ScanBar.Value = s.Percent;
                ScanText.Text = s.Text + "   " + s.Percent + "%";
            }
            else
            {
                ScanPanel.Visibility = Visibility.Collapsed;
            }

            // a scan just finished: if the user is waiting in Scan mode, show the results
            int v = ScanManager.ResultsVersion;
            if (v != seenResultsVersion)
            {
                seenResultsVersion = v;
                if (mode == PickMode.Scan && page == PickPage.Browse && !s.Running && ScanManager.GetResults().Count > 0)
                    ShowResults(null);
            }
        }

        void ShowToast(string text)
        {
            toastTimer.Stop();
            ProgressPanel.Visibility = Visibility.Visible;
            MoveBar.Visibility = Visibility.Collapsed;
            ProgressText.Text = text;
            toastTimer.Start();
        }

        // ---------- browsing ----------

        void Navigate(string path, string focusPath)
        {
            page = PickPage.Browse;
            items.Clear();
            try
            {
                if (path == null) FillPlaces(); else FillFolder(path);
            }
            catch (Exception ex)
            {
                items.Add(new PickItem { Kind = PickKind.Up, Glyph = "\uE74A", Text = "Can't open this folder", Sub = ex.Message });
            }
            current = path;
            PathText.Text = path ?? "This PC";

            int idx = focusPath == null ? -1 : items.ToList().FindIndex(i => string.Equals(i.Path, focusPath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                bool skipUp = !opt.FoldersOnly;
                idx = items.ToList().FindIndex(i => i.Selectable && i.Kind != PickKind.Action && (!skipUp || i.Kind != PickKind.Up));
                if (idx < 0) idx = items.ToList().FindIndex(i => i.Selectable);
            }
            if (idx >= 0) Select(idx);
            RefreshTitle();
            RenderHints();
        }

        List<string> SavedRoots()
        {
            return (Settings.SavedRoots ?? new List<string>()).Where(Directory.Exists).ToList();
        }

        void FillPlaces()
        {
            if (mode == PickMode.Scan && page == PickPage.Browse)
            {
                var results = ScanManager.GetResults();
                var saved = SavedRoots();
                if (results.Count > 0 || saved.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "SCAN" });
                if (results.Count > 0)
                    items.Add(new PickItem { Kind = PickKind.Action, Tag = "results", Glyph = "\uE8FD", Text = "Review scan results", Sub = results.Count + " games waiting" });
                if (saved.Count > 0)
                {
                    items.Add(new PickItem { Kind = PickKind.Action, Tag = "scanall", Glyph = "\uE72C", Text = "Scan all saved folders", Sub = saved.Count + " folder(s)" });
                    items.Add(new PickItem { Kind = PickKind.Header, Text = "SAVED FOLDERS" });
                    foreach (var r in saved)
                    {
                        var name = System.IO.Path.GetFileName(r.TrimEnd('\\'));
                        items.Add(new PickItem
                        {
                            Kind = PickKind.Saved, Glyph = "\uE8B7", Text = string.IsNullOrEmpty(name) ? r : name, Sub = r, Path = r,
                            Marked = markedScan.Contains(r, StringComparer.OrdinalIgnoreCase)
                        });
                    }
                }
            }

            items.Add(new PickItem { Kind = PickKind.Header, Text = "PLACES" });
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var places = new[]
            {
                Tuple.Create("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                Tuple.Create("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                Tuple.Create("Downloads", System.IO.Path.Combine(profile, "Downloads")),
                Tuple.Create("Start menu", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")),
                Tuple.Create("Start menu (all users)", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"))
            };
            foreach (var p in places)
            {
                if (string.IsNullOrEmpty(p.Item2) || !Directory.Exists(p.Item2)) continue;
                items.Add(new PickItem { Kind = PickKind.Place, Glyph = "\uE80F", Text = p.Item1, Sub = p.Item2, Path = p.Item2 });
            }

            items.Add(new PickItem { Kind = PickKind.Header, Text = "DRIVES" });
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    var root = d.RootDirectory.FullName;
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? root : d.VolumeLabel + " (" + root.TrimEnd('\\') + ")";
                    items.Add(new PickItem { Kind = PickKind.Drive, Glyph = "\uEDA2", Text = label, Sub = FormatSize(d.AvailableFreeSpace) + " free", Path = root });
                }
                catch { }
            }
        }

        void FillFolder(string path)
        {
            if (opt.FoldersOnly)
                items.Add(new PickItem { Kind = PickKind.Use, Glyph = "\uE73E", Text = "Use this folder", Sub = path, Path = path });
            if (mode == PickMode.Scan)
                items.Add(new PickItem { Kind = PickKind.Action, Tag = "scanhere", Glyph = "\uE721", Text = "Scan this folder", Sub = path });

            string parent = null;
            try { parent = Directory.GetParent(path)?.FullName; } catch { }
            items.Add(new PickItem { Kind = PickKind.Up, Glyph = "\uE74A", Text = "Up a folder", Sub = parent ?? "Places and drives", Path = parent });

            var dir = new DirectoryInfo(path);
            var dirs = dir.GetDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).ToList();
            dirs = SortDirs(dirs).Take(MaxFolders).ToList();

            if (mode == PickMode.Scan || opt.FoldersOnly)
            {
                foreach (var d in dirs)
                    items.Add(new PickItem
                    {
                        Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName,
                        Marked = mode == PickMode.Scan && markedScan.Contains(d.FullName, StringComparer.OrdinalIgnoreCase)
                    });
                return;
            }

            var exts = mode == PickMode.Move ? new string[0] : (opt.Extensions ?? new string[0]);
            var files = dir.GetFiles()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                            (exts.Length == 0 || exts.Contains(f.Extension.ToLowerInvariant()))).ToList();
            files = SortFiles(files).Take(MaxFiles).ToList();

            if (dirs.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FOLDERS" });
            foreach (var d in dirs)
                items.Add(new PickItem
                {
                    Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName,
                    Marked = mode == PickMode.Move && markedMove.Contains(d.FullName, StringComparer.OrdinalIgnoreCase)
                });
            if (files.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FILES" });
            foreach (var f in files)
                items.Add(new PickItem
                {
                    Kind = PickKind.File, Glyph = "\uE7FC", Text = f.Name,
                    Sub = f.LastWriteTime.ToString("d MMM yyyy, HH:mm") + "  ·  " + FormatSize(f.Length), Path = f.FullName,
                    Marked = Marks.Contains(f.FullName, StringComparer.OrdinalIgnoreCase)
                });
        }

        static IEnumerable<DirectoryInfo> SortDirs(List<DirectoryInfo> dirs)
        {
            IEnumerable<DirectoryInfo> sorted = Settings.SortBy == 1
                ? dirs.OrderBy(d => d.LastWriteTimeUtc)
                : dirs.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase); // folders have no size: name
            return Settings.SortDescending ? sorted.Reverse() : sorted;
        }

        static IEnumerable<FileInfo> SortFiles(List<FileInfo> files)
        {
            IEnumerable<FileInfo> sorted;
            switch (Settings.SortBy)
            {
                case 1: sorted = files.OrderBy(f => f.LastWriteTimeUtc); break;
                case 2: sorted = files.OrderBy(f => f.Length); break;
                default: sorted = files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase); break;
            }
            return Settings.SortDescending ? sorted.Reverse() : sorted;
        }

        // ---------- selection ----------

        void Move(int delta)
        {
            if (items.Count == 0) return;
            int dir = Math.Sign(delta);
            int i = List.SelectedIndex;
            if (i < 0) i = dir > 0 ? -1 : items.Count;
            int target = Math.Max(0, Math.Min(items.Count - 1, i + delta));

            int t = target;
            while (t >= 0 && t < items.Count && !items[t].Selectable) t += dir;
            if (t < 0 || t >= items.Count)
            {
                t = target;
                while (t >= 0 && t < items.Count && !items[t].Selectable) t -= dir;
            }
            if (t >= 0 && t < items.Count) Select(t);
        }

        void Select(int index)
        {
            List.SelectedIndex = index;
            List.ScrollIntoView(items[index]);
        }

        static string FormatSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int n = 0;
            while (v >= 1024 && n < u.Length - 1) { v /= 1024; n++; }
            return v.ToString(n == 0 ? "0" : "0.#") + " " + u[n];
        }
    }
}
