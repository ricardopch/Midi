using System;
using System.Collections.Generic;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /**
     * Detección sencilla de golpes de batería en la pista separada por Spleeter: aumento de energía por bandas
     * (bombo 30-150 Hz, caja 500-5000 Hz, platos 8-16 kHz), picos de novedad y clasificación por la banda
     * que más crece. Devuelve notas General MIDI de percusión: 36 bombo, 38 caja, 42 charles cerrado.
     */
    internal static class DrumDetector
    {
        private static readonly int[] Pitches = { 36, 38, 42 };
        private static readonly double[] Lo = { 30, 500, 8000 }, Hi = { 150, 5000, 16000 };
        private static readonly double[] Rel = { 0.65, 0.35, 0.35 };

        public static List<Note> Detect(float[] x, int length, int sr)
        {
            const int n = 1024, hop = 256;
            List<Note> res = new List<Note>();
            int nf = length >= n ? 1 + (length - n) / hop : 0;
            if (nf < 8) return res;
            float[] win = new float[n];
            for (int i = 0; i < n; i++) win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
            int[] k0 = new int[3], k1 = new int[3];
            for (int b = 0; b < 3; b++)
            {
                k0[b] = (int)Math.Ceiling(Lo[b] * n / sr);
                k1[b] = Math.Min(n / 2, (int)Math.Ceiling(Hi[b] * n / sr));
            }
            float[] prev = new float[n / 2 + 1], cur = new float[n / 2 + 1];
            float[] band = new float[nf * 3];
            Spleeter.Fft fft = new Spleeter.Fft(n);
            float[] re = new float[n], im = new float[n];
            for (int t = 0; t < nf; t++)
            {
                for (int i = 0; i < n; i++) { re[i] = x[t * hop + i] * win[i]; im[i] = 0f; }
                fft.Forward(re, im);
                for (int k = 0; k <= n / 2; k++) cur[k] = re[k] * re[k] + im[k] * im[k];
                for (int b = 0; b < 3; b++)
                {
                    double s = 0;
                    if (t > 0)
                        for (int k = k0[b]; k < k1[b]; k++)
                        {
                            float d = cur[k] - prev[k];
                            if (d > 0) s += d;
                        }
                    band[t * 3 + b] = (float)Math.Sqrt(s);
                }
                float[] tmp = prev; prev = cur; cur = tmp;
            }
            // normalización por el percentil 99 de cada banda
            float[] top = new float[3];
            for (int b = 0; b < 3; b++)
            {
                float[] v = new float[nf];
                for (int t = 0; t < nf; t++) v[t] = band[t * 3 + b];
                Array.Sort(v);
                top[b] = v[Math.Min(nf - 1, (int)(0.99 * (nf - 1)))] + 1e-12f;
            }
            float[] nov = new float[nf];
            for (int t = 0; t < nf; t++)
            {
                float s = 0;
                for (int b = 0; b < 3; b++) { band[t * 3 + b] /= top[b]; s += band[t * 3 + b]; }
                nov[t] = s;
            }
            double[] cs = new double[nf + 1];
            for (int t = 0; t < nf; t++) cs[t + 1] = cs[t] + nov[t];
            double fps = (double)sr / hop;
            int w = (int)(0.5 * fps), gap = (int)(0.04 * fps), last = -1000000;
            for (int t = 2; t < nf - 2; t++)
            {
                float v = nov[t];
                if (v < 0.15f) continue;
                bool peak = true;
                for (int d = -2; d <= 2; d++) if (nov[t + d] > v) peak = false;
                if (!peak) continue;
                int lo = Math.Max(0, t - w), hi = Math.Min(nf, t + w + 1);
                if (v < 1.4 * (cs[hi] - cs[lo]) / (hi - lo)) continue;
                if (t - last < gap) continue;
                last = t;
                float m = Math.Max(band[t * 3], Math.Max(band[t * 3 + 1], band[t * 3 + 2]));
                double time = (t * hop + n * 0.5) / sr;
                for (int b = 0; b < 3; b++)
                {
                    float bv = band[t * 3 + b];
                    if (bv >= Rel[b] && bv >= 0.35f * m)
                        res.Add(new Note(Pitches[b], time, time + 0.08, Math.Max(0.3f, Math.Min(1f, bv))));
                }
            }
            return res;
        }
    }
}
