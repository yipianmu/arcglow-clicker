using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassClicker
{
    internal sealed class PointerDrag
    {
        private readonly FrameworkElement grip;
        private readonly Action<ScreenPoint> move;
        private readonly Action<ScreenPoint, bool> released;
        private readonly Action canceled;
        private readonly int threshold;
        private MousePressWatch watch;
        private ScreenPoint origin, last;
        private TimeSpan lastFrame;
        private bool moved;
        private static int activeCount;
        public static bool InProgress { get { return activeCount != 0; } }
        public bool IsActive { get; private set; }

        public PointerDrag(FrameworkElement grip, Func<MouseButtonEventArgs, ScreenPoint, bool> start,
            Action<ScreenPoint> move, Action<ScreenPoint, bool> released, Action canceled = null, int threshold = 5)
        {
            this.grip = grip; this.move = move; this.released = released; this.canceled = canceled; this.threshold = threshold;
            grip.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                var point = Windows.Cursor();
                if (IsActive || !start(args, point)) return;
                origin = last = point;
                moved = false;
                IsActive = true;
                activeCount++;
                watch = new MousePressWatch(grip.Dispatcher, (cursor, message) => Finish(cursor), true);
                if (!grip.CaptureMouse() && !watch.Available) { Cancel(); return; }
                lastFrame = TimeSpan.MinValue;
                CompositionTarget.Rendering += FollowFrame;
                args.Handled = true;
            };
            grip.PreviewMouseLeftButtonUp += (sender, args) =>
            {
                if (!IsActive) return;
                Finish(watch == null ? Windows.Cursor() : watch.DragReleasePoint ?? Windows.Cursor());
                args.Handled = true;
            };
            grip.LostMouseCapture += (sender, args) => { if (watch == null || !watch.Available) Cancel(); };
            grip.IsVisibleChanged += (sender, args) => { if (!grip.IsVisible) Cancel(); };
            grip.Unloaded += (sender, args) => Cancel();
        }

        private void FollowFrame(object sender, EventArgs args)
        {
            var frame = ((RenderingEventArgs)args).RenderingTime;
            if (!IsActive || lastFrame == frame || (watch != null && watch.DragReleasePoint.HasValue)) return;
            lastFrame = frame;
            Travel(Windows.Cursor());
        }

        private void Travel(ScreenPoint point)
        {
            if (!IsActive || point.X == last.X && point.Y == last.Y) return;
            last = point;
            moved = moved || Math.Abs(point.X - origin.X) + Math.Abs(point.Y - origin.Y) >= threshold;
            if (moved) move(point);
        }

        private void Finish(ScreenPoint point)
        {
            if (!IsActive) return;
            Travel(point);
            Reset();
            released(point, moved);
        }

        private void Cancel()
        {
            if (!IsActive) return;
            Reset();
            if (canceled != null) canceled();
        }

        private void Reset()
        {
            IsActive = false;
            activeCount--;
            CompositionTarget.Rendering -= FollowFrame;
            if (watch != null) watch.Dispose();
            watch = null;
            if (grip.IsMouseCaptured) grip.ReleaseMouseCapture();
        }
    }
}
