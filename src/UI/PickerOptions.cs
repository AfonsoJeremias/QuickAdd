using System;
using System.Collections.Generic;

namespace QuickAdd.UI
{
    public sealed class PickerOptions
    {
        public string Title { get; set; } = "Select";
        public bool FoldersOnly { get; set; }
        /// <summary>X marks files and Start confirms them; also enables the options page (Select).</summary>
        public bool AllowMultiple { get; set; }
        public bool AllowMove { get; set; }
        public bool AllowScan { get; set; }
        public bool AllowRename { get; set; }
        /// <summary>Add mode only. Lower-case with dot. Empty = all files.</summary>
        public string[] Extensions { get; set; } = new string[0];
        public string StartPath { get; set; }

        public Func<string, string, string, string> PromptText { get; set; }          // (message, caption, default) -> text or null
        public Func<string, bool> IsInLibrary { get; set; }
        public Func<IList<KeyValuePair<string, string>>, int> UpdateLibrary { get; set; }
        public Action<string> Notify { get; set; }
        public Func<List<string>> MetadataSources { get; set; }
    }
}
