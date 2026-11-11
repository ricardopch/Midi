package com.ricardopch.midi.engine;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * Extrae la melodía principal de una mezcla de audio.
 *
 * 1) STFT y picos espectrales. 2) Función de saliencia por suma de armónicos.
 * 3) Seguimiento de la línea más saliente con Viterbi (continuidad + estado sin voz).
 * 4) Segmentación en notas MIDI (histéresis de tono, cortes por caída de nivel).
 */
public final class MelodyExtractor {
    public static final int SAMPLE_RATE = 22050;
    public static final int FRAME = 2048;
    public static final int HOP = 256;

    static final double FMIN = 82.4068892282175;    // Mi2 (MIDI 40)
    static final int BPO = 60;                        // bins por octava (20 cents)
    static final int NB = 282;                        // hasta ~2.1 kHz
    static final int HARM = 16;
    static final double KW = 2.0;                     // semiancho del núcleo (bins)

    // Parámetros ajustables
    public double alpha = 0.8;            // peso armónico
    public double comp = 0.6;             // compresión de amplitud
    public double peakDb = 45;            // umbral de picos bajo el máximo de la trama
    public double priorLowHz = 120;       // registro típico de la melodía (meseta)
    public double priorHighHz = 1000;
    public double priorEdgeOct = 0.7;
    public double priorFloor = 0.3;
    public double priorTilt = 0.2;         // preferencia por voces agudas (por octava)
    public double lowCutHz = 1;         // atenúa graves (bajo, bombo) al sumar armónicos
    public double lowCutOrder = 2;
    public double refPercentile = 0.90;
    public double voicingThreshold = 0.05;
    public double localSlope = 0.02;      // coste por bin en cambios pequeños
    public int localWidth = 6;            // bins (120 cents)
    public double jumpCost = 0.6;         // coste fijo de un salto
    public double farSlope = 0.01;        // coste extra por bin en saltos
    public double voicingCost = 0.5;      // coste de entrar/salir de silencio
    public double hysteresis = 0.7;       // semitonos para cambiar de nota
    public int persistFrames = 3;
    public int minNoteFrames = 6;         // ~70 ms
    public double dipDb = 8.0;            // caída de nivel para re-ataque
    public double riseDb = 8.0;           // subida brusca para re-ataque
    public double minLevelDb = -24;       // notas más débiles se descartan
    public double contextSec = 0;        // contexto (s) para corregir octavas; 0 = desactivado
    public boolean keepDebug = false;
    public float[] debugSal;

    public static final class Result {
        public final List<Note> notes;
        public final float[] f0;          // Hz por trama (0 = sin voz)
        public final double frameRate;

        Result(List<Note> notes, float[] f0, double frameRate) {
            this.notes = notes;
            this.f0 = f0;
            this.frameRate = frameRate;
        }
    }

    public Result extract(float[] x, Progress p) {
        if (p == null) p = Progress.NONE;
        final int n = x.length;
        final int T = n / HOP + 1;
        final double frameRate = (double) SAMPLE_RATE / HOP;
        float[] sal = new float[T * NB];

        // ---------- 1+2: STFT y saliencia ----------
        double[] win = new double[FRAME];
        for (int i = 0; i < FRAME; i++) win[i] = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / FRAME);
        Fft fft = new Fft(FRAME);
        double[] re = new double[FRAME], im = new double[FRAME];
        double[] magA = new double[FRAME / 2 + 1], magB = new double[FRAME / 2 + 1];
        final double binHz = (double) SAMPLE_RATE / FRAME;
        final int kLo = Math.max(2, (int) Math.ceil(55 / binHz));
        final int kHi = Math.min(FRAME / 2 - 2, (int) (5500 / binHz));
        double[] harmW = new double[HARM + 1];
        double[] harmLog = new double[HARM + 1];
        for (int h = 1; h <= HARM; h++) {
            harmW[h] = Math.pow(alpha, h - 1);
            harmLog[h] = BPO * Math.log(h) / Math.log(2);
        }
        final double peakThr = Math.pow(10, -peakDb / 20);
        final int mask = FRAME - 1;
        for (int t = 0; t < T; t += 2) {
            if ((t & 63) == 0) {
                if (p.isCancelled()) throw new Progress.Cancelled();
                p.update(0.8f * t / T);
            }
            fillFrame(x, t, win, re);
            if (t + 1 < T) fillFrame(x, t + 1, win, im);
            else Arrays.fill(im, 0);
            fft.transform(re, im);
            for (int k = 0; k <= FRAME / 2; k++) {
                int kk = (FRAME - k) & mask;
                double zr = re[k], zi = im[k], wr = re[kk], wi = im[kk];
                double ar = zr + wr, ai = zi - wi;
                double br = zr - wr, bi = zi + wi;
                magA[k] = 0.5 * Math.sqrt(ar * ar + ai * ai);
                magB[k] = 0.5 * Math.sqrt(br * br + bi * bi);
            }
            salienceFrame(magA, sal, t * NB, kLo, kHi, binHz, peakThr, harmW, harmLog);
            if (t + 1 < T) salienceFrame(magB, sal, (t + 1) * NB, kLo, kHi, binHz, peakThr, harmW, harmLog);
        }

        // ---------- referencia global y prior de registro ----------
        float[] fmax = new float[T];
        int nz = 0;
        for (int t = 0; t < T; t++) {
            float m = 0;
            int o = t * NB;
            for (int b = 0; b < NB; b++) if (sal[o + b] > m) m = sal[o + b];
            fmax[t] = m;
            if (m > 0) nz++;
        }
        List<Note> notes = new ArrayList<Note>();
        float[] f0 = new float[T];
        if (nz == 0) return new Result(notes, f0, frameRate);
        float[] sorted = new float[nz];
        int si = 0;
        for (int t = 0; t < T; t++) if (fmax[t] > 0) sorted[si++] = fmax[t];
        Arrays.sort(sorted);
        double R = sorted[Math.min(nz - 1, (int) (refPercentile * nz))];
        if (R <= 0) return new Result(notes, f0, frameRate);

        double[] prior = new double[NB];
        double lo = Math.log(priorLowHz / FMIN) / Math.log(2), hi = Math.log(priorHighHz / FMIN) / Math.log(2);
        for (int b = 0; b < NB; b++) {
            double oct = (double) b / BPO;
            double d = oct < lo ? (lo - oct) / priorEdgeOct : (oct > hi ? (oct - hi) / priorEdgeOct : 0);
            prior[b] = (priorFloor + (1 - priorFloor) * Math.exp(-0.5 * d * d)) * Math.pow(2, priorTilt * (oct - hi));
        }
        double invR = 1.0 / R;
        for (int t = 0; t < T; t++) {
            int o = t * NB;
            for (int b = 0; b < NB; b++) sal[o + b] = (float) (sal[o + b] * invR * prior[b]);
        }

        if (keepDebug) debugSal = sal;
        // ---------- 3: Viterbi ----------
        int[] state = viterbi(sal, T, p);

        // ---------- 4: tono fino y nivel ----------
        double[] midi = new double[T];
        double[] level = new double[T];
        boolean[] voiced = new boolean[T];
        for (int t = 0; t < T; t++) {
            int b = state[t];
            if (b >= NB) continue;
            int o = t * NB;
            double y1 = sal[o + b];
            if (y1 < voicingThreshold) continue;
            double delta = 0;
            if (b > 0 && b < NB - 1) {
                double y0 = sal[o + b - 1], y2 = sal[o + b + 1];
                double den = y0 - 2 * y1 + y2;
                if (den < 0) delta = 0.5 * (y0 - y2) / den;
                if (delta > 0.5) delta = 0.5;
                if (delta < -0.5) delta = -0.5;
            }
            midi[t] = 40.0 + (b + delta) * 12.0 / BPO;
            level[t] = y1 / prior[b];
            voiced[t] = true;
            f0[t] = (float) Note.midiToHz(midi[t]);
        }
        p.update(0.95f);
        double[] ldb = harmonicLevelDb(x, midi, voiced, T);
        notes = segment(midi, level, ldb, voiced, T, frameRate);
        p.update(1f);
        return new Result(notes, f0, frameRate);
    }

    /** Nivel (dB) de los armónicos de la melodía con ventana corta (23 ms), para detectar re-ataques. */
    private static double[] harmonicLevelDb(float[] x, double[] midi, boolean[] voiced, int T) {
        final int N2 = 512;
        double[] win = new double[N2];
        for (int i = 0; i < N2; i++) win[i] = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / N2);
        Fft fft = new Fft(N2);
        double[] re = new double[N2], im = new double[N2];
        double[] out = new double[T];
        Arrays.fill(out, -120);
        double bin = (double) SAMPLE_RATE / N2;
        int n = x.length;
        for (int t = 0; t < T; t++) {
            if (!voiced[t]) continue;
            int start = t * HOP - N2 / 2;
            for (int i = 0; i < N2; i++) {
                int j = start + i;
                re[i] = (j >= 0 && j < n) ? x[j] * win[i] : 0.0;
                im[i] = 0;
            }
            fft.transform(re, im);
            double f0 = Note.midiToHz(midi[t]);
            double acc = 0;
            for (int h = 1; h <= 8; h++) {
                int k = (int) Math.round(h * f0 / bin);
                if (k + 1 >= N2 / 2) break;
                double m = 0;
                for (int kk = Math.max(1, k - 1); kk <= k + 1; kk++) {
                    double v = re[kk] * re[kk] + im[kk] * im[kk];
                    if (v > m) m = v;
                }
                acc += Math.sqrt(m) / h;
            }
            out[t] = 20 * Math.log10(acc + 1e-9);
        }
        // mediana de 3 para quitar picos aislados
        double[] sm = new double[T];
        for (int t = 0; t < T; t++) {
            double a = out[Math.max(0, t - 1)], b = out[t], c = out[Math.min(T - 1, t + 1)];
            sm[t] = Math.max(Math.min(a, b), Math.min(Math.max(a, b), c));
        }
        return sm;
    }

    private static void fillFrame(float[] x, int t, double[] win, double[] out) {
        int start = t * HOP - FRAME / 2;
        int n = x.length;
        for (int i = 0; i < FRAME; i++) {
            int j = start + i;
            out[i] = (j >= 0 && j < n) ? x[j] * win[i] : 0.0;
        }
    }

    private void salienceFrame(double[] mag, float[] sal, int off, int kLo, int kHi, double binHz,
                               double peakThr, double[] harmW, double[] harmLog) {
        double mx = 0;
        for (int k = kLo; k <= kHi; k++) if (mag[k] > mx) mx = mag[k];
        if (mx <= 1e-7) return;
        double thr = mx * peakThr;
        final double log2 = Math.log(2);
        for (int k = kLo; k <= kHi; k++) {
            double m = mag[k];
            if (m <= thr || m <= mag[k - 1] || m < mag[k + 1]) continue;
            double la = Math.log(mag[k - 1] + 1e-12), lb = Math.log(m), lc = Math.log(mag[k + 1] + 1e-12);
            double den = la - 2 * lb + lc;
            double d = den < 0 ? 0.5 * (la - lc) / den : 0;
            if (d > 0.5) d = 0.5;
            if (d < -0.5) d = -0.5;
            double f = (k + d) * binHz;
            double amp = Math.exp(lb - 0.25 * (la - lc) * d);
            double a = Math.pow(amp, comp) / (1 + Math.pow(lowCutHz / f, lowCutOrder));
            double pos0 = BPO * Math.log(f / FMIN) / log2;
            for (int h = 1; h <= HARM; h++) {
                double pos = pos0 - harmLog[h];
                if (pos < -KW) break;
                if (pos > NB - 1 + KW) continue;
                double wa = a * harmW[h];
                int b0 = (int) Math.ceil(pos - KW), b1 = (int) Math.floor(pos + KW);
                if (b0 < 0) b0 = 0;
                if (b1 > NB - 1) b1 = NB - 1;
                for (int b = b0; b <= b1; b++) {
                    double dd = (b - pos) / KW;
                    double w = 1 - dd * dd;
                    if (w > 0) sal[off + b] += (float) (wa * w);
                }
            }
        }
    }

    /** Devuelve por trama el bin de tono elegido o NB si es silencio. */
    private int[] viterbi(float[] sal, int T, Progress p) {
        final int U = NB;
        final double gU = Math.sqrt(voicingThreshold);
        double[] prev = new double[NB], cur = new double[NB], G = new double[NB];
        int[] argG = new int[NB];
        short[] bp = new short[T * NB];
        short[] bpU = new short[T];
        for (int b = 0; b < NB; b++) prev[b] = obs(sal[b]);
        double prevU = gU;
        final double farOffset = farSlope * localWidth - localSlope * localWidth - jumpCost;
        for (int t = 1; t < T; t++) {
            if ((t & 255) == 0) {
                if (p.isCancelled()) throw new Progress.Cancelled();
                p.update(0.8f + 0.15f * t / T);
            }
            int bestB = 0;
            double bestV = prev[0];
            for (int b = 1; b < NB; b++) if (prev[b] > bestV) { bestV = prev[b]; bestB = b; }
            // transformada de distancia L1 (saltos grandes)
            G[0] = prev[0];
            argG[0] = 0;
            for (int b = 1; b < NB; b++) {
                double c = G[b - 1] - farSlope;
                if (prev[b] >= c) { G[b] = prev[b]; argG[b] = b; } else { G[b] = c; argG[b] = argG[b - 1]; }
            }
            for (int b = NB - 2; b >= 0; b--) {
                double c = G[b + 1] - farSlope;
                if (c > G[b]) { G[b] = c; argG[b] = argG[b + 1]; }
            }
            int o = t * NB;
            double fromU = prevU - voicingCost;
            double norm = Math.max(bestV, prevU);
            for (int b = 0; b < NB; b++) {
                double best = Double.NEGATIVE_INFINITY;
                int arg = b;
                int j0 = Math.max(0, b - localWidth), j1 = Math.min(NB - 1, b + localWidth);
                for (int j = j0; j <= j1; j++) {
                    double v = prev[j] - localSlope * Math.abs(b - j);
                    if (v > best) { best = v; arg = j; }
                }
                double far = G[b] + farOffset;
                if (far > best) { best = far; arg = argG[b]; }
                if (fromU > best) { best = fromU; arg = U; }
                cur[b] = best + obs(sal[o + b]) - norm;
                bp[o + b] = (short) arg;
            }
            double toU = bestV - voicingCost;
            double curU;
            if (prevU >= toU) { curU = prevU + gU - norm; bpU[t] = (short) U; }
            else { curU = toU + gU - norm; bpU[t] = (short) bestB; }
            double[] tmp = prev; prev = cur; cur = tmp;
            prevU = curU;
        }
        int[] state = new int[T];
        int s = U;
        double best = prevU;
        for (int b = 0; b < NB; b++) if (prev[b] > best) { best = prev[b]; s = b; }
        state[T - 1] = s;
        for (int t = T - 1; t > 0; t--) {
            s = (s == U) ? bpU[t] : bp[t * NB + s];
            state[t - 1] = s;
        }
        return state;
    }

    private static double obs(float s) {
        double v = s;
        if (v <= 0) return 0;
        if (v > 4) v = 4;
        return Math.sqrt(v);
    }

    // ---------------- segmentación en notas ----------------

    private List<Note> segment(double[] midi, double[] level, double[] ldb, boolean[] voiced, int T, double frameRate) {
        // mediana de 5 tramas sobre tramas con voz
        double[] ms = new double[T];
        double[] tmp = new double[5];
        for (int t = 0; t < T; t++) {
            if (!voiced[t]) continue;
            int c = 0;
            for (int k = t - 2; k <= t + 2; k++) if (k >= 0 && k < T && voiced[k]) tmp[c++] = midi[k];
            Arrays.sort(tmp, 0, c);
            ms[t] = (c % 2 == 1) ? tmp[c / 2] : 0.5 * (tmp[c / 2 - 1] + tmp[c / 2]);
        }
        // afinación global (media circular de la parte fraccionaria)
        double C = 0, S = 0, W = 0;
        for (int t = 0; t < T; t++) {
            if (!voiced[t]) continue;
            double frac = ms[t] - Math.rint(ms[t]);
            double w = Math.min(level[t], 2.0);
            C += w * Math.cos(2 * Math.PI * frac);
            S += w * Math.sin(2 * Math.PI * frac);
            W += w;
        }
        double tuning = 0;
        if (W > 0 && Math.sqrt(C * C + S * S) / W > 0.2) tuning = Math.atan2(S, C) / (2 * Math.PI);


        // segmentos por histéresis de tono
        List<int[]> segs = new ArrayList<int[]>();   // {inicio, fin(excl), corteBrusco}
        int t = 0;
        while (t < T) {
            if (!voiced[t]) { t++; continue; }
            int s = t;
            double cur = Math.rint(ms[t] - tuning);
            t++;
            while (t < T && voiced[t]) {
                double v = ms[t] - tuning;
                if (Math.abs(v - cur) > hysteresis) {
                    double nq = Math.rint(v);
                    int k = t, cnt = 0;
                    while (k < T && voiced[k] && Math.abs(ms[k] - tuning - nq) < 0.6 && cnt < persistFrames) { k++; cnt++; }
                    if (cnt >= persistFrames) {
                        segs.add(new int[]{s, t, 0});
                        s = t;
                        cur = nq;
                    }
                }
                t++;
            }
            segs.add(new int[]{s, t, 0});
        }

        // segmentos cortos: unir al vecino contiguo de tono más parecido
        boolean changed = true;
        while (changed) {
            changed = false;
            for (int i = 0; i < segs.size(); i++) {
                int[] g = segs.get(i);
                if (g[1] - g[0] >= minNoteFrames) continue;
                int[] pv = i > 0 ? segs.get(i - 1) : null;
                int[] nx = i + 1 < segs.size() ? segs.get(i + 1) : null;
                boolean pc = pv != null && pv[1] == g[0];
                boolean nc = nx != null && nx[0] == g[1];
                double mg = median(ms, g[0], g[1]);
                if (pc && (!nc || Math.abs(median(ms, pv[0], pv[1]) - mg) <= Math.abs(median(ms, nx[0], nx[1]) - mg))) {
                    pv[1] = g[1];
                } else if (nc) {
                    nx[0] = g[0];
                } // si no es contiguo a nada, se descarta
                segs.remove(i);
                changed = true;
                break;
            }
        }

        // re-ataques: caídas de nivel dentro de un segmento
        List<int[]> split = new ArrayList<int[]>();
        for (int[] g : segs) {
            int s = g[0];
            for (int i = g[0] + minNoteFrames; i < g[1] - minNoteFrames; i++) {
                // subida brusca (nota repetida en instrumentos de cuerda pulsada / percutidos)
                if (riseDb > 0 && i - s >= minNoteFrames) {
                    int km = i - 1;
                    for (int k = i - 3; k < i; k++) if (ldb[k] < ldb[km]) km = k;
                    if (ldb[i] - ldb[km] >= riseDb && ldb[i] >= ldb[i + 1] - 1) {
                        int cut = km + 1;
                        if (cut - s >= minNoteFrames) {
                            split.add(new int[]{s, cut, s == g[0] ? g[2] : 1});
                            s = cut;
                            i = cut + minNoteFrames - 1;
                            continue;
                        }
                    }
                }
                if (ldb[i] > ldb[i - 1] || ldb[i] > ldb[i + 1]) continue;
                double lm = -200, rm = -200;
                for (int k = Math.max(s, i - 15); k < i; k++) lm = Math.max(lm, ldb[k]);
                for (int k = i + 1; k <= Math.min(g[1] - 1, i + 10); k++) rm = Math.max(rm, ldb[k]);
                if (lm - ldb[i] >= dipDb && rm - ldb[i] >= dipDb && i - s >= minNoteFrames) {
                    split.add(new int[]{s, i, s == g[0] ? g[2] : 1});
                    s = i;
                }
            }
            split.add(new int[]{s, g[1], s == g[0] ? g[2] : 1});
        }

        // notas
        List<Note> notes = new ArrayList<Note>();
        List<int[]> keep = new ArrayList<int[]>();
        for (int[] g : split) {
            if (g[1] - g[0] < minNoteFrames) continue;
            double pm = median(ms, g[0], g[1]) - tuning;
            int pitch = (int) Math.rint(pm);
            double lv = 0;
            for (int k = g[0]; k < g[1]; k++) lv += level[k];
            lv /= (g[1] - g[0]);
            double db = 20 * Math.log10(lv + 1e-9);
            if (db < minLevelDb) continue;
            float vel = (float) Math.max(0.45, Math.min(1.0, 0.8 + db / 40.0));
            Note nt = new Note(pitch, g[0] / frameRate, g[1] / frameRate, vel);
            // unir con la anterior si es el mismo tono, contigua y no hubo re-ataque
            if (!notes.isEmpty()) {
                Note last = notes.get(notes.size() - 1);
                int[] lg = keep.get(keep.size() - 1);
                if (last.pitch == pitch && g[0] - lg[1] <= 2 && g[2] == 0) {
                    last.end = nt.end;
                    lg[1] = g[1];
                    continue;
                }
            }
            notes.add(nt);
            keep.add(new int[]{g[0], g[1], g[2]});
        }

        // errores de octava aislados
        for (int i = 1; i + 1 < notes.size(); i++) {
            Note a = notes.get(i - 1), b = notes.get(i), c = notes.get(i + 1);
            if (b.duration() > 0.25 || b.start - a.end > 0.15 || c.start - b.end > 0.15) continue;
            if (Math.abs(a.pitch - c.pitch) > 5) continue;
            int ref = (a.pitch + c.pitch) / 2;
            int d = b.pitch - ref;
            if (Math.abs(d) >= 10) {
                int shifted = b.pitch - 12 * (int) Math.rint(d / 12.0);
                if (Math.abs(shifted - ref) <= 5) b.pitch = shifted;
            }
        }
        // notas a una octava de su contexto (±2 s): corregir la octava
        int[] orig = new int[notes.size()];
        for (int i = 0; i < orig.length; i++) orig[i] = notes.get(i).pitch;
        for (int i = 0; contextSec > 0 && i < orig.length; i++) {
            Note b = notes.get(i);
            List<Integer> ctx = new ArrayList<Integer>();
            for (int j = 0; j < orig.length; j++) {
                if (j == i) continue;
                Note o = notes.get(j);
                if (o.end > b.start - contextSec && o.start < b.end + contextSec) ctx.add(orig[j]);
            }
            if (ctx.size() < 3) continue;
            java.util.Collections.sort(ctx);
            double med = ctx.size() % 2 == 1 ? ctx.get(ctx.size() / 2) : 0.5 * (ctx.get(ctx.size() / 2 - 1) + ctx.get(ctx.size() / 2));
            double d = orig[i] - med;
            if (Math.abs(d) >= 10) {
                int shifted = orig[i] - 12 * (int) Math.signum(d);
                if (Math.abs(shifted - med) <= 7) b.pitch = shifted;
            }
        }
        // rango razonable
        List<Note> out = new ArrayList<Note>();
        for (Note nt : notes) if (nt.pitch >= 28 && nt.pitch <= 108) out.add(nt);
        return out;
    }

    private static double median(double[] v, int a, int b) {
        int n = b - a;
        if (n <= 0) return 0;
        double[] c = Arrays.copyOfRange(v, a, b);
        Arrays.sort(c);
        return (n % 2 == 1) ? c[n / 2] : 0.5 * (c[n / 2 - 1] + c[n / 2]);
    }
}
