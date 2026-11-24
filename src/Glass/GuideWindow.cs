using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GlassClicker
{
    internal sealed class GuideWindow : GlassWindow
    {
        private readonly Workbench host;
        private readonly TextBlock headline, instruction;
        private readonly Border preview;
        private readonly StackPanel progress;
        private readonly Button previous, next, skip;
        private int step;

        public GuideWindow(Workbench host, GlassWindow owner)
            : base("弧光 · 使用教程", 362, 321, true)
        {
            this.host = host;
            if (owner != null) Owner = owner;
            var card = Glass.Card(new Thickness(15));
            var body = new StackPanel();
            card.Child = body;
            Content = card;
            var title = Glass.Text("使用教程", 12, Glass.Muted, true);
            title.Background = Brushes.Transparent;
            body.Children.Add(title);
            DragBy(title);
            preview = Glass.Tile("#F1F5FB", 15, new Thickness(10));
            preview.Margin = new Thickness(0, 9, 0, 0);
            preview.Height = 126;
            body.Children.Add(preview);
            headline = Glass.Text("", 20, Glass.Ink, true);
            headline.Margin = new Thickness(1, 10, 0, 0);
            body.Children.Add(headline);
            instruction = Glass.Text("", 12, Glass.Muted);
            instruction.TextWrapping = TextWrapping.Wrap;
            instruction.Height = 38;
            instruction.Margin = new Thickness(1, 4, 0, 0);
            body.Children.Add(instruction);
            var footer = TaskPanel.Columns(GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto,
                new GridLength(6), GridLength.Auto);
            footer.Margin = new Thickness(0, 5, 0, 0);
            skip = Glass.Button("跳过", Brushes.Transparent, Glass.Muted);
            skip.Click += (sender, args) => Close();
            progress = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            previous = Glass.Button("上一步");
            previous.Click += (sender, args) => ShowStep(step - 1);
            next = Glass.Button("下一步", Glass.Primary, Glass.White);
            next.Click += (sender, args) => { if (step == 2) Close(); else ShowStep(step + 1); };
            next.IsDefault = true;
            Glass.On(footer, skip, 0, 0); Glass.On(footer, progress, 0, 1);
            Glass.On(footer, previous, 0, 2); Glass.On(footer, next, 0, 4);
            body.Children.Add(footer);
            PreviewKeyDown += (sender, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
            Loaded += (sender, args) => ShowStep(0);
        }

        private void ShowStep(int index)
        {
            step = index;
            switch (step)
            {
                case 0:
                    headline.Text = "拖准星选窗口";
                    instruction.Text = "从小球或设置里的准星按钮拖到目标窗口。松手后选中窗口和点击位置。";
                    preview.Child = WindowDemo();
                    break;
                case 1:
                    headline.Text = "开始一枚连点";
                    instruction.Text = "选好按键和发送间隔，点「开始这枚」。输出进程里可以单独暂停或继续。";
                    preview.Child = OutputDemo();
                    break;
                default:
                    headline.Text = "收纳与紧急停止";
                    instruction.Text = "点「−」收纳面板。按 " + host.EmergencyKeyText + " 急停，快捷键可在齿轮里改。";
                    preview.Child = FoldDemo();
                    break;
            }
            AutomationProperties.SetName(progress, "第 " + (step + 1) + " 步，共 3 步");
            progress.Children.Clear();
            for (var i = 0; i < 3; i++) progress.Children.Add(new Border { Width = i == step ? 14 : 5, Height = 5,
                Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(3), Background = i == step ? Glass.Blue : Glass.MutedLine });
            previous.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
            skip.Content = step == 2 ? "关闭" : "跳过";
            next.Content = step == 2 ? "开始使用" : "下一步";
            Glass.Animate((FrameworkElement)preview.Child, true);
        }

        private static FrameworkElement WindowDemo()
        {
            var stage = DemoStage();
            var target = Glass.Tile("#FFFFFF", 13, new Thickness(10));
            target.Width = 128; target.Height = 78;
            target.BorderThickness = new Thickness(2); target.BorderBrush = Glass.Blue;
            var header = Glass.Tile("#E8F0FF", 4, new Thickness(0)); header.Height = 9;
            header.VerticalAlignment = VerticalAlignment.Top;
            target.Child = header;
            Place(stage, target, 115, 10);
            var crosshair = Glass.Crosshair(Glass.Blue, 24, 2.2);
            Place(stage, crosshair, 165, 41);
            var slide = new TranslateTransform(-103, 13);
            crosshair.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-103, 0, TimeSpan.FromMilliseconds(510))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(13, 0, TimeSpan.FromMilliseconds(510)));
            return stage;
        }

        private static FrameworkElement OutputDemo()
        {
            var stage = DemoStage();
            Place(stage, Chip("K", 43, 35), 38, 33);
            Place(stage, Chip("1000 ms", 85, 35), 88, 33);
            var start = Chip("▶", 49, 35);
            start.Background = Glass.Primary;
            ((TextBlock)start.Child).Foreground = Glass.White;
            Place(stage, start, 181, 33);
            return stage;
        }

        private static FrameworkElement FoldDemo()
        {
            var stage = DemoStage();
            Place(stage, Chip("−", 32, 32), 49, 35);
            for (var i = 0; i < 3; i++)
            {
                var dot = new Border { Background = Glass.White, BorderBrush = Palette.Get(i).Marker, BorderThickness = new Thickness(2),
                    Width = 35, Height = 35, CornerRadius = new CornerRadius(18) };
                dot.Child = Glass.Crosshair(Palette.Get(i).Marker, 13);
                Place(stage, dot, 109 + i * 22, 33 + i % 2 * 8);
            }
            return stage;
        }

        private static Canvas DemoStage()
        {
            return new Canvas { Width = 266, Height = 102, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        }

        private static Border Chip(string value, int width, int height)
        {
            var chip = Glass.Tile("#FFFFFF", 10, new Thickness(2));
            chip.Width = width; chip.Height = height;
            var text = Glass.Text(value, 14, Glass.Ink, true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            chip.Child = text;
            return chip;
        }

        private static void Place(Canvas stage, FrameworkElement sample, double left, double top)
        {
            Canvas.SetLeft(sample, left); Canvas.SetTop(sample, top); stage.Children.Add(sample);
        }
    }
}
