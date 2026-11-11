package com.ricardopch.midi;

import android.media.MediaCodec;
import android.media.MediaExtractor;
import android.media.MediaFormat;

import com.ricardopch.midi.engine.Progress;
import com.ricardopch.midi.engine.WavIO;

import java.io.File;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Decodifica cualquier audio que soporte Android (mp3, m4a, aac, ogg, opus, flac, wav, vídeo...) a PCM mono. */
final class AudioDecoder {
    static final int MAX_SECONDS = 10 * 60;

    static final class Result {
        final short[] pcm;
        final int length;
        final int sampleRate;
        final boolean truncated;

        Result(short[] pcm, int length, int sampleRate, boolean truncated) {
            this.pcm = pcm;
            this.length = length;
            this.sampleRate = sampleRate;
            this.truncated = truncated;
        }
    }

    private AudioDecoder() { }

    static Result decode(File f, Progress p) throws IOException {
        if (WavIO.isWav(f)) {
            try {
                WavIO.Pcm w = WavIO.read(f);
                int max = MAX_SECONDS * w.sampleRate;
                boolean trunc = w.length > max;
                return new Result(w.mono, Math.min(w.length, max), w.sampleRate, trunc);
            } catch (IOException e) {
                // se intenta con MediaExtractor
            }
        }
        MediaExtractor ex = new MediaExtractor();
        MediaCodec codec = null;
        try {
            try {
                ex.setDataSource(f.getAbsolutePath());
            } catch (IOException e) {
                throw new IOException("Formato de archivo no reconocido. Prueba con MP3, M4A, WAV, OGG o FLAC.");
            }
            int track = -1;
            MediaFormat fmt = null;
            for (int i = 0; i < ex.getTrackCount(); i++) {
                MediaFormat tf = ex.getTrackFormat(i);
                String mime = tf.getString(MediaFormat.KEY_MIME);
                if (mime != null && mime.startsWith("audio/")) {
                    track = i;
                    fmt = tf;
                    break;
                }
            }
            if (track < 0) throw new IOException("El archivo no contiene ninguna pista de audio.");
            ex.selectTrack(track);
            String mime = fmt.getString(MediaFormat.KEY_MIME);
            int rate = fmt.containsKey(MediaFormat.KEY_SAMPLE_RATE) ? fmt.getInteger(MediaFormat.KEY_SAMPLE_RATE) : 44100;
            int channels = fmt.containsKey(MediaFormat.KEY_CHANNEL_COUNT) ? fmt.getInteger(MediaFormat.KEY_CHANNEL_COUNT) : 2;
            long durUs = fmt.containsKey(MediaFormat.KEY_DURATION) ? fmt.getLong(MediaFormat.KEY_DURATION) : -1;
            long est = durUs > 0 ? (long) (durUs / 1e6 * rate) + rate : (long) rate * 240;
            Sink sink = new Sink((int) Math.min(est, (long) MAX_SECONDS * 48000 + 48000));
            int encoding = 2; // ENCODING_PCM_16BIT
            if (fmt.containsKey("pcm-encoding")) encoding = fmt.getInteger("pcm-encoding");

            if ("audio/raw".equals(mime)) {
                ByteBuffer buf = ByteBuffer.allocateDirect(1 << 18);
                while (true) {
                    if (p.isCancelled()) throw new Progress.Cancelled();
                    buf.clear();
                    int n = ex.readSampleData(buf, 0);
                    if (n < 0) break;
                    buf.position(0);
                    buf.limit(n);
                    sink.add(buf, encoding, channels);
                    if (durUs > 0) p.update((float) Math.min(1.0, ex.getSampleTime() / (double) durUs));
                    if (!ex.advance()) break;
                    if (sink.count >= (long) MAX_SECONDS * rate) { sink.truncated = true; break; }
                }
                return new Result(sink.data, Math.min(sink.count, MAX_SECONDS * rate), rate, sink.truncated);
            }

            try {
                codec = MediaCodec.createDecoderByType(mime);
                codec.configure(fmt, null, null, 0);
                codec.start();
            } catch (Exception e) {
                throw new IOException("Este móvil no puede decodificar este audio (" + mime + ").");
            }
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
            boolean inDone = false, outDone = false;
            int idle = 0;
            while (!outDone) {
                if (p.isCancelled()) throw new Progress.Cancelled();
                if (!inDone) {
                    int ii = codec.dequeueInputBuffer(10000);
                    if (ii >= 0) {
                        ByteBuffer ib = codec.getInputBuffer(ii);
                        int n = ib == null ? -1 : ex.readSampleData(ib, 0);
                        if (n < 0) {
                            codec.queueInputBuffer(ii, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                            inDone = true;
                        } else {
                            long pts = ex.getSampleTime();
                            codec.queueInputBuffer(ii, 0, n, Math.max(0, pts), 0);
                            ex.advance();
                            if (durUs > 0) p.update((float) Math.min(1.0, pts / (double) durUs));
                        }
                    }
                }
                int oi = codec.dequeueOutputBuffer(info, 10000);
                if (oi >= 0) {
                    idle = 0;
                    ByteBuffer ob = codec.getOutputBuffer(oi);
                    if (ob != null && info.size > 0) {
                        ob.position(info.offset);
                        ob.limit(info.offset + info.size);
                        sink.add(ob, encoding, channels);
                    }
                    codec.releaseOutputBuffer(oi, false);
                    if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) outDone = true;
                    if (sink.count >= (long) MAX_SECONDS * rate) {
                        sink.truncated = true;
                        break;
                    }
                } else if (oi == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    MediaFormat of = codec.getOutputFormat();
                    if (of.containsKey(MediaFormat.KEY_SAMPLE_RATE)) rate = of.getInteger(MediaFormat.KEY_SAMPLE_RATE);
                    if (of.containsKey(MediaFormat.KEY_CHANNEL_COUNT)) channels = of.getInteger(MediaFormat.KEY_CHANNEL_COUNT);
                    if (of.containsKey("pcm-encoding")) encoding = of.getInteger("pcm-encoding");
                } else if (oi == MediaCodec.INFO_TRY_AGAIN_LATER) {
                    if (inDone && ++idle > 500) break; // 5 s sin salida tras el final
                }
            }
            if (sink.count == 0) throw new IOException("No se pudo decodificar el audio.");
            int len = Math.min(sink.count, MAX_SECONDS * rate);
            return new Result(sink.data, len, rate, sink.truncated);
        } finally {
            if (codec != null) {
                try { codec.stop(); } catch (Exception ignored) { }
                try { codec.release(); } catch (Exception ignored) { }
            }
            ex.release();
        }
    }

    /** Acumula PCM convirtiendo a mono 16 bits. */
    private static final class Sink {
        short[] data;
        int count;
        boolean truncated;

        Sink(int initial) { data = new short[Math.max(initial, 1 << 16)]; }

        void add(ByteBuffer b, int encoding, int channels) {
            b.order(ByteOrder.LITTLE_ENDIAN);
            if (channels < 1) channels = 1;
            int bps;
            switch (encoding) {
                case 3: bps = 1; break;          // 8 bits
                case 4: bps = 4; break;          // float
                case 21: bps = 3; break;         // 24 bits empaquetado
                case 22: bps = 4; break;         // 32 bits entero
                default: bps = 2; break;         // 16 bits
            }
            int frameBytes = bps * channels;
            int start = b.position();
            int frames = (b.limit() - start) / frameBytes;
            ensure(count + frames);
            for (int fI = 0; fI < frames; fI++) {
                int base = start + fI * frameBytes;
                float sum = 0;
                for (int c = 0; c < channels; c++) {
                    int o = base + c * bps;
                    float v;
                    switch (encoding) {
                        case 3: v = ((b.get(o) & 0xFF) - 128) / 128f; break;
                        case 4: v = b.getFloat(o); break;
                        case 21: {
                            int s = (b.get(o) & 0xFF) | ((b.get(o + 1) & 0xFF) << 8) | (b.get(o + 2) << 16);
                            v = s / 8388608f;
                            break;
                        }
                        case 22: v = b.getInt(o) / 2147483648f; break;
                        default: v = b.getShort(o) / 32768f; break;
                    }
                    sum += v;
                }
                float m = sum / channels;
                if (m > 1f) m = 1f;
                if (m < -1f) m = -1f;
                data[count++] = (short) (m * 32767f);
            }
        }

        private void ensure(int n) {
            if (n <= data.length) return;
            int cap = Math.max(n, data.length + (data.length >> 1));
            short[] d = new short[cap];
            System.arraycopy(data, 0, d, 0, count);
            data = d;
        }
    }
}
