using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace GlassClicker
{
    // Four narrow layered surfaces, not a translucent window the size of the game.
    internal sealed class WindowOutline : IDisposable
    {
        private sealed class Segment
        {
            public StreamGeometry Path;
            public Pen[] Glow;
        }

        private readonly IntPtr target;
        private readonly Edge[] edges = new Edge[4];
        private List<Segment> segments;
        private Windows.Rectangle last;
        private IntPtr lastAbove;
        private uint lastDpi;
        private int colorStamp, radiusPreference, lastStrength;
        private bool shown;

        public WindowOutline(IntPtr target) { this.target = target; }

        public void Layout(int[] colors, int strength)
        {
            Windows.Rectangle bounds;
            if (!Windows.Frame(target, out bounds)) { Hide(); return; }
            var dpi = Windows.Dpi(target);
            var rim = Math.Max(9, (int)Math.Round(dpi / 96.0 * 12));
            var width = bounds.Width + 2 * rim;
            var height = bounds.Height + 2 * rim;
            var safe = Math.Min(rim * 2 + 1, Math.Min(width, height) / 2);
            var stamp = colors.Length;
            foreach (var color in colors) stamp = unchecked(stamp * 31 + color);
            var corner = Windows.IsZoomed(target) ? 1 : Windows.CornerPreference(target);
            var rebuild = segments == null || last.Width != bounds.Width || last.Height != bounds.Height ||
                dpi != lastDpi || stamp != colorStamp || corner != radiusPreference || strength != lastStrength;
            if (rebuild)
            {
                segments = MakeSegments(colors, rim, width, height, corner, dpi / 96.0, strength / 100.0);
                colorStamp = stamp;
                radiusPreference = corner;
                lastStrength = strength;
            }

            var above = Windows.GetWindow(target, 3);
            while (above != IntPtr.Zero)
            {
                uint pid;
                Windows.Thread(above, out pid);
                if (pid != Windows.OwnProcess) break;
                above = Windows.GetWindow(above, 3);
            }
            var topmost = (Windows.Style(target) & 8) != 0;
            if (!topmost && above != IntPtr.Zero && (Windows.Style(above) & 8) != 0) above = IntPtr.Zero;
            if (above == IntPtr.Zero && topmost) above = new IntPtr(-1);
            if (rebuild || !shown || above != lastAbove || bounds.Left != last.Left || bounds.Top != last.Top)
            {
                var x = bounds.Left - rim;
                var y = bounds.Top - rim;
                var sideHeight = Math.Max(1, height - safe * 2);
                var parts = new[]
                {
                    new Int32Rect(0, 0, width, safe),
                    new Int32Rect(0, height - safe, width, safe),
                    new Int32Rect(0, safe, safe, sideHeight),
                    new Int32Rect(width - safe, safe, safe, sideHeight)
                };
                if (rebuild)
                {
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var edge = edges[i] ?? (edges[i] = new Edge());
                        var part = parts[i];
                        if (edge.Present(segments, part, new ScreenPoint(x + part.X, y + part.Y))) continue;
                        Hide();
                        segments = null;
                        return;
                    }
                }
                // Positions change independently from size. A resize presents new pixels and size together.
                const uint flags = Windows.Place | 0x0040 | 0x0001;
                var batch = Windows.BeginDeferWindowPos(parts.Length);
                for (var i = 0; i < parts.Length && batch != IntPtr.Zero; i++)
                {
                    var part = parts[i];
                    batch = Windows.DeferWindowPos(batch, edges[i].Hwnd, above, x + part.X, y + part.Y, part.Width, part.Height, flags);
                }
                if (batch == IntPtr.Zero || !Windows.EndDeferWindowPos(batch))
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var part = parts[i];
                        Windows.SetWindowPos(edges[i].Hwnd, above, x + part.X, y + part.Y, part.Width, part.Height, flags);
                    }
            }
            last = bounds; lastAbove = above; lastDpi = dpi; shown = true;
        }

        private void Hide()
        {
            if (!shown) return;
            foreach (var edge in edges) if (edge != null) edge.Hide();
            shown = false;
        }

        public void Dispose()
        {
            foreach (var edge in edges) if (edge != null) edge.Dispose();
        }

        private static List<Segment> MakeSegments(int[] colors, int rim, int width, int height, int corner, double scale, double strength)
        {
            var left = rim + 1.8 * scale;
            var top = left;
            var right = width - left;
            var bottom = height - top;
            var r = Math.Min(Math.Min(width, height) / 4.0, (corner == 1 ? 0 : (corner == 3 ? 5 : 10)) * scale);
            var upper = new Point(width / 2.0, top);
            var points = new List<Point> { upper, new Point(right - r, top) };
            Arc(points, right - r, top + r, r, -90);
            points.Add(new Point(right, bottom - r));
            Arc(points, right - r, bottom - r, r, 0);
            points.Add(new Point(left + r, bottom));
            Arc(points, left + r, bottom - r, r, 90);
            points.Add(new Point(left, top + r));
            Arc(points, left + r, top + r, r, 180);
            points.Add(upper);
            var lengths = new double[points.Count];
            for (var i = 1; i < points.Count; i++) lengths[i] = lengths[i - 1] + (points[i] - points[i - 1]).Length;
            var perimeter = lengths[lengths.Length - 1];
            var result = new List<Segment>();
            var tones = colors.Select(color => Palette.Get(color).Rim).ToArray();
            var widths = new[] { 15.0, 9.0, 3.8 };
            var alphas = new byte[] { 47, 102, 248 };
            foreach (var band in RimBand.Along(tones))
            {
                var from = perimeter * band.From;
                var to = perimeter * band.To;
                var first = At(points, lengths, from);
                var last = At(points, lengths, to);
                var geometry = new StreamGeometry();
                using (var writer = geometry.Open())
                {
                    writer.BeginFigure(first, false, colors.Length == 1);
                    for (var j = 1; j < points.Count; j++) if (lengths[j] > from && lengths[j] < to) writer.LineTo(points[j], true, false);
                    writer.LineTo(last, true, false);
                }
                geometry.Freeze();
                var pens = widths.Select((size, layer) => Stroke(band.Tone, (byte)Math.Round(alphas[layer] * strength),
                    size * scale * (.63 + .37 * strength))).ToArray();
                result.Add(new Segment { Path = geometry, Glow = pens });
            }
            return result;
        }

        private static Pen Stroke(Color tone, byte alpha, double thickness)
        {
            var pen = new Pen(Glass.Fill(Glass.Tint(tone, alpha)), thickness)
                { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        private static void Arc(List<Point> points, double x, double y, double radius, int start)
        {
            if (radius == 0) return;
            for (var i = 1; i <= 8; i++)
            {
                var angle = (start + i * 90.0 / 8) * Math.PI / 180;
                points.Add(new Point(x + Math.Cos(angle) * radius, y + Math.Sin(angle) * radius));
            }
        }

        private static Point At(List<Point> points, double[] lengths, double distance)
        {
            for (var i = 1; i < lengths.Length; i++)
            {
                var span = lengths[i] - lengths[i - 1];
                if (span > 0 && lengths[i] >= distance)
                    return points[i - 1] + (points[i] - points[i - 1]) * ((distance - lengths[i - 1]) / span);
            }
            return points[points.Count - 1];
        }

        private sealed class Edge : Forms.NativeWindow, IDisposable
        {
            public IntPtr Hwnd { get { return Handle; } }
            public Edge()
            {
                CreateHandle(new Forms.CreateParams { Caption = "弧光 · 窗口描边", Style = unchecked((int)0x80000000),
                    ExStyle = 0x80000 | Windows.ToolWindow | Windows.NoActivate | Windows.ClickThrough });
            }

            public bool Present(List<Segment> segments, Int32Rect part, ScreenPoint destination)
            {
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.PushTransform(new TranslateTransform(-part.X, -part.Y));
                    for (var band = 0; band < segments[0].Glow.Length; band++)
                        foreach (var segment in segments) drawing.DrawGeometry(null, segment.Glow[band], segment.Path);
                    drawing.Pop();
                }
                var bitmap = new RenderTargetBitmap(part.Width, part.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var pixels = new int[part.Width * part.Height];
                bitmap.CopyPixels(pixels, part.Width * sizeof(int), 0);
                return PresentLayer(pixels, part.Width, part.Height, destination);
            }

            private bool PresentLayer(int[] pixels, int width, int height, ScreenPoint destination)
            {
                var dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                try
                {
                    var header = new BitmapHeader { Size = (uint)Marshal.SizeOf(typeof(BitmapHeader)), Width = width,
                        Height = -height, Planes = 1, Bits = 32 };
                    IntPtr bits;
                    var bitmap = CreateDIBSection(dc, ref header, 0, out bits, IntPtr.Zero, 0);
                    if (bitmap == IntPtr.Zero) return false;
                    try
                    {
                        var previous = SelectObject(dc, bitmap);
                        if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return false;
                        try
                        {
                            Marshal.Copy(pixels, 0, bits, pixels.Length);
                            var source = new ScreenPoint(0, 0);
                            var size = new ScreenPoint(width, height);
                            var blend = new Blend { Opacity = 255, Alpha = 1 };
                            return UpdateLayeredWindow(Hwnd, IntPtr.Zero, ref destination, ref size, dc, ref source, 0, ref blend, 2);
                        }
                        finally { SelectObject(dc, previous); }
                    }
                    finally { DeleteObject(bitmap); }
                }
                finally { DeleteDC(dc); }
            }

            public void Hide() { ShowWindow(Hwnd, 0); }
            public void Dispose() { if (Hwnd != IntPtr.Zero) DestroyHandle(); }

            [StructLayout(LayoutKind.Sequential)]
            private struct BitmapHeader
            {
                public uint Size;
                public int Width, Height;
                public ushort Planes, Bits;
                public uint Compression, ImageSize;
                public int XResolution, YResolution;
                public uint Colors, ImportantColors;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Blend { public byte Op, Flags, Opacity, Alpha; }

            [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader info, uint usage,
                out IntPtr bits, IntPtr section, uint offset);
            [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
            [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
            [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
            [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
            [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr drawingDC,
                ref ScreenPoint destination, ref ScreenPoint size, IntPtr sourceDC, ref ScreenPoint source, uint colorKey, ref Blend blend, uint flags);
        }
    }
}
