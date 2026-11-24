using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace GlassClicker
{
    internal static class Windows
    {
        internal static readonly uint OwnProcess = (uint)Process.GetCurrentProcess().Id;
        internal const uint Place = 0x0010 | 0x0200;
        internal const uint KeepOrder = Place | 0x0004;
        internal const int ToolWindow = 0x80, NoActivate = 0x08000000, ClickThrough = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rectangle
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ThreadInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rectangle CaretRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime { public uint Low, High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo { public uint Size, Flags; public IntPtr Cursor; public ScreenPoint Position; }

        internal static IntPtr Root(IntPtr handle) { return GetAncestor(handle, 2); }
        internal static uint Thread(IntPtr handle, out uint pid) { return NativeMouse.GetWindowThreadProcessId(handle, out pid); }

        internal static string Title(IntPtr handle)
        {
            var result = new StringBuilder(512);
            GetWindowText(handle, result, result.Capacity);
            return result.ToString().Trim();
        }

        internal static string Class(IntPtr handle)
        {
            var result = new StringBuilder(256);
            GetClassName(handle, result, result.Capacity);
            return result.ToString();
        }

        internal static string StableClass(IntPtr handle)
        {
            var name = Class(handle);
            var split = name.StartsWith("HwndWrapper[", StringComparison.Ordinal) ? name.IndexOf(';') :
                name.StartsWith("WindowsForms", StringComparison.Ordinal) ? name.IndexOf(".app.", StringComparison.Ordinal) : -1;
            return split > 0 ? name.Substring(0, split) : name;
        }

        internal static bool Eligible(IntPtr handle, bool allowPopup = false)
        {
            if (handle == IntPtr.Zero || handle == GetShellWindow() || handle == GetDesktopWindow()) return false;
            uint pid;
            if (Thread(handle, out pid) == 0 || pid == OwnProcess) return false;
            var style = Style(handle);
            if ((!allowPopup && (style & ToolWindow) != 0) || (style & (ClickThrough | 0x80000)) == (ClickThrough | 0x80000)) return false;
            var name = Class(handle);
            return name != "Shell_TrayWnd" && name != "Shell_SecondaryTrayWnd" && name != "Progman" &&
                name != "WorkerW" && (allowPopup || name != "#32768") && name != "tooltips_class32";
        }

        internal static IntPtr ForegroundTarget()
        {
            var handle = Root(GetForegroundWindow());
            return Eligible(handle) ? handle : IntPtr.Zero;
        }

        internal static bool IsFullscreen(IntPtr handle)
        {
            Rectangle frame, client;
            if (!Frame(handle, out frame) || !GetClientRect(handle, out client)) return false;
            var origin = new ScreenPoint(0, 0);
            if (!ClientToScreen(handle, ref origin)) return false;
            var bounds = Forms.Screen.FromRectangle(new Drawing.Rectangle(frame.Left, frame.Top, frame.Width, frame.Height)).Bounds;
            const int tolerance = 3;
            return frame.Width >= bounds.Width - tolerance && frame.Height >= bounds.Height - tolerance &&
                Math.Abs(origin.X - bounds.Left) <= tolerance && Math.Abs(origin.Y - bounds.Top) <= tolerance &&
                Math.Abs(client.Width - bounds.Width) <= tolerance && Math.Abs(client.Height - bounds.Height) <= tolerance;
        }

        internal static bool IsMinecraft(IntPtr handle)
        {
            return IsMinecraft(Class(handle), Title(handle));
        }

        internal static bool IsMinecraft(string windowClass, string title)
        {
            return windowClass.StartsWith("GLFW", StringComparison.OrdinalIgnoreCase) &&
                (title.StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("我的世界", StringComparison.Ordinal) ||
                title.IndexOf("朽木呓语", StringComparison.Ordinal) >= 0);
        }

        internal static IntPtr RecognizeGameWindow(IntPtr foreground)
        {
            if (VisibleMinecraft(foreground)) return foreground;
            var match = IntPtr.Zero;
            foreach (var handle in TopWindows())
            {
                if (!VisibleMinecraft(handle)) continue;
                if (match != IntPtr.Zero) return IntPtr.Zero;
                match = handle;
            }
            return match;
        }

        private static bool VisibleMinecraft(IntPtr handle)
        {
            Rectangle area;
            return Eligible(handle) && IsMinecraft(handle) && Frame(handle, out area);
        }

        internal static bool CursorCaptured()
        {
            var info = new CursorInfo { Size = (uint)Marshal.SizeOf(typeof(CursorInfo)) };
            Rectangle clip;
            return (GetCursorInfo(ref info) && (info.Flags & 1) == 0) ||
                (GetClipCursor(out clip) && clip.Width <= 16 && clip.Height <= 16);
        }

        internal static IntPtr TargetAt(ScreenPoint point)
        {
            var handle = Root(NativeMouse.WindowFromPoint(point));
            bool awaitingFrame;
            if (PickableAt(handle, point, out awaitingFrame)) return handle;
            if (awaitingFrame) return IntPtr.Zero;
            foreach (var candidate in TopWindows())
            {
                if (candidate == handle) continue;
                if (PickableAt(candidate, point, out awaitingFrame)) return candidate;
                if (awaitingFrame) return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        internal static IntPtr DirectTargetAt(ScreenPoint point)
        {
            var handle = Root(NativeMouse.WindowFromPoint(point));
            bool awaitingFrame;
            return PickableAt(handle, point, out awaitingFrame) ? handle : IntPtr.Zero;
        }

        private static bool PickableAt(IntPtr handle, ScreenPoint point, out bool awaitingFrame)
        {
            awaitingFrame = false;
            Rectangle bounds;
            if (!Eligible(handle, true) || !IsWindowEnabled(handle) || !NativeFrame(handle, out bounds) || !Contains(bounds, point)) return false;
            if (!ShellMenu.Frame(handle, bounds, out bounds)) { awaitingFrame = true; return false; }
            if (!Contains(bounds, point)) return false;
            var region = CreateRectRgn(0, 0, 0, 0);
            if (region == IntPtr.Zero) return true;
            try
            {
                return GetWindowRgn(handle, region) == 0 || !GetWindowRect(handle, out bounds) ||
                    PtInRegion(region, point.X - bounds.Left, point.Y - bounds.Top);
            }
            finally { DeleteObject(region); }
        }

        internal static bool Contains(Rectangle rect, ScreenPoint point)
        {
            return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
        }

        internal static List<IntPtr> TopWindows()
        {
            var handles = new List<IntPtr>();
            EnumWindows((handle, unused) => { handles.Add(handle); return true; }, IntPtr.Zero);
            return handles;
        }

        internal static bool Frame(IntPtr handle, out Rectangle bounds)
        {
            return NativeFrame(handle, out bounds) && ShellMenu.Frame(handle, bounds, out bounds);
        }

        private static bool NativeFrame(IntPtr handle, out Rectangle bounds)
        {
            bounds = default(Rectangle);
            if (!VisibleSurface(handle)) return false;
            if (DwmBounds(handle, 9, out bounds, Marshal.SizeOf(typeof(Rectangle))) != 0 && !GetWindowRect(handle, out bounds))
                return false;
            return bounds.Width > 0 && bounds.Height > 0;
        }

        internal static bool VisibleSurface(IntPtr handle)
        {
            int cloaked;
            return handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle) &&
                (DwmNumber(handle, 14, out cloaked, sizeof(int)) != 0 || cloaked == 0);
        }

        internal static bool IsPopup(IntPtr handle, string image)
        {
            return !Eligible(handle) || GetWindow(handle, 4) != IntPtr.Zero ||
                ShellMenu.IsMenuProcess(image);
        }

        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr handle, IntPtr region);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);

        internal static int CornerPreference(IntPtr handle)
        {
            int value;
            return DwmNumber(handle, 33, out value, sizeof(int)) == 0 ? value : 1;
        }

        internal static IntPtr Focused(IntPtr root, uint rootThread, uint process)
        {
            var info = new ThreadInfo { Size = (uint)Marshal.SizeOf(typeof(ThreadInfo)) };
            uint pid;
            return GetGUIThreadInfo(rootThread, ref info) && info.Focus != IntPtr.Zero &&
                Thread(info.Focus, out pid) != 0 && pid == process && Root(info.Focus) == root ? info.Focus : IntPtr.Zero;
        }

        internal static void Identity(uint pid, out string image, out ulong created)
        {
            image = null;
            created = 0;
            var process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return;
            try
            {
                FileTime start, exit, kernel, user;
                if (GetProcessTimes(process, out start, out exit, out kernel, out user))
                    created = ((ulong)start.High << 32) | start.Low;
                var text = new StringBuilder(1024);
                var length = text.Capacity;
                if (QueryFullProcessImageName(process, 0, text, ref length)) image = text.ToString();
            }
            finally { CloseHandle(process); }
        }

        internal static int Style(IntPtr hwnd) { return GetWindowLong(hwnd, -20); }
        internal static void AddStyle(IntPtr hwnd, int flags) { SetWindowLong(hwnd, -20, Style(hwnd) | flags); }
        internal static void SetNoActivate(IntPtr hwnd, bool value)
        {
            var old = Style(hwnd);
            var next = value ? old | NoActivate : old & ~NoActivate;
            if (old != next) SetWindowLong(hwnd, -20, next);
        }

        internal static uint Dpi(IntPtr handle)
        {
            try { var dpi = GetDpiForWindow(handle); return dpi == 0 ? 96u : dpi; }
            catch (EntryPointNotFoundException) { return 96; }
        }

        internal static ScreenPoint Cursor()
        {
            ScreenPoint point;
            GetCursorPos(out point);
            return point;
        }

        private delegate bool Enumerator(IntPtr handle, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerator callback, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr handle, uint relation);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr handle, out Rectangle rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr handle, out Rectangle rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr handle, ref ScreenPoint point);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr handle, ref ScreenPoint point);
        [DllImport("user32.dll")] internal static extern IntPtr ChildWindowFromPointEx(IntPtr handle, ScreenPoint point, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int left, int top, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")] internal static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr handle, IntPtr after,
            int left, int top, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool EndDeferWindowPos(IntPtr batch);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] private static extern bool GetClipCursor(out Rectangle rect);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref ThreadInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmBounds(IntPtr handle, int id, out Rectangle rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmNumber(IntPtr handle, int id, out int value, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr handle, out FileTime created, out FileTime exited,
            out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags,
            StringBuilder path, ref int length);
    }

    internal sealed class TargetBinding
    {
        internal sealed class Handle
        {
            public readonly IntPtr Window;
            public readonly uint Process, Thread;
            public readonly ulong Created;
            public readonly string Image;

            public Handle(IntPtr window)
            {
                Window = window;
                Thread = Windows.Thread(window, out Process);
                Windows.Identity(Process, out Image, out Created);
            }

            public Handle(SavedTarget saved)
            {
                Process = saved.Process;
                Created = saved.Created;
                Image = saved.Image;
            }

            public bool IsCurrent
            {
                get
                {
                    uint pid;
                    return Thread != 0 && Windows.Thread(Window, out pid) == Thread && pid == Process;
                }
            }

            public bool Owns(IntPtr child)
            {
                uint pid;
                return child != IntPtr.Zero && Windows.Thread(child, out pid) != 0 && pid == Process && Windows.Root(child) == Window;
            }
        }

        private volatile Handle handle;
        private readonly string identityTitle, identityClass;
        private readonly bool restartAllowed, sameProcessAllowed, popup;
        private IntPtr focusedChild, mouseChild;
        private ScreenPoint mousePoint;
        public readonly double X, Y;
        public readonly bool HasAnchor;

        private TargetBinding(Handle handle, double x, double y, bool hasAnchor)
        {
            this.handle = handle;
            X = x;
            Y = y;
            HasAnchor = hasAnchor;
            identityTitle = Windows.Title(handle.Window);
            identityClass = Windows.StableClass(handle.Window);
            popup = Windows.IsPopup(handle.Window, handle.Image);
            var siblings = Windows.TopWindows().FindAll(other => other != handle.Window && Matches(other));
            sameProcessAllowed = !popup && !siblings.Exists(other => IsSameProcess(handle, other));
            restartAllowed = !popup && !String.IsNullOrWhiteSpace(identityTitle) && handle.Created != 0 && handle.Image != null &&
                !siblings.Exists(other => SameImage(handle, other));
            focusedChild = Windows.Focused(handle.Window, handle.Thread, handle.Process);
            RefreshMouse(handle);
        }

        private TargetBinding(SavedTarget saved)
        {
            identityTitle = saved.Title;
            identityClass = saved.WindowClass;
            handle = new Handle(saved);
            popup = saved.Popup;
            var previousWindow = new IntPtr(saved.Window);
            if (saved.Created != 0 && MatchesClass(previousWindow))
            {
                var candidate = new Handle(previousWindow);
                if (candidate.IsCurrent && candidate.Process == saved.Process && candidate.Created == saved.Created)
                {
                    handle = candidate;
                    popup |= Windows.IsPopup(candidate.Window, candidate.Image);
                    focusedChild = Windows.Focused(candidate.Window, candidate.Thread, candidate.Process);
                    RefreshMouse(candidate);
                }
            }
            sameProcessAllowed = !popup && saved.SameProcessAllowed;
            restartAllowed = !popup && saved.RestartAllowed && !String.IsNullOrEmpty(saved.Image) && saved.Created != 0;
            X = saved.X;
            Y = saved.Y;
            HasAnchor = saved.HasAnchor || X != .5 || Y != .5;
        }

        public static TargetBinding Restore(SavedTarget saved)
        {
            if (saved == null || String.IsNullOrWhiteSpace(saved.Title) || String.IsNullOrWhiteSpace(saved.WindowClass) ||
                (IntPtr.Size == 4 && (saved.Window < Int32.MinValue || saved.Window > Int32.MaxValue)) ||
                !(saved.X >= 0 && saved.X < 1) || !(saved.Y >= 0 && saved.Y < 1)) return null;
            var restored = new TargetBinding(saved);
            restored.Resolve();
            return restored;
        }

        public SavedTarget Export()
        {
            var current = handle;
            var caption = current.IsCurrent ? Windows.Title(current.Window) : identityTitle;
            return new SavedTarget { Title = String.IsNullOrWhiteSpace(caption) ? identityClass : caption,
                WindowClass = identityClass, Window = current.Window.ToInt64(), X = X, Y = Y,
                Image = current.Image, Process = current.Process, Created = current.Created, RestartAllowed = restartAllowed,
                SameProcessAllowed = sameProcessAllowed, HasAnchor = HasAnchor, Popup = popup };
        }

        public IntPtr Window { get { var current = handle; return IsAvailable(current) ? current.Window : IntPtr.Zero; } }
        public bool RemembersGame { get { return Windows.IsMinecraft(identityClass, identityTitle); } }
        public string Name
        {
            get
            {
                var current = Window;
                var name = current == IntPtr.Zero ? identityTitle : Windows.Title(current);
                return !String.IsNullOrWhiteSpace(name) ? name : !String.IsNullOrWhiteSpace(identityTitle) ? identityTitle : identityClass;
            }
        }

        public string ShortName
        {
            get { return ShortenTitle(Name); }
        }

        public string ShortenTitle(string name)
        {
            const string modpack = "朽木呓语";
            return identityClass.StartsWith("GLFW", StringComparison.OrdinalIgnoreCase) &&
                name.IndexOf(modpack, StringComparison.Ordinal) >= 0 ? modpack : name;
        }

        public static TargetBinding Create(IntPtr window, ScreenPoint? screen = null)
        {
            if (!Windows.Eligible(window, true)) return null;
            var entity = new Handle(window);
            if (!entity.IsCurrent) return null;
            double x = 0.5, y = 0.5;
            if (screen.HasValue)
            {
                var pt = screen.Value;
                Windows.Rectangle rect;
                if (!Windows.ScreenToClient(window, ref pt) || !Windows.GetClientRect(window, out rect) || !Windows.Contains(rect, pt)) return null;
                x = (pt.X + 0.5) / rect.Width;
                y = (pt.Y + 0.5) / rect.Height;
            }
            return new TargetBinding(entity, x, y, screen.HasValue);
        }

        public TargetBinding ReconnectGame(IntPtr window)
        {
            const string modpack = "朽木呓语";
            if (!RemembersGame || !Windows.Eligible(window) || !Windows.IsMinecraft(window)) return null;
            var oldModpack = identityTitle.IndexOf(modpack, StringComparison.Ordinal) >= 0;
            if (oldModpack != (Windows.Title(window).IndexOf(modpack, StringComparison.Ordinal) >= 0)) return null;
            var candidate = new Handle(window);
            return candidate.IsCurrent ? new TargetBinding(candidate, X, Y, HasAnchor) : null;
        }

        private bool MatchesClass(IntPtr window)
        {
            return Windows.Eligible(window, true) && String.Equals(Windows.StableClass(window), identityClass, StringComparison.Ordinal);
        }

        private bool Matches(IntPtr window)
        {
            return MatchesClass(window) && String.Equals(Windows.Title(window), identityTitle, StringComparison.Ordinal);
        }

        private static bool SameImage(Handle previous, IntPtr candidate)
        {
            uint process;
            if (Windows.Thread(candidate, out process) == 0) return false;
            if (process == previous.Process) return true;
            string image;
            ulong created;
            Windows.Identity(process, out image, out created);
            return String.Equals(previous.Image, image, StringComparison.OrdinalIgnoreCase) && image != null;
        }

        private static bool IsSameProcess(Handle previous, IntPtr window)
        {
            uint process;
            return Windows.Thread(window, out process) != 0 && process == previous.Process;
        }

        public Handle Resolve()
        {
            var previous = handle;
            if (IsAvailable(previous)) return previous;
            if (popup) return null;
            Handle found = null;
            foreach (var window in Windows.TopWindows())
            {
                if (!Matches(window)) continue;
                var candidate = new Handle(window);
                if (!candidate.IsCurrent) continue;
                var sameInstance = sameProcessAllowed && candidate.Process == previous.Process && candidate.Created != 0 && candidate.Created == previous.Created;
                var restarted = restartAllowed && candidate.Created > previous.Created && candidate.Image != null &&
                    String.Equals(candidate.Image, previous.Image, StringComparison.OrdinalIgnoreCase);
                if (!sameInstance && !restarted) continue;
                if (found != null) return null;
                found = candidate;
            }
            if (found == null) return null;
            focusedChild = Windows.Focused(found.Window, found.Thread, found.Process);
            mouseChild = IntPtr.Zero;
            RefreshMouse(found);
            handle = found;
            return found;
        }

        private bool IsAvailable(Handle current)
        {
            if (!current.IsCurrent || popup && !Windows.VisibleSurface(current.Window)) return false;
            if (!popup || !ShellMenu.IsMenuProcess(current.Image)) return true;
            Windows.Rectangle frame;
            ScreenPoint point;
            return Windows.Frame(current.Window, out frame) && (!HasAnchor || ClientAnchor(current.Window, out point) &&
                Windows.ClientToScreen(current.Window, ref point) && Windows.Contains(frame, point));
        }

        public IntPtr KeyDestination(Handle current)
        {
            if (HasAnchor)
            {
                // A reticle placed on an actual native text field selects that field, even while its window is inactive.
                if (!Windows.IsIconic(current.Window)) RefreshMouse(current);
                if (mouseChild != current.Window && current.Owns(mouseChild))
                {
                    var control = Windows.Class(mouseChild);
                    if (String.Equals(control, "Edit", StringComparison.OrdinalIgnoreCase) ||
                        control.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
                        control.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)) return mouseChild;
                }
            }
            var focus = Windows.Focused(current.Window, current.Thread, current.Process);
            if (focus != IntPtr.Zero) focusedChild = focus;
            return current.Owns(focusedChild) ? focusedChild : current.Window;
        }

        public bool MouseDestination(Handle current, out IntPtr child, out ScreenPoint point)
        {
            if (!Windows.IsIconic(current.Window)) RefreshMouse(current);
            child = mouseChild;
            point = mousePoint;
            return current.Owns(child);
        }

        public bool ScreenAnchor(out ScreenPoint point)
        {
            point = default(ScreenPoint);
            var window = Window;
            return !Windows.IsIconic(window) && ClientAnchor(window, out point) && Windows.ClientToScreen(window, ref point);
        }

        private bool ClientAnchor(IntPtr window, out ScreenPoint point)
        {
            point = default(ScreenPoint);
            Windows.Rectangle rect;
            if (!Windows.GetClientRect(window, out rect) || rect.Width <= 0 || rect.Height <= 0) return false;
            point = new ScreenPoint(Math.Min(32767, Math.Min(rect.Width - 1, (int)(rect.Width * X))),
                Math.Min(32767, Math.Min(rect.Height - 1, (int)(rect.Height * Y))));
            return true;
        }

        private void RefreshMouse(Handle current)
        {
            ScreenPoint point;
            if (!ClientAnchor(current.Window, out point)) return;
            var child = current.Window;
            for (var depth = 0; depth < 12; depth++)
            {
                // Hidden top-level windows keep their controls and last client positions.
                var next = Windows.ChildWindowFromPointEx(child, point, 0x0002 | 0x0004);
                if (next == child || !current.Owns(next)) break;
                var mapped = point;
                Windows.Rectangle bounds;
                if (!Windows.ClientToScreen(child, ref mapped) || !Windows.ScreenToClient(next, ref mapped) ||
                    !Windows.GetClientRect(next, out bounds) || !Windows.Contains(bounds, mapped)) break;
                child = next;
                point = mapped;
            }
            mouseChild = child;
            mousePoint = point;
        }
    }

    internal sealed class SavedTarget
    {
        public bool Popup { get; set; }
        public string Title { get; set; }
        public string WindowClass { get; set; }
        public long Window { get; set; }
        public string Image { get; set; }
        public uint Process { get; set; }
        public ulong Created { get; set; }
        public bool RestartAllowed { get; set; }
        public bool SameProcessAllowed { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool HasAnchor { get; set; }
    }
}
