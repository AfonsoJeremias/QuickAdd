using FullscreenKit;
using Playnite.SDK;
using Playnite.SDK.Events;
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
        static readonly string[] Exts = { ".exe", ".lnk", ".bat", ".cmd" };

        public override Guid Id { get; } = new Guid("c1a5e0d2-7b3f-4e6a-9d18-5f2b8a4c7e91");

        public QuickAddPlugin(IPlayniteAPI api) : base(api) { }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Add a game or app...",
                MenuSection = "Quick Add",
                Action = a => AddLoop()
            };
        }

        // Fullscreen controller input goes straight to the picker (no virtual cursor).
        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            PickerHost.RouteController(args.Button, args.State == ControllerInputState.Pressed);
        }

        void AddLoop()
        {
            while (true)
            {
                var path = PickerHost.PickFile(PlayniteApi, "Add a game or app", Exts);
                if (string.IsNullOrEmpty(path)) return;
                if (!AddFromPath(path)) return;
                var again = PlayniteApi.Dialogs.ShowMessage("Added. Add another one?", "Quick Add", MessageBoxButton.YesNo);
                if (again != MessageBoxResult.Yes) return;
            }
        }

        bool AddFromPath(string path)
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
                var input = PlayniteApi.Dialogs.SelectString("Name of the game / app:", "Add game", SuggestName(path, target));
                if (!input.Result || string.IsNullOrWhiteSpace(input.SelectedString)) return false;
                var name = input.SelectedString.Trim();

                var exists = PlayniteApi.Database.Games.Any(g => g.GameActions != null &&
                    g.GameActions.Any(x => string.Equals(x.Path, target, StringComparison.OrdinalIgnoreCase)));
                if (exists && PlayniteApi.Dialogs.ShowMessage(
                        "This file is already in your library. Add it again?", "Quick Add",
                        MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    return false;
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
                            Type = GameActionType.File, Name = name, Path = target,
                            Arguments = args, WorkingDir = dir, IsPlayAction = true
                        }
                    }
                };

                var pc = PlayniteApi.Database.Platforms.FirstOrDefault(p => p.SpecificationId == "pc_windows");
                if (pc != null) game.PlatformIds = new List<Guid> { pc.Id };

                PlayniteApi.Database.Games.Add(game);
                return true;
            }
            catch (Exception ex)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "Quick Add");
                return false;
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

        static bool ResolveShortcut(string lnk, out string target, out string args, out string workDir)
        {
            target = args = workDir = null;
            try
            {
                var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                string Get(string p) => sc.GetType().InvokeMember(p, BindingFlags.GetProperty, null, sc, null) as string;
                target = Get("TargetPath"); args = Get("Arguments"); workDir = Get("WorkingDirectory");
                return true;
            }
            catch { return false; }
        }
    }
}
