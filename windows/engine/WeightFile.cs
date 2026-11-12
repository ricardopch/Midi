// Generado desde el motor Java con tools/java2cs.py. No editar a mano.
using System;
using System.Collections.Generic;
using System.IO;

namespace MelodiaMidi.Engine
{
/**
 * Archivo de pesos de Melodía MIDI (generado por tools/export_basic_pitch.py).
 * Formato big-endian: "MLDY" | versión | n | tipo | n x [nombre | f32? | ndim | dims | datos (float16 o float32)].
 */
public sealed class WeightFile {
    public string kind;

    public bool isKind(string k) { return kind != null && (kind == k); }
    private readonly Dictionary<string, float[]> map = new Dictionary<string, float[]>();

    public static WeightFile parse(byte[] b){
        WeightFile w = new WeightFile();
        int[] pos = new int[1];
        if (b.Length < 16 || b[0] != 77 || b[1] != 76 || b[2] != 68 || b[3] != 89) throw new IOException("Pesos no válidos");
        pos[0] = 4;
        int version = readInt(b, pos);
        if (version != 1) throw new IOException("Versión de pesos no soportada");
        int n = readInt(b, pos);
        w.kind = readName(b, pos);
        for (int t = 0; t < n; t++) {
            string name = readName(b, pos);
            bool f32 = b[pos[0]] != 0;
            pos[0] += 1;
            int nd = readInt(b, pos);
            int size = 1;
            for (int i = 0; i < nd; i++) size *= readInt(b, pos);
            float[] a = new float[size];
            int p = pos[0];
            if (f32) {
                for (int i = 0; i < size; i++, p += 4) {
                    int bits = ((b[p] & 0xFF) << 24) | ((b[p + 1] & 0xFF) << 16) | ((b[p + 2] & 0xFF) << 8) | (b[p + 3] & 0xFF);
                    a[i] = JU.IntBitsToFloat(bits);
                }
            } else {
                for (int i = 0; i < size; i++, p += 2) a[i] = halfToFloat(((b[p] & 0xFF) << 8) | (b[p + 1] & 0xFF));
            }
            pos[0] = p;
            w.map[name] = a;
        }
        return w;
    }

    public float[] tensor(string name) {
        if (!map.ContainsKey(name)) throw new InvalidOperationException("Falta el tensor " + name);
        return map[name];
    }

    public float scalar(string name) { return tensor(name)[0]; }

    /** Libera un tensor que ya no se necesita. */
    public void drop(string name) { map.Remove(name); }

    private static int readInt(byte[] b, int[] pos) {
        int p = pos[0];
        pos[0] = p + 4;
        return ((b[p] & 0xFF) << 24) | ((b[p + 1] & 0xFF) << 16) | ((b[p + 2] & 0xFF) << 8) | (b[p + 3] & 0xFF);
    }

    private static string readName(byte[] b, int[] pos) {
        int p = pos[0];
        int len = ((b[p] & 0xFF) << 8) | (b[p + 1] & 0xFF);
        char[] c = new char[len];
        for (int i = 0; i < len; i++) c[i] = (char) (b[p + 2 + i] & 0xFF);
        pos[0] = p + 2 + len;
        return new string(c);
    }

    static float halfToFloat(int h) {
        int s = (h >> 15) & 1, e = (h >> 10) & 0x1F, f = h & 0x3FF;
        double v;
        if (e == 0) v = f * Math.Pow(2, -24);
        else if (e == 31) v = 65504.0;
        else v = (1 + f / 1024.0) * Math.Pow(2, e - 15);
        return (float) (s == 1 ? -v : v);
    }
}
}
