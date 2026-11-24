using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace GlassClicker
{
    internal sealed class Palette
    {
        public readonly Color Main, Accent, Rim;
        public readonly Brush Marker, Icon;
        private Palette(string main, string accent, string marker, string highlight)
        {
            Main = Glass.Color(main);
            Accent = Glass.Color(accent);
            Rim = Glass.Mix(Glass.Color(marker), Main, .18);
            Marker = Glass.Fill(marker);
            Icon = Glass.IconBrush(marker, highlight);
        }
        private static readonly Palette[] Colors = {
            new Palette("#A1CADA", "#759AB8", "#407BE0", "#41B6DA"), new Palette("#D3A8B4", "#AE8399", "#B74F9D", "#EE7279"),
            new Palette("#99C8BB", "#729F9D", "#258E7F", "#63BF8C"), new Palette("#B1A8CE", "#8985AE", "#6956CC", "#A677E3"),
            new Palette("#DBC49E", "#B29A83", "#B86C26", "#E9AA42"), new Palette("#94BDC9", "#6C929D", "#287DAB", "#4DB7B8"),
            new Palette("#C3A4B1", "#A183A4", "#A755A3", "#D97CB5"), new Palette("#BBC9A1", "#91A282", "#688629", "#AAB943"),
            new Palette("#D1AA9D", "#AE8781", "#BC5A43", "#ED9363"), new Palette("#A3ADC9", "#7C86AB", "#4D64D0", "#779CEF"),
            new Palette("#A3BDA4", "#819E8C", "#418450", "#80B867"), new Palette("#C6AED0", "#A185AD", "#865AA7", "#BD82DC")
        };
        public static Palette Get(int index) { return Colors[index % Colors.Length]; }
    }

    internal struct RimBand
    {
        public readonly double From, To;
        public readonly Color Tone;

        private RimBand(double from, double to, Color tone) { From = from; To = to; Tone = tone; }

        public static IEnumerable<RimBand> Along(Color[] colors)
        {
            if (colors.Length == 1) yield return new RimBand(0, 1, colors[0]);
            if (colors.Length < 2) yield break;
            // The perimeter runs clockwise from the top. The first task stays on the left.
            if (colors.Length == 2) colors = new[] { colors[1], colors[0] };
            var share = 1.0 / colors.Length;
            var feather = Math.Min(.048, share * .22);
            const int steps = 5;
            for (var i = 0; i < colors.Length; i++)
            {
                var start = i * share;
                var end = (i + 1) * share;
                var previous = colors[(i + colors.Length - 1) % colors.Length];
                var next = colors[(i + 1) % colors.Length];
                var step = feather / steps;
                for (var s = 0; s < steps; s++)
                    yield return new RimBand(start + s * step, start + (s + 1) * step,
                        Glass.Mix(previous, colors[i], .5 + .5 * (s + .5) / steps));
                yield return new RimBand(start + feather, end - feather, colors[i]);
                for (var s = 0; s < steps; s++)
                    yield return new RimBand(end - feather + s * step, end - feather + (s + 1) * step,
                        Glass.Mix(colors[i], next, .5 * (s + .5) / steps));
            }
        }
    }

    internal sealed class EdgeLight
    {
        private readonly LinearGradientBrush brush = new LinearGradientBrush { StartPoint = new Point(.1, 0), EndPoint = new Point(.9, 1) };
        private Color[] colors;
        private double strength = 1;
        private bool lit;
        public Brush Brush { get { return brush; } }

        public EdgeLight(params Color[] colors)
        {
            this.colors = colors;
            RefreshColors();
        }

        public void SetColors(Color[] next)
        {
            if (colors.SequenceEqual(next)) return;
            colors = next;
            if (lit) RefreshColors();
        }

        public void SetLit(bool value, int percent = 100)
        {
            var next = percent / 100.0;
            if (value == lit && next == strength) return;
            lit = value;
            strength = next;
            RefreshColors();
        }

        private void RefreshColors()
        {
            brush.GradientStops.Clear();
            var neutral = ((SolidColorBrush)Glass.MutedLine).Color;
            if (!lit || colors.Length == 0 || strength == 0)
            {
                brush.GradientStops.Add(new GradientStop(neutral, 0));
                brush.GradientStops.Add(new GradientStop(neutral, 1));
                return;
            }
            if (colors.Length == 1)
            {
                var color = Glass.Mix(neutral, colors[0], strength);
                brush.GradientStops.Add(new GradientStop(color, 0));
                brush.GradientStops.Add(new GradientStop(color, 1));
                return;
            }
            for (var i = 0; i < colors.Length; i++)
                brush.GradientStops.Add(new GradientStop(Glass.Mix(neutral, colors[i], strength), (double)i / (colors.Length - 1)));
        }
    }

    internal static class Glass
    {
        public static readonly Brush Ink = Fill("#222631"), Muted = Fill("#626976"), Blue = Fill("#326DCD");
        public static readonly Brush MutedLine = Fill("#E2E5EA"), Surface = Fill("#F3F4F6"), White = Brushes.White;
        public static readonly Brush Primary = Fill("#30394A"), Selected = Fill("#E9EDF3");
        public static readonly Brush Green = Fill("#127B57"), Gold = Fill("#9C5D1F");
        public static readonly Brush DeleteInk = Fill("#A04751");
        public static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI, Segoe UI");
        public static Color Color(string code) { return (Color)ColorConverter.ConvertFromString(code); }
        public static SolidColorBrush Fill(string code) { return Fill(Color(code)); }
        public static SolidColorBrush Fill(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
        public static Color Tint(Color color, byte alpha) { return System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B); }
        public static Color Mix(Color first, Color second, double weight)
        {
            return System.Windows.Media.Color.FromRgb((byte)(first.R + (second.R - first.R) * weight),
                (byte)(first.G + (second.G - first.G) * weight), (byte)(first.B + (second.B - first.B) * weight));
        }

        public static Brush IconBrush(string first, string last, string middle = null)
        {
            var brush = new LinearGradientBrush(Color(first), Color(last), new Point(0, 1), new Point(1, 0));
            if (middle != null) brush.GradientStops.Insert(1, new GradientStop(Color(middle), .55));
            brush.Freeze();
            return brush;
        }

        private static ControlTemplate hintTemplate;
        public static void Hint(FrameworkElement owner, string text)
        {
            var current = owner.ToolTip as ToolTip;
            if (String.IsNullOrEmpty(text))
            {
                if (owner.ToolTip != null) owner.ToolTip = null;
                return;
            }
            if (current != null && current.Tag is TextBlock)
            {
                var existing = (TextBlock)current.Tag;
                if (existing.Text != text) existing.Text = text;
                return;
            }
            if (hintTemplate == null)
                hintTemplate = (ControlTemplate)XamlReader.Parse(
                    "<ControlTemplate TargetType='ToolTip' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ContentPresenter/></ControlTemplate>");
            var plate = Tile("#FFFFFF", 10, new Thickness(10, 6, 10, 6));
            var label = Text(text, 11);
            label.TextWrapping = TextWrapping.Wrap; label.MaxWidth = 248;
            plate.Child = label; plate.BorderBrush = MutedLine; plate.BorderThickness = new Thickness(1);
            plate.Margin = new Thickness(2);
            var tip = new ToolTip { Template = hintTemplate, Content = plate, Tag = label, PlacementTarget = owner,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, VerticalOffset = 3,
                IsHitTestVisible = false };
            tip.Opened += (sender, args) =>
            {
                plate.RenderTransformOrigin = new Point(.5, 0);
                var scale = new ScaleTransform(1, 1); plate.RenderTransform = scale;
                var duration = TimeSpan.FromMilliseconds(155);
                var arrive = new DoubleAnimation(.88, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, arrive);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, arrive);
                plate.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.15, 1, duration));
            };
            ToolTipService.SetInitialShowDelay(owner, 430);
            owner.ToolTip = tip;
        }

        public static TextBlock Text(string text, double size, Brush color = null, bool bold = false)
        {
            return new TextBlock { Text = text, FontFamily = Font, FontSize = size, Foreground = color ?? Ink,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        }

        public static Border Tile(string color, double radius, Thickness padding)
        {
            return new Border { Background = Fill(color), CornerRadius = new CornerRadius(radius), Padding = padding };
        }

        public static Border Card(Thickness padding, double radius = 22)
        {
            return new Border { Background = White, CornerRadius = new CornerRadius(radius), Padding = padding,
                BorderBrush = MutedLine, BorderThickness = new Thickness(1), Margin = new Thickness(2) };
        }

        private static ControlTemplate buttonTemplate;
        public static Button Button(string label, Brush background = null, Brush foreground = null)
        {
            if (buttonTemplate == null)
                buttonTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
 <Border Name='ButtonFace' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'>
   <ContentPresenter VerticalAlignment='{TemplateBinding VerticalContentAlignment}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' RecognizesAccessKey='True'/>
 </Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ButtonFace' Property='Opacity' Value='0.86'/></Trigger>
 <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ButtonFace' Property='BorderBrush' Value='#78909F'/><Setter TargetName='ButtonFace' Property='BorderThickness' Value='1'/></Trigger>
 <Trigger Property='IsPressed' Value='True'><Setter TargetName='ButtonFace' Property='Opacity' Value='0.72'/></Trigger>
 <Trigger Property='IsEnabled' Value='False'><Setter TargetName='ButtonFace' Property='Opacity' Value='0.47'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            var button = new Button { Content = label, FontFamily = Font, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand, Foreground = foreground ?? Ink, Background = background ?? Surface,
                BorderBrush = Brushes.Transparent, Padding = new Thickness(10, 0, 10, 0), Height = 32, Template = buttonTemplate,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
            button.Click += (sender, args) => Bounce((ScaleTransform)button.RenderTransform);
            return button;
        }

        public static Button DeleteButton(Brush background)
        {
            var button = Button("", background, DeleteInk);
            button.Width = 28;
            button.Height = 29;
            button.Padding = new Thickness(0);
            Hint(button, "删除这枚");
            System.Windows.Automation.AutomationProperties.SetName(button, "删除这枚");
            button.Content = new Path { Data = Geometry.Parse("M 1,3 L 13,3 M 5,3 L 5,1.5 L 9,1.5 L 9,3 M 3,4 L 4,12.5 L 10,12.5 L 11,4 M 6,5.5 L 6,10 M 8,5.5 L 8,10"),
                Stroke = DeleteInk, StrokeThickness = 1.2, Width = 13, Height = 13, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            return button;
        }

        public const string PointGlyph = "M 8,1 L 8,5 M 8,11 L 8,15 M 1,8 L 5,8 M 11,8 L 15,8 M 8,6 L 10,8 L 8,10 L 6,8 Z";
        public const string GearGlyph = "M6 1 H8 L8.5 3 L10 3.7 L12 3.2 L13 5 L11.6 6.4 V7.6 L13 9 L12 10.8 L10 10.3 L8.5 11 L8 13 H6 L5.5 11 L4 10.3 L2 10.8 L1 9 L2.4 7.6 V6.4 L1 5 L2 3.2 L4 3.7 L5.5 3 Z M9 7 A2 2 0 1 0 5 7 A2 2 0 1 0 9 7 Z";

        public static Path LineIcon(string glyph, Brush line, double width, double height, double stroke)
        {
            return new Path { Data = Geometry.Parse(glyph), Stroke = line, StrokeThickness = stroke, Width = width, Height = height,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
        }

        public static Button IconButton(string glyph, string name)
        {
            var button = Button("");
            button.Height = 28; button.Padding = new Thickness(0);
            button.Content = LineIcon(glyph, Muted, 14, 14, 1.35);
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            Hint(button, name);
            return button;
        }

        public static Grid Crosshair(Brush line, double diameter, double thickness = 0)
        {
            var sight = new Grid { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            sight.Children.Add(new Path { Data = Geometry.Parse(PointGlyph), Stroke = line,
                StrokeThickness = thickness > 0 ? thickness : diameter / 11,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform });
            return sight;
        }

        public static Path Checkmark(Brush line)
        {
            return new Path { Data = Geometry.Parse("M 1,5 L 4,8 L 9,1"), Stroke = line, StrokeThickness = 1.65,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Width = 11, Height = 11, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        }

        private sealed class SpringEase : EasingFunctionBase
        {
            protected override Freezable CreateInstanceCore() { return new SpringEase(); }
            protected override double EaseInCore(double time) { return Spring(time); }
        }

        public static double Spring(double time)
        {
            if (time <= 0) return 0;
            if (time >= 1) return 1;
            const double damping = 7.2, frequency = 10.5;
            var oscillation = frequency * time;
            return 1 - Math.Exp(-damping * time) * (Math.Cos(oscillation) + damping / frequency * Math.Sin(oscillation));
        }

        public static void Bounce(ScaleTransform scale)
        {
            var bounce = new DoubleAnimation(.944, 1, TimeSpan.FromMilliseconds(280))
                { EasingFunction = new SpringEase { EasingMode = EasingMode.EaseIn } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }

        public static void Animate(FrameworkElement target, bool show, bool fromRight = false, Action completed = null)
        {
            var scale = target.RenderTransform as ScaleTransform;
            if (scale == null) { scale = new ScaleTransform(1, 1); target.RenderTransform = scale; }
            target.RenderTransformOrigin = new Point(fromRight ? .8 : .2, .18);
            var length = TimeSpan.FromMilliseconds(show ? 300 : 195);
            if (show)
            {
                target.BeginAnimation(UIElement.OpacityProperty, null);
                target.Opacity = 1;
            }
            else
            {
                var fade = new DoubleAnimation(target.Opacity, 0, length)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                target.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            var size = new DoubleAnimation(show ? .91 : scale.ScaleX, show ? 1 : .90, length);
            size.EasingFunction = show ? (IEasingFunction)new SpringEase { EasingMode = EasingMode.EaseIn } :
                new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            if (completed != null) size.Completed += (sender, args) => completed();
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
        }

        public static void On(Grid grid, UIElement child, int row, int column)
        {
            Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child);
        }

        public static Forms.Screen Screen(ScreenPoint point) { return Forms.Screen.FromPoint(new Drawing.Point(point.X, point.Y)); }
        public static ScreenPoint Clamp(ScreenPoint point, int width, int height)
        {
            var area = Screen(point).WorkingArea;
            return new ScreenPoint(Math.Max(area.Left + 4, Math.Min(point.X, area.Right - width - 4)),
                Math.Max(area.Top + 4, Math.Min(point.Y, area.Bottom - height - 4)));
        }
    }

    internal class GlassWindow : Window
    {
        private readonly bool interactive;
        private bool protectFocus;
        private ForegroundReturn textForeground;
        private IntPtr hwnd;
        public bool FocusProtected { get { return protectFocus; } }
        public bool FullscreenOverlay { get; set; }
        public IntPtr Hwnd
        {
            get
            {
                if (hwnd == IntPtr.Zero) hwnd = new WindowInteropHelper(this).EnsureHandle();
                return hwnd;
            }
        }
        public ScreenPoint Position
        {
            get { Windows.Rectangle r; return Windows.GetWindowRect(Hwnd, out r) ? new ScreenPoint(r.Left, r.Top) : new ScreenPoint(0, 0); }
        }
        public double Scale { get { return Windows.Dpi(Hwnd) / 96.0; } }

        public GlassWindow(string title, double width, double height, bool interactive, bool topmost = true, bool clickThrough = false)
        {
            this.interactive = interactive;
            Title = title;
            Width = width; Height = height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            ShowActivated = interactive;
            Topmost = topmost;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = Glass.Font;
            SourceInitialized += (sender, args) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Windows.AddStyle(Hwnd, Windows.ToolWindow | (clickThrough ? Windows.ClickThrough : 0));
                Windows.SetNoActivate(Hwnd, !this.interactive || protectFocus);
                HwndSource.FromHwnd(Hwnd).AddHook(MouseActivate);
            };
            PreviewTextInput += (sender, args) =>
            {
                var field = Keyboard.FocusedElement as TextBox;
                if (field != null && !field.IsReadOnly && !String.IsNullOrEmpty(args.Text) && !Char.IsControl(args.Text, 0))
                    UiSounds.Key();
            };
            PreviewKeyDown += (sender, args) =>
            {
                var field = Keyboard.FocusedElement as TextBox;
                if (field == null || field.IsReadOnly) return;
                if ((args.Key == Key.Back && (field.SelectionLength > 0 || field.CaretIndex > 0)) ||
                    (args.Key == Key.Delete && field.CaretIndex < field.Text.Length))
                    UiSounds.Key();
            };
            PreviewMouseDown += (sender, args) =>
            {
                if (textForeground != null && !InsideTextBox(args.OriginalSource as DependencyObject))
                    Keyboard.ClearFocus();
            };
            LostKeyboardFocus += (sender, args) =>
            {
                var next = args.NewFocus as TextBox;
                if (args.OldFocus is TextBox && (next == null || GetWindow(next) != this)) EndTextFocus();
            };
            Deactivated += (sender, args) => EndTextFocus();
            Closed += (sender, args) => { hwnd = IntPtr.Zero; EndTextFocus(); };
        }

        private static bool InsideTextBox(DependencyObject element)
        {
            for (var target = element; target != null; target = target is Visual
                ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target))
                if (target is TextBox) return true;
            return false;
        }

        protected bool FocusTextBox(TextBox field)
        {
            if (FullscreenOverlay || Windows.IsFullscreen(Windows.ForegroundTarget())) return false;
            OverlayInput.End(this, true);
            textForeground = new ForegroundReturn();
            ProtectFocus(false);
            if (Activate() && field.Focus())
            {
                field.SelectAll();
                return true;
            }
            EndTextFocus();
            return false;
        }

        private void EndTextFocus()
        {
            if (textForeground == null) return;
            var foreground = textForeground;
            textForeground = null;
            if (IsVisible) ProtectFocus(true);
            foreground.Dispose();
        }

        public void ProtectFocus(bool value)
        {
            if (!interactive || protectFocus == value) return;
            protectFocus = value;
            ShowActivated = !value;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) Windows.SetNoActivate(handle, value);
        }

        private IntPtr MouseActivate(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x0021 || (interactive && !protectFocus)) return IntPtr.Zero;
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE, deliver the mouse click without activating the overlay.
        }

        public void Move(ScreenPoint point, bool clamp = true)
        {
            if (clamp) point = ClampPosition(point);
            Windows.SetWindowPos(Hwnd, IntPtr.Zero, point.X, point.Y, 0, 0, Windows.KeepOrder | 1);
        }

        private ScreenPoint ClampPosition(ScreenPoint point)
        {
            var scale = Scale;
            return Glass.Clamp(point, (int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
        }

        protected void MoveWhileDragging(ScreenPoint point, ref ScreenPoint previous)
        {
            point = ClampPosition(point);
            if (point.X == previous.X && point.Y == previous.Y) return;
            if (Windows.SetWindowPos(Hwnd, IntPtr.Zero, point.X, point.Y, 0, 0, Windows.KeepOrder | 1))
                previous = point;
        }

        public void RaiseWithoutFocus()
        {
            Windows.SetWindowPos(Hwnd, Topmost ? new IntPtr(-1) : IntPtr.Zero, 0, 0, 0, 0, Windows.Place | 0x0003);
        }

        public void DragBy(FrameworkElement header, Action<ScreenPoint> displaced = null)
        {
            var origin = default(ScreenPoint);
            var grab = default(ScreenPoint);
            var previous = default(ScreenPoint);
            header.Cursor = Cursors.SizeAll;
            new PointerDrag(header, (args, point) =>
            {
                for (var source = args.OriginalSource as DependencyObject; source != null && source != header;
                    source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                    if (source is ButtonBase) return false;
                previous = origin = Position;
                grab = point;
                return true;
            }, cursor =>
            {
                var x = cursor.X - grab.X;
                var y = cursor.Y - grab.Y;
                MoveWhileDragging(new ScreenPoint(origin.X + x, origin.Y + y), ref previous);
            }, (point, moved) => { if (moved && displaced != null) displaced(Position); }, threshold: 4);
        }
    }

    internal sealed class GlassOrb : GlassWindow
    {
        internal const int InnerCapacity = 4, SecondCapacity = 5;
        private const double SecondRing = 76, ThirdRing = 102, RingThickness = 16;
        private static readonly Brush CollectorInk = Glass.IconBrush("#3983EA", "#D866A8", "#7961DF");
        private static readonly Brush AddInk = Glass.IconBrush("#2898E0", "#E272A9", "#7359DF");
        private static readonly DoubleAnimation AddPulse = CreateAddPulse();
        private const string CollectorGlyph = "M 1,5 L 9,1 L 17,5 L 9,9 Z M 1,9 L 9,13 L 17,9 M 1,13 L 9,17 L 17,13";
        private const string AddGlyph = "M 8,1 L 8,15 M 1,8 L 15,8";
        private readonly Grid sphere;
        private readonly Path symbol;
        private readonly Path addGlow;
        private readonly Ellipse statusLight, halo;
        private readonly TextBlock target;
        private readonly OrbBadge badge;
        private readonly EdgeLight edge;
        private readonly Brush idleSymbol, activeSymbol;
        private readonly Canvas bands;
        private readonly Border caption;
        private readonly List<OrbBadge> processBadges = new List<OrbBadge>();
        private readonly double diameter;
        private int[] processColors;
        private double orbitRadius;
        private ScreenPoint grab, original;
        public Action Tapped, OutputTapped, Dragged, Grabbed;
        public Func<bool> CanDragTarget;
        public Action<ScreenPoint> TargetDragged, TargetReleased;
        public Action TargetCanceled;
        public Action<int> ProcessTapped;
        public Func<int, ContextMenu> ProcessMenu;
        public ScreenPoint CenterOffset
        {
            get
            {
                var scale = Scale;
                return new ScreenPoint((int)Math.Round(Width * scale / 2), (int)Math.Round(CenterY * scale));
            }
        }
        private double CenterY { get { return orbitRadius > 0 ? Height / 2 : diameter / 2 + 4; } }

        public GlassOrb(Palette palette, bool hub = false) : base(hub ? "连点器 · 汇总" : "连点器 · 悬浮球", 160, 118, false)
        {
            diameter = hub ? 56 : 54;
            sphere = new Grid { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
            System.Windows.Automation.AutomationProperties.SetName(sphere, hub ? "展开连点任务" : "展开连点设置");
            if (!hub)
            {
                edge = new EdgeLight(palette.Rim);
                halo = new Ellipse { Stroke = edge.Brush, StrokeThickness = 3.4, Margin = new Thickness(-1.4),
                    Opacity = .25, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                sphere.Children.Add(halo);
            }
            sphere.Children.Add(new Ellipse { Fill = Glass.White, Stroke = hub ? Glass.MutedLine : edge.Brush, StrokeThickness = 1.7 });
            if (hub)
            {
                addGlow = Glass.LineIcon(AddGlyph, AddInk, 30, 30, 4.5);
                addGlow.Opacity = .38;
                addGlow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 7 };
                addGlow.Visibility = Visibility.Collapsed;
                sphere.Children.Add(addGlow);
                IsVisibleChanged += (sender, args) => UpdateAddGlow();
            }
            idleSymbol = hub ? CollectorInk : palette.Icon;
            activeSymbol = Glass.Fill(palette.Rim);
            symbol = new Path { Data = Geometry.Parse(hub ? CollectorGlyph : Glass.PointGlyph),
                StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Stroke = idleSymbol, Stretch = Stretch.Uniform, Width = 21, Height = 21, IsHitTestVisible = false };
            sphere.Children.Add(symbol);
            if (!hub)
            {
                statusLight = new Ellipse { Width = 7, Height = 7,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 9, 9),
                    Visibility = Visibility.Collapsed, IsHitTestVisible = false, Fill = Glass.Green };
                sphere.Children.Add(statusLight);
            }
            caption = Glass.Tile("#F9FFFFFF", 10, new Thickness(8, 2, 8, 3));
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.VerticalAlignment = VerticalAlignment.Top;
            caption.MaxWidth = 134;
            caption.Cursor = Cursors.Hand;
            target = Glass.Text("", 10, Glass.Muted);
            target.TextTrimming = TextTrimming.CharacterEllipsis;
            target.TextAlignment = TextAlignment.Center;
            caption.Child = target;
            caption.MouseLeftButtonUp += (sender, args) => { if (OutputTapped != null) OutputTapped(); };
            if (hub)
            {
                var collector = new Grid();
                bands = new Canvas { RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
                collector.Children.Add(bands);
                collector.Children.Add(sphere);
                caption.Margin = new Thickness(0, diameter + 9, 0, 0);
                collector.Children.Add(caption);
                Content = collector;
            }
            else
            {
                var orbArea = new Grid();
                orbArea.Children.Add(sphere);
                var center = new Point(Width / 2, CenterY);
                badge = new OrbBadge(center, 40, Math.PI / 2, .90, RingThickness, palette.Marker);
                badge.Clicked = () => { if (OutputTapped != null) OutputTapped(); };
                orbArea.Children.Add(badge);
                caption.Margin = new Thickness(0, CenterY + 55, 0, 0);
                orbArea.Children.Add(caption);
                Content = orbArea;
            }
            var pickTarget = false;
            var previous = default(ScreenPoint);
            new PointerDrag(sphere, (args, point) => {
                if (Grabbed != null) Grabbed();
                grab = point; previous = original = Position;
                pickTarget = CanDragTarget != null && CanDragTarget();
                return true;
            }, cursor => {
                if (pickTarget) TargetDragged(cursor);
                else MoveWhileDragging(new ScreenPoint(original.X + cursor.X - grab.X, original.Y + cursor.Y - grab.Y), ref previous);
            }, (point, dragged) => {
                Glass.Bounce((ScaleTransform)sphere.RenderTransform);
                if (dragged && pickTarget) TargetReleased(point);
                else if (dragged) { if (Dragged != null) Dragged(); }
                else if (Tapped != null) { UiSounds.Click(); Tapped(); }
            }, () => { if (pickTarget && TargetCanceled != null) TargetCanceled(); });
        }

        public void SetProcesses(int[] colors)
        {
            if (bands == null || processColors != null && processColors.SequenceEqual(colors)) return;
            processColors = (int[])colors.Clone();
            var adding = colors.Length == 0;
            symbol.Data = Geometry.Parse(adding ? AddGlyph : CollectorGlyph);
            symbol.Stroke = adding ? AddInk : CollectorInk;
            symbol.Width = symbol.Height = adding ? 24 : 21;
            UpdateAddGlow();
            var description = colors.Length == 0 ? "添加一枚连点" : "展开输出进程";
            System.Windows.Automation.AutomationProperties.SetName(sphere, description);
            Glass.Hint(sphere, colors.Length == 0 ? description : null);
            bands.Children.Clear();
            processBadges.Clear();
            var innerCount = Math.Min(colors.Length, InnerCapacity);
            var secondCount = Math.Min(colors.Length - innerCount, SecondCapacity);
            var thirdCount = colors.Length - innerCount - secondCount;
            var innerRadius = innerCount < InnerCapacity ? 44 : 52;
            orbitRadius = thirdCount > 0 ? ThirdRing : secondCount > 0 ? SecondRing : innerCount > 0 ? innerRadius : 0;
            var diameterWithRing = 2 * (orbitRadius + RingThickness / 2 + 4);
            Width = colors.Length == 0 ? 160 : diameterWithRing;
            Height = colors.Length == 0 ? 118 : diameterWithRing;
            sphere.Margin = new Thickness(0, CenterY - diameter / 2, 0, 0);
            caption.Visibility = colors.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            AddRing(colors, 0, innerCount, innerCount, innerRadius, innerCount == InnerCapacity);
            AddRing(colors, innerCount, secondCount, SecondCapacity, SecondRing, false);
            AddRing(colors, innerCount + secondCount, thirdCount,
                LayoutFile.MaxJobs - InnerCapacity - SecondCapacity, ThirdRing, false);
        }

        private void AddRing(int[] colors, int first, int count, int slots, double radius, bool stagger)
        {
            if (count == 0) return;
            var step = 2 * Math.PI / slots;
            var phase = stagger ? step / 2 : 0;
            var halfArc = Math.Min(1.37, step / 2 - (RingThickness / 2 + 2.5) / radius);
            var center = new Point(Width / 2, CenterY);
            for (var i = 0; i < count; i++)
            {
                var index = first + i;
                var angle = -Math.PI / 2 + phase + step * i;
                var item = new OrbBadge(center, radius, angle, halfArc, RingThickness, Palette.Get(colors[index]).Marker)
                {
                    Width = Width, Height = Height
                };
                item.Clicked = () => { if (ProcessTapped != null) ProcessTapped(index); };
                if (ProcessMenu != null) item.ContextMenu = ProcessMenu(index);
                processBadges.Add(item);
                bands.Children.Add(item);
            }
        }

        private static DoubleAnimation CreateAddPulse()
        {
            var pulse = new DoubleAnimation(.25, .46, TimeSpan.FromMilliseconds(1650))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            pulse.Freeze();
            return pulse;
        }

        private void UpdateAddGlow()
        {
            var adding = processColors != null && processColors.Length == 0;
            addGlow.Visibility = adding ? Visibility.Visible : Visibility.Collapsed;
            var animate = adding && IsVisible && SystemParameters.ClientAreaAnimation;
            addGlow.BeginAnimation(OpacityProperty, animate ? AddPulse : null);
        }

        public void Transition(double size, double opacity, double reveal = 1)
        {
            var scale = (ScaleTransform)sphere.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = size;
            Opacity = opacity;
            if (bands != null)
            {
                var spread = (ScaleTransform)bands.RenderTransform;
                spread.ScaleX = spread.ScaleY = .65 + .35 * reveal;
                bands.Opacity = reveal;
            }
        }

        public void Display(string action, string count, string window, RunPhase phase, string detail = null)
        {
            if (badge != null)
            {
                badge.Display(action, count, PhaseInk(phase));
                Glass.Hint(badge, detail == null ? action + count : action + " · " + detail);
            }
            if (target.Text != window) { target.Text = window; Glass.Hint(caption, window); }
        }

        public void ShowOutputLight(bool active, int strength)
        {
            if (edge == null) return;
            var glowing = active && strength > 0;
            edge.SetLit(glowing, strength);
            statusLight.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            halo.Visibility = glowing ? Visibility.Visible : Visibility.Collapsed;
            symbol.Stroke = glowing ? activeSymbol : idleSymbol;
        }

        public void DisplayProcess(int index, string action, string count, string window, RunPhase phase, string detail = null,
            bool selected = false, int outputStrength = 0)
        {
            if (index >= processBadges.Count) return;
            var item = processBadges[index];
            item.Display(action, count, PhaseInk(phase));
            item.Select(selected);
            item.SetOutputLight(outputStrength, Palette.Get(processColors[index]).Rim);
            Glass.Hint(item, action + " · " + (detail ?? window));
        }

        private static Brush PhaseInk(RunPhase phase)
        {
            return phase == RunPhase.Active || phase == RunPhase.Complete ? Glass.Green :
                phase == RunPhase.Waiting || phase == RunPhase.Delay ? Glass.Gold :
                phase == RunPhase.Paused ? Glass.Muted : Glass.Ink;
        }
    }

    internal sealed class OrbBadge : FrameworkElement
    {
        private readonly Pen pill, rim, hover, selection;
        private Pen outputRim, outputHalo;
        private int outputStrength;
        private readonly Geometry arc, hitArea;
        private readonly Typeface typeface = new Typeface(Glass.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private readonly Point center;
        private readonly double radius, angle, direction, textBudget;
        private readonly Brush marker;
        private readonly Dictionary<string, FormattedText> glyphCache = new Dictionary<string, FormattedText>();
        private double pixelsPerDip;
        private string action = "", count = "";
        private Brush ink = Glass.Ink;
        private bool selected;
        public Action Clicked;

        public OrbBadge(Point center, double radius, double angle, double halfArc, double thickness, Brush marker = null)
        {
            this.center = center;
            this.radius = radius;
            this.angle = angle;
            this.marker = marker;
            direction = Math.Sin(angle) <= 0 ? 1 : -1;
            textBudget = radius * halfArc * 2 + thickness * .68;
            pill = RoundedPen(Glass.White, thickness);
            rim = RoundedPen(Glass.MutedLine, thickness + 1.2);
            hover = RoundedPen(Glass.Fill("#B8C7DC"), thickness + 1.8);
            selection = marker == null ? rim : RoundedPen(marker, thickness + 1.8);
            var curve = new StreamGeometry();
            using (var figure = curve.Open())
            {
                figure.BeginFigure(PointAt(angle - halfArc), false, false);
                figure.ArcTo(PointAt(angle + halfArc), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            curve.Freeze();
            arc = curve;
            hitArea = arc.GetWidenedPathGeometry(hover);
            hitArea.Freeze();
            Cursor = Cursors.Hand;
            Focusable = true;
            MouseEnter += (sender, args) => InvalidateVisual();
            MouseLeave += (sender, args) => InvalidateVisual();
            MouseLeftButtonDown += (sender, args) => { if (Clicked != null && CaptureMouse()) args.Handled = true; };
            MouseLeftButtonUp += (sender, args) =>
            {
                if (!IsMouseCaptured) return;
                ReleaseMouseCapture();
                if (Clicked != null && hitArea.FillContains(args.GetPosition(this))) { UiSounds.Click(); Clicked(); }
                args.Handled = true;
            };
            KeyDown += (sender, args) =>
            {
                if ((args.Key == Key.Enter || args.Key == Key.Space) && Clicked != null) { UiSounds.Click(); Clicked(); args.Handled = true; }
            };
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters parameters)
        {
            return action.Length != 0 && hitArea.FillContains(parameters.HitPoint) ?
                new PointHitTestResult(this, parameters.HitPoint) : null;
        }

        public void Display(string value, string suffix, Brush color)
        {
            if (action == value && count == suffix && ink == color) return;
            action = value;
            count = suffix;
            if (ink != color)
            {
                ink = color;
                glyphCache.Clear();
            }
            System.Windows.Automation.AutomationProperties.SetName(this, action + (count.Length == 0 ? "" : "，" + count));
            InvalidateVisual();
        }

        private static Pen RoundedPen(Brush brush, double width)
        {
            var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        public void Select(bool value)
        {
            if (selected == value) return;
            selected = value;
            InvalidateVisual();
        }

        public void SetOutputLight(int strength, Color color)
        {
            if (outputStrength == strength) return;
            outputStrength = strength;
            outputRim = outputHalo = null;
            if (strength > 0)
            {
                var neutral = ((SolidColorBrush)rim.Brush).Color;
                outputRim = RoundedPen(Glass.Fill(Glass.Mix(neutral, color, strength / 100.0)), pill.Thickness + 2.6);
                outputHalo = RoundedPen(Glass.Fill(Glass.Tint(color, (byte)(.58 * strength))), pill.Thickness + 5.4);
            }
            InvalidateVisual();
        }

        private Point PointAt(double radians)
        {
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (String.IsNullOrEmpty(action)) return;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (pixelsPerDip != dpi)
            {
                pixelsPerDip = dpi;
                glyphCache.Clear();
            }
            var glyphs = new List<FormattedText>();
            var markerWidth = marker == null ? 0 : 8;
            double textWidth = markerWidth;
            var suffix = StringInfo.GetTextElementEnumerator(count);
            var suffixGlyphs = new List<FormattedText>();
            while (suffix.MoveNext()) suffixGlyphs.Add(Format(suffix.GetTextElement()));
            var suffixWidth = suffixGlyphs.Sum(glyph => glyph.WidthIncludingTrailingWhitespace);
            var remaining = textBudget - suffixWidth;
            var ellipsis = Format("…");
            var letters = StringInfo.GetTextElementEnumerator(action);
            while (letters.MoveNext())
            {
                var letter = letters.GetTextElement();
                var glyph = Format(letter);
                var advance = glyph.WidthIncludingTrailingWhitespace;
                var reserve = letters.ElementIndex + letter.Length == action.Length ? 0 : ellipsis.WidthIncludingTrailingWhitespace;
                if (textWidth + advance + reserve > remaining)
                {
                    if (textWidth + ellipsis.WidthIncludingTrailingWhitespace <= remaining)
                    {
                        glyphs.Add(ellipsis);
                        textWidth += ellipsis.WidthIncludingTrailingWhitespace;
                    }
                    break;
                }
                glyphs.Add(glyph);
                textWidth += advance;
            }
            glyphs.AddRange(suffixGlyphs);
            textWidth += suffixWidth;
            if (outputHalo != null) drawing.DrawGeometry(null, outputHalo, arc);
            drawing.DrawGeometry(null, outputRim ?? (selected ? selection : IsMouseOver ? hover : rim), arc);
            drawing.DrawGeometry(null, pill, arc);
            var offset = -textWidth / 2;
            if (marker != null)
            {
                drawing.DrawEllipse(marker, null, PointAt(angle + direction * (offset + 2.1) / radius), 2.1, 2.1);
                offset += markerWidth;
            }
            foreach (var glyph in glyphs)
            {
                var advance = glyph.WidthIncludingTrailingWhitespace;
                var radians = angle + direction * (offset + advance / 2) / radius;
                var point = PointAt(radians);
                drawing.PushTransform(new RotateTransform(radians * 180 / Math.PI + direction * 90, point.X, point.Y));
                drawing.DrawText(glyph, new Point(point.X - advance / 2, point.Y - glyph.Height / 2));
                drawing.Pop();
                offset += advance;
            }
        }

        private FormattedText Format(string value)
        {
            FormattedText glyph;
            if (glyphCache.TryGetValue(value, out glyph)) return glyph;
            if (glyphCache.Count == 64) glyphCache.Clear();
            glyph = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, 9.5, ink, pixelsPerDip);
            glyphCache.Add(value, glyph);
            return glyph;
        }
    }

    internal sealed class Locator : GlassWindow
    {
        private readonly Border grip;
        private readonly ScaleTransform bloom = new ScaleTransform(1, 1);
        private readonly PointerDrag pointer;
        public bool Dragging { get { return pointer.IsActive; } }
        public Action Grabbed;
        public Action<ScreenPoint> Pointed;
        public Action DragEnded;
        public Action<ScreenPoint> Dropped;
        public ScreenPoint Center
        {
            get
            {
                var origin = Position;
                var half = HalfSize;
                return new ScreenPoint(origin.X + half, origin.Y + half);
            }
        }
        public int HalfSize
        {
            get
            {
                Windows.Rectangle bounds;
                if (IsVisible && Windows.GetWindowRect(Hwnd, out bounds) && bounds.Width > 0) return bounds.Width / 2;
                return (int)Math.Round(Width * Scale) / 2;
            }
        }

        public ScreenPoint At(ScreenPoint center)
        {
            var half = HalfSize;
            return new ScreenPoint(center.X - half, center.Y - half);
        }

        public void Follow(ScreenPoint point)
        {
            var dpi = Windows.Dpi(Hwnd);
            var start = At(point);
            Move(start, false);
            if (Windows.Dpi(Hwnd) == dpi) return;
            var corrected = At(point);
            if (corrected.X != start.X || corrected.Y != start.Y) Move(corrected, false);
        }

        public Locator(Palette palette) : base("连点器 · 拖动点击准星", 42, 42, false)
        {
            var sight = new Grid { IsHitTestVisible = false };
            sight.Children.Add(Glass.Crosshair(palette.Icon, 26, 2.3));
            grip = new Border { Margin = new Thickness(2), Cursor = Cursors.SizeAll,
                Background = Glass.Fill("#01FFFFFF"),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = bloom, Child = sight };
            System.Windows.Automation.AutomationProperties.SetName(grip, "拖动点击准星");
            Content = grip;
            pointer = new PointerDrag(grip, (args, point) => {
                if (Grabbed != null) Grabbed();
                return true;
            }, point => {
                Follow(point);
                if (Pointed != null) Pointed(point);
            }, (cursor, moved) => {
                if (DragEnded != null) DragEnded();
                if (moved) { Hide(); if (Dropped != null) Dropped(cursor); }
            }, () => { if (DragEnded != null) DragEnded(); }, 1);
        }

        public void Reveal(double progress)
        {
            grip.Opacity = Math.Min(1, .25 + progress * 1.4);
            bloom.ScaleX = bloom.ScaleY = .38 + .62 * Glass.Spring(progress);
        }

        public void Confirm() { Glass.Bounce(bloom); }
    }
}
