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
        private static readonly Lazy<byte[][]> resources = new Lazy<byte[][]>(() =>
            new[] { ReadClip("click.wav"), ReadClip("key.wav") });
        private static readonly SoundPlayer[] sounds = new SoundPlayer[2];
        private static int preparedVolume = -1;
        private static readonly long[] lastCue = { -1000, -1000 };
        private static bool enabled, scheduled;
        private static int volume = AppSettings.DefaultInterfaceVolume;
        private static Effect? pending;
        private static long pendingAt;

        public static void Configure(bool value, int percent)
        {
            lock (gate)
            {
                enabled = value;
                volume = percent;
                if (!enabled || volume == 0) pending = null;
            }
        }

        public static void Click() { Request(Effect.Click); }
        public static void Key() { Request(Effect.Key); }

        private static void Request(Effect effect)
        {
            lock (gate)
            {
                if (!enabled || volume == 0) return;
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
            while (true)
            {
                Effect effect;
                int percent;
                lock (gate)
                {
                    if (!enabled || volume == 0 || !pending.HasValue || clock.ElapsedMilliseconds - pendingAt > 180)
                    {
                        pending = null;
                        scheduled = false;
                        return;
                    }
                    effect = pending.Value;
                    percent = volume;
                    pending = null;
                }
                try
                {
                    var player = GetPlayer((int)effect, percent);
                    if (player != null) player.PlaySync();
                }
                catch (IOException) { }
                catch (TimeoutException) { }
                catch (InvalidOperationException) { }
            }
        }

        private static SoundPlayer GetPlayer(int index, int percent)
        {
            if (preparedVolume != percent)
            {
                foreach (var sound in sounds)
                {
                    if (sound == null) continue;
                    sound.Stream.Dispose();
                    sound.Dispose();
                }
                Array.Clear(sounds, 0, sounds.Length);
                preparedVolume = percent;
            }
            if (sounds[index] != null) return sounds[index];
            var source = resources.Value[index];
            if (source == null) return null;
            var bytes = (byte[])source.Clone();
            // The bundled effects have a standard 44-byte header and signed 16-bit PCM samples.
            for (var offset = 44; offset + 1 < bytes.Length; offset += 2)
            {
                var sample = (short)(bytes[offset] | bytes[offset + 1] << 8);
                var scaled = sample * percent / 100;
                bytes[offset] = unchecked((byte)scaled);
                bytes[offset + 1] = unchecked((byte)(scaled >> 8));
            }
            return sounds[index] = new SoundPlayer(new MemoryStream(bytes, false)) { LoadTimeout = 1000 };
        }

        private static byte[] ReadClip(string name)
        {
            var assembly = typeof(UiSounds).Assembly;
            using (var stream = assembly.GetManifestResourceStream("ArcGlow.Audio." + name))
            {
                if (stream == null) return null;
                using (var file = new MemoryStream())
                {
                    stream.CopyTo(file);
                    return file.ToArray();
                }
            }
        }
    }
}
