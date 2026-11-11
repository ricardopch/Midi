package com.ricardopch.midi.engine;

/** FFT compleja radix-2 in situ (convención e^{-i}). */
public final class Fft {
    private final int n;
    private final int[] rev;
    private final double[] cosT;
    private final double[] sinT;

    public Fft(int n) {
        if (n < 2 || (n & (n - 1)) != 0) throw new IllegalArgumentException("n debe ser potencia de 2");
        this.n = n;
        int levels = Integer.numberOfTrailingZeros(n);
        rev = new int[n];
        for (int i = 0; i < n; i++) rev[i] = Integer.reverse(i) >>> (32 - levels);
        cosT = new double[n / 2];
        sinT = new double[n / 2];
        for (int i = 0; i < n / 2; i++) {
            cosT[i] = Math.cos(2 * Math.PI * i / n);
            sinT[i] = Math.sin(2 * Math.PI * i / n);
        }
    }

    public int size() { return n; }

    public void transform(double[] re, double[] im) {
        for (int i = 0; i < n; i++) {
            int j = rev[i];
            if (j > i) {
                double t = re[i]; re[i] = re[j]; re[j] = t;
                t = im[i]; im[i] = im[j]; im[j] = t;
            }
        }
        for (int size = 2; size <= n; size <<= 1) {
            int half = size >> 1;
            int step = n / size;
            for (int i = 0; i < n; i += size) {
                for (int j = i, k = 0; j < i + half; j++, k += step) {
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
