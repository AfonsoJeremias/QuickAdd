using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace QuickAdd.Services
{
    /// <summary>All Quick Add options, saved as scan-settings.json in the plugin's data folder.</summary>
    [DataContract]
    public class AppSettings
    {
        public const string DefaultInclude = "*.exe";
        public const string DefaultExclude =
            "unins*;uninstall*;*setup*;install*;vc_redist*;vcredist*;dxsetup*;dotnetfx*;ndp*;oalinst*;physx*;directx*;" +
            "*crashhandler*;*crashreport*;crashpad*;*redist*;*updater*";

        // scan
        [DataMember] public bool FolderPerGame { get; set; }
        [DataMember] public int Depth { get; set; }
        [DataMember] public bool NameFromFolder { get; set; }
        [DataMember] public string Include { get; set; }
        [DataMember] public string Exclude { get; set; }
        [DataMember] public bool HideExisting { get; set; }
        [DataMember] public bool RelativePaths { get; set; }
        [DataMember] public List<string> SavedRoots { get; set; }
        [DataMember] public bool AskNamesScan { get; set; }
        // browsing
        [DataMember] public int SortBy { get; set; }          // 0 name, 1 date, 2 size
        [DataMember] public bool SortDescending { get; set; }
        [DataMember] public bool ShowHidden { get; set; }
        [DataMember] public bool RememberLastFolder { get; set; }
        [DataMember] public string LastFolder { get; set; }
        // add
        [DataMember] public bool AskNamesAdd { get; set; }
        [DataMember] public bool DownloadMetadata { get; set; }
        [DataMember] public string MetadataSource { get; set; }   // "" = all metadata plugins, IGDB first
        // move / rename
        [DataMember] public bool UpdateLibraryPaths { get; set; }
        [DataMember] public int MoveConflict { get; set; }        // 0 skip, 1 keep both
        [DataMember] public bool KeepExtension { get; set; }

        static AppSettings current = new AppSettings();
        static string path;
        static bool loaded;

        public AppSettings() { SetDefaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext context) { SetDefaults(); } // json deserializer skips constructors

        public void SetDefaults()
        {
            FolderPerGame = true; Depth = 2; NameFromFolder = true;
            Include = DefaultInclude; Exclude = DefaultExclude;
            HideExisting = true; RelativePaths = false;
            SavedRoots = new List<string>(); AskNamesScan = false;
            SortBy = 0; SortDescending = false; ShowHidden = false;
            RememberLastFolder = true; LastFolder = "";
            AskNamesAdd = true; DownloadMetadata = true; MetadataSource = "";
            UpdateLibraryPaths = true; MoveConflict = 0; KeepExtension = true;
        }

        /// <summary>Resets the options but keeps saved folders and the last folder.</summary>
        public void ResetOptions()
        {
            var roots = SavedRoots; var last = LastFolder;
            SetDefaults();
            SavedRoots = roots ?? new List<string>(); LastFolder = last;
        }

        public AppSettings Clone()
        {
            var c = (AppSettings)MemberwiseClone();
            c.SavedRoots = new List<string>(SavedRoots ?? new List<string>());
            return c;
        }

        public static AppSettings Current { get { return current; } }

        public static void Load(string dir)
        {
            if (loaded || string.IsNullOrEmpty(dir)) return;
            loaded = true;
            path = Path.Combine(dir, "scan-settings.json");
            try
            {
                Directory.CreateDirectory(dir);
                if (!File.Exists(path)) return;
                using (var fs = File.OpenRead(path))
                {
                    var o = new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(fs) as AppSettings;
                    if (o == null) return;
                    if (o.Depth < 1 || o.Depth > 4) o.Depth = 2;
                    if (string.IsNullOrWhiteSpace(o.Include)) o.Include = DefaultInclude;
                    if (o.Exclude == null) o.Exclude = DefaultExclude;
                    if (o.SavedRoots == null) o.SavedRoots = new List<string>();
                    if (o.MetadataSource == null) o.MetadataSource = "";
                    if (o.LastFolder == null) o.LastFolder = "";
                    current = o;
                }
            }
            catch { }
        }

        public void Save()
        {
            if (path == null) return;
            try
            {
                using (var fs = File.Create(path))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, this);
            }
            catch { }
        }

        /// <summary>"*.exe;unins*" -> regexes (case-insensitive, whole file name).</summary>
        public static Regex[] ToRegexes(string list, string fallback)
        {
            var l = string.IsNullOrWhiteSpace(list) ? fallback : list;
            if (string.IsNullOrWhiteSpace(l)) return new Regex[0];
            return l.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).Where(p => p.Length > 0)
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToArray();
        }
    }
}
