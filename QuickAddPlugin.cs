using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace QuickAdd
{
    public class QuickAddPlugin : GenericPlugin
    {
        public override Guid Id { get; } = new Guid("c1a5e0d2-7b3f-4e6a-9d18-5f2b8a4c7e91");

        public QuickAddPlugin(IPlayniteAPI api) : base(api) { }

        // Shows up in Playnite's menu in desktop AND fullscreen (under "Extensions").
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Add a game or app...",
                MenuSection = "Quick Add",
                Action = a => OpenBrowser()
            };
        }

        void OpenBrowser()
        {
            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });
            window.Title = "Add a game or app";
            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                window.WindowState = WindowState.Maximized;
            }
            else
            {
                window.Width = 900;
                window.Height = 650;
            }
            window.Content = new FileBrowserView(window, this);
            window.ShowDialog();
        }

        internal void AddFromPath(string path)
        {
            try
            {
                string target = path, args = null, workDir = null;
                if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    if (ResolveShortcut(path, out var t, out var a, out var w) && !string.IsNullOrEmpty(t) && File.Exists(t))
                    {
                        target = t; args = a; workDir = w;
                    }
                }

                var dir = string.IsNullOrEmpty(workDir) ? Path.GetDirectoryName(target) : workDir;
                var suggested = SuggestName(path, target);

                // Playnite's own dialog: has the on-screen keyboard in fullscreen.
                var input = PlayniteApi.Dialogs.SelectString("Name of the game / app:", "Add game", suggested);
                if (!input.Result || string.IsNullOrWhiteSpace(input.SelectedString)) return;
                var name = input.SelectedString.Trim();

                var exists = PlayniteApi.Database.Games.Any(g => g.GameActions != null &&
                    g.GameActions.Any(x => string.Equals(x.Path, target, StringComparison.OrdinalIgnoreCase)));
                if (exists && PlayniteApi.Dialogs.ShowMessage(
                        "This file is already in your library. Add it again?", "Quick Add",
                        MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return;
                }

                var game = new Game(name)
                {
                    IsInstalled = true,
                    InstallDirectory = dir,
                    Added = DateTime.Now,
                    GameActions = new ObservableCollection<GameAction>
                    {
                        new GameAction
                        {
                            Type = GameActionType.File,
                            Name = name,
                            Path = target,
                            Arguments = args,
                            WorkingDir = dir,
                            IsPlayAction = true
                        }
                    }
                };

                var pc = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.SpecificationId == "pc_windows");
                if (pc != null) game.PlatformIds = new List<Guid> { pc.Id };

                PlayniteApi.Database.Games.Add(game);
                PlayniteApi.Dialogs.ShowMessage($"\"{name}\" was added to your library.", "Quick Add");
            }
            catch (Exception ex)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Quick Add");
            }
        }

        static string SuggestName(string path, string target)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileNameWithoutExtension(path);
                var product = FileVersionInfo.GetVersionInfo(target).ProductName;
                if (!string.IsNullOrWhiteSpace(product)) return product.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(target);
        }

        // WScript.Shell via reflection (no COM reference needed).
        static bool ResolveShortcut(string lnk, out string target, out string args, out string workDir)
        {
            target = args = workDir = null;
            try
            {
                var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                string Get(string p) => sc.GetType().InvokeMember(p, BindingFlags.GetProperty, null, sc, null) as string;
                target = Get("TargetPath");
                args = Get("Arguments");
                workDir = Get("WorkingDirectory");
                return true;
            }
            catch { return false; }
        }
    }
}
