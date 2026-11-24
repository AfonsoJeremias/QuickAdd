using Playnite.SDK;
using Playnite.SDK.Models;
using QuickAdd.Services;
using QuickAdd.UI;
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
    /// <summary>One run of "Add a game or app": pick files, add each one to the library, then fetch metadata.</summary>
    internal sealed class AddSession
    {
        static readonly string[] Extensions = { ".exe", ".lnk", ".bat", ".cmd" };

        readonly IPlayniteAPI api;
        readonly string dataDir;

        public AddSession(IPlayniteAPI api, string dataDir) { this.api = api; this.dataDir = dataDir; }

        public void Run()
        {
            PickerService.Configure(api, dataDir); // idempotent safety net
            LibraryIndex.Refresh(api);

            var paths = PickerService.PickFiles(api, "Quick Add", Extensions, null, true, true, true, true);
            if (paths == null || paths.Count == 0) return;

            var settings = AppSettings.Current;
            var added = new List<Game>();
            for (int i = 0; i < paths.Count; i++)
            {
                var game = AddOne(paths[i], i + 1, paths.Count, settings.AskNamesAdd);
                if (game != null) added.Add(game);
            }
            if (added.Count == 0) return;

            Info(added.Count == 1 ? "Quick Add: 1 game added." : "Quick Add: " + added.Count + " games added.");
            if (settings.DownloadMetadata && !ScanService.StartMetadata(added, settings))
                Info("Quick Add: metadata download skipped, another job is still running.");
        }

        void Info(string text)
        {
            api.Notifications.Add(new NotificationMessage(Guid.NewGuid().ToString(), text, NotificationType.Info));
        }

        Game AddOne(string path, int index, int total, bool askName)
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

                // the same executable can't be in the library twice
                if (LibraryIndex.Contains(target) || LibraryIndex.Contains(path))
                {
                    api.Dialogs.ShowMessage(
                        "This executable is already present in the library and can't be added again:\n" + Path.GetFileName(target),
                        "Quick Add");
                    return null;
                }

                var dir = string.IsNullOrEmpty(workDir) ? Path.GetDirectoryName(target) : workDir;
                var name = SuggestName(path, target);
                if (askName)
                {
                    var message = total > 1 ? "Name of the game / app (" + index + " of " + total + "):" : "Name of the game / app:";
                    var input = api.Dialogs.SelectString(message, "Add game", name);
                    if (!input.Result || string.IsNullOrWhiteSpace(input.SelectedString)) return null; // Cancel skips this one
                    name = input.SelectedString.Trim();
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

                var pc = api.Database.Platforms.FirstOrDefault(p => p.SpecificationId == "pc_windows");
                if (pc != null) game.PlatformIds = new List<Guid> { pc.Id };

                var icon = IconService.ImportIcon(api, dataDir, target, game.Id);
                if (!string.IsNullOrEmpty(icon)) game.Icon = icon;

                api.Database.Games.Add(game);
                LibraryIndex.Add(target);
                return game;
            }
            catch (Exception ex)
            {
                api.Dialogs.ShowErrorMessage(ex.Message, "Quick Add");
                return null;
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
