using System.Windows;
using System.Windows.Media;

namespace QuickAdd.UI
{
    public enum RowKind { Header, Place, Drive, Up, Use, Folder, File, Action, Saved, Candidate, Executable, Option }

    public sealed class RowViewModel : ObservableObject
    {
        bool marked;

        public RowKind Kind { get; set; }
        public string Glyph { get; set; }
        public string Text { get; set; }
        public string Sub { get; set; }
        public string Path { get; set; }
        public string Tag { get; set; }
        public object Data { get; set; }
        public bool Dim { get; set; }

        public bool IsSelectable { get { return Kind != RowKind.Header; } }
        public double TextSize { get { return Kind == RowKind.Header ? 14 : 22; } }
        public Brush TextBrush { get { return Kind == RowKind.Header ? Brushes.Gray : Dim ? Brushes.DarkGray : Brushes.White; } }
        public Visibility SubVisibility { get { return string.IsNullOrEmpty(Sub) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility MarkVisibility { get { return marked ? Visibility.Visible : Visibility.Collapsed; } }

        public bool Marked
        {
            get { return marked; }
            set { if (Set(ref marked, value)) Raise(nameof(MarkVisibility)); }
        }
    }
}
