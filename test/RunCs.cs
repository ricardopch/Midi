// Prueba de escritorio del motor C# (mono): mono RunCs.exe entrada.wav notas.csv [instrumento salida.wav]
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using MelodiaMidi;
using MelodiaMidi.Engine;

public static class RunCs
{
    public static void Main(string[] a)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Stopwatch sw = Stopwatch.StartNew();
        WavFile.Pcm pcm = WavFile.Read(a[0], 600);
        float[] x = Resampler.resample(pcm.Samples, pcm.Length, pcm.SampleRate, MelodyExtractor.SAMPLE_RATE, null);
        long t1 = sw.ElapsedMilliseconds;
        MelodyExtractor.Result r = new MelodyExtractor().extract(x, null);
        long t2 = sw.ElapsedMilliseconds;
        using (StreamWriter w = new StreamWriter(a[1]))
            foreach (Note n in r.notes) w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2},{3:F3}", n.start, n.end, n.pitch, n.velocity));
        double dur = (double)pcm.Length / pcm.SampleRate;
        Console.Error.WriteLine("dur={0:F1}s resample={1}ms extract={2}ms notes={3}", dur, t1, t2 - t1, r.notes.Count);
        if (a.Length >= 4)
        {
            long t3 = sw.ElapsedMilliseconds;
            float[] y = new Synth(int.Parse(a[2]), 44100).render(r.notes, dur, null);
            Console.Error.WriteLine("synth={0}ms", sw.ElapsedMilliseconds - t3);
            File.WriteAllBytes(a[3], WavFile.Build(y, 44100));
        }
    }
}
