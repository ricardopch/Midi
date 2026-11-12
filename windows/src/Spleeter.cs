using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /**
     * Separación de fuentes con Spleeter 4stems (Deezer, 2019, licencia MIT): voz, batería, bajo y resto.
     * Reimplementación propia de la inferencia (cuatro U-Net sobre el espectrograma de 4096 puntos con máscaras
     * de proporción), validada contra TensorFlow. El modelo oficial se descarga la primera vez de la página de
     * Spleeter en GitHub y se lee directamente del checkpoint de TensorFlow.
     */
    internal sealed class Spleeter
    {
        public const string ModelUrl = "https://github.com/deezer/spleeter/releases/download/v1.4.0/4stems.tar.gz";
        private const string ModelSha256 = "3adb4a50ad4eb18c7c4d65fcf4cf2367a07d48408a5eb7d03cd20067429dfaa8";
        private const string IndexName = "model.index";
        private const string DataName = "model.data-00000-of-00001";
        private const long IndexSize = 10368, DataSize = 157228152;

        public const int Vocals = 0, Drums = 1, Bass = 2, Other = 3;
        public static readonly string[] Names = { "voz", "batería", "bajo", "resto" };

        private const int N = 4096, Hop = 1024, T = 512, F = 1024, Bins = N / 2 + 1;

        private static Spleeter cached;
        private static readonly object CacheLock = new object();

        private readonly UNet[] nets = new UNet[4];

        public static string ModelDir
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("MELODIA_SPLEETER_DIR");    // para pruebas
                return !string.IsNullOrEmpty(over) ? over : Path.Combine(MuScriptor.AppDir, "spleeter");
            }
        }

        public static bool ModelReady()
        {
            try
            {
                FileInfo i = new FileInfo(Path.Combine(ModelDir, IndexName)), d = new FileInfo(Path.Combine(ModelDir, DataName));
                return i.Exists && d.Exists && i.Length == IndexSize && d.Length == DataSize;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ descarga

        /** Descarga y descomprime el modelo oficial (146 MB) si no está ya en la carpeta del programa. */
        public static void EnsureModel(Action<string> status, IProgress p)
        {
            if (ModelReady()) return;
            Directory.CreateDirectory(ModelDir);
            string tgz = Path.Combine(ModelDir, "4stems.tar.gz.part");
            string local = Environment.GetEnvironmentVariable("MELODIA_SPLEETER_TGZ");    // para pruebas
            if (!string.IsNullOrEmpty(local)) File.Copy(local, tgz, true);
            else Download(tgz, status, p);
            status("Comprobando el modelo…");
            using (SHA256 sha = SHA256.Create())
            using (FileStream f = File.OpenRead(tgz))
            {
                string h = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (h != ModelSha256)
                {
                    f.Close();
                    File.Delete(tgz);
                    throw new IOException("El modelo descargado no es el esperado (descarga dañada). Vuelve a intentarlo.");
                }
            }
            status("Descomprimiendo el modelo…");
            using (FileStream f = File.OpenRead(tgz))
            using (GZipStream gz = new GZipStream(f, CompressionMode.Decompress))
                ExtractTar(gz, ModelDir, new[] { IndexName, DataName });
            File.Delete(tgz);
            if (!ModelReady()) throw new IOException("No se pudo preparar el modelo de separación.");
        }

        private static void Download(string tgz, Action<string> status, IProgress p)
        {
            status("Descargando el modelo de separación (146 MB, solo la primera vez)…");
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch (Exception) { }
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ModelUrl);
            req.UserAgent = "MelodiaMIDI";
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream f = File.Create(tgz))
            {
                long total = resp.ContentLength, done = 0;
                byte[] buf = new byte[1 << 16];
                int n, tick = 0;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (p.isCancelled()) throw new CancelledException();
                    f.Write(buf, 0, n);
                    done += n;
                    if (total > 0 && (++tick & 31) == 0)
                    {
                        p.update((float)done / total);
                        status("Descargando el modelo de separación… " + (done >> 20) + " de " + (total >> 20) + " MB");
                    }
                }
            }
        }

        /** Extrae del tar solo los archivos pedidos (por nombre, sin carpeta). */
        private static void ExtractTar(Stream s, string dir, string[] wanted)
        {
            byte[] hdr = new byte[512];
            while (ReadFully(s, hdr, 512))
            {
                bool empty = true;
                for (int i = 0; i < 512; i++) if (hdr[i] != 0) { empty = false; break; }
                if (empty) break;
                string name = CString(hdr, 0, 100);
                long size = Convert.ToInt64(CString(hdr, 124, 12).Trim().Trim('\0').PadLeft(1, '0'), 8);
                char type = (char)hdr[156];
                string baseName = name.Replace('\\', '/');
                int slash = baseName.LastIndexOf('/');
                if (slash >= 0) baseName = baseName.Substring(slash + 1);
                bool keep = (type == '0' || type == '\0') && Array.IndexOf(wanted, baseName) >= 0;
                long padded = (size + 511) / 512 * 512;
                if (keep)
                {
                    string tmp = Path.Combine(dir, baseName + ".tmp");
                    using (FileStream o = File.Create(tmp)) Copy(s, o, size);
                    Skip(s, padded - size);
                    string dst = Path.Combine(dir, baseName);
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(tmp, dst);
                }
                else Skip(s, padded);
            }
        }

        private static string CString(byte[] b, int off, int len)
        {
            int n = 0;
            while (n < len && b[off + n] != 0) n++;
            return System.Text.Encoding.ASCII.GetString(b, off, n);
        }

        private static bool ReadFully(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        private static void Copy(Stream s, Stream o, long n)
        {
            byte[] buf = new byte[1 << 16];
            while (n > 0)
            {
                int r = s.Read(buf, 0, (int)Math.Min(buf.Length, n));
                if (r <= 0) throw new EndOfStreamException();
                o.Write(buf, 0, r);
                n -= r;
            }
        }

        private static void Skip(Stream s, long n)
        {
            byte[] buf = new byte[1 << 16];
            while (n > 0)
            {
                int r = s.Read(buf, 0, (int)Math.Min(buf.Length, n));
                if (r <= 0) return;
                n -= r;
            }
        }

        // ------------------------------------------------------------------ carga del checkpoint de TensorFlow

        public static Spleeter Load()
        {
            lock (CacheLock)
            {
                if (cached == null) cached = new Spleeter(ModelDir);
                return cached;
            }
        }

        private Spleeter(string dir)
        {
            Dictionary<string, long[]> index = ReadIndex(File.ReadAllBytes(Path.Combine(dir, IndexName)));
            using (FileStream data = File.OpenRead(Path.Combine(dir, DataName)))
            {
                for (int k = 0; k < 4; k++) nets[k] = new UNet(index, data, k);
            }
        }

        /** Índice del checkpoint (tabla SSTable sin compresión): nombre -> {desplazamiento, tamaño, dims...}. */
        private static Dictionary<string, long[]> ReadIndex(byte[] b)
        {
            int p = b.Length - 48;
            ReadVarint(b, ref p); ReadVarint(b, ref p);                    // metaindex
            int io = (int)ReadVarint(b, ref p), isz = (int)ReadVarint(b, ref p);
            Dictionary<string, long[]> res = new Dictionary<string, long[]>();
            foreach (KeyValuePair<string, byte[]> kv in ReadBlock(b, io, isz))
            {
                int q = 0;
                int bo = (int)ReadVarint(kv.Value, ref q), bs = (int)ReadVarint(kv.Value, ref q);
                foreach (KeyValuePair<string, byte[]> e in ReadBlock(b, bo, bs))
                {
                    if (e.Key.Length == 0) continue;                       // cabecera del bundle
                    res[e.Key] = ParseEntry(e.Value);
                }
            }
            return res;
        }

        private static List<KeyValuePair<string, byte[]>> ReadBlock(byte[] b, int off, int size)
        {
            if (b[off + size] != 0) throw new IOException("Checkpoint comprimido: no soportado.");
            int nrest = BitConverter.ToInt32(b, off + size - 4);
            int end = off + size - 4 - 4 * nrest;
            List<KeyValuePair<string, byte[]>> l = new List<KeyValuePair<string, byte[]>>();
            byte[] key = new byte[0];
            int p = off;
            while (p < end)
            {
                int shared = (int)ReadVarint(b, ref p), ns = (int)ReadVarint(b, ref p), vl = (int)ReadVarint(b, ref p);
                byte[] k = new byte[shared + ns];
                Array.Copy(key, k, shared);
                Array.Copy(b, p, k, shared, ns);
                p += ns;
                byte[] v = new byte[vl];
                Array.Copy(b, p, v, 0, vl);
                p += vl;
                key = k;
                l.Add(new KeyValuePair<string, byte[]>(System.Text.Encoding.UTF8.GetString(k), v));
            }
            return l;
        }

        /** BundleEntryProto: dtype(1), shape(2), shard(3), offset(4), size(5). */
        private static long[] ParseEntry(byte[] v)
        {
            long offset = 0, size = 0, dtype = 0;
            List<long> dims = new List<long>();
            int p = 0;
            while (p < v.Length)
            {
                long tag = ReadVarint(v, ref p);
                int field = (int)(tag >> 3), wt = (int)(tag & 7);
                if (wt == 0)
                {
                    long x = ReadVarint(v, ref p);
                    if (field == 1) dtype = x;
                    else if (field == 4) offset = x;
                    else if (field == 5) size = x;
                }
                else if (wt == 2)
                {
                    int len = (int)ReadVarint(v, ref p);
                    if (field == 2)
                    {
                        int q = p, qe = p + len;
                        while (q < qe)
                        {
                            long t2 = ReadVarint(v, ref q);
                            if ((t2 & 7) == 2)
                            {
                                int dl = (int)ReadVarint(v, ref q), de = q + dl;
                                if ((t2 >> 3) == 2)
                                {
                                    while (q < de)
                                    {
                                        long t3 = ReadVarint(v, ref q);
                                        if ((t3 & 7) == 0) { long d = ReadVarint(v, ref q); if ((t3 >> 3) == 1) dims.Add(d); }
                                        else if ((t3 & 7) == 2) q += (int)ReadVarint(v, ref q);
                                        else break;
                                    }
                                }
                                q = de;
                            }
                            else if ((t2 & 7) == 0) ReadVarint(v, ref q);
                            else break;
                        }
                    }
                    p += len;
                }
                else if (wt == 5) p += 4;
                else if (wt == 1) p += 8;
                else break;
            }
            long[] r = new long[3 + dims.Count];
            r[0] = offset; r[1] = size; r[2] = dtype;
            for (int i = 0; i < dims.Count; i++) r[3 + i] = dims[i];
            return r;
        }

        private static long ReadVarint(byte[] b, ref int p)
        {
            long r = 0;
            int s = 0;
            while (true)
            {
                int c = b[p++];
                r |= (long)(c & 0x7F) << s;
                if (c < 0x80) return r;
                s += 7;
            }
        }

        internal static float[] ReadTensor(Dictionary<string, long[]> index, FileStream data, string name, out int[] dims)
        {
            long[] e;
            if (!index.TryGetValue(name, out e)) throw new IOException("Falta " + name + " en el modelo de separación.");
            if (e[2] != 1) throw new IOException("Tipo de dato inesperado en " + name);
            dims = new int[e.Length - 3];
            for (int i = 0; i < dims.Length; i++) dims[i] = (int)e[3 + i];
            byte[] raw = new byte[e[1]];
            data.Seek(e[0], SeekOrigin.Begin);
            if (!ReadFully(data, raw, raw.Length)) throw new IOException("Modelo de separación incompleto.");
            float[] f = new float[raw.Length / 4];
            Buffer.BlockCopy(raw, 0, f, 0, raw.Length);                   // little-endian, como Windows
            return f;
        }

        // ------------------------------------------------------------------ separación

        /** y: audio mono a 44100 Hz. Devuelve las 4 pistas (voz, batería, bajo, resto) a 44100 Hz. */
        public float[][] Separate(float[] y, int length, IProgress p)
        {
            int padLen = length + N;                                   // Spleeter antepone N ceros
            int nfr = (padLen + Hop - 1) / Hop;
            int patches = (nfr + T - 1) / T;
            float[][] stems = new float[4][];
            for (int k = 0; k < 4; k++) stems[k] = new float[length];
            float[] win = Hann(N);
            Fft fft = new Fft(N);
            float[] re = new float[T * Bins], im = new float[T * Bins];
            float[] x = new float[T * F * 2];
            float[][] est = new float[4][];
            int steps = patches * 5, step = 0;
            for (int pt = 0; pt < patches; pt++)
            {
                int f0 = pt * T, fn = Math.Min(T, nfr - f0);
                // STFT del trozo
                Parallel.For(0, fn, delegate (int t)
                {
                    float[] r = new float[N], i = new float[N];
                    int start = (f0 + t) * Hop - N;
                    for (int n = 0; n < N; n++)
                    {
                        int s = start + n;
                        r[n] = s >= 0 && s < length ? y[s] * win[n] : 0f;
                    }
                    fft.Forward(r, i);
                    Array.Copy(r, 0, re, t * Bins, Bins);
                    Array.Copy(i, 0, im, t * Bins, Bins);
                });
                Array.Clear(x, 0, x.Length);
                for (int t = 0; t < fn; t++)
                    for (int f = 0; f < F; f++)
                    {
                        float a = re[t * Bins + f], b = im[t * Bins + f];
                        float m = (float)Math.Sqrt(a * a + b * b);
                        x[(t * F + f) * 2] = m;
                        x[(t * F + f) * 2 + 1] = m;
                    }
                for (int k = 0; k < 4; k++)
                {
                    if (p.isCancelled()) throw new CancelledException();
                    est[k] = nets[k].Forward(x);
                    p.update((float)(++step) / steps);
                }
                // máscaras de proporción (exponente 2) y síntesis por solapamiento
                float[][] masks = new float[4][];
                for (int k = 0; k < 4; k++) masks[k] = new float[fn * F];
                Parallel.For(0, fn, delegate (int t)
                {
                    for (int f = 0; f < F; f++)
                    {
                        float m0 = 0, m1 = 0, m2 = 0, m3 = 0;
                        for (int c = 0; c < 2; c++)
                        {
                            int idx = (t * F + f) * 2 + c;
                            float e0 = est[0][idx], e1 = est[1][idx], e2 = est[2][idx], e3 = est[3][idx];
                            float sum = e0 * e0 + e1 * e1 + e2 * e2 + e3 * e3 + 1e-10f;
                            m0 += (e0 * e0 + 2.5e-11f) / sum;
                            m1 += (e1 * e1 + 2.5e-11f) / sum;
                            m2 += (e2 * e2 + 2.5e-11f) / sum;
                            m3 += (e3 * e3 + 2.5e-11f) / sum;
                        }
                        masks[0][t * F + f] = 0.5f * m0;
                        masks[1][t * F + f] = 0.5f * m1;
                        masks[2][t * F + f] = 0.5f * m2;
                        masks[3][t * F + f] = 0.5f * m3;
                    }
                });
                Parallel.For(0, 4, delegate (int k)
                {
                    float[] r = new float[N], i = new float[N];
                    Fft local = new Fft(N);
                    float[] dst = stems[k];
                    for (int t = 0; t < fn; t++)
                    {
                        for (int f = 0; f < Bins; f++)
                        {
                            float m = f < F ? masks[k][t * F + f] : 0f;
                            r[f] = re[t * Bins + f] * m;
                            i[f] = im[t * Bins + f] * m;
                        }
                        local.InverseReal(r, i);                       // r = señal real del cuadro
                        int start = (f0 + t) * Hop - N;
                        for (int n = 0; n < N; n++)
                        {
                            int s = start + n;
                            if (s >= 0 && s < length) dst[s] += r[n] * win[n] * (2f / 3f);
                        }
                    }
                });
                p.update((float)(++step) / steps);
            }
            return stems;
        }

        private static float[] Hann(int n)
        {
            float[] w = new float[n];
            for (int i = 0; i < n; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
            return w;
        }

        // ------------------------------------------------------------------ U-Net

        private sealed class Layer
        {
            public int O, C, K;
            public float[] W, B, S, Tb;      // W: O x K; S/Tb: BatchNorm posterior (escala, desplazamiento)
        }

        private sealed class UNet
        {
            private readonly Layer[] enc = new Layer[6];
            private readonly Layer[][] dec = new Layer[6][];        // 4 clases de paridad (fila, columna)
            private readonly float[][] decS = new float[6][], decT = new float[6][], decB = new float[6][];
            private readonly int[] decO = new int[6], decC = new int[6];
            private readonly Layer last;

            private static string Name(string b, int i) { return i == 0 ? b : b + "_" + i; }

            public UNet(Dictionary<string, long[]> index, FileStream data, int k)
            {
                int[] d;
                for (int i = 0; i < 6; i++)
                {
                    string n = Name("conv2d", 7 * k + i);
                    float[] w = ReadTensor(index, data, n + "/kernel", out d);   // (5, 5, C, O)
                    Layer L = new Layer { C = d[2], O = d[3], K = 25 * d[2] };
                    L.W = new float[L.O * L.K];
                    for (int kt = 0; kt < 5; kt++)
                        for (int kf = 0; kf < 5; kf++)
                            for (int c = 0; c < L.C; c++)
                                for (int o = 0; o < L.O; o++)
                                    L.W[o * L.K + (kt * 5 + kf) * L.C + c] = w[((kt * 5 + kf) * L.C + c) * L.O + o];
                    L.B = ReadTensor(index, data, n + "/bias", out d);
                    BatchNorm(index, data, 12 * k + i, out L.S, out L.Tb);
                    enc[i] = L;
                }
                for (int i = 0; i < 6; i++)
                {
                    string n = Name("conv2d_transpose", 6 * k + i);
                    float[] w = ReadTensor(index, data, n + "/kernel", out d);   // (5, 5, O, C)
                    int O = d[2], C = d[3];
                    decO[i] = O;
                    decC[i] = C;
                    dec[i] = new Layer[4];
                    for (int cls = 0; cls < 4; cls++)
                    {
                        int[] kts = Taps(cls >> 1), kfs = Taps(cls & 1);
                        Layer L = new Layer { O = O, C = C, K = kts.Length * kfs.Length * C };
                        L.W = new float[O * L.K];
                        for (int a = 0; a < kts.Length; a++)
                            for (int b = 0; b < kfs.Length; b++)
                                for (int c = 0; c < C; c++)
                                    for (int o = 0; o < O; o++)
                                        L.W[o * L.K + (a * kfs.Length + b) * C + c] = w[((kts[a] * 5 + kfs[b]) * O + o) * C + c];
                        dec[i][cls] = L;
                    }
                    decB[i] = ReadTensor(index, data, n + "/bias", out d);
                    BatchNorm(index, data, 12 * k + 6 + i, out decS[i], out decT[i]);
                }
                {
                    string n = Name("conv2d", 7 * k + 6);
                    float[] w = ReadTensor(index, data, n + "/kernel", out d);   // (4, 4, 1, 2)
                    Layer L = new Layer { C = 1, O = 2, K = 16 };
                    L.W = new float[32];
                    for (int a = 0; a < 16; a++)
                        for (int o = 0; o < 2; o++) L.W[o * 16 + a] = w[a * 2 + o];
                    L.B = ReadTensor(index, data, n + "/bias", out d);
                    last = L;
                }
            }

            /** Posiciones del núcleo que llegan a una salida de paridad dada: par -> {1, 3}, impar -> {0, 2, 4}. */
            private static int[] Taps(int parity) { return parity == 0 ? new[] { 1, 3 } : new[] { 0, 2, 4 }; }

            private static void BatchNorm(Dictionary<string, long[]> index, FileStream data, int i, out float[] s, out float[] t)
            {
                string n = Name("batch_normalization", i);
                int[] d;
                float[] g = ReadTensor(index, data, n + "/gamma", out d), b = ReadTensor(index, data, n + "/beta", out d);
                float[] m = ReadTensor(index, data, n + "/moving_mean", out d), v = ReadTensor(index, data, n + "/moving_variance", out d);
                s = new float[g.Length];
                t = new float[g.Length];
                for (int c = 0; c < g.Length; c++)
                {
                    s[c] = (float)(g[c] / Math.Sqrt(v[c] + 1e-3));
                    t[c] = b[c] - m[c] * s[c];
                }
            }

            /** x: (512, 1024, 2) magnitudes. Devuelve el espectrograma estimado del instrumento (mismo tamaño). */
            public float[] Forward(float[] x)
            {
                float[][] convs = new float[6][];
                float[] h = x;
                int t = T, f = F, c = 2;
                for (int i = 0; i < 6; i++)
                {
                    float[] co = ConvS2(h, t, f, c, enc[i]);
                    t /= 2; f /= 2; c = enc[i].O;
                    convs[i] = co;
                    if (i < 5)
                    {
                        float[] a = new float[co.Length];
                        BnElu(co, a, enc[i].S, enc[i].Tb, c);
                        h = a;
                    }
                }
                h = convs[5];
                for (int i = 0; i < 6; i++)
                {
                    float[] u = DeconvS2(h, t, f, i);
                    t *= 2; f *= 2;
                    int O = decO[i];
                    EluBn(u, decS[i], decT[i], O);
                    if (i < 5)
                    {
                        float[] skip = convs[4 - i];
                        int cs = enc[4 - i].O;
                        float[] m = new float[t * f * (cs + O)];
                        for (int q = 0; q < t * f; q++)
                        {
                            Array.Copy(skip, q * cs, m, q * (cs + O), cs);
                            Array.Copy(u, q * O, m, q * (cs + O) + cs, O);
                        }
                        h = m;
                        c = cs + O;
                    }
                    else { h = u; c = O; }
                }
                float[] o2 = ConvLast(h, t, f);
                for (int q = 0; q < o2.Length; q++) o2[q] = (float)(1.0 / (1.0 + Math.Exp(-o2[q]))) * x[q];
                return o2;
            }

            private static void BnElu(float[] src, float[] dst, float[] s, float[] t, int C)
            {
                for (int q = 0; q < src.Length; q++)
                {
                    int c = q % C;
                    float v = src[q] * s[c] + t[c];
                    dst[q] = v > 0 ? v : (float)(Math.Exp(v) - 1.0);
                }
            }

            private static void EluBn(float[] u, float[] s, float[] t, int C)
            {
                for (int q = 0; q < u.Length; q++)
                {
                    float v = u[q];
                    v = v > 0 ? v : (float)(Math.Exp(v) - 1.0);
                    int c = q % C;
                    u[q] = v * s[c] + t[c];
                }
            }

            /** Conv2D 5x5 de paso 2 con relleno 'same' (1 antes, 2 después). */
            private static float[] ConvS2(float[] x, int Ti, int Fi, int C, Layer L)
            {
                int To = Ti / 2, Fo = Fi / 2, O = L.O, K = L.K;
                float[] y = new float[To * Fo * O];
                Parallel.For(0, To, () => new float[4 * K], delegate (int t, ParallelLoopState st, float[] patch)
                {
                    for (int f0 = 0; f0 < Fo; f0 += 4)
                    {
                        int nb = Math.Min(4, Fo - f0);
                        for (int j = 0; j < 4; j++)
                        {
                            int f = f0 + Math.Min(j, nb - 1);
                            int k = j * K;
                            for (int kt = 0; kt < 5; kt++)
                            {
                                int ti = 2 * t + kt - 1;
                                for (int kf = 0; kf < 5; kf++, k += C)
                                {
                                    int fi = 2 * f + kf - 1;
                                    if (ti < 0 || ti >= Ti || fi < 0 || fi >= Fi) Array.Clear(patch, k, C);
                                    else Array.Copy(x, (ti * Fi + fi) * C, patch, k, C);
                                }
                            }
                        }
                        Gemm4(patch, K, L.W, L.B, O, y, (t * Fo + f0) * O, O, nb);
                    }
                    return patch;
                }, delegate (float[] patch) { });
                return y;
            }

            /** Conv2D traspuesta 5x5 de paso 2 ('same'): salida (2T, 2F, O), por clases de paridad. */
            private float[] DeconvS2(float[] x, int Ti, int Fi, int li)
            {
                int To = 2 * Ti, Fo = 2 * Fi, O = decO[li], C = decC[li];
                float[] bias = decB[li];
                float[] y = new float[To * Fo * O];
                int maxK = 9 * C;
                Parallel.For(0, To, () => new float[4 * maxK], delegate (int yy, ParallelLoopState st, float[] patch)
                {
                    int py = yy & 1;
                    int[] kts = Taps(py);
                    for (int px = 0; px < 2; px++)
                    {
                        Layer L = dec[li][py * 2 + px];
                        int[] kfs = Taps(px);
                        int K = L.K;
                        int count = Fi;                             // columnas con esta paridad: px, px+2, …
                        for (int g = 0; g < count; g += 4)
                        {
                            int nb = Math.Min(4, count - g);
                            for (int j = 0; j < 4; j++)
                            {
                                int xx = 2 * (g + Math.Min(j, nb - 1)) + px;
                                int k = j * K;
                                for (int a = 0; a < kts.Length; a++)
                                {
                                    int ti = (yy + 1 - kts[a]) / 2;
                                    bool rowOk = ti >= 0 && ti < Ti;
                                    for (int b = 0; b < kfs.Length; b++, k += C)
                                    {
                                        int fi = (xx + 1 - kfs[b]) / 2;
                                        if (!rowOk || fi < 0 || fi >= Fi) Array.Clear(patch, k, C);
                                        else Array.Copy(x, (ti * Fi + fi) * C, patch, k, C);
                                    }
                                }
                            }
                            // las 4 columnas de salida no son contiguas (paso 2): se escriben aparte
                            Gemm4(patch, K, L.W, bias, O, y, (yy * Fo + 2 * g + px) * O, 2 * O, nb);
                        }
                    }
                    return patch;
                }, delegate (float[] patch) { });
                return y;
            }

            /** Última capa: Conv2D 4x4 con dilatación 2, 'same' (relleno 3), C = 1, O = 2. */
            private float[] ConvLast(float[] x, int Ti, int Fi)
            {
                float[] y = new float[Ti * Fi * 2];
                float[] W = last.W, B = last.B;
                Parallel.For(0, Ti, delegate (int t)
                {
                    float[] patch = new float[16];
                    for (int f = 0; f < Fi; f++)
                    {
                        int k = 0;
                        for (int a = 0; a < 4; a++)
                        {
                            int ti = t + 2 * a - 3;
                            for (int b = 0; b < 4; b++, k++)
                            {
                                int fi = f + 2 * b - 3;
                                patch[k] = ti < 0 || ti >= Ti || fi < 0 || fi >= Fi ? 0f : x[ti * Fi + fi];
                            }
                        }
                        float s0 = B[0], s1 = B[1];
                        for (k = 0; k < 16; k++) { s0 += patch[k] * W[k]; s1 += patch[k] * W[16 + k]; }
                        y[(t * Fi + f) * 2] = s0;
                        y[(t * Fi + f) * 2 + 1] = s1;
                    }
                });
                return y;
            }

            /**
             * Bloque de nb (≤ 4) píxeles por todas las salidas: y[yo + j*stride + o] = B[o] + Σ_k patch[j*K + k] * W[o*K + k].
             */
            private static unsafe void Gemm4(float[] patch, int K, float[] W, float[] B, int O, float[] y, int yo, int stride, int nb)
            {
                fixed (float* P = patch, Wp = W, Y = y)
                {
                    float* p0 = P, p1 = P + K, p2 = P + 2 * K, p3 = P + 3 * K;
                    int o = 0;
                    for (; o + 2 <= O; o += 2)
                    {
                        float* w0 = Wp + o * K, w1 = w0 + K;
                        float a00 = 0, a01 = 0, a02 = 0, a03 = 0, a10 = 0, a11 = 0, a12 = 0, a13 = 0;
                        for (int k = 0; k < K; k++)
                        {
                            float v0 = w0[k], v1 = w1[k];
                            float q0 = p0[k], q1 = p1[k], q2 = p2[k], q3 = p3[k];
                            a00 += q0 * v0; a01 += q1 * v0; a02 += q2 * v0; a03 += q3 * v0;
                            a10 += q0 * v1; a11 += q1 * v1; a12 += q2 * v1; a13 += q3 * v1;
                        }
                        float b0 = B[o], b1 = B[o + 1];
                        Y[yo + o] = a00 + b0; Y[yo + o + 1] = a10 + b1;
                        if (nb > 1) { Y[yo + stride + o] = a01 + b0; Y[yo + stride + o + 1] = a11 + b1; }
                        if (nb > 2) { Y[yo + 2 * stride + o] = a02 + b0; Y[yo + 2 * stride + o + 1] = a12 + b1; }
                        if (nb > 3) { Y[yo + 3 * stride + o] = a03 + b0; Y[yo + 3 * stride + o + 1] = a13 + b1; }
                    }
                    for (; o < O; o++)
                    {
                        float* w0 = Wp + o * K;
                        float a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                        for (int k = 0; k < K; k++)
                        {
                            float v = w0[k];
                            a0 += p0[k] * v; a1 += p1[k] * v; a2 += p2[k] * v; a3 += p3[k] * v;
                        }
                        float b = B[o];
                        Y[yo + o] = a0 + b;
                        if (nb > 1) Y[yo + stride + o] = a1 + b;
                        if (nb > 2) Y[yo + 2 * stride + o] = a2 + b;
                        if (nb > 3) Y[yo + 3 * stride + o] = a3 + b;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ FFT

        internal sealed class Fft
        {
            private readonly int n;
            private readonly float[] cos, sin;
            private readonly int[] rev;

            public Fft(int size)
            {
                n = size;
                cos = new float[n / 2];
                sin = new float[n / 2];
                for (int i = 0; i < n / 2; i++)
                {
                    cos[i] = (float)Math.Cos(-2 * Math.PI * i / n);
                    sin[i] = (float)Math.Sin(-2 * Math.PI * i / n);
                }
                rev = new int[n];
                int bits = 0;
                while ((1 << bits) < n) bits++;
                for (int i = 0; i < n; i++)
                {
                    int r = 0;
                    for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                    rev[i] = r;
                }
            }

            /** FFT compleja in situ, exponente negativo. */
            public void Forward(float[] re, float[] im)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = rev[i];
                    if (j > i)
                    {
                        float t = re[i]; re[i] = re[j]; re[j] = t;
                        t = im[i]; im[i] = im[j]; im[j] = t;
                    }
                }
                for (int size = 2; size <= n; size <<= 1)
                {
                    int half = size >> 1, step = n / size;
                    for (int s = 0; s < n; s += size)
                        for (int k = 0; k < half; k++)
                        {
                            float wr = cos[k * step], wi = sin[k * step];
                            int a = s + k, b = a + half;
                            float tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                            re[b] = re[a] - tr; im[b] = im[a] - ti;
                            re[a] += tr; im[a] += ti;
                        }
                }
            }

            /** Inversa de un espectro de señal real dado en las bandas 0..n/2; deja la señal en re. */
            public void InverseReal(float[] re, float[] im)
            {
                for (int k = 1; k < n / 2; k++)
                {
                    re[n - k] = re[k];
                    im[n - k] = -im[k];
                }
                im[0] = 0;
                im[n / 2] = 0;
                for (int i = 0; i < n; i++) im[i] = -im[i];
                Forward(re, im);
                float s = 1f / n;
                for (int i = 0; i < n; i++) re[i] *= s;
            }
        }
    }
}
