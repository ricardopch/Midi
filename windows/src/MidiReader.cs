using System;
using System.Collections.Generic;
using System.Globalization;
using MelodiaMidi.Engine;

namespace MelodiaMidi
{
    /**
     * Lector de archivos MIDI estándar (tipos 0 y 1) con mapa de tempo.
     * Se usa para tocar con un instrumento las notas que devuelve MuScriptor.
     */
    internal sealed class MidiReader
    {
        public sealed class MidiNote
        {
            public double Start, End;
            public int Pitch, Velocity, Program, Channel;
            public bool IsDrum { get { return Channel == 9; } }
        }

        public readonly List<MidiNote> Notes = new List<MidiNote>();
        public readonly List<string> TrackNames = new List<string>();
        /** Segundos del MIDI en los que suena el instante 0 de la grabación (marca "muscriptor:bar_offset="). */
        public double RecordingOffset;

        private const string OffsetMarker = "muscriptor:bar_offset=";

        private struct Tempo { public long Tick; public int MicrosPerBeat; }

        private struct Pending { public long Tick; public int Velocity; public int Program; }

        public static MidiReader Parse(byte[] b)
        {
            MidiReader r = new MidiReader();
            int pos = 0;
            if (b.Length < 14 || Ascii(b, 0, 4) != "MThd") throw new FormatException("No es un archivo MIDI.");
            int hlen = U32(b, 4);
            int ntracks = U16(b, 10);
            int division = U16(b, 12);
            if ((division & 0x8000) != 0) throw new FormatException("MIDI con división SMPTE no soportado.");
            pos = 8 + hlen;
            List<Tempo> tempos = new List<Tempo>();
            // notas en ticks: (inicio, fin, tono, velocidad, programa, canal)
            List<long[]> raw = new List<long[]>();
            for (int t = 0; t < ntracks && pos + 8 <= b.Length; t++)
            {
                string id = Ascii(b, pos, 4);
                int len = U32(b, pos + 4);
                int p = pos + 8, end = Math.Min(b.Length, p + len);
                pos = p + len;
                if (id != "MTrk") continue;
                long tick = 0;
                int status = 0;
                int[] program = new int[16];
                Dictionary<int, Pending> open = new Dictionary<int, Pending>();
                while (p < end)
                {
                    tick += VarLen(b, ref p);
                    if (p >= end) break;
                    int s = b[p];
                    if (s >= 0x80) { p++; if (s < 0xF0) status = s; }
                    else s = status;                       // estado continuo
                    if (s == 0xFF)
                    {
                        int type = b[p++];
                        int ml = (int)VarLen(b, ref p);
                        if (type == 0x51 && ml >= 3)
                            tempos.Add(new Tempo { Tick = tick, MicrosPerBeat = (b[p] << 16) | (b[p + 1] << 8) | b[p + 2] });
                        else if (type == 0x03)
                            r.TrackNames.Add(Text(b, p, ml));
                        else if (type == 0x06)
                        {
                            string m = Text(b, p, ml);
                            if (m.StartsWith(OffsetMarker, StringComparison.Ordinal))
                            {
                                double v;
                                if (double.TryParse(m.Substring(OffsetMarker.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                                    r.RecordingOffset = v;
                            }
                        }
                        p += ml;
                        if (type == 0x2F) break;
                        continue;
                    }
                    if (s == 0xF0 || s == 0xF7)
                    {
                        int sl = (int)VarLen(b, ref p);
                        p += sl;
                        continue;
                    }
                    int kind = s & 0xF0, ch = s & 0x0F;
                    if (kind == 0xC0 || kind == 0xD0)
                    {
                        if (kind == 0xC0) program[ch] = b[p];
                        p += 1;
                        continue;
                    }
                    int d1 = b[p], d2 = b[p + 1];
                    p += 2;
                    if (kind == 0x90 && d2 > 0)
                    {
                        int key = ch * 128 + d1;
                        Pending prev;
                        if (open.TryGetValue(key, out prev))
                            raw.Add(new long[] { prev.Tick, tick, d1, prev.Velocity, prev.Program, ch });
                        open[key] = new Pending { Tick = tick, Velocity = d2, Program = program[ch] };
                    }
                    else if (kind == 0x80 || kind == 0x90)
                    {
                        int key = ch * 128 + d1;
                        Pending on;
                        if (open.TryGetValue(key, out on))
                        {
                            raw.Add(new long[] { on.Tick, tick, d1, on.Velocity, on.Program, ch });
                            open.Remove(key);
                        }
                    }
                }
                foreach (KeyValuePair<int, Pending> kv in open)
                    raw.Add(new long[] { kv.Value.Tick, tick, kv.Key % 128, kv.Value.Velocity, kv.Value.Program, kv.Key / 128 });
            }
            tempos.Sort(delegate (Tempo x, Tempo y) { return x.Tick.CompareTo(y.Tick); });
            foreach (long[] n in raw)
            {
                MidiNote q = new MidiNote
                {
                    Start = Seconds(n[0], tempos, division) - r.RecordingOffset,
                    End = Seconds(n[1], tempos, division) - r.RecordingOffset,
                    Pitch = (int)n[2],
                    Velocity = (int)n[3],
                    Program = (int)n[4],
                    Channel = (int)n[5]
                };
                if (q.End <= q.Start) q.End = q.Start + 0.05;
                r.Notes.Add(q);
            }
            r.Notes.Sort(delegate (MidiNote x, MidiNote y) { return x.Start.CompareTo(y.Start); });
            return r;
        }

        /** Notas con instrumento (sin batería), en segundos de la grabación, para el sintetizador. */
        public List<Note> PitchedNotes()
        {
            List<Note> l = new List<Note>();
            foreach (MidiNote n in Notes)
            {
                if (n.IsDrum || n.End <= 0) continue;
                double s = Math.Max(0, n.Start);
                l.Add(new Note(n.Pitch, s, n.End, Math.Max(0.3f, Math.Min(1f, n.Velocity / 127f))));
            }
            return l;
        }

        private static double Seconds(long tick, List<Tempo> tempos, int division)
        {
            double sec = 0;
            long last = 0;
            int us = 500000;
            foreach (Tempo t in tempos)
            {
                if (t.Tick >= tick) break;
                sec += (t.Tick - last) * (double)us / division / 1e6;
                last = t.Tick;
                us = t.MicrosPerBeat;
            }
            return sec + (tick - last) * (double)us / division / 1e6;
        }

        private static long VarLen(byte[] b, ref int p)
        {
            long v = 0;
            for (int i = 0; i < 4 && p < b.Length; i++)
            {
                int c = b[p++];
                v = (v << 7) | (uint)(c & 0x7F);
                if ((c & 0x80) == 0) break;
            }
            return v;
        }

        private static int U16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }

        private static int U32(byte[] b, int p) { return (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]; }

        private static string Ascii(byte[] b, int p, int n)
        {
            if (p + n > b.Length) return "";
            return System.Text.Encoding.ASCII.GetString(b, p, n);
        }

        private static string Text(byte[] b, int p, int n)
        {
            if (p + n > b.Length) n = Math.Max(0, b.Length - p);
            return System.Text.Encoding.UTF8.GetString(b, p, n);
        }
    }
}
