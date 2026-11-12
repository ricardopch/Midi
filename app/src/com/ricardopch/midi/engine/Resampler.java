package com.ricardopch.midi.engine;

/** Remuestreo con sinc enventanado (Blackman) en forma polifásica. */
public final class Resampler {
    private static final int ZERO_CROSSINGS = 8;

    private Resampler() { }

    /** Convierte PCM de 16 bits mono a float [-1,1] en la frecuencia destino. */
    public static float[] resample(short[] in, int n, int srcRate, int dstRate, Progress p) {
        return resample(null, in, n, srcRate, dstRate, p);
    }

    public static float[] resample(float[] in, int n, int srcRate, int dstRate, Progress p) {
        return resample(in, null, n, srcRate, dstRate, p);
    }

    private static int gcd(int a, int b) {
        while (b != 0) {
            int t = a % b;
            a = b;
            b = t;
        }
        return a;
    }

    private static double kernel(double x, double fc, double halfWidth) {
        x = Math.abs(x);
        if (x >= halfWidth) return 0;
        double u = fc * x;
        double sinc = (u == 0) ? 1.0 : Math.sin(Math.PI * u) / (Math.PI * u);
        double w = x / halfWidth;
        double blackman = 0.42 + 0.5 * Math.cos(Math.PI * w) + 0.08 * Math.cos(2 * Math.PI * w);
        return fc * sinc * blackman;
    }

    private static float[] resample(float[] fin, short[] sin, int n, int srcRate, int dstRate, Progress p) {
        if (p == null) p = Progress.NONE;
        final float s16 = 1f / 32768f;
        if (srcRate == dstRate) {
            float[] same = new float[n];
            for (int i = 0; i < n; i++) same[i] = fin != null ? fin[i] : sin[i] * s16;
            return same;
        }
        // tiempo de entrada de la salida i: i * M / L (racional exacto)
        int g = gcd(srcRate, dstRate);
        long L = dstRate / g, M = srcRate / g;
        if (L > 4096) {          // razón poco habitual: aproximación con 4096 fases
            L = 4096;
            M = Math.round((double) srcRate / dstRate * 4096);
        }
        double ratio = (double) L / M;                       // salidas por entrada
        double fc = Math.min(1.0, ratio) * 0.92;
        double halfWidth = ZERO_CROSSINGS / fc;
        int R = (int) Math.ceil(halfWidth);                   // muestras a cada lado
        int taps = 2 * R;
        int phases = (int) L;
        float[] coef = new float[phases * taps];
        for (int ph = 0; ph < phases; ph++) {
            double frac = (double) ph / L;
            for (int k = 0; k < taps; k++) {
                double d = (k - R + 1) - frac;              // j - t
                coef[ph * taps + k] = (float) kernel(d, fc, halfWidth);
            }
        }
        int outLen = (int) ((long) n * L / M);
        float[] out = new float[outLen];
        for (int i = 0; i < outLen; i++) {
            if ((i & 0xFFFF) == 0) {
                if (p.isCancelled()) throw new Progress.Cancelled();
                p.update((float) i / outLen);
            }
            long num = (long) i * M;
            int center = (int) (num / L);
            int ph = (int) (num - (long) center * L);
            int j0 = center - R + 1;
            int base = ph * taps;
            float acc = 0;
            if (j0 >= 0 && j0 + taps <= n) {
                if (fin != null) {
                    for (int k = 0; k < taps; k++) acc += coef[base + k] * fin[j0 + k];
                } else {
                    for (int k = 0; k < taps; k++) acc += coef[base + k] * sin[j0 + k];
                    acc *= s16;
                }
            } else {
                for (int k = 0; k < taps; k++) {
                    int j = j0 + k;
                    if (j < 0 || j >= n) continue;
                    acc += coef[base + k] * (fin != null ? fin[j] : sin[j] * s16);
                }
            }
            out[i] = acc;
        }
        p.update(1f);
        return out;
    }
}
