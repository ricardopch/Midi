package com.ricardopch.midi.engine;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * Transcripción polifónica "todo lo que suena" con el modelo Basic Pitch (Spotify, 2022, licencia Apache 2.0).
 * Reimplementación propia de la inferencia: CQT de 9 octavas, log normalizado, apilado de 8 armónicos y
 * 6 convoluciones; después, el algoritmo de creación de notas de Basic Pitch (ataques + seguimiento de energía).
 *
 * Uso: prepare() con audio mono a 22050 Hz; runWindow() para cada ventana de 2 s (independientes, se pueden
 * repartir entre hilos con un Workspace por hilo); notes() al final.
 */
public final class BasicPitch {
    public static final int SR = 22050;
    public static final int HOP = 256;
    public static final int WIN = 43844;            // muestras por ventana
    public static final int FR = 172;               // tramas por ventana
    public static final int OLAP = 15;              // tramas descartadas a cada lado
    public static final int KEEP = FR - 2 * OLAP;   // 142 tramas útiles por ventana
    public static final int KEYS = 88;              // MIDI 21..108
    public static final int CB = 264;               // bandas de contorno (3 por semitono)
    public static final int QB = 309;               // bandas CQT
    public static final double FPS = SR / (double) HOP;
    private static final int[] SHIFTS = {-36, 0, 36, 57, 72, 84, 93, 101};

    private final float[] cqtR, cqtI, lowpass, cqtScale;
    private final float bnMul, bnAdd;
    private final float[] c1k, c1b, c2k, c2b, n1k, n1b, n2k, n2b, o1k, o1b, o2k, o2b;

    private float[] audio;
    private int frames;

    public BasicPitch(WeightFile w) {
        if (!w.isKind("bp")) throw new IllegalStateException("Modelo no válido: " + w.kind);
        cqtR = w.tensor("cqt_r");
        cqtI = w.tensor("cqt_i");
        lowpass = w.tensor("lowpass");
        cqtScale = w.tensor("cqt_scale");
        bnMul = w.scalar("bn_mul");
        bnAdd = w.scalar("bn_add");
        c1k = w.tensor("cont1_k"); c1b = w.tensor("cont1_b");
        c2k = w.tensor("cont2_k"); c2b = w.tensor("cont2_b");
        n1k = w.tensor("note1_k"); n1b = w.tensor("note1_b");
        n2k = w.tensor("note2_k"); n2b = w.tensor("note2_b");
        o1k = w.tensor("onset1_k"); o1b = w.tensor("onset1_b");
        o2k = w.tensor("onset2_k"); o2b = w.tensor("onset2_b");
    }

    /** Audio mono a 22050 Hz. */
    public void prepare(float[] x) {
        audio = x;
        frames = x.length / HOP + 1;
    }

    public int frameCount() { return frames; }

    public int windowCount() { return (frames + KEEP - 1) / KEEP; }

    public final class Workspace {
        public final float[] x = new float[WIN];
        public final float[] d1 = new float[WIN / 2 + 8];
        public final float[] d2 = new float[WIN / 2 + 8];
        public final float[] pad = new float[WIN + 256];
        public final float[] mag = new float[FR * QB];
        public final float[] st = new float[8 * FR * CB];
        public final float[] c1 = new float[8 * FR * CB];
        public final float[] contour = new float[FR * CB];
        public final float[] n1 = new float[32 * FR * KEYS];
        public final float[] cat = new float[33 * FR * KEYS];     // canal 0 = notas, 1..32 = ataques
        public final float[] onset = new float[FR * KEYS];
    }

    public Workspace newWorkspace() { return new Workspace(); }

    /** Procesa la ventana w y escribe sus 142 tramas útiles en note/onset (frames x 88). */
    public void runWindow(int w, Workspace ws, float[] noteOut, float[] onsetOut) {
        // 1) audio de la ventana (con 3840 ceros delante del principio de la canción)
        int start = w * KEEP * HOP - OLAP * HOP;
        float[] x = ws.x;
        int n = audio.length;
        for (int i = 0; i < WIN; i++) {
            int j = start + i;
            x[i] = (j >= 0 && j < n) ? audio[j] : 0f;
        }
        // 2) CQT: 9 octavas, de la más aguda a la más grave, submuestreando por 2 cada vez
        float[] cur = x;
        int len = WIN;
        int hop = HOP;
        for (int o = 0; o < 9; o++) {
            if (o > 0) {
                hop /= 2;
                float[] dst = (o % 2 == 1) ? ws.d1 : ws.d2;
                len = downsample(cur, len, dst);
                cur = dst;
            }
            // relleno por reflexión de 128 a cada lado
            float[] p = ws.pad;
            for (int i = 0; i < 128; i++) p[i] = cur[128 - i];
            System.arraycopy(cur, 0, p, 128, len);
            for (int i = 0; i < 128; i++) p[128 + len + i] = cur[len - 2 - i];
            int plen = len + 256;
            int nf = (plen - 256) / hop + 1;
            if (nf > FR) nf = FR;
            for (int k = 0; k < 36; k++) {
                int kb = k * 256;
                for (int t = 0; t < nf; t++) {
                    int pb = t * hop;
                    double sr = 0, si = 0;
                    for (int i = 0; i < 256; i++) {
                        float v = p[pb + i];
                        sr += v * cqtR[kb + i];
                        si += v * cqtI[kb + i];
                    }
                    int bin = (8 - o) * 36 + k - 15;          // octavas graves primero; se quitan las 15 bandas inferiores
                    if (bin >= 0) {
                        float s = cqtScale[bin];
                        ws.mag[t * QB + bin] = (float) (Math.abs(s) * Math.sqrt(sr * sr + si * si));
                    }
                }
                for (int t = nf; t < FR; t++) {
                    int bin = (8 - o) * 36 + k - 15;
                    if (bin >= 0) ws.mag[t * QB + bin] = 0f;
                }
            }
        }
        // 3) potencia en dB normalizada a [0,1] en la ventana, y BatchNorm
        float mn = Float.POSITIVE_INFINITY, mx = Float.NEGATIVE_INFINITY;
        float[] mag = ws.mag;
        for (int i = 0; i < FR * QB; i++) {
            float m = mag[i];
            float lp = (float) (10 * Math.log10(m * m + 1e-10));
            mag[i] = lp;
            if (lp < mn) mn = lp;
            if (lp > mx) mx = lp;
        }
        float range = mx - mn;
        for (int i = 0; i < FR * QB; i++) {
            float v = range != 0 ? (mag[i] - mn) / range : 0f;
            mag[i] = v * bnMul + bnAdd;
        }
        // 4) apilado de armónicos: 8 canales x 172 x 264
        float[] st = ws.st;
        for (int c = 0; c < 8; c++) {
            int s = SHIFTS[c];
            for (int t = 0; t < FR; t++) {
                int o = (c * FR + t) * CB;
                for (int f = 0; f < CB; f++) {
                    int q = f + s;
                    st[o + f] = (q >= 0 && q < QB) ? mag[t * QB + q] : 0f;
                }
            }
        }
        // 5) red
        conv(st, 8, FR, CB, c1k, c1b, 8, 3, 39, 1, 1, 19, ws.c1, CB);
        relu(ws.c1, 8 * FR * CB);
        conv(ws.c1, 8, FR, CB, c2k, c2b, 1, 5, 5, 1, 2, 2, ws.contour, CB);
        sigmoid(ws.contour, FR * CB);
        conv(ws.contour, 1, FR, CB, n1k, n1b, 32, 7, 7, 3, 3, 2, ws.n1, KEYS);
        relu(ws.n1, 32 * FR * KEYS);
        conv(ws.n1, 32, FR, KEYS, n2k, n2b, 1, 7, 3, 1, 3, 1, ws.cat, KEYS);      // notas -> canal 0
        sigmoid(ws.cat, FR * KEYS);
        conv(st, 8, FR, CB, o1k, o1b, 32, 5, 5, 3, 2, 1, ws.n1, KEYS);           // ataques (reutiliza n1)
        relu(ws.n1, 32 * FR * KEYS);
        System.arraycopy(ws.n1, 0, ws.cat, FR * KEYS, 32 * FR * KEYS);
        conv(ws.cat, 33, FR, KEYS, o2k, o2b, 1, 3, 3, 1, 1, 1, ws.onset, KEYS);
        sigmoid(ws.onset, FR * KEYS);
        // 6) tramas útiles
        for (int t = OLAP; t < OLAP + KEEP; t++) {
            int fr = w * KEEP + (t - OLAP);
            if (fr >= frames) break;
            System.arraycopy(ws.cat, t * KEYS, noteOut, fr * KEYS, KEYS);
            System.arraycopy(ws.onset, t * KEYS, onsetOut, fr * KEYS, KEYS);
        }
    }

    /** Filtro paso bajo (256 coeficientes) con 127 ceros a cada lado y paso 2. Devuelve la nueva longitud. */
    private int downsample(float[] x, int len, float[] out) {
        int n = (len + 254 - 256) / 2 + 1;
        for (int t = 0; t < n; t++) {
            int b = 2 * t - 127;
            double s = 0;
            int i0 = Math.max(0, -b), i1 = Math.min(256, len - b);
            for (int i = i0; i < i1; i++) s += x[b + i] * lowpass[i];
            out[t] = (float) s;
        }
        return n;
    }

    /**
     * Conv2D con canales delante. x: C x T x F; k: O x C x kt x kf; relleno pt (tiempo) y pf (frecuencia, izquierda);
     * paso sf en frecuencia; la salida conserva T y tiene Fo bandas.
     */
    private static void conv(float[] x, int C, int T, int F, float[] k, float[] b, int O, int kt, int kf, int sf,
                             int pt, int pf, float[] out, int Fo) {
        for (int o = 0; o < O; o++) {
            int ob = o * T * Fo;
            Arrays.fill(out, ob, ob + T * Fo, b[o]);
            for (int c = 0; c < C; c++)
                for (int i = 0; i < kt; i++)
                    for (int j = 0; j < kf; j++) {
                        float kv = k[((o * C + c) * kt + i) * kf + j];
                        if (kv == 0f) continue;
                        // bandas de salida válidas: 0 <= sf*fo + j - pf < F
                        int lo = Math.max(0, (pf - j + sf - 1) / sf);
                        int hi = Math.min(Fo - 1, (F - 1 + pf - j) / sf);
                        if (hi < lo) continue;
                        for (int t = 0; t < T; t++) {
                            int ti = t + i - pt;
                            if (ti < 0 || ti >= T) continue;
                            int ib = (c * T + ti) * F + j - pf;
                            int orow = ob + t * Fo;
                            if (sf == 1) {
                                for (int fo = lo; fo <= hi; fo++) out[orow + fo] += kv * x[ib + fo];
                            } else {
                                for (int fo = lo; fo <= hi; fo++) out[orow + fo] += kv * x[ib + sf * fo];
                            }
                        }
                    }
        }
    }

    private static void relu(float[] a, int n) {
        for (int i = 0; i < n; i++) if (a[i] < 0) a[i] = 0;
    }

    private static void sigmoid(float[] a, int n) {
        for (int i = 0; i < n; i++) a[i] = (float) (1.0 / (1.0 + Math.exp(-a[i])));
    }

    // ------------------------------------------------------------------ notas

    /** Ajustes de la creación de notas (los de Basic Pitch por defecto). */
    public static final class Settings {
        public float onsetThreshold = 0.5f;
        public float frameThreshold = 0.3f;
        public int minNoteFrames = 11;
        public int energyTolerance = 11;
        public int minMidi = 21;
        public int maxMidi = 108;

        /** 0 = menos notas (valores originales de Basic Pitch), 1 = normal, 2 = más notas. */
        public static Settings forSensitivity(int level) {
            Settings s = new Settings();
            if (level <= 0) { s.onsetThreshold = 0.5f; s.frameThreshold = 0.3f; s.minNoteFrames = 11; }
            else if (level == 1) { s.onsetThreshold = 0.42f; s.frameThreshold = 0.26f; s.minNoteFrames = 8; }
            else { s.onsetThreshold = 0.35f; s.frameThreshold = 0.22f; s.minNoteFrames = 6; }
            return s;
        }
    }

    /** Convierte las salidas (frames x 88) en notas. note/onset se modifican. */
    public static List<Note> notes(float[] note, float[] onset, int T, Settings s) {
        int P = KEYS;
        for (int t = 0; t < T; t++)
            for (int p = 0; p < P; p++)
                if (p + 21 < s.minMidi || p + 21 > s.maxMidi) { note[t * P + p] = 0; onset[t * P + p] = 0; }
        // ataques deducidos de subidas bruscas en las tramas
        float[] diff = new float[T * P];
        float maxOnset = 0, maxDiff = 0;
        for (int i = 0; i < T * P; i++) if (onset[i] > maxOnset) maxOnset = onset[i];
        for (int t = 2; t < T; t++)
            for (int p = 0; p < P; p++) {
                float d1 = note[t * P + p] - note[(t - 1) * P + p];
                float d2 = note[t * P + p] - note[(t - 2) * P + p];
                float d = Math.min(d1, d2);
                if (d < 0) d = 0;
                diff[t * P + p] = d;
                if (d > maxDiff) maxDiff = d;
            }
        float[] on = new float[T * P];
        for (int i = 0; i < T * P; i++) {
            float d = maxDiff > 0 ? maxOnset * diff[i] / maxDiff : 0;
            on[i] = Math.max(onset[i], d);
        }
        float[] rem = Arrays.copyOf(note, T * P);
        List<float[]> ev = new ArrayList<float[]>();
        // picos de ataque, del final al principio (y de agudo a grave)
        for (int t = T - 2; t >= 1; t--)
            for (int p = P - 1; p >= 0; p--) {
                float v = on[t * P + p];
                if (v < s.onsetThreshold || !(v > on[(t - 1) * P + p] && v > on[(t + 1) * P + p])) continue;
                int i = t + 1, k = 0;
                while (i < T - 1 && k < s.energyTolerance) {
                    if (rem[i * P + p] < s.frameThreshold) k++; else k = 0;
                    i++;
                }
                i -= k;
                if (i - t <= s.minNoteFrames) continue;
                double amp = 0;
                for (int q = t; q < i; q++) {
                    amp += note[q * P + p];
                    rem[q * P + p] = 0;
                    if (p < P - 1) rem[q * P + p + 1] = 0;
                    if (p > 0) rem[q * P + p - 1] = 0;
                }
                ev.add(new float[]{t, i, p + 21, (float) (amp / (i - t))});
            }
        // "truco de Melodia": notas sin ataque claro, de mayor a menor energía
        int cnt = 0;
        for (int i = 0; i < T * P; i++) if (rem[i] > s.frameThreshold) cnt++;
        int[] order = new int[cnt];
        float[] vals = new float[cnt];
        cnt = 0;
        for (int i = 0; i < T * P; i++) if (rem[i] > s.frameThreshold) { order[cnt] = i; vals[cnt] = rem[i]; cnt++; }
        sortDescending(order, vals, cnt);
        for (int q = 0; q < cnt; q++) {
            int idx = order[q];
            if (rem[idx] <= s.frameThreshold) continue;   // ya consumida (los valores solo se ponen a 0)
            int im = idx / P, p = idx % P;
            rem[idx] = 0;
            int i = im + 1, k = 0;
            while (i < T - 1 && k < s.energyTolerance) {
                if (rem[i * P + p] < s.frameThreshold) k++; else k = 0;
                clear(rem, i, p, P);
                i++;
            }
            int ie = i - 1 - k;
            i = im - 1;
            k = 0;
            while (i > 0 && k < s.energyTolerance) {
                if (rem[i * P + p] < s.frameThreshold) k++; else k = 0;
                clear(rem, i, p, P);
                i--;
            }
            int is = i + 1 + k;
            if (ie - is <= s.minNoteFrames) continue;
            double amp = 0;
            for (int t = is; t < ie; t++) amp += note[t * P + p];
            ev.add(new float[]{is, ie, p + 21, (float) (amp / (ie - is))});
        }
        List<Note> out = new ArrayList<Note>();
        for (float[] e : ev) {
            float vel = Math.max(0.3f, Math.min(1f, e[3]));
            out.add(new Note((int) e[2], e[0] / FPS, e[1] / FPS, vel));
        }
        sortByStart(out);
        return out;
    }

    private static void clear(float[] rem, int i, int p, int P) {
        rem[i * P + p] = 0;
        if (p < P - 1) rem[i * P + p + 1] = 0;
        if (p > 0) rem[i * P + p - 1] = 0;
    }

    /** Ordenación por valor descendente e índice ascendente (heapsort, sin objetos). */
    private static void sortDescending(int[] idx, float[] v, int n) {
        for (int i = n / 2 - 1; i >= 0; i--) siftDown(idx, v, i, n);
        for (int end = n - 1; end > 0; end--) {
            swap(idx, v, 0, end);
            siftDown(idx, v, 0, end);
        }
    }

    private static void siftDown(int[] idx, float[] v, int i, int n) {
        while (true) {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < n && later(idx, v, l, m)) m = l;
            if (r < n && later(idx, v, r, m)) m = r;
            if (m == i) return;
            swap(idx, v, i, m);
            i = m;
        }
    }

    private static boolean later(int[] idx, float[] v, int a, int b) {
        return v[a] < v[b] || (v[a] == v[b] && idx[a] > idx[b]);
    }

    private static void swap(int[] idx, float[] v, int a, int b) {
        int ti = idx[a]; idx[a] = idx[b]; idx[b] = ti;
        float tv = v[a]; v[a] = v[b]; v[b] = tv;
    }

    private static void sortByStart(List<Note> l) {
        Note[] a = new Note[l.size()];
        for (int i = 0; i < a.length; i++) a[i] = l.get(i);
        mergeSort(a, new Note[a.length], 0, a.length);
        l.clear();
        for (Note n : a) l.add(n);
    }

    private static void mergeSort(Note[] a, Note[] tmp, int lo, int hi) {
        if (hi - lo < 2) return;
        int mid = (lo + hi) / 2;
        mergeSort(a, tmp, lo, mid);
        mergeSort(a, tmp, mid, hi);
        int i = lo, j = mid, k = lo;
        while (i < mid && j < hi) {
            boolean takeRight = a[j].start < a[i].start || (a[j].start == a[i].start && a[j].pitch < a[i].pitch);
            tmp[k++] = takeRight ? a[j++] : a[i++];
        }
        while (i < mid) tmp[k++] = a[i++];
        while (j < hi) tmp[k++] = a[j++];
        for (k = lo; k < hi; k++) a[k] = tmp[k];
    }
}
