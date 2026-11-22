using Playnite.SDK;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FullscreenKit
{
    /// <summary>
    /// Replacement for IconExtractor / NativeMethods / IconUtil.
    /// - extracts the icon in memory (PNG bytes), never to the system temp folder;
    /// - if a file is needed for Database.AddFile, it lives in the plugin's own folder, has a unique name
    ///   and is deleted right after the import, so no global "clean temp" step is needed;
    /// - Cleanup() only removes this class's own leftovers.
    /// </summary>
    public static class SafeIconExtractor
    {
        const string Prefix = "icon_";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy,
            IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>PNG bytes of the file's icon (largest available up to size), or null.</summary>
        public static byte[] ExtractPng(string path, int size = 256)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            IntPtr hIcon = IntPtr.Zero;
            try
            {
                var icons = new IntPtr[1];
                var ids = new uint[1];
                try
                {
                    if (PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0) > 0) hIcon = icons[0];
                }
                catch { }

                if (hIcon == IntPtr.Zero)
                {
                    using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                    {
                        if (ico == null) return null;
                        return ToPng(ico.Handle); // ico owns the handle, disposed above
                    }
                }
                return ToPng(hIcon);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
            }
        }

        static byte[] ToPng(IntPtr hIcon)
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Extracts the icon of <paramref name="exePath"/> and imports it into Playnite's database.
        /// workDir = plugin.GetPluginUserDataPath(). Returns the database file id (use as game.Icon) or null.
        /// </summary>
        public static string ImportIcon(IPlayniteAPI api, string workDir, string exePath, Guid gameId)
        {
            var png = ExtractPng(exePath);
            if (png == null) return null;

            string file = null;
            try
            {
                var dir = Path.Combine(workDir, "icon-work");
                Directory.CreateDirectory(dir);
                file = Path.Combine(dir, Prefix + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(file, png);
                return api.Database.AddFile(file, gameId);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { if (file != null && File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        /// <summary>Call once on startup: removes only our own stale files (older than 1 h).</summary>
        public static void Cleanup(string workDir)
        {
            try
            {
                var dir = Path.Combine(workDir, "icon-work");
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, Prefix + "*.png"))
                {
                    try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromHours(1)) File.Delete(f); }
                    catch { }
                }
            }
            catch { }
        }
    }
}
