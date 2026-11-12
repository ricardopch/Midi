using System;
using System.IO;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /** Entrada de audio (WAV propio y Media Foundation en Windows) y salida en MP3. */
    internal static class AudioIO
    {
        public const int MaxSeconds = 10 * 60;

        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        public sealed class Input
        {
            public float[] Samples;
            public int Length;
            public int SampleRate;
            public bool Truncated;
        }

        public static Input Decode(string path, IProgress p)
        {
            if (WavFile.IsWav(path))
            {
                try
                {
                    WavFile.Pcm w = WavFile.Read(path, MaxSeconds);
                    if (w.Length > 0)
                    {
                        p.update(1f);
                        return new Input { Samples = w.Samples, Length = w.Length, SampleRate = w.SampleRate, Truncated = w.Truncated };
                    }
                }
                catch (IOException)
                {
                    if (!IsWindows) throw;   // en Windows se reintenta con Media Foundation
                }
            }
            if (!IsWindows) throw new IOException("En este sistema solo se pueden leer archivos WAV.");
            MediaFoundation.Decoded d;
            try
            {
                d = MediaFoundation.Decode(path, MaxSeconds, p);
            }
            catch (DllNotFoundException)
            {
                throw new MediaFoundation.MfUnavailableException();
            }
            return new Input { Samples = d.Samples, Length = d.Length, SampleRate = d.SampleRate, Truncated = d.Truncated };
        }

        /** Guarda el resultado en MP3 (codificador propio del motor, igual que en Android). */
        public static void SaveMp3(float[] pcm, int rate, string path, IProgress p)
        {
            File.WriteAllBytes(path, Mp3Encoder.encode(pcm, pcm.Length, rate, 128, p));
        }
    }
}
