using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickAdd
{
    // Simple keyboard/controller-friendly file browser (no Windows file dialog needed).
    internal class FileBrowserView : UserControl
    {
        enum Kind { Drive, Folder, File, Up }

        class Entry
        {
            public string Label { get; set; }
            public string Path { get; set; }
            public Kind Kind { get; set; }
        }

        static readonly string[] Exts = { ".exe", ".lnk", ".bat", ".cmd" };

        readonly Window window;
        readonly QuickAddPlugin plugin;
        readonly ListBox list = new ListBox();
        readonly TextBlock header = new TextBlock();
        string current;

        public FileBrowserView(Window window, QuickAddPlugin plugin)
        {
            this.window = window;
            this.plugin = plugin;

            var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(20, 24, 28)) };

            header.Foreground = Brushes.White;
            header.FontSize = 22;
            header.Margin = new Thickness(24, 16, 24, 8);
            header.TextTrimming = TextTrimming.CharacterEllipsis;
            DockPanel.SetDock(header, Dock.Top);

            var hint = new TextBlock
            {
                Text = "A / Enter: open or add      B / Esc: back",
                Foreground = Brushes.Gray,
                FontSize = 16,
                Margin = new Thickness(24, 8, 24, 16)
            };
            DockPanel.SetDock(hint, Dock.Bottom);

            var blue = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            list.Resources[SystemColors.HighlightBrushKey] = blue;
            list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = blue;
            list.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
            list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Brushes.White;
            list.ItemContainerStyle = new Style(typeof(ListBoxItem))
            {
                Setters = { new Setter(Control.PaddingProperty, new Thickness(12, 10, 12, 10)) }
            };
            list.Background = Brushes.Transparent;
            list.BorderThickness = new Thickness(0);
            list.Foreground = Brushes.White;
            list.FontSize = 24;
            list.Margin = new Thickness(16, 0, 16, 0);
            list.DisplayMemberPath = "Label";
            list.PreviewKeyDown += OnKey;
            list.MouseDoubleClick += (s, e) => Activate();

            root.Children.Add(header);
            root.Children.Add(hint);
            root.Children.Add(list);
            Content = root;

            Navigate(null, null);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Activate(); e.Handled = true; }
            else if (e.Key == Key.Escape || e.Key == Key.Back) { GoBack(); e.Handled = true; }
        }

        void GoBack()
        {
            if (current == null) { window.Close(); return; }
            var parent = Directory.GetParent(current)?.FullName; // null at a drive root
            Navigate(parent, current);
        }

        void Activate()
        {
            var entry = list.SelectedItem as Entry;
            if (entry == null) return;
            switch (entry.Kind)
            {
                case Kind.Up: GoBack(); break;
                case Kind.Drive:
                case Kind.Folder: Navigate(entry.Path, null); break;
                case Kind.File:
                    plugin.AddFromPath(entry.Path);
                    FocusSelected();
                    break;
            }
        }

        void Navigate(string path, string select)
        {
            var items = new List<Entry>();

            if (path == null)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        items.Add(new Entry { Kind = Kind.Drive, Path = d.RootDirectory.FullName, Label = "💾  " + d.Name + "  " + d.VolumeLabel });
                    }
                    catch { }
                }
                AddIfExists(items, "🖥  Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                AddIfExists(items, "⬇  Downloads", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                AddIfExists(items, "📋  Start menu (you)", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"));
                AddIfExists(items, "📋  Start menu (all users)", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"));
            }
            else
            {
                items.Add(new Entry { Kind = Kind.Up, Path = null, Label = "⬆  .." });
                try
                {
                    foreach (var d in Directory.GetDirectories(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var attr = File.GetAttributes(d);
                            if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            items.Add(new Entry { Kind = Kind.Folder, Path = d, Label = "📁  " + System.IO.Path.GetFileName(d) });
                        }
                        catch { }
                    }
                    foreach (var f in Directory.GetFiles(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var ext = System.IO.Path.GetExtension(f);
                        if (!Exts.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                        items.Add(new Entry { Kind = Kind.File, Path = f, Label = "🎮  " + System.IO.Path.GetFileName(f) });
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            current = path;
            header.Text = path ?? "This PC";
            list.ItemsSource = items;

            var idx = select == null ? -1 : items.FindIndex(i => string.Equals(i.Path, select, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = (path != null && items.Count > 1) ? 1 : 0;
            list.SelectedIndex = idx;
            FocusSelected();
        }

        static void AddIfExists(List<Entry> items, string label, string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                items.Add(new Entry { Kind = Kind.Folder, Path = path, Label = label });
        }

        void FocusSelected()
        {
            list.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (list.SelectedItem == null) return;
                list.ScrollIntoView(list.SelectedItem);
                (list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem)?.Focus();
            }), DispatcherPriority.Loaded);
        }
    }
}
