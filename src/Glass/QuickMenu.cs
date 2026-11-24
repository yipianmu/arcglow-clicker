using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GlassClicker
{
    internal sealed class QuickMenu : ContextMenu
    {
        private static readonly ControlTemplate MenuTemplate = (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate TargetType='ContextMenu' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Border Background='White' BorderBrush='#DDE4ED' BorderThickness='1' CornerRadius='14' Padding='5'>
                  <DockPanel>
                    <ContentPresenter DockPanel.Dock='Top' Content='{TemplateBinding Tag}'/>
                    <ItemsPresenter/>
                  </DockPanel>
                </Border>
              </ControlTemplate>");

        private static readonly ControlTemplate MenuItemTemplate = (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate TargetType='MenuItem' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Border Name='Face' Background='Transparent' CornerRadius='8' Padding='8,0,10,0'>
                  <Grid Height='33'>
                    <Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
                    <ContentPresenter ContentSource='Icon' HorizontalAlignment='Left' VerticalAlignment='Center'/>
                    <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                  </Grid>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Face' Property='Background' Value='#EDF3FA'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Face' Property='Opacity' Value='0.43'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>");

        private static readonly ControlTemplate SeparatorTemplate = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate TargetType='Separator' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Border Height='1' Background='#E5E9EF'/></ControlTemplate>");

        private MousePressWatch watch;

        public QuickMenu()
        {
            FontFamily = Glass.Font;
            MinWidth = 166;
            Template = MenuTemplate;
            StaysOpen = false;
            Placement = PlacementMode.MousePoint;
            Opened += (sender, args) =>
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null) Windows.SetNoActivate(source.Handle, true);
                watch = new MousePressWatch(Dispatcher, (point, button) => DismissOutside(point));
            };
            Closed += (sender, args) =>
            {
                if (watch != null) watch.Dispose();
                watch = null;
            };
            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key != Key.Escape) return;
                IsOpen = false;
                args.Handled = true;
            };
        }

        internal void DismissOutside(ScreenPoint point)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            Windows.Rectangle bounds;
            if (IsOpen && source != null && Windows.GetWindowRect(source.Handle, out bounds) && !Windows.Contains(bounds, point))
                IsOpen = false;
        }

        public MenuItem Add(string label, string glyph, Action action, bool destructive = false)
        {
            var ink = destructive ? Glass.DeleteInk : Glass.Ink;
            var item = new MenuItem
            {
                Header = label, FontFamily = Glass.Font, FontSize = 12, Foreground = ink,
                Template = MenuItemTemplate, Cursor = Cursors.Hand
            };
            item.Icon = new Path
            {
                Data = Geometry.Parse(glyph), Stroke = destructive ? ink : Glass.Muted, StrokeThickness = 1.35,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Width = 13, Height = 13, Stretch = Stretch.Uniform, IsHitTestVisible = false
            };
            item.Click += (sender, args) => { IsOpen = false; action(); };
            Items.Add(item);
            return item;
        }

        public void Separate()
        {
            Items.Add(new Separator { Margin = new Thickness(7, 3, 7, 3), Template = SeparatorTemplate });
        }
    }

    internal sealed class MousePressWatch : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly Action<ScreenPoint, int> received;
        private readonly HookProc callback;
        private readonly bool watchRelease;
        private IntPtr hook;
        public ScreenPoint? DragReleasePoint { get; private set; }
        public bool Available { get { return hook != IntPtr.Zero; } }

        public MousePressWatch(Dispatcher dispatcher, Action<ScreenPoint, int> received, bool watchRelease = false)
        {
            this.dispatcher = dispatcher;
            this.received = received;
            this.watchRelease = watchRelease;
            callback = HandleMouse;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        }

        private IntPtr HandleMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0) ObserveMouse(message.ToInt32(), data);
            return CallNextHookEx(hook, code, message, data);
        }

        private void ObserveMouse(int button, IntPtr data)
        {
            if (DragReleasePoint.HasValue || !(watchRelease ? button == 0x0202 :
                button == 0x0201 || button == 0x0204 || button == 0x0207 || button == 0x020b)) return;
            var point = new ScreenPoint(Marshal.ReadInt32(data), Marshal.ReadInt32(data, sizeof(int)));
            if (watchRelease) DragReleasePoint = point;
            dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (Available) received(point, button); }));
        }

        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc procedure, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    }
}
