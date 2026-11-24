using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GlassClicker
{
    internal sealed class TaskPanel : GlassWindow
    {
        private readonly Workbench host;
        private readonly ClickTask task;
        private readonly TextBlock targetName, message;
        private readonly Button tracking, shortcut, overlayKey, bind, start, stop, record;
        private readonly FrameworkElement actionCheck;
        private readonly Border targetTile;
        private readonly StackPanel interval;
        private readonly ModeTrack repeat, hold;
        private readonly EdgeLight edge;
        private Action updateIntervalField;
        private bool binding, captureSuspendsShortcut;
        public readonly Border Card;
        public readonly Button LocateButton;
        public ClickTask Task { get { return task; } }

        public TaskPanel(Workbench host, ClickTask task) : base("连点器 · 编辑连点", 359, 376, true)
        {
            this.host = host;
            this.task = task;
            var palette = Palette.Get(task.Color);
            Card = Glass.Card(new Thickness(15, 12, 15, 11));
            edge = new EdgeLight(palette.Rim);
            Card.BorderBrush = edge.Brush;
            Card.BorderThickness = new Thickness(1.6);
            Card.Focusable = true;
            KeyboardNavigation.SetIsTabStop(Card, false);
            var body = new StackPanel();
            Card.Child = body;
            Content = Card;

            var header = Columns(new GridLength(1, GridUnitType.Star), new GridLength(9), new GridLength(35), new GridLength(6), new GridLength(58));
            header.Background = Brushes.Transparent;
            Glass.On(header, Glass.Text("连点设置", 18, Glass.Ink, true), 0, 0);
            DragBy(header, position => host.EditorMoved(this, position));
            var add = Glass.Button("＋");
            add.FontSize = 18; add.Padding = new Thickness(0); Glass.Hint(add, "添加一枚连点");
            AutomationProperties.SetName(add, "添加一枚连点");
            add.Click += (sender, args) => host.AddTask();
            var fold = Glass.Button("− 收起");
            fold.Padding = new Thickness(0);
            Glass.Hint(fold, "收起到浮球，连点继续");
            AutomationProperties.SetName(fold, "收起到浮球");
            fold.Click += (sender, args) => host.DismissEditor();
            Glass.On(header, add, 0, 2); Glass.On(header, fold, 0, 4);
            body.Children.Add(header);

            body.Children.Add(Label("连点键", 9));
            var action = Columns(new GridLength(1, GridUnitType.Star), new GridLength(7), new GridLength(65));
            bind = Glass.Button(task.Action, Glass.White);
            bind.Height = 34; bind.BorderBrush = Glass.MutedLine; bind.BorderThickness = new Thickness(1);
            bind.Click += (sender, args) => BeginBinding();
            Glass.On(action, bind, 0, 0);
            actionCheck = Glass.Checkmark(Glass.Green);
            actionCheck.HorizontalAlignment = HorizontalAlignment.Right;
            actionCheck.VerticalAlignment = VerticalAlignment.Center;
            actionCheck.Margin = new Thickness(0, 0, 9, 0);
            actionCheck.IsHitTestVisible = false;
            actionCheck.Opacity = 0;
            Glass.On(action, actionCheck, 0, 0);
            record = Glass.Button("录制"); record.Height = 34;
            record.Click += (sender, args) => Record(false, value => SetAction(value, "已录制动作"));
            Glass.On(action, record, 0, 2); body.Children.Add(action);

            var times = Columns(new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(1, GridUnitType.Star));
            times.Margin = new Thickness(0, 8, 0, 0);
            interval = Labeled("发送间隔", Number(task.Interval, null, RepeatTiming.MinimumInterval,
                RepeatTiming.MaximumInterval, "ms", isInterval: true));
            RefreshIntervalDisplay();
            Glass.On(times, interval, 0, 0);
            var delay = Labeled("启动延时", Number(task.Delay, value => task.Delay = value, 0, 600000, "ms"));
            Glass.On(times, delay, 0, 2);
            body.Children.Add(times);

            var modes = Columns(new GridLength(1, GridUnitType.Star), new GridLength(8), new GridLength(1, GridUnitType.Star));
            modes.Margin = new Thickness(0, 8, 0, 0);
            hold = new ModeTrack(Number(task.Hold, value => task.Hold = value, 0, 60000, "ms", true),
                "长按", "持续按住", "持续按住直到停止", "改回按指定时长");
            hold.Toggle.Click += (sender, args) =>
            {
                task.ContinuousHold = !task.ContinuousHold;
                RefreshModes(true);
                host.ScheduleLayoutSave();
            };
            repeat = new ModeTrack(Number(task.Count, value => task.Count = value, 1, 9999999, "", true),
                "∞", "∞", "改为无限循环", "改为指定次数");
            repeat.Toggle.Click += (sender, args) =>
            {
                task.Infinite = !task.Infinite;
                RefreshModes(true);
                host.ScheduleLayoutSave();
            };
            Glass.On(modes, Labeled("按住时长", hold), 0, 0);
            Glass.On(modes, Labeled("重复", repeat), 0, 2);
            body.Children.Add(modes);

            targetTile = Glass.Tile("#F3F4F6", 11, new Thickness(10, 5, 6, 5));
            targetTile.BorderThickness = new Thickness(1);
            targetTile.Margin = new Thickness(0, 10, 0, 0);
            var targetGrid = Columns(new GridLength(1, GridUnitType.Star), new GridLength(7), GridLength.Auto, new GridLength(5), GridLength.Auto);
            var targetLines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            targetLines.Children.Add(Glass.Text("当前窗口", 10, Glass.Muted));
            targetName = Glass.Text("", 11.5, Glass.Ink, true); targetName.TextTrimming = TextTrimming.CharacterEllipsis;
            targetLines.Children.Add(targetName);
            Glass.On(targetGrid, targetLines, 0, 0);
            tracking = Glass.Button("自动识别", Glass.White); tracking.Height = 27;
            tracking.Click += (sender, args) => host.ToggleTracking(task);
            LocateButton = Glass.Button("", Glass.White);
            LocateButton.Width = 31; LocateButton.Height = 27; LocateButton.Padding = new Thickness(0);
            LocateButton.Content = Glass.Crosshair(palette.Marker, 15);
            AutomationProperties.SetName(LocateButton, "定位点击位置");
            LocateButton.Click += (sender, args) => host.ToggleLocator(task);
            new PointerDrag(LocateButton, (args, point) => true, point => host.DrawLocator(task, point), (point, dragged) =>
            {
                if (dragged) host.FinishDrawLocator(task, point);
                else LocateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, host.CancelDrawLocator);
            Glass.On(targetGrid, tracking, 0, 2); Glass.On(targetGrid, LocateButton, 0, 4);
            targetTile.Child = targetGrid; body.Children.Add(targetTile);

            message = Glass.Text("", 10, Glass.Muted); message.TextTrimming = TextTrimming.CharacterEllipsis;
            message.Height = 19; message.Margin = new Thickness(1, 1, 0, 0); body.Children.Add(message);
            start = Glass.Button("开始这枚", Glass.Primary, Brushes.White); start.Height = 33;
            start.Click += (sender, args) =>
            {
                if (task.Target == null && !task.Worker.State.HasSession) host.ChooseWindow(task);
                else host.ToggleOne(task);
            };
            stop = Glass.Button("停止");
            stop.Height = 33;
            stop.Margin = new Thickness(6, 0, 0, 0);
            Glass.Hint(stop, "停止这枚连点");
            stop.Click += (sender, args) => host.StopOne(task);
            var commands = Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Glass.On(commands, start, 0, 0);
            Glass.On(commands, stop, 0, 1);
            body.Children.Add(commands);

            var tools = Columns(GridLength.Auto, new GridLength(7), GridLength.Auto,
                new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(4), GridLength.Auto);
            tools.Margin = new Thickness(0, 6, 0, 0);
            var settings = Glass.Button("", Brushes.Transparent); settings.Height = 23; settings.Padding = new Thickness(4, 0, 4, 0);
            var gear = new StackPanel { Orientation = Orientation.Horizontal };
            gear.Children.Add(Glass.LineIcon(Glass.GearGlyph, Glass.Muted, 14, 14, 1.25));
            var settingsText = Glass.Text("设置", 11, Glass.Muted, true);
            settingsText.Margin = new Thickness(5, 0, 0, 0);
            gear.Children.Add(settingsText);
            settings.Content = gear;
            AutomationProperties.SetName(settings, "设置");
            settings.Click += (sender, args) => host.ShowSettings();
            var remove = Glass.Button("删除这枚", Brushes.Transparent, Glass.DeleteInk);
            remove.Height = 23; remove.FontSize = 10.5; remove.Padding = new Thickness(4, 0, 4, 0);
            remove.Click += (sender, args) => host.RemoveTask(task);
            shortcut = Glass.Button(AppSettings.DefaultShortcut); shortcut.Height = 23; shortcut.FontSize = 10.5;
            overlayKey = Glass.Button("隐藏"); overlayKey.Height = 23; overlayKey.FontSize = 10.5;
            AutomationProperties.SetName(overlayKey, "隐藏浮球和面板");
            Glass.Hint(overlayKey, "隐藏浮球和面板。点击系统托盘的弧光图标可恢复。");
            overlayKey.Click += (sender, args) => host.ToggleVisibility();
            shortcut.Click += (sender, args) => Record(true, value => host.ChangeShortcut(value));
            Glass.On(tools, settings, 0, 0); Glass.On(tools, remove, 0, 2);
            Glass.On(tools, shortcut, 0, 4); Glass.On(tools, overlayKey, 0, 6);
            body.Children.Add(tools);

            bind.Content = DisplayAction(task.Action);
            PreviewKeyDown += BindKey;
            PreviewMouseDown += BindMouse;
            bind.LostKeyboardFocus += (sender, args) => { if (binding) EndBinding(null); };
            Closed += (sender, args) => { if (binding) EndBinding(null); };
            foreach (var control in new[] { record, shortcut, hold.Toggle })
                ToolTipService.SetShowOnDisabled(control, true);
            RefreshModes(false);
            Refresh();
        }

        public void Refresh()
        {
            targetName.Text = task.Target == null ? "未选择" : task.Target.Name;
            Glass.Hint(targetName, task.Target == null ? null : task.Target.Name);
            tracking.Content = task.Tracking ? "取消选择" : FullscreenOverlay ? "用全屏窗口" :
                task.Target == null ? "选择窗口" : task.Target.Window == IntPtr.Zero ? "重新选窗" : "更换窗口";
            Glass.Hint(tracking, FullscreenOverlay ? "以当前全屏窗口为目标" : "点这里，再点要控制的窗口");
            targetTile.BorderBrush = task.Tracking ? Palette.Get(task.Color).Marker : Brushes.Transparent;
            shortcut.Content = host.ShortcutText;
            shortcut.Foreground = host.ShortcutAvailable ? Glass.Blue : Glass.Gold;
            var hideLabel = host.OverlayKeyText == null ? "隐藏" : "隐藏 " + host.OverlayKeyText;
            if (!Equals(overlayKey.Content, hideLabel)) overlayKey.Content = hideLabel;
            var state = task.Worker.State;
            var showRim = task.OutputActive || !state.HasSession && task.Target != null && task.Target.Window != IntPtr.Zero;
            edge.SetLit(host.OutlineStrength > 0 && showRim, host.OutlineStrength);
            start.Content = state.PausedForEditing ? "保持暂停" : state.Phase == RunPhase.Paused ? "继续这枚" : state.Running ? "暂停这枚" :
                task.Tracking ? "等待选择窗口" : task.Target == null ? "先选择窗口" :
                task.Target.Window == IntPtr.Zero ? "重新选择窗口" : "开始这枚";
            start.IsEnabled = state.HasSession || !task.Tracking;
            stop.Visibility = state.HasSession ? Visibility.Visible : Visibility.Collapsed;
            Glass.Hint(start, state.PausedForEditing ? "将调试暂停改为手动暂停" :
                state.Phase == RunPhase.Paused ? "从当前进度继续" : state.Running ? "暂停并保留当前次数" : null);
            record.IsEnabled = shortcut.IsEnabled = !FullscreenOverlay;
            Glass.Hint(record, FullscreenOverlay ? "在窗口模式下录制键盘动作" : "录制一串动作");
            Glass.Hint(shortcut, FullscreenOverlay ? "在窗口模式下修改快捷键" : host.ShortcutAvailable ?
                "设置开始或停止全部的快捷键" : host.ShortcutText + " 正被占用，点击可重新录入");
            Glass.Hint(bind, "单击后按键，或再点一下鼠标按键");
            LocatorShown(host.LocatorVisible);
        }

        public void LocatorShown(bool visible)
        {
            LocateButton.Background = visible ? Glass.Selected : Glass.White;
            Glass.Hint(LocateButton, visible ? "拖过去，松手就绑定" : "从这里直接拖出准星，或点击后再拖");
        }

        public void ShowMessage(string text, bool important = false)
        {
            message.Text = text ?? "";
            Glass.Hint(message, text);
            message.Foreground = important ? Glass.Gold : Glass.Muted;
        }

        public void ShortcutRestored()
        {
            if (message.Text == host.ShortcutText + " 正被占用") ShowMessage(host.ShortcutText + " 已可用");
        }

        public void RefreshIntervalDisplay()
        {
            ((TextBlock)interval.Children[0]).Text = host.IntervalAsRate ? "每秒次数" : "发送间隔";
            updateIntervalField();
        }

        private void RefreshModes(bool animation)
        {
            var canHold = ActionSequence.Parse(task.Action).Steps.Length == 1;
            if (!canHold) task.ContinuousHold = false;
            hold.SetMode(task.ContinuousHold, animation);
            hold.Toggle.IsEnabled = canHold;
            if (!canHold) Glass.Hint(hold.Toggle, "持续按住适用于单个动作或组合键");
            repeat.SetMode(task.Infinite, animation);
            repeat.IsEnabled = interval.IsEnabled = !task.ContinuousHold;
            repeat.Opacity = interval.Opacity = task.ContinuousHold ? .46 : 1;
        }

        private static void MoveRepeat(IAnimatable element, DependencyProperty property, double to, bool animate)
        {
            var target = (DependencyObject)element;
            var from = (double)target.GetValue(property);
            element.BeginAnimation(property, null);
            target.SetValue(property, to);
            if (!animate || Math.Abs(from - to) < .001) return;
            element.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(245))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private static string DisplayAction(string value)
        {
            return value.Length > 22 ? value.Substring(0, 21) + "…" : value;
        }

        private void BeginBinding()
        {
            if (binding) return;
            binding = true;
            host.SuspendShortcut();
            captureSuspendsShortcut = true;
            actionCheck.BeginAnimation(OpacityProperty, null);
            bind.Content = "按键或点一下鼠标";
            if (FocusProtected)
            {
                if (!OverlayInput.Begin(this, bind, (key, mods) => key != 0 && !ActionSequence.IsModifier(key) &&
                    (mods & ModifierKeys.Windows) == 0, (key, mods) => EndBinding(key == 0x1B ? (int?)null : key, mods),
                    commit => EndBinding(null))) bind.Content = "点一下鼠标按键";
                return;
            }
            bind.Focus();
            if (!bind.IsKeyboardFocusWithin) EndBinding(null);
        }

        private void BindKey(object sender, KeyEventArgs args)
        {
            if (!binding)
            {
                if (args.Key == Key.Escape && !args.IsRepeat && !(Keyboard.FocusedElement is TextBox))
                {
                    args.Handled = true;
                    host.DismissEditor();
                }
                return;
            }
            args.Handled = true;
            if (args.Key == Key.Escape) { EndBinding(null); return; }
            var key = ActionSequence.ReadKey(args);
            if (key != 0 && !args.IsRepeat && !ActionSequence.IsModifier(key)) EndBinding(key);
        }

        private void BindMouse(object sender, MouseButtonEventArgs args)
        {
            var source = args.OriginalSource as DependencyObject;
            OverlayInput.ConfirmOutside(this, source);
            if (!binding) return;
            if (!OverlayInput.IsWithin(source, bind)) { EndBinding(null); return; }
            int key = args.ChangedButton == MouseButton.Left ? -1 : args.ChangedButton == MouseButton.Right ? -2 :
                args.ChangedButton == MouseButton.Middle ? -3 : 0;
            if (key == 0) return;
            args.Handled = true;
            EndBinding(key);
        }

        private void EndBinding(int? key, ModifierKeys? capturedModifiers = null)
        {
            if (!binding) return;
            binding = false;
            if (OverlayInput.Editing(bind)) OverlayInput.End(this, false);
            if (captureSuspendsShortcut) { captureSuspendsShortcut = false; host.ResumeShortcut(); }
            if (key.HasValue)
            {
                try
                {
                    var action = new ActionSequence(new[] { new ActionSequence.Step(0, ActionSequence.Record(key.Value,
                        capturedModifiers ?? Keyboard.Modifiers)) }).Text;
                    SetAction(action, "已绑定 " + action);
                }
                catch (FormatException error) { ShowMessage(error.Message, true); }
            }
            else ShowMessage("");
            bind.Content = DisplayAction(task.Action);
        }

        private void SetAction(string action, string feedback)
        {
            host.SetAction(task, ActionSequence.Parse(action));
            bind.Content = DisplayAction(task.Action);
            RefreshModes(true);
            actionCheck.BeginAnimation(OpacityProperty, Confirmation(true));
            ShowMessage(feedback);
        }

        private void Record(bool hotkey, Action<string> apply)
        {
            if (FullscreenOverlay) return;
            OverlayInput.End(this, true);
            host.SuspendShortcut();
            try
            {
                var capture = new RecordPanel(this, hotkey, host.ShortcutText);
                capture.OpenPassively(value =>
                {
                    host.ResumeShortcut();
                    if (value != null && IsVisible) apply(value);
                });
            }
            catch { host.ResumeShortcut(); throw; }
        }

        private static TextBlock Label(string label, int margin)
        {
            var text = Glass.Text(label, 11, Glass.Muted, true);
            text.Margin = new Thickness(1, margin, 0, 4);
            return text;
        }

        private FrameworkElement Number(string value, Action<string> changed, decimal minimum, decimal maximum, string unit,
            bool integrated = false, bool isInterval = false)
        {
            var surface = Glass.Tile(integrated ? "#00000000" : "#FFFFFF", 9, new Thickness(4, 0, 4, 0));
            var restingBorder = integrated ? Brushes.Transparent : Glass.MutedLine;
            surface.Height = 34; surface.BorderBrush = restingBorder; surface.BorderThickness = new Thickness(1);
            var field = new TextBox { Text = value, Foreground = Glass.Ink, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), FontSize = 12.5, FontFamily = Glass.Font, VerticalContentAlignment = VerticalAlignment.Center,
                MaxLength = isInterval ? 9 : 7, Padding = new Thickness(4, 0, unit.Length == 0 ? 18 : 24, 0) };
            var units = Glass.Text(unit, 10, Glass.Muted);
            units.HorizontalAlignment = HorizontalAlignment.Right;
            units.Margin = new Thickness(0, 0, 3, 0); units.IsHitTestVisible = false;
            var checkmark = Glass.Checkmark(Glass.Green);
            checkmark.HorizontalAlignment = HorizontalAlignment.Right;
            checkmark.VerticalAlignment = VerticalAlignment.Center;
            checkmark.Margin = units.Margin;
            checkmark.Opacity = 0;
            MousePressWatch outside = null;
            Action stopWatching = () =>
            {
                if (outside == null) return;
                outside.Dispose();
                outside = null;
            };
            var saved = value;
            var range = "";
            Action updateRange = () =>
            {
                range = RepeatTiming.Format(minimum) + "–" + RepeatTiming.Format(maximum) + (unit.Length == 0 ? "" : " " + unit);
                Glass.Hint(surface, "点击输入 " + range);
            };
            updateRange();
            var asRate = false;
            surface.Cursor = Cursors.IBeam;
            Func<bool, bool> confirm = explicitEnter =>
            {
                if (!explicitEnter && field.Text == saved) return true;
                decimal number;
                var styles = isInterval ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
                var dot = field.Text.IndexOf('.');
                if (!Decimal.TryParse(field.Text, styles, CultureInfo.InvariantCulture, out number) ||
                    number < minimum || number > maximum || (dot >= 0 && field.Text.Length - dot - 1 > RepeatTiming.Precision))
                {
                    ShowMessage("请输入 " + range, true);
                    if (explicitEnter) field.SelectAll();
                    else field.Text = saved;
                    return false;
                }
                field.Text = RepeatTiming.Format(number);
                if (field.Text != saved)
                {
                    saved = field.Text;
                    if (isInterval)
                        host.SetInterval(task, asRate ? 1000m / number : number);
                    else changed(saved);
                    host.ScheduleLayoutSave();
                }
                checkmark.BeginAnimation(OpacityProperty, Confirmation(true));
                units.BeginAnimation(OpacityProperty, Confirmation(false));
                ShowMessage("");
                return true;
            };
            field.IsKeyboardFocusedChanged += (sender, args) =>
            {
                surface.BorderBrush = field.IsKeyboardFocused ? Glass.Blue : restingBorder;
                if (!field.IsKeyboardFocused)
                {
                    stopWatching();
                    confirm(false);
                    return;
                }
                checkmark.BeginAnimation(OpacityProperty, null);
                units.BeginAnimation(OpacityProperty, null);
                outside = new MousePressWatch(Dispatcher, (point, button) =>
                {
                    if (!field.IsKeyboardFocused || PresentationSource.FromVisual(surface) == null) return;
                    var local = surface.PointFromScreen(new Point(point.X, point.Y));
                    if (new Rect(0, 0, surface.ActualWidth, surface.ActualHeight).Contains(local)) return;
                    confirm(false);
                    Card.Focus();
                });
            };
            field.PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    args.Handled = true;
                    if (confirm(true)) Card.Focus();
                }
                else if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    field.Text = saved;
                    ShowMessage("");
                    Card.Focus();
                }
            };
            Deactivated += (sender, args) =>
            {
                if (field.IsKeyboardFocusWithin) confirm(false);
            };
            Closed += (sender, args) => stopWatching();
            surface.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (!FocusProtected || !field.IsEnabled) return;
                args.Handled = true;
                if (FocusTextBox(field)) return;
                if (OverlayInput.Editing(surface)) { field.SelectAll(); return; }
                if (!OverlayInput.EditNumber(this, surface, field, confirm, editing =>
                {
                    surface.BorderBrush = editing ? Glass.Blue : restingBorder;
                    if (!editing) return;
                    checkmark.BeginAnimation(OpacityProperty, null);
                    units.BeginAnimation(OpacityProperty, null);
                }, allowDecimal: isInterval)) ShowMessage("暂时无法输入，请在窗口模式下重试。", true);
            };
            if (isInterval)
            {
                updateIntervalField = () =>
                {
                    if (OverlayInput.Editing(surface)) OverlayInput.End(this, true);
                    if (field.IsKeyboardFocused) { confirm(false); Card.Focus(); }
                    asRate = host.IntervalAsRate;
                    minimum = asRate ? RepeatTiming.MinimumRate : RepeatTiming.MinimumInterval;
                    maximum = asRate ? RepeatTiming.MaximumRate : RepeatTiming.MaximumInterval;
                    units.Text = unit = asRate ? "次/秒" : "ms";
                    field.Padding = new Thickness(4, 0, asRate ? 35 : 24, 0);
                    decimal milliseconds;
                    saved = field.Text = Decimal.TryParse(task.Interval, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out milliseconds) && milliseconds > 0
                        ? RepeatTiming.Format(asRate ? 1000m / milliseconds : milliseconds) : task.Interval;
                    updateRange();
                };
            }
            var contents = new Grid();
            contents.Children.Add(field);
            contents.Children.Add(units);
            contents.Children.Add(checkmark);
            surface.Child = contents;
            return surface;
        }

        private static DoubleAnimationUsingKeyFrames Confirmation(bool show)
        {
            var now = show ? 1 : 0;
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(now, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(now, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620))));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(1 - now, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
            return animation;
        }

        private static StackPanel Labeled(string caption, FrameworkElement control)
        {
            var stack = new StackPanel();
            stack.Children.Add(Label(caption, 0));
            stack.Children.Add(control);
            return stack;
        }

        internal static Grid Columns(params GridLength[] sizes)
        {
            var grid = new Grid();
            foreach (var size in sizes) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = size });
            return grid;
        }

        private sealed class ModeTrack : Border
        {
            private const double SwitchWidth = 46;
            private const string InfinityGlyph = "M8 4 C6.2 1.6 5.1 .75 3.7 .75 C1.9 .75 .75 2.2 .75 4 C.75 5.8 1.9 7.25 3.7 7.25 C5.1 7.25 6.2 6.4 8 4 C9.8 1.6 10.9 .75 12.3 .75 C14.1 .75 15.25 2.2 15.25 4 C15.25 5.8 14.1 7.25 12.3 7.25 C10.9 7.25 9.8 6.4 8 4";
            private readonly Grid track;
            private readonly Border fill;
            private readonly FrameworkElement field;
            private readonly TextBlock label;
            private readonly FrameworkElement mark;
            private readonly string off, on, turnOn, turnOff;
            private bool active;
            public readonly Button Toggle;

            public ModeTrack(FrameworkElement field, string off, string on, string turnOn, string turnOff)
            {
                this.field = field; this.off = off; this.on = on; this.turnOn = turnOn; this.turnOff = turnOff;
                Height = 34; CornerRadius = new CornerRadius(11);
                Background = Glass.White; BorderBrush = Glass.MutedLine; BorderThickness = new Thickness(1);
                track = new Grid();
                track.SizeChanged += (sender, args) =>
                {
                    track.Clip = new RectangleGeometry(new Rect(args.NewSize), 10, 10);
                    SetMode(active, false);
                };
                field.Margin = new Thickness(0, 0, SwitchWidth, 0);
                track.Children.Add(field);
                fill = new Border { Background = Glass.Surface, CornerRadius = new CornerRadius(10),
                    RenderTransformOrigin = new Point(1, .5), RenderTransform = new ScaleTransform(1, 1), IsHitTestVisible = false };
                track.Children.Add(fill);
                if (off == "∞") mark = Glass.LineIcon(InfinityGlyph, Glass.Ink, 18, 10, 1.85);
                else mark = label = Glass.Text(off, 11.5, Glass.Ink, true);
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.VerticalAlignment = VerticalAlignment.Center;
                mark.IsHitTestVisible = false;
                mark.RenderTransform = new TranslateTransform();
                track.Children.Add(mark);
                Toggle = Glass.Button("", Brushes.Transparent);
                Toggle.Height = Double.NaN; Toggle.Padding = new Thickness(0);
                track.Children.Add(Toggle);
                Child = track;
            }

            public void SetMode(bool value, bool animate)
            {
                active = value;
                var width = track.ActualWidth > SwitchWidth ? track.ActualWidth : 152;
                var hint = active ? turnOff : turnOn;
                Toggle.Width = active ? Double.NaN : SwitchWidth;
                Toggle.HorizontalAlignment = active ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
                Glass.Hint(Toggle, hint);
                AutomationProperties.SetName(Toggle, hint);
                field.IsEnabled = !active;
                if (label != null) label.Text = active ? on : off;
                MoveRepeat((ScaleTransform)fill.RenderTransform, ScaleTransform.ScaleXProperty, active ? 1 : SwitchWidth / width, animate);
                MoveRepeat((TranslateTransform)mark.RenderTransform, TranslateTransform.XProperty, active ? 0 : (width - SwitchWidth) / 2, animate);
                MoveRepeat(field, OpacityProperty, active ? 0 : 1, animate);
            }
        }
    }

    internal sealed class RecordPanel : GlassWindow
    {
        private readonly bool hotkey;
        private readonly List<ActionSequence.Step> steps = new List<ActionSequence.Step>();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly TextBlock preview;
        private readonly Button accept;
        private Shortcut pending;
        private Action<string> completed;
        private ForegroundReturn fallbackFocus;
        private bool accepted;
        private F8Capture f8Capture;
        private long last;
        public string Value { get; private set; }

        public RecordPanel(Window owner, bool hotkey, string currentShortcut = AppSettings.DefaultShortcut)
            : base(hotkey ? "设置快捷键" : "录制连点动作", 340, hotkey ? 175 : 195, true)
        {
            Owner = owner;
            if (owner != null) Topmost = owner.Topmost;
            this.hotkey = hotkey;
            var root = Glass.Card(new Thickness(14));
            root.Focusable = true;
            KeyboardNavigation.SetIsTabStop(root, false);
            var grid = new StackPanel(); root.Child = grid; Content = root;
            var title = Glass.Text(hotkey ? "按下快捷键" : "录制键鼠动作", 17, Glass.Ink, true);
            title.Background = Brushes.Transparent;
            grid.Children.Add(title);
            DragBy(title);
            if (!hotkey)
            {
                var note = Glass.Text("按键盘，或在下方点击鼠标。", 11, Glass.Muted);
                note.Margin = new Thickness(0, 5, 0, 0);
                grid.Children.Add(note);
            }
            var surface = Glass.Tile("#F3F4F6", 12, new Thickness(9, 5, 9, 5));
            surface.Height = hotkey ? 56 : 60;
            surface.Margin = new Thickness(0, 10, 0, 0);
            surface.Cursor = hotkey ? Cursors.Arrow : Cursors.Cross;
            preview = Glass.Text(hotkey ? "等待按键" : "可以在这里点击", 13, Glass.Muted, true);
            preview.TextWrapping = TextWrapping.Wrap; preview.TextAlignment = TextAlignment.Center; preview.TextTrimming = TextTrimming.CharacterEllipsis;
            surface.Child = preview; grid.Children.Add(surface);
            if (!hotkey) surface.PreviewMouseDown += (sender, args) => {
                int key = args.ChangedButton == MouseButton.Left ? -1 : args.ChangedButton == MouseButton.Right ? -2 : args.ChangedButton == MouseButton.Middle ? -3 : 0;
                if (key != 0) Add(key); args.Handled = true;
            };
            var footer = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), new GridLength(7), new GridLength(65));
            footer.Margin = new Thickness(0, 8, 0, 0);
            accept = Glass.Button(hotkey ? "保留 " + currentShortcut : "完成录制", Glass.Primary, Brushes.White);
            accept.IsEnabled = hotkey;
            accept.Click += (sender, args) =>
            {
                if (hotkey) Value = currentShortcut;
                Finish(true);
            };
            var cancel = Glass.Button("取消"); cancel.Click += (sender, args) => Finish(false);
            Glass.On(footer, accept, 0, 0); Glass.On(footer, cancel, 0, 2); grid.Children.Add(footer);
            PreviewKeyDown += RecordDown;
            PreviewKeyUp += RecordUp;
            Loaded += (sender, args) =>
            {
                if (FocusProtected) return;
                root.Focus();
                if (hotkey && f8Capture == null)
                    f8Capture = new F8Capture(Hwnd, modifiers => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!IsVisible || Windows.GetForegroundWindow() != Hwnd) return;
                        QueueShortcut(F8Capture.KeyCode, modifiers);
                        FinishShortcut(F8Capture.KeyCode);
                    })));
            };
            ContentRendered += (sender, args) => { if (!FocusProtected && !IsKeyboardFocusWithin) root.Focus(); };
            Closed += (sender, args) =>
            {
                OverlayInput.End(this, false);
                if (f8Capture != null) f8Capture.Dispose();
                if (fallbackFocus != null) fallbackFocus.Dispose();
                if (completed != null) completed(accepted ? Value : null);
            };
            SourceInitialized += (sender, args) => {
                var p = ((GlassWindow)owner).Position;
                Move(new ScreenPoint(p.X + 11, p.Y + 45));
            };
        }

        public void OpenPassively(Action<string> result)
        {
            completed = result;
            var previous = new ForegroundReturn();
            ProtectFocus(true);
            Show();
            if (OverlayInput.Begin(this, (FrameworkElement)Content,
                (key, mods) => key != 0 && !ActionSequence.IsModifier(key) && (mods & ModifierKeys.Windows) == 0 &&
                    !(key == 9 && (mods & ModifierKeys.Alt) != 0),
                (key, mods) =>
                {
                    if (key == 0x1B && mods == ModifierKeys.None) Finish(false);
                    else if (hotkey) QueueShortcut(key, mods);
                    else AddChord(key, mods);
                }, commit => { if (commit && IsVisible) Close(); }, key => { if (hotkey) FinishShortcut(key); })) return;
            fallbackFocus = previous;
            ProtectFocus(false);
            Activate();
        }

        private void Finish(bool apply)
        {
            if (completed == null) { DialogResult = apply; return; }
            accepted = apply;
            Close();
        }

        private void RecordDown(object sender, KeyEventArgs args)
        {
            var key = ActionSequence.ReadKey(args);
            args.Handled = true;
            if (key == 0 || args.IsRepeat || ActionSequence.IsModifier(key)) return;
            var modifiers = Keyboard.Modifiers;
            if (!hotkey) Add(key);
            else if (key == 0x1B && modifiers == ModifierKeys.None) Finish(false);
            else QueueShortcut(key, modifiers);
        }

        private void RecordUp(object sender, KeyEventArgs args)
        {
            if (!hotkey || pending == null) return;
            args.Handled = true;
            FinishShortcut(ActionSequence.ReadKey(args));
        }

        private void QueueShortcut(int key, ModifierKeys modifiers)
        {
            try
            {
                pending = Shortcut.Parse(String.Join("+", ActionSequence.Record(key, modifiers).Select(ActionSequence.Name)));
                preview.Text = pending.Text;
                preview.Foreground = Glass.Ink;
            }
            catch (FormatException error)
            {
                pending = null;
                preview.Text = error.Message;
                preview.Foreground = Glass.Gold;
            }
        }

        private void FinishShortcut(int key)
        {
            if (pending == null || pending.Key != key) return;
            Value = pending.Text;
            pending = null;
            Finish(true);
        }

        private void Add(int key)
        {
            AddChord(key, Keyboard.Modifiers);
        }

        private void AddChord(int key, ModifierKeys mods)
        {
            if (steps.Count >= 64) return;
            var now = watch.ElapsedMilliseconds;
            steps.Add(new ActionSequence.Step(steps.Count == 0 ? 0 : (int)Math.Min(60000, now - last), ActionSequence.Record(key, mods)));
            last = now;
            Value = new ActionSequence(steps.ToArray()).Text;
            preview.Text = String.Join("  ›  ", steps.Skip(Math.Max(0, steps.Count - 4)).Select(step => step.Chord));
            accept.IsEnabled = true;
        }

        private sealed class F8Capture : IDisposable
        {
            public const int KeyCode = 0x77;
            private readonly IntPtr window;
            private readonly Action<ModifierKeys> received;
            private readonly HookProc callback;
            private IntPtr hook;
            private bool pressed;
            private ModifierKeys modifiers;

            public F8Capture(IntPtr window, Action<ModifierKeys> received)
            {
                this.window = window;
                this.received = received;
                callback = HandleKey;
                // A foreground-only, temporary capture for F8 even when another instance registered it.
                hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            }

            private IntPtr HandleKey(int code, IntPtr message, IntPtr keyData)
            {
                if (code >= 0 && Marshal.ReadInt32(keyData) == KeyCode)
                {
                    var foreground = Windows.GetForegroundWindow() == window;
                    var keyMessage = message.ToInt32();
                    if (keyMessage == 0x0101 || keyMessage == 0x0105)
                    {
                        if (pressed)
                        {
                            pressed = false;
                            if (foreground) received(modifiers);
                            return new IntPtr(1);
                        }
                    }
                    else if ((keyMessage == 0x0100 || keyMessage == 0x0104) && (foreground || pressed))
                    {
                        if (!pressed) modifiers = ReadModifiers();
                        pressed = true;
                        return new IntPtr(1);
                    }
                }
                return CallNextHookEx(hook, code, message, keyData);
            }

            private static ModifierKeys ReadModifiers()
            {
                var state = ModifierKeys.None;
                if (GetAsyncKeyState(0x10) < 0) state |= ModifierKeys.Shift;
                if (GetAsyncKeyState(0x11) < 0) state |= ModifierKeys.Control;
                if (GetAsyncKeyState(0x12) < 0) state |= ModifierKeys.Alt;
                if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) state |= ModifierKeys.Windows;
                return state;
            }

            public void Dispose()
            {
                if (hook == IntPtr.Zero) return;
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }

            private delegate IntPtr HookProc(int code, IntPtr message, IntPtr keyData);
            [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
            private static extern IntPtr SetWindowsHookEx(int id, HookProc procedure, IntPtr module, uint thread);
            [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr keyData);
            [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
            [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
        }
    }
}
