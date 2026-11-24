using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace GlassClicker
{
    internal sealed class SettingsDialog : GlassWindow
    {
        private readonly TextBlock notice;
        private readonly TextBlock profileCaption;
        private readonly Button emergency;
        private readonly Workbench host;

        public SettingsDialog(Workbench host, GlassWindow owner, ClickTask task)
            : base("弧光 · 设置", 372, task == null ? 388 : 512, true)
        {
            this.host = host;
            if (owner != null) Owner = owner;
            var card = Glass.Card(new Thickness(15));
            var body = new StackPanel();
            var layout = new DockPanel();
            card.Child = layout;
            Content = card;

            var heading = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            heading.Background = Brushes.Transparent;
            Glass.On(heading, Glass.Text("弧光设置", 20, Glass.Ink, true), 0, 0);
            var close = Glass.Button("×"); close.Width = 31; close.Padding = new Thickness(0);
            AutomationProperties.SetName(close, "关闭设置");
            close.Click += (sender, args) => { UiSounds.Click(); Close(); };
            Glass.On(heading, close, 0, 1);
            DockPanel.SetDock(heading, Dock.Top);
            layout.Children.Add(heading);
            DragBy(heading);
            PreviewKeyDown += (sender, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };

            Section(body, "配置");
            profileCaption = Glass.Text("", 11, Glass.Muted);
            profileCaption.Margin = new Thickness(0, 0, 0, 6);
            profileCaption.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(profileCaption);
            RefreshProfileName();
            var files = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), new GridLength(7), new GridLength(1, GridUnitType.Star));
            var save = Glass.Button("保存当前配置");
            save.Click += (sender, args) => host.SaveConfiguration(this);
            var open = Glass.Button("切换配置");
            open.Click += (sender, args) => { UiSounds.Click(); host.OpenConfiguration(this); };
            Glass.On(files, save, 0, 0); Glass.On(files, open, 0, 2);
            body.Children.Add(files);

            Section(body, "外观与调试");
            var topmost = Glass.Button(host.AlwaysOnTop ? "已开启" : "已关闭", Glass.White,
                host.AlwaysOnTop ? Glass.Blue : Glass.Muted);
            AutomationProperties.SetName(topmost, "切换悬浮界面始终置顶");
            Glass.Hint(topmost, "关闭后，其他窗口可以盖住浮球和面板。点击系统托盘的弧光图标可找回。");
            topmost.Click += (sender, args) =>
            {
                var enabled = !host.AlwaysOnTop;
                var error = host.ChangeAlwaysOnTop(enabled);
                topmost.Content = enabled ? "已开启" : "已关闭";
                topmost.Foreground = enabled ? Glass.Blue : Glass.Muted;
                Notice(error ?? "", error != null);
                UiSounds.Click();
            };
            body.Children.Add(Row("悬浮界面始终置顶", topmost));

            var automaticPause = Glass.Button(host.PauseOthersWhileEditing ? "已开启" : "已关闭", Glass.White,
                host.PauseOthersWhileEditing ? Glass.Blue : Glass.Muted);
            AutomationProperties.SetName(automaticPause, "切换调试时自动暂停");
            automaticPause.Click += (sender, args) =>
            {
                UiSounds.Click();
                var active = !host.PauseOthersWhileEditing;
                var error = host.ChangePauseOthers(active);
                automaticPause.Content = active ? "已开启" : "已关闭";
                automaticPause.Foreground = active ? Glass.Blue : Glass.Muted;
                Notice(error ?? (active ? "退出调试后自动继续" : "已关闭调试时自动暂停"), error != null);
            };
            Glass.Hint(automaticPause, "调试一枚时暂缓其他任务。收起后自动继续。");
            body.Children.Add(Row("调试时暂停其他连点", automaticPause));
            Slider outlineSlider;
            body.Children.Add(PercentControl("弧光强度", "窗口弧光强度", host.OutlineStrength,
                host.ChangeOutlineStrength, StrengthLabel, out outlineSlider));
            AutomationProperties.SetHelpText(outlineSlider, "0 关闭，100 最亮");

            var rememberPosition = Glass.Button(host.RememberPanelPosition ? "已开启" : "已关闭", Glass.White,
                host.RememberPanelPosition ? Glass.Blue : Glass.Muted);
            AutomationProperties.SetName(rememberPosition, "切换记住面板位置");
            rememberPosition.Click += (sender, args) =>
            {
                UiSounds.Click();
                var enabled = !host.RememberPanelPosition;
                var error = host.ChangeRememberPanelPosition(enabled);
                rememberPosition.Content = enabled ? "已开启" : "已关闭";
                rememberPosition.Foreground = enabled ? Glass.Blue : Glass.Muted;
                Notice(error ?? (enabled ? "✓ 已记住拖动后的面板位置" : "✓ 面板将在小球旁边打开"), error != null);
            };
            body.Children.Add(Row("记住拖动后的面板位置", rememberPosition));

            var restore = Glass.Button(host.RestoreLayoutOnStart ? "已开启" : "已关闭", Glass.White,
                host.RestoreLayoutOnStart ? Glass.Blue : Glass.Muted);
            AutomationProperties.SetName(restore, "切换启动时恢复布局");
            Glass.Hint(restore, "下次启动时恢复退出前的任务和悬浮界面布局。");
            restore.Click += (sender, args) =>
            {
                var enabled = !host.RestoreLayoutOnStart;
                var error = host.ChangeRestoreLayoutOnStart(enabled);
                restore.Content = enabled ? "已开启" : "已关闭";
                restore.Foreground = enabled ? Glass.Blue : Glass.Muted;
                Notice(error ?? "", error != null);
                UiSounds.Click();
            };
            body.Children.Add(Row("启动时恢复上次布局", restore));

            var sound = Glass.Button(host.InterfaceSounds ? "已开启" : "已关闭", Glass.White,
                host.InterfaceSounds ? Glass.Blue : Glass.Muted);
            AutomationProperties.SetName(sound, "切换界面音效");
            Glass.Hint(sound, "播放按钮点击和文本输入反馈。");
            Slider volume;
            var volumeTile = PercentControl("音效音量", "界面音效音量", host.InterfaceSoundVolume,
                host.ChangeInterfaceSoundVolume, PercentLabel, out volume);
            volume.IsEnabled = host.InterfaceSounds;
            volume.Opacity = host.InterfaceSounds ? 1 : .55;
            sound.Click += (sender, args) =>
            {
                var enabled = !host.InterfaceSounds;
                var error = host.ChangeInterfaceSounds(enabled);
                sound.Content = enabled ? "已开启" : "已关闭";
                sound.Foreground = enabled ? Glass.Blue : Glass.Muted;
                volume.IsEnabled = enabled;
                volume.Opacity = enabled ? 1 : .55;
                Notice(error ?? "", error != null);
            };
            body.Children.Add(Row("界面音效", sound));
            body.Children.Add(volumeTile);

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
            learn.Click += (sender, args) => { UiSounds.Click(); host.ShowGuide(this, false); };
            body.Children.Add(Row("使用教程", learn));
            Section(body, "开源");
            var export = Glass.Button("导出源码");
            Glass.Hint(export, "保存包含源码、构建脚本和 MIT 许可证的 ZIP 文件。");
            export.Click += (sender, args) => host.ExportSource(this);
            body.Children.Add(export);
            notice = Glass.Text("", 11, Glass.Muted);
            notice.TextTrimming = TextTrimming.CharacterEllipsis;
            notice.Height = 20;
            notice.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(notice, Dock.Bottom);
            layout.Children.Add(notice);
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            scroll.Resources[typeof(ScrollBar)] = SettingsScrollBar;
            layout.Children.Add(scroll);
        }

        private static readonly Style SettingsScrollBar = (Style)XamlReader.Parse(
            @"<Style TargetType='ScrollBar' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                <Style.Resources>
                  <ControlTemplate x:Key='ScrollPageButton' TargetType='RepeatButton'>
                    <Border Background='Transparent'/>
                  </ControlTemplate>
                </Style.Resources>
                <Setter Property='Width' Value='11'/>
                <Setter Property='Template'>
                  <Setter.Value>
                    <ControlTemplate TargetType='ScrollBar'>
                      <Grid Background='Transparent' Margin='1,3,0,3'>
                        <Border Width='4' Background='#F1F3F6' CornerRadius='2'/>
                        <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'
                              Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
                              ViewportSize='{TemplateBinding ViewportSize}'
                              Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                          <Track.DecreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'
                              Template='{StaticResource ScrollPageButton}'/>
                          </Track.DecreaseRepeatButton>
                          <Track.IncreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'
                              Template='{StaticResource ScrollPageButton}'/>
                          </Track.IncreaseRepeatButton>
                          <Track.Thumb>
                            <Thumb Focusable='False'>
                              <Thumb.Template>
                                <ControlTemplate TargetType='Thumb'>
                                  <Grid Background='Transparent'>
                                    <Border x:Name='Grip' Width='5' CornerRadius='3' Background='#C0CBDA'/>
                                  </Grid>
                                  <ControlTemplate.Triggers>
                                    <Trigger Property='IsMouseOver' Value='True'>
                                      <Setter TargetName='Grip' Property='Background' Value='#9DACBF'/>
                                    </Trigger>
                                    <Trigger Property='IsDragging' Value='True'>
                                      <Setter TargetName='Grip' Property='Background' Value='#879DB8'/>
                                    </Trigger>
                                  </ControlTemplate.Triggers>
                                </ControlTemplate>
                              </Thumb.Template>
                            </Thumb>
                          </Track.Thumb>
                        </Track>
                      </Grid>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>");

        private static readonly ControlTemplate StrengthTrack = (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate TargetType='Slider' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                <Grid Height='23' Background='Transparent'>
                  <Border Background='#DAE2ED' Height='3' CornerRadius='2' Margin='9,0'/>
                  <Track x:Name='PART_Track' VerticalAlignment='Center' Height='19'
                    Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
                    Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                    <Track.DecreaseRepeatButton>
                      <RepeatButton Focusable='False' Command='Slider.DecreaseLarge'>
                        <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>
                          <Grid Background='Transparent'>
                            <Border Background='#527FC5' Height='3' CornerRadius='2' Margin='9,0,-9,0'/>
                          </Grid>
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
                          <Grid Background='Transparent'>
                            <Ellipse Width='12' Height='12' Fill='White' Stroke='#527FC5' StrokeThickness='1.35'/>
                          </Grid>
                        </ControlTemplate></Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </Grid>
              </ControlTemplate>");

        private static Border PercentControl(string title, string automationName, int value, Action<int> change,
            Func<int, string> format, out Slider result)
        {
            var tile = Glass.Tile("#F3F4F6", 11, new Thickness(9, 6, 9, 4));
            tile.Margin = new Thickness(0, 0, 0, 5);
            var content = new StackPanel();
            var heading = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            var percent = Glass.Text(format(value), 11, Glass.Muted);
            Glass.On(heading, Glass.Text(title, 12, Glass.Ink, true), 0, 0);
            Glass.On(heading, percent, 0, 1);
            content.Children.Add(heading);
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = value, SmallChange = 1, LargeChange = 10,
                IsSnapToTickEnabled = true, TickFrequency = 1, Height = 23, Template = StrengthTrack, Cursor = Cursors.Hand };
            AutomationProperties.SetName(slider, automationName);
            slider.ValueChanged += (sender, args) =>
            {
                var percentValue = (int)Math.Round(args.NewValue);
                percent.Text = format(percentValue);
                change(percentValue);
            };
            Action<ScreenPoint> moveThumb = point =>
            {
                var x = slider.PointFromScreen(new Point(point.X, point.Y)).X;
                var percentValue = Math.Round((x - 9) * 100 / Math.Max(1, slider.ActualWidth - 18));
                slider.Value = Math.Min(100, Math.Max(0, percentValue));
            };
            new PointerDrag(slider, (args, point) => { moveThumb(point); return true; }, moveThumb,
                (point, moved) => { moveThumb(point); UiSounds.Click(); });
            content.Children.Add(slider);
            tile.Child = content;
            result = slider;
            return tile;
        }

        private static string StrengthLabel(int value)
        {
            return value == 0 ? "0% · 关闭" : PercentLabel(value);
        }

        private static string PercentLabel(int value) { return value.ToString(CultureInfo.InvariantCulture) + "%"; }

        public void RefreshProfileName()
        {
            profileCaption.Text = "当前 · " + (host.CurrentProfileName ?? "未命名配置");
            Glass.Hint(profileCaption, profileCaption.Text);
        }

        public void Notice(string message, bool error = false)
        {
            notice.Text = message;
            notice.Foreground = error ? Glass.DeleteInk : Glass.Green;
            notice.Visibility = String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            Glass.Hint(notice, message);
        }

        private static void Section(Panel body, string title)
        {
            var label = Glass.Text(title, 12, Glass.Muted, true);
            label.Margin = new Thickness(0, 11, 0, 6);
            body.Children.Add(label);
        }

        private static Border Row(string title, FrameworkElement control)
        {
            control.Height = 27;
            var tile = Glass.Tile("#F3F4F6", 11, new Thickness(9, 4, 5, 4));
            tile.Margin = new Thickness(0, 0, 0, 5);
            var grid = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Glass.On(grid, Glass.Text(title, 12, Glass.Ink, true), 0, 0);
            Glass.On(grid, control, 0, 1);
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
            tile.Cursor = Cursors.IBeam;
            field = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), FontSize = 12, FontFamily = Glass.Font,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Glass.Ink,
                VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 5 };
            AutomationProperties.SetName(field, name);
            var input = field;
            input.IsKeyboardFocusedChanged += (sender, args) =>
                tile.BorderBrush = input.IsKeyboardFocused ? Glass.Blue : Glass.MutedLine;
            tile.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (!FocusProtected) return;
                args.Handled = true;
                if (FocusTextBox(input)) return;
                if (OverlayInput.Editing(tile)) { input.SelectAll(); return; }
                if (!OverlayInput.EditNumber(this, tile, input, explicitEnter => true,
                    editing => tile.BorderBrush = editing ? Glass.Blue : Glass.MutedLine))
                    Notice("暂时无法输入，请在窗口模式下重试。", true);
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
