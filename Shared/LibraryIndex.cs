using Playnite.SDK;
using Playnite.SDK.Models;
using System.Collections.Generic;
using System.IO;

namespace FullscreenKit
{
    /// <summary>Executables already used by a game in the library, so the same one can't be added twice.</summary>
    public static class LibraryIndex
    {
        static readonly object gate = new object();
        static HashSet<string> exes = new HashSet<string>();

        public static string Key(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant(); } catch { return null; }
        }

        /// <summary>Call on the UI thread.</summary>
        public static void Refresh(IPlayniteAPI api)
        {
            var set = new HashSet<string>();
            foreach (var g in api.Database.Games)
            {
                try
                {
                    if (g.GameActions == null) continue;
                    foreach (var a in g.GameActions)
                    {
                        if (a.Type != GameActionType.File || string.IsNullOrEmpty(a.Path)) continue;
                        var k = Key(api.ExpandGameVariables(g, a.Path));
                        if (k != null) set.Add(k);
                    }
                }
                catch { }
            }
            lock (gate) { exes = set; }
        }

        public static bool Contains(string path)
        {
            var k = Key(path);
            if (k == null) return false;
            lock (gate) { return exes.Contains(k); }
        }

        public static void Add(string path)
        {
            var k = Key(path);
            if (k == null) return;
            lock (gate) { exes.Add(k); }
        }
    }
}
