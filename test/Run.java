import com.ricardopch.midi.engine.*;

import java.io.File;
import java.io.PrintWriter;
import java.util.List;

/** Prueba de escritorio: java Run entrada.wav notas.csv [instrumento salida.wav] */
public class Run {
    public static void main(String[] a) throws Exception {
        long t0 = System.nanoTime();
        WavIO.Pcm pcm = WavIO.read(new File(a[0]));
        float[] x = Resampler.resample(pcm.mono, pcm.length, pcm.sampleRate, MelodyExtractor.SAMPLE_RATE, null);
        long t1 = System.nanoTime();
        MelodyExtractor ex = new MelodyExtractor();
        for (int i = 4; i + 1 < a.length; i += 2) {
            java.lang.reflect.Field f = MelodyExtractor.class.getField(a[i]);
            if (f.getType() == int.class) f.setInt(ex, Integer.parseInt(a[i + 1]));
            else f.setDouble(ex, Double.parseDouble(a[i + 1]));
        }
        MelodyExtractor.Result r = ex.extract(x, null);
        long t2 = System.nanoTime();
        PrintWriter pw = new PrintWriter(a[1]);
        for (Note n : r.notes) pw.printf(java.util.Locale.US, "%.4f,%.4f,%d,%.3f%n", n.start, n.end, n.pitch, n.velocity);
        pw.close();
        PrintWriter pf = new PrintWriter(a[1] + ".f0");
        for (int i = 0; i < r.f0.length; i++) pf.printf(java.util.Locale.US, "%.5f,%.2f%n", i / r.frameRate, r.f0[i]);
        pf.close();
        double dur = (double) pcm.length / pcm.sampleRate;
        System.err.printf(java.util.Locale.US, "dur=%.1fs resample=%.0fms extract=%.0fms notes=%d%n",
                dur, (t1 - t0) / 1e6, (t2 - t1) / 1e6, r.notes.size());
        if (a.length >= 4 && !a[2].equals("-")) {
            int inst = Integer.parseInt(a[2]);
            long t3 = System.nanoTime();
            float[] y = new Synth(inst, 44100).render(r.notes, dur, null);
            System.err.printf(java.util.Locale.US, "synth=%.0fms%n", (System.nanoTime() - t3) / 1e6);
            WavIO.writeMono16(new File(a[3]), y, 44100);
        }
    }
}
