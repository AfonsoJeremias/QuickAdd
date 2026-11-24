using System.Collections.Generic;

namespace QuickAdd.UI
{
    /// <summary>One screen of the picker (a browse page per mode, scan results, executables, options).</summary>
    public abstract class PickerPage
    {
        protected PickerViewModel Vm { get; private set; }

        public void Attach(PickerViewModel vm) { Vm = vm; }

        /// <summary>Fills Vm.Rows. Rows are already cleared. focus is a path, a candidate or an index, depending on the page.</summary>
        public abstract void Enter(object focus);
        public abstract IEnumerable<HintViewModel> Hints();
        public abstract void Handle(PickerCommand command, RowViewModel row);

        public virtual string Title(string baseTitle) { return baseTitle; }
        public virtual string Subtitle { get { return ""; } }
        public virtual bool AllowModeSwitch { get { return true; } }

        protected static HintViewModel H(string key, string label) { return new HintViewModel(key, label); }

        protected static RowViewModel Row(RowKind kind, string glyph, string text, string sub = null, string path = null)
        {
            return new RowViewModel { Kind = kind, Glyph = glyph, Text = text, Sub = sub, Path = path };
        }

        protected static string FormatSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int n = 0;
            while (v >= 1024 && n < u.Length - 1) { v /= 1024; n++; }
            return v.ToString(n == 0 ? "0" : "0.#") + " " + u[n];
        }
    }
}
