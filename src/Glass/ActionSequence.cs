using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace GlassClicker
{
    internal sealed class ActionSequence
    {
        public sealed class Step
        {
            public readonly int Delay;
            public readonly int[] Inputs;
            public Step(int delay, int[] inputs) { Delay = delay; Inputs = inputs; }
            public string Chord { get { return String.Join("+", Inputs.Select(Name)); } }
        }

        private static readonly Dictionary<string, int> Names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            {"Ctrl", 0x11}, {"Control", 0x11}, {"Shift", 0x10}, {"Alt", 0x12}, {"Win", 0x5B},
            {"左键", -1}, {"鼠标左键", -1}, {"LButton", -1}, {"右键", -2}, {"鼠标右键", -2}, {"RButton", -2},
            {"中键", -3}, {"鼠标中键", -3}, {"MButton", -3}, {"空格", 0x20}, {"回车", 0x0D}, {"Enter", 0x0D},
            {"退格", 8}, {"Esc", 0x1B}, {"Del", 0x2E}, {"加号", 0xBB}, {"减号", 0xBD},
            {"↑", 0x26}, {"↓", 0x28}, {"←", 0x25}, {"→", 0x27}
        };

        public readonly Step[] Steps;
        public ActionSequence(Step[] steps) { Steps = steps; }
        public string Text
        {
            get { return String.Join("；", Steps.Select(step => (step.Delay == 0 ? "" : step.Delay + "ms ") + step.Chord)); }
        }

        public static ActionSequence Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text) || text.Length > 4096) throw new FormatException("输入一个连点键，或点击右侧录制。");
            var parts = text.Split(';', '；');
            if (parts.Length > 64) throw new FormatException("一次录制最多保留 64 个动作，可拆成两组。");
            var result = new List<Step>();
            foreach (var part in parts)
            {
                var content = part.Trim();
                var delay = 0;
                var prefix = Regex.Match(content, @"^(\d+)\s*ms\s+", RegexOptions.IgnoreCase);
                if (prefix.Success)
                {
                    if (!Int32.TryParse(prefix.Groups[1].Value, out delay) || delay > 60000)
                        throw new FormatException("录制动作之间的等待需在 60 秒以内。");
                    content = content.Substring(prefix.Length);
                }
                var keys = content.Split('+').Select(Lookup).Distinct().OrderBy(ModifierOrder).ToArray();
                if (keys.Length > 8 || keys.Count(key => key < 0) > 1)
                    throw new FormatException("组合动作最多 8 个键，并可包含一个鼠标键。");
                result.Add(new Step(result.Count == 0 ? 0 : delay, keys));
            }
            return new ActionSequence(result.ToArray());
        }

        private static int Lookup(string input)
        {
            var token = input.Trim();
            int vk;
            if (Names.TryGetValue(token, out vk)) return vk;
            if (token.Length == 1)
            {
                var letter = Char.ToUpperInvariant(token[0]);
                if ((letter >= 'A' && letter <= 'Z') || (letter >= '0' && letter <= '9')) return letter;
            }
            Key key;
            if (token.Length > 0 && !Char.IsDigit(token[0]) && Enum.TryParse(token, true, out key) &&
                Enum.IsDefined(typeof(Key), key) && (vk = KeyInterop.VirtualKeyFromKey(key)) > 0)
                return Normalize(vk);
            throw new FormatException("无法识别「" + token + "」。例如 Ctrl+K、左键或 Alt+F4。");
        }

        public static int Normalize(int vk)
        {
            if (vk == 0xA0 || vk == 0xA1) return 0x10;
            if (vk == 0xA2 || vk == 0xA3) return 0x11;
            if (vk == 0xA4 || vk == 0xA5) return 0x12;
            return vk == 0x5C ? 0x5B : vk;
        }

        private static int ModifierOrder(int key)
        {
            if (key == 0x11) return 0;
            if (key == 0x10) return 1;
            if (key == 0x12) return 2;
            return key == 0x5B ? 3 : 4;
        }

        public static bool IsModifier(int key) { return ModifierOrder(Normalize(key)) < 4; }

        public static int[] Record(int key, ModifierKeys mods)
        {
            var keys = new List<int>(5);
            if ((mods & ModifierKeys.Control) != 0) keys.Add(0x11);
            if ((mods & ModifierKeys.Shift) != 0) keys.Add(0x10);
            if ((mods & ModifierKeys.Alt) != 0) keys.Add(0x12);
            if ((mods & ModifierKeys.Windows) != 0) keys.Add(0x5B);
            key = Normalize(key);
            if (!keys.Contains(key)) keys.Add(key);
            return keys.ToArray();
        }

        public static int ReadKey(KeyEventArgs args)
        {
            var key = args.Key == Key.System ? args.SystemKey : args.Key == Key.ImeProcessed ? args.ImeProcessedKey : args.Key;
            return Normalize(KeyInterop.VirtualKeyFromKey(key));
        }

        public static string Name(int key)
        {
            switch (key)
            {
                case -1: return "左键";
                case -2: return "右键";
                case -3: return "中键";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0x5B: return "Win";
                case 0x20: return "空格";
                case 0x0D: return "回车";
                case 0xBB: return "加号";
                case 0xBD: return "减号";
            }
            if ((key >= 'A' && key <= 'Z') || (key >= '0' && key <= '9')) return ((char)key).ToString(CultureInfo.InvariantCulture);
            return KeyInterop.KeyFromVirtualKey(key).ToString();
        }
    }
}
