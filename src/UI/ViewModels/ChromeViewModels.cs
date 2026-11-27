using System.Windows;
using System.Windows.Media;

namespace QuickAdd.UI
{
    /// <summary>One button prompt in the bottom bar.</summary>
    public sealed class HintViewModel
    {
        public HintViewModel(string key, string label)
        {
            Key = key; Label = label; FaceBrush = Theme.Face(key);
        }
        public string Key { get; private set; }
        public string Label { get; private set; }
        public Brush FaceBrush { get; private set; }
        public Visibility FaceVisibility { get { return FaceBrush != null ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility PillVisibility { get { return FaceBrush == null ? Visibility.Visible : Visibility.Collapsed; } }
    }

    /// <summary>One mode tab in the header.</summary>
    public sealed class TabViewModel
    {
        public string Title { get; set; }
        public Brush Background { get; set; }
        public Brush Foreground { get; set; }
    }

    /// <summary>A progress bar (or a short message) shown above the button prompts.</summary>
    public sealed class JobViewModel : ObservableObject
    {
        Visibility visibility = Visibility.Collapsed, barVisibility = Visibility.Visible;
        string text = "";
        double percent;

        public Visibility Visibility { get { return visibility; } set { Set(ref visibility, value); } }
        public Visibility BarVisibility { get { return barVisibility; } set { Set(ref barVisibility, value); } }
        public string Text { get { return text; } set { Set(ref text, value); } }
        public double Percent { get { return percent; } set { Set(ref percent, value); } }

        public void Show(string message, double pct, bool bar)
        {
            Text = message; Percent = pct;
            BarVisibility = bar ? Visibility.Visible : Visibility.Collapsed;
            Visibility = Visibility.Visible;
        }

        public void Hide() { Visibility = Visibility.Collapsed; }
    }
}
