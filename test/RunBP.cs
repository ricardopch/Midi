// Prueba de escritorio del motor C# (Windows), equivalente a RunBP.java.
// mcs -out:RunBP.exe test/RunBP.cs windows/engine/*.cs windows/src/WavFile.cs
// mono RunBP.exe bp.bin entrada.wav salida_sin_extension [notas 0|1|2] [instrumento]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using MelodiaMidi;
using MelodiaMidi.Engine;

public static class RunBP
{
    public static void Main(string[] a)
    {
        int level = a.Length > 3 ? int.Parse(a[3]) : 1;
        int inst = a.Length > 4 ? int.Parse(a[4]) : 0;
        DateTime t0 = DateTime.Now;
        BasicPitch bp = new BasicPitch(WeightFile.parse(File.ReadAllBytes(a[0])));
        WavFile.Pcm pcm = WavFile.Read(a[1], 3600);
        float[] x = Resampler.resample(pcm.Samples, pcm.Length, pcm.SampleRate, BasicPitch.SR, null);
        bp.prepare(x);
        int frames = bp.frameCount(), windows = bp.windowCount();
        float[] note = new float[frames * BasicPitch.KEYS], onset = new float[frames * BasicPitch.KEYS];
        int next = -1;
        Thread[] pool = new Thread[Environment.ProcessorCount];
        for (int k = 0; k < pool.Length; k++)
        {
            pool[k] = new Thread(delegate ()
            {
                BasicPitch.Workspace ws = bp.newWorkspace();
                int w;
                while ((w = Interlocked.Increment(ref next)) < windows) bp.runWindow(w, ws, note, onset);
            });
            pool[k].Start();
        }
        foreach (Thread t in pool) t.Join();
        double net = (DateTime.Now - t0).TotalSeconds;
        List<Note> notes = BasicPitch.notes(note, onset, frames, BasicPitch.Settings.forSensitivity(level));
        using (StreamWriter sw = new StreamWriter(a[2] + ".csv"))
            foreach (Note n in notes)
                sw.Write(string.Format(CultureInfo.InvariantCulture, "{0:0.0000},{1:0.0000},{2},{3:0.0000}\n", n.start, n.end, n.pitch, n.velocity));
        File.WriteAllBytes(a[2] + ".mid", MidiWriter.toBytes(notes, Synth.GM_PROGRAM[inst]));
        double dur = (double)pcm.Length / pcm.SampleRate;
        float[] y = new Synth(inst, 44100).render(notes, dur, null);
        File.WriteAllBytes(a[2] + ".wav", WavFile.Build(y, 44100));
        Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "dur={0:0.0}s notas={1} red={2:0.0}s ({3} hilos)", dur, notes.Count, net, pool.Length));
    }
}
