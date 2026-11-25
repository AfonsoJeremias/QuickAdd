using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickAdd.Services
{
    public static class FileService
    {
        static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
          "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "The name is empty.";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The name has characters Windows doesn't allow.";
            if (name.EndsWith(".") || name.EndsWith(" ")) return "The name can't end with a dot or space.";
            if (Reserved.Contains(Path.GetFileNameWithoutExtension(name))) return "That name is reserved by Windows.";
            return null;
        }

        /// <summary>Renames a file or folder in place. Case-only changes work too.</summary>
        public static bool TryRename(string path, string newName, out string newPath, out string error)
        {
            newPath = null; error = ValidateName(newName);
            if (error != null) return false;
            try
            {
                var oldFull = path.TrimEnd('\\');
                bool isDir = Directory.Exists(oldFull);
                if (!isDir && !File.Exists(oldFull)) { error = "It doesn't exist anymore."; return false; }

                var parent = Path.GetDirectoryName(oldFull);
                var target = Path.Combine(parent, newName);
                newPath = target;
                if (string.Equals(oldFull, target, StringComparison.Ordinal)) { error = "Same name."; return false; }

                if (string.Equals(oldFull, target, StringComparison.OrdinalIgnoreCase))
                {
                    var tmp = oldFull + ".qa-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Move(oldFull, tmp, isDir);
                    Move(tmp, target, isDir);
                    return true;
                }
                if (File.Exists(target) || Directory.Exists(target)) { error = "\"" + newName + "\" already exists here."; return false; }
                Move(oldFull, target, isDir);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        static void Move(string from, string to, bool isDir)
        {
            if (isDir) Directory.Move(from, to); else File.Move(from, to);
        }
    }
}
