using System;
using System.Collections.Generic;

namespace MelodiaMidi.Engine
{
    /** Nota MIDI: tono (0-127), inicio y fin en segundos, intensidad 0..1. */
    public sealed class Note
    {
        public int pitch;
        public double start;
        public double end;
        public float velocity;

        public Note(int pitch, double start, double end, float velocity)
        {
            this.pitch = pitch;
            this.start = start;
            this.end = end;
            this.velocity = velocity;
        }

        public double duration() { return end - start; }

        public static double midiToHz(double midi)
        {
            return 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);
        }
    }

    /** Informe de progreso y cancelación para los procesos largos. */
    public interface IProgress
    {
        /** fraction: 0..1 dentro de la etapa actual. */
        void update(float fraction);

        bool isCancelled();
    }

    /** Lanzada cuando el usuario cancela. */
    public sealed class CancelledException : Exception
    {
        public CancelledException() : base("Cancelado") { }
    }

    public static class ProgressUtil
    {
        private sealed class NoProgress : IProgress
        {
            public void update(float fraction) { }
            public bool isCancelled() { return false; }
        }

        public static readonly IProgress NONE = new NoProgress();
    }

    /** Equivalentes de utilidades de Java usadas por el código portado. */
    internal static class JU
    {
        public static void Fill(double[] a, double v) { for (int i = 0; i < a.Length; i++) a[i] = v; }

        public static double[] CopyOfRange(double[] a, int from, int to)
        {
            double[] r = new double[to - from];
            Array.Copy(a, from, r, 0, to - from);
            return r;
        }

        /** Math.round de Java: redondeo hacia arriba en el punto medio. */
        public static long Round(double x) { return (long)Math.Floor(x + 0.5); }

        public static double Signum(double x) { return x > 0 ? 1.0 : (x < 0 ? -1.0 : 0.0); }
    }

    /** FFT compleja radix-2 in situ (convención e^{-i}). */
    public sealed class Fft
    {
        private readonly int n;
        private readonly int[] rev;
        private readonly double[] cosT;
        private readonly double[] sinT;

        public Fft(int n)
        {
            if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("n debe ser potencia de 2");
            this.n = n;
            int levels = 0;
            while ((1 << levels) < n) levels++;
            rev = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0, v = i;
                for (int b = 0; b < levels; b++) { r = (r << 1) | (v & 1); v >>= 1; }
                rev[i] = r;
            }
            cosT = new double[n / 2];
            sinT = new double[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                cosT[i] = Math.Cos(2 * Math.PI * i / n);
                sinT[i] = Math.Sin(2 * Math.PI * i / n);
            }
        }

        public int size() { return n; }

        public void transform(double[] re, double[] im)
        {
            for (int i = 0; i < n; i++)
            {
                int j = rev[i];
                if (j > i)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1;
                int step = n / size;
                for (int i = 0; i < n; i += size)
                {
                    for (int j = i, k = 0; j < i + half; j++, k += step)
                    {
                        int l = j + half;
                        double c = cosT[k], s = sinT[k];
                        double tpre = re[l] * c + im[l] * s;
                        double tpim = -re[l] * s + im[l] * c;
                        re[l] = re[j] - tpre;
                        im[l] = im[j] - tpim;
                        re[j] += tpre;
                        im[j] += tpim;
                    }
                }
            }
        }
    }
}
