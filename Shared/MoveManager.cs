using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FullscreenKit
{
    public sealed class MoveProgress
    {
        public bool Running;
        public int Percent;
        public string Name = "";
        public int Index;   // 1-based item being moved
        public int Count;   // items in the current job
        public int Queued;  // jobs waiting after this one
    }

    /// <summary>
    /// Moves files/folders on a background thread, one job at a time. Keeps running when the picker is closed;
    /// a reopened picker reads Snapshot() and subscribes to Changed. Same drive = instant rename; other drive =
    /// copy with progress, and the original is deleted only after everything was copied.
    /// </summary>
    public static class MoveManager
    {
        sealed class Job { public List<string> Sources; public string Dest; }
        sealed class Item { public string Source, Target; public bool IsDir, NeedsCopy; public long Weight; }

        static readonly object gate = new object();
        static readonly Queue<Job> queue = new Queue<Job>();
        static readonly Stopwatch throttle = Stopwatch.StartNew();
        static IPlayniteAPI api;
        static bool running;
        static MoveProgress state = new MoveProgress();

        /// <summary>Raised from the worker thread; marshal to the UI thread in the handler.</summary>
        public static event Action Changed;

        public static void Init(IPlayniteAPI playniteApi) { api = playniteApi; }

        public static MoveProgress Snapshot()
        {
            lock (gate)
            {
                return new MoveProgress
                {
                    Running = state.Running, Percent = state.Percent, Name = state.Name,
                    Index = state.Index, Count = state.Count, Queued = queue.Count
                };
            }
        }

        public static void Enqueue(IList<string> sources, string destFolder)
        {
            bool start;
            lock (gate)
            {
                queue.Enqueue(new Job { Sources = sources.ToList(), Dest = destFolder });
                start = !running;
                running = true;
                if (start) { state = new MoveProgress { Running = true, Count = sources.Count }; }
            }
            Raise();
            if (start) Task.Factory.StartNew(Worker, TaskCreationOptions.LongRunning);
        }

        static void Raise() { try { Changed?.Invoke(); } catch { } }

        static void Worker()
        {
            while (true)
            {
                Job job;
                lock (gate)
                {
                    if (queue.Count == 0) { running = false; state = new MoveProgress(); break; }
                    job = queue.Dequeue();
                    state = new MoveProgress { Running = true, Count = job.Sources.Count };
                }
                Raise();
                try { RunJob(job); }
                catch (Exception ex) { Notify("Quick Add: move failed - " + ex.Message, NotificationType.Error); }
            }
            Raise();
        }

        static void Update(int index, int count, string name, long done, long total, bool force)
        {
            if (!force && throttle.ElapsedMilliseconds < 100) return;
            throttle.Restart();
            lock (gate)
            {
                state.Running = true;
                state.Index = index;
                state.Count = count;
                state.Name = name;
                state.Percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
            }
            Raise();
        }

        static void RunJob(Job job)
        {
            var destTrim = job.Dest.TrimEnd('\\');
            var items = new List<Item>();
            int skipped = 0;

            foreach (var src in job.Sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var srcTrim = src.TrimEnd('\\');
                bool isDir = Directory.Exists(srcTrim);
                if (!isDir && !File.Exists(srcTrim)) { skipped++; continue; }

                var parent = Path.GetDirectoryName(srcTrim);
                if (string.Equals((parent ?? "").TrimEnd('\\'), destTrim, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; } // already there
                if (isDir && IsSameOrInside(destTrim, srcTrim)) { skipped++; continue; }                                       // into itself

                var target = Path.Combine(job.Dest, Path.GetFileName(srcTrim));
                if (File.Exists(target) || Directory.Exists(target))
                {
                    if (ScanSettings.Current.MoveConflict != 1) { skipped++; continue; } // 0 = skip, 1 = keep both
                    target = UniqueTarget(job.Dest, Path.GetFileName(srcTrim), isDir);
                }

                bool copy = !SameVolume(srcTrim, job.Dest);
                items.Add(new Item
                {
                    Source = srcTrim, Target = target, IsDir = isDir, NeedsCopy = copy,
                    Weight = copy ? Math.Max(1, SizeOf(srcTrim, isDir)) : 1
                });
            }

            long total = items.Sum(i => i.Weight), done = 0;
            int moved = 0, failed = 0;
            var pairs = new List<KeyValuePair<string, string>>();
            string firstError = null;
            Item last = null;

            for (int idx = 0; idx < items.Count; idx++)
            {
                var it = items[idx];
                int index = idx + 1, count = items.Count;
                var name = Path.GetFileName(it.Source);
                long baseDone = done;
                Update(index, count, name, done, total, true);
                try
                {
                    if (!it.NeedsCopy)
                    {
                        if (it.IsDir) Directory.Move(it.Source, it.Target); else File.Move(it.Source, it.Target);
                    }
                    else
                    {
                        CopyAcross(it, bytes => Update(index, count, name, baseDone + bytes, total, false));
                    }
                    done += it.Weight;
                    moved++;
                    last = it;
                    pairs.Add(new KeyValuePair<string, string>(it.Source, it.Target));
                }
                catch (Exception ex)
                {
                    failed++;
                    if (firstError == null) firstError = ex.Message;
                    done += it.Weight;
                }
            }
            Update(items.Count, items.Count, "", total, total, true);

            int libUpdated = 0;
            if (pairs.Count > 0 && ScanSettings.Current.UpdateLibraryPaths)
            {
                try { libUpdated = LibraryPathUpdater.UpdateOnUi(api, pairs); } catch { }
            }

            // ----- notification -----
            var destName = string.IsNullOrEmpty(Path.GetFileName(destTrim)) ? job.Dest : Path.GetFileName(destTrim);
            var skipText = skipped > 0 ? " (" + skipped + " skipped: already there, missing or name conflict)" : "";
            if (libUpdated > 0) skipText += " - " + libUpdated + " library game(s) updated";

            if (failed > 0)
                Notify("Quick Add: " + moved + " moved, " + failed + " failed - " + firstError + skipText, NotificationType.Error);
            else if (moved == 1 && last != null)
                Notify((last.IsDir ? "Folder moved: " : "File moved: ") + Path.GetFileName(last.Source) + " → " + destName + skipText, NotificationType.Info);
            else if (moved > 1)
                Notify(moved + " items moved to \"" + destName + "\"" + skipText, NotificationType.Info);
            else
                Notify("Quick Add: nothing was moved" + skipText, NotificationType.Info);
        }

        // ----- copy across drives -----

        static void CopyAcross(Item it, Action<long> progress)
        {
            long copied = 0;
            if (!it.IsDir)
            {
                try { CopyFile(it.Source, it.Target, n => { copied += n; progress(copied); }); }
                catch { try { if (File.Exists(it.Target)) File.Delete(it.Target); } catch { } throw; }
                RemoveOriginal(() => { File.SetAttributes(it.Source, FileAttributes.Normal); File.Delete(it.Source); });
                return;
            }

            bool created = false;
            try
            {
                Directory.CreateDirectory(it.Target);
                created = true;
                var srcLen = it.Source.Length + 1;
                foreach (var d in Directory.EnumerateDirectories(it.Source, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(it.Target, d.Substring(srcLen)));
                foreach (var f in Directory.EnumerateFiles(it.Source, "*", SearchOption.AllDirectories))
                    CopyFile(f, Path.Combine(it.Target, f.Substring(srcLen)), n => { copied += n; progress(copied); });
            }
            catch
            {
                if (created) { try { Directory.Delete(it.Target, true); } catch { } } // never leave a half copy behind
                throw;
            }
            RemoveOriginal(() => DeleteDir(it.Source));
        }

        static void RemoveOriginal(Action delete)
        {
            try { delete(); }
            catch (Exception ex) { throw new IOException("copied, but could not remove the original (" + ex.Message + ")"); }
        }

        static void CopyFile(string src, string dst, Action<int> onBytes)
        {
            var buffer = new byte[1 << 20];
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length))
            using (var output = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length))
            {
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, n);
                    onBytes(n);
                }
            }
            try
            {
                File.SetCreationTime(dst, File.GetCreationTime(src));
                File.SetLastWriteTime(dst, File.GetLastWriteTime(src));
                File.SetAttributes(dst, File.GetAttributes(src));
            }
            catch { }
        }

        static void DeleteDir(string dir)
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(dir, true);
        }

        // ----- helpers -----

        static string UniqueTarget(string dest, string name, bool isDir)
        {
            var baseName = isDir ? name : Path.GetFileNameWithoutExtension(name);
            var ext = isDir ? "" : Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(dest, baseName + " (" + i + ")" + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine(dest, baseName + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")" + ext);
        }


        static bool SameVolume(string a, string b)
        {
            return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
        }

        static bool IsSameOrInside(string path, string folder)
        {
            path = path.TrimEnd('\\'); folder = folder.TrimEnd('\\');
            return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        static long SizeOf(string path, bool isDir)
        {
            try
            {
                if (!isDir) return new FileInfo(path).Length;
                return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { return 1; }
        }

        static void Notify(string text, NotificationType type)
        {
            var a = api;
            if (a == null) return;
            Action add = () => a.Notifications.Add(new NotificationMessage("QuickAddMove_" + Guid.NewGuid().ToString("N"), text, type));
            try
            {
                var dispatcher = a.MainView.UIDispatcher;
                if (dispatcher != null) dispatcher.BeginInvoke(add); else add();
            }
            catch { try { add(); } catch { } }
        }
    }
}
