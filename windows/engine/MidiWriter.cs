// Generado desde el motor Java con tools/java2cs.py. No editar a mano.
using System;
using System.Collections.Generic;
using System.IO;

namespace MelodiaMidi.Engine
{
/**
 * Archivo MIDI estándar (SMF tipo 0) con las notas en el canal 1.
 * 480 pulsos por negra a 120 ppm, es decir, 960 pulsos por segundo: los tiempos coinciden con el MP4.
 */
public sealed class MidiWriter {
    private const int PPQ = 480;
    private const double TICKS_PER_SECOND = 960.0;

    private byte[] buf = new byte[4096];
    private int len;

    /** gmProgram: instrumento General MIDI (0-127), por ejemplo Synth.GM_PROGRAM[i]. */
    public static byte[] toBytes(List<Note> notes, int gmProgram) {
        int n = notes.Count;
        int[] pitch = new int[n], on = new int[n], off = new int[n], vel = new int[n];
        long[] key = new long[n];
        for (int i = 0; i < n; i++) {
            Note q = notes[i];
            pitch[i] = Math.Max(0, Math.Min(127, q.pitch));
            on[i] = (int) Math.Max(0, Math.Min(8000000, JU.Round(q.start * TICKS_PER_SECOND)));
            off[i] = (int) Math.Max(0, Math.Min(8000000, JU.Round(q.end * TICKS_PER_SECOND)));
            if (off[i] <= on[i]) off[i] = on[i] + 1;
            vel[i] = (int) Math.Max(1, Math.Min(127, JU.Round(q.velocity * 127.0)));
            key[i] = ((long) pitch[i] << 43) | ((long) on[i] << 20) | (long) i;
        }
        // notas repetidas del mismo tono que se solapan: la anterior termina donde empieza la siguiente
        Array.Sort(key);
        bool[] skip = new bool[n];
        int prev = -1;
        for (int k = 0; k < n; k++) {
            int i = (int) (key[k] & 0xFFFFF);
            if (prev >= 0 && pitch[prev] == pitch[i]) {
                if (on[i] <= on[prev]) {
                    if (off[i] > off[prev]) off[prev] = off[i];
                    skip[i] = true;
                    continue;
                }
                if (on[i] < off[prev]) off[prev] = on[i];
            }
            prev = i;
        }
        // eventos: tiempo, y a igual tiempo primero los finales de nota
        int m = 0;
        long[] ev = new long[2 * n];
        for (int i = 0; i < n; i++) {
            if (skip[i]) continue;
            ev[m++] = ((long) off[i] << 21) | (long) i;                    // fin
            ev[m++] = ((long) on[i] << 21) | (1L << 20) | (long) i;        // inicio
        }
        long[] sorted = new long[m];
        Array.Copy(ev, 0, sorted, 0, m);
        Array.Sort(sorted);

        MidiWriter w = new MidiWriter();
        w.ascii("MThd");
        w.u32(6);
        w.u16(0);
        w.u16(1);
        w.u16(PPQ);
        w.ascii("MTrk");
        int lenPos = w.len;
        w.u32(0);
        int start = w.len;
        // nombre de la pista, tempo (500000 µs por negra), compás 4/4 e instrumento
        w.varLen(0); w.put(0xFF); w.put(0x03); w.put(12); w.ascii("Melodia MIDI");
        w.varLen(0); w.put(0xFF); w.put(0x51); w.put(3); w.put(0x07); w.put(0xA1); w.put(0x20);
        w.varLen(0); w.put(0xFF); w.put(0x58); w.put(4); w.put(4); w.put(2); w.put(24); w.put(8);
        w.varLen(0); w.put(0xC0); w.put(Math.Max(0, Math.Min(127, gmProgram)));
        long last = 0;
        for (int k = 0; k < m; k++) {
            long e = sorted[k];
            long t = e >> 21;
            int i = (int) (e & 0xFFFFF);
            bool isOn = (e & (1L << 20)) != 0;
            w.varLen((int) (t - last));
            last = t;
            if (isOn) { w.put(0x90); w.put(pitch[i]); w.put(vel[i]); }
            else { w.put(0x80); w.put(pitch[i]); w.put(64); }
        }
        w.varLen(0); w.put(0xFF); w.put(0x2F); w.put(0);
        int trackLen = w.len - start;
        w.buf[lenPos] = (byte) (trackLen >> 24);
        w.buf[lenPos + 1] = (byte) (trackLen >> 16);
        w.buf[lenPos + 2] = (byte) (trackLen >> 8);
        w.buf[lenPos + 3] = (byte) trackLen;
        byte[] @out = new byte[w.len];
        Array.Copy(w.buf, 0, @out, 0, w.len);
        return @out;
    }

    private void put(int v) {
        if (len == buf.Length) {
            byte[] nb = new byte[buf.Length * 2];
            Array.Copy(buf, 0, nb, 0, len);
            buf = nb;
        }
        buf[len++] = (byte) (v & 0xFF);
    }

    private void u16(int v) { put(v >> 8); put(v); }

    private void u32(int v) { put(v >> 24); put(v >> 16); put(v >> 8); put(v); }

    private void ascii(string s) {
        for (int i = 0; i < s.Length; i++) put(s[i]);
    }

    private void varLen(int v) {
        if (v < 0) v = 0;
        int b = v & 0x7F;
        int rest = v >> 7;
        int[] stack = new int[5];
        int n = 0;
        stack[n++] = b;
        while (rest > 0) {
            stack[n++] = (rest & 0x7F) | 0x80;
            rest >>= 7;
        }
        while (n > 0) put(stack[--n]);
    }
}
}
