using Playnite.SDK;
using Playnite.SDK.Events;
using QuickAdd.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace QuickAdd.UI
{
    /// <summary>
    /// Entry point for picking folders / files. Desktop: native Windows dialogs. Fullscreen: PickerView in a Playnite
    /// window that covers the Playnite window, driven by OnControllerButtonStateChanged.
    /// </summary>
    public static class PickerService
    {
        static PickerView active;

        public static void Configure(IPlayniteAPI api, string dataDirectory)
        {
            MoveService.Init(api);
            ScanService.Init(api, dataDirectory);
            AppSettings.Load(dataDirectory); // no-op after the first call
        }

        public static string PickFolder(IPlayniteAPI api, string title, string startPath = null)
        {
            if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                try
                {
                    var owner = api.Dialogs.GetCurrentAppWindow();
                    var handle = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                    return FolderDialog.Show(handle, title, startPath);
                }
                catch { return api.Dialogs.SelectFolder(); }
            }
            return Show(api, new PickerOptions { Title = title, FoldersOnly = true, StartPath = startPath })?.FirstOrDefault();
        }

        public static List<string> PickFiles(IPlayniteAPI api, string title, string[] extensions, string startPath = null,
            bool allowMultiple = true, bool allowMove = false, bool allowScan = false, bool allowRename = false)
        {
            extensions = extensions ?? new string[0];
            if (api.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                var filter = extensions.Length == 0 ? "All files|*.*" : "Supported files|" + string.Join(";", extensions.Select(e => "*" + e));
                if (allowMultiple) return api.Dialogs.SelectFiles(filter);
                var one = api.Dialogs.SelectFile(filter);
                return string.IsNullOrEmpty(one) ? null : new List<string> { one };
            }

            var s = AppSettings.Current;
            if (string.IsNullOrEmpty(startPath) && s.RememberLastFolder) startPath = s.LastFolder;

            var result = Show(api, new PickerOptions
            {
                Title = title, AllowMultiple = allowMultiple, AllowMove = allowMove, AllowScan = allowScan, AllowRename = allowRename,
                Extensions = extensions, StartPath = startPath
            });
            return result == null ? null : result.ToList();
        }

        public static void RouteController(ControllerInput button, bool pressed)
        {
            var view = active;
            if (view == null) return;
            view.Dispatcher.BeginInvoke(new Action(() => view.ViewModel.Input.Pad(button, pressed)));
        }

        static IList<string> Show(IPlayniteAPI api, PickerOptions options)
        {
            LibraryIndex.Refresh(api);

            // delegates the pages use (they never touch the Playnite API directly)
            options.PromptText = (message, caption, defaultText) =>
            {
                var r = api.Dialogs.SelectString(message, caption, defaultText ?? "");
                return r.Result ? r.SelectedString : null;
            };
            options.IsInLibrary = LibraryIndex.Contains;
            options.UpdateLibrary = moves => LibraryPathService.Update(api, moves);
            options.Notify = text => Notifier.Send(api, text, NotificationType.Info);
            options.MetadataSources = () => MetadataService.SourceNames(api);

            IList<string> result = null;
            var viewModel = new PickerViewModel(options);
            var view = new PickerView(viewModel);
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

            CoverOwner(window, owner);
            window.Loaded += (s, e) => { CoverOwner(window, owner); window.Activate(); view.Focus(); };

            viewModel.Done += paths => { result = paths; window.Close(); };
            window.Closed += (s, e) => { if (active == view) active = null; };

            active = view;
            window.ShowDialog();
            AppSettings.Current.Save();
            return result;
        }

        // cover exactly the Playnite window, in DIPs, whatever the DPI
        static void CoverOwner(Window window, Window owner)
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
