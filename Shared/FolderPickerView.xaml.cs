using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        /// <summary>File mode only. Lower-case with dot, e.g. ".exe". Empty = all files.</summary>
        public string[] Extensions { get; set; } = new string[0];
        public string StartPath { get; set; }
    }

    public enum PickKind { Header, Place, Drive, Up, Use, Folder, File }

    public class PickItem
    {
        public PickKind Kind { get; set; }
        public string Glyph { get; set; }
        public string Text { get; set; }
        public string Sub { get; set; }
        public string Path { get; set; }
        public bool Selectable { get { return Kind != PickKind.Header; } }
        public double TextSize { get { return Kind == PickKind.Header ? 14 : 22; } }
        public Brush TextBrush { get { return Kind == PickKind.Header ? Brushes.Gray : Brushes.White; } }
        public Visibility SubVisibility { get { return string.IsNullOrEmpty(Sub) ? Visibility.Collapsed : Visibility.Visible; } }
    }

    /// <summary>Folder / file browser driven by D-pad, stick, A/B/X/Y, keyboard or mouse.</summary>
    public partial class FolderPickerView : UserControl
    {
        const int MaxFolders = 1000, MaxFiles = 500;

        public event Action<string> Done; // null = cancelled

        readonly PickerOptions opt;
        readonly ObservableCollection<PickItem> items = new ObservableCollection<PickItem>();
        readonly DispatcherTimer repeat = new DispatcherTimer();
        int repeatDir;
        int lastPadTick;
        string current; // null = places + drives

        public FolderPickerView(PickerOptions options)
        {
            InitializeComponent();
            opt = options ?? new PickerOptions();
            List.ItemsSource = items;
            TitleText.Text = opt.Title;
            HintText.Text = opt.FoldersOnly
                ? "A  Open      B  Back      X  Use this folder      Y  Places / drives"
                : "A  Open / select      B  Back      Y  Places / drives      LB RB  Page";

            repeat.Tick += (s, e) => { repeat.Interval = TimeSpan.FromMilliseconds(70); lastPadTick = Environment.TickCount; Move(repeatDir); };
            PreviewKeyDown += OnKey;
            List.MouseDoubleClick += (s, e) => Activate();
            Loaded += (s, e) => Focus();
            Unloaded += (s, e) => repeat.Stop();

            var start = opt.StartPath;
            Navigate(!string.IsNullOrEmpty(start) && Directory.Exists(start) ? start : null, null);
        }

        // ---------- input ----------

        /// <summary>Called by PickerHost from Playnite's OnControllerButtonStateChanged.</summary>
        public void HandleController(ControllerInput button, bool pressed)
        {
            lastPadTick = Environment.TickCount;
            switch (button)
            {
                case ControllerInput.DPadUp:
                case ControllerInput.LeftStickUp: Hold(-1, pressed); break;
                case ControllerInput.DPadDown:
                case ControllerInput.LeftStickDown: Hold(1, pressed); break;
                case ControllerInput.A: if (pressed) Activate(); break;
                case ControllerInput.B: if (pressed) GoBack(); break;
                case ControllerInput.X:
                case ControllerInput.Start: if (pressed && opt.FoldersOnly && current != null) Finish(current); break;
                case ControllerInput.Y: if (pressed) Navigate(null, current); break;
                case ControllerInput.LeftShoulder: if (pressed) Move(-8); break;
                case ControllerInput.RightShoulder: if (pressed) Move(8); break;
            }
        }

        void Hold(int dir, bool pressed)
        {
            if (pressed)
            {
                repeatDir = dir;
                Move(dir);
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
            // If Playnite also turns pad presses into key presses, ignore those duplicates.
            if (unchecked(Environment.TickCount - lastPadTick) < 120) { e.Handled = true; return; }

            switch (e.Key)
            {
                case Key.Up: Move(-1); break;
                case Key.Down: Move(1); break;
                case Key.PageUp: Move(-8); break;
                case Key.PageDown: Move(8); break;
                case Key.Enter: Activate(); break;
                case Key.Escape:
                case Key.Back: GoBack(); break;
                case Key.Space: if (opt.FoldersOnly && current != null) Finish(current); break;
                default: return;
            }
            e.Handled = true;
        }

        // ---------- navigation ----------

        void Activate()
        {
            var it = List.SelectedItem as PickItem;
            if (it == null || !it.Selectable) return;
            switch (it.Kind)
            {
                case PickKind.Use:
                case PickKind.File: Finish(it.Path); break;
                case PickKind.Up: GoBack(); break;
                default: Navigate(it.Path, null); break;
            }
        }

        void GoBack()
        {
            if (current == null) { Finish(null); return; }
            string parent = null;
            try { parent = Directory.GetParent(current)?.FullName; } catch { }
            Navigate(parent, current);
        }

        void Finish(string result)
        {
            repeat.Stop();
            Done?.Invoke(result);
        }

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
                // file mode: skip "Up" so the first folder/file is highlighted; folder mode: "Use this folder"
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
                var exts = opt.Extensions ?? new string[0];
                var files = dir.GetFiles()
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                                (exts.Length == 0 || exts.Contains(f.Extension.ToLowerInvariant())))
                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxFiles).ToList();

                if (dirs.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FOLDERS" });
                foreach (var d in dirs)
                    items.Add(new PickItem { Kind = PickKind.Folder, Glyph = "\uE8B7", Text = d.Name, Path = d.FullName });
                if (files.Count > 0) items.Add(new PickItem { Kind = PickKind.Header, Text = "FILES" });
                foreach (var f in files)
                    items.Add(new PickItem { Kind = PickKind.File, Glyph = "\uE7FC", Text = f.Name, Sub = f.LastWriteTime.ToString("d MMM yyyy, HH:mm"), Path = f.FullName });
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
