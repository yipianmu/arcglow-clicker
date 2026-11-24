using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace GlassClicker
{
    internal sealed class ProfileDialog : GlassWindow
    {
        public string Value { get; private set; }
        private Action<string> chosen;

        public ProfileDialog(GlassWindow owner, bool save, string[] names, string current,
            Action<string> remove = null, Func<bool> hasOutput = null)
            : base(save ? "保存配置" : "应用内配置", 320,
                108 + (save ? 39 : 23) + (names.Length == 0 ? 0 : 10 + Math.Min(names.Length, 4) * 34), true)
        {
            Owner = owner;
            var card = Glass.Card(new Thickness(14));
            Content = card;
            var body = new StackPanel();
            card.Child = body;
            var title = Glass.Text(save ? "保存当前配置" : "切换配置", 17, Glass.Ink, true);
            title.Background = Brushes.Transparent;
            body.Children.Add(title);
            DragBy(title);
            var nameField = new TextBox { Text = current ?? "新配置", FontFamily = Glass.Font, FontSize = 12,
                Foreground = Glass.Ink, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center };
            if (save)
            {
                var field = Glass.Tile("#FCFEFF", 9, new Thickness(9, 1, 9, 1));
                field.Height = 32;
                field.Margin = new Thickness(0, 7, 0, 0);
                field.BorderBrush = Glass.MutedLine; field.BorderThickness = new Thickness(1);
                field.Child = nameField; body.Children.Add(field);
                System.Windows.Automation.AutomationProperties.SetName(nameField, "配置名称");
            }
            var currentLabel = Glass.Text("", 11, Glass.Muted);
            Action showCurrent = () => currentLabel.Text = "当前 · " + (current ?? "未命名配置");
            if (!save)
            {
                currentLabel.Margin = new Thickness(0, 5, 0, 1);
                currentLabel.TextTrimming = TextTrimming.CharacterEllipsis;
                showCurrent();
                body.Children.Add(currentLabel);
            }
            var profiles = new ObservableCollection<string>(names);
            var list = new ListBox { ItemsSource = profiles, FontFamily = Glass.Font, FontSize = 12,
                Height = Math.Min(names.Length, 4) * 34 + 3, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(0),
                ItemContainerStyle = ProfileItemStyle() };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            if (names.Length > 0)
            {
                if (!save)
                    list.SelectedItem = names.FirstOrDefault(name => String.Equals(name, current, StringComparison.OrdinalIgnoreCase)) ?? names[0];
                list.SelectionChanged += (sender, args) =>
                {
                    if (save && list.SelectedItem is string) nameField.Text = (string)list.SelectedItem;
                };
            }
            list.Visibility = names.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            body.Children.Add(list);
            var note = Glass.Text("", 10, Glass.Muted);
            Action showNote = () =>
            {
                if (save) return;
                note.Text = hasOutput != null && hasOutput() ? "切换配置会停止当前输出。" :
                    profiles.Count == 0 ? "暂无配置。可以保存当前布局或导入文件。" : "";
            };
            showNote();
            list.SelectionChanged += (sender, args) => showNote();
            note.Height = 18;
            body.Children.Add(note);
            var buttons = TaskPanel.Columns(GridLength.Auto, new GridLength(6), GridLength.Auto,
                new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(7), GridLength.Auto);
            var confirm = Glass.Tile("#FCF1F2", 10, new Thickness(8, 4, 4, 4));
            confirm.Visibility = Visibility.Collapsed;
            string pending = null;
            Action resize = () =>
            {
                list.Visibility = profiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                list.Height = Math.Min(profiles.Count, 4) * 34 + 3;
                Height = 108 + (save ? 39 : 23) + (profiles.Count == 0 ? 0 : 10 + Math.Min(profiles.Count, 4) * 34) +
                    (pending == null ? 0 : 41);
            };
            Action clearConfirm = () =>
            {
                pending = null;
                confirm.Visibility = Visibility.Collapsed;
                resize();
            };
            if (!save)
            {
                var import = Glass.Button("导入文件");
                import.Click += (sender, args) =>
                {
                    var picker = new OpenFileDialog { Filter = "连点器配置 (*.clicker.json)|*.clicker.json|JSON 文件 (*.json)|*.json" };
                    using (new ForegroundReturn()) if (picker.ShowDialog(this) != true) return;
                    Complete(picker.FileName);
                };
                Glass.On(buttons, import, 0, 0);
                if (remove != null)
                {
                    var delete = Glass.Button("删除", Glass.Surface, Glass.DeleteInk);
                    System.Windows.Automation.AutomationProperties.SetName(delete, "删除选中的配置");
                    delete.IsEnabled = list.SelectedItem != null;
                    Glass.On(buttons, delete, 0, 2);
                    var line = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(4), GridLength.Auto);
                    var question = Glass.Text("", 11, Glass.DeleteInk, true);
                    question.TextTrimming = TextTrimming.CharacterEllipsis;
                    Glass.On(line, question, 0, 0);
                    var keep = Glass.Button("保留", Glass.White); keep.Height = 26;
                    keep.Click += (sender, args) => clearConfirm();
                    Glass.On(line, keep, 0, 1);
                    var confirmDelete = Glass.Button("确认删除", Glass.DeleteInk, Glass.White); confirmDelete.Height = 26;
                    confirmDelete.Click += (sender, args) =>
                    {
                        var name = pending;
                        if (name == null) return;
                        try
                        {
                            remove(name);
                            if (String.Equals(current, name, StringComparison.OrdinalIgnoreCase))
                            {
                                current = null;
                                showCurrent();
                            }
                            var selected = list.SelectedIndex;
                            profiles.Remove(name);
                            if (profiles.Count != 0) list.SelectedIndex = Math.Max(0, Math.Min(selected, profiles.Count - 1));
                            clearConfirm();
                            note.Text = "已删除「" + name + "」";
                        }
                        catch (Exception error)
                        {
                            if (!Workbench.IsConfigError(error)) throw;
                            note.Text = "删除失败。" + error.Message;
                        }
                    };
                    Glass.On(line, confirmDelete, 0, 3);
                    confirm.Child = line;
                    confirm.Margin = new Thickness(0, 5, 0, 2);
                    body.Children.Add(confirm);
                    delete.Click += (sender, args) =>
                    {
                        pending = list.SelectedItem as string;
                        if (pending == null) return;
                        question.Text = "删除「" + pending + "」？";
                        confirm.Visibility = Visibility.Visible;
                        resize();
                        keep.Focus();
                    };
                    list.SelectionChanged += (sender, args) => { clearConfirm(); delete.IsEnabled = list.SelectedItem != null; };
                    PreviewKeyDown += (sender, args) =>
                    {
                        if (args.Key != Key.Escape || pending == null) return;
                        clearConfirm(); args.Handled = true;
                    };
                }
            }
            var cancel = Glass.Button("取消");
            cancel.Click += (sender, args) => Close();
            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key != Key.Escape || args.Handled) return;
                args.Handled = true;
                Close();
            };
            var accept = Glass.Button(save ? "保存" : "切换", Glass.Primary, Brushes.White); accept.IsDefault = true;
            System.Windows.Automation.AutomationProperties.SetName(accept, save ? "保存配置" : "切换到所选配置");
            accept.IsEnabled = save || list.SelectedItem != null;
            list.SelectionChanged += (sender, args) => accept.IsEnabled = save || list.SelectedItem != null;
            Action submit = () =>
            {
                if (pending != null) return;
                var name = (save ? nameField.Text : list.SelectedItem as string ?? "").Trim();
                if (!ValidName(name))
                {
                    note.Text = "请输入有效的配置名称。";
                    return;
                }
                Complete(name);
            };
            accept.Click += (sender, args) => submit();
            list.MouseDoubleClick += (sender, args) =>
            {
                var source = args.OriginalSource as DependencyObject;
                if (!save && source != null && ItemsControl.ContainerFromElement(list, source) is ListBoxItem) submit();
            };
            Glass.On(buttons, cancel, 0, 4);
            Glass.On(buttons, accept, 0, 6);
            body.Children.Add(buttons);
            Loaded += (sender, args) =>
            {
                var position = owner == null ? Windows.Cursor() : owner.Position;
                Move(new ScreenPoint(position.X + 20, position.Y + 52));
                if (FocusProtected) return;
                if (save) { nameField.Focus(); nameField.SelectAll(); }
                else if (names.Length == 0) cancel.Focus();
                else list.Focus();
            };
        }

        public void ShowWithoutFocus(Action<string> onChosen)
        {
            chosen = onChosen;
            ProtectFocus(true);
            Show();
            RaiseWithoutFocus();
        }

        private void Complete(string value)
        {
            Value = value;
            if (chosen == null) { DialogResult = true; return; }
            var loaded = chosen;
            Close();
            loaded(value);
        }

        internal static bool ValidName(string name)
        {
            return !String.IsNullOrWhiteSpace(name) && name.Length <= 48 && name == name.Trim() &&
                !name.EndsWith(".") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static Style ProfileItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            var template = new ControlTemplate(typeof(ListBoxItem));
            var surface = new FrameworkElementFactory(typeof(Border), "Surface");
            surface.SetValue(Border.BackgroundProperty, Glass.Surface);
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            surface.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 4));
            surface.SetValue(Border.HeightProperty, 30.0);
            surface.SetValue(Border.PaddingProperty, new Thickness(9, 0, 9, 0));
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetValue(TextBlock.ForegroundProperty, Glass.Ink);
            label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            label.SetBinding(TextBlock.TextProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            surface.AppendChild(label);
            template.VisualTree = surface;
            var over = new Trigger { Property = IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, Glass.Selected, "Surface"));
            template.Triggers.Add(over);
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Glass.Fill("#E3EAF6"), "Surface"));
            template.Triggers.Add(selected);
            style.Setters.Add(new Setter(TemplateProperty, template));
            return style;
        }
    }
}
