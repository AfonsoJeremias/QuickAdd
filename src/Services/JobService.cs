using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuickAdd.Services
{
    public sealed class JobStatus
    {
        public bool Running;
        public string Text = "";
        public int Percent;
    }

    /// <summary>Downloads metadata for the games that were just added, on a background thread.</summary>
    public static class JobService
    {
        static readonly object gate = new object();
        static readonly Stopwatch throttle = Stopwatch.StartNew();
        static IPlayniteAPI api;
        static string dataDir;
        static bool busy;
        static volatile bool cancel;
        static JobStatus status = new JobStatus();

        public static event Action Changed;

        public static void Init(IPlayniteAPI playniteApi, string dataDirectory) { api = playniteApi; dataDir = dataDirectory; }

        public static JobStatus Snapshot()
        {
            lock (gate) { return new JobStatus { Running = status.Running, Text = status.Text, Percent = status.Percent }; }
        }

        public static void Cancel() { cancel = true; }

        static void SetStatus(bool running, int percent, string text, bool force)
        {
            if (!force && throttle.ElapsedMilliseconds < 100) return;
            throttle.Restart();
            lock (gate) { status = new JobStatus { Running = running, Percent = Math.Max(0, Math.Min(100, percent)), Text = text }; }
            try { Changed?.Invoke(); } catch { }
        }

        public static bool StartMetadata(IList<Game> games, AppSettings settings)
        {
            if (games == null || games.Count == 0 || api == null) return false;
            lock (gate) { if (busy) return false; busy = true; cancel = false; }

            var list = games.ToList();
            var s = settings.Clone();
            SetStatus(true, 0, "Downloading metadata…", true);
            Task.Factory.StartNew(() =>
            {
                try
                {
                    int n = MetadataService.Run(api, list, s.MetadataSource, Path.Combine(dataDir, "icon-work"),
                        (i, total, name) => SetStatus(true, (int)((double)i / total * 100),
                            "Metadata: \"" + name + "\" (" + (i + 1) + " of " + total + ")", false),
                        () => cancel);
                    Notifier.Send(api, cancel
                        ? "Metadata download cancelled (" + n + " of " + list.Count + " game(s) updated)."
                        : "Metadata downloaded for " + n + " of " + list.Count + " game(s).", NotificationType.Info);
                }
                catch (Exception ex) { Notifier.Send(api, "Quick Add: metadata failed - " + ex.Message, NotificationType.Error); }
                finally
                {
                    lock (gate) { busy = false; }
                    SetStatus(false, 0, "", true);
                }
            }, TaskCreationOptions.LongRunning);
            return true;
        }
    }
}
