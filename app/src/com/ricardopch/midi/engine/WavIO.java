package com.ricardopch.midi.engine;

import java.io.BufferedInputStream;
import java.io.DataInputStream;
import java.io.EOFException;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/** Lectura (PCM 8/16/24/32 bits y float) y escritura (16 bits) de WAV, sin dependencias. */
public final class WavIO {
    private WavIO() { }

    public static final class Pcm {
        public final short[] mono;   // mezcla mono en 16 bits
        public final int length;
        public final int sampleRate;

        Pcm(short[] mono, int length, int sampleRate) {
            this.mono = mono;
            this.length = length;
            this.sampleRate = sampleRate;
        }
    }

    /** Devuelve true si el archivo empieza por RIFF....WAVE. */
    public static boolean isWav(File f) {
        InputStream in = null;
        try {
            in = new FileInputStream(f);
            byte[] h = new byte[12];
            int r = 0;
            while (r < 12) {
                int k = in.read(h, r, 12 - r);
                if (k < 0) return false;
                r += k;
            }
            return h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
                    && h[8] == 'W' && h[9] == 'A' && h[10] == 'V' && h[11] == 'E';
        } catch (IOException e) {
            return false;
        } finally {
            closeQuietly(in);
        }
    }

    public static Pcm read(File f) throws IOException {
        DataInputStream in = new DataInputStream(new BufferedInputStream(new FileInputStream(f), 1 << 16));
        try {
            byte[] hdr = new byte[12];
            in.readFully(hdr);
            if (hdr[0] != 'R' || hdr[8] != 'W') throw new IOException("No es un WAV");
            int format = -1, channels = 0, rate = 0, bits = 0;
            while (true) {
                byte[] idb = new byte[4];
                try {
                    in.readFully(idb);
                } catch (EOFException e) {
                    throw new IOException("WAV sin datos");
                }
                String id = new String(idb, "US-ASCII");
                long size = readLE32(in) & 0xFFFFFFFFL;
                if (id.equals("fmt ")) {
                    byte[] fmt = new byte[(int) size];
                    in.readFully(fmt);
                    format = le16(fmt, 0);
                    channels = le16(fmt, 2);
                    rate = le32(fmt, 4);
                    bits = le16(fmt, 14);
                    if (format == 0xFFFE && size >= 26) format = le16(fmt, 24); // WAVE_FORMAT_EXTENSIBLE
                    if ((size & 1) == 1) in.skipBytes(1);
                } else if (id.equals("data")) {
                    if (format < 0) throw new IOException("WAV sin cabecera fmt");
                    return readData(in, size, format, channels, rate, bits);
                } else {
                    skipFully(in, size + (size & 1));
                }
            }
        } finally {
            closeQuietly(in);
        }
    }

    private static Pcm readData(DataInputStream in, long size, int format, int channels, int rate, int bits)
            throws IOException {
        if (channels <= 0 || rate <= 0) throw new IOException("WAV no válido");
        boolean isFloat = format == 3;
        if (!(format == 1 || isFloat)) throw new IOException("Formato WAV no soportado (" + format + ")");
        int bps = bits / 8;
        if (bps < 1 || bps > 4 || (isFloat && bps != 4)) throw new IOException("Bits WAV no soportados");
        int frameBytes = bps * channels;
        long frames = (size == 0 || size == 0xFFFFFFFFL) ? Long.MAX_VALUE : size / frameBytes;
        int cap = (int) Math.min(frames, 1 << 20);
        short[] out = new short[Math.max(cap, 1024)];
        int count = 0;
        byte[] buf = new byte[frameBytes * 4096];
        long remaining = frames;
        while (remaining > 0) {
            int want = (int) Math.min(remaining, 4096);
            int got = readUpTo(in, buf, want * frameBytes);
            int gotFrames = got / frameBytes;
            if (gotFrames <= 0) break;
            for (int fI = 0; fI < gotFrames; fI++) {
                double sum = 0;
                int base = fI * frameBytes;
                for (int c = 0; c < channels; c++) {
                    int o = base + c * bps;
                    double v;
                    if (isFloat) {
                        v = Float.intBitsToFloat(le32(buf, o));
                    } else if (bps == 1) {
                        v = ((buf[o] & 0xFF) - 128) / 128.0;
                    } else if (bps == 2) {
                        v = (short) le16(buf, o) / 32768.0;
                    } else if (bps == 3) {
                        int s = (buf[o] & 0xFF) | ((buf[o + 1] & 0xFF) << 8) | (buf[o + 2] << 16);
                        v = s / 8388608.0;
                    } else {
                        v = le32(buf, o) / 2147483648.0;
                    }
                    sum += v;
                }
                double m = sum / channels;
                if (m > 1) m = 1;
                if (m < -1) m = -1;
                if (count == out.length) {
                    short[] n2 = new short[out.length * 2];
                    System.arraycopy(out, 0, n2, 0, count);
                    out = n2;
                }
                out[count++] = (short) Math.round(m * 32767);
            }
            remaining -= gotFrames;
            if (got < want * frameBytes) break;
        }
        return new Pcm(out, count, rate);
    }

    /** Escribe PCM float mono como WAV 16 bits (usado en pruebas). */
    public static void writeMono16(File f, float[] data, int rate) throws IOException {
        OutputStream os = new FileOutputStream(f);
        try {
            int n = data.length;
            byte[] h = new byte[44];
            put(h, 0, "RIFF");
            putLE32(h, 4, 36 + n * 2);
            put(h, 8, "WAVE");
            put(h, 12, "fmt ");
            putLE32(h, 16, 16);
            putLE16(h, 20, 1);
            putLE16(h, 22, 1);
            putLE32(h, 24, rate);
            putLE32(h, 28, rate * 2);
            putLE16(h, 32, 2);
            putLE16(h, 34, 16);
            put(h, 36, "data");
            putLE32(h, 40, n * 2);
            os.write(h);
            byte[] b = new byte[8192];
            int bi = 0;
            for (int i = 0; i < n; i++) {
                float v = data[i];
                if (v > 1f) v = 1f;
                if (v < -1f) v = -1f;
                int s = Math.round(v * 32767f);
                b[bi++] = (byte) s;
                b[bi++] = (byte) (s >> 8);
                if (bi == b.length) {
                    os.write(b);
                    bi = 0;
                }
            }
            if (bi > 0) os.write(b, 0, bi);
        } finally {
            os.close();
        }
    }

    private static int readUpTo(InputStream in, byte[] b, int len) throws IOException {
        int r = 0;
        while (r < len) {
            int k = in.read(b, r, len - r);
            if (k < 0) break;
            r += k;
        }
        return r;
    }

    private static void skipFully(DataInputStream in, long n) throws IOException {
        while (n > 0) {
            long k = in.skip(n);
            if (k <= 0) {
                if (in.read() < 0) throw new EOFException();
                k = 1;
            }
            n -= k;
        }
    }

    private static int readLE32(DataInputStream in) throws IOException {
        int b0 = in.readUnsignedByte(), b1 = in.readUnsignedByte(), b2 = in.readUnsignedByte(), b3 = in.readUnsignedByte();
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }

    private static int le16(byte[] b, int o) { return (b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8); }

    private static int le32(byte[] b, int o) {
        return (b[o] & 0xFF) | ((b[o + 1] & 0xFF) << 8) | ((b[o + 2] & 0xFF) << 16) | ((b[o + 3] & 0xFF) << 24);
    }

    private static void put(byte[] b, int o, String s) {
        for (int i = 0; i < 4; i++) b[o + i] = (byte) s.charAt(i);
    }

    private static void putLE16(byte[] b, int o, int v) { b[o] = (byte) v; b[o + 1] = (byte) (v >> 8); }

    private static void putLE32(byte[] b, int o, int v) {
        b[o] = (byte) v; b[o + 1] = (byte) (v >> 8); b[o + 2] = (byte) (v >> 16); b[o + 3] = (byte) (v >> 24);
    }

    private static void closeQuietly(InputStream in) {
        if (in != null) try { in.close(); } catch (IOException ignored) { }
    }
}
