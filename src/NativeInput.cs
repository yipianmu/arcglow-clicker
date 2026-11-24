using System;
using System.Runtime.InteropServices;

internal static class NativeInput
{
    private const uint KeyboardType = 1;
    private const uint Extended = 0x0001;
    private const uint KeyUp = 0x0002;
    private static readonly uint Owner = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    internal static readonly int InputSize = Marshal.SizeOf(typeof(Packet));

    internal static ClickOutcome PressKey(int virtualKey)
    {
        if (virtualKey < 1 || virtualKey > 255) return ClickOutcome.Blocked;
        uint process;
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return ClickOutcome.Wait;
        uint thread = NativeMouse.GetWindowThreadProcessId(foreground, out process);
        if (thread == 0 || process == Owner || IsHeld(virtualKey) || ModifiersHeld())
            return ClickOutcome.Wait;

        uint scan = ScanCode(virtualKey, thread);
        uint flags = IsExtended(scan) ? Extended : 0;
        Packet down = KeyPacket(virtualKey, scan, flags);
        Packet up = KeyPacket(virtualKey, scan, flags | KeyUp);
        uint sent = Send(new[] { down, up });
        if (sent == 2) return ClickOutcome.Sent;
        if (sent == 1) Send(new[] { up });
        return ClickOutcome.Blocked;
    }

    internal static uint Send(Packet[] packets)
    {
        return SendInput((uint)packets.Length, packets, InputSize);
    }

    internal static bool IsHeld(int virtualKey)
    {
        return GetAsyncKeyState(virtualKey) < 0;
    }

    private static bool ModifiersHeld()
    {
        return IsHeld(0x10) || IsHeld(0x11) || IsHeld(0x12) || IsHeld(0x5B) || IsHeld(0x5C);
    }

    internal static uint ScanCode(int virtualKey, uint thread)
    {
        return MapVirtualKeyEx((uint)virtualKey, 4, GetKeyboardLayout(thread)); // MAPVK_VK_TO_VSC_EX
    }

    internal static bool IsExtended(uint scan)
    {
        return (scan & 0xFF00) == 0xE000;
    }

    internal static Packet KeyPacket(int virtualKey, uint scan, uint flags)
    {
        return new Packet
        {
            Type = KeyboardType,
            Data = new Payload { Keyboard = new KeyboardEvent {
                VirtualKey = (ushort)virtualKey, ScanCode = (ushort)(scan & 0xFF), Flags = flags,
            } },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Packet
    {
        public uint Type;
        public Payload Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct Payload
    {
        [FieldOffset(0)] public MouseEvent Mouse;
        [FieldOffset(0)] public KeyboardEvent Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseEvent
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardEvent
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Packet[] events, int size);
}
