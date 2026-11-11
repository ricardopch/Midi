package com.ricardopch.midi;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;

import com.ricardopch.midi.engine.Progress;

import java.io.File;
import java.io.IOException;
import java.nio.ByteBuffer;

/** Codifica PCM mono en AAC-LC dentro de un contenedor MP4. */
final class Mp4Writer {
    private static final String MIME = "audio/mp4a-latm";

    private Mp4Writer() { }

    static void write(float[] pcm, int sampleRate, File out, Progress p) throws IOException {
        MediaFormat fmt = MediaFormat.createAudioFormat(MIME, sampleRate, 1);
        fmt.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
        fmt.setInteger(MediaFormat.KEY_BIT_RATE, 128000);
        fmt.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 16384);
        MediaCodec enc = MediaCodec.createEncoderByType(MIME);
        MediaMuxer mux = null;
        boolean muxStarted = false;
        try {
            enc.configure(fmt, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            enc.start();
            if (out.exists() && !out.delete()) throw new IOException("No se puede sobrescribir " + out);
            mux = new MediaMuxer(out.getAbsolutePath(), MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
            int track = -1;
            int pos = 0;                    // muestras enviadas
            final int total = pcm.length;
            boolean inDone = false, outDone = false;
            int idle = 0;
            long lastPts = -1;
            byte[] tmp = new byte[16384];
            while (!outDone) {
                if (p.isCancelled()) throw new Progress.Cancelled();
                if (!inDone) {
                    int ii = enc.dequeueInputBuffer(10000);
                    if (ii >= 0) {
                        ByteBuffer ib = enc.getInputBuffer(ii);
                        long pts = (long) pos * 1000000L / sampleRate;
                        if (ib == null || pos >= total) {
                            enc.queueInputBuffer(ii, 0, 0, pts, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                            inDone = true;
                        } else {
                            ib.clear();
                            int cap = Math.min(ib.remaining(), tmp.length) & ~1;
                            int n = Math.min(cap / 2, total - pos);
                            for (int i = 0; i < n; i++) {
                                float v = pcm[pos + i];
                                if (v > 1f) v = 1f;
                                if (v < -1f) v = -1f;
                                int s = (int) (v * 32767f);
                                tmp[2 * i] = (byte) s;
                                tmp[2 * i + 1] = (byte) (s >> 8);
                            }
                            ib.put(tmp, 0, n * 2);
                            enc.queueInputBuffer(ii, 0, n * 2, pts, 0);
                            pos += n;
                            p.update((float) pos / total);
                        }
                    }
                }
                int oi = enc.dequeueOutputBuffer(info, 10000);
                if (oi == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    if (muxStarted) throw new IOException("Formato de salida cambiado dos veces");
                    track = mux.addTrack(enc.getOutputFormat());
                    mux.start();
                    muxStarted = true;
                } else if (oi >= 0) {
                    idle = 0;
                    ByteBuffer ob = enc.getOutputBuffer(oi);
                    if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) info.size = 0;
                    if (ob != null && info.size > 0 && muxStarted) {
                        if (info.presentationTimeUs <= lastPts) info.presentationTimeUs = lastPts + 1;
                        lastPts = info.presentationTimeUs;
                        ob.position(info.offset);
                        ob.limit(info.offset + info.size);
                        mux.writeSampleData(track, ob, info);
                    }
                    enc.releaseOutputBuffer(oi, false);
                    if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) outDone = true;
                } else if (oi == MediaCodec.INFO_TRY_AGAIN_LATER) {
                    if (inDone && ++idle > 500) break;
                }
            }
            if (!muxStarted) throw new IOException("El codificador AAC no produjo datos");
        } finally {
            try { enc.stop(); } catch (Exception ignored) { }
            try { enc.release(); } catch (Exception ignored) { }
            if (mux != null) {
                if (muxStarted) {
                    try { mux.stop(); } catch (Exception ignored) { }
                }
                try { mux.release(); } catch (Exception ignored) { }
            }
        }
    }
}
