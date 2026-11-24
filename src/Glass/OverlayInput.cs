using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GlassClicker
{
    // Only installed while the user is editing a specific control in a non-activating window.
    internal sealed class OverlayInput : IDisposable
    {
        private static OverlayInput current;
        private readonly GlassWindow owner;
        private readonly FrameworkElement region;
        private readonly IntPtr foreground;
        private readonly Func<int, ModifierKeys, bool> accepts;
        private readonly Action<int, ModifierKeys> pressed;
        private readonly Action<int> released;
        private readonly HashSet<int> handled = new HashSet<int>();
        private readonly Action<bool> ended;
        private readonly HookProc callback;
        private readonly DispatcherTimer focusWatch;
        private readonly MousePressWatch mouse;
        private IntPtr hook;

        private OverlayInput(GlassWindow owner, FrameworkElement region, Func<int, ModifierKeys, bool> accepts,
            Action<int, ModifierKeys> pressed, Action<bool> ended, Action<int> released)
        {
            this.owner = owner; this.region = region; this.accepts = accepts; this.pressed = pressed; this.ended = ended;
            this.released = released;
            foreground = Windows.GetForegroundWindow();
            callback = OnKey;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) return;
            mouse = new MousePressWatch(owner.Dispatcher, OutsidePress);
            focusWatch = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
            focusWatch.Tick += (sender, args) => { if (!owner.IsVisible || Windows.GetForegroundWindow() != foreground) Finish(true); };
            focusWatch.Start();
            owner.Closed += OwnerClosed;
        }

        public static bool Begin(GlassWindow owner, FrameworkElement region, Func<int, ModifierKeys, bool> accepts,
            Action<int, ModifierKeys> pressed, Action<bool> ended, Action<int> released = null)
        {
            if (current != null) current.Finish(true);
            var capture = new OverlayInput(owner, region, accepts, pressed, ended, released);
            if (capture.hook == IntPtr.Zero) return false;
            current = capture;
            return true;
        }

        public static bool Editing(FrameworkElement region) { return current != null && current.region == region; }

        public static bool IsWithin(DependencyObject source, FrameworkElement region)
        {
            for (var item = source; item != null; item = item is Visual
                ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
                if (item == region) return true;
            return false;
        }

        public static void ConfirmOutside(GlassWindow owner, DependencyObject source)
        {
            if (current != null && current.owner == owner && !IsWithin(source, current.region))
                current.Finish(true);
        }

        public static void End(GlassWindow owner, bool commit)
        {
            if (current != null && current.owner == owner) current.Finish(commit);
        }

        public static void CommitPending()
        {
            if (current != null) current.Finish(true);
        }

        private void OwnerClosed(object sender, EventArgs args) { Finish(true); }

        private void OutsidePress(ScreenPoint point, int button)
        {
            if (!owner.IsVisible || !region.IsVisible) { Finish(true); return; }
            if (PresentationSource.FromVisual(region) == null) { Finish(true); return; }
            var local = region.PointFromScreen(new Point(point.X, point.Y));
            if (!new Rect(0, 0, region.ActualWidth, region.ActualHeight).Contains(local)) Finish(true);
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            var kind = message.ToInt32();
            if (code >= 0 && (Marshal.ReadInt32(data, 8) & 0x10) == 0 && Windows.GetForegroundWindow() == foreground)
            {
                var key = Marshal.ReadInt32(data);
                if ((kind == 0x0101 || kind == 0x0105) && handled.Remove(key))
                {
                    owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (current == this && released != null) released(key);
                    }));
                    return new IntPtr(1);
                }
                if (kind == 0x0100 || kind == 0x0104)
                {
                    var mods = Modifiers();
                    if (accepts(key, mods))
                    {
                        if (handled.Add(key) || released == null)
                            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                            {
                                if (current == this && owner.IsVisible && Windows.GetForegroundWindow() == foreground) pressed(key, mods);
                            }));
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }

        private void Finish(bool commit)
        {
            if (current != this) return;
            current = null;
            Dispose();
            ended(commit);
        }

        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            focusWatch.Stop();
            mouse.Dispose();
            owner.Closed -= OwnerClosed;
        }

        public static bool EditNumber(GlassWindow owner, FrameworkElement region, TextBox field, Func<bool, bool> confirm,
            Action<bool> editing = null, bool allowDecimal = false)
        {
            var before = field.Text;
            if (!Begin(owner, region, (key, mods) => NumberKey(key, mods, allowDecimal), (key, mods) =>
            {
                if (key == 0x0D) { if (confirm(true)) End(owner, false); return; }
                if (key == 0x1B) { field.Text = before; End(owner, false); return; }
                Edit(field, key, mods, allowDecimal);
            }, commit => { if (commit) confirm(false); if (editing != null) editing(false); })) return false;
            field.SelectAll();
            if (editing != null) editing(true);
            return true;
        }

        private static bool NumberKey(int key, ModifierKeys mods, bool allowDecimal)
        {
            if (mods == ModifierKeys.Control) return key == 'A' || key == 'V' || key == 'C';
            if (mods != ModifierKeys.None && mods != ModifierKeys.Shift) return false;
            return key >= 0x30 && key <= 0x39 || key >= 0x60 && key <= 0x69 ||
                allowDecimal && (key == 0x6E || key == 0xBE) ||
                key == 0x08 || key == 0x2E || key == 0x25 || key == 0x27 || key == 0x23 || key == 0x24 || key == 0x0D || key == 0x1B;
        }

        private static void Edit(TextBox field, int key, ModifierKeys mods, bool allowDecimal)
        {
            var start = field.SelectionStart;
            var size = field.SelectionLength;
            var end = start + size;
            if (mods == ModifierKeys.Control)
            {
                try
                {
                    if (key == 'A') field.SelectAll();
                    else if (key == 'C' && size != 0) Clipboard.SetText(field.SelectedText);
                    else if (key == 'V')
                    {
                        var text = Clipboard.GetText();
                        if (text.Length > 0 && text.Length <= field.MaxLength &&
                            System.Linq.Enumerable.All(text, c => c >= '0' && c <= '9' || allowDecimal && c == '.'))
                            Replace(field, start, size, text);
                    }
                }
                catch (ExternalException) { }
                return;
            }
            switch (key)
            {
                case 0x08: if (start > 0 || size != 0) Replace(field, size == 0 ? start - 1 : start, Math.Max(1, size), ""); return;
                case 0x2E: if (start < field.Text.Length) Replace(field, start, Math.Max(1, size), ""); return;
                case 0x25: field.Select(size == 0 ? Math.Max(0, start - 1) : start, 0); return;
                case 0x27: field.Select(size == 0 ? Math.Min(field.Text.Length, end + 1) : end, 0); return;
                case 0x23: field.Select(field.Text.Length, 0); return;
                case 0x24: field.Select(0, 0); return;
                case 0x6E:
                case 0xBE: Replace(field, start, size, "."); return;
            }
            var digit = key >= 0x60 ? (char)('0' + key - 0x60) : (char)key;
            Replace(field, start, size, digit.ToString());
        }

        private static void Replace(TextBox field, int start, int size, string replacement)
        {
            if (field.Text.Length - size + replacement.Length > field.MaxLength) return;
            if (size != 0 || replacement.Length != 0)
            {
                field.Text = field.Text.Remove(start, size).Insert(start, replacement);
                UiSounds.Key();
            }
            field.Select(start + replacement.Length, 0);
        }

        private static ModifierKeys Modifiers()
        {
            return (Down(0x10) ? ModifierKeys.Shift : ModifierKeys.None) | (Down(0x11) ? ModifierKeys.Control : ModifierKeys.None) |
                (Down(0x12) ? ModifierKeys.Alt : ModifierKeys.None) | (Down(0x5B) || Down(0x5C) ? ModifierKeys.Windows : ModifierKeys.None);
        }

        private static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")] private static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    }

    internal sealed class ForegroundReturn : IDisposable
    {
        private readonly IntPtr original = Windows.GetForegroundWindow();
        public void Dispose()
        {
            var focused = Windows.GetForegroundWindow();
            if (original == IntPtr.Zero || original == focused || !Windows.IsWindowVisible(original)) return;
            uint process;
            Windows.Thread(focused, out process);
            if (focused == IntPtr.Zero || process == Windows.OwnProcess) SetForegroundWindow(original);
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    }
}
