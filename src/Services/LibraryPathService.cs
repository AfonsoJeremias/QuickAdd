using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickAdd.Services
{
    /// <summary>After files/folders are moved or renamed, fixes absolute paths of library games that pointed at them.</summary>
    public static class LibraryPathService
    {
        /// <summary>Marshals to the UI thread. Returns how many games were changed.</summary>
        public static int UpdateOnUi(IPlayniteAPI api, IList<KeyValuePair<string, string>> moves)
        {
            int n = 0;
            Action run = () => n = Update(api, moves);
            var d = api.MainView.UIDispatcher;
            if (d != null) d.Invoke(run); else run();
            return n;
        }

        public static int Update(IPlayniteAPI api, IList<KeyValuePair<string, string>> moves)
        {
            if (moves == null || moves.Count == 0) return 0;
            int changed = 0;
            foreach (var g in api.Database.Games.ToList())
            {
                bool dirty = false;
                var dir = Fix(g.InstallDirectory, moves);
                if (dir != g.InstallDirectory) { g.InstallDirectory = dir; dirty = true; }

                if (g.GameActions != null)
                {
                    foreach (var a in g.GameActions)
                    {
                        var p = Fix(a.Path, moves);
                        if (p != a.Path) { a.Path = p; dirty = true; }
                        var w = Fix(a.WorkingDir, moves);
                        if (w != a.WorkingDir) { a.WorkingDir = w; dirty = true; }
                    }
                }
                if (g.Roms != null)
                {
                    foreach (var r in g.Roms)
                    {
                        var p = Fix(r.Path, moves);
                        if (p != r.Path) { r.Path = p; dirty = true; }
                    }
                }
                if (dirty) { api.Database.Games.Update(g); changed++; }
            }
            return changed;
        }

        // only absolute paths: {InstallDir}\... style values follow InstallDirectory by themselves
        static string Fix(string value, IList<KeyValuePair<string, string>> moves)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith("{")) return value;
            foreach (var m in moves)
            {
                var old = m.Key.TrimEnd('\\');
                if (string.Equals(value.TrimEnd('\\'), old, StringComparison.OrdinalIgnoreCase)) return m.Value;
                if (value.StartsWith(old + "\\", StringComparison.OrdinalIgnoreCase))
                    return m.Value.TrimEnd('\\') + value.Substring(old.Length);
            }
            return value;
        }
    }
}
