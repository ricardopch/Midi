// Generado desde el motor Java con tools/java2cs.py. No editar a mano.
using System;
using System.Collections.Generic;

namespace MelodiaMidi.Engine
{
/** Sintetizador de un solo instrumento, sin muestras externas (aditivo, FM, Karplus-Strong). */
public sealed class Synth {
    public const int PIANO = 0, EPIANO = 1, ORGAN = 2, GUITAR = 3, FLUTE = 4, VIOLIN = 5,
            CLARINET = 6, TRUMPET = 7, MARIMBA = 8, MUSICBOX = 9, CHIP = 10, SYNTH = 11;

    public static readonly string[] NAMES = {
            "Piano", "Piano eléctrico", "Órgano", "Guitarra", "Flauta", "Violín",
            "Clarinete", "Trompeta", "Marimba", "Caja de música", "Chiptune 8 bits", "Sintetizador"
    };

    private static readonly double[] REVERB_WET = {0.14, 0.12, 0.12, 0.12, 0.16, 0.18, 0.13, 0.13, 0.12, 0.16, 0.05, 0.13};

    private readonly int sr;
    private readonly int instrument;
    private readonly Random rnd = new Random(12345);
    private readonly Dictionary<int, float[]> tableCache = new Dictionary<int, float[]>();
    private const int TABLE = 2048;

    public Synth(int instrument, int sampleRate) {
        this.instrument = Math.Max(0, Math.Min(NAMES.Length - 1, instrument));
        this.sr = sampleRate;
    }

    /** Renderiza todas las notas. minDuration: duración mínima del resultado (s). */
    public float[] render(List<Note> notes, double minDuration, IProgress p) {
        if (p == null) p = ProgressUtil.NONE;
        double end = minDuration;
        foreach (Note n in notes) end = Math.Max(end, n.end + 1.5);
        int len = (int) Math.Ceiling(end * sr) + sr / 2;
        float[] @out = new float[len];
        int count = 0;
        foreach (Note n in notes) {
            if ((count & 15) == 0) {
                if (p.isCancelled()) throw new CancelledException();
                p.update(0.9f * count / Math.Max(1, notes.Count));
            }
            count++;
            int pitch = n.pitch;
            if (instrument == MUSICBOX && pitch < 84) pitch += 12;
            renderNote(@out, (int) JU.Round(n.start * sr), Math.Max(0.03, n.duration()), pitch, n.velocity);
        }
        reverb(@out, REVERB_WET[instrument]);
        normalize(@out, 0.89f);
        p.update(1f);
        return @out;
    }

    /** Frase corta para probar el instrumento. */
    public static List<Note> demoPhrase() {
        int[] pitches = {64, 64, 65, 67, 67, 65, 64, 62, 60, 60, 62, 64, 64, 62, 62};
        double[] beats = {1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5, 0.5, 2};
        List<Note> l = new List<Note>();
        double t = 0.1, spb = 0.36;
        for (int i = 0; i < pitches.Length; i++) {
            double d = beats[i] * spb;
            l.Add(new Note(pitches[i], t, t + d * 0.92, i % 4 == 0 ? 0.95f : 0.8f));
            t += d;
        }
        return l;
    }

    // ------------------------------------------------------------------

    private void renderNote(float[] @out, int start, double dur, int pitch, float vel) {
        double f = Note.midiToHz(pitch);
        int L = (int) (dur * sr);
        switch (instrument) {
            case PIANO: piano(@out, start, L, f, vel); break;
            case EPIANO: epiano(@out, start, L, f, vel); break;
            case ORGAN: organ(@out, start, L, f, vel); break;
            case GUITAR: guitar(@out, start, L, f, vel); break;
            case FLUTE: flute(@out, start, L, f, vel); break;
            case VIOLIN: violin(@out, start, L, f, vel); break;
            case CLARINET: clarinet(@out, start, L, f, vel); break;
            case TRUMPET: trumpet(@out, start, L, f, vel); break;
            case MARIMBA: marimba(@out, start, L, f, vel); break;
            case MUSICBOX: musicbox(@out, start, L, f, vel); break;
            case CHIP: chip(@out, start, L, f, vel); break;
            default: synthLead(@out, start, L, f, vel); break;
        }
    }

    /** Parcial senoidal con decaimiento exponencial y apagado al soltar la nota. */
    private void decayingPartial(float[] @out, int start, int L, double freq, double amp, double tau,
                                 double attackSec, double releaseTau, int maxLen) {
        if (freq >= sr * 0.45 || amp <= 0) return;
        double w = 2 * Math.PI * freq / sr;
        double cw = Math.Cos(w), sw = Math.Sin(w);
        double s = 0, c = 1;
        double dec = Math.Exp(-1.0 / (tau * sr));
        double rel = Math.Exp(-1.0 / (releaseTau * sr));
        int A = Math.Max(1, (int) (attackSec * sr));
        int total = Math.Min(maxLen, L + (int) (releaseTau * sr * 7));
        double a = amp;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double env = i < A ? (double) i / A : 1.0;
            @out[o] += (float) (a * env * s);
            double ns = s * cw + c * sw;
            c = c * cw - s * sw;
            s = ns;
            a *= (i < L) ? dec : dec * rel;
            if ((i & 1023) == 1023) {
                double r = 1.0 / Math.Sqrt(s * s + c * c);
                s *= r;
                c *= r;
                if (a < 1e-5) break;
            }
        }
    }

    private void piano(float[] @out, int start, int L, double f, float vel) {
        int K = 18;
        double B = 0.00012 * Math.Pow(2, (12 * Math.Log(f / 261.6) / Math.Log(2)) / 24.0);
        double tau1 = clamp(1.4 * Math.Pow(261.6 / f, 0.55), 0.25, 3.5);
        double gain = 0.22 * vel;
        double bright = 0.10 + 0.14 * (1 - vel);
        double hp = 0.12; // posición del martillo
        double norm = Math.Sin(Math.PI * hp);
        for (int k = 1; k <= K; k++) {
            double fk = k * f * Math.Sqrt(1 + B * k * k);
            if (fk > sr * 0.45) break;
            double ak = Math.Abs(Math.Sin(Math.PI * k * hp)) / norm / k * Math.Exp(-bright * (k - 1));
            if (f > 1000) ak *= Math.Exp(-0.5 * (k - 1));
            double tk = tau1 / (1 + 0.45 * (k - 1));
            decayingPartial(@out, start, L, fk, gain * ak, tk, 0.002, 0.07, sr * 8);
            if (k <= 3) decayingPartial(@out, start, L, fk * 1.0007, gain * ak * 0.5, tk * 1.3, 0.002, 0.07, sr * 8);
        }
        // golpe del martillo
        int nlen = (int) (0.012 * sr);
        double lp = 0;
        for (int i = 0; i < nlen && start + i < @out.Length; i++) {
            lp += 0.25 * ((rnd.NextDouble() * 2 - 1) - lp);
            @out[start + i] += (float) (lp * gain * 0.25 * (1 - (double) i / nlen));
        }
    }

    private void epiano(float[] @out, int start, int L, double f, float vel) {
        double tau = clamp(1.7 * Math.Pow(261.6 / f, 0.4), 0.4, 3.0);
        double I0 = 0.8 + 2.0 * vel;
        bool tine = f * 14 < sr * 0.45;
        int R = (int) (0.09 * sr);
        int total = L + 6 * R;
        double gain = 0.25 * vel;
        double w = 2 * Math.PI * f / sr;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double t = (double) i / sr;
            double env = Math.Exp(-t / tau) * Math.Min(1.0, i / (0.002 * sr));
            if (i >= L) env *= Math.Exp(-(double) (i - L) / R);
            if (env < 1e-5 && i > L) break;
            double I = I0 * Math.Exp(-t / 0.45);
            double mod = I * Math.Sin(w * i);
            if (tine) mod += 0.35 * vel * Math.Exp(-t / 0.025) * Math.Sin(14 * w * i);
            double trem = 1.0 + 0.06 * Math.Sin(2 * Math.PI * 4.5 * t);
            @out[o] += (float) (gain * env * trem * Math.Sin(w * i + mod));
        }
    }

    private void organ(float[] @out, int start, int L, double f, float vel) {
        double @base = f * 0.5;
        int[] harm = {1, 2, 3, 4, 6, 8, 10, 12, 16};
        double[] amp = {0.45, 1.0, 0.55, 0.65, 0.3, 0.35, 0.12, 0.18, 0.2};
        float[] table = makeTable(1000 + (int) JU.Round(@base * 10), @base, harm, amp);
        int A = (int) (0.006 * sr), R = (int) (0.035 * sr);
        double gain = 0.16 * (0.7 + 0.3 * vel);
        wavetableVoice(@out, start, L, @base, table, gain, A, R, 0, 0, 0, 0.0, 6.2, 0.08);
    }

    private void guitar(float[] @out, int start, int L, double f, float vel) {
        double D = sr / f;
        double d = D - 0.5;                          // el filtro promedio añade 0,5 muestras
        int N = (int) Math.Floor(d - 0.5);
        if (N < 2) N = 2;
        double frac = d - N;
        double apc = (1 - frac) / (1 + frac);
        double T60 = clamp(3.2 * Math.Sqrt(110.0 / f), 0.6, 4.0);
        double rho = Math.Pow(10, -3.0 / (f * T60));
        float[] buf = new float[N];
        double lp = 0, bright = 0.35 + 0.5 * vel;
        double mean = 0;
        for (int i = 0; i < N; i++) {
            lp += bright * ((rnd.NextDouble() * 2 - 1) - lp);
            buf[i] = (float) lp;
            mean += lp;
        }
        mean /= N;
        int pick = Math.Max(1, (int) (0.13 * N));
        float[] ex = new float[N];
        for (int i = 0; i < N; i++) ex[i] = (float) ((buf[i] - mean) - (i >= pick ? (buf[i - pick] - mean) : 0) * 0.6);
        Array.Copy(ex, 0, buf, 0, N);
        int R = (int) (0.06 * sr);
        int total = L + 6 * R;
        double gain = 0.5 * vel;
        int idx = 0;
        double prevY = 0, apIn = 0, apOut = 0, body = 0;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double y = buf[idx];
            double avg = 0.5 * (y + prevY);
            prevY = y;
            double ap = apc * avg + apIn - apc * apOut;
            apIn = avg;
            apOut = ap;
            buf[idx] = (float) (ap * rho);
            idx++;
            if (idx == N) idx = 0;
            double env = i < L ? 1.0 : Math.Exp(-(double) (i - L) / R);
            body += 0.5 * (y - body);
            @out[o] += (float) (gain * env * body);
        }
    }

    private void flute(float[] @out, int start, int L, double f, float vel) {
        float[] table = makeTable(2000 + (int) JU.Round(f * 10), f, new int[]{1, 2, 3, 4, 5},
                new double[]{1.0, 0.22, 0.09, 0.04, 0.015});
        int A = (int) (0.06 * sr), R = (int) (0.08 * sr);
        double gain = 0.22 * (0.6 + 0.4 * vel);
        wavetableVoice(@out, start, L, f, table, gain, A, R, 5.0, 0.004, 0.16, 0.05, 5.0, 0.04);
    }

    private void violin(float[] @out, int start, int L, double f, float vel) {
        int K = Math.Min(40, (int) (sr * 0.45 / (f * 1.01)));
        int[] h = new int[K];
        double[] a = new double[K];
        for (int k = 1; k <= K; k++) {
            double fk = k * f;
            double body = 1 + 1.6 * gauss(Math.Log(fk / 2800) / Math.Log(2), 0.35)
                    + 0.7 * gauss(Math.Log(fk / 450) / Math.Log(2), 0.4);
            h[k - 1] = k;
            a[k - 1] = body / k / (1 + Math.Pow(fk / 6000, 4));
        }
        float[] table = makeTable(3000 + (int) JU.Round(f * 10), f, h, a);
        int A = (int) (0.08 * sr), R = (int) (0.13 * sr);
        double gain = 0.13 * (0.6 + 0.4 * vel);
        wavetableVoice(@out, start, L, f, table, gain, A, R, 5.6, 0.006, 0.12, 0.02, 0, 0);
    }

    private void clarinet(float[] @out, int start, int L, double f, float vel) {
        int K = Math.Min(25, (int) (sr * 0.45 / f));
        int[] h = new int[K];
        double[] a = new double[K];
        for (int k = 1; k <= K; k++) {
            h[k - 1] = k;
            double v = (k % 2 == 1) ? 1.0 / Math.Pow(k, 1.1) : 0.06 / k;
            if (k * f > 1600) v *= Math.Pow(1600 / (k * f), 1.5);
            a[k - 1] = v;
        }
        float[] table = makeTable(4000 + (int) JU.Round(f * 10), f, h, a);
        int A = (int) (0.03 * sr), R = (int) (0.05 * sr);
        double gain = 0.2 * (0.6 + 0.4 * vel);
        wavetableVoice(@out, start, L, f, table, gain, A, R, 4.5, 0.0015, 0.3, 0.012, 0, 0);
    }

    private void trumpet(float[] @out, int start, int L, double f, float vel) {
        int K = Math.Min(24, (int) (sr * 0.45 / f));
        int[] h = new int[K];
        double[] dull = new double[K], bright = new double[K];
        for (int k = 1; k <= K; k++) {
            h[k - 1] = k;
            double cut = 1.0 / (1 + Math.Pow(k * f / 5000, 3));
            dull[k - 1] = Math.Pow(k, -2.4) * cut;
            bright[k - 1] = Math.Pow(k, -0.8) * cut * (1 + 0.8 * gauss(Math.Log(k * f / 1300) / Math.Log(2), 0.6));
        }
        float[] t1 = makeTable(5000 + (int) JU.Round(f * 10), f, h, dull);
        float[] t2 = makeTable(6000 + (int) JU.Round(f * 10), f, h, bright);
        int A = (int) (0.035 * sr), R = (int) (0.07 * sr);
        int total = L + 6 * R;
        double gain = 0.16 * (0.6 + 0.4 * vel);
        double phase = 0;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double t = (double) i / sr;
            double env = i < A ? (double) i / A : 1.0;
            if (i >= L) env *= Math.Exp(-(double) (i - L) / R);
            double b = (i < A) ? (double) i / A : Math.Max(0.75, 1.0 - 0.25 * (t - A / (double) sr) / 0.15);
            b *= (0.6 + 0.4 * vel);
            double scoop = t < 0.04 ? -0.012 * (1 - t / 0.04) : 0;
            double vib = t > 0.3 ? 0.003 * Math.Min(1, (t - 0.3) / 0.3) * Math.Sin(2 * Math.PI * 5 * t) : 0;
            phase += f * (1 + scoop + vib) * TABLE / sr;
            if (phase >= TABLE) phase -= TABLE;
            double v1 = lerp(t1, phase), v2 = lerp(t2, phase);
            @out[o] += (float) (gain * env * (v1 + (v2 - v1) * b));
        }
    }

    private void marimba(float[] @out, int start, int L, double f, float vel) {
        double tau = clamp(0.45 * Math.Pow(220 / f, 0.5), 0.1, 1.0);
        int maxLen = L + (int) (0.35 * sr);
        double g = 0.35 * vel;
        decayingPartial(@out, start, maxLen, f, g, tau, 0.001, 0.05, maxLen + sr);
        decayingPartial(@out, start, maxLen, f * 3.93, g * 0.28 * vel, tau / 4, 0.001, 0.05, maxLen + sr);
        decayingPartial(@out, start, maxLen, f * 9.2, g * 0.1 * vel, tau / 10, 0.0005, 0.05, maxLen + sr);
        click(@out, start, g * 0.15);
    }

    private void musicbox(float[] @out, int start, int L, double f, float vel) {
        double tau = clamp(0.9 * Math.Pow(523 / f, 0.3), 0.3, 1.5);
        int maxLen = L + (int) (1.2 * sr);
        double g = 0.3 * vel;
        decayingPartial(@out, start, maxLen, f, g, tau, 0.0008, 0.3, maxLen + sr);
        decayingPartial(@out, start, maxLen, f * 2.0, g * 0.12, tau / 2, 0.0008, 0.3, maxLen + sr);
        decayingPartial(@out, start, maxLen, f * 5.4, g * 0.18, tau / 8, 0.0005, 0.3, maxLen + sr);
        decayingPartial(@out, start, maxLen, f * 8.9, g * 0.07, tau / 15, 0.0005, 0.3, maxLen + sr);
    }

    private void chip(float[] @out, int start, int L, double f, float vel) {
        int K = Math.Max(1, Math.Min(60, (int) (sr * 0.45 / f)));
        int[] h = new int[K];
        double[] a = new double[K];
        double duty = 0.25;
        for (int k = 1; k <= K; k++) {
            h[k - 1] = k;
            a[k - 1] = Math.Sin(Math.PI * k * duty) / k;
        }
        float[] table = makeTable(7000 + (int) JU.Round(f * 10), f, h, a);
        int A = (int) (0.002 * sr), R = (int) (0.02 * sr);
        int total = L + 6 * R;
        double gain = 0.14 * (0.7 + 0.3 * vel);
        double phase = 0;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double t = (double) i / sr;
            double env = i < A ? (double) i / A : 0.75 + 0.25 * Math.Exp(-t / 0.06);
            if (i >= L) env *= Math.Exp(-(double) (i - L) / R);
            double vib = t > 0.25 ? 0.005 * Math.Sin(2 * Math.PI * 6 * t) : 0;
            phase += f * (1 + vib) * TABLE / sr;
            if (phase >= TABLE) phase -= TABLE;
            @out[o] += (float) (gain * env * lerp(table, phase));
        }
    }

    private void synthLead(float[] @out, int start, int L, double f, float vel) {
        int K = Math.Max(1, Math.Min(50, (int) (sr * 0.45 / (f * 1.01))));
        int[] h = new int[K];
        double[] a = new double[K];
        for (int k = 1; k <= K; k++) { h[k - 1] = k; a[k - 1] = 1.0 / k; }
        float[] table = makeTable(8000 + (int) JU.Round(f * 10), f, h, a);
        int A = (int) (0.01 * sr), R = (int) (0.12 * sr);
        int total = L + 6 * R;
        double gain = 0.12 * (0.6 + 0.4 * vel);
        double p1 = 0, p2 = TABLE / 3.0;
        double low = 0, band = 0;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double t = (double) i / sr;
            double env = i < A ? (double) i / A : 1.0;
            if (i >= L) env *= Math.Exp(-(double) (i - L) / R);
            double vib = t > 0.3 ? 0.004 * Math.Min(1, (t - 0.3) / 0.3) * Math.Sin(2 * Math.PI * 5.5 * t) : 0;
            p1 += f * (1.0035 + vib) * TABLE / sr;
            p2 += f * (0.9965 + vib) * TABLE / sr;
            if (p1 >= TABLE) p1 -= TABLE;
            if (p2 >= TABLE) p2 -= TABLE;
            double x = 0.5 * (lerp(table, p1) + lerp(table, p2));
            double cutoff = Math.Min(0.45 * sr, f * (2.5 + 6 * Math.Exp(-t / 0.25)) * (0.6 + 0.5 * vel));
            double fq = 2 * Math.Sin(Math.PI * cutoff / (sr * 2));
            low += fq * band;
            double high = x - low - 0.7 * band;
            band += fq * high;
            @out[o] += (float) (gain * env * low);
        }
    }

    // ---------------- utilidades ----------------

    /**
     * Voz de tabla de onda con envolvente AR, vibrato retardado, trémolo y ruido de soplido.
     */
    private void wavetableVoice(float[] @out, int start, int L, double f, float[] table, double gain, int A, int R,
                                double vibRate, double vibDepth, double vibDelay, double breath,
                                double tremRate, double tremDepth) {
        int total = L + 6 * R;
        double phase = 0;
        double nlp = 0, bp1 = 0, bp2 = 0;
        double r = Math.Exp(-Math.PI * Math.Max(80, f * 0.25) / sr);
        double c1 = 2 * r * Math.Cos(2 * Math.PI * f / sr), c2 = -r * r;
        for (int i = 0; i < total; i++) {
            int o = start + i;
            if (o >= @out.Length) break;
            double t = (double) i / sr;
            double env;
            if (i < A) {
                double u = (double) i / A;
                env = u * u * (3 - 2 * u);
            } else env = 1.0;
            if (i >= L) env *= Math.Exp(-(double) (i - L) / R);
            double vib = 0;
            if (vibRate > 0 && t > vibDelay) vib = vibDepth * Math.Min(1, (t - vibDelay) / 0.3) * Math.Sin(2 * Math.PI * vibRate * t);
            phase += f * (1 + vib) * TABLE / sr;
            if (phase >= TABLE) phase -= TABLE;
            double v = lerp(table, phase);
            if (tremDepth > 0) v *= 1 + tremDepth * Math.Sin(2 * Math.PI * tremRate * t);
            if (breath > 0) {
                double nz = rnd.NextDouble() * 2 - 1;
                double y = nz * (1 - r) + c1 * bp1 + c2 * bp2;   // resonador en f
                bp2 = bp1;
                bp1 = y;
                nlp += 0.3 * (nz - nlp);
                double chiff = (i < A * 2) ? 1.0 - (double) i / (A * 2) : 0;
                v += breath * (y * 4 + nlp * (0.15 + chiff));
            }
            @out[o] += (float) (gain * env * v);
        }
    }

    /** Tabla de un ciclo normalizada (pico 1) con los armónicos indicados. */
    private float[] makeTable(int key, double f, int[] harm, double[] amp) {
        int k = key;
        float[] cached;
        if (tableCache.TryGetValue(k, out cached)) return cached;
        double[] acc = new double[TABLE + 1];
        for (int j = 0; j < harm.Length; j++) {
            if (harm[j] * f >= sr * 0.45 || amp[j] == 0) continue;
            double a = amp[j];
            int hk = harm[j];
            for (int i = 0; i < TABLE; i++) acc[i] += a * Math.Sin(2 * Math.PI * hk * i / TABLE);
        }
        double mx = 1e-9;
        for (int i = 0; i < TABLE; i++) mx = Math.Max(mx, Math.Abs(acc[i]));
        float[] t = new float[TABLE + 1];
        for (int i = 0; i < TABLE; i++) t[i] = (float) (acc[i] / mx);
        t[TABLE] = t[0];
        if (tableCache.Count > 400) tableCache.Clear();
        tableCache[k] = t;
        return t;
    }

    private static double lerp(float[] t, double phase) {
        int i = (int) phase;
        double fr = phase - i;
        return t[i] + (t[i + 1] - t[i]) * fr;
    }

    private void click(float[] @out, int start, double g) {
        int n = (int) (0.004 * sr);
        double prev = 0;
        for (int i = 0; i < n && start + i < @out.Length; i++) {
            double nz = rnd.NextDouble() * 2 - 1;
            double hp = nz - prev;
            prev = nz;
            @out[start + i] += (float) (g * hp * (1 - (double) i / n));
        }
    }

    private static double gauss(double x, double s) { return Math.Exp(-0.5 * (x / s) * (x / s)); }

    private static double clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

    /** Reverberación Schroeder mono sencilla (4 peines + 2 pasa-todo). */
    private void reverb(float[] x, double wet) {
        if (wet <= 0) return;
        double scale = sr / 44100.0;
        int[] combLen = {(int) (1557 * scale), (int) (1617 * scale), (int) (1491 * scale), (int) (1422 * scale)};
        int[] apLen = {(int) (225 * scale), (int) (556 * scale)};
        float[][] comb = new float[4][];
        int[] ci = new int[4];
        double[] damp = new double[4];
        for (int i = 0; i < 4; i++) comb[i] = new float[Math.Max(1, combLen[i])];
        float[][] ap = {new float[Math.Max(1, apLen[0])], new float[Math.Max(1, apLen[1])]};
        int[] ai = new int[2];
        double fb = 0.80, dampK = 0.25;
        for (int n = 0; n < x.Length; n++) {
            double @in = x[n] * 0.25;
            double sum = 0;
            for (int i = 0; i < 4; i++) {
                float[] c = comb[i];
                double y = c[ci[i]];
                damp[i] = y * (1 - dampK) + damp[i] * dampK;
                c[ci[i]] = (float) (@in + damp[i] * fb);
                if (++ci[i] >= c.Length) ci[i] = 0;
                sum += y;
            }
            for (int i = 0; i < 2; i++) {
                float[] a = ap[i];
                double b = a[ai[i]];
                double y = -sum + b;
                a[ai[i]] = (float) (sum + b * 0.5);
                if (++ai[i] >= a.Length) ai[i] = 0;
                sum = y;
            }
            x[n] = (float) (x[n] * (1 - wet * 0.5) + sum * wet);
        }
    }

    /** Normaliza por sonoridad (RMS de las partes activas) con limitador suave para los picos. */
    private static void normalize(float[] x, float target) {
        float mx = 0;
        double e = 0;
        int cnt = 0;
        foreach (float v in x) {
            float a = Math.Abs(v);
            if (a > mx) mx = a;
            if (a > 1e-4f) { e += v * v; cnt++; }
        }
        if (mx < 1e-6f || cnt == 0) return;
        double rms = Math.Sqrt(e / cnt);
        double g = target / mx;                          // ganancia por pico
        double gr = 0.2 / rms;                           // ganancia por sonoridad
        if (gr > g) g = Math.Min(gr, g * 3.0);
        double knee = 0.6;
        for (int i = 0; i < x.Length; i++) {
            double v = x[i] * g;
            double a = Math.Abs(v);
            if (a > knee) {
                a = knee + (1 - knee) * Math.Tanh((a - knee) / (1 - knee));
                v = JU.Signum(v) * a * 0.98;
            }
            x[i] = (float) v;
        }
    }
}
}
