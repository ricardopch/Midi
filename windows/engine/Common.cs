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

        public static void Fill(float[] a, int from, int to, float v) { for (int i = from; i < to; i++) a[i] = v; }

        public static float[] CopyOf(float[] a, int n)
        {
            float[] r = new float[n];
            Array.Copy(a, r, Math.Min(n, a.Length));
            return r;
        }

        /** Float.intBitsToFloat de Java. */
        public static float IntBitsToFloat(int bits) { return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0); }
    }
}
