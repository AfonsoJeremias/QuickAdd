using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FullscreenKit
{
    public sealed class ScanCandidate
    {
        public string Name;
        public string Folder;
        public List<string> Exes = new List<string>(); // best first
        public int Main;                               // index of the Play executable
        public HashSet<int> Extra = new HashSet<int>(); // other executables that become extra game actions
        public bool Selected;
        public bool Exists;
        public string Exe { get { return Exes[Main]; } }
        public bool IsIncluded(int i) { return i == Main || Extra.Contains(i); }
        public int ActionCount { get { return 1 + Extra.Count; } }
    }

    public sealed class ScanStatus
    {
        public bool Running;
        public string Phase = "";
        public string Text = "";
        public int Percent;
    }

    internal static class Notifier
    {
        public static void Send(IPlayniteAPI api, string text, NotificationType type)
        {
            if (api == null) return;
            Action add = () => api.Notifications.Add(new NotificationMessage("QuickAdd_" + Guid.NewGuid().ToString("N"), text, type));
            try
            {
                var d = api.MainView.UIDispatcher;
                if (d != null) d.BeginInvoke(add); else add();
            }
            catch { try { add(); } catch { } }
        }
    }

    /// <summary>Scans folders for games, imports the chosen ones and downloads their metadata, on background threads.</summary>
    public static class ScanManager
    {
        sealed class ExeInfo { public string Path; public long Size; public int Level; public int Score; }
        sealed class Raw { public string Folder; public List<ExeInfo> Exes; }
        sealed class Existing { public HashSet<string> Dirs = new HashSet<string>(); public HashSet<string> Exes = new HashSet<string>(); }

        static readonly object gate = new object();
        static readonly Stopwatch throttle = Stopwatch.StartNew();
        static readonly List<ScanCandidate> results = new List<ScanCandidate>();
        static readonly HashSet<string> RootSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "$recycle.bin", "system volume information", "windows", "$windows.~bt", "recovery", "config.msi" };
        static readonly HashSet<string> ExeSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "$recycle.bin", "system volume information", "_commonredist", "commonredist", "redist", "redistributable",
          "redistributables", "directx", "vcredist", "dotnet", "dotnetfx", "installer", "__installer", "crashreporter",
          "crashpad", "eac", "easyanticheat", "battleye" };

        static IPlayniteAPI api;
        static string dataDir;
        static bool busy;
        static volatile bool cancel;
        static ScanStatus status = new ScanStatus();

        public static event Action Changed;
        public static int ResultsVersion { get; private set; }

        public static void Init(IPlayniteAPI playniteApi, string dataDirectory) { api = playniteApi; dataDir = dataDirectory; }

        public static ScanStatus Snapshot()
        {
            lock (gate) { return new ScanStatus { Running = status.Running, Phase = status.Phase, Text = status.Text, Percent = status.Percent }; }
        }

        public static List<ScanCandidate> GetResults() { lock (gate) { return results.ToList(); } }
        public static void Cancel() { cancel = true; }
        static void Raise() { try { Changed?.Invoke(); } catch { } }

        static void SetStatus(bool running, string phase, int percent, string text, bool force = true)
        {
            if (!force && throttle.ElapsedMilliseconds < 100) return;
            throttle.Restart();
            lock (gate) { status = new ScanStatus { Running = running, Phase = phase, Percent = Math.Max(0, Math.Min(100, percent)), Text = text }; }
            Raise();
        }

        static bool TryBegin()
        {
            lock (gate) { if (busy) return false; busy = true; cancel = false; return true; }
        }

        static void End()
        {
            lock (gate) { busy = false; }
            SetStatus(false, "", 0, "");
        }

        // ================= scan =================

        public static bool StartScan(IList<string> roots, ScanSettings settings)
        {
            if (!TryBegin()) return false;
            var s = settings.Clone();
            var list = roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var existing = Gather(); // caller (UI) thread
            SetStatus(true, "Scanning", 0, "Scanning…");
            Task.Factory.StartNew(() => ScanWorker(list, s, existing), TaskCreationOptions.LongRunning);
            return true;
        }

        sealed class ScanRun
        {
            public ScanSettings S;
            public Regex[] Inc, Exc;
            public Existing Ex;
            public List<string> RootKeys = new List<string>();
            public List<ScanCandidate> Found = new List<ScanCandidate>();
            public HashSet<string> Seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int SkippedExisting, RootIndex, RootCount;

            public void Report(string text, double fraction)
            {
                int pct = (int)(((RootIndex + Math.Max(0, Math.Min(1, fraction))) / Math.Max(1, RootCount)) * 100);
                SetStatus(true, "Scanning", pct, text, false);
            }
        }

        static void ScanWorker(List<string> roots, ScanSettings s, Existing existing)
        {
            var run = new ScanRun
            {
                S = s, Ex = existing, RootCount = roots.Count,
                Inc = ScanSettings.ToRegexes(s.Include, ScanSettings.DefaultInclude),
                Exc = ScanSettings.ToRegexes(s.Exclude, null)
            };
            run.RootKeys.AddRange(roots.Select(r => Key(r)).Where(k => k != null));

            try
            {
                for (int r = 0; r < roots.Count && !cancel; r++)
                {
                    run.RootIndex = r;
                    if (s.FolderPerGame) ScanUnits(run, roots[r]); else ScanExeFolders(run, roots[r]);
                }

                if (cancel) { Notifier.Send(api, "Quick Add: scan cancelled.", NotificationType.Info); }
                else
                {
                    var found = run.Found.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    lock (gate) { results.Clear(); results.AddRange(found); ResultsVersion++; }
                    var ex = run.SkippedExisting > 0 ? " (" + run.SkippedExisting + " already in your library)" : "";
                    Notifier.Send(api, found.Count > 0
                        ? "Scan finished: " + found.Count + " new game(s) found" + ex + ". Open Quick Add > Scan to review them."
                        : "Scan finished: no new games found" + ex + ".", NotificationType.Info);
                }
            }
            catch (Exception ex) { Notifier.Send(api, "Quick Add: scan failed - " + ex.Message, NotificationType.Error); }
            finally { End(); }
        }

        // "One game per subfolder". A folder that holds several games in subfolders (a collection) gives one game per
        // subfolder - the folder where each executable lives. A folder whose executable is buried in a single chain of
        // subfolders is one game at the top folder.
        static void ScanUnits(ScanRun run, string root)
        {
            var rootExes = new List<ExeInfo>();
            CollectExes(run, root, 0, 0, rootExes);
            if (rootExes.Count > 0) { AddRaw(run, root, FullExes(run, root)); return; } // the chosen folder is itself a game

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); } catch { return; }
            dirs = dirs.Where(d => !RootSkip.Contains(Path.GetFileName(d)) && !IsReparse(d))
                       .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();

            for (int i = 0; i < dirs.Length; i++)
            {
                if (cancel) return;
                run.Report("Scanning \"" + root + "\" - " + Path.GetFileName(dirs[i]) + " (" + run.Found.Count + " found)", (double)i / dirs.Length);
                foreach (var raw in FindGames(run, dirs[i], run.S.Depth, true)) AddRaw(run, raw.Folder, raw.Exes);
            }
        }

        // "Folder with the executable": every folder that directly holds an executable is a game.
        static void ScanExeFolders(ScanRun run, string root)
        {
            int visited = 0;
            var queue = new Queue<Tuple<string, int>>();
            queue.Enqueue(Tuple.Create(root, 0));
            while (queue.Count > 0 && !cancel)
            {
                var t = queue.Dequeue();
                visited++;
                run.Report("Scanning \"" + root + "\" - " + visited + " folders, " + run.Found.Count + " found", 1.0 - 1.0 / (1.0 + visited / 50.0));

                var direct = new List<ExeInfo>();
                CollectExes(run, t.Item1, 0, 0, direct);
                if (direct.Count > 0) { AddRaw(run, t.Item1, FullExes(run, t.Item1)); continue; }
                if (t.Item2 >= run.S.Depth) continue;

                string[] subs;
                try { subs = Directory.GetDirectories(t.Item1); } catch { continue; }
                foreach (var d in subs)
                {
                    var n = Path.GetFileName(d);
                    if (ExeSkip.Contains(n) || RootSkip.Contains(n) || IsReparse(d)) continue;
                    queue.Enqueue(Tuple.Create(d, t.Item2 + 1));
                }
            }
        }

        static List<Raw> FindGames(ScanRun run, string dir, int depthLeft, bool collapseSingle)
        {
            var result = new List<Raw>();
            if (cancel) return result;

            var direct = new List<ExeInfo>();
            CollectExes(run, dir, 0, 0, direct);
            if (direct.Count > 0) { result.Add(new Raw { Folder = dir, Exes = FullExes(run, dir) }); return result; }
            if (depthLeft <= 0) return result;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return result; }

            var children = new List<Raw>();
            foreach (var d in subs)
            {
                var n = Path.GetFileName(d);
                if (ExeSkip.Contains(n) || RootSkip.Contains(n) || IsReparse(d)) continue;
                children.AddRange(FindGames(run, d, depthLeft - 1, collapseSingle));
            }

            if (children.Count == 1 && collapseSingle)
                result.Add(new Raw { Folder = dir, Exes = children[0].Exes }); // one game, executable deeper inside
            else
                result.AddRange(children);                                      // several games in this folder
            return result;
        }

        static List<ExeInfo> FullExes(ScanRun run, string dir)
        {
            var list = new List<ExeInfo>();
            CollectExes(run, dir, 0, Math.Max(1, Math.Min(run.S.Depth, 2)), list);
            return list;
        }

        static void CollectExes(ScanRun run, string dir, int level, int maxLevel, List<ExeInfo> list)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    var name = Path.GetFileName(f);
                    if (!run.Inc.Any(r => r.IsMatch(name)) || run.Exc.Any(r => r.IsMatch(name))) continue;
                    long size = 0;
                    try { size = new FileInfo(f).Length; } catch { }
                    list.Add(new ExeInfo { Path = f, Size = size, Level = level });
                }
            }
            catch { }
            if (level >= maxLevel || cancel) return;

            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    if (ExeSkip.Contains(Path.GetFileName(d)) || IsReparse(d)) continue;
                    CollectExes(run, d, level + 1, maxLevel, list);
                }
            }
            catch { }
        }

        static bool IsReparse(string dir)
        {
            try { return (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0; } catch { return true; }
        }

        static void AddRaw(ScanRun run, string folder, List<ExeInfo> exes)
        {
            if (exes == null || exes.Count == 0) return;
            var trimmed = folder.TrimEnd('\\');
            var folderName = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(folderName) || !run.Seen.Add(trimmed)) return;

            var norm = Norm(folderName);
            var uniq = new Dictionary<string, ExeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in exes)
            {
                if (!e.Path.StartsWith(trimmed + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                e.Level = e.Path.Substring(trimmed.Length + 1).Count(ch => ch == '\\'); // depth relative to the game folder
                e.Score = Score(e, norm);
                uniq[e.Path] = e;
            }
            if (uniq.Count == 0) return;

            var ordered = uniq.Values.OrderByDescending(e => e.Score).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var c = new ScanCandidate { Folder = trimmed, Exes = ordered.Select(e => e.Path).ToList() };
            c.Name = NameCleaner.Clean(run.S.NameFromFolder ? folderName : Path.GetFileNameWithoutExtension(c.Exes[0]));
            c.Exists = IsExisting(run, c);

            if (c.Exists && run.S.HideExisting) { run.SkippedExisting++; return; }
            c.Selected = !c.Exists;
            run.Found.Add(c);
        }

        static int Score(ExeInfo e, string folderNorm)
        {
            var nameNorm = Norm(Path.GetFileNameWithoutExtension(e.Path));
            int s = 0;
            if (nameNorm == folderNorm) s += 100;
            else if (folderNorm.Length >= 3 && nameNorm.Length >= 3 &&
                     (nameNorm.Contains(folderNorm) || folderNorm.Contains(nameNorm))) s += 50;
            s -= e.Level * 15;
            s += (int)Math.Min(20, e.Size / (5L * 1024 * 1024));
            if (nameNorm.Contains("launcher")) s -= 5;
            return s;
        }

        static string Norm(string s) { return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); }

        // ----- games already in the library -----

        static string Key(string p) { return LibraryIndex.Key(p); }

        static Existing Gather()
        {
            var ex = new Existing();
            if (api == null) return ex;
            foreach (var g in api.Database.Games)
            {
                try
                {
                    if (!string.IsNullOrEmpty(g.InstallDirectory))
                    {
                        var d = Key(api.ExpandGameVariables(g, g.InstallDirectory));
                        if (d != null) ex.Dirs.Add(d);
                    }
                    if (g.GameActions != null)
                        foreach (var a in g.GameActions)
                        {
                            if (a.Type != GameActionType.File || string.IsNullOrEmpty(a.Path)) continue;
                            var p = Key(api.ExpandGameVariables(g, a.Path));
                            if (p != null) ex.Exes.Add(p);
                        }
                }
                catch { }
            }
            return ex;
        }

        static bool IsExisting(ScanRun run, ScanCandidate c)
        {
            var fk = Key(c.Folder);
            if (fk != null)
            {
                if (run.Ex.Dirs.Contains(fk)) return true;
                var cur = fk;
                while (true)
                {
                    var parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrEmpty(parent)) break;
                    cur = parent.TrimEnd('\\').ToLowerInvariant();
                    if (run.RootKeys.Contains(cur)) break; // the folder being scanned doesn't count
                    if (run.Ex.Dirs.Contains(cur)) return true;
                }
            }
            foreach (var exe in c.Exes)
            {
                var k = Key(exe);
                if (k != null && run.Ex.Exes.Contains(k)) return true;
            }
            return false;
        }

        // ================= import =================

        public static bool StartImport(IList<ScanCandidate> list, ScanSettings settings)
        {
            if (list == null || list.Count == 0) return false;
            if (!TryBegin()) return false;
            lock (gate) { foreach (var c in list) results.Remove(c); }

            var s = settings.Clone();
            var items = list.ToList();
            var platform = api.Database.Platforms.FirstOrDefault(p => p.SpecificationId == "pc_windows");
            var platformId = platform != null ? platform.Id : Guid.Empty;

            SetStatus(true, "Importing", 0, "Importing…");
            Task.Factory.StartNew(() => ImportWorker(items, s, platformId), TaskCreationOptions.LongRunning);
            return true;
        }

        static void ImportWorker(List<ScanCandidate> list, ScanSettings s, Guid platformId)
        {
            int added = 0, failed = 0;
            var addedGames = new List<Game>();
            try
            {
                for (int i = 0; i < list.Count && !cancel; i++)
                {
                    var c = list[i];
                    SetStatus(true, "Importing", (int)((double)i / list.Count * 100),
                        "Importing \"" + c.Name + "\" (" + (i + 1) + " of " + list.Count + ")");
                    try
                    {
                        var game = Build(c, s, platformId);
                        var dispatcher = api.MainView.UIDispatcher;
                        if (dispatcher != null) dispatcher.Invoke(new Action(() => api.Database.Games.Add(game)));
                        else api.Database.Games.Add(game);
                        LibraryIndex.Add(c.Exe);
                        addedGames.Add(game);
                        added++;
                    }
                    catch { failed++; }
                }

                var skipped = list.Count - added - failed;
                var text = added + " game(s) added to your library";
                if (failed > 0) text += ", " + failed + " failed";
                if (skipped > 0) text += ", " + skipped + " not imported (cancelled)";
                Notifier.Send(api, "Scan import finished: " + text + ".", failed > 0 ? NotificationType.Error : NotificationType.Info);

                if (addedGames.Count > 0 && s.DownloadMetadata && !cancel)
                {
                    try { RunMetadata(addedGames, s); } catch { }
                }
            }
            catch (Exception ex) { Notifier.Send(api, "Quick Add: import failed - " + ex.Message, NotificationType.Error); }
            finally { End(); }
        }

        static Game Build(ScanCandidate c, ScanSettings s, Guid platformId)
        {
            var folder = c.Folder.TrimEnd('\\');
            var installDir = folder;
            if (s.RelativePaths)
            {
                var app = api.Paths.ApplicationPath.TrimEnd('\\');
                if (folder.StartsWith(app + "\\", StringComparison.OrdinalIgnoreCase))
                    installDir = "{PlayniteDir}" + folder.Substring(app.Length);
            }

            // main executable first (the Play action), then the extra ones
            var order = new List<int> { c.Main };
            order.AddRange(c.Extra.Where(i => i != c.Main && i >= 0 && i < c.Exes.Count).OrderBy(i => i));

            var actions = new ObservableCollection<GameAction>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var idx in order)
            {
                var exe = c.Exes[idx];
                if (!exe.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = exe.Substring(folder.Length + 1);
                var relDir = Path.GetDirectoryName(rel) ?? "";

                var name = idx == c.Main ? c.Name : Path.GetFileNameWithoutExtension(rel);
                if (!usedNames.Add(name)) { name = rel; usedNames.Add(name); }

                actions.Add(new GameAction
                {
                    Type = GameActionType.File,
                    Name = name,
                    Path = "{InstallDir}\\" + rel,
                    WorkingDir = relDir.Length == 0 ? "{InstallDir}" : "{InstallDir}\\" + relDir,
                    IsPlayAction = idx == c.Main
                });
            }
            if (actions.Count == 0) throw new InvalidOperationException("No executable inside the game folder.");

            var game = new Game(c.Name)
            {
                IsInstalled = true,
                InstallDirectory = installDir,
                Added = DateTime.Now,
                GameActions = actions
            };
            if (platformId != Guid.Empty) game.PlatformIds = new List<Guid> { platformId };

            var icon = SafeIconExtractor.ImportIcon(api, dataDir, c.Exe, game.Id);
            if (!string.IsNullOrEmpty(icon)) game.Icon = icon;
            return game;
        }

        // ================= metadata =================

        public static bool StartMetadata(IList<Game> games, ScanSettings settings)
        {
            if (games == null || games.Count == 0 || !TryBegin()) return false;
            var list = games.ToList();
            var s = settings.Clone();
            SetStatus(true, "Metadata", 0, "Downloading metadata…");
            Task.Factory.StartNew(() =>
            {
                try { RunMetadata(list, s); }
                catch (Exception ex) { Notifier.Send(api, "Quick Add: metadata failed - " + ex.Message, NotificationType.Error); }
                finally { End(); }
            }, TaskCreationOptions.LongRunning);
            return true;
        }

        static void RunMetadata(List<Game> games, ScanSettings s)
        {
            int n = MetadataFetcher.Run(api, games, s.MetadataSource, Path.Combine(dataDir, "icon-work"),
                (i, total, name) => SetStatus(true, "Metadata", (int)((double)i / total * 100),
                    "Metadata: \"" + name + "\" (" + (i + 1) + " of " + total + ")", false),
                () => cancel);
            Notifier.Send(api, "Metadata downloaded for " + n + " of " + games.Count + " game(s).", NotificationType.Info);
        }
    }
}
