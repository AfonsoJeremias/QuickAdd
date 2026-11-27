using Playnite.SDK;
using Playnite.SDK.Events;
using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickAdd.UI
{
    /// <summary>
    /// Turns pad buttons and key presses into PickerCommands: hold-to-repeat for up/down, a cooldown that stops the
    /// stick's sideways drift from switching mode, and duplicate filtering when Playnite also sends keys for the pad.
    /// </summary>
    public sealed class InputRouter
    {
        const int DuplicateMs = 200, StickCooldownMs = 300;

        readonly Action<PickerCommand> execute;
        readonly DispatcherTimer repeat = new DispatcherTimer();
        PickerCommand repeating;
        PickerCommand? last;
        int lastTick, verticalTick;
        bool lastFromPad;

        public InputRouter(Action<PickerCommand> execute)
        {
            this.execute = execute;
            repeat.Tick += (s, e) =>
            {
                repeat.Interval = TimeSpan.FromMilliseconds(70);
                verticalTick = Environment.TickCount;
                IsDuplicate(repeating, true);
                execute(repeating);
            };
        }

        public void Stop() { repeat.Stop(); }

        bool IsDuplicate(PickerCommand c, bool fromPad)
        {
            int now = Environment.TickCount;
            bool dup = last == c && lastFromPad != fromPad && unchecked(now - lastTick) < DuplicateMs;
            if (!dup) { last = c; lastTick = now; lastFromPad = fromPad; }
            return dup;
        }

        public void Pad(ControllerInput button, bool pressed)
        {
            switch (button)
            {
                case ControllerInput.DPadUp: Hold(PickerCommand.Up, pressed, false); break;
                case ControllerInput.DPadDown: Hold(PickerCommand.Down, pressed, false); break;
                case ControllerInput.LeftStickUp: Hold(PickerCommand.Up, pressed, true); break;
                case ControllerInput.LeftStickDown: Hold(PickerCommand.Down, pressed, true); break;

                case ControllerInput.DPadLeft: Fire(PickerCommand.Left, pressed, false); break;
                case ControllerInput.DPadRight: Fire(PickerCommand.Right, pressed, false); break;
                case ControllerInput.LeftStickLeft: Fire(PickerCommand.Left, pressed, true); break;
                case ControllerInput.LeftStickRight: Fire(PickerCommand.Right, pressed, true); break;

                case ControllerInput.A: Fire(PickerCommand.Accept, pressed, false); break;
                case ControllerInput.B: Fire(PickerCommand.Back, pressed, false); break;
                case ControllerInput.X: Fire(PickerCommand.Mark, pressed, false); break;
                case ControllerInput.Y: Fire(PickerCommand.Alt, pressed, false); break;
                case ControllerInput.Start: Fire(PickerCommand.Confirm, pressed, false); break;
                case ControllerInput.Back: Fire(PickerCommand.Options, pressed, false); break; // Select / View
                case ControllerInput.LeftShoulder: Fire(PickerCommand.PageUp, pressed, false); break;
                case ControllerInput.RightShoulder: Fire(PickerCommand.PageDown, pressed, false); break;
            }
        }

        void Fire(PickerCommand c, bool pressed, bool stick)
        {
            if (!pressed) return;
            if (stick && unchecked(Environment.TickCount - verticalTick) < StickCooldownMs) return;
            if (!IsDuplicate(c, true)) execute(c);
        }

        void Hold(PickerCommand c, bool pressed, bool stick)
        {
            if (stick) verticalTick = Environment.TickCount;
            if (pressed)
            {
                repeating = c;
                if (!IsDuplicate(c, true)) execute(c);
                repeat.Interval = TimeSpan.FromMilliseconds(400);
                repeat.Start();
            }
            else if (repeating == c) repeat.Stop();
        }

        /// <summary>Returns true if the key was a picker key (and marks it handled).</summary>
        public bool Key(KeyEventArgs e)
        {
            PickerCommand c;
            switch (e.Key)
            {
                case Key.Up: c = PickerCommand.Up; break;
                case Key.Down: c = PickerCommand.Down; break;
                case Key.PageUp: c = PickerCommand.PageUp; break;
                case Key.PageDown: c = PickerCommand.PageDown; break;
                case Key.Left: c = PickerCommand.Left; break;
                case Key.Right:
                case Key.Tab: c = PickerCommand.Right; break;
                case Key.Enter: c = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? PickerCommand.Confirm : PickerCommand.Accept; break;
                case Key.Escape:
                case Key.Back: c = PickerCommand.Back; break;
                case Key.Space: c = PickerCommand.Mark; break;
                case Key.Y: c = PickerCommand.Alt; break;
                case Key.F2: c = PickerCommand.Options; break;
                default: return false;
            }
            e.Handled = true;
            if (!IsDuplicate(c, false)) execute(c);
            return true;
        }
    }
}
