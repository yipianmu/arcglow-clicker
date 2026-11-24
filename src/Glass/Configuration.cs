using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GlassClicker
{
    internal sealed class SavedLayout
    {
        public int Version { get; set; }
        public string Shortcut { get; set; }
        public int? HubLeft { get; set; }
        public int? HubTop { get; set; }
        public int? OutputLeft { get; set; }
        public int? OutputTop { get; set; }
        public string ProfileName { get; set; }
        public SavedView View { get; set; }
        public List<SavedJob> Jobs { get; set; }
    }

    internal sealed class SavedView
    {
        public bool Expanded { get; set; }
        public int? EditorColor { get; set; }
        public bool OutputOpen { get; set; }
        public int? OutputAnchorColor { get; set; }
        public int? PanelLeft { get; set; }
        public int? PanelTop { get; set; }
    }

    internal sealed class SavedJob
    {
        public string Action { get; set; }
        public decimal Interval { get; set; }
        public int Delay { get; set; }
        public int Hold { get; set; }
        public bool ContinuousHold { get; set; }
        public long Count { get; set; }
        public bool Infinite { get; set; }
        public int Color { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int? EditorLeft { get; set; }
        public int? EditorTop { get; set; }
        public int IntervalVariation { get; set; }
        public int RunSeconds { get; set; }
        public SavedTarget Target { get; set; }
    }

    internal sealed class AppSettings
    {
        public const string DefaultShortcut = "F10";
        public const string DefaultEmergencyKey = "Ctrl+Shift+F10";
        public const int DefaultInterfaceVolume = 32;
        public bool TutorialSeen { get; set; }
        public string EmergencyKey { get; set; }
        public bool PauseOthersWhileEditing { get; set; }
        public bool RememberPanelPosition { get; set; }
        public bool RestoreLayoutOnStart { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool ShowClickCounts { get; set; }
        public bool IntervalAsRate { get; set; }
        public bool InterfaceSounds { get; set; }
        public int? InterfaceSoundVolume { get; set; }
        public int OutlineStrength { get; set; }

        public AppSettings()
        {
            EmergencyKey = DefaultEmergencyKey;
            PauseOthersWhileEditing = true;
            RestoreLayoutOnStart = true;
            AlwaysOnTop = true;
            ShowClickCounts = true;
            OutlineStrength = 100;
        }
    }

    internal static class RepeatTiming
    {
        public const int Precision = 3;
        public const decimal MinimumInterval = 10, MaximumInterval = 60000;
        public const decimal MinimumRate = 0.017m, MaximumRate = 100m;

        public static string Format(decimal value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        public static string Store(decimal milliseconds) { return milliseconds.ToString("0.######", CultureInfo.InvariantCulture); }
    }

    internal static class LayoutFile
    {
        public const int MaxJobs = 12;
        public const int MaxVariation = 60000, MaxRunSeconds = 86400;
        public static SavedLayout Read(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException("配置文件过大。");
            var layout = new JavaScriptSerializer().Deserialize<SavedLayout>(File.ReadAllText(path));
            if (layout == null || layout.Version != 1 || layout.Jobs == null || layout.Jobs.Count > MaxJobs)
                throw new FormatException("请选择弧光连点器保存的配置。");
            Shortcut.Parse(layout.Shortcut);
            var colors = new HashSet<int>();
            foreach (var job in layout.Jobs)
            {
                if (job == null || job.Interval < RepeatTiming.MinimumInterval || job.Interval > RepeatTiming.MaximumInterval ||
                    job.Delay < 0 || job.Delay > 600000 ||
                    job.Hold < 0 || job.Hold > 60000 || job.IntervalVariation < 0 || job.IntervalVariation > MaxVariation ||
                    job.RunSeconds < 0 || job.RunSeconds > MaxRunSeconds ||
                    job.Count < 1 || job.Count > 9999999 || job.Color < 0 || job.Color >= MaxJobs || !colors.Add(job.Color))
                    throw new FormatException("配置中的时间、次数或任务颜色有误。");
                var action = ActionSequence.Parse(job.Action);
                if (action.Steps.Length != 1) job.ContinuousHold = false;
            }
            return layout;
        }

        public static AppSettings ReadSettings(string path)
        {
            if (!File.Exists(path)) return new AppSettings();
            if (new FileInfo(path).Length > 8192) throw new FormatException("设置文件过大。");
            var settings = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(path));
            if (settings == null) return new AppSettings();
            settings.EmergencyKey = Shortcut.Parse(settings.EmergencyKey).Text;
            settings.OutlineStrength = Math.Max(0, Math.Min(100, settings.OutlineStrength));
            // Earlier releases enabled sounds automatically and did not store a volume.
            if (!settings.InterfaceSoundVolume.HasValue) settings.InterfaceSounds = false;
            settings.InterfaceSoundVolume = Math.Max(0, Math.Min(100,
                settings.InterfaceSoundVolume ?? AppSettings.DefaultInterfaceVolume));
            return settings;
        }

        public static void Write(string path, object value)
        {
            WriteAtomically(path, temporary =>
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false)));
        }

        public static void WriteAtomically(string path, Action<string> write)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                write(temporary);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed class Shortcut
    {
        public readonly string Text;
        public readonly int Key;
        public readonly uint Modifiers;
        public Shortcut(string text, int key, uint modifiers) { Text = text; Key = key; Modifiers = modifiers; }

        public static Shortcut Parse(string text)
        {
            var keys = ActionSequence.Parse(text);
            if (keys.Steps.Length != 1) throw new FormatException("快捷键只需要一个组合键。");
            int trigger = 0;
            uint mods = 0;
            foreach (var key in keys.Steps[0].Inputs)
            {
                switch (key)
                {
                    case 0x10: mods |= 4; break;
                    case 0x11: mods |= 2; break;
                    case 0x12: mods |= 1; break;
                    case 0x5B: mods |= 8; break;
                    default:
                        if (key < 1 || trigger != 0) throw new FormatException("快捷键需要一个键盘主键。");
                        trigger = key;
                        break;
                }
            }
            if (trigger == 0) throw new FormatException("快捷键需要一个键盘主键。");
            return new Shortcut(keys.Text, trigger, mods);
        }
    }
}
