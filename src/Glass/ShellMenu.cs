using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace GlassClicker
{
    internal static class ShellMenu
    {
        private sealed class Snapshot
        {
            public IntPtr Window;
            public Windows.Rectangle Host, Panel;
            public long RetryAt, ValidUntil;
            public bool Reading;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<uint, Snapshot> Panels = new Dictionary<uint, Snapshot>();

        public static bool IsMenuProcess(string image)
        {
            var name = Path.GetFileNameWithoutExtension(image);
            return String.Equals(name, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "SearchHost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Frame(IntPtr window, Windows.Rectangle native, out Windows.Rectangle frame)
        {
            frame = native;
            var display = Forms.Screen.FromRectangle(new Drawing.Rectangle(native.Left, native.Top, native.Width, native.Height)).WorkingArea;
            if (native.Width < display.Width * .94 || native.Height < display.Height * .94) return true;
            uint pid;
            if (Windows.Thread(window, out pid) == 0) return false;
            string image;
            ulong created;
            Windows.Identity(pid, out image, out created);
            if (!IsMenuProcess(image)) return true;

            lock (Gate)
            {
                Snapshot record;
                if (!Panels.TryGetValue(pid, out record) || record.Window != window || !record.Host.Equals(native))
                {
                    if (Panels.Count >= 8) Panels.Clear();
                    record = new Snapshot { Window = window, Host = native };
                    Panels[pid] = record;
                }
                var now = DateTime.UtcNow.Ticks;
                if (!record.Reading && now >= record.RetryAt)
                {
                    record.Reading = true;
                    ThreadPool.QueueUserWorkItem(_ => Read(record));
                }
                frame = record.Panel;
                return now < record.ValidUntil && frame.Width > 0 && frame.Height > 0;
            }
        }

        private static void Read(Snapshot record)
        {
            var frame = default(Windows.Rectangle);
            try
            {
                var host = AutomationElement.FromHandle(record.Window);
                if (host == null) return;
                var outer = new Rect(record.Host.Left, record.Host.Top, record.Host.Width, record.Host.Height);
                var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None,
                    TreeFilter = Automation.RawViewCondition };
                cache.Add(AutomationElement.BoundingRectangleProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                cache.Add(AutomationElement.IsControlElementProperty);
                var best = Rect.Empty;
                var controls = Rect.Empty;
                using (cache.Activate())
                {
                    var elements = host.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                    foreach (AutomationElement element in elements)
                    {
                        if (element.Cached.IsOffscreen) continue;
                        var part = Rect.Intersect(outer, element.Cached.BoundingRectangle);
                        if (!IsPanelPart(outer, part)) continue;
                        if (element.Cached.IsControlElement) controls.Union(part);
                        if (best.IsEmpty || part.Width * part.Height > best.Width * best.Height) best = part;
                    }
                }
                if (IsPanelPart(outer, controls)) best.Union(controls);
                if (!IsPanelPart(outer, best) || best.Width < 120 || best.Height < 90) return;
                frame = new Windows.Rectangle { Left = (int)Math.Floor(best.Left), Top = (int)Math.Floor(best.Top),
                    Right = (int)Math.Ceiling(best.Right), Bottom = (int)Math.Ceiling(best.Bottom) };
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            finally
            {
                lock (Gate)
                {
                    var now = DateTime.UtcNow.Ticks;
                    var present = frame.Width > 0 && Windows.VisibleSurface(record.Window);
                    record.Panel = frame;
                    record.ValidUntil = present ? now + TimeSpan.TicksPerMillisecond * 700 : now;
                    record.RetryAt = now + TimeSpan.TicksPerMillisecond * 180;
                    record.Reading = false;
                }
            }
        }

        private static bool IsPanelPart(Rect outer, Rect part)
        {
            return !part.IsEmpty && part.Width > 0 && part.Height > 0 &&
                (part.Width <= outer.Width * .93 || part.Height <= outer.Height * .93);
        }
    }
}
