using System;
using System.IO;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /** Entrada/salida de audio: WAV propio y Media Foundation en Windows. */
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

        /**
         * Guarda el resultado. Devuelve la ruta realmente escrita: el MP4 pedido o,
         * si Windows no tiene codificador AAC, un WAV con el mismo nombre.
         */
        public static string Save(float[] pcm, int rate, string mp4Path, IProgress p, out string warning)
        {
            warning = null;
            if (IsWindows)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "MelodiaMIDI_" + Guid.NewGuid().ToString("N") + ".mp4");
                try
                {
                    MediaFoundation.EncodeMp4(pcm, rate, tmp, p);
                    File.Copy(tmp, mp4Path, true);
                    return mp4Path;
                }
                catch (MediaFoundation.NoAacEncoderException e)
                {
                    warning = e.Message + " Se ha guardado como WAV.";
                }
                catch (MediaFoundation.MfUnavailableException)
                {
                    warning = "Este Windows no tiene los componentes multimedia para crear MP4 (en las ediciones «N» instala el «Media Feature Pack»). Se ha guardado como WAV.";
                }
                catch (DllNotFoundException)
                {
                    warning = "Este Windows no tiene los componentes multimedia para crear MP4 (en las ediciones «N» instala el «Media Feature Pack»). Se ha guardado como WAV.";
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                }
            }
            else
            {
                warning = "En este sistema no hay codificador MP4: se ha guardado como WAV.";
            }
            string wav = Path.ChangeExtension(mp4Path, ".wav");
            File.WriteAllBytes(wav, WavFile.Build(pcm, rate));
            p.update(1f);
            return wav;
        }
    }
}
