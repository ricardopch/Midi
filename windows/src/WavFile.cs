using System;
using System.IO;
using System.Text;

namespace MelodiaMidi
{
    /** Lectura de WAV (PCM 8/16/24/32 bits y float) a mono, y creación de WAV de 16 bits. */
    internal static class WavFile
    {
        public sealed class Pcm
        {
            public float[] Samples;
            public int Length;
            public int SampleRate;
            public bool Truncated;
        }

        public static bool IsWav(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = new byte[12];
                    if (fs.Read(h, 0, 12) < 12) return false;
                    return h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'A' && h[10] == 'V' && h[11] == 'E';
                }
            }
            catch (Exception) { return false; }
        }

        public static Pcm Read(string path, int maxSeconds)
        {
            using (BinaryReader r = new BinaryReader(new BufferedStream(File.OpenRead(path), 1 << 16)))
            {
                byte[] hdr = r.ReadBytes(12);
                if (hdr.Length < 12 || hdr[0] != 'R' || hdr[8] != 'W') throw new IOException("No es un WAV");
                int format = -1, channels = 0, rate = 0, bits = 0;
                while (true)
                {
                    byte[] idb = r.ReadBytes(4);
                    if (idb.Length < 4) throw new IOException("WAV sin datos");
                    string id = Encoding.ASCII.GetString(idb);
                    long size = r.ReadUInt32();
                    if (id == "fmt ")
                    {
                        byte[] fmt = r.ReadBytes((int)size);
                        format = BitConverter.ToUInt16(fmt, 0);
                        channels = BitConverter.ToUInt16(fmt, 2);
                        rate = BitConverter.ToInt32(fmt, 4);
                        bits = BitConverter.ToUInt16(fmt, 14);
                        if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(fmt, 24);
                        if ((size & 1) == 1) r.ReadByte();
                    }
                    else if (id == "data")
                    {
                        if (format < 0) throw new IOException("WAV sin cabecera fmt");
                        return ReadData(r, size, format, channels, rate, bits, maxSeconds);
                    }
                    else
                    {
                        long skip = size + (size & 1);
                        r.BaseStream.Seek(skip, SeekOrigin.Current);
                    }
                }
            }
        }

        private static Pcm ReadData(BinaryReader r, long size, int format, int channels, int rate, int bits, int maxSeconds)
        {
            if (channels <= 0 || rate <= 0) throw new IOException("WAV no válido");
            bool isFloat = format == 3;
            if (!(format == 1 || isFloat)) throw new IOException("Formato WAV no soportado (" + format + ")");
            int bps = bits / 8;
            if (bps < 1 || bps > 4 || (isFloat && bps != 4)) throw new IOException("Bits WAV no soportados");
            int frameBytes = bps * channels;
            long frames = (size == 0 || size == 0xFFFFFFFFL) ? long.MaxValue : size / frameBytes;
            long maxFrames = (long)maxSeconds * rate;
            bool truncated = frames != long.MaxValue && frames > maxFrames;
            if (frames > maxFrames) frames = maxFrames;
            float[] o = new float[(int)Math.Min(frames, 1 << 20) + 1024];
            int count = 0;
            byte[] buf = new byte[frameBytes * 4096];
            long remaining = frames;
            while (remaining > 0)
            {
                int want = (int)Math.Min(remaining, 4096);
                int got = ReadUpTo(r.BaseStream, buf, want * frameBytes);
                int gotFrames = got / frameBytes;
                if (gotFrames <= 0) break;
                if (count + gotFrames > o.Length)
                {
                    float[] n2 = new float[Math.Max(o.Length * 2, count + gotFrames)];
                    Array.Copy(o, n2, count);
                    o = n2;
                }
                for (int f = 0; f < gotFrames; f++)
                {
                    double sum = 0;
                    int b0 = f * frameBytes;
                    for (int c = 0; c < channels; c++)
                    {
                        int k = b0 + c * bps;
                        double v;
                        if (isFloat) v = BitConverter.ToSingle(buf, k);
                        else if (bps == 1) v = (buf[k] - 128) / 128.0;
                        else if (bps == 2) v = BitConverter.ToInt16(buf, k) / 32768.0;
                        else if (bps == 3) v = ((buf[k] | (buf[k + 1] << 8) | ((sbyte)buf[k + 2] << 16))) / 8388608.0;
                        else v = BitConverter.ToInt32(buf, k) / 2147483648.0;
                        sum += v;
                    }
                    double m = sum / channels;
                    if (m > 1) m = 1;
                    if (m < -1) m = -1;
                    o[count++] = (float)m;
                }
                remaining -= gotFrames;
                if (got < want * frameBytes) break;
            }
            if (count >= maxFrames) truncated = true;
            return new Pcm { Samples = o, Length = count, SampleRate = rate, Truncated = truncated };
        }

        private static int ReadUpTo(Stream s, byte[] b, int len)
        {
            int r = 0;
            while (r < len)
            {
                int k = s.Read(b, r, len - r);
                if (k <= 0) break;
                r += k;
            }
            return r;
        }

        /** WAV mono de 16 bits en memoria. */
        public static byte[] Build(float[] data, int rate)
        {
            int n = data.Length;
            byte[] w = new byte[44 + n * 2];
            WriteAscii(w, 0, "RIFF");
            WriteInt(w, 4, 36 + n * 2);
            WriteAscii(w, 8, "WAVE");
            WriteAscii(w, 12, "fmt ");
            WriteInt(w, 16, 16);
            WriteShort(w, 20, 1);
            WriteShort(w, 22, 1);
            WriteInt(w, 24, rate);
            WriteInt(w, 28, rate * 2);
            WriteShort(w, 32, 2);
            WriteShort(w, 34, 16);
            WriteAscii(w, 36, "data");
            WriteInt(w, 40, n * 2);
            int p = 44;
            for (int i = 0; i < n; i++)
            {
                float v = data[i];
                if (v > 1f) v = 1f;
                if (v < -1f) v = -1f;
                short s = (short)Math.Round(v * 32767f);
                w[p++] = (byte)s;
                w[p++] = (byte)(s >> 8);
            }
            return w;
        }

        private static void WriteAscii(byte[] b, int o, string s) { for (int i = 0; i < 4; i++) b[o + i] = (byte)s[i]; }

        private static void WriteShort(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }

        private static void WriteInt(byte[] b, int o, int v)
        {
            b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
        }
    }
}
