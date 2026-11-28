using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace FullscreenKit
{
    /// <summary>
    /// One entry point for picking folders / files.
    /// Desktop: native Windows dialogs. Fullscreen: FolderPickerView in a Playnite window sized to the Playnite window,
    /// driven by OnControllerButtonStateChanged (Add / Move / Rename / Create modes).
    /// Call Configure once, and forward OnControllerButtonStateChanged to RouteController.
    /// </summary>
    public static class PickerHost
    {
        static FolderPickerView active;
        static bool dialogOpen;
        static int dialogClosedTick = unchecked(Environment.TickCount - 10000);

        public static void Configure(IPlayniteAPI api, string dataDirectory)
        {
            MoveManager.Init(api);
            QuickSettings.Load(dataDirectory); // no-op after the first call
        }

        public static string PickFolder(IPlayniteAPI api, string title, string startPath = null)
        {
            if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                try
                {
                    var owner = api.Dialogs.GetCurrentAppWindow();
                    var handle = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                    return ModernFolderDialog.Show(handle, title, startPath);
                }
                catch { return api.Dialogs.SelectFolder(); }
            }
            return ShowView(api, new PickerOptions { Title = title, FoldersOnly = true, StartPath = startPath })?.FirstOrDefault();
        }

        public static string PickFile(IPlayniteAPI api, string title, string[] extensions, string startPath = null)
        {
            return PickFiles(api, title, extensions, startPath, false)?.FirstOrDefault();
        }

        /// <summary>
        /// Fullscreen: X marks files, Start adds the marked ones; allowMove / allowRename / allowCreate enable the other modes
        /// (switch with left/right). Desktop: native multi-select file dialog.
        /// </summary>
        public static List<string> PickFiles(IPlayniteAPI api, string title, string[] extensions, string startPath = null,
            bool allowMultiple = true, bool allowMove = false, bool allowRename = false, bool allowCreate = false)
        {
            extensions = extensions ?? new string[0];
            if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                var filter = extensions.Length == 0 ? "All files|*.*" :
                    "Supported files|" + string.Join(";", extensions.Select(e => "*" + e));
                if (allowMultiple) return api.Dialogs.SelectFiles(filter);
                var one = api.Dialogs.SelectFile(filter);
                return string.IsNullOrEmpty(one) ? null : new List<string> { one };
            }

            var settings = QuickSettings.Current;
            if (string.IsNullOrEmpty(startPath) && settings.RememberLastFolder) startPath = settings.LastFolder;

            var result = ShowView(api, new PickerOptions
            {
                Title = title, FoldersOnly = false, AllowMultiple = allowMultiple,
                AllowMove = allowMove, AllowRename = allowRename, AllowCreate = allowCreate,
                Extensions = extensions, StartPath = startPath
            });
            return result == null ? null : result.ToList();
        }

        public static void RouteController(ControllerInput button, bool pressed)
        {
            // while Playnite's on-screen keyboard / dialog is open the pad belongs to it, not to the picker
            if (dialogOpen || unchecked(Environment.TickCount - dialogClosedTick) < 400) return;
            var view = active;
            if (view == null) return;
            view.Dispatcher.BeginInvoke(new Action(() => view.HandleController(button, pressed)));
        }

        static IList<string> ShowView(IPlayniteAPI api, PickerOptions options)
        {
            LibraryIndex.Refresh(api);

            // delegates the view uses (it never touches the Playnite API directly)
            options.PromptText = (message, caption, defaultText) =>
            {
                active?.StopRepeat();
                dialogOpen = true;
                try
                {
                    var r = api.Dialogs.SelectString(message, caption, defaultText ?? "");
                    return r.Result ? r.SelectedString : null;
                }
                finally { dialogOpen = false; dialogClosedTick = Environment.TickCount; }
            };
            options.IsInLibrary = LibraryIndex.Contains;
            options.UpdateLibrary = moves => LibraryPathUpdater.Update(api, moves);
            options.Notify = text => Notifier.Send(api, text, NotificationType.Info);

            IList<string> result = null;
            var view = new FolderPickerView(options);
            var owner = api.Dialogs.GetCurrentAppWindow();

            var window = api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false, ShowMaximizeButton = false, ShowCloseButton = false
            });
            window.Title = options.Title;
            window.Owner = owner;
            window.ResizeMode = ResizeMode.NoResize;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Content = view;

            ApplyOwnerBounds(window, owner);
            window.Loaded += (s, e) =>
            {
                ApplyOwnerBounds(window, owner);
                window.Activate();
                view.Focus();
            };

            view.Done += paths => { result = paths; window.Close(); };
            window.Closed += (s, e) => { if (active == view) active = null; };

            active = view;
            window.ShowDialog();
            QuickSettings.Current.Save();
            return result;
        }

        static void ApplyOwnerBounds(Window window, Window owner)
        {
            try
            {
                var source = owner != null ? PresentationSource.FromVisual(owner) : null;
                if (source == null || owner.ActualWidth < 1) { window.WindowState = WindowState.Maximized; return; }
                var topLeft = source.CompositionTarget.TransformFromDevice.Transform(owner.PointToScreen(new Point(0, 0)));
                window.WindowState = WindowState.Normal;
                window.Left = topLeft.X;
                window.Top = topLeft.Y;
                window.Width = owner.ActualWidth;
                window.Height = owner.ActualHeight;
            }
            catch { window.WindowState = WindowState.Maximized; }
        }
    }
}
