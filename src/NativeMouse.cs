using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal enum ClickButton { Left, Right }
internal enum ClickOutcome { Sent, Wait, LostPoint, Blocked, Busy }

[StructLayout(LayoutKind.Sequential)]
internal struct ScreenPoint
{
    public int X, Y;

    public ScreenPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString()
    {
        return "坐标 " + X + ", " + Y;
    }
}

internal static class NativeMouse
{
    private const uint Move = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint VirtualDesktop = 0x4000;
    private const uint Absolute = 0x8000;
    private const uint NoRepeat = 0x4000;
    private const uint ControlAndShift = 0x0002 | 0x0004;
    private static readonly uint OwnerProcess = (uint)Process.GetCurrentProcess().Id;

    public static ScreenPoint? Capture()
    {
        ScreenPoint cursor;
        if (!GetCursorPos(out cursor) || IsOwnWindow(cursor)) return null;
        return cursor;
    }

    public static ClickOutcome Click(ClickButton button, ScreenPoint? target)
    {
        ScreenPoint cursor;
        if (!GetCursorPos(out cursor)) return ClickOutcome.Blocked;
        ScreenPoint clickPoint = target.GetValueOrDefault(cursor);
        if (IsOwnWindow(clickPoint) || IsMouseHeld()) return ClickOutcome.Wait;
        if (target.HasValue && MonitorFromPoint(clickPoint, 0) == IntPtr.Zero) return ClickOutcome.LostPoint;

        uint down = button == ClickButton.Right ? RightDown : LeftDown;
        uint up = button == ClickButton.Right ? RightUp : LeftUp;
        bool moving = target.HasValue && (cursor.X != clickPoint.X || cursor.Y != clickPoint.Y);
        NativeInput.Packet back = default(NativeInput.Packet);
        NativeInput.Packet[] events;
        if (moving)
        {
            int left = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
            int top = GetSystemMetrics(77);    // SM_YVIRTUALSCREEN
            int width = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
            int height = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
            if (width <= 0 || height <= 0) return ClickOutcome.Blocked;
            back = MoveTo(cursor, left, top, width, height);
            events = new[] { MoveTo(clickPoint, left, top, width, height), Packet(down), Packet(up), back };
        }
        else
        {
            events = new[] { Packet(down), Packet(up) };
        }
        uint sent = NativeInput.Send(events);
        if (sent == events.Length) return ClickOutcome.Sent;

        // If only part of the sequence was accepted, release the button and restore the pointer.
        if (sent == (moving ? 2 : 1))
            NativeInput.Send(new[] { Packet(up) });
        if (moving && sent > 0)
            NativeInput.Send(new[] { back });
        return ClickOutcome.Blocked;
    }

    private static bool IsMouseHeld()
    {
        return NativeInput.IsHeld(1) || NativeInput.IsHeld(2) || NativeInput.IsHeld(4);
    }

    private static bool IsOwnWindow(ScreenPoint point)
    {
        IntPtr window = WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        uint process;
        GetWindowThreadProcessId(window, out process);
        return process == OwnerProcess;
    }

    private static NativeInput.Packet MoveTo(ScreenPoint point, int left, int top, int width, int height)
    {
        return Packet(Move | VirtualDesktop | Absolute,
            NormalizeCoordinate(point.X, left, width), NormalizeCoordinate(point.Y, top, height));
    }

    internal static int NormalizeCoordinate(int pixel, int origin, int span)
    {
        long normalized = (((long)pixel - origin) * 65536 + 32768) / span;
        return (int)Math.Max(0, Math.Min(65535, normalized));
    }

    private static NativeInput.Packet Packet(uint flags, int x = 0, int y = 0)
    {
        return new NativeInput.Packet { Data = new NativeInput.Payload {
            Mouse = new NativeInput.MouseEvent { X = x, Y = y, Flags = flags },
        } };
    }

    internal static string RegisterShortcut(IntPtr window, int id, uint key, string label, bool modifiedOnly = false)
    {
        if (!modifiedOnly && RegisterHotKey(window, id, NoRepeat, key)) return label;
        if (RegisterHotKey(window, id, NoRepeat | ControlAndShift, key)) return "Ctrl+Shift+" + label;
        return null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(ScreenPoint point, uint flags);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);
}
