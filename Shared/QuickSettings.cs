using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FullscreenKit
{
    /// <summary>All Quick Add options, saved as settings.json in the plugin's data folder.</summary>
    [DataContract]
    public class QuickSettings
    {
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
        [DataMember] public bool SelectAdded { get; set; }        // select the new games in the Playnite library afterwards
        // move / rename
        [DataMember] public bool UpdateLibraryPaths { get; set; }
        [DataMember] public int MoveConflict { get; set; }        // 0 skip, 1 keep both
        [DataMember] public bool KeepExtension { get; set; }

        static QuickSettings current = new QuickSettings();
        static string path;
        static bool loaded;

        public QuickSettings() { SetDefaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext context) { SetDefaults(); } // json deserializer skips constructors

        public void SetDefaults()
        {
            SortBy = 0; SortDescending = false; ShowHidden = false;
            RememberLastFolder = true; LastFolder = "";
            AskNamesAdd = true; DownloadMetadata = true; MetadataSource = ""; SelectAdded = true;
            UpdateLibraryPaths = true; MoveConflict = 0; KeepExtension = true;
        }

        /// <summary>Resets the options but keeps the last folder.</summary>
        public void ResetOptions()
        {
            var last = LastFolder;
            SetDefaults();
            LastFolder = last;
        }

        public QuickSettings Clone() { return (QuickSettings)MemberwiseClone(); }

        public static QuickSettings Current { get { return current; } }

        public static void Load(string dir)
        {
            if (loaded || string.IsNullOrEmpty(dir)) return;
            loaded = true;
            path = Path.Combine(dir, "settings.json");
            try
            {
                Directory.CreateDirectory(dir);
                if (!File.Exists(path)) return;
                using (var fs = File.OpenRead(path))
                {
                    var o = new DataContractJsonSerializer(typeof(QuickSettings)).ReadObject(fs) as QuickSettings;
                    if (o == null) return;
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
                    new DataContractJsonSerializer(typeof(QuickSettings)).WriteObject(fs, this);
            }
            catch { }
        }
    }
}
