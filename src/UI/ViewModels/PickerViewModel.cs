using QuickAdd.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace QuickAdd.UI
{
    /// <summary>
    /// State of the picker: rows, header, tabs, button prompts and background-job bars. Pages (browse pages for every
    /// mode, scan results, executables, options) fill it and react to commands; the view only binds to it.
    /// </summary>
    public sealed class PickerViewModel : ObservableObject
    {
        readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
        readonly List<BrowsePage> modes = new List<BrowsePage>();
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        PickerPage page;
        int modeIndex;
        int seenResultsVersion;
        string title = "", subtitle = "";
        int selectedIndex = -1;
        bool tabsVisible;

        public PickerViewModel(PickerOptions options)
        {
            Options = options;
            Input = new InputRouter(Execute);
            seenResultsVersion = ScanService.ResultsVersion;
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); RefreshJobs(); };

            if (options.FoldersOnly) modes.Add(new FolderPickPage());
            else
            {
                modes.Add(new AddPage());
                if (options.AllowMultiple && options.AllowMove) modes.Add(new MovePage());
                if (options.AllowMultiple && options.AllowScan) modes.Add(new ScanPage());
                if (options.AllowMultiple && options.AllowRename) modes.Add(new RenamePage());
            }
            foreach (var m in modes) m.Attach(this);

            var start = options.StartPath;
            CurrentPath = !string.IsNullOrEmpty(start) && System.IO.Directory.Exists(start) ? start : null;
            Show(modes[0], null);
        }

        public PickerOptions Options { get; private set; }
        public InputRouter Input { get; private set; }
        public string CurrentPath { get; set; } // null = places and drives; shared by every mode

        public ObservableCollection<RowViewModel> Rows { get; } = new ObservableCollection<RowViewModel>();
        public ObservableCollection<TabViewModel> Tabs { get; } = new ObservableCollection<TabViewModel>();
        public ObservableCollection<HintViewModel> Hints { get; } = new ObservableCollection<HintViewModel>();
        public JobViewModel MoveJob { get; } = new JobViewModel();
        public JobViewModel ScanJob { get; } = new JobViewModel();

        public string Title { get { return title; } set { Set(ref title, value); } }
        public string Subtitle { get { return subtitle; } set { Set(ref subtitle, value); } }
        public int SelectedIndex { get { return selectedIndex; } set { Set(ref selectedIndex, value); } }
        public bool TabsVisible { get { return tabsVisible; } set { Set(ref tabsVisible, value); } }
        public Visibility TabsVisibility { get { return tabsVisible ? Visibility.Visible : Visibility.Collapsed; } }

        public RowViewModel SelectedRow
        {
            get { return selectedIndex >= 0 && selectedIndex < Rows.Count ? Rows[selectedIndex] : null; }
        }

        public event Action<IList<string>> Done;

        // ---------- pages ----------

        public PickerPage Page { get { return page; } }

        public void Show(PickerPage next, object focus)
        {
            page = next;
            Rows.Clear();
            SelectedIndex = -1;
            next.Enter(focus);
            RefreshChrome();
        }

        /// <summary>Back to the browse page of the current mode.</summary>
        public void ShowBrowse(object focus = null) { Show(modes[modeIndex], focus); }

        public void CycleMode(int dir)
        {
            if (modes.Count < 2) return;
            modeIndex = (modeIndex + dir + modes.Count) % modes.Count;
            Show(modes[modeIndex], SelectedRow == null ? null : SelectedRow.Path);
        }

        public void Execute(PickerCommand command)
        {
            switch (command)
            {
                case PickerCommand.Up: Move(-1); break;
                case PickerCommand.Down: Move(1); break;
                case PickerCommand.PageUp: Move(-8); break;
                case PickerCommand.PageDown: Move(8); break;
                case PickerCommand.Left: if (page.AllowModeSwitch) CycleMode(-1); break;
                case PickerCommand.Right: if (page.AllowModeSwitch) CycleMode(1); break;
                default: page.Handle(command, SelectedRow); break;
            }
        }

        public void Finish(IList<string> result)
        {
            Input.Stop();
            Detach();
            var s = AppSettings.Current;
            if (s.RememberLastFolder && CurrentPath != null) s.LastFolder = CurrentPath;
            Done?.Invoke(result);
        }

        // ---------- header, tabs, prompts ----------

        public void RefreshChrome()
        {
            Title = page.Title(Options.Title);
            Subtitle = page.Subtitle;

            Tabs.Clear();
            TabsVisible = modes.Count > 1;
            Raise(nameof(TabsVisibility));
            if (TabsVisible)
            {
                var active = modes[modeIndex];
                foreach (var m in modes)
                {
                    bool on = m == active;
                    Tabs.Add(new TabViewModel
                    {
                        Title = m.Mode.ToString().ToUpperInvariant(),
                        Background = on ? Theme.Of(m.Mode) : Theme.KeyCap,
                        Foreground = on ? System.Windows.Media.Brushes.White : Theme.Muted
                    });
                }
            }

            Hints.Clear();
            foreach (var h in page.Hints()) Hints.Add(h);
            if (TabsVisible && page.AllowModeSwitch) Hints.Add(new HintViewModel("◀ ▶", "Switch mode"));
        }

        // ---------- rows ----------

        public void Move(int delta)
        {
            if (Rows.Count == 0) return;
            int dir = Math.Sign(delta);
            int i = selectedIndex;
            if (i < 0) i = dir > 0 ? -1 : Rows.Count;
            int target = Math.Max(0, Math.Min(Rows.Count - 1, i + delta));

            int t = target;
            while (t >= 0 && t < Rows.Count && !Rows[t].IsSelectable) t += dir;
            if (t < 0 || t >= Rows.Count)
            {
                t = target;
                while (t >= 0 && t < Rows.Count && !Rows[t].IsSelectable) t -= dir;
            }
            if (t >= 0 && t < Rows.Count) SelectedIndex = t;
        }

        public void Select(int index)
        {
            if (index >= 0 && index < Rows.Count) SelectedIndex = index;
        }

        // ---------- toast + background jobs ----------

        public void Toast(string text)
        {
            toastTimer.Stop();
            MoveJob.Show(text, 0, false);
            toastTimer.Start();
        }

        public void Attach()
        {
            Detach();
            MoveService.Changed += OnJobsChanged;
            ScanService.Changed += OnJobsChanged;
            RefreshJobs();
        }

        public void Detach()
        {
            MoveService.Changed -= OnJobsChanged;
            ScanService.Changed -= OnJobsChanged;
        }

        void OnJobsChanged() { ui.BeginInvoke(new Action(RefreshJobs)); }

        void RefreshJobs()
        {
            var m = MoveService.Snapshot();
            if (m.Running)
            {
                toastTimer.Stop();
                var text = string.IsNullOrEmpty(m.Name) ? "Moving…" : "Moving \"" + m.Name + "\"   " + m.Percent + "%";
                if (m.Count > 1 && m.Index > 0) text += "   (" + m.Index + " of " + m.Count + ")";
                if (m.Queued > 0) text += "   +" + m.Queued + " queued";
                MoveJob.Show(text, m.Percent, true);
            }
            else if (!toastTimer.IsEnabled) MoveJob.Hide();

            var s = ScanService.Snapshot();
            if (s.Running) ScanJob.Show(s.Text + "   " + s.Percent + "%", s.Percent, true);
            else ScanJob.Hide();

            int v = ScanService.ResultsVersion;
            if (v != seenResultsVersion)
            {
                seenResultsVersion = v;
                if (page is ScanPage && !s.Running && ScanService.GetResults().Count > 0)
                    Show(new ResultsPage(), null); // a scan just finished while the user was waiting in Scan mode
            }
        }
    }
}
