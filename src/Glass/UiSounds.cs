using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading;

namespace GlassClicker
{
    internal static class UiSounds
    {
        private enum Effect { Click, Key }

        private static readonly object gate = new object();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly Lazy<SoundPlayer[]> sounds = new Lazy<SoundPlayer[]>(() =>
            new[] { LoadSound("click.wav"), LoadSound("key.wav") });
        private static readonly long[] lastCue = { -1000, -1000 };
        private static bool enabled = true, scheduled;
        private static Effect? pending;
        private static long pendingAt;

        public static void SetEnabled(bool value)
        {
            lock (gate)
            {
                enabled = value;
                if (!enabled) pending = null;
                else if (!sounds.IsValueCreated) Schedule();
            }
        }

        public static void Click() { Request(Effect.Click); }
        public static void Key() { Request(Effect.Key); }

        private static void Request(Effect effect)
        {
            lock (gate)
            {
                if (!enabled) return;
                var index = (int)effect;
                var now = clock.ElapsedMilliseconds;
                if (now - lastCue[index] < (effect == Effect.Key ? 100 : 65)) return;
                lastCue[index] = now;
                pending = effect;
                pendingAt = now;
                Schedule();
            }
        }

        private static void Schedule()
        {
            if (scheduled) return;
            scheduled = true;
            ThreadPool.QueueUserWorkItem(PlayPending);
        }

        private static void PlayPending(object ignored)
        {
            var bank = sounds.Value;
            while (true)
            {
                Effect effect;
                lock (gate)
                {
                    if (!enabled || !pending.HasValue || clock.ElapsedMilliseconds - pendingAt > 180)
                    {
                        pending = null;
                        scheduled = false;
                        return;
                    }
                    effect = pending.Value;
                    pending = null;
                }
                var player = bank[(int)effect];
                if (player == null) continue;
                try { player.PlaySync(); }
                catch (TimeoutException) { }
                catch (InvalidOperationException) { }
            }
        }

        private static SoundPlayer LoadSound(string name)
        {
            var assembly = typeof(UiSounds).Assembly;
            var stream = assembly.GetManifestResourceStream("ArcGlow.Audio." + name);
            if (stream == null) return null;
            var player = new SoundPlayer(stream) { LoadTimeout = 1000 };
            try
            {
                player.Load();
                return player;
            }
            catch (IOException) { }
            catch (TimeoutException) { }
            catch (InvalidOperationException) { }
            player.Dispose();
            stream.Dispose();
            return null;
        }
    }
}
