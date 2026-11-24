using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GlassClicker
{
    internal enum RunPhase { Idle, Delay, Active, Waiting, Paused, Complete, Stopped }

    internal sealed class RunState
    {
        public readonly RunPhase Phase;
        public readonly long Count;
        public readonly string Detail;
        public readonly bool Holding;
        public readonly bool PausedForEditing;
        public RunState(RunPhase phase, long count, string detail = null, bool holding = false, bool pausedForEditing = false)
        {
            Phase = phase; Count = count; Detail = detail; Holding = holding; PausedForEditing = pausedForEditing;
        }
        public bool Running { get { return Phase == RunPhase.Delay || Phase == RunPhase.Active || Phase == RunPhase.Waiting; } }
        public bool HasSession { get { return Running || Phase == RunPhase.Paused; } }
    }

    internal sealed class RepeatWorker
    {
        private const int FirstBusyRetry = 40, MaxBusyRetry = 240;
        private readonly object sync = new object();
        private CancellationTokenSource active;
        private ManualResetEvent pause, changes;
        private TargetBinding requestedTarget;
        private ActionSequence requestedAction;
        private double requestedInterval;
        private bool manuallyPaused, editingPaused;
        private bool stopRequested;
        private long pausedAt;
        private Thread runThread;
        private volatile RunState state = new RunState(RunPhase.Idle, 0);
        public RunState State { get { return state; } }

        public bool Start(TargetBinding target, ActionSequence sequence, double interval, int delay, long limit, long origin,
            int hold = 0, bool continuousHold = false, int variation = 0, int runSeconds = 0, bool pauseForEditing = false)
        {
            if (continuousHold && sequence.Steps.Length != 1) return false;
            lock (sync)
            {
                if (active != null) return false;
                var cancellation = new CancellationTokenSource();
                active = cancellation;
                requestedTarget = target;
                requestedAction = sequence;
                requestedInterval = interval;
                pause = new ManualResetEvent(pauseForEditing);
                changes = new ManualResetEvent(false);
                manuallyPaused = false;
                editingPaused = pauseForEditing;
                pausedAt = origin;
                stopRequested = false;
                state = new RunState(RunPhase.Delay, 0);
                runThread = new Thread(() => Run(target, sequence, interval, delay, limit, origin, hold, continuousHold, variation, cancellation))
                    { IsBackground = true, Name = "Window repetition" };
                if (runSeconds > 0) cancellation.CancelAfter(delay + runSeconds * 1000);
                runThread.Start();
                return true;
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (active == null) return;
                stopRequested = true;
                active.Cancel();
            }
        }

        public void TogglePause()
        {
            lock (sync)
            {
                if (active == null || active.IsCancellationRequested) return;
                manuallyPaused = !manuallyPaused;
                SignalPauseChange();
            }
        }

        public void SetEditingPause(bool value)
        {
            lock (sync)
            {
                if (active == null || active.IsCancellationRequested || editingPaused == value) return;
                editingPaused = value;
                SignalPauseChange();
            }
        }

        private void SignalPauseChange()
        {
            if ((manuallyPaused || editingPaused) && !pause.WaitOne(0))
            {
                pausedAt = Stopwatch.GetTimestamp();
                pause.Set();
            }
            Monitor.PulseAll(sync);
        }

        public void WaitForStop(int timeout)
        {
            Thread thread;
            lock (sync) thread = runThread;
            if (thread != null) thread.Join(timeout);
        }

        public bool SwitchWindow(TargetBinding target)
        {
            lock (sync)
            {
                if (active == null || active.IsCancellationRequested) return false;
                if (target == requestedTarget) return true;
                requestedTarget = target;
                changes.Set();
                return true;
            }
        }

        public void SwitchAction(ActionSequence action)
        {
            lock (sync)
            {
                if (active == null || active.IsCancellationRequested || action == requestedAction) return;
                requestedAction = action;
                changes.Set();
            }
        }

        public void SwitchInterval(double interval)
        {
            lock (sync)
            {
                if (active == null || active.IsCancellationRequested || interval == requestedInterval) return;
                requestedInterval = interval;
                changes.Set();
            }
        }

        private void WaitWhilePaused(CancellationToken token, ref long due, ref long cycleStart, long count)
        {
            using (token.Register(WakePause))
            lock (sync)
            {
                var previous = state;
                while ((manuallyPaused || editingPaused) && !token.IsCancellationRequested)
                {
                    state = new RunState(RunPhase.Paused, count, pausedForEditing: editingPaused && !manuallyPaused);
                    Monitor.Wait(sync);
                }
                if (token.IsCancellationRequested) return;
                var timePaused = Stopwatch.GetTimestamp() - pausedAt;
                due += timePaused;
                cycleStart += timePaused;
                pause.Reset();
                state = new RunState(previous.Phase, count, previous.Detail);
            }
        }

        private void WakePause()
        {
            lock (sync) Monitor.PulseAll(sync);
        }

        private void Run(TargetBinding target, ActionSequence sequence, double interval, int delay, long limit,
            long origin, int timedHold, bool continuousHold, int variation, CancellationTokenSource cancellation)
        {
            var token = cancellation.Token;
            long count = 0;
            int step = 0;
            var hold = continuousHold ? -1 : timedHold;
            var busyRetry = FirstBusyRetry;
            var completed = false;
            var phase = RunPhase.Delay;
            var due = origin + WaitClock.Ticks(delay);
            var cycleStart = due;
            var random = variation == 0 ? null : new Random(unchecked((int)Stopwatch.GetTimestamp()));
            try
            {
                using (var clock = new WaitClock(token, pause, changes))
                {
                    var sender = new WindowSender(clock);
                    Action<string> onHold = chord =>
                    {
                        if (hold < 0)
                        {
                            phase = RunPhase.Active;
                            count = Math.Max(count, 1);
                        }
                        state = new RunState(RunPhase.Active, count, chord, hold < 0);
                    };
                    try
                    {
                        while (!clock.Cancelled)
                        {
                            if (clock.Paused)
                            {
                                sender.Finish();
                                WaitWhilePaused(token, ref due, ref cycleStart, count);
                                continue;
                            }
                            if (clock.Changing)
                            {
                                bool actionChanged, targetChanged;
                                var previousInterval = interval;
                                lock (sync)
                                {
                                    actionChanged = sequence != requestedAction;
                                    targetChanged = target != requestedTarget;
                                    sequence = requestedAction;
                                    target = requestedTarget;
                                    interval = requestedInterval;
                                    changes.Reset();
                                }
                                if (actionChanged || targetChanged) busyRetry = FirstBusyRetry;
                                if (actionChanged)
                                {
                                    step = 0;
                                    if (hold < 0 && sequence.Steps.Length > 1)
                                    {
                                        hold = timedHold;
                                        if (limit > 0 && count >= limit) { completed = true; break; }
                                    }
                                    if (phase != RunPhase.Delay)
                                    {
                                        due = cycleStart = Stopwatch.GetTimestamp();
                                        state = new RunState(phase, count, phase == RunPhase.Waiting ? state.Detail : null);
                                    }
                                }
                                else if (targetChanged && (phase == RunPhase.Waiting || phase == RunPhase.Active && hold < 0))
                                    due = Stopwatch.GetTimestamp();
                                else if (previousInterval != interval && phase == RunPhase.Active && step == 0 && hold >= 0)
                                    due = Math.Max(Stopwatch.GetTimestamp(), due + WaitClock.Ticks(interval - previousInterval));
                                continue;
                            }
                            if (!clock.Until(due)) continue;
                            ClickOutcome outcome;
                            long pressStarted = 0;
                            try
                            {
                                var destination = target.Resolve();
                                outcome = destination == null ? ClickOutcome.LostPoint :
                                    sender.Send(target, destination, sequence.Steps[step], interval, hold, hold == 0 ? null : onHold, out pressStarted);
                            }
                            catch (System.ComponentModel.Win32Exception) { outcome = ClickOutcome.Blocked; }
                            catch (InvalidOperationException) { outcome = ClickOutcome.Wait; }
                            if (token.IsCancellationRequested) break;
                            var now = Stopwatch.GetTimestamp();
                            if ((clock.Paused || clock.Changing) && (hold < 0 || outcome != ClickOutcome.Sent)) continue;
                            if (outcome != ClickOutcome.Sent)
                            {
                                var message = outcome == ClickOutcome.LostPoint ? "等目标窗口恢复" : outcome == ClickOutcome.Busy ? "等窗口响应" :
                                    outcome == ClickOutcome.Blocked ? "等窗口接收权限" : "正在重试发送";
                                if (phase != RunPhase.Waiting || state.Detail != message)
                                    state = new RunState(RunPhase.Waiting, count, message);
                                phase = RunPhase.Waiting;
                                var retry = 550;
                                if (outcome == ClickOutcome.Busy)
                                {
                                    retry = busyRetry;
                                    busyRetry = Math.Min(busyRetry * 2, MaxBusyRetry);
                                }
                                else if (outcome == ClickOutcome.Wait) retry = 35;
                                due = now + WaitClock.Ticks(retry);
                                continue;
                            }
                            busyRetry = FirstBusyRetry;
                            if (step == 0) cycleStart = pressStarted;
                            phase = RunPhase.Active;
                            if (++step < sequence.Steps.Length)
                            {
                                state = new RunState(phase, count, sequence.Steps[step - 1].Chord);
                                due = now + WaitClock.Ticks(sequence.Steps[step].Delay);
                                continue;
                            }
                            count++;
                            step = 0;
                            state = new RunState(phase, count);
                            if (limit > 0 && count >= limit) { completed = true; break; }
                            var spacing = WaitClock.Ticks(interval + (random == null ? 0 : random.Next(variation + 1)));
                            due = cycleStart + spacing;
                            if (due <= now) due = now + spacing;
                        }
                    }
                    finally { sender.Finish(); }
                }
            }
            finally
            {
                lock (sync)
                {
                    var timedOut = token.IsCancellationRequested && !stopRequested;
                    state = new RunState(completed || timedOut ? RunPhase.Complete : RunPhase.Stopped, count,
                        timedOut ? "已到运行时限" : null);
                    active = null;
                    requestedTarget = null;
                    requestedAction = null;
                    runThread = null;
                    pause.Dispose();
                    pause = null;
                    changes.Dispose();
                    changes = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    internal sealed class WaitClock : IDisposable
    {
        private readonly CancellationToken token;
        private readonly WaitHandle pause;
        private readonly WaitHandle changes;
        private readonly WaitHandle timer;
        private readonly WaitHandle[] interrupts;
        private readonly WaitHandle[] sources;
        public WaitClock(CancellationToken token, WaitHandle pause, WaitHandle changes)
        {
            this.token = token;
            this.pause = pause;
            this.changes = changes;
            interrupts = new[] { token.WaitHandle, pause, changes };
            try
            {
                var native = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x001F0003);
                if (native != IntPtr.Zero)
                {
                    timer = new TimerHandle(native);
                    sources = new[] { token.WaitHandle, pause, changes, timer };
                }
            }
            catch (EntryPointNotFoundException) { }
        }

        public static long Ticks(double milliseconds) { return (long)(milliseconds * Stopwatch.Frequency / 1000.0); }
        public bool Cancelled { get { return token.IsCancellationRequested; } }
        public bool Paused { get { return pause.WaitOne(0); } }
        public bool Changing { get { return changes.WaitOne(0); } }
        public bool Interrupted { get { return Cancelled || Paused || Changing; } }

        public bool Until(long deadline)
        {
            while (!Interrupted)
            {
                var remaining = (deadline - Stopwatch.GetTimestamp()) * (1000.0 / Stopwatch.Frequency);
                if (remaining <= 0) return true;
                var relativeDue = -(long)Math.Max(1, Math.Ceiling(remaining * 10000));
                if (timer != null && SetWaitableTimer(timer.SafeWaitHandle, ref relativeDue, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    if (WaitHandle.WaitAny(sources) != 3) return false;
                }
                else if (WaitHandle.WaitAny(interrupts, (int)Math.Ceiling(remaining)) != WaitHandle.WaitTimeout) return false;
            }
            return false;
        }

        public void Dispose() { if (timer != null) timer.Dispose(); }
        private sealed class TimerHandle : WaitHandle
        {
            public TimerHandle(IntPtr native) { SafeWaitHandle = new SafeWaitHandle(native, true); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWaitableTimerEx(
            IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetWaitableTimer(SafeWaitHandle timer,
            ref long due, int period, IntPtr callback, IntPtr argument, bool resume);
    }

    internal sealed class WindowSender
    {
        private struct Pair { public IntPtr Window, Key, DownData, UpData, UpKey; public uint Down, Up; }
        private sealed class InputGate
        {
            public readonly Dictionary<IntPtr, HashSet<int>> Held = new Dictionary<IntPtr, HashSet<int>>();
            public int Users;
        }
        private static readonly Dictionary<uint, InputGate> Gates = new Dictionary<uint, InputGate>();
        private readonly WaitClock clock;
        private readonly Pair[] keys = new Pair[8];
        private int pressed;
        private TargetBinding.Handle previous;
        private InputGate pressGate;
        private int[] reserved;
        private IntPtr readyKey, readyMouse;
        private long readyUntil;

        public WindowSender(WaitClock clock) { this.clock = clock; }

        public ClickOutcome Send(TargetBinding target, TargetBinding.Handle current, ActionSequence.Step step, double interval,
            int hold, Action<string> onHold, out long started)
        {
            started = 0;
            if (!ReleasePending()) return ClickOutcome.Blocked;
            var keyboard = target.KeyDestination(current);
            var mouse = IntPtr.Zero;
            var point = default(ScreenPoint);
            var hasMouse = step.Inputs.Any(key => key < 0);
            if (hasMouse && !target.MouseDestination(current, out mouse, out point)) return ClickOutcome.LostPoint;
            if (clock.Interrupted || !current.IsCurrent) return ClickOutcome.Wait;
            var now = Stopwatch.GetTimestamp();
            if (keyboard != readyKey || mouse != readyMouse || now >= readyUntil)
            {
                var status = Probe(keyboard);
                if (status == ClickOutcome.Sent && mouse != IntPtr.Zero && mouse != keyboard) status = Probe(mouse);
                if (status != ClickOutcome.Sent) return status;
                readyKey = keyboard;
                readyMouse = mouse;
                readyUntil = now + WaitClock.Ticks(210);
            }
            var gate = BorrowGate(current.Thread);
            var entered = false;
            try
            {
                Monitor.TryEnter(gate, 35, ref entered);
                if (!entered) return ClickOutcome.Wait;
                if (clock.Interrupted || !current.IsCurrent) return ClickOutcome.Wait;
                HashSet<int> held;
                if (gate.Held.TryGetValue(current.Window, out held) && Conflicts(held, step.Inputs)) return ClickOutcome.Wait;
                started = Stopwatch.GetTimestamp();
                Press(current, step.Inputs, keyboard, mouse, point);
                if (pressed != 0)
                {
                    if (held == null) gate.Held.Add(current.Window, held = new HashSet<int>());
                    pressGate = gate;
                    reserved = step.Inputs;
                    held.UnionWith(reserved);
                }
            }
            finally
            {
                if (entered) Monitor.Exit(gate);
                if (pressGate != gate) ReturnGate(current.Thread, gate);
            }
            var outcome = ClickOutcome.Blocked;
            try
            {
                if (pressed == step.Inputs.Length)
                {
                    if (onHold != null) onHold(step.Chord);
                    outcome = ClickOutcome.Sent;
                    if (hold < 0)
                    {
                        while (current.IsCurrent && clock.Until(Stopwatch.GetTimestamp() + WaitClock.Ticks(160))) { }
                        if (!clock.Interrupted) outcome = ClickOutcome.LostPoint;
                    }
                    else
                    {
                        var duration = hold > 0 ? hold : hasMouse
                            ? Math.Min(22, Math.Max(2, interval / 3))
                            : Math.Min(35, Math.Max(2, interval / 2));
                        clock.Until(Stopwatch.GetTimestamp() + WaitClock.Ticks(duration));
                    }
                }
            }
            finally { if (!ReleasePending()) outcome = ClickOutcome.Blocked; }
            return outcome;
        }

        private static bool Conflicts(HashSet<int> held, int[] next)
        {
            return next.Any(held.Contains) || held.Any(ActionSequence.IsModifier) || next.Any(ActionSequence.IsModifier) ||
                (held.Any(key => key < 0) && next.Any(key => key < 0));
        }

        private void Press(TargetBinding.Handle current, int[] inputs, IntPtr keyboard, IntPtr mouse, ScreenPoint point)
        {
            uint keyboardProcess;
            var keyThread = Windows.Thread(keyboard, out keyboardProcess);
            if (keyThread == 0 || keyboardProcess != current.Process) return;
            previous = current;
            var alt = false;
            var mouseFlags = (inputs.Contains(0x10) ? 4 : 0) | (inputs.Contains(0x11) ? 8 : 0);
            foreach (var key in inputs)
            {
                if (clock.Interrupted) break;
                var packet = key < 0 ? Mouse(key, mouse, point, mouseFlags) : Key(key, keyboard, keyThread, alt);
                if (key == 0x12) alt = true;
                if (!current.Owns(packet.Window)) break;
                if (key < 0 && !PostMessage(packet.Window, 0x0200, packet.UpKey, packet.DownData)) break;
                if (!PostMessage(packet.Window, packet.Down, packet.Key, packet.DownData)) break;
                keys[pressed++] = packet;
            }
        }

        private static InputGate BorrowGate(uint thread)
        {
            lock (Gates)
            {
                InputGate gate;
                if (!Gates.TryGetValue(thread, out gate)) Gates.Add(thread, gate = new InputGate());
                gate.Users++;
                return gate;
            }
        }

        private static void ReturnGate(uint thread, InputGate gate)
        {
            lock (Gates) if (--gate.Users == 0) Gates.Remove(thread);
        }

        private bool ReleasePending()
        {
            var gate = pressGate;
            if (gate == null) return true;
            lock (gate)
            {
                while (pressed > 0)
                {
                    var packet = keys[pressed - 1];
                    if (previous.IsCurrent && previous.Owns(packet.Window) &&
                        !PostMessage(packet.Window, packet.Up, packet.UpKey, packet.UpData)) return false;
                    pressed--;
                }
                ClearReservation(gate);
            }
            return true;
        }

        private void ClearReservation(InputGate gate)
        {
            var held = gate.Held[previous.Window];
            held.ExceptWith(reserved);
            if (held.Count == 0) gate.Held.Remove(previous.Window);
            ReturnGate(previous.Thread, gate);
            pressGate = null;
            reserved = null;
        }

        public void Finish()
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                if (ReleasePending()) return;
                Thread.Sleep(8);
            }
            var gate = pressGate;
            if (gate != null) lock (gate)
            {
                pressed = 0;
                ClearReservation(gate);
            }
        }

        internal static void PauseMinecraft(IntPtr window)
        {
            uint process;
            var thread = Windows.Thread(window, out process);
            if (thread == 0 || Windows.ForegroundTarget() != window || !Windows.IsMinecraft(window) ||
                !Windows.IsFullscreen(window) || !Windows.CursorCaptured()) return;
            var escape = Key(0x1B, window, thread, false);
            if (PostMessage(window, escape.Down, escape.Key, escape.DownData))
                PostMessage(window, escape.Up, escape.UpKey, escape.UpData);
        }

        private static ClickOutcome Probe(IntPtr window)
        {
            IntPtr discarded;
            SetLastError(0);
            if (SendMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0002 | 0x0020, 115, out discarded) != IntPtr.Zero)
                return ClickOutcome.Sent;
            return Marshal.GetLastWin32Error() == 5 ? ClickOutcome.Blocked : ClickOutcome.Busy;
        }

        private static Pair Key(int key, IntPtr window, uint thread, bool alt)
        {
            var scan = NativeInput.ScanCode(key, thread);
            var data = 1 | ((int)(scan & 255) << 16) | (NativeInput.IsExtended(scan) ? 1 << 24 : 0);
            var system = alt || key == 0x12 || key == 0x79;
            if (alt) data |= 1 << 29;
            var up = data | unchecked((int)0xC0000000) | (key == 0x12 ? 1 << 29 : 0);
            return new Pair { Window = window, Down = system ? 0x0104u : 0x0100u, Up = system ? 0x0105u : 0x0101u,
                Key = (IntPtr)key, UpKey = (IntPtr)key, DownData = (IntPtr)data, UpData = (IntPtr)up };
        }

        private static Pair Mouse(int key, IntPtr window, ScreenPoint point, int modifiers)
        {
            var position = (IntPtr)((point.Y << 16) | (point.X & 65535));
            var down = key == -1 ? 0x0201u : key == -2 ? 0x0204u : 0x0207u;
            var flag = key == -1 ? 1 : key == -2 ? 2 : 16;
            return new Pair { Window = window, Down = down, Up = down + 1, Key = (IntPtr)(modifiers | flag),
                UpKey = (IntPtr)modifiers, DownData = position, UpData = position };
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool PostMessage(
            IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(
            IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
    }
}
