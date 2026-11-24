using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace GlassClicker
{
    internal sealed class ClickTask
    {
        public readonly RepeatWorker Worker = new RepeatWorker();
        public GlassOrb Orb;
        public TargetBinding Target, RunningTarget;
        public ScreenPoint Home;
        public ScreenPoint? EditorHome;
        public int Color;
        public int IntervalVariation, RunSeconds;
        public bool Infinite = true, ContinuousHold, Tracking;
        public string Action = "左键", Interval = "100", Delay = "0", Hold = "0", Count = "100";
        public string RunningAction;
        public TargetBinding OutputTarget { get { return Worker.State.HasSession ? RunningTarget : Target; } }
        public string TargetName { get { return Target == null ? "未选择窗口" : Target.Name; } }
        public bool OutputActive
        {
            get
            {
                var target = OutputTarget;
                return Worker.State.Phase == RunPhase.Active && target != null && target.Window != IntPtr.Zero;
            }
        }

        public SavedJob Snapshot(bool starting)
        {
            int interval, delay, hold;
            long count;
            if (!Int32.TryParse(Interval, out interval) || interval < 10 || interval > 60000)
            {
                if (!ContinuousHold) throw new FormatException("发送间隔请输入 10–60000 ms。");
                interval = 100;
            }
            if (!Int32.TryParse(Delay, out delay) || delay < 0 || delay > 600000)
                throw new FormatException("启动延时请输入 0–600000 ms。");
            if (!Int32.TryParse(Hold, out hold) || hold < 0 || hold > 60000)
            {
                if (!ContinuousHold) throw new FormatException("按住时长请输入 0–60000 ms。");
                hold = 0;
            }
            if (!Int64.TryParse(Count, out count) || count < 1 || count > 9999999)
            {
                if (!Infinite && !ContinuousHold) throw new FormatException("重复次数请输入 1–9999999。");
                count = 100;
            }
            if (starting && Target == null) throw new FormatException("请先选择窗口，或把准星拖到目标窗口。");
            return new SavedJob { Action = ActionSequence.Parse(Action).Text, Interval = interval, Delay = delay, Count = count,
                Hold = hold, ContinuousHold = ContinuousHold, Infinite = Infinite, Color = Color, Left = Home.X, Top = Home.Y,
                IntervalVariation = IntervalVariation, RunSeconds = RunSeconds,
                EditorLeft = EditorHome.HasValue ? (int?)EditorHome.Value.X : null,
                EditorTop = EditorHome.HasValue ? (int?)EditorHome.Value.Y : null,
                Target = starting || Target == null ? null : Target.Export() };
        }
    }

    internal sealed class Workbench : IDisposable
    {
        private const int ShortcutId = 1, OverlayKeyId = 2, EmergencyKeyId = 3;
        private const uint F9 = 0x78, Control = 2, NoRepeat = 0x4000;
        private const long OrbsOnlyIdle = 4400, ProcessPanelIdle = 11000;
        private const string Brand = "弧光";
        private const string ProfileSuffix = ".clicker.json";
        private static readonly string DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassClicker");
        private readonly Application app;
        private readonly bool rememberChanges;
        private readonly HwndSource messages;
        private readonly Forms.NotifyIcon tray;
        private readonly QuickMenu trayMenu;
        private readonly Drawing.Icon icon;
        private readonly WinEvent foregroundEvent;
        private readonly WinEvent locationEvent;
        private readonly IntPtr foregroundHook, locationHook;
        private readonly DispatcherTimer frames, locatorPreview, settingsSaveTimer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<ClickTask> tasks = new List<ClickTask>();
        private readonly List<RepeatWorker> retiring = new List<RepeatWorker>();
        private readonly List<Motion> motions = new List<Motion>();
        private readonly Dictionary<IntPtr, WindowOutline> outlines = new Dictionary<IntPtr, WindowOutline>();
        private readonly string dataRoot, profilesRoot, layoutPath, preferencePath, settingsPath;
        private readonly AppSettings preferences;
        private readonly GlassOrb hub = new GlassOrb(Palette.Get(0), true);
        private readonly Dictionary<ClickTask, ProcessRow> processRows = new Dictionary<ClickTask, ProcessRow>();
        private Shortcut shortcut, emergencyKey;
        private bool emergencyRegistered;
        private int overlayModifiers = -1;
        private int suspendedOverlayModifiers = -1;
        private bool keyRegistered, keySuspended, expanded, packing, hidden, exiting, disposed, rendering;
        private ScreenPoint hubHome;
        private TaskPanel panel;
        private SettingsDialog settingsDialog;
        private GuideWindow guide;
        private Locator locator;
        private ClickTask locatorTask;
        private bool drawingLocator;
        private ScreenPoint? pendingLocatorPoint;
        private IntPtr pointedWindow, previewCandidate;
        private ClickTask tracking;
        private MousePressWatch targetPicker;
        private IntPtr lastForeground;
        private GlassWindow status;
        private EdgeLight statusEdge;
        private GlassOrb statusAnchor;
        private ScreenPoint? statusHome;
        private ClickTask hiddenEditor;
        private ScreenPoint? hiddenEditorPosition;
        private GlassOrb hiddenStatusAnchor;
        private ScreenPoint? hiddenStatusPosition;
        private bool hiddenWasExpanded;
        private GlassWindow undoToast;
        private ClickTask removedTask;
        private int removedIndex;
        private long undoUntil;
        private Button statusStart;
        private long lastLabels, trayRefresh, lastOutlineLayout, lastRecovery;
        private long shortcutRetry;
        private long saveAt;
        private long lastInteraction, mergeAt;
        private TimeSpan lastFrame = TimeSpan.MinValue;
        private ClickTask mergeAfterEditor;
        private bool outlineDirty = true, outlineQueued, editingPauseQueued;
        private string profileName;
        private ProfileDialog profileBrowser;
        private readonly List<Motion> movedThisFrame = new List<Motion>();
        private readonly List<Action> finishedThisFrame = new List<Action>();

        public string ShortcutText { get { return shortcut.Text; } }
        public bool ShortcutAvailable { get { return keyRegistered; } }
        public string EmergencyKeyText { get { return emergencyKey.Text; } }
        public bool EmergencyKeyAvailable { get { return emergencyRegistered; } }
        public string OverlayKeyText { get { return overlayModifiers < 0 ? null : overlayModifiers == 0 ? "F9" : "Ctrl+F9"; } }
        public bool HasRunningJobs { get { return tasks.Any(task => task.Worker.State.HasSession); } }
        public bool LocatorVisible { get { return locator != null; } }
        public bool PauseOthersWhileEditing { get { return preferences.PauseOthersWhileEditing; } }
        public bool RememberPanelPosition { get { return preferences.RememberPanelPosition; } }
        public bool InterfaceSounds { get { return preferences.InterfaceSounds; } }
        public int OutlineStrength { get { return preferences.OutlineStrength; } }
        private bool Running { get { return tasks.Any(CountsAsGroupOutput); } }
        private bool Collected { get { return tasks.Count > 1 && !expanded; } }

        private static bool CountsAsGroupOutput(ClickTask task)
        {
            var state = task.Worker.State;
            return state.Running || state.PausedForEditing;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var preset = args.Length == 1 ? args[0] : null;
            using (var workbench = new Workbench(app, preset)) app.Run();
        }

        private Workbench(Application app, string preset)
        {
            this.app = app;
            rememberChanges = preset == null;
            dataRoot = rememberChanges ? DataRoot : Path.GetDirectoryName(Path.GetFullPath(preset));
            profilesRoot = Path.Combine(dataRoot, "配置");
            layoutPath = Path.Combine(dataRoot, "当前配置.clicker.json");
            preferencePath = Path.Combine(dataRoot, "快捷键.txt");
            settingsPath = Path.Combine(dataRoot, "设置.json");
            preferences = ReadSettings();
            UiSounds.SetEnabled(preferences.InterfaceSounds);
            emergencyKey = Shortcut.Parse(preferences.EmergencyKey);
            lastForeground = Windows.ForegroundTarget();
            var area = Glass.Screen(Windows.Cursor()).WorkingArea;
            hubHome = new ScreenPoint(area.Right - 160, area.Top + (int)(area.Height * .62));
            var parameters = new HwndSourceParameters("GlassClicker messages") { ParentWindow = new IntPtr(-3) };
            messages = new HwndSource(parameters);
            messages.AddHook(MessageReceived);
            shortcut = ReadShortcut();
            if (rememberChanges)
            {
                ResumeShortcut();
                emergencyRegistered = RegisterEmergencyKey(emergencyKey);
            }

            foregroundEvent = (hook, kind, window, objectId, childId, thread, time) => ObserveForeground();
            foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundEvent, 0, 0, 2);
            locationEvent = (hook, kind, window, objectId, childId, thread, time) =>
            {
                if (objectId == 0 && childId == 0 && outlines.ContainsKey(window)) QueueOutlineLayout();
            };
            locationHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, locationEvent, 0, 0, 2);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("clicker.ico"))
            using (var resource = new Drawing.Icon(stream)) icon = (Drawing.Icon)resource.Clone();
            tray = new Forms.NotifyIcon { Icon = icon, Text = Brand + " · 连点器", Visible = true };
            trayMenu = CreateTrayMenu();
            tray.MouseUp += (sender, args) =>
            {
                if (args.Button == Forms.MouseButtons.Right)
                    app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowTrayMenu));
            };
            tray.DoubleClick += (sender, args) => { if (hidden) ToggleVisibility(); else ShowOrbs(); };

            hub.Grabbed = () => { Touch(); CancelMotion(hub); };
            hub.LocationChanged += (sender, args) =>
            {
                if (undoToast != null && tasks.Count == 0 && hub.IsVisible) MoveUndo(hub.Position);
            };
            hub.Dragged = () =>
            {
                RememberHubPosition(); RepositionPanels(hub);
                Touch(); ScheduleLayoutSave();
            };
            hub.Tapped = () => { if (tasks.Count == 0) AddTask(); else RevealTasks(); };
            hub.OutputTapped = hub.Tapped;
            hub.ProcessTapped = index => { if (index >= 0 && index < tasks.Count) ShowEditor(tasks[index]); };
            hub.ProcessMenu = index => OrbMenu(tasks[index]);
            hub.ContextMenu = OrbMenu(null);

            frames = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(65) };
            frames.Tick += (sender, args) => Tick();
            locatorPreview = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(32) };
            locatorPreview.Tick += (sender, args) => RefreshLocatorPreview();
            settingsSaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            settingsSaveTimer.Tick += (sender, args) =>
            {
                if (PointerDrag.InProgress) return;
                var error = SaveSettings();
                if (error != null) ConfigurationNotice(settingsDialog, error);
            };
            var first = MakeTask(null, 0);
            var initialLayout = preset ?? (File.Exists(layoutPath) ? layoutPath : null);
            var fresh = initialLayout == null || !File.Exists(initialLayout);
            var pointer = Windows.Cursor();
            if (fresh)
            {
                first.Home = FirstHome(first.Orb, pointer);
            }
            ShowWindow(first.Orb, first.Home);
            if (fresh) first.Orb.Move(FirstHome(first.Orb, pointer), false);
            first.Home = first.Orb.Position;
            if (fresh) hubHome = first.Home;
            if (initialLayout != null)
            {
                try { LoadLayout(LayoutFile.Read(initialLayout)); }
                catch (Exception error)
                {
                    if (!IsConfigError(error)) throw;
                    ResumeShortcut();
                    Report("打开配置失败。" + error.Message, true);
                }
            }
            RefreshLabels();
            frames.Start();
            SetRendering(true);
            if (rememberChanges) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowFirstGuide));
        }

        private QuickMenu CreateTrayMenu()
        {
            var menu = new QuickMenu { MinWidth = 176 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 7, 9, 7) };
            heading.Children.Add(new Image { Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(21, 21)), Width = 21, Height = 21,
                Margin = new Thickness(0, 0, 7, 0) });
            heading.Children.Add(Glass.Text(Brand, 15, Glass.Ink, true));
            menu.Tag = heading;
            menu.Opened += (sender, args) => Touch();
            var visibility = menu.Add("隐藏悬浮界面", "M1 7 Q7 1 13 7 Q7 13 1 7 Z M5 7 A2 2 0 1 1 9 7 A2 2 0 1 1 5 7", ToggleVisibility);
            var panelItem = menu.Add("打开连点面板", "M2 2 H12 V12 H2 Z M2 5 H12 M5 8 H9", ToggleOverlay);
            var output = menu.Add("开始全部", "M4 2 L12 7 L4 12 Z", ToggleAll);
            menu.Add("设置", Glass.GearGlyph, ShowSettings);
            menu.Separate();
            menu.Add("退出弧光", "M7 1 V7 M4 3 A5 5 0 1 0 10 3", Quit);
            menu.Opened += (sender, args) =>
            {
                visibility.Header = hidden ? "显示悬浮界面" : "隐藏悬浮界面";
                panelItem.Header = panel != null || status != null ? "收纳连点面板" : "打开连点面板";
                output.Header = Running ? "停止全部" : "开始全部";
            };
            return menu;
        }

        private void ShowTrayMenu()
        {
            if (exiting || disposed) return;
            trayMenu.IsOpen = false;
            trayMenu.IsOpen = true;
        }

        private Shortcut ReadShortcut()
        {
            try { if (File.Exists(preferencePath)) return Shortcut.Parse(File.ReadAllText(preferencePath)); }
            catch (Exception error) { if (!IsConfigError(error)) throw; }
            return Shortcut.Parse(AppSettings.DefaultShortcut);
        }

        private IntPtr MessageReceived(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x0312) return IntPtr.Zero;
            if (wParam.ToInt32() == EmergencyKeyId && emergencyRegistered)
            {
                handled = true;
                StopAll();
                Report("已紧急停止全部输出");
                return IntPtr.Zero;
            }
            if (keySuspended) return IntPtr.Zero;
            switch (wParam.ToInt32())
            {
                case ShortcutId:
                    if (keyRegistered) { handled = true; ToggleAll(); }
                    break;
                case OverlayKeyId:
                    if (overlayModifiers >= 0) { handled = true; ToggleVisibility(); }
                    break;
            }
            return IntPtr.Zero;
        }

        public void SuspendShortcut()
        {
            if (!keySuspended) suspendedOverlayModifiers = overlayModifiers;
            keySuspended = true;
            ReleaseShortcut();
        }

        private void ReleaseShortcut()
        {
            if (keyRegistered) UnregisterHotKey(messages.Handle, ShortcutId);
            if (overlayModifiers >= 0) UnregisterHotKey(messages.Handle, OverlayKeyId);
            keyRegistered = false;
            overlayModifiers = -1;
        }

        public void ResumeShortcut()
        {
            keySuspended = false;
            suspendedOverlayModifiers = -1;
            if (!keyRegistered) keyRegistered = RegisterShortcut(shortcut);
            RegisterOverlayKey();
        }

        private bool RegisterShortcut(Shortcut candidate)
        {
            return RegisterHotKey(messages.Handle, ShortcutId, candidate.Modifiers | NoRepeat, (uint)candidate.Key);
        }

        private bool RegisterEmergencyKey(Shortcut candidate)
        {
            return RegisterHotKey(messages.Handle, EmergencyKeyId, candidate.Modifiers | NoRepeat, (uint)candidate.Key);
        }

        public string ChangeEmergencyKey(string value)
        {
            var candidate = Shortcut.Parse(value);
            var overlay = keySuspended ? suspendedOverlayModifiers : overlayModifiers;
            if (candidate.Key == shortcut.Key && candidate.Modifiers == shortcut.Modifiers ||
                candidate.Key == F9 && overlay >= 0 && candidate.Modifiers == (uint)overlay)
                return candidate.Text + " 已用于弧光的其他操作";
            if (candidate.Text == emergencyKey.Text && emergencyRegistered) return null;
            var wasRegistered = emergencyRegistered;
            if (wasRegistered) UnregisterHotKey(messages.Handle, EmergencyKeyId);
            emergencyRegistered = RegisterEmergencyKey(candidate);
            if (!emergencyRegistered)
            {
                emergencyRegistered = wasRegistered && RegisterEmergencyKey(emergencyKey);
                return candidate.Text + " 正被占用";
            }
            emergencyKey = candidate;
            preferences.EmergencyKey = candidate.Text;
            return SaveSettings();
        }

        private void RegisterOverlayKey()
        {
            if (!rememberChanges || overlayModifiers >= 0) return;
            if (RegisterHotKey(messages.Handle, OverlayKeyId, NoRepeat, F9)) overlayModifiers = 0;
            else if (RegisterHotKey(messages.Handle, OverlayKeyId, Control | NoRepeat, F9)) overlayModifiers = (int)Control;
        }

        public void ToggleOverlay()
        {
            var fullscreen = ForegroundFullscreen();
            if (fullscreen != IntPtr.Zero && Windows.IsMinecraft(fullscreen) && Windows.CursorCaptured())
            {
                WindowSender.PauseMinecraft(fullscreen);
                if (panel != null || status != null) return;
            }
            if (panel != null) { DismissEditor(); return; }
            if (status != null) { CloseStatus(); Touch(); return; }
            if (hidden) ShowOrbs();
            if (packing) FinishPacking();
            if (tasks.Count == 0) { AddTask(); return; }
            if (tasks.Count == 1) ShowEditor(tasks[0]);
            else RevealTasks();
        }

        public void ToggleVisibility()
        {
            UiSounds.Click();
            if (!hidden)
            {
                hiddenEditor = panel == null ? null : panel.Task;
                hiddenEditorPosition = panel == null ? (ScreenPoint?)null : panel.Position;
                hiddenStatusAnchor = statusAnchor;
                hiddenStatusPosition = status == null ? (ScreenPoint?)null : status.Position;
                hiddenWasExpanded = expanded;
                HideOrbs();
                return;
            }
            var editor = hiddenEditor;
            var editorPosition = hiddenEditorPosition;
            var anchor = hiddenStatusAnchor;
            var position = hiddenStatusPosition;
            expanded = hiddenWasExpanded && tasks.Count > 1;
            ShowOrbs();
            if (editor != null && tasks.Contains(editor))
            {
                ShowEditor(editor);
                if (panel != null && editorPosition.HasValue) panel.Move(editorPosition.Value);
            }
            else if (anchor != null && (anchor == hub || tasks.Any(task => task.Orb == anchor))) OpenStatus(anchor, position);
        }

        private static IntPtr ForegroundFullscreen()
        {
            var foreground = Windows.ForegroundTarget();
            return Windows.IsFullscreen(foreground) ? foreground : IntPtr.Zero;
        }

        public void ChangeShortcut(string value)
        {
            var candidate = Shortcut.Parse(value);
            if (!UseShortcut(candidate)) { Report(candidate.Text + " 正被占用", true); return; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preferencePath));
                File.WriteAllText(preferencePath, shortcut.Text);
                ScheduleLayoutSave();
                Report("已设置「" + shortcut.Text + "」。");
            }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
                Report("快捷键当前可用。保存设置失败。", true);
            }
        }

        private bool UseShortcut(Shortcut candidate)
        {
            if (candidate.Text == shortcut.Text && keyRegistered) return true;
            ReleaseShortcut();
            keyRegistered = RegisterShortcut(candidate);
            if (keyRegistered) { shortcut = candidate; RegisterOverlayKey(); }
            else ResumeShortcut();
            if (panel != null) panel.Refresh();
            return candidate.Text == shortcut.Text && keyRegistered;
        }

        private void ObserveForeground()
        {
            if (locator != null && locator.IsVisible)
            {
                var surface = Windows.Root(Windows.GetForegroundWindow());
                if (Windows.Eligible(surface, true))
                {
                    Windows.Rectangle previewBounds;
                    Windows.Frame(surface, out previewBounds);
                    locator.RaiseWithoutFocus();
                }
            }
            var current = Windows.ForegroundTarget();
            if (current == IntPtr.Zero) return;
            if (current != lastForeground)
            {
                lastForeground = current;
                QueueOutlineLayout();
            }
            if (tracking != null && (targetPicker == null || !targetPicker.Available)) SelectForeground(tracking, current);
        }

        private void SelectForeground(ClickTask task, IntPtr current)
        {
            var former = task.Target;
            var target = former != null && former.Window == current ? former :
                former != null && former.Window == IntPtr.Zero ? former.ReconnectGame(current) ?? TargetBinding.Create(current) :
                TargetBinding.Create(current);
            if (target == null) return;
            SetTarget(task, target);
            if (panel != null && panel.Task == task) Report("已选择窗口");
        }

        private void SetTarget(ClickTask task, TargetBinding target)
        {
            task.Target = target;
            if (task.Worker.SwitchWindow(target)) task.RunningTarget = target;
            Arm(null);
            QueueOutlineLayout();
            ScheduleLayoutSave();
        }

        private void Arm(ClickTask task)
        {
            if (tracking != null) tracking.Tracking = false;
            if (targetPicker != null) targetPicker.Dispose();
            targetPicker = null;
            tracking = task;
            if (task == null) return;
            task.Tracking = true;
            targetPicker = new MousePressWatch(app.Dispatcher, (point, button) =>
            {
                if (button != 0x0201 || tracking != task) return;
                var window = Windows.DirectTargetAt(point);
                if (window != IntPtr.Zero) SelectForeground(task, window);
            });
        }

        public void ToggleTracking(ClickTask task)
        {
            if (task.Tracking) { Arm(null); Report(""); }
            else ChooseWindow(task);
        }

        public void ChooseWindow(ClickTask task)
        {
            var prior = task.Target;
            if (prior != null && prior.Window == IntPtr.Zero && prior.RemembersGame)
            {
                var currentGame = Windows.RecognizeGameWindow(Windows.ForegroundTarget());
                var replacement = prior.ReconnectGame(currentGame);
                if (replacement != null)
                {
                    SetTarget(task, replacement);
                    Report("已接回 " + replacement.Name);
                    return;
                }
            }
            var fullscreen = ForegroundFullscreen();
            if (fullscreen != IntPtr.Zero && panel != null && panel.FullscreenOverlay)
            {
                SelectForeground(task, fullscreen);
                return;
            }
            Arm(task);
            Report(targetPicker.Available ? "点一下要控制的窗口" : "切到要控制的窗口");
        }

        private ClickTask MakeTask(SavedJob saved, int index, int? color = null)
        {
            var task = new ClickTask { Home = index == 0 ? hubHome : NewHome(index), Color = color ?? index };
            if (saved != null)
            {
                task.Color = saved.Color;
                task.Home = ClampAt(new ScreenPoint(saved.Left, saved.Top), 160, 118, hub.Scale);
                if (saved.EditorLeft.HasValue && saved.EditorTop.HasValue)
                    task.EditorHome = new ScreenPoint(saved.EditorLeft.Value, saved.EditorTop.Value);
                task.Action = saved.Action;
                task.Interval = saved.Interval.ToString(CultureInfo.InvariantCulture);
                task.Delay = saved.Delay.ToString(CultureInfo.InvariantCulture);
                task.Hold = saved.Hold.ToString(CultureInfo.InvariantCulture);
                task.ContinuousHold = saved.ContinuousHold;
                task.IntervalVariation = saved.IntervalVariation;
                task.RunSeconds = saved.RunSeconds;
                task.Count = saved.Count.ToString(CultureInfo.InvariantCulture);
                task.Infinite = saved.Infinite;
                task.Target = TargetBinding.Restore(saved.Target);
            }
            var orb = new GlassOrb(Palette.Get(task.Color));
            task.Orb = orb;
            orb.Tapped = () => ShowEditor(task);
            orb.Grabbed = () => { Touch(); CancelMotion(orb); orb.Transition(1, 1); };
            orb.Dragged = () => {
                task.Home = orb.Position;
                if (tasks.Count == 1) hubHome = task.Home;
                RepositionPanels(orb);
                Touch();
                ScheduleLayoutSave();
            };
            orb.OutputTapped = orb.Tapped;
            orb.CanDragTarget = () => task.Target == null && !task.Worker.State.HasSession;
            orb.TargetDragged = point => DrawLocator(task, point);
            orb.TargetReleased = point => FinishDrawLocator(task, point);
            orb.TargetCanceled = CancelDrawLocator;
            orb.ContextMenu = OrbMenu(task);
            tasks.Add(task);
            return task;
        }

        public void AddTask()
        {
            if (tasks.Count >= LayoutFile.MaxJobs) { Report("最多可同时配置 12 枚连点。", true); return; }
            FinishPacking();
            var wasCollected = Collected;
            if (tasks.Count == 1)
            {
                var first = tasks[0];
                hubHome = first.Home;
                first.Home = NewHome(0);
                if (first.Orb.IsVisible) Fly(first.Orb, first.Home);
            }
            var color = Enumerable.Range(0, LayoutFile.MaxJobs).Where(i => tasks.All(task => task.Color != i))
                .OrderBy(i => removedTask != null && i == removedTask.Color).First();
            if (removedTask != null && color == removedTask.Color) ForgetRemoval();
            var next = MakeTask(null, tasks.Count, color);
            expanded = tasks.Count > 1 && !wasCollected;
            if (wasCollected) ShowOrbs();
            else hub.Hide();
            ShowEditor(next);
            mergeAfterEditor = expanded ? next : null;
            RememberCurrentLayout();
        }

        private ScreenPoint NewHome(int index)
        {
            var area = Glass.Screen(hubHome).WorkingArea;
            var scale = hub.Scale;
            var width = (int)(132 * scale);
            var height = (int)(115 * scale);
            var direction = hubHome.X > area.Left + area.Width / 2 ? -1 : 1;
            var horizontalRoom = direction < 0 ? hubHome.X - area.Left : area.Right - hubHome.X - (int)(160 * scale);
            var columns = Math.Max(1, horizontalRoom / width);
            var row = index / columns;
            int vertical = row == 0 ? 0 : ((row + 1) / 2) * (row % 2 == 1 ? -height : height);
            return Glass.Clamp(new ScreenPoint(hubHome.X + direction * (index % columns + 1) * width, hubHome.Y + vertical),
                (int)(160 * scale), (int)(118 * scale));
        }

        private ContextMenu OrbMenu(ClickTask task)
        {
            var menu = new QuickMenu();
            var output = menu.Add("输出进程", "M2 3 H12 M2 7 H12 M2 11 H12", () =>
            { if (tasks.Count == 0) AddTask(); else ToggleStatus(task == null ? hub : task.Orb); });
            var gather = menu.Add("收纳悬浮球", "M2 3 L7 7 L12 3 M2 7 L7 11 L12 7", Collapse);
            var pauseItem = task == null ? null : menu.Add("暂停这枚", "M5 2 V12 M9 2 V12", () => ToggleOne(task));
            var stop = menu.Add(task == null ? "停止全部" : "停止这枚", "M3 3 H11 V11 H3 Z",
                () => { if (task == null) StopAll(); else StopOne(task); });
            menu.Separate();
            menu.Add("隐藏悬浮界面", "M1 7 Q7 1 13 7 Q7 13 1 7 M2 12 L12 2", ToggleVisibility);
            if (task != null) menu.Add("删除这枚", "M2 3 H12 M5 3 V1 H9 V3 M4 5 L5 12 H9 L10 5", () => RemoveTask(task), true);
            menu.Opened += (sender, args) =>
            {
                Touch();
                output.Header = tasks.Count == 0 ? "添加一枚连点" : "输出进程";
                stop.IsEnabled = task == null ? HasRunningJobs : task.Worker.State.HasSession;
                if (task != null)
                {
                    var state = task.Worker.State;
                    var canResume = state.Phase == RunPhase.Paused && !state.PausedForEditing;
                    pauseItem.Header = state.PausedForEditing ? "保持暂停" : canResume ? "继续这枚" : "暂停这枚";
                    ((System.Windows.Shapes.Path)pauseItem.Icon).Data = Geometry.Parse(canResume ? "M4 2 L12 7 L4 12 Z" : "M5 2 V12 M9 2 V12");
                    pauseItem.Visibility = task.Worker.State.HasSession ? Visibility.Visible : Visibility.Collapsed;
                }
                gather.Visibility = expanded && tasks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            };
            return menu;
        }

        public void RemoveTask(ClickTask task)
        {
            var originalIndex = tasks.IndexOf(task);
            if (originalIndex < 0) return;
            ForgetRemoval();
            Touch();
            FinishPacking();
            var statusPosition = status != null ? (ScreenPoint?)status.Position : null;
            var toastPosition = panel != null && panel.Task == task ? panel.Position :
                statusPosition ?? task.Home;
            if (task.Worker.State.HasSession)
            {
                task.Worker.Stop();
                retiring.Add(task.Worker);
            }
            tasks.RemoveAt(originalIndex);
            if (tracking == task) Arm(null);
            if (panel != null && panel.Task == task) CloseEditor(true);
            if (mergeAfterEditor == task) mergeAfterEditor = null;
            CloseStatus();
            CancelMotion(task.Orb);
            task.Orb.Hide();
            if (tasks.Count < 2) expanded = false;
            if (tasks.Count == 0) hubHome = task.Home;
            ShowOrbs();
            if (statusPosition.HasValue && tasks.Count > 0) OpenStatus(hub, statusPosition);
            removedTask = task;
            removedIndex = originalIndex;
            ShowUndo(toastPosition);
            RememberCurrentLayout();
        }

        private void ShowUndo(ScreenPoint position)
        {
            var width = status != null ? status.Width : tasks.Count == 0 ? 248 : 325;
            var toast = new GlassWindow("连点器 · 撤销删除", width, 52, false);
            var card = Glass.Card(new Thickness(13, 5, 11, 5), 15);
            var grid = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), GridLength.Auto);
            Glass.On(grid, Glass.Text("已删除 " + ShortAction(removedTask.Action), 11, Glass.Ink, true), 0, 0);
            var undo = Glass.Button("撤销");
            undo.Height = 28;
            undo.Click += (sender, args) => UndoRemoval();
            Glass.On(grid, undo, 0, 1);
            card.Child = grid;
            toast.Content = card;
            undoUntil = clock.ElapsedMilliseconds + 7500;
            undoToast = toast;
            MoveUndo(position);
            Glass.Animate(card, true);
        }

        private void MoveUndo(ScreenPoint fallback)
        {
            if (undoToast == null) return;
            if (status == null && tasks.Count == 0 && hub.IsVisible)
            {
                var orb = hub.Position;
                var orbCenter = CenterOf(hub, orb);
                var orbScale = hub.Scale;
                var boxWidth = (int)Math.Round(undoToast.Width * orbScale);
                var boxHeight = (int)Math.Round(undoToast.Height * orbScale);
                var padding = (int)Math.Round(8 * orbScale);
                var screen = Glass.Screen(orbCenter).WorkingArea;
                var y = orb.Y - boxHeight - padding;
                if (y < screen.Top + 4) y = orb.Y + (int)Math.Round(90 * orbScale) + padding;
                var aligned = Glass.Clamp(new ScreenPoint(orbCenter.X - boxWidth / 2, y), boxWidth, boxHeight);
                var previous = undoToast.Position;
                if (!undoToast.IsVisible || previous.X != aligned.X || previous.Y != aligned.Y)
                    ShowWindow(undoToast, aligned, false);
                return;
            }
            var point = status == null ? fallback : status.Position;
            var scale = status == null ? undoToast.Scale : status.Scale;
            var area = Glass.Screen(point).WorkingArea;
            var gap = (int)Math.Ceiling(5 * scale);
            var height = (int)Math.Round(undoToast.Height * scale);
            var top = point.Y - height - gap;
            if (top < area.Top)
                top = status == null ? point.Y + gap : point.Y + (int)Math.Round(status.Height * scale) + gap;
            var at = new ScreenPoint(point.X, Math.Max(area.Top, Math.Min(top, area.Bottom - height)));
            if (status == null) ShowWindow(undoToast, at);
            else if (!undoToast.IsVisible || undoToast.Position.X != at.X || undoToast.Position.Y != at.Y) ShowWindow(undoToast, at, false);
        }

        private void UndoRemoval()
        {
            var task = removedTask;
            if (task == null) return;
            removedTask = null;
            ForgetRemoval();
            FinishPacking();
            var statusPosition = status != null ? (ScreenPoint?)status.Position : null;
            tasks.Insert(Math.Min(removedIndex, tasks.Count), task);
            retiring.Remove(task.Worker);
            if (statusPosition.HasValue && Collected)
            {
                Spread(panel == null ? null : panel.Task);
            }
            else
            {
                CloseStatus();
                ShowOrbs();
            }
            if (statusPosition.HasValue) OpenStatus(hub, statusPosition);
            QueueOutlineLayout();
            RememberCurrentLayout();
        }

        private void ForgetRemoval()
        {
            if (undoToast != null) { undoToast.Close(); undoToast = null; }
            if (removedTask != null) { removedTask.Orb.Close(); removedTask = null; }
        }

        private void FinishPacking()
        {
            if (!packing) return;
            CancelMotion(hub);
            foreach (var task in tasks)
            {
                CancelMotion(task.Orb);
                task.Orb.Hide();
            }
            packing = false;
            hub.Transition(1, 1);
            hub.IsHitTestVisible = true;
        }

        private void RevealTasks()
        {
            if (hidden || packing) return;
            if (Collected) CloseEditor(true);
            Spread(null);
            OpenStatus(hub);
        }

        private void Spread(ClickTask editing)
        {
            if (hidden || tasks.Count < 2 || expanded) return;
            CloseStatus();
            Touch();
            var center = CenterOf(hub, HubPosition());
            CancelMotion(hub);
            hub.Hide();
            hub.Transition(1, 1);
            hub.IsHitTestVisible = true;
            packing = false;
            expanded = true;
            foreach (var task in tasks)
            {
                if (task == editing) continue;
                var orb = task.Orb;
                if (!orb.IsVisible) ShowWindow(orb, AlignedAt(orb, center));
                orb.IsHitTestVisible = true;
                orb.Transition(.64, .60);
                Fly(orb, task.Home, null, fraction => orb.Transition(.64 + .36 * Glass.Spring(fraction),
                    Math.Min(1, .60 + .62 * fraction)), 325);
            }
            RefreshLabels();
        }

        public void Collapse()
        {
            if (hidden || packing) return;
            var editing = panel == null ? null : panel.Task;
            CloseEditor(true);
            mergeAfterEditor = null;
            CloseStatus();
            if (editing != null && !Collected) ShowFolded(editing);
            if (!expanded) { RefreshLabels(); return; }

            Touch();
            expanded = false;
            packing = true;
            hub.SetProcesses(tasks.Select(task => task.Color).ToArray());
            hub.Transition(.57, 0, 0);
            hub.IsHitTestVisible = false;
            ShowWindow(hub, HubPosition());
            RememberHubPosition();
            var center = CenterOf(hub, hub.Position);
            foreach (var task in tasks)
            {
                var orb = task.Orb;
                if (!orb.IsVisible) ShowFolded(task);
                orb.IsHitTestVisible = false;
                Fly(orb, AlignedAt(orb, center), () =>
                {
                    orb.Hide();
                    orb.Transition(1, 1);
                    orb.IsHitTestVisible = true;
                },
                portion => orb.Transition(1 - .69 * portion * portion, 1 - portion * portion), 315);
            }
            Fly(hub, hub.Position, () => { packing = false; hub.Transition(1, 1); hub.IsHitTestVisible = true; },
                portion =>
                {
                    var arrival = Math.Max(0, (portion - .25) / .75);
                    var settle = Glass.Spring(arrival);
                    hub.Transition(.57 + .43 * settle, arrival, arrival);
                }, 390);
            RefreshLabels();
        }

        public void DismissEditor()
        {
            if (panel == null) return;
            var task = panel.Task;
            var location = panel.Position;
            var scale = panel.Scale;
            var origin = new ScreenPoint(location.X + (int)Math.Round(panel.Width * scale / 2),
                location.Y + (int)Math.Round(panel.Height * scale / 2));
            CloseEditor(true);
            ReturnToOrb(task, origin);
            RememberCurrentLayout();
        }

        private void ReturnToOrb(ClickTask task, ScreenPoint? origin = null)
        {
            if (hidden || !tasks.Contains(task)) return;
            Touch();
            if (Collected) { RefreshLabels(); return; }
            var orb = task.Orb;
            var duration = origin.HasValue ? 370 : 230;
            CancelMotion(orb);
            orb.IsHitTestVisible = true;
            orb.Transition(.63, .62);
            ShowWindow(orb, origin.HasValue ? AlignedAt(orb, origin.Value) : task.Home, false);
            orb.RaiseWithoutFocus();
            Fly(orb, task.Home, () => orb.Transition(1, 1), portion => orb.Transition(.63 + .37 * Glass.Spring(portion),
                Math.Min(1, .62 + .83 * portion)), duration);
            if (mergeAfterEditor == task && tasks.Count > 1) mergeAt = clock.ElapsedMilliseconds + duration + 35;
            mergeAfterEditor = null;
            RefreshLabels();
        }

        private void ShowFolded(ClickTask task)
        {
            if (hidden || !tasks.Contains(task)) return;
            CancelMotion(task.Orb);
            task.Orb.Transition(1, 1);
            task.Orb.IsHitTestVisible = true;
            ShowWindow(task.Orb, task.Home);
        }

        private void ShowEditor(ClickTask task)
        {
            if (hidden || !tasks.Contains(task)) return;
            if (packing)
            {
                FinishPacking();
                ShowOrbs();
            }
            Touch();
            CloseStatus();
            var fullscreen = ForegroundFullscreen();
            if (panel != null && panel.Task == task)
            {
                panel.RaiseWithoutFocus();
                return;
            }
            var collected = Collected;
            var anchor = collected ? hub : task.Orb;
            var anchorPosition = anchor.IsVisible ? anchor.Position : collected ? HubPosition() : task.Home;
            var recognized = task.Target == null ? TargetBinding.Create(Windows.RecognizeGameWindow(Windows.ForegroundTarget())) : null;
            if (recognized != null) SetTarget(task, recognized);
            var previous = panel == null ? null : panel.Task;
            CloseEditor(false);
            if (previous != null && !collected) ShowFolded(previous);
            if (mergeAfterEditor != task) mergeAfterEditor = null;
            CancelMotion(task.Orb);
            task.Orb.Hide();
            if (!collected) hub.Hide();
            var editor = new TaskPanel(this, task);
            editor.ProtectFocus(true);
            editor.FullscreenOverlay = fullscreen != IntPtr.Zero;
            panel = editor;
            UpdateEditingPause();
            editor.Closed += (sender, args) =>
            {
                if (panel != editor) return;
                panel = null;
                CloseLocator();
                if (tracking == task) Arm(null);
                QueueEditingPause();
                if (!exiting) { ReturnToOrb(task); RememberCurrentLayout(); }
            };
            var nearby = PanelPosition(editor, anchor, anchorPosition);
            ShowWindow(editor, (RememberPanelPosition ? task.EditorHome : null) ?? nearby);
            Glass.Animate(editor.Card, true, editor.Position.X < anchorPosition.X);
            QueueOutlineLayout();
            if (!keyRegistered) editor.ShowMessage(shortcut.Text + " 正被占用", true);
            else if (recognized != null) editor.ShowMessage("已识别 " + recognized.Name);
            RefreshLabels();
        }

        public void EditorMoved(TaskPanel editor, ScreenPoint position)
        {
            if (editor != panel) return;
            editor.Task.EditorHome = position;
            QueueOutlineLayout();
            ScheduleLayoutSave();
        }

        private void CloseEditor(bool animate)
        {
            CloseLocator();
            if (panel == null) return;
            var old = panel; panel = null;
            QueueEditingPause();
            OverlayInput.End(old, true);
            if (settingsDialog != null && settingsDialog.Owner == old) settingsDialog.Close();
            if (tracking == old.Task) Arm(null);
            if (animate && old.IsVisible)
            {
                old.IsHitTestVisible = false;
                Glass.Animate(old.Card, false, old.Position.X < old.Task.Home.X, old.Close);
            }
            else old.Close();
        }

        public void ToggleLocator(ClickTask task)
        {
            if (panel == null || panel.Task != task) return;
            if (locator != null) { CloseLocator(); panel.LocatorShown(false); return; }
            var marker = MakeLocator(task);
            var origin = LocatorOrigin();
            marker.Reveal(0);
            ShowWindow(marker, marker.At(origin), false);
            Fly(marker, LocatorHome(task, marker), MoveLocator, marker.Reveal, 320);
        }

        private Locator MakeLocator(ClickTask task)
        {
            Arm(null);
            var marker = new Locator(Palette.Get(task.Color));
            locator = marker;
            locatorTask = task;
            if (panel != null && panel.Task == task) panel.LocatorShown(true);
            marker.Grabbed = () => { CancelMotion(marker); marker.Reveal(1); Touch(); };
            marker.Pointed = QueueLocatorPreview;
            marker.DragEnded = ClearLocatorPreview;
            marker.Dropped = point => BindLocator(task, marker, point);
            return marker;
        }

        public void DrawLocator(ClickTask task, ScreenPoint point)
        {
            if (!drawingLocator || locatorTask != task || locator == null)
            {
                CloseLocator();
                MakeLocator(task);
                drawingLocator = true;
                UpdateEditingPause();
                locator.Reveal(1);
                ShowWindow(locator, locator.At(point), false);
            }
            locator.Follow(point);
            QueueLocatorPreview(point);
        }

        public void FinishDrawLocator(ClickTask task, ScreenPoint point)
        {
            if (locatorTask != task || locator == null || !drawingLocator) return;
            drawingLocator = false;
            QueueEditingPause();
            var marker = locator;
            marker.Hide();
            BindLocator(task, marker, point);
        }

        public void CancelDrawLocator()
        {
            if (drawingLocator) CloseLocator();
        }

        private void BindLocator(ClickTask task, Locator marker, ScreenPoint point)
        {
            ClearLocatorPreview();
            var target = TargetBinding.Create(Windows.TargetAt(point), point);
            var hasEditor = panel != null && panel.Task == task;
            if (target != null)
            {
                SetTarget(task, target);
                UiSounds.Click();
                if (!hasEditor) ShowEditor(task);
                Report("已切换到 " + target.Name);
            }
            else if (hasEditor) Report("请把准星放在目标窗口的内容里。", true);
            if (!hasEditor) CloseLocator();
            else if (locator == marker)
            {
                MoveLocator();
                if (target != null) marker.Confirm();
            }
        }

        private void PointLocatorAt(IntPtr window)
        {
            if (pointedWindow == window) return;
            pointedWindow = window;
            QueueOutlineLayout();
        }

        private void QueueLocatorPreview(ScreenPoint point)
        {
            if (OutlineStrength == 0) return;
            pendingLocatorPoint = point;
            if (!locatorPreview.IsEnabled) locatorPreview.Start();
        }

        private void RefreshLocatorPreview()
        {
            var point = pendingLocatorPoint;
            if (!point.HasValue || locator == null || OutlineStrength == 0) { ClearLocatorPreview(); return; }
            var candidate = Windows.TargetAt(point.Value);
            if (candidate != pointedWindow && candidate != previewCandidate)
            {
                previewCandidate = candidate;
                return;
            }
            locatorPreview.Stop();
            if (candidate == pointedWindow) return;
            PointLocatorAt(candidate);
            if (PointerDrag.InProgress) UpdateOutlines();
        }

        private void ClearLocatorPreview()
        {
            locatorPreview.Stop();
            pendingLocatorPoint = null;
            previewCandidate = IntPtr.Zero;
            PointLocatorAt(IntPtr.Zero);
        }

        private ScreenPoint LocatorOrigin()
        {
            var button = panel.LocateButton;
            var origin = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            return new ScreenPoint((int)Math.Round(origin.X), (int)Math.Round(origin.Y));
        }

        private ScreenPoint LocatorHome(ClickTask task, Locator marker)
        {
            ScreenPoint point;
            var half = marker.HalfSize;
            Windows.Rectangle editor;
            Windows.GetWindowRect(panel.Hwnd, out editor);
            if (task.Target != null && task.Target.HasAnchor && task.Target.ScreenAnchor(out point) &&
                Glass.Screen(point).Bounds.Contains(point.X, point.Y))
                return marker.At(point);
            var origin = LocatorOrigin();
            var area = Glass.Screen(origin).WorkingArea;
            var gap = (int)Math.Ceiling(9 * marker.Scale);
            if (area.Right - editor.Right >= 2 * half + gap)
                return Glass.Clamp(new ScreenPoint(editor.Right + gap, origin.Y - half), 2 * half, 2 * half);
            var below = editor.Bottom + gap;
            if (below + 2 * half <= area.Bottom)
                return Glass.Clamp(new ScreenPoint(origin.X - half, below), 2 * half, 2 * half);
            var above = editor.Top - gap - 2 * half;
            if (above >= area.Top)
                return Glass.Clamp(new ScreenPoint(origin.X - half, above), 2 * half, 2 * half);
            return Glass.Clamp(new ScreenPoint(editor.Left - gap - 2 * half, origin.Y - half), 2 * half, 2 * half);
        }

        private void MoveLocator()
        {
            if (locator == null || panel == null || drawingLocator || locator.Dragging || motions.Any(motion => motion.Window == locator)) return;
            var goal = LocatorHome(panel.Task, locator);
            var old = locator.Position;
            if (!locator.IsVisible) ShowWindow(locator, goal, false);
            else if (goal.X != old.X || goal.Y != old.Y) locator.Move(goal, false);
        }

        private void CloseLocator()
        {
            if (locator == null) return;
            ClearLocatorPreview();
            var old = locator;
            locator = null; locatorTask = null; drawingLocator = false;
            CancelMotion(old); old.Close();
            QueueEditingPause();
        }

        public void StartOne(ClickTask task)
        {
            if (!task.Worker.State.HasSession) Start(new[] { task });
        }

        public void ToggleOne(ClickTask task)
        {
            if (!tasks.Contains(task)) return;
            UiSounds.Click();
            Touch();
            if (task.Worker.State.HasSession) task.Worker.TogglePause();
            else StartOne(task);
            RefreshLabels();
            QueueOutlineLayout();
        }

        public void StopOne(ClickTask task)
        {
            if (task.Worker.State.HasSession) UiSounds.Click();
            task.Worker.Stop();
            RefreshLabels();
            QueueOutlineLayout();
        }

        private void ToggleAll()
        {
            if (keySuspended) return;
            if (tasks.Count == 0) { if (hidden) ShowOrbs(); return; }
            UiSounds.Click();
            Touch();
            if (Running)
            {
                foreach (var task in tasks.Where(CountsAsGroupOutput)) task.Worker.Stop();
            }
            else
            {
                var idle = tasks.Where(item => !item.Worker.State.HasSession).ToArray();
                if (idle.Length > 0) Start(idle);
            }
            RefreshLabels();
        }

        private void StopAll()
        {
            foreach (var task in tasks) task.Worker.Stop();
        }

        private void Start(ClickTask[] batch)
        {
            ObserveForeground();
            var choices = new SavedJob[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                var task = batch[i];
                if (task.Target != null && task.Target.Resolve() == null)
                {
                    ShowOrbs();
                    ShowEditor(task);
                    ChooseWindow(task);
                    if (task.Target.Window == IntPtr.Zero) Report("原目标窗口已关闭，点要控制的窗口或拖动准星。", true);
                    return;
                }
                try { choices[i] = batch[i].Snapshot(true); }
                catch (FormatException error)
                {
                    ShowOrbs();
                    ShowEditor(batch[i]);
                    Report(error.Message, true);
                    return;
                }
            }
            Arm(null);
            var origin = Stopwatch.GetTimestamp();
            for (var i = 0; i < batch.Length; i++)
            {
                var job = choices[i];
                var task = batch[i];
                if (task.Worker.Start(task.Target, ActionSequence.Parse(job.Action), job.Interval, job.Delay,
                    job.Infinite ? 0 : job.Count, origin, job.Hold, job.ContinuousHold, job.IntervalVariation, job.RunSeconds, ShouldPauseForEditing(task)))
                {
                    task.RunningTarget = task.Target;
                    task.RunningAction = job.Action;
                }
            }
            QueueOutlineLayout();
            RefreshLabels();
        }

        private bool ShouldPauseForEditing(ClickTask task)
        {
            if (!PauseOthersWhileEditing || hidden || exiting || disposed) return false;
            if (drawingLocator && locatorTask != null) return locatorTask != task;
            if (panel != null) return panel.Task != task;
            return settingsDialog != null || profileBrowser != null;
        }

        private void UpdateEditingPause()
        {
            foreach (var task in tasks) task.Worker.SetEditingPause(ShouldPauseForEditing(task));
        }

        private void QueueEditingPause()
        {
            if (editingPauseQueued || exiting || disposed) return;
            editingPauseQueued = true;
            app.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                editingPauseQueued = false;
                if (!exiting && !disposed) UpdateEditingPause();
            }));
        }

        private void ToggleStatus(GlassOrb orb)
        {
            if (status != null) { CloseStatus(); Touch(); return; }
            if (orb == hub || Collected) RevealTasks();
            else OpenStatus(orb);
        }

        private void OpenStatus(GlassOrb orb, ScreenPoint? previousPosition = null)
        {
            if (hidden || packing || status != null || tasks.Count == 0) return;
            DismissEditor();
            Touch();
            statusAnchor = orb;
            var count = tasks.Count;
            var height = 100 + Math.Min(count, 5) * 51;
            status = new GlassWindow("连点器 · 输出进程", 325, height, true);
            var fullscreen = ForegroundFullscreen();
            status.ProtectFocus(true);
            status.FullscreenOverlay = fullscreen != IntPtr.Zero;
            var card = Glass.Card(new Thickness(13, 10, 13, 11));
            statusEdge = new EdgeLight(tasks.Select(item => Palette.Get(item.Color).Rim).ToArray());
            card.BorderBrush = statusEdge.Brush;
            card.BorderThickness = new Thickness(1.6);
            var contents = new Grid();
            contents.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contents.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contents.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), new GridLength(28), new GridLength(5),
                new GridLength(28), new GridLength(8), new GridLength(30), new GridLength(5), new GridLength(30));
            heading.Background = Brushes.Transparent;
            Glass.On(heading, Glass.Text("输出进程", 17, Glass.Ink, true), 0, 0);
            status.DragBy(heading, point => { statusHome = point; Touch(); ScheduleLayoutSave(); });
            status.LocationChanged += (sender, args) =>
            {
                if (ReferenceEquals(sender, status)) MoveUndo(status.Position);
            };
            var settingsButton = Glass.IconButton(Glass.GearGlyph, "设置");
            settingsButton.Click += (sender, args) => ShowSettings();
            Glass.On(heading, settingsButton, 0, 1);
            var configs = Glass.IconButton("M1 4 H5 L6.5 6 H13 V11.5 H1 Z M1 4 V2.5 H6 L7.5 4 H12 V6", "打开配置");
            configs.Click += (sender, args) => { if (status != null) OpenConfiguration(status); };
            Glass.On(heading, configs, 0, 3);
            var add = Glass.Button("＋"); add.FontSize = 16; add.Padding = new Thickness(0); Glass.Hint(add, "添加一枚连点");
            AutomationProperties.SetName(add, "添加一枚连点");
            add.IsEnabled = count < LayoutFile.MaxJobs;
            add.Click += (sender, args) => AddTask();
            Glass.On(heading, add, 0, 5);
            var dismiss = Glass.Button("−"); dismiss.Height = 28; dismiss.FontSize = 18; dismiss.Padding = new Thickness(0);
            Glass.Hint(dismiss, "收纳悬浮球");
            AutomationProperties.SetName(dismiss, "收纳悬浮球");
            dismiss.Click += (sender, args) => Collapse();
            Glass.On(heading, dismiss, 0, 7); Glass.On(contents, heading, 0, 0);
            var rows = new StackPanel { Margin = new Thickness(0, 5, 0, 2) };
            foreach (var task in tasks)
            {
                var palette = Palette.Get(task.Color);
                var row = Glass.Tile("#F3F4F6", 11, new Thickness(2, 1, 5, 1));
                row.Margin = new Thickness(0, 0, 0, 5); row.Height = 46;
                var rowLayout = TaskPanel.Columns(new GridLength(1, GridUnitType.Star), new GridLength(4), GridLength.Auto,
                    new GridLength(4), GridLength.Auto);
                var edit = Glass.Button("", Brushes.Transparent);
                Glass.Hint(edit, "展开这枚连点的设置"); edit.Padding = new Thickness(8, 2, 7, 2);
                edit.Height = Double.NaN;
                edit.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                edit.Click += (sender, args) => ShowEditor(task);
                var editLayout = TaskPanel.Columns(new GridLength(13), new GridLength(1, GridUnitType.Star), new GridLength(16));
                var dot = new Border { Background = palette.Marker, Width = 7, Height = 7,
                    CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
                Glass.On(editLayout, dot, 0, 0);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var summary = Glass.Text("", 11.5, Glass.Ink, true); summary.TextTrimming = TextTrimming.CharacterEllipsis;
                var target = Glass.Text(task.TargetName, 10, Glass.Muted); target.TextTrimming = TextTrimming.CharacterEllipsis;
                info.Children.Add(summary); info.Children.Add(target); Glass.On(editLayout, info, 0, 1);
                Glass.On(editLayout, Glass.Text("›", 19, Glass.Muted), 0, 2);
                edit.Content = editLayout;
                Glass.On(rowLayout, edit, 0, 0);
                var toggle = Glass.Button("开始", Glass.White);
                toggle.Height = 29; toggle.MinWidth = 53; toggle.FontSize = 11;
                toggle.Click += (sender, args) =>
                {
                    if (task.Target == null && !task.Worker.State.HasSession) { ShowEditor(task); if (task.Target == null) ChooseWindow(task); }
                    else ToggleOne(task);
                };
                processRows.Add(task, new ProcessRow { Summary = summary, Target = target, Edit = edit, Toggle = toggle });
                Glass.On(rowLayout, toggle, 0, 2);
                var remove = Glass.DeleteButton(Glass.White);
                remove.Click += (sender, args) => RemoveTask(task);
                Glass.On(rowLayout, remove, 0, 4);
                row.Child = rowLayout; rows.Children.Add(row);
            }
            Glass.On(contents, new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = rows }, 1, 0);
            statusStart = Glass.Button("开始全部", Glass.Primary, Brushes.White); statusStart.Height = 30;
            statusStart.Click += (sender, args) => ToggleAll();
            Glass.On(contents, statusStart, 2, 0);
            card.Child = contents; status.Content = card;
            var opened = status;
            opened.Closed += (sender, args) => { if (status == opened) { ClearStatus(); Touch(); } };
            opened.PreviewKeyDown += (sender, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true; CloseStatus(); Touch();
            };
            var preferred = previousPosition ?? (RememberPanelPosition ? statusHome : null);
            ShowWindow(status, preferred ?? PanelPosition(status, orb, orb == hub ? HubPosition() : orb.Position, true));
            Glass.Animate(card, true);
            RefreshLabels();
        }

        private void CloseStatus()
        {
            if (status == null) return;
            var old = status; ClearStatus(); old.Close();
        }

        private void ClearStatus()
        {
            status = null; statusEdge = null; statusStart = null; statusAnchor = null; processRows.Clear();
        }

        private void ShowOrbs()
        {
            hidden = false;
            hiddenEditor = null; hiddenEditorPosition = null; hiddenStatusAnchor = null;
            hiddenStatusPosition = null; hiddenWasExpanded = false;
            if (!packing && (tasks.Count == 0 || Collected))
            {
                foreach (var task in tasks) task.Orb.Hide();
                hub.SetProcesses(tasks.Select(task => task.Color).ToArray());
                hub.Transition(tasks.Count == 0 ? .82 : 1, 1);
                hub.IsHitTestVisible = true;
                ShowWindow(hub, HubPosition());
                RememberHubPosition();
            }
            else if (!packing)
            {
                hub.Hide();
                foreach (var task in tasks)
                    if (!task.Orb.IsVisible && (panel == null || panel.Task != task)) ShowFolded(task);
            }
            Touch();
            RefreshLabels();
            frames.Start();
            SetRendering(true);
            QueueOutlineLayout();
        }

        private void HideOrbs()
        {
            if (saveAt != 0) RememberCurrentLayout();
            hidden = true; expanded = false; packing = false; frames.Stop(); SetRendering(false);
            trayMenu.IsOpen = false;
            if (guide != null) guide.Close();
            if (settingsDialog != null) settingsDialog.Close();
            CloseEditor(false); CloseStatus(); ForgetRemoval();
            motions.Clear(); mergeAfterEditor = null; Touch();
            hub.Hide();
            foreach (var task in tasks) task.Orb.Hide();
            ClearOutlines();
            if (!exiting) UpdateEditingPause();
            tray.Text = Brand + " · 双击显示";
        }

        public void ShowSettings()
        {
            if (settingsDialog != null) { settingsDialog.RaiseWithoutFocus(); return; }
            var editor = panel;
            var task = editor != null ? editor.Task : tasks.Count == 1 ? tasks[0] : null;
            var owner = (GlassWindow)editor ?? status;
            var dialog = new SettingsDialog(this, owner, task);
            dialog.ProtectFocus(true);
            settingsDialog = dialog;
            dialog.Closed += (sender, args) => { settingsDialog = null; QueueEditingPause(); };
            UpdateEditingPause();
            var origin = owner == null ? Windows.Cursor() : owner.Position;
            var area = Glass.Screen(origin).WorkingArea;
            dialog.Height = Math.Min(dialog.Height, Math.Max(168, area.Height / dialog.Scale - 12));
            ShowWindow(dialog, new ScreenPoint(origin.X + 14, origin.Y + 20));
            Glass.Animate((FrameworkElement)dialog.Content, true);
        }

        private void ShowFirstGuide()
        {
            if (!preferences.TutorialSeen && !exiting && !disposed) ShowGuide(null, true);
        }

        public void ShowGuide(GlassWindow owner, bool first)
        {
            if (guide != null) { guide.RaiseWithoutFocus(); return; }
            var dialog = new GuideWindow(this, owner);
            guide = dialog;
            dialog.Closed += (sender, args) =>
            {
                guide = null;
                if (!first || exiting) return;
                preferences.TutorialSeen = true;
                var error = SaveSettings();
                if (error != null) Report(error, true);
            };
            var area = Glass.Screen(Windows.Cursor()).WorkingArea;
            var origin = owner == null ? new ScreenPoint(area.Left + (area.Width - 362) / 2, area.Top + (area.Height - 321) / 2) :
                new ScreenPoint(owner.Position.X + 16, owner.Position.Y + 25);
            dialog.ProtectFocus(true);
            ShowWindow(dialog, origin);
        }

        private AppSettings ReadSettings()
        {
            try { return LayoutFile.ReadSettings(settingsPath); }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
                return new AppSettings();
            }
        }

        private string SaveSettings()
        {
            settingsSaveTimer.Stop();
            try
            {
                Directory.CreateDirectory(dataRoot);
                LayoutFile.Write(settingsPath, preferences);
                return null;
            }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
                return "设置保存失败。" + error.Message;
            }
        }

        public string ChangePauseOthers(bool enabled)
        {
            preferences.PauseOthersWhileEditing = enabled;
            UpdateEditingPause();
            return SaveSettings();
        }

        public string ChangeInterfaceSounds(bool enabled)
        {
            preferences.InterfaceSounds = enabled;
            UiSounds.SetEnabled(enabled);
            if (enabled) UiSounds.Click();
            return SaveSettings();
        }

        public void ChangeOutlineStrength(int strength)
        {
            if (OutlineStrength == strength) return;
            preferences.OutlineStrength = strength;
            if (strength == 0) { ClearLocatorPreview(); ClearOutlines(); }
            else QueueOutlineLayout();
            if (!PointerDrag.InProgress) RefreshLabels();
            settingsSaveTimer.Stop();
            settingsSaveTimer.Start();
        }

        public string ChangeRememberPanelPosition(bool enabled)
        {
            preferences.RememberPanelPosition = enabled;
            return SaveSettings();
        }

        public void SaveConfiguration(SettingsDialog owner)
        {
            OverlayInput.End(owner, true);
            try
            {
                var layout = CaptureLayout();
                var name = ChooseProfileName(owner);
                if (name == null) return;
                Directory.CreateDirectory(profilesRoot);
                LayoutFile.Write(ProfilePath(name), layout);
                LayoutFile.Write(layoutPath, layout);
                profileName = name;
                owner.Notice("✓ 已保存「" + name + "」");
                UiSounds.Click();
            }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
                owner.Notice(error.Message, true);
            }
        }

        public void OpenConfiguration(GlassWindow owner)
        {
            OverlayInput.End(owner, true);
            if (profileBrowser != null) { profileBrowser.RaiseWithoutFocus(); return; }
            try
            {
                var picker = new ProfileDialog(owner, false, ProfileNames(), profileName, DeleteProfile, () => !HasRunningJobs);
                profileBrowser = picker;
                picker.Closed += (sender, args) => { profileBrowser = null; QueueEditingPause(); };
                picker.ShowWithoutFocus(path => LoadConfiguration(owner, path));
                UpdateEditingPause();
            }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
                ConfigurationNotice(owner, "打开配置失败。" + error.Message);
            }
        }

        private void LoadConfiguration(GlassWindow owner, string path)
        {
            try
            {
                if (HasRunningJobs) { ConfigurationNotice(owner, "停止输出后可以打开配置。"); return; }
                if (!Path.IsPathRooted(path)) path = ProfilePath(path);
                var layout = LayoutFile.Read(path);
                Directory.CreateDirectory(dataRoot);
                LayoutFile.Write(layoutPath, layout);
                var fileName = Path.GetFileName(path);
                profileName = fileName.EndsWith(ProfileSuffix, StringComparison.OrdinalIgnoreCase) ?
                    fileName.Substring(0, fileName.Length - ProfileSuffix.Length) : Path.GetFileNameWithoutExtension(fileName);
                var keyLoaded = LoadLayout(layout);
                if (settingsDialog == owner) owner.Close();
                if (!keyLoaded) tray.ShowBalloonTip(3500, "配置已载入", "配置内的快捷键不可用，仍可从悬浮球开始。", Forms.ToolTipIcon.Warning);
            }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
                ConfigurationNotice(owner, "打开配置失败。" + error.Message);
            }
        }

        private void ConfigurationNotice(GlassWindow owner, string text)
        {
            var settings = owner as SettingsDialog;
            if (settings != null && settings.IsVisible) settings.Notice(text, true);
            else Report(text, true);
        }

        private string[] ProfileNames()
        {
            return Directory.Exists(profilesRoot) ? Directory.GetFiles(profilesRoot, "*" + ProfileSuffix)
                .Select(Path.GetFileName).Select(file => file.Substring(0, file.Length - ProfileSuffix.Length)).OrderBy(file => file).ToArray() : new string[0];
        }

        private string ChooseProfileName(SettingsDialog owner)
        {
            SuspendShortcut();
            try
            {
                var picker = new ProfileDialog(owner, true, ProfileNames(), profileName);
                using (new ForegroundReturn()) return picker.ShowDialog() == true ? picker.Value : null;
            }
            finally { ResumeShortcut(); }
        }

        private string ProfilePath(string name)
        {
            if (!ProfileDialog.ValidName(name)) throw new FormatException("配置名称无效。");
            return Path.Combine(profilesRoot, name + ProfileSuffix);
        }

        private void DeleteProfile(string name)
        {
            File.Delete(ProfilePath(name));
            if (String.Equals(profileName, name, StringComparison.OrdinalIgnoreCase)) profileName = null;
        }

        private bool LoadLayout(SavedLayout layout)
        {
            var keyLoaded = UseShortcut(Shortcut.Parse(layout.Shortcut));
            CloseEditor(false); CloseStatus(); ForgetRemoval(); ClearOutlines();
            Arm(null); motions.Clear(); expanded = false; packing = false; mergeAfterEditor = null;
            foreach (var task in tasks) task.Orb.Close();
            tasks.Clear(); hub.Hide();
            if (layout.HubLeft.HasValue && layout.HubTop.HasValue)
                hubHome = ClampAt(new ScreenPoint(layout.HubLeft.Value, layout.HubTop.Value), 160, 118, hub.Scale);
            statusHome = layout.OutputLeft.HasValue && layout.OutputTop.HasValue
                ? (ScreenPoint?)new ScreenPoint(layout.OutputLeft.Value, layout.OutputTop.Value) : null;
            foreach (var saved in layout.Jobs) MakeTask(saved, tasks.Count);
            ShowOrbs();
            return keyLoaded;
        }

        private SavedLayout CaptureLayout()
        {
            return new SavedLayout { Version = 1, Shortcut = shortcut.Text, HubLeft = hubHome.X, HubTop = hubHome.Y,
                OutputLeft = statusHome.HasValue ? (int?)statusHome.Value.X : null,
                OutputTop = statusHome.HasValue ? (int?)statusHome.Value.Y : null,
                Jobs = tasks.Select(task => task.Snapshot(false)).ToList() };
        }

        private void RememberCurrentLayout()
        {
            saveAt = 0;
            if (!rememberChanges) return;
            try
            {
                var layout = CaptureLayout();
                Directory.CreateDirectory(dataRoot);
                LayoutFile.Write(layoutPath, layout);
            }
            catch (Exception error)
            {
                if (!IsConfigError(error)) throw;
            }
        }

        public void ScheduleLayoutSave()
        {
            if (rememberChanges) saveAt = clock.ElapsedMilliseconds + 450;
        }

        internal static bool IsConfigError(Exception error)
        {
            return error is FormatException || error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException || error is System.Security.SecurityException;
        }

        private void Report(string message, bool important = false)
        {
            if (panel == null) { if (important) tray.ShowBalloonTip(3500, Brand, message, Forms.ToolTipIcon.Warning); return; }
            panel.Refresh();
            panel.ShowMessage(message, important);
        }

        private void Tick()
        {
            // While a pointer is captured, preview changes are handled by the locator timer.
            if (PointerDrag.InProgress) return;
            var now = clock.ElapsedMilliseconds;
            if (saveAt != 0 && now >= saveAt) RememberCurrentLayout();
            if (undoToast != null && now >= undoUntil && !undoToast.IsMouseOver) ForgetRemoval();
            retiring.RemoveAll(worker => !worker.State.HasSession);
            if (now - lastLabels >= 125)
            {
                lastLabels = now;
                if (rememberChanges && !keyRegistered && !keySuspended && now >= shortcutRetry)
                {
                    shortcutRetry = now + 1500;
                    keyRegistered = RegisterShortcut(shortcut);
                    if (keyRegistered && panel != null) panel.ShortcutRestored();
                }
                if (foregroundHook == IntPtr.Zero && !keySuspended) ObserveForeground();
                if (now - lastRecovery >= 950)
                {
                    lastRecovery = now;
                    foreach (var task in tasks)
                        if (!task.Worker.State.HasSession && task.Target != null && task.Target.Window == IntPtr.Zero) task.Target.Resolve();
                }
                if (panel != null || status != null)
                {
                    var fullscreen = ForegroundFullscreen() != IntPtr.Zero;
                    if (panel != null) panel.FullscreenOverlay = fullscreen;
                    if (status != null) status.FullscreenOverlay = fullscreen;
                }
                RefreshLabels();
            }
            if (outlineDirty || now - lastOutlineLayout >= 140) RefreshOutlineLayout();
            if (packing || !expanded || tasks.Count < 2 || panel != null) return;
            if (mergeAt != 0)
            {
                if (now >= mergeAt) Collapse();
                return;
            }
            if (keySuspended || settingsDialog != null || profileBrowser != null || tasks.Any(task => task.Orb.IsMouseOver || task.Orb.IsMouseCaptureWithin ||
                task.Orb.ContextMenu.IsOpen) || (status != null && (status.IsMouseOver || status.IsMouseCaptureWithin)))
                lastInteraction = now;
            if (now - lastInteraction >= (status == null ? OrbsOnlyIdle : ProcessPanelIdle)) Collapse();
        }

        private void Touch()
        {
            lastInteraction = clock.ElapsedMilliseconds;
            mergeAt = 0;
        }

        private void SetRendering(bool enabled)
        {
            if (rendering == enabled) return;
            rendering = enabled;
            lastFrame = TimeSpan.MinValue;
            if (enabled) CompositionTarget.Rendering += RenderFrame;
            else CompositionTarget.Rendering -= RenderFrame;
        }

        private void RenderFrame(object sender, EventArgs args)
        {
            var frame = ((RenderingEventArgs)args).RenderingTime;
            if (frame == lastFrame) return;
            lastFrame = frame;
            var now = clock.Elapsed.TotalMilliseconds;
            movedThisFrame.Clear();
            finishedThisFrame.Clear();
            for (var i = motions.Count - 1; i >= 0; i--)
            {
                var motion = motions[i];
                var portion = Math.Min(1, (now - motion.Started) / motion.Duration);
                var move = portion * portion * (3 - 2 * portion);
                var point = new ScreenPoint(motion.From.X + (int)Math.Round((motion.To.X - motion.From.X) * move),
                    motion.From.Y + (int)Math.Round((motion.To.Y - motion.From.Y) * move));
                if (point.X != motion.Last.X || point.Y != motion.Last.Y)
                {
                    motion.Last = point;
                    movedThisFrame.Add(motion);
                }
                if (motion.Visual != null) motion.Visual(portion);
                if (portion < 1) continue;
                motions.RemoveAt(i);
                if (motion.Done != null) finishedThisFrame.Add(motion.Done);
            }
            MoveAnimationWindows();
            foreach (var finished in finishedThisFrame) finished();
            if (motions.Count == 0) SetRendering(false);
        }

        private void MoveAnimationWindows()
        {
            if (movedThisFrame.Count > 1)
            {
                var batch = Windows.BeginDeferWindowPos(movedThisFrame.Count);
                foreach (var motion in movedThisFrame)
                {
                    if (batch == IntPtr.Zero) break;
                    batch = Windows.DeferWindowPos(batch, motion.Window.Hwnd, IntPtr.Zero, motion.Last.X,
                        motion.Last.Y, 0, 0, Windows.KeepOrder | 1);
                }
                if (batch != IntPtr.Zero && Windows.EndDeferWindowPos(batch)) return;
            }
            foreach (var motion in movedThisFrame) motion.Window.Move(motion.Last, false);
        }

        private void QueueOutlineLayout()
        {
            outlineDirty = true;
            if (hidden || disposed || outlineQueued || PointerDrag.InProgress) return;
            outlineQueued = true;
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                outlineQueued = false;
                if (outlineDirty && !hidden && !disposed) RefreshOutlineLayout();
            }));
        }

        private void RefreshOutlineLayout()
        {
            outlineDirty = false;
            lastOutlineLayout = clock.ElapsedMilliseconds;
            MoveLocator();
            UpdateOutlines();
        }

        private void RefreshLabels()
        {
            var aggregate = RunPhase.Idle;
            var activeColors = new List<Color>();
            long total = 0;
            for (var i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i]; var state = task.Worker.State;
                total += state.Count;
                if (PhasePriority(state.Phase) > PhasePriority(aggregate)) aggregate = state.Phase;
                var keys = state.Phase == RunPhase.Active && state.Detail != null ? state.Detail :
                    ShortAction(state.HasSession ? task.RunningAction ?? task.Action : task.Action);
                var count = state.Holding ? "按住中" : state.Count + " 次";
                var target = task.OutputTarget;
                var destination = target == null ? "未绑定窗口" : target.Name;
                var orbitCount = state.Holding ? count : CompactCount(state.Count);
                var detail = destination + " · " + count + (state.Phase == RunPhase.Paused ? " · " + PhaseText(state) : "");
                var lit = task.OutputActive;
                if (lit) activeColors.Add(Palette.Get(task.Color).Rim);
                if (task.Orb.IsVisible)
                {
                    task.Orb.Display(keys, " · " + orbitCount, destination, state.Phase, detail);
                    task.Orb.ShowOutputLight(lit, OutlineStrength);
                }
                if (hub.IsVisible) hub.DisplayProcess(i, keys, " · " + orbitCount, destination, state.Phase, detail,
                    panel != null && panel.Task == task);
                ProcessRow row;
                if (!processRows.TryGetValue(task, out row)) continue;
                var summary = keys + " · " + count + (state.Holding ? "" : " · " + PhaseText(state));
                if (row.Summary.Text != summary) row.Summary.Text = summary;
                if (row.Target.Text != destination)
                {
                    row.Target.Text = destination;
                    Glass.Hint(row.Target, destination);
                }
                var accessibleName = "编辑 " + keys + " · " + destination;
                if (AutomationProperties.GetName(row.Edit) != accessibleName)
                    AutomationProperties.SetName(row.Edit, accessibleName);
                row.Summary.Foreground = state.Phase == RunPhase.Active || state.Phase == RunPhase.Complete ? Glass.Green :
                    state.Phase == RunPhase.Waiting || state.Phase == RunPhase.Delay ? Glass.Gold :
                    state.Phase == RunPhase.Paused ? Glass.Muted : Glass.Ink;
                var control = state.PausedForEditing ? "保持暂停" : state.Phase == RunPhase.Paused ? "继续" :
                    state.Running ? "暂停" : task.Target == null ? "选窗口" : "开始";
                if ((string)row.Toggle.Content != control) row.Toggle.Content = control;
            }
            if (hub.IsVisible || statusEdge != null)
            {
                var colors = activeColors.ToArray();
                if (hub.IsVisible)
                {
                    hub.Display("", "", tasks.Count == 0 ? "添加一枚连点" : "输出进程", aggregate);
                    hub.ShowOutputLight(colors.Length > 0, OutlineStrength, colors);
                }
                if (statusEdge != null)
                {
                    if (colors.Length > 0) statusEdge.SetColors(colors);
                    statusEdge.SetLit(colors.Length > 0 && OutlineStrength > 0, OutlineStrength);
                }
            }
            if (statusStart != null)
            {
                statusStart.IsEnabled = Running || tasks.Any(task => !task.Worker.State.HasSession);
                var label = Running ? "停止全部" : statusStart.IsEnabled ? "开始全部" : "全部已暂停";
                if ((string)statusStart.Content != label) statusStart.Content = label;
            }
            if (panel != null) panel.Refresh();
            if (clock.ElapsedMilliseconds - trayRefresh > 500)
            {
                trayRefresh = clock.ElapsedMilliseconds;
                tray.Text = Running ? Brand + " · 已发送 " + total + " 次" : Brand + " · 连点器";
            }
        }

        private static string ShortAction(string action)
        {
            var first = action.Split('；', ';')[0].Trim();
            if (first.Length > 16) first = first.Substring(0, 15) + "…";
            return first.Length == 0 ? "未设置动作" : first;
        }

        private static string CompactCount(long count)
        {
            if (count < 1000) return count.ToString(CultureInfo.InvariantCulture) + " 次";
            if (count >= 1000000000000L) return count.ToString("0.#e0", CultureInfo.InvariantCulture);
            var divisor = count >= 100000000L ? 100000000L : count >= 10000 ? 10000L : 1000L;
            var unit = divisor == 100000000L ? "亿" : divisor == 10000 ? "万" : "千";
            var value = (decimal)count / divisor;
            return value.ToString(value < 100 ? "0.#" : "0", CultureInfo.InvariantCulture) + unit;
        }

        private static int PhasePriority(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.Waiting: return 5;
                case RunPhase.Delay: return 4;
                case RunPhase.Active: return 3;
                case RunPhase.Paused: return 2;
                case RunPhase.Complete: return 2;
                case RunPhase.Stopped: return 1;
                default: return 0;
            }
        }

        private static string PhaseText(RunState state)
        {
            switch (state.Phase)
            {
                case RunPhase.Active: return "运行中";
                case RunPhase.Delay: return "等待启动";
                case RunPhase.Waiting: return state.Detail ?? "等待恢复";
                case RunPhase.Paused: return state.PausedForEditing ? "调试暂停" : "已暂停";
                case RunPhase.Complete: return state.Detail ?? "已完成";
                case RunPhase.Stopped: return "已停止";
                default: return "待启动";
            }
        }

        private void UpdateOutlines()
        {
            if (OutlineStrength == 0) return;
            var groups = new Dictionary<IntPtr, List<int>>();
            foreach (var task in tasks)
            {
                var target = task.OutputTarget;
                if (task.OutputActive) AddOutlineColor(groups, target.Window, task.Color);
                if (panel != null && panel.Task == task && task.Target != null &&
                    (!task.Worker.State.HasSession || task.Target != target))
                    AddOutlineColor(groups, task.Target.Window, task.Color);
            }
            if (locatorTask != null) AddOutlineColor(groups, pointedWindow, locatorTask.Color);
            foreach (var handle in outlines.Keys.Where(handle => !groups.ContainsKey(handle)).ToArray())
            {
                outlines[handle].Dispose(); outlines.Remove(handle);
            }
            foreach (var entry in groups)
            {
                WindowOutline outline;
                if (!outlines.TryGetValue(entry.Key, out outline)) outlines.Add(entry.Key, outline = new WindowOutline(entry.Key));
                outline.Layout(entry.Value.ToArray(), OutlineStrength);
            }
        }

        private static void AddOutlineColor(Dictionary<IntPtr, List<int>> groups, IntPtr handle, int color)
        {
            if (handle == IntPtr.Zero) return;
            List<int> colors;
            if (!groups.TryGetValue(handle, out colors)) groups.Add(handle, colors = new List<int>());
            if (!colors.Contains(color)) colors.Add(color);
        }

        private void ClearOutlines()
        {
            foreach (var outline in outlines.Values) outline.Dispose();
            outlines.Clear();
        }

        private static void ShowWindow(GlassWindow window, ScreenPoint position, bool clamp = true)
        {
            window.Move(position, clamp);
            if (!window.IsVisible) window.Show();
            window.Move(position, clamp);
        }

        private ScreenPoint HubPosition()
        {
            var offset = HubSlotOffset();
            return AlignedAt(hub, new ScreenPoint(hubHome.X + offset.X, hubHome.Y + offset.Y));
        }

        private void RememberHubPosition()
        {
            var center = CenterOf(hub, hub.Position);
            var offset = HubSlotOffset();
            hubHome = new ScreenPoint(center.X - offset.X, center.Y - offset.Y);
        }

        private ScreenPoint HubSlotOffset()
        {
            var scale = hub.Scale;
            return new ScreenPoint((int)Math.Round(80 * scale), (int)Math.Round(37 * scale));
        }

        private static ScreenPoint ClampAt(ScreenPoint point, double width, double height, double scale)
        {
            return Glass.Clamp(point, (int)Math.Round(width * scale), (int)Math.Round(height * scale));
        }

        private static ScreenPoint CenterOf(GlassOrb orb, ScreenPoint home)
        {
            var offset = orb.CenterOffset;
            return new ScreenPoint(home.X + offset.X, home.Y + offset.Y);
        }

        private static ScreenPoint FirstHome(GlassOrb orb, ScreenPoint pointer)
        {
            var screen = Glass.Screen(pointer).Bounds;
            return AlignedAt(orb, new ScreenPoint(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2));
        }

        private static ScreenPoint AlignedAt(GlassOrb orb, ScreenPoint center)
        {
            var offset = orb.CenterOffset;
            return new ScreenPoint(center.X - offset.X, center.Y - offset.Y);
        }

        private ScreenPoint PanelPosition(GlassWindow window, GlassOrb orb, ScreenPoint home, bool group = false)
        {
            var area = Glass.Screen(home).WorkingArea;
            var scale = orb.Scale;
            var width = (int)Math.Round(window.Width * scale);
            var height = (int)Math.Round(window.Height * scale);
            var center = CenterOf(orb, home);
            var gap = (int)((orb == hub && tasks.Count > 0 ? orb.Width / 2 + 5 : 35) * scale);
            var left = new ScreenPoint(center.X - gap - width, center.Y - gap);
            var right = new ScreenPoint(center.X + gap, center.Y - gap);
            var below = new ScreenPoint(center.X - width / 2, center.Y + (int)(Math.Max(82, gap / scale) * scale));
            var above = new ScreenPoint(center.X - width / 2, center.Y - height - gap);
            var options = group ? new[] { below, above, left, right } : new[] { left, right, below, above };
            var best = home;
            var bestCost = Double.MaxValue;
            foreach (var option in options)
            {
                var x = Math.Max(area.Left, Math.Min(option.X, area.Right - width));
                var y = Math.Max(area.Top, Math.Min(option.Y, area.Bottom - height));
                double cost = Math.Abs(x - option.X) + Math.Abs(y - option.Y);
                var placed = new Drawing.Rectangle(x, y, width, height);
                foreach (var task in tasks)
                {
                    if (!group && task.Orb == orb) continue;
                    var at = CenterOf(task.Orb, task.Home);
                    var overlap = Drawing.Rectangle.Intersect(placed, new Drawing.Rectangle(at.X - (int)(43 * scale),
                        task.Home.Y, (int)(86 * scale), (int)(118 * scale)));
                    cost += overlap.Width * overlap.Height / 8.0;
                }
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = new ScreenPoint(x, y);
            }
            return best;
        }

        private void RepositionPanels(GlassOrb orb)
        {
            if (status != null && statusAnchor == orb && !(RememberPanelPosition && statusHome.HasValue))
                status.Move(PanelPosition(status, orb, orb.Position, true));
        }

        private void Fly(GlassWindow window, ScreenPoint destination, Action done = null, Action<double> visual = null, int duration = 250)
        {
            CancelMotion(window);
            var from = window.Position;
            motions.Add(new Motion { Window = window, From = from, Last = from, To = destination, Started = clock.Elapsed.TotalMilliseconds,
                Duration = duration, Done = done, Visual = visual });
            SetRendering(true);
        }

        private void CancelMotion(GlassWindow window) { motions.RemoveAll(motion => motion.Window == window); }

        private void Quit()
        {
            exiting = true;
            if (settingsSaveTimer.IsEnabled) SaveSettings();
            RememberCurrentLayout();
            HideOrbs();
            StopAll();
            app.Shutdown();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAll(); frames.Stop(); locatorPreview.Stop(); SetRendering(false); ReleaseShortcut();
            if (settingsSaveTimer.IsEnabled) SaveSettings();
            if (emergencyRegistered) UnregisterHotKey(messages.Handle, EmergencyKeyId);
            if (guide != null) guide.Close();
            if (settingsDialog != null) settingsDialog.Close();
            var wait = Stopwatch.StartNew();
            foreach (var task in tasks) task.Worker.WaitForStop(Math.Max(0, 350 - (int)wait.ElapsedMilliseconds));
            foreach (var worker in retiring) worker.WaitForStop(Math.Max(0, 350 - (int)wait.ElapsedMilliseconds));
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            if (locationHook != IntPtr.Zero) UnhookWinEvent(locationHook);
            Arm(null);
            trayMenu.IsOpen = false;
            tray.Visible = false; tray.Dispose(); icon.Dispose(); messages.Dispose();
        }

        private sealed class Motion
        {
            public GlassWindow Window;
            public ScreenPoint From, To, Last;
            public double Started, Duration;
            public Action Done;
            public Action<double> Visual;
        }

        private sealed class ProcessRow
        {
            public TextBlock Summary, Target;
            public Button Edit, Toggle;
        }

        private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
