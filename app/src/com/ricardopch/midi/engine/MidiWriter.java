package com.ricardopch.midi.engine;

import java.util.Arrays;
import java.util.List;

/**
 * Archivo MIDI estándar: SMF tipo 0 (una pista, canal 1) o tipo 1 (una pista por instrumento).
 * 480 pulsos por negra a 120 ppm, es decir, 960 pulsos por segundo: los tiempos coinciden con el MP4.
 */
public final class MidiWriter {
    private static final int PPQ = 480;
    private static final double TICKS_PER_SECOND = 960.0;

    private byte[] buf = new byte[4096];
    private int len;

    /** gmProgram: instrumento General MIDI (0-127), por ejemplo Synth.GM_PROGRAM[i]. Una sola pista (SMF tipo 0). */
    public static byte[] toBytes(List<Note> notes, int gmProgram) {
        MidiWriter w = new MidiWriter();
        w.header(0, 1);
        int lenPos = w.beginTrack();
        w.conductor();
        w.trackName("Melodia MIDI");
        w.notesTrack(notes, 0, gmProgram);
        w.endTrack(lenPos);
        return w.bytes();
    }

    /**
     * Varias pistas (SMF tipo 1): una por instrumento. channels[k] = 9 indica batería (canal de percusión);
     * names en ASCII.
     */
    public static byte[] toBytesTracks(List<List<Note>> tracks, int[] programs, int[] channels, String[] names) {
        MidiWriter w = new MidiWriter();
        int n = tracks.size();
        w.header(1, n + 1);
        int lenPos = w.beginTrack();
        w.conductor();
        w.trackName("Melodia MIDI");
        w.endTrack(lenPos);
        for (int k = 0; k < n; k++) {
            lenPos = w.beginTrack();
            w.trackName(names[k]);
            w.notesTrack(tracks.get(k), channels[k], channels[k] == 9 ? -1 : programs[k]);
            w.endTrack(lenPos);
        }
        return w.bytes();
    }

    private void header(int format, int ntracks) {
        ascii("MThd");
        u32(6);
        u16(format);
        u16(ntracks);
        u16(PPQ);
    }

    private int beginTrack() {
        ascii("MTrk");
        int lenPos = len;
        u32(0);
        return lenPos;
    }

    private void endTrack(int lenPos) {
        varLen(0); put(0xFF); put(0x2F); put(0);
        int trackLen = len - lenPos - 4;
        buf[lenPos] = (byte) (trackLen >> 24);
        buf[lenPos + 1] = (byte) (trackLen >> 16);
        buf[lenPos + 2] = (byte) (trackLen >> 8);
        buf[lenPos + 3] = (byte) trackLen;
    }

    /** Tempo (500000 µs por negra) y compás 4/4. */
    private void conductor() {
        varLen(0); put(0xFF); put(0x51); put(3); put(0x07); put(0xA1); put(0x20);
        varLen(0); put(0xFF); put(0x58); put(4); put(4); put(2); put(24); put(8);
    }

    private void trackName(String name) {
        varLen(0); put(0xFF); put(0x03); varLen(name.length()); ascii(name);
    }

    private byte[] bytes() {
        byte[] out = new byte[len];
        System.arraycopy(buf, 0, out, 0, len);
        return out;
    }

    /** Programa (si program >= 0) y notas de una pista en el canal indicado. */
    private void notesTrack(List<Note> notes, int channel, int program) {
        int n = notes.size();
        int[] pitch = new int[n], on = new int[n], off = new int[n], vel = new int[n];
        long[] key = new long[n];
        for (int i = 0; i < n; i++) {
            Note q = notes.get(i);
            pitch[i] = Math.max(0, Math.min(127, q.pitch));
            on[i] = (int) Math.max(0, Math.min(8000000, Math.round(q.start * TICKS_PER_SECOND)));
            off[i] = (int) Math.max(0, Math.min(8000000, Math.round(q.end * TICKS_PER_SECOND)));
            if (off[i] <= on[i]) off[i] = on[i] + 1;
            vel[i] = (int) Math.max(1, Math.min(127, Math.round(q.velocity * 127.0)));
            key[i] = ((long) pitch[i] << 43) | ((long) on[i] << 20) | (long) i;
        }
        // notas repetidas del mismo tono que se solapan: la anterior termina donde empieza la siguiente
        Arrays.sort(key);
        boolean[] skip = new boolean[n];
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
        System.arraycopy(ev, 0, sorted, 0, m);
        Arrays.sort(sorted);
        if (program >= 0) { varLen(0); put(0xC0 | channel); put(Math.max(0, Math.min(127, program))); }
        long last = 0;
        for (int k = 0; k < m; k++) {
            long e = sorted[k];
            long t = e >> 21;
            int i = (int) (e & 0xFFFFF);
            boolean isOn = (e & (1L << 20)) != 0;
            varLen((int) (t - last));
            last = t;
            if (isOn) { put(0x90 | channel); put(pitch[i]); put(vel[i]); }
            else { put(0x80 | channel); put(pitch[i]); put(64); }
        }
    }

    private void put(int v) {
        if (len == buf.length) {
            byte[] nb = new byte[buf.length * 2];
            System.arraycopy(buf, 0, nb, 0, len);
            buf = nb;
        }
        buf[len++] = (byte) (v & 0xFF);
    }

    private void u16(int v) { put(v >> 8); put(v); }

    private void u32(int v) { put(v >> 24); put(v >> 16); put(v >> 8); put(v); }

    private void ascii(String s) {
        for (int i = 0; i < s.length(); i++) put(s.charAt(i));
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
