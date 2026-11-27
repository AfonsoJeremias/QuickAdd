using System.Windows.Controls;

namespace QuickAdd.UI
{
    /// <summary>Thin view: everything lives in PickerViewModel, input goes through InputRouter.</summary>
    public partial class PickerView : UserControl
    {
        public PickerViewModel ViewModel { get; private set; }

        public PickerView(PickerViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;

            PreviewKeyDown += (s, e) => ViewModel.Input.Key(e);
            List.MouseDoubleClick += (s, e) => ViewModel.Execute(PickerCommand.Accept);
            Loaded += (s, e) => { Focus(); ViewModel.Attach(); };
            Unloaded += (s, e) => { ViewModel.Input.Stop(); ViewModel.Detach(); };

            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PickerViewModel.SelectedIndex) && ViewModel.SelectedRow != null)
                    List.ScrollIntoView(ViewModel.SelectedRow);
            };
        }
    }
}
