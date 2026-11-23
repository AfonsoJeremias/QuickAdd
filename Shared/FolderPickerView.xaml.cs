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
        /// <summary>File mode only: X marks files, Start confirms all marked ones.</summary>
        public bool AllowMultiple { get; set; }
        /// <summary>File mode only: Select toggles Add mode / Move mode (move files and folders in the background).</summary>
        public bool AllowMove { get; set; }
        /// <summary>Add mode only. Lower-case with dot, e.g. ".exe". Empty = all files.</summary>
        public string[] Extensions { get; set; } = new string[0];
        public string StartPath { get; set; }
    }

    public enum PickKind { Header, Place, Drive, Up, Use, Folder, File }

    public class PickItem : INotifyPropertyChanged
    {
        bool marked;
        public event PropertyChangedEventHandler PropertyChanged;

        public PickKind Kind { get; set; }
        public string Glyph { get; set; }
        public string Text { get; set; }
        public string Sub { get; set; }
        public string Path { get; set; }
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

    /// <summary>Folder / file browser driven by D-pad, stick, A/B/X/Y/Start/Select, keyboard or mouse.</summary>
    public partial class FolderPickerView : UserControl
    {
        const int MaxFolders = 1000, MaxFiles = 500;
        const int DuplicateWindowMs = 200;

        static readonly Brush AddColor = new SolidColorBrush(Color.FromRgb(0x2F, 0x7D, 0xD1));
        static readonly Brush MoveColor = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E));
        static readonly Brush KeyCapColor = new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x38));
        static readonly Brush HintColor = new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB0));

        /// <summary>Raised with the chosen paths (one entry for single picks); null = cancelled.</summary>
        public event Action<IList<string>> Done;

        readonly PickerOptions opt;
        readonly ObservableCollection<PickItem> items = new ObservableCollection<PickItem>();
        readonly List<string> markedAdd = new List<string>();
        readonly List<string> markedMove = new List<string>();
        readonly DispatcherTimer repeat = new DispatcherTimer();
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        int repeatDir;
        bool moveMode;
        string current; // null = places + drives

        string lastAction;
        int lastActionTick;
        bool lastFromPad;

        List<string> Marks { get { return moveMode ? markedMove : markedAdd; } }

        public FolderPickerView(PickerOptions options)
        {
            InitializeComponent();
            opt = options ?? new PickerOptions();
            if (opt.FoldersOnly) { opt.AllowMultiple = false; opt.AllowMove = false; }
            if (!opt.AllowMultiple) opt.AllowMove = false;

            List.ItemsSource = items;
            UpdateModeUi();

            repeat.Tick += (s, e) =>
            {
                repeat.Interval = TimeSpan.FromMilliseconds(70);
                IsDuplicate("move" + repeatDir, true);
                Move(repeatDir);
            };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); UpdateProgress(); };

            PreviewKeyDown += OnKey;
            List.MouseDoubleClick += (s, e) => Activate();
            Loaded += (s, e) =>
            {
                Focus();
                MoveManager.Changed -= OnMoveChanged;
                MoveManager.Changed += OnMoveChanged;
                UpdateProgress(); // reopening the picker while a move runs shows the bar again
            };
            Unloaded += (s, e) => { repeat.Stop(); MoveManager.Changed -= OnMoveChanged; };

            var start = opt.StartPath;
            Navigate(!string.IsNullOrEmpty(start) && Directory.Exists(start) ? start : null, null);
        }

        // ---------- input ----------

        bool IsDuplicate(string action, bool fromPad)
        {
            int now = Environment.TickCount;
            bool dup = lastAction == action && lastFromPad != fromPad &&
                       unchecked(now - lastActionTick) < DuplicateWindowMs;
            if (!dup) { lastAction = action; lastActionTick = now; lastFromPad = fromPad; }
            return dup;
        }

        /// <summary>Called by PickerHost from Playnite's OnControllerButtonStateChanged.</summary>
        public void HandleController(ControllerInput button, bool pressed)
        {
            switch (button)
            {
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp: Hold(-1, pressed); break;
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown: Hold(1, pressed); break;
                case ControllerInput.A: if (pressed && !IsDuplicate("activate", true)) Activate(); break;
                case ControllerInput.B: if (pressed && !IsDuplicate("back", true)) GoBack(); break;
                case ControllerInput.X: if (pressed && !IsDuplicate("mark", true)) MarkOrUse(); break;
                case ControllerInput.Start: if (pressed && !IsDuplicate("confirm", true)) Confirm(); break;
                case ControllerInput.Back: if (pressed && !IsDuplicate("mode", true)) ToggleMode(); break; // Select / View button
                case ControllerInput.Y: if (pressed) Navigate(null, current); break;
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
                case Key.Enter: action = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? "confirm" : "activate"; break;
                case Key.Escape:
                case Key.Back: action = "back"; break;
                case Key.Space: action = "mark"; break;
                case Key.Tab: action = "mode"; break;
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
                case "activate": Activate(); break;
                case "confirm": Confirm(); break;
                case "back": GoBack(); break;
                case "mark": MarkOrUse(); break;
                case "mode": ToggleMode(); break;
            }
        }

        // ---------- actions ----------

        void Activate()
        {
            var it = List.SelectedItem as PickItem;
            if (it == null || !it.Selectable) return;
            switch (it.Kind)
            {
                case PickKind.Use: Finish(new List<string> { it.Path }); break;
                case PickKind.File:
                    if (moveMode) return; // files are only marked (X) in Move mode
                    var result = new List<string>(markedAdd);
                    if (!result.Contains(it.Path, StringComparer.OrdinalIgnoreCase)) result.Add(it.Path);
                    Finish(result);
                    break;
                case PickKind.Up: GoBack(); break;
                default: Navigate(it.Path, null); break;
            }
        }

        // X: folder mode = use current folder; Add mode = mark a file; Move mode = mark a file or folder
        void MarkOrUse()
        {
            if (opt.FoldersOnly)
            {
                if (current != null) Finish(new List<string> { current });
                return;
            }
            var it = List.SelectedItem as PickItem;
            if (it == null) return;

            if (moveMode)
            {
                if (it.Kind != PickKind.Folder && it.Kind != PickKind.File) return;
            }
            else
            {
                if (!opt.AllowMultiple || it.Kind != PickKind.File) return;
            }

            var list = Marks;
            int idx = list.FindIndex(p => string.Equals(p, it.Path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) { list.RemoveAt(idx); it.Marked = false; }
            else { list.Add(it.Path); it.Marked = true; }
            RefreshTitle();
        }

        // Start: folder mode = use current folder; Add mode = add marked; Move mode = move marked here
        void Confirm()
        {
            if (opt.FoldersOnly)
            {
                if (current != null) Finish(new List<string> { current });
                return;
            }
            if (moveMode) { StartMove(); return; }
            if (!opt.AllowMultiple) return;

            if (markedAdd.Count > 0) { Finish(new List<string>(markedAdd)); return; }
            var it = List.SelectedItem as PickItem;
            if (it != null && it.Kind == PickKind.File) Finish(new List<string> { it.Path });
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

        void ToggleMode()
        {
            if (!opt.AllowMove) return;
            var focus = (List.SelectedItem as PickItem)?.Path;
            moveMode = !moveMode;
            UpdateModeUi();
            Navigate(current, focus); // Move mode lists every file, Add mode only the supported ones
        }

        void GoBack()
        {
            if (current == null) { Finish(null); return; }
            string parent = null;
            try { parent = Directory.GetParent(current)?.FullName; } catch { }
            Navigate(parent, current);
        }

        void Finish(IList<string> result)
        {
            repeat.Stop();
            MoveManager.Changed -= OnMoveChanged;
            Done?.Invoke(result);
        }

        // ---------- mode UI, prompts, progress ----------

        void UpdateModeUi()
        {
            ModeBadge.Visibility = opt.AllowMove ? Visibility.Visible : Visibility.Collapsed;
            ModeText.Text = moveMode ? "MOVE MODE" : "ADD MODE";
            ModeBadge.Background = moveMode ? MoveColor : AddColor;
            RefreshTitle();
            RenderHints();
        }

        void RefreshTitle()
        {
            int n = Marks.Count;
            TitleText.Text = opt.Title + (n > 0 ? "   ·   " + n + (moveMode ? " to move" : " marked") : "");
        }

        void RenderHints()
        {
            var h = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, l) => h.Add(new KeyValuePair<string, string>(k, l));

            if (opt.FoldersOnly)
            {
                add("A", "Open"); add("X", "Use this folder"); add("Start", "Use this folder");
                add("B", "Back"); add("Y", "Places / drives");
            }
            else if (moveMode)
            {
                add("A", "Open"); add("X", "Mark to move"); add("Start", "Move here");
                add("Select", "Add mode"); add("B", "Back"); add("Y", "Places / drives");
            }
            else if (opt.AllowMultiple)
            {
                add("A", "Open / add"); add("X", "Mark"); add("Start", "Add marked");
                if (opt.AllowMove) add("Select", "Move mode");
                add("B", "Back"); add("Y", "Places / drives");
            }
            else
            {
                add("A", "Open / select"); add("B", "Back"); add("Y", "Places / drives");
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

        void OnMoveChanged() { Dispatcher.BeginInvoke(new Action(UpdateProgress)); }

        void UpdateProgress()
        {
            var p = MoveManager.Snapshot();
            if (!p.Running)
            {
                if (!toastTimer.IsEnabled) ProgressPanel.Visibility = Visibility.Collapsed;
                return;
            }
            toastTimer.Stop();
            ProgressPanel.Visibility = Visibility.Visible;
            MoveBar.Visibility = Visibility.Visible;
            MoveBar.Value = p.Percent;

            var text = string.IsNullOrEmpty(p.Name) ? "Moving…" : "Moving \"" + p.Name + "\"   " + p.Percent + "%";
            if (p.Count > 1 && p.Index > 0) text += "   (" + p.Index + " of " + p.Count + ")";
            if (p.Queued > 0) text += "   +" + p.Queued + " queued";
            ProgressText.Text = text;
        }

        void ShowToast(string text)
        {
            toastTimer.Stop();
            ProgressPanel.Visibility = Visibility.Visible;
            MoveBar.Visibility = Visibility.Collapsed;
            ProgressText.Text = text;
            toastTimer.Start();
        }

        // ---------- navigation ----------

        void Navigate(string path, string focusPath)
        {
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
                idx = items.ToList().FindIndex(i => i.Selectable && (opt.FoldersOnly || i.Kind != PickKind.Up));
                if (idx < 0) idx = items.ToList().FindIndex(i => i.Selectable);
            }
            if (idx >= 0) Select(idx);
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

            var dir = new DirectoryInfo(path);
            var dirs = dir.GetDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxFolders).ToList();

            if (!opt.FoldersOnly)
            {
                var exts = moveMode ? new string[0] : (opt.Extensions ?? new string[0]);
                var files = dir.GetFiles()
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                                (exts.Length == 0 || exts.Contains(f.Extension.ToLowerInvariant())))
                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxFiles).ToList();

                if (dirs.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FOLDERS" });
                foreach (var d in dirs)
                    items.Add(new PickItem
                    {
                        Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName,
                        Marked = moveMode && markedMove.Contains(d.FullName, StringComparer.OrdinalIgnoreCase)
                    });
                if (files.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FILES" });
                foreach (var f in files)
                    items.Add(new PickItem
                    {
                        Kind = PickKind.File, Glyph = "\uE7FC", Text = f.Name,
                        Sub = f.LastWriteTime.ToString("d MMM yyyy, HH:mm"), Path = f.FullName,
                        Marked = Marks.Contains(f.FullName, StringComparer.OrdinalIgnoreCase)
                    });
            }
            else
            {
                foreach (var d in dirs)
                    items.Add(new PickItem { Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName });
            }
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
