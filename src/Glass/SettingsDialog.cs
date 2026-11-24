using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace GlassClicker
{
    internal sealed class SettingsDialog : GlassWindow
    {
        private readonly TextBlock notice;
        private readonly Button emergency;

        public SettingsDialog(Workbench host, GlassWindow owner, ClickTask task)
            : base("弧光 · 设置", 372, task == null ? 388 : 512, true)
        {
            if (owner != null) Owner = owner;
            var card = Glass.Card(new Thickness(15));
            var body = new StackPanel();
            card.Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Content = card;

            var heading = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            heading.Background = Brushes.Transparent;
            Glass.On(heading, Glass.Text("弧光设置", 20, Glass.Ink, true), 0, 0);
            var close = Glass.Button("×"); close.Width = 31; close.Padding = new Thickness(0);
            AutomationProperties.SetName(close, "关闭设置");
            close.Click += (sender, args) => Close();
            Glass.On(heading, close, 0, 1);
            body.Children.Add(heading);
            DragBy(heading);
            PreviewKeyDown += (sender, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };

            Section(body, "配置");
            var files = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), new GridLength(7), new GridLength(1, GridUnitType.Star));
            var save = Glass.Button("保存当前配置");
            save.Click += (sender, args) => host.SaveConfiguration(this);
            var open = Glass.Button("打开与管理");
            open.Click += (sender, args) => host.OpenConfiguration(this);
            Glass.On(files, save, 0, 0); Glass.On(files, open, 0, 2);
            body.Children.Add(files);

            Section(body, "外观与调试");
            var automaticPause = Glass.Button(host.PauseOthersWhileEditing ? "已开启" : "已关闭", Glass.White,
                host.PauseOthersWhileEditing ? Glass.Blue : Glass.Muted);
            AutomationProperties.SetName(automaticPause, "切换调试时自动暂停");
            automaticPause.Click += (sender, args) =>
            {
                var active = !host.PauseOthersWhileEditing;
                var error = host.ChangePauseOthers(active);
                automaticPause.Content = active ? "已开启" : "已关闭";
                automaticPause.Foreground = active ? Glass.Blue : Glass.Muted;
                Notice(error ?? (active ? "退出调试后自动继续" : "已关闭调试时自动暂停"), error != null);
            };
            Glass.Hint(automaticPause, "调试一枚时暂缓其他任务。收起后自动继续。");
            body.Children.Add(Row("调试时暂停其他连点", automaticPause));
            body.Children.Add(OutlineControl(host));

            if (task != null)
            {
                Section(body, "此枚连点");
                var values = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(1, GridUnitType.Star));
                TextBox variation, seconds;
                Glass.On(values, Field("间隔浮动", task.IntervalVariation, "ms", out variation), 0, 0);
                Glass.On(values, Field("自动停止时限", task.RunSeconds, "秒", out seconds), 0, 2);
                body.Children.Add(values);
                variation.IsEnabled = !task.ContinuousHold;
                ToolTipService.SetShowOnDisabled(variation, true);
                Glass.Hint(variation, task.ContinuousHold ? "持续按住时使用自动停止时限。" : "每轮在发送间隔之外随机等待 0 到此数值毫秒。0 使用固定间隔。");
                Glass.Hint(seconds, "启动延时结束后开始计时。0 表示不限时。");
                var apply = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
                apply.Margin = new Thickness(0, 5, 0, 0);
                Glass.On(apply, Glass.Text("0 为固定间隔和不限时", 10, Glass.Muted), 0, 0);
                var edit = Glass.Button("应用"); edit.Height = 27;
                edit.Click += (sender, args) =>
                {
                    int extra, limit;
                    if (!ReadNumber(variation, LayoutFile.MaxVariation, out extra))
                    { Notice("间隔浮动请输入 0–60000 ms", true); return; }
                    if (!ReadNumber(seconds, LayoutFile.MaxRunSeconds, out limit))
                    { Notice("自动停止时限请输入 0–86400 秒", true); return; }
                    task.IntervalVariation = extra;
                    task.RunSeconds = limit;
                    host.ScheduleLayoutSave();
                    Notice(task.Worker.State.HasSession ? "✓ 已保存，下次启动生效" : "✓ 已应用到此枚");
                };
                Glass.On(apply, edit, 0, 1);
                body.Children.Add(apply);
            }

            Section(body, "快捷键与教程");
            emergency = Glass.Button(host.EmergencyKeyText, Glass.White);
            AutomationProperties.SetName(emergency, "修改紧急停止快捷键");
            emergency.Click += (sender, args) =>
            {
                OverlayInput.End(this, true);
                host.SuspendShortcut();
                try
                {
                    var record = new RecordPanel(this, true, host.EmergencyKeyText);
                    record.OpenPassively(value =>
                    {
                        try
                        {
                            if (value == null || !IsVisible) return;
                            var error = host.ChangeEmergencyKey(value);
                            emergency.Content = host.EmergencyKeyText;
                            emergency.Foreground = host.EmergencyKeyAvailable ? Glass.Blue : Glass.Gold;
                            Notice(error ?? "✓ 已设置急停键", error != null);
                        }
                        finally { host.ResumeShortcut(); }
                    });
                }
                catch { host.ResumeShortcut(); throw; }
            };
            emergency.Foreground = host.EmergencyKeyAvailable ? Glass.Blue : Glass.Gold;
            Glass.Hint(emergency, "一键停止所有正在运行或暂停的任务");
            body.Children.Add(Row("紧急停止全部", emergency));
            var learn = Glass.Button("查看", Glass.White);
            AutomationProperties.SetName(learn, "查看使用教程");
            learn.Click += (sender, args) => host.ShowGuide(this, false);
            body.Children.Add(Row("使用教程", learn));
            notice = Glass.Text("", 11, Glass.Muted);
            notice.TextTrimming = TextTrimming.CharacterEllipsis;
            notice.Height = 20;
            body.Children.Add(notice);
        }

        private static readonly ControlTemplate StrengthTrack = (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate TargetType='Slider' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                <Grid Height='23' Background='Transparent'>
                  <Border Background='#DCE4EF' Height='5' CornerRadius='3' Margin='9,0'/>
                  <Track x:Name='PART_Track' VerticalAlignment='Center' Height='19'
                    Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
                    Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                    <Track.DecreaseRepeatButton>
                      <RepeatButton Focusable='False' Command='Slider.DecreaseLarge'>
                        <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>
                          <Border Background='#527FC5' Height='5' CornerRadius='3' Margin='9,0,0,0'/>
                        </ControlTemplate></RepeatButton.Template>
                      </RepeatButton>
                    </Track.DecreaseRepeatButton>
                    <Track.IncreaseRepeatButton>
                      <RepeatButton Focusable='False' Command='Slider.IncreaseLarge'>
                        <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                      </RepeatButton>
                    </Track.IncreaseRepeatButton>
                    <Track.Thumb>
                      <Thumb Width='18' Height='18' Cursor='Hand'>
                        <Thumb.Template><ControlTemplate TargetType='Thumb'>
                          <Border Background='White' BorderBrush='#527FC5' BorderThickness='2' CornerRadius='9'/>
                        </ControlTemplate></Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </Grid>
              </ControlTemplate>");

        private static FrameworkElement OutlineControl(Workbench host)
        {
            var tile = Glass.Tile("#F3F4F6", 11, new Thickness(9, 6, 9, 4));
            var content = new StackPanel();
            var heading = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            var percent = Glass.Text(StrengthLabel(host.OutlineStrength), 11, Glass.Muted);
            Glass.On(heading, Glass.Text("窗口弧光强度", 12, Glass.Ink, true), 0, 0);
            Glass.On(heading, percent, 0, 1);
            content.Children.Add(heading);
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = host.OutlineStrength, SmallChange = 1, LargeChange = 10,
                IsSnapToTickEnabled = true, TickFrequency = 1, Height = 23, Template = StrengthTrack, Cursor = Cursors.Hand };
            AutomationProperties.SetName(slider, "窗口弧光强度");
            AutomationProperties.SetHelpText(slider, "0 关闭，100 最亮");
            slider.ValueChanged += (sender, args) =>
            {
                var value = (int)Math.Round(args.NewValue);
                percent.Text = StrengthLabel(value);
                host.ChangeOutlineStrength(value);
            };
            Action<ScreenPoint> moveThumb = point =>
            {
                var x = slider.PointFromScreen(new Point(point.X, point.Y)).X;
                var value = Math.Round((x - 9) * 100 / Math.Max(1, slider.ActualWidth - 18));
                slider.Value = Math.Min(100, Math.Max(0, value));
            };
            new PointerDrag(slider, (args, point) => { moveThumb(point); return true; }, moveThumb,
                (point, moved) => moveThumb(point));
            content.Children.Add(slider);
            tile.Child = content;
            return tile;
        }

        private static string StrengthLabel(int value)
        {
            return value == 0 ? "0% · 关闭" : value.ToString(CultureInfo.InvariantCulture) + "%";
        }

        public void Notice(string message, bool error = false)
        {
            notice.Text = message;
            notice.Foreground = error ? Glass.DeleteInk : Glass.Green;
            Glass.Hint(notice, message);
        }

        private static void Section(Panel body, string title)
        {
            var label = Glass.Text(title, 12, Glass.Muted, true);
            label.Margin = new Thickness(0, 11, 0, 6);
            body.Children.Add(label);
        }

        private static Border Row(string title, Button button)
        {
            button.Height = 27;
            var tile = Glass.Tile("#F3F4F6", 11, new Thickness(9, 4, 5, 4));
            tile.Margin = new Thickness(0, 0, 0, 5);
            var grid = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Glass.On(grid, Glass.Text(title, 12, Glass.Ink, true), 0, 0);
            Glass.On(grid, button, 0, 1);
            tile.Child = grid;
            return tile;
        }

        private FrameworkElement Field(string name, int value, string unit, out TextBox field)
        {
            var stack = new StackPanel();
            stack.Children.Add(Glass.Text(name, 11, Glass.Muted));
            var tile = Glass.Tile("#FFFFFF", 10, new Thickness(8, 0, 7, 0));
            tile.BorderBrush = Glass.MutedLine;
            tile.BorderThickness = new Thickness(1);
            tile.Height = 32;
            tile.Margin = new Thickness(0, 4, 0, 0);
            field = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), FontSize = 12, FontFamily = Glass.Font,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Glass.Ink,
                VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 5 };
            AutomationProperties.SetName(field, name);
            var input = field;
            input.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (!FocusProtected) return;
                args.Handled = true;
                OverlayInput.EditNumber(this, tile, input, explicitEnter => true,
                    editing => tile.BorderBrush = editing ? Glass.Blue : Glass.MutedLine);
            };
            var grid = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Glass.On(grid, field, 0, 0);
            Glass.On(grid, Glass.Text(unit, 10, Glass.Muted), 0, 1);
            tile.Child = grid;
            stack.Children.Add(tile);
            return stack;
        }

        private static bool ReadNumber(TextBox field, int maximum, out int value)
        {
            if (Int32.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= maximum) return true;
            field.Focus();
            field.SelectAll();
            return false;
        }
    }
}
