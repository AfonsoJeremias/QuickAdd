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
        public bool AllowMove { get; set; }
        public bool AllowRename { get; set; }
        /// <summary>Add mode only. Lower-case with dot, e.g. ".exe". Empty = all files.</summary>
        public string[] Extensions { get; set; } = new string[0];
        public string StartPath { get; set; }

        /// <summary>(message, caption, default) -> text, or null if cancelled.</summary>
        public Func<string, string, string, string> PromptText { get; set; }
        /// <summary>true if the executable is already used by a game in the library.</summary>
        public Func<string, bool> IsInLibrary { get; set; }
        /// <summary>Fixes library paths after files were renamed: returns the number of games changed.</summary>
        public Func<IList<KeyValuePair<string, string>>, int> UpdateLibrary { get; set; }
        public Action<string> Notify { get; set; }
        public Func<List<string>> MetadataSources { get; set; }
    }

    public enum PickKind { Header, Place, Drive, Up, Use, Folder, File, Option }
    public enum PickMode { Add, Move, Rename }
    public enum PickPage { Browse, Options }

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
        public bool Dim { get; set; }
        public bool Selectable { get { return Kind != PickKind.Header; } }
        public double TextSize { get { return Kind == PickKind.Header ? 14 : 22; } }
        public Brush TextBrush { get { return Kind == PickKind.Header ? Brushes.Gray : Dim ? Brushes.DarkGray : Brushes.White; } }
        public Visibility SubVisibility { get { return string.IsNullOrEmpty(Sub) ? Visibility.Collapsed : Visibility.Visible; } }

        public bool Marked
        {
            get { return marked; }
            set { marked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkVisibility))); }
        }
        public Visibility MarkVisibility { get { return marked ? Visibility.Visible : Visibility.Collapsed; } }
    }

    /// <summary>
    /// Controller-driven browser with three modes (switch with left/right): Add, Move and Rename.
    /// Select = options of the current mode.
    /// </summary>
    public partial class FolderPickerView : UserControl
    {
        const int MaxFolders = 1000, MaxFiles = 500, MaxFilesBig = 3000;
        const int DuplicateWindowMs = 200, StickCooldownMs = 300;
        const string InLibraryMessage = "This executable is already in your library - it can't be added again.";

        static readonly Brush AddColor = new SolidColorBrush(Color.FromRgb(0x2F, 0x7D, 0xD1));
        static readonly Brush MoveColor = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E));
        static readonly Brush RenameColor = new SolidColorBrush(Color.FromRgb(0x9B, 0x5D, 0xD1));
        static readonly Brush KeyCapColor = new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x38));
        static readonly Brush HintColor = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB0));
        static readonly Dictionary<string, Brush> FaceColors = new Dictionary<string, Brush>
        {
            { "A", new SolidColorBrush(Color.FromRgb(0x3B, 0xA5, 0x5D)) },
            { "B", new SolidColorBrush(Color.FromRgb(0xE0, 0x48, 0x48)) },
            { "X", new SolidColorBrush(Color.FromRgb(0x2F, 0x7D, 0xD1)) },
            { "Y", new SolidColorBrush(Color.FromRgb(0xD9, 0xA8, 0x1E)) }
        };

        /// <summary>Raised with the chosen paths (one entry for single picks); null = cancelled.</summary>
        public event Action<IList<string>> Done;

        readonly PickerOptions opt;
        readonly ObservableCollection<PickItem> items = new ObservableCollection<PickItem>();
        readonly List<string> markedAdd = new List<string>();
        readonly List<string> markedMove = new List<string>();
        readonly List<string> markedRename = new List<string>();
        readonly List<PickMode> modes = new List<PickMode>();
        readonly DispatcherTimer repeat = new DispatcherTimer();
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        int repeatDir;
        PickMode mode = PickMode.Add;
        PickPage page = PickPage.Browse;
        string current; // null = places + drives
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
                    case PickMode.Rename: return markedRename;
                    default: return markedAdd;
                }
            }
        }

        static QuickSettings Settings { get { return QuickSettings.Current; } }

        public FolderPickerView(PickerOptions options)
        {
            InitializeComponent();
            opt = options ?? new PickerOptions();
            if (!opt.AllowMultiple || opt.FoldersOnly) { opt.AllowMove = false; opt.AllowRename = false; }

            modes.Add(PickMode.Add);
            if (opt.AllowMove) modes.Add(PickMode.Move);
            if (opt.AllowRename) modes.Add(PickMode.Rename);

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
                MoveManager.Changed -= OnJobsChanged; MoveManager.Changed += OnJobsChanged;
                MetadataManager.Changed -= OnJobsChanged; MetadataManager.Changed += OnJobsChanged;
                UpdateProgress();
            };
            Unloaded += (s, e) => { repeat.Stop(); Unsubscribe(); };

            var start = opt.StartPath;
            Navigate(!string.IsNullOrEmpty(start) && Directory.Exists(start) ? start : null, null);
        }

        void Unsubscribe()
        {
            MoveManager.Changed -= OnJobsChanged;
            MetadataManager.Changed -= OnJobsChanged;
        }

        /// <summary>Stops a held direction (used when Playnite's on-screen keyboard takes over the pad).</summary>
        public void StopRepeat() { repeat.Stop(); }

        // ---------- input ----------

        // Playnite may turn pad presses into key presses as well: the same action from the other source within a
        // short window is the same press and is ignored.
        bool IsDuplicate(string action, bool fromPad)
        {
            int now = Environment.TickCount;
            bool dup = lastAction == action && lastFromPad != fromPad && unchecked(now - lastActionTick) < DuplicateWindowMs;
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
            else if (repeatDir == dir) repeat.Stop();
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
            }
        }

        // ---------- actions ----------

        void Activate()
        {
            var it = List.SelectedItem as PickItem;
            if (it == null || !it.Selectable) return;
            if (page == PickPage.Options) { ChangeOption(it); return; }

            switch (it.Kind)
            {
                case PickKind.Use: Finish(new List<string> { it.Path }); break;
                case PickKind.File:
                    if (mode != PickMode.Add) return; // Move/Rename only mark (X) or act with Start
                    if (it.Tag == "inlib") { ShowToast(InLibraryMessage); return; }
                    var result = new List<string>(markedAdd);
                    if (!result.Contains(it.Path, StringComparer.OrdinalIgnoreCase)) result.Add(it.Path);
                    Finish(result);
                    break;
                case PickKind.Up: GoBack(); break;
                default: Navigate(it.Path, null); break; // Place, Drive, Folder
            }
        }

        // X: folder mode = use folder; otherwise mark for the current mode
        void MarkOrUse()
        {
            if (page != PickPage.Browse) return;
            var it = List.SelectedItem as PickItem;

            if (opt.FoldersOnly)
            {
                if (current != null) Finish(new List<string> { current });
                return;
            }
            if (it == null) return;

            switch (mode)
            {
                case PickMode.Add:
                    if (!opt.AllowMultiple || it.Kind != PickKind.File) return;
                    if (it.Tag == "inlib") { ShowToast(InLibraryMessage); return; }
                    break;
                default:
                    if (it.Kind != PickKind.Folder && it.Kind != PickKind.File) return;
                    break;
            }

            var list = Marks;
            int idx = list.FindIndex(p => string.Equals(p, it.Path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) { list.RemoveAt(idx); it.Marked = false; }
            else { list.Add(it.Path); it.Marked = true; }
            RefreshTitle();
        }

        // Start: folder mode = use folder; Add = add marked; Move = move here; Rename = rename
        void Confirm()
        {
            if (page != PickPage.Browse) return;

            if (opt.FoldersOnly)
            {
                if (current != null) Finish(new List<string> { current });
                return;
            }
            if (mode == PickMode.Move) { StartMove(); return; }
            if (mode == PickMode.Rename) { StartRename(); return; }
            if (!opt.AllowMultiple) return;

            if (markedAdd.Count > 0) { Finish(new List<string>(markedAdd)); return; }
            var it = List.SelectedItem as PickItem;
            if (it != null && it.Kind == PickKind.File)
            {
                if (it.Tag == "inlib") { ShowToast(InLibraryMessage); return; }
                Finish(new List<string> { it.Path });
            }
        }

        void YAction()
        {
            if (page == PickPage.Browse) Navigate(null, current);
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
            if (modes.Count < 2 || page == PickPage.Options) return;
            int i = modes.IndexOf(mode);
            mode = modes[(i + dir + modes.Count) % modes.Count];
            var focus = (List.SelectedItem as PickItem)?.Path;
            UpdateModeUi();
            Navigate(current, focus);
        }

        void GoBack()
        {
            if (page == PickPage.Options) { CloseOptions(); return; }

            if (current == null) { Finish(null); return; }
            string parent = null;
            try { parent = Directory.GetParent(current)?.FullName; } catch { }
            Navigate(parent, current);
        }

        void Finish(IList<string> result)
        {
            repeat.Stop();
            Unsubscribe();
            if (Settings.RememberLastFolder && current != null) Settings.LastFolder = current;
            Done?.Invoke(result);
        }

        // ---------- tabs, title, button prompts ----------

        void UpdateModeUi()
        {
            RenderTabs();
            RefreshTitle();
            RenderHints();
        }

        static Brush ColorOf(PickMode m)
        {
            switch (m)
            {
                case PickMode.Move: return MoveColor;
                case PickMode.Rename: return RenameColor;
                default: return AddColor;
            }
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
                TabsPanel.Children.Add(new Border
                {
                    Background = on ? ColorOf(m) : KeyCapColor,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(16, 6, 16, 6),
                    Margin = new Thickness(3, 0, 3, 0),
                    Child = new TextBlock
                    {
                        Text = m.ToString().ToUpperInvariant(), FontSize = 15, FontWeight = FontWeights.Bold,
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
            if (page == PickPage.Options) t += "   ·   Options";
            else
            {
                int n = Marks.Count;
                if (n > 0) t += "   ·   " + n + (mode == PickMode.Move ? " to move" : mode == PickMode.Rename ? " to rename" : " marked");
            }
            TitleText.Text = t;
            if (page == PickPage.Browse && mode == PickMode.Rename) RenderHints(); // Start's label depends on the marks
        }

        void RenderHints()
        {
            var h = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, l) => h.Add(new KeyValuePair<string, string>(k, l));
            bool tabs = modes.Count > 1;

            if (opt.FoldersOnly)
            {
                add("A", "Open"); add("X", "Use this folder"); add("Y", "Places / drives"); add("B", "Back");
                add("Start", "Use this folder");
            }
            else if (page == PickPage.Options)
            {
                add("A", "Change"); add("B", "Close"); add("Select", "Close");
            }
            else if (!opt.AllowMultiple)
            {
                add("A", "Open / select"); add("Y", "Places / drives"); add("B", "Back");
            }
            else
            {
                switch (mode)
                {
                    case PickMode.Move:
                        add("A", "Open"); add("X", "Mark to move"); add("Y", "Places / drives"); add("B", "Back");
                        add("Start", "Move here");
                        break;
                    case PickMode.Rename:
                        add("A", "Open"); add("X", "Mark"); add("Y", "Places / drives"); add("B", "Back");
                        add("Start", markedRename.Count > 0 ? "Rename marked" : "Rename highlighted");
                        break;
                    default:
                        add("A", "Open / add"); add("X", "Mark"); add("Y", "Places / drives"); add("B", "Back");
                        add("Start", "Add marked");
                        break;
                }
                add("Select", "Options");
                if (tabs) add("◀ ▶", "Switch mode");
            }

            HintPanel.Children.Clear();
            foreach (var p in h) HintPanel.Children.Add(HintCell(p.Key, p.Value));
        }

        // fixed-width key column so every label lines up
        static FrameworkElement HintCell(string key, string label)
        {
            Border cap;
            Brush face;
            if (FaceColors.TryGetValue(key, out face))
            {
                cap = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = face,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = key, FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
            }
            else
            {
                cap = new Border
                {
                    MinWidth = 62, Height = 28, CornerRadius = new CornerRadius(7), Background = KeyCapColor,
                    Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = key, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
            }

            var keyColumn = new Grid { Width = 80 };
            keyColumn.Children.Add(cap);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(keyColumn);
            row.Children.Add(new TextBlock { Text = label, Foreground = HintColor, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 16, 0) });
            return row;
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
            else if (!toastTimer.IsEnabled) ProgressPanel.Visibility = Visibility.Collapsed;

            var s = MetadataManager.Snapshot();
            if (s.Running)
            {
                JobPanel.Visibility = Visibility.Visible;
                JobBar.Value = s.Percent;
                JobText.Text = s.Text + "   " + s.Percent + "%";
            }
            else JobPanel.Visibility = Visibility.Collapsed;
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
            try { if (path == null) FillPlaces(); else FillFolder(path); }
            catch (Exception ex)
            {
                items.Add(new PickItem { Kind = PickKind.Up, Glyph = "\uE74A", Text = "Can't open this folder", Sub = ex.Message });
            }
            current = path;
            if (path != null) Settings.LastFolder = path;
            PathText.Text = path ?? "This PC";

            int idx = focusPath == null ? -1 : items.ToList().FindIndex(i => string.Equals(i.Path, focusPath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                bool skipUp = !opt.FoldersOnly;
                idx = items.ToList().FindIndex(i => i.Selectable && (!skipUp || i.Kind != PickKind.Up));
                if (idx < 0) idx = items.ToList().FindIndex(i => i.Selectable);
            }
            if (idx >= 0) Select(idx);
            RefreshTitle();
            RenderHints();
        }

        void FillPlaces()
        {
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

            string parent = null;
            try { parent = Directory.GetParent(path)?.FullName; } catch { }
            items.Add(new PickItem { Kind = PickKind.Up, Glyph = "\uE74A", Text = "Up a folder", Sub = parent ?? "Places and drives", Path = parent });

            bool hidden = Settings.ShowHidden;
            var dir = new DirectoryInfo(path);
            var dirs = dir.GetDirectories().Where(d => hidden || (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0).ToList();
            dirs = SortDirs(dirs).Take(MaxFolders).ToList();

            if (opt.FoldersOnly)
            {
                foreach (var d in dirs)
                    items.Add(new PickItem { Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName });
                return;
            }

            // Move and Rename list every kind of file; only Add filters by extension
            var exts = mode == PickMode.Add ? (opt.Extensions ?? new string[0]) : new string[0];
            var files = dir.GetFiles()
                .Where(f => (hidden || (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) &&
                            (exts.Length == 0 || exts.Contains(f.Extension.ToLowerInvariant()))).ToList();
            files = SortFiles(files).Take(mode == PickMode.Add ? MaxFiles : MaxFilesBig).ToList();

            if (dirs.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FOLDERS" });
            foreach (var d in dirs)
                items.Add(new PickItem
                {
                    Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName,
                    Marked = mode != PickMode.Add && Marks.Contains(d.FullName, StringComparer.OrdinalIgnoreCase)
                });
            if (files.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FILES" });
            foreach (var f in files)
            {
                bool inLib = mode == PickMode.Add && opt.IsInLibrary != null && opt.IsInLibrary(f.FullName);
                items.Add(new PickItem
                {
                    Kind = PickKind.File, Glyph = inLib ? "\uE7BA" : "\uE7FC", Text = f.Name, Path = f.FullName,
                    Sub = f.LastWriteTime.ToString("d MMM yyyy, HH:mm") + "  ·  " + FormatSize(f.Length) + (inLib ? "  ·  already in library" : ""),
                    Tag = inLib ? "inlib" : null, Dim = inLib,
                    Marked = Marks.Contains(f.FullName, StringComparer.OrdinalIgnoreCase)
                });
            }
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
