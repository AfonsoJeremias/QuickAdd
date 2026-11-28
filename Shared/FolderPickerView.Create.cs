using System;
using System.IO;

namespace FullscreenKit
{
    public partial class FolderPickerView
    {
        // Create mode: X = new folder, Start = new file, inside the folder you are browsing.
        // The name is typed with Playnite's on-screen keyboard; an existing name is never overwritten.
        void CreateItem(bool folder)
        {
            if (current == null) { ShowToast("Open a folder first, then create things inside it."); return; }
            if (opt.PromptText == null) { ShowToast("Text input isn't available here."); return; }

            var suggestion = FileOps.UniqueName(current, folder ? Settings.DefaultFolderName : Settings.DefaultFileName, folder);
            var message = (folder ? "Name of the new folder" : "Name of the new file") + " in:\n" + current;
            var entered = opt.PromptText(message, folder ? "New folder" : "New file", suggestion);
            if (string.IsNullOrWhiteSpace(entered)) return; // Cancel does nothing

            string path, error;
            if (FileOps.TryCreate(current, entered.Trim(), folder, out path, out error))
            {
                if (folder && Settings.OpenNewFolder) Navigate(path, null); // go into the new folder
                else Navigate(current, path);                                // stay, with the new item highlighted
                ShowToast((folder ? "Folder created: " : "File created: ") + Path.GetFileName(path));
            }
            else ShowToast("Couldn't create it: " + error);
        }
    }
}
