import com.ricardopch.midi.engine.*;

import java.io.File;
import java.io.PrintWriter;
import java.nio.file.Files;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Prueba de escritorio del motor Java.
 * java RunBP bp.bin entrada.wav salida_sin_extension [notas 0|1|2] [instrumento]
 * Escribe salida.csv (inicio,fin,midi,intensidad), salida.mid y salida.wav.
 */
public class RunBP {
    public static void main(String[] a) throws Exception {
        int level = a.length > 3 ? Integer.parseInt(a[3]) : 1;
        int inst = a.length > 4 ? Integer.parseInt(a[4]) : 0;
        long t0 = System.nanoTime();
        final BasicPitch bp = new BasicPitch(WeightFile.parse(Files.readAllBytes(new File(a[0]).toPath())));
        WavIO.Pcm pcm = WavIO.read(new File(a[1]));
        float[] x = Resampler.resample(pcm.mono, pcm.length, pcm.sampleRate, BasicPitch.SR, null);
        bp.prepare(x);
        final int frames = bp.frameCount(), windows = bp.windowCount();
        final float[] note = new float[frames * BasicPitch.KEYS], onset = new float[frames * BasicPitch.KEYS];
        final AtomicInteger next = new AtomicInteger();
        Thread[] pool = new Thread[Runtime.getRuntime().availableProcessors()];
        for (int k = 0; k < pool.length; k++) {
            pool[k] = new Thread(new Runnable() {
                public void run() {
                    BasicPitch.Workspace ws = bp.newWorkspace();
                    int w;
                    while ((w = next.getAndIncrement()) < windows) bp.runWindow(w, ws, note, onset);
                }
            });
            pool[k].start();
        }
        for (Thread t : pool) t.join();
        long t1 = System.nanoTime();
        List<Note> notes = BasicPitch.notes(note, onset, frames, BasicPitch.Settings.forSensitivity(level));
        PrintWriter pw = new PrintWriter(a[2] + ".csv");
        for (Note n : notes) pw.printf(Locale.ROOT, "%.4f,%.4f,%d,%.4f%n", n.start, n.end, n.pitch, n.velocity);
        pw.close();
        Files.write(new File(a[2] + ".mid").toPath(), MidiWriter.toBytes(notes, Synth.GM_PROGRAM[inst]));
        double dur = (double) pcm.length / pcm.sampleRate;
        float[] y = new Synth(inst, 44100).render(notes, dur, null);
        WavIO.writeMono16(new File(a[2] + ".wav"), y, 44100);
        System.err.printf(Locale.ROOT, "dur=%.1fs notas=%d red=%.1fs (%d hilos)%n", dur, notes.size(), (t1 - t0) / 1e9, pool.length);
    }
}
